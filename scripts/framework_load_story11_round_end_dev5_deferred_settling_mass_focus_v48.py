"""Pinned Story 1-1 stack deferring a settling actor across mass restore."""

import framework_load_story11_round_end_dev5_pending_target_focus_v47 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = (
    "BodyRestore-r48-deferred-settling-mass-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v48 setup retaining v47 behavior and admitting the "
    "same-actor sleeping-checkpoint versus targetless-SETTLING future preimage "
    "across the module's exact automatic mass-frame restoration; one paused "
    "maintenance step must still converge to the checkpoint sleep lifecycle; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
