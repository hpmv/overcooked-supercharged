# Native logical focus and checkpoint discovery

The installed `LogicalButtonBase.Update` consumes both press and release claims when its virtual `CanProcessInput` returns false. The native default predicate reads `Application.isFocused`. `GateLogicalButton` inherits that predicate while maintaining its own claims. The earlier virtual-pad patch changed only the separate `PlayerControls.CanButtonBePressed` focus check, so unfocused movement levels could work while pickup edges were silently claimed.

Frozen plugin V adds a focus-getter substitution in the original logical predicate. It admits only a registered TAS device, or an observed native gate chain whose exact child references lead to that device. The device must still belong to the same locally controlled chef incarnation and current active virtual pad. Native gate callbacks, down levels, press/release claims, held duration and cooldowns retain their original implementations. Physical buttons, unobserved wrappers, stale/foreign chefs and `OC2SC_BACKGROUND_INPUT=0` retain the native focus behavior.

`scripts/FrameworkLogicalFocusCheck` passes31 enabled and13 opt-out checks against the installed game IL and frozen V plugin. Its production source tests use a small Unity/Harmony metadata surface under .NET10; installed CLR2 compilation is separate. Reports: `artifacts/framework-focus-check/tests-v.json` and `tests-v-disabled.json`. These offline checks do not qualify native unfocused interaction success.

The closed S/U plate-search evidence is recorded by `scripts/check_framework_focus_evidence.py`. S completed the plate transfer while focused; U timed out while unfocused. All four emitted pads are identical for frames32 through56, including the first pickup edge at54. U's strict11-frame input replay was a parity result without a successful pickup; its original evidence remains unchanged.

## Measured capture cost

V's captured pickup-failure receipt contains real `kitchenCaptureStages` counters. Its process-lifetime means were11.285ms per full capture: station synchronization5.370ms, cannons2.808ms, chef interactions2.687ms, and the remaining capture work about0.42ms. A separate flow lookup averaged2.639ms on every callback. The slow stages each invoked a full Unity scene discovery; station synchronization invoked two. Startup and paused callbacks are included, so these figures are not FPS measurements.

The following candidate performs one `FindObjectsOfType<MonoBehaviour>()` per synchronous `CaptureFrame`. Typed filters preserve its order and refresh membership on every callback. Every native field and serialized payload is still read at its original capture site. `finally` releases the membership array, and sidecars called outside that capture retain their original typed native query.

The first three requests for each type after each native scene-metadata refresh compare exact instance IDs and ordering against the old typed query. A mismatch immediately returns the original result and disables batching for that type until the next load. More than one kitchen flow also uses the original singular query. Native diagnostics expose the comparisons and refusals under `checkpointComponentDiscovery`. No component membership or state is reused across callbacks.

`scripts/FrameworkComponentBatchCheck` passes33 lifecycle, fallback and compiled-IL checks. Installed-target compilation preserves original capture call/write order except the explicit discovery replacements. Native membership equality, parity and speed still require a new run. `scripts/framework_checkpoint_profile.py` reports lifetime counters or same-process deltas, with separate denominators and nested-stage caveats.

## W native measurements

W observed all five native typed lists with exactly the same instance-ID membership and ordering as the broad query on each of their first three comparisons. None refused or fell back. Its later measured interval performed308 component discoveries for308 checkpoint callbacks and no extra typed comparison scans. Each callback discovered5015 current MonoBehaviours; the array was cleared before the paused diagnostic response.

The comparison below uses deltas within each process, excluding accumulated startup costs. V's neutral warmup advanced30 frames and included38 capture callbacks; W's neutral run advanced300 frames and included308 callbacks. Neither interval performed an authoring restore.

| Mean work per capture callback | V | W |
| --- | ---: | ---: |
| Entire `CaptureFrame`, including discovery | 14.535ms | 4.868ms |
| Inner kitchen snapshot capture | 11.330ms | 1.323ms |
| Cannon snapshot | 2.776ms | 0.229ms |
| Chef interaction snapshot | 2.801ms | 0.207ms |
| Station synchronization snapshot | 5.360ms | 0.470ms |

The entire checkpoint call measured66.51% less work per callback. The shared broad discovery still costs3.308ms per callback and is included in W's4.868ms total. W completed300 advancing frames in6.062 wall seconds, or49.49FPS. V was focused and W unfocused, and their intervals differ in length; this is not a controlled FPS speedup measurement. The W timing run did not qualify a full rewind/replay.

Exact receipts, source binaries, counter deltas and membership arrays are pinned in `artifacts/framework-component-batch-check/native-v-w-comparison.json`. Original native timing receipts remain under `artifacts/framework-migration/native-w/profile-300-*`.
