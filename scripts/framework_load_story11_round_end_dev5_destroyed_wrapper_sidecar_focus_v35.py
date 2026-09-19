"""Pinned Story 1-1 stack with destroyed-wrapper native-sidecar rebinding."""

import framework_load_story11_round_end_dev5_destroyed_empty_sidecar_focus_v34 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r44z-destroyed-wrapper-sidecar-rebind-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v35 setup authenticating a destroyed historical Unity "
    "Rigidbody by managed reference while requiring the current colliderless body "
    "to remain live, then rebinding its native sidecar exactly; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
