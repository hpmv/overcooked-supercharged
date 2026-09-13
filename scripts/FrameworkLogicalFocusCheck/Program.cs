using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text.Json;
using HarmonyLib;
using Mono.Cecil;
using SuperchargedPatch;
using UnityEngine;
using Team17.Online.Multiplayer.Messaging;
using Hpmv;

var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
const string game="runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll";
string plugin=args.Length>0?args[0]:"artifacts/framework-plugin-native-v/SuperchargedPatch.dll";
using var assembly=AssemblyDefinition.ReadAssembly(game);using var candidate=AssemblyDefinition.ReadAssembly(plugin);
var native=assembly.MainModule.Types.Single(t=>t.Name=="LogicalButtonBase");
MethodDefinition Method(TypeDefinition t,string n)=>t.Methods.Single(m=>m.Name==n);
bool Calls(MethodDefinition m,string owner,string method)=>m.Body.Instructions.Any(i=>i.Operand is MethodReference r&&r.DeclaringType.Name==owner&&r.Name==method);
Check(Calls(Method(native,"CanProcessInput"),"Application","get_isFocused"),"installed native logical focus reads Application.isFocused");
var update=Method(native,"Update");
Check(Calls(update,"LogicalButtonBase","CanProcessInput")&&Calls(update,"LogicalButtonBase","ClaimPressEvent")&&Calls(update,"LogicalButtonBase","ClaimReleaseEvent"),"installed Update claims both edges when focus predicate rejects");
Check(Calls(Method(native,"HasUnclaimedPressEvent"),"LogicalButtonBase","Update"),"native edge observation updates claims before acceptance");
var nativeGate=assembly.MainModule.Types.Single(t=>t.Name=="GateLogicalButton");
Check(nativeGate.BaseType.Name=="LogicalButtonBase"&&!nativeGate.Methods.Any(m=>m.Name=="CanProcessInput"),"installed native gates inherit the same focus predicate");
Check(nativeGate.Fields.Single(f=>f.Name=="m_childButton").FieldType.Name=="ILogicalButton"&&Calls(Method(nativeGate,"IsDown"),"ILogicalButton","IsDown"),"installed gate retains exact child and native level callback");
var patch=candidate.MainModule.Types.Single(t=>t.Name=="BackgroundTasLogicalInputFocus");
Check(patch.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPatch"&&a.ConstructorArguments.Any(v=>v.Value?.ToString()=="CanProcessInput")),"compiled plugin patch targets CanProcessInput only");
var tas=candidate.MainModule.Types.Single(t=>t.Name=="TASLogicalButton");
Check(!tas.Methods.Any(m=>new[]{"Update","JustPressed","JustReleased","CanProcessInput"}.Contains(m.Name)),"TAS device does not replace native claim/down/edge methods");
var getter=AccessTools.PropertyGetter(typeof(Application),"isFocused");
var input=new[]{new CodeInstruction(OpCodes.Call,getter),new CodeInstruction(OpCodes.Ret)};
var generated=BackgroundTasLogicalInputFocus.Transpiler(input).ToArray();
Check(generated.Length==3&&generated[0].opcode==OpCodes.Ldarg_0&&generated[1].operand is MethodInfo info&&info.Name=="IsFocusedForLogicalButton","actual transpiler substitutes only the getter plus this argument");
var method=new DynamicMethod("Focus",typeof(bool),new[]{typeof(LogicalButtonBase)},typeof(Program).Module,true);var il=method.GetILGenerator();
foreach(var instruction in generated){if(instruction.operand is MethodInfo target)il.Emit(instruction.opcode,target);else il.Emit(instruction.opcode);}
LogicalButtonBase.Focus=(Func<LogicalButtonBase,bool>)method.CreateDelegate(typeof(Func<LogicalButtonBase,bool>));
void Reject(IEnumerable<CodeInstruction> list,string name){try{BackgroundTasLogicalInputFocus.Transpiler(list).ToArray();throw new Exception("accepted");}catch(InvalidOperationException){checks.Add(name);}}
Reject(new[]{new CodeInstruction(OpCodes.Ret)},"changed native body without focus getter rejected");
Reject(new[]{new CodeInstruction(OpCodes.Call,getter),new CodeInstruction(OpCodes.Call,getter)},"multiple native focus reads rejected");
var owner=new GameObject();EntitySerialisationRegistry.Ids[owner]=103;var physical=new Physical();
var device=(TASLogicalButton)TASLogicalButton.GetOrCreate(103,TASLogicalButtonType.Pickup,physical,owner);
bool open=true;int callbacks=0;var gate=new GateLogicalButton(device,()=>{callbacks++;return open;});TASLogicalButton.ObserveNativeGate(device,gate);
var outer=new GateLogicalButton(gate,()=>open);TASLogicalButton.ObserveNativeGate(gate,outer);
void Pad(bool down,int id=103){TASLogicalButton.ApplyInputFrame(new InputData{Input=new(){{id,new ChefInput{Pickup=new Button{Down=down}}}}});}
Application.isFocused=false;Check(!LogicalButtonBase.Focus(device),"inactive emulation rejected");Pad(false);
bool enabled=Environment.GetEnvironmentVariable("OC2SC_BACKGROUND_INPUT")!="0";
if(!enabled){Check(!LogicalButtonBase.Focus(device)&&!LogicalButtonBase.Focus(gate),"opt-out retains unfocused native rejection for both device and gate");Application.isFocused=true;Check(LogicalButtonBase.Focus(device),"opt-out retains actual foreground behavior");}
else {
 Check(LogicalButtonBase.Focus(device)&&LogicalButtonBase.Focus(gate)&&LogicalButtonBase.Focus(outer),"active local TAS device and both observed gate layers qualify");
 Check(callbacks==0,"focus verification never invokes native gate callback");
 Check(!LogicalButtonBase.Focus(physical),"ordinary physical button retains unfocused rejection");
 Check(!LogicalButtonBase.Focus(new GateLogicalButton(device,()=>true)),"unobserved gate rejected even around a virtual device");
 var foreign=new GateLogicalButton(physical,()=>true);TASLogicalButton.ObserveNativeGate(device,foreign);Check(!LogicalButtonBase.Focus(foreign),"gate whose native child differs from observed source rejected");
 Pad(false);gate.HasUnclaimedPressEvent();outer.HasUnclaimedPressEvent();Time.time=10;Pad(true);
 Check(gate.JustPressed()&&!gate.JustPressed(),"unfocused virtual press accepted exactly once by native gate history");
 Check(outer.JustPressed()&&!outer.JustPressed(),"nested native gate keeps its own single-consumption claim");
 Time.time=12;Check(gate.GetHeldTimeLength()==2,"native held duration remains unchanged");Pad(true);Check(!gate.JustPressed(),"repeated down input cannot manufacture another press");
 Pad(false);Check(gate.JustReleased()&&!gate.JustReleased(),"native release is consumed once");
 open=false;Pad(true);Check(!gate.IsDown()&&!gate.JustPressed(),"native menu/direct-control callback still suppresses the virtual button");open=true;
 owner.Provider.Local=false;Check(!LogicalButtonBase.Focus(device)&&!LogicalButtonBase.Focus(gate),"remote chef rejected");owner.Provider.Local=true;
 owner.Provider=null;Check(!LogicalButtonBase.Focus(gate),"missing native player provider rejected");owner.Provider=new();
 EntitySerialisationRegistry.Ids[owner]=999;Check(!LogicalButtonBase.Focus(gate),"changed registry owner rejected");EntitySerialisationRegistry.Ids[owner]=103;
 owner.InstanceId++;Check(!LogicalButtonBase.Focus(gate),"changed chef incarnation rejected");owner.InstanceId--;
 Pad(false,104);Check(!LogicalButtonBase.Focus(device),"absent active pad rejected");Pad(false);
 typeof(GateLogicalButton).GetField("m_childButton",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(gate,physical);
 Check(!LogicalButtonBase.Focus(gate)&&!LogicalButtonBase.Focus(outer),"mutated child rejects direct and nested gate chains");
 typeof(GateLogicalButton).GetField("m_childButton",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(gate,device);
 var successor=new GameObject();EntitySerialisationRegistry.Ids[successor]=103;TASLogicalButton.GetOrCreate(103,TASLogicalButtonType.Pickup,physical,successor);
 Check(!LogicalButtonBase.Focus(device)&&!LogicalButtonBase.Focus(outer),"stale device and wrappers rejected after same-ID chef replacement");
 TASLogicalButton.ResetAll();Check(!LogicalButtonBase.Focus(device),"disconnect neutral reset removes pad eligibility");
 Application.isFocused=true;Check(LogicalButtonBase.Focus(physical),"ordinary focused physical-button behavior retained");
}
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,checks=checks.Count,names=checks,enabled,gameSha256=Hash(game),pluginSha256=Hash(plugin),sourceSha256=new[]{"framework/patch/BackgroundTasInputFocus.cs","framework/patch/TASLogicalButton.cs"}.ToDictionary(p=>p,Hash),scope="Installed IL plus actual production transpiler/device guard tested against native logical-history semantics. No native game execution or successful pickup claim."}));
class Physical:LogicalButtonBase {public bool Down;public override bool IsDown()=>Down;}
