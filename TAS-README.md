# Four-chef Overcooked 2 TAS

Current work uses overcooked-supercharged as the foundation for a four-local-chef Story 1-1 TAS. The active priority is exact authoring rewind parity before route search. The current milestone repeatedly rewinds across destruction and exact recreation of the first returned plate stack, then replays the same pickup with exact logical, native-physics, clock, contact-pool, Transform-dispatch, input, and Animator results. See the [fresh-session handoff](docs/FRESH-SESSION-HANDOFF.md) for pinned hashes and evidence.

The Git repository remains rooted at `framework/`, preserving the upstream Supercharged paths and history. Native x86 C++ helpers now live under `native/`; reproducible probes and offline fixtures under `scripts/`; durable investigation state under `docs/`; and the separate planner/plugin sources under `planner/` and `tas-plugin/`. Workspace-root compatibility junctions preserve the established command lines. Generated binaries, live runtime state, large evidence trees, and external PhysX source clones remain outside or ignored. Every demonstrated milestone receives a local commit containing its source, native build inputs, fixtures, and concise evidence hashes.

## Current development loop

The isolated, windowed game can remain open while external authoring modules and the .NET 10 controller change. The permanent Unity/.NET 3.5 plugin supplies a paused main-thread loader, native inspection, and strict restore checks. Controller replacement uses a normal level restart. See [live iteration commands and evidence](docs/FRAMEWORK-LIVE-ITERATION.md).

- Native runtime and assets: `lab/runtime`; isolated saves and module DLLs: `framework-run`.
- Framework and headless controller: `framework/`; external managed modules: `framework/modules`.
- Native C++ instrumentation/checkpoint sources and build inputs: `native/`.
- Current frozen core, controller, module hashes, process identities, and evidence paths: `docs/FRESH-SESSION-HANDOFF.md`.
- The original Steam installation and earlier artifacts remain preserved.

Build and hot-load a new body-restoration revision against the frozen core:

```powershell
./scripts/Build-FrameworkBodyModule.ps1 -CoreBuild artifacts/framework-plugin-native-x -Revision my-next-revision
python scripts/framework_hot_module.py load --manifest framework-run/modules/BodyRestore-my-next-revision/manifest.json --activate --out artifacts/my-next-revision-load.json
```

Use a new revision and evidence path. Modules have independent slots, explicit activation, DLL/core hash checks, and recorded native receipts. The [inspection module](docs/FRAMEWORK-INSPECTION-MODULE.md) supports selected fields/properties, typed setters, and bounded native method calls. A module replacement does not undo prior mutations; restart the level after a partially failed restore.

The following older restart example is **Carnival-specific**. For active Story 1-1 work, use the `level-session` module and commands in the handoff; the legacy bridge restart hardcodes Carnival. After a native fresh load, clear the retired action graph before an independent experiment:

```powershell
python scripts/framework_rpc.py --script routes/probes/framework-restart-clear.json --out artifacts/my-fresh-level.json
python scripts/framework_probe.py --out artifacts/my-idle-probe --warmup 30 --frames 60 --repeat 3 --render-fps 0
```

Keep one bridge owner during a native experiment. Closing it releases inputs and pauses. Read-only controller status uses HTTP JSON on 17637; the native bridge uses length-prefixed JSON on 17636; the framework transport uses binary Thrift on 14455. Checkpoints and reconstructed history use protobuf.

## Demonstrated progress

The automated plate-transfer search evaluated four candidates in the native game and selected a 56-frame route over a 73-frame baseline. Two additional level restarts replayed the selected exact four-chef inputs successfully. This saves 17 frames on the measured preparation task; it is not a full-round score gain. [Independent native audit and replay evidence](artifacts/framework-migration/native-x-v11/plate-search-achievement-review/README.md).

Live iteration has been demonstrated in the same game process using replacement controllers and external body, timing and synchronization helpers. **Body R4 + phase r1c + world-sync-cache r1b** passed three idle replays, movement and actual pickup: [201 strict advancing-frame comparisons](artifacts/framework-migration/native-x-v11b/offline-exact-frames-synced-r4/README.md). That result belongs to this exact combination and does not qualify later revisions.

Body R5 now [exactly restores the fixed spawn baseline after native Egg and proxy deletion](artifacts/framework-migration/native-x-v11b/spawn-synced-r5/summary.json); repeating the same inputs to respawn still differs after attachment. World-sync r2 made all56 plate replay input, message and phase frames exact, while small geometry differences remain. The r3 and latest r3a trials each completed all four candidates and verified four cache restores, but the selected replay still differs from frame74 in14 of56 physical frames. R3a is a tested compatibility correction, not a parity improvement. [Current measured limits and receipts](docs/FRAMEWORK-LIVE-ITERATION.md).

Dynamic target recreation, preparation, washing, throws, cannons and complete round reproducibility remain qualification work. None of the small state differences has been waived.

## Evidence and next gates

- [Resume-phase diagnosis and module](docs/FRAMEWORK-RESUME-PHASE-MODULE.md)
- [Native body restoration diagnosis](docs/FRAMEWORK-X-EMPTY-PROXY-RESET.md)
- [Search and exact-input replay](docs/FRAMEWORK-FRESH-PLATE-SEARCH.md)
- [Bounded ingredient-spawn probe](docs/FRAMEWORK-SPAWN-PROBE.md)
- [Headless controller commands](framework/headless/README.md)

Authoring rewind and inspection mutations are development tools. Final score validation must use a frozen configuration, native timing and scoring, and five fresh-process runs without authoring restoration or position correction. Report strict state parity, exact-input gameplay repetition, and adaptive replay separately.

The initial controller's documentation was archived unchanged in `docs/ARCHIVED-FIRST-CONTROLLER-README.md`; it is not the current route or qualification evidence.
