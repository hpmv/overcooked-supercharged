"""Independent native chopped-flavor throw/catch, mixing and frying evidence."""
import argparse
import gzip
import json
import math
from pathlib import Path
from check_bowl_offmix import ingredients, nodes, ordinary_request, controller_evidence, pin, require

JOBS = ["T01-supply-flour", "T02-supply-egg", "T03-chop-and-throw-flavor", "T04-mix-fry-and-restore"]


class Checker:
    def __init__(self, flavor):
        self.flavor, self.recipe = {"Chocolate": (22804, 228996), "Raspberry": (129618, 130976)}[flavor]
        self.name, self.expected = flavor, sorted([18448, 16620, self.flavor])
        self.last = None
        self.mapping = None
        self.raw = self.raw_ordinal = self.source = self.source_ordinal = None
        self.progress = []
        self.work_frames = self.flight_frames = 0
        self.prepared_frame = self.held_frame = self.release_frame = self.caught_frame = None
        self.native_mixed = self.bowl_carried = self.transferred = False
        self.done = []
        self.release = None
        self.throw_complete = None

    def entity(self, state, identity):
        return next((e for e in state["entities"] if e["id"] == identity and e.get("active")), None)

    def resolve(self, state):
        require(state["gameplayFrame"] == 0 and state["scene"] == "s_Day_3_4" and len(state["chefs"]) == 4, "Fresh native four-chef Carnival start required")
        def station(component, x, z):
            found = [e for e in state["entities"] if e.get("active") and component in e.get("components", [])
                     and abs(e["position"]["x"] - x) < .05 and abs(e["position"]["z"] - z) < .05]
            require(len(found) == 1, "Native station is not uniquely resolved: " + component)
            return found[0]
        home = station("MixingStation", 24, -10.8)
        far_home = station("MixingStation", 22.8, -10.8)
        bowl, other = self.entity(state, home["attachedEntityId"]), self.entity(state, far_home["attachedEntityId"])
        basket = station("CookableContainer", 22.8, -21.6)
        board = station("Workstation", 25.2, -14.4)
        require(bowl and other and "IngredientCatcher" in bowl["components"] and "MixableContainer" in bowl["components"], "Original native mixer catcher is missing")
        require(all(not ingredients(e.get("composition")) for e in [bowl, other, basket]) and board["attachedEntityId"] == 0, "Fresh bowl, other bowl, fryer and board must be empty")
        self.mapping = {"home": home["id"], "bowl": bowl["id"], "otherBowl": other["id"], "basket": basket["id"], "board": board["id"]}
        self.identities = {e["id"]: e["observedOrdinal"] for e in [home, bowl, other, basket, board]}

    def mixed(self, entity):
        food = entity.get("composition")
        return ingredients(food) == self.expected and any(n.get("type") == "MixedCompositeAssembledNode" and n.get("state") == "Mixed" for n in nodes(food))

    def state(self, state, inputs):
        if not self.mapping:
            self.resolve(state)
        frame = state["gameplayFrame"]
        for identity, ordinal in self.identities.items():
            entity = self.entity(state, identity)
            require(entity and entity["observedOrdinal"] == ordinal, "An original station or vessel identity changed")
        m = self.mapping
        bowl, other, basket, board = [self.entity(state, m[k]) for k in ("bowl", "otherBowl", "basket", "board")]
        chef = next(c for c in state["chefs"] if c["playerId"] == 1)
        central = next(c for c in state["chefs"] if c["playerId"] == 3)
        require(bowl["mixingTime"] == 12 and basket["cookingTime"] == 10, "Native preparation durations changed")
        require(not ingredients(other.get("composition")), "The other original bowl received an unintended ingredient")
        require(state["score"] == 0 and state["delivered"] == 0 and state["deductions"] == 0, "Unexpected score, delivery or order expiry")
        item = self.entity(state, board["attachedEntityId"])
        if item and "WorkableItem" in item.get("components", []) and item.get("workProgress", -1) >= 0:
            require(item["name"].lower().startswith(self.name.lower()), "A different raw flavor appeared on the reserved board")
            if self.raw is None:
                self.raw, self.raw_ordinal = item["id"], item["observedOrdinal"]
            require(item["id"] == self.raw and item["observedOrdinal"] == self.raw_ordinal, "The raw workable identity changed before preparation")
            self.progress.append(item["workProgress"])
            if chef.get("interactingEntityId") == m["board"] and chef.get("serverInteractionId") == m["board"] and chef["heldEntityId"] == 0:
                self.work_frames += 1
        if item and ingredients(item.get("composition")) == [self.flavor] and "WorkableItem" not in item.get("components", []):
            require(self.raw is not None and item["id"] != self.raw and "ThrowableItem" in item.get("components", []), "Prepared flavor lacks native workable replacement/throwable evidence")
            if self.source is None:
                self.source, self.source_ordinal, self.prepared_frame = item["id"], item["observedOrdinal"], frame
        source = self.entity(state, self.source) if self.source else None
        if source:
            require(source["observedOrdinal"] == self.source_ordinal and ingredients(source.get("composition")) == [self.flavor], "Exact prepared throwable changed before its native catch")
            if chef["heldEntityId"] == self.source and self.held_frame is None:
                require(board["attachedEntityId"] == 0, "P1 pickup did not clear the shared preparation board")
                self.held_frame = frame
            if self.release_frame is not None and source.get("throwFlying") and source.get("throwerEntityId") == chef["entityId"]:
                self.flight_frames += 1
        if self.release_frame is not None and self.caught_frame is None:
            require(self.entity(state, m["home"])["attachedEntityId"] == m["bowl"], "Receiving bowl left its exact original mixer during flavor flight")
            if not source and ingredients(bowl.get("composition")) == self.expected and chef["heldEntityId"] == 0:
                require(self.flight_frames > 0, "Flavor disappeared into a bowl without observed native P1 flight")
                self.caught_frame = frame
        if self.mixed(bowl):
            self.native_mixed = True
            if central["heldEntityId"] == m["bowl"] and self.entity(state, m["home"])["attachedEntityId"] == 0:
                self.bowl_carried = True
        if self.caught_frame is not None and not ingredients(bowl.get("composition")) and self.mixed(basket):
            require(self.native_mixed and self.bowl_carried, "Fryer received dough without native Mixed bowl pickup")
            self.transferred = True
        self.last, self.inputs = state, inputs

    def event(self, name, value):
        require(name not in ["actionFailure", "planFailure", "throwFailure"], "Native flavor probe recorded failure")
        if name == "throwReleased" and self.source and value.get("itemId") == self.source:
            require(self.release_frame is None and self.held_frame is not None and value.get("player") == 1, "Prepared flavor release lacks its original P1 pickup")
            chef = next(c for c in self.last["chefs"] if c["playerId"] == 1)
            bowl = self.entity(self.last, self.mapping["bowl"])
            require(ingredients(bowl.get("composition")) == [16620, 18448], "Flavor release did not target the exact flour-and-egg bowl")
            require(self.entity(self.last, self.mapping["home"])["attachedEntityId"] == self.mapping["bowl"], "Flavor receiver is not at its original mixer")
            distance = math.hypot(chef["position"]["x"] - bowl["position"]["x"], chef["position"]["z"] - bowl["position"]["z"])
            self.release_frame = self.last["gameplayFrame"]
            self.release = {"frame": self.release_frame, "absoluteNativeFrame": value["frame"], "distance": distance,
                            "throwForce": chef["throwForce"], "throwInclination": chef["throwInclination"], "beforeIngredientIds": [16620, 18448]}
        if name == "throwComplete" and value.get("itemId") == self.source:
            require(value.get("player") == 1 and value.get("targetEntityId") == self.mapping["bowl"] and self.caught_frame is not None,
                    "Flavor completion lacks its exact observed native target catch")
            self.throw_complete = value
        if name == "jobComplete":
            require(value.get("id") in JOBS and value["id"] not in self.done, "Unexpected or duplicate probe job")
            self.done.append(value["id"])

    def finish(self):
        require(self.done == JOBS and self.work_frames > 0 and self.progress and max(self.progress) - min(self.progress) > .5,
                "Native P1 chopping or authored job completion evidence is incomplete")
        require(self.release and self.caught_frame is not None and self.throw_complete and self.native_mixed and self.transferred,
                "Native flavor throw/catch/mix/fryer transfer evidence is incomplete")
        m, state = self.mapping, self.last
        basket, bowl = self.entity(state, m["basket"]), self.entity(state, m["bowl"])
        require(self.mixed(basket) and basket["composition"]["type"] == "CookedCompositeAssembledNode" and basket["composition"]["state"] == "Cooked"
                and basket["composition"]["cookingStepId"] == 17160, "Final exact native donut recipe is not correctly cooked")
        require(not ingredients(bowl.get("composition")) and self.entity(state, m["home"])["attachedEntityId"] == m["bowl"], "Original empty mixing bowl was not restored")
        require(all(c["controlsEnabled"] and c["heldEntityId"] == 0 for c in state["chefs"]), "Final chefs are not controlled and empty")
        require(len(self.inputs) == 4 and all(i["x"] == 0 and i["y"] == 0 and not any(i[k] for k in ["pickup", "use", "dash"]) for i in self.inputs), "Final emitted inputs are not neutral")
        return {"ok": True, "classification": "One native chopped-flavor throw/catch and completed donut mechanism; no high-score/replay qualification",
                "mapping": m, "flavorId": self.flavor, "finalRecipeId": self.recipe, "rawItem": self.raw, "preparedItem": self.source,
                "preparedOrdinal": self.source_ordinal, "nativeP1ChopFrames": self.work_frames, "nativeWorkProgressMin": min(self.progress),
                "nativeWorkProgressMax": max(self.progress), "preparedFrame": self.prepared_frame, "heldFrame": self.held_frame,
                "release": self.release, "nativeFlightFrames": self.flight_frames, "catchFrame": self.caught_frame,
                "afterIngredientIds": self.expected, "otherBowlStayedEmpty": True, "originalEmptyBowlRestored": True,
                "finalNativeDonutComposition": basket["composition"], "finalGameplayFrame": state["gameplayFrame"], "fourControlledEmptyNeutralChefs": True}


def main():
    p = argparse.ArgumentParser(description=__doc__)
    for arg in ["trace", "plan", "result", "output"]:
        p.add_argument(arg, type=Path)
    p.add_argument("--controller-bundle", type=Path, required=True)
    args = p.parse_args()
    plan = json.loads(args.plan.read_text(encoding="utf-8-sig"))
    require([j["id"] for j in plan["jobs"]] == JOBS and plan["timeoutFrames"] == 3000, "Unexpected authored plan")
    checker = Checker(plan["jobs"][2]["actions"][0]["station"])
    evidence = controller_evidence(args.controller_bundle)
    first_stat, plugin, manifest, previous_frame, calls = args.trace.stat(), None, None, None, 0
    with gzip.open(args.trace, "rt", encoding="utf-8-sig") as stream:
        for line in stream:
            row = json.loads(line)
            if row.get("kind") == "event":
                checker.event(row["name"], row.get("value") or {})
            elif row.get("kind") == "call":
                calls += 1
                request, response = row["request"], row["response"]
                ordinary_request(request)
                require(request["command"] != "restart" or calls == 1, "Restart occurred after probe start")
                require(response.get("ok"), "Native call failed")
                state = response["state"]
                require(previous_frame is None or state["gameplayFrame"] == previous_frame + (1 if request["command"] == "step" else 0), "Native frame sequence has a gap")
                previous_frame = state["gameplayFrame"]
                current_plugin = state["instrumentation"]["manifest"]["pluginSha256"]
                current_manifest = state["instrumentation"]["manifestSha256"]
                if plugin is None:
                    plugin, manifest = current_plugin, current_manifest
                require(plugin == current_plugin and manifest == current_manifest and state["captureFramerate"] == 60 and state["fixedDeltaTime"] == .02 and not state["timerSuppressed"], "Native instrumentation or timing configuration changed")
                checker.state(state, response["inputs"])
    result = json.loads(args.result.read_text(encoding="utf-8-sig"))
    require(result.get("ok") and result.get("paused") and result.get("state") == checker.last, "Closed result differs from final native trace state")
    last_stat = args.trace.stat()
    require((first_stat.st_size, first_stat.st_mtime_ns) == (last_stat.st_size, last_stat.st_mtime_ns), "Trace changed during checking")
    proof = checker.finish()
    proof.update({"sourceTrace": pin(args.trace), "route": pin(args.plan), "result": pin(args.result), "checkerSource": pin(Path(__file__)),
                  "controllerBundleEvidence": evidence, "pluginSha256": plugin, "instrumentationManifestSha256": manifest})
    args.output.write_text(json.dumps(proof, indent=2), encoding="utf-8")
    print(json.dumps({"ok": True, "output": str(args.output), "source": checker.source, "catchFrame": checker.caught_frame, "finalFrame": checker.last["gameplayFrame"]}))


if __name__ == "__main__":
    main()
