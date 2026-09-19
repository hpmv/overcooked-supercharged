"""Pinned Story 1-1 stack restoring the canceled fade's surviving plate material."""

import framework_load_story11_round_end_dev5_flushed_presentation_retirement_focus_v40 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["delivery-fade-checkpoint"] = (
    "DeliveryFadeCheckpoint-r18a-retire-flushed-cancellation-presentation-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v41 setup retaining v40's exact outgoing cosmetic "
    "retirement and using the source sequence's authenticated pre-fade survivor "
    "snapshot to restore entity 1's original plate material before recertification; "
    "no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
