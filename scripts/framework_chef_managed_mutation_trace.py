"""Trace managed local-chef physics writers across a paired idle rewind."""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--frames", type=int, default=12)
    parser.add_argument("--slot", default="chef-managed-mutation-tracer")
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if not 4 <= args.frames <= 60:
        parser.error("Use 4..60 frames")

    bridge = host = None
    began = time.monotonic()
    records = []
    report = {"passed": False, "completed": False, "frames": args.frames,
              "classification": "read-only managed-writer and Unity-phase trace"}

    def call(target, request, label, retain=True):
        response = (bridge if target == "bridge" else host).call(request)
        if retain:
            records.append({"label": label, "target": target, "request": request, "response": response})
        return response

    def wait_paused(label):
        deadline = time.monotonic() + 30
        while True:
            value = host.status()
            if value.get("errors") or value.get("traceFailure") or value.get("state") == "Error":
                raise RuntimeError(json.dumps(value))
            if value.get("state") == "Paused" and not value.get("requestPending"):
                return value
            if time.monotonic() >= deadline:
                raise TimeoutError(label + " did not pause")
            time.sleep(.025)

    def module(operation, label, values=None, retain=True):
        value = call("bridge", {"command": "hot-call", "slot": args.slot,
                                "operation": operation, "args": values or {}}, label, retain)
        return value["detail"]["result"]

    def step(label):
        start = wait_paused(label + "-before")["frame"]
        call("bridge", {"command": "arm"}, label + "-arm")
        call("controller", {"command": "step", "frames": args.frames}, label + "-step")
        state = wait_paused(label + "-after")
        if state["frame"] != start + args.frames:
            raise RuntimeError("Advancing frame count differs")
        call("bridge", {"command": "pause"}, label + "-fence")
        return state

    def active_updates(status, segment):
        return [row for row in status["phases"]
                if row["segment"] == segment and row["phase"] == "update" and not row["paused"]]

    def y_sequences(status, segment):
        rows = active_updates(status, segment)
        ids = status["chefEntityIds"]
        return {str(entity): [next(chef["bodyYBits"] for chef in row["chefs"]
                                   if chef["entityId"] == entity) for row in rows]
                for entity in ids}

    def call_summary(status, segment):
        selected = [row for row in status["calls"] if row["segment"] == segment]
        called = Counter(row["method"] for row in selected)
        changed = Counter(row["method"] for row in selected if row["changed"])
        return [{"method": method, "calls": count, "publicStateChanges": changed[method]}
                for method, count in sorted(called.items())]

    def native_gaps(status, segment):
        phase_rows = sorted((row for row in status["phases"]
                             if row["segment"] == segment and not row["paused"]),
                            key=lambda row: row["sequence"])
        call_rows = [row for row in status["calls"] if row["segment"] == segment]
        gaps = []
        for before, after in zip(phase_rows, phase_rows[1:]):
            old = {row["entityId"]: row["bodyYBits"] for row in before["chefs"]}
            new = {row["entityId"]: row["bodyYBits"] for row in after["chefs"]}
            changed = {str(entity): {"before": old[entity], "after": new[entity]}
                       for entity in old if old[entity] != new[entity]}
            if not changed:
                continue
            between = [row for row in call_rows if before["sequence"] < row["sequence"] < after["sequence"]]
            gaps.append({"beforeSequence": before["sequence"], "beforePhase": before["phase"],
                         "afterSequence": after["sequence"], "afterPhase": after["phase"],
                         "yChanges": changed,
                         "managedCallsBetween": [{"method": row["method"], "changed": row["changed"]}
                                                 for row in between]})
        return gaps

    try:
        bridge, host = Client(args.bridge_port), ControllerClient(args.controller_port)
        state = wait_paused("initial")
        native = call("bridge", {"command": "pause"}, "initial-fence")["bridge"]
        if native.get("session", {}).get("scene") != "s_sushi_1_1" or not native.get("inputBlocked"):
            raise RuntimeError("Requires fenced Story 1-1")
        status = module("clear", "clear", retain=False)
        if not status.get("active") or status.get("failure") is not None or len(status.get("chefEntityIds", [])) != 4:
            raise RuntimeError("Managed mutation tracer is not healthy")
        frame = state["frame"]
        key = hashlib.sha256(str(args.out.resolve()).encode()).hexdigest()[:16]
        call("controller", {"command": "checkpoint", "path": "chef-managed-trace-" + key + "-" + str(frame) + ".pb"}, "checkpoint")
        module("mark", "mark-original", {"label": "original"}, retain=False)
        original_state = step("original")
        original_status = module("status", "original-status", retain=False)
        attempt = native.get("nativeCheckpoints", {}).get("restoreAttempts", 0)

        call("bridge", {"command": "arm"}, "warp-arm")
        call("controller", {"command": "warp", "frame": frame, "development": True}, "warp")
        restored_state = wait_paused("restored")
        call("bridge", {"command": "pause"}, "restored-fence")
        restored_native = call("bridge", {"command": "status"}, "restored-native")["bridge"]
        receipt = restored_native.get("nativeCheckpoints", {}).get("lastRestore")
        if restored_state["frame"] != frame or not receipt or receipt.get("verified") is not True \
                or receipt.get("attempt", -1) <= attempt:
            raise RuntimeError("No new exact restore acknowledgement")
        module("mark", "mark-replay", {"label": "replay"}, retain=False)
        replay_state = step("replay")
        final_status = module("status", "final-status", retain=False)
        if final_status.get("failure") is not None or final_status.get("discardedCalls") \
                or final_status.get("discardedPhases"):
            raise RuntimeError("Tracer failed or exceeded its evidence bounds")

        original_y = y_sequences(final_status, "original")
        replay_y = y_sequences(final_status, "replay")
        report.update(
            passed=True,
            completed=True,
            checkpointFrame=frame,
            endFrames={"original": original_state["frame"], "replay": replay_state["frame"]},
            patchedMethodCount=len(final_status["patchedMethods"]),
            originalYBits=original_y,
            replayYBits=replay_y,
            publicYParity=original_y == replay_y,
            originalManagedCalls=call_summary(final_status, "original"),
            replayManagedCalls=call_summary(final_status, "replay"),
            originalNativePhaseGaps=native_gaps(final_status, "original"),
            replayNativePhaseGaps=native_gaps(final_status, "replay"),
            nativeRestore=receipt,
            tracer=final_status,
        )
    except Exception as error:
        report["error"] = str(error)
    finally:
        try:
            if bridge is not None:
                call("bridge", {"command": "pause"}, "finally-pause")
        except Exception as error:
            report["pauseError"] = str(error)
            report["passed"] = False
        for client in (bridge, host):
            try:
                if client is not None:
                    client.close()
            except Exception as error:
                report["closeError"] = str(error)
                report["passed"] = False
        report["wallSeconds"] = time.monotonic() - began
        report["records"] = records
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding="utf8")
        console = {key: value for key, value in report.items() if key not in ("records", "tracer")}
        print(json.dumps(console, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
