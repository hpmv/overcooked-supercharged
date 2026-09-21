"""Pinned Story 1-1 stack admitting clean multi-output contact progress."""

import framework_load_story11_round_end_dev5_contact_recreate_transient_focus_v64 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14q-contact-recreate-output-lifecycle-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v66 parity setup retaining v64 native contact "
    "lifecycle handling; authenticated non-poisoned partial recreation may "
    "progress across advancing output boundaries, while exact checkpoint "
    "count, order, manifold ownership, and owner mapping are still required "
    "before acceptance and the next authoring pause is a hard deadline; "
    "ordinary forward gameplay is unchanged and no search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
