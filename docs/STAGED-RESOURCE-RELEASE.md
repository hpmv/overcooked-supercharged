# Optional staged unplated-hotdog reservations

`--staged-release` sets `CarnivalPlannerOptions.StagedResourceRelease`. It defaults to false. It changes when the controller releases a cleared source board and emptied sausage pot during `unplated-hotdog-*` jobs; it does not change the job's input actions, native food, timers or final recipe validation.

The actual V7 job `unplated-hotdog-2` started at gameplay frame 958 with resources `[23,153,2,33]`: bun board, exact bun entity, pot, and final counter. Board 23 became empty while chef 0 held bun 153 at frame 1038. Pot 2 became empty with zero native cooking progress while the same held bun contained a complete cooked-sausage base at frame 1071. The original job completed only at frame 1127. These native observations are preserved unchanged in `artifacts/v7-unplated-lease-gf958.json`, `-gf1038.json`, `-gf1071.json`, and `-gf1127.json`.

The controller may release a source only after its corresponding action has completed successfully, the native state confirms the transfer, and no active or queued action still references that station. Source-board evidence requires its recorded identity, explicitly empty native attachment, and the exact original food held by the assigned chef. Pot evidence additionally requires the recorded pot identity, explicit empty contents and zero progress, attachment to its original native stove, no chef holding it, and a complete native plain hotdog in the original held food entity. Missing evidence retains ownership. A buffered bun's source that is also its final destination stays reserved because the final place action still needs it. Pans and unrelated job resources are not released by this option.

Each work item now distinguishes its historical `Resources` declaration from mutable `OwnedResources`. A staged release removes the resource from that work item's ownership before returning it to the free pool. The eventual `CompleteWork` removes only ownership that still belongs to that work item. A refill can therefore reserve the pot or board immediately without the old job later removing that newer reservation. Existing shared sauce/onion leases retain their independent ownership model.

`plannerResourceReleased` trace events record the original job/player, actual native frame and resource, proof kind, held food, and remaining owned resources. They are controller scheduling observations, not native scoring events. Native completion may precede route-action completion, so the captured physical transition frame is an earliest opportunity, not a claim that the enabled planner releases on that exact frame.

The isolated test method `CarnivalPlanner.StagedReleaseSelfTest` has 36 assertions. It reconstructs the original hotdog job from frame 958 using the recorded first job's counter-32 reservation as its selection constraint, follows the actual native transfer snapshots, and tests synthetic overlapping refill jobs. It confirms that the old completion preserves both newer reservations, that repeated release is harmless, and that missing/changed food, identities, progress, attachments or future station use prevent early release. These are offline ownership tests, not evidence that the synthetic refill jobs ran natively.

```powershell
dotnet build controller/OvercookedTAS.Controller.csproj -c Release --artifacts-path artifacts/staged-release-check
dotnet run --project artifacts/staged-release-check/harness/StagedReleaseCheck.csproj
dotnet artifacts/staged-release-check/bin/OvercookedTAS.Controller/release/OvercookedTAS.Controller.dll selftest
```

Results are in `artifacts/staged-release-check/fixture-results.txt` and `core-results.txt`. The build has no warnings or errors; 36 new checks, 47 parallel-onion, 45 early-onion, 29 pantry-chopping, 71 planner, 15 retired-plate checks and the ordinary controller selftest passed.

Earlier availability does not guarantee greater score. Native scheduling must measure the tradeoff between pantry refills and departing with a ready meal. A full native candidate and replay evidence are still required.
