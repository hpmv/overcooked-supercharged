using System;
using BitStream;
using UnityEngine;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009D3 RID: 2515
    public class SpawnEntityMessage : Serialisable {
        // Token: 0x06002FFD RID: 12285 RVA: 0x0001F541 File Offset: 0x0001D741
        public void Initialise(EntityMessageHeader _spawner, int _spawnableID, EntityMessageHeader _desiredHeader, Vector3 _position, Quaternion _rotation) {
            this.m_SpawnerHeader = _spawner;
            this.m_SpawnableID = _spawnableID;
            this.m_DesiredHeader = _desiredHeader;
            this.m_Position = _position;
            this.m_Rotation = _rotation;
        }

        // Token: 0x06002FFE RID: 12286 RVA: 0x0001F568 File Offset: 0x0001D768
        public void Serialise(BitStreamWriter writer) {
            this.m_SpawnerHeader.Serialise(writer);
            writer.Write((uint) this.m_SpawnableID, 4);
            this.m_DesiredHeader.Serialise(writer);
            writer.Write(ref this.m_Position);
            writer.Write(ref this.m_Rotation);
        }

        // Token: 0x06002FFF RID: 12287 RVA: 0x000D4D74 File Offset: 0x000D2F74
        public bool Deserialise(BitStreamReader reader) {
            if (this.m_SpawnerHeader.Deserialise(reader)) {
                this.m_SpawnableID = (int) reader.ReadUInt32(4);
                if (this.m_DesiredHeader.Deserialise(reader)) {
                    reader.ReadVector3(ref this.m_Position);
                    reader.ReadQuaternion(ref this.m_Rotation);
                    return true;
                }
            }
            return false;
        }

        // Token: 0x04002844 RID: 10308
        public const int kBitsPerSpawnableID = 4;

        // Token: 0x04002845 RID: 10309
        public EntityMessageHeader m_SpawnerHeader = new EntityMessageHeader();

        // Token: 0x04002846 RID: 10310
        public int m_SpawnableID;

        // Token: 0x04002847 RID: 10311
        public EntityMessageHeader m_DesiredHeader = new EntityMessageHeader();

        // Token: 0x04002848 RID: 10312
        public Vector3 m_Position;

        // Token: 0x04002849 RID: 10313
        public Quaternion m_Rotation;
    }
}