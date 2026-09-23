# Joined actor/shape lifetime boundary

This Win32 source-built PhysX 3.3.3 diagnostic uses the existing synthetic
12-contact, 4-trigger, 2-marker world at the cold A boundary. At settled
checkpoint A it releases interacting static actor ID 1 and its one box shape,
recreates both with the
same public construction inputs, and settles one more step. It tests existing
component restore guards individually. It does not launch the game or edit
the PhysX source/build mirror.

Run from the framework root:

```bat
cmd /c experiments\physx333-offline\joined_actor_lifetime\Build-Check.cmd
```

## Measured boundary

The pinned build reuses all five measured addresses: `NpRigidStatic`,
`NpShape`, `Sc::ActorSim`, `Sc::ShapeCore`, and `Sc::ShapeSim`. The fixture
asserts that reuse. The replacement's `Sc::ShapeSim` ID is **14**, while A's
was **0**. The transform-cache ID is invalid immediately after recreation
and returns to **1** after the settling step. The shape-ID tracker changes
from current ID 14/free count 0 to current ID 15/free count 1. Thus even
matching public and internal pointers, plus a matching settled cache ID,
do not establish the original object's lifetime identity.

The settled graph still has **12/4/2** interactions and the same active body
order, but scene-pair order and per-actor interaction order differ. The
`NpScene` rigid-actor array also differs: removing actor 1 replaces its entry
with the last actor, and re-adding appends the new lifetime. The test asserts
these differences. Source: `../build/work/PhysXSDK/Source/PhysX/src/NpScene.cpp:721-777`;
`../build/work/PhysXSDK/Source/SimulationController/src/ScShapeSim.cpp:40-80`;
`../build/work/PhysXSDK/Source/SimulationController/src/ScObjectIDTracker.h:33-51`.
The latter defers released shape IDs, explaining why an immediate
recreation gets 14 rather than the just-released 0; `ScScene.cpp:1745-1750`
drains pending IDs later.

Three A-image restore calls all reject before writing, and a full changed
component capture remains identical after each rejection:

| Component | Exact reported reason | Source of guard |
| --- | --- | --- |
| Scene clock | `Np.rigidActorOrder identity, allocation, or ordering changed` | `../scene_clock/SceneClockImage.cpp:163-173` |
| Shape-cache bindings | `shape-cache identity, target ID, or reference count is invalid` | `../shape_cache/ShapeCacheBindings.cpp:173-188` |
| SAP | `BPElem actor/shape identity changed at slot 1` | `../sap/SapImage.cpp:889-913` |

The SAP binding comparison includes the element slot, AABB handle, owner ID,
shape/rigid cores, and other fields (`../sap/SapImage.cpp:458-465`). The
first sorted binding already differs by slot and handle despite the selected
actor's reused addresses. These are independent negative gates; this fixture
does not run a partially successful joined restore.

As a reference only, `ArenaSnapshotAllocator::restore` returns the arena's
allocation ledger and every byte to A. The fixture also restores its external
actor vector, memory-block registry, and callback rows, then verifies exact
raw arena equality and the A component snapshot captured by `LevelGraph`.
Across the release/recreate path the arena ledger grows from **329 to 347 blocks**, with
cursor **813888 to 815444**. This fixed-address diagnostic can resurrect the
old allocation image; it does not demonstrate a safe component-level or game
runtime lifecycle restore. Source: `../arena_snapshot/ArenaSnapshot.cpp:79-131`.

## Minimum state for a future positive path

1. Establish a generation-aware actor/shape inventory, including public
   ownership and the corresponding `Sc` simulations. Address equality is
   insufficient here because the factory pools reuse freed objects
   (`NpRigidStatic.cpp:47-73`, `NpFactory.cpp:870-914`). Record or prove the
   target lifetime before any saved pointer is applied.
2. Reconstitute the scene's registration and ID history through a source
   bridge, including rigid-actor order, `Sc::ShapeSim` IDs, deferred/free
   shape IDs, and transform-cache references. This fixture proves these
   states diverge; copying pointers or cache IDs alone cannot repair them.
3. Rebuild and validate dependent SAP bindings, interaction/pair ordering,
   contact and auxiliary interaction ownership, query-pruner entries, and
   their allocation identities before the remaining component stages.
   `NpScene.cpp:767-799` sets up and tears down scene-query shapes along
   with scene membership. The current SAP, graph, and scene-clock images
   correctly reject this lifetime
   change. A future positive test must verify both restored A and branching
   continuations; until then, retaining the rejection is the supported path.

The source-built allocator and fixture are diagnostic. This test does not
establish a Unity, Animator, or shipped-game rewind boundary.
