"""Pinned Story 1-1 stack with exact empty-proxy pause-mode restoration."""

import framework_load_story11_round_end_dev5_empty_proxy_diag_focus_v31 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r44w-empty-proxy-pause-mode-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v32 setup rebuilding an exact zero-motion colliderless "
    "ObjectContainer while preserving its current pause-lifecycle mode independently "
    "of the saved advancing mode; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
