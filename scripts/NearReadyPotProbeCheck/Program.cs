using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// File-only native mechanism proof. The executing controller is operator identified;
// the loaded food classifier is independently checked against its frozen manifest.
const string PotKey = "pot:dlc08_utensil_pot_01@16.80,-10.80";
const string HomeKey = "cook-station:workstation_cooker_01@16.80,-10.80";
const string OutputKey = "counter:dlc08_countertop_01_standard_circus@19.20,-10.80";
const string BoardKey = "chop:dlc08_countertop_01_chopping_circus@15.60,-14.40";
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
int I(JsonNode? n) => n?.GetValue<int>() ?? 0;
double N(JsonNode? n) => n is null ? double.NaN : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
bool B(JsonNode? n) => n?.GetValue<bool>() == true;
string HashBytes(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
string HashFile(string path) { using var f = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(f)); }
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
JsonObject Entity(JsonObject s, int id) => s["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == id);
JsonObject Chef(JsonObject s, int player) => s["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == player);
bool Bun(JsonObject e) { var f = CarnivalRecipes.ClassifyEntity(e); return !f.IsPlate && f.EvidenceGaps.Length == 0 && f.Food.Preparation == FoodPreparation.Chopped && f.Food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Bun.Id }); }
bool Cooked(JsonObject e) { var f = CarnivalRecipes.ClassifyEntity(e); return f.EvidenceGaps.Length == 0 && f.Food.Kind == FoodNodeKind.Cooked && f.Food.Preparation == FoodPreparation.Cooked && f.Food.CookingStepId == CarnivalRecipes.PotCookingStepId && f.Food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }); }
bool Meal(JsonObject e) => !CarnivalRecipes.ClassifyEntity(e).IsPlate && CarnivalRecipes.MatchRecipe(e, 296560, false).ReadyToDeliver;
bool Empty(JsonObject e) => CarnivalRecipes.ClassifyEntity(e).Food.IngredientIds.Length == 0 && N(e["cookingProgress"]) == 0;
bool Neutral(JsonObject p) => N(p["x"]) == 0 && N(p["y"]) == 0 && !B(p["pickup"]) && !B(p["use"]) && !B(p["dash"]);
JsonObject report;
try
{
    Require(args.Length == 4 || args.Length == 6 && args[4] == "--controller-bundle", "Usage: TRACE PLAN RESULT OUTPUT [--controller-bundle DIRECTORY]");
    string bundle = Path.GetFullPath(args.Length == 6 ? args[5] : "artifacts/planner-candidate-v15");
    string controller = Path.Combine(bundle, "OvercookedTAS.Controller.dll"), manifestPath = Path.Combine(bundle, "manifest.json");
    var plan = Read(args[1]); var result = Read(args[2]); var manifest = Read(manifestPath);
    Require(B(result["ok"]) && B(result["paused"]), "Result must be successful and paused.");
    string controllerHash = HashFile(controller), pluginHash = HashFile("lab/runtime/BepInEx/plugins/Oc2Tas.dll");
    Require(controllerHash == manifest["controllerSha256"]?.ToString() && HashFile(typeof(CarnivalRecipes).Assembly.Location) == controllerHash, "Frozen controller/loaded classifier hash mismatch.");
    var sources = manifest["sourceFiles"]!.AsArray().OfType<JsonObject>().ToArray();
    foreach (var source in sources) Require(HashFile(Path.Combine(bundle, "source", source["path"]!.ToString())) == source["sha256"]?.ToString(), "Frozen source hash mismatch.");
    string sourceTree = HashBytes(Encoding.UTF8.GetBytes(string.Concat(sources.OrderBy(s => s["path"]!.ToString(), StringComparer.Ordinal).Select(s => s["path"] + "\0" + s["sha256"]!.ToString().ToLowerInvariant() + "\n"))));
    Require(sourceTree == manifest["controllerSourceTreeSha256"]?.ToString(), "Frozen source tree mismatch.");
    var jobs = plan["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
    string[] expectedJobs = ["NP01-load-near-pot", "NP02-prepare-bun", "NP03-held-bun-native-wait-and-harvest"];
    Require(I(plan["timeoutFrames"]) == 1800 && jobs.Select(j => j["id"]!.ToString()).SequenceEqual(expectedJobs), "Probe job/bound differs.");
    Require(jobs.Select(j => I(j["player"])).SequenceEqual(new[] { 2, 2, 0 }) && jobs.Select(j => j["actions"]!.AsArray().Count).SequenceEqual(new[] { 3, 3, 5 }), "Probe roles/action counts differ.");
    Require(jobs[2]["actions"]!.AsArray().Select(a => a!["type"]!.ToString()).SequenceEqual(new[] { "take", "navigate", "cook", "combine", "place" }), "Direct mechanism action sequence differs.");
    Require(jobs.SelectMany(j => j["actions"]!.AsArray()).All(a => I(a!["timeoutFrames"]) > 0 && new[] { "take", "place", "chop", "cook", "throw", "combine", "navigate" }.Contains(a["type"]?.ToString())), "Unbounded/nonordinary action.");
    int pot = 0, home = 0, output = 0, board = 0, food = 0, foodOrdinal = -1, rawBun = 0;
    var ordinals = new Dictionary<int, int>(); var commands = new Dictionary<string, int>();
    var started = new HashSet<string>(); var done = new HashSet<string>(); var actions = new Dictionary<int, JsonObject>();
    int calls = 0, frames = 0, lastFrame = -1, firstFrame = -1, cookingFrame = -1, harvestFrame = -1, waitFirst = -1, waitLast = -1, waitSamples = 0;
    int clientServerChopSamples = 0, cookedHeldSamples = 0, actionCompletions = 0, tests = 0, combineTarget = 0;
    double chopMax = 0, heatMin = double.PositiveInfinity, heatMax = 0, waitFirstTime = 0, waitLastTime = 0, waitFirstProgress = 0, waitLastProgress = 0;
    JsonObject? last = null, waitFixture = null, cookedFixture = null, harvestFixture = null, waitPad = null;
    string? instrumentationHash = null; bool harvested = false, lastPickup = false, matchingCombineEdge = false;
    void StationProof(JsonObject s)
    {
        foreach (var pair in ordinals) Require(I(Entity(s, pair.Key)["observedOrdinal"]) == pair.Value && B(Entity(s, pair.Key)["active"]), "Exact native station identity/activity changed.");
        Require(I(Entity(s, home)["attachedEntityId"]) == pot && s["chefs"]!.AsArray().All(c => I(c!["heldEntityId"]) != pot), "Original pot left its stove or was held.");
        Require(N(Entity(s, pot)["cookingTime"]) == 12 && !CarnivalRecipes.ClassifyEntity(Entity(s, pot)).Food.IsRuined, "Native pot duration/ruin evidence changed.");
        if (food != 0) Require(I(Entity(s, food)["observedOrdinal"]) == foodOrdinal && B(Entity(s, food)["active"]), "Exact prepared bun identity was lost.");
    }
    void WaitProof(JsonObject s, JsonObject pad)
    {
        StationProof(s); Require(food != 0 && I(Chef(s, 0)["heldEntityId"]) == food && Bun(Entity(s, food)), "Native wait lacks the same held prepared bun.");
        Require(Neutral(pad), "Cook wait issued movement/button input.");
        var f = CarnivalRecipes.ClassifyEntity(Entity(s, pot));
        Require(f.EvidenceGaps.Length == 0 && f.Food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }) && f.Food.CookingStepId == CarnivalRecipes.PotCookingStepId && !f.Food.IsRuined, "Native wait pot composition differs.");
    }
    void ConsumptionProof(JsonObject before, JsonObject after, bool cookedPreviously, bool edge)
    {
        StationProof(after);
        Require(cookedPreviously && Cooked(Entity(before, pot)) && Bun(Entity(before, food)) && I(Chef(before, 0)["heldEntityId"]) == food, "Consumption lacks prior native Cooked plus exact held bun.");
        Require(edge && Empty(Entity(after, pot)) && I(Chef(after, 0)["heldEntityId"]) == food && Meal(Entity(after, food)), "Native target/button/empty-pot/same-food consumption proof failed.");
    }
    bool TargetsPot(JsonObject s) => I(Chef(s, 0)["placementTargetId"]) == pot ||
        I(Chef(s, 0)["placementTargetId"]) == home && I(Entity(s, home)["attachedEntityId"]) == pot;
    using var file = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
    long bytes = file.Length; Require(bytes > 0 && bytes <= 1_000_000_000, "Trace size bound exceeded."); string traceHash = Convert.ToHexStringLower(SHA256.HashData(file)); file.Position = 0;
    using var gzip = new GZipStream(file, CompressionMode.Decompress, true); using var reader = new StreamReader(gzip);
    string? line; int lines = 0;
    while ((line = reader.ReadLine()) is not null)
    {
        Require(++lines < 12000 && line.Length < 8_000_000, "Trace line bound exceeded.");
        using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
        string? kind = row.GetProperty("kind").GetString();
        if (kind == "event")
        {
            string name = row.GetProperty("name").GetString()!; var value = row.GetProperty("value");
            Require(name is not ("planFailure" or "actionFailure" or "plannerFailure"), "Trace contains failure.");
            if (name is "jobStart" or "jobComplete")
            {
                string id = value.GetProperty("id").GetString()!; Require(expectedJobs.Contains(id), "Unexpected job.");
                if (name == "jobStart") Require(started.Add(id), "Duplicate job start."); else Require(started.Contains(id) && done.Add(id), "Unmatched job completion.");
            }
            if (name == "actionCreated") { var action = JsonNode.Parse(value.GetRawText())!.AsObject(); Require(actions.TryAdd(I(action["player"]), action), "Overlapping same-chef action."); }
            if (name == "actionComplete")
            {
                var action = JsonNode.Parse(value.GetProperty("action").GetRawText())!.AsObject(); int player = I(action["player"]);
                Require(actions.TryGetValue(player, out var active) && JsonNode.DeepEquals(active, action), "Action completion differs from its creation.");
                if (action["type"]?.ToString() == "cook") Require(last is not null && Cooked(Entity(last, pot)) && I(Chef(last, 0)["heldEntityId"]) == food, "Cook action completed before native Cooked/held-bun evidence.");
                actions.Remove(player); actionCompletions++;
            }
            continue;
        }
        if (kind != "call") continue;
        Require(++calls <= 2000, "Protocol call bound exceeded."); var request = row.GetProperty("request"); string command = request.GetProperty("command").GetString()!;
        Require(command is "inspect" or "step" or "restart", "Non-input mutation command: " + command); if (command == "restart") Require(calls == 1, "Mid-probe restart.");
        commands[command] = commands.GetValueOrDefault(command) + 1;
        JsonObject? pad0 = null;
        if (command == "step")
        {
            Require(request.GetProperty("steps").GetInt32() == 1 && request.EnumerateObject().All(p => new[] { "version", "command", "steps", "inputs" }.Contains(p.Name)), "Nonordinary/nonunit step.");
            var pads = request.GetProperty("inputs").EnumerateArray().ToArray(); Require(pads.Length == 4 && pads.Select(p => p.GetProperty("player").GetInt32()).Order().SequenceEqual(new[] { 0, 1, 2, 3 }), "Four unique inputs absent.");
            foreach (var p in pads)
            {
                Require(p.EnumerateObject().All(k => new[] { "player", "x", "y", "pickup", "use", "dash" }.Contains(k.Name)), "Unexpected input field.");
                foreach (string axis in new[] { "x", "y" }) Require(double.IsFinite(p.GetProperty(axis).GetDouble()) && Math.Abs(p.GetProperty(axis).GetDouble()) <= 1, "Invalid axis.");
                foreach (string button in new[] { "pickup", "use", "dash" }) Require(p.GetProperty(button).ValueKind is JsonValueKind.True or JsonValueKind.False, "Invalid button.");
                if (p.GetProperty("player").GetInt32() == 0) pad0 = JsonNode.Parse(p.GetRawText())!.AsObject();
            }
        }
        var response = row.GetProperty("response"); Require(response.GetProperty("ok").GetBoolean(), "Native protocol failure.");
        if (!response.TryGetProperty("state", out var native) || native.ValueKind != JsonValueKind.Object) continue;
        int frame = native.GetProperty("gameplayFrame").GetInt32();
        Require(native.GetProperty("scene").GetString() == "s_Day_3_4" && new[] { "score", "baseScore", "tips", "delivered", "deductions" }.All(k => native.GetProperty(k).GetInt32() == 0), "Unexpected scene/score/delivery/deduction.");
        Require(native.GetProperty("gameEventsInstalled").GetBoolean() && native.GetProperty("gameEventsDropped").GetInt32() == 0 && native.GetProperty("gameEvents").GetArrayLength() == 0, "Native game-event proof absent or nonempty.");
        var instrumentation = native.GetProperty("instrumentation"); string hash = instrumentation.GetProperty("manifestSha256").GetString()!;
        Require(instrumentation.GetProperty("manifest").GetProperty("pluginSha256").GetString() == pluginHash && instrumentation.GetProperty("nativePhysicsAutoSimulation").GetBoolean() && instrumentation.GetProperty("inputActive").GetBoolean() && native.GetProperty("fixedDeltaTime").GetDouble() == .02 && native.GetProperty("captureFramerate").GetInt32() == 60 && !native.GetProperty("timerSuppressed").GetBoolean(), "Native plugin/input/clock/physics configuration differs.");
        instrumentationHash ??= hash; Require(hash == instrumentationHash, "Instrumentation changed.");
        var es = native.GetProperty("entities").EnumerateArray().ToArray();
        if (pot == 0)
        {
            var sv = response.GetProperty("session"); var session = JsonNode.Parse(sv.ValueKind == JsonValueKind.String ? sv.GetString()! : sv.GetRawText())!;
            Require(I(session["dlc"]) == 8 && I(session["variantPlayers"]) == 4 && I(session["serverUsers"]) == 4 && I(session["clientUsers"]) == 4, "Native four-player session absent.");
            var initial = JsonNode.Parse(native.GetRawText())!.AsObject(); var model = KitchenModel.Build(initial);
            pot = model.Resolve(PotKey).EntityId; home = model.Resolve(HomeKey).EntityId; board = model.Resolve(BoardKey).EntityId; output = model.Resolve(OutputKey).EntityId;
            foreach (int id in new[] { pot, home, board, output }) ordinals.Add(id, I(Entity(initial, id)["observedOrdinal"]));
            Require(Empty(Entity(initial, pot)) && I(Entity(initial, board)["attachedEntityId"]) == 0 && I(Entity(initial, output)["attachedEntityId"]) == 0, "Initial preparation state differs."); firstFrame = frame;
        }
        if (food == 0)
        {
            int candidate = es.Single(e => e.GetProperty("id").GetInt32() == board).GetProperty("attachedEntityId").GetInt32();
            if (candidate != 0)
            {
                var e = JsonNode.Parse(es.Single(e => e.GetProperty("id").GetInt32() == candidate).GetRawText())!.AsObject();
                if (Bun(e)) { food = candidate; foodOrdinal = I(e["observedOrdinal"]); Require(rawBun != 0 && rawBun != food && clientServerChopSamples > 1 && chopMax > .5, "Prepared bun lacks native raw replacement and chopping proof."); }
                else { rawBun = candidate; chopMax = Math.Max(chopMax, N(e["workProgress"])); }
            }
        }
        var s = new JsonObject { ["entities"] = new JsonArray(es.Where(e => new[] { pot, home, board, output, food }.Contains(e.GetProperty("id").GetInt32())).Select(e => JsonNode.Parse(e.GetRawText())).ToArray()),
            ["chefs"] = JsonNode.Parse(native.GetProperty("chefs").GetRawText()), ["gameplayFrame"] = frame, ["clientTime"] = native.GetProperty("clientTime").GetDouble(), ["timer"] = native.GetProperty("timer").GetDouble() };
        Require(lastFrame < 0 || frame == lastFrame + (command == "step" ? 1 : 0), "Native frame alignment gap.");
        if (last is not null && command == "step") Require(N(s["clientTime"]) > N(last["clientTime"]) && N(s["timer"]) < N(last["timer"]), "Native client/timer did not advance.");
        Require(s["chefs"]!.AsArray().Select(c => I(c!["playerId"])).Order().SequenceEqual(new[] { 0, 1, 2, 3 }), "Native chef set differs."); StationProof(s);
        if (I(Chef(s, 2)["clientPredictedInteractionId"]) == board && I(Chef(s, 2)["serverInteractionId"]) == board && I(Chef(s, 2)["heldEntityId"]) == 0) clientServerChopSamples++;
        var vessel = Entity(s, pot); if (!Empty(vessel)) { heatMin = Math.Min(heatMin, N(vessel["cookingProgress"])); heatMax = Math.Max(heatMax, N(vessel["cookingProgress"])); }
        if (command == "step" && actions.GetValueOrDefault(0)?["type"]?.ToString() == "cook")
        {
            WaitProof(s, pad0!); if (waitFirst < 0) { waitFirst = frame; waitFirstTime = N(s["clientTime"]); waitFirstProgress = N(vessel["cookingProgress"]); waitFixture = s.DeepClone().AsObject(); waitPad = pad0!.DeepClone().AsObject(); }
            else Require(frame == waitLast + 1, "Cook wait frame gap.");
            waitLast = frame; waitLastTime = N(s["clientTime"]); waitLastProgress = N(vessel["cookingProgress"]); waitSamples++;
        }
        if (food != 0 && Cooked(vessel) && I(Chef(s, 0)["heldEntityId"]) == food && Bun(Entity(s, food))) { if (cookingFrame < 0) cookingFrame = frame; cookedHeldSamples++; cookedFixture = s.DeepClone().AsObject(); }
        bool pickup = pad0 is not null && B(pad0["pickup"]);
        bool edge = pickup && !lastPickup && last is not null && TargetsPot(last);
        if (edge && actions.GetValueOrDefault(0)?["type"]?.ToString() == "combine") combineTarget = I(Chef(last!, 0)["placementTargetId"]);
        matchingCombineEdge |= edge && actions.GetValueOrDefault(0)?["type"]?.ToString() == "combine";
        if (!harvested && food != 0 && Meal(Entity(s, food)))
        {
            Require(last is not null && actions.GetValueOrDefault(0)?["type"]?.ToString() == "combine", "Native food changed outside combine action.");
            ConsumptionProof(last!, s, cookingFrame >= 0 && cookingFrame < frame, matchingCombineEdge); harvested = true; harvestFrame = frame; harvestFixture = s.DeepClone().AsObject();
        }
        if (harvested) Require(Empty(vessel) && Meal(Entity(s, food)), "Post-harvest empty pot/meal changed.");
        if (frame == I(result["state"]!["gameplayFrame"])) Require(JsonNode.DeepEquals(JsonNode.Parse(native.GetRawText()), result["state"]), "Final result differs from trace.");
        last = s; lastFrame = frame; lastPickup = pickup; frames++;
    }
    Require(file.Length == bytes && done.Count == 3 && actionCompletions == 11 && actions.Count == 0 && lastFrame == I(result["state"]!["gameplayFrame"]), "Trace closure/job/action completion incomplete.");
    Require(waitSamples > 2 && waitLastTime - waitFirstTime > 0 && waitLastProgress > waitFirstProgress && cookedHeldSamples > 0 && harvested && heatMax - heatMin >= 11.9, "Native heating/wait/consumption transition incomplete.");
    Require(last is not null && I(Entity(last, output)["attachedEntityId"]) == food && I(Entity(last, board)["attachedEntityId"]) == 0 && Empty(Entity(last, pot)) && Meal(Entity(last, food)) && last["chefs"]!.AsArray().All(c => I(c!["heldEntityId"]) == 0 && B(c["controlsEnabled"])), "Final meal/output/pot/chef proof incomplete.");
    void Reject(Action test, string name) { bool rejected = false; try { test(); } catch (InvalidOperationException) { rejected = true; } Require(rejected, "Checker mutation accepted: " + name); tests++; }
    Require(waitFixture is not null && cookedFixture is not null && harvestFixture is not null && waitPad is not null, "Missing native test fixtures.");
    WaitProof(waitFixture!, waitPad!); ConsumptionProof(cookedFixture!, harvestFixture!, true, true); tests += 2;
    void Mutation(Action<JsonObject> change, string name) { var s = waitFixture!.DeepClone().AsObject(); change(s); Reject(() => WaitProof(s, waitPad!), name); }
    Mutation(s => Entity(s, home)["attachedEntityId"] = 0, "pot removed from stove");
    Mutation(s => Chef(s, 1)["heldEntityId"] = pot, "another chef holds pot");
    Mutation(s => Entity(s, pot)["observedOrdinal"] = -8, "pot identity replaced");
    Mutation(s => Entity(s, food)["observedOrdinal"] = -8, "prepared food identity replaced");
    Mutation(s => Entity(s, food)["active"] = false, "prepared food inactive");
    Mutation(s => Chef(s, 0)["heldEntityId"] = 0, "bun absent during wait");
    Mutation(s => Entity(s, pot)["cookingTime"] = 1, "native cooking duration changed");
    var badPad = waitPad!.DeepClone().AsObject(); badPad["use"] = true; Reject(() => WaitProof(waitFixture!, badPad), "button input during wait");
    Reject(() => ConsumptionProof(cookedFixture!, harvestFixture!, false, true), "missing prior Cooked observation");
    Reject(() => ConsumptionProof(cookedFixture!, harvestFixture!, true, false), "missing native target/button edge");
    var foreignTarget = cookedFixture!.DeepClone().AsObject(); Chef(foreignTarget, 0)["placementTargetId"] = output; Require(!TargetsPot(foreignTarget), "Unrelated native target accepted."); tests++;
    var badMeal = harvestFixture!.DeepClone().AsObject(); Entity(badMeal, food)["composition"] = Entity(waitFixture!, food)["composition"]!.DeepClone(); Reject(() => ConsumptionProof(cookedFixture!, badMeal, true, true), "sausage not transferred");
    report = new() { ["ok"] = true, ["classification"] = "Native held-bun cook-wait and direct pot harvest mechanism; adaptive V16 admission and performance are not established by this V15 primitive probe.",
        ["tracePath"] = Path.GetFullPath(args[0]), ["traceBytes"] = bytes, ["traceSha256"] = traceHash, ["planSha256"] = HashFile(args[1]), ["resultSha256"] = HashFile(args[2]),
        ["checkerSourceSha256"] = HashFile("scripts/NearReadyPotProbeCheck/Program.cs"), ["checkerProjectSha256"] = HashFile("scripts/NearReadyPotProbeCheck/NearReadyPotProbeCheck.csproj"),
        ["controllerBundle"] = bundle, ["controllerSha256"] = controllerHash, ["classifierSha256"] = HashFile(typeof(CarnivalRecipes).Assembly.Location), ["controllerManifestSha256"] = HashFile(manifestPath), ["controllerSourceTreeSha256"] = sourceTree, ["frozenSourceFilesVerified"] = sources.Length,
        ["controllerProvenance"] = "Operator identified the frozen V15 controller as executing candidate. The trace header itself identifies only controller version; loaded checker classifier hash is independently verified against the frozen assembly.",
        ["pluginFileAndTelemetrySha256"] = pluginHash, ["instrumentationManifestSha256"] = instrumentationHash, ["commands"] = JsonSerializer.SerializeToNode(commands), ["calls"] = calls, ["stateSamples"] = frames, ["firstFrame"] = firstFrame, ["finalFrame"] = lastFrame,
        ["potId"] = pot, ["homeId"] = home, ["boardId"] = board, ["outputId"] = output, ["rawBunId"] = rawBun, ["preparedBunAndMealId"] = food, ["foodObservedOrdinal"] = foodOrdinal,
        ["nativeChoppingClientServerSamples"] = clientServerChopSamples, ["maximumRawWorkProgress"] = chopMax, ["potRemainedOnOriginalStoveEverySample"] = true, ["potHeldSamples"] = 0,
        ["neutralHeldBunWaitSamples"] = waitSamples, ["waitFirstFrame"] = waitFirst, ["waitLastFrame"] = waitLast, ["nativeWaitElapsedSeconds"] = waitLastTime - waitFirstTime,
        ["waitFirstNativeProgress"] = waitFirstProgress, ["waitLastNativeProgress"] = waitLastProgress, ["firstNativeCookedWhileHeldFrame"] = cookingFrame, ["nativeCookedHeldSamples"] = cookedHeldSamples,
        ["nativeConsumptionFrame"] = harvestFrame, ["observedTargetAndPickupEdge"] = matchingCombineEdge, ["nativeCombineTargetId"] = combineTarget, ["nativeTargetQualification"] = "Native placement target is the original stove, whose observed attachment is the exact original pot.", ["finalRecipeId"] = 296560, ["sameFoodReturnedToCounter"] = true,
        ["originalPotEmptyAndReset"] = true, ["completedJobs"] = done.Count, ["completedActions"] = actionCompletions, ["nativeFixtureCheckerAssertions"] = tests,
        ["noScoreDeliveryDeduction"] = true, ["ordinaryInputRequestsOnly"] = true, ["clockQualification"] = "Monotonic native ClientTime/timer, unchanged observed clock/physics manifest and twelve-second pot setting; no non-input command or clock correction request during the trace. Existing instrumented clock policy remains explicit in the manifest." };
}
catch (Exception e) { report = new() { ["ok"] = false, ["error"] = e.Message, ["detail"] = e.ToString() }; Environment.ExitCode = 1; }
string text = report.ToJsonString(new() { WriteIndented = true }); if (args.Length >= 4) File.WriteAllText(args[3], text + Environment.NewLine); Console.WriteLine(text);
