"""Load the v14/r44j Story 1-1 stack with dirty-interaction restoration."""

from pathlib import Path

import framework_load_story11_round_end_dev5_physics_pause_r44i_focus_v14 as setup


loader = setup.setup.loader
loader.STACK = tuple(
    (slot,
     "RigidbodyActorRebuild-r14e-dirty-projection-core-bg4-v14-r44i"
     if slot == "rigidbody-actor-rebuild" else
     "BodyRestore-r44s-sleeping-kinematic-noop-transform-core-bg4-v14"
     if slot == "body-restore" else revision,
     activation)
    for slot, revision, activation in loader.STACK
)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE_OPTIONS = dict(loader.NATIVE_OPTIONS)
loader.NATIVE_OPTIONS["actor"] = dict(loader.NATIVE_OPTIONS.get("actor", {}))
loader.NATIVE_OPTIONS["actor"]["dirtyInteractionRestoreProjection"] = True
native = (
    Path("framework/artifacts/native-rigidbody-rebuild-r19-target-invalidate-cmake2/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "E2A1569A586CA8488A110BAECD47865FA31C79F0923FB36FCB395D36AC4302A1",
)
loader.NATIVE["actor"] = native
loader.NATIVE["body"] = native
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v14/r44j rewind-parity setup with one-shot "
    "PhysX dirty-interaction semantic-order projection; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
