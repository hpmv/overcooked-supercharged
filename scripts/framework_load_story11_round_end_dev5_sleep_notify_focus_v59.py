"""Pinned Story 1-1 stack admitting the exact pending-sleep PhysX phase."""

import framework_load_story11_round_end_dev5_authoritative_body_pause_focus_v58 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = (
    "BodyRestore-r49-sleep-notification-intermediate-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v59 parity setup retaining v58 behavior; a "
    "mass-changing targetless kinematic may pass through the exact PhysX "
    "sleep-notification queue phase before the already-required final "
    "maintenance convergence; ordinary forward gameplay is unchanged and no "
    "search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
