using Google.Protobuf;
using Hpmv;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

public static class Story11Tests
{
    public static JsonObject Run(string evidenceRoot)
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new InvalidOperationException("Story11 bootstrap: " + name); checks++; }
        void Reject(Action action, string name) { try { action(); throw new InvalidOperationException("Story11 bootstrap accepted invalid case: " + name); } catch (InvalidOperationException ex) { if (ex.Message.StartsWith("Story11 bootstrap accepted", StringComparison.Ordinal)) throw; checks++; } }
        var setup = new Story11FourLevel(inspectionOnly: true);
        Check(setup.entityRecords.FixedEntities.Count == 0 && setup.mapByChef.Count == 0, "No invented native IDs or navigation geometry");
        var session = new HeadlessSession(setup, "synthetic Story11 lifecycle only", evidenceRoot);
        var load = new ServerMessage { Type = (int)MessageType.LevelLoadByName, Message = new LevelLoadByNameMessage
            { m_Scene = "s_sushi_1_1", m_StartLoadGameState = GameState.InLevel, m_HideLoadingScreenGameState = GameState.InLevel }.ToBytes() };
        OutputData Frame(params ServerMessage[] messages) => new() { ServerMessages = messages.ToList(), Items = new(), Chefs = new(), EntityRegistry = new(), InvalidStateReason = "", FramesSinceLastNoPhysicsFrame = 5 };
        session.getNext(Frame(load)).GetAwaiter().GetResult();
        var start = Frame(new ServerMessage { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() });
        start.LastFramePaused = start.NextFramePaused = true;
        // Synthetic unknown scene registration: validates inspection transport, not Story11 geometry.
        start.EntityRegistry.Add(new EntityRegistryData { EntityId = 999, Name = "UnmappedNativeObject", Pos = new() { X = 1, Y = 2, Z = 3 }, Components = new() { "BoxCollider" }, SyncEntityTypes = new() { 0 }, SpawnNames = new() });
        start.Chefs[43] = new ChefSpecificData();
        start.Items[43] = new ItemData { Pos = new() { X = 17, Y = .05, Z = -5 }, Rotation = new() { W = 1 } };
        var input = session.getNext(start).GetAwaiter().GetResult();
        var inspection = session.Inspect(true);
        Check(inspection["freshLevelLoadObserved"]!.GetValue<bool>() && inspection["discoveryOnly"]!.GetValue<bool>(), "Observed new scene remains discovery-only");
        Check(inspection["registry"]!.AsArray().Count == 1 && inspection["entities"]!.AsArray().Count == 0, "Unmapped actual observations retained without fabricated reconstruction");
        Check(!inspection["registryValidation"]!["layoutValid"]!.GetValue<bool>(), "Unknown layout never validates from empty expected set");
        Check(input.Input is null || input.Input.Count == 0, "Bootstrap emits no fabricated chef input");
        Check(inspection["nativeObservedChefs"]?["43"] is JsonObject && inspection["nativeObservedItems"]?["43"]?["Pos"]?["X"]?.GetValue<double>() == 17, "Unknown native chef and physics remain inspectable without empty-setup reconstruction");
        var delta = Frame(); delta.LastFramePaused = delta.NextFramePaused = true; delta.Items[43] = new ItemData { Velocity = new() { X = 2 } };
        session.getNext(delta).GetAwaiter().GetResult();
        Check(session.Inspect(true)["nativeObservedItems"]!["43"]!["Pos"]!["X"]!.GetValue<double>() == 17, "Absent native physics fields retain the earlier actual observation");
        foreach (string command in new[] { "resume", "step", "goto", "actions", "raw-input", "raw-replay", "warp", "checkpoint", "record-input" })
        {
            try { session.Command(new() { ["command"] = command }); throw new Exception("Expected discovery gate: " + command); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("inspection bootstrap"), "Explicit discovery gate: " + command); }
        }
        var restored = Hpmv.Save.GameSetup.Parser.ParseFrom(setup.ToProto().ToByteArray()).FromProto();
        Check(new HeadlessSession(restored, "replayed discovery header", evidenceRoot, discoveryOnly: true).Inspect()["registryValidation"]!["discoveryOnly"]!.GetValue<bool>(), "Serialized trace header preserves discovery gate after protobuf loses subclass");
        using var stream = typeof(Story11Tests).Assembly.GetManifestResourceStream("Headless.Reference.Story11NativeRegistryFixture.json")!;
        var fixture = JsonNode.Parse(stream)!.AsObject();
        var mapped = new Story11FourLevel();
        Check(mapped.entityRecords.FixedEntities.Count == 50 && mapped.mapByChef.Keys.Order().SequenceEqual(new[] {43,44,45,46}), "All50 native fixed paths and four actual chefs");
        var audit = CarnivalRegistryAudit.FromInspection(mapped, fixture);
        var report = audit.Report();
        Check(report["layoutValid"]!.GetValue<bool>() && report["checkedFixedEntities"]!.GetValue<int>() == 50 && report["semanticGaps"]!.AsArray().Count == 0, "Actual native fifty-record names/components/positions and flow annotation validate");
        var records = mapped.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]);
        Check(records[40].prefab.IsKitchenFlowController && records[33].prefab.IsAttachStation && records[34].prefab.IsPlateReturnStation, "Native flow, delivery and clean return roles");
        Check(records[34].prefab.Spawns.Count == 1 && records[34].prefab.Spawns[0].IsStack &&
            records[34].prefab.Spawns[0].Spawns.Count == 1 && records[34].prefab.Spawns[0].Spawns[0].CanContainIngredients,
            "Returned clean-plate hierarchy annotates the native stack and its plate child");
        var mappedRoundTrip = mapped.ToProto().FromProto();
        Check(mappedRoundTrip.entityRecords.GetRecordFromPath(new[] {34}).prefab.Spawns[0].IsStack,
            "Returned clean-plate stack annotation survives controller setup serialization");
        foreach (int id in new[] {18,20,22,23}) Check(!records[id].prefab.IsAttachStation, "Observed corner has no native AttachStation: " + id);
        var wrongCorner = new Story11FourLevel(); wrongCorner.entityRecords.GetRecordFromPath(new[]{18}).prefab.IsAttachStation = true;
        Check(!CarnivalRegistryAudit.FromInspection(wrongCorner, fixture).Report()["layoutValid"]!.GetValue<bool>(), "Invented native component annotation rejects before control or warp");
        Check(records[29].prefab.Spawns[0].Name == "SushiPrawn" && records[30].prefab.Spawns[0].Name == "SushiFish", "Actual native ordered crate names");
        Check(records[29].prefab.Spawns[0].Spawns[0].IngredientId == 21875 && records[30].prefab.Spawns[0].Spawns[0].IngredientId == 23600, "Matching ingredient asset recipe IDs");
        Check(records[29].prefab.Spawns[0].MaxProgress == 1.4 && records[30].prefab.Spawns[0].MaxProgress == 1.4, "Eight stages finish at seven increments; original framework estimate labelled separately from native proof");
        byte[] beforeProof=mapped.ToProto().ToByteArray();
        var currentProof=audit.RequireGraphMappings(records, 0);
        Check(currentProof["ok"]!.GetValue<bool>() && currentProof["frame"]!.GetValue<int>() == 0 && currentProof["spawnedMappings"]!.AsArray().Count == 0 && currentProof["removedNativeIds"]!.AsArray().Count == 0, "Read-only current mapping proof binds its exact observation frame");
        Check(beforeProof.SequenceEqual(mapped.ToProto().ToByteArray()), "Current mapping inspection preserves every serialized setup field");
        (CarnivalRegistryAudit Audit, Dictionary<int, GameEntityRecord> Live, EntityRegistryData Owner, EntityRegistryData Body) ReincarnationFixture()
        {
            var reincarnationSetup = new Story11FourLevel();
            var reincarnationAudit = CarnivalRegistryAudit.FromInspection(reincarnationSetup, fixture);
            var owner = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Single(entry => entry.EntityId == 2).DeepCopy();
            var body = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Single(entry => entry.EntityId == 47).DeepCopy();
            owner.Name = "equipment_plate_01"; owner.Pos = new() { X = 21.600000381469727, Y = 0, Z = -3.6000001430511475 };
            owner.Components.Remove("CachedObject"); owner.SpawnNames = null;
            body.Name = "equipment_plate_01(Clone)_Rigidbody"; body.Pos = owner.Pos.DeepCopy(); body.Components.Remove("CachedObject");
            body.Components.AddRange(new[] { "DynamicLandscapeParenting", "ServerDynamicLandscapeParenting", "ClientDynamicLandscapeParenting" }); body.SpawnNames = null;
            reincarnationAudit.Observe(new[] { owner, body });
            return (reincarnationAudit, reincarnationSetup.entityRecords.FixedEntities.ToDictionary(entity => entity.path.ids[0]), owner, body);
        }
        var reincarnation = ReincarnationFixture();
        Reject(() => reincarnation.Audit.RequireGraphMappings(reincarnation.Live, 444), "fixed recreation cannot validate before successful-warp rebranch");
        var fixedRebranch = reincarnation.Audit.RebranchAfterSuccessfulWarp(reincarnation.Live, 444, new HashSet<int> { 2, 47, 55, 56 });
        Check(fixedRebranch["fixedReincarnation"]!["active"]!.GetValue<bool>() && fixedRebranch["fixedReincarnation"]!["currentMetadataExact"]!.GetValue<bool>() &&
            reincarnation.Audit.RequireGraphMappings(reincarnation.Live, 444)["fixedMappingsValidated"]!.GetValue<bool>(),
            "exact successful-warp plate/container pair rebases fixed validation bookkeeping");
        var missingPair = ReincarnationFixture();
        Reject(() => missingPair.Audit.RebranchAfterSuccessfulWarp(missingPair.Live, 444, new HashSet<int> { 2 }), "fixed recreation requires both IDs together");
        foreach (string mutation in new[] { "name", "component", "sync", "position" })
        {
            var invalid = ReincarnationFixture();
            if (mutation == "name") invalid.Owner.Name = "another plate";
            if (mutation == "component") invalid.Body.Components.Remove("DynamicLandscapeParenting");
            if (mutation == "sync") invalid.Owner.SyncEntityTypes[0]++;
            if (mutation == "position") invalid.Body.Pos.X += .001;
            invalid.Audit.Observe(new[] { invalid.Owner, invalid.Body });
            Reject(() => invalid.Audit.RebranchAfterSuccessfulWarp(invalid.Live, 444, new HashSet<int> { 2, 47 }), "fixed recreation rejects " + mutation + " mutation");
        }
        reincarnation.Owner.Name = "mutated after authorization"; reincarnation.Audit.Observe(new[] { reincarnation.Owner });
        Reject(() => reincarnation.Audit.RequireGraphMappings(reincarnation.Live, 444), "later fixed recreation metadata mutation remains poisoned");
        Check(records[1].data[0].attachmentParent is null && records[13].data[0].attachment is null, "No position-inferred attachment inserted before native messages");
        var decoded = Hpmv.Save.GameSetup.Parser.ParseFrom(mapped.ToProto().ToByteArray()).FromProto();
        var replayAudit = new CarnivalRegistryAudit(decoded, levelProfile: "story11"); replayAudit.Reset();
        replayAudit.Observe(fixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!);
        replayAudit.Observe(fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!);
        replayAudit.ObserveSettledChefInspection(fixture);
        Check(replayAudit.Report()["layoutValid"]!.GetValue<bool>(), "Explicit trace level profile survives protobuf round trip");
        using var phaseStream = typeof(Story11Tests).Assembly.GetManifestResourceStream("Headless.Reference.Story11FreshChefPhaseFixture.json")!;
        var phaseFixture = JsonNode.Parse(phaseStream)!.AsObject();
        var phaseAudit = CarnivalRegistryAudit.FromInspection(new Story11FourLevel(), phaseFixture);
        var phaseReport = phaseAudit.Report();
        Check(phaseReport["layoutValid"]!.GetValue<bool>(), "Actual fresh-prep-a early chef Y differs while registered XZ and native settled XYZ validate");
        Check(phaseReport["chefStartupPhases"]!.AsArray().Count == 4 && phaseReport["chefStartupPhases"]!.AsArray().All(p => Math.Abs(p!["registrationYDelta"]!.GetValue<double>()) > CarnivalRegistryAudit.PositionTolerance && p["settledPositionDelta"]!.GetValue<double>() == 0), "All four raw early Y discrepancies remain explicit, exact settled reference retained");
        foreach (var mutation in new[] { "chef-registration-x", "chef-registration-z", "chef-settled-y", "chef-settled-x", "station-registration-y", "missing-settled" }) {
            var changed = phaseFixture.DeepClone().AsObject();
            var list = changed[mutation.Contains("settled") ? "registry" : "initialRegistry"]!.AsArray();
            var row = list.Single(e => e!["EntityId"]!.GetValue<int>() == (mutation.StartsWith("station") ? 30 : 43))!;
            if (mutation == "missing-settled") list.Remove(row);
            else { string axis = mutation.EndsWith("-x") ? "X" : mutation.EndsWith("-z") ? "Z" : "Y"; row["Pos"]![axis] = row["Pos"]![axis]!.GetValue<double>() + .001; }
            Check(!CarnivalRegistryAudit.FromInspection(new Story11FourLevel(), changed).Report()["layoutValid"]!.GetValue<bool>(), "Phase-specific position mutant rejects: " + mutation);
        }
        var liveAudit = new CarnivalRegistryAudit(new Story11FourLevel()); liveAudit.Reset();
        liveAudit.Observe(phaseFixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!);
        Check(!liveAudit.Report()["layoutValid"]!.GetValue<bool>(), "Early registration alone cannot prove settled startup pose");
        var earlyStart = Frame(new ServerMessage { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() });
        foreach (var e in phaseFixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Where(e => e.EntityId is >=43 and <=46)) earlyStart.Items[e.EntityId] = new ItemData { Pos = e.Pos.DeepCopy() };
        liveAudit.ObserveStartupFrame(earlyStart);
        Check(liveAudit.SettledChefPositions().Count == 0 && !liveAudit.Report()["layoutValid"]!.GetValue<bool>(), "Registration-like first InLevel chef positions are ignored rather than pinned as settled receipts");
        var nativeStart = Frame(new ServerMessage { Type = (int)MessageType.GameState, Message = new GameStateMessage { m_State = GameState.InLevel }.ToBytes() });
        foreach (var e in phaseFixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Where(e => e.EntityId is >=43 and <=46)) nativeStart.Items[e.EntityId] = new ItemData { Pos = e.Pos.DeepCopy() };
        nativeStart.ServerMessages.Clear();
        liveAudit.ObserveStartupFrame(nativeStart);
        Check(liveAudit.SettledChefPositions().Count == 4 && liveAudit.Report()["layoutValid"]!.GetValue<bool>(), "Later matching chef positions prove live settled phase independently of metadata refresh");
        string receiptBefore = liveAudit.SettledChefPositions().ToJsonString();
        nativeStart.ServerMessages.Clear(); nativeStart.Items[43].Pos.Y = 9;
        liveAudit.ObserveStartupFrame(nativeStart);
        Check(liveAudit.SettledChefPositions().ToJsonString() == receiptBefore && liveAudit.Report()["layoutValid"]!.GetValue<bool>(), "Subsequent movement/warp cannot overwrite startup phase proof");
        var wrongOnlyAudit = new CarnivalRegistryAudit(new Story11FourLevel()); wrongOnlyAudit.Reset();
        wrongOnlyAudit.Observe(phaseFixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!);
        wrongOnlyAudit.ObserveStartupFrame(earlyStart);
        earlyStart.ServerMessages.Clear();
        wrongOnlyAudit.ObserveStartupFrame(earlyStart);
        Check(wrongOnlyAudit.SettledChefPositions().Count == 0 && !wrongOnlyAudit.Report()["layoutValid"]!.GetValue<bool>(), "Wrong-only post-InLevel chef positions remain missing and rejected");
        liveAudit.Reset(); liveAudit.Observe(phaseFixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!); liveAudit.ObserveStartupFrame(nativeStart);
        Check(!liveAudit.Report()["layoutValid"]!.GetValue<bool>() && liveAudit.SettledChefPositions().Count == 0, "Fresh level resets phase evidence and rejects positions before new native InLevel");
        foreach (string mutation in new[] {"name", "position", "component", "spawn", "missing"}) {
            var changed = fixture.DeepClone().AsObject();
            foreach (string list in new[] { "registry", "initialRegistry" }) {
                var entry = changed[list]!.AsArray().Single(e => e!["EntityId"]!.GetValue<int>() == 30)!;
                if (mutation == "name") entry["Name"] = "WrongCrate";
                if (mutation == "position") entry["Pos"]!["X"] = 8.2;
                if (mutation == "component") entry["Components"]!.AsArray().Remove(entry["Components"]!.AsArray().Single(e => e!.ToString() == "PickupItemSpawner"));
                if (mutation == "spawn" && list == "registry") entry["SpawnNames"] = new JsonArray("SushiPrawn");
                if (mutation == "missing") changed[list]!.AsArray().Remove(entry);
            }
            Check(!CarnivalRegistryAudit.FromInspection(new Story11FourLevel(), changed).Report()["layoutValid"]!.GetValue<bool>(), "Native registry mutation rejects: " + mutation);
        }
        // Source-derived approach positions lie inside chef-clearance polygons.
        foreach (int chef in new[] {43,44,45,46})
            foreach (var endpoint in new[] {new Vector2(8.3f,-3.6f), new Vector2(20.5f,-4.8f), new Vector2(9.6f,-6.1f), new Vector2(19.2f,-6.1f), new Vector2(8.4f,-6.1f), new Vector2(20.4f,-6.1f), new Vector2(20.5f,-1.8f)}) {
                var p=records[chef].position[0]; var path=mapped.mapByChef[chef].FindPath(new Vector2(p.X,p.Z),new(){endpoint});
                Check(path is not null && path.Count > 0, "Modeled approach reachable for chef " + chef + " to " + endpoint);
            }
        return new() { ["ok"] = true, ["checks"] = checks, ["gameCalls"] = 0, ["referenceSourceSha256"] = report["referenceSourceSha256"]!.DeepClone(),
            ["scope"] = "Actual native50-record registry validation/mutations, original graph/setup serialization and source-polygon reachability; native collision paths, chopping timings and delivery outcome require live receipts." };
    }
}
