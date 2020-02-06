using System;
using UnityEngine;

// Token: 0x020009B6 RID: 2486
public class ItemOrderNode : OrderDefinitionNode {
	// Token: 0x060030C6 RID: 12486 RVA: 0x000E52AC File Offset: 0x000E36AC
	public override AssembledDefinitionNode Convert() {
		return new ItemAssembledNode(this);
	}

	public float m_heatValue;
}