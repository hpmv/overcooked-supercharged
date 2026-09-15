"""Prove one logical pickup edge is consumed while Unity stays minimized."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import time

from framework_prime_background import focus_view
from framework_rpc import Client, ControllerClient


def wait_paused(host, timeout):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        last = host.call({"command": "status"})
        if last.get("errors") or last.get("state") == "Error":
            raise RuntimeError(json.dumps(last))
        if last.get("state") == "Paused" and not last.get("requestPending"):
            return host.call({"command": "inspect", "full": True})
        time.sleep(.025)
    raise TimeoutError("Controller did not settle: " + json.dumps(last))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--chef", required=True, type=int)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--timeout", type=float, default=30)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")

    bridge = host = None
    report = {
        "passed": False,
        "classification": (
            "Forward-only logical pickup-edge background smoke; no native input, "
            "checkpoint, rewind, route search, or level reload"
        ),
    }
    started = time.monotonic()
    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        initial = wait_paused(host, args.timeout)
        before_response = bridge.call({"command": "status"})
        before = focus_view(before_response)
        if before["applicationFocused"] or not before["minimized"]:
            raise RuntimeError("Background smoke requires a minimized, unfocused Unity process")
        logical = before_response["bridge"].get("tasLogicalInput", {})
        prior_polls = logical.get("pickupPolls")
        prior_applications = logical.get("inputApplications")
        if not isinstance(prior_polls, list) or not isinstance(prior_applications, list):
            raise RuntimeError("Bridge does not expose logical input receipts")
        last_sequence = max((row.get("sequence", 0) for row in prior_polls
                             if isinstance(row, dict)), default=0)
        last_application_sequence = max((row.get("sequence", 0) for row in prior_applications
                                         if isinstance(row, dict)), default=0)
        chef_ids = sorted(row.get("entity") for row in before_response["bridge"].get("chefs", [])
                          if isinstance(row, dict) and row.get("local") is True)
        if len(chef_ids) != 4 or args.chef not in chef_ids or any(type(value) is not int for value in chef_ids):
            raise RuntimeError("Expected the requested chef among exactly four local chefs")

        pads = {str(identity): {
            "x": 0, "y": 0, "pickup": identity == args.chef,
            "interact": False, "dash": False,
        } for identity in chef_ids}
        bridge.call({"command": "arm"})
        host.call({"command": "raw-input", "segments": [{"frames": 1, "chefs": pads}]})
        final = wait_paused(host, args.timeout)
        after_response = bridge.call({"command": "status"})
        after = focus_view(after_response)
        if after["applicationFocused"] or not after["minimized"]:
            raise RuntimeError("Unity left the minimized background state during logical input")
        polls = [row for row in after_response["bridge"]["tasLogicalInput"]["pickupPolls"]
                 if isinstance(row, dict) and row.get("sequence", 0) > last_sequence]
        down_polls = [row for row in polls if row.get("deviceEntity") == args.chef and
                      row.get("inputLevel") is True]
        applications = [row for row in after_response["bridge"]["tasLogicalInput"]["inputApplications"]
                        if isinstance(row, dict) and row.get("sequence", 0) > last_application_sequence]
        phase_resumes = [row for row in applications if row.get("reason") == "controller-reply" and
                         row.get("controllerFrame") == initial.get("frame") and row.get("requestResume") is True and
                         row.get("preservedMissingInput") is True]
        checks = {
            "controllerAdvanced": final.get("frame") == initial.get("frame") + 3,
            "remainedUnfocused": after["applicationFocused"] is False,
            "remainedMinimized": after["minimized"] is True,
            "exactlyOneDownConsumerPoll": len(down_polls) == 1,
            "nativeLogicalEdgeAccepted": len(down_polls) == 1 and down_polls[0].get("result") is True,
            "consumerSawUnfocusedUnity": len(down_polls) == 1 and
                                          down_polls[0].get("applicationFocused") is False,
            "verifiedGateChain": len(down_polls) == 1 and all(
                layer.get("verified") is True for layer in down_polls[0].get("before", [])),
            "phaseOnlyResumePreservedPads": len(phase_resumes) == 1,
        }
        report.update(before=before, after=after, startFrame=initial.get("frame"),
                      endFrame=final.get("frame"), inputApplications=applications,
                      pickupPolls=polls, checks=checks,
                      passed=all(checks.values()))
        if not report["passed"]:
            report["error"] = "Background logical pickup edge did not satisfy every proof check"
    except Exception as error:
        report["error"] = str(error)
    finally:
        if bridge is not None:
            try:
                bridge.call({"command": "pause"})
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
        report["wallSeconds"] = time.monotonic() - started
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
