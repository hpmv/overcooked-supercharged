using System.Buffers.Binary;
using System.Text;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class SelfTests
{
    private static int assertions;
    public static async Task Run()
    {
        var recipeFailures=CarnivalRecipes.SelfTest();
        Assert(recipeFailures.Length==0,"native recipe model: "+string.Join("; ",recipeFailures));
        Assert(RouteRunner.ThrowSelfTest()>0,"native throw action state transitions");
        using (var stream = new MemoryStream())
        {
            var payload = Encoding.UTF8.GetBytes("{\"emoji\":\"🍽️\",\"version\":1}");
            await TasClient.WriteFrameAsync(stream, payload, default);
            stream.Position = 0;
            Assert((await TasClient.ReadFrameAsync(stream, default)).SequenceEqual(payload), "UTF8 framed roundtrip");
        }
        using (var stream = new MemoryStream([255, 255, 255, 255]))
            await Throws<InvalidDataException>(() => TasClient.ReadFrameAsync(stream, default), "negative length rejected");
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, TasClient.MaxMessageBytes + 1);
        using (var stream = new MemoryStream(header))
            await Throws<InvalidDataException>(() => TasClient.ReadFrameAsync(stream, default), "oversized frame rejected");
        using (var stream = new MemoryStream([5, 0, 0, 0, 1, 2]))
            await Throws<EndOfStreamException>(() => TasClient.ReadFrameAsync(stream, default), "truncated payload rejected");
        using (var stream = new FragmentedStream([3, 0, 0, 0, 5, 6, 7]))
            Assert((await TasClient.ReadFrameAsync(stream, default)).SequenceEqual(new byte[] { 5, 6, 7 }), "fragmented network frame read");

        Assert(StateComparer.First(Json.Object("{\"b\":2,\"a\":1}"), Json.Object("{\"a\":1,\"b\":2}")) is null, "object order ignored");
        Assert(StateComparer.First(Json.Object("{\"x\":[1,2]}"), Json.Object("{\"x\":[1,3]}"))?.Path == "$.x.1", "first differing array scalar");
        Assert(StateComparer.First(Json.Object("{\"x\":1,\"score\":10}"), Json.Object("{\"x\":2,\"score\":10}"), new HashSet<string> { "$.x" }) is null, "explicit ignored path");
        Assert(StateComparer.First(Json.Object("{\"x\":null}"), Json.Object("{}")) is not null, "missing differs from null");
        var neutral = Inputs.AllNeutral();
        Assert(neutral.Count == 4 && neutral.OfType<JsonObject>().All(c => c["pickup"]!.GetValue<bool>() == false), "all four inputs released");
        neutral[0]!["use"] = true;
        Assert(!Inputs.AllNeutral()[0]!["use"]!.GetValue<bool>(), "fresh inputs do not alias");
        var directStart=Program.DirectRequest(Json.Request("load"),new Options(["--seed","0","--isolate-recipe-random"]));
        Assert(directStart["seed"]!.GetValue<int>()==0&&directStart["isolateRecipeRandom"]!.GetValue<bool>(),"direct load forwards explicit native seed and isolation flag");
        directStart=Program.DirectRequest(Json.Request("restart"),new Options(["--seed","-10","--isolate-recipe-random","false"]));
        Assert(directStart["seed"]!.GetValue<int>()==-10&&!directStart["isolateRecipeRandom"]!.GetValue<bool>(),"direct restart supports explicit false isolation and negative native seed");
        var suppliedStart=Json.Object("""{"version":1,"command":"load","seed":42,"isolateRecipeRandom":true}""");
        Assert(JsonNode.DeepEquals(Program.DirectRequest(suppliedStart,new Options([])),suppliedStart),"JSON request alternative preserves explicit startup settings");
        var overridden=Program.DirectRequest(suppliedStart,new Options(["--seed","7","--isolate-recipe-random","false"]));
        Assert(overridden["seed"]!.GetValue<int>()==7&&!overridden["isolateRecipeRandom"]!.GetValue<bool>()&&suppliedStart["seed"]!.GetValue<int>()==42,"explicit CLI overrides affect only emitted request");
        try{Program.DirectRequest(Json.Request("load"),new Options(["--isolate-recipe-random","maybe"]));throw new Exception("Invalid isolation flag accepted");}catch(ArgumentException){assertions++;}

        var reservations = new ReservationTable();
        reservations.Add(["sink"], 5, 10, "wash");
        Assert(reservations.Earliest(["sink"], 0, 5) == 0, "touching reservation permitted");
        Assert(reservations.Earliest(["sink"], 0, 6) == 15, "conflict moves start");
        Assert(reservations.Earliest(["board"], 0, 20) == 0, "independent resource");
        var schedule = KitchenScheduler.Schedule([
            new("a", [], ["board"], [0, 1], 10),
            new("b", [], ["board"], [0, 1], 7),
            new("c", ["a", "b"], ["plate"], [2, 3], 2)
        ]);
        var a = schedule.Single(t => t.Id == "a");
        var b = schedule.Single(t => t.Id == "b");
        var c = schedule.Single(t => t.Id == "c");
        Assert(a.End <= b.Start || b.End <= a.Start, "shared station serialized");
        Assert(c.Start >= Math.Max(a.End, b.End), "dependency completion respected");
        try { KitchenScheduler.Schedule([new("a", ["b"], [], [0], 1), new("b", ["a"], [], [1], 1)]); throw new Exception("Cycle accepted."); }
        catch (ArgumentException) { assertions++; }

        var calls = new List<JsonObject>();
        async Task<JsonObject> FakeCall(JsonObject request)
        {
            await Task.CompletedTask;
            calls.Add(request.DeepClone().AsObject());
            return Json.Object("{\"version\":1,\"ok\":true,\"frame\":0,\"state\":{\"score\":0,\"chefs\":[{\"playerId\":0,\"position\":{\"x\":0,\"y\":0,\"z\":0}}]}}");
        }
        await new RouteRunner(FakeCall, null).RunAsync(Json.Object("""
        {"phases":[{"name":"pulse","actions":[{"player":0,"type":"pulse","button":"pickup"}]},{"name":"hold","actions":[{"player":1,"type":"hold","button":"use","durationFrames":3}]}]}
        """));
        var steps = calls.Where(q => q["command"]?.ToString() == "step").ToArray();
        Assert(steps.Count(q => q["inputs"]![0]!["pickup"]!.GetValue<bool>()) == 1, "pickup pulse exactly one frame");
        Assert(steps.Count(q => q["inputs"]![1]!["use"]!.GetValue<bool>()) == 3, "hold lasts requested frames");
        Assert(!steps[^1]["inputs"]![1]!["use"]!.GetValue<bool>(), "route ends released");
        var tracePath = Path.Combine(Path.GetTempPath(), "overcooked-tas-selftest-" + Guid.NewGuid().ToString("N") + ".jsonl.gz");
        try
        {
            using (var trace = new TraceWriter(tracePath, "selftest"))
            {
                trace.Event("test", JsonValue.Create(1));
                trace.Call(Inputs.Step(1), Json.Object("{\"version\":1,\"ok\":true,\"state\":{\"score\":10}}"));
            }
            var entries = Traces.ReadCalls(tracePath).ToArray();
            Assert(entries.Length == 1 && entries[0]["response"]!["state"]!["score"]!.GetValue<int>() == 10, "gzip trace roundtrip and metadata filtered");
        }
        finally { File.Delete(tracePath); }
        TestNativeMovementLatency();
        TestNativeBlockedBraking();
        NativeDashTests.Run();
        NativeShortDashTests.Run();
        NavigationObbTests.Run();
        NavigationContactTests.Run();
        SeedOptimizerTests.Run();
        await VerificationTests.RunAsync();
        await VerificationProofTests.RunAsync();
        Console.WriteLine($"PASS: {assertions} controller self-test assertions.");
    }

    private static void TestNativeMovementLatency()
    {
        foreach(int phase in Enumerable.Range(0,6))foreach(var goal in new[]{new Point2(.121,0),new Point2(.181,0),new Point2(4.9208641475,0),new Point2(-1.4762971315,4.6941933)})
        {
            var state=Json.Object("""
            {"scene":"latency-fixture","fixedDeltaTime":0.02,"unityDeltaTime":0.016666667,"chefs":[{"playerId":0,"position":{"x":0,"y":0,"z":0},"lastVelocity":{"x":0,"y":0,"z":0}}]}
            """);
            var runner=new RouteRunner(_=>throw new Exception("Tick performed a network call."),null);
            var action=runner.CreateAction(new JsonObject{["player"]=0,["type"]="navigate",["target"]=new JsonObject{["x"]=goal.X,["z"]=goal.Z},["timeoutFrames"]=150});
            var position=new Point2();var cached=new Point2();bool onlyUnitSticks=true;int frame=0;
            for(;frame<160&&!action.IsDone;frame++)
            {
                state["framesSinceNoPhysics"]=(frame+phase+5)%6;
                var input=runner.Tick(action,state);
                // Native FixedUpdate consumes the prior Update's cached vector;
                // five physics ticks occur during each six logical frames.
                if((frame+phase)%6!=0)position=new(position.X+cached.X*.02,position.Z+cached.Z*.02);
                var stick=new Point2(KitchenModel.N(input["x"]),-KitchenModel.N(input["y"]));double magnitude=stick.Distance(default);
                onlyUnitSticks&=magnitude<1e-8||Math.Abs(magnitude-1)<1e-8;
                cached=magnitude<1e-8?default:new(stick.X/magnitude*6,stick.Z/magnitude*6);
                state["chefs"]![0]!["position"]!["x"]=position.X;state["chefs"]![0]!["position"]!["z"]=position.Z;
                state["chefs"]![0]!["lastVelocity"]!["x"]=cached.X;state["chefs"]![0]!["lastVelocity"]!["z"]=cached.Z;
            }
            Assert(action.IsDone&&action.Error is null&&position.Distance(goal)<=.100001&&cached.Distance(default)==0&&onlyUnitSticks,$"normalized native movement with latency converges, phase {phase}, target {goal}");
        }
    }

    private static void TestNativeBlockedBraking()
    {
        var state=Json.Object("""
        {"scene":"collision-fixture","fixedDeltaTime":0.02,"unityDeltaTime":0.016666667,"framesSinceNoPhysics":0,"chefs":[{"playerId":0,"position":{"x":0,"z":0},"lastVelocity":{"x":0,"z":0}}]}
        """);
        var runner=new RouteRunner(_=>throw new Exception("Unexpected network call"),null);
        var action=runner.CreateAction(Json.Object("""{"player":0,"type":"navigate","target":{"x":0.161975648,"z":0},"timeoutFrames":100}"""));
        for(int frame=0;frame<100&&!action.IsDone;frame++)
        {
            state["framesSinceNoPhysics"]=frame%6;
            var input=runner.Tick(action,state);
            // Collision consumes the pending physics displacement without
            // moving the chef; cached intent is still updated normally.
            state["chefs"]![0]!["lastVelocity"]!["x"]=KitchenModel.N(input["x"])*6;
        }
        Assert(action.Error?.Contains("Native collision",StringComparison.Ordinal)==true&&action.ElapsedFrames<60,"unmodeled native collision cannot cycle braking until route timeout");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
        assertions++;
    }
    private static async Task Throws<T>(Func<Task<byte[]>> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { assertions++; return; }
        throw new Exception("FAIL: " + message);
    }
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
