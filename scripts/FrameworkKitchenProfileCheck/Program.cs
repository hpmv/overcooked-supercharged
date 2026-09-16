using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

string baselinePath="artifacts/framework-plugin-native-u/SuperchargedPatch.dll";
string candidatePath="artifacts/framework-kitchen-profile-check/SuperchargedPatch.dll";
using var baseline=AssemblyDefinition.ReadAssembly(baselinePath);
using var candidate=AssemblyDefinition.ReadAssembly(candidatePath);
TypeDefinition Subject(AssemblyDefinition assembly)=>assembly.MainModule.Types.Single(t=>t.Name=="NativeKitchenCheckpoint");
var before=Subject(baseline);var after=Subject(candidate);var checks=new List<string>();
void Check(bool condition,string message){if(!condition)throw new Exception(message);checks.Add(message);}
MethodDefinition Method(TypeDefinition type,string name)=>type.Methods.Single(m=>m.Name==name);
string Normal(string text){text=text.Replace("CaptureCore","Capture").Replace("SameGameplayBoundaryCore","SameGameplayBoundary").Replace("CaptureFrameCore","CaptureFrame");return Regex.Replace(text,@"(<>c__DisplayClass|<>9__|[bg]__|<Capture>)[0-9]+_[0-9]+","$1N");}
bool IsTiming(MethodReference m)=>m.DeclaringType.Name=="NativeKitchenCheckpoint"&&(m.Name=="Timestamp"||m.Name=="RecordTiming"||m.Name=="EndTiming");
string[] Calls(MethodDefinition method)=>method.Body.Instructions.Where(i=>i.Operand is MethodReference m&&!IsTiming(m)).Select(i=>Normal(((MethodReference)i.Operand).FullName)).ToArray();
string[] Writes(MethodDefinition method)=>method.Body.Instructions.Where(i=>i.OpCode.Code is Code.Stfld or Code.Stsfld).Select(i=>Normal(((FieldReference)i.Operand).FullName)).ToArray();
foreach(var names in new[]{("CaptureFrame","CaptureFrameCore"),("Capture","CaptureCore"),("SameGameplayBoundary","SameGameplayBoundaryCore")}) {
 var old=Method(before,names.Item1);var next=Method(after,names.Item2);
 var a=Calls(old);var b=Calls(next);
 Check(a.SequenceEqual(b),names.Item1+" preserves every original operation call and its order: "+string.Join(" | ",a.Except(b).Concat(b.Except(a))));
 Check(Writes(old).SequenceEqual(Writes(next)),names.Item1+" preserves original state field writes and order");
}
string[] Fields(TypeDefinition type)=>type.Fields.Select(f=>f.Name+":"+f.FieldType.FullName).ToArray();
Check(Fields(before.NestedTypes.Single(t=>t.Name=="Snapshot")).SequenceEqual(Fields(after.NestedTypes.Single(t=>t.Name=="Snapshot"))),"snapshot field layout and evidence are unchanged");
Check(Calls(Method(before,"Prepare")).SequenceEqual(Calls(Method(after,"Prepare"))),"all native preflight checks retain operation order");
Check(Writes(Method(before,"Prepare")).SequenceEqual(Writes(Method(after,"Prepare"))),"preflight state effects unchanged");
var oldRestore=before.NestedTypes.Single(t=>t.Name=="RestorePlan");var newRestore=after.NestedTypes.Single(t=>t.Name=="RestorePlan");
foreach(var name in new[]{"Complete","RestoreClocks","RestoreFixedBodyPoses","RestoreClientPresentation"}) {
 Check(Calls(Method(oldRestore,name)).SequenceEqual(Calls(Method(newRestore,name))),name+" native restore/check order unchanged");
}
foreach(var names in new[]{("CaptureFrame","CaptureFrameCore"),("Capture","CaptureCore"),("SameGameplayBoundary","SameGameplayBoundaryCore")}) {
 var wrapper=Method(after,names.Item1);
 Check(wrapper.Body.ExceptionHandlers.Count==1&&wrapper.Body.ExceptionHandlers[0].HandlerType==ExceptionHandlerType.Finally,names.Item1+" includes every return/error in total elapsed time");
 Check(wrapper.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name==names.Item2)==1,names.Item1+" calls original body exactly once");
 Check(wrapper.Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.Name=="RecordTiming")==1,names.Item1+" records one total per invocation");
}
var timing=Method(after,"RecordTiming");
Check(Calls(timing).Length==0,"counter accumulation cannot call native APIs or gameplay clocks");
Check(timing.Body.Instructions.All(i=>i.OpCode.Code!=Code.Newobj),"timing accumulator allocates no objects per stage");
Check(Method(after,"Timestamp").Body.Instructions.Count(i=>i.Operand is MethodReference m&&m.DeclaringType.FullName=="System.Diagnostics.Stopwatch"&&m.Name=="GetTimestamp")==1,"timestamps come from monotonic Stopwatch only");
Check(Method(after,"CaptureTimingDiagnostics").Body.Instructions.All(i=>i.OpCode.Code!=Code.Stsfld&&i.OpCode.Code!=Code.Stelem_I8),"profiling diagnostics reads counters without resetting them");
var stages=after.NestedTypes.Single(t=>t.Name=="CaptureStage").Fields.Where(f=>f.HasConstant&&f.Name!="Count").Select(f=>f.Name).ToArray();
Check(stages.Length==14&&new[]{"FlowFind","RoundDiagnostics","BaseKitchenClockInput","Cannons","ChefInteractions","StationSync","FixedBodyPoses","FixedAttachmentPoses","RemainingOrdersUi","SameGameplayBoundary"}.All(stages.Contains),"all requested substage counters are compiled");
Check(Method(after,"Diagnostics").Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldstr&&(string)i.Operand=="requestedBodyCheckpoint")&&Method(after,"Diagnostics").Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldstr&&(string)i.Operand=="kitchenCaptureStages"),"new profiling and existing requested checkpoint diagnostics coexist");
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,checks=checks.Count,names=checks,baselineAssemblySha256=Hash(baselinePath),candidateAssemblySha256=Hash(candidatePath),sourceSha256=Hash("framework/patch/NativeKitchenCheckpoint.cs"),stages,scope="Installed-target CLR2 full-source compile and compiled-IL operation-order/field-layout checks. Profiling counters await native observations; no game calls or timing gain claim."}));
