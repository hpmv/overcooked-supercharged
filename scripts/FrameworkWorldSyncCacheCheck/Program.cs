using System.Reflection;
using System.Collections;
using System.Security.Cryptography;
using System.Text.Json;
using Hpmv;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SuperchargedPatch;
using SuperchargedPatch.Authoring.Modules;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

var checks=new List<string>();
void Check(bool ok,string name){if(!ok)throw new Exception(name);checks.Add(name);}
void Reject(Action a,string name){try{a();}catch(InvalidOperationException){checks.Add(name);return;}throw new Exception("Missing rejection: "+name);}
void Set(object o,string n,object v)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).SetValue(o,v);
object Get(object o,string n)=>o.GetType().GetField(n,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public).GetValue(o);
void AddPlanRemoval(NativeDynamicWarpPlan plan,GameObject owner,GameObject container){
 var removalType=plan.GetType().GetNestedType("Removal",BindingFlags.NonPublic);
 var removal=Activator.CreateInstance(removalType);
 Set(removal,"Id",(int)EntitySerialisationRegistry.GetId(owner));Set(removal,"ContainerId",(int)EntitySerialisationRegistry.GetId(container));
 Set(removal,"Object",owner);Set(removal,"Container",container);
 var removals=Get(plan,"removals");removals.GetType().GetMethod("Add").Invoke(removals,new[]{removal});
}
Dictionary<string,object> Status(WorldSyncCacheModule m)=>(Dictionary<string,object>)m.Invoke("status",new());
(WorldSyncCacheModule m,ServerWorldObjectSynchroniser s,GameObject parent) Setup(bool preexisting=false){
 NativeKitchenCheckpoint.Reset();NativeSceneMetadata.InitialPhysicalAttachmentIds.Clear();EntitySerialisationRegistry.entries.Clear();
 NativeInitialAttachmentDeletionAuthorization.ResetFixture();
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{51,52,53});
 var parent=new GameObject();var parentComponent=parent.Add<AttachStation>();var obj=new GameObject();obj.transform.parent=parent.transform;obj.Add<PhysicalAttachment>();var sync=obj.Add<ServerWorldObjectSynchroniser>();sync.Setup();obj.Add<ClientWorldObjectSynchroniser>().Setup(parentComponent,41);
 var entry=new EntitySerialisationEntry {m_GameObject=obj};entry.m_Header.m_uEntityID=12;entry.m_ServerSynchronisedComponents._items.Add(sync);
 EntitySerialisationRegistry.entries.Add(12,entry);NativeSceneMetadata.InitialPhysicalAttachmentIds.Add(12);
 var parentEntry=new EntitySerialisationEntry{m_GameObject=parent};parentEntry.m_Header.m_uEntityID=41;EntitySerialisationRegistry.entries.Add(41,parentEntry);
 ControllerHandler.MultiplayerController=new();DebugManager.Instance.fast=true;EntitySerialisationRegistry.HasUrgentOutgoingUpdates=false;
 UnityEngine.Time.time=100;UnrealTimePatch.LogicalTime=100;TimeManager.paused=true;Hpmv.Injector.Server.CurrentInput=null;
 var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");((FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList"))._items.Add(entry);
 if(preexisting)NativeKitchenCheckpoint.CaptureFrame(161);
 var m=new WorldSyncCacheModule();m.Invoke("activate",new());return(m,sync,parent);
}
void Capture(int f){Hpmv.Injector.Server.CurrentFrameData.FrameNumber=f;WorldSyncCacheModule.BeforeCaptureFrame(f,out var prior);NativeKitchenCheckpoint.CaptureFrame(f);WorldSyncCacheModule.AfterCaptureFrame(f,prior);}
void PlainResumeInput(){Hpmv.Injector.Server.CurrentInput=new(){RequestResume=true,GameSpeed=1000};Hpmv.Injector.Server.CurrentInput.__isset.gameSpeed=true;}
void Prepare(int f)=>WorldSyncCacheModule.BeforePrepare(new(){Frame=f});
void Restore(int f){var p=NativeKitchenCheckpoint.Prepare(new(){Frame=f});p.Complete();WorldSyncCacheModule.AfterComplete(p);}
Check(!WorldSyncCacheModule.IsSupportedCacheLifecycle(false,true,true,true,true,true,false,false),
 "Active pending-rest WorldObject remains rejected without exact local dynamic recreation");
Check(WorldSyncCacheModule.IsSupportedCacheLifecycle(false,true,true,true,true,true,false,true),
 "Exact local dynamic recreation admits its active pending-rest cache");
Check(!WorldSyncCacheModule.IsSupportedCacheLifecycle(false,true,true,true,true,false,false,true),
 "Local dynamic recreation still rejects a pending cache without parent-change state");
Check(!WorldSyncCacheModule.IsSupportedCacheLifecycle(false,true,true,true,false,true,false,true),
 "Local dynamic recreation still rejects disabled position synchronization");
Check(!WorldSyncCacheModule.IsSupportedCacheLifecycle(false,true,true,true,true,true,true,true),
 "Local dynamic recreation still rejects a paused synchronizer lifecycle");
{
 var(m,s,p)=Setup(preexisting:true);Capture(161);
 Check((int)Status(m)["frames"]==0,"Late activation does not invent caches for an existing immutable native snapshot");
 Reject(()=>Prepare(161),"Pre-activation target rejects despite a currently settled native cache");
 Capture(162);Prepare(162);Check((int)Status(m)["frames"]==1,"Actual newly inserted native boundary is admitted after activation");
 m.Dispose();
}
void ReturnedStackRecreation(int physicalParentMode)
{
 // A returned stack can disappear while its plate/body pair survives.  Its
 // logical path is a dynamic ordinal, not the historical native owner ID, so
 // admit it only through the exact saved station -> stack -> child topology.
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{53,54,55,56,59});
 EntitySerialisationEntry AddEntry(uint id,GameObject obj,ServerSynchroniser sync){
  var entry=new EntitySerialisationEntry{m_GameObject=obj};entry.m_Header.m_uEntityID=id;
  if(sync!=null)entry.m_ServerSynchronisedComponents._items.Add(sync);
  EntitySerialisationRegistry.entries.Add(id,entry);regular._items.Add(entry);return entry;
 }
 var stackPrefab=new GameObject();p.Add<SpawnableEntityCollection>().Spawnables.Add(stackPrefab);
 var oldStack=new GameObject();oldStack.transform.parent=p.transform;var stackParent=oldStack.Add<AttachStation>();
 var oldStackPlatePoint=new GameObject();oldStackPlatePoint.transform.parent=oldStack.transform;
 var oldStackPhysical=oldStack.Add<PhysicalAttachment>();oldStack.Add<ServerPhysicalAttachment>();
 oldStack.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
 var oldStackWorld=oldStack.Add<ServerWorldObjectSynchroniser>();oldStackWorld.Setup();
 var oldStackClient=oldStack.Add<ClientWorldObjectSynchroniser>();oldStackClient.Setup(p.GetComponent<AttachStation>(),41);
 var oldStackContainer=new GameObject();oldStackPhysical.m_container=oldStackContainer.Add<ObjectContainer>();
 var oldStackEntry=AddEntry(51,oldStack,oldStackWorld);oldStackEntry.m_ClientSynchronisedComponents._items.Add(oldStackClient);
 var oldStackContainerEntry=AddEntry(52,oldStackContainer,oldStackContainer.Add<ServerPhysicsObjectSynchroniser>());
 NativeDynamicWarpPlan.ObserveFixture(oldStack,stackPrefab);
 var plate=new GameObject();plate.transform.parent=oldStackPlatePoint.transform;var platePhysical=plate.Add<PhysicalAttachment>();
 plate.Add<ServerPhysicalAttachment>();var plateClientPhysical=plate.Add<ClientPhysicalAttachment>();
 var savedPhysicalParent=physicalParentMode==0?null:physicalParentMode==1?(IParentable)stackParent:p.GetComponent<AttachStation>();
 plateClientPhysical.Setup(savedPhysicalParent);
 var plateWorld=plate.Add<ServerWorldObjectSynchroniser>();plateWorld.Setup();
 ((WorldObjectMessage)Get(plateWorld,"m_ServerData")).ParentEntityID=51;
 var plateClient=plate.Add<ClientWorldObjectSynchroniser>();plateClient.Setup(stackParent,51);
 var plateContainer=new GameObject();platePhysical.m_container=plateContainer.Add<ObjectContainer>();
 var plateEntry=AddEntry(57,plate,plateWorld);plateEntry.m_ClientSynchronisedComponents._items.Add(plateClient);
 var plateContainerEntry=AddEntry(58,plateContainer,plateContainer.Add<ServerPhysicsObjectSynchroniser>());
 Capture(161);
 regular._items.Remove(oldStackEntry);regular._items.Remove(oldStackContainerEntry);
 EntitySerialisationRegistry.entries.Remove(51);EntitySerialisationRegistry.entries.Remove(52);
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(51);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(52);
 EntityIdOrRef PathRef(params int[] ids){var value=new EntityIdOrRef{EntityPathReference=new()};value.__isset.entityPathReference=true;value.EntityPathReference.Ids.AddRange(ids);return value;}
 EntityIdOrRef IdRef(int id){var value=new EntityIdOrRef{EntityId=id};value.__isset.entityId=true;return value;}
 WarpSpec StackWarp(bool plateReturn=true,int child=57){
  var path=PathRef(41,0);var stack=new EntityWarpSpec{EntityPathReference=path.EntityPathReference,Stack=new()};
  stack.__isset.entityPathReference=true;stack.SpawningPath.AddRange(new[]{41,0});stack.Stack.StackContents.Add(IdRef(child));
  var parent=new EntityWarpSpec{EntityId=41,AttachStation=new(){Item=PathRef(41,0)},
   PlateReturnStation=plateReturn?new(){Stack=PathRef(41,0)}:null};parent.__isset.entityId=true;
  var result=new WarpSpec{Frame=161};result.Entities.Add(parent);result.Entities.Add(stack);return result;
 }
 Reject(()=>WorldSyncCacheModule.BeforePrepare(StackWarp(false)),
  "Returned-stack recreation rejects without the saved station's bidirectional stack reference");
 Reject(()=>WorldSyncCacheModule.BeforePrepare(StackWarp(true,58)),
  "Returned-stack recreation rejects a child that was not saved as attached to the missing owner");
 var stackWarp=StackWarp();WorldSyncCacheModule.BeforePrepare(stackWarp);
 var stackSpec=stackWarp.Entities.Single(value=>!value.__isset.entityId);
 var stackPlan=NativeDynamicWarpPlan.Fixture(stackSpec,oldStack.GetComponents<Component>().Select(c=>c.GetType()).ToArray(),null,null,stackPrefab);
 WorldSyncCacheModule.BeforeDynamicSpawn(stackPlan);
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{51,52,53,54,55,56,59}),
  "Returned-stack recreation reserves only the exact historical owner/container destruction suffix");
 var newStack=new GameObject();newStack.transform.parent=p.transform;newStack.Add<AttachStation>();
 var newStackPlatePoint=new GameObject();newStackPlatePoint.transform.parent=newStack.transform;
 var newStackPhysical=newStack.Add<PhysicalAttachment>();newStack.Add<ServerPhysicalAttachment>();
 newStack.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
 var newStackWorld=newStack.Add<ServerWorldObjectSynchroniser>();newStackWorld.Setup();
 var newStackClient=newStack.Add<ClientWorldObjectSynchroniser>();newStackClient.Setup(p.GetComponent<AttachStation>(),41);
 var newStackContainer=new GameObject();newStackPhysical.m_container=newStackContainer.Add<ObjectContainer>();
 uint newStackId=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();
 var newStackEntry=AddEntry(newStackId,newStack,newStackWorld);newStackEntry.m_ClientSynchronisedComponents._items.Add(newStackClient);
 uint newContainerId=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();
 var newStackContainerEntry=AddEntry(newContainerId,newStackContainer,newStackContainer.Add<ServerPhysicsObjectSynchroniser>());
 newStack.Add<SuperchargedPatch.EntityPathReferenceMarker>();stackPlan.SetActual(newStack);
 Check(WorldSyncCacheModule.FinalizeDynamicSpawn(stackPlan,null)==null
  &&newStackId==51&&newContainerId==52
  &&EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{53,54,55,56,59}),
  "Returned-stack spawn consumes exact historical IDs and restores the target allocator queue");
 Check(regular._items.SequenceEqual(new[]{EntitySerialisationRegistry.entries[12],newStackEntry,newStackContainerEntry,plateEntry,plateContainerEntry}),
  "Returned-stack spawn restores saved scheduler order around the surviving plate pair");
 var registryIds=EntitySerialisationRegistry.m_EntitiesList._items.Select(value=>value.m_Header.m_uEntityID).ToArray();
 Check(registryIds.SequenceEqual(new uint[]{12,41,51,52,57,58}),
  "Returned-stack spawn restores exact native entity-registry order");
 var physicsField=typeof(ServerPhysicsObjectSynchroniser).GetField("ms_ServerPhysicsObjectSytnchroniserTransforms",BindingFlags.Static|BindingFlags.NonPublic);
 var physicsValues=(IList)physicsField.GetValue(null);var pairType=typeof(ServerPhysicsObjectSynchroniser).GetNestedType("SerialisationEntryTransformPair");
 var pairEntry=pairType.GetField("m_Entry");
 Check(physicsValues.Cast<object>().Select(value=>((EntitySerialisationEntry)pairEntry.GetValue(value)).m_Header.m_uEntityID)
      .SequenceEqual(new uint[]{52,58}),
  "Returned-stack spawn restores exact physics-synchroniser pair order");
 int bodyRestoresBefore=BodyRestoreModule.DynamicRestores;
 plate.transform.parent=newStackPlatePoint.transform;
 var restorePlan=NativeKitchenCheckpoint.Prepare(stackWarp);restorePlan.Complete();WorldSyncCacheModule.AfterComplete(restorePlan);
 Check(ReferenceEquals(plate.transform.parent,newStackPlatePoint.transform)
  &&ReferenceEquals(Get(plateWorld,"m_CachedParentTransform"),newStackPlatePoint.transform)
  &&ReferenceEquals(Get(plateClient,"m_Parent"),newStack.GetComponent<AttachStation>())
  &&ReferenceEquals(Get(plateClientPhysical,"m_Parent"),physicalParentMode==1
      ?newStack.GetComponent<AttachStation>():savedPhysicalParent),
  "Returned-stack recreation rebinds every surviving plate parent cache to the exact new stack incarnation");
 Check(BodyRestoreModule.DynamicRestores==bodyRestoresBefore+2,
  "Returned-stack recreation restores both the recreated stack body and surviving plate body");
 m.Dispose();
}
ReturnedStackRecreation(0);
ReturnedStackRecreation(1);
ReturnedStackRecreation(2);
{
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 var absent=EntitySerialisationRegistry.entries[12];regular._items.Remove(absent);
 EntitySerialisationRegistry.entries.Remove(12);Capture(161);
 var latest=(Dictionary<string,object>)Status(m)["latest"];
 Check(latest["unsupported"]==null&&((object[])latest["items"]).Length==0,
  "A core checkpoint with a wholly absent fixed attachment omits it from WorldObject capture");
 Prepare(161);Check(true,"An absent fixed-attachment target is admitted when scheduler membership agrees");
 m.Dispose();
}
{
 var(m,s,p)=Setup();Capture(161);var original=Get(s,"m_ServerData");
 Check((int)Status(m)["frames"]==1,"Capture binds existing actual native history snapshot");
 Set(s,"m_bSentReliableRestPosition",false);Capture(161);
 Check(((Dictionary<string,object>)Status(m)["latest"])["unsupported"]==null,"Repeated paused callback does not overwrite first saved cache");
 s.transform.parent=new GameObject().transform;Prepare(161);Check(true,"Preflight allows current future parent to differ");
 s.transform.parent=p.transform;s.ResumePositionsWitness();Check(s.RestEventDue(),"Native attachment reset reproduces replay-only reliable rest packet");
 Restore(161);Check(!s.RestEventDue(),"Restored actual reliable-rest cache does not re-emit that packet");
 Check(ReferenceEquals(original,Get(s,"m_ServerData")),"Restore retains original native mutable message object");
 Check((float)Get(s,"m_LastUnreliableActiveSend")==0&&UnityEngine.Time.time==100&&UnrealTimePatch.LogicalTime==100,"Captured timestamp restored with no clock mutation");
 Check((int)Status(m)["restores"]==1,"Restore receipt records successful exact cache verification");
 m.Dispose();
}
foreach(var mutation in new[]{"pending-parent"}){
 var(m,s,p)=Setup();
 UnrealTimePatch.LogicalTime=1146.4834f;
 Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_LastUnreliableActiveSend",1145.95007f);Set(s,"m_bParentChanged",true);
 Capture(161);Prepare(161);
 var liveResidual=(((float)Get(s,"m_LastUnreliableActiveSend")+1f)-UnrealTimePatch.LogicalTime);
 Check(BitConverter.SingleToInt32Bits(liveResidual)==unchecked((int)0x3EEEF000),"Fixture retains the actual f1198 pending-rest residual bits");
 Set(s,"m_bSentReliableRestPosition",true);Set(s,"m_bActive",false);Set(s,"m_bParentChanged",false);
 UnityEngine.Time.time=33250.82f;Restore(161);
 Check(BitConverter.SingleToInt32Bits((float)Get(s,"m_LastUnreliableActiveSend"))==BitConverter.SingleToInt32Bits(1145.95007f)
  &&UnityEngine.Time.time==33250.82f&&UnrealTimePatch.LogicalTime==1146.4834f,
  "Pending rest timestamp restores exactly despite a huge unrelated Unity time: "+mutation);
 Check(!(bool)Get(s,"m_bSentReliableRestPosition")
  &&!(bool)Get(s,"m_bActive")&&(bool)Get(s,"m_bParentChanged"),
  "Pending/active WorldObject flags restore exactly: "+mutation);
 Check(!s.RestEventDue(),"Rest event remains not-due at the captured logical time: "+mutation);
 float strictThreshold=(float)Get(s,"m_LastUnreliableActiveSend")+1f;
 UnrealTimePatch.LogicalTime=strictThreshold;Check(!s.RestEventDue(),"Exact rest deadline retains the native strict greater-than test: "+mutation);
 UnrealTimePatch.LogicalTime=BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(strictThreshold)+1);
 Check(s.RestEventDue(),"The next logical-clock ULP triggers the native rest event: "+mutation);
 UnrealTimePatch.LogicalTime=1146.4834f;PlainResumeInput();WorldSyncCacheModule.BeforeAuthoringResume();
 Check(BitConverter.SingleToInt32Bits((float)Get(s,"m_LastUnreliableActiveSend"))==BitConverter.SingleToInt32Bits(1145.95007f),
  "Read-only resume validation never rewrites the pending timestamp");
 Helpers.Resume();WorldSyncCacheModule.AfterAuthoringResume();
 Check(Status(m)["pendingResumeValidation"]==null&&Status(m)["pendingPausedDynamicTransforms"]==null,
  "Successful resume consumes the exact restored validation target and dynamic-maintenance state");
 TimeManager.paused=true;
 m.Dispose();
}
{
 var(m,s,p)=Setup();Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_LastUnreliableActiveSend",99.25f);Set(s,"m_bParentChanged",true);
 Capture(161);UnityEngine.Time.time=33250.82f;PlainResumeInput();WorldSyncCacheModule.BeforeAuthoringResume();
 Check((float)Get(s,"m_LastUnreliableActiveSend")==99.25f,"Reference branch ignores arbitrary authoring-pause Unity-time dwell without a write");
 Helpers.Resume();WorldSyncCacheModule.AfterAuthoringResume();
 Check((int)Status(m)["referenceResumeValidations"]==1,"Reference resume receives one read-only exact-boundary validation");
 TimeManager.paused=true;m.Dispose();
}
{
 var(m,s,p)=Setup();Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_LastUnreliableActiveSend",99.25f);Set(s,"m_bParentChanged",true);
 Capture(161);Prepare(161);Set(s,"m_LastUnreliableActiveSend",88f);UnrealTimePatch.LogicalTime=101;
 Reject(()=>Restore(161),"Logical-clock mismatch rejects restore before any WorldObject cache write");
 Check((float)Get(s,"m_LastUnreliableActiveSend")==88f,"Rejected logical-clock mismatch leaves the live cache untouched");
 m.Dispose();
}
{
 var(m,s,p)=Setup();Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_LastUnreliableActiveSend",99.25f);Set(s,"m_bParentChanged",true);
 Capture(161);Prepare(161);Restore(161);Hpmv.Injector.Server.CurrentFrameData.FrameNumber=162;PlainResumeInput();
 Reject(()=>WorldSyncCacheModule.BeforeAuthoringResume(),"Post-rewind resume rejects when its exact restored boundary cannot be resolved");
 Check((int)Status(m)["pendingResumeValidation"]==161,"Rejected boundary lookup retains the required restored resume target");
 Hpmv.Injector.Server.CurrentFrameData.FrameNumber=161;WorldSyncCacheModule.BeforeAuthoringResume();Helpers.Resume();WorldSyncCacheModule.AfterAuthoringResume();
 Check(Status(m)["pendingResumeValidation"]==null&&(int)Status(m)["warpResumeValidations"]==1,
  "Exact post-rewind boundary validation clears only after successful main-pause release");
 TimeManager.paused=true;m.Dispose();
}
{
 var(m,s,p)=Setup();Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_LastUnreliableActiveSend",99.25f);Set(s,"m_bParentChanged",true);
 Capture(161);Set(s,"m_LastUnreliableActiveSend",88f);PlainResumeInput();
 Reject(()=>WorldSyncCacheModule.BeforeAuthoringResume(),"Read-only resume validation rejects a cache mutation during authoring pause");
 Check((float)Get(s,"m_LastUnreliableActiveSend")==88f,"Rejected resume validation performs no compensating timestamp write");
 m.Dispose();
}
{
 var(m,s,p)=Setup();Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_LastUnreliableActiveSend",33249.8f);Set(s,"m_bParentChanged",true);
 Capture(161);Reject(()=>Prepare(161),"A future pending timestamp from a different clock epoch fails closed");m.Dispose();
}
{
 var(m,s,p)=Setup();UnrealTimePatch.LogicalTime=33250.8f;Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_LastUnreliableActiveSend",99.25f);Set(s,"m_bParentChanged",true);
 Capture(161);Reject(()=>Prepare(161),"A past pending timestamp from a different clock epoch fails closed");m.Dispose();
}
foreach(var mutation in new[]{"sleep","started","paused","nan","message","inconsistent","active","pending-no-parent","pending-no-sync"}){
 var(m,s,p)=Setup();
 if(mutation=="sleep")Set(s,"m_bSleepAllowed",false);
 if(mutation=="started")Set(s,"m_bStartedSynchronising",false);
 if(mutation=="paused")Set(s,"m_bPaused",true);
 if(mutation=="nan")Set(s,"m_LastUnreliableActiveSend",float.NaN);
 if(mutation=="message")((WorldObjectMessage)Get(s,"m_ServerData")).LocalPosition=new(){x=float.PositiveInfinity};
 if(mutation=="inconsistent")Set(s,"m_bActive",true);
 if(mutation=="active"){Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_bActive",true);}
 if(mutation=="pending-no-parent")Set(s,"m_bSentReliableRestPosition",false);
 if(mutation=="pending-no-sync"){Set(s,"m_bSentReliableRestPosition",false);Set(s,"m_bParentChanged",true);Set(s,"m_bSyncPositions",false);}
 Capture(161);Check((int)Status(m)["frames"]==1,"Unsupported target still permits ordinary core capture: "+mutation);
 Reject(()=>Prepare(161),"Unsupported target rejects before original Prepare/mutation: "+mutation);m.Dispose();
}
foreach(var mutation in new[]{"source","message","parent","round","missing"}){
 var(m,s,p)=Setup();Capture(161);
 if(mutation=="source")EntitySerialisationRegistry.entries[12]=new(){m_GameObject=new GameObject()};
 if(mutation=="message")Set(s,"m_ServerData",new WorldObjectMessage());
 // Destroyed target parent is represented by changed pinned instance in this managed fixture.
 if(mutation=="parent")s.gameObject.components.Remove(s);
 if(mutation=="round")NativeKitchenCheckpoint.Reset();
 Reject(()=>Prepare(mutation=="missing"?160:161),"Changed target identity/history rejected: "+mutation);m.Dispose();
}
{
 var(m,s,p)=Setup();Capture(161);s.transform.parent=new GameObject().transform;
 Reject(()=>Restore(161),"Restore requires native callback to establish exact saved parent");m.Dispose();
}
{
 var(m,s,p)=Setup();Capture(161);s.transform.localPosition=new(){z=.01f};
 Reject(()=>Restore(161),"Cache helper never substitutes for actual pose restoration");m.Dispose();
}
{
 var(m,s,p)=Setup();Capture(161);Capture(172);Restore(161);
 Check((int)Status(m)["frames"]==1,"Successful restore prunes future sidecar snapshots");
 NativeKitchenCheckpoint.Reset();Capture(31);Check((int)Status(m)["frames"]==1&&(int)Status(m)["lastFrame"]==31,"Normal level restart resets cache by actual round object");
 m.Invoke("deactivate",new());Check(!(bool)Status(m)["active"],"Deactivation removes module ownership");m.Dispose();
}

var nativePath="runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll";
{
 var(m,s,p)=Setup();Capture(161);var client=s.gameObject.GetComponent<ClientWorldObjectSynchroniser>();
 Set(client,"m_ParentEntityID",32u);Set(client,"m_Parent",new GameObject().Add<AttachStation>());
 Set(client,"m_ServerPosition",new Vector3{x=2,y=3,z=4});Set(client,"m_bHasEverReceived",false);
 Prepare(161);Restore(161);
 Check((uint)Get(client,"m_ParentEntityID")==41u&&ReferenceEquals(Get(client,"m_Parent"),p.GetComponent<AttachStation>()),"Restore exact saved client parent cache after native Transform already matches target");
 Check((Vector3)Get(client,"m_ServerPosition") is {x:0,y:0,z:0}&&(bool)Get(client,"m_bHasEverReceived"),"Restore client observed server position and receipt state");
 var receipt=(Dictionary<string,object>)Status(m)["lastRestore"];var before=(object[])receipt["clientsBefore"];var after=(object[])receipt["clientsAfter"];
 Check((uint)((Dictionary<string,object>)before[0])["parentId"]==32&&(uint)((Dictionary<string,object>)after[0])["parentId"]==41,"Restore receipt preserves actual before and after cache values separately");m.Dispose();
}
{
 var(m,s,p)=Setup();var client=s.gameObject.GetComponent<ClientWorldObjectSynchroniser>();Set(client,"m_Parent",null);Capture(161);
 Set(client,"m_Parent",new GameObject().Add<AttachStation>());Set(client,"m_ParentEntityID",32u);Prepare(161);Restore(161);
 Check(Get(client,"m_Parent")==null&&(uint)Get(client,"m_ParentEntityID")==41u&&(bool)Get(client,"m_bHasParent"),"Preserve native startup null parent cache with its observed registered parent ID");m.Dispose();
}
{
 var(m,s,p)=Setup();Set(s.gameObject.GetComponent<ClientWorldObjectSynchroniser>(),"m_Parent",null);Capture(161);
 EntitySerialisationRegistry.entries[41]=new(){m_GameObject=new GameObject()};
 Reject(()=>Prepare(161),"Null client parent cache still pins the referenced parent registry incarnation");m.Dispose();
}
foreach(var mutation in new[]{"pending","paused","parent","nonfinite"}){
 var(m,s,p)=Setup();var client=s.gameObject.GetComponent<ClientWorldObjectSynchroniser>();
 if(mutation=="pending")Set(client,"m_PendingResumeData",new WorldObjectMessage());
 if(mutation=="paused")Set(client,"m_bPaused",true);
 if(mutation=="parent")Set(client,"m_ParentEntityID",99u);
 if(mutation=="nonfinite")Set(client,"m_ServerPosition",new Vector3{x=float.NaN});
 Capture(161);Reject(()=>Prepare(161),"Unsupported client target rejects without world mutation: "+mutation);m.Dispose();
}
foreach(var mutation in new[]{"client","lerper","callback","parentIncarnation"}){
 var(m,s,p)=Setup();Capture(161);var client=s.gameObject.GetComponent<ClientWorldObjectSynchroniser>();
 if(mutation=="client")s.gameObject.components.Remove(client);
 if(mutation=="lerper")Set(client,"m_Lerper",s.gameObject.Add<EmptyLerp>());
 if(mutation=="callback")Set(client,"m_parentChanged",new GenericVoid(()=>{}));
 if(mutation=="parentIncarnation")EntitySerialisationRegistry.entries[41]=new(){m_GameObject=new GameObject()};
 Reject(()=>Prepare(161),"Client cache reference mutation rejects before restoration: "+mutation);m.Dispose();
}
{
 var(m,s,p)=Setup();var sched=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var entry=EntitySerialisationRegistry.entries[12];Set(entry,"m_bUrgentUpdate",true);EntitySerialisationRegistry.HasUrgentOutgoingUpdates=false;
 var initialNext=(float)Get(sched,"m_fNextUpdate");var initialFast=(float)Get(sched,"m_fNextFastUpdate");Capture(161);
 Set(sched,"m_fNextUpdate",.099f);Set(sched,"m_fNextFastUpdate",.021f);Set(entry,"m_bUrgentUpdate",false);EntitySerialisationRegistry.HasUrgentOutgoingUpdates=true;
 Prepare(161);Restore(161);
 Check((float)Get(sched,"m_fNextUpdate")==initialNext&&(float)Get(sched,"m_fNextFastUpdate")==initialFast,"Restore both exact native residual floats rather than normalize a cadence phase");
 Check((bool)Get(entry,"m_bUrgentUpdate")&&!EntitySerialisationRegistry.HasUrgentOutgoingUpdates,"Restore observed per-entry/global urgent flags independently without OR side effects");
 m.Dispose();
}
foreach(var mutation in new[]{"owner","list","membership","componentOrder","mode","session","stopped"}){
 var(m,s,p)=Setup();Capture(161);var sched=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var entries=(FastList<EntitySerialisationEntry>)Get(sched,"m_EntitiesList");
 if(mutation=="owner")ControllerHandler.MultiplayerController=new();
 if(mutation=="list")Set(sched,"m_EntitiesList",new FastList<EntitySerialisationEntry>());
 if(mutation=="membership")entries._items.Clear();
 if(mutation=="componentOrder")entries._items[0].m_ServerSynchronisedComponents._items.Add(new ServerWorldObjectSynchroniser());
 if(mutation=="mode")DebugManager.Instance.fast=false;
 if(mutation=="session")Set(sched,"m_SessionCoordinator",new object());
 if(mutation=="stopped")Set(sched,"m_bStarted",false);
 Reject(()=>Prepare(161),"Unsupported scheduler difference rejects before core mutation: "+mutation);m.Dispose();
}
{
 var(m,s,p)=Setup();var sched=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var entries=(FastList<EntitySerialisationEntry>)Get(sched,"m_EntitiesList");var saved=entries._items[0];Capture(161);
 var futureObject=new GameObject();var future=new EntitySerialisationEntry{m_GameObject=futureObject};future.m_Header.m_uEntityID=99;
 EntitySerialisationRegistry.entries.Add(99,future);entries._items.Insert(0,future);
 Prepare(161);Check(true,"Preflight admits an extra future scheduler member while checkpoint members survive unchanged");
 entries._items.Remove(future);EntitySerialisationRegistry.entries.Remove(99);Restore(161);
 Check(entries._items.SequenceEqual(new[]{saved}),"Post-core restore accepts the exact checkpoint scheduler membership");m.Dispose();
}
{
 var(m,s,p)=Setup();var sched=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var entries=(FastList<EntitySerialisationEntry>)Get(sched,"m_EntitiesList");Capture(161);
 var futureObject=new GameObject();var future=new EntitySerialisationEntry{m_GameObject=futureObject};future.m_Header.m_uEntityID=99;
 EntitySerialisationRegistry.entries.Add(99,future);entries._items.Add(future);
 Prepare(161);Reject(()=>Restore(161),"Extra future scheduler member must be gone after core entity restoration");m.Dispose();
}
{
 var(m,s,p)=Setup();var sched=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var entries=(FastList<EntitySerialisationEntry>)Get(sched,"m_EntitiesList");Capture(161);
 ushort allocated=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();var futureObject=new GameObject();
 var future=new EntitySerialisationEntry{m_GameObject=futureObject};future.m_Header.m_uEntityID=allocated;
 EntitySerialisationRegistry.entries.Add(allocated,future);entries._items.Add(future);Prepare(161);
 entries._items.Remove(future);EntitySerialisationRegistry.entries.Remove(allocated);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(allocated);
 Restore(161);Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{51,52,53}),"Restore recovers exact native free entity-ID allocation order after future entity retirement");m.Dispose();
}
{
 var(m,s,p)=Setup();Capture(161);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();Prepare(161);
 Reject(()=>Restore(161),"Changed free entity-ID membership rejects after core restoration");m.Dispose();
}
{
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{53,54,55});
 EntitySerialisationEntry AddDynamic(uint id,GameObject obj,ServerSynchroniser sync){
  var e=new EntitySerialisationEntry{m_GameObject=obj};e.m_Header.m_uEntityID=id;e.m_ServerSynchronisedComponents._items.Add(sync);
  EntitySerialisationRegistry.entries.Add(id,e);regular._items.Add(e);return e;
 }
 var oldOwner=new GameObject();oldOwner.transform.parent=p.transform;var oldPhysical=oldOwner.Add<PhysicalAttachment>();oldOwner.Add<ServerPhysicalAttachment>();oldOwner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());var oldWorld=oldOwner.Add<ServerWorldObjectSynchroniser>();oldWorld.Setup();var oldClientWorld=oldOwner.Add<ClientWorldObjectSynchroniser>();oldClientWorld.Setup(p.GetComponent<AttachStation>(),41);
 var oldContainer=new GameObject();oldPhysical.m_container=oldContainer.Add<ObjectContainer>();var oldPhysics=oldContainer.Add<ServerPhysicsObjectSynchroniser>();
 var oldOwnerEntry=AddDynamic(51,oldOwner,oldWorld);oldOwnerEntry.m_ClientSynchronisedComponents._items.Add(oldClientWorld);var oldContainerEntry=AddDynamic(52,oldContainer,oldPhysics);Set(oldOwnerEntry,"m_bUrgentUpdate",true);
 int bodyCapturesBefore=BodyRestoreModule.DynamicCaptures,bodyRestoresBefore=BodyRestoreModule.DynamicRestores;
 Capture(161);
 Check(BodyRestoreModule.DynamicCaptures==bodyCapturesBefore+1&&BodyRestoreModule.LastCapturedId==52,"Dynamic scheduler capture obtains one exact historical body checkpoint for the attachment container");
 var capturedItems=(object[])((Dictionary<string,object>)Status(m)["latest"])["items"];
 Check(capturedItems.Length==2&&(bool)((Dictionary<string,object>)capturedItems.Single(value=>(int)((Dictionary<string,object>)value)["id"]==51))["dynamic"],
  "Dynamic scheduler owner contributes one full WorldObject cache checkpoint alongside the fixed attachment");
 regular._items.Remove(oldOwnerEntry);regular._items.Remove(oldContainerEntry);EntitySerialisationRegistry.entries.Remove(51);EntitySerialisationRegistry.entries.Remove(52);
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(51);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(52);
 EntitySerialisationEntry AddFuture(GameObject obj,ServerSynchroniser sync){uint id=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();return AddDynamic(id,obj,sync);}
 var futureOwner=new GameObject();futureOwner.transform.parent=p.transform;var futurePhysical=futureOwner.Add<PhysicalAttachment>();futureOwner.Add<ServerPhysicalAttachment>();futureOwner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());var futureWorld=futureOwner.Add<ServerWorldObjectSynchroniser>();futureWorld.Setup();var futureClientWorld=futureOwner.Add<ClientWorldObjectSynchroniser>();futureClientWorld.Setup(p.GetComponent<AttachStation>(),41);
 var futureContainer=new GameObject();futurePhysical.m_container=futureContainer.Add<ObjectContainer>();var futurePhysics=futureContainer.Add<ServerPhysicsObjectSynchroniser>();
 var futureOwnerEntry=AddFuture(futureOwner,futureWorld);futureOwnerEntry.m_ClientSynchronisedComponents._items.Add(futureClientWorld);var futureContainerEntry=AddFuture(futureContainer,futurePhysics);
 var spec=new EntityWarpSpec();spec.__isset.entityPathReference=true;spec.EntityPathReference=new();spec.EntityPathReference.Ids.Add(51);spec.SpawningPath.AddRange(new[]{30,0});
 var foodWarp=new WarpSpec{Frame=161};foodWarp.Entities.Add(spec);foodWarp.EntitiesToDelete.Add(53);
 WorldSyncCacheModule.BeforePrepare(foodWarp);
 var dynamicPlan=NativeDynamicWarpPlan.Fixture(spec,oldOwner.GetComponents<Component>().Select(c=>c.GetType()).ToArray(),futureOwner,futureContainer);
 WorldSyncCacheModule.BeforeDynamicSpawn(dynamicPlan);
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{51,52,55}),"Dynamic restore transaction reserves historical owner/body IDs at the allocator head");
 var spawnFailure=new InvalidOperationException("fixture spawn failure");
 Check(ReferenceEquals(WorldSyncCacheModule.FinalizeDynamicSpawn(dynamicPlan,spawnFailure),spawnFailure)
  &&EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{55,51,52}),
  "Failed dynamic spawn rolls the allocator back to its exact preimage");
 WorldSyncCacheModule.BeforePrepare(foodWarp);WorldSyncCacheModule.BeforeDynamicSpawn(dynamicPlan);
 var newOwner=new GameObject();newOwner.transform.parent=p.transform;var newPhysical=newOwner.Add<PhysicalAttachment>();newOwner.Add<ServerPhysicalAttachment>();newOwner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());var newWorld=newOwner.Add<ServerWorldObjectSynchroniser>();newWorld.Setup();var newClientWorld=newOwner.Add<ClientWorldObjectSynchroniser>();newClientWorld.Setup(p.GetComponent<AttachStation>(),41);
 var newContainer=new GameObject();newPhysical.m_container=newContainer.Add<ObjectContainer>();var newPhysics=newContainer.Add<ServerPhysicsObjectSynchroniser>();
 newContainer.Add<DynamicLandscapeParenting>();var transientServer=newContainer.Add<ServerDynamicLandscapeParenting>();var transientClient=newContainer.Add<ClientDynamicLandscapeParenting>();
 var newOwnerEntry=AddFuture(newOwner,newWorld);newOwnerEntry.m_ClientSynchronisedComponents._items.Add(newClientWorld);var newContainerEntry=AddFuture(newContainer,newPhysics);
 newContainerEntry.m_ServerSynchronisedComponents._items.Add(transientServer);newContainerEntry.m_ClientSynchronisedComponents._items.Add(transientClient);
 newOwner.Add<SuperchargedPatch.EntityPathReferenceMarker>();dynamicPlan.SetActual(newOwner);
 Check(WorldSyncCacheModule.FinalizeDynamicSpawn(dynamicPlan,null)==null,"Exact dynamic spawn rebinds the historical scheduler slots");
 regular._items.Remove(futureOwnerEntry);regular._items.Remove(futureContainerEntry);EntitySerialisationRegistry.entries.Remove(53);EntitySerialisationRegistry.entries.Remove(54);
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(53);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(54);
 var reboundMessage=(WorldObjectMessage)Get(newWorld,"m_ServerData");reboundMessage.ParentEntityID=99;
 Set(newWorld,"m_CachedParentTransform",new GameObject().transform);Set(newWorld,"m_bSentReliableRestPosition",false);Set(newWorld,"m_bParentChanged",true);
 var reboundLerper=Get(newClientWorld,"m_Lerper");var reboundCallback=Get(newClientWorld,"m_parentChanged");
 Set(newClientWorld,"m_ParentEntityID",99u);Set(newClientWorld,"m_Parent",new GameObject().Add<AttachStation>());Set(newClientWorld,"m_PendingResumeData",new WorldObjectMessage());
 var restorePlan=NativeKitchenCheckpoint.Prepare(foodWarp);restorePlan.Complete();WorldSyncCacheModule.AfterComplete(restorePlan);
 Check(regular._items.SequenceEqual(new[]{EntitySerialisationRegistry.entries[12],newOwnerEntry,newContainerEntry}),"Dynamic scheduler membership/order restores with new exact-ID incarnations");
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{53,54,55}),"Dynamic replacement restores the historical exact free-ID queue order");
 Check((bool)Get(newOwnerEntry,"m_bUrgentUpdate")&&(int)Status(m)["dynamicRestores"]==1,"Dynamic replacement restores historical urgent state and records one exact restore");
 Check(newContainer.GetComponent<DynamicLandscapeParenting>()==null&&newContainer.GetComponent<ServerDynamicLandscapeParenting>()==null
  &&newContainer.GetComponent<ClientDynamicLandscapeParenting>()==null,"Disabled deferred dynamic-parenting components are finalized before checkpoint validation");
 Check(BodyRestoreModule.DynamicRestores==bodyRestoresBefore+1&&BodyRestoreModule.LastRestoredId==52,"Dynamic scheduler rebind restores the historical body values onto the recreated exact-ID incarnation");
 Check(ReferenceEquals(Get(newWorld,"m_ServerData"),reboundMessage)&&reboundMessage.ParentEntityID==41
  &&ReferenceEquals(Get(newWorld,"m_CachedParentTransform"),p.transform)&&(bool)Get(newWorld,"m_bSentReliableRestPosition")&&!(bool)Get(newWorld,"m_bParentChanged"),
  "Dynamic replacement copies the historical server WorldObject values into the recreated incarnation's own message/cache objects");
 Check((uint)Get(newClientWorld,"m_ParentEntityID")==41u&&ReferenceEquals(Get(newClientWorld,"m_Parent"),p.GetComponent<AttachStation>())
  &&Get(newClientWorld,"m_PendingResumeData")==null&&ReferenceEquals(Get(newClientWorld,"m_Lerper"),reboundLerper)&&ReferenceEquals(Get(newClientWorld,"m_parentChanged"),reboundCallback),
  "Dynamic replacement restores client parent values while retaining recreated lerper/callback identity");
 Set(newClientWorld,"m_ParentEntityID",99u);Set(newWorld,"m_bSentReliableRestPosition",false);Set(newWorld,"m_bParentChanged",true);
 Prepare(161);Restore(161);
 Check((uint)Get(newClientWorld,"m_ParentEntityID")==41u&&(bool)Get(newWorld,"m_bSentReliableRestPosition")
  &&!(bool)Get(newWorld,"m_bParentChanged")&&(int)Status(m)["dynamicRestores"]==1&&(int)Status(m)["survivingDynamicRestores"]==1,
  "A cross-incarnation target remains reusable as an exact same-incarnation dynamic checkpoint after rebinding");
 m.Dispose();
}
{
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{55,56,57});
 EntitySerialisationEntry AddInitial(uint id,GameObject obj,ServerSynchroniser sync=null){
  var e=new EntitySerialisationEntry{m_GameObject=obj};e.m_Header.m_uEntityID=id;if(sync!=null)e.m_ServerSynchronisedComponents._items.Add(sync);
  EntitySerialisationRegistry.entries.Add(id,e);return e;
 }
 var oldOwner=new GameObject();oldOwner.transform.parent=p.transform;var oldPhysical=oldOwner.Add<PhysicalAttachment>();oldOwner.Add<ServerPhysicalAttachment>();oldOwner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
 var oldWorld=oldOwner.Add<ServerWorldObjectSynchroniser>();oldWorld.Setup();var oldClient=oldOwner.Add<ClientWorldObjectSynchroniser>();oldClient.Setup(p.GetComponent<AttachStation>(),41);
 var oldContainer=new GameObject();oldPhysical.m_container=oldContainer.Add<ObjectContainer>();var oldPhysics=oldContainer.Add<ServerPhysicsObjectSynchroniser>();
 var oldOwnerEntry=AddInitial(2,oldOwner,oldWorld);oldOwnerEntry.m_ClientSynchronisedComponents._items.Add(oldClient);
 var fixedObject=new GameObject();var fixedEntry=AddInitial(99,fixedObject);
 var oldContainerEntry=AddInitial(47,oldContainer,oldPhysics);
 regular._items.Add(oldOwnerEntry);regular._items.Add(fixedEntry);regular._items.Add(oldContainerEntry);
 NativeSceneMetadata.InitialPhysicalAttachmentIds.Add(2);Capture(161);
 regular._items.Remove(oldOwnerEntry);regular._items.Remove(oldContainerEntry);
 EntitySerialisationRegistry.entries.Remove(2);EntitySerialisationRegistry.entries.Remove(47);
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(2);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(47);
 var spec=new EntityWarpSpec{EntityPathReference=new()};spec.__isset.entityPathReference=true;spec.EntityPathReference.Ids.Add(2);spec.SpawningPath.AddRange(new[]{34,0,0});
 var initialWarp=new WarpSpec{Frame=161};initialWarp.Entities.Add(spec);WorldSyncCacheModule.BeforePrepare(initialWarp);
 Check(NativeInitialAttachmentDeletionAuthorization.Authorizations==0,
  "Initial attachment recreation without deletions does not issue a core deletion capability");
 var initialPlan=NativeDynamicWarpPlan.InitialFixture(spec,new[]{new[]{typeof(PhysicalAttachment)},oldOwner.GetComponents<Component>().Select(c=>c.GetType()).ToArray()});
 WorldSyncCacheModule.BeforeDynamicSpawn(initialPlan);
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{55,56,2,47,57}),
  "Initial attachment recreation reserves both intermediate owner/container scratch IDs before historical plate/body IDs");
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==55&&EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==56
  &&EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==2&&EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==47,
  "Observed two-stage plate factory consumes the reserved IDs in exact registration order");
 var newOwner=new GameObject();newOwner.transform.parent=p.transform;var newPhysical=newOwner.Add<PhysicalAttachment>();newOwner.Add<ServerPhysicalAttachment>();newOwner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
 var newWorld=newOwner.Add<ServerWorldObjectSynchroniser>();newWorld.Setup();var newClient=newOwner.Add<ClientWorldObjectSynchroniser>();newClient.Setup(p.GetComponent<AttachStation>(),41);
 var newContainer=new GameObject();newPhysical.m_container=newContainer.Add<ObjectContainer>();var newPhysics=newContainer.Add<ServerPhysicsObjectSynchroniser>();
 var newOwnerEntry=AddInitial(2,newOwner,newWorld);newOwnerEntry.m_ClientSynchronisedComponents._items.Add(newClient);var newContainerEntry=AddInitial(47,newContainer,newPhysics);
 regular._items.Add(newOwnerEntry);regular._items.Add(newContainerEntry);newOwner.Add<SuperchargedPatch.EntityPathReferenceMarker>();
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(55);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(56);initialPlan.SetActual(newOwner);
 Check(WorldSyncCacheModule.FinalizeDynamicSpawn(initialPlan,null)==null
  &&EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{55,56,57}),
  "Initial attachment spawn finalizer restores exact checkpoint free-ID order after scratch retirement");
 Check(regular._items.SequenceEqual(new[]{EntitySerialisationRegistry.entries[12],newOwnerEntry,fixedEntry,newContainerEntry}),
  "Initial attachment spawn finalizer reinstates historical scheduler slots around surviving members");
 var restorePlan=NativeKitchenCheckpoint.Prepare(initialWarp);restorePlan.Complete();WorldSyncCacheModule.AfterComplete(restorePlan);
 Check((int)Status(m)["dynamicRestores"]==1&&ReferenceEquals(EntitySerialisationRegistry.entries[2].m_GameObject,newOwner),
  "Initial attachment WorldObject/body caches rebind to exact historical IDs after core completion");
 m.Dispose();
}
{
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{53,54,55,56,57,58,59});
 EntitySerialisationEntry AddEntry(uint id,GameObject obj,ServerSynchroniser sync=null){
  var entry=new EntitySerialisationEntry{m_GameObject=obj};entry.m_Header.m_uEntityID=id;
  if(sync!=null)entry.m_ServerSynchronisedComponents._items.Add(sync);EntitySerialisationRegistry.entries.Add(id,entry);return entry;
 }
 var oldOwner=new GameObject();oldOwner.transform.parent=p.transform;var oldPhysical=oldOwner.Add<PhysicalAttachment>();
 oldOwner.Add<ServerPhysicalAttachment>();oldOwner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
 var oldWorld=oldOwner.Add<ServerWorldObjectSynchroniser>();oldWorld.Setup();var oldClient=oldOwner.Add<ClientWorldObjectSynchroniser>();oldClient.Setup(p.GetComponent<AttachStation>(),41);
 var oldContainer=new GameObject();oldPhysical.m_container=oldContainer.Add<ObjectContainer>();var oldPhysics=oldContainer.Add<ServerPhysicsObjectSynchroniser>();
 var oldOwnerEntry=AddEntry(2,oldOwner,oldWorld);oldOwnerEntry.m_ClientSynchronisedComponents._items.Add(oldClient);
 var fixedEntry=AddEntry(99,new GameObject());var oldContainerEntry=AddEntry(47,oldContainer,oldPhysics);
 regular._items.Add(oldOwnerEntry);regular._items.Add(fixedEntry);regular._items.Add(oldContainerEntry);
 NativeSceneMetadata.InitialPhysicalAttachmentIds.Add(2);Capture(161);
 regular._items.Remove(oldOwnerEntry);regular._items.Remove(oldContainerEntry);EntitySerialisationRegistry.entries.Remove(2);EntitySerialisationRegistry.entries.Remove(47);
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(2);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(47);
 (EntitySerialisationEntry ownerEntry,EntitySerialisationEntry containerEntry,GameObject owner,GameObject container) AddFuturePair(){
  var owner=new GameObject();owner.transform.parent=p.transform;var physical=owner.Add<PhysicalAttachment>();owner.Add<ServerPhysicalAttachment>();owner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
  var world=owner.Add<ServerWorldObjectSynchroniser>();world.Setup();var client=owner.Add<ClientWorldObjectSynchroniser>();client.Setup(p.GetComponent<AttachStation>(),41);
  var container=new GameObject();physical.m_container=container.Add<ObjectContainer>();var physics=container.Add<ServerPhysicsObjectSynchroniser>();
  uint ownerId=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();var ownerEntry=AddEntry(ownerId,owner,world);ownerEntry.m_ClientSynchronisedComponents._items.Add(client);regular._items.Add(ownerEntry);
  uint containerId=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();var containerEntry=AddEntry(containerId,container,physics);regular._items.Add(containerEntry);
  return(ownerEntry,containerEntry,owner,container);
 }
 var future1=AddFuturePair();var future2=AddFuturePair();
 var spec=new EntityWarpSpec{EntityPathReference=new()};spec.__isset.entityPathReference=true;spec.EntityPathReference.Ids.Add(2);spec.SpawningPath.AddRange(new[]{34,0,0});
 var incompleteWarp=new WarpSpec{Frame=161};incompleteWarp.Entities.Add(spec);incompleteWarp.EntitiesToDelete.Add((int)future1.ownerEntry.m_Header.m_uEntityID);
 Reject(()=>WorldSyncCacheModule.BeforePrepare(incompleteWarp),"Initial recreation rejects when declared deletion pairs are not precisely all scheduler extras");
 var multiDeleteWarp=new WarpSpec{Frame=161};multiDeleteWarp.Entities.Add(spec);multiDeleteWarp.EntitiesToDelete.Add((int)future1.ownerEntry.m_Header.m_uEntityID);multiDeleteWarp.EntitiesToDelete.Add((int)future2.ownerEntry.m_Header.m_uEntityID);
 WorldSyncCacheModule.BeforePrepare(multiDeleteWarp);
 Check(NativeInitialAttachmentDeletionAuthorization.Authorizations==1
  &&ReferenceEquals(NativeInitialAttachmentDeletionAuthorization.Warp,multiDeleteWarp)
  &&NativeInitialAttachmentDeletionAuthorization.HistoricalOwnerId==2
  &&NativeInitialAttachmentDeletionAuthorization.HistoricalContainerId==47
  &&NativeInitialAttachmentDeletionAuthorization.FutureOwners.SequenceEqual(new[]{53,55}),
  "Complete scheduler/free-ID preflight issues one exact core capability naming only future deletion owners");
 var incompletePlan=NativeDynamicWarpPlan.InitialFixture(spec,new[]{new[]{typeof(PhysicalAttachment)},oldOwner.GetComponents<Component>().Select(c=>c.GetType()).ToArray()});
 AddPlanRemoval(incompletePlan,future1.owner,future1.container);
 Reject(()=>WorldSyncCacheModule.BeforeDynamicSpawn(incompletePlan),"Initial recreation requires the native plan to contain every exact admitted deletion pair");
 var multiDeletePlan=NativeDynamicWarpPlan.InitialFixture(spec,new[]{new[]{typeof(PhysicalAttachment)},oldOwner.GetComponents<Component>().Select(c=>c.GetType()).ToArray()});
 AddPlanRemoval(multiDeletePlan,future1.owner,future1.container);AddPlanRemoval(multiDeletePlan,future2.owner,future2.container);
 WorldSyncCacheModule.BeforeDynamicSpawn(multiDeletePlan);
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{57,58,2,47,59}),
  "Initial recreation with deletions reserves scratch and historical IDs without reintroducing live future IDs");
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==57&&EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==58
  &&EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==2&&EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==47,
  "Initial recreation with deletions consumes exact scratch and historical registration IDs");
 var newOwner=new GameObject();newOwner.transform.parent=p.transform;var newPhysical=newOwner.Add<PhysicalAttachment>();newOwner.Add<ServerPhysicalAttachment>();newOwner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
 var newWorld=newOwner.Add<ServerWorldObjectSynchroniser>();newWorld.Setup();var newClient=newOwner.Add<ClientWorldObjectSynchroniser>();newClient.Setup(p.GetComponent<AttachStation>(),41);
 var newContainer=new GameObject();newPhysical.m_container=newContainer.Add<ObjectContainer>();var newPhysics=newContainer.Add<ServerPhysicsObjectSynchroniser>();
 var newOwnerEntry=AddEntry(2,newOwner,newWorld);newOwnerEntry.m_ClientSynchronisedComponents._items.Add(newClient);var newContainerEntry=AddEntry(47,newContainer,newPhysics);
 regular._items.Add(newOwnerEntry);regular._items.Add(newContainerEntry);newOwner.Add<SuperchargedPatch.EntityPathReferenceMarker>();
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(57);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(58);multiDeletePlan.SetActual(newOwner);
 Check(WorldSyncCacheModule.FinalizeDynamicSpawn(multiDeletePlan,null)==null
  &&EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{59,57,58})
  &&regular._items.Contains(future1.ownerEntry)&&regular._items.Contains(future2.containerEntry),
  "Spawn finalization preserves future scheduler extras and checkpoint-minus-deletions allocator ordering until generic deletion");
 foreach(var entry in new[]{future1.ownerEntry,future1.containerEntry,future2.ownerEntry,future2.containerEntry}){
  regular._items.Remove(entry);EntitySerialisationRegistry.entries.Remove(entry.m_Header.m_uEntityID);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue((ushort)entry.m_Header.m_uEntityID);
 }
 var restorePlan=NativeKitchenCheckpoint.Prepare(multiDeleteWarp);restorePlan.Complete();WorldSyncCacheModule.AfterComplete(restorePlan);
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{53,54,55,56,57,58,59}),
  "Post-deletion initial recreation restores the exact checkpoint free-ID queue order");
 Check(regular._items.SequenceEqual(new[]{EntitySerialisationRegistry.entries[12],newOwnerEntry,fixedEntry,newContainerEntry}),
  "Post-deletion initial recreation restores exact target scheduler order before dynamic rebind");
 Check((int)Status(m)["dynamicRestores"]==1&&ReferenceEquals(EntitySerialisationRegistry.entries[2].m_GameObject,newOwner),
  "Initial recreation plus multiple future-only deletion pairs completes one exact dynamic rebind");
 m.Dispose();
}
{
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{59,60,61,62,63,64});
 EntitySerialisationEntry AddEntry(uint id,GameObject obj,ServerSynchroniser sync=null){
  var entry=new EntitySerialisationEntry{m_GameObject=obj};entry.m_Header.m_uEntityID=id;
  if(sync!=null)entry.m_ServerSynchronisedComponents._items.Add(sync);EntitySerialisationRegistry.entries.Add(id,entry);return entry;
 }
 (EntitySerialisationEntry ownerEntry,EntitySerialisationEntry containerEntry,GameObject owner,GameObject container) AddPair(uint ownerId,uint containerId,bool detached=false){
  var ownerObject=new GameObject();var physical=ownerObject.Add<PhysicalAttachment>();var serverAttachment=ownerObject.Add<ServerPhysicalAttachment>();
  var clientAttachment=ownerObject.Add<ClientPhysicalAttachment>();var containerObject=new GameObject();var objectContainer=containerObject.Add<ObjectContainer>();
  physical.m_container=objectContainer;var physics=containerObject.Add<ServerPhysicsObjectSynchroniser>();
  IParentable parent=p.GetComponent<AttachStation>();uint parentId=41;
  if(detached){ownerObject.transform.parent=containerObject.transform;serverAttachment.attached=false;clientAttachment.attached=false;parent=objectContainer;parentId=containerId;}
  else ownerObject.transform.parent=p.transform;
  clientAttachment.Setup(parent);var world=ownerObject.Add<ServerWorldObjectSynchroniser>();world.Setup();
  var message=(WorldObjectMessage)Get(world,"m_ServerData");message.ParentEntityID=parentId;
  var client=ownerObject.Add<ClientWorldObjectSynchroniser>();client.Setup(parent,parentId);
  var ownerEntry=AddEntry(ownerId,ownerObject,world);ownerEntry.m_ClientSynchronisedComponents._items.Add(client);
  var containerEntry=AddEntry(containerId,containerObject,physics);return(ownerEntry,containerEntry,ownerObject,containerObject);
 }
 var initial=AddPair(2,47,true);var fixedEntry=AddEntry(99,new GameObject());var dynamicTarget=AddPair(55,56);
 var crate=new GameObject();var dynamicPrefab=new GameObject();crate.Add<SpawnableEntityCollection>().Spawnables.Add(dynamicPrefab);
 AddEntry(30,crate);NativeDynamicWarpPlan.ObserveFixture(dynamicTarget.owner,dynamicPrefab);
 regular._items.Add(initial.ownerEntry);regular._items.Add(fixedEntry);regular._items.Add(initial.containerEntry);
 regular._items.Add(dynamicTarget.ownerEntry);regular._items.Add(dynamicTarget.containerEntry);
 var initialWorld=initial.owner.GetComponent<ServerWorldObjectSynchroniser>();
 Set(initialWorld,"m_bSentReliableRestPosition",false);Set(initialWorld,"m_bActive",true);
 Set(initialWorld,"m_bParentChanged",true);Set(initialWorld,"m_LastUnreliableActiveSend",99.5f);
 NativeSceneMetadata.InitialPhysicalAttachmentIds.Add(2);Capture(161);
 foreach(var entry in new[]{initial.ownerEntry,initial.containerEntry,dynamicTarget.ownerEntry,dynamicTarget.containerEntry}){
  regular._items.Remove(entry);EntitySerialisationRegistry.entries.Remove(entry.m_Header.m_uEntityID);
 }
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(2);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(47);
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(55);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(56);
 (EntitySerialisationEntry ownerEntry,EntitySerialisationEntry containerEntry,GameObject owner,GameObject container) AddFuture(){
  ushort ownerId=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();ushort containerId=EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue();
  var pair=AddPair(ownerId,containerId);regular._items.Add(pair.ownerEntry);regular._items.Add(pair.containerEntry);return pair;
 }
 var future1=AddFuture();var future2=AddFuture();
 var initialSpec=new EntityWarpSpec{EntityPathReference=new()};initialSpec.__isset.entityPathReference=true;
 initialSpec.EntityPathReference.Ids.Add(2);initialSpec.SpawningPath.AddRange(new[]{34,0,0});
 var dynamicSpec=new EntityWarpSpec{EntityPathReference=new()};dynamicSpec.__isset.entityPathReference=true;
 dynamicSpec.EntityPathReference.Ids.AddRange(new[]{30,1});dynamicSpec.SpawningPath.AddRange(new[]{30,0});
 var mixedWarp=new WarpSpec{Frame=161};mixedWarp.Entities.Add(initialSpec);mixedWarp.Entities.Add(dynamicSpec);
 mixedWarp.EntitiesToDelete.Add((int)future1.ownerEntry.m_Header.m_uEntityID);mixedWarp.EntitiesToDelete.Add((int)future2.ownerEntry.m_Header.m_uEntityID);
 WorldSyncCacheModule.BeforePrepare(mixedWarp);
 Check(NativeInitialAttachmentDeletionAuthorization.Authorizations==1
  &&NativeInitialAttachmentDeletionAuthorization.HistoricalOwnerId==2
  &&NativeInitialAttachmentDeletionAuthorization.HistoricalContainerId==47,
  "Mixed initial/dynamic recreation authenticates only the initial pair for future deletion capability");
 var mixedPlan=NativeDynamicWarpPlan.MixedFixture(initialSpec,
  new[]{new[]{typeof(PhysicalAttachment)},initial.owner.GetComponents<Component>().Select(c=>c.GetType()).ToArray()},
  dynamicSpec,dynamicTarget.owner.GetComponents<Component>().Select(c=>c.GetType()).ToArray(),dynamicPrefab);
 AddPlanRemoval(mixedPlan,future1.owner,future1.container);AddPlanRemoval(mixedPlan,future2.owner,future2.container);
 WorldSyncCacheModule.BeforeDynamicSpawn(mixedPlan);
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{63,64,2,47,55,56}),
  "Mixed recreation reserves initial scratch IDs and both historical owner/body pairs in native plan order");
 foreach(var expected in new ushort[]{63,64,2,47,55,56})Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.Dequeue()==expected,
  "Mixed recreation allocator consumes the next authenticated registration ID");
 var newInitial=AddPair(2,47,true);var newDynamic=AddPair(55,56);
 regular._items.Add(newInitial.ownerEntry);regular._items.Add(newInitial.containerEntry);
 regular._items.Add(newDynamic.ownerEntry);regular._items.Add(newDynamic.containerEntry);
 newInitial.owner.Add<SuperchargedPatch.EntityPathReferenceMarker>();newDynamic.owner.Add<SuperchargedPatch.EntityPathReferenceMarker>();
 EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(63);EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue(64);
 mixedPlan.SetActual(0,newInitial.owner);mixedPlan.SetActual(1,newDynamic.owner);
 Check(WorldSyncCacheModule.FinalizeDynamicSpawn(mixedPlan,null)==null,
  "Mixed recreation finalizes both exact historical scheduler incarnations in one native spawn transaction");
 foreach(var entry in new[]{future1.ownerEntry,future1.containerEntry,future2.ownerEntry,future2.containerEntry}){
  regular._items.Remove(entry);EntitySerialisationRegistry.entries.Remove(entry.m_Header.m_uEntityID);
  EntitySerialisationRegistry.m_ServerFreeEntityIDList.Enqueue((ushort)entry.m_Header.m_uEntityID);
 }
 var restorePlan=NativeKitchenCheckpoint.Prepare(mixedWarp);restorePlan.Complete();WorldSyncCacheModule.AfterComplete(restorePlan);
 Check(regular._items.SequenceEqual(new[]{EntitySerialisationRegistry.entries[12],newInitial.ownerEntry,fixedEntry,
  newInitial.containerEntry,newDynamic.ownerEntry,newDynamic.containerEntry}),
  "Mixed recreation restores exact checkpoint scheduler order across both rebound pairs");
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(new ushort[]{59,60,61,62,63,64})
  &&ReferenceEquals(EntitySerialisationRegistry.entries[2],newInitial.ownerEntry)
  &&ReferenceEquals(EntitySerialisationRegistry.entries[55],newDynamic.ownerEntry),
  "Mixed recreation restores checkpoint allocator order and both exact historical owner IDs");
 m.Dispose();
}
{
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{53,54,55});
 EntitySerialisationEntry AddDynamic(uint id,GameObject obj,ServerSynchroniser sync){
  var e=new EntitySerialisationEntry{m_GameObject=obj};e.m_Header.m_uEntityID=id;e.m_ServerSynchronisedComponents._items.Add(sync);
  EntitySerialisationRegistry.entries.Add(id,e);regular._items.Add(e);return e;
 }
 var owner=new GameObject();owner.transform.parent=p.transform;owner.transform.localRotation=new(){y=.25f,w=.75f};
 var physical=owner.Add<PhysicalAttachment>();owner.Add<ServerPhysicalAttachment>();owner.Add<ClientPhysicalAttachment>().Setup(p.GetComponent<AttachStation>());
 var world=owner.Add<ServerWorldObjectSynchroniser>();world.Setup();var clientWorld=owner.Add<ClientWorldObjectSynchroniser>();clientWorld.Setup(p.GetComponent<AttachStation>(),41);
 var container=new GameObject();physical.m_container=container.Add<ObjectContainer>();
 container.transform.localPosition=new(){z=1};physical.m_container.position=new(){z=2};
 var ownerEntry=AddDynamic(51,owner,world);ownerEntry.m_ClientSynchronisedComponents._items.Add(clientWorld);AddDynamic(52,container,container.Add<ServerPhysicsObjectSynchroniser>());
 int bodyRestoresBefore=BodyRestoreModule.DynamicRestores;Capture(161);
 owner.transform.parent=new GameObject().transform;owner.transform.localRotation=new(){w=1};container.transform.localPosition=new(){z=9};Set(clientWorld,"m_ParentEntityID",99u);Set(clientWorld,"m_Parent",new GameObject().Add<AttachStation>());Set(world,"m_bSentReliableRestPosition",false);Set(world,"m_bParentChanged",true);Prepare(161);
 owner.transform.parent=p.transform;Restore(161);
 Check(BodyRestoreModule.DynamicRestores==bodyRestoresBefore+1&&BodyRestoreModule.LastRestoredId==52,
  "Surviving same-incarnation dynamic container consumes its captured body checkpoint");
 Check(owner.transform.localRotation.y==.25f&&owner.transform.localRotation.w==.75f
  &&(int)Status(m)["survivingDynamicRestores"]==1,
  "Surviving dynamic owner restores its exact checkpoint local pose after core attachment callbacks");
 Check(container.transform.localPosition.z==9&&physical.m_container.position.z==2,
  "Dynamic body restore leaves the separately stored Transform discrepancy visible until paused maintenance");
 WorldSyncCacheModule.BeforeTasLateUpdate();
 Check(container.transform.localPosition.z==1&&physical.m_container.position.z==2
  &&(int)Status(m)["pausedDynamicTransformCorrections"]==1,
  "Paused rewind maintenance restores the exact dynamic container Transform without changing Rigidbody position");
 Check((uint)Get(clientWorld,"m_ParentEntityID")==41u&&ReferenceEquals(Get(clientWorld,"m_Parent"),p.GetComponent<AttachStation>())
  &&(bool)Get(world,"m_bSentReliableRestPosition")&&!(bool)Get(world,"m_bParentChanged"),
  "Surviving dynamic owner restores its exact server/client WorldObject caches");
 m.Dispose();
}
{
 var(m,s,p)=Setup();var scheduler=Get(ControllerHandler.MultiplayerController,"m_ServerSync");
 var regular=(FastList<EntitySerialisationEntry>)Get(scheduler,"m_EntitiesList");
 EntitySerialisationRegistry.m_ServerFreeEntityIDList=new(new ushort[]{53,54,55});
 EntitySerialisationEntry AddDynamic(uint id,GameObject obj,ServerSynchroniser sync){
  var e=new EntitySerialisationEntry{m_GameObject=obj};e.m_Header.m_uEntityID=id;e.m_ServerSynchronisedComponents._items.Add(sync);
  EntitySerialisationRegistry.entries.Add(id,e);regular._items.Add(e);return e;
 }
 var container=new GameObject();var body=container.Add<ObjectContainer>();container.transform.localPosition=new(){z=1};body.position=new(){z=2};
 var owner=new GameObject();var physical=owner.Add<PhysicalAttachment>();physical.m_container=body;owner.transform.parent=container.transform;
 owner.transform.localPosition=new(){x=.25f};owner.transform.localRotation=new(){y=.5f,w=.5f};
 var serverPhysical=owner.Add<ServerPhysicalAttachment>();serverPhysical.attached=false;
 var clientPhysical=owner.Add<ClientPhysicalAttachment>();clientPhysical.Setup(p.GetComponent<AttachStation>());clientPhysical.attached=false;
 var world=owner.Add<ServerWorldObjectSynchroniser>();world.Setup();var message=(WorldObjectMessage)Get(world,"m_ServerData");message.ParentEntityID=52;
 Set(world,"m_bSentReliableRestPosition",false);Set(world,"m_bParentChanged",true);Set(world,"m_LastUnreliableActiveSend",99.2f);
 var clientWorld=owner.Add<ClientWorldObjectSynchroniser>();clientWorld.Setup(body,52);
 var ownerEntry=AddDynamic(51,owner,world);ownerEntry.m_ClientSynchronisedComponents._items.Add(clientWorld);
 var containerEntry=AddDynamic(52,container,container.Add<ServerPhysicsObjectSynchroniser>());
 int bodyRestoresBefore=BodyRestoreModule.DynamicRestores;Capture(161);
 var latest=(Dictionary<string,object>)Status(m)["latest"];
 Check(latest["unsupported"]==null&&((object[])latest["items"]).Length==2,
  "A settled loose dynamic owner/container pair is admitted with exact surviving identity");
 var schedulerStatus=(Dictionary<string,object>)latest["scheduler"];
 var dynamicOwner=((object[])schedulerStatus["regular"]).Cast<Dictionary<string,object>>()
  .Single(row=>(uint)row["id"]==51u);var dynamicPose=(Dictionary<string,object>)dynamicOwner["dynamicOwnerPose"];
 Check(!(bool)dynamicPose["attached"]&&(bool)dynamicPose["detachedOnContainer"]&&(int)dynamicPose["parentEntityId"]==52,
  "Loose dynamic checkpoint records detached physical-container parenting explicitly");
 var savedClient=(Dictionary<string,object>)((Dictionary<string,object>)((object[])latest["items"])
  .Cast<Dictionary<string,object>>().Single(row=>(int)row["id"]==51))["client"];
 Check((uint)savedClient["parentId"]==52u&&(int)savedClient["parentComponentId"]==body.GetInstanceID(),
  "Loose dynamic checkpoint pins the exact registered client ObjectContainer parent");
 owner.transform.parent=p.transform;serverPhysical.attached=true;clientPhysical.attached=true;
 Set(clientPhysical,"m_Parent",null);Set(world,"m_CachedParentTransform",p.transform);message.ParentEntityID=41;
 Set(world,"m_bSentReliableRestPosition",true);Set(world,"m_bParentChanged",false);Set(world,"m_LastUnreliableActiveSend",0f);
 Set(clientWorld,"m_Parent",p.GetComponent<AttachStation>());Set(clientWorld,"m_ParentEntityID",41u);
 container.transform.localPosition=new(){z=9};Prepare(161);
 // Model the original core attachment callback before the sidecar's post-core restore.
 owner.transform.parent=container.transform;serverPhysical.attached=false;clientPhysical.attached=false;
 Restore(161);WorldSyncCacheModule.BeforeTasLateUpdate();
 Check(BodyRestoreModule.DynamicRestores==bodyRestoresBefore+1&&owner.transform.parent==container.transform
  &&owner.transform.localPosition.x==.25f&&owner.transform.localRotation.y==.5f,
  "Surviving loose dynamic restore reconstructs exact owner/container pose through the normal body service");
 Check(!serverPhysical.attached&&!clientPhysical.attached&&ReferenceEquals(Get(clientPhysical,"m_Parent"),p.GetComponent<AttachStation>()),
  "Surviving loose dynamic restore preserves detached flags and inert client PhysicalAttachment parent history");
 Check(ReferenceEquals(Get(world,"m_CachedParentTransform"),container.transform)&&message.ParentEntityID==52
  &&ReferenceEquals(Get(clientWorld,"m_Parent"),body)&&(uint)Get(clientWorld,"m_ParentEntityID")==52u&&(bool)Get(clientWorld,"m_bHasParent"),
  "Surviving loose dynamic restore reconstructs exact server/client WorldObject container-parent caches");
 Check(!(bool)Get(world,"m_bSentReliableRestPosition")&&(bool)Get(world,"m_bParentChanged")
  &&(float)Get(world,"m_LastUnreliableActiveSend")==99.2f,
  "Surviving loose dynamic restore preserves the admitted relative pending-rest deadline state");
 regular._items.Remove(ownerEntry);regular._items.Remove(containerEntry);EntitySerialisationRegistry.entries.Remove(51);EntitySerialisationRegistry.entries.Remove(52);
 var queueBefore=EntitySerialisationRegistry.m_ServerFreeEntityIDList.ToArray();
 Reject(()=>Prepare(161),"Loose dynamic checkpoint rejects cross-incarnation recreation before allocator or native mutation");
 Check(EntitySerialisationRegistry.m_ServerFreeEntityIDList.SequenceEqual(queueBefore),
  "Rejected loose cross-incarnation restore leaves the native free-ID queue unchanged");
 m.Dispose();
}
{
 var(m,s,p)=Setup();var sched=Get(ControllerHandler.MultiplayerController,"m_ServerSync");Set(sched,"m_fNextUpdate",float.NaN);Capture(161);
 Reject(()=>Prepare(161),"Nonfinite captured scheduler residual rejects without rewriting it");m.Dispose();
}
{
 // Installed native float recurrence, with a synthetic initial residual chosen
 // to match the observed first poll at offset26. It does not claim that private
 // residual was logged in the old trace.
 float Tick(float x,out bool poll){x+=1f/60f;poll=x>=.1f;if(poll)x-=.1f;return x;}
 int FirstAfter(float x){for(int f=1;f<=56;f++){x=Tick(x,out bool p);if(f>=24&&p)return f;}return -1;}
 float originalResidual=.06666667f,futureResidual=originalResidual;
 for(int n=0;n<56+136+124;n++)futureResidual=Tick(futureResidual,out _);
 Check(FirstAfter(originalResidual)==26&&FirstAfter(futureResidual)==28,"Native six-frame residual model reproduces observed +2 packet shift after316 intervening frames");
 var(m,s,p)=Setup();var sched=Get(ControllerHandler.MultiplayerController,"m_ServerSync");Set(sched,"m_fNextUpdate",originalResidual);Capture(161);
 Set(sched,"m_fNextUpdate",futureResidual);Restore(161);
 Check(FirstAfter((float)Get(sched,"m_fNextUpdate"))==26,"Actual module restoration recovers original native recurrence without new clock or delay");m.Dispose();
}
var corePath="artifacts/framework-build-c7bn-logical-world-rest/SuperchargedPatch.dll";
using var native=AssemblyDefinition.ReadAssembly(nativePath);
var type=native.MainModule.Types.Single(t=>t.FullName=="Team17.Online.Multiplayer.Messaging.ServerWorldObjectSynchroniser");
var methodNames=new[]{"GetServerUpdate","PopulateMessage","ResumePositions","RefreshParent"};
var methods=methodNames.Select(n=>type.Methods.Single(m=>m.Name==n)).ToArray();
string[] Calls(MethodDefinition m)=>m.Body.Instructions.Select(i=>i.Operand).OfType<MethodReference>().Select(r=>r.FullName).ToArray();
string[] Stores(MethodDefinition m)=>m.Body.Instructions.Where(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Stfld).Select(i=>((FieldReference)i.Operand).Name).ToArray();
Check(Stores(methods[2]).SequenceEqual(new[]{"m_bSyncPositions","m_bSentReliableRestPosition","m_LastUnreliableActiveSend"}),"Installed ResumePositions resets exactly the three expected sync-cache fields");
Check(methods[2].Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_R4&&(float)i.Operand==0),"Installed ResumePositions stores the zero absolute last-send timestamp");
Check(Calls(methods[0]).Count(x=>x.Contains("UnityEngine.Time::get_time"))==2&&Calls(methods[0]).Any(x=>x.Contains("SendServerEvent")),"Installed native rest branch reads Time.time twice and emits real SendServerEvent");
Check(methods[0].Body.Instructions.Any(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_R4&&(float)i.Operand==1),"Installed reliable rest threshold is one native Unity second");
var attach=native.MainModule.Types.Single(t=>t.Name=="ServerPhysicalAttachment").Methods.Single(m=>m.Name=="Attach");
Check(Calls(attach).Any(x=>x.Contains("ServerWorldObjectSynchroniser::ResumePositions")),"Installed physical attachment callback actually invokes the cache reset");
var schedulerType=native.MainModule.Types.Single(t=>t.Name=="ServerSynchronisationScheduler");
var clientType=native.MainModule.Types.Single(t=>t.Name=="ClientWorldObjectSynchroniser");
var clientStart=clientType.Methods.Single(m=>m.Name=="StartSynchronising");
var parentStores=clientStart.Body.Instructions.Where(i=>i.OpCode.Code==Code.Stfld&&i.Operand is FieldReference f&&f.Name=="m_Parent").ToArray();
Check(parentStores.Length==1&&parentStores[0].Previous.OpCode.Code==Code.Ldnull&&clientStart.Body.Instructions.Any(i=>i.OpCode.Code==Code.Stfld&&i.Operand is FieldReference f&&f.Name=="m_ParentEntityID"&&i.Offset>parentStores[0].Offset),"Installed native startup retains null parent cache while recording an observed parent ID");
var parenting=clientType.Methods.Single(m=>m.Name=="ParentingLogic");
Check(parenting.Body.Instructions.Select(i=>i.Operand).OfType<FieldReference>().Any(f=>f.Name=="m_ParentEntityID")&&Calls(parenting).Any(c=>c.Contains("DoReparenting")),"Installed client parenting decision reads its separate cached parent ID");
var reparent=clientType.Methods.Single(m=>m.Name=="DoReparenting");
Check(Calls(reparent).Any(c=>c.Contains("UnityEngine.Rigidbody::set_position"))&&Calls(reparent).Any(c=>c.Contains("UnityEngine.Rigidbody::set_rotation")),"Installed native DoReparenting writes attachment-container body pose");
var schedulerUpdate=schedulerType.Methods.Single(m=>m.Name=="Update");
Check(Calls(schedulerUpdate).Count(c=>c.Contains("UnityEngine.Time::get_deltaTime"))==1&&Stores(schedulerUpdate).Contains("m_fNextUpdate")&&Stores(schedulerUpdate).Contains("m_fNextFastUpdate"),"Installed scheduler advances both residuals from native deltaTime");
Check(schedulerUpdate.Body.Instructions.Where(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_R4).Select(i=>(float)i.Operand).Contains(.1f)
 &&schedulerUpdate.Body.Instructions.Where(i=>i.OpCode.Code==Mono.Cecil.Cil.Code.Ldc_R4).Select(i=>(float)i.Operand).Contains(.0333333351f),"Installed normal/fast delays remain exact0.1 and0.0333333351 seconds");
foreach(string n in new[]{"SynchroniseEntity","SynchroniseForRecipient"}){
 var sm=schedulerType.Methods.Single(m=>m.Name==n);var calls=Calls(sm);
 Check(calls.First(c=>c.Contains("FastList`1<Team17.Online.Multiplayer.Messaging.Serialisable>" )||c.Contains("GetServerUpdate")).Contains("::Clear"),"Installed scratch payload list clears before payload use: "+n);
}
using var core=AssemblyDefinition.ReadAssembly(corePath);
var kitchen=core.MainModule.Types.Single(t=>t.Name=="NativeKitchenCheckpoint");
Check(kitchen.Fields.Any(f=>f.Name=="history")&&kitchen.Fields.Any(f=>f.Name=="roundIdentity"),"Frozen X has snapshot history plus actual round identity");
var plan=kitchen.NestedTypes.Single(t=>t.Name=="RestorePlan");
Check(plan.Fields.Any(f=>f.Name=="snapshot")&&plan.Methods.Any(m=>m.Name=="Complete"),"Frozen X completed plan retains the exact selected snapshot reference");
var unreal=core.MainModule.Types.Single(t=>t.Name=="UnrealTimePatch");
var restClockPatch=unreal.NestedTypes.Single(t=>t.Name=="WorldObjectRestClock");
var restTarget=restClockPatch.Methods.Single(m=>m.Name=="TargetMethod");
var restTranspiler=restClockPatch.Methods.Single(m=>m.Name=="Transpiler");
var restTargetStrings=restTarget.Body.Instructions.Select(i=>i.Operand).OfType<string>().ToArray();
var restTranspilerStrings=restTranspiler.Body.Instructions.Select(i=>i.Operand).OfType<string>().ToArray();
Check(restClockPatch.CustomAttributes.Any(a=>a.AttributeType.Name=="HarmonyPatch")
 &&restTargetStrings.Contains("GetServerUpdate")
 &&Calls(restTarget).Any(c=>c.Contains("HarmonyLib.AccessTools::Method")),
 "Core installs a fail-closed patch on the declared parameterless WorldObject update method");
Check(restTranspilerStrings.Contains("time")&&restTranspilerStrings.Contains("LogicalRealtime")
 &&restTranspiler.Body.Instructions.Any(i=>i.OpCode.Code==Code.Ldc_I4_2)
 &&Calls(restTranspiler).Any(c=>c.Contains("System.InvalidOperationException::.ctor")),
 "Core transpiler requires and replaces exactly both native Time.time reads with the checkpointed logical clock");
var warp=core.MainModule.Types.Single(t=>t.Name=="WarpHandler").Methods.Single(m=>m.Name=="HandleWarpRequestIfAny");
var warpCalls=Calls(warp);
Check(Array.FindIndex(warpCalls,s=>s.Contains("RestorePlan::Complete"))<Array.FindIndex(warpCalls,s=>s.Contains("ClearCacheAfterWarp")),"External Complete postfix executes before acknowledgement frame/cache reset");
var summary=JsonDocument.Parse(File.ReadAllText("artifacts/framework-migration/native-x-v11/offline-exact-frames/summary.json")).RootElement;
var packet=summary.GetProperty("pickupMessageDifferences")[0];
Check(packet.GetProperty("frame").GetInt32()==165&&!packet.GetProperty("rawMultisetEqual").GetBoolean(),"Pinned live pickup replay differs by an extra packet, not merely ordering");
var originalMessages=packet.GetProperty("originalRaw").EnumerateArray().Select(x=>x.GetRawText()).ToArray();
var replay=packet.GetProperty("replayRaw").EnumerateArray().Select(x=>x.GetRawText()).ToArray();
Check(originalMessages.SequenceEqual(replay.Skip(1)),"Remaining actual native and auxiliary messages retain identical order");
Check(packet.GetProperty("replayRaw")[0].GetProperty("Message").GetString()=="AwIUwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD+AAAAA=","Pinned extra native WorldObject packet identifies exact plate12/source41 pose");
var modulePath="framework-run/modules/WorldSyncCache-r13n-logical-rest-clock-core-c7bnr2/WorldSyncCache.r13n-logical-rest-clock-core-c7bnr2.dll";
using var compiledModule=AssemblyDefinition.ReadAssembly(modulePath);
var moduleType=compiledModule.MainModule.Types.Single(t=>t.Name=="WorldSyncCacheModule");
Check(moduleType.Methods.Single(m=>m.Name=="BeforeCaptureFrame").Parameters.Select(p=>(p.Name,p.ParameterType.FullName)).SequenceEqual(new[]{("__0","System.Int32"),("__state","System.Object&")})
 &&moduleType.Methods.Single(m=>m.Name=="AfterCaptureFrame").Parameters.Select(p=>(p.Name,p.ParameterType.FullName)).SequenceEqual(new[]{("__0","System.Int32"),("__state","System.Object")}),"Compiled CLR2 paired Harmony capture state contract is exact");
var moduleCalls=moduleType.Methods.Where(m=>m.HasBody).SelectMany(Calls).ToArray();
var restoreCalls=Calls(moduleType.Methods.Single(m=>m.Name=="Restore"));
Check(Array.FindIndex(restoreCalls,c=>c.Contains("UnrealTimePatch::CaptureLogicalRealtime"))
 <Array.FindIndex(restoreCalls,c=>c.Contains("NativeSchedulerSnapshot::RestoreDynamicAfterCore")),
 "Logical-clock postcondition is checked before any dynamic scheduler/body restoration");
Check(!Calls(moduleType.Methods.Single(m=>m.Name=="BeforeAuthoringResume")).Any(c=>c.Contains("FieldInfo::SetValue")),
 "Resume validation is read-only and contains no reflected WorldObject cache write");
Check(moduleCalls.Count(c=>c.Contains("HarmonyLib.Harmony::Patch("))==7
 &&moduleCalls.Any(c=>c.Contains("UnrealTimePatch::CaptureLogicalRealtime"))
 &&!moduleCalls.Any(c=>c.Contains("UnityEngine.Time::get_time")||c.Contains("UnityEngine.Time::set_")||c.Contains("SendServerEvent")||c.Contains("ServerWorldObjectSynchroniser::GetServerUpdate")),
 "Compiled module installs only checkpoint/failure/dynamic-spawn/read-only-resume/paused-LateUpdate hooks, restores exact cache bits, and never reads/writes Unity time or invokes/suppresses native event generation");
var livePath="artifacts/framework-migration/native-x-v11b/world-sync-r1a-status.json";
var live=JsonDocument.Parse(File.ReadAllText(livePath)).RootElement.GetProperty("result").GetProperty("result").GetProperty("latest");
Check(live.GetProperty("unsupported").ValueKind==JsonValueKind.Null&&live.GetProperty("items").GetArrayLength()==13
 &&live.GetProperty("items").EnumerateArray().All(x=>x.GetProperty("sentReliable").GetBoolean()&&!x.GetProperty("active").GetBoolean()&&!x.GetProperty("parentChanged").GetBoolean()),"Actual fresh native r1a observation establishes all13 initial attachments already reliably settled (not a replay proof)");
Check(live.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetInt32()==12).GetProperty("messageParent").GetInt32()==41,"Actual native cache confirms plate12's source parent41");
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
var report=new{passed=true,count=checks.Count,checks,nativeAssemblySha256=Hash(nativePath),coreSha256=Hash(corePath),
 moduleSha256=Hash(modulePath),liveSettledReceiptSha256=Hash(livePath),
 nativeMethods=methods.Select(m=>new{m.FullName,il=m.Body.Instructions.Select(i=>new{offset=i.Offset,opcode=i.OpCode.Name,operand=i.Operand?.ToString()})}),
 schedulerMethods=schedulerType.Methods.Where(m=>new[]{"Update","SynchroniseList","SynchroniseEntity","SynchroniseForRecipient"}.Contains(m.Name)).Select(m=>new{m.FullName,il=m.Body.Instructions.Select(i=>new{offset=i.Offset,opcode=i.OpCode.Name,operand=i.Operand?.ToString()})}),
 scope="Actual external module logic with managed native/Harmony stubs; installed native and frozen-X IL verified. No game calls. Native cache values and strict replay still require root-owned experiment."};
File.WriteAllText("artifacts/framework-world-sync-cache-r13n-core-c7bn-tests.json",JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true})+"\n");
Console.WriteLine("PASS "+checks.Count+" WorldSync cache checks");

namespace SuperchargedPatch
{
 public static class NativeInitialAttachmentDeletionAuthorization
 {
  public static int Authorizations,HistoricalOwnerId,HistoricalContainerId;
  public static Hpmv.WarpSpec Warp;
  public static int[] FutureOwners=Array.Empty<int>();
  public static void ResetFixture(){Authorizations=0;HistoricalOwnerId=0;HistoricalContainerId=0;Warp=null;FutureOwners=Array.Empty<int>();}
  public static void Authorize(Hpmv.WarpSpec warp,int historicalOwnerId,int historicalContainerId,IEnumerable<int> futureDeletionOwnerIds){
   Authorizations++;Warp=warp;HistoricalOwnerId=historicalOwnerId;HistoricalContainerId=historicalContainerId;FutureOwners=futureDeletionOwnerIds.ToArray();
  }
 }
}
