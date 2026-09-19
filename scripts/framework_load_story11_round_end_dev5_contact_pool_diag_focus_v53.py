"""Pinned Story 1-1 stack reporting exact contact-pool restore mismatches."""

import framework_load_story11_round_end_dev5_animator_final_controller_focus_v52 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14i-contact-pool-mismatch-diag-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v53 diagnostic setup retaining v52 behavior; a "
    "fail-closed contact-pool restore now reports the observed and checkpoint "
    "counts, storage, hashes, and leading entries without changing either pool; "
    "ordinary forward gameplay is unchanged and search remains disabled"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
