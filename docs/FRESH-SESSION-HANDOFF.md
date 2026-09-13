# Fresh-session handoff — 2026-09-08

> **Active rewind result (2026-09-13, logical WorldObject rest clock):**
> the frame-1198 pending returned-plate rest deadline that previously could
> not even begin replay now passes an exact 300-frame neutral
> original/rewind/replay cell.  The failure was not another physics-history
> divergence.  `ServerWorldObjectSynchroniser.GetServerUpdate` used
> `UnityEngine.Time.time` both to stamp an active send and to evaluate the
> later strict `current > last + 1f` rest deadline.  Unity time continues to
> grow while authoring is paused, whereas the TAS logical clock is frozen and
> checkpointed.  Reconstructing the old residual at a much larger Unity-time
> epoch eventually became mathematically impossible because of float ULP
> spacing.
>
> Core c7bn replaces exactly the two declared parameterless
> `GetServerUpdate` `Time.time` reads with
> `UnrealTimePatch.LogicalRealtime`; startup rejects any installed method with
> a different number of reads.  The native strict comparison, reliable-rest
> event, payload, scheduler and client handlers are unchanged.  WorldSyncCache
> r13n now restores `m_LastUnreliableActiveSend` bit-for-bit and performs only
> read-only resume validation; it contains no inverse/rebase arithmetic and no
> resume-time reflected cache write.  Logical-clock validation runs before
> dynamic scheduler/body restoration and missing post-rewind boundary
> evidence fails closed.
>
> The clean process was rebuilt from Story 1-1 frame 1 and replayed without
> search through the established exact route: f1 -> f444, f444 -> f1047
> delivery, f1047 -> f1090 returned-stack pickup, f1090 -> f1122 held dash,
> and f1122 -> f1198 genuine dash-drop.  Every checkpointed cell passed exact
> entities, native round/food/physics/clocks, contact-manager/manifold pool
> history and TransformChangeDispatch restoration.  At f1198 plate 57 was
> loose/dynamic with `sentReliable=false`, `parentChanged=true`, timestamp
> `74.3`, and pending logical residual `0.433334351`.  The authoring pause had
> already separated the clocks to logical `74.86667` versus Unity time
> `860.4167`.  Nevertheless, original and replay both reached f1500 with the
> same recording SHA
> `840c789b05f3e22f9fe82127b45f8746acfed538f1ad8f2e6299ea9f4ffb9ef7`,
> no changed entity IDs, and exact native physics, food, round state and
> logical clocks.  The rewind incremented `authoringClockRestores` 4 -> 5;
> both endpoints had logical time `79.9`, while their unrelated Unity times
> were `865.9501` and `872.0667`.
>
> Follow-on settled-state coverage also passes.  At f1500 plate 57 had
> `sentReliable=true`, `parentChanged=false`, and no pending residual.  The
> 600-payload-frame neutral cell f1500 -> f2102 restored and replayed with no
> changed entity IDs and exact native physics, food, round state and clocks;
> original and replay recording SHA
> `1c4c4e7396eaeae9afaf2220f5909000e44b88605b51fa8282d4f8a17a75f15d`.
> Evidence:
> `artifacts/framework-migration/story11-logical-world-rest-c7bnr2-clean-r1/settled-plate57-neutral-f1500-r1/summary.json`.
>
> Live summary:
> `artifacts/framework-migration/story11-logical-world-rest-c7bnr2-clean-r1/returned-plate57-settle-neutral-f1198-r2/summary.json`,
> SHA-256
> `2FCFACF710A1A70CE538FCDE1890286F28511AB265CB7ABA96F46F86D8BC4FE3`.
> Core DLL:
> `artifacts/framework-build-c7bn-logical-world-rest/SuperchargedPatch.dll`,
> SHA-256
> `6E94D454663FECA6917EDAC70197BB4A963059F9905378648E4DD1D9EC179F12`.
> Managed module:
> `framework-run/modules/WorldSyncCache-r13n-logical-rest-clock-core-c7bnr2/WorldSyncCache.r13n-logical-rest-clock-core-c7bnr2.dll`,
> SHA-256
> `3BF06396EF25431F83ACFD072CCC8C0E59780E15676DEB2E976E00AE44941FF0`.
> The direct core/transpiler/rest-timing harness passes 131 assertions and the
> external WorldSync module/compiled-IL harness passes 185 checks.  The same
> game PID 51196 and host PID 42372 are healthy and paused at f2102; revalidate
> their saved identities before control.  This closes the exercised pending
> and settled rest-deadline cells, not complete Story 1-1 rewind parity.  Search remains
> disabled; continue expanding the checkpoint/continuation matrix.

> **Active rewind result (2026-09-13, PhysX manifold-pool history / API7):**
> the genuine returned-plate dash-drop that previously diverged on its first
> ground contact now has exact endpoint and every-frame rewind parity.  The
> missing state was PhysX 3.3.3's intrusive large-manifold free-list order.
> RigidbodyActorRebuild r13z captures the complete large- and sphere-manifold
> pool orders beside each exact core checkpoint and restores them after the
> contact-manager free stack and before TransformChangeDispatch.  The native
> API7 implementation validates the exact UnityPlayer revision, pool identity,
> list membership/count/uniqueness and writable links before mutation, then
> verifies the restored head and every link.  Sphere-manifold history is also
> supported but was empty in this proof.  This is paused authoring state only;
> ordinary forward game execution, `ServerWorldObjectSynchroniser`, Animator
> attachment motion, and plate-throw physics are unchanged.
>
> A fresh game/controller was rebuilt from Story 1-1 frame 1.  The established
> f1 -> f444 setup, f444 -> f1047 delivery, f1047 -> f1090 returned-stack
> pickup, and f1090 -> f1122 held-dash continuation all passed exact
> rewind/replay before the target test.  The real f1122 -> f1198 input has 74
> payload plus two release frames and recording SHA
> `74dcee7f662d0f2bceb6655e259ba5724573c7c5653ed4ab9aaca4fe179e1cb2`.
> Its summary is
> `artifacts/framework-migration/story11-manifold-pool-r13z-clean-r2/returned-plate57-genuine-dash-drop-f1122-r1/summary.json`,
> SHA-256
> `EF38341568EA86F0EF81A145800544916B0AB14F0EB2001F32834E45E8C9AE4D`.
> At f1122 the large pool contained 32 free manifolds.  Original execution
> changed its order hash from saved `0x84E3F565` to `0x063950A5`; rewind
> restored it exactly to `0x84E3F565`.  The sphere pool was empty with hash
> `0x811C9DC5` on both sides.
>
> The stronger offline comparison covers every advancing frame in
> `(1122,1198]`: native physics, all four chefs, phase counters, exact input,
> raw messages, and decoded native/auxiliary message sequences are all equal.
> Report:
> `framework/artifacts/story11-manifold-pool-r13z-live-r1/per-frame-parity-r4.json`,
> SHA-256
> `E40E2B1C166DD20F3B316DFEFD009D45E8513751FB4588F777A77DF2F52F4EA1`;
> closed trace SHA-256
> `40551CC4E55540F7D7F75ED3B705BC01DD96EC0C445EA51F87800EDC45B9B6B9`.
> The trace was cropped after line 3854 solely to avoid an older bulk-destroy
> message whose registry decoder is not implemented; the cropped proof still
> requires complete checkpoint bodies/chefs and exact per-frame body, chef,
> input, phase, and message equality.  It waives only registry-membership
> reconstruction from rows preceding the crop.
>
> Native DLL:
> `artifacts/native-rigidbody-rebuild-v7-ninja/Oc2NativeRigidbodyRebuild.dll`,
> SHA-256
> `2E6284C6380B0085853C2240D09044EE266FC7D8415442E731B529C38910A35D`.
> Managed r13z DLL:
> `framework-run/modules/RigidbodyActorRebuild-r13z-manifold-pool-history-core-c7bm/RigidbodyActorRebuild.r13z-manifold-pool-history-core-c7bm.dll`,
> SHA-256
> `7275B04E5BF578519052EFC174EEF3A08DB580D02A92FF846FDDE5C09EFA49D2`.
> Native caller-owned-history harness and all comparer tests pass.  The healthy
> process is paused at f1198: game PID 67872 / start ticks
> 639248829021662289; Story11 host PID 82160 / start ticks
> 639248835076944530.  Revalidate identities before control.  Search remains
> disabled; continue expanding rewind parity beyond this exercised throw.

> **Active rewind result (2026-09-13, controller v12q7 / retired plate-return
> stack reference):** the post-pickup returned-plate continuation that
> previously failed rewind preflight now passes exact restore and replay.  The
> controller had retained station 34's `plateReturnStationStack -> [34,0]`
> after pickup destroyed returned stack owner 55/body 56 and moved child plate
> 57/body 58 to chef 44.  Native `ServerPlateReturnStation.OnItemRemoved`
> clears the corresponding `m_stack`; the controller now mirrors that exact
> lifecycle when processing the attachment-removal message, has an exact-record
> destruction fail-safe, and serializes old/imported target-absent stale stack
> history as null.  Strict native dangling-reference rejection is unchanged.
> The change affects controller observation/reconstruction only, not forward
> game execution, `ServerWorldObjectSynchroniser`, held-item animation, or
> plate-throw physics.
>
> Offline gates for the frozen v12q7 artifact pass DynamicWarp 53, full
> Headless 367, Story11 89, and Status 32 checks.  The exact f444 -> f1047
> delivery was first re-established with recording SHA
> `c91fe72f0376f51e3b826761c86b2550b6d97302ebaf13bad61230eae3eb5141`;
> summary SHA-256
> `22ED2ED66C664B1311FD784691D513E8CE1805A9512A9B57D9B807A6FFAE4183`.
> The exact f1047 -> f1090 returned-stack pickup then passed with recording SHA
> `345305c60fc756953d3427710f517606eba5f9ed5c95c224484e2aa533aaed08`;
> summary SHA-256
> `119624BB1F2C634513F1CEFF7DD8BCC83F84CB31F374C05F166D880DCBFC0BF3`.
> Finally, the formerly failing f1090 -> f1122 continuation passed exact
> entities, native round/food/physics/clocks, contact-manager free-stack,
> TransformChangeDispatch, and input replay.  Its recording SHA is
> `deeacfb089af4f918ba356b0a67a2d21aa184caa4ddb14367e1310e196a448d6`;
> evidence is
> `artifacts/framework-migration/story11-postreturn-pickup-r13k-v12q7-live-r2/returned-plate57-dash-drop-west-f1090-r3/summary.json`,
> SHA-256
> `2D622C88878BC85A68CB0878D376D9F2ADD1320132172B76F59F35DA27BB743C`.
> This input dashed while the plate remained held; it is a held-dash parity
> proof, not a genuine plate throw.
>
> Frozen host v12q7 is
> `artifacts/framework-headless-host-v12q7-station-stack-lifecycle/Headless.dll`,
> SHA-256
> `8042E2D716B3DF0C38A415BD7FBCA8A6DEB5A503D7E901B450B08F5B25769368`.
> The healthy process remains paused/fenced at f1122: game PID 30980 / start
> ticks 639248775946932891, Story11 host PID 76388 / start ticks
> 639248777971150961.  Revalidate both identities before control.  Search
> remains disabled.  Next adjust fixed pickup/drop timing to exercise a genuine
> returned-plate dash-drop/plate-throw, then continue broader rewind parity.

> **Active rewind result (2026-09-12, returned-stack pickup / WorldSync
> r13k):** rewinding across destruction of the first returned
> `CleanPlateStack` now passes exact checkpoint restoration and fixed-input
> replay repeatedly in one game process.  At f1047, stack owner 55/body proxy
> 56 contains plate 57/body proxy 58 under station 34.  The recorded 41-frame
> pickup payload plus two release frames destroys 55/56 and attaches plate 57
> to chef 44 at f1090.  A rewind recreates the exact stack prefab, rebinds the
> surviving plate to its exact nested attachment transform, restores registry
> and physics canonical ordering, and the same input destroys/picks it up again.
>
> r13i proved that the saved plate parent is not the stack root: stack 55 is
> rooted at Unity object `-33254`, while the plate's parent is nested transform
> `-33278`.  r13j captured and restored that exact sibling-index path and
> component shape, eliminating the topology rejection, but exposed one final
> native-physics ordering difference (`...,56,58` became `...,58,56`).  r13k
> captures the exact `EntitySerialisationRegistry.m_EntitiesList` and
> `ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms`
> order.  Its zero-deletion preflight proves the current lists are the
> checkpoint lists minus only the authorized destroyed owner/body, then exact
> recreation replaces those historical entries at their checkpoint indices.
> No surviving Rigidbody actor is rebuilt.
>
> The first full f1047 -> f1090 -> f1047 -> f1090 proof is
> `artifacts/framework-migration/story11-postreturn-pickup-r13k-live-r1/returned-stack-pickup-f1047-r1/summary.json`
> (SHA-256
> `5C66CAE66894B3394631216B8918D1E8FDE5116DA7CBD7D2BD87D682E1ED1F18`).
> Its recording SHA is
> `345305c60fc756953d3427710f517606eba5f9ed5c95c224484e2aa533aaed08`.
> WorldSync r13k is
> `framework-run/modules/WorldSyncCache-r13k-stack-canonical-order-core-c7bm/WorldSyncCache.r13k-stack-canonical-order-core-c7bm.dll`,
> SHA-256
> `646612E3DE1E8CFEACBE83CBB8728B73EA6AF1761ABF9C0584E3F193676B6ACF`.
> Its offline fixture passes 170 checks; report SHA-256
> `A721D93C903F922522E60148CB071E0B7B5304CFFADA4C40CB896ED7908F3621`.
> The adjacent gates pass: DynamicWarp 92, initial-attachment collider 25,
> and Python registry evidence 9.
>
> The first repeat harness attempt completed one additional rewind/replay but
> omitted the main probe's observation-only proxy retirement after pickup, so
> the next controller warp rejected stale registry ID 56 before native
> mutation.  The harness now uses the same `RegistryEvidence` protocol after
> every replay and can recover an interrupted live process from the exact last
> restored proxy identity.  The recovery and two further same-process
> rewind/replays both pass, recreating body 56 as `-37736` and `-38460`, while
> body 58 remains `-36152`.  Every checkpoint and endpoint comparison is exact;
> both pickup contracts pass; contact-pool restores advance exactly; Animator
> returns to quiescent Record mode; actor rebuilds remain zero.  Evidence:
> `artifacts/framework-migration/story11-postreturn-pickup-r13k-live-r1/returned-stack-pickup-f1047-repeat2-r2/summary.json`,
> SHA-256
> `385A7D6650B3357BCB0FA2E3D8ADD09056A034A5FCED9491D6A5E8C9D59439A1`.
>
> The healthy game remains paused at f1090: Overcooked2 PID 12944, start
> 2026-09-12 20:46:33 local; host PID 35448, start 20:47:19.  No authoring
> failure overlay occurred.  Continue parity expansion rather than route
> search.  After any `AUTHORING_WARP_FAILED` or
> `AUTHORING_RESUME_PHASE_FAILED`, preserve diagnostics and restart the whole
> game process because the overlay/error state survives a level reload.
>
> **Repository rule:** the Supercharged Git root remains `framework/`,
> preserving its original paths and history.  The previously adjacent source
> trees now live inside it: `native/`, `scripts/`, `docs/`, `planner/`,
> `tas-plugin/`, `routes/`, `tests/`, and `experiments/`.  Workspace-root
> compatibility junctions preserve the existing commands and evidence paths.
> Starting with this milestone, every demonstrated milestone must receive a
> local commit.  Generated runtime/evidence trees and external PhysX source
> clones remain outside or ignored; source, native C++ build inputs, fixtures,
> and concise evidence hashes must be committed.

> **Active rewind result (2026-09-12, core c7bm / controller v12q4 /
> DeliveryFade r10k):** target-absent post-return checkpoints and mixed
> non-adjacent destroyed-root rewinds now compose repeatedly in one fresh
> process.  The exact f444 -> f1047 delivery path first passes with +28 score,
> orders `[1,2,3] -> [2,3,4]`, input SHA
> `c91fe72f0376f51e3b826761c86b2550b6d97302ebaf13bad61230eae3eb5141`,
> exact reconstructed entities/food/native round/native physics/native clocks,
> contact-manager LIFO, TransformChangeDispatch, and Animator semantics.  The
> Story11 controller now marks returned `CleanPlateStack` entity 55 as a real
> stack and records plate child 57; this is reconstruction/warp metadata only.
>
> Two f1047 -> f1109 neutral rewind/replays then pass exactly.  Each native
> receipt reports `ignoredAbsentInitialBodyIds:[47]`: initial body 47 is absent
> from the f1047 target, while returned bodies 56/58 remain exact and no native
> recreation occurs.  In both replays chef 44 follows the same physical
> staircase from approximately 0 to 0.05, closing the original target-absent
> 0/0.05 comparison for this topology.
>
> An initial composed f1109 -> f444 attempt failed before mutation because
> DeliveryFade r10h had lost its f444 sidecar.  Evidence in
> `artifacts/framework-migration/story11-posttopology-c7bm-stack-live-r3/nonadjacent-f1109-to444-to1047-r1/`
> proves the core/contact sidecars still existed while DeliveryFade retained
> only 1047..1109.  Root cause: r10h tracked a restore only when delivery itself
> needed mutation.  The f1047 restore needed none, so its successful native
> completion did not prune the abandoned future or lower DeliveryFade's
> `lastFrame`; the next f1047 capture cleared the complete older history.
> The failed game/host pair was identity-checked and stopped.
>
> DeliveryFade r10k now associates every exact sidecar-authorized Prepare with
> its exact native `RestorePlan`, independently of a delivery mutation
> candidate.  Only after successful `Complete` and snapshot-identity proof does
> it prune frames newer than the target; delivery object/presentation mutation
> and `restores` remain conditional.  All prepare/complete/failure/clear paths
> clear the temporary association.  Forward delivery code and gameplay state
> are unchanged.  The pure delivery contract remains 38/38.  r10k artifact:
> `framework-run/modules/DeliveryFadeCheckpoint-r10k-persistent-backward-history-core-c7bm/DeliveryFadeCheckpoint.r10k-persistent-backward-history-core-c7bm.dll`,
> SHA-256
> `75E17D160A3E317B244C42C48957157E7679DA5EFDB0B41603618D969E4839DC`.
>
> With r10k, DeliveryFade retains f444 after each f1047 rewind.  Two successive
> mixed f1109 -> f444 -> f1047 cycles pass exact baselines and endpoints,
> recreate historical owner/body 2/47, retire future owners 55/57 and bodies
> 56/58, restore allocator/dispatch state, and finish with zero actor rebuilds.
> Both Animator branch commits are exact and mutation-free.  Final module
> status reports WorldSync restores=5/rejected=0/error empty; Animator
> restores=5, Record mode, no failure/resume failure; DeliveryFade
> historyRestores=5, mutation restores=3, frames 0..1047 including 444, no
> pending transaction/failure.  Primary evidence:
> `artifacts/framework-migration/story11-posttopology-c7bm-stack-r10k-live-r1/postfade-f444-to1047-r1/summary.json`,
> siblings `absent-target-f1047-to1109-r1/summary.json`,
> `nonadjacent-f1109-to444-to1047-r1/summary.json`,
> `absent-target-f1047-to1109-repeat2-r1/summary.json`, and
> `nonadjacent-f1109-to444-to1047-repeat2-r1/summary.json`.
>
> Core c7bm SHA-256 is
> `BD2E1530A3023512A75CBDC442D7899333FE9CABBDE62C79FC5D8197A2331CDD`;
> host v12q4 SHA-256 is
> `361422EACF8387002459D8EE73A26005B0400AC03AE3CCCFBA2AAA8402C5E7C6`.
> After the successful evidence above, a separate exploratory returned-stack
> pickup was reset manually to f1047 and then advanced with different inputs
> while Animator remained `ReplayActive`.  Its reference callback stream
> correctly faulted at f1061 (`expected=25`, `actual=24`); a later reset was
> rejected before mutation.  This was an invalid branch-authoring sequence,
> not a failure of either passing replay.  Receipts are
> `animator-status-after-reset-failure.json` and
> `inspect-after-reset-timeout.json` in the same evidence root.  Game PID
> 68984 and host PID 55920 were identity-checked and stopped, so no process is
> currently live.  A divergent branch after manual warp must explicitly commit
> the checkpoint prefix/switch Animator back to Record before new input, or be
> produced directly as an input probe's original branch.  Search remains
> disabled.  The passing evidence proves the exercised first-delivery/returned-
> stack topology, not arbitrary future gameplay; next broaden post-return plate
> manipulation and genuine dash-drop/plate-throw rewind coverage.

> **Active rewind result (2026-09-12, core c7bj / f1047 -> f444
> future-return deletion):** the long destroyed-root rewind now passes three
> times in one fresh process.  The c7bh attempt had already passed the
> scheduler, delivery, registry, ingredient, and initial-root reconstruction
> proofs, then failed after mutation at
> `Native restored physics membership cardinality differs.`  Native IL and the
> live memberships explain it exactly: the f444
> `ServerPhysicsObjectSynchroniser` canonical list contains container IDs
> `[47,48,49,50]`; f1047 contains `[48,49,50,56,58]`; recreating historical
> body 47 appends its row, while native deletion unregisters returned bodies 56
> and 58 synchronously but leaves their two canonical rows until Unity's
> deferred `OnDestroy`.  The failed c7bh process was identity-checked and
> stopped.  Evidence:
> `artifacts/framework-migration/story11-postfade-c7bh-r10h-v12q2-return-tree-story-r1/postfade-f444-to1047-neutral600-r1/summary.json`.
>
> c7bj retires only those residual physics rows during the already authorized
> paused deletion transaction.  Before any canonical-list mutation it proves
> each future owner/container and its physics entry/transform independently
> unique, proves that removing only present authorized residuals leaves the
> exact checkpoint membership, removes all residuals highest-index-first, then
> verifies checkpoint membership and restores checkpoint order.  A later
> ordinary `OnDestroy` is therefore a remove-if-found no-op.  Unknown, missing,
> aliased, or duplicate rows fail closed; ordinary forward destruction and
> ServerWorldObjectSynchroniser gameplay paths are unchanged.  The reusable
> helper has two-row and mutation-free rejection fixtures.  Offline gates pass
> DynamicWarp 88, WorldSync 141, DeliveryFade 38, and initial-attachment
> collider 23 checks.
>
> The first complete c7bj probe and a second complete same-process probe both
> pass f444 -> f1047 original/replay equality: delivery +28, order IDs
> `[1,2,3] -> [2,3,4]`, exact reconstructed entities/food, native round,
> native physics and clocks, input recording SHA
> `c91fe72f0376f51e3b826761c86b2550b6d97302ebaf13bad61230eae3eb5141`,
> contact-manager LIFO, and TransformChangeDispatch.  Between them, a direct
> f1047 -> f444 repeat also verifies.  All three core receipts report future
> owners `[55,57]`, `retiredFuturePhysicsPairs=2`,
> `alreadyRetiredFuturePhysicsPairs=0`, and `topologyFinalized=true`.
> WorldSync reports three restores, zero rejects, and no error; DeliveryFade
> has three verified discard receipts with exact plate presentation; Animator
> has three verified four-chef restores and no semantic failure fields.
> Primary evidence:
> `artifacts/framework-migration/story11-postfade-c7bj-r10h-v12q2-return-tree-story-r2/postfade-f444-to1047-neutral600-r1/summary.json`,
> sibling `postfade-f444-to1047-neutral600-repeat2-r1/summary.json`, and
> `return-to-f444-repeat2.json`.
>
> Core artifact:
> `artifacts/framework-build-c7bj-deferred-physics-retirement/SuperchargedPatch.dll`,
> SHA-256
> `32BD94BF20889FF3D60ADC702E72EFF46B7B548EAFE4601DC18E4F1DB9B7F63B`.
> The current exact processes are game PID 77156 / UTC start ticks
> 639248525087739418 and Story11 host PID 58944 / UTC start ticks
> 639248526903654138, paused and input-fenced at controller frame 1047 after
> the second full replay.  Revalidate identities before control.  Search
> remains disabled.  One distinct remaining generality gap is checkpoint
> capture after body membership has changed: DeliveryFade's diagnostic
> `failure` records `Native checkpoint is absent for delivery fade frame 1047`
> because the core intentionally stopped admitting frames after the initial
> body set changed.  That did not affect these f444-target restores, but it must
> not be mistaken for qualification of arbitrary post-return checkpoint
> targets.

> **Active rewind investigation (2026-09-12, f1047 -> f444 future return
> trees):** controller v12q2 now validates both returned objects at frame 1047:
> owner/container 55/56 at logical `[34,0]` comes from ordered parent registry
> spawn names, and owner/container 57/58 at `[34,0,0]` comes from the decoded
> native spawn-header receipt.  The previous graph-reconstruction blocker is
> therefore closed.  The required inverse is exactly one historical native
> spawn `[34,0,0] -> [2]` (recreating body 47), plus deletion of future-only
> logical owners 55 and 57 (which natively remove containers 56 and 58).
>
> WorldSync r13c implements that paused transaction.  Its preflight proves each
> deletion is a live registered PhysicalAttachment owner with one live
> container, the pairs exactly equal all scheduler extras, and the current
> free-ID membership equals `(saved - deletion owner/container IDs) + {2,47}`.
> It forces 2/47 for the historical spawn, exact-matches all native removal
> receipts, then requires the deleted pairs absent and restores exact saved
> scheduler/free-queue order.  A one-attempt core capability means the old
> initial-recreation deletion guard relaxes only after this full proof.  Offline
> WorldSync checks pass 141 cases.  DeliveryFade r10h separately requires
> distinct positive deletion owners disjoint from every target plate and
> records `discardedReturnEntityIds`; its pure contract passes 38 checks.  No
> forward gameplay, ServerWorldObjectSynchroniser update, animator-held-item,
> or plate-throw behavior changed.
>
> The first clean c7bg/r13c/r10h live run reproduced the same delivery (+28,
> orders `[1,2,3] -> [2,3,4]`) and returned tree, and passed all earlier
> delivery/scheduler guards.  It then failed before mutation at the next core
> invariant: `NativeInitialAttachmentRecreation.TopologySnapshot.ValidateMissing`
> still expected the entity-registry/ingredient/physics canonical lists to be
> only the f444 survivors and did not admit future pairs 55/56 and 57/58.
> Exact receipt:
> `artifacts/framework-migration/story11-postfade-c7bg-r10h-v12q2-return-tree-story-r1/failure-inspection.json`;
> `lastRestoreFailure.mutationStarted=false`.  The failed game PID 31124 and
> host PID 31788 were identity-checked and stopped.  A core-only follow-up is
> now extending these three canonical-list proofs transactionally: validate
> the exact future-only extras before spawn, retain them until generic native
> deletion, then restore the f444 list orders before final checkpoint
> validation.  Search remains disabled and every authoring failure still
> requires a whole fresh process.
>
> The c7bg candidate used for that diagnostic compiled as SHA-256
> `8FB9A47B847B54C5DDC0B9A4AD6882AA91C483962BC0146C33B1A8A9695EB7EF`;
> rebuild it under a new tag after the canonical-list fix rather than reusing
> the binary.  The deterministic eight-prefix f444 setup can now be replayed
> directly by passing the prior probe's `observations.json` to
> `framework_input_probe.py --prefix-inputs`; the loader extracts only
> contiguous `prefix-N-input` requests.

> **Newest rewind result (2026-09-12, core c7bf / BodyRestore r32 /
> WorldSync r13b):** the destroyed-initial-plate boundary now passes the
> original frame-444 -> 567 replay and seven additional same-checkpoint rewinds
> in one process.  All eight cycles restore and replay exact entities, native
> order/score/food, clocks, native physics state and canonical body order,
> input recording, contact-manager free-list order, TransformChangeDispatch,
> and Animator semantics.  Each inspected native warp receipt names spawn path
> `[34,0,0]`, reports `latentInitialFactory=true`, retires the intermediate
> stack physics pair, and verifies owner/body IDs 2/47.  BodyRestore records
> eight verified two-collider native PxShape geometry/pose rebinds on chef 44,
> plus eight exact ID-47 mass-frame restores.  WorldSync records eight
> verified restores, zero rejection and no error.  Primary evidence:
> `artifacts/framework-migration/story11-postfade-c7bf-r10g-v12p-body-r32-plate-reincarnation-story-r1/postfade-f444-to567-r1/summary.json`,
> siblings `postfade-f444-to567-repeat2-r1/summary.json` and
> `postfade-f444-to567-repeat5-r1/summary.json`,
> `bridge-status-after-repeat-success.json`, and
> `module-status-after-repeat8-success.json`.
>
> Three independent authoring/bookkeeping defects were separated.  First,
> `NativeDynamicWarpPlan.Observe` used `Distinct()` on component types and
> collapsed the plate's two serialized `BoxCollider` rows; removing it changes
> observation metadata only.  Second, the first rewind's real spawns populated
> generic profiles marked `LatentInitialFactory=false`; repeat rewind then
> preferred those profiles, skipped synchronous intermediate physics-pair
> retirement, and produced c7bc's proved actual canonical order
> `[48,49,50,56,47]` instead of `[48,49,50,47]`.  The exact qualified initial
> root now always uses its already validated authoritative two-stage factory;
> other spawns still require observed profiles.  Third, that correction exposed
> a pre-mutation c7be failure: topology rebind transferred registry, ingredient,
> physics and attachment identities but left `PlateRecord.Entry` on the
> destroyed prior incarnation.  c7bf transfers only that record's Entry to the
> exact freshly rebound owner, preserving all immutable component, collider and
> asset signatures.  No ServerWorldObjectSynchroniser gameplay logic was added
> or relaxed; WorldSync remains r13b.  Offline gates pass DynamicWarp 83,
> WorldSync 131, native shape 13, and initial-attachment collider 23 checks.
> c7bf core SHA-256 is
> `E4F888814E499B5562B43B308F6BA434826B99B9348998EEA71AE82921219178`.
> Game PID 77368 and host PID 68712 are paused/input-fenced at frame 567;
> revalidate before control.  Search remains disabled.  Next broaden destroyed-
> root/non-adjacent repeat coverage before considering route search.

> **Newest rewind result (2026-09-12, DeliveryFade r9 / Animator r53b /
> ResumePhase r1bc / BodyRestore r31):** the first delivered-plate boundary is
> now exact for repeated unwind without reloading the level. The second r5
> cycle did not fail because of physics or renderer traversal order:
> `ClientAttachedOrderCosmeticDecisions` deliberately destroyed and rebuilt
> its physics-free `CompositeSushi(Clone)` presentation. A retained-target
> inspector proved the replacement matched owner, hierarchy, mesh, shader,
> activity, layer, and absence of Collider/Rigidbody/Animator/world-sync
> components. Its sole structural residual was local scale `(1,1,1)` versus
> `(1.00000012,1,1.00000012)`, plus the expected fresh material identity.
> DeliveryFade r9 admits only this proved one-renderer reincarnation and a
> bounded `1e-6` scale residual, then writes the checkpoint scale and exact
> target material onto the replacement and verifies both before and after the
> original resume. It does not mutate physics components.
>
> A clean f444 -> f447 delivery and two additional same-checkpoint replays now
> pass exact entities, order/score/food, native physics and clocks, input,
> contact-manager LIFO, TransformChangeDispatch, and Animator semantics, with
> zero actor rebuilds. All three delivery inverse receipts destroyed their
> owned fade materials/PFX and report `platePresentationExact=true`; all three
> resume-presentation receipts verified before and after resume. Evidence:
> `artifacts/framework-migration/story11-delivery-fade-r1-host-r9/delivery-r9-body-r31-animator-r53b-resume-r1bc-f444-to447-r1/summary.json`,
> sibling `...f444-repeat2-r1/summary.json`, and
> `delivery-r9-status-after-repeat2-r1.json`. r9 SHA-256 is
> `D436AB64AC3AB26A1307B1CF9E58A6DCFEB284C3EF6A9730F4790EDF19039B0C`.
> Game PID 75372 and host PID 29956 are paused/input-fenced at frame 447.
> Search remains disabled. The next delivery-lifecycle gap is rewind after the
> fade has destroyed the delivered plate, rather than while its iterator and
> stable plate object are still live; genuine dash-drop/plate-throw physics is
> also still an explicit coverage requirement.

> **Newest rewind result (2026-09-12, Animator r52 / BodyRestore r26):**
> both branch composability and the reverse prepared-food attachment topology
> now pass. Animator r52 replaces the invalid paused hot-call use of
> `Server.CurrentFrameData.FrameNumber` (observed as 0 at controller output
> frame 487) with a caller-supplied paused output frame and exact retained-
> history/reference checks. A direct 789 -> 425 -> 487 replay committed its
> exact 62-frame prefix, discarded the 302-frame abandoned future and callback
> tail, performed no game-state mutation, returned to Record mode, and then
> passed another 425 -> 487 rewind/replay without a level reload. Evidence:
> `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r52-r13y-r12-nonadjacent-789-to425-to487-prefix-commit-r1/summary.json`.
>
> The opposite loose-489 -> held-546 pickup initially found a legitimate
> non-root collider-transform scale preimage difference on surviving dynamic
> body 54. BodyRestore r26 keeps Rigidbody-root scale exact, but restores the
> checkpointed scale on the exact surviving child collider hierarchy. The
> measured write was `(1,1,1)` -> `(1,1,0.9999999)` on
> `ChoppedSushiFish(Clone)_Rigidbody/ChoppedSushiFish`; managed postconditions
> and native PxShape pose/geometry/order all verified exact. A clean full
> pickup rewind/replay and two more same-checkpoint cycles pass exact entities,
> native food/round/physics/clocks, Animator semantics, input, contact LIFO,
> and TransformChangeDispatch, with zero actor rebuilds. Evidence:
> `.../r52-r26-r13y-r12-loose489-to-held546-r3/summary.json`,
> `.../r52-r26-r13y-r12-loose489-to-held546-repeat-r1/summary.json`, and
> `.../body-r26-status-after-held546-to-loose489-r1.json`. Animator r52 SHA-256
> is `C9393018DAD340C8075B779437C928A54EF80EAFBD43EC87A6601F9080A13A54`;
> BodyRestore r26 SHA-256 is
> `C8D9F69BBD71B3C519A36B16EE9C958100E1B9AE717EC186C7CA2A485FC8568B`.
> Game PID 75372 and host PID 78804 are paused/input-fenced at frame 546.
> The staged rewind-before-first-resume path is also live-proved in
> `.../r52-r26-staged-prefix-commit-frame489-r1.json`: frame 490 was rejected
> with exact no mutation and the staged transaction intact; frame 489 then
> committed all 57 future references and one callback-tail entry, preserved
> the pending exact resume, transitioned to Record on first resume, and
> reproduced the held endpoint exactly. Search remains disabled. Next choose
> a genuinely new gameplay topology for broader rewind coverage.

> **Newest rewind result (2026-09-12, RigidbodyActorRebuild r13y / native
> r12):** true non-adjacent rewind now passes without a level reload. The old
> actor sidecar and native helper each retained only the most recently captured
> contact-pool/Transform-dispatch checkpoint, so capturing frame 487 overwrote
> frame 425 and made a direct frame-789 -> 425 restore unsafe. r13y retains up
> to 20,000 caller-owned sidecars keyed by both frame and the exact private
> `NativeKitchenCheckpoint` snapshot object. Native r12 copies/restores the
> complete free-list order through caller-owned buffers and still requires the
> exact context, free-array address, count, unique membership, writability, and
> element-by-element readback. Scene/context/round changes clear all sidecars;
> successful rewind prunes only the abandoned future; a missing sidecar fails
> in `Prepare` before mutation. The held 425 -> 487 and loose 487 -> 789 cells
> pass under the new pair. With both sidecars resident, two independent direct
> 789 -> 425 rewinds and 425 -> 487 replays pass exact entities, food, native
> round/physics/clocks, Animator semantics, input, contact LIFO, and Transform
> dispatch, with zero actor rebuilds. The second cycle also proves frame-487
> future pruning and re-capture. Primary evidence is
> `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r13y-r12-r50-r17f-r12f-r24h-nonadjacent-frame789-to425-to487-r2-rebranch/summary.json`;
> the adjacent inputs are the sibling `...held-frame425-to487-r3` and
> `...loose-frame487-to789-r2-rebranch` directories. r13y SHA-256 is
> `F2CF6F20F3C443FCB5FF554C43CDB862DC52758470F475D41F4D14C80EA79836`;
> native r12 SHA-256 is
> `E28EA2D8EE26FD3D7E363334A6C9E90D7F9BCE0CD999CFD1F73D51A199FCA25E`.
> Its Win32 negative harness passes array/count/duplicate/membership rejection
> without mutation. Game PID 75372 and host PID 78804 are paused/fenced at
> frame 487 after the second non-adjacent replay. Search remains disabled.

> **Newest rewind result (2026-09-12, WorldSync r12f / BodyRestore
> r24h):** the composed loose prepared-food checkpoint now passes. The exact
> frame-425 held prepared-food dash/drop replay still passes to frame 487.
> Starting from that replayed loose state, a second checkpoint at frame 487
> now rewinds from frame 789 and replays all 302 observed frames exactly.
> WorldSync r12f models the game's actual loose topology: owner 53 is a direct
> child of its surviving registered Rigidbody container 54; server and client
> attachment flags are false; and both WorldObject caches name entity 54. It
> also preserves the observed pending reliable-rest deadline as a relative
> offset. BodyRestore r24h admits the loose item's analytic collider only when
> the exact owner/body/Transform/collider/PxShape incarnation and static
> signature survive; collider-bearing cross-incarnation restore still fails
> before mutation. The live frame-487 -> 789 receipt reports exact entities,
> food, round, native physics, clocks, Animator semantics, input, contact-
> manager LIFO, and TransformChangeDispatch. Evidence:
> `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12f-r24h-composed-loose-prepared-food-frame487-to789-r1/summary.json`.
> The held regression is the adjacent `...r12f-r24h-prepared-food-held-dash-drop-frame425-to487-r1/summary.json`.
> WorldSync r12f SHA-256 is
> `BF67C0D9C773B99444443B78D9A495B40A0219DBFCAB5481073C2F1281217459`;
> BodyRestore r24h SHA-256 is
> `12CD85F6E2820E0B3C0920C1B82A1C4565C4CCADB0DBFB6F6D711A42F4636E03`.
> Offline WorldSync checks pass 126 cases. Game PID 75372 and host PID 78804
> are paused/input-fenced at frame 789; revalidate before control. Search is
> disabled. Next exercise repeated and non-adjacent rewind order without a
> level reload.

> **Newest rewind result (2026-09-12, WorldSync r12b):** the real prepared-
> food held-dash/drop continuation is now exact from frame 425 to 487. The
> original frame-428 scale drift was caused by restoring only the four initial
> `ServerWorldObjectSynchroniser` caches: dynamic prepared owner 53 retained
> its future detached client cache after rewind, so the next ordinary packet
> falsely corrected its parent and scale. r12a added exact, fail-closed dynamic
> server/client cache restoration. A fresh uninterrupted run then exposed one
> independent two-ULP paused-state mismatch: at frame 425 proxy 54 legitimately
> had `Rigidbody.position.z=-6.853491` while its public Transform was
> `-6.85349`; the final frozen maintenance copied the body pose into the
> Transform after restore. r12b retains that exact checkpoint Transform only
> while authoring is paused, verifies the dynamic colliderless container and
> parent incarnation, and proves the complete Rigidbody state is unchanged.
> It performs no advancing-gameplay write. Offline tests pass 117 checks.
> The live probe at
> `artifacts/framework-migration/story11-world-sync-r12a-host-r6/r50-r17f-r12b-prepared-food-held-dash-drop-frame425-to487-r1/summary.json`
> passes exact restored-baseline and replay-endpoint entities, food, round,
> clocks, native physics, Animator semantics, contact-manager order, and
> TransformChangeDispatch. WorldSync r12b SHA-256 is
> `D6EF7A12E515C908451C10B6DB1CA3E20E3A987AD29AB8FE72FCFED9764A1DAC`.
> Game PID 75372 and host PID 78804 are paused/fenced at frame 487; revalidate
> before control. Search remains disabled. Next take a composed checkpoint at
> this replayed loose-prepared-food state, run a long continuation, then stress
> mixed non-adjacent rewinds.

> **Newest rewind result (2026-09-12, Animator r50/native r17f):** the
> previously failing inverse transition-owner case is now exact. At the old
> frame-324 failure, Players 2/3 had 26 physical target Playable nodes versus
> 28 live nodes; every target node existed live, and the two additional live
> nodes were resolver-only controller/layer-mixer nodes removed by the already
> guarded `EndTransition` normalization. Native r17f therefore restores only
> target-matched clocks when the target node set is a strict live subset while
> retaining exact identity, topology, projected-postimage, live-cardinality,
> and final no-plan verification. A fresh frame-177 checkpoint exercised this
> 26-of-28 path for three simultaneous transitions and passed its first replay
> plus three additional same-checkpoint rewinds. The canonical frame-234 plate
> throw (`40a90f...`) and a checkpoint composed from its replayed loose-plate
> frame 266 through 300 neutral frames to 568 also pass exactly. Evidence is
> under
> `artifacts/framework-migration/story11-animator-r49-host-r33/` in
> `r50-r17f-multichef-inverse-transition-frame177-r2/`,
> `r50-r17f-multichef-inverse-transition-frame177-repeat-r2/`,
> `r50-r17f-canonical-plate-throw-frame234-to266-r1/`, and
> `r50-r17f-composed-post-throw-neutral300-frame266-to568-r1/`. Active hashes:
> managed r50 `CE77A5E20A2F0B0963ABF11CACCEE3BED33EDF431CEE05E18E5E780B1E256BA5`,
> native r17f `4E4407C84EB97A2CBCB338433F928E22E7888AB73FDFA17B5AEA5EA09C45430C`,
> ResumePhase r1ba `8429C6B8F9E13AA710048FD6977AE469F0937867879201CC63431EAA3AFB0C54`.
> Game PID 31204 and host PID 54416 are paused/fenced at frame 568; revalidate
> before control. Search remains disabled. Next broaden parity with an actual
> prepared-food attachment transfer/drop and mixed non-adjacent rewinds.

> **Newest rewind result (2026-09-11, v12n dynamic composition):** controller
> v12n fixes two observation/reconstruction defects without writing game
> state. `RealGameSimulator` now mirrors the observed frame-176/177 workstation
> replacement lifecycle instead of checkpointing a destroyed raw item, and
> the registry audit retains an observed consumed-parent receipt while its
> prepared descendant exists at the rewind target. The genuine frame-90->215
> chop/replacement rewind passes; the formerly blocked prepared-food
> frame-215->397 rewind passes; and a checkpoint taken from that replayed frame
> 397 passes another 180-frame future to 579. Exact entities, round, food,
> physics, clocks, contact-pool order, TransformChangeDispatch, and Animator
> semantics all match. Frozen DLL:
> `artifacts/framework-headless-host-v12n-registry-ancestor-lifecycle/Headless.dll`,
> SHA-256 `E50F7650AC505CB0AA27A509EDBF8A2841651D71069A8A038EB6DD9811710B9D`.
> Current game PID 31204 and host PID 86116 are paused/fenced at frame 617;
> revalidate before control. Search remains disabled. Start with
> [`REWIND-PARITY-CONTEXT-SNAPSHOT.md`](REWIND-PARITY-CONTEXT-SNAPSHOT.md).

> **Newest rewind result (2026-09-11, r48):** managed Animator r48/native r17d
> closes the composed-checkpoint ControllerInput false failure. Disassembly
> proves layer `record+0` is a consumed `GotoState` hash when both
> `ActiveGotoState` gates are clear; r48 changes only semantic comparison and
> performs no state write. The formerly failing frame-7->10 then replayed-
> frame-10->42 composition passes. A deliberately constructed four-cell
> lifecycle matrix with genuinely transitioning frames 51/54 passes, as do
> the real frame-234->266 plate throw, a composed 300-frame loose-plate future,
> and two more same-checkpoint rewinds without reload. Start with
> [`REWIND-PARITY-CONTEXT-SNAPSHOT.md`](REWIND-PARITY-CONTEXT-SNAPSHOT.md).
> Current game PID 31204 and host PID 61088 are healthy and paused/fenced at
> frame 568; revalidate before control. Search is still disabled pending
> broader mixed dynamic-food/workable composition stress.

> **Newer rewind snapshot (2026-09-11):** start with
> [`REWIND-PARITY-CONTEXT-SNAPSHOT.md`](REWIND-PARITY-CONTEXT-SNAPSHOT.md), then
> use [`ANIMATOR-REWIND-PARITY.md`](ANIMATOR-REWIND-PARITY.md) for the detailed
> evidence. They record the contact-pool/transform-dispatch repair, the complete
> r16c Animator lifecycle matrix, held-item transition replay, active binaries,
> paused runtime, and next experiment. r16c proves that the last r16b mismatch
> was an out-of-bounds read of allocator metadata after a 0xA0-byte
> `AnimationMixerPlayable`, not hidden Animator state. Where
> these files differ from the older rewind sections below, the newer snapshots
> control.

> **Current validated state (2026-09-11):** managed Animator r43/native r16c
> passes all four representative settled/transitioning rewind cells. A combined
> frame-234 held-transition checkpoint also passes through a genuine mid-dash
> plate drop and five seconds of physics to frame 568, then passes two more
> same-checkpoint rewinds without reloading. RigidbodyActorRebuild r13w clears
> an armed contact-pool/TransformDispatch snapshot when the native scene
> generation changes, preventing an old authoring checkpoint from being
> applied to a new PhysX context. Search is still disabled. The live game PID
> is 48296 and host PID 80160; both were healthy, with the controller paused at
> frame 568. Revalidate identities before control. The next parity target is a
> fixed-input station/workable route with dynamic food replacement, followed by
> mixed non-adjacent rewind stress and only then unwind timing/search.

Read this first. This is the current task state; older README examples and Carnival reports are historical. All relative paths below resolve under **M:\projects\game-test-2**. The previous session was paused at the user's request, not because the objective was achieved. No commits, publication, or final score validation were completed during this handoff.

## Latest rewind investigation update — 2026-09-08 evening

The active milestone is full rewind parity; do **not** run route search yet.
The original-game side experiment requested after this handoff has now run:

- An isolated capsule/private-floor probe inside the shipped Overcooked process
  and global physics scene produced the `0 -> 0.05` contact staircase but was
  bit-exact across a 60-frame rewind. Evidence:
  `artifacts/unity-2017-physics-repro/inprocess-game-summary-v1.json`.
- A read-only 53-method managed trace of the four real chefs reproduced the
  branch mismatch. Every height change occurred in the FixedUpdate-to-Update
  native physics interval; the active managed control/movement methods caused
  zero immediate public-state changes. Evidence:
  `artifacts/unity-2017-physics-repro/managed-mutation-summary-v1.json`.
- The local-only synchronization bypass was active and had suppressed 3,676
  `ServerChefSynchroniser.GetServerUpdate` calls with zero pass-throughs and no
  failure. The real-chef mismatch persists with that outbound path removed.
- The shipped player has an exact GUID/age match to Unity Hub's installed
  non-development 2017.4.8f1 PDB. The binaries differ by one non-physics Mono
  initialization byte plus certificate data; all audited physics target
  regions match. Symbol RVAs and hashes are in
  `artifacts/unity-2017-physics-repro/native-debugger-symbol-audit-v1.json`.

The stronger conclusion is that global engine/project setup is insufficient.
The discriminator follows the real chef actor/component lifecycle or its
actor-specific native history. The next useful run is a symbolized native trace
started **before a fresh Story 1-1 load**, capturing chef actor creation,
insertion, initial pose/kinematic calls, and the first physics step on both
branches. The transient in-process probe and managed tracer were retired. The
game remains paused/fenced at framework frame 936; current game PID 57680 and
host PID 21696 were responding when this update was written. Revalidate both
identities before control.

## User intent and constraints

The original objective was an independently implemented four-chef Carnival of Chaos 3-4 TAS scoring 5,000+, using the local installation. The user subsequently chose their **overcooked-supercharged framework** as the foundation and then switched the active milestone to **four-player main-map Story 1-1** to reach an end-to-end automated search sooner. Carnival and its evidence are preserved for later. Do not revive the earlier score discussion; the user explicitly considers it irrelevant.

The user wants useful automated optimization, eventual high scoring full rounds, and practical rewind with explicitly bounded physics tolerance. They want evidence of full rewind parity and an automated search achievement. They prefer native state inspection and hot-loadable fixes over restarting the game for each bug. They authorize normal level restarts and development rewind, but final score validation must use fresh starts without authoring restoration or position correction. Keep raw differences and distinguish exact state, repeatable exact inputs with state differences, and adaptive replay. Do not call a 0.05 chef-height difference tiny or waive it.

**Latest intentional gameplay modification:** Story 1-1 must start its timer immediately, eliminating free preparation before the first delivery. This is implemented and verified. Keep native duration, work times, recipes, scoring, collisions, and order deadlines. Report this as an immediate-timer category, not an unmodified-game record.

Keep the game **windowed** so the user can use the computer. Current policy is 1280×720, run in background, render target60, logical capture60 with native fixedDeltaTime0.02. Give concise progress updates at least once a minute while actively working. Source, runnable tools, traces, logs, screenshots and replay are wanted; video is not.

## What is actually demonstrated

- Native Story 1-1 loads through the normal session/kitchen pipeline with **four registered local chefs**, scene `s_sushi_1_1`, config `Sushi_1_1S_4P`, duration **150 seconds**.
- Immediate timer proof: `artifacts/story11/timer-120-neutral-b.json`. Exactly120 neutral advancing frames increased native elapsed time by **1.9999986649 seconds**, with zero deliveries/score, no rewind, and `timerSuppressed=false`. Screenshot `framework-run/artifacts/timer-120-neutral-b.png` shows02:28 and score0. Reproduction tool: `scripts/verify_story11_timer.py`. Attempt `...-a.json` was rejected before simulation by an overly narrow empty-graph validator; fixed using the existing empty-action check.
- Native recipe setup uses the actual `ScriptedRoundData`: first six Fish, Fish, Prawn, Fish, Prawn, Fish, then native weighted generation. Seed0 is isolated for this native generator. The scripted prefix advances the native cursor without invented weighted-frequency/RNG changes.
- Native action run `artifacts/framework-migration/story11-first-delivery-b`: chef45 fetched raw fish from crate30 to board31 while chef44 collected clean plate2. All three native transfer predicates completed in **59 advancing frames**. This run did not establish a score or a fresh-start replay achievement.
- `story11-first-delivery-c`: ordinary chopping replaced raw51 with prepared53 `ChoppedSushiFish`, native assembled `SushiFish` UID23600. No progress/duration was modified. Screenshot `framework-run/artifacts/story11-first-chopped.png`.
- Native idle rewind in `artifacts/framework-migration/story11-idle-parity-b` restores the complete observed baseline exactly, but subsequent replay fails the physical tolerance: chef Y differs0 versus about0.05. Full parity is **not** demonstrated.
- Hot modules and controller replacements work with the **same game process**. Existing Carnival-only historical evidence includes a 73→56-frame plate-transfer search and bounded replay checks. Those are separate from Story 1-1; do not present them as current-level achievement or full parity.

**Not yet demonstrated on Story 1-1:** any delivery, any positive score, a completed automated search improvement, repeated meals, plate recycling, complete round, or full rewind parity. The fresh-search and first-delivery runners are implemented but await successful native execution after the pending host fix.

## Paused runtime and ownership

Native installation (read-only/untouched): `K:\trash\Steam\steamapps\common\Overcooked! 2`, build20236421, Unity2017.4.8, x86 Mono. Isolated executable `lab/runtime/Overcooked2.exe`; isolated profile/modules `framework-run`.

At handoff:

- Game PID **38824**, UTC start ticks **639244435115626447**, responding. Permanent frozen coreX SHA256 **391F6265321AE57C7924E723ECB0C3EC3F9F9C91BAADD42AAC2521EC624BD746**, build directory `artifacts/framework-plugin-native-x`.
- Headless host PID **44816**, start ticks **639244873850164555**, responding. Current run **`artifacts/framework-migration/story11-mapped-c`**; current DLL **`artifacts/framework-headless-host-v12c-story11/Headless.dll`**, SHA256 **00191FCC6C365F5820D58808211EE2437BBD82E7BB17D9CA3F4766A0D1133F3B**.
- Current native level is fresh, **frame1**, readyUnityFrame6956310. Host `Paused`, no errors or pending request, no active raw/typed action. Bridge paused, inputBlockedtrue, loadCompletetrue, loadingfalse, lastErrorempty; window1280×720/fullScreenfalse.
- Handoff verification: **`artifacts/story11/handoff-pause.json`**. The pause RPC connection is closed. No root-controlled long-running test remains. Existing host/game intentionally remain open; revalidate PIDs/start ticks before controlling them in a later session.
- Native bridge **17636**, controller HTTP **17637**, framework binary Thrift **14455**. Only **one owning bridge connection** during an experiment. Its disconnect releases inputs and pauses. Keep it open through asynchronous native loading and stepping.

The framework source is `framework/`, .NET10 headless `framework/headless`, permanent Unity/.NET3.5 plugin `framework/patch`, external module sources `framework/modules`. The original `controller/` and `plugin/` pre-framework project are historical.

## Immediate next blocker: v12d host spawn audit

The current native run was loaded by **`artifacts/framework-migration/story11-fresh-prep-a`**, which failed before warmup on graph mapping:

`43,44,45,46 Registration position differs from setup initial position`.

The actual captured fixture is `story11-fresh-prep-a/observations.json`, last `fresh-host` full inspection. All four chefs have exact expected XZ. Early native registration Y is0.0000377893447876 versus reference0.000188946723938, difference about0.000151 >0.0001. Their **corresponding later settled XYZ are identical**, including Y0.049999952. This is an early spawn-phase comparison issue, distinct from the0.05 rewind continuation problem.

The controller agent has implemented a **Story11-only two-phase audit**: strict early registrationXZ, separately recorded earlyY delta, strict corresponding settledXYZ; unchanged0.0001 tolerance and unchanged full3D fixed-station checks. Do not widen all position tolerance or invent native positions.

At handoff, v12d is a **check build, not yet the running or frozen release**:

- `artifacts/framework-headless-host-v12d-story11-check`; checked DLL SHA256 **C3BF1D36B9646F2AACF38F4D4BC03EB8C3189CF2F33AA65E41C898B165600AE3**.
- `artifacts/framework-story11-selftest-v12d-check.json`: agent reports77 checks passed, including actual fresh-prep-a, wrong registrationXZ, wrong settledXYZ, missing settled observation, and fixed-stationY rejection.
- `artifacts/framework-proxy-retirement-v12d-check.json`: verified `ok=true`,18 checks, **69,399 unchanged exchanges /8 controls /zero recorded-input differences**.
- `artifacts/framework-v12d-story11-check-build.txt`

The recorded integration prefix ends before the old failing callback. Its suffix is an **explicitly synthetic codec test built from actual native absence evidence**, not a claimed captured native callback. Preserve this distinction.

The controller agent was interrupted with the parent turn, so no final release receipt was produced. The phase audit is in `framework/headless/CarnivalRegistryAudit.cs`, with pinned startup receipts exposed by `HeadlessSession.cs`. Before native work, finish/freeze v12d if needed and use a new run directory. Do not assume a `framework-headless-host-v12d-story11` release exists just because the check build does. `framework/headless/README.md` documents the normal `dotnet build ... -c Release --no-restore -o <new-directory>` flow; cached NuGet packages are under `framework/.packages`.

## Next native execution sequence

1. Verify or finish v12d release, with the phase-specific fixture checks. Replace only the existing headless host; keep the game open. Example **after a frozen v12d DLL exists**:

   ```powershell
   .\scripts\Restart-FrameworkHost.ps1 -PreviousRun story11-mapped-c -Run story11-mapped-d -ControllerDll artifacts/framework-headless-host-v12d-story11/Headless.dll -Level story11 -SkipLevelRestart
   ```

   The script validates saved PID/start ticks/CIM before stopping our host and records the preserved game identity. CIM may require sandbox escalation; this was previously auto-approved. Use a unique run name if `...-d` now exists.

2. Run the released **fresh preparation search**; it performs its own native loads:

   ```powershell
   python scripts/framework_story11_fresh_search.py --trace artifacts/framework-migration/story11-mapped-d/exchange.jsonl --out artifacts/framework-migration/story11-fresh-prep-b > artifacts/story11/fresh-prep-b.stdout.json
   ```

   Four seed0 native fresh loads measure two chef assignments × walk/dash, then a fifth fresh load replays the selected exact four-pad recording. All start with30 neutral frames. It ranks only **raw ingredient fetch-to-board plus parallel clean-plate pickup**. No rewind or full-route optimization claim. It checks unchanged restore counters and per-round warpCount0; raw body/private-clock differences are retained separately from the exact named preparation-goal projection. The global same-process restore count is already nonzero; this is not final fresh-process score qualification.

   The native load fence precedes graph clearing: preserve old spawn claims until native load retires them, then fresh fixed-only actions-clear, then warmup. V12c has the corresponding CLI-owned old-graph lifecycle fix. Do not pre-clear a graph containing live dynamic native objects.

3. If search and selected replay pass, continue the actual fifth live round through its first delivery:

   ```powershell
   python scripts/framework_story11_search.py --out artifacts/framework-migration/story11-first-delivery-d --trace artifacts/framework-migration/story11-mapped-d/exchange.jsonl --warmup 0 --resume-staged artifacts/framework-migration/story11-fresh-prep-b/selected-replay-cases.json --candidate 0
   ```

   Validate the current live staged proof, not a historical candidate snapshot. `selected-replay-cases.json` contains one case with actual fifth-round clocks; index0 is intentional. The runner now accepts completed raw replay for this resume. Use unique output directories. Capture authoritative ledger and final screenshot after success.

4. On real failures, retain raw inputs and native observations. Fix one demonstrated cause, resume through ordinary legal inputs when the exact current-state proof supports it, or reload. Never patch positions/food/score to obtain a score claim. `--resume-observed` also exists for a proven already-chopped stage; inspect its CLI/provenance contract rather than assuming it accepts arbitrary state.

5. Once first delivery works, a separate `scripts/framework_story11_sequence.py` implements a bounded three-meal forward attempt with native event decoding. It uses original clean plates only. Full150-second operation is still gated on observed plate return/pickup and fixed original plate/proxy retirement support.

6. For rewind diagnosis, reload fresh and run the paired private-field probe below. Do not delay the first forward search indefinitely to solve every rewind feature.

**Important:** old bridge commands `load`/`restart`, `routes/probes/framework-restart-clear.json`, and the default host-restart behavior select **Carnival**. For Story11 use `-Level story11 -SkipLevelRestart` and the explicit `level-session` module pipeline. Never use a one-shot hot-module CLI for asynchronous loading: closing its owning socket prematurely pauses/fences the transition.

## Hot module inventory

All loaded against coreX. Do not overwrite loaded/frozen DLLs; build unique revisions. `framework-run/modules/<revision>/manifest.json` records exact files, entry types and hashes.

| Slot | Current revision | Important behavior |
|---|---|---|
| body-restore | BodyRestore-r5 | Native body restoration strategy; active |
| inspection | Inspection-r1 | Selected read/write fields and bounded method calls; paused/fenced gate |
| resume-phase | ResumePhase-r1c | Resume scheduling helper; active |
| world-sync-cache | WorldSyncCache-r3a | Native sync caches; active; not full parity |
| level-session | LevelSession-r2 | Normal four-local Story11 loading and immediate timer; no activation |
| scripted-round | ScriptedRound-r2 | Actual native scripted prefix/weighted suffix wrapper; active; must remain active while its round exists |
| registry-observer | RegistryObserver-r2 | Publish actual metadata/current absence into framework observation only; no activation |

Notable hashes:

- LevelSession r2: **B7F6036AE6DE85FCF13D9AE7B74546AAECCFA05876F0C38B0ACE821FEA93D4D3**
- ScriptedRound r2: **76B123665E0BB20D1B97AB322C3D64E04DF66C5BB6D385E68400BEDE425F92FE**. Manifests and native receipts are authoritative.
- RegistryObserver r2: **29cf483ba847b3d9aeb255255532abdd8cf7b3b452557122c58c442fb242e432**

`level-session` operation **`load-main-1-1 {"seed":0}`** is the correct native load. `status {}` returns timer/selection receipts. Config mutation is only `m_recipesBeforeTimerStarts:1→0`, with original captured value retained; both native campaign Begin paths verify it. No asset/save modification. Evidence log `framework-run/artifacts/level-session-transitions.jsonl`.

`scripted-round` operations activate/status/validate-current/deactivate. Initial native six manual entries remain exact; post-prefix generation uses native base generator. Frame0 checkpoint state is ambiguous/ineligible; warmup precedes capture. Native proof files `artifacts/story11/scripted-round-r2-{load,validate}.json`, `scripted-r2-reload.json`.

`registry-observer` operations:

- `refresh {"entityId":51}` republishes **current actual** metadata. Raw food registers before `ServerWorkableItem.StartSynchronising` adds its next prefab, so this is needed to see `ChoppedSushiFish`. It must not call whole-scene `NativeSceneMetadata.Refresh()` or change initial capture-ID sets.
- `observe-absent {"entityId":52,"sceneMetadataRefreshes":<current>,"priorBodyInstanceId":<actual prior receipt>}` proves current registry absence and publishes a framework-only retirement. It rejects initial fixed IDs, present/reused bodies, wrong epoch, or missing fence. The proof says `historicalRemovalEventObserved=false`; do not claim it observed a historical RemoveEntry callback or destroyed a Unity object.
- Prior raw51/proxy52 body ID-435166 and epoch28 belong to an old destroyed level. **Do not reuse them.** The automatic observer computes live provenance.

The first-delivery runner's automatic registry helper pauses/fences, pins scene/ready/connection epochs and actual proxy-owner/path/body identity, publishes metadata/absence, checks native food/physics/round/private clocks unchanged, and restores the prior armed policy. This handles dynamic raw/chopped proxies. It does **not** yet handle initial plate-proxy retirement after delivery fade.

## Released Python source and checks

- `scripts/framework_story11_fresh_search.py`: fresh-load search, latest SHA **ecc469fadb2dd140208666760a37c8df8ea38298d474534cf1b28b3afc07bcad**,8 focused checks. Source manifest also hashes the new registry helper.
- `scripts/framework_story11_search.py`: first delivery / staged or observed resume / optional rewind-based search. SHA **632ddb710fbe1dbaddfa77d57c521acd27a061aec0fc3bacd183c284378d14d2**.
- `scripts/framework_story11_registry.py`: automatic observation reconciliation, SHA **430bc70381ec8c81df37d99e9a2c2b7789055825583b3a5ae00b703b45247577**.
- `scripts/framework_story11_planner.py`: observed recipe/geometry selectors, reservations and delivery phases. SHA **5865bc8699d3b636824205ddfb0cd1a83fb52f53821f929363f9f6ee86ce558c**.
- First-dish release passed **29 offline checks**, including captured raw/chopped identities, actual refresh/absence receipts, completed-raw resume, and board clearance.
- `scripts/framework_story11_compare.py`: explicitly scoped state/physics comparison.
- `scripts/framework_story11_delivery_events.py`: native delivery event decoder. NativePlateStation success alone is insufficient; correlate kitchen acceptance, order, plate, station and ledger.
- `scripts/framework_story11_sequence.py`: separate three-meal prototype. Full-round flag stays false until plate recycling is proven.
- `scripts/verify_story11_timer.py`, `scripts/audit_story11_idle_probe.py`, `scripts/extract_unity_scene_geometry.py` are root-owned diagnostic tools.

Recompute hashes if source changes. Offline tests verify contracts, not native achievement. Do not rerun every old suite without a relevant change.

## Native action failures already understood

1. First-delivery-a: timer-policy module status called after input arm; authoring gate rejected it. Fixed ordering: pause/fence, inspect timer policy, arm, warmup, validate cached policy against current native evidence.
2. First-delivery-b: raw fish was incorrectly expected to have assembled composition. Native raw `SushiFish` has null composition and a `ServerWorkableItem`. Fixed with exact crate prefab/spawn path/workable stage proof.
3. First-delivery-c: chopping succeeded, but raw proxy52 was gone natively without a framework retirement observation. RegistryObserver r2 proved absence; old controller crashed treating proxy52 as a logical food ID. **V12c fixes proven known-proxy retirement in callback order**; unknown IDs remain fatal. Actual prefix replay plus synthetic absence suffix tested this. A prepared proxy cannot retire while its owner is still live.
4. V12b had already fixed corner entities18/20/22/23 incorrectly annotated AttachStation and audited all50 component→warp annotations. Chopping estimator corrected eight stages to seven work increments/native max progress1.4.
5. The first-delivery runner now moves the empty chopper to a free adjacent-board approach before the plate helper approaches. Uses ordinary movement and reservations, no position mutation.

## Rewind status and next diagnostic

`story11-idle-parity-b`: baseline61, original endpoint121, restore attempt44 at61. Immediate restore matches complete observed entity/body/food/order/private-clock state. The following60-frame continuation has20 Y-related body-position/world-COM differences: all four chefs0 versus approximately0.05. Logical state/events are exact; the explicit physical bound correctly fails.

More precise source/trace analysis:

- Baseline61 was already atY0. Original GF62→71 rises0→.03999996→.048→.0496→.05; replay stays0.
- Earlier untouched warmup GF32→41 decays.05→.01→.002→0 **before a new restore**.
- Position changes happen in **nine physics steps**; zero-physics GF36/66 keep previous height. Reported velocities and chef LastVelocity stay0. Original/replay60-frame chef state, server messages and phase fields match.
- Actual local component is `ClientOnTheServerChefSynchroniser`, inheriting `ClientWorldObjectSynchroniser`, with `EmptyLerp`. Installed IL shows empty local-server event/resume handlers and EmptyLerp.UpdateLerp. The remote `ClientChefSynchroniser.RunCorrection` hypothesis is disproved for these chefs.
- GroundCast itself cannot move the chef and skips refresh while sleeping. Native controls can call Rigidbody.MovePosition even for zero movement. Contact/grounding state or pending MovePosition target is a hypothesis, not an established cause.

Evidence: `artifacts/story11/height-physics-step-audit.json`, `artifacts/story11/height-source-calls.json`; original trace remains under `story11-idle-parity-b` and the host that recorded it.

Prepared but **not natively run**:

```powershell
python scripts/framework_story11_chef_correction_probe.py --out artifacts/framework-migration/story11-chef-correction-a --warmup 30 --frames 60
```

Use a fresh Story11 level with empty action graph, zero ledger and Inspection-r1. Probe has67 installed-member selectors checked,5 tests. It captures all four chefs before/original/restored/replay with32 read-only operations, pause→settle→inspect→arm symmetry, pinned identities and exact baseline gate. Inspect capsule/ground identity/contactOffset/surface velocity/frozen flags first. Matching visible fields still cannot establish equal hidden PhysX contact caches.

Cross-delivery rewind is a **separate larger gap**: NativeKitchenCheckpoint and WorldSyncCache rely on initial body/attachment membership; original delivered plates/proxies later disappear. Capturing a new checkpoint after proven retirement may be possible within a new epoch with explicit membership receipts. Rewinding across delivery requires recreating the logical plate/proxy and remapping all saved component references. The returned plate prefab sharing a model does not establish equivalence to an original scene plate. Pending delivery fade is unsupported. Do not waive missing objects.

## Actual level map and native food identities

Base DLC-1, directory index1, labelText.Menu.Level01, WorldOne, themeSushi. Initial registry50 objects. Station coordinates below are X,Z; resolve through validated scene properties/path/component proofs, not assumed IDs alone.

- Plates1@(12,-3.6),2@(10.8,-3.6),3@(16.8,-3.6),4@(18,-3.6).
- Counters5..26; corners18/20/22/23 have no AttachStation.
- Boards27@(9.6,-7.2),28@(19.2,-7.2),31@(8.4,-7.2),32@(20.4,-7.2).
- Prawn crate29@(21.6,-4.8); fish crate30@(7.2,-3.6).
- Service33@(21.6,-1.8); clean plate return34@(21.6,-3.6), initially empty. Bin35@(7.2,-1.2).
- Flow40; LimitedQty41; killplanes36..39,42.
- Chef43 Player3@(17.826292,-5.834194);44 Player1@(11.426291,-1.584194);45 Player4@(11.366292,-5.734194);46 Player2@(17.786293,-1.674194).
- Initial proxies47→plate2,48→plate3,49→plate1,50→plate4. Chef early registration Y is transient; settled about.05. PlatesY.5.
- Fish order recipe22294, Prawn32748, base value20 each. Prepared fish UID23600 natively proven. Raw workable8 stages, completes at progress7. No cooking/mixing/washing stations on this level.
- Native scoring: base20 is not combo-multiplied; tip uses prior max(multiplier,1), then ordered combo increments up to4. Correlate native events and ledger rather than an estimated UI score.

Native inspection: `artifacts/story11/native-map-inspection.json`; original registry `artifacts/framework-migration/story11-discovery-b/load.json`. Geometry extraction: `artifacts/story11/extracted-stations.json`; scene source `H:/tiny2/Overcooked2/tinyoc2/Assets/Scene/buildplayer-s_sushi_1_1.unity`. Source polygons have been checked against live anchors; station BoxColliders are triggers and do not prove the entire solid mesh.

## Source/reference access and operational cautions

User-provided decompiled code: **`H:\tiny2\Overcooked2\tinyoc2\Assets\Scripts\Assembly-CSharp`**. Parent is a decompiled Unity project with scene/asset definitions. Read-only. Prefer current installed assembly/IL verification where versions matter. Framework reference is already local; original upstream `hpmv/overcooked-supercharged`, GUA `gua248/Overcooked2-TAS` were technical references, not route copies.

- Read `docs/STORY11-PROGRESS.md`, `docs/FRAMEWORK-STORY11-HOST.md`, `docs/FRAMEWORK-STORY11-SESSION.md`, `docs/FRAMEWORK-STORY11-SCRIPTED-ROUND.md`, `docs/FRAMEWORK-REGISTRY-OBSERVER.md`, `docs/STORY11-AUTOMATION.md` as needed. Older `docs/FRAMEWORK-LIVE-ITERATION.md` contains historical Carnival-specific commands.
- Prefer existing RPC/scripts to GUI automation. Native CUA is unavailable in the current environment. Capture game screenshots via the existing bridge tooling.
- Use `rg --files` to discover paths before reading; Windows rg arguments like `framework/patch/Physics*` are invalid. Avoid guessed paths and repeated giant JSON dumps.
- Errors may embed whole multi-megabyte bridge receipts. Parse JSON and truncate error text for review; preserve full files.
- Use unique output/revision directories. Do not overwrite frozen DLLs/evidence. Do not kill arbitrary dotnet/game processes; validate our saved PID/start ticks.
- Authoring hot calls, including module status, need a paused input-fenced boundary. `arm` removes the input fence while keeping the game paused until a controlled step request.
- .NET helper background processes use `Start-Process -WindowStyle Hidden`; the game stays in windowed mode. No global git safe.directory changes; no destructive filesystem cleanup needed.
- No active scheduled task or goal was created. Prior collaboration agents should not be assumed available in a fresh session. Existing source edits are shared and uncommitted; preserve them.

## Suggested opening prompt for the new session

> Continue the Overcooked2 Story1-1 TAS work in M:\projects\game-test-2. First read docs/FRESH-SESSION-HANDOFF.md and the current v12d release/handoff receipt. Keep the game windowed and retain the immediate150-second timer. Finish the pending phase-specific host fix, run the four-candidate fresh preparation search and selected exact-input replay, then complete a native delivery. Report measured results honestly; full rewind parity and a Story1-1 score are not yet proven. Continue the paired physics probe afterward. Preserve existing evidence and use unique output directories.
