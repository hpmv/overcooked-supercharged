using Hpmv;
using System.Numerics;
using System.Text.Json.Nodes;
using Team17.Online.Multiplayer.Messaging;
using Google.Protobuf;
using Thrift.Protocol;
using Thrift.Transport.Client;
using System.Text.Json;

namespace Supercharged.Headless;

public static class SessionTests
{
    public static JsonObject Run(string evidenceRoot)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Headless fixture: " + message); checks++; }
        void Reject(Action action, string message) { try { action(); } catch (InvalidOperationException) { checks++; return; } throw new InvalidOperationException("Expected rejection: " + message); }
        GameSetup Setup() => new() { geometry = new GameMapGeometry(Vector2.Zero, new Vector2(12, 12)) };
        OutputData Frame(params ServerMessage[] messages) => new() { ServerMessages = messages.ToList(), Items = new(), Chefs = new(), EntityRegistry = new(), InvalidStateReason = "", FramesSinceLastNoPhysicsFrame = 5 };
        ServerMessage Load() => new() { Type = (int)MessageType.LevelLoadByName, Message = new LevelLoadByNameMessage { m_Scene = "s_Day_3_4", m_StartLoadGameState = GameState.InLevel, m_HideLoadingScreenGameState = GameState.InLevel }.ToBytes() };
        ServerMessage Start() => new() { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() };
        var s = new HeadlessSession(Setup(), "synthetic-empty-setup", evidenceRoot);
        Check(s.Inspect()["needsFreshLevelBaseline"]!.GetValue<bool>(), "late attachment is not a complete baseline");
        Reject(() => s.Command(new() { ["command"] = "resume" }), "no fabricated initial game state");
        s.getNext(Frame(Start())).GetAwaiter().GetResult();
        Check(s.Inspect()["state"]!.ToString() == "NotInLevel", "late InLevel message alone cannot fabricate load history");
        var seedless = s.getNext(Frame(Load())).GetAwaiter().GetResult();
        Check(!seedless.__isset.resetOrderSeed && !seedless.PreventInvalidState, "upstream seed/UI prevention not enabled implicitly");
        Check(!s.Inspect()["needsFreshLevelBaseline"]!.GetValue<bool>(), "actual load message establishes observed lifecycle");
        s.getNext(Frame(Start())).GetAwaiter().GetResult();
        Check(s.Inspect()["state"]!.ToString() == "Running" && s.Inspect()["frame"]!.GetValue<int>() == 0, "native start special frame retained");
        var advancing = Frame(); advancing.FrameNumber = 1;
        s.getNext(advancing).GetAwaiter().GetResult();
        Check(s.Inspect()["frame"]!.GetValue<int>() == 1, "one running callback advances once");
        var pause = Frame(); pause.NextFramePaused = true; pause.FrameNumber = 2;
        var pausedInput = s.getNext(pause).GetAwaiter().GetResult();
        Check(s.Inspect()["state"]!.ToString() == "Paused" && s.Inspect()["frame"]!.GetValue<int>() == 2 && pausedInput.NextFrame == 2, "external bridge pause keeps final native advance");
        var stillPaused = Frame(); stillPaused.LastFramePaused = stillPaused.NextFramePaused = true; stillPaused.FrameNumber = 2;
        s.getNext(stillPaused).GetAwaiter().GetResult(); s.getNext(stillPaused).GetAwaiter().GetResult();
        Check(s.Inspect()["frame"]!.GetValue<int>() == 2, "paused callbacks do not manufacture gameplay frames");
        Reject(() => s.Command(new() { ["command"] = "warp", ["frame"] = 0 }), "warp requires explicit development mode");
        var before = s.Inspect().ToJsonString(); s.Inspect(true);
        Check(s.Inspect().ToJsonString() == before, "full inspection is read-only");
        s.Command(new() { ["command"] = "resume" });
        Check(s.Inspect()["requestPending"]!.GetValue<bool>(), "resume queues through original framework request engine");
        Reject(() => s.Command(new() { ["command"] = "resume" }), "duplicate transition rejected");
        s.getNext(stillPaused).GetAwaiter().GetResult();
        Check(s.Inspect()["state"]!.ToString() == "AwaitingPhysicsPhaseShiftAlignment", "original physics alignment retained");
        var resume = s.getNext(stillPaused).GetAwaiter().GetResult();
        Check(resume.RequestResume && resume.__isset.gameSpeed && resume.GameSpeed == 1005.0 &&
              s.Inspect()["state"]!.ToString() == "AwaitingResume" && s.Inspect()["resumePhaseMetadataEmissions"]!.GetValue<long>() == 1,
            "plain resume carries the exact saved native phase to the in-process gate without RPC phase sampling");
        s.getNext(Frame()).GetAwaiter().GetResult();
        Check(s.Inspect()["state"]!.ToString() == "Running", "native unpaused acknowledgement completes resume");
        var seeded = new HeadlessSession(Setup(), "synthetic-empty-setup", evidenceRoot, 59);
        var explicitSeed = seeded.getNext(Frame(Load())).GetAwaiter().GetResult();
        Check(explicitSeed.__isset.resetOrderSeed && explicitSeed.ResetOrderSeed == 59, "explicit seed overrides upstream UI constant");
        InputData Wire(InputData value)
        {
            using var buffer = new MemoryStream();
            using var transport = new TStreamTransport(buffer, buffer, new Thrift.TConfiguration());
            var protocol = new TBinaryProtocol(transport);
            value.WriteAsync(protocol, default).GetAwaiter().GetResult(); buffer.Position = 0;
            var copy = new InputData(); copy.ReadAsync(protocol, default).GetAwaiter().GetResult(); return copy;
        }
        var noSeedWire = Wire(seedless);
        Check(!noSeedWire.__isset.resetOrderSeed && !noSeedWire.__isset.gameSpeed && !noSeedWire.__isset.warp && noSeedWire.__isset.preventInvalidState && !noSeedWire.PreventInvalidState,
            "binary wire omits all unspecified rule-changing controls");
        Check(Wire(explicitSeed).ResetOrderSeed == 59 && Wire(explicitSeed).__isset.resetOrderSeed, "explicit seed survives binary wire");
        var actualSetup = new Carnival34FourLevel();
        Check(actualSetup.IsValid() && actualSetup.entityRecords.Chefs.Count == 4, "upstream Carnival setup loads as four-chef data, not a native validation");
        using var fixtureStream = typeof(SessionTests).Assembly.GetManifestResourceStream("Headless.Reference.NativeDInitialRegistryFixture.json")!;
        var fixture = JsonNode.Parse(fixtureStream)!.AsObject();
        var audit = CarnivalRegistryAudit.FromInspection(actualSetup, fixture);
        var validation = audit.Report();
        Check(validation["layoutValid"]!.GetValue<bool>() && validation["checkedFixedEntities"]!.GetValue<int>() == 122,
            "all actual native-d fixed registrations match independent legacy names/components and upstream initial coordinates");
        Check(validation["maximumInitialPositionDelta"]!.GetValue<double>() < .00002 && !validation["nativeQualification"]!.GetValue<bool>(), "tight actual geometry fit never claims native qualification");
        Check(validation["spawnMappings"]!.AsArray().All(m => m!["status"]!.ToString() == "unavailable-at-registration"), "actual absent spawn arrays are reported unavailable, never padded");
        void AuditReject(Action<JsonObject> mutate, string name)
        {
            var damaged = (JsonObject)fixture.DeepClone(); mutate(damaged);
            Check(!CarnivalRegistryAudit.FromInspection(new Carnival34FourLevel(), damaged).Report()["layoutValid"]!.GetValue<bool>(), name);
        }
        AuditReject(f => f["freshLevelLoadObserved"] = false, "captured values alone cannot fabricate live fresh baseline");
        AuditReject(f => f["registry"]!.AsArray().RemoveAt(0), "missing fixed registration rejects layout");
        AuditReject(f => f["registry"]![0]!["Name"] = "different object", "changed native name rejects layout");
        AuditReject(f => f["registry"]![0]!["Pos"]!["X"] = 100, "large initial position mismatch rejects layout");
        AuditReject(f => f["registry"]![0]!["Pos"] = null, "missing position is not default zero");
        AuditReject(f => f["registry"]![0]!["Components"] = new JsonArray(), "missing observed component role rejects layout");
        AuditReject(f => f["registry"]![0]!["SyncEntityTypes"] = null, "missing synchronization schema rejects layout");
        AuditReject(f => f["registry"]!.AsArray().First(n => n!["EntityId"]!.GetValue<int>() == 65)!["SpawnNames"] = new JsonArray("WrongIngredient"), "conflicting observed primary spawn rejects layout");
        var metadataAudit = CarnivalRegistryAudit.FromInspection(actualSetup, fixture);
        var refreshedCrate = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Single(e => e.EntityId == 65);
        refreshedCrate.Components.Add("SpawnableEntityCollection"); refreshedCrate.SpawnNames = new() { "Egg" }; refreshedCrate.Pos.X += .2;
        metadataAudit.Observe(new[] { refreshedCrate });
        Check(metadataAudit.Report()["layoutValid"]!.GetValue<bool>(), "additive actual metadata refresh preserves original registration position");
        Check(metadataAudit.Report()["spawnMappings"]!.AsArray().Single(m => m!["id"]!.GetValue<int>() == 65)!["status"]!.ToString() == "primary-name-matches", "latest observed spawn metadata is checked");
        var beforeRefresh = audit.Report()["observedRegistrySha256"]!.ToString();
        Check(metadataAudit.Report()["observedRegistrySha256"]!.ToString() != beforeRefresh, "receipt hash binds latest metadata as well as first position");
        refreshedCrate.Components.Remove("PickupItemSpawner"); metadataAudit.Observe(new[] { refreshedCrate });
        Check(!metadataAudit.Report()["layoutValid"]!.GetValue<bool>(), "refresh cannot erase original component identity");
        audit.RequireFixedWarpPaths(0, 0); Check(true, "explicit development warp may inspect known fixed paths without full native score qualification");
        var crate = actualSetup.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 65);
        var unknownSpawn = new GameEntityRecord { path = new EntityPath { ids = new[] { 65, 0 } }, existed = new Versioned<bool>(true) };
        crate.spawned.Add(unknownSpawn);
        Reject(() => audit.RequireFixedWarpPaths(0, 0), "unknown ordered dynamic spawn mapping rejects development warp");
        crate.spawned.Remove(unknownSpawn);
        var movement = new HeadlessSession(actualSetup, "synthetic messages over upstream Carnival setup", evidenceRoot);
        movement.getNext(Frame(Load())).GetAwaiter().GetResult();
        var initial = Frame(Start()); var chef = actualSetup.entityRecords.Chefs.Keys.First(c => c.path.ids[0] == 103);
        var position = chef.position[0];
        initial.EntityRegistry = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
        movement.getNext(initial).GetAwaiter().GetResult(); movement.getNext(Frame()).GetAwaiter().GetResult(); movement.getNext(pause).GetAwaiter().GetResult();
        Vector2 destination = new[] { new Vector2(.5f, 0), new Vector2(-.5f, 0), new Vector2(0, .5f), new Vector2(0, -.5f) }
            .Select(d => position.XZ() + d).First(d => actualSetup.mapByChef[103].FindPath(position.XZ(), new() { d }).Count >= 2);
        int controls = 0; movement.Control += _ => controls++;
        movement.Command(new() { ["command"] = "goto", ["chef"] = 103, ["x"] = destination.X, ["z"] = destination.Y, ["frames"] = 10 });
        Check(controls == 1 && actualSetup.sequences.NodeById.Count == 1 && actualSetup.sequences.NodeById.Values.Single().Action is GotoAction { DisallowDash: true }, "diagnostic movement reuses original graph and serializes one accepted command");
        movement.getNext(stillPaused).GetAwaiter().GetResult(); movement.getNext(stillPaused).GetAwaiter().GetResult();
        var walking = movement.getNext(Frame()).GetAwaiter().GetResult();
        Check(walking.Input[103].Pad.X != 0 || walking.Input[103].Pad.Y != 0, "original action/controller history emits movement axes");
        for (int i = 0; i < 9; i++) movement.getNext(Frame()).GetAwaiter().GetResult();
        movement.getNext(pause).GetAwaiter().GetResult();
        Check(movement.Inspect()["state"]!.ToString() == "Paused" && movement.Inspect()["frame"]!.GetValue<int>() == 12, "bounded movement pauses after ten advancing reconstructed frames even without arrival");
        Check(!movement.Inspect()["movementCompleted"]!.GetValue<bool>(), "timeout pause does not claim native arrival");
        var checkpoint = Hpmv.Save.GameSetup.Parser.ParseFrom(actualSetup.ToProto().ToByteArray()).FromProto();
        Check(checkpoint.sequences.NodeById.Values.Single().Action is GotoAction && checkpoint.LastEmpiricalFrame == 12, "checkpoint protobuf retains original action graph and observed frame");
        using var startsStream = typeof(SessionTests).Assembly.GetManifestResourceStream("Headless.Reference.NativeDStartFramesFixture.json")!;
        var startFixture = JsonNode.Parse(startsStream)!.AsObject(); var nativeSetup = new Carnival34FourLevel();
        var nativeStart = new HeadlessSession(nativeSetup, "captured native-d start callbacks", evidenceRoot);
        nativeStart.getNext(Frame(Load())).GetAwaiter().GetResult();
        var registrations = Frame(); registrations.EntityRegistry = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
        nativeStart.getNext(registrations).GetAwaiter().GetResult();
        var firstNative = startFixture["frames"]![0]!["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!;
        nativeStart.getNext(firstNative).GetAwaiter().GetResult();
        var flow = nativeSetup.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 107);
        Check(nativeStart.Inspect()["frame"]!.GetValue<int>() == 0, "actual first InLevel callback retains frame-zero alignment");
        Check(flow.data[0].kitchenFlowController.nextOrderId == 0, "actual InLevel callback has no order yet; do not invent one");
        nativeStart.getNext(startFixture["frames"]![1]!["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!).GetAwaiter().GetResult();
        Check(flow.data[1].kitchenFlowController.activeOrders?.Length == 1 && flow.data[1].kitchenFlowController.nextOrderId == 1,
            "actual first order arrives on following advancing callback");
        nativeStart.getNext(startFixture["frames"]![2]!["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!).GetAwaiter().GetResult();
        Check(flow.data[1].kitchenFlowController.activeOrders?.Length == 2 && flow.data[1].kitchenFlowController.nextOrderId == 2,
            "actual second order on already-paused callback is applied exactly once at unchanged frame");
        Check(nativeStart.Inspect()["frame"]!.GetValue<int>() == 1 && nativeStart.Inspect()["state"]!.ToString() == "Paused", "late native order does not manufacture elapsed time");
        nativeStart.getNext(stillPaused).GetAwaiter().GetResult();
        Check(flow.data[1].kitchenFlowController.activeOrders.Length == 2, "empty paused callback does not replay prior native orders");
        var sameFrameSetup = new Carnival34FourLevel(); var sameFrameStart = new HeadlessSession(sameFrameSetup, "synthetic InLevel plus actual captured order payload", evidenceRoot);
        sameFrameStart.getNext(Frame(Load())).GetAwaiter().GetResult(); sameFrameStart.getNext(registrations).GetAwaiter().GetResult();
        var joinedStart = firstNative.DeepCopy();
        var nativeOrder = startFixture["frames"]![1]!["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!.ServerMessages
            .Single(m => Deserializer.Deserialize(m.Type, m.Message) is EntityEventMessage e && e.m_Payload is KitchenFlowMessage);
        joinedStart.ServerMessages.Add(nativeOrder); sameFrameStart.getNext(joinedStart).GetAwaiter().GetResult();
        Check(sameFrameSetup.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 107).data[0].kitchenFlowController.activeOrders?.Length == 1,
            "synthetic same-callback InLevel order is applied without frame advance");
        var mapped = nativeSetup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]);
        var warp = WarpCalculator.CalculateWarp(mapped, nativeSetup.entityRecords, 1, 1);
        Check(warp.Entities.Count(e => e.EntityId == 107 && e.KitchenController is not null && e.PlateReturnController is not null) == 1,
            "actual mapped flow emits both kitchen and plate return blocks");
        Check(warp.Entities.Single(e => e.EntityId == 75).AttachStation is not null, "native sink attachment included");
        flow.prefab.IsKitchenFlowController = false;
        Reject(() => WarpCalculator.CalculateWarp(mapped, nativeSetup.entityRecords, 1, 1), "missing flow annotation");
        flow.prefab.IsKitchenFlowController = true; flow.prefab.Ignore = true;
        Reject(() => WarpCalculator.CalculateWarp(mapped, nativeSetup.entityRecords, 1, 1), "ignored flow annotation");
        flow.prefab.Ignore = false; mapped.Remove(107);
        Reject(() => WarpCalculator.CalculateWarp(mapped, nativeSetup.entityRecords, 1, 1), "absent live flow");
        int rawChecks = RawInputTests.Run(evidenceRoot);
        int nativeChecks = NativeObservationTests.Run();
        int actionChecks = TypedActionTests.Run(evidenceRoot);
        int registrationChecks = RegistrationPoseTests.Run();
        int traceChecks = TraceTests.Run(evidenceRoot)["checks"]!.GetValue<int>();
        int dynamicChecks = DynamicWarpTests.Run();
        checks += StatusTests.Run(evidenceRoot);
        checks += ObservedTransferTests.RunSynthetic(evidenceRoot);
        return new() { ["ok"] = true, ["checks"] = checks + rawChecks + nativeChecks + actionChecks + registrationChecks + traceChecks + dynamicChecks, ["rawInputChecks"] = rawChecks, ["nativeObservationChecks"] = nativeChecks, ["typedActionChecks"] = actionChecks, ["registrationPoseChecks"] = registrationChecks, ["traceChecks"] = traceChecks, ["dynamicWarpChecks"] = dynamicChecks, ["gameCalls"] = 0, ["qualification"] = "Synthetic offline framework session/lifecycle checks; native integration and Carnival property validation pending." };
    }
}
