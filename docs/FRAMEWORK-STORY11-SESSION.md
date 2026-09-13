# Main 1-1 in the existing native process

`LevelSession-r2` loads the base-game fish/shrimp level through native frontend/session/loading methods, retaining four independent local players and the existing isolated save profile. It is an external DLL against frozen X; no core replacement or process restart is required. The [actual load receipt](../artifacts/framework-migration/story11-discovery-a/load.json), SHA256 `889D1C8122B996B386A82EB0713792B77371F525C3A6C6FAF7CF74E1BC60487A`, confirms `s_sushi_1_1`, `Sushi_1_1S_4P`, four local chefs, native150-second duration, elapsed0.0333333351, suppression false and score0 in the existing game process38824 (operator process identity). This is load/timer-start evidence, not rewind parity.

The selector requires the **live** base cooperative prefab (`DLC=-1`, also used by the installed native frontend), then scans its actual directory for `Text.Menu.Level01`, world `One`, theme `Sushi`, an allowed nonhidden exact four-player variant and scene `s_sushi_1_1`. It uses the observed ordinal; the extracted directory corroborates index1. Missing or ambiguous mappings reject. The native config instance must be `Sushi_1_1S_4P`,150 seconds.

The user explicitly requested **an immediately running timer**. This is an instrumented rule variant: the module changes only that native config's `m_recipesBeforeTimerStarts` from1 to0 in memory before loading, then verifies/reapplies it before both native campaign `Begin` calls. Native150-second duration, recipe data and other rules are retained. Receipts carry `immediateStory11Timer=true`, original/before/after values and exact config instance ID. No serialized asset/save file is edited for this override. Disposal restores the owned in-memory prerequisite if still0 and removes its hooks. Native restarts while the module is installed invoke the same scoped hooks.

Build a unique revision:

```powershell
./scripts/Build-FrameworkLevelSessionModule.ps1 -Revision my_revision
```

Load [the r2 manifest](../framework-run/modules/LevelSession-r2/manifest.json) into slot `level-session` with entry `SuperchargedPatch.Authoring.Modules.LevelSessionModule`. There is no `activate` operation. On the existing **persistent** bridge connection, issue:

```json
{"version":1,"command":"hot-call","slot":"level-session","operation":"load-main-1-1","args":{"seed":0}}
```

Begin from a ready kitchen paused/fenced by the bridge. Clear the controller's previous action graph before warmup. Poll ordinary bridge `status` on the same connection until `loadComplete=true` or `lastError` is nonempty. Progress appears in `bridge.session.stage`; detailed append-only transitions are in `framework-run/artifacts/level-session-transitions.jsonl`. The fixed total load bound is150 wall-clock seconds. X's original finalizer refreshes native metadata and pauses the loaded kitchen; use the existing `arm` handshake afterward. `hot-call level-session/status` with empty args returns detailed final state when the ready authoring boundary is available.

For a scoped restart, invoke `load-main-1-1` again. **The frozen bridge's ordinary `load`/`restart` commands still select Carnival**, so they are not Story11 restart commands. The module neither substitutes a controller layout nor qualifies rewind on this new scene; the headless host must reconstruct its actual native registry. Initial story suppression is intentionally absent under the recorded user-authorized variant. Delivery fades and deletion of initial fixed plates still require their own rewind qualification.

Offline `scripts/FrameworkLevelSessionCheck` executes the production pure selector and checks installed native/frozen X/module signatures and IL. These checks do not establish native frontend progress, timer behavior or replay parity.
