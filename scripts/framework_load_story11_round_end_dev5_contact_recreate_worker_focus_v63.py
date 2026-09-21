"""Pinned Story 1-1 stack serializing armed contact creation across workers."""

from pathlib import Path

import framework_load_story11_round_end_dev5_contact_recreate_lifecycle_focus_v62 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    Path("framework/artifacts/"
         "native-rigidbody-rebuild-r25-contact-recreate-worker-cmake1/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "2FD20AFFDAB02265447D36F4283C5A07F692DEB0E7A521930827845CED84A5D8",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v63 parity setup retaining v62 lifecycle "
    "admission; while an authenticated rewind plan is armed, allocator-top "
    "preparation and the untouched shipped PhysX createContactManager call "
    "are one serialized transaction across worker-thread handoffs; ordinary "
    "forward gameplay retains the original direct trampoline and no search "
    "is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
