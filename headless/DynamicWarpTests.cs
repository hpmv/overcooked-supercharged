using Hpmv;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Numerics;
using Thrift.Protocol;
using Thrift.Transport.Client;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

public static class DynamicWarpTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool yes, string label) { if (!yes) throw new InvalidOperationException("Dynamic mapping fixture: " + label); checks++; }
        void Reject(Action action, string label) { try { action(); } catch (InvalidOperationException) { checks++; return; } throw new InvalidOperationException("Expected dynamic mapping rejection: " + label); }
        using var source = typeof(DynamicWarpTests).Assembly.GetManifestResourceStream("Headless.Reference.NativeGInitialWorldFixture.json")!;
        var native = JsonNode.Parse(source)!.AsObject();
        (GameSetup setup, CarnivalRegistryAudit audit, Dictionary<int, GameEntityRecord> live, GameEntityRecord raw, GameEntityRecord prepared) Fixture(bool captureRaw = true)
        {
            var setup = new Carnival34FourLevel(); var audit = CarnivalRegistryAudit.FromInspection(setup, native);
            var live = setup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]); var crate = live[68];
            audit.ObserveMappedEntities(live, 0);
            var raw = new GameEntityRecord { path = new() { ids = new[] { 68, 0 } }, prefab = crate.prefab.Spawns[0], spawner = crate,
                existed = new(false), position = new(Vector3.Zero) }; raw.existed.ChangeTo(true, 10); crate.spawned.Add(raw); live[123] = raw;
            // Root HotdogBun is actual native-G metadata. Nested prepared name
            // and lifecycle below are explicitly synthetic supplements, not a
            // claim that a native chopping/rewind probe has already passed.
            audit.Observe(new[] { new EntityRegistryData { EntityId = 123, Name = "HotdogBun", Pos = new(), Components = new() { "PhysicalAttachment", "SpawnableEntityCollection" },
                SyncEntityTypes = new() { (int)EntityType.PhysicalAttach }, SpawnNames = new() { "fixture.PreparedBun" } } });
            if (captureRaw) audit.ObserveMappedEntities(live, 10);
            raw.existed.ChangeTo(false, 20); live.Remove(123); audit.ObserveRemoval(123);
            var prepared = new GameEntityRecord { path = new() { ids = new[] { 68, 0, 0 } }, prefab = raw.prefab.Spawns[0], spawner = raw,
                existed = new(false), position = new(Vector3.Zero) }; prepared.existed.ChangeTo(true, 20); raw.spawned.Add(prepared); live[124] = prepared;
            audit.Observe(new[] { new EntityRegistryData { EntityId = 124, Name = "fixture.PreparedBun", Pos = new(), Components = new() { "PhysicalAttachment", "IngredientContainer" },
                SyncEntityTypes = new() { (int)EntityType.PhysicalAttach }, SpawnNames = new() } }); audit.ObserveMappedEntities(live, 20);
            return (setup, audit, live, raw, prepared);
        }
        var caps = new NativeWarpCapabilities { Version = 1, Features = 1 };
        (GameSetup setup, CarnivalRegistryAudit audit, Dictionary<int, GameEntityRecord> live,
            GameEntityRecord station, GameEntityRecord stack, GameEntityRecord plate,
            List<ServerMessage> messages) ReturnedPlateFixture(bool observeSpawnHeaders = true, bool corruptPlateParent = false)
        {
            using var storySource = typeof(DynamicWarpTests).Assembly.GetManifestResourceStream("Headless.Reference.Story11NativeRegistryFixture.json")!;
            var storyNative = JsonNode.Parse(storySource)!.AsObject();
            var storySetup = new Story11FourLevel();
            var audit = CarnivalRegistryAudit.FromInspection(storySetup, storyNative);
            var live = storySetup.entityRecords.FixedEntities.ToDictionary(entity => entity.path.ids[0]);
            var station = live[34];
            var stack = new GameEntityRecord { path = new() { ids = new[] { 34, 0 } }, prefab = station.prefab.Spawns[0], spawner = station,
                existed = new(false), position = new(Vector3.Zero) };
            stack.existed.ChangeTo(true, 866); station.spawned.Add(stack); live[55] = stack;
            var plate = new GameEntityRecord { path = new() { ids = new[] { 34, 0, 0 } }, prefab = stack.prefab.Spawns[0], spawner = stack,
                existed = new(false), position = new(Vector3.Zero) };
            plate.existed.ChangeTo(true, 866); stack.spawned.Add(plate); live[57] = plate;
            var stationData = station.data.Last(); stationData.attachment = stack;
            stationData.plateReturnStationStack = stack; station.data.ChangeTo(stationData, 866);
            var stackData = stack.data.Last(); stackData.stackContents = new() { plate }; stack.data.ChangeTo(stackData, 866);
            audit.Observe(new[] {
                new EntityRegistryData { EntityId = 55, Name = "CleanPlateStack", Pos = new(), Components = new() { "PhysicalAttachment", "CleanPlateStack", "Stack", "BoxCollider" },
                    SyncEntityTypes = new() { (int)EntityType.PhysicalAttach }, SpawnNames = null },
                new EntityRegistryData { EntityId = 56, Name = "CleanPlateStack(Clone)_Rigidbody", Pos = new(), Components = new() { "Rigidbody", "ObjectContainer" },
                    SyncEntityTypes = new() { (int)EntityType.PhysicsObject }, SpawnNames = null },
                new EntityRegistryData { EntityId = 57, Name = "equipment_plate_01", Pos = new(), Components = new() { "PhysicalAttachment", "Plate", "BoxCollider" },
                    SyncEntityTypes = new() { (int)EntityType.PhysicalAttach }, SpawnNames = null },
                new EntityRegistryData { EntityId = 58, Name = "equipment_plate_01(Clone)_Rigidbody", Pos = new(), Components = new() { "Rigidbody", "ObjectContainer" },
                    SyncEntityTypes = new() { (int)EntityType.PhysicsObject }, SpawnNames = null }
            });
            audit.ObserveMappedEntities(live, 866);
            var stackSpawn = new SpawnPhysicalAttachmentMessage();
            stackSpawn.Initialise(new() { m_uEntityID = 34 }, 0, new() { m_uEntityID = 55 }, new UnityEngine.Vector3(), new UnityEngine.Quaternion(), new() { m_uEntityID = 56 });
            var plateSpawn = new SpawnPhysicalAttachmentMessage();
            plateSpawn.Initialise(new() { m_uEntityID = (uint)(corruptPlateParent ? 34 : 55) }, 0, new() { m_uEntityID = 57 }, new UnityEngine.Vector3(), new UnityEngine.Quaternion(), new() { m_uEntityID = 58 });
            var messages = new List<ServerMessage> {
                new() { Type = (int)MessageType.SpawnPhysicalAttachment, Message = stackSpawn.ToBytes() },
                new() { Type = (int)MessageType.SpawnPhysicalAttachment, Message = plateSpawn.ToBytes() }
            };
            if (observeSpawnHeaders) audit.ObserveSpawnMappings(messages, live, 866);
            audit.ObservePhysicalContainers(messages, live);
            return (storySetup, audit, live, station, stack, plate, messages);
        }
        var missingPlateHeader = ReturnedPlateFixture(observeSpawnHeaders: false);
        Reject(() => missingPlateHeader.audit.RequireGraphMappings(missingPlateHeader.live, 866),
            "stack child with no ordered collection metadata cannot be inferred without its decoded native spawn headers");
        var returnedPlate = ReturnedPlateFixture();
        var returnedMappings = returnedPlate.audit.RequireGraphMappings(returnedPlate.live, 866);
        Check(returnedMappings["spawnedMappings"]!.AsArray().Count == 2 && returnedMappings["observedPhysicalContainers"]!.AsArray().Count == 2,
            "actual returned stack/plate spawn and both physical-container headers admit the complete live graph");
        Check(returnedMappings["spawnedMappings"]!.AsArray().Single(row => row!["id"]!.GetValue<int>() == 57)!["mappingSource"]!.ToString() == "decoded-native-spawn-header",
            "returned plate path explicitly reports the decoded spawn-header fallback instead of fabricated SpawnNames");
        var returnedWarp = WarpCalculator.CalculateWarp(returnedPlate.live, returnedPlate.setup.entityRecords, 866, 866);
        var returnedStackWarp = returnedWarp.Entities.Single(entity => entity.__isset.entityId && entity.EntityId == 55);
        Check(returnedStackWarp.Stack?.StackContents.Count == 1 && returnedStackWarp.Stack.StackContents[0].__isset.entityId &&
            returnedStackWarp.Stack.StackContents[0].EntityId == 57,
            "returned native stack emits one explicit Stack block containing plate57");
        var returnStationWarp = returnedWarp.Entities.Single(entity => entity.__isset.entityId && entity.EntityId == 34);
        Check(returnStationWarp.PlateReturnStation?.Stack?.__isset.entityId == true && returnStationWarp.PlateReturnStation.Stack.EntityId == 55,
            "plate-return station retains its explicit returned-stack reference");
        var retiredStack = ReturnedPlateFixture();
        var retiredStackSimulator = new RealGameSimulator {
            setup = retiredStack.setup,
            entityIdToRecord = retiredStack.live
        };
        retiredStackSimulator.SetFrameAfterWarping(867);
        var stackTaken = new EntityEventMessage();
        stackTaken.Initialise(new() { m_uEntityID = 34 }, 0, new AttachStationMessage { m_item = -1 });
        retiredStackSimulator.ApplyGameUpdate(stackTaken);
        Check(retiredStack.station.data[867].attachment is null &&
              retiredStack.station.data[867].plateReturnStationStack is null,
            "taking the returned stack mirrors ServerPlateReturnStation.OnItemRemoved and clears both station references");
        Check(ReferenceEquals(retiredStack.station.data[866].attachment, retiredStack.stack) &&
              ReferenceEquals(retiredStack.station.data[866].plateReturnStationStack, retiredStack.stack),
            "taking the returned stack preserves the exact earlier station history for backward rewind");
        retiredStackSimulator.ApplyGameUpdate(new DestroyEntitiesMessage { m_rootId = 55 });
        Check(retiredStack.station.data[867].plateReturnStationStack is null,
            "destroyed returned stack cannot revive the cleared plate-return station reference");
        var retiredStackWarp = WarpCalculator.CalculateWarp(retiredStack.live, retiredStack.setup.entityRecords, 867, 867);
        Check(retiredStackWarp.Entities.Single(entity => entity.__isset.entityId && entity.EntityId == 34)
                .PlateReturnStation?.Stack is null,
            "rewind after returned-stack retirement emits an explicit null station stack instead of a dangling path");
        var legacyStaleStack = ReturnedPlateFixture();
        legacyStaleStack.stack.existed.ChangeTo(false, 867); legacyStaleStack.live.Remove(55);
        Check(WarpCalculator.CalculateWarp(legacyStaleStack.live, legacyStaleStack.setup.entityRecords, 867, 867)
                .Entities.Single(entity => entity.__isset.entityId && entity.EntityId == 34)
                .PlateReturnStation?.Stack is null,
            "legacy stale station history cannot serialize a target-absent returned stack path");
        var directStackRetirement = ReturnedPlateFixture();
        var directStackSimulator = new RealGameSimulator {
            setup = directStackRetirement.setup,
            entityIdToRecord = directStackRetirement.live
        };
        directStackSimulator.SetFrameAfterWarping(867);
        directStackSimulator.ApplyGameUpdate(new DestroyEntitiesMessage { m_rootId = 55 });
        Check(directStackRetirement.station.data[867].plateReturnStationStack is null,
            "direct returned-stack retirement clears its exact station pointer without a preceding station message");
        var unrelatedRetirement = ReturnedPlateFixture();
        var unrelatedRetirementSimulator = new RealGameSimulator {
            setup = unrelatedRetirement.setup,
            entityIdToRecord = unrelatedRetirement.live
        };
        unrelatedRetirementSimulator.SetFrameAfterWarping(867);
        unrelatedRetirementSimulator.ApplyGameUpdate(new DestroyEntitiesMessage { m_rootId = 57 });
        Check(ReferenceEquals(unrelatedRetirement.station.data[867].plateReturnStationStack, unrelatedRetirement.stack),
            "unrelated retirement does not clear a live returned-stack station pointer");
        var missingStackAnnotation = ReturnedPlateFixture(); missingStackAnnotation.stack.prefab.IsStack = false;
        Check(WarpCalculator.CalculateWarp(missingStackAnnotation.live, missingStackAnnotation.setup.entityRecords, 866, 866)
                .Entities.Single(entity => entity.__isset.entityId && entity.EntityId == 55).Stack is null,
            "clearing the Story11 stack annotation reproduces the missing native Stack block");
        var corruptedPlateHeader = ReturnedPlateFixture(corruptPlateParent: true);
        Reject(() => corruptedPlateHeader.audit.RequireGraphMappings(corruptedPlateHeader.live, 866),
            "spawn header naming a different live parent poisons the returned plate mapping");
        returnedPlate.audit.RebranchAfterSuccessfulWarp(returnedPlate.live, 866, new HashSet<int> { 55, 56, 57, 58 });
        Reject(() => returnedPlate.audit.RequireGraphMappings(returnedPlate.live, 866),
            "fresh reincarnations cannot inherit a prior incarnation's decoded spawn-header proof");
        returnedPlate.audit.ObserveSpawnMappings(returnedPlate.messages, returnedPlate.live, 866);
        Check(returnedPlate.audit.RequireGraphMappings(returnedPlate.live, 866)["spawnedMappings"]!.AsArray().Count == 2,
            "fresh exact spawn headers reauthorize both returned-plate logical paths after rebranch");
        var consumedParentHeader = Fixture();
        var replacementSpawn = new SpawnPhysicalAttachmentMessage();
        replacementSpawn.Initialise(new() { m_uEntityID = 123 }, 0, new() { m_uEntityID = 124 }, new UnityEngine.Vector3(), new UnityEngine.Quaternion(), new() { m_uEntityID = 125 });
        consumedParentHeader.audit.ObserveSpawnMappings(new[] { new ServerMessage {
            Type = (int)MessageType.SpawnPhysicalAttachment, Message = replacementSpawn.ToBytes() } }, consumedParentHeader.live, 20);
        Check(consumedParentHeader.audit.RequireGraphMappings(consumedParentHeader.live, 20)["spawnedMappings"]!.AsArray().Count == 1,
            "same-packet consumed parent leaves its unauthenticatable header unrecorded and uses retained ordered metadata");
        var f = Fixture();
        var report = f.audit.RequireWarpPaths(f.live, 20, 10, caps);
        Check(report["dynamicMappings"]!.AsArray().Count == 2, "consumed raw target and current prepared descendant both validated");
        Check(report["dynamicMappings"]![0]!["nativeName"]!.ToString() == "HotdogBun" && f.raw.prefab.Name == "Bun", "native observed index binds friendly framework label without renaming source");
        Check(f.audit.RequireGraphMappings(f.live, 20)["spawnedMappings"]!.AsArray().Count == 1, "prepared actions permit observed consumed historical parent");
        Check(f.audit.RequireWarpPaths(f.live, 20, 0, caps)["dynamicMappings"]!.AsArray().Count == 1, "dynamic current to fixed baseline deletion is a gated candidate");
        var branch = Fixture();
        branch.live.Remove(124); branch.live[123] = branch.raw;
        branch.audit.Observe(new[] { new EntityRegistryData { EntityId = 123, Name = "HotdogBun", Pos = new(),
            Components = new() { "PhysicalAttachment", "SpawnableEntityCollection" }, SyncEntityTypes = new() { (int)EntityType.PhysicalAttach }, SpawnNames = null } });
        var rebranch = branch.audit.RebranchAfterSuccessfulWarp(branch.live, 10, new HashSet<int> { 123 });
        Check(rebranch["discardedFuturePaths"]!.AsArray().Any(path => path!.ToJsonString() == "[68,0,0]") &&
            rebranch["rebasedCurrentMappings"]!.AsArray().Any(row => row!["id"]!.GetValue<int>() == 123 && row["preservedPriorMetadata"]!.GetValue<bool>()),
            "verified rewind discards abandoned prepared receipt and rebases only freshly registered raw mapping");
        branch.audit.ObserveMappedEntities(branch.live, 10);
        branch.live.Remove(123); branch.audit.ObserveRemoval(123); branch.live[124] = branch.prepared;
        branch.audit.Observe(new[] { new EntityRegistryData { EntityId = 124, Name = "fixture.PreparedBun", Pos = new(),
            Components = new() { "PhysicalAttachment", "IngredientContainer" }, SyncEntityTypes = new() { (int)EntityType.PhysicalAttach }, SpawnNames = new() } });
        branch.audit.ObserveMappedEntities(branch.live, 20);
        Check(branch.audit.RequireGraphMappings(branch.live, 20)["spawnedMappings"]!.AsArray().Count == 1,
            "replayed prepared incarnation validates without an abandoned-branch object receipt");
        var sameDynamic = Fixture();
        var sameDynamicRebranch = sameDynamic.audit.RebranchAfterSuccessfulWarp(sameDynamic.live, 20, new HashSet<int>());
        Check(sameDynamicRebranch["freshlyRegisteredIds"]!.AsArray().Count == 0 &&
            sameDynamicRebranch["discardedFuturePaths"]!.AsArray().Count == 0 &&
            sameDynamicRebranch["retainedHistoricalAncestorPaths"]!.AsArray().Any(path => path!.ToJsonString() == "[68,0]") &&
            sameDynamic.audit.RequireGraphMappings(sameDynamic.live, 20)["spawnedMappings"]!.AsArray().Count == 1,
            "same dynamic target without fresh registration retains the consumed native parent receipt");
        var deeperHistory = Fixture();
        deeperHistory.prepared.existed.ChangeTo(false, 30);
        deeperHistory.live.Remove(124); deeperHistory.audit.ObserveRemoval(124);
        var deeperHistoryRebranch = deeperHistory.audit.RebranchAfterSuccessfulWarp(deeperHistory.live, 30, new HashSet<int>());
        Check(deeperHistoryRebranch["discardedFuturePaths"]!.AsArray().Count == 0,
            "later fixed target does not discard mappings first observed on its retained history");
        Check(deeperHistoryRebranch["retainedHistoricalPaths"]!.AsArray().Any(path => path!.ToJsonString() == "[68,0]") &&
            deeperHistoryRebranch["retainedHistoricalPaths"]!.AsArray().Any(path => path!.ToJsonString() == "[68,0,0]"),
            "later fixed target reports both consumed historical mappings as retained");
        Check(deeperHistory.audit.RequireWarpPaths(deeperHistory.live, 30, 10, caps)["dynamicMappings"]!.AsArray().Count == 1,
            "retained raw mapping admits a subsequent deeper rewind");
        var preparedHistoryWarp = deeperHistory.audit.RequireWarpPaths(deeperHistory.live, 30, 20, caps);
        Check(preparedHistoryWarp["dynamicMappings"]!.AsArray().Count == 1 &&
            preparedHistoryWarp["dynamicMappings"]![0]!["spawnChain"]!.AsArray().Count == 3,
            "retained consumed-parent receipt authenticates the prepared mapping's complete chain on a subsequent deeper rewind");
        sameDynamic.live.Remove(124);
        var fixedBaselineRebranch = sameDynamic.audit.RebranchAfterSuccessfulWarp(sameDynamic.live, 0, new HashSet<int>());
        Check(fixedBaselineRebranch["discardedFuturePaths"]!.AsArray().Any(path => path!.ToJsonString() == "[68,0]") &&
            fixedBaselineRebranch["discardedFuturePaths"]!.AsArray().Any(path => path!.ToJsonString() == "[68,0,0]") &&
            fixedBaselineRebranch["retainedHistoricalAncestorPaths"]!.AsArray().Count == 0,
            "fixed target still discards all dynamic receipts from the abandoned future");
        var proxyRetirements = new JsonArray {
            new JsonObject { ["nativeId"] = 52, ["frame"] = 200 },
            new JsonObject { ["nativeId"] = 54, ["frame"] = 283 },
            new JsonObject { ["nativeId"] = 56, ["frame"] = 1090 },
            new JsonObject { ["nativeId"] = 58, ["frame"] = 444 }
        };
        var proxyRebranch = HeadlessSession.RebranchProxyRetirements(proxyRetirements, 444, new HashSet<int> { 58 });
        Check(proxyRetirements.Select(row => row!["nativeId"]!.GetValue<int>()).SequenceEqual(new[] { 52, 54 }),
            "successful rewind retains only proxy retirements belonging to target history");
        Check(proxyRebranch["discardedIds"]!.AsArray().Select(row => row!.GetValue<int>()).SequenceEqual(new[] { 56, 58 }) &&
            proxyRebranch["discarded"]!.AsArray().Single(row => row!["nativeId"]!.GetValue<int>() == 56)!["abandonedFuture"]!.GetValue<bool>() &&
            proxyRebranch["discarded"]!.AsArray().Single(row => row!["nativeId"]!.GetValue<int>() == 58)!["reincarnatedAtTarget"]!.GetValue<bool>() &&
            !proxyRebranch["nativeStateChanged"]!.GetValue<bool>(),
            "successful rewind expires abandoned-future and reincarnated proxy receipts without a native mutation");
        var bodyMetadata = new EntityRegistryData { EntityId = 125, Name = "fixture.PreparedBun_Rigidbody", Pos = new(), Components = new() { "Rigidbody", "ObjectContainer" }, SyncEntityTypes = new() { (int)EntityType.PhysicsObject } };
        f.audit.Observe(new[] { bodyMetadata });
        Reject(() => f.audit.RequireWarpPaths(f.live, 20, 10, caps), "a Rigidbody name/type alone never excuses an unknown registered object");
        var physicalSpawn = new SpawnPhysicalAttachmentMessage();
        physicalSpawn.Initialise(new() { m_uEntityID = 123 }, 0, new() { m_uEntityID = 124 }, new UnityEngine.Vector3(), new UnityEngine.Quaternion(), new() { m_uEntityID = 125 });
        f.audit.ObservePhysicalContainers(new[] { new ServerMessage { Type = (int)MessageType.SpawnPhysicalAttachment, Message = physicalSpawn.ToBytes() } }, f.live);
        Check(f.audit.RequireWarpPaths(f.live, 20, 10, caps)["observedPhysicalContainers"]!.AsArray().Count == 1, "actual encoded logical/container header association retains separate native proxy ID");
        bodyMetadata.Components.Remove("ObjectContainer"); f.audit.Observe(new[] { bodyMetadata });
        Reject(() => f.audit.RequireWarpPaths(f.live, 20, 10, caps), "native proxy component mutation rejects");
        f.audit.ObserveRemoval(125);
        Check(f.audit.RequireWarpPaths(f.live, 20, 10, caps)["observedPhysicalContainers"]!.AsArray().Count == 0, "actual proxy removal retires only its association");
        Reject(() => f.audit.RequireWarpPaths(f.live, 20, 10, null), "absent plugin capability");
        Reject(() => f.audit.RequireWarpPaths(f.live, 20, 10, new() { Version = 2, Features = 1 }), "unknown capability version");
        Reject(() => f.audit.RequireWarpPaths(f.live, 20, 10, new() { Version = 1, Features = 0 }), "dynamic feature absent");
        var missing = Fixture(false); Reject(() => missing.audit.RequireWarpPaths(missing.live, 20, 10, caps), "consumed parent receipt may not be inferred from its child");
        var wrong = Fixture(); wrong.raw.path.ids[1] = 1;
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "historical path ordinal mutation");
        wrong = Fixture(); wrong.live.Remove(68);
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "original root must remain current");
        wrong = Fixture(); wrong.raw.prefab = new PrefabRecord("Bun");
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "record prefab incarnation cannot silently change");
        wrong = Fixture(); wrong.live[125] = wrong.prepared;
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "duplicate live native IDs cannot alias one record");
        wrong = Fixture(); wrong.audit.Observe(new[] { new EntityRegistryData { EntityId = 124, Name = "different native food", Pos = new(), Components = new() { "PhysicalAttachment" }, SyncEntityTypes = new() { 47 }, SpawnNames = new() } });
        wrong.audit.ObserveMappedEntities(wrong.live, 20);
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "native identity/component changes are retained as faults");
        wrong = Fixture(); wrong.raw.prefab.Spawns.Add(new PrefabRecord("unobserved extra option"));
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "complete native ordered list cannot be padded");
        wrong = Fixture(); wrong.raw.prefab.SpawningPath = null;
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "missing calculator spawn path cannot silently omit target");
        wrong = Fixture(); wrong.raw.prefab.SpawningPath.InitialFixedEntityId = 69;
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "calculator may not choose a different native spawn root");
        wrong = Fixture(); wrong.audit.Reset();
        Reject(() => wrong.audit.RequireWarpPaths(wrong.live, 20, 10, caps), "fresh load clears historical receipts");
        var setupFixed = new Carnival34FourLevel(); var fixedAudit = CarnivalRegistryAudit.FromInspection(setupFixed, native);
        Check(fixedAudit.RequireWarpPaths(setupFixed.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]), 0, 0, null)["dynamicMappings"]!.AsArray().Count == 0, "old plugin fixed-only authoring remains available");
        var data = new OutputData { NativeWarpCapabilities = caps };
        using var bytes = new MemoryStream(); using var transport = new TStreamTransport(bytes, bytes, new Thrift.TConfiguration()); var protocol = new TBinaryProtocol(transport);
        data.WriteAsync(protocol, default).GetAwaiter().GetResult(); bytes.Position = 0;
        var copied = new OutputData(); copied.ReadAsync(protocol, default).GetAwaiter().GetResult();
        Check(copied.__isset.nativeWarpCapabilities && copied.NativeWarpCapabilities.Version == 1 && copied.NativeWarpCapabilities.Features == 1, "optional native capability survives actual binary Thrift field12");
        var simulator = new RealGameSimulator { setup = f.setup }; foreach (var pair in f.live) simulator.entityIdToRecord[pair.Key] = pair.Value;
        simulator.ApplyGameUpdateWhenWarping(new EntityRetirementMessage { m_entityHeader = new() { m_uEntityID = 124 } }, new());
        Check(!simulator.entityIdToRecord.ContainsKey(124) && f.prepared.existed[20], "actual warp retirement remaps ID without falsifying target history");

        // Workstation stop messages omit the item header. The real Story 1-1
        // sequence is start(board31, chef45, raw51), prepared53 spawn, then
        // stop(board31, chef45). Retaining raw51 after the final stop creates a
        // dangling [30,0] reference in a later dynamic checkpoint.
        var workstationSetup = new Story11FourLevel();
        var workstationSimulator = new RealGameSimulator { setup = workstationSetup };
        workstationSimulator.Reset();
        var storyRecords = workstationSetup.entityRecords;
        var storyCrate = storyRecords.GetRecordFromPath(new[] { 30 });
        var storyRaw = new GameEntityRecord { path = new() { ids = new[] { 30, 0 } }, prefab = storyCrate.prefab.Spawns[0], spawner = storyCrate,
            existed = new(true), position = new(Vector3.Zero) };
        storyCrate.spawned.Add(storyRaw); workstationSimulator.entityIdToRecord[51] = storyRaw;
        void Interaction(InputEventMessage.InputEventType type, int target)
        {
            var message = new EntityEventMessage();
            message.Initialise(new() { m_uEntityID = 45 }, 0,
                new InputEventMessage(type) { entityId = (uint)target });
            workstationSimulator.ApplyGameUpdate(message);
        }
        var storyChef = workstationSimulator.entityIdToRecord[45];
        Interaction(InputEventMessage.InputEventType.BeginInteraction, 31);
        Check(ReferenceEquals(storyChef.data.Last().interactingWith, workstationSimulator.entityIdToRecord[31]),
            "native begin-interaction records the chef target");
        Interaction(InputEventMessage.InputEventType.EndInteraction, 0);
        Check(storyChef.data.Last().interactingWith is null,
            "native end-interaction clears the chef target instead of retaining stale controller history");
        void Work(bool active, int chef, int item)
        {
            var payload = new WorkstationMessage { m_interacting = active,
                m_interactorHeader = new() { m_uEntityID = (uint)chef }, m_itemHeader = new() { m_uEntityID = (uint)item } };
            var message = new EntityEventMessage();
            message.Initialise(new() { m_uEntityID = 31 }, 0, payload);
            workstationSimulator.ApplyGameUpdate(message);
        }
        var storyBoard = workstationSimulator.entityIdToRecord[31];
        Work(true, 45, 51); Work(true, 46, 51); Work(false, 45, 0);
        Check(ReferenceEquals(storyBoard.data.Last().itemBeingChopped, storyRaw) && storyBoard.data.Last().chopInteracters.Count == 1,
            "one stopping chef retains the item while another native workstation interacter remains");
        Work(false, 46, 0);
        Check(storyBoard.data.Last().itemBeingChopped is null && storyBoard.data.Last().chopInteracters.Count == 0,
            "last native workstation stop clears the item omitted from the stop message");
        var stoppedWarp = WarpCalculator.CalculateWarp(workstationSimulator.entityIdToRecord, storyRecords, 0, 0);
        Check(stoppedWarp.Entities.Single(e => e.__isset.entityId && e.EntityId == 31).Workstation.Item is null,
            "settled board warp cannot retain a consumed raw-item path");
        Work(true, 45, 51);
        var storyPrepared = new GameEntityRecord { path = new() { ids = new[] { 30, 0, 0 } }, prefab = storyRaw.prefab.Spawns[0], spawner = storyRaw,
            existed = new(true), position = new(Vector3.Zero) };
        storyRaw.spawned.Add(storyPrepared); workstationSimulator.entityIdToRecord[53] = storyPrepared;
        var replacement = new EntityEventMessage();
        replacement.Initialise(new() { m_uEntityID = 31 }, 0, new AttachStationMessage { m_item = 53 });
        workstationSimulator.ApplyGameUpdate(replacement);
        workstationSimulator.ApplyGameUpdate(new DestroyEntityMessage { m_Header = new() { m_uEntityID = 51 } });
        Check(ReferenceEquals(storyBoard.data.Last().attachment, storyPrepared) &&
              storyBoard.data.Last().itemBeingChopped is null && storyBoard.data.Last().chopInteracters.Count == 1,
            "captured completion replacement clears only the destroyed native workstation item while retaining its interacter");
        var completionWarp = WarpCalculator.CalculateWarp(workstationSimulator.entityIdToRecord, storyRecords, 0, 0);
        var completionBoard = completionWarp.Entities.Single(e => e.__isset.entityId && e.EntityId == 31);
        Check(completionBoard.Workstation.Item is null && completionBoard.AttachStation.Item.EntityId == 53,
            "exact completion-frame warp targets prepared attachment53 without a consumed raw51 workstation path");
        workstationSimulator.AdvanceAutomaticProgress();
        Check(storyBoard.data.Last().itemBeingChopped is null,
            "automatic estimator does not dereference the native null item on the exact completion frame");
        return checks;
    }
}
