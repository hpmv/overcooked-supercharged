# Retained-actor scene replay differential

This is an offline PhysX 3.3.3 Win32 Release experiment for proposed rewind
approach (2). It does **not** run Unity or the game and does not establish
Story 1-1 rewind parity. Run `Build-Check.cmd` after building the pinned source
mirror described in `../build/README.md`.

The fixture reuses the synthetic 13-actor/14-shape interaction graph from
`../level_graph/LevelGraph.cpp`. It runs an eight-step script containing
pose/velocity mutations and unassisted physics steps. Each path uses the same
scene descriptor, actor/shape construction and insertion order, and script.
The controls and comparisons are:

1. Fresh scene against another fresh scene, on separate runtimes. Their public
   observations must match bit for bit.
2. Warm a scene with the script, remove **all** actors, call
   `flushSimulation(false)` and `flushQueryUpdates()`, reset the bodies' public
   initial poses, velocities, wake counters, and sleeping states, then reinsert
   the *same* `PxActor` and `PxShape` objects in the original order. A pointer
   inventory verifies their identity is retained. Replay the script and compare
   to fresh after every step. Both forward and reverse removal are tested.
3. A diagnostic control repeats step 2 but destroys the emptied `PxScene` and
   creates a new `PxScene` before reinserting the same retained actors/shapes.
   This is **not** an implementation of Unity scene replacement.

Both default and shipped-like static insertion orders are exercised. At every
step the fixture compares public actor poses, velocities, wake/sleep state,
ordered contact/trigger callbacks, five multi-hit raycasts and one overlap.
It also compares normalized source-private actor/shape IDs and broadphase
handles, actor and interaction order, scene/query timestamps, and the shape,
rigid, and query handle-pool cursors/free lists. These observations are not a
complete byte-for-byte native-state image; pointer addresses are intentionally
excluded from cross-scene comparisons.

## Observed result

Fresh-versus-fresh is publicly exact. All four **same-scene** reset variants
have bit-identical public body state *before* replay but diverge on the first
physics step: for example, chef 9's x position is approximately
`-0.00342270` fresh versus `-0.00324494` reset. Ray distance and ordered
callback results also differ. The callback multiset and normalized interaction
graph match in this script. Some body and query observations converge later,
but callback order stays different through all eight steps. Source-private
timestamps and IDs also differ.

All four **new-scene, retained-actor** controls match fresh on every observed
public and normalized source-private field over all eight steps. This isolates
the observed failure to something in the reused scene, rather than requiring
replacement of the retained actor/shape objects in this fixture. It does not
identify which scene field causes the drift, prove parity on other trajectories,
or show that Unity 2017 can replace its scene while retaining its GameObjects.

Source audit explains why the same-scene path is not a true reset:
`Sc::Scene::flush()` leaves parts of narrowphase/broadphase memory and the
ID-pool histories intact; the scene timestamp continues advancing; and the
scene-query pruning pool retains handle free-list history even when its tree is
released after its last shape is removed. See pinned PhysX 3.3.3 files
`ScScene.cpp`, `ScObjectIDTracker.h`, `SqAABBPruner.cpp`, and
`SqPruningPool.cpp`. These differences are witnesses of residual history, not
yet a causal attribution for the first pose difference.
