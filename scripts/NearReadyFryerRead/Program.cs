using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

try
{
    const string path = "artifacts/near-ready-fryer-v15-gf2409.json";
    var response = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    var planner = new CarnivalPlanner(_ => throw new InvalidOperationException("File-only review attempted native I/O."), null);
    var type = typeof(CarnivalPlanner); const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
    void Set(string name, object value) => type.GetField(name, flags)!.SetValue(planner, value);
    T Field<T>(string name) => (T)type.GetField(name, flags)!.GetValue(planner)!;
    object? Call(string name, params object?[] values) => type.GetMethod(name, flags)!.Invoke(planner, values);
    var projected = JsonNode.Parse(File.ReadAllText("artifacts/v15-partial-mixer.json"))!;
    var sequence = projected["events"]!.AsArray().OfType<JsonObject>().First(e => e["name"]!.ToString() == "plannerInitialized")
        ["value"]!["preview"]!["recipes"]!.AsArray().OfType<JsonObject>()
        .Select(r => CarnivalRecipes.GetRecipe(r["recipeId"]!.GetValue<int>())).ToArray();
    Set("response", response); Set("options", new CarnivalPlannerOptions(ServiceSideHead: true)); Set("recipes", sequence);
    Call("Refresh");
    // Explicit reconstruction from job/resource and persistent-lease events,
    // not a claim that controller ownership is native game state.
    Field<Dictionary<int,int>>("mealPlates")[3] = 13;
    Field<HashSet<int>>("assembling").Add(2); Field<HashSet<int>>("plating").Add(2);
    Field<HashSet<int>>("reserved").UnionWith([11,161,38,47,72,79,69,56,7,19,33,4,16,61]);
    var model = Field<KitchenModel>("model");
    var obstacles = (KitchenObstacle[])Call("TrafficObstacles", 0, -1, default(Point2))!;
    var origin = (Point2)Call("Position",0)!;
    int plate = (int)Call("AvailablePlate",0)!;
    var pickupStation = model.Stations.Single(s => s.EntityId == plate);
    var basketStation = model.Stations.Single(s => s.EntityId == 5);
    var pickup = Navigation.ToStation(model, origin, pickupStation, obstacles);
    var harvest = pickup.Success ? Navigation.ToStation(model, pickup.Points[^1], basketStation, obstacles) : pickup;
    var state = response["state"]!; var chef = state["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>c["playerId"]!.GetValue<int>()==0);
    double speed=chef["runSpeed"]!.GetValue<double>()*chef["surfaceSpeedMultiplier"]!.GetValue<double>();
    var basket=state["entities"]!.AsArray().OfType<JsonObject>().Single(e=>e["id"]!.GetValue<int>()==5);
    double progress=basket["cookingProgress"]!.GetValue<double>();
    var report = new
    {
        qualification="File-only native snapshot plus explicitly reconstructed emitted controller ownership; no native action executed",
        frame=2409,player=0,basket=5,home=15,index=7,recipe=sequence[7].Id,plate,
        output=(int)Call("MealOutput",plate,7)!,canAllocatePlate=(bool)Call("CanAllocatePlate",7)!,
        existingDirectRouteBudgetPasses=(bool)Call("DirectFryerPlateHasTime",0,5)!,
        nativeRecipeAlreadyCooked=CarnivalRecipes.MatchRecipe(basket,sequence[7].Id,false).ReadyToDeliver,
        nativeProgress=progress,nativeCookingTime=10,guardRemaining=19-progress,
        walkingSpeed=speed,pickup,harvest,
        conservativeWaitWalkAndEdgesSeconds=pickup.Success&&harvest.Success?(pickup.Length+harvest.Length)/speed+2+Math.Max(0,10-progress):(double?)null,
        reservedFromEmittedOwnership=Field<HashSet<int>>("reserved").Order().ToArray(),
        sourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
        loadedControllerSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(type.Assembly.Location))).ToLowerInvariant()
    };
    Console.WriteLine(JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
