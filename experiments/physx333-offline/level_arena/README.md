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
100 rewinds. A second checkpoint C is taken after the first return; each
cycle also restores C from a later state and replays its own leave/return
suffix. At every replay step the test compares all 39 implemented
source Oracle sections; the full trigger/marker, ActorPair, SAP,
TransformCache, Island, memory-block, shape-cache-binding, Body, SceneClock,
Context, and Query images; ordered scene/actor graph and callbacks; the
allocation ledger; and every initialized byte in its fixed-address arena.
These are same-scene target-image comparisons, not assertions that raw
address-bearing images from two fresh scenes should be equal.
Each A and C restore is also captured and compared immediately, before any
fixture pose or velocity setter or simulation. A separate branch from A
advances one step **without applying any fixture inputs** and matches its
full component images, callbacks, arena ledger, and initialized bytes over
100 restores. This narrows the body-state blind spot in the input-driven
suffix; it is still one source-built trace, not general body-state coverage.

Each component comparison follows that observer's documented equality
contract. In particular, the memory-block comparator compares full block
payloads but not saved raw addresses; the SAP comparator permits one rebased
result-buffer address; and the context comparator skips bitmaps marked reset
before reuse. For this fixed-address fixture, the separate arena byte and
allocation-ledger comparisons cover source-owned addresses and backing bytes
through its allocator. No user-provided scratch buffer is used.

The only normalized comparison bytes are exactly 75 unwritten padding bytes
inside 25 embedded `PxsComputeAABBParams` task copies. Their offsets are
derived from pinned source types and checked at compile/run time. The
snapshot itself retains those raw bytes. A difference in any other captured
byte fails the test. The shared padding normalizer also runs the older
12-contact arena regression fixture.

The same build check separately runs `--cache-history`. It uses public PhysX
calls to add four temporary, non-touching contact shapes to one existing
static actor, giving the scene four new cache IDs (10–13). Detaching them in
order 12, 11, 10, 13 and settling leaves the original 12/4/2 semantic graph,
five active bodies, ten live cache IDs and 24 references, but changes the
cache ledger to `currentId=13`, free-ID LIFO `[12,11,10]`—the three scalar
facts observed at shipped f444. This proves that cache history is reachable,
not that the entire resulting scene is equivalent to f444: the temporary
shapes and extra steps also alter NPhase, island, broadphase, query,
actor/shape ID, clock, and allocator histories. The main arena replay uses
its original, simpler history.

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
