namespace UnityEngine
{
    public class Object
    {
        static int next=100;readonly int id=++next;public string name="fixture";
        public int GetInstanceID()=>id;
    }
    public class Component:Object { }
    public class GameObject:Object
    {
        public readonly List<Component> components=new();
        public T[] GetComponents<T>()where T:Component=>components.OfType<T>().ToArray();
    }
    public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}}
    public struct Quaternion {public float x,y,z,w;public Quaternion(float a,float b,float c,float d){x=a;y=b;z=c;w=d;}}
}
namespace Team17.Online.Multiplayer.Messaging
{
    public class Header {public uint m_uEntityID;}
    public class Entry {public Header m_Header=new();public UnityEngine.GameObject m_GameObject;}
    public class FastList {public Entry[] _items=Array.Empty<Entry>();public int Count=>_items.Length;}
    public static class EntitySerialisationRegistry {public static FastList m_EntitiesList=new();}
}
