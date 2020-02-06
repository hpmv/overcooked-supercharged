using System;
using System.Collections.Generic;
using BitStream;

// Token: 0x020009BD RID: 2493
public class NullAssembledNode : AssembledDefinitionNode
{
	// Token: 0x060030EF RID: 12527 RVA: 0x000E577A File Offset: 0x000E3B7A
	public override void Serialise(BitStreamWriter writer)
	{
	}

	// Token: 0x060030F0 RID: 12528 RVA: 0x000E577C File Offset: 0x000E3B7C
	public override bool Deserialise(BitStreamReader reader)
	{
		return true;
	}

	// Token: 0x060030F1 RID: 12529 RVA: 0x000E5780 File Offset: 0x000E3B80
	public override IEnumerator<AssembledDefinitionNode> GetEnumerator()
	{
		yield return this;
		yield break;
	}

	// Token: 0x060030F2 RID: 12530 RVA: 0x000E579B File Offset: 0x000E3B9B
	protected override bool IsMatch(AssembledDefinitionNode _node)
	{
		return _node.GetType() == typeof(NullAssembledNode);
	}

	// Token: 0x060030F3 RID: 12531 RVA: 0x000E57AF File Offset: 0x000E3BAF
	public override AssembledDefinitionNode Simpilfy()
	{
		return AssembledDefinitionNode.NullNode;
	}

	// Token: 0x060030F4 RID: 12532 RVA: 0x000E57B6 File Offset: 0x000E3BB6
	public override int GetNodeCount()
	{
		return 1;
	}
}
