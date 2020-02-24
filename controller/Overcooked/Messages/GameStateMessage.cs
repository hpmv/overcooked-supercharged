using System;
using BitStream;
using Team17.Online;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020009B0 RID: 2480
public class GameStateMessage : Serialisable {
    // Token: 0x17000428 RID: 1064
    // (get) Token: 0x06002F5B RID: 12123 RVA: 0x0001E83D File Offset: 0x0001CA3D
    // (set) Token: 0x06002F5C RID: 12124 RVA: 0x0001E845 File Offset: 0x0001CA45
    public GameStateMessage.GameStatePayload Payload { get; private set; }

    public enum MachineID {
        // Token: 0x04002BA9 RID: 11177
        One,
        // Token: 0x04002BAA RID: 11178
        Two,
        // Token: 0x04002BAB RID: 11179
        Three,
        // Token: 0x04002BAC RID: 11180
        Four,
        // Token: 0x04002BAD RID: 11181
        Count
    }

    // Token: 0x06002F5D RID: 12125 RVA: 0x0001E84E File Offset: 0x0001CA4E
    public void Initialise(GameState state, MachineID machine) {
        this.Initialise(state, machine, null);
    }

    // Token: 0x06002F5E RID: 12126 RVA: 0x0001E859 File Offset: 0x0001CA59
    public void Initialise(GameState state, MachineID machine, GameStateMessage.GameStatePayload payload) {
        this.m_State = state;
        this.m_Machine = machine;
        this.Payload = payload;
    }

    // Token: 0x06002F5F RID: 12127 RVA: 0x000D2F64 File Offset: 0x000D1164
    public void Serialise(BitStreamWriter writer) {
        writer.Write((uint)this.m_State, 6);
        writer.Write((uint)this.m_Machine, 3);
        writer.Write(this.Payload != null);
        if (this.Payload != null) {
            writer.Write((uint)this.Payload.GetPayLoadType(), this.kBitsPayloadType);
            this.Payload.Serialise(writer);
        }
    }

    // Token: 0x06002F60 RID: 12128 RVA: 0x000D2FCC File Offset: 0x000D11CC
    public bool Deserialise(BitStreamReader reader) {
        this.m_State = (GameState)reader.ReadUInt32(6);
        this.m_Machine = (MachineID)reader.ReadUInt32(3);
        if (reader.ReadBit()) {
            GameStateMessage.GameStatePayload payloadForType = this.GetPayloadForType((GameStateMessage.PayLoadType)reader.ReadUInt32(this.kBitsPayloadType));
            payloadForType.Deserialise(reader);
            this.Payload = payloadForType;
        } else {
            this.Payload = null;
        }
        return true;
    }

    // Token: 0x06002F61 RID: 12129 RVA: 0x0001E870 File Offset: 0x0001CA70
    private GameStateMessage.GameStatePayload GetPayloadForType(GameStateMessage.PayLoadType type) {
        if (type != GameStateMessage.PayLoadType.ClientSave) {
            return null;
        }
        return new GameStateMessage.ClientSavePayload();
    }

    // Token: 0x0400277A RID: 10106
    public const int kGameStateBits = 6;

    // Token: 0x0400277B RID: 10107
    public GameState m_State;

    // Token: 0x0400277C RID: 10108
    public MachineID m_Machine = MachineID.Count;

    // Token: 0x0400277D RID: 10109
    private int kBitsPayloadType = GameUtils.GetRequiredBitCount(1);

    // Token: 0x020009B1 RID: 2481
    public enum PayLoadType {
        // Token: 0x04002780 RID: 10112
        ClientSave,
        // Token: 0x04002781 RID: 10113
        COUNT
    }

    // Token: 0x020009B2 RID: 2482
    public abstract class GameStatePayload : Serialisable {
        // Token: 0x06002F63 RID: 12131
        public abstract GameStateMessage.PayLoadType GetPayLoadType();

        // Token: 0x06002F64 RID: 12132
        public abstract void Serialise(BitStreamWriter writer);

        // Token: 0x06002F65 RID: 12133
        public abstract bool Deserialise(BitStreamReader reader);
    }

    // Token: 0x020009B3 RID: 2483
    public class ClientSavePayload : GameStateMessage.GameStatePayload {
        // Token: 0x17000429 RID: 1065
        // (get) Token: 0x06002F67 RID: 12135 RVA: 0x0001E88C File Offset: 0x0001CA8C
        // (set) Token: 0x06002F68 RID: 12136 RVA: 0x0001E894 File Offset: 0x0001CA94
        public int DLCID { get; private set; }

        // Token: 0x06002F69 RID: 12137 RVA: 0x00002869 File Offset: 0x00000A69
        public override GameStateMessage.PayLoadType GetPayLoadType() {
            return GameStateMessage.PayLoadType.ClientSave;
        }

        // Token: 0x06002F6A RID: 12138 RVA: 0x0001E89D File Offset: 0x0001CA9D
        public void Initialise(int dlcID) {
            this.DLCID = dlcID;
        }

        // Token: 0x06002F6B RID: 12139 RVA: 0x000D3030 File Offset: 0x000D1230
        public override void Serialise(BitStreamWriter writer) {
            bool flag = this.DLCID != -1;
            writer.Write(flag);
            if (flag) {
                writer.Write((uint)this.DLCID, 4);
            }
        }

        // Token: 0x06002F6C RID: 12140 RVA: 0x000D3064 File Offset: 0x000D1264
        public override bool Deserialise(BitStreamReader reader) {
            bool flag = reader.ReadBit();
            if (flag) {
                this.DLCID = (int)reader.ReadUInt32(4);
            } else {
                this.DLCID = -1;
            }
            return true;
        }
    }
}
