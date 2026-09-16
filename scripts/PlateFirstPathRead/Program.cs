using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
string root = "artifacts/v16-plate-first-opportunities";
var manifest = JsonNode.Parse(File.ReadAllText(root + "/manifest.json"))!.AsObject();
var history = JsonNode.Parse(File.ReadAllText("artifacts/v16-central-work-analysis.json"))!["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
var reviews = new List<object>();
int I(JsonNode? n) => n is null ? 0 : int.Parse(n.ToString());
double N(JsonNode? n) => n is null ? 0 : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
foreach (var item in manifest["jobs"]!.AsArray().OfType<JsonObject>())
{
    var oldBase = item["base"]!; var oldAssembly = item["assembly"]!;
    int frame = I(oldBase["start"]), player = I(oldBase["player"]);
    var response = JsonNode.Parse(File.ReadAllText($"{root}/gf{frame}.json"))!.AsObject();
    var state = response["state"]!.AsObject();
    var planner = new CarnivalPlanner(_ => throw new InvalidOperationException("Read-only review attempted game I/O"), null);
    var type = typeof(CarnivalPlanner);
    type.GetField("response", Flags)!.SetValue(planner, response);
    type.GetMethod("Refresh", Flags)!.Invoke(planner, null);
    var model = (KitchenModel)type.GetField("model", Flags)!.GetValue(planner)!;
    JsonObject Entity(int id) => state["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == id);
    var priorOtherWorkResources = history.Where(j => I(j["number"]) != I(oldBase["number"]) && I(j["start"]) <= frame && (j["end"] is null || I(j["end"]) > frame))
        .SelectMany(j => j["resources"]!.AsArray().Select(I)).ToHashSet();
    var held = state["chefs"]!.AsArray().OfType<JsonObject>().Select(c => I(c["heldEntityId"])).ToHashSet();
    var plates = model.Stations.Where(s => s.Role == "plate" && s.Regions.Contains("center") &&
        !priorOtherWorkResources.Contains(s.EntityId) && !held.Contains(s.EntityId) &&
        CarnivalRecipes.ClassifyEntity(Entity(s.EntityId)).Food.IngredientIds.Length == 0).Select(s => s.EntityId).ToArray();
    int source = I(oldBase["actions"]!.AsArray().First(a => a!["type"]!.ToString() == "take")!["station"]);
    int pot = I(oldBase["actions"]!.AsArray().First(a => a!["type"]!.ToString() == "combine")!["station"]);
    var chef = state["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == player);
    var obstacles = (KitchenObstacle[])type.GetMethod("TrafficObstacles", Flags)!.Invoke(planner, [player, -1, default(Point2)])!;
    int sauce = oldAssembly["actions"]!.AsArray().OfType<JsonObject>().Where(a => a["type"]!.ToString() == "apply").Select(a => I(a["expectedIngredientId"])).FirstOrDefault();
    bool switching = sauce != 0 && I(Entity(72)["switchIndex"]) != (sauce == 17094 ? 0 : 1);
    var alternatives = new List<object>();
    foreach (int plate in plates)
    foreach (int output in new[] { 47, 49, 52 }.Where(o => I(Entity(o)["attachedEntityId"]) == 0 && !priorOtherWorkResources.Contains(o)))
    {
        var cursor = KitchenModel.Position(chef["position"]); double length = 0, throughPot = 0;
        var targets = (switching ? new[] { 79 } : Array.Empty<int>()).Concat(new[] { plate, source, pot }).Concat(sauce == 0 ? [] : new[] { 72 }).Append(output);
        var legs = new List<object>(); bool clear = true;
        foreach (int target in targets)
        {
            var path = Navigation.ToStation(model, cursor, model.Stations.Single(s => s.EntityId == target), obstacles);
            legs.Add(new { target, path }); if (!path.Success) { clear = false; break; }
            cursor = path.Points[^1]; length += path.Length; if (target == pot) throughPot = length;
        }
        double speed = N(chef["runSpeed"]) * N(chef["surfaceSpeedMultiplier"]), progress = N(Entity(pot)["cookingProgress"]);
        alternatives.Add(new { plate, output, clear, length, throughPot, legs,
            conservativeSecondsThroughPot = throughPot / speed + Math.Max(0, 12 - progress) + 3.5,
            originalPotGuardRemainingSeconds = 23 - progress });
    }
    reviews.Add(new { frame, player, mealNumber = I(item["mealNumber"]), source, pot, sauce, switching, cleanPlateCandidates = plates,
        excludedOtherActiveJobResources = priorOtherWorkResources.Order().ToArray(),
        recordedOldJobChefFrames = I(oldBase["nativeFrames"]) + I(oldAssembly["nativeFrames"]),
        recordedOldFirstPathUnits = N(oldBase["firstPlanDistance"]) + N(oldAssembly["firstPlanDistance"]), alternatives });
}
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
Console.WriteLine(JsonSerializer.Serialize(new { qualification = "Read-only static path opportunities, not new planner admissions or native time savings. Other active original jobs excluded conservatively by full original resources; persistent leases, FIFO allocation and live future traffic require actual admission checks. Old planned lengths were measured at different native states.",
    manifestSha256 = Hash(root + "/manifest.json"), loadedControllerSha256 = Hash(typeof(CarnivalPlanner).Assembly.Location), reviews }, new JsonSerializerOptions { WriteIndented = true }));
