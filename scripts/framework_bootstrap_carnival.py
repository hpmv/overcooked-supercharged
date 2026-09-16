"""Enter the first ready four-local kitchen through the core bridge only."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--timeout", type=float, default=165)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if not 30 <= args.timeout <= 180:
        parser.error("Use a 30..180 second timeout")

    bridge = host = None
    started = time.monotonic()
    report = {
        "passed": False,
        "classification": (
            "One normal core-bridge four-local Carnival bootstrap; no hot modules, "
            "rewind, route search, or gameplay qualification"
        ),
    }
    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        before = bridge.call({"command": "status"})["bridge"]
        if before.get("loading") or before.get("session", {}).get("scene") != "StartScreen":
            raise RuntimeError("Bootstrap requires a stable StartScreen process")
        if before.get("authoringModules", {}).get("active"):
            raise RuntimeError("Bootstrap requires no hot-loaded authoring modules")

        queued = bridge.call({"command": "load", "seed": 0})
        if queued.get("ok") is not True:
            raise RuntimeError("Native Carnival bootstrap did not queue")
        deadline = time.monotonic() + args.timeout
        status = None
        while True:
            status = bridge.call({"command": "status"})["bridge"]
            if status.get("lastError"):
                raise RuntimeError("Native bootstrap failed: " + status["lastError"])
            if status.get("loadComplete") is True and not status.get("loading"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Native Carnival bootstrap timed out")
            time.sleep(.1)

        deadline = time.monotonic() + 30
        controller = None
        while True:
            controller = host.call({"command": "inspect", "full": True})
            if controller.get("state") == "Paused" and not controller.get("requestPending"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Carnival controller did not settle after bootstrap")
            time.sleep(.05)

        session = status.get("session", {})
        if session.get("scene", "").lower() != "s_day_3_4" or \
                session.get("serverUsers") != 4 or session.get("clientUsers") != 4:
            raise RuntimeError("Expected four-local Carnival 3-4 ready boundary was not observed")
        report.update(
            passed=True,
            before={key: before.get(key) for key in
                    ("loading", "loadComplete", "readyUnityFrame", "sceneEpoch")},
            after={
                "readyUnityFrame": status.get("readyUnityFrame"),
                "sceneEpoch": status.get("sceneEpoch"),
                "controllerFrame": controller.get("frame"),
                "controllerState": controller.get("state"),
                "freshLevelLoadObserved": controller.get("freshLevelLoadObserved"),
            },
            session=session,
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
