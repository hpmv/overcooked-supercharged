using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    var findings = new List<object>();
    foreach (var selection in new[] { (Frame:11099, Player:3, Plate:379, Output:52, Recipe:257844), (Frame:11104, Player:3, Plate:379, Output:52, Recipe:257844), (Frame:11105, Player:3, Plate:379, Output:52, Recipe:257844), (Frame:12407, Player:0, Plate:408, Output:52, Recipe:47642) })
    {
        string path = $"artifacts/v16-service-departure-gf{selection.Frame}.json";
        if (!File.Exists(path)) path = $"artifacts/v16-final-sauce-gf{selection.Frame}.json";
        var response = JsonNode.Parse(File.ReadAllText(path))!.AsObject(); var state = response["state"]!.AsObject();
        var planner = new CarnivalPlanner(_ => throw new InvalidOperationException("File-only review attempted native I/O."), null);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic; var type = typeof(CarnivalPlanner);
        type.GetField("response", flags)!.SetValue(planner, response); type.GetMethod("Refresh", flags)!.Invoke(planner, null);
        var model = (KitchenModel)type.GetField("model", flags)!.GetValue(planner)!;
        var obstacles = (KitchenObstacle[])type.GetMethod("TrafficObstacles", flags)!.Invoke(planner, [selection.Player, -1, default(Point2)])!;
        var chef = state["chefs"]!.AsArray().OfType<JsonObject>().Single(c => c["playerId"]!.GetValue<int>() == selection.Player);
        var plate = state["entities"]!.AsArray().OfType<JsonObject>().Single(e => e["id"]!.GetValue<int>() == selection.Plate);
        var position = KitchenModel.Position(chef["position"]); double speed = chef["runSpeed"]!.GetValue<double>() * chef["surfaceSpeedMultiplier"]!.GetValue<double>();
        var distance = typeof(Navigation).GetMethod("ObstacleDistanceSquared", BindingFlags.Static | BindingFlags.NonPublic)!;
        var nearby = model.Obstacles.Concat(obstacles).Select(o => new { o.Key, o.EntityId, o.Dynamic, o.Bounds, o.CircleRadius, distance = Math.Sqrt((double)distance.Invoke(null, [position, o])!) }).OrderBy(o => o.distance).Take(5).ToArray();
        var toSauce = Navigation.ToStation(model, position, model.Stations.Single(s => s.EntityId == 72), obstacles);
        var fromNativeSaucePose = Navigation.ToStation(model, position, model.Stations.Single(s => s.EntityId == selection.Output), obstacles);
        NavigationPath? afterApproach = toSauce.Success ? Navigation.ToStation(model, toSauce.Points[^1], model.Stations.Single(s => s.EntityId == selection.Output), obstacles) : null;
        findings.Add(new {
            selection.Frame, selection.Player, selection.Plate, selection.Output, selection.Recipe,
            nativePlacementTarget=chef["placementTargetId"]!.GetValue<int>(), position, speed,
            model.ChefRadius, model.Clearance, nearby,
            nativeFoodAssessment=CarnivalRecipes.MatchRecipe(plate,selection.Recipe), toSauce, fromNativeSaucePose, afterApproach,
            proposedFramesIfAlreadyNativeSauceTarget=fromNativeSaucePose.Success?fromNativeSaucePose.Length/speed*60+24:(double?)null,
            proposedFramesWithApproach=toSauce.Success&&afterApproach?.Success==true?(toSauce.Length+afterApproach.Length)/speed*60+24:(double?)null,
            proposedAllowance="12 frames existing final-transfer allowance plus12 frames for final ordinary apply; this is a proposed admission budget, not a measured guarantee",
            sourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
        });
    }
    Console.WriteLine(JsonSerializer.Serialize(new {
        qualification="File-only paths from native departure observations; no game inputs executed and no hypothetical score credited",
        loadedControllerSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(CarnivalPlanner).Assembly.Location))).ToLowerInvariant(), findings
    },new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
