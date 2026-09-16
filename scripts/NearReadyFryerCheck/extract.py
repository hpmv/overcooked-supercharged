import gzip
import json
from pathlib import Path

source = Path("artifacts/near-ready-fryer-a.jsonl.gz")
wanted = {1250, 1263, 1264, 1709, 1710, 1718, 1742}
with gzip.open(source, "rt", encoding="utf-8") as stream:
    for line in stream:
        row = json.loads(line)
        response = row.get("response") or {}
        state = response.get("state") or {}
        frame = state.get("gameplayFrame")
        if frame not in wanted:
            continue
        Path(f"artifacts/near-ready-fryer-native-gf{frame}.json").write_text(json.dumps(response, indent=2), encoding="utf-8")
        chef = next(c for c in state["chefs"] if c["playerId"] == 3)
        basket = next(e for e in state["entities"] if e["id"] == 5)
        print(json.dumps({"frame": frame, "held": chef["heldEntityId"], "progress": basket["cookingProgress"]}))
        wanted.remove(frame)
        if not wanted:
            break
if wanted:
    raise SystemExit(f"Missing recorded frames: {sorted(wanted)}")
