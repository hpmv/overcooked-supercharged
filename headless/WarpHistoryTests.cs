using Hpmv;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;

namespace Supercharged.Headless;

public static class WarpHistoryTests
{
    public static JsonObject Run(string checkpoint, string fixture)
    {
        int checks = 0;
        void Check(bool ok, string label) { if (!ok) throw new InvalidOperationException("Warp history: " + label); checks++; }
        var captured = JsonNode.Parse(File.ReadAllBytes(fixture))!["frames"]!.AsArray();
        var requestOutput = captured[0]!["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!;
        var acknowledgement = captured[1]!["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!;
        byte[] original = File.ReadAllBytes(checkpoint);
        (RealGameConnector core, GameSetup setup) Prepare()
        {
            var setup = Hpmv.Save.GameSetup.Parser.ParseFrom(original).FromProto();
            var core = new RealGameConnector(setup); core.FramerateController.Framerate = int.MaxValue;
            foreach (var entity in setup.entityRecords.FixedEntities) core.simulator.entityIdToRecord[entity.path.ids[0]] = entity;
            using var metadata = typeof(WarpHistoryTests).Assembly.GetManifestResourceStream("Headless.Reference.NativeGInitialWorldFixture.json")!;
            foreach (var row in JsonNode.Parse(metadata)!["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!) core.simulator.ApplyEntityRegistryUpdateEarly(row);
            core.simulator.SetFrameAfterWarping(301); setup.LastEmpiricalFrame = 301; core.State = RealGameState.Paused;
            foreach (var message in requestOutput.ServerMessages) core.simulator.ApplyGameUpdate(Deserializer.Deserialize(message.Type, message.Message));
            return (core, setup);
        }
        var (core, setup) = Prepare();
        var cannon = core.simulator.entityIdToRecord[84];
        Check(cannon.data.changes.Any(c => c.time == 301), "actual native-K cannon update creates future history");
        core.RequestWarp(181);
        var requested = core.getNext(requestOutput.DeepCopy()).GetAwaiter().GetResult();
        Check(requested.Warp?.Frame == 181 && core.State == RealGameState.Warping, "original framework requests target181");
        var response = core.getNext(acknowledgement.DeepCopy()).GetAwaiter().GetResult();
        Check(core.State == RealGameState.Paused && !core.RequestPending && core.simulator.Frame == 181 && response.NextFrame == 181, "verified actual ack moves controller and response to181");
        Check(setup.LastEmpiricalFrame == 181 && cannon.data.changes.All(c => c.time <= 181), "future authoring history is truncated immediately on success");
        foreach (int id in new[] { 84, 85 })
        {
            var observed = acknowledgement.ServerMessages.Select(m => Deserializer.Deserialize(m.Type, m.Message)).OfType<EntityAuxMessage>()
                .Last(m => m.m_entityHeader.m_uEntityID == id && m.m_payload is NativeCannonAuxMessage);
            Check(core.simulator.entityIdToRecord[id].data[181].rawNativeCannonAux.SequenceEqual(observed.m_payload.ToBytes()), "acknowledged actual auxiliary snapshot retained " + id);
        }
        Check(acknowledgement.Items.Where(p => p.Value.__isset.pos && core.simulator.entityIdToRecord.ContainsKey(p.Key))
            .All(p => core.simulator.entityIdToRecord[p.Key].position[181] == p.Value.Pos.FromThrift()), "all actual acknowledged positions applied after cleanup");
        Check(acknowledgement.Items.Where(p => p.Value.__isset.rotation && core.simulator.entityIdToRecord.ContainsKey(p.Key))
            .All(p => core.simulator.entityIdToRecord[p.Key].rotation[181] == p.Value.Rotation.FromThrift()), "all actual acknowledged raw rotations applied");
        core.getNext(acknowledgement.DeepCopy()).GetAwaiter().GetResult();
        Check(core.State == RealGameState.Paused && core.simulator.Frame == 181, "following paused native messages apply without backwards writes");
        Check(original.SequenceEqual(File.ReadAllBytes(checkpoint)), "original checkpoint file unchanged");
        foreach (bool sameTarget in new[] { false, true })
        {
            var (failed, retained) = Prepare(); failed.RequestWarp(sameTarget ? 301 : 181);
            failed.getNext(requestOutput.DeepCopy()).GetAwaiter().GetResult();
            var error = requestOutput.DeepCopy(); error.InvalidStateReason = "AUTHORING_WARP_FAILED: injected native preflight rejection";
            failed.getNext(error).GetAwaiter().GetResult();
            Check(failed.State == RealGameState.Error && !failed.RequestPending && retained.LastEmpiricalFrame == 301 &&
                retained.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 84).data.changes.Any(c => c.time == 301), "failed acknowledgement preserves future history; sameTarget=" + sameTarget);
        }
        return new() { ["ok"] = true, ["checks"] = checks, ["gameCalls"] = 0, ["checkpointSha256"] = Convert.ToHexStringLower(SHA256.HashData(original)),
            ["fixtureSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(fixture))), ["qualification"] = "Actual native-K request/acknowledgement replayed offline from its captured181checkpoint plus reconstructed301cannon updates; no native calls." };
    }
}
