using System;
using System.Collections.Generic;
using BitStream;

// Token: 0x02000AD2 RID: 2770
public class ItemAssembledNode : AssembledDefinitionNode {
    // Token: 0x06003519 RID: 13593 RVA: 0x00021CC6 File Offset: 0x0001FEC6
    public ItemAssembledNode() { }

    // Token: 0x0600351A RID: 13594 RVA: 0x00021D2A File Offset: 0x0001FF2A
    public ItemAssembledNode(ItemOrderNode _orderNode) {
        this.m_itemOrderNode = _orderNode;
    }

    // Token: 0x0600351B RID: 13595 RVA: 0x00021D39 File Offset: 0x0001FF39
    public override void Serialise(BitStreamWriter writer) {
        writer.Write((uint) this.m_itemOrderNode.m_uID, 32);
    }

    // Token: 0x0600351C RID: 13596 RVA: 0x000EAC70 File Offset: 0x000E8E70
    public override bool Deserialise(BitStreamReader reader) {
        OrderDefinitionNode orderDefinitionNode = new OrderDefinitionNode { m_uID = (int) reader.ReadUInt32(32) };
        ItemOrderNode itemOrderNode = orderDefinitionNode as ItemOrderNode;
        this.m_itemOrderNode = itemOrderNode;
        return true;
    }

    // Token: 0x0600351D RID: 13597 RVA: 0x000EAC9C File Offset: 0x000E8E9C
    public override IEnumerator<AssembledDefinitionNode> GetEnumerator() {
        yield return this;
        yield break;
    }

    // Token: 0x0600351E RID: 13598 RVA: 0x000EACB8 File Offset: 0x000E8EB8
    protected override bool IsMatch(AssembledDefinitionNode _subject) {
        ItemAssembledNode itemAssembledNode = _subject as ItemAssembledNode;
        return itemAssembledNode != null && this.m_itemOrderNode.m_uID == itemAssembledNode.m_itemOrderNode.m_uID;
    }

    // Token: 0x0600351F RID: 13599 RVA: 0x00021D4E File Offset: 0x0001FF4E
    public override AssembledDefinitionNode Simpilfy() {
        return new ItemAssembledNode(this.m_itemOrderNode);
    }

    // Token: 0x06003520 RID: 13600 RVA: 0x000EACEC File Offset: 0x000E8EEC
    public override void ReplaceData(AssembledDefinitionNode _node) {
        ItemAssembledNode itemAssembledNode = _node as ItemAssembledNode;
        this.m_itemOrderNode = itemAssembledNode.m_itemOrderNode;
        base.ReplaceData(_node);
    }

    // Token: 0x06003521 RID: 13601 RVA: 0x00003D34 File Offset: 0x00001F34
    public override int GetNodeCount() {
        return 1;
    }

    // Token: 0x04002C65 RID: 11365
    public ItemOrderNode m_itemOrderNode;
}