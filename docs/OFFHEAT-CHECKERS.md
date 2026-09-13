# Native offheat proof checkers

The C# pot and fryer checkers accept four positional paths: trace, authored plan, closed result, output proof. Their legacy classifier defaults remain the frozen V11 pot and V10 fryer bundles. Both checkers hash the classifier assembly actually loaded and require it to equal the selected bundle's DLL and manifest; selecting a different bundle without rebuilding is rejected. Frozen source file and source-tree hashes are also verified.

For an explicitly identified V13 execution, build an isolated checker with the matching reference and pass the same bundle at run time:

```powershell
dotnet build scripts/PotOffheatProbeCheck/PotOffheatProbeCheck.csproj -c Release -p:ControllerBundle=M:\projects\game-test-2\artifacts\planner-candidate-v13 -o artifacts/pot-proof-check-v13
dotnet artifacts/pot-proof-check-v13/PotOffheatProbeCheck.dll artifacts/pot-offheat-short-dash-a.jsonl.gz routes/probes/pot-offheat-short-dash.json artifacts/pot-offheat-short-dash-a-result.json artifacts/pot-offheat-short-dash-a-proof.json --controller-bundle artifacts/planner-candidate-v13
```

Use `FryerOffheatProbeCheck`, its matching project directory, and the corresponding fryer paths for that workflow. Controller execution identity remains operator-supplied because these trace headers contain only the controller version. Reports distinguish that assertion from the independently verified loaded-classifier hash and the native plugin hash recorded throughout the trace.

The bowl's native evidence checker is `scripts/check_bowl_offmix.py`; `BowlOffmixProbeCheck` is only a geometry preflight. The Python checker can additionally bind the exact result and an operator-selected controller source/binary bundle:

```powershell
python scripts/check_bowl_offmix.py artifacts/bowl-offmix-short-dash-a.jsonl.gz routes/probes/bowl-offmix-short-dash.json artifacts/bowl-offmix-short-dash-a-proof.json --result artifacts/bowl-offmix-short-dash-a-result.json --controller-bundle artifacts/planner-candidate-v13
```

This checker uses its own hashed food-schema checks, not the controller classifier. Its report verifies the full native Mixed composition and progress remain unchanged off the mixer for13 seconds, ordinary request types, whole-bowl pickup, transfer to the original fryer, empty-bowl restoration, and final native Cooked chocolate. It reports the observed plugin/instrumentation hashes and validates the selected bundle's frozen source tree.

All three reports separate dash-button requests from actual native dash frames while holding the relevant vessel. A successful probe with zero held-vessel dash frames proves the workflow and its observed approach dashes; it does not prove carrying a vessel during dash.

Regression evidence: all10 pot black-box cases pass with the original default, the original fryer native trace passes with its default, both old-classifier/V13-label mismatch checks reject, and the bowl's7 fixture tests pass. Cannon production checking is separate: `scripts/audit_cannon_flights.py --trace TRACE --out PROOF --require-complete` requires a closed trace with completed arrival obligations and no unresolved passengers;8 checker tests cover false receipts, identity changes, non-neutral inputs and incomplete arrival evidence.
