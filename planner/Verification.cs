using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

// Uses Program's existing Call delegate, hence one TasClient connection for
// prelude, validation and movie. No network implementation or adaptive tail.
public static class Verification
{
    public static async Task<JsonObject> RunAsync(Options options,
        Func<JsonObject, Task<JsonObject>> call, TraceWriter? processTrace)
    {
        var movie = Path.GetFullPath(options.Required("file"));
        var manifestPath = Path.GetFullPath(options.Get("manifest", movie + ".manifest.json"));
        var prefix = Path.GetFullPath(options.Required("prefix"));
        var runs = options.Int("runs", 1);
        var mode = options.Get("validation-mode", "highscore");
        var renderRate = options.Int("render-rate", 60);
        if (runs is < 1 or > 4 || mode is not ("highscore" or "probe") || renderRate is not (0 or 30 or 60 or 120))
            throw new ArgumentException("Verification requires runs 1..4, highscore/probe mode and render-rate 0/30/60/120.");
        var audit = AuditMovie(movie, manifestPath);
        var first = Traces.ExtractRequest(Traces.ReadCalls(movie).First());
        var seed = first["seed"]!.GetValue<int>();
        var isolation = first["isolateRecipeRandom"]!.GetValue<bool>();
        processTrace?.Event("verificationMovieAudit", audit);
        var render = Json.Request("render");
        render["width"] = 1280; render["height"] = 720; render["renderRate"] = renderRate;
        await call(render);
        // A recorded restart requires an already-established Carnival session.
        // This prelude is logged separately; the movie's own restart remains.
        if (first["command"]!.GetValue<string>() == "restart")
        {
            var load = Json.Request("load"); load["seed"] = seed; load["isolateRecipeRandom"] = isolation;
            var loaded = await call(load);
            ValidateInitial(loaded);
            processTrace?.Event("verificationPreludeValidated", InitialProof(loaded));
        }
        var results = new JsonArray();
        for (var run = 1; run <= runs; run++)
        {
            if (FileHash(movie) != audit["movieFileSha256"]!.GetValue<string>())
                throw new InvalidDataException("Movie changed after preflight.");
            var tracePath = prefix + $"-run{run:D2}.jsonl.gz";
            if (File.Exists(tracePath)) throw new IOException("Verification trace already exists: " + tracePath);
            using var bodyTrace = new TraceWriter(tracePath, "verification-movie");
            var coverage = new NativeProbeCoverage();
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            JsonObject? finalResponse = null;
            JsonObject? initialProof = null;
            var count = 0;
            foreach (var entry in Traces.ReadCalls(movie))
            {
                var request = Traces.ExtractRequest(entry);
                digest.AppendData(CanonicalBytes(request));
                finalResponse = await call(request);
                bodyTrace.Call(request, finalResponse);
                if (request["command"]?.GetValue<string>() == "preview")
                {
                    ValidatePreview(request, finalResponse);
                    bodyTrace.Event("verificationPreviewRestorationValidated", new JsonObject
                    {
                        ["seed"] = request["seed"]!.DeepClone(), ["count"] = request["count"]!.DeepClone(),
                        ["frame"] = finalResponse["preview"]!["frame"]?.DeepClone(),
                        ["restorationChecks"] = new JsonArray(PreviewRestorationFields.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray())
                    });
                }
                if (count == 0)
                {
                    // Enforced synchronously before the next movie request.
                    ValidateInitial(finalResponse);
                    ValidateInstrumentation(finalResponse["state"]!.AsObject(),seed,isolation);
                    initialProof=InitialProof(finalResponse);
                    bodyTrace.Event("verificationInitialValidated", initialProof);
                }
                var state = finalResponse["state"]!.AsObject();
                ValidateObservers(state);
                coverage.Observe(state);
                count++;
            }
            var inputHash = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            if (inputHash != audit["dotnetCanonicalRequestsSha256"]!.GetValue<string>())
                throw new InvalidDataException("Executed request sequence differs from preflight movie.");
            if (finalResponse is null) throw new InvalidDataException("No movie response.");
            if (mode == "highscore") ValidateFinished(finalResponse);
            else coverage.RequireCompleteProbe();
            RequireNeutral(finalResponse);
            var finalState = finalResponse["state"]!.AsObject();
            var result = new JsonObject
            {
                ["run"] = run, ["mode"] = mode, ["trace"] = tracePath,
                ["movieFileSha256"] = audit["movieFileSha256"]!.DeepClone(),
                ["executedRequestsSha256"] = inputHash, ["requests"] = count,
                ["renderRate"] = renderRate, ["nativeSetupValidatedBeforeInput"] = true,
                ["initialProof"] = initialProof?.DeepClone(),
                ["score"] = finalState["score"]?.DeepClone(), ["delivered"] = finalState["delivered"]?.DeepClone(),
                ["timer"] = finalState["timer"]?.DeepClone(), ["gameState"] = finalState["gameState"]?.DeepClone(),
                ["gameplayFrame"] = finalState["gameplayFrame"]?.DeepClone(),
                ["roundComplete"] = IsFinished(finalState), ["coverage"] = coverage.Json(), ["coverageEvidence"] = coverage.Evidence(),
                ["inputsNeutral"] = true, ["passedExecutionGate"] = true
            };
            if (mode == "highscore")
            {
                result["nativeScoreProof"] = ValidateScoreEvidence(finalState);
                // Evidence capture is after the immutable movie and never
                // contributes an input or request to its canonical digest.
                result["finalScreenshot"] = await CaptureFinalScreenshotAsync(prefix+$"-run{run:D2}-final.png",finalResponse,inputHash,call);
                processTrace?.Event("verificationFinalScreenshot",result["finalScreenshot"]);
            }
            bodyTrace.Event("verificationRunPassed", result);
            processTrace?.Event("verificationRunPassed", result);
            results.Add(result);
        }
        var initialHashes=new JsonArray(results.Select(r=>r!["initialProof"]!["canonicalInitialStateSha256"]!.DeepClone()).ToArray());
        return new JsonObject { ["ok"] = true, ["movieAudit"] = audit, ["runs"] = results,
            ["canonicalInitialStateHashes"] = initialHashes,
            ["canonicalInitialStatesAggregateSha256"] = NodeHash(initialHashes),
            ["classification"] = "execution_gates_passed_cross_run_comparison_pending" };
    }

    public static JsonObject AuditMovie(string movie, string manifestPath)
    {
        var manifest = Json.Object(File.ReadAllText(manifestPath));
        var fileHash = FileHash(movie);
        if (!string.Equals(manifest["outputFileSha256"]?.GetValue<string>(), fileHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Manifest outputFileSha256 does not match exact movie bytes.");
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var count = 0; long frames = 0; int previewRequests = 0, recordedPreviewResponses = 0;
        JsonObject? first = null;
        foreach (var entry in Traces.ReadCalls(movie))
        {
            var request = Traces.ExtractRequest(entry);
            if (request["version"]?.GetValue<int>() != 1) throw new InvalidDataException("Movie protocol must be version 1.");
            var command = request["command"]?.GetValue<string>();
            if (count == 0)
            {
                first = request;
                if (command is not ("load" or "restart") || request["seed"] is null || request["isolateRecipeRandom"] is null)
                    throw new InvalidDataException("Movie must begin with explicit native load/restart, seed and recipe isolation setting.");
                _ = request["seed"]!.GetValue<int>(); _ = request["isolateRecipeRandom"]!.GetValue<bool>();
            }
            else if (command is not ("step" or "inspect" or "state" or "pause" or "preview"))
                throw new InvalidDataException("Movie body may contain only step/inspect/state/pause and validated read-only preview requests; render and lifecycle commands belong in the recorded prelude.");
            if (command == "preview")
            {
                ValidatePreviewRequest(request); previewRequests++;
                if (entry.ContainsKey("response"))
                {
                    ValidatePreview(request, entry["response"] as JsonObject ?? throw new InvalidDataException("Recorded preview response must be an object."));
                    recordedPreviewResponses++;
                }
            }
            if (command == "step")
            {
                if ((request["steps"]?.GetValue<int>() ?? 1) != 1)
                    throw new InvalidDataException("Verification movie must capture every frame (step count 1); validate any expansion before this gate.");
                frames++;
            }
            digest.AppendData(CanonicalBytes(request)); count++;
        }
        if (first is null || frames == 0) throw new InvalidDataException("Movie has no gameplay frames.");
        var recordedStart = manifest["starts"]?.AsArray().FirstOrDefault()?["request"];
        if (recordedStart is null || !JsonNode.DeepEquals(first, recordedStart))
            throw new InvalidDataException("Manifest does not bind the movie's initial load/restart request.");
        return new JsonObject { ["movie"] = Path.GetFullPath(movie), ["manifest"] = Path.GetFullPath(manifestPath),
            ["movieFileSha256"] = fileHash, ["manifestFileSha256"] = FileHash(manifestPath),
            ["dotnetCanonicalRequestsSha256"] = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant(),
            ["hashEncoding"] = "Sorted-key System.Text.Json UTF-8 request + LF; numeric formatting follows .NET, distinct from the extractor's Python canonical hash",
            ["requests"] = count, ["frames"] = frames, ["startup"] = first.DeepClone(),
            ["previewRequests"] = previewRequests, ["recordedPreviewResponsesValidated"] = recordedPreviewResponses,
            ["previewResponsesDeferredToExecution"] = previewRequests - recordedPreviewResponses,
            ["previewPolicy"] = "Recorded responses are checked when present; input-only movies defer response validation. Every executed preview must preserve all six native restoration invariants.",
            ["perFrameMovie"] = true, ["feedbackTailAdded"] = false };
    }

    private static readonly string[] PreviewRestorationFields = ["ambientRngRestored", "isolatedRngRestored", "isolatedRegistryRestored", "observationsRestored", "liveInstanceUnchanged", "frameUnchanged"];

    public static void ValidatePreviewRequest(JsonObject request)
    {
        if (request["version"]?.GetValue<int>() != 1 || request["command"]?.GetValue<string>() != "preview"
            || request.Any(pair => pair.Key is not ("version" or "command" or "seed" or "count")))
            throw new InvalidDataException("Read-only preview requests require only version, command, explicit seed and count fields.");
        if (request["seed"] is not JsonValue seed || !seed.TryGetValue<int>(out _)
            || request["count"] is not JsonValue count || !count.TryGetValue<int>(out var length) || length is < 1 or > 1024)
            throw new InvalidDataException("Read-only preview requires an explicit Int32 seed and count 1..1024.");
    }

    public static void ValidatePreview(JsonObject request, JsonObject response)
    {
        ValidatePreviewRequest(request);
        Json.RequireOk(response);
        var preview = response["preview"] as JsonObject ?? throw new InvalidDataException("Read-only preview response is missing.");
        if (preview["seed"]?.GetValue<int>() != request["seed"]!.GetValue<int>() || preview["count"]?.GetValue<int>() != request["count"]!.GetValue<int>()
            || !SceneMatches(preview["scene"]) || preview["generator"]?.GetValue<string>() != "RoundData" || preview["freshInstance"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Read-only preview must echo the requested seed/count and independent native Carnival RoundData configuration.");
        foreach (var field in PreviewRestorationFields)
            if (preview[field]?.GetValue<bool>() != true) throw new InvalidDataException("Read-only preview restoration assertion failed or is missing: " + field);
        // When detailed before/after observations are present, do not accept a
        // contradictory true flag. Older recordings may omit these details.
        foreach (var pair in new[] { ("ambientBefore", "ambientAfter"), ("isolatedBefore", "isolatedAfter"),
            ("isolatedRegistryBefore", "isolatedRegistryAfter"), ("liveRoundInstanceOrdinalBefore", "liveRoundInstanceOrdinalAfter"),
            ("observedDrawsBefore", "observedDrawsAfter"), ("lifecycleBefore", "lifecycleAfter"),
            ("liveRecipeCountBefore", "liveRecipeCountAfter"), ("liveFrequenciesBefore", "liveFrequenciesAfter") })
            if ((preview.ContainsKey(pair.Item1) || preview.ContainsKey(pair.Item2))
                && (!preview.ContainsKey(pair.Item1) || !preview.ContainsKey(pair.Item2) || !JsonNode.DeepEquals(preview[pair.Item1], preview[pair.Item2])))
                throw new InvalidDataException("Read-only preview restoration details contradict their assertion: " + pair.Item1);
        var recipes = preview["recipes"] as JsonArray ?? throw new InvalidDataException("Native preview recipes are absent.");
        if (recipes.Count != request["count"]!.GetValue<int>()) throw new InvalidDataException("Native preview returned a different recipe count.");
        for (var index = 0; index < recipes.Count; index++)
        {
            var recipe = recipes[index] as JsonObject ?? throw new InvalidDataException("Native preview recipe is missing.");
            if (recipe["index"]?.GetValue<int>() != index || recipe["recipeId"] is not JsonValue id || !id.TryGetValue<int>(out var recipeId)
                || !CarnivalRecipes.ById.TryGetValue(recipeId, out var expected) || recipe["baseValue"]?.GetValue<int>() != expected.BaseScore)
                throw new InvalidDataException("Native preview recipe sequence/value differs from audited Carnival configuration.");
        }
    }

    public static void ValidateInitial(JsonObject response)
    {
        Json.RequireOk(response);
        var state = response["state"]?.AsObject() ?? throw new InvalidDataException("No native world snapshot.");
        var session = Json.Object(response["session"]?.GetValue<string>() ?? "{}");
        if (session["stage"]?.GetValue<string>() != "kitchen_ready" || session["busy"]?.GetValue<bool>() != false
            || session["dlc"]?.GetValue<int>() != 8 || session["variantPlayers"]?.GetValue<int>() != 4
            || session["serverUsers"]?.GetValue<int>() != 4 || session["clientUsers"]?.GetValue<int>() != 4
            || !SceneMatches(session["variantScene"]) || !SceneMatches(state["scene"]))
            throw new InvalidDataException("Expected validated local Carnival DLC8/3-4/four-player kitchen.");
        if (state["gameplayFrame"]?.GetValue<long>() != 0 || state["levelReady"]?.GetValue<bool>() != true
            || state["gameState"]?.GetValue<string>() != "InLevel" || response["paused"]?.GetValue<bool>() != true)
            throw new InvalidDataException("Initial snapshot must be paused at native gameplay frame zero.");
        ValidateInitialClocks(state);
        ValidateInstrumentation(state);
        ValidateRoundDuration(state);
        ValidateRegistration(state,true);
        var chefs = state["chefs"]?.AsArray() ?? throw new InvalidDataException("Chef observations unavailable.");
        var ids = chefs.Select(c => c?["playerId"]?.GetValue<int>() ?? -1).Order().ToArray();
        if (!ids.SequenceEqual(new[] { 0, 1, 2, 3 })) throw new InvalidDataException("Expected four distinct locally assigned chef slots.");
        // The native round loop has ticked once at the observed frame-zero
        // boundary. This is the measured 270-second configuration check, not an
        // invented exact comparison to an untouched 270.0 remainder.
        var timer = state["timer"]?.GetValue<double>() ?? -1;
        if (Math.Abs(timer - (270.0 - 1.0 / 60.0)) > 0.0001)
            throw new InvalidDataException("Expected native 270-second round after one frame-zero tick.");
        if (state["score"]?.GetValue<int>() != 0 || state["delivered"]?.GetValue<int>() != 0)
            throw new InvalidDataException("Movie must begin with zero score and deliveries.");
        ValidateObservers(state); RequireNeutral(response);
    }

    public static void ValidateInitialClocks(JsonObject state)
    {
        if(RequiredInt(state,"captureFramerate")!=60||state["alignStartPhysics"]?.GetValue<bool>()!=true
            ||RequiredInt(state,"levelStartPhysicsPhase")!=5||RequiredInt(state,"framesSinceNoPhysics")!=5
            ||RequiredInt(state,"physicsStepsThisFrame")!=1||RequiredLong(state,"gameplayFixedFrame")!=0)
            throw new InvalidDataException("Frame zero requires actual capture60, native50Hz alignment enabled, and measured start phase5.");
        foreach(var field in new[]{"logicalTime","clientTime","levelClientTimeZero","clientDeltaTime","unityDeltaTime","unityMaximumDeltaTime","fixedDeltaTime"})
            if(!double.IsFinite(RequiredDouble(state,field)))throw new InvalidDataException("Nonfinite native clock: "+field);
        if(Math.Abs(RequiredDouble(state,"fixedDeltaTime")-.02)>1e-8
            ||Math.Abs(RequiredDouble(state,"unityDeltaTime")-1.0/60)>1e-7
            ||Math.Abs(RequiredDouble(state,"clientDeltaTime")-1.0/60)>.00001
            ||Math.Abs(RequiredDouble(state,"clientTime")-RequiredDouble(state,"levelClientTimeZero"))>1e-6)
            throw new InvalidDataException("Native clock values do not demonstrate the required60Hz logical/50Hz physics frame-zero boundary.");
        if(RequiredLong(state,"frame")!=RequiredLong(state,"levelFrameZero")
            ||RequiredLong(state,"fixedFrame")!=RequiredLong(state,"levelFixedFrameZero")
            ||RequiredLong(state,"startAlignmentReleaseFrame")<0||RequiredLong(state,"startAlignmentReleaseFrame")>RequiredLong(state,"frame")
            ||RequiredInt(state,"startAlignmentWaitFrames")<0||RequiredLong(state,"introFrameZero")<0)
            throw new InvalidDataException("Native absolute frame-zero and alignment clock evidence is inconsistent.");
        if(state["serverRoundActive"]?.GetValue<bool>()!=true||state["clientRoundActive"]?.GetValue<bool>()!=true||state["timerSuppressed"]?.GetValue<bool>()!=false)
            throw new InvalidDataException("Both native rounds must be active with the round timer unsuppressed at frame zero.");
    }

    public static void ValidateInstrumentation(JsonObject state,int? expectedSeed=null,bool? expectedIsolation=null)
    {
        var observed=state["instrumentation"] as JsonObject??throw new InvalidDataException("Native instrumentation status is absent.");
        if(observed["error"] is null||!string.IsNullOrEmpty(observed["error"]!.GetValue<string>()))throw new InvalidDataException("Instrumentation status reports an error or is incomplete.");
        foreach(string flag in new[]{"inputActive","logicalClockActive","alignStartPhysics","nativePhysicsAutoSimulation"})
            if(observed[flag]?.GetValue<bool>()!=true)throw new InvalidDataException("Required native instrumentation flag is inactive: "+flag);
        int seed=RequiredInt(observed,"seed");
        if(observed["isolateRecipeRandom"] is not JsonValue isolation||!isolation.TryGetValue<bool>(out var isolated)
            ||expectedSeed.HasValue&&seed!=expectedSeed.Value||expectedIsolation.HasValue&&isolated!=expectedIsolation.Value)
            throw new InvalidDataException("Actual native seed/order-isolation mode differs from the movie request or is missing.");
        static bool Hash(JsonNode? value)=>value?.GetValue<string>() is {Length:64} hash&&hash.All(Uri.IsHexDigit);
        if(!Hash(observed["manifestSha256"]))throw new InvalidDataException("Instrumentation manifest SHA256 is missing or invalid.");
        var manifest=observed["manifest"] as JsonObject??throw new InvalidDataException("Instrumentation manifest is absent.");
        if(RequiredInt(manifest,"version")!=1)throw new InvalidDataException("Unsupported instrumentation manifest version.");
        foreach(string field in new[]{"identifier","unityVersion","runtimeArchitecture","frameGate","clockPolicy","orderPolicy","inputPolicy","focusPolicy","savePolicy","hookEvidencePolicy"})
            if(string.IsNullOrWhiteSpace(manifest[field]?.GetValue<string>()))throw new InvalidDataException("Instrumentation manifest field missing: "+field);
        foreach(string field in new[]{"pluginSha256","gameAssemblySha256","executableSha256"})
            if(!Hash(manifest[field]))throw new InvalidDataException("Instrumentation binary identity missing: "+field);
        foreach(string field in new[]{"pluginModuleVersionId","gameModuleVersionId"})
            if(!Guid.TryParse(manifest[field]?.GetValue<string>(),out _))throw new InvalidDataException("Instrumentation module identity missing: "+field);
        foreach(string field in new[]{"inputHooksInstalled","clockHooksInstalled","startAlignmentHookInstalled","saveHooksInstalled","gameEventHooksInstalled"})
            if(manifest[field]?.GetValue<bool>()!=true)throw new InvalidDataException("Required native hook group is not installed: "+field);
        if(manifest["hooks"] is not JsonArray hooks||hooks.Count==0||hooks.Any(h=>h is not JsonObject||new[]{"original","kind","handler","owner"}.Any(f=>string.IsNullOrWhiteSpace(h?[f]?.GetValue<string>()))))
            throw new InvalidDataException("Actual Harmony registration evidence is absent or incomplete.");
        if(manifest["loadedPlugins"] is not JsonArray plugins||plugins.Count!=1||plugins[0]?["identifier"]?.GetValue<string>()!="local.oc2tas.newbot"
            ||string.IsNullOrWhiteSpace(plugins[0]?["version"]?.GetValue<string>())
            ||!string.Equals(plugins[0]?["assemblySha256"]?.GetValue<string>(),manifest["pluginSha256"]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("BepInEx must report exactly the new TAS plugin, bound to the instrumented plugin assembly hash.");
    }

    public static void ValidateRoundDuration(JsonObject state)
    {
        var duration=state["roundDuration"] as JsonObject??throw new InvalidDataException("Loaded native round duration evidence is absent.");
        foreach(string field in new[]{"available","loadedRoundDataAvailable","timerAvailable","valuesAgree"})
            if(duration[field]?.GetValue<bool>()!=true)throw new InvalidDataException("Native loaded round/timer duration evidence is incomplete: "+field);
        if(!string.IsNullOrEmpty(duration["error"]?.GetValue<string>())||!SceneMatches(duration["scene"]))throw new InvalidDataException("Native duration source reports an error or another scene.");
        foreach(string field in new[]{"seconds","loadedRoundDataSeconds","timerLimitSeconds"})
            if(RequiredDouble(duration,field)!=270)throw new InvalidDataException("Loaded native Carnival round duration must be270seconds: "+field);
        foreach(string field in new[]{"source","levelConfigName","roundDataType","timerType"})
            if(string.IsNullOrWhiteSpace(duration[field]?.GetValue<string>()))throw new InvalidDataException("Native duration provenance is missing: "+field);
    }

    public static void ValidateRegistration(JsonObject state,bool requireInitialHistory)
    {
        var registry=state["entityRegistration"] as JsonObject??throw new InvalidDataException("Native entity registration audit is absent.");
        foreach(string flag in new[]{"installed","addHookInstalled","removeHookInstalled","clearHookInstalled"})
            if(registry[flag]?.GetValue<bool>()!=true)throw new InvalidDataException("Native registration hook is not installed: "+flag);
        if(RequiredLong(registry,"errorCount")!=0||!string.IsNullOrEmpty(registry["error"]?.GetValue<string>())||registry["status"]?.ToString()!="observing")
            throw new InvalidDataException("Native entity registration audit reports observation errors.");
        if(RequiredLong(registry,"roundDropped")!=0)throw new InvalidDataException("Native registration history dropped events in this round.");
        if(string.IsNullOrWhiteSpace(registry["owner"]?.GetValue<string>())||!Guid.TryParse(registry["nativeModuleVersionId"]?.GetValue<string>(),out _))
            throw new InvalidDataException("Native registration audit hook/module identity is missing.");
        if(!requireInitialHistory)return;
        foreach(var (field,kind) in new[]{("initialPreexisting","initial-preexisting"),("roundBegin","round-begin-preexisting")})
        {
            var checkpoint=registry[field] as JsonObject??throw new InvalidDataException("Native registry checkpoint missing: "+field);
            if(checkpoint["available"]?.GetValue<bool>()!=true||checkpoint["kind"]?.ToString()!=kind||checkpoint["entities"] is not JsonArray
                ||RequiredLong(checkpoint,"afterSequence")<0||RequiredLong(checkpoint,"frame")<0||RequiredLong(checkpoint,"fixedFrame")<0)
                throw new InvalidDataException("Native registry checkpoint is unavailable/incomplete: "+field);
        }
        var beginning=registry["roundBegin"]!.AsObject();int epoch=RequiredInt(registry,"roundEpoch");
        if(epoch<1||RequiredInt(beginning,"roundEpoch")!=epoch||registry["returnedHistoryLimited"]?.GetValue<bool>()!=false)
            throw new InvalidDataException("Frame-zero registry must include the complete current round history after its checkpoint.");
        long sequence=RequiredLong(beginning,"afterSequence");
        if(RequiredLong(registry,"returnedAfterSequence")>sequence)throw new InvalidDataException("Initial registry event cursor skips current-round registrations.");
        var current=new HashSet<int>();
        foreach(var entry in beginning["entities"]!.AsArray().OfType<JsonObject>())
            if(!current.Add(RequiredInt(entry,"entityId")))throw new InvalidDataException("Duplicate native entity identity in registry checkpoint.");
        var events=registry["events"] as JsonArray??throw new InvalidDataException("Native registration events missing.");
        foreach(var node in events)
        {
            var entry=node as JsonObject??throw new InvalidDataException("Invalid native registration event.");
            if(RequiredLong(entry,"sequence")!=++sequence||RequiredInt(entry,"roundEpoch")!=epoch||string.IsNullOrWhiteSpace(entry["method"]?.GetValue<string>()))
                throw new InvalidDataException("Initial native registration event order is incomplete or crosses a round boundary.");
            switch(entry["kind"]?.ToString())
            {
                case "register":
                    if(entry["entity"] is not JsonObject added||!current.Add(RequiredInt(added,"entityId"))||RequiredLong(added,"observedRegistrationSequence")!=sequence)
                        throw new InvalidDataException("Native registration event has duplicate or unbound identity.");
                    break;
                case "remove":
                    if(entry["entity"] is not JsonObject removed||!current.Remove(RequiredInt(removed,"entityId")))throw new InvalidDataException("Native removal has no observed registration/checkpoint identity.");
                    break;
                case "clear":
                    if(entry["clearedEntities"] is not JsonArray cleared||!current.SetEquals(cleared.Select(e=>RequiredInt(e!.AsObject(),"entityId"))))throw new InvalidDataException("Native bulk-clear evidence differs from the preceding registry.");
                    current.Clear();break;
                default:throw new InvalidDataException("Unknown native registration operation.");
            }
            if(RequiredInt(entry,"registryCountAfter")!=current.Count)throw new InvalidDataException("Native registry size disagrees with observed writes.");
        }
        if(sequence!=RequiredLong(registry,"lastSequence")||events.Count!=RequiredInt(registry,"retainedCount"))throw new InvalidDataException("Initial registration sequence window is incomplete.");
        foreach(var entity in state["entities"]?.AsArray().OfType<JsonObject>()??[])
            if(!current.Contains(RequiredInt(entity,"id")))throw new InvalidDataException("Frame-zero entity is not explained by native registry writes or the round checkpoint.");
    }

    private static double RequiredDouble(JsonObject value,string field)
    {
        if(value[field] is JsonValue number&&number.TryGetValue<double>(out var result))return result;
        throw new InvalidDataException("Required native numeric evidence is missing or invalid: "+field);
    }

    private static bool SceneMatches(JsonNode? value) => string.Equals(value?.GetValue<string>(), "s_Day_3_4", StringComparison.OrdinalIgnoreCase);

    public static void ValidateObservers(JsonObject state)
    {
        if (state["gameEventsInstalled"]?.GetValue<bool>() != true || state["gameEventsDropped"]?.GetValue<int>() != 0
            || !string.IsNullOrEmpty(state["gameEventsError"]?.GetValue<string>())
            || state["warnings"]?.AsArray().Count > 0)
            throw new InvalidDataException("Native observation evidence is missing, dropped, or reports errors.");
        ValidateRegistration(state,false);
    }

    public static bool IsFinished(JsonObject state) => state["timer"]?.GetValue<double>() == 0
        && state["serverRoundActive"]?.GetValue<bool>() == false && state["clientRoundActive"]?.GetValue<bool>() == false
        && state["gameState"]?.GetValue<string>() is "RunLevelOutro" or "RanLevelOutro";

    public static void ValidateFinished(JsonObject response)
    {
        Json.RequireOk(response);
        var state = response["state"]!.AsObject();
        if (!IsFinished(state) || !SceneMatches(state["scene"]))
            throw new InvalidDataException("Movie must include the native round ending; verifier will not add a feedback-driven tail.");
        if ((state["gameplayFrame"]?.GetValue<long>() ?? -1) < 16199)
            throw new InvalidDataException("Observed gameplay does not cover the native 270-second round at 60 logical frames/second.");
        if ((state["score"]?.GetValue<int>() ?? -1) < 5000)
            throw new InvalidDataException("Native completed-round score is below 5000.");
        ValidateScoreEvidence(state);
        ValidateObservers(state);
    }

    public static JsonObject ValidateScoreEvidence(JsonObject state)
    {
        var events = state["gameEvents"] as JsonArray ?? throw new InvalidDataException("Native score event history is absent.");
        string[] totals = ["score", "baseScore", "tips", "deductions", "delivered"];
        string[] deltas = ["scoreDelta", "baseScoreDelta", "tipDelta", "deductionDelta", "deliveryDelta"];
        var previous = new long[totals.Length];
        var sums = new long[totals.Length];
        long previousIndex = -1; int matched = 0, expired = 0, failed = 0;
        var resolvedOrders = new HashSet<int>();
        var deliveries = new JsonArray();
        foreach (var item in events)
        {
            var entry = item as JsonObject ?? throw new InvalidDataException("Native event entry is missing.");
            var index = RequiredLong(entry, "index");
            if (index <= previousIndex) throw new InvalidDataException("Native event indices are duplicated or out of sequence.");
            previousIndex = index;
            var kind = entry["kind"]?.GetValue<string>();
            if (kind == "input_release" && entry["scoreApplied"]?.GetValue<bool>() == false) continue;
            if (kind is not ("delivery" or "timeout") || entry["scoreApplied"]?.GetValue<bool>() != true
                || entry["team"]?.GetValue<int>() != 0 || RequiredLong(entry, "finalizedFrame") < RequiredLong(entry, "frame"))
                throw new InvalidDataException("Native score event is unknown, pending, or belongs to another team.");
            var before = entry["beforeScore"] as JsonObject ?? throw new InvalidDataException("Native score event has no before-score proof.");
            var after = entry["afterScore"] as JsonObject ?? throw new InvalidDataException("Native score event has no after-score proof.");
            var change = new long[totals.Length];
            for (var field = 0; field < totals.Length; field++)
            {
                long start = RequiredLong(before, totals[field]), end = RequiredLong(after, totals[field]);
                change[field] = RequiredLong(entry, deltas[field]);
                if (start != previous[field] || end - start != change[field])
                    throw new InvalidDataException("Native score transition chain/delta does not reconcile: " + totals[field]);
                previous[field] = end; sums[field] += change[field];
            }
            if (RequiredLong(before, "score") != RequiredLong(before, "baseScore") + RequiredLong(before, "tips") - RequiredLong(before, "deductions")
                || previous[0] != previous[1] + previous[2] - previous[3])
                throw new InvalidDataException("Native total score does not equal base score plus tips minus deductions.");
            if (kind == "delivery" && entry["nativeMatch"]?.GetValue<bool>() == true)
            {
                int recipeId = RequiredInt(entry, "recipeId"), orderId = RequiredInt(entry, "orderId");
                if (!CarnivalRecipes.ById.TryGetValue(recipeId, out var recipe) || orderId < 0 || !resolvedOrders.Add(orderId)
                    || RequiredInt(entry, "baseValue") != recipe.BaseScore || change[1] != recipe.BaseScore
                    || change[2] < 0 || change[3] != 0 || change[4] != 1 || change[0] != change[1] + change[2]
                    || RequiredInt(entry, "stationEntityId") <= 0 || string.IsNullOrWhiteSpace(entry["foodSignature"]?.GetValue<string>()))
                    throw new InvalidDataException("Matched delivery lacks a unique native order, audited recipe value, station, food or legal score transition.");
                var ingredients = entry["deliveredIngredientIds"] as JsonArray ?? throw new InvalidDataException("Delivered native ingredient evidence is missing.");
                if (!ingredients.Select(id => id?.GetValue<int>() ?? -1).Order().SequenceEqual(recipe.RequiredInputs.Select(i => i.Id).Order()))
                    throw new InvalidDataException("Matched delivery ingredient multiset differs from its native recipe.");
                double fraction = entry["remainingFraction"]?.GetValue<double>() ?? double.NaN;
                if (!double.IsFinite(fraction) || fraction < 0 || fraction > 1.00001)
                    throw new InvalidDataException("Matched order has no valid native remaining-time fraction.");
                matched++;
                deliveries.Add(new JsonObject
                {
                    ["eventIndex"] = index, ["orderId"] = orderId, ["recipeId"] = recipeId, ["recipe"] = recipe.Name,
                    ["baseScore"] = change[1], ["tip"] = change[2], ["score"] = change[0],
                    ["remainingFraction"] = fraction, ["stationEntityId"] = entry["stationEntityId"]!.DeepClone(),
                    ["gameplayFrame"] = entry["gameplayFrame"]?.DeepClone(), ["foodSignature"] = entry["foodSignature"]!.DeepClone()
                });
            }
            else if (kind == "delivery")
            {
                if (entry["nativeMatch"]?.GetValue<bool>() != false || change.Any(value => value != 0))
                    throw new InvalidDataException("Unmatched delivery illegally changes native score or successful delivery counts.");
                failed++;
            }
            else
            {
                int orderId = RequiredInt(entry, "orderId");
                if (orderId < 0 || !resolvedOrders.Add(orderId) || change[1] != 0 || change[2] != 0 || change[3] < 0 || change[4] != 0 || change[0] != -change[3])
                    throw new InvalidDataException("Expired order is duplicated or has an invalid native deduction transition.");
                expired++;
            }
        }
        if (matched == 0 || matched != RequiredInt(state, "delivered")) throw new InvalidDataException("Every final successful delivery must have native matched/scored recipe evidence.");
        for (var field = 0; field < totals.Length; field++)
            if (previous[field] != RequiredLong(state, totals[field]) || sums[field] != previous[field])
                throw new InvalidDataException("Native score event components do not reconcile with final totals: " + totals[field]);
        return new JsonObject
        {
            ["matchedDeliveries"] = matched, ["expiredOrders"] = expired, ["failedDeliveries"] = failed,
            ["baseScore"] = previous[1], ["tips"] = previous[2], ["deductions"] = previous[3], ["totalScore"] = previous[0],
            ["nativeRecipeAndOrderEvidenceValidated"] = true, ["beforeAfterScoreChainValidated"] = true,
            ["cookingAndPlatingEvidence"] = "Pinned native OnFoodDelivered match result, native recipe ID and delivered food signature; no controller-generated substitute match.",
            ["tipEvidence"] = "Native before/after tip deltas; the controller does not replace the native configured tip-boundary calculation.",
            ["deliveryHistory"] = deliveries
        };
    }

    private static long RequiredLong(JsonObject value, string field)
    {
        if (value[field] is JsonValue number && number.TryGetValue<long>(out var result)) return result;
        if (value[field] is JsonValue small && small.TryGetValue<int>(out var integer)) return integer;
        throw new InvalidDataException("Required native integer evidence is missing or invalid: " + field);
    }
    private static int RequiredInt(JsonObject value, string field)
    {
        long result = RequiredLong(value, field);
        if (result is < int.MinValue or > int.MaxValue) throw new InvalidDataException("Native integer evidence exceeds Int32: " + field);
        return (int)result;
    }

    private static IEnumerable<JsonNode> SuccessfulDeliveries(JsonObject state) => (state["gameEvents"]?.AsArray() ?? [])
        .OfType<JsonNode>().Where(e => e["kind"]?.GetValue<string>() == "delivery" && e["nativeMatch"]?.GetValue<bool>() == true
            && e["scoreApplied"]?.GetValue<bool>() == true && e["deliveryDelta"]?.GetValue<int>() == 1);

    public static void RequireNeutral(JsonObject response)
    {
        var inputs = response["inputs"]?.AsArray() ?? throw new InvalidDataException("Input readback unavailable.");
        if (inputs.Count != 4 || inputs.Any(input => input?["x"]?.GetValue<double>() != 0 || input?["y"]?.GetValue<double>() != 0
            || input?["pickup"]?.GetValue<bool>() != false || input?["use"]?.GetValue<bool>() != false || input?["dash"]?.GetValue<bool>() != false))
            throw new InvalidDataException("Movie must begin/end with four neutral input slots.");
    }

    public static JsonObject InitialProof(JsonObject response) => new()
    {
        ["session"] = Json.Object(response["session"]!.GetValue<string>()),
        ["gameplayFrame"] = response["state"]!["gameplayFrame"]?.DeepClone(),
        ["timer"] = response["state"]!["timer"]?.DeepClone(),
        ["levelStartPhysicsPhase"] = response["state"]!["levelStartPhysicsPhase"]?.DeepClone(),
        ["canonicalInitialStateSha256"] = NodeHash(response["state"]!),
        ["initialStateHashPolicy"] = "Full native state, sorted JSON object keys, original array order and all numeric/string values preserved; System.Text.Json UTF-8 + LF. Absolute clocks and transient entity IDs are not normalized. Raw state remains in the full trace.",
        ["captureFramerate"] = response["state"]!["captureFramerate"]?.DeepClone(),
        ["fixedDeltaTime"] = response["state"]!["fixedDeltaTime"]?.DeepClone(),
        ["alignStartPhysics"] = response["state"]!["alignStartPhysics"]?.DeepClone(),
        ["roundDuration"] = response["state"]!["roundDuration"]?.DeepClone(),
        ["entityRegistrationSha256"] = NodeHash(response["state"]!["entityRegistration"]!),
        ["entityRegistrationInterpretation"] = "Observed native network registration writes and explicitly labeled preexisting checkpoints; does not assert Unity object creation order.",
        ["localOwnershipEvidence"] = "SessionSetup kitchen_ready requires ValidateFourLocalUsers and four PlayerIDProvider.IsLocallyControlled checks"
    };

    public static async Task<JsonObject> CaptureFinalScreenshotAsync(string path,JsonObject finalResponse,string inputHash,Func<JsonObject,Task<JsonObject>> call)
    {
        ValidateFinished(finalResponse);RequireNeutral(finalResponse);
        path=Path.GetFullPath(path);var manifestPath=path+".manifest.json";
        if(File.Exists(path)||File.Exists(manifestPath))throw new IOException("Final screenshot evidence already exists: "+path);
        var request=Json.Request("screenshot");request["path"]=path;
        var captured=await call(request);Json.RequireOk(captured);RequireNeutral(captured);
        var expected=finalResponse["state"]!.AsObject();var observed=captured["state"]?.AsObject()??throw new InvalidDataException("Screenshot native state is absent.");
        if(!string.Equals(Path.GetFullPath(captured["message"]?.GetValue<string>()??""),path,StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Screenshot returned a different output path.");
        foreach(var field in new[]{"frame","fixedFrame","gameplayFrame","gameplayFixedFrame","score","baseScore","tips","deductions","delivered","timer","gameState","scene","serverRoundActive","clientRoundActive","gameEvents"})
            if(expected[field] is null||!JsonNode.DeepEquals(expected[field],observed[field]))throw new InvalidDataException("Screenshot does not bind the movie's final native state: "+field);
        if(captured["paused"]?.GetValue<bool>()!=true||captured["frame"] is null||!JsonNode.DeepEquals(captured["frame"],finalResponse["frame"]))
            throw new InvalidDataException("Screenshot advanced the paused native final frame.");
        ValidateObservers(observed);
        var bytes=File.ReadAllBytes(path);
        if(bytes.Length<33||!bytes.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10})||Encoding.ASCII.GetString(bytes,12,4)!="IHDR")
            throw new InvalidDataException("Final screenshot is not a PNG with an image header.");
        ValidatePngChunks(bytes);
        int width=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4));
        int height=System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        if(width<=0||height<=0||width!=RequiredInt(observed,"screenWidth")||height!=RequiredInt(observed,"screenHeight"))
            throw new InvalidDataException("PNG dimensions do not match the captured native screen.");
        var manifest=new JsonObject
        {
            ["path"]=path,["manifest"]=manifestPath,["imageSha256"]=FileHash(path),["bytes"]=bytes.Length,["width"]=width,["height"]=height,
            ["frame"]=observed["frame"]!.DeepClone(),["fixedFrame"]=observed["fixedFrame"]!.DeepClone(),["gameplayFrame"]=observed["gameplayFrame"]!.DeepClone(),
            ["score"]=observed["score"]!.DeepClone(),["timer"]=observed["timer"]!.DeepClone(),["delivered"]=observed["delivered"]!.DeepClone(),
            ["executedRequestsSha256"]=inputHash,["finalNativeStateSha256"]=NodeHash(expected),["screenshotNativeStateSha256"]=NodeHash(observed),
            ["nativeDeliveryEventsSha256"]=NodeHash(observed["gameEvents"]!),["outsideMovieHash"]=true,["pausedFrameUnchanged"]=true
        };
        File.WriteAllText(manifestPath,manifest.ToJsonString(Json.Options));return manifest;
    }

    internal static string NodeHash(JsonNode node)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sorted(node)!.ToJsonString(Json.Options)+"\n"))).ToLowerInvariant();

    private static void ValidatePngChunks(byte[] bytes)
    {
        int at=8;bool imageData=false,end=false;
        while(at<=bytes.Length-12)
        {
            uint length=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at,4));
            if(length>int.MaxValue||length>bytes.Length-at-12)throw new InvalidDataException("Truncated PNG chunk.");
            int size=(int)length;string kind=Encoding.ASCII.GetString(bytes,at+4,4);
            uint crc=uint.MaxValue;
            foreach(byte value in bytes.AsSpan(at+4,size+4))
            {
                crc^=value;for(int bit=0;bit<8;bit++)crc=(crc>>1)^((crc&1)!=0?0xedb88320u:0);
            }
            if(~crc!=System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(at+8+size,4)))throw new InvalidDataException("Invalid PNG chunk CRC.");
            if(kind=="IDAT"&&size>0)imageData=true;
            at+=size+12;
            if(kind=="IEND"){if(size!=0)throw new InvalidDataException("Invalid PNG end chunk.");end=true;break;}
        }
        if(!imageData||!end||at!=bytes.Length)throw new InvalidDataException("PNG image data/end evidence is incomplete.");
    }

    private static string FileHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static byte[] CanonicalBytes(JsonObject request) => Encoding.UTF8.GetBytes(Sorted(request)!.ToJsonString(Json.Options) + "\n");
    private static JsonNode? Sorted(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => KeyValuePair.Create(p.Key, Sorted(p.Value)))),
        JsonArray array => new JsonArray(array.Select(Sorted).ToArray()),
        _ => node?.DeepClone()
    };

}
