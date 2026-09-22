"""Pinned Story 1-1 stack with full read-only PhysX island planning."""

from pathlib import Path

import framework_load_story11_round_end_dev5_transform_broadphase_readiness_focus_v76 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r18-island-api17-final-v13"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r35-island-api17-release" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "33741A06BA60F9D7712AEB9A9E02FDD9E09645BC229E875AA07C62F67291AAF1",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v77 read-only audit setup. The actor provider "
    "captures the complete settled PhysX island allocators, topology, "
    "bitmaps, change queues, and semantic contact-edge bindings plus the "
    "exact subsequent pass-zero island pre/post transition and add/remove "
    "journal under coverage contract v6"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
