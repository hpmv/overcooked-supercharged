# V15 terminal supplier dependency

This is a file-only diagnosis of `artifacts/native-round-v15/trial001.jsonl.gz`, using the controller agent's event projection `artifacts/v15-partial-mixer.json` and native captures. No game calls, source edits, state corrections or relaxed native deadlines were used in this review.

The adaptive trial stopped at gameplay frame12672, with17 deliveries and1800 points, because the far bowl3 exceeded the controller's21-second safety deadline. Cleanup advanced to12673. This is a safety failure before a completed round; the reported deadline is not evidence that native food had already become ruined.

## Recorded sequence

| Native frames | Observed work or state |
| --- | --- |
| 11071–11220 | P1 returns from LL to the upper-right bakery through the ordinary portal. |
| 11220–11300 | P1 puts flour on the addressed far-bowl handoff48. |
| 11300–11412 | P0 transfers that flour into far bowl3. |
| 11300–11395 | P1 supplies flour directly into near bowl6. |
| 11395–11474 | P1 supplies egg into near bowl6. |
| 11474–11688 | P1 chops and throws Chocolate into near bowl6, assigned to later meal25. |
| 11688 | P1 starts boarding the washing cannon. Far bowl3 still contains only flour. The adjacent sampled native frame11690 has progress4.65000534. |
| 11869–11927 | After native flight/arrival, P1 receives a dirty stack into sink75. |
| 11927–12116 | P1 washes one native plate. |
| 12008 | P0 becomes idle and is blocked by the oldest partial-bowl obligation, with no local missing ingredient to insert. |
| 12013 | P3 finishes head meal18: plate361 is ready on the LR output. Both center chefs are now blocked by bowl3. P2 starts boarding the service cannon. |
| 12066 | P2's boarding job completes; its native controls remain disabled in cannon84, awaiting a firing chef. |
| 12116–12167 | P1 takes the newly clean plate and places plate408 on clean handoff44. |
| 12167–12209 | P1 receives the next dirty stack. |
| 12209–12398 | P1 washes the second plate. |
| 12398 | All planner worker slots are empty. Far bowl3 is flour-only at16.44992 seconds; near bowl6 has complete Chocolate dough at13.9957933 seconds. P1 has empty hands, clean plate408 remains on44, and an additional clean stack410 is on output76. Sink75 and dirty handoff46 are empty. |
| 12672 | The unchanged partial bowl reaches the21-second controller guard. |

The first native blocker is absent supplier work, not a navigation error. Logged full-clearance center approaches to bowl3 succeed at12008 and12013, but no legal ingredient action is available there. The neighboring full bowl6 has a later deadline and is also prevented from receiving safety work while the earlier dependency remains unresolved.

## Why the wash return cannot proceed

P1's normal clean-output job requires44 to be empty. It therefore cannot take the remaining clean stack from76. The normal return-to-bakery predicate explicitly requires the drying output to be empty. The existing urgent FIFO return is also inapplicable: the native head is a Mustard hotdog already prepared as plate361, whereas that override only supplies an unplated FIFO donut. It independently rejects nonempty drying output.

Both center chefs would normally clear44, but common heat arbitration blocks elective clean-plate relocation until the older partial bowl has a feasible safety action. The supplier needed to make that action possible is waiting for those same centers to clear44. P2 is not an independently available helper despite its empty worker slot: native cannon84 is in `Load`, holding its chef entity105 with controls disabled.

The V16 washer-side plating option intentionally does not resolve this cycle. It rejects immediate clean-output work and existing heat obligations, and this ready head already has its plate. Changing that unrelated transaction would obscure the actual supplier dependency.

## Bounded correction supported by this evidence

The strongest preventive boundary is11688, before P1 leaves the bakery. Its previous prepared-flavor job has completed, its hands are empty, and the exact older far bowl is already processing a strict incomplete recipe. Keeping P1 for the missing unique egg supply preserves roughly16 seconds before the existing guard. The previous urgency threshold of9 seconds excludes this observed4.65-second bowl. A dedicated supplier prerequisite should consider an already processing incomplete bowl before admitting a washing departure, without replacing any active job or duplicating an existing addressed supply.

A secondary return from LL may leave ordinary clean plates safely on44/76 when a processing bowl requires a unique supplier ingredient. Such a return needs explicit native bowl/recipe/home identity, proof that the ingredient is not already locally available or addressed, and a complete portal/supply/center-insertion budget. It is a heat dependency, so it cannot be limited to the FIFO meal. Existing in-progress washing work remains untouched.

Waiting until the terminal idle frame12398 is risky: only about4.55 seconds remain before21. The earlier observed portal trip alone took149 frames, or2.48 seconds, before ingredient pickup, handoff and center insertion. That prior trip is contextual evidence, not a guaranteed budget from a different starting pose. At12120 the bowl was only11.816658 seconds, and at12170 it was12.6499786; a bounded return decision before the second washing job has materially more slack.

This review recommends preserving the existing deadline and fixing the missing producer admission. It does not establish that a particular new recovery route succeeds, or that the full-round score target has been achieved. The controller agent owns the coordinated source fix and regression tests.
