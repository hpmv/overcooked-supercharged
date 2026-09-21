"""Pinned Story 1-1 stack using joint paused dynamic body restoration."""

import framework_load_story11_round_end_dev5_animator_resolver_superset_focus_v57 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["world-sync-cache"] = (
    "WorldSyncCache-r13y-joint-paused-body-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v58 parity setup retaining v57 behavior; after an "
    "authoring rewind the frozen maintenance fence repairs a changed dynamic-"
    "container Transform through the existing exact joint Transform/native-"
    "Rigidbody checkpoint transaction; ordinary forward gameplay is unchanged "
    "and no search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
