"""Small offline byte-integrity and refusal tests; no real logs modified."""
import gzip
import importlib.util
import json
import os
from pathlib import Path
import time

spec = importlib.util.spec_from_file_location("archive", Path(__file__).with_name("archive_closed_gate_logs.py"))
archive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archive)
root = archive.ROOT / "artifacts" / ("archive-fixtures-" + str(time.time_ns()))
artifacts = root / "artifacts"
artifacts.mkdir(parents=True)
payload = (b'\xef\xbb\xbf { "unchanged" : 1.00, "escaped": "\\u0041" }\r\n' * 8192) + b'\xff\x00last bytes\n'
source = artifacts / "gate20-b-process01-all-calls.jsonl.gz"
source.write_bytes(gzip.compress(payload, mtime=12345))
runs = []
for n in range(1, 5):
    path = artifacts / f"gate20-b-process01-run{n:02d}.jsonl.gz"
    path.write_bytes(gzip.compress(bytes([n]) * 100, mtime=12345))
    runs.append({"run": n, "trace": str(path), "passedExecutionGate": True})
result = {"ok": True, "runs": runs}
(artifacts / "gate20-b-process01-result.json").write_text(json.dumps(result))
summary = {"executed": True, "passed": True, "processes": [{"ordinal": 1, "result": result}]}
summary_path = artifacts / "gate20-b-summary.json"
summary_path.write_text(json.dumps(summary))
checks = []

def check(value, label):
    assert value, label
    checks.append(label)

def reject(fn, label):
    try:
        fn()
    except (ValueError, OSError):
        checks.append(label)
        return
    raise AssertionError(label)

original = source.read_bytes()
if os.name == "nt":
    with archive.locked_read(source):
        reject(lambda: source.open("ab"), "source archive lock refuses a concurrent writer")
    def lock_while_writer_open():
        with archive.locked_read(source):
            pass
    with source.open("ab"):
        reject(lock_while_writer_open, "archive lock refuses an already open writer")
proof = archive.archive(source, 16, root)
saved = Path(proof["archive"]["path"])
check(source.read_bytes() == original, "original gzip container unchanged")
check(all(archive.file_proof(Path(p["path"])) == p for p in proof["closedSourceEvidence"]["individualRunsPreserved"]), "four run files unchanged")
check(proof["uncompressed"]["bytes"] == len(payload), "uncompressed byte count preserved")
_, verified = archive.verify_archive(saved, str(saved)+".manifest.json")
check(verified["verifiedUncompressedSha256"] == proof["uncompressed"]["sha256"], "streaming XZ roundtrip hash")
plain = artifacts / "restored.jsonl"
archive.restore(saved, plain, root)
check(plain.read_bytes() == payload, "raw restore preserves whitespace, CRLF, BOM and arbitrary bytes")
regzip = artifacts / "restored.jsonl.gz"
archive.restore(saved, regzip, root)
check(gzip.decompress(regzip.read_bytes()) == payload, "gzip restore preserves exact decompressed bytes")
check(regzip.read_bytes() != original, "gzip container identity difference is explicit")
reject(lambda: archive.archive(source, 16, root), "refuse existing archive overwrite")
reject(lambda: archive.restore(saved, plain, root), "refuse existing restore overwrite")
reject(lambda: archive.closed_source(artifacts / "gate20-c-process01-all-calls.jsonl.gz", root), "reject missing matching-series proof")
reject(lambda: archive.closed_source(artifacts / "gate20-d-process01-all-calls.jsonl.gz", root), "reject unsupported series")
cruns = []
for n in range(1, 5):
    cpath = artifacts / f"gate20-c-process01-run{n:02d}.jsonl.gz"
    cpath.write_bytes(gzip.compress(bytes([n]) * 100, mtime=12345))
    cruns.append({"run": n, "trace": str(cpath), "passedExecutionGate": True})
cresult = {"ok": True, "runs": cruns}
(artifacts / 'gate20-c-process01-result.json').write_text(json.dumps(cresult))
(artifacts / 'gate20-c-summary.json').write_text(json.dumps({"executed": True, "passed": True,
    "processes": [{"ordinal": 1, "result": cresult}]}))
csource = artifacts / 'gate20-c-process01-all-calls.jsonl.gz'
csource.write_bytes(gzip.compress(payload, mtime=12345))
cproof = archive.archive(csource, 16, root)
check(cproof['closedSourceEvidence']['series'] == 'gate20-c', 'completed C series pins its own summary and runs')
check(cproof['uncompressed']['sha256'] == proof['uncompressed']['sha256'], 'C series also preserves exact raw bytes')
reject(lambda: archive.closed_source(artifacts / 'gate20-c-process06-all-calls.jsonl.gz', root), 'reject process outside frozen five')
reject(lambda: archive.closed_source(Path(runs[0]["trace"]), root), "reject individual run source")
reject(lambda: archive.restore(saved, artifacts / "gate20-b-process01-run99.jsonl.gz", root), "reject individual run restore name")
reject(lambda: archive.closed_source(root / source.name, root), "reject path outside allowed artifacts directory")
summary["passed"] = False
summary_path.write_text(json.dumps(summary))
reject(lambda: archive.closed_source(source, root), "reject incomplete series proof")
data = bytearray(saved.read_bytes()); data[len(data)//2] ^= 1; saved.write_bytes(data)
reject(lambda: archive.verify_archive(saved, str(saved)+".manifest.json"), "reject corrupted XZ before trusting stream")
report = {"ok": True, "checks": len(checks), "details": checks,
          "fixtureRoot": str(root), "qualification": "Synthetic byte tests; no original task logs were changed or removed."}
(archive.ROOT / "artifacts" / "closed-log-archive-tests.json").write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
