"""Pinned Story 1-1 stack diagnosing the canceled plate's external hover UI."""

import framework_load_story11_round_end_dev5_flushed_presentation_material_focus_v41 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["delivery-fade-checkpoint"] = (
    "DeliveryFadeCheckpoint-r18b-external-ui-diagnostics-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v42 diagnostic setup retaining v41 behavior and "
    "recording exact target/current ClientIngredientContentGUI controller, instance, "
    "object, parent, active-state, layer, and component identities on rejection; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
