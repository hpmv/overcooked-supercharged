// Controlled object/registry/field fixtures only; not Unity/native replay proof.
using System.Reflection;
namespace HarmonyLib {
    public sealed class HarmonyPatch : Attribute { public HarmonyPatch(Type type,string method) {} }
    public sealed class HarmonyPostfix : Attribute {}
    public static class AccessTools { public static FieldInfo Field(Type type,string name) => type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Static); }
}
namespace UnityEngine {
    public class Object { public static List<ClientPlayerControlsImpl_Default> Chefs=new(); public static T[] FindObjectsOfType<T>() => Chefs.Cast<T>().ToArray(); }
    public sealed class GameObject { private static int serial;private int instance=++serial; public PlayerControls Controls=new();public int GetInstanceID()=>instance; }
}
public sealed class PlayerControls {
    public ControlSchemeData ControlScheme=new();
    public sealed class ControlSchemeData { private bool m_supressUse; public bool IsUseSuppressed()=>m_supressUse;public void TestSet(bool value)=>m_supressUse=value; }
}
public sealed class ClientPlayerControlsImpl_Default {
    private float m_lastPickupTimestamp;
    public UnityEngine.GameObject gameObject=new();
    public T GetComponent<T>() => (T)(object)gameObject.Controls;
    public float Timestamp=>m_lastPickupTimestamp;
    public void TestSet(float value)=>m_lastPickupTimestamp=value;
}
namespace SuperchargedPatch.Extensions { public static class SchemeExt { public static void SetUseSuppressed(this PlayerControls.ControlSchemeData value,bool suppressed) => value.TestSet(suppressed); } }
namespace Team17.Online.Multiplayer.Messaging {
    public sealed class EntityMessageHeader { public uint m_uEntityID; }
    public sealed class EntitySerialisationEntry { public EntityMessageHeader m_Header=new(); public UnityEngine.GameObject m_GameObject; }
    public static class EntitySerialisationRegistry {
        public static Dictionary<UnityEngine.GameObject,EntitySerialisationEntry> Entries=new();
        public static EntitySerialisationEntry GetEntry(UnityEngine.GameObject obj)=>Entries.TryGetValue(obj,out var e)?e:null;
        public static EntitySerialisationEntry GetEntry(uint id)=>Entries.Values.FirstOrDefault(e=>e.m_Header.m_uEntityID==id);
    }
}
public sealed class ClientPlateStation {}
public sealed class ClientPlate { public UnityEngine.GameObject gameObject=new(); }
namespace BitStream { public sealed class BitStreamWriter {
    public List<(uint Value,int Bits)> Integers=new();
    public void Write(uint value,int bits) { Integers.Add((value,bits)); }
    public void Write(float value) { throw new InvalidOperationException("Unexpected float encoding in integer lifecycle payload."); }
} }
namespace SuperchargedPatch {
    public enum AuxEntityType { PlateLifecycleAux=4 }
    public abstract class AuxMessageBase { public abstract AuxEntityType GetAuxEntityType(); public abstract void Serialise(BitStream.BitStreamWriter writer); }
    public static class AuxMessageSender {
        public static List<(uint Id,AuxMessageBase Message)> Messages=new();
        public static void SendAuxMessage(uint id,AuxMessageBase message)=>Messages.Add((id,message));
    }
    public static class EntityRetirementMessageSender {
        public static List<uint> Messages=new();
        public static void SendMessageToRetireEntity(uint id)=>Messages.Add(id);
    }
}
