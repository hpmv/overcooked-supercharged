using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

return await Probe.RunMode(args);

static class Probe
{
    const string FrozenHash = "0afc09032339fb9a69c006bd303bc3bf45862c433afb3bed07fc62253d086404";
    const string ColdPath = "routes/probes/washer-plating-cold-setup.json", MainPath = "routes/probes/washer-plating-main.json";
    const string PlateKey = "plate:equipment_plate_01@19.20,-15.60", PotKey = "pot:dlc08_utensil_pot_01@16.80,-10.80";
    const string HomeKey = "cook-station:workstation_cooker_01@16.80,-10.80", BunKey = "chop:dlc08_countertop_01_chopping_circus@15.60,-14.40";
    const string RawKey = "chop:dlc08_countertop_01_chopping_circus@15.60,-12.00", CleanKey = "counter:dlc08_countertop_01_standard_circus@15.60,-20.40";
    const string SharedKey = "counter:dlc08_countertop_01_standard_circus@15.60,-18.00", OutputKey = "counter:dlc08_countertop_01_standard_circus@25.20,-20.40";
    static readonly string[] Keys = [PlateKey, PotKey, HomeKey, BunKey, RawKey, CleanKey, SharedKey, OutputKey];
    internal static int I(JsonNode? n) => n is null ? 0 : int.Parse(n.ToString());
    internal static double N(JsonNode? n) => n is null ? double.NaN : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
    static JsonObject Read(string p) => JsonNode.Parse(File.ReadAllText(p))!.AsObject();
    static string Hash(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
    static void Need(bool b, string reason) { if (!b) throw new InvalidDataException(reason); }
    static JsonObject S(JsonObject r) => r["state"] as JsonObject ?? r;
    static JsonObject E(JsonObject s, int id) => s["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == id);
    static JsonObject C(JsonObject s, int p) => s["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == p);
    static bool Ready(JsonObject e, bool plate) => CarnivalRecipes.MatchRecipe(e, 296560, plate).ReadyToDeliver;
    static bool Empty(JsonObject e) => CarnivalRecipes.ClassifyEntity(e).Food.IngredientIds.Length == 0;
    static void Save(string p, JsonObject value) => File.WriteAllText(p, value.ToJsonString(Json.Pretty));

    public static async Task<int> RunMode(string[] args)
    {
        try
        {
            Need(Hash(typeof(CarnivalPlanner).Assembly.Location) == FrozenHash, "Probe must use the pinned frozen V14 controller.");
            Need(args.Length >= 1, "Modes: preflight SNAPSHOT OUT | analyze TRACE RESULT OUT | run PORT OUTPREFIX");
            if (args[0] == "preflight") { Save(args[2], Preflight(Read(args[1]))); Console.WriteLine("Washer plating static preflight passed."); return 0; }
            if (args[0] == "selftest") { Console.WriteLine("Washer plating observer checks: " + Observer.SelfTest(Read(args[1]), Read(args[2]))); return 0; }
            if (args[0] == "analyze") { Save(args[3], Analyze(args[1], args[2])); Console.WriteLine("Washer plating native evidence passed."); return 0; }
            Need(args[0] == "run" && args.Length == 3, "Unknown mode or incomplete explicit run arguments.");
            await Run(int.Parse(args[1]), args[2]); return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    static JsonObject Preflight(JsonObject response)
    {
        var state = S(response); var model = KitchenModel.Build(response);
        Need(model.Scene == "s_Day_3_4" && state["chefs"]!.AsArray().Count == 4, "Native Carnival four-chef snapshot required.");
        var selected = Keys.ToDictionary(k => k, k => model.Resolve(k));
        Need(selected[SharedKey].Regions.Contains("lower-left") && selected[SharedKey].Regions.Contains("center"), "Plating counter lacks measured access from both chefs' platforms.");
        Need(new[] { CleanKey, SharedKey, OutputKey, BunKey, RawKey }.All(k => I(E(state, selected[k].EntityId)["attachedEntityId"]) == 0), "Fresh staging surfaces must be empty.");
        Need(Empty(E(state, selected[PlateKey].EntityId)), "The selected original plate must be clean.");
        var identities = new JsonObject(selected.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)new JsonObject
        { ["id"] = p.Value.EntityId, ["ordinal"] = E(state, p.Value.EntityId)["observedOrdinal"]!.DeepClone() })));
        var paths = new JsonArray(); int actions = 0, jobs = 0;
        var positions = state["chefs"]!.AsArray().OfType<JsonObject>().ToDictionary(c => I(c["playerId"]), c => KitchenModel.Position(c["position"]));
        foreach (string path in new[] { ColdPath, MainPath })
        {
            var list = Read(path)["jobs"]!.AsArray().OfType<JsonObject>().ToArray(); jobs += list.Length;
            var done = new HashSet<string>();
            foreach (var job in list)
            {
                Need(job["dependencies"]!.AsArray().All(n => done.Contains(n!.ToString())), "Authored jobs must be in dependency order.");
                int player = I(job["player"]);
                foreach (var a in job["actions"]!.AsArray().OfType<JsonObject>())
                {
                    actions++; Need(I(a["timeoutFrames"]) is > 0 and <= 900, "Every action needs a bounded timeout.");
                    string type = a["type"]!.ToString();
                    if (type is "aim-cannon" or "board-cannon") continue; // Existing native transport action; no synthetic board pose proof.
                    if (type == "fire-cannon")
                    {
                        var cannon = state["entities"]!.AsArray().OfType<JsonObject>().Single(e => e["components"]!.AsArray().Any(c => c?.ToString() == "Cannon") && N(e["position"]?["x"]) > 30);
                        double yaw = N(cannon["cannonMinAngle"]) * Math.PI / 180;
                        var p = KitchenModel.Position(cannon["position"]); double radius = p.Distance(KitchenModel.Position(cannon["cannonTarget"]));
                        positions[1] = new Point2(p.X + radius * Math.Sin(yaw), p.Z + radius * Math.Cos(yaw));
                        Need(model.RegionAt(positions[1]) == "lower-left", "Native right-cannon minimum angle must target LL.");
                        positions[3] = model.Stations.Single(s => s.EntityId == I(cannon["cannonButtonEntityId"])).Approaches.First(a => a.Region == "center").Position;
                        continue;
                    }
                    var route = type == "navigate" ? Navigation.FindPath(model, positions[player], KitchenModel.Position(a["target"])) :
                        Navigation.ToStation(model, positions[player], model.Resolve(a["station"]!.ToString()));
                    Need(route.Success, "Static path failed: " + job["id"] + " " + a + " " + route.Error);
                    paths.Add(new JsonObject { ["job"] = job["id"]!.DeepClone(), ["player"] = player, ["type"] = type, ["length"] = route.Length });
                    positions[player] = route.Points[^1];
                }
                done.Add(job["id"]!.ToString());
            }
        }
        return new() { ["ok"] = true, ["qualification"] = "Static scene paths and authored dependencies only; native plating route untested.",
            ["controllerSha256"] = FrozenHash, ["coldPlanSha256"] = Hash(ColdPath), ["mainPlanSha256"] = Hash(MainPath),
            ["jobs"] = jobs, ["actions"] = actions, ["initialIdentities"] = identities, ["paths"] = paths };
    }

    sealed class Observer
    {
        readonly Dictionary<int, int> ordinals;
        readonly int plate, pot, home, bunBoard, rawBoard, clean, shared, output;
        readonly int[] vessels;
        int bun, raw, lastFrame = -1, samples;
        bool main, staged, washerClean, washerMeal, stagedPlate, centerRecovered, loaded;
        double maxPot;
        readonly JsonObject milestones = new();
        public Observer(JsonObject response)
        {
            var state = S(response); var model = KitchenModel.Build(response);
            Need(I(state["gameplayFrame"]) == 0 && N(state["timer"]) > 269.9 && N(state["timer"]) <= 270, "Exact fresh native frame0 required.");
            var session = JsonNode.Parse(response["session"]!.ToString())!;
            Need(I(session["dlc"]) == 8 && I(session["variantPlayers"]) == 4 && I(session["serverUsers"]) == 4 && I(session["clientUsers"]) == 4, "Native DLC8/four-player session required.");
            int Id(string k) => model.Resolve(k).EntityId;
            plate = Id(PlateKey); pot = Id(PotKey); home = Id(HomeKey); bunBoard = Id(BunKey); rawBoard = Id(RawKey);
            clean = Id(CleanKey); shared = Id(SharedKey); output = Id(OutputKey);
            vessels = model.Stations.Where(s => s.Role is "pot" or "pan" or "bowl" or "basket").Select(s => s.EntityId).ToArray();
            ordinals = vessels.Concat(new[] { plate, home, bunBoard, rawBoard, clean, shared, output }).Distinct().ToDictionary(i => i, i => I(E(state, i)["observedOrdinal"]));
            Observe(response);
        }
        public void BeginMain(JsonObject response)
        {
            var s = S(response); Need(!main && vessels.All(i => Empty(E(s, i))), "Cold setup must leave every processing vessel empty.");
            Need(KitchenModel.Build(s).RegionAt(KitchenModel.Position(C(s, 1)["position"])) == "lower-left" && C(s, 1)["controlsEnabled"]?.GetValue<bool>() == true, "Washer must have completed native LL arrival.");
            Need(s["chefs"]!.AsArray().OfType<JsonObject>().All(c => I(c["heldEntityId"]) == 0), "Cold setup must finish with four empty hands.");
            Need(I(E(s, clean)["attachedEntityId"]) == plate && Empty(E(s, plate)) && I(E(s, shared)["attachedEntityId"]) == 0 && I(E(s, output)["attachedEntityId"]) == 0, "Exact clean plate or free plating/output surface missing.");
            bun = I(E(s, bunBoard)["attachedEntityId"]); raw = I(E(s, rawBoard)["attachedEntityId"]);
            var b = CarnivalRecipes.ClassifyEntity(E(s, bun)).Food; var r = CarnivalRecipes.ClassifyEntity(E(s, raw)).Food;
            Need(b.Preparation == FoodPreparation.Chopped && b.IngredientIds.SequenceEqual(new[] { 262914 }) && r.IngredientIds.SequenceEqual(new[] { 284626 }), "Cold setup lacks exact chopped bun and raw sausage.");
            ordinals.Add(bun, I(E(s, bun)["observedOrdinal"])); main = true; milestones["coldReady"] = I(s["gameplayFrame"]);
        }
        public void Observe(JsonObject response)
        {
            var s = S(response); int frame = I(s["gameplayFrame"]);
            Need(lastFrame < 0 || frame == lastFrame || frame == lastFrame + 1, "Probe trace skipped a native gameplay frame.");
            if (frame != lastFrame) samples++; lastFrame = frame;
            Need(s["scene"]?.ToString() == "s_Day_3_4" && I(s["score"]) == 0 && I(s["delivered"]) == 0 && I(s["deductions"]) == 0, "Probe must stay in its unscored native session.");
            foreach (var pair in ordinals.Where(p => p.Key != bun)) Need(I(E(s, pair.Key)["observedOrdinal"]) == pair.Value, "Original entity incarnation changed.");
            Need(I(E(s, home)["attachedEntityId"]) == pot, "Original pot must remain at its native stove.");
            foreach (int id in vessels)
            {
                var food = CarnivalRecipes.ClassifyEntity(E(s, id)).Food;
                Need(!food.IsRuined, "Native food became ruined.");
                if (!main || id != pot) Need(food.IngredientIds.Length == 0, "Unexpected heat/mixing work outside the single planned pot.");
                else if (food.IngredientIds.Length != 0)
                {
                    Need(food.IngredientIds.SequenceEqual(new[] { 284626 }), "Pot composition differs from the exact supplied sausage.");
                    double progress = N(E(s, pot)["cookingProgress"]); maxPot = Math.Max(maxPot, progress); loaded = true;
                    Need(progress < 22, "Single-pot probe reached the 22-second native heat abort boundary.");
                }
            }
            if (!main) return;
            var foodEntity = s["entities"]!.AsArray().OfType<JsonObject>().SingleOrDefault(e => I(e["id"]) == bun && I(e["observedOrdinal"]) == ordinals[bun] && e["active"]?.GetValue<bool>() == true);
            void Mark(string name) { if (milestones[name] is null) milestones[name] = frame; }
            if (foodEntity is not null && I(C(s, 0)["heldEntityId"]) == bun && Ready(foodEntity, false)) Mark("centerHeldUnplated");
            if (foodEntity is not null && I(E(s, shared)["attachedEntityId"]) == bun && Ready(foodEntity, false)) { staged = true; Mark("unplatedOnShared"); }
            if (staged && I(C(s, 1)["heldEntityId"]) == plate && Empty(E(s, plate))) { washerClean = true; Mark("washerHeldOriginalCleanPlate"); }
            if (washerClean && I(C(s, 1)["heldEntityId"]) == plate && Ready(E(s, plate), true)) { washerMeal = true; Mark("washerHeldPreparedPlate"); }
            if (washerMeal && I(E(s, shared)["attachedEntityId"]) == plate && I(C(s, 1)["heldEntityId"]) == 0) { stagedPlate = true; Mark("washerStagedPreparedPlate"); }
            if (stagedPlate && I(C(s, 0)["heldEntityId"]) == plate && Ready(E(s, plate), true)) { centerRecovered = true; Mark("centerRecoveredSamePreparedPlate"); }
        }
        public JsonObject Finish(JsonObject response)
        {
            Observe(response); var s = S(response);
            Need(main && loaded && maxPot >= 12 && staged && washerClean && washerMeal && stagedPlate && centerRecovered, "Native heating/plating/identity transition proof is incomplete.");
            Need(Empty(E(s, pot)) && N(E(s, pot)["cookingProgress"]) == 0 && I(E(s, shared)["attachedEntityId"]) == 0 && I(E(s, clean)["attachedEntityId"]) == 0, "Native pot reset or source handoff consumption missing.");
            Need(I(E(s, output)["attachedEntityId"]) == plate && Ready(E(s, plate), true) && s["chefs"]!.AsArray().OfType<JsonObject>().All(c => I(c["heldEntityId"]) == 0), "Same ready native plate was not recovered to the final output with empty hands.");
            Need(!s["entities"]!.AsArray().OfType<JsonObject>().Any(e => I(e["id"]) == bun && I(e["observedOrdinal"]) == ordinals[bun] && e["active"]?.GetValue<bool>() == true), "Native unplated food was not consumed by plate assembly.");
            return new() { ["ok"] = true, ["qualification"] = "One native washer-side plating mechanism; no washing, delivery, score, or reproducibility qualification.",
                ["controllerSha256"] = FrozenHash, ["plate"] = plate, ["plateOrdinal"] = ordinals[plate], ["unplatedFood"] = bun,
                ["foodOrdinal"] = ordinals[bun], ["rawSausage"] = raw, ["pot"] = pot, ["sharedCounter"] = shared, ["outputCounter"] = output,
                ["maximumNativePotProgress"] = maxPot, ["nativeFrameSamples"] = samples, ["finalGameplayFrame"] = lastFrame, ["milestones"] = milestones.DeepClone() };
        }
        public static int SelfTest(JsonObject initial, JsonObject nativeHeated)
        {
            int checks = 0;
            void Reject(Action action, string why)
            {
                bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; }
                Need(rejected, "Observer regression failed: " + why); checks++;
            }
            Observer Fresh() => new(initial.DeepClone().AsObject());
            var valid = Fresh(); valid.Observe(initial); checks++;
            var jump = initial.DeepClone().AsObject(); S(jump)["gameplayFrame"] = 2;
            Reject(() => Fresh().Observe(jump), "skipped gameplay frame");
            var plateReuse = initial.DeepClone().AsObject(); E(S(plateReuse), 10)["observedOrdinal"] = 123456;
            Reject(() => Fresh().Observe(plateReuse), "reused plate identity");
            var homeChange = initial.DeepClone().AsObject(); E(S(homeChange), 19)["attachedEntityId"] = 0;
            Reject(() => Fresh().Observe(homeChange), "empty original stove");
            JsonObject Heated(double progress)
            {
                var r = initial.DeepClone().AsObject(); var e = E(S(r), 7);
                e["composition"] = E(S(nativeHeated), 7)["composition"]!.DeepClone(); e["cookingProgress"] = progress;
                return r;
            }
            Reject(() => Fresh().Observe(Heated(1)), "cold setup unexpectedly started cooking");
            var mainObserver = Fresh(); mainObserver.main = true; mainObserver.Observe(Heated(21.9)); checks++;
            var hot = Fresh(); hot.main = true;
            Reject(() => hot.Observe(Heated(22)), "22-second native heat abort boundary");
            var extra = Heated(1); E(S(extra), 2)["composition"] = E(S(extra), 7)["composition"]!.DeepClone();
            var otherPot = Fresh(); otherPot.main = true;
            Reject(() => otherPot.Observe(extra), "another pot cannot gain unplanned cooking work");
            Reject(() => Fresh().BeginMain(initial), "unprepared cold setup cannot enter plating experiment");
            Reject(() => Fresh().Finish(initial), "missing native mechanism sequence cannot pass");
            return checks;
        }
    }

    static async Task Run(int port, string prefix)
    {
        string tracePath = prefix + ".jsonl.gz", resultPath = prefix + "-result.json";
        Need(!File.Exists(tracePath) && !File.Exists(resultPath), "Refusing to overwrite an existing probe artifact.");
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        await using var client = new TasClient(); await client.ConnectAsync("127.0.0.1", port, cancellation.Token);
        using var trace = new TraceWriter(tracePath, "washer-side-plating-probe");
        trace.Event("washerProbeManifest", new JsonObject { ["controllerSha256"] = FrozenHash, ["coldPlanSha256"] = Hash(ColdPath), ["mainPlanSha256"] = Hash(MainPath), ["port"] = port });
        Observer? observer = null; JsonObject? last = null;
        async Task<JsonObject> Call(JsonObject request)
        {
            if (last is not null) observer?.Observe(last);
            var response = await client.CallAsync(request, cancellation.Token); trace.Call(request, response); Json.RequireOk(response);
            last = response; observer?.Observe(response); return response;
        }
        try
        {
            var restart = Json.Request("restart"); restart["seed"] = 0; restart["isolateRecipeRandom"] = true;
            last = await Call(restart); Preflight(last); observer = new Observer(last);
            last = await new ConcurrentPlanRunner(Call, trace).RunAsync(Read(ColdPath)); observer.BeginMain(last);
            trace.Event("washerProbeColdReady", new JsonObject { ["gameplayFrame"] = S(last)["gameplayFrame"]!.DeepClone() });
            last = await new ConcurrentPlanRunner(Call, trace).RunAsync(Read(MainPath));
            var proof = observer.Finish(last); trace.Event("washerProbeComplete", proof); Save(resultPath, last);
            Console.WriteLine(proof.ToJsonString(Json.Pretty));
        }
        catch (Exception error)
        {
            trace.Event("washerProbeFailure", new JsonObject { ["error"] = error.Message, ["gameplayFrame"] = last?["state"]?["gameplayFrame"]?.DeepClone() });
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { var neutral = Inputs.Step(1); var r = await client.CallAsync(neutral, cleanup.Token); trace.Call(neutral, r); last = r; }
            catch (Exception cleanupError) { trace.Event("washerProbeCleanupError", JsonValue.Create(cleanupError.Message)); }
            if (last is not null) Save(resultPath, last);
            throw;
        }
    }

    static JsonObject Analyze(string tracePath, string resultPath)
    {
        using var file = File.OpenRead(tracePath); using var gzip = new GZipStream(file, CompressionMode.Decompress); using var reader = new StreamReader(gzip);
        Observer? observer = null; JsonObject? last = null; bool manifest = false, completed = false;
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue; var row = JsonNode.Parse(line)!.AsObject();
            if (row["kind"]?.ToString() == "call")
            {
                last = row["response"]!.AsObject(); Json.RequireOk(last);
                var request = row["request"]!.AsObject(); string command = request["command"]!.ToString();
                if (observer is null)
                {
                    Need(command == "restart" && I(request["seed"]) == 0 && request["isolateRecipeRandom"]?.GetValue<bool>() == true,
                        "Native trace must begin with its own seeded fresh restart."); observer = new Observer(last);
                }
                else
                {
                    Need(command == "inspect" || command == "step" && I(request["steps"]) == 1 && request["inputs"] is JsonArray a && a.Count == 4 &&
                        a.OfType<JsonObject>().Select(p => I(p["player"])).Order().SequenceEqual(new[] { 0, 1, 2, 3 }),
                        "Probe permits only observations and complete four-player single-frame ordinary inputs.");
                    observer.Observe(last);
                }
            }
            else if (row["kind"]?.ToString() == "event")
            {
                string name = row["name"]!.ToString();
                Need(name is not ("washerProbeFailure" or "planFailure"), "Recorded native probe failed.");
                if (name == "washerProbeManifest")
                {
                    var v = row["value"]!; Need(v["controllerSha256"]?.ToString() == FrozenHash && v["coldPlanSha256"]?.ToString() == Hash(ColdPath) && v["mainPlanSha256"]?.ToString() == Hash(MainPath), "Recorded source manifest differs from the checked plans/controller."); manifest = true;
                }
                if (name == "washerProbeColdReady") { Need(observer is not null && last is not null, "Cold marker lacks native snapshot."); observer!.BeginMain(last!); }
                if (name == "washerProbeComplete") completed = true;
            }
        }
        Need(manifest && completed && observer is not null && last is not null, "Closed successful native trace with manifest required.");
        var result = Read(resultPath); Need(JsonNode.DeepEquals(last, result), "Result file differs from the final recorded native response.");
        var proof = observer!.Finish(result); proof["traceSha256"] = Hash(tracePath); proof["resultSha256"] = Hash(resultPath);
        proof["coldPlanSha256"] = Hash(ColdPath); proof["mainPlanSha256"] = Hash(MainPath); proof["checkerSourceSha256"] = Hash("scripts/WasherPlatingProbe/Program.cs");
        return proof;
    }
}
