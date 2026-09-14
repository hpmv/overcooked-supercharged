"""Observed fish/prawn first-delivery planner. No sockets or native state writes.

Entity IDs come from the live registry. Recipe names are the vanilla single-leaf
recipes; prepared ingredient IDs come from the actual native food observation.
Each batch has a native postcondition. Input/action completion alone never
proves chopping, assembly or delivery.
"""
from __future__ import annotations

from dataclasses import dataclass
from math import hypot, isfinite
import copy


class Blocked(ValueError):
    pass


RECIPES = {
    "Sushi_PlainFish": {"prepared": "SushiFish", "spawnWord": "fish"},
    "Sushi_PlainPrawn": {"prepared": "SushiPrawn", "spawnWord": "prawn"},
}


def require(ok, message):
    if not ok:
        raise Blocked(message)


def leaves(tree, depth=0):
    """Preserve native multiplicities and reject any process/optional mismatch."""
    if tree is None:
        return []
    require(isinstance(tree, dict) and depth < 16, "Missing or excessive native food definition")
    if tree.get("type") == "NullAssembledNode":
        return []
    require(not tree.get("optional"), "Optional food composition is outside plain Story11 recipes")
    require(tree.get("type") in {"IngredientAssembledNode", "CompositeAssembledNode"}, "Unexpected native preparation step")
    if tree["type"] == "IngredientAssembledNode":
        require(type(tree.get("id")) is int and isinstance(tree.get("name"), str), "Native ingredient identity is absent")
        return [(tree["id"], tree["name"])]
    return [leaf for child in tree.get("children", []) for leaf in leaves(child, depth+1)]


def validate_observed_layout(state):
    """Accept only current native path/container/removal receipts for extras.

    Keep the original fixed-map audit untouched. A generic extra-collider
    warning cannot stand in for a mapped spawn or its exact physical container.
    """
    audit=state.get("registryValidation") or {}
    require(not audit.get("semanticGaps"), "Native fixed-map semantic gap")
    if audit.get("layoutValid") is True and not audit.get("errors"):
        return {"source":"fixed-native-registry-audit","acceptedExtraIds":[]}
    graph=state.get("graphMappingValidation") or {}
    require(graph.get("ok") is True and graph.get("frame")==state.get("frame") and graph.get("fixedMappingsValidated") is True,
            "Current native graph mapping/physical-container receipts are required after a spawn")
    live={e["id"]:e for e in state["entities"] if e.get("exists")}
    mapped={}
    for row in graph.get("spawnedMappings") or []:
        eid=row.get("id");path=row.get("path")
        require(eid in live and live[eid].get("path")==path and isinstance(path,list) and len(path)>1 and eid not in mapped,
                "Current native mapped spawn identity/path is inconsistent")
        mapped[eid]=path
    registry={e["EntityId"]:e for e in state["registry"]}
    errors=audit.get("errors") or []
    warned={e.get("id") for e in errors}
    allowed=set(mapped)
    for row in graph.get("observedPhysicalContainers") or []:
        eid=row.get("nativeId");owner=row.get("logicalPath")
        # The host also reports original fixed plate/proxy relationships. They
        # need no exception to the fixed audit and are not dynamic admissions.
        if eid not in warned:continue
        require(eid not in live and eid not in allowed and owner in mapped.values(),"Native physical-container association has no unique live mapped owner")
        metadata=registry.get(eid) or {}
        require({"Rigidbody","ObjectContainer"} <= set(metadata.get("Components") or []) and 47 in (metadata.get("SyncEntityTypes") or []),
                "Native associated physical container lacks its actual component/codec metadata")
        allowed.add(eid)
    removed=graph.get("removedNativeIds") or []
    require(all(type(i) is int and i not in live and i not in allowed for i in removed),"Native removed ID is still a live mapped participant")
    allowed.update(removed)
    require(bool(errors) and all(e.get("id") in allowed and e.get("reason")=="Unmodeled registered object has a physical collider; layout needs review." for e in errors),
            "Native Story11 fixed mapping error or unproved extra collider")
    return {"source":"current-native-graph-mappings","frame":state["frame"],"registrySha256":graph.get("registrySha256"),"acceptedExtraIds":sorted(allowed)}


class Observation:
    def __init__(self, state, receipt, scene):
        self.state, self.receipt = state, receipt
        b = receipt.get("bridge", {}); self.round = b.get("nativeRound", {})
        require(state.get("state") == "Paused" and not state.get("requestPending") and not state.get("errors") and not state.get("invalidStateReason"), "Host is not a settled valid boundary")
        self.layout_proof = validate_observed_layout(state)
        require(b.get("paused") is True and b.get("loadComplete") is True and b.get("session", {}).get("scene") == scene, "Wrong or unloaded native scene")
        require(all(b["session"].get(k) == 4 for k in ("variantPlayers", "serverUsers", "clientUsers", "virtualPads")) and self.round.get("available") is True, "Four-player native round required")
        require(self.round.get("configuredDuration") == 150 and self.round.get("timeLimit") == 150, "Live Story11 round duration is not150 seconds")
        # Native suppression is observed, never overridden here. Root's scoped
        # immediate-timer instrumentation can make the initial checkpoint legal.
        self.frame = state["frame"]
        rows = [e for e in state["entities"] if e.get("exists")]
        self.entities = {e["id"]: e for e in rows}
        self.paths = {tuple(e["path"]): e["id"] for e in rows}
        self.registry = {e["EntityId"]: e for e in state["registry"]}
        require(receipt.get("detail", {}).get("source") == "native-server-preparation-composition", "Native food source is absent")
        self.food = {e["id"]: e for e in receipt["detail"]["entities"]}
        require(len(rows) == len(self.entities) == len(self.paths) and set(self.entities) <= set(self.registry), "Duplicate/unregistered native identities")
        self.chefs = sorted(e["id"] for e in rows if e.get("chef") is not None)
        require(len(self.chefs) == 4, "Missing one of four actual chefs")

    def components(self, eid):
        return {c.rsplit(".", 1)[-1] for c in self.registry[eid].get("Components", [])}

    def role(self, component):
        return [i for i in self.entities if component in self.components(i) and len(self.entities[i]["path"]) == 1]

    def parent(self, eid):
        path = (self.entities[eid].get("data", {}).get("attachmentParent") or {}).get("path", [])
        require(not path or tuple(path) in self.paths, "Attachment parent path is not live")
        return self.paths.get(tuple(path)) if path else None

    def on(self, parent):
        found = [i for i in self.entities if self.parent(i) == parent]
        inverse = (self.entities[parent].get("data", {}).get("attachment") or {}).get("path", [])
        require(len(found) <= 1, "Ambiguous native held/attached object")
        if found:
            require(inverse == self.entities[found[0]]["path"], "Native attachment directions disagree")
            return found[0]
        require(not inverse, "Native attachment inverse points to no live child")
        return None

    def composition(self, eid):
        require(eid in self.food, "Missing native food observation")
        return leaves(self.food[eid].get("composition"))

    def is_food(self, eid, name):
        return eid is not None and eid in self.food and [n for _, n in self.composition(eid)] == [name]

    def clean_plate(self, eid):
        return "ServerPlate" in self.components(eid) and eid in self.food and not self.composition(eid) and not (self.entities[eid].get("plateLifecycle") or {}).get("phase")

    def distance(self, a, b):
        p, q = self.entities[a]["position"], self.entities[b]["position"]
        require(all(isfinite(v) for v in (p["x"], p["z"], q["x"], q["z"])), "Nonfinite observed geometry")
        return hypot(p["x"]-q["x"], p["z"]-q["z"])

    def highlight(self, chef, field):
        value = (self.entities[chef]["chef"].get(field) or {}).get("path", [])
        return self.paths.get(tuple(value)) if value else None


def action(key, chef, kind, target=None, **extra):
    row = {"id": key, "chef": chef, "type": kind, "timeoutFrames": 480, **extra}
    if target is not None:
        row["target"] = target
    return row


def batch(rows, purpose, maximum=600):
    return {"purpose": purpose, "request": {"command": "actions", "maximumFrames": maximum, "actions": rows}}


def primary_pulse(chefs, chef):
    return {"command": "raw-input", "segments": [{"frames": 1, "chefs": {
        str(i): {"x": 0, "y": 0, "pickup": i == chef, "interact": False, "dash": False} for i in chefs}}]}


def candidates(o):
    orders = o.round.get("orders") or []
    require(bool(orders), "No actual native order")
    head = orders[0]; spec = RECIPES.get(head.get("recipe"))
    require(spec is not None, "Observed order is not a supported vanilla plain fish/prawn recipe")
    crates = [(i, s) for i, e in o.registry.items() if i in o.entities and len(o.entities[i]["path"]) == 1
              for s in (e.get("SpawnNames") or []) if spec["spawnWord"] in s.lower()]
    require(len(crates) == 1, "Native matching crate/SpawnNames are missing or ambiguous")
    crate, spawn = crates[0]
    boards = [i for i in o.role("Workstation") if o.on(i) is None]
    plates = [i for i in o.entities if o.clean_plate(i) and o.parent(i) is not None and o.parent(i) not in o.chefs]
    serves = o.role("PlateStation")
    free = [i for i in o.chefs if o.on(i) is None]
    require(boards and plates and serves and len(free) >= 2, "Need a free chopping board, clean plate, service station and two empty-handed chefs")
    result = []
    for chef in sorted(free, key=lambda c: (o.distance(c, crate), c))[:2]:
        board = min(boards, key=lambda b: (o.distance(crate, b), b))
        helper, plate = min(((h, p) for h in free if h != chef for p in plates),
                            key=lambda hp: (o.distance(hp[0], hp[1])+o.distance(hp[1], board), hp))
        serve = min(serves, key=lambda s: (o.distance(board, s), s))
        for dash in (False, True):
            resources = [crate, board]
            rows = [action("fetch", chef, "pickup", crate, expectSpawn=True, dash=dash, resources=resources),
                    action("board", chef, "place", board, after=["fetch"], dash=dash, resources=resources),
                    action("clean-plate", helper, "pickup", plate, dash=dash, resources=[plate])]
            result.append({"id": f"chef-{chef}-{'dash' if dash else 'walk'}", "chef": chef, "helper": helper,
                           "board": board, "plate": plate, "crate": crate, "spawnName": spawn, "serve": serve,
                           "head": copy.deepcopy(head), "spec": spec, "dash": dash,
                           "initialLedger": copy.deepcopy(o.round["ledger"]), "startFrame": o.frame,
                           "startElapsed": o.round["elapsed"],
                           "request": batch(rows, "Fetch native raw ingredient and collect plate in parallel")["request"]})
    return result


def staged_proof(o, case):
    raw = o.on(case["board"])
    raw_work = raw_workable_proof(o, raw, case)
    raw_path = o.entities[raw]["path"]
    require(len(raw_path) == 2 and raw_path[0] == case["crate"], "Raw source does not have the selected crate's observed spawn path")
    require(o.on(case["chef"]) is None and o.on(case["helper"]) == case["plate"] and o.clean_plate(case["plate"]), "Parallel ingredient/clean-plate transfers lack two-sided proof")
    require(o.round["ledger"] == case["initialLedger"], "Preparation unexpectedly changed native ledger")
    require(any(r["id"] == case["head"]["id"] and r["recipeId"] == case["head"]["recipeId"] for r in o.round["orders"]), "Selected native order changed during preparation")
    frames = o.frame-case["startFrame"]; seconds = o.round["elapsed"]-case["startElapsed"]
    require(0 < frames <= 604 and isfinite(seconds) and seconds > 0, "Invalid native preparation duration")
    return {"achieved": True, "frames": frames, "nativeSeconds": seconds, "rawId": raw,
            "rawPath": o.entities[raw]["path"], "rawComposition": o.food[raw]["composition"],
            "rawWorkable": raw_work,
            "plate": case["plate"], "board": case["board"]}


def raw_workable_proof(o, item, case):
    require(item in o.entities and item in o.food,"Native raw workable item is absent")
    row=o.food[item];meta=o.registry[item];path=o.entities[item]["path"]
    names=o.registry[case["crate"]].get("SpawnNames") or []
    require(len(path)==2 and path[0]==case["crate"] and case["spawnName"] in names,
            "Raw source lacks its exact observed crate prefab/path")
    require(row.get("name")==case["spawnName"] and meta.get("Name")==case["spawnName"]
            and "ServerWorkableItem" in o.components(item),"Raw native object/workable component differs from the selected crate prefab")
    require(row.get("composition") is None,"Raw workable unexpectedly has an assembled ingredient composition")
    require(row.get("workStages")==8 and type(row.get("workStage")) is int and 0<=row["workStage"]<=7
            and type(row.get("workSubStage")) is int and row["workSubStage"]>=0,"Raw native preparation stage metadata is missing or inconsistent")
    return {"name":row["name"],"path":path,"workStage":row["workStage"],"workSubStage":row["workSubStage"],
            "workStages":row["workStages"],"composition":None,"source":"native ServerWorkableItem and exact observed crate spawn"}


@dataclass
class DeliveryPlanner:
    case: dict
    phase: str = "staged"
    phase_start: int = 0
    prepared_composition: list | None = None
    source: int | None = None
    raw_source: int | None = None
    raw_path: list | None = None
    assembly_attempts: int = 0

    def advance(self, o):
        c = self.case; plate, helper, board, chef = (c[k] for k in ("plate", "helper", "board", "chef"))
        require(o.frame-c["startFrame"] <= 2400, "First delivery exceeded its bounded native frame budget")
        if self.phase == "serving":
            before, after = c["initialLedger"], o.round["ledger"]
            require(after["deliveries"] == before["deliveries"]+1 and after["baseScore"]-before["baseScore"] == c["head"]["baseValue"], "Native ledger did not verify the expected delivery")
            require(after["deductions"] == before["deductions"] and after["total"] > before["total"], "Native delivery has a deduction or no score gain")
            require(not any(r["id"] == c["head"]["id"] for r in o.round["orders"]) and o.on(helper) != plate, "Delivered native order/plate is still held")
            return {"complete": True, "proof": {"order": c["head"], "ledgerBefore": before, "ledgerAfter": after,
                    "frames": o.frame-c["startFrame"], "plate": plate, "ingredient": self.prepared_composition}}
        require(any(r["id"] == c["head"]["id"] and r["recipeId"] == c["head"]["recipeId"] for r in o.round["orders"]), "Native head disappeared before delivery")
        if self.phase == "staged":
            stage=staged_proof(o, c);self.raw_source=stage["rawId"];self.raw_path=stage["rawPath"]
            self.phase = "chopping"; self.phase_start = o.frame
        if self.phase == "chopping":
            item = o.on(board)
            if not o.is_food(item, c["spec"]["prepared"]):
                raw_workable_proof(o,item,c)
                require(item==self.raw_source and o.entities[item]["path"]==self.raw_path and o.frame-self.phase_start <= 480, "Unexpected food or native chop timeout")
                rows = []
                if o.highlight(chef, "interactingEntity") not in {board, item}:
                    rows.append(action("start-chop", chef, "interact", board, resources=[board, item]))
                rows.append(action("native-chop", chef, "wait", frames=60, timeoutFrames=90, resources=[board, item]))
                return batch(rows, "Observe native preparation until the raw item becomes the exact recipe ingredient")
            require(self.raw_source not in o.entities and len(o.entities[item]["path"])==len(self.raw_path)+1
                    and o.entities[item]["path"][:-1]==self.raw_path,"Prepared food does not prove native replacement of the exact raw source")
            self.source = item; self.prepared_composition = o.composition(item); self.phase = "prepare-assembly"
        if self.phase == "prepare-assembly":
            require(o.on(helper) == plate and o.clean_plate(plate), "Reserved clean plate changed before assembly")
            self.phase = "assembly-edge"
            rows=[];after=[]
            if o.distance(chef,board)<1.8:
                require(o.on(chef) is None,'Assembly approach chopper is unexpectedly holding an item')
                source=o.entities[board]['position'];position=o.entities[chef]['position']
                require(position['z']>source['z']+.5 and abs(position['x']-source['x'])<.8,
                        'Occupied board approach is outside the observed front-side clearance geometry')
                # A literal waypoint beside the neighbouring board proved too
                # fragile on later meals: the incoming helper could push the
                # already-completed chopper action back across this board's
                # interaction point.  Facing the exact source crate is still an
                # ordinary no-button navigation action, is already part of the
                # selected case, and leaves the complete board approach clear.
                rows.append(action('clear-chopper',chef,'prepare-primary',c['crate'],
                                   resources=[board,c['crate']]))
                after=['clear-chopper']
            rows.append(action("face-food", helper, "prepare-primary", board, after=after, resources=[board, plate, self.source]))
            return batch(rows, "Clear any occupied front approach, then navigate to the exact native assembly target")
        if self.phase == "assembly-edge":
            require(o.on(board) == self.source and o.composition(self.source) == self.prepared_composition and o.on(helper) == plate, "Native source/plate identity changed")
            require(o.highlight(helper, "highlightedForPickup") in {board, self.source}, "Native assembly target is not the reserved food/board")
            self.assembly_attempts += 1
            self.phase = "assembly-proof"
            return {"purpose": "Ordinary primary edge for native plate assembly", "request": primary_pulse(o.chefs, helper)}
        if self.phase == "assembly-proof":
            if not (o.composition(plate) == self.prepared_composition and self.source not in o.entities):
                require(o.on(board) == self.source and o.composition(self.source) == self.prepared_composition
                        and o.on(helper) == plate and o.clean_plate(plate),
                        "Native assembly did not consume the exact prepared ingredient into the same plate")
                require(self.assembly_attempts < 4,
                        "Native assembly ignored four ordinary primary pulses despite unchanged exact food/plate identities")
                self.phase = "assembly-edge"
                return batch([action("assembly-retry-settle", helper, "wait", frames=1, timeoutFrames=30,
                                     resources=[board, plate, self.source])],
                             "Bounded neutral settle before retrying an ignored ordinary assembly pulse")
            if o.on(helper) != plate:
                require(o.on(board) == plate and o.on(helper) is None, "Native plate-under-food recovery is not attached as expected")
                self.phase = "ready"
                return batch([action("recover-plated", helper, "pickup", plate, resources=[board, plate])], "Recover the exact natively plated meal")
            self.phase = "ready"
        if self.phase == "ready":
            require(o.on(helper) == plate and o.composition(plate) == self.prepared_composition, "Exact prepared plate is not held for service")
            self.phase = "service-edge"
            return batch([action("face-service", helper, "prepare-primary", c["serve"], resources=[plate, c["serve"]])], "Navigate the exact plated meal to native service")
        if self.phase == "service-edge":
            require(o.on(helper) == plate and o.composition(plate) == self.prepared_composition and o.highlight(helper, "highlightedForPickup") == c["serve"], "Native serving target or plated recipe changed")
            self.phase = "serving"
            return {"purpose": "Ordinary native delivery edge", "request": primary_pulse(o.chefs, helper)}
        raise Blocked("Unknown first-delivery phase")
