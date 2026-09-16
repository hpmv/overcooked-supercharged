# V11 heat arbitration diagnosis

V11 stopped at gameplay frame2711 when native pot7 became Burnt at24.0165 seconds of cooking. The trace includes a final neutral frame2712. Score was156 with two deliveries. The new fryer mechanism worked: basket8 was parked off heat with progress11.1000 and proved stable at2604. This is a failed prefix, not full-round or high-score evidence.

`artifacts/v11-heat-arbitration-audit.json` records native heat/chef state beside each relevant planner decision. Full unchanged responses are saved as `artifacts/v11-heat-boundary-gf<frame>.json` at the frames below. The source is `artifacts/native-round-v11/trial001.jsonl.gz`.

| Frame and free chef | New job selected | Pot7 cooking seconds | Future bowl6 mixing seconds | Basket8 cooking seconds |
|---|---|---:|---:|---:|
|2382, chef0 finishes empty-fryer restoration|Head3 ketchup assembly|18.5332|12.3583|7.9500|
|2384, chef3 finishes firing right cannon|Dirty-stack relay|18.5666|12.3916|7.9834|
|2489, chef3 finishes dirty relay|Rescue basket8|20.3165|14.1416|9.7334|
|2604, chef3 completes basket8 offheat proof|Load a buffered raw sausage into empty pot2|22.2332|16.0583|11.1000, already off heat|
|2627, chef0 completes head3 assembly|Start an onion pan for meal5|22.6165|16.4416|11.1000, already off heat|
|2711, both center jobs remain active|Native ruined-food failure|24.0165 Burnt|17.8416|11.1000, already off heat|

Chef0's existing head assembly occupied2382–2627. Chef3's dirty relay occupied2384–2489 and fryer rescue/proof occupied2489–2604. The issue cannot be solved only by reprioritizing the two new jobs at2604/2627: across-room travel may already be longer than the pot's remaining safe window. Earlier completed-job boundaries exist at2382,2384 and2489.

The controller agent checked full-clearance pot7 approach paths using the frozen V11 geometry: GF2382/chef0 is11.348 units (1.891 native walking seconds), GF2384/chef3 is8.476 units (1.413 seconds), and GF2489/chef3 is7.689 units (1.281 seconds). These are approach-only lower bounds; native pickup still has to finish before the heat deadline, and later parking occupies the chef after heat stops. Empty physical counters41/43 existed at those snapshots;32/33 were reserved for future/raw buffers. The2384 boundary offers4.433 seconds to a23-second guard, whereas2489 offers only2.683 seconds. The earlier boundary is materially safer; these offline path estimates are not a native rescue proof.

At2489, pot7 had3.6835 native seconds before burning, versus10.2666 seconds for basket8. Using the proposed23-second pot guard and existing19-second fryer guard leaves2.6835 versus9.2666 seconds. The existing family priority chose the less urgent fryer. Pot offheat rescue had not yet been implemented, so a common arbiter also requires that native mechanism and ownership path; changing a comparison alone cannot manufacture a legal rescue action.

## The mixer case is not merely a lower-priority parking job

At2604 bowl6 contains native Mixed **flour and egg only**. Its exact native composition lacks raspberry. Already chopped Raspberry173 is attached to board24. The future-bakery lease for orderIndex10 was acquired at1504 and remains `Supplying` without an owner at2711.

`AdvanceBakeryLookahead` requires the full exact recipe before creating its parking job. This partial bowl therefore cannot be rescued by that path at any priority. The existing legal `add-chopped-donut-flavor` action, reached through the lower-priority `PrepareDonut`, is the available candidate: take the exact chopped raspberry, place it into the exact partial bowl, and observe the new native composition/progress. Adding an ingredient affects native mix progress; the scheduler must observe that change and must not reset progress itself.

A generic partial-bowl parking path would be a separate implementation and proof obligation. The bounded first fix should elevate the already supported missing-flavor transfer when the bowl is nearing its native overmix deadline. A fully prepared dough, partial dough, raw ingredient and offheat parked vessel need distinct candidate actions.

## Implemented common admission step

The next candidate source implements this scheduler in `controller/CarnivalHeatArbitration.cs`. It has passed offline captured-state and synthetic lifecycle assertions. Its full-round native behavior remains unverified.

1. Finish completed actions and update existing fryer, pot, onion and bakery lease phases. Onion and bakery phase updates no longer assign a new owner before common arbitration. Existing owned work continues unchanged.
2. Enumerate active, nonempty vessels actually attached to native cooking or mixing stations. Empty or detached vessels have no heating obligation. Every observed processing clock retains an independent guard check, even when an existing job already owns the vessel.
3. Order unclaimed mature obligations by remaining guarded native time, then native vessel ID. For the earliest obligation, choose an available empty-handed center chef by full-clearance path/native walking time plus a two-second interaction allowance, with player ID as the tie-breaker. Include native completion waiting when needed. This is deterministic greedy admission, not a global optimality or deadline guarantee.
4. Dispatch the exact selected vessel's legal action. Cooked pots may be harvested directly using the selected pot and a prepared bun when the full route fits; otherwise the proved pot parking mechanism applies. Fryers use their existing direct plating or parking mechanism. Full bowls use exact recipe transfer or proved full-dough parking. Partial bowls use their exact already-chopped missing flavor; an exact raw flavor on a board instead receives a native chopping prerequisite, retaining the bowl lease. Both flavor cases budget the subsequent board-to-bowl walk. No partial bowl is parked.
5. An ordinary onion pan may be adopted into the existing exact-pan offheat lease even when its matching plain base is unavailable. This mandatory safety path does not require optional early-onion admission. It reserves the original pan, stove and an ordinary parking counter, then uses the same native detachment/proof/combination/restoration lifecycle. The recipe's plain base can be prepared while the onion is safely parked.
6. If the earliest obligation lacks a feasible exact action, reservation or approach budget, prevent available center chefs from taking unrelated elective work. Recompute on each observation without replacing or restarting any active Work, action or owned resource. Record dispatch and blocking decisions, native progress, identity, path budget and slack. Deadline expiry remains a failed candidate, with neutral cleanup; it never changes game timing or food.

Empty speculative future bowls now wait for their flavor to be prepared before starting flour and egg, avoiding an unnecessary partial-mixing clock. Normal shared-board supply/chopping remains responsible for that preparation.

The guard deadlines are controller failure thresholds, not changes to native cooking durations. V11's existing thresholds are19 seconds for a10-second fryer and21 seconds for a12-second future mixer/early onion. A23-second pot guard leaves one native second before burning. Route and interaction estimates are admission estimates; collision delays remain possible and must be observed.

## Validation and remaining costs

`scripts/HeatArbitrationCheck` passes25 common-admission assertions,54 pot-rescue assertions,34 updated cannon-boundary assertions and375 related planner/lease assertions against the same isolated current-source assembly. The controller core self-test passes545 assertions. These checks make no native game calls.

The unchanged GF2384 fixture selects chef3's exact pot7 rescue before dirty relay, preserving chef0's active work and leases. GF2489 still ranks pot7 before basket8 but rejects its late approach plus the conservative two-second allowance; it blocks electives instead of choosing the less urgent fryer. The GF2604 partial-bowl fixture verifies the exact missing Raspberry173 transfer and both walking legs. Explicit synthetic variants cover partially chopped flavor with PantryChopping disabled, an ordinary onion without a plain base, busy chefs, unexplained reservations and detached vessels.

The previous optional cannon fixture at GF1287 contains a real mature bowl3 at9.279 seconds. It now correctly rejects cannon preemption while that unclaimed heat obligation remains. The unchanged measured button geometry and ownership/resume cases are separately exercised using an explicitly synthetic earlier mixer clock. This is a changed scheduling policy, not new native detour evidence.

Admission is intentionally conservative. Pot eligibility starts at11 seconds, fryer eligibility at9, and onion/mixer eligibility at9. Consequently an idle chef may commit to parking before the native12-second pot or10-second fryer becomes Cooked, even if a bun or plate would soon permit direct harvesting. The explicit native wait completes first, then ordinary pickup parks the vessel. This may add travel, proof and restoration work; a later optimization can compare a bounded native wait followed by direct harvest. The current candidate must first establish safe sustained operation.

The two-second allowance is an estimate, not a measured maximum. Ordinary-pan route estimation may also include the base-to-pan leg even if fallback adoption parks the whole pan, conservatively declining some late cases. Only new native runs can establish the resulting throughput and whether congestion still causes a deadline failure. No high-score or full-round success is asserted here.
