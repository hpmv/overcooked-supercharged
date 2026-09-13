using BitStream;
using Team17.Online.Multiplayer.Messaging;

/// <summary>Served animation and actual native removal are separate observations.</summary>
public class PlateLifecycleAuxMessage : Serialisable
{
    public uint version = 1, phase, unityInstanceBits;
    public bool Deserialise(BitStreamReader reader)
    {
        if (reader.RemainingBits < 96) return false;
        version = reader.ReadUInt32(32); phase = reader.ReadUInt32(32); unityInstanceBits = reader.ReadUInt32(32);
        return version == 1 && (phase == 1 || phase == 2);
    }
    public void Serialise(BitStreamWriter writer)
    { writer.Write(version, 32); writer.Write(phase, 32); writer.Write(unityInstanceBits, 32); }
}
