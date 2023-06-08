using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x020008B3 RID: 2227
public class IngredientContainerMessage : Serialisable {
	// Token: 0x17000336 RID: 822
	// (get) Token: 0x06002B68 RID: 11112 RVA: 0x000CACD5 File Offset: 0x000C90D5
	public IngredientContainerMessage.MessageType Type {
		get {
			return this.m_type;
		}
	}

	// Token: 0x17000337 RID: 823
	// (get) Token: 0x06002B69 RID: 11113 RVA: 0x000CACDD File Offset: 0x000C90DD
	public AssembledDefinitionNode[] Contents {
		get {
			return this.m_contents;
		}
	}

	// Token: 0x17000338 RID: 824
	// (get) Token: 0x06002B6A RID: 11114 RVA: 0x000CACE5 File Offset: 0x000C90E5
	public bool ActiveState {
		get {
			return this.m_activeState;
		}
	}

	// Token: 0x06002B6D RID: 11117 RVA: 0x000CAD10 File Offset: 0x000C9110
	public void Serialise(BitStreamWriter writer) {
		writer.Write((uint)this.m_type, 1);
		IngredientContainerMessage.MessageType type = this.m_type;
		if (type != IngredientContainerMessage.MessageType.ActiveState)
		{
			if (type == IngredientContainerMessage.MessageType.ContentsChanged)
			{
				if (this.m_contents != null)
				{
					CompositeAssembledNode compositeAssembledNode = new CompositeAssembledNode();
					compositeAssembledNode.m_composition = this.m_contents;
					writer.Write((uint)AssembledDefinitionNodeFactory.GetNodeType(compositeAssembledNode), 4);
					compositeAssembledNode.Serialise(writer);
				}
			}
		}
		else
		{
			writer.Write(this.m_activeState);
		}
	}

	// Token: 0x06002B6E RID: 11118 RVA: 0x000CAD8C File Offset: 0x000C918C
	public bool Deserialise(BitStreamReader reader) {
		this.m_type = (IngredientContainerMessage.MessageType) reader.ReadByte(1);
		IngredientContainerMessage.MessageType type = this.m_type;
		if (type != IngredientContainerMessage.MessageType.ActiveState) {
			if (type == IngredientContainerMessage.MessageType.ContentsChanged) {
				AssembledDefinitionNode assembledDefinitionNode = AssembledDefinitionNodeFactory.CreateNode((int) reader.ReadUInt32(4));
				CompositeAssembledNode compositeAssembledNode = assembledDefinitionNode as CompositeAssembledNode;
				if (compositeAssembledNode != null) {
					compositeAssembledNode.Deserialise(reader);
					this.m_contents = compositeAssembledNode.m_composition;
				}
			}
		} else {
			this.m_activeState = reader.ReadBit();
		}
		return true;
	}

	// Token: 0x0400226C RID: 8812
	private const int m_kBitsPerType = 1;

	// Token: 0x0400226D RID: 8813
	private IngredientContainerMessage.MessageType m_type;

	// Token: 0x0400226E RID: 8814
	private AssembledDefinitionNode[] m_contents;

	// Token: 0x0400226F RID: 8815
	private bool m_activeState;

	// Token: 0x020008B4 RID: 2228
	public enum MessageType {
		// Token: 0x04002271 RID: 8817
		ContentsChanged,
		// Token: 0x04002272 RID: 8818
		ActiveState
	}
}