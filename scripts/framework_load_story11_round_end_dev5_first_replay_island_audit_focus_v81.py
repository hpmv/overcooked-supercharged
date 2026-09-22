"""Pinned first-replay audit with idempotent framework pause ownership."""

import framework_load_story11_round_end_dev5_first_replay_island_audit_focus_v80 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["pause-owner-guard"] = "PauseOwnerGuard-r1-core-bg4-v27-r44i"
REVISIONS["inspection"] = "Inspection-r2-pause-owner-inventory-core-bg4-v27"
loader.STACK = (
    ("pause-owner-guard", REVISIONS["pause-owner-guard"], {}),
) + tuple(
    (slot, REVISIONS.get(slot, revision), activation)
    for slot, revision, activation in loader.STACK
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v81 read-only first-replay audit setup. The exact "
    "framework stable Main-pause key is normalized once and subsequent equal "
    "acquisitions are idempotent; unrelated game pause owners and all release "
    "paths are unchanged. The controller must advertise native resume-phase "
    "metadata protocol v1. No search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
