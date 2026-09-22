"""Pinned first-replay broadphase-plus-island output-boundary audit."""

import framework_load_story11_round_end_dev5_first_replay_island_audit_focus_v83 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r20-first-replay-transition-audit-api17-v14"
)
loader.STACK = tuple(
    (slot, REVISIONS.get(slot, revision), activation)
    for slot, revision, activation in loader.STACK
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v84 read-only first-replay transition audit. "
    "The f444 rewind arms both native finishBroadPhase and island observers; "
    "their exact f445 results are copied at the advancing output boundary "
    "before contact validation or the ordinary f446 pause can cancel them. "
    "Contact receipts expose the actual matched manager mask. The worker-safe "
    "API17 native observer, phase-compatible tutorial shortcut, and resume "
    "metadata protocol v1 remain unchanged. No search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
