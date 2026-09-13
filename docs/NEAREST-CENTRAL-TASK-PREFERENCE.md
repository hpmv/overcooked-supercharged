# Optional nearest central task preference

`--nearest-central-task` enables `CarnivalPlannerOptions.NearestCentralTaskPreference`; the default is false. This option is limited to existing fire-button jobs, addressed raw-ingredient relays, and prepared-bun buffer jobs. It runs after each original candidate's native-food and resource checks and before that candidate creates a Work or acquires resources.

Both central chefs must be idle, empty handed, directly controlled, resting, on the center platform, and outside heat, persistent vessel, sauce, traffic, and cannon interruption duties. An unowned native heat obligation prevents preference. The helper must have a strictly shorter measured walking-time estimate to the original first station using full-clearance navigation with all actual chef obstacles; transfer jobs also require a clear continuation from that approach to the original destination. Failed geometry, absent/invalid speed, and equal costs preserve the ordinary decision. Equal costs never defer either chef, retaining player0-first behavior.

The farther chef may fall through its unchanged ordinary priority list. The nearer chef then reaches the original candidate through its own normal dispatch. No existing Work, action, input edge, callback, food address, or resource ownership transfers. Same-frame hints give earlier task preferences first claim on a proposed helper, so a lower candidate cannot overwrite an earlier cannon/helper hint. Conservative checks decline lower preferences when visible earlier cannon, plate, preparation, or addressed supply work is present. These hints are not resource leases or promises to interrupt higher-priority work. If the other chef actually accepts another job, its busy state prevents repeated deferral of the still-unowned original candidate on the next frame.

The full-clearance native geometry witnesses are documented in `docs/CENTRAL-DISPATCH-DISTANCE-REVIEW.md`. The new offline regression reconstructs the complete ordinary player0-then-player3 priority calls at V16 frame494: the right Flour141 handoff on48 goes to nearby chef3 while preserving bowl3, the existing address, action queue, callback, and resources. At the subsequent495 observation, chef0 can start the prepared-bun buffer while that exact relay remains owned. This is a counterfactual controller assignment test using captured native observations; it is not a simulated native path or a measured performance result.

Other captured checks choose chef3 for the exact right cannon/button at6936 and the existing bun311 buffer at7784. Negative fixtures cover busy, held, moving, disabled, suppressed, off-platform, heat-blocked, and persistent-lease participants; identity/resource loss; blocked paths; slower or equal costs; higher-priority work; and later fallback after a proposed helper becomes busy. `nearestCentralTaskDeferred` logs the exact station ordinal/resources, both measured paths, and estimated walking times. It records only a preference; subsequent normal `plannerJobStart` and native action evidence establish whether the job was actually accepted and completed.

Run the isolated checks with:

```powershell
dotnet build controller/OvercookedTAS.Controller.csproj -c Release -o artifacts/nearest-central-check
dotnet run --project scripts/NearestCentralCheck/NearestCentralCheck.csproj -c Release
```

The initial suite passes38 new assertions plus56 direct-clean-pass and71 prior planner checks. The bound report is `artifacts/nearest-central-tests.json`. A native candidate run is still required to assess timing, interaction traffic, and score. Existing V17 binaries and game processes remain unchanged.

Separately, the requested `ContinuousWaypoints=false` option and `--continuous-waypoints` CLI forwarding are wired into planner initialization through `runner.ContinuousWaypoints`. The actual waypoint algorithm, native probe audit, and approval to enable it are owned by the parent task; this preference does not enable that option.
