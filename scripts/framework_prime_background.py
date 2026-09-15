"""Verify a focus-free Unity 2017 background session.

The game injects TAS controls at its logical-button layer, so gameplay does not
need foreground keyboard/controller input.  Unity 2017 can retain a stale
Application.isFocused startup value when a process starts minimized, so that
managed bit is diagnostic only.  This utility never requests foreground focus:
it minimizes the verified process-owned window and proves that Windows reports
another process as foreground while Unity continues running in background.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import time

from framework_rpc import Client


def focus_view(response):
    bridge = response.get("bridge") if isinstance(response, dict) else None
    window = bridge.get("nativeWindow") if isinstance(bridge, dict) else None
    if not isinstance(bridge, dict) or not isinstance(window, dict):
        raise RuntimeError("Bridge does not expose native window/focus diagnostics")
    view = {
        "unityFrame": bridge.get("unityFrame"),
        "applicationFocused": bridge.get("applicationFocused"),
        "runInBackground": bridge.get("runInBackground"),
        "backgroundTasInput": bridge.get("backgroundTasInput"),
        "minimized": window.get("minimized"),
        "foregroundOwned": window.get("foregroundOwned"),
        "lastRequest": window.get("lastRequest"),
    }
    if type(view["unityFrame"]) is not int or any(
            type(view[name]) is not bool for name in (
                "applicationFocused", "runInBackground", "backgroundTasInput", "minimized",
                "foregroundOwned")):
        raise RuntimeError("Bridge native window/focus diagnostics have an invalid shape")
    return view


def wait_for(bridge, predicate, deadline, label):
    last = None
    while time.monotonic() < deadline:
        last = focus_view(bridge.call({"command": "status"}))
        if predicate(last):
            return last
        time.sleep(.025)
    raise TimeoutError(f"Timed out waiting for {label}: {json.dumps(last)}")


def prime_background(bridge, timeout):
    before = focus_view(bridge.call({"command": "status"}))
    if not before["runInBackground"] or not before["backgroundTasInput"]:
        raise RuntimeError("Background priming requires runInBackground and logical TAS focus bypass")

    minimized_command = focus_view(bridge.call({"command": "window", "mode": "minimize"}))
    minimize_frame = minimized_command["unityFrame"]
    background = wait_for(
        bridge,
        lambda state: (not state["foregroundOwned"] and state["minimized"] and
                       state["lastRequest"] == "minimize" and
                       state["unityFrame"] > minimize_frame),
        time.monotonic() + timeout,
        "a minimized non-foreground Unity window",
    )
    return {
        "before": before,
        "activationAttempted": False,
        "minimizedCommand": minimized_command,
        "backgroundStable": background,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--timeout", type=float, default=10)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if not 1 <= args.timeout <= 30:
        parser.error("Use a timeout from 1 to 30 seconds")

    bridge = None
    report = {
        "passed": False,
        "classification": (
            "Verified minimized non-foreground game window with no activation request, native gameplay "
            "input, level load, simulation advance, or rewind"
        ),
    }
    started = time.monotonic()
    try:
        bridge = Client(args.bridge_port)
        report.update(prime_background(bridge, args.timeout))
        report["passed"] = True
    except Exception as error:
        report["error"] = str(error)
        if bridge is not None:
            try:
                report["failureMinimize"] = focus_view(
                    bridge.call({"command": "window", "mode": "minimize"}))
            except Exception as minimize_error:
                report["failureMinimizeError"] = str(minimize_error)
    finally:
        if bridge is not None:
            bridge.close()
        report["wallSeconds"] = time.monotonic() - started
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
