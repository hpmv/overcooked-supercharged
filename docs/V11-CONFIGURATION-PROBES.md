# V11 configuration probes

These are local native development runs with frozen controller SHA256 `7856cad051fbe0ce0f9fb60b612ec796f1e77452bc31b13b1ea11cc02ff2bc7b` and plugin SHA256 `7D5FA9FB67A17CFC974D80E2416620BFA4F6AB2CC213AC8A33B890AA7B6FAFBD`. Each restarts the native four-player Carnival kitchen with isolated native recipe seed 0. None is a completed 270-second round or a high-score result.

| Trial | Policy difference | Observed result |
| --- | --- | --- |
| `artifacts/native-round-v11` | Lookahead 8, bakery lookahead 12, two raw sausage buffers; all recorded V11 optional policies | Pot7 burned at gameplay frame 2711. Clean failure/release ended at2712 with156 points and2 deliveries. |
| `artifacts/native-prefix-v11-narrow` | Lookahead4, bakery lookahead0, one raw sausage buffer | Eight-delivery checkpoint not reached. Pot7 burned at4212; neutral failure ended4213 with356 points and4 deliveries. |
| `artifacts/native-prefix-v11-serial-sauces` | Same original V11 settings, except cooperative sauces disabled; requested checkpoint2 | Checkpoint reached at2249 with156 points and2 deliveries. This deliberate prefix stop is not round completion. |

The narrow configuration changes three preparation controls together. It establishes that this candidate still has a pot deadline failure; it does not isolate any one parameter's performance effect. Trial summaries retain the intended configuration, the controller's observed initialization settings, actual completion status and native result evidence.

The sauce comparison is narrower. Both original V11 and the cooperative-disabled prefix assigned `assemble-meal-2-Hotdog_Ketchup_Mustard` to chef0 from frame1346 through2155, a13.4833-second job. In this opening sequence, the helper was already busy when cooperative admission was attempted, so the original candidate used its serial fallback. Disabling the optional policy therefore did not shorten this observed job. This comparison of job frames is not an assertion that every raw state or input sample was identical.

The static path audit found that the serial fallback chose the original unplated-food counter37 as its temporary plate surface, despite free counters33 and40 being available at admission. See `artifacts/v11-sauce-admission-gf1346.json`, `artifacts/v11-sauce-admission-ownership.json`, and `artifacts/v11-sauce-nearby-paths.json`. Planned path length is a candidate-selection measurement; native execution is still needed to establish time saved.

Root-level failure/score screenshots and receipts use the matching `native-prefix-v11-narrow` and `native-prefix-v11-serial-sauces` names. The compressed per-frame traces and result/summary files remain in each trial directory. The file observer's status is explicitly partial and cannot substitute for those records.
