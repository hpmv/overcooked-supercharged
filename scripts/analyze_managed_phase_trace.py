"""Reduce paired managed phase traces to one chef's FixedUpdate and phase states."""
import argparse
import json
from pathlib import Path


def target(states, entity):
    return next(state for state in states if state["entityId"] == entity)


def load(path, entity):
    root = json.loads(path.read_text(encoding="utf-8-sig"))["result"]
    fixed = []
    for call in root["calls"]:
        if call["method"] != "PlayerControls::Void FixedUpdate()":
            continue
        before, after = target(call["before"], entity), target(call["after"], entity)
        if before != after:
            fixed.append({"sequence": call["sequence"], "unityFrame": call["unityFrame"],
                          "fixedTime": call["fixedTime"], "before": before, "after": after})
    phases = [{"sequence": row["sequence"], "phase": row["phase"],
               "unityFrame": row["unityFrame"], "fixedTime": row["fixedTime"],
               "state": target(row["chefs"], entity)} for row in root["phases"]]
    return {"path": str(path.resolve()), "calls": len(root["calls"]),
            "phases": len(root["phases"]), "discardedCalls": root["discardedCalls"],
            "discardedPhases": root["discardedPhases"], "failure": root["failure"],
            "changedTargetFixedUpdates": fixed, "phaseSamples": phases}


def first_difference(left, right, path="$"):
    if type(left) is not type(right):
        return {"path": path, "original": left, "replay": right}
    if isinstance(left, dict):
        if left.keys() != right.keys():
            return {"path": path, "originalKeys": list(left), "replayKeys": list(right)}
        for key in left:
            difference = first_difference(left[key], right[key], f"{path}/{key}")
            if difference:
                return difference
    elif isinstance(left, list):
        if len(left) != len(right):
            return {"path": path + "/length", "original": len(left), "replay": len(right)}
        for index, (a, b) in enumerate(zip(left, right)):
            difference = first_difference(a, b, f"{path}/{index}")
            if difference:
                return difference
    elif left != right:
        return {"path": path, "original": left, "replay": right}
    return None


def comparable(row):
    return {"before": row["before"], "after": row["after"]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--original", type=Path, required=True)
    parser.add_argument("--replay", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--entity", type=int, default=44)
    args = parser.parse_args()
    original, replay = load(args.original, args.entity), load(args.replay, args.entity)
    a = [comparable(row) for row in original["changedTargetFixedUpdates"]]
    b = [comparable(row) for row in replay["changedTargetFixedUpdates"]]
    result = {"entityId": args.entity, "original": original, "replay": replay,
              "firstChangedFixedUpdateDifference": first_difference(a, b),
              "scope": "Read-only reduction of saved managed observer receipts; no game access."}
    args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({
        "original": {"changedFixedUpdates": len(a), "phaseSamples": original["phases"],
                     "first": original["changedTargetFixedUpdates"][:3]},
        "replay": {"changedFixedUpdates": len(b), "phaseSamples": replay["phases"],
                   "first": replay["changedTargetFixedUpdates"][:3]},
        "firstDifference": result["firstChangedFixedUpdateDifference"]}, indent=2))


if __name__ == "__main__":
    main()
