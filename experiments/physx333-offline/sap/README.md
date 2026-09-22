# Cold AABB result-array rollback

`Build-Check.cmd` builds and runs a Win32 fixture against the locally built,
pinned PhysX 3.3.3 source mirror. It creates one box/box overlap, captures a
settled checkpoint before the AABB manager has ever allocated its deleted
overlap result array, deletes the overlap, and restores the checkpoint's SAP
image. The check also verifies that malformed images and unrelated capacity
changes are rejected without modifying the scene.

PhysX allocates 32 result slots on the first deleted overlap. The manager owns
this buffer, and `Sc::Scene::finishBroadPhase` consumes its contents before
`fetchResults` returns. `RestoreSap` accepts exactly this saved state (null
buffer, zero capacity and size) against a live 32-slot buffer. It holds the
live allocation until the restored image is verified, reattaches it if
verification fails, and otherwise frees it with PhysX's allocator.

The fixture ends after the SAP-only restore because NPhase and islands still
describe the later state. Allocation growth beyond 32 slots, other allocation
changes, and whole-scene rollback require additional components and are not
accepted by this restore path.
