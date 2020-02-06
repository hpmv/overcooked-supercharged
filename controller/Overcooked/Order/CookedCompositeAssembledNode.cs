using System;
using BitStream;

// Token: 0x02000ACD RID: 2765
public class CookedCompositeAssembledNode : CompositeAssembledNode {
    // Token: 0x06003501 RID: 13569 RVA: 0x000EA860 File Offset: 0x000E8A60
    public override void Serialise(BitStreamWriter writer) {
        writer.Write((uint) this.m_cookingStep.m_uID, 32);
        writer.Write((uint) this.m_progress, 4);
        writer.Write(this.m_recordedProgress != null);
        if (this.m_recordedProgress != null) {
            writer.Write(this.m_recordedProgress.Value);
        }
        base.Serialise(writer);
    }

    // Token: 0x06003502 RID: 13570 RVA: 0x000EA8C8 File Offset: 0x000E8AC8
    public override bool Deserialise(BitStreamReader reader) {
        this.m_cookingStep = new CookingStepData { m_uID = (int) reader.ReadUInt32(32) };
        this.m_progress = (CookedCompositeOrderNode.CookingProgress) reader.ReadUInt32(4);
        if (reader.ReadBit()) {
            this.m_recordedProgress = new float?(reader.ReadFloat32());
        } else {
            this.m_recordedProgress = null;
        }
        return base.Deserialise(reader);
    }

    // Token: 0x06003503 RID: 13571 RVA: 0x000EA92C File Offset: 0x000E8B2C
    protected override bool IsMatch(AssembledDefinitionNode _subject) {
        CookedCompositeAssembledNode cookedCompositeAssembledNode = _subject as CookedCompositeAssembledNode;
        return cookedCompositeAssembledNode != null && base.AssumeTypeMatch(cookedCompositeAssembledNode) && this.m_cookingStep.m_uID == cookedCompositeAssembledNode.m_cookingStep.m_uID && this.m_progress == cookedCompositeAssembledNode.m_progress;
    }

    // Token: 0x06003504 RID: 13572 RVA: 0x000EA980 File Offset: 0x000E8B80
    public override void ReplaceData(AssembledDefinitionNode _node) {
        CookedCompositeAssembledNode cookedCompositeAssembledNode = _node as CookedCompositeAssembledNode;
        this.m_cookingStep = cookedCompositeAssembledNode.m_cookingStep;
        this.m_progress = cookedCompositeAssembledNode.m_progress;
        this.m_recordedProgress = cookedCompositeAssembledNode.m_recordedProgress;
        base.ReplaceData(_node);
    }

    // Token: 0x06003505 RID: 13573 RVA: 0x000EA9C0 File Offset: 0x000E8BC0
    public override AssembledDefinitionNode Simpilfy() {
        bool flag = this.m_progress != CookedCompositeOrderNode.CookingProgress.Raw;
        CompositeAssembledNode compositeAssembledNode;
        if (flag) {
            compositeAssembledNode = new CookedCompositeAssembledNode {
                m_cookingStep = this.m_cookingStep,
                m_progress = this.m_progress
            };
        } else {
            compositeAssembledNode = new CompositeAssembledNode();
        }
        for (int i = 0; i < this.m_composition.Length; i++) {
            AssembledDefinitionNode assembledDefinitionNode = this.m_composition[i].Simpilfy();
            if (assembledDefinitionNode != AssembledDefinitionNode.NullNode) {
                ArrayUtils.PushBack<AssembledDefinitionNode>(ref compositeAssembledNode.m_composition, assembledDefinitionNode);
            }
        }
        for (int j = 0; j < this.m_optional.Length; j++) {
            AssembledDefinitionNode assembledDefinitionNode2 = this.m_optional[j].Simpilfy();
            if (assembledDefinitionNode2 != AssembledDefinitionNode.NullNode) {
                ArrayUtils.PushBack<AssembledDefinitionNode>(ref compositeAssembledNode.m_optional, assembledDefinitionNode2);
            }
        }
        if (compositeAssembledNode.m_optional.Length == 0) {
            if (compositeAssembledNode.m_composition.Length == 1 && !flag) {
                return compositeAssembledNode.m_composition[0];
            }
            if (compositeAssembledNode.m_composition.Length == 1 && compositeAssembledNode.m_composition[0] is CompositeAssembledNode && !(compositeAssembledNode.m_composition[0] is MixedCompositeAssembledNode)) {
                CompositeAssembledNode compositeAssembledNode2 = compositeAssembledNode.m_composition[0] as CompositeAssembledNode;
                compositeAssembledNode.m_composition = compositeAssembledNode2.m_composition;
                compositeAssembledNode.m_optional = compositeAssembledNode.m_optional.Union(compositeAssembledNode2.m_optional);
                return compositeAssembledNode;
            }
            if (compositeAssembledNode.m_composition.Length == 0) {
                return AssembledDefinitionNode.NullNode;
            }
        }
        return compositeAssembledNode;
    }

    // Token: 0x04002C56 RID: 11350
    public CookingStepData m_cookingStep;

    // Token: 0x04002C57 RID: 11351
    public CookedCompositeOrderNode.CookingProgress m_progress = CookedCompositeOrderNode.CookingProgress.Cooked;

    // Token: 0x04002C58 RID: 11352
    public float? m_recordedProgress = new float?(0f);
}