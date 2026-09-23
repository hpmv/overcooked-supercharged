# PhysX 3.3.3 offline rewind parity

This is the active development track for the Story 1-1 physics predecessor.
The objective is to make an instrumented, source-built Win32 PhysX scene rewind
to a settled checkpoint and produce the same subsequent internal states and
outputs when given the same calls. Game execution is deferred during this
track. A source-built result is not yet a claim about Unity's shipped binary.

## Latest status (2026-09-22)

The expanded six-contact same-scene joined rewind passes 100
checkpoint-to-next-step cycles with full component-image comparisons and a
five-step suffix. The separate 12→8 box/box fixture now also passes a complete
same-scene checkpoint restore and next-step replay for 100 cycles in each of
four modes: cold and warmed allocation states, each with either twelve
touching pairs or ten touching and two overlapping/non-touching pairs. It
matches ordered callbacks, all 39 implemented source-Oracle sections, and
the SAP, island, interaction, cache, body, clock, context, query, and contact
memory component images. A capsule/box variant of the mixed fixture now
passes both cold and warmed 100-cycle joined replays with twelve PCM
manifolds, the level-shaped ten-touch/two-no-report pattern, and the same
full-image/ordered-callback gates. A diagnostic fixed-address allocator independently
replays a five-step alternating 12↔8 suffix for each cold variant, matching
initialized arena bytes, allocation ledgers, and callbacks over 100 cycles.
The same arena diagnostic also survives releasing and recreating one static
actor and shape before restoring its checkpoint, then reproduces a five-step
suffix for 100 cycles after repairing the fixture's external actor pointer.
That narrow result does not make actor lifetime safe in the component-based
restorer or in Unity: deletion listeners, OS synchronization, and external
allocations are outside this snapshot.
See the
[12→8 fixture](../experiments/physx333-offline/partial_contacts/README.md),
[arena diagnostic](../experiments/physx333-offline/arena_snapshot/README.md),
and [source-state audit](PHYSX-SOURCE-STATE-AUDIT.md). The older milestone
narrative below records how the experiment reached this point.

This is not complete PhysX-only parity for the level. The joined capsule
fixture still uses a simplified single-mover layout, deletes only four
contact pairs rather than the level's four contact plus two trigger pairs,
and has no trigger/marker interactions. A separate
[trigger/marker baseline](../experiments/physx333-offline/trigger_marker/README.md)
now reproduces the level's 12/4/2→8/2/2 interaction-count pattern and
ordered four-contact/two-trigger losses in a fresh-scene source-built test;
it does **not** rewind those interactions yet. Its auxiliary observer now
also accepts the level-relevant capsule/box triggers in either orientation,
and an isolated source-built test verifies touch and separation histories.
The newer [level-shaped graph baseline](../experiments/physx333-offline/level_graph/README.md)
reproduces four chef capsules, a fifth active body, shared static endpoints,
two auxiliary markers, four triggers, twelve large capsule manifolds, and
the six semantic broadphase deletions. It now also captures the full existing
component images at each settled boundary and checks their immediate
same-scene recapture. It is still a fresh-scene control:
its deletion order and TransformCache allocation history differ from the
shipped observation. Graph-aware contact/trigger reconstruction and joined
component next-step rewind remain open. The
[opt-in SAP-order variant](../experiments/physx333-offline/level_graph/README.md)
keeps the semantic graph but changes static creation order and reproduces
the shipped six-deletion **type** sequence `C,C,C,T,C,T`. It does not prove
the shipped actor insertion history or whole native image. A separate
[whole-allocation diagnostic](../experiments/physx333-offline/level_arena/README.md)
does rewind this synthetic level-shaped source scene: it replays a five-step
leave/return suffix and a second non-adjacent checkpoint's suffix 100 times,
matching the full implemented Oracle, trigger/marker and ActorPair images,
the SAP, cache, island, memory-block, body, clock, context, and query
comparator-defined payloads, ordered callbacks, and initialized allocator
bytes. It also matches A and C immediately after restoration, before fixture
inputs, and replays a separate no-input one-step branch from A 100 times.
That result depends on a fixed-address diagnostic allocator unavailable in
the shipped Unity binary; it is a source-level reference, not a game-side
restore. An [isolated trigger-rewind fixture](../experiments/physx333-offline/trigger_rewind/README.md)
now also reconstructs two deleted capsule/box trigger pairs through the
original PhysX NPhase lifecycle, with exact A and next-B images over 100 cold
and 100 warm cycles. Its second variant keeps one trigger and one marker
survivor while recreating two deleted triggers; it still has no contacts, so
the joined level-graph component restore is open. A new
[joined topology bridge](../experiments/physx333-offline/joined_topology/README.md)
recreates the level-shaped graph's four missing contacts and two missing
triggers together through native NPhase, preserving eight contact, two
trigger, and two marker survivors, physical SIP/ActorPair/trigger slots, and
mixed scene/actor interaction order. The bridge now also validates the four
contact-manager indices and island-edge IDs against their source-defined
free-pool allocation order before writing. It now also validates the PCM
large-manifold pool and all twelve target manager-to-manifold identities,
including the four reallocated slots. A second native stage creates
exactly two missing ActorPair report objects through PhysX's lazy path and
restores all twelve pairs' touch/report metadata, ordered persistent events,
and manager bitmaps. Cold and warm readbacks match the full A ActorPair/report
graph; fifteen malformed plans across both stages reject without mutation.
The full Oracle still first differs at `contact.managers[48]`, and no
restored successor step is attempted. Actor
and shape lifetime, CCD, and several other native-state families remain gated.
A [read-only joined contact image](../experiments/physx333-offline/joined_contact_image/README.md)
now captures all twelve A and eight B contact rows by native-oriented
actor/shape endpoints, including report ownership, contact-manager work
units, and used PCM payload. Same-scene exact recapture and a portable
fresh-scene comparison pass. It identifies `contact.managers[48]` as
`frictionPatchCount` on a **surviving** chef/static pair, changing 2→1;
therefore the eventual contact payload restore must cover all twelve A
managers, not just the four recreated contacts.
A separate [joined WorkUnit stage](../experiments/physx333-offline/joined_workunit_restore/README.md)
now restores all twelve checkpoint WorkUnits, their used PCM manifold payloads,
and the tracked contact-memory blocks after topology/report reconstruction.
Cold and warm tests pass in both baseline and shipped-like SAP deletion order;
six malformed checkpoint images reject without changing the contact image.
The first remaining full Oracle difference is `island.change_queues`.
This is still a partial restore: the fixture does not simulate after it.
A new [joined full-replay fixture](../experiments/physx333-offline/joined_full_replay/README.md)
composes the guarded topology, report, WorkUnit/PCM, memory-block, island,
SAP, cache, shape-ID, body, clock, context, and query restorers. In the
synthetic level-shaped source scene, cold and warm histories with either
default or shipped-like SAP order each pass 100 exact A→B restore/replay
cycles and 100 later C→N→D→E cycles; N advances without fixture pose or
velocity setters. Every restore matches the full implemented stopped image
before simulation. Only one query FIFO capacity-tail comparison is semantic:
the active entries and every other field still match, and source code shows
the unused tail is overwritten before read. This is not shipped f444 or
Unity-binary parity.
A separate [joined cache-history fixture](../experiments/physx333-offline/joined_cache_history/README.md)
uses public PhysX shape attachments and detachments to reach the observed
transform-cache ledger (`currentId=13`, ten live IDs, 24 references, and free
IDs `[12,11,10]`) before its joined checkpoint. In independent fresh-process
traces, complete component restoration and exact successor comparisons pass
100 direct A→B cycles and 100 A→no-input→B cycles. Its other native histories
and geometry remain synthetic. A longer trajectory after 100 direct cycles
currently reaches a query-pruner phase rejected by the existing guarded
capture; this test does not silently bypass that phase.
A [fixed-address joined arena differential](../experiments/physx333-offline/joined_arena_diff/README.md)
compares the same component restore against a raw allocator checkpoint. In
four cold/warm and SAP-order variants, component A/B and direct/no-input
successors match. The warm successor allocation ledger and all non-padding
bytes converge exactly, but restored warm A retained 121–123 differing native
bytes before the statistics fix. The immediately readable
`Sc::SimStats::numTriggerPairs` byte is now restored by `SceneClockImage`,
leaving 120–122 warm-A bytes in retained array capacity or task scratch.
Those tails have not been proven safe for all histories, so complete native
checkpoint parity is **not** yet established. Cold histories also retain five
allocator-ledger entries after component rewind and allocate five new blocks
on replay, although component and tested output comparisons pass.
A separate [joined manifold-pool image](../experiments/physx333-offline/joined_manifold_pool/README.md)
checks all twelve keyed PCM bindings and the physical used/free large-manifold
pool partition across A, B, and a public-API return to A. The four deleted
contacts reacquire their original slots in two independent synthetic scenes,
and the same-scene pool image returns exactly to A. This is read-only evidence
for the restore plan, not a joined contact-payload restorer or proof for an
arbitrary Unity allocation history.
The component safety pass now rejects malformed saved island-object pointers
unless they match the source-computed typed work-buffer starts, and rejects
sleep/wake list entries that are not live same-scene BodyCore pointers.
Focused negative tests prove both failures leave the scene unchanged.
These guards do not solve allocation-address reuse (ABA) or establish that a
different live body belongs in a saved sleep/wake list.
The [query image](../experiments/physx333-offline/query_image/README.md) now
also recognizes PhysX's valid committed post-tree-swap boundary without
dereferencing the stale builder input pointer or freed cached-box storage.
Its fixed-storage test passes 20 restores and query replays and atomically
rejects attempts to rewind across refit allocation, build restart, or a
second swap. Reconstructing deleted tree allocations across a swap remains
open.
A [joined motion fixture](../experiments/physx333-offline/joined_motion/README.md)
now stresses the same 12/4/2→8/2/2 component transaction under gravity,
solver-generated nonzero velocity, and ten live friction records. It passes
100 direct A/B cycles, 100 no-input-then-scripted cycles, and 100 kinetic
dash-to-deletion cycles; the last has three simulation steps with no fixture
input after A and checks every intermediate image and ordered callback. Its
auxiliary trigger box is deliberately taller than the game's unknown live
geometry to preserve the surviving trigger-history invariant, so this is a
motion stress test, not a level-geometry match.
The level-arena build also has a separate public-API cache-history check that
reaches the shipped cache ledger `currentId=13`, ten live IDs, 24 references,
and free-ID order `[12,11,10]` while retaining the semantic 12/4/2 graph.
Its temporary shapes perturb other native histories, so this is a reachability
control, not an exact f444 scene reconstruction.
Unity integration is separate: the user independently confirmed that Unity
2017.4.8.f1 uses PhysX 3.3.3, but Unity's statically linked binary layout and
game-side ABI have not been proven from the source-built fixtures.
The [f444 inventory gap](PHYSX-F444-SCENE-INVENTORY-GAP.md) also records why
the existing serialized scene and interaction-only dumps cannot yet support
a truly level-matched offline source scene.

### Why a rebuilt PhysX DLL is not a drop-in game replacement

The shipped x86 `UnityPlayer.dll` matches the installed 2017.4.8f1 PDB by
CodeView GUID `638D1878-FE24-4B65-B675-A2C146E30E24` and age 1. That PDB
lists `ScNPhaseCore.obj`, `ScScene.obj`, and low-level PhysX objects linked
from Unity's `vs2015/release` static archives. The player's normal and delay
import tables contain no PhysX DLL, and its only export is `UnityMain`.
See the [shipped-player/PDB identity audit](../../artifacts/unity-2017-physics-repro/native-debugger-symbol-audit-v1.json)
and [Unity reproduction notes](UNITY-2017-PHYSICS-REPRO.md). Thus compiling
the matching PhysX release into `PhysX3_x86.dll` cannot replace the code
already embedded in UnityPlayer.

Our source-built test DLLs can add rewind exports for **their own** PhysX
scenes. A future sidecar native module could, in principle, call or inspect
Unity-owned PhysX objects, but would need verified x86 layouts, calling
conventions, vtables/function addresses, allocator ownership, and settled
phase guards for that exact Unity binary. The installed PDB is stripped of
private type records, and our offline build uses a different compiler/toolset;
the shared 3.3.3 version does not prove those ABI details. Adding fields to a
rebuilt PhysX class would not add them to Unity's already-compiled objects.
External shadow bookkeeping would need complete, early hooks for object
lifetime and relevant state changes. These are possible research directions,
not a current game-side rewind implementation.

## Source and existing evidence

The local upstream source is the `3.3.3-1.3.3` tag of
`https://github.com/daxiazh/PhysX-3.3.git`, commit
`efd57d99e04a1b017df06a3495ec8a326988abe3`. It provides the Win32
PhysX SDK projects and the private SAP, AABB, NPhase, interaction, and island
implementations. The source is an external checkout; this repository contains
our build and test code, not a copy of NVIDIA's source.

The shipped Story 1-1 runtime uses SAP. At the observed `finishBroadPhase`
entry its broadphase object has the SAP vtable at `UnityPlayer + 0xF00080`.
The uninterrupted `f444 -> f445` transaction reports zero created and six
deleted overlaps and changes twelve contact edges to eight. The restored
transaction currently reports zero and zero, starting from no contact edges.
The full-scene Unity reproduction rewound exactly, so a scene containing only
the visible colliders is not a sufficient test fixture for this failure.

The existing native history harness uses a synthetic UnityPlayer image. It
tests several capture, rebasing, and rollback primitives but does not execute
PhysX. The source-backed harness must add that execution path while retaining
precise comparisons of private state.

PhysX's public `PxCollection` serializer cannot capture this history: a live
`PxScene` is not a serializable `PxBase` collection member, and the private
SAP/NPhase/island allocations are not public scene objects. The offline build
therefore needs test-only typed access to those internals. Any source change
for such access is applied to an ignored build copy, with a pinned source
revision; the external source checkout remains unmodified.

## Rewind contract

The checkpoint is taken only after `fetchResults` has finished and no PhysX
task is active. A test records a deterministic sequence of API calls and their
order, advances the same scene, restores the checkpoint in that scene, repeats
the calls, and compares the two continuations. Multiple rewinds to the same
checkpoint and non-adjacent checkpoints must also work.

The comparison covers both public body output and the private state that can
affect the next step: SAP pairs/endpoints and element handles, AABB change
lists, NPhase interactions and pools, reports and manifold history, transform
cache, island graph and queues, sleep/wake lists, and allocator/free-list order.
Pointer-bearing structures are compared through explicit object identities
and rebasing rules. Raw addresses are diagnostics, not equality criteria.
Opaque capacity tails are retained bytewise where subsequent allocation can
observe them.

An accepted restore first validates every identity, capacity, phase, and
free/active partition. Any prewrite rejection leaves the scene unchanged.
After a write, verification either proves the target image, proves rollback to
the prior image, or stops the test before another simulation step. The same
restoration code should be shared with the Unity helper wherever the two
builds have compatible semantics; binary-specific field access stays in
separate adapters.

## Development gates

1. Build the 32-bit source with a reproducible external-checkout setup and run
   a deterministic SAP scene with typed internal observations.
2. Prove that independent runs from the same initial scene and call trace have
   identical internal and public outputs. This validates the oracle only.
3. Add settled SAP/AABB checkpoint restore; force overlap creation, removal,
   handle reuse, capacity growth, and repeated rewind. Require pair order,
   free lists, and next-step state to match.
4. Add the NPhase interaction graph, five pools, reports, manifold data,
   transform cache, and island history as one dependency-ordered transaction.
   Start with one dynamic actor whose six separated shapes overlap six static
   colliders. Checkpoint after all six contacts exist, move it away, and
   require the same six ordered deletions and complete next-step image after
   several divergent frames and repeated restores. Then exercise the
   twelve-contact to eight-contact deletion as a required case.
5. Extend the matrix to actor and shape lifetime changes, static/dynamic and
   trigger pairs, kinematic targets, sleeping/waking bodies, and plate-like
   dash/drop collisions. Require repeated and non-adjacent replays to remain
   exact.
6. Run fault-injected preflight and postwrite tests for rejection, rollback,
   and fail-stop. Then compare the complete first step and a longer replay
   suffix for every supported case.

These gates establish PhysX-only parity for the exercised Story 1-1 feature
set. Unity's build flags, private layout, scene integration, and exact game
input history still require later calibration against the shipped binary.

## Earlier component milestones

Gates 1 and 2 pass in the pinned Win32 Release source build. The fixture has
six separated dynamic-box/static-box contacts. After the checkpoint, moving
the dynamic actor away changes six SAP pairs to zero. A fresh scene reproduces
the checkpoint, deletion frame, and settled suffix exactly across body bits,
SAP endpoints/boxes/pair chains, the BPElem active/free partition, contact
events, and public simulation statistics. Run the source-backed oracle with:

```powershell
cmd /c experiments\physx333-offline\harness\Build-Harness.cmd
```

The additional `--public-rewind-probe` is a negative control. Restoring only
the body's public pose/velocities in the same scene and repeating the deletion
step yields zero contact-event words, whereas uninterrupted execution yields
24 words (six four-word contact rows). This is a fast, source-level reproduction
of a missing contact predecessor. It is not yet a rewind fix.

The SAP/BPElem component of gate 3 also passes in the same source-built scene.
Its test-only image owns complete declared-capacity SAP buffers, the BPElem
backing and AABB data free lists, change lists, transient pair outputs, scalar
metadata, and actor/shape bindings. Restore accepts only the same scene and
allocation topology. It validates saved and live structure before writes,
recaptures and checks exact equality afterward, and rolls back on a failed
postwrite check. The harness proves duplicate capture, 100 exact checkpoint /
deleted-state round-trips, and atomic rejection of a corrupted image. It
restores the deleted SAP state before resuming simulation, because NPhase and
island history are not restored yet. Capacity changes and cross-allocation
rebasing remain outside this component's admission. The production Unity
helper remains read-only for SAP/BPElem and does not apply the full predecessor
transaction.

The low-level `PxsTransformCache` and its `Cm::IDPool` now have a separate
same-scene component image. It preserves the full transform and reference-count
allocations, including capacity tails, plus the ID high-water mark and ordered
free-ID stack. In the six-contact fixture it passes duplicate checkpoint
capture, 100 checkpoint/deleted-state round trips, and atomic rejection of a
corrupt high-water mark. This component likewise cannot be used alone before
simulation: ShapeSim IDs, contact managers, and islands still own references to
the old topology. It rejects an allocation-address or capacity change; growth
of the free-ID array requires a later allocator-aware restore.

A read-only NPhase/contact/island oracle now passes the same checkpoint,
six-deletion, and settled-suffix comparisons between independent scenes. It
captures 39 ordered sections including interaction and ActorPair topology,
six NPhase pools, contact-manager free order and work units, contact streams,
single-manifold contacts, report/event lists, and island nodes, edges, islands,
free lists, bitmaps, and change queues. Duplicate capture at the settled
checkpoint also agrees. In the public-only rewind negative control, this
oracle differs at the contact-manager free stack even before considering the
six lost-contact callbacks. The fixture explicitly checks six found-contact
callbacks at checkpoint and six lost-contact callbacks on deletion.

This is observation coverage, not a restore implementation. The oracle marks
free-slot payload and allocator tails, the filter-pair pool and dirty set,
constraint/articulation payload, and solver/friction backing as unsupported;
multi-manifold contents are flagged if encountered. The next milestone is a
same-scene NPhase/contact/island transaction that restores the checkpoint and
reproduces the full next-step image. Until then, no PhysX-only rewind parity
claim is warranted.

The first source-lifecycle probe is narrower than that transaction. Build the
disposable DLL with `-NPhaseBridge`, then run the harness with
`--nphase-topology-probe`. A test-only export calls the original
`NPhaseCore::onOverlapCreated` path for six preflighted box pairs. The probe
first proves that a mismatched filter flag is rejected without mutation; it
then recreates the six ordered shape pairs, manager presence, and ActorPair
reference topology. Full touch state still differs. The probe runs in its own
process and exits without another simulation or normal scene teardown, because
SAP, contact caches, and islands are not yet jointly restored. The bridge is
our own C++ code compiled into an ignored source mirror; the pinned external
PhysX checkout remains unchanged.

An additional `--nphase-reverse-probe` isolates allocation order. In the
checkpoint, the six shape pairs own SIP and contact-manager slots
`5,4,3,2,1,0` by mover-shape index. Recreating in checkpoint interaction
order gives `0,1,2,3,4,5` and loses those pair-to-slot bindings. Recreating
in reverse order restores the exact `5,4,3,2,1,0` binding for each shape,
though InteractionScene enumeration is then reversed and touch, report,
manifold, island, and allocator history still differ. This supports a
lifecycle-first restore followed by an explicit ordered-container and payload
projection; it is not a whole-scene rewind result.

The `--interaction-metadata-probe` now continues that reverse-order lifecycle
path: it restores physical SIP/ActorPair/contact-manager slot bindings, puts
InteractionScene and the mover's interaction array back in checkpoint order,
then uses a second test-only source bridge to allocate six lazy ActorPair
report objects in their original pool slots. It restores the persistent event
list and SIP/ActorPair/report/manager touch metadata. The NPhase topology and
touch image then matches, and the first remaining oracle difference is the
contact manager's contact count (`4` versus `0`). The probe still exits
without simulating because contact streams, manifolds, memory blocks, island
graph, and body/scene state are not yet a joined transaction.

A separate island image now captures manager-owned graph pools, ordered free
chains, bitmaps, queues, counters, and work backing. Same-topology idempotent
restore passes 100 times, and malformed images or missing contact-edge
bindings are rejected before mutation. It does not yet restore a deletion
successor to a contact-bearing predecessor. A read-only memory-block-pool
image also passes duplicate capture at checkpoint and deletion, covering
ordered tracking arrays, stream selectors, counters, and all 16 KiB block
contents; this fixture reports no unsupported allocations. Its address-to-ID
registry cannot detect a free-and-reallocate at the same address entirely
between captures. Full raw block bytes are retained for eventual same-scene
restore, but independent-scene equality cannot compare uninitialized tails.

The semantic oracle excludes `PxcNpWorkUnit::prevSolverConstraintSize`, which
the pinned source never reads or writes, and compares only the fields and
contact bytes actually consumed by `PxcNpCacheRead2`, not its unwritten
16-byte alignment padding. Extra observer allocations exposed both as
independent-scene false positives; the source-backed normalization restores
fresh-scene oracle equality.
