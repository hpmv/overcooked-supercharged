"""Compare original/replay halves of a split native pose+mass trace."""
from __future__ import annotations

import argparse
import collections
import difflib
import hashlib
import json
from pathlib import Path


def integer(value: str) -> int:
    return int(value, 16)


def load_phase(path: Path, start_code: int, end_code: int) -> tuple[dict, list[dict], list[dict]]:
    root = json.loads(path.read_text(encoding="utf-8-sig"))
    receipt = root["result"]
    events = receipt["events"]
    markers = {integer(event["returnAddress"]): event["sequence"]
               for event in events if event["kindId"] == 1}
    if start_code not in markers or end_code not in markers:
        raise RuntimeError(f"{path} lacks markers {start_code}/{end_code}")
    phase = [event for event in events
             if markers[start_code] < event["sequence"] < markers[end_code]]
    return receipt, events, phase


def event_payload(event: dict) -> tuple:
    kind = event["kindId"]
    if kind == 12:
        return tuple(event["payload"][:3])
    if kind in (30, 31, 32, 33, 34):
        return tuple(event["payload"][:7])
    if kind == 35:
        return tuple(event["payload"][:3])
    if kind == 36:
        return tuple(event["payload"][:5])
    if kind == 37:
        return tuple(event["stack"][:4] + event["payload"][:7] + event["extra"][:3])
    if kind == 38:
        # stack[1] is the enclosing trace sequence, not a physics input.
        return tuple([event["stack"][0]] + event["stack"][2:8] +
                     event["payload"][:7] + event["extra"])
    if kind == 13:
        return tuple(event["stack"][1:2])
    return tuple(event["payload"])


def signature(event: dict) -> tuple:
    return event["kind"], event["self"], event["returnAddress"], event_payload(event)


def structural_signature(event: dict) -> tuple:
    return event["kind"], event["self"], event["returnAddress"]


def compact(event: dict | None) -> dict | None:
    if event is None:
        return None
    return {
        "sequence": event["sequence"], "kind": event["kind"], "self": event["self"],
        "returnAddress": event["returnAddress"], "payload": list(event_payload(event)),
        "payloadFloat": event["payloadFloat"], "stack": event["stack"],
    }


def first_difference(left: list[dict], right: list[dict]) -> dict | None:
    for index in range(max(len(left), len(right))):
        a = left[index] if index < len(left) else None
        b = right[index] if index < len(right) else None
        if a is None or b is None or signature(a) != signature(b):
            return {"index": index, "original": compact(a), "replay": compact(b),
                    "originalContext": [compact(x) for x in left[max(0, index-2):index+3]],
                    "replayContext": [compact(x) for x in right[max(0, index-2):index+3]]}
    return None


def mass_calls(events: list[dict], body: str) -> list[dict]:
    calls = []
    pending = collections.defaultdict(collections.deque)
    shapes = collections.defaultdict(list)
    for event in events:
        if event["kindId"] == 38:
            shapes[integer(event["stack"][1])].append(event)
        elif event["kindId"] == 36 and event["self"] == body:
            row = {"pre": event, "shapes": shapes[event["sequence"]]}
            pending[(event["threadId"], event["self"], event["returnAddress"])].append(row)
        elif event["kindId"] == 37 and event["self"] == body:
            key = (event["threadId"], event["self"], event["returnAddress"])
            row = pending[key].popleft() if pending[key] else {"pre": None, "shapes": []}
            row["post"] = event
            calls.append(row)
    return calls


def mass_input_signature(call: dict) -> tuple:
    pre = call["pre"]
    return ((pre["returnAddress"], tuple(pre["payload"][:5])) if pre else None,
            tuple(event_payload(shape) for shape in call["shapes"]))


def call_compact(call: dict | None) -> dict | None:
    if call is None:
        return None
    return {"preSequence": call["pre"]["sequence"] if call["pre"] else None,
            "postSequence": call["post"]["sequence"],
            "returnAddress": call["post"]["returnAddress"],
            "shapeInputs": [list(event_payload(shape)) for shape in call["shapes"]],
            "result": list(event_payload(call["post"]))}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", required=True, type=Path)
    parser.add_argument("--replay", required=True, type=Path)
    parser.add_argument("--out", required=True, type=Path)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")

    original_receipt, _, original = load_phase(args.original, 220, 230)
    replay_receipt, _, replay = load_phase(args.replay, 260, 270)
    original_chefs = {row["path"]: row["bodyPointer"] for row in original_receipt["chefs"]}
    replay_chefs = {row["path"]: row["bodyPointer"] for row in replay_receipt["chefs"]}
    if original_chefs != replay_chefs:
        raise RuntimeError("Chef native pointers changed between split traces")
    body = original_chefs["Chefs/Player 1"]
    original_mass = mass_calls(original, body)
    replay_mass = mass_calls(replay, body)
    if not original_mass or not replay_mass:
        raise RuntimeError("Chef 44 mass calls are absent")
    actor = original_mass[0]["pre"]["payload"][0]
    if replay_mass[0]["pre"]["payload"][0] != actor:
        raise RuntimeError("Chef 44 actor pointer changed")

    relevant_original = [event for event in original if event["self"] in (body, actor)]
    relevant_replay = [event for event in replay if event["self"] in (body, actor)]
    structure = difflib.SequenceMatcher(None,
        [structural_signature(event) for event in relevant_original],
        [structural_signature(event) for event in relevant_replay], autojunk=False)
    mass_alignment = difflib.SequenceMatcher(None,
        [mass_input_signature(call) for call in original_mass],
        [mass_input_signature(call) for call in replay_mass], autojunk=False)
    mass_ops = [{"tag": tag, "original": [i1, i2], "replay": [j1, j2]}
                for tag, i1, i2, j1, j2 in mass_alignment.get_opcodes() if tag != "equal"]

    kinds = sorted(set(event["kind"] for event in original + replay))
    report = {
        "passed": True,
        "classification": "read-only split native pose/mass trace comparison",
        "sources": {
            "original": str(args.original.resolve()),
            "originalSha256": hashlib.sha256(args.original.read_bytes()).hexdigest(),
            "replay": str(args.replay.resolve()),
            "replaySha256": hashlib.sha256(args.replay.read_bytes()).hexdigest(),
        },
        "trace": {"nativeSha256": original_receipt["nativeSha256"],
                  "mask": original_receipt["installedMask"],
                  "originalDropped": original_receipt["droppedEstimate"],
                  "replayDropped": replay_receipt["droppedEstimate"]},
        "chef44": {"body": body, "actor": actor},
        "phaseCounts": {kind: {"original": sum(e["kind"] == kind for e in original),
                               "replay": sum(e["kind"] == kind for e in replay)} for kind in kinds},
        "firstGlobalDifference": first_difference(original, replay),
        "relevant": {
            "originalCount": len(relevant_original), "replayCount": len(relevant_replay),
            "firstExactDifference": first_difference(relevant_original, relevant_replay),
            "structuralOpcodes": [{"tag": tag, "original": [i1, i2], "replay": [j1, j2]}
                                  for tag, i1, i2, j1, j2 in structure.get_opcodes() if tag != "equal"],
        },
        "mass": {
            "originalCount": len(original_mass), "replayCount": len(replay_mass),
            "firstInputDifference": next(({
                "ordinal": index + 1,
                "original": call_compact(original_mass[index]),
                "replay": call_compact(replay_mass[index]),
            } for index in range(min(len(original_mass), len(replay_mass)))
                if mass_input_signature(original_mass[index]) != mass_input_signature(replay_mass[index])), None),
            "alignmentOpcodes": mass_ops,
        },
    }
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps({"passed": True, "chef44": report["chef44"],
                      "phaseCounts": report["phaseCounts"],
                      "firstGlobalDifference": report["firstGlobalDifference"],
                      "relevant": report["relevant"], "mass": {
                          "originalCount": report["mass"]["originalCount"],
                          "replayCount": report["mass"]["replayCount"],
                          "firstInputDifference": report["mass"]["firstInputDifference"],
                          "alignmentOpcodes": report["mass"]["alignmentOpcodes"],
                      }, "evidence": str(args.out)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
