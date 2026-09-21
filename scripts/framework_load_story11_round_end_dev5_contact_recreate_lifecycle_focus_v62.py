"""Pinned Story 1-1 stack admitting safe contact destroy/recreate lifecycles."""

from pathlib import Path

import framework_load_story11_round_end_dev5_contact_recreate_diag_focus_v61 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    Path("framework/artifacts/"
         "native-rigidbody-rebuild-r24-contact-recreate-recycle-cmake1/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "F4D0211F2094ADDA373ECF3DFEA1BD71492327449C7F98AB8B994ACFEA41B1A4",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v62 parity setup retaining v61 diagnostics; an "
    "authenticated rewind contact row may be selected again only after its "
    "exact manager and manifold have both returned to their free structures, "
    "all slot-membership bits and the SIP backlink are clear, and no live "
    "manager owns the same shape pair; ordinary forward gameplay is unchanged "
    "and no search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
