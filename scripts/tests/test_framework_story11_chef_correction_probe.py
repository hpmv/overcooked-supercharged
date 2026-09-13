import copy
import sys
import unittest
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import framework_story11_chef_correction_probe as probe


def fixture(ops):
    rows = []
    for i, op in enumerate(ops):
        args = op["args"]
        values = [dict(s, value={"type": "System.Int32", "value": 0}) for s in args["members"]]
        rows.append({"entityId": args["entityId"], "objectInstanceId": 1000 + args["entityId"],
                     "componentInstanceId": 2000 + i, "ok": True, "mutationAttempted": False,
                     "before": {"values": copy.deepcopy(values)}, "after": {"values": values}})
    return {"detail": {"result": {"ok": True, "operations": rows}}}


class ChefCorrectionProbeTests(unittest.TestCase):
    def test_all_four_single_native_batch_and_no_mutations(self):
        ops = probe.inspection_operations([43, 44, 45, 46])
        self.assertEqual(32, len(ops))
        self.assertEqual({"inspect"}, {o["operation"] for o in ops})
        self.assertFalse(any(s["declaringType"].endswith("ClientChefSynchroniser")
                             for o in ops for s in o["args"]["members"]))

    def test_exact_identity_pins_reused(self):
        ops = probe.inspection_operations([43])
        pins, values = probe.inspection_result(fixture(ops), ops)
        self.assertEqual(8, len(values))
        for op in probe.inspection_operations([43], pins):
            key = (43, op["args"]["component"])
            self.assertEqual(pins[key], (op["args"]["expectedObjectId"], op["args"]["expectedComponentId"]))

    def test_selection_bounds(self):
        for invalid in ([], [43, 43], [103], [43, 44, 45, 46, 47]):
            with self.assertRaises(ValueError):
                probe.inspection_operations(invalid)

    def test_partial_batch_rejected(self):
        ops = probe.inspection_operations([43])
        data = fixture(ops)
        data["detail"]["result"]["operations"].pop()
        with self.assertRaises(RuntimeError):
            probe.inspection_result(data, ops)

    def test_failed_read_rejected(self):
        ops = probe.inspection_operations([43])
        for change in ("readError", "missing", "mutation", "changed"):
            data = fixture(ops)
            row = data["detail"]["result"]["operations"][0]
            if change == "readError":
                row["after"]["values"][0]["readError"] = "unavailable"
            elif change == "missing":
                row["after"]["values"].pop()
            elif change == "mutation":
                row["mutationAttempted"] = True
            else:
                row["after"]["values"][0]["value"]["value"] = 1
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                probe.inspection_result(data, ops)


if __name__ == "__main__":
    unittest.main()
