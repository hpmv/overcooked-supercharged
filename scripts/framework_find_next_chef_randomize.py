"""Advance a paused Story 1-1 scene until the next recorded chef idle-randomizer callback.

This is a bounded diagnostic helper. It advances only neutral controller steps,
does not checkpoint or rewind, and leaves the game paused/input-fenced.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


def animator_status(bridge: Client) -> dict:
    receipt = bridge.call({
        "command": "hot-call",
        "slot": "chef-animator-checkpoint",
        "operation": "status",
        "args": {},
    })
    return receipt["detail"]["result"]


def settled(host: ControllerClient, expected_frame: int) -> dict:
    deadline = time.monotonic() + 20
    while True:
        state = host.status()
        if state.get("state") == "Error" or state.get("errors"):
            raise RuntimeError(json.dumps(state))
        if state.get("state") == "Paused" and not state.get("requestPending"):
            if state.get("frame") != expected_frame:
                raise RuntimeError(f"Expected frame {expected_frame}, got {state.get('frame')}")
            return state
        if time.monotonic() >= deadline:
            raise TimeoutError(json.dumps(state))
        time.sleep(0.02)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--maximum-frames", type=int, default=360)
    parser.add_argument("--step", type=int, default=2)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if args.step < 2 or args.maximum_frames < args.step or args.maximum_frames > 3600:
        parser.error("Use step >= 2 and step <= maximum-frames <= 3600")

    bridge = host = None
    report = {
        "passed": False,
        "classification": "bounded neutral diagnostic; no rewind or route search",
    }
    started = time.monotonic()
    try:
        bridge = Client(17636)
        host = ControllerClient(17637)
        initial = host.status()
        if initial.get("state") != "Paused" or initial.get("requestPending") or initial.get("errors"):
            raise RuntimeError("Diagnostic requires a settled paused controller")
        before = animator_status(bridge)
        if before.get("chefRandomizeMode") != "Record" or before.get("failure") is not None:
            raise RuntimeError("Chef randomizer is not in clean record mode")
        observations = before.get("randomizeAnimParamObservations", [])
        old_count = len(observations)
        old_chef_count = sum(row.get("chef") is True for row in observations)
        frame = initial["frame"]
        found = []
        steps = 0
        while steps < args.maximum_frames and not found:
            count = min(args.step, args.maximum_frames - steps)
            if count < 2:
                break
            bridge.call({"command": "arm"})
            host.call({"command": "step", "frames": count})
            frame += count
            settled(host, frame)
            bridge.call({"command": "pause"})
            current = animator_status(bridge)
            if current.get("chefRandomizeMode") != "Record" or current.get("failure") is not None:
                raise RuntimeError("Chef randomizer left clean record mode")
            rows = current.get("randomizeAnimParamObservations", [])
            found = [row for row in rows[old_count:] if row.get("chef") is True]
            steps += count
        if not found:
            raise RuntimeError("No chef randomizer callback occurred inside the bounded neutral window")
        report.update(
            passed=True,
            startFrame=initial["frame"],
            endFrame=frame,
            advancedFrames=steps,
            initialObservationCount=old_count,
            initialChefObservationCount=old_chef_count,
            observations=found,
            targetFrame=max(row["lastCapturedOutputFrame"] for row in found) + 10,
            scope=("Stops at the first two-frame pause boundary after at least one new chef callback. "
                   "targetFrame is callback history frame + 10 and must be inspected before use."),
        )
    except Exception as error:
        report["error"] = str(error)
    finally:
        if bridge is not None:
            try:
                bridge.call({"command": "pause"})
            except Exception as error:
                report["finalPauseError"] = str(error)
                report["passed"] = False
            bridge.close()
        if host is not None:
            host.close()
        report["wallSeconds"] = time.monotonic() - started
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
