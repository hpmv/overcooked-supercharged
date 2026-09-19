"""Pinned Story 1-1 stack proving the virgin entity-2 base renderer preimage."""

import framework_load_story11_round_end_dev5_absent_external_ui_focus_v44 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["delivery-fade-checkpoint"] = (
    "DeliveryFadeCheckpoint-r18d-active-prefade-base-proof-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v45 setup retaining v44 behavior and validating the "
    "fresh entity-2 plate base renderer against the target iterator's authenticated "
    "pre-fade material snapshot before reconstructing the f444 fade; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
