"""Load the dev5 Story 1-1 rewind stack with native tracing first.

The native trace trampolines revision-lock the original Unity entry bytes, so
they must be installed before any later authoring module can Harmony-patch a
traced method. Only the non-conflicting Unity lifecycle and PhysicsManager
simulation hooks are enabled: BodyRestore independently revision-checks and
reads the lower PhysX pose/kinematic state. This is a diagnostic parity fixture;
the trace is read-only and search remains disabled.
"""
from pathlib import Path

import framework_load_story11_r47_parity_environment as loader


loader.STACK = (
    ("native-physics-trace", "NativePhysicsTrace-r8ag-mass-diagonalization-core-bg1", "trace"),
    ("level-session", "LevelSession-r2-core-bg1", None),
    ("scripted-round", "ScriptedRound-r2-core-bg1", {}),
    ("registry-observer", "RegistryObserver-r2-core-bg1", None),
    ("inspection", "Inspection-r1-core-bg1", None),
    ("world-sync-cache", "WorldSyncCache-r13n-logical-rest-clock-core-bg1", {}),
    ("local-sync-bypass", "LocalSyncBypass-r1-core-bg1", {}),
    ("chef-pause-pose", "ChefPausePose-r4-core-bg1", {}),
    # Activate only after fresh-scene startup settling, via
    # framework_activate_authoring_physics_pause.py.
    ("authoring-physics-pause-gate", "AuthoringPhysicsPauseGate-r2-core-bg1", None),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r13-core-bg1", {}),
    ("rigidbody-motion-target", "RigidbodyMotionTarget-r1-core-bg1", {}),
    ("chef-movement-history-checkpoint", "ChefMovementHistoryCheckpoint-r3-core-bg1", {}),
    ("rigidbody-actor-rebuild", "RigidbodyActorRebuild-r13z-manifold-pool-history-core-bg1", "actor"),
    ("resume-phase", "ResumePhase-r1bc-core-bg1", {}),
    ("chef-animator-checkpoint", "ChefAnimatorCheckpoint-r53b-core-bg1", "animator"),
    ("animator-checkpoint-inspector", "AnimatorCheckpointInspector-r25b-update-zero-probe-core-bg1", None),
    # Keep the startup-proven r44b module through scene construction. Diagnostic
    # revisions are hot-loaded only after Story 1-1 has settled.
    ("body-restore", "BodyRestore-r44b-corrected-lifecycle-post-maintenance-core-bg1", "body"),
    ("delivery-fade-checkpoint", "DeliveryFadeCheckpoint-r10k-persistent-backward-history-core-bg1", {}),
    ("round-end-checkpoint", "RoundEndCheckpoint-dev5-core-bg1", None),
)

loader.NATIVE = {
    "trace": (
        Path("artifacts/native-physics-trace-r21-mass-diagonalize-build1/Oc2NativePhysicsTrace.r21.dll"),
        "07E7E0E0ED770802E85A70999FBE3BBEFBD6A0DF5CDB68B1BB05E237A0FA0030",
    ),
    "actor": (
        Path("artifacts/native-rigidbody-rebuild-v7-ninja/Oc2NativeRigidbodyRebuild.dll"),
        "2E6284C6380B0085853C2240D09044EE266FC7D8415442E731B529C38910A35D",
    ),
    "animator": (
        Path("artifacts/native-animator-checkpoint-r17d-mixed-zero-weight-children-build/Oc2NativeAnimatorCheckpoint.r17d.dll"),
        "5E207E27B0E2929A5534334C1947DA4A77B82CE439915F72D969535936A55D85",
    ),
    "body": (
        Path("framework/artifacts/native-rigidbody-rebuild-r16-corrected-lifecycle-receipt-cmake1/Oc2NativeRigidbodyRebuild.dll"),
        "DB41115017649D16E14A91093A3D00EADAE4B9897B11940ADE4209B8BE539664",
    ),
}

# Mask 1: Unity Rigidbody lifecycle/MovePosition/set_isKinematic.
# Mask 8: PhysicsManager simulation boundary. Avoid mask 4 because its PhysX
# pose hooks intentionally change entry bytes revision-checked by BodyRestore.
loader.NATIVE_OPTIONS = {"trace": {"mask": 9}}

loader.CLASSIFICATION = (
    "Pinned local Story 1-1 dev5 rewind-parity setup with read-only native physics trace; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
