"""Pinned Story 1-1 stack with live replacement mode diagnostics."""

import framework_load_story11_round_end_dev5_recreated_colliderless_mode_focus_v37 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r46a-recreated-colliderless-mode-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v38 setup reconstructing the saved kinematic/gravity "
    "mode on the authenticated colliderless ObjectContainer replacement, with all "
    "native pre/post diagnostics read from the live replacement rather than the "
    "destroyed historical Unity wrapper; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
