"""Pinned Story 1-1 stack with complete interaction-graph planning."""

from pathlib import Path

import framework_load_story11_round_end_dev5_nphase_report_readiness_focus_v74 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r16c-interaction-graph-readiness-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r33-interaction-graph-cmake1" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "C97C5FD249462537444309820EE28D6FDBD54499FD92511C15404D79DCA41669",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v75 read-only audit setup. The actor provider "
    "captures the complete active-body/global-interaction/per-actor graph "
    "and exact 8/16/32 pointer-pool topology under coverage contract v4"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
