"""Load the dev5 Story 1-1 rewind stack with quiescent authoring physics."""
from pathlib import Path

import framework_load_story11_r47_parity_environment as loader


loader.STACK = (
    ("level-session", "LevelSession-r2-core-dev5", None),
    ("scripted-round", "ScriptedRound-r2-core-dev5", {}),
    ("registry-observer", "RegistryObserver-r2-core-dev5", None),
    ("inspection", "Inspection-r1-core-dev5", None),
    ("world-sync-cache", "WorldSyncCache-r13n-logical-rest-clock-core-dev5", {}),
    ("local-sync-bypass", "LocalSyncBypass-r1-core-dev5", {}),
    ("chef-pause-pose", "ChefPausePose-r4-core-dev5", {}),
    # Load now, but activate only after the fresh Story scene has completed its
    # ordinary startup physics settling and passed the controller graph audit.
    ("authoring-physics-pause-gate", "AuthoringPhysicsPauseGate-r2-core-dev5", None),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r13-core-dev5", {}),
    ("rigidbody-motion-target", "RigidbodyMotionTarget-r1-core-dev5", {}),
    ("chef-movement-history-checkpoint", "ChefMovementHistoryCheckpoint-r3-core-dev5", {}),
    ("rigidbody-actor-rebuild", "RigidbodyActorRebuild-r13z-manifold-pool-history-core-dev5", "actor"),
    ("resume-phase", "ResumePhase-r1bc-core-dev5", {}),
    ("chef-animator-checkpoint", "ChefAnimatorCheckpoint-r53b-core-dev5", "animator"),
    ("animator-checkpoint-inspector", "AnimatorCheckpointInspector-r25b-update-zero-probe-core-dev5", None),
    ("body-restore", "BodyRestore-r36-five-step-pose-lattice-core-dev5", "body"),
    ("delivery-fade-checkpoint", "DeliveryFadeCheckpoint-r10k-persistent-backward-history-core-dev5", {}),
    ("round-end-checkpoint", "RoundEndCheckpoint-dev5", None),
)

loader.NATIVE = {
    "actor": (
        Path("artifacts/native-rigidbody-rebuild-v7-ninja/Oc2NativeRigidbodyRebuild.dll"),
        "2E6284C6380B0085853C2240D09044EE266FC7D8415442E731B529C38910A35D",
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

loader.CLASSIFICATION = (
    "Pinned local Story 1-1 dev5 rewind-parity setup with quiescent authoring physics; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
