"""Bounded Story 1-1 paired idle rewind for an external native debugger.

By default the driver performs the original fresh level load.  ``--reuse-current``
instead keeps the already paused kitchen and performs no level transition.  The
driver does not search or mutate gameplay state beyond that optional load, neutral
frame stepping, checkpoint, and development rewind.
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
    parser.add_argument("--warmup", type=int, default=30)
    parser.add_argument("--frames", type=int, default=12)
    parser.add_argument("--load-timeout", type=float, default=165)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--inprocess-trace", action="store_true",
                        help="Clear/mark/read the active native-physics-trace module")
    parser.add_argument("--reuse-current", action="store_true",
                        help="Trace the current paused Story 1-1 kitchen without reloading it")
    parser.add_argument("--load-only", action="store_true",
                        help="Perform only the fresh Story 1-1 load and paused neutral validation")
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if not 0 <= args.warmup <= 120 or not 2 <= args.frames <= 30:
        parser.error("Use warmup 0..120 and paired frames 2..30")
    if not 10 <= args.load_timeout <= 180:
        parser.error("Use a load timeout from 10 to 180 seconds")
    if args.load_only and args.reuse_current:
        parser.error("--load-only requires the fresh-load mode")

    bridge = host = None
    began = time.monotonic()
    records = []
    report = {
        "passed": False,
        "completed": False,
        "classification": ("current native Story 1-1 paired neutral rewind driver" if args.reuse_current
                           else "fresh native Story 1-1 lifecycle plus paired neutral rewind driver"),
        "warmupFrames": args.warmup,
        "continuationFrames": args.frames,
        "sourceSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
    }

    def call(target, request, label, retain=True):
        started = time.monotonic()
        response = (bridge if target == "bridge" else host).call(request)
        if retain:
            records.append({
                "label": label,
                "target": target,
                "request": request,
                "wallOffsetSeconds": started - began,
                "wallSeconds": time.monotonic() - started,
                "response": response,
            })
        return response

    def wait_paused(label, timeout=45):
        deadline = time.monotonic() + timeout
        while True:
            value = host.status()
            if value.get("errors") or value.get("traceFailure") or value.get("state") == "Error":
                raise RuntimeError(label + ": " + json.dumps(value))
            if value.get("state") == "Paused" and not value.get("requestPending"):
                return value
            if time.monotonic() >= deadline:
                raise TimeoutError(label + " did not pause")
            time.sleep(.025)

    def step(frames, label):
        start = wait_paused(label + "-before")["frame"]
        call("bridge", {"command": "arm"}, label + "-arm")
        call("host", {"command": "step", "frames": frames}, label + "-step")
        state = wait_paused(label + "-after")
        if state["frame"] != start + frames:
            raise RuntimeError(label + " advanced an unexpected frame count")
        native = call("bridge", {"command": "pause"}, label + "-fence")
        return state, native

    def mark(code, value, label):
        if args.inprocess_trace:
            call("bridge", {"command": "hot-call", "slot": "native-physics-trace",
                 "operation": "mark", "args": {"code": code, "value": value}}, label)

    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        initial = call("bridge", {"command": "pause"}, "initial-fence")["bridge"]
        previous_epoch = initial.get("readyUnityFrame")
        if type(previous_epoch) is not int or initial.get("loading") or not initial.get("inputBlocked"):
            raise RuntimeError("Requires a completed, input-fenced native kitchen before reload")

        if args.inprocess_trace:
            call("bridge", {"command": "hot-call", "slot": "native-physics-trace",
                 "operation": "clear", "args": {}}, "trace-clear")
            mark(100, 0, "trace-before-load")

        if args.reuse_current:
            loaded_state = wait_paused("current-host", timeout=60)
            current_host = call("host", {"command": "inspect", "full": True}, "current-host-inspection")
            loaded = call("bridge", {"command": "status"}, "current-native-boundary")
            native = loaded["bridge"]
            epoch = native.get("readyUnityFrame")
            if current_host.get("frame") != loaded_state.get("frame") or \
                    (current_host.get("typedActions") or {}).get("active") or \
                    (current_host.get("rawInput") or {}).get("active") or \
                    any(chef.get("actions") for chef in current_host.get("actionGraph", {}).get("chefs", [])):
                raise RuntimeError("Current kitchen is not a paused neutral authoring boundary")
            if epoch != previous_epoch or native.get("loadComplete") is not True or native.get("loading") or \
                    native.get("paused") is not True or native.get("inputBlocked") is not True or \
                    native.get("session", {}).get("scene") != "s_sushi_1_1":
                raise RuntimeError("Current session is not a completed fenced Story 1-1 kitchen")
        else:
            call("bridge", {
                "command": "hot-call",
                "slot": "level-session",
                "operation": "load-main-1-1",
                "args": {"seed": 0},
            }, "fresh-load")
            deadline = time.monotonic() + args.load_timeout
            while True:
                response = call("bridge", {"command": "status"}, "load-poll", retain=False)
                native = response["bridge"]
                if native.get("lastError"):
                    raise RuntimeError("Native load failed: " + str(native["lastError"]))
                if native.get("loadComplete") is True and native.get("loading") is False:
                    break
                if time.monotonic() >= deadline:
                    raise TimeoutError("Native Story 1-1 load did not complete")
                time.sleep(.1)
            epoch = native.get("readyUnityFrame")
            if type(epoch) is not int or epoch <= previous_epoch or native.get("paused") is not True \
                    or native.get("inputBlocked") is not True or native.get("session", {}).get("scene") != "s_sushi_1_1":
                raise RuntimeError("Fresh load did not reach the expected paused Story 1-1 epoch")
            loaded_state = wait_paused("fresh-host", timeout=60)
            call("host", {"command": "actions-clear", "all": True}, "fresh-actions-clear")
            loaded = call("bridge", {"command": "status"}, "fresh-loaded-boundary")
        mark(110, loaded_state["frame"], "trace-after-load")

        if args.load_only:
            report.update(
                passed=True,
                completed=True,
                reusedCurrentKitchen=False,
                priorReadyUnityFrame=previous_epoch,
                readyUnityFrame=epoch,
                freshLoadedFrame=loaded_state["frame"],
                boundaries={"fresh": loaded},
            )
            return 0

        warm_state, warm_native = step(args.warmup, "warmup") if args.warmup else (loaded_state, loaded)
        checkpoint_frame = warm_state["frame"]
        if args.inprocess_trace:
            call("bridge", {"command": "hot-call", "slot": "native-physics-trace",
                 "operation": "clear", "args": {}}, "trace-clear-before-pair")
        mark(120, checkpoint_frame, "trace-after-warmup")
        checkpoint_name = "native-physx-trace-" + hashlib.sha256(str(args.out.resolve()).encode()).hexdigest()[:16] \
            + "-" + str(checkpoint_frame) + ".pb"
        call("host", {"command": "checkpoint", "path": checkpoint_name}, "checkpoint")

        mark(130, checkpoint_frame, "trace-before-original")
        original_state, original_native = step(args.frames, "original")
        mark(140, original_state["frame"], "trace-after-original")
        restore_before = original_native["bridge"].get("nativeCheckpoints", {}).get("restoreAttempts", 0)
        mark(150, checkpoint_frame, "trace-before-warp")
        call("bridge", {"command": "arm"}, "warp-arm")
        call("host", {"command": "warp", "frame": checkpoint_frame, "development": True}, "warp")
        restored_state = wait_paused("restored")
        restored_native = call("bridge", {"command": "pause"}, "restored-fence")
        restore = restored_native["bridge"].get("nativeCheckpoints", {}).get("lastRestore")
        if restored_state["frame"] != checkpoint_frame or not restore or restore.get("verified") is not True \
                or restore.get("attempt", -1) <= restore_before:
            raise RuntimeError("No new verified native restore")

        mark(160, restored_state["frame"], "trace-after-warp")

        mark(170, checkpoint_frame, "trace-before-replay")
        replay_state, replay_native = step(args.frames, "replay")
        mark(180, replay_state["frame"], "trace-after-replay")
        native_trace = None
        if args.inprocess_trace:
            native_trace = call("bridge", {"command": "hot-call", "slot": "native-physics-trace",
                "operation": "read", "args": {"afterSequence": 0, "max": 32768}}, "trace-read", retain=False)
        report.update(
            passed=True,
            completed=True,
            reusedCurrentKitchen=args.reuse_current,
            priorReadyUnityFrame=previous_epoch,
            readyUnityFrame=epoch,
            freshLoadedFrame=loaded_state["frame"],
            checkpointFrame=checkpoint_frame,
            endFrames={"original": original_state["frame"], "replay": replay_state["frame"]},
            nativeRestore=restore,
            nativeTrace=native_trace,
            boundaries={
                "fresh": loaded,
                "warmup": warm_native,
                "original": original_native,
                "restored": restored_native,
                "replay": replay_native,
            },
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
                          if key not in ("records", "boundaries", "nativeRestore", "nativeTrace")}, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
