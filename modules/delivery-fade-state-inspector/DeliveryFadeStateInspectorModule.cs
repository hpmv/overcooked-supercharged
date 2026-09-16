using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    public sealed class DeliveryFadeStateInspectorModule : IAuthoringModule
    {
        private const BindingFlags Any=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private bool disposed;
        public string Name { get { return "delivery-fade-state-inspector-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException(Name);
            if(operation!="status"||args==null||args.Count!=0)throw new ArgumentException("Use status with no arguments.");
            var assembly=AppDomain.CurrentDomain.GetAssemblies().Single(value=>
                value.GetName().Name=="DeliveryFadeCheckpoint.r8b-attached-cosmetic-resume-restore");
            var moduleType=assembly.GetType("SuperchargedPatch.Authoring.Modules.DeliveryFadeCheckpointModule",true);
            var module=Field(moduleType,"active").GetValue(null);
            var target=Field(moduleType,"resumePresentationTarget").GetValue(module);
            if(target==null)return Map("target",null);
            var plate=(ClientPlate)Read(target,"Plate");var root=plate.gameObject.transform;
            var owner=(ClientAttachedOrderCosmeticDecisions)Read(target,"PresentationOwner");
            var ownerContainerField=FindField(typeof(ClientAttachedOrderCosmeticDecisions),"m_container");
            var currentContainer=(GameObject)ownerContainerField.GetValue(owner);
            var targetContainer=(GameObject)Read(target,"PresentationContainer");
            var saved=((Array)Read(target,"Renderers")).Cast<object>().Select(EncodeSavedRenderer).ToArray();
            var current=ComponentsRecursive<MeshRenderer>(root).Select(value=>EncodeCurrentRenderer(root,currentContainer,value)).ToArray();
            return Map("targetEntityId",Read(target,"EntityId"),"plateId",plate.GetInstanceID(),
                "ownerId",owner.GetInstanceID(),"savedOwnerId",Read(target,"PresentationOwnerId"),
                "targetContainer",EncodeObject(targetContainer),"savedContainerId",Read(target,"PresentationContainerId"),
                "savedContainerKey",Read(target,"PresentationContainerKey"),
                "savedContainerPhysicsFree",Read(target,"PresentationContainerPhysicsFree"),
                "currentContainer",EncodeObject(currentContainer),
                "currentContainerKey",Alive(currentContainer)?TransformKey(root,currentContainer.transform):null,
                "currentForbidden",Alive(currentContainer)?Forbidden(currentContainer):null,
                "savedRenderers",saved,"currentRenderers",current);
        }

        private static object EncodeSavedRenderer(object value)
        {
            var renderer=(MeshRenderer)Read(value,"Renderer");
            return Map("instanceId",Read(value,"InstanceId"),"alive",Alive(renderer),
                "hierarchyKey",Read(value,"HierarchyKey"),"inContainer",Read(value,"InPresentationContainer"),
                "presentationKey",Read(value,"PresentationKey"),"sharedMeshId",Read(value,"SharedMeshId"),
                "localPosition",Read(value,"LocalPosition"),"localRotation",Read(value,"LocalRotation"),
                "localScale",Read(value,"LocalScale"),"activeSelf",Read(value,"ActiveSelf"),
                "activeInHierarchy",Read(value,"ActiveInHierarchy"),"layer",Read(value,"Layer"),
                "enabled",Read(value,"Enabled"),"materialIds",Read(value,"MaterialIds"),
                "shaderIds",Read(value,"ShaderIds"),"hasMode",Read(value,"HasMode"),
                "mode",Read(value,"Mode"),"hasAlpha",Read(value,"HasAlpha"),"alpha",Read(value,"Alpha"));
        }

        private static object EncodeCurrentRenderer(Transform root,GameObject container,MeshRenderer value)
        {
            var materials=value.sharedMaterials;var filter=value.GetComponent<MeshFilter>();
            bool inside=Alive(container)&&Descendant(value.transform,container.transform);
            return Map("instanceId",value.GetInstanceID(),"hierarchyKey",RendererKey(root,value),
                "inContainer",inside,"presentationKey",inside?RendererKey(container.transform,value):null,
                "sharedMeshId",filter==null||filter.sharedMesh==null?0:filter.sharedMesh.GetInstanceID(),
                "localPosition",value.transform.localPosition,"localRotation",value.transform.localRotation,
                "localScale",value.transform.localScale,"activeSelf",value.gameObject.activeSelf,
                "activeInHierarchy",value.gameObject.activeInHierarchy,"layer",value.gameObject.layer,
                "enabled",value.enabled,"materialIds",materials.Select(Id).ToArray(),
                "shaderIds",materials.Select(item=>item==null||item.shader==null?0:item.shader.GetInstanceID()).ToArray(),
                "hasMode",materials.Select(item=>item!=null&&item.HasProperty("_Mode")).ToArray(),
                "mode",materials.Select(item=>item!=null&&item.HasProperty("_Mode")?item.GetFloat("_Mode"):Single.NaN).ToArray(),
                "hasAlpha",materials.Select(item=>item!=null&&item.HasProperty("_Alpha")).ToArray(),
                "alpha",materials.Select(item=>item!=null&&item.HasProperty("_Alpha")?item.GetFloat("_Alpha"):Single.NaN).ToArray());
        }

        private static object Forbidden(GameObject root)
        {
            return Map("colliders",ComponentsRecursive<Collider>(root.transform).Select(value=>value.GetInstanceID()).ToArray(),
                "rigidbodies",ComponentsRecursive<Rigidbody>(root.transform).Select(value=>value.GetInstanceID()).ToArray(),
                "animators",ComponentsRecursive<Animator>(root.transform).Select(value=>value.GetInstanceID()).ToArray(),
                "serverWorld",ComponentsRecursive<ServerWorldObjectSynchroniser>(root.transform).Select(value=>value.GetInstanceID()).ToArray(),
                "clientWorld",ComponentsRecursive<ClientWorldObjectSynchroniser>(root.transform).Select(value=>value.GetInstanceID()).ToArray());
        }

        private static object EncodeObject(GameObject value)
        {
            bool alive=Alive(value);
            return Map("referenceNull",ReferenceEquals(value,null),"alive",alive,
                "instanceId",alive?value.GetInstanceID():0,"name",alive?value.name:null);
        }

        private static T[] ComponentsRecursive<T>(Transform root) where T:Component
        {
            var values=new List<T>();var pending=new Stack<Transform>();pending.Push(root);
            while(pending.Count!=0){var current=pending.Pop();values.AddRange(current.GetComponents<T>());
                for(int i=current.childCount-1;i>=0;i--)pending.Push(current.GetChild(i));}
            return values.ToArray();
        }

        private static string RendererKey(Transform root,MeshRenderer renderer)
        {
            var siblings=renderer.transform.GetComponents<MeshRenderer>();
            int ordinal=Array.FindIndex(siblings,value=>ReferenceEquals(value,renderer));
            return TransformKey(root,renderer.transform)+"@"+ordinal;
        }

        private static string TransformKey(Transform root,Transform value)
        {
            var parts=new List<string>();var item=value;
            while(!ReferenceEquals(item,root)){if(item==null)throw new InvalidOperationException("Outside root.");
                parts.Add(item.name+"#"+item.GetSiblingIndex());item=item.parent;}
            parts.Reverse();return String.Join("/",parts.ToArray());
        }

        private static bool Descendant(Transform value,Transform root)
        { for(var item=value;item!=null;item=item.parent)if(ReferenceEquals(item,root))return true;return false; }
        private static int Id(Material value){return value==null?0:value.GetInstanceID();}
        private static bool Alive(UnityEngine.Object value){return !ReferenceEquals(value,null)&&value!=null;}
        private static object Read(object value,string name){return Field(value.GetType(),name).GetValue(value);}
        private static FieldInfo Field(Type type,string name)
        { var value=type.GetField(name,Any);if(value==null)throw new MissingFieldException(type.FullName,name);return value; }
        private static FieldInfo FindField(Type type,string name)
        { for(var item=type;item!=null;item=item.BaseType){var value=item.GetField(name,Any|BindingFlags.DeclaredOnly);if(value!=null)return value;}throw new MissingFieldException(type.FullName,name); }
        private static Dictionary<string,object> Map(params object[] values)
        { var result=new Dictionary<string,object>();for(int i=0;i<values.Length;i+=2)result.Add((string)values[i],values[i+1]);return result; }
        public void Dispose(){disposed=true;}
    }
}
