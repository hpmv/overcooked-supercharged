"""Negative tests on the recorded native A/B checker; no game I/O."""
import copy
import json
import tempfile
import unittest
from pathlib import Path

from check_waypoint_comparison import Probe, check_routes, diff_summary, read_probe


class WaypointComparisonTests(unittest.TestCase):
    def clone(self):
        return copy.deepcopy(self.candidate, {id(self.candidate.input_hash): self.candidate.input_hash.copy()})

    @classmethod
    def setUpClass(cls):
        cls.actions = check_routes(Path("routes/probes/waypoint-baseline.json"), Path("routes/probes/waypoint-continued.json"))
        cls.candidate, cls.report = read_probe(Path("artifacts/waypoint-continued-a.jsonl.gz"), Path("artifacts/waypoint-continued-a-result.json"), True, cls.actions)
        cls.result = json.loads(Path("artifacts/waypoint-continued-a-result.json").read_text(encoding="utf-8-sig"))

    def test_native_positive(self):
        self.assertEqual(self.report["frames"], 384)
        self.assertEqual(len(self.report["continuedCorners"]), 2)
        self.assertEqual(len(self.report["settledTargets"]), 7)

    def test_changed_queued_prediction(self):
        q = self.clone()
        q.continued[0]["queuedPosition"]["X"] += .01
        with self.assertRaisesRegex(ValueError, "queued prediction"):
            q.finish(self.result)

    def test_changed_new_direction_prediction(self):
        q = self.clone()
        q.continued[0]["firstNewDirectionStep"]["X"] += .01
        with self.assertRaisesRegex(ValueError, "first-new-direction"):
            q.finish(self.result)

    def test_wrong_next_native_position(self):
        q = self.clone()
        x, z = q.samples[30]["position"]
        q.samples[30]["position"] = (x + .01, z)
        with self.assertRaisesRegex(ValueError, "Next native position"):
            q.finish(self.result)

    def test_native_impact(self):
        q = self.clone()
        q.samples[30]["impactTimer"] = .3
        with self.assertRaisesRegex(ValueError, "Next native position"):
            q.finish(self.result)

    def test_unsettled_final_target(self):
        q = self.clone()
        target = self.report["settledTargets"][1]
        q.current_target = tuple(target["target"])
        q.current_action = {"station": target["station"]}
        q.samples[target["frame"]]["cachedVelocity"] = (6, 0)
        with self.assertRaisesRegex(ValueError, "not physically settled"):
            q.check_settled(target["frame"], "mutated-final-target")

    def test_state_correction_command(self):
        q = Probe(True, self.actions)
        response = copy.deepcopy(self.candidate.raw_initial_response)
        with self.assertRaisesRegex(ValueError, "Unexpected request fields"):
            q.call({"version": 1, "command": "restart", "seed": 0, "isolateRecipeRandom": True, "setPosition": [1, 2]}, response)

    def test_changed_route_target(self):
        with tempfile.TemporaryDirectory(prefix="oc2-waypoint-check-") as folder:
            baseline = Path("routes/probes/waypoint-baseline.json")
            modified = json.loads(Path("routes/probes/waypoint-continued.json").read_text())
            modified["jobs"][0]["actions"][1]["station"] = "74"
            candidate = Path(folder) / "changed.json"
            candidate.write_text(json.dumps(modified))
            with self.assertRaisesRegex(ValueError, "differ beyond"):
                check_routes(baseline, candidate)

    def test_raw_initial_clock_difference_is_retained(self):
        report = diff_summary({"clientTime": 1, "chefs": []}, {"clientTime": 2, "chefs": []})
        self.assertFalse(report["equal"])
        self.assertEqual(report["differingLeavesByTopLevelField"], {"clientTime": 1})


if __name__ == "__main__":
    unittest.main()
