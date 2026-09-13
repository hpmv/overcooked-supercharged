# Authoring clock: paused callbacks and exact checkpoints

The closed native N trace demonstrates a clock-source problem independently of its repaired station synchronization problem. The original checkpoint-to-first-advancing-frame interval contains seven native exchange callbacks; both rewinds contain eight. The existing `(float)(Time.time + authoringOffset)` source advances during those authoring-paused callbacks. The same native TimeSync payload occurs at gameplay frame 241 originally and frame 240 after the first rewind. The second rewind also sends at 240, with a one-bit payload difference.

| N observation | Original | First replay | Second replay |
| --- | ---: | ---: | ---: |
| First checkpoint receipt, expanded exchange ordinal | 1651 | 1782 | 1914 |
| First advancing frame 182 receipt | 1658 | 1790 | 1922 |
| Callback distance | 7 | 8 | 8 |
| Native TimeSync gameplay frame | 241 | 240 | 240 |
| Original message payload | `QegAAQ==` | `QegAAQ==` | `QegAAA==` |
| Decoded float | 29.000001907348633 | 29.000001907348633 | 29 |

Evidence is in `artifacts/framework-migration/native-n/idle-clock-phase-evidence.json`, `frame-parity-0-1.json`, and `idle-native-event-difference.json`. The latter separately identifies the eight additional off/empty CookingStation messages at frame 182. Root's station checkpoint change addresses those pending messages, not the clock source.

Closed native P is a useful counterexample: its `idle-frame-parity.json` passes all 120 advancing frames, including every native event and raw physics field. Its callback distances were 13/8/8, but **none** of those intervals contains a TimeSync event. That pass validates the station repair without testing a clock threshold.

Closed native R supplies the stronger native clock evidence. Across the original and three 240-frame replays, callback distances were 7/8/9/8. Every interval emitted TimeSync at gameplay frame **263**, with identical payload `Qeiqqw==` (native float 29.08333396911621). Exact trace SHA and message locations are recorded in `artifacts/framework-migration/native-r/idle-clock-phase-evidence.json`. Root's full advancing-frame comparisons also pass. This verifies the tested active clock/event continuation across varying paused counts; it does not erase raw pause-acknowledgement sleep-bit differences.

## Source distinction

`UnrealTimePatch.LogicalRealtime` currently derives its value from Unity `Time.time` plus a double offset. Native `TimeManager.Update` sets `timeScale` from its debug multiplier. Its Main pause instead suppresses layer time, freezes bodies and pauses animators; it does not freeze Unity's public `Time.time`. Native `ServerTime.Update` uses a strict `m_fServerTime > m_fNextSyncTime` test and sets the next threshold to source time plus three seconds. `ClientTime.Update` also derives its delta from that source. Neither native algorithm is changed by the proposed helper.

There are three distinct states:

1. **Explicit authoring pause:** ownership of the stable key used by `Helpers.Pause/Resume`. Extra Thrift polls, inspection and physics-phase alignment must not consume authoring timeline time.
2. **Native game/menu/round pause without that ownership:** native global clocks keep their normal behavior. Do not treat every `TimeManager.IsPaused(Main)` or suppressed round timer as an authoring pause.
3. **An active native frame:** exactly one capture step elapses, including an active render frame with no FixedUpdate. A physics call cannot add another capture step.

The root-approved integration will also suspend `ServerTime.Update` and `ClientTime.Update` only for authoring-paused frames. That preserves their checkpointed private fields, including the last nonzero client delta. It is explicit authoring suspension, not a new rule for native menu pause. On active frames the original native methods, comparison, arithmetic and event emission execute. Pending native sync work is preserved rather than flushed or suppressed as a repair.

## Pure helper, not a plugin patch

The initial isolated helper was written in `scripts/FrameworkLogicalClockCheck/AuthoringClockState.cs`. Root subsequently integrated the production copy at `framework/patch/AuthoringClockState.cs` and linked the executable harness to that copy. The state machine itself does not reference Unity, TimeManager, Harmony or game objects; root owns its plugin hooks in `UnrealTimePatch` and the existing checkpoint/pause code.

* Construct with the **already observed** current native frame, its source value, the configured float capture step and current authoring ownership. Construction adds no invented tick.
* `ObserveFrame(frame)` caches one value and one eligibility decision. Every new native frame must be observed; skipped or backward identities fail instead of guessing pause history. Controller frame tags are unsuitable because they rewind.
* `SetAuthoringPaused(frame, paused)` first observes that elapsed frame using its previous ownership, then changes the next frame's ownership. A late pause cannot turn elapsed gameplay into a pause. A late resume cannot turn elapsed paused work into gameplay. Repeated same-frame getters return the cached value.
* `ShouldRunNativeClockUpdates` exposes the cached eligibility. Native/menu-only pause is deliberately not an input to this helper.
* `CaptureCheckpoint()` retains an immutable double anchor, integer eligible-tick count, float step and exact public float. `RestoreCheckpoint` accepts only the same clock instance and an already observed, authoring-paused current frame. It restores the current cache, retains a sticky restore count and leaves the next frame paused. Temporary native Resume/Pause inside WarpHandler cannot make the cached paused frame active.

The source is `float(anchor + eligibleTicks * double(captureStep))`, rounded once at the native float boundary. It is not reconstructed from a saved float plus today's larger Unity float. A focused fixture starting at 27.95 and checkpointing after 179 ticks diverges after four subsequent frames if it is instead re-anchored at the rounded saved float.

The tests pin `captureStep = 1f / 60f`. The observed N Unity value 24.7500019 is consistent with 1485 multiplied by that float step. Mathematical double `1.0 / 60.0` is a different deterministic policy and can cross native strict thresholds on a different float boundary. Changing to it would require a separate declared policy and forward native comparison. A getter must never use `Time.deltaTime`: a call from FixedUpdate can see the physics step instead.

## Integration obligations

Root owns the plugin hooks. Before integration, pin the Unity frame-entry key across Update and all preceding FixedUpdate calls. Drive the helper once every native frame, before any native clock method, even when no consumer otherwise asks for time. The helper cannot establish Unity execution order on its own.

`Helpers.Pause/Resume` must report their own arbitration ownership through the setter, including bridge calls. The setter's observe-before-change rule also handles a pause callback that precedes the first getter. On a restore, the current frame must already be latched as authoring-paused before WarpHandler temporarily resumes native bodies. Restore the full exact clock checkpoint alongside the existing native ServerTime/ClientTime private fields; do not serialize only its float `Value`. The native frame identity itself remains current and is never rewound.

Do not derive time advancement from receipt of `NextFrame`, an RPC count, a gameplay frame tag, native round elapsed time, or the number of FixedUpdate callbacks. Native phase alignment may continue while the authoring clock is frozen; Unity time and its physics accumulator remain unchanged.

## Validation and limits

Run:

```powershell
dotnet run --project scripts/FrameworkLogicalClockCheck -c Release -- artifacts/framework-migration/logical-clock-check.json
```

The executable passes **117 assertions** covering 1, 7 and 1000 paused polls, repeated getters, setter-before-getter transitions, native-only pause, six float-rounding magnitudes, same-frame warp restoration, exact native-arithmetic-model sync frames and payloads, strict threshold equality, foreign/missing checkpoints and unknown frame histories. The arithmetic model is an executable transcription, not evidence that a plugin hook has run correctly in the game.

After integration, root should repeat the same native checkpoint and active input sequence with deliberately different paused poll counts, then compare every advancing native event, input and physical state. Include a checkpoint immediately around a three-second sync threshold. Require matching private clock state and exact source values before resuming, and confirm active source cadence and native-menu-only pause separately. A fresh process using the same declared source policy is required for feedback-free playback; these authoring tests do not qualify a score or prove full physics rewind.
