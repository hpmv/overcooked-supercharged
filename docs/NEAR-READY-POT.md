# Near-ready pot harvest

V16 adds the default-off `NearReadyPotHarvest` option (`--near-ready-pot`). When a native sausage pot has reached 11 seconds but has not yet reported Cooked, a free central chef can pick up an already chopped bun, approach the original pot, wait for native Cooked, combine the sausage into that bun, and return the same food to the ordinary unplated-job output. The pot remains on its original stove. Failure to admit this branch leaves the existing whole-pot rescue policy available.

This is a controller scheduling change. It adds no plugin hook, native state write, timer adjustment, cooking override, or input-claim override. The native mechanism has been observed using frozen V15 primitives; the adaptive V16 admission still needs a production trial.

## Captured opportunity

The first recorded V14 opportunity is gameplay frame 789 in `artifacts/near-ready-pot-audit/native-round-v14-gf789.json`. Pot7 on stove19 contains a native sausage at 11.0000038 seconds, chef3 is available, and prepared bun143 is buffered on counter32. The ordinary Cooked-only direct-harvest predicate rejects this state. Another native chopped bun149 is on board23.

The new admission uses the existing buffered-bun preference and FIFO unplated recipe/output selection. Full-clearance measured paths from chef3 to counter32 and from counter32 to pot7 are 2.4204017m and 2.4m. At the observed native walking speed6m/s, the conservative consumption estimate is 3.8034 seconds, including the remaining native cook time and a two-second interaction margin. The existing pot guard has approximately12 seconds remaining. The second captured opportunity, pot2 at frame843, has a3.7919-second estimate. Offline fixture tests explicitly reconstruct the recorded ordinary Work reservations and the other pot's persistent rescue lease.

The original first pot7 sequence has four whole-pot rescue actions, three subsequent unplated assembly actions, and two empty-pot restore actions. The new direct sequence has five actions, removing four whole-pot transport actions. This action-count reduction is established by the source. It is not a measured net frame saving: the alternative changes travel, wait placement, stock availability, and later scheduling.

The broader event audit found ten near-ready rescues with prepared-bun witnesses in each of the closed V14 seed0 and seed59 traces. These are opportunities in the original traces, not additive counterfactual savings or proof that every alternative can be admitted. Evidence and qualifications are retained in `artifacts/near-ready-pot-audit/summary.json`.

## Admission and ownership

`controller/CarnivalNearReadyPot.cs` requires the exact original pot/home identities, active native station attachments, pure sausage, progress11–<12, a free controlled central chef, an exact active chopped bun on a legal central counter or chopping board, a pending hotdog recipe, and the existing selected legal output. Cooking and mixing stations cannot serve as the source/output. Full native geometry paths must exist for pickup, pot approach, optional cooked onion consumption, and output return. The consumption budget must fit the pot's23-second guard; an optional ordinary onion pan retains its21-second guard. The complete walking estimate must be below12 seconds.

The existing unplated Work owns the source, exact food, pot, original stove, optional onion pan, and output. The observer runs before `CompleteWork` and verifies native Cooked before accepting an empty pot plus the same held plain hotdog. Native identities, preparation, recipe index, remaining Work resources, original attachment, finite nonnegative progress, and deadlines remain checked. The operation has a720-frame bound.

With staged release enabled, completed pickup can release a source that differs from the output. Completed native consumption releases the exact empty pot and original stove together. The observer records these releases so a successor pantry job can refill them while the original chef finishes returning the food. The old Work's completion removes only its remaining resources. Source==output retains its station reservation. The captured lifecycle tests include a successor refill and verify that old completion preserves the new pot/home/source ownership.

## Native mechanism probe

`routes/probes/near-ready-pot.json` was executed by the operator using frozen V15, with a fresh seed0 four-player Carnival3-4 restart. It uses only existing take, navigation, throw, chop, native cook wait, combine, and place actions. It deliberately begins waiting earlier than the adaptive11-second gate, so it tests the native held-bun mechanism independently of V16 scheduling.

The closed independent proof is `artifacts/near-ready-pot-a-proof.json`:

- Pot7 remains attached to its original stove19 in every one of825 native state samples. No chef holds the pot.
- P2 performs native client/server chopping on board23 for85 samples; raw bun125 is replaced by prepared bun127, observedOrdinal126.
- P0 holds that exact prepared bun during471 neutral cook samples, GF314–784. Native elapsed time is7.8333324 seconds; pot progress rises from4.18333244 to12.016655.
- The native pot reports Cooked atGF784. AtGF792 a fresh ordinary pickup edge targets native stove19, whose observed attachment is pot7. The pot resets to empty and the same held bun127 becomes the exact native plain hotdog296560.
- The same food reaches ordinary counter32, board23 ends empty, all four chefs end controlled and empty-handed, and the final frame is823. Score, tips, deliveries, deductions, and gameplay-event arrays remain zero throughout.

The checker validates the entire frozen source manifest, executing candidate hash as identified by the operator, loaded frozen classifier hash, observed plugin/instrumentation hashes, full trace/plan/result hashes, one-frame input schema, frame continuity, native clock advance, identities, preparation transitions, target edge, and final state. Fourteen positive/negative checker assertions mutate captured native samples to verify that missing food, changed identities, detached/held pots, missing Cooked evidence, nonneutral wait input, and an absent transfer are rejected.

The trace itself attests the plugin hash and controller version, not the operating-system identity of the controller process. The proof labels the frozen V15 execution choice as operator identified; the checker independently verifies that its loaded food classifier matches that same frozen assembly. The existing instrumented clock policy remains explicit in the manifest. The probe issues no clock-correction command or gameplay mutation command after its initial restart.

Reproduce the file-only proof:

```powershell
dotnet run --project scripts/NearReadyPotProbeCheck/NearReadyPotProbeCheck.csproj -c Release -- artifacts/near-ready-pot-a.jsonl.gz routes/probes/near-ready-pot.json artifacts/near-ready-pot-a-result.json artifacts/near-ready-pot-a-proof.json
```

V16 offline API: `CarnivalPlanner.NearReadyPotSelfTest(at789, at843)`. The current isolated harness is `scripts/NearReadyPotCheck`; its result and exact assembly hash are in `artifacts/near-ready-pot-check/tests.json`. These tests and the native mechanism probe do not establish full-round stability, replay qualification, or a5000-point score.
