"""Pinned Story 1-1 stack with Transform-cache and broadphase planning."""

from pathlib import Path

import framework_load_story11_round_end_dev5_interaction_graph_readiness_focus_v75 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r17g-transform-broadphase-observer-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r34-transform-broadphase-cmake3" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "F61461A570F816F64624257C221A8CD49C10E292750878B076CD5417CC7355E6",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v76 read-only audit setup. The actor provider "
    "captures the settled Transform-cache allocator, IDs, refcounts, and "
    "active poses plus the exact subsequent pass-zero finishBroadPhase "
    "created/deleted overlap transition under coverage contract v5"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
