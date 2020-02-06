using System;

// Token: 0x02000ACB RID: 2763
[Serializable]
public class CookedCompositeOrderNode : CompositeOrderNode {
    // Token: 0x060034FD RID: 13565 RVA: 0x000EA7D8 File Offset: 0x000E89D8
    public override AssembledDefinitionNode Convert() {
        CookedCompositeAssembledNode cookedCompositeAssembledNode = new CookedCompositeAssembledNode();
        cookedCompositeAssembledNode.m_composition = this.m_composition.ConvertAll((OrderDefinitionNode x) => x.Convert());
        cookedCompositeAssembledNode.m_optional = this.m_optional.ConvertAll((OrderDefinitionNode x) => x.Convert());
        cookedCompositeAssembledNode.m_cookingStep = this.m_cookingStep;
        cookedCompositeAssembledNode.m_progress = this.m_progress;
        return cookedCompositeAssembledNode;
    }

    // Token: 0x04002C4E RID: 11342
    public CookingStepData m_cookingStep;

    // Token: 0x04002C4F RID: 11343
    public CookedCompositeOrderNode.CookingProgress m_progress = CookedCompositeOrderNode.CookingProgress.Cooked;

    // Token: 0x02000ACC RID: 2764
    public enum CookingProgress {
        // Token: 0x04002C53 RID: 11347
        Raw,
        // Token: 0x04002C54 RID: 11348
        Cooked,
        // Token: 0x04002C55 RID: 11349
        Burnt
    }
}