"""Pinned Story 1-1 stack admitting the exact paused PhysX settling lifecycle."""

import framework_load_story11_round_end_dev5_external_ui_diag_focus_v42 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = (
    "BodyRestore-r46b-authoring-freeze-settling-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v43 setup retaining v42 behavior and admitting only "
    "the exact targetless PhysX BF_KINEMATIC_SETTLING lifecycle when the authoring "
    "freeze contributes BF_DISABLE_GRAVITY; post-maintenance validation remains "
    "strict for actor-local identity, pose, island, bitmap, and membership state; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
