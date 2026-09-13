"""Load the pinned destroyed-delivery rewind-parity stack built for core c7bm."""
from pathlib import Path

import framework_load_story11_r47_parity_environment as loader


loader.STACK = (
    ("level-session", "LevelSession-r2-core-c7bm", None),
    ("scripted-round", "ScriptedRound-r2-core-c7bm", {}),
    ("registry-observer", "RegistryObserver-r2-core-c7bm", None),
    ("inspection", "Inspection-r1-core-c7bm", None),
    ("world-sync-cache", "WorldSyncCache-r13k-stack-canonical-order-core-c7bm", {}),
    ("local-sync-bypass", "LocalSyncBypass-r1-core-c7bm", {}),
    ("chef-pause-pose", "ChefPausePose-r4-core-c7bm", {}),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r13-core-c7bm", {}),
    ("rigidbody-motion-target", "RigidbodyMotionTarget-r1-core-c7bm", {}),
    ("chef-movement-history-checkpoint", "ChefMovementHistoryCheckpoint-r3-core-c7bm", {}),
    ("rigidbody-actor-rebuild", "RigidbodyActorRebuild-r13z-manifold-pool-history-core-c7bm", "actor"),
    ("resume-phase", "ResumePhase-r1bc-core-c7bm", {}),
    ("chef-animator-checkpoint", "ChefAnimatorCheckpoint-r53b-core-c7bm", "animator"),
    ("body-restore", "BodyRestore-r32-recreated-native-shape-state-rebind-core-c7bm", "body"),
    ("delivery-fade-checkpoint", "DeliveryFadeCheckpoint-r10k-persistent-backward-history-core-c7bm", {}),
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
    "Pinned local Story 1-1 post-delivery destroyed-root rewind-parity setup on core c7bm; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
