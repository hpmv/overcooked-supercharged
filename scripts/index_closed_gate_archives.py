"""Reverify the five archived closed combined logs and write a review index."""
import json
import argparse
from pathlib import Path
import sys
import time
import archive_closed_gate_logs as archive

artifacts = archive.ROOT / "artifacts"
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--series', choices=('gate20-b', 'gate20-c'), default='gate20-b')
args = parser.parse_args()
series = args.series
path = artifacts / f"{series}-closed-combined-archive-index.json"
archive.require(not path.exists(), "Archive index already exists; refusing replacement.")
entries = []
for ordinal in range(1, 6):
    stem = f"{series}-process{ordinal:02d}-all-calls.jsonl"
    original, packed = artifacts / (stem + ".gz"), artifacts / (stem + ".xz")
    manifest = Path(str(packed) + ".manifest.json")
    print(f"Verifying closed process {ordinal}/5 XZ and individual runs...", file=sys.stderr, flush=True)
    proof, verified = archive.verify_archive(packed, manifest)
    archive.require(Path(proof["source"]["path"]).resolve() == original and Path(proof["archive"]["path"]).resolve() == packed,
                    "Manifest file identity mismatch.")
    if original.exists():
        actual = archive.file_proof(original)
        archive.require(actual["sha256"] == proof["source"]["sha256"] and actual["bytes"] == proof["source"]["bytes"],
                        "An original combined gzip changed since archival.")
    individuals = proof["closedSourceEvidence"]["individualRunsPreserved"]
    archive.require(len(individuals) == 4, "Manifest must pin four individual runs.")
    for number, individual in enumerate(individuals, 1):
        expected = artifacts / f"{series}-process{ordinal:02d}-run{number:02d}.jsonl.gz"
        archive.require(Path(individual["path"]).resolve() == expected and archive.file_proof(expected) == individual,
                        "An original individual run file changed or disappeared.")
    entries.append({"processOrdinal": ordinal, "originalGzip": proof["source"],
                    "originalGzipPresentAtVerification": original.exists(), "archive": proof["archive"],
                    "manifest": archive.file_proof(manifest), "uncompressed": proof["uncompressed"],
                    "verified": verified, "individualRunsPreserved": individuals,
                    "restoreGzipExample": f'python scripts/archive_closed_gate_logs.py restore "{packed}" "{artifacts / ("restored-" + stem + ".gz")}"'})
result = {"format": "oc2-closed-combined-log-archive-index", "version": 1,
          "verifiedUtc": time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()), "ok": True,
          "scope": f"Only five closed {series} combined logs; twenty individual run gzip files preserved.",
          "byteGuarantee": "Exact original decompressed JSONL bytes; recreated gzip container bytes may differ.",
          "deletion": "This index command never deletes files. Original presence is a fact at verification time.",
          "originalGzipBytes": sum(e["originalGzip"]["bytes"] for e in entries),
          "archiveAndManifestBytes": sum(e["archive"]["bytes"] + e["manifest"]["bytes"] for e in entries),
          "entries": entries}
result["potentialReclaimedBytes"] = result["originalGzipBytes"] - result["archiveAndManifestBytes"]
with path.open('x', encoding='utf-8') as output:
    output.write(json.dumps(result, indent=2) + "\n")
print(json.dumps({"ok": True, "index": str(path), "originalGzipBytes": result["originalGzipBytes"],
                  "archiveAndManifestBytes": result["archiveAndManifestBytes"],
                  "potentialReclaimedBytes": result["potentialReclaimedBytes"]}, indent=2))
