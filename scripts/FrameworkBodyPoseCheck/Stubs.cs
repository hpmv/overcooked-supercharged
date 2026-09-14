namespace UnityEngine {
 public class Object {
  static int sequence;readonly int id=++sequence;private IntPtr m_CachedPtr=new(1);public bool Destroyed;public int GetInstanceID()=>id;
  public static bool operator ==(Object a,Object b){bool x=ReferenceEquals(a,null)||a.Destroyed,y=ReferenceEquals(b,null)||b.Destroyed;return x||y?x==y:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object value)=>ReferenceEquals(this,value);public override int GetHashCode()=>id;
 }
 public class Component:Object{public GameObject gameObject;public Transform transform=>gameObject.transform;}
 public class GameObject:Object {
  public readonly Transform transform;public Rigidbody body;public Component objectContainer;public bool activeSelf=true;public int layer;public string name="GameObject";
  public bool activeInHierarchy=>activeSelf&&(transform.parent==null||transform.parent.gameObject.activeInHierarchy);
  public List<Collider> colliders=new();public static List<GameObject> All=new();
  public GameObject(){transform=new Transform{gameObject=this};All.Add(this);}
  public T GetComponent<T>()where T:Component=>body as T;
  public Rigidbody AddBody(){body=new Rigidbody{gameObject=this};return body;}
  public T[] GetComponents<T>()where T:Component=>new Component[]{transform,body,objectContainer}.Concat(colliders).Where(c=>c!=null).OfType<T>().ToArray();
  public T[] GetComponentsInChildren<T>(bool includeInactive)where T:Component=>All.Where(o=>o==this||Ancestor(o.transform,transform)).SelectMany(o=>o.GetComponents<T>()).ToArray();
  static bool Ancestor(Transform t,Transform parent){for(var p=t.parent;p!=null;p=p.parent)if(p==parent)return true;return false;}
  public T AddCollider<T>()where T:Collider,new(){var c=new T{gameObject=this};colliders.Add(c);return c;}
 }
 public class Transform:Component {
  Vector3 local;Quaternion orient;
  public Transform parent;public Vector3 position;public Quaternion rotation;public Vector3 localScale=new(1,1,1);
  public int GetSiblingIndex()=>0;
  public Vector3 localPosition {get=>local;set{Events.Rows.Add("transform-position");local=value;position=value;}}
  public Quaternion localRotation {get=>orient;set{Events.Rows.Add("transform-rotation");orient=value;rotation=value;}}
 }
 public class Rigidbody:Component {
  Vector3 pos;Quaternion orient;public Vector3 velocity,angularVelocity;public bool useGravity,detectCollisions=true;
  bool kin;public Action<bool> OnKinematicSet;public bool isKinematic{get=>kin;set{kin=value;OnKinematicSet?.Invoke(value);}}
  public float mass=1,drag,angularDrag=.05f,sleepThreshold=.005f,maxAngularVelocity=7;
  public Vector3 centerOfMass{get;set;}public Vector3 inertiaTensor{get;set;}=new(1,1,1);public Quaternion inertiaTensorRotation{get;set;}=new(0,0,0,1);
  public Action ResetCenter,ResetTensor;
  public void ResetCenterOfMass(){Events.Rows.Add("reset-center");ResetCenter?.Invoke();}
  public void ResetInertiaTensor(){Events.Rows.Add("reset-inertia");ResetTensor?.Invoke();}
  public RigidbodyConstraints constraints;public RigidbodyInterpolation interpolation;public CollisionDetectionMode collisionDetectionMode;
  public Func<Quaternion,Quaternion> RotationRoundtrip;public Action OnRotationSet;
  public Func<Vector3,Vector3> PositionRoundtrip;
  public Vector3 position {get=>pos;set{Events.Rows.Add("body-position");pos=PositionRoundtrip==null?value:PositionRoundtrip(value);}}
  public Quaternion rotation {get=>orient;set{Events.Rows.Add("body-rotation");orient=RotationRoundtrip==null?value:RotationRoundtrip(value);OnRotationSet?.Invoke();}}
 }
 public class PhysicMaterial:Object{}
 public class Mesh:Object{}
 public class Collider:Component {
  public bool enabled=true,isTrigger;public PhysicMaterial sharedMaterial;
  public PhysicMaterial material=>throw new Exception("Instantiating material getter must never be used");
  public Rigidbody attachedRigidbody{get{for(var t=transform;t!=null;t=t.parent)if(t.gameObject.body!=null)return t.gameObject.body;return null;}}
 }
 public class BoxCollider:Collider{public Vector3 center,size=new(1,1,1);}
 public class SphereCollider:Collider{public Vector3 center;public float radius=.4f;}
 public class CapsuleCollider:Collider{public Vector3 center;public float radius=.4f,height=2;public int direction=1;}
 public class MeshCollider:Collider{public Mesh sharedMesh;public bool convex;}
 public enum RigidbodyConstraints {None,FreezeRotation}
 public enum RigidbodyInterpolation {None,Interpolate}
 public enum CollisionDetectionMode {Discrete,Continuous}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public struct Quaternion {public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
}
namespace HarmonyLib {
 [AttributeUsage(AttributeTargets.Class)]public class HarmonyPatch:Attribute{public HarmonyPatch(Type type,string method){}}
 [AttributeUsage(AttributeTargets.Method)]public class HarmonyPrefix:Attribute{}
 public class Patch{public System.Reflection.MethodInfo PatchMethod;}
 public class Patches{public Patch[] Prefixes;}
 public static class Harmony {
  public static bool Installed=true;
  public static Patches GetPatchInfo(System.Reflection.MethodInfo method){
   if(!Installed)return null;var name=method.Name switch{"set_centerOfMass"=>"CenterSetter","set_inertiaTensor"=>"TensorSetter",_=>"RotationSetter"};
   var type=typeof(SuperchargedPatch.NativeBodyMassMode).GetNestedType(name,System.Reflection.BindingFlags.NonPublic);
   return new Patches{Prefixes=new[]{new Patch{PatchMethod=type.GetMethod("Prefix",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)}}};
  }
 }
}
public static class Events{public static List<string> Rows=new();}
public class ObjectContainer:UnityEngine.Component{}
public static class TimeManager{public enum PauseLayer{Main}public static bool IsPaused(PauseLayer layer)=>SuperchargedPatch.Helpers.Paused;}
namespace SuperchargedPatch {public static class Helpers{public static bool Paused=true;public static bool IsPaused()=>Paused;}}
namespace Team17.Online.Multiplayer.Messaging {
 public sealed class EntitySerialisationEntry {public Header m_Header=new();public UnityEngine.GameObject m_GameObject;public sealed class Header{public uint m_uEntityID;}}
 public sealed class EntryList {public List<EntitySerialisationEntry> _items=new();public int Count=>_items.Count;}
 public static class EntitySerialisationRegistry {
  public static EntryList m_EntitiesList=new();
  public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject obj)=>m_EntitiesList._items.FirstOrDefault(e=>ReferenceEquals(e.m_GameObject,obj));
  public static EntitySerialisationEntry GetEntry(uint id)=>m_EntitiesList._items.FirstOrDefault(e=>e.m_Header.m_uEntityID==id);
 }
}
