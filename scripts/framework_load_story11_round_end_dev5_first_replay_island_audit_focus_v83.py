"""Pinned first-replay audit with dispatcher-worker-safe native observers."""

from pathlib import Path

import framework_load_story11_round_end_dev5_first_replay_island_audit_focus_v82 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r19-worker-callback-api17-v13"
)
loader.STACK = tuple(
    (slot, REVISIONS.get(slot, revision), activation)
    for slot, revision, activation in loader.STACK
)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r36-worker-observers-release" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "A03C0BC3A80FEFDD8346E7CDDD1A58575DAE35D4F0BE36A991ED2BEB7D3CD8D1",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v83 read-only first-replay audit setup. PhysX "
    "finishBroadPhase and island-update observations retain separate nonzero "
    "arming and dispatcher-callback thread IDs; exact Scene/manager, Context/"
    "NPhase, pass, ordinal, pre/post snapshot, and double-copy validation own "
    "the transaction. The phase-compatible tutorial shortcut and resume "
    "metadata protocol v1 remain required. No search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
