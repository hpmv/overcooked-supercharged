"""Pinned Story 1-1 stack restoring historical PhysX shape-pair identities."""

from pathlib import Path

import framework_load_story11_round_end_dev5_contact_recreate_output_lifecycle_focus_v66 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14s-sip-identity-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r27-sip-identity-cmake2" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "2F7B7C0D6DD9CAF51F69ADAD7876198E0383682AB74BF9B159119E61703E9565",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v67 parity setup retaining v66 lifecycle handling; "
    "the rewind-only transaction now restores the exact historical "
    "ShapeInstancePairLL pool order and selects the checkpoint SIP, contact "
    "manager, and manifold identities through untouched shipped PhysX "
    "constructors; ordinary forward gameplay is unchanged and no search is "
    "performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
