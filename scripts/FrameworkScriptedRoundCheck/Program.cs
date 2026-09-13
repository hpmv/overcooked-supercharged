using System.Collections;
using System.Reflection;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SuperchargedPatch.AlteredComponents;
using SuperchargedPatch.Authoring.Modules;
using Random=UnityEngine.Random;

var checks=new List<string>();
void Check(bool good,string name){if(!good)throw new Exception(name);checks.Add(name);}
void Reject(Action action,string name){try{action();}catch(Exception e)when(e is InvalidOperationException||e is ArgumentException){checks.Add(name);return;}throw new Exception("Missing rejection: "+name);}
ScriptedRoundData Source(){
 var fish=new OrderDefinitionNode{m_uID=10,name="Fish"};var prawn=new OrderDefinitionNode{m_uID=20,name="Prawn"};
 var weighted=new[]{new RecipeList.Entry{m_order=fish,m_scoreForMeal=20},new RecipeList.Entry{m_order=prawn,m_scoreForMeal=20}};
 return new(){m_roundTimer=150,m_recipes=new(){m_recipes=weighted},m_manualOrder=new[]{fish,fish,prawn,fish,prawn,fish}.Select(o=>new RecipeList.Entry{m_order=o,m_scoreForMeal=20}).ToArray()};
}
var source=Source();
var config=new ScriptedCampaignLevelConfig{m_rounds=new[]{source}};
NativeTestHost.Session=new(){LevelSettings=new(){SceneDirectoryVarientEntry=new(){LevelConfig=config}}};
var flow=new ServerKitchenFlowControllerBase();flow.monitor.OrdersController.Source=source;NativeTestHost.Flows.Add(flow);
var module=new ScriptedRoundModule();module.Invoke("activate",new());
module.Invoke("validate-current",new());
Check(ReferenceEquals(flow.monitor.OrdersController.Source,source),"Validation creates no live generator replacement or order draw");
RoundData selected=source;ScriptedRoundModule.AfterGetRoundData(config,ref selected);
var wrapper=(WarpableRoundData)selected;flow.monitor.OrdersController.Source=wrapper;
Check(ReferenceEquals(wrapper.m_recipes,source.m_recipes)&&ReferenceEquals(wrapper.m_recipes.m_recipes,source.m_recipes.m_recipes),"Exact native recipe list and array retained without cloning");
var originalField=typeof(WarpableRoundData).GetField("original",BindingFlags.Instance|BindingFlags.NonPublic);
Check(ReferenceEquals(originalField.GetValue(wrapper),source),"Actual native ScriptedRoundData is the bound generator before initialise");
int[] Baseline(int seed,int count){var ambient=Random.state;try{Random.InitState(seed);var data=source.InitialiseRound();return Enumerable.Range(0,count).Select(_=>source.GetNextRecipe(data)[0].m_order.m_uID).ToArray();}finally{Random.state=ambient;}}
var expected=Baseline(17,60);
Check(expected.Take(6).SequenceEqual(new[]{10,10,20,10,20,10}),"Native six manual opening entries retained");
Random.InitState(913);var ambient=Random.state;WarpableRoundData.ConfigureSeedForNextRound(17);
var live=(WarpableRoundInstanceData)wrapper.InitialiseRound();
Check(Random.state.Equals(ambient)&&live.RecipeCount==0&&live.CumulativeFrequencies.Sum()==0,"Initialise retains native empty state and ambient RNG");
for(int i=0;i<6;i++){
 int calls=Random.DrawCount;var returned=wrapper.GetNextRecipe(live);
 Check(ReferenceEquals(returned[0],source.m_manualOrder[i]),"Manual draw"+i+" returns actual native scripted entry object");
 Check(live.RecipeCount==i+1&&live.nextIndex==i+1&&live.CumulativeFrequencies.Sum()==0&&Random.DrawCount==calls&&Random.state.Equals(ambient),"Manual draw"+i+" has exact cursor/no RNG/no frequencies");
}
int draws=Random.DrawCount;
Check(wrapper.GetNextRecipe(live)[0].m_order.m_uID==expected[6]&&live.CumulativeFrequencies.Sum()==1&&Random.DrawCount==draws+1,"Seventh draw uses unmodified native weighted transition once");
for(int i=7;i<24;i++)Check(wrapper.GetNextRecipe(live)[0].m_order.m_uID==expected[i],"Native weighted continuation"+i);
Check(wrapper.GetDiagnostics(live).generator=="ScriptedRoundData.GetNextRecipe"&&wrapper.GetDiagnostics(live).nativeRecipes.Select(r=>r.recipeId).SequenceEqual(expected.Take(24)),"Existing diagnostics preserve native generator and recipe/base history");
var before=wrapper.GetDiagnostics(live);draws=Random.DrawCount;
var preview=wrapper.GetAuxMessage(live,24);
Check(preview.currentIndex==0&&preview.recipes.Select(r=>r.RecipeId).SequenceEqual(expected.Take(10))&&Random.DrawCount==draws+4,"Preview replays six native manual entries then four native weighted draws");
var after=wrapper.GetDiagnostics(live);
Check(after.nextIndex==before.nextIndex&&after.history.SequenceEqual(before.history)&&after.cumulativeFrequencies.SequenceEqual(before.cumulativeFrequencies)&&Random.state.Equals(ambient),"Preview preserves live cursor/history/frequencies and ambient state");
foreach(int target in new[]{0,5,6,9}){
 wrapper.Warp(live,target);draws=Random.DrawCount;
 Check(live.RecipeCount==target&&live.CumulativeFrequencies.Sum()==Math.Max(0,target-6),"Rewind restores scripted/weighted frontier at"+target);
 var repeated=Enumerable.Range(target,24-target).Select(_=>wrapper.GetNextRecipe(live)[0].m_order.m_uID).ToArray();
 Check(repeated.SequenceEqual(expected.Skip(target).Take(24-target))&&Random.DrawCount==24-Math.Max(6,target)+draws,"Native continuation repeats exactly across target"+target);
}
wrapper.Warp(live,35);
Check(live.RecipeCount==35&&live.CumulativeFrequencies.Sum()==29&&wrapper.GetDiagnostics(live).nativeRecipes.Select(r=>r.recipeId).SequenceEqual(expected.Take(35)),"Forward seek preserves native scripted prefix and weighted frontier");
before=wrapper.GetDiagnostics(live);Random.ThrowNext=true;
Reject(()=>wrapper.GetNextRecipe(live),"Native weighted failure propagated");after=wrapper.GetDiagnostics(live);
Check(after.nextIndex==before.nextIndex&&after.history.SequenceEqual(before.history)&&after.cumulativeFrequencies.SequenceEqual(before.cumulativeFrequencies)&&Random.state.Equals(ambient),"Failure restores count/frequencies/RNG without fake draw");
Reject(()=>module.Invoke("deactivate",new()),"Cannot remove hooks from live wrapped native round");
Check(wrapper.GetNextRecipe(live)[0].m_order.m_uID==expected[35],"Rejected deactivation leaves required handler active");
var wrong=new WarpableRoundData(new RoundData{m_recipes=source.m_recipes,m_roundTimer=150});
var binding=new ScriptedRoundBinding(source);
Reject(()=>binding.TryManual(wrong.InitialiseRound(),out _),"Wrong instance owner rejected");
var changed=Source();changed.m_manualOrder[0].m_scoreForMeal=999;Reject(()=>new ScriptedRoundBinding(changed),"Mismatched native manual score cannot be hidden in diagnostic mapping");
changed=Source();changed.m_recipes.m_recipes=new[]{changed.m_recipes.m_recipes[0],changed.m_recipes.m_recipes[0],changed.m_recipes.m_recipes[1]};Reject(()=>new ScriptedRoundBinding(changed),"Ambiguous native order reference rejected");
changed=Source();changed.m_manualOrder=changed.m_manualOrder.Take(5).ToArray();Reject(()=>new ScriptedRoundBinding(changed),"Unsupported manual sequence length rejected");
changed=Source();var mutation=new ScriptedRoundBinding(changed);changed.m_manualOrder=(RecipeList.Entry[])changed.m_manualOrder.Clone();Reject(()=>mutation.RequireMetadata(),"Replaced equal-content manual array rejected");
changed=Source();mutation=new ScriptedRoundBinding(changed);changed.m_manualOrder[0].m_order.name="changed";Reject(()=>mutation.RequireMetadata(),"Mutated original recipe metadata rejected");
NativeTestHost.Flows.Clear();module.Invoke("deactivate",new());module.Dispose();Check(true,"Native level boundary permits clean hook removal");

using var native=AssemblyDefinition.ReadAssembly("runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll");
var script=native.MainModule.Types.Single(t=>t.Name=="ScriptedRoundData");var next=script.Methods.Single(m=>m.Name=="GetNextRecipe");
Check(script.Methods.All(m=>m.Name!="InitialiseRound"),"Installed ScriptedRoundData inherits native initialisation");
Check(next.Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.DeclaringType.Name=="RoundData"&&m.Name=="GetNextRecipe"),"Installed script delegates weighted tail to native base");
Check(next.Body.Instructions.Where(i=>i.OpCode==OpCodes.Stfld).All(i=>((FieldReference)i.Operand).Name=="RecipeCount"),"Installed scripted body only assigns recipe cursor");
Check(!next.Body.Instructions.Any(i=>i.Operand is MemberReference m&&(m.Name=="CumulativeFrequencies"||m.DeclaringType.FullName=="UnityEngine.Random")),"Installed scripted prefix has no frequency or RNG reference");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,count=checks.Count,checks,scope="Offline source semantics: production adapter and frozen wrapper body, explicit Harmony-prefix test dispatch, native draw source with controlled Unity RNG; not live native RNG/rewind proof"},new JsonSerializerOptions{WriteIndented=true}));
