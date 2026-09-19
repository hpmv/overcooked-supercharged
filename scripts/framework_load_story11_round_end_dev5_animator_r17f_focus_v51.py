"""Pinned Story 1-1 stack using the proved inverse-owner Animator helper."""

from pathlib import Path

import framework_load_story11_round_end_dev5_animator_pause_projection_focus_v50 as setup


loader = setup.loader
REVISIONS = setup.REVISIONS
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["animator"] = (
    Path("artifacts/native-animator-checkpoint-r17f-target-time-subset-build/"
         "Oc2NativeAnimatorCheckpoint.r17f.dll"),
    "4E4407C84EB97A2CBCB338433F928E22E7888AB73FDFA17B5AEA5EA09C45430C",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v51 setup retaining v50 behavior and replacing the "
    "stale r17d Animator helper with previously validated r17f target-time-subset "
    "restoration for a smaller checkpoint owner graph against live-only resolver "
    "nodes; ordinary forward gameplay is unchanged and search remains disabled"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
