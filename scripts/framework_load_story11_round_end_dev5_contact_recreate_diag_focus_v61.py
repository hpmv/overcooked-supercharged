"""Pinned Story 1-1 stack diagnosing a repeated contact recreation pair."""

from pathlib import Path

import framework_load_story11_round_end_dev5_contact_recreate_focus_v60 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14p-contact-recreate-duplicate-diag-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    Path("framework/artifacts/"
         "native-rigidbody-rebuild-r23-contact-recreate-diagnostics-cmake1/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "4641CBD12641E068D054BF3A87756EF16051674A51AC069A4507C598068D7B2E",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v61 diagnostic retaining v60 behavior; the "
    "rewind-only contact reconstruction receipt records whether a repeated "
    "shape-pair call sees its previously selected manager and manifold back "
    "in their free pools; ordinary forward gameplay is unchanged and no "
    "search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
