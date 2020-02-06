using System;

// Token: 0x020009B8 RID: 2488
[Serializable]
public class MixedCompositeOrderNode : CompositeOrderNode
{
	// Token: 0x060030D1 RID: 12497 RVA: 0x000E5440 File Offset: 0x000E3840
	public override AssembledDefinitionNode Convert()
	{
		MixedCompositeAssembledNode mixedCompositeAssembledNode = new MixedCompositeAssembledNode();
		mixedCompositeAssembledNode.m_composition = this.m_composition.ConvertAll((OrderDefinitionNode x) => x.Convert());
		mixedCompositeAssembledNode.m_optional = this.m_optional.ConvertAll((OrderDefinitionNode x) => x.Convert());
		mixedCompositeAssembledNode.m_progress = this.m_progress;
		return mixedCompositeAssembledNode;
	}

	// Token: 0x04002702 RID: 9986
	public MixedCompositeOrderNode.MixingProgress m_progress = MixedCompositeOrderNode.MixingProgress.Mixed;

	// Token: 0x020009B9 RID: 2489
	public enum MixingProgress
	{
		// Token: 0x04002706 RID: 9990
		Unmixed,
		// Token: 0x04002707 RID: 9991
		Mixed,
		// Token: 0x04002708 RID: 9992
		OverMixed
	}
}
