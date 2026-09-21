"""Pinned Story 1-1 stack clearing inactive target-null Animator clips."""

from pathlib import Path

import framework_load_story11_round_end_dev5_sip_identity_focus_v67 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["chef-animator-checkpoint"] = (
    "ChefAnimatorCheckpoint-r59-target-null-resolver-superset-core-bg4-v27"
)
loader.STACK = tuple((slot, REVISIONS[slot], activation)
                     for slot, _revision, activation in loader.STACK)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["animator"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-animator-checkpoint-r19-target-null-resolver-superset-build" /
     "Oc2NativeAnimatorCheckpoint.r19.dll"),
    "A66C32D8B4A5E5E8E640A009C580C9F1F1105A7B8EE26320D536473D4287ADDA",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v68 parity setup retaining v67 SIP identity "
    "restoration; paused Animator Stage A now recognizes only a verified "
    "resolver-row superset and clears a target-null clip on either exact, "
    "zero-weight branch through Unity's own SetClip path, with byte-stable "
    "preimage and exact projected postimage fences; ordinary forward gameplay "
    "is unchanged and no search is performed"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
