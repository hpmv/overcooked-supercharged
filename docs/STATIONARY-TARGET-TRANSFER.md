# Stationary exact-target transfer experiment

`stationaryTargetTransfer: true` is an optional route-action flag for the existing `take`, `place`, `combine`, `apply`, and `assemble` actions. It can replace part of the final navigation/face delay only while the chef is already stationary and the native interaction target is correct. Moving transfers remain unsupported.

The V23 source also provides `CarnivalPlannerOptions.StationaryTargetTransfers` / `--stationary-target-transfers`, disabled by default. It configures the planner's RouteRunner so `CreateAction` copies `stationaryTargetTransfer: true` onto those five action types only when the per-action field is absent. An explicit `false` opt-out remains false, and the original queued specification is not modified. Existing actions keep their creation-time setting. This wiring does not change any admission guard, input edge, material expectation, or completion barrier.

The first eligible observation emits one ordinary neutral input frame. The next contiguous 60 Hz observation must preserve the native target, station/source/held registration and observation identities, attachment topology, food material, pose, and forward direction. Native Rigidbody velocity and the movement velocity cached for the next FixedUpdate must both have horizontal magnitude at most 0.00001; impact, moving surfaces, wind, dash, throw, suppression, and control gates also exclude admission. A nearby chef contact envelope or any active projectile/cannon flight excludes the shortcut. This addresses the pending FixedUpdate risk without changing physics or native timing.

After that confirmation, the action emits the same ordinary pickup edge as the existing transfer implementation. Existing attachment, ingredient delta, retained-held-object, and plate-under-food recovery barriers still determine completion. Extra checks pin the native source incarnation, including a crate's newly registered expected ingredient and the original plate through under-placement/recovery. Native food progress may advance during confirmation; its ingredient multiset, preparation state, and cooking/mixing configuration must remain unchanged.

An ineligible initial observation continues through ordinary navigation. A changed confirmation permanently disables the shortcut for that action and resumes its normal path. A contradictory result after an issued edge fails the attempt through the existing route error handling. The code performs no native state, position, timer, or input-handler writes.

The trace records `stationaryTransferNeutralConfirmation`, `stationaryTransferRejected`, and `stationaryTransferEdge`, followed by the existing transfer events. A repeated read of the same frame cannot satisfy confirmation.

## Captured checks

`RouteRunner.StationaryTransferSelfTest` uses exact snapshots from the completed plated-sauce-before-onion B mechanism trace. The current suite has 69 focused checks, including stationary pickup/placement, cooked pot/pan transfer, plate-under-bun recovery, the actual moving-target negative frames, and identity/material/clock/motion/control mutations. `StationaryTransferDefaultsSelfTest` adds 48 checks of disabled defaults, explicit opt-outs, transfer-only propagation, source/action immutability, and captured inherited-option behavior. The existing waypoint suite adds 69 passing checks. `scripts/StationaryTransferCheck` runs these against an explicitly selected controller DLL and prints the loaded DLL's SHA256.

The captured completion states come from the original later input edges. They validate controller admission and result barriers; they are not evidence that earlier native edges have succeeded. No native performance claim follows from these offline tests.

## Native A/B plan

`routes/probes/stationary-transfer-baseline.json` and `routes/probes/stationary-transfer-shortcut.json` retain B's same five jobs, 21 actions, dependencies, resources, and 2,400-frame bound. Their only semantic differences are the 13 action flags in the shortcut plan. Both must run against the same frozen controller/native configuration, with their route, trace, result, stderr, and bundle hashes retained.

The existing `scripts/check_plated_sauce_before_onion.py` can check each completed trace's original plate/ingredient identities, native cooking clocks and order of mustard/onion assembly, original unmoved vessels, ordinary input sequence, complete result binding, and final neutral frame. A comparison should separately count admitted/rejected shortcuts and compare action/job timing and elapsed native time. Different early motions or interaction targets must be reported, not normalized away. The original B circuit took 1,165 gameplay frames. At source release the shortcut had no native result; the subsequent measured V22 comparison is documented in `STATIONARY-TRANSFER-AB.md`.

The stringent observation guards deliberately allow fallback, including when precision at a large native clock value prevents confirming one 60 Hz interval. They do not guarantee a saving for every flagged action. Root owns native execution and frozen candidate creation.
