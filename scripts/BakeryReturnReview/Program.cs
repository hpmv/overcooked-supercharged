using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;

// File-only geometry review. This executable has no controller connection.
try
{
    double N(JsonNode? n)=>n?.GetValue<double>()??0;
    int I(JsonNode? n)=>n?.GetValue<int>()??0;
    string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
    var results=new List<object>();
    foreach(int frame in new[]{11069,11445,12373,14401,15625})
    {
        string file=$"artifacts/v16-supply-snapshots/gf{frame}.json";
        var response=JsonNode.Parse(File.ReadAllText(file))!.AsObject();
        var state=KitchenModel.SnapshotState(response);var model=KitchenModel.Build(response);
        var chefs=state["chefs"]!.AsArray().OfType<JsonObject>().ToArray();
        var entities=state["entities"]!.AsArray().OfType<JsonObject>().ToDictionary(e=>I(e["id"]));
        var chef=chefs.Single(c=>I(c["playerId"])==1);double speed=N(chef["runSpeed"])*N(chef["surfaceSpeedMultiplier"]);
        var obstacles=chefs.Where(c=>I(c["playerId"])!=1).Select(c=>
        {var p=KitchenModel.Position(c["position"]);double r=model.ChefRadius;return new KitchenObstacle("chef:"+I(c["playerId"]),I(c["entityId"]),new(p.X-r,p.X+r,p.Z-r,p.Z+r),"Actual native chef",true,CircleRadius:r);}).ToArray();
        var paths=new List<object>();double distance=0;
        Point2 StationPath(string name,Point2 start,int station)
        {
            var path=Navigation.ToStation(model,start,model.Stations.Single(s=>s.EntityId==station),obstacles);
            paths.Add(new{name,station,path});if(!path.Success)throw new InvalidOperationException($"GF{frame} {name}: {path.Error}");distance+=path.Length;return path.Points[^1];
        }
        Point2 FloorPath(string name,Point2 start,Point2 target)
        {var path=Navigation.FindPath(model,start,target,dynamic:obstacles);paths.Add(new{name,path});if(!path.Success)throw new InvalidOperationException($"GF{frame} {name}: {path.Error}");distance+=path.Length;return path.Points[^1];}
        var portal=model.Transitions.Single(t=>t.Kind=="portal"&&t.Verified&&t.FromRegion=="lower-left"&&t.ToRegion=="upper-right");
        StationPath("native lower-left portal approach",KitchenModel.Position(chef["position"]),model.Resolve(portal.SourceKey).EntityId);
        double portalDistance=distance;distance=0;
        // This position was observed on this round's ordinary completed portal
        // action at GF7771. It is not the elevated animation teleport point.
        Point2 cursor=new(28.8,-11.2999992),throwPoint=new(27.2,-12.2);
        int flour=model.Stations.Single(s=>s.Ingredient=="Flour").EntityId;
        int egg=model.Stations.Single(s=>s.Ingredient=="Egg").EntityId;
        int chocolate=model.Stations.Single(s=>s.Ingredient=="Chocolate").EntityId;
        int board=model.Stations.Where(s=>s.Role=="chop"&&s.Regions.Contains("upper-right")).OrderByDescending(s=>s.Position.Z).ElementAt(1).EntityId;
        cursor=StationPath("Flour pickup",cursor,flour);cursor=FloorPath("Flour native throw staging",cursor,throwPoint);
        cursor=StationPath("Egg pickup",cursor,egg);cursor=FloorPath("Egg native throw staging",cursor,throwPoint);
        cursor=StationPath("Chocolate pickup",cursor,chocolate);cursor=StationPath("Chocolate board approach",cursor,board);cursor=FloorPath("prepared Chocolate native throw staging",cursor,throwPoint);
        // Conservative accounting: two seconds portal/landing and two seconds
        // per existing ingredient job for native edges and transfer settling.
        double kitSeconds=portalDistance/speed+2+distance/speed+6+CarnivalRecipes.Chocolate.ChopSeconds;
        var plates=entities.Values.Where(e=>CarnivalRecipes.ClassifyEntity(e).IsPlate).Select(e=>new{
            id=I(e["id"]),ordinal=I(e["observedOrdinal"]),active=e["active"],
            holder=chefs.Where(c=>I(c["heldEntityId"])==I(e["id"])).Select(c=>I(c["playerId"])).ToArray(),
            parent=entities.Values.Where(p=>I(p["attachedEntityId"])==I(e["id"])).Select(p=>I(p["id"])).ToArray(),
            physicalActive=e["colliders"]?.AsArray().OfType<JsonObject>().Count(c=>c["active"]?.GetValue<bool>()==true&&c["enabled"]?.GetValue<bool>()==true&&c["trigger"]?.GetValue<bool>()!=true),
            food=CarnivalRecipes.ClassifyEntity(e).Food}).ToArray();
        results.Add(new{frame,file,sha256=Hash(file),delivered=I(state["delivered"]),timer=N(state["timer"]),nativeSpeed=speed,
            portalDistance,kitWalkingDistance=distance,fullKitBudgetSeconds=kitSeconds,
            plusFullNativeMixAndFrySeconds=kitSeconds+12+10,
            remainingAfterKitMixFry=N(state["timer"])-kitSeconds-22,
            paths,plates});
    }
    Console.WriteLine(JsonSerializer.Serialize(new{format="predictive-bakery-return-native-geometry-review-v1",
        controllerBundle="artifacts/planner-candidate-v18",controllerSha256=Hash("artifacts/planner-candidate-v18/OvercookedTAS.Controller.dll"),
        sourceSha256=Hash("scripts/BakeryReturnReview/Program.cs"),
        qualification="Exact native geometry with frozen controller navigation. One sequential whole-kit walking/portal/edges estimate; not a guaranteed completion time, runtime admission, score prediction or reservation reconstruction. Future center transfers/plating/service are not included in the reported sum.",results},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
