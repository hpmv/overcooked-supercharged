using Hpmv;
using SuperchargedPatch;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using PathRef=SuperchargedPatch.EntityPathReference;

var checks=new List<string>();
void Check(bool ok,string text) { if(!ok)throw new Exception(text);checks.Add(text); }
void Reject(Action action,string text) { try{action();}catch(InvalidOperationException){checks.Add(text);return;}throw new Exception("Expected rejection: "+text); }
GameObject root=null,raw=null,prepared=null,rawObserved=null;
GameObject Clone(string name,bool workable) {
    var obj=new GameObject(name+"(Clone)"); obj.AddComponent<Collider>();
    var physical=obj.AddComponent<PhysicalAttachment>();physical.m_container=new GameObject(name+"_Rigidbody").AddComponent<Rigidbody>();
    obj.AddComponent<ServerPhysicalAttachment>();
    if(workable){obj.AddComponent<ServerWorkableItem>();obj.AddComponent<SpawnableEntityCollection>().Values.Add(prepared);}
    return obj;
}
void Reset() {
    EntitySerialisationRegistry.m_EntitiesList._items.Clear(); EntitySerialisationRegistry.Next=100;
    UnityEngine.Object.Flow=new ServerKitchenFlowControllerBase();
    NetworkUtils.Events.Clear();NetworkUtils.Factories.Clear();NetworkUtils.SpawnCalls=0;NetworkUtils.FailAfterRegisterAt=-1;NetworkUtils.DeferDestroy=false;NetworkUtils.DeferPhysicsOnDestroy=false;NetworkUtils.Pending.Clear();
    ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms.Clear();
    root=new GameObject("Crate"); raw=new GameObject("Raw");prepared=new GameObject("Prepared");
    root.AddComponent<SpawnableEntityCollection>().Values.Add(raw);EntitySerialisationRegistry.Add(root,61);
    NetworkUtils.Factories[raw]=()=>Clone("Raw",true);NetworkUtils.Factories[prepared]=()=>Clone("Prepared",false);
    rawObserved=NetworkUtils.ServerSpawnPrefab(root,raw);
    var preparedObserved=NetworkUtils.ServerSpawnPrefab(rawObserved,prepared);
    NetworkUtils.DestroyObject(preparedObserved.GetComponent<PhysicalAttachment>().m_container.gameObject);NetworkUtils.DestroyObject(preparedObserved);
    NetworkUtils.DestroyObject(rawObserved.GetComponent<PhysicalAttachment>().m_container.gameObject);NetworkUtils.DestroyObject(rawObserved);
    NetworkUtils.Events.Clear();NetworkUtils.SpawnCalls=0;
}
Type[] ObservedTypes(GameObject prefab) {
    var profiles=(System.Collections.IDictionary)typeof(NativeDynamicWarpPlan)
        .GetField("profiles",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic).GetValue(null);
    var profile=profiles[prefab];
    return (Type[])profile.GetType().GetField("Components",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public).GetValue(profile);
}
EntityWarpSpec Keep()=>new(){EntityId=61};
EntityWarpSpec New(bool chopped=false,int ordinal=0)=>new(){SpawningPath=chopped?new(){61,0,0}:new(){61,0},
    EntityPathReference=new Hpmv.EntityPathReference{Ids=chopped?new(){61,ordinal,0}:new(){61,ordinal}},
    WorkableItem=chopped?null:new(){Progress=3}};
EntityWarpSpec InitialNew(int id=2){var value=New();value.EntityPathReference.Ids=new(){id};return value;}
WarpSpec Warp(params EntityWarpSpec[] targets)=>new(){Entities=targets.ToList(),EntitiesToDelete=new(),Frame=10};
NativeDynamicWarpPlan Plan(WarpSpec spec,Dictionary<PathRef,EntitySerialisationEntry> map=null,Action flush=null,
    Func<EntityWarpSpec,PathRef> missingFixedEntityOwnerPath=null,
    Func<EntityWarpSpec,EntitySerialisationEntry,NativeInitialAttachmentSpawnChain> latentInitialFactory=null,
    Func<EntityWarpSpec,bool> ignoreAbsentFixedEntity=null)=>NativeDynamicWarpPlan.Prepare(
        spec,map??new(),flush??NetworkUtils.Flush,missingFixedEntityOwnerPath,latentInitialFactory,ignoreAbsentFixedEntity);

Reset(); var map=new Dictionary<PathRef,EntitySerialisationEntry>();var plan=Plan(Warp(Keep(),New()),map);
Check(NetworkUtils.SpawnCalls==0,"complete preflight does not instantiate");plan.Spawn();plan.DeleteAndVerify();
Check(map.Count==1 && map.Single().Key.ToString()=="61.0","raw respawn maps exact historical logical path");
Check(EntitySerialisationRegistry.m_EntitiesList.Count==3,"raw respawn retains exactly root,item,physical container");
Check(map.Single().Value.m_GameObject.GetComponent<EntityPathReferenceMarker>()!=null,"native output marker installed");

Reset();
var absentResidue=new EntityWarpSpec{EntityId=999};
plan=Plan(Warp(Keep(),absentResidue),ignoreAbsentFixedEntity:value=>ReferenceEquals(value,absentResidue));
plan.Spawn();plan.DeleteAndVerify();
Check(EntitySerialisationRegistry.GetEntry(999)==null,
    "qualified absent fixed controller residue is excluded from dynamic target construction");
var liveRow=Keep();
Reject(()=>Plan(Warp(liveRow),ignoreAbsentFixedEntity:value=>ReferenceEquals(value,liveRow)),
    "live fixed row cannot be ignored by a caller predicate");
var duplicateResidueA=new EntityWarpSpec{EntityId=999};var duplicateResidueB=new EntityWarpSpec{EntityId=999};
Reject(()=>Plan(Warp(Keep(),duplicateResidueA,duplicateResidueB),ignoreAbsentFixedEntity:value=>
    ReferenceEquals(value,duplicateResidueA)||ReferenceEquals(value,duplicateResidueB)),
    "duplicate ignored fixed residues fail closed");
var referenceToResidue=new EntityWarpSpec{EntityId=61,AttachStation=new(){Item=new(){EntityId=999}}};
Reject(()=>Plan(Warp(referenceToResidue,absentResidue),ignoreAbsentFixedEntity:value=>ReferenceEquals(value,absentResidue)),
    "retained authoring references cannot target an ignored absent fixed body");

Reset();
var duplicatePrefab=new GameObject("DuplicateComponents");root.GetComponent<SpawnableEntityCollection>().Values[0]=duplicatePrefab;
NetworkUtils.Factories[duplicatePrefab]=()=>{var value=Clone("DuplicateComponents",false);value.AddComponent<Collider>();return value;};
var duplicateObserved=NetworkUtils.ServerSpawnPrefab(root,duplicatePrefab);
var duplicateLiveTypes=duplicateObserved.GetComponents<Component>().Select(value=>value.GetType()).ToArray();
var duplicateObservedTypes=ObservedTypes(duplicatePrefab);
Check(duplicateObservedTypes.SequenceEqual(duplicateLiveTypes)
    &&duplicateObservedTypes.Count(type=>type==typeof(Collider))==2,
    "spawn observation retains exact ordered component multiplicity instead of collapsing duplicate collider types");

Reset();
UnityEngine.Object.Flow=null;
var earlyRawObserved=NetworkUtils.ServerSpawnPrefab(root,raw);
var earlyPreparedObserved=NetworkUtils.ServerSpawnPrefab(earlyRawObserved,prepared);
NetworkUtils.DestroyObject(earlyPreparedObserved.GetComponent<PhysicalAttachment>().m_container.gameObject);
NetworkUtils.DestroyObject(earlyPreparedObserved);
NetworkUtils.DestroyObject(earlyRawObserved.GetComponent<PhysicalAttachment>().m_container.gameObject);
NetworkUtils.DestroyObject(earlyRawObserved);
NetworkUtils.Events.Clear();NetworkUtils.SpawnCalls=0;
UnityEngine.Object.Flow=new ServerKitchenFlowControllerBase();
map=new();plan=Plan(Warp(Keep(),New(true)),map);
Check(NetworkUtils.SpawnCalls==0,"early startup observations survive CLR-null to new-flow adoption");
plan.Spawn();plan.DeleteAndVerify();
Check(map.Single().Key.ToString()=="61.0.0","adopted early startup chain remains authoring-valid");

Reset();
UnityEngine.Object.Flow=new ServerKitchenFlowControllerBase();
Reject(()=>Plan(Warp(Keep(),New())),"direct flow replacement clears prior-scene spawn profiles");

Reset();map=new();int missingContainerId=(int)EntitySerialisationRegistry.Next+1;
var missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
PathRef ExactMissingContainer(EntityWarpSpec value)=>value.EntityId==missingContainerId?new PathRef(new[]{61,0}):null;
plan=Plan(Warp(Keep(),New(),missingContainer),map,null,ExactMissingContainer);
Check(EntitySerialisationRegistry.GetEntry((uint)missingContainerId)==null,
    "qualified missing fixed container remains absent throughout preflight");
plan.Spawn();
Check(EntitySerialisationRegistry.GetEntry((uint)missingContainerId)!=null,
    "owner spawn creates the qualified missing fixed container");
plan.BindRecreatedInitialAttachments();plan.DeleteAndVerify();
Check(ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)missingContainerId).m_GameObject,
    map.Single().Value.m_GameObject.GetComponent<PhysicalAttachment>().m_container.gameObject),
    "qualified missing fixed container binds to the spawned owner's physical container");

Reset();map=new();missingContainerId=(int)EntitySerialisationRegistry.Next+1;
missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
plan=Plan(Warp(Keep(),InitialNew(),missingContainer),map,null,
    value=>value.EntityId==missingContainerId?new PathRef(new[]{2}):null);
plan.Spawn();plan.BindRecreatedInitialAttachments();plan.DeleteAndVerify();
Check(map.Single().Key.ToString()=="2",
    "qualified initial attachment owner alone may restore its one-element historical path");

Reset();
Reject(()=>Plan(Warp(Keep(),InitialNew())),
    "generic dynamic spawn cannot use a one-element logical path");
missingContainerId=(int)EntitySerialisationRegistry.Next+1;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
Reject(()=>Plan(Warp(Keep(),InitialNew(3),missingContainer),null,null,
    value=>new PathRef(new[]{2})),
    "wrong one-element owner path cannot claim initial-root recreation authority");
Reject(()=>Plan(Warp(Keep(),InitialNew(),InitialNew(),missingContainer),null,null,
    value=>new PathRef(new[]{2})),
    "multiple one-element owner targets cannot claim initial-root recreation authority");

Reset();map=new();
var latentStack=new GameObject("LatentStack");var latentPlate=new GameObject("LatentPlate");
root.GetComponent<SpawnableEntityCollection>().Values[0]=latentStack;
GameObject latentStackClone=null;
NetworkUtils.Factories[latentStack]=()=>{var value=Clone("LatentStack",false);latentStackClone=value;value.AddComponent<SpawnableEntityCollection>().Values.Add(latentPlate);return value;};
NetworkUtils.Factories[latentPlate]=()=>Clone("LatentPlate",false);
var latentOwner=InitialNew();latentOwner.SpawningPath=new(){61,0,0};latentOwner.WorkableItem=null;
missingContainerId=(int)EntitySerialisationRegistry.Next+3;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
PathRef LatentOwnerPath(EntityWarpSpec value)=>value.EntityId==missingContainerId?new PathRef(new[]{2}):null;
NativeInitialAttachmentSpawnChain LatentFactory(EntityWarpSpec value,EntitySerialisationEntry entry)=>new(){
    Prefabs=new[]{latentStack,latentPlate},Children=new[]{new[]{latentPlate},Array.Empty<GameObject>()},
    Components=new[]{new[]{typeof(PhysicalAttachment)},new[]{typeof(Collider),typeof(PhysicalAttachment),typeof(ServerPhysicalAttachment)}},
    Colliders=new[]{1,1}};
plan=Plan(Warp(Keep(),latentOwner,missingContainer),map,null,LatentOwnerPath,LatentFactory);
Check(NetworkUtils.SpawnCalls==0,"qualified latent initial factory remains mutation-free during preflight");
NetworkUtils.DeferPhysicsOnDestroy=true;
plan.Spawn();plan.BindRecreatedInitialAttachments();plan.DeleteAndVerify();
Check(map.Single().Key.ToString()=="2","qualified latent initial factory restores the one-element historical owner path");
Check(NetworkUtils.Events.SequenceEqual(new[]{"spawn:LatentStack","spawn:LatentPlate","destroy:LatentStack(Clone)","destroy:LatentStack_Rigidbody"}),
    "latent initial factory uses exact two-stage native spawn and intermediate cleanup order");
var finalContainer=map.Single().Value.m_GameObject.GetComponent<PhysicalAttachment>().m_container.gameObject;
var physicsPairs=ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms;
Check(physicsPairs.Count==1&&ReferenceEquals(((ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair)physicsPairs[0]).m_Transform,finalContainer.transform),
    "latent initial factory synchronously retires only its intermediate physics pair");
var latentReceipt=(Dictionary<string,object>)((object[])((Dictionary<string,object>)NativeDynamicWarpPlan.LastReceipt)["spawned"])[0];
Check((bool)latentReceipt["latentInitialFactory"]&&(bool)latentReceipt["retiredLatentIntermediatePhysicsPair"],
    "latent initial factory reports exact intermediate physics-pair retirement");
var retiredTransform=latentStackClone.GetComponent<PhysicalAttachment>().m_container.gameObject.transform;
ServerPhysicsObjectSynchroniser.RemoveForTransform(retiredTransform);
Check(physicsPairs.Count==1&&ReferenceEquals(((ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair)physicsPairs[0]).m_Transform,finalContainer.transform),
    "later deferred intermediate OnDestroy is an order-preserving no-op");

NetworkUtils.DeferPhysicsOnDestroy=false;
NetworkUtils.DestroyObject(finalContainer);NetworkUtils.DestroyObject(map.Single().Value.m_GameObject);
NetworkUtils.Events.Clear();NetworkUtils.SpawnCalls=0;map=new();
latentOwner=InitialNew();latentOwner.SpawningPath=new(){61,0,0};latentOwner.WorkableItem=null;
missingContainerId=(int)EntitySerialisationRegistry.Next+3;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
plan=Plan(Warp(Keep(),latentOwner,missingContainer),map,null,
    value=>value.EntityId==missingContainerId?new PathRef(new[]{2}):null,LatentFactory);
Check(NetworkUtils.SpawnCalls==0,"repeat latent initial recreation remains mutation-free during preflight");
NetworkUtils.DeferPhysicsOnDestroy=true;
plan.Spawn();plan.BindRecreatedInitialAttachments();plan.DeleteAndVerify();
finalContainer=map.Single().Value.m_GameObject.GetComponent<PhysicalAttachment>().m_container.gameObject;
Check(physicsPairs.Count==1&&ReferenceEquals(((ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair)physicsPairs[0]).m_Transform,finalContainer.transform),
    "repeat latent recreation does not let generic spawn observations strand the intermediate physics pair");
latentReceipt=(Dictionary<string,object>)((object[])((Dictionary<string,object>)NativeDynamicWarpPlan.LastReceipt)["spawned"])[0];
Check((bool)latentReceipt["latentInitialFactory"]&&(bool)latentReceipt["retiredLatentIntermediatePhysicsPair"],
    "repeat recreation preserves authoritative latent classification after observation-cache population");

Reset();map=new();latentStack=new GameObject("LatentStack");latentPlate=new GameObject("LatentPlate");latentStackClone=null;
root.GetComponent<SpawnableEntityCollection>().Values[0]=latentStack;
NetworkUtils.Factories[latentStack]=()=>{var value=Clone("LatentStack",false);latentStackClone=value;value.AddComponent<SpawnableEntityCollection>().Values.Add(latentPlate);return value;};
NetworkUtils.Factories[latentPlate]=()=>{
    var container=latentStackClone.GetComponent<PhysicalAttachment>().m_container.gameObject;
    ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms.Add(
        new ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair{m_Entry=new EntitySerialisationEntry{m_GameObject=new GameObject("AliasEntry")},m_Transform=container.transform});
    return Clone("LatentPlate",false);
};
latentOwner=InitialNew();latentOwner.SpawningPath=new(){61,0,0};latentOwner.WorkableItem=null;
missingContainerId=(int)EntitySerialisationRegistry.Next+3;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
plan=Plan(Warp(Keep(),latentOwner,missingContainer),map,null,
    value=>value.EntityId==missingContainerId?new PathRef(new[]{2}):null,LatentFactory);
NetworkUtils.DeferPhysicsOnDestroy=true;
Reject(plan.Spawn,"latent intermediate rejects a different entry sharing its transform");
var aliasContainer=latentStackClone.GetComponent<PhysicalAttachment>().m_container.gameObject;
Check(ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms.Cast<ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair>()
    .Count(value=>ReferenceEquals(value.m_Transform,aliasContainer.transform))==2,
    "same-transform alias rejection does not manually remove either row");

Reset();map=new();latentStack=new GameObject("LatentStack");latentPlate=new GameObject("LatentPlate");latentStackClone=null;
root.GetComponent<SpawnableEntityCollection>().Values[0]=latentStack;
NetworkUtils.Factories[latentStack]=()=>{var value=Clone("LatentStack",false);latentStackClone=value;value.AddComponent<SpawnableEntityCollection>().Values.Add(latentPlate);return value;};
NetworkUtils.Factories[latentPlate]=()=>{
    var container=latentStackClone.GetComponent<PhysicalAttachment>().m_container.gameObject;
    ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms.Add(
        new ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair{m_Entry=EntitySerialisationRegistry.GetEntry(container),m_Transform=new GameObject("AliasTransform").transform});
    return Clone("LatentPlate",false);
};
latentOwner=InitialNew();latentOwner.SpawningPath=new(){61,0,0};latentOwner.WorkableItem=null;
missingContainerId=(int)EntitySerialisationRegistry.Next+3;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
plan=Plan(Warp(Keep(),latentOwner,missingContainer),map,null,
    value=>value.EntityId==missingContainerId?new PathRef(new[]{2}):null,LatentFactory);
NetworkUtils.DeferPhysicsOnDestroy=true;
Reject(plan.Spawn,"latent intermediate rejects its entry paired with a different transform");
var aliasEntry=ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms.Cast<ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair>()
    .Single(value=>ReferenceEquals(value.m_Transform,latentStackClone.GetComponent<PhysicalAttachment>().m_container.gameObject.transform)).m_Entry;
Check(ServerPhysicsObjectSynchroniser.ms_ServerPhysicsObjectSytnchroniserTransforms.Cast<ServerPhysicsObjectSynchroniser.SerialisationEntryTransformPair>()
    .Count(value=>ReferenceEquals(value.m_Entry,aliasEntry))==2,
    "same-entry alias rejection does not manually remove either row");

Reset();
latentStack=new GameObject("LatentStack");root.GetComponent<SpawnableEntityCollection>().Values[0]=latentStack;
NetworkUtils.Factories[latentStack]=()=>Clone("LatentStack",false);
latentOwner=InitialNew();latentOwner.SpawningPath=new(){61,0,0};latentOwner.WorkableItem=null;
missingContainerId=(int)EntitySerialisationRegistry.Next+3;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
Reject(()=>Plan(Warp(Keep(),latentOwner,missingContainer),null,null,
    value=>value.EntityId==missingContainerId?new PathRef(new[]{2}):null,
    (value,entry)=>new(){Prefabs=new[]{latentStack},Children=new[]{Array.Empty<GameObject>()},
        Components=new[]{new[]{typeof(PhysicalAttachment)}},Colliders=new[]{1}}),
    "latent initial factory requires one exact profile per requested spawn stage");

Reset();
latentStack=new GameObject("LatentStack");latentPlate=new GameObject("LatentPlate");var wrongPlate=new GameObject("WrongPlate");
root.GetComponent<SpawnableEntityCollection>().Values[0]=latentStack;
NetworkUtils.Factories[latentStack]=()=>{var value=Clone("LatentStack",false);value.AddComponent<SpawnableEntityCollection>().Values.Add(wrongPlate);return value;};
NetworkUtils.Factories[latentPlate]=()=>Clone("LatentPlate",false);
latentOwner=InitialNew();latentOwner.SpawningPath=new(){61,0,0};latentOwner.WorkableItem=null;
missingContainerId=(int)EntitySerialisationRegistry.Next+3;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
plan=Plan(Warp(Keep(),latentOwner,missingContainer),null,null,
    value=>value.EntityId==missingContainerId?new PathRef(new[]{2}):null,
    (value,entry)=>new(){Prefabs=new[]{latentStack,latentPlate},Children=new[]{new[]{latentPlate},Array.Empty<GameObject>()},
        Components=new[]{new[]{typeof(PhysicalAttachment)},new[]{typeof(PhysicalAttachment)}},Colliders=new[]{1,1}});
Reject(plan.Spawn,"latent intermediate must register the exact historical child collection");
Check(EntitySerialisationRegistry.m_EntitiesList.Count==1,"latent child mismatch transactionally removes the intermediate owner and container");

Reset();missingContainerId=(int)EntitySerialisationRegistry.Next+1;missingContainer=new EntityWarpSpec{EntityId=missingContainerId};
Reject(()=>Plan(Warp(Keep(),New(),missingContainer),null,null,value=>null),
    "unrelated missing fixed entity still rejects during dynamic preflight");
Reject(()=>Plan(Warp(Keep(),missingContainer),null,null,value=>new PathRef(new[]{61,0})),
    "qualified missing container without its owner spawn target rejects during preflight");
var referencesMissing=new EntityWarpSpec{EntityId=61,AttachStation=new(){Item=new(){EntityId=missingContainerId}}};
Reject(()=>Plan(Warp(referencesMissing,New(),missingContainer),null,null,value=>new PathRef(new[]{61,0})),
    "qualified missing container cannot become a gameplay reference before recreation");

Reset();map=new();plan=Plan(Warp(Keep(),New(true)),map);plan.Spawn();plan.DeleteAndVerify();
Check(map.Single().Key.ToString()=="61.0.0","prepared respawn maps historical consumed-parent path");
Check(EntitySerialisationRegistry.m_EntitiesList.Count==3,"nested respawn removes intermediate item and container");
Check(NetworkUtils.Events.SequenceEqual(new[]{"spawn:Raw","spawn:Prepared","destroy:Raw(Clone)","destroy:Raw_Rigidbody"}),"native stage creation and intermediate cleanup order");

Reset(); var extra=NetworkUtils.ServerSpawnPrefab(root,raw); int extraId=(int)EntitySerialisationRegistry.GetId(extra);
var deletion=Warp(Keep());deletion.EntitiesToDelete.Add(extraId);plan=Plan(deletion);plan.Spawn();plan.DeleteAndVerify();
Check(EntitySerialisationRegistry.m_EntitiesList.Count==1,"future branch deletion removes physical proxy as well as item");

Reset();NetworkUtils.FailAfterRegisterAt=2;plan=Plan(Warp(Keep(),New(true)));
Reject(plan.Spawn,"failure after native registration is surfaced");Check(EntitySerialisationRegistry.m_EntitiesList.Count==1,"partial native registration and earlier intermediates cleaned");
Reset();NetworkUtils.FailAfterRegisterAt=2;plan=Plan(Warp(Keep(),New(false,0),New(false,1)));
Reject(plan.Spawn,"later target failure surfaced");Check(EntitySerialisationRegistry.m_EntitiesList.Count==1,"earlier final spawn also removed after later failure");

Reset();int before=NetworkUtils.SpawnCalls;
var bad=New();bad.SpawningPath[1]=9;Reject(()=>Plan(Warp(Keep(),bad)),"invalid native ordered prefab index fails preflight");
bad=New();bad.SpawningPath.Add(9);Reject(()=>Plan(Warp(Keep(),bad)),"invalid nested prefab index fails preflight");
bad=New();bad.WorkableItem=null;Reject(()=>Plan(Warp(Keep(),bad)),"missing native workable block fails before spawn");
bad=New();bad.EntityPathReference=null;Reject(()=>Plan(Warp(Keep(),bad)),"missing logical path fails preflight");
Reject(()=>Plan(Warp(Keep(),New(),New())),"duplicate logical target rejected");
Reject(()=>Plan(Warp(Keep(),Keep())),"duplicate retained native ID rejected");
var both=New();both.EntityId=61;Reject(()=>Plan(Warp(Keep(),both)),"native ID plus spawn ambiguity rejected");
var removed=Warp();removed.EntitiesToDelete.Add(61);Reject(()=>Plan(removed),"fixed native root without spawn receipt cannot be deleted");
var conflict=Warp(Keep());conflict.EntitiesToDelete.Add(61);Reject(()=>Plan(conflict),"retained and deleted identity conflict rejected");
var dangling=Keep();dangling.AttachStation=new(){Item=new(){EntityPathReference=new(){Ids=new(){61,99}}}};
Reject(()=>Plan(Warp(dangling,New())),"dangling logical attachment rejected");
Check(NetworkUtils.SpawnCalls==before,"all malformed plans leave native spawn count unchanged");

Reset();var extinguisher=new GameObject("Extinguisher");EntitySerialisationRegistry.Add(extinguisher,1);
var counter=new GameObject("Counter63");EntitySerialisationRegistry.Add(counter,63);
var unchangedReference=new EntityWarpSpec{EntityId=63,AttachStation=new(){Item=new(){EntityId=1}}};
plan=Plan(Warp(Keep(),unchangedReference));plan.Spawn();plan.DeleteAndVerify();
Check(EntitySerialisationRegistry.GetEntry(1).m_GameObject==extinguisher,"native L unchanged extinguisher reference outside mutable WarpSpec survives");
plan=Plan(Warp(Keep(),unchangedReference));EntitySerialisationRegistry.m_EntitiesList._items.RemoveAll(e=>e.m_Header.m_uEntityID==1);EntitySerialisationRegistry.Add(new GameObject("replacement"),1);
Reject(plan.Spawn,"read-only retained reference incarnation is still pinned");
Reset();var referencedFuture=NetworkUtils.ServerSpawnPrefab(root,raw);var futureId=(int)EntitySerialisationRegistry.GetId(referencedFuture);
var deleteReference=Warp(new EntityWarpSpec{EntityId=61,AttachStation=new(){Item=new(){EntityId=futureId}}});deleteReference.EntitiesToDelete.Add(futureId);
Reject(()=>Plan(deleteReference),"omitted reference to explicitly deleted native item still rejects");

Reset();var preflight=Plan(Warp(Keep(),New()));root.GetComponent<SpawnableEntityCollection>().Values[0]=prepared;
Reject(preflight.Spawn,"native prefab order replacement after preflight rejected");Check(NetworkUtils.SpawnCalls==0,"changed root cannot spawn wrong prefab");
Reset();var reused=NetworkUtils.ServerSpawnPrefab(root,raw);reused.AddComponent<EntityPathReferenceMarker>().EntityPath=new PathRef(new[]{61,0});
Reject(()=>Plan(Warp(Keep(),new(){EntityId=(int)EntitySerialisationRegistry.GetId(reused)},New())),"retained marker collision rejected");

Reset();var originalFlow=UnityEngine.Object.Flow;UnityEngine.Object.Flow=new ServerKitchenFlowControllerBase();
Reject(()=>Plan(Warp(Keep(),New())),"old native prefab observations cannot cross round identity");UnityEngine.Object.Flow=originalFlow;
Reset();var unobserved=new GameObject("Unobserved");root.GetComponent<SpawnableEntityCollection>().Values.Add(unobserved);bad=New();bad.SpawningPath[1]=1;
Reject(()=>Plan(Warp(Keep(),bad)),"unobserved prefab asset is not authoring permission to spawn");

Reset();plan=Plan(Warp(Keep(),New()));EntitySerialisationRegistry.m_EntitiesList._items.RemoveAll(e=>e.m_Header.m_uEntityID==61);EntitySerialisationRegistry.Add(new GameObject("replaced"),61);
Reject(plan.Spawn,"retained root incarnation change after preflight rejected");Check(NetworkUtils.SpawnCalls==0,"replaced native root cannot enter mutation");
Reset();bad=New();bad.EntityPathReference.Ids[1]=-1;Reject(()=>Plan(Warp(Keep(),bad)),"negative logical spawn ordinal rejected");
bad=New();bad.SpawningPath[0]=999;Reject(()=>Plan(Warp(Keep(),bad)),"missing native spawn root rejected");
bad=New();bad.EntityPathReference.Ids=Enumerable.Repeat(1,33).ToList();Reject(()=>Plan(Warp(Keep(),bad)),"excessive nested logical path rejected");
var duplicateDelete=Warp(Keep());var extraAgain=NetworkUtils.ServerSpawnPrefab(root,raw);var deleteId=(int)EntitySerialisationRegistry.GetId(extraAgain);duplicateDelete.EntitiesToDelete.AddRange(new[]{deleteId,deleteId});
Reject(()=>Plan(duplicateDelete),"duplicate native deletion cannot double free its ID");
Reset();NetworkUtils.DeferDestroy=true;map=new();plan=Plan(Warp(Keep(),New(true)),map,NetworkUtils.Flush);plan.Spawn();plan.DeleteAndVerify();
Check(EntitySerialisationRegistry.m_EntitiesList.Count==3,"batched native destruction flush removes intermediate and container");

var survivorA=new object();var survivorB=new object();var pendingPair=new object();var finalPair=new object();
System.Collections.IList exactList=new List<object>{survivorA,survivorB,pendingPair,finalPair};
var exactPreimage=new[]{survivorA,survivorB,pendingPair,finalPair};
NativeDynamicWarpRules.RetireExactCanonicalListValue(exactList,exactList,exactPreimage,2,pendingPair);
Check(exactList.Cast<object>().SequenceEqual(new[]{survivorA,survivorB,finalPair}),
    "exact canonical-list retirement preserves survivor and final append order");
void RejectRetirement(System.Collections.IList current,System.Collections.IList captured,object[] preimage,int index,object value,string text) {
    var before=current.Cast<object>().ToArray();Reject(()=>NativeDynamicWarpRules.RetireExactCanonicalListValue(current,captured,preimage,index,value),text);
    Check(current.Cast<object>().SequenceEqual(before),text+" is mutation-free");
}
exactList=new List<object>{survivorA,pendingPair,finalPair};
RejectRetirement(exactList,new List<object>(exactList.Cast<object>()),new[]{survivorA,pendingPair,finalPair},1,pendingPair,
    "canonical-list incarnation mismatch rejects");
exactList=new List<object>{survivorA,pendingPair,finalPair};
RejectRetirement(exactList,exactList,new[]{survivorB,pendingPair,finalPair},1,pendingPair,
    "canonical-list preimage mismatch rejects");
exactList=new List<object>{survivorA,pendingPair,finalPair};
RejectRetirement(exactList,exactList,new[]{survivorA,pendingPair,finalPair},0,pendingPair,
    "canonical-list wrong captured index rejects");
exactList=new List<object>{survivorA,pendingPair,pendingPair,finalPair};
RejectRetirement(exactList,exactList,new[]{survivorA,pendingPair,pendingPair,finalPair},1,pendingPair,
    "canonical-list duplicate pending value rejects");
exactList=new List<object>{survivorA,finalPair};
RejectRetirement(exactList,exactList,new[]{survivorA,finalPair},1,pendingPair,
    "canonical-list missing pending value rejects");

var pendingPair2=new object();
exactList=new List<object>{survivorA,pendingPair,survivorB,pendingPair2,finalPair};
exactPreimage=new[]{survivorA,pendingPair,survivorB,pendingPair2,finalPair};
NativeDynamicWarpRules.RetireExactCanonicalListValues(exactList,exactList,exactPreimage,
    new[]{pendingPair,pendingPair2});
Check(exactList.Cast<object>().SequenceEqual(new[]{survivorA,survivorB,finalPair}),
    "exact canonical-list retirement set preserves every survivor in order");
void RejectRetirementSet(System.Collections.IList current,System.Collections.IList captured,
    object[] preimage,object[] values,string text) {
    var before=current.Cast<object>().ToArray();
    Reject(()=>NativeDynamicWarpRules.RetireExactCanonicalListValues(current,captured,preimage,values),text);
    Check(current.Cast<object>().SequenceEqual(before),text+" is mutation-free");
}
exactList=new List<object>{survivorA,pendingPair,survivorB};
RejectRetirementSet(exactList,exactList,new[]{survivorA,pendingPair,survivorB},
    new[]{pendingPair,pendingPair},"canonical-list duplicate retirement set rejects");
exactList=new List<object>{survivorA,pendingPair,survivorB};
RejectRetirementSet(exactList,exactList,new[]{survivorA,pendingPair,survivorB},
    new[]{pendingPair,pendingPair2},"canonical-list retirement set with an absent value rejects");

var cleanup=new List<string>();var transaction=new NativeSpawnTransaction<string>(x=>{cleanup.Add(x);if(x=="second")throw new InvalidOperationException("injected cleanup failure");});
transaction.Create(()=>"first");transaction.Create(()=>"second");
Reject(transaction.Cleanup,"cleanup failure is visible");Check(cleanup.SequenceEqual(new[]{"second","first"}),"cleanup still attempts all owned objects in reverse order");
Reject(()=>transaction.RemoveIntermediate("unowned"),"transaction cannot destroy unowned object");
transaction=new NativeSpawnTransaction<string>(cleanup.Add);transaction.Create(()=>"committed");transaction.Commit();transaction.Cleanup();Check(!cleanup.Contains("committed"),"committed final object survives transaction disposal");

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {passed=true,checks=checks.Count,names=checks,scope="Actual plugin planner/transaction with controlled native API, registry, callback-failure fixtures; no Unity runtime or physics proof."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
