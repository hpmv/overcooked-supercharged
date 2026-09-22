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
it does **not** rewind those interactions yet. Actor and
shape lifetime, CCD, and several other native-state families remain gated.
Unity integration is separate: the user independently confirmed that Unity
2017.4.8.f1 uses PhysX 3.3.3, but Unity's statically linked binary layout and
game-side ABI have not been proven from the source-built fixtures.

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
