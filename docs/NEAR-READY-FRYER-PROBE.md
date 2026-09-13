# Direct native fryer plating probe

The isolated lab completed `routes/probes/near-ready-fryer.json` using frozen V16 (`ad5bf5370678c092dc63b01ccccfd5cc1d8077daff372e1bd1611bdfa389122e`). The route prepares chocolate dough through native chopping, throwing, mixing and frying, then collects it directly onto an initial clean plate. It never moves the fryer basket or changes cooking time.

The original basket5 remained attached to its original stove15 in every observation. P3 held original plate10 before the ten-second native cooking transition, observed Cooked at gameplay frame1710, and consumed that basket's recipe with a fresh pickup edge targeting stove15 at1718. The same plate reached counter49; all four chefs were controlled, empty-handed and neutral at the final frame1742. Score and deliveries remained zero.

The checker observed355 neutral held-plate samples before completion. The first and last such samples span7.41655 native seconds; that span includes travel and is not a claim of355 consecutive waiting frames. The probe begins earlier than the proposed adaptive nine-to-ten-second admission window, so it establishes the input mechanism rather than that policy's scheduling or performance.

Evidence: [native proof](../artifacts/near-ready-fryer-a-proof.json), [closed result](../artifacts/near-ready-fryer-a-result.json), [recorded calls](../artifacts/near-ready-fryer-a.jsonl.gz), and [nine captured-observation checker regressions](../artifacts/near-ready-fryer-checker-tests.txt). The proof pins the executing controller/source bundle, trace, route, checker dependencies, and unchanged plugin/timing telemetry. The regressions reject changed identities, basket transport, missing native Cooked evidence, an incorrect target or pickup edge, changed cooking duration, and invalid recipe preparation.

```powershell
dotnet artifacts/planner-candidate-v16/OvercookedTAS.Controller.dll plan --restart --seed 0 --isolate-recipe-random --file routes/probes/near-ready-fryer.json --out artifacts/near-ready-fryer-new.jsonl.gz --port 17635 --compact > artifacts/near-ready-fryer-new-result.json
python scripts/check_near_ready_fryer.py artifacts/near-ready-fryer-new.jsonl.gz routes/probes/near-ready-fryer.json artifacts/near-ready-fryer-new-result.json artifacts/near-ready-fryer-new-proof.json --controller-bundle artifacts/planner-candidate-v16
```

Only run a controller when no other client owns that isolated game. This is a mechanism checkpoint, not a completed-round score or reproducibility qualification.
