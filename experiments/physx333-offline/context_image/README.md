# Settled low-level context image (PhysX 3.3.3 source build)

This is a test-only, same-scene component image for the pinned source build's
`PxsContext` and `PxsDynamicsContext`. It captures values retained after
`PxScene::fetchResults`: changed-AABB bitmap storage and words; touch counts;
simulation statistics; the threshold stream and table; solver body/data pools;
world solver body and data; the last dynamics timestep and solver counts.
Configuration pointers and flags are checked as invariants. Six batch work
arrays must be empty, and their storage identities/capacities are guarded.
The Win32 thread-context stack order is observed read-only and required to
remain unchanged when this component is restored.

The image records the exact scene, actor enumeration, context, and allocation
addresses. Restore checks all identities, capacities, schema, and threshold
table layout before writing. It verifies the result and rolls back the
component bytes if verification fails. It does not own topology changes.

`PxsContext::beginUpdate` only clears `mSimStats`; it does not clear the
changed-AABB bitmap or threshold data. `PxsDynamicsContext::update` replaces
`mDt` and `mInvDt` and resizes solver pools, while retained pool bytes can be
read during the next solve. These are source dependencies, not proof that each
byte independently affects the six-contact replay.

Run `Build-Check.cmd` after the pinned Win32 Release source build. The check
uses the six-box scene, confirms duplicate captures, performs 100 A/B/A
component restorations, and verifies that corrupt capacity/table/binding
images are rejected before writes. It does not simulate after a partial
component restore.

The component rejects CCD, constraints, articulations, aggregates, contact
modification, and nonempty batch arrays. It does not yet image the cached
`PxsThreadContext` objects or restore their Win32 `SLIST_HEADER` LIFO order. The source
`PxsThreadContext::reset` clears some streams and counters but retains other
arrays/capacities, so thread-context parity needs its own read/write audit.
There is no claim of whole-scene rewind or Unity-binary parity.
