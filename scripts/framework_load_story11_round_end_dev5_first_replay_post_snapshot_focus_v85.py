"""Pinned first-replay transition plus complete post-output physics audit."""

from pathlib import Path

import framework_load_story11_round_end_dev5_first_replay_transition_audit_focus_v84 as setup


loader = setup.loader
REVISIONS = dict(setup.REVISIONS)
REVISIONS["rigidbody-actor-rebuild"] = (
    "RigidbodyActorRebuild-r21-transition-post-snapshot-api18-v15"
)
loader.STACK = tuple(
    (slot, REVISIONS.get(slot, revision), activation)
    for slot, revision, activation in loader.STACK
)
loader.NATIVE = dict(loader.NATIVE)
loader.NATIVE["actor"] = (
    (Path(__file__).resolve().parents[1] / "artifacts" /
     "native-rigidbody-rebuild-r37-post-transition-snapshot-release2" /
     "Oc2NativeRigidbodyRebuild.dll"),
    "658339E51DB314CCC2675D7087B916AA06716B7B770AD7E4088C97DE80E091F6",
)
loader.CLASSIFICATION = (
    "Pinned local Story 1-1 v85 read-only first-replay transition and exact "
    "post-output audit. API18 captures the full f445 contact/SIP/ActorPair/"
    "report/InteractionGraph/TransformCache/island/manifold/dispatch/dirty "
    "state against its retained core snapshot without changing the legacy "
    "dirty hook receipt. Target and replay images are compared family by "
    "family before the ordinary f446 authoring fence. No search"
)
loader.__file__ = __file__
loader.__doc__ = __doc__


if __name__ == "__main__":
    raise SystemExit(loader.main())
