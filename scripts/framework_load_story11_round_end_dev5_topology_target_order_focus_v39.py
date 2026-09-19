"""Pinned Story 1-1 stack with replacement-aware canonical registry ordering."""

import framework_load_story11_round_end_dev5_recreated_colliderless_mode_focus_v38 as setup


loader = setup.loader
REVISIONS = {
    slot: revision.replace("bg4-v26", "bg4-v27")
    for slot, revision in setup.REVISIONS.items()
}
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v39 setup retaining the exact mixed-incarnation, "
    "delivery-presentation, colliderless-body, and targetless-kinematic restores "
    "while finalizing entity, ingredient, and physics registries in the already "
    "validated replacement-aware historical order; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
