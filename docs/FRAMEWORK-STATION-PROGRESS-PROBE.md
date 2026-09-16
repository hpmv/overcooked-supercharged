# Native station progress fixture

The first fixture loads one native raw Frankfurter into the original near pot,
then records **60 neutral input frames plus the raw runner's two release
frames**. Rewind and replay begin after ingredient consumption, when the live
scene again contains only its fixed entities. This tests nonempty food and
ongoing station progress without requiring reconstruction of a live ingredient.
It is not yet executed and establishes no preparation achievement, optimized
winner, or full-round qualification.

`scripts/framework_station_progress_fixture.py` is file-only. It opens no port
and sends no input. Each invocation reads a fresh, jointly sampled paused full
inspection and native food receipt, and prepares **one** operator request.
Do not concatenate phases or use the historical S example as a current-session
identity receipt. Re-run resolution against the actual test session.

The captured S initial scene resolves these roles:

| Role | Observed ID | Required native evidence |
|---|---:|---|
| Upper-left supplier | 105 | Unique empty-handed chef on UL |
| Frankfurter crate | 70 | Sausage class, PickupItemSpawner, actual SpawnNames Frankfurter, original/current (14.4,-10.8) |
| Shared left handoff | 45 | Empty native AttachStation at (15.6,-13.2) |
| Central relay chef | 103 | Nearest of the two observed central chefs |
| Original near pot/home | 7 / 19 | Both attachment directions, ServerCookableContainer, CookingStation, original home (16.8,-10.8) |

Ingredient **284626** is Frankfurter; **20068** is the pot's cooking-step ID.
The actual native food receipt reports cookingTime **12**, rather than deriving
it from either ID. Both pots and all other food start empty.

1. `initial`: supplier105 walks to (14.4,-12), settles six frames, uses typed
   pickup with an exact spawn claim, walks to (14.4,-13.2), settles six frames,
   and uses typed attachment placement on45. Whole graph bound360 frames;
   each movement/transfer node180, each wait30. No dash, cannon, throw, or other
   chef input is requested. Actual route feasibility is still a native test.
2. `staged`: require completed graph **and** the single exact raw sausage from
   crate70 on45, including source→45 and45→source. Pin its observed ID/path/name.
   Central103 approaches (16.8,-13.2), settles six frames, takes that item, then
   approaches (16.8,-12) and settles six frames. Again bound360 frames.
3. `edge`: require completed relay **and** both exact chef/source attachment
   directions, unchanged empty pot7/home19, current native placement target19,
   no dash/impact/throw/interaction, and zero actual, cached and resumable
   horizontal motion. Prepare one ordinary primary-down frame with the other
   pads neutral; the existing raw runner appends two neutral release frames.
   Native focus/control/cooldown rules still decide acceptance. Refusal does
   not permit an automatic retry, success claim, or speculative extra input.
4. `loaded`: require same fixed inventory and pot/home, empty hands, no source
   in actual native food, no extra live native physics container, exact one
   Frankfurter in the pot, and native Raw cooking progress strictly between0
   and2 seconds. Historical registry receipts may remain after actual removal;
   they are not live objects. Retain the actual destruction/retirement messages
   for the logical ingredient and its native physical container separately.

Example file-only invocation:

```powershell
python scripts/framework_station_progress_fixture.py --phase initial --snapshot initial-full.json --native initial-food.json --out stage-case.json
python scripts/framework_station_progress_fixture.py --phase staged --case stage-case.json --snapshot staged-full.json --native staged-food.json --out relay-case.json
python scripts/framework_station_progress_fixture.py --phase edge --case relay-case.json --snapshot held-full.json --native held-food.json --out edge-case.json
python scripts/framework_station_progress_fixture.py --phase loaded --case edge-case.json --snapshot loaded-full.json --native loaded-food.json --out progress-case.json
```

The operator sends only each returned `request` after recording the associated
inputs/observations. The preparer does not certify that an input was executed.
The existing `framework_input_probe.py --out <new-proof-directory> --frames 60
--x 0 --y 0 --chef 103` can run from the verified loaded boundary. It
adds30 initial neutral warmup frames before its checkpoint and then compares
the62-frame original/replay. The initial progress<2 bound leaves this complete
sequence below4 seconds, well before native Cooked12 or the controller's23
second pot safety deadline. Keep the game authoring-paused after the probe;
do not leave a live cooking process advancing during analysis.

Required proof is stricter than `rawInput.outcome == complete`: preserve source
spawn/claim and handoff/take messages; exact ingredient consumption and removal
receipts; original and replay accepted four-pad inputs; native pot hierarchy,
Raw state, cookingTime and positive progress increase; pot/home identities and
attachments; every other food and score ledger unchanged. Original and replay
must have exact matching progress/composition, elapsed round time, native
server/client clocks, full reconstructed entities and full raw physics at the
same declared settled-pause condition. Compare advancing-frame physics, chef
state and ordered raw native events with `compare_framework_frames.py` using
the actual checkpoint/end frame and branch epochs. A failed restore, refused
pickup, input mismatch, missing native effect or extra event is retained and
does not become a pass because endpoint scores match.

Mixing is the next analogous fixture: actual flour crate71 at(31.2,-10.8),
right pass at(25.2,-13.2), bowl6/home18 at(24,-10.8). Resolve those roles anew,
consume exactly one raw Flour18448, and checkpoint its observed Unmixed
progress<2/mixingTime12. Native `ServerMixingStation.OnOrderCompositionChanged`
starts any nonempty valid bowl; `UpdateSynchronising` calls `Mix` with native
TimeManager delta. Partial mixing is real native work, not completed dough.
Adding another ingredient later averages/clamps progress via
`ServerMixableContainer.CalculateCombinedMixingProgress`; a neutral replay
must not add ingredients or pretend that this rule is absent.

Chopping is intentionally a separate test. Actual bun crate68/board23 and
native WorkableItem stage/substage must be resolved. Native
`ServerWorkstation` creates a per-chef animation-timed interactor and keeps the
interaction sticky while unfinished. `ServerWorkableItem.DoWork` spawns the
prepared replacement and destroys the raw item at completion. A60-frame
secondary hold/release cannot be assumed to stop chopping or preserve source
identity. Test that only after dynamic raw/prepared rollback and the interactor
state are supported, or as a fresh one-way completion diagnostic with a fixed
timeout. No chop timing or score is inferred from the framework heuristic.

Source references are the user's extracted native Assembly-CSharp tree under
`H:\tiny2\Overcooked2\tinyoc2\Assets\Scripts\Assembly-CSharp`:
`ServerCookingStation.UpdateSynchronising` (native delta times cooking speed),
`ServerIngredientContainer.OnContentsChanged`, `ServerMixingStation`,
`ServerMixingHandler`, `ServerMixableContainer`, `ServerWorkstation`, and
`ServerWorkableItem`. The duration, fixed roles and component names above also
come from actual S receipts. This review is not a new whole-assembly equivalence
claim. The V9 typed adapter explicitly limits placement to attachment and
rejects consumed items (`ObservedTransferAction.cs`), hence the separate native
load edge and result barrier.

Validation: five offline tests pass, comprising the actual S cold-layout case,
synthetic staged/held/consumed positive cases, and26 negative subcases for native
metadata, source lineage, attachments, readiness, motion, target, food and
missing consumption. Synthetic states are not native probe evidence.
