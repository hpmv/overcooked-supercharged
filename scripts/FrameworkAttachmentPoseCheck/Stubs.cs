namespace UnityEngine {
 public class Object {
  static int sequence;readonly int id=++sequence;public bool Destroyed;public int GetInstanceID()=>id;
  public static bool operator ==(Object a,Object b){bool x=ReferenceEquals(a,null)||a.Destroyed,y=ReferenceEquals(b,null)||b.Destroyed;return x||y?x==y:ReferenceEquals(a,b);}
  public static bool operator !=(Object a,Object b)=>!(a==b);
  public override bool Equals(object value)=>ReferenceEquals(this,value);public override int GetHashCode()=>id;
 }
 public class Component:Object{public GameObject gameObject;}
 public class GameObject:Object {
  public readonly Transform transform;public List<Component> components=new();
  public GameObject(){transform=new Transform{gameObject=this};components.Add(transform);}
  public T GetComponent<T>()where T:Component=>components.OfType<T>().FirstOrDefault();
  public T[] GetComponents<T>()where T:Component=>components.OfType<T>().ToArray();
  public T Add<T>()where T:Component,new(){var c=new T{gameObject=this};components.Add(c);return c;}
 }
 public class Transform:Component {
  Vector3 local,scale=new(1,1,1);Quaternion orient=new(0,0,0,1);
  public Transform parent;public Vector3? WrongWorld;public Quaternion? WrongRotation;
  public Vector3 localPosition {get=>local;set{Events.Rows.Add("position");local=value;}}
  public Quaternion localRotation {get=>orient;set{Events.Rows.Add("rotation");orient=value;}}
  public Vector3 localScale {get=>scale;set{Events.Rows.Add("scale");scale=value;}}
  public Vector3 position=>WrongWorld??(parent==null?local:new(local.x+parent.position.x,local.y+parent.position.y,local.z+parent.position.z));
  public Quaternion rotation=>WrongRotation??orient;
  public Vector3 lossyScale=>parent==null?scale:new(scale.x*parent.lossyScale.x,scale.y*parent.lossyScale.y,scale.z*parent.lossyScale.z);
 }
 public class Rigidbody:Component{public Vector3 position;}
 public struct Vector3 {public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}}
 public struct Quaternion {public float x,y,z,w;public Quaternion(float x,float y,float z,float w){this.x=x;this.y=y;this.z=z;this.w=w;}}
}
public static class Events{public static List<string> Rows=new();}
public class EmptyLerp:UnityEngine.Component{}
public class BasicLerp:EmptyLerp{}
public class MeshLerper:UnityEngine.Component{}
public class PhysicalAttachment:UnityEngine.Component {public UnityEngine.Rigidbody m_container;public MeshLerper m_meshLerper;public bool ActiveMesh;public bool GetFakeMeshActive()=>ActiveMesh;}
public class ServerPhysicalAttachment:UnityEngine.Component {bool m_bIsClientSidePredicted;public bool Attached=true;public bool IsAttached()=>Attached;public void SetPrediction(bool v)=>m_bIsClientSidePredicted=v;}
public interface IParentable {}
public interface IClientSidePredicted {}
public class ConveyorPrediction:IClientSidePredicted {public List<int> m_Destinations=new();public UnityEngine.Transform m_Transform;float m_RemainingMove;bool m_bCalculatedDistance;public void SetState(float v,bool b){m_RemainingMove=v;m_bCalculatedDistance=b;}}
public class UnknownPrediction:IClientSidePredicted {}
public class ClientPhysicalAttachment:UnityEngine.Component {bool m_bClientSidePredicted;IParentable m_Parent;public bool Attached=true;public IClientSidePredicted m_Prediction;public bool IsAttached()=>Attached;public IClientSidePredicted GetClientSidePrediction()=>m_Prediction;public void SetPrediction(bool v)=>m_bClientSidePredicted=v;}
namespace Team17.Online.Multiplayer.Messaging {
 public sealed class EntitySerialisationEntry {public Header m_Header=new();public UnityEngine.GameObject m_GameObject;public sealed class Header{public uint m_uEntityID;}}
 public sealed class EntryList {public List<EntitySerialisationEntry> _items=new();public int Count=>_items.Count;}
 public static class EntitySerialisationRegistry {
  public static EntryList m_EntitiesList=new();
  public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject obj)=>m_EntitiesList._items.FirstOrDefault(e=>ReferenceEquals(e.m_GameObject,obj));
  public static EntitySerialisationEntry GetEntry(uint id)=>m_EntitiesList._items.FirstOrDefault(e=>e.m_Header.m_uEntityID==id);
 }
}
