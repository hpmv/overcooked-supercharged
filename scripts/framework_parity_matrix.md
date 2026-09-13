# Native rewind acceptance matrix

Full rewind parity is not established by a matching score or a matching input hash.
Each experiment records the original checkpoint, native clocks/order RNG/score/food,
all reconstructed entities, emitted inputs, and corresponding restored continuation.
No numerical tolerance is applied by the current raw entity comparisons.

Required cases: idle across recipe arrivals; each chef movement/dash; wall and chef
collisions; pickup/drop/re-pickup; spawned ingredient pickup and removal; chopping
and replacement; cooking/mixing/frying; throws and catches; simultaneous use;
washing and plate return; cannon loading, flight, landing; delivery/fade; active
order expiry and deductions; disconnect neutralization. Active native coroutine
states remain unsupported until explicit capture/restore and native tests exist.

Qualification requires per-boundary comparisons and repeated future continuations,
20 consecutive passing probes including five fresh process starts. Native IDs,
logical entity paths, raw floating point differences, and gameplay-event differences
must be reported separately. A dynamic object with a different native allocation ID
is not a claim of identical native state.

Current scope: bounded authoring rewind only. Final score verification must use
fresh starts without authoring rewind or position correction.
