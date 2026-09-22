"""Pinned first-replay audit with phase-compatible tutorial skipping."""

import framework_load_story11_round_end_dev5_first_replay_island_audit_focus_v81 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["level-session"] = (
    "LevelSession-r4c-phase-compatible-tutorial-skip-core-bg4-v27-r44i"
)
loader.STACK = tuple(
    (slot, REVISIONS.get(slot, revision), activation)
    for slot, revision, activation in loader.STACK
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v82 read-only first-replay audit setup. The sashimi "
    "tutorial's native 15-second float timer is reduced from 901 render yields "
    "to its one-yield residue modulo the observed six-frame 60/50 scheduler, "
    "preserving the gameplay physics phase while retaining native dismissal, "
    "cleanup and pause ownership. Resume metadata protocol v1 and idempotent "
    "framework pause ownership remain required. No search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
