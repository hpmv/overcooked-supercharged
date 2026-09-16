# Far-pot interception

The B mechanism probe completed through216 native gameplay frames using ordinary four-player inputs. Chef2 filled near pot7 on its original stove19, then threw a second raw Frankfurter toward far pot2 on stove17. Chef3 caught that exact source and placed it into its intended original pot using a fresh native pickup edge.

| Native observation | Gameplay frame |
| --- | ---: |
| Crate70 creates source125, ordinal124, held by chef2 | 163 |
| Native source flight after ordinary use release | 189 |
| Chef3 catches the same source; native previous thrower105 | 196 |
| Fresh targeted pickup consumes it into far pot2 on home17 | 213 |
| Last authored job completes | 215 |
| Four empty controlled chefs, neutral ending input | 216 |

Catch-to-insertion took17 frames. Both pots remained on their original native homes. The final far pot contained exactly one Frankfurter inside the native cooking-step20068 wrapper. The probe scored zero and tested a transport mechanism; it did not test a high-score route or establish the adaptive policy's throughput.

The trace ran the frozen V18 controller selected by the operator, SHA256 `6f8a87d592dee3626eca01e8f0b76432f2c1841fd5c78fb0f33129d586ed9258`. The trace itself reports only a controller version, so the proof labels binary selection as operator provenance and verifies its frozen source/binary manifest. Native telemetry consistently identifies the deployed plugin `7d5fa9fb67a17cfc974d80e2416620bfa4f6ab2cc213ac8a33b890aa7b6fafbd`.

## Why the planner needed this

The earlier V18 full-route attempt failed at gameplay4138 after six deliveries and600 points. Chef3 had natively caught source215 from chef2's conditional far-pot throw, while the supplier's exact far-pot job still owned its resources. The planner treated the idle chef's held source as unassigned and stopped. The failure projection is `artifacts/v18-failure-projection/projection.json`; its native captures at4130/4137/4138 distinguish flight, catch and failure.

Existing bounded interception recovery required the throw action's captured `nativeSupplyHome` identity evidence. Conditional far-pot actions already owned the near/far pots, both homes, lane board and fallback counter, but omitted that metadata. The subsequent source change attaches the original far-pot/home evidence while retaining the separate conditional-lane permission check. It does not grant ordinary near-vessel throw permission to the far pot.

The recovery retains the original supplier Work and its throw completion barrier. An eligible idle central catcher gets only the exact source lease and an ordinary placement into the captured original home. Both native source consumption and the original throw's exact two-sample vessel delta must complete before the supplier releases its leases. Invalid identity, busy catcher, missing ownership or timeout retains failure behavior. The new adaptive integration remains separate from this frozen V18 generic mechanism probe.

Attempt A stopped while navigating chef3 to a requested point inside the navigation model's conservative station margin. Attempt B requested the ordinary near-pot station approach instead and completed. This is a corrected setup, not a controlled timing comparison between the two attempts.

## Independent checks

`scripts/check_far_pot_interception.py` now binds the complete final trace response to the saved result, requires neutral final requested and observed inputs, and matches all11 completed actions to their authored jobs and dependencies. It checks fresh native frame zero, the270-second four-local-player DLC8 configuration, contiguous unit steps, advancing native clocks, unchanged instrumentation and original pot/home geometry, and the zero score ledger.

Source125 is pinned from its native crate pickup and actual registration event through unchanged raw composition, observed ordinal, registry incarnation, release edge, flight, catch and exact target insertion. Registry observations are registration provenance, not a claim to observe every Unity object's creation. The entire original source remains with chef3 between catch and insertion; final native pot contents persist through the neutral ending response.

The original proof is retained as `artifacts/far-pot-interception-b-proof.json`. The stronger report is `artifacts/far-pot-interception-b-proof-v2.json`, with hashes for the closed trace, route, result, checker, imported helper and mutation tests. Four test groups pass, including26 rejection cases for mismatched result data, nonneutral endings, source/registration changes, missing flight/frame evidence, private request fields, altered routes, wrong targets/configuration and lost final food. All checks are file-only.
