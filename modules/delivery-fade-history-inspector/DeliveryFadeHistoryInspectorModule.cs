using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SuperchargedPatch.Authoring;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only diagnostic sidecar for an already-active delivery checkpoint.
    // It deliberately owns no hooks and retains no references after Invoke.
    public sealed class DeliveryFadeHistoryInspectorModule : IAuthoringModule
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private bool disposed;

        public string Name { get { return "delivery-fade-history-inspector-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException(Name);
            if(operation!="inspect")throw new ArgumentException("Use inspect.");
            if(args==null||!args.ContainsKey("frame")||args.Count!=1)
                throw new ArgumentException("inspect requires exactly one frame argument.");
            int frame=Convert.ToInt32(args["frame"]);

            object module=FindActiveCheckpoint();
            Type moduleType=module.GetType();
            var history=Read(module,"history") as IDictionary;
            if(history==null||!history.Contains(frame))
                return Map("available",false,"frame",frame,"historyFrames",DictionaryKeys(history));

            object state=history[frame];
            var encode=moduleType.GetMethod("Encode",Any);
            var observe=moduleType.GetMethod("ObserveSequence",Any);
            if(encode==null||observe==null)throw new MissingMethodException(moduleType.FullName,"Encode/ObserveSequence");
            var currentSequences=new List<object>();
            foreach(object sequence in (IEnumerable)Read(module,"sequences"))
            {
                var observed=(IDictionary<string,object>)observe.Invoke(module,new[]{sequence});
                object iterator=Read(sequence,"Iterator");
                var pfx=Read(iterator,"<pfx>__1") as GameObject;
                observed["pfxDetail"]=EncodePfx(pfx);
                currentSequences.Add(observed);
            }

            var plates=new List<object>();
            var plateArray=Read(state,"Plates") as Array;
            if(plateArray!=null)foreach(object plate in plateArray)plates.Add(EncodePlate(plate));

            return Map("available",true,"frame",frame,"historyFrames",DictionaryKeys(history),
                "state",encode.Invoke(module,new[]{state}),"plates",plates.ToArray(),
                "currentSequences",currentSequences.ToArray(),
                "scope","Read-only reflection of the active delivery checkpoint's immutable historical sidecar; no game mutation or retained Unity references.");
        }

        private static object FindActiveCheckpoint()
        {
            var values=new List<object>();
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type=assembly.GetType("SuperchargedPatch.Authoring.Modules.DeliveryFadeCheckpointModule",false);
                if(type==null)continue;
                var field=type.GetField("active",Any);
                object value=field==null?null:field.GetValue(null);
                if(value!=null)values.Add(value);
            }
            if(values.Count!=1)throw new InvalidOperationException("Expected one active delivery checkpoint, found "+values.Count+".");
            return values[0];
        }

        private static object EncodePlate(object plate)
        {
            var colliders=new List<object>();
            var colliderArray=Read(plate,"Colliders") as Array;
            if(colliderArray!=null)foreach(object value in colliderArray)colliders.Add(Map(
                "instanceId",Read(value,"InstanceId"),"hierarchyKey",Read(value,"HierarchyKey"),
                "type",Read(value,"TypeName"),"enabled",Read(value,"Enabled"),"trigger",Read(value,"Trigger"),
                "activeSelf",Read(value,"ActiveSelf"),"activeInHierarchy",Read(value,"ActiveInHierarchy")));

            var renderers=new List<object>();
            var rendererArray=Read(plate,"Renderers") as Array;
            if(rendererArray!=null)foreach(object value in rendererArray)renderers.Add(Map(
                "instanceId",Read(value,"InstanceId"),"hierarchyKey",Read(value,"HierarchyKey"),
                "inPresentationContainer",Read(value,"InPresentationContainer"),"presentationKey",Read(value,"PresentationKey"),
                "sharedMeshId",Read(value,"SharedMeshId"),"localPosition",Vector((Vector3)Read(value,"LocalPosition")),
                "localRotation",Rotation((Quaternion)Read(value,"LocalRotation")),"localScale",Vector((Vector3)Read(value,"LocalScale")),
                "activeSelf",Read(value,"ActiveSelf"),"activeInHierarchy",Read(value,"ActiveInHierarchy"),
                "layer",Read(value,"Layer"),"enabled",Read(value,"Enabled"),
                "materialIds",Read(value,"MaterialIds"),"shaderIds",Read(value,"ShaderIds"),
                "hasMode",Read(value,"HasMode"),"mode",Read(value,"Mode"),
                "hasAlpha",Read(value,"HasAlpha"),"alpha",Read(value,"Alpha")));

            return Map("entityId",Read(plate,"EntityId"),"objectId",Read(plate,"ObjectId"),
                "componentId",Read(plate,"ComponentId"),"parentId",Read(plate,"ParentId"),
                "name",Read(plate,"Name"),"layer",Read(plate,"Layer"),
                "activeSelf",Read(plate,"ActiveSelf"),"activeInHierarchy",Read(plate,"ActiveInHierarchy"),
                "localPosition",Vector((Vector3)Read(plate,"LocalPosition")),
                "localRotation",Rotation((Quaternion)Read(plate,"LocalRotation")),
                "localScale",Vector((Vector3)Read(plate,"LocalScale")),
                "presentationOwnerId",Read(plate,"PresentationOwnerId"),
                "presentationContainerId",Read(plate,"PresentationContainerId"),
                "presentationContainerKey",Read(plate,"PresentationContainerKey"),
                "presentationCompositionFingerprint",Read(plate,"PresentationCompositionFingerprint"),
                "presentationContainerPhysicsFree",Read(plate,"PresentationContainerPhysicsFree"),
                "componentTypes",Read(plate,"ComponentTypes"),
                "serverSynchroniserTypes",Read(plate,"ServerSynchroniserTypes"),
                "clientSynchroniserTypes",Read(plate,"ClientSynchroniserTypes"),
                "colliders",colliders.ToArray(),"renderers",renderers.ToArray());
        }

        private static object EncodePfx(GameObject root)
        {
            if(root==null)return null;
            bool alive=root!=null;
            if(!alive)return Map("alive",false);
            var transforms=root.GetComponentsInChildren<Transform>(true).Select(value=>Map(
                "path",TransformKey(root.transform,value),"instanceId",value.GetInstanceID(),
                "activeSelf",value.gameObject.activeSelf,"activeInHierarchy",value.gameObject.activeInHierarchy,
                "localPosition",Vector(value.localPosition),"localRotation",Rotation(value.localRotation),
                "localScale",Vector(value.localScale),"componentTypes",value.GetComponents<Component>()
                    .Where(component=>component!=null).Select(component=>component.GetType().FullName).ToArray())).ToArray();
            var particles=root.GetComponentsInChildren<ParticleSystem>(true).Select(value=>Map(
                "path",TransformKey(root.transform,value.transform),"instanceId",value.GetInstanceID(),
                "time",value.time,"randomSeed",value.randomSeed,"useAutoRandomSeed",value.useAutoRandomSeed,
                "isPlaying",value.isPlaying,"isPaused",value.isPaused,"isStopped",value.isStopped,
                "particleCount",value.particleCount,"duration",value.main.duration,"loop",value.main.loop,
                "playOnAwake",value.main.playOnAwake,"simulationSpace",value.main.simulationSpace.ToString())).ToArray();
            return Map("alive",true,"instanceId",root.GetInstanceID(),"name",root.name,
                "activeSelf",root.activeSelf,"activeInHierarchy",root.activeInHierarchy,
                "position",Vector(root.transform.position),"rotation",Rotation(root.transform.rotation),
                "transforms",transforms,"particleSystems",particles);
        }

        private static string TransformKey(Transform root,Transform value)
        {
            var parts=new List<string>();var item=value;
            while(!ReferenceEquals(item,root))
            {
                if(item==null)throw new InvalidOperationException("PFX transform is outside its root.");
                parts.Add(item.name+"#"+item.GetSiblingIndex());item=item.parent;
            }
            parts.Reverse();return String.Join("/",parts.ToArray());
        }

        private static object Read(object value,string name)
        {
            if(value==null)return null;
            var field=value.GetType().GetField(name,Any);
            if(field==null)throw new MissingFieldException(value.GetType().FullName,name);
            return field.GetValue(value);
        }

        private static object[] DictionaryKeys(IDictionary value)
        {
            if(value==null)return new object[0];
            var keys=new List<object>();foreach(object key in value.Keys)keys.Add(key);return keys.ToArray();
        }

        private static float[] Vector(Vector3 value){return new[]{value.x,value.y,value.z};}
        private static float[] Rotation(Quaternion value){return new[]{value.x,value.y,value.z,value.w};}
        private static Dictionary<string,object> Map(params object[] values)
        {
            var result=new Dictionary<string,object>();
            for(int i=0;i<values.Length;i+=2)result[(string)values[i]]=values[i+1];
            return result;
        }

        public void Dispose(){disposed=true;}
    }
}
