"""Pinned first-replay audit with native-lifecycle sashimi tutorial skipping."""

import framework_load_story11_round_end_dev5_first_replay_island_audit_focus_v78 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["level-session"] = "LevelSession-r4b-early-tutorial-skip-core-bg4-v27-r44i"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v80 read-only first-replay audit setup. The level "
    "adapter replaces only the sashimi popup's native 15-second wait with an "
    "already-complete enumerator, then verifies that the original coroutine "
    "performed its dismissal callback, canvas cleanup, pause-owner release and "
    "Shutdown exactly once; no search and no gameplay-state shortcut"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
