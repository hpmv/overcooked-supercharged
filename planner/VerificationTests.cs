using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class VerificationTests
{
    public static async Task<int> RunAsync()
    {
        int checks = 0;
        void Check(bool value, string label) { if (!value) throw new Exception("FAIL: " + label); checks++; }
        void Reject(Action action, string label) { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception("FAIL: " + label); }
        var request = Json.Object("""{"version":1,"command":"preview","seed":-10,"count":2}""");
        var response = Preview(request);
        Verification.ValidatePreview(request, response); checks++;
        foreach (var field in new[] { "ambientRngRestored", "isolatedRngRestored", "isolatedRegistryRestored", "observationsRestored", "liveInstanceUnchanged", "frameUnchanged" })
        {
            var bad = response.DeepClone().AsObject(); bad["preview"]![field] = false;
            Reject(() => Verification.ValidatePreview(request, bad), "false restoration flag " + field);
            bad["preview"]!.AsObject().Remove(field);
            Reject(() => Verification.ValidatePreview(request, bad), "missing restoration flag " + field);
        }
        foreach (var field in new[] { "seed", "count" })
        {
            var bad = request.DeepClone().AsObject(); bad.Remove(field);
            Reject(() => Verification.ValidatePreviewRequest(bad), "implicit preview " + field + " rejected");
        }
        foreach (int count in new[] { 0, 1025 })
        {
            var bad = request.DeepClone().AsObject(); bad["count"] = count;
            Reject(() => Verification.ValidatePreviewRequest(bad), "native count bound " + count);
        }
        var extra = request.DeepClone().AsObject(); extra["inputs"] = Inputs.AllNeutral();
        Reject(() => Verification.ValidatePreviewRequest(extra), "preview rejects unrelated input payload");
        var nonIntegral = request.DeepClone().AsObject(); nonIntegral["seed"] = 1.5;
        Reject(() => Verification.ValidatePreviewRequest(nonIntegral), "preview rejects non-integral seed");
        var mismatch = response.DeepClone().AsObject(); mismatch["preview"]!["seed"] = 42;
        Reject(() => Verification.ValidatePreview(request, mismatch), "preview binds response seed");
        mismatch = response.DeepClone().AsObject(); mismatch["preview"]!["recipes"]![1]!["baseValue"] = 10000;
        Reject(() => Verification.ValidatePreview(request, mismatch), "preview binds native recipe base values");
        mismatch = response.DeepClone().AsObject(); mismatch["preview"]!["ambientAfter"] = "changed";
        Reject(() => Verification.ValidatePreview(request, mismatch), "true restoration flag cannot hide contradictory details");

        var finished = Finished();
        Verification.ValidateFinished(finished);
        var proof = Verification.ValidateScoreEvidence(finished["state"]!.AsObject());
        Check(proof["matchedDeliveries"]!.GetValue<int>() == 40 && proof["totalScore"]!.GetValue<long>() >= 5000, "all native deliveries reconcile into score breakdown");
        void RejectScore(Action<JsonObject> mutate, string label)
        {
            var bad = finished.DeepClone().AsObject(); mutate(bad["state"]!.AsObject());
            Reject(() => Verification.ValidateFinished(bad), label);
        }
        RejectScore(s => s["gameEvents"]![0]!["nativeMatch"] = false, "unmatched food cannot explain positive score");
        RejectScore(s => s["gameEvents"]![1]!["orderId"] = 1, "native order cannot be scored twice");
        RejectScore(s => s["gameEvents"]![1]!["index"] = 0L, "native event cannot be counted twice");
        RejectScore(s => s["gameEvents"]![0]!["beforeScore"] = null, "missing score origin rejected");
        RejectScore(s => s["gameEvents"]![1]!["beforeScore"]!["score"] = 0, "score chain must join previous native total");
        RejectScore(s => s["gameEvents"]![0]!["scoreApplied"] = false, "unfinalized native delivery rejected");
        RejectScore(s => s["gameEvents"]![0]!["baseValue"] = 999, "native recipe base value must match installed recipe");
        RejectScore(s => s["gameEvents"]![0]!["deliveredIngredientIds"]![0] = CarnivalRecipes.Bun.Id, "delivered ingredient multiset must match recipe");
        RejectScore(s => s["gameEvents"]![0]!["remainingFraction"] = -1.0, "successful order needs remaining-time evidence");
        RejectScore(s => s["gameEvents"]![0]!["tipDelta"] = 1000, "tip delta must equal native score transition");
        RejectScore(s => s["baseScore"] = 0, "final native component totals reconcile independently");
        RejectScore(s => s["timer"] = .01, "high score before round completion is insufficient");

        string directory = Path.Combine(Path.GetTempPath(), "oc2-verification-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var startup = Json.Object("""{"version":1,"command":"load","seed":-10,"isolateRecipeRandom":true}""");
            string movie = Path.Combine(directory, "movie.jsonl.gz"), manifest = movie + ".manifest.json";
            void WriteMovie(JsonObject? recorded)
            {
                using (var gzip = new GZipStream(File.Create(movie), CompressionLevel.Fastest))
                using (var writer = new StreamWriter(gzip))
                {
                    writer.WriteLine(startup.ToJsonString());
                    writer.WriteLine(recorded is null ? request.ToJsonString() : new JsonObject { ["request"] = request.DeepClone(), ["response"] = recorded.DeepClone() }.ToJsonString());
                    writer.WriteLine(Inputs.Step(1).ToJsonString());
                }
                File.WriteAllText(manifest, new JsonObject
                {
                    ["outputFileSha256"] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(movie))).ToLowerInvariant(),
                    ["starts"] = new JsonArray(new JsonObject { ["request"] = startup.DeepClone() })
                }.ToJsonString());
            }
            WriteMovie(response);
            var audit = Verification.AuditMovie(movie, manifest);
            Check(audit["previewRequests"]!.GetValue<int>() == 1 && audit["recordedPreviewResponsesValidated"]!.GetValue<int>() == 1, "recorded preview response is audited");
            var rejectedRecorded = response.DeepClone().AsObject(); rejectedRecorded["preview"]!["isolatedRegistryRestored"] = false;
            WriteMovie(rejectedRecorded);
            Reject(() => Verification.AuditMovie(movie, manifest), "recorded mutation rejected before playback");
            WriteMovie(null);
            audit = Verification.AuditMovie(movie, manifest);
            Check(audit["previewResponsesDeferredToExecution"]!.GetValue<int>() == 1 && audit["recordedPreviewResponsesValidated"]!.GetValue<int>() == 0, "input-only audit reports deferred actual-response checks honestly");
            int calls = 0, steps = 0;
            Task<JsonObject> FakeCall(JsonObject command)
            {
                calls++;
                if (command["command"]?.ToString() == "step") steps++;
                return Task.FromResult(command["command"]?.ToString() switch
                {
                    "load" => Initial(),
                    "preview" => rejectedRecorded.DeepClone().AsObject(),
                    _ => new JsonObject { ["version"] = 1, ["ok"] = true }
                });
            }
            try
            {
                await Verification.RunAsync(new Options(["--file", movie, "--manifest", manifest, "--prefix", Path.Combine(directory, "execution"), "--validation-mode", "probe"]), FakeCall, null);
                throw new Exception("Actual preview mutation accepted");
            }
            catch (InvalidDataException error) when (error.Message.Contains("isolatedRegistryRestored", StringComparison.Ordinal)) { checks++; }
            Check(calls == 3 && steps == 0, "actual failed restoration stops before any next movie input");
        }
        finally { foreach (var file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        Console.WriteLine($"PASS: {checks} movie-preview/native-score verification assertions.");
        return checks;
    }

    private static JsonObject Preview(JsonObject request) => new()
    {
        ["version"] = 1, ["ok"] = true, ["preview"] = new JsonObject
        {
            ["seed"] = request["seed"]!.DeepClone(), ["count"] = 2, ["scene"] = "s_Day_3_4", ["generator"] = "RoundData", ["freshInstance"] = true,
            ["ambientRngRestored"] = true, ["isolatedRngRestored"] = true, ["isolatedRegistryRestored"] = true, ["observationsRestored"] = true,
            ["liveInstanceUnchanged"] = true, ["frameUnchanged"] = true, ["ambientBefore"] = "rng", ["ambientAfter"] = "rng",
            ["recipes"] = new JsonArray(new JsonObject { ["index"] = 0, ["recipeId"] = 296560, ["baseValue"] = 40 }, new JsonObject { ["index"] = 1, ["recipeId"] = 158500, ["baseValue"] = 60 })
        }
    };
    internal static JsonObject Initial()
    {
        var result = Json.Object("""{"version":1,"ok":true,"paused":true,"state":{"scene":"s_Day_3_4","gameplayFrame":0,"levelReady":true,"gameState":"InLevel","timer":269.9833333333333,"score":0,"delivered":0,"gameEventsInstalled":true,"gameEventsDropped":0,"gameEventsError":"","warnings":[],"entities":[],"chefs":[]}}""");
        result["session"] = """{"stage":"kitchen_ready","busy":false,"dlc":8,"variantPlayers":4,"serverUsers":4,"clientUsers":4,"variantScene":"s_Day_3_4"}""";
        result["inputs"] = Inputs.AllNeutral();
        var state=result["state"]!.AsObject();
        foreach(var (field,value) in new[]{("frame",600L),("fixedFrame",500L),("levelFrameZero",600L),("levelFixedFrameZero",500L),("introFrameZero",367L),("startAlignmentReleaseFrame",367L),("gameplayFixedFrame",0L)})state[field]=value;
        state["captureFramerate"]=60;state["fixedDeltaTime"]=.02;state["unityDeltaTime"]=1.0/60;state["clientDeltaTime"]=1.0/60;
        state["logicalTime"]=10.0;state["clientTime"]=10.0;state["levelClientTimeZero"]=10.0;state["unityMaximumDeltaTime"]=1.0/3;
        state["alignStartPhysics"]=true;state["levelStartPhysicsPhase"]=5;state["framesSinceNoPhysics"]=5;state["physicsStepsThisFrame"]=1;state["startAlignmentWaitFrames"]=2;
        state["serverRoundActive"]=true;state["clientRoundActive"]=true;state["timerSuppressed"]=false;
        state["instrumentation"]=InstrumentationFixture();
        state["roundDuration"]=new JsonObject{["available"]=true,["loadedRoundDataAvailable"]=true,["timerAvailable"]=true,["valuesAgree"]=true,["seconds"]=270.0,["loadedRoundDataSeconds"]=270.0,["timerLimitSeconds"]=270.0,["source"]="synthetic test source",["scene"]="s_Day_3_4",["levelConfigName"]="synthetic",["roundDataType"]="RoundData",["timerType"]="ServerRoundTimer",["error"]=""};
        JsonObject Checkpoint(string kind)=>new(){["kind"]=kind,["available"]=true,["afterSequence"]=0L,["frame"]=0L,["fixedFrame"]=0L,["roundEpoch"]=1,["entities"]=new JsonArray()};
        state["entityRegistration"]=new JsonObject{["installed"]=true,["addHookInstalled"]=true,["removeHookInstalled"]=true,["clearHookInstalled"]=true,["errorCount"]=0L,["error"]="",["status"]="observing",["roundDropped"]=0L,["owner"]="synthetic",["nativeModuleVersionId"]="11111111-1111-1111-1111-111111111111",["initialPreexisting"]=Checkpoint("initial-preexisting"),["roundBegin"]=Checkpoint("round-begin-preexisting"),["roundEpoch"]=1,["returnedHistoryLimited"]=false,["returnedAfterSequence"]=0L,["lastSequence"]=0L,["retainedCount"]=0,["events"]=new JsonArray()};
        result["frame"]=600L;result["physicsFrame"]=500L;
        for (int player = 0; player < 4; player++) result["state"]!["chefs"]!.AsArray().Add(new JsonObject { ["playerId"] = player, ["heldEntityId"] = 0, ["aimingThrow"] = false });
        return Json.Object(result.ToJsonString());
    }
    internal static JsonObject Finished()
    {
        var response = Initial(); var state = response["state"]!.AsObject();
        state["gameplayFrame"] = 16200L; state["timer"] = 0; state["serverRoundActive"] = false; state["clientRoundActive"] = false; state["gameState"] = "RunLevelOutro";
        var events = new JsonArray(); state["gameEvents"] = events;
        int score = 0, baseScore = 0, tips = 0;
        JsonObject Score(int delivered) => new() { ["score"] = score, ["baseScore"] = baseScore, ["tips"] = tips, ["deductions"] = 0, ["delivered"] = delivered };
        for (int index = 0; index < 40; index++)
        {
            var before = Score(index); int tip = 8 * Math.Max(1, Math.Min(4, index));
            baseScore += 100; tips += tip; score += 100 + tip;
            events.Add(new JsonObject
            {
                ["index"] = (long)index, ["frame"] = (long)index * 300, ["finalizedFrame"] = (long)index * 300, ["gameplayFrame"] = (long)index * 300,
                ["kind"] = "delivery", ["team"] = 0, ["nativeMatch"] = true, ["scoreApplied"] = true, ["orderId"] = index + 1,
                ["recipeId"] = 130976, ["baseValue"] = 100, ["stationEntityId"] = 73, ["remainingFraction"] = .9,
                ["foodSignature"] = "cook:17160:Cooked(mix:Mixed(18448,16620,129618))", ["deliveredIngredientIds"] = new JsonArray(18448, 16620, 129618),
                ["beforeScore"] = before, ["afterScore"] = Score(index + 1), ["scoreDelta"] = 100 + tip, ["baseScoreDelta"] = 100,
                ["tipDelta"] = tip, ["deductionDelta"] = 0, ["deliveryDelta"] = 1
            });
        }
        state["score"] = score; state["baseScore"] = baseScore; state["tips"] = tips; state["deductions"] = 0; state["delivered"] = 40;
        return Json.Object(response.ToJsonString());
    }

    private static JsonObject InstrumentationFixture()
    {
        var manifest=new JsonObject{["version"]=1};
        foreach(string field in new[]{"identifier","unityVersion","runtimeArchitecture","frameGate","clockPolicy","orderPolicy","inputPolicy","focusPolicy","savePolicy","hookEvidencePolicy"})manifest[field]="synthetic-test-only";
        foreach(string field in new[]{"pluginSha256","gameAssemblySha256","executableSha256"})manifest[field]=new string('a',64);
        foreach(string field in new[]{"pluginModuleVersionId","gameModuleVersionId"})manifest[field]="11111111-1111-1111-1111-111111111111";
        foreach(string field in new[]{"inputHooksInstalled","clockHooksInstalled","startAlignmentHookInstalled","saveHooksInstalled","gameEventHooksInstalled"})manifest[field]=true;
        manifest["hooks"]=new JsonArray(new JsonObject{["original"]="fixture-original",["kind"]="prefix",["handler"]="fixture-handler",["owner"]="fixture-owner"});
        manifest["loadedPlugins"]=new JsonArray(new JsonObject{["identifier"]="local.oc2tas.newbot",["version"]="synthetic-test",["assemblySha256"]=new string('a',64)});
        return new(){["manifestSha256"]=new string('a',64),["error"]="",["manifest"]=manifest,["inputActive"]=true,["logicalClockActive"]=true,["isolateRecipeRandom"]=true,["alignStartPhysics"]=true,["nativePhysicsAutoSimulation"]=true,["seed"]=-10};
    }
}
