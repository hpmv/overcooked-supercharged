"""Pinned Story 1-1 stack with read-only ActorPair partition planning."""

from pathlib import Path

import framework_load_story11_round_end_dev5_atomic_sip_readiness_focus_v70 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14w-actor-pair-readiness-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r30-actor-pair-readiness-cmake1" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "28E690C34B2437DA7AA527740299A977B2828D90173757A17E1E23CC70F03013",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v71 audit setup retaining the v70 atomic SIP "
    "boundary; the actor provider now captures the complete ActorPair free "
    "and allocated partitions plus semantic owner state at that same "
    "boundary, and audits live reachability without mutating PhysX"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
