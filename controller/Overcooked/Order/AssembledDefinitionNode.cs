using System;
using System.Collections;
using System.Collections.Generic;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

// Token: 0x02000AD8 RID: 2776
public abstract class AssembledDefinitionNode : IEnumerable<AssembledDefinitionNode>, Serialisable, IEnumerable {
    // Token: 0x06003537 RID: 13623
    public abstract void Serialise(BitStreamWriter writer);

    // Token: 0x06003538 RID: 13624
    public abstract bool Deserialise(BitStreamReader reader);

    // Token: 0x06003539 RID: 13625 RVA: 0x00021DAF File Offset: 0x0001FFAF
    public static bool MatchingAlreadySimple(AssembledDefinitionNode _simpleNode1, AssembledDefinitionNode _simpleNode2) {
        if (_simpleNode1 == null || _simpleNode2 == null) {
            return _simpleNode1 == null && _simpleNode2 == null;
        }
        return _simpleNode1.IsMatch(_simpleNode2);
    }

    // Token: 0x0600353A RID: 13626 RVA: 0x000EB08C File Offset: 0x000E928C
    public static bool Matching(OrderDefinitionNode _node1, OrderDefinitionNode _node2) {
        if (_node1 == null || _node2 == null) {
            return _node1 == null && _node2 == null;
        }
        AssembledDefinitionNode assembledDefinitionNode = _node1.Simpilfy();
        AssembledDefinitionNode node = _node2.Simpilfy();
        return assembledDefinitionNode.IsMatch(node);
    }

    // Token: 0x0600353B RID: 13627 RVA: 0x000EB0E0 File Offset: 0x000E92E0
    public static bool Matching(AssembledDefinitionNode _node1, AssembledDefinitionNode _node2) {
        if (_node1 == null || _node2 == null) {
            return _node1 == null && _node2 == null;
        }
        AssembledDefinitionNode assembledDefinitionNode = _node1.Simpilfy();
        AssembledDefinitionNode node = _node2.Simpilfy();
        return assembledDefinitionNode.IsMatch(node);
    }

    // Token: 0x0600353C RID: 13628 RVA: 0x000EB11C File Offset: 0x000E931C
    public static bool Matching(OrderDefinitionNode _node1, AssembledDefinitionNode _node2) {
        if (_node1 == null || _node2 == null) {
            return _node1 == null && _node2 == null;
        }
        AssembledDefinitionNode assembledDefinitionNode = _node1.Simpilfy();
        AssembledDefinitionNode node = _node2.Simpilfy();
        return assembledDefinitionNode.IsMatch(node);
    }

    // Token: 0x0600353D RID: 13629 RVA: 0x000EB164 File Offset: 0x000E9364
    public static bool Matching(AssembledDefinitionNode _node1, OrderDefinitionNode _node2) {
        if (_node1 == null || _node2 == null) {
            return _node1 == null && _node2 == null;
        }
        AssembledDefinitionNode assembledDefinitionNode = _node1.Simpilfy();
        AssembledDefinitionNode node = _node2.Simpilfy();
        return assembledDefinitionNode.IsMatch(node);
    }

    // Token: 0x0600353E RID: 13630
    public abstract IEnumerator<AssembledDefinitionNode> GetEnumerator();

    // Token: 0x0600353F RID: 13631 RVA: 0x00021DD2 File Offset: 0x0001FFD2
    IEnumerator IEnumerable.GetEnumerator() {
        return this.GetEnumerator();
    }

    // Token: 0x06003541 RID: 13633
    protected abstract bool IsMatch(AssembledDefinitionNode _node);

    // Token: 0x06003542 RID: 13634
    public abstract int GetNodeCount();

    // Token: 0x06003543 RID: 13635
    public abstract AssembledDefinitionNode Simpilfy();

    // Token: 0x06003544 RID: 13636 RVA: 0x00021DE8 File Offset: 0x0001FFE8
    public virtual void ReplaceData(AssembledDefinitionNode _node) {
        this.m_freeObject = _node.m_freeObject;
    }

    // Token: 0x04002C77 RID: 11383
    public static AssembledDefinitionNode NullNode = new NullAssembledNode();

    // Token: 0x04002C78 RID: 11384
    public int m_freeObject = -1;
}