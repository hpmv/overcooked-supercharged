"""Load the frozen Story 1-1 rewind/observation module stack into a paused kitchen."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import time

from framework_rpc import Client


STACK = (
    ("body-restore", "BodyRestore-r5", True),
    ("resume-phase", "ResumePhase-r1d", True),
    ("world-sync-cache", "WorldSyncCache-r3a", True),
    ("local-sync-bypass", "LocalSyncBypass-r1", True),
    ("inspection", "Inspection-r1", False),
    ("scripted-round", "ScriptedRound-r2", True),
    ("registry-observer", "RegistryObserver-r2", False),
    ("chef-pause-pose", "ChefPausePose-r4", True),
    ("chef-motion-lifecycle-observer", "ChefMotionLifecycleObserver-r1", True),
    ("chef-contact-refresh", "ChefContactRefresh-r3", True),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r4", True),
    ("chef-physics-shape-observer", "ChefPhysicsShapeObserver-r1", False),
)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--module-root", type=Path, default=Path("framework-run/modules"))
    parser.add_argument("--port", type=int, default=17636)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")

    began = time.monotonic()
    bridge = None
    records = []
    report = {
        "passed": False,
        "classification": "paused hot-load of previously validated Story 1-1 rewind/observation stack",
        "sourceSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "stack": [{"slot": slot, "revision": revision, "activate": activate}
                  for slot, revision, activate in STACK],
    }

    def call(request, label):
        started = time.monotonic()
        response = bridge.call(request)
        records.append({"label": label, "request": request, "response": response,
                        "wallSeconds": time.monotonic() - started})
        return response

    try:
        bridge = Client(args.port)
        boundary = call({"command": "pause"}, "initial-pause")["bridge"]
        if not boundary.get("loadComplete") or not boundary.get("paused") or \
                not boundary.get("inputBlocked") or boundary.get("loading"):
            raise RuntimeError("Module stack requires a completed, paused, input-fenced kitchen")
        for slot, revision, activate in STACK:
            manifest_path = args.module_root / revision / "manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            call({"command": "hot-load", "slot": slot, "path": manifest["dll"],
                  "type": manifest["entryType"], "sha256": manifest["sha256"],
                  "coreSha256": manifest["coreSha256"]}, "load-" + slot)
            if activate:
                call({"command": "hot-call", "slot": slot,
                      "operation": "activate", "args": {}}, "activate-" + slot)
        status = call({"command": "hot-status"}, "final-status")
        active = status.get("detail", {}).get("active", [])
        expected = {slot for slot, _, _ in STACK}
        observed = {entry.get("slot") for entry in active}
        missing = sorted(expected - observed)
        if missing:
            raise RuntimeError("Loaded stack is missing slots: " + ", ".join(missing))
        report.update(passed=True, activeSlots=sorted(observed), finalStatus=status)
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
        report["records"] = records
        report["wallSeconds"] = time.monotonic() - began
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2), encoding="utf-8")
        print(json.dumps({key: value for key, value in report.items()
                          if key not in ("records", "finalStatus")}, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
