# V14 intercepted sausage and separate native ending

The original adaptive seed-0 bot trial failed at gameplay frame 15520 after 20 deliveries and 2,042 points. Its one neutral cleanup frame ended the trace at 15521, with 11.3190308 seconds remaining and both native round controllers active. This was not a completed adaptive round and did not meet the 5,000-point target.

## Actual terminal transfer

P2 started `supply-Frankfurter` at 15449, owning crate70, pot7 and its original native stove19. The throw resolved source443 at15497 and released at15512. P3 had completed `restore-empty-pot-7` at15407 and remained idle at `(17.08043,-11.7999992)`, facing stove19.

| Frame | Source443 | P3 held item | Pot7 contents |
|---|---|---|---|
|15518|Flying, previous/native thrower105|0|Empty|
|15519|Flying, native flight time0.1|0|Empty|
|15520|Not flying, attached under Player4, previous thrower105|443|Empty|
|15521|Same intercepted source after neutral cleanup|443|Empty|

Source443 has observed ordinal442 and actual native registration sequence1759. The target was pot7, not P3. The planner correctly declined to treat this catch as successful vessel delivery; its generic unassigned-held-item guard then ended the attempt.

Native `ServerPlayerControlsImpl_Default.Update_Catch` runs automatically. `PlayerControls.ScanForCatch` scans a two-unit, 90-degree collider arc. `ServerAttachmentCatcher.CanHandleCatch` additionally requires empty hands, a different thrower, native catcher distance1.8 and incoming velocity angle within120 degrees. `ServerCatchableItem` requires flight time at least0.1 seconds for a chef. This automatic catch need not collide with the chef's physical movement capsule, so a capsule sweep alone is insufficient prevention.

## Bounded source recovery, pending native trial

`CarnivalInterceptedThrow.cs` admits only this class of raw Frankfurter supply: the existing P2 vessel throw must have observed native flight, the captured source registration/ordinal and previous thrower must match, the idle controlled center catcher must hold that source, and the original supplier must still own the exact pot/home leases. A clear native station approach must fit the short placement budget.

The catcher receives one ordinary `place` action on the original stove. It owns only the intercepted source. The supplier keeps the same Work, Active action and vessel/home ownership. A per-frame observer requires either the exact caught source or its native consumption with exactly one Frankfurter added to that pot. The original throw still performs its normal two-sample vessel-arrival test. Supplier completion waits until the catcher's ordinary placement also completes; its original resources cannot be released early. The whole handoff is bounded to120 frames, with no native cancellation, relocation, clock change or arbitrary food adoption.

The immutable receipt identifies the same original action after `CompleteThrow` removes its mutable throw-state table entry. Validation still requires that action's successful completion and the exact native source-consumption/vessel delta. This lifecycle is exercised through actual `runner.Tick` completion in the offline regression.

Scope deliberately excludes prepared-flavor throws, unrelated caught food, busy catchers, changed source incarnations, missing receiving-home ownership and uncertain control/approach state. Those cases retain failure behavior.

Validation:40 captured/mutation/lifecycle assertions,71 existing planner assertions,25 supply-topology assertions,7 receiving-home input assertions, and545 controller core assertions passed against the isolated build in `artifacts/intercepted-pot-check`. No game connection or frozen-bundle replacement was performed by this checker. Native recovery success remains to be established in a subsequent recorded trial.

## Separate authored neutral tail

After the failure, a separate recording applied700 neutral single-frame inputs from15521 to16221. This continuation produced no further deliveries or score changes. Native timer0, server stop and `RunLevelOutro` first appear at16201; client stop first appears at16202. Final score remains2,042 =1,600 base +442 tips −0 deductions, with20 deliveries.

The original trace has15,524 requests: one restart, one inspection, one read-only preview, and15,521 single-frame steps. The tail has702 requests: two inspections and700 neutral single-frame steps. Each trace has contiguous request indices and gameplay frames, with explicit four-player inputs and no protocol state-correction commands. The loaded/plugin behavior still requires its separate instrumentation/process identity evidence; a protocol audit alone does not establish the absence of hidden code changes.

At the join, all native state fields match except observation history: the tail's first inspection contains three extra `input_release` records at15521 with reason `controller-disconnected`. They do not change gameplay frames, food, orders or score. A combined replay on one connection should report this transport-observer history difference explicitly rather than claim byte-identical event history.

The frozen V14 `Verification.ValidateScoreEvidence` independently accepted every native recipe/order/ingredient match and the full before/after score chain. Its initial native270-second/four-player setup and six preview restoration checks also passed. The ending tail satisfies native round completion but remains below the high-score gate. This combined authored movie is an experiment for feedback-free replay; it does not relabel the original adaptive trial as successful.

Reports:

- `artifacts/v14-prefix-structural-audit.json`
- `artifacts/v14-tail-structural-audit.json`
- `artifacts/v14-prefix-native-score-audit.json`
- `artifacts/v14-tail-native-score-audit.json`

Trace SHA256: prefix `47f466ac2de1ee9ddec960a106d08fdb9171133106cb82543f680da4860b5e94`; tail `237a05601d7b8b886e7da48dfd42303441d760a13f503daf96624fd41f171193`. The offline ledger validator loaded frozen V14 SHA256 `0afc09032339fb9a69c006bd303bc3bf45862c433afb3bed07fc62253d086404`.
