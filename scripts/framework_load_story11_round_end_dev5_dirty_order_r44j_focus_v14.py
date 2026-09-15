"""Load the v14/r44j Story 1-1 stack with dirty-interaction restoration."""

from pathlib import Path

import framework_load_story11_round_end_dev5_physics_pause_r44i_focus_v14 as setup


loader = setup.setup.loader
loader.STACK = tuple(
    (slot,
     "RigidbodyActorRebuild-r14d-dirty-interaction-order-lifecycle-core-bg4-v14-r44i"
     if slot == "rigidbody-actor-rebuild" else
     "BodyRestore-r44j-api10-exact-warp-noops-core-bg4-v14"
     if slot == "body-restore" else revision,
     activation)
    for slot, revision, activation in loader.STACK
)
loader.NATIVE = dict(loader.NATIVE)
native = (
    Path("framework/artifacts/native-rigidbody-rebuild-r17-dirty-order-cmake2/"
         "Oc2NativeRigidbodyRebuild.dll"),
    "87DFD3E0C59D5DF14F70546A0CFCF052E3007B1FB47977BE2FD9BEEE7801E0AB",
)
loader.NATIVE["actor"] = native
loader.NATIVE["body"] = native
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v14/r44j rewind-parity setup with one-shot "
    "PhysX dirty-interaction semantic-order restoration; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
