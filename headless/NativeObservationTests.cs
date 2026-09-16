using BitStream;
using Google.Protobuf;
using Hpmv;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;

public static class NativeObservationTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException("Native codec fixture: " + name); checks++; }
        foreach (var state in new[] { CannonMessage.CannonState.Launched, CannonMessage.CannonState.Load, CannonMessage.CannonState.Unload })
        foreach (int loaded in new[] { -1, 104 })
        {
            var message = new CannonMessage { m_state = state, m_angle = -32.125f, m_loadedObject = loaded };
            var bytes = message.ToBytes(); var copy = new CannonMessage();
            Check(copy.Deserialise(new BitStreamReader(bytes)) && copy.m_state == state && copy.m_angle == -32.125f && copy.m_loadedObject == loaded, "native state/angle/optional loaded header roundtrip");
            Check(bytes.Length == (loaded > 0 ? 6 : 5), "native cannon has35/45bits, not replacement payload");
        }
        Check(!new CannonMessage().Deserialise(new BitStreamReader(new byte[4])), "truncated native payload rejected");
        // Captured native-I frame21 EntityAux43 from the old plugin's overloaded
        // Write(1u): uint1 incorrectly became float1.0 bits 0x3f800000.
        byte[] malformedNativeI = Convert.FromHexString("1500cfe00000000000000000000002856802f8826c0000");
        Check(Deserializer.Deserialize((int)MessageType.EntityAuxMessage, malformedNativeI) is null, "actual malformed native-I auxiliary version is rejected, not treated as settled");
        var setup = new Carnival34FourLevel(); var simulator = new RealGameSimulator { setup = setup }; simulator.Reset();
        FakeEntityRegistry.entityToTypes[84] = new() { EntityType.Cannon };
        var nativeEvent = new EntityEventMessage { m_Header = new() { m_uEntityID = 84 }, m_ComponentId = 0,
            m_Payload = new CannonMessage { m_state = CannonMessage.CannonState.Load, m_angle = 44, m_loadedObject = 105 } };
        var decoded = Deserializer.Deserialize((int)MessageType.EntityEvent, nativeEvent.ToBytes());
        Check(decoded is EntityEventMessage { m_Payload: CannonMessage }, "native EntityType.Cannon registration selects native codec");
        simulator.ApplyGameUpdate(decoded);
        var cannon = simulator.entityIdToRecord[84];
        Check(new CannonMessage().FromBytes(cannon.data[0].rawGameEntityData).m_loadedObject == 105, "native cannon data retained in original versioned history");
        Check(cannon.IsInCriticalSectionForWarping(0), "missing auxiliary observations reject cannon warp");
        // Independent corrected producer: native-J frame21, plugin explicit
        // Write(uint,32), retained verbatim from the actual exchange evidence.
        foreach (var pair in new[] {
            (84u, "1500c0000000400000000000000002856802f882700000"),
            (85u, "1540c0000000400000000000000002870e00d482700000") })
        {
            var actual = Deserializer.Deserialize((int)MessageType.EntityAuxMessage, Convert.FromHexString(pair.Item2));
            Check(actual is EntityAuxMessage message && message.m_entityHeader.m_uEntityID == pair.Item1 &&
                message.m_payload is NativeCannonAuxMessage { version: 1, activeLaunches: 0, settledForWarp: true }, "actual native-J corrected auxiliary decoded for " + pair.Item1);
            simulator.ApplyGameUpdate(actual);
            Check(!simulator.entityIdToRecord[(int)pair.Item1].IsInCriticalSectionForWarping(0), "actual native-J settled receipt opens only the observed cannon guard " + pair.Item1);
        }
        var aux = new NativeCannonAuxMessage { loadedEntityId = 105, readyToLaunch = true, settledForWarp = true, angle = 44, loadNormalizedTime = 1 };
        EntityAuxMessage Wrapped(Serialisable value, AuxEntityType type, uint id) => new() { m_entityHeader = new() { m_uEntityID = id }, m_auxEntityType = type, m_payload = value };
        var wrapped = Wrapped(aux, AuxEntityType.NativeCannonAux, 84);
        var decodedAux = Deserializer.Deserialize((int)MessageType.EntityAuxMessage, wrapped.ToBytes());
        Check(decodedAux is EntityAuxMessage { m_auxEntityType: AuxEntityType.NativeCannonAux, m_payload: NativeCannonAuxMessage { loadedEntityId: 105 } }, "auxiliary header precedes type and independent native payload");
        simulator.ApplyGameUpdate(decodedAux);
        Check(!cannon.IsInCriticalSectionForWarping(0), "explicit settled auxiliary receipt allows stale loaded ID");
        foreach (string field in new[] { "flying", "serverSessionActive", "clientSessionActive", "activeLaunches", "settledForWarp" })
        {
            var bad = new NativeCannonAuxMessage().FromBytes(aux.ToBytes());
            typeof(NativeCannonAuxMessage).GetField(field)!.SetValue(bad, field == "activeLaunches" ? (object)1u : field != "settledForWarp");
            simulator.ApplyGameUpdate(Wrapped(bad, AuxEntityType.NativeCannonAux, 84));
            Check(cannon.IsInCriticalSectionForWarping(0), "active native cannon guard: " + field);
        }
        simulator.ApplyGameUpdate(wrapped);
        Check(!new NativeCannonAuxMessage().Deserialise(new BitStreamReader(new byte[20])), "truncated auxiliary bytes rejected");
        var invalid = new NativeCannonAuxMessage { version = 2, settledForWarp = true };
        Check(!new NativeCannonAuxMessage().Deserialise(new BitStreamReader(invalid.ToBytes())), "unknown auxiliary version rejected");
        simulator.AdvanceFrame();
        var plate = simulator.entityIdToRecord[10];
        simulator.ApplyGameUpdate(Wrapped(new PlateLifecycleAuxMessage { phase = 1, unityInstanceBits = 0xfffffff0 }, AuxEntityType.PlateLifecycleAux, 10));
        Check(plate.existed[1] && simulator.entityIdToRecord.ContainsKey(10), "served marker leaves actual plate observable");
        var restored = Hpmv.Save.GameSetup.Parser.ParseFrom(setup.ToProto().ToByteArray()).FromProto();
        Check(restored.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 84).data[0].rawNativeCannonAux.SequenceEqual(aux.ToBytes()), "checkpoint preserves exact auxiliary cannon bytes");
        Check(new PlateLifecycleAuxMessage().FromBytes(restored.entityRecords.FixedEntities.Single(e => e.path.ids[0] == 10).data[1].rawPlateLifecycle).unityInstanceBits == 0xfffffff0, "checkpoint preserves native Unity instance bits");
        simulator.ApplyGameUpdate(Wrapped(new PlateLifecycleAuxMessage { phase = 2, unityInstanceBits = 0xfffffff0 }, AuxEntityType.PlateLifecycleAux, 10));
        Check(new PlateLifecycleAuxMessage().FromBytes(plate.data[1].rawPlateLifecycle).phase == 2, "actual removal receipt retained separately");
        simulator.ApplyGameUpdate(new EntityRetirementMessage { m_entityHeader = new() { m_uEntityID = 10 } });
        Check(!plate.existed[1] && !simulator.entityIdToRecord.ContainsKey(10), "only actual retirement removes plate from live reconstruction");
        return checks;
    }
}
