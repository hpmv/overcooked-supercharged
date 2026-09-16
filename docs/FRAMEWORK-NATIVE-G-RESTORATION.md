# Native G source restoration

This change removes the framework's replacement cannon registration and codec. Installed native cannon components, sessions, callbacks, flight/exit iterators and `CannonMessage` execute normally. The old `*CannonMod*` classes remain compiled but are not registered. Native advancing synchronization cadence, world-object updates, loader readiness and mesh interpolation are restored together. The existing explicit-pause scheduler guard remains.

The patch observes native cannons through a separate versioned auxiliary message. It does not call native launch/load/unload callbacks or change ordinary cannon state. Controller V5 decodes the native wire message and the separate auxiliary state; it must be used with this plugin change.

Delivered plates remain registered for their actual native fade/destruction lifetime. The first auxiliary receipt means served, not removed. Actual registry removal produces a second receipt and the existing controller retirement event. Registry `Clear` resets old observer metadata without inventing individual retirements. A reentrant ID reuse at removal is diagnosed; the observer omits an unsafe ID-only retirement and authoring rejects until a fresh registry. This exceptional case is a controlled fixture, not an observed native failure.

## Bounded explicit authoring restore

Both the captured and current native cannon states must be inactive: no server/client session, flight, launch/exit iterator, cannon-parented chef, interaction-end callback, active player handler, occupied animator or animator transition. A stale native loaded-object pointer alone is allowed; the saved registry entry must still identify that same passenger incarnation. Active delivery fades also reject before mutation.

For admitted explicit warps, the sidecar restores exact native cannon fields, pilot rotation and inactive animator playback. The gameplay-field postcondition is checked. `Animator.Play` takes effect later; this is **not** an animator state/time equivalence proof. Native synchronization accumulators, world prediction caches and all raw physics are not comprehensively checkpointed by this change. Native replay comparisons must distinguish gameplay evidence from raw transform equality.

The native-F pickup experiment identified another state dependency: `ClientPlayerControlsImpl_Default.Update_Carry` admits an edge only after `ClientTime.Time()` reaches private `m_lastPickupTimestamp`. Rewinding the clock without that timestamp can block the repeated pickup. G captures/restores the exact native float alongside the clock, and restores native `ControlSchemeData.m_supressUse` instead of forcing use suppression false. Field types, chef/native ID/Unity instance/control-scheme identity and post-restoration values are checked before/after explicit warp. The timestamp is not rebased against Unity wall time.

## Validation and limits

`scripts/FrameworkNativeCheckpointCheck` runs 59 focused checks against the production checkpoint/guard/lifecycle source with controlled object/registry fixtures and installed-assembly metadata. These cover exact cooldown/suppression restoration, identity failures before writes, every inactive-cannon guard dimension, native field presence/types, absence of the removed Harmony overrides, and served/removed/reset/reused-ID lifecycle separation. The plugin builds against the installed managed assemblies.

These checks do not execute a game. Native G pickup replay, cannon boarding/cancel/fire/landing/world-resume, native plate fade/removal and physics replay remain separate root-owned probes. An authoring-restored run is not a fresh-start high-score qualification.
