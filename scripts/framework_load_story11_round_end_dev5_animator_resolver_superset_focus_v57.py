"""Pinned Story 1-1 stack admitting the exact paused Animator resolver superset."""

from pathlib import Path

import framework_load_story11_round_end_dev5_container_state_diff_focus_v56 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["chef-animator-checkpoint"] = (
    "ChefAnimatorCheckpoint-r58-stageb-resolver-superset-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["animator"] = (
    Path("framework/artifacts/"
         "native-animator-checkpoint-r18-stageb-resolver-superset-build/"
         "Oc2NativeAnimatorCheckpoint.r18.dll"),
    "7F46C4C63566AB368E293F908FBA872A642116FB1ED1C70DAB83C14ED82623F8",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v57 parity setup retaining v56 behavior; Animator "
    "Stage B may admit a mutation-free zero-plan live owner graph only when "
    "removing target-rooted resolver observations produces the exact checkpoint "
    "row sequence and the complete live graph remains byte-exact; final resume "
    "verification still requires exact target cardinality; no search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
