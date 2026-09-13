"""Reload the current hot-module stack into a fresh four-local Story 1-1 scene."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--load-timeout", type=float, default=165)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if not 30 <= args.load_timeout <= 180:
        parser.error("Use a 30..180 second load timeout")

    bridge = host = None
    started = time.monotonic()
    report = {"passed": False, "classification": "fresh Story 1-1 reload; no search or gameplay qualification"}
    try:
        bridge = Client(17636)
        host = ControllerClient(17637)
        before = bridge.call({"command": "pause"})["bridge"]
        if before.get("loading") or not before.get("loadComplete") or not before.get("paused") or not before.get("inputBlocked"):
            raise RuntimeError("Reload requires a completed paused input-fenced scene")
        queued = bridge.call({"command": "hot-call", "slot": "level-session",
                              "operation": "load-main-1-1", "args": {"seed": 0}})
        if queued.get("ok") is not True:
            raise RuntimeError("Story 1-1 load did not queue")
        deadline = time.monotonic() + args.load_timeout
        bridge_status = None
        while True:
            bridge_status = bridge.call({"command": "status"})["bridge"]
            if bridge_status.get("lastError"):
                raise RuntimeError("Story 1-1 load failed: " + bridge_status["lastError"])
            if bridge_status.get("loadComplete") is True and not bridge_status.get("loading"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Story 1-1 load timed out")
            time.sleep(.1)
        deadline = time.monotonic() + 30
        controller = None
        while True:
            controller = host.call({"command": "inspect", "full": True})
            if controller.get("state") == "Paused" and not controller.get("requestPending") and not controller.get("invalidStateReason"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Controller did not settle after Story 1-1 load")
            time.sleep(.05)
        session = bridge_status.get("session", {})
        if session.get("scene") != "s_sushi_1_1" or session.get("serverUsers") != 4 or session.get("clientUsers") != 4:
            raise RuntimeError("Fresh four-local Story 1-1 boundary was not observed")
        report.update(passed=True, before={key: before.get(key) for key in
                      ("sceneEpoch", "readyUnityFrame", "paused", "inputBlocked")},
                      after={"frame": controller.get("frame"), "state": controller.get("state"),
                             "freshLevelLoadObserved": controller.get("freshLevelLoadObserved"),
                             "sceneEpoch": bridge_status.get("sceneEpoch"),
                             "readyUnityFrame": bridge_status.get("readyUnityFrame")}, session=session)
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
