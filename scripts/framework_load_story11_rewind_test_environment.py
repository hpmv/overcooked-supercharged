"""Load the pinned rewind stack and a fresh Story 1-1 test kitchen.

The bridge connection stays open across the asynchronous level-session load.
This is setup for rewind validation, not a search or gameplay result.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


STACK = (
    ("level-session", "LevelSession-r2", False),
    ("body-restore", "BodyRestore-r15", "body"),
    ("resume-phase", "ResumePhase-r1d", True),
    ("world-sync-cache", "WorldSyncCache-r5", True),
    ("local-sync-bypass", "LocalSyncBypass-r1", True),
    ("inspection", "Inspection-r1", False),
    ("scripted-round", "ScriptedRound-r2", True),
    ("registry-observer", "RegistryObserver-r2", False),
    ("chef-pause-pose", "ChefPausePose-r4", True),
    ("chef-motion-lifecycle-observer", "ChefMotionLifecycleObserver-r1", True),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r12", True),
    ("chef-physics-shape-observer", "ChefPhysicsShapeObserver-r1", False),
    ("rigidbody-motion-target", "RigidbodyMotionTarget-r1", True),
    ("physics-capture-trace", "PhysicsCaptureTrace-r5", True),
    ("chef-animator-checkpoint", "ChefAnimatorCheckpoint-r14", "animator"),
)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--module-root", type=Path, default=Path("framework-run/modules"))
    parser.add_argument("--actor-manifest", type=Path,
                        default=Path("framework-run/modules/RigidbodyActorRebuild-r13a/manifest.json"))
    parser.add_argument("--actor-native", type=Path,
                        default=Path("artifacts/native-rigidbody-rebuild-r6b-context-auto-build/Oc2NativeRigidbodyRebuild.r6b.dll"))
    parser.add_argument("--actor-native-sha256",
                        default="E8522017323090709415FCEFCBCEA9F1668275DF5E02715E9EF3DBBDF1CFD933")
    parser.add_argument("--body-native", type=Path,
                        default=Path("artifacts/native-rigidbody-rebuild-r9-shape-pose-build1/Oc2NativeRigidbodyRebuild.r9.dll"))
    parser.add_argument("--body-native-sha256",
                        default="28DC85C0CE62B303636828947D58A3686298B493F368C662F61E91479C328088")
    parser.add_argument("--animator-native", type=Path,
                        default=Path("artifacts/native-animator-checkpoint-r3-build1/Oc2NativeAnimatorCheckpoint.r3.dll"))
    parser.add_argument("--animator-native-sha256",
                        default="7D5C5B54B547F2F7943253EA769F7FAF8101122B7D1FAE00B0405CAC70400675")
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--load-timeout", type=float, default=165)
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")
    if not 30 <= args.load_timeout <= 180:
        parser.error("Use a 30..180 second load timeout")

    report = {
        "passed": False,
        "classification": "Pinned local rewind-test setup; no search or gameplay qualification",
        "sourceSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "modules": [],
    }
    bridge = host = None
    started = time.monotonic()

    def call(request):
        return bridge.call(request)

    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        boundary = call({"command": "pause"})["bridge"]
        if boundary.get("loading") or not boundary.get("loadComplete") or \
                not boundary.get("paused") or not boundary.get("inputBlocked"):
            raise RuntimeError("Setup requires a completed paused bootstrap kitchen")
        existing = {module["slot"]: module for module in
                    boundary.get("authoringModules", {}).get("active", [])}

        for slot, revision, activate in STACK:
            manifest_path = args.module_root / revision / "manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            current = existing.get(slot)
            already_loaded = current is not None
            if already_loaded:
                if current.get("sha256", "").upper() != manifest["sha256"].upper():
                    raise RuntimeError("Slot already contains a different module: " + slot)
            else:
                loaded = call({"command": "hot-load", "slot": slot, "path": manifest["dll"],
                               "type": manifest["entryType"], "sha256": manifest["sha256"],
                               "coreSha256": manifest["coreSha256"]})
                if loaded.get("ok") is not True:
                    raise RuntimeError("Failed to load " + slot)
            already_active = False
            if already_loaded and activate:
                status_value = call({"command": "hot-call", "slot": slot,
                                     "operation": "status", "args": {}})["detail"]["result"]
                already_active = (status_value.get("active") is True or
                                  status_value.get("hasRegistrationLease") is True)
            if activate and not already_active:
                activation_args = {}
                if activate == "body":
                    activation_args = {"nativePath": str(args.body_native.resolve()),
                                       "sha256": args.body_native_sha256.upper()}
                elif activate == "animator":
                    activation_args = {"nativePath": str(args.animator_native.resolve()),
                                       "sha256": args.animator_native_sha256.upper()}
                activated = call({"command": "hot-call", "slot": slot,
                                  "operation": "activate", "args": activation_args})
                if activated.get("ok") is not True:
                    raise RuntimeError("Failed to activate " + slot)
            report["modules"].append({"slot": slot, "revision": revision,
                                      "sha256": manifest["sha256"], "activated": bool(activate),
                                      "alreadyLoaded": already_loaded,
                                      "nativeSha256": (args.body_native_sha256.upper() if activate == "body" else
                                                       args.animator_native_sha256.upper() if activate == "animator" else None)})

        actor = json.loads(args.actor_manifest.read_text(encoding="utf-8-sig"))
        actor_slot = "rigidbody-actor-rebuild"
        current_actor = existing.get(actor_slot)
        if current_actor is not None:
            if current_actor.get("sha256", "").upper() != actor["sha256"].upper():
                raise RuntimeError("Slot already contains a different module: " + actor_slot)
            actor_before = call({"command": "hot-call", "slot": actor_slot,
                                 "operation": "status", "args": {}})["detail"]["result"]
        else:
            call({"command": "hot-load", "slot": actor_slot, "path": actor["dll"],
                  "type": actor["entryType"], "sha256": actor["sha256"],
                  "coreSha256": actor["coreSha256"]})
            actor_before = None
        actor_args = {
            "nativePath": str(args.actor_native.resolve()),
            "sha256": args.actor_native_sha256.upper(),
            "automaticChefs": False,
            "automaticGroundCollider": False,
            "observeContactManagerContext": True,
            "automaticContactPoolRestore": True,
            "automaticTransformDispatchRestore": True,
        }
        if actor_before is None or actor_before.get("active") is not True:
            activated = call({"command": "hot-call", "slot": actor_slot,
                              "operation": "activate", "args": actor_args})
            actor_before = activated["detail"]["result"]
        if actor_before.get("active") is not True or actor_before.get("contextObserverInstalled") is not True or \
                (current_actor is None and actor_before.get("contactManagerContext") is not None):
            raise RuntimeError("Actor observer did not start without a supplied context")
        report["modules"].append({"slot": "rigidbody-actor-rebuild",
                                  "revision": actor["revision"], "sha256": actor["sha256"],
                                  "nativeSha256": args.actor_native_sha256.upper(), "activated": True})

        queued = call({"command": "hot-call", "slot": "level-session",
                       "operation": "load-main-1-1", "args": {"seed": 0}})
        if queued.get("ok") is not True:
            raise RuntimeError("Story 1-1 load did not queue")
        deadline = time.monotonic() + args.load_timeout
        while True:
            status = call({"command": "status"})["bridge"]
            if status.get("lastError"):
                raise RuntimeError("Story 1-1 load failed: " + status["lastError"])
            if status.get("loadComplete") is True and not status.get("loading"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Story 1-1 load timed out")
            time.sleep(.1)

        deadline = time.monotonic() + 30
        while True:
            controller = host.call({"command": "inspect", "full": True})
            if controller.get("state") == "Paused" and not controller.get("requestPending"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Controller did not settle after Story 1-1 load")
            time.sleep(.05)
        actor_after = call({"command": "hot-call", "slot": "rigidbody-actor-rebuild",
                            "operation": "status", "args": {}})["detail"]["result"]
        session = status.get("session", {})
        if session.get("scene") != "s_sushi_1_1" or session.get("serverUsers") != 4 or \
                session.get("clientUsers") != 4 or not controller.get("freshLevelLoadObserved"):
            raise RuntimeError("Fresh four-local Story 1-1 boundary was not observed")
        if not actor_after.get("contactManagerContext") or actor_after.get("contextObservations", 0) < 1 or \
                actor_after.get("contactPoolSnapshotCaptured") is not False:
            raise RuntimeError("Native observer did not discover the fresh scene context cleanly")
        report.update(passed=True, readyUnityFrame=status.get("readyUnityFrame"),
                      controllerFrame=controller.get("frame"), session=session,
                      actorBeforeStoryLoad=actor_before, actorAfterStoryLoad=actor_after)
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
        print(json.dumps({key: value for key, value in report.items()
                          if key not in ("actorBeforeStoryLoad", "actorAfterStoryLoad")}, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
