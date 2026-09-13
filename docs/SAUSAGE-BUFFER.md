# Optional native raw-sausage reserve

`--sausage-buffer 1` or `--sausage-buffer 2` enables `CarnivalPlannerOptions.SausageBufferSize`. Default `0` retains the existing planner. Values outside `0..2` are rejected before native requests. Two slots are the intended trial configuration, not a demonstrated performance improvement. This implementation changes ordinary input scheduling only.

## Measured reason for the change

The closed V9 trace has two pots idle-empty for a combined 113.70 pot-seconds through GF9038 (150.633 seconds). Examples are pot2 empty GF3667–4707 (17.33 seconds) and GF8019–8996 (16.28 seconds). P2 was idle GF7454–8060 (10.10 seconds), with both pots occupied for most of that interval. The unchanged GF7454 response has empty pantry hands, empty pass45, occupied pots2/7, and free ordinary center storage.

Evidence:

- `artifacts/v9-sausage-buffer-analysis.json`, produced by `scripts/inspect_sausage_buffers.py` from `artifacts/native-round-v9/trial001.jsonl.gz`. The bounded projection checks ten selected samples against full JSON decoding and records its compressed-source prefix hash.
- Unchanged native responses: `artifacts/v9-sausage-buffer-gf7454.json` and `artifacts/v9-sausage-buffer-gf8019.json`.
- `artifacts/native-pot-capacity.json`: original scene `s_day_3_4`, SHA256 `5013b16ff9856f2337dfe50086b7697e72a1921be552cbc82f415123c82e7d3e`, IngredientContainer objects8702/9060 both have capacity1. The generic exported prefab's capacity3 does not apply to these scene instances. Native `ServerIngredientContainer.CanAddIngredient` compares current count with that capacity.

## Admission and ownership

After normal pantry assignments, otherwise idle P2 can reserve one raw sausage while both original native pots are occupied and pending hotdog demand exceeds observed sausage stock. It first reserves an actually empty ordinary center counter and the pantry pass resolved at `(15.6,-13.2)` (native ID45 in the captured scene). It excludes the protected FIFO workspace `(20.4,-16.8)`, all occupied/reserved storage, and existing addressed handoffs. The crate is resolved by its native `Frankfurter` ingredient property. No future full pot is addressed or reserved.

P2 performs ordinary `take(crate)` then `place(pass)`. After the successful native pickup, the lease records the actual held ingredient ID and observed ordinal and requires a pure raw Frankfurter. Supply has a 120-frame whole-job bound. Its final proof requires that same raw item attached to the pass and empty pantry hands.

An otherwise idle center cook can take that raw item and park it on the reserved center counter. Only the observed successful parking releases the pass. The exact ingredient and storage counter remain reserved; no chef remains assigned. Full pots are an ordinary waiting condition and do not time out stationary raw stock.

Refill is checked after urgent heat/FIFO/fire/plate logistics and before future base assembly. It chooses a currently empty, reset native pot on its initialization-observed stove, with matching pot/stove ordinals and no other reservation or addressed supply. It reserves that pot/stove for `take(stock)` then `place(pot)`. A newly emptied pot can also receive directly from the pass, avoiding parking. Completion requires empty original source/hands, the exact original raw item consumed, the same pot/stove attachment, and the native pot cooking node containing exactly one Frankfurter with cooking step20068. The stock lease releases only its own still-owned resources. An old buffer completion cannot remove a later job's lease on the reused pass.

Parking and loading now receive one fixed total at admission: the two full legal walking paths at observed native speed, plus 90 frames for the two interactions and 180 frames for bounded navigation recovery. The total may not exceed 600 frames. Both actions retain their own finite timeout, while the transaction observer enforces the single aggregate deadline; pickup and replanning never extend it. An infeasible path or over-cap estimate declines admission without changing the stationary stock lease. Exact source/destination ordinals, original Work identity, resource ownership and observed raw pickup/consumption remain checked throughout. Supply still has its separate 120-frame bound.

This replaces the 240-frame aggregate that actually aborted V18 while the correct raw item was still held during placement. See [the native failure and fixed-budget evidence](SAUSAGE-TRANSFER-BUDGET.md). Existing RouteRunner native interaction, motion, collision and transfer guards remain active.

## Bounded service departure

Before a ready FIFO departure, raw buffering may admit at most one pantry supply in the existing per-visit pre-service allowance. It shares the 240-frame total ready-head budget, 120-frame sausage job budget, exact oldest-order/meal identity, native tip-band admission check, and 10-second service plus one-second safety allowance. This works even when ordinary `--pre-service-stock` is disabled. The protected head is released as soon as the raw item reaches the pass; it does not wait for center parking or an empty pot.

An existing ready meal on pass45 blocks admission cleanly. `--service-side-head` can keep the pass available, but the buffer does not move that meal or silently enable another option. Existing pre-service work and raw buffering consume the same one-job allowance. Ordinary pantry production retains priority when no head is ready; the buffer does not preempt an active chef action.

## Verification and limits

`CarnivalPlanner.SausageBufferSelfTest(idle7454, ready1108)` has 64 checks. It tests the unchanged recorded admission opportunity, then explicitly constructed native-equivalent state transitions for two independent buffers, full-pot waiting, direct pass refill, native consumption, reused pass ownership, changed ordinals, missing resources, finite active transfers, and guarded FIFO delays. The ready-head branches explicitly move fixture data to a separate counter and mark both fixture pots full; they are synthetic lifecycle tests and are not claimed as recorded routes.

Isolated build and results: `artifacts/sausage-buffer-check/`. The harness also exercises pre-service, staged release, parallel/early onion, pantry chopping and baseline planner checks. No frozen candidate or runtime plugin was changed. A native candidate trial remains required to establish route liveness and throughput.

Storage costs one ordinary counter per raw sausage. A busy center can delay parking/refill; the option does not guarantee continuous pot use or a high score. Exact stock metadata belongs to the current planner instance and is not reconstructed when attaching a new planner to an already-running round.
