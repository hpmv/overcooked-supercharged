"""Pinned Story 1-1 stack with one-step targetless-kinematic sleep convergence."""

import framework_load_story11_round_end_dev5_destroyed_wrapper_sidecar_focus_v35 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r45a-targetless-kinematic-maintenance-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v36 setup admitting only an exact awake, targetless, "
    "settling kinematic preimage for a saved sleeping targetless actor, restoring "
    "its pose without a synthetic target, and requiring bit-exact convergence "
    "after the single authoring final-maintenance step; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
