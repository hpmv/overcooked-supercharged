"""Pinned Story 1-1 stack with exact empty-proxy prerequisite diagnostics."""

import framework_load_story11_round_end_dev5_dynamic_empty_proxy_focus_v30 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r44v-empty-proxy-diagnostics-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v31 diagnostic setup retaining v30 restore behavior and recording "
    "saved/current empty ObjectContainer kinematic and motion prerequisites exactly; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
