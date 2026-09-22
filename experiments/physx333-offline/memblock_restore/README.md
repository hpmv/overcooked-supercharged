# Same-scene PhysX 3.3.3 memory-block restore

`MemBlockRestore.h` adds a guarded writer for the read-only `MemBlockImage`.
It targets the pinned, source-built Win32 PhysX 3.3.3 offline fixture. It is
not a public PhysX or Unity restore API.

`CaptureMemBlockRestore(scene, registry, image, error)` captures the full
16 KiB contents of each tracked block, the order of every tracking array,
pool counters/selectors, scratch storage, allocation identities, and live
contact-manager stream pointers. The image and registry are assigned only
after all reads succeed.

`RestoreMemBlockPool(scene, currentRegistry, target, error)` admits a restore
only if all of the following hold before its first write:

- The stopped scene, pool, scratch allocator and array backing addresses are
  exactly those captured, with unchanged capacities and array sizes.
- Every heap allocation still exists at its original address and ordinal.
  The registry, including its next ordinal, is unchanged. Both images fully
  enumerate pool-owned blocks; external constraint arrays and exceptional
  allocations are rejected.
- Each array still owns the same set of blocks. Entry order may differ, so
  the unused array's LIFO order can be restored. No block is transferred
  between active streams or between an active stream and the unused array.
- No scratch constraint blocks or outstanding scratch suballocations exist.
  The scratch arena's address, size, stack capacity and stack position match.
- The same contact managers remain live, with identical stream pointers.
  Used stream ranges must fit in a still-allocated tracked block. After
  `fetchResults`, PhysX can leave a live contact manager pointing into a block
  already on `mUnused`; that stale payload is part of this same-scene image.
  CCD contact streams are currently rejected.

The commit path copies array entry order, all 16 KiB block payloads, scratch
arena bytes, and pool counters/selectors. It allocates no PhysX objects and
changes no array capacity. A post-write readback must match the target. On
failure it writes the saved predecessor back and verifies that rollback;
unverifiable rollback terminates the process.

`RestoreMemBlockPoolForJoin` is the companion for a larger NPhase transaction.
It requires the same scene, allocations, array storage/capacities, complete
block inventory, and contact-manager bindings, but permits an existing block
to move between active stream arrays and the unused LIFO stack. Every target
array must fit its unchanged capacity. The caller must first install the
checkpoint WorkUnit bindings and must finish contact, island, body, and scene
restoration before simulating. A synthetic active/unused ownership transfer
round-trips 100 times, and duplicate-block corruption is rejected before
writing. In the joined six-contact probe, the deletion successor has two
additional unused blocks; this variant restores their friction/cache owners
and full payload, after which the contact-manager payload stage passes.

The joined variant is **not** yet a standalone safe scene restore: while its
array contents are verified and rolled back on a failed postcheck, it does
not repair pair topology or manifold ownership. It rejects allocation
growth, changed manager identities, scratch/exceptional blocks, CCD contact
streams, and any target whose blocks or arrays are incompletely accounted for.

The source reason for the owner-array guard is
`PxcNpMemBlockPool.cpp`: `acquire()` pops `mUnused`, `release()` pushes
blocks there in LIFO order, and `releaseContacts()` and the friction/cache
swaps change which stream arrays are live. `PxcScratchAllocator.h` uses a
separate stack, so an outstanding scratch suballocation cannot be restored
by merely copying the block arrays. `PxcNpWorkUnit.h` exposes the persistent
contact, solver, friction, cache and CCD pointers that can refer to these
blocks.

Raw unused or padding bytes are copied for same-scene history. They are not
compared across freshly created scenes, where allocator residue can differ.
The ownership registry observes captures, not every allocation call; a
free-and-reallocate at the identical address entirely between captures is
invisible. The writer therefore requires an unchanged registry and is not
yet suitable for arbitrary scene histories. It also assumes images come from
the trusted capture function; it cannot validate semantic contents of every
byte in an intentionally edited active contact stream.

`Build-Check.cmd` builds the component and runs the focused test with the
source-built 32-bit SDK. The test changes the unused-block LIFO order, one
stale byte and a statistic, then round-trips 100 times; corrupt array storage
and a checkpoint-to-deletion topology change must be rejected atomically.
It also exercises 100 ownership-transfer round trips and corrupt-transfer
rejection through the joined variant.
