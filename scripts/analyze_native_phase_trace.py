"""Reduce two native physics traces to target-body pose changes around simulation."""
import argparse
import json
from collections import Counter
from pathlib import Path


def marker_code(event):
    return int(event["stack"][0], 16) if event.get("kindId") == 1 else None


def load(path, start_code, end_code, target_path):
    root = json.loads(path.read_text(encoding="utf-8-sig"))["result"]
    events = root["events"]
    starts = [event["sequence"] for event in events if marker_code(event) == start_code]
    ends = [event["sequence"] for event in events if marker_code(event) == end_code]
    if len(starts) != 1 or len(ends) != 1 or starts[0] >= ends[0]:
        raise ValueError(f"Expected one ordered marker pair in {path}")
    target = next(chef for chef in root["chefs"] if chef["path"] == target_path)
    selected = [event for event in events if starts[0] < event["sequence"] < ends[0]]
    counts = Counter(event["kind"] for event in selected)
    simulate = -1
    previous = {}
    changes = []
    constraints = []
    markers = []
    for event in selected:
        if event["kindId"] == 40:
            simulate += 1
        if event["kindId"] == 1:
            markers.append({"sequence": event["sequence"], "code": marker_code(event),
                            "value": int(event["self"], 16)})
        if event["self"] != target["bodyPointer"] or event["kindId"] not in (42, 43):
            continue
        pose_bits = event["payload"][4:7]
        row = {"sequence": event["sequence"], "simulationIndex": simulate,
               "kind": event["kind"], "position": event["payloadFloat"][4:7],
               "positionBits": pose_bits, "returnAddress": event["returnAddress"],
               "frame0": event["frames"][0]}
        if event["kindId"] == 42:
            constraints.append(row)
        key = event["kindId"]
        if previous.get(key) != pose_bits:
            changes.append(row)
            previous[key] = pose_bits
    return {
        "path": str(path.resolve()), "installedMask": root["installedMask"],
        "droppedEstimate": root["droppedEstimate"], "target": target,
        "startSequence": starts[0], "endSequence": ends[0],
        "eventCount": len(selected), "kindCounts": dict(counts),
        "simulationCount": counts["physics-manager-simulate"],
        "innerMarkers": markers, "targetPoseChanges": changes,
        "targetApplyConstraints": constraints,
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--replay", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--target-path", default="Chefs/Player 1")
    args = parser.parse_args()
    result = {
        "original": load(args.original, 220, 230, args.target_path),
        "replay": load(args.replay, 260, 270, args.target_path),
        "scope": "Read-only reduction of marker-bounded native trace events; no game process access.",
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({side: {
          "eventCount": result[side]["eventCount"],
          "simulationCount": result[side]["simulationCount"],
          "kindCounts": result[side]["kindCounts"],
          "poseChangeCount": len(result[side]["targetPoseChanges"]),
          "firstPoseChanges": result[side]["targetPoseChanges"][:8],
          "lastPoseChanges": result[side]["targetPoseChanges"][-4:]}
          for side in ("original", "replay")}, indent=2))


if __name__ == "__main__":
    main()
