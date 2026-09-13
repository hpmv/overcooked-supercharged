using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only ordered snapshot of the native physics synchroniser list and
    // registered Rigidbody containers.  No callbacks or Unity objects are retained.
    public sealed class PhysicsListInspectionModule : IAuthoringModule
    {
        private bool disposed;
        public string Name { get { return "native-physics-list-inspection-v1"; } }
        public int ApiVersion { get { return 1; } }
        public void Dispose() { disposed=true; }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("PhysicsListInspectionModule");
            if(operation=="inspect-checkpoint")return InspectCheckpoint(args);
            if(operation!="inspect")throw new ArgumentException("Use inspect or inspect-checkpoint.");
            var listField=AccessTools.Field(typeof(ServerPhysicsObjectSynchroniser),
                "ms_ServerPhysicsObjectSytnchroniserTransforms");
            var pairType=typeof(ServerPhysicsObjectSynchroniser).GetNestedType(
                "SerialisationEntryTransformPair",BindingFlags.Public|BindingFlags.NonPublic);
            var entryField=pairType==null?null:AccessTools.Field(pairType,"m_Entry");
            var transformField=pairType==null?null:AccessTools.Field(pairType,"m_Transform");
            var list=listField==null?null:listField.GetValue(null) as IList;
            if(list==null||entryField==null||transformField==null)
                throw new InvalidOperationException("Native physics canonical list contract differs.");
            var ordered=new List<object>();
            for(int i=0;i<list.Count;i++)
            {
                var pair=list[i];
                var entry=pair==null?null:entryField.GetValue(pair) as EntitySerialisationEntry;
                var transform=pair==null?null:transformField.GetValue(pair) as Transform;
                ordered.Add(Map("index",i,"pairType",pair==null?null:pair.GetType().FullName,
                    "entityId",entry==null?0:(int)entry.m_Header.m_uEntityID,
                    "entryObject",entry==null||entry.m_GameObject==null?null:entry.m_GameObject.name,
                    "entryObjectId",entry==null||entry.m_GameObject==null?0:entry.m_GameObject.GetInstanceID(),
                    "transform",transform==null?null:transform.gameObject.name,
                    "transformId",transform==null?0:transform.GetInstanceID(),
                    "registered",entry!=null&&ReferenceEquals(EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID),entry)));
            }
            var bodies=new List<object>();var registry=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<registry.Count;i++)
            {
                var entry=registry._items[i];var obj=entry==null?null:entry.m_GameObject;
                var body=obj==null?null:obj.GetComponent<Rigidbody>();
                if(body==null)continue;
                bodies.Add(Map("registryIndex",i,"entityId",(int)entry.m_Header.m_uEntityID,
                    "name",obj.name,"objectId",obj.GetInstanceID(),"bodyId",body.GetInstanceID(),
                    "colliderCount",body.gameObject.GetComponentsInChildren<Collider>(true).Length,
                    "physicsSynchroniserCount",obj.GetComponents<ServerPhysicsObjectSynchroniser>().Length));
            }
            return Map("ok",true,"canonicalCount",list.Count,"canonical",ordered.ToArray(),
                "registeredBodies",bodies.ToArray(),
                "scope","Read-only ordered native list and registry observation; no mutation.");
        }

        private static object InspectCheckpoint(Dictionary<string,object> args)
        {
            object rawFrame;
            if(args==null||!args.TryGetValue("frame",out rawFrame))
                throw new ArgumentException("inspect-checkpoint requires frame.");
            int frame=Convert.ToInt32(rawFrame);
            var checkpointType=AccessTools.TypeByName("SuperchargedPatch.NativeKitchenCheckpoint");
            var historyField=checkpointType==null?null:AccessTools.Field(checkpointType,"history");
            var history=historyField==null?null:historyField.GetValue(null) as IDictionary;
            if(history==null||!history.Contains(frame))
                throw new InvalidOperationException("Native checkpoint frame is unavailable: "+frame);
            var snapshot=history[frame];
            var topologyField=snapshot==null?null:AccessTools.Field(snapshot.GetType(),"InitialAttachmentTopology");
            var topology=topologyField==null?null:topologyField.GetValue(snapshot);
            var physicsRecordsField=topology==null?null:AccessTools.Field(topology.GetType(),"physics");
            var physicsRecords=physicsRecordsField==null?null:physicsRecordsField.GetValue(topology) as Array;
            if(physicsRecords==null)
                throw new InvalidOperationException("Native checkpoint topology physics records are unavailable.");

            var listField=AccessTools.Field(typeof(ServerPhysicsObjectSynchroniser),
                "ms_ServerPhysicsObjectSytnchroniserTransforms");
            var pairType=typeof(ServerPhysicsObjectSynchroniser).GetNestedType(
                "SerialisationEntryTransformPair",BindingFlags.Public|BindingFlags.NonPublic);
            var pairEntryField=pairType==null?null:AccessTools.Field(pairType,"m_Entry");
            var pairTransformField=pairType==null?null:AccessTools.Field(pairType,"m_Transform");
            var list=listField==null?null:listField.GetValue(null) as IList;
            if(list==null||pairEntryField==null||pairTransformField==null)
                throw new InvalidOperationException("Native physics canonical list contract differs.");

            var rows=new List<object>();
            for(int i=0;i<physicsRecords.Length;i++)
            {
                var record=physicsRecords.GetValue(i);
                var recordType=record==null?null:record.GetType();
                var value=recordType==null?null:AccessTools.Field(recordType,"Value").GetValue(record);
                var entry=recordType==null?null:AccessTools.Field(recordType,"Entry").GetValue(record) as EntitySerialisationEntry;
                var transform=recordType==null?null:AccessTools.Field(recordType,"Transform").GetValue(record) as Transform;
                int canonicalReferenceIndex=-1;
                for(int j=0;j<list.Count;j++)if(ReferenceEquals(list[j],value)){canonicalReferenceIndex=j;break;}
                rows.Add(Map("index",i,"entityId",entry==null?0:(int)entry.m_Header.m_uEntityID,
                    "entryObject",entry==null||entry.m_GameObject==null?null:entry.m_GameObject.name,
                    "entryObjectId",entry==null||entry.m_GameObject==null?0:entry.m_GameObject.GetInstanceID(),
                    "transform",transform==null?null:transform.gameObject.name,
                    "transformId",transform==null?0:transform.GetInstanceID(),
                    "registered",entry!=null&&ReferenceEquals(EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID),entry),
                    "canonicalReferenceIndex",canonicalReferenceIndex));
            }
            return Map("ok",true,"frame",frame,"topologyPhysicsCount",physicsRecords.Length,
                "canonicalCount",list.Count,"topologyPhysics",rows.ToArray(),
                "scope","Read-only reflection over the retained native checkpoint topology; no mutation.");
        }

        private static Dictionary<string,object> Map(params object[] values)
        {
            var result=new Dictionary<string,object>();
            for(int i=0;i<values.Length;i+=2)result[(string)values[i]]=values[i+1];
            return result;
        }
    }
}
