"""Pinned Story 1-1 stack with complete NPhase report-history planning."""

import framework_load_story11_round_end_dev5_nphase_report_readiness_focus_v73 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r15b-nphase-report-readiness-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v74 read-only audit setup. The actor provider "
    "captures NPhase report ActorPair/persistent/force list order, the "
    "persistent split boundary, full ContactReportBuffer allocation bytes, "
    "and per-SIP report metadata under an aggregate-owned coverage contract"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
