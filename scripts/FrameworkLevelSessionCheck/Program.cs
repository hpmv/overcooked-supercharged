using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SuperchargedPatch.Authoring.Modules;

var checks=new List<string>();
void Check(bool pass,string name){if(!pass)throw new Exception(name);checks.Add(name);}
void Reject(Action run,string name){try{run();}catch(InvalidOperationException){checks.Add(name);return;}throw new Exception("Missing rejection: "+name);}
LevelCandidate Good(int index=1)=>new(){Index=index,Label="Text.Menu.Level01",World="One",Theme="Sushi",Allowed=true,Hidden=false,Players=4,Scene="s_sushi_1_1",Config="Sushi_1_1S_4P"};
Check(LevelSelection.MainOneOne(new[]{Good()}).Index==1,"Select actual main directory identity");
Check(LevelSelection.MainOneOne(new[]{Good(9)}).Index==9,"Derive observed ordinal rather than hardcode index1");
var irrelevant=Good(11);irrelevant.Label="Text.Menu.Level12";irrelevant.Scene="s_Day_3_4";
Check(LevelSelection.MainOneOne(new[]{irrelevant,Good()}).Index==1,"Ignore Carnival and unrelated directory rows");
var lower=Good();lower.Scene="S_SUSHI_1_1";
Check(LevelSelection.MainOneOne(new[]{lower})==lower,"Allow native asset-bundle scene casing only");
foreach(var mutation in new Action<LevelCandidate>[] {r=>r.Players=3,r=>r.Players=-1,r=>r.Scene="s_sushi_1_2",r=>r.Allowed=false,r=>r.Hidden=true,r=>r.Config=null,r=>r.Index=-1})
{var row=Good();mutation(row);Reject(()=>LevelSelection.MainOneOne(new[]{row}),"Refuse unsupported native variant "+checks.Count);}
Reject(()=>LevelSelection.MainOneOne(new[]{Good(),Good(2)}),"Duplicate native main1-1 rejects");
Reject(()=>LevelSelection.MainOneOne(Array.Empty<LevelCandidate>()),"Missing target rejects");
var tutorial=Good();tutorial.World="Tutorial";Reject(()=>LevelSelection.MainOneOne(new[]{tutorial}),"Tutorial is not main1-1");

var root=Directory.GetCurrentDirectory();
var nativePath=Path.Combine(root,"runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll");
var corePath=Path.Combine(root,"artifacts/framework-plugin-native-x/SuperchargedPatch.dll");
var modulePath=Path.Combine(root,"framework-run/modules/LevelSession-r2/LevelSession.r2.dll");
using var native=AssemblyDefinition.ReadAssembly(nativePath);
using var core=AssemblyDefinition.ReadAssembly(corePath);
using var module=AssemblyDefinition.ReadAssembly(modulePath);
TypeDefinition T(AssemblyDefinition a,string name)=>a.MainModule.Types.Single(t=>t.FullName==name);
MethodDefinition M(TypeDefinition t,string name,params string[] signature)=>t.Methods.Single(m=>m.Name==name&&m.Parameters.Select(p=>p.ParameterType.FullName).SequenceEqual(signature));
var messenger=T(native,"ServerMessenger");
Check(M(messenger,"LoadLevel","System.String","GameState","System.Boolean","GameState").IsStatic,"Installed native frontend load signature exists");
Check(M(messenger,"LoadLevel","System.UInt32","System.UInt32","GameState","GameState").IsStatic,"Installed native indexed kitchen load signature exists");
Check(M(messenger,"SetupCoopSession","System.Int32","GameProgress/GameProgressData","System.Boolean[]","GameModes.SessionConfig").ReturnType.FullName=="System.Boolean","Installed native setup receipt is boolean");
Check(M(T(native,"T17FrontendFlow"),"StartEmptySession","GameSession/GameType","System.Int32").ReturnType.FullName=="GameSession","Installed native session factory exists");
var couch=M(T(native,"FrontendCoopTabOptions"),"SetupGameSession");
Check(couch.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldc_I4_M1)&&couch.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="StartEmptySession"),"Installed base game native menu passes DLC-1");
foreach(string type in new[]{"GameModes.ServerCampaignMode","GameModes.ClientCampaignMode"})
{
 var begin=M(T(native,type),"Begin");
 Check(begin.Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_recipesBeforeTimerStarts"),type+" Begin reads native prerequisite");
 Check(begin.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="AddSuppressor"),type+" Begin gates timer using native suppressor");
 Check(T(native,type).Fields.Any(f=>f.Name=="m_context"),type+" actual context field exists");
}
var bridge=T(core,"SuperchargedPatch.Bridge.NativeSessionBridge");
foreach(string name in new[]{"holdPause","loadComplete","lastError","readyUnityFrame","loadingDeadline","loading","root"})
 Check(bridge.Fields.Any(f=>f.Name==name&&f.IsStatic),"FrozenX bridge field "+name);
Check(M(bridge,"FinishLoad","System.Boolean","System.String").IsStatic,"Frozen X finalizer signature preserved");
var setup=T(core,"SuperchargedPatch.Bridge.SessionSetup");
Check(M(setup,"ValidateFourLocalUsers").IsStatic,"FrozenX local-user validation reused");
var timer=T(module,"SuperchargedPatch.Authoring.Modules.StoryTimerPolicy");
var writes=timer.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Where(i=>i.OpCode==OpCodes.Stfld&&i.Operand is FieldReference f&&f.DeclaringType.Scope.Name=="Assembly-CSharp").Select(i=>((FieldReference)i.Operand).FullName).Distinct().ToArray();
Check(writes.Length==1&&writes[0].EndsWith("KitchenLevelConfigBase::m_recipesBeforeTimerStarts"),"Timer module only writes named native prerequisite field");
Check(!timer.Methods.Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions).Any(i=>i.Operand is MethodReference m&&new[]{"Resume","SetRemainingTime","SetTimeLimit"}.Contains(m.Name)),"Timer override contains no timer advancement/correction call");
Check(M(timer,"Install").Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="Patch")==2,"Both native Begin hooks installed");
Check(M(timer,"Dispose").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="UnpatchSelf"),"Module disposal releases its native Begin hooks");
var assemblyRefs=module.MainModule.AssemblyReferences;
Check(assemblyRefs.All(a=>a.Name!="Newtonsoft.Json"),"No stripped CLR JSON dependency introduced");
Check(module.MainModule.Runtime==TargetRuntime.Net_2_0,"External module targets installed CLR2 runtime");
var asset="H:/tiny2/Overcooked2/tinyoc2/Assets/AssetBundles/bundle18/data/datafile/CoopGameSceneDirectory.asset";
var text=File.ReadAllText(asset).Replace("\r\n","\n");
var entries=text.Split("\n  - Label: ").Skip(1).ToArray();
int actual=Array.FindIndex(entries,e=>e.StartsWith("Text.Menu.Level01\n"));
Check(actual==1,"Extracted native directory independently shows main1-1 ordinal1");
var four=Regex.Match(entries[actual],"- PlayerCount: 4\\n(?<value>.*?)(?=\\n    PreviousEntriesToUnlock:)",RegexOptions.Singleline).Groups["value"].Value;
Check(four.Contains("SceneName: s_sushi_1_1"),"Extracted native four-player scene corroborates selection");
var config="H:/tiny2/Overcooked2/tinyoc2/Assets/Resources/datafile/levelconfigs/overcooked_2/Sushi_1_1S_4P.asset";
var configText=File.ReadAllText(config);
Check(configText.Contains("m_recipesBeforeTimerStarts: 1")&&configText.Contains("m_roundTimer: 150"),"Extracted original rule1 and duration150 corroborate override scope");
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)));
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,scope="Offline pure selector plus installed/native and frozen-module IL contracts; no Unity/native execution",count=checks.Count,checks,sources=new[]{nativePath,corePath,modulePath,asset,config}.Select(p=>new{path=p,sha256=Hash(p)})},new JsonSerializerOptions{WriteIndented=true}));
