using System;
using UnityEngine;

public class OrderDefinitionNode {
    // Token: 0x06003533 RID: 13619 RVA: 0x000EB070 File Offset: 0x000E9270
    public AssembledDefinitionNode Simpilfy() {
        AssembledDefinitionNode assembledDefinitionNode = this.Convert();
        return assembledDefinitionNode.Simpilfy();
    }

    // Token: 0x06003534 RID: 13620
    public virtual AssembledDefinitionNode Convert() {
        throw new NotImplementedException();
    }

    // Token: 0x06003535 RID: 13621 RVA: 0x00021DA1 File Offset: 0x0001FFA1
    public override bool Equals(object _node) {
        return AssembledDefinitionNode.Matching(this, _node as OrderDefinitionNode);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(m_platingPrefab, m_uID);
    }


    // Token: 0x04002C75 RID: 11381
    public int m_platingPrefab = -1;

    public int m_uID;
}