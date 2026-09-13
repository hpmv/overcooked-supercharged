using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only inspection of one registered plate's complete Transform and
    // MeshRenderer hierarchy. No Unity references are retained after Invoke.
    public sealed class DeliveryRendererInspectionModule : IAuthoringModule
    {
        private bool disposed;
        public string Name { get { return "delivery-renderer-hierarchy-inspection-v3-materials-pose"; } }
        public int ApiVersion { get { return 1; } }
        public void Dispose() { disposed=true; }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("DeliveryRendererInspectionModule");
            if(operation!="inspect")throw new ArgumentException("Use inspect.");
            int entityId=args!=null&&args.ContainsKey("entityId")?Convert.ToInt32(args["entityId"]):2;
            int frame=args!=null&&args.ContainsKey("frame")?Convert.ToInt32(args["frame"]):444;
            var entry=EntitySerialisationRegistry.GetEntry((uint)entityId);
            var root=entry==null?null:entry.m_GameObject;
            if(root==null)throw new InvalidOperationException("Requested registered plate is absent.");
            var owner=root.GetComponent<ClientAttachedOrderCosmeticDecisions>();
            var containerField=AccessTools.Field(typeof(ClientAttachedOrderCosmeticDecisions),"m_container");
            var container=owner==null||containerField==null?null:containerField.GetValue(owner) as GameObject;
            var transforms=root.GetComponentsInChildren<Transform>(true).Select(value=>Map(
                "key",TransformKey(root.transform,value),"name",value.name,"instanceId",value.GetInstanceID(),
                "parentId",value.parent==null?0:value.parent.GetInstanceID(),"siblingIndex",value.GetSiblingIndex(),
                "activeSelf",value.gameObject.activeSelf,"activeInHierarchy",value.gameObject.activeInHierarchy,
                "componentTypes",value.GetComponents<Component>().Where(component=>component!=null)
                    .Select(component=>component.GetType().FullName).ToArray())).ToArray();
            var renderers=root.GetComponentsInChildren<MeshRenderer>(true).Select(value=>Map(
                "key",RendererKey(root.transform,value),"instanceId",value.GetInstanceID(),
                "meshId",value.GetComponent<MeshFilter>()==null||value.GetComponent<MeshFilter>().sharedMesh==null
                    ?0:value.GetComponent<MeshFilter>().sharedMesh.GetInstanceID(),
                "layer",value.gameObject.layer,"enabled",value.enabled,
                "activeSelf",value.gameObject.activeSelf,"activeInHierarchy",value.gameObject.activeInHierarchy,
                "localPosition",Vector(value.transform.localPosition),"localRotation",Quaternion(value.transform.localRotation),
                "localScale",Vector(value.transform.localScale),
                "materialIds",value.sharedMaterials.Select(material=>material==null?0:material.GetInstanceID()).ToArray(),
                "shaderIds",value.sharedMaterials.Select(material=>material==null||material.shader==null?0:material.shader.GetInstanceID()).ToArray(),
                "hasMode",value.sharedMaterials.Select(material=>material!=null&&material.HasProperty("_Mode")).ToArray(),
                "mode",value.sharedMaterials.Select(material=>material!=null&&material.HasProperty("_Mode")?(object)material.GetFloat("_Mode"):null).ToArray(),
                "hasAlpha",value.sharedMaterials.Select(material=>material!=null&&material.HasProperty("_Alpha")).ToArray(),
                "alpha",value.sharedMaterials.Select(material=>material!=null&&material.HasProperty("_Alpha")?(object)material.GetFloat("_Alpha"):null).ToArray())).ToArray();
            return Map("ok",true,"entityId",entityId,"root",root.name,"rootId",root.GetInstanceID(),
                "parentId",root.transform.parent==null?0:root.transform.parent.GetInstanceID(),
                "presentationOwnerId",owner==null?0:owner.GetInstanceID(),
                "presentationContainerId",container==null?0:container.GetInstanceID(),
                "presentationContainerKey",container==null?null:TransformKey(root.transform,container.transform),
                "transforms",transforms,"renderers",renderers,"historical",Historical(frame,entityId),
                "scope","Read-only registered plate hierarchy observation after failed restore; no mutation or retained Unity references.");
        }

        private static object Historical(int frame,int entityId)
        {
            var candidates=new List<object>();
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var type=assembly.GetType("SuperchargedPatch.Authoring.Modules.DeliveryFadeCheckpointModule",false);
                if(type==null)continue;
                var activeField=type.GetField("active",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
                var value=activeField==null?null:activeField.GetValue(null);
                if(value!=null)candidates.Add(value);
            }
            if(candidates.Count!=1)return Map("available",false,"activeModuleCount",candidates.Count);
            var module=candidates[0];var moduleType=module.GetType();
            var history=Value(module,"history") as IDictionary;
            if(history==null||!history.Contains(frame))return Map("available",false,"frame",frame);
            var state=history[frame];var plates=Value(state,"Plates") as Array;
            if(plates==null)return Map("available",false,"frame",frame,"reason","plates-absent");
            object target=null;
            foreach(var plate in plates)
                if(plate!=null&&Convert.ToInt32(Value(plate,"EntityId"))==entityId){target=plate;break;}
            if(target==null)return Map("available",false,"frame",frame,"entityId",entityId);
            var rendererValues=Value(target,"Renderers") as Array;var renderers=new List<object>();
            if(rendererValues!=null)foreach(var renderer in rendererValues)renderers.Add(Map(
                "hierarchyKey",Value(renderer,"HierarchyKey"),"presentationKey",Value(renderer,"PresentationKey"),
                "inPresentationContainer",Value(renderer,"InPresentationContainer"),"instanceId",Value(renderer,"InstanceId"),
                "sharedMeshId",Value(renderer,"SharedMeshId"),"layer",Value(renderer,"Layer"),
                "enabled",Value(renderer,"Enabled"),"activeSelf",Value(renderer,"ActiveSelf"),
                "activeInHierarchy",Value(renderer,"ActiveInHierarchy"),
                "localPosition",Vector((Vector3)Value(renderer,"LocalPosition")),
                "localRotation",Quaternion((UnityEngine.Quaternion)Value(renderer,"LocalRotation")),
                "localScale",Vector((Vector3)Value(renderer,"LocalScale")),
                "materialIds",Value(renderer,"MaterialIds"),"shaderIds",Value(renderer,"ShaderIds"),
                "hasMode",Value(renderer,"HasMode"),"mode",Value(renderer,"Mode"),
                "hasAlpha",Value(renderer,"HasAlpha"),"alpha",Value(renderer,"Alpha")));
            return Map("available",true,"frame",frame,"entityId",entityId,
                "presentationContainerKey",Value(target,"PresentationContainerKey"),"renderers",renderers.ToArray());
        }

        private static object Value(object instance,string name)
        {
            if(instance==null)return null;
            var field=instance.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
            if(field==null)throw new InvalidOperationException("Delivery sidecar field is absent: "+name);
            return field.GetValue(instance);
        }

        private static string TransformKey(Transform root,Transform value)
        {
            var parts=new List<string>();var item=value;
            while(!ReferenceEquals(item,root))
            {
                if(item==null)throw new InvalidOperationException("Transform is outside requested root.");
                parts.Add(item.name+"#"+item.GetSiblingIndex());item=item.parent;
            }
            parts.Reverse();return String.Join("/",parts.ToArray());
        }
        private static string RendererKey(Transform root,MeshRenderer renderer)
        {
            var siblings=renderer.transform.GetComponents<MeshRenderer>();
            int ordinal=Array.FindIndex(siblings,value=>ReferenceEquals(value,renderer));
            return TransformKey(root,renderer.transform)+"@"+ordinal;
        }
        private static float[] Vector(Vector3 value){return new[]{value.x,value.y,value.z};}
        private static float[] Quaternion(UnityEngine.Quaternion value){return new[]{value.x,value.y,value.z,value.w};}
        private static Dictionary<string,object> Map(params object[] values)
        {
            var result=new Dictionary<string,object>();
            for(int i=0;i<values.Length;i+=2)result[(string)values[i]]=values[i+1];
            return result;
        }
    }
}
