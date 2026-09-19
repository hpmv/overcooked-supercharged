"""Pinned Story 1-1 stack with bounded one-or-two-row native shape rebind proof."""

import framework_load_story11_round_end_dev5_split_entity2_presentation_focus_v28 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r44t-bounded-recreated-shapes-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v29 rewind-parity setup with split entity 2 presentation restore "
    "and exact bijective native shape-geometry rebind proof for bounded one- or two-row "
    "collider reincarnations; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
