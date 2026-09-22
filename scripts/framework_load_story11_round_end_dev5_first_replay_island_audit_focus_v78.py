"""Pinned Story 1-1 stack with a one-frame restored-island audit."""

import framework_load_story11_round_end_dev5_island_readiness_focus_v77 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r18a-first-replay-island-audit-api17-v14"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v78 read-only audit setup. After an exact f444 "
    "rewind, the actor provider can arm the existing pass-through island "
    "observer and copy the restored branch's exact first f445 pass-zero "
    "pre/post transition without changing physics or replaying the suffix"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
