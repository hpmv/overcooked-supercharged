"""Activate quiescent authoring physics at a validated fresh Story boundary."""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from framework_rpc import Client, ControllerClient


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")

    bridge = host = None
    report = {
        "passed": False,
        "classification": (
            "Authoring-only physics pause-gate activation after a validated fresh Story 1-1 baseline; no search"
        ),
    }
    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        native = bridge.call({"command": "pause"})["bridge"]
        state = host.call({"command": "inspect", "full": True})
        graph = state.get("graphMappingValidation") or {}
        session = native.get("session") or {}
        if native.get("loading") or not native.get("loadComplete") or not native.get("paused") or not native.get("inputBlocked"):
            raise RuntimeError("Activation requires a stable paused native kitchen")
        if session.get("scene") != "s_sushi_1_1" or session.get("serverUsers") != 4 or session.get("clientUsers") != 4:
            raise RuntimeError("Activation requires the four-local Story 1-1 kitchen")
        if state.get("state") != "Paused" or state.get("requestPending") or not state.get("freshLevelLoadObserved"):
            raise RuntimeError("Activation requires the fresh paused Story controller boundary")
        if graph.get("ok") is not True or graph.get("frame") != state.get("frame") or graph.get("fixedMappingsValidated") is not True:
            raise RuntimeError("Fresh Story graph/settled-chef validation is not exact")
        activated = bridge.call({
            "command": "hot-call", "slot": "authoring-physics-pause-gate",
            "operation": "activate", "args": {},
        })["detail"]["result"]
        if (activated.get("active") is not True or activated.get("gated") is not True or
                activated.get("originalAutoSimulation") is not True or
                activated.get("liveAutoSimulation") is not False or activated.get("failure") is not None):
            raise RuntimeError("Physics pause gate did not establish its exact paused postcondition")
        report.update(
            passed=True,
            boundary={
                "frame": state.get("frame"), "readyUnityFrame": native.get("readyUnityFrame"),
                "graphFrame": graph.get("frame"), "fixedMappingsValidated": graph.get("fixedMappingsValidated"),
            },
            module=activated,
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
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps(report, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
