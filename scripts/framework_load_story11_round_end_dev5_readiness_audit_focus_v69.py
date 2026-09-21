"""Pinned Story 1-1 stack with a non-poisoning rewind readiness audit."""

from pathlib import Path

import framework_load_story11_round_end_dev5_target_null_resolver_superset_focus_v68 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14t-readonly-readiness-audit-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r28-readiness-audit-cmake1" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "D032E605CEE88BBE3D2D1072BF3D9EA22B8730036AF8C59918AA786520E9ACD6",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v69 audit setup retaining the v68 Animator repair; "
    "the actor provider now plans contact/SIP/manifold reconstruction through "
    "a repeatable read-only native audit, reports every independently testable "
    "admission condition, and labels later PhysX families as deferred without "
    "arming hooks, poisoning state, or running search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
