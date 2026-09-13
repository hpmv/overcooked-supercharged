using System.Reflection;
namespace Hpmv {
 public class WarpSpec { public int Frame;public List<EntityWarpSpec> Entities=new();public List<int> EntitiesToDelete=new(); }
 public class EntityWarpSpec {
  public int EntityId;public EntityPathReference EntityPathReference;public List<int> SpawningPath=new();public Isset __isset=new();
  public StackWarpData Stack;public AttachStationWarpData AttachStation;public PlateReturnStationWarpData PlateReturnStation;
  public class Isset {public bool entityId,entityPathReference;}
 }
 public class EntityPathReference {public List<int> Ids=new();}
 public class EntityIdOrRef {public int EntityId;public EntityPathReference EntityPathReference;public Isset __isset=new();public class Isset{public bool entityId,entityPathReference;}}
 public class StackWarpData {public List<EntityIdOrRef> StackContents=new();}
 public class AttachStationWarpData {public EntityIdOrRef Item;}
 public class PlateReturnStationWarpData {public EntityIdOrRef Stack;}
 public class InputData {
  public bool RequestResume,RequestPause;public WarpSpec Warp;public double GameSpeed;public Dictionary<int,object> Input=new();
  public Isset __isset=new();public class Isset {public bool gameSpeed,resetOrderSeed;}
 }
 public class FrameData {public int FrameNumber;}
 public class InjectorServer {public InputData CurrentInput;public FrameData CurrentFrameData=new();}
 public static class Injector {public static InjectorServer Server=new();}
}
namespace UnityEngine {
 public class Object {static int next;readonly int id=++next;public int GetInstanceID()=>id;public static void DestroyImmediate(Object value){if(value is Component c)c.gameObject.components.Remove(c);}}
 public class Component:Object {public GameObject gameObject;public Transform transform=>gameObject.transform;}
 public class GameObject:Object {
  public readonly Transform transform;public readonly List<Component> components=new();
  public GameObject(){transform=new(){gameObject=this};}
  public T GetComponent<T>()where T:Component=>components.OfType<T>().FirstOrDefault();
  public T[] GetComponents<T>()where T:Component=>components.OfType<T>().ToArray();
  public T Add<T>()where T:Component,new(){var x=new T{gameObject=this};components.Add(x);return x;}
 }
 public class Transform:Component {
  readonly List<Transform> children=new();Transform _parent;
  public Transform parent {get=>_parent;set{if(ReferenceEquals(_parent,value))return;_parent?.children.Remove(this);_parent=value;_parent?.children.Add(this);}}
  public int childCount=>children.Count;public Transform GetChild(int index)=>children[index];
  public Vector3 localPosition;public Quaternion localRotation=new(){w=1};public Vector3 localScale=new(){x=1,y=1,z=1};
  public Vector3 position {get=>parent==null?localPosition:new(){x=parent.position.x+localPosition.x,y=parent.position.y+localPosition.y,z=parent.position.z+localPosition.z};set=>localPosition=parent==null?value:new(){x=value.x-parent.position.x,y=value.y-parent.position.y,z=value.z-parent.position.z};}
  public Quaternion rotation {get=>localRotation;set=>localRotation=value;}
  public Vector3 lossyScale=>localScale;
 }
 public struct Vector3 {public float x,y,z;}
 public struct Quaternion {public float x,y,z,w;}
 public enum RigidbodyConstraints{}
 public enum RigidbodyInterpolation{}
 public enum CollisionDetectionMode{}
 public class Rigidbody:Component {
  public Vector3 position,velocity,angularVelocity,centerOfMass,inertiaTensor;public Quaternion rotation=new(){w=1},inertiaTensorRotation=new(){w=1};
  public float mass=1,drag,angularDrag,sleepThreshold=.005f,maxAngularVelocity=7;public RigidbodyConstraints constraints;
  public RigidbodyInterpolation interpolation;public CollisionDetectionMode collisionDetectionMode;public bool isKinematic=true,useGravity=true,detectCollisions=true,sleeping=true;
  public bool IsSleeping()=>sleeping;
 }
 public static class Time {public static float time=100;}
}
public static class TimeManager {public enum PauseLayer{Main};public static bool paused=true;public static bool IsPaused(PauseLayer p)=>paused;}
public class ObjectContainer:UnityEngine.Rigidbody,IParentable{}
public class PhysicalAttachment:UnityEngine.Component{public ObjectContainer m_container;public Lerp m_meshLerper;public bool GetFakeMeshActive()=>false;}
public interface IClientSidePredicted{}
public class ServerPhysicalAttachment:UnityEngine.Component {private bool m_bIsClientSidePredicted;public bool attached=true;public bool IsAttached()=>attached;}
public class ClientPhysicalAttachment:UnityEngine.Component {private bool m_bClientSidePredicted;private IParentable m_Parent;public IClientSidePredicted m_Prediction;public bool attached=true;public bool IsAttached()=>attached;public IClientSidePredicted GetClientSidePrediction()=>m_Prediction;public void Setup(IParentable parent){m_Parent=parent;}}
public class DynamicLandscapeParenting:UnityEngine.Component{}
public class ServerDynamicLandscapeParenting:UnityEngine.Component,Team17.Online.Multiplayer.Messaging.ServerSynchroniser{}
public class ClientDynamicLandscapeParenting:UnityEngine.Component,Team17.Online.Multiplayer.Messaging.ClientSynchroniser{}
public interface IParentable{}
public class AttachStation:UnityEngine.Component,IParentable{}
public class SpawnableEntityCollection:UnityEngine.Component {
 public readonly List<UnityEngine.GameObject> Spawnables=new();
 public IEnumerable<UnityEngine.GameObject> GetSpawnables()=>Spawnables;
 public UnityEngine.GameObject GetSpawnableEntityByIndex(int index)=>Spawnables[index];
}
public delegate void GenericVoid();
public interface Lerp{}
public class EmptyLerp:UnityEngine.Component,Lerp{}
public class FastList<T> {public List<T> _items=new();public int Count=>_items.Count;public bool Remove(T value)=>_items.Remove(value);}
public class LevelConfigBase {public bool m_disableDynamicParenting=true;}
public static class GameUtils {public static LevelConfigBase GetLevelConfig()=>new();}
public class MultiplayerController:UnityEngine.Component {private Team17.Online.Multiplayer.Messaging.ServerSynchronisationScheduler m_ServerSync=new();}
public class DebugManager {public static DebugManager Instance=new();public bool fast=true;public bool GetOption(string name)=>fast;}
namespace Team17.Online.Multiplayer.Messaging {
 public interface Serialisable{}
 public class WorldObjectMessage:Serialisable {
  public UnityEngine.Vector3 LocalPosition;public UnityEngine.Quaternion LocalRotation;
  public bool HasParent,HasPositions;public uint ParentEntityID;
  public void Copy(WorldObjectMessage o){LocalPosition=o.LocalPosition;LocalRotation=o.LocalRotation;HasParent=o.HasParent;HasPositions=o.HasPositions;ParentEntityID=o.ParentEntityID;}
 }
 public interface ServerSynchroniser{}
 public interface ClientSynchroniser{}
 public class ServerPhysicsObjectSynchroniser:UnityEngine.Component,ServerSynchroniser{
  public sealed class SerialisationEntryTransformPair {public EntitySerialisationEntry m_Entry;public UnityEngine.Transform m_Transform;}
  private static readonly List<SerialisationEntryTransformPair> ms_ServerPhysicsObjectSytnchroniserTransforms=new();
  internal static void Register(EntitySerialisationEntry entry){foreach(var sync in entry.m_ServerSynchronisedComponents._items.OfType<ServerPhysicsObjectSynchroniser>())ms_ServerPhysicsObjectSytnchroniserTransforms.Add(new(){m_Entry=entry,m_Transform=sync.transform});}
  internal static void Unregister(EntitySerialisationEntry entry){ms_ServerPhysicsObjectSytnchroniserTransforms.RemoveAll(value=>ReferenceEquals(value.m_Entry,entry));}
  internal static void ClearRegistry(){ms_ServerPhysicsObjectSytnchroniserTransforms.Clear();}
 }
 public class ClientWorldObjectSynchroniser:UnityEngine.Component,ClientSynchroniser {
  protected UnityEngine.Transform m_Transform;protected IParentable m_Parent;protected uint m_ParentEntityID;protected bool m_bHasParent;
  private UnityEngine.Vector3 m_ServerPosition;protected Lerp m_Lerper;protected bool m_bPaused;protected Serialisable m_PendingResumeData;
  private PhysicalAttachment m_PhysicalAttachment;private GenericVoid m_parentChanged=()=>{};public bool m_bHasEverReceived;
  public void Setup(IParentable p,uint id){m_Transform=transform;m_Parent=p;m_ParentEntityID=id;m_bHasParent=true;m_PhysicalAttachment=gameObject.GetComponent<PhysicalAttachment>();m_Lerper=gameObject.Add<EmptyLerp>();m_bHasEverReceived=true;}
 }
 public class ServerWorldObjectSynchroniser:UnityEngine.Component,ServerSynchroniser {
  private WorldObjectMessage m_ServerData=new();protected UnityEngine.Transform m_Transform;
  private UnityEngine.Transform m_CachedParentTransform;private float m_LastUnreliableActiveSend;
  private bool m_bSentReliableRestPosition;protected bool m_bStartedSynchronising;
  protected bool m_bSleepAllowed=true;private bool m_bActive;private bool m_bSyncPositions=true;
  private bool m_bParentChanged=true;protected bool m_bPaused;
  public void Setup(){m_Transform=transform;m_CachedParentTransform=transform.parent;m_ServerData.HasParent=true;m_ServerData.ParentEntityID=41;m_ServerData.HasPositions=true;m_ServerData.LocalRotation=transform.localRotation;m_bSentReliableRestPosition=true;m_bStartedSynchronising=true;m_bParentChanged=false;}
  public bool RestEventDue()=>m_bSleepAllowed&&!m_bSentReliableRestPosition&&SuperchargedPatch.UnrealTimePatch.LogicalRealtime()>m_LastUnreliableActiveSend+1f;
  public void ResumePositionsWitness(){m_bSyncPositions=true;m_bSentReliableRestPosition=false;m_LastUnreliableActiveSend=0;m_CachedParentTransform=transform.parent;m_bParentChanged=true;}
 }
 public class Header {public uint m_uEntityID;}
 public class EntitySerialisationEntry {public UnityEngine.GameObject m_GameObject;public Header m_Header=new();public FastList<ServerSynchroniser> m_ServerSynchronisedComponents=new();public FastList<ClientSynchroniser> m_ClientSynchronisedComponents=new();private bool m_bUrgentUpdate;}
 public class ServerSynchronisationScheduler {
  private float m_fNextUpdate=.016666673f,m_fNextFastUpdate=.0000001f;
  private FastList<EntitySerialisationEntry> m_EntitiesList=new(),m_FastEntitiesList=new();
  private bool m_bStarted=true;private object m_SessionCoordinator=new();
 }
 public static class EntitySerialisationRegistry {
  public sealed class EntryDictionary:Dictionary<uint,EntitySerialisationEntry>{
   public new void Add(uint id,EntitySerialisationEntry entry){base.Add(id,entry);m_EntitiesList._items.Add(entry);ServerPhysicsObjectSynchroniser.Register(entry);}
   public new bool Remove(uint id){if(!TryGetValue(id,out var entry)||!base.Remove(id))return false;m_EntitiesList._items.Remove(entry);ServerPhysicsObjectSynchroniser.Unregister(entry);return true;}
   public new void Clear(){base.Clear();m_EntitiesList._items.Clear();ServerPhysicsObjectSynchroniser.ClearRegistry();}
  }
  public static bool HasUrgentOutgoingUpdates;
  public static Queue<ushort> m_ServerFreeEntityIDList=new();
  public static readonly FastList<EntitySerialisationEntry> m_EntitiesList=new();
  public static readonly EntryDictionary entries=new();public static EntitySerialisationEntry GetEntry(uint id)=>entries.GetValueOrDefault(id);
  public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject obj)=>entries.Values.SingleOrDefault(e=>ReferenceEquals(e.m_GameObject,obj));
  public static uint GetId(UnityEngine.GameObject obj)=>GetEntry(obj)?.m_Header.m_uEntityID??0;
 }
}
namespace SuperchargedPatch {
 public static class UnrealTimePatch {
  public static float LogicalTime=100;
  public static float LogicalRealtime()=>LogicalTime;
  public static float CaptureLogicalRealtime()=>LogicalTime;
 }
 public class EntityPathReferenceMarker:UnityEngine.Component{}
 public static class Helpers {public static void Resume(){TimeManager.paused=false;}}
 public sealed class TASPatcher {public void LateUpdate(){}}
 public static class ControllerHandler {public static MultiplayerController MultiplayerController;}
 public static class NativeSceneMetadata {internal static readonly HashSet<int> InitialPhysicalAttachmentIds=new();}
 public static class NativeAttachmentPoseCheckpoint {public sealed class Snapshot {public int EntityId {get;internal set;}}}
 public static class NativeKitchenCheckpoint {
  private static readonly Dictionary<int,Snapshot> history=new();private static object roundIdentity=new();
  internal class Snapshot {internal int frame;internal float LogicalRealtime;internal NativeAttachmentPoseCheckpoint.Snapshot[] FixedAttachmentPoses;}
  public static void Reset(){history.Clear();roundIdentity=new();}
  public static void CaptureFrame(int frame){if(!history.ContainsKey(frame))history.Add(frame,new(){frame=frame,LogicalRealtime=UnrealTimePatch.CaptureLogicalRealtime(),
   FixedAttachmentPoses=NativeSceneMetadata.InitialPhysicalAttachmentIds
    .Where(id=>Team17.Online.Multiplayer.Messaging.EntitySerialisationRegistry.GetEntry((uint)id)!=null)
    .OrderBy(id=>id).Select(id=>new NativeAttachmentPoseCheckpoint.Snapshot{EntityId=id}).ToArray()});}
  public static void RecordRestoreFailure(int frame,Exception error,bool mutationStarted){}
  public static RestorePlan Prepare(Hpmv.WarpSpec w)=>new(history[w.Frame]);
  public sealed class RestorePlan {private readonly Snapshot snapshot;internal RestorePlan(Snapshot s){snapshot=s;}public void Complete(){foreach(int f in history.Keys.Where(f=>f>snapshot.frame).ToArray())history.Remove(f);}}
 }
 public sealed class NativeDynamicWarpPlan {
  private sealed class Profile {public UnityEngine.GameObject Prefab;public Type[] Components;}
  private sealed class Target {public Hpmv.EntityWarpSpec Spec;public Profile[] Chain;public UnityEngine.GameObject Actual;}
  private sealed class Removal {public int Id,ContainerId;public UnityEngine.GameObject Object,Container;}
  private static readonly Dictionary<UnityEngine.GameObject,UnityEngine.GameObject> liveSpawned=new();
  private readonly List<Target> targets=new();private readonly List<Removal> removals=new();
  public static void ObserveFixture(UnityEngine.GameObject spawned,UnityEngine.GameObject prefab)=>liveSpawned[spawned]=prefab;
  public static NativeDynamicWarpPlan Fixture(Hpmv.EntityWarpSpec spec,Type[] components,UnityEngine.GameObject deleteOwner,UnityEngine.GameObject deleteContainer,UnityEngine.GameObject prefab=null){
   var p=new NativeDynamicWarpPlan();p.targets.Add(new(){Spec=spec,Chain=new[]{new Profile{Prefab=prefab,Components=components}}});
   if(deleteOwner!=null||deleteContainer!=null)p.removals.Add(new(){Id=(int)Team17.Online.Multiplayer.Messaging.EntitySerialisationRegistry.GetId(deleteOwner),ContainerId=(int)Team17.Online.Multiplayer.Messaging.EntitySerialisationRegistry.GetId(deleteContainer),Object=deleteOwner,Container=deleteContainer});return p;
  }
  public static NativeDynamicWarpPlan InitialFixture(Hpmv.EntityWarpSpec spec,Type[][] components){
   var p=new NativeDynamicWarpPlan();p.targets.Add(new(){Spec=spec,Chain=components.Select(c=>new Profile{Components=c}).ToArray()});return p;
  }
  public void SetActual(UnityEngine.GameObject value){targets[0].Actual=value;}
  public void Spawn(){}
 }
}
namespace SuperchargedPatch.Extensions {}
namespace SuperchargedPatch.Authoring.Modules {
 public sealed class BodyRestoreModule {
  private sealed class Token {public int Id;}
  public static int DynamicCaptures,DynamicRestores,LastCapturedId,LastRestoredId;
  public static object CaptureDynamicBody(int entityId){
   if(Team17.Online.Multiplayer.Messaging.EntitySerialisationRegistry.GetEntry((uint)entityId)==null)
    throw new InvalidOperationException("fixture body is absent");
   DynamicCaptures++;LastCapturedId=entityId;return new Token{Id=entityId};
  }
  public static void RestoreDynamicBody(object token,int entityId){
   var saved=token as Token;
   if(saved==null||saved.Id!=entityId||Team17.Online.Multiplayer.Messaging.EntitySerialisationRegistry.GetEntry((uint)entityId)==null)
    throw new InvalidOperationException("fixture body rebind differs");
   DynamicRestores++;LastRestoredId=entityId;
  }
 }
}
namespace HarmonyLib {
 public class Patch {public string owner;}
 public class Patches {public readonly List<Patch> Prefixes=new(),Postfixes=new();}
 public class Harmony {
  public Harmony(string id){} public static Patches GetPatchInfo(MethodBase m)=>null;
  public void Patch(MethodBase m,HarmonyMethod prefix=null,HarmonyMethod postfix=null,HarmonyMethod transpiler=null,HarmonyMethod finalizer=null){}public void UnpatchSelf(){}
 }
 public class HarmonyMethod {public int priority;public string[] after;public HarmonyMethod(MethodInfo m){}}
 public static class Priority {public const int Last=0;}
 public static class AccessTools {
  public static FieldInfo Field(Type t,string n)=>t.GetField(n,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
  public static MethodInfo Method(Type t,string n,Type[] p)=>t.GetMethod(n,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,p,null);
 }
}
