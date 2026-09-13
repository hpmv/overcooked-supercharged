using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// Offline only: file streams and the frozen native-food classifier. No client.
const string DwellJob = "O04-observe-offheat-13-seconds";
const string PanKey = "pan:dlc08_utensil_frying_pan@18.00,-21.60";
const string HomeKey = "cook-station:workstation_cooker_01@18.00,-21.60";
const string ParkKey = "counter:dlc08_countertop_01_standard_circus@19.20,-21.60";
const string OutputKey = "counter:dlc08_countertop_01_standard_circus@21.60,-21.60";
const string BoardKey = "chop:dlc08_countertop_01_chopping_circus@15.60,-14.40";
void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
int I(JsonNode? n) => n?.GetValue<int>() ?? 0;
double N(JsonNode? n) => n is null ? double.NaN : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
string HashBytes(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
string HashNode(JsonNode? n) => HashBytes(Encoding.UTF8.GetBytes(n?.ToJsonString() ?? "null"));
string HashFile(string path) { using var file = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); }
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
bool Ready(JsonObject e, bool meal = false)
{
    var observed = CarnivalRecipes.ClassifyEntity(e);
    return meal ? !observed.IsPlate && CarnivalRecipes.MatchRecipe(e, 472326, false).ReadyToDeliver :
        observed.EvidenceGaps.Length == 0 && observed.Food.Kind == FoodNodeKind.Cooked &&
        observed.Food.Preparation == FoodPreparation.Cooked && observed.Food.CookingStepId == CarnivalRecipes.PanCookingStepId &&
        observed.Food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Onion.Id });
}
JsonObject report;
try
{
    bool classifierOnly = args.Contains("--classifier-only");
    var options = args.Skip(4).Where(a => a != "--classifier-only").ToArray();
    Require(args.Length >= 4 && (options.Length == 0 || options.Length == 2 && options[0] == "--controller-bundle"),
        "Usage: TRACE PLAN RESULT OUTPUT [--controller-bundle DIRECTORY] [--classifier-only]");
    string? selectedBundle = options.Length == 2 ? options[1] : null;
    string bundle = Path.GetFullPath(selectedBundle ?? "artifacts/planner-candidate-v13");
    string controller = Path.Combine(bundle, "OvercookedTAS.Controller.dll"), plugin = "lab/runtime/BepInEx/plugins/Oc2Tas.dll";
    string manifestPath = Path.Combine(bundle, "manifest.json");
    var plan = Read(args[1]); var result = Read(args[2]); var manifest = Read(manifestPath);
    var resultState = result["state"]!.AsObject();
    Require(result["ok"]?.GetValue<bool>() == true && result["paused"]?.GetValue<bool>() == true, "Result must be successful and paused.");
    string controllerHash = HashFile(controller), pluginHash = HashFile(plugin);
    Require(controllerHash == manifest["controllerSha256"]?.ToString(), "Frozen controller differs from its manifest.");
    string classifierPath = typeof(CarnivalRecipes).Assembly.Location;
    Require(HashFile(classifierPath) == controllerHash, "Loaded classifier does not match the selected frozen controller; rebuild the checker with -p:ControllerBundle=<absolute-directory>.");
    var sourceFiles = manifest["sourceFiles"]!.AsArray().OfType<JsonObject>().ToArray();
    foreach (var source in sourceFiles)
        Require(HashFile(Path.Combine(bundle, "source", source["path"]!.ToString())) == source["sha256"]?.ToString(), "Frozen source file hash mismatch.");
    string sourceTree = HashBytes(Encoding.UTF8.GetBytes(string.Concat(sourceFiles.OrderBy(s => s["path"]!.ToString(), StringComparer.Ordinal)
        .Select(s => s["path"]!.ToString() + "\0" + s["sha256"]!.ToString().ToLowerInvariant() + "\n"))));
    Require(sourceTree == manifest["controllerSourceTreeSha256"]?.ToString(), "Frozen source tree hash mismatch.");
    var jobs = plan["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
    Require(jobs.Length == 9 && jobs.Select(j => j["id"]!.ToString()).Distinct().Count() == 9 && I(plan["timeoutFrames"]) == 5200, "Unexpected authored probe bounds.");
    Require(jobs.Single(j => j["id"]?.ToString() == DwellJob)["actions"]!.AsArray().OfType<JsonObject>().Any(a => a["type"]?.ToString() == "wait" && I(a["durationFrames"]) >= 780), "Missing authored thirteen-second wait.");
    Require(jobs.SelectMany(j => j["actions"]!.AsArray().OfType<JsonObject>()).All(a => I(a["timeoutFrames"]) > 0 &&
        new[] { "take", "place", "chop", "mix", "cook", "throw", "combine", "navigate", "wait" }.Contains(a["type"]?.ToString())), "Unbounded or unexpected authored action.");
    var done = new HashSet<string>(); var started = new HashSet<string>(); var commandCounts = new Dictionary<string, int>();
    int pan = 0, home = 0, park = 0, output = 0, board = 0, food = 0, potOrdinal = -1, homeOrdinal = -1, parkOrdinal = -1, foodOrdinal = -1;
    int firstFrame = -1, lastFrame = -1, dwellFirst = -1, dwellLast = -1, dwellSamples = 0, calls = 0, stateSamples = 0, dashRequests = 0, heldVesselDashFrames = 0;
    double firstTime = 0, lastTime = 0, firstTimer = 0, lastTimer = 0, progress = 0, minHeat = double.PositiveInfinity, maxHeat = double.NegativeInfinity;
    bool dwelling = false, dwellDone = false, heldCooked = false, heldCombined = false, plainBaseObserved = false;
    int plainBaseFirstFrame = -1, onionCombinedFirstFrame = -1;
    string? foodHash = null, instrumentationHash = null; JsonObject? last = null;
    JsonObject Entity(JsonObject s, int id) => s["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == id);
    void Dwell(JsonObject s)
    {
        int frame = I(s["gameplayFrame"]); if (frame == dwellLast) return;
        var b = Entity(s, pan); var h = Entity(s, home); var p = Entity(s, park);
        Require(I(b["observedOrdinal"]) == potOrdinal && I(h["observedOrdinal"]) == homeOrdinal && I(p["observedOrdinal"]) == parkOrdinal, "Dwell native identity changed.");
        Require(I(h["attachedEntityId"]) == 0 && I(p["attachedEntityId"]) == pan && Ready(b) &&
            s["chefs"]!.AsArray().OfType<JsonObject>().All(c => I(c["heldEntityId"]) != pan), "Dwell pan is not the same fully cooked pan-fried onions on its ordinary offheat counter.");
        Require(!p["components"]!.AsArray().Any(c => c!.ToString().Contains("CookingStation", StringComparison.Ordinal) || c.ToString().Contains("MixingStation", StringComparison.Ordinal)), "Dwell counter is a processing station.");
        double current = N(b["cookingProgress"]); string hash = HashNode(b["composition"]);
        if (dwellFirst < 0) { dwellFirst = frame; firstTime = N(s["clientTime"]); firstTimer = N(s["timer"]); progress = current; foodHash = hash; }
        else
        {
            Require(frame == dwellLast + 1, "Dwell native frame alignment has a gap.");
            Require(N(s["clientTime"]) > lastTime && N(s["timer"]) < lastTimer, "Dwell native clock/timer did not advance monotonically.");
        }
        Require(current == progress && hash == foodHash, "Native composition or cooking progress changed off heat.");
        dwellLast = frame; lastTime = N(s["clientTime"]); lastTimer = N(s["timer"]); dwellSamples++;
    }
    using var file = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
    long traceBytes = file.Length; Require(traceBytes > 0 && traceBytes <= 1_000_000_000, "Trace exceeds bounded probe size.");
    string traceHash = Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant(); file.Position = 0;
    using var gzip = new GZipStream(file, CompressionMode.Decompress, leaveOpen: true); using var reader = new StreamReader(gzip);
    string? line; int lines = 0;
    while ((line = reader.ReadLine()) is not null)
    {
        Require(++lines <= 20000 && line.Length < 8_000_000, "Trace exceeds bounded line budget.");
        using var document = JsonDocument.Parse(line); var row = document.RootElement;
        string? kind = row.GetProperty("kind").GetString();
        if (kind == "event")
        {
            string? name = row.GetProperty("name").GetString(); var value = row.GetProperty("value");
            Require(name is not ("planFailure" or "actionFailure" or "plannerFailure"), "Trace contains a native action/plan failure.");
            string? id = value.TryGetProperty("id", out var jobId) ? jobId.GetString() : null;
            if (name == "jobStart")
            {
                Require(id is not null && jobs.Any(j => j["id"]?.ToString() == id) && started.Add(id), "Unknown or duplicate authored job start.");
                if (id == DwellJob) { Require(last is not null, "Dwell lacks a preceding native state."); dwelling = true; Dwell(last!); }
            }
            if (name == "jobComplete")
            {
                Require(id is not null && started.Contains(id) && done.Add(id), "Unknown or duplicate job completion.");
                if (id == DwellJob) { Dwell(last!); dwelling = false; dwellDone = true; }
            }
            continue;
        }
        if (kind != "call") continue;
        calls++; Require(calls <= 5000, "Too many native protocol calls.");
        var request = row.GetProperty("request"); string command = request.GetProperty("command").GetString()!;
        Require(command is "restart" or "inspect" or "step", "Non-input native mutation command in probe: " + command);
        commandCounts[command] = commandCounts.GetValueOrDefault(command) + 1;
        if (command == "restart") Require(calls == 1, "Restart after probe start is forbidden.");
        if (command == "step")
        {
            Require(request.GetProperty("steps").GetInt32() == 1 && request.EnumerateObject().All(p => new[] { "version", "command", "steps", "inputs" }.Contains(p.Name)), "Probe step is not a one-frame ordinary input request.");
            var pads = request.GetProperty("inputs").EnumerateArray().ToArray();
            Require(pads.Length == 4 && pads.Select(p => p.GetProperty("player").GetInt32()).Order().SequenceEqual(new[] { 0, 1, 2, 3 }), "Step lacks four unique player inputs.");
            foreach (var pad in pads)
            {
                if (pad.GetProperty("dash").ValueKind == JsonValueKind.True) dashRequests++;
                Require(pad.EnumerateObject().All(p => new[] { "player", "x", "y", "pickup", "use", "dash" }.Contains(p.Name)), "Unexpected input property.");
                foreach (string axis in new[] { "x", "y" }) Require(double.IsFinite(pad.GetProperty(axis).GetDouble()) && Math.Abs(pad.GetProperty(axis).GetDouble()) <= 1, "Invalid movement axis.");
                foreach (string button in new[] { "pickup", "use", "dash" }) Require(pad.GetProperty(button).ValueKind is JsonValueKind.True or JsonValueKind.False, "Invalid native button value.");
            }
        }
        var response = row.GetProperty("response"); Require(response.GetProperty("ok").GetBoolean(), "Recorded native protocol call failed.");
        if (!response.TryGetProperty("state", out var native) || native.ValueKind != JsonValueKind.Object) continue;
        int frameNow = native.GetProperty("gameplayFrame").GetInt32();
        Require(native.GetProperty("scene").GetString() == "s_Day_3_4" && native.GetProperty("delivered").GetInt32() == 0 && native.GetProperty("score").GetInt32() == 0 && native.GetProperty("deductions").GetInt32() == 0, "Scene, delivery, score or timeout changed during diagnostic.");
        Require(native.GetProperty("gameEventsInstalled").GetBoolean() && native.GetProperty("gameEventsDropped").GetInt32() == 0 &&
            native.GetProperty("gameEvents").GetArrayLength() == 0, "Native event coverage absent or unexpected delivery/other gameplay event occurred.");
        var instrumentation = native.GetProperty("instrumentation"); string currentManifest = instrumentation.GetProperty("manifestSha256").GetString()!;
        Require(instrumentation.GetProperty("manifest").GetProperty("pluginSha256").GetString() == pluginHash &&
            instrumentation.GetProperty("nativePhysicsAutoSimulation").GetBoolean() && instrumentation.GetProperty("inputActive").GetBoolean() &&
            native.GetProperty("fixedDeltaTime").GetDouble() == .02 && native.GetProperty("captureFramerate").GetInt32() == 60 && !native.GetProperty("timerSuppressed").GetBoolean(), "Native clock/input/physics configuration or plugin hash differs.");
        instrumentationHash ??= currentManifest; Require(currentManifest == instrumentationHash, "Instrumentation changed during probe.");
        if (pan == 0)
        {
            var sessionValue = response.GetProperty("session");
            var session = JsonNode.Parse(sessionValue.ValueKind == JsonValueKind.String ? sessionValue.GetString()! : sessionValue.GetRawText())!.AsObject();
            Require(I(session["dlc"]) == 8 && I(session["variantPlayers"]) == 4 && I(session["serverUsers"]) == 4 && I(session["clientUsers"]) == 4,
                "Initial session does not attest the native four-player Carnival variant.");
            var initial = JsonNode.Parse(native.GetRawText())!.AsObject(); var model = KitchenModel.Build(initial);
            pan = model.Resolve(PanKey).EntityId; home = model.Resolve(HomeKey).EntityId; park = model.Resolve(ParkKey).EntityId;
            output = model.Resolve(OutputKey).EntityId; board = model.Resolve(BoardKey).EntityId;
            potOrdinal = I(Entity(initial, pan)["observedOrdinal"]); homeOrdinal = I(Entity(initial, home)["observedOrdinal"]);
            parkOrdinal = I(Entity(initial, park)["observedOrdinal"]); 
            Require(I(Entity(initial, home)["attachedEntityId"]) == pan && I(Entity(initial, park)["attachedEntityId"]) == 0 &&
                CarnivalRecipes.ClassifyEntity(Entity(initial, pan)).Food.IngredientIds.Length == 0, "Initial exact pan/stove or empty parking state differs.");
            firstFrame = frameNow;
        }
        Require(lastFrame < 0 || frameNow == lastFrame + (command == "step" ? 1 : 0), "Native frame/call alignment gap.");
        lastFrame = frameNow;
        // Retain only exact observed entities/chefs used by the proof; the full
        // response remains hashed in the immutable source trace.
        if (food == 0)
        {
            var rawEntities = native.GetProperty("entities").EnumerateArray().ToArray();
            int candidate = rawEntities.Single(e => e.GetProperty("id").GetInt32() == board).GetProperty("attachedEntityId").GetInt32();
            var entity = rawEntities.FirstOrDefault(e => e.GetProperty("id").GetInt32() == candidate);
            if (candidate != 0 && entity.ValueKind == JsonValueKind.Object)
            {
                var node = JsonNode.Parse(entity.GetRawText())!.AsObject(); var observed = CarnivalRecipes.ClassifyEntity(node);
                if (observed.EvidenceGaps.Length == 0 && observed.Food.Preparation == FoodPreparation.Chopped &&
                    observed.Food.IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Bun.Id }))
                { food = candidate; foodOrdinal = I(node["observedOrdinal"]); }
            }
        }
        var selected = native.GetProperty("entities").EnumerateArray().Where(e => new[] { pan, home, park, output, board, food }.Contains(e.GetProperty("id").GetInt32()));
        var s = new JsonObject { ["entities"] = new JsonArray(selected.Select(e => JsonNode.Parse(e.GetRawText())).ToArray()),
            ["chefs"] = JsonNode.Parse(native.GetProperty("chefs").GetRawText()), ["gameplayFrame"] = frameNow,
            ["clientTime"] = native.GetProperty("clientTime").GetDouble(), ["timer"] = native.GetProperty("timer").GetDouble() };
        var vessel = Entity(s, pan);
        Require(I(vessel["observedOrdinal"]) == potOrdinal && I(Entity(s, home)["observedOrdinal"]) == homeOrdinal &&
            I(Entity(s, park)["observedOrdinal"]) == parkOrdinal && N(vessel["cookingTime"]) == 12,
            "Native pan, original stove, storage identity or cooking duration changed outside dwell.");
        if (food != 0) Require(I(Entity(s, food)["observedOrdinal"]) == foodOrdinal && Entity(s, food)["active"]?.GetValue<bool>() == true,
            "Observed chopped bun identity was lost before the final native hotdog.");
        Require(s["chefs"]!.AsArray().OfType<JsonObject>().Select(c => I(c["playerId"])).Order().SequenceEqual(new[] { 0, 1, 2, 3 }),
            "Native frame does not contain the same four player IDs.");
        if (I(Entity(s, home)["attachedEntityId"]) == pan && CarnivalRecipes.ClassifyEntity(vessel).Food.IngredientIds.Length > 0)
        { minHeat = Math.Min(minHeat, N(vessel["cookingProgress"])); maxHeat = Math.Max(maxHeat, N(vessel["cookingProgress"])); }
        var chef = s["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == 0);
        if (I(chef["heldEntityId"]) == pan && N(chef["dashTimer"]) > 0) heldVesselDashFrames++;
        if (I(chef["heldEntityId"]) == pan && I(Entity(s, home)["attachedEntityId"]) == 0 && Ready(vessel)) heldCooked = true;
        var assembler = s["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == 3);
        if (food != 0 && CarnivalRecipes.MatchRecipe(Entity(s, food), 296560, false).ReadyToDeliver)
        {
            if (!plainBaseObserved) plainBaseFirstFrame = frameNow;
            plainBaseObserved = true;
        }
        if (dwellDone && I(assembler["heldEntityId"]) == food && Ready(Entity(s, food), true))
        {
            Require(plainBaseObserved, "Onion hotdog appeared without observing the same exact native plain hotdog first.");
            if (!heldCombined) onionCombinedFirstFrame = frameNow;
            heldCombined = true;
        }
        if (dwelling) Dwell(s);
        last = s; stateSamples++;
        if (frameNow == I(resultState["gameplayFrame"])) Require(JsonNode.DeepEquals(JsonNode.Parse(native.GetRawText()), resultState), "Closed result differs from final recorded native state.");
    }
    Require(file.Length == traceBytes && done.Count == jobs.Length && !dwelling && dwellDone, "Closed trace or job completion proof is incomplete.");
    Require(dwellSamples >= 781 && dwellLast - dwellFirst >= 780 && lastTime - firstTime >= 13 && firstTimer - lastTimer >= 13, "Less than thirteen observed native seconds off heat.");
    Require(heldCooked && maxHeat - minHeat >= 11.9 && plainBaseObserved && heldCombined, "Native full heating, whole-pan pickup or cooked combined onion hotdog transition missing.");
    Require(last is not null && lastFrame == I(resultState["gameplayFrame"]), "Final result is not the last recorded frame.");
    Require(I(Entity(last!, output)["attachedEntityId"]) == food && I(Entity(last!, food)["observedOrdinal"]) == foodOrdinal && Ready(Entity(last!, food), true), "Final exact food lacks native completed onion hotdog recipe at output.");
    Require(I(Entity(last!, home)["attachedEntityId"]) == pan && I(Entity(last!, park)["attachedEntityId"]) == 0 &&
        CarnivalRecipes.ClassifyEntity(Entity(last!, pan)).Food.IngredientIds.Length == 0 && N(Entity(last!, pan)["cookingProgress"]) == 0, "Original empty pan not restored/reset on its original stove.");
    Require(last!["chefs"]!.AsArray().Count == 4 && last["chefs"]!.AsArray().OfType<JsonObject>().All(c => I(c["heldEntityId"]) == 0 && c["controlsEnabled"]?.GetValue<bool>() == true), "Final four chefs are not controlled and empty-handed.");
    Require(result["inputs"] is JsonArray finalInputs && finalInputs.Count == 4 && finalInputs.OfType<JsonObject>().Select(i => I(i["player"])).Order().SequenceEqual(new[] { 0, 1, 2, 3 }) &&
        finalInputs.OfType<JsonObject>().All(i => N(i["x"]) == 0 && N(i["y"]) == 0 && new[] { "pickup", "use", "dash" }.All(k => i[k]?.GetValue<bool>() == false)),
        "Final completed inputs are not four neutral native controller slots.");
    report = new() { ["ok"] = true, ["classification"] = "Observed native pan offheat/combine/restore probe; no high-score or replay qualification.",
        ["tracePath"] = Path.GetFullPath(args[0]), ["traceBytes"] = traceBytes, ["traceSha256"] = traceHash,
        ["planSha256"] = HashFile(args[1]), ["resultSha256"] = HashFile(args[2]), ["checkerSourceSha256"] = HashFile("scripts/OnionOffheatProbeCheck/Program.cs"),
        ["checkerProjectSha256"] = HashFile("scripts/OnionOffheatProbeCheck/OnionOffheatProbeCheck.csproj"), ["controllerSha256"] = controllerHash,
        ["controllerSourceTreeSha256"] = sourceTree, ["frozenSourceFilesVerified"] = sourceFiles.Length, ["controllerManifestSha256"] = HashFile(manifestPath),
        ["controllerBundle"] = bundle, ["classifierLoadedPath"] = classifierPath, ["classifierSha256"] = controllerHash,
        ["controllerSelection"] = selectedBundle is not null ? "explicit --controller-bundle" : "V13 classifier default",
        ["controllerBundleRole"] = classifierOnly ? "classifier-only regression reanalysis; execution bundle not asserted" : "operator-identified execution and verified classifier",
        ["controllerProvenance"] = classifierOnly ? "V13 classifier reanalyzes this trace; no assertion that this controller executed its inputs." :
            "Operator identified " + Path.GetFileName(bundle) + " as the executing candidate; trace header itself records only controller version. The actually loaded classifier hash equals this selected frozen assembly.",
        ["pluginFileAndTelemetrySha256"] = pluginHash, ["instrumentationManifestSha256"] = instrumentationHash,
        ["calls"] = calls, ["stateSamples"] = stateSamples, ["firstFrame"] = firstFrame, ["finalFrame"] = lastFrame,
        ["dashButtonRequests"] = dashRequests, ["heldVesselNativeDashFrames"] = heldVesselDashFrames,
        ["panId"] = pan, ["panObservedOrdinal"] = potOrdinal, ["stoveId"] = home, ["stoveObservedOrdinal"] = homeOrdinal,
        ["parkCounterId"] = park, ["outputCounterId"] = output, ["foodId"] = food, ["foodObservedOrdinal"] = foodOrdinal,
        ["dwellSamples"] = dwellSamples, ["dwellFirstFrame"] = dwellFirst, ["dwellLastFrame"] = dwellLast,
        ["nativeElapsedSeconds"] = lastTime - firstTime, ["timerElapsedSeconds"] = firstTimer - lastTimer,
        ["unchangedCookingProgress"] = progress, ["unchangedCompositionSha256"] = foodHash, ["onHeatProgressGain"] = maxHeat - minHeat,
        ["wholeCookedPanPickupObserved"] = heldCooked, ["nativeCookedOnionHotdogObserved"] = heldCombined,
        ["sameNativePlainBaseObserved"] = plainBaseObserved, ["plainBaseFirstFrame"] = plainBaseFirstFrame, ["onionCombinedFirstFrame"] = onionCombinedFirstFrame,
        ["allFourInputsNeutral"] = true, ["finalRecipeId"] = 472326, ["originalEmptyPanRestored"] = true, ["choppedBunBoard"] = board, ["sameObservedBunBecamePlainThenOnionHotdog"] = true, ["fourControlledEmptyChefs"] = true,
        ["noDeliveryScoreOrTimeout"] = true, ["ordinaryInputCommandsOnly"] = true, ["commands"] = JsonSerializer.SerializeToNode(commandCounts), ["completedJobs"] = done.Count };
}
catch (Exception error) { report = new() { ["ok"] = false, ["error"] = error.Message, ["detail"] = error.ToString() }; Environment.ExitCode = 1; }
string text = report.ToJsonString(new() { WriteIndented = true }); if (args.Length >= 4) File.WriteAllText(args[3], text + Environment.NewLine); Console.WriteLine(text);
