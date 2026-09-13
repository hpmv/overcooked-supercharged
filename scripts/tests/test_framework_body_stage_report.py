import copy
import json
from pathlib import Path
import sys
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
import framework_body_stage_report as report


def fixture():
    path = ROOT / "artifacts/framework-migration/native-u/search-failure-native.json"
    document = json.loads(path.read_text(encoding="utf-8-sig"))
    checkpoint = document["bridge"]["nativeCheckpoints"]
    target = copy.deepcopy(checkpoint["requestedBodyCheckpoint"])
    for row in target["bodies"]:
        row["rawIsKinematic"] = True; row["rawUseGravity"] = False
    changed = copy.deepcopy(target)
    chef = next(row for row in changed["bodies"] if row["entityId"] == 103)
    chef["inertiaTensorRotation"]["z"] = -1.78814062e-7
    chef["rawIsKinematic"] = False
    checkpoint["nativeBodyWarpStages"] = {"scope": "read-only-current-authoring-warp-stage-observations", "frame": 31,
        "discardedOlderStages": 0, "stages": [{"stage": "before-resume", "captured": True, "current": target},
            {"stage": "after-resume", "captured": True, "current": changed},
            {"stage": "after-attachment", "captured": True, "current": copy.deepcopy(changed)}]}
    # Stages are explicitly synthetic. Only target/value come from native U;
    # native V is required to establish the actual transition boundary.
    return document


class StageReportTests(unittest.TestCase):
    def test_exact_changed_boundary_does_not_blame_later_unchanged_attachment_stage(self):
        result = report.interpret(fixture())
        self.assertEqual(result["firstObservedTransitionByEntity"]["103"]["stage"], "after-resume")
        self.assertEqual(result["firstObservedTransitionByEntity"]["103"]["difference"]["actual"]["z"], -1.78814062e-7)
        self.assertIsNone(result["stages"][2]["bodies"][0]["firstInvariantTransition"])
        self.assertFalse(result["stages"][1]["bodies"][0]["actualPhase"]["rawIsKinematic"])

    def test_initial_target_mismatch_is_not_claimed_new_transition(self):
        document = fixture(); stages = document["bridge"]["nativeCheckpoints"]["nativeBodyWarpStages"]["stages"]
        stages[0]["current"] = copy.deepcopy(stages[1]["current"])
        result = report.interpret(document)
        self.assertEqual(result["firstObservedTransitionByEntity"], {})
        self.assertTrue(result["firstObservedTargetDifferenceByEntity"]["103"]["presentAtFirstCapturedStage"])

    def test_failed_capture_preserves_observation_gap_and_error(self):
        document = fixture(); stages = document["bridge"]["nativeCheckpoints"]["nativeBodyWarpStages"]["stages"]
        stages.insert(1, {"stage": "failed", "captured": False, "error": "native diagnostic error"})
        result = report.interpret(document)
        self.assertFalse(result["completeObservationSequence"])
        self.assertEqual(result["firstObservedTransitionByEntity"], {})
        self.assertEqual(result["stages"][1]["error"], "native diagnostic error")

    def test_signed_zero_and_tiny_components_remain_distinct(self):
        expected = {"mass": 0.0}; actual = {"mass": -0.0}
        self.assertEqual(report.different_fields(expected, actual, ["mass"])[0]["field"], "mass")
        self.assertTrue(report.different_fields({}, {"mass": None}, ["mass"]))

    def test_missing_stage_target_frame_or_invariant_rejected(self):
        for mutation in ("frame", "missing", "duplicate", "empty"):
            document = fixture(); checkpoint = document["bridge"]["nativeCheckpoints"]
            if mutation == "frame": checkpoint["nativeBodyWarpStages"]["frame"] += 1
            elif mutation == "missing": checkpoint["requestedBodyCheckpoint"]["bodies"][0].pop("mass")
            elif mutation == "duplicate": checkpoint["requestedBodyCheckpoint"]["bodies"].append(copy.deepcopy(checkpoint["requestedBodyCheckpoint"]["bodies"][0]))
            else: checkpoint["nativeBodyWarpStages"]["stages"] = []
            with self.subTest(mutation=mutation), self.assertRaises(ValueError): report.interpret(document)


if __name__ == "__main__": unittest.main()
