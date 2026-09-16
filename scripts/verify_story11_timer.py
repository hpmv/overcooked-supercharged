"""Prove that the native Story 1-1 timer advances before any delivery.

Runs neutral frames from a fresh, paused mapped controller. It never changes the
timer: the level-session module must have applied the requested policy on load.
The resulting run is a diagnostic prefix, not a score attempt.
"""
import argparse
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient
from framework_plate_search import require_empty_search_graph


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--frames", type=int, default=120)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Choose a new evidence path.")
    if not 60 <= args.frames <= 600:
        parser.error("Use 60..600 neutral frames.")
    records = []
    result = {"passed": False, "purpose": "timer-before-first-delivery", "records": records}
    bridge = host = None

    def call(target, request):
        response = (bridge if target == "bridge" else host).call(request)
        records.append({"target": target, "request": request, "response": response})
        return response

    def settled():
        deadline = time.monotonic() + 45
        while True:
            status = host.status()
            if status["state"] == "Paused" and not status["requestPending"]:
                return call("controller", {"command": "inspect", "full": True})
            if time.monotonic() > deadline:
                raise TimeoutError("Native neutral step did not settle within 45 seconds.")
            time.sleep(0.1)

    try:
        bridge, host = Client(17636), ControllerClient(17637)
        call("bridge", {"command": "pause"})
        before_host = settled()
        before = call("bridge", {"command": "food"})["bridge"]
        session, round_before = before["session"], before["nativeRound"]
        if (session["scene"] != "s_sushi_1_1" or session["variantPlayers"] != 4
                or session["serverUsers"] != 4 or session["clientUsers"] != 4):
            raise ValueError("Expected native Story 1-1 with four local chefs.")
        if before_host.get("discoveryOnly") or not before_host["freshLevelLoadObserved"]:
            raise ValueError("A mapped fresh native baseline is required.")
        if (round_before["timerSuppressed"] or round_before["timeLimit"] != 150
                or round_before["elapsed"] > 0.2 or round_before["ledger"]["deliveries"] != 0):
            raise ValueError("Expected a fresh unsuppressed native 150-second timer before first delivery.")
        require_empty_search_graph(before_host)
        if before_host.get("warpUsed"):
            raise ValueError("Timer proof requires a fresh run without authored actions or rewind.")
        call("bridge", {"command": "arm"})
        call("controller", {"command": "step", "frames": args.frames})
        after_host = settled()
        after = call("bridge", {"command": "food"})["bridge"]["nativeRound"]
        frame_delta = after_host["frame"] - before_host["frame"]
        elapsed_delta = after["elapsed"] - round_before["elapsed"]
        expected = frame_delta / 60.0
        checks = {
            "requestedFramesAdvanced": frame_delta == args.frames,
            "timerRemainsUnsuppressed": after["timerSuppressed"] is False,
            "nativeDurationUnchanged": after["timeLimit"] == 150 and after["configuredDuration"] == 150,
            "elapsedMatchesFrames": abs(elapsed_delta - expected) < 0.002,
            "noDeliveryOrScore": after["ledger"] == round_before["ledger"] and after["ledger"]["total"] == 0,
            "noRewind": after_host["warpUsed"] is False,
        }
        result.update(checks=checks, passed=all(checks.values()), frameDelta=frame_delta,
                      elapsedDeltaSeconds=elapsed_delta, expectedSeconds=expected,
                      initialRound=round_before, finalRound=after)
        call("bridge", {"command": "screenshot", "path": "artifacts/" + args.out.stem + ".png"})
    except Exception as error:
        result["error"] = str(error)
    finally:
        if bridge:
            try:
                call("bridge", {"command": "pause"})
            except Exception as error:
                result["pauseError"] = str(error)
                result["passed"] = False
            bridge.close()
        if host:
            host.close()
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(json.dumps({key: value for key, value in result.items() if key not in ("records", "initialRound", "finalRound")}, indent=2))
    if not result["passed"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
