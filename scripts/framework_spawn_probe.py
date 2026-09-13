"""Operator-run bounded native Egg spawn/delete/input-replay mechanism probe.

Importing this module makes no connection. Each advancing leg is capped at 300
frames, including two release frames. Authoring rollback must restore the fixed
baseline before another leg runs. Native IDs and retired record history remain
in raw receipts; a separately named projection compares live logical paths.
Dynamic-target recreation is deliberately NOT qualified by this first probe.
"""
from __future__ import annotations

import argparse
import copy
from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import time

from compare_framework_frames import first_difference
from framework_kitchen_planner import Observation, PlanningError, EGG, tree_facts, finite
from framework_native_search import gameplay_round, native_clock_state, require_native_boundary, require_recorded_completion
from framework_pause_boundary import PauseBoundaryError, native_physics, observe_settled_pause
from framework_plate_search import AdvancingTrace, digest, input_report, to_raw_request
from framework_rpc import Client, ControllerClient


PROJECTION = (
    "live-native-logical-path-v2: live mapped entities replace top-level id with exact path; "
    "retired controller history remains raw only. Historical registry rows may be excluded only "
    "for IDs pinned by prior verified native root/container deletion receipts and absent from "
    "live entities/food/physics. Dynamic registry metadata excludes birth Pos "
    "and UnityInstanceId; fixed registry metadata is exact. Native body entityId maps to exact "
    "path, or physical-container plus its unique dynamic path established by a shared native "
    "body or a same-epoch/frame read-only PhysicalAttachment.m_container receipt; only dynamic "
    "bodyInstanceId is removed. All other food/body/entity fields remain exact. Round projection "
    "removes authoringWarpCount and recipe history beyond nextIndex; private clocks remain exact."
)


def unique(rows, key):
    result = {}
    for row in rows:
        value = row.get(key)
        if type(value) is not int or value in result:
            raise PlanningError("Missing/duplicate observed identity: " + key)
        result[value] = row
    return result


def ownership_context(receipt, frame):
    """The bridge connection epoch and native level boundary are observations, not counters we invent."""
    require_native_boundary(receipt)
    b = receipt["bridge"]; c = b["nativeCheckpoints"]; exchange = b.get("inputExchange") or {}
    epoch = exchange.get("connectionEpoch"); ready = b.get("readyUnityFrame")
    if receipt.get("ok") is not True or type(epoch) is not int or epoch < 1 \
            or exchange.get("controllerConnected") is not True or type(ready) is not int or ready < 0 \
            or type(frame) is not int or c.get("lastFrame") != frame or b.get("loading") is not False:
        raise PlanningError("Ownership read lacks its exact native connection epoch/frame/level boundary")
    return {"expectedEpoch": epoch, "expectedFrame": frame, "readyUnityFrame": ready,
            "restoreAttempts": c["restoreAttempts"], "nativeClocks": native_clock_state(b),
            "nativeRound": b["nativeRound"]}


def ownership_request(entity):
    return {"command": "hot-call", "slot": "inspection", "operation": "inspect", "args": {
        "entityId": entity, "component": "PhysicalAttachment",
        "members": [{"kind": "field", "name": "m_container"}]}}


def require_ownership_host(status, frame):
    if status.get("state") != "Paused" or status.get("frame") != frame or status.get("requestPending") \
            or status.get("errors") or status.get("traceFailure") or status.get("invalidStateReason"):
        raise PlanningError("Ownership read moved outside the expected paused host frame")


def inspection_body(response, entity):
    """Read the actual native field; never use names, birth poses or adjacent numeric IDs."""
    if response.get("ok") is not True:
        raise PlanningError("Native attachment inspection failed")
    detail = response.get("detail") or {}; module = detail.get("module") or {}; r = detail.get("result") or {}
    if module.get("slot") != "inspection" or module.get("name") != "registered-component-inspection-v1" \
            or module.get("apiVersion") != 1 or r.get("operation") != "inspect" \
            or r.get("entityId") != entity or r.get("ok") is not True \
            or r.get("mutationAttempted") is not False or r.get("rollbackPerformed") is not False \
            or first_difference(r.get("before"), r.get("after")) is not None:
        raise PlanningError("Attachment ownership requires an unchanged read-only native inspection")
    before = r.get("before") or {}; oid = r.get("objectInstanceId"); cid = r.get("componentInstanceId")
    components = before.get("components") or []; values = before.get("values") or []
    if type(oid) is not int or oid == 0 or type(cid) is not int or cid == 0 \
            or before.get("entityId") != entity or before.get("objectInstanceId") != oid \
            or len([c for c in components if c.get("type") == "PhysicalAttachment" and c.get("instanceId") == cid]) != 1 \
            or len([c for c in components if c.get("type") == "PhysicalAttachment"]) != 1 or len(values) != 1:
        raise PlanningError("Native PhysicalAttachment object/component incarnation is missing or ambiguous")
    v = values[0]; body = v.get("value") or {}
    if v.get("kind") != "field" or v.get("name") != "m_container" or v.get("declaringType") != "PhysicalAttachment" \
            or v.get("type") != "UnityEngine.Rigidbody" or v.get("readable") is not True \
            or body.get("type") != "UnityEngine.Rigidbody" or type(body.get("instanceId")) is not int or body["instanceId"] == 0:
        raise PlanningError("Native PhysicalAttachment.m_container is not an observed live Rigidbody")
    return body["instanceId"]


def physical_container_owners(state, receipt, entities, registry, bodies):
    dynamic = {i for i, e in entities.items() if len(e["path"]) > 1}
    owner_bodies = {i: bodies[i]["bodyInstanceId"] for i in dynamic if i in bodies}
    sidecar = receipt.get("physicalAttachmentOwnership")
    if sidecar is not None:
        context = ownership_context(receipt, state["frame"])
        if sidecar.get("version") != 1 or sidecar.get("stateSha256") != digest(state) \
                or first_difference(sidecar.get("context"), context) is not None:
            raise PlanningError("Stale ownership receipt does not match the exact state/epoch/frame")
        require_ownership_host(sidecar["hostBefore"], state["frame"])
        require_ownership_host(sidecar["hostAfter"], state["frame"])
        reads = sidecar.get("reads") or []
        if not 1 <= len(reads) <= 1:
            raise PlanningError("This probe permits at most one actual Egg ownership read")
        seen = set()
        for read in reads:
            entity = read.get("entityId")
            if entity not in dynamic or entity in seen or read.get("path") != entities[entity]["path"] \
                    or read.get("request") != ownership_request(entity) \
                    or "PhysicalAttachment" not in registry[entity].get("Components", []):
                raise PlanningError("Ownership receipt addresses an unknown/duplicate native dynamic root")
            response = read["response"]; b = response["bridge"]
            if not b.get("inputBlocked") or not b.get("holdPause") \
                    or first_difference(ownership_context(response, state["frame"]), context) is not None:
                raise PlanningError("Ownership inspection changed the native epoch/frame or lost its fence")
            body = inspection_body(response, entity)
            observed = unique(native_physics(response)["bodies"], "entityId")
            candidates = [i for i, row in bodies.items() if i not in entities and row["bodyInstanceId"] == body]
            if len(candidates) != 1 or observed.get(candidates[0], {}).get("bodyInstanceId") != body:
                raise PlanningError("Inspected native container lacks the same uniquely observed live physics identity")
            if entity in owner_bodies and owner_bodies[entity] != body:
                raise PlanningError("Native shared-body and PhysicalAttachment ownership disagree")
            owner_bodies[entity] = body; seen.add(entity)
    owners = {}
    for i, row in bodies.items():
        if i in entities: continue
        meta = registry.get(i) or {}
        matches = [d for d, body in owner_bodies.items() if body == row["bodyInstanceId"]]
        if len(matches) != 1 or not {"Rigidbody", "ObjectContainer"}.issubset(meta.get("Components", [])) \
                or 47 not in meta.get("SyncEntityTypes", []):
            raise PlanningError("Unmapped native body has no unique actual dynamic physical-container ownership")
        if matches[0] in owners.values():
            raise PlanningError("One native dynamic root has multiple registered physical containers")
        owners[i] = matches[0]
    if sidecar and any(i not in owners.values() for i in seen):
        raise PlanningError("Observed native m_container has no unique registered ObjectContainer body")
    return owners


def collect_ownership(state, receipt, call, label):
    """One existing owner connection; fence, inspect only, then re-arm without advancing time."""
    entities = unique([e for e in state["entities"] if e.get("exists")], "id")
    bodies = unique(native_physics(receipt)["bodies"], "entityId")
    missing = [i for i, e in entities.items() if len(e["path"]) > 1 and i not in bodies]
    if not missing: return receipt
    if len(missing) != 1 or entities[missing[0]]["path"] != [65, 0] or entities[missing[0]].get("className") != "egg":
        raise PlanningError("Read-only ownership request is outside the single actual Egg profile")
    context = ownership_context(receipt, state["frame"])
    before = call("controller", {"command": "status"}, label + "-ownership-before")
    require_ownership_host(before, state["frame"])
    fence = call("bridge", {"command": "pause"}, label + "-ownership-fence")
    if first_difference(ownership_context(fence, state["frame"]), context) is not None:
        raise PlanningError("Native epoch/frame changed before ownership inspection")
    entity = missing[0]; request = ownership_request(entity)
    response = call("bridge", request, label + "-ownership-read")
    after = call("controller", {"command": "status"}, label + "-ownership-after")
    result = copy.deepcopy(receipt)
    result["physicalAttachmentOwnership"] = {"version": 1, "context": context, "stateSha256": digest(state),
        "hostBefore": before, "hostAfter": after, "reads": [{"entityId": entity, "path": entities[entity]["path"],
            "request": request, "response": response}]}
    physical_container_owners(state, result, entities, unique(state["registry"], "EntityId"), bodies)
    armed = call("bridge", {"command": "arm"}, label + "-ownership-arm")
    if first_difference(ownership_context(armed, state["frame"]), context) is not None:
        raise PlanningError("Native epoch/frame changed while releasing ownership read fence")
    return result


def artifact_name(output, kind, frame, suffix):
    # Native checkpoint/export writers retain CreateNew. A distinct probe output
    # directory gets a distinct namespace; an actual collision still fails closed.
    run = hashlib.sha256(str(output.resolve()).encode("utf-8")).hexdigest()[:16]
    return f"spawn-{run}-{kind}-{frame}.{suffix}"


@dataclass
class SpawnObservation(Observation):
    retired_ids: tuple = ()

    def __post_init__(self):
        # The generic scheduler requires layoutValid for a fixed-only scene.
        # Its raw report also flags every historical spawned collider. Preserve
        # that report unchanged and independently admit ONLY this probe's exact
        # Egg paths, body-linked containers and already proven deletion IDs.
        s, n = self.snapshot, self.native_round
        if s.get("state") != "Paused" or s.get("requestPending") or s.get("errors"):
            raise PlanningError("Spawn observer requires an error-free settled pause")
        if not n.get("available") or n.get("gameState", "InLevel") != "InLevel":
            raise PlanningError("An active native round is required")
        finite(n.get("elapsed"), "native elapsed")
        if type(s.get("frame")) is not int or s["frame"] < 0:
            raise PlanningError("Invalid observed gameplay frame")
        rows = [e for e in s.get("entities", []) if e.get("exists")]
        self.entities = unique(rows, "id")
        self.paths = {tuple(e["path"]): e["id"] for e in rows}
        if len(self.paths) != len(rows): raise PlanningError("Duplicate live logical path")
        self.registry = unique(s.get("registry", []), "EntityId")
        self.initial = unique(s.get("initialRegistry", []), "EntityId")
        self.foods = unique(self.food["detail"]["entities"], "id")
        bodies = unique(native_physics(self.food)["bodies"], "entityId")
        fixed = {i for i, e in self.entities.items() if e["path"] == [i]}
        dynamic = set(self.entities) - fixed
        allowed = set(self.retired_ids)
        for i in dynamic:
            e = self.entities[i]; path = e["path"]; meta = self.registry.get(i) or {}
            if len(path) != 2 or path[0] != 65 or type(path[1]) is not int or path[1] < 0 \
                    or e["className"] != "egg" or meta.get("Name") not in {"Egg", "Egg(Clone)"}:
                raise PlanningError("Spawn metadata is outside the bounded actual Egg-crate profile")
            allowed.add(i)
        allowed.update(physical_container_owners(s, self.food, self.entities, self.registry, bodies))
        audit = s.get("registryValidation") or {}
        errors = audit.get("errors")
        if not s.get("freshLevelLoadObserved") or not isinstance(errors, list) or audit.get("semanticGaps") \
                or audit.get("expectedFixedEntities") != 122 or audit.get("checkedFixedEntities") != 122 or len(fixed) != 122:
            raise PlanningError("Actual complete Carnival fixed-layout validation is required")
        for error in errors:
            if error.get("id") not in allowed or error.get("reason") != "Unmodeled registered object has a physical collider; layout needs review.":
                raise PlanningError("Native fixed-layout or unrecognized dynamic metadata error: " + str(error))
        if not errors and audit.get("layoutValid") is not True:
            raise PlanningError("Native layout refusal has no independently verified dynamic cause")
        if any(tree_facts(row["composition"])[3] for i, row in self.foods.items() if i in self.entities and row.get("composition") is not None):
            raise PlanningError("Native ruined food observed")


def observation(state, receipt, retired_ids=()):
    if state.get("invalidStateReason") or state.get("traceFailure") or (state.get("trace") or {}).get("failed"):
        raise PlanningError("Native state/trace failure prevents a qualified observation")
    require_native_boundary(receipt)
    native_clock_state(receipt["bridge"])
    return SpawnObservation(state, receipt["bridge"]["nativeRound"], receipt, tuple(retired_ids))


def prepare_case(state, receipt, maximum_frames=300):
    if type(maximum_frames) is not int or not 60 <= maximum_frames <= 300:
        raise PlanningError("Use a total per-leg frame bound between 60 and 300")
    o = observation(state, receipt)
    bridge = receipt["bridge"]
    session = bridge.get("session") or {}
    expected = {"stage": "kitchen_ready", "scene": "s_Day_3_4", "variantScene": "s_Day_3_4",
                "dlc": 8, "variantPlayers": 4, "serverUsers": 4, "clientUsers": 4, "virtualPads": 4,
                "serverStates": "InLevel,InLevel,InLevel,InLevel", "clientStates": "InLevel,InLevel,InLevel,InLevel"}
    if any(type(session.get(k)) is not type(v) or session[k] != v for k, v in expected.items()):
        raise PlanningError("Actual native Carnival 3-4 DLC8/four-local-player session is required")
    caps = state.get("nativeWarpCapabilities") or {}
    if caps.get("Version") != 1 or type(caps.get("Features")) is not int or not caps["Features"] & 1:
        raise PlanningError("Observed native dynamic-warp capability is missing")
    chefs = sorted(i for i, e in o.entities.items() if e.get("chef") is not None)
    local = bridge.get("chefs") or []
    if len(chefs) != 4 or len(local) != 4 or {r.get("entity") for r in local} != set(chefs) \
            or {r.get("player") for r in local} != set(range(4)) or any(r.get("local") is not True for r in local):
        raise PlanningError("Four native local chef identities are not observed")
    if any(len(e["path"]) != 1 or e["path"] != [i] for i, e in o.entities.items()) \
            or any(o.held(c) is not None for c in chefs):
        raise PlanningError("Probe requires a fixed-only, empty-handed native baseline")
    if any((o.facts(i) or ({},))[0] for i in o.foods if i in o.entities
           and o.entity(i)["className"] != "condiment-dispenser"):
        raise PlanningError("Initial native food must be empty; no unrelated cooking experiment may be active")
    if o.crate(EGG) != 65 or o.entity(65)["path"] != [65] \
            or o.registry[65].get("SpawnNames") != ["Egg"] \
            or not {"ServerPickupItemSpawner", "SpawnableEntityCollection"}.issubset(o.registry[65].get("Components", [])):
        raise PlanningError("Crate65 does not have the exact observed native Egg spawn profile")
    if not any(r.get("player") == 1 and r.get("entity") == 104 for r in local) \
            or o.entity(104)["className"] != "chef-red" or not o.accessible(104, 65):
        raise PlanningError("Native player1/chef104 is not the accessible UR supplier")
    if any(abs(o.entity(65)["position"][k] - v) > .04 for k, v in {"x": 30, "z": -10.8}.items()):
        raise PlanningError("Actual Egg crate pose does not match the validated UR role")
    for e in o.entities.values():
        if e["className"] == "cannon":
            aux = e.get("nativeCannon") or {}
            if not aux.get("observedAux") or not aux.get("settledForWarp") or aux.get("flying") or aux.get("activeLaunches"):
                raise PlanningError("Cannons must have actual inactive checkpoint observations")
    native_physics(receipt, chefs)
    resources = [65]
    actions = [
        {"id": "egg-approach", "chef": 104, "type": "goto", "x": 30, "z": -12,
         "resources": resources, "timeoutFrames": maximum_frames - 20, "dash": False},
        {"id": "egg-settle", "chef": 104, "type": "wait", "frames": 6, "after": ["egg-approach"],
         "resources": resources, "timeoutFrames": 15},
        {"id": "egg-pickup", "chef": 104, "type": "pickup", "target": 65, "expectSpawn": True,
         "after": ["egg-settle"], "resources": resources, "timeoutFrames": maximum_frames - 20, "dash": False}]
    return {"version": 1, "classification": "authoring dynamic deletion and fixed-input replay; recreation untested",
            "frame": o.frame, "maximumFramesIncludingRelease": maximum_frames, "chefs": chefs,
            "chef": 104, "crate": 65, "ingredient": EGG, "expectedNativeSpawnNames": ["Egg"],
            "fixedIds": sorted(o.entities), "registryIds": sorted(o.registry),
            "fixedTokens": {str(i): o.token(i) for i in o.entities},
            "fixedParents": {str(i): o.parent(i) for i in o.entities},
            "fixedNativeFood": {str(i): copy.deepcopy(row) for i, row in o.foods.items()},
            "ledger": copy.deepcopy(o.native_round["ledger"]),
            "request": {"command": "actions", "maximumFrames": maximum_frames - 2, "actions": actions}}


def logical_projection(state, receipt, retired_ids=()):
    """Never infer proxy identity from numeric adjacency or an entity's name."""
    o = observation(state, receipt, retired_ids)
    registry = unique(state["registry"], "EntityId")
    food = unique(receipt["detail"]["entities"], "id")
    physics = native_physics(receipt, sorted(i for i, e in o.entities.items() if e.get("chef") is not None))
    bodies = unique(physics["bodies"], "entityId")
    retired = set(retired_ids)
    if retired & (set(o.entities) | set(food) | set(bodies)):
        raise PlanningError("Previously proven retired native ID unexpectedly remains live or was reused")
    registry = {i: r for i, r in registry.items() if i not in retired}
    paths = {i: tuple(e["path"]) for i, e in o.entities.items()}
    if len(set(paths.values())) != len(paths):
        raise PlanningError("Duplicate live logical entity path")
    dynamic = {i for i, p in paths.items() if len(p) > 1}
    proxy_roots = physical_container_owners(state, receipt, o.entities, registry, bodies)
    for i, owner in proxy_roots.items():
        paths[i] = ("physical-container",) + paths[owner]
    if set(registry) - set(paths):
        raise PlanningError("Unexpected unmapped dynamic native registration")
    def key(i):
        if i not in paths:
            raise PlanningError("Native food/body references an unknown live identity")
        return json.dumps(paths[i], separators=(",", ":"))
    entities = {}
    for i, e in o.entities.items():
        value = copy.deepcopy(e); value.pop("id")
        entities[key(i)] = value
    meta = {}
    for i, r in registry.items():
        value = copy.deepcopy(r); value.pop("EntityId")
        if i in dynamic or i in proxy_roots:
            value.pop("UnityInstanceId", None); value.pop("Pos", None)
        meta[key(i)] = value
    food_rows = {}
    for i, row in food.items():
        value = copy.deepcopy(row); value.pop("id")
        food_rows[key(i)] = value
    physical = {}
    for i, row in bodies.items():
        value = copy.deepcopy(row); value.pop("entityId")
        if i in dynamic or i in proxy_roots:
            value.pop("bodyInstanceId")
        physical[key(i)] = value
    if len(physical) != len(bodies):
        raise PlanningError("Logical native body projection collided")
    return {"frame": o.frame, "entities": entities, "registry": meta, "food": food_rows,
            "nativePhysics": {**{k: v for k, v in physics.items() if k != "bodies"}, "bodies": physical},
            "nativeRound": gameplay_round(o.native_round), "nativeClocks": native_clock_state(receipt["bridge"])}, \
           {"nativeIdsByPath": {key(i): i for i in paths}, "dynamicRootIds": sorted(dynamic),
            "physicalContainers": {str(i): owner for i, owner in proxy_roots.items()},
            "retiredRegistryIdsExcludedByPriorNativeReceipts": sorted(retired)}


def compare_logical(expected_state, expected_receipt, state, receipt, expected_retired=(), actual_retired=()):
    a, aid = logical_projection(expected_state, expected_receipt, expected_retired)
    b, bid = logical_projection(state, receipt, actual_retired)
    difference = first_difference(a, b, "$logicalNativeBoundary")
    raw = {"entities": first_difference(expected_state["entities"], state["entities"], "$rawEntities"),
           "registry": first_difference(expected_state["registry"], state["registry"], "$rawRegistry"),
           "food": first_difference(expected_receipt["detail"]["entities"], receipt["detail"]["entities"], "$rawFood"),
           "physics": first_difference(expected_receipt["bridge"]["nativePhysics"], receipt["bridge"]["nativePhysics"], "$rawPhysics")}
    return {"passed": difference is None, "projection": PROJECTION, "firstDifference": difference,
            "expectedProjectionSha256": digest(a), "actualProjectionSha256": digest(b),
            "expectedIdentities": aid, "actualIdentities": bid,
            "rawEqual": all(d is None for d in raw.values()), "rawFirstDifferences": raw}


def held_egg(case, state, receipt, retired_ids=()):
    o = observation(state, receipt, retired_ids)
    for i, token in case["fixedTokens"].items():
        if o.token(int(i)) != token:
            raise PlanningError("Initial fixed entity incarnation changed")
    for i, parent in case["fixedParents"].items():
        if o.parent(int(i)) != parent:
            raise PlanningError("Unrelated initial attachment changed during Egg pickup")
    if first_difference(o.native_round["ledger"], case["ledger"]):
        raise PlanningError("Egg mechanism changed native score or delivery ledger")
    if any(first_difference(row, o.foods.get(int(i))) is not None for i, row in case["fixedNativeFood"].items()):
        raise PlanningError("Egg pickup changed unrelated native food or cooking/mixing progress")
    egg = o.held(case["chef"])
    if egg is None or egg in case["fixedIds"] or o.entity(egg)["className"] != "egg":
        raise PlanningError("Actual supplier is not holding a newly spawned raw Egg")
    path = o.entity(egg)["path"]
    if len(path) != 2 or path[0] != case["crate"] or type(path[1]) is not int or path[1] < 0:
        raise PlanningError("Held Egg has no exact observed child path of native Egg crate")
    if (o.entity(case["chef"]).get("data", {}).get("attachment") or {}).get("path") != path:
        raise PlanningError("Native chef and raw Egg attachment directions disagree")
    facts = o.facts(egg)
    if not facts or dict(facts[0]) != {EGG: 1} or facts[1] or facts[2] or facts[3]:
        raise PlanningError("Actual held composition is not exactly one uncooked unmixed Egg")
    if any(o.held(c) is not None for c in case["chefs"] if c != case["chef"]):
        raise PlanningError("Another chef caught an unexpected object")
    if set(o.entities) - set(case["fixedIds"]) != {egg}:
        raise PlanningError("Pickup produced unexpected additional live mapped entities")
    _, identities = logical_projection(state, receipt, retired_ids)
    proxies = [int(i) for i, owner in identities["physicalContainers"].items() if owner == egg]
    if len(proxies) != 1 or set(o.registry) - set(case["registryIds"]) - set(retired_ids) != {egg, proxies[0]}:
        raise PlanningError("Spawn requires exactly one observed Egg root and its native physical container")
    frames = state["frame"] - case["frame"]
    if not 0 < frames <= case["maximumFramesIncludingRelease"]:
        raise PlanningError("Native spawn exceeded its finite total frame bound")
    return {"achieved": True, "egg": egg, "container": proxies[0], "path": path,
            "framesIncludingRelease": frames, "nativeComposition": copy.deepcopy(o.foods[egg]["composition"])}


def require_deletion(goal, state, receipt):
    audit = receipt["bridge"]["nativeCheckpoints"].get("nativeDynamicWarp") or {}
    if audit.get("verified") is not True:
        raise PlanningError("Native dynamic deletion lacks a verified transaction receipt")
    matches = [r for r in audit.get("deleted", []) if r.get("id") == goal["egg"]]
    if len(matches) != 1 or matches[0].get("containerId") != goal["container"] or matches[0].get("containerRemoved") is not True:
        raise PlanningError("Native receipt does not prove removal of both exact Egg and physical container")
    live = {e["id"] for e in state["entities"] if e.get("exists")}
    bodies = {r["entityId"] for r in native_physics(receipt)["bodies"]}
    food = set(unique(receipt["detail"]["entities"], "id"))
    if {goal["egg"], goal["container"]} & (live | food | bodies):
        raise PlanningError("Deleted native Egg or physical container remains live")
    return {"passed": True, "exactDeletion": matches[0], "nativeTransaction": audit}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--trace", type=Path, required=True)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--maximum-frames", type=int, default=300)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    evidence = []
    summary = {"passed": False, "scope": "native Egg spawn, baseline deletion, exact-input repetition and recording replay",
               "dynamicTargetRecreation": "not exercised; requires a separately validated consumption route",
               "comparisonScope": "settled native endpoints and every accepted input, not every intermediate native state",
               "projection": PROJECTION}
    bridge = host = None
    cursors = []
    began = time.monotonic()
    def save():
        (args.out / "observations.json").write_text(json.dumps(evidence, indent=2), encoding="utf-8")
        (args.out / "summary.json").write_text(json.dumps(summary, indent=2), encoding="utf-8")
    def call(target, request, label):
        row = {"label": label, "target": target, "request": request, "wallSeconds": time.monotonic() - began}
        evidence.append(row)
        try:
            row["response"] = (bridge if target == "bridge" else host).call(request)
            return row["response"]
        except Exception as error:
            row["error"] = str(error)
            raise
    def settled(label):
        deadline = time.monotonic() + 90
        while True:
            state = host.status()
            if state.get("errors") or state.get("traceFailure") or state.get("state") == "Error":
                evidence.append({"label": label + "-failed-status", "response": state})
                raise PlanningError("Native action/trace failed: " + json.dumps(state))
            if state.get("state") == "Paused" and not state.get("requestPending"):
                return call("controller", {"command": "inspect", "full": True}, label)
            if time.monotonic() > deadline:
                raise TimeoutError("Native leg failed to reach a bounded settled pause")
            time.sleep(.03)
    def native(label, state):
        frame = state["frame"]
        def status_frame():
            s = call("controller", {"command": "status"}, label + "-frame")
            if s.get("state") != "Paused" or s.get("requestPending") or s.get("errors") or s.get("traceFailure"):
                raise PlanningError("Native pause lost its settled host observation")
            return s.get("frame")
        def food():
            r = call("bridge", {"command": "food"}, label + "-raw")
            require_native_boundary(r); native_clock_state(r["bridge"])
            return r
        try:
            result = observe_settled_pause(food, status_frame, expected_frame=frame)
        except PauseBoundaryError as error:
            (args.out / (label + "-pause.json")).write_text(json.dumps(error.report, indent=2), encoding="utf-8")
            raise
        (args.out / (label + "-pause.json")).write_text(json.dumps(result["proof"], indent=2), encoding="utf-8")
        receipt = collect_ownership(state, result["receipt"], call, label)
        evidence.append({"label": label, "response": receipt})
        return receipt
    def inputs(cursor, start, end, chefs, label):
        deadline = time.monotonic() + 3
        while True:
            try:
                rows = cursor.frames(start, end, chefs)
                (args.out / (label + "-inputs.json")).write_text(json.dumps(rows, separators=(",", ":")), encoding="utf-8")
                return rows
            except PlanningError as error:
                if "does not yet contain" not in str(error) or time.monotonic() > deadline:
                    raise
                time.sleep(.02)
    def trace_cursor(label):
        value = AdvancingTrace(args.trace)
        stamp = args.trace.stat()
        cursors.append((label, value, value.offset, (stamp.st_dev, stamp.st_ino)))
        return value
    try:
        summary["sources"] = {str(p.resolve()): hashlib.sha256(p.read_bytes()).hexdigest() for p in
                              [Path(__file__).with_name(name) for name in
                               ("framework_spawn_probe.py", "framework_plate_search.py", "framework_pause_boundary.py",
                                "framework_native_search.py", "framework_kitchen_planner.py", "framework_search.py",
                                "framework_rpc.py", "compare_framework_frames.py")]}
        summary["tracePath"] = str(args.trace.resolve())
        bridge = Client(args.bridge_port); host = ControllerClient(args.controller_port)
        initial = settled("initial")
        call("bridge", {"command": "arm"}, "arm")
        call("controller", {"command": "step", "frames": 30}, "warmup")
        base = settled("base"); base_native = native("base-native", base)
        if base["frame"] != initial["frame"] + 30:
            raise PlanningError("Warmup did not observe exactly thirty native advancing frames")
        case = prepare_case(base, base_native, args.maximum_frames)
        summary["case"] = case
        logical_projection(base, base_native)
        checkpoint_name = artifact_name(args.out, "base", base["frame"], "pb")
        recording_name = artifact_name(args.out, "input", base["frame"], "json")
        summary["artifactNames"] = {"checkpoint": checkpoint_name, "recording": recording_name,
            "scope": "output-directory SHA256 prefix namespaces native CreateNew paths; existing files are never overwritten"}
        call("controller", {"command": "checkpoint", "path": checkpoint_name}, "checkpoint")
        retired = set()
        def restore(label, goal):
            before = call("bridge", {"command": "status"}, label + "-before")["bridge"]["nativeCheckpoints"]["restoreAttempts"]
            call("controller", {"command": "warp", "frame": base["frame"], "development": True}, label + "-warp")
            s = settled(label + "-state"); r = native(label + "-native", s)
            restored = r["bridge"]["nativeCheckpoints"].get("lastRestore") or {}
            if restored.get("verified") is not True or restored.get("frame") != base["frame"] or restored.get("attempt", -1) <= before:
                raise PlanningError("No new verified baseline warp acknowledgment")
            deletion = require_deletion(goal, s, r)
            retired.update((goal["egg"], goal["container"]))
            comparison = compare_logical(base, base_native, s, r, actual_retired=retired)
            summary[label] = {"deletion": deletion, "comparison": comparison}; save()
            if not comparison["passed"]:
                raise PlanningError("Fixed baseline differs after native dynamic deletion")
            if (s.get("typedActions") or {}).get("outcome") not in {None, "none", "cleared"}:
                call("controller", {"command": "actions-clear"}, label + "-clear")
                settled(label + "-cleared")
        cursor = trace_cursor("original")
        call("controller", case["request"], "typed-spawn")
        original = settled("original"); original_native = native("original-native", original)
        original_goal = held_egg(case, original, original_native)
        if (original.get("typedActions") or {}).get("outcome") != "complete":
            raise PlanningError("Typed graph did not complete its observed native pickup")
        original_rows = inputs(cursor, base["frame"], original["frame"], case["chefs"], "original")
        raw_request = to_raw_request(original_rows, case["chefs"])
        summary["original"] = {"goal": original_goal, "inputs": input_report(original_rows)}; save()
        restore("first-deletion", original_goal)
        cursor = trace_cursor("fixed")
        call("controller", raw_request, "fixed-input-repeat")
        fixed = settled("fixed-input"); fixed_native = native("fixed-input-native", fixed)
        fixed_retired = set(retired)
        fixed_goal = held_egg(case, fixed, fixed_native, fixed_retired)
        fixed_rows = inputs(cursor, base["frame"], fixed["frame"], case["chefs"], "fixed")
        comparison = compare_logical(original, original_native, fixed, fixed_native, actual_retired=fixed_retired)
        comparison["inputFirstDifference"] = first_difference(original_rows, fixed_rows, "$acceptedInputs")
        summary["fixedInputComparison"] = comparison; save()
        if not comparison["passed"] or comparison["inputFirstDifference"]:
            raise PlanningError("Fixed-input repetition differs from original spawn")
        exported = call("controller", {"command": "record-input", "path": recording_name}, "record-input")
        recording = json.loads(Path(exported["path"]).read_text(encoding="utf-8-sig"))
        require_recorded_completion(fixed, recording, base["frame"])
        (args.out / "recording.json").write_text(json.dumps(recording, indent=2), encoding="utf-8")
        restore("second-deletion", fixed_goal)
        cursor = trace_cursor("replay")
        call("controller", {"command": "raw-replay", "recording": recording}, "raw-replay")
        replay = settled("replay"); replay_native = native("replay-native", replay)
        require_recorded_completion(replay, recording, base["frame"])
        replay_goal = held_egg(case, replay, replay_native, retired)
        replay_rows = inputs(cursor, base["frame"], replay["frame"], case["chefs"], "replay")
        final = compare_logical(fixed, fixed_native, replay, replay_native, fixed_retired, retired)
        final["inputFirstDifference"] = first_difference(fixed_rows, replay_rows, "$acceptedInputs")
        summary.update(replayComparison=final, replayGoal=replay_goal, recordingSha256=recording["sha256"],
                       passed=final["passed"] and final["inputFirstDifference"] is None)
    except Exception as error:
        summary["error"] = str(error)
    finally:
        try:
            if bridge is not None:
                call("bridge", {"command": "pause"}, "finally-neutral-pause")
        except Exception as error:
            summary.update(pauseError=str(error), passed=False)
        for connection in (bridge, host):
            if connection is not None:
                try: connection.close()
                except Exception as error: summary.update(closeError=str(error), passed=False)
        summary["traceWindows"] = []
        for label, cursor, start_offset, identity in cursors:
            try:
                # Finished cursors keep their original exact byte interval. Read
                # a failed final leg once to retain emitted inputs before abort.
                if label == cursors[-1][0] and not summary["passed"]:
                    cursor.read()
                    (args.out / (label + "-failed-input-exchanges.json")).write_text(json.dumps(cursor.rows), encoding="utf-8")
                stamp = args.trace.stat()
                if (stamp.st_dev, stamp.st_ino) != identity or stamp.st_size < cursor.offset:
                    raise PlanningError("Native source trace identity or retained byte interval changed")
                remaining = cursor.offset - start_offset
                if remaining > 128 * 1024 * 1024:
                    raise PlanningError("Probe trace window exceeded 128MiB evidence bound")
                sha = hashlib.sha256()
                with args.trace.open("rb") as stream:
                    stream.seek(start_offset)
                    while remaining:
                        data = stream.read(min(65536, remaining))
                        if not data: raise PlanningError("Native trace interval truncated during hashing")
                        sha.update(data); remaining -= len(data)
                summary["traceWindows"].append({"label": label, "source": str(args.trace.resolve()),
                    "startByteInclusive": start_offset, "endByteExclusive": cursor.offset,
                    "sha256": sha.hexdigest(), "classification": "exact retained byte window in operator-owned trace; full active file is not claimed closed"})
            except Exception as error:
                summary.update(traceEvidenceError=str(error), passed=False)
        summary["wallSeconds"] = time.monotonic() - began
        save()
        files = {str(p.resolve()): hashlib.sha256(p.read_bytes()).hexdigest() for p in args.out.iterdir() if p.is_file()}
        (args.out / "receipt-hashes.json").write_text(json.dumps(files, indent=2), encoding="utf-8")
        print(json.dumps(summary, indent=2))
    return 0 if summary["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
