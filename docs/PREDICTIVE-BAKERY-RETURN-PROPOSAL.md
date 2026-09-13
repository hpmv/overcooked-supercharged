# Predictive bakery return: recorded failure and bounded proposal

This document preserves the initial source-policy proposal. The subsequently approved optional implementation and tests are documented in [Predictive one-kit bakery return](PREDICTIVE-BAKERY-RETURN.md). The V16 native recording demonstrates the idle interval and missing batch; the geometry report estimates a possible earlier excursion. No game state, recording or frozen controller was changed for this review, and the new policy still requires native validation.

## Recorded cause

The next Chocolate donut, zero-based order index24, received its first Flour only at GF15845, Egg at15926 and prepared Chocolate at16119. At the native round end its bowl still lacked enough mixing and frying time. P1 did not begin the FIFO bakery return until15625 and reached the upper-right pantry at15763. The current head became index24 at15201.

The ordinary lower-left return requires two `AvailablePlates`, zero dirty work and a delivery-age condition. `AvailablePlates` counts free, empty, unheld center plates. It excludes plates already held in an assembly and finished unserved meals. `ActiveBakeryCommitments` intentionally protects actual partial batches or issued raw addresses; it does not protect an empty bowl assignment. Thus neither the ordinary return nor the young-partial-batch prevention addressed this missing *empty* batch.

The native recipes establish index24 enters the ordinary eight-meal window at delivered17, GF11445. Assignment itself is inferred from `AssignBowls` and the recipe window: V16 did not emit a bowl-assignment event at that moment. This review does not label an inferred assignment a native event.

## Strongest earlier boundary

At GF12373, P1 had just completed `return-clean-plate` and was empty-handed and controlled at `(14.5204744,-20.4008255)`. The sink75, dryer76, dirty pass46 and remote dirty return74 were all empty. P1 had no further work until12942:569 frames,9.483 seconds.

Four distinct live plates were in the usable pipeline:

| Exact plate / observation ordinal | Actual location | Native food / known job |
| --- | --- | --- |
|395 /394|Held by P2|Completed Mustard, FIFO index17|
|408 /407|Held by P0|Empty plate allocated to active assembly index18|
|375 /374|Service counter49|Completed Chocolate, reserved index19|
|412 /411|Clean pass44|Empty clean plate|

Each had an active physical collider. Only412 was returned by `AvailablePlates`. Bowls3 and6 were empty at their original mixers14 and18. Basket8 was empty at its original fryer20; the other basket5 held cooked Raspberry off heat. The pantry chop board24 and raw pass48 were empty.

Do not count every observable plate: at GF11445 the just-served plate379 remained in telemetry with zero enabled physical colliders, no holder and no station parent. It is a delivery-animation residue. The only usable plate there was375, and dirty return74 already had work.

## Native geometry and time budget

`scripts/BakeryReturnReview` references the immutable V18 controller and has no game connection. It uses each unmodified native fixture, measured circle/box collision geometry, current other-chef obstacles and native run speed6. It resolves the native portal and pantry stations. The upper-right start `(28.8,-11.2999992)` was observed on an ordinary completed portal action at GF7771 in this same round; it is not the elevated animation teleport point.

For GF12373:

- Lower-left portal approach:4.611688 units.
- Sequential Flour→throw staging, Egg→throw staging, Chocolate→board→throw staging:15.780103 units.
- Walking plus2 seconds for portal/landing,2 seconds per ingredient job for input/transfer settling and1.4 seconds native chopping:12.798632 seconds.
- Adding the full native12-second mix and10-second fry durations gives34.798632 seconds, leaving28.974272 of the native63.772903 seconds for central transfers, plating and service.

The estimate is deliberately longer than the observed9.483-second idle interval. It does not imply the whole excursion is free, that future traffic is unchanged, or that the remaining central work is guaranteed. The current controller already supports the exact three-ingredient route: its late V16 jobs used native guarded near-bowl throws and completed without synthetic food changes. Reusing that route earlier still needs a fresh native scheduling trial.

The same estimate exceeds remaining time at GF14401 and15625, so those are useful late-admission negatives. GF11069 has three physical plates but index24 is outside the normal recipe window; GF11445 has remote washing work and insufficient usable plates. The report includes all five measured path sets and fixture/DLL hashes: `artifacts/v16-predictive-bakery-path-review.json`.

## Proposed first scope, pending approval

Add a default-off predictive return that runs only at an idle, empty, controlled P1 boundary in the lower-left room. Keep the existing immediate-washing and partial-mixer escape priorities. Require no sink, dryer, dirty-pass, carried or remote dirty work. Require at least three distinct exact live plate tokens, including at least one clean plate and an actual ready/allocated FIFO pipeline; count only validated native unserved plates or exact ordinary assembly ownership. Retired/disappearing/reused identities and delivery-animation residues do not qualify.

Choose one exact empty bowl at its original mixer, assigned to the earliest missing donut in the unchanged ordinary recipe window. For the initial bounded implementation, require the proven near-bowl direct raw and prepared-flavor routes, empty shared chopping space, no issued address or conflicting vessel/work lease, and enough remaining native round time for the complete walking/portal/kit plus native mixing/frying budget and explicitly measured transfer/service allowance. Do not manufacture a future free chef or plate in that budget.

Bind the excursion to that recipe index, bowl and mixer identity. Complete one ingredient kit before another washing departure, including the interval while the bowl is still empty. Merely invoking the portal is insufficient: newly returned dirty plates could otherwise send P1 back to washing immediately upon arrival. Native full required ingredient acceptance ends the visit commitment; it does not pretend the dough is Mixed or reserve a new plate. Active ordinary supply jobs retain their own source and vessel leases throughout. Existing native21-second mixer and19-second fryer guards remain in force.

This proposal changes neither the normal recipe window nor plate allocation. Earlier ordinary dough can reach the fryer well before its order is served; existing mandatory off-heat rescue then competes for storage. A first native trial must check this counter pressure and whether the excursion reduces starvation without creating a washing backlog. It is not a5000-score claim.

## Evidence and reproducibility

- Closed trace: `artifacts/native-round-v16/trial001.jsonl.gz`, SHA256 `d3802824e17cf667c954b822c5479c3d36ac30ed45b5699caa601c683be1f5e2`.
- Frozen path implementation: `artifacts/planner-candidate-v18/OvercookedTAS.Controller.dll`, SHA256 `6f8a87d592dee3626eca01e8f0b76432f2c1841fd5c78fb0f33129d586ed9258`.
- Unmodified responses: `artifacts/v16-supply-snapshots/gf11069.json`, `gf11445.json`, `gf12373.json`, `gf14401.json`, `gf15625.json`; extraction provenance in `artifacts/v16-native-pot-gap-screen.json.snapshots`.
- Compact native/event projection: `artifacts/v16-late-bakery-evidence.jsonl`.
- Run the file-only calculation with `dotnet run --project scripts/BakeryReturnReview/BakeryReturnReview.csproj --configuration Release`; its output is the geometry report, not a native game result.
