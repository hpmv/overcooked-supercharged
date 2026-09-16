"""Read closed native bytes and retain exact pre-service events/completion states."""
import argparse
import gzip
import hashlib
import json
import lzma
import re
from pathlib import Path

p = argparse.ArgumentParser(description=__doc__)
p.add_argument("version", type=int, choices=(16, 17))
a = p.parse_args()
source = Path(f"artifacts/native-round-v{a.version}/trial001.jsonl.gz")
archive_manifest = None
if not source.exists():
    source = source.with_suffix(".xz")
    archive_manifest = json.loads(Path(str(source) + ".manifest.json").read_text())
with gzip.open(f"artifacts/v{a.version}-central-work-projection.json.gz", "rt", encoding="utf-8") as stream:
    prior = json.load(stream)
expected_raw = prior["uncompressedSha256"]
expected_container = archive_manifest["archive"]["sha256"] if archive_manifest else prior["compressedSha256"]
before = source.stat()
container = hashlib.sha256()
plain = hashlib.sha256()
events = []
boundaries = {}
last_line = None
last_frame = -1
last_record = -1
pattern = re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)')
directory = Path(f"artifacts/v{a.version}-preservice-boundaries")
directory.mkdir(exist_ok=True)


class Reader:
    def __init__(self, stream): self.stream = stream
    def read(self, size=-1):
        data = self.stream.read(size)
        container.update(data)
        return data
    def seekable(self): return False


with source.open("rb") as raw:
    decoder = lzma.LZMAFile(Reader(raw), mode="rb") if source.suffix == ".xz" else gzip.GzipFile(fileobj=Reader(raw), mode="rb")
    with decoder as stream:
        for record, line in enumerate(stream, 1):
            plain.update(line)
            prefix = line[:90].replace(b" ", b"")
            if b'"kind":"call"' in prefix:
                match = pattern.search(line)
                if match:
                    last_frame = int(match[1]); last_line = line; last_record = record
            elif b'"kind":"event"' in prefix and b'"preService' in line[:180]:
                row = json.loads(line)
                events.append({"frame": last_frame, "record": record, "lineSha256": hashlib.sha256(line).hexdigest(), **row})
                if row["name"] not in ("preServiceStockAdmitted", "preServiceStockComplete") or last_frame in boundaries:
                    continue
                assert last_line is not None
                response = json.loads(last_line)["response"]
                data = json.dumps(response, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
                path = directory / f"gf{last_frame}.json"
                if path.exists() and path.read_bytes() != data: raise RuntimeError("Refusing different native witness overwrite")
                path.write_bytes(data)
                boundaries[last_frame] = {"frame": last_frame, "record": last_record, "path": str(path),
                                          "callLineSha256": hashlib.sha256(last_line).hexdigest(), "responseSha256": hashlib.sha256(data).hexdigest()}
                print(a.version, row["name"], last_frame, flush=True)
after = source.stat()
assert (before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Closed source changed"
assert container.hexdigest() == expected_container, "Container SHA256 mismatch"
assert plain.hexdigest() == expected_raw, "Exact original JSONL SHA256 mismatch"
report = {"format": "closed-native-preservice-boundaries-v1", "version": a.version,
          "source": str(source.resolve()), "readContainerSha256": container.hexdigest(),
          "originalGzipSha256": prior["compressedSha256"], "originalRawJsonlSha256": plain.hexdigest(),
          "archiveManifestSha256": hashlib.sha256(Path(str(source) + ".manifest.json").read_bytes()).hexdigest() if archive_manifest else None,
          "events": events, "snapshots": list(boundaries.values()),
          "qualification": "Exact native events and unchanged response values from closed, hash-verified bytes. XZ when present preserves original JSONL, not the removed gzip container. No game I/O."}
(directory / "manifest.json").write_text(json.dumps(report, indent=2), encoding="utf-8")
print(a.version, "verified", "events", len(events), "snapshots", len(boundaries), flush=True)
