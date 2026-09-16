"""Reduce a marked native Rigidbody mass-distribution trace."""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict, deque
import json
from pathlib import Path


PHASES = {
    200: "before-warmup",
    210: "after-warmup",
    220: "before-original",
    230: "after-original",
    240: "before-warp",
    250: "after-warp",
    260: "before-replay",
    270: "after-replay",
}


def integer(value: str) -> int:
    return int(value, 16)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("trace", type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")

    source = json.loads(args.trace.read_text(encoding="utf-8-sig"))
    receipt = source["result"]["result"]
    base = integer(receipt["unityPlayerBase"])
    chefs = {row["bodyPointer"]: row for row in receipt["chefs"]}
    shape_events = defaultdict(list)
    for event in receipt["events"]:
        if event["kindId"] != 38:
            continue
        geometry_type = integer(event["stack"][5])
        geometry = {"rawBits": event["extra"]}
        if geometry_type == 2:
            geometry.update({"kind": "capsule", "radius": event["extraFloat"][1],
                             "halfHeight": event["extraFloat"][2]})
        elif geometry_type == 3:
            geometry.update({"kind": "box", "halfExtents": {
                "x": event["extraFloat"][1], "y": event["extraFloat"][2],
                "z": event["extraFloat"][3]}})
        else:
            geometry["kind"] = "geometry-" + str(geometry_type)
        shape_events[integer(event["stack"][1])].append({
            "sequence": event["sequence"],
            "shape": event["stack"][2],
            "index": integer(event["stack"][3]),
            "count": integer(event["stack"][4]),
            "geometryType": geometry_type,
            "flags": integer(event["stack"][6]),
            "geometryReadSucceeded": integer(event["stack"][7]) == 1,
            "localPose": {
                "rotation": {"x": event["payloadFloat"][0], "y": event["payloadFloat"][1],
                             "z": event["payloadFloat"][2], "w": event["payloadFloat"][3]},
                "position": {"x": event["payloadFloat"][4], "y": event["payloadFloat"][5],
                             "z": event["payloadFloat"][6]},
                "rawBits": event["payload"][:7],
            },
            "geometry": geometry,
        })
    current_phase = "before-first-marker"
    rows = []
    calls = []
    pending = defaultdict(deque)
    markers = []
    for event in receipt["events"]:
        if event["kindId"] == 1:
            code = integer(event["returnAddress"])
            current_phase = PHASES.get(code, "marker-" + str(code))
            markers.append({"sequence": event["sequence"], "code": code,
                            "phase": current_phase, "frame": integer(event["self"])})
            continue
        if event["kindId"] not in (36, 37):
            continue
        self_pointer = event["self"]
        return_address = integer(event["returnAddress"])
        frames = [integer(value) for value in event["frames"] if integer(value)]
        if event["kindId"] == 36:
            word50 = integer(event["payload"][2])
            row = {
            "sequence": event["sequence"],
            "phase": current_phase,
            "self": self_pointer,
            "chef": chefs.get(self_pointer),
            "actor": event["payload"][0],
            "massFloat": event["payloadFloat"][1],
            "word50": event["payload"][2],
            "automaticInertia": (word50 >> 8) & 0xFF,
            "automaticCenter": (word50 >> 16) & 0xFF,
            "byte53": (word50 >> 24) & 0xFF,
            "word54": event["payload"][3],
            "word58": event["payload"][4],
            "returnAddress": event["returnAddress"],
            "returnRva": ("0x%08X" % (return_address - base)
                          if base <= return_address < base + 0x010F0000 else None),
            "unityFrameRvas": ["0x%08X" % (value - base) for value in frames
                               if base <= value < base + 0x010F0000],
            "shapes": shape_events[event["sequence"]],
            }
            rows.append(row)
            pending[(event["threadId"], self_pointer, event["returnAddress"])].append(row)
            continue

        key = (event["threadId"], self_pointer, event["returnAddress"])
        pre = pending[key].popleft() if pending[key] else None
        post_word50 = integer(event["stack"][1])
        post = {
            "sequence": event["sequence"],
            "phase": pre["phase"] if pre else current_phase,
            "self": self_pointer,
            "chef": chefs.get(self_pointer),
            "actor": event["stack"][0],
            "word50": event["stack"][1],
            "automaticInertia": (post_word50 >> 8) & 0xFF,
            "automaticCenter": (post_word50 >> 16) & 0xFF,
            "byte53": (post_word50 >> 24) & 0xFF,
            "word54": event["stack"][2],
            "word58": event["stack"][3],
            "returnAddress": event["returnAddress"],
            "returnRva": ("0x%08X" % (return_address - base)
                          if base <= return_address < base + 0x010F0000 else None),
            "massSpaceRotation": {
                "x": event["payloadFloat"][0],
                "y": event["payloadFloat"][1],
                "z": event["payloadFloat"][2],
                "w": event["payloadFloat"][3],
            },
            "centerOfMass": {
                "x": event["payloadFloat"][4],
                "y": event["payloadFloat"][5],
                "z": event["payloadFloat"][6],
            },
            "inertiaTensorRaw": {
                "x": event["extraFloat"][0],
                "y": event["extraFloat"][1],
                "z": event["extraFloat"][2],
            },
        }
        calls.append({"pre": pre, "post": post})

    ordinal_counts = Counter()
    for call in calls:
        pre = call["pre"]
        post = call["post"]
        ordinal_key = (post["phase"], post["self"], post["returnRva"])
        ordinal_counts[ordinal_key] += 1
        call["ordinalWithinPhaseSelfCaller"] = ordinal_counts[ordinal_key]

    original_by_key = {}
    replay_by_key = {}
    for call in calls:
        post = call["post"]
        key = (post["self"], post["returnRva"], call["ordinalWithinPhaseSelfCaller"])
        if post["phase"] == "before-original":
            original_by_key[key] = call
        elif post["phase"] == "before-replay":
            replay_by_key[key] = call

    original_replay = []
    for key in sorted(set(original_by_key) | set(replay_by_key), key=lambda values: tuple(str(v) for v in values)):
        original = original_by_key.get(key)
        replay = replay_by_key.get(key)
        original_post = original["post"] if original else None
        replay_post = replay["post"] if replay else None
        comparison_fields = ("centerOfMass", "massSpaceRotation", "inertiaTensorRaw")
        changed_fields = [field for field in comparison_fields
                          if (original_post or {}).get(field) != (replay_post or {}).get(field)]
        original_shapes = original["pre"]["shapes"] if original and original["pre"] else None
        replay_shapes = replay["pre"]["shapes"] if replay and replay["pre"] else None
        # Sequence numbers locate the enclosing trace records; they are not
        # PhysX inputs and necessarily differ between original and replay.
        comparable_original_shapes = ([{field: value for field, value in shape.items()
                                        if field != "sequence"} for shape in original_shapes]
                                      if original_shapes is not None else None)
        comparable_replay_shapes = ([{field: value for field, value in shape.items()
                                      if field != "sequence"} for shape in replay_shapes]
                                    if replay_shapes is not None else None)
        original_replay.append({
            "self": key[0],
            "chef": chefs.get(key[0]),
            "returnRva": key[1],
            "ordinal": key[2],
            "bothPresent": original is not None and replay is not None,
            "changedFields": changed_fields,
            "shapeInputsEqual": comparable_original_shapes == comparable_replay_shapes,
            "originalShapes": original_shapes,
            "replayShapes": replay_shapes,
            "originalSequence": original_post["sequence"] if original_post else None,
            "replaySequence": replay_post["sequence"] if replay_post else None,
            "original": {field: original_post[field] for field in comparison_fields} if original_post else None,
            "replay": {field: replay_post[field] for field in comparison_fields} if replay_post else None,
        })

    groups = Counter((row["phase"], row["self"], row["returnRva"]) for row in rows)
    report = {
        "classification": "read-only marked Rigidbody::UpdateMassDistribution trace reduction",
        "source": str(args.trace.resolve()),
        "nativeTrace": {
            "installedMask": receipt["installedMask"],
            "installedHookCount": receipt["installedHookCount"],
            "latestSequence": receipt["latestSequence"],
            "droppedEstimate": receipt["droppedEstimate"],
            "lastError": receipt["lastError"],
        },
        "markers": markers,
        "updateMassDistributionCount": len(rows),
        "updateMassDistributionPostCount": len(calls),
        "unpairedPreCount": sum(len(queue) for queue in pending.values()),
        "groups": [
            {"phase": phase, "self": self_pointer, "chef": chefs.get(self_pointer),
             "returnRva": return_rva, "count": count}
            for (phase, self_pointer, return_rva), count in sorted(
                groups.items(), key=lambda item: tuple(str(value) for value in item[0]))
        ],
        "events": rows,
        "calls": calls,
        "originalReplay": original_replay,
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in report.items() if key != "events"}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
