"""Read-only R5 saved spawn/deletion/respawn audit; never connects to the game."""
import argparse
import hashlib
import json
from pathlib import Path

from compare_framework_frames import records, first_difference, without_metadata
from framework_spawn_probe import prepare_case, held_egg, require_deletion, compare_logical


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--probe", type=Path, required=True)
    ap.add_argument("--body-status", type=Path, required=True)
    ap.add_argument("--out", type=Path, required=True)
    args = ap.parse_args()
    summary_path = args.probe / "summary.json"; observations_path = args.probe / "observations.json"
    summary = json.loads(summary_path.read_text()); observations = json.loads(observations_path.read_text())
    get = lambda label: [r["response"] for r in observations if r["label"] == label][-1]
    case = prepare_case(get("base"), get("base-native"))
    original_goal = held_egg(case, get("original"), get("original-native"))
    deletion = require_deletion(original_goal, get("first-deletion-state"), get("first-deletion-native"))
    retired = {original_goal["egg"], original_goal["container"]}
    baseline = compare_logical(get("base"), get("base-native"), get("first-deletion-state"), get("first-deletion-native"), actual_retired=retired)
    fixed_goal = held_egg(case, get("fixed-input"), get("fixed-input-native"), retired)
    endpoint = compare_logical(get("original"), get("original-native"), get("fixed-input"), get("fixed-input-native"), actual_retired=retired)
    remap = {str(fixed_goal[k]): str(original_goal[k]) for k in ("egg", "container")}
    args.out.mkdir(exist_ok=True)
    windows = {}; files = [summary_path, observations_path, args.body_status, Path(__file__),
        Path(__file__).with_name("framework_spawn_probe.py"), Path(__file__).with_name("compare_framework_frames.py")]
    for w in summary["traceWindows"]:
        with open(w["source"], "rb") as f:
            f.seek(w["startByteInclusive"]); data = f.read(w["endByteExclusive"] - w["startByteInclusive"])
        if hashlib.sha256(data).hexdigest() != w["sha256"]:
            raise ValueError("Saved trace window hash changed")
        path = args.out / (w["label"] + "-exact-window.jsonl")
        if path.exists() and path.read_bytes() != data: raise ValueError("Refusing to overwrite a different trace window")
        if not path.exists(): path.write_bytes(data)
        files.append(path)
        rows = [(r, loc) for r, loc in records(path) if r.get("kind") == "exchange"]
        advancing = [(r, loc) for r, loc in rows if r["output"]["LastFramePaused"] is False]
        start = case["frame"]; end = start + original_goal["framesIncludingRelease"]
        if [r["output"]["FrameNumber"] for r, _ in advancing] != list(range(start + 1, end + 1)):
            raise ValueError("Advancing callback interval is missing, duplicated or reordered")
        if any(len(r["output"]["Chefs"]) != 4 for r, _ in advancing): raise ValueError("Incomplete chef observation")
        if any((r.get("input") or {}).get("Warp") is not None for r, _ in rows): raise ValueError("Warp inside advancing leg")
        windows[w["label"]] = {r["output"]["FrameNumber"]: (r["output"], loc) for r, loc in advancing}
    names = ("Chefs", "Items", "ServerMessages", "EntityRegistry", "PhysicsFramesElapsed", "FramesSinceLastNoPhysicsFrame", "LastFramePaused", "NextFramePaused")
    first = {k: None for k in names}; logical_items_first = None; changed_frames = []
    frame_evidence = {}
    for frame in range(start + 1, end + 1):
        a, aloc = windows["original"][frame]; b, bloc = windows["fixed"][frame]
        for name in names:
            diff = first_difference(without_metadata(a.get(name)), without_metadata(b.get(name)), "$output/" + name)
            if diff and first[name] is None: first[name] = dict(diff, frame=frame, originalLocation=aloc, fixedLocation=bloc)
        ai = without_metadata(a.get("Items") or {}); bi = without_metadata(b.get("Items") or {})
        bi = {remap.get(i, i): v for i, v in bi.items()}
        diff = first_difference(ai, bi, "$observedItemFields")
        if diff:
            changed_frames.append(frame)
            if logical_items_first is None: logical_items_first = dict(diff, frame=frame)
        if frame >= end - 3:
            frame_evidence[str(frame)] = {"original": {"items": ai, "phase": {k: a[k] for k in names[4:]}},
                "fixed": {"items": bi, "phase": {k: b[k] for k in names[4:]}}, "firstDifference": diff}
    ainputs = args.probe / "original-inputs.json"; binputs = args.probe / "fixed-inputs.json"; files += [ainputs, binputs]
    inputs = first_difference(json.loads(ainputs.read_text()), json.loads(binputs.read_text()), "$acceptedInputs")
    if inputs or first["Chefs"] or any(first[k] for k in names[4:]) \
            or logical_items_first is None or logical_items_first["frame"] != 58 or changed_frames != [58, 59]:
        raise ValueError("Saved experiment no longer supports the specific R5 frame58 interpretation")
    body = json.loads(args.body_status.read_text())["result"]
    mass_rows = [r for r in body["result"]["massRestores"] if r.get("positionPreimageAttempts")]
    if len(mass_rows) != 1: raise ValueError("Expected one separately observed R5 native position correction")
    mass = mass_rows[0]; attempts = mass["positionPreimageAttempts"]
    if len(attempts) != 2 or attempts[0]["exact"] is not False or attempts[1]["exact"] is not True \
            or first_difference(attempts[-1]["target"], attempts[-1]["readback"]) is not None \
            or mass["exact"] is not True or first_difference(mass["target"], mass["after"]) is not None:
        raise ValueError("R5 native correction does not match its claimed exact outcome")
    proxy_bodies = {label: next(b for b in receipt["bridge"]["nativePhysics"]["bodies"] if b["entityId"] == goal["container"])
        for label, receipt, goal in (("original", get("original-native"), original_goal), ("fixed", get("fixed-input-native"), fixed_goal))}
    report = {"auditCompleted": True, "fullProbePassed": False, "firstDeletionPassed": deletion["passed"] and baseline["passed"],
        "originalGoal": original_goal, "respawnGoal": fixed_goal, "deletion": deletion, "baselineComparison": baseline,
        "respawnEndpointComparison": endpoint, "bodyModule": body["module"], "nativePositionCorrection": mass,
        "endpointProxyRawBodies": proxy_bodies,
        "acceptedInputFirstDifference": inputs, "frameInterval": {"startExclusive": start, "endInclusive": end},
        "rawOutputFirstDifferences": first, "logicalObservedItemFirstDifference": logical_items_first,
        "changedLogicalItemFrames": changed_frames, "lastFrames": frame_evidence,
        "projection": "Frame comparison removes only Thrift __isset metadata and remaps Items keys by exact held-Egg/PhysicalAttachment ownership receipts. All emitted field values, signed zeros, emission presence and phase fields remain exact. No full physical state is inferred for fields absent from a callback.",
        "interpretation": ["The fixed baseline is exact under the separately named retired-ID projection.",
            "All four-chef control observations and advancing phase fields match throughout32..59; emitted item fields match through57 after proven identity remap.",
            "Native spawn/attach first changes raw IDs at57. First geometric difference is chef104 at58, concurrently with original proxy moving/rotating while replay proxy retains its spawn pose. Egg root still matches at58 and differs at59.",
            "Endpoint proxy discrepancy is also present in raw nativePhysics, so it is not solely a missing observer emission.",
            "Native scheduler residual/update order, compound collider mass updates and internal PhysX state are candidate explanations, not demonstrated causes. This evidence does not warrant another body restore revision."],
        "limitations": "Two exact retained windows from an operator-owned trace; whole source file closure/global warp epoch not claimed. Native food/private clocks/raw Rigidbody diagnostics are endpoint evidence, not per-frame captures. No dynamic-target recreation or whole-game parity.",
        "traceWindows": summary["traceWindows"], "sources": {str(p.resolve()): sha(p) for p in files}}
    (args.out / "report.json").write_text(json.dumps(report, indent=2))
    print(json.dumps({k: report[k] for k in ("auditCompleted", "firstDeletionPassed", "acceptedInputFirstDifference", "logicalObservedItemFirstDifference", "changedLogicalItemFrames")}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
