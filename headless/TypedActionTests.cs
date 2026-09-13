using Google.Protobuf;
using Hpmv;
using System.Text.Json;
using System.Text.Json.Nodes;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

public static class TypedActionTests
{
    public static int Run(string evidenceRoot)
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Typed action fixture: " + name); checks++; }
        void Reject(Action f, string name) { try { f(); } catch (Exception e) when (e is ArgumentException or InvalidOperationException or InvalidDataException) { checks++; return; } throw new InvalidOperationException("Expected typed rejection: " + name); }
        var setup = new Carnival34FourLevel(); var live = setup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]);
        JsonObject Wait(string id, int chef, int frames, bool shared = false) => new() { ["id"] = id, ["type"] = "wait", ["chef"] = chef, ["frames"] = frames, ["timeoutFrames"] = 20, ["resources"] = shared ? new JsonArray(42) : new() };
        var request = new JsonObject { ["command"] = "actions", ["maximumFrames"] = 60, ["actions"] = new JsonArray(Wait("a", 103, 2), Wait("b", 104, 2, true), Wait("c", 105, 3, true), Wait("d", 106, 2)) };
        byte[] before = setup.ToProto().ToByteArray();
        var plan = TypedActionPlan.Create(setup, live, 0, request);
        Check(before.SequenceEqual(setup.ToProto().ToByteArray()), "preflight does not change setup/action IDs/history");
        Check(plan.Nodes.Count == 4 && plan.Nodes.Select(n => n.Chef).Distinct().Count() == 4, "four concurrent chef lanes");
        Check(plan.Nodes[2].Dependencies.SequenceEqual(new[] { plan.Nodes[1].Id }) && plan.Nodes[0].Dependencies.Length == 0 && plan.Nodes[3].Dependencies.Length == 0, "only actual shared-resource nodes serialize");
        var invalid = (JsonObject)request.DeepClone(); invalid["actions"]![3]!["type"] = "fabricated-cannon-flight";
        Reject(() => TypedActionPlan.Create(setup, live, 0, invalid), "unavailable original primitive");
        Check(before.SequenceEqual(setup.ToProto().ToByteArray()), "invalid late node is atomic");
        foreach (string type in new[] { "goto", "drop", "throw", "pilot-rotation" })
        {
            var overflow = new JsonObject { ["id"] = "overflow", ["chef"] = 103, ["type"] = type, ["x"] = 1e100, ["z"] = 0 };
            if (type == "pilot-rotation") { overflow["target"] = 84; overflow["angle"] = -1e100; }
            Reject(() => TypedActionPlan.Create(setup, live, 0, new() { ["actions"] = new JsonArray(overflow) }), "finite double cannot overflow native float: " + type);
        }
        Check(before.SequenceEqual(setup.ToProto().ToByteArray()), "float overflow rejection leaves all graph/claim state unchanged");
        invalid = (JsonObject)request.DeepClone(); invalid["actions"]![0]!["after"] = new JsonArray("d");
        Reject(() => TypedActionPlan.Create(setup, live, 0, invalid), "forward/cyclic dependencies");
        invalid = (JsonObject)request.DeepClone(); invalid["actions"]![1]!["id"] = "a";
        Reject(() => TypedActionPlan.Create(setup, live, 0, invalid), "duplicate named action");
        invalid = (JsonObject)request.DeepClone(); invalid["actions"]![0]!["resources"] = new JsonArray(999);
        Reject(() => TypedActionPlan.Create(setup, live, 0, invalid), "unknown native resource");
        invalid = (JsonObject)request.DeepClone(); invalid["actions"]![0]!["timeoutFrames"] = 1;
        Reject(() => TypedActionPlan.Create(setup, live, 0, invalid), "per-action bound required");
        var typed = new JsonObject { ["actions"] = new JsonArray(
            new JsonObject { ["id"] = "egg", ["chef"] = 104, ["type"] = "pickup", ["target"] = 65, ["expectSpawn"] = true },
            new JsonObject { ["id"] = "follow", ["chef"] = 103, ["type"] = "pickup", ["spawnedBy"] = "egg" },
            new JsonObject { ["id"] = "place", ["chef"] = 103, ["type"] = "place", ["target"] = 48 },
            new JsonObject { ["id"] = "use", ["chef"] = 104, ["type"] = "interact", ["target"] = 75 },
            new JsonObject { ["id"] = "washwait", ["chef"] = 104, ["type"] = "wait-progress", ["target"] = 75, ["progressType"] = "washing", ["progress"] = 2 },
            new JsonObject { ["id"] = "throw", ["chef"] = 105, ["type"] = "throw", ["target"] = 7 },
            new JsonObject { ["id"] = "drop", ["chef"] = 106, ["type"] = "drop", ["x"] = 19, ["z"] = -15 },
            new JsonObject { ["id"] = "pilot", ["chef"] = 106, ["type"] = "pilot-rotation", ["target"] = 84, ["angle"] = 45 }) };
        var mixed = TypedActionPlan.Create(setup, live, 0, typed);
        Check(mixed.Nodes[1].Action is InteractAction { Subject: SpawnedEntityReference } && mixed.Nodes[1].Dependencies.Contains(mixed.Nodes[0].Id), "spawn reference uses original producing action claim");
        Check(mixed.Nodes[2].Action is InteractAction { Primary: true, IsPickup: false } && mixed.Nodes[3].Action is InteractAction { Primary: false }, "place and use preserve original mapping");
        Check(mixed.Nodes[4].Action is WaitForWashingProgressAction && mixed.Nodes[5].Action is ThrowAction && mixed.Nodes[6].Action is DropAction && mixed.Nodes[7].Action is PilotRotationAction, "original typed implementations retained");
        var preparation = new JsonObject { ["actions"] = new JsonArray(new JsonObject { ["id"] = "aim", ["type"] = "prepare-primary", ["chef"] = 103, ["target"] = 38 }) };
        var prep = TypedActionPlan.Create(setup, live, 0, preparation).Nodes.Single().Action;
        Check(prep is InteractAction { Primary: true, Prepare: true, IsPickup: false, ExpectSpawn: false, RequireObservedTransfer: false, DisallowOvershoot: true }, "prepare-primary exposes only the original native-target preparation");
        Check(prep.ToProto().Interact.Prepare, "preparation flag survives original action serialization");
        preparation["actions"]![0]!["expectSpawn"] = true;
        Reject(() => TypedActionPlan.Create(setup, live, 0, preparation), "preparation cannot claim a spawned item");
        plan.Install(setup); Check(setup.sequences.NodeById.Count == 4 && setup.sequences.NextId == 5, "one atomic install assigns exact preflight IDs");
        var saved = Hpmv.Save.GameSetup.Parser.ParseFrom(setup.ToProto().ToByteArray()).FromProto();
        Check(saved.sequences.NodeById[3].Deps.SequenceEqual(new[] { 2 }), "original checkpoint preserves cross-chef resource dependencies");

        using var stream = typeof(TypedActionTests).Assembly.GetManifestResourceStream("Headless.Reference.NativeDInitialRegistryFixture.json")!;
        var fixture = JsonNode.Parse(stream)!.AsObject();
        OutputData Frame(params ServerMessage[] messages) => new() { ServerMessages = messages.ToList(), Items = new(), Chefs = new(), EntityRegistry = new(), InvalidStateReason = "", FramesSinceLastNoPhysicsFrame = 5 };
        var runningSetup = new Carnival34FourLevel(); var session = new HeadlessSession(runningSetup, "synthetic typed graph", evidenceRoot);
        InputData Send(OutputData value) => session.getNext(value).GetAwaiter().GetResult();
        Send(Frame(new ServerMessage { Type = (int)MessageType.LevelLoadByName, Message = new LevelLoadByNameMessage { m_Scene = "s_Day_3_4", m_StartLoadGameState = GameState.InLevel, m_HideLoadingScreenGameState = GameState.InLevel }.ToBytes() }));
        var initial = Frame(new ServerMessage { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() }); initial.EntityRegistry = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
        Send(initial); Send(Frame()); var pause = Frame(); pause.NextFramePaused = true; Send(pause);
        void Resume()
        { var still = Frame(); still.LastFramePaused = still.NextFramePaused = true; Send(still); still.FramesSinceLastNoPhysicsFrame = 4; Send(still); var resumed = Frame(); resumed.LastFramePaused = true; Send(resumed); }
        session.Command(request); Resume();
        InputData output = null;
        for (int i = 0; i < 30; i++) { output = Send(Frame()); if (output.RequestPause) break; }
        Check(output?.RequestPause == true, "finished graph queues bounded original pause"); Send(pause);
        Check(session.Inspect()["typedActions"]!["outcome"]!.ToString() == "complete", "graph completion observes final pause");
        var timings = runningSetup.sequences.NodeById;
        Check(timings[1].Predictions.StartFrame == timings[2].Predictions.StartFrame && timings[4].Predictions.StartFrame == timings[1].Predictions.StartFrame, "independent chef work actually runs concurrently");
        Check(timings[3].Predictions.StartFrame >= timings[2].Predictions.EndFrame, "shared counter lease cannot overlap");
        var timeoutRequest = new JsonObject { ["command"] = "actions", ["maximumFrames"] = 30, ["actions"] = new JsonArray(new JsonObject { ["id"] = "never", ["type"] = "wait-progress", ["chef"] = 103, ["target"] = 7, ["progressType"] = "cooking", ["progress"] = 100, ["timeoutFrames"] = 3 }) };
        session.Command(timeoutRequest); Resume();
        for (int i = 0; i < 12; i++) { output = Send(Frame()); if (output.RequestPause) break; }
        Check(output?.RequestPause == true && output.Input.Values.All(p => !p.Pickup.Down && !p.Interact.Down && p.Pad.X == 0 && p.Pad.Y == 0), "timeout emits neutral release and pause"); Send(pause);
        Check(session.Inspect()["typedActions"]!["outcome"]!.ToString() == "failed" && runningSetup.sequences.NodeById[5].Predictions.EndFrame is null, "timeout never claims action completion or erases unfinished node");
        Reject(() => session.Command(new() { ["command"] = "resume" }), "failed graph cannot silently lose fixed bounds");
        session.Command(new() { ["command"] = "actions-clear" });
        Check(runningSetup.sequences.NodeById.Count == 4 && runningSetup.sequences.NodeById.Values.All(n => n.Predictions.EndFrame.HasValue), "explicit clear removes only unfinished owned nodes");
        int beforeClearFrame = session.Inspect()["frame"]!.GetValue<int>(), beforeClearNextId = runningSetup.sequences.NextId;
        string beforeClearEntities = session.Inspect(true)["entities"]!.ToJsonString();
        session.Command(new() { ["command"] = "actions-clear", ["all"] = true });
        Check(runningSetup.sequences.NodeById.Count == 0 && runningSetup.sequences.Actions.All(l => l.Count == 0), "explicit clear-all removes completed and earlier authored batches");
        Check(session.Inspect()["frame"]!.GetValue<int>() == beforeClearFrame && session.Inspect(true)["entities"]!.ToJsonString() == beforeClearEntities, "clear-all preserves exact current observed entities and frame");
        Check(runningSetup.sequences.NextId == beforeClearNextId, "clear-all does not reuse prior action or claim IDs");
        var liveChild = new GameEntityRecord { path = new() { ids = new[] { 65, 0 } }, prefab = runningSetup.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 65).prefab.Spawns[0], existed = new(true) };
        runningSetup.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 65).spawned.Add(liveChild);
        Reject(() => session.Command(new() { ["command"] = "actions-clear", ["all"] = true }), "clear-all cannot erase a live spawned record even outside the latest batch");
        runningSetup.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 65).spawned.Clear();

        var mappingSetup = new Carnival34FourLevel(); var mappings = CarnivalRegistryAudit.FromInspection(mappingSetup, fixture);
        var current = mappingSetup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]);
        Check(mappings.RequireGraphMappings(current, 0)["fixedMappingsValidated"]!.GetValue<bool>(), "initial fixed map gate passes actual122fixture");
        var spawner = current[65]; var child = new GameEntityRecord { path = new() { ids = new[] { 65, spawner.spawned.Count } }, prefab = spawner.prefab.Spawns[0], spawner = spawner, existed = new(true), position = new(default(System.Numerics.Vector3)) }; spawner.spawned.Add(child); current[123] = child;
        Reject(() => mappings.RequireGraphMappings(current, 0), "observed spawn message alone does not fabricate metadata");
        var crateMetadata = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Single(e => e.EntityId == 65); crateMetadata.SpawnNames = spawner.prefab.Spawns.Select(p => p.Name).ToList(); mappings.Observe(new[] { crateMetadata });
        mappings.Observe(new[] { new EntityRegistryData { EntityId = 123, Name = child.prefab.Name, Pos = new() { X = 0, Y = 0, Z = 0 }, Components = new() { "BoxCollider" }, SyncEntityTypes = new() { (int)EntityType.PhysicalAttach } } });
        Check(mappings.RequireGraphMappings(current, 0)["spawnedMappings"]!.AsArray().Count == 1, "explicit matching ordered metadata and actual path admitted");
        child.path.ids[1]++;
        Reject(() => mappings.RequireGraphMappings(current, 0), "mutated spawn ordinal rejected"); child.path.ids[1]--;
        current.Remove(123); Reject(() => mappings.RequireGraphMappings(current, 0), "missing live path is not inferred removal");
        mappings.ObserveRemoval(123); Check(mappings.RequireGraphMappings(current, 0)["spawnedMappings"]!.AsArray().Count == 0, "actual removal receipt separates historical extra metadata");
        return checks;
    }
}
