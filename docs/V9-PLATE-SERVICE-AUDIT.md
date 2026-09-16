# V9 plate circulation and bounded service waiting

This report describes a failed native prefix, not a completed round or a score qualification. V9 reached 14 deliveries and 1484 points at gameplay frame 9038 (150.633 seconds). Its final failure was the separately corrected idle-chef obstruction during a dirty-stack relay.

The compact read-only projection is `artifacts/v9-plate-service-audit.json`; job totals also use `artifacts/v9-sausage-buffer-analysis.json`. Both refer to the closed `artifacts/native-round-v9/trial001.jsonl.gz`, SHA256 `94cbe0783192bed237e43316c357de705dbcbbed1d2d4f26d3c9dc481da3e579`. Native station/plate observations and planner decisions are separate fields. Source events are observations of jobs, not native action completion proof by themselves.

## Actual completed job costs

| Work | Completed jobs | Chef-seconds | Mean seconds |
|---|---:|---:|---:|
| P1 native wash | 11 | 34.767 | 3.161 |
| P1 take one clean output and hand it to center | 10 | 8.850 | 0.885 |
| P1 take dirty handoff and load sink | 9 | 6.333 | 0.704 |
| Central clean handoff relay | 9 | 16.517 | 1.835 |
| Central dirty-stack relay | 9 | 16.833 | 1.870 |
| P2 service portal return | 9 | 16.600 | 1.844 |
| P2 boarding with a meal | 6 | 4.850 | 0.808 |
| P2 boarding empty for service | 3 | 2.200 | 0.733 |

The native single-interactor wash duration is three seconds. Eleven completed wash jobs therefore include only about 1.767 seconds of approach/input/completion overhead above 33 seconds of required washing. Eliminating that overhead is a smaller opportunity than unnecessary service travel or central relay work. These totals exclude unfinished jobs at the failed endpoint and do not equate every unassigned interval with usable chef time: native cannon travel also leaves a chef unassigned.

P1's completed jobs occupy 69.867 seconds; the unfinished final clean handoff adds 19 frames (0.317 seconds), leaving 80.45 seconds without an assigned P1 job in this prefix. P2's completed jobs occupy 106.50 seconds; the final unfinished onion supply adds 40 frames (0.667 seconds), leaving 43.467 seconds without an assigned P2 job. Native control/region conditions must be checked before using that apparent slack.

## Two service returns performed no pantry work

The nine service waves delivered `2,2,1,1,2,1,1,3,1` dishes.

* At GF4883 P2 returned after the eighth delivery. P0 already held the complete ninth Plain hotdog on native plate226, with only final placement to counter52 underway. That placement completed at4952, 69 frames after departure. P2 reached the upper pantry at4990 and immediately boarded empty, doing no supply job. The next collection started at5121, 169 frames after the plate became available.
* At GF8425 P2 returned after a three-dish wave. Plate290 still lacked ketchup at that exact frame, so an exact-ready predicate cannot justify waiting there. Ketchup appeared and the cooperative job entered its final placement phase at8428. The meal was staged at8515; P2 returned to the upper pantry at8525 and immediately boarded empty. Collection began at8696, 181 frames after staging.

These are avoidable travel opportunities, not measured time savings of the new scheduler. Waiting changes subsequent positions and timings and requires a native candidate trial.

`--wait-ready-head` enables `WaitForImminentHead` (default false). Before an otherwise required service return, including the batch cap, it may wait only when a center chef already holds the exact fully prepared native FIFO plate and has exclusively its final `place` action left. Its existing plate/output leases and same Work must remain intact. The output must be an existing shared center/service counter with a current full-clearance route. Walking at the observed native surface speed plus a 12-frame transfer allowance must fit the remaining budget.

The deadline is 120 native frames, created once for that head in the visit. It never restarts. Identity, recipe, owner, output, lease or route loss cancels waiting and falls through to the ordinary portal return. The exact native plate attached to its intended output is recognized during the brief action-callback completion interval; it does not create another deadline or bypass the normal ready-meal registration and collection. No central action or input is interrupted. The service chef emits ordinary neutral input while waiting. Events `imminentHeadWaitStarted`, `imminentHeadWaitRejected`, and `imminentHeadWaitEnded` preserve reasons and native identities.

The actual GF4883 fixture passes the full path and recipe admission. GF8425 is rejected; GF8428 passes if the chef is still there. The implementation intentionally does not broaden to a projected final condiment application. Thirty-two targeted checks cover those actual frames, fixed expiry, native attachment completion, normal collection, default behavior, and identity/ownership/incomplete-food rejections.

## Washer return dependency and native stack rules

The old washer return required two free clean plates. Future plating can legally reduce that inventory to one plate deliberately retained for the FIFO head. If the head is a donut missing a supplier ingredient, no further dish can be served; without a delivery there may be no more dirty plates to wash. Requiring a second clean plate before supplying the head closes this dependency cycle. This is a source-level counterexample verified with explicitly synthetic inventory on the actual native scene; V9 did not reach that deadlock.

The mandatory escape applies only to the FIFO donut with an assigned bowl and a specifically missing supplier ingredient. It requires one actual active, free clean plate, no complete matching dough already mixing/parked/frying, and no addressed or loose copy of the missing supply. Immediate sink contents, drying output and an occupied dirty handoff keep their existing priority. A remotely returned or carried dirty stack may be intentionally deferred for the critical FIFO ingredient; its count is logged, not described as absent. Normal washer-return behavior remains unchanged outside this case.

V9 provides a related priority observation: P1 departed for bakery at5983 while P3 was still relaying a dirty stack (5878–6029). The original `DirtyCount` does not include carried stacks. P1 next reached washing work at6680. The escape explicitly distinguishes immediate work from carried/remote work and logs that decision.

Native `ServerCleanPlateStack.HandlePickup` calls `RemoveFromStack` once and carries that one plate. It does not carry the complete clean output stack. `ServerWashingStation` can continue producing clean plates into its native drying stack while the handoff is occupied; the existing loop already permits this. Dirty-stack pickup and sink loading can move the whole dirty stack. A whole-clean-stack courier is therefore not an available input optimization. These facts come from the installed game's decompiled classes, not a modified implementation.

## Native ruined-food stop

V10 later exposed a real burned Raspberry basket: basket8 is Cooked at GF2334 and Burnt at GF2935. The mandatory main-loop check now terminates on active native composition classified Burnt or Overmixed before further meal scheduling. Cook warnings, absent composition, inactive or copied Rigidbody entities, empty compositions, and detached objects whose observed physical colliders are all disabled do not trigger this failure. An attached or carried vessel remains live even if its carry collider is disabled. The actual burned/cooked snapshots and synthetic boundary cases pass 25 FIFO/ruin checks together.

This stop preserves the diagnostic and prevents hours of authoring beyond already ruined food; it does not rescue the food or claim a completed score. Native off-heat fryer rescue is a separate controller change and native mechanism probe.
