using Hpmv;
using System.Text.Json.Nodes;

namespace Supercharged.Headless;

public sealed partial class HeadlessSession
{
    private static JsonObject NativeCannonSnapshot(GameEntityRecord entity, int frame)
    {
        if (!entity.prefab.IsCannon) return null;
        var data = entity.data[frame]; var result = new JsonObject { ["observedAux"] = data.rawNativeCannonAux is not null };
        if (data.rawGameEntityData is not null)
        {
            var native = new CannonMessage();
            if (!native.Deserialise(new BitStream.BitStreamReader(data.rawGameEntityData))) throw new InvalidDataException("Invalid native cannon wire history.");
            result["state"] = native.m_state.ToString(); result["messageAngle"] = native.m_angle; result["messageLoadedEntityId"] = native.m_loadedObject;
        }
        if (data.rawNativeCannonAux is null) return result;
        var aux = new NativeCannonAuxMessage();
        if (!aux.Deserialise(new BitStream.BitStreamReader(data.rawNativeCannonAux))) throw new InvalidDataException("Invalid native cannon auxiliary history.");
        result["version"] = aux.version; result["loadedEntityId"] = aux.loadedEntityId;
        result["serverSessionActive"] = aux.serverSessionActive; result["clientSessionActive"] = aux.clientSessionActive;
        result["flying"] = aux.flying; result["readyToLaunch"] = aux.readyToLaunch; result["activeLaunches"] = aux.activeLaunches;
        result["settledForWarp"] = aux.IsSettled; result["angle"] = aux.angle; result["loadNormalizedTime"] = aux.loadNormalizedTime;
        result["loadedIdentityMeaning"] = "Observed native field; may retain a stale ID after flight. It does not alone establish occupancy.";
        return result;
    }
    private static JsonObject PlateLifecycleSnapshot(GameEntityRecord entity, int frame)
    {
        var bytes = entity.data[frame].rawPlateLifecycle; if (bytes is null) return null;
        var value = new PlateLifecycleAuxMessage();
        if (!value.Deserialise(new BitStream.BitStreamReader(bytes))) throw new InvalidDataException("Invalid native plate lifecycle history.");
        return new() { ["version"] = value.version, ["phase"] = value.phase, ["phaseName"] = value.phase == 1 ? "served" : "native-removal",
            ["unityInstanceBits"] = value.unityInstanceBits, ["servedIsNotRemoval"] = true };
    }
}
