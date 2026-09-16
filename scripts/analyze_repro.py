#!/usr/bin/env python3
"""Compare OC2 telemetry offline; stdlib only, streaming JSONL/gzip input.

Usage: python scripts/analyze_repro.py a.jsonl.gz b.jsonl.gz --out report.json
       Add --map-initial-rigidbodies to compare a second, explicitly canonical
       view. Raw native-ID and strict-state comparisons are always retained.

Reports the first *sampled* difference, never an unobserved intervening frame.
Event comparison is a named projection, not a claim of complete determinism.
"""

import argparse
import gzip
import hashlib
import json
import math
import pickle
import re
import struct
import sys
from collections import Counter
from pathlib import Path


OBSERVATIONS = {
    "frame", "fixedFrame", "gameplayFrame", "gameplayFixedFrame", "levelFrameZero",
    "levelFixedFrameZero", "introFrameZero", "physicsStepsThisFrame",
    "framesSinceNoPhysics", "levelStartPhysicsPhase", "levelClientTimeZero",
    "levelReady", "serverRoundActive", "clientRoundActive", "logicalTime",
    "lifecycle", "warnings", "applicationFocused", "recipeDraws", "currentRecipeDraws", "currentRecipeRoundInstanceOrdinal",
    "alignStartPhysics", "startAlignmentWaitFrames", "startAlignmentReleaseFrame",
    "gameEvents", "gameEventsInstalled", "gameEventsDropped", "gameEventsError",
}
TIMING = {
    "timer", "clientTime", "clientDeltaTime", "fixedDeltaTime", "timerSuppressed",
    "physicsStepsThisFrame", "framesSinceNoPhysics", "levelStartPhysicsPhase",
    "gameplayFixedFrame", "levelClientTimeZero",
}
EVENT_WORLD = {
    "scene", "gameState", "score", "baseScore", "tips", "multiplier", "combo",
    "delivered", "deductions", "inLevel", "paused", "timerSuppressed",
}
EVENT_CHEF = {
    "entityId", "playerId", "heldEntityId", "pickupTargetId", "useTargetId",
    "placementTargetId", "interactingEntityId", "aimingThrow", "inputSuppressed",
    "respawning", "canAcceptInput", "directlyControlled", "controlsEnabled",
    "useSuppressed", "clientPredictedInteractionId", "serverInteractionId",
}
EVENT_ENTITY = {
    "id", "name", "active", "attachedEntityId", "workStage", "workSubStage",
    "ingredientIds", "contents", "spawnPrefab", "switchIndex", "cookingTypeId",
    "mixingTypeId", "cannonLoadedEntityId", "plateCount", "cannonFlying",
    "cannonReady", "cannonState", "portalDestinationId", "portalTeleporting",
    "portalReceiving", "portalSenderCount", "portalReceiverCount",
    "cannonButtonEntityId", "controlTargetId",
    "plateStackEntityId", "plateStackKind", "washingOutputEntityId", "throwFlying", "throwerEntityId", "previousThrowerEntityId",
}
PHYSICS_ENTITY = {
    "id", "name", "position", "rotation", "velocity", "angularVelocity",
    "physicsHash", "hasRigidbody", "kinematic", "sleeping", "active", "layer",
    "colliders", "cannonAngle", "cannonTarget", "cannonAttachPoint",
    "cannonExitPoint", "portalTeleportPoint",
}
PHYSICS_CHEF = {
    "playerId", "entityId", "position", "velocity", "forward", "lastVelocity",
    "lastMoveInputDirection", "impactVelocity", "dashTimer", "impactTimer",
    "impactStartTime", "leftOverTime",
}
ENTITY_REFERENCES = {
    "entityId", "heldEntityId", "pickupTargetId", "useTargetId",
    "placementTargetId", "interactingEntityId", "attachedEntityId",
    "cannonLoadedEntityId", "portalDestinationId",
    "cannonButtonEntityId", "controlTargetId", "stationEntityId",
}
HASH_FIELDS = ["position.x", "position.y", "position.z", "rotation.x",
               "rotation.y", "rotation.z", "rotation.w", "velocity.x",
               "velocity.y", "velocity.z", "angularVelocity.x",
               "angularVelocity.y", "angularVelocity.z"]
MISSING = {"$missing": True}
# Internal equality acceleration only: no pickle data is read from disk or
# unpickled. Protocol 4 encodes a float as G followed by eight big-endian bytes.
# Exponent 0x7ff detects every NaN (and conservatively infinity). Matches inside
# strings are harmless false positives: they merely select the reference walk.
NONFINITE_PICKLE = re.compile(rb"G[\x7f\xff][\xf0-\xff]")


def select(value, keys):
    return {key: value[key] for key in sorted(keys) if key in value}


def compact(value, limit=800):
    text = json.dumps(value, ensure_ascii=False, sort_keys=True)
    return value if len(text) <= limit else {"preview": text[:limit], "characters": len(text)}


def first_diff(a, b, path="$", _equal_pairs=None):
    # Python's container equality is fast C code, but considers True == 1.
    # An identical finite type-preserving C encoding of these parsed JSON trees
    # additionally proves the original leaf type rules. Numeric 1/1.0 or dict
    # insertion-order differences fall back to the reference walk. NaN must not
    # use C container equality: the decoder can reuse a singleton NaN object.
    containers = isinstance(a, (dict, list)) and type(a) is type(b)
    pair = (id(a), id(b)) if containers and _equal_pairs is not None else None
    if pair is not None and pair in _equal_pairs:
        return None
    if containers and a == b:
        try:
            encoded = pickle.dumps(a, protocol=4)
            encoded_equal = not NONFINITE_PICKLE.search(encoded) and encoded == pickle.dumps(b, protocol=4)
        except (TypeError, ValueError, OverflowError, pickle.PicklingError):
            encoded_equal = False
        if encoded_equal:
            if pair is not None:
                # Retain identities until this aligned sample is finished, so
                # object-id reuse cannot turn this cache into a false positive.
                _equal_pairs[pair] = (a, b)
            return None
    if isinstance(a, dict) and isinstance(b, dict):
        for key in sorted(a.keys() | b.keys()):
            if key not in a or key not in b:
                return {"path": path + "." + key, "expected": compact(a.get(key, MISSING)),
                        "actual": compact(b.get(key, MISSING))}
            found = first_diff(a[key], b[key], path + "." + key, _equal_pairs)
            if found:
                return found
        return None
    if isinstance(a, list) and isinstance(b, list):
        if len(a) != len(b):
            return {"path": path + ".length", "expected": len(a), "actual": len(b)}
        for index, (left, right) in enumerate(zip(a, b)):
            found = first_diff(left, right, path + "." + str(index), _equal_pairs)
            if found:
                return found
        return None
    # int/float JSON spellings represent the same numeric value; booleans do not.
    numeric = isinstance(a, (int, float)) and not isinstance(a, bool) and isinstance(b, (int, float)) and not isinstance(b, bool)
    if a == b and (numeric or type(a) is type(b)):
        return None
    result = {"path": path, "expected": compact(a), "actual": compact(b)}
    if numeric and math.isfinite(a) and math.isfinite(b):
        result["deltaActualMinusExpected"] = b - a
    if path.endswith(".physicsHash") and isinstance(a, str) and isinstance(b, str):
        result["rawFloatDifference"] = hash_diff(a, b)
    return result


def hash_diff(a, b):
    if len(a) != 104 or len(b) != 104:
        return {"error": "Expected thirteen IEEE754 hexadecimal words"}
    try:
        for index, field in enumerate(HASH_FIELDS):
            left, right = a[index * 8:index * 8 + 8], b[index * 8:index * 8 + 8]
            if left != right:
                av = struct.unpack(">f", bytes.fromhex(left))[0]
                bv = struct.unpack(">f", bytes.fromhex(right))[0]
                return {"field": field, "expectedBits": left, "actualBits": right,
                        "expectedFloat": av, "actualFloat": bv, "delta": bv - av}
    except (ValueError, struct.error):
        return {"error": "Invalid IEEE754 hexadecimal word"}
    return None


def values_equal(a, b, equal_pairs=None):
    # After a view's first difference has been recorded, only its equal/different
    # sample count is needed. A C-level unequal result already proves a JSON
    # difference; avoid rediscovering the same late path thousands of times.
    # Equality still uses first_diff's stricter boolean and nonfinite rules.
    return a == b and first_diff(a, b, _equal_pairs=equal_pairs) is None


def by_id(values, key):
    result = {}
    for index, value in enumerate(values):
        identity = str(value.get(key, "missing:" + str(index)))
        if identity in result:
            identity += ":duplicate:" + str(index)
        result[identity] = value
    return result


def without_progress(value):
    if isinstance(value, list):
        result = None
        for index, item in enumerate(value):
            child = without_progress(item) if isinstance(item, (dict, list)) else item
            if child is not item and result is None:
                result = value[:index]
            if result is not None:
                result.append(child)
        return value if result is None else result
    if isinstance(value, dict):
        result = None
        for key, item in value.items():
            if key == "progress":
                if result is None:
                    result = value.copy()
                del result[key]
                continue
            child = without_progress(item) if isinstance(item, (dict, list)) else item
            if child is not item:
                if result is None:
                    result = value.copy()
                result[key] = child
        return value if result is None else result
    return value


def views(state):
    native = {key: value for key, value in state.items() if key not in OBSERVATIONS}
    native["entities"] = by_id([{key: value for key, value in e.items() if key != "observedOrdinal"}
                                 for e in state.get("entities", [])], "id")
    native["chefs"] = by_id(state.get("chefs", []), "playerId")
    native_except_clocks = {key: value for key, value in native.items() if key not in {"clientTime", "clientDeltaTime"}}
    events = select(state, EVENT_WORLD)
    events["chefs"] = by_id([select(c, EVENT_CHEF) for c in state.get("chefs", [])], "playerId")
    events["entities"] = by_id([without_progress(select(e, EVENT_ENTITY)) for e in state.get("entities", [])], "id")
    events["orders"] = [without_progress({key: value for key, value in o.items() if key != "remaining"})
                        for o in state.get("orders", [])]
    current_draws = state.get("currentRecipeDraws", state.get("recipeDraws", []))
    events["recipeDraws"] = [{**select(draw, {"generator", "recipeIds", "frequencies"}),
                              "roundDrawIndex":draw.get("roundDrawIndex",draw.get("index"))}
                            for draw in current_draws]
    native_events = []
    for event in state.get("gameEvents", []):
        observed = {key: value for key, value in event.items() if key not in {"frame", "fixedFrame", "unityFrame", "finalizedFrame"}}
        if event.get("finalizedFrame", -1) >= 0 and state.get("levelFrameZero", -1) >= 0:
            observed["finalizedGameplayFrame"] = event["finalizedFrame"] - state["levelFrameZero"]
        native_events.append(observed)
    event_time_fields = {"remainingFraction", "orderRemaining", "roundElapsed"}
    event_sequence = [{key: value for key, value in event.items() if key not in event_time_fields} for event in native_events]
    events["nativeEvents"] = event_sequence
    event_timing = [select(event, event_time_fields | {"index", "kind", "gameplayFrame"}) for event in native_events]
    physics = {"entities": by_id([select(e, PHYSICS_ENTITY) for e in state.get("entities", [])], "id"),
               "chefs": by_id([select(c, PHYSICS_CHEF) for c in state.get("chefs", [])], "playerId")}
    hashes = by_id([select(e, {"id", "physicsHash"}) for e in state.get("entities", [])], "id")
    observations = select(state, OBSERVATIONS)
    observations["observedOrdinals"] = {str(e.get("id")): e.get("observedOrdinal") for e in state.get("entities", [])}
    rng = {"ambientState": state.get("randomState"), "draws": [
        select(draw, {"index", "generator", "before", "after", "isolated", "recipeIds", "frequencies"})
        for draw in state.get("recipeDraws", [])]}
    current_rng = [{**select(draw, {"generator", "before", "after", "isolated", "recipeIds", "frequencies"}),
                    "roundDrawIndex":draw.get("roundDrawIndex",draw.get("index"))} for draw in current_draws]
    return {"strictState": state, "nativeState": native,
            "nativeStateExceptClockReadings": native_except_clocks, "gameplayEvents": events,
            "physics": physics, "rawPhysicsHashes": hashes, "timing": select(state, TIMING),
            "observations": observations, "recipeRandom": rng, "currentRoundRecipeRandom": current_rng, "nativeEventTrace": native_events,
            "nativeEventSequence": event_sequence, "nativeEventTiming": event_timing}


def entries(path):
    opener = gzip.open if str(path).lower().endswith(".gz") else open
    with opener(path, "rt", encoding="utf-8-sig") as source:
        if ".jsonl" not in str(path).lower():
            yield 1, json.load(source)
            return
        for number, line in enumerate(source, 1):
            if not line.strip() or line.lstrip().startswith("#"):
                continue
            try:
                yield number, json.loads(line)
            except json.JSONDecodeError as error:
                raise ValueError("Invalid JSON at %s:%s: %s" % (path, number, error)) from error


class Samples:
    def __init__(self, path):
        self.path = str(path)
        self.stats = {"path": self.path, "calls": 0, "requestsRecorded": 0, "inputSnapshotsRecorded": 0,
                      "samples": 0, "controllerEvents": {}, "rawPhysicsHashCoverage": {"entities": 0, "validHashes": 0},
                      "preGameplaySnapshots": 0, "failedResponses": [], "segments": []}
        self.request_hash = hashlib.sha256()
        self.events = Counter()

    def __iter__(self):
        segment, last_frame, occurrence = 0, None, 0
        for line, entry in entries(self.path):
            if not isinstance(entry, dict):
                raise ValueError("Expected object at %s:%s" % (self.path, line))
            if entry.get("kind") == "header":
                self.stats["header"] = entry
                continue
            if entry.get("kind") == "event":
                self.events[entry.get("name", "unknown")] += 1
                continue
            request = entry.get("request")
            response = entry.get("response", entry)
            state = response.get("state")
            if state is None and "entities" in response:
                state = response
            self.stats["calls"] += 1
            if request is not None:
                self.stats["requestsRecorded"] += 1
                self.request_hash.update(json.dumps(request, sort_keys=True, separators=(",", ":")).encode())
                self.request_hash.update(b"\n")
            if response.get("ok") is False:
                self.stats["failedResponses"].append({"line": line, "error": response.get("error")})
            if not isinstance(state, dict):
                continue
            frame = state.get("gameplayFrame")
            if frame is None:
                if not state.get("inLevel", False):
                    self.stats["preGameplaySnapshots"] += 1
                    continue
                frame = state.get("frame", 0)
                self.stats["legacyAbsoluteFrameFallback"] = True
            if frame < 0:
                self.stats["preGameplaySnapshots"] += 1
                continue
            if last_frame is not None and (frame < last_frame or (request or {}).get("command") in ("load", "restart")):
                segment += 1
                last_frame = None
            if last_frame is None:
                self.stats["segments"].append({"segment": segment, "initial": summarize(state), "final": None})
            occurrence = occurrence + 1 if last_frame == frame else 0
            last_frame = frame
            self.stats["samples"] += 1
            if "inputs" in response:
                self.stats["inputSnapshotsRecorded"] += 1
            hash_coverage = self.stats["rawPhysicsHashCoverage"]
            hash_coverage["entities"] += len(state.get("entities", []))
            hash_coverage["validHashes"] += sum(len(e.get("physicsHash", "")) == 104 for e in state.get("entities", []))
            self.stats["segments"][-1]["final"] = summarize(state)
            yield {"key": (segment, frame, occurrence), "line": line, "state": state,
                   "request": request, "inputs": response.get("inputs")}
        self.stats["requestSha256"] = self.request_hash.hexdigest()
        self.stats["controllerEvents"] = dict(self.events)


def summarize(state):
    result = select(state, TIMING | EVENT_WORLD | {
        "frame", "fixedFrame", "gameplayFrame", "gameplayFixedFrame", "levelFrameZero",
        "levelFixedFrameZero", "introFrameZero", "alignStartPhysics", "startAlignmentWaitFrames",
        "startAlignmentReleaseFrame", "applicationFocused", "randomState", "warnings"})
    result["orders"] = [select(order, {"id", "recipeId", "recipe", "remaining", "lifetime"}) for order in state.get("orders", [])]
    result["recipeDraws"] = state.get("recipeDraws", [])
    result["lifecycleTransitions"] = [mark for mark in state.get("lifecycle", [])
        if mark.get("state") in {"RunKitchen", "RunLevelIntro", "InLevel", "Active", "BothRoundsActive"}
        or mark.get("source") == "TASStartAlignment"]
    result["entityCount"] = len(state.get("entities", []))
    result["chefCount"] = len(state.get("chefs", []))
    return result


def transient(entity):
    components = entity.get("components", [])
    return (entity.get("hasRigidbody") is True and str(entity.get("name", "")).endswith("_Rigidbody")
            and "ObjectContainer" in components
            and any(name.endswith(".ServerPhysicsObjectSynchroniser") for name in components))


def anchors(state):
    result, rejected = {}, []
    entities = state.get("entities", [])
    for rigidbody in (e for e in entities if transient(e)):
        name = rigidbody["name"][:-len("_Rigidbody")]
        matches = [e for e in entities if not transient(e) and e.get("name") == name
                   and e.get("position") == rigidbody.get("position") and e.get("hierarchyPath")]
        if len(matches) != 1:
            rejected.append({"rigidbodyId": rigidbody.get("id"), "name": name,
                             "reason": "Initial same-name/same-position anchor is not unique", "candidates": len(matches)})
            continue
        anchor = matches[0]
        proof = {"anchorId": anchor.get("id"), "anchorPath": anchor["hierarchyPath"],
                 "anchorName": name, "anchorComponents": sorted(anchor.get("components", [])),
                 "initialPosition": anchor.get("position"),
                 "rigidbodyComponents": sorted(rigidbody.get("components", []))}
        key = json.dumps(proof, sort_keys=True, separators=(",", ":"))
        if key in result:
            rejected.append({"rigidbodyId": rigidbody.get("id"), "reason": "Multiple rigidbodies share an anchor"})
            result[key] = None
        else:
            result[key] = (rigidbody["id"], proof)
    return result, rejected


def validate_mapping(a, b):
    left, left_rejected = anchors(a)
    right, right_rejected = anchors(b)
    maps = ({}, {})
    evidence = {"validated": [], "expectedRejected": left_rejected, "actualRejected": right_rejected,
                "expectedUnmatched": [], "actualUnmatched": []}
    if a.get("scene") != b.get("scene") or a.get("gameplayFrame") != 0 or b.get("gameplayFrame") != 0:
        evidence["refused"] = "Requires same scene and both initial samples at gameplayFrame 0"
        return maps, evidence
    for key in sorted(left.keys() | right.keys()):
        lvalue, rvalue = left.get(key), right.get(key)
        if not lvalue or not rvalue:
            if lvalue:
                evidence["expectedUnmatched"].append(lvalue[0])
            if rvalue:
                evidence["actualUnmatched"].append(rvalue[0])
            continue
        lid, proof = lvalue
        rid = rvalue[0]
        canonical = "rigidbody@anchor:" + str(proof["anchorId"])
        maps[0][lid], maps[1][rid] = canonical, canonical
        evidence["validated"].append({"expectedId": lid, "actualId": rid, "canonicalId": canonical,
            "identityChanged": lid != rid, "proof": proof,
            "proofSha256": hashlib.sha256(key.encode()).hexdigest()})
    return maps, evidence


def canonicalize(state, mapping):
    def remap(value):
        if isinstance(value, dict):
            result = None
            for key, child in value.items():
                if key in ENTITY_REFERENCES and isinstance(child, int):
                    changed = mapping.get(child, child)
                elif isinstance(child, (dict, list)):
                    changed = remap(child)
                else:
                    continue
                if changed is not child:
                    if result is None:
                        result = value.copy()
                    result[key] = changed
            return value if result is None else result
        elif isinstance(value, list):
            result = None
            for index, child in enumerate(value):
                changed = remap(child) if isinstance(child, (dict, list)) else child
                if changed is not child and result is None:
                    result = value[:index]
                if result is not None:
                    result.append(changed)
            return value if result is None else result
        return value

    # Projections are read-only. Copy only branches containing remapped IDs;
    # unchanged collider/food/transform data can safely share the parsed state.
    result = remap(state) if mapping else state
    entities = result.get("entities", [])
    changed_entities = None
    for index, entity in enumerate(entities):
        identity = mapping.get(entity.get("id"), entity.get("id"))
        if "id" not in entity or identity is not entity["id"]:
            if changed_entities is None:
                changed_entities = entities.copy()
            changed_entities[index] = {**entity, "id": identity}
    if changed_entities is not None:
        result = {**result, "entities": changed_entities}
    return result


def location(sample):
    return {"segment": sample["key"][0], "gameplayFrame": sample["key"][1],
            "occurrence": sample["key"][2], "line": sample["line"],
            "absoluteFrame": sample["state"].get("frame"), "fixedFrame": sample["state"].get("fixedFrame")}


def analyze(expected_path, actual_path, map_rigidbodies=False):
    expected, actual = Samples(expected_path), Samples(actual_path)
    left, right = iter(expected), iter(actual)
    a, b = next(left, None), next(right, None)
    report = {"format": "oc2-repro-analysis", "version": 1, "comparisonPolicy": {
        "alignment": "segment, gameplayFrame, same-frame occurrence",
        "precision": "Exact JSON numeric equality; physicsHash preserves all recorded IEEE754 bits",
        "observationsExcludedFromNativeState": sorted(OBSERVATIONS) + ["entities.*.observedOrdinal"],
        "nativeState": "Every other field, keyed by native entity ID and player ID; unknown fields retained",
        "nativeStateExceptClockReadings": "Additional projection excluding only native clientTime and clientDeltaTime; their divergence is still reported separately",
        "gameplayEvents": "Named discrete projection; excludes continuous timers, progress values and transforms; recipe observations select currentRecipeDraws when present and compare native per-round draw indices rather than observational instance ordinals. Full recipeDraws remain in strictState/observations/recipeRandom.",
        "eventWorldFields": sorted(EVENT_WORLD), "eventChefFields": sorted(EVENT_CHEF),
        "eventEntityFields": sorted(EVENT_ENTITY),
        "nativeEventTrace": "Native observer payloads including gameplay frames; absolute frame/fixedFrame/unityFrame removed and finalizedFrame converted to gameplay origin",
        "nativeEventSequence": "Same ordered native payloads excluding only remainingFraction/orderRemaining/roundElapsed; nativeEventTiming and nativeEventTrace retain these exact values",
        "canonicalMapping": "Opt-in, initial Rigidbody proxies only; raw/native-ID comparisons always retained",
        "limitations": ["First divergence means first observed sample; unsampled frames are not inferred",
                        "No exclusions establish fresh-process determinism or prove unobserved engine state equal",
                        "Canonical initial identity matching does not suppress later position or physics differences"]},
        "comparedSamples": 0, "sampleCoverage": {}, "missingSamples": {"expectedOnly": 0, "actualOnly": 0, "first": None},
        "comparisons": {}, "canonicalMappings": [], "expected": expected.stats, "actual": actual.stats}
    maps, active_segment, previous_frame = ({}, {}), None, None
    while a is not None or b is not None:
        if a is None or b is None or a["key"] != b["key"]:
            take_left = a is not None and (b is None or a["key"] < b["key"])
            sample = a if take_left else b
            side = "expectedOnly" if take_left else "actualOnly"
            report["missingSamples"][side] += 1
            if report["missingSamples"]["first"] is None:
                report["missingSamples"]["first"] = {"side": side, **location(sample)}
            if take_left:
                a = next(left, None)
            else:
                b = next(right, None)
            continue
        segment, frame, _ = a["key"]
        if segment != active_segment:
            active_segment, previous_frame = segment, None
            maps = ({}, {})
            if map_rigidbodies:
                maps, evidence = validate_mapping(a["state"], b["state"])
                report["canonicalMappings"].append({"segment": segment, **evidence})
        coverage = report["sampleCoverage"].setdefault(str(segment), {"firstFrame": frame, "lastFrame": frame, "largestGap": 0})
        coverage["lastFrame"] = frame
        if previous_frame is not None:
            coverage["largestGap"] = max(coverage["largestGap"], frame - previous_frame)
        previous_frame = frame
        report["comparedSamples"] += 1
        av, bv = views(a["state"]), views(b["state"])
        av["requests"], bv["requests"] = a["request"], b["request"]
        av["emulatedInputs"], bv["emulatedInputs"] = a["inputs"], b["inputs"]
        if map_rigidbodies:
            ca, cb = views(canonicalize(a["state"], maps[0])), views(canonicalize(b["state"], maps[1]))
            for name in ("nativeState", "nativeStateExceptClockReadings", "gameplayEvents", "physics", "rawPhysicsHashes"):
                av["canonical." + name], bv["canonical." + name] = ca[name], cb[name]
        equal_pairs = {}
        for name in av:
            result = report["comparisons"].setdefault(name, {"equalSamples": 0, "differentSamples": 0, "firstDivergence": None})
            if result["firstDivergence"] is not None:
                result["equalSamples" if values_equal(av[name], bv[name], equal_pairs) else "differentSamples"] += 1
                continue
            difference = first_diff(av[name], bv[name], _equal_pairs=equal_pairs)
            if difference:
                result["differentSamples"] += 1
                if result["firstDivergence"] is None:
                    result["firstDivergence"] = {"expectedSample": location(a), "actualSample": location(b), **difference}
            else:
                result["equalSamples"] += 1
        a, b = next(left, None), next(right, None)
    full_requests = (expected.stats["requestsRecorded"] > 0 and actual.stats["requestsRecorded"] > 0
                     and expected.stats["requestsRecorded"] == expected.stats["calls"]
                     and actual.stats["requestsRecorded"] == actual.stats["calls"])
    report["sameRequestSequence"] = ((expected.stats.get("requestSha256") == actual.stats.get("requestSha256")
                                      and expected.stats["calls"] == actual.stats["calls"]) if full_requests else None)
    report["completeSampleAlignment"] = not (report["missingSamples"]["expectedOnly"] or report["missingSamples"]["actualOnly"])
    report["hasGameplayEvidence"] = report["comparedSamples"] > 0
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("expected", type=Path)
    parser.add_argument("actual", type=Path)
    parser.add_argument("--map-initial-rigidbodies", action="store_true")
    parser.add_argument("--out", type=Path, help="Write full JSON report; console then shows compact first differences")
    args = parser.parse_args()
    if args.out and args.out.resolve() in {args.expected.resolve(), args.actual.resolve()}:
        parser.error("Report output must not overwrite an input")
    try:
        report = analyze(args.expected, args.actual, args.map_initial_rigidbodies)
    except (OSError, ValueError, EOFError) as error:
        print("analysis failed: " + str(error), file=sys.stderr)
        return 2
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False) + "\n", encoding="utf-8")
        summary = {"report": str(args.out.resolve()), "comparedSamples": report["comparedSamples"],
                   "completeSampleAlignment": report["completeSampleAlignment"],
                   "sameRequestSequence": report["sameRequestSequence"],
                   "firstDivergences": {key: value["firstDivergence"] for key, value in report["comparisons"].items()}}
        print(json.dumps(summary, indent=2, ensure_ascii=False, allow_nan=False))
    else:
        print(json.dumps(report, indent=2, ensure_ascii=False, allow_nan=False))
    return 0 if report["hasGameplayEvidence"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
