using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020009BA RID: 2490
public class LevelLoadByIndexMessage : Serialisable {
    // Token: 0x06002F84 RID: 12164 RVA: 0x0001E9D3 File Offset: 0x0001CBD3
    public void Initialise(GameState start, GameState stop, uint uLevelIndex, uint uPlayers, bool bUseLoadingScreen) {
        this.m_StartLoadGameState = start;
        this.m_HideLoadingScreenGameState = stop;
        this.LevelIndex = uLevelIndex;
        this.Players = uPlayers;
        this.UseLoadingScreen = bUseLoadingScreen;
    }

    // Token: 0x06002F85 RID: 12165 RVA: 0x000D3650 File Offset: 0x000D1850
    public void Serialise(BitStreamWriter writer) {
        writer.Write((uint) this.m_StartLoadGameState, 6);
        writer.Write((uint) this.m_HideLoadingScreenGameState, 6);
        writer.Write(this.LevelIndex, 8);
        writer.Write(this.Players, 8);
        writer.Write(this.UseLoadingScreen);
    }

    // Token: 0x06002F86 RID: 12166 RVA: 0x000D36A0 File Offset: 0x000D18A0
    public bool Deserialise(BitStreamReader reader) {
        this.m_StartLoadGameState = (GameState) reader.ReadUInt32(6);
        this.m_HideLoadingScreenGameState = (GameState) reader.ReadUInt32(6);
        this.LevelIndex = reader.ReadUInt32(8);
        this.Players = reader.ReadUInt32(8);
        this.UseLoadingScreen = reader.ReadBit();
        return true;
    }

    // Token: 0x040027BB RID: 10171
    public const uint kInvalidLevel = 255U;

    // Token: 0x040027BC RID: 10172
    public GameState m_StartLoadGameState;

    // Token: 0x040027BD RID: 10173
    public GameState m_HideLoadingScreenGameState;

    // Token: 0x040027BE RID: 10174
    public uint LevelIndex = 255U;

    // Token: 0x040027BF RID: 10175
    public uint Players = 255U;

    // Token: 0x040027C0 RID: 10176
    public bool UseLoadingScreen = true;
}