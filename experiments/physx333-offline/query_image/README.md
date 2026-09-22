# PhysX 3.3.3 settled scene-query image

`QueryImage` is a same-scene source-layout component for the pinned
`3.3.3-1.3.3` Win32 source build. It captures `Sq::SceneQueryManager` dirty
timestamps, dirty bitmaps, ordered dirty list, and both AABB pruners' active
pool, handle maps, tree nodes, refit state, tree map, and rebuild counters.
For `BUILD_INIT` and `BUILD_IN_PROGRESS`, it also captures the second tree,
cached boxes, builder counters, and the FIFO node stack. Most allocations
must remain fixed. One guarded exception admits growth of the dynamic new
tree's FIFO backing array: when a later build doubles its capacity, the old
checkpoint address is already freed. Restore copies the checkpoint entries
to the currently owned buffer and lowers its *logical* capacity; the next
build step grows the array naturally. It never writes to the freed address.
`equalsWithRebasedStack` compares this one buffer by logical capacity and
content. A second, narrower exception reverses a cold `BUILD_INIT` checkpoint
after the first `BUILD_IN_PROGRESS` step allocates indices, nodes, and FIFO.
It strictly preflights the exact phase and unchanged unrelated topology, then
invokes the source tree's `release()` through a test-only DLL export and
restores the checkpoint fields. The restored checkpoint must pass raw `equals`.
Because release frees storage, a failed postcondition terminates the probe;
preflight rejections leave the scene untouched. On replay the new tree's
addresses can differ. `equalsWithRebuiltColdTree` compares that first build
step by initialized node bits, index data, and FIFO node offsets; the node
AABB bytes have not yet been initialized by PhysX. Later build steps are
outside that comparator. The source checkout and game are untouched.

Source basis:

- `NpSceneQueries.cpp::multiQuery` calls
  `SceneQueryManager::flushUpdates()` before most raycasts, overlaps, and
  sweeps. A `const` query can therefore mutate the query manager.
- `SqSceneQueryManager.cpp::flushShapes` drains the dirty list and updates
  pruner bounds and timestamps. `flushUpdates` then commits both pruners.
- `SqAABBPruner.cpp::updateObjects`, `commit`, and `buildStep` retain pool
  bounds, tree nodes/refit marks, tree map, rebuild phase, and counters.
  `buildStep` can allocate a second tree and cached bounds during a later
  simulation step. `SqAABBTree.cpp::progressiveBuild` retains the FIFO stack
  and partially built node pool; its source-local `FIFOStack2` layout is
  mirrored for this exact Win32 build with a static size check.

Run from the framework root:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File experiments\physx333-offline\build\Build-PhysX333.ps1 -Configuration release -NPhaseBridge -QueryBridge
cmd /c experiments\physx333-offline\query_image\Build-Check.cmd
```

The test uses the real source-built DLL. After a `simulate`/`fetchResults`
boundary it creates a pending dynamic-shape update. Its first raycast drains
that update, and an overlap checks the updated shape. Capture and restore pass
100 pending↔flushed round trips, with exact image equality and repeated
raycast/overlap results. A raycast with no pending update leaves the image
unchanged. Corrupt-image and changed-topology restores reject without writing.
A 32-shape fixture passes another 100 pending↔flushed query replays at
`BUILD_INIT` and 100 at `BUILD_IN_PROGRESS`. A separate sleeping 32-shape
fixture tests 100 cold `BUILD_INIT`→first `BUILD_IN_PROGRESS` allocation
growth rewinds, exact checkpoint images, rebuilt-step semantic equality,
and repeated raycast/overlap results. With the actor asleep, the test
also finds one progressive build step whose allocations remain fixed,
restores the preceding query image, and replays the actual next
`simulate`/`fetchResults` step 100 times to the same query image. This last
check concerns the query component; it does not compare contact or body state.
An independent six-shape fixture reproduces the progressive FIFO capacity
1→2/address-replacement transition from the joined contact probe. It replays
the checkpoint→next-step query image and overlap result 100 times, using the
semantic comparator for the rebased address. Attempting reverse growth in the
component is rejected before mutation.

Integration calls are `CaptureQueryImage(PxScene&, QueryImage&, error)` and
`RestoreQueryImage(PxScene&, const QueryImage&, error)`.

The present gate requires a stopped scene, static AABB tree plus dynamic AABB
tree, the same scene, unchanged shape topology and pool capacity, no
uncommitted pruner changes, no bucket-pruner objects, and no queued rebuild
fixups. All allocation addresses must match except the explicitly rebased
FIFO buffer and the strictly gated cold tree release. `BUILD_INIT` and
`BUILD_IN_PROGRESS` are supported when the second tree and cached bounds
retain their allocation addresses. A dynamic new-tree FIFO buffer may have
grown after the checkpoint if its live capacity is at least the checkpoint
capacity; shrinking or replacing the stack object or node pool is otherwise
rejected.
It permits pending `SceneQueryManager` dirty shapes, which is the path
exercised above. It does not yet restore a progressive rebuild across other
allocation changes, bucket fallback, static tree replacement, pruner growth,
`eNONE`, volume caches, batched/SPU queries, or PVD query
collector history. A checkpoint in any of those states needs a wider image.

This component is not a joined rewind. In particular, changing a rigid actor
pose to match the query image requires the separate actor/body restore, and
the next simulation step also depends on broadphase, contacts, islands, and
allocator state. No Unity shipped-binary parity follows from this test.
