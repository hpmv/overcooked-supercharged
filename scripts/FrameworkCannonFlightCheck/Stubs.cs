using System.Collections;
using System.Reflection;

namespace HarmonyLib {
 public static class AccessTools {
  public static FieldInfo Field(Type type,string name) {
   for(;type!=null;type=type.BaseType) {var f=type.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null)return f;}
   return null;
  }
 }
}
namespace UnityEngine {
 public class Object {
  public bool Destroyed;
  public static bool operator ==(Object a,Object b) {bool x=ReferenceEquals(a,null)||a.Destroyed,y=ReferenceEquals(b,null)||b.Destroyed;return x||y?x==y:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object value)=>ReferenceEquals(this,value);
  public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
 }
 public class Component:Object {public GameObject gameObject;public bool enabled=true;public Transform transform=>gameObject.transform;public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();}
 public class GameObject:Object {
  readonly Dictionary<Type,Component> parts=new();public Transform transform;
  public GameObject(){transform=new Transform{gameObject=this};parts[typeof(Transform)]=transform;}
  public T Add<T>()where T:Component,new(){var value=new T{gameObject=this};parts[typeof(T)]=value;return value;}
  public T GetComponent<T>()where T:Component=>parts.TryGetValue(typeof(T),out var value)?(T)value:null;
 }
 public class Transform:Component {public Transform parent;public void SetParent(Transform value,bool world){parent=value;}}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public struct Quaternion {public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
 public class Rigidbody:Component {public Vector3 position,velocity,angularVelocity;public Quaternion rotation;public bool isKinematic;}
 public class Collider:Component {}
}
public class PlayerControls:UnityEngine.Component {}
public class DynamicLandscapeParenting:UnityEngine.Component {}
public class ServerCannon:UnityEngine.Component {public bool flying;public bool IsFlying()=>flying;}
public class ClientCannon:UnityEngine.Component {public IList m_launches=new ArrayList();}
public class ClientCannonPlayerHandler:UnityEngine.Component {}
public class Cannon:UnityEngine.Component {public UnityEngine.Transform m_target;public ProjectileAnimation m_animation;}
public class ProjectileAnimation:UnityEngine.Component {}
public class ServerSessionInteractable:UnityEngine.Component {}
public class ServerCannonSessionInteractable:ServerSessionInteractable {}
public class ClientCannonSessionInteractable:UnityEngine.Component {public bool HasSession;}
public class ServerPlayerControlsImpl_Default:UnityEngine.Component {}
public class ChefLerp:UnityEngine.Component {}
public class EntitySerialisationEntry {public Header m_Header=new();public class Header{public uint m_uEntityID;}}
public static class EntitySerialisationRegistry {
 public static readonly Dictionary<UnityEngine.GameObject,EntitySerialisationEntry> Entries=new();
 public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject obj)=>Entries.TryGetValue(obj,out var value)?value:null;
}
namespace Team17.Online.Multiplayer.Messaging {
 public class WorldObjectMessage {
  public UnityEngine.Vector3 LocalPosition;public UnityEngine.Quaternion LocalRotation;public bool HasParent,HasPositions;public uint ParentEntityID;
  public void Copy(WorldObjectMessage value){LocalPosition=value.LocalPosition;LocalRotation=value.LocalRotation;HasParent=value.HasParent;HasPositions=value.HasPositions;ParentEntityID=value.ParentEntityID;}
 }
 public class ChefPositionMessage {
  public UnityEngine.Vector3 Velocity;public float NetworkTime,ClientTimeStamp;public WorldObjectMessage WorldObject=new();
  public void Copy(ChefPositionMessage value){Velocity=value.Velocity;NetworkTime=value.NetworkTime;ClientTimeStamp=value.ClientTimeStamp;WorldObject.Copy(value.WorldObject);}
 }
 public class ClientWorldObjectSynchroniser:UnityEngine.Component {}
 public class ClientChefSynchroniser:ClientWorldObjectSynchroniser {}
 public class ServerWorldObjectSynchroniser:UnityEngine.Component {}
 public class ServerChefSynchroniser:ServerWorldObjectSynchroniser {}
}
