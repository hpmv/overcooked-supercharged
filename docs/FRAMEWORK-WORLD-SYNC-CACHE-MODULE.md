# Settled native WorldObject cache restoration

> **2026-09-11 r8b extension:** a genuine plate throw proved that a useful
> rewind target can legitimately contain an active/pending rest update. Entity
> 2 was detached and moving at frame 266, so r5 rejected the checkpoint before
> mutation. R6 retains the complete server message/flags, local client cache,
> scheduler cadence/order/urgent state, and free-ID queue exactly. When the
> reliable-rest packet is still pending, it translates only its absolute
> `Time.time` deadline: the captured remaining offset to
> `m_LastUnreliableActiveSend + 1f` is re-based onto `Time.time` both at
> checkpoint restoration and again immediately before the actual authoring
> resume, so arbitrary paused inspection time cannot consume the residual.
> This preserves when the original code emits the rest event without rewinding
> or replacing Unity's clock. Completed-rest timestamps remain bit-exact.
> `GetServerUpdate`, `PopulateMessage`, client handlers, packet generation,
> physics poses, and forward behavior are not patched. Admission is limited to
> the observed inactive pending-parent state with position sync enabled;
> active or otherwise inconsistent states still fail closed. Both deadline
> residuals are checked bit-exactly before any cache write. The same final
> rebase applies to the uninterrupted reference continuation, so authoring
> pause duration is excluded symmetrically. The r8b build is
> `framework-run/modules/WorldSyncCache-r8b-reference-and-warp-rebase/WorldSyncCache.r8b-reference-and-warp-rebase.dll`,
> SHA-256 `4B3A1994715B4A9AF36DBD95F793903AB68558AA7139E07476B3027380EF8574`.
> The 99-check managed/installed-IL audit is
> `artifacts/framework-world-sync-cache-r8-tests.json`. Live continuation parity
> remains required before this extension is accepted.

> **2026-09-10 contextual review:** generic WorldObject synchronization is not
> dead in local-only play. The paired local `ClientWorldObjectSynchroniser`
> applies update/event messages; parent-cache changes can reparent the object,
> invoke callbacks, and write the physical attachment container's Rigidbody
> pose. Do not blanket-suppress `ServerWorldObjectSynchroniser` or force its
> reliable-rest handshake complete. The current `RequireSettled` rule is safe
> but narrower than necessary. A future extension may admit in-flight rest
> states by storing
> `deadlineOffset = (lastUnreliableSend + 1f) - capturedUnityTime` and restoring
> `lastUnreliableSend = (restoreTime + deadlineOffset) - 1f`, while preserving
> every other server message, client cache, and scheduler field/order exactly.
> This requires fail-closed identity, parent/pose, interpolation, pause, finite-
> value, and scheduler-topology checks. Force-completion is not parity-safe
> because it removes the original reliable event and changes transport/batching
> history.

The native X/V11 pickup replay contains one extra native WorldObject event for plate12 at frame165, before pickup completes. It encodes source parent41 and identity local pose. Every other packet at that frame is byte-identical in the same order. Physics, chef state, actual phase, inputs and auxiliary bytes match; strict raw-event replay still fails. The pinned proof is `artifacts/framework-migration/native-x-v11/offline-exact-frames`.

The installed game's `ServerPhysicalAttachment.Attach` calls `ServerWorldObjectSynchroniser.ResumePositions` when restoring the counter attachment. That native method sets `m_bSyncPositions=true`, `m_bSentReliableRestPosition=false`, and `m_LastUnreliableActiveSend=0`, then refreshes the parent. `GetServerUpdate` later calls `SendServerEvent(m_ServerData)` when its one-second rest condition is met. This explains a concrete route by which correct native reattachment and exact pose restoration can leave future synchronization behavior different. The old trace does not expose the private flags, so the causal conclusion still requires the new module's captured native diagnostics and strict continuation proof.

`framework/modules/world-sync-cache/WorldSyncCacheModule.cs` is an external authoring module. It installs three owned Harmony hooks while paused:

1. A paired prefix/postfix around `NativeKitchenCheckpoint.CaptureFrame` binds a copied native cache only when that call inserts a new snapshot into the core history dictionary. An existing snapshot from before module activation cannot acquire a retroactive cache. Repeated paused callbacks do not overwrite the first saved cache. The actual round object resets this sidecar on level restart.
2. Before `NativeKitchenCheckpoint.Prepare`, require the identical selected snapshot and retained entity/component/message/parent incarnations. Every observed initial PhysicalAttachment must have its reliable rest packet already sent, be started and sleep-eligible, and have no active or pending parent change. Unsupported targets reject before native world mutation.
3. After `RestorePlan.Complete`, verify that native attachment/body callbacks already restored the saved parent and exact local pose. Copy the observed WorldObject payload into the same message object, restore its private cache fields, and read them back exactly. This runs before the core's acknowledgement collection and next physics step.

The helper does not patch native GetServerUpdate, suppress/reorder any event, change a physics pose, or rewrite Unity time. A saved pending one-second timer is explicitly unsupported: `Time.time` does not rewind, so copying its absolute timestamp would be incorrect. For an admitted already-settled target the timestamp has no pending effect; the next actual movement overwrites it through the unchanged native algorithm. Broader timer behavior after future movement and pauses still needs separate native parity tests. The cache is native synchronization state, while module counts/receipts are added instrumentation; neither receipt alone qualifies physical replay or a score.

Load `framework-run/modules/WorldSyncCache-r1b/WorldSyncCache.r1b.dll`, SHA256 `9dba09058e736564c40a1a1ffd0b19a8ada9484edef98cef53ffb8d3e3fb5607`, entry `SuperchargedPatch.Authoring.Modules.WorldSyncCacheModule`, suggested slot `world-sync-cache`. `activate`, `status`, and `deactivate` take no arguments. Activate before a new normal level restart and explicitly clear old action graphs before warmup. Existing checkpoints created before activation are not retroactively invented. Status lists every initial attachment's observed rest flags, timestamp, source identity and first unsupported reason, making an overly broad initial gate visible. The earlier r1a DLL is preserved; r1b closes its late-activation snapshot-binding gap.

The module is independently compiled against frozen X (`391f6265321ae57c7924e723ecb0c3ec3f9f9c91baadd42aac2521ec624bd746`) and actual native assemblies. `scripts/FrameworkWorldSyncCacheCheck` passes50 assertions covering exact snapshot binding, late activation, paused duplicate capture, the actual rest-event reset mechanism, current versus saved parent identity, failed pending timers, registration/message/history mutations, exact pose prerequisite, and restart/pruning. It verifies installed native method IL, frozen core hook ordering and the compiled CLR2 module's paired Harmony state contract, while behavioral tests use managed native/Harmony stubs without sockets. Report: `artifacts/framework-world-sync-cache-tests.json`.

The parent-owned fresh native observation `artifacts/framework-migration/native-x-v11b/world-sync-r1a-status.json` establishes all13 initial attachments had `sentReliable=true`, `active=false`, `parentChanged=false`, with no unsupported target reason. Plate12's cached parent is41. This actual observation validates the narrow gate's initial availability; it is not a strict rewind or continuation proof. Native restoration and replay with r1b remain separate parent-owned validation; all prior failures remain unchanged.

The subsequent combined BodyRestore r4 + ResumePhase r1c + WorldSyncCache r1b experiment passes an independent strict trace audit: three60-frame idle replays, one10-frame movement replay, and one11-frame actual pickup replay. All201 advancing frames match exact physics/chef/phase/input/raw-native/auxiliary fields and event order. The old extra plate12 WorldObject packet is absent from both pickup continuations; frame165 has24 identical raw packets on both sides. Both native endpoints separately prove plate12 moved from source41 to chef103. Proof: `artifacts/framework-migration/native-x-v11b/offline-exact-frames-synced-r4`, unchanged source-prefix SHA256 `4f36505982f1dc66a979477a3c4422bca5e22e66e6ff0ae1ff42a17ffe50606d`. This is bounded combined-module parity, not a broader dynamic recipe or score claim.

The longer plate search subsequently exposed a separate native polling-cadence difference. External r2 adds exact scheduler residual/order/urgent-state capture and restoration without changing native Update or time. Its implementation, bounds, installed-IL evidence,66-check report and subsequent native timing proof are described in `docs/FRAMEWORK-NATIVE-SCHEDULER-CHECKPOINT.md`. All56 native-message/input/phase frames now match in that macro replay, although tiny late physical differences still fail strict parity. The above201-frame combined proof belongs specifically to r1b.

External r3a additionally restores bounded client WorldObject bookkeeping after the native attachment/pose restoration. See `docs/FRAMEWORK-CLIENT-WORLD-CACHE.md` for the observed client32/logical38 post-warp mismatch, exact reference guards,82-check release and subsequent native results. R3a completes all four candidates and verifies cache restoration, but its selected macro still fails strict physical parity from frame74 despite exact input/phase/message streams.
