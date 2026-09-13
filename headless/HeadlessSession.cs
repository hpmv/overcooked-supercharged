using Google.Protobuf;
using Hpmv;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

/// <summary>Thread-safe CLI boundary around the original framework reconstruction and request engine.</summary>
public sealed partial class HeadlessSession : Interceptor.IAsync
{
    private readonly object gate = new();
    private readonly RealGameConnector core;
    private readonly GameSetup setup;
    private readonly string evidenceRoot;
    private readonly int? seed;
    private readonly CarnivalRegistryAudit registryAudit;
    private readonly bool discoveryOnly;
    private readonly Dictionary<int, EntityRegistryData> registry = new();
    private readonly List<string> errors = new();
    private int? stopAt;
    private int? movementAction;
    private long exchanges;
    private bool freshLoadObserved, connected, warpUsed;
    private int externalPauses;
    private string setupSource;
    public event Action<OutputData, InputData> Exchange;
    public event Action<JsonObject> Control;
    private TraceStore trace;
    private string traceFailure;
    private NativeWarpCapabilities nativeWarpCapabilities;
    private JsonObject warpMappingValidation;
    private JsonObject registryWarpRebranch;
    private readonly JsonArray observedProxyRetirements = new();
    public bool TraceFailed { get { lock (gate) return traceFailure is not null; } }
    public void AttachTrace(TraceStore store) { lock (gate) { if (trace is not null) throw new InvalidOperationException("Trace is already attached."); trace = store; } }

    public HeadlessSession(GameSetup setup, string setupSource, string evidenceRoot, int? seed = null, bool realtime = false, bool discoveryOnly = false, string levelProfile = null)
    {
        this.setup = setup; this.setupSource = setupSource; this.evidenceRoot = Path.GetFullPath(evidenceRoot); this.seed = seed;
        this.discoveryOnly = discoveryOnly || setup is Story11FourLevel { InspectionOnly: true };
        setup.PreventInvalidState = false;
        core = new RealGameConnector(setup);
        registryAudit = new CarnivalRegistryAudit(setup, this.discoveryOnly, levelProfile);
        core.TryHandleUnmappedRetirement = (id, frame) => {
            var proof = registryAudit.InspectRetiredOwnerProxy(id, core.simulator.entityIdToRecord, frame);
            // RegistryObserver may publish the exact current absence before the
            // native retirement packet reaches the bridge. The first packet
            // consumes the physical-container association in the audit; a later
            // byte-identical retirement for that same proven proxy is therefore
            // an idempotent receipt, not a new logical-entity lookup. Fresh-load
            // reset clears this list, and the connector has already established
            // that the ID is not a currently live logical entity.
            if (proof is null) return observedProxyRetirements.Any(existing =>
                existing?["nativeId"]?.GetValue<int>() == id);
            observedProxyRetirements.Add(proof); return true;
        };
        core.FramerateController.Framerate = realtime ? 60 : int.MaxValue;
        core.OnFrameUpdate += () =>
        {
            ObserveActionFrame();
            bool arrived = movementAction is int id && setup.sequences.NodeById[id].Predictions.EndFrame.HasValue;
            if (core.State == RealGameState.Running && !core.RequestPending &&
                (arrived || (stopAt is int frame && core.simulator.Frame >= frame - 1)))
            { core.RequestPause(); stopAt = null; }
        };
    }

    public Task<InputData> getNext(OutputData output, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested(); connected = true; exchanges++;
            if (traceFailure is not null) return Task.FromResult(TraceFailureInput(output));
            nativeWarpCapabilities = output.__isset.nativeWarpCapabilities ? output.NativeWarpCapabilities?.DeepCopy() : null;
            output.ServerMessages ??= new(); output.EntityRegistry ??= new(); output.Chefs ??= new(); output.Items ??= new();
            output.InvalidStateReason ??= "";
            bool load = output.ServerMessages.Any(m => m.Type is (int)MessageType.LevelLoadByIndex or (int)MessageType.LevelLoadByName);
            if (load) { InterruptRaw("Fresh level load interrupted the input stream."); InterruptActions("Fresh level load interrupted the action graph."); DiscardAuthoredActionsForNativeLoad(); registry.Clear(); freshLoadObserved = true; stopAt = null; movementAction = null; registryAudit.Reset(); registryWarpRebranch = null; observedProxyRetirements.Clear(); }
            foreach (var entity in output.EntityRegistry) registry[entity.EntityId] = entity;
            registryAudit.Observe(output.EntityRegistry);
            registryAudit.ObserveStartupFrame(output);
            try
            {
                if (discoveryOnly) return Task.FromResult(ObserveDiscovery(output, load));
                bool externalPause = !core.RequestPending && output.NextFramePaused;
                if (externalPause && output.LastFramePaused && core.State == RealGameState.Running)
                { core.State = RealGameState.Paused; externalPauses++; }
                // Reuse the original framework transition, reconstruction, input history,
                // action graph and pause/warp implementation. Its UI network Start() is never used.
                PrepareRawExchange(output);
                bool wasWarping = core.State == RealGameState.Warping;
                var input = core.getNext(output, cancellationToken).GetAwaiter().GetResult();
                foreach (var message in output.ServerMessages)
                {
                    if (message.Type is not ((int)MessageType.EntityRetirementMessage) and not ((int)MessageType.DestroyEntity) and not ((int)MessageType.DestroyEntities)) continue;
                    var removal = Deserializer.Deserialize(message.Type, message.Message);
                    if (removal is EntityRetirementMessage retired) registryAudit.ObserveRemoval((int)retired.m_entityHeader.m_uEntityID);
                    else if (removal is DestroyEntityMessage destroyed) registryAudit.ObserveRemoval((int)destroyed.m_Header.m_uEntityID);
                    else if (removal is DestroyEntitiesMessage destroyedMany) { registryAudit.ObserveRemoval((int)destroyedMany.m_rootId); foreach (var id in destroyedMany.m_ids) registryAudit.ObserveRemoval((int)id); }
                }
                if (wasWarping && core.State == RealGameState.Paused && !core.RequestPending)
                {
                    var freshIds = output.EntityRegistry.Select(entity => entity.EntityId).ToHashSet();
                    var proxyRetirementRebranch = RebranchProxyRetirements(observedProxyRetirements,
                        core.simulator.Frame, freshIds);
                    registryWarpRebranch = registryAudit.RebranchAfterSuccessfulWarp(core.simulator.entityIdToRecord,
                        core.simulator.Frame, freshIds);
                    registryWarpRebranch["discardedProxyRetirementIds"] =
                        proxyRetirementRebranch["discardedIds"]!.DeepClone();
                    registryWarpRebranch["proxyRetirementRebranch"] = proxyRetirementRebranch;
                }
                registryAudit.ObserveMappedEntities(core.simulator.entityIdToRecord, core.simulator.Frame);
                registryAudit.ObserveSpawnMappings(output.ServerMessages, core.simulator.entityIdToRecord, core.simulator.Frame);
                registryAudit.ObservePhysicalContainers(output.ServerMessages, core.simulator.entityIdToRecord);
                if (externalPause && core.State == RealGameState.Running)
                {
                    // Process the final advancing callback first. A bridge pause
                    // is real observed state, not an invitation to advance every
                    // following paused RPC callback as another gameplay frame.
                    core.State = RealGameState.Paused; externalPauses++;
                    input.NextFrame = core.simulator.Frame;
                    input.Input = setup.entityRecords.Chefs.Keys.ToDictionary(c => c.path.ids[0], _ => new OneInputData
                    { Pad = new(), Pickup = new(), Interact = new(), Dash = new() });
                }
                // A headless attachment must not silently select upstream's UI seed12347
                // or its optional invalid-state prevention mode.
                input.PreventInvalidState = false;
                if (input.__isset.resetOrderSeed)
                {
                    if (seed is int explicitSeed) input.ResetOrderSeed = explicitSeed;
                    else input.__isset.resetOrderSeed = false;
                }
                ApplyRawExchange(output, input);
                ApplyActionExchange(input);
                trace?.Exchange(output, input, core.State == RealGameState.Paused && !core.RequestPending);
                Exchange?.Invoke(output, input);
                return Task.FromResult(input);
            }
            catch (TraceWriteException error)
            {
                FailTrace(error);
                return Task.FromResult(TraceFailureInput(output));
            }
            catch (Exception error)
            {
                errors.Add(error.ToString()); connected = false;
                throw;
            }
        }
    }

    internal static JsonObject RebranchProxyRetirements(JsonArray receipts, int targetFrame,
        IReadOnlySet<int> freshlyRegisteredIds)
    {
        var discardedIds = new SortedSet<int>();
        var discarded = new JsonArray();
        for (int index = receipts.Count - 1; index >= 0; index--)
        {
            if (receipts[index] is not JsonObject receipt ||
                receipt["nativeId"]?.GetValue<int>() is not int nativeId ||
                receipt["frame"]?.GetValue<int>() is not int receiptFrame)
                throw new InvalidDataException("Controller-owned proxy retirement receipt is malformed.");
            bool abandonedFuture = receiptFrame > targetFrame;
            bool reincarnated = freshlyRegisteredIds.Contains(nativeId);
            if (!abandonedFuture && !reincarnated) continue;
            discardedIds.Add(nativeId);
            discarded.Add(new JsonObject {
                ["nativeId"] = nativeId,
                ["receiptFrame"] = receiptFrame,
                ["abandonedFuture"] = abandonedFuture,
                ["reincarnatedAtTarget"] = reincarnated
            });
            receipts.RemoveAt(index);
        }
        return new JsonObject {
            ["targetFrame"] = targetFrame,
            ["discardedIds"] = JsonSerializerNode(discardedIds),
            ["discarded"] = discarded,
            ["retainedCount"] = receipts.Count,
            ["nativeStateChanged"] = false,
            ["scope"] = "Controller observation bookkeeping only; no native game state or input changed."
        };
    }

    public void ConnectionEnded(string error = null)
    {
        lock (gate)
        {
            connected = false; InterruptRaw("Framework connection ended before completion."); InterruptActions("Framework connection ended before completion.");
            if (error is not null) errors.Add(error);
            if (traceFailure is null) try { trace?.Flush(); } catch (TraceWriteException failed) { FailTrace(failed); }
        }
    }
    private void FailTrace(TraceWriteException error)
    {
        if (traceFailure is not null) return;
        traceFailure = error.Message; errors.Add(traceFailure); stopAt = null; movementAction = null;
        InterruptRaw(traceFailure); InterruptActions(traceFailure); core.State = RealGameState.Error;
        Console.Error.WriteLine(traceFailure);
    }
    private InputData TraceFailureInput(OutputData output) => new()
    {
        NextFrame = output.FrameNumber, RequestPause = true, RequestResume = false, PreventInvalidState = false,
        Input = setup.entityRecords.Chefs.Keys.ToDictionary(c => c.path.ids[0], c => new OneInputData
        {
            Pad = new(),
            Pickup = new() { JustReleased = setup.entityRecords.Chefs[c][core.simulator.Frame].primaryButtonDown },
            Interact = new() { JustReleased = setup.entityRecords.Chefs[c][core.simulator.Frame].secondaryButtonDown },
            Dash = new() { JustReleased = setup.inputHistory.FrameInputs[c][core.simulator.Frame].dash.isDown }
        })
    };

    public JsonObject Inspect(bool full = false)
    {
        lock (gate)
        {
            int frame = core.simulator.Frame;
            var result = new JsonObject
            {
                ["ok"] = traceFailure is null, ["kind"] = "supercharged-headless-reconstructed-state", ["connected"] = connected,
                ["trace"] = trace?.Status(), ["traceFailure"] = traceFailure,
                ["nativeWarpCapabilities"] = nativeWarpCapabilities is null ? null : JsonSerializerNode(nativeWarpCapabilities),
                ["warpMappingValidation"] = warpMappingValidation?.DeepClone(),
                ["registryWarpRebranch"] = registryWarpRebranch?.DeepClone(),
                ["state"] = core.State.ToString(), ["frame"] = frame, ["requestPending"] = core.RequestPending,
                ["exchanges"] = exchanges, ["resumePhaseMetadataEmissions"] = core.ResumePhaseMetadataEmissions,
                ["freshLevelLoadObserved"] = freshLoadObserved,
                ["needsFreshLevelBaseline"] = !freshLoadObserved, ["setupSource"] = setupSource,
                ["discoveryOnly"] = discoveryOnly,
                ["registryCount"] = registry.Count, ["reconstructedEntityCount"] = core.simulator.entityIdToRecord.Count,
                ["externalPausesObserved"] = externalPauses,
                ["movementAction"] = movementAction,
                ["movementCompleted"] = movementAction is int actionId && setup.sequences.NodeById[actionId].Predictions.EndFrame.HasValue,
                ["registryValidation"] = registryAudit.Report(),
                ["rawInput"] = RawStatus(),
                ["typedActions"] = ActionStatus(),
                ["lastEmpiricalFrame"] = setup.LastEmpiricalFrame, ["warpUsed"] = warpUsed,
                ["preventInvalidState"] = false, ["requestedSeed"] = seed,
                ["invalidStateReason"] = setup.entityRecords.InvalidStateReason[frame],
                ["errors"] = new JsonArray(errors.Select(e => (JsonNode)JsonValue.Create(e)).ToArray()),
                ["qualification"] = "Framework reconstruction and development control; not native geometry, score, RNG, or full-round validation. A late connection requires a fresh level or an explicit complete baseline resynchronization."
            };
            if (full)
            {
                // Read the same current identity proof used by action admission.
                // A stale prior graph's admission cannot certify a later spawn.
                if (!discoveryOnly) {
                    try { result["graphMappingValidation"] = registryAudit.RequireGraphMappings(core.simulator.entityIdToRecord, frame); }
                    catch (InvalidOperationException invalid) { result["graphMappingValidation"] = new JsonObject { ["ok"] = false, ["frame"] = frame, ["error"] = invalid.Message }; }
                }
                var records = new JsonArray();
                foreach (var (id, entity) in core.simulator.entityIdToRecord.OrderBy(p => p.Key))
                {
                    var position = entity.position[frame]; var velocity = entity.velocity[frame];
                    var rotation = entity.rotation[frame]; var angular = entity.angularVelocity[frame];
                    records.Add(new JsonObject
                    {
                        ["id"] = id, ["path"] = JsonSerializerNode(entity.path.ids), ["name"] = entity.displayName,
                        ["className"] = entity.className, ["prefab"] = entity.prefab?.Name, ["exists"] = entity.existed[frame],
                        ["position"] = new JsonObject { ["x"] = position.X, ["y"] = position.Y, ["z"] = position.Z },
                        ["velocity"] = new JsonObject { ["x"] = velocity.X, ["y"] = velocity.Y, ["z"] = velocity.Z },
                        ["rotation"] = new JsonObject { ["x"] = rotation.X, ["y"] = rotation.Y, ["z"] = rotation.Z, ["w"] = rotation.W },
                        ["angularVelocity"] = new JsonObject { ["x"] = angular.X, ["y"] = angular.Y, ["z"] = angular.Z },
                        ["cookingProgress"] = entity.cookingProgress[frame], ["mixingProgress"] = entity.mixingProgress[frame],
                        ["washingProgress"] = entity.washingProgress[frame], ["choppingProgress"] = entity.choppingProgress[frame],
                        ["data"] = JsonNode.Parse(JsonFormatter.Default.Format(entity.data[frame].ToProto())),
                        ["nativeCannon"] = NativeCannonSnapshot(entity, frame),
                        ["plateLifecycle"] = PlateLifecycleSnapshot(entity, frame),
                        ["chef"] = entity.chefState is null ? null : JsonNode.Parse(JsonFormatter.Default.Format(entity.chefState[frame].ToProto()))
                    });
                }
                result["entities"] = records;
                result["registry"] = JsonSerializerNode(registry.Values.OrderBy(e => e.EntityId).ToArray());
                result["initialRegistry"] = registryAudit.InitialRegistrations();
                result["settledChefPositions"] = registryAudit.SettledChefPositions();
                result["observedProxyRetirements"] = observedProxyRetirements.DeepClone();
                result["nativeReloadDiscard"] = nativeReloadDiscard?.DeepClone();
                result["actionGraph"] = JsonNode.Parse(JsonFormatter.Default.Format(setup.sequences.ToProto()));
                if (discoveryOnly)
                {
                    result["nativeObservedItems"] = JsonSerializerNode(discoveryItems);
                    result["nativeObservedChefs"] = JsonSerializerNode(discoveryChefs);
                    result["observationScope"] = "Raw native registry and item/chef observations only, accumulated from explicit field-presence deltas. Entity records and action maps are intentionally absent.";
                }
            }
            return result;
        }
    }

    private static JsonNode JsonSerializerNode<T>(T value) => System.Text.Json.JsonSerializer.SerializeToNode(value);

    public JsonObject Command(JsonObject request)
    {
        lock (gate)
        {
            string command = request["command"]?.ToString() ?? "inspect";
            if (command == "inspect") return Inspect(request["full"]?.GetValue<bool>() == true);
            if (command == "status") return StatusCommand(request);
            if (discoveryOnly && command is not ("pause" or "actions-clear"))
                throw new InvalidOperationException("Story 1-1 inspection bootstrap has no validated roles or navigation map; control/checkpoint/warp commands are unavailable.");
            if (traceFailure is not null) throw new InvalidOperationException(traceFailure + " Restart with a new trace after resolving storage; inspect remains available.");
            if (command == "record-input") return ExportRawRecording(request);
            if (command == "checkpoint")
            {
                if (core.State != RealGameState.Paused || core.RequestPending || !freshLoadObserved)
                    throw new InvalidOperationException("Checkpoint requires a settled paused reconstructed fresh-level session.");
                string relative = request["path"]?.ToString() ?? $"checkpoint-{core.simulator.Frame}.pb";
                string path = Path.GetFullPath(Path.Combine(evidenceRoot, relative));
                if (!path.StartsWith(evidenceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Checkpoint path must remain under evidence root.");
                byte[] bytes = setup.ToProto().ToByteArray();
                if (File.Exists(path) || File.Exists(path + ".json")) throw new IOException("Checkpoint or receipt already exists.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write)) file.Write(bytes);
                var receipt = Inspect(); receipt["path"] = path; receipt["sha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes));
                using (var file = new StreamWriter(new FileStream(path + ".json", FileMode.CreateNew, FileAccess.Write))) file.Write(receipt.ToJsonString());
                return receipt;
            }
            if (!freshLoadObserved) throw new InvalidOperationException("No fresh level baseline has been observed; reconnect before loading the level or request a complete bridge resynchronization.");
            if (core.RequestPending) throw new InvalidOperationException("A framework state transition is already pending.");
            if (command is "resume" or "step" && actionPlan is not null && actionOutcome is "failed" or "interrupted")
                throw new InvalidOperationException("The bounded action graph stopped incomplete. Use actions-clear while paused before another unbounded resume.");
            switch (command)
            {
                case "actions": StartActions(request); break;
                case "actions-clear": ClearActions(request["all"]?.GetValue<bool>() == true); break;
                case "raw-input":
                case "raw-replay":
                    StartRaw(request, command == "raw-replay"); break;
                case "pause":
                    if (core.State != RealGameState.Running) throw new InvalidOperationException("Pause requires Running.");
                    core.RequestPause(); break;
                case "resume":
                    if (core.State != RealGameState.Paused) throw new InvalidOperationException("Resume requires Paused.");
                    movementAction = null; stopAt = null;
                    core.RequestResume(); break;
                case "step":
                    if (core.State != RealGameState.Paused) throw new InvalidOperationException("Step requires Paused.");
                    int count = request["frames"]?.GetValue<int>() ?? 1;
                    if (count is < 2 or > 36000) throw new ArgumentOutOfRangeException("frames", "Original pause handshake requires at least two advancing frames.");
                    movementAction = null;
                    stopAt = checked(core.simulator.Frame + count); core.RequestResume(); break;
                case "goto":
                    if (core.State != RealGameState.Paused) throw new InvalidOperationException("Goto requires Paused.");
                    registryAudit.RequireLayout();
                    int chefId = request["chef"]?.GetValue<int>() ?? -1;
                    if (!core.simulator.entityIdToRecord.TryGetValue(chefId, out var chef) || !setup.sequences.ChefIndexByChef.TryGetValue(chef, out int chefIndex))
                        throw new ArgumentException("Chef must be an observed reconstructed native chef ID.");
                    if (!chef.existed[core.simulator.Frame] || !registry.ContainsKey(chefId)) throw new InvalidOperationException("Chef has no current registry baseline.");
                    if (setup.sequences.NodeById.Values.Any(n => !n.Predictions.EndFrame.HasValue || n.Predictions.EndFrame >= core.simulator.Frame))
                        throw new InvalidOperationException("Diagnostic goto requires no unfinished graph actions.");
                    float x = request["x"]?.GetValue<float>() ?? float.NaN, z = request["z"]?.GetValue<float>() ?? float.NaN;
                    if (!float.IsFinite(x) || !float.IsFinite(z)) throw new ArgumentException("Goto requires finite world x/z.");
                    int maximum = request["frames"]?.GetValue<int>() ?? 120;
                    if (maximum is < 2 or > 600) throw new ArgumentOutOfRangeException("frames", "Diagnostic goto limit is 2..600 frames.");
                    if (!setup.mapByChef.TryGetValue(chefId, out var map) || map.FindPath(chef.position[core.simulator.Frame].XZ(), new() { new(x, z) }).Count < 2)
                        throw new InvalidOperationException("No path in the supplied framework geometry; no direct-vector fallback admitted by CLI.");
                    movementAction = setup.sequences.InsertAction((chefIndex, setup.sequences.Actions[chefIndex].Count), new GotoAction
                    { DesiredPos = new LiteralLocationToken(new(x, z)), DisallowDash = true, DisallowOvershoot = true });
                    authoredActionIds.Add(movementAction.Value);
                    stopAt = checked(core.simulator.Frame + maximum); core.RequestResume(); break;
                case "warp":
                    if (request["development"]?.GetValue<bool>() != true) throw new InvalidOperationException("Warp requires explicit development:true and disqualifies a fresh-run proof.");
                    if (core.State != RealGameState.Paused) throw new InvalidOperationException("Warp requires Paused.");
                    int target = request["frame"]?.GetValue<int>() ?? -1;
                    if (target < 0 || target > setup.LastEmpiricalFrame) throw new ArgumentOutOfRangeException("frame", "Warp target must be within observed history.");
                    warpMappingValidation = registryAudit.RequireWarpPaths(core.simulator.entityIdToRecord, core.simulator.Frame, target, nativeWarpCapabilities);
                    if (setup.entityRecords.CriticalSectionForWarping[core.simulator.Frame] != 0 || setup.entityRecords.CriticalSectionForWarping[target] != 0)
                        throw new InvalidOperationException("Framework marks this source or target as an unsupported warp critical section.");
                    if (setup.entityRecords.GenAllEntities().Any(e => (e.existed[core.simulator.Frame] && e.IsInCriticalSectionForWarping(core.simulator.Frame)) ||
                        (e.existed[target] && e.IsInCriticalSectionForWarping(target))))
                        throw new InvalidOperationException("Current or target native cannon audit is missing or active; authoring warp requires settled observations.");
                    warpUsed = true; core.RequestWarp(target); break;
                default: throw new ArgumentException("Unknown headless command: " + command);
            }
            try { trace?.Control(request); }
            catch (TraceWriteException error) { FailTrace(error); throw; }
            Control?.Invoke((JsonObject)request.DeepClone());
            var status = Inspect(); status["acceptedCommand"] = command; return status;
        }
    }
}
