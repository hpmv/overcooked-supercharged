using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
var input = "artifacts/v16-onion-chain-gf5730.json";
var response = JsonNode.Parse(File.ReadAllText(input))!.AsObject(); var state = response["state"]!.AsObject();
var planner = new CarnivalPlanner(_ => throw new InvalidOperationException("File-only path review attempted game I/O."), null);
const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
var type = typeof(CarnivalPlanner);
type.GetField("response", flags)!.SetValue(planner, response); type.GetMethod("Refresh", flags)!.Invoke(planner, null);
var model = (KitchenModel)type.GetField("model", flags)!.GetValue(planner)!;
JsonObject Entity(int id) => state["entities"]!.AsArray().OfType<JsonObject>().Single(e => e["id"]!.GetValue<int>() == id);
var alternatives = new List<object>();
bool switchNeeded = Entity(72)["switchIndex"]!.GetValue<int>() != 0;
foreach (int player in new[] { 0, 3 })
foreach (int output in new[] { 47, 49, 52 }.Where(id => Entity(id)["attachedEntityId"]!.GetValue<int>() == 0))
{
    var chef = state["chefs"]!.AsArray().OfType<JsonObject>().Single(c => c["playerId"]!.GetValue<int>() == player);
    var obstacles = (KitchenObstacle[])type.GetMethod("TrafficObstacles", flags)!.Invoke(planner, [player, -1, default(Point2)])!;
    var cursor = KitchenModel.Position(chef["position"]); var legs = new List<object>();
    var destinations = (switchNeeded ? new[] { 79 } : Array.Empty<int>()).Concat(new[] { 243, 32, 72, 4, output });
    double length = 0, throughPan = 0; bool success = true;
    foreach (int id in destinations)
    {
        var path = Navigation.ToStation(model, cursor, model.Stations.Single(s => s.EntityId == id), obstacles);
        legs.Add(new { destination = id, path });
        if (!path.Success) { success = false; break; }
        length += path.Length; cursor = path.Points[^1]; if (id == 4) throughPan = length;
    }
    double speed = chef["runSpeed"]!.GetValue<double>() * chef["surfaceSpeedMultiplier"]!.GetValue<double>();
    double progress = Entity(4)["cookingProgress"]!.GetValue<double>();
    alternatives.Add(new { player, output, switchNeeded, success, legs, length, speed,
        plannedWalkingSeconds = length / speed, walkingSecondsThroughPan = throughPan / speed,
        nativeRemainingCookSeconds = Math.Max(0, 12 - progress), nativeGuardRemainingSeconds = 21 - progress,
        conservativeHeatBudgetSeconds = throughPan / speed + 3.5 + Math.Max(0, 12 - progress),
        qualification = "Current static/dynamic paths only. Transfer/assembly allowance3.5s and full remaining native cooking wait are conservative estimates, not executed timing or reserved future routes." });
}
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
Console.WriteLine(JsonSerializer.Serialize(new { classification = "Read-only route proposal from actual V16 GF5730; no game mutations or score projection",
    fixture = input, fixtureSha256 = Hash(input), loadedControllerSha256 = Hash(typeof(CarnivalPlanner).Assembly.Location),
    nativeFrame = state["gameplayFrame"]!.GetValue<int>(), plate = 243, plainHotdog = 234, source = 32, pan = 4, panHome = 16,
    panNativeProgress = Entity(4)["cookingProgress"]!.GetValue<double>(), alternatives }, new JsonSerializerOptions { WriteIndented = true }));
