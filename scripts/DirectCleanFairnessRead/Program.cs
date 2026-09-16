using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
try
{
    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    var type=typeof(CarnivalPlanner);
    var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Read-only frozen admission attempted native I/O."),null);
    void Set(string name,object value)=>type.GetField(name,flags)!.SetValue(p,value);
    object? Invoke(string name,params object?[] args)=>type.GetMethod(name,flags)!.Invoke(p,args);
    T Get<T>(string name)=>(T)type.GetField(name,flags)!.GetValue(p)!;
    var snapshot=JsonNode.Parse(File.ReadAllText("artifacts/v17-head-plate-gf14995.json"))!.AsObject();
    Set("response",snapshot);Set("options",new CarnivalPlannerOptions(DirectCleanPassAssembly:true,CooperativeSauces:true,NearSauceStaging:true,ServiceSideHead:true));
    var preview=JsonNode.Parse(File.ReadAllText("artifacts/native-round-v17/native-preview-call.json"))!;
    Set("recipes",preview["response"]!["preview"]!["recipes"]!.AsArray().Select(r=>CarnivalRecipes.GetRecipe(r!["recipeId"]!.GetValue<int>())).ToArray());
    Invoke("Refresh");Set("runner",new RouteRunner(_=>throw new InvalidOperationException("Read-only frozen route attempted native I/O."),null));
    Get<Dictionary<int,int>>("mealFoods")[23]=419;Get<Dictionary<int,int>>("mealPlates")[22]=377;
    Get<HashSet<int>>("reserved").UnionWith([2,17,33]);
    var workType=type.GetNestedType("Work",BindingFlags.NonPublic)!;
    var busy=Activator.CreateInstance(workType,flags,null,["rescue-cooked-pot-2",Array.Empty<JsonObject>(),Array.Empty<int>(),null],null)!;
    Get<Array>("workers").SetValue(busy,3);
    string before=snapshot.ToJsonString();
    bool started=(bool)Invoke("TryAssemble",0,false,23,484,null,false)!;
    var work=Get<Array>("workers").GetValue(0);
    object? W(string name)=>work?.GetType().GetField(name,flags)!.GetValue(work);
    string hash=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CarnivalPlanner).Assembly.Location)));
    Console.WriteLine(JsonSerializer.Serialize(new{ok=started,frame=14995,controllerSha256=hash,
        nativeSnapshotUnchanged=before==snapshot.ToJsonString(),workName=W("Name"),actions=W("Actions"),resources=W("OwnedResources"),
        qualification="Frozen V17 ordinary exact-index/exact-plate admission on unmodified native14995 state with explicitly reconstructed meal and active pot-rescue ownership; no game commands or native continuation"},new JsonSerializerOptions{WriteIndented=true}));
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
