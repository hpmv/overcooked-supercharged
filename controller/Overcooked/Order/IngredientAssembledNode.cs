using System;
using System.Collections.Generic;
using BitStream;

// Token: 0x02000ACF RID: 2767
public class IngredientAssembledNode : AssembledDefinitionNode {
    // Token: 0x06003508 RID: 13576 RVA: 0x00021CC6 File Offset: 0x0001FEC6
    public IngredientAssembledNode() { }

    // Token: 0x06003509 RID: 13577 RVA: 0x00021CCE File Offset: 0x0001FECE
    public IngredientAssembledNode(IngredientOrderNode _orderNode) {
        this.m_ingriedientOrderNode = _orderNode;
    }

    // Token: 0x0600350A RID: 13578 RVA: 0x00021CDD File Offset: 0x0001FEDD
    public override void Serialise(BitStreamWriter writer) {
        writer.Write((uint) this.m_ingriedientOrderNode.m_uID, 32);
    }

    // Token: 0x0600350B RID: 13579 RVA: 0x000EAB5C File Offset: 0x000E8D5C
    public override bool Deserialise(BitStreamReader reader) {
        OrderDefinitionNode orderDefinitionNode = new OrderDefinitionNode { m_uID = (int) reader.ReadUInt32(32) };
        IngredientOrderNode ingriedientOrderNode = orderDefinitionNode as IngredientOrderNode;
        this.m_ingriedientOrderNode = ingriedientOrderNode;
        return true;
    }

    // Token: 0x0600350C RID: 13580 RVA: 0x000EAB88 File Offset: 0x000E8D88
    public override IEnumerator<AssembledDefinitionNode> GetEnumerator() {
        yield return this;
        yield break;
    }

    // Token: 0x0600350D RID: 13581 RVA: 0x000EABA4 File Offset: 0x000E8DA4
    protected override bool IsMatch(AssembledDefinitionNode _subject) {
        IngredientAssembledNode ingredientAssembledNode = _subject as IngredientAssembledNode;
        return ingredientAssembledNode != null && this.m_ingriedientOrderNode.m_uID == ingredientAssembledNode.m_ingriedientOrderNode.m_uID;
    }

    // Token: 0x0600350E RID: 13582 RVA: 0x00021CF2 File Offset: 0x0001FEF2
    public override AssembledDefinitionNode Simpilfy() {
        return new IngredientAssembledNode(this.m_ingriedientOrderNode);
    }

    // Token: 0x0600350F RID: 13583 RVA: 0x000EABD8 File Offset: 0x000E8DD8
    public override void ReplaceData(AssembledDefinitionNode _node) {
        IngredientAssembledNode ingredientAssembledNode = _node as IngredientAssembledNode;
        this.m_ingriedientOrderNode = ingredientAssembledNode.m_ingriedientOrderNode;
        base.ReplaceData(_node);
    }

    // Token: 0x06003510 RID: 13584 RVA: 0x00003D34 File Offset: 0x00001F34
    public override int GetNodeCount() {
        return 1;
    }

    // Token: 0x04002C5C RID: 11356
    public IngredientOrderNode m_ingriedientOrderNode;
}