# Optional plate-first hotdog base

`--plated-hotdog-base` enables `CarnivalPlannerOptions.PlatedHotdogBase`; the default is false. The optional branch sits inside the existing `BuildUnplatedHotdog` candidate, after that method selects its exact pending recipe, chopped-bun source, food and pot. It does not scan past an earlier onion recipe merely because a later plain recipe fits the feature. If new admission fails, the same existing unplated candidate continues.

The first scope is a hotdog with no onions and zero or one condiment, no existing `mealFoods` entry, an actually available clean plate, and an unleased sausage pot on its original stove. Ordinary entry requires native Cooked sausage. The only uncooked entry is the existing exact-pot wait request with `NearReadyPotHarvest` enabled and native progress11–12 seconds. A parked pot and its restoration lease remain in the original fallback; this feature does not adopt or rewrite them.

## Native sequence and workload motivation

The independent frozen V19 probe `plated-hotdog-sauce-before-onion-b` demonstrates the relevant native order: plate plus prepared bun, same-plate recovery, Cooked sausage, then Mustard. Native bun-only plate contents are a prepared ingredient observation, not one of the nine complete recipes. The observer therefore explicitly verifies the one chopped-bun ingredient before sausage transfer, then requires the exact native plain-hotdog shape, then the requested full recipe.

The unchanged native candidate at `artifacts/v16-plate-first-opportunities/gf789.json` binds buffered bun143 on32 and near-ready pot7 on19 at11.0000038 seconds for the original first-meal job. The new branch admits that same candidate in offline checks. `gf6377.json` is an important negative: cooked pot7 is parked on33 with its original stove19 empty, so the branch refuses even though the static plate-first path appears attractive.

`artifacts/v16-plate-first-path-review.json` compares the recorded old unplated-plus-assembly path sums with a single-state plate-first plan. For example GF1589 has33.02 old planned units versus24.22 proposed, and GF6090 has29.95 versus18.62. GF789 changes only18.20 to17.15. These are different-state planned paths, not observed movement or timing gains. Some other captures have no available plate. A native production run is needed to determine whether consolidation improves throughput without delaying supplier refill or another meal.

## Admission, timing and ownership

Admission uses the existing idle central-chef, native control, participant, exact prepared source, original pot/stove, real plate, FIFO allocation and output checks. Native identities and recipe assignment must be present and active. The source bun's registration sequence, Unity instance and observed ordinal are pinned. A later candidate cannot take the sole usable clean plate when an earlier Pending recipe admits ordinary assembly with that same plate. Two clean tokens allow parallel allocation.

The ordinary input sequence is: switch the required condiment while empty handed, take the exact clean plate, assemble the exact chopped bun and recover the same plate, navigate to the selected pot, wait for native Cooked, combine, apply zero/one condiment, and place on the selected lower-right output. A switch already in the required state completes through the existing native switch action. The pot never moves.

Admission requires collision-aware paths for the full sequence. Walking through the pot plus the full remaining native cooking wait and3.5 seconds of input allowance must fit `23 - cookingProgress`. The full route adds downstream travel and one/two seconds for final placement/condiment handling, must stay below14.5 seconds, and fixes a deadline of `ceil(totalSeconds * 60) + 30`, capped at900 frames. Optional dash never shortens this conservative budget. The existing independent23-second pot observer remains active throughout.

One Work owns the exact board/buffer, bun, plate, pot, original stove, condiment resources and output. The initial plate counter must be free at admission; its native occupied state protects pickup, and a successor may use it after pickup. In this first scope, bun-source and pot locks remain held until final completion. There is no early source release, no new persistent lease, and no cannon preemption of this Work. This conservative retention may delay a refill and must be assessed in native recordings before adding a staged-release extension.

## Proof and completion

The per-frame observer proves empty-plate pickup, exact native bun-only contents, the original source becoming inactive, matching registration removal within three frames, and same-plate recovery at the original source. The short inactive/removal interval matches native probe B. It rejects source ID reuse or arbitrary disappearance. Native Cooked sausage must be observed before the pot empties into the exact held bun-only plate. The resulting plain plate and reset original home pot must then persist while the requested condiment is applied and the plate reaches its original output.

Only final exact recipe/output/empty-hands evidence updates `mealPlates[index]` and clears the assembling/plating markers. Buffered-bun metadata, if used, is retired only at this completion after proven consumption. No intermediate unplated meal is invented. Callback release affects only the original Work; unrelated or successor plate-origin owners retain their leases.

Events `nativePlatedBaseAdmitted`, `nativePlatedBasePlatePickedUp`, `nativePlatedBaseBunAcquired`, `nativePlatedBaseCooked`, `nativePlatedBaseSausageConsumed`, `nativePlatedBaseSauceObserved` and `nativePlatedBaseComplete` record exact identities, recipe, source registration, stage evidence, fixed deadline and admission paths. They are observations of legal inputs and native changes, not native food or timer writes.

`CarnivalPlanner.PlatedHotdogBaseSelfTest(at789, at6377, preview)` and `scripts/PlatedBaseCheck` cover unchanged native admission/exclusion plus explicitly synthetic transitions and negative ownership fixtures. The suite also reruns plated-onion, clean-plate fairness, near-ready-pot and core planner checks. The counted test receipt is `artifacts/plated-base-tests.json`. Production admission, comparative timing, full-round success and the requested final score require separate native evidence.
