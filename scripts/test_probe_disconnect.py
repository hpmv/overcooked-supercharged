"""Offline fixtures only: no socket or native game calls."""
import copy
import unittest
from probe_disconnect import decode_exact, neutral, verify_attempt


def states():
    pads = [{"player": p, "x": 0, "y": 0, "pickup": False, "use": False, "dash": False} for p in range(4)]
    before = {"frame": 10, "inputs": pads, "transport": {"requestsCancelledBeforeDispatch": 2, "requestsDispatched": 18,
              "activeConnectionId": 7}, "state": {"gameEvents": [{"index": 1}]}}
    after = {"frame": 11, "inputs": copy.deepcopy(pads), "paused": True,
             "transport": {"requestsCancelledBeforeDispatch": 3, "requestsDispatched": 19,
                           "activeConnectionId": 8, "lastCancelledConnectionId": 7},
             "state": {"gameEvents": [{"index": 1}, {"index": 2, "kind": "input_release",
                       "reason": "controller-disconnected", "inputsNeutral": True}]}}
    return before, after


class ProofTests(unittest.TestCase):
    def test_owned_cancellation_proved(self):
        before, after = states()
        proof = verify_attempt(before, after)
        self.assertTrue(proof["passed"])
        self.assertEqual(proof["nativeFramesAdvanced"], 1)

    def test_quiet_game_alone_is_insufficient(self):
        for field, value in (("requestsCancelledBeforeDispatch", 2), ("requestsCancelledBeforeDispatch", 4),
                             ("lastCancelledConnectionId", 6), ("activeConnectionId", 7), ("requestsDispatched", 20)):
            before, after = states()
            after["transport"][field] = value
            with self.subTest(field=field, value=value), self.assertRaises(AssertionError):
                verify_attempt(before, after)

    def test_missing_diagnostics_rejected(self):
        before, after = states()
        del after["transport"]
        with self.assertRaises(AssertionError):
            verify_attempt(before, after)

    def test_input_and_pause_evidence_required(self):
        for mutate in (lambda a: a.update(paused=False), lambda a: a["inputs"][0].update(x=1),
                       lambda a: a["inputs"][2].update(use=True), lambda a: a["inputs"].pop(),
                       lambda a: a["inputs"][3].update(player=0)):
            before, after = states()
            mutate(after)
            with self.assertRaises(AssertionError):
                verify_attempt(before, after)

    def test_fresh_neutral_release_event_required(self):
        for change in ({"index": 1}, {"inputsNeutral": False}, {"reason": "another-event"}):
            before, after = states()
            after["state"]["gameEvents"][-1].update(change)
            with self.assertRaises(AssertionError):
                verify_attempt(before, after)

    def test_fragmented_reply_and_eof(self):
        class Bytes:
            def __init__(self, chunks): self.chunks = iter(chunks)
            def recv(self, count): return next(self.chunks, b"")
        self.assertEqual(decode_exact(Bytes([b"a", b"bc"]), 3), b"abc")
        with self.assertRaises(ConnectionError):
            decode_exact(Bytes([b"a"]), 3)


if __name__ == "__main__":
    unittest.main()
