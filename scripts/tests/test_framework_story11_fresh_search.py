"""Offline fresh-load protocol and exact native preparation-proof tests; no ports."""
import copy
import json
from pathlib import Path
import sys
import tempfile
from types import SimpleNamespace
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/"scripts"))
import framework_story11_fresh_search as f
from framework_story11_planner import Observation, Blocked
from test_framework_story11 import fixture, staged, observation


def pads(count, start=31):
    return [{"ordinal": i, "nextFrame": start+i+1, "inputs": {
        str(c): {"Pad": {"X": 0, "Y": 0}, **{b: {"Down": False, "JustPressed": False, "JustReleased": False}
                   for b in ("Pickup", "Interact", "Dash")}} for c in (43, 44, 45, 46)}} for i in range(count)]


def complete_bridge(receipt, epoch=200):
    receipt["bridge"].update(readyUnityFrame=epoch, loading=False, inputBlocked=True,
        screenWidth=1280, screenHeight=720, fullScreen=False, runInBackground=True,
        captureFramerate=60, fixedDeltaTime=.02, authoringClockRestores=5,
        nativeCheckpoints={"restoreAttempts": 7, "nativeServerClock": [0, 1, 2], "nativeClientClock": list(range(6))},
        logicalClock={"eligibleTicks": 1, "step": 1/60}, logicalRealtime=10,
        nativePhysics={"bodies": [{"entityId": 43, "bodyInstanceId": 100}]})
    receipt["bridge"]["nativeRound"]["recipeRandom"] = {"seed": 0, "authoringWarpCount": 0,
        "generator": "ScriptedRoundData.GetNextRecipe", "isolatedPerRound": True}
    return receipt


def policy():
    rows = [{"phase": p, "config": "Sushi_1_1S_4P", "original": 1, "after": 0,
             "nativeDurationSeconds": 150, "assetFileWritten": False} for p in
            ("before-native-load", "ServerCampaignMode.Begin-prefix", "ClientCampaignMode.Begin-prefix")]
    return {"detail": {"result": {"stage": "complete", "pending": False, "error": "", "seed": 0,
        "scene": "s_sushi_1_1", "dlc": -1, "selected": {"Config": "Sushi_1_1S_4P", "Scene": "s_sushi_1_1", "Players": 4},
        "timerPolicy": {"installed": True, "immediateStory11Timer": True, "receipts": rows}}}}


class FakeFreshProtocol(f.FreshRunner):
    """Fake RPCs expose a loader transition; native observation rules stay real."""
    def __init__(self, out, stale=False, uncleared=False):
        args = SimpleNamespace(out=out, search=True, trace=out/"trace.jsonl", load_timeout=10, scene="s_sushi_1_1")
        self.calls, self.polls = [], 0
        self.stale, self.uncleared = stale, uncleared
        super().__init__(args, None, None, None)
        self.s, self.r = fixture()
        self.s.update(preventInvalidState=False, freshLevelLoadObserved=True, needsFreshLevelBaseline=False)
        self.s["frame"] = 1
        self.s["actionGraph"] = {"chefs": [{"actions": [{"id": "old-spawn-owner"}]}]}
        self.r["bridge"]["nativeRound"]["elapsed"] = .0333333351
        complete_bridge(self.r)

    def save(self): pass

    def call(self, target, request, label):
        self.calls.append((target, copy.deepcopy(request)))
        command = request["command"]
        if command == "pause":
            bridge = copy.deepcopy(self.r["bridge"]); bridge["readyUnityFrame"] = 100
            return {"bridge": bridge}
        if command == "hot-call":
            if request["operation"] == "load-main-1-1":
                return {"detail": {"result": {"pending": True}}}
            return policy()
        if command == "status":
            self.polls += 1
            bridge = copy.deepcopy(self.r["bridge"])
            if self.polls == 1:
                bridge.update(readyUnityFrame=-1, loading=True, loadComplete=False)
            elif self.stale:
                bridge["readyUnityFrame"] = 100
            return {"bridge": bridge}
        if command == "actions-clear":
            if not self.uncleared:
                self.s["actionGraph"] = {"chefs": []}
            return {"ok": True}
        if command == "arm":
            return {"ok": True}
        raise AssertionError(request)

    def settled(self, label): return copy.deepcopy(self.s)
    def observe(self, label): return observation(copy.deepcopy(self.s), copy.deepcopy(self.r))

    def execute(self, request, label, start):
        self.calls.append(("controller", request))
        self.s["frame"] += 30
        self.r["bridge"]["nativeRound"]["elapsed"] = .5333335
        return self.observe(label), pads(30, start.frame)


class FreshSearchTests(unittest.TestCase):
    def test_recorded_story11_policy_and_four_candidates_use_actual_native_schema(self):
        path = ROOT/"artifacts/framework-migration/story11-idle-parity-b/observations.json"
        rows = json.loads(path.read_text())
        state = next(r["response"] for r in rows if r.get("label") == "baseline")
        receipt = [r["response"] for r in rows if r.get("label") == "baseline-native"][-1]
        observed = observation(state, receipt)
        f.native_policy(observed)
        self.assertEqual(observed.chefs, [43, 44, 45, 46])
        self.assertEqual(len(f.candidates(observed)), 4)
        # This historical frame61 tests only the native schema/policy. It is not
        # relabeled as the new driver's required fresh frame0..2 boundary.
        self.assertEqual(observed.frame, 61)

    def test_fenced_loader_polls_same_owner_clears_only_after_new_load_then_neutral_warmup(self):
        with tempfile.TemporaryDirectory() as tmp, patch.object(f.time, "sleep"):
            runner = FakeFreshProtocol(Path(tmp))
            base, epoch = runner.fresh("a")
            self.assertEqual((base.frame, epoch), (31, 200))
            commands = [r["command"] for _, r in runner.calls]
            self.assertEqual(commands, ["pause", "hot-call", "status", "status", "actions-clear", "hot-call", "arm", "step"])
            load = runner.calls[1][1]
            self.assertEqual(load, {"command": "hot-call", "slot": "level-session", "operation": "load-main-1-1", "args": {"seed": 0}})
            self.assertEqual(runner.summary["freshLoads"][0]["warmupInputs"]["frames"], 30)
            self.assertFalse(any(c in ("warp", "restart", "load", "checkpoint") for c in commands))

    def test_stale_load_and_uncleared_graph_fail_before_arm(self):
        for option in ("stale", "uncleared"):
            with self.subTest(option=option), tempfile.TemporaryDirectory() as tmp, patch.object(f.time, "sleep"):
                runner = FakeFreshProtocol(Path(tmp), **{option: True})
                with self.assertRaises(ValueError): runner.fresh("a")
                self.assertNotIn("arm", [r["command"] for _, r in runner.calls])

    def test_actual_raw_workable_goal_null_composition_survives_named_fresh_projection(self):
        s, r, c = staged(); complete_bridge(r)
        a = observation(s, r); goal = f.staged_proof(a, c)
        sr, rr = copy.deepcopy(s), copy.deepcopy(r)
        for e in sr["entities"]:
            if e["id"] == 51: e["id"] = 99
        for e in sr["registry"]:
            if e["EntityId"] == 51: e["EntityId"] = 99
        for e in rr["detail"]["entities"]:
            if e["id"] == 51: e["id"] = 99
        rr["bridge"]["logicalRealtime"] += 100
        rr["bridge"]["nativePhysics"]["bodies"][0]["bodyInstanceId"] += 1
        b = observation(sr, rr); repeated = f.staged_proof(b, c)
        report = f.fresh_goal_comparison(goal, repeated, a, b, pads(60), pads(60))
        self.assertTrue(report["passed"])
        self.assertEqual((report["originalRawId"], report["replayRawId"]), (51, 99))
        self.assertIsNone(goal["rawWorkable"]["composition"])
        raw = f.raw_differences(a, b)
        self.assertIsNotNone(raw["nativePhysics"])
        self.assertIsNotNone(raw["privateClocks"])

    def test_changed_workstage_recipe_inputs_or_native_duration_reject_replay(self):
        s, r, c = staged(); complete_bridge(r)
        a = observation(s, r); goal = f.staged_proof(a, c)
        for name in ("stage", "recipe", "seconds", "input", "path"):
            with self.subTest(name=name):
                g, receipt, rows = copy.deepcopy(goal), copy.deepcopy(r), pads(60)
                if name == "stage": g["rawWorkable"]["workStage"] = 1
                if name == "recipe": receipt["bridge"]["nativeRound"]["orders"][0]["recipeId"] += 1
                if name == "seconds": g["nativeSeconds"] += .0000001
                if name == "input": rows[0]["inputs"]["43"]["Pad"]["X"] = .1
                if name == "path": g["rawPath"] = [30, 1]
                b = observation(copy.deepcopy(s), receipt)
                self.assertFalse(f.fresh_goal_comparison(goal, g, a, b, pads(60), rows)["passed"])

    def test_same_epoch_seed_window_ledger_and_authoring_counters_are_required(self):
        s, r = fixture(); complete_bridge(r)
        s.update(preventInvalidState=False, freshLevelLoadObserved=True, needsFreshLevelBaseline=False)
        original = f.counters(r["bridge"])
        f.same_round(observation(s, r), 200, original)
        for name in ("epoch", "seed", "warp", "ledger", "window", "counter", "generator", "correction"):
            with self.subTest(name=name):
                b = copy.deepcopy(r); native = b["bridge"]
                if name == "epoch": native["readyUnityFrame"] += 1
                if name == "seed": native["nativeRound"]["recipeRandom"]["seed"] = 1
                if name == "warp": native["nativeRound"]["recipeRandom"]["authoringWarpCount"] = 1
                if name == "ledger": native["nativeRound"]["ledger"]["deliveries"] = 1
                if name == "window": native["fullScreen"] = True
                if name == "counter": native["authoringClockRestores"] += 1
                if name == "generator": native["nativeRound"]["recipeRandom"]["generator"] = "replacement"
                state = copy.deepcopy(s)
                if name == "correction": state["preventInvalidState"] = True
                with self.assertRaises(Blocked): f.same_round(observation(state, b), 200, original)

    def test_unsettled_execution_failure_preserves_raw_inputs_before_next_reload(self):
        with tempfile.TemporaryDirectory() as tmp:
            out = Path(tmp); trace = out/"trace.jsonl"; trace.write_text("")
            args = SimpleNamespace(out=out, trace=trace, search=True, scene="s_sushi_1_1")
            runner = f.FreshRunner(args, None, None, None)
            s, r = fixture(); base = observation(s, r); case = f.candidates(base)[0]
            calls = []
            runner.call = lambda target, request, label: calls.append(request)
            def failure(*args):
                trace.write_text(json.dumps({"kind": "exchange", "input": {"NextFrame": 32, "Input": pads(1)[0]["inputs"]}})+"\n")
                raise RuntimeError("bounded native controller timeout")
            with patch.object(f.Runner, "execute", side_effect=failure):
                trial, record = runner.attempt(case, base, 200, "failure")
            self.assertFalse(trial["eligible"]); self.assertIsNone(record)
            saved = json.loads((out/"failure-failure-inputs.json").read_text())
            self.assertEqual(saved["validation"], "failure-only-unqualified")
            self.assertEqual(len(saved["rawExchanges"]), 1)
            self.assertEqual(calls, [{"command": "pause"}])

    def test_four_trials_then_fifth_load_replay_and_failed_trial_does_not_rank(self):
        with tempfile.TemporaryDirectory() as tmp:
            runner = FakeFreshProtocol(Path(tmp))
            state, receipt = fixture(); complete_bridge(receipt)
            base = observation(state, receipt); cases = f.candidates(base)
            _, er, _ = staged(); complete_bridge(er)
            end = SimpleNamespace(frame=88, round=er["bridge"]["nativeRound"], receipt=er,
                                  state={"rawInput": {"outcome": "complete"}})
            loads, attempts = [], []
            def fresh(label): loads.append(label); return base, 200
            def attempt(case, start, epoch, label):
                index = len(attempts); attempts.append(case["id"])
                if index == 0: return {"id": case["id"], "eligible": False, "error": "native timeout"}, None
                goal = {"frames": 60-index, "nativeSeconds": 1-index/60}
                return {"id": case["id"], "eligible": True, "goal": goal}, (case, base, end, pads(60-index))
            runner.fresh, runner.attempt = fresh, attempt
            runner.execute = lambda req, label, start: (end, pads(57))
            with patch.object(f, "raw_differences", return_value={}), patch.object(f, "same_round"), \
                 patch.object(f, "staged_proof", return_value={}), \
                 patch.object(f, "fresh_goal_comparison", return_value={"passed": True}):
                runner.run()
            self.assertEqual(len(loads), 5)
            self.assertEqual(len(attempts), 4)
            self.assertEqual(runner.summary["selected"], cases[3]["id"])
            self.assertTrue(runner.summary["passed"])
            self.assertFalse(runner.summary["trials"][0]["eligible"])
            resume = json.loads((Path(tmp)/"selected-replay-cases.json").read_text())
            self.assertEqual(resume, [cases[3]])
            self.assertEqual(runner.summary["stagedResume"]["candidate"], 0)


if __name__ == "__main__": unittest.main()
