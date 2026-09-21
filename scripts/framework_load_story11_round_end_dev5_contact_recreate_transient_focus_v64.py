"""Pinned Story 1-1 stack admitting returned transient contact pairs."""

from pathlib import Path

import framework_load_story11_round_end_dev5_contact_recreate_worker_focus_v63 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    Path("framework/artifacts/"
         "native-rigidbody-rebuild-r26-contact-recreate-transient-cmake1/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "B118470161AADC0039C94BB3C826EF12FDC2F3DE27156F6E1634229163605A8D",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v64 parity setup retaining v63 serialized "
    "contact creation; maintenance-only shape pairs absent from the target "
    "checkpoint remain untouched pass-through calls and are admitted only if "
    "their LIFO allocations return before exact checkpoint count, order, "
    "membership, and owner validation; ordinary forward gameplay is unchanged "
    "and no search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
