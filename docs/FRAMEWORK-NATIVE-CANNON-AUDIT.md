# Native cannon, retirement, and synchronization audit

Read-only source audit, 2026-09-07. No game calls or component changes were made for this report. The framework currently replaces ordinary cannon behavior; it cannot yet be described as native-equivalent. This finding is independent of the successful input/weighted-recipe source checks and the kitchen-clock authoring work.

The installed decompilation root is `M:/projects/AssetRipper/Source/0Bins/AssetRipper.Tools.SystemTester/Release/Ripped/ExportedProject/Assets/Scripts/Assembly-CSharp`. The accompanying `artifacts/framework-native-cannon-audit/manifest.json` pins the inspected files and installed assembly. References below are relative to the repository, or to that native root when marked **native**.

## Replacement inventory

`framework/patch/ComponentAltering.cs:14` replaces the message codec registered for `EntityType.Cannon`; lines27–43 replace **all four component pairs**, not merely add observations.

| Framework class | Native counterpart and concrete difference |
|---|---|
| `ServerCannonMod` | Replaces `ServerCannon` and incorporates pieces of three other native components. Its own five-state machine owns boarding, flight and cleanup. Details below. |
| `ClientCannonMod` | Only returns its entity type. Native `ClientCannon:114` consumes Load/Unload/Launched events; its coroutine list drives the original `ProjectileAnimation.Run`, handler callbacks, landing and synchronization-resume wait. All of that native client behavior is absent. |
| `ServerCannonSessionInteractableMod` | Empty `ServerSynchroniserBase`, replacing the native `ServerSessionInteractable` subclass. Removes its session object, SessionInteractable messages, placement-trigger callback, session begin/end triggers, and DestroyChef subscription/cleanup. Some ordinary use-button boarding logic was copied into ServerCannonMod. |
| `ClientCannonSessionInteractableMod` | Empty synchronizer replaces native `ClientSessionInteractable` and its cannon session. Native client session signaling/lifecycle is absent. |
| `ServerCannonPlayerHandlerMod` | Empty synchronizer replaces `IServerCannonHandler`. Native `ExitCannonRoutine` restores rigidbody state, refreshes GroundCast, and invokes `ServerWorldObjectSynchroniser.ResumeAllClients(false)`. The replacement copies the first two operations but omits the resume handshake. |
| `ClientCannonPlayerHandlerMod` | Empty synchronizer replaces `IClientCannonHandler`. The original pauses world synchronization on load, manages direct-control throw indicators, performs Launch/Land callbacks, waits one coroutine boundary and then waits for resume data before resuming synchronization. The replacement copies selected controls/collider operations without this lifecycle. |
| `ServerCannonCosmeticDecisionsMod` | Keeps the same animator Ready-state predicate, exposes normalized Load animation capture/`Animator.Play` restoration, and removes native callback registration on ServerCannon. The replacement polls it directly. The added accessors are authoring support; replacing the component is unnecessary merely to read/restore them. |
| `ClientCannonCosmeticDecisionsMod` | Its Load/Unload/Launch visual method bodies match the installed source after removing formatting, `this`/`base` qualifiers and field ordering. It removes the three native ClientCannon callback subscriptions; ServerCannonMod calls those methods directly. Thus visual logic is copied, but callback timing and ordering differ. |

## Specific ordinary-gameplay changes

1. **Unload location is different.** `ServerCannonMod:268` calls `OnLoad` before saving `m_exitPosition/Rotation`. `OnLoad:159` already snaps the chef onto the attach point. The Loaded branch later teleports to that saved attach pose. Installed `ServerCannon.Unload:79` and `ClientCannon.ApplyServerEvent:130` use the cannon's actual `m_exitPoint`. Native `ClientCannon.Load:47` also saves its pre-attachment pose before moving, although the normal native Unload event uses the exit point.
2. **Dash cannot cancel during Loading.** The mod checks dash only in `Loaded` (`ServerCannonMod:31`). Native `ServerCannonSessionInteractable.UserSession.Update` checks throughout the live session whenever `!IsFlying()`, including before the animator is Ready. The mod also delays use-release consumption until Loaded.
3. **Placement boarding is absent.** Native `ServerCannonSessionInteractable.StartSynchronising` registers both `ServerPlacementInteractable` CanInteract and StartSession callbacks. The mod obtains that component but only registers on `ServerInteractable`. Carrying a plate and using the native placement interaction therefore needs explicit revalidation; current replacement code does not preserve that input entry point.
4. **Launch has a different execution boundary.** `ServerCannonMod:64` handles BeginLaunching in a server synchronizer update, sets Flying/time0 and does no first interpolation until a subsequent update. Native `ClientCannon` queues the original coroutine on a Launched event; its first `MoveNext` enters `ProjectileAnimation.Run` and computes the time0 pose and first time increment in that call. The interpolation formula, native curve assets and `TimeManager.GetDeltaTime(chef)` are copied correctly; equality of launch/landing frame counts does not follow from that. Message dispatch and update ordering must be measured.
5. **Session/handler lifecycle is bypassed.** There is no native session object, DestroyChef handling, `EndCannonRoutine` delegate call, generic handler enumeration, or native session-begin/end trigger. `ServerCannonMod.OnDestroy` only calls its base and does not remove its registered interaction callbacks. Null/destroyed passengers during loading/flight are dereferenced, whereas the native projectile iterator stops for a destroyed object.
6. **Cleanup changes observable state.** `OnExit:173` writes localScale=(1,1,1) after ordinary flights as well as warps; native cannon cleanup does not. The mod calls its Unload cosmetic routine after landing; native successful flight invokes Launch then Land/Exit, without the Unload callback. This can add unload audio/animator transitions. The native server's launch-button achievement callback is also absent. Native landing enables controls before applying impact; the mod applies impact before its combined OnExit enables them. The impact magnitude/duration are the same (2,0.2).
7. **Launch admission and wire protocol change.** The mod accepts launch only in Loaded; native `ServerCannon.OnTrigger:115` checks the trigger and `!m_flying`, relying on native button/session state. `CannonModMessage` uses five states plus serialized pose/flight/load history instead of native three-state `CannonMessage`. The controller's `Overcooked/Deserializer.cs:61`, `Data/RealGameSimulator.cs:383`, `GameEntityRecord.cs:181` and WarpHandler all depend on the replacement protocol. Disabling only one replacement would mix incompatible codecs/components.

## Delivered plates

`framework/patch/AlteredComponents/PlateStationPatching.cs:6–12` runs **after every native client DeliverPlate**, emits a controller-only retirement message and immediately calls real `EntitySerialisationRegistry.UnregisterObject`.

The native path is different: `ServerPlateStation:66` scores the dish, reserves it and starts delivery; `ServerPlate:192` detaches the plate, disables its colliders and makes it kinematic. `ClientPlateStation:70–138` runs the native particle delay/fade coroutine and destroys the object at the end. Immediate unregister neither reproduces that delay nor merely hides the object from the controller: native registry removal stops synchronizers, removes mappings and enqueues the ID for reuse (`Team17/Online/Multiplayer/Messaging/EntitySerialisationRegistry`, UnregisterObject/RemoveEntry). The already-running fade coroutine can continue on the now-unregistered object.

This patch does not directly rewrite the already-awarded score or dirty-return timer. It does change registration lifetime, ID allocation and which objects later messages/observations can resolve. Minimum correction: keep a separate **served/unavailable** controller marker at the native delivery event; leave the native object registered until actual removal. Emit the actual retirement observation from native `RemoveEntry`/unregistration, with ID and incarnation. Do not make logical delivery and physical retirement the same event. Authoring snapshots that intersect a delivered-plate fade must initially reject, or separately capture that native coroutine/presentation state; immediate unregister is not a valid substitute.

## World synchronization and startup

These are coupled changes in `AlteredComponents/SynchronizationPatches.cs`:

| Location | Native behavior removed or changed |
|---|---|
| 141–147, `SynchroniseList` | Forces frame delay0. Native scheduler uses0.1s ordinary cadence and1/30s fast cadence, with an urgent-update path. With delay0 it walks every list every rendered update and never subtracts a positive cadence from its accumulators. |
| 151–161, `GetServerUpdate` | Returns null without executing native world-object message population. This suppresses position/parent updates **and** native activity/rest/parent-change bookkeeping and its reliable rest-position event after1s. It is not a read-only observer optimization. |
| 165–175, `CheckStarted` | Returns true instead of checking that each native WorldObject/Chef synchronizer has received its initial update. The native loader normally waits for readiness, with its own10s fallback. |
| 179–185, `MeshLerper.Update` | Skips native visual interpolation, parent-motion compensation and lerp clock progression. Native source primarily writes its target mesh transform; this is not proof of a changed authoritative rigidbody in every case, but raw rendered/transformed state is different. |
| 189 onward | The ClientWorldObject/ClientChef/MeshLerper diagnostic prefixes are void and **do not suppress** their original methods. Do not count these warning-only hooks as additional skipped native callbacks. Likewise the SendMessageToClient prefix's EntitySynchronisation return only omits controller capture; it does not cancel the native call. |

The native cannon handshake specifically depends on these systems. `ClientCannonPlayerHandler.Load` pauses world sync; its Exit routine waits for pending resume data. `ServerCannonPlayerHandler.ExitCannonRoutine` requests native resume messages. Restoring the original cannon pair while leaving global world-update suppression unchanged requires a coupled probe, and is an unnecessary compatibility risk. Restore the native scheduler/world/startup path together; keep direct read-only frame snapshots for controller observations instead of changing native network cadence to obtain them.

## Smallest migration path

1. **Create one explicit native-component backend selection before scene registration**, defaulting to installed native cannon configs and native CannonMessage codec. Retain the old replacement backend only as a named unqualified authoring compatibility mode, if needed. Switch patch and controller codecs together. Add an auxiliary observed cannon DTO/history for planning, rather than inject a custom message into the native Cannon entity type.
2. **Initially retain authoring warp only at verified inactive cannon boundaries on both source and target.** Require no native session, `!IsFlying`, no client launch/exit iterator, no cannon-parented chef, and settled native animation/control state. Do not use `m_loadedObject==null` alone: native ServerCannon can retain a stale passenger reference after completion. Capture exact native fields/angle in the process-local sidecar and reject unsupported active states before any mutation. Kitchen/entity authoring remains useful without replacing all ordinary cannon execution.
3. Restore the native scheduler cadence, world-object getter, loader readiness and MeshLerper in the same native backend. Paused-authoring suppression may remain scoped to explicit pause/restore; it must not alter advancing native frames. Add synchronization accumulators/pending resume state to the authoring checkpoint before claiming clock-complete rebranch behavior with that backend.
4. Remove immediate delivered-plate unregister; separate logical served status from actual registry retirement. This is smaller than cannon migration and independently testable.
5. Extend authoring to Loaded, Loading and Flying only after native ordinary behavior passes. Active flight needs exact native iterator state, native session/delegate/mailbox relationships and synchronization-resume state, not just flight time and chef transform. A process-local iterator/field checkpoint may be feasible, but cannot be declared correct from this source audit. Reject unsupported warp states rather than replace the live native coroutine.

## Required native comparisons

Use identical fixed-step inputs and native parameters, a fresh process/load and the same phase for each pair. Compare ordinary native baseline with the new native backend; compare old mod separately as a diagnostic, not the authority.

- Empty-handed **use** boarding and held-plate **placement** boarding on both cannons; exact plate incarnation/contents retained.
- Dash exit once during Loading and once after Ready; exact native exit pose, input gates, collider/rigidbody state, session end and next accepted movement edge.
- Fire at the first observed Ready boundary across all six render/physics phases; native launch receipt, first coroutine pose, every flight sample, impact timer, landing/controls and world-sync resume. Include next cannon/portal interaction after landing.
- Load, destroy/remove the passenger through an authorized native lifecycle, and verify cleanup; no stale callback or passenger dereference. Test game pause/resume while loaded and in flight independently from authoring warp.
- One delivery through native fade retirement: score/tip and dirty queue, actual delivery-to-removal interval, registration sequence, and next allocated ID. Then one wash/reuse cycle.
- Startup with native world synchronization: original CheckStarted transition and first received flags, then pickup/attachment, moving held item, drop and thrown item parent/position synchronization.
- Authoring inactive-cannon checkpoint: restore kitchen clocks/order deadlines/RNG, native cannon fields and synchronization state; rerun the suffix and compare native events and raw transforms separately. Only afterward add each active cannon checkpoint phase.

No equivalence probe in this list was executed by this audit. Restoring native components is the proposed fix, not a new claim that the current framework is qualified.
