# Carnival throughput measurements and candidate ranking

The combined probe supplies useful action timings, but it is not a high-score round. Its 57 completed actions include explicit waits, setup, one serving loop, one washing loop and two throw cases. Costs that depend on repeated batches remain uncalibrated.

Source: `artifacts/combined-probe-a.jsonl.gz`, SHA256 `9db0e41605df62d2f7c78c3bd974e95989ea0c9f504273dc31047592e328043d`. Reproduce the streaming extraction without parsing frame snapshots:

```powershell
python scripts/calibrate_actions.py artifacts/combined-probe-a.jsonl.gz --out artifacts/combined-action-timings.json
```

The action duration is its recorded logical frame count divided by 60. Parallel chef times must not be added to obtain elapsed round time. Cook/mix wait actions measure the *remaining wait*, not the native full processing time.

| Observation | Measured seconds | Scope |
|---|---:|---|
| Ingredient crate pickup | 0.517 median | Six crate pickups, including movement |
| All pickup actions | 0.500 median | 16 samples, 0.117–1.000 |
| All placement actions | 0.533 median | 15 samples, 0.167–1.567 |
| Face/verify/transfer after movement | 0.083 | Ordinary take/place sequence |
| Chopping | 1.533 | Two samples; 1.450 in native chopping stage |
| Plate-under assembly | 0.500 | One bun/plate recovery sample |
| Condiment application | 0.667 | One mustard sample; switching was not measured |
| Pot-to-held-plate harvest | 0.617 | One sample; unplated harvesting may differ |
| Mixer-to-fryer pour | 1.700 | 1.617 movement, 0.083 transfer |
| Empty bowl returned to mixer | 1.567 | 1.483 movement, 0.083 transfer |
| Board cannon | 0.200 / 0.967 | Two different starting positions |
| Cannon flight/arrival observation | 0.867 | Both measured fire actions |
| Fire action including switch approach | 1.617 | 0.733 movement plus launch/arrival |
| Delivery from landing area | 0.917 | One sample |
| Portal action | 1.750 | 0.300 approach plus 1.450 receiving/travel |
| Washing one plate | 3.183 | Native configured duration 3.000; suppression guard included |
| Clean plate pickup plus counter placement | 0.833 | 0.333 + 0.500; central collection is separate |
| Ingredient throw/catch or throw-to-vessel | 0.400 / 0.433 | Staging movement is separate |

The observed native movement configuration is run speed 6, dash speed 18, dash duration 0.3 seconds and cooldown 0.4 seconds. A dash uses the native sinusoidal velocity blend and cannot stop immediately on neutral input. On an indefinitely clear, aligned corridor, repeated native dashes have a continuous-time average-speed ceiling near 10.5 units/second (versus 6 walking); actual input/physics phasing, turns, approach clearance and cooldown alter that result. This is not a measured route speedup. Dash and vessel-throw cost discounts stay disabled in the cost file until complete routes establish them.

## Partial surrogate calibration

`artifacts/probe-calibrated-costs.json` replaces directly supported terms and labels the remaining proxies. Its provenance points back to the trace hash. In particular:

- Pantry handling is crate pickup median 0.517 plus one ordinary placement's 0.083 non-navigation overhead. The additional pantry trip amortization is still an assumption; chefs cannot carry multiple raw ingredients at once.
- Chop setup is total observed chopping minus the audited 1.400-second native ingredient work requirement.
- Vessel loading uses the median 0.275 of three bowl placements and one pot placement. Generic harvesting uses the 0.617 pot sample as a proxy, not a claim that every vessel takes the same time.
- Plate assembly includes clean plate pickup 0.367 plus bun/plate assembly 0.500.
- The first single-item service loop is approximately 5.367: plate pickup 0.867, boarding 0.967, flight 0.867, delivery 0.917, portal 1.750. The cost file separates 4.450 trip from 0.917 delivery. Additional lower-right counter pickups are not measured by this first loop.
- The 3.500 washing trip is a partial estimate assembled from boarding/flight, dirty-stack handling, a portal return, and wash setup. Its exact repeated path was not recorded as one complete role cycle. Outer clean handoff is observed at 0.833; assigning that same duration to central collection remains a proxy.

Running the existing compiled optimizer with these partial costs evaluated 355 deterministic candidates. Its best reported candidate was seed 102 with an estimated 34 deliveries / 3756 fresh-tip score ceiling, versus the default model's 32 / 3532. Both are estimates; no 3756-point native run is claimed. The difference is not evidence that lowering more assumptions would produce a valid high-score route.

```powershell
dotnet controller/bin/Release/net10.0/OvercookedTAS.Controller.dll optimize-seeds `
  --file artifacts/native-seed-previews.jsonl `
  --costs artifacts/probe-calibrated-costs.json `
  --beam 8 --rounds 2 --top 10 --out artifacts/seed-candidates-calibrated.json
```

The surrogate reserves future pantry work up to its lookahead before serving a batch. This can delay service more than a state-aware priority scheduler would. Its first delivery can occur after 30 seconds while the real combined probe delivered its first hotdog near 20 seconds. Plate placement geography, changing chef assignments and actual order arrival/expiry are also absent. Use it to select experiments and identify work demands, not to reject a demonstrated legal route or certify an achievable score.

## Bottleneck goals for the native planner

Seed 8's first 45 native recipes contain 34 hotdogs, 11 donuts and 15 onion variants. All 45 fresh FIFO deliveries would give 5028 points, leaving only 28 points of tip-loss margin. Planning at least 46 deliveries gives more practical score margin.

For that 45-dish prefix, native processing alone requires 84 chef-seconds of chopping and 123 chef-seconds of washing 41 reused plates. Across the two vessels of each kind, the minimum occupied round times are 204 seconds for pots, 90 for onion pans, 66 for mixers and 55 for fryers, before loading/harvest delays. The pots are the tightest equipment resource. With 34 hotdogs and a first load around 3 seconds, both pots should turn over in roughly 14–15 seconds each and should not wait for plates or ordered delivery.

After a first delivery at about 20.4 seconds, delivering another 44 by round end requires an average interval below 5.68 seconds. The measured one-item service loop already consumes 5.37 seconds of the pantry/service chef before that chef performs any ingredient work. The route therefore needs lower-right service batches through the shared counters, rather than one complete cannon/portal loop per dish.

The following goals have the largest effect on measured work:

1. Keep both pots loaded; harvest into legal unplated bun buffers. Preserve cookware reservations during passive cooking but free both central chefs for loading, assembly and transfers.
2. Amortize cannon/portal role changes across several deliveries and dirty plates. Reserve all required clean plate tokens and shared-counter capacity before a service wave; flush a partial wash batch before four plate tokens are exhausted.
3. Shorten the 3.267-second mixer/fryer/bowl-return trip through a better approach or division of labor. Across 11 donuts this is nearly 36 chef-seconds. Native mixer bowls lack `ThrowableItem`, and filled unplated buns veto throwing; neither is a valid whole-container throw optimization.
4. Move ingredient work between available chefs when the pantry/service chef is approaching saturation. Seven native chop impacts are fixed; repeated cross-room approaches and handoffs are the variable costs.
5. Use optional dash navigation only where its observed native corridor and stopping bounds permit it. The current conservative full-dash bound limits short station approaches; a global travel discount would overstate its effect.
6. Give a ready FIFO delivery priority over additional speculative pantry reservations. Native order/tip observation must decide whether further lookahead is useful.

The optimizer's newer source reports chef busy seconds and vessel occupancy, clipped to the native round, and returns the best evaluated parameters for each seed to avoid filling the result with equivalent reserve settings. These fields still describe the surrogate schedule, not telemetry from a native run.

## Implemented adaptive candidate

`controller/CarnivalPlanner.cs` dispatches one native action at a time for each chef. Every frame it reconciles the observed world, completes previous actions, allocates nonoverlapping entity reservations, then emits all four chefs' inputs together. Pots, pans, mixers and fryers continue their native passive processing while both center chefs perform other jobs. There is no timer, cooking-progress, inventory, score or position mutation.

| Chef | Default job | Working route |
|---|---|---|
| P2, upper left | Hotdog pantry and FIFO service | Throw into the nearer pot and supply the farther pot through an addressed shared counter; supply onion and bun boards; board the left cannon for lower-right delivery waves; return through the verified portal. |
| P1, upper right | Donut pantry and plate washing | Supply near-bowl flour/egg by measured throws and far-bowl flour/egg through the addressed shared counter; provide flavor for chopping; cannon to lower left to wash and hand back plates. |
| P0, center | Hotdog preparation and shared jobs | Chop, load onion pans, harvest sausages into unplated buns, complete onions, plate/condiment, and clear blocked handoffs. |
| P3, center | Donut preparation and shared jobs | Chop flavor, complete mix bowls, pour finished dough into baskets, return empty bowls, plate and stage orders, and clear blocked handoffs. |

These are priorities, not exclusive job ownership. Both center chefs can perform the other's work. Each whole job reserves its source, carried item, vessel and destination where applicable. A cooking vessel's contents retain a recipe assignment while the chef is free; a bowl-to-basket job transfers that exact order index. Consecutive equal donut recipes receive separate assignments rather than being collapsed into one matching ingredient shape.

The current center priority is: harvest urgently cooked onions into their reserved hotdogs; fire an actually loaded cannon; finish the oldest ready order; clear the clean handoff; bring a dirty stack when washing is idle or clean plates are exhausted; finish missing onions in a staged hotdog; unload cooked sausages into buns; transfer ready dough; plate later eligible meals; load/chop ingredients; clear remaining raw and dirty transfers. These priorities are a candidate policy and need whole-round measurement.

### Plate and counter admission rules

- Later dishes cannot use the final available clean plate until the oldest dish has a separately reserved plating job or a completed plate. Unplated preparation does not count as allocating that plate.
- The lower-right counter at `(25.2, -19.2)` remains reserved for the oldest dish. Later completed dishes use the two neighboring counters. The first ready dish may instead use the upper-left pass when P2 is there.
- Ordinary unplated-food and clean-plate storage excludes the center counter at `(20.4, -16.8)`. The oldest dish may borrow it; if all storage is occupied it may temporarily reuse its own bun board.
- An unplated hotdog contains a bun already allocated to that order. It reduces that order's outstanding ingredient demand but never counts as a loose bun for a different order.
- Plate-under-food assembly targets the stable parent counter. The original food entity is consumed natively; retaining that disappearing entity as the recovery target caused the second live attempt to stop. The parent remains reserved through recovery of the filled plate.
- For a two-condiment hotdog, the surface just vacated by plate assembly becomes the temporary plate parking spot during the empty-handed condiment switch. This avoids requiring an unrelated spare counter in a crowded kitchen.
- A cooked sausage can be harvested into its bun before the onion is ready. The planner later carries that exact staged hotdog to the cooked onion pan and returns it to its counter. The delayed onion addition still needs native execution evidence.
- Chopped onions stay off heat until their matching unplated bun-plus-sausage exists. Occupied onion pans cannot exceed the number of those waiting bases. This prevents cooking seed 0's fifth dish during the initial first-delivery wait. Above `1.3 × cookingTime`, onion completion takes priority; the native burn threshold is above `2 × cookingTime`.

The washer waits with a clean plate if the one-item handoff is occupied. Center cooks clear that handoff before speculative preparation. The washer's return-to-bakery decision counts accessible plates, not clean inventory stranded on the drying station, and waits for drying output to be handed back.

### Optional cooperative condiments

`bot --cooperative-sauces` enables a two-chef workflow for the native mustard-and-ketchup hotdog; its default is false. It starts only with two idle, empty-handed center chefs and an available complete base, plate and output. Otherwise the existing serial workflow remains available.

One shared lease reserves the source food/counter, plate, output, dispenser and switch. The owner prepares and holds the plate at the dispenser while the helper remains empty-handed at the switch. The first apply waits for both setup actions, the exact native base plate and observed mustard index. The helper switches to ketchup only after native mustard composition is present in the owner's exact plate. The second apply waits for the observed ketchup index, then native complete-recipe evidence permits staging. Both chefs and all shared resources are released after the exact plate is observed on its reserved output.

The coordinator logs every lease, barrier, action and completion. Child jobs cannot release the shared locks independently. A phase timeout, lost participant constraint, wrong dispenser index or missing food evidence fails the attempt and emits an all-chef neutral frame. Checkpoints retain unfinished coordinator state for same-instance resumption; native round end releases local ownership after neutral input. This feature has passed synthetic barrier/timeout tests but has no native timing or completion claim yet.

`--short-dash` implies `--dash` and forwards the native short-dash policy to supported navigation actions. Its admission and neutral-coasting behavior remain governed by `NativeDash`; forwarding a request does not bypass its native speed, cooldown, collision, facing or stopping checks. Transport child actions retain their own additional restrictions. Both dash options default off.

### Measured budget and the next useful adjustments

The calibrated surrogate's best seed-102 candidate reports P0/P1/P2/P3 busy times of approximately 71.0/216.9/251.2/65.2 seconds and total pot occupancy of 401.9 out of 540 available pot-seconds. Those are surrogate calendars, not native measurements. They point to an experiment: move work to the underused center chefs while measuring whether the two outer chefs' actual trips shorten.

For seed 8's 45-dish prefix, P2 must obtain 83 ingredients: 34 sausages, 34 buns and 15 onions. A rough three-delivery wave using the first-loop measurements costs about `4.450 + 3 × 0.917 + 2 × 0.500 = 8.20` seconds, where 0.500 is only the observed generic pickup median used as a proxy for each extra lower-right plate. Fifteen such waves would consume about 123 P2 seconds. The 83 pantry pickups plus ordinary transfer overhead alone add about `83 × (0.517 + 0.083) = 49.8` seconds; staging, travel and actual throw handling remain additional. This leaves roughly 97 seconds for those additional P2 costs in a 270-second round. Single-delivery loops would consume about 242 seconds before pantry work, so batching is a necessary design target under the current role split.

For P1, 41 native washes use 123 seconds, and the measured 0.833-second clean pickup/handoff loop adds roughly 34.2 seconds. Thirty-three donut ingredients add around 19.8 seconds using the same pickup/transfer proxy, before their travel and throws. About 93 seconds remain for bakery movement, role transitions and waiting. This budget is demanding but not a proof of impossibility; native concurrent traces must establish the actual costs.

The next full native candidate should report pot empty intervals, cooked-food waiting time, service-wave size, P1/P2 role-transition count, time with no accessible clean plate, and center idle time while an outer handoff is blocked. Use those observations to decide whether to keep larger service waves, shorten pantry approaches, allow measured safe dashes, or move receiving/throwing work to a center chef. Do not tune an assumed travel discount merely to make the surrogate exceed 5000.

## Current evidence boundary

The first full-planner attempt stopped around gameplay frame 832, score 0: a throw toward the far mixer bowl landed on the intervening chopping board. Far-bowl flour and egg now use a counter handoff with an exact vessel address. The second attempt created its first native unplated hotdog at frame 1067 and stopped during plate recovery because the target food entity had been consumed; the stable-parent assembly fix is now in source. Neither attempt demonstrates a high-score round.

The corrected first-delivery trial, `planner-first-c2`, delivered the first mustard hotdog at gameplay frame 1463 for 68 native points. The following four-delivery attempt stopped at frame 1894 on a navigation failure, still at one delivery and 68 points. Its measured trace shows pot 2 waiting cooked for 9.83 seconds, then remaining empty for 6.68 seconds at the tail; the far-pot throw was still open. P1 spent 22.98 of the observed 31.58 seconds without a planner job. Pan 4 reached 23.69981 native cooking seconds against a 24-second burn threshold, and pan 9 reached 16.31659; neither had become a native Burnt food node. These observations motivated the onion heat admission rule.

The next trial, `planner-four-b`, reached four native deliveries and 356 points at frame 3040, then ended its requested checkpoint at frame 3041. Delivery frames were 1386, 2236, 2920 and 3040: intervals of 14.167, 11.400 and 2.000 seconds after the first. Service visits contained one, one and two deliveries. P0/P1/P2/P3 assigned-job time was 43.667/18.533/25.633/25.150 seconds out of 50.683; P2 also spent 7.133 seconds without a job while native controls were disabled for transport. These short native measurements currently point to center work and transport scheduling, whereas the full-prefix surrogate predicted outer saturation. A 7.767-second two-condiment assembly and 4.900-second far-bowl counter relay are concrete optimization targets. Native dispenser handling requires `ServerPlacementContainer`, which the prepared unplated bun lacks, so applying these sauces directly to an unplated bun is not a legal shortcut.

No onion pan was heated before an observed base existed in four-b. Its first onion base finished at frame 2920 and the pan-loading job was still active at the checkpoint. This preserves the ingredient but postpones cooking; an eight-delivery baseline must measure that cost and establish delayed onion completion plus delivery of a washed plate before adjusting heat lookahead. No eight-delivery or full-round result is asserted here.

The next eight-delivery attempt stopped at frame 4214, still at four deliveries and 356 points, after sausage 215 missed the far pot and left the kitchen. The near pot remained full through its flight, so this was not a compatible-near-pot interception. The occupied onion board lay along the failed lane. Far-pot supply now uses the upper-left counter with an exact destination address; its raw transfer respects an existing plate-handoff reservation. The recorded trajectory establishes that the previous direct lane is unsafe in that state, without claiming a newly validated alternate throw arc.

An isolated Release controller build completed with zero warnings and zero errors. `CarnivalPlanner.SelfTest` passes 53 offline checks against the recorded `artifacts/cycle-start.json` kitchen plus narrowly modified food/resource states. The checks cover stale cannon IDs, native nested mixed-food shapes, addressed far-vessel handoffs, stable-parent plate recovery, plate/output/workspace reservations, ingredient accounting, onion-independent sausage harvest, heat admission, distinct equal-donut assignments, cooperative condiment barriers/resource release, timeout neutralization, and opt-in navigation flags. These checks validate dispatcher decisions; they do not claim that newly composed native action sequences have executed successfully.

Run `scripts/analyze_planner.py` to obtain bounded streaming performance reports from completed or in-progress JSONL/gzip traces. `artifacts/planner-four-a-analysis.json` preserves measured durations, sampled inventory/heat intervals, native events and the first failure; `scripts/test_analyze_planner.py` contains seven targeted offline analyzer checks.

The 20-replay probe gate and five fresh-process high-score gate remain separate requirements. At the time of this review, no completed native round with score at least 5000 has been recorded by this planner.
