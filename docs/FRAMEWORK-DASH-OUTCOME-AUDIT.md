# Four-chef native dash acceptance

`scripts/check_framework_dash_outcome.py` is an offline companion to the existing
input/replay probe. It opens no port and changes no runner or native component.
No native four-chef dash run was available when it was prepared.

The exact fixture `routes/probes/framework-four-chef-dash.json` sends one dash
edge on all four observed chefs, with103/105 walking left and104/106 right. It
then sends25 walking frames with dash released. The raw runner adds two neutral
frames: **26 payload,28 total**. The checker compares every emitted pad and
button edge against that fixture, using the actual selected checkpoint frame
and trace epoch. It rejects correction/warp/seed directives, incomplete frame
coverage, unknown chef IDs, missing native timer telemetry and malformed native
Dash messages. The shared reader supports plain/gzip traces and verified V6
Brotli paused blocks; audit a closed trace.

For every chef, require at least one of these during the payload, after its
recorded dash edge:

- Native `Chefs[id].DashTimer` transitions from its non-dashing baseline to a
  larger value greater than zero. All28 actual timer values are retained.
- A native EntityEvent for that chef's registry-resolved **InputEvent component
  type30**, subtype **Dash0**. The raw bytes and location are retained.

The native `ClientPlayerControlsImpl_Default.Update_Movement` first tests the
ordinary dash button's `JustPressed`, cooldown and impact gates, assigns
`m_dashTimer = Movement.DashTime`, then invokes server `StartDash`. The server
emits `InputEventMessage(Dash)`. Its wire payload is event-type10 bits followed
by entity-argument10 bits, after the native entity10/component4 header.
`DashCollision1` and cosmetic chef effects are different messages and are not
accepted as `StartDash`. Collision can reset the timer to float.MinValue in the
same gameplay interval; an exact `StartDash` receipt therefore remains valid
even when no positive timer reaches an output snapshot. Conversely, changed
position or a long movement distance proves neither receipt.

Run independently for original and replay branches, using the actual frames:

```powershell
python scripts/check_framework_dash_outcome.py --trace <closed-exchange.jsonl> --epoch 0 --start <checkpoint-frame> --out <original-dash-outcome.json>
python scripts/check_framework_dash_outcome.py --trace <closed-exchange.jsonl> --epoch 1 --start <checkpoint-frame> --out <replayed-dash-outcome.json>
```

The output lists missing chefs, each edge/witness frame and its measured delay,
timer/event evidence, and input/trace hashes. A passed dash outcome is not
rewind parity. The existing strict frame comparator must separately match all28
frames of physics, chef state, inputs and ordered native messages, and the
native probe must match food, round state and declared pause-boundary clocks.

Seven offline tests pass: synthetic positive timer and exact wire-event cases,
immediate collision reset, one refused chef, moving endpoints without dash,
input/telemetry mutations, wrong/collision events, release-only events,
preexisting dash and malformed payload. Synthetic passes are not native evidence.
