# Bounded service before a safe dough transfer

V15 adds default-off `ServeBeforeSafeHeat` / `--serve-before-safe-heat`. It also requires `ReleaseFiringChefOnLaunch`; otherwise ordinary heat-first dispatch remains unchanged. No frozen V14 artifact was edited.

The first supported case is deliberately narrow: exactly one central chef is idle and empty-handed at a neutral input boundary, the sole mature unowned heat obligation is a complete near-ready dough bowl, and an empty service chef is already loaded in the ready left cannon. The actual FIFO plate must be complete, free and attached to an accessible lower-right counter. Its native order must have time for firing, arrival, collection and delivery. Other unowned processing vessels may only be at most two younger native sausage pots at their original homes. Partial mixers, additional bowls, pans, fryers and multiple mature obligations retain the normal heat priority.

Admission shares the existing near-ready bowl eligibility and exact fryer selection. It creates that original transfer Work first, retaining its bowl, mixer, basket and fryer reservations, then suspends only its untouched queue. A separate Work owns the fire button; the existing cannon-flight lease owns the passenger and cannon. The FIFO plate and its source remain reserved until verified native launch. Native launch restores the same original Work, action queue and heat reservations; the normal native Mixed, pickup, transfer and empty-bowl return checks continue. No already active action is paused or replaced.

All timing uses measured full-clearance walking paths. The firing allowance includes one second for targeting, native button edges and settling; the derived timeout is capped at180 logical frames. The selected bowl must fit button walking plus its subsequent native wait and whole-bowl detachment, with more than one second remaining before the21-second guard. The proven native offmix behavior makes detachment the end of this bowl's mixing hazard. The entire transfer and empty-bowl restoration still count before any other rescue.

Young pots are then budgeted **serially**, in native deadline order, using one cook. Each cost includes the walk to its original home, any remaining native cook wait, a two-second detachment allowance, and a legal walk/place at a distinct currently empty, unreserved ordinary center counter before the next pot. Protected workspace42 is excluded. This avoids treating two independent next-rescue estimates as simultaneous capacity. The storage counters are feasibility witnesses, not persistent new leases or promised later destinations. Geometry, native identities, capacity and deadlines are rechecked throughout firing; after launch, existing global heat arbitration and rescue guards continue. These estimates do not prove the later full schedule succeeds under changing traffic or counter use.

The captured V14GF1872 state has bowl3/home14 at10.0999947 mixing seconds, empty basket5/fryer15, current FIFO plate10 on counter47, and chef2 loaded in cannon84. The measured button walk is8.0979265m. With a180-frame firing limit, the current source estimates:

| Obligation | Cumulative seconds to native detachment | Remaining guard margin | Subsequent parking witness |
|---|---:|---:|---:|
| Bowl3 |8.2362|2.6638|Original bowl transfer and restore |
| Pot7, after full bowl queue |15.0659|4.1175|Counter32 |
| Pot2, after pot7 parking |18.4325|2.3508|Counter33 |

These are conservative admission estimates, not observed alternative-route timings or score gains. A synthetic younger-pot progress change to7.4 shortens the firing limit to152 frames; progress8 rejects the detour. The native baseline did not run this option.

`SafeServiceHeatSelfTest(at1872)` passes64 assertions, including the actual positive admission, default-off arbiter behavior, exact queue/lease resumption, existing active-work preservation, two-pot serial accounting, insufficient storage, changed identities, wrong passengers, incomplete food, deadline/geometry rejection, and a lost-capacity active guard. The same isolated assembly passes44 near-ready dough,42 cannon-flight,25 common-heat,60 direct-onion and19 ordinary-counter onion checks. The launch callback fixture explicitly simulates an already validated launch boundary; native firing/arrival evidence remains the existing cannon flight proof, rather than being inferred from the callback test.

Evidence is in `artifacts/v14-safe-service-gf1872.json` and `artifacts/safe-service-check/{tests,captured-budget,provenance}.json`. The provenance receipt hashes the consumed compressed native prefix and exact source/fixture files. `nativeServiceBeforeSafeHeatAdmitted` and `nativeServiceBeforeSafeHeatResumed` identify production attempts; normal planner pause/resume events account for the untouched heat queue's occupied and paused time. Native performance and full-round qualification remain to be measured separately.
