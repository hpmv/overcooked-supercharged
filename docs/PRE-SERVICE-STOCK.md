# Optional bounded stock before service

`--pre-service-stock` sets `CarnivalPlannerOptions.PreServiceStock`, default false. It allows at most one additional critical pantry supply job before a ready FIFO meal departs. It changes dispatch priority, retaining the existing native supply, throwing, handoff and chopping actions and their completion proofs.

The V7 trace measured completed pantry sausage supplies at 32–88 frames and bun supplies with chopping at 172–178 frames. Its first ready meal was observed at frame 1108 and delivered at 1547, a 439-frame interval. These are observations from one candidate, not guarantees for a changed route. The stock policy budgets at most 120 frames for one sausage job or 240 for one bun job; the total delay window is 240 frames from the first available ready-head observation. Time spent finishing an existing pantry job after that observation counts against the same window. There is no wait for a blocked destination and no second optional job in that pantry visit.

Admission considers only missing stock for the earliest two future hotdogs still lacking their bun-and-sausage bases within the configured preview window, skipping doughnuts and already assigned/assembled bases. Existing stock reserved for another unplated-hotdog consumer is not counted twice. An actual empty, free sausage pot is preferred; a needed bun can use its actual empty free board when the pot's existing supply contract cannot start. There is no onion batching or speculative third stock job.

The ready meal and its source stay reserved while the optional job runs. Its exact native plate identity, FIFO order ID, recipe, and timing are checked each frame. Admission subtracts the job's maximum time, a ten-second service allowance and a one-second margin from the observed remaining order time, and requires the same native tip tier afterward. The tier uses the installed strict `> .66`, `> .33`, `> 0` boundaries. The round must also have time for the job, twelve seconds of possible subsequent cooking, service allowance and margin. A native timeout, missing arrival proof, changed head, or endangered protected order window fails the candidate rather than claiming that stocking or service completed. The service allowance is a scheduling budget; dynamic navigation can still fail.

## Actual first-wave constraint

`artifacts/v7-unplated-lease-gf1108.json` shows ready plate 10 on counter 45 at `(15.6,-13.2)`. Counter 45 is also the addressed far-pot handoff and the mandatory reserved fallback for conditional far-pot throws. Chopped onion 135 simultaneously occupies board 56 in that throwing lane. Therefore the empty far pot cannot be refilled in that exact state without moving other items. The policy preserves both native guards and can choose only the available bun job. If a previous bun job has already consumed part of the ready-head window, the blocked far refill is refused and normal service proceeds.

No ready-plate relocation route is implemented. The policy can help when the near pot is empty, or when a ready head is already staged elsewhere and the existing far-pot supply contract is actually available. It does not solve the first-wave far-pot obstruction by itself.

## Offline validation

`CarnivalPlanner.PreServiceStockSelfTest` has 38 assertions. It uses the unchanged V7 ready-state fixture to establish the occupied-handoff rejection and critical-bun selection, then explicit synthetic configurations to test an empty near pot and a separately staged ready meal. The latter are ownership/admission tests, not claims that those modified states or refill routes were executed. Tests also cover prior-job tails, no second admission, tip boundaries, round limits, missing telemetry, head identity/ownership loss, successful native-equivalent stock evidence, and timeout rejection.

```powershell
dotnet build controller/OvercookedTAS.Controller.csproj -c Release --artifacts-path artifacts/pre-service-check
dotnet run --project artifacts/pre-service-check/harness/PreServiceCheck.csproj
dotnet artifacts/pre-service-check/bin/OvercookedTAS.Controller/release/OvercookedTAS.Controller.dll selftest
```

Results: `artifacts/pre-service-check/fixture-results.txt` and `core-results.txt`. Build: no warnings/errors. All 38 new, 36 staged-release, 47 parallel-onion, 45 early-onion, 29 pantry-chopping, 71 planner and 15 retired-plate checks pass, along with the controller selftest. A native trial must measure whether admitted refills improve score enough to justify delayed service.
