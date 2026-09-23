# Settled low-level context image (PhysX 3.3.3 source build)

This is a test-only, same-scene component image for the pinned source build's
`PxsContext` and `PxsDynamicsContext`. It captures values retained after
`PxScene::fetchResults`: changed-AABB bitmap storage and words; touch counts;
simulation statistics; the threshold stream and table; solver body/data pools;
world solver body and data; the last dynamics timestep and solver counts.
Configuration pointers and flags are checked as invariants. Six batch work
arrays must be empty, and their storage identities/capacities are guarded.
The Win32 thread-context stack order is observed read-only and required to
remain unchanged when this component is restored. Each cached
`PxsThreadContext` has its retained object bytes and 19 owned array buffers
imaged; capacity tails are included because `resizeArrays` uses
`forceSize_Unsafe`. Array and bitmap ownership headers are masked out of the
raw object copy, then handled with explicit allocation checks.

The image records the exact scene, actor enumeration, context, and allocation
addresses. Restore checks all identities, including every array's live backing
pointer, capacities, schema, and threshold table layout before writing. A
matching capacity alone cannot make a saved pointer safe after reallocation.
It verifies the result and rolls back the
component bytes if verification fails. It does not own topology changes.

`PxsContext::beginUpdate` only clears `mSimStats`; it does not clear the
changed-AABB bitmap or threshold data. `PxsDynamicsContext::update` replaces
`mDt` and `mInvDt` and resizes solver pools, while retained pool bytes can be
read during the next solve. These are source dependencies, not proof that each
byte independently affects the six-contact replay.

The six-box checkpoint contains one cached thread context. Its
`mConstraintSize` is 736 at warm A and 0 at deleted B. That field is consumed
by `PxsContext::mergeCMDiscreteUpdateResults` when it accumulates
`mSimStats.mTotalConstraintSize`, then `clearStats` sets it to zero. The
`mCompressedCacheSize` field follows the same path; it happens to be zero in
both states of this fixture. The explicit cached object image restores both.

The cached `mLocalChangeTouch` bitmap grows from one word to eight words
across A/B and moves to a new allocation. Its existing owner header is kept;
its saved header and payload are omitted from semantic comparison because
`Sc::Scene::postBroadPhase` calls `resetThreadContexts` before narrow-phase
tasks fetch the context. That reset clears and resizes the bitmap. The same
rule applies to `mLocalChangedActors`. All other cached-thread allocations
must retain the same backing address and capacity for this component restore.

Run `Build-Check.cmd` after the pinned Win32 Release source build. The check
uses the six-box scene, confirms duplicate captures, performs 100 A/B/A
component restorations, and verifies that corrupt capacity/table/binding
images, a stale array backing pointer, cached-thread counters, and header
masks are rejected before writes.
It does not simulate after a partial
component restore.

The component rejects CCD, constraints, articulations, aggregates, contact
modification, and nonempty batch arrays. It cannot re-create a missing cached
thread object, reorder its Win32 `SLIST_HEADER` LIFO stack, or rebase an owned
array that moved. A joined next-step test is required to determine whether
the expanded image closes the observed solver-statistics difference.
There is no claim of whole-scene rewind or Unity-binary parity.
