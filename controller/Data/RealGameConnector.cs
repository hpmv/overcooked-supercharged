using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv
{
    class RealGameConnector : Interceptor.IAsync
    {
        public readonly FramerateController FramerateController = new FramerateController { Framerate = Config.FRAMERATE };
        private GameSetup setup;
        public RealGameSimulator simulator = new RealGameSimulator();
        public readonly HashSet<GameEntityRecord> ObservedPhysicsContainers = new HashSet<GameEntityRecord>();
        internal void ObservePhysicsContainer(EntityRegistryData metadata)
        {
            if (!simulator.entityIdToRecord.TryGetValue(metadata.EntityId, out var record) || record.path.ids.Length != 1) return;
            if (metadata.Components != null && metadata.Components.Contains("Rigidbody") && metadata.Components.Contains("ObjectContainer") &&
                metadata.SyncEntityTypes != null && metadata.SyncEntityTypes.Contains((int)EntityType.PhysicsObject)) ObservedPhysicsContainers.Add(record);
            else ObservedPhysicsContainers.Remove(record);
        }

        // Native attachments/progress can precede InLevel and their first schema.
        // Retain raw callbacks, then replay at frame zero without simulating load time.
        private readonly List<OutputData> startupObservations = new List<OutputData>();
        private long startupMessageBytes;
        private int startupEntries;
        private const int MaxStartupCallbacks = 16384;
        private const int MaxStartupEntries = 262144;
        private const long MaxStartupMessageBytes = 64L * 1024 * 1024;

        // Queue of state transition requests; enqueued by the UI thread, dequeued by the RPC handling thread.
        private ConcurrentQueue<RealGameStateRequest> requests = new ConcurrentQueue<RealGameStateRequest>();
        // Current request that is being processed by the RPC handling thread; only used by RPC handling thread.
        private RealGameStateRequest currentRequest;
        private const double NativeResumePhaseMetadataBase = 1000.0;
        public long ResumePhaseMetadataEmissions { get; private set; }

        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        private Connector connector;

        public RealGameState State { get; set; } = RealGameState.NotInLevel;

        public event Action OnFrameUpdate;
        public event Action OnConnectionTerminated;
        public event Action OnStateChanged;
        // Optional observer adapter for physical proxy IDs which the original
        // model intentionally does not instantiate as logical entities.
        internal Func<int, int, bool> TryHandleUnmappedRetirement;

        public async void Start()
        {
            connector = new Connector(this);
            while (!cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await connector.Connect(cancellationTokenSource.Token);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Connection error: {0}", e);
                }
                if (this.State != RealGameState.Paused)
                {
                    break;
                }
                Console.WriteLine("Reestablishing connection");
            }
            OnConnectionTerminated?.Invoke();
        }

        public void Stop()
        {
            cancellationTokenSource.Cancel();
        }

        public RealGameConnector(GameSetup level)
        {
            this.setup = level;

            simulator.Graph = setup.sequences.ToGraph();
            simulator.setup = setup;

            FakeEntityRegistry.entityToTypes.Clear(); // sigh.
        }

        private void RestartLevel()
        {
            startupObservations.Clear();
            startupMessageBytes = 0;
            startupEntries = 0;
            simulator.Reset();
            ObservedPhysicsContainers.Clear();
            State = RealGameState.AwaitingStart;
            OnFrameUpdate?.Invoke();
        }

        public Task<InputData> getNext(OutputData output, CancellationToken cancellationToken = default)
        {
            try
            {
                return Task.FromResult(getNextImpl(output));
            }
            catch (Exception e)
            {
                Console.WriteLine($"Exception in getNext: {e}");
                throw;
            }
        }

        private InputData getNextImpl(OutputData output)
        {
            var startTime = DateTime.Now;
            InputData inputs = new InputData();
            int firstCurrentLevelMessage = 0;
            // Reset before applying this callback's registry, which can arrive
            // with the load. Discard messages before the latest load in a packet:
            // they belong to the old reconstruction epoch.
            if (output.ServerMessages != null)
            {
                for (int i = 0; i < output.ServerMessages.Count; i++)
                {
                    var msg = output.ServerMessages[i];
                    if (msg.Type != (int)MessageType.LevelLoadByIndex && msg.Type != (int)MessageType.LevelLoadByName) continue;
                    var load = Deserializer.Deserialize(msg.Type, msg.Message);
                    if (!(load is LevelLoadByIndexMessage) && !(load is LevelLoadByNameMessage))
                        throw new InvalidOperationException("Native level-load message could not be decoded.");
                    firstCurrentLevelMessage = i + 1;
                }
            }
            if (firstCurrentLevelMessage > 0)
            {
                RestartLevel();
                inputs.ResetOrderSeed = 12347;
            }
            // We do this step first because this is needed to deserialize further game messages.
            // See FakeEntityRegistry for more information.
            //
            // We do this even before the first frame, because these messages are constructed by intercepting
            // StartSynchronisingEntry, which happens before the level timer begins. We need to keep applying
            // these in the middle of the level, because these are needed for newly constructed entities.
            if (State != RealGameState.NotInLevel)
            {
                if (output.EntityRegistry != null)
                {
                    foreach (var entity in output.EntityRegistry)
                    {
                        simulator.ApplyEntityRegistryUpdateEarly(entity);
                        ObservePhysicsContainer(entity);
                    }
                }
            }

            var time1 = DateTime.Now;
            // First handle major game state transitions.
            var didGameJustStart = false;
            if (output.ServerMessages != null)
            {
                for (int i = firstCurrentLevelMessage; i < output.ServerMessages.Count; i++)
                {
                    var msg = output.ServerMessages[i];
                    // Entity packets may still be awaiting a schema. Decode
                    // only control messages until startup replay below.
                    if (msg.Type == (int)MessageType.GameState && Deserializer.Deserialize(msg.Type, msg.Message) is GameStateMessage gsm)
                    {
                        Console.WriteLine($"Transitioned to {gsm.m_State}");
                        if (gsm.m_State == GameState.InLevel && State == RealGameState.AwaitingStart)
                        {
                            State = RealGameState.Running;
                            didGameJustStart = true;
                        }
                    }
                }
            }

            if (State == RealGameState.AwaitingStart)
                BufferStartupObservation(output, firstCurrentLevelMessage);
            if (didGameJustStart)
                ApplyStartupObservations(output);

            var time2 = DateTime.Now;
            // For both Running and AwaitingPause, the logical game state advanced by one frame
            // so we need to do the same.
            if (State == RealGameState.Running || State == RealGameState.AwaitingPause)
            {
                // Don't advance frame if we just started running. This is a special case.
                if (!didGameJustStart)
                {
                    simulator.AdvanceFrame();
                    simulator.AdvanceAutomaticProgress();
                    setup.LastEmpiricalFrame = simulator.Frame;
                    simulator.ApplyInvalidGameState(output.InvalidStateReason);
                }
                ApplyObservedServerMessages(output, firstCurrentLevelMessage);
                simulator.ApplyPhysicsPhaseShift(output.FramesSinceLastNoPhysicsFrame);

                if (output.EntityRegistry != null)
                {
                    foreach (var entity in output.EntityRegistry)
                    {
                        // TODO: what does this do exactly?
                        simulator.ApplyEntityRegistryUpdateLate(entity);
                    }
                }
                if (output.Chefs != null)
                {
                    foreach (var entry in output.Chefs)
                    {
                        // Console.WriteLine(new {entry.Key, entry.Value});
                        simulator.ApplyChefUpdate(entry.Key, entry.Value);
                    }
                }
                if (output.Items != null)
                {
                    foreach (var entry in output.Items)
                    {
                        simulator.ApplyPositionUpdate(entry.Key, entry.Value);
                    }
                }
            }

            // Native messages can finish arriving on frame zero or while the
            // bridge is already paused. Apply each observed message once at the
            // current frame; neither case advances automatic progress. Warping
            // retains its separate ApplyGameUpdateWhenWarping path below.
            if (State == RealGameState.Paused || State == RealGameState.AwaitingPhysicsPhaseShiftAlignment ||
                State == RealGameState.AwaitingResume)
            {
                ApplyObservedServerMessages(output, firstCurrentLevelMessage);
                // Native ItemData is a delta stream. A full initial refresh or
                // a pose change can arrive after the bridge has paused; dropping
                // it leaves a setup default until that field changes again.
                // Observe at this boundary without simulating another frame.
                // Warping remains on its verified-acknowledgement path below.
                if (output.Items != null)
                    foreach (var entry in output.Items) simulator.ApplyPositionUpdate(entry.Key, entry.Value);
            }

            var time3 = DateTime.Now;

            // For both Running and AwaitingResume, the *next* frame will need inputs, so compute
            // inputs.
            if (State == RealGameState.Running || State == RealGameState.AwaitingResume)
            {
                if (simulator.Frame == 0)
                {
                    // Suppress inputs for the first frame, as the game doesn't seem to be able to
                    // process it yet.
                    inputs.Input = new Dictionary<int, OneInputData>();
                    foreach (var chef in setup.entityRecords.Chefs)
                    {
                        inputs.Input[chef.Key.path.ids[0]] = new OneInputData();
                    }
                }
                else
                {
                    // TODO: this also computes whether current actions have ended. If we pause right
                    // when an action is done, we would not be marking the current action as done
                    // until we resume again. Is that an issue?
                    inputs.Input = simulator.ComputeInputForNextFrame();
                }
                inputs.NextFrame = simulator.Frame + 1;
            }
            else
            {
                inputs.NextFrame = simulator.Frame;
            }
            inputs.PreventInvalidState = setup.PreventInvalidState;

            var time4 = DateTime.Now;
            if (State == RealGameState.Warping)
            {
                var entityIdToPathWhenWarping = new Dictionary<int, EntityPath>();
                if (State == RealGameState.Warping)
                {
                    foreach (var entry in output.Items)
                    {
                        if (entry.Value.__isset.entityPathReference)
                        {
                            var path = entry.Value.EntityPathReference.FromThrift();
                            entityIdToPathWhenWarping[entry.Key] = path;
                        }
                    }
                }
                if (output.ServerMessages != null)
                {
                    foreach (var msg in output.ServerMessages)
                    {
                        var item = Deserializer.Deserialize(msg.Type, msg.Message);
                        simulator.ApplyGameUpdateWhenWarping(item, entityIdToPathWhenWarping);
                    }
                }
                simulator.SetFrameAfterWarping(output.FrameNumber);
            }


            var time5 = DateTime.Now;
            // If we advanced logical frame earlier, notify the UI of the new frame. We don't do this
            // earlier because we may have called ComputeInputForNextFrame in the middle and that
            // could affect start and end times of actions that affects UI layout.
            if (State == RealGameState.Running || State == RealGameState.AwaitingPause)
            {
                OnFrameUpdate?.Invoke();
            }


            var time6 = DateTime.Now;
            // Finally, handle user requests to pause, resume, or warp.
            if (currentRequest != null)
            {
                switch (currentRequest.Kind)
                {
                    case RealGameStateRequestKind.Pause:
                        if (State != RealGameState.AwaitingPause)
                        {
                            Console.WriteLine($"Unexpected state when handling pause; should be AwaitingPause; actual state: {State}");
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        else if (output.NextFramePaused)
                        {
                            State = RealGameState.Paused;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        }
                        else
                        {
                            Console.WriteLine($"Unexpected game state when pausing; game is not paused but should be");
                            State = RealGameState.Error;
                        }
                        break;
                    case RealGameStateRequestKind.Resume:
                        if (State == RealGameState.AwaitingPhysicsPhaseShiftAlignment)
                        {
                            int targetPhase = setup.entityRecords.PhysicsPhaseShift[simulator.Frame];
                            if (targetPhase < 0 || targetPhase >= 6)
                                throw new InvalidOperationException("Saved native resume phase is outside the six-phase scheduler.");
                            inputs.RequestResume = true;
                            // GameSpeed is unused by the frozen game/patch. In
                            // this one plain-resume envelope it is a wire-stable
                            // metadata carrier for the active ResumePhase
                            // module, which consumes the exact saved phase and
                            // holds native pause until that phase is observed.
                            inputs.GameSpeed = NativeResumePhaseMetadataBase + targetPhase;
                            ResumePhaseMetadataEmissions++;
                            State = RealGameState.AwaitingResume;
                            simulator.ClearHistoryBeforeSimulation();
                        }
                        else if (State != RealGameState.AwaitingResume)
                        {
                            Console.WriteLine($"Unexpected state when handling resume; should be AwaitingResume; actual state: {State}");
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        else if (!output.NextFramePaused)
                        {
                            State = RealGameState.Running;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        }
                        else
                        {
                            Console.WriteLine($"Unexpected game state when resuming; game is paused but should not be");
                            State = RealGameState.Error;
                        }
                        break;
                    case RealGameStateRequestKind.Warp:
                        if (State != RealGameState.Warping)
                        {
                            Console.WriteLine($"Unexpected state when handling warp; should be Warping; actual state: {State}");
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        else if (!string.IsNullOrEmpty(output.InvalidStateReason) && output.InvalidStateReason.StartsWith("AUTHORING_WARP_FAILED:", StringComparison.Ordinal))
                        {
                            simulator.ApplyInvalidGameState(output.InvalidStateReason);
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        else if (output.FrameNumber == currentRequest.FrameToWarpTo)
                        {
                            // A verified native rewind starts a new authoring
                            // branch now, before any following paused messages
                            // can write at the restored frame. Waiting until
                            // Resume left future Versioned entries in the way.
                            simulator.ClearHistoryBeforeSimulation();
                            ApplyObservedServerMessages(output, firstCurrentLevelMessage, restoredSnapshot: true);
                            if (output.EntityRegistry != null)
                                foreach (var entity in output.EntityRegistry) simulator.ApplyEntityRegistryUpdateLate(entity);
                            if (output.Chefs != null)
                                foreach (var entry in output.Chefs) simulator.ApplyChefUpdate(entry.Key, entry.Value);
                            if (output.Items != null)
                                foreach (var entry in output.Items) simulator.ApplyPositionUpdate(entry.Key, entry.Value);
                            inputs.NextFrame = simulator.Frame;
                            State = RealGameState.Paused;
                            currentRequest.Completed.SetResult(true);
                            currentRequest = null;
                        }
                        else
                        {
                            Console.WriteLine($"Unexpected game state when warping; game frame {output.FrameNumber} is not equal to the requested frame {currentRequest.FrameToWarpTo}");
                            currentRequest.Completed.SetResult(false);
                            currentRequest = null;
                            State = RealGameState.Error;
                        }
                        break;
                }
            }
            else if (!didGameJustStart)
            {
                RealGameStateRequest request;
                if (requests.TryDequeue(out request))
                {
                    currentRequest = request;
                    switch (currentRequest.Kind)
                    {
                        case RealGameStateRequestKind.Pause:
                            if (State == RealGameState.Running)
                            {
                                inputs.RequestPause = true;
                                State = RealGameState.AwaitingPause;
                            }
                            else
                            {
                                currentRequest.Completed.SetResult(false);
                                currentRequest = null;
                            }
                            break;
                        case RealGameStateRequestKind.Resume:
                            if (State == RealGameState.Paused)
                            {
                                Console.WriteLine($"Requesting resume from frame {simulator.Frame}");
                                State = RealGameState.AwaitingPhysicsPhaseShiftAlignment;
                            }
                            else
                            {
                                currentRequest.Completed.SetResult(false);
                                currentRequest = null;
                            }
                            break;
                        case RealGameStateRequestKind.Warp:
                            if (State == RealGameState.Paused)
                            {
                                inputs.Warp = WarpCalculator.CalculateWarp(simulator.entityIdToRecord, setup.entityRecords, simulator.Frame, request.FrameToWarpTo, ObservedPhysicsContainers);
                                State = RealGameState.Warping;
                            }
                            else
                            {
                                currentRequest.Completed.SetResult(false);
                                currentRequest = null;
                            }
                            break;
                    }
                }
            }

            var time7 = DateTime.Now;
            OnStateChanged?.Invoke();
            var endTime = DateTime.Now;
            // Console.WriteLine($"getNext took {(endTime - startTime).TotalMilliseconds} ms: time1: {(time1 - startTime).TotalMilliseconds} ms, time2: {(time2 - time1).TotalMilliseconds} ms, time3: {(time3 - time2).TotalMilliseconds} ms, time4: {(time4 - time3).TotalMilliseconds} ms, time5: {(time5 - time4).TotalMilliseconds} ms, time6: {(time6 - time5).TotalMilliseconds} ms, time7: {(time7 - time6).TotalMilliseconds} ms, time8: {(endTime - time7).TotalMilliseconds} ms");

            FramerateController.WaitTillNextFrame();
            return inputs;
        }

        private void BufferStartupObservation(OutputData output, int firstMessage)
        {
            long bytes = 0;
            int messages = output.ServerMessages == null ? 0 : output.ServerMessages.Count - firstMessage;
            if (output.ServerMessages != null)
                for (int i = firstMessage; i < output.ServerMessages.Count; i++)
                    bytes += output.ServerMessages[i].Message?.Length ?? 0;
            int entries = messages + (output.EntityRegistry?.Count ?? 0) + (output.Chefs?.Count ?? 0) + (output.Items?.Count ?? 0);
            if (startupObservations.Count >= MaxStartupCallbacks || startupEntries + (long)entries > MaxStartupEntries ||
                startupMessageBytes + bytes > MaxStartupMessageBytes)
                throw new InvalidOperationException("Native startup observation buffer exceeded its finite limit before InLevel.");
            // The RPC caller can reuse its OutputData. Own the raw bytes and all
            // optional-field flags until this callback has been replayed once.
            var copy = output.DeepCopy();
            if (firstMessage > 0 && copy.ServerMessages != null)
                copy.ServerMessages.RemoveRange(0, firstMessage);
            startupObservations.Add(copy);
            startupEntries += entries;
            startupMessageBytes += bytes;
        }

        private void ApplyStartupObservations(OutputData firstInLevel)
        {
            if (simulator.Frame != 0)
                throw new InvalidOperationException("Native startup reconstruction must remain at frame zero.");
            // Seed IDs with their first observed schemas, including those first
            // sent on InLevel. Earlier raw payloads can then decode. Later schema
            // updates still apply in callback order; entity/spawn mappings remain
            // owned by Reset and native messages, not by this schema pre-pass.
            FakeEntityRegistry.entityToTypes.Clear();
            var seeded = new HashSet<int>();
            foreach (var callback in startupObservations)
                SeedFirstObservedSchemas(callback, seeded);
            SeedFirstObservedSchemas(firstInLevel, seeded);
            foreach (var callback in startupObservations)
            {
                if (callback.EntityRegistry != null)
                    foreach (var entity in callback.EntityRegistry)
                    {
                        simulator.ApplyEntityRegistryUpdateEarly(entity);
                        ObservePhysicsContainer(entity);
                    }
                ApplyObservedServerMessages(callback);
                ApplyObservedEntityState(callback);
            }
            startupObservations.Clear();
            startupEntries = 0;
            startupMessageBytes = 0;
            // The ordinary start path applies this callback once and records its
            // physics phase. Startup replay deliberately never calls Advance*.
            if (firstInLevel.EntityRegistry != null)
                foreach (var entity in firstInLevel.EntityRegistry)
                {
                    simulator.ApplyEntityRegistryUpdateEarly(entity);
                    ObservePhysicsContainer(entity);
                }
        }

        private void SeedFirstObservedSchemas(OutputData output, HashSet<int> seeded)
        {
            if (output.EntityRegistry != null)
                foreach (var entity in output.EntityRegistry)
                    if (seeded.Add(entity.EntityId)) simulator.ApplyEntityRegistryUpdateEarly(entity);
        }

        private void ApplyObservedEntityState(OutputData output)
        {
            if (output.EntityRegistry != null)
                foreach (var entity in output.EntityRegistry) simulator.ApplyEntityRegistryUpdateLate(entity);
            if (output.Chefs != null)
                foreach (var entry in output.Chefs) simulator.ApplyChefUpdate(entry.Key, entry.Value);
            if (output.Items != null)
                foreach (var entry in output.Items) simulator.ApplyPositionUpdate(entry.Key, entry.Value);
        }

        private void ApplyObservedServerMessages(OutputData output, int firstMessage = 0, bool restoredSnapshot = false)
        {
            if (output.ServerMessages == null) return;
            for (int i = firstMessage; i < output.ServerMessages.Count; i++)
            {
                var msg = output.ServerMessages[i];
                // Warp lifecycle receipts already remapped native IDs through
                // ApplyGameUpdateWhenWarping. Replaying them as ordinary spawns
                // would create duplicate records/claims. Remaining native state
                // receipts are authoritative at the acknowledged target frame.
                if (restoredSnapshot && (msg.Type == (int)MessageType.SpawnEntity || msg.Type == (int)MessageType.SpawnPhysicalAttachment ||
                    msg.Type == (int)MessageType.DestroyEntity || msg.Type == (int)MessageType.DestroyEntities || msg.Type == (int)MessageType.EntityRetirementMessage)) continue;
                var decoded = Deserializer.Deserialize(msg.Type, msg.Message);
                if (decoded is EntityRetirementMessage retirement &&
                    !simulator.entityIdToRecord.ContainsKey((int)retirement.m_entityHeader.m_uEntityID) &&
                    TryHandleUnmappedRetirement?.Invoke((int)retirement.m_entityHeader.m_uEntityID, simulator.Frame) == true) continue;
                simulator.ApplyGameUpdate(decoded);
            }
        }

        public void RequestPause()
        {
            if (RequestPending)
            {
                Console.WriteLine("RequestPause: request already pending");
                return;
            }
            if (State != RealGameState.Running)
            {
                Console.WriteLine($"RequestPause: unexpected state {State}; expected Running");
                return;
            }
            var request = RealGameStateRequest.Pause();
            requests.Enqueue(request);
        }

        public void RequestPauseAndWarp(int frame)
        {
            if (RequestPending)
            {
                Console.WriteLine("RequestPauseAndWarp: request already pending");
                return;
            }
            if (State != RealGameState.Running)
            {
                Console.WriteLine($"RequestPauseAndWarp: unexpected state {State}; expected Running");
                return;
            }
            var request = RealGameStateRequest.Pause();
            requests.Enqueue(request);
            request = RealGameStateRequest.Warp(frame);
            requests.Enqueue(request);
        }

        public void RequestResume()
        {
            if (RequestPending)
            {
                Console.WriteLine("RequestResume: request already pending");
                return;
            }
            if (State != RealGameState.Paused)
            {
                Console.WriteLine($"RequestResume: unexpected state {State}; expected Paused");
                return;
            }
            var request = RealGameStateRequest.Resume();
            requests.Enqueue(request);
        }

        // Headless fixed-input playback can abandon a resume only while the
        // connector is still waiting for the matching paused physics phase.
        // At this point RequestResume has not been sent to the native bridge,
        // so cancelling is an entirely controller-local operation.  Once the
        // native resume signal has been emitted (AwaitingResume), callers must
        // instead complete the normal handshake and request a new pause.
        internal bool TryCancelResumeBeforeNativeSignal()
        {
            if (currentRequest == null || currentRequest.Kind != RealGameStateRequestKind.Resume ||
                State != RealGameState.AwaitingPhysicsPhaseShiftAlignment)
                return false;
            currentRequest.Completed.TrySetResult(false);
            currentRequest = null;
            State = RealGameState.Paused;
            OnStateChanged?.Invoke();
            return true;
        }

        public void RequestWarp(int frame)
        {
            if (RequestPending)
            {
                Console.WriteLine("RequestWarp: request already pending");
                return;
            }
            if (State != RealGameState.Paused)
            {
                Console.WriteLine($"RequestWarp: unexpected state {State}; expected Paused");
                return;
            }
            var request = RealGameStateRequest.Warp(frame);
            requests.Enqueue(request);
        }

        public void RequestWarpAndResume(int frame)
        {
            if (RequestPending)
            {
                Console.WriteLine("RequestWarpAndResume: request already pending");
                return;
            }
            if (State != RealGameState.Paused)
            {
                Console.WriteLine($"RequestWarpAndResume: unexpected state {State}; expected Paused");
                return;
            }
            var request = RealGameStateRequest.Warp(frame);
            requests.Enqueue(request);
            request = RealGameStateRequest.Resume();
            requests.Enqueue(request);
        }

        public bool RequestPending
        {
            get
            {
                return currentRequest != null || requests.Count > 0;
            }
        }
    }
}
