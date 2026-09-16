using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hpmv;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed class WarpSpecObserverModule : IAuthoringModule
    {
        private static WarpSpecObserverModule active;
        private Harmony harmony;
        private object last;
        private long observations;
        private bool disposed;
        public string Name { get { return "warp-spec-observer-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException(Name);
            if(args==null||args.Count!=0)throw new ArgumentException("Warp observer operations take no arguments.");
            if(operation=="activate")Activate();
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, deactivate or status.");
            return Map("active",ReferenceEquals(active,this),"observations",observations,"last",last,
                "scope","Read-only WarpSpec capture before native authoring preflight; no game or controller state is changed.");
        }

        private void Activate()
        {
            if(ReferenceEquals(active,this))return;
            if(active!=null)throw new InvalidOperationException("Another WarpSpec observer is active.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("WarpSpec observer activation requires the authoring pause fence.");
            harmony=new Harmony("supercharged.authoring.warp-spec-observer."+GetType().Assembly.GetName().Name);
            var method=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"Prepare",new[]{typeof(WarpSpec)});
            var prefix=new HarmonyMethod(GetType().GetMethod("BeforePrepare",BindingFlags.Public|BindingFlags.Static));
            prefix.priority=Priority.First;harmony.Patch(method,prefix:prefix);active=this;
        }

        public static void BeforePrepare(WarpSpec __0)
        {
            var module=active;if(module==null)return;
            var rows=__0==null||__0.Entities==null?new object[0]:__0.Entities.Select(Encode).ToArray();
            var current=new List<int>();var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)current.Add((int)entries._items[i].m_Header.m_uEntityID);
            module.last=Map("frame",__0==null?-1:__0.Frame,
                "entitiesToDelete",__0==null||__0.EntitiesToDelete==null?null:(object)__0.EntitiesToDelete.ToArray(),
                "entities",rows,"currentEntityIds",current.ToArray());
            module.observations++;
        }

        private static object Encode(EntityWarpSpec value)
        {
            var blocks=value.GetType().GetProperties(BindingFlags.Instance|BindingFlags.Public)
                .Where(property=>property.CanRead&&property.Name!="__isset")
                .Select(property=>new {property.Name,Value=property.GetValue(value,null)})
                .Where(item=>item.Value!=null&&!item.Name.StartsWith("Entity",StringComparison.Ordinal)
                    &&item.Name!="SpawningPath"&&item.Name!="Position"&&item.Name!="Rotation"
                    &&item.Name!="Velocity"&&item.Name!="AngularVelocity")
                .Select(item=>item.Name).OrderBy(name=>name).ToArray();
            return Map("hasEntityId",value.__isset.entityId,"entityId",value.__isset.entityId?(object)value.EntityId:null,
                "hasPath",value.__isset.entityPathReference,
                "path",value.__isset.entityPathReference&&value.EntityPathReference!=null?(object)value.EntityPathReference.Ids.ToArray():null,
                "spawnPath",value.SpawningPath==null?null:(object)value.SpawningPath.ToArray(),"blocks",blocks,
                "chefCarry",value.ChefCarry==null?null:(object)Map("carried",EncodeRef(value.ChefCarry.CarriedItem)),
                "ingredientContainer",value.IngredientContainer==null?null:(object)"present");
        }

        private static object EncodeRef(EntityIdOrRef value)
        {
            if(value==null)return null;
            return Map("hasId",value.__isset.entityId,"id",value.__isset.entityId?(object)value.EntityId:null,
                "hasPath",value.__isset.entityPathReference,
                "path",value.__isset.entityPathReference&&value.EntityPathReference!=null?(object)value.EntityPathReference.Ids.ToArray():null);
        }

        private static Dictionary<string,object> Map(params object[] values)
        { var result=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)result.Add((string)values[i],values[i+1]);return result; }
        private void Deactivate(){if(!ReferenceEquals(active,this))return;active=null;if(harmony!=null)harmony.UnpatchSelf();harmony=null;}
        public void Dispose(){Deactivate();disposed=true;}
    }
}
