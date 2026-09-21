"""Pinned Story 1-1 stack with atomic output-boundary SIP capture."""

from pathlib import Path

import framework_load_story11_round_end_dev5_readiness_audit_focus_v69 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r14u-atomic-sip-readiness-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r29-atomic-sip-capture-cmake1" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "4EB92B092872579349A313BA5E5CB3CA5D1719EB1C9F89636EFC5BA886FEB33A",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v70 audit setup retaining the v69 aggregate "
    "readiness provider; the actor checkpoint now seals SIP allocator state "
    "beside managers and manifolds at the exact output boundary, brackets the "
    "capture with passive NPhase observations, and leaves the later dirty-list "
    "sample unable to overwrite allocator history"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
