# V14 seed59 partial-mixer failure and bounded recovery

The native seed59 attempt stopped at gameplay frame9929 because original far bowl3 was still attached to original mixer14 with only Flour and native mixing progress21.0165. The final neutral failure response at9930 reports21.0332. This was a controller safety stop, not a claimed completed round or a native overmix event. The score was1212 after12 deliveries.

The recorded sequence explains the stalled chefs:

| Native frame | Observed transition |
| --- | --- |
| 8444–8538 | P1 supplied Flour through addressed counter48 for far bowl3. |
| 8539 | P1 began boarding the wash cannon. |
| 8668 | P0 completed native Flour placement into bowl3; its ordinary mixer clock began. |
| 9294 | P1 returned to the pantry and supplied Flour to the newly empty near bowl6. |
| 9390 | P3 became idle; the earliest heat obligation was Flour-only bowl3 at12.0333s. P1 supplied Egg to near bowl6. |
| 9469 | P0 became idle; both centers were blocked on bowl3 at13.35s. P1 began producing Chocolate. Neither board24 nor counter48 then contained a usable ingredient. |
| 9615–9616 | P1's native chopping replaced raw Chocolate335 with prepared Chocolate337 on board24 and completed its job. |
| 9616–9929 | All four jobs were idle. Chocolate337 remained available, but the heat helper required the recipient already contain both Flour and Egg. |

P3's final idle interval was539 frames (8.983s), P0's460 (7.667s), P2's456 (7.6s), and P1's313 (5.217s). These are recorded job-idle intervals; they are not estimates of recoverable score. The available flavor could reduce a valid partial recipe without completing it, which the ordinary `PrepareDonut` path already supports. The heat helper had imposed a stricter condition and prevented that legal action.

The source change admits an exact missing flavor for any nonempty compatible strict subset of the assigned donut's native ingredient multiset. Its completion verifies the exact previous ingredients plus one flavor, original bowl/home/board identities, unchanged assignment, cleared source and empty chef. It does not declare complete dough while Egg remains missing. The existing raw-flavor chopping prerequisite uses the same compatible-subset check.

An addressed missing Flour or Egg can also become the selected bowl's heat-changing prerequisite. The ordinary take/place job retains the exact bowl, source and item reservations; the callback validates the native one-ingredient delta before removing that exact handoff address. Another bowl's explicitly addressed ingredient is never reassigned.

At an idle upper-pantry boundary, P1 prioritizes the oldest compatible partial bowl with at least9s of observed native mixing and a missing raw Flour/Egg input. This precedes fresh near-bowl supply and wash departure. Production uses the existing ordinary `Supply` path and its measured direct-lane or addressed-handoff guards. An already pending exact supply is not duplicated. Active jobs are never preempted.

The native mixer combines progress when ingredients are added. The recovery observes the resulting progress; it does not assign or predict a synthetic reset. The21-second guard, recipe requirements, native12-second mixing duration, cooking and scoring are unchanged. A missing supplier, occupied handoff, or route with insufficient deadline slack still yields a bounded failure rather than manufacturing a prerequisite.

Read-only evidence is in `artifacts/v14-seed59-partial-mixer.json`, with exact captured responses at9469,9929 and9930, plus `artifacts/v14-seed59-prerequisite-context-gf9616.json`. The projection samples ordinary state every10 frames and records the exact controller event boundaries above. New source regression tests use the exact captures and explicitly synthetic mutation/accepted-transfer observations. A fresh native run of the changed planner is still required.
