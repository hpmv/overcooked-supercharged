"""Pinned Story 1-1 stack with recreated empty-proxy actor-pose diagnostics."""

import framework_load_story11_round_end_dev5_empty_proxy_pause_mode_focus_v32 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = "BodyRestore-r44x-actor-pose-preflight-core-bg4-v26"
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v33 diagnostic setup retaining the v32 empty-proxy "
    "restore and recording an exact native-versus-managed actor-pose preflight "
    "mismatch before mutation; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
