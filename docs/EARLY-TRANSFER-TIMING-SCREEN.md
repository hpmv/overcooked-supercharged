# Earlier native transfer: read-only opportunity screen

The closed V19 plated-hotdog probe contains a repeated six-frame delay after a stationary chef already has the correct, eligible native pickup/placement target. Thirteen generic transfers expose99 aggregate decision frames between the first observed valid target and the existing input decision, including69 frames after the first valid target with zero cached horizontal velocity. Those are potential overlaps/omissions, not measured game-time savings: cooking waits and concurrent jobs mean they cannot simply be subtracted from the19.4167-second probe.

[Detailed observations](M:/projects/game-test-2/artifacts/plated-hotdog-transfer-timing-screen.json) retain each action's native target, stages, candidate poses, cached velocity, phase and actual input. A candidate observed at frame F could first affect the native game at F+1. The table therefore compares observation frames, not the next response containing the actual pickup edge.

| Chef/action/target | First eligible native target | First zero-velocity target | Existing decision → native edge | Potential frames after zero velocity |
|---|---:|---:|---:|---:|
|P2 take sausage70|28|29|29 →30|0|
|P2 take onion69|67|67|76 →77|9|
|P2 place onion56|93|100|106 →107|6|
|P2 take bun68|236|237|243 →244|6|
|P0 take chopped onion56|258|none before edge|258 →259|0|
|P2 place bun23|284|287|293 →294|6|
|P0 place onion into pan9/home21|340|346|352 →353|6|
|P0 take original plate38|435|437|443 →444|6|
|P0 assemble bun on23|461|464|470 →471|6|
|P0 consume cooked pot7/home19|785|785|791 →792|6|
|P0 apply mustard72|825|826|832 →833|6|
|P0 consume cooked pan9/home21|1075|1075|1081 →1082|6|
|P0 place final plate49|1150|1156|1162 →1163|6|

The repeated six-frame pattern is two additional navigation settling observations, three face-stage frames, then entry into verify. `Face` may already issue no movement because forward is aligned. It can also create movement even when the correct native target was already present: the completed onion cook at1075 already exposes valid placement21 with zero cached velocity, but the generic face stage issues a normalized movement input again at1079. Native movement normalizes the nominal0.15 face stick, so this is a full walking velocity, not slow turning in place.

Native source supports trying an earlier ordinary input, but does not prove a particular earlier input safe. In `ClientPlayerControlsImpl_Default.Update_Impl`, `UpdateNearbyObjects` runs before `Update_Carry`, and rotation/movement update runs afterward. `Update_Carry` consumes `JustPressed`, checks pickup eligibility and native pickup handling, or sends the held-item placement event. Neither this client path nor `ServerPlayerControlsImpl_Default.ReceivePickUpEvent/ReceivePlaceEvent` requires speed zero. Ordinary controls, current carrier state, the native next-pickup timestamp, and target-specific native handlers remain required.

The critical risk is before that Update: the next FixedUpdate can consume the previous cached velocity, then the native interaction scan selects a new object. `PlayerControls.FindNearbyObjects` uses the game's collider/grid scan and handler predicates; it is not a promise that the target from the preceding snapshot remains selected. A wrong empty-hand pickup can take another item. More seriously, `PlaceHeldItem_Client` with a held item and no current placement handler sends native `Take`, dropping the item. A generic early drop must not infer permission solely from the prior `placementTargetId`.

Concrete native observations illustrate both cases. From435→436, plate target38 remains selected while the chef walks6units/s; the queued position estimate differs from the next actual position by0.000000735units. From1150→1151, output49 remains selected while walking, with0.000002772units error. In contrast, at28→29, the sausage crate approach hits native collision: queued prediction differs from the actual next pose by0.116846units. Target70 happens to remain selected, but this is a counterexample to treating the unclipped cached displacement as the actual next position. These existing inputs do not include an earlier pickup/drop edge and therefore do not constitute a moving-transfer mechanism proof.

The narrow first candidate is an exact already-eligible native target at a controlled, empty/expected-held state with effectively zero cached horizontal motion, no active dash/impact/throw/interaction suppression, stable native station/source/held incarnations and the normal material prerequisites. It would issue ordinary neutral movement plus a fresh pickup edge and retain the existing native `await-transfer`, composition/attachment, retry and plate-under recovery barriers. Original job/leases remain untouched. A stationary native probe can establish this before considering moving final-segment transfers. Normal final navigation behavior should remain the fallback whenever a guard is uncertain.

An actual moving-transfer candidate needs a separate probe and narrower scene/phase evidence for the queued collision-free displacement and native selection after that displacement. Concurrent moving ingredients/chefs, grid boundaries, target occupancy changes, active dash/coasting, or a just-selected collider near a competing target all require explicit rejection or further proof. Keeping the same movement request during an input edge is legal in the source, but this screen has not executed it.

Full native fixtures are saved at `artifacts/early-transfer-b-gf67.json`, `-gf435.json`, `-gf436.json`, `-gf437.json`, `-gf1150.json`, `-gf1151.json` and `-gf1156.json`. [Provenance](M:/projects/game-test-2/artifacts/early-transfer-b-screen-provenance.json) pins these, the original trace/route, the frozen controller source and local native source used for this review. No controller, plugin, native input or game state was changed by this investigation.
