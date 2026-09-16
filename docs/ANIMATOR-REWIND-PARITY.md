# Animator rewind-parity snapshot — 2026-09-10

This is the current high-value context for the Story 1-1 rewind investigation.
Read it together with `docs/FRESH-SESSION-HANDOFF.md`, but treat this file as
newer where their rewind status differs. The active milestone remains **full
rewind continuation parity**. Do not run route search yet.

For a shorter explanation of the causal chain and current decision, start with
[`REWIND-PARITY-CONTEXT-SNAPSHOT.md`](REWIND-PARITY-CONTEXT-SNAPSHOT.md).

## 2026-09-12: r52 closes replay-prefix branch composability

The r51c prefix-commit guard was checking
`Hpmv.Injector.Server.CurrentFrameData.FrameNumber` from a paused hot-call.
Live diagnostics proved that value was 0 while the controller's exact paused
output boundary was frame 487, so an otherwise exact commit was rejected by
the wrong clock. Animator r52 requires exactly one explicit `frame` argument
from the controller boundary and independently requires it to equal the last
history key and the retained reference boundary. All prior exact semantic,
callback-cursor, failure, resume-coordination, and pause-fence checks remain.

The direct 789 -> 425 -> 487 test now commits 62 verified prefix frames at
487, discards 302 abandoned reference frames and four callback-tail entries,
records `gameStateMutation=false`, and returns ReplayActive to Record. A
second 487 -> 425 -> 487 rewind/replay then passes naturally in Record mode.
Immediate post-commit inspection remained exact, including raw native physics.

Evidence:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r13y-r12-nonadjacent-789-to425-to487-prefix-commit-r1/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/animator-r52-hotload-r1.json`; and
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/animator-r52-activate-r1.json`.

Animator r52 SHA-256 is
`C9393018DAD340C8075B779437C928A54EF80EAFBD43EC87A6601F9080A13A54`.
This closes the known active-prefix branch defect.

The staged target-before-first-resume path now passes as well. From held frame
546, rewind to loose frame 489 produced an exact pending ReplayStaged
transaction. A deliberate commit request for frame 490 failed before mutation;
the complete paused controller/native boundary remained exact and the staged
transaction was unchanged. The exact frame-489 commit then discarded all 57
future references and one callback-tail entry while retaining the linked
resume-prefix state. It remained ReplayStaged until the ordinary first resume,
then completed the native Animator transaction in Record mode and reproduced
the original held-546 endpoint exactly, including raw native physics and the
contact/Transform sidecars. Evidence:
`artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r26-staged-prefix-commit-frame489-r1.json`.
Both general commit timings—after an exact active prefix and immediately at a
restored target—are therefore live-proved, including a wrong-boundary
fail-closed case.

## 2026-09-12: BodyRestore r26 closes loose-to-held reverse topology

The first held-546 -> loose-489 rewind passed every same-incarnation identity,
hierarchy, material, active-state, analytic-geometry, motion, and native
PxShape precondition but found a different scale on a non-root collider
ancestor. This was not safe to ignore: the successful r26 receipt measures
`ChoppedSushiFish(Clone)_Rigidbody/ChoppedSushiFish` at `(1,1,1)` before
restore versus the target `(1,1,0.9999999)`, followed by native shape
`poseChanged=1`, `geometryChanged=1`, and exact restored order/hash.

r26 therefore treats only non-root collider-ancestor scale as checkpointed
pose. It explicitly retains byte-exact Rigidbody-root scale, exact array
length and hierarchy identities, and every prior static/native precondition.
The existing collider-ancestor restore writes the target pose, reads it back,
and performs strict managed and native-shape postcondition checks. No
`Physics.SyncTransforms` was required in the observed case.

A fresh end-to-end loose frame-489 -> pickup/held frame-546 -> rewind -> replay
passes, and the same checkpoint passes two more unwind/replay cycles without
reload. Both original and replay independently prove the two-sided chef/item
attachment and native proxy-body alias retirement. Entities, food, native
round/physics/clocks, Animator semantics, input, contact-manager LIFO, and
TransformChangeDispatch are exact; actor rebuild count remains zero.

Evidence:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r26-r13y-r12-loose489-to-held546-r3/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r26-r13y-r12-loose489-to-held546-repeat-r1/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/body-r26-status-after-held546-to-loose489-r1.json`; and
- `framework-run/modules/BodyRestore-r26-dynamic-nonroot-scale-restore/manifest.json`.

BodyRestore r26 SHA-256 is
`C8D9F69BBD71B3C519A36B16EE9C958100E1B9AE717EC186C7CA2A485FC8568B`.
The pickup observer was also corrected to recognize stable hierarchy paths
such as item 53's `[30,0,0]` and its native proxy alias; that Python-only
change does not write or alter game state. Search remains disabled.

## 2026-09-12: loose prepared-food checkpoint passes under r12f/r24h

Animator r50/native r17f remained exact while the composed prepared-food test
was extended from its loose frame-487 endpoint through frame 789. The two new
failures were conservative WorldSync/BodyRestore admission boundaries, not
Animator drift.

WorldSync r12f represents loose owner 53 as the direct child of its surviving
registered Rigidbody container 54, with false server/client attachment flags
and exact server/client WorldObject parent caches naming entity 54. The
checkpoint's pending reliable-rest transition is restored through the existing
relative-deadline model. BodyRestore r24h admits the collider that moves under
container 54 only for an exact surviving body/collider/PxShape incarnation;
static collider/material/geometry/hierarchy differences reject before native
shape mutation, and collider-bearing cross-incarnation restore remains
unsupported.

The held regression and composed loose receipt are:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12f-r24h-prepared-food-held-dash-drop-frame425-to487-r1/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12f-r24h-composed-loose-prepared-food-frame487-to789-r1/summary.json`.

Both report exact immediate restore and replay endpoint state. The loose cell
also proves exact Animator semantic state, native physics/clocks, contact-pool
order, and TransformChangeDispatch after 302 observed frames. WorldSync r12f
SHA-256 is
`BF67C0D9C773B99444443B78D9A495B40A0219DBFCAB5481073C2F1281217459`;
BodyRestore r24h SHA-256 is
`12CD85F6E2820E0B3C0920C1B82A1C4565C4CCADB0DBFB6F6D711A42F4636E03`.
Search remains disabled; next test repeated and non-adjacent rewind order.

## 2026-09-12: WorldSync r12b closes prepared-food dash/drop parity

Animator r50/native r17f remained exact throughout the first real prepared-
food held-dash/drop replay. The new failures were outside the Animator and are
recorded here because they affected the same complete rewind boundary.

WorldSync r12a extends checkpoint membership from the four fixed initial items
to admitted dynamic `PhysicalAttachment` owners. This was required because the
frame-425 prepared owner 53 server and client caches both named chef 45, while
the frame-487 future was detached. Without restoring that dynamic cache, the
first post-rewind synchronization performed a false `CorrectScale`, producing
the old frame-428 `1` versus `0.999999762` mismatch. r12a restores the exact
owner/cache incarnation and parent; it does not remove or bypass ordinary
forward synchronization.

A clean uninterrupted rerun then found a separate immediate baseline mismatch
on proxy 54. At the checkpoint, Rigidbody z bits were `CC4FDBC0` and Transform
z bits were `CA4FDBC0`. BodyRestore's receipt proves it reconstructed that
split exactly. The final paused/frozen lifecycle later materialized the body
pose into the Transform. WorldSync r12b therefore retains the target Transform
for admitted colliderless dynamic containers across paused post-warp
maintenance. It writes no Rigidbody field, verifies the complete Rigidbody
pre/post state, requires exact owner/body/parent/shape identity, and clears the
correction on resume. It cannot change advancing gameplay.

The offline suite passes 117 checks. Live evidence:

- failing r12a diagnostic:
  `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12a-prepared-food-held-dash-drop-frame425-to487-r3/summary.json`;
- exact r12b replay:
  `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12b-prepared-food-held-dash-drop-frame425-to487-r1/summary.json`;
- offline report: `artifacts/framework-world-sync-cache-r12b-tests.json`.

The passing replay is exact at the restored frame-425 baseline and frame-487
endpoint for managed entities, native round/food/physics/clocks, fixed input,
Animator semantics, contact-manager LIFO state, and TransformChangeDispatch.
WorldSync r12b SHA-256 is
`D6EF7A12E515C908451C10B6DB1CA3E20E3A987AD29AB8FE72FCFED9764A1DAC`.
Search remains disabled. Next compose a checkpoint from the replayed loose
prepared-food frame 487 and then test mixed non-adjacent rewind order.

## 2026-09-12: r50/r17f closes inverse target-node cardinality

The r49/r17e frame-324 restore reached Stage B with an exact non-Animator
baseline but refused Player 2 before mutation: its target owner capture had
76 rows/26 unique physical nodes while the current graph had 104 rows/28
unique nodes. Player 3 had the same shape. Independent comparison found no
target-only physical node. The two live-only nodes were resolver-path
`AnimationLayerMixerPlayable` and `AnimatorControllerPlayable` objects. They
remain reachable before transition normalization and disappear from the
captured owner walk after the separately guarded `EndTransition` operation.

Native r17f generalizes playable-time restoration only for this proved shape:
all target nodes must exist in the current graph, current-only nodes are not
validated as target clocks and are never mutated, the current flat graph must
retain exact cardinality and match the projected postimage, and final
post-normalization verification remains exact and mutation-forbidden. Managed
r50 requires `uniqueTargetNodes <= uniqueCurrentNodes` and records
`targetNodesAreLiveSubset`; all other admission checks remain strict.

Fresh live validation at frame 177 exercised three 26-target/28-current
transitions in one rewind. Each Stage-B receipt completed 26 plans against the
unchanged 104-row live graph; final graphs were the exact 76-row targets. The
32-frame replay passed exact entity, native round/food/physics/clock,
contact-manager LIFO, TransformChangeDispatch, fixed-input, and Animator
semantic comparisons. Three more rewinds of that checkpoint passed without a
reload or Rigidbody actor rebuild.

The canonical frame-234 held-transition plate throw was then replayed with its
historical input SHA-256
`40a90f5f013bfa3bbd9b052023547216cb21aece9738806ea047c9d4a3d59890`.
Its exact physics and Animator boundary passed. A new checkpoint composed from
the replayed loose-plate frame 266 also passed 300 neutral payload frames to
568, input SHA-256
`ed0a99f959188e70996ecc8c6f1152bba61b18e37831121c1a72b3bc48334aec`.

Evidence:

- `artifacts/framework-migration/story11-animator-r49-host-r33/r49-frame324-failed-resume-status-r1.json`;
- `artifacts/framework-migration/story11-animator-r49-host-r33/animator-inspector-frame324-failed-resume-owner-r1.json`;
- `artifacts/framework-migration/story11-animator-r49-host-r33/r50-r17f-multichef-inverse-transition-frame177-r2/summary.json`;
- `artifacts/framework-migration/story11-animator-r49-host-r33/r50-r17f-multichef-inverse-transition-frame177-repeat-r2/summary.json`;
- `artifacts/framework-migration/story11-animator-r49-host-r33/r50-status-after-frame177-pass-r1.json`;
- `artifacts/framework-migration/story11-animator-r49-host-r33/r50-r17f-canonical-plate-throw-frame234-to266-r1/summary.json`; and
- `artifacts/framework-migration/story11-animator-r49-host-r33/r50-r17f-composed-post-throw-neutral300-frame266-to568-r1/summary.json`.

Active managed r50 SHA-256 is
`CE77A5E20A2F0B0963ABF11CACCEE3BED33EDF431CEE05E18E5E780B1E256BA5`;
native r17f SHA-256 is
`4E4407C84EB97A2CBCB338433F928E22E7888AB73FDFA17B5AEA5EA09C45430C`.
Search remains disabled. Next test an actual prepared-food attachment
transfer/drop and mixed non-adjacent composed rewinds.

## 2026-09-11: dynamic food composition passes after controller-only repairs

Animator r48/native r17d remains exact after expanding from the closed
transition matrix and plate throw into a real workable replacement. The first
frame-90 -> 215 chop/replacement replay passes. A checkpoint at the replayed
prepared-food frame 215 then passes 180 neutral payload frames to 397, and a
new checkpoint at that replayed frame 397 passes another 180-frame future to
579. These are composed checkpoints with prepared entity 53 and physical
proxy 54 alive, not fresh-level repetitions.

The initial frame-397 -> 215 attempt had already restored every compared game
and native boundary exactly. Its failure was post-restore controller evidence:
the audit discarded consumed raw parent `[30,0]`, so it could no longer prove
prepared child `[30,0,0]` against the parent's observed native spawn table.
Headless v12n retains such historical ancestors only while a current target
descendant requires them. Its exact receipt has no fresh registrations and no
native mutation. It still deletes abandoned future-only paths. Earlier in the
same lifecycle, v12m corrected the controller's stale workstation item at the
observed raw-destruction/prepared-replacement boundary. Both changes are
managed reconstruction/evidence logic; neither changes forward gameplay,
Animator state, attachments, or physics.

v12n DLL SHA-256 is
`E50F7650AC505CB0AA27A509EDBF8A2841651D71069A8A038EB6DD9811710B9D`.
Primary live receipts are under
`artifacts/framework-migration/story11-animator-r48-host-r31/`, in
`r48-v12n-food-chop-frame90-to215-r3`,
`r48-v12n-prepared-food-frame215-neutral180-r1`, and
`r48-v12n-prepared-food-frame397-neutral180-composed-r1`.

## 2026-09-11: r48 fixes composed ControllerInput history and passes plate stress

The r47 failure on a checkpoint created from a replayed frame 10 was not an
unrestored transition, owner graph, pose, Playable clock, or physics state.
All of those were exact. The sole final difference for every chef was
ControllerInput layer-0 `record+0x00..+0x07`: saved
`AA2FBA390BD7A33D`, live zero. The first word is state hash `0x39BA2FAA`;
the second is the already-understood normalized-time residue.

UnityPlayer disassembly closes the ownership question. `GotoStateInternal`
sets `StateMachineMemory+0x6B` and writes its requested-state hash to
ControllerInput `record+0`. `EvaluateStateMachine` reads that word only in the
`+0x6B`-gated command path, then clears the gate without clearing the record.
Both saved and live gates were zero in the failure. Future `GotoState` calls
overwrite the word before consuming a new command. It is therefore inert
consumed-command history under exactly the dual-clear condition.

Managed r48 extends the existing dual-clear semantic exemption from
`record+4..+7` to `record+0..+7`. It adds no write and leaves the complete
record strict whenever either gate is nonzero. Raw bytes and hashes remain in
telemetry. The build SHA-256 is
`EA1EC534ED03D0C98C24F6F1E20F6AA361BE9C7829DF459D9C37FE4607222608`;
native r17d is unchanged. The offline suite passes 22 cases, including both
single-gate rejection directions.

Live validation under the current r48/r17d stack passed:

- the formerly failing composition, frame 7 -> 10 followed immediately by a
  new replayed-frame-10 checkpoint and frame 10 -> 42;
- a corrected four-cell lifecycle matrix with genuinely transitioning saved
  frames 51 and 54 (`S<-T` 45->51, `T<-T` 51->54, `T<-S` 54->86,
  `S<-S` 86->118);
- the genuine held-transition plate throw, frame 234 -> 266, with the exact
  historical input hash `40a90f5f...`; and
- a composed loose-plate frame-266 checkpoint through 300 neutral frames to
  568, followed by two additional same-checkpoint rewinds without reload.

Every run required exact input, restored baseline, entities, native round,
food, physics, clocks, contact-manager LIFO order, TransformChangeDispatch,
and Animator semantics. The two repeats performed zero Rigidbody rebuilds.
The primary receipts are the `summary.json` files in:

- `artifacts/framework-migration/story11-animator-r47-host-r25/composed-r48-step2-frame10-to42-r1/`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-matrix-transitioning-target-transitioning-frame51-to54-r1/`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-matrix-transitioning-target-settled-frame54-to86-r1/`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-held-transition-plate-throw-frame234-to266-r1/`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-composed-post-throw-neutral300-frame266-to568-r1/`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-composed-post-throw-neutral300-repeat2-r1/`.

The persistent red `AUTHORING_RESUME_PHASE_FAILED` screen text was stale
ResumePhase r1ax diagnostic state surviving a level reload, not a persistent
core invalid state. Loading fresh ResumePhase r1ay cleared the owner. A later
quality-of-life revision should clear scene-owned failure display on scene
incarnation change; this is separate from Animator rewind correctness.

## 2026-09-11: r43 boundary-only owner policy passes the matrix and plate run

The current Animator revision is
`framework-run/modules/ChefAnimatorCheckpoint-r43-replay-pre-owner-fence/ChefAnimatorCheckpoint.r43-replay-pre-owner-fence.dll`,
SHA-256
`40BC5B47983B18156C1A2B869A0F01735EE53BA3E3110B9DD66C2FA3ECFCD002`.
It retires the expensive continuous owner-graph observer while preserving
every restore input or runtime guard supported by evidence:

| Capture site | Capture owner graph | Role |
| --- | --- | --- |
| ordinary per-output `CaptureFrame` | false | managed/topology/mixer comparison remains, owner comparison omitted |
| stored `reference-prefix` | true | required resume snapshot and Stage A/B/final restore input |
| diagnostic `reference-post` | false | no restore consumer |
| `replay-pre` | true | empirically required pre-restore fence |
| `replay-post` | true | fail-closed final owner-state verification |
| `OverrideClipPlayables` before/after | true | fail-closed mutation admission guards |

r42 had also removed `replay-pre`. Its first T<-S resume then failed before any
payload was emitted: Player1 `ControllerInput` byte offset 12 was expected
`AA` but observed `00`, and the mixer's outer child differed. Restoring only
that capture produced r43. Inspection of the native capture export found graph
reads, hashing, and ProcessHeap temporary allocations but no proved Unity
write. The working interpretation is therefore an empirically necessary
timing/ordering/allocator fence, not a demonstrated state mutation. Do not
remove it again without a controlled replacement experiment.

### Representative transition matrix

Every matrix cell now passes under the r43 policy:

| Target <- current | Test span | Evidence |
| --- | --- | --- |
| T<-T | frame 7 -> 10, 1 payload + 2 release | `r43-matrix-transition-target-transitioning-r1/summary.json` |
| T<-S | frame 10 -> 42, 30 + 2 | `r43-matrix-transition-target-settled-r3/summary.json` |
| S<-T | frame 45 -> 51, 4 + 2 | `r43-matrix-settled-target-transitioning-r1/summary.json` |
| held S<-S | frame 228 -> 290, 60 + 2 | `r43-matrix-held-settled-target-settled-60-r3/summary.json` |

T<-S passed two additional rewind/replays in
`r43-matrix-transition-target-settled-repeat2-r1/summary.json`. The held S<-S
cell is the full 60-payload-frame plus two-release-frame run, not the earlier
short probe. All paths passed exact endpoint entities, native round/food/
physics/clocks, and Animator semantic checks; the held run additionally
verified its contact pool and TransformDispatch state.

This is representative-matrix proof only. It does not establish correctness
for every controller, layer, transition kind, Animator state, level, or
arbitrary future.

### Genuine plate throw, 300 + 2 continuation

The frame-234-to-266 dash/drop prefix is a genuine plate throw with recording
SHA-256
`40a90f5f013bfa3bbd9b052023547216cb21aece9738806ea047c9d4a3d59890`.
Starting from its frame-266 checkpoint, r43 replayed 300 neutral payload frames
and two controller release frames to frame 568. The continuation recording
SHA-256 is
`ed0a99f959188e70996ecc8c6f1152bba61b18e37831121c1a72b3bc48334aec`.
The endpoint was exact across serialized entities, native round, food,
physics, clocks, restored baseline, contact stack, TransformDispatch, and the
final Animator verification. Primary receipt:
`artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r43-plate-throw-neutral300-parity-r1/summary.json`.

Final r43 telemetry was **793,728 scene owner bytes** and **3,968,640 lifetime
owner bytes**, versus the continuous per-output capture used by r41. The
remaining captures are sparse restore/guard boundaries, so this reduction did
not relax any owner comparison that the current restore protocol consumes.

### Combined transitioning target through throw, repeated

A later regression widened the same proof into one non-adjacent continuation.
It checkpointed frame 234 while chef 44 held plate 2 in an active transition,
then replayed through the genuine dash/drop and neutral plate physics to frame
568. The 332 payload plus two release frames have recording SHA-256
`ce41e2c3f72ebfc5f158e1a5fb7a5fd875764a3876724cffc966331c5fc7e23c`.

The first rewind and two additional same-checkpoint rewinds all passed without
reloading. Each restored frame 234 exactly and reproduced frame 568 exactly for
entities, native round/food/physics/clocks, and recorded input. Raw native
physics, contact-manager free-stack order, and TransformChangeDispatch were
exact; Animator semantic parity passed on both repeats; actor rebuild count
remained zero. Final r43 status had no replay, random-state, ControllerInput,
transition-topology, mixer, or owner-graph difference. Evidence:

- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r43-r13w-mixed-frame234-to568-r1/summary.json`;
- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r43-r13w-mixed-frame234-to568-repeat2-r1/summary.json`;
- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r43-r13w-mixed-frame234-to568-repeat2-r1/final-module-status.json`.

After those repeats, scene owner capture was 1,234,688 bytes and lifetime
capture was 5,379,712 bytes, still far below the 256 MiB scene budget. The live
runtime is paused/fenced at frame 568. This is substantially stronger than the
four small lifecycle cells, but still does not cover station/workable state,
dynamic food replacement, every transition graph, or another level.

### Scene-owned native snapshot hardening

`RigidbodyActorRebuild-r13w-scene-owned-pool-reset` passed an armed-snapshot
reload test. Before reload, a frame-572 contact-pool and TransformDispatch
restore was armed in scene-metadata generation 60 against native context
`0x35DCFFA0`. Reload changed the generation to 61 and the observed context to
`0x3742F720`. r13w recorded one scene-owned reset, cleared both stale
snapshots, left `contactPoolRestores` and `transformDispatchRestores` at zero,
and the next step reached frame 3 cleanly. Receipts:

- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r13w-arm-abandoned-restore-r3.json`;
- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r13w-reload-with-abandoned-restore-r1.json`;
- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r13w-post-reload-status-r1.json`;
- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r13w-post-reload-step-r1.json`.

This hardens reload isolation; it is not additional evidence that arbitrary
same-scene rewind futures are correct. Search remains disabled.

### Multi-frame native sidecar history and non-adjacent rewind

The next mixed-order audit identified a separate history-ownership issue. The
core and Animator retained many frame checkpoints, but r13w/r6b retained only
one contact-manager free-list and one Transform-dispatch snapshot. This could
support repeated rewind to the latest checkpoint, not an older non-adjacent
target.

`RigidbodyActorRebuild-r13y-multiframe-sidecars-hardened` now stores a bounded
dictionary keyed by frame and exact core snapshot object. Native r12 exposes
caller-owned capture/restore buffers instead of relying on its old single
global slot. The actor module verifies core snapshot identity during prepare
and completion, mirrors core future-history pruning, and clears every sidecar
on scene generation, native context, or kitchen round identity changes. It
also preflights every saved Transform hierarchy before mutation. The native
restore checks unchanged unique free membership and exact array/count, then
does element-wise readback verification in addition to the receipt hash.

With frame-425 and frame-487 sidecars simultaneously resident, a direct
frame-789 -> 425 restore and held dash/drop replay to 487 passed twice. Between
the two passes, the abandoned frame-487 sidecar was proved absent, then was
re-captured from the replayed branch before rebuilding the same 789 future.
Both restored baselines and endpoints were exact for Animator semantics and
all broader entity/food/round/physics/clock/input comparisons; actor rebuilds
stayed zero. Evidence:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r13y-r12-r50-r17f-r12f-r24h-nonadjacent-frame789-to425-to487-r1/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r13y-r12-r50-r17f-r12f-r24h-nonadjacent-frame789-to425-to487-r2-rebranch/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/actor-r13y-checkpoint487-after-nonadjacent-r1.json`; and
- `artifacts/native-rigidbody-rebuild-r12-multiframe-sidecars-testbuild1/manifest.json`.

Managed r13y is SHA-256
`F2CF6F20F3C443FCB5FF554C43CDB862DC52758470F475D41F4D14C80EA79836`;
native r12 is SHA-256
`E28EA2D8EE26FD3D7E363334A6C9E90D7F9BCE0CD999CFD1F73D51A199FCA25E`.
This closes the known last-value sidecar limitation; it does not claim every
gameplay lifecycle is rewindable.

## Prior diagnostic baseline — complete r41 owner-graph replay, repeated

The genuine frame-266 dash/drop checkpoint now reproduces its entire
five-second post-throw future twice. Each run advances 300 neutral payload
frames and two controller release frames to frame 568. Animator r41 compared
all 302 replay output boundaries, rather than only the final resume tuple.
Every managed Animator semantic state, Unity random state, ControllerInput
blob, transition topology, mixer graph, and full native owner graph was exact;
all first-difference and failure fields remained null. The second rewind began
from the already replayed endpoint and produced the same result.

The prior r40p attempt stopped owner-graph observation after 54 replay frames,
but the simulation itself reached an exact endpoint. This was not a new
Animator mismatch. The 256 MiB safety counter charged every owner-graph byte
captured during the module's lifetime even after a scene change cleared the
only references to the old arrays. r41 adds a scene-owned byte counter, resets
it with the existing scene-owned histories, retains the lifetime counter as
telemetry, and enforces the unchanged 256 MiB bound against the scene counter.
No capture, comparison, or restoration bytes changed.

The complete reference and two replays used 104,188,672 scene bytes. Primary
receipts:

- `artifacts/framework-migration/story11-frame266-bounded-motion-host-r19/post-throw-neutral-original-r6-scene-budget.json`;
- `artifacts/framework-migration/story11-frame266-bounded-motion-host-r19/post-throw-neutral-replay-r6-scene-budget.json`;
- `artifacts/framework-migration/story11-frame266-bounded-motion-host-r19/post-throw-neutral-replay-r7-scene-budget-repeat.json`.

Managed revision:

- `framework-run/modules/ChefAnimatorCheckpoint-r41-scene-owner-budget/ChefAnimatorCheckpoint.r41-scene-owner-budget.dll`;
- SHA-256
  `F5EF2182FD7F203D72577FD84B4C07F36CD066E823A63E25CA8FC8B3133FC06F`.

This continuous owner observer is still diagnostic, not search-qualified. It
captures about 87.5 KiB per ordinary output frame across the four chefs. The
owner graph actually consumed by rewind is the stored `reference-prefix`
snapshot; Stage A/B/final restoration uses it for clip override, target-null
clip, playable time, and settled-transition operations. The fresh
`replay-post-restore` owner graph is also a required fail-closed final check,
and OverrideClipPlayables needs its live before/after owner captures as
mutation admission guards. In contrast, ordinary `CaptureFrame` owner blobs
and the reference-post/replay-pre owner blobs are continuous diagnostics only.
The next revision should omit only those diagnostic captures, preserve every
restore input and guard above, then rerun all four transition cells and this
long plate-throw case before measuring rewind speed. Search remains disabled.

## User intent and constraints

- Search must eventually use fast in-process rewind, not repeated level loads.
- First make rewind reproduce the same future under the same exact inputs.
- Preserve real Animator and held-item behavior. Community plate throwing drops
  a plate during a dash, so changing the chef attachment point or bypassing its
  animation could subtly change important physics.
- Local-only multiplayer simplifications are acceptable when local gameplay is
  demonstrably unchanged. `ServerWorldObjectSynchronizer` correctness for
  online multiplayer is not a goal, but it is not the current demonstrated
  Animator cause.
- The Story 1-1 sashimi tutorial dialogue may be skipped later to shorten fresh
  validation. It is separate from rewind parity.
- Keep the game windowed and paused/fenced between experiments. Use unique DLL
  revisions and evidence directories; do not overwrite loaded artifacts.

## What full rewind parity means

There are two different requirements:

1. **Boundary parity:** immediately after rewinding to checkpoint frame `F`, all
   captured state matches the original checkpoint.
2. **Continuation parity:** after resuming from `F`, the replay of identical
   inputs produces the same state on every future frame.

An exact restored snapshot is insufficient if hidden engine state causes the
first resumed update to choose a different future.

```text
checkpoint F
   +-- continue normally with exact inputs --> reference future
   `-- run ahead, rewind to F, same inputs  --> must reproduce that future
```

## Larger causal history

The original continuation failure was the conspicuous chef-height mismatch:
the reference branch rose through the normal `0 -> 0.05` contact staircase,
while the replay branch stayed at `0` (and other tests saw the reverse phase).

The investigation established:

- A Unity 2017.4.8f1 empty project and an isolated probe inside the shipped game
  reproduce the `0 -> 0.05` staircase itself, but are bit-exact across rewind.
  The staircase is normal behavior; the branch inconsistency is not.
- A broad managed mutation trace found no gameplay method directly producing
  the real-chef discrepancy. The changes occurred in the native physics
  interval.
- Native tracing exposed rewind-sensitive PhysX contact-manager allocation
  order. The framework now restores the contact-manager LIFO free stack and
  Unity TransformChangeDispatch pending order/masks.
- With those restorations, the short controlled routes no longer reproduce the
  `0` versus `0.05` endpoint mismatch. This is a controlled-route result, not a
  proof that all physics history is solved.
- Once physics was no longer the first observed mismatch, internal Animator
  continuation became the earliest known parity failure.

The Animator issue has **not** been proved to be the cause of the original
height drift. It is the next independently demonstrated continuation mismatch.
It still matters to physics because animation controls the chef skeleton and
held-item attachment transform. A different release transform can change a
dash/drop plate collision even when the chef root endpoint happens to match.

## Active Animator restoration

Managed module:

- `framework-run/modules/ChefAnimatorCheckpoint-r23/ChefAnimatorCheckpoint.r23.dll`
- SHA-256 `61149B42C3899AD835B852FB5729D25AA0F19B2C131A2E50C3F19E473FADC738`
- Name `chef-animator-checkpoint-v23-resume-ready-restore`
- Hot-load generation 43

Native helper:

- `artifacts/native-animator-checkpoint-r8-outer-inner-weights-build1/Oc2NativeAnimatorCheckpoint.r8.dll`
- SHA-256 `2B2DD73698F9DA38691A268122040E31ACC7F91FEB2ECC7532A10B4433E88481`
- API 8

At rewind, r23 retains the ordinary advancing checkpoint restoration. It also
requires a separate, unambiguous resume-ready reference tuple linked by object
identity to that exact checkpoint. At the final `Helpers.Resume` prefix, r23:

1. restores the complete serialized ControllerMemory using Unity's own
   `CopyBlob`/bump-allocator machinery;
2. recaptures it and requires byte-exact equality;
3. validates every known outer/inner mixer object, connection, mode, count and
   non-weight word;
4. transactionally restores only differing mixer weight scalars using Unity's
   revision-verified `SetInputWeight` implementation;
5. verifies the complete mixer graph byte-for-byte;
6. reapplies the captured evaluated descendant pose; then
7. recaptures ControllerMemory, ControllerInput, topology, mixer graph, public
   state, parameters/layers, and pose and requires them to match the original
   resume-prefix tuple exactly.

Pending state is cleared only in the resume postfix after the main pause is
actually removed. r23 does not write public Animator speed/enabled state or
call `Animator.SetFloat`, `Animator.Play`, or `Animator.Update`. The first live
r23 run restored two mixer weights and verified all four 372-byte
ControllerMemory blobs and all four 26-record mixer graphs exactly at the
frame-221 resume boundary.

Do not replace this with a raw 112-byte memcpy. Each StateMachineMemory contains
self-relative pointers and references allocation targets outside that block. A
partial copy could corrupt the layout and would omit layer 1 and adjacent
ControllerMemory arrays.

## ControllerMemory layout proved from Unity's PDB/disassembly

`controller+0xB4` points to the normalized ControllerMemory root. Unity resolves
each layer's StateMachineMemory through self-relative offsets:

```text
root    = *(controller + 0xB4)
slots   = (root + 4) + s32(*(root + 4))
slot_i  = slots + 4*i
sm_i    = *slot_i ? slot_i + s32(*slot_i) : null
```

The current 372-byte chef blob has layer 0 at blob `+0x24` and layer 1 at blob
`+0x98`. Unity RVA `0x649EEF` performs this lookup and passes the resolved block
as evaluator argument 4.

`StateMachineMemory::Transfer<BlobWrite>` at Unity RVA `0x6520A0` establishes:

- serialized blob offset `0x2C` (decimal 44) = layer-0
  `StateMachineMemory+0x08`, `m_CurrentStateIndex`;
- serialized blob offset `0x30` (decimal 48) = layer-0
  `StateMachineMemory+0x0C`, `m_NextStateIndex`.

At evaluator completion, Unity copies NextState into CurrentState and then
clears transition fields (`0x6645F8` through `0x664613`). Therefore an offset-48
NextState mismatch in one experiment and an offset-44 CurrentState mismatch in
a later experiment are compatible with one transition divergence observed at
different phases. State ordinals 14 and 16 are compiled indices; do not call
them named animation states until the corresponding constant table is mapped.

The exact 112-byte `StateMachineMemory` layout, recovered from
`StateMachineMemory::Transfer<BlobWrite>` field-name literals, is:

```text
00 MotionSetCount                 04 MotionSetAutoWeightArray
08 CurrentStateIndex             0C NextStateIndex
10 ExitStateIndex                14 InterruptedStateIndex
18 TransitionIndex               1C TransitionSourceStateIndex
20 TransitionType                24 CurrentStatePreviousTime
28 NextStatePreviousTime         2C InterruptedStatePreviousTime
30 ExitStatePreviousTime         34 CurrentStateDuration
38 NextStateDuration             3C NextStateBaseDuration
40 ExitStateDuration             44 InterruptedStateDuration
48 CurrentStateSpeedModifier     4C NextStateSpeedModifier
50 ExitStateSpeedModifier        54 InterruptedStateSpeedModifier
58 TransitionStartTime           5C TransitionTime
60 TransitionDuration            64 TransitionOffset
68 InInterruptedTransition       69 InTransition
6A InDynamicTransition           6B ActiveGotoState
6C FixedTransition               6D CleanAfterTransition
6E ResetPlayableGraph            6F padding
```

## Raw-state trace added on 2026-09-09

Native trace:

- `artifacts/native-physics-trace-r17-animator-raw-state-build1/Oc2NativePhysicsTrace.r17.dll`
- SHA-256 `B63760346A22B9970C0026F1B3A9765BE3B9D3D86BA2DA637524C28D48A1FBEE`

Managed decoder:

- `framework-run/modules/NativePhysicsTrace-r8q/NativePhysicsTrace.r8q.dll`
- SHA-256 `600BC6D12601815943DBC957BCF72760D990C6C9CB9DAEF3AFF78D1D5F0EC33A`
- Name `native-physics-trace-v12-animator-raw-state`

The native harness passes `/W4 /WX`. The extension preserves the existing
152-byte event layout and API 1. It adds gated, read-only child records:

- kind 65: all 28 words of the evaluator's 112-byte StateMachineMemory;
- kind 66: all five words of the 20-byte StateMachineOutput, including bytes
  `+0x10` through `+0x13`;
- kind 63 now samples the actual inner mixer child's `+0xA5` flag rather than
  the outer object.

The hooks are pass-through observers. They change no argument, return value,
Animator data, physics data, or game data.

## Latest controlled A/B

Evidence directory:

`artifacts/framework-migration/story11-animator-r21-raw-state-dash-r1`

The four-chef eight-frame dash plus two release frames ran wholly through
in-process rewind in about 18 seconds:

- checkpoint/start frame 71;
- original endpoint frame 81;
- replay endpoint frame 81;
- identical recorded inputs;
- exact reconstructed entities and food;
- exact round state and private clocks;
- exact native physics endpoint;
- exact contact-manager free-stack restoration;
- exact TransformChangeDispatch restoration;
- no trace loss: original 20,027 events, replay 22,334 events, dropped count 0.

Important files:

- `summary.json`
- `animator-status-after.json`
- `native-trace-original.json`
- `native-trace-replay.json`
- `recording.json`

The endpoint success is not full continuation parity. The first resumed Animator
evaluation still differs.

## Strongest current finding

All four chef controllers show the same signature.

The resume fence is native marker `0xA50`, emitted by the trace module only
after `Helpers.Resume` has removed the main authoring pause. Both trace files
contain this marker. Compare the first layer-0 transaction strictly after that
marker on each branch; do not pair a paused pre-fence original evaluation with
a resumed replay evaluation.

At the correctly paired first resumed layer-0 `EvaluateStateMachine` entry:

- the trace-captured 36-byte input **prefix** is exact, including
  `StateMachineInput+0x08 = 1.0` on both branches;
- CurrentState and NextState are exact;
- outer mixer topology and weights are exact;
- true and false inner mixer topology, weights and child `+0xA5` flags are
  exact;
- the **only** StateMachineMemory difference is the word at `+0x34`, which its
  BlobWrite field-name string proves is `m_CurrentStateDuration`:
  - reference word `0x7F800000` = positive infinity;
  - replay word `0x3FA00000` = `1.25f`.

Do **not** treat that entry value as the value later consumed unchanged.
Contextual control-flow reconstruction proves that every non-empty evaluation
unconditionally recomputes `m_CurrentStateDuration` first:

- normal path: `EvaluateState(current=true)` writes it at RVA `0x6638F1`;
- interrupted path: `EvaluateInterruptedStateDuration` writes it at
  `0x663672` or `0x663684`.

Only afterward does RVA `0x664358` load it and `0x664395` use it in normalized
transition progress. Therefore the entry `+Inf` versus `1.25` is a strong
history/upstream-state signature, but it is **not yet proved causal**. The next
trace must capture the recomputed value and its inputs before transition
selection.

`EvaluateState` derives the value as motion/blend-tree duration divided by the
absolute effective state speed, or `+Inf` when effective speed is zero.
`StateMachineInput+0x08` is the controller's copy of public `Animator.speed`:
`Animator::Prepare` copies `Animator+0x1BC` to ControllerInput `+0x00`, and
`UpdateGraph` copies that into its local StateMachineInput `+0x08`. The correct
post-fence pair has `1.0` in both branches. Paused pre-fence transactions have
`0.0`; pairing one of those with a resumed transaction falsely makes
`Animator.speed` look like the rewind difference.

StateMachineOutput also has a scalar difference at word `+0x04`, identically for
all four chefs:

- evaluator entry: reference `0x00000080`, replay `0x0000001C`;
- evaluator exit: reference `0x00000080`, replay `0x00000019`.

This word is not a pointer. Its semantic name is not yet proved. The
pointer-shaped live-descriptor word is Output `+0x0C`, and that pointer is exact
and stable per controller. Output bytes `+0x10` through `+0x13` are zero and
exact in both branches.

Contextual disassembly also shows that entry Output `+0x04` is stale output,
not an evaluator input: for the non-empty state machine, RVA `0x663C8F` writes
`0x80` to Output `+0x04` before reading it. The function similarly initializes
Output `+0x00` and `+0x08` at `0x663CA9` and `0x663CAF`. Therefore the entry
`0x80` versus `0x1C` difference cannot drive this evaluation. Replay's exit
value `0x19` is instead another result of the divergent evaluation.

At evaluator exit:

- the first StateMachineMemory difference is NextState at `+0x0C`;
- reference NextState = 0;
- replay NextState = 16;
- the parent evaluator event independently reports destination 0 versus 16.

The exported chef controller and its hashes identify this as the
`NewChef_Idle -> NewChef_Walk` transition. The authored condition is
`Speed > 0.1`, with duration `0.1`, no exit time, and non-fixed duration.
Ordinal 0 is therefore `NewChef_Idle` and ordinal 16 is `NewChef_Walk` for this
exact controller. This naming is now proved for these two ordinals only.

The surrounding transition code is now mapped far enough to narrow the next
question. `EvaluateTransitions` reaches the condition helper at RVA `0x663430`.
Its caller passes the runtime-parameter block stored at
`StateMachineInput+0x0C`. Within that separate block, the mode-3 float branch at
`0x663506` through `0x66352F` resolves the value through a self-relative array
anchored at parameter-block `+0x1C` and evaluates it against the authored
threshold. Other branches similarly use integer and bool arrays anchored at
parameter-block `+0x24` and `+0x2C`.

This corrects an earlier overclaim: the current trace captures and hashes only
the first `0x24` bytes (`+0x00..+0x23`) of the UpdateGraph input object. It
proves that prefix, including the `+0x0C` parameter-block pointer, is equal, but
**does not capture the pointed-to block or its resolved float array element**.
UpdateGraph itself constructs fields through at least `+0x2C`, so calling the
36-byte prefix the complete input structure was wrong. The runtime `Speed`
float is therefore not yet compared at the decision point.
The later load of StateMachineMemory `+0x34` occurs only after a transition has
already matched and been accepted, so the stale entry duration cannot choose
Idle versus Walk. The strongest unresolved candidate is a transient parameter-
array or upstream parameter-update difference, with motion/playable state still
an alternative until the next trace observes both paths directly.

After this, managed frame observation sees the replay false-inner mixer input 0
weight become `1.0` while the reference remains `0.0`. Because the mixer was
exact on evaluator entry, that mixer mismatch is an **output of the divergent
evaluation**, not its initial cause.

The resulting causal chain is currently:

```text
frame-71 final rewind boundary
  whole ControllerMemory exact
  mixer graph exact
  skeleton pose restored
          |
resume/pause scheduling and first resumed UpdateGraph
          |
evaluator entry: only SM+0x34 differs (+Inf reference / 1.25 replay)
captured StateMachineInput prefix is exact; Animator.speed is 1.0 in both
          |
EvaluateState recomputes duration from state/motion graph inputs
post-recompute parity is not captured yet
          |
transition helper follows input+0x0C to the live parameter block
then resolves Speed through that block's +0x1C float array
reference rejects Speed>0.1; replay accepts it
          |
replay mixer weight changes; NextState may later commit into CurrentState
```

Static disassembly proves `AnimatorController::UpdateGraph` does not write
StateMachineMemory `+0x34` between its entry and the evaluator call. Because
the whole blob was exact immediately before resume, the stale duration change
must occur before that UpdateGraph entry. It may merely record a missing
evaluation-history phase; the direct transition cause can still live in the
motion/blend-tree evaluation, parameter pointees, playable state, or another
workspace not represented by the exact captured input prefix.

The r21 status also records a deliberate pause-state difference at the final
resume boundary: saved public Animator speed is `1`, while the paused live
speed and ControllerInput prefix are `0`. `ObserveControllerInput` reports
first byte difference 2 for that reason. Resume changes it back to `1`, and the
first post-fence evaluator input is exact. Do not mistake this expected pause
mechanism for the continuation bug.

## Claims that must not be made yet

- Do not claim the Animator mismatch caused the original `0` versus `0.05`
  physics drift.
- Do not claim full rewind parity from the latest exact ten-frame endpoint.
- Do not generalize ordinal names beyond the exact chef controller. Ordinal 0
  and 16 are now mapped to `NewChef_Idle` and `NewChef_Walk`; ordinal 14 remains
  unnamed.
- Do not claim the entry duration difference directly drives the transition;
  it is recomputed before use and post-recompute parity is not captured yet.
- Do not treat the stale StateMachineOutput `+0x04` entry value as causal; the
  evaluator overwrites it before use. Its exit value remains useful as a result
  discriminator.
- Do not patch the evaluator, pin the held-item attachment, disable Animator
  influence, or raw-copy one StateMachineMemory block.
- Do not begin route search while the first resumed Animator frame differs.

## Next direction: reverse the surrounding state flow first

Prefer a contextual reverse-engineering pass over chasing another isolated byte:

1. Capture the layer-0 StateMachineMemory summary at UpdateGraph entry to close
   the final-restore-to-UpdateGraph interval. Static disassembly already proves
   UpdateGraph itself has no pre-evaluator `+0x34` writer.
2. Extend the input observation beyond the misleading 36-byte prefix. Follow
   `StateMachineInput+0x0C`, resolve that parameter block's self-relative float
   array at `+0x1C`, identify the `Speed` element index, and capture its raw bits
   at the condition helper.
3. Capture immediately after `EvaluateState` / interrupted-duration evaluation,
   including returned motion duration, computed state speed, parameter source,
   and the resulting `+0x34`. This separates the transition-parameter problem
   from the independent duration-history signature.
4. Map the meaning of the StateMachineOutput result codes only as needed. The
   entry `+0x04` value is already excluded as a cause because it is overwritten;
   keep it distinct from the exact descriptor pointer at Output `+0x0C`.
5. Decide from evidence whether the missing checkpoint state lives in the
   controller, output/workspace, graph scheduler, or another retained object.
6. Restore the owning source through its native ownership/copy mechanism. Do
   not write a derived symptom field if the source can be restored safely.
7. Rerun the same short unwind-only A/B. Require no first ControllerMemory or
   mixer-graph replay difference and raw entry/exit parity.
8. Then test a held-plate and dash/drop continuation, followed by longer and
   repeated rewind cycles. Only after those pass should fast rewind search
   resume.

## Contextual reverse result and next native observation — 2026-09-10

The contextual reverse is now the primary guide, rather than a byte-by-byte
search. It established the complete owner path for the transition parameter:

```text
ControllerMemory+0x10 (m_Values)
  -> StateMachineInput+0x0C (runtime ValueArray pointer)
  -> ValueArray+0x1C (self-relative float-array base)
  -> float[descriptor.typedArrayIndex]
  -> EvaluateCondition mode 3: value > authored threshold
```

The runtime `ValueArray` layout is:

```text
+00 position count    +04 relative position array
+08 quaternion count  +0C relative quaternion array
+10 scale count       +14 relative scale array
+18 float count       +1C relative float array
+20 int count         +24 relative int array
+28 bool count        +2C relative bool array
```

`FindValueIndex` at Unity RVA `0x660610` scans 12-byte descriptors
`{hash,type,typedArrayIndex}`. `EvaluateCondition` at RVA `0x663430` receives
conditions `{mode,parameterHash,threshold}`. Its float comparison at RVA
`0x663524` has already resolved the exact element: `XMM1` is the live value and
the condition's `+0x08` word is the threshold. For the demonstrated transition,
this is the decisive `Speed > 0.1` comparison.

`PlayerAnimationDecisions.UpdateVariables` writes the same slot through
`Animator.SetFloat(Speed, m_controls.GetMovementSpeed())`. Unity's native setter
at RVA `0x6461A0` resolves the same descriptor and writes the `ValueArray`
float. `AnimatorController::UpdateGraph` constructs its input from
`ControllerMemory.m_Values`; it does not itself write the float before
`EvaluateStateMachine`. The serialized ControllerMemory restore therefore
contains this array, but an earlier managed setter or scheduling phase can
still overwrite it between the exact rewind boundary and the first native
evaluation.

The new passive comparison-point trace is:

- native:
  `artifacts/native-physics-trace-r19-condition-float-build1/Oc2NativePhysicsTrace.r19.dll`;
  SHA-256
  `21F318B53D977DF7E29DD40C36D3734468AE096910C6D011B5F8AE13226C04B7`;
- managed decoder:
  `framework-run/modules/NativePhysicsTrace-r8u/NativePhysicsTrace.r8u.dll`;
  SHA-256
  `CD19E23C7A09F3351E1BB5F39EED10DDC6F1DFF70C731BAEB6509EDFAB11DAFF`;
- module name `native-physics-trace-v14-animator-condition-float`;
- new event kind 67, `animator-condition-float`.

The hook is at RVA `0x663524` and preserves all GPRs, flags, and XMM0-XMM7
around its observer. It records the condition and descriptor, resolved float
address, memory bits, `XMM1` value bits, parent evaluator/UpdateGraph sequence,
input and `ValueArray` pointers, ControllerInput speed, current/next state,
duration, and StateMachineInput `+0x08`. That last scalar is Animator playback
speed copied into the evaluator; it is not the movement parameter also named
`Speed`. Its x86 `/W4 /WX` harness passes with five state-machine hooks, mask
256, and no native error.

One fresh Story 1-1 setup activated r19 successfully. The first bounded A/B did
not reach warmup frame 2: the host rejected its resume with
`AUTHORING_RESUME_PHASE_FAILED: Resume has no exact valid paused RPC origin.`
This produced no Animator comparison and advanced no gameplay. It is a separate
controller-envelope identity problem, not evidence for or against the Speed
hypothesis. Evidence:

- `artifacts/framework-migration/story11-animator-r21-condition-float-dash-r1/summary.json`;
- `artifacts/framework-migration/story11-animator-native-host-r7/resume-phase-status-after-r1-abort.json`.

The failed resume module was `ResumePhase-r1d`, generation 3, SHA-256
`DC0826F10B9E2F7DEAF2C761CB7C050355777E72E5AE36768AD8A9F587EE6F82`.
It has 42 observed resumes, 41 allowed resumes, no pending origin, and a sticky
failure after this one rejected request. Its manifest source hash exactly
matched the r1d source used at build time; the source has since advanced to
r1e. The accepted
native exchange itself was plain and paused (`RequestResume=true`, phase 0), so
the recovery replaced r1d with uniquely built r1e and followed exact reply
provenance through `InjectorServer.Accept`; it did not relax resume alignment.
The existing host then recovered through one controlled fresh level load.

That control-boundary repair and the planned A/B have now completed. The
following result supersedes the proposed comparison above.

## Decisive runtime-parameter result — 2026-09-10

Evidence directory:

`artifacts/framework-migration/story11-animator-r21-condition-float-dash-r2`

This run used the same bounded four-chef route: checkpoint/start frame 31,
eight dash frames, two release frames, and endpoint frame 41. It completed in
about 23 seconds through in-process rewind. The endpoint again has exact
recorded inputs, reconstructed entities, food, round state, private clocks,
native physics, contact-manager free stack, and TransformChangeDispatch state.
There was no trace loss: original 20,983 events, replay 23,124 events, dropped
count zero.

Use the last post-arm resume fence in each split trace:

- original `0xA50` marker sequence 4787;
- replay `0xA50` marker sequence 6163.

At the first layer-0 condition evaluation after those fences, all four chef
controllers test the same parameter and descriptor:

```text
condition mode       3
parameter hash       0xCEE7D1F2  (Speed)
threshold            0x3DCCCCCD  (0.1f)
descriptor type      1           (float)
typed array index    0
ValueArray floats    5
float-array relative +0x14
```

The decisive value differs identically for all four chefs:

```text
reference: ValueArray Speed = 0x00000000 = 0.0f
replay:    ValueArray Speed = 0x3F800000 = 1.0f
```

`XMM1`, the actual comparison operand, exactly matches the resolved memory word
on each branch. This is therefore not a decoder, hash, stale-register, or
pointer-indirection mistake. The descriptor, resolved address, float count,
threshold, entry CurrentState/NextState, and StateMachineMemory `+0x34` duration
all match. ControllerInput playback speed and StateMachineInput `+0x08` are
also `1.0` on both branches; those are distinct from the divergent movement
parameter named `Speed`.

The kind-67 observation occurs before the state divergence. Exact controller
mapping and first evaluator transactions are:

| Chef | Controller | Reference entry / condition / exit | Replay entry / condition / exit | Entry state | Reference exit | Replay exit |
|---|---|---|---|---|---|---|
| P1 | `0x45FF6CC0` | 5157 / 5177 / 5179 | 6605 / 6628 / 6630 | 8 / 8 | 8 / 8 | 0 / 0 |
| P2 | `0x3F5DC480` | 5171 / 5212 / 5218 | 6626 / 6666 / 6673 | 15 / 15 | 15 / 15 | 0 / 0 |
| P3 | `0x3F5953A0` | 5195 / 5239 / 5247 | 6652 / 6703 / 6706 | 15 / 15 | 15 / 15 | 0 / 0 |
| P4 | `0x3F521300` | 5210 / 5242 / 5245 | 6660 / 6709 / 6716 | 14 / 14 | 14 / 14 | 0 / 0 |

Thus the missing continuation parity is no longer merely “some Animator
history.” A specific live controller parameter is wrong before transition
selection and is sufficient to explain this first state/mixer divergence. It
does not yet prove why the parameter changed. The serialized ControllerMemory,
which owns the `ValueArray`, was byte-exact at the final restore. Therefore
something writes movement `Speed=1` on the replay between that exact boundary
and the first native comparison, while the reference still sees `0`.

The two strongest upstream explanations are now:

1. `PlayerAnimationDecisions.UpdateVariables` / `Animator.SetFloat` executes at
   a different relative point around the first resumed evaluation; or
2. it executes on both branches but its source
   `m_controls.GetMovementSpeed()` returns different cached control/motion
   state, which the current rewind checkpoint does not restore.

Contextually reverse `GetMovementSpeed` and trace the exact native SetFloat
write before changing restoration. A fix should restore the owning cached
state or preserve the original update ordering, not overwrite the derived
Animator parameter at every frame.

The managed decoder with an explicit named kind-67 object is built but not
hot-loaded because the existing raw evidence is complete:

- `framework-run/modules/NativePhysicsTrace-r8w/NativePhysicsTrace.r8w.dll`;
- SHA-256
  `974EB0AF1FEEB6BDB1EF6EC783CADCE73BE2722E214FCD4A327085BC8D9D423F`;
- module name `native-physics-trace-v15-animator-condition-float-decoded`.

Its field name is `stateMachineInputSpeed`, not `animatorSpeed`, specifically
to prevent confusing playback speed with the movement parameter.

## Paused runtime at this snapshot

- Game PID 59016 is alive, windowed, and paused in Story 1-1 at replay endpoint
  frame 41.
- Headless host PID 72152 is alive, connected, settled `Paused`, and has a
  complete frame-31-to-41 original/replay recording.
- ResumePhase r1e is active as generation 38, SHA-256
  `81E4BC29FC034C37073EB31A91AB11B3989691D2DFF559A557669CE1C87D422C`.
  It follows exact reply provenance through `InjectorServer.Accept`, passed 46
  offline source/IL checks including input-object substitution, and completed
  the native r2 run without a resume failure. Post-run status proves three
  published resumes, three exact accept mappings, three allowed resumes, zero
  unmatched accepts, zero pending origins, and no failure; see
  `story11-animator-r21-condition-float-dash-r2/resume-phase-status-after-r1.json`.
- Native r19 is installed through managed r8u with Unity base `0x61190000`, mask
  256, five hooks, and no native hook error. Probe marker 270 ended Animator
  capture after replay; the successful split evidence remains retained.
- No route search or level reload loop was run during this investigation.

## Managed owner identified and first repair result — 2026-09-10

Contextual managed-code reversal found the owner of the transient movement
parameter. This supersedes the earlier plan to keep descending through native
Animator internals before inspecting the caller.

The exact managed path is:

```text
PlayerAnimationDecisions.Update
  -> UpdateVariables
  -> Animator.SetFloat(hash("Speed"), m_controls.GetMovementSpeed())
  -> Mathf.Clamp01(PlayerControls.m_xzSpeed / m_movement.RunSpeed)
```

Relevant installed method tokens and IL:

- `PlayerAnimationDecisions.Update` token `0x06003212` calls
  `UpdateVariables` at `IL_000C`.
- `UpdateVariables` token `0x06003225` loads `m_controls`, calls
  `PlayerControls.GetMovementSpeed` at `IL_0033`, and calls
  `Animator.SetFloat(int,float)` at `IL_0038`.
- `PlayerAnimationDecisions..cctor` token `0x06003228` hashes the literal
  `Speed` at `IL_004B..IL_0055`; the result is `0xCEE7D1F2`, exactly the hash
  observed by native condition event 67.
- `PlayerControls.GetMovementSpeed` token `0x0600327A` is exactly
  `Mathf.Clamp01(GetUnclampedMovementSpeed())`.
- `GetUnclampedMovementSpeed` token `0x06003279` is exactly
  `m_xzSpeed / m_movement.RunSpeed`. Story 1-1 serializes `RunSpeed = 6` for
  all four chefs.

`PlayerControls.FixedUpdate` token `0x06003290` owns the cache. It:

1. gets the layer-specific fixed delta;
2. returns immediately when that delta is at most zero;
3. handles a parent-coordinate-basis change;
4. computes
   `(Transform.localPosition - m_previousPosition) / fixedDelta` into
   `m_localVelocity`;
5. replaces `m_previousPosition` with the current local position; and
6. writes the XZ magnitude to `m_xzSpeed`.

The only other `m_xzSpeed` writer in Assembly-CSharp is `OnDisable`, which
writes zero. `Start` and `OnEnable` seed the previous-position/parent caches.
During the authoring main pause, `TimeManager` makes the affected fixed delta
zero, so `FixedUpdate` returns without refreshing the caches.

The ordinary rewind path restored the chef Rigidbody, Transform, and selected
`ClientPlayerControlsImpl_Default` fields, but none of these owning
`PlayerControls` fields:

```text
m_previousParent
m_previousPosition
m_localVelocity
m_xzSpeed
```

The existing cannon-flight checkpoint happens to include them, but only for a
single chef already in steady cannon flight. The controlled Story 1-1 runs had
no cannon passenger. The Animator checkpoint restores the derived ValueArray
slot but not these upstream managed caches.

This explains the complete one-tick signature in the prior native trace. After
the eight-frame run-ahead, rewind put each chef Transform back at the checkpoint
while leaving `m_previousPosition` at the future location. The next fixed tick
therefore measured an artificial displacement at about run speed, producing
`m_xzSpeed ~= 6`, so `GetMovementSpeed()` returned `1`. That value overwrote the
correctly restored Animator `Speed = 0`. The fixed tick simultaneously replaced
`m_previousPosition`, so the following condition batch returned to zero.

An ownership-level authoring checkpoint was added:

- source:
  `framework/modules/chef-movement-history-checkpoint/ChefMovementHistoryCheckpointModule.cs`;
- first tested DLL:
  `framework-run/modules/ChefMovementHistoryCheckpoint-r2/ChefMovementHistoryCheckpoint.r2.dll`;
- tested SHA-256:
  `1D726D489C31FCFB9CF098D5A12CAD1CA03ED5BC8D27B51ADFE6653B2422BAE1`;
- name: `chef-movement-history-checkpoint-v1`.

The module is read-only during ordinary forward play. It captures all four
fields for exactly four local `PlayerControls` components and validates the
component, cached Transform, movement-config object, and `RunSpeed` identities.
It writes only during an exact native authoring rewind, after the completed root
Transform/body restoration, and reapplies/verifies the same state at the final
resume fence. It never sets an Animator parameter, Transform, Rigidbody,
gameplay input, clock, or physics state.

First repaired evidence:

`artifacts/framework-migration/story11-animator-r21-movement-history-r2-dash-r1`

This was another wholly in-process four-chef eight-dash-frame plus two-release-
frame A/B, from frame 71 to frame 81. It passed in about 24 seconds. The restore
receipt directly captured the missing state before repair:

- all four future `m_previousPosition` values differed from their checkpoint
  values;
- all four future `m_localVelocity` values had XZ magnitude approximately 6;
- all four future `m_xzSpeed` values were approximately 6;
- all four checkpoint values restored `m_localVelocity = 0` and
  `m_xzSpeed = 0`, with `RunSpeed = 6` and unchanged parent identity.

At the final resume fence, recapture was byte/value exact for all four cache
owners. Across all ten replay frames, r21 then reported:

```text
firstReplayDifference                   null
firstControllerInputReplayDifference    null
firstTransitionTopologyReplayDifference null
firstMixerGraphReplayDifference         null
```

The endpoint also retained exact input, entities, food, native clocks, native
physics, contact-manager free-stack order, and TransformChangeDispatch state.
Independent raw native-trace comparison then confirmed the repair at the actual
decision point. The correct last `0xA50` resume fences are original sequence
26225 and replay sequence 4647. For all four chefs, the first layer-0 kind-67
event now has ValueArray memory bits and `XMM1` equal to `0x00000000` on both
branches. The condition hash, mode, threshold, descriptor, controller,
ValueArray address, ControllerInput playback speed, and StateMachineInput
playback speed also match. The evaluator exits match completely: raw 28-word
StateMachineMemory, five-word StateMachineOutput, hashes, Current/Next state
`0/0`, and mixer observations.

This is strong causal evidence that the omitted PlayerControls history was the
source of the demonstrated first-frame Animator behavior mismatch.

One earlier trace-only difference remains open. At the first UpdateGraph and
layer-0 evaluator entry, all four reference branches have
`StateMachineMemory+0x34 = 0x7F800000` (`+Inf`) while all four replay branches
have `0x3FA00000` (`1.25`). Every other raw entry word matches. The evaluator
unconditionally recomputes that duration to `1.25` before kind 67 and before
any consumer, after which the complete paired transaction is exact. Thus it is
a pause/evaluation-history sentinel and is proved non-causal for this
transaction, but it remains a raw internal-entry parity difference. Do not hide
it behind the successful outputs. Broader held-item, dash/drop, longer, and
repeated-rewind tests also remain required before claiming full rewind parity.

An optimized follow-up build avoids four-chef discovery on repeated paused
captures by first checking the exact native snapshot identity:

- `framework-run/modules/ChefMovementHistoryCheckpoint-r3/ChefMovementHistoryCheckpoint.r3.dll`;
- SHA-256
  `199327E0CA511651BF06B25788458A4A378959FF35B8AE38CA653203BCEF8165`.

It was hot-loaded as generation 40 and passed the same bounded A/B from frame
111 to frame 121 without the large native trace export. Evidence:

`artifacts/framework-migration/story11-movement-history-r3-dash-r1`

The probe completed in about 7.3 seconds. Its entity/food/clock/native-physics,
contact-pool, and TransformChangeDispatch comparisons were exact. The r3 module
reported one verified restore, one final-resume restore, ten exact replay
comparisons, no first replay difference, and no failure. Animator r21 again
reported null for all four first-difference categories. Native r2 evidence above
already verifies the decision operand and complete evaluator exit; this r3 run
verifies that the duplicate-capture optimization preserves the tested behavior.

The live game remains windowed and paused at repaired replay endpoint frame 121
with r3 active. The headless controller and ResumePhase r1e are healthy. Do not
run route search yet.

## Exact cause of the remaining raw `+0x34` entry mismatch — 2026-09-10

The remaining `CurrentStateDuration` entry mismatch is no longer unexplained.
Native r19 trace chronology on both branches proves that the Animator naturally
reaches the paused resume boundary with layer-0 `StateMachineMemory+0x34 = +Inf`
and evaluator-input playback speed zero. Public `Animator.speed` remains `1`;
the zero is the pause-adjusted ControllerInput/StateMachineInput value. This is
the engine's ordinary paused state, not a rewind-specific corruption.

For all four chefs, `+0x34` is already `+Inf` at the first sampled paused
layer-0 evaluation and stays `+Inf` through the last evaluation before the
final `0xA50` resume fence. Those evaluations use input playback speed zero on
both branches. Exact last evaluator-entry/raw-state to evaluator-exit/raw-state
sequences are:

| Chef | Controller | Reference entry/raw -> exit/raw | Replay entry/raw -> exit/raw |
|---|---|---|---|
| P1 | `0x45FF6CC0` | `25877/25884 -> 25900/25903` | `4302/4305 -> 4320/4322` |
| P2 | `0x3F5DC480` | `25893/25901 -> 25957/25966` | `4301/4308 -> 4341/4347` |
| P3 | `0x3F5953A0` | `25887/25890 -> 25925/25930` | `4343/4345 -> 4377/4385` |
| P4 | `0x3F521300` | `25923/25931 -> 25970/25978` | `4361/4367 -> 4402/4412` |

The final resume fences are reference sequence `26225` and replay sequence
`4647`. At the first post-fence layer-0 evaluations, reference enters with
`+Inf` and exits with `1.25`, while replay enters and exits with `1.25`. Exact
UpdateGraph/evaluator-entry/raw-state to evaluator-exit/raw-state sequences are:

| Chef | Reference UpdateGraph / entry / raw -> exit / raw | Replay UpdateGraph / entry / raw -> exit / raw |
|---|---|---|
| P1 | `26654 / 26657 / 26659 -> 26670 / 26671` | `5076 / 5078 / 5083 -> 5103 / 5104` |
| P2 | `26688 / 26689 / 26695 -> 26714 / 26719` | `5117 / 5125 / 5133 -> 5183 / 5191` |
| P3 | `26708 / 26711 / 26715 -> 26749 / 26756` | `5121 / 5126 / 5131 -> 5170 / 5174` |
| P4 | `26717 / 26721 / 26726 -> 26768 / 26777` | `5130 / 5134 / 5142 -> 5186 / 5198` |

The branch difference is introduced afterward by the checkpoint module itself:

```text
reference branch
  ordinary paused Animator evaluations -> natural +Inf sentinel
  no pending rewind restore at final resume
  first resumed UpdateGraph enters with +Inf

replay branch
  ordinary paused Animator evaluations -> natural +Inf sentinel
  r21 final-resume hook restores the advancing-frame checkpoint blob
  that blob contains CurrentStateDuration = 1.25
  first resumed UpdateGraph therefore enters with 1.25
```

The evaluator immediately and unconditionally recomputes the field to `1.25`
on both branches before the condition evaluator or any consumer observes it.
The repaired movement `Speed` value is then zero on both branches and the full
evaluator exit transaction is exact. Consequently:

- the `+Inf` versus `1.25` entry difference is non-causal in the demonstrated
  transaction;
- it is nevertheless a genuine raw parity defect;
- r21's final restore is the direct source of that defect; and
- directly patching `+0x34` would restore a symptom rather than the engine-owned
  resume phase.

There is no dedicated native marker inside the r21 restore hook, so this is a
bracketed causal proof rather than an instruction-level hook trace: replay is
`+Inf` at the last paused evaluator exit; r21's status records a successful
`before-authoring-resume` full ControllerMemory restore for frame 71; and the
first post-fence replay entry is `1.25`. The reference branch has no pending
restore and remains `+Inf`. No other observed phase lies in that interval.

The safest current design is a **resume-ready Animator snapshot**. Keep the
existing advancing-frame checkpoint for rewind history, but also capture the
reference branch's complete, naturally paused Animator state at
`Helpers.Resume`, immediately before unpausing. On replay, the final resume hook
would restore that complete resume-ready ControllerMemory, mixer graph, and
pose instead of reapplying the older advancing-frame snapshot. Preparation
must fail closed if the exact target frame has no unambiguous resume-ready
template.

A contextual review of r21's lifecycle and native ownership supports that
design but recommends a two-revision rollout. First build an observer-only r22:
capture the complete tuple at the actual `Helpers.Resume` prefix, before r21's
own restore, on reference and replay. This proves whether reference already has
`+Inf` and replay has `1.25` at precisely that managed boundary; the current
native evidence brackets the write but has no marker inside the hook. The
observer must not block ordinary reference gameplay if capture fails; it marks
that frame unusable for a later rewind instead.

Only after that observation succeeds should a restore-enabled revision keep a
separate resume-ready history linked by identity to each advancing snapshot.
It should restore ControllerMemory, mixer graph, and pose as one coherent tuple;
validate public configuration, ControllerInput, and transition topology without
force-writing pause-owned public speed/enabled state; prune both histories
together; reject ambiguous duplicate templates; and clear a pending restore
only in a resume postfix after confirming the pause was actually removed. The
Animator prefix should run last among resume prefixes, while the existing
TransformChangeDispatch repair remains paired with the later
`TimeManager.SetPaused(false)` postfix. Do not write only the derived duration
field, call `Animator.Update`, call `Animator.Play`, or change attachment
transforms.

After each uniquely named build, rerun the same short native-traced unwind A/B.
The restore-enabled revision must require all of the following:

- first resumed evaluator entry has `+Inf` on both branches;
- kind-67 movement `Speed` remains `0` on both branches for all four chefs;
- complete evaluator entry/exit, ControllerMemory, transition topology, mixer
  graph, and pose comparisons are exact;
- entity, food, clocks, native physics, contact-manager pool, and
  TransformChangeDispatch comparisons remain exact; and
- no module failure or trace loss occurs.

Only after that should validation expand to held-plate dash/drop, longer runs,
and repeated rewinds. Route search remains out of scope until those continuation
tests establish full parity.

## Resume-prefix observer proof — 2026-09-10

The observer-only phase is complete. The tested module is:

- `framework-run/modules/ChefAnimatorCheckpoint-r22e/ChefAnimatorCheckpoint.r22e.dll`;
- SHA-256
  `8F4C7281BDDC9923571A3D0CFCD86A3D67017F2A2347C2C98FE6E604462A800B`;
- name `chef-animator-checkpoint-v22e-balanced-resume-prefix-observer`.

It observes the complete Animator tuple twice on each compared branch at the
same `Helpers.Resume` prefix: reference pre/post-observer, and replay pre/post-
r21-restore. The paired calls balance the native controller-capture allocator
traffic. It binds observations to `Hpmv.Injector.Server.CurrentFrameData`'s
exact output frame, requires that frame's checkpoint key and boundary-object
identity, retains duplicate/self-perturbation comparisons, and never writes
from an observed tuple. Static IL contains no `Animator.SetFloat`,
`Animator.Play`, `Animator.Update`, or reflection field write added by the
observer.

Evidence:

`artifacts/framework-migration/story11-animator-r22e-resume-prefix-observer-dash-r1`

The four-chef eight-dash-frame plus two-release-frame A/B ran wholly through
in-process rewind from frame 181 to 191 in about 20 seconds. It passed exact
input, entity, food, round/clock, native-physics, contact-manager free-stack,
and TransformChangeDispatch comparisons. r22e reported one rewind restore, ten
exact replay-frame comparisons, no observer failure, no resume failure, and no
ControllerMemory/ControllerInput/topology/mixer first replay difference.

The managed prefix receipts directly prove the phase split:

1. The reference prefix has layer-0 duration bits `0x7F800000` (`+Inf`) in all
   four 372-byte ControllerMemory blobs.
2. The second reference observation is exact across ControllerMemory, public
   state, parameters/layers, all 349 descendant transforms, ControllerInput,
   transition topology, and mixer graph. The observer did not perturb its own
   reference snapshot.
3. Replay **before** r21 is byte-exact to the reference ControllerMemory, so it
   also has `+Inf`; public state/parameters/layers, complete pose,
   ControllerInput, and transition topology are exact. The remaining natural
   replay discrepancy is in the mixer graph: the first mismatch is Player 1's
   layer-0 false-inner input-0 weight, reference `0`, replay `1`. The existing
   restore needed one weight write for Players 1, 3, and 4 and none for Player
   2 in this run.
4. Replay **after** r21 has the exact reference mixer graph and pose, but its
   first ControllerMemory difference is serialized offset 90. This is the
   third byte of the layer-0 duration word at blob `0x58`: every replay blob now
   contains `0x3FA00000` (`1.25`) instead of reference `0x7F800000` (`+Inf`).

Thus the correct resume boundary is not merely “the natural replay state” or
“the old advancing checkpoint.” The natural replay already has the correct
paused ControllerMemory and pose but can retain the wrong mixer weight; r21
repairs that mixer from the advancing checkpoint while simultaneously replacing
the correct paused ControllerMemory with the wrong-phase advancing blob. The
required repair is now exact and source-owned: restore the complete reference
resume-ready tuple captured at the original `Helpers.Resume` prefix—especially
its `+Inf` ControllerMemory, correct mixer graph, and exact descendant pose—as
one coherent unit.

The next restore-enabled revision must require an unambiguous resume-ready
template linked to the target boundary before allowing `Prepare`, preserve the
existing boundary restore during rewind, restore the resume-ready tuple at the
last resume prefix, verify it byte/graph/pose-exact, and clear the pending
restore only after `Helpers.Resume` actually removes the main pause. The native
r19 trace comparison is being independently checked, but the managed prefix
receipts already close the former timing ambiguity.

## Resume-ready restore succeeds on the short native-traced A/B — 2026-09-10

The restore-enabled r23 phase is now implemented and tested. Module:

- `framework-run/modules/ChefAnimatorCheckpoint-r23/ChefAnimatorCheckpoint.r23.dll`;
- SHA-256
  `61149B42C3899AD835B852FB5729D25AA0F19B2C131A2E50C3F19E473FADC738`;
- name `chef-animator-checkpoint-v23-resume-ready-restore`;
- hot-load generation 43.

Static review found no gameplay-facing Animator call or direct derived-field
patch. An independent lifecycle review found no blocker: Prepare requires an
unambiguous reference tuple linked to the exact advancing boundary; rewind
still restores that advancing boundary; the last resume prefix restores the
separate resume-ready tuple; and pending state is cleared only after successful
restore plus confirmed main-pause release.

Evidence:

`artifacts/framework-migration/story11-animator-r23-resume-ready-dash-r1`

The four-chef eight-dash-frame plus two-release-frame A/B ran wholly through
in-process rewind from frame 221 to frame 231 and passed. Exact comparisons
included inputs, entities, food, native round/clocks, captured native physics,
contact-manager free-stack order, and TransformChangeDispatch state.

r23's managed status reports:

- one ordinary rewind restore;
- one resume-ready restore and one confirmed resume completion;
- zero resume completion, observation, module, pose, or native-helper failures;
- ten exact replay-frame comparisons;
- the expected replay-pre mixer mismatch, requiring one weight write for
  Player 1 and one for Player 4 in this run; and
- null differences after the resume-ready restore for ControllerMemory, public
  Animator state/parameters/layers/pose, ControllerInput, transition topology,
  and the complete mixer graph.

All four replay-post ControllerMemory blobs contain the reference pause-phase
`CurrentStateDuration = 0x7F800000` (`+Inf`). The replay-post complete hashes
match the corresponding reference-post hashes. The movement-history module
also reports exact ten-frame replay comparison, no failure, and verified
resume restoration of zero `m_xzSpeed` history for all four chefs.

Native r19 independently closes the final requirement. Correct final `0xA50`
resume fences are original sequence `3101` and replay sequence `2325`. The
first post-fence layer-0 sequence tuples are
`UpdateGraph / entry / entry-memory / entry-output / condition / exit /
exit-memory / exit-output`:

| Chef | Original | Replay |
|---|---|---|
| P1 | `3511/3514/3524/3527/3541/3543/3544/3545` | `2746/2749/2751/2753/2770/2777/2778/2779` |
| P2 | `3539/3542/3554/3556/3590/3598/3601/3604` | `2767/2769/2773/2774/2800/2803/2807/2808` |
| P3 | `3560/3562/3566/3567/3579/3582/3584/3587` | `2796/2799/2804/2806/2832/2837/2841/2843` |
| P4 | `3612/3615/3617/3618/3644/3650/3654/3655` | `2817/2822/2827/2828/2865/2872/2875/2877` |

For every chef:

- entry `CurrentStateDuration` is `+Inf` on both branches and becomes `1.25`
  naturally inside the evaluator on both branches;
- the complete 28-word raw layer memory is bit-exact original/replay at entry
  and exit;
- kind 67 reads movement `Speed = 0` on both branches with identical mode,
  hash, threshold, current state, and transition destination; and
- the five-word StateMachineOutput and its hashes are exact and unchanged
  through the evaluator.

The literal scratch-buffer addresses used for StateMachineInput differ, as
expected for transient worker storage, but their contents and hashes are exact.
Both traces report `droppedEstimate = 0` and `lastError = 0`.

This removes the known raw first-resumed Animator mismatch on the controlled
route. It does **not** yet establish global full rewind parity. The next scope
is held-plate dash/drop, followed by longer continuations and repeated rewinds.
Route search remains blocked until those tests pass.

## Held-plate resume exposes a new mixer link and ControllerInput mismatch — 2026-09-10

Plate entity 2 was picked up by chef 44 and observed with
`attachmentParent=[44]` while chef 44 held `attachment=[2]`. The first attempted
checkpoint at frame 38 was rejected before mutation because the plate's native
`ServerWorldObjectSynchroniser` still had a pending reliable rest update. This
was not an Animator or physics failure. After ordinary simulation through frame
198, WorldSyncCache r5 observed `sentReliable=true`, `active=false`,
`parentChanged=false`, and no unsupported condition. No synchronizer bypass was
used to manufacture that settled state.

The historical held-plate four-chef dash route then ran from checkpoint frame
228 to endpoint frame 290. Exporting its full r19 trace hit a 32-bit managed
`OutOfMemoryException` while the bridge serialized the very large read result;
the original branch itself completed and no rewind had begun. The trace was
deactivated and a direct verified native rewind to frame 228 succeeded.

The exact route timing matters: frame 228 precedes the tested dash. The first
continuation frame asserts dash, followed by 29 movement frames and 30 neutral
frames. At the retained frame-228 resume boundary, every chef/layer has equal
current and next state indices, `inDynamicTransitionRaw = 0`, and no pending
`GotoState` command in both the reference and rewind observations. Thus this
receipt does not show a checkpoint inside an active visible transition. It
shows that transition history from frames 229--290 changed the reusable mixer
branch ordering and that the existing rewind restore did not reconstruct that
settled frame-228 ordering.

The next resume was rejected by r23 before the main pause was removed. Receipt:

`artifacts/framework-migration/story11-animator-r23-held-plate-resume-failure-status-r1.json`

The replay-prefix observation and fail-stop receipt establish:

- the complete ControllerMemory blob, public Animator configuration,
  transition topology, and captured pose match the reference resume-ready
  tuple;
- Player 2's mixer graph differs only at record 1, byte 52, the first outer
  input's `outerChild` pointer: reference `0x411BE2B0`, replay `0x411BE370`.
  Record 0 is the synthetic layer descriptor, so record 1 corresponds to outer
  input index 0 rather than input index 1;
- both records retain the same outer playable, internal storage, entries array,
  input count, mode, argument, weight `1.0`, and trailing word;
- Player 1's ControllerInput separately differs at byte 16, reference `0x44`,
  replay `0x00`, while the other captured resume-prefix components remain
  exact; and
- r23 made no mixer weight write for the failing Player 2 record, reported
  native result 10/topology mismatch, retained `pendingResumeRestore=true`, and
  left the game paused before any replay physics frame.

This demonstrates why strict contextual reconstruction is needed. The mixer
record is not merely an endpoint hash: native r8 reads `outerChild` directly
from the live outer input entry at `entry+4`. Its current restore deliberately
permits only weight differences and therefore refuses to guess whether two
different child pointers are semantically interchangeable or should be
rewired. Likewise, r23 observes but does not write ControllerInput. The r24
design was deferred until the producer/consumer lifecycle of both fields was
identified from Unity's surrounding code.

### Contextual meaning of the mixer pointer

UnityPlayer RVA `0x2F4E00` establishes that each 12-byte connection entry is
`{weight, child playable pointer, port/word}`: it writes the supplied child at
`entry+4`, while the separately revision-verified RVA `0x2F5000` changes only
`entry+0` weight. Therefore the observed `outerChild` is real mutable graph
topology outside ControllerMemory, not a harmless cached address.

Both reference `0x411BE2B0` and replay `0x411BE370` were accepted and traversed
as reachable `AnimationMixerPlayable` objects. The failure receipt stops at the
first raw difference, so the initial safe-design candidate was to accept only
role-equivalent allocation rebasing after proving complete subtree equality.

That candidate has now been tested and rejected using a read-only inspection of
the still-retained failed boundary:

`artifacts/framework-migration/story11-animator-r23-held-resume-native-context-r1.json`

Inspector r7 reflected the immutable `resumeReadyFrame` from r23, invoked only
r23's native capture functions against the live paused Animators, and made no
Animator, graph, Transform, or physics write. It found:

- Player 1's mixer graph is raw-exact.
- Players 2, 3, and 4 each have outer input 0 and outer input 1 exchanged.
- The already-existing branch mixer object, its internal storage/entries, its
  captured raw `+0xA5` byte, and all seven inner child connections travel
  together to the other outer slot.
- Mixer weights and the other non-allocation record fields remain equal.

For Player 2, for example, reference outer input 0 points to `0x411BE2B0`
with children `0x411BE500, 0x411BE760, ...`; replay outer input 0 points to
`0x411BE370` with its pre-existing children `0x411BE630, 0x411BE890, ...`.
Outer input 1 contains the converse. Players 3 and 4 show the same complete
permutation. Thus the numeric differences are not fresh objects filling the
same semantic role; the two whole branches changed slots. A role-by-slot
rebasing comparator would hide the exact topology change that needs an
explanation.

Do not raw-write the child pointers, ignore them, or implement the preliminary
role-rebase plan. The next contextual reverse target is the owning
`AnimationStateMachineMixerPlayable` lifecycle—especially state-mixer
selection, transition completion/interruption, and its no-topology-change
connection operation—to identify how Unity performs this permutation and
which coherent owner operation, if any, can restore the reference ordering.

### Contextual meaning of ControllerInput byte 16

Native capture serializes a 12-byte ControllerInput prefix followed by one
24-byte command record per controller layer. Snapshot byte 16 is consequently
layer-0 `record+0x04`, specifically the first byte of the non-fixed normalized-
time offset written by `Play`, `CrossFade`, and `GotoState`; it is neither a
prefix byte nor a pointer.

UnityPlayer's `GotoStateInternal` writes the fixed-time discriminator at
`record+0x14`, the non-fixed offset at `+0x04`, the fixed-time value at `+0x08`,
and the remaining command fields at `+0x0C/+0x10`. `EvaluateState` consumes the
non-fixed offset only while `StateMachineMemory+0x6B` marks a pending command.
It clears that gate and the `+0x08` fixed-time field afterward, but leaves
`+0x04` unchanged. `Animator::Prepare` only copies public Animator speed into
the ControllerInput prefix; pause/resume does not clear these layer records.

The read-only inspector above also captured the complete command records and
decoded each gate from the retained self-relative ControllerMemory blob. Player
1's layer-0 saved normalized-time offset is `0x3E600744`; replay contains
`0x00000000`. Both saved and replay `ActiveGotoState` gates are zero. The gates
are likewise zero for both layers of all four chefs, ControllerMemory is
byte-exact, and every other ControllerInput byte is equal.

This proves that the observed Player-1 mismatch is inert stale residue from a
consumed command. It does not require restoration. Resume-boundary semantic
comparison may ignore exactly `record+0x04..+0x07` when both matching gates are
zero, while preserving the raw hashes/difference in telemetry. If either gate
is nonzero, the gate and complete 24-byte command record must remain strict and
fail closed. No other ControllerInput field is exempt and no ControllerInput
write is authorized by this result.

### r24 validation order

1. Finish the contextual reverse of the branch permutation and identify a
   coherent Unity-owned restoration operation; fail closed if none is proved.
2. Offline tests must recognize only the proved operation's exact topology and
   reject changed inner child, count, mode, word, or alias relationships.
3. A fresh no-plate dash must require no branch repair and preserve r23's raw
   and native trace parity.
4. The exact held-plate frame-228 boundary must restore the reference branch
   ordering, retain the proved inert ControllerInput classification, and reach
   an exact post-resume tuple before any replay physics frame.
5. Only then run held-plate dash/drop, repeated rewinds, and post-fence native
   parity. Search remains disabled.

### Clean r24 execution and EndTransition trace

The first r24 execution attempted with native transition-lifecycle mask 512
must not be interpreted as an Animator checkpoint result. That trace detours
UnityPlayer RVA `0x646CD0` (`EndTransition`), and API9 hashes the same live
`0x117` bytes before every owner-graph capture. The deterministic result was
`ResultRevisionMismatch`; the fresh Animator incarnation consequently had no
captured history or resume-prefix template. Evidence:

- `artifacts/framework-migration/story11-animator-r24-clean-held-dash-r1/animator-status-after-admission-reject.json`
- `artifacts/framework-migration/story11-animator-r24-clean-held-dash-r1/native-transition-lifecycle-read.json`

The trace itself is complete: 148 paired `EndTransition` calls, no
`StartInterruptedTransition`, no drops. Every observed call exchanged outer
slots 0 and 1 while leaving slot 2 fixed. Sixteen calls occurred during the
held 228→290 forward continuation. None occurred at checkpoint marker 228 or
during the rejected old rewind. Because mask 512 did not install UpdateGraph/
evaluator context, these calls cannot be assigned to a particular chef from
that artifact alone.

After deactivating the trace hook, a fresh r24-only run captured normally and
cleanly reproduced the held S→S failure. Plate 2 was acquired by chef 44,
allowed to finish its ordinary reliable-rest handshake, settled through frame
198, warmed to checkpoint 228, and carried through the same dash continuation
to 290. Native rewind verified at 228. The first replay resume then failed
before emitting an input or physics frame:

`artifacts/framework-migration/story11-animator-r24-observer-only-held-dash-r1/post-failure-module-status.json`

At that boundary all four ControllerMemory blobs and all transition-topology
snapshots are byte-exact. Player 1's mixer graph is exact. Player 2's first and
only reported mismatch is owner/mixer record 1 byte 52: expected outer child
`0x406E31C0`, actual `0x406E3280`. Player 2 layer 0 is still classified
`settled-clean`, with current state 11, next state 11, transition index -1,
and every transition/pending-command gate clear. The native kitchen rewind,
entities, food, clocks, native physics, contact free-stack, and transform
dispatch snapshot were already exact.

This makes the next write experiment precise. It is not a raw pointer swap.
API9 will project the complete mode-2 `EndTransition` postimage—including
connection changes, outer and reciprocal weights, old-branch child weights and
clips, dirty fields, playable flags, and graph dirty bits—and require the
projected complete owner blob to equal the saved target before mutation. Only
then may it invoke Unity's original `EndTransition`; exact structural
recapture, existing mixer-weight restore, and the complete resume-prefix tuple
remain required. There is no generally safe rollback after an unexpected
partial native call, so attempted/completed counts and a mutation-stage receipt
must be retained and such a result is process-fatal for further parity claims.
Transitioning targets remain rejected by this S→S helper.

The live game is windowed and paused at frame 228. The headless controller is
in fail-stop `Error`, r23 retains the failed resume boundary for inspection,
and the large native trace is deactivated. Controller recovery/reload should
occur only after the receipts above are no longer needed live.

## First exact settled-target replay and repeatability — 2026-09-10

The settled-to-settled branch-permutation problem is now repaired for the
representative held-plate four-chef dash probe. This does **not** yet cover a
checkpoint whose saved target is itself transitioning.

Native Animator r15h restores the settled target in two guarded stages. The
relevant Unity operations and admission rules are now based on disassembly and
complete projected owner-graph comparisons:

- UnityPlayer RVA `0x645760` (`SetClip`) writes the clip pointer at `+0x108`,
  sets the child dirty byte at `+0x92` to one, and can dirty the root at
  `+0x93`. It neither reads nor writes the previously suspected `+0x94` and
  `+0xA4` fields. The unsupported r15d preconditions on those bytes were
  removed; the admitted dirty-byte difference is limited to the idempotent
  zero-or-one value that `SetClip` itself writes.
- Stage A can consequently call the original `SetClip(null)` only for the
  exact inactive zero-weight clip topology selected by the preflight. On the
  held probe, Player 1 planned and completed that operation; Players 2–4 were
  correctly skipped at this stage.
- UnityPlayer RVA `0x646CD0` (`EndTransition`) calls `SetClip(null)` for each
  non-terminal old-branch-zero child. `SetClip` intentionally leaves a live
  binding-cache preimage until the next ordinary graph-maintenance visit.
  r15h admits this pending observation only for the exact cleared playable,
  exact projected branch role, coherent null target, binding-observation byte
  range, and a complete cache value equal to the captured preimage. It does
  not waive the final comparison: after the following maintenance frame, the
  mutation-forbidden `requireNoPlan` pass must be fully exact.
- Stage B planned and completed one `EndTransition` for each of Players 2–4.
  The final pass reported zero remaining plans for every chef and exact
  topology, identities, targets, cache observations, and dirty fields.

Managed Animator r31a fixed the six-phase scheduling mismatch that had made an
otherwise successful two-stage native restore impossible to release. It
chooses finalization phase `(origin + 1) % 6`, waits *before any mutation* for
Stage A two phases earlier, requires Stage B on the very next Unity frame, and
requires final verification/release on the next frame at the target phase.
For the saved origin phase 5, the live receipt is:

- Stage A: Unity frame `319182`, scheduler phase 4;
- Stage B: Unity frame `319183`, scheduler phase 5;
- final verification and release: Unity frame `319184`, phase 0; and
- exactly one `CommitFrame`, with no Animator, resume-gate, or completion
  failure.

The complete input probe is:

`artifacts/framework-migration/story11-animator-r30-startup-pose-fix-r7/held-dash-r31a-r15h-r8/`

It rewound frame 290 to held-plate checkpoint frame 228 and replayed all 62
frames. The restored checkpoint and replay endpoint were exact for framework
entities, native round, native food, native physics, native clocks,
contact-manager free-stack order, and TransformChangeDispatch state. Animator
receipts additionally reported exact target-null restoration, Playable clocks
and internal weights, mixer graph, final no-plan owner graph, public
configuration, and captured pose (`assignments = 0`). The post-run receipt is
`post-success-status.json` in that directory.

Two more rewinds of the same saved checkpoint then ran without a level reload:

`artifacts/framework-migration/story11-animator-r30-startup-pose-fix-r7/held-dash-r31a-r15h-repeat-r9/`

Both restored frame 228 and reproduced frame 290 exactly across the same
entity/native/physics/clock boundary. Contact-pool restore accounting advanced
once per rewind and no Rigidbody actor rebuild occurred. This closes the known
settled-target owner-lifecycle defect and provides repeatability evidence, but
it is not general Animator rewind parity yet.

The next required quadrant is a saved target captured during an active
transition. Current r31a deliberately rejects such a target. The immediate
work is to construct a bounded transition-target checkpoint, capture its
complete controller/topology/owner tuple, and determine which Unity-owned
sequence can reconstruct it from both settled and differently-transitioning
current states. Route search remains disabled.

## Transition matrix completed and repeated — 2026-09-10 late evening

The text immediately above is historical. Managed r33 through r39b and native
r15i generalized the guarded owner-lifecycle restoration to transitioning
targets. The controlled transition/settled matrix now has exact evidence in all
four cells. `T <- S` means a transitioning saved target restored after the
forward branch reached a settled current state.

| Cell | Representative checkpoint/endpoint | Repeat evidence |
|---|---|---|
| `S <- S` | held plate, frame 228 -> 290 | r38 first replay plus two repeats |
| `S <- T` | frame 45 -> 51 | r33/r38 first replay; r33 two-repeat semantic proof |
| `T <- S` | frame 10 -> 42 | r40 first replay plus two repeats |
| `T <- T` | frame 7 -> 10 | r40a first replay plus two repeats |

All passing repeat receipts require exact restored baseline and replay endpoint
entities, native round, food, physics, clocks, fixed-input recording, Animator
semantic tuple, contact-manager LIFO order, and TransformChangeDispatch state.
They show zero Rigidbody actor rebuilds.

The final native issue in `T <- T` was not a missing transition operation.
Player 1's saved and live clip binding were already identical. Unity 2017's
`SetClip` returns without dirtying the controller when asked to set the same
clip, so native r15i correctly returned its strict write-mismatch receipt.
Managed r39b admits that one idempotent no-op only when all native before/after
digests are unchanged and a fresh complete owner-graph recapture is byte-exact.
Actual clip mutations still require the original result/stage and dirty-bit
contract. The original frame-7 case exercised this verified no-op path and
then reproduced frame 10 exactly.

The first attempt to repeat that case was falsely rejected by a historical
`resumePrefixObserverFailure` from a different scene's synthetic frame 0. The
before/after counters prove the repeat did not create it: reference captures
and observer-failure count were unchanged, while replay pre/post counts each
advanced once and the frame-7 tuple was exact. `ambiguousResumePrefixFrames`
had been cleared with the scene, but the old error string and count had not.

Managed r40 made those diagnostics obey the same scene lifecycle as the
history they describe. On Animator-incarnation change it clears pending restore
coordination, scene-owned failures and first differences, last receipts, and
observer lists/counters. A fresh frame-7 first replay and two additional
same-checkpoint rewinds then passed completely. Managed r40a also skips creation
of resume-prefix references at output frame 0, matching the native kitchen
checkpoint's existing fail-closed rule for that ambiguous startup frame. Its
fresh-scene status retained only reference frames `4,7`, with zero observer
failure, and its frame-7 replay plus two repeats were exact.

Active managed artifact:

`framework-run/modules/ChefAnimatorCheckpoint-r40a-scene-fault-frame0-lifecycle/ChefAnimatorCheckpoint.r40a-scene-fault-frame0-lifecycle.dll`

SHA-256:

`57630F23B4D9FFBB7B0D0CC1499D4364054352EF0753A7ED814B6896180C21A6`

Native r15i remains active and unchanged:

`artifacts/native-animator-checkpoint-r15i-selective-target-null-weight-build/Oc2NativeAnimatorCheckpoint.r15i.dll`

SHA-256:

`D2DB2C7AEF8842AE59A92E95968A8BBD9C01745FBC841C4A25EA4655844F7CBD`

Primary evidence:

- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-transitioning-r40a-r15i-r9/summary.json`
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-transitioning-repeat-r40a-r15i-r10/summary.json`
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/status-after-r40a-frame7-repeat-r1.json`
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-settled-r40-r15i-r7/summary.json`
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-settled-repeat-r40-r15i-r8/summary.json`
- historical false failure:
  `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-transitioning-repeat-r39b-r15i-r4/observations.json`

The matrix result is general over settled-versus-transitioning lifecycle shape
for this tested locomotion transition, not over every Animator behavior. Next
test checkpoints the chef while holding an item inside a transition and then
replays dash/drop or plate-throwing release/collision. Other transition kinds,
longer mixed gameplay, repeated non-adjacent rewinds, and any later physics or
world-object history remain open. Do not start route search yet.

## Held-item transitioning target with real branch/clip mutation — 2026-09-11

The next case is now exact and repeatable. At saved frame 234, chef 44 holds
plate 2 and Player 1 layer 0 is actively transitioning from state 2 to state 1
with transition index 1. The forward continuation ends at frame 266. Unlike
the earlier frame-7 no-op clip case, returning from 266 to 234 requires a real
native owner-graph mutation: one branch rotation, two non-null clip rebinds,
and 17 weight plans.

The first r16a live attempt failed closed before starting that owner mutation.
Its record-16 diagnostic compared raw `+0xA4` values `0x218D83E0` and
`0x284ADF20`. The failure exposed a verifier modeling error. A single physical
clip playable is represented by both a child/output row, which captures its
complete binding/cache state, and resolver/input/output rows, which
intentionally do not. The old shared preimage helper demanded the child row's
richer cache preimage when checking the resolver row.

UnityPlayer RVA `0x645760` (`SetClip`) was rechecked in disassembly. It only
compares and writes the clip pointer at `+0x108`, writes the child dirty byte
at `+0x92`, resolves the root, and can write root dirty byte `+0x93`. It does
not read or write raw `+0xA4` or the binding/cache fields. Immediately after a
clip rebind, a resolver row may therefore retain precisely the source physical
playable's captured `+0xA4` word until ordinary maintenance. Native r16b
models the two roles separately:

- child/output rows require the complete retained clip-binding/cache
  preimage;
- resolver/input/output rows admit only the exact retained raw `+0xA4` word;
- every other resolver byte remains strict; and
- the final `requireNoPlan` verification remains mutation-forbidden and must
  recapture an exact semantic owner graph.

This split passed an independent source audit and agrees with the disassembled
operation's write set. Native r16b also retains the transaction protections
added in r16a: projected postimage stability before mutation, rollback of
pre-mutation owned writes on refusal, high-byte replacement for controller
dirty state, explicit `mutationStarted`/rollback-failure receipts, and
fail-stop treatment after an unexpected partial native mutation.

The first r16b replay restored frame 234 from frame 266 and replayed all 30
payload plus two release frames. It was exact for:

- recorded inputs (matching SHA-256);
- framework entities and positions;
- native round and food;
- native physics and clocks;
- Animator semantic tuple and final no-plan owner verification;
- contact-manager LIFO free-stack; and
- TransformChangeDispatch order and masks.

Player 1's Stage B receipt reports stage 16, transition `1/1`, rebound clips
`2/2`, weight plans `17/17`, no rollback failure, a stable projected
postimage, and the expected controller dirty transition from zero to
`0x01000000`. Players 2–4 took exact no-op paths. Raw owner hashes can still
differ when binding-cache allocation pointers are rebased, but semantic
comparison remains strict on their complete contents.

Two additional rewinds of the same saved checkpoint passed without reloading
the level. Both reproduced frame 266 across the same complete boundary, the
contact-pool restore count advanced once per rewind, and Rigidbody actor
rebuild count remained zero.

Active artifacts:

- managed r40m:
  `framework-run/modules/ChefAnimatorCheckpoint-r40m-r16b-resolver-rebind/ChefAnimatorCheckpoint.r40m-r16b-resolver-rebind.dll`,
  SHA-256 `09BDF5852AD788C28AE3F21E818A0BE6DE306D06ABF5E4BC4AE0E99F5931FC9B`;
- native r16b:
  `artifacts/native-animator-checkpoint-r16b-resolver-rebind-transient-build/Oc2NativeAnimatorCheckpoint.r16b.dll`,
  SHA-256 `882F470B4D06EAA59234BF3CD8A1C70E842D48B7D1B3E1FC4591A57F22C3C450`;
- BodyRestore r16:
  `framework-run/modules/BodyRestore-r16-collider-ancestor-pose/BodyRestore.r16-collider-ancestor-pose.dll`,
  SHA-256 `606CB10F8F22B5EE9DDD7895B46537679375F807491CF81FD818E3EA8A93210D`.

Evidence:

- `artifacts/framework-migration/story11-animator-r40a-body-r16-process-reset-r3/held-transition-dash-drop-r40m-r16b-r16-r13t-mutation-persistent-r1/summary.json`
- `artifacts/framework-migration/story11-animator-r40a-body-r16-process-reset-r3/held-transition-dash-drop-r40m-r16b-r16-r13t-mutation-persistent-r1/animator-status-post-success-r1.json`
- `artifacts/framework-migration/story11-animator-r40a-body-r16-process-reset-r3/held-transition-dash-drop-r40m-r16b-r16-r13t-mutation-repeat-r2/summary.json`

This closes the first held-item, actively-transitioning target that requires
real branch and non-null clip reconstruction. It still does not prove every
Animator transition or plate-throwing physics. Next rerun the original four
matrix cells under r16b, then prove that the held continuation contains the
intended detach/release/collision and compare the plate's exact physical
outcome. Route search remains disabled.

## 2026-09-11: r16c removes an out-of-bounds false state comparison

The r16b rerun of transitioning-target from settled-current (frame 10 to 42)
failed closed on Player 3 owner record 56. It expected raw `+0xA4` word
`0x424C065C` and observed `0x4220EE2C` on a branch node with vtable RVA
`0xE83744`. Records 56 through 64 repeated the same physical node snapshot.

Disassembly proves that node is an ordinary `AnimationMixerPlayable` whose
allocation is exactly `0xA0` bytes:

- `ConstructPlayable<AnimationMixerPlayable>` at RVA `0x64A6E0` passes
  `0xA0` to allocation;
- the deleting destructor at RVA `0x641260` passes `0xA0` to delete; and
- the constructor at RVA `0x640DF0` initializes only through `+0x9F`.

For an object pointer `P`, the TLSF layout places the next physical block
header at `P+0x9C`. Thus `P+0xA0` is the next block's size/status word and
`P+0xA4` its free-list link. The observed `0x19` at `P+0xA0` decodes as a free
`0x18`-byte alignment-padding block. Normal allocator maintenance—not Animator
evaluation—changed its `next_free` pointer. The separate `mixerFlagA5` byte was
the same out-of-bounds allocation metadata.

Native r16c now reads only `0xA0` bytes and canonicalizes these fields for the
exact ordinary `AnimationMixerPlayable` vtable. It never writes them. Outer
state-machine mixers and clip playables retain strict, in-bounds
`+0xA0/+0xA4` capture. Revision-lock hashes cover the allocation-size operand
and deleting destructor so the assumption fails closed on another Unity
binary.

The corrected build is:

- `artifacts/native-animator-checkpoint-r16c-animation-mixer-oob-canonical-build/Oc2NativeAnimatorCheckpoint.r16c.dll`,
  SHA-256 `5B73BB0C7A8FD6E6F026CDC8703D5C8B51B1803FB17A89F94937478216ACBF07`.

The exact four-cell regression now passes under managed r40m/native r16c:

| Saved target | Run-ahead/current | Frames | Evidence |
|---|---|---:|---|
| transitioning | settled | 10 to 42 | `transition-target-settled-r40m-r16c-r1/summary.json` |
| settled | transitioning | 45 to 51 | `settled-target-transitioning-r40m-r16c-r1/summary.json` |
| transitioning | transitioning | 7 to 10 | `transition-target-transitioning-r40m-r16c-r1/summary.json` |
| settled/held | settled/held | 228 to 290 | `held-dash-settled-target-settled-r40m-r16c-r2/summary.json` |

All evidence directories are beneath
`artifacts/framework-migration/story11-animator-r16b-matrix-host-r4/`.
The frame-234 held-item transitioning mutation also passes to frame 266 in
`held-transition-dash-drop-r40m-r16c-r1/summary.json`.

Endpoint inspection found that the last route's historical name is stronger
than its behavior: plate 2 remains attached to chef 44 at frame 266. It is a
valid held-item Animator mutation proof, but not yet a plate-throw proof. The
next task is to produce a genuine mid-dash detach/release and compare the
plate's exact body pose, velocity, angular velocity, sleeping/kinematic state,
and collision consequences across rewind. Search remains disabled.
## 2026-09-12: r53 composes with repeated delivery presentation reincarnation

Animator r53's dynamic-descendant ownership boundary remains exact in the
first delivered-plate case. Fresh assembly identity r53b contains the same
managed sources as r53; it was needed only because an unrelated DeliveryFade
r8b resume-prefix exception correctly fault-latched the old instance.

With DeliveryFade r9, BodyRestore r31, and ResumePhase r1bc, one clean f444 ->
f447 delivery plus two additional no-reload rewinds all pass Animator semantic
parity. Each rewind reached status 2, was armed at the saved six-phase target,
completed one acknowledged commit, and returned to Record mode. Delivery r9
independently restored and verified the reincarnated physics-free sushi
renderer before and after the same original resume. This adds a composed
dynamic-presentation lifecycle regression without weakening the rule that
nested server/client WorldObjectSynchronisers terminate Animator-owned pose
capture.

Evidence is
`artifacts/framework-migration/story11-delivery-fade-r1-host-r9/delivery-r9-body-r31-animator-r53b-resume-r1bc-f444-to447-r1/summary.json`,
the sibling `...f444-repeat2-r1/summary.json`, and
`animator-r53b-status-after-repeat2-r1.json`. No new Animator defect was found.

## 2026-09-14: second-delivery split is below the Animator boundary

The first broader second-delivery replay under Animator r53b restores f1045
exactly and reaches the same scored delivery at f1048, but fails full physics
parity.  Read-only history from the r25b inspector proves all four endpoint
Animator descriptors are exact: controller memory, controller input,
transition topology, mixer graph, controller lifecycle and descendant pose
hashes all match original versus replay.  The Animator checkpoint module also
reports no semantic, random-state, input, transition, mixer, owner-graph or
resume-prefix difference.

PlayerControls movement history narrows the split further.  All four chefs are
bit-exact at f1046.  At f1047 only Players 1 and 2 differ: the original branch
records Player 1 moving vertically at about -2 while Player 2 remains at zero;
the replay records the opposite assignment.  Players 3 and 4 remain exact.
The corresponding chef Rigidbody heights at f1048 are approximately 0.0404
and 0.0100 in the original and exchanged in replay.  Native food, orders,
score, clocks, dynamic body identities, input hash, contact-pool restoration
and Transform-dispatch restoration remain exact.

Evidence is
`artifacts/framework-migration/story11-multidelivery-dev5-live-r1/second-delivery-rewind-r4/`,
including `summary.json`, `entity-differences.json`, and the three
`movement-frame-1046..1048.json` receipts.  This is no longer classified as an
Animator lifecycle defect.  The next experiment must start a clean process and
install the verified read-only native physics/PhysX tracer before any Harmony
hooks alter its revision-locked entry bytes, then capture the first resumed
simulation step on both branches.
