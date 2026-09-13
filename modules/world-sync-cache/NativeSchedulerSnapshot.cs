using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hpmv;
using SuperchargedPatch.Extensions;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using UQuaternion=UnityEngine.Quaternion;

namespace SuperchargedPatch.Authoring.Modules
{
    // Native cadence is state, separate from the Unity physics callback phase.
    // No scheduler method or clock is replaced. Its scratch payload caches need
    // no restoration: native SynchroniseEntity/ForRecipient clear them first.
    internal sealed class NativeSchedulerSnapshot
    {
        private sealed class Member
        {
            internal EntitySerialisationEntry Entry;
            internal uint Id;
            internal GameObject Object;
            internal Transform ObjectTransform;
            internal int ObjectId;
            internal object ComponentList;
            internal ServerSynchroniser[] Components;
            internal object ClientComponentList;
            internal ClientSynchroniser[] ClientComponents;
            internal Type[] ObjectComponents;
            internal uint ContainerId;
            internal object DynamicBodyToken;
            internal MethodInfo DynamicBodyRestore;
            internal GameObject DynamicSpawnPrefab;
            internal DynamicContainerPose DynamicContainerPose;
            internal DynamicOwnerPose DynamicPose;
            internal bool InitialAttachment;
            internal bool Urgent;
        }
        private sealed class DynamicOwnerPose
        {
            internal Transform Parent;
            internal EntitySerialisationEntry ParentEntry;
            internal GameObject ParentObject;
            internal TransformPathNode[] ParentTransformPath;
            internal Vector3 LocalPosition,WorldPosition,LocalScale,WorldScale;
            internal UQuaternion LocalRotation,WorldRotation;
            internal bool Attached,DetachedOnContainer,ServerPredictionMode,ClientPredictionMode;
            internal IParentable CachedClientParent;
            internal GameObject CachedClientParentObject;
            internal int CachedClientParentIndex;
            internal Type CachedClientParentType;
        }
        private sealed class TransformPathNode
        {
            internal int ChildIndex;
            internal Type[] ComponentTypes;
        }
        private sealed class DynamicContainerState
        {
            internal Rigidbody Body;
            internal Vector3 Position,Velocity,AngularVelocity,CenterOfMass,InertiaTensor;
            internal UQuaternion Rotation,InertiaTensorRotation;
            internal float Mass,Drag,AngularDrag,SleepThreshold,MaxAngularVelocity;
            internal RigidbodyConstraints Constraints;
            internal RigidbodyInterpolation Interpolation;
            internal CollisionDetectionMode CollisionDetection;
            internal bool Kinematic,Gravity,DetectCollisions,Sleeping;
        }
        private sealed class DynamicContainerPose
        {
            internal Transform Parent;
            internal int ParentId;
            internal Vector3 BodyPosition,LocalPosition,WorldPosition;
            internal UQuaternion BodyRotation,LocalRotation,WorldRotation;
        }
        private sealed class DynamicRestore
        {
            internal Member Owner,Container;
            internal Member DependentOwner,DependentContainer;
            internal EntitySerialisationEntry HistoricalOwnerEntry;
            internal GameObject HistoricalOwnerObject;
            internal Transform HistoricalOwnerTransform;
            internal EntityWarpSpec Spec;
            internal EntitySerialisationEntry SpawnedOwner,SpawnedContainer;
            internal DynamicDeletion[] Deletions;
            internal EntitySerialisationEntry[] CurrentRegular,CurrentFast;
            internal EntitySerialisationEntry[] CurrentRegistry;
            internal object[] CurrentPhysics;
            internal object HistoricalPhysicsPair,SpawnedPhysicsPair;
            internal ushort[] QueueBefore;
            internal bool InitialRecreation,StackTopologyRecreation,QueueReordered,Rebound;
        }
        private sealed class DynamicDeletion
        {
            internal EntitySerialisationEntry Owner,Container;
            internal uint OwnerId,ContainerId;
        }
        private sealed class SurvivingDynamicPair
        {
            internal Member Owner,Container;
            internal EntitySerialisationEntry CurrentOwner,CurrentContainer;
        }
        private sealed class DynamicBodyService
        {
            internal MethodInfo Restore;
            internal object Token;
        }
        private readonly MultiplayerController controller;
        private readonly int controllerId;
        private readonly ServerSynchronisationScheduler scheduler;
        private readonly object coordinator;
        private readonly FastList<EntitySerialisationEntry> regularList, fastList;
        private readonly FastList<EntitySerialisationEntry> registryList;
        private readonly EntitySerialisationEntry[] registryEntries;
        private readonly IList physicsList;
        private readonly object[] physicsPairs;
        private readonly Queue<ushort> freeEntityIds;
        private readonly Member[] regular, fast;
        private readonly ushort[] savedFreeEntityIds;
        private readonly float nextUpdate, nextFastUpdate;
        private readonly bool urgent, fastChefs;
        private DynamicRestore dynamic;
        private static FieldInfo owner, next, nextFast, entities, fastEntities, started, session, urgentField, observedSpawns;
        private static FieldInfo physicsListField,physicsEntryField,physicsTransformField;
        private static FieldInfo serverPredicted,clientPredicted,clientParent;

        internal static void ValidateContract()
        {
            owner=Field(typeof(MultiplayerController),"m_ServerSync",typeof(ServerSynchronisationScheduler));
            next=Field(typeof(ServerSynchronisationScheduler),"m_fNextUpdate",typeof(float));
            nextFast=Field(typeof(ServerSynchronisationScheduler),"m_fNextFastUpdate",typeof(float));
            entities=Field(typeof(ServerSynchronisationScheduler),"m_EntitiesList",typeof(FastList<EntitySerialisationEntry>));
            fastEntities=Field(typeof(ServerSynchronisationScheduler),"m_FastEntitiesList",typeof(FastList<EntitySerialisationEntry>));
            started=Field(typeof(ServerSynchronisationScheduler),"m_bStarted",typeof(bool));
            session=AccessTools.Field(typeof(ServerSynchronisationScheduler),"m_SessionCoordinator");
            urgentField=Field(typeof(EntitySerialisationEntry),"m_bUrgentUpdate",typeof(bool));
            serverPredicted=Field(typeof(ServerPhysicalAttachment),"m_bIsClientSidePredicted",typeof(bool));
            clientPredicted=Field(typeof(ClientPhysicalAttachment),"m_bClientSidePredicted",typeof(bool));
            clientParent=Field(typeof(ClientPhysicalAttachment),"m_Parent",typeof(IParentable));
            observedSpawns=AccessTools.Field(typeof(NativeDynamicWarpPlan),"liveSpawned");
            physicsListField=AccessTools.Field(typeof(ServerPhysicsObjectSynchroniser),"ms_ServerPhysicsObjectSytnchroniserTransforms");
            var physicsPairType=typeof(ServerPhysicsObjectSynchroniser).GetNestedType("SerialisationEntryTransformPair",
                BindingFlags.Public|BindingFlags.NonPublic);
            physicsEntryField=physicsPairType==null?null:AccessTools.Field(physicsPairType,"m_Entry");
            physicsTransformField=physicsPairType==null?null:AccessTools.Field(physicsPairType,"m_Transform");
            if(session==null||observedSpawns==null||!typeof(IDictionary).IsAssignableFrom(observedSpawns.FieldType))
                throw new InvalidOperationException("Native sync scheduler session/spawn-observation field is absent.");
            if(physicsListField==null||!typeof(IList).IsAssignableFrom(physicsListField.FieldType)
                ||physicsEntryField==null||physicsEntryField.FieldType!=typeof(EntitySerialisationEntry)
                ||physicsTransformField==null||physicsTransformField.FieldType!=typeof(Transform))
                throw new InvalidOperationException("Native physics synchroniser canonical-list contract differs.");
        }
        private static FieldInfo Field(Type type,string name,Type expected)
        {
            var field=AccessTools.Field(type,name);
            if(field==null||field.FieldType!=expected)throw new InvalidOperationException("Native sync scheduler field differs: "+name);
            return field;
        }
        internal static NativeSchedulerSnapshot Capture(HashSet<int> initialAttachmentIds)
        {
            if(initialAttachmentIds==null)throw new ArgumentNullException("initialAttachmentIds");
            return new NativeSchedulerSnapshot(initialAttachmentIds);
        }
        private NativeSchedulerSnapshot(HashSet<int> initialAttachmentIds)
        {
            controller=ControllerHandler.MultiplayerController;
            if(controller==null)throw new InvalidOperationException("No actual native scheduler owner.");
            controllerId=controller.GetInstanceID();scheduler=(ServerSynchronisationScheduler)owner.GetValue(controller);
            if(scheduler==null||!(bool)started.GetValue(scheduler))throw new InvalidOperationException("Native scheduler is not synchronising.");
            coordinator=session.GetValue(scheduler);
            regularList=(FastList<EntitySerialisationEntry>)entities.GetValue(scheduler);
            fastList=(FastList<EntitySerialisationEntry>)fastEntities.GetValue(scheduler);
            registryList=EntitySerialisationRegistry.m_EntitiesList;
            physicsList=physicsListField.GetValue(null) as IList;
            if(registryList==null||registryList._items==null||physicsList==null)
                throw new InvalidOperationException("Native registry/physics canonical lists are unavailable.");
            registryEntries=registryList._items.Take(registryList.Count).ToArray();
            physicsPairs=Current(physicsList);
            if(registryEntries.Any(entry=>entry==null||entry.m_GameObject==null||entry.m_Header.m_uEntityID==0
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID),entry))
                ||registryEntries.Distinct().Count()!=registryEntries.Length)
                throw new InvalidOperationException("Native entity registry canonical list is incomplete or duplicated.");
            var ids=new HashSet<uint>();regular=CaptureList(regularList,ids);fast=CaptureList(fastList,ids);
            freeEntityIds=EntitySerialisationRegistry.m_ServerFreeEntityIDList;
            if(freeEntityIds==null)throw new InvalidOperationException("Native free entity-ID queue is unavailable.");
            savedFreeEntityIds=freeEntityIds.ToArray();ValidateFreeIds(savedFreeEntityIds);
            nextUpdate=(float)next.GetValue(scheduler);nextFastUpdate=(float)nextFast.GetValue(scheduler);
            if(!Finite(nextUpdate)||!Finite(nextFastUpdate)||nextUpdate<0||nextFastUpdate<0)
                throw new InvalidOperationException("Native scheduler residual is invalid.");
            urgent=EntitySerialisationRegistry.HasUrgentOutgoingUpdates;
            fastChefs=DebugManager.Instance.GetOption("Fast NetworkChefs");
            CaptureDynamicBodies(initialAttachmentIds);
        }
        private static Member[] CaptureList(FastList<EntitySerialisationEntry> list,HashSet<uint> ids)
        {
            if(list==null)throw new InvalidOperationException("Native scheduler membership is unavailable.");
            var members=new Member[list.Count];
            for(int i=0;i<members.Length;i++)
            {
                var entry=list._items[i];
                if(entry==null||entry.m_GameObject==null||!ids.Add(entry.m_Header.m_uEntityID)
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID),entry))
                    throw new InvalidOperationException("Native scheduler has unregistered/duplicate membership.");
                var components=entry.m_ServerSynchronisedComponents;
                if(components==null)throw new InvalidOperationException("Native scheduler components are unavailable.");
                var copy=new ServerSynchroniser[components.Count];
                for(int j=0;j<copy.Length;j++) { copy[j]=components._items[j]; if(copy[j]==null)throw new InvalidOperationException("Native scheduler has a null synchroniser."); }
                var clientComponents=entry.m_ClientSynchronisedComponents;
                if(clientComponents==null)throw new InvalidOperationException("Native client synchroniser components are unavailable.");
                var clientCopy=new ClientSynchroniser[clientComponents.Count];
                for(int j=0;j<clientCopy.Length;j++) { clientCopy[j]=clientComponents._items[j]; if(clientCopy[j]==null)throw new InvalidOperationException("Native scheduler owner has a null client synchroniser."); }
                var attachment=entry.m_GameObject.GetComponent<PhysicalAttachment>();
                uint containerId=attachment==null||attachment.m_container==null?0:EntitySerialisationRegistry.GetId(attachment.m_container.gameObject);
                members[i]=new Member {Entry=entry,Id=entry.m_Header.m_uEntityID,Object=entry.m_GameObject,
                    ObjectTransform=entry.m_GameObject.transform,ObjectId=entry.m_GameObject.GetInstanceID(),
                    ComponentList=components,Components=copy,ClientComponentList=clientComponents,ClientComponents=clientCopy,
                    ObjectComponents=entry.m_GameObject.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType()).ToArray(),
                    ContainerId=containerId,Urgent=(bool)urgentField.GetValue(entry)};
            }
            return members;
        }
        private void CaptureDynamicBodies(HashSet<int> initialAttachmentIds)
        {
            var all=regular.Concat(fast).ToArray();
            foreach(var ownerMember in all.Where(m=>m.ContainerId!=0))
            {
                var containers=all.Where(m=>m.Id==ownerMember.ContainerId).ToArray();
                if(containers.Length!=1)
                    throw new InvalidOperationException("Dynamic PhysicalAttachment container membership differs: "+ownerMember.Id);
                var containerMember=containers[0];
                if(containerMember.DynamicBodyToken!=null)
                    throw new InvalidOperationException("Dynamic Rigidbody container is shared by multiple scheduler owners: "+containerMember.Id);
                ownerMember.InitialAttachment=initialAttachmentIds.Contains((int)ownerMember.Id);
                ownerMember.DynamicSpawnPrefab=ObservedSpawnPrefab(ownerMember.Object);
                ownerMember.DynamicPose=CaptureDynamicOwnerPose(ownerMember,containerMember);
                CaptureDynamicBody(containerMember);
            }
        }
        private static GameObject ObservedSpawnPrefab(GameObject value)
        {
            var observed=observedSpawns.GetValue(null) as IDictionary;
            return observed!=null&&value!=null&&observed.Contains(value)?observed[value] as GameObject:null;
        }
        private static DynamicOwnerPose CaptureDynamicOwnerPose(Member ownerMember,Member containerMember)
        {
            var obj=ownerMember.Object;
            var transform=obj==null?null:obj.transform;
            var physical=obj==null?null:obj.GetComponent<PhysicalAttachment>();
            var server=obj==null?null:obj.GetComponent<ServerPhysicalAttachment>();
            var client=obj==null?null:obj.GetComponent<ClientPhysicalAttachment>();
            var container=containerMember.Object==null?null:containerMember.Object.GetComponent<Rigidbody>();
            if(transform==null||physical==null||server==null||client==null||container==null
                ||!ReferenceEquals(physical.m_container,container))
                throw new InvalidOperationException("Dynamic PhysicalAttachment owner/container contract differs: "+ownerMember.Id);
            var parent=transform.parent;
            EntitySerialisationEntry parentEntry=null;
            for(var ancestor=parent;ancestor!=null&&parentEntry==null;ancestor=ancestor.parent)
                parentEntry=EntitySerialisationRegistry.GetEntry(ancestor.gameObject);
            bool serverAttached=server.IsAttached(),clientAttached=client.IsAttached();
            bool detachedOnContainer=!serverAttached&&!clientAttached&&parent!=null
                &&ReferenceEquals(parent,container.transform)&&ReferenceEquals(parentEntry,containerMember.Entry)
                &&container.transform.parent==null;
            bool attachedToSurvivingParent=serverAttached&&clientAttached&&parent!=null&&parentEntry!=null
                &&!ReferenceEquals(parentEntry,ownerMember.Entry)&&!ReferenceEquals(parentEntry,containerMember.Entry);
            // A surviving loose item has a stable two-object incarnation: its
            // logical owner is parented to its independently registered empty
            // Rigidbody container. Admit that exact topology, but keep it out
            // of the cross-incarnation recreation path below.
            if(!attachedToSurvivingParent&&!detachedOnContainer
                ||client.GetClientSidePrediction()!=null||physical.m_meshLerper!=null&&physical.GetFakeMeshActive()
                ||obj.GetComponents<EmptyLerp>().Any(value=>value.GetType()!=typeof(EmptyLerp)))
                throw new InvalidOperationException("Dynamic PhysicalAttachment owner is not a settled attached or same-incarnation loose item: "+ownerMember.Id);
            var result=new DynamicOwnerPose {Parent=parent,ParentEntry=parentEntry,ParentObject=parentEntry.m_GameObject,
                ParentTransformPath=CaptureTransformPath(parentEntry.m_GameObject.transform,parent),
                LocalPosition=transform.localPosition,WorldPosition=transform.position,
                LocalRotation=transform.localRotation,WorldRotation=transform.rotation,
                LocalScale=transform.localScale,WorldScale=transform.lossyScale,Attached=serverAttached,
                DetachedOnContainer=detachedOnContainer,
                ServerPredictionMode=(bool)serverPredicted.GetValue(server),
                ClientPredictionMode=(bool)clientPredicted.GetValue(client),
                CachedClientParent=(IParentable)clientParent.GetValue(client)};
            var cachedComponent=result.CachedClientParent as Component;
            result.CachedClientParentObject=cachedComponent==null?null:cachedComponent.gameObject;
            result.CachedClientParentIndex=cachedComponent==null?-1:Array.FindIndex(result.ParentObject.GetComponents<Component>(),
                value=>ReferenceEquals(value,cachedComponent));
            result.CachedClientParentType=cachedComponent==null?null:cachedComponent.GetType();
            if(!Finite(result.LocalPosition)||!Finite(result.WorldPosition)||!Finite(result.LocalRotation)
                ||!Finite(result.WorldRotation)||!Finite(result.LocalScale)||!Finite(result.WorldScale)
                ||Destroyed(result.CachedClientParent as UnityEngine.Object))
                throw new InvalidOperationException("Dynamic PhysicalAttachment owner pose/prediction state is invalid: "+ownerMember.Id);
            return result;
        }
        private static TransformPathNode[] CaptureTransformPath(Transform root,Transform target)
        {
            if(root==null||target==null)throw new InvalidOperationException("Dynamic attachment parent transform path is absent.");
            var reversed=new List<TransformPathNode>();
            for(var value=target;!ReferenceEquals(value,root);)
            {
                var ancestor=value.parent;
                if(ancestor==null)throw new InvalidOperationException("Dynamic attachment parent is outside its registered owner hierarchy.");
                int index=-1;
                for(int i=0;i<ancestor.childCount;i++)if(ReferenceEquals(ancestor.GetChild(i),value)){index=i;break;}
                if(index<0)throw new InvalidOperationException("Dynamic attachment parent transform is absent from its hierarchy.");
                reversed.Add(new TransformPathNode {ChildIndex=index,
                    ComponentTypes=value.gameObject.GetComponents<Component>().Where(component=>component!=null)
                        .Select(component=>component.GetType()).ToArray()});
                value=ancestor;
            }
            reversed.Reverse();return reversed.ToArray();
        }
        private static Transform ResolveTransformPath(GameObject root,TransformPathNode[] path)
        {
            var value=root==null?null:root.transform;
            if(value==null||path==null)return null;
            foreach(var node in path)
            {
                if(node==null||node.ChildIndex<0||node.ChildIndex>=value.childCount)return null;
                value=value.GetChild(node.ChildIndex);
                if(value==null||!SameTypes(value.gameObject.GetComponents<Component>().Where(component=>component!=null)
                    .Select(component=>component.GetType()).ToArray(),node.ComponentTypes))return null;
            }
            return value;
        }
        private static void CaptureDynamicBody(Member member)
        {
            var body=member.Object==null?null:member.Object.GetComponent<Rigidbody>();
            if(body==null)throw new InvalidOperationException("Dynamic Rigidbody checkpoint body is absent: "+member.Id);
            var transform=body.transform;
            member.DynamicContainerPose=new DynamicContainerPose {Parent=transform.parent,
                ParentId=transform.parent==null?0:transform.parent.GetInstanceID(),BodyPosition=body.position,
                BodyRotation=body.rotation,LocalPosition=transform.localPosition,LocalRotation=transform.localRotation,
                WorldPosition=transform.position,WorldRotation=transform.rotation};
            if(!Finite(member.DynamicContainerPose.BodyPosition)||!Finite(member.DynamicContainerPose.BodyRotation)
                ||!Finite(member.DynamicContainerPose.LocalPosition)||!Finite(member.DynamicContainerPose.LocalRotation)
                ||!Finite(member.DynamicContainerPose.WorldPosition)||!Finite(member.DynamicContainerPose.WorldRotation))
                throw new InvalidOperationException("Dynamic Rigidbody checkpoint pose is invalid: "+member.Id);
            var successes=new List<DynamicBodyService>();
            var failures=new List<string>();
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type=assembly.GetType("SuperchargedPatch.Authoring.Modules.BodyRestoreModule",false);
                if(type==null)continue;
                var capture=type.GetMethod("CaptureDynamicBody",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(int)},null);
                var restore=type.GetMethod("RestoreDynamicBody",BindingFlags.Public|BindingFlags.Static,null,new[]{typeof(object),typeof(int)},null);
                if(capture==null||capture.ReturnType!=typeof(object)||restore==null||restore.ReturnType!=typeof(void))continue;
                try
                {
                    var token=capture.Invoke(null,new object[]{checked((int)member.Id)});
                    if(token==null)throw new InvalidOperationException("Body restore returned a null dynamic checkpoint token.");
                    successes.Add(new DynamicBodyService {Restore=restore,Token=token});
                }
                catch(TargetInvocationException error)
                {
                    failures.Add(assembly.GetName().Name+": "+(error.InnerException==null?error.Message:error.InnerException.Message));
                }
                catch(Exception error) { failures.Add(assembly.GetName().Name+": "+error.Message); }
            }
            if(successes.Count!=1)
                throw new InvalidOperationException("Dynamic Rigidbody checkpoint requires exactly one active body-restore service for entity "+member.Id+
                    "; successes="+successes.Count+" failures=["+string.Join(" | ",failures.ToArray())+"]");
            member.DynamicBodyRestore=successes[0].Restore;
            member.DynamicBodyToken=successes[0].Token;
        }
        private static void RestoreDynamicBody(Member saved,EntitySerialisationEntry current)
        {
            if(saved.DynamicBodyRestore==null||saved.DynamicBodyToken==null||current==null
                ||current.m_Header.m_uEntityID!=saved.Id)
                throw new InvalidOperationException("Dynamic Rigidbody restore service/checkpoint is absent: "+saved.Id);
            try { saved.DynamicBodyRestore.Invoke(null,new[]{saved.DynamicBodyToken,(object)checked((int)saved.Id)}); }
            catch(TargetInvocationException error)
            {
                throw new InvalidOperationException("Dynamic Rigidbody restore failed at "+saved.Id+": "+
                    (error.InnerException==null?error.Message:error.InnerException.Message),error.InnerException??error);
            }
        }
        private void ValidateOwner()
        {
            if(controller==null||controller.GetInstanceID()!=controllerId||!ReferenceEquals(ControllerHandler.MultiplayerController,controller)
                ||!ReferenceEquals(owner.GetValue(controller),scheduler)||!(bool)started.GetValue(scheduler)
                ||!ReferenceEquals(session.GetValue(scheduler),coordinator)
                ||!ReferenceEquals(entities.GetValue(scheduler),regularList)||!ReferenceEquals(fastEntities.GetValue(scheduler),fastList)
                ||!ReferenceEquals(EntitySerialisationRegistry.m_EntitiesList,registryList)
                ||!ReferenceEquals(physicsListField.GetValue(null),physicsList)
                ||!ReferenceEquals(EntitySerialisationRegistry.m_ServerFreeEntityIDList,freeEntityIds)
                ||DebugManager.Instance.GetOption("Fast NetworkChefs")!=fastChefs)
                throw new InvalidOperationException("Native scheduler owner/session/mode changed.");
        }
        internal bool ValidateBeforeRestore(WarpSpec warp)
        {
            dynamic=null;
            ValidateOwner();
            var missingRegular=Missing(regular);var missingFast=Missing(fast);
            ValidateSurvivingMembers(regularList,regular,missingRegular);ValidateSurvivingMembers(fastList,fast,missingFast);
            if(missingRegular.Length==0&&missingFast.Length==0)return false;
            PrepareDynamicRestore(warp,missingRegular,missingFast);
            return true;
        }
        internal void Validate()
        {
            ValidateOwner();
            ValidateList(regularList,regular);ValidateList(fastList,fast);
        }
        private static Member[] Missing(Member[] saved)
        {
            var result=new List<Member>();
            foreach(var member in saved)
            {
                var current=EntitySerialisationRegistry.GetEntry(member.Id);
                if(ReferenceEquals(current,member.Entry))continue;
                if(current!=null)throw new InvalidOperationException("Native scheduler checkpoint ID was reused before restore: "+member.Id);
                result.Add(member);
            }
            return result.ToArray();
        }
        private static void ValidateSurvivingMembers(FastList<EntitySerialisationEntry> list,Member[] saved,Member[] missing)
        {
            var absent=new HashSet<Member>(missing);
            int current=0;
            for(int i=0;i<saved.Length;i++)
            {
                var member=saved[i];
                if(absent.Contains(member))continue;
                while(current<list.Count&&!ReferenceEquals(list._items[current],member.Entry))current++;
                if(current==list.Count)throw new InvalidOperationException("Native scheduler lost checkpoint membership before restore: "+member.Id);
                ValidateMember(list._items[current],member);current++;
            }
        }
        private void PrepareDynamicRestore(WarpSpec warp,Member[] missingRegular,Member[] missingFast)
        {
            if(warp==null||missingFast.Length!=0||missingRegular.Length!=2)
                throw new InvalidOperationException("Unsupported dynamic native scheduler checkpoint membership.");
            var owners=missingRegular.Where(m=>m.ContainerId!=0&&missingRegular.Any(c=>c.Id==m.ContainerId)).ToArray();
            if(owners.Length!=1)throw new InvalidOperationException("Missing scheduler members are not one exact PhysicalAttachment pair.");
            var ownerMember=owners[0];var containerMember=missingRegular.Single(m=>m.Id==ownerMember.ContainerId);
            if(ownerMember.DynamicPose==null)
                throw new InvalidOperationException("PhysicalAttachment recreation checkpoint is absent: owner "+ownerMember.Id+" container "+containerMember.Id);
            if(!ownerMember.DynamicPose.Attached||ownerMember.DynamicPose.DetachedOnContainer
                ||ReferenceEquals(ownerMember.DynamicPose.ParentEntry,containerMember.Entry))
                throw new InvalidOperationException(ownerMember.InitialAttachment
                    ?"Initial PhysicalAttachment recreation target is not a settled attached item."
                    :"A loose dynamic PhysicalAttachment requires its exact surviving owner/container incarnation.");
            int ownerIndex=Array.IndexOf(regular,ownerMember),containerIndex=Array.IndexOf(regular,containerMember);
            if(ownerMember.Id>ushort.MaxValue||containerMember.Id>ushort.MaxValue
                ||(!ownerMember.InitialAttachment&&containerIndex!=ownerIndex+1))
                throw new InvalidOperationException("Historical PhysicalAttachment scheduler allocation order is unsupported.");
            if(containerMember.DynamicBodyToken==null||containerMember.DynamicBodyRestore==null)
                throw new InvalidOperationException("Historical PhysicalAttachment body checkpoint is absent: "+containerMember.Id);
            var spawned=warp.Entities.Where(s=>!s.__isset.entityId).ToArray();
            if(spawned.Length!=1||!spawned[0].__isset.entityPathReference||spawned[0].EntityPathReference==null
                ||spawned[0].EntityPathReference.Ids==null||spawned[0].SpawningPath==null
                ||spawned[0].SpawningPath.Count<2)
                throw new InvalidOperationException("Scheduler restore requires one exact observed PhysicalAttachment recreation path.");
            bool stackTopologyRecreation=false;Member dependentOwner=null,dependentContainer=null;
            if(ownerMember.InitialAttachment)
            {
                if(spawned[0].EntityPathReference.Ids.Count!=1
                    ||spawned[0].EntityPathReference.Ids[0]!=(int)ownerMember.Id)
                    throw new InvalidOperationException("Scheduler restore requires one exact observed PhysicalAttachment recreation path.");
            }
            else if(spawned[0].EntityPathReference.Ids.Count==1
                &&spawned[0].EntityPathReference.Ids[0]==(int)ownerMember.Id)
            {
                // Preserve the already-qualified legacy replacement case. The
                // core's ordinary dynamic-path rules reject this shape in real
                // gameplay, but existing checkpoint fixtures exercise it.
            }
            else stackTopologyRecreation=ValidateAttachedStackRecreation(warp,spawned[0],ownerMember,
                out dependentOwner,out dependentContainer);
            if(ownerMember.InitialAttachment)
            {
                var currentRegular=regularList._items.Take(regularList.Count).ToArray();
                var currentFast=fastList._items.Take(fastList.Count).ToArray();
                var initialQueue=freeEntityIds.ToArray();ValidateFreeIds(initialQueue);
                var deletions=CaptureDeletions(warp,ownerMember,containerMember,currentRegular,currentFast);
                if(deletions.Length==0)
                {
                    var expectedCurrent=regular.Where(m=>!ReferenceEquals(m,ownerMember)&&!ReferenceEquals(m,containerMember)).Select(m=>m.Entry).ToArray();
                    if(!currentRegular.SequenceEqual(expectedCurrent)||!currentFast.SequenceEqual(fast.Select(m=>m.Entry)))
                        throw new InvalidOperationException("Current scheduler is not the exact checkpoint order with one initial attachment pair removed.");
                    var expectedQueue=savedFreeEntityIds.Concat(new[]{(ushort)ownerMember.Id,(ushort)containerMember.Id}).ToArray();
                    if(!initialQueue.SequenceEqual(expectedQueue))
                        throw new InvalidOperationException("Initial PhysicalAttachment free-ID queue does not contain the exact destruction suffix.");
                }
                else ValidateTransactionalFreeIds(initialQueue,deletions,ownerMember,containerMember);
                dynamic=new DynamicRestore {Owner=ownerMember,Container=containerMember,Spec=spawned[0],
                    InitialRecreation=true,Deletions=deletions,CurrentRegular=currentRegular,CurrentFast=currentFast,QueueBefore=initialQueue};
                if(deletions.Length!=0)
                    NativeInitialAttachmentDeletionAuthorization.Authorize(warp,checked((int)ownerMember.Id),
                        checked((int)containerMember.Id),deletions.Select(pair=>checked((int)pair.OwnerId)));
                return;
            }
            if(warp.EntitiesToDelete==null||warp.EntitiesToDelete.Count>1)
                throw new InvalidOperationException("Dynamic scheduler restore requires one exact recreation with at most one replacement.");
            var currentRegularReplacement=regularList._items.Take(regularList.Count).ToArray();
            var currentFastReplacement=fastList._items.Take(fastList.Count).ToArray();
            var currentRegistry=registryList._items.Take(registryList.Count).ToArray();
            var currentPhysics=Current(physicsList);
            var replacementDeletions=CaptureDeletions(warp,ownerMember,containerMember,currentRegularReplacement,currentFastReplacement);
            var currentQueue=freeEntityIds.ToArray();ValidateFreeIds(currentQueue);
            object historicalPhysicsPair=null;
            if(replacementDeletions.Length==0)
            {
                if(!stackTopologyRecreation)
                    throw new InvalidOperationException("Dynamic scheduler restore without a replacement lacks an exact stack topology witness.");
                historicalPhysicsPair=ValidateExactRemovedPair(currentRegularReplacement,currentFastReplacement,
                    currentRegistry,currentPhysics,currentQueue,ownerMember,containerMember,"Dynamic PhysicalAttachment");
            }
            else ValidateTransactionalFreeIds(currentQueue,replacementDeletions,ownerMember,containerMember);
            dynamic=new DynamicRestore {Owner=ownerMember,Container=containerMember,Spec=spawned[0],
                DependentOwner=dependentOwner,DependentContainer=dependentContainer,
                HistoricalOwnerEntry=ownerMember.Entry,HistoricalOwnerObject=ownerMember.Object,
                HistoricalOwnerTransform=stackTopologyRecreation?dependentOwner.DynamicPose.Parent:ownerMember.ObjectTransform,
                StackTopologyRecreation=stackTopologyRecreation,Deletions=replacementDeletions,
                CurrentRegular=currentRegularReplacement,CurrentFast=currentFastReplacement,
                CurrentRegistry=currentRegistry,CurrentPhysics=currentPhysics,HistoricalPhysicsPair=historicalPhysicsPair,
                QueueBefore=currentQueue};
        }
        private bool ValidateAttachedStackRecreation(WarpSpec warp,EntityWarpSpec spec,Member ownerMember,
            out Member childOwner,out Member childContainer)
        {
            childOwner=null;childContainer=null;
            var path=spec.EntityPathReference.Ids;
            var parent=ownerMember.DynamicPose==null?null:ownerMember.DynamicPose.ParentEntry;
            uint parentId=parent==null?0:parent.m_Header.m_uEntityID;
            if(path.Count!=2||parentId==0||path[0]!=(int)parentId
                ||spec.SpawningPath.Count!=2||spec.SpawningPath[0]!=(int)parentId
                ||ownerMember.DynamicSpawnPrefab==null||spec.Stack==null||spec.Stack.StackContents==null
                ||spec.Stack.StackContents.Count!=1)
                throw new InvalidOperationException("Dynamic scheduler recreation lacks an exact attached-stack topology witness.");

            var root=EntitySerialisationRegistry.GetEntry(parentId);
            var collection=root==null||root.m_GameObject==null?null:root.m_GameObject.GetComponent<SpawnableEntityCollection>();
            var prefabs=collection==null?null:collection.GetSpawnables().ToArray();
            int prefabIndex=spec.SpawningPath[1];
            if(!ReferenceEquals(root,parent)||prefabs==null||prefabIndex<0||prefabIndex>=prefabs.Length
                ||!ReferenceEquals(prefabs[prefabIndex],ownerMember.DynamicSpawnPrefab))
                throw new InvalidOperationException("Dynamic scheduler recreation spawn root/prefab differs from the observed stack incarnation.");

            var content=spec.Stack.StackContents[0];
            if(content==null||!content.__isset.entityId||content.__isset.entityPathReference
                ||content.EntityId<=0||warp.EntitiesToDelete.Contains(content.EntityId))
                throw new InvalidOperationException("Dynamic scheduler recreation stack child is not one exact surviving native entity.");
            uint childId=(uint)content.EntityId;
            var childOwners=regular.Concat(fast).Where(member=>member.Id==childId&&member.DynamicPose!=null
                &&ReferenceEquals(member.DynamicPose.ParentEntry,ownerMember.Entry)).ToArray();
            if(childOwners.Length!=1||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(childId),childOwners[0].Entry)
                ||ReferenceEquals(childOwners[0].DynamicPose.Parent,null)
                ||!ReferenceEquals(childOwners[0].DynamicPose.ParentObject,ownerMember.Object)
                ||childOwners[0].DynamicPose.ParentTransformPath==null)
                throw new InvalidOperationException("Dynamic scheduler recreation stack child does not match the saved owner/child topology.");
            var childContainers=regular.Concat(fast).Where(member=>member.Id==childOwners[0].ContainerId).ToArray();
            if(childContainers.Length!=1||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(childContainers[0].Id),childContainers[0].Entry))
                throw new InvalidOperationException("Dynamic scheduler recreation stack child container did not survive exactly.");

            var parentSpecs=warp.Entities.Where(value=>value.__isset.entityId&&value.EntityId==(int)parentId).ToArray();
            if(parentSpecs.Length!=1||parentSpecs[0].AttachStation==null||parentSpecs[0].PlateReturnStation==null
                ||!ExactPathReference(parentSpecs[0].AttachStation.Item,path)
                ||!ExactPathReference(parentSpecs[0].PlateReturnStation.Stack,path))
                throw new InvalidOperationException("Dynamic scheduler recreation is not bidirectionally referenced by its saved return station.");
            childOwner=childOwners[0];childContainer=childContainers[0];
            return true;
        }
        private static bool ExactPathReference(EntityIdOrRef value,IEnumerable<int> expected)
        {
            return value!=null&&!value.__isset.entityId&&value.__isset.entityPathReference
                &&value.EntityPathReference!=null&&value.EntityPathReference.Ids!=null
                &&value.EntityPathReference.Ids.SequenceEqual(expected);
        }
        private object ValidateExactRemovedPair(EntitySerialisationEntry[] currentRegular,
            EntitySerialisationEntry[] currentFast,EntitySerialisationEntry[] currentRegistry,
            object[] currentPhysics,ushort[] currentQueue,Member missingOwner,
            Member missingContainer,string kind)
        {
            var expectedCurrent=regular.Where(member=>!ReferenceEquals(member,missingOwner)
                &&!ReferenceEquals(member,missingContainer)).Select(member=>member.Entry).ToArray();
            if(!currentRegular.SequenceEqual(expectedCurrent)||!currentFast.SequenceEqual(fast.Select(member=>member.Entry)))
                throw new InvalidOperationException(kind+" current scheduler is not the exact checkpoint order with one pair removed.");
            var expectedQueue=savedFreeEntityIds.Concat(new[]{(ushort)missingOwner.Id,(ushort)missingContainer.Id}).ToArray();
            if(!currentQueue.SequenceEqual(expectedQueue))
                throw new InvalidOperationException(kind+" free-ID queue does not contain the exact destruction suffix.");
            var expectedRegistry=registryEntries.Where(entry=>!ReferenceEquals(entry,missingOwner.Entry)
                &&!ReferenceEquals(entry,missingContainer.Entry)).ToArray();
            if(!currentRegistry.SequenceEqual(expectedRegistry))
                throw new InvalidOperationException(kind+" entity registry is not the exact checkpoint order with one pair removed.");
            var historicalPhysicsPair=FindPhysicsPair(physicsPairs,missingContainer.Entry,
                missingContainer.ObjectTransform,kind+" historical body");
            var expectedPhysics=physicsPairs.Where(value=>!ReferenceEquals(value,historicalPhysicsPair)).ToArray();
            if(!SameReferences(currentPhysics,expectedPhysics))
                throw new InvalidOperationException(kind+" physics synchroniser list is not the exact checkpoint order with one body removed.");
            return historicalPhysicsPair;
        }
        private static object[] Current(IList values)
        {
            var result=new object[values.Count];values.CopyTo(result,0);return result;
        }
        private static object FindPhysicsPair(object[] values,EntitySerialisationEntry entry,
            Transform transform,string kind)
        {
            var matches=values.Where(value=>value!=null&&ReferenceEquals(physicsEntryField.GetValue(value),entry)
                &&ReferenceEquals(physicsTransformField.GetValue(value),transform)).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException(kind+" physics pair is not independently unique.");
            return matches[0];
        }
        private static bool SameReferences(object[] actual,object[] expected)
        {
            if(actual==null||expected==null||actual.Length!=expected.Length)return false;
            for(int i=0;i<actual.Length;i++)if(!ReferenceEquals(actual[i],expected[i]))return false;
            return true;
        }
        private static bool SameReferenceSet(object[] actual,object[] expected)
        {
            return actual!=null&&expected!=null&&actual.Length==expected.Length
                &&actual.All(value=>expected.Count(target=>ReferenceEquals(target,value))==1)
                &&expected.All(value=>actual.Count(target=>ReferenceEquals(target,value))==1);
        }
        private DynamicDeletion[] CaptureDeletions(WarpSpec warp,Member missingOwner,Member missingContainer,
            EntitySerialisationEntry[] currentRegular,EntitySerialisationEntry[] currentFast)
        {
            if(warp.EntitiesToDelete==null)throw new InvalidOperationException("Dynamic scheduler deletion declaration is absent.");
            var savedEntries=new HashSet<EntitySerialisationEntry>(regular.Concat(fast).Select(member=>member.Entry));
            var savedIds=new HashSet<uint>(regular.Concat(fast).Select(member=>member.Id));
            var seenIds=new HashSet<uint>();var deletions=new List<DynamicDeletion>();
            foreach(var rawId in warp.EntitiesToDelete)
            {
                if(rawId<=0)throw new InvalidOperationException("Dynamic scheduler deletion owner ID is invalid: "+rawId);
                uint ownerId=(uint)rawId;
                if(ownerId>ushort.MaxValue||savedIds.Contains(ownerId)||!seenIds.Add(ownerId))
                    throw new InvalidOperationException("Dynamic scheduler deletion is not a unique future-only owner: "+ownerId);
                var deleteOwner=EntitySerialisationRegistry.GetEntry(ownerId);
                var ownerObject=deleteOwner==null?null:deleteOwner.m_GameObject;
                var attachment=ownerObject==null?null:ownerObject.GetComponent<PhysicalAttachment>();
                var containerObject=attachment==null||attachment.m_container==null?null:attachment.m_container.gameObject;
                var deleteContainer=containerObject==null?null:EntitySerialisationRegistry.GetEntry(containerObject);
                uint containerId=deleteContainer==null?0:deleteContainer.m_Header.m_uEntityID;
                if(deleteOwner==null||ownerObject==null||deleteOwner.m_Header.m_uEntityID!=ownerId
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(ownerId),deleteOwner)
                    ||deleteContainer==null||deleteContainer.m_GameObject==null||containerId==0||containerId>ushort.MaxValue
                    ||savedIds.Contains(containerId)||!seenIds.Add(containerId)
                    ||!ReferenceEquals(deleteContainer.m_GameObject,containerObject)
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(containerId),deleteContainer))
                    throw new InvalidOperationException("Dynamic scheduler deletion owner/container pair is absent, reused, or not future-only: "+ownerId);
                deletions.Add(new DynamicDeletion {Owner=deleteOwner,Container=deleteContainer,OwnerId=ownerId,ContainerId=containerId});
            }
            var extras=currentRegular.Concat(currentFast).Where(entry=>!savedEntries.Contains(entry)).ToArray();
            var declared=deletions.SelectMany(pair=>new[]{pair.Owner,pair.Container}).ToArray();
            if(extras.Length!=declared.Length||extras.Distinct().Count()!=extras.Length
                ||declared.Distinct().Count()!=declared.Length||extras.Any(entry=>entry==null||!declared.Contains(entry))
                ||declared.Any(entry=>!extras.Contains(entry)
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID),entry)))
                throw new InvalidOperationException("Current scheduler extras do not exactly match the declared future PhysicalAttachment pairs.");
            return deletions.ToArray();
        }
        private void ValidateTransactionalFreeIds(ushort[] current,DynamicDeletion[] deletions,Member missingOwner,Member missingContainer)
        {
            var expected=new HashSet<ushort>(savedFreeEntityIds);
            foreach(var deletion in deletions)
                if(!expected.Remove((ushort)deletion.OwnerId)||!expected.Remove((ushort)deletion.ContainerId))
                    throw new InvalidOperationException("A future deletion pair did not consume checkpoint-free entity IDs.");
            if(!expected.Add((ushort)missingOwner.Id)||!expected.Add((ushort)missingContainer.Id)
                ||current.Length!=expected.Count||!expected.SetEquals(current))
                throw new InvalidOperationException("Native free entity-ID membership cannot be transactionally reversed for the dynamic pairs.");
        }
        internal void BeforeDynamicSpawn(object plan)
        {
            if(dynamic==null)return;
            ValidateOwner();ValidateDynamicPreimage();
            object target=ValidateDynamicPlan(plan);
            var chain=(Array)FieldValue(target,"Chain");var profile=chain.GetValue(chain.Length-1);
            var prefabTypes=(Type[])FieldValue(profile,"Components");
            if(dynamic.StackTopologyRecreation
                &&!ReferenceEquals(FieldValue(profile,"Prefab"),dynamic.Owner.DynamicSpawnPrefab))
                throw new InvalidOperationException("Dynamic spawn plan prefab differs from the observed historical stack prefab.");
            if(!SameBehavioralTypes(prefabTypes,dynamic.Owner.ObjectComponents)||!prefabTypes.Any(t=>typeof(PhysicalAttachment).IsAssignableFrom(t)))
                throw new InvalidOperationException("Dynamic spawn prefab differs from the historical scheduler owner signature: expected ["
                    +Names(dynamic.Owner.ObjectComponents)+"] actual ["+Names(prefabTypes)+"]");
            var remainder=dynamic.QueueBefore.Where(id=>id!=(ushort)dynamic.Owner.Id&&id!=(ushort)dynamic.Container.Id).ToArray();
            int scratchCount=0;
            if(dynamic.InitialRecreation)
                for(int i=0;i<chain.Length-1;i++)
                {
                    var intermediateTypes=(Type[])FieldValue(chain.GetValue(i),"Components");
                    // Every intermediate consumes its registered owner ID.  The
                    // spawner also calls ManualEnable, so a PhysicalAttachment
                    // consumes a separately registered Rigidbody-container ID.
                    scratchCount+=1+intermediateTypes.Count(t=>typeof(PhysicalAttachment).IsAssignableFrom(t));
                }
            if(scratchCount<0||scratchCount>remainder.Length)
                throw new InvalidOperationException("Native initial-attachment spawn requires unavailable scratch entity IDs.");
            freeEntityIds.Clear();
            for(int i=0;i<scratchCount;i++)freeEntityIds.Enqueue(remainder[i]);
            freeEntityIds.Enqueue((ushort)dynamic.Owner.Id);freeEntityIds.Enqueue((ushort)dynamic.Container.Id);
            for(int i=scratchCount;i<remainder.Length;i++)freeEntityIds.Enqueue(remainder[i]);
            dynamic.QueueReordered=true;
        }
        internal Exception FinalizeDynamicSpawn(object plan,Exception error)
        {
            if(dynamic==null||!dynamic.QueueReordered)return error;
            if(error!=null)
            {
                try { RollbackQueue(); }
                catch(Exception rollback) { return new InvalidOperationException(error.Message+" | dynamic allocator rollback: "+rollback.Message,error); }
                return error;
            }
            try
            {
                object target=ValidateDynamicPlan(plan);var actual=(GameObject)FieldValue(target,"Actual");
                var ownerEntry=actual==null?null:EntitySerialisationRegistry.GetEntry(actual);
                var attachment=actual==null?null:actual.GetComponent<PhysicalAttachment>();
                var containerObject=attachment==null||attachment.m_container==null?null:attachment.m_container.gameObject;
                var containerEntry=containerObject==null?null:EntitySerialisationRegistry.GetEntry(containerObject);
                if(ownerEntry==null||ownerEntry.m_Header.m_uEntityID!=dynamic.Owner.Id||containerEntry==null||containerEntry.m_Header.m_uEntityID!=dynamic.Container.Id)
                    throw new InvalidOperationException("Native dynamic spawn did not consume the exact historical owner/container IDs.");
                if(!regularList._items.Take(regularList.Count).SequenceEqual(dynamic.CurrentRegular.Concat(new[]{ownerEntry,containerEntry}))
                    ||!fastList._items.Take(fastList.Count).SequenceEqual(dynamic.CurrentFast))
                    throw new InvalidOperationException("Native dynamic spawn scheduler append order differs from the historical lane.");
                ValidateReplacement(ownerEntry,dynamic.Owner);ValidateReplacement(containerEntry,dynamic.Container);
                var currentQueue=freeEntityIds.ToArray();ValidateFreeIds(currentQueue);
                var expectedPostSpawn=new HashSet<ushort>(savedFreeEntityIds);
                foreach(var deletion in dynamic.Deletions)
                    if(!expectedPostSpawn.Remove((ushort)deletion.OwnerId)||!expectedPostSpawn.Remove((ushort)deletion.ContainerId))
                        throw new InvalidOperationException("A dynamic deletion pair is not drawn from the checkpoint free-ID set.");
                if(currentQueue.Length!=expectedPostSpawn.Count||!expectedPostSpawn.SetEquals(currentQueue))
                    throw new InvalidOperationException("Dynamic spawn did not leave the exact checkpoint-minus-deletions free-ID membership.");
                if(dynamic.Deletions.Length==0)
                {
                    if(currentQueue.Length!=savedFreeEntityIds.Length
                        ||!new HashSet<ushort>(currentQueue).SetEquals(savedFreeEntityIds))
                        throw new InvalidOperationException("Initial PhysicalAttachment scratch allocation did not return the exact checkpoint free-ID membership.");
                    freeEntityIds.Clear();foreach(var id in savedFreeEntityIds)freeEntityIds.Enqueue(id);
                    var expected=regular.Select(member=>ReferenceEquals(member,dynamic.Owner)?ownerEntry:
                        ReferenceEquals(member,dynamic.Container)?containerEntry:member.Entry).ToArray();
                    if(expected.Length!=regularList.Count)
                        throw new InvalidOperationException("Initial PhysicalAttachment scheduler target cardinality differs after spawn.");
                    for(int i=0;i<expected.Length;i++)regularList._items[i]=expected[i];
                    if(!regularList._items.Take(regularList.Count).SequenceEqual(expected))
                        throw new InvalidOperationException("Initial PhysicalAttachment scheduler order could not be restored.");
                    if(dynamic.StackTopologyRecreation)RestoreStackCanonicalOrder(ownerEntry,containerEntry);
                }
                dynamic.SpawnedOwner=ownerEntry;dynamic.SpawnedContainer=containerEntry;
                return null;
            }
            catch(Exception failure) { return failure; }
        }
        private void RestoreStackCanonicalOrder(EntitySerialisationEntry ownerEntry,
            EntitySerialisationEntry containerEntry)
        {
            var currentRegistry=registryList._items.Take(registryList.Count).ToArray();
            var expectedRegistryAppend=dynamic.CurrentRegistry.Concat(new[]{ownerEntry,containerEntry}).ToArray();
            if(!currentRegistry.SequenceEqual(expectedRegistryAppend))
                throw new InvalidOperationException("Returned-stack entity registry append order differs.");
            var currentPhysics=Current(physicsList);
            var spawnedPhysicsPair=FindPhysicsPair(currentPhysics,containerEntry,containerEntry.m_GameObject.transform,
                "Returned-stack spawned body");
            var expectedPhysicsAppend=dynamic.CurrentPhysics.Concat(new[]{spawnedPhysicsPair}).ToArray();
            if(!SameReferences(currentPhysics,expectedPhysicsAppend))
                throw new InvalidOperationException("Returned-stack physics synchroniser append order differs.");

            int ownerIndex=Array.FindIndex(registryEntries,value=>ReferenceEquals(value,dynamic.Owner.Entry));
            int containerIndex=Array.FindIndex(registryEntries,value=>ReferenceEquals(value,dynamic.Container.Entry));
            int physicsIndex=Array.FindIndex(physicsPairs,value=>ReferenceEquals(value,dynamic.HistoricalPhysicsPair));
            if(ownerIndex<0||containerIndex<0||ownerIndex==containerIndex||physicsIndex<0
                ||registryEntries.Count(value=>ReferenceEquals(value,dynamic.Owner.Entry))!=1
                ||registryEntries.Count(value=>ReferenceEquals(value,dynamic.Container.Entry))!=1
                ||physicsPairs.Count(value=>ReferenceEquals(value,dynamic.HistoricalPhysicsPair))!=1)
                throw new InvalidOperationException("Returned-stack canonical checkpoint positions are ambiguous.");
            var targetRegistry=(EntitySerialisationEntry[])registryEntries.Clone();
            targetRegistry[ownerIndex]=ownerEntry;targetRegistry[containerIndex]=containerEntry;
            var targetPhysics=(object[])physicsPairs.Clone();targetPhysics[physicsIndex]=spawnedPhysicsPair;
            if(targetRegistry.Length!=registryList.Count||targetPhysics.Length!=physicsList.Count
                ||!SameReferenceMembers(currentRegistry,targetRegistry)
                ||!SameReferenceSet(currentPhysics,targetPhysics))
                throw new InvalidOperationException("Returned-stack canonical membership differs from the checkpoint target.");
            for(int i=0;i<targetRegistry.Length;i++)registryList._items[i]=targetRegistry[i];
            for(int i=0;i<targetPhysics.Length;i++)physicsList[i]=targetPhysics[i];
            if(!registryList._items.Take(registryList.Count).SequenceEqual(targetRegistry)
                ||!SameReferences(Current(physicsList),targetPhysics))
                throw new InvalidOperationException("Returned-stack canonical order could not be restored.");
            registryEntries[ownerIndex]=ownerEntry;registryEntries[containerIndex]=containerEntry;
            physicsPairs[physicsIndex]=spawnedPhysicsPair;dynamic.SpawnedPhysicsPair=spawnedPhysicsPair;
        }
        internal int RestoreDynamicAfterCore()
        {
            if(dynamic==null)return RestoreSurvivingDynamicAfterCore();
            if(!dynamic.QueueReordered||dynamic.Rebound||dynamic.SpawnedOwner==null||dynamic.SpawnedContainer==null)
                throw new InvalidOperationException("Dynamic scheduler recreation did not complete before core restoration.");
            var expectedRegular=regular.Select(m=>ReferenceEquals(m,dynamic.Owner)?dynamic.SpawnedOwner:
                ReferenceEquals(m,dynamic.Container)?dynamic.SpawnedContainer:m.Entry).ToArray();
            var expectedFast=fast.Select(m=>m.Entry).ToArray();
            foreach(var deletion in dynamic.Deletions)
                if(EntitySerialisationRegistry.GetEntry(deletion.OwnerId)!=null
                    ||EntitySerialisationRegistry.GetEntry(deletion.ContainerId)!=null)
                    throw new InvalidOperationException("A declared future dynamic deletion pair remains registered after core restoration: "+deletion.OwnerId);
            var currentRegular=regularList._items.Take(regularList.Count).ToArray();
            var currentFast=fastList._items.Take(fastList.Count).ToArray();
            if(!SameReferenceMembers(currentRegular,expectedRegular)||!SameReferenceMembers(currentFast,expectedFast))
                throw new InvalidOperationException("Post-core dynamic scheduler membership differs from the checkpoint target.");
            if(dynamic.StackTopologyRecreation
                &&(!registryList._items.Take(registryList.Count).SequenceEqual(registryEntries)
                    ||!SameReferences(Current(physicsList),physicsPairs)))
                throw new InvalidOperationException("Post-core returned-stack canonical order differs from the checkpoint target.");
            var currentQueue=freeEntityIds.ToArray();ValidateFreeIds(currentQueue);
            if(currentQueue.Length!=savedFreeEntityIds.Length||!new HashSet<ushort>(currentQueue).SetEquals(savedFreeEntityIds))
                throw new InvalidOperationException("Post-core dynamic free-ID membership differs from the checkpoint.");
            freeEntityIds.Clear();foreach(var id in savedFreeEntityIds)freeEntityIds.Enqueue(id);
            for(int i=0;i<expectedRegular.Length;i++)regularList._items[i]=expectedRegular[i];
            for(int i=0;i<expectedFast.Length;i++)fastList._items[i]=expectedFast[i];
            if(!regularList._items.Take(regularList.Count).SequenceEqual(expectedRegular)
                ||!fastList._items.Take(fastList.Count).SequenceEqual(expectedFast))
                throw new InvalidOperationException("Post-core dynamic scheduler order could not be restored to the checkpoint target.");
            ValidateReplacement(dynamic.SpawnedOwner,dynamic.Owner);ValidateReplacement(dynamic.SpawnedContainer,dynamic.Container);
            RestoreDynamicBody(dynamic.Container,dynamic.SpawnedContainer);
            RestoreDynamicOwnerPose(dynamic.Owner,dynamic.SpawnedOwner,dynamic.SpawnedContainer);
            Rebind(dynamic.Owner,dynamic.SpawnedOwner);Rebind(dynamic.Container,dynamic.SpawnedContainer);dynamic.Rebound=true;
            if(dynamic.DependentOwner!=null)
            {
                RebindDependentParent(dynamic.DependentOwner,dynamic.SpawnedOwner);
                RestoreDynamicBody(dynamic.DependentContainer,dynamic.DependentContainer.Entry);
                RestoreDynamicOwnerPose(dynamic.DependentOwner,dynamic.DependentOwner.Entry,dynamic.DependentContainer.Entry);
                return 1;
            }
            return 0;
        }
        private static void RebindDependentParent(Member child,EntitySerialisationEntry replacementParent)
        {
            var target=child==null?null:child.DynamicPose;
            var replacementObject=replacementParent==null?null:replacementParent.m_GameObject;
            var replacementTransform=ResolveTransformPath(replacementObject,target==null?null:target.ParentTransformPath);
            if(target==null||replacementObject==null||replacementParent.m_Header.m_uEntityID==0
                ||target.ParentEntry==null||target.ParentEntry.m_Header.m_uEntityID!=replacementParent.m_Header.m_uEntityID)
                throw new InvalidOperationException("Recreated stack child parent topology differs: "+(child==null?0:child.Id));
            if(replacementTransform==null)
                throw new InvalidOperationException("Recreated stack child nested parent topology differs: "+child.Id);
            if(ReferenceEquals(target.CachedClientParentObject,target.ParentObject))
            {
                var components=replacementObject.GetComponents<Component>();
                var replacement=target.CachedClientParentIndex>=0&&target.CachedClientParentIndex<components.Length
                    ?components[target.CachedClientParentIndex]:null;
                var parentable=replacement as IParentable;
                if(replacement==null||replacement.GetType()!=target.CachedClientParentType||parentable==null)
                    throw new InvalidOperationException("Recreated stack child cached-parent component topology differs: "+child.Id);
                target.CachedClientParent=parentable;target.CachedClientParentObject=replacementObject;
            }
            else if(Destroyed(target.CachedClientParent as UnityEngine.Object))
                throw new InvalidOperationException("Recreated stack child has an unrelated destroyed cached parent: "+child.Id);
            target.Parent=replacementTransform;target.ParentEntry=replacementParent;
            target.ParentObject=replacementObject;
        }
        private int RestoreSurvivingDynamicAfterCore()
        {
            // Dynamic attachment owners and their independently registered empty
            // Rigidbody containers can survive a future pickup/drop unchanged by
            // identity. Core attachment callbacks restore the target parent, but
            // do not restore the inactive container's pose/mass frame or the
            // owner's exact local pose. Those values were already checkpointed
            // for every admitted dynamic owner; apply them through the same body
            // service used by the stricter cross-incarnation recreation path.
            Validate();
            var all=regular.Concat(fast).ToArray();
            var owners=all.Where(member=>member.DynamicPose!=null&&!member.InitialAttachment).ToArray();
            var pairs=new List<SurvivingDynamicPair>();
            foreach(var savedOwner in owners)
            {
                var savedContainers=all.Where(member=>member.Id==savedOwner.ContainerId).ToArray();
                if(savedContainers.Length!=1||savedContainers[0].DynamicBodyToken==null||savedContainers[0].DynamicBodyRestore==null)
                    throw new InvalidOperationException("Surviving dynamic PhysicalAttachment body checkpoint is absent: "+savedOwner.Id);
                var savedContainer=savedContainers[0];
                var currentOwner=EntitySerialisationRegistry.GetEntry(savedOwner.Id);
                var currentContainer=EntitySerialisationRegistry.GetEntry(savedContainer.Id);
                if(!ReferenceEquals(currentOwner,savedOwner.Entry)||!ReferenceEquals(currentContainer,savedContainer.Entry))
                    throw new InvalidOperationException("Surviving dynamic PhysicalAttachment incarnation changed after core restoration: "+savedOwner.Id);
                pairs.Add(new SurvivingDynamicPair {Owner=savedOwner,Container=savedContainer,
                    CurrentOwner=currentOwner,CurrentContainer=currentContainer});
            }
            foreach(var pair in pairs)
            {
                RestoreDynamicBody(pair.Container,pair.CurrentContainer);
                RestoreDynamicOwnerPose(pair.Owner,pair.CurrentOwner,pair.CurrentContainer);
            }
            return pairs.Count;
        }
        internal int RestorePausedDynamicContainerTransforms()
        {
            Validate();
            int changed=0;
            var all=regular.Concat(fast).ToArray();
            foreach(var savedOwner in all.Where(member=>member.DynamicPose!=null&&!member.InitialAttachment))
            {
                var savedContainers=all.Where(member=>member.Id==savedOwner.ContainerId).ToArray();
                if(savedContainers.Length!=1)throw new InvalidOperationException("Paused dynamic container checkpoint differs: "+savedOwner.Id);
                var savedContainer=savedContainers[0];var target=savedContainer.DynamicContainerPose;
                var current=EntitySerialisationRegistry.GetEntry(savedContainer.Id);
                var body=current==null||current.m_GameObject==null?null:current.m_GameObject.GetComponent<Rigidbody>();
                var transform=body==null?null:body.transform;
                if(target==null||body==null||transform==null||!ReferenceEquals(current,savedContainer.Entry)
                    ||!ReferenceEquals(transform.parent,target.Parent)
                    ||target.ParentId!=0&&(target.Parent==null||target.Parent.GetInstanceID()!=target.ParentId)
                    ||!Exact(body.position,target.BodyPosition)||!Exact(body.rotation,target.BodyRotation))
                    throw new InvalidOperationException("Paused dynamic container identity/body pose differs: "+savedOwner.Id);
                Transform looseOwnerTransform=null;
                if(savedOwner.DynamicPose.DetachedOnContainer)
                {
                    var looseTarget=savedOwner.DynamicPose;
                    var ownerEntry=EntitySerialisationRegistry.GetEntry(savedOwner.Id);
                    var ownerObject=ownerEntry==null?null:ownerEntry.m_GameObject;
                    looseOwnerTransform=ownerObject==null?null:ownerObject.transform;
                    var ownerPhysical=ownerObject==null?null:ownerObject.GetComponent<PhysicalAttachment>();
                    var ownerServer=ownerObject==null?null:ownerObject.GetComponent<ServerPhysicalAttachment>();
                    var ownerClient=ownerObject==null?null:ownerObject.GetComponent<ClientPhysicalAttachment>();
                    if(!ReferenceEquals(ownerEntry,savedOwner.Entry)||looseOwnerTransform==null
                        ||ownerPhysical==null||ownerServer==null||ownerClient==null
                        ||!ReferenceEquals(ownerPhysical.m_container,body)||!ReferenceEquals(looseTarget.Parent,transform)
                        ||!ReferenceEquals(looseOwnerTransform.parent,transform)||ownerServer.IsAttached()||ownerClient.IsAttached()
                        ||ownerClient.GetClientSidePrediction()!=null
                        ||(bool)serverPredicted.GetValue(ownerServer)!=looseTarget.ServerPredictionMode
                        ||(bool)clientPredicted.GetValue(ownerClient)!=looseTarget.ClientPredictionMode
                        ||!ReferenceEquals(clientParent.GetValue(ownerClient),looseTarget.CachedClientParent)
                        ||!Exact(looseOwnerTransform.localPosition,looseTarget.LocalPosition)
                        ||!Exact(looseOwnerTransform.localRotation,looseTarget.LocalRotation)
                        ||!Exact(looseOwnerTransform.localScale,looseTarget.LocalScale))
                        throw new InvalidOperationException("Paused loose dynamic owner preimage differs: "+savedOwner.Id);
                }
                var before=CaptureDynamicContainerState(body);
                bool different=!Exact(transform.localPosition,target.LocalPosition)||!Exact(transform.localRotation,target.LocalRotation)
                    ||!Exact(transform.position,target.WorldPosition)||!Exact(transform.rotation,target.WorldRotation);
                if(!Exact(transform.localPosition,target.LocalPosition))transform.localPosition=target.LocalPosition;
                if(!Exact(transform.localRotation,target.LocalRotation))transform.localRotation=target.LocalRotation;
                var after=CaptureDynamicContainerState(body);
                RequireDynamicContainerSame(before,after,savedOwner.Id);
                if(!Exact(transform.localPosition,target.LocalPosition)||!Exact(transform.localRotation,target.LocalRotation)
                    ||!Exact(transform.position,target.WorldPosition)||!Exact(transform.rotation,target.WorldRotation)
                    ||!Exact(body.position,target.BodyPosition)||!Exact(body.rotation,target.BodyRotation))
                    throw new InvalidOperationException("Paused dynamic container Transform correction differs: "+savedOwner.Id);
                if(looseOwnerTransform!=null&&(!Exact(looseOwnerTransform.position,savedOwner.DynamicPose.WorldPosition)
                    ||!Exact(looseOwnerTransform.rotation,savedOwner.DynamicPose.WorldRotation)
                    ||!Exact(looseOwnerTransform.lossyScale,savedOwner.DynamicPose.WorldScale)))
                    throw new InvalidOperationException("Paused loose dynamic owner world pose differs: "+savedOwner.Id);
                if(different)changed++;
            }
            return changed;
        }
        private static void RestoreDynamicOwnerPose(Member saved,EntitySerialisationEntry current,
            EntitySerialisationEntry currentContainer)
        {
            var target=saved.DynamicPose;
            var obj=current==null?null:current.m_GameObject;
            var transform=obj==null?null:obj.transform;
            var physical=obj==null?null:obj.GetComponent<PhysicalAttachment>();
            var server=obj==null?null:obj.GetComponent<ServerPhysicalAttachment>();
            var client=obj==null?null:obj.GetComponent<ClientPhysicalAttachment>();
            var container=currentContainer==null||currentContainer.m_GameObject==null
                ?null:currentContainer.m_GameObject.GetComponent<Rigidbody>();
            if(target==null||transform==null||physical==null||server==null||client==null||container==null
                ||!ReferenceEquals(physical.m_container,container)||!ReferenceEquals(transform.parent,target.Parent)
                ||target.ParentEntry==null||target.ParentObject==null
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(target.ParentObject),target.ParentEntry)
                ||!HasAncestor(transform.parent,target.ParentObject.transform)
                ||server.IsAttached()!=target.Attached||client.IsAttached()!=target.Attached
                ||client.GetClientSidePrediction()!=null||Destroyed(target.CachedClientParent as UnityEngine.Object))
                throw new InvalidOperationException("Recreated dynamic PhysicalAttachment owner parent/prediction contract differs: "+saved.Id);

            var bodyBefore=CaptureDynamicContainerState(container);
            serverPredicted.SetValue(server,target.ServerPredictionMode);
            clientPredicted.SetValue(client,target.ClientPredictionMode);
            clientParent.SetValue(client,target.CachedClientParent);
            if(!Exact(transform.localScale,target.LocalScale))transform.localScale=target.LocalScale;
            if(!Exact(transform.localPosition,target.LocalPosition))transform.localPosition=target.LocalPosition;
            if(!Exact(transform.localRotation,target.LocalRotation))transform.localRotation=target.LocalRotation;

            if(!ReferenceEquals(transform.parent,target.Parent)||server.IsAttached()!=target.Attached
                ||client.IsAttached()!=target.Attached||client.GetClientSidePrediction()!=null
                ||(bool)serverPredicted.GetValue(server)!=target.ServerPredictionMode
                ||(bool)clientPredicted.GetValue(client)!=target.ClientPredictionMode
                ||!ReferenceEquals(clientParent.GetValue(client),target.CachedClientParent)
                ||!Exact(transform.localPosition,target.LocalPosition)
                ||!Exact(transform.localRotation,target.LocalRotation)||!Exact(transform.localScale,target.LocalScale)
                ||!target.DetachedOnContainer&&(!Exact(transform.position,target.WorldPosition)
                    ||!Exact(transform.rotation,target.WorldRotation)||!Exact(transform.lossyScale,target.WorldScale)))
                throw new InvalidOperationException("Recreated dynamic PhysicalAttachment owner pose restore differs: "+saved.Id);
            RequireDynamicContainerSame(bodyBefore,CaptureDynamicContainerState(container),saved.Id);
        }
        private static DynamicContainerState CaptureDynamicContainerState(Rigidbody body)
        {
            if(body==null)throw new InvalidOperationException("Dynamic container Rigidbody is absent.");
            return new DynamicContainerState {Body=body,Position=body.position,Rotation=body.rotation,
                Velocity=body.velocity,AngularVelocity=body.angularVelocity,CenterOfMass=body.centerOfMass,
                InertiaTensor=body.inertiaTensor,InertiaTensorRotation=body.inertiaTensorRotation,
                Mass=body.mass,Drag=body.drag,AngularDrag=body.angularDrag,SleepThreshold=body.sleepThreshold,
                MaxAngularVelocity=body.maxAngularVelocity,Constraints=body.constraints,Interpolation=body.interpolation,
                CollisionDetection=body.collisionDetectionMode,Kinematic=body.isKinematic,Gravity=body.useGravity,
                DetectCollisions=body.detectCollisions,Sleeping=body.IsSleeping()};
        }
        private static void RequireDynamicContainerSame(DynamicContainerState before,DynamicContainerState after,uint ownerId)
        {
            if(before==null||after==null||!ReferenceEquals(before.Body,after.Body)
                ||!Exact(before.Position,after.Position)||!Exact(before.Rotation,after.Rotation)
                ||!Exact(before.Velocity,after.Velocity)||!Exact(before.AngularVelocity,after.AngularVelocity)
                ||!Exact(before.CenterOfMass,after.CenterOfMass)||!Exact(before.InertiaTensor,after.InertiaTensor)
                ||!Exact(before.InertiaTensorRotation,after.InertiaTensorRotation)||before.Mass!=after.Mass
                ||before.Drag!=after.Drag||before.AngularDrag!=after.AngularDrag
                ||before.SleepThreshold!=after.SleepThreshold||before.MaxAngularVelocity!=after.MaxAngularVelocity
                ||before.Constraints!=after.Constraints||before.Interpolation!=after.Interpolation
                ||before.CollisionDetection!=after.CollisionDetection||before.Kinematic!=after.Kinematic
                ||before.Gravity!=after.Gravity||before.DetectCollisions!=after.DetectCollisions
                ||before.Sleeping!=after.Sleeping)
                throw new InvalidOperationException("Dynamic owner pose restore changed container Rigidbody state: "+ownerId);
        }
        private object ValidateDynamicPlan(object plan)
        {
            if(plan==null)throw new InvalidOperationException("Native dynamic spawn plan is absent.");
            var targets=((IEnumerable)FieldValue(plan,"targets")).Cast<object>().ToArray();
            var recreated=targets.Where(t=>FieldValue(t,"Chain")!=null).ToArray();
            if(recreated.Length!=1||!ReferenceEquals(FieldValue(recreated[0],"Spec"),dynamic.Spec)
                ||((Array)FieldValue(recreated[0],"Chain")).Length!=dynamic.Spec.SpawningPath.Count-1)
                throw new InvalidOperationException("Native dynamic plan differs from the admitted recreation path.");
            var removals=((IEnumerable)FieldValue(plan,"removals")).Cast<object>().ToArray();
            if(removals.Length!=dynamic.Deletions.Length)
                throw new InvalidOperationException("Native dynamic deletion plan cardinality differs from the admitted future pairs.");
            var matched=new HashSet<DynamicDeletion>();
            foreach(var removal in removals)
            {
                int ownerId=(int)FieldValue(removal,"Id");
                var deletion=dynamic.Deletions.SingleOrDefault(pair=>pair.OwnerId==(uint)ownerId);
                if(deletion==null||!matched.Add(deletion)
                    ||(int)FieldValue(removal,"ContainerId")!=(int)deletion.ContainerId
                    ||!ReferenceEquals(FieldValue(removal,"Object"),deletion.Owner.m_GameObject)
                    ||!ReferenceEquals(FieldValue(removal,"Container"),deletion.Container.m_GameObject))
                    throw new InvalidOperationException("Native dynamic deletion plan differs from the admitted future pairs.");
            }
            return recreated[0];
        }
        private void ValidateDynamicPreimage()
        {
            if(dynamic.QueueReordered||dynamic.Rebound||!freeEntityIds.SequenceEqual(dynamic.QueueBefore)
                ||EntitySerialisationRegistry.GetEntry(dynamic.Owner.Id)!=null||EntitySerialisationRegistry.GetEntry(dynamic.Container.Id)!=null
                ||dynamic.Deletions.Any(deletion=>!ReferenceEquals(EntitySerialisationRegistry.GetEntry(deletion.OwnerId),deletion.Owner)
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(deletion.ContainerId),deletion.Container))
                ||!regularList._items.Take(regularList.Count).SequenceEqual(dynamic.CurrentRegular)
                ||!fastList._items.Take(fastList.Count).SequenceEqual(dynamic.CurrentFast)
                ||dynamic.StackTopologyRecreation&&(!registryList._items.Take(registryList.Count).SequenceEqual(dynamic.CurrentRegistry)
                    ||!SameReferences(Current(physicsList),dynamic.CurrentPhysics)))
                throw new InvalidOperationException("Dynamic scheduler state changed between checkpoint preflight and native spawn.");
        }
        private void RollbackQueue()
        {
            if(EntitySerialisationRegistry.GetEntry(dynamic.Owner.Id)!=null||EntitySerialisationRegistry.GetEntry(dynamic.Container.Id)!=null)
                throw new InvalidOperationException("A partially spawned historical ID remains registered.");
            var now=freeEntityIds.ToArray();ValidateFreeIds(now);
            if(now.Length!=dynamic.QueueBefore.Length||!new HashSet<ushort>(now).SetEquals(dynamic.QueueBefore))
                throw new InvalidOperationException("Native allocator membership changed during failed dynamic spawn.");
            freeEntityIds.Clear();foreach(var id in dynamic.QueueBefore)freeEntityIds.Enqueue(id);
            dynamic.QueueReordered=false;
        }
        private static void ValidateReplacement(EntitySerialisationEntry entry,Member saved)
        {
            NormalizeDisabledDynamicParenting(entry,saved);
            var currentTypes=entry==null||entry.m_GameObject==null?new Type[0]:entry.m_GameObject.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType()).ToArray();
            if(currentTypes.Count(IsPathMarker)>1||entry==null||entry.m_GameObject==null||entry.m_Header.m_uEntityID!=saved.Id
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(saved.Id),entry)||entry.m_ServerSynchronisedComponents==null)
                throw new InvalidOperationException("Recreated scheduler member identity differs: "+saved.Id);
            if(!SameBehavioralTypes(currentTypes,saved.ObjectComponents))
                throw new InvalidOperationException("Recreated scheduler object components differ at "+saved.Id+": expected ["
                    +Names(saved.ObjectComponents)+"] actual ["+Names(currentTypes)+"]");
            var currentSync=entry.m_ServerSynchronisedComponents._items.Take(entry.m_ServerSynchronisedComponents.Count).Select(c=>c.GetType()).ToArray();
            var savedSync=saved.Components.Select(c=>c.GetType()).ToArray();
            if(!SameTypes(currentSync,savedSync))
                throw new InvalidOperationException("Recreated scheduler synchronisers differ at "+saved.Id+": expected ["
                    +Names(savedSync)+"] actual ["+Names(currentSync)+"]");
            var currentClient=entry.m_ClientSynchronisedComponents._items.Take(entry.m_ClientSynchronisedComponents.Count).Select(c=>c.GetType()).ToArray();
            var savedClient=saved.ClientComponents.Select(c=>c.GetType()).ToArray();
            if(!SameTypes(currentClient,savedClient))
                throw new InvalidOperationException("Recreated client synchronisers differ at "+saved.Id+": expected ["
                    +Names(savedClient)+"] actual ["+Names(currentClient)+"]");
        }
        private static void NormalizeDisabledDynamicParenting(EntitySerialisationEntry entry,Member saved)
        {
            if(entry==null||entry.m_GameObject==null)return;
            var actual=entry.m_GameObject.GetComponents<Component>().Where(c=>c!=null).ToArray();
            if(SameBehavioralTypes(actual.Select(c=>c.GetType()).ToArray(),saved.ObjectComponents))return;
            string[] transient={"DynamicLandscapeParenting","ServerDynamicLandscapeParenting","ClientDynamicLandscapeParenting"};
            var extras=actual.Where(c=>transient.Contains(c.GetType().FullName)).ToArray();
            if(extras.Length!=3||extras.Select(c=>c.GetType().FullName).Distinct().Count()!=3
                ||!SameBehavioralTypes(actual.Where(c=>!transient.Contains(c.GetType().FullName)).Select(c=>c.GetType()).ToArray(),saved.ObjectComponents))return;
            var config=GameUtils.GetLevelConfig();
            if(config==null||!config.m_disableDynamicParenting)
                throw new InvalidOperationException("Dynamic-parenting transients cannot be finalized while the level feature is enabled.");
            var savedServer=saved.Components.Select(c=>c.GetType()).ToArray();
            var currentServer=entry.m_ServerSynchronisedComponents._items.Take(entry.m_ServerSynchronisedComponents.Count).ToArray();
            var savedClient=saved.ClientComponents.Select(c=>c.GetType()).ToArray();
            var currentClient=entry.m_ClientSynchronisedComponents._items.Take(entry.m_ClientSynchronisedComponents.Count).ToArray();
            if(!SameTypes(currentServer.Where(c=>c.GetType().FullName!="ServerDynamicLandscapeParenting").Select(c=>c.GetType()).ToArray(),savedServer)
                ||!SameTypes(currentClient.Where(c=>c.GetType().FullName!="ClientDynamicLandscapeParenting").Select(c=>c.GetType()).ToArray(),savedClient))
                throw new InvalidOperationException("Deferred dynamic-parenting synchroniser set differs from the checkpoint.");
            foreach(var component in currentServer.Where(c=>c.GetType().FullName=="ServerDynamicLandscapeParenting").ToArray())
                entry.m_ServerSynchronisedComponents.Remove(component);
            foreach(var component in currentClient.Where(c=>c.GetType().FullName=="ClientDynamicLandscapeParenting").ToArray())
                entry.m_ClientSynchronisedComponents.Remove(component);
            foreach(var component in extras)UnityEngine.Object.DestroyImmediate(component);
            var finalTypes=entry.m_GameObject.GetComponents<Component>().Where(c=>c!=null).Select(c=>c.GetType()).ToArray();
            if(!SameBehavioralTypes(finalTypes,saved.ObjectComponents))
                throw new InvalidOperationException("Disabled dynamic-parenting components did not retire synchronously.");
        }
        private static void Rebind(Member member,EntitySerialisationEntry entry)
        {
            member.Entry=entry;member.Object=entry.m_GameObject;member.ObjectId=entry.m_GameObject.GetInstanceID();
            member.ObjectTransform=entry.m_GameObject.transform;
            member.ComponentList=entry.m_ServerSynchronisedComponents;
            member.Components=entry.m_ServerSynchronisedComponents._items.Take(entry.m_ServerSynchronisedComponents.Count).ToArray();
            member.ClientComponentList=entry.m_ClientSynchronisedComponents;
            member.ClientComponents=entry.m_ClientSynchronisedComponents._items.Take(entry.m_ClientSynchronisedComponents.Count).ToArray();
        }
        private static object FieldValue(object value,string name)
        {
            if(value==null)throw new InvalidOperationException("Native dynamic plan value is absent: "+name);
            var field=AccessTools.Field(value.GetType(),name);
            if(field==null)throw new InvalidOperationException("Native dynamic plan field differs: "+name);
            return field.GetValue(value);
        }
        private static bool SameTypes(Type[] a,Type[] b) { return a!=null&&b!=null&&a.SequenceEqual(b); }
        private static bool IsPathMarker(Type type) { return type!=null&&type.FullName=="SuperchargedPatch.EntityPathReferenceMarker"; }
        private static bool SameBehavioralTypes(Type[] a,Type[] b)
        { return a!=null&&b!=null&&a.Where(t=>!IsPathMarker(t)).SequenceEqual(b.Where(t=>!IsPathMarker(t))); }
        private static string Names(Type[] values) { return values==null?"<null>":string.Join(",",values.Select(t=>t==null?"<null>":t.FullName).ToArray()); }
        internal void FinishDynamicRestore() { dynamic=null; }
        internal void AbortDynamicRestore() { dynamic=null; }
        internal bool DynamicPending { get { return dynamic!=null; } }
        internal int[] DynamicOwnerIds
        {
            get { return regular.Concat(fast).Where(member=>member.DynamicPose!=null&&!member.InitialAttachment).Select(member=>checked((int)member.Id)).ToArray(); }
        }
        internal bool WillRebindOwner(int id)
        {
            return dynamic!=null&&dynamic.Owner.Id==(uint)id;
        }
        internal bool WillRebindParent(int id)
        {
            return dynamic!=null&&dynamic.StackTopologyRecreation&&dynamic.DependentOwner!=null
                &&dynamic.DependentOwner.Id==(uint)id;
        }
        internal bool TryGetHistoricalParentRebind(int id,out EntitySerialisationEntry entry,out Transform transform)
        {
            if(!WillRebindParent(id)) { entry=null;transform=null;return false; }
            entry=dynamic.HistoricalOwnerEntry;
            transform=dynamic.HistoricalOwnerTransform;
            return entry!=null&&!ReferenceEquals(transform,null);
        }
        internal bool TryGetCurrentParentRebind(int id,out EntitySerialisationEntry entry,out Transform transform)
        {
            if(!WillRebindParent(id)) { entry=null;transform=null;return false; }
            entry=dynamic.SpawnedOwner;
            transform=entry==null||entry.m_GameObject==null?null:
                ResolveTransformPath(entry.m_GameObject,dynamic.DependentOwner.DynamicPose.ParentTransformPath);
            return entry!=null&&transform!=null;
        }
        private static void ValidateList(FastList<EntitySerialisationEntry> list,Member[] saved)
        {
            if(list.Count!=saved.Length)throw new InvalidOperationException("Unsupported native scheduler membership change.");
            for(int i=0;i<saved.Length;i++)
            {
                var m=saved[i];var entry=list._items[i];
                if(!ReferenceEquals(entry,m.Entry))throw new InvalidOperationException("Native scheduler order/incarnation changed: "+m.Id);
                ValidateMember(entry,m);
            }
        }
        private static bool SameReferenceMembers(EntitySerialisationEntry[] current,EntitySerialisationEntry[] expected)
        {
            if(current==null||expected==null||current.Length!=expected.Length)return false;
            return expected.All(target=>current.Count(value=>ReferenceEquals(value,target))==1)
                &&current.All(value=>expected.Count(target=>ReferenceEquals(value,target))==1);
        }
        private static void ValidateMember(EntitySerialisationEntry entry,Member m)
        {
            if(entry==null||entry.m_Header.m_uEntityID!=m.Id||m.Object==null||m.Object.GetInstanceID()!=m.ObjectId
                ||!ReferenceEquals(entry.m_GameObject,m.Object)||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(m.Id),entry)
                ||!ReferenceEquals(entry.m_ServerSynchronisedComponents,m.ComponentList)||entry.m_ServerSynchronisedComponents.Count!=m.Components.Length)
                throw new InvalidOperationException("Native scheduler member incarnation changed: "+m.Id);
            for(int j=0;j<m.Components.Length;j++)
                if(!ReferenceEquals(entry.m_ServerSynchronisedComponents._items[j],m.Components[j]))
                    throw new InvalidOperationException("Native synchroniser component order changed: "+m.Id);
        }
        internal void Restore()
        {
            Validate();
            var currentFreeEntityIds=freeEntityIds.ToArray();ValidateFreeIds(currentFreeEntityIds);
            if(currentFreeEntityIds.Length!=savedFreeEntityIds.Length
                ||!new HashSet<ushort>(currentFreeEntityIds).SetEquals(savedFreeEntityIds))
                throw new InvalidOperationException("Native free entity-ID membership differs after core restoration.");
            freeEntityIds.Clear();foreach(var id in savedFreeEntityIds)freeEntityIds.Enqueue(id);
            next.SetValue(scheduler,nextUpdate);nextFast.SetValue(scheduler,nextFastUpdate);
            foreach(var m in regular.Concat(fast)) urgentField.SetValue(m.Entry,m.Urgent);
            // Do not use SetRequiresUrgentUpdate: it ORs the global flag, whereas
            // the actual captured per-entry/global state must be restored exactly.
            EntitySerialisationRegistry.HasUrgentOutgoingUpdates=urgent;
            VerifyRestored();
        }
        internal void VerifyRestored()
        {
            Validate();
            if((float)next.GetValue(scheduler)!=nextUpdate||(float)nextFast.GetValue(scheduler)!=nextFastUpdate
                ||EntitySerialisationRegistry.HasUrgentOutgoingUpdates!=urgent
                ||!freeEntityIds.SequenceEqual(savedFreeEntityIds)
                ||regular.Concat(fast).Any(m=>(bool)urgentField.GetValue(m.Entry)!=m.Urgent))
                throw new InvalidOperationException("Native scheduler state restoration differs.");
        }
        private static void ValidateFreeIds(ushort[] values)
        {
            var unique=new HashSet<ushort>();
            foreach(var id in values)
                if(id==0||!unique.Add(id)||EntitySerialisationRegistry.GetEntry(id)!=null)
                    throw new InvalidOperationException("Native free entity-ID queue contains an invalid/registered ID: "+id);
        }
        internal object Describe()
        {
            return new Dictionary<string,object>{{"controllerId",controllerId},{"nextUpdate",nextUpdate},{"nextFastUpdate",nextFastUpdate},
                {"fastNetworkChefs",fastChefs},{"globalUrgent",urgent},
                {"freeEntityIdCount",savedFreeEntityIds.Length},{"freeEntityIdOrderHash","0x"+Hash(savedFreeEntityIds).ToString("X8")},
                {"freeEntityIdHead",savedFreeEntityIds.Take(16).Select(id=>(int)id).ToArray()},
                {"registryEntityIds",registryEntries.Select(entry=>(int)entry.m_Header.m_uEntityID).ToArray()},
                {"physicsEntityIds",physicsPairs.Select(PhysicsId).ToArray()},
                {"regular",DescribeList(regular)},{"fast",DescribeList(fast)},
                {"scope","Actual ordered scheduler/entity-registry/physics-synchroniser memberships, free entity-ID allocation queue, and residual/urgent state; native Update, delays and clocks unchanged."}};
        }
        private static object[] DescribeList(Member[] members) { return members.Select(m=>(object)new Dictionary<string,object>{{"id",m.Id},{"objectId",m.ObjectId},{"urgent",m.Urgent},{"components",m.Components.Select(c=>c.GetType().FullName).ToArray()},
            {"initialAttachment",m.InitialAttachment},{"dynamicSpawnPrefabId",m.DynamicSpawnPrefab==null?0:m.DynamicSpawnPrefab.GetInstanceID()},
            {"dynamicOwnerPose",m.DynamicPose==null?null:(object)new Dictionary<string,object>{{"parentEntityId",(int)m.DynamicPose.ParentEntry.m_Header.m_uEntityID},
                {"attached",m.DynamicPose.Attached},{"detachedOnContainer",m.DynamicPose.DetachedOnContainer},
                {"parentTransformId",m.DynamicPose.Parent==null?0:m.DynamicPose.Parent.GetInstanceID()},
                {"parentTransformPath",m.DynamicPose.ParentTransformPath==null?null:
                    m.DynamicPose.ParentTransformPath.Select(node=>node.ChildIndex).ToArray()},
                {"cachedClientParentKind",ReferenceEquals(m.DynamicPose.CachedClientParent,null)?"null":
                    ReferenceEquals(m.DynamicPose.CachedClientParentObject,m.DynamicPose.ParentObject)?"parent-root":"surviving-other"},
                {"cachedClientParentType",m.DynamicPose.CachedClientParentType==null?null:m.DynamicPose.CachedClientParentType.FullName},
                {"cachedClientParentIndex",m.DynamicPose.CachedClientParentIndex},
                {"localPosition",Point(m.DynamicPose.LocalPosition)},{"worldPosition",Point(m.DynamicPose.WorldPosition)},
                {"localRotation",Rotation(m.DynamicPose.LocalRotation)},{"worldRotation",Rotation(m.DynamicPose.WorldRotation)}}}}).ToArray(); }
        private static uint Hash(IEnumerable<ushort> values) {uint hash=2166136261;foreach(var value in values){hash=(hash^(byte)value)*16777619;hash=(hash^(byte)(value>>8))*16777619;}return hash;}
        private static int PhysicsId(object value)
        {
            var entry=value==null?null:physicsEntryField.GetValue(value) as EntitySerialisationEntry;
            return entry==null?0:(int)entry.m_Header.m_uEntityID;
        }
        private static bool Finite(float v) { return !float.IsNaN(v)&&!float.IsInfinity(v); }
        private static bool Finite(Vector3 v) { return Finite(v.x)&&Finite(v.y)&&Finite(v.z); }
        private static bool Finite(UQuaternion q) { return Finite(q.x)&&Finite(q.y)&&Finite(q.z)&&Finite(q.w); }
        private static bool Exact(Vector3 a,Vector3 b) { return a.x==b.x&&a.y==b.y&&a.z==b.z; }
        private static bool Exact(UQuaternion a,UQuaternion b) { return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w; }
        private static bool HasAncestor(Transform child,Transform parent)
        {for(var value=child;value!=null;value=value.parent)if(ReferenceEquals(value,parent))return true;return false;}
        private static bool Destroyed(UnityEngine.Object value) {return !ReferenceEquals(value,null)&&value==null;}
        private static object Point(Vector3 value) {return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z}};}
        private static object Rotation(UQuaternion value) {return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z},{"w",value.w}};}
    }
}
