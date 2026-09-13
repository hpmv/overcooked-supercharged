using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

int I(JsonNode? n) => n is null ? 0 : int.Parse(n.ToString());
double N(JsonNode? n) => n is null ? double.NaN : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
void Need(bool b, string why) { if (!b) throw new InvalidDataException(why); }
string Hash(string path) { using var f = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
JsonObject? last = null;
int frame = -1, calls = 0, steps = 0;
var records = new List<Life>();
var eventCounts = new Dictionary<string, int>();
var commands = new Dictionary<string, int>();
JsonObject report = new();
JsonObject E(int id) => last!["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == id);
JsonObject? MaybeE(int id) => last!["entities"]!.AsArray().OfType<JsonObject>().SingleOrDefault(e => I(e["id"]) == id);
int Attached(int id) => I(E(id)["attachedEntityId"]);
int Holder(int id) => last!["chefs"]!.AsArray().OfType<JsonObject>().Where(c => I(c["heldEntityId"]) == id).Select(c => I(c["playerId"])).DefaultIfEmpty(-1).Single();
int Held(int player) => I(last!["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == player)["heldEntityId"]);
FoodObservation Food(int id) => CarnivalRecipes.ClassifyEntity(E(id)).Food;
bool Empty(int id) => Food(id).IngredientIds.Length == 0;
bool Recipe(int id, int recipe, bool plated) => CarnivalRecipes.MatchRecipe(E(id), recipe, plated).ReadyToDeliver;
bool Bun(int id) => Food(id).IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Bun.Id }) && Food(id).Preparation == FoodPreparation.Chopped;
bool Sausage(int id, bool cooked) => !Food(id).IsRuined && Food(id).IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }) &&
    (!cooked || Food(id).DescendantsAndSelf().Any(f => f.Kind == FoodNodeKind.Cooked && f.Preparation == FoodPreparation.Cooked && f.CookingStepId == CarnivalRecipes.PotCookingStepId));
void Identity(Life p)
{
    foreach (var pair in p.Ordinals)
    {
        if (p.Kind == "washer" && pair.Key == p.Food && MaybeE(p.Food) is null) continue;
        Need(I(E(pair.Key)["observedOrdinal"]) == pair.Value, $"{p.Kind}/{p.Index}: native identity {pair.Key} changed at {frame}.");
    }
}
void Observe(Life p)
{
    Identity(p); p.Samples++; p.Last = frame;
    if (p.Kind == "pot")
    {
        Need(frame - p.Start <= 720, "Near-pot observed interval exceeds its fixed deadline.");
        if (!p.Released.Contains(p.Vessel))
        {
            Need(Attached(p.Home) == p.Vessel && Holder(p.Vessel) == -1, "Near-pot moved from its original native stove before consumer release.");
            double progress = N(E(p.Vessel)["cookingProgress"]);
            Need(double.IsFinite(progress) && progress >= 0 && progress < 23, "Near-pot native cooking guard violated.");
            p.MaxProgress = Math.Max(p.MaxProgress, progress);
            if (!Empty(p.Vessel)) Need(Sausage(p.Vessel, false), "Near-pot source recipe changed.");
            if (Sausage(p.Vessel, true) && p.Cooked < 0) p.Cooked = frame;
            if (Empty(p.Vessel) && p.Consumed < 0)
            {
                Need(p.Picked >= p.Start && p.Cooked >= p.Start && p.Cooked < frame && Holder(p.Food) == p.Player && Recipe(p.Food, 296560, false) && progress == 0,
                    "Near-pot consumption lacks previously observed native Cooked sausage, exact held bun and empty reset pot.");
                p.Consumed = frame;
            }
        }
        if (Holder(p.Food) == p.Player && p.Picked < 0)
        {
            Need(Bun(p.Food) && Attached(p.Source) == 0, "First near-pot pickup is not the same chopped native bun."); p.Picked = frame;
        }
        if (p.Consumed < 0) Need(Bun(p.Food), "Near-pot bun changed before the verified consumption.");
        else Need(Recipe(p.Food, 296560, false) || p.Pan != 0 && Recipe(p.Food, 472326, false), "Near-pot prepared native output recipe changed.");
    }
    else
    {
        Need(frame - p.Start <= 900, "Washer transaction exceeds its fixed deadline.");
        bool prepared = Recipe(p.Plate, p.Recipe, true);
        if (MaybeE(p.Food) is not null) Need(Recipe(p.Food, p.Recipe, false), "Washer source no longer has its exact prepared recipe.");
        else Need(prepared, "Washer source disappeared without the exact prepared plate.");
        Need(Empty(p.Plate) || prepared, "Washer plate gained the wrong composition.");
        if (Holder(p.Plate) == 1 && Empty(p.Plate) && p.Picked < 0) p.Picked = frame;
        if (prepared && p.PreparedFirst < 0)
        {
            // Native plate-under-food assembly returns the plate to the station;
            // the destroyed source remains in telemetry until the following frame.
            Need(p.Picked >= p.Start && (Holder(p.Plate) == 1 && Attached(p.Shared) == 0 || Holder(p.Plate) == -1 && Attached(p.Shared) == p.Plate),
                "Native washer assembly lacks previously held clean plate and exact plate/shared destination."); p.PreparedFirst = frame;
        }
        if (prepared && MaybeE(p.Food) is null && p.Consumed < 0) p.Consumed = frame;
        if (p.PreparedFirst >= 0 && p.Consumed < 0) Need(frame - p.PreparedFirst <= 2, "Consumed native source did not disappear within the observed destruction boundary.");
        if (prepared && Holder(p.Plate) == 1 && p.PreparedPickup < 0) p.PreparedPickup = frame;
    }
}
try
{
    Need(args.Length == 2 && !File.Exists(args[1]), "Usage: CLOSED_TRACE NEW_REPORT; never overwrite an audit.");
    Need(Hash(typeof(CarnivalRecipes).Assembly.Location) == "ad5bf5370678c092dc63b01ccccfd5cc1d8077daff372e1bd1611bdfa389122e", "Classifier is not the frozen V16 assembly.");
    using var f = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.Read);
    using var gzip = new GZipStream(f, CompressionMode.Decompress);
    using var reader = new StreamReader(gzip);
    while (reader.ReadLine() is { } line)
    {
        using var doc = JsonDocument.Parse(line); var root = doc.RootElement;
        string kind = root.GetProperty("kind").GetString()!;
        if (kind == "call")
        {
            var request = root.GetProperty("request"); string command = request.GetProperty("command").GetString()!;
            commands[command] = commands.GetValueOrDefault(command) + 1;
            if (command == "step") { Need(request.GetProperty("steps").GetInt32() == 1, "Recorded step is not one ordinary frame."); steps++; }
            var response = root.GetProperty("response"); var state = response.GetProperty("state");
            frame = state.GetProperty("gameplayFrame").GetInt32(); calls++;
            // Retain small native projections only; all event checkpoints refer to this preceding response.
            last = new JsonObject { ["gameplayFrame"] = frame, ["entities"] = JsonNode.Parse(state.GetProperty("entities").GetRawText()), ["chefs"] = JsonNode.Parse(state.GetProperty("chefs").GetRawText()) };
            foreach (var p in records.Where(p => p.End < 0))
            { Need(response.GetProperty("ok").GetBoolean() && response.GetProperty("paused").GetBoolean(), "Active audited work escaped paused successful calls."); Observe(p); }
            continue;
        }
        if (kind != "event") continue;
        string name = root.GetProperty("name").GetString()!;
        if (!(name.Contains("NearReadyPot") || name.StartsWith("plannerWasher") || name == "plannerResourceReleased")) continue;
        var v = JsonNode.Parse(root.GetProperty("value").GetRawText())!.AsObject();
        eventCounts[name] = eventCounts.GetValueOrDefault(name) + 1;
        Need(last is not null && I(v["frame"]) == frame, "Lifecycle event is not aligned to an actual native response.");
        if (name is "nativeNearReadyPotAdmitted" or "plannerWasherPlatingStarted")
        {
            bool pot = name == "nativeNearReadyPotAdmitted";
            var p = new Life { Kind = pot ? "pot" : "washer", Start = frame, Index = I(v[pot ? "recipeIndex" : "index"]), Player = I(v[pot ? "player" : "stager"]), Food = I(v["food"]), Source = I(v["source"]), Output = I(v["output"]), Vessel = I(v["pot"]), Home = I(v["home"]), Pan = I(v["pan"]), Plate = I(v["plate"]), Shared = I(v["sharedCounter"]), CleanPass = I(v["cleanPass"]), Recipe = pot ? (I(v["pan"]) == 0 ? 296560 : 472326) : I(v["recipe"]) };
            Need(!records.Any(x => x.Kind == p.Kind && x.End < 0 && x.Index == p.Index), "Duplicate active recipe lifecycle.");
            int[] ids = (pot ? new[] { p.Source, p.Food, p.Vessel, p.Home, p.Pan, p.Output } : new[] { p.Source, p.Food, p.Plate, p.CleanPass, p.Shared, p.Output }).Where(id => id != 0).Distinct().ToArray();
            p.Ordinals = ids.ToDictionary(id => id, id => I(E(id)["observedOrdinal"]));
            if (pot)
            {
                Need(Held(p.Player) == 0 && Attached(p.Source) == p.Food && Bun(p.Food) && Attached(p.Home) == p.Vessel && Sausage(p.Vessel, false) && !Sausage(p.Vessel, true) && N(E(p.Vessel)["cookingProgress"]) is >= 11 and < 12, "Near-pot admission lacks exact nearly cooked pot and prepared bun.");
                foreach (var pair in v["identities"]!.AsObject()) Need(p.Ordinals[int.Parse(pair.Key)] == I(pair.Value), "Near-pot admission identity receipt differs from native state.");
                p.InitialProgress = N(E(p.Vessel)["cookingProgress"]);
            }
            else Need(Attached(p.Source) == p.Food && Recipe(p.Food, p.Recipe, false) && Attached(p.CleanPass) == p.Plate && Empty(p.Plate) && Held(1) == 0 && I(v["foodOrdinal"]) == p.Ordinals[p.Food] && I(v["plateOrdinal"]) == p.Ordinals[p.Plate], "Washer admission lacks exact prepared food and clean plate.");
            records.Add(p); Observe(p); continue;
        }
        if (name == "plannerResourceReleased")
        {
            var p = records.SingleOrDefault(p => p.Kind == "pot" && p.End < 0 && p.Player == I(v["player"]) && v["name"]!.ToString() == "unplated-hotdog-" + (p.Index + 1));
            if (p is null) continue;
            int id = I(v["resource"]); Need(!p.Released.Contains(id), "Near-pot resource was released twice.");
            if (id == p.Source) Need(id != p.Output && p.Picked >= p.Start && Attached(id) == 0, "Source released before observed native pickup.");
            else Need((id == p.Vessel || id == p.Home) && p.Consumed >= p.Start && Empty(p.Vessel) && N(E(p.Vessel)["cookingProgress"]) == 0 && Attached(p.Home) == p.Vessel && (id != p.Home || p.Released.Contains(p.Vessel)), "Pot/home lease notification precedes actual empty/reset consumption.");
            p.Released.Add(id); p.ReleaseReceipts.Add(v.DeepClone());
            Need(!v["remainingOwnedResources"]!.AsArray().Any(n => I(n) == id), "Released resource remains in old Work receipt."); continue;
        }
        bool isPot = name.Contains("NearReadyPot");
        var current = records.Single(p => p.End < 0 && p.Kind == (isPot ? "pot" : "washer") && p.Index == I(v[isPot ? "recipeIndex" : "index"]));
        if (name == "nativeNearReadyPotCooked") Need(current.Cooked == frame && I(v["cookedFrame"]) == frame, "Native Cooked notification differs from independent first observation.");
        if (name == "nativeNearReadyPotConsumed") Need(current.Consumed == frame && I(v["harvestFrame"]) == frame, "Native consumption notification differs from independent first observation.");
        if (name == "plannerWasherFoodStaged") { Need(Attached(current.Shared) == current.Food && Held(current.Player) == 0, "Washer shared-food staging failed native check."); current.Staged = frame; }
        if (name == "plannerWasherPreparedPlateStaged") { Need(current.Consumed > current.Staged && current.PreparedPickup >= current.Consumed && Attached(current.Shared) == current.Plate && Recipe(current.Plate, current.Recipe, true) && MaybeE(current.Food) is null && Held(1) == 0, "Washer same-plate return failed native check."); current.PreparedStaged = frame; }
        if (name is "nativeNearReadyPotComplete" or "plannerWasherPlatingComplete")
        {
            Identity(current); Need(current.Consumed >= current.Start, "Completion without an actual food transfer.");
            int item = isPot ? current.Food : current.Plate;
            Need(Attached(current.Output) == item && Holder(item) == -1 && Recipe(item, current.Recipe, !isPot), "Completion lacks exact native finished food on the reserved output.");
            if (isPot) Need(Held(current.Player) == 0 && I(v["cookedFrame"]) == current.Cooked && I(v["harvestFrame"]) == current.Consumed && v["potReleased"]!.GetValue<bool>() == current.Released.Contains(current.Vessel) && v["homeReleased"]!.GetValue<bool>() == current.Released.Contains(current.Home), "Pot completion lifecycle or lease flags differ.");
            else Need(current.PreparedStaged > current.Consumed && Held(I(v["recoveryPlayer"])) == 0, "Washer final recovery lacks staged same-plate evidence.");
            current.End = frame;
        }
    }
    Need(frame == 16203 && calls == 16206 && steps == 16203, "Closed V16 trace extent differs from extraction receipt.");
    report["ok"] = true;
}
catch (Exception ex) { report["ok"] = false; report["error"] = ex.ToString(); Environment.ExitCode = 1; }
report["scope"] = "Independent native food/identity/home/output transitions for every admitted near-ready pot and washer-side plating interval in one completed V16 adaptive trace. Resource releases are controller lease notifications checked against native consumer evidence, not native game locks. No replay or whole-state identity assertion.";
report["trace"] = args.Length > 0 ? Path.GetFullPath(args[0]) : null;
report["traceSha256"] = args.Length > 0 ? Hash(args[0]) : null;
report["classifierSha256"] = Hash(typeof(CarnivalRecipes).Assembly.Location);
report["sourceSha256"] = Hash(Path.Combine(AppContext.BaseDirectory, "../../../Program.cs"));
report["calls"] = calls; report["steps"] = steps; report["lastGameplayFrame"] = frame;
report["commandCounts"] = JsonSerializer.SerializeToNode(commands);
report["eventCounts"] = JsonSerializer.SerializeToNode(eventCounts);
report["nearReadyPot"] = JsonSerializer.SerializeToNode(records.Where(p => p.Kind == "pot"));
report["washerPlating"] = JsonSerializer.SerializeToNode(records.Where(p => p.Kind == "washer"));
report["completed"] = records.Count(p => p.End >= 0); report["pendingAtRoundEnd"] = records.Count(p => p.End < 0);
File.WriteAllText(args[1], report.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(new JsonObject { ["ok"] = report["ok"]!.DeepClone(), ["completed"] = report["completed"]!.DeepClone(), ["pending"] = report["pendingAtRoundEnd"]!.DeepClone(), ["frame"] = frame, ["error"] = report["error"]?.DeepClone() }.ToJsonString());

sealed class Life
{
    public string Kind { get; set; } = "";
    public int Index { get; set; }
    public int Player { get; set; }
    public int Start { get; set; }
    public int End { get; set; } = -1;
    public int Last { get; set; }
    public int Samples { get; set; }
    public int Food { get; set; }
    public int Source { get; set; }
    public int Output { get; set; }
    public int Vessel { get; set; }
    public int Home { get; set; }
    public int Pan { get; set; }
    public int Plate { get; set; }
    public int Shared { get; set; }
    public int CleanPass { get; set; }
    public int Recipe { get; set; }
    public int Picked { get; set; } = -1;
    public int Cooked { get; set; } = -1;
    public int Consumed { get; set; } = -1;
    public int Staged { get; set; } = -1;
    public int PreparedStaged { get; set; } = -1;
    public int PreparedFirst { get; set; } = -1;
    public int PreparedPickup { get; set; } = -1;
    public double InitialProgress { get; set; }
    public double MaxProgress { get; set; }
    public Dictionary<int, int> Ordinals { get; set; } = [];
    public HashSet<int> Released { get; set; } = [];
    public List<JsonNode> ReleaseReceipts { get; set; } = [];
}
