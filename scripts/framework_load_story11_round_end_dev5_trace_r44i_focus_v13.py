"""Load the v13/r44i Story 1-1 stack with the native trace wrapper dormant."""

from pathlib import Path

import framework_load_story11_round_end_dev5_physics_pause_r44i_focus_v13 as setup


loader = setup.setup.loader
loader.STACK = (
    # Load the managed wrapper first, but activate its native hooks only after
    # RigidbodyActorRebuild has completed and removed its temporary
    # PxsContext::createContactManager discovery hook.
    ("native-physics-trace", "NativePhysicsTrace-r8ag-narrowphase-core-bg3-v13-r44i", None),
) + loader.STACK
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["trace"] = (
    Path("artifacts/native-physics-trace-r21-mass-diagonalize-build1/Oc2NativePhysicsTrace.r21.dll"),
    "07E7E0E0ED770802E85A70999FBE3BBEFBD6A0DF5CDB68B1BB05E237A0FA0030",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v13/r44i rewind-parity setup prepared for "
    "an explicit post-load actor-context/native-trace handoff; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
