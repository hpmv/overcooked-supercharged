# Native intermediate-waypoint A/B circuit

The continued circuit completed in384 frames versus390 for the baseline:6 frames,0.10 seconds, or1.54% faster. This is one short held-plate navigation comparison, not a full planner performance result.

The executable plans differ only in seven `continuousWaypoints` booleans; their descriptive text also differs. Both use the same operator-selected frozen V17 controller (`80c0cb67009341e23da9a1b0ac73d2693d734127ffd2fc936f5011d2d7449476`), native plugin and instrumentation manifest. All seven actions completed, original empty plate10 was picked up from38 and returned to38, the other chefs remained still and empty, and both native score ledgers remained zero.

The continued run recorded two intermediate continuations, at gameplay frames29 and267. Braking events decreased from11 to9. Every final station approach retained three consecutive observed samples with neutral input, negligible movement and cached velocity below0.05; targets remained within the original0.10-unit tolerance. No native impact, dash, throw, respawn, hidden input command or additional plate transfer was observed.

| Continued event | Phase | Error in immediately queued native position | First later physics frame | Error in originally estimated first new-direction step |
| --- | ---: | ---: | ---: | ---: |
|29, toward station72 |4 |0.00000266 units |32 |0.00235525 units |
|267, toward station48 |2 |0.00000134 units |269 |0.00000020 units |

At the first corner, the no-physics Update at31 recomputed direction before physics ran at32. The checker therefore distinguishes the immediately queued old-velocity prediction from the longer-lived new-direction estimate. Recomputing the latter using the last actually observed cached velocity also matches the following native position. The recorded displacement at unrelated frame160 was0.12001652 units,0.00001652 above the nominal0.12-unit walking step; it is disclosed rather than rounded into exact equality. The checker accepts at most0.0001 units above that nominal step and reports every sample exceeding0.12001.

Initial measured entities, chefs and selected station geometry match exactly. Raw initial states differ: global frames586 versus588, client clock9.766666 versus9.8, registration histories/incarnations, lifecycle timestamps and ambient RNG. Both start at gameplay frame0, native timer269.983337 and physics phase5. The result does not assert bit-identical initial states or isolate every possible source of one-frame timing variation.

The independent checker is `scripts/check_waypoint_comparison.py`; final report `artifacts/waypoint-comparison-a-proof-v2.json` pins both closed traces, results, authored plans, selected frozen source tree and checker sources. The earlier proof is retained. Nine recorded-data/negative tests in `scripts/test_waypoint_comparison.py` pass, rejecting changed predictions, changed next positions, native impact, unsettled final targets, correction commands and route-target changes. The native trace itself reports controller version; identifying the exact executing executable still relies on the operator-selected frozen bundle and execution provenance.

This checker validates observed motion and input evidence. It does not independently rebuild every Unity collision surface or prove unrestricted corner continuation safe. The six-phase offline motion fixtures and this native circuit cover different evidence scopes.
