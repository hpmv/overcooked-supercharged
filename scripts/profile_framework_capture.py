"""Read captured bridge timing receipts; never contact a game or host."""
from __future__ import annotations
import argparse
import hashlib
import json
from pathlib import Path


def summarize(path: Path) -> dict:
    raw = path.read_bytes()
    points = []
    for row in json.loads(raw):
        metrics = row.get("response", {}).get("bridge", {}).get("captureMetrics")
        if metrics:
            points.append({"label": row["label"], "wallSeconds": row.get("wallSeconds"), **metrics})
    intervals = []
    for before, after in zip(points, points[1:]):
        count = after["samples"] - before["samples"]
        elapsed = after["totalMilliseconds"] - before["totalMilliseconds"]
        wall = after["wallSeconds"] - before["wallSeconds"]
        if count > 0 and elapsed >= 0 and wall > 0:
            intervals.append({"start": before["label"], "end": after["label"], "callbacks": count,
                              "captureMilliseconds": elapsed, "meanCaptureMilliseconds": elapsed / count,
                              "wallSeconds": wall, "callbacksPerWallSecond": count / wall})
    return {"source": str(path.resolve()), "sha256": hashlib.sha256(raw).hexdigest(), "points": points, "intervals": intervals}


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("observations", nargs="+", type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    report = {"gameCalls": 0, "scope": "Differences of actual native CollectDataForFrame timing counters. Includes the whole collector and its kitchen checkpoint, not individual subphase attribution. Callback intervals include paused polls and control/inspection time.",
              "sources": [summarize(p) for p in args.observations]}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
        stream.write("\n")


if __name__ == "__main__":
    main()
