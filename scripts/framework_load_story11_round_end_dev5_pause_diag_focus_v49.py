"""Pinned Story 1-1 stack with one-shot paused dynamic diagnostics."""

import framework_load_story11_round_end_dev5_deferred_settling_mass_focus_v48 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["world-sync-cache"] = (
    "WorldSyncCache-r13v-one-shot-pause-diagnostic-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v49 diagnostic setup retaining v48 behavior; a failed "
    "paused dynamic-container correction is reported once with exact identity and "
    "pose values instead of livelocking every paused LateUpdate; successful restores "
    "and ordinary forward gameplay are unchanged; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
