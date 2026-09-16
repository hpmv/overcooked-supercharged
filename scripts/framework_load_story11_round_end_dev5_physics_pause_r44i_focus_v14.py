"""Load the r44i Story 1-1 parity stack against the foreground-release v14 core."""

import framework_load_story11_round_end_dev5_physics_pause as setup


setup.loader.STACK = (
    ("level-session", "LevelSession-r2-core-bg4-v14-r44i", None),
    ("scripted-round", "ScriptedRound-r2-core-bg4-v14-r44i", {}),
    ("registry-observer", "RegistryObserver-r2-core-bg4-v14-r44i", None),
    ("inspection", "Inspection-r1-core-bg4-v14-r44i", None),
    ("world-sync-cache", "WorldSyncCache-r13n-logical-rest-clock-core-bg4-v14-r44i", {}),
    ("local-sync-bypass", "LocalSyncBypass-r1-core-bg4-v14-r44i", {}),
    ("chef-pause-pose", "ChefPausePose-r4-core-bg4-v14-r44i", {}),
    ("authoring-physics-pause-gate", "AuthoringPhysicsPauseGate-r2-core-bg4-v14-r44i", None),
    ("physics-sync-after-restore", "PhysicsSyncAfterRestore-r13-core-bg4-v14-r44i", {}),
    ("rigidbody-motion-target", "RigidbodyMotionTarget-r1-core-bg4-v14-r44i", {}),
    ("chef-movement-history-checkpoint", "ChefMovementHistoryCheckpoint-r3-core-bg4-v14-r44i", {}),
    ("rigidbody-actor-rebuild", "RigidbodyActorRebuild-r13z-manifold-pool-history-core-bg4-v14-r44i", "actor"),
    ("resume-phase", "ResumePhase-r1bc-core-bg4-v14-r44i", {}),
    ("chef-animator-checkpoint", "ChefAnimatorCheckpoint-r53b-core-bg4-v14-r44i", "animator"),
    ("animator-checkpoint-inspector", "AnimatorCheckpointInspector-r25b-update-zero-probe-core-bg4-v14-r44i", None),
    ("body-restore", "BodyRestore-r44i-exact-warp-noops-core-bg4-v14", "body"),
    ("delivery-fade-checkpoint", "DeliveryFadeCheckpoint-r10k-persistent-backward-history-core-bg4-v14-r44i", {}),
    ("round-end-checkpoint", "RoundEndCheckpoint-dev5-core-bg4-v14-r44i", None),
)

setup.loader.CLASSIFICATION = (
    "Pinned local Story 1-1 r44i rewind-parity setup with deterministic "
    "foreground release, background input, and quiescent authoring physics; no search"
)
setup.loader.__file__ = __file__
setup.loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(setup.loader.main())
