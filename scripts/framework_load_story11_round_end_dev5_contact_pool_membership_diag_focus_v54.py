"""Pinned Story 1-1 stack reporting exact contact-pool membership differences."""

import framework_load_story11_round_end_dev5_contact_pool_diag_focus_v53 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14j-contact-pool-membership-diag-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v54 diagnostic setup retaining v53 behavior; a "
    "failed contact-pool restore performs one additional caller-owned, "
    "read-only capture and reports the exact live-only and checkpoint-only "
    "manager pointers without changing either pool; ordinary forward gameplay "
    "is unchanged and search remains disabled"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
