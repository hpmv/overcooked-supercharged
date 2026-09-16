# Recorded V15 throughput review

The closed V15 trial is a failed partial native run:17 deliveries,1800 score, last delivery at gameplay frame11254, failure12672 and cleanup12673. It is not a270-second completed TAS. `artifacts/v15-throughput-analysis.json` reads every frame of the closed745,961,808-byte trace (SHA-256`42c1505d628cd7dea8d49d6a4f7aa93c827243d004c0881e3065696e06613e38`), with no analyzer ordering anomalies. Durations below are chef-seconds; simultaneous jobs cannot be summed as elapsed round time.

| Observed work | Native V15 cost | Interpretation |
| --- | ---: | --- |
| Seventeen P1 washes |53.50s |50.98s is native washing-stage time, essentially the required3 seconds per plate. Shortening the native delay is not an optimization. |
| Fourteen central dirty-stack relays |23.43s |Another10.17s was P1 receiving those stacks. Batching might reduce trips, but delaying a needed plate requires a separate capacity/deadline proof. |
| Fourteen central clean-pass evacuations |23.68s |Direct clean-pass assembly is being implemented separately; this review does not duplicate it. |
| Seventeen P2 bun supply jobs |48.50s |Each includes native chopping. Eight additional central prepared-bun buffering jobs cost13.20s; the measured counter throw would move comparable work onto the already busy supplier rather than prove a net saving. |
| Three fryer parking and restoration cycles |8.18s |The earliest9.700-second case already had an allocatable plate and clear route. Direct wait/plating removes an actual needless whole-basket trip; admission remains conditional. |
| Eleven P2 service visits |17 deliveries |Wave sizes were1,1,2,1,3,1,3,1,1,2,1. Returning to pantry cost19.20s of jobs, and P2 spent50.95s in transit or with controls disabled across the run. |

The fryer case is the clearest bounded next action outside the V16 pot/washer/departure fixes. At frame2409 the basket was rescued to a counter in118 frames and later restored in46 frames, though the exact clean plate170 and output49 were available. The measured direct-route budget fits the unchanged heat deadline. Two later fryer rescues also started before native Cooked, but plate eligibility is not proven for every case;8.18s is observed redundant-work exposure, not a promised time saving.

The larger remaining limitation is travel and synchronized service readiness. Central take/place navigation alone consumed188.78 chef-seconds, before combine and cannon approaches. P1 was idle90.82s and P2 idle75.45s, but9.02s and36.85s respectively were observed disabled-control intervals, and the final dependency stall inflates those totals. Idle time does not imply those chefs could reach the blocked station. The recorded production needs materially more than isolated multi-second savings to reach45–46 meals; the end-to-end score must be measured after the liveness and circulation fixes.

Four near-ready dough jobs also held a central chef through roughly2.13–3.00 seconds of native mixing. A future admission policy could defer the held-bowl wait until closer to native completion while admitting a bounded useful intervening job, but the common deadline blocker currently makes such a threshold change insufficient by itself. That is a scheduling hypothesis, not an implemented or native-validated optimization.
