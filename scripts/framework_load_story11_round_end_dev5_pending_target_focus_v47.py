"""Pinned Story 1-1 stack reconstructing a recreated proxy's pending target."""

from pathlib import Path

import framework_load_story11_round_end_dev5_signed_material_ids_focus_v46 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["body-restore"] = (
    "BodyRestore-r47-recreated-pending-target-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
native = (
    Path("framework/artifacts/native-rigidbody-rebuild-r17-kinematic-target-restore-cmake2/"
         "Release/Oc2NativeRigidbodyRebuild.dll"),
    "75C6A466EB19CF3EBF50EAC41F80FA466FA8445285748FCA787158C6158F2018",
)
loader.NATIVE["actor"] = native
loader.NATIVE["body"] = native
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v47 setup retaining v46 behavior and reconstructing "
    "only the proven colliderless replacement proxy's current-pose pending "
    "kinematic target through PhysX's official setter; one paused maintenance "
    "step must converge to the exact source-derived settling lifecycle; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
