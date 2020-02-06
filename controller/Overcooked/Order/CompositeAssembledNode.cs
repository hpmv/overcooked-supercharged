using System;
using System.Collections.Generic;
using BitStream;

// Token: 0x02000AC7 RID: 2759
public class CompositeAssembledNode : AssembledDefinitionNode {
    // Token: 0x060034E1 RID: 13537 RVA: 0x000E9F3C File Offset: 0x000E813C
    public override void Serialise(BitStreamWriter writer) {
        writer.Write((uint) this.m_composition.Length, 5);
        for (int i = 0; i < this.m_composition.Length; i++) {
            writer.Write((uint) AssembledDefinitionNodeFactory.GetNodeType(this.m_composition[i]), 4);
            this.m_composition[i].Serialise(writer);
        }
    }

    // Token: 0x060034E2 RID: 13538 RVA: 0x000E9F94 File Offset: 0x000E8194
    public override bool Deserialise(BitStreamReader reader) {
        bool flag = true;
        if (this.m_composition.Length != 0) {
            this.m_composition = new AssembledDefinitionNode[0];
        }
        int num = (int) reader.ReadUInt32(5);
        for (int i = 0; i < num; i++) {
            AssembledDefinitionNode assembledDefinitionNode = AssembledDefinitionNodeFactory.CreateNode((int) reader.ReadUInt32(4));
            flag &= assembledDefinitionNode.Deserialise(reader);
            Array.Resize<AssembledDefinitionNode>(ref this.m_composition, this.m_composition.Length + 1);
            this.m_composition[this.m_composition.Length - 1] = assembledDefinitionNode;
        }
        return flag;
    }

    // Token: 0x060034E3 RID: 13539 RVA: 0x000EA018 File Offset: 0x000E8218
    public IEnumerator<AssembledDefinitionNode> GetEnumeratorExhaustive() {
        for (int i = 0; i < this.m_composition.Length; i++) {
            yield return this.m_composition[i];
            foreach (AssembledDefinitionNode node in this.m_composition[i]) {
                yield return node;
            }
        }
        yield break;
    }

    // Token: 0x060034E4 RID: 13540 RVA: 0x000EA034 File Offset: 0x000E8234
    public override IEnumerator<AssembledDefinitionNode> GetEnumerator() {
        for (int i = 0; i < this.m_composition.Length; i++) {
            foreach (AssembledDefinitionNode node in this.m_composition[i]) {
                yield return node;
            }
        }
        yield break;
    }

    // Token: 0x060034E5 RID: 13541 RVA: 0x000EA050 File Offset: 0x000E8250
    protected override bool IsMatch(AssembledDefinitionNode _subject) {
        if (_subject.GetType() == typeof(CompositeAssembledNode)) {
            CompositeAssembledNode subject = _subject as CompositeAssembledNode;
            return this.AssumeTypeMatch(subject);
        }
        return this.IsMatch(new CompositeAssembledNode {
            m_composition = new AssembledDefinitionNode[] {
                _subject
            }
        });
    }

    // Token: 0x060034E6 RID: 13542 RVA: 0x000EA0A0 File Offset: 0x000E82A0
    protected bool AssumeTypeMatch(CompositeAssembledNode _subject) {
        return CompositeAssembledNode.Contains(this.m_composition.Union(this.m_optional), _subject.m_composition) && CompositeAssembledNode.Contains(_subject.m_composition.Union(_subject.m_optional), this.m_composition);
    }

    // Token: 0x060034E7 RID: 13543 RVA: 0x000EA0F0 File Offset: 0x000E82F0
    public override AssembledDefinitionNode Simpilfy() {
        CompositeAssembledNode compositeAssembledNode = new CompositeAssembledNode();
        for (int i = 0; i < this.m_composition.Length; i++) {
            if (this.m_composition[i] != AssembledDefinitionNode.NullNode) {
                AssembledDefinitionNode assembledDefinitionNode = this.m_composition[i].Simpilfy();
                if (assembledDefinitionNode != AssembledDefinitionNode.NullNode) {
                    ArrayUtils.PushBack<AssembledDefinitionNode>(ref compositeAssembledNode.m_composition, assembledDefinitionNode);
                }
            }
        }
        for (int j = 0; j < this.m_optional.Length; j++) {
            if (this.m_optional[j] != AssembledDefinitionNode.NullNode) {
                AssembledDefinitionNode assembledDefinitionNode2 = this.m_optional[j].Simpilfy();
                if (assembledDefinitionNode2 != AssembledDefinitionNode.NullNode) {
                    ArrayUtils.PushBack<AssembledDefinitionNode>(ref compositeAssembledNode.m_optional, assembledDefinitionNode2);
                }
            }
        }
        if (compositeAssembledNode.m_optional.Length == 0) {
            if (compositeAssembledNode.m_composition.Length == 1) {
                return compositeAssembledNode.m_composition[0];
            }
            if (compositeAssembledNode.m_composition.Length == 0) {
                return AssembledDefinitionNode.NullNode;
            }
        }
        return compositeAssembledNode;
    }

    // Token: 0x060034E8 RID: 13544 RVA: 0x000EA1EC File Offset: 0x000E83EC
    public static bool Contains(AssembledDefinitionNode[] _superSet, AssembledDefinitionNode[] _subSet) {
        bool[] array = new bool[_superSet.Length];
        for (int i = 0; i < _subSet.Length; i++) {
            bool flag = false;
            for (int j = 0; j < _superSet.Length; j++) {
                if (!array[j] && (_subSet[i] == null || AssembledDefinitionNode.MatchingAlreadySimple(_subSet[i], _superSet[j]))) {
                    array[j] = true;
                    flag = true;
                    break;
                }
            }
            if (!flag) {
                return false;
            }
        }
        return true;
    }

    // Token: 0x060034E9 RID: 13545 RVA: 0x000EA260 File Offset: 0x000E8460
    public override void ReplaceData(AssembledDefinitionNode _node) {
        CompositeAssembledNode compositeAssembledNode = _node as CompositeAssembledNode;
        this.m_composition = compositeAssembledNode.m_composition;
        this.m_optional = compositeAssembledNode.m_optional;
        base.ReplaceData(_node);
    }

    // Token: 0x060034ED RID: 13549 RVA: 0x000EA480 File Offset: 0x000E8680
    public override int GetNodeCount() {
        int num = 1;
        for (int i = 0; i < this.m_composition.Length; i++) {
            num += this.m_composition[i].GetNodeCount();
        }
        return num;
    }

    // Token: 0x04002C3C RID: 11324
    public AssembledDefinitionNode[] m_composition = new AssembledDefinitionNode[0];

    // Token: 0x04002C3D RID: 11325
    public AssembledDefinitionNode[] m_optional = new AssembledDefinitionNode[0];
}