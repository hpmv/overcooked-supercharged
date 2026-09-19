"""Pinned Story 1-1 stack accepting nonzero signed Unity material identities."""

import framework_load_story11_round_end_dev5_active_prefade_base_focus_v45 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["delivery-fade-checkpoint"] = (
    "DeliveryFadeCheckpoint-r18e-signed-unity-material-ids-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v46 setup retaining v45 behavior and treating all "
    "nonzero signed Unity instance IDs as valid in the exact fresh-fade ownership proof; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
