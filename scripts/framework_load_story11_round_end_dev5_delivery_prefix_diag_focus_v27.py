"""Pinned Story 1-1 v26 stack with exact recreated-presentation diagnostics."""

import framework_load_story11_round_end_dev5_pc2_delivery_root_focus_v26 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["delivery-fade-checkpoint"] = "DeliveryFadeCheckpoint-r16-prefix-diagnostics-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v27 diagnostic setup retaining the v26 rewind behavior and recording "
    "every recreated attached-order presentation prefix invariant at the authoring restore boundary; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
