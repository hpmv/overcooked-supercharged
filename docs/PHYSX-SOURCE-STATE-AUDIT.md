# Settled PhysX 3.3.3 state audit for offline rewind

This audit is for the pinned `3.3.3-1.3.3` source build and a checkpoint taken
after `PxScene::fetchResults` returns, before another scene write. The six-box
fixture is one dynamic actor with six shapes touching six static actors, using
SAP and an inline dispatcher. “Complete” below means exact replay for a stated
fixture and call trace, not a claim about Unity's shipped binary or every PhysX
feature. Source links point to the sibling `PhysX-3.3.3` checkout.

The checkpoint boundary matters. `fetchResults` processes pending removals,
reports, state synchronization, callbacks, and ID release cleanup in that order
([NpScene.cpp:2242](../../PhysX-3.3.3/PhysXSDK/Source/PhysX/src/NpScene.cpp#L2242),
[ScScene.cpp:1745](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScScene.cpp#L1745)). A capture in the middle of a callback or before the final cleanup is a
different state. The test-only observers already refuse an active simulation.

## Evidence scale

- **Observed:** the offline fixture or existing component test demonstrated a
  difference, round trip, or exact fresh-scene replay.
- **Source dependency:** the source retains a value at the settled boundary and
  reads it later, or uses it to choose allocation/order. It still needs a
  divergent-replay test before we can call the field necessary in this fixture.
- **Unresolved:** lifetime, configuration, or a source read has not yet been
  mapped sufficiently to prove whether it survives or matters at this boundary.

The public-only rewind control lost six contact callbacks (24 event words
became zero). This demonstrates that the retained contact predecessor matters
as a *system*. It does not by itself isolate one byte or prove that restoring
any one component would fix the replay.

**Current joined fixture milestone:** the joined six-box transaction
restores a checkpoint from its all-contacts-deleted successor in the same
source-built scene. The reconstructed checkpoint matches all 39 implemented
oracle sections; the first replayed step and all 100 rewind/next-step cycles
match SAP, transform-cache, all rigid ShapeSim bindings, body, scene-clock,
cached worker-context, scene-query, island, and memory-block component images,
as well as public body state and ordered contact events. The query image uses
a guarded rebase for one FIFO stack allocation that grows from one to two
entries; its semantic equality permits the owned backing address to change,
but not the stack contents or unaffected fields. A five-step contact suffix
also matches an independent reference scene.
This is not complete PhysX-only parity for a
level: four oracle categories remain explicitly unsupported, feature matrices
are incomplete, and the transaction is fail-stop rather than atomically
recoverable. See the [joined probe](../experiments/physx333-offline/harness/README.md).

## Retained-state inventory

| Family and source | Boundary assessment | Offline coverage and gap |
| --- | --- | --- |
| **Scene phase, time, settings.** `Sc::Scene` owns gravity and its dirty flag, step and report timestamps, time step, filtering, dominance, scene flags, sleep/wake lists, ID trackers, and callbacks ([ScScene.h:512](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/include/ScScene.h#L512), [ScScene.h:573](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/include/ScScene.h#L573)). The next `simulate` overwrites `mDt` and `mOneOverDt`; `mTimeStamp` and `mReportShapePairTimeStamp` advance at distinct points ([ScScene.cpp:459](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScScene.cpp#L459), [ScScene.cpp:2328](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScScene.cpp#L2328)). | **Source dependency** for timestamps, gravity dirty, callbacks, and settings; elapsed time is at least an observable counter. `mDt` may be an invariant under the next identical `simulate` call, but `collide`/`solve` have different writes. | [SceneClockImage.h](../experiments/physx333-offline/scene_clock/SceneClockImage.h) now images source-visible clocks, gravity/dirty flag, sleep/wake arrays, and shape/rigid ID LIFO stacks under immutable scene/actor/allocation guards. Empty-scene and six-contact component tests pass 100 A/B round trips. It is not yet joined to a safe next-step rewind; query and other context state are separate. |
| **Actor/body state.** `BodyCore` retains full low-level core, wake counter, thresholds, and a `SimStateData` pointer ([ScBodyCore.h:170](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/include/ScBodyCore.h#L170)); `BodySim` retains low-level rigid body, force dirty flags, accumulated sleep velocities, freeze count, and island hook ([ScBodySim.h:196](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScBodySim.h#L196)). Sleep calculations read the accumulated values ([ScBodySim.cpp:541](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScBodySim.cpp#L541)); force update reads dirty state ([ScScene.cpp:1898](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScScene.cpp#L1898)). | **Source dependency.** The public pose/velocity/wake snapshot misses `BodySim` internals, `PxsRigidBody::mLastTransform`, CCD links, and pending force data. | [BodyImage.h](../experiments/physx333-offline/body/BodyImage.h) now images same-actor Core/Sim/low-level body, including contact-derived `mBodyConstraints`, force payload, sleep accumulators, and last transform. Six-contact and force-payload component tests pass duplicate capture, 100 A/B round trips, and corrupt-image rejection. Unchanged actor/shape/SimStateData allocation and active topology are required; full joined next-step replay is open. |
| **Kinematics and pending velocity modification.** `SimStateData` overlays a saved kinematic target/backup with a force/velocity accumulator ([ScSimStateData.h:28](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScSimStateData.h#L28), [ScSimStateData.h:62](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScSimStateData.h#L62)). `postCallbacksPreSync` deactivates active kinematics and invalidates targets before `fetchResults` returns ([ScScene.cpp:2949](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScScene.cpp#L2949)); the backed-up values and settling flag can remain. | **Source dependency** for a kinematic fixture; no evidence that every target persists across the chosen checkpoint. | A separate settled kinematic-target [BodyImage test](../experiments/physx333-offline/body/BodyCheck.cpp) passes 100 A↔B component round trips. It does not prove next-step parity or cover sleep/wake, mode switching, target validity through a full joined restore, or pool free order. |
| **Shape/actor ownership and IDs.** Each `ShapeSim` stores object and transform-cache IDs ([ScShapeSim.h:103](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScShapeSim.h#L103)); elements store AABB handles and actor links ([ScElement.h:117](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/framework/ScElement.h#L117)); actors store ordered interaction and element lists ([ScActor.h:145](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/framework/ScActor.h#L145)). `ObjectIDTracker` defers releases until cleanup, and `Cm::IDPool` reuses IDs from an ordered stack ([ScObjectIDTracker.h:31](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScObjectIDTracker.h#L31), [CmIDPool.h:34](../../PhysX-3.3.3/PhysXSDK/Source/Common/src/CmIDPool.h#L34)). | **Observed hidden dependency:** without restoring `ShapeSim.mTransformCacheId`, the next step produced the same public results and oracle but a different transform free-ID LIFO order. Public release/recreation of three fixture actors also changes contact-manager history and BodyImage identity, despite restoring seven visible actors. | [ShapeCacheBindings.h](../experiments/physx333-offline/shape_cache/ShapeCacheBindings.h) enumerates every attached rigid-static/dynamic ShapeSim, including contactless shapes; the isolated 13-shape probe passes. The [actor-lifetime negative control](../experiments/physx333-offline/harness/README.md) demonstrates why the same-allocation image cannot cover released/recreated actors. General actor/shape lifetime, articulation shapes, triggers, and complete shape/core fields remain open. |
| **SAP and AABB manager.** The AABB manager owns `BPElems`, change lists, aggregate managers, pair outputs/capacities, and a broadphase object ([PxsAABBManager.h:207](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/include/PxsAABBManager.h#L207), [PxsAABBManager.h:296](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/include/PxsAABBManager.h#L296)). Broadphase outputs are consumed in order by `finishBroadPhase` ([ScScene.cpp:4119](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScScene.cpp#L4119)); its temporary BP buffers are freed earlier ([PxsAABBManagerTasks.cpp:882](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/src/PxsAABBManagerTasks.cpp#L882)). | **Observed system dependency.** Original six-pair deletion becomes `0/0` after public-only rewind. The exact SAP contribution is not separately isolated. | `SapImage` round trips full declared-capacity SAP/BPElem state 100 times and rejects malformed images atomically ([SapImage.h](../experiments/physx333-offline/sap/SapImage.h)). It only accepts unchanged same-scene allocation and binding topology. Aggregate state, growth/reallocation, and joint replay with NPhase are open. |
| **NPhase interactions and reports.** Creation/removal uses the original `onOverlapCreated`/`onOverlapRemoved` lifecycle; removal releases SIP/trigger/marker and sometimes ActorPair pools ([ScNPhaseCore.cpp:180](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScNPhaseCore.cpp#L180), [ScNPhaseCore.cpp:497](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScNPhaseCore.cpp#L497), [ScNPhaseCore.cpp:1834](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/ScNPhaseCore.cpp#L1834)). Ordered persistent/threshold pair lists, a report buffer, dirty interaction set, and filter-pair pool also persist ([ScNPhaseCore.h:217](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/include/ScNPhaseCore.h#L217)). | **Observed** as a missing contact predecessor. **Source dependency** for report timestamps, list order, filter-pair IDs, and pool reuse. | [InteractionImage.h](../experiments/physx333-offline/interaction/InteractionImage.h) restores physical SIP/CM/report slots, scene/actor order, event lists, report bytes, touch metadata, and context CM bitmaps for the six-contact fixture. The new [subset bridge](../experiments/physx333-offline/nphase/NPhaseTopology.h) independently restores just four missing pair objects in a 12→8 fixture, preserving all eight survivor identities and matching all twelve SIP/CM slots; it stops before simulation because the survivor contact/report payload is still different. Trigger/filter-pair/dirty-set matrix and complete pool free-slot bytes remain open. |
| **Low-level contact manager, manifold, and streams.** A CM owns `PxcNpWorkUnit`; its contact cache, compressed contacts, friction data pointer/count, solver pointer/size, flags, and transform-cache IDs are distinct fields ([PxcNpWorkUnit.h:85](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/common/include/pipeline/PxcNpWorkUnit.h#L85)). Contact generation reads prior cache/manifold ([PxcNpBatch.cpp:145](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/common/src/pipeline/PxcNpBatch.cpp#L145)); contact preparation consumes friction state ([PxcNpContactPrep.cpp:415](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/common/src/pipeline/PxcNpContactPrep.cpp#L415)). | **Source dependency** for contact geometry and solver parity; the public-only control's CM free stack differs at the checkpoint. | [InteractionImage.h](../experiments/physx333-offline/interaction/InteractionImage.h) now installs the six target WorkUnit bindings, validates their block ranges, and restores single PCM manifold semantic contents after memory-block restoration. The joined probe passes 100 repeat applications of binding, allocator, and payload stages; it still has not replayed a next step. Multi-manifold, solver/friction backing beyond this fixture, and complete free-slot bytes remain open. |
| **Contact memory and allocation.** `PxcNpMemBlockPool` keeps ordered constraint/contact/friction/cache stream arrays, stream selectors, unused blocks, counters, and scratch state ([PxcNpMemBlockPool.h:112](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/common/include/pipeline/PxcNpMemBlockPool.h#L112)). It pops from the unused stack and swaps prior/current streams ([PxcNpMemBlockPool.cpp:188](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/common/src/pipeline/PxcNpMemBlockPool.cpp#L188), [PxcNpMemBlockPool.cpp:319](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/common/src/pipeline/PxcNpMemBlockPool.cpp#L319)). | **Source dependency** even when a particular active payload is empty: stream choice and allocation order can affect future pointers and reused bytes. | [MemBlockRestore.h](../experiments/physx333-offline/memblock_restore/MemBlockRestore.h) now restores ordered arrays, all 16 KiB block payloads, and counters under same-allocation guards. Its joined variant transfers the two friction/cache blocks back out of `mUnused` after NPhase recreation. Component tests pass 100 LIFO/payload and 100 ownership-transfer A/B round trips, with atomic corrupt-image rejection. Allocation growth, scratch/exceptional blocks, CCD, and unobserved free-and-reallocate-at-same-address remain unsupported. |
| **Transform cache.** Shape/CM IDs index transforms and reference counts; free IDs are popped in order ([PxsTransformCache.h:39](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/include/PxsTransformCache.h#L39), [CmIDPool.h:50](../../PhysX-3.3.3/PhysXSDK/Source/Common/src/CmIDPool.h#L50)). | **Source dependency** for contact transforms and any shape lifetime change. | `TransformCacheImage` round trips full allocation including capacity tails and ID order 100 times. It requires unchanged same-scene allocation; caller must restore ShapeSim and CM references too ([TransformCacheImage.h](../experiments/physx333-offline/cache/TransformCacheImage.h)). |
| **Island graph and sleep/wake.** Node, edge, island managers and ordered change journals survive, along with flags that may skip an update when all are asleep ([PxsIslandManager.h:251](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/include/PxsIslandManager.h#L251), [PxsIslandManager.cpp:600](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/src/PxsIslandManager.cpp#L600)). Temporary work pointers are nulled after the pass ([PxsIslandManager.cpp:622](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/src/PxsIslandManager.cpp#L622)); graph and pool metadata remain. | **Observed system dependency** in the shipped trace (12 edges to 8 versus 0 to 0); **source dependency** in the offline fixture. | [IslandImage.h](../experiments/physx333-offline/island/IslandImage.h) now has a joined variant that accepts the pending change journals left by NPhase pair recreation, while rejecting active simulation/work pointers and changed actor/CM bindings. After contact and allocator restore, it restores the checkpoint graph. The resulting six-contact checkpoint matches all 39 currently implemented oracle sections; the oracle still declares four unsupported categories, and no next step has run from this reconstructed scene. |
| **Dynamics, CCD, and other context state.** `PxsDynamicsContext` retains solver-body/data pool allocations and solver settings ([PxsDynamics.h:433](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/include/PxsDynamics.h#L433)); `PxsContext` owns active/touch CM bitmaps, changed-AABB handles, threshold stream/table, thread context pool, and CCD context ([PxsContext.h:349](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/include/PxsContext.h#L349)). Solver bodies are resized/copied from active bodies during update ([PxsDynamics.cpp:3093](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/src/PxsDynamics.cpp#L3093)); CCD clears many active arrays after passes but retains capacities and context fields ([PxsCCD.cpp:1252](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/src/PxsCCD.cpp#L1252)). | **Observed hidden dependency:** without cached worker-context contents, joined next-step simulation statistics diverged even though public contacts and the 39-section oracle matched. `PxsContext::beginUpdate` only clears simulation statistics ([PxsContext.cpp:1366](../../PhysX-3.3.3/PhysXSDK/Source/LowLevel/software/src/PxsContext.cpp#L1366)); it is not a blanket reset. | [ContextImage.h](../experiments/physx333-offline/context_image/ContextImage.h) now restores cached worker-context LIFO order, object bytes, and 19 backing arrays. The standalone and expanded joined 100-cycle tests pass. A touch bitmap that reallocates 1→8 words is deliberately left live because source resets it before reuse. CCD, constraints, articulations, aggregates, contact modification, and nonempty scratch remain feature-gated. |

The body-transform vault is retained but is registered by particle-system code
([ScParticleSystemSim.cpp:226](../../PhysX-3.3.3/PhysXSDK/Source/SimulationController/src/particles/ScParticleSystemSim.cpp#L226)). It should be asserted empty in the rigid-only fixture. If Story 1-1 creates particles, that assertion becomes a feature gate and the vault's hash/pool history needs an image.

## Scene-query caveat

Raycasts, overlaps, and sweeps can change private query state even though the
user-facing call is `const`: `NpSceneQueries::multiQuery` flushes pending
updates into `mSceneQueryManager` pruners before querying
([NpSceneQueries.cpp:744](../../PhysX-3.3.3/PhysXSDK/Source/PhysX/src/NpSceneQueries.cpp#L744)).
`fetchResults` also processes query updates
([NpScene.cpp:2299](../../PhysX-3.3.3/PhysXSDK/Source/PhysX/src/NpScene.cpp#L2299)).
The six-contact joined harness does not call scene queries, but even its
ordinary `fetchResults` updates the dynamic pruner. [QueryImage.h](../experiments/physx333-offline/query_image/QueryImage.h)
now captures/restores the default AABB query manager and both pruners under
strict same-allocation guards. Offline tests pass 100 pending-to-flushed
raycast/overlap replays in settled, rebuild-init, and rebuild-in-progress
phases, plus a fixed-allocation progressive-build next-step replay. It still
accepts one guarded cross-allocation transition: the progressive dynamic
pruner's FIFO stack grows from capacity one to two. The restore keeps the
current owned allocation, lowers its logical capacity, and checks parity
through a semantic comparator that ignores only the rebased address. Its
isolated and expanded joined tests pass 100 next-step replays. Other
cross-allocation transitions remain rejected. It also rejects bucket fallback, alternate
pruners, shape topology changes, and volume-cache/batched/PVD paths. Record
the level's actual query trace and join its image before a level parity claim.

## Dependency order for a complete transaction

```text
stopped phase + exact API-call trace + scene/actor/shape identity
                         |
          body/core and actor/shape allocation/ID state
                         |
        SAP/BPElem handles and transform-cache bindings
                         |
         NPhase lifecycle materialization and pool slots
                         |
          CM/manifold/memory-stream pointer rebasing
                         |
        reports, event lists, timestamps, active bitmaps
                         |
       island nodes/edges/journals and sleep/wake state
                         |
          full image comparison -> next PhysX step
```

This is an admission and publication order, not evidence that each object
family is independently restorable. Existing SAP, transform-cache, and island
component tests return to a consistent successor before stepping because they
cannot currently repair the cross-links. Lifecycle calls may allocate and
change IDs; make those calls before projecting raw payload and free-list tails.
Preflight all bindings, capacities, arena ownership, and feature gates before
the first write. Use same-scene rollback or fail-stop if postwrite verification
cannot prove the target.

## Tests that expose multiple blockers early

1. **Complete read-only checkpoint matrix.** At both the warm six-contact
   checkpoint and deleted successor, capture one joined image covering every
   row above. Repeat capture without a scene write; compare independent fresh
   scenes using semantic IDs. If a section is unimplemented, report that
   omission explicitly. Include a read/write ledger for fields currently
   classified as transient.
2. **Same-scene first-step gate.** Run `warm A -> delete B -> restore A ->
   delete B` in one scene. Before stepping, prove joined target equality;
   after stepping, compare the ordered broadphase rows, callbacks, *all* joined
   sections, and public body bits. Repeat the rewind 100 times and compare a
   settled suffix. **Passed for the expanded six-box fixture image, including
   cached worker context and rebased scene-query stack, with complete extended
   next-step image checks in all 100 cycles.**
   The oracle's unsupported sections and other contact topologies remain open.
3. **Allocation and topology matrix.** A separate [12→8 partial-contact
   fixture](../experiments/physx333-offline/partial_contacts/README.md) now
   proves the four lost-contact callbacks disappear on public-only rewind.
   Extend the joined transaction to preserve eight survivors and resurrect
   four missing pairs. Create, remove, and recreate one contact
   pair; then reuse a shape ID, transform ID, CM slot, NPhase pool slot, and
   island edge. Add triggers, filtering/refiltering, and a 12-to-8 contact
   deletion. Force capacity growth separately. Reject unsupported growth before
   mutation until a rebasing transaction supports it.
4. **Motion and solver matrix.** Give the dynamic body gravity, a tangential
   velocity, friction, an impulse, and then a dash-like collision. Compare
   velocities, contact impulses, friction/manifold bytes, sleep/wake events,
   and next-step memory streams. This determines whether the current observer
   misses solver state that the stationary six-box case does not exercise.
5. **Kinematic and CCD matrix.** Exercise target set/move/settle, body sleeping
   and waking, fast CCD contact, and contact removal in each phase. Capture at
   settled `fetchResults` only. Preflight `mCCD` and context state; require no
   unsupported pointer before replay.
6. **Fault and feature gates.** Corrupt each saved identity/size/free chain and
   inject a postwrite verification fault. Prove rejection leaves the scene
   unchanged, or rollback restores the prior joined image; otherwise stop
   before simulation. Assert absent particles, cloth, articulations,
   constraints, aggregates, GPU paths, and contact-modify callbacks in the
   rigid-only fixture. Add dedicated fixtures for any feature actually used by
   Story 1-1 before declaring PhysX-only parity for that level.

The expanded six-contact first-step gate has passed. The next implementation
path is to restore the surviving contact/report payload in the 12→8 fixture,
then add solver, sleep/wake, CCD, actor/shape-lifetime, and remaining
feature/topology coverage. No
current test proves arbitrary scene or Unity-shipped-binary rewind parity.
