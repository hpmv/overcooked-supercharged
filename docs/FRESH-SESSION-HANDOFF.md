# Fresh-session handoff — 2026-09-08

> **Sleeping targetless kinematic rewind milestone (2026-09-15, BodyRestore
> r44s / native API 11):** the previously failing committed-branch sequence is
> now exact.  Fresh minimized/non-foreground evidence is under
> `artifacts/framework-migration/story11-kinematic-native-v28-live-r1/`.
> `second-delivery-f1045-r44s-exact-r1/summary.json` first proves the known
> f1045 -> f1048 delivery cell unchanged: score 28 -> 56, orders/food/clocks,
> all entities, native physics, Animator semantics, contact-pool order,
> dirty-interaction order, and Transform dispatch all match.  The harness then
> manually rewound to f1045, committed the exact replay prefix, advanced the
> historical 30 neutral frames to f1077, rewound, and replayed.  The decisive
> `neutral-reuse-f1045-r44s-r1/summary.json` passes every one of those same
> comparisons with no changed entity IDs; its SHA-256 is
> `08AE0EC2C0F3C1E84C6EDC1ABB1864C9F7211E32019AEC57AF54DAF9C9B7C79F`.
> Both endpoint focus readings are false.
>
> Entity 49 was a stable, sleeping, targetless kinematic.  r44q's first
> preflight incorrectly compared checkpoint-wide active/sleep/wake queues and
> dirty bitmap words against the middle of the restore pass, although the
> actor-local Rigidbody, actor, BodyCore, BodySim, island, target, sleep, mass,
> and shape state all matched.  r44r removed only that whole-scene admission
> test and retained bit-exact mutation checks.  The next live receipt proved
> that native-first body2World restoration already made Unity's managed
> Transform exact while preserving targetless/asleep state; the old code
> nevertheless required the Transform step to create a synthetic target and
> wake the actor.  r44s admits exactly two guarded outcomes: either storage is
> already exact and the actor remains targetless/asleep, or Unity creates the
> one expected synthetic target and the existing native invalidator clears it.
> Every pose, shape, wake, lifecycle, target, identity, and managed Transform
> postcondition remains strict.
>
> The successful BodyRestore receipt is
> `body-status-after-r44s-neutral-reuse-pass-r1.json`, SHA-256
> `4A80BFF891D50D3C2158A442E2EA96059DF025258B297346D330B6471186A798`.
> It records one exact entity-49 restore at restore call 9 with outcome
> `already-exact-targetless-sleeping`, explicitly skips target invalidation
> because no target exists, and records no invalidation calls.  The retained
> fallback uses the verified PhysX 3.3.3
> `Sc::BodyCore::invalidateKinematicTarget` body at UnityPlayer RVA `0xA3A5A0`
> only for an observed 1 -> 0 synthetic target.  Native helper DLL SHA-256 is
> `E2A1569A586CA8488A110BAECD47865FA31C79F0923FB36FCB395D36AC4302A1`;
> managed r44s DLL SHA-256 is
> `F5913CB566A59703E37E48B91F504259ECC3B9D2839C5B5AC778E2C1BCD44482`.
> The offline BodyRestore suite passes 248 checks.  These paths run only inside
> an explicit paused authoring restore; ordinary forward physics, chef
> animation attachment motion, dash/drop plate throwing, and synchronization
> behavior are unchanged.
>
> The exact fixture is the prior 15 input prefixes plus a 90-frame neutral
> settle.  Omitting that settle reaches f955, not f1045, and exercises a
> different collider-ancestor checkpoint; preserve v26's diagnostic rather
> than treating it as this cell.  At this writing the successful v28 game is
> PID 41192 / UTC start ticks 639251153387417580 and the Story11 host is PID
> 3212 / ticks 639251154025368021, paused and fenced after the f1077 replay.
> Revalidate identities before reuse.  Search remains disabled.  The next
> broader-parity target is the later delivery/destruction boundary whose
> abandoned branch requires the already-known `[34,0,0] -> [2]` historical
> factory transaction, followed by additional non-adjacent/repeated cells.

> **Dirty-interaction projection milestone (2026-09-15, API 11):** the
> checkpoint sidecar can now restore across legitimate alternate branches
> whose live dirty-interaction membership differs.  Projection leaves every
> current-only interaction in its exact live dense-array slot, orders the
> surviving checkpoint interactions by their captured relative order, fills
> only the surviving slots, and rebuilds the live hash buckets/next chains.
> Captured-only interactions are discarded and no stale pooled pointer is
> retained.  Exact mode remains available; projection is explicit and both
> modes fail closed on invalid/duplicate identities or incoherent containers.
> Receipts report the restore mode and matched/captured-only/live-only counts.
>
> Fresh minimized/non-foreground Story 1-1 evidence is
> `artifacts/framework-migration/story11-dirty-projection-v15-live-r3/second-delivery-f1045-projection-exact-r2/`;
> `summary.json` SHA-256 is
> `C2D4CF9DC83ADDB66135326DFF0C3787FCF9C4D3531614E320CAA5F1603A4225`.
> The known f1045 -> f1048 second-delivery cell remains exact: score 28 -> 56,
> no changed entities, chef 46 Y `0.040400088` in both branches, and exact
> native physics, Animator, round/orders, food, and clocks.  The API-11 live
> receipt restored all 23 surviving entries in projection mode, changing the
> order hash from `0xF0F7A028` to checkpoint hash `0x239E7130` exactly once.
>
> The native harness additionally covers mixed add/remove membership at equal,
> larger, and smaller live counts, current-only slot preservation, hash rebuild,
> exact-mode rejection, and one-shot dormancy.  It passes against
> `artifacts/native-rigidbody-rebuild-r18-dirty-projection-cmake1/Oc2NativeRigidbodyRebuild.dll`,
> SHA-256
> `C6E1525C4011B7A2606D5B7CCEE914291BD9B033C3E618235BA06C5FF3470BE3`.
> The managed actor/body DLLs are respectively
> `65B4B6AB90EA329B3F12E134C15BEF7A5254B1D5F126438D7856BC141C1660AD`
> and
> `94FB8C381C485B4B0F7141CC444D839A7EBCC2F3DF8F70596A31F806D5D18C9C`.
> Python input/background suites pass 39 tests total.
>
> A guarded branch transaction at restored f1045 also proved it can discard
> the abandoned three-frame delivery future and one chef RandomizeAnimParam
> callback without changing game state.  The following 30-frame neutral branch
> reached f1077, but its rewind stopped later in BodyRestore because the
> necessary pose restoration woke sleeping kinematic entity 49.  The dirty
> restore had already completed successfully with 23 matched / zero differing
> members, so this branch did not live-exercise the changed-membership path.
> Evidence is sibling `commit-f1045-replay-prefix.json` (SHA-256
> `43E96C53EABC6300DF57B9A206155E14A58ABB0BEB6A913F269FBB020794D5F9`)
> and `neutral-reuse-f1045-projection-r1/summary.json` (SHA-256
> `3C1518E6921CD5AB6F0D7EA11AC006B69076601F389128A633E141768D929401`).
>
> **Scope/next:** the live game proves API-11 exact-membership projection and
> the harness proves the changed-membership algorithm.  Full Story 1-1 parity
> is not claimed.  Next restore entity 49's source-equivalent kinematic sleep
> lifecycle after a required pose write, verify exact post-maintenance island,
> active-list, notification-list, and dirty-interaction state, then obtain a
> live changed-membership branch.  Full-level non-element dirty-interaction
> census and plate-throw/dash coverage remain required.  Search stays disabled.

> **Dirty-interaction order restoration closes the f1045 physics drift
> (2026-09-15, API 10):** the exact Story 1-1 second-delivery acceptance now
> passes under the minimized/background v14 runtime. Evidence is
> `artifacts/framework-migration/story11-dirty-order-v14-api10-live-r2/second-delivery-f1045-dirty-restore-r1/`;
> `summary.json` SHA-256 is
> `DBBB54A349B04F171639C7E3405FCFE619BBC03F16DA686511D6E3CA48957975`.
> The restored f1045 checkpoint is exact, original and replay both perform the
> same 28-to-56 delivery with input SHA-256
> `35867d2f81e4579fd0d02346cfde731058b5fa3d9770ee887c2cda248decc687`,
> and the endpoint is exact for all entities, native physics, Animator state,
> round/orders, food, and clocks. In particular, chef 46 now ends at the same
> Y `0.04039979` in both branches; there are no changed entity IDs.
>
> The actor sidecar captures the checkpoint's ordered 23-entry
> `Sc::NPhaseCore::mDirtyInteractions` list by canonical `Element*` endpoint
> pair, primary vtable, and interaction type. Immediately before the first
> replay `updateDirtyInteractions`, the native one-shot resolves those keys to
> the current pooled `CoreInteraction*` objects, rewrites the dense array, and
> rebuilds the hash buckets and next chains. It does not retain stale pooled
> pointers. The live receipt proves exactly one capture and one restore; the
> replay order changed from `0x95704AA8` back to checkpoint hash `0x4C383350`.
> Capture and restore were both dormant afterward. The contact-manager free
> stack and Transform dispatch sidecars also remained exact.
>
> Native helper API 10 DLL:
> `artifacts/native-rigidbody-rebuild-r17-dirty-order-cmake2/Oc2NativeRigidbodyRebuild.dll`,
> SHA-256
> `87DFD3E0C59D5DF14F70546A0CFCF052E3007B1FB47977BE2FD9BEEE7801E0AB`.
> Managed actor module:
> `framework-run/modules/RigidbodyActorRebuild-r14d-dirty-interaction-order-lifecycle-core-bg4-v14-r44i/`,
> SHA-256
> `A798D0C24522A7193A20BB3CBCAE06EFBDE0F58F194422815113EC87B60C500C`.
> The native harness covers changed backing allocation/capacity/hash storage,
> exact semantic membership, rejection paths, cancellation, and one-shot
> dormancy. Python input/background suites pass 35 and 4 tests.
>
> **Scope/next:** this first restoration mode intentionally requires exact
> dirty-interaction semantic membership. It proves and fixes the known exact
> rewind/replay cell without touching ordinary forward gameplay. Before route
> search, broaden rewind coverage and add a projection mode that preserves the
> captured relative order of surviving checkpoint keys while permitting
> legitimate alternate-input additions/removals. Also audit non-element dirty
> interaction classes before claiming full-level generality. Search remains
> disabled.

> **Dirty-interaction order proved end to end (2026-09-15, native trace
> r22):** fresh v14 live evidence is
> `artifacts/framework-migration/story11-dirty-order-v14-live-r2/second-delivery-f1045-dirty-order-r2/`.
> It reproduces the exact f1045 checkpoint, delivery, input SHA
> `35867d2f81e4579fd0d02346cfde731058b5fa3d9770ee887c2cda248decc687`,
> score/food/orders/clocks and Animator parity. The known residual remains
> native physics only: chef 46 ends at `0.04039979` on the original branch and
> `0.009999692` on replay (plus chef 44's sub-ulp residual velocity).
>
> The new entry probe at shipped `Sc::NPhaseCore::updateDirtyInteractions`
> (RVA `0xA540F0`) records the exact compacting-set header and dense entries;
> the probe at `ShapeInstancePairLL::createManager` (RVA `0xA54430`) and the
> existing contact-manager receipt join the semantic pair through descriptor
> `userData`. The first post-checkpoint simulation has the same NPhaseCore,
> set/buffer/entries/next/hash storage, capacity 48, hash size 64, count 23,
> owner scene and owner flags byte zero in both branches. The exact 23-pointer
> set and typed semantic-pair multiset are also equal, but their dense order is
> different; 21 pooled CoreInteraction prefixes and the pooled SIP-to-semantic
> assignments have evolved differently.
>
> Filtering the dense list to the 15 manager-producing interactions proves
> that its semantic order is exactly the `A57C3A` createManager order in both
> branches. Every immediately following kind-52 receipt has descriptor
> `userData == SIP` (15/15). Both branches consume the exact same restored pool
> sequence `[1,5,10,7,2,14,12,15,13,4,0,11,9,8,6]`, yet the order permutation
> changes six semantic pair-to-slot assignments. Chef 46's capsule contacts
> against static shapes `0x476AA960` and `0x47698910` swap pool 11/pool 9,
> exactly matching the previously observed supporting-constraint swap and
> first native writeback divergence. Thus the restored free stack is correct;
> `mDirtyInteractions` semantic order is the missing upstream state.
>
> Offline analyzer:
> `scripts/analyze_framework_dirty_interaction_trace.py`. Its live analysis
> passes every container, set, descriptor and pool-correlation check and
> reports exactly six changed mappings. Native tracer DLL:
> `artifacts/native-physics-trace-r22-dirty-order-build1/Oc2NativePhysicsTrace.r22.dll`,
> SHA-256
> `96A57839A3A2F5D71B2F7E559184E2799221FF1A2936B959736DCEE5805BA502`;
> the x86 harness passes with mask 229, 22 total hooks, and no error.
>
> **Next:** restore the checkpoint-owned cause of this semantic ordering, not
> a route-specific future allocation map. First inspect whether the paused
> checkpoint already contains the intended dirty semantic order and capture
> the relevant interaction-container/free-pool state that produces it. If a
> one-shot `mDirtyInteractions` rebuild is used, resolve current entries by
> semantic element-pair identity, preserve alternate-input membership, and
> rebuild the hash buckets/next chains plus membership/dirty flags; copying or
> permuting stale raw pointers alone is invalid because pooled interaction
> objects are reassigned across rewind. Then rerun the same f1045 cell for full
> endpoint parity. Search remains disabled.

> **Background-launch hardening (2026-09-15, v14):** the earlier no-activation
> core removed every explicit request to foreground Overcooked, but the launcher
> still used `.NET ProcessWindowStyle.Minimized`, which maps to Windows
> `SW_SHOWMINIMIZED` and is permitted to activate the child. This was exposed
> after a clean relaunch: the window was iconic but remained the OS foreground
> owner, so the strict background primer correctly stopped.
>
> Minimized launches now use `CreateProcessW` with
> `STARTF_USESHOWWINDOW/SW_SHOWMINNOACTIVE`. As a second bounded defense, an
> explicit authoring `window=minimize` request releases foreground ownership to
> the Windows shell only if Unity still owns it after minimization; it never
> changes another application's foreground. No gameplay input or ordinary game
> path calls this authoring-only window control. Fresh PID 8708 launched and
> passed the primer in 0.063 seconds with `foregroundOwned=false`,
> `foregroundReleaseAttempted=false`, and no game activation request. Evidence:
> `framework-run/artifacts/background-prime-8708.json`. Core:
> `artifacts/framework-build-focus-v14-release-foreground/SuperchargedPatch.dll`,
> SHA-256
> `A4B50DB0CAF564CFCED075DADEA6109CA14F3A0C2D7C16A9310954D0C7DE960F`.
> The Story 1-1 parity modules were rebuilt against this exact core. No user
> focus is needed for subsequent tracing or rewind probes.

> **Current native-contact milestone (2026-09-15, pair activation order):**
> the first residual chef-46 physics divergence is now localized to the order
> in which pre-existing PhysX shape interactions recreate contact managers on
> the first simulation after rewind. The valid split-ring evidence is
> `artifacts/framework-migration/story11-native-narrowphase-v13-live-r4/second-delivery-f1045-native-narrowphase-r2/`;
> `summary.json` SHA-256 is
> `E354D662732B834DD4981634D28098BE33C45897BB9B3307A29BB63A52D6AA14`.
> It preserves the exact f1045 checkpoint, identical second delivery, score,
> order/food/clocks, input SHA-256, and Animator semantics. Only chef 46 differs
> at the endpoint (`0.0404009223` original versus `0.0100009441` replay).
>
> Both branches enter the first native simulation with chef 46 at the exact
> same pose and create the same 15 semantic contact pairs. They also pop the
> exact same restored contact-manager free-stack sequence and pool indices:
> `[1,5,10,7,2,14,12,15,13,4,0,11,9,8,6]`. The causal difference is that the
> pair sequence is permuted, so six pairs receive different pool indices.
> Chef 46's two static-box contacts swap pool 9 and pool 11. PhysX subsequently
> traverses active managers by ascending pool index; the supporting constraint
> follows the reassigned slot, and chef 46's first native writeback is already
> different (`0.0100003481` original versus `0.05000025` replay). This rules
> out managed movement order and rules out a defect in the restored LIFO free
> stack by itself.
>
> PhysX 3.3.3 source identifies the leading hidden state as
> `Sc::NPhaseCore::mDirtyInteractions`, a compacting
> `Ps::CoalescedHashSet<CoreInteraction*>`. `updateDirtyInteractions()` walks
> its dense entry array in order; each pair's `updateState()` may call
> `ShapeInstancePairLL::createManager()`, assigning transform-cache IDs and a
> free contact-manager slot in that order. The rewind stack restores the free
> pools but not this dirty-interaction ordering. Active manifold contents may
> still be a later parity issue, but they are downstream of the already-proved
> pair-to-manager permutation.
>
> The diagnostic stack now loads the native trace dormant, hands the live
> `PxsContext` from RigidbodyActorRebuild to the tracer without overlapping
> hooks, then reactivates restoration against the explicit context. The split
> trace harness also restores the bridge input fence before reading the
> warp-only ring, preventing an accidental physics step. Python input-probe
> tests pass 35 checks.
>
> **Next:** resolve the stripped UnityPlayer offset and exact in-memory layout
> of `NPhaseCore::mDirtyInteractions`, then add a read-only entry probe for
> `updateDirtyInteractions()` to prove its dense pointer order is the observed
> contact-manager creation order. If proved, capture this checkpoint-owned
> container order and restore it immediately before the first replay
> `updateDirtyInteractions()` call. Do not force a route-specific future pair
> mapping: alternate post-checkpoint inputs must remain free to change contact
> membership. Search remains disabled.

> **Current physics-localization milestone (2026-09-15, paired ground/force
> trace):** the remaining second-delivery continuation mismatch is now proved
> to begin inside Unity/PhysX simulation rather than captured managed chef
> movement. A fresh no-tracer v13 control reproduced the clean r44i result:
> exact f1045 restoration and exact delivery/Animator/round/food/clocks, followed
> only by chef 46 Y (`0.04039979` original versus `0.009999692` replay) and a
> chef-44 residual Y velocity (`-4.7683716e-7` versus `-2.3841858e-7`). Evidence:
> `artifacts/framework-migration/story11-clean-r44i-v13-control-r2/second-delivery-f1045-clean-control-r1/`.
>
> The first broad tracer disturbed the rewind patch chain and caused an
> entity-49 kinematic-wake guard. That was an observer effect, not a v13/r44i
> regression. The replacement tracer patches only ordinary advancing
> `PlayerControls`, `ClientPlayerControlsImpl_Default`, `RigidbodyMotion`,
> `GroundCast`, and `SurfaceMovable` methods. It never patches authoring warp,
> checkpoint, attachment, synchronizer, or `FrozenPhysicsData` methods. Its
> wrappers stay installed but collection is suspended across rewind, avoiding
> Harmony-chain reconstruction at the restore boundary.
>
> The fresh paired trace is
> `artifacts/framework-migration/story11-ground-force-v5-live-r1/second-delivery-f1045-ground-force-paired-r1/`.
> Original and replay contain the same 188 managed callbacks with identical
> order. Chef 46 is exact through the phase sample immediately after all four
> first-step `PlayerControls.FixedUpdate` calls. At the prefix of the very next
> callback, `RigidbodyMotion.Accelerate`, native simulation/write-back has
> already produced the branch difference: original Y `0.0100002289`, replay Y
> `0.05000007`. At that point the captured previous/local velocity, GroundCast
> collider/point/normal/distance/current, surface velocity, gravity flag,
> leftover time, last velocity, and dash/impact timers are still exact. Later
> managed differences are consequences of the already-different Rigidbody.
> Chef 44's tiny residual likewise first appears across a later native physics
> interval with an exact pre-step state.
>
> The cleaned r5b tracer compiles against the v13 core and passed a complete
> restored-history rewind/replay without changing its exact endpoint. DLL:
> `framework-run/modules/ChefManagedMutationTracer-r5b-ground-force-only-core-bg3-v13-r44i/`,
> SHA-256
> `2FD3315A933D785C3D6A354C31623E8C92577382AE202DE882BBA92D56D03BC1`.
> Its validation is `.../story11-ground-force-v5-live-r1/second-delivery-f1045-r5b-validation-r1/`.
> The input-probe suite passes 35 checks.
>
> **Next:** install the existing read-only native trace before the rewind stack
> with masks for Unity lifecycle, PhysicsManager simulation/write-back, and
> PhysX narrowphase/contact-manager creation. Compare the original and replay
> f1045->f1048 contact work units. If pair creation/order differs, restore the
> responsible broadphase/contact lifecycle. If the same contact managers and
> work units enter narrowphase, instrument the specific chef-floor manifold and
> solver warm-start data using the matched PhysX 3.3.3 source. Search remains
> disabled.

> **Background-input milestone (2026-09-15, no foreground activation):**
> process launch no longer calls `SetForegroundWindow` or asks the user to
> focus Overcooked. Unity 2017 can leave `Application.isFocused=true` when it
> starts minimized even though Windows has never made the game foreground.
> That managed bit is now diagnostic only: the launcher calls only the verified
> process-owned minimize operation, and advancing probes require the window to
> remain minimized and `foregroundOwned=false` before and after every input
> lease. `runInBackground` and the managed logical TAS input contract remain
> mandatory. Native keyboard/controller input is still never injected.
>
> Fresh proof is
> `artifacts/framework-migration/focus-background-no-activation-v13-r1/`.
> `automatic-prime.json` proves `activationAttempted=false` and a minimized,
> non-foreground window across consecutive Unity frames. `button-smoke-r1.json`
> then advances controller f1 -> f4 and accepts exactly one logical pickup edge
> while the window remains minimized/non-foreground; the stale Unity focus bit
> remains true throughout, directly proving it is not needed. The linked
> installed-IL check passes 42 assertions and the Python focus/input suite
> passes 38 tests. Candidate core:
> `artifacts/framework-build-focus-v13-no-activation/SuperchargedPatch.dll`,
> SHA-256
> `36D2E8EB7B05D32ED04EF196FDEFD89A793482E96A3BA6E324060FDDE01B82DD`.
> This supersedes the v12 launch primer below. It is launcher/authoring
> infrastructure only and does not alter normal gameplay.

> **Current milestone (2026-09-14/15, r44i exact rewind boundary):** the
> deterministic Story 1-1 f1048 -> f1045 rewind now completes instead of
> failing the entity-48 kinematic wake guard. The restored f1045 baseline is
> exact across reconstructed entities, native round/food, native physics, and
> native clocks; Animator semantic parity passes. Replaying the identical
> three-frame delivery input produces the same second delivery, score 28 ->
> 56, combo/order transition, and recording SHA-256
> `35867d2f81e4579fd0d02346cfde731058b5fa3d9770ee887c2cda248decc687`.
> The probe still reports failure only because continuation physics now exposes
> the next residual: chef 46 Y is `0.0404009223` on the original f1048 and
> `0.0100009441` on replay; the report's only changed entity is 46.
>
> The causal chain is proved. R44e showed that `TimeManager.FrozenPhysicsData`
> woke already-kinematic entity 48. R44f pinned the first cause to a redundant
> `Rigidbody.velocity = Vector3.zero`; r44g proved the following redundant
> angular-velocity setter independently did the same. R44h suppressed both only
> for a stable, targetless, bit-exact-zero, sleeping kinematic body. That moved
> the first transition outside the pause setters: entity 48 stayed asleep and
> targetless through `after-early-body-pose-restore`, then entities 48-50 all
> became awake with same-pose kinematic targets before
> `before-final-attachment-pose-restore`.
>
> `WarpHandler.WarpChefAndPositions` was unconditionally rewriting position,
> rotation, velocity, and angular velocity after the exact early body restore;
> body-only proxies could traverse both the physics-container and body branches.
> R44i now skips only componentwise bit-exact public-state no-ops in all three
> branches. Every real pose/motion difference retains the original Unity setter
> and ordering. This is authoring-warp code only; normal gameplay and plate
> throwing are untouched. The new core is
> `artifacts/framework-build-r44i-exact-warp-noops/SuperchargedPatch.dll`,
> SHA-256
> `AF0F8E7EE1D6BC067AF9E444E31D77A78B06A841376C73E1A2EC48BAD24BC8A5`.
> The matching body module is
> `BodyRestore-r44i-exact-warp-noops-core-bg2`, SHA-256
> `61B6F745B27A63569A4F332EF5C874EB60DEECDA72D719D9FA965C2E49188EF9`.
>
> Live evidence is under
> `artifacts/framework-migration/story11-exact-warp-noops-r44i-bg-live-r2/`;
> the decisive summary is
> `second-delivery-f1045-rewind-r44i-bg-r1/summary.json`, SHA-256
> `17FB2F83E43A9164BBA73F54322C251EA8A4BC8BB31087F208FACF065ABD11E3`.
> Offline checks pass: BodyRestore 248, kitchen-ordering 28, dynamic-warp 92,
> and warp-component coverage 13. All 18 advancing leases in the live run were
> exact while minimized and `Application.isFocused=false`; no user focus or
> native gameplay input was used. The focus milestone is committed as
> `70b8c6f`.
>
> **Next:** isolate chef 46's continuation-only vertical discrepancy between
> restored f1045 and replayed f1048. Do not search or reload routes for search;
> continue using this single exact unwind/replay cell. Commit every working
> parity milestone locally and never push.

> **Superseded background-input milestone (2026-09-14/15, unattended logical input):**
> advancing TAS segments no longer require the user to focus Overcooked. Gameplay
> input remains entirely at the game's managed logical-button layer; no Win32
> keyboard/controller input is injected. The minimized launcher now performs one
> automatic, process-local Unity focus lifecycle (activate, hold for three Unity
> frames, minimize) and verifies `Application.isFocused=false` before returning.
> This briefly surfaces the game once per fresh process, for roughly 50 ms of the
> 0.31-second primer in the latest run; it is not repeated per route, segment, or
> rewind. The game then stays minimized while `Application.runInBackground` and
> the verified local-TAS focus bypass handle advancing input.
>
> The apparent remaining focus failure was actually an input-protocol bug. The
> headless controller's phase-only `RequestResume` reply legitimately carries
> `Input=null` before the first advancing frame. `InjectorServer` previously sent
> that through `ApplyInputFrame` as an empty pad set. While Unity was unfocused,
> removing all active pad ownership made `LogicalButtonBase.Update(false)` fail
> the TAS verification and execute Unity's native `ClaimPressEvent`; the first
> real pickup frame therefore arrived already claimed. Object-identity receipts
> proved the same gate/device instances changed from neutral/unclaimed at arm to
> neutral/claimed before the pickup. A full `ControlSchemeData.ClearEvents` call-
> site probe recorded no calls and was removed.
>
> `Input=null` is now preserved only for replies received from the authenticated
> controller: it means "no new pad sample." Locally manufactured connection/
> timeout control pauses still fail safe to explicit neutral and claimed input.
> Native gate callbacks, menu/direct-control suppression, local ownership checks,
> native edge histories, and the exact `ClientPlayerControlsImpl_Default.Update_Carry`
> consumer remain in place. The cleaned core build is
> `artifacts/framework-build-focus-v12/SuperchargedPatch.dll`, SHA-256
> `E36DB870CD4C35C298BA5AF69563A039533E0DBAB483ECC7264D4C1F831AA924`.
> The linked installed-IL/native-history fixture passes 42 checks and the Python
> input/primer suite passes 37 tests.
>
> Fresh-process proof is
> `artifacts/framework-migration/focus-background-carnival-r6/`: the launcher's
> `automatic-prime.json`, normal four-local `bootstrap.json`, and
> `button-smoke-r1.json` all pass. The smoke advanced f1 -> f4 with the window
> minimized and Unity unfocused throughout; it records the phase-only f1 resume
> preserving pads, then the exact f2 pickup returning `true` at the native
> `Update_Carry` consumer, followed by a clean release. A repeated same-process
> proof also passed in r5. No user click, native input, checkpoint, rewind,
> search, or level reload was involved.
>
> After the reboot, Windows HTTP.sys returned `ERROR_INVALID_HANDLE` even though
> its service reported running. The headless-only `RuntimeHost` now falls back to
> a loopback `TcpListener` implementation of the same small HTTP/JSON contract;
> the live r3-r6 controller runs used this fallback. This does not enter or alter
> the game process. `framework_input_probe.py` now requires and receipts the
> minimized/unfocused background contract for every advancing segment instead of
> requiring foreground focus.
>
> This milestone is committed as `70b8c6f`. The active rewind stack has since
> been rebuilt against the r44i core and runs entirely minimized. Search remains
> disabled until full rewind parity. The older notes below that request a manual
> focus lease are superseded by this milestone.

> **Current investigation (2026-09-14, r42 kinematic capture-phase skew):** a
> fresh focused r42 f1048 -> f1045 run reproduced the real failure with complete
> staged receipts:
> `Kinematic native wake state changed for entity 48: target=(wake 0,
> sleeping 1, active 0, publicTarget 0, coreTarget 0) current=(wake 1053609164,
> sleeping 0, active 1, publicTarget 0, coreTarget 0).`  Contrary to the r38
> bounded-ring interpretation preserved below, entity 48 is already awake on
> entry to the warp and stays byte-for-byte awake through every recorded restore
> stage: preflight, before/after authoring resume, spawn/removal work, attachment
> and component restore, early fixed-pose restore, and the final pause path.  The
> rewind does not perform the sleep -> wake transition.  The checkpoint sidecar
> and the current state on entry to the warp describe different lifecycle
> phases, but the exact pause-maintenance boundary between them is not yet
> resolved.
>
> `ActiveStateCollector.CollectDataForFrame` calls
> `NativeKitchenCheckpoint.CaptureFrame` before `ControllerHandler` calls
> `Helpers.Pause`.  The f1045 checkpoint sidecar therefore records entity 48
> asleep, targetless, and inactive at the pre-pause end-of-frame point.  The
> immediate paused warp stages and the `base-native`/`original-native` host
> observations record it awake, targetless, and active.  The large-dump
> timeline now proves those base/original observations occur after their
> admitted maintenance simulations: base gates at Unity 164241 -> 164242 and
> is sampled at 164245/247/249; original gates at 164274 -> 164275 and is
> sampled at 164279/280/282.  Conversely, the failed warp is still awake at
> `after-final-pause`, then its sole admitted maintenance simulation runs and
> the 164309 `finally-pause` observation plus every later native-body capture
> records entity 48 asleep with wake counter zero.  Thus the mismatch is a
> real checkpoint-phase skew: the pre-pause sidecar is asleep, the normal
> host-visible f1045 boundary is awake, and the failed rewind's final
> maintenance step advances the actor back to sleep.  The remaining question
> is which exact `MOVED`/targetless `SETTLING`/asleep phase and hidden lists
> correspond to each boundary.
>
> PhysX 3.3.3 and UnityPlayer review rules out a tempting but incorrect fix: the
> pause/unpause code repeatedly assigns `isKinematic=true`, but
> `Sc::BodyCore::setFlags` is a no-op when the flags are unchanged, and the
> kinematic velocity setters are rejected.  Skipping a redundant managed
> property assignment cannot explain or repair this phase skew.  Ignoring the
> guard is also not yet justified: even when final public wake bits agree,
> active-body ordering, island/change bitmaps, notification lists, and
> interaction ordering may retain different history.
>
> The next step is a read-only lifecycle receipt at each relevant boundary:
> capture `BodySim+0x90` internal flags (especially `MOVED` and
> `KINEMATIC_SETTLING`), active-list index/order, island node/bitmap state, and
> wake/sleep notification arrays alongside the existing public wake/target
> fields.  That will identify the exact phase of each observation and show
> whether the rewind divergence is a one-step phase offset or hidden lifecycle
> history.  Do not implement a phase rebase or add native active-list/island
> writes until this receipt proves the required source-equivalent transition.
>
> R43 implements that read-only receipt in native-helper API 8.  It appends
> `BodySim+0x90` flags, reciprocal BodyCore identity, scene active-list
> index/count/hash, island-node and relevant bitmap words, and sleep/wake
> notification-list membership/count/hash/validity to each existing body
> capture.  It double-reads the quiescent list headers/hashes and fails closed
> if they move.  No native lifecycle state is written and no restore guard has
> been relaxed.  The x86 helper static ABI is 356 bytes; its caller-owned pool
> harness passes.  The managed BodyRestore suite still passes 248 checks and
> the input-outcome suite passes 34.  Prepared artifacts are
> `framework-run/modules/BodyRestore-r43-kinematic-lifecycle-receipt-core-dev5/`
> (DLL SHA-256
> `B34DE21FA885F52F8C5BCC52BA549C6F44082278229AB66F04F6483E603123FD`)
> and
> `artifacts/native-rigidbody-rebuild-r15-lifecycle-receipt-cmake1/Release/Oc2NativeRigidbodyRebuild.dll`
> (SHA-256
> `1B4ED5FEC76C6B1F53C59FAD8A863C7EE7373B63A7CD85BAAA9D4339B8EFBEB0`).
>
> Evidence is under
> `artifacts/framework-migration/story11-kinematic-r42-trace-live-r4/second-delivery-f1045-rewind-r42-r1/`.
> The exact advancing recording SHA-256 remained
> `35867d2f81e4579fd0d02346cfde731058b5fa3d9770ee887c2cda248decc687`;
> all 17 focus-lease segments were valid, the second delivery advanced the
> ledger from 28 to 56, and the pause gate recorded 18 matching
> resume/maintenance/gate callbacks.  The isolated warp trace contains 777
> events with no drops.  R38 through the next read-only diagnostics remain
> uncommitted; the last working milestone commit remains `e953c92`.
>
> Focus is needed only for deterministic advancing input/replay segments,
> normally once per fresh route (about 45 seconds for this diagnostic), not for
> builds, dump analysis, capture-only polling, or warp-only work.
> `runInBackground` keeps Unity simulation alive but does not make focused raw
> input polling reliable.  The durable workarounds are to inject at the logical
> gameplay-input abstraction or run the game in an isolated desktop/VM.  Until
> then, warn the user before every focus-sensitive segment and release focus
> immediately afterward.  The r42 run has finished and the user may use the
> computer normally now.

> **Superseded r38 interpretation (kept for diagnostic history):** the focused
> r38 f1048 -> f1045 attempt reached the real
> rewind and failed closed before BodyRestore made a body mutation:
> `Kinematic native wake state changed for entity 48.`  The f1045 checkpoint
> directly proves entity 48 was awake/active (`sleeping=0`, `BodySimActive=1`),
> and the full r38 observation trace proves all three settled `original-native`
> observations immediately before the warp were also awake.  The first rows
> retained in the smaller failure ring are asleep, but the earliest in-warp
> stages were evicted.  Thus the awake -> asleep transition happens inside the
> warp/resume lifecycle; r38 does not identify its first stage or whether a
> kinematic target was still valid at that boundary.
>
> PhysX 3.3.3 source establishes that this is meaningful lifecycle state:
> `setKinematicTarget` produces the awake `MOVED` state; post-simulation target
> consumption produces an awake/no-target `SETTLING` state; the following
> targetless step sleeps and removes the actor from the active set.  Dynamic
> `wakeUp`/`setWakeCounter` APIs are invalid for kinematics.  A general fix must
> reconstruct the applicable source-equivalent lifecycle and account for
> active-list/island/notification state, rather than ignore or raw-write the
> public sleep fields.
>
> Managed BodyRestore r39 bound API7's already-existing read-only
> `oc2_rigidbody_get_kinematic_target` export.  Each kinematic checkpoint
> retains public/core/buffered target validity and exact actor-space target;
> a wake mismatch serializes target and current diagnostics before preserving
> the existing exception.  No parity check was relaxed and no new native write
> was added.  R40 additionally labels the already-occurring read-only native
> body/kinematic capture for every restore stage, so the next failure receipt
> will identify the first stage at which entity 48 sleeps without introducing
> another physics call.  R42 makes each stage failure explicit and persistent
> in that warp's receipt, removes transient stage sidecars immediately, and
> periodically sweeps dead historical weak keys.  R41's attempted 4,096-entry
> cap was rejected by live evidence: the legitimate retained checkpoint history
> already contained 58,967 sidecars at f1045.  It caused an early entity-49
> missing-sidecar failure before the intended guard and is not a parity result.
> It also preserves the kinematic-target receipt when cloning supported dynamic
> sidecars.  The offline BodyRestore suite passes 248 checks. Built
> module:
> `framework-run/modules/BodyRestore-r42-kinematic-stage-transient-cleanup-core-dev5/BodyRestore.r42-kinematic-stage-transient-cleanup-core-dev5.dll`,
> SHA-256
> `56B0A628BAB3168B6C5E01D74B4D10AD2F6D5DBA36AD03BC13662ABE03FF3C8B`.
> The dev5 loaders point at r42 and the unchanged API7/r14 native helper.
> R38 through r42 remain uncommitted diagnostics; the last working milestone
> commit remains
> `e953c92`.
>
> The next focused run is prepared as one combined receipt.  The trace loader
> installs only native trace masks 1 and 8 (Unity Rigidbody lifecycle plus
> PhysicsManager simulation boundaries).  Do not enable mask 4: its PhysX pose
> hooks intentionally change the pose entrypoints whose revision BodyRestore
> verifies, causing native error 1306 before this diagnostic.  The input probe
> now keeps those hooks installed but clears the ring immediately before the
> rewind, then saves `native-trace-warp.json` or
> `native-trace-warp-failure.json`; the long forward route can no longer evict
> the short rewind trace.
> Mask 1 also hooks Rigidbody creation, which conflicts with the actor-rebuild
> helper only if a rewind actually requests native actor reconstruction.  The
> exact f1048 -> f1045 cell has unchanged membership and zero rebuilds, so mask
> 9 is valid for this diagnostic but is not a generally composable parity
> configuration.
>
> Static managed/IL review also rules out both network synchronizers as direct
> entity-48 body writers.  `ServerWorldObjectSynchroniser` only reads/caches and
> sends pose state, while `ServerPhysicsObjectSynchroniser` reads velocities and
> contacts and packages messages.  With owner 3 still attached to parent 21,
> the synchronous warp performs no detach/attach and therefore none of the
> game's explicit kinematic toggles.  The only recurring relevant managed write
> is held-item `Transform.position = m_transform.position` in
> `ServerPhysicalAttachment.UpdateSynchronising`; it happens during ordinary
> Update, not inside the rewind's LateUpdate call.  Removing either synchronizer
> is not proven behavior-neutral and cannot explain this guard failure.
>
> Exact next probe recipe: replay the 15 requests in
> `story11-multidelivery-dev5-live-r1/forward-three-initial-plates-r3/prefix-through-second-service.json`
> (byte-for-byte equal to the prefix recovered from the r38 observations), then
> use a 90-frame neutral warmup to reach f1045 and the one-payload-frame
> `meal-1-batch-06-raw-capture.json` to reach f1048.  Use chef 46,
> `--expect-delivery`, contact-pool plus Transform-dispatch restoration,
> Animator/world-sync inspection, registry reconciliation, and both native trace
> flags.  The original r38 recording SHA-256 was
> `35867d2f81e4579fd0d02346cfde731058b5fa3d9770ee887c2cda248decc687`.
> A fresh process and uninterrupted game focus are required only while that
> advancing probe runs.
>
> The PhysX active-order investigation proves that sleeping and re-waking this
> kinematic actor is not a locally isolated operation: it changes active-body
> ordering, island/change bitmaps, wake/sleep lists, and potentially interaction
> ordering.  Even a source-equivalent MOVED -> SETTLING reconstruction cannot
> claim parity without restoring those structures.  Re-reading the *full* r38
> observation trace corrects the bounded-ring interpretation: all three settled
> `original-native` observations immediately before the f1048 -> f1045 warp show
> entity 48 awake, and all prefix/base observations do too.  The first retained
> in-warp/current ring rows show it asleep, but later capture pumping evicted the
> earliest stages.  Therefore the awake -> asleep transition occurs inside the
> authoring warp/resume lifecycle, not naturally in the abandoned future.  R42
> will identify the first exact restore stage and the checkpoint/current target-
> valid phase.  Both the f1045 checkpoint and f1048 current paused snapshots
> record `resumeIsKinematic=true` for entity 48, so the transition is not the
> ordinary dynamic -> temporary-kinematic pause conversion.
>
> If the staged receipt still cannot identify the cause, the next deeper native
> diagnostic is a bounded lifecycle receipt covering BodySim flags, active-list
> position, wake/sleep notification lists, and island bitmaps.  Critical layout
> correction from the PhysX 3.3.3 source/disassembly: the `Sc::BodyCore*` is at
> `*(BodySim + 0x34)`, not `BodySim + 0x04`; validate it with the inverse
> `*(BodyCore + 0x04) == BodySim`.  `BodySim + 0x04` is inherited Actor
> interaction storage.  Do not add native active-list writes merely to satisfy
> this diagnostic—sleep/re-wake changes actor ordering and island state.
>
> The failed game and host were stopped after preserving diagnostics.  The
> user is actively using the computer, so do not launch or foreground the game
> without warning.  A focused/unfocused A/B proved that the prior unfocused route
> delivered the same one-frame pickup to the framework, but native gameplay did
> not consume it.  This was a false-negative forward run, not rewind evidence.
> `framework_input_probe.py` now takes a harness-only focus lease around every
> advancing input burst: it fails before arming when unfocused and rejects an
> endpoint if either unfocused-input counter changed.  Warp-only work and all
> offline analysis remain background-safe.  All current work is offline. A fresh full game
> process is mandatory before the next live run because r38 displayed a real
> `AUTHORING_WARP_FAILED` overlay.

> **Active rewind result (2026-09-14, quiescent pause and five-step pose
> lattice):** the deterministic second-delivery f1045 rewind now completes
> the native warp instead of failing on chef 46's coupled Rigidbody pose.
> Two independent problems were separated. First, Unity continued automatic
> PhysX simulation on render frames while the logical TAS frame was paused.
> The authoring-only physics-pause gate now disables `Physics.autoSimulation`
> only while the bridge owns the pause, restores it before every real resume,
> and admits exactly one maintenance `FixedUpdate` before re-gating. The live
> receipt recorded 18 resumes, 18 maintenance callbacks, and 18 maintenance
> gates with no failure. Ordinary advancing gameplay is unchanged.
>
> Second, the PhysX 3.x quaternion-normalization/center-of-mass composition
> makes its public actor-pose setter a quantized inverse. The real chef-46
> residual crosses the target, briefly grows by one ULP, and becomes exact on
> the fifth bounded assignment. BodyRestore r36 therefore gives only the
> coupled native pose loop a five-assignment cap and removes the invalid
> monotonic-residual assumption. Finite/candidate/residual envelopes, actual
> candidate progress, exact final readback, and every Transform, collider,
> motion, mass-frame, and identity postcondition remain mandatory. Both live
> `after-final-rotation` records reached exact position and quaternion on
> attempt 5. The tested r36 DLL SHA-256 is
> `D5F070314E259B64D656F7634F150BF7B981B6A30607E2EDDF6C1212AA1B9322`;
> the focused synthetic contract passes 220 checks.
>
> This exposed the next real boundary rather than completing the cell. At the
> restored f1045 baseline, entities, registry, food, round state, clocks,
> frame, poses, velocities, modes, and body incarnations are exact. The sole
> native-physics difference is surviving dynamic body/entity 60 (the empty
> Rigidbody container for plate-stack owner 59): checkpoint `sleeping=false`,
> restored `sleeping=true`. Evidence is
> `artifacts/framework-migration/story11-physics-pause-dev5-live-r4/second-delivery-f1045-rewind-r36-r1/summary.json`
> (SHA-256
> `CABA30B80AD6C455C63C3A109A3C136FBD57FF11FC37F68C44B4B2EFBE5496FA`)
> with sibling `body-r36-status.json` and
> `physics-pause-gate-status.json`. Search remains disabled. Continue by
> restoring and verifying the checkpoint sleep lifecycle for admitted dynamic
> container bodies, then rerun this same unwind-only cell.

> **Active rewind result (2026-09-13, repeated scored terminal lifecycle):**
> Story 1-1 now survives repeated non-adjacent f8999 -> f8492 -> f8999
> rewinds after a real +28 delivery and returned plate/stack lifecycle, all
> without reloading the level.  The fresh r2 prerequisite f444 -> f1047
> delivery was exact; its summary SHA-256 is
> `EBA27395A17F67679F8DDA2918C188301E36ED263ED1E6886A48D9FC3F6DD1FE`.
>
> The r2 recovery proof began at the exact restored f8492 checkpoint left by
> the diagnostic first repeat attempt.  It reused the persistent contact and
> Transform sidecar instead of recapturing it, reached the natural scored
> terminal twice for the standard original/replay comparison, and then
> completed two additional held-latch rewind/replay cycles.  All four terminal
> receipts (nonces 3, 4, 5, and 6) are identical at f8999, with score 28,
> one delivery, returned body incarnations 56/58, and exactly 507 input frames
> from f8492.  Every restored baseline and terminal comparison passed exact
> controller entities/registry/raw receipt, lifecycle and iterator PCs,
> round/orders, food, normalized physics, and clocks.  Animator replay stayed
> in clean `Record` mode.  Sidecar capture counts stayed fixed and every
> rewind consumed exactly one contact-pool and Transform restore.  Evidence:
> `artifacts/framework-migration/story11-scored-terminal-dev5-live-r2/scored-terminal-f8492-repeat2-r2-reuse/summary.json`,
> SHA-256
> `E6DA273781CAECDE01B3C117BDD99E5EF9689D41865065E533848B3AA1B9606D`;
> input SHA-256
> `e2d1897f59cc25f692e72896e760b57f9b43af78dd1f3dd0d2e6c5c7d439c25c`.
>
> The first extended r2 attempt was a harness fence error, not parity drift:
> the held-terminal warp restored f8492 exactly, but the probe attempted a
> managed hot-call before reissuing the bridge pause/fence handshake.  It
> produced no authoring failure.  The corrected harness fences that restored
> boundary, validates the latch's `restored-acknowledged` state, re-arms it,
> and loops while the terminal latch is still held.  A completed terminal
> probe cancels the latch once at the end; it cannot initiate another rewind
> after cancellation.
>
> The pinned core/headless/RoundEnd binaries remain respectively
> `C3E4A874FABDC3D232521972B1597330D1FE1EC197145E025A4932109BE9717F`,
> `01C07B138C64C7A28081B6893E2E009C179FDDE2B1FD257EE8A037C3640870EB`,
> and
> `301213527C163375CA0BE3C292B5A5977A780A62AA8F3D374AC52B00A683781C`.
> The r2 identities at proof completion were game PID 26348 / UTC start ticks
> 639249426768162416 and host PID 59064 / UTC start ticks
> 639249427587139910.  The probe released the terminal latch cleanly, so this
> is not a reusable in-level checkpoint; revalidate both identities and load a
> fresh level before further control.  This proves repeatability of the scored
> terminal cell, not complete level parity.  Search remains disabled while
> coverage expands to multiple deliveries/dynamic-ID cycles, expiry/deduction
> paths, wider multi-chef interactions, and arbitrary rewind order.

> **Active rewind result (2026-09-13, surviving PxShape state rebind):**
> the non-adjacent f1500 -> f444 rewind that previously failed before replay
> now restores and replays the complete 603-frame first-delivery continuation
> to f1047 exactly.  The failure was entity 44's persistent Player 1 capsule:
> its `PxShape` identity survived, but its native actor-local pose legitimately
> changed while the chef moved in the abandoned future.  BodyRestore r32
> incorrectly required the surviving row's current pose and geometry to
> already equal the f444 checkpoint before the native restore ran.
>
> BodyRestore r33 keeps the strict surviving `PxShape` identity, actor row
> order, geometry type, managed Collider-role bijection, and exactly two
> authorized recreated plate BoxCollider rows.  It now carries checkpointed
> pose and geometry targets for every actor row, including survivors, into the
> existing native restore helper.  That helper writes and verifies every row
> before simulation resumes.  This is rewind-only snapshot bookkeeping;
> ordinary forward gameplay, Animator attachment motion, and plate-throw
> physics are unchanged.  No native C++ change was required.
>
> A clean c7bn process rebuilt the established no-search route from Story 1-1
> frame 1 through f444, delivery f1047, returned-stack pickup f1090, held dash
> f1122, genuine dash-drop f1198, and neutral rest f1500.  Every prerequisite
> cell passed exact rewind/replay.  The decisive direct f1500 -> f444 restore
> recreated initial plate owner/body 2/47, discarded the returned-plate
> future, and replayed to f1047 with no changed entities and exact native
> physics, food, round state, logical clocks, input recording, contact-manager
> and manifold pools, TransformChangeDispatch, and Animator semantics.  Actor
> rebuild count remained zero.
>
> The r33 receipt proves the expected changed survivor rather than merely
> bypassing it: entity 44 had three actor rows, two recreated rows, and exactly
> one differing surviving pose at actor index 0; surviving geometry differences
> were zero and the rebind was verified without poison/failure.  Primary
> evidence:
> `artifacts/framework-migration/story11-surviving-shape-r33-live-r2/nonadjacent-f1500-to444-r1/summary.json`,
> SHA-256
> `85C952A86964FAEBCCE617BEC8355F9663A9C8AC2A1DB6088692E2FABC915354`,
> and sibling `module-status.json`, SHA-256
> `0055848A16BB05AD30F708968132579FB978259E6A585EB47CAC603E77D96FF6`.
> r33 DLL SHA-256 is
> `114E1995226031045603FF601E56A2E643B7197EBFDCD524A5A7FEE15CC1D41D`;
> its manifest's seven source hashes all match the current repository files.
> The focused native-shape role test passes 13 checks and the synthetic body
> contract passes 216.  The healthy game PID 65312 / UTC start ticks
> 639249222117919950 and Story11 host PID 37752 / UTC start ticks
> 639249224351800543 are paused and input-fenced at f1047.  Revalidate both
> identities before control.  Search remains disabled; complete Story 1-1
> rewind parity is still broader than this exercised lifecycle.

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
> Non-adjacent retained-history coverage passes too.  From live f2102, the
> framework rewound directly to the older pending-rest checkpoint f1198 and
> replayed its 302 observed frames to f1500.  Baseline and endpoint comparisons
> had no changed entities and exact native physics, food, round state and
> clocks.  Animator semantic replay completed, the verified 302-frame branch
> prefix was committed without game-state mutation, and the abandoned
> 602-frame future was discarded.  Evidence:
> `artifacts/framework-migration/story11-logical-world-rest-c7bnr2-clean-r1/nonadjacent-f2102-to1198-r1/summary.json`.
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
> game PID 51196 and host PID 42372 are healthy and paused at f1500; revalidate
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

## 2026-09-13: far-rewind proxy-retirement rebranch fixed and proved

The older opening sections above are historical. Search remains disabled while
rewind parity is being completed. The current headless source adds no native
mutation: it expires controller-owned proxy-retirement observations after a
verified rewind when their receipt frame is later than the target frame, or
when the same native ID was freshly registered at the target. Historical
receipts on the surviving branch remain intact.

The concrete failure was a stale `56@1090` retirement observation surviving a
far f1500→444 rewind. It suppressed the new `observe-absent(56)` publication
when replay later picked returned plate 57 up from stack 55. The actual game
state, input, and normalized physics had already been exact; only the
controller's abandoned-future evidence was wrong.

The new helper is `HeadlessSession.RebranchProxyRetirements`. Its focused unit
matrix retains `52@200` and `54@283`, discards future `56@1090`, and discards
`58@444` when that ID is freshly registered at the target. It reports
`nativeStateChanged=false`. Offline checks passed: DynamicWarp 55, Story11 89,
Status 32, and the full Headless suite 369.

Live proof uses headless DLL
`framework-run/headless-host-v12q8-proxy-retirement-rebranch/Headless.dll`,
SHA256
`AC3CEFB2D1AB4ABC04ACB0E18E8218E75F4C0678D75D74C3F5B217C4D57368CA`.
Evidence is under
`artifacts/framework-migration/story11-proxy-rebranch-v12q8-live-r3/`:

- focused f444→1047 delivery passed with input SHA
  `c91fe72f0376f51e3b826761c86b2550b6d97302ebaf13bad61230eae3eb5141`;
- f1047→1090 pickup, f1090→1122 held dash, f1122→1198 dash-drop,
  and f1198→1500 settle each passed exact rewind/replay comparisons;
- `nonadjacent-f1500-to444-r1/summary.json` passed the far rewind and replay,
  discarded only `56@1090` as `abandonedFuture=true`, retained two historical
  receipts, and matched entities, round, food, clocks, normalized native
  physics, and input at f1047;
- `postfar-pickup-f1047-to1090-r1/summary.json` passed the formerly failing
  pickup, published a fresh `observe-absent(56)`, achieved the requested
  pickup on original and replay, and matched all compared state. Its exact
  input SHA is
  `345305c60fc756953d3427710f517606eba5f9ed5c95c224484e2aa533aaed08`.

All live route endpoints in this proof were focused. A prior apparent delivery
regression was an unfocused run that dropped the one-frame f280 interaction;
it was not rewind drift. Foreground the exact game process before each route
cell and inspect `actualFocusAtEndpoints`.

At this writing the isolated game is PID 90352, UTC start ticks
639249244851564978; the headless host is PID 60080, start ticks
639249247979836514. The controller is paused/fenced at f1090 after the
post-far pickup proof. Revalidate both identities before reuse. On any real
authoring-module failure or red overlay, preserve diagnostics and restart the
whole game; a level reload does not clear all failure state.

This far route also composes capabilities proved by earlier milestones:
post-fade plate destruction/resurrection, returned-stack recreation, the
representative Animator transition matrix, and genuine dash-drop/plate-throw
physics. Those are not current gaps. Next work is broader Story 1-1 rewind
coverage, not route search: long order/timer evolution and expiry, multiple
deliveries with repeated dynamic-ID lifecycles, multi-chef interactions, and
round end. Preserve plate-throw physics and commit every newly demonstrated
milestone locally.

## 2026-09-13: pristine natural round-end rewind is exact

The first full Story 1-1 terminal edge is now a proved rewind milestone. Core
dev5 adds an explicitly armed, local-authoring-only latch for the exact natural
`InLevel -> RunLevelOutro` callback. When unarmed it does not change forward
round-end behavior. When armed, it defers the terminal server deactivation,
retains the live server/client round coroutine identities and dormant outro
iterator, pauses at the single pristine callback, and permits only an immediate
rewind to the pinned checkpoint. Restore writes the captured lifecycle and
iterator fields directly before the ordinary resume; it does not call
`ChangeGameState`, start an outro coroutine, or execute an iterator manually.

Live proof is under
`artifacts/framework-migration/story11-round-end-latch-dev5-live-r7/`.
`terminal-parity-f8492-r1/summary.json` passed with SHA256
`9CF59DF61FFC7667B9877E9201BA9A53DE5C8BC93CA3C741C1A8ABD8B19498F9`:

- checkpoint f8492 restored exactly after the original terminal;
- original and replay both reached the natural terminal at f8999 after exactly
  507 emitted and observed input frames;
- reconstructed frame/entities/registry and terminal input receipt matched;
- server/client lifecycle, all three iterator PCs, native round/orders, food,
  normalized native physics, and captured native/logical/timer clocks matched;
- the contact-manager free-list and TransformChangeDispatch sidecars were each
  captured once at f8492 and restored once before replay; and
- latch nonce advanced exactly once and the latch was cancelled only after all
  evidence was retained.

The exact input recording SHA is
`52FA366B4EF715C591804F7B5637C0162047FD2E298FEACD40D7AB810860570F`.
The fixture receipt `setup-dev5-r1.json` has SHA256
`4A7FF8A2535119080E3AF5146FA07B1B62F956E2C873DBFD4496007C5921727F`.
Core SHA256 is
`C3E4A874FABDC3D232521972B1597330D1FE1EC197145E025A4932109BE9717F`,
headless SHA256 is
`01C07B138C64C7A28081B6893E2E009C179FDDE2B1FD257EE8A037C3640870EB`,
and the thin RoundEndCheckpoint authority module SHA256 is
`301213527C163375CA0BE3C292B5A5977A780A62AA8F3D374AC52B00A683781C`.

The terminal proof requires the already-proved exact contact-pool and
Transform-dispatch sidecar. The first live latch attempt in `live-r6` captured
the correct f8999 terminal but intentionally failed closed with
`AUTHORING_WARP_FAILED` because the new probe had omitted that sidecar. Its
player log was preserved, the whole game process was restarted, and the probe
now verifies the one-capture/one-restore lifecycle explicitly.

The preceding long neutral coverage is also proved under
`story11-broader-parity-v12q8-live-r1/neutral-orders-f1090-to4692-r1/`.
That 3600-frame continuation produced new scripted orders 5 and 6 and matched
the rewind/replay endpoint for entities, timer/order state, food, native
physics, clocks, and input. Its summary SHA256 is
`E24884D929950CED4EDD381B2F1EC33500762AB9C8AA9116605CE8D90D705AE0`.

This closes pristine no-delivery round-end and long neutral order/timer
coverage. It does not yet prove every possible terminal inventory or score
state. Broader parity work should next combine the terminal edge with delivered
food/returned plates and repeated dynamic-ID lifecycles, then expand multi-chef
interaction coverage. Search remains disabled.

The `live-r7` game was PID 38168, UTC start ticks 639249362656054236; its
headless host was PID 53028, start ticks 639249363220780862. The successful
proof cancelled the latch at the terminal, so this is not an `InLevel`
checkpoint to reuse. Revalidate process identities and start a fresh level for
the next parity cell.

## 2026-09-13: scored terminal rewind is repeatable without reload

The scored round-end proof now composes repeatedly inside one live Story 1-1
level.  `framework_input_probe.py --expect-round-end --terminal-repeats N`
keeps the local-authoring round-end latch held, rewinds from f8999 to the pinned
f8492 checkpoint, proves the restored boundary, re-arms the latch, raw-replays
the same recording, and compares the next natural terminal against the
immediately preceding terminal.  It cancels the latch only after every cycle.
The option is bounded to 20 repeats and is unavailable outside the isolated
round-end proof path.

The decisive recovery proof is
`artifacts/framework-migration/story11-scored-terminal-dev5-live-r2/scored-terminal-f8492-repeat2-r2-reuse/summary.json`
(SHA-256
`E6DA273781CAECDE01B3C117BDD99E5EF9689D41865065E533848B3AA1B9606D`).
It reused the existing f8492 contact-manager/Transform checkpoint and therefore
also proves that repeat unwind does not require level reload or sidecar
recapture.  The standard original/replay pair and two extra cycles yielded
terminal nonces 3 through 6.  Each endpoint was f8999 after 507 frames, and
each comparison was exact for reconstructed entities and registry, raw input
and terminal receipt, lifecycle and iterator PCs, score/orders/food, normalized
native physics including returned bodies 56/58, and captured clocks.  Animator
status was clean `Record` before each extra warp and after each replay.  Contact
and Transform capture counters remained stable; restore counters advanced by
exactly one per rewind.

The first repeat attempt in sibling
`scored-terminal-f8492-repeat2-r1/summary.json` is retained as diagnostic
evidence.  Its first extra held-terminal warp and native restore were exact,
but the new harness issued the latch-status hot-call before re-fencing the
restored boundary.  The bridge rejected that call without an
`AUTHORING_*_FAILED` latch.  Adding the missing pause/fence handshake fixed the
harness; no core, synchronizer, managed gameplay module, or native C++ behavior
changed.  The focused Python suite passes 44 tests with one expected skip.

This milestone closes repeated scored-terminal rewind, but not complete level
parity.  Continue with multiple deliveries and repeated dynamic-ID lifecycles,
order expiry/deduction, broader two-chef interaction combinations, and varied
non-adjacent rewind order.  Keep route search disabled until those cells pass.

## 2026-09-14: deterministic three-delivery forward fixture

The bounded no-search Story 1-1 fixture now completes three consecutive native
deliveries from a fresh frame-1 level.  The old assembly-clear route moved the
chopper beside an adjacent board; on the second meal the incoming plate helper
could push that chef back over the selected board interaction point and create
a deterministic collision deadlock.  `DeliveryPlanner` now clears the chopper
by ordinary no-button navigation back toward the exact ingredient crate already
owned by the selected case.  This changes only generated test input; it does not
write a chef pose, physics state, recipe, timer, score, or gameplay component.

The clean proof is
`artifacts/framework-migration/story11-multidelivery-dev5-live-r1/forward-three-initial-plates-r3/summary.json`
(SHA-256
`36D886392937677087DFBC1D19B3626482BBCC36610F46660123F6CF1C423567`).
It advanced from f1 through the f1240 release boundary, delivered fish on
plates 2 and 1 at f435 and f957, then prawn on plate 4 at f1239.  The final
native ledger is score 92, base 60, tips 32, combo/multiplier 3, three
deliveries and zero deductions.  Every meal is correlated to native kitchen,
plate-station, order and ledger events.  The focused registry tests pass 9/9.
Returned-stack pickup remains outside this fixture's admission rule.

The first exact second-delivery unwind cell exposed the next real parity gap in
`.../second-delivery-rewind-r4/summary.json`.  Original and replay both consume
order 2 and produce the exact 28-to-56 ledger, food, order, clock, input and
Animator state.  The restored f1045 checkpoint is exact, including dynamic
returned-plate bodies 60/62, contact-manager free-list and Transform dispatch.
The first divergence is the following physics step: movement history is exact
at f1046, but at f1047 Players 1 and 2 exchange the vertical fall response
(`-2.000004` versus zero).  Their endpoint Rigidbody heights consequently swap
between approximately 0.0100 and 0.0404.  Animator controller memory,
transition topology, mixer graph and pose hashes remain exact through f1048,
so current evidence places this below Animator evaluation.  A clean process
must install the read-only native physics tracer before the hooked rewind stack
to compare the original and replay simulation calls; late installation
correctly failed its entry-byte revision guard.  Search remains disabled.

## 2026-09-15: sleeping-kinematic parity and persistent controller history

Commit `356a675` closes the later f1045/f1077 sleeping targetless-kinematic
drift.  BodyRestore r44s uses native API 11 to preserve the exact sleeping
kinematic state; the working native helper is
`artifacts/native-rigidbody-rebuild-r19-target-invalidate-cmake2/Oc2NativeRigidbodyRebuild.dll`
(SHA-256 `E2A15632E0409AD6D034E7D0B51FF9442AFF8B13C638BE0F7E389ABF634302A1`).
The managed r44s DLL SHA-256 is
`F5913C7983A152DC7A91D507423FC03EE9A4A3A6D5D219581B42B7F684D44482`;
the v14 core SHA-256 is
`A4B50DB0CAF564CFCED075DADEA6109CA14F3A0C2D7C16A9310954D0C7DE960F`.

The next non-adjacent rewind exposed a controller-only history bug.  A
successful shallow rebranch to f1045 erased consumed dynamic spawn receipts
whose objects were absent at that target even though they had first existed on
the retained prefix.  The affected Story 1-1 paths were `[30,0]`, `[30,0,0]`,
`[30,1]`, and `[30,1,0]`.  `CarnivalRegistryAudit` now discards an absent
receipt only when its first native observation is later than the rewind target;
earlier consumed receipts remain as authentication history for a subsequent
deeper rewind.  This changes controller validation bookkeeping only and reports
`nativeStateChanged=false`; it does not call or mutate the game.

The fixed headless build is
`artifacts/framework-headless-host-v12q9d-deep-history/Headless.dll`, SHA-256
`B7544877DDF735D74B0EE6DD1624323387B5EEF4AC4A540F37781EBCB727B6B9`.
Its broad offline selftest passed 391 checks.  The clean v31 live fixture
`story11-kinematic-native-v31-live-r1/second-delivery-f1045-r44s-history-r1/summary.json`
again proves the exact f1045-to-f1048 rewind/replay and reports all four paths
under `retainedHistoricalPaths`.  The subsequent f1048-to-f444 request passed
registry/controller preflight and entered native restore preparation, proving
that the old missing-receipt failure is gone.  It then failed closed before any
restore mutation in DeliveryFadeCheckpoint because f444 is inside the first
plate's delivery fade.

Read-only extraction from the retained r10k sidecar established the exact f444
state: plate entity 2 has iterator `$PC=2`, progress `0.3`, `$current=null`, two
disabled colliders, two fade-shader materials at alpha `0.733333349`, and a live
detached `PFX_Delivery`.  At f1048 that first iterator is terminal while plate
entity 1 is in a separate future fade.  The next parity implementation must
therefore compose two inverse operations: cancel entity 1's future fade back to
its ordinary f444 plate state, and recreate entity 2 through the proven native
factory into the exact already-yielded fade state.  Native checkpoint preflight
also requires a scoped temporary mask of the target snapshot's delivery-fade
count, restored before its final exact-boundary capture.  Search remains
disabled.

## 2026-09-18: f1048 -> f444 reaches Animator after exact mixed scheduler preflight

The bounded, no-search f1048 -> f444 Story 1-1 probe now passes the delivery
parent-incarnation and WorldObject/native-scheduler gates which previously
blocked restore preparation.  The target contains two destroyed checkpoint
owner/body pairs simultaneously: initial delivery plate `2/47`, detached on
its own container during the f444 fade, and dynamically spawned sushi fish
`55/56`, addressed by logical crate path `[30,1]` from spawn path `[30,0]`.
The source contains future returned-stack pairs `59/60` and `61/62`, which the
warp deletes.

`WorldSyncCache-r13u-mixed-logical-spawn-path-core-bg4-v14` authenticates that
exact mixed transaction before mutation.  It validates the observed crate
prefab against entity 30's live `SpawnableEntityCollection`, reserves all four
historical IDs in native plan order, preserves the allocator and scheduler
orders, rebinds both recreated incarnations, and can rebind the detached
initial plate's server/client parent caches to its recreated container.  Its
local-only pending-rest admission is retained at capture but accepted at warp
time only for an owner the actual scheduler plan proves will be recreated.
This deliberately makes no remote packet-parity claim.  Ordinary forward play
is unchanged: capture is read-only and every allocator, registry, parent-cache,
body, and pose write is behind an authenticated authoring rewind.

The focused WorldSync suite passes 201 checks, including the exact mixed
plate/sushi transaction with active pending-rest state.  The pinned module DLL
SHA-256 is
`97D9DBD1072C6D1827BBEB261432AFF44EA3DCB030C4EBED0472F6B4B3528198`.
The same stack pins delivery r10o SHA-256
`A0DD0FCC54E82B7758A8E262FCF27B7CA82F466A74994562ACAE4D169362F71B`
and actor-sidecar r14h SHA-256
`206C81591ABA6B9368B3C375CFEEB1E210252C031F20BE8F8116508EBDD8FE8D`.

Live v43 is the decisive downstream-gate proof.  Its summary is
`framework/artifacts/live-v43-midfade-f1048-to-f444-r1/summary.json`, SHA-256
`84D8DB005E45A66192165322A01B3F060F525004D581E558B1BC18B1F0A677E6`.
The game stayed minimized and non-foreground throughout every advancing
lease.  Restore preparation passed registry, delivery, mixed scheduler,
WorldObject, dynamic logical-path, and native sidecar validation, then failed
closed before mutation in ChefAnimatorCheckpoint with:
`No unambiguous linked chef Animator boundary and resume-ready checkpoint at
output frame 444.`

This is a scheduler/WorldSync preflight milestone, not a completed rewind.
The immediate next task is to inspect why the already scheduled exact f444
Animator capture is not linked/resume-ready in this non-adjacent fixture.  Do
not weaken the new scheduler transaction and do not start search; continue
toward a successful f1048 -> f444 restore and exact suffix replay.
