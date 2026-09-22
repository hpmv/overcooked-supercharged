"""Pinned Story 1-1 stack with complete ActorPair report-history planning."""

from pathlib import Path

import framework_load_story11_round_end_dev5_actor_pair_readiness_focus_v71 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14z-actor-pair-report-readiness-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r31-actor-pair-report-readiness-cmake3" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "CD8657CDB046EB73C1FCBEB6AA47A8F121B3511CEE0257DE469C4E7A8E055933",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v72 read-only audit setup. The actor provider "
    "validates PhysX touch/refcount semantics, scans the exact actor interaction "
    "arrays used by findActorPair, captures the complete ActorPairContactReportData "
    "pool partition and object bytes, and uses an aggregate-owned coverage contract"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
