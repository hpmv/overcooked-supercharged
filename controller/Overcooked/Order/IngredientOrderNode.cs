using System;
using UnityEngine;

// Token: 0x02000ACE RID: 2766
[Serializable]
public class IngredientOrderNode : OrderDefinitionNode {
    // Token: 0x06003507 RID: 13575 RVA: 0x000EAB44 File Offset: 0x000E8D44
    public override AssembledDefinitionNode Convert() {
        return new IngredientAssembledNode(m_uID);
    }
}