using Google.Protobuf;
using Hpmv;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public static class ObservedTransferTests
{
    private static void Attach(GameEntityRecord item, GameEntityRecord owner, int frame)
    { var a = item.data[frame]; a.attachmentParent = owner; item.data.ChangeTo(a, frame); var b = owner.data[frame]; b.attachment = item; owner.data.ChangeTo(b, frame); }
    private static void Empty(GameEntityRecord owner, int frame)
    { var data = owner.data[frame]; data.attachment = null; owner.data.ChangeTo(data, frame); }
    private static GameActionInput Input(GameSetup setup, int frame = 11, int start = 10, bool down = false) => new()
    { Frame = frame, FrameWithinAction = frame - start, Entities = setup.entityRecords, Geometry = setup.geometry, MapByChef = setup.mapByChef, ControllerState = new() { primaryButtonDown = down } };

    public static int RunSynthetic(string root)
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException("Observed transfer: " + why); checks++; }
        (GameSetup s, Dictionary<int, GameEntityRecord> e, InteractAction a) Fixture(bool pickup = true)
        {
            var s = new Carnival34FourLevel(); var e = s.entityRecords.FixedEntities.ToDictionary(x => x.path.ids[0]);
            Empty(e[103], 10); Empty(e[32], 10); Attach(e[10], pickup ? e[38] : e[103], 10);
            var state = e[103].chefState[10]; state.highlightedForPickup = e[38]; state.highlightedForPlacement = e[32]; e[103].chefState.ChangeTo(state, 10);
            return (s, e, new() { Chef = e[103], Subject = new LiteralEntityReference(pickup ? e[10] : e[32]), IsPickup = pickup, RequireObservedTransfer = true, ActionId = 100 });
        }
        var f = Fixture(); var released = f.a.Step(Input(f.s, down: true));
        Check(!released.Done && released.ControllerInput?.primaryUp == true, "rejected native pickup emits release without completion");
        Check(!f.a.Step(Input(f.s)).Done, "press/up without attachment never completes");
        var old = new InteractAction { Chef = f.e[103], Subject = new LiteralEntityReference(f.e[10]), IsPickup = true };
        Check(old.Step(Input(f.s, down: true)).Done, "absent option retains original release-only semantics");
        var heldGate = Input(f.s, down: true); heldGate.ControllerState.buttonDownDurationLeft = TimeSpan.FromSeconds(1);
        Check(f.a.Step(heldGate).ControllerInput == null, "native framework minimum hold gate retained");
        var cooldown = Input(f.s); cooldown.ControllerState.pickupCooldown = TimeSpan.FromSeconds(1);
        Check(f.a.Step(cooldown).ControllerInput == null, "existing pickup cooldown retained");
        Check(f.a.Step(Input(f.s)).ControllerInput?.primaryDown == true, "legal rejected pickup may retry when original gate allows");
        Attach(f.e[10], f.e[103], 12);
        Check(!f.a.Step(Input(f.s, 12)).Done, "stale original source pair blocks apparent held proof");
        Empty(f.e[38], 13);
        Check(!f.a.Step(Input(f.s, 13, down: true)).Done, "exact pickup still waits for observed primary release");
        Check(f.a.Step(Input(f.s, 14)).Done, "exact chef/item pair plus original source release completes pickup");
        Check(f.a.ObserveTransfer(Input(f.s, 14)).Source == f.e[38] && f.a.ObserveTransfer(Input(f.s, 14)).Item == f.e[10], "binding remains original start history after source empties");
        f = Fixture(); Attach(f.e[11], f.e[103], 12); Empty(f.e[38], 12);
        Check(!f.a.Step(Input(f.s, 12)).Done && f.a.Step(Input(f.s, 12)).ControllerInput == null, "wrong carried item neither completes nor presses");
        f = Fixture(); Attach(f.e[10], f.e[104], 12); Empty(f.e[38], 12);
        Check(!f.a.Step(Input(f.s, 12)).Done, "different native chef cannot complete pickup");
        f = Fixture(); f.e[10].existed.ChangeTo(false, 12);
        Check(!f.a.Step(Input(f.s, 12)).Done, "retired exact item fails closed");
        f = Fixture(); f.e[38].existed.ChangeTo(false, 12);
        Check(f.a.Step(Input(f.s, 12)).ControllerInput == null, "retired original source cannot issue another press");
        f = Fixture(false);
        Check(f.a.Step(Input(f.s)).ControllerInput?.primaryDown == true, "place uses actual placement highlight even when pickup highlight differs");
        var cs = f.e[103].chefState[11]; cs.highlightedForPlacement = null; cs.highlightedForPickup = f.e[32]; f.e[103].chefState.ChangeTo(cs, 11);
        Check(f.a.Step(Input(f.s)).ControllerInput?.primaryDown != true, "pickup highlight alone cannot certify placement target");
        Check(!f.a.Step(Input(f.s, 12, down: true)).Done, "place up edge alone is not completion");
        Attach(f.e[10], f.e[32], 13);
        Check(!f.a.Step(Input(f.s, 13)).Done, "target attachment with uncleared chef is not accepted");
        Empty(f.e[103], 14);
        Check(!f.a.Step(Input(f.s, 14, down: true)).Done && f.a.Step(Input(f.s, 15)).Done, "exact target/item and empty chef plus release completes place");
        f = Fixture(false); Empty(f.e[103], 12); Attach(f.e[10], f.e[33], 12);
        Check(!f.a.Step(Input(f.s, 12)).Done, "placement on wrong counter is not accepted");
        f = Fixture(false); Empty(f.e[103], 12); f.e[10].existed.ChangeTo(false, 12);
        Check(!f.a.Step(Input(f.s, 12)).Done, "ingredient consumption is not misclassified as attachment placement");
        f = Fixture(false); Attach(f.e[11], f.e[32], 10);
        Check(f.a.Step(Input(f.s)).ControllerInput == null, "occupied initial target cannot be silently replaced");
        f = Fixture(); Empty(f.e[38], 10);
        Check(f.a.Step(Input(f.s)).ControllerInput == null, "inconsistent original source pair blocks new presses");

        // A claimed crate spawn must be a single new exact child, held by this chef.
        f = Fixture(); f.a.ExpectSpawn = true; f.a.Subject = new LiteralEntityReference(f.e[68]);
        var child = new GameEntityRecord { path = new() { ids = new[] { 68, 0 } }, prefab = f.e[68].prefab.Spawns[0], spawner = f.e[68], existed = new(false), position = new(default) };
        f.e[68].spawned.Add(child); child.existed.ChangeTo(true, 11);
        Check(f.a.Step(Input(f.s, 11)).ControllerInput == null, "observed unheld spawn blocks repeated crate presses");
        Attach(child, f.e[103], 12);
        Check(!f.a.Step(Input(f.s, 12, down: true)).Done, "spawned pickup also observes release");
        var accepted = f.a.Step(Input(f.s, 13));
        Check(accepted.Done && accepted.SpawningClaim == child, "only exact held new native child receives original graph claim");
        child.spawnOwner.ChangeTo(999, 13);
        Check(!f.a.Step(Input(f.s, 13)).Done, "another action's native spawn claim is retained");
        child.spawnOwner.ChangeTo(-1, 13); child.existed.initialValue = true;
        Check(!f.a.Step(Input(f.s, 13)).Done, "preexisting child cannot become this action's spawn");

        f = Fixture();
        var request = new JsonObject { ["maximumFrames"] = 100, ["actions"] = new JsonArray(
            new JsonObject { ["id"] = "take", ["chef"] = 103, ["type"] = "pickup", ["target"] = 10, ["timeoutFrames"] = 20 },
            new JsonObject { ["id"] = "put", ["chef"] = 103, ["type"] = "place", ["target"] = 32, ["timeoutFrames"] = 20 }) };
        var plan = TypedActionPlan.Create(f.s, f.e, 10, request); plan.Install(f.s);
        Check(plan.Nodes.All(n => ((InteractAction)n.Action).RequireObservedTransfer), "typed pickup and place enable persisted guard");
        f.s.sequences.NodeById[plan.Nodes[0].Id].Predictions.StartFrame = 10;
        var restored = Hpmv.Save.GameSetup.Parser.ParseFrom(f.s.ToProto().ToByteArray()).FromProto();
        var ra = (InteractAction)restored.sequences.NodeById[plan.Nodes[0].Id].Action;
        Check(ra.RequireObservedTransfer && restored.sequences.NodeById[plan.Nodes[0].Id].Predictions.StartFrame == 10, "checkpoint retains policy and binding start");
        Check(ra.ObserveTransfer(Input(restored)).Item.path.ids.SequenceEqual(new[] { 10 }) && ra.ObserveTransfer(Input(restored)).Source.path.ids.SequenceEqual(new[] { 38 }), "restored history retains exact original item/source references");
        Check(!ra.Step(Input(restored, down: true)).Done, "restored guard cannot downgrade to old completion");
        var simulator = new RealGameSimulator { setup = f.s }; simulator.SetFrameAfterWarping(11); simulator.ClearHistoryBeforeSimulation();
        f.s.entityRecords.Chefs[f.e[103]].ChangeTo(new() { primaryButtonDown = true }, 11); simulator.ComputeInputForNextFrame();
        Check(f.s.sequences.NodeById[plan.Nodes[0].Id].Predictions.EndFrame is null && f.s.sequences.NodeById[plan.Nodes[1].Id].Predictions.StartFrame is null, "release-only pickup does not unblock actual graph dependency");
        var session = new HeadlessSession(f.s, "synthetic timeout after failed transfer", root);
        var core = (RealGameConnector)typeof(HeadlessSession).GetField("core", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
        void Set(string name, object value) => typeof(HeadlessSession).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session, value);
        Set("actionPlan", plan); Set("actionOutcome", "running"); Set("actionStart", 10); Set("actionWall", Stopwatch.StartNew());
        f.s.sequences.NodeById[plan.Nodes[0].Id].Predictions.StartFrame = 10; core.simulator.SetFrameAfterWarping(30); core.State = RealGameState.Running;
        typeof(HeadlessSession).GetMethod("ObserveActionFrame", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(session, null);
        Check(session.Status()["typedActions"]!["outcome"]!.ToString() == "awaiting-pause" && session.Status()["typedActions"]!["error"]!.ToString().Contains("Action timeout: take"), "failed transfer retains original per-node deadline and requests bounded pause");
        return checks;
    }

    public static JsonObject RunCaptured(string checkpoint, string observations, string edges)
    {
        int checks = 0; void Check(bool ok, string why) { if (!ok) throw new InvalidOperationException("Native Q transfer: " + why); checks++; }
        byte[] bytes = File.ReadAllBytes(checkpoint);
        var s = Hpmv.Save.GameSetup.Parser.ParseFrom(bytes).FromProto(); var core = new RealGameConnector(s);
        var e = s.entityRecords.FixedEntities.ToDictionary(x => x.path.ids[0]);
        foreach (var p in e) core.simulator.entityIdToRecord[p.Key] = p.Value;
        var baseline = JsonNode.Parse(File.ReadAllBytes(observations))!.AsArray().Single(x => (string)x!["label"]! == "base")!["response"]!;
        foreach (var r in baseline["registry"]!.Deserialize<List<EntityRegistryData>>(RuntimeHost.Json)!) core.simulator.ApplyEntityRegistryUpdateEarly(r);
        var rows = JsonNode.Parse(File.ReadAllBytes(edges))!.AsArray();
        foreach (var row in rows.Where(x => (int)x!["frame"]! <= 43))
        {
            int frame = (int)row!["frame"]!; core.simulator.SetFrameAfterWarping(frame);
            foreach (var m in row["messages"]!.Deserialize<List<ServerMessage>>(RuntimeHost.Json)!) core.simulator.ApplyGameUpdate(Deserializer.Deserialize(m.Type, m.Message));
            core.simulator.ApplyChefUpdate(103, row["chef"]!.Deserialize<ChefSpecificData>(RuntimeHost.Json)!);
            var physics = new ItemData();
            if (row["position"] is JsonObject position) physics.Pos = position.Deserialize<Point>(RuntimeHost.Json);
            if (row["velocity"] is JsonObject velocity) physics.Velocity = velocity.Deserialize<Point>(RuntimeHost.Json);
            core.simulator.ApplyPositionUpdate(103, physics);
        }
        var down = rows.Single(x => (int)x!["frame"]! == 42)!["input"]!["Pickup"]!;
        Check((bool)down["Down"]! && (bool)down["JustPressed"]!, "actual42 issued native press");
        Check(e[103].data[43].attachment == null && e[38].data[43].attachment == e[10] && e[10].data[43].attachmentParent == e[38], "actual43 still has exact plate10 on38 and empty chef");
        var action = new InteractAction { Chef = e[103], Subject = new LiteralEntityReference(e[10]), IsPickup = true };
        var input = Input(s, 43, 31, true);
        Check(action.Step(input).Done, "old native failure predicate reproduced at43");
        action.RequireObservedTransfer = true; var result = action.Step(input);
        Check(!result.Done && result.ControllerInput?.primaryUp == true && result.SpawningClaim == null, "new predicate releases without falsely completing");
        var proof = action.ObserveTransfer(input);
        Check(proof.Item == e[10] && proof.Source == e[38] && proof.StartFrame == 31 && !proof.Accepted, "actual start boundary pins exact intended plate/source");
        Check(bytes.SequenceEqual(File.ReadAllBytes(checkpoint)), "captured checkpoint unchanged");
        return new() { ["ok"] = true, ["checks"] = checks, ["gameCalls"] = 0,
            ["checkpointSha256"] = Convert.ToHexStringLower(SHA256.HashData(bytes)),
            ["observationsSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(observations))),
            ["edgesSha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(edges))),
            ["qualification"] = "Actual native-Q failure replayed offline; positive native transfer/replay validation remains separate." };
    }
}
