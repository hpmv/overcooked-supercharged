using System;
using System.Collections.Generic;
using Hpmv;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed partial class RegistryObserverModule
    {
        private static int Integer(Dictionary<string,object> args,string key)
        {
            object value;
            if(!args.TryGetValue(key,out value) || !(value is int || value is long)) throw new ArgumentException("Integer required: "+key);
            return Convert.ToInt32(value);
        }
        private object ObserveAbsent(Dictionary<string,object> args)
        {
            if(args.Count!=3) throw new ArgumentException("observe-absent requires entityId, sceneMetadataRefreshes and priorBodyInstanceId.");
            int id=Integer(args,"entityId"), epoch=Integer(args,"sceneMetadataRefreshes"), priorBody=Integer(args,"priorBodyInstanceId");
            if(id<=0 || epoch<=0 || priorBody==0) throw new ArgumentException("Positive entity/scene epoch and nonzero prior native body instance required.");
            RequireBoundary();
            if(epoch!=NativeSceneMetadata.Refreshes) throw new InvalidOperationException("Prior observation belongs to another native scene refresh epoch.");
            var bodies=InitialSet("InitialRigidBodyIds"); var attachments=InitialSet("InitialPhysicalAttachmentIds");
            var beforeBodies=new HashSet<int>(bodies); var beforeAttachments=new HashSet<int>(attachments);
            int fixedUpperBound=0; foreach(int initial in bodies) fixedUpperBound=Math.Max(fixedUpperBound,initial);
            foreach(int initial in attachments) fixedUpperBound=Math.Max(fixedUpperBound,initial);
            if(fixedUpperBound==0 || id<=fixedUpperBound || bodies.Contains(id) || attachments.Contains(id))
                throw new InvalidOperationException("Absence reconciliation is limited to later dynamic IDs; initial objects are unsupported.");
            var server=Injector.Server; var output=server.CurrentFrameData;
            var entries=EntitySerialisationRegistry.m_EntitiesList; int count=entries.Count;
            if(count<1 || count>4096) throw new InvalidOperationException("Native current registry is missing or excessive.");
            if(EntitySerialisationRegistry.GetEntry((uint)id)!=null) throw new InvalidOperationException("Requested native entity still has a registry entry.");
            var currentIds=new List<int>();var currentObjects=new List<GameObject>();var currentInstances=new List<int>();var bodyIds=new List<int>();
            var unique=new HashSet<int>();
            for(int i=0;i<count;i++)
            {
                var entry=entries._items[i];var obj=entry.m_GameObject;int current=(int)entry.m_Header.m_uEntityID;
                if(current==id || current<=0 || !unique.Add(current) || obj==null) throw new InvalidOperationException("Current native registry is ambiguous, incomplete or contains the requested ID.");
                if(!object.ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)current),entry)) throw new InvalidOperationException("Native registry lookup/list disagree.");
                var body=obj.GetComponent<Rigidbody>();
                if(body!=null) {if(body.GetInstanceID()==priorBody) throw new InvalidOperationException("Prior body instance is still registered under a native ID.");bodyIds.Add(body.GetInstanceID());}
                currentIds.Add(current);currentObjects.Add(obj);currentInstances.Add(obj.GetInstanceID());
            }
            RequireBoundary();
            if(epoch!=NativeSceneMetadata.Refreshes || !object.ReferenceEquals(output,server.CurrentFrameData) || !object.ReferenceEquals(entries,EntitySerialisationRegistry.m_EntitiesList) || entries.Count!=count || EntitySerialisationRegistry.GetEntry((uint)id)!=null)
                throw new InvalidOperationException("Native observation epoch or absence changed before publication.");
            for(int i=0;i<count;i++)
                if((int)entries._items[i].m_Header.m_uEntityID!=currentIds[i] || !object.ReferenceEquals(entries._items[i].m_GameObject,currentObjects[i]) || currentObjects[i].GetInstanceID()!=currentInstances[i])
                    throw new InvalidOperationException("Native registry incarnation/order changed during absence inspection.");
            if(!object.ReferenceEquals(bodies,InitialSet("InitialRigidBodyIds")) || !object.ReferenceEquals(attachments,InitialSet("InitialPhysicalAttachmentIds")) || !bodies.SetEquals(beforeBodies) || !attachments.SetEquals(beforeAttachments))
                throw new InvalidOperationException("Initial fixed membership changed during absence observation.");
            if(output.ServerMessages!=null && output.ServerMessages.Count>=8192) throw new InvalidOperationException("Pending controller observation queue is full.");
            // This existing helper writes only a framework observation to the
            // pending controller output. It does not call native RemoveEntry,
            // destroy an object or send a native server/network event.
            EntityRetirementMessageSender.SendMessageToRetireEntity((uint)id);
            return last=new Dictionary<string,object> {
                {"ok",true},{"entityId",id},{"sceneMetadataRefreshes",epoch},{"priorBodyInstanceId",priorBody},
                {"source","observed-native-registry-absence"},{"historicalRemovalEventObserved",false},
                {"priorIdentitySource","Caller supplies the prior captured body identity; this operation observes current absence, not a historical RemoveEntry callback."},
                {"currentRegisteredIds",currentIds.ToArray()},{"currentRegisteredBodyInstances",bodyIds.ToArray()},
                {"nativeStateChanged",false},{"initialSetsChanged",false},{"frameworkOnlyRetirementPublished",true},
                {"scope","Current lookup/list absence and absence of the prior instance among registered bodies. No claim that an unregistered or inactive Unity object was destroyed."}
            };
        }
    }
}
