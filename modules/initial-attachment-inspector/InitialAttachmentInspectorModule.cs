using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Hpmv;
using SuperchargedPatch.Bridge;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only development inspector for retained core attachment/body rows.
    public sealed class InitialAttachmentInspectorModule : IAuthoringModule
    {
        private bool disposed;
        public string Name { get { return "initial-attachment-checkpoint-inspector-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("InitialAttachmentInspectorModule");
            if(operation=="status")return new Dictionary<string,object>{{"name",Name},{"apiVersion",1},{"readOnly",true}};
            if(operation!="inspect")throw new ArgumentException("Use inspect or status.");
            if(args==null||args.Count!=1||!args.ContainsKey("frame")||args["frame"]==null||
                (args["frame"].GetType()!=typeof(int)&&args["frame"].GetType()!=typeof(long)))
                throw new ArgumentException("inspect requires exactly one whole-number frame.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Initial-attachment inspection requires the authoring pause fence.");
            long requested=Convert.ToInt64(args["frame"]);
            if(requested<int.MinValue||requested>int.MaxValue)throw new ArgumentOutOfRangeException("frame");
            return Inspect((int)requested);
        }

        private static object Inspect(int frame)
        {
            const BindingFlags stat=BindingFlags.Static|BindingFlags.NonPublic;
            const BindingFlags inst=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var historyField=typeof(NativeKitchenCheckpoint).GetField("history",stat);
            var history=historyField==null?null:historyField.GetValue(null) as IDictionary;
            object snapshot=history==null?null:history[frame];
            if(snapshot==null)throw new InvalidOperationException("No retained native checkpoint at frame "+frame+".");
            var attachments=ArrayField(snapshot,"FixedAttachmentPoses",inst);
            var bodies=ArrayField(snapshot,"FixedBodyPoses",inst);
            return new Dictionary<string,object>{{"frame",frame},{"readOnly",true},{"gameStateMutation",false},
                {"missingAttachments",attachments.Cast<object>().Where(Missing).Select(DescribeAttachment).ToArray()},
                {"missingBodies",bodies.Cast<object>().Where(Missing).Select(DescribeBody).ToArray()},
                {"registryTopology",DescribeRegistryTopology(snapshot)},
                {"concurrentPhysicalPair55",DescribePhysicalPair(snapshot,55)},
                {"deliveryMaterialOwnership",DescribeDeliveryMaterialOwnership()},
                {"initialPlateTopology",DescribeInitialPlateTopology(snapshot,2)},
                {"story11PlateFactory",DescribeStory11PlateFactory()},
                {"attachments",attachments.Cast<object>().Select(DescribeAttachment).ToArray()},
                {"bodies",bodies.Cast<object>().Select(DescribeBody).ToArray()}};
        }

        private static object DescribeRegistryTopology(object snapshot)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var topologyField=snapshot.GetType().GetField("InitialAttachmentTopology",flags);
            var topology=topologyField==null?null:topologyField.GetValue(snapshot);
            var historicalField=topology==null?null:topology.GetType().GetField("registryEntries",flags);
            var historical=historicalField==null?null:historicalField.GetValue(topology) as EntitySerialisationEntry[];
            var registry=EntitySerialisationRegistry.m_EntitiesList;
            if(historical==null||registry==null||registry._items==null)return null;
            var current=registry._items.Take(registry.Count).ToArray();
            return new Dictionary<string,object>{
                {"historicalCount",historical.Length},{"currentCount",current.Length},
                {"historical",historical.Select(entry=>DescribeRegistryEntry(entry,
                    EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID))).ToArray()},
                {"currentExtras",current.Where(entry=>!historical.Any(saved=>ReferenceEquals(saved,entry)))
                    .Select(entry=>DescribeRegistryEntry(entry,historical.FirstOrDefault(saved=>
                        saved.m_Header.m_uEntityID==entry.m_Header.m_uEntityID))).ToArray()},
                {"missingHistorical",historical.Where(entry=>!current.Any(value=>ReferenceEquals(value,entry)))
                    .Select(entry=>DescribeRegistryEntry(entry,EntitySerialisationRegistry.GetEntry(
                        entry.m_Header.m_uEntityID))).ToArray()}};
        }

        private static object DescribeRegistryEntry(EntitySerialisationEntry entry,
            EntitySerialisationEntry sameIdEntry)
        {
            return new Dictionary<string,object>{
                {"entityId",entry==null?0:(int)entry.m_Header.m_uEntityID},
                {"sameEntry",ReferenceEquals(entry,sameIdEntry)},
                {"entryHash",entry==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(entry)},
                {"sameIdEntryHash",sameIdEntry==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(sameIdEntry)},
                {"object",DescribeUnity(entry==null?null:entry.m_GameObject)},
                {"sameIdObject",DescribeUnity(sameIdEntry==null?null:sameIdEntry.m_GameObject)}};
        }

        private static object DescribeInitialPlateTopology(object snapshot,int entityId)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var topologyField=snapshot.GetType().GetField("InitialAttachmentTopology",flags);
            var topology=topologyField==null?null:topologyField.GetValue(snapshot);
            var platesField=topology==null?null:topology.GetType().GetField("plates",flags);
            var plates=platesField==null?null:platesField.GetValue(topology) as Array;
            if(plates==null)return null;
            var rows=plates.Cast<object>().Where(value=>
            {
                var entry=Value(value,"Entry") as EntitySerialisationEntry;
                return entry!=null&&entry.m_Header.m_uEntityID==(uint)entityId;
            }).ToArray();
            return rows.Select(value=>new Dictionary<string,object>{
                {"entityId",entityId},{"components",TypeNames((Type[])Value(value,"Components"))},
                {"authoringComponents",TypeNames((Type[])Value(value,"AuthoringComponents"))},
                {"colliderTypes",TypeNames((Type[])Value(value,"ColliderTypes"))},
                {"step",DescribeUnity(Value(value,"Step") as UnityEngine.Object)},
                {"modelPrefab",DescribeUnity(Value(value,"ModelPrefab") as UnityEngine.Object)}}).ToArray();
        }

        private static object DescribePhysicalPair(object snapshot,int entityId)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var topologyField=snapshot.GetType().GetField("InitialAttachmentTopology",flags);
            var topology=topologyField==null?null:topologyField.GetValue(snapshot);
            var pairsField=topology==null?null:topology.GetType().GetField("physicalPairs",flags);
            var pairs=pairsField==null?null:pairsField.GetValue(topology) as Array;
            var historical=pairs==null?null:pairs.Cast<object>().SingleOrDefault(value=>
            {
                var entry=Value(value,"OwnerEntry") as EntitySerialisationEntry;
                return entry!=null&&entry.m_Header.m_uEntityID==(uint)entityId;
            });
            var ownerEntry=EntitySerialisationRegistry.GetEntry((uint)entityId);
            var owner=ownerEntry==null?null:ownerEntry.m_GameObject;
            var attachments=owner==null?new PhysicalAttachment[0]:owner.GetComponents<PhysicalAttachment>();
            var physical=attachments.Length==1?attachments[0]:null;
            var container=physical==null||physical.m_container==null?null:physical.m_container.gameObject;
            var containerEntry=container==null?null:EntitySerialisationRegistry.GetEntry(container);
            Type[] ownerComponents=owner==null?new Type[0]:owner.GetComponents<Component>()
                .Where(value=>value!=null).Select(value=>value.GetType()).ToArray();
            Type[] containerComponents=container==null?new Type[0]:container.GetComponents<Component>()
                .Where(value=>value!=null).Select(value=>value.GetType()).ToArray();
            Type[] ownerColliders=owner==null?new Type[0]:owner.GetComponentsInChildren<Collider>(true)
                .Where(value=>value!=null).Select(value=>value.GetType()).OrderBy(value=>value.FullName).ToArray();
            Type[] containerColliders=container==null?new Type[0]:container.GetComponentsInChildren<Collider>(true)
                .Where(value=>value!=null).Select(value=>value.GetType()).OrderBy(value=>value.FullName).ToArray();
            Type[] historicalOwnerComponents=historical==null?null:(Type[])Value(historical,"OwnerComponents");
            Type[] historicalContainerComponents=historical==null?null:(Type[])Value(historical,"ContainerComponents");
            Type[] historicalOwnerColliders=historical==null?null:(Type[])Value(historical,"OwnerColliderTypes");
            Type[] historicalContainerColliders=historical==null?null:(Type[])Value(historical,"ContainerColliderTypes");
            return new Dictionary<string,object>{{"entityId",entityId},{"historicalPresent",historical!=null},
                {"ownerEntry",DescribeRegistryEntry(ownerEntry,historical==null?null:Value(historical,"OwnerEntry") as EntitySerialisationEntry)},
                {"containerEntry",DescribeRegistryEntry(containerEntry,historical==null?null:Value(historical,"ContainerEntry") as EntitySerialisationEntry)},
                {"attachmentCount",attachments.Length},{"physicalContainer",DescribeUnity(container)},
                {"ownerComponents",TypeNames(ownerComponents)},{"historicalOwnerComponents",TypeNames(historicalOwnerComponents)},
                {"ownerComponentDifference",Difference(ownerComponents,historicalOwnerComponents)},
                {"containerComponents",TypeNames(containerComponents)},{"historicalContainerComponents",TypeNames(historicalContainerComponents)},
                {"containerComponentDifference",Difference(containerComponents,historicalContainerComponents)},
                {"ownerColliderTypes",TypeNames(ownerColliders)},{"historicalOwnerColliderTypes",TypeNames(historicalOwnerColliders)},
                {"ownerColliderDifference",Difference(ownerColliders,historicalOwnerColliders)},
                {"containerColliderTypes",TypeNames(containerColliders)},{"historicalContainerColliderTypes",TypeNames(historicalContainerColliders)},
                {"containerColliderDifference",Difference(containerColliders,historicalContainerColliders)}};
        }

        private static object DescribeDeliveryMaterialOwnership()
        {
            const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var type=AppDomain.CurrentDomain.GetAssemblies().Select(value=>
                value.GetType("SuperchargedPatch.Authoring.Modules.DeliveryFadeCheckpointModule",false))
                .SingleOrDefault(value=>value!=null);
            var activeField=type==null?null:type.GetField("active",flags);
            var module=activeField==null?null:activeField.GetValue(null);
            var sequencesField=type==null?null:type.GetField("sequences",flags);
            var sequences=module==null||sequencesField==null?null:sequencesField.GetValue(module) as IEnumerable;
            if(sequences==null)return null;
            return sequences.Cast<object>().Select(sequence=>
            {
                var owned=Value(sequence,"OwnedMaterials") as IEnumerable;
                var iterator=Value(sequence,"Iterator");
                var iteratorType=iterator==null?null:iterator.GetType();
                var renderersField=iteratorType==null?null:iteratorType.GetField("<allRenderers>__0",flags);
                var renderers=renderersField==null?null:renderersField.GetValue(iterator) as MeshRenderer[];
                return new Dictionary<string,object>{{"entityId",Value(sequence,"EntityId")},
                    {"materialOwnershipComplete",Value(sequence,"MaterialOwnershipComplete")},
                    {"materialsBeforeMoveNextNull",Value(sequence,"MaterialsBeforeMoveNext")==null},
                    {"ownedMaterials",owned==null?new object[0]:owned.Cast<Material>().Select(DescribeMaterial).ToArray()},
                    {"rendererMaterials",renderers==null?new object[0]:renderers.Select(renderer=>new Dictionary<string,object>{
                        {"renderer",DescribeUnity(renderer)},{"materials",renderer==null?new object[0]:renderer.sharedMaterials.Select(DescribeMaterial).ToArray()}}).ToArray()}};
            }).ToArray();
        }

        private static object DescribeMaterial(Material material)
        {
            bool alive=!ReferenceEquals(material,null)&&material!=null;
            return new Dictionary<string,object>{{"material",DescribeUnity(material)},
                {"shader",DescribeUnity(alive?material.shader:null)},
                {"hasAlpha",alive&&material.HasProperty("_Alpha")},
                {"alpha",alive&&material.HasProperty("_Alpha")?(object)material.GetFloat("_Alpha"):null},
                {"hasMode",alive&&material.HasProperty("_Mode")},
                {"mode",alive&&material.HasProperty("_Mode")?(object)material.GetFloat("_Mode"):null}};
        }

        private static string Difference(Type[] actual,Type[] expected)
        {
            if(actual==null||expected==null)return "one signature is null";
            int common=Math.Min(actual.Length,expected.Length);
            for(int i=0;i<common;i++)if(actual[i]!=expected[i])
                return "index "+i+" expected "+expected[i].FullName+" but found "+actual[i].FullName
                    +" (expectedCount="+expected.Length+", actualCount="+actual.Length+")";
            return actual.Length==expected.Length?null:"matching prefix length "+common+" but expectedCount="
                +expected.Length+" and actualCount="+actual.Length;
        }

        private static object DescribeStory11PlateFactory()
        {
            var root=EntitySerialisationRegistry.GetEntry(34u);
            var obj=root==null?null:root.m_GameObject;
            var station=obj==null?null:obj.GetComponent<PlateReturnStation>();
            var stack=station==null?null:station.m_stackPrefab;
            var clean=stack==null?null:stack.GetComponent<CleanPlateStack>();
            var plate=clean==null?null:clean.m_platePrefab;
            return new Dictionary<string,object>{{"root",DescribeUnity(obj)},{"stack",DescribeUnity(stack)},
                {"stackComponents",stack==null?new string[0]:TypeNames(stack.GetComponents<Component>().Where(value=>value!=null).Select(value=>value.GetType()).ToArray())},
                {"stackColliderTypes",stack==null?new string[0]:TypeNames(stack.GetComponentsInChildren<Collider>(true).Where(value=>value!=null).Select(value=>value.GetType()).ToArray())},
                {"plate",DescribeUnity(plate)},
                {"plateComponents",plate==null?new string[0]:TypeNames(plate.GetComponents<Component>().Where(value=>value!=null).Select(value=>value.GetType()).ToArray())},
                {"plateColliderTypes",plate==null?new string[0]:TypeNames(plate.GetComponentsInChildren<Collider>(true).Where(value=>value!=null).Select(value=>value.GetType()).ToArray())}};
        }

        private static string[] TypeNames(Type[] values)
        {return values==null?new string[0]:values.Select(value=>value==null?null:value.FullName).ToArray();}

        private static Array ArrayField(object owner,string name,BindingFlags flags)
        {
            var field=owner.GetType().GetField(name,flags);var value=field==null?null:field.GetValue(owner) as Array;
            if(value==null)throw new InvalidOperationException("Checkpoint field "+name+" is unavailable.");
            return value;
        }

        private static bool Missing(object row)
        {
            int id=Convert.ToInt32(Value(row,"EntityId"));
            return EntitySerialisationRegistry.GetEntry((uint)id)==null;
        }

        private static object DescribeAttachment(object row)
        {
            int id=Convert.ToInt32(Value(row,"EntityId"));
            var parentEntry=Value(row,"ParentEntry") as EntitySerialisationEntry;
            var parent=Value(row,"Parent") as Transform;
            var transform=Value(row,"Transform") as Transform;
            var container=Value(row,"Container") as Rigidbody;
            var containerEntry=container==null?null:EntitySerialisationRegistry.GetEntry(container.gameObject);
            var prediction=Value(row,"Prediction");
            var predictionTransform=Value(row,"PredictionTransform") as Transform;
            var cachedParent=Value(row,"CachedClientParent");
            var cachedComponent=cachedParent as Component;
            var cachedObject=cachedComponent==null?null:cachedComponent.gameObject;
            int cachedIndex=cachedObject==null?-1:Array.FindIndex(cachedObject.GetComponents<Component>(),
                value=>ReferenceEquals(value,cachedComponent));
            return new Dictionary<string,object>{{"entityId",id},{"missing",Missing(row)},
                {"attached",Value(row,"Attached")},{"unsupported",Value(row,"Unsupported")},
                {"parentEntryId",parentEntry==null?0:(int)parentEntry.m_Header.m_uEntityID},
                {"parent",DescribeUnity(parent)},{"transform",DescribeUnity(transform)},
                {"transformParent",DescribeUnity(transform==null?null:transform.parent)},
                {"container",DescribeUnity(container)},{"containerEntryId",containerEntry==null?0:(int)containerEntry.m_Header.m_uEntityID},
                {"cachedClientParentType",cachedParent==null?null:cachedParent.GetType().FullName},
                {"cachedClientParent",DescribeUnity(cachedComponent)},
                {"cachedClientParentObject",DescribeUnity(cachedObject)},{"cachedClientParentComponentIndex",cachedIndex},
                {"prediction",prediction==null?null:prediction.GetType().FullName},
                {"predictionTransform",DescribeUnity(predictionTransform)},
                {"predictionQueueCount",Value(row,"PredictionQueueCount")},
                {"serverPredictionMode",Value(row,"ServerPredictionMode")},
                {"clientPredictionMode",Value(row,"ClientPredictionMode")},
                {"localPosition",Value(row,"LocalPosition")},{"worldPosition",Value(row,"WorldPosition")}};
        }

        private static object DescribeBody(object row)
        {
            int id=Convert.ToInt32(Value(row,"EntityId"));
            var body=Value(row,"Body") as Rigidbody;
            var transform=Value(row,"Transform") as Transform;
            var colliders=(Array)Value(row,"Colliders");
            return new Dictionary<string,object>{{"entityId",id},{"missing",Missing(row)},
                {"body",DescribeUnity(body)},{"transform",DescribeUnity(transform)},
                {"parent",DescribeUnity(Value(row,"Parent") as Transform)},
                {"colliderCount",colliders.Length},{"colliders",colliders.Cast<object>().Select(DescribeShape).ToArray()},
                {"bodyPosition",Value(row,"BodyPosition")},{"bodyRotation",Value(row,"BodyRotation")},
                {"rawIsKinematic",Value(row,"RawIsKinematic")},{"rawUseGravity",Value(row,"RawUseGravity")}};
        }

        private static object DescribeShape(object row)
        {
            return new Dictionary<string,object>{{"kind",Value(row,"Kind")},{"enabled",Value(row,"Enabled")},
                {"trigger",Value(row,"Trigger")},{"layer",Value(row,"Layer")},
                {"componentIndex",Value(row,"ComponentIndex")},
                {"collider",DescribeUnity(Value(row,"Collider") as UnityEngine.Object)},
                {"transform",DescribeUnity(Value(row,"Transform") as UnityEngine.Object)},
                {"parent",DescribeUnity(Value(row,"Parent") as UnityEngine.Object)},
                {"ancestorNames",Value(row,"AncestorNames")}};
        }

        private static object Value(object owner,string name)
        {
            const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
            var type=owner.GetType();var property=type.GetProperty(name,flags);
            if(property!=null)return property.GetValue(owner,null);
            var field=type.GetField(name,flags);
            if(field==null)throw new InvalidOperationException(type.FullName+" lacks "+name+".");
            return field.GetValue(owner);
        }

        private static object DescribeUnity(UnityEngine.Object value)
        {
            if(ReferenceEquals(value,null))return null;
            if(value==null)return new Dictionary<string,object>{{"destroyed",true},{"instanceId",value.GetInstanceID()}};
            return new Dictionary<string,object>{{"destroyed",false},{"instanceId",value.GetInstanceID()},
                {"name",value.name},{"type",value.GetType().FullName}};
        }

        public void Dispose(){disposed=true;}
    }
}
