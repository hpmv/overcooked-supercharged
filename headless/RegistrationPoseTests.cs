using Hpmv;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public static class RegistrationPoseTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Registration pose fixture: " + name); checks++; }
        using var stream = typeof(RegistrationPoseTests).Assembly.GetManifestResourceStream("Headless.Reference.NativeGInitialWorldFixture.json")!;
        var fixture = JsonNode.Parse(stream)!.AsObject(); var setup = new Carnival34FourLevel();
        var audit = CarnivalRegistryAudit.FromInspection(setup, fixture);
        var first = audit.InitialRegistrations().ToJsonString(); var before = audit.Report();
        Check(before["layoutValid"]!.GetValue<bool>() && before["maximumInitialPositionDelta"]!.GetValue<double>() < .00002, "all actual G registration poses match original immutable setup");
        var entities = setup.entityRecords.FixedEntities.ToDictionary(e => e.path.ids[0]);
        foreach (var node in fixture["entities"]!.AsArray())
        {
            var entity = entities[node!["id"]!.GetValue<int>()]; var pos = node["position"]!;
            entity.position.ChangeTo(new Vector3(pos["x"]!.GetValue<float>(), pos["y"]!.GetValue<float>(), pos["z"]!.GetValue<float>()), 0);
            var data = entity.data[0];
            var parentPath = node["data"]?["attachmentParent"]?["path"]?.Deserialize<int[]>();
            var childPath = node["data"]?["attachment"]?["path"]?.Deserialize<int[]>();
            data.attachmentParent = parentPath is null ? null : setup.entityRecords.GetRecordFromPath(parentPath);
            data.attachment = childPath is null ? null : setup.entityRecords.GetRecordFromPath(childPath);
            entity.data.ChangeTo(data, 0);
        }
        var after = audit.Report();
        Check(after["layoutValid"]!.GetValue<bool>(), "actual native settled frame-zero poses no longer invalidate earlier registrations");
        Check(after["maximumInitialPositionDelta"]!.GetValue<double>() == before["maximumInitialPositionDelta"]!.GetValue<double>(), "registration comparison is immutable under native reconstruction");
        Check(first == audit.InitialRegistrations().ToJsonString(), "first raw registrations remain byte-identical");
        Check(Math.Abs(entities[2].position[0].Y - .603719) < .00001 && ReferenceEquals(entities[2].data[0].attachmentParent, entities[17]) && ReferenceEquals(entities[17].data[0].attachment, entities[2]), "actual pot pose and bidirectional home link retained");
        Check(Math.Abs(entities[5].position[0].Y - .269) < .00001 && ReferenceEquals(entities[5].data[0].attachmentParent, entities[15]), "actual native fryer basket pose and home retained");
        Check(Math.Abs(entities[10].position[0].Y - .5) < .00001 && ReferenceEquals(entities[10].data[0].attachmentParent, entities[38]), "actual plate settling retained");
        var changedIds = after["startupPoseDifferences"]!.AsArray().Select(n => n!["id"]!.GetValue<int>()).Order().ToArray();
        var expectedChanged = Enumerable.Range(2, 12).Concat(Enumerable.Range(87, 4)).Concat(Enumerable.Range(110, 13).Where(id => id != 119));
        Check(changedIds.SequenceEqual(expectedChanged), "captured movable/content and body-proxy settling preserves every stationary kitchen pose; actual IDs=" + string.Join(',', changedIds));
        audit.Reset(); audit.Observe(fixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!);
        Check(audit.Report()["layoutValid"]!.GetValue<bool>(), "restart retains immutable reference despite prior native history");
        var changed = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Single(e => e.EntityId == 14); changed.Pos.X += .01;
        audit.Reset(); audit.Observe(fixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Where(e => e.EntityId != 14)); audit.Observe(new[] { changed });
        Check(!audit.Report()["layoutValid"]!.GetValue<bool>(), "changed static station registration still rejected with original tolerance");
        changed = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Single(e => e.EntityId == 2); changed.Pos.X += .01;
        audit.Reset(); audit.Observe(fixture["initialRegistry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Where(e => e.EntityId != 2)); audit.Observe(new[] { changed });
        Check(!audit.Report()["layoutValid"]!.GetValue<bool>(), "changed movable first-registration pose is also rejected, not ignored");
        var wrongRole = fixture["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!.Single(e => e.EntityId == 2); wrongRole.Components.Remove("PhysicalAttachment"); audit.Observe(new[] { wrongRole });
        Check(audit.Report()["errors"]!.AsArray().Any(e => e!["reason"]!.ToString().Contains("component", StringComparison.OrdinalIgnoreCase)), "component identity guard remains active");
        return checks;
    }
}
