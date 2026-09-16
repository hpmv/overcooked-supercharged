"""Synthetic archive/restore/refusal checks. No original task trace is modified."""
import gzip
import hashlib
import json
import lzma
import os
from pathlib import Path
import time

import archive_closed_native_trace as tool

root = tool.ROOT / "artifacts" / ("native-archive-fixtures-" + str(time.time_ns()))
artifacts = root / "artifacts"; artifacts.mkdir(parents=True)
checks = []


def check(value, name):
    assert value, name; checks.append(name)


def reject(fn, name):
    try:
        fn()
    except (ValueError, OSError, EOFError, lzma.LZMAError):
        checks.append(name); return
    raise AssertionError(name)


payload = b'\xef\xbb\xbf{ "x" : 1.00, "escaped": "\\u0041" }\r\n' * 8192 + b'\xff\x00no reformatting\n'


def fixture(name, exit_code=1, packed=None):
    directory = artifacts / name; directory.mkdir()
    source = directory / "trial001.jsonl.gz"
    source.write_bytes(packed if packed is not None else gzip.compress(payload[:10000], mtime=123) + gzip.compress(payload[10000:], mtime=456))
    summary = directory / "summary.json"
    result = {"trial": 1, "exitCode": exit_code, "trace": str(source), "traceSha256": hashlib.sha256(source.read_bytes()).hexdigest(),
              "score": 876, "delivered": 9, "failure": "recorded synthetic failure" if exit_code else None,
              "completedRequestedRun": exit_code == 0, "completedFullRound": False, "qualifyingNativeHighScoreRound": False, "observationSource": "trace-fallback"}
    data = {"format": "oc2-native-candidate-search", "version": 2, "results": [result]}
    summary.write_text(json.dumps(data))
    return source, summary, data


source, provenance, data = fixture("failed-closed-native")
original, original_provenance = source.read_bytes(), provenance.read_bytes()
if os.name == "nt":
    with source.open("ab"):
        reject(lambda: tool.plan(source, provenance, 1, root), "reject already-open native trace writer")
    with tool.locked_read(source):
        reject(lambda: source.open("ab"), "lock refuses newly opened writer")
    with provenance.open("ab"):
        reject(lambda: tool.plan(source, provenance, 1, root), "reject active provenance writer")
plan = tool.plan(source, provenance, 1, root)
check(plan["completeGzipCRCValidated"] and plan["uncompressed"]["sha256"] == hashlib.sha256(payload).hexdigest(), "plan consumes all concatenated gzip members and preserves exact raw hash")
check(not source.with_suffix(".xz").exists(), "read-only plan creates no archive")
proof = tool.archive(source, provenance, 1, 16, root)
packed = Path(proof["archive"]["path"]); manifest = Path(str(packed) + ".manifest.json")
check(source.read_bytes() == original and provenance.read_bytes() == original_provenance, "archive preserves original gzip/provenance bytes")
check(proof["closedSourceEvidence"]["terminalTrial"]["exitCode"] == 1 and proof["closedSourceEvidence"]["terminalTrial"]["completedFullRound"] is False, "closed failed trial remains a failure")
check(lzma.decompress(packed.read_bytes()) == payload and proof["uncompressed"]["bytes"] == len(payload), "XZ preserves exact BOM whitespace numeric spelling CRLF and arbitrary bytes")
check(tool.verify(packed, root)["verifiedUncompressedSha256"] == hashlib.sha256(payload).hexdigest(), "verify independently checks compressed and raw hashes")
plain, regzip = artifacts / "restored.jsonl", artifacts / "restored.jsonl.gz"
tool.restore(packed, plain, root); restored = tool.restore(packed, regzip, root)
check(plain.read_bytes() == payload, "plain restore exact byte identity")
check(gzip.decompress(regzip.read_bytes()) == payload, "gzip restore exact decompressed byte identity")
check(regzip.read_bytes() != original and restored["gzipContainerMayDiffer"], "recreated gzip container difference qualified")
reject(lambda: tool.archive(source, provenance, 1, 16, root), "archive refuses existing destination/manifest")
reject(lambda: tool.restore(packed, plain, root), "restore refuses existing destination")
reject(lambda: tool.restore(packed, root / "outside.jsonl", root), "restore refuses resolved path outside artifacts")
reject(lambda: tool.restore(packed, artifacts / "unsupported.txt", root), "restore refuses unsupported output extension")
reject(lambda: tool.plan(root / source.name, provenance, 1, root), "reject source outside artifacts")
reject(lambda: tool.plan(source, provenance, 2, root), "reject unrecorded trial ordinal")
reject(lambda: tool.archive(source, provenance, 1, 128, root), "reject unsupported compression dictionary")
for label, mutate in [
    ("no terminal exit", lambda d: d["results"][0].pop("exitCode")),
    ("boolean exit is not process exit", lambda d: d["results"][0].update(exitCode=True)),
    ("wrong trace path", lambda d: d["results"][0].update(trace=str(artifacts / "other.jsonl.gz"))),
    ("wrong trace hash", lambda d: d["results"][0].update(traceSha256="0" * 64)),
    ("watcher is not provenance", lambda d: d.update(format="oc2-native-trace-watch")),
    ("duplicate trial", lambda d: d["results"].append(dict(d["results"][0]))),
]:
    changed = json.loads(original_provenance); mutate(changed); provenance.write_text(json.dumps(changed))
    reject(lambda: tool.plan(source, provenance, 1, root), label)
    provenance.write_bytes(original_provenance)
for label, blob in (("truncated gzip", original[:-6]), ("bad gzip CRC", original[:-8] + bytes([original[-8] ^ 1]) + original[-7:])):
    bad_source, evidence, _ = fixture(label.replace(" ", "-"), packed=blob)
    before = bad_source.read_bytes()
    reject(lambda: tool.archive(bad_source, evidence, 1, 16, root), label + " rejected even with matching compressed hash")
    check(bad_source.read_bytes() == before and not bad_source.with_suffix(".xz").exists() and not Path(str(bad_source.with_suffix(".xz")) + ".manifest.json").exists(), label + " publishes no archive and preserves original")
passed_source, passed_summary, _ = fixture("closed-success", 0)
passed = tool.archive(passed_source, passed_summary, 1, 16, root)
check(passed["closedSourceEvidence"]["terminalTrial"]["exitCode"] == 0 and passed["closedSourceEvidence"]["terminalTrial"]["qualifyingNativeHighScoreRound"] is False, "closed success does not invent high-score qualification")
partial_output = artifacts / "partial-restore.jsonl"
Path(str(partial_output) + ".partial").write_bytes(b"retain existing partial")
reject(lambda: tool.restore(packed, partial_output, root), "restore refuses existing partial")
with packed.open("ab"):
    reject(lambda: tool.verify(packed, root), "verification refuses open archive writer")
saved_manifest = manifest.read_bytes(); altered = json.loads(saved_manifest); altered["uncompressed"]["sha256"] = "0" * 64; manifest.write_text(json.dumps(altered))
reject(lambda: tool.verify(packed, root), "raw-hash mismatch rejected")
manifest.write_bytes(saved_manifest)
damaged = bytearray(packed.read_bytes()); damaged[len(damaged)//2] ^= 1; packed.write_bytes(damaged)
reject(lambda: tool.verify(packed, root), "corrupted XZ hash rejected")
reject(lambda: tool.restore(packed, artifacts / "corrupt-restore.jsonl", root), "restore refuses corrupted archive before publication")
check(not (artifacts / "corrupt-restore.jsonl").exists(), "corrupt archive produces no final restore")
report = {"ok": True, "checks": len(checks), "details": checks, "fixtureRoot": str(root),
          "toolSha256": tool.byte_proof(Path(tool.__file__))["sha256"], "testSourceSha256": tool.byte_proof(Path(__file__))["sha256"],
          "qualification": "Only small synthetic byte fixtures were created/archived/restored. No original native trace was archived, modified or deleted."}
with (tool.ROOT / "artifacts" / "closed-native-archive-tests.json").open("w", encoding="utf-8") as stream:
    json.dump(report, stream, indent=2); stream.write("\n")
print(json.dumps(report, indent=2))
