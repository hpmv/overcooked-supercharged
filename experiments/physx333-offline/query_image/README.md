# PhysX 3.3.3 settled scene-query image

`QueryImage` is a same-scene, fixed-allocation component for the pinned
`3.3.3-1.3.3` Win32 source build. It captures `Sq::SceneQueryManager` dirty
timestamps, dirty bitmaps, ordered dirty list, and both AABB pruners' active
pool, handle maps, tree nodes, refit state, tree map, and rebuild counters.
For `BUILD_INIT` and `BUILD_IN_PROGRESS`, it also captures the second tree,
cached boxes, builder counters, and the FIFO node stack. This is still a
same-allocation image: a restore between rebuild phases is rejected when
PhysX allocated or freed tree storage. It checks allocation and topology identities before writing, verifies a new
capture afterward, and rolls back if verification fails. The source checkout
and the game are untouched.

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
cmd /c experiments\physx333-offline\query_image\Build-Check.cmd
```

The test uses the real source-built DLL. After a `simulate`/`fetchResults`
boundary it creates a pending dynamic-shape update. Its first raycast drains
that update, and an overlap checks the updated shape. Capture and restore pass
100 pending↔flushed round trips, with exact image equality and repeated
raycast/overlap results. A raycast with no pending update leaves the image
unchanged. Corrupt-image and changed-topology restores reject without writing.
A 32-shape fixture passes another 100 pending↔flushed query replays at
`BUILD_INIT`, 100 at `BUILD_IN_PROGRESS`, and rejects a restore across those
phases after the allocation graph changes. With the actor asleep, the test
also finds one progressive build step whose allocations remain fixed,
restores the preceding query image, and replays the actual next
`simulate`/`fetchResults` step 100 times to the same query image. This last
check concerns the query component; it does not compare contact or body state.

Integration calls are `CaptureQueryImage(PxScene&, QueryImage&, error)` and
`RestoreQueryImage(PxScene&, const QueryImage&, error)`.

The present gate requires a stopped scene, static AABB tree plus dynamic AABB
tree, the same scene and allocation addresses, unchanged shape topology and
pool capacity, no uncommitted pruner changes, no bucket-pruner objects, and
no queued rebuild fixups. `BUILD_INIT` and
`BUILD_IN_PROGRESS` are supported when the second tree, cached bounds, and
FIFO stack have the same allocation addresses and capacities at both ends.
It permits pending `SceneQueryManager` dirty shapes, which is the path
exercised above. It does not yet restore a progressive rebuild across
allocation changes, bucket fallback, static tree replacement, pruner growth,
`eNONE`, volume caches, batched/SPU queries, or PVD query
collector history. A checkpoint in any of those states needs a wider image.

This component is not a joined rewind. In particular, changing a rigid actor
pose to match the query image requires the separate actor/body restore, and
the next simulation step also depends on broadphase, contacts, islands, and
allocator state. No Unity shipped-binary parity follows from this test.
