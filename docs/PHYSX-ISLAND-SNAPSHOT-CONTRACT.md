# PhysX 3.3.3 island snapshot contract

This document specifies the caller-owned snapshot needed to reproduce the
shipped game's first PhysX island update exactly.  It is intentionally tied to
the 32-bit `UnityPlayer.dll` used by this project.  The layout was checked
against that binary, its Unity 2017.4.8f1 PDB, and the matching PhysX 3.3.3
source.  It is not a portable PhysX ABI.

The immediate oracle is the uninterrupted transition from framework frame 444
to frame 445.  A restored run must match both the state on entry to the first
island update and the state on return from it.  Matching only the final scene
objects is insufficient.

All offsets below are hexadecimal, all pointers and handles are 32-bit, and
`INVALID_NODE`, `INVALID_EDGE`, and `INVALID_ISLAND` are all `0xffffffff`.

## Shipped layout

The low-level island manager is embedded at:

```text
PxsIslandManager = PxsContext + 0x181c
sizeof(PxsIslandManager) = 0x2d8
```

Top-level members, relative to `PxsIslandManager`, are:

| Offset | Size | Member |
| ---: | ---: | --- |
| `000` | `4` | `mRigidBodyOffset` |
| `004` | `4` | `mScratchAllocator` reference |
| `008` | `4` | `mEventProfiler` |
| `00c` | `10c` | `NodeManager` |
| `118` | `1c` | `EdgeManager` |
| `134` | `18` | `NodeChangeManager` |
| `14c` | `28` | `EdgeChangeManager` |
| `174` | `30` | `IslandManager` |
| `1a4` | `18` | `ArticulationRootManager` |
| `1bc` | `4` | `mNumAddedRBodies` |
| `1c0` | `4` | `mNumAddedArtics` |
| `1c4` | `4` | `mNumAddedKinematics` |
| `1c8` | `c` | `mNumAddedEdges[contact,constraint,articulation]` |
| `1d4` | `4` | `mNumEdgeReferencesToKinematic` |
| `1d8` | `4` | `mNumRequiredKinematicDuplicates` |
| `1dc` | `1` | `mEverythingAsleep` |
| `1dd` | `1` | `mHasAnythingChanged` |
| `1de` | `1` | `mPerformIslandUpdate` |
| `1df` | `1` | padding |
| `1e0` | `7c` | `ProcessSleepingIslandsComputeData` |
| `25c` | `14` | `PxsIslandObjects` (five pointers) |
| `270` | `4` | `mBufferSize` |
| `274` | `4` | `mBuffer` |
| `278` | `60` | `IslandManagerUpdateWorkBuffers` |

The persistent managers have these exact sub-layouts:

```text
NodeManager at +00c
  +00 vtable
  +04 Node*       elems                  (absolute +010)
  +08 NodeType*   freeNext              (absolute +014)
  +0c uint32      capacity              (absolute +018)
  +10 uint32      freeHead              (absolute +01c)
  +14 uint32      freeCount             (absolute +020)
  +18 NodeType*   nextNode              (absolute +024)
  +1c uint32*[4]  bitmapWords           (absolute +028)
  +2c uint32[4]   bitmapWordCounts      (absolute +038)
  +3c byte[0xc0]  inline Cm::BitMap storage
  +fc BitMap*[4]  bitmapObjects         (absolute +108)

EdgeManager at +118
  +00 vtable
  +04 Edge*       elems                  (absolute +11c)
  +08 EdgeType*   freeNext              (absolute +120)
  +0c uint32      capacity              (absolute +124)
  +10 uint32      freeHead              (absolute +128)
  +14 uint32      freeCount             (absolute +12c)
  +18 EdgeType*   nextEdge              (absolute +130)

NodeChangeManager at +134
  +00/+04 created pointer/count           (absolute +134/+138)
  +08/+0c deleted pointer/count           (absolute +13c/+140)
  +10/+14 capacity/defaultCapacity        (absolute +144/+148)

EdgeChangeManager at +14c
  +00/+04 created pointer/count (C)       (absolute +14c/+150)
  +08/+0c deleted pointer/count (D)       (absolute +154/+158)
  +10/+14 broken pointer/count  (B)       (absolute +15c/+160)
  +18/+1c joined pointer/count  (J)       (absolute +164/+168)
  +20/+24 capacity/defaultCapacity        (absolute +16c/+170)

IslandManager at +174
  +00 vtable
  +04 Island*     elems                  (absolute +178)
  +08 IslandType* freeNext               (absolute +17c)
  +0c uint32      capacity               (absolute +180)
  +10 uint32      freeHead               (absolute +184)
  +14 uint32      freeCount              (absolute +188)
  +18 byte[0x0c]  inline Cm::BitMap       (absolute +18c)
  +24 BitMap*     bitmapObject           (absolute +198)
  +28 uint32*     bitmapWords            (absolute +19c)
  +2c uint32      bitmapWordCount        (absolute +1a0)

ArticulationRootManager at +1a4
  +00 vtable
  +04 ArticulationRoot* elems             (absolute +1a8)
  +08 NodeType*   freeNext               (absolute +1ac)
  +0c uint32      capacity               (absolute +1b0)
  +10 uint32      freeHead               (absolute +1b4)
  +14 uint32      freeCount              (absolute +1b8)
```

The four EdgeChange member pointers must always be read independently.
`preallocate` lays out one backing allocation physically as C, D, B, J, while
`resize` lays its replacement out as C, D, J, B; the member offsets above do
not change.

The four node bitmaps, in order, are `KINEMATIC`, `KINEMATIC_CHANGE`,
`NOT_READY_FOR_SLEEPING`, and `NOT_READY_FOR_SLEEPING_CHANGE`.

Raw element strides are:

```text
Node, 12 bytes
  +0 owner / articulation-link / articulation-root-id union
  +4 islandId
  +8 flags byte followed by three padding bytes

Edge, 12 bytes
  +0 node0
  +4 node1
  +8 tagged contact-manager / constraint pointer and flags

Island, 16 bytes
  +0 startNode
  +4 startEdge
  +8 endNode
  +c endEdge

ArticulationRoot, 8 bytes
  +0 articulationLinkHandle
  +4 articulationOwner
```

Node flag bits are kinematic `01`, articulated `02`, articulated-root `04`,
not-ready-for-sleeping `08`, in-sleeping-island `10`, deleted `20`, and new
`40`.

The low nibble of `Edge + 8` is tagged as follows:

| Bit | Meaning |
| ---: | --- |
| `0` | constraint or articulation; clear means contact-manager edge |
| `1` | connected |
| `2` | created |
| `3` | removed |

The payload is `taggedWord & ~0x0f`.  A set type bit with a nonzero payload is
a constraint; a set type bit with a zero payload is an articulation edge.

Expected manager vtables, expressed as Unity module RVAs, are:

```text
NodeManager             0xeff2c0
EdgeManager             0xeff2c8
IslandManager           0xeff2d8
ArticulationRootManager 0xeff2e0
```

These guards and the code signatures below must be checked before reading or
writing the layout.  ASLR means every address is `UnityPlayer base + RVA`.

## Shipped island functions

| RVA | Function / x86 signature |
| ---: | --- |
| `a63940` | `void __thiscall addEdge(self, uint32 edgeType, uint32 node0, uint32 node1, uint32* edgeHook)` |
| `a64490` | `void __thiscall removeEdge(self, uint32 edgeType, uint32* edgeHook)` |
| `a63b70` | `void __thiscall clearEdgeRigidCM(self, const uint32* edgeHook)` |
| `a650f0` | `void __thiscall setEdgeConnected(self, const uint32* edgeHook)` |
| `a65180` | `void __thiscall setEdgeRigidCM(self, const uint32* edgeHook, void* cm)` |
| `a651b0` | `void __thiscall setEdgeUnconnected(self, const uint32* edgeHook)` |
| `a65430` | private synchronous first-pass body, `void __thiscall updateIslands(self)` |
| `a65530` | public first pass, `void __thiscall updateIslands(self, PxBaseTask*, uint32 numSpus)` |
| `a655b0` | public second pass, `void __thiscall updateIslandsSecondPass(self, PxBaseTask*, uint32 numSpus)` |
| `a6c910` | `PxsContext::updateIslands` wrapper |

Fail-closed byte guards:

```text
a63940  55 8b ec 53 8b d9 83 bb 28 01 00 00 ff 56 8d b3
a64490  55 8b ec 53 8b 5d 0c 56 57 8b f9 8b 03 8d b7
a65430  53 8b d9 56 57 ff 73 08 8d 83 78 02 00 00 50
a65530  53 56 57 8b f1 e8 e6 e4 ff ff 8b ce e8 9f f6 ff ff
a655b0  55 8b ec 83 ec 08 53 56 8b f1 57 89 75 f8
a6c910  55 8b ec 6a 00 ff 75 0c 81 c1 1c 18 00 00 e8
```

The shipped CPU path calls the private first-pass body synchronously inside
`a65530`; it has completed before `a65530` returns.

All three detour targets use callee cleanup: `a63940` returns with `ret 0x10`,
while `a64490` and `a65530` each return with `ret 0x08`.  A wrapper must leave
the caller's original arguments in place for its own matching final return.

## Caller-owned snapshot ABI

The native boundary must be allocation-free and use four-byte packing.  The
following records are normative for version 1.  Every field is `uint32_t`,
including observed process addresses.

```cpp
#pragma pack(push, 4)

struct IslandNodeSlotRecord {             // sizeof == 32
    uint32_t id;
    uint32_t ownerOrArticulationRaw;
    uint32_t islandId;
    uint32_t rawFlagsWord;                 // all four bytes at Node + 8
    uint32_t freeNext;
    uint32_t nextNode;
    uint32_t slotFlags;                    // derived; never restored
    uint32_t validationFlags;              // derived; never restored
};

struct IslandEdgeSlotRecord {             // sizeof == 36
    uint32_t id;
    uint32_t node0;
    uint32_t node1;
    uint32_t taggedObjectRaw;
    uint32_t freeNext;
    uint32_t nextEdge;
    uint32_t slotFlags;                    // derived; never restored
    uint32_t semanticBindingIndex;         // ffffffff when absent
    uint32_t validationFlags;              // derived; never restored
};

struct IslandSlotRecord {                 // sizeof == 32
    uint32_t id;
    uint32_t startNode;
    uint32_t startEdge;
    uint32_t endNode;
    uint32_t endEdge;
    uint32_t freeNext;
    uint32_t slotFlags;                    // derived; never restored
    uint32_t validationFlags;              // derived; never restored
};

struct IslandArticulationRootSlotRecord { // sizeof == 24
    uint32_t id;
    uint32_t articulationLinkHandle;
    uint32_t articulationOwner;
    uint32_t freeNext;
    uint32_t slotFlags;
    uint32_t validationFlags;
};

struct IslandSipEdgeBinding {             // sizeof == 44
    uint32_t edgeId;
    uint32_t edgeType;                     // 0 contact, 1 constraint, 2 artic
    uint32_t sip;
    uint32_t hookAddress;                  // sip + 3c for a contact SIP
    uint32_t shapeSim0;
    uint32_t shapeSim1;
    uint32_t pxsShapeCoreLow;              // orientation-independent key
    uint32_t pxsShapeCoreHigh;
    uint32_t contactManager;
    uint32_t taggedObjectRaw;
    uint32_t validationFlags;
};

struct IslandEdgeJournalRecord {          // sizeof == 56
    uint32_t ordinal;
    uint32_t eventKind;                    // 1 add, 2 remove
    uint32_t observerPhase;
    uint32_t threadId;
    uint32_t edgeType;
    uint32_t node0;
    uint32_t node1;
    uint32_t preEdgeId;
    uint32_t postEdgeId;
    uint32_t hookAddress;
    uint32_t ownerObject;                  // primary SIP for contact edge
    uint32_t pxsShapeCoreLow;
    uint32_t pxsShapeCoreHigh;
    uint32_t validationFlags;
};

#pragma pack(pop)
```

`slotFlags` records free-chain membership, island-list reachability, relevant
bitmap membership, and C/D/B/J queue membership.  It exists to make validation
and diagnostics cheap; it is not source state.  Queue membership flags do not
imply uniqueness.

The call shape is:

```cpp
uint32_t __cdecl oc2_island_capture_snapshot_v1(
    uint32_t unityBase,
    uint32_t nphaseCore,
    uint32_t expectedPhase,
    const IslandSnapshotBuffersV1* buffers,
    IslandSnapshotReceiptV1* receipt);
```

`IslandSnapshotBuffersV1` supplies pointer/capacity pairs for:

- node, edge, island, and articulation-root records;
- each of the four node bitmap word arrays and the island bitmap words;
- node C and D ID arrays;
- edge C, D, B, and J ID arrays;
- SIP-to-edge bindings.

`IslandSnapshotReceiptV1` stores, at minimum:

- ABI version/size, result, Win32 error, validation kind/index/detail;
- Unity, NPhase, owner-scene, interaction-scene, context, and island-manager
  addresses;
- phase, observer sequence, observation ordinal, and capture thread ID;
- the source address, capacity, free head, free count, required/written count,
  and content hash for each of the four element managers;
- pointer, count, capacity, default capacity, required/written count, and hash
  for every C/D/B/J queue;
- source pointer, word count, required/written count, and hash for all five
  bitmaps;
- every scalar at `+1bc..+1de`, required/written binding counts, the journal
  ordinal interval covering this snapshot, and an aggregate snapshot hash.

No DLL-owned heap allocation is permitted during capture.  A sizing call may
return required counts outside the simulation hook, but an armed hook must use
already pinned/preallocated buffers.  Insufficient space makes the observation
invalid; silently truncating is forbidden.

Hard safety limits for version 1 are:

```text
nodes                 16,384
edges                 65,536
islands               16,384
articulation roots    16,384
each individual queue 65,536
SIP bindings          16,384
journal records       65,536
```

Bitmap limits are derived from the corresponding element limit.  A live count
or capacity beyond a limit must fail before dereferencing the array.

## Atomic capture and validation

A valid capture performs the following steps:

1. Resolve owner scene, interaction scene, `PxsContext`, and
   `PxsIslandManager`; check all code signatures and manager vtables.
2. Read a header `H0` containing every base pointer, capacity, queue pointer and
   count, bitmap pointer and word count, top-level scalar, and observer epoch.
3. Reject odd/in-update epochs for an external checkpoint.  An in-hook capture
   instead requires the hook's exact pre or post phase and expected ordinal.
4. Walk every free chain.  Every ID must be in range, visited once, and the
   chain must terminate at `ffffffff` after exactly `freeCount` entries.
5. Copy every physical slot, including free slots; copy every `freeNext`,
   `nextNode`, and `nextEdge` entry; copy all bitmap words.
6. Copy C/D/B/J queues verbatim.  Preserve order and multiplicity.  Never sort,
   deduplicate, or reduce opposing B/J events.
7. Validate island lists with bounded walks: no cycles, terminal nodes/edges
   agree with each island's end fields, each reached node reports that island
   ID, edge endpoints are in range or invalid, and no live list element is also
   on a free chain.  Tail bitmap bits above capacity must be clear.
8. Apply phase-aware queue checks.  C IDs refer to allocated created elements;
   D IDs refer to allocated deleted/removed elements until the update releases
   them.  B and J may contain repeated and opposing events, so uniqueness is
   not an invariant.
9. Enumerate type-zero global interactions.  Each global-array entry is the
   SIP's secondary-subobject pointer, equal to the primary SIP plus `0x08`.
   Therefore validate the managed island hook at primary `+0x3c` (global entry
   `+0x34`) against its used edge.  The two ShapeSims are at primary
   `+0x20/+0x24` (global entry `+0x18/+0x1c`).  Validate the SIP's contact
   manager against the edge payload and form the semantic key from the
   unordered pair of `PxsShapeCore` addresses.  A `ShapeSim` contains its
   `Sc::ShapeCore*` at `+0x1c`, and the corresponding `PxsShapeCore*` is
   `Sc::ShapeCore + 0x20`.
10. Resolve an already-removed contact edge through the add/remove journal,
    because its SIP hook is no longer managed and its contact-manager payload
    may already be clear.
11. Re-read the complete header as `H1` and re-hash every copied source family.
    Accept only if `H0 == H1`, both observations belong to the same allowed
    phase/epoch, and all required counts equal written counts.  Otherwise retry
    outside the hook or mark the armed hook observation invalid.

PhysX deliberately allows duplicate and competing B/J entries.
`cleanupEdgeEvents` computes their net effect, then retains the first surviving
entry in each original list.  The original order is therefore observable
state.  Deleted nodes and edges are released by walking D forward and pushing
each ID onto a LIFO free head; the resulting free-chain prefix is the reverse
of D.  D order is likewise observable state.

## Passive first-update observer and edge journal

Install one detour at `UnityPlayer + 0xa65530`.  For the expected manager,
pass, and ordinal it must:

1. claim a one-shot slot with an interlocked compare/exchange;
2. copy and validate the pre-call snapshot into fixed caller-owned storage;
3. call the trampoline exactly once with the original arguments;
4. copy and validate the post-call snapshot before returning to the caller;
5. publish the completed slot with a release operation.

The thread that arms this observation is not a simulation identity. PhysX
schedules the island-generation task on its dispatcher, so the matching
callback may run on another worker. Retain both thread IDs as diagnostics,
require each to be nonzero, and bind both embedded snapshots to the actual
callback thread. The atomic armed-to-capturing claim plus exact manager,
NPhase, pass, sequence, and ordinal—not equality with the arming caller—owns
the one transition.

The detour must not allocate, log synchronously, wait on another thread, call a
PhysX mutator, or change an argument or return path.  Under those conditions a
single `a65530` hook can passively capture the byte-exact pre/post island
transition without changing gameplay state.

That single hook is **not** sufficient to recover semantic ownership for every
deferred-deleted edge.  `removeEdge` writes `ffffffff` to the caller's edge
hook immediately, before the island update consumes D.  For contact edges the
lost hook is exactly the primary `ShapeInstancePairLL + 0x3c`.  Once the hook
and contact-manager payload are clear, a final-scene enumeration cannot infer
which SIP owned that pending edge.

Therefore install passive companion journals at:

- `a63940` (`addEdge`): capture inputs on entry and the assigned edge ID after
  the trampoline.  For a contact edge, `edgeHook - 0x3c` is the primary SIP;
  the corresponding type-zero global-array pointer is that primary SIP plus
  `0x08`.
- `a64490` (`removeEdge`): before the trampoline, capture the current edge ID,
  endpoints, hook address, primary SIP, and unordered shape-core key.  After
  the trampoline, confirm that the hook became invalid.

The journal uses a fixed ring or caller-owned array, monotonic ordinals, and an
overflow flag.  It must still call the original function if observation fails.
The add journal also covers an edge created and deleted before the next
`a65530` entry.  Constraint and articulation owners must not be guessed from
the contact-SIP offset.

## Restore transaction

Restoration is supported only at the quiescent boundary before the first
island update.  It is an all-or-nothing transaction:

1. Suspend/serialize the simulation at the intended phase and mark the restore
   epoch odd.  Disarm ordinary observation publication while writing.
2. Capture a complete live rollback image.
3. Preflight revision, scene/context identities, semantic object keys, all
   pointer readability, exact capacities, and buffer sizes.  Do not write
   anything if any check fails.
4. Restore upstream identity/order dependencies first: transform-cache
   IDs/refcounts and poses, broadphase overlap order, actor/interaction graph
   order, BodySim node ownership/hooks, and SIP/contact-manager recreation.
5. Assign current semantic objects to the recorded node and edge IDs.  Write
   the current `BodySim` owner into each node, current contact-manager or
   constraint pointers into edge payloads, and each contact SIP's `+0x3c` hook.
   Never restore a stale process pointer merely because its numeric value was
   captured.
6. Copy node, edge, island, and articulation-root raw slots into the existing
   live allocations.  Then copy `nextNode`, `nextEdge`, all free arrays, bitmap
   words, and C/D/B/J arrays in their exact recorded order.
7. Restore free heads/counts, queue counts, persistent counters, and the three
   booleans at `+1dc..+1de`.  Array base pointers, vtables, allocator
   references, and live allocation capacities remain those of the current
   process.
8. Restore the dependent NPhase/ActorPair/report-stream history before
   simulation resumes.
9. Run the complete structural and semantic validation again and compare the
   aggregate hash.  On failure, restore the rollback image before releasing
   the phase lock.  On success, publish an even restore epoch.

Version 1 requires live capacities to equal snapshot capacities.  Capacity
mismatch is not repaired with a prefix copy.  Supporting it later requires a
separately guarded resize/allocation-steering implementation that preserves
allocation order.

The required dependency order is deliberate: transform-cache and broadphase
identity influence interaction/SIP construction; SIP construction determines
contact-manager identity; those objects determine semantic edge bindings; only
then can the raw island graph and its allocator/queue history be restored.

## Frame-444 to frame-445 oracle

Arm the observer on the uninterrupted run and retain four hashes plus detailed
records:

```text
uninterrupted a65530 entry (frame 444 boundary)
uninterrupted a65530 return (first island pass in frame 445)
restored      a65530 entry
restored      a65530 return
```

Interpret the comparison as follows:

- Entry mismatch means an upstream restore dependency is still wrong; do not
  blame the island algorithm.
- Entry match and return mismatch means island topology, queue order,
  allocator order, semantic rebinding, or the implementation of this contract
  is wrong.
- Both match but later frame 445 diverges means the next observation boundary
  is `a655b0` and then `PxsIslandManager::freeBuffers`, not another special case
  in first-pass restore.

## Explicit limitations and unresolved work

- The uninterrupted frame-444/445 records are now captured under
  `artifacts/readiness-plan-island-f1048-to-f444-r2/`.  The settled, pre, and
  post hashes are `0x39B41B41`, `0x2433DECA`, and `0x74601DDB`; journal
  interval `[233,237)` contains four ordered contact removals and no overflow.
  Restored f444 repeats exactly at `0x4F772361`, proving that topology,
  allocator order, bitmaps, and SIP-edge bindings still require projection.
  The restore transaction described above is not implemented yet.
- `ShapeInstancePairLL + 0x3c` and the contact semantic key are resolved.
  The live Story 1-1 oracle proves all 12 settled edges and all four journal
  events are contacts.  Equivalent owner offsets and stable semantic keys for
  constraint and articulation edge hooks have not yet been binary-derived;
  a general restore must either resolve these types or reject a snapshot
  containing them.
- Managed code can install the journal only after the first native context
  observation at a safe post-output boundary.  An edge removed during that
  first blind interval cannot be reconstructed from history; capture fails
  closed if such a deferred edge is still relevant rather than publishing a
  guessed semantic owner.
- The persistent manager layout is resolved.  Arbitrary mid-update restore is
  not supported.  The pointer-rich region `+1e0..+2d7` owns scratch,
  kinematic-proxy, solver, wake/sleep, and second-pass state whose lifetimes
  span island phases.  Post-call observation may hash and inspect it, but it
  must not be copied back as a persistent blob.
- A snapshot taken after `a65530` but before the second pass/free-buffers
  boundary needs a separate transient-state contract.
- Raw owner and payload addresses are diagnostic evidence, not portable
  identity.  Restore requires the current node/SIP/contact/constraint objects
  found through semantic keys and the already-established upstream snapshot
  contracts.
- If asynchronous tasking is enabled in a different build, the synchronous
  `a65530` assumption and this observer are invalid.  The byte signatures and
  phase checks intentionally fail closed in that case.
