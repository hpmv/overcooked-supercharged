# V11 serial sauce critical path

The measured 13.4833-second meal2 delay is serial plate transport, not a cooperative barrier. The cheapest supported next change is to choose and reserve a nearby temporary counter at serial-job admission, evaluating the whole dispenser → parking counter → switch → parking counter → dispenser route. No controller or gameplay code was changed for this audit.

## Actual job and native timeline

`artifacts/native-round-v11/trial001.jsonl.gz` is closed: SHA256 `2a21da1c5f245d734b1372022ed6b5cc3d22fe2573b4357083ff37e958bd6489`. Frozen V11 controller SHA256 is `7856cad051fbe0ce0f9fb60b612ec796f1e77452bc31b13b1ea11cc02ff2bc7b`.

P0 `assemble-meal-2-Hotdog_Ketchup_Mustard` starts GF1346 and completes GF2155. P3 already owns `transfer-mixed-dough` GF1242–1504, so the cooperative admission check fails. The trace contains zero cooperative sauce lease/barrier/completion events. Frozen source: `artifacts/planner-candidate-v11/source/controller/CarnivalPlanner.cs:663` tries cooperative admission, and `:712` requires the helper to be idle. It immediately falls back to the ordinary action list.

| Action | Observed GF interval | Action frames | Native X/Z travel |
|---|---:|---:|---:|
| Select already-active mustard | 1346–1346 | 0 | 0.00m |
| Take plate13 | 1347–1378 | 31 | 2.42m |
| Assemble unplated hotdog from counter37 | 1379–1428 | 49 | 3.48m |
| Carry to dispenser72; apply mustard | 1429–1585 | 156 | 13.80m |
| Park plate on counter37 | 1586–1705 | 119 | 10.24m |
| Walk empty-handed to switch79; select ketchup | 1706–1843 | 137 | 10.48m |
| Return to counter37; recover plate | 1844–1939 | 95 | 7.92m |
| Return to dispenser72; apply ketchup | 1940–2046 | 106 | 11.30m |
| Stage final plate on output49 | 2047–2154 | 107 | 9.62m |

The complete job has 809 frames: 745 in explicit `navigate` stages (12.4167s), 10 in initial navigation/contact settling before the switch approach, 45 in facing/interaction/recovery/verification stages, and 9 between action/job boundaries. Native planar displacement totals approximately 69.26m. The middle four actions alone, GF1586–2046, consume 460 elapsed frames / 7.6667s and approximately39.94m. These are recorded movement and frame counts, not simulated timings.

Plate13 gains the complete bun+Cooked sausage tree at GF1425, native mustard at GF1585, and native ketchup at GF2046. Cooking is already complete. The existing mustard-first order matches the initial native switch index0 and needs no first switch edge.

There are four P0 replans, at1456/1538/1641/1751. Two have direct dynamic-geometry evidence: P3's center lies only0.732m and0.449m from the previous planned path at1538/1751, inside the combined chef footprint/clearance. This supports actual route avoidance, not a prolonged stationary deadlock. The standard trace analyzer finds zero sustained P0 movement-stall spans; the only explicit navigation wait within this job is the10-frame post-placement settling interval at1706–1716. Not every replan is attributed to P3.

## Admission storage and bounded geometric comparison

Frozen source `CarnivalPlanner.cs:669` unconditionally uses `temp = unplatedSource`. For this meal the source is counter37 at(21.6,-21.6), while dispenser72 is at(20.4,-10.8). The counter choice forces the extra lower-kitchen visits.

Immediately before meal2 starts atGF1346:

| Counter | Position X/Z | Actual availability |
|---|---|---|
| 33 | 21.6,-10.8 | Empty, no Work or persistent lease |
| 40 | 21.6,-15.6 | Empty, no Work or persistent lease |
| 32 | 19.2,-10.8 | Physically empty, reserved by raw-sausage handoff |
| 37 | 21.6,-21.6 | Exact source food151; becomes empty through this job's native assembly |
| 42 | 20.4,-16.8 | Protected workspace; excluded |

Other ordinary counters hold clean plates or the second raw-sausage buffer. The exact unchanged response is `artifacts/v11-sauce-admission-gf1346.json`; `artifacts/v11-sauce-admission-ownership.json` reconstructs active Work and persistent leases before the meal2 start. No onion, bakery, or fryer lease exists yet at that frame. The future bakery lease chooses33 atGF1504; reserving33 earlier would correctly make it unavailable to that later admission, rather than allowing overlap.

`scripts/SaucePathCheck` uses the frozen V11 navigation assembly and actual geometry to compare the four middle legs. It starts at the recorded post-mustard chef position GF1585, holds the GF1346 admission colliders/other chef positions fixed, and repeatedly chooses a reachable station approach:

| Temporary counter | Same static four-leg planner | Reachable |
|---|---:|---|
| Existing source37 | 34.877m | Yes |
| Empty counter33 | 7.089m | Yes |
| Empty counter40 | 6.638m | Yes |

These are feasible static planned paths, **not mathematical shortest-path lower bounds, native elapsed-time predictions, or proven savings**. Counter40 is slightly better than33 for the complete loop despite33 being nearest the dispenser. Actual chef movement and a newly reserved counter will change later scheduling; a native trial must measure the result.

The bounded proposed policy selects only an actually empty, unreserved ordinary center counter (or the existing exact source as fallback), excludes the protected workspace/output/other leases, compares all four relevant native approach legs, and reserves the chosen counter with the ordinary job before any action starts. Keep the source lease for assembly and all existing plate/food/dispenser/switch locks; later generic completion releases only that Work's current ownership. Preserve exact held-plate/native sauce checks and live navigation. No active Work needs to be abandoned or paused.

Root's closed serial-setting comparison, `artifacts/native-prefix-v11-serial-sauces-analysis.json`, confirms exactly the same GF1346–2155 meal2 job, first delivery1720, second2248, total score156. Disabling cooperative sauces alone does not improve this serial fallback.

## Why pantry idle time is not an available center helper

Over the45.2-second V11 prefix, P1 has27.3667 seconds without a job and P2 has20.2333; P0/P3 are assigned38.9167/37.6333 seconds. These parallel totals do not add to round duration. P1's longest idle interval is GF726–1504 on the upper-right ingredient platform while prepared dough waits for central handling. P2 is aboard/waiting for the first cannon arrival during much of GF1399–1654, then waits on the lower-right service platform GF1721–2155 for this second plate. These roles cannot directly reach the center sauce switch; their idle totals do not establish that a cooperative center helper was available. Source role dispatch is in frozen `CarnivalPlanner.cs:448` and`:512`.

## Cannon wait ownership

Frozen `RouteCannon.cs:573–601` observes exact native launch, then retains the firing central chef until native flight ends and the passenger has two stable destination/control observations. It correctly rejects the native one-frame control-enable transient during launch.

| Trace | Completed fire actions | Arrival-wait frames | Central chef-seconds occupied |
|---|---:|---:|---:|
| V11 throughGF2712 | 2 | 104 (52 each) | 1.7333 |
| V9 throughGF9038 | 11 | 572 (52 each) | 9.5333 |

There is also one launch-confirmation frame per fire. V11's first fire sends the edge at1602, observes `Launched` at1603, briefly sees passenger controls enabled at1604 while still flying, observes flight ended with control at1654, and completes at1655. During the arrival stage, the firing chef's output is neutral. A later split launch/arrival observer could free that chef after confirmed launch while retaining passenger identity/item/cannon and landing validation independently; these counts show the ceiling of occupied chef time, not an equal guaranteed round-time saving.

Detailed outputs: `artifacts/v11-sauce-path.json` (810 native samples, five exact full-JSON projection checks), `artifacts/v11-sauce-nearby-paths.json` (paths and input/binary hashes), and `artifacts/v9-cannon-action-waits.json`. V9 source SHA256 is `94cbe0783192bed237e43316c357de705dbcbbed1d2d4f26d3c9dc481da3e579`. All analysis was file-only; native/frozen/controller source files were unchanged.
