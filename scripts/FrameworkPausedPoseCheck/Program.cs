using System.Reflection;
using System.Numerics;
using Quaternion = System.Numerics.Quaternion;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Hpmv;
using Supercharged.Headless;

var stdout=Console.Out; Console.SetOut(Console.Error);
string folder="artifacts/framework-paused-pose-fixtures";
var names=new List<string>();
void Check(bool ok,string why){if(!ok)throw new Exception(why);names.Add(why);}
string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
var diagnosis=JsonNode.Parse(File.ReadAllText("artifacts/framework-migration/native-w-v10/plate-search-2-baseline-rotation-diagnosis.json"));
object Core(HeadlessSession s)=>typeof(HeadlessSession).GetField("core",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(s);
RealGameSimulator Sim(HeadlessSession s)=>(RealGameSimulator)Core(s).GetType().GetField("simulator").GetValue(Core(s));
HeadlessSession Session()=>new(Hpmv.Save.GameSetup.Parser.ParseFrom(File.ReadAllBytes(folder+"/initial.pb")).FromProto(),"actual W trace header",folder);
Quaternion Expected(string label){var body=diagnosis["contexts"].AsArray().Single(x=>x["label"].GetValue<string>()==label)["actualNativeBody"];var r=body["rotation"];return new(r["x"].GetValue<float>(),r["y"].GetValue<float>(),r["z"].GetValue<float>(),r["w"].GetValue<float>());}
var manifest=JsonNode.Parse(File.ReadAllText(folder+"/manifest.json"));
foreach(var fixture in manifest["fixtures"].AsArray())Check(Hash(fixture["file"].GetValue<string>())==fixture["sha256"].GetValue<string>(),"Fixture hash matches extraction "+fixture["file"]);
Check(Hash(folder+"/initial.pb")==manifest["initialSha256"].GetValue<string>(),"Exact initial setup protobuf hash");
var results=new JsonArray(); HeadlessSession last=null; OutputData witness=null;
foreach(string candidate in new[]{"candidate-0","candidate-3"}){
 var session=Session(); int callback=0; bool initialChecked=false;
 foreach(string line in File.ReadLines(folder+"/"+candidate+".jsonl")){
  var row=JsonNode.Parse(line);
  if(row["kind"].GetValue<string>()=="control"){session.Command(row["request"].AsObject());continue;}
  var output=row["output"].Deserialize<OutputData>(RuntimeHost.Json);callback++;
  session.getNext(output.DeepCopy()).GetAwaiter().GetResult();
  Check(session.Inspect()["state"].GetValue<string>()!="Error","Captured callback reconstructs without error "+candidate+"/"+callback);
  if(!initialChecked&&output.LastFramePaused&&output.NextFramePaused&&output.Items.TryGetValue(106,out var item)&&item.__isset.rotation){
   Check(Sim(session).Frame==1,"Paused initial refresh remains at actual frame1 "+candidate);
   Check(Sim(session).entityIdToRecord[106].rotation[1]==Expected(candidate+"-base"),"Actual paused refresh matches exact native Rigidbody rotation "+candidate);
   initialChecked=true;witness=output.DeepCopy();
  }
 }
 Check(initialChecked&&Sim(session).Frame==31,"Captured initial refresh and native warmup boundary present "+candidate);
 Check(Sim(session).entityIdToRecord[106].rotation[31]==Expected(candidate+"-base"),"Native rotation retained through unchanged-field warmup "+candidate);
 var inspection=session.Inspect(true);results.Add(new JsonObject{["candidate"]=candidate,["callbacks"]=callback,["chef"]=inspection["entities"].AsArray().Single(x=>x["id"].GetValue<int>()==106).DeepClone()});last=session;
}
Check(Expected("candidate-0-base")==Expected("candidate-3-base"),"Both independent actual native receipts agree exactly");
var simulator=Sim(last);var chef=simulator.entityIdToRecord[106];int before=simulator.Frame;
var sparse=new OutputData{ServerMessages=new(),Items=new(),Chefs=new(),EntityRegistry=new(),InvalidStateReason="",LastFramePaused=true,NextFramePaused=true};
last.getNext(sparse.DeepCopy()).GetAwaiter().GetResult();
Check(simulator.Frame==before&&chef.rotation[before]==Expected("candidate-3-base"),"Absent optional fields preserve last native observation without advancing");
var changed=sparse.DeepCopy(); changed.Items[106]=new(){Rotation=new(){X=0,Y=0,Z=0,W=1},Velocity=new(){X=0.125,Y=-0.25,Z=0.5},AngularVelocity=new(){X=1,Y=2,Z=3}};
var priorPosition=chef.position[before];last.getNext(changed).GetAwaiter().GetResult();
Check(simulator.Frame==before&&chef.rotation[before]==Quaternion.Identity,"Changed native paused rotation applies without frame advance");
Check(chef.position[before]==priorPosition&&chef.velocity[before]==new Vector3(.125f,-.25f,.5f)&&chef.angularVelocity[before]==new Vector3(1,2,3),"Only present native pose fields apply, including effective frozen velocities");
Check(chef.rotation.changes.All(x=>x.time<=before),"Paused observation creates no future history");
var state=Core(last).GetType().GetProperty("State");
foreach(string phase in new[]{"AwaitingPhysicsPhaseShiftAlignment","AwaitingResume"}){
 state.SetValue(Core(last),Enum.Parse(state.PropertyType,phase));
 var phaseItem=sparse.DeepCopy();phaseItem.Items[106]=new(){Rotation=new(){X=0,Y=1,Z=0,W=0}};
 last.getNext(phaseItem).GetAwaiter().GetResult();
 Check(simulator.Frame==before&&chef.rotation[before]==new Quaternion(0,1,0,0),"Native pose applies during "+phase+" without advancing");
 chef.rotation.ChangeTo(Quaternion.Identity,before);
}
state.SetValue(Core(last),Enum.Parse(state.PropertyType,"Warping"));
var unacknowledged=sparse.DeepCopy();unacknowledged.Items[106]=new(){Rotation=new(){X=0,Y=1,Z=0,W=0}};
last.getNext(unacknowledged).GetAwaiter().GetResult();
Check(chef.rotation[before]==Quaternion.Identity,"Unacknowledged warp never uses ordinary paused pose application");
state.SetValue(Core(last),Enum.Parse(state.PropertyType,"Paused"));
var unknown=sparse.DeepCopy();unknown.Items[999]=new(){Rotation=new(){W=1}};
last.getNext(unknown).GetAwaiter().GetResult();
Check(!simulator.entityIdToRecord.ContainsKey(999),"Pose delta cannot fabricate an unregistered logical entity");
var report=new JsonObject{["ok"]=true,["checks"]=names.Count,["semanticChecks"]=names.Count(n=>!n.StartsWith("Captured callback")),["names"]=JsonSerializer.SerializeToNode(names.Where(n=>!n.StartsWith("Captured callback"))),["results"]=results,["hostSha256"]=Hash(typeof(HeadlessSession).Assembly.Location),["manifestSha256"]=Hash(folder+"/manifest.json"),["scope"]="Offline exact recorded load/start/paused-refresh/warmup observations plus paired native Rigidbody receipts; no native game calls or input/performance equivalence claim."};
File.WriteAllText(args.ElementAtOrDefault(0)??"artifacts/framework-paused-pose-check/tests.json",report.ToJsonString());stdout.WriteLine(report.ToJsonString());
