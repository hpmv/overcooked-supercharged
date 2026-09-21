"""Pinned Story 1-1 stack reporting the exact dynamic-container pose side effect."""

import framework_load_story11_round_end_dev5_contact_owner_diag_focus_v55 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["world-sync-cache"] = (
    "WorldSyncCache-r13w-container-state-diff-diag-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v56 diagnostic setup retaining v55 behavior; the "
    "strict held-owner pose guard now reports every exact managed Rigidbody "
    "field changed by the restore instead of a generic mismatch; successful "
    "restores and ordinary forward gameplay are unchanged; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
