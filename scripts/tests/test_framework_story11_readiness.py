import sys
import unittest
from pathlib import Path


SCRIPTS = Path(__file__).resolve().parents[1]
if str(SCRIPTS) not in sys.path:
    sys.path.insert(0, str(SCRIPTS))

from framework_story11_midfade_replay_probe import (
    first_replay_island_audit_plan,
    module_failure_values,
    require_first_replay_authoring_boundary,
)


class ModuleHealthClassificationTests(unittest.TestCase):
    def test_first_replay_audit_uses_normal_two_frame_pause_handshake(self):
        self.assertEqual(
            {
                "checkpointFrame": 444,
                "transitionFrame": 445,
                "capturedAtOutputFrame": 445,
                "readAtFrame": 446,
                "stepFrames": 2,
            },
            first_replay_island_audit_plan(444),
        )

    def test_first_replay_audit_rejects_invalid_checkpoint_frames(self):
        for frame in (-1, 444.0, True, None):
            with self.subTest(frame=frame), self.assertRaises(RuntimeError):
                first_replay_island_audit_plan(frame)

    def test_first_replay_transition_read_requires_bridge_owned_fence(self):
        boundary = {
            "paused": True,
            "holdPause": True,
            "inputBlocked": True,
            "loading": False,
        }
        self.assertIs(
            boundary,
            require_first_replay_authoring_boundary({"bridge": boundary}),
        )
        for field, invalid in (
            ("paused", False),
            ("holdPause", False),
            ("inputBlocked", False),
            ("loading", True),
        ):
            with self.subTest(field=field), self.assertRaises(RuntimeError):
                require_first_replay_authoring_boundary({
                    "bridge": {**boundary, field: invalid},
                })

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
