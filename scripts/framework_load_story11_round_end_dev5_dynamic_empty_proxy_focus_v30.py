"""Pinned Story 1-1 stack with exact dynamic empty-proxy mass restoration."""

import framework_load_story11_round_end_dev5_bounded_shape_rebind_focus_v29 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r44u-dynamic-empty-proxy-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v30 rewind-parity setup with split entity 2 presentation restore, "
    "bounded exact native shape rebinds, and exact motionless dynamic-or-kinematic empty "
    "ObjectContainer mass-frame restoration; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
