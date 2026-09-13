using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hpmv;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed class WorldSyncTargetInspectorModule : IAuthoringModule
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private bool disposed;
        public string Name { get { return "world-sync-target-inspector-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException(Name);
            if(operation!="status"||args==null||args.Count!=0)throw new ArgumentException("Use status with no arguments.");
            var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(value=>
                value.GetName().Name=="WorldSyncCache.r12f-surviving-loose-pending");
            var moduleType=assembly.GetType("SuperchargedPatch.Authoring.Modules.WorldSyncCacheModule",true);
            var module=Field(moduleType,"active").GetValue(null);
            var saved=(IDictionary)Field(moduleType,"saved").GetValue(module);
            var target=saved[444];
            if(target==null)return Map("target",null,"savedKeys",saved.Keys.Cast<object>().ToArray());
            var scheduler=Read(target,"Scheduler");
            var regular=((Array)Read(scheduler,"regular")).Cast<object>().ToArray();
            var fast=((Array)Read(scheduler,"fast")).Cast<object>().ToArray();
            var interesting=regular.Concat(fast).Where(value=>
            {
                uint id=Convert.ToUInt32(Read(value,"Id"));
                return Convert.ToUInt32(Read(value,"ContainerId"))!=0
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(id),Read(value,"Entry"));
            }).Select(EncodeMember).ToArray();
            var currentRegular=CurrentIds(Read(scheduler,"regularList"));
            var currentFast=CurrentIds(Read(scheduler,"fastList"));
            var savedFree=((IEnumerable)Read(scheduler,"savedFreeEntityIds")).Cast<object>().ToArray();
            var currentFree=((IEnumerable)Read(scheduler,"freeEntityIds")).Cast<object>().ToArray();
            var initialType=typeof(NativeKitchenCheckpoint).Assembly.GetType("SuperchargedPatch.NativeSceneMetadata",true);
            var initialAttachments=(IEnumerable)Field(initialType,"InitialPhysicalAttachmentIds").GetValue(null);
            var initialBodies=(IEnumerable)Field(initialType,"InitialRigidBodyIds").GetValue(null);
            return Map("targetFrame",Read(target,"Frame"),"savedKeys",saved.Keys.Cast<object>().ToArray(),
                "interestingMembers",interesting,"savedRegularIds",regular.Select(Id).ToArray(),
                "savedFastIds",fast.Select(Id).ToArray(),"currentRegularIds",currentRegular,
                "currentFastIds",currentFast,"savedFreeEntityIds",savedFree,"currentFreeEntityIds",currentFree,
                "initialAttachmentIds",initialAttachments.Cast<object>().ToArray(),
                "initialBodyIds",initialBodies.Cast<object>().ToArray());
        }

        private static object EncodeMember(object value)
        {
            uint id=Convert.ToUInt32(Read(value,"Id"));
            var entry=(EntitySerialisationEntry)Read(value,"Entry");
            var current=EntitySerialisationRegistry.GetEntry(id);
            var obj=(GameObject)Read(value,"Object");
            var pose=Read(value,"DynamicPose");
            return Map("id",id,"containerId",Read(value,"ContainerId"),
                "savedEntryExact",ReferenceEquals(entry,current),"savedObject",EncodeObject(obj),
                "currentObject",EncodeObject(current==null?null:current.m_GameObject),
                "serverComponents",((Array)Read(value,"Components")).Cast<object>().Select(item=>item.GetType().FullName).ToArray(),
                "clientComponents",((Array)Read(value,"ClientComponents")).Cast<object>().Select(item=>item.GetType().FullName).ToArray(),
                "objectComponents",((Array)Read(value,"ObjectComponents")).Cast<Type>().Select(item=>item.FullName).ToArray(),
                "dynamicPose",pose==null?null:Map("attached",Read(pose,"Attached"),
                    "detachedOnContainer",Read(pose,"DetachedOnContainer"),
                    "parentEntryId",EntryId((EntitySerialisationEntry)Read(pose,"ParentEntry")),
                    "parentObject",EncodeObject((GameObject)Read(pose,"ParentObject")),
                    "localPosition",Read(pose,"LocalPosition"),"localRotation",Read(pose,"LocalRotation"),
                    "localScale",Read(pose,"LocalScale")),
                "dynamicBodyTokenPresent",Read(value,"DynamicBodyToken")!=null);
        }

        private static uint Id(object value){return Convert.ToUInt32(Read(value,"Id"));}
        private static uint EntryId(EntitySerialisationEntry value){return value==null?0:value.m_Header.m_uEntityID;}
        private static uint[] CurrentIds(object list)
        {
            int count=Convert.ToInt32(list.GetType().GetProperty("Count",Any).GetValue(list,null));
            var items=(Array)Read(list,"_items");var result=new uint[count];
            for(int i=0;i<count;i++)result[i]=((EntitySerialisationEntry)items.GetValue(i)).m_Header.m_uEntityID;
            return result;
        }

        private static object EncodeObject(GameObject value)
        {
            bool alive=!ReferenceEquals(value,null)&&value!=null;
            return Map("referenceNull",ReferenceEquals(value,null),"alive",alive,
                "instanceId",alive?value.GetInstanceID():0,"name",alive?value.name:null);
        }
        private static object Read(object value,string name){return value==null?null:Field(value.GetType(),name).GetValue(value);}
        private static FieldInfo Field(Type type,string name)
        { var value=type.GetField(name,Any);if(value==null)throw new MissingFieldException(type.FullName,name);return value; }
        private static Dictionary<string,object> Map(params object[] values)
        { var result=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)result.Add((string)values[i],values[i+1]);return result; }
        public void Dispose(){disposed=true;}
    }
}
