"""Pinned Story 1-1 stack with split-phase entity 2 presentation restore."""

import framework_load_story11_round_end_dev5_delivery_prefix_diag_focus_v27 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["delivery-fade-checkpoint"] = "DeliveryFadeCheckpoint-r17-split-entity2-presentation-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v28 rewind-parity setup with entity 2 collider/scale restoration "
    "before core physics certification and attached-order presentation/PC2 iterator reconstruction "
    "after the queued local contents event; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
