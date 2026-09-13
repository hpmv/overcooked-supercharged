# Native cooked-pot offheat proof

`artifacts/pot-offheat-a-proof.json` passes the complete, closed native recording. No game connection is made by the checker.

Pot7 (observed ordinal6) began on stove19 (ordinal18). The native cooked sausage was picked up in that whole pot and parked on ordinary counter32. Across782 contiguous samples, GF887–1668, its native cooking progress stayed exactly13.0499725 and its full food-tree hash remained unchanged. Native client time advanced13.0166668 seconds and the round timer advanced13.016458 seconds; each distinct frame advanced both clocks monotonically. Logical frame count alone is not accepted as elapsed-time proof.

The checker observed chopped bun127 (ordinal126) on board23 before assembly. That same native food entity became the fully prepared, unplated plain hotdog recipe296560 after taking the boiled sausage from the parked pot, and ended on counter37. The same empty pot7 returned to its original stove19 with native cooking progress0. All six authored jobs completed. All four chefs ended controlled and empty-handed, with no delivery, score change or order timeout.

The recording contains one initial restart, one inspect and2017 ordinary single-frame input steps. Four unique player IDs, bounded numeric axes, ordinary pickup/use/dash buttons, frame continuity, native automatic physics, configured clock/input instrumentation, and unchanged plugin identity are checked. Unexpected mutation commands, native protocol failures and missing gameplay-event coverage are rejected.

## Provenance

- Trace: `artifacts/pot-offheat-a.jsonl.gz`, SHA256 `f29122c3720599eceebe29340300d8fbe1ba828f3946d4bc3e01119ed54a9856`.
- Plan: `routes/probes/pot-offheat.json`, SHA256 `4a3808f50a39934011ffbed5a8f854624bf6f1e16068ee1bbc08e1443f530503`.
- Result: `artifacts/pot-offheat-a-result.json`, SHA256 `df1aeac775f8756793940b7be020a2f7781ab3002b85384d963b0380e7a91aac`.
- Frozen V11 controller: `7856cad051fbe0ce0f9fb60b612ec796f1e77452bc31b13b1ea11cc02ff2bc7b`. The operator identified this executing candidate; the trace header records only its version. The independent checker uses that same frozen assembly's native food classifier and verifies all62 frozen source-file hashes plus their aggregate manifest.
- Plugin: `7d5fa9fb67a17cfc974d80e2416620bfa4f6ab2cc213ac8a33b890aa7b6fafbd`, checked against both the local runtime file and every frame's instrumentation manifest.
- The proof also records checker source/project hashes, the controller manifest hash, and the exact unchanged native food-tree hash.

The separate final screenshot is `lab/artifacts/pot-offheat-a.png`; `artifacts/pot-offheat-a-screenshot-proof.json` records image/receipt hashes and its GF2017 final pot/output observations. Its later connection-release audit event is distinct from the final trace response.

## Reproduce the offline checks

```powershell
dotnet run --project scripts/PotOffheatProbeCheck/PotOffheatProbeCheck.csproj -c Release -- artifacts/pot-offheat-a.jsonl.gz routes/probes/pot-offheat.json artifacts/pot-offheat-a-result.json artifacts/pot-offheat-a-proof.json
C:\Python312\python.exe scripts/test_pot_offheat_checker.py
```

The black-box regression suite retains complete initial/final native states and produces compact intermediate projections solely for testing the checker. It requires the unchanged projection to pass, then verifies rejection of cooking drift, raw contents, reused pot/bun identity, missing storage attachment, a forbidden command, extra score, reversed native time and a monotonically advancing dwell shortened below13 native seconds. These altered files are labeled diagnostic fixtures and are not treated as native executions or replay evidence.

This establishes the native offheat/combination/restoration mechanism for the observed pot and recipe. It does not establish a high score, whole-round planner liveness, or the elapsed time needed to rescue every congested pot.
