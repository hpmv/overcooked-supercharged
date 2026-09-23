# Offline one-swap query rewind experiment

This fixture tests one narrowly defined PhysX 3.3.3 Win32 scene-query
transition: a settled dynamic AABB pruner at `BUILD_INIT`, followed by one
completed progressive tree build and committed swap. At the latter boundary,
PhysX has deleted the former active tree and freed its cached bounds. The
ordinary `QueryImage` restore correctly rejects that transition.

`RestoreAcrossOneQuerySwap` is a separate offline-only experiment. It checks
same-scene ownership, source phase and pointer relationships, stable backing
for every other captured field, and exact image sealing before mutation. A
small bridge compiled into a private source mirror creates a former active
tree, an empty second tree, and cached bounds using the source's own
allocator. It holds the current tree as rollback escrow until capture and
semantic comparison of the restored boundary succeeds. A forced verifier
failure tests that escrow rollback returns to the exact live pre-restore
image and allocation totals. If rollback verification itself fails, the
experiment aborts rather than returning a potentially corrupted scene.
The caller must externally exclude concurrent queries and scene mutations
throughout the operation; a settled simulation alone does not establish
that exclusivity.

The test uses the public scene API to exercise raycast and overlap and then
replays the swap for 20 cycles. It compares every captured field after
rebasing source-owned tree/array pointers, except four uninitialized padding
bytes in each 24-byte compressed AABB-tree node. The comparison checks all
three compressed coordinates and the complete 64-bit node bitfield; it is
therefore semantic node parity, **not** exact live byte parity. At both
boundaries, the counting PhysX allocator must return to the same live
allocation count and byte total on each cycle. Wrong-phase and different-
scene attempts must reject without changing ownership.

From the repository root, run the self-contained check. It builds the private
source mirror before the fixture:

```powershell
cmd /c experiments\physx333-offline\query_cross_swap\Build-Check.cmd
```

The build script requires the pinned PhysX 3.3.3 source revision and writes
only under this folder's ignored `work/` and `out-ninja/` directories. It
does not edit the vendor checkout, the existing `QueryImage` restore gate,
or the shipped game. The support is confined to this one static-shape plus
32-dynamic-shape fixture with an unchanged pool, stable tree-map allocation,
empty bucket, no pending fixups, and a cold empty second tree at the saved
boundary. It neither proves arbitrary cross-swap rewinds nor preserves
allocator address/history. No shipped Unity/PhysX ABI compatibility or
game-side integration is claimed.
