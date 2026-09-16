# Framework migration

The user resumed work and approved using their overcooked-supercharged framework as the foundation for the new automated bot and search engine. The native score goal remains five fresh starts at 5,000+ on Carnival of Chaos 3-4, four players, 270 seconds. Authoring rewind is allowed; final verification must use ordinary inputs from fresh starts without state restoration or position corrections.

Upstream: https://github.com/hpmv/overcooked-supercharged at `49701883ff20755daddfb819d54710c91a6d5486`. The checkout is in `framework/`; upstream Git history is retained. The original controller, plugin source, frozen candidates, and recorded evidence remain in place. The best completed legacy result is still V21, 2,720 points / 25 deliveries, authored adaptively and not yet exact-replayed.

The migration uses the existing isolated `lab/runtime` assets, a separate `framework-run/profile`, and only the framework plugin plus its dependencies. `Launch-Framework.ps1` preserves the lab's old plugin by hash before replacing it. The primary isolated runtime and original Steam installation remain intact.

All game launches are windowed at 1280×720 as requested. Native game access belongs to the root agent; supporting agents do not launch or advance it. Native-e verified that this remains true after kitchen loading: `Screen.fullScreen=false`, 1280×720, eight native display-option commits intercepted. The plugin prefixes the game's two managed display writers (`Windowed.Commit`, `Resolution.Commit`) and retains a late-frame guard. It changes only this isolated process's in-memory options; it does not save the user's graphics preferences.

Background TAS input is separate, explicit instrumentation. `BackgroundTasInputFocus` replaces only the OS focus getter inside native `PlayerControls.CanButtonBePressed`, allowing an unfocused locally controlled chef only when the TAS owns its pad snapshot. Native menu, dialog, direct-control and cannon suppression checks still execute. Set `OC2SC_BACKGROUND_INPUT=0` before launching to retain the original focus requirement. Native status reports both actual focus and the number of virtual unfocused checks. This addition compiles against the installed game; a native movement test while unfocused is still pending.

Initial milestone: build the framework on the installed assemblies, load the actual four-player kitchen, establish compact state reconstruction and controlled input stepping, then compare restored continuations against ordinary prefix playback. A successful compilation is not a native compatibility or rewind-correctness claim.

Known upstream differences under review: native input gates and event-claim semantics, replacement recipe RNG, native clock/update patches, replacement cannon components, and a hard-coded warp log location. These must be documented and tested before any migrated score can qualify.

Commands under development:

```powershell
.\scripts\Build-Framework.ps1 -Restore
.\scripts\Launch-Framework.ps1 -PlanOnly
.\scripts\Launch-Framework.ps1
.\scripts\Launch-FrameworkHost.ps1 -Run unique-run -ControllerDll artifacts/framework-headless-host-v3/Headless.dll
python scripts/framework_rpc.py --script routes/probes/framework-load.json --out artifacts/framework-migration/unique-run/load.json
python scripts/framework_probe.py --out artifacts/framework-migration/unique-run/idle-probe --repeat 2
```

Choose new evidence directories and a frozen controller build for each process. The game and controller must run in the user's desktop session; the controller window is hidden. The bridge uses length-prefixed JSON on loopback17636, the framework game connection uses its original binary Thrift on14455, and headless control uses HTTP JSON on17637. Entity history/checkpoints use the framework's protobuf representation.

Native integration results so far:

- Native-d loaded four registered local users into `s_Day_3_4`, DLC8, four-player variant, native270-second round. The controller reconstructed122 entities. Its first rewind failed: native time advanced from5.033344 to7.03337049 seconds instead of returning to the earlier elapsed time. The original definition omitted flow107 and misclassified sink75. Evidence is preserved under `artifacts/framework-migration/native-d`.
- Native-e validated all122 initial entity names/positions and refreshed native spawn collections. A120-frame idle segment took2.094 wall seconds. It remained windowed throughout. The new native checkpoint preflight rejected a wrong CookingStation annotation on mixer14 **before any mutation**. The probe is a failed rewind test, not a successful replay. The oldV3 host also did not complete its failed-warp request promptly; the source now recognizes the explicit failure. Native-e's exchange trace temporarily appeared empty during inspection, then became readable at28,522,935 bytes. The cause of that live file visibility discrepancy is under investigation. Full endpoint observations and the protobuf checkpoint are also preserved.
- No migrated full round or5,000-point result has been demonstrated. Rewind correctness, raw input replay, component lifecycle equivalence, search integration and final score validation remain in progress.
