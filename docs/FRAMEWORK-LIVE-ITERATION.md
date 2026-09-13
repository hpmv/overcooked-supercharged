# Live iteration in the installed game

The permanent X plugin provides a paused main-thread module loader. It is installed in the isolated, windowed runtime. The game can stay open while controller DLLs or authoring helper DLLs change. No full state reconnect is implemented: a replacement controller receives a normal level restart after its connection is confirmed.

## Current commands

Compile a new body-restore revision against the exact permanent core, then load it:

```powershell
./scripts/Build-FrameworkBodyModule.ps1 -CoreBuild artifacts/framework-plugin-native-x -Revision my-next-revision
python scripts/framework_hot_module.py load --manifest framework-run/modules/BodyRestore-my-next-revision/manifest.json --activate --out artifacts/my-next-revision-loaded.json
```

Revision names and evidence paths must be new. A load verifies the helper SHA256, its core SHA256 and API contract. It retires the previous module in that slot. Activation is explicit; the CLI's `--activate` performs it after loading. Changes execute with native gameplay paused and inputs fenced. They do not silently restore the world, advance the round, or make an incompatible checkpoint valid.

Inspect the active body helper or invoke the independent inspection module:

```powershell
python scripts/framework_hot_module.py call --operation status --out artifacts/body-status.json
python scripts/framework_hot_module.py call --slot inspection --operation inspect --args '{"entityId":103}' --out artifacts/chef-components.json
```

The inspection module supports selected private/public fields and properties, typed setters, native instance methods without arguments, and batches. Mutations require the observed native object and component instance IDs. See [exact selectors and examples](FRAMEWORK-INSPECTION-MODULE.md).

Replace the headless controller while preserving the game:

```powershell
dotnet build framework/headless/Headless.csproj -c Release --no-restore -p:RestorePackagesPath=M:/projects/game-test-2/framework/.packages -o artifacts/framework-headless-host-v12
./scripts/Restart-FrameworkHost.ps1 -PreviousRun native-x-v11b -Run native-x-v12 -ControllerDll artifacts/framework-headless-host-v12/Headless.dll -ProbePausedGap
```

Use the actual current run receipt for `PreviousRun`. The script verifies PID, process start time and command line before stopping the old controller. It preserves the game process, waits for the replacement controller's first response, and then restarts the native level. `ProbePausedGap` measures bridge responsiveness during disconnection. Run names, DLL output directories and evidence files should never overwrite a previously validated revision. The final `host-replacement-complete.json` records the observed fresh paused baseline separately from the initial dispatch receipt.

For independent experiments with the same controller, restart and clear its retained action graph explicitly:

```powershell
python scripts/framework_rpc.py --script routes/probes/framework-restart-clear.json --out artifacts/my-restart-clear.json
```

A native level restart preserves the controller's authored graph. Without the explicit clear, a completed previous graph can execute again during the next warmup. Probe checkpoint/export filenames now include an output-directory hash, so repeated frame31 checkpoints coexist in one host evidence directory while retaining create-new semantics.

## Current measured status

The game remains in the same process38824. Current controller evidence is [native-x-v11b/host-process.json](../artifacts/framework-migration/native-x-v11b/host-process.json). That replacement preserved the game's PID/start time, received a fresh paused frame1 and completed normal load/handshake in8.938s. While disconnected, all20 bridge requests succeeded with47ms median/63ms maximum response latency and no gameplay-time change. These are bridge measurements, not operating-system input latency.

External helper changes are also demonstrated in this process: body r1 activation took0.094s, body r2 replacement/activation0.187s, and phase r1c replacement/activation0.203s. Compilation is separate. A native inspection/setter batch changed proxy121 drag2→3→2 with exact readbacks and unchanged gameplay time. Earlier receipts remain under `native-x` and `native-x-v11`; current experiments are under `native-x-v11b`.

| Experiment | Proven result | Remaining limit / evidence |
| --- | --- | --- |
| Fresh-round plate search | Four native candidates; selected56 frames versus73 walking; two exact-input repetitions after normal level restarts reached the goal. | Measured17-frame preparation improvement, not a full-round score gain or checkpoint parity. [Audit](../artifacts/framework-migration/native-x-v11/plate-search-achievement-review/README.md). |
| Body R4 + phase r1c + world-sync r1b | **201 strict advancing replay frames:** idle3×60, movement10, actual pickup11. Physics, chefs, phase, inputs, native/auxiliary bytes and message order match. | Applies only to this exact combination and these intervals, not later modules. [Hash-bound proof](../artifacts/framework-migration/native-x-v11b/offline-exact-frames-synced-r4/README.md). |
| Body R5 Egg probe | Native Egg and proxy deletion succeeds and restores the exact observed fixed baseline. | Repeating the fixed inputs to respawn differs after attachment; saved dynamic-target recreation was not exercised. [Result](../artifacts/framework-migration/native-x-v11b/spawn-synced-r5/summary.json). |
| World-sync r2 plate search | All56 replay inputs, phases, native/auxiliary bytes and message order match; plate10's WorldObject packet occurs at frame56 on both sides. | Physical state first differs at74; selected replay still fails strict geometry/COM comparison. [Strict audit](../artifacts/framework-migration/native-x-v11b/plate-checkpoint-sync-r2-strict-review-b/summary.json). |
| World-sync r3 plate search | All four candidates complete; selected chef103 dash takes56 frames versus73 walking. Four sidecar restores verify with zero rejections; plate10 client parent changes32→38. | Selected exact-input repeat still fails on small geometry/COM differences for10,103,118. [Search](../artifacts/framework-migration/native-x-v11b/plate-checkpoint-sync-r3/summary.json), [actual cache receipt](../artifacts/framework-migration/native-x-v11b/world-sync-r3-after-search-status.json). |
| World-sync r3a, latest tested revision | All four candidates complete with the same56-frame winner; four verified sidecar restores, zero rejections. All56 input, phase and native/auxiliary message frames match; the WorldObject packet is at61 in both branches. | First physical difference remains74, with14 of56 frames differing and the same endpoint COM discrepancy. Compatibility correction, not a parity improvement. [Strict audit](../artifacts/framework-migration/native-x-v11b/plate-checkpoint-sync-r3a-strict-review/summary.json). |

The r3 cache receipt is pinned by SHA256 `c2d23c065539ec85093475cfee54cca22e771a9f54e807528a0bb557ea6625c7`. It proves restoration of the missing client bookkeeping; it does not prove that bookkeeping caused or removed the later physical drift. The [r3a admission correction](FRAMEWORK-CLIENT-WORLD-CACHE.md) preserves a legitimate native startup null-parent cache and passes82 offline checks. Its native load/activation took0.203s in the same process, followed by a normal restart-clear in about6.44s. The latest [cache receipt](../artifacts/framework-migration/native-x-v11b/world-sync-r3a-after-search-status.json), SHA256 `dfb7955b57e620bcbd04735ea80dcd6e977665b01944904b208794582ea881a4`, records the last plate10 client parent106→38 restoration. [Paused frame87/module receipt](../artifacts/framework-migration/native-x-v11b/live-iteration-milestone-receipt.json) and [screenshot](../framework-run/artifacts/live-iteration-milestone-r3a.png) preserve the current state.

The plugin logs module loads/calls/retirements, hashes, process IDs and Unity frames in `framework-run/artifacts/authoring-modules.jsonl`. Older assemblies remain loaded but inactive. Replacing a helper neither unloads its assembly nor undoes prior mutations. Use a normal level restart after any partially failed restore. Preserve each failed comparison; no numerical tolerances or omitted messages qualify it as a pass.

Continue with a unique helper revision, paused load, bounded native probe and first-divergence audit. Full rewind parity, dynamic preparation and complete-round reproducibility remain unproved. Final score validation still requires a frozen configuration and five fresh-process runs without authoring restoration.

The next discriminating observation is native mass and attachment state at the last matching plate boundary, frame73: chef103 Rigidbody mass/COM/inertia, plate10 local/world pose and collider owner, and proxy118 pose. Compare both original and restored continuations before proposing another correction. Existing inspection can read these fields while paused; a diagnostic pause must be explicitly identical in both legs and distinguished from the original uninterrupted recording. The raw runner's two neutral release frames do not constitute an exact moving frame73 capture. A read-only helper observing the existing capture boundary would avoid introducing that pause.
