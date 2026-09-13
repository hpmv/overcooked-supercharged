using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;

if(args.Length!=4)throw new ArgumentException("ADMISSION_RESPONSE PROJECTION OWNERSHIP OUTPUT");
var snapshot=JsonNode.Parse(File.ReadAllText(args[0]))!.AsObject();
var model=KitchenModel.Build(snapshot);var state=KitchenModel.SnapshotState(snapshot);
var projection=JsonNode.Parse(File.ReadAllText(args[1]))!;
var ownership=JsonNode.Parse(File.ReadAllText(args[2]))!;
var startSample=projection["nativeSamples"]!.AsArray().Single(s=>s!["gameplayFrame"]!.GetValue<int>()==1585)!;
var chef=startSample["chefs"]!.AsArray().Single(c=>c!["playerId"]!.GetValue<int>()==0)!;
Point2 Point(JsonNode? n)=>new(n!["x"]!.GetValue<double>(),n["z"]!.GetValue<double>());
var start=Point(chef["position"]);
var obstacles=state["chefs"]!.AsArray().Where(c=>c!["playerId"]!.GetValue<int>()!=0).Select(c=>
{var p=Point(c!["position"]);double r=model.ChefRadius;return new KitchenObstacle("observed-chef-"+c["playerId"],c["entityId"]!.GetValue<int>(),new Rect2(p.X-r,p.X+r,p.Z-r,p.Z+r),"Admission frame native chef position, held static for geometric comparison",true,CircleRadius:r);}).ToArray();
var choices=new JsonArray();
foreach(int counter in new[]{37,33,40})
{
 var point=start;var legs=new JsonArray();bool success=true;double length=0;
 foreach(int target in new[]{counter,79,counter,72})
 {
  var path=Navigation.ToStation(model,point,model.Stations.Single(s=>s.EntityId==target),obstacles);
  legs.Add(JsonSerializer.SerializeToNode(new{target,path}));if(!path.Success){success=false;break;}
  length+=path.Length;point=path.Points[^1];
 }
 choices.Add(new JsonObject{["counter"]=counter,["success"]=success,["totalPathLength"]=length,["legs"]=legs,
   ["admissionOwnership"]=ownership["counters"]!.AsArray().Single(c=>c!["id"]!.GetValue<int>()==counter)!.DeepClone()});
}
var sourcePaths=new[]{args[0],args[1],args[2],typeof(KitchenModel).Assembly.Location};
var result=new JsonObject{["qualification"]="Static geometry comparison only: measured post-mustard start GF1585 with GF1346 admission colliders and other chef positions. No simulated state advance, predicted native completion time, or score claim. Reachability must be repeated when actions run.",
 ["start"]=JsonSerializer.SerializeToNode(start),["choices"]=choices,["hashes"]=JsonSerializer.SerializeToNode(sourcePaths.Select(p=>new{path=Path.GetFullPath(p),sha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant()}))};
File.WriteAllText(args[3],result.ToJsonString(new(){WriteIndented=true})+Environment.NewLine);
Console.WriteLine(new JsonArray(choices.Select(c=>(JsonNode?)new JsonObject{["counter"]=c!["counter"]!.DeepClone(),["success"]=c["success"]!.DeepClone(),["totalPathLength"]=c["totalPathLength"]!.DeepClone()}).ToArray()).ToJsonString());
