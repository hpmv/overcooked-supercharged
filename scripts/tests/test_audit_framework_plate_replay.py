"""Evidence binding and strict parity regressions; no native process or socket."""
import copy
import json
from pathlib import Path
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
sys.path.insert(0, str(Path(__file__).resolve().parent))
import audit_framework_plate_replay as audit
import compare_framework_frames as parity
from test_compare_framework_frames import fixture


REQUEST = {"command": "actions", "actions": [{"id": "plate", "chef": 103, "type": "pickup", "target": 10}]}
RAW = {"command": "raw-input", "segments": [{"frames": 1, "chefs": {}}]}


def evidence(directory, mutate=None, suffix=b""):
    rows = fixture()
    # Separate native load callback supplies the baseline registry. No gameplay
    # frame is synthesized by it; the next frame10 row starts the candidate.
    load = copy.deepcopy(rows[0]); load["input"]["Input"] = None
    load["output"]["ServerMessages"] = [{"Type": 1, "Message": ""}]
    first, middle, terminal, warp, replay_start, replay_middle, replay_terminal = rows
    all_rows = [load, {"kind": "control", "request": REQUEST}, first, middle, terminal,
                warp, {"kind": "control", "request": RAW}, replay_start, replay_middle, replay_terminal]
    if mutate:
        mutate(all_rows)
    raw = b""; offsets = []
    for row in all_rows:
        raw += (json.dumps(row) + "\n").encode(); offsets.append(len(raw))
    source = Path(directory) / "source.jsonl"; source.write_bytes(raw + suffix)
    a = {"traceByteOffset": offsets[4], "endInclusive": 12,
         "rawExchanges": json.loads(json.dumps([all_rows[2], all_rows[3]]))}
    b = {"traceByteOffset": offsets[-1], "endInclusive": 12,
         "rawExchanges": json.loads(json.dumps([all_rows[7], all_rows[8]]))}
    return source, a, b


class PlateReplayAuditTests(unittest.TestCase):
    def test_command_offsets_bind_identical_inputs_to_distinct_actual_epochs(self):
        with tempfile.TemporaryDirectory() as d:
            source, a, b = evidence(d)
            left = audit.bind_capture(source, a, REQUEST); right = audit.bind_capture(source, b, RAW)
            self.assertEqual((left["epoch"], right["epoch"]), (0, 1))
            self.assertEqual(left["load"], right["load"])
            self.assertGreater(left["terminalOutput"]["startByte"], left["lastInput"]["startByte"])
            self.assertEqual(right["inputExchangesVerified"], 2)

    def test_end_offset_cannot_silently_bind_a_later_request(self):
        with tempfile.TemporaryDirectory() as d:
            source, a, b = evidence(d)
            a["traceByteOffset"] = b["traceByteOffset"]
            with self.assertRaisesRegex(audit.AuditError, "exact saved control"):
                audit.bind_capture(source, a, REQUEST)

    def test_changed_raw_input_metadata_and_source_fields_are_rejected(self):
        for path in ["input", "output"]:
            with self.subTest(path=path), tempfile.TemporaryDirectory() as d:
                source, a, _ = evidence(d)
                if path == "input":
                    a["rawExchanges"][0]["input"]["__isset"]["resetOrderSeed"] = True
                else:
                    a["rawExchanges"][1]["output"]["Items"]["103"]["Pos"]["X"] += 1e-8
                with self.assertRaisesRegex(audit.AuditError, "capture/source mismatch"):
                    audit.bind_capture(source, a, REQUEST)

    def test_duplicate_terminal_is_an_ambiguity_error(self):
        with tempfile.TemporaryDirectory() as d:
            source, _, b = evidence(d)
            terminal = source.read_bytes().splitlines(keepends=True)[-1]
            source.write_bytes(source.read_bytes() + terminal)
            b["traceByteOffset"] = source.stat().st_size
            with self.assertRaisesRegex(audit.AuditError, "ambiguous terminal"):
                audit.bind_capture(source, b, RAW)

    def test_terminal_must_be_observed_after_input_only_capture(self):
        with tempfile.TemporaryDirectory() as d:
            source, _, b = evidence(d)
            b["traceByteOffset"] -= len(source.read_bytes().splitlines(keepends=True)[-1])
            with self.assertRaisesRegex(audit.AuditError, "terminal advancing"):
                audit.bind_capture(source, b, RAW)

    def test_duplicate_inputs_cannot_be_hidden_by_frame_filtering(self):
        with tempfile.TemporaryDirectory() as d:
            source, _, b = evidence(d)
            lines = source.read_bytes().splitlines(keepends=True)
            lines.insert(-1, lines[-2]); source.write_bytes(b"".join(lines))
            b["traceByteOffset"] = source.stat().st_size
            with self.assertRaisesRegex(audit.AuditError, "extra input"):
                audit.bind_capture(source, b, RAW)

    def test_exact_byte_freeze_retains_partial_later_line_and_excludes_live_suffix(self):
        with tempfile.TemporaryDirectory() as d:
            source, _, b = evidence(d, suffix=b'{"kind":"paused-exchanges"')
            cutoff = source.stat().st_size
            source.write_bytes(source.read_bytes() + b',"later":true}\n')
            frozen = Path(d) / "prefix.bin"
            saved = audit.copy_range(source, frozen, 0, cutoff)
            self.assertEqual(frozen.read_bytes(), source.read_bytes()[:cutoff])
            self.assertEqual(saved["bytes"], cutoff)
            self.assertEqual(audit.bind_capture(frozen, b, RAW)["terminalOutput"]["endByte"], b["traceByteOffset"])
            with self.assertRaises(FileExistsError):
                audit.copy_range(source, frozen, 0, cutoff)

    def test_truncated_source_and_replaced_capture_fail_closed(self):
        with tempfile.TemporaryDirectory() as d:
            source, a, _ = evidence(d)
            with self.assertRaisesRegex(audit.AuditError, "shorter"):
                audit.copy_range(source, Path(d) / "short.bin", 0, source.stat().st_size + 1)
            a["rawExchanges"].pop()
            with self.assertRaisesRegex(audit.AuditError, "extra input"):
                audit.bind_capture(source, a, REQUEST)

    def test_original_capture_decoder_does_not_weaken_strict_signed_zero_parity(self):
        with tempfile.TemporaryDirectory() as d:
            def mutate(rows):
                rows[3]["output"]["Items"]["103"]["Pos"]["Y"] = "SIGNED_ZERO"
                rows[8]["output"]["Items"]["103"]["Pos"]["Y"] = 0
            source, a, b = evidence(d, mutate)
            source.write_bytes(source.read_bytes().replace(b'"SIGNED_ZERO"', b'-0'))
            # Recreate the original producer's read offsets and json.loads.
            lines = source.read_bytes().splitlines(keepends=True)
            a["traceByteOffset"] = sum(map(len, lines[:5])); b["traceByteOffset"] = sum(map(len, lines))
            a["rawExchanges"] = [json.loads(lines[i]) for i in (2, 3)]
            self.assertEqual(audit.bind_capture(source, a, REQUEST)["epoch"], 0)
            epochs = parity.selected_epochs(source, {0, 1}, 10, 12)
            result = parity.compare(epochs[0], epochs[1], 10, 12)
            self.assertFalse(result["passed"])
            self.assertEqual(result["firstDivergence"]["field"], "$frame/physics/103/Pos/Y")

    def test_actual_r2_captures_have_exact_events_until_later_pose_divergence(self):
        run = ROOT / "artifacts/framework-migration/native-x-v11b/plate-checkpoint-sync-r2"
        case = audit.read(run / "case.json")
        a, b = [audit.read(run / n) for n in ("chef-103-dash-raw-capture.json", "selected-fixed-input-raw-capture.json")]
        rows = [audit.validate_capture(c, case, 56) for c in (a, b)]
        self.assertIsNone(parity.first_difference(*rows))
        diffs = []
        for left, right in zip(a["rawExchanges"][1:], b["rawExchanges"][1:]):
            self.assertIsNone(parity.first_difference(left["output"]["ServerMessages"], right["output"]["ServerMessages"]))
            if parity.first_difference(left["output"]["Items"], right["output"]["Items"]):
                diffs.append(left["output"]["FrameNumber"])
        self.assertEqual(diffs[0], 74)
        bad = copy.deepcopy(a); bad["inputs"][0]["nextFrame"] += 1
        with self.assertRaisesRegex(audit.AuditError, "normalized"):
            audit.validate_capture(bad, case, 56)
        bad = copy.deepcopy(a); bad["rawExchanges"][1]["input"]["NextFrame"] -= 1
        with self.assertRaisesRegex(audit.AuditError, "unordered"):
            audit.validate_capture(bad, case, 56)


if __name__ == "__main__":
    unittest.main()
