"""Load the pinned destroyed-delivery rewind-parity stack into fresh Story 1-1."""
from pathlib import Path

import framework_load_story11_r47_parity_environment as loader


loader.STACK = (
    ("level-session", "LevelSession-r2-core-c7bl", None),
    ("scripted-round", "ScriptedRound-r2-core-c7bl", {}),
    ("registry-observer", "RegistryObserver-r2-core-c7bl", None),
    ("inspection", "Inspection-r1-core-c7bl", None),
    ("world-sync-cache", "WorldSyncCache-r13d-live-fixed-membership-core-c7bl", {}),
    ("local-sync-bypass", "LocalSyncBypass-r1-core-c7bl", {}),
    ("chef-pause-pose", "ChefPausePose-r4-core-c7bl", {}),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r13-core-c7bl", {}),
    ("rigidbody-motion-target", "RigidbodyMotionTarget-r1-core-c7bl", {}),
    ("chef-movement-history-checkpoint", "ChefMovementHistoryCheckpoint-r3-core-c7bl", {}),
    ("rigidbody-actor-rebuild", "RigidbodyActorRebuild-r13y-core-c7bl", "actor"),
    ("resume-phase", "ResumePhase-r1bc-core-c7bl", {}),
    ("chef-animator-checkpoint", "ChefAnimatorCheckpoint-r53b-core-c7bl", "animator"),
    ("body-restore", "BodyRestore-r32-recreated-native-shape-state-rebind-core-c7bl", "body"),
    ("delivery-fade-checkpoint", "DeliveryFadeCheckpoint-r10h-future-return-tree-transaction-core-c7bl", {}),
)

loader.NATIVE = {
    "actor": (
        Path("artifacts/native-rigidbody-rebuild-r12-multiframe-sidecars-testbuild1/Oc2NativeRigidbodyRebuild.dll"),
        "E28EA2D8EE26FD3D7E363334A6C9E90D7F9BCE0CD999CFD1F73D51A199FCA25E",
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
    "Pinned local Story 1-1 post-delivery destroyed-root rewind-parity setup; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
