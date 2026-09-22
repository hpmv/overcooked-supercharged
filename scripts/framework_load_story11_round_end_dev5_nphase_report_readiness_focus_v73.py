"""Superseded setup layer for the Story 1-1 NPhase report audit."""

from pathlib import Path

import framework_load_story11_round_end_dev5_actor_pair_report_readiness_focus_v72 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r15a-nphase-report-readiness-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r32-nphase-report-readiness-cmake2" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "55FEAB8A3C9426DC8488A2476A480BFB60377DC1CC10020405762D3BEB13B514",
)
loader.CLASSIFICATION = (
    "Superseded Story 1-1 v73 NPhase report audit setup; use v74, which "
    "corrects the expanded managed owner-record ABI assertion"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit("v73 is superseded; use the v74 NPhase report loader")
