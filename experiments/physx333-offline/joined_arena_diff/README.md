# Joined arena differential (diagnostic, not a restore implementation)

This source-built Win32 PhysX 3.3.3 fixture asks what the joined component
restore still leaves different from a full fixed-address arena checkpoint. It
uses the level-shaped scene with 12 contacts, 4 triggers, and 2 marker pairs.
The supported component chain is topology, report graph, WorkUnit bindings,
memory blocks, WorkUnit/PCM payload, island, SAP, transform cache, shape-cache
IDs, bodies, scene clock, context, and query state. No game or Unity binary is
launched.

From the `framework` directory, run:

```powershell
cmd /c experiments\physx333-offline\joined_arena_diff\Build-Check.cmd
```

The source bridge must already be built. This command only builds the fixture's
executable; it does not rebuild or edit the shared PhysX source mirror. The
fixture runs cold and warm variants with both ordinary and shipped-like SAP
ordering. Each makes a source checkpoint A, records a direct-input B and
no-input B, returns to A using the raw arena *only to establish a reference
starting state*, reaches B again, performs the component restore to A, and
checks both continuations. It checks all existing component images (using the
query image's explicit cold-tree/rebased-stack comparison), the contact image,
graph, facts, and callbacks. The raw arena is never used for the component
restore under test.

`JOINED_ARENA_DIFF` compares allocator owner/cursor/peak/allocation ledger and
every byte through the arena's allocated peak. It reports byte differences
after normalizing only the 75 source-proven unwritten AABB task-padding bytes
(`../arena_snapshot/ArenaAabbPadding.cpp:15-57`). An
allocation's source, live/free state, and first differing byte are printed.
The restored-A and cold-history raw differences deliberately **do not gate**
the passing component tests: unmatched bytes are the subject of the
investigation. Both warm successor branches do gate exact allocator-ledger
and non-padding byte convergence.

## Findings

All four scenarios pass complete component A and B image comparisons and the
tested direct-input and no-input continuations. Raw arena equality is stronger
and currently fails at restored A.

In both cold variants, the checkpoint A has 329 blocks and cursor 813888. The
source direct-input B has 334 blocks and cursor 814472: one 256-byte AABB
manager auxiliary allocation (`PxsAABBManagerAux.h:59`) plus four scene-query
tree/array allocations (`SqAABBTree.cpp:539,546,554`, `PsArray.h:543`). The
component restore frees those five allocations but leaves their entries in
the diagnostic allocator's ledger and leaves cursor 814472. Replaying B adds
five *new* blocks, reaching 339 blocks and cursor 815064. The no-input branch
likewise allocates new query-tree blocks after A. The allocator deliberately
advances its cursor on allocation (`../arena_snapshot/ArenaSnapshot.cpp:48-64`)
and copies its whole ledger into snapshots (`:79-88`); the component restore
has no allocator-ledger stage. Exact raw-address equality therefore fails in
the cold replay, including rebased live query-tree pointers, although the
query image's source-defined rebasing comparison and tested outputs pass.

Both warm variants hold the allocator ledger fixed across A, B, restore, and
replay. At restored A, the ordinary warm case still has 120 non-padding changed
bytes in live blocks; shipped-order warm has 122. The ordinary warm breakdown
is:

| Owner and changed bytes | Source evidence | Classification |
| --- | --- | --- |
| Contact-report buffer, 49 | `SimulationController/src/ScContactReportBuffer.h:70-78` resets the used index; `../joined_contact_image/JoinedContactImage.cpp:419-430` captures only used bytes. The fixture measures used index 0 at A. | Retained-capacity tail; not proven irrelevant to every future read. |
| Flush-pool chunk, 45 | `Common/src/CmFlushPool.h:35-39,59-82,94-100` shows reusable task storage with reset index/offset; `SimulationController/src/ScScene.cpp:1718` clears it. | Task scratch history; no general read-before-overwrite proof. |
| Contact-report actor-pair array, 14 | `SimulationController/src/ScNPhaseCore.h:150,219` identifies its logical size/storage; `ScNPhaseCore.cpp:1949` clears it. The fixture measures size 0 at A. | Retained-capacity array contents; tested next steps converge. |
| Trigger API and extra-data arrays, 8 + 2 (shipped warm: 10 + 2) | `SimulationController/include/ScScene.h:417-418,533-535` names the arrays; `SimulationController/src/ScScene.cpp:2679-2680` clears them. Both measure size 0 at A. `foundation/include/PsArray.h:248-251` sets size 0 without clearing the backing bytes. | Retained-capacity array contents; tested next steps converge. |
| Friction stream array, 2 | `LowLevel/common/include/pipeline/PxcNpMemBlockPool.h:113-120` names the two arrays; `../memblock_restore/MemBlockRestore.cpp:398-412` restores logical entries only. The changed backing array measures logical size 0 at A. | Retained-capacity entries; tested next steps converge. |

The original differential also found one immediately readable
`Sc::SimStats::numTriggerPairs` byte (source A=2, restored A=0).
`SceneClockImage` now restores all five source-defined statistics tables,
including this value; its focused test verifies immediate
`getSimulationStatistics()` parity. That byte is no longer among the warm-A
differences.

Source paths in the table that begin `SimulationController/`, `LowLevel/`,
`Common/`, or `foundation/` are relative to the generated
`../joined_topology/work/PhysXSDK/Source/` mirror. `JOINED_ARENA_POINTER_OWNER`
and `JOINED_ARENA_MEMBLOCK_ARRAY` independently map the changed blocks to these
fields. The warm direct-input B and no-input B each have an exact allocator
ledger and **zero non-padding byte differences**. Only known AABB task padding
remains. This proves convergence for these suffixes, **not** for arbitrary
subsequent inputs or scene histories.

The residual bytes should not be declared universally benign. The fixture has
shown no downstream effect in the tested branches, not that every possible
native read ignores them. Retained-capacity contents and flush-pool scratch
history warrant a longer branching test or an explicit source-level proof
before an unconditional parity claim.

This test uses a diagnostic fixed-address allocator instead of the shipped
Unity allocator. It does not establish Unity/Animator/game rewind parity, and
it cannot validate hidden native memory outside this allocator. Its purpose is
to expose the remaining source-level ownership and history gap after the
joined component images agree.
