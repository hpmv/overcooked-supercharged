// Controlled native API/registry failure fixtures; no Unity process or physics.
using Team17.Online.Multiplayer.Messaging;
namespace HarmonyLib {
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type,string method,Type[] args) {} }
    public static class AccessTools { public static System.Reflection.FieldInfo Field(Type type,string name)=>type.GetField(name,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Instance); }
}
namespace UnityEngine {
    public class Object { public static ServerKitchenFlowControllerBase Flow; public static T FindObjectOfType<T>() => (T)(object)Flow; }
    public class Component : Object { public GameObject gameObject; }
    public class MonoBehaviour : Component {}
    public class Collider : Component {}
    public class Rigidbody : Component {}
    public class Transform : Component {}
    public struct Vector3 {}
    public struct Quaternion {}
    public sealed class GameObject : Object {
        public string name; public bool active=true; public List<Component> Components=new(); public Transform transform;
        public GameObject(string name="object") { this.name=name;transform=new Transform{gameObject=this};Components.Add(transform); }
        public T AddComponent<T>() where T:Component,new() { var value=new T {gameObject=this}; Components.Add(value); return value; }
        public Component GetComponent(Type type)=>Components.FirstOrDefault(type.IsInstanceOfType);
        public T GetComponent<T>() where T:Component => (T)GetComponent(typeof(T));
        public T[] GetComponents<T>() where T:Component => Components.OfType<T>().ToArray();
        public T[] GetComponentsInChildren<T>() where T:Component => GetComponents<T>();
    }
}
public sealed class ServerKitchenFlowControllerBase : UnityEngine.Component {}
public sealed class PhysicalAttachment : UnityEngine.Component { public UnityEngine.Rigidbody m_container; }
public sealed class ServerPhysicalAttachment : UnityEngine.Component { public void ManualEnable() {} }
public sealed class ServerWorkableItem : UnityEngine.Component {}
public sealed class ServerThrowableItem : UnityEngine.Component {}
public sealed class ServerPhysicsObjectSynchroniser : UnityEngine.Component {
    public sealed class SerialisationEntryTransformPair {
        public EntitySerialisationEntry m_Entry;
        public UnityEngine.Transform m_Transform;
    }
    public static System.Collections.IList ms_ServerPhysicsObjectSytnchroniserTransforms=new List<SerialisationEntryTransformPair>();
    public static void Add(EntitySerialisationEntry entry,UnityEngine.GameObject obj) {
        obj.AddComponent<ServerPhysicsObjectSynchroniser>();
        ms_ServerPhysicsObjectSytnchroniserTransforms.Add(new SerialisationEntryTransformPair{m_Entry=entry,m_Transform=obj.transform});
    }
    public static void RemoveForTransform(UnityEngine.Transform transform) {
        for(int i=0;i<ms_ServerPhysicsObjectSytnchroniserTransforms.Count;i++)
            if(ReferenceEquals(((SerialisationEntryTransformPair)ms_ServerPhysicsObjectSytnchroniserTransforms[i]).m_Transform,transform))
            {ms_ServerPhysicsObjectSytnchroniserTransforms.RemoveAt(i);return;}
    }
}
namespace Team17.Online.Multiplayer.Messaging {
    public sealed class FastList<T> { public List<T> _items=new(); public int Count=>_items.Count; }
    public sealed class EntityMessageHeader { public uint m_uEntityID; }
    public sealed class EntitySerialisationEntry { public EntityMessageHeader m_Header=new(); public UnityEngine.GameObject m_GameObject; }
    public static class EntitySerialisationRegistry {
        public static FastList<EntitySerialisationEntry> m_EntitiesList=new(); public static uint Next=100;
        public static EntitySerialisationEntry GetEntry(uint id)=>m_EntitiesList._items.FirstOrDefault(e=>e.m_Header.m_uEntityID==id);
        public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject obj)=>m_EntitiesList._items.FirstOrDefault(e=>ReferenceEquals(e.m_GameObject,obj));
        public static uint GetId(UnityEngine.GameObject obj)=>GetEntry(obj)?.m_Header.m_uEntityID??0;
        public static void Add(UnityEngine.GameObject obj,uint? id=null)=>m_EntitiesList._items.Add(new(){m_GameObject=obj,m_Header=new(){m_uEntityID=id??Next++}});
    }
    public sealed class SpawnableEntityCollection : UnityEngine.Component { public List<UnityEngine.GameObject> Values=new(); }
}
namespace SuperchargedPatch.Extensions {
    public static class SpawnExt {
        public static List<UnityEngine.GameObject> GetSpawnables(this SpawnableEntityCollection collection)=>collection.Values;
        public static UnityEngine.GameObject GetSpawnableEntityByIndex(this SpawnableEntityCollection collection,int index)=>index<0||index>=collection.Values.Count?null:collection.Values[index];
    }
}
namespace SuperchargedPatch {
    public static class NativeKitchenCheckpoint {
        public static void ValidateComponentBlocks(Func<Type,bool> has,Hpmv.EntityWarpSpec spec,List<string> errors) {
            if(has(typeof(ServerWorkableItem))!=(spec.WorkableItem!=null))errors.Add("Workable mismatch");
            if(has(typeof(ServerThrowableItem))!=(spec.ThrowableItem!=null))errors.Add("Throwable mismatch");
        }
    }
}
public static class NetworkUtils {
    public static Dictionary<UnityEngine.GameObject,Func<UnityEngine.GameObject>> Factories=new();
    public static List<string> Events=new(); public static int SpawnCalls;public static int FailAfterRegisterAt=-1;
    public static bool DeferDestroy,DeferPhysicsOnDestroy; public static List<UnityEngine.GameObject> Pending=new();
    public static UnityEngine.GameObject ServerSpawnPrefab(UnityEngine.GameObject parent,UnityEngine.GameObject prefab) {
        SpawnCalls++; Events.Add("spawn:"+prefab.name); var obj=Factories[prefab](); EntitySerialisationRegistry.Add(obj);
        var attachment=obj.GetComponent<PhysicalAttachment>();
        if(attachment!=null) {
            EntitySerialisationRegistry.Add(attachment.m_container.gameObject);
            ServerPhysicsObjectSynchroniser.Add(EntitySerialisationRegistry.GetEntry(attachment.m_container.gameObject),attachment.m_container.gameObject);
        }
        if(SpawnCalls==FailAfterRegisterAt)throw new InvalidOperationException("Injected registered-spawn failure");
        SuperchargedPatch.NativeSpawnObservationPatch.Postfix(prefab,obj); return obj;
    }
    public static void DestroyObject(UnityEngine.GameObject obj) {
        obj.active=false; Events.Add("destroy:"+obj.name); Pending.Add(obj); if(!DeferDestroy)Flush();
    }
    public static void Flush() { foreach(var obj in Pending) {
        EntitySerialisationRegistry.m_EntitiesList._items.RemoveAll(e=>ReferenceEquals(e.m_GameObject,obj));
        if(!DeferPhysicsOnDestroy)ServerPhysicsObjectSynchroniser.RemoveForTransform(obj.transform);
    }Pending.Clear(); }
}
