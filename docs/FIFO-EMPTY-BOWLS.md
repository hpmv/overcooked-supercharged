# Empty-bowl FIFO assignment

`--fifo-empty-bowls` (`FifoEmptyBowls`, default `false`) optionally moves the earliest unissued donut assignment into the original near bowl. It changes only the planner's two bowl-to-order entries and their corresponding flavor entries. Native ingredients, vessels, recipes, RNG, inputs, clocks and scores are unchanged by the transaction. The existing near-lane supply actions run afterward.

The closed V19 round scored **2396 with 22 deliveries**, with no deductions. Its late batch exposes a concrete assignment inversion. At GF13027, `urgentFifoBakeryReturn` identifies Raspberry order21/index20 in far bowl3 as the missing FIFO batch. Both original bowls are still empty at GF13165 when P1 finishes returning. The ordinary near-first supply loop nevertheless starts the later Chocolate order25/index24 in near bowl6. Flavors are not hardcoded to a side: old assignments remain sticky, and the near bowl takes the next unclaimed recipe after its previous batch finishes.

| Native observation | Frame |
|---|---:|
| Previous Chocolate dough transfer finishes; near bowl6 restored empty | 11738 |
| P1 completes a clean-plate return, idle in lower-left with both bowls empty | 12355 |
| Urgent FIFO return explicitly requests far bowl3 / Raspberry index20 | 13027 |
| Return completes; both bowls empty, P1 idle before new supply dispatch | 13165 |
| Future Chocolate near6 receives Flour / Egg / prepared Chocolate | 13247 / 13328 / 13521 |
| Current Raspberry far3 receives Flour / Egg / prepared Raspberry | 13699 / 13833 / 14056 |
| Future Chocolate enters basket8 / becomes Cooked | 14209 / 14809 |
| Current Raspberry enters basket5 / becomes Cooked | 14705 / 15305 |
| Current Raspberry delivered | 15569 |

The later batch becomes Cooked **496 logical frames** before the current batch. That is an observed ordering gap, not a predicted speedup from swapping assignments. The original direct near supply and addressed far supply have different travel and receiver dependencies. Far bowl3 is observed continuously empty from the audit's first sample GF7500 through GF13698. Ordinary assignment has no original trace event: the precise near assignment timestamp is inferred from source behavior and later explicit supply records, and is labeled as reconstructed metadata in the tests.

Admission requires both original bowls at their captured homes with unchanged ordinals, active native identities, empty `Unmixed` food trees and exactly zero mixing progress. P1 must be idle, empty-handed, controlled and neutral in either upper-right or lower-left. The two indices must be current ordinary-window donut assignments with the correct flavors, with the earlier far assignment also the earliest pending donut not already committed to a basket. Same-flavor pairs and already ordered pairs do nothing.

Any related current or suspended Work, original/remaining resource ownership, queued or active target reference, bowl/home/board/pass reservation, addressed supply, loose kit ingredient, active predictive visit, parked bakery lease, near-ready dough transfer or prepared-flavor transfer refuses the swap. The original near/far lane identities are never inferred by sorting moved bowls. The shared chopping board and addressed pass must also be empty and unreserved. This intentionally does not rebalance partly mixed bowls, issued kits, active predictive visits or speculative parked batches.

The exact GF11738 snapshot is a negative fixture because P1 is still washing. GF12355 and GF13165 are positive idle fixtures; GF13247 closes the window because Flour has entered near6. **66 focused assertions and 229 existing bakery/planner assertions pass** in [the offline receipt](../artifacts/fifo-empty-bowls-offline-check.json). Tests verify unchanged full native observations, unchanged unrelated Work/leases, stable repeated assignment, the ordinary next Flour throw to near6, and synthetic refusal mutations. [The audit manifest](../artifacts/v19-late-bakery-fifo-proof.json) pins the closed native source, projections, fixtures and source files. Native execution of this new option remains untested at this writeup; it supplies no new score or performance qualification.
