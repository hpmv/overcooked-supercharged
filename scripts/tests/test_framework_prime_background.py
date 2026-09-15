"""Contract tests for the bounded Unity background-focus primer."""
import sys
from pathlib import Path
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
from framework_prime_background import focus_view, prime_background


def response(frame, focused, minimized, request=None, enabled=True):
    return {"bridge": {
        "unityFrame": frame,
        "applicationFocused": focused,
        "runInBackground": enabled,
        "backgroundTasInput": enabled,
        "nativeWindow": {"minimized": minimized, "lastRequest": request},
    }}


class FakeBridge:
    def __init__(self, rows):
        self.rows = iter(rows)
        self.requests = []

    def call(self, request):
        self.requests.append(request)
        return next(self.rows)


class BackgroundPrimeTest(unittest.TestCase):
    def test_exact_activate_dwell_minimize_deactivate_sequence(self):
        bridge = FakeBridge([
            response(10, True, True),
            response(11, True, False, "activate"),
            response(12, True, False, "activate"),
            response(14, True, False, "activate"),
            response(15, True, True, "minimize"),
            response(16, False, True, "minimize"),
        ])
        with patch("framework_prime_background.time.sleep"):
            proof = prime_background(bridge, 1)
        self.assertFalse(proof["backgroundStable"]["applicationFocused"])
        self.assertEqual(bridge.requests[1], {"command": "window", "mode": "activate"})
        self.assertEqual(bridge.requests[-2], {"command": "window", "mode": "minimize"})

    def test_disabled_background_contract_is_rejected_before_window_mutation(self):
        bridge = FakeBridge([response(1, False, True, enabled=False)])
        with self.assertRaisesRegex(RuntimeError, "Background priming requires"):
            prime_background(bridge, 1)
        self.assertEqual(bridge.requests, [{"command": "status"}])

    def test_invalid_diagnostics_fail_closed(self):
        with self.assertRaisesRegex(RuntimeError, "invalid shape"):
            focus_view({"bridge": {"unityFrame": 1, "applicationFocused": "false",
                                    "runInBackground": True, "backgroundTasInput": True,
                                    "nativeWindow": {"minimized": True}}})


if __name__ == "__main__":
    unittest.main()
