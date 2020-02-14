using System;
using BitStream;
using UnityEngine;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009D5 RID: 2517
    public class SpawnPhysicalAttachmentMessage : Serialisable {
        // Token: 0x06003004 RID: 12292 RVA: 0x0001F17F File Offset: 0x0001D37F
        public void Initialise(EntityMessageHeader _spawner, int _spawnableID, EntityMessageHeader _desiredHeader, Vector3 _position, Quaternion _rotation, EntityMessageHeader _container) {
            this.m_SpawnEntityData.Initialise(_spawner, _spawnableID, _desiredHeader, _position, _rotation);
            this.m_ContainerHeader = _container;
        }

        // Token: 0x06003005 RID: 12293 RVA: 0x0001F19B File Offset: 0x0001D39B
        public void Serialise(BitStreamWriter writer) {
            this.m_SpawnEntityData.Serialise(writer);
            this.m_ContainerHeader.Serialise(writer);
        }

        // Token: 0x06003006 RID: 12294 RVA: 0x000D48A8 File Offset: 0x000D2AA8
        public bool Deserialise(BitStreamReader reader) {
            bool flag = this.m_SpawnEntityData.Deserialise(reader);
            return flag | this.m_ContainerHeader.Deserialise(reader);
        }

        // Token: 0x04002841 RID: 10305
        public SpawnEntityMessage m_SpawnEntityData = new SpawnEntityMessage();

        // Token: 0x04002842 RID: 10306
        public EntityMessageHeader m_ContainerHeader = new EntityMessageHeader();
    }
}