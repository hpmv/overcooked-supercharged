# Joined source-built PhysX 3.3.3 component replay

Run from the framework root after building the isolated `joined_topology`
source bridge:

```bat
cmd /c experiments\physx333-offline\joined_full_replay\Build-Check.cmd
```

This uses only the source-built offline level-shaped graph. It does not load
Unity or the game, modify the pinned PhysX checkout, or provide a binary
replacement for statically linked Unity PhysX.

The fixture captures a settled 12-contact/4-trigger/2-marker checkpoint A,
then its 8/2/2 successor B. It recreates the four lost contacts and two lost
triggers through the isolated original-source NPhase bridge, restores report
ownership and touch history, installs all twelve WorkUnits and PCM manifolds,
restores the contact memory-block pool, then restores island, SAP, transform
cache, ShapeSim cache IDs, bodies, scene clock, low-level context, and scene
query. No simulation is permitted until the complete stopped A image and
exact graph-keyed contact image both match. A rejected stage exits the
process without simulating or destroying the partial scene.

The cold and warm baselines, with either default or alternate SAP creation
order, each pass 100 same-scene A→B component restore/replay cycles, including
full component-defined A/B image and ordered callback comparison. The default
build check runs all four variants. A single variant can be selected with
`--warm`, `--shipped-order`, or `--shipped-order-warm`. Component comparators
use only their documented address-rebase policies for the cache free-ID array
and query FIFO stack.

The test also exercises a later checkpoint C restored from a three-step-later
deleted state E. Its first replay step uses **no** fixture pose or velocity
setters, and all three successor images N, D, and E match over 100 cycles in
all four variants. At E, PhysX's progressive query-build FIFO can
retain different bytes beyond its logical `mStack.size()` even though the
active entries and every other image field match. The fixture prints the
original full-buffer difference, then permits one explicitly scoped semantic
comparison: both FIFO sizes and capacities must match, each 8-byte active
entry must match bytewise, only bytes at indices `>= size` are normalized,
and the existing rebased-stack comparator must then accept every other
field. Source `SqAABBTree.cpp` reads only entries below `size`; `PsArray.h`
constructs a future push at `size` before any read. No general query
comparator or restore preflight was changed. A difference in any active FIFO
entry still fails the gate.

Even a fully passing synthetic graph cannot establish the shipped level's
allocation history, exact geometry/body state, additional actors, Unity ABI,
or unsupported CCD/constraint/articulation/actor-lifetime paths.
