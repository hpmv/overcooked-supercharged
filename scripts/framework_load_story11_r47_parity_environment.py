"""Load the pinned r47/r17d rewind-parity stack into a fresh Story 1-1 kitchen.

This recreates the gameplay-affecting checkpoint stack used by the final host-r23
probes.  Passive managed/native trace modules are intentionally excluded so this
is a parity fixture, not a search or tracing run.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import time

from framework_rpc import Client, ControllerClient


STACK = (
    ("level-session", "LevelSession-r2", None),
    ("scripted-round", "ScriptedRound-r2", {}),
    ("registry-observer", "RegistryObserver-r2", None),
    ("inspection", "Inspection-r1", None),
    ("world-sync-cache", "WorldSyncCache-r11b-exact-dynamic-owner-pose", {}),
    ("local-sync-bypass", "LocalSyncBypass-r1", {}),
    ("chef-pause-pose", "ChefPausePose-r4", {}),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r13-fixed-only-last", {}),
    ("rigidbody-motion-target", "RigidbodyMotionTarget-r1", {}),
    ("chef-movement-history-checkpoint", "ChefMovementHistoryCheckpoint-r3", {}),
    ("rigidbody-actor-rebuild", "RigidbodyActorRebuild-r13w-scene-owned-pool-reset", "actor"),
    ("resume-phase", "ResumePhase-r1ax-reset-after-r17c-mixed-child-guard", {}),
    ("chef-animator-checkpoint", "ChefAnimatorCheckpoint-r47-mixed-zero-weight-children", "animator"),
    ("animator-checkpoint-inspector", "AnimatorCheckpointInspector-r25b-update-zero-probe", None),
    ("body-restore", "BodyRestore-r24e-dynamic-empty-transient-mass-preflight", "body"),
)

NATIVE = {
    "actor": (
        Path("artifacts/native-rigidbody-rebuild-r6b-context-auto-build/Oc2NativeRigidbodyRebuild.r6b.dll"),
        "E8522017323090709415FCEFCBCEA9F1668275DF5E02715E9EF3DBBDF1CFD933",
    ),
    "animator": (
        Path("artifacts/native-animator-checkpoint-r17d-mixed-zero-weight-children-build/Oc2NativeAnimatorCheckpoint.r17d.dll"),
        "5E207E27B0E2929A5534334C1947DA4A77B82CE439915F72D969535936A55D85",
    ),
    "body": (
        Path("artifacts/native-rigidbody-rebuild-r10-shape-geometry-build1/Oc2NativeRigidbodyRebuild.r10.dll"),
        "C8C08A88DB1DC4A03D7A20FDB2F2F24E1C3C782B724F9E4F59229E06801AEBCF",
    ),
}

# Optional per-native-module activation arguments for derived diagnostic
# loaders. The ordinary parity loaders leave this empty.
NATIVE_OPTIONS = {}

CLASSIFICATION = "Pinned local r47/r17d rewind-parity setup; no search"


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, type=Path)
    parser.add_argument("--module-root", type=Path, default=Path("framework-run/modules"))
    parser.add_argument("--bridge-port", type=int, default=17636)
    parser.add_argument("--controller-port", type=int, default=17637)
    parser.add_argument("--load-timeout", type=float, default=165)
    parser.add_argument("--stack-only", action="store_true",
                        help="Load and activate the stack in the current kitchen without changing scenes.")
    args = parser.parse_args()
    if args.out.exists():
        parser.error("Output already exists")

    report = {
        "passed": False,
        "classification": CLASSIFICATION,
        "sourceSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
        "modules": [],
    }
    bridge = host = None
    started = time.monotonic()

    try:
        bridge = Client(args.bridge_port)
        host = ControllerClient(args.controller_port)
        host_status = host.call({"command": "status"})
        if host_status.get("resumePhaseMetadataVersion") != 1:
            raise RuntimeError(
                "Controller host does not advertise native resume-phase metadata protocol v1; "
                "refusing to load a stack whose resume gate requires it")
        report["controllerCapabilities"] = {
            "resumePhaseMetadataVersion": host_status["resumePhaseMetadataVersion"],
            "resumePhaseMetadataEmissions": host_status.get("resumePhaseMetadataEmissions", 0),
        }
        boundary = bridge.call({"command": "pause"})["bridge"]
        if (boundary.get("loading") or not boundary.get("paused") or
                not boundary.get("inputBlocked")):
            raise RuntimeError("Setup requires a stable paused, input-fenced bootstrap scene")
        bootstrap = boundary.get("session", {})
        if (boundary.get("loadComplete") is not True or
                bootstrap.get("stage") != "kitchen_ready" or
                bootstrap.get("scene") == "StartScreen" or
                bootstrap.get("serverUsers") != 4 or bootstrap.get("clientUsers") != 4):
            raise RuntimeError(
                "Setup requires one completed four-local bootstrap kitchen; "
                "the bridge forbids live-module installation at StartScreen")
        if boundary.get("authoringModules", {}).get("active"):
            raise RuntimeError("Fresh-process fixture requires no preloaded authoring modules")

        for slot, revision, activation in STACK:
            manifest_path = args.module_root / revision / "manifest.json"
            manifest = json.loads(manifest_path.read_text(encoding="utf-8-sig"))
            loaded = bridge.call({
                "command": "hot-load", "slot": slot, "path": manifest["dll"],
                "type": manifest["entryType"], "sha256": manifest["sha256"],
                "coreSha256": manifest["coreSha256"],
            })
            if loaded.get("ok") is not True:
                raise RuntimeError("Failed to load " + slot)

            activation_args = None
            native_sha = None
            if isinstance(activation, dict):
                activation_args = activation
            elif activation in NATIVE:
                native_path, native_sha = NATIVE[activation]
                actual = hashlib.sha256(native_path.read_bytes()).hexdigest().upper()
                if actual != native_sha:
                    raise RuntimeError("Native dependency hash mismatch: " + str(native_path))
                activation_args = {"nativePath": str(native_path.resolve()), "sha256": native_sha}
                activation_args.update(NATIVE_OPTIONS.get(activation, {}))
                if activation == "actor":
                    activation_args.update({
                        "automaticChefs": False,
                        "automaticGroundCollider": False,
                        "observeContactManagerContext": True,
                        "automaticContactPoolRestore": True,
                        "automaticTransformDispatchRestore": True,
                    })
            if activation_args is not None:
                activated = bridge.call({
                    "command": "hot-call", "slot": slot,
                    "operation": "activate", "args": activation_args,
                })
                if activated.get("ok") is not True:
                    raise RuntimeError("Failed to activate " + slot)
                if "mask" in activation_args:
                    activation_result = activated.get("detail", {}).get("result", {})
                    if activation_result.get("active") is not True or \
                            activation_result.get("installedMask") != activation_args["mask"] or \
                            activation_result.get("lastError") != 0:
                        raise RuntimeError("Native hook mask activation differs for " + slot)
            report["modules"].append({
                "slot": slot, "revision": revision, "sha256": manifest["sha256"],
                "activated": activation_args is not None, "nativeSha256": native_sha,
            })

        if args.stack_only:
            hot = bridge.call({"command": "hot-status"})["detail"]
            expected = {slot for slot, _, _ in STACK}
            observed = {entry.get("slot") for entry in hot.get("active", [])}
            if expected != observed:
                raise RuntimeError("Loaded stack membership differs: " +
                                   ", ".join(sorted(expected.symmetric_difference(observed))))
            controller = host.call({"command": "inspect", "full": True})
            if controller.get("state") != "Paused" or controller.get("requestPending"):
                raise RuntimeError("Controller did not remain settled after stack-only load")
            report.update(passed=True, stackOnly=True, readyUnityFrame=boundary.get("readyUnityFrame"),
                          controllerFrame=controller.get("frame"), session=bootstrap)
            return 0

        queued = bridge.call({
            "command": "hot-call", "slot": "level-session",
            "operation": "load-main-1-1", "args": {"seed": 0},
        })
        if queued.get("ok") is not True:
            raise RuntimeError("Story 1-1 load did not queue")

        deadline = time.monotonic() + args.load_timeout
        while True:
            status = bridge.call({"command": "status"})["bridge"]
            if status.get("lastError"):
                raise RuntimeError("Story 1-1 load failed: " + status["lastError"])
            if status.get("loadComplete") is True and not status.get("loading"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Story 1-1 load timed out")
            time.sleep(.1)

        level_session_status = bridge.call({
            "command": "hot-call", "slot": "level-session",
            "operation": "status", "args": {},
        })["detail"]["result"]
        if level_session_status.get("tutorialSkipSupported") is True:
            deadline = time.monotonic() + 15
            while level_session_status.get("pending") is True:
                if level_session_status.get("error"):
                    raise RuntimeError("Story 1-1 tutorial skip failed: " +
                                       level_session_status["error"])
                if time.monotonic() >= deadline:
                    raise TimeoutError("Story 1-1 tutorial skip did not settle")
                time.sleep(.05)
                level_session_status = bridge.call({
                    "command": "hot-call", "slot": "level-session",
                    "operation": "status", "args": {},
                })["detail"]["result"]
            tutorial_skip = level_session_status.get("tutorialSkip") or {}
            if (tutorial_skip.get("intercepted") is not True or
                    tutorial_skip.get("cleanupObserved") is not True or
                    tutorial_skip.get("prefixCalls") != 1 or
                    tutorial_skip.get("shutdownPrefixCalls") != 1 or
                    tutorial_skip.get("shutdownPostfixCalls") != 1 or
                    tutorial_skip.get("requestOpen") is not False or
                    tutorial_skip.get("lastError")):
                raise RuntimeError("Story 1-1 tutorial skip receipt is incomplete: " +
                                   json.dumps(tutorial_skip, sort_keys=True))

        # The tutorial policy completes inside the session coroutine after the
        # bridge's first InLevel observation. Refresh this receipt so the saved
        # session stage cannot retain an earlier in-flight value.
        status = bridge.call({"command": "status"})["bridge"]

        deadline = time.monotonic() + 30
        while True:
            controller = host.call({"command": "inspect", "full": True})
            if controller.get("state") == "Paused" and not controller.get("requestPending"):
                break
            if time.monotonic() >= deadline:
                raise TimeoutError("Controller did not settle after Story 1-1 load")
            time.sleep(.05)

        actor = bridge.call({
            "command": "hot-call", "slot": "rigidbody-actor-rebuild",
            "operation": "status", "args": {},
        })["detail"]["result"]
        pause_owner_guard = None
        if any(slot == "pause-owner-guard" for slot, _, _ in STACK):
            pause_owner_guard = bridge.call({
                "command": "hot-call", "slot": "pause-owner-guard",
                "operation": "status", "args": {},
            })["detail"]["result"]
            if (pause_owner_guard.get("active") is not True or
                    pause_owner_guard.get("failure") or
                    pause_owner_guard.get("stableMainOwnerCount") != 1 or
                    pause_owner_guard.get("otherMainOwnerCount") != 0 or
                    pause_owner_guard.get("nonMainOwnerCount") != 0):
                raise RuntimeError("Framework pause-owner invariant failed: " +
                                   json.dumps(pause_owner_guard, sort_keys=True))
        session = status.get("session", {})
        if (session.get("scene") != "s_sushi_1_1" or session.get("serverUsers") != 4 or
                session.get("clientUsers") != 4 or not controller.get("freshLevelLoadObserved")):
            raise RuntimeError("Fresh four-local Story 1-1 boundary was not observed")
        if (not actor.get("contactManagerContext") or actor.get("contextObservations", 0) < 1 or
                actor.get("contactPoolSnapshotCaptured") is not False):
            raise RuntimeError("Native context observer did not discover the fresh scene cleanly")

        report.update(
            passed=True,
            readyUnityFrame=status.get("readyUnityFrame"),
            controllerFrame=controller.get("frame"),
            session=session,
            levelSession=level_session_status,
            actorAfterStoryLoad=actor,
            pauseOwnerGuard=pause_owner_guard,
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
        print(json.dumps({k: v for k, v in report.items() if k != "actorAfterStoryLoad"}, indent=2))
    return 0 if report["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
