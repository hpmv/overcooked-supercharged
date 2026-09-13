using System.Security.Cryptography;
using System.Text.Json;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SuperchargedPatch;
using UnityEngine;
using Obj=UnityEngine.Object;
var checks=new List<string>();void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
int Count<T>()=>Obj.Calls.GetValueOrDefault(typeof(T));
var flow=new ServerKitchenFlowControllerBase{Id=107};var cannon=new ServerCannon{Id=84};var chef=new ClientPlayerControlsImpl_Default{Id=103};
var cooking=new ServerCookingStation{Id=17,Value=1};var mixing=new ServerMixingStation{Id=14};var other=new Unrelated{Id=999};
Obj.Queries[typeof(MonoBehaviour)]=new Obj[]{other,cannon,cooking,chef,flow,mixing};
Obj.Queries[typeof(ServerKitchenFlowControllerBase)]=new Obj[]{flow};Obj.Queries[typeof(ServerCannon)]=new Obj[]{cannon};
Obj.Queries[typeof(ClientPlayerControlsImpl_Default)]=new Obj[]{chef};Obj.Queries[typeof(ServerCookingStation)]=new Obj[]{cooking};Obj.Queries[typeof(ServerMixingStation)]=new Obj[]{mixing};
Check(NativeCheckpointComponentBatch.Find<ServerCannon>().Single()==cannon&&Count<ServerCannon>()==1,"outside capture preserves actual typed native query");
for(int i=0;i<4;i++) {
 NativeCheckpointComponentBatch.Begin();
 Check(NativeCheckpointComponentBatch.FindFlow()==flow,"actual flow from membership batch "+i);
 Check(NativeCheckpointComponentBatch.Find<ServerCannon>().Single()==cannon&&NativeCheckpointComponentBatch.Find<ClientPlayerControlsImpl_Default>().Single()==chef,"exact cannon/chef identity "+i);
 Check(NativeCheckpointComponentBatch.Find<ServerCookingStation>().Single()==cooking&&NativeCheckpointComponentBatch.Find<ServerMixingStation>().Single()==mixing,"exact cooking/mixing identity "+i);
 NativeCheckpointComponentBatch.End();
}
Check(Count<MonoBehaviour>()==4&&Count<ServerCannon>()==4&&Count<ServerCookingStation>()==3,"one broad query per capture; only first3 typed validation queries");
var newer=new ServerCookingStation{Id=27,Value=3};Obj.Queries[typeof(MonoBehaviour)]=new Obj[]{newer};
NativeCheckpointComponentBatch.Begin();Check(NativeCheckpointComponentBatch.Find<ServerCookingStation>().Single()==newer,"new callback rediscovers changed membership after validation budget");newer.Value=4;Check(NativeCheckpointComponentBatch.Find<ServerCookingStation>().Single().Value==4,"native field remains live; membership batch does not cache state");NativeCheckpointComponentBatch.End();
NativeSceneMetadata.Refreshes++;
Obj.Queries[typeof(MonoBehaviour)]=new Obj[]{newer};Obj.Queries[typeof(ServerCookingStation)]=new Obj[]{cooking};
NativeCheckpointComponentBatch.Begin();Check(NativeCheckpointComponentBatch.Find<ServerCookingStation>().Single()==cooking,"membership mismatch immediately returns original typed query");NativeCheckpointComponentBatch.End();
Obj.Queries[typeof(ServerCookingStation)]=new Obj[]{newer};
int before=Count<ServerCookingStation>();NativeCheckpointComponentBatch.Begin();Check(NativeCheckpointComponentBatch.Find<ServerCookingStation>().Single()==newer&&Count<ServerCookingStation>()==before+1,"refused type continues native fallback on next callback");NativeCheckpointComponentBatch.End();
NativeSceneMetadata.Refreshes++;Obj.Queries[typeof(MonoBehaviour)]=new Obj[]{newer};NativeCheckpointComponentBatch.Begin();NativeCheckpointComponentBatch.Find<ServerCookingStation>();NativeCheckpointComponentBatch.End();
var report=(Dictionary<string,object>)NativeCheckpointComponentBatch.Diagnostics();Check(((string[])report["refusedTypes"]).Length==0,"fresh scene generation revalidates previous refusal");
var secondFlow=new ServerKitchenFlowControllerBase{Id=108};Obj.Queries[typeof(MonoBehaviour)]=new Obj[]{flow,secondFlow};Obj.Queries[typeof(ServerKitchenFlowControllerBase)]=new Obj[]{secondFlow,flow};
NativeSceneMetadata.Refreshes++;NativeCheckpointComponentBatch.Begin();Check(NativeCheckpointComponentBatch.FindFlow()==secondFlow,"ambiguous flow retains original singular native ordering");NativeCheckpointComponentBatch.End();
NativeCheckpointComponentBatch.Begin();try{NativeCheckpointComponentBatch.Begin();throw new Exception("accepted nested");}catch(InvalidOperationException){checks.Add("nested batch rejected without replacing active membership");}finally{NativeCheckpointComponentBatch.End();}
Check(!(bool)((Dictionary<string,object>)NativeCheckpointComponentBatch.Diagnostics())["active"],"End drops every captured component reference");
string candidatePath=args.Length>0?args[0]:"artifacts/framework-component-batch-check/SuperchargedPatch.dll";
using var old=AssemblyDefinition.ReadAssembly("artifacts/framework-plugin-native-v/SuperchargedPatch.dll");using var current=AssemblyDefinition.ReadAssembly(candidatePath);
TypeDefinition Type(AssemblyDefinition a,string name)=>a.MainModule.Types.Single(t=>t.Name==name);
MethodDefinition Method(TypeDefinition t,string name)=>t.Methods.Single(m=>m.Name==name);
string[] Calls(MethodDefinition m)=>m.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>{var r=(MethodReference)i.Operand;return r.DeclaringType.Name=="NativeCheckpointComponentBatch"?"DISCOVERY":r.DeclaringType.Name=="Object"&&(r.Name=="FindObjectOfType"||r.Name=="FindObjectsOfType")?"DISCOVERY":r.FullName;}).ToArray();
string[] Writes(MethodDefinition m)=>m.Body.Instructions.Where(i=>i.OpCode.Code is Code.Stfld or Code.Stsfld).Select(i=>i.Operand.ToString()).ToArray();
foreach(var pair in new[]{("NativeChefInteractionCheckpoint","CaptureAll"),("NativeStationSyncCheckpoint","Capture"),("NativeCannonCheckpoint","CaptureAll"),("NativeKitchenCheckpoint","CaptureFrameCore")}) {
 var a=Method(Type(old,pair.Item1),pair.Item2);var b=Method(Type(current,pair.Item1),pair.Item2);
 Check(Calls(a).SequenceEqual(Calls(b)),pair.Item1+" preserves all original calls/order except membership discovery");
 Check(Writes(a).SequenceEqual(Writes(b)),pair.Item1+" preserves original native/checkpoint field writes");
}
var wrapper=Method(Type(current,"NativeKitchenCheckpoint"),"CaptureFrame");
Check(wrapper.Body.ExceptionHandlers.Any(h=>h.HandlerType==ExceptionHandlerType.Finally&&wrapper.Body.Instructions.Where(i=>i.Offset>=h.HandlerStart.Offset&&i.Offset<h.HandlerEnd.Offset).Any(i=>i.Operand is MethodReference r&&r.DeclaringType.Name=="NativeCheckpointComponentBatch"&&r.Name=="End")),"compiled CaptureFrame clears membership inside finally on early return/error");
Check(wrapper.Body.Instructions.Count(i=>i.Operand is MethodReference r&&r.DeclaringType.Name=="NativeCheckpointComponentBatch"&&r.Name=="Begin")==1,"compiled CaptureFrame begins only one membership discovery");
var batchType=Type(current,"NativeCheckpointComponentBatch");Check(Method(batchType,"End").Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldnull),"compiled End releases array reference");
string Hash(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,checks=checks.Count,names=checks,candidateSha256=Hash(candidatePath),baselineSha256=Hash("artifacts/framework-plugin-native-v/SuperchargedPatch.dll"),scope="Actual production discovery helper with synthetic native-query identities; full installed-target CLR2 compiled call/write-order and finally inspection. Native speed and exact membership proofs await W."}));
