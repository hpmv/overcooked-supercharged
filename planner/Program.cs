using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "help" or "--help" or "-h") { Help(); return 0; }
            if (args[0] == "selftest") { await SelfTests.Run(); return 0; }
            var options = new Options(args.Skip(1).ToArray());
            if(args[0]=="optimize-seeds") return SeedOptimizer.Command(options);
            if(args[0]=="route-test") {await RouteTests.Run(Json.Object(File.ReadAllText(options.Required("file"))));return 0;}
            if (args[0] == "compare") return Compare(options);
            if (args[0] == "schedule") return Schedule(options);
            if (args[0] is "map" or "path" or "navigation-test") return MapOrPath(args[0], options);
            if (options.Has("out"))
            {
                foreach (var source in new[] { "file", "script" }.Where(options.Has))
                    if (string.Equals(Path.GetFullPath(options.Required(source)), Path.GetFullPath(options.Required("out")), StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("Output trace must not overwrite its input file.");
            }
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(options.Int("timeout", 600)));
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
            await using var client = new TasClient();
            await client.ConnectAsync(options.Get("host", "127.0.0.1"), options.Int("port", 17634), cancellation.Token);
            using var trace = options.Has("out") ? new TraceWriter(options.Required("out"), args[0]) : null;
            async Task<JsonObject> Call(JsonObject request)
            {
                var response = await client.CallAsync(request, cancellation.Token);
                trace?.Call(request, response);
                Json.RequireOk(response);
                return response;
            }
            JsonObject? result;
            switch (args[0])
            {
                case "record":
                    _ = options.Required("out");
                    result = await RunScript(options.Required("script"), Call, false, options);
                    break;
                case "replay":
                    result = await RunScript(options.Required("file"), Call, !options.Has("no-compare"), options);
                    break;
                case "diagnose":
                    result = await Diagnose(options, Call, trace);
                    break;
                case "verify-run":
                    result = await Verification.RunAsync(options,Call,trace);
                    break;
                case "bot":
                    if(options.Has("restart"))
                    {
                        var botStart=Json.Request("restart");botStart["seed"]=options.Int("seed",0);botStart["isolateRecipeRandom"]=true;
                        await Call(botStart);
                    }
                    result=await new CarnivalPlanner(Call,trace).RunAsync(new CarnivalPlannerOptions(
                        Seed:options.Int("seed",0), TargetScore:options.Int("target-score",5000),
                        Lookahead:options.Int("lookahead",8), ServiceBatch:options.Int("service-batch",3),
                        StopAfterDeliveries:options.Int("stop-after-deliveries",0),
                        MaximumFrames:options.Int("max-frames",16320),
                        DirectVesselThrows:!options.Has("no-direct-throws"), UseDash:options.Has("dash")||options.Has("short-dash"),
                        CooperativeSauces:options.Has("cooperative-sauces"), UseShortDash:options.Has("short-dash"),
                        BufferChoppedBuns:options.Has("bun-buffer"), EarlyOnionHeat:options.Has("early-onion"), PantryChopping:options.Has("pantry-chop"),
                        ParallelOnionHeat:options.Has("parallel-onion"),
                        StagedResourceRelease:options.Has("staged-release"),
                        ConditionalFarPotThrows:options.Has("conditional-far-pot"), BakeryLookahead:options.Int("bakery-lookahead",0),
                        PreServiceStock:options.Has("pre-service-stock"), CannonBoundaryPreemption:options.Has("preempt-cannons"),
                        ServiceSideHead:options.Has("service-side-head"), SharedPantryChopping:options.Has("share-pantry-chop"),
                        SausageBufferSize:options.Int("sausage-buffer",0), WaitForImminentHead:options.Has("wait-ready-head"),
                        ReleaseFiringChefOnLaunch:options.Has("release-firer"), NearSauceStaging:options.Has("near-sauce-stage"),
                        ShortDashVessels:options.Has("short-dash-vessels"), ServeBeforeSafeHeat:options.Has("serve-before-safe-heat"),
                        DirectPreparedFlavorThrows:options.Has("direct-flavor-throws"), NearReadyPotHarvest:options.Has("near-ready-pot"),
                        WasherSidePlating:options.Has("washer-side-plating"),DirectCleanPassAssembly:options.Has("direct-clean-pass"),
                        NearReadyFryerHarvest:options.Has("near-ready-fryer"),NearestCentralTaskPreference:options.Has("nearest-central-task"),
                        ContinuousWaypoints:options.Has("continuous-waypoints"),WaitForFinalSauceHead:options.Has("wait-final-sauce"),
                        StationaryTargetTransfers:options.Has("stationary-target-transfers"),
                        PredictiveBakeryReturn:options.Has("predictive-bakery-return"),PlatedOnionFinish:options.Has("plated-onion-finish"),
                        FifoEmptyBowls:options.Has("fifo-empty-bowls"),PlatedHotdogBase:options.Has("plated-hotdog-base")));
                    break;
                case "route":
                case "plan":
                    if(options.Has("restart"))
                    {
                        var start=Json.Request("restart");start["seed"]=options.Int("seed",0);
                        start["isolateRecipeRandom"]=options.Has("isolate-recipe-random");
                        await Call(start);
                    }
                    var route=Json.Object(await File.ReadAllTextAsync(options.Required("file"), cancellation.Token));
                    result = args[0]=="plan"?await new ConcurrentPlanRunner(Call,trace).RunAsync(route):await new RouteRunner(Call, trace).RunAsync(route);
                    break;
                default:
                    var request = options.Has("file") ? Json.Object(await File.ReadAllTextAsync(options.Required("file"), cancellation.Token))
                        : options.Has("json") ? Json.Object(options.Required("json"))
                        : options.Positionals.Count > 0 && args[0] == "call" ? Json.Object(options.Positionals[0])
                        : Json.Request(args[0] == "state" ? "inspect" : args[0]);
                    request=DirectRequest(request,options);
                    result = await Call(request);
                    break;
            }
            Console.WriteLine(result?.ToJsonString(options.Has("compact") ? Json.Options : Json.Pretty));
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(new JsonObject { ["ok"] = false, ["error"] = error.Message, ["type"] = error.GetType().Name }.ToJsonString());
            return 1;
        }
    }

    public static JsonObject DirectRequest(JsonObject payload,Options options)
    {
        var request=payload.DeepClone().AsObject();
        if(options.Has("steps"))request["steps"]=options.Int("steps",1);
        if(options.Has("seed"))request["seed"]=options.Int("seed",0);
        if(options.Has("isolate-recipe-random"))
        {
            if(!bool.TryParse(options.Get("isolate-recipe-random","true"),out bool isolate))throw new ArgumentException("--isolate-recipe-random accepts true or false.");
            request["isolateRecipeRandom"]=isolate;
        }
        if(options.Has("path"))request["path"]=Path.GetFullPath(options.Required("path"));
        if(options.Has("inputs"))request["inputs"]=JsonNode.Parse(options.Required("inputs"))?.AsArray();
        if(request["command"]?.ToString() is "load" or "restart")
        {
            request["seed"]??=0;request["isolateRecipeRandom"]??=false;
        }
        if(request["command"]?.ToString()=="step")
        {
            request["steps"]??=1;request["inputs"]??=Inputs.AllNeutral();
        }
        return request;
    }

    private static async Task<JsonObject?> RunScript(string path, Func<JsonObject, Task<JsonObject>> call, bool compare, Options options)
    {
        JsonObject? result = null;
        var ignored = Ignored(options);
        var index = 0;
        foreach (var entry in Traces.ReadCalls(path))
        {
            var request = Traces.ExtractRequest(entry);
            result = await call(request);
            if (compare)
            {
                var expected = entry["response"] as JsonObject ?? throw new InvalidDataException($"Replay call {index} has no recorded response; use record first or --no-compare for an input-only script.");
                if (expected["state"] is null || result["state"] is null) throw new InvalidDataException($"Replay call {index} has no comparable state.");
                var difference = StateComparer.First(expected["state"], result["state"], ignored);
                if (difference is not null) throw new InvalidDataException($"Replay diverged at call {index}: {JsonSerializer.Serialize(difference)}");
            }
            index++;
        }
        if (index == 0) throw new InvalidDataException("Script contains no calls.");
        return result;
    }

    private static async Task<JsonObject> Diagnose(Options options, Func<JsonObject, Task<JsonObject>> call, TraceWriter? trace)
    {
        var calls = Traces.ReadCalls(options.Required("script")).Select(Traces.ExtractRequest).ToArray();
        if (calls.Length == 0 || calls[0]["command"]?.ToString() is not ("restart" or "load" or "setup"))
            throw new ArgumentException("Diagnostic script must start with restart, load, or setup and include readiness steps.");
        var runs = options.Int("runs", 20);
        if (runs < 2) throw new ArgumentException("Diagnose needs at least two runs.");
        var summary = new JsonArray();
        var baselinePath = Path.Combine(Path.GetTempPath(), "overcooked-tas-baseline-" + Guid.NewGuid().ToString("N") + ".jsonl.gz");
        try
        {
            for (var run = 0; run < runs; run++)
            {
                using var baselineWriter = run == 0 ? new TraceWriter(baselinePath, "baseline") : null;
                using var baselineReader = run == 0 ? null : Traces.ReadCalls(baselinePath).GetEnumerator();
                Difference? divergence = null;
                var divergentCall = -1;
                for (var i = 0; i < calls.Length; i++)
                {
                    var response = await call(calls[i].DeepClone().AsObject());
                    if (run == 0) baselineWriter!.Call(calls[i], response);
                    else
                    {
                        if (!baselineReader!.MoveNext()) throw new InvalidDataException("Diagnostic baseline is truncated.");
                        if (divergence is null)
                        {
                            divergence = StateComparer.First(baselineReader.Current["response"]?["state"], response["state"], Ignored(options));
                            if (divergence is not null) divergentCall = i;
                        }
                    }
                }
                var item = new JsonObject { ["run"] = run, ["matchedBaseline"] = divergence is null, ["firstDivergentCall"] = divergentCall, ["difference"] = JsonSerializer.SerializeToNode(divergence) };
                summary.Add(item);
                trace?.Event("diagnosticRun", item);
                Console.Error.WriteLine(item.ToJsonString());
            }
        }
        finally { if (File.Exists(baselinePath)) File.Delete(baselinePath); }
        return new JsonObject
        {
            ["ok"] = true, ["runs"] = summary,
            ["classification"] = summary.OfType<JsonObject>().All(i => i["matchedBaseline"]!.GetValue<bool>()) ? "identical-observed-gameplay-state" : "divergent-observed-gameplay-state",
            ["freshProcessStartsVerified"] = 0, ["ignoredPaths"] = JsonSerializer.SerializeToNode(Ignored(options))
        };
    }

    private static int Compare(Options options)
    {
        using var expected = Traces.ReadCalls(options.Required("expected")).GetEnumerator();
        using var actual = Traces.ReadCalls(options.Required("actual")).GetEnumerator();
        var count = 0;
        while (true)
        {
            var hasExpected = expected.MoveNext();
            var hasActual = actual.MoveNext();
            if (!hasExpected && !hasActual) break;
            if (hasExpected && expected.Current["response"]?["state"] is null || hasActual && actual.Current["response"]?["state"] is null)
                throw new InvalidDataException($"Call {count} has no recorded state; compare requires full recordings.");
            var difference = hasExpected != hasActual ? new Difference("$.calls." + count, hasExpected ? "call" : "end-of-file", hasActual ? "call" : "end-of-file")
                : StateComparer.First(expected.Current["request"], actual.Current["request"], null, "$.request")
                  ?? StateComparer.First(expected.Current["response"]?["state"], actual.Current["response"]?["state"], Ignored(options));
            if (difference is not null)
            {
                Console.WriteLine(JsonSerializer.Serialize(new { ok = false, firstDivergentCall = count, difference }, Json.Pretty));
                return 2;
            }
            count++;
        }
        if (count == 0) throw new InvalidDataException("Cannot compare traces with no calls.");
        Console.WriteLine(JsonSerializer.Serialize(new { ok = true, calls = count, ignoredPaths = Ignored(options) }, Json.Pretty));
        return 0;
    }

    private static int Schedule(Options options)
    {
        var tasks = JsonSerializer.Deserialize<KitchenTask[]>(File.ReadAllText(options.Required("file")), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Expected task array.");
        Console.WriteLine(JsonSerializer.Serialize(KitchenScheduler.Schedule(tasks), Json.Pretty));
        return 0;
    }

    private static int MapOrPath(string command,Options options)
    {
        var snapshot=Json.Object(File.ReadAllText(options.Required("file")));
        var model=KitchenModel.Build(snapshot);
        if(command=="navigation-test") {NavigationTests.Run(model,snapshot);return 0;}
        object result=model;
        if(command=="path")
        {
            var state=KitchenModel.SnapshotState(snapshot);
            int player=options.Int("player",0);
            var chefs=(state["chefs"] as JsonArray)?.OfType<JsonObject>().ToArray()??[];
            var chef=chefs.SingleOrDefault(c=>c["playerId"]?.GetValue<int>()==player)??throw new ArgumentException("Chef player not present in snapshot.");
            var start=KitchenModel.Position(chef["position"]);
            var obstacles=options.Has("ignore-chefs")?[]:chefs.Where(c=>c["playerId"]?.GetValue<int>()!=player).Select(c=>
            {
                var p=KitchenModel.Position(c["position"]);double r=model.ChefRadius;
                return new KitchenObstacle("chef:"+c["playerId"],c["entityId"]!.GetValue<int>(),new Rect2(p.X-r,p.X+r,p.Z-r,p.Z+r),"Observed other chef capsule",true,CircleRadius:r);
            }).ToArray();
            if(options.Has("station"))result=Navigation.ToStation(model,start,model.Resolve(options.Required("station")),obstacles);
            else
            {
                var pair=options.Required("target").Split(',');
                if(pair.Length!=2)throw new ArgumentException("--target must be X,Z");
                var target=new Point2(double.Parse(pair[0],System.Globalization.CultureInfo.InvariantCulture),double.Parse(pair[1],System.Globalization.CultureInfo.InvariantCulture));
                result=Navigation.FindPath(model,start,target,.15,obstacles);
            }
        }
        string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        if(options.Has("out"))
        {
            string output=Path.GetFullPath(options.Required("out"));
            if(string.Equals(output,Path.GetFullPath(options.Required("file")),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Map output must not overwrite source snapshot.");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);File.WriteAllText(output,json);
        }
        Console.WriteLine(json);
        return result is NavigationPath path&&!path.Success?2:0;
    }

    private static HashSet<string> Ignored(Options options) => options.Get("ignore", "").Split(',', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);

    private static void Help() => Console.WriteLine("""
Overcooked TAS controller, protocol 1
  inspect | state | setup | load | restart | pause | resume
  step --steps N [--inputs '[{...},...]']
  screenshot --path PATH
  call --json '{"command":"step","steps":1,"inputs":[...]}'
  call --file request.json
  record --script calls.jsonl --out recording.jsonl
  replay --file recording.jsonl --out replay.jsonl [--no-compare]
  diagnose --script probe.jsonl --runs 20 --out probes.jsonl
  compare --expected a.jsonl --actual b.jsonl
  route --file route.json --out route-trace.jsonl
  plan --file concurrent-plan.json --restart --seed 0 --isolate-recipe-random --out plan-trace.jsonl.gz
  bot --restart --seed 0 --out bot-trace.jsonl.gz [--dash] [--service-batch 3] [--lookahead 8]
      [--stop-after-deliveries N] [--target-score 5000] [--max-frames 16320] [--no-direct-throws]
      [--cooperative-sauces] [--short-dash] [--bun-buffer] [--early-onion] [--pantry-chop] [--parallel-onion]
      [--staged-release] [--conditional-far-pot] [--bakery-lookahead 12] [--preempt-cannons]
      [--pre-service-stock] [--service-side-head] [--wait-ready-head] [--release-firer] [--near-sauce-stage] [--short-dash-vessels]
      [--near-ready-pot] [--washer-side-plating] [--direct-clean-pass] [--near-ready-fryer] [--nearest-central-task] [--continuous-waypoints] [--wait-final-sauce]
      [--stationary-target-transfers]
      [--plated-onion-finish] [--fifo-empty-bowls] [--plated-hotdog-base]
      [--predictive-bakery-return] (requires direct vessel throws, pantry chopping and direct flavor throws)
  optimize-seeds --file native-seed-previews.jsonl --out seed-candidates.json
  verify-run --file inputs.jsonl.gz --manifest inputs.jsonl.gz.manifest.json --validation-mode probe --prefix artifacts/probe
  schedule --file tasks.json
  map --file snapshot.json [--out kitchen-map.json]
  path --file snapshot.json --player 0 --target 22,-18 [--ignore-chefs]
  path --file snapshot.json --player 0 --station STABLE_KEY_OR_INGREDIENT
  navigation-test --file snapshot.json
  route-test --file snapshot.json
  selftest
Shared: --host 127.0.0.1 --port 17634 --timeout 600 --seed N --compact
Comparison: --ignore '$.frame,$.fixedFrame' (explicit exact JSON paths only)
Indices: players 0..3. Movement input Y defaults to negative world Z.
All calls can be recorded with --out. Ctrl+C cancels and closes the socket.
""");
}

public sealed class Options
{
    private readonly Dictionary<string, string> values = new(StringComparer.Ordinal);
    public List<string> Positionals { get; } = [];
    public Options(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal)) { Positionals.Add(args[i]); continue; }
            var key = args[i][2..];
            values[key] = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[++i] : "true";
        }
    }
    public bool Has(string key) => values.ContainsKey(key);
    public string Get(string key, string fallback) => values.GetValueOrDefault(key, fallback);
    public string Required(string key) => Has(key) && Get(key, "") != "true" ? Get(key, "") : throw new ArgumentException($"Missing --{key}.");
    public int Int(string key, int fallback) => Has(key) ? int.Parse(Required(key), System.Globalization.CultureInfo.InvariantCulture) : fallback;
}
