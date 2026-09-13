# Pantry batch-wait review

The closed V16/V17 recordings do not establish an additional safe six-to-eight-second UL wait for a second completed FIFO plate under the proposed unchanged native tip protection. This is a screening result, not proof that batching can never help.

`scripts/screen_pantry_batch_wait.py` reuses the existing hashed operational projections and corrected job callbacks. `artifacts/pantry-batch-wait-screen.json` lists every departure. V16 has three single-meal departures whose next FIFO plate completed within eight seconds. Its first case, boardGF1201 and next plateGF1510, is already handled by V17's LR near-interaction wait: V17 collects both meals on that visit. Counting it as a new UL-wait benefit would duplicate an existing improvement.

The remaining cases are:

| V16 board frame | FIFO order | Next plate ready | Delay | Native head remaining | Extra same-tip allowance after10+1s travel |
|---|---:|---:|---:|---:|---:|
|4304|6|4750|7.433s|104.28022s|strictly less than3.52022s|
|12161|18|12604|7.383s|95.31164s|negative5.44836s|

Both heads have native lifetime136s and are currently in the8-tip band, whose lower boundary is89.76s. Waiting for either next plate would cross that band with the existing11s travel/safety allowance. GF12161 already fails that conservative allowance even with zero extra delay. These facts reject the proposed delay; they do not assert that the actual old delivery lost a tip.

Each capture has three distinct active attached clean/unserved plate identities. GF4304 has head plate211, clean207 and future raspberry178. GF12161 has head395, clean408 and future chocolate375. Capacity therefore exists physically, but it does not override the native tip guard or prove every planner lease is available. `scripts/PantryBatchRead` classifies these exact plates using frozen V21 and pins its DLL hash in `artifacts/pantry-batch-wait-native-review.json`.

V17 has no new singleton departure whose next recorded FIFO plate completes within480frames. Several pairs are already collected together; nearby misses take509,529 or560frames, and others take longer. This comparison uses the unchanged old executions, so it cannot predict how extra supplier work would change a future plate's readiness.

At V16GF4304, P3 is finishing the next meal's existing onion work until4474; P0 then assembles it until4750. At12161, P3 already carries the next onion base and finishes12169; P0 clears clean408 from44, fires the cannon and starts its assembly12289–12604. Neither next plate requires the subsequent P2 refill to finish in these recordings. Reordering those center jobs is a different scheduling proposal.

The seven unchanged full native response fixtures are under `artifacts/v16-batch-wait-witnesses`. Their manifest binds exact call-line and response hashes and the complete original compressed trace SHA256 `d3802824e17cf667c954b822c5479c3d36ac30ed45b5699caa601c683be1f5e2`. The extractor verified the closed file before and after reading it. No game input or planner policy changed during this review.

A second bounded pre-service refill is a separate proposal: it may fit the existing240-frame total delay without waiting for the next plate. It requires its own exact completion-boundary admission evidence and must not inherit a claimed gain from this rejected until-two-ready wait.

## Second refill under the existing240-frame clock

The exact `preServiceStockComplete` events also provide no positive second-refill admission in these two recordings. V16 has three completed pre-service jobs; V17 has no admissions or completions, despite14 observed-ready-head events.

| V16 completion | Original ready frame | Elapsed | Remaining240-frame allowance | Rejection |
|---|---:|---:|---:|---|
|1772, bun|1608|164|76|A second pot job needs the full120-frame admission budget.|
|5377, far sausage|5255|122|118|The remaining budget is two frames short; both pots also contain sausage.|
|6354, addressed sausage|6293|61|179|Time and tip fit, but no second unaddressed empty pot exists.|

At6354 pot2 is natively empty on its original stove17 because the just-completed supplier job placed its exact raw sausage276 on handoff45 for center relay into2. That original address remains binding. Pot7 contains Cooked sausage on parking counter33, with original stove19 empty. Treating the empty pot2 as available again would duplicate its issued supply; treating the empty stove19 as another empty pot would ignore the actual parked vessel and its lease. The bun240-frame admission budget cannot fit either.

`scripts/extract_preservice_boundaries.py` reads closed compressed bytes and pins every admission/completion response and exact event line. V16's original gzip hash matches the earlier projection. V17 was read directly from its lossless XZ archive; both archive hash and original raw JSONL hash `d498110443fa014b4c4e747a1737e50d750e7907fee14eae6c7e5bd76f766e8d` match the archival/projection receipts. No removed gzip was recreated. `scripts/review_second_preservice_refill.py` verifies the three rejection facts in `artifacts/second-preservice-refill-review.json`.

These negatives do not justify adding a second-refill policy yet. A later recording could supply a positive boundary with two originally empty, free, on-home pots; a first short refill; enough remaining original240-frame/tip budget; and no address, buffer or rescue lease on the second pot. Ordinary supply, exact head identity and the same non-resetting deadline would still have to govern that bounded experiment.
