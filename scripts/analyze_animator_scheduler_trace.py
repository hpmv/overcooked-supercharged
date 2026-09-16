#!/usr/bin/env python3
"""Compare marker-bounded Animator/Director scheduling across rewind replay."""

from __future__ import annotations

import argparse
import json
import struct
from collections import Counter
from pathlib import Path
from typing import Any


def word(value: Any) -> int:
    if isinstance(value, int):
        return value
    return int(value, 16) if isinstance(value, str) and value.startswith("0x") else int(value)


def u64(words: list[Any], index: int) -> int:
    return word(words[index]) | (word(words[index + 1]) << 32)


def f32(value: Any) -> float:
    return struct.unpack("<f", struct.pack("<I", word(value)))[0]


def f64(words: list[Any], index: int) -> float:
    return struct.unpack("<d", struct.pack("<Q", u64(words, index)))[0]


def load_phase(path: Path, start_code: int, end_code: int) -> tuple[dict[str, Any], list[dict[str, Any]], dict[str, int]]:
    with path.open("r", encoding="utf-8") as stream:
        trace = json.load(stream)["result"]
    markers = {
        word(event["returnAddress"]): event["sequence"]
        for event in trace["events"]
        if event["kind"] == "marker"
    }
    start = markers[start_code]
    end = markers[end_code]
    events = [event for event in trace["events"] if start < event["sequence"] < end]
    return trace, events, {"startSequence": start, "endSequence": end}


def chef_animators(trace: dict[str, Any]) -> dict[int, str]:
    return {word(row["animatorPointer"]): row["path"] for row in trace.get("chefAnimators", [])}


def animator_writes(trace: dict[str, Any], events: list[dict[str, Any]]) -> list[dict[str, Any]]:
    aliases = chef_animators(trace)
    result = []
    first_graph_frame: int | None = None
    for event in events:
        animator = word(event["self"])
        if event["kind"] != "animator-write-properties" or animator not in aliases:
            continue
        graph_frame = u64(event["extra"], 1)
        if first_graph_frame is None:
            first_graph_frame = graph_frame
        result.append({
            "sequence": event["sequence"],
            "animator": aliases[animator],
            "animatorPointer": event["self"],
            "returnAddress": event["returnAddress"],
            "deltaTimeBits": event["stack"][1],
            "deltaTime": f32(event["stack"][1]),
            "secondArgumentBits": event["stack"][2],
            "secondArgument": f32(event["stack"][2]),
            "animatorFields": event["payload"],
            "graphPointer": event["extra"][0],
            "graphFrame": graph_frame,
            "relativeGraphFrame": graph_frame - first_graph_frame,
            "preparedTimeBits": event["extra"][3:5],
            "preparedTime": f64(event["extra"], 3),
        })
    return result


def update_avatars(events: list[dict[str, Any]]) -> list[dict[str, Any]]:
    result = []
    first_graph_frame: int | None = None
    for event in events:
        if event["kind"] != "animator-update-avatars":
            continue
        graph_frame = u64(event["payload"], 4)
        if first_graph_frame is None:
            first_graph_frame = graph_frame
        result.append({
            "sequence": event["sequence"],
            "outputCount": word(event["payload"][1]),
            "firstOutput": event["payload"][2],
            "graphPointer": event["payload"][3],
            "graphFrame": graph_frame,
            "relativeGraphFrame": graph_frame - first_graph_frame,
            "preparedTimeBits": event["payload"][6:8],
            "preparedTime": f64(event["payload"], 6),
        })
    return result


def director(events: list[dict[str, Any]]) -> list[dict[str, Any]]:
    return [
        {
            "sequence": event["sequence"],
            "kind": event["kind"],
            "stage": word(event["payload"][0]),
        }
        for event in events
        if event["kind"] in ("director-prepare-stage", "director-process-stage")
    ]


def sync_timeline(events: list[dict[str, Any]], writes: list[dict[str, Any]], stages: list[dict[str, Any]]) -> list[dict[str, Any]]:
    syncs = [event for event in events if event["kind"] == "physics-manager-sync-transforms"]
    player1 = [row for row in writes if row["animator"] == "Chefs/Player 1/Chef"]
    result = []
    previous = events[0]["sequence"] - 1 if events else 0
    cumulative_writes = 0
    cumulative_director = 0
    for ordinal, sync in enumerate(syncs):
        between_writes = [row for row in player1 if previous < row["sequence"] < sync["sequence"]]
        between_stages = [row for row in stages if previous < row["sequence"] < sync["sequence"]]
        cumulative_writes += len(between_writes)
        cumulative_director += len(between_stages)
        result.append({
            "ordinal": ordinal,
            "sequence": sync["sequence"],
            "player1WritesSincePriorSync": len(between_writes),
            "cumulativePlayer1Writes": cumulative_writes,
            "lastPlayer1GraphFrame": between_writes[-1]["graphFrame"] if between_writes else None,
            "lastPlayer1RelativeGraphFrame": between_writes[-1]["relativeGraphFrame"] if between_writes else None,
            "directorEventsSincePriorSync": len(between_stages),
            "cumulativeDirectorEvents": cumulative_director,
            "directorStagesSincePriorSync": [f'{row["kind"]}:{row["stage"]}' for row in between_stages],
            "queueCount": word(sync["payload"][3]),
            "globalMaskLow": sync["payload"][0],
            "globalMaskHigh": sync["payload"][1],
        })
        previous = sync["sequence"]
    return result


def first_difference(left: list[dict[str, Any]], right: list[dict[str, Any]], fields: list[str]) -> dict[str, Any] | None:
    count = max(len(left), len(right))
    for index in range(count):
        if index >= len(left) or index >= len(right):
            return {"ordinal": index, "original": left[index] if index < len(left) else None,
                    "replay": right[index] if index < len(right) else None}
        differences = {
            field: {"original": left[index].get(field), "replay": right[index].get(field)}
            for field in fields
            if left[index].get(field) != right[index].get(field)
        }
        if differences:
            return {"ordinal": index, "differences": differences,
                    "original": left[index], "replay": right[index]}
    return None


def summarize(trace: dict[str, Any], events: list[dict[str, Any]], markers: dict[str, int]) -> dict[str, Any]:
    writes = animator_writes(trace, events)
    updates = update_avatars(events)
    stages = director(events)
    syncs = sync_timeline(events, writes, stages)
    stage_counts = Counter((row["kind"], row["stage"]) for row in stages)
    write_counts = Counter(row["animator"] for row in writes)
    return {
        "traceCompleteness": {
            **markers,
            "latestSequence": trace["latestSequence"],
            "droppedEstimate": trace["droppedEstimate"],
            "phaseEventCount": len(events),
        },
        "eventCounts": dict(sorted(Counter(event["kind"] for event in events).items())),
        "directorStageCounts": [
            {"kind": kind, "stage": stage, "count": count}
            for (kind, stage), count in sorted(stage_counts.items())
        ],
        "chefWriteCounts": dict(sorted(write_counts.items())),
        "firstChefWrites": writes[:8],
        "lastChefWrites": writes[-8:],
        "firstUpdateAvatars": updates[:4],
        "lastUpdateAvatars": updates[-4:],
        "syncTimeline": syncs,
        "_writes": writes,
        "_updates": updates,
        "_stages": stages,
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--replay", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--original-start", type=int, default=220)
    parser.add_argument("--original-end", type=int, default=230)
    parser.add_argument("--replay-start", type=int, default=260)
    parser.add_argument("--replay-end", type=int, default=270)
    args = parser.parse_args()

    original_trace, original_events, original_markers = load_phase(
        args.original, args.original_start, args.original_end
    )
    replay_trace, replay_events, replay_markers = load_phase(
        args.replay, args.replay_start, args.replay_end
    )
    original = summarize(original_trace, original_events, original_markers)
    replay = summarize(replay_trace, replay_events, replay_markers)

    write_fields = [
        "animator", "returnAddress", "deltaTimeBits", "secondArgumentBits",
        "animatorFields", "graphPointer", "graphFrame", "preparedTimeBits",
    ]
    normalized_write_fields = [
        "animator", "returnAddress", "deltaTimeBits", "secondArgumentBits",
        "animatorFields", "graphPointer", "relativeGraphFrame", "preparedTimeBits",
    ]
    update_fields = [
        "outputCount", "firstOutput", "graphPointer", "graphFrame", "preparedTimeBits",
    ]
    normalized_update_fields = [
        "outputCount", "firstOutput", "graphPointer", "relativeGraphFrame", "preparedTimeBits",
    ]
    sync_fields = [
        "player1WritesSincePriorSync", "cumulativePlayer1Writes",
        "lastPlayer1RelativeGraphFrame", "directorEventsSincePriorSync",
        "cumulativeDirectorEvents", "directorStagesSincePriorSync",
    ]
    result = {
        "passed": True,
        "scope": "Read-only marker-bounded comparison of Unity Animator, PlayableGraph, DirectorManager, and SyncTransforms scheduling.",
        "original": {key: value for key, value in original.items() if not key.startswith("_")},
        "replay": {key: value for key, value in replay.items() if not key.startswith("_")},
        "comparisons": {
            "absoluteChefWriteFirstDifference": first_difference(original["_writes"], replay["_writes"], write_fields),
            "normalizedChefWriteFirstDifference": first_difference(original["_writes"], replay["_writes"], normalized_write_fields),
            "absoluteUpdateAvatarsFirstDifference": first_difference(original["_updates"], replay["_updates"], update_fields),
            "normalizedUpdateAvatarsFirstDifference": first_difference(original["_updates"], replay["_updates"], normalized_update_fields),
            "syncInterleaveFirstDifference": first_difference(
                original["syncTimeline"], replay["syncTimeline"], sync_fields
            ),
        },
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "passed": True,
        "out": str(args.out),
        "originalChefWrites": len(original["_writes"]),
        "replayChefWrites": len(replay["_writes"]),
        "originalUpdateAvatars": len(original["_updates"]),
        "replayUpdateAvatars": len(replay["_updates"]),
        "comparisons": result["comparisons"],
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
