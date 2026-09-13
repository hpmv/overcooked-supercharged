import gzip
import json
import re
from pathlib import Path

wanted = {11104, 11105, 11184}
with gzip.open("artifacts/native-round-v16/trial001.jsonl.gz", "rt", encoding="utf-8-sig") as stream:
    for line in stream:
        if not line.startswith('{"kind":"call"'):
            continue
        found = re.search(r'"gameplayFrame":(-?\d+)', line)
        frame = int(found[1]) if found else -1
        if frame not in wanted:
            continue
        row = json.loads(line)
        path = Path(f"artifacts/v16-final-sauce-gf{frame}.json")
        if path.exists():
            raise SystemExit(f"Capture already exists: {path}")
        path.write_text(json.dumps(row["response"], indent=2), encoding="utf-8")
        wanted.remove(frame)
        print(frame, flush=True)
        if not wanted:
            break
if wanted:
    raise SystemExit(f"Missing native frames: {wanted}")
