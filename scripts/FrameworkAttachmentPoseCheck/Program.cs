using SuperchargedPatch;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using Mono.Cecil;
using System.Text.Json;
using System.Security.Cryptography;

var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Reject(Action action,string name){try{action();}catch(InvalidOperationException){checks.Add(name);return;}throw new Exception("Expected rejection: "+name);}
EntitySerialisationEntry Add(int id){var e=new EntitySerialisationEntry{m_GameObject=new GameObject()};e.m_Header.m_uEntityID=(uint)id;EntitySerialisationRegistry.m_EntitiesList._items.Add(e);return e;}
var source=Add(38);var target=Add(32);var entry=Add(10);var bodyEntry=Add(119);
source.m_GameObject.transform.localPosition=new(19.2f,.5f,-15.599998f);
target.m_GameObject.transform.localPosition=new(19.2f,.5f,-10.799999f);
var obj=entry.m_GameObject;var physical=obj.Add<PhysicalAttachment>();physical.m_container=bodyEntry.m_GameObject.Add<Rigidbody>();
var server=obj.Add<ServerPhysicalAttachment>();var client=obj.Add<ClientPhysicalAttachment>();obj.Add<EmptyLerp>();obj.transform.parent=source.m_GameObject.transform;
var ids=new HashSet<int>{10};var saved=NativeAttachmentPoseCheckpoint.Capture(ids);
Check(saved.Length==1&&saved[0].EntityId==10,"fixed logical item selected independently of its registered body");
Check(saved[0].WorldPosition.z==-15.599998f&&saved[0].LocalPosition.z==0,"actual local and world poses retained separately");
// The actual S mismatch: native parent restored, but logical pose remains at the future counter.
obj.transform.parent=target.m_GameObject.transform;
NativeAttachmentPoseCheckpoint.Validate(saved);Check(true,"preflight allows future current parent and retains captured target identity");
Events.Rows.Clear();Reject(()=>NativeAttachmentPoseCheckpoint.Restore(saved),"restore requires native callbacks to establish captured parent");
Check(Events.Rows.Count==0,"wrong parent cannot cause partial pose writes");
obj.transform.parent=source.m_GameObject.transform;obj.transform.localPosition=new(0,0,4.799999f);obj.transform.localRotation=new(0,.214f,0,.976f);obj.transform.localScale=new(2,3,4);
physical.m_container.position=new(77,88,99);Events.Rows.Clear();
NativeAttachmentPoseCheckpoint.Restore(saved);NativeAttachmentPoseCheckpoint.VerifyRestored(saved);
Check(Events.Rows.SequenceEqual(new[]{"position","rotation","scale"}),"same native item restores three observed local fields only");
Check(obj.transform.position.z==-15.599998f&&physical.m_container.position.x==77,"logical pose restores without touching already owned body state");
Events.Rows.Clear();NativeAttachmentPoseCheckpoint.Restore(saved);Check(Events.Rows.Count==0,"matching native poses cause no redundant setters");
var extra=Add(200);Check(NativeAttachmentPoseCheckpoint.Capture(ids).Length==1,"dynamic objects never enter initial fixed set");
Reject(()=>NativeAttachmentPoseCheckpoint.Capture(new(){10,999}),"missing initial fixed attachment is not inferred");
Reject(()=>NativeAttachmentPoseCheckpoint.Capture(new(){38}),"ordinary counter cannot be treated as physical attachment");
source.m_GameObject.Destroyed=true;Reject(()=>NativeAttachmentPoseCheckpoint.Validate(saved),"destroyed captured parent rejects preflight");source.m_GameObject.Destroyed=false;
var oldParentEntry=source;EntitySerialisationRegistry.m_EntitiesList._items.Remove(source);var replaced=new EntitySerialisationEntry{m_GameObject=source.m_GameObject};replaced.m_Header.m_uEntityID=38;EntitySerialisationRegistry.m_EntitiesList._items.Add(replaced);
Reject(()=>NativeAttachmentPoseCheckpoint.Validate(saved),"same ID and object under a successor registration cannot satisfy captured parent");EntitySerialisationRegistry.m_EntitiesList._items.Remove(replaced);EntitySerialisationRegistry.m_EntitiesList._items.Add(source);
var oldContainer=physical.m_container;physical.m_container=new GameObject().Add<Rigidbody>();Reject(()=>NativeAttachmentPoseCheckpoint.Validate(saved),"replaced physical container rejects");physical.m_container=oldContainer;
obj.components.Remove(server);Reject(()=>NativeAttachmentPoseCheckpoint.Validate(saved),"replaced native attachment component rejects");obj.components.Add(server);
server.SetPrediction(true);client.SetPrediction(true);var predicted=NativeAttachmentPoseCheckpoint.Capture(ids);NativeAttachmentPoseCheckpoint.Validate(predicted);Check(predicted[0].ServerPredictionMode&&predicted[0].ClientPredictionMode&&predicted[0].Prediction==null,"T idle counter mode flags with null FSM remain valid");
server.SetPrediction(false);client.SetPrediction(false);NativeAttachmentPoseCheckpoint.Restore(predicted);NativeAttachmentPoseCheckpoint.VerifyRestored(predicted);Check(true,"exact native prediction modes restore without invoking any predictor");
NativeAttachmentPoseCheckpoint.Restore(saved);NativeAttachmentPoseCheckpoint.VerifyRestored(saved);Check(true,"opposite saved prediction modes also restore exactly");
client.m_Prediction=new UnknownPrediction();var unknown=NativeAttachmentPoseCheckpoint.Capture(ids);Check(unknown.Length==1,"unsupported predictor capture does not break ordinary bookkeeping");Reject(()=>NativeAttachmentPoseCheckpoint.Validate(unknown),"unknown native predictor target rejected");Events.Rows.Clear();Reject(()=>NativeAttachmentPoseCheckpoint.Restore(saved),"unknown current predictor must be retired by native callbacks");Check(Events.Rows.Count==0,"unsupported post-callback predictor causes no pose writes");client.m_Prediction=null;
var conveyor=new ConveyorPrediction{m_Transform=obj.transform};conveyor.SetState(.25f,true);client.m_Prediction=conveyor;var emptyQueue=NativeAttachmentPoseCheckpoint.Capture(ids);NativeAttachmentPoseCheckpoint.Validate(emptyQueue);Check(true,"actual empty native conveyor queue can be captured and validated");conveyor.SetState(9,false);NativeAttachmentPoseCheckpoint.Restore(emptyQueue);NativeAttachmentPoseCheckpoint.VerifyRestored(emptyQueue);Check(ReferenceEquals(client.m_Prediction,conveyor),"empty predictor native object identity and latent fields restore exactly");conveyor.m_Destinations.Add(38);var moving=NativeAttachmentPoseCheckpoint.Capture(ids);Reject(()=>NativeAttachmentPoseCheckpoint.Validate(moving),"nonempty native queue remains unsupported despite mode flag");Events.Rows.Clear();Reject(()=>NativeAttachmentPoseCheckpoint.Restore(emptyQueue),"pending current destination must be retired before restore");Check(Events.Rows.Count==0,"live queue rejection performs no transform mutations");conveyor.m_Destinations.Clear();client.m_Prediction=null;
physical.m_meshLerper=obj.Add<MeshLerper>();NativeAttachmentPoseCheckpoint.Validate(NativeAttachmentPoseCheckpoint.Capture(ids));Check(true,"inactive cached mesh lerper is harmless");physical.ActiveMesh=true;Reject(()=>NativeAttachmentPoseCheckpoint.Validate(NativeAttachmentPoseCheckpoint.Capture(ids)),"active mesh interpolation rejected");physical.ActiveMesh=false;
var basic=obj.Add<BasicLerp>();Reject(()=>NativeAttachmentPoseCheckpoint.Validate(NativeAttachmentPoseCheckpoint.Capture(ids)),"stateful world interpolation not reset or guessed");obj.components.Remove(basic);
client.Attached=false;var transitioning=NativeAttachmentPoseCheckpoint.Capture(ids);Reject(()=>NativeAttachmentPoseCheckpoint.Validate(transitioning),"target native attachment transition rejects");Events.Rows.Clear();Reject(()=>NativeAttachmentPoseCheckpoint.Restore(saved),"post-callback held flags must match target");Check(Events.Rows.Count==0,"held-state mismatch remains mutation-free");client.Attached=true;
obj.transform.localPosition=new(float.NaN,0,0);var nonfinite=NativeAttachmentPoseCheckpoint.Capture(ids);Reject(()=>NativeAttachmentPoseCheckpoint.Validate(nonfinite),"nonfinite target recorded as unsupported, never synthesized");obj.transform.localPosition=saved[0].LocalPosition;
obj.transform.WrongWorld=new(0,0,777);Reject(()=>NativeAttachmentPoseCheckpoint.VerifyRestored(saved),"world pose mismatch remains visible after local restoration");obj.transform.WrongWorld=null;
obj.transform.WrongRotation=new(0,1,0,0);Reject(()=>NativeAttachmentPoseCheckpoint.VerifyRestored(saved),"world quaternion mismatch rejects exact postcondition");obj.transform.WrongRotation=null;
var orphan=new GameObject();obj.transform.parent=orphan.transform;Reject(()=>NativeAttachmentPoseCheckpoint.Validate(NativeAttachmentPoseCheckpoint.Capture(ids)),"unregistered parent ancestry cannot be invented");obj.transform.parent=source.m_GameObject.transform;
EntitySerialisationRegistry.m_EntitiesList._items.Add(entry);Reject(()=>NativeAttachmentPoseCheckpoint.Capture(ids),"duplicate fixed native membership rejects");EntitySerialisationRegistry.m_EntitiesList._items.RemoveAt(EntitySerialisationRegistry.m_EntitiesList.Count-1);
var diagnostic=(Dictionary<string,object>)NativeAttachmentPoseCheckpoint.Diagnostics(saved);Check(((object[])diagnostic["attachments"]).Length==1,"diagnostics retain explicit logical-pose provenance");

string nativePath="runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll";
using var native=AssemblyDefinition.ReadAssembly(nativePath);
TypeDefinition Type(string name)=>native.MainModule.Types.Single(t=>t.Name==name);
MethodDefinition Method(string type,string name)=>Type(type).Methods.Single(m=>m.Name==name);
string[] Calls(string type,string method)=>Method(type,method).Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>((MethodReference)i.Operand).FullName).ToArray();
Check(Type("ServerPhysicalAttachment").Fields.Any(f=>f.Name=="m_bIsClientSidePredicted"&&f.FieldType.FullName=="System.Boolean")&&Type("ClientPhysicalAttachment").Fields.Any(f=>f.Name=="m_bClientSidePredicted"&&f.FieldType.FullName=="System.Boolean"),"installed exact prediction field types verified");
Check(Calls("ServerPhysicalAttachment","Attach").Any(c=>c.Contains("HasClientSidePrediction"))&&Calls("ServerPhysicalAttachment","Attach").Any(c=>c.Contains("set_localPosition")),"installed native attach owns parent placement and prediction admission");
Check(Method("ClientPhysicalAttachment","UpdateSynchronising").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_Prediction")&&Method("ClientPhysicalAttachment","UpdateSynchronising").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_bClientSidePredicted"),"installed client update gates on both actual predictor and mode flag");
Check(Type("ConveyorPrediction").Fields.Any(f=>f.Name=="m_Destinations")&&Type("ConveyorPrediction").Fields.Any(f=>f.Name=="m_RemainingMove"&&f.FieldType.FullName=="System.Single")&&Type("ConveyorPrediction").Fields.Any(f=>f.Name=="m_bCalculatedDistance"&&f.FieldType.FullName=="System.Boolean"),"installed empty conveyor native queue and latent field layout verified");
Check(Calls("ClientWorldObjectSynchroniser","DoReparenting").Any(c=>c.Contains("Transform::SetParent"))&&Calls("ClientWorldObjectSynchroniser","DoReparenting").Any(c=>c.Contains("Lerp::Reparented")),"installed client reparent owns logical parenting and lerp notification");
Check(new[]{"UpdateLerp","Reparented","ReceiveServerEvent"}.All(m=>Method("EmptyLerp",m).Body.Instructions.All(i=>i.OpCode.Name is "ret" or "nop")),"installed EmptyLerp has no stateful sliding or repair behavior");
Check(Calls("PhysicalAttachment","GetFakeMeshActive").Length==0&&Method("PhysicalAttachment","GetFakeMeshActive").Body.Instructions.Any(i=>i.Operand is FieldReference f&&f.Name=="m_fakeMeshActive"),"installed mesh-active getter only reads actual native flag");

string witness="artifacts/framework-migration/native-s/plate-search/observations.json";
using var evidence=JsonDocument.Parse(File.ReadAllText(witness));var all=evidence.RootElement;
JsonElement Response(string name)=>all.EnumerateArray().Single(r=>r.GetProperty("label").GetString()==name).GetProperty("response");
JsonElement Item(JsonElement response,int id)=>response.GetProperty("entities").EnumerateArray().Single(e=>e.GetProperty("id").GetInt32()==id);
var before=Item(Response("base"),10);var after=Item(Response("candidate-1-restored"),10);var terminal=Item(Response("chef-103-walk-end"),10);
Check(before.GetProperty("data").GetProperty("attachmentParent").GetProperty("path")[0].GetInt32()==38&&after.GetProperty("data").GetProperty("attachmentParent").GetProperty("path")[0].GetInt32()==38,"unchanged S witness proves restored same exact source attachment");
Check(before.GetProperty("position").GetProperty("z").GetDouble()!=after.GetProperty("position").GetProperty("z").GetDouble()&&terminal.GetProperty("position").GetRawText()==after.GetProperty("position").GetRawText(),"unchanged S witness pins future logical pose surviving ACK");
var components=Response("base").GetProperty("registry").EnumerateArray().Single(e=>e.GetProperty("EntityId").GetInt32()==10).GetProperty("Components").EnumerateArray().Select(x=>x.GetString()).ToArray();
Check(components.Contains("PhysicalAttachment")&&components.Contains("EmptyLerp")&&!components.Contains("Rigidbody")&&!components.Contains("BasicLerp"),"actual plate10 has EmptyLerp and no own Rigidbody, not assumed mesh interpolation");
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,checks=checks.Count,names=checks,nativeAssemblySha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath))).ToLowerInvariant(),witnessSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(witness))).ToLowerInvariant(),scope="Pure authoring transform fixtures, installed native IL, unchanged native-S witness. No game calls; native restore/continuation verification pending."}));
