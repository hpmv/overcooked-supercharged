using Hpmv;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public static class PhysicsContainerTests
{
    public static JsonObject Run(string checkpoint, string fixture)
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new InvalidOperationException("Physics container: " + label); checks++; }
        byte[] bytes = File.ReadAllBytes(checkpoint), observations = File.ReadAllBytes(fixture);
        var rows = JsonNode.Parse(observations)!.AsArray();
        var baseline = rows.Single(r => (string)r!["label"] == "base")!["response"]!;
        var future = rows.Single(r => (string)r!["label"] == "original")!["response"]!;
        var registry = baseline["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!;
        var setup = Hpmv.Save.GameSetup.Parser.ParseFrom(bytes).FromProto();
        var core = new RealGameConnector(setup);
        foreach (var record in setup.entityRecords.FixedEntities) core.simulator.entityIdToRecord[record.path.ids[0]] = record;
        foreach (var metadata in registry) { core.simulator.ApplyEntityRegistryUpdateEarly(metadata); core.ObservePhysicsContainer(metadata); }
        int frame = (int)baseline["frame"]!;
        var target = core.simulator.entityIdToRecord[121];
        JsonNode Entity(JsonNode state, int id) => state["entities"]!.AsArray().Single(e => (int)e!["id"]! == id)!;
        Check(frame == 371 && (int)future["frame"]! == 382, "actual O pickup endpoints retained");
        Check(!JsonNode.DeepEquals(Entity(baseline, 121)["position"], Entity(future, 121)["position"]), "actual original pickup moved physical container121");
        var old = WarpCalculator.CalculateWarp(core.simulator.entityIdToRecord, setup.entityRecords, 382, frame);
        Check(old.Entities.All(e => !e.__isset.entityId || e.EntityId != 121), "previous calculator omitted body-only121");
        Check(core.ObservedPhysicsContainers.Contains(target), "actual registry validates fixed body121");
        Check(!core.ObservedPhysicsContainers.Contains(core.simulator.entityIdToRecord[46]), "ordinary station is not inferred to be a physics container");
        var warp = WarpCalculator.CalculateWarp(core.simulator.entityIdToRecord, setup.entityRecords, 382, frame, core.ObservedPhysicsContainers);
        var body = warp.Entities.Single(e => e.__isset.entityId && e.EntityId == 121);
        Check(body.__isset.position && body.Position.FromThrift() == target.position[frame], "exact target position emitted");
        Check(body.__isset.rotation && body.Rotation.FromThrift() == target.rotation[frame], "exact target raw rotation emitted");
        Check(body.__isset.velocity && body.Velocity.FromThrift() == target.velocity[frame], "exact target resume velocity emitted");
        Check(body.__isset.angularVelocity && body.AngularVelocity.FromThrift() == target.angularVelocity[frame], "exact target angular velocity emitted");
        var originalMetadata = registry.Single(e => e.EntityId == 121);
        foreach (string component in new[] { "Rigidbody", "ObjectContainer" })
        {
            var changed = originalMetadata.DeepCopy(); changed.Components.Remove(component); core.ObservePhysicsContainer(changed);
            Check(!core.ObservedPhysicsContainers.Contains(target), "missing " + component + " excludes body");
            core.ObservePhysicsContainer(originalMetadata);
        }
        var noPhysics = originalMetadata.DeepCopy(); noPhysics.SyncEntityTypes.Clear(); core.ObservePhysicsContainer(noPhysics);
        Check(!core.ObservedPhysicsContainers.Contains(target), "missing native PhysicsObject type excludes body");
        core.ObservePhysicsContainer(originalMetadata);
        int count = core.ObservedPhysicsContainers.Count;
        var unknown = originalMetadata.DeepCopy(); unknown.EntityId = 99999; core.ObservePhysicsContainer(unknown);
        Check(core.ObservedPhysicsContainers.Count == count, "unknown fixed registration cannot create a record");
        Check(bytes.SequenceEqual(File.ReadAllBytes(checkpoint)) && observations.SequenceEqual(File.ReadAllBytes(fixture)), "captured checkpoint and native evidence unchanged");
        return new() { ["ok"] = true, ["checks"] = checks, ["gameCalls"] = 0,
            ["checkpointSha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["fixtureSha256"] = Convert.ToHexStringLower(SHA256.HashData(observations)),
            ["observedFixedContainers"] = core.ObservedPhysicsContainers.Count,
            ["qualification"] = "Actual native-O pickup checkpoint/registry regression for exact fixed-body warp spec. Native plugin-P restoration parity remains a separate probe." };
    }
}
