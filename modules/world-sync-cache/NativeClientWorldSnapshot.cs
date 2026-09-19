using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Client parenting decisions use their own cache, not Transform.parent.
    // Transport sequence/timestamp state is intentionally outside this class:
    // native network transport is not rewound by this authoring sidecar.
    internal sealed class NativeClientWorldSnapshot
    {
        private static FieldInfo transformField,parentField,parentIdField,hasParentField,positionField,lerperField,
            pausedField,pendingField,physicalField,callbackField,receivedField;
        private readonly int id,objectId,clientId,parentObjectId,parentComponentId,parentComponentIndex,lerperId;
        private readonly GameObject owner,parentObject;
        private readonly ClientWorldObjectSynchroniser client;
        private readonly Transform transform;
        private readonly PhysicalAttachment physical;
        private readonly IParentable parent;
        private readonly Component parentComponent,lerperComponent;
        private readonly Type parentComponentType;
        private readonly EntitySerialisationEntry parentEntry;
        private readonly uint parentId;
        private readonly bool hasParent,paused,received;
        private readonly Vector3 serverPosition;
        private readonly Lerp lerper;
        private readonly object callback;

        internal static void ValidateContract()
        {
            transformField=Field("m_Transform",typeof(Transform));parentField=Field("m_Parent",typeof(IParentable));
            parentIdField=Field("m_ParentEntityID",typeof(uint));hasParentField=Field("m_bHasParent",typeof(bool));
            positionField=Field("m_ServerPosition",typeof(Vector3));lerperField=Field("m_Lerper",typeof(Lerp));
            pausedField=Field("m_bPaused",typeof(bool));pendingField=Field("m_PendingResumeData",typeof(Serialisable));
            physicalField=Field("m_PhysicalAttachment",typeof(PhysicalAttachment));callbackField=Field("m_parentChanged",typeof(GenericVoid));
            receivedField=Field("m_bHasEverReceived",typeof(bool));
        }
        private static FieldInfo Field(string name,Type expected)
        {
            var field=AccessTools.Field(typeof(ClientWorldObjectSynchroniser),name);
            if(field==null||field.FieldType!=expected)throw new InvalidOperationException("Native client world field differs: "+name);
            return field;
        }
        internal NativeClientWorldSnapshot(int entity,GameObject obj) : this(entity,obj,null,null) {}
        private NativeClientWorldSnapshot(int entity,GameObject obj,NativeClientWorldSnapshot target,
            EntitySerialisationEntry replacementParent)
        {
            id=entity;owner=obj;objectId=obj.GetInstanceID();
            var clients=obj.GetComponents<ClientWorldObjectSynchroniser>();
            if(clients.Length!=1||clients[0].GetType()!=typeof(ClientWorldObjectSynchroniser))
                throw new InvalidOperationException("Unsupported client WorldObject role: "+id);
            client=clients[0];clientId=client.GetInstanceID();transform=(Transform)transformField.GetValue(client);
            physical=(PhysicalAttachment)physicalField.GetValue(client);
            parent=target==null?(IParentable)parentField.GetValue(client):replacementParent==null
                ?target.parent:ResolveReplacementParent(target,replacementParent);
            parentId=target==null?(uint)parentIdField.GetValue(client):target.parentId;
            hasParent=target==null?(bool)hasParentField.GetValue(client):target.hasParent;
            paused=target==null?(bool)pausedField.GetValue(client):target.paused;
            received=target==null?(bool)receivedField.GetValue(client):target.received;
            serverPosition=target==null?(Vector3)positionField.GetValue(client):target.serverPosition;
            lerper=(Lerp)lerperField.GetValue(client);callback=callbackField.GetValue(client);
            if(transform!=obj.transform||physical==null||physical!=obj.GetComponent<PhysicalAttachment>()
                ||target==null&&(paused||pendingField.GetValue(client)!=null)||!Finite(serverPosition))
                throw new InvalidOperationException("Unsupported pending/native client WorldObject state: "+id);
            if(lerper!=null)
            {
                lerperComponent=lerper as Component;
                if(lerper.GetType()!=typeof(EmptyLerp)||lerperComponent==null||lerperComponent.gameObject!=obj)
                    throw new InvalidOperationException("Stateful native client world interpolation: "+id);
                lerperId=lerperComponent.GetInstanceID();
            }
            if(hasParent)
            {
                if(parentId==0)throw new InvalidOperationException("Unresolved native client cached parent: "+id);
                parentEntry=target==null?EntitySerialisationRegistry.GetEntry(parentId):replacementParent??target.parentEntry;
                if(parentEntry==null||parentEntry.m_GameObject==null)
                    throw new InvalidOperationException("Client cached parent is not its registered incarnation: "+id);
                parentObject=parentEntry.m_GameObject;
                parentObjectId=parentObject.GetInstanceID();
                // Native StartSynchronising can set the ID while leaving this cache null.
                // Pin the referenced registry owner, but do not resolve or invent m_Parent.
                if(parent!=null)
                {
                    parentComponent=parent as Component;
                    if(parentComponent==null||!ReferenceEquals(parentComponent.gameObject,parentObject))
                        throw new InvalidOperationException("Client cached parent component differs from its registered incarnation: "+id);
                    parentComponentId=parentComponent.GetInstanceID();
                    parentComponentType=parentComponent.GetType();
                    if(target==null)
                    {
                        var components=parentObject.GetComponents<Component>();
                        parentComponentIndex=Array.FindIndex(components,value=>ReferenceEquals(value,parentComponent));
                        if(parentComponentIndex<0)
                            throw new InvalidOperationException("Client cached parent component is absent from its owner: "+id);
                    }
                    else parentComponentIndex=target.parentComponentIndex;
                }
            }
            else if(parent!=null||parentId!=0)throw new InvalidOperationException("Unsupported unresolved client parent cache: "+id);
            Validate();
        }
        internal NativeClientWorldSnapshot Rebind(GameObject obj)
        {
            if(obj==null)throw new ArgumentNullException("obj");
            return new NativeClientWorldSnapshot(id,obj,this,null);
        }
        internal NativeClientWorldSnapshot Rebind(GameObject obj,EntitySerialisationEntry replacementParent)
        {
            if(obj==null)throw new ArgumentNullException("obj");
            if(replacementParent==null)throw new ArgumentNullException("replacementParent");
            return new NativeClientWorldSnapshot(id,obj,this,replacementParent);
        }
        internal void ValidateForParentRebind(EntitySerialisationEntry historicalParent)
        {
            ValidateOwner();
            if(historicalParent==null||ReferenceEquals(historicalParent.m_GameObject,null)||!hasParent
                ||parentId!=historicalParent.m_Header.m_uEntityID
                ||!ReferenceEquals(parentEntry,historicalParent)
                ||!ReferenceEquals(parentObject,historicalParent.m_GameObject)
                ||ReferenceEquals(parent,null)||ReferenceEquals(parentComponent,null)
                ||parentComponentIndex<0||parentComponentType==null)
                throw new InvalidOperationException("Native client cached parent is not the admitted historical incarnation: "+id);
        }
        internal NativeClientWorldSnapshot RebindParent(EntitySerialisationEntry replacementParent)
        {
            ValidateOwner();
            if(replacementParent==null||replacementParent.m_GameObject==null||!hasParent
                ||replacementParent.m_Header.m_uEntityID!=parentId)
                throw new InvalidOperationException("Native client replacement parent differs: "+id);
            return new NativeClientWorldSnapshot(id,owner,this,replacementParent);
        }
        internal void Validate()
        {
            ValidateOwner();
            if(hasParent&&(parentObject==null||parentObject.GetInstanceID()!=parentObjectId
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(parentId),parentEntry)||!ReferenceEquals(parentEntry.m_GameObject,parentObject)
                ||(parent!=null&&(parentComponent==null||parentComponent.GetInstanceID()!=parentComponentId
                    ||parentComponent.GetType()!=parentComponentType||!ReferenceEquals(parentComponent.gameObject,parentObject)))))
                throw new InvalidOperationException("Native client cached-parent incarnation changed: "+id);
        }
        private void ValidateOwner()
        {
            var entry=EntitySerialisationRegistry.GetEntry((uint)id);
            if(owner==null||owner.GetInstanceID()!=objectId||entry==null||!ReferenceEquals(entry.m_GameObject,owner)
                ||client==null||client.GetInstanceID()!=clientId||!ReferenceEquals(owner.GetComponent<ClientWorldObjectSynchroniser>(),client)
                ||!ReferenceEquals(transformField.GetValue(client),transform)||!ReferenceEquals(physicalField.GetValue(client),physical)
                ||physical==null||!ReferenceEquals(owner.GetComponent<PhysicalAttachment>(),physical)
                ||!ReferenceEquals(lerperField.GetValue(client),lerper)||!ReferenceEquals(callbackField.GetValue(client),callback)
                ||(lerper!=null&&(lerperComponent==null||lerperComponent.GetInstanceID()!=lerperId)))
                throw new InvalidOperationException("Native client world component/lerper/callback incarnation changed: "+id);
        }
        private static IParentable ResolveReplacementParent(NativeClientWorldSnapshot target,
            EntitySerialisationEntry replacementParent)
        {
            if(target==null||ReferenceEquals(target.parent,null)||ReferenceEquals(target.parentComponent,null)
                ||target.parentComponentIndex<0||target.parentComponentType==null
                ||replacementParent==null||replacementParent.m_GameObject==null
                ||replacementParent.m_Header.m_uEntityID!=target.parentId)
                throw new InvalidOperationException("Native client parent replacement is not structurally identified: "+(target==null?0:target.id));
            var components=replacementParent.m_GameObject.GetComponents<Component>();
            var replacement=target.parentComponentIndex<components.Length?components[target.parentComponentIndex]:null;
            var parentable=replacement as IParentable;
            if(replacement==null||replacement.GetType()!=target.parentComponentType||parentable==null)
                throw new InvalidOperationException("Native client parent replacement component topology differs: "+target.id);
            return parentable;
        }
        internal void RequireExactParent(EntitySerialisationEntry expectedEntry,IParentable expectedParent)
        {
            var expectedComponent=expectedParent as Component;
            if(expectedEntry==null||expectedEntry.m_GameObject==null||expectedComponent==null
                ||!ReferenceEquals(expectedComponent.gameObject,expectedEntry.m_GameObject)
                ||!hasParent||parentId!=expectedEntry.m_Header.m_uEntityID
                ||!ReferenceEquals(parentEntry,expectedEntry)||!ReferenceEquals(parentObject,expectedEntry.m_GameObject)
                ||!ReferenceEquals(parent,expectedParent)||!ReferenceEquals(parentComponent,expectedComponent))
                throw new InvalidOperationException("Native client WorldObject parent is not the exact registered container: "+id);
        }
        internal void Restore()
        {
            Validate();
            parentField.SetValue(client,parent);parentIdField.SetValue(client,parentId);hasParentField.SetValue(client,hasParent);
            positionField.SetValue(client,serverPosition);pausedField.SetValue(client,paused);pendingField.SetValue(client,null);receivedField.SetValue(client,received);
            VerifyRestored();
        }
        internal void VerifyRestored()
        {
            Validate();
            if(!ReferenceEquals(parentField.GetValue(client),parent)||(uint)parentIdField.GetValue(client)!=parentId
                ||(bool)hasParentField.GetValue(client)!=hasParent||(bool)pausedField.GetValue(client)!=paused
                ||pendingField.GetValue(client)!=null||(bool)receivedField.GetValue(client)!=received
                ||!Exact((Vector3)positionField.GetValue(client),serverPosition))
                throw new InvalidOperationException("Native client WorldObject cache restoration differs: "+id);
        }
        internal object Describe()
        {
            return new Dictionary<string,object>{{"id",id},{"clientId",clientId},{"hasParent",hasParent},{"parentId",parentId},
                {"parentObjectId",parentObjectId},{"parentComponentId",parentComponentId},{"serverPosition",Point(serverPosition)},
                {"paused",paused},{"pendingResume",false},{"hasEverReceived",received},{"lerper",lerper==null?null:lerper.GetType().FullName}};
        }
        internal object DescribeCurrent()
        {
            return new Dictionary<string,object>{{"id",id},{"clientId",clientId},{"hasParent",hasParentField.GetValue(client)},
                {"parentId",parentIdField.GetValue(client)},{"parentComponentId",(parentField.GetValue(client) as Component)?.GetInstanceID()},
                {"serverPosition",Point((Vector3)positionField.GetValue(client))},{"paused",pausedField.GetValue(client)},
                {"pendingResume",pendingField.GetValue(client)!=null},{"hasEverReceived",receivedField.GetValue(client)}};
        }
        private static object Point(Vector3 v) { return new[]{v.x,v.y,v.z}; }
        private static bool Finite(Vector3 v) { return Finite(v.x)&&Finite(v.y)&&Finite(v.z); }
        private static bool Finite(float v) { return !float.IsNaN(v)&&!float.IsInfinity(v); }
        private static bool Exact(Vector3 a,Vector3 b) { return a.x==b.x&&a.y==b.y&&a.z==b.z; }
    }
}
