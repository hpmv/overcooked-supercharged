using BitStream;
using Team17.Online.Multiplayer.Messaging;

/// <summary>Read-only native component observations; not the replacement cannon's state machine.</summary>
public class NativeCannonAuxMessage : Serialisable
{
    public uint version = 1, loadedEntityId, activeLaunches;
    public bool serverSessionActive, clientSessionActive, flying, readyToLaunch, settledForWarp;
    public float angle, loadNormalizedTime;
    public bool Deserialise(BitStreamReader reader)
    {
        if (reader.RemainingBits < 165) return false;
        version = reader.ReadUInt32(32);
        if (version != 1) return false;
        loadedEntityId = reader.ReadUInt32(32);
        serverSessionActive = reader.ReadBit(); clientSessionActive = reader.ReadBit();
        flying = reader.ReadBit(); readyToLaunch = reader.ReadBit(); activeLaunches = reader.ReadUInt32(32);
        settledForWarp = reader.ReadBit(); angle = reader.ReadFloat32(); loadNormalizedTime = reader.ReadFloat32();
        return !float.IsNaN(angle) && !float.IsInfinity(angle) && !float.IsNaN(loadNormalizedTime) && !float.IsInfinity(loadNormalizedTime);
    }
    public void Serialise(BitStreamWriter writer)
    {
        writer.Write(version, 32); writer.Write(loadedEntityId, 32);
        writer.Write(serverSessionActive); writer.Write(clientSessionActive); writer.Write(flying); writer.Write(readyToLaunch);
        writer.Write(activeLaunches, 32); writer.Write(settledForWarp); writer.Write(angle); writer.Write(loadNormalizedTime);
    }
    public bool IsSettled => version == 1 && settledForWarp && !serverSessionActive && !clientSessionActive && !flying && activeLaunches == 0;
}
