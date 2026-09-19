"""Pinned Story 1-1 stack projecting scheduled Animator captures into pause state."""

import framework_load_story11_round_end_dev5_pause_diag_focus_v49 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["chef-animator-checkpoint"] = (
    "ChefAnimatorCheckpoint-r56-scheduled-pause-projection-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v50 setup retaining v49 behavior; an exact scheduled "
    "advancing-boundary Animator tuple is projected in checkpoint data only into "
    "TimeManager's paused public/native speed state before resume restoration; all "
    "other Animator state remains strict, live game state is not written, and search "
    "remains disabled"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
