# Current offline PhysX 3.3.3 gates

After building the pinned Win32 PhysX source mirror, run from any directory:

```bat
cmd /c experiments\physx333-offline\Run-Current-Gates.cmd
```

The script stops on the first failure. It builds and runs the six-contact
joined rewind, the box/box and capsule/box 12→8 cold/warm joined fixtures,
their mixed-contact fresh-scene baselines, the read-only trigger/marker
baseline, the level-shaped shared-endpoint fresh-scene graph, a shared
ActorPair ownership observer, and the
raw-arena replay/lifetime diagnostics. Joined fixtures
repeat their next-step comparisons 100 times; the arena fixture also checks
five-step continuations 100 times. The separate level-shaped allocation
diagnostic repeats a joined contact/trigger/marker five-step suffix 100 times
inside one source-built scene. An isolated source-native trigger fixture
reconstructs two deleted capsule/box trigger interactions for 100 cold and
100 warm same-scene rewinds.

These are **current covered gates**, not a test for complete level or Unity
parity. The [remaining trigger/marker and graph-aware rewind work](../../docs/PHYSX-TRIGGER-MARKER-JOIN-PLAN.md)
is still open. The script never launches the game.
