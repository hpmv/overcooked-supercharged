# Native stationary transfer A/B

Both V22 runs passed the independent preparation-order and transfer-input checker. The shortcut run reached its final neutral endpoint in **1,123 frames versus 1,165**, a measured difference of **42 gameplay frames (0.700 seconds)** for this circuit. This is one native mechanism comparison, with no deliveries or score; it is not a full-round throughput or replay qualification.

The routes differ only in thirteen `stationaryTargetTransfer: true` action fields. Both completed the same 21 actions and five jobs using the original plate, the exact raw/chopped ingredient registration chain, and the original unmoved pot/pan with their native twelve-second cooking rules. Mustard was applied before cooked onion, and both results contain bun, boiled sausage, mustard, and fried onion on the original plate at the original output. The final full recorded response equals each separate result file, and the last requested and observed inputs are neutral.

| Observation | Baseline | Shortcut |
|---|---:|---:|
| Final plate on output | 1,163 | 1,121 |
| Final neutral endpoint | 1,165 | 1,123 |
| Native client elapsed seconds | 19.416667 | 18.716666 |
| Pot insertion / native Cooked | 64 / 784 | 64 / 784 |
| Pan insertion / native Cooked | 353 / 1,074 | 323 / 1,044 |

The shortcut emitted eleven neutral confirmations and eleven subsequent ordinary pickup edges. The checker binds every confirmation to a distinct neutral native request/response, then checks unchanged stationary motion, pose, target, source/held registration identities and food material before each edge. Every edge has its following fresh requested/observed native pickup receipt. The initial sausage pickup and the center chef's chopped-onion pickup used the ordinary path. There were no logged rejected confirmations; initially ineligible observations are not individually logged, so their count is unknown.

Local action timing differences are not added together. For example, earlier bun/plate preparation leaves the chef waiting longer for the same pot's native Cooked frame 784. Some later walks are one frame longer in the shortcut run. The 42-frame result above comes directly from the complete endpoint, including those effects and concurrent work.

Raw initial responses differ. Absolute logical frames are 575 and 599, fixed frames are 480 and 500, and client clocks are 9.583333 and 9.983334 seconds, with slightly different float deltas. Native registration/Unity identities and transport metadata also differ. Both initial gameplay frames are zero, initial countdowns are 269.983337, physics phases are five, and the four chef positions, forward vectors and zero velocities match exactly. The proof retains the raw differences and validates each incarnation within its own run; it does not claim identical initial native state.

The operator selected the same frozen `artifacts/planner-candidate-v22` bundle for both executions. Its manifest/source files verify, with DLL SHA256 `a2501df60129ae4ca47d3bc023bf5aa3b50a7bf213fb809dee786f23d9d280a9` and source-tree SHA256 `cfbf4feb01e5d5f02deb14620306a720081606b5c27ec5e7664b475ae6ae7e5f`. This is retained execution provenance; the native trace does not cryptographically attest the controller binary.

Evidence: `artifacts/stationary-transfer-ab-proof.json` (SHA256 `62616e0ae5b66ef366315a215fd434fced7934995e09e18fd1d34ce867f8ee10`), the two `stationary-transfer-{baseline,shortcut}-a` traces/results/stderr files, and the corresponding probe routes. The proof pins all input files and checker helpers. `scripts/test_stationary_transfer_comparison.py` passes five test groups covering captured confirmation, motion/identity/target/material mutations, missing native input receipts, exact route differences, and retention of a failed attempt's full neutral diagnostic. Synthetic checker test events are explicitly separated from the native evidence.

The optional feature remains disabled unless a route action requests it. Moving transfers and generic planner enablement were not tested by this A/B.
