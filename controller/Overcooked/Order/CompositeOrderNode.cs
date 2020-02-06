using System;

// Token: 0x02000AC6 RID: 2758
[Serializable]
public class CompositeOrderNode : OrderDefinitionNode {
    // Token: 0x060034DD RID: 13533 RVA: 0x000E9ECC File Offset: 0x000E80CC
    public override AssembledDefinitionNode Convert() {
        CompositeAssembledNode compositeAssembledNode = new CompositeAssembledNode();
        compositeAssembledNode.m_composition = this.m_composition.ConvertAll((OrderDefinitionNode x) => x.Convert());
        compositeAssembledNode.m_optional = this.m_optional.ConvertAll((OrderDefinitionNode x) => x.Convert());
        return compositeAssembledNode;
    }

    // Token: 0x04002C38 RID: 11320
    public OrderDefinitionNode[] m_composition = new OrderDefinitionNode[0];

    // Token: 0x04002C39 RID: 11321
    public OrderDefinitionNode[] m_optional = new OrderDefinitionNode[0];
}