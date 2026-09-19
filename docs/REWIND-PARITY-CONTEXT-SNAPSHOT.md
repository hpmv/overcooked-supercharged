# Rewind parity: current context snapshot — 2026-09-13

This is the short, causal overview of the current Story 1-1 rewind work. Read
this first after a context reset. For native addresses, trace sequence numbers,
module hashes, and the full evidence trail, continue with
[`ANIMATOR-REWIND-PARITY.md`](ANIMATOR-REWIND-PARITY.md). Where older handoff
notes differ, this snapshot and that detailed Animator note control.

## Latest result — all 12 f444 contact managers have semantic owners

Managed RigidbodyActorRebuild r14k and native helper r20 add a strictly
read-only, fail-closed owner walk for the shipped PhysX 3.3.3 contact-manager
pool.  The f444 checkpoint's 12 active managers are exactly slots 0..11.  All
are chef-capsule/static-box pairs: four chefs against
`Design/Colliders/Collider`, four against `Ceiling`, and four extra Player 4
pairs against nearby counter/crate boxes.  No delivery plate, fish, or dynamic
container participates.  Each manager is non-touching with zero contacts and
owns a 240-byte capsule/box manifold at this boundary.

The walker validates pool membership and bitmaps, free-list complement,
manager/SIP backlinks, shape endpoints and exact shipped PxShape vtables,
geometry types, transform-cache IDs, and stable manager/SIP/manifold hashes.
It performs no game or PhysX writes.  Native SHA-256 is
`9069543CC915CB7BDFFC8D87B8F742BBB9B3145EC2FCC838172DDDA29DF19633`;
managed SHA-256 is
`6ECBACE2F95DE9D535DCBDE1453EF2CF9842230F12B6EEE32CB95E919B2CEB4F`.
Evidence is under
`framework/artifacts/live-v87-contact-owner-f1048-to-f444-r1/`.

The v87 run stopped before contact-pool restoration at an earlier strict
WorldSync boundary: restoring held fish 55's local owner pose changed an
unspecified field of its container Rigidbody 56.  Next make that guard report
the exact per-field before/after difference, then correct the authoring restore
ordering/state without weakening the invariant.  Search remains disabled.

## Latest result — f1048 -> f444 Animator resume boundary is exact

Managed Animator r57 with native r17f now restores all four chefs across the
large direct f1048 -> f444 rewind.  The target is a scheduled uninterrupted
output boundary rather than a naturally paused checkpoint.  Its capture is
therefore projected into authoring pause state only after duplicate read-only
captures prove the native speed word, and its advancing ControllerMemory is
restored once more after paused maintenance.  The final strict checks pass for
ControllerMemory, ControllerInput semantics, transition topology, mixer and
owner graphs, Playable times, pose, and Unity random state.  Ordinary paused
templates and ordinary forward gameplay are unchanged.  The focused semantic
suite passes 25 tests.

The next failure is no longer Animator state.  Before the first replay physics
frame, RigidbodyActorRebuild rejects contact-manager pool restoration.  Fresh
v86 with managed r14j proves that the f444 checkpoint has 244 free / 12 active
managers, while restored f444 has all 256 free.  The live free set equals the
complete checkpoint free set plus exactly 12 pointers; there are zero
checkpoint-only free pointers.  Therefore the rewind has lost the entire
checkpoint-active contact-manager topology, not merely one plate manager.
This is a real hidden-physics-history boundary: the existing helper can reorder
an exact membership set but deliberately cannot invent active SIP ownership.

PhysX 3.3.3 source puts dirty-interaction processing before broadphase; newly
restored overlaps, shape-instance pairs, contact managers, and manifolds appear
during `finishBroadPhase`.  The next read-only diagnostic must map those 12
checkpoint-active manager pointers to their SIP endpoints and touch/cache/
manifold summaries, then trace their first-replay recreation.  The eventual
fix must reconstruct allocation ownership and any required persistent state at
the correct broadphase phase rather than skip the gate.  Search remains
disabled.

Evidence and binaries:

- `framework/artifacts/live-v85-midfade-f1048-to-f444-r1/summary.json`;
- `artifacts/live-v85-animator-failure-status-r1.json`;
- Animator r57 SHA-256
  `0213C9005F4AA00B62A82F913380B3A8FF4D1BFFD3ED4A8C328CF20C1EA88A59`;
- native Animator r17f SHA-256
  `4E4407C84EB97A2CBCB338433F928E22E7888AB73FDFA17B5AEA5EA09C45430C`.
- read-only membership diagnostic r14j SHA-256
  `2BE2E8D9F083328954C5638903D94B7A0C681176A2A48C92E18ADBB748CE741C`.
- fresh v86 summary SHA-256
  `7583AD1436A7836988496709F9531AB1788062571596E641641AA336E96CEC04`;
- preserved v86 player log SHA-256
  `827231943E56CA0EDC31867AAC7A13EB195428DFD99D39B7AC7DB4B0272CE7D4`.

## Latest result — f1045 warp succeeds; one dynamic sleep bit remains

The clean second-delivery f1045 rewind now gets through the complete native
restore. The earlier red-overlay failure on chef 46 was a quantized inverse
problem, not an Animator overwrite: PhysX normalizes the actor quaternion and
composes it through a nonidentity center-of-mass frame, so successive exact
float corrections can cross the target and briefly grow before converging.
The recorded attempts and a float32 PhysX 3.3.3 composition model agree
bit-for-bit for attempts 1–3 and predict the first exact solution at attempt
5. BodyRestore r36 uses a dedicated five-assignment limit for this coupled
position/quaternion loop while retaining bounded inputs/residuals, progress,
all invariants, and exact final readback. Both live final-pose restorations
converged exactly on attempt 5. Its DLL SHA-256 is
`D5F070314E259B64D656F7634F150BF7B981B6A30607E2EDDF6C1212AA1B9322`,
and the focused fixture passes 220 checks.

The accompanying authoring physics gate solves the other hidden-history
problem: automatic PhysX simulation no longer runs repeatedly while the
logical TAS frame is paused. It changes only the authoring pause lifecycle;
`Physics.autoSimulation` is restored before advancing gameplay. The live
receipt proved one and only one maintenance callback for each of 18 actual
resume-to-pause transitions and no gate failure.

The restored f1045 comparison is now exact except for one field:
`$nativePhysics/bodies/7/sleeping`. This row is entity 60 / Rigidbody instance
`-38766`, the surviving empty container of dynamic plate-stack owner 59. It is
kinematic with zero motion and byte-exact pose/settings in both observations,
but was awake at capture and sleeping immediately after rewind. This is now
the active parity target. Evidence:
`artifacts/framework-migration/story11-physics-pause-dev5-live-r4/second-delivery-f1045-rewind-r36-r1/summary.json`
(SHA-256
`CABA30B80AD6C455C63C3A109A3C136FBD57FF11FC37F68C44B4B2EFBE5496FA`).
Search remains disabled; rerun the same f1045 unwind-only cell after adding a
fail-closed dynamic-container sleep checkpoint/restore.

## Latest result — far cross-delivery rewind passes under BodyRestore r33

The retained-history rewind from settled returned-plate frame 1500 directly
back across delivery to frame 444 now restores and replays the 603-frame first
delivery exactly.  The previous preflight failure was not a changed shape
identity: entity 44's persistent chef capsule retained the same `PxShape`, but
its native actor-local pose changed after the chef moved later in the route.
BodyRestore r32 incorrectly treated the old pose/geometry as invariants that
had to match before restoration.

r33 still requires exact surviving `PxShape` identity, actor order, geometry
type, managed role mapping, and exactly two authorized recreated plate box
shapes.  It carries checkpoint pose/geometry for all rows into the existing
native write-and-readback helper.  The successful live receipt reports two
recreated rows, one differing surviving pose at row 0, zero surviving geometry
differences, and no poison/failure.  This path runs only during rewind; forward
physics and plate throwing are unchanged.

The clean no-search reconstruction re-proved every cell from Story 1-1 frame 1
through f1500.  The decisive f1500 -> f444 -> f1047 result has exact entities,
native physics, food, round state, logical clocks, input, contact/manifold pool
history, TransformChangeDispatch, and Animator semantics, with zero actor
rebuilds.  Evidence is
`artifacts/framework-migration/story11-surviving-shape-r33-live-r2/nonadjacent-f1500-to444-r1/summary.json`
(SHA-256 `85C952A86964FAEBCCE617BEC8355F9663A9C8AC2A1DB6088692E2FABC915354`)
and sibling `module-status.json` (SHA-256
`0055848A16BB05AD30F708968132579FB978259E6A585EB47CAC603E77D96FF6`).
The exact tested r33 DLL is
`114E1995226031045603FF601E56A2E643B7197EBFDCD524A5A7FEE15CC1D41D`.
Search remains disabled; continue expanding the lifecycle matrix from this
healthy f1047 boundary.

## Latest result — branch commit and reverse attachment topology pass

Two composability gaps are now closed.

First, Animator r51c rejected an exact non-adjacent replay-prefix commit
because a paused module call observed `Server.CurrentFrameData.FrameNumber=0`
while the controller's actual output boundary was 487. r52 accepts an explicit
controller frame and then proves it against the last captured history and
reference keys, exact semantic comparisons, callback counts, failure fields,
and resume state. The live 789 -> 425 -> 487 transaction verified 62 prefix
frames, discarded 302 abandoned future frames and four callback-tail records,
mutated no game state, returned to Record, and passed another 425 -> 487 cycle.

Second, the inverse prepared-food path—loose at 489, picked up and held at
546, then rewound—found a real collider-child local-scale preimage difference
on surviving body 54. BodyRestore r26 permits restoration only below the
Rigidbody root and leaves all identity, topology, material, analytic geometry,
motion, native shape identity/order, and root-scale checks fail-closed. Its
receipt directly measures `(1,1,1)` -> `(1,1,0.9999999)` on the exact
`ChoppedSushiFish` child, followed by exact native shape pose/geometry
restoration. A fresh complete rewind/replay plus two additional same-checkpoint
cycles pass exact entities, food, round, raw native physics, clocks, Animator,
input, contact-pool LIFO, and TransformChangeDispatch with zero actor rebuilds.

Primary evidence:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r13y-r12-nonadjacent-789-to425-to487-prefix-commit-r1/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r26-r13y-r12-loose489-to-held546-r3/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r26-r13y-r12-loose489-to-held546-repeat-r1/summary.json`; and
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/body-r26-status-after-held546-to-loose489-r1.json`.

Animator r52 is
`C9393018DAD340C8075B779437C928A54EF80EAFBD43EC87A6601F9080A13A54`;
BodyRestore r26 is
`C8D9F69BBD71B3C519A36B16EE9C958100E1B9AE717EC186C7CA2A485FC8568B`.
The staged target-before-first-resume branch transaction now passes too. A
deliberate frame-490 commit at restored frame 489 rejected without changing
entities, food, round, clocks, or raw native physics, and left ReplayStaged
intact. The exact frame-489 commit discarded all 57 retained future frames and
one callback-tail entry with no game mutation, preserved the linked pending
resume, became Record on the first ordinary resume, and reproduced the held
frame-546 endpoint exactly. Evidence:
`artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r26-staged-prefix-commit-frame489-r1.json`.

The game and controller are paused/input-fenced at frame 546. Search remains
disabled. Both active-prefix and staged-target branch commit timings are now
proved; next choose a genuinely new gameplay topology for broader rewind
coverage.

## Latest result — non-adjacent checkpoint history passes under r13y/r12

The remaining mixed-order blocker was architectural rather than a new physics
drift. `RigidbodyActorRebuild-r13w` retained one managed
`TransformDispatchState`, while native helper r6b retained one DLL-global
contact-manager free-list snapshot. Capturing the loose frame-487 checkpoint
therefore discarded the older held frame-425 hidden order even though the core
checkpoint history retained both frames.

r13y replaces that last-value cache with bounded per-frame sidecars. Every
entry owns the exact private `NativeKitchenCheckpoint.history[frame]` object,
observed PxsContext, free-array address, complete ordered free-list, and paired
Transform-dispatch state. Native r12 adds caller-owned capture/restore exports.
Restore still rejects context, array, count, duplicate, or membership changes
before writing, then verifies every restored pointer exactly. All saved
Transform hierarchy pointers are preflighted before the first queue write.
Round identity, scene generation, or context change clears the collection;
successful rewind prunes entries newer than its target just as the core does.
A target with no exact sidecar now fails during `Prepare`, before core mutation.

Live sequence in one Story 1-1 load:

1. frame 425 -> 487 held dash/drop passed and retained sidecar 425;
2. frame 487 -> 789 loose neutral continuation passed and retained both 425
   and 487 (their contact-order hashes differ);
3. direct frame 789 -> 425, then replay to 487, passed exactly;
4. frame 487 was correctly pruned, re-captured on the new branch, and the same
   direct 789 -> 425 test passed again.

The non-adjacent receipts are exact for restored baseline and replay endpoint:
entities, native round, food, raw native physics, clocks, input, Animator
semantics, contact-pool LIFO, and TransformChangeDispatch. Actor rebuild count
remained zero. Evidence:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r13y-r12-r50-r17f-r12f-r24h-held-frame425-to487-r3/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r13y-r12-r50-r17f-r12f-r24h-loose-frame487-to789-r2-rebranch/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r13y-r12-r50-r17f-r12f-r24h-nonadjacent-frame789-to425-to487-r2-rebranch/summary.json`; and
- `artifacts/native-rigidbody-rebuild-r12-multiframe-sidecars-testbuild1/manifest.json`.

Managed r13y SHA-256 is
`F2CF6F20F3C443FCB5FF554C43CDB862DC52758470F475D41F4D14C80EA79836`;
native r12 SHA-256 is
`E28EA2D8EE26FD3D7E363334A6C9E90D7F9BCE0CD999CFD1F73D51A199FCA25E`.
Game PID 75372 and host PID 78804 are paused/input-fenced at frame 487.
Search remains disabled while broader parity coverage continues.

## Latest result — loose prepared-food composition passes under r12f/r24h

The frame-487 loose prepared-food checkpoint now rewinds from frame 789 and
replays 300 neutral payload frames plus two release frames exactly. This is a
checkpoint composed from the successful frame-425 held-dash/drop replay, not
a fresh-level approximation.

Two fail-closed admission gaps were corrected. First, the actual loose
`PhysicalAttachment` topology is owner 53 parented directly beneath its own
registered Rigidbody container 54. Both attachment flags are false, while the
server and client `WorldObjectSynchroniser` caches name the container's
`ObjectContainer` / entity 54. WorldSync r12f captures and restores that exact
same-incarnation topology. It accepts the observed quiescent pending-rest
tuple (`sentReliable=false`, `active=false`, `parentChanged=true`) through the
already modeled relative deadline restoration; no native clock or event is
suppressed.

Second, container 54 is colliderless while held, but when loose the item's
collider is a child shape of that same Rigidbody. BodyRestore r24h therefore
permits analytic collider-bearing dynamic bodies only when the exact managed
body, Transform, collider hierarchy/static fields, native Rigidbody pointer,
and ordered PxShape pointers survive. It reuses the ordinary exact collider,
native shape, mass-frame, and body-pose restore path. Empty containers retain
the older exact cross-incarnation path; collider-bearing cross-incarnation
restore remains unsupported and fails before mutation.

Offline WorldSync checks pass 126 cases. Live evidence:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12f-r24h-prepared-food-held-dash-drop-frame425-to487-r1/summary.json`;
- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12f-r24h-composed-loose-prepared-food-frame487-to789-r1/summary.json`;
- `artifacts/framework-world-sync-cache-r12f-tests.json`.

The second receipt is exact for the immediate frame-487 restored baseline and
the replayed frame-789 endpoint: entities, native round, food, physics,
clocks, input, Animator semantics, contact-manager LIFO, and
TransformChangeDispatch all agree. Active WorldSync r12f SHA-256 is
`BF67C0D9C773B99444443B78D9A495B40A0219DBFCAB5481073C2F1281217459`.
Active BodyRestore r24h SHA-256 is
`12CD85F6E2820E0B3C0920C1B82A1C4565C4CCADB0DBFB6F6D711A42F4636E03`.
Game PID 75372 and host PID 78804 are paused/input-fenced at frame 789.
Search remains disabled. Next stress repeated and non-adjacent rewind order
without reloading the level.

## Latest result — prepared-food dash/drop passes under WorldSync r12b

The actual frame-425 -> 487 prepared-food held-dash/drop branch now restores
and replays exactly. This closes two separate defects rather than weakening a
comparison.

First, old WorldSync checkpoints captured only the four initial object caches.
At frame 425 dynamic prepared owner 53 was attached to chef 45 in both its
server message cache and `ClientWorldObjectSynchroniser` cache. After running
to the detached future and rewinding, the client cache remained detached; its
next normal packet therefore issued a false parent/scale correction. r12a
captures and restores admitted dynamic server/client caches using exact
scheduler owner identity and a surviving registered parent. The clean
checkpoint status proves five items, with owner 53 `messageParent=45` and
client `hasParent=true,parentId=45`.

Second, the clean uninterrupted checkpoint contained a legitimate public pose
split on colliderless proxy 54: Rigidbody z was `-6.853491` while Transform z
was `-6.85349`, two float representable steps apart. BodyRestore reproduced
both values exactly, but Unity's final frozen maintenance subsequently copied
the body pose to the Transform. r12b stores the separate Transform pose for
the already-admitted dynamic container and reapplies it only during paused
post-warp authoring maintenance. It fails closed unless entity, body, parent,
and shape identity are exact, and compares the complete Rigidbody state before
and after the Transform-only write. The correction is cleared on resume and
does not run during advancing gameplay.

`FrameworkWorldSyncCacheCheck` passes 117 cases. The live receipt is:

- `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12b-prepared-food-held-dash-drop-frame425-to487-r1/summary.json`.

It reports exact immediate restored baseline and exact frame-487 replay for
entities, native round, food, physics, clocks, input, Animator semantics,
contact-manager LIFO order, and TransformChangeDispatch. The original
frame-428 `ChoppedSushiFish.localScale` drift is gone. Active WorldSync r12b is
`framework-run/modules/WorldSyncCache-r12b-dynamic-paused-transform/WorldSyncCache.r12b-dynamic-paused-transform.dll`,
SHA-256 `D6EF7A12E515C908451C10B6DB1CA3E20E3A987AD29AB8FE72FCFED9764A1DAC`.
Game PID 75372 and host PID 78804 are paused/fenced at frame 487. Search remains
disabled. Next checkpoint this replayed loose prepared-food state through a
long continuation, then exercise mixed non-adjacent rewinds.

## Latest result — inverse transition-owner subset passes under r50/r17f

The remaining known Animator restore refusal was a guard that was too strict,
not evidence of a missing target node. In the failed frame-324 Stage-B capture,
Players 2 and 3 each had a 76-row/26-unique-node target owner graph against a
104-row/28-unique-node live graph. The target physical node set was a strict
subset of the live set. The two additional live nodes per chef were the
resolver-only `AnimationLayerMixerPlayable` and `AnimatorControllerPlayable`;
the existing guarded `EndTransition` lifecycle step subsequently removed them
from reachability. No target-only node existed.

Native r17f keeps every prior identity, topology, temporal-consistency,
projected-postimage, mutation-completion, and final no-plan check. It permits
only `uniqueTargetNodes <= uniqueCurrentNodes`, still requires every target
physical node to exist live, restores only target-matched clocks, and requires
the complete live row count/hash projection to remain stable before the
separate transition normalization. Managed r50 exposes and enforces that same
narrow rule. This does not suppress animation or change ordinary forward
gameplay.

A fresh frame-177 checkpoint then exercised the generalized path for three
simultaneous transitions: each target had 26 unique nodes against 28 live
nodes before normalization. Stage B restored all planned target clocks while
the live graphs stayed at 104 rows, guarded transition normalization reduced
them to the exact 76-row targets, final verification passed, and all 32 replay
frames were exact. Three additional same-checkpoint rewinds also passed with
zero Rigidbody actor rebuilds. The canonical held-transition plate throw from
frame 234 to 266, input SHA-256 `40a90f...`, passes under the same stack, as
does a composed checkpoint at the replayed loose-plate frame 266 through 300
neutral frames to 568.

Primary evidence beneath
`artifacts/framework-migration/story11-animator-r49-host-r33/`:

- `r50-r17f-multichef-inverse-transition-frame177-r2/summary.json`;
- `r50-r17f-multichef-inverse-transition-frame177-repeat-r2/summary.json`;
- `r50-status-after-frame177-pass-r1.json`;
- `r50-r17f-canonical-plate-throw-frame234-to266-r1/summary.json`; and
- `r50-r17f-composed-post-throw-neutral300-frame266-to568-r1/summary.json`.

Active artifacts are managed r50 SHA-256
`CE77A5E20A2F0B0963ABF11CACCEE3BED33EDF431CEE05E18E5E780B1E256BA5`,
native r17f SHA-256
`4E4407C84EB97A2CBCB338433F928E22E7888AB73FDFA17B5AEA5EA09C45430C`,
and ResumePhase r1ba SHA-256
`8429C6B8F9E13AA710048FD6977AE469F0937867879201CC63431EAA3AFB0C54`.
Game PID 31204 and host PID 54416 are paused/input-fenced at frame 568.
Revalidate before control. Search remains disabled. The next useful parity
boundary is an actual prepared-food attachment transfer/drop followed by
mixed non-adjacent composed rewinds.

## Latest result — dynamic prepared-food composition now passes

The r48 Animator/native r17d stack remains clean. The next composed dynamic-
food test exposed two controller reconstruction/evidence defects; neither was
a new physics or Animator mismatch and neither fix writes to the game.

First, the actual raw-fish-to-prepared-fish lifecycle is:

1. board 31 starts working raw entity 51 at frame 92;
2. prepared entity 53 is spawned and attached at frame 176 while raw 51 is
   destroyed; and
3. the final inactive workstation message arrives at frame 177 without an
   item header.

`RealGameSimulator` had retained raw 51 as `itemBeingChopped`, causing a later
frame-215 checkpoint to contain a dangling `[30,0]` workstation reference.
The controller now clears a workstation item when that exact record is
destroyed, keeps active interacters intact on the completion frame, accepts an
active item ID of zero as null, clears the item after the last inactive
interacter, and null-guards synthetic progress. This mirrors observed native
lifecycle only; it does not alter the running game.

Second, a successful frame-397 -> 215 native restore was already exact but the
registry audit deleted the consumed raw `[30,0]` receipt because raw 51 was no
longer live. Prepared `[30,0,0]` still exists at frame 215 and needs that
parent's observed `SpawnNames` receipt to prove its native prefab index. v12n
therefore retains consumed dynamic ancestors of entities that exist at the
target, while still deleting paths exclusive to the abandoned future. No
fresh native registration is invented: the live prepared entity 53 and proxy
54 keep their exact native IDs/instance, and the receipt explicitly reports
`freshlyRegisteredIds: []`, `retainedHistoricalAncestorPaths: [[30,0]]`, and
`nativeStateChanged: false`.

The frozen v12n host is
`artifacts/framework-headless-host-v12n-registry-ancestor-lifecycle/Headless.dll`,
SHA-256
`E50F7650AC505CB0AA27A509EDBF8A2841651D71069A8A038EB6DD9811710B9D`.
Offline checks pass: broad selftest 348, dynamic-warp 34, Story 1-1 79, and
Python registry evidence 9. Live evidence under host-r31 passes:

- the true frame-90 -> 215 chop/replacement rewind, with recreated dynamic
  proxy incarnation allowed only by the established dynamic-ID rule;
- the formerly blocked prepared-food frame-215 -> 397 composition, with exact
  proxy 54 instance across rewind;
- a second checkpoint taken from that replayed frame 397 through frame 579;
  and
- a short frame-592 -> 617 movement/input parity probe.

The first three relevant directories are:

- `artifacts/framework-migration/story11-animator-r48-host-r31/r48-v12n-food-chop-frame90-to215-r3/`;
- `artifacts/framework-migration/story11-animator-r48-host-r31/r48-v12n-prepared-food-frame215-neutral180-r1/`;
- `artifacts/framework-migration/story11-animator-r48-host-r31/r48-v12n-prepared-food-frame397-neutral180-composed-r1/`.

All report zero managed entity differences and exact native round, food,
physics, clocks, contact-pool order, TransformChangeDispatch state, and
Animator semantics. Current live state is game PID 31204 and host PID 86116,
paused/input-fenced at frame 617. Revalidate identities before control. Search
remains disabled. The next useful parity boundary is an actual attachment
transfer of prepared dynamic food (then drop/throw), not more neutral search.

The red `AUTHORING_RESUME_PHASE_FAILED` overlay shown by the user is useful
diagnostically but is not evidence against these successful receipts. The
known r1ax module instance can retain its old `failure` field and repaint the
text after the core invalid reason clears, including across a level reload.
That stale-display lifecycle should be fixed separately; structured resume,
restore, and comparison receipts remain authoritative.

## Latest result — r48 closes composed rewinds and revalidates plate throwing

Managed Animator r48 fixes the false failure that appeared only when a second
checkpoint was taken from a replayed endpoint. Native disassembly proved that
ControllerInput layer `record+0x00` is the requested-state hash retained after
a `GotoState` command is consumed. Unity reads it only while the matching
`StateMachineMemory+0x6B` `ActiveGotoState` gate is set, clears the gate when
the command is consumed, and does not clear the record. The preceding
`record+0x04` normalized-time word has the same lifecycle and was already
classified this way.

r48 therefore ignores only `record+0x00..+0x07` during semantic comparison
when both the saved and observed same-layer gates are zero. If either gate is
set, the complete 24-byte record remains strict. Prefix bytes, record
`+0x08..+0x17`, identities, shapes, raw hashes, `byteExact`, and first-
difference telemetry remain strict/visible. No native state is written and no
Animator or gameplay behavior is changed. The managed artifact is:

- `framework-run/modules/ChefAnimatorCheckpoint-r48-consumed-state-hash/ChefAnimatorCheckpoint.r48-consumed-state-hash.dll`;
- SHA-256 `EA1EC534ED03D0C98C24F6F1E20F6AA361BE9C7829DF459D9C37FE4607222608`;
- unchanged native r17d SHA-256
  `5E207E27B0E2929A5534334C1947DA4A77B82CE439915F72D969535936A55D85`.

The offline semantic suite passes 22 cases. More importantly, the exact live
composition that failed under r47 now passes:

1. frame 7 -> 10 passes and produces recording SHA-256 `f7a3a9...`;
2. a new checkpoint is taken from that replayed frame 10; and
3. frame 10 -> 42 then restores and replays exactly.

At r48's final resume-ready check all four ControllerInput blobs are still raw
inexact at overall byte 12 (`record+0`), both saved/live gates are zero, and
`semanticEqual=true`. Thus the test exercised the intended exemption rather
than making the raw mismatch disappear. Evidence:

- `artifacts/framework-migration/story11-animator-r47-host-r25/composed-r48-step1-frame7-to10-r1/summary.json`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/composed-r48-step2-frame10-to42-r1/summary.json`.

Because the earlier r47 frame-7 fixture had become settled after long paused
startup maintenance, r48 was also tested with deliberately constructed active
transitions. The corrected four-cell matrix is exact for entities, native
round/food/physics/clocks, contact-pool order, TransformChangeDispatch, input,
and Animator semantics:

| Rewind cell | Frames | Evidence directory |
| --- | --- | --- |
| S<-T | 45 -> 51 | `r48-matrix-settled-target-transitioning-frame45-to51-r1` |
| T<-T | 51 -> 54 | `r48-matrix-transitioning-target-transitioning-frame51-to54-r1` |
| T<-S | 54 -> 86 | `r48-matrix-transitioning-target-settled-frame54-to86-r1` |
| S<-S | 86 -> 118 | `r48-matrix-settled-target-settled-frame86-to118-r1` |

All directories are beneath
`artifacts/framework-migration/story11-animator-r47-host-r25/`. The frame-51
and frame-54 checkpoints explicitly report `transitioning-authored`, so the
T-target claims do not rely on filenames or assumed startup timing.

The exact historical real plate-throw input then passed from the held,
transitioning frame-234 checkpoint to frame 266 (recording SHA-256
`40a90f5f013bfa3bbd9b052023547216cb21aece9738806ea047c9d4a3d59890`).
A second checkpoint taken from that replayed loose-plate frame 266 passed a
300-neutral-frame future to 568 (SHA-256
`ed0a99f959188e70996ecc8c6f1152bba61b18e37831121c1a72b3bc48334aec`).
Two more same-checkpoint rewinds passed without a reload, with exact native
physics and zero Rigidbody actor rebuilds. Evidence:

- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-held-transition-plate-throw-frame234-to266-r1/summary.json`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-composed-post-throw-neutral300-frame266-to568-r1/summary.json`;
- `artifacts/framework-migration/story11-animator-r47-host-r25/r48-composed-post-throw-neutral300-repeat2-r1/summary.json`.

The red `AUTHORING_RESUME_PHASE_FAILED` overlay observed by the user was a
separate stale-diagnostic issue. Core `InvalidReason` cleared on level reload,
but the retired ResumePhase r1ax instance retained its `failure` field and
reasserted the overlay during paused callbacks. Structured receipts, not the
persisting text, were authoritative. A fresh r1ay module instance cleared it;
automatic scene-lifecycle clearing remains a quality-of-life fix, not a
rewind-state repair.

Current live state: game PID 31204, host PID 61088, Story 1-1 paused/fenced at
frame 568 after the two repeat replays. Revalidate identities before control.
Search remains disabled. The next parity target is broader mixed gameplay and
dynamic food/workable replacement under composed non-adjacent rewinds; the
existing r47 frame-90-to-215 dynamic-food proof should be repeated only where
r48 composition or a newly exercised lifecycle adds coverage.

## Latest validated stack — r43 retires continuous owner-graph capture

Animator r43 preserves native owner-graph capture only at the boundaries for
which evidence shows it is required. Its exact policy is:

| Capture site | Owner graph |
| --- | --- |
| ordinary per-output `CaptureFrame` | no |
| stored `reference-prefix` resume snapshot | yes |
| diagnostic `reference-post` | no |
| `replay-pre` restore fence | yes |
| `replay-post` final verification | yes |
| `OverrideClipPlayables` admission guards | yes |

The `reference-prefix` graph remains restore input. The fresh `replay-post`
graph and the override before/after graphs remain fail-closed guards. The
apparently diagnostic `replay-pre` graph also remains: r42 omitted it and the
T<-S case failed before its first replay payload with a Player1
`ControllerInput` mismatch and a different outer mixer child. Static native
inspection found reads, hashing, and temporary allocations rather than an
obvious Unity mutation, so its precise mechanism is still unproved; runtime
evidence nevertheless requires treating it as a fence. Ordinary output and
`reference-post` owner graphs remain omitted.

All four representative transition-lifecycle cells pass on r43:

| Rewind cell | Frames | Result receipt |
| --- | --- | --- |
| T<-T | 7 -> 10 (1 payload + 2 release) | `r43-matrix-transition-target-transitioning-r1/summary.json` |
| T<-S | 10 -> 42 (30 + 2) | `r43-matrix-transition-target-settled-r3/summary.json` |
| S<-T | 45 -> 51 (4 + 2) | `r43-matrix-settled-target-transitioning-r1/summary.json` |
| held S<-S | 228 -> 290 (60 + 2) | `r43-matrix-held-settled-target-settled-60-r3/summary.json` |

T<-S then passed two more same-checkpoint rewinds in
`r43-matrix-transition-target-settled-repeat2-r1/summary.json`. These prove the
four tested lifecycle shapes, including a full held-item settled case; they do
not prove every Animator state, transition type, level, or gameplay future.

The genuine dash/drop route also passes a long r43 continuation. The throw
prefix from frame 234 to 266 has recording SHA-256
`40a90f5f013bfa3bbd9b052023547216cb21aece9738806ea047c9d4a3d59890`.
From that checkpoint, 300 neutral payload frames plus two release frames reach
frame 568 with exact entity, native round, food, physics, clock, restored
baseline, contact-pool, TransformDispatch, and final Animator checks. That
continuation recording has SHA-256
`ed0a99f959188e70996ecc8c6f1152bba61b18e37831121c1a72b3bc48334aec`;
the receipt is
`artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r43-plate-throw-neutral300-parity-r1/summary.json`.

A stronger combined regression then checkpointed the held chef while actively
transitioning at frame 234 and ran one uninterrupted future through the real
dash/drop and five seconds of plate physics to frame 568. The 332 payload plus
two release frames have recording SHA-256
`ce41e2c3f72ebfc5f158e1a5fb7a5fd875764a3876724cffc966331c5fc7e23c`.
The reference and first replay passed, followed by two additional rewinds from
the already-replayed endpoint without a level reload. All three rewinds had
exact restored baselines and endpoints for entities, native round, food,
physics, clocks, and the recording; raw native physics was exact, both native
ordering structures were restored, Animator semantic parity passed, and no
Rigidbody actor rebuild occurred. Evidence:

- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r43-r13w-mixed-frame234-to568-r1/summary.json`;
- `artifacts/framework-migration/story11-frame266-owner-restore-host-r21/r43-r13w-mixed-frame234-to568-repeat2-r1/summary.json`.

After these validations, r43 reported only **793,728** owner-graph bytes for
the current scene and **3,968,640** lifetime bytes. This replaces r41's costly
per-output owner observation without changing restore behavior. The active DLL
is
`framework-run/modules/ChefAnimatorCheckpoint-r43-replay-pre-owner-fence/ChefAnimatorCheckpoint.r43-replay-pre-owner-fence.dll`,
SHA-256
`40BC5B47983B18156C1A2B869A0F01735EE53BA3E3110B9DD66C2FA3ECFCD002`.

Actor module r13w separately passed the abandoned-restore scene-generation
test. An armed frame-572 contact-pool/TransformDispatch snapshot belonged to
scene-metadata generation 60 and native contact context `0x35DCFFA0`. Reloading
advanced the generation to 61 and produced context `0x3742F720`; r13w cleared
the old snapshots (`sceneOwnedResets: 1`). Contact-pool and TransformDispatch
restore counters both stayed zero, and the next armed step reached frame 3 in
the new scene cleanly. Evidence is `r13w-arm-abandoned-restore-r3.json`,
`r13w-post-reload-status-r1.json`, and `r13w-post-reload-step-r1.json` in the
same r21 evidence directory.

This closes the known representative Animator and plate-throw regressions, not
universal rewind parity. The game and controller are healthy, windowed, and
paused/fenced at frame 568 after the third combined replay. r13w retains the
valid current-scene frame-234 snapshot with no automatic restore pending;
restore counts are three and actor rebuild count is zero. Route search remains
disabled. The next parity dimension should exercise real station/workable and
dynamic food replacement lifecycles, then mixed/non-adjacent checkpoints, before
unwind throughput is timed.

## Prior diagnostic result — r41 captured the 302-frame future twice

The first long continuation of the genuine plate-throw checkpoint now passes
completely. From target frame 266, the reference ran 300 neutral payload
frames plus the controller's two neutral release frames to frame 568. The game
then rewound 568 to 266 and reproduced that future twice without reloading the
level. Both replays consumed the same 302-frame input recording, SHA-256
`ed0a99f959188e70996ecc8c6f1152bba61b18e37831121c1a72b3bc48334aec`.

At all 302 output boundaries in each replay, the Animator checkpoint compared
the managed semantic state, Unity random state, controller input records,
transition topology, mixer graph, and complete native owner graph. Every
comparison was exact. There were zero observer, resume, or Animator restore
failures. The independent endpoint check was also exact across all 50
serialized entities, all 9 native rigidbodies, registry/attachments, settled
chef positions, action graph, raw-input receipt, elapsed time, orders, score,
recipe cursor, and recipe RNG history. Stable compact-JSON hashes were:

- entities:
  `64c6365d32c3d71d743dd223564aae43ed3e1b51c6698f5e1cc6fe857f1e92ae`;
- native physics:
  `84f56203531e51453c659768c1a3630d59a076761d2c772072e1f20eb12333e3`;
- registry:
  `f57241534b9e5567d577f8bc7dbc08ef83a9c379b39f1382bea1a3bac9b5ed51`;
- native round after excluding only the authoring diagnostic warp counter:
  `4305128ddc76094f59ec17f7fed01052996308b0cedb77b85a363a678fa1dd7b`.

The raw native-round difference is only
`recipeRandom.authoringWarpCount: 0 -> 1` (and then the same value remains on
the second replay). That field records that an authoring rewind occurred; it
is not read by gameplay.

Two additional rewind defects were exposed and fixed before this result:

1. Frame 266 legitimately has only chef 44's capsule attached, but still has
   the mass frame from the immediately preceding held-plate compound. PhysX
   shape removal/local-pose mutation does not automatically recompute mass.
   BodyRestore r23 therefore restores that exact transient center of mass and
   inertia through the native rigidbody helper after exact topology, shape,
   scalar-mass, and automatic-mode checks. Exact readback and unchanged
   motion/pose guards remain mandatory.
2. Unity's pose and automatic-mass setters perturb entity 47's angular-Y
   velocity by five ULPs during rewind. r23 repairs only witnessed finite
   setter side effects below `1e-6`, requires kinematic/gravity modes to remain
   unchanged, and verifies exact motion readback. The reference branch and
   ordinary gameplay are untouched.

The long run also requires WorldSyncCache r8b. Plate 2 is in a valid local
quiescent parent-change/rest handshake at frame 266. r8b captures the relative
deadline and rebases it at both reference and rewind resume; it leaves the
native synchronizer's Update, delay, event, and clock behavior intact.

Animator r41 fixes a diagnostic-accounting problem discovered during the long
run. The former 256 MiB owner-graph counter accumulated bytes across old scene
snapshots that had already been cleared, so the first replay stopped observing
after 54 frames. r41 keeps lifetime telemetry but enforces the safety budget
against the current scene. It does not change any captured bytes, comparison,
or restore path. The complete reference plus two replays used 104,188,672
scene bytes, below the 268,435,456-byte limit.

Primary evidence is under
`artifacts/framework-migration/story11-frame266-bounded-motion-host-r19/`:

- `post-throw-neutral-original-r6-scene-budget.json` — frame 266 to 568
  reference;
- `post-throw-neutral-replay-r6-scene-budget.json` — first complete long
  rewind/replay; and
- `post-throw-neutral-replay-r7-scene-budget-repeat.json` — second rewind from
  the already replayed state.

Artifacts active for that r41 result:

- controller v12i:
  `artifacts/framework-headless-host-v12i-native-phase-metadata/Headless.dll`,
  SHA-256 `8D151B95E41F45168E870D238CB8C4F1F3B8A39CFAE7B1ADDC64FB4AA07BE0DA`;
- BodyRestore r23:
  `framework-run/modules/BodyRestore-r23-bounded-motion-side-effects/BodyRestore.r23-bounded-motion-side-effects.dll`,
  SHA-256 `CA7C75B00CB3347F71A6A0E572423EB8406DCDFBBA51A654042741AF767E0B90`;
- WorldSyncCache r8b:
  `framework-run/modules/WorldSyncCache-r8b-reference-and-warp-rebase/WorldSyncCache.r8b-reference-and-warp-rebase.dll`,
  SHA-256 `4B3A1994715B4A9AF36DBD95F793903AB68558AA7139E07476B3027380EF8574`;
- managed Animator r41:
  `framework-run/modules/ChefAnimatorCheckpoint-r41-scene-owner-budget/ChefAnimatorCheckpoint.r41-scene-owner-budget.dll`,
  SHA-256 `F5EF2182FD7F203D72577FD84B4C07F36CD066E823A63E25CA8FC8B3133FC06F`;
- native Animator r16c:
  `artifacts/native-animator-checkpoint-r16c-animation-mixer-oob-canonical-build/Oc2NativeAnimatorCheckpoint.r16c.dll`,
  SHA-256 `5B73BB0C7A8FD6E6F026CDC8703D5C8B51B1803FB17A89F94937478216ACBF07`;
- native rigidbody/shape helper r10:
  `artifacts/native-rigidbody-rebuild-r10-shape-geometry-build1/Oc2NativeRigidbodyRebuild.r10.dll`,
  SHA-256 `C8C08A88DB1DC4A03D7A20FDB2F2F24E1C3C782B724F9E4F59229E06801AEBCF`.

At the time of that result, game PID 48296 and host PID 88776 were healthy and
paused at frame 568 after the second long replay. This closed every then-known
divergence on
the controlled transition matrix, genuine dash/drop plate throw, and its
five-second post-throw future. It is strong continuation-parity evidence, not
a proof over arbitrary future gameplay. Search remains disabled. The next
engineering step is to remove continuous owner-graph diagnostic capture from
ordinary output frames while retaining every owner graph actually consumed by
resume restoration and final fail-closed verification, then rerun the matrix
and long plate-throw regression before timing unwind performance.

## Latest result — genuine plate throw is frame-exact across three rewinds

The remaining Story 1-1 plate-throw divergence is fixed on the controlled
frame-234 to frame-266 route. This route really detaches plate 2 during chef
44's dash: at frame 266 the plate has no parent and has velocity
`(6.036876, -2.450443, 5.819812e-7)`. Its input recording SHA-256 is
`40a90f5f013bfa3bbd9b052023547216cb21aece9738806ea047c9d4a3d59890`.

The first native divergence had been two PhysX box half-extents on the held
plate compound. Managed collider geometry and every transform were exact, but
the reference native values were `0x3EB33332` while rewind produced
`0x3EB3332B` (seven ULPs). BodyRestore r20 now captures each actor shape's
exact native geometry and restores it through PhysX 3.3's official
`NpShape::setGeometry` path before mass recomputation/unfreeze. On the valid
rewind, entity 44's three shapes report `geometryChanged=2`, then the second
verification pass reports zero changes and exact order hashes.

The long apparent stalls at frame 197/198 were a separate controller defect.
The uncapped paused RPC pump samples every other Unity phase, so controller-
side waiting can miss the desired parity forever. Controller v12i no longer
guesses from sampled phases. It puts the saved target phase in the otherwise
unused optional `GameSpeed` field of the plain resume envelope. ResumePhase
v4 consumes that metadata in-process, retains the exact accepted input, and
holds the native pause until the actual target phase. No game code reads
`GameSpeed`; original input, time, Animator, and physics handlers are
unchanged. The successful replay published at observed RPC phase 1, executed
at saved target phase 0 after four held native callbacks, coordinated Animator
status 2, and committed exactly once.

The valid run explicitly captures the contact-manager free stack at the same
scene's frame 234 before the reference continuation. Its restore used context
`0x06038240`, retained all 256 free members, and restored the saved order.
This explicit capture matters: an earlier invalid attempt accidentally reused
an old frame-234 snapshot from a previous scene and failed closed before any
replay physics frame.

Primary evidence is under
`artifacts/framework-migration/story11-frame236-shape-geometry-host-r13/`:

- `plate-throw-original-r8-captured-pool.json` — reference continuation;
- `plate-throw-replay-r8-captured-pool.json` — first valid rewind/replay;
- `plate-throw-replay-r8-repeat-2.json` and
  `plate-throw-replay-r8-repeat-3.json` — two more rewinds without a reload;
- `inspect-after-successful-replay-r1.json` — exact Body/PhysX pool,
  ResumePhase, and Animator receipts; and
- `exchange.jsonl` — complete input/output exchange evidence.

For each of the three replays, all 33 advancing exchanges from frame 234
through frame 266 are exact after structural JSON normalization: 33/33 native
outputs and 33/33 controller inputs, with zero differing rows. All 50 endpoint
entities and every non-bookkeeping inspection field are also exact. The only
endpoint differences are expected control evidence: `warpUsed`, exchange and
resume-metadata counters, the trace byte counters, and the populated warp
validation receipt.

Current active artifacts:

- controller v12i:
  `artifacts/framework-headless-host-v12i-native-phase-metadata/Headless.dll`,
  SHA-256 `8D151B95E41F45168E870D238CB8C4F1F3B8A39CFAE7B1ADDC64FB4AA07BE0DA`;
- BodyRestore r20:
  `framework-run/modules/BodyRestore-r20-native-shape-geometry/BodyRestore.r20-native-shape-geometry.dll`,
  SHA-256 `A4ED65EC38CE04FA6323EBD572B789DF7208B908E935621E964300B0CFB95B5B`;
- native shape helper r10:
  `artifacts/native-rigidbody-rebuild-r10-shape-geometry-build1/Oc2NativeRigidbodyRebuild.r10.dll`,
  SHA-256 `C8C08A88DB1DC4A03D7A20FDB2F2F24E1C3C782B724F9E4F59229E06801AEBCF`;
- ResumePhase r1ar:
  `framework-run/modules/ResumePhase-r1ar-explicit-target-retry/ResumePhase.r1ar-explicit-target-retry.dll`,
  SHA-256 `07D393F088B8E08AA9FA6DCF076EB4E89B0FA8E5B40767A7795F648009B96274`;
- managed Animator r40p:
  `framework-run/modules/ChefAnimatorCheckpoint-r40p-explicit-target-retry/ChefAnimatorCheckpoint.r40p-explicit-target-retry.dll`,
  SHA-256 `95F21FC5819D73CD5043D659D750F06BAF285B7DCDF3E70E55CFE4A59E396352`;
- native Animator r16c remains unchanged, SHA-256
  `5B73BB0C7A8FD6E6F026CDC8703D5C8B51B1803FB17A89F94937478216ACBF07`.

The live game/controller are healthy and paused at frame 266 in run
`story11-frame236-shape-geometry-host-r13`. This closes the known genuine
plate-throw rewind defect and proves repeatability at this checkpoint. It does
not yet prove arbitrary long mixed gameplay or every component. Search remains
disabled until broader non-adjacent/long continuation parity is exercised.

## Prior result — r16c closes the complete Animator lifecycle regression

Native r16b exposed one final apparent mismatch while rerunning the original
four-cell settled/transitioning matrix: the transitioning-target from settled
current case (frame 10 to frame 42) rejected Player 3 owner record 56 because
raw word `+0xA4` changed from `0x424C065C` to `0x4220EE2C`. Static
disassembly proved this was a verifier bug, not hidden Animator state.

The record is an ordinary `AnimationMixerPlayable` with vtable RVA
`0xE83744`. Its constructor allocation site at RVA `0x64A6E0` and deleting
destructor at RVA `0x641260` independently prove that the object is exactly
`0xA0` bytes. `P+0xA0` is the following TLSF block's size/status word and
`P+0xA4` is that adjacent free block's `next_free` pointer. The observed
`P+0xA0 == 0x19` describes a free `0x18`-byte alignment-padding block, and
normal allocator maintenance is allowed to change `P+0xA4`. Those two words,
plus the old single-byte `mixerFlagA5` observation, were out-of-bounds reads.
Writing them would corrupt the allocator.

Native r16c canonicalizes those out-of-bounds values only for the exact
ordinary `AnimationMixerPlayable` vtable. It retains strict in-bounds
`+0xA0/+0xA4` comparison for the outer state-machine `MixerPlayable` and clip
playables. Revision-locked hashes cover both the `0xA0` allocation operand and
the deleting destructor. No gameplay or restore mutation was added, and no
Animator admission rule was relaxed.

On r16c, every original lifecycle cell now passes again in the same process:

| Saved target | Run-ahead/current state | Exact frames | Result |
|---|---|---:|---|
| transitioning | settled | 10 to 42 | pass |
| settled | transitioning | 45 to 51 | pass |
| transitioning | transitioning | 7 to 10 | pass |
| settled | settled, held plate/four-chef dash | 228 to 290 | pass |

All four require exact framework entities, native round/food/physics/clocks,
recorded input, Animator semantics, contact-manager free-stack, and
TransformChangeDispatch state. The real held-plate transitioning checkpoint
at frame 234 also passes to frame 266 under r16c; it still performs the one
branch rotation, two non-null clip rebinds, and 17 weight restorations.

Current native Animator artifact:

- `artifacts/native-animator-checkpoint-r16c-animation-mixer-oob-canonical-build/Oc2NativeAnimatorCheckpoint.r16c.dll`,
  SHA-256 `5B73BB0C7A8FD6E6F026CDC8703D5C8B51B1803FB17A89F94937478216ACBF07`.

New primary evidence:

- `artifacts/framework-migration/story11-animator-r16b-matrix-host-r4/transition-target-settled-r40m-r16c-r1/summary.json`;
- `artifacts/framework-migration/story11-animator-r16b-matrix-host-r4/settled-target-transitioning-r40m-r16c-r1/summary.json`;
- `artifacts/framework-migration/story11-animator-r16b-matrix-host-r4/transition-target-transitioning-r40m-r16c-r1/summary.json`;
- `artifacts/framework-migration/story11-animator-r16b-matrix-host-r4/held-dash-settled-target-settled-r40m-r16c-r2/summary.json`;
- `artifacts/framework-migration/story11-animator-r16b-matrix-host-r4/held-transition-dash-drop-r40m-r16c-r1/summary.json`.

The runtime is paused at an experimental frame-266 endpoint in the same game
process. Route search remains disabled. The next parity task is to construct
and prove an actual plate detach/release during dash, then compare the plate's
exact per-body outcome; the older route name `held-transition-dash-drop` is
misleading because inspection proves plate 2 remains attached to chef 44 at
its frame-266 endpoint. Longer mixed gameplay, repeated non-adjacent rewinds,
the tutorial skip, and local-only synchronizer simplification remain open.

## Prior result — held-item transition mutation passes repeatedly

The held-item transition checkpoint that was still open below now passes. This
is a materially stronger case than the original four-cell locomotion matrix:
Player 1 is holding plate 2 at saved frame 234, layer 0 is actively
transitioning (`currentState = 2`, `nextState = 1`, `transitionIndex = 1`),
and restoring it from endpoint frame 266 requires an actual native owner-graph
mutation: one branch rotation, two non-null clip rebinds, and 17 weight
restores.

Managed Animator r40m plus native Animator r16b reproduced the complete
30-frame payload and two neutral release frames exactly. The first replay and
two further same-checkpoint rewinds passed without a level reload. Every run
required exact restored-baseline and replay-endpoint entities, native round,
food, physics, clocks, input recording, Animator semantic tuple,
contact-manager free-stack, and TransformChangeDispatch state. No Rigidbody
actor rebuild was used.

The last native defect was in the mutation verifier, not in Unity's mutation.
The same physical clip playable appears both as a rich child record and as a
resolver/input record. The resolver legitimately retains the source clip's
raw `+0xA4` word immediately after `SetClip`; it does not own the child's
binding/cache fields. The old verifier incorrectly demanded the richer child
preimage for both roles and refused safely before mutation. Disassembly of
UnityPlayer RVA `0x645760` confirms that `SetClip` only compares/writes the
clip pointer at `+0x108`, marks the child at `+0x92`, and may mark the root at
`+0x93`; it does not touch `+0xA4` or the binding/cache fields. r16b therefore
keeps the complete-preimage rule for child/output rows and admits only the
exact retained `+0xA4` word for resolver/input rows. All other bytes and the
final mutation-forbidden no-plan recapture remain strict.

Current active artifacts:

- managed Animator
  `framework-run/modules/ChefAnimatorCheckpoint-r40m-r16b-resolver-rebind/ChefAnimatorCheckpoint.r40m-r16b-resolver-rebind.dll`,
  SHA-256 `09BDF5852AD788C28AE3F21E818A0BE6DE306D06ABF5E4BC4AE0E99F5931FC9B`;
- native Animator
  `artifacts/native-animator-checkpoint-r16b-resolver-rebind-transient-build/Oc2NativeAnimatorCheckpoint.r16b.dll`,
  SHA-256 `882F470B4D06EAA59234BF3CD8A1C70E842D48B7D1B3E1FC4591A57F22C3C450`;
- BodyRestore r16
  `framework-run/modules/BodyRestore-r16-collider-ancestor-pose/BodyRestore.r16-collider-ancestor-pose.dll`,
  SHA-256 `606CB10F8F22B5EE9DDD7895B46537679375F807491CF81FD818E3EA8A93210D`.

Primary evidence:

- first exact mutation replay:
  `artifacts/framework-migration/story11-animator-r40a-body-r16-process-reset-r3/held-transition-dash-drop-r40m-r16b-r16-r13t-mutation-persistent-r1/summary.json`;
- complete successful Animator receipt:
  `artifacts/framework-migration/story11-animator-r40a-body-r16-process-reset-r3/held-transition-dash-drop-r40m-r16b-r16-r13t-mutation-persistent-r1/animator-status-post-success-r1.json`;
- two further in-process rewinds:
  `artifacts/framework-migration/story11-animator-r40a-body-r16-process-reset-r3/held-transition-dash-drop-r40m-r16b-r16-r13t-mutation-repeat-r2/summary.json`;
- safe pre-mutation r16a rejection that isolated the resolver-role issue:
  `artifacts/framework-migration/story11-animator-r40a-body-r16-process-reset-r3/held-transition-dash-drop-r40l-r16a-r16-r13s-mutation-persistent-r1/`.

The runtime is paused at endpoint frame 266 in the same game process after the
two repeats. Route search is still disabled. The immediate regression is to
rerun all four settled/transitioning matrix cells under r16b, then validate
that the continuation really exercises plate detach/release collision and
compare its exact plate pose/physics. Other transition kinds, longer mixed
gameplay, and repeated non-adjacent rewinds remain open. The tutorial skip and
any local-only synchronizer simplification remain later, behavior-preserving
work.

All sections below predate this r16b result unless they explicitly say
otherwise.

## Prior result — 2026-09-10 late evening

The first-pass Animator transition matrix is now complete, and every cell has
an exact repeated-rewind proof on a controlled Story 1-1 route:

| Saved target | Run-ahead/current state | Result |
|---|---|---|
| settled | settled | exact first replay plus two additional rewinds |
| settled | transitioning | exact first replay plus two additional rewinds |
| transitioning | settled | exact first replay plus two additional rewinds |
| transitioning | transitioning | exact first replay plus two additional rewinds |

The last missing cell was transitioning-to-transitioning at the original
frame-7 timing. Its first r39b replay was exact, but the repeat harness rejected
a stale `resumePrefixObserverFailure` left by a prior scene's ambiguous frame-0
startup. Counter evidence proved the repeat added no observer fault: reference
capture counts stayed fixed, replay pre/post counts increased by one, and the
fault count did not change. The final frame-7 replay tuple was exact.

Managed r40 repaired the lifecycle mismatch by clearing scene-owned fault,
first-difference, receipt, and observer state whenever the four Animator
incarnations change. The fresh-scene frame-7 replay and two further in-process
rewinds then passed exact gameplay/native physics/food/clock, Animator semantic,
contact-manager free-stack, and TransformChangeDispatch comparison with zero
Rigidbody actor rebuilds. A transitioning-target/settled-current frame-10 to
frame-42 case also passed once plus two repeats under r40.

Managed r40a additionally refuses to create an Animator resume-prefix template
at output frame 0. The native kitchen checkpoint already declares that
synthetic startup frame ambiguous because multiple advancing engine states can
share it. On a fresh scene r40a retained reference frames `4,7`, reported zero
observer failures and no ambiguous reference, then passed the frame-7
transitioning-to-transitioning replay once plus two further rewinds exactly.

Current active Animator artifacts:

- managed
  `framework-run/modules/ChefAnimatorCheckpoint-r40a-scene-fault-frame0-lifecycle/ChefAnimatorCheckpoint.r40a-scene-fault-frame0-lifecycle.dll`,
  SHA-256 `57630F23B4D9FFBB7B0D0CC1499D4364054352EF0753A7ED814B6896180C21A6`,
  generation 53;
- native
  `artifacts/native-animator-checkpoint-r15i-selective-target-null-weight-build/Oc2NativeAnimatorCheckpoint.r15i.dll`,
  SHA-256 `D2DB2C7AEF8842AE59A92E95968A8BBD9C01745FBC841C4A25EA4655844F7CBD`.

Primary newest evidence:

- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-transitioning-r40a-r15i-r9/summary.json`;
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-transitioning-repeat-r40a-r15i-r10/summary.json`;
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/status-after-r40a-frame7-repeat-r1.json`;
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-settled-r40-r15i-r7/summary.json`;
- `artifacts/framework-migration/story11-animator-r39-host-reset-r2/transition-target-settled-repeat-r40-r15i-r8/summary.json`.

This closes the transition/settled lifecycle matrix for the tested locomotion
transition. It is not yet global rewind parity. The next scope is broader
Animator behavior: checkpoints during transitions while holding an item,
dash/drop and plate-throwing release/collision parity, other transition kinds,
then longer mixed gameplay and repeated non-adjacent rewinds. Route search
remains disabled.

## Objective and constraints

The immediate objective is **full rewind continuation parity**, not route
search. Given a checkpoint and the same subsequent inputs, the rewound branch
must reproduce the original future. An exact-looking snapshot at the rewind
boundary is not enough if omitted engine history makes the first resumed update
choose a different future.

Search must eventually be in-process and rewind-only; repeatedly reloading the
level is too slow. Preserve real Animator and held-item behavior because
plate-throwing depends on the release pose during a dash. Local-only network
synchronization may be simplified only when local gameplay is unchanged. The
Story 1-1 sashimi tutorial can be skipped later, independently of this parity
work.

The current reverse-engineering method is deliberately contextual. A differing
byte or pointer is not patched in isolation: first identify the code that owns
it, the lifecycle phase in which that code produces it, and every consumer that
can affect the next frame. This method already separated the ordinary paused
Animator tuple from the resume-ready tuple and prevented an incorrect direct
patch of `CurrentStateDuration`. It is now being applied to the held-route
mixer-child link and ControllerInput record.

## The causal chain so far

The first conspicuous failure was a chef root-height discrepancy: one branch
followed Unity/PhysX's normal `0 -> 0.05` settling staircase while the other
remained at `0` (and some tests showed the reverse phase).

That did not turn out to mean the staircase itself was faulty:

1. Unity 2017.4.8f1 empty-project and isolated in-game probes reproduced the
   staircase but rewound bit-exactly.
2. Managed mutation tracing showed the real-chef discrepancy appeared during
   the native physics interval, not as a direct managed position write.
3. Native tracing found rewind-sensitive PhysX contact-manager allocation
   order. Restoring its LIFO free-stack order, together with Unity's
   TransformChangeDispatch pending order and masks, removed the `0` versus
   `0.05` mismatch on the controlled routes.

This proves a concrete cause and repair for those routes, not that every piece
of hidden physics history is now covered. With that mismatch gone, internal
Animator state became the earliest observed continuation difference.

## Why the Animator is part of rewind correctness

The Animator is not being investigated merely because a private byte differs.
It evaluates the chef skeleton and therefore the transform from which a held
plate is released. A replay can have the same chef root position yet give the
plate a subtly different position or collision during dash/drop if its
animation state, mixer weights, or evaluated pose differs.

The investigation therefore preserves the original Animator rather than
freezing the attachment point or replacing animation-driven behavior.

One Animator mismatch was ordinary missing gameplay history. The movement
parameter named `Speed` is written by `PlayerAnimationDecisions` from
`PlayerControls.GetMovementSpeed()`. The latter depends on cached
`PlayerControls` fields including the previous position. Rewind restored the
Transform but omitted that history, so the first replay fixed tick falsely
calculated maximum movement and wrote `Speed = 1`. The
`ChefMovementHistoryCheckpoint-r3` module restores those fields; the same test
now writes `Speed = 0` on both branches and reaches an exact evaluator exit.

## The remaining Animator phase mismatch

After the movement repair, the only raw first-entry difference was layer 0
`StateMachineMemory.CurrentStateDuration`:

- reference first resumed evaluator entry: `+Inf`;
- replay first resumed evaluator entry: `1.25`.

The evaluator overwrites the field with `1.25` before any demonstrated
consumer, and both evaluator exits are exact. It is non-causal in that one
transaction, but it remains a genuine indication that the replay is entering
the Animator in a different phase.

Native chronology and managed observations now explain the difference exactly:

```text
ordinary paused state at Helpers.Resume
  reference: ControllerMemory has +Inf; mixer graph is correct
  natural replay: ControllerMemory also has +Inf; pose is correct;
                  one or more mixer weights can be stale

old r21 replay repair
  restores the advancing checkpoint as one tuple
  mixer becomes correct
  ControllerMemory is replaced with the wrong-phase 1.25 value
```

The r22e observer measured the complete tuple at the actual resume prefix. Its
second reference capture was identical to the first, showing that the observer
did not perturb the state. Before r21's replay restore, ControllerMemory,
ControllerInput, topology, public state, and pose matched the reference, while
the mixer did not. After r21, the mixer and pose matched but every chef's
duration word had changed from the reference `+Inf` to `1.25`.

So neither “leave the natural replay alone” nor “restore the advancing
checkpoint again at resume” is correct. The correct unit is a separate,
engine-produced **resume-ready Animator tuple** captured at the original
`Helpers.Resume` boundary.

## Current implementation and short-test result

The active restore-enabled module is
`ChefAnimatorCheckpoint-r23/ChefAnimatorCheckpoint.r23.dll`, SHA-256
`61149B42C3899AD835B852FB5729D25AA0F19B2C131A2E50C3F19E473FADC738`.
It has passed static and independent structural review and its first live
native-traced rewind A/B.

r23 keeps two distinct states for a frame:

- the ordinary advancing checkpoint used during the rewind mutation; and
- the unambiguous resume-ready reference tuple linked by object identity to
  that exact checkpoint.

At the last `Helpers.Resume` prefix on replay, it restores the full resume-ready
ControllerMemory, repairs the mixer graph, validates ControllerInput and
transition topology, reapplies the captured descendant pose, then recaptures
and requires the whole tuple to equal the reference. It does not write public
Animator speed, call `Animator.Update`/`Play`, or patch the duration field
directly. Pending state is cleared only after the resume postfix confirms that
the main pause was actually removed.

The short test used four chefs, eight dash/input frames, two neutral release
frames, and only in-process rewind from frame 221 to 231. It passed:

- first resumed evaluator entry had `+Inf` on both branches for all chefs;
- movement `Speed` was `0` on both branches for all chefs;
- raw layer memory, evaluator output/exit, ControllerMemory, ControllerInput,
  topology, mixer graph, and pose were exact;
- entities, food, clocks, native physics, contact-pool ordering, and
  TransformChangeDispatch state were exact; and
- r23 reported one completed resume-ready restore with no failures or trace
  loss.

The first held-plate expansion has now exposed a second Animator boundary that
the short test did not exercise. Plate 2 was attached to chef 44, allowed to
finish its ordinary reliable WorldObject rest update, and then carried through
the historical 60-frame four-chef dash route. A rewind from the original
endpoint to held-plate checkpoint frame 228 completed and verified. At the
first attempted resume, r23 correctly failed closed while restoring the saved
resume-ready mixer graph:

Frame 228 is a settled checkpoint immediately before the tested dash input,
not a checkpoint in the middle of that dash transition. In the retained
reference and replay ControllerMemory, every layer has `currentState ==
nextState`, `inDynamicTransition = 0`, and no pending `GotoState` command. The
60-frame continuation starts by pressing dash for one frame. Its later
transition completions rearrange Unity's reusable native mixer branches; the
rewind restores the semantic frame-228 state but currently leaves that
endpoint-era branch arrangement behind. The present failure is therefore the
settled-to-settled quadrant of general Animator restoration. Mid-transition
targets remain a required but separately unproved quadrant.

- Player 2's 26-record graph had one non-weight mismatch at record 1, byte 52:
  the outer input's child pointer was reference `0x411BE2B0`, replay
  `0x411BE370`;
- ControllerMemory, public configuration, transition topology, and the full
  captured pose were otherwise exact at the replay resume prefix; and
- Player 1's separate 60-byte ControllerInput snapshot also differed at byte
  16 (`0x44` versus `0x00`). r23 currently validates rather than restores that
  structure, so this is a second issue that would remain after the mixer link.

No first replay physics frame ran after this failure; the game stayed paused.
This is therefore an Animator resume-boundary result, not evidence of later
physics drift.

Contextual UnityPlayer disassembly has now identified both fields. Mixer record
1 is outer input index 0; record 0 is a synthetic layer descriptor. Its byte 52
is the actual live graph-edge target, not scratch storage. Both reference and
replay targets were valid reachable `AnimationMixerPlayable` objects.

A read-only inspector then captured the retained reference tuple and complete
current graph before disturbing the failed boundary. It disproved the first
"allocation identity only" hypothesis. Player 1's graph is raw-exact, while
Players 2, 3, and 4 have outer inputs 0 and 1 exchanged. Each existing branch
mixer, its storage, and all seven child connections move together to the other
outer slot; they are not fresh role-equivalent objects in the same slot. The
preliminary role-rebase design would therefore conceal a real branch
permutation and is rejected. r24 must not ignore or raw-write these pointers.
The investigation is now reconstructing the owning
`AnimationStateMachineMixerPlayable` transition lifecycle and looking for the
Unity operation that can restore the exact slot ordering with its invariants.

ControllerInput snapshot byte 16 is the low byte of layer 0 record offset 4:
the non-fixed normalized-time offset command written by `Play`/`CrossFade`.
Unity consumes it only while the corresponding
`StateMachineMemory+0x6B` pending-command gate is set, and it does not clear the
offset after consuming the command. The same read-only inspector now closes
the missing evidence: Player 1's complete saved value is `0x3E600744`, replay
is `0x00000000`, and the layer-0 pending-command gate is zero on both sides.
Both gates are also zero for every other chef/layer. This particular mismatch
is therefore proved inert stale residue, not a pending Animator command. r24
may exclude only the four `record+0x04..+0x07` bytes from resume-boundary
semantic equality when both corresponding gates are zero, while continuing to
log raw differences and requiring the entire record if either gate is set. No
ControllerInput write is needed for this case.

After the branch-permutation ownership and safe restoration path are proved,
validation returns to held-plate dash/drop, longer continuations, and repeated
rewinds. Route search remains blocked until those tests pass.

## Clean r24 owner/lifecycle result — 2026-09-10

The first combined r24 plus transition-lifecycle run was not a valid owner-
graph test. The lifecycle tracer detoured the first bytes of UnityPlayer's
`EndTransition`, while native Animator API9 deliberately hashes that exact
function body before every owner-graph capture. API9 therefore returned
`ResultRevisionMismatch` and r24 captured no Story 1-1 frame. This is a
deterministic instrumentation/verifier conflict, not Animator ambiguity or
rewind drift. Lifecycle traces and API9 owner captures must remain separate
until the verifier has an explicit hook-coordination contract.

The complete trace-only run is nevertheless useful. It retained 631 events
with zero drops: 148 paired `EndTransition` entries/exits and no
`StartInterruptedTransition` calls. Every `EndTransition` exchanged outer
slots 0 and 1 while leaving slot 2 fixed. The held-route checkpoint marker at
frame 228 had no transition call; the forward 228→290 continuation contained
16 `EndTransition` pairs. The rejected old combined rewind itself contained no
transition-lifecycle call. These receipts establish `EndTransition` as the
Unity-owned producer of the observed slot permutation, but do not yet prove
that invoking it at rewind produces the complete target postimage.

A fresh r24-only run then removed the trace hook and repeated the deterministic
plate acquisition, ordinary one-second WorldObject settle, 30-frame warmup,
and held four-chef dash. The original continuation ran 228→290, native rewind
to 228 verified, and r24 stopped the first replay resume before any input or
physics frame. This is the clean S→S reproduction:

- all four ControllerMemory blobs and transition-topology snapshots were
  byte-exact after rewind;
- Player 1's mixer/owner graph was exact;
- Player 2 first differed only at outer owner record 1, entry-playable byte 52,
  expected `0x406E31C0`, actual `0x406E3280`;
- the Animator remained lifecycle-classified `settled-clean` with
  `currentState == nextState`, no transition, and no pending GotoState; and
- the framework's entities, food, clocks, native physics, contact-manager
  free-stack, and TransformChangeDispatch snapshot were exact at restored
  frame 228.

Authoritative clean evidence is under
`artifacts/framework-migration/story11-animator-r24-observer-only-held-dash-r1/`;
the detailed module receipt is `post-failure-module-status.json`. The log's
`Framework state transition failed` is only the headless wrapper's report that
r24 threw at `BeforeAuthoringResume`; the rewind itself succeeded.

The implementation now in progress is deliberately limited to this proved
settled target class. After restoring target ControllerMemory, a native helper
will preflight the current and target transition topology plus complete owner
graph, admit only the exact one-`EndTransition` predecessor shape, invoke
Unity's original operation, and require the expected structural postimage.
Existing identity-based mixer-weight restore and complete resume-prefix
recapture remain mandatory. Any transitioning target or unrelated graph
difference must still fail closed; T→S, S→T, and T→T remain separate work.

## Local-only WorldObject synchronization conclusion

The earlier entity-2 checkpoint rejection was an overly narrow admission rule,
not proof that the plate was corrupt. Attaching a physical item legitimately
starts a one-second reliable-rest handshake: the server synchronizer marks the
parent/pose dirty, sends ordinary updates, and later emits one reliable rest
event before becoming settled. Waiting for that event made the current test
admissible without changing game behavior.

This generic path cannot safely be blanket-disabled even in a local-only TAS.
The local `ClientWorldObjectSynchroniser` really receives these updates. A
parent-cache difference can run `DoReparenting`, invoke parent callbacks, and
write the physical attachment container's Rigidbody pose. That is materially
different from the existing chef-only synchronization bypass, whose paired
local client pose/event handlers were proved empty.

If in-flight held-item checkpoints later matter for search throughput, the
smallest behavior-preserving extension is to checkpoint the handshake rather
than suppress it. Save its reliable-rest deadline relative to captured Unity
time, translate that deadline into the restored Unity-time epoch, and restore
the existing server message, client cache, and scheduler order exactly. Do not
force the handshake complete: that would remove an event and change transport
and batching history. Detailed evidence and implementation constraints are in
[`FRAMEWORK-WORLD-SYNC-CACHE-MODULE.md`](FRAMEWORK-WORLD-SYNC-CACHE-MODULE.md).

## What is proved, likely, and still open

**Proved on controlled routes:** the earlier root-height failure was repaired
by restoring contact-manager free-stack and TransformChangeDispatch history;
missing `PlayerControls` movement history caused the false Animator `Speed = 1`;
r21 itself caused the residual `+Inf` versus `1.25` resume-entry mismatch; and
the reference resume-ready tuple is observable without self-perturbation.

**Now proved on the short A/B:** restoring the complete reference resume-ready
tuple preserves both pause-phase ControllerMemory and the correct mixer graph,
eliminating the known first-resumed Animator mismatch without changing any
captured gameplay result.

**Still open:** whether a guarded call to Unity's now-identified
`EndTransition` operation produces the complete target owner-graph postimage;
the transitioning-target quadrants; whether other Animator or PhysX history
appears after that boundary; whether held-item release/collision is exact
during plate-throwing; whether longer and repeated rewinds remain exact;
whether the optional in-flight WorldObject deadline translation is needed for
search throughput; and therefore whether rewind is ready for high-speed route
search.

## Current paused runtime

The game is paused at frame 228 after the clean held-plate rewind. The headless
controller is intentionally in `Error` because r24 rejected the first resume;
`pendingResumeRestore=true` preserves the failed boundary for inspection. The
native transition-lifecycle trace is deactivated because its `EndTransition`
detour conflicts with API9's revision verifier. The clean r24-only failure is
separate from the old large-trace serializer failure and occurred before any
replay frame.

## Current result: exact repeatable settled-target rewind — 2026-09-10

The old paused-r24 description immediately above is historical. The runtime
has since been reloaded with managed Animator r31a and native Animator r15h,
and the representative held-plate settled-target rewind now passes exactly.

The native repair invokes only proved Unity-owned operations behind strict
whole-graph preflight/postflight checks. Disassembly established the actual
`SetClip` write set (`clip +0x108`, dirty byte `+0x92`, optional root dirty
byte `+0x93`) and removed two false opaque-byte assumptions. For the later
`EndTransition`, r15h recognizes the exact pending binding-cache lifecycle:
the just-cleared old-branch clip may retain only its complete captured binding
preimage until the one ordinary maintenance visit that follows. The final
mutation-forbidden no-plan verification remains strict and was exact for all
four chefs.

Managed r31a aligns the two mutation stages with the saved six-phase resume
origin. The successful origin-5 receipt ran Stage A at phase 4 / Unity frame
319182, Stage B at phase 5 / frame 319183, and final verification plus release
at phase 0 / frame 319184. There was exactly one commit and no Animator or
resume failure.

Primary evidence:

- `artifacts/framework-migration/story11-animator-r30-startup-pose-fix-r7/held-dash-r31a-r15h-r8/summary.json`
- `artifacts/framework-migration/story11-animator-r30-startup-pose-fix-r7/held-dash-r31a-r15h-r8/post-success-status.json`
- `artifacts/framework-migration/story11-animator-r30-startup-pose-fix-r7/held-dash-r31a-r15h-repeat-r9/summary.json`

The first probe and two additional same-checkpoint rewinds all restored frame
228 and reproduced frame 290 exactly for entities, native round/food/physics/
clocks, contact free-stack, and transform dispatch. Repeats required no level
reload and no Rigidbody actor rebuild.

This closes the known settled-to-settled branch-permutation defect; it does
not prove general Animator parity. Current r31a intentionally rejects saved
targets inside an active transition. The next bounded task is to capture such
a target and reconstruct it through coherent Unity lifecycle operations for
settled-to-transitioning and transitioning-to-transitioning rewinds. Do not
start route search until those quadrants and subsequent plate-throwing/longer
parity probes pass.
## 2026-09-12: repeated delivered-plate unwind is exact

The f444 checkpoint precedes the first Story 1-1 fish-sushi delivery; the
three-frame continuation reaches f447 with score 28, one delivery, and the
head order consumed. The delivery coroutine is still in its first fade tick,
so rewinding must stop the iterator, remove the delivery PFX/observer entry,
destroy only the iterator-owned fade materials, and restore the still-live
plate's colliders and presentation.

The first replay worked under DeliveryFade r5, but the next unwind exposed a
presentation lifecycle outside the original identity model. The stable plate
and its `ClientAttachedOrderCosmeticDecisions` component survive. That
component destroys and reinstantiates its private `m_container`, replacing
the one sushi `MeshRenderer` and its material. A retained-target inspector on
the failed r8b boundary proved that the old and new containers were both
physics-free and that the renderer topology was otherwise identical. The only
transform difference was local scale `(1,1,1)` versus
`(1.00000012,1,1.00000012)`; the material had the same shader but a new native
identity.

DeliveryFade r9 now maps only that exact one-renderer, same-owner, same-key,
same-mesh, physics-free reincarnation. It permits at most `1e-6` local-scale
residual, restores the checkpoint scale and exact target material, and then
runs strict verification both before and after `Helpers.Resume`. This is a
cosmetic presentation restore; no Collider, Rigidbody, Animator, server sync,
or client sync component is admitted below the replaced container.

The clean single proof and two additional unwind-only cycles all pass exact
restored f444 and exact f447 endpoint equality for framework entities, native
round/food/physics/clocks, input, contact-manager free-list order,
TransformChangeDispatch, and Animator semantics. No Rigidbody actor was
rebuilt. Delivery r9 reports three verified inverse receipts and three exact
resume-presentation restorations with no pending state or failure. Evidence
is under
`artifacts/framework-migration/story11-delivery-fade-r1-host-r9/`, primarily
`delivery-r9-body-r31-animator-r53b-resume-r1bc-f444-to447-r1/summary.json`,
`delivery-r9-body-r31-animator-r53b-resume-r1bc-f444-repeat2-r1/summary.json`,
and `delivery-r9-status-after-repeat2-r1.json`.

This proves repeated unwind while the delivered plate and delivery iterator
are still live. It does not yet prove resurrection after the fade destroys
the plate. That is the next delivery boundary to exercise. A real
dash-detach/plate-throw collision remains separate required coverage. Route
search stays disabled.

## 2026-09-13: abandoned-future registry evidence now rebranches exactly

A later long-route test exposed a controller-only parity defect after an exact
far rewind. The original f1047→1090 pickup retired returned-stack proxy 56 and
recorded an `observedProxyRetirements` receipt at f1090. Rewinding from f1500
to f444 correctly rebuilt and replayed the native world, but the receipt from
the abandoned future survived because the old cleanup considered only IDs
freshly registered at the target. On the second pickup, that stale receipt
suppressed the new absence observation and graph validation failed even though
entities, native round/food/clocks, normalized physics, and exact input all
matched.

`HeadlessSession.RebranchProxyRetirements` now runs only after a verified warp.
It removes a controller-owned receipt when `receiptFrame > targetFrame` or the
same ID is freshly registered at the target, while retaining historical
receipts on the surviving branch. It publishes detailed telemetry and
explicitly reports `nativeStateChanged=false`; it does not alter game state,
physics, synchronizers, or input. The focused DynamicWarp matrix proves
retention of `52@200` and `54@283`, abandonment of `56@1090`, and
target-reincarnation removal of ID 58. The full Headless suite passes 369
checks.

The clean live sequence under
`artifacts/framework-migration/story11-proxy-rebranch-v12q8-live-r3/` passed:
f444→1047 delivery, f1047→1090 returned-plate pickup, held dash to f1122,
dash-drop to f1198, neutral settle to f1500, far f1500→444 rewind plus replay
to f1047, and the decisive second pickup to f1090. The far-rebranch receipt
discarded `56@1090` with `abandonedFuture=true`, retained two historical
receipts, and the second pickup published a fresh `observe-absent(56)`. The
second pickup achieved the requested lifecycle on original and replay and was
exact for reconstructed entities, native round, food, clocks, normalized
native physics, and input SHA
`345305c60fc756953d3427710f517606eba5f9ed5c95c224484e2aa533aaed08`.

This closes the known post-far returned-plate registry failure without changing
in-game behavior. Together with earlier milestones, the far route composes
already-proved post-fade plate resurrection, returned-stack recreation, the
representative Animator transition matrix, and genuine plate-throw physics.
It still does not establish complete level parity. Search remains disabled
while coverage expands into long order/timer evolution and expiry, multiple
deliveries and repeated dynamic-ID lifecycles, multi-chef interactions, and
round end.

## 2026-09-13: natural terminal lifecycle now rewinds exactly

The pristine Story 1-1 `InLevel -> RunLevelOutro` edge is no longer an open
rewind gap. `NativeRoundEndLatch` is an explicit local-authoring latch in the
frozen core. It retains the live server `RunRound` and scheduled client
`RunLevel` iterators plus a dormant client `RunLevelEnd` iterator, defers only
the armed terminal deactivation, and pauses the exact callback. Its restore
reinstalls the captured lifecycle/iterator state before ordinary resume without
calling `ChangeGameState`, starting coroutines, or manually advancing them.
Unarmed forward play is unchanged, and any latch invariant failure freezes the
flow and requires a process restart.

The clean live proof is
`artifacts/framework-migration/story11-round-end-latch-dev5-live-r7/terminal-parity-f8492-r1/summary.json`
(SHA256
`9CF59DF61FFC7667B9877E9201BA9A53DE5C8BC93CA3C741C1A8ABD8B19498F9`).
It restored f8492 exactly, then reproduced the natural f8999 terminal after 507
frames on both branches. Exact comparisons passed for reconstructed entities
and registry, raw terminal receipt, lifecycle and iterator PCs, native
round/orders, food, normalized physics, and native/logical/timer clocks. The
contact-manager free-list and TransformChangeDispatch sidecars each performed
one exact f8492 capture and one restore before replay. Nonces were 1 then 2.

Pinned binaries for that proof:

- core dev5 SHA256
  `C3E4A874FABDC3D232521972B1597330D1FE1EC197145E025A4932109BE9717F`;
- headless SHA256
  `01C07B138C64C7A28081B6893E2E009C179FDDE2B1FD257EE8A037C3640870EB`;
- RoundEndCheckpoint dev5 SHA256
  `301213527C163375CA0BE3C292B5A5977A780A62AA8F3D374AC52B00A683781C`.

The prior long-neutral f1090->4692 run also passed 3600 frames exactly while
orders 5 and 6 arrived; summary SHA256
`E24884D929950CED4EDD381B2F1EC33500762AB9C8AA9116605CE8D90D705AE0`.
Together these prove long timer/order evolution and pristine zero-score round
end. Remaining broader coverage is terminal rewind with deliveries and returned
plates, repeated dynamic object lifecycles, and wider multi-chef combinations.
Route search remains disabled until that coverage is satisfactory.

## 2026-09-13: scored Story 1-1 terminal lifecycle is exact

The remaining round-end composition with nonzero score and returned dynamic
objects now passes in one fresh process.  The fixed f1 -> f444 setup and
f444 -> f1047 delivery first replayed exactly.  It awarded 28 points, consumed
order 1, changed the live order queue from `[1,2,3]` to `[2,3,4]`, and created
the returned stack/plate owner-body pairs 55/56 and 57/58.  The prerequisite
summary is
`artifacts/framework-migration/story11-scored-terminal-dev5-live-r1/delivery-f444-to1047-r1/summary.json`
(SHA-256
`94ADC3C9AC67412F78C35B690A2EEF366B4314B58247B440E8166836049A1247`).

An uninterrupted controller-owned warmup then reached f8492.  Its saved round
state had server score 28 and elapsed time 141.559555 seconds; returned body
incarnations 56 and 58 were still present.  Baseline restoration was exact for
frame, entities, round, food, normalized physics, clocks, and both dynamic body
incarnations.  Original and replay both reached `RunLevelOutro` at f8999 after
507 frames.  All terminal comparator fields passed, including registry, raw
latch receipt, lifecycle/iterator state, orders, physics, clocks, and nonce
1 -> 2.  Contact-manager and TransformChangeDispatch histories were captured
at f8492 and restored before replay.

Primary evidence is
`artifacts/framework-migration/story11-scored-terminal-dev5-live-r1/scored-terminal-f8492-r1/summary.json`,
SHA-256
`BCD7E70F257AD8B3D13F12EA7E6C984F34ACB20273ED6C19D7057CDDE851B658`.
The 507-frame terminal input SHA-256 is
`e2d1897f59cc25f692e72896e760b57f9b43af78dd1f3dd0d2e6c5c7d439c25c`.
This uses the same frozen dev5 core, headless host, managed modules, and native
Rigidbody/Animator helpers as the pristine terminal proof; no gameplay or
synchronizer behavior was changed for this result.

The preceding f7957 failure was diagnosed separately and must not be confused
with a terminal bug.  A 300-second warmup timed out while the round was still
`InLevel` with 17.3382721 seconds remaining.  Cleanup imposed a generic bridge
pause after two advancing native states had occupied controller frame 7957.
WorldSyncCache correctly failed closed on the resulting logical-clock mismatch,
and the process-sticky ResumePhase failure made later level reloads appear
stuck at frame 0.  Long probes must use sufficient wall time and only a
controller-owned pause boundary; a timeout/fallback pause is diagnostic state,
not a checkpoint.  Any real `AUTHORING_*_FAILED` result requires whole-process
restart after evidence is preserved.

This closes scored round-end composition with a surviving returned plate/stack.
Complete level parity remains broader: repeat delivery and dynamic-ID cycles,
order expiry/deduction paths, wider multi-chef interaction combinations, and
arbitrary non-adjacent rewinds still need coverage.  Route search stays off.

## 2026-09-13: repeated scored-terminal unwind is exact

The scored f8492 -> f8999 terminal cell is now repeatable without a level
reload.  The extended `framework_input_probe.py` keeps the local round-end
latch held and supports bounded `--terminal-repeats`: after every natural
terminal it restores f8492, proves the exact baseline and native restore,
re-arms the latch, raw-replays 507 frames, and compares the next terminal to
the immediately preceding one.  The latch is cancelled only after the final
comparison.

Primary evidence:
`artifacts/framework-migration/story11-scored-terminal-dev5-live-r2/scored-terminal-f8492-repeat2-r2-reuse/summary.json`,
SHA-256
`E6DA273781CAECDE01B3C117BDD99E5EF9689D41865065E533848B3AA1B9606D`.
The run began from an already restored f8492 checkpoint and intentionally
reused its native contact-manager and TransformChangeDispatch sidecar.  Its
standard original/replay endpoints plus two extra cycles produced four exact
f8999 receipts, nonces 3 -> 6, with score 28, one delivery, returned dynamic
bodies 56/58, and the same 507-frame recording SHA-256
`e2d1897f59cc25f692e72896e760b57f9b43af78dd1f3dd0d2e6c5c7d439c25c`.
Every f8492 restore and f8999 terminal matched entities/registry, round and
orders, food, normalized physics, clocks, lifecycle/iterator PCs, and input.
Animator status remained clean `Record`.  Sidecar captures stayed fixed while
each rewind consumed exactly one contact-pool and Transform restore.

The sibling r1 extended attempt is useful negative evidence, not a rewind
failure.  It restored the first extra f8999 -> f8492 warp exactly, then the
probe made a managed hot-call before restoring the bridge pause/fence.  The
bridge rejected the call without poisoning any authoring module.  The harness
now fences every restored boundary before inspecting/re-arming the latch.  No
gameplay, synchronizer, Animator, physics, or native C++ behavior changed for
this milestone; the focused suite passes 44 tests with one expected skip.

Current open coverage before search: multiple deliveries and repeated dynamic
ID lifecycles, order expiry/deduction, broader two-chef combinations, and
varied arbitrary non-adjacent rewind order.

## 2026-09-18 checkpoint: mixed f444 reincarnations pass preflight

The current deep-rewind cell is the single bounded f1048 -> f444 request; no
route search is running.  f444 requires simultaneous recreation of delivery
plate/body `2/47` and sushi-fish/body `55/56`.  Plate 2 is the initial
attachment in its exact container-detached delivery-fade topology.  Fish 55 is
a dynamic crate spawn whose target reference is logical path `[30,1]` and
whose observed spawn path is `[30,0]`; its native historical ID therefore
cannot be inferred from the path text alone.  f1048 instead contains future
returned-stack owner/body pairs `59/60` and `61/62`, both declared for deletion.

WorldSyncCache r13u now recognizes only that fully authenticated shape.  It
uses captured scheduler/body/prefab identity plus the live entity-30 spawnable
collection, restores all historical allocator and scheduler slots in native
plan order, rebinds the detached plate's recreated container and client/server
parent caches, and defers local-only active pending-rest eligibility until the
actual WarpSpec proves the corresponding owner recreation.  It does not alter
ordinary forward behavior and does not claim online packet parity.  The
synthetic exact transaction and all prior cases pass 201 WorldSync checks.

Pinned WorldSync DLL:
`WorldSyncCache-r13u-mixed-logical-spawn-path-core-bg4-v14`, SHA-256
`97D9DBD1072C6D1827BBEB261432AFF44EA3DCB030C4EBED0472F6B4B3528198`.
Pinned core remains
`A4B50DB0CAF564CFCED075DADEA6109CA14F3A0C2D7C16A9310954D0C7DE960F`.
Delivery r10o is
`A0DD0FCC54E82B7758A8E262FCF27B7CA82F466A74994562ACAE4D169362F71B`;
actor-sidecar r14h is
`206C81591ABA6B9368B3C375CFEEB1E210252C031F20BE8F8116508EBDD8FE8D`.

Live evidence is
`framework/artifacts/live-v43-midfade-f1048-to-f444-r1/summary.json`
(SHA-256
`84D8DB005E45A66192165322A01B3F060F525004D581E558B1BC18B1F0A677E6`).
All advancing leases remained minimized, background-owned, and exact.  The
request passed the new delivery/WorldSync/scheduler gates and stopped at the
next downstream validator, before restore mutation:
`No unambiguous linked chef Animator boundary and resume-ready checkpoint at
output frame 444.`

This does not yet prove that either recreated pair completes live restoration.
Next inspect the exact f444 Animator checkpoint/link lifecycle, then rerun the
same single rewind until the restored boundary and suffix replay are exact.
Search stays disabled.
