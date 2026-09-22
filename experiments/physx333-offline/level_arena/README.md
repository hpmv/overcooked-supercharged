# Source-built level-graph allocation rewind diagnostic

Run from the framework root after building the pinned Win32 PhysX 3.3.3
source mirror:

```bat
cmd /c experiments\physx333-offline\level_arena\Build-Check.cmd
```

This reuses the [level-shaped fixture](../level_graph/README.md): four chef
capsules, one isolated active body, eight shared static endpoints, and the
settled `12 contact / 4 trigger / 2 marker` graph. It constructs only one
source-built PhysX scene. After checkpoint A it follows five poses:
separate, stay separated, return, separate, return. The first step makes the
same *semantic* four-contact/two-trigger deletion set as the game trace.
The sequence is recorded once and then repeated from the same A image for
100 rewinds. At every replay step the test compares all 39 implemented
source Oracle sections, the full trigger/marker auxiliary image, ordered
scene/actor graph and callbacks, SAP/cache/manifold/island facts, the
allocation ledger, and every initialized byte in its fixed-address arena.

The only normalized comparison bytes are exactly 75 unwritten padding bytes
inside 25 embedded `PxsComputeAABBParams` task copies. Their offsets are
derived from pinned source types and checked at compile/run time. The
snapshot itself retains those raw bytes. A difference in any other captured
byte fails the test. The shared padding normalizer also runs the older
12-contact arena regression fixture.

This is a **diagnostic source-level rewind**, not the component-by-component
restorer and not a Unity implementation. All PhysX allocations in this
fixture pass through the diagnostic allocator and stay at their old
addresses; the game does not use it. The test uses an inline dispatcher,
does not destroy actors or shapes across this suffix, and does not capture
OS threads/locks, allocations outside the callback, Unity-owned state,
Animator state, or user-side effects. Source-built graph geometry, history,
and callback order also differ in documented ways from the actual level.
The result proves exact replay for this one source-built graph and call
trace; it does not prove complete PhysX-only or game rewind parity.
