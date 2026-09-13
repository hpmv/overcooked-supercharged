using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hpmv;
using OrderController;
using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Extensions;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Process-local authoring sidecar. Keys are actual OutputData.FrameNumber,
    // never Unity wall frames or a derived frame/60 timer. It observes gameplay
    // boundaries; repeated paused callbacks cannot overwrite a saved boundary.
    public static class NativeKitchenCheckpoint
    {
        private static readonly Dictionary<int, Snapshot> history = new Dictionary<int, Snapshot>();
        private static readonly HashSet<int> ambiguous = new HashSet<int>();
        private static object roundIdentity;
        private static int lastFrame = -1;
        private static string lastError = "";
        private static object lastRestore;
        private static object lastRestoreFailure;
        private static int restoreAttempts;
        private static int lastRequestedFrame = -1;
        private static readonly string[] serverClockFields = { "m_fNextSyncTime", "m_fServerTime", "m_fLastTime" };
        private static readonly string[] clientClockFields = { "m_fDelta", "m_fLocalRunningTime", "m_fLocalTimeLastFrame", "m_fLastReceivedTime", "m_fCurrentOffset", "m_fOldOffset" };

        // Process-local profiling only. The Unity main-thread capture/bridge
        // paths own these counters; no native game clock or snapshot field is
        // changed. Stage totals are nested and must not be summed with parents.
        private enum CaptureStage { FrameTotal, FlowFind, FrameAdmission, CaptureTotal, Prerequisites,
            RoundDiagnostics, BaseKitchenClockInput, Cannons, ChefInteractions, StationSync,
            FixedBodyPoses, FixedAttachmentPoses, RemainingOrdersUi, SameGameplayBoundary, Count }
        private static readonly long[] captureStageTicks = new long[(int)CaptureStage.Count];
        private static readonly long[] captureStageCounts = new long[(int)CaptureStage.Count];
        private static readonly long[] captureStageMaximumTicks = new long[(int)CaptureStage.Count];
        private static long Timestamp() { return System.Diagnostics.Stopwatch.GetTimestamp(); }
        private static void RecordTiming(CaptureStage stage, long started)
        {
            long elapsed = Timestamp() - started;
            int index = (int)stage;
            captureStageTicks[index] += elapsed;
            captureStageCounts[index]++;
            if (elapsed > captureStageMaximumTicks[index]) captureStageMaximumTicks[index] = elapsed;
        }
        private static T EndTiming<T>(CaptureStage stage, ref long started, T value)
        {
            RecordTiming(stage, started);
            started = Timestamp();
            return value;
        }
        private static object CaptureTimingDiagnostics()
        {
            var stages = new Dictionary<string, object>();
            for (int i = 0; i < (int)CaptureStage.Count; i++)
                stages.Add(((CaptureStage)i).ToString(), new Dictionary<string, object> {
                    { "calls", captureStageCounts[i] }, { "ticks", captureStageTicks[i] }, { "maximumTicks", captureStageMaximumTicks[i] }
                });
            return new Dictionary<string, object> {
                { "source", "process-local-monotonic-Stopwatch" }, { "tickFrequency", System.Diagnostics.Stopwatch.Frequency },
                { "stages", stages }, { "scope", "Cumulative main-thread measurements including paused captures. Total stages include children; Capture also runs during restore verification. Leaf stages count completed operations; totals include exceptions." }
            };
        }

        public static void CaptureFrame(int frame)
        {
            long started = Timestamp();
            bool batchStarted = false;
            try
            {
                NativeCheckpointComponentBatch.Begin(); batchStarted = true;
                CaptureFrameCore(frame);
            }
            finally
            {
                if (batchStarted) NativeCheckpointComponentBatch.End();
                RecordTiming(CaptureStage.FrameTotal, started);
            }
        }

        private static void CaptureFrameCore(int frame)
        {
            long started = Timestamp();
            var flow = NativeCheckpointComponentBatch.FindFlow();
            RecordTiming(CaptureStage.FlowFind, started);
            started = Timestamp();
            if (flow == null || (GameState)Field(flow, "m_State") != GameState.InLevel) return;
            var monitor = flow.GetMonitorForTeam(TeamID.One);
            var orders = monitor == null ? null : monitor.OrdersController;
            if (orders == null || !(orders.GetRoundData() is WarpableRoundData)) return;
            object identity = orders.GetRoundInstanceData();
            if (!ReferenceEquals(identity, roundIdentity))
            {
                history.Clear(); ambiguous.Clear(); roundIdentity = identity; lastFrame = -1; lastError = "";
            }
            RecordTiming(CaptureStage.FrameAdmission, started);
            // Even a paused poll can flush native order messages. Compare its
            // gameplay state; never overwrite an earlier state under the same key.
            try
            {
                Snapshot snapshot = Capture(flow, frame);
                Snapshot previous;
                if (history.TryGetValue(frame, out previous))
                {
                    if (!SameGameplayBoundary(previous, snapshot))
                    {
                        ambiguous.Add(frame);
                        lastError = "Multiple advancing native states used output frame " + frame;
                    }
                    return;
                }
                if (frame < lastFrame)
                {
                    ambiguous.Add(frame);
                    lastError = "Output frame regressed without a completed authoring checkpoint restore.";
                    return;
                }
                history.Add(frame, snapshot);
                lastFrame = frame;
            }
            catch (Exception error)
            {
                ambiguous.Add(frame);
                lastError = error.ToString();
            }
        }

        public static object Diagnostics()
        {
            Snapshot requested;
            history.TryGetValue(lastRequestedFrame, out requested);
            return new Dictionary<string, object> {
                { "scope", "process-local-native-kitchen-authoring" }, { "frames", history.Count },
                { "kitchenCaptureStages", CaptureTimingDiagnostics() },
                { "checkpointComponentDiscovery", NativeCheckpointComponentBatch.Diagnostics() },
                { "collectionStages", new Dictionary<string, object> {
                    { "samples", ActiveStateCollector.CollectionSamples }, { "entityTicks", ActiveStateCollector.EntityCollectionTicks },
                    { "kitchenTicks", ActiveStateCollector.KitchenCaptureTicks }, { "tickFrequency", System.Diagnostics.Stopwatch.Frequency }
                } },
                { "nativeServerClock", ReadStatics(typeof(ServerTime), serverClockFields) },
                { "nativeClientClock", ReadStatics(typeof(ClientTime), clientClockFields) },
                { "firstFrame", history.Count == 0 ? -1 : history.Keys.Min() }, { "lastFrame", lastFrame },
                { "ambiguousFrames", ambiguous.OrderBy(x => x).ToArray() }, { "lastError", lastError },
                { "restoreAttempts", restoreAttempts }, { "lastRestore", lastRestore }, { "lastRestoreFailure", lastRestoreFailure },
                { "nativeCannonObservationError", NativeCannonCheckpoint.LastObservationError },
                { "nativeCannonCapture", NativeCannonCheckpoint.CaptureDiagnostics() },
                { "nativeBodyRotationRestores", NativeBodyPoseCheckpoint.LastRotationRestores },
                { "nativeBodyWarpStages", NativeBodyPoseCheckpoint.StageDiagnostics },
                { "nativeBodyMassFrameRestores", NativeBodyPoseCheckpoint.LastMassFrameRestores },
                { "requestedBodyCheckpointFrame", lastRequestedFrame },
                { "requestedBodyCheckpoint", requested == null ? null : NativeBodyPoseCheckpoint.Diagnostics(requested.FixedBodyPoses) },
                { "nativeCannonFlightScopeReason", NativeCannonFlightCheckpoint.LastScopeReason },
                { "nativeDynamicWarp", NativeDynamicWarpPlan.LastReceipt },
                { "nativeSpawnObservationError", NativeDynamicWarpPlan.ObservationError },
                { "nativeWarpCapabilities", new Dictionary<string, object> { { "version", 1 }, { "features", NativeRoundEndLatch.CapabilityFeatures } } },
                { "nativeRoundEndLatch", NativeRoundEndLatch.Diagnostics() },
                { "plateLifecycleObservationError", NativePlateLifecycle.LastObservationError },
                { "pendingNativeDeliveryFades", NativePlateLifecycle.PendingDeliveryFades }
            };
        }

        public static void BeginRestoreAttempt()
        {
            NativeInitialAttachmentDeletionAuthorization.Clear();
            restoreAttempts++; lastRestore = null; lastRestoreFailure = null;
        }

        public static void RecordRestoreFailure(int frame, Exception error, bool mutationStarted)
        {
            NativeInitialAttachmentDeletionAuthorization.Clear();
            lastRestoreFailure = new Dictionary<string, object> {
                { "frame", frame }, { "attempt", restoreAttempts }, { "error", error.ToString() },
                { "mutationStarted", mutationStarted }, { "verified", false },
                { "worldRollbackAttempted", false }, { "paused", true }
            };
            lastError = "Authoring warp failed: " + error.Message;
        }

        public static RestorePlan Prepare(WarpSpec warp)
        {
            lastRequestedFrame = warp == null ? -1 : warp.Frame;
            if (warp == null || warp.Entities == null || warp.EntitiesToDelete == null)
                throw new InvalidOperationException("Authoring warp is missing entity data.");
            if (!Helpers.IsPaused()) throw new InvalidOperationException("Native checkpoint restore requires a paused game.");
            Snapshot snapshot;
            if (ambiguous.Contains(warp.Frame) || !history.TryGetValue(warp.Frame, out snapshot))
                throw new InvalidOperationException("No unambiguous observed native kitchen checkpoint at output frame " + warp.Frame);
            ValidateIdentity(snapshot, false);
            bool restoreRoundLifecycle = NativeRoundEndLatch.PrepareRestore(snapshot.RoundLifecycle);
            if (!restoreRoundLifecycle && (GameState)Field(snapshot.Flow, "m_State") != GameState.InLevel)
                throw new InvalidOperationException("Native kitchen checkpoint source is outside InLevel without an exact held round-end latch.");
            NativeSceneMetadata.RequireCurrentInitialMembershipSubset(
                snapshot.FixedBodyPoses.Select(value => value.EntityId),
                snapshot.FixedAttachmentPoses.Select(value => value.EntityId));
            var targetAbsentInitialBodyIds=NativeSceneMetadata.QualifyTargetAbsentInitialBodyIds(
                snapshot.FixedBodyPoses.Select(value => value.EntityId),
                snapshot.FixedAttachmentPoses.Select(value => value.EntityId));
            var ignoredAbsentFixedRows=warp.Entities.Where(value=>value!=null&&value.__isset.entityId
                &&targetAbsentInitialBodyIds.Contains(value.EntityId)).ToArray();
            foreach(var row in ignoredAbsentFixedRows)
                NativeInitialAttachmentMissingEntityValidator.ValidatePoseOnly(row,row.EntityId);
            TASLogicalButton.ValidateCheckpoint(snapshot.Input);
            NativeChefInteractionCheckpoint.Validate(snapshot.ChefInteractions);
            NativeCannonCheckpoint.Validate(snapshot.Cannons);
            NativeStationSyncCheckpoint.Validate(snapshot.StationSync);
            NativeAttachmentPoseCheckpoint.Snapshot[] survivingAttachments;
            NativeBodyPoseCheckpoint.Snapshot[] survivingBodies;
            var initialRecreation=NativeInitialAttachmentRecreation.Prepare(warp,snapshot.FixedAttachmentPoses,
                snapshot.FixedBodyPoses,snapshot.InitialAttachmentTopology,out survivingAttachments,out survivingBodies);
            NativeBodyPoseCheckpoint.ObserveStage(survivingBodies,"before-preflight-body-validation");
            NativeBodyPoseCheckpoint.Validate(survivingBodies);
            NativeAttachmentPoseCheckpoint.Validate(survivingAttachments);
            if(NativePlateLifecycle.LastObservationError.Length!=0)
                throw new InvalidOperationException("Native plate lifecycle observation is incomplete: "+NativePlateLifecycle.LastObservationError);
            if(snapshot.DeliveryFades != 0 || NativePlateLifecycle.PendingDeliveryFades != 0)
                throw new InvalidOperationException("Native delivered-plate fade is outside this authoring checkpoint scope.");
            var tables = new HashSet<int>();
            foreach (var order in snapshot.ClientOrders)
                if (order.Table < 0 || order.Table >= snapshot.Gui.GetOccupiedTables().Length || !tables.Add(order.Table)
                    || float.IsNaN(order.Progress) || float.IsInfinity(order.Progress))
                    throw new InvalidOperationException("Native checkpoint client order table/deadline is invalid.");
            var flows = warp.Entities.Where(e => e.KitchenController != null || e.PlateReturnController != null).ToArray();
            if (flows.Length != 1 || !flows[0].__isset.entityId || flows[0].EntityId != snapshot.FlowId
                || flows[0].KitchenController == null || flows[0].PlateReturnController == null
                || warp.EntitiesToDelete.Contains(snapshot.FlowId))
                throw new InvalidOperationException("Warp must include both native kitchen blocks on the same observed flow incarnation.");
            var ids = new HashSet<int>();
            var componentProblems = new List<string>();
            foreach (var spec in warp.Entities)
            {
                if (!spec.__isset.entityId) continue; // Spawn paths are checked by the existing authoring spawner.
                if (!ids.Add(spec.EntityId)) throw new InvalidOperationException("Duplicate warp entity " + spec.EntityId);
                var entry = EntitySerialisationRegistry.GetEntry((uint)spec.EntityId);
                if (entry == null || entry.m_GameObject == null) {
                    if(initialRecreation!=null&&initialRecreation.ValidateMissingFixedEntity(spec))continue;
                    if(ignoredAbsentFixedRows.Any(value=>ReferenceEquals(value,spec)))continue;
                    throw new InvalidOperationException("Warp entity is no longer registered: " + spec.EntityId);
                }
                ValidateComponentBlocks(entry.m_GameObject, spec, componentProblems);
            }
            // Also inspect omitted currently registered objects. The qualified
            // missing initial pair is absent from this list by construction and
            // its sole fixed container row was validated by its recreation plan.
            // Every unrelated absent fixed row still failed in the loop above.
            // A missing whole entity must
            // not silently omit its switch, attachment or plate-return state.
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries._items[i];
                int id = (int)entry.m_Header.m_uEntityID;
                if (entry.m_GameObject == null || ids.Contains(id) || warp.EntitiesToDelete.Contains(id)) continue;
                ValidateComponentBlocks(entry.m_GameObject, new EntityWarpSpec { EntityId = id }, componentProblems);
            }
            if (componentProblems.Count != 0)
                throw new InvalidOperationException("Warp component annotation mismatches: " + string.Join("; ", componentProblems.ToArray()));
            foreach (var pending in snapshot.Plates)
                if (pending.Station == null || EntitySerialisationRegistry.GetEntry((uint)pending.EntityId)?.m_GameObject != pending.Station.gameObject)
                    throw new InvalidOperationException("Native plate return station incarnation changed.");
            return new RestorePlan(snapshot,initialRecreation,ignoredAbsentFixedRows,restoreRoundLifecycle);
        }

        internal static NativeRoundEndLatch.Target RequireRoundEndTarget(int frame)
        {
            Snapshot snapshot;
            if (ambiguous.Contains(frame) || !history.TryGetValue(frame, out snapshot))
                throw new InvalidOperationException("No unambiguous native round lifecycle checkpoint at output frame " + frame);
            ValidateIdentity(snapshot, true);
            if (snapshot.RoundLifecycle == null)
                throw new InvalidOperationException("Native round lifecycle checkpoint is unavailable at output frame " + frame);
            return snapshot.RoundLifecycle;
        }

        internal static object CaptureNativeClockObservation(ServerKitchenFlowControllerBase flow,
            ClientKitchenFlowControllerBase client)
        {
            var serverTimer = flow == null ? null : flow.RoundTimer as ServerRoundTimer;
            var clientTimer = client == null ? null : client.RoundTimer as ClientRoundTimer;
            if (serverTimer == null || clientTimer == null)
                throw new InvalidOperationException("Native round-end clock observation requires both round timers.");
            var logical = (Dictionary<string, object>)UnrealTimePatch.Diagnostics();
            return new Dictionary<string, object> {
                { "nativeServerClock", ReadStatics(typeof(ServerTime), serverClockFields) },
                { "nativeClientClock", ReadStatics(typeof(ClientTime), clientClockFields) },
                { "source", UnrealTimePatch.CaptureLogicalRealtime() },
                { "ticks", logical["eligibleTicks"] }, { "step", logical["step"] },
                { "serverTimer", new Dictionary<string, object> {
                    { "elapsed", serverTimer.TimeElapsed }, { "timeLeft", (int)Field(serverTimer, "m_timeLeft") },
                    { "limit", (float)Field(serverTimer, "m_timeLimit") }, { "suppressed", serverTimer.IsSuppressed }
                } },
                { "clientTimer", new Dictionary<string, object> {
                    { "elapsed", clientTimer.TimeElapsed }, { "timeLeft", (int)Field(clientTimer, "m_timeLeft") },
                    { "limit", (float)Field(clientTimer, "m_timeLimit") }, { "suppressed", clientTimer.IsSuppressed }
                } }
            };
        }

        private static void ValidateComponentBlocks(GameObject obj, EntityWarpSpec spec, List<string> problems)
        {
            ValidateComponentBlocks(t => obj.GetComponent(t) != null, spec, problems);
        }

        internal static void ValidateComponentBlocks(Func<Type, bool> hasComponent, EntityWarpSpec spec, List<string> problems)
        {
            Action<bool, object, string, int> RequireBlock = (nativeComponent, block, name, entity) => {
                if (nativeComponent != (block != null))
                    problems.Add(entity + ":" + name + " native=" + nativeComponent + " block=" + (block != null));
            };
            RequireBlock(hasComponent(typeof(ServerAttachStation)), spec.AttachStation, "AttachStation", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerPlayerAttachmentCarrier)), spec.ChefCarry, "ChefCarry", spec.EntityId);
            RequireBlock(hasComponent(typeof(ClientPlayerControlsImpl_Default)), spec.Chef, "Chef", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerCannon)), spec.Cannon, "Cannon", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerWorkableItem)), spec.WorkableItem, "WorkableItem", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerThrowableItem)), spec.ThrowableItem, "ThrowableItem", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerTerminal)), spec.Terminal, "Terminal", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerPilotRotation)), spec.PilotRotation, "PilotRotation", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerIngredientContainer)), spec.IngredientContainer, "IngredientContainer", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerCookingHandler)), spec.CookingHandler, "CookingHandler", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerCookingStation)), spec.CookingStation, "CookingStation", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerMixingHandler)), spec.MixingHandler, "MixingHandler", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerPickupItemSwitcher)) || hasComponent(typeof(ServerPlacementItemSwitcher)), spec.PickupItemSwitcher, "PickupItemSwitcher", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerTriggerColourCycle)), spec.TriggerColourCycle, "TriggerColourCycle", spec.EntityId);
            RequireBlock(hasComponent(typeof(Stack)), spec.Stack, "Stack", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerPlateReturnStation)), spec.PlateReturnStation, "PlateReturnStation", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerWorkstation)), spec.Workstation, "Workstation", spec.EntityId);
            RequireBlock(hasComponent(typeof(ServerWashingStation)), spec.WashingStation, "WashingStation", spec.EntityId);
        }

        private static Snapshot Capture(ServerKitchenFlowControllerBase flow, int frame)
        {
            long started = Timestamp();
            try { return CaptureCore(flow, frame); }
            finally { RecordTiming(CaptureStage.CaptureTotal, started); }
        }

        private static Snapshot CaptureCore(ServerKitchenFlowControllerBase flow, int frame)
        {
            long started = Timestamp();
            var client = flow.GetComponent<ClientKitchenFlowControllerBase>();
            var serverTimer = flow.RoundTimer as ServerRoundTimer;
            var clientTimer = client == null ? null : client.RoundTimer as ClientRoundTimer;
            var serverMonitor = flow.GetMonitorForTeam(TeamID.One);
            var clientMonitor = client == null ? null : client.GetMonitorForTeam(TeamID.One);
            if (client == null || serverTimer == null || clientTimer == null || serverMonitor == null || clientMonitor == null)
                throw new InvalidOperationException("Native campaign kitchen clocks/monitors are incomplete.");
            var orders = serverMonitor.OrdersController;
            var clientOrders = clientMonitor.OrdersController;
            var wrapper = orders.GetRoundData() as WarpableRoundData;
            var round = orders.GetRoundInstanceData() as WarpableRoundInstanceData;
            if (wrapper == null || round == null) throw new InvalidOperationException("Native weighted round checkpoint is unavailable.");
            if (serverTimer.IsSuppressed || clientTimer.IsSuppressed)
                throw new InvalidOperationException("Suppressed round timers are outside the bounded InLevel checkpoint.");
            var entry = EntitySerialisationRegistry.GetEntry(flow.gameObject);
            if (entry == null) throw new InvalidOperationException("Native flow is not registered.");
            RecordTiming(CaptureStage.Prerequisites, started);
            started = Timestamp();
            var diagnostics = wrapper.GetDiagnostics(round);
            RecordTiming(CaptureStage.RoundDiagnostics, started);
            started = Timestamp();
            var fixedMembership = NativeSceneMetadata.CaptureCurrentInitialMembership();
            var snapshot = new Snapshot {
                Frame = frame, FlowId = (int)entry.m_Header.m_uEntityID, Flow = flow, Client = client,
                FlowInstanceId = flow.gameObject.GetInstanceID(), ServerTimer = serverTimer, ClientTimer = clientTimer,
                OrdersController = orders, ClientOrdersController = clientOrders, Wrapper = wrapper, Round = round,
                ServerElapsed = serverTimer.TimeElapsed, ClientElapsed = clientTimer.TimeElapsed,
                ServerTimeLeft = (int)Field(serverTimer, "m_timeLeft"), ClientTimeLeft = (int)Field(clientTimer, "m_timeLeft"),
                ServerLimit = (float)Field(serverTimer, "m_timeLimit"), ClientLimit = (float)Field(clientTimer, "m_timeLimit"),
                NextOrderId = (uint)Field(orders, "m_nextOrderID"), TimerUntilOrder = (float)Field(orders, "m_timerUntilOrder"),
                ComboIndex = (int)Field(orders, "m_comboIndex"), RoundIndex = diagnostics.nextIndex,
                Frequencies = (int[])diagnostics.cumulativeFrequencies.Clone(),
                ServerScore = CopyScore(serverMonitor.Score), ClientScore = CopyScore(clientMonitor.Score),
                AutoProgress = (bool)Field(orders, "m_autoProgress"), ServerOrderExpiration = orders.EnableOrderExpiration,
                ClientOrderExpiration = clientOrders.EnableOrderExpiration,
                LogicalRealtime = UnrealTimePatch.CaptureLogicalRealtime(), ExactClock = UnrealTimePatch.CaptureClock(), ServerClock = ReadStatics(typeof(ServerTime), serverClockFields),
                ClientClock = ReadStatics(typeof(ClientTime), clientClockFields), Input = TASLogicalButton.CaptureCheckpoint(),
                AmbientRandom = EndTiming(CaptureStage.BaseKitchenClockInput, ref started, UnityEngine.Random.state),
                Cannons = EndTiming(CaptureStage.Cannons, ref started, NativeCannonCheckpoint.CaptureAll()),
                DeliveryFades = NativePlateLifecycle.PendingDeliveryFades,
                ChefInteractions = EndTiming(CaptureStage.ChefInteractions, ref started, NativeChefInteractionCheckpoint.CaptureAll()),
                StationSync = EndTiming(CaptureStage.StationSync, ref started, NativeStationSyncCheckpoint.Capture()),
                FixedBodyPoses = EndTiming(CaptureStage.FixedBodyPoses, ref started, NativeBodyPoseCheckpoint.Capture(fixedMembership.RigidBodyIds)),
                FixedAttachmentPoses = EndTiming(CaptureStage.FixedAttachmentPoses, ref started, NativeAttachmentPoseCheckpoint.Capture(fixedMembership.PhysicalAttachmentIds))
            };
            snapshot.RoundLifecycle = NativeRoundEndLatch.Target.Capture(flow, client, frame);
            snapshot.Orders = ((List<ServerOrderData>)Field(orders, "m_activeOrders")).Select(CopyOrder).ToArray();
            snapshot.InitialAttachmentTopology=NativeInitialAttachmentRecreation.TopologySnapshot.Capture();
            snapshot.Plates = new List<PendingPlate>();
            var pending = flow.GetPlateReturnController().m_platesToReturn();
            for (int i = 0; i < pending.Count; i++)
            {
                var plate = pending[i];
                snapshot.Plates.Add(new PendingPlate { Station = plate.m_station, EntityId = (int)EntitySerialisationRegistry.GetId(plate.m_station.gameObject),
                    Timer = plate.m_timer, Step = plate.m_platingStepData });
            }
            snapshot.Gui = clientOrders.GetGUI();
            snapshot.GuiNextIndex = snapshot.Gui.GetNextIndex();
            snapshot.ClientOrders = new List<ClientOrder>();
            foreach (object item in (IList)Field(clientOrders, "m_activeOrders"))
            {
                var token = (RecipeFlowGUI.ElementToken)Field(item, "UIToken");
                var widget = (RecipeFlowGUI.RecipeWidgetData)Field(token, "m_widget");
                snapshot.ClientOrders.Add(new ClientOrder { Id = (OrderID)Field(item, "ID"),
                    Entry = CopyEntry((RecipeList.Entry)Field(item, "RecipeListEntry")),
                    Progress = widget.m_widget.GetTimePropRemaining(), TimeLimit = widget.m_timeLimit, Order = widget.m_order,
                    Table = (int)Field(widget.m_widget, "m_tableNumber") });
            }
            if (snapshot.ClientOrders.Count != snapshot.Orders.Length || snapshot.ClientOrders.Any(c => !snapshot.Orders.Any(s => s.ID == c.Id)))
                throw new InvalidOperationException("Native client/server orders differ at this boundary; checkpoint is not settled.");
            RecordTiming(CaptureStage.RemainingOrdersUi, started);
            return snapshot;
        }

        private static void ValidateIdentity(Snapshot snapshot, bool requireInLevel = true)
        {
            if (!ReferenceEquals(roundIdentity, snapshot.Round) || snapshot.Flow == null || snapshot.Client == null
                || snapshot.Flow.gameObject.GetInstanceID() != snapshot.FlowInstanceId
                || EntitySerialisationRegistry.GetEntry((uint)snapshot.FlowId)?.m_GameObject != snapshot.Flow.gameObject
                || !ReferenceEquals(snapshot.Flow.RoundTimer, snapshot.ServerTimer) || !ReferenceEquals(snapshot.Client.RoundTimer, snapshot.ClientTimer)
                || !ReferenceEquals(snapshot.OrdersController.GetRoundInstanceData(), snapshot.Round)
                || !ReferenceEquals(snapshot.OrdersController.GetRoundData(), snapshot.Wrapper)
                || !ReferenceEquals(snapshot.ClientOrdersController.GetGUI(), snapshot.Gui)
                || (requireInLevel && (GameState)Field(snapshot.Flow, "m_State") != GameState.InLevel))
                throw new InvalidOperationException("Native kitchen checkpoint belongs to another flow/round incarnation or phase.");
            if (snapshot.ServerTimer.IsSuppressed || snapshot.ClientTimer.IsSuppressed
                || (float)Field(snapshot.ServerTimer, "m_timeLimit") != snapshot.ServerLimit || (float)Field(snapshot.ClientTimer, "m_timeLimit") != snapshot.ClientLimit
                || (bool)Field(snapshot.OrdersController, "m_autoProgress") != snapshot.AutoProgress
                || snapshot.OrdersController.EnableOrderExpiration != snapshot.ServerOrderExpiration
                || snapshot.ClientOrdersController.EnableOrderExpiration != snapshot.ClientOrderExpiration)
                throw new InvalidOperationException("Native round configuration/suppression changed since checkpoint.");
        }

        private static bool SameGameplayBoundary(Snapshot a, Snapshot b)
        {
            long started = Timestamp();
            try { return SameGameplayBoundaryCore(a, b); }
            finally { RecordTiming(CaptureStage.SameGameplayBoundary, started); }
        }

        private static bool SameGameplayBoundaryCore(Snapshot a, Snapshot b)
        {
            if(a.DeliveryFades!=b.DeliveryFades || !NativeRoundEndLatch.Target.Same(a.RoundLifecycle,b.RoundLifecycle)
                || !NativeCannonCheckpoint.SameBoundary(a.Cannons,b.Cannons) || !NativeStationSyncCheckpoint.Same(a.StationSync,b.StationSync)
                || !NativeChefInteractionCheckpoint.SameBoundary(a.ChefInteractions,b.ChefInteractions)
                || !SameFixedMembership(a.FixedBodyPoses,b.FixedBodyPoses)
                || !SameFixedMembership(a.FixedAttachmentPoses,b.FixedAttachmentPoses)) return false;
            if (a.ServerElapsed != b.ServerElapsed || a.ClientElapsed != b.ClientElapsed || a.TimerUntilOrder != b.TimerUntilOrder
                || a.RoundIndex != b.RoundIndex || a.NextOrderId != b.NextOrderId || a.ComboIndex != b.ComboIndex
                || a.ServerTimeLeft != b.ServerTimeLeft || a.ClientTimeLeft != b.ClientTimeLeft
                || a.Orders.Length != b.Orders.Length || a.Plates.Count != b.Plates.Count
                || a.ClientOrders.Count != b.ClientOrders.Count
                || !a.Frequencies.SequenceEqual(b.Frequencies) || !SameScore(a.ServerScore, b.ServerScore) || !SameScore(a.ClientScore, b.ClientScore)) return false;
            for (int i = 0; i < a.Orders.Length; i++)
                if (a.Orders[i].ID != b.Orders[i].ID || a.Orders[i].Remaining != b.Orders[i].Remaining || a.Orders[i].Lifetime != b.Orders[i].Lifetime
                    || a.Orders[i].RecipeListEntry.m_order != b.Orders[i].RecipeListEntry.m_order) return false;
            for (int i = 0; i < a.Plates.Count; i++)
                if (a.Plates[i].Station != b.Plates[i].Station || a.Plates[i].Timer != b.Plates[i].Timer || a.Plates[i].Step != b.Plates[i].Step) return false;
            for (int i = 0; i < a.ClientOrders.Count; i++)
                if (a.ClientOrders[i].Id != b.ClientOrders[i].Id || a.ClientOrders[i].Progress != b.ClientOrders[i].Progress
                    || a.ClientOrders[i].TimeLimit != b.ClientOrders[i].TimeLimit || a.ClientOrders[i].Table != b.ClientOrders[i].Table) return false;
            return true;
        }

        private static bool SameFixedMembership(NativeBodyPoseCheckpoint.Snapshot[] a,
            NativeBodyPoseCheckpoint.Snapshot[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] == null || b[i] == null || a[i].EntityId != b[i].EntityId
                    || !ReferenceEquals(a[i].Entry, b[i].Entry)) return false;
            return true;
        }

        private static bool SameFixedMembership(NativeAttachmentPoseCheckpoint.Snapshot[] a,
            NativeAttachmentPoseCheckpoint.Snapshot[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
                if (a[i] == null || b[i] == null || a[i].EntityId != b[i].EntityId
                    || !ReferenceEquals(a[i].Entry, b[i].Entry)) return false;
            return true;
        }

        public sealed class RestorePlan
        {
            private readonly Snapshot snapshot;
            private readonly NativeInitialAttachmentRecreation initialRecreation;
            private readonly EntityWarpSpec[] ignoredAbsentFixedRows;
            private readonly bool restoreRoundLifecycle;
            internal RestorePlan(Snapshot value,NativeInitialAttachmentRecreation recreation,
                EntityWarpSpec[] ignoredRows,bool restoreLifecycle)
            {
                snapshot=value;initialRecreation=recreation;
                ignoredAbsentFixedRows=ignoredRows??new EntityWarpSpec[0];
                restoreRoundLifecycle=restoreLifecycle;
            }
            internal bool IgnoreAbsentFixedEntity(EntityWarpSpec spec)
            {return ignoredAbsentFixedRows.Any(value=>ReferenceEquals(value,spec));}
            internal EntityPathReference ResolveMissingFixedEntityOwnerPath(EntityWarpSpec spec)
            {
                return initialRecreation==null?null:initialRecreation.ResolveMissingFixedEntityOwnerPath(spec);
            }
            internal NativeInitialAttachmentSpawnChain ResolveLatentInitialAttachmentFactory(
                EntityWarpSpec spec,EntitySerialisationEntry root)
            {
                return initialRecreation==null?null:initialRecreation.ResolveLatentSpawnChain(spec,root);
            }
            internal void RebindRecreatedInitialAttachments(Dictionary<EntityPathReference,EntitySerialisationEntry> references)
            {
                if(initialRecreation!=null)initialRecreation.Rebind(references,snapshot.FixedAttachmentPoses,snapshot.FixedBodyPoses);
            }
            public void ObserveBodyStage(string stage) { NativeBodyPoseCheckpoint.ObserveStage(snapshot.FixedBodyPoses,stage); }
            public int FlowId { get { return snapshot.FlowId; } }
            public KitchenControllerWarpData KitchenData { get { return new KitchenControllerWarpData {
                RoundTime = snapshot.ServerElapsed, NextOrderId = snapshot.RoundIndex, LastComboIndex = snapshot.ComboIndex,
                ActiveOrders = snapshot.Orders.Select(o => o.ToBytes()).ToList(), TeamScore = snapshot.ServerScore.ToBytes()
            }; } }
            public void RestoreLifecycleBeforeResume()
            {
                if (restoreRoundLifecycle) NativeRoundEndLatch.Restore(snapshot.RoundLifecycle);
            }
            public void RestoreClocks()
            {
                ValidateIdentity(snapshot);
                UnrealTimePatch.RestoreClock(snapshot.ExactClock);
                WriteStatics(typeof(ServerTime), serverClockFields, snapshot.ServerClock);
                WriteStatics(typeof(ClientTime), clientClockFields, snapshot.ClientClock);
                UnityEngine.Random.state = snapshot.AmbientRandom;
            }
            public void RestoreFlow(ServerKitchenFlowControllerBase flow)
            {
                if (!ReferenceEquals(flow, snapshot.Flow)) throw new InvalidOperationException("Wrong native flow for authoring restore.");
                var pending = flow.GetPlateReturnController().m_platesToReturn();
                pending.Clear();
                foreach (var plate in snapshot.Plates) pending.Add(PlateReturnController_PlatesPendingReturnExt.Create(plate.Station, plate.Timer, plate.Step));
                snapshot.ServerTimer.SetRoundTimer(snapshot.ServerElapsed);
                snapshot.ClientTimer.SetRoundTimer(snapshot.ClientElapsed);
                Set(snapshot.ServerTimer, "m_timeLeft", snapshot.ServerTimeLeft);
                Set(snapshot.ClientTimer, "m_timeLeft", snapshot.ClientTimeLeft);
                var server = flow.GetMonitorForTeam(TeamID.One);
                var client = snapshot.Client.GetMonitorForTeam(TeamID.One);
                server.SetScore(CopyScore(snapshot.ServerScore)); client.SetScore(CopyScore(snapshot.ClientScore));
                snapshot.Client.GetDataStore().Write(new DataStore.Id("score.team"), new TeamScore { m_team = TeamID.One, m_score = CopyScore(snapshot.ClientScore) });
                snapshot.OrdersController.SetNextOrderID(snapshot.NextOrderId);
                snapshot.OrdersController.SetTimerUntilOrder(snapshot.TimerUntilOrder);
                snapshot.OrdersController.SetComboIndex(snapshot.ComboIndex);
                snapshot.OrdersController.SetActiveOrders(snapshot.Orders.Select(CopyOrder).ToList());
                snapshot.Wrapper.Warp(snapshot.Round, snapshot.RoundIndex);
                Console.WriteLine("[WARP] Native kitchen restored outputFrame=" + snapshot.Frame + " elapsed=" + snapshot.ServerElapsed.ToString("R")
                    + " clientElapsed=" + snapshot.ClientElapsed.ToString("R") + " recipeCursor=" + snapshot.RoundIndex + " nextOrderId=" + snapshot.NextOrderId);
            }
            public void RestoreClientPresentation()
            {
                var widgets = snapshot.Gui.GetActiveWidgets();
                if (widgets.Count != snapshot.Orders.Length) throw new InvalidOperationException("Restored native client order widgets are incomplete.");
                var occupied = snapshot.Gui.GetOccupiedTables();
                Array.Clear(occupied, 0, occupied.Length);
                for (int i = 0; i < snapshot.Orders.Length; i++)
                {
                    var saved = snapshot.ClientOrders.Single(c => c.Id == snapshot.Orders[i].ID);
                    if (saved.Table < 0 || saved.Table >= occupied.Length || occupied[saved.Table])
                        throw new InvalidOperationException("Native checkpoint has invalid client order table ownership.");
                    widgets[i].m_timeLimit = saved.TimeLimit; widgets[i].m_order = saved.Order;
                    widgets[i].m_widget.SetTableNumber(saved.Table);
                    widgets[i].m_widget.SetTimePropRemaining(saved.Progress);
                    occupied[saved.Table] = true;
                }
                snapshot.Gui.SetNextIndex(snapshot.GuiNextIndex);
                snapshot.Gui.LayoutWidgets();
            }
            public void Complete()
            {
                // The dynamic plan has now synchronously unregistered every
                // scheduler-authorized future attachment pair. Only at this
                // boundary can the recreated historical pair be placed back
                // into the checkpoint's exact canonical-list ordering.
                if(initialRecreation!=null)initialRecreation.FinalizeTopologyAfterDeletions();
                ValidateIdentity(snapshot);
                NativeCannonCheckpoint.Restore(snapshot.Cannons);
                NativeCannonCheckpoint.VerifyRestored(snapshot.Cannons);
                NativeStationSyncCheckpoint.Restore(snapshot.StationSync);
                ObserveBodyStage("before-final-attachment-pose-restore");
                NativeAttachmentPoseCheckpoint.Restore(snapshot.FixedAttachmentPoses);
                ObserveBodyStage("after-final-attachment-pose-restore");
                NativeAttachmentPoseCheckpoint.VerifyRestored(snapshot.FixedAttachmentPoses);
                NativeBodyPoseCheckpoint.Restore(snapshot.FixedBodyPoses);
                ObserveBodyStage("after-final-body-pose-restore");
                NativeBodyPoseCheckpoint.VerifyRestored(snapshot.FixedBodyPoses);
                if(initialRecreation!=null)initialRecreation.CommitLineage();
                NativeSceneMetadata.RequireCurrentInitialMembershipExact(
                    snapshot.FixedBodyPoses.Select(value => value.EntityId),
                    snapshot.FixedAttachmentPoses.Select(value => value.EntityId));
                TASLogicalButton.RestoreCheckpoint(snapshot.Input);
                NativeChefInteractionCheckpoint.Restore(snapshot.ChefInteractions);
                NativeChefInteractionCheckpoint.VerifyRestored(snapshot.ChefInteractions);
                UnityEngine.Random.state = snapshot.AmbientRandom;
                var observed = snapshot.Wrapper.GetDiagnostics(snapshot.Round);
                if (snapshot.ServerTimer.TimeElapsed != snapshot.ServerElapsed || snapshot.ClientTimer.TimeElapsed != snapshot.ClientElapsed
                    || observed.nextIndex != snapshot.RoundIndex || !observed.cumulativeFrequencies.SequenceEqual(snapshot.Frequencies)
                    || (float)Field(snapshot.OrdersController, "m_timerUntilOrder") != snapshot.TimerUntilOrder
                    || (uint)Field(snapshot.OrdersController, "m_nextOrderID") != snapshot.NextOrderId
                    || !SameScore(snapshot.Flow.GetMonitorForTeam(TeamID.One).Score, snapshot.ServerScore)
                    || !SameScore(snapshot.Client.GetMonitorForTeam(TeamID.One).Score, snapshot.ClientScore))
                    throw new InvalidOperationException("Native authoring kitchen restoration postcondition failed.");
                if (!SameGameplayBoundary(snapshot, Capture(snapshot.Flow, snapshot.Frame))
                    || !ReadStatics(typeof(ServerTime), serverClockFields).SequenceEqual(snapshot.ServerClock)
                    || !ReadStatics(typeof(ClientTime), clientClockFields).SequenceEqual(snapshot.ClientClock))
                    throw new InvalidOperationException("Native order, plate-return, client deadline or global clock restoration differs from checkpoint.");
                foreach (var frame in history.Keys.Where(f => f > snapshot.Frame).ToArray()) history.Remove(frame);
                ambiguous.RemoveWhere(f => f > snapshot.Frame);
                lastFrame = snapshot.Frame;
                lastRestore = new Dictionary<string, object> {
                    { "frame", snapshot.Frame }, { "attempt", restoreAttempts }, { "flowId", snapshot.FlowId },
                    { "serverElapsed", snapshot.ServerElapsed }, { "clientElapsed", snapshot.ClientElapsed },
                    { "roundIndex", snapshot.RoundIndex }, { "nativeNextOrderId", snapshot.NextOrderId },
                    { "timerUntilOrder", snapshot.TimerUntilOrder }, { "serverScore", snapshot.ServerScore.GetTotalScore() },
                    { "nativeCannonCount", snapshot.Cannons.Length }, { "nativeCannonsSettled", snapshot.Cannons.All(c => c.Settled) },
                    { "nativeSteadyFlightCount", snapshot.Cannons.Count(c => c.Flight != null) },
                    { "nativeChefInteractionCount", snapshot.ChefInteractions.Length }, { "nativePickupAndUseStateRestored", true },
                    { "nativeFixedBodyPoseCount", snapshot.FixedBodyPoses.Length }, { "nativeBodyAndTransformPosesRestored", true },
                    { "nativeFixedAttachmentPoseCount", snapshot.FixedAttachmentPoses.Length }, { "nativeLogicalAttachmentPosesRestored", true },
                    { "ignoredAbsentInitialBodyIds", ignoredAbsentFixedRows.Select(value=>value.EntityId).ToArray() },
                    { "nativeInitialAttachmentRecreation", initialRecreation==null?null:initialRecreation.Diagnostics },
                    { "verified", true }, { "qualification", "Native kitchen/input authoring checkpoint only; entity physics and complete TAS replay are unverified." }
                };
            }

            public void RestoreFixedBodyPoses()
            {
                ObserveBodyStage("before-early-attachment-pose-restore");
                NativeAttachmentPoseCheckpoint.Restore(snapshot.FixedAttachmentPoses);
                if(initialRecreation!=null)initialRecreation.FinalizeColliders(snapshot.FixedBodyPoses);
                ObserveBodyStage("after-early-attachment-pose-restore");
                NativeBodyPoseCheckpoint.Restore(snapshot.FixedBodyPoses);
                ObserveBodyStage("after-early-body-pose-restore");
            }
        }

        private static object Field(object instance, string name)
        {
            var field = AccessTools.Field(instance.GetType(), name);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
            return field.GetValue(instance);
        }
        private static void Set(object instance, string name, object value) { AccessTools.Field(instance.GetType(), name).SetValue(instance, value); }
        private static float[] ReadStatics(Type type, string[] names) { return names.Select(n => (float)AccessTools.Field(type, n).GetValue(null)).ToArray(); }
        private static void WriteStatics(Type type, string[] names, float[] values) { for (int i = 0; i < names.Length; i++) AccessTools.Field(type, names[i]).SetValue(null, values[i]); }
        private static TeamMonitor.TeamScoreStats CopyScore(TeamMonitor.TeamScoreStats source) { var copy = new TeamMonitor.TeamScoreStats(); copy.Copy(source); return copy; }
        private static bool SameScore(TeamMonitor.TeamScoreStats a, TeamMonitor.TeamScoreStats b) { return a.TotalBaseScore == b.TotalBaseScore && a.TotalTipsScore == b.TotalTipsScore
            && a.TotalMultiplier == b.TotalMultiplier && a.TotalCombo == b.TotalCombo && a.TotalTimeExpireDeductions == b.TotalTimeExpireDeductions
            && a.ComboMaintained == b.ComboMaintained && a.TotalSuccessfulDeliveries == b.TotalSuccessfulDeliveries; }
        private static RecipeList.Entry CopyEntry(RecipeList.Entry source) { var copy = new RecipeList.Entry(); copy.Copy(source); return copy; }
        private static ServerOrderData CopyOrder(ServerOrderData source) { return new ServerOrderData(source.ID, CopyEntry(source.RecipeListEntry), source.Lifetime) { Remaining = source.Remaining }; }
        internal sealed class PendingPlate { internal ServerPlateReturnStation Station; internal int EntityId; internal float Timer; internal PlatingStepData Step; }
        internal sealed class ClientOrder { internal OrderID Id; internal RecipeList.Entry Entry; internal float Progress, TimeLimit; internal int Order, Table; }
        internal sealed class Snapshot
        {
            internal int Frame, FlowId, FlowInstanceId, ServerTimeLeft, ClientTimeLeft, ComboIndex, RoundIndex, GuiNextIndex;
            internal uint NextOrderId;
            internal float ServerElapsed, ClientElapsed, ServerLimit, ClientLimit, TimerUntilOrder, LogicalRealtime;
            internal bool AutoProgress, ServerOrderExpiration, ClientOrderExpiration;
            internal float[] ServerClock, ClientClock;
            internal int[] Frequencies;
            internal ServerKitchenFlowControllerBase Flow;
            internal ClientKitchenFlowControllerBase Client;
            internal ServerRoundTimer ServerTimer;
            internal ClientRoundTimer ClientTimer;
            internal ServerOrderControllerBase OrdersController;
            internal ClientOrderControllerBase ClientOrdersController;
            internal WarpableRoundData Wrapper;
            internal WarpableRoundInstanceData Round;
            internal TeamMonitor.TeamScoreStats ServerScore, ClientScore;
            internal ServerOrderData[] Orders;
            internal List<PendingPlate> Plates;
            internal List<ClientOrder> ClientOrders;
            internal RecipeFlowGUI Gui;
            internal TASLogicalButton.InputCheckpoint Input;
            internal UnityEngine.Random.State AmbientRandom;
            internal NativeCannonCheckpoint.Snapshot[] Cannons;
            internal NativeStationSyncCheckpoint.State[] StationSync;
            internal NativeBodyPoseCheckpoint.Snapshot[] FixedBodyPoses;
            internal NativeAttachmentPoseCheckpoint.Snapshot[] FixedAttachmentPoses;
            internal NativeInitialAttachmentRecreation.TopologySnapshot InitialAttachmentTopology;
            internal AuthoringClockState.Checkpoint ExactClock;
            internal int DeliveryFades;
            internal NativeChefInteractionCheckpoint.Snapshot[] ChefInteractions;
            internal NativeRoundEndLatch.Target RoundLifecycle;
        }
    }
}

