"""Pinned Story 1-1 stack restoring a recreated colliderless body's saved mode."""

import framework_load_story11_round_end_dev5_targetless_kinematic_focus_v36 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r46-recreated-colliderless-mode-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v37 setup reconstructing the saved kinematic/gravity "
    "mode on the authenticated colliderless ObjectContainer replacement before "
    "capturing and rebinding its native sidecar; exact zero motion, native identity, "
    "pose, targetlessness, and empty shape topology are required; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
