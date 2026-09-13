using System.Collections;
using System.Reflection;

namespace HarmonyLib {
 public static class AccessTools {
  public static FieldInfo Field(Type type,string name) {
   for(;type!=null;type=type.BaseType) {var f=type.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null)return f;}return null;
  }
 }
}
namespace UnityEngine {
 public class Object {
  static int next; readonly int id=++next; public bool Destroyed;
  public static readonly List<Object> All=new();
  public Object(){All.Add(this);} public int GetInstanceID()=>id;
  public static T[] FindObjectsOfType<T>() where T:Object=>All.OfType<T>().Where(x=>!x.Destroyed).ToArray();
  public static bool operator ==(Object a,Object b) {bool x=ReferenceEquals(a,null)||a.Destroyed,y=ReferenceEquals(b,null)||b.Destroyed;return x||y?x==y:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object value)=>ReferenceEquals(this,value);
  public override int GetHashCode()=>System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
 }
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;public T GetComponent<T>()where T:Component=>gameObject.GetComponent<T>();}
 public class GameObject:Object {
  readonly Dictionary<Type,Component> parts=new();public Transform transform;
  public GameObject(){transform=new Transform{gameObject=this};parts[typeof(Transform)]=transform;}
  public T Add<T>()where T:Component,new(){var value=new T{gameObject=this};parts[typeof(T)]=value;return value;}
  public T GetComponent<T>()where T:Component=>parts.TryGetValue(typeof(T),out var value)?(T)value:null;
 }
 public class Transform:Component {public Transform parent;public Vector3 localPosition;public Quaternion localRotation=Quaternion.identity,rotation=Quaternion.identity;public bool IsChildOf(Transform root){for(var t=this;t!=null;t=t.parent)if(t==root)return true;return false;}}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public struct Quaternion {public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}public static Quaternion identity=>new(0,0,0,1);public Vector3 eulerAngles=>new(0,y,0);}
 public struct AnimatorStateInfo {public int fullPathHash;public float normalizedTime;}
 public class Animator:Component {public bool Transition,Occupied;public AnimatorStateInfo State=new(){fullPathHash=99};public AnimatorStateInfo GetCurrentAnimatorStateInfo(int i)=>State;public bool GetBool(string name)=>Occupied;public bool IsInTransition(int i)=>Transition;public void SetBool(string name,bool value){Occupied=value;}public void ResetTrigger(string name){}public void Play(int hash,int layer,float time){State=new(){fullPathHash=hash,normalizedTime=time};}public void Update(float dt){}}
 public static class Time {public static int frameCount;}
}
public class PlayerControls:UnityEngine.Component {}
public class ServerCannon:UnityEngine.Component {
 public bool m_flying,m_readyToLaunch=true;public UnityEngine.GameObject m_loadedObject;public CannonMessage m_message=new();public object OnInteractionEnd;
 public bool IsFlying()=>m_flying;
}
public class ClientCannon:UnityEngine.Component {public IList m_launches=new ArrayList();public UnityEngine.GameObject m_loadedObject;public CannonMessage m_message=new();public UnityEngine.Vector3 m_exitPosition;public UnityEngine.Quaternion m_exitRotation;}
public class ClientCannonPlayerHandler:UnityEngine.Component {public object m_controls;public bool m_inCannon;}
public class ClientCannonCosmeticDecisions:UnityEngine.Component {}
public class CannonCosmeticDecisions:UnityEngine.Component {public UnityEngine.Animator m_cannonAnimator;}
public class Cannon:UnityEngine.Component {public UnityEngine.Transform m_attachPoint;}
public class ServerSessionInteractable:UnityEngine.Component {public object m_session;}
public class ServerCannonSessionInteractable:ServerSessionInteractable {}
public class ClientCannonSessionInteractable:UnityEngine.Component {public bool HasSession;}
public class PilotRotation:UnityEngine.Component {public UnityEngine.Transform m_transformToRotate;public object m_previousPose,m_previousPoseDifference,m_velocityAverage,m_belowThresholdCounter,m_bEstimateVelocityInX,m_directionModifier;}
public class ServerPilotRotation:UnityEngine.Component {public object m_angle,m_startAngle,m_startRightDirection,m_message,m_controlScheme;}
public class ClientPilotRotation:UnityEngine.Component {public object m_nextRotation,m_message,m_avatar;}
public class PilotRotationMessage {public float m_angle;}
public class CannonMessage {public int m_state;public float m_angle;public UnityEngine.GameObject m_loadedObject;}
public class FastList<T> {public readonly List<T> _items=new();public int Count=>_items.Count;}
namespace Team17.Online.Multiplayer.Messaging {
 public class EntitySerialisationEntry {public UnityEngine.GameObject m_GameObject;public Header m_Header=new();public class Header{public uint m_uEntityID;}}
 public static class EntitySerialisationRegistry {
  public static readonly Dictionary<UnityEngine.GameObject,EntitySerialisationEntry> Entries=new();public static readonly FastList<EntitySerialisationEntry> m_EntitiesList=new();
  public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject obj)=>Entries.TryGetValue(obj,out var value)?value:null;
 }
}
namespace BitStream {public class BitStreamWriter {public void Write(uint a,int b){}public void Write(bool a){}public void Write(float a){}}}
namespace SuperchargedPatch {
 public enum AuxEntityType {NativeCannonAux}
 public abstract class AuxMessageBase {public abstract AuxEntityType GetAuxEntityType();public abstract void Serialise(BitStream.BitStreamWriter writer);}
 public static class NativeCannonWarpGuard {public static string UnsettledReason(params object[] values)=>values.OfType<bool>().Any(v=>v)?"active":null;}
 public static class NativeCannonFlightCheckpoint {
  public class Snapshot {public UnityEngine.GameObject Passenger;}
  public static Snapshot TryCapture(ServerCannon s)=>null;public static void Validate(Snapshot s){}public static void Restore(Snapshot s){}public static bool Same(Snapshot a,Snapshot b)=>ReferenceEquals(a,b);
 }
 public static class Extensions {
  public static Action<ServerCannon,NativeCannonAuxMessage> OnSend;
  public static void SendAuxMessage(this ServerCannon server,NativeCannonAuxMessage message){OnSend?.Invoke(server,message);}
  public static T RequestComponentRecursive<T>(this UnityEngine.GameObject obj)where T:UnityEngine.Component=>obj.GetComponent<T>();
 }
}
