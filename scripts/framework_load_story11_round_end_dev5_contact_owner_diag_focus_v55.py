"""Pinned Story 1-1 stack capturing active PhysX contact-manager owners."""

from pathlib import Path

import framework_load_story11_round_end_dev5_contact_pool_membership_diag_focus_v54 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14k-contact-manager-owner-diag-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
native = (
    Path("framework/artifacts/native-rigidbody-rebuild-r20-contact-owner-diag-cmake2/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "9069543CC915CB7BDFFC8D87B8F742BBB9B3145EC2FCC838172DDDA29DF19633",
)
loader.NATIVE["actor"] = native
loader.NATIVE["body"] = native
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v55 diagnostic setup retaining v54 behavior; each "
    "checkpoint captures a guarded, double-read, read-only semantic map of "
    "every pool-used PxsContactManager, its ShapeInstancePairLL owner, Collider "
    "endpoints, work-unit flags, cache and manifold hashes; ordinary forward "
    "gameplay is unchanged and search remains disabled"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
