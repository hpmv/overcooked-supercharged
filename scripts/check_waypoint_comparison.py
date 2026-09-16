"""Independent file-only comparison of native ordinary-input waypoint A/B probes."""
import argparse
import collections
import gzip
import hashlib
import json
import math
from pathlib import Path

from check_bowl_offmix import controller_evidence, ingredients, ordinary_request, pin, require


def canon(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":"), allow_nan=False)


def point(value):
    return (value.get("x", value.get("X")), value.get("z", value.get("Z")))


def distance(a, b):
    return math.hypot(a[0] - b[0], a[1] - b[1])


def neutral(pad):
    return pad["x"] == pad["y"] == 0 and not any(pad[k] for k in ("pickup", "use", "dash"))


def diff_summary(left, right):
    counts = collections.Counter()
    examples = []

    def visit(a, b, path):
        if a == b:
            return
        if isinstance(a, dict) and isinstance(b, dict):
            for key in sorted(a.keys() | b.keys()):
                visit(a.get(key), b.get(key), path + [str(key)])
        elif isinstance(a, list) and isinstance(b, list) and len(a) == len(b):
            for i, (x, y) in enumerate(zip(a, b)):
                visit(x, y, path + [str(i)])
        else:
            counts[path[0] if path else "root"] += 1
            if len(examples) < 30:
                examples.append({"path": ".".join(path), "baseline": a, "continued": b})
    visit(left, right, [])
    return {"equal": left == right, "differingLeavesByTopLevelField": dict(counts), "firstDifferences": examples}


def check_routes(baseline, continued):
    a = json.loads(baseline.read_text(encoding="utf-8-sig"))
    b = json.loads(continued.read_text(encoding="utf-8-sig"))
    actions_a = a["jobs"][0]["actions"]
    actions_b = b["jobs"][0]["actions"]
    require(len(a["jobs"]) == len(b["jobs"]) == 1 and len(actions_a) == len(actions_b) == 7, "Unexpected probe job/action count")
    require([(x["type"], str(x["station"])) for x in actions_a] ==
            [("take", "38"), ("navigate", "72"), ("navigate", "49"), ("navigate", "74"), ("navigate", "48"), ("navigate", "32"), ("place", "38")], "Unexpected native circuit")
    for old, new in zip(actions_a, actions_b):
        require(old.get("continuousWaypoints") is False and new.get("continuousWaypoints") is True, "Missing explicit A/B flag")
        old.pop("continuousWaypoints"); new.pop("continuousWaypoints")
    a.pop("description", None); b.pop("description", None)
    require(a == b, "Executable routes differ beyond the continuation flag")
    return actions_b


class Probe:
    def __init__(self, enabled, expected_actions):
        self.enabled = enabled
        self.expected_actions = expected_actions
        self.initial = self.last = None
        self.samples = {}
        self.events = collections.Counter()
        self.continued = []
        self.actions = []
        self.current_action = None
        self.current_target = None
        self.settled = []
        self.calls = self.steps = self.pickup_edges = 0
        self.plate_phase = 0
        self.pickup_frame = self.return_frame = None
        self.mapping = {}
        self.plugin = self.manifest = None
        self.input_hash = hashlib.sha256()
        self.maximum_speed = self.maximum_step = self.maximum_other_displacement = 0
        self.small_step_residuals = []
        self.raw_initial_response = None

    def event(self, name, value):
        require(self.last is not None, "Event appears before native initial observation")
        self.events[name] += 1
        frame = self.last["gameplayFrame"]
        if name == "actionCreated":
            self.current_action = value
            self.current_target = None
        elif name == "pathPlanned":
            require(value["path"]["Success"], "Native route reports an unsuccessful planned path")
            self.current_target = point(value["path"]["Points"][-1])
        elif name == "navigationIntermediateContinued":
            require(self.enabled and self.current_action is not None and self.current_action.get("continuousWaypoints") is True,
                    "Continuation appeared in an unenabled action")
            self.continued.append({"frame": frame, "action": self.current_action.copy(), **value})
        elif name == "actionStage" and value.get("stage") == "face":
            self.check_settled(frame, "final-navigation-before-native-interaction")
        elif name == "actionComplete":
            actual = value["action"].copy()
            require(actual.pop("continuousWaypoints") is self.enabled and actual.pop("player") == 0, "Action flag/player changed")
            require(actual == self.expected_actions[len(self.actions)], "Emitted completed action differs from authored route")
            self.actions.append({"frame": frame, "actionFrames": value["frames"], "type": actual["type"], "station": actual["station"]})
            if actual["type"] == "navigate":
                self.check_settled(frame, "final-navigate-completion")
        elif name in ("actionFailed", "jobFailed", "plannerFailure", "pathReplan", "navigationBrakeBlocked", "navigationWaiting"):
            raise AssertionError("Probe encountered an unresolved/replanned navigation event: " + name)

    def check_settled(self, frame, reason):
        current = self.samples[frame]
        require(self.current_target is not None and frame >= 2, "Missing final target or prior settle samples")
        recent = [self.samples[frame - offset] for offset in (2, 1, 0)]
        target_error = distance(current["position"], self.current_target)
        maximum_motion = max(distance(recent[i]["position"], recent[i - 1]["position"]) for i in (1, 2))
        require(target_error <= .10001 and maximum_motion < .002 and
                all(distance(x["cachedVelocity"], (0, 0)) < .05 and neutral(x["input"]) for x in recent),
                "Final station approach was not physically settled with neutral native inputs")
        self.settled.append({"frame": frame, "reason": reason, "station": self.current_action["station"],
                             "target": self.current_target, "actualPosition": current["position"], "targetError": target_error,
                             "maximumMotionInThreeSamples": maximum_motion})

    def call(self, request, response):
        ordinary_request(request)
        allowed = {"restart": {"version", "command", "seed", "isolateRecipeRandom"}, "inspect": {"version", "command"},
                   "step": {"version", "command", "steps", "inputs"}}[request["command"]]
        require(set(request) <= allowed and response.get("ok") and response.get("paused"), "Unexpected request fields, failed call or unpaused response")
        self.calls += 1
        require(self.calls <= 3003, "Probe exceeded its authored bounded frame budget")
        if request["command"] == "restart":
            require(self.calls == 1 and request.get("seed") == 0 and request.get("isolateRecipeRandom") is True, "Unexpected restart or seed change")
        state = response["state"]
        frame = state["gameplayFrame"]
        session = json.loads(response["session"])
        require(session["variantPlayers"] == session["virtualPads"] == session["serverUsers"] == session["clientUsers"] == 4 and session["dlc"] == 8,
                "Native session is not four-player Carnival")
        require(state["scene"] == "s_Day_3_4" and state["roundDuration"]["valuesAgree"] and state["roundDuration"]["seconds"] == 270 and
                state["roundDuration"]["levelConfigName"] == "Day_3_4_4P", "Wrong native scene or duration")
        require(state["captureFramerate"] == 60 and state["fixedDeltaTime"] == .02 and not state["timerSuppressed"] and
                state["serverRoundActive"] and state["clientRoundActive"], "Native timing or round state changed")
        require(all(state[k] == 0 for k in ("score", "baseScore", "tips", "delivered", "deductions")), "Navigation circuit changed the native score ledger")
        ins = state["instrumentation"]
        plugin, manifest = ins["manifest"]["pluginSha256"], ins["manifestSha256"]
        if self.plugin is None:
            self.plugin, self.manifest = plugin, manifest
        require((plugin, manifest) == (self.plugin, self.manifest), "Native instrumentation identity changed")
        chefs = {c["playerId"]: c for c in state["chefs"]}
        pads = {p["player"]: p for p in response["inputs"]}
        require(set(chefs) == set(pads) == {0, 1, 2, 3}, "Missing native chef/input slot")
        for player, pad in pads.items():
            require(not pad["use"] and not pad["dash"] and (player == 0 or neutral(pad)), "Nonordinary circuit input or other chef movement")
        chef = chefs[0]
        entities = {e["id"]: e for e in state["entities"]}
        if self.initial is None:
            require(frame == 0 and request["command"] == "restart", "Probe lacks a fresh native frame zero")
            self.initial = state
            self.raw_initial_response = response
            source = entities[38]; plate = entities[source["attachedEntityId"]]
            require(plate["id"] == 10 and "Plate" in plate["components"] and not ingredients(plate.get("composition")), "Original plate10 missing from source38")
            for identity in (10, 38, 72, 49, 74, 48, 32):
                entity = entities[identity]
                self.mapping[identity] = {"ordinal": entity["observedOrdinal"], "position": point(entity["position"]),
                                          "components": entity["components"], "attachment": entity["attachedEntityId"]}
        for identity, observed in self.mapping.items():
            entity = entities.get(identity)
            require(entity and entity["active"] and entity["observedOrdinal"] == observed["ordinal"] and entity["components"] == observed["components"], "Native source/plate/target incarnation changed")
            if identity != 10:
                require(distance(point(entity["position"]), observed["position"]) < 1e-5, "Original target station moved")
                if identity != 38:
                    require(entity["attachedEntityId"] == observed["attachment"], "Non-source station contents changed")
        require(not ingredients(entities[10].get("composition")), "Original empty plate gained food")
        held = chef["heldEntityId"]
        attached = entities[38]["attachedEntityId"]
        require(held in (0, 10) and not any(c["heldEntityId"] == 10 for p, c in chefs.items() if p != 0), "Another object or chef replaced original carried plate")
        if self.plate_phase == 0 and held == 10:
            self.plate_phase = 1; self.pickup_frame = frame
            require(self.last and pads[0]["pickup"] and not self.samples[frame - 1]["input"]["pickup"] and
                    self.samples[frame - 1]["pickupTarget"] in (10, 38), "Plate pickup lacks prior native target and fresh input edge")
        if self.plate_phase == 1 and held == 0:
            self.plate_phase = 2; self.return_frame = frame
            require(pads[0]["pickup"] and not self.samples[frame - 1]["input"]["pickup"] and self.samples[frame - 1]["placementTarget"] == 38,
                    "Plate return lacks prior native target and fresh input edge")
        require((self.plate_phase == 1 and held == 10 and attached == 0) or (self.plate_phase in (0, 2) and held == 0 and attached == 10),
                "Original plate was dropped or staged outside source38")
        require(chef["controlsEnabled"] and chef["directlyControlled"] and chef["canAcceptInput"] and not chef["respawning"] and
                not chef["aimingThrow"] and not chef["inputSuppressed"] and chef["impactTimer"] < 0 and chef["dashTimer"] < 0,
                "Native impact, respawn, throw, dash or closed controls occurred")
        cached = point(chef["lastVelocity"])
        self.maximum_speed = max(self.maximum_speed, distance(cached, (0, 0)))
        require(self.maximum_speed <= 6.0001, "Native cached speed exceeded ordinary walking")
        for player in (1, 2, 3):
            initial_chef = next(c for c in self.initial["chefs"] if c["playerId"] == player)
            movement = distance(point(chefs[player]["position"]), point(initial_chef["position"]))
            self.maximum_other_displacement = max(self.maximum_other_displacement, movement)
            require(movement < .002 and chefs[player]["heldEntityId"] == 0, "Uncontrolled chef changed position or held item")
        if self.last is not None:
            step = int(request["command"] == "step")
            require(frame == self.last["gameplayFrame"] + step, "Noncontiguous native input frames")
            if step:
                self.steps += 1
                requested = {pad["player"]: pad for pad in request["inputs"]}
                require(all(all(requested[p][k] == pads[p][k] for k in ("pickup", "use", "dash")) and
                            all(abs(requested[p][k] - pads[p][k]) <= .000001 for k in ("x", "y")) for p in pads),
                        "Native float axes or exact logical buttons differ from ordinary request")
                self.input_hash.update((canon(request) + "\n").encode())
                require(state["clientTime"] > self.last["clientTime"] and state["timer"] < self.last["timer"], "Native clocks did not advance monotonically")
                expected_physics = self.last["framesSinceNoPhysics"] != 5
                require(state["gameplayFixedFrame"] - self.last["gameplayFixedFrame"] == int(expected_physics) and
                        state["framesSinceNoPhysics"] == (self.last["framesSinceNoPhysics"] + 1) % 6, "Native60/50 physics phase differs from queued-step schedule")
                displacement = distance(point(chef["position"]), self.samples[frame - 1]["position"])
                self.maximum_step = max(self.maximum_step, displacement)
                if displacement > .12001:
                    self.small_step_residuals.append({"frame": frame, "displacement": displacement, "overNominalPoint12": displacement - .12})
                require(displacement <= .1201 and (expected_physics or displacement < 1e-5),
                        f"Observed displacement exceeds one ordinary native fixed step at{frame}: {displacement}, physics={expected_physics}")
                if pads[0]["pickup"] and not self.samples[frame - 1]["input"]["pickup"]:
                    self.pickup_edges += 1
        self.samples[frame] = {"position": point(chef["position"]), "cachedVelocity": cached, "phase": state["framesSinceNoPhysics"],
                               "physicsFrame": state["gameplayFixedFrame"], "input": pads[0], "pickupTarget": chef["pickupTargetId"],
                               "placementTarget": chef["placementTargetId"], "impactTimer": chef["impactTimer"], "held": held}
        self.last = state

    def finish(self, result):
        require(result.get("ok") and result.get("paused") and result["state"] == self.last, "Closed result differs from final native observation")
        require(self.plate_phase == 2 and self.pickup_edges == 2 and len(self.actions) == len(self.settled) == 7 and self.events["jobComplete"] == 1,
                "Native plate lifecycle, actions or settled targets are incomplete")
        require(all(neutral(p) for p in result["inputs"]), "Final native input is not neutral")
        predictions = []
        for event in self.continued:
            frame = event["frame"]; before = self.samples[frame]; after = self.samples[frame + 1]
            require(before["held"] == 10 and point(event["position"]) == before["position"] and
                    point(event["cachedVelocity"]) == before["cachedVelocity"], "Continued corner lacks exact observed held-plate motion")
            physics = before["phase"] != 5
            expected = tuple(before["position"][i] + (before["cachedVelocity"][i] * .02 if physics else 0) for i in (0, 1))
            require(event["nextFrameHasPhysics"] == physics and distance(expected, point(event["queuedPosition"])) < 1e-8,
                    "Logged queued prediction differs from independent native phase/velocity arithmetic")
            queued_error = distance(after["position"], expected)
            require(queued_error < .00002 and not neutral(after["input"]) and after["held"] == 10 and after["impactTimer"] < 0,
                    "Next native position or continued movement disagrees with queued prediction")
            target = point(event["nextWaypoint"])
            length = distance(before["position"], target)
            expected_direction = tuple((target[i] - before["position"][i]) / length for i in (0, 1))
            analytic_new_step = tuple(expected[i] + expected_direction[i] * .12 for i in (0, 1))
            require(distance(analytic_new_step, point(event["firstNewDirectionStep"])) < 1e-8,
                    "Logged first-new-direction prediction differs from independently normalized native walking")
            stick = (after["input"]["x"], -after["input"]["y"])
            require(distance(stick, expected_direction) < .00001, "Continued input does not face the observed next waypoint")
            next_physics_frame = frame + 2
            while self.samples[next_physics_frame]["physicsFrame"] == after["physicsFrame"]:
                next_physics_frame += 1
            actual_new_step = self.samples[next_physics_frame]
            immediately_before_physics = self.samples[next_physics_frame - 1]
            updated_prediction = tuple(immediately_before_physics["position"][i] + immediately_before_physics["cachedVelocity"][i] * .02 for i in (0, 1))
            updated_error = distance(updated_prediction, actual_new_step["position"])
            require(updated_error < .00002, "First later native step differs from the immediately preceding observed cached velocity")
            predictions.append({"frame": frame, "action": event["action"], "phase": before["phase"], "nextFrameHasPhysics": physics,
                                "position": before["position"], "cachedVelocity": before["cachedVelocity"], "predictedQueuedPosition": expected,
                                "actualNextFramePosition": after["position"], "queuedError": queued_error,
                                "firstNewDirectionPrediction": point(event["firstNewDirectionStep"]), "firstLaterPhysicsFrame": next_physics_frame,
                                "actualFirstLaterPhysicsPosition": actual_new_step["position"],
                                "firstNewDirectionError": distance(actual_new_step["position"], point(event["firstNewDirectionStep"])),
                                "updatedPredictionUsingLastObservedVelocity": updated_prediction, "updatedPredictionError": updated_error,
                                "interveningNoPhysicsFrames": next_physics_frame - frame - 2,
                                "note": "Direction can be recomputed during an intervening no-physics Update; only the immediately queued old-velocity step is asserted exact.",
                                "nativeImpactObserved": False})
        elapsed = self.last["gameplayFrame"] - self.initial["gameplayFrame"]
        client_elapsed = self.last["clientTime"] - self.initial["clientTime"]
        require(abs(client_elapsed - elapsed / 60) < .00002, "Native elapsed client clock differs from recorded logical input frames")
        return {"passed": True, "frames": elapsed, "seconds": elapsed / 60, "nativeClientElapsed": client_elapsed,
                "nativeTimerElapsed": self.initial["timer"] - self.last["timer"], "calls": self.calls, "unitInputSteps": self.steps,
                "inputRequestSha256": self.input_hash.hexdigest(), "pickupFrame": self.pickup_frame, "returnFrame": self.return_frame,
                "originalPlate": 10, "originalSource": 38, "score": self.last["score"], "continuedCorners": predictions,
                "eventCounts": dict(self.events), "completedActions": self.actions, "settledTargets": self.settled,
                "maximumNativeCachedSpeed": self.maximum_speed, "maximumNativePositionStep": self.maximum_step,
                "nativeStepsAbovePoint12001": self.small_step_residuals,
                "maximumOtherChefDisplacement": self.maximum_other_displacement,
                "pluginSha256": self.plugin, "instrumentationManifestSha256": self.manifest}


def read_probe(trace, result_path, enabled, actions):
    before = pin(trace)
    checker = Probe(enabled, actions)
    with gzip.open(trace, "rt", encoding="utf-8-sig") as stream:
        for line in stream:
            row = json.loads(line)
            if row.get("kind") == "call":
                checker.call(row["request"], row["response"])
            elif row.get("kind") == "event":
                checker.event(row["name"], row.get("value") or {})
    result = json.loads(result_path.read_text(encoding="utf-8-sig"))
    report = checker.finish(result)
    require(before == pin(trace), "Trace changed while being checked")
    report.update(trace=before, result=pin(result_path))
    return checker, report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", type=Path, default=Path("artifacts/waypoint-baseline-a.jsonl.gz"))
    parser.add_argument("--continued", type=Path, default=Path("artifacts/waypoint-continued-a.jsonl.gz"))
    parser.add_argument("--baseline-result", type=Path, default=Path("artifacts/waypoint-baseline-a-result.json"))
    parser.add_argument("--continued-result", type=Path, default=Path("artifacts/waypoint-continued-a-result.json"))
    parser.add_argument("--baseline-route", type=Path, default=Path("routes/probes/waypoint-baseline.json"))
    parser.add_argument("--continued-route", type=Path, default=Path("routes/probes/waypoint-continued.json"))
    parser.add_argument("--controller-bundle", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    require(not args.output.exists(), "Comparison output already exists")
    actions = check_routes(args.baseline_route, args.continued_route)
    a, baseline = read_probe(args.baseline, args.baseline_result, False, actions)
    b, continued = read_probe(args.continued, args.continued_result, True, actions)
    require(baseline["pluginSha256"] == continued["pluginSha256"] and baseline["instrumentationManifestSha256"] == continued["instrumentationManifestSha256"],
            "A/B native instrumentation differs")
    geometry_equal = a.mapping == b.mapping and [c["position"] for c in a.initial["chefs"]] == [c["position"] for c in b.initial["chefs"]]
    require(geometry_equal, "A/B initial measured station or chef geometry differs")
    saved = baseline["frames"] - continued["frames"]
    report = {"passed": True, "classification": "One native A/B navigation mechanism comparison; different raw initial epochs; no full planner or high-score claim",
              "baseline": baseline, "continued": continued, "savedFrames": saved, "savedSeconds": saved / 60,
              "relativeFrameReductionPercent": saved * 100 / baseline["frames"], "executableRouteDiffIsOnlyFlag": True,
              "initialMeasuredGeometryEqual": geometry_equal, "rawInitialStateComparison": diff_summary(a.initial, b.initial),
              "initialClockSummary": [{k: c.initial.get(k) for k in ("gameplayFrame", "frame", "fixedFrame", "clientTime", "clientDeltaTime", "timer", "framesSinceNoPhysics", "levelStartPhysicsPhase")} for c in (a, b)],
              "baselineRoute": pin(args.baseline_route), "continuedRoute": pin(args.continued_route),
              "controllerBundleEvidence": controller_evidence(args.controller_bundle), "checkerSource": pin(Path(__file__)),
              "sharedValidationSource": pin(Path(__file__).with_name("check_bowl_offmix.py")),
              "limits": ["Operator selects the executing frozen binary; the native trace does not independently identify the controller executable.",
                         "Requested double stick axes are compared to native float JSON within1e-6; player and logical button values must match exactly.",
                         "Observed no-impact and predicted next-position agreement are checked independently; this checker does not reimplement every Unity collider or prove all possible corner trajectories.",
                         "Per-action performance can differ by a frame after the path timing changes; six-phase offline fixtures and this one native A/B circuit have different scopes."]}
    args.output.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({k: report[k] for k in ("passed", "savedFrames", "savedSeconds", "relativeFrameReductionPercent", "initialMeasuredGeometryEqual")}))


if __name__ == "__main__":
    main()
