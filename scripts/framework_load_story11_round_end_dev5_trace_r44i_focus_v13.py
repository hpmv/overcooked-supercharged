"""Load the v13/r44i Story 1-1 stack with the native trace wrapper dormant."""

from pathlib import Path

import framework_load_story11_round_end_dev5_physics_pause_r44i_focus_v13 as setup


loader = setup.setup.loader
loader.STACK = (
    # Load the managed wrapper first, but activate its native hooks only after
    # RigidbodyActorRebuild has completed and removed its temporary
    # PxsContext::createContactManager discovery hook.
    ("native-physics-trace", "NativePhysicsTrace-r8ah-dirty-order-core-bg3-v13-r44i", None),
) + loader.STACK
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["trace"] = (
    Path("artifacts/native-physics-trace-r22-dirty-order-build1/Oc2NativePhysicsTrace.r22.dll"),
    "96A57839A3A2F5D71B2F7E559184E2799221FF1A2936B959736DCEE5805BA502",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v13/r44i rewind-parity setup prepared for "
    "an explicit post-load actor-context/native-trace handoff; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
