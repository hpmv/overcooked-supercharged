# Measured central dispatch ordering opportunities

This read-only review uses the bound V15/V16 prefixes through gameplay frame8500 from `docs/V16-PANTRY-CRITICAL-PATH.md`. It screens normal central job admission when both central Work slots are empty, then checks native chef states, adjacent scheduler events, and full-clearance paths using the frozen V16 controller. Heat arbitration already ranks its candidates by measured path and deadline; this recommendation concerns ordinary jobs admitted by the fixed player0-then-player3 loop.

`artifacts/v16-nearest-dispatch-paths.json` contains the actual frozen controller's offline path results. `artifacts/v16-nearest-dispatch-gf494.json`, `...495.json`, `...3254.json`, `...6936.json`, and `...7784.json` are exact responses copied from the original trace. No native calls or simulated state corrections were used.

| Native boundary | Actual chosen job | Chosen chef0 first leg | Idle chef3 first leg |
| --- | --- | ---: | ---: |
| 494 | Raw Flour141, handoff48→far bowl3 | 8.447 | 2.468 |
| 3254 | Prepared bun198, board23→parked pot2→counter38 | 9.500 | 4.798 |
| 6936 | Fire right cannon85 using button77 | 8.035 | 0.541 |
| 7784 | Prepared bun311, board23→buffer40 | 6.555 | 2.459 |

These distances are native world units to a validated interaction approach, with the actual other chefs present as dynamic obstacles. They are not input-frame predictions or claims of equivalent score improvement.

The strongest paired witness begins identically in V15 and V16. At494 both central chefs are idle, empty handed, controlled, and without a heat lease or blocked-heat status. Chef0 at(18.146,-17.044) receives the right-hand Flour relay, although chef3 at(22.926,-15.704) is substantially nearer. One frame later the pantry finishes chopped bun143 on left board23, and chef3 receives its buffer job while chef0 is already committed. From the actual495 positions, the board path is7.062 units for chef3 and2.728 for chef0. The two actual selected first legs sum15.509 units; reversing their recipients yields5.196 using the independently measured paths. That10.313-unit difference is a geometric opportunity. The relay completes624 and the buffer678; the pantry cannot start its next bun until678. No claim is made that a swap preserves those later states or recovers the distance divided by walking speed as exact native time.

At3254 both chefs finish their preceding jobs on the same native boundary: chef0 has just placed the onion-mustard plate at service, while chef3 has just delivered a dirty stack at the left handoff. The fixed ordinary order immediately sends chef0 back across the kitchen for board23. Both parked pots have free owners and the remaining onion leases are passive. The closer chef3 receives no competing job at that boundary.

At6936 the washer finishes boarding cannon85. Chef3 has been idle since its meal callback6874 and stands beside the right fire button. Chef0 instead receives `fire-right`, occupying66 frames until7002. The6960 status confirms chef3 remains idle with no early-onion, pot, fryer, paused-cannon, or heat-blocked obligation. This is a particularly narrow one-target experiment: the cannon/passenger/button ownership, native fire edge, flight observer, and subsequent washer work can remain unchanged while the closer eligible firing chef is selected.

At7784 the pantry completes bun311 and begins the next sausage supply. Chef0 starts the bun-buffer job even though chef3 is idle nearer the board. The job ends7922; the pantry then immediately starts its next bun. This example links the crossing to an observed source-board wait, rather than treating all central idle time as recoverable throughput.

A bounded next policy should select the eligible idle central chef for the **same highest-priority admissible job** using full-clearance path to its first interaction. Preserve recipe/FIFO order, source and destination reservations, passive heat ownership, existing native completion observers, and the global heat arbiter. Reordering chefs solely by coordinates or creating a lower-priority task for the farther chef could change policy in unintended ways. Candidate selection should be read-only until a single winner acquires the original Work/resources, and it must never transfer an active action. The narrowly scoped right-button example can test that machinery before extending to addressed handoffs and bun-buffer jobs.

This report preceded implementation. The subsequently authorized optional three-job preference is described in `docs/NEAREST-CENTRAL-TASK-PREFERENCE.md`; the measured geometry here remains unchanged. No native comparison of that preference has been performed.
