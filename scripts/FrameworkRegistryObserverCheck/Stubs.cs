namespace UnityEngine {
 public class Object {static int next; readonly int id=++next; public string name="fixture";public int GetInstanceID()=>id;}
 public class Component:Object {}
 public class Rigidbody:Component {}
 public struct Vector3 {public float x,y,z;}
 public class Transform:Component {public Vector3 position;}
 public class GameObject:Object {
  public Transform transform=new();public List<Component> components=new();public Action OnRead;
  public T[] GetComponents<T>() where T:Component {OnRead?.Invoke();return components.OfType<T>().ToArray();}
  public T GetComponent<T>() where T:Component=>components.OfType<T>().SingleOrDefault();
 }
}
public class SpawnableEntityCollection:UnityEngine.Component {public List<UnityEngine.GameObject> spawnables=new();}
public class TimeManager {public enum PauseLayer{Main} public static bool Paused=true; public static bool IsPaused(PauseLayer layer)=>Paused;}
namespace SuperchargedPatch {
 public static class NativeSceneMetadata {public static int Refreshes=3;internal static readonly HashSet<int> InitialRigidBodyIds=new(){43,44,45,46,47,48,49,50};internal static readonly HashSet<int> InitialPhysicalAttachmentIds=new(){1,2,3,4};}
 public static class EntityRetirementMessageSender {public static void SendMessageToRetireEntity(uint id){Hpmv.Injector.Server.CurrentFrameData.ServerMessages??=new();Hpmv.Injector.Server.CurrentFrameData.ServerMessages.Add(id);}}
}
namespace SuperchargedPatch.Bridge {public static class NativeSessionBridge {public static bool InputBlocked=true;}}
namespace SuperchargedPatch.Extensions {public static class Spawnables {public static List<UnityEngine.GameObject> GetSpawnables(this SpawnableEntityCollection c)=>c.spawnables;}}
namespace Hpmv {
 public class Point {public double X,Y,Z;}
 public class EntityRegistryData {public int EntityId;public string Name;public Point Pos;public List<string> Components,SpawnNames;public List<int> SyncEntityTypes;}
 public class OutputData {public int FrameNumber=180;public List<EntityRegistryData> EntityRegistry;public List<uint> ServerMessages;}
 public class InjectorServer {public OutputData CurrentFrameData=new();}
 public static class Injector {public static InjectorServer Server=new();}
}
namespace Team17.Online.Multiplayer.Messaging {
 public class FastList<T> {public T[] _items=Array.Empty<T>();public int Count=>_items.Length;}
 public class Header {public uint m_uEntityID;}
 public class Sync {public int Type;public int GetEntityType()=>Type;}
 public class Entry {public Header m_Header=new();public UnityEngine.GameObject m_GameObject;public FastList<Sync> m_ServerSynchronisedComponents=new();}
 public static class EntitySerialisationRegistry {public static FastList<Entry> m_EntitiesList=new();public static Entry GetEntry(uint id)=>m_EntitiesList._items.SingleOrDefault(e=>e.m_Header.m_uEntityID==id);}
}
