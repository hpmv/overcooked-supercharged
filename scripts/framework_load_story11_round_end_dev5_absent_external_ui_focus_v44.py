"""Pinned Story 1-1 stack restoring an absent pre-creation plate hover UI."""

import framework_load_story11_round_end_dev5_settling_freeze_focus_v43 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["delivery-fade-checkpoint"] = (
    "DeliveryFadeCheckpoint-r18c-absent-external-ui-retirement-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v44 setup retaining v43 behavior and retiring only "
    "the exact inactive, physics-free entity 1 ClientIngredientContentGUI prefab "
    "created on the abandoned future when the saved f444 owner field is CLR-null; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
