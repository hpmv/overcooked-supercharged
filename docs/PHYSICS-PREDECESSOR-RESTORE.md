# Story 1-1 physics predecessor restore

This is the implementation plan for exact local-only rewind parity at the
first restored transition, framework frame 444 to 445.  It consolidates the
read-only evidence so future work does not regress into repairing one visible
count at a time.  It is tied to the shipped 32-bit UnityPlayer/PhysX 3.3.3
binary and is not a portable PhysX serializer.

Search stays disabled until the complete transition and post image match.
The restore must preserve ordinary forward behaviour, including plate
throwing, so it may project checkpoint history at the paused rewind boundary
but must let the shipped simulation consume that history normally.

## Proven causal split

The checkpoint owns three different artifacts:

1. the settled f444 predecessor image;
2. the shipped f444 to f445 broadphase/island transaction;
3. the complete managed f445 output image.

The uninterrupted transition has:

```text
finishBroadPhase: created 0, deleted 6
first island update: 12 contact edges -> 8
island removals: edge IDs 8, 9, 10, 11, in that order
```

The restored transition currently has `0/0`, `0 -> 0`, and no island
journal.  Eight survivor contacts appear later during f445, but those late
creations cannot retroactively supply the twelve-edge predecessor or the
canonical deletions.  At the f445 output boundary only frame identity, the
sphere-manifold pool, TransformChangeDispatch, and the empty dirty set match.
The manager/SIP/ActorPair/report pools, report buffer, InteractionScene,
TransformCache, island state, and large-manifold pool differ.

This means the primary defect is the missing predecessor transaction, not ten
independent output-count defects.

## Implemented island primitive

Commit `c5cac22` adds native API 19's unwired atomic island projection.  It
rebases the saved nine nodes and twelve contact edges to current BodySim,
BodyCore, SIP, manager, node-ID, and edge-ID identities.  It captures a
rollback image, validates storage and phase, publishes backing arrays before
metadata, rereads the result, and either proves the commit, proves rollback,
or fail-stops with its gate closed and epoch odd.

It is not called by managed code.  Before live wiring its harness still needs
a deterministic post-write fault that exercises rollback success and terminal
fail-stop.

Island projection is the last raw predecessor write.  Publishing it earlier
would allow later shipped interaction allocation to change edge IDs and
invalidate its bindings.

## Complete NPhase pool image

Native API 20 and managed r23 implement the read-only capture described in
this section for all five pools.  The implementation also validates the
`InlineArray<void*,64>` storage mode: inline slab tables live at `pool+0x04`,
have masked capacity 64, and require `InlineAllocator::mBufferUsed == 1` at
`pool+0x104`; grown external tables require capacity greater than 64 and used
byte zero.  Header, table, slabs, free order, and allocation bitmap are reread
before publication.  Managed sidecars cross-check the generic image against
the specialized pool captures and InteractionScene partition.

The current comparator is intentionally raw/address-sensitive and exposed as
`nphasePoolImagesRawEqual`.  It is appropriate for repeated-capture identity,
not final rebased parity.  The projected comparator remains part of the
semantic-relocation step below.

Every one of these pools is `0x128` bytes and uses 32 elements per slab:

| Kind | NPhase offset | element | slab |
| --- | ---: | ---: | ---: |
| ActorPair | `0x090` | `0x18` | `0x300` |
| ShapeInstancePairLL | `0x2e0` | `0x44` | `0x880` |
| TriggerInteraction | `0x408` | `0x3c` | `0x780` |
| ActorPairContactReportData | `0x530` | `0x24` | `0x480` |
| ElementInteractionMarker | `0x658` | `0x28` | `0x500` |

The common metadata is:

```text
+108 slabs.data
+10c slabs.size
+110 slabs.capacityRaw
+114 elementsPerSlab
+118 used
+11c unreleasedFree
+120 slabSize
+124 freeHead
```

A sufficient checkpoint captures the raw capacity ownership bit, slab bases,
the free chain as slab/element ordinals, an allocated bitmap, and every byte
of every slab.  Capturing only allocated objects and free-list order loses
behaviour-relevant stale tails: report construction leaves several counters
untouched, trigger construction leaves cache fields untouched, and pool
release overwrites only the free slot's first pointer.

For the first restore, require equal slab counts and compatible raw capacity
mode.  Do not grow or release slabs.  The allocated/free partition must be
total and disjoint, and the free chain must be unique, acyclic, aligned, and
exactly `unreleasedFree` elements long.

## Semantic relocation, not historical pointer replay

Raw checkpoint bytes contain addresses.  Exact parity is defined as exact
bytes after one explicit target-to-live semantic projection, not equality of
historical virtual addresses.

The projection maps:

- actors, ShapeSims, ShapeCores/PxsShapeCores, PxShapes, and PxActors;
- SIPs, triggers, markers, ActorPairs, report objects, managers, and
  manifolds;
- Scene/context/material-manager singleton roles;
- graph indices, manager slots, island edge IDs, and TransformCache IDs.

Allocated pool objects use declared field relocations.  Free slots copy their
opaque bytes from `+4` onward and rewrite only the intrusive next pointer at
`+0`.  Vtables are normalized to UnityPlayer RVAs and must retain the same
RVA.  Never scan arbitrary stale bytes for pointer-looking values.

Important special cases:

- SIP `+0x3c` is an island edge ID, not a pointer.
- report `+0x00` is a report-buffer offset, not a pointer.
- manager transient pointers at `+0x10`, `+0x1c`, `+0x28`, `+0x2c`,
  `+0x38`, and `+0x44` must be zero or have an explicitly captured owning
  arena and payload; otherwise preflight fails.
- both manifold classes contain a self-relative pointer at `+0x2c`; it must
  become `live manifold + 0x30` while preserving its tag.
- pointer-bearing hashes saved at the target are diagnostics.  Recompute
  hashes from the projected image before comparison.

## Report-history completion

Capture and restore both Scene timestamps at `ownerScene + 0x4c` and
`ownerScene + 0x50`.  Preserve their exact values and equality relation; do
not assume either must equal an object stamp in every phase.

For each of the three NPhase report arrays capture its raw capacity ownership
bit, logical order, and the complete `capacity * 4` backing bytes.  Capture
the entire retained report-buffer allocation, its active bounds, and typed
relocations for any active records.  Target f444 has an 8192-byte allocation
with active index zero, so its bytes are opaque allocation history and no
active record relocation is presently needed.

Target f444 has twelve ActorPairs: ten touched/report-owning and two
untouched/null-report.  The transient ActorPair report set is empty; the
persistent SIP list has ten entries and split index ten; the force-threshold
list is empty.  Those are coherent historical states, not corruption.

## Broadphase boundary

At `Sc::Scene::finishBroadPhase` entry, publishing the six ordered target rows
to `PxsAABBManager::mDeletedOverlaps` is equivalent to the original high-level
deletion path from that boundary onward only if the predecessor is already
exact.  The shipped loop then calls `NPhaseCore::onOverlapRemoved` in row
order, performs its normal swap-removals, updates pools and islands, frees the
temporary overlap list, and continues unchanged.

Do not call `onOverlapRemoved` manually.  Use the existing finishBroadPhase
detour and let its original trampoline run once.

Row injection is not yet admitted.  By this hook the SAP implementation has
already removed persistent pairs and repaired/compacted its pair structures.
A fresh target/replay capture must compare the complete post-update SAP image,
BPElem semantic mapping, AABB capacities, and all six still-live NPhase
interactions.  The choices are:

1. exact SAP equivalence: inject only the six rows;
2. semantically identical handles and compatible storage: project a fully
   captured SAP image, then inject (still unproved);
3. any identity/capacity/history mismatch: fail closed.

The six target rows, in order, are:

```text
4D537580 : 4D49BD00  contact
4D537760 : 4D49BD00  contact
4D49B680 : 4D49BD00  contact
4D49B9C0 : 4D49BD00  trigger
4D49BAC0 : 4D49BD00  contact
4D49BAE0 : 4D49BD00  trigger
```

Addresses above identify the recorded target incarnation only.  The live
preflight must resolve the same semantic elements and preserve row
orientation/order.

## One transaction, in dependency order

The planned restore is:

1. capture a complete target and live rollback image;
2. fail closed on revision, phase, capacity, FilterPair, transient-arena, or
   ambiguous semantic mapping;
3. use shipped lifecycle paths to retain two triggers/two markers, convert
   the eight corresponding markers to SIPs, create four missing SIPs and two
   missing triggers, and allocate ten report objects;
4. freeze lifecycle allocation/destruction and build complete object, pool
   slot, graph-index, cache-ID, node, and edge maps;
5. project owned payloads: TransformCache, report buffer, supported manager
   arenas, and manifolds;
6. project report objects, ActorPairs, managers, SIP/trigger/marker objects,
   InteractionScene arrays, per-actor arrays, active bodies, and report
   containers;
7. publish the atomic island image last;
8. install each pool's opaque free tails and intrusive free chain, then pool
   metadata, with no subsequent allocation;
9. prove projected target equality across every family;
10. at finishBroadPhase entry, validate SAP and inject the six deletion rows;
11. require the shipped transition to produce `0/6`, `12 -> 8`, removal IDs
    `[8,9,10,11]`, and the complete target f445 post image.

All potentially allocating lifecycle calls occur before raw projection and
need shipped inverse operations in the rollback log.  Raw projection contains
only prevalidated writes.  If post-call broadphase output diverges, it is too
late for an ordinary rollback; pause/fail-stop before another frame and retain
diagnostics.

## Immediate implementation sequence

1. **Complete:** add the generic five-pool caller-owned read-only snapshot and
   tests (API 20 / managed r23).
2. Extend report capture with timestamps and complete backing arrays.
3. Add complete read-only SAP/BPElem capture at the existing hook.
4. Run one fresh no-search target/replay audit and choose the admitted
   broadphase path from evidence.
5. Add projected comparison without changing the address-sensitive repeated
   capture comparator.
6. Add allocation/materialization planning and fault-injected rollback tests.
7. Enable the atomic predecessor restore only after every dependency has a
   complete capture, projection, and validator.
