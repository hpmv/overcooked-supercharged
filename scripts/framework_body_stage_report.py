"""Compare captured target body invariants with ordered native warp-stage receipts.

Read-only native JSON input; no game connection, canonicalization or tolerance.
Reports target differences separately from changes since the previous observed
stage. A phase label identifies the observation boundary, not the exact native
instruction that caused a change between two boundaries.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path

from compare_framework_frames import first_difference


INVARIANTS = ("mass", "centerOfMass", "inertiaTensor", "inertiaTensorRotation", "drag", "angularDrag",
              "constraints", "interpolation", "collisionDetectionMode", "detectCollisions",
              "sleepThreshold", "maxAngularVelocity")
IDENTITY = ("entityId", "bodyInstanceId", "transformInstanceId", "parentInstanceId")
PHASE = ("rawIsKinematic", "rawUseGravity", "rawVelocity", "rawAngularVelocity")


def different_fields(expected, actual, keys):
    result = []
    for key in keys:
        # Missing differs from explicit null and is never synthesized as zero.
        a = {key: expected[key]} if key in expected else {}
        b = {key: actual[key]} if key in actual else {}
        difference = first_difference(a, b, "$body")
        if difference is not None:
            result.append({"field": key, "expectedPresent": key in expected, "actualPresent": key in actual,
                           "expected": expected.get(key), "actual": actual.get(key), "firstDifference": difference})
    return result


def body_rows(document):
    if not isinstance(document, dict) or document.get("source") != "native-registered-Rigidbody-and-Transform":
        raise ValueError("Missing native saved/current body checkpoint source")
    rows = document.get("bodies")
    if not isinstance(rows, list) or not rows:
        raise ValueError("Missing native body observations")
    result = {}
    for row in rows:
        if not isinstance(row, dict) or type(row.get("entityId")) is not int or row["entityId"] in result:
            raise ValueError("Missing or duplicate native body identity")
        missing = [k for k in INVARIANTS + IDENTITY if k not in row]
        if missing:
            raise ValueError("Incomplete native body invariant/identity fields: " + ",".join(missing))
        result[row["entityId"]] = row
    return result


def interpret(document):
    if "response" in document:
        document = document["response"]
    bridge = document.get("bridge") or {}
    checkpoint = bridge.get("nativeCheckpoints") or {}
    target = body_rows(checkpoint.get("requestedBodyCheckpoint"))
    log = checkpoint.get("nativeBodyWarpStages") or {}
    if log.get("scope") != "read-only-current-authoring-warp-stage-observations":
        raise ValueError("Native warp-stage diagnostics are unavailable")
    frame = checkpoint.get("requestedBodyCheckpointFrame")
    if type(frame) is not int or log.get("frame") != frame:
        raise ValueError("Stage log and requested target frame disagree")
    stages = log.get("stages")
    if not isinstance(stages, list) or not stages or len(stages) > 32:
        raise ValueError("Native stage observations are empty or exceed the declared bound")
    result = {"classification": "exact native body invariant stage interpretation; no tolerance or repair",
              "frame": frame, "nativeRestoreFailure": checkpoint.get("lastRestoreFailure"),
              "nativeRestore": checkpoint.get("lastRestore"), "frameworkAssembly": bridge.get("frameworkAssembly"),
              "discardedOlderStages": log.get("discardedOlderStages"), "stages": [],
              "firstObservedTargetDifferenceByEntity": {}, "firstObservedTransitionByEntity": {},
              "scope": "The first changed stage bounds the cause between observations; it does not identify a specific setter or callback. Target comparison and previous-stage comparison remain separate."}
    previous = None; previous_index = None
    complete = log.get("discardedOlderStages") == 0
    for index, entry in enumerate(stages):
        row = {"index": index, "stage": entry.get("stage"), "captured": entry.get("captured"), "bodies": []}
        result["stages"].append(row)
        if entry.get("captured") is not True:
            row["error"] = entry.get("error", "Missing native capture success")
            complete = False
            # A failure leaves an explicit observation gap; do not pretend the
            # preceding successful state is the immediately previous stage.
            previous = None; previous_index = None
            continue
        current = body_rows(entry.get("current"))
        if set(current) != set(target):
            row["membershipDifference"] = {"missing": sorted(set(target) - set(current)), "extra": sorted(set(current) - set(target))}
            complete = False
        for entity in sorted(current):
            actual = current[entity]; expected = target.get(entity)
            if expected is None:
                continue
            target_changes = different_fields(expected, actual, INVARIANTS)
            identity_changes = different_fields(expected, actual, IDENTITY)
            prior = previous.get(entity) if previous is not None else None
            transition = different_fields(prior, actual, INVARIANTS) if prior is not None else None
            phase_changes = different_fields(prior, actual, PHASE) if prior is not None else None
            if target_changes or identity_changes or transition or phase_changes:
                observed = {"entityId": entity, "targetIdentityDifferences": identity_changes,
                            "firstTargetInvariantDifference": target_changes[0] if target_changes else None,
                            "targetInvariantDifferences": target_changes,
                            "previousObservedStageIndex": previous_index,
                            "firstInvariantTransition": transition[0] if transition else None,
                            "invariantTransitions": transition,
                            "phaseChanges": phase_changes,
                            "targetPhase": {k: expected[k] for k in PHASE if k in expected},
                            "actualPhase": {k: actual[k] for k in PHASE if k in actual}}
                row["bodies"].append(observed)
            if target_changes and str(entity) not in result["firstObservedTargetDifferenceByEntity"]:
                result["firstObservedTargetDifferenceByEntity"][str(entity)] = {"index": index, "stage": entry.get("stage"),
                    "difference": target_changes[0], "presentAtFirstCapturedStage": previous is None}
            if transition and str(entity) not in result["firstObservedTransitionByEntity"]:
                result["firstObservedTransitionByEntity"][str(entity)] = {"index": index, "stage": entry.get("stage"),
                    "previousStageIndex": previous_index, "previousStage": stages[previous_index].get("stage"), "difference": transition[0]}
        row["unchangedBodyCount"] = len(current) - len(row["bodies"])
        row["observedBodyCount"] = len(current)
        previous = current; previous_index = index
    result["completeObservationSequence"] = complete
    result["hasObservedInvariantDifference"] = bool(result["firstObservedTargetDifferenceByEntity"])
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("receipt", type=Path)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.receipt.stat().st_size > 64 * 1024 * 1024:
        raise ValueError("Native diagnostic receipt exceeds 64MiB bound")
    raw = args.receipt.read_bytes()
    report = interpret(json.loads(raw.decode("utf-8-sig")))
    report["receipt"] = {"path": str(args.receipt.resolve()), "bytes": len(raw), "sha256": hashlib.sha256(raw).hexdigest()}
    report["interpreterSha256"] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    # Avoid accidentally overwriting either raw evidence or a prior report.
    with args.out.open("x", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
    print(json.dumps({k: report[k] for k in ("frame", "completeObservationSequence", "firstObservedTargetDifferenceByEntity", "firstObservedTransitionByEntity")}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
