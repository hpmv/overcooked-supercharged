# Optional parallel onion scheduling

`--parallel-onion` sets `CarnivalPlannerOptions.ParallelOnionHeat`. It defaults to false and works independently of `--early-onion`. Enabling only the existing `--early-onion` option retains its original one-lease, both-central-chefs-idle admission policy. This change affects controller scheduling; it changes no native cooking durations, food, score, physics, or inputs.

## Measured reason for the change

The bounded V6 analysis in `artifacts/native-round-v6-THROUGHPUT.md` and `artifacts/native-round-v6-selected-checkpoints.json` identifies two separate blockers:

- After meal five's first early-pan lease ended at frame 3042, meal six's chopped onion had been ready since 2385. Chef 0 was still assembling meal five through 3324. Requiring both central chefs idle prevented the other chef from starting onion heat.
- Meal seven's chopped onion was ready at 3908. Pan 4 was occupied by meal six through 4409, while pan 9 remained empty. The original admission rejected any existing onion pan. Later unrelated bun construction also preceded starting meal seven's native cook, which began at 4878.

These observations show scheduling opportunities; they do not measure a speedup for the new mode.

## Admission and native completion

`CarnivalEarlyOnion.cs` tracks at most two leases, each owning a unique meal index, actual native pan and observed ordinal, original cooking station, and separate empty parking counter. Both central chefs must remain in the center with native controls enabled; only the loader needs to be idle and emptyhanded. A static route to the pan and its parking counter must exist. The protected FIFO workspace and cooking stations cannot be parking locations.

An existing ordinary onion pan can be adopted only while its native progress is below 12 seconds, its pan and stove are free, and a pending meal has an observed attached plain bun-and-sausage base matching native recipe 296560. Adoption assigns that precise meal, pan and counter without changing the food or cooking progress. It is refused when both physical pans are occupied, when the meal is already owned, or when the base or parking evidence is missing. Mature ordinary onions remain on the existing immediate-harvest path.

The per-pan rescue is unchanged from the native offheat proof: at 9 seconds, claim a free chef to approach; require the native cooked composition and progress strictly greater than 12; use ordinary pickup and placement to move the entire pan off heat; require the exact pan attached to its reserved counter with unchanged composition and progress across two distinct advancing frames and a decreasing native timer. The hotter pan claims its rescue chef first. Each chef is excluded from other work while owning a rescue.

Only the leased meal may consume its parked onion. The emptied pan must be observed back on its original stove before that lease is released. Completing one lease preserves the other lease's identity, reservations and meal exclusion, including when the original primary lease has finished first.

The controller stops the candidate at native onheat progress 21 seconds, before the native burn threshold above 24 seconds, if rescue has not been established. Finite action and phase limits remain. This is a failed candidate, not successful rescue. Static route admission does not guarantee that later dynamic blockage will clear.

Central priority keeps ready FIFO assembly, cannon firing, urgent mature cooking harvest, and clean/dirty logistics ahead. Onion heat may precede its own missing bun-and-sausage base or a later unrelated base; an earlier missing hotdog base retains priority. Ordinary onion assignment excludes both leased meal indexes.

## Validation and limits

The underlying single-pan legal native transfer is established by `artifacts/onion-offheat-a-proof.json`: cooked onion progress 12.1666527 stayed identical across 809 samples over 13.4500027 native seconds off heat, then the onion was consumed into native recipe 472326 and the empty pan restored.

New offline regression `CarnivalPlanner.ParallelOnionSelfTest` has 47 assertions: both observed admission patterns; unchanged baseline admission; separate resources; simultaneous rescue and independent native progress proof; second-pan deadline enforcement; exact ordinary-pan adoption and rejection cases; independent cleanup; and preparation priority. It performs no game calls and does not replace native trial evidence. Existing 45 early-onion, 29 pantry-chopping, 71 planner and 15 retired-plate assertions also pass. The ordinary controller selftest passes with the combined chef-circle navigation source.

Reproduce the isolated checks from the workspace root:

```powershell
dotnet build controller/OvercookedTAS.Controller.csproj -c Release --artifacts-path artifacts/parallel-onion-check
dotnet run --project artifacts/parallel-onion-check/harness/ParallelOnionCheck.csproj -- artifacts/cycle-start.json
dotnet artifacts/parallel-onion-check/bin/OvercookedTAS.Controller/release/OvercookedTAS.Controller.dll selftest
```

Concurrent pans consume two storage slots and can occupy both central chefs during rescue. This option does not solve missing ingredient supply, lack of clean plates, or service transport. It requires a completed native trial and comparison before making a throughput or score claim.
