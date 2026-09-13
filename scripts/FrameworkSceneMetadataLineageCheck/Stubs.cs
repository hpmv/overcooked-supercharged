using System;
using System.Collections.Generic;
using System.Linq;

namespace UnityEngine
{
    public class Object { }
    public class Component : Object { public GameObject gameObject; }
    public struct Vector3 { public float x, y, z; }
    public class Transform : Component { public Vector3 position; }
    public class GameObject : Object
    {
        private readonly List<Component> components = new List<Component>();
        public string name;
        public Transform transform;
        public GameObject(string value)
        {
            name = value; transform = new Transform { gameObject = this }; components.Add(transform);
        }
        public T Add<T>(T value) where T : Component
        { value.gameObject = this; components.Add(value); return value; }
        public T GetComponent<T>() where T : Component
        { return components.OfType<T>().FirstOrDefault(); }
        public T[] GetComponents<T>() where T : Component
        { return components.OfType<T>().ToArray(); }
    }
    public class Rigidbody : Component { }
}

public class PhysicalAttachment : UnityEngine.Component
{ public UnityEngine.Rigidbody m_container; }

public class SpawnableEntityCollection : UnityEngine.Component
{
    public UnityEngine.GameObject[] Spawnables = Array.Empty<UnityEngine.GameObject>();
    public IEnumerable<UnityEngine.GameObject> GetSpawnables() { return Spawnables; }
}

public sealed class EntityHeader { public uint m_uEntityID; }
public sealed class EntitySerialisationEntry
{
    public EntityHeader m_Header = new EntityHeader();
    public UnityEngine.GameObject m_GameObject;
    public ServerSynchronisedComponents m_ServerSynchronisedComponents = new ServerSynchronisedComponents();
}
public sealed class ServerSynchronisedComponent { public int Value; public int GetEntityType() { return Value; } }
public sealed class ServerSynchronisedComponents
{
    public ServerSynchronisedComponent[] _items = Array.Empty<ServerSynchronisedComponent>();
    public int Count;
}
public sealed class FastEntries
{
    public EntitySerialisationEntry[] _items = Array.Empty<EntitySerialisationEntry>();
    public int Count;
}
public static class EntitySerialisationRegistry
{
    public static FastEntries m_EntitiesList = new FastEntries();
    public static EntitySerialisationEntry GetEntry(uint id)
    {
        for (int i = 0; i < m_EntitiesList.Count; i++)
            if (m_EntitiesList._items[i].m_Header.m_uEntityID == id) return m_EntitiesList._items[i];
        return null;
    }
    public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject value)
    {
        for (int i = 0; i < m_EntitiesList.Count; i++)
            if (ReferenceEquals(m_EntitiesList._items[i].m_GameObject, value)) return m_EntitiesList._items[i];
        return null;
    }
    public static void Set(params EntitySerialisationEntry[] values)
    { m_EntitiesList._items = values; m_EntitiesList.Count = values.Length; }
}

namespace Hpmv
{
    public sealed class FrameData { public List<EntityRegistryData> EntityRegistry; }
    public sealed class ServerState { public FrameData CurrentFrameData = new FrameData(); }
    public static class Injector { public static ServerState Server = new ServerState(); }
    public sealed class EntityRegistryData
    {
        public int EntityId; public string Name; public object Pos;
        public List<string> Components; public List<int> SyncEntityTypes; public List<string> SpawnNames;
    }
}

namespace SuperchargedPatch.Extensions
{
    public static class VectorExtensions
    { public static object ToThrift(this UnityEngine.Vector3 value) { return value; } }
}

namespace Team17.Online.Multiplayer.Messaging { }

namespace SuperchargedPatch
{
    public static class ActiveStateCollector
    {
        public static int Requests;
        public static void RequestFullObservation() { Requests++; }
    }
}
