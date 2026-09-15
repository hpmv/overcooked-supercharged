"""Replace the actor helper's context observer with the read-only native physics trace.

This is a diagnostic-only handoff at a fresh, paused Story 1-1 boundary.  The
actor helper first observes the live PxsContext through its reversible
createContactManager trampoline.  Before any checkpoint is captured, this
script removes that observer, installs the native trace on the now-pristine
entry point, and reactivates actor restoration with the exact observed context
supplied explicitly.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


ACTOR_NATIVE = Path("artifacts/native-rigidbody-rebuild-v7-ninja/Oc2NativeRigidbodyRebuild.dll")
ACTOR_SHA256 = "2E6284C6380B0085853C2240D09044EE266FC7D8415442E731B529C38910A35D"
TRACE_NATIVE = Path("artifacts/native-physics-trace-r22-dirty-order-build1/Oc2NativePhysicsTrace.r22.dll")
TRACE_SHA256 = "96A57839A3A2F5D71B2F7E559184E2799221FF1A2936B959736DCEE5805BA502"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--mask", type=int, default=25)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if args.mask != 25:
        parser.error("This diagnostic is pinned to mask 25 (lifecycle + physics phases + narrowphase)")

    bridge = host = None
    records = []
    report = {
        "passed": False,
        "classification": (
            "Fresh paused Story 1-1 diagnostic handoff from the reversible "
            "PxsContext observer to read-only native mask-25 tracing; no search"
        ),
        "records": records,
    }
    started = time.monotonic()

    def call(request):
        began = time.monotonic()
        response = bridge.call(request)
        records.append({"request": request, "wallSeconds": time.monotonic() - began, "response": response})
        return response

    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        native = call({"command": "pause"})["bridge"]
        controller = host.call({"command": "inspect", "full": True})
        session = native.get("session") or {}
        if (native.get("loading") or native.get("loadComplete") is not True or
                native.get("paused") is not True or native.get("inputBlocked") is not True):
            raise RuntimeError("Handoff requires a stable paused native kitchen")
        if (session.get("scene") != "s_sushi_1_1" or session.get("serverUsers") != 4 or
                session.get("clientUsers") != 4):
            raise RuntimeError("Handoff requires the four-local Story 1-1 kitchen")
        if (controller.get("state") != "Paused" or controller.get("requestPending") or
                controller.get("frame") != 1 or controller.get("freshLevelLoadObserved") is not True):
            raise RuntimeError("Handoff is restricted to the fresh controller frame-1 boundary")

        actor_before = call({
            "command": "hot-call", "slot": "rigidbody-actor-rebuild",
            "operation": "status", "args": {},
        })["detail"]["result"]
        forbidden = (
            actor_before.get("contactPoolSnapshotCount", 0) != 0 or
            actor_before.get("contactPoolCaptures", 0) != 0 or
            actor_before.get("contactPoolRestores", 0) != 0 or
            actor_before.get("manifoldPoolCaptures", 0) != 0 or
            actor_before.get("manifoldPoolRestores", 0) != 0 or
            actor_before.get("transformDispatchCaptures", 0) != 0 or
            actor_before.get("transformDispatchRestores", 0) != 0 or
            actor_before.get("pendingContactPoolAction") != "none" or
            actor_before.get("warpInProgress") is True
        )
        context = actor_before.get("contactManagerContext")
        if (actor_before.get("active") is not True or
                actor_before.get("contextObserverInstalled") is not True or
                actor_before.get("observeContactManagerContext") is not True or
                actor_before.get("automaticContactPoolRestore") is not True or
                actor_before.get("automaticTransformDispatchRestore") is not True or
                actor_before.get("failure") is not None or not isinstance(context, str) or
                not context.startswith("0x") or int(context[2:], 16) == 0 or forbidden):
            raise RuntimeError("Actor helper is not at a pristine observed-context boundary")

        actor_off = call({
            "command": "hot-call", "slot": "rigidbody-actor-rebuild",
            "operation": "deactivate", "args": {},
        })["detail"]["result"]
        if actor_off.get("active") is not False or actor_off.get("contextObserverInstalled") is not False:
            raise RuntimeError("Actor context observer did not uninstall cleanly")

        trace = call({
            "command": "hot-call", "slot": "native-physics-trace",
            "operation": "activate",
            "args": {
                "nativePath": str(TRACE_NATIVE.resolve()),
                "sha256": TRACE_SHA256,
                "mask": args.mask,
            },
        })["detail"]["result"]
        if (trace.get("active") is not True or trace.get("installedMask") != args.mask or
                trace.get("installedHookCount") != 12 or trace.get("lastError") != 0):
            raise RuntimeError("Native trace did not establish the pinned mask-25 postcondition")

        actor_after = call({
            "command": "hot-call", "slot": "rigidbody-actor-rebuild",
            "operation": "activate",
            "args": {
                "nativePath": str(ACTOR_NATIVE.resolve()),
                "sha256": ACTOR_SHA256,
                "automaticChefs": False,
                "automaticGroundCollider": False,
                "observeContactManagerContext": False,
                "contactManagerContext": context,
                "automaticContactPoolRestore": True,
                "automaticTransformDispatchRestore": True,
            },
        })["detail"]["result"]
        if (actor_after.get("active") is not True or
                actor_after.get("contactManagerContext") != context or
                actor_after.get("observeContactManagerContext") is not False or
                actor_after.get("contextObserverInstalled") is not False or
                actor_after.get("automaticContactPoolRestore") is not True or
                actor_after.get("automaticTransformDispatchRestore") is not True or
                actor_after.get("failure") is not None):
            raise RuntimeError("Actor restoration did not reactivate against the explicit context")

        report.update(
            passed=True,
            boundary={
                "controllerFrame": controller.get("frame"),
                "readyUnityFrame": native.get("readyUnityFrame"),
                "scene": session.get("scene"),
            },
            context=context,
            actorBefore=actor_before,
            actorAfter=actor_after,
            nativeTrace=trace,
        )
    except Exception as error:
        report["error"] = str(error)
        report["processReusable"] = False
    finally:
        if bridge is not None:
            try:
                call({"command": "pause"})
            except Exception as error:
                report["finalPauseError"] = str(error)
                report["passed"] = False
                report["processReusable"] = False
            bridge.close()
        if host is not None:
            host.close()
        report["wallSeconds"] = time.monotonic() - started
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
        console = {key: value for key, value in report.items() if key not in ("records", "actorBefore")}
        print(json.dumps(console, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
