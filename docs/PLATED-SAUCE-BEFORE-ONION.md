# Native plated hotdog: mustard before onion

Probe B proves this preparation order works through ordinary inputs in the installed four-player Carnival of Chaos 3-4: empty plate → chopped bun → boiled sausage → mustard → fried onion → output counter. The same original plate10, observation ordinal9 and native registration6019, remains the meal container. This is a mechanism proof with zero deliveries/score; it does not establish a scheduling gain or a high-score route.

The complete evidence is [the proof](M:/projects/game-test-2/artifacts/plated-hotdog-sauce-before-onion-b-proof.json), [authored route](M:/projects/game-test-2/routes/probes/plated-hotdog-sauce-before-onion-b.json), [closed trace](M:/projects/game-test-2/artifacts/plated-hotdog-sauce-before-onion-b.jsonl.gz), and [completed result](M:/projects/game-test-2/artifacts/plated-hotdog-sauce-before-onion-b-result.json). The proof pins their bytes, the checker/helpers/tests and operator-selected frozen V19 controller/source manifest. The controller DLL is `6a0a218a0d485c3030315a848148ea9170a79b01e7f9e089fa5b844c3fa0ac4b`; native telemetry reports plugin `7d5fa9fb67a17cfc974d80e2416620bfa4f6ab2cc213ac8a33b890aa7b6fafbd` throughout. The trace itself reports controller version rather than its binary hash, so the executing controller association retains that explicit operator-provenance limit.

| Native gameplay frame | Observed transition |
|---:|---|
|30 → 64|P2 picks raw sausage123 from crate70, throws it with native inputs, and original pot7 consumes it.|
|77 → 199 → 353|Raw onion125 becomes chopped onion127 on board56; P0 places the same chopped source into original pan9.|
|244 → 386|Raw bun129 becomes chopped bun131 on board23.|
|444|P0 takes original empty plate10 from counter38.|
|471 → 474|Native placement consumes bun131 and puts plate10 under it on board23; existing `assemble` then recovers that same plate.|
|784 → 792|Sausage becomes native `Cooked`, step20068; the held plate consumes it while pot7 stays on stove19.|
|833|Native dispenser72, switch index0, adds mustard17094 to the held plated plain hotdog.|
|1074 → 1082|Onion becomes native `Cooked`, step20294; the held mustard hotdog consumes it while pan9 stays on stove21.|
|1163 → 1165|The same plate reaches output49; all five jobs/21 actions complete, followed by a recorded neutral end frame.|

Both vessels retain their original native identity, position, home attachment and12-second cooking configuration in every recorded frame; neither is picked up. Pot cooking is first observed at64 and becomes Cooked784, exactly12.0000 client seconds later. Onion insertion is353 with zero progress; native Cooked appears1074 after12.0166672 client seconds. The last observed on-heat progress before each consumption is12.13332. Every cooking increment follows the native client delta. Final food contains exactly bun262914, sausage284626 under Cooked20068, mustard17094, and onion461162 under Cooked20294; the original vessels end empty.

The checker follows actual native registration/removal receipts and observed ordinals for the raw and chopped source incarnations. Chopping creates a new prepared object; it does not preserve the raw object's native ID. Ingredient transfer subsequently consumes those objects. Mustard is a native dispenser addition to the existing composition, not a separately claimed ingredient-object registration. Registration is network registration evidence, not a Unity object-creation hook.

There are1165 consecutive one-frame ordinary-input requests, plus the initial native restart and inspect. Four local players, the270-second native round configuration,60Hz logical/50Hz physics clocks, and zero score/base/tips/deductions remain observed. Native client elapsed time is19.4166666 seconds; the countdown changes19.416549 seconds, with its floating-point difference reported directly. The complete final result equals the last trace response. The checker rejects nine captured mutations covering plate incarnation, ingredient registration, vessel motion, native cooking duration, missing frames, changed mustard ordering, missing fresh input edge, missing action completion, and a mismatched full result. [Test output](M:/projects/game-test-2/artifacts/plated-hotdog-sauce-before-onion-b-checker-tests.txt).

Probe A's failure was an action-contract mismatch. At470 P0 held empty plate10 and board23 held bun131. At471, the native pickup input successfully attached plate10 containing bun262914 to board23 and deactivated bun131. Generic `combine` then failed its retained-held-entity expectation; it did not demonstrate an illegal recipe. A ended473 with the plated bun still on23 and no completed result. B changes that action to existing `assemble`, which already handles native plate-under-food and recovery. A's [route](M:/projects/game-test-2/routes/probes/plated-hotdog-sauce-before-onion.json), [trace](M:/projects/game-test-2/artifacts/plated-hotdog-sauce-before-onion-a.jsonl.gz) and [failure log](M:/projects/game-test-2/artifacts/plated-hotdog-sauce-before-onion-a-stderr.txt) remain intact.

Local native source agrees with the observed result. `ServerPlate`/`Plate.CanPlaceOnPlate` delegates additions on an existing composite to the hidden bun's `IHandleOrderModification`; `ServerPreparationContainer` checks its permitted entries/capacity. The original DLC8 `DLC08_HotdogPrefabLookup.asset` allows one each of boiled sausage, fried onion, mustard and ketchup with no ingredient exclusions. The chopped bun's internal capacity is3 in addition to its base bun, so sausage+mustard+onion fits. Native reverse `ServerPlacementContainer` transfer lets the held plate draw the cooked contents from the standing vessel. These sources are under the local AssetRipper export's `Assets/Scripts/Assembly-CSharp` and original `Assets/downloadablecontent/dlc08/dlc_assets` directories. The live native transition, rather than source interpretation alone, establishes this specific ordering.

Reproduce the read-only check from the workspace:

```powershell
python scripts/check_plated_sauce_before_onion.py artifacts/plated-hotdog-sauce-before-onion-b.jsonl.gz routes/probes/plated-hotdog-sauce-before-onion-b.json artifacts/plated-hotdog-sauce-before-onion-b-result.json artifacts/plated-hotdog-sauce-before-onion-b-proof.json --controller-bundle artifacts/planner-candidate-v19 --stderr artifacts/plated-hotdog-sauce-before-onion-b-stderr.txt
python -m unittest discover -s scripts -p test_plated_sauce_before_onion.py -v
```
