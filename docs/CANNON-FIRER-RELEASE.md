# Optional firing-chef release

`--release-firer` enables `ReleaseFiringChefOnLaunch`; the default is false. Ordinary `fire-cannon` still completes only after native passenger arrival. Its explicit `completeOnLaunch:true` action variant instead requires its own verified button edge, the same observed cannon/passenger/held-item identities, native `Launched` plus `cannonFlying`, and an unchanged target. Completion emits neutral input and a retained `transportLaunchConfirmed` receipt. A standalone caller must still observe passenger arrival.

The bot owns a separate `CarnivalCannonFlight` obligation before firing. The persistent lease reserves the cannon, exact passenger, held logical item, and destination landing area. The fire work owns only the button. On launch completion, the button and firing chef become available while those flight reservations remain. An interrupted assembly resumes its exact original Work, queue, and resources at this boundary.

The passenger remains neutral and ineligible for outer-area work until two different advancing native frames show flight finished, direct controls restored, the same held item, the expected platform, height at most0.9, and a position within1 unit of the observed native target. A120-frame arrival limit terminates unexpected travel. This also covers the measured one-frame native control restoration during launch. No controls, physics, positions, or native flight duration are changed.

`CannonFlightSelfTest` passes42 focused assertions using the captured V7 loaded-cannon state and explicit synthetic transitions. Existing transport35, interruption34, transit7, and planner71 assertions also pass. Results are in `artifacts/cannon-release-tests.json`; these checks make no game calls.

The bounded native mechanism plan is `routes/probes/cannon-fire-release.json`. It stages an original clean plate, boards the left cannon, confirms native launch, then legally moves the firing chef while the exact passenger remains neutral. The plan's named landing reservation is for the mechanism probe; a later explicit bot trial must exercise the production arrival obligation. Completion of this probe alone is not a high-score or replay qualification.

```powershell
dotnet artifacts/planner-candidate-v13/OvercookedTAS.Controller.dll plan --file routes/probes/cannon-fire-release.json --restart --seed 0 --isolate-recipe-random --port 17634 --out artifacts/cannon-fire-release-a.jsonl.gz
```

Enable the bot flag only for a subsequent native experiment after the mechanism probe passes. The separate native event log distinguishes `cannonFiringChefReleased` from `cannonPassengerArrivalConfirmed`; it must not treat launch as passenger arrival.

The V13 mechanism probe passed. `artifacts/cannon-fire-release-a-proof.json` binds the trace, route, result and checker hashes. Native launch occurred at gameplay frame195; the firing work closed at196, and the exact plate-carrying passenger satisfied arrival observations at246 and247. The firing chef legally moved during flight, releasing its work51 frames before confirmed arrival. Final frame289 had all four native controls restored. This is mechanism evidence; production flight ownership is evaluated separately in the optional bot trial.
