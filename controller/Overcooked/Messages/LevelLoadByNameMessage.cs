using System;
using System.Text;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020008C7 RID: 2247
public class LevelLoadByNameMessage : Serialisable
{
	// Token: 0x06002BA7 RID: 11175 RVA: 0x000CC071 File Offset: 0x000CA471
	public void Initialise(GameState start, GameState stop, string _sceneName, bool bUseLoadingScreen)
	{
		this.m_StartLoadGameState = start;
		this.m_HideLoadingScreenGameState = stop;
		this.m_Scene = _sceneName;
		this.UseLoadingScreen = bUseLoadingScreen;
	}

	// Token: 0x06002BA8 RID: 11176 RVA: 0x000CC090 File Offset: 0x000CA490
	public void Serialise(BitStreamWriter writer)
	{
		writer.Write((uint)this.m_StartLoadGameState, 6);
		writer.Write((uint)this.m_HideLoadingScreenGameState, 6);
		writer.Write(this.UseLoadingScreen);
		writer.Write(this.m_Scene, Encoding.ASCII);
	}

	// Token: 0x06002BA9 RID: 11177 RVA: 0x000CC0C9 File Offset: 0x000CA4C9
	public bool Deserialise(BitStreamReader reader)
	{
		this.m_StartLoadGameState = (GameState)reader.ReadUInt32(6);
		this.m_HideLoadingScreenGameState = (GameState)reader.ReadUInt32(6);
		this.UseLoadingScreen = reader.ReadBit();
		this.m_Scene = reader.ReadString(Encoding.ASCII);
		return true;
	}

	// Token: 0x040022E9 RID: 8937
	public GameState m_StartLoadGameState;

	// Token: 0x040022EA RID: 8938
	public GameState m_HideLoadingScreenGameState;

	// Token: 0x040022EB RID: 8939
	public string m_Scene;

	// Token: 0x040022EC RID: 8940
	public bool UseLoadingScreen = true;
}
