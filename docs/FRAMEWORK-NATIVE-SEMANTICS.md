# Framework native input and recipe semantics

The migration starts from framework commit `49701883ff20755daddfb819d54710c91a6d5486`. These changes restore two native mechanisms. They do not establish that the entire framework preserves native gameplay, or qualify any TAS score.

## Input device and native gates

`framework/patch/TASLogicalButton.cs` now inherits the installed game's `LogicalButtonBase`. Only `IsDown` supplies the accepted device level; the native methods own press/release claims and held duration. Protocol `JustPressed` and `JustReleased` hints cannot create an edge. Native `PlayerControls.CanButtonBePressed` and both original `GetGated` overloads execute normally, including focus, direct-control, menu and dialog predicates.

`ApplyInputFrame(InputData)` copies finite pad values and button levels once per accepted logical frame. After the first explicit input dictionary, absent inputs or chef entries mean neutral. `TASLogicalValue` uses the same accepted snapshot, so applying neutral immediately also clears analog movement. A repeated application of unchanged levels cannot create a new native edge.

All calls involving this API belong on Unity's main thread. `ResetAll()` clears levels and outstanding native device/gate claims, invalidates older checkpoints, and retains emulation ownership. It does not revert to physical controls on disconnect. Native gates are observed after their original construction; their callbacks and objects are not replaced.

`CaptureCheckpoint()` and `RestoreCheckpoint(...)` are explicit, process-local authoring APIs. They preserve device and observed native gate histories, require the same live chef incarnations and exact gate objects, and reject a new reset generation or changed membership. Held-button timestamps are rebased against the current Unity clock so restoring history preserves the captured held duration without writing the clock. These APIs are not yet evidence of a complete world/input rewind, and invoking authoring restoration is separate from fresh-process validation.

## Native weighted recipe stream

`framework/patch/AlteredComponents/WarpableRoundData.cs` wraps exactly the installed native `RoundData` class. Other subclasses remain unwrapped. The original recipe asset, score entries and round duration remain in place. Every draw calls the original `RoundData.GetNextRecipe` on an original `RoundData.InitialiseRound()` instance. The wrapper does not reproduce the weighting formula or use `System.Random`.

`ConfigureSeedForNextRound(int)` sets the seed for a new instance; its default is zero. Each native round instance owns a separate `UnityEngine.Random.State`. Calls save the ambient Unity stream, install that instance's stream, execute the native draw, capture the resulting stream and restore the ambient stream in `finally`. Outgoing rounds cannot consume a new round's recipe draws.

An explicit `Warp` restores native `RecipeCount`, every cumulative frequency, the recipe cursor and isolated RNG together. Repeated draws execute the native method again and must match the saved result and state. The diagnostic `authoringWarpCount` remains visible after rewinding. `GetAuxMessage` runs its ten-recipe preview on a separate native instance with copied checkpoint arrays; it cannot advance live history or frequency state.

`GetDiagnostics` exposes the actual native method/module, selected seed, duration, cursor, count, frequencies and native recipe IDs/names/base values. Live fresh-start comparison of this sequence remains required; CPU tests cannot establish Unity's RNG equivalence.

## Validation completed

`python scripts/run_framework_native_semantics.py` passes **53 assertions**. It compiles the two implementation files with verbatim decompiled native button, gate and `RoundData` source, and verbatim extracted native weighted-selection/array-fold methods. Controlled Unity API doubles make the history and failure cases reproducible. Tests cover edge claims, duration, gates, neutral/disconnect, invalid input atomicity, checkpoint membership and time rebasing, preview isolation, native count/frequency rewind, exception restoration and independent interleaved round streams.

The harness manifest under `artifacts/framework-native-semantics/` pins the source files and extracted native methods. Its RNG, clock, focus and Unity-object APIs are test doubles. This is source-semantic evidence only, not a live input, RNG, physics or score proof. `scripts/Build-Framework.ps1` also compiles these changes against the actual installed game assemblies.

## Remaining native-rule audit findings

- Authoring `WarpHandler` writes entity/food states, transforms, progress, timers, orders and score. A fresh run must reject warp operations and record their absence.
- Optional invalid-state prevention suppresses native fire, respawn and round completion. It must remain disabled during native validation.
- Upstream cannon components replace the original client/server control flow, including synchronization and control-resume ordering. The replacement records an exit pose after moving the chef into the cannon. Its compatibility still needs explicit native comparison.
- Upstream delivered-plate instrumentation unregisters the plate immediately, changing the native delayed retirement and possible entity-ID reuse.
- Upstream synchronization patches suppress world-object updates and override loader readiness. They are gameplay execution changes, not solely observation hooks.

No normal-path replacement of cooking duration or score arithmetic was identified in this bounded audit. That finding does not clear the remaining component and lifecycle changes.
