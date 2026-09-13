"""Run an isolated capsule/floor rewind control inside the original game.

The helper is already loaded and active. This runner records one neutral
continuation, authoring-restores its exact paused checkpoint, records the same
continuation again, and compares the probe's active-render samples exactly.
It reports the game's own chef drift but does not require chef parity: the
experiment's question is whether the isolated same-process probe alternates.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--frames", type=int, default=60)
    parser.add_argument("--slot", default="inprocess-physics-repro")
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists; choose a new evidence path")
    if not 10 <= args.frames <= 300:
        parser.error("Use 10..300 continuation frames")

    bridge = host = None
    began = time.monotonic()
    evidence_key = hashlib.sha256(str(args.out.resolve()).encode()).hexdigest()[:16]
    records = []
    report = {
        "passed": False,
        "completed": False,
        "classification": "same-process isolated Unity/PhysX rewind control",
        "scriptSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "frames": args.frames,
    }

    def call(target, request, label):
        started = time.monotonic()
        response = (bridge if target == "bridge" else host).call(request)
        records.append({"label": label, "target": target, "request": request,
                        "wallSeconds": time.monotonic() - started, "response": response})
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
                raise TimeoutError(label + " did not reach paused controller state")
            time.sleep(.025)

    def module(operation, label, values=None):
        response = call("bridge", {"command": "hot-call", "slot": args.slot,
                                   "operation": operation, "args": values or {}}, label)
        return response["detail"]["result"]

    def mark(label):
        return module("mark", "mark-" + label, {"label": label})

    def step(count, label):
        start = wait_paused(label + "-before")["frame"]
        call("bridge", {"command": "arm"}, label + "-arm")
        call("controller", {"command": "step", "frames": count}, label + "-step")
        state = wait_paused(label + "-after")
        if state["frame"] != start + count:
            raise RuntimeError("Observed advancing frame count differs")
        call("bridge", {"command": "pause"}, label + "-fence")
        return state, module("status", label + "-status")

    def active_rows(status, segment):
        return [row for row in status["samples"]
                if row["segment"] == segment and row["stage"] == "late-update" and not row["paused"]]

    def comparable(row):
        return {key: row[key] for key in (
            "position", "transformPosition", "localPosition", "velocity", "angularVelocity",
            "bodyYBits", "transformYBits", "localYBits", "kinematic", "gravity", "sleeping")}

    try:
        bridge, host = Client(args.bridge_port), ControllerClient(args.controller_port)
        initial_host = wait_paused("initial")
        native = call("bridge", {"command": "pause"}, "initial-fence")["bridge"]
        if native.get("session", {}).get("scene") != "s_sushi_1_1" or not native.get("paused") \
                or not native.get("inputBlocked") or native.get("fixedDeltaTime") != .02:
            raise RuntimeError("Requires paused, fenced Story 1-1 with 0.02 fixedDeltaTime")
        initial = module("status", "initial-module")
        if not initial.get("active") or not initial.get("probeAlive") or initial.get("failure") is not None:
            raise RuntimeError("In-process probe is not active and healthy")
        if initial["current"]["bodyYBits"] != 0 or not initial["current"]["kinematic"]:
            raise RuntimeError("Initial paused probe must be exactly Y=0 and kinematic")
        # Give NativeKitchenCheckpoint a paused main-thread capture after probe
        # creation, then use that exact logical frame as the rewind target.
        for index in range(3):
            module("status", "checkpoint-capture-pump-" + str(index))
        baseline = module("status", "baseline-module")
        frame = wait_paused("baseline")["frame"]
        if baseline.get("captures", 0) == 0:
            raise RuntimeError("Probe did not bind a native checkpoint")
        call("controller", {"command": "checkpoint", "path": "inprocess-physics-repro-" + evidence_key + "-" + str(frame) + ".pb"}, "checkpoint")

        mark("original")
        original_state, original_status = step(args.frames, "original")
        original = active_rows(original_status, "original")
        restore_attempt = native.get("nativeCheckpoints", {}).get("restoreAttempts", 0)

        call("bridge", {"command": "arm"}, "warp-arm")
        call("controller", {"command": "warp", "frame": frame, "development": True}, "warp")
        restored_state = wait_paused("restored")
        call("bridge", {"command": "pause"}, "restored-fence")
        restored = module("status", "restored-module")
        current_native = call("bridge", {"command": "status"}, "restored-native")["bridge"]
        receipt = current_native.get("nativeCheckpoints", {}).get("lastRestore")
        if restored_state["frame"] != frame or not receipt or receipt.get("verified") is not True \
                or receipt.get("frame") != frame or receipt.get("attempt", -1) <= restore_attempt:
            raise RuntimeError("No new exact native restore acknowledgement")
        if restored.get("restores", 0) == 0 or restored["current"]["bodyYBits"] != 0:
            raise RuntimeError("Probe checkpoint was not restored to exact Y=0")

        mark("replay")
        replay_state, replay_status = step(args.frames, "replay")
        replay = active_rows(replay_status, "replay")
        left = [comparable(row) for row in original]
        right = [comparable(row) for row in replay]
        first_difference = None
        for index in range(max(len(left), len(right))):
            a = left[index] if index < len(left) else None
            b = right[index] if index < len(right) else None
            if a != b:
                first_difference = {"index": index, "original": a, "replay": b}
                break
        report.update(
            completed=True,
            checkpointFrame=frame,
            endFrames={"original": original_state["frame"], "replay": replay_state["frame"]},
            sampleCounts={"original": len(left), "replay": len(right)},
            originalYBits=[row["bodyYBits"] for row in original],
            replayYBits=[row["bodyYBits"] for row in replay],
            firstDifference=first_difference,
            nativeRestore=receipt,
            moduleStatus=replay_status,
        )
        report["passed"] = first_difference is None and len(left) == args.frames and len(right) == args.frames
        report["conclusion"] = (
            "The isolated capsule/floor remained exact inside the original game process."
            if report["passed"] else
            "The isolated capsule/floor diverged inside the original game process; inspect firstDifference and module events."
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
        print(json.dumps({key: value for key, value in report.items()
                          if key not in ("records", "moduleStatus")}, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
