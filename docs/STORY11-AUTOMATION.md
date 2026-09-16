# Story 1-1 automation scope

The tools use the live four-player `s_sushi_1_1` scene and its actual registered
IDs, spawn lists, food definitions and orders. They preserve the native 150-second
duration. The only declared recipe-session change is the user's requested removal
of free preparation: the level-session module sets `recipesBeforeTimerStarts`
from one to zero before native campaign mode begins. Both begin receipts and the
live unsuppressed timer are required. Python never sets that field or the clock.

`scripts/framework_story11_search.py` makes one first-delivery attempt by default.
It fetches the actual ordered fish/prawn onto a board while a second chef collects
a clean plate; observes native chopped-food replacement; uses a prepared native
interaction target followed by an ordinary primary edge; recovers the same plate
if native assembly places it under the food; and requires the expected order's
removal, consumed food identity and exact native base-score/delivery increment.
Native execution remains necessary to validate this pipeline's navigation and
interaction timing. Offline tests do not imply an achieved dish.

The first native preparation completed in 59 frames: exact raw fish51 on board31
and plate2 held by chef44. Its native composition is **null**: raw `SushiFish` is
a `ServerWorkableItem`, not an assembled ingredient container. The corrected
proof pins the actual crate spawn name, object name, path and eight-stage native
workable fields. It never fabricates an `UncookedFish` composition. Prepared food
must be the actual replacement child of that raw path, with its observed native
ingredient definition. The original failed assertion is retained under
`artifacts/framework-migration/story11-first-delivery-b`.

An already completed native preparation can be continued with
`--resume-staged <retained-cases.json> --candidate 0 --warmup 0`. The runner checks
the current exact native attachments, workable identity, active order and unchanged
ledger, hashes the retained case, and records the assertion reinterpretation.
It does not repeat the completed ingredient or plate transfers.

```
python scripts/framework_story11_search.py --out <new-directory> --trace <host-exchange.jsonl> --warmup 0
```

Use `--warmup 30` for a just-loaded baseline. The timer policy is inspected while
inputs are fenced, before arming. Every RPC receipt is appended once to compact
JSONL; compatibility JSON is streamed once at shutdown. Exact emitted four-pad
rows are saved before checking the native outcome, including failed attempts.

Optional `--search --candidates 4` compares the two closest ingredient suppliers,
each walking or dashing, with a parallel plate pickup. It requires the native
scripted-order checkpoint adapter. Each rollback verifies its new native restore
receipt and clears all action definitions only at the restored fixed baseline.
The selected exact raw inputs must reproduce the native preparation effect,
logical spawn path, food, order and clock state before committing to delivery.
The measured objective is preparation duration, not full-delivery optimality.
Dynamic native identities and full endpoint differences remain separate evidence.

The physical tolerance profile in `framework_story11_compare.py` is limited to
named position/velocity components (0.00002 native units), quaternion components
(0.000002), center of mass and inertia tensor (0.000002). It does not normalize
quaternion signs, schema, flags, unknown fields or logical state. All differences
and acceptance decisions are retained. Food, attachment identities, orders,
ledger, progress and native private clocks remain exact; no comparison writes
anything back into the game. This is a search acceptance rule, not exact PhysX
replay qualification.

`scripts/framework_story11_sequence.py` is a separate, bounded continuation for
one to three additional ordered deliveries, using the original fixed clean
plates. It never rewinds. Each delivery additionally decodes the native
KitchenFlowMessage and PlateStationMessage from the original trace and requires
the exact order, station, plate, same-frame outcomes and live resulting ledger.

```
python scripts/framework_story11_sequence.py --out <new-directory> --trace <host-exchange.jsonl> --deliveries 3 --warmup 0
```

Installed native `Assembly-CSharp.dll` SHA256:
`9bb6a3791331201d32ca89c3509f019a9780309da7110002f04020e8491e1908`.
Its `ServerKitchenFlowControllerBase.OnSuccessfulDelivery` IL adds the unchanged
meal base value, multiplies the calculated tip by the **previous** multiplier
(at least one), then increments a maintained combo and caps the new multiplier
at four. `TeamMonitor.TeamScoreStats` serializes base, tips, multiplier, combo,
deductions, combo-maintained and deliveries. The decoder preserves that order.
Native `ServerPlateStation.DeliverCurrentPlate` emits its success flag after
calling the kitchen even when the kitchen rejects a recipe; that flag alone is
therefore insufficient evidence. The sequence checker requires both messages.

The full-round option is intentionally unavailable until returned plate handling
has an actual native witness. Native `ServerPlateReturnStation.ReturnPlate`
creates/fills a stack; `ServerCleanPlateStack.HandlePickup` removes its current top
and clears referral state. An arbitrary clean plate inside that stack is not
equivalent to the direct attached plate supported by the existing typed pickup.
The bounded sequence reports this precise limitation instead of inferring reuse.

The runner now uses the loaded `registry-observer` module while explicitly
input-fenced. It refreshes the actual raw workable's ordered next-prefab metadata
before chopping. At valid pauses it retains each dynamic proxy's native body
instance, exact owner path, scene refresh, ready frame and connection epoch.
After native consumption, a missing proxy can receive an observation-only
retirement only with the module's exact current registry/body absence proof.
This does **not** assert that a historical native removal callback was captured.
Native physics, food, round, and private clocks must remain exactly unchanged
through publication. Original fixed plate proxies are excluded. This requires
the proxy-retirement-aware v12c host and RegistryObserver r2; old host failure
artifacts remain intact.

`--resume-staged <cases.json> --candidate 0 --warmup 0` accepts completed typed
staging or a completed exact raw replay with the same independently verified
native preparation goal. The fresh search writes `selected-replay-cases.json`
for this purpose. `--resume-observed` additionally recognizes a chopped child,
provided the original append-only journal proves its exact earlier raw source
in the same scene/connection epoch. Resume batches include their actual starting
frame in every action ID. Before assembly, an occupied board front is cleared
through an ordinary path action to a free adjacent board's observed front area;
the plate carrier waits for that action. No position or native food is written.

Current offline validation: 29 test methods, including the captured native map
and timer-policy receipts, source/plate inverse-link mutations, chopping and
consumption lifecycle, explicit physical-tolerance rejection cases, native event
codec and score/combo mutations, the fenced timer-read ordering regression and
the actual null-composition workable/resume fixture with identity mutations,
captured raw-to-chopped proxy absence, actual module publication receipts,
epoch/body/owner failures, pause/rearm ordering and occupied assembly clearance.

```
python -m unittest discover -s scripts -p "test_framework_story11*.py"
```
