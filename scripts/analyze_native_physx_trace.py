"""Reduce a marked in-process native physics trace to branch-comparison evidence."""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
from pathlib import Path


MARKERS = {
    100: "before-load", 110: "after-load", 120: "after-warmup",
    130: "before-original", 140: "after-original", 150: "before-warp",
    160: "after-warp", 170: "before-replay", 180: "after-replay",
}
# Load/warmup markers may be intentionally evicted by a clear immediately
# before the paired capture. The branch delimiters themselves remain strict.
REQUIRED_BRANCH_MARKERS = {120, 130, 140, 150, 160, 170, 180}


def integer(value: str) -> int:
    return int(value, 16)


def payload_words(event: dict) -> tuple[str, ...]:
    kind = event["kind"]
    if kind == "pxc-discrete-narrowphase-pcm":
        return tuple(event.get("rawWorkUnit", ()))
    if kind == "nphase-core-overlap-created":
        return tuple(event["stack"][:3] + event.get("rawElement0", []) + event.get("rawElement1", []))
    if kind == "pxs-contact-manager-created":
        allocation = event.get("contactManagerAllocation", {})
        return tuple(
            event["stack"][:6] + [
                str(allocation.get("managerIndex")), allocation.get("rigidCore0"),
                allocation.get("rigidCore1"), allocation.get("shapeCore0"),
                allocation.get("shapeCore1"), str(allocation.get("transformCache0")),
                str(allocation.get("transformCache1")),
            ] + event.get("rawContactManagerPrefix", [])
        )
    if kind == "rigidbody-move-position":
        return tuple(event["payload"][:3])
    if kind in {
        "scb-body-set-body2world", "np-rigid-dynamic-set-global-pose",
        "np-rigid-dynamic-set-kinematic-target", "sc-body-core-set-body2world",
        "rigidbody-apply-constraints-post-get-pose", "rigidbody-get-position-post-get-pose",
    }:
        return tuple(event["payload"][:7])
    if kind == "physics-manager-simulate":
        return tuple(event["payload"][:1])
    if kind in {"rigidbody-create", "rigidbody-set-is-kinematic"}:
        return tuple(event["stack"][1:2])
    return ()


def signature(event: dict) -> tuple:
    return event["kind"], event["self"], payload_words(event)


def compact(event: dict) -> dict:
    result = {
        "sequence": event["sequence"], "kind": event["kind"], "self": event["self"],
        "payload": list(payload_words(event)),
        "payloadFloat": event["payloadFloat"][:len(payload_words(event))],
        "stackArguments": event["stack"][1:3], "aux": event["stack"][:3],
        "returnAddress": event["returnAddress"],
    }
    if event["kind"] == "pxc-discrete-narrowphase-pcm":
        result.update(
            threadId=event["threadId"],
            arguments={"context": event["stack"][0], "argument3": event["stack"][1],
                       "argument4": event["stack"][2]},
            rawWorkUnit=event.get("rawWorkUnit", []),
        )
    elif event["kind"] == "nphase-core-overlap-created":
        result.update(
            threadId=event["threadId"],
            elements=event["stack"][:2],
            pairData=event["stack"][2],
            rawElement0=event.get("rawElement0", []),
            rawElement1=event.get("rawElement1", []),
        )
    elif event["kind"] == "pxs-contact-manager-created":
        result.update(
            threadId=event["threadId"],
            allocation=event.get("contactManagerAllocation", {}),
            rawContactManagerPrefix=event.get("rawContactManagerPrefix", []),
        )
    return result


def narrowphase_semantic_row(event: dict) -> dict:
    words = event.get("rawWorkUnit", [])
    return {
        "sequence": event["sequence"],
        "workUnit": event["self"],
        "managerIndex": words[15] if len(words) > 15 else None,
        "pairIdentity": words[16:20],
    }


def contact_manager_semantic_row(event: dict) -> dict:
    allocation = event.get("contactManagerAllocation", {})
    return {
        "sequence": event["sequence"],
        "manager": event["self"],
        "managerIndex": allocation.get("managerIndex"),
        "poppedFreeSlot": allocation.get("poppedFreeSlot"),
        "postFreeCount": allocation.get("postFreeCount"),
        "context": allocation.get("context"),
        "freeArray": allocation.get("freeArray"),
        "pairIdentity": [
            allocation.get("rigidCore0"), allocation.get("rigidCore1"),
            allocation.get("shapeCore0"), allocation.get("shapeCore1"),
        ],
    }


def without_sequence(row: dict) -> dict:
    return {key: value for key, value in row.items() if key != "sequence"}


def collapse_consecutive(rows: list[dict]) -> list[dict]:
    collapsed = []
    for row in rows:
        if collapsed and without_sequence(collapsed[-1]) == without_sequence(row):
            continue
        collapsed.append(row)
    return collapsed


def contact_manager_comparison(left: list[dict], right: list[dict]) -> dict:
    raw_left = [contact_manager_semantic_row(event) for event in left]
    raw_right = [contact_manager_semantic_row(event) for event in right]
    collapsed_left = collapse_consecutive(raw_left)
    collapsed_right = collapse_consecutive(raw_right)
    left_semantic = [without_sequence(row) for row in collapsed_left]
    right_semantic = [without_sequence(row) for row in collapsed_right]
    return {
        "duplicatePolicy": "collapse consecutive identical allocation records",
        "originalCollapsedCount": len(collapsed_left),
        "replayCollapsedCount": len(collapsed_right),
        "exactAllocationOrderParity": left_semantic == right_semantic,
        "originalCollapsed": collapsed_left,
        "replayCollapsed": collapsed_right,
    }


def narrowphase_comparison(left: list[dict], right: list[dict]) -> dict:
    rows = []
    for index in range(max(len(left), len(right))):
        a = left[index] if index < len(left) else None
        b = right[index] if index < len(right) else None
        a_words = a.get("rawWorkUnit", []) if a else []
        b_words = b.get("rawWorkUnit", []) if b else []
        word_differences = [
            {"word": word, "offset": "0x%02X" % (word * 4),
             "original": a_words[word] if word < len(a_words) else None,
             "replay": b_words[word] if word < len(b_words) else None}
            for word in range(max(len(a_words), len(b_words)))
            if word >= len(a_words) or word >= len(b_words) or a_words[word] != b_words[word]
        ]
        rows.append({
            "index": index,
            "original": compact(a) if a else None,
            "replay": compact(b) if b else None,
            "sameWorkUnitPointer": bool(a and b and a["self"] == b["self"]),
            "samePairIdentityWords16To19": bool(a and b and a_words[16:20] == b_words[16:20]),
            "sameRawWorkUnit": bool(a and b and a_words == b_words),
            "wordDifferences": word_differences,
        })
    semantic_left = [narrowphase_semantic_row(event) for event in left]
    semantic_right = [narrowphase_semantic_row(event) for event in right]
    return {
        "originalCount": len(left),
        "replayCount": len(right),
        "exactWorkUnitPointerOrder": [x["self"] for x in left] == [x["self"] for x in right],
        "exactPairIdentityWordOrder": [x.get("rawWorkUnit", [])[16:20] for x in left] ==
                                      [x.get("rawWorkUnit", [])[16:20] for x in right],
        "exactManagerIndexPairOrder": [without_sequence(x) for x in semantic_left] ==
                                      [without_sequence(x) for x in semantic_right],
        "exactRawWorkUnitOrder": [x.get("rawWorkUnit", []) for x in left] ==
                                 [x.get("rawWorkUnit", []) for x in right],
        "originalSemanticOrder": semantic_left,
        "replaySemanticOrder": semantic_right,
        "firstDifferentEntry": next((row for row in rows if not row["sameRawWorkUnit"]), None),
        "entries": rows,
    }


def first_difference(left: list[dict], right: list[dict]) -> dict | None:
    for index in range(max(len(left), len(right))):
        a = left[index] if index < len(left) else None
        b = right[index] if index < len(right) else None
        if a is None or b is None or signature(a) != signature(b):
            return {
                "index": index,
                "original": compact(a) if a else None,
                "replay": compact(b) if b else None,
                "originalContext": [compact(x) for x in left[max(0, index-2):index+3]],
                "replayContext": [compact(x) for x in right[max(0, index-2):index+3]],
            }
    return None


def endpoint_rows(data: dict) -> list[dict]:
    original = {body["entityId"]: body for body in
                data["boundaries"]["original"]["bridge"]["nativePhysics"]["bodies"]}
    replay = {body["entityId"]: body for body in
              data["boundaries"]["replay"]["bridge"]["nativePhysics"]["bodies"]}
    rows = []
    for entity in (43, 44, 45, 46):
        a, b = original[entity], replay[entity]
        rows.append({
            "entityId": entity,
            "originalPosition": a["position"], "replayPosition": b["position"],
            "deltaY": b["position"]["y"] - a["position"]["y"],
            "originalSleeping": a["sleeping"], "replaySleeping": b["sleeping"],
            "originalKinematic": a["rawIsKinematic"], "replayKinematic": b["rawIsKinematic"],
        })
    return rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")

    data = json.loads(args.input.read_text(encoding="utf-8-sig"))
    trace = data["nativeTrace"]["detail"]["result"]
    events = trace["events"]
    markers = {}
    marker_rows = []
    for event in events:
        if event["kind"] != "marker":
            continue
        code = integer(event["returnAddress"])
        if code not in MARKERS:
            continue
        markers[code] = event["sequence"]
        marker_rows.append({"code": code, "name": MARKERS[code],
                            "sequence": event["sequence"], "value": integer(event["self"])})
    missing = sorted(set(MARKERS) - set(markers))
    missing_required = sorted(REQUIRED_BRANCH_MARKERS - set(markers))
    if missing_required:
        raise RuntimeError("Missing branch markers: " + ", ".join(map(str, missing_required)))

    def between(start: int, end: int) -> list[dict]:
        return [event for event in events if markers[start] < event["sequence"] < markers[end]
                and event["kind"] != "marker"]

    phases = {
        "load": between(100, 110) if 100 in markers else [],
        "warmup": between(110, 120) if 110 in markers else [],
        "original": between(130, 140),
        "warp": between(150, 160),
        "replay": between(170, 180),
    }
    original, replay = phases["original"], phases["replay"]
    kinds = sorted({event["kind"] for event in original + replay})
    by_kind = {}
    for kind in kinds:
        left = [event for event in original if event["kind"] == kind]
        right = [event for event in replay if event["kind"] == kind]
        by_kind[kind] = {
            "originalCount": len(left), "replayCount": len(right),
            "exactSignatureParity": [signature(x) for x in left] == [signature(x) for x in right],
            "firstDifference": first_difference(left, right),
        }

    chefs = {row["bodyPointer"]: row["path"] for row in trace["chefs"]}
    chef_moves = {}
    chef_constraint_poses = {}
    chef_position_reads = {}
    for pointer, path in sorted(chefs.items(), key=lambda item: item[1]):
        left = [event for event in original
                if event["kind"] == "rigidbody-move-position" and event["self"] == pointer]
        right = [event for event in replay
                 if event["kind"] == "rigidbody-move-position" and event["self"] == pointer]
        chef_moves[path] = {
            "bodyPointer": pointer,
            "originalTargets": [event["payloadFloat"][:3] for event in left],
            "replayTargets": [event["payloadFloat"][:3] for event in right],
            "exactTargetParity": [payload_words(x) for x in left] == [payload_words(x) for x in right],
        }
        left = [event for event in original
                if event["kind"] == "rigidbody-apply-constraints-post-get-pose" and event["self"] == pointer]
        right = [event for event in replay
                 if event["kind"] == "rigidbody-apply-constraints-post-get-pose" and event["self"] == pointer]
        chef_constraint_poses[path] = {
            "bodyPointer": pointer,
            "actorPointers": sorted({event["stack"][0] for event in left + right}),
            "original": [event["payloadFloat"][:7] for event in left],
            "replay": [event["payloadFloat"][:7] for event in right],
            "exactPoseParity": [payload_words(x) for x in left] == [payload_words(x) for x in right],
            "firstDifference": first_difference(left, right),
        }
        left = [event for event in original
                if event["kind"] == "rigidbody-get-position-post-get-pose" and event["self"] == pointer]
        right = [event for event in replay
                 if event["kind"] == "rigidbody-get-position-post-get-pose" and event["self"] == pointer]
        chef_position_reads[path] = {
            "bodyPointer": pointer,
            "actorPointers": sorted({event["stack"][0] for event in left + right}),
            "original": [event["payloadFloat"][:7] for event in left],
            "replay": [event["payloadFloat"][:7] for event in right],
            "exactPoseParity": [payload_words(x) for x in left] == [payload_words(x) for x in right],
            "firstDifference": first_difference(left, right),
        }

    actor_kinds = ("np-rigid-dynamic-set-kinematic-target", "np-rigid-dynamic-set-global-pose")
    actor_calls = {}
    for kind in actor_kinds:
        pointers = sorted({event["self"] for event in original + replay if event["kind"] == kind})
        actor_calls[kind] = {}
        for pointer in pointers:
            left = [event for event in original if event["kind"] == kind and event["self"] == pointer]
            right = [event for event in replay if event["kind"] == kind and event["self"] == pointer]
            actor_calls[kind][pointer] = {
                "original": [event["payloadFloat"][:7] for event in left],
                "replay": [event["payloadFloat"][:7] for event in right],
                "exactPayloadParity": [payload_words(x) for x in left] == [payload_words(x) for x in right],
            }

    original_narrowphase = [event for event in original if event["kind"] == "pxc-discrete-narrowphase-pcm"]
    replay_narrowphase = [event for event in replay if event["kind"] == "pxc-discrete-narrowphase-pcm"]
    original_overlaps = [event for event in original if event["kind"] == "nphase-core-overlap-created"]
    replay_overlaps = [event for event in replay if event["kind"] == "nphase-core-overlap-created"]
    original_managers = [event for event in original if event["kind"] == "pxs-contact-manager-created"]
    replay_managers = [event for event in replay if event["kind"] == "pxs-contact-manager-created"]

    report = {
        "passed": True,
        "classification": "read-only marked native-call trace reduction; gameplay parity is reported separately",
        "source": str(args.input),
        "sourceSha256": hashlib.sha256(args.input.read_bytes()).hexdigest(),
        "traceModuleSha256": trace["nativeSha256"],
        "unityPlayerBase": trace["unityPlayerBase"],
        "eventCount": len(events), "latestSequence": trace["latestSequence"],
        "droppedEstimate": trace["droppedEstimate"], "missingMarkers": missing,
        "markers": marker_rows,
        "phaseCounts": {name: dict(collections.Counter(event["kind"] for event in rows))
                        for name, rows in phases.items()},
        "originalReplay": {
            "exactFullSignatureParity": [signature(x) for x in original] == [signature(x) for x in replay],
            "firstDifference": first_difference(original, replay),
            "byKind": by_kind,
            "chefMovePosition": chef_moves,
            "chefApplyConstraintsInputPose": chef_constraint_poses,
            "chefGetPositionPose": chef_position_reads,
            "physxActorCalls": actor_calls,
            "narrowPhasePcm": narrowphase_comparison(original_narrowphase, replay_narrowphase),
            "overlapCreated": {
                "originalCount": len(original_overlaps), "replayCount": len(replay_overlaps),
                "exactSignatureParity": [signature(x) for x in original_overlaps] ==
                                        [signature(x) for x in replay_overlaps],
                "firstDifference": first_difference(original_overlaps, replay_overlaps),
                "original": [compact(x) for x in original_overlaps],
                "replay": [compact(x) for x in replay_overlaps],
            },
            "contactManagerCreated": {
                "originalCount": len(original_managers), "replayCount": len(replay_managers),
                "exactSignatureParity": [signature(x) for x in original_managers] ==
                                        [signature(x) for x in replay_managers],
                "firstDifference": first_difference(original_managers, replay_managers),
                "semanticAllocation": contact_manager_comparison(original_managers, replay_managers),
                "original": [compact(x) for x in original_managers],
                "replay": [compact(x) for x in replay_managers],
            },
        },
        "endpointChefs": endpoint_rows(data),
        "nativeRestore": data["nativeRestore"],
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({
        "passed": True, "eventCount": report["eventCount"],
        "droppedEstimate": report["droppedEstimate"],
        "phaseCounts": report["phaseCounts"],
        "exactFullSignatureParity": report["originalReplay"]["exactFullSignatureParity"],
        "firstDifference": report["originalReplay"]["firstDifference"],
        "endpointChefs": report["endpointChefs"], "evidence": str(args.out),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
