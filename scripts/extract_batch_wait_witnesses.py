"""Extract selected unchanged responses from the closed V16 batch-wait trace."""
import gzip
import hashlib
import json
import re
from pathlib import Path

source = Path("artifacts/native-round-v16/trial001.jsonl.gz")
directory = Path("artifacts/v16-batch-wait-witnesses")
targets = {4304, 4474, 4750, 12161, 12289, 12407, 12604}
expected = "d3802824e17cf667c954b822c5479c3d36ac30ed45b5699caa601c683be1f5e2"
pattern = re.compile(rb'"gameplayFrame"\s*:\s*(-?\d+)')
before = source.stat()
compressed = hashlib.sha256()
plain = hashlib.sha256()
found = {}
directory.mkdir(exist_ok=True)


class Reader:
    def __init__(self, stream): self.stream = stream
    def read(self, size=-1):
        data = self.stream.read(size)
        compressed.update(data)
        return data


with source.open("rb") as raw:
    with gzip.GzipFile(fileobj=Reader(raw), mode="rb") as stream:
        for record, line in enumerate(stream, 1):
            plain.update(line)
            if b'"kind":"call"' not in line[:90].replace(b" ", b""):
                continue
            match = pattern.search(line)
            if not match: continue
            frame = int(match[1])
            if frame not in targets or frame in found: continue
            row = json.loads(line)
            response = row["response"]
            assert response["state"]["gameplayFrame"] == frame
            data = json.dumps(response, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
            path = directory / f"gf{frame}.json"
            if path.exists() and path.read_bytes() != data:
                raise RuntimeError(f"Refusing to overwrite a different extracted witness: {path}")
            path.write_bytes(data)
            found[frame] = {"frame": frame, "record": record, "callLineSha256": hashlib.sha256(line).hexdigest(),
                            "path": str(path), "responseSha256": hashlib.sha256(data).hexdigest()}
            print("extracted", frame, flush=True)
after = source.stat()
assert (before.st_size, before.st_mtime_ns) == (after.st_size, after.st_mtime_ns), "Closed trace changed"
assert compressed.hexdigest() == expected, "Original trace SHA256 mismatch"
assert set(found) == targets, "Missing exact native response"
manifest = {"format": "native-batch-wait-witnesses-v1", "source": str(source.resolve()),
            "sourceCompressedSha256": compressed.hexdigest(), "sourceUncompressedSha256": plain.hexdigest(),
            "sourceBytes": before.st_size, "snapshots": list(found.values()),
            "qualification": "Unchanged native response values reserialized from exact hashed call lines; no native requests, modifications, or hypothetical policy execution."}
(directory / "manifest.json").write_text(json.dumps(manifest, indent=2), encoding="utf-8")
print("verified", compressed.hexdigest(), flush=True)
