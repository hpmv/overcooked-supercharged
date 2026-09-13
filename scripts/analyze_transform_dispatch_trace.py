#!/usr/bin/env python3
"""Compare filtered Unity transform-dispatch traces across rewind replay."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


def word(value: Any) -> int:
    if isinstance(value, int):
        return value
    return int(value, 16) if isinstance(value, str) and value.startswith("0x") else int(value)


def load_trace(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as stream:
        document = json.load(stream)
    return document["result"]


def phase(events: list[dict[str, Any]], start_code: int, end_code: int) -> tuple[list[dict[str, Any]], dict[str, int]]:
    markers = {
        word(event["returnAddress"]): event
        for event in events
        if event["kind"] == "marker"
    }
    start = markers[start_code]
    end = markers[end_code]
    selected = [
        event
        for event in events
        if start["sequence"] < event["sequence"] < end["sequence"]
    ]
    return selected, {"startSequence": start["sequence"], "endSequence": end["sequence"]}


def aliases(trace: dict[str, Any], events: list[dict[str, Any]]) -> dict[int, list[str]]:
    result: dict[int, list[str]] = {}

    def add(pointer: Any, label: str) -> None:
        address = word(pointer)
        if address:
            result.setdefault(address, [])
            if label not in result[address]:
                result[address].append(label)

    for chef in trace.get("chefs", []):
        add(chef["bodyPointer"], f'body:{chef["path"]}')
    for collider in trace.get("chefColliders", []):
        add(collider["colliderPointer"], f'collider:{collider["path"]}:{collider["colliderType"]}')
        add(collider["transformPointer"], f'transform:{collider["path"]}')
        if collider.get("shapePointer"):
            add(collider["shapePointer"], f'shape:{collider["path"]}:{collider["colliderType"]}')
    for event in events:
        if event["kind"] == "transform-queue-changes":
            transform = word(event["self"])
            hierarchy = word(event["payload"][0])
            for label in result.get(transform, []):
                if label.startswith("transform:"):
                    add(hierarchy, "hierarchy:" + label.removeprefix("transform:"))
    for event in events:
        if event["kind"] == "rigidbody-update-mass-distribution":
            body = word(event["self"])
            actor = word(event["payload"][0])
            for label in result.get(body, []):
                if label.startswith("body:"):
                    add(actor, "physx-actor:" + label.removeprefix("body:"))
    return result


def pointer(pointer_value: Any, pointer_aliases: dict[int, list[str]]) -> dict[str, Any]:
    address = word(pointer_value)
    return {
        "pointer": f"0x{address:08X}",
        "aliases": pointer_aliases.get(address, []),
    }


def sync_calls(events: list[dict[str, Any]], pointer_aliases: dict[int, list[str]]) -> list[dict[str, Any]]:
    snapshots: dict[int, list[dict[str, Any]]] = {}
    for event in events:
        if event["kind"] != "transform-dispatch-queued":
            continue
        parent = word(event["stack"][0])
        snapshots.setdefault(parent, []).append(event)

    calls = []
    for ordinal, event in enumerate(item for item in events if item["kind"] == "physics-manager-sync-transforms"):
        queued = sorted(snapshots.get(event["sequence"], []), key=lambda item: word(item["stack"][1]))
        calls.append(
            {
                "ordinal": ordinal,
                "sequence": event["sequence"],
                "threadId": event["threadId"],
                "caller": event["returnAddress"],
                "globalMaskLow": event["payload"][1],
                "globalMaskHigh": event["payload"][2],
                "queueCount": word(event["payload"][5]),
                "recordedQueueCount": len(queued),
                "queue": [
                    {
                        **pointer(item["self"], pointer_aliases),
                        "index": word(item["stack"][1]),
                        "queueIndex": word(item["payload"][0]),
                        "maskLow": item["payload"][1],
                        "maskHigh": item["payload"][2],
                    }
                    for item in queued
                ],
            }
        )
    return calls


def queue_changes(events: list[dict[str, Any]], pointer_aliases: dict[int, list[str]]) -> list[dict[str, Any]]:
    calls = []
    for ordinal, event in enumerate(item for item in events if item["kind"] == "transform-queue-changes"):
        calls.append(
            {
                "ordinal": ordinal,
                "sequence": event["sequence"],
                "threadId": event["threadId"],
                "caller": event["returnAddress"],
                "transform": pointer(event["self"], pointer_aliases),
                "hierarchy": pointer(event["payload"][0], pointer_aliases),
                "queueIndexBefore": word(event["payload"][1]),
                "hierarchyMaskLowBefore": event["payload"][2],
                "hierarchyMaskHighBefore": event["payload"][3],
                "globalQueueCountBefore": word(event["payload"][5]),
                "globalMaskLowBefore": event["payload"][6],
                "globalMaskHighBefore": event["payload"][7],
            }
        )
    return calls


def comparable(value: Any) -> Any:
    if isinstance(value, dict):
        return {key: comparable(item) for key, item in value.items() if key not in {"sequence", "aliases"}}
    if isinstance(value, list):
        return [comparable(item) for item in value]
    return value


def first_mismatch(left: list[dict[str, Any]], right: list[dict[str, Any]]) -> dict[str, Any] | None:
    for ordinal in range(max(len(left), len(right))):
        original = left[ordinal] if ordinal < len(left) else None
        replay = right[ordinal] if ordinal < len(right) else None
        if comparable(original) != comparable(replay):
            return {"ordinal": ordinal, "original": original, "replay": replay}
    return None


def sync_difference_rows(left: list[dict[str, Any]], right: list[dict[str, Any]]) -> list[dict[str, Any]]:
    rows = []
    for ordinal in range(max(len(left), len(right))):
        original = left[ordinal] if ordinal < len(left) else None
        replay = right[ordinal] if ordinal < len(right) else None
        if original is None or replay is None:
            rows.append({"ordinal": ordinal, "original": original, "replay": replay})
            continue
        original_pointers = [item["pointer"] for item in original["queue"]]
        replay_pointers = [item["pointer"] for item in replay["queue"]]
        original_states = [comparable(item) for item in original["queue"]]
        replay_states = [comparable(item) for item in replay["queue"]]
        global_masks_equal = (
            original["globalMaskLow"] == replay["globalMaskLow"]
            and original["globalMaskHigh"] == replay["globalMaskHigh"]
        )
        if global_masks_equal and original_states == replay_states:
            continue
        rows.append(
            {
                "ordinal": ordinal,
                "globalMasksEqual": global_masks_equal,
                "queuePointersEqual": original_pointers == replay_pointers,
                "queueStatesEqual": original_states == replay_states,
                "original": original,
                "replay": replay,
            }
        )
    return rows


def bounded_sync_outputs(events: list[dict[str, Any]], pointer_aliases: dict[int, list[str]]) -> list[dict[str, Any]]:
    calls = []
    active: dict[str, Any] | None = None
    for event in events:
        if event["kind"] == "physics-manager-sync-transforms":
            if active is not None:
                raise ValueError("Nested or missing SyncTransforms post edge")
            active = {"entrySequence": event["sequence"], "events": []}
        elif event["kind"] == "physics-manager-sync-transforms-post":
            if active is None:
                raise ValueError("SyncTransforms post edge without entry")
            active["postSequence"] = event["sequence"]
            active["ordinal"] = len(calls)
            calls.append(active)
            active = None
        elif active is not None and event["kind"] != "transform-dispatch-queued":
            active["events"].append(
                {
                    "kind": event["kind"],
                    "self": pointer(event["self"], pointer_aliases),
                    "caller": event["returnAddress"],
                    "payload": event["payload"],
                    "extra": event["extra"],
                }
            )
    if active is not None:
        raise ValueError("Unclosed SyncTransforms entry")
    return calls


def first_output_mismatch(
    original_inputs: list[dict[str, Any]],
    replay_inputs: list[dict[str, Any]],
    original_outputs: list[dict[str, Any]],
    replay_outputs: list[dict[str, Any]],
) -> dict[str, Any] | None:
    for ordinal in range(min(len(original_inputs), len(replay_inputs), len(original_outputs), len(replay_outputs))):
        if comparable(original_inputs[ordinal]) != comparable(replay_inputs[ordinal]):
            continue
        left = original_outputs[ordinal]["events"]
        right = replay_outputs[ordinal]["events"]
        if comparable(left) == comparable(right):
            continue
        mismatch_index = 0
        while mismatch_index < min(len(left), len(right)) and comparable(left[mismatch_index]) == comparable(right[mismatch_index]):
            mismatch_index += 1
        return {
            "syncOrdinal": ordinal,
            "inputEqual": True,
            "mismatchEventOrdinal": mismatch_index,
            "originalOutputCount": len(left),
            "replayOutputCount": len(right),
            "originalEvent": left[mismatch_index] if mismatch_index < len(left) else None,
            "replayEvent": right[mismatch_index] if mismatch_index < len(right) else None,
            "originalKinds": [item["kind"] for item in left],
            "replayKinds": [item["kind"] for item in right],
        }
    return None


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

    original_trace = load_trace(args.original)
    replay_trace = load_trace(args.replay)
    original_events, original_markers = phase(original_trace["events"], args.original_start, args.original_end)
    replay_events, replay_markers = phase(replay_trace["events"], args.replay_start, args.replay_end)
    pointer_aliases = aliases(original_trace, original_events + replay_events)
    original_sync = sync_calls(original_events, pointer_aliases)
    replay_sync = sync_calls(replay_events, pointer_aliases)
    original_queue_changes = queue_changes(original_events, pointer_aliases)
    replay_queue_changes = queue_changes(replay_events, pointer_aliases)
    sync_differences = sync_difference_rows(original_sync, replay_sync)
    original_outputs = bounded_sync_outputs(original_events, pointer_aliases)
    replay_outputs = bounded_sync_outputs(replay_events, pointer_aliases)

    result = {
        "passed": True,
        "scope": "Read-only comparison of marker-bounded transform-dispatch input captured at PhysicsManager::SyncTransforms entry.",
        "traceCompleteness": {
            "original": {
                **original_markers,
                "latestSequence": original_trace["latestSequence"],
                "droppedEstimate": original_trace["droppedEstimate"],
                "phaseEventCount": len(original_events),
            },
            "replay": {
                **replay_markers,
                "latestSequence": replay_trace["latestSequence"],
                "droppedEstimate": replay_trace["droppedEstimate"],
                "phaseEventCount": len(replay_events),
            },
        },
        "syncTransforms": {
            "originalCount": len(original_sync),
            "replayCount": len(replay_sync),
            "originalQueuedHierarchyCount": sum(call["recordedQueueCount"] for call in original_sync),
            "replayQueuedHierarchyCount": sum(call["recordedQueueCount"] for call in replay_sync),
            "firstMismatch": first_mismatch(original_sync, replay_sync),
            "mismatchCount": len(sync_differences),
            "firstQueuePointerMismatch": next(
                (row for row in sync_differences if not row.get("queuePointersEqual", True)), None
            ),
            "differences": sync_differences,
            "boundedOutput": {
                "originalCallCount": len(original_outputs),
                "replayCallCount": len(replay_outputs),
                "firstMismatchWithIdenticalInput": first_output_mismatch(
                    original_sync, replay_sync, original_outputs, replay_outputs
                ),
            },
        },
        "queueChanges": {
            "originalCount": len(original_queue_changes),
            "replayCount": len(replay_queue_changes),
            "transformOrderEqual": [item["transform"]["pointer"] for item in original_queue_changes]
            == [item["transform"]["pointer"] for item in replay_queue_changes],
            "callerOrderEqual": [item["caller"] for item in original_queue_changes]
            == [item["caller"] for item in replay_queue_changes],
            "firstMismatch": first_mismatch(original_queue_changes, replay_queue_changes),
        },
        "pointerAliases": {f"0x{key:08X}": value for key, value in sorted(pointer_aliases.items())},
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    with args.out.open("w", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, indent=2)
        stream.write("\n")
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
