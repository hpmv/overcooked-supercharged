"""Pinned Story 1-1 rewind-parity stack with destroyed-wrapper identity qualification."""

import framework_load_story11_round_end_dev5_detached_delivery_focus_v15 as setup


loader = setup.loader
REVISIONS = {
    "level-session": "LevelSession-r2-core-bg4-v16-r44i",
    "scripted-round": "ScriptedRound-r2-core-bg4-v16-r44i",
    "registry-observer": "RegistryObserver-r2-core-bg4-v16-r44i",
    "inspection": "Inspection-r1-core-bg4-v16-r44i",
    "world-sync-cache": "WorldSyncCache-r13u-mixed-logical-spawn-path-core-bg4-v16",
    "local-sync-bypass": "LocalSyncBypass-r1-core-bg4-v16-r44i",
    "chef-pause-pose": "ChefPausePose-r4-core-bg4-v16-r44i",
    "authoring-physics-pause-gate": "AuthoringPhysicsPauseGate-r2-core-bg4-v16-r44i",
    "physics-sync-after-restore": "PhysicsSyncAfterRestore-r13-core-bg4-v16-r44i",
    "rigidbody-motion-target": "RigidbodyMotionTarget-r1-core-bg4-v16-r44i",
    "chef-movement-history-checkpoint": "ChefMovementHistoryCheckpoint-r3-core-bg4-v16-r44i",
    "rigidbody-actor-rebuild": "RigidbodyActorRebuild-r14h-target-frame-json-int64-focus-bg4-v16",
    "resume-phase": "ResumePhase-r1bc-core-bg4-v16-r44i",
    "chef-animator-checkpoint": "ChefAnimatorCheckpoint-r55-chef-owned-animator-core-bg4-v16",
    "animator-checkpoint-inspector": "AnimatorCheckpointInspector-r25b-update-zero-probe-core-bg4-v16-r44i",
    "body-restore": "BodyRestore-r44s-sleeping-kinematic-noop-transform-core-bg4-v16",
    "delivery-fade-checkpoint": "DeliveryFadeCheckpoint-r11-detached-container-early-restore-core-bg4-v16",
    "round-end-checkpoint": "RoundEndCheckpoint-dev5-core-bg4-v16-r44i",
}
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v16 rewind-parity setup with exact future-frame Animator/PhysX "
    "sidecars, detached-container mid-delivery reconstruction, and CLR identity qualification "
    "for retained destroyed Unity wrappers; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
