# PhysX 3.3.3 settled scene-clock component

`SceneClockImage` is a **same-scene component**, built against the pinned
`3.3.3-1.3.3` source DLL. It captures the settled `Sc::Scene` counters and
flags, `NpScene` time/phase metadata, the ordered sleep/wake body lists, and
the shape/rigid `ObjectIDTracker` free-ID stacks. It captures the five
source-defined `Sc::SimStats` counter tables individually: current and pending
broadphase adds/removes, plus trigger pairs. It also preflights immutable
scene configuration, actor pointer order, and array allocation identities.
It does not edit the vendor checkout or the game.

The source reads these fields again: `ScScene.cpp` increments the step and
report timestamps, advances global time, uses the gravity-dirty bit and
sleep/wake lists, and drains pending ID releases in `postReportsCleanup`.
`CmIDPool.h` pops recycled IDs from the back of its free list. `NpScene.cpp`
retains elapsed time and `mHasSimulated`. This image only admits a stopped
scene after `fetchResults`, with no pending tracker releases or deletion bits.
`ScSimStats.cpp` moves pending broadphase counts into current counts and clears
trigger counts at `simStart`; `readOut` exposes current broadphase and trigger
counts to `getSimulationStatistics` even before the next step. The stats object
pointer is a restore invariant, and each table's address and size is checked
against the same live scene before writing.

Run from the framework root:

```bat
cmd /c experiments\physx333-offline\scene_clock\Build-Check.cmd
```

The check passes duplicate capture; 100 A↔B restores after two different
time-step/gravity states; exact next shape/rigid LIFO ID reuse; malformed ID
and field-address atomic rejection; live-BodyCore membership checks and
atomic rejection of malformed sleep/wake pointers; and 100 A↔B component
restores at the warm/away six-contact boundary. It also checks immediate
simulation-statistics API parity for trigger and broadphase counts, and atomic
rejection of a malformed stats-table address. It deliberately does **not** step the scene
after restoring only this component.

Integration calls are `CaptureSceneClock(PxScene&, SceneClockImage&, error)`
and `RestoreSceneClock(PxScene&, const SceneClockImage&, error)`. Restore
preflights every field address, target size, invariant, array allocation, and
free-ID range before writing. It verifies the resulting capture and rolls
back to the live component image if verification fails.

Limitations: actor lifetime changes, capacity growth, changed bitmap storage,
and any other scene instance are rejected. Pointer reuse at the same address
is not generation-safe and must be guarded by the joined actor/shape image.
This component omits NPhase report-buffer payload, contact streams, broadphase,
body state, island state, scene-query pruners, task/allocator internals, and
low-level simulation statistics. Pending trigger reports, constraints,
particles, cloth, and other event paths are gated out. Full PhysX-only parity requires the joined image,
an exact next-step comparison, and feature-specific fixtures; this check is
not that proof.
