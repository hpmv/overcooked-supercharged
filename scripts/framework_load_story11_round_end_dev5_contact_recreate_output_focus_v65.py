"""Pinned Story 1-1 stack validating contact recreation at output phase."""

import framework_load_story11_round_end_dev5_contact_recreate_transient_focus_v64 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14p-contact-recreate-output-boundary-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v65 parity setup retaining v64 native contact "
    "lifecycle handling; managed acceptance now occurs at the first replayed "
    "advancing output boundary, matching the checkpoint sidecar capture phase, "
    "with the later authoring pause retained only as a fail-closed backstop; "
    "ordinary forward gameplay is unchanged and no search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
