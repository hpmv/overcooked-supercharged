import sys
import unittest
from pathlib import Path


SCRIPTS = Path(__file__).resolve().parents[1]
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

from framework_story11_midfade_replay_probe import module_failure_values


class ModuleHealthClassificationTests(unittest.TestCase):
    def test_animator_diagnostic_differences_are_not_terminal_failures(self):
        status = {
            "active": True,
            "failure": None,
            "resumeFailure": None,
            "controllerInputRestoreObservations": [
                {"animators": [{"firstByteDifference": 2}]},
            ],
            "transitionTopologyRestoreObservations": [
                {"animators": [{"firstByteDifference": -1}]},
            ],
        }
        self.assertEqual([], module_failure_values(
            "chef-animator-checkpoint", status))

    def test_declared_terminal_failure_is_reported(self):
        status = {"active": True, "failure": "restore failed"}
        self.assertEqual(
            [{"path": "$.failure", "value": "restore failed"}],
            module_failure_values("rigidbody-actor-rebuild", status),
        )

    def test_nested_receipt_error_is_diagnostic_only(self):
        status = {
            "active": True,
            "failure": None,
            "receipts": [{"result": 18, "lastError": 997}],
        }
        self.assertEqual([], module_failure_values(
            "rigidbody-actor-rebuild", status))

    def test_body_restore_declares_its_own_terminal_fields(self):
        clean = {
            "hasRegistrationLease": True,
            "nativeShapePoseCaptureFailure": None,
            "nativeShapeGeometryRebindFailure": None,
            "nativeShapeGeometryRebindPoisoned": False,
        }
        self.assertEqual([], module_failure_values("body-restore", clean))
        poisoned = dict(clean, nativeShapeGeometryRebindPoisoned=True)
        self.assertEqual(
            [{"path": "$.nativeShapeGeometryRebindPoisoned", "value": True}],
            module_failure_values("body-restore", poisoned),
        )


if __name__ == "__main__":
    unittest.main()
