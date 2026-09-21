"""Pinned Story 1-1 stack recreating checkpoint contact ownership."""

from pathlib import Path

import framework_load_story11_round_end_dev5_sleep_notify_focus_v59 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14o-contact-recreate-safe-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    Path("framework/artifacts/"
         "native-rigidbody-rebuild-r22-contact-recreate-strict-cmake3/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "FE6D4B62F37093B3E67BFF20C1017B532EEC48C5363605BA541E6AF2ADA51C7A",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v60 parity setup retaining v59 behavior; only an "
    "authenticated rewind whose live allocator membership is the exact union "
    "of checkpoint-free and checkpoint-owned entries arms a one-maintenance "
    "shape-pair keyed contact-manager/large-manifold recreation plan; the "
    "observer remains a pass-through during ordinary forward gameplay and no "
    "search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
