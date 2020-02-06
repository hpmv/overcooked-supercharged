using System;
using BitStream;

// Token: 0x020009BA RID: 2490
public class MixedCompositeAssembledNode : CompositeAssembledNode {
	// Token: 0x060030D5 RID: 12501 RVA: 0x000E54EC File Offset: 0x000E38EC
	public override void Serialise(BitStreamWriter writer) {
		writer.Write((uint) this.m_progress, 4);
		writer.Write(this.m_recordedProgress != null);
		if (this.m_recordedProgress != null) {
			writer.Write(this.m_recordedProgress.Value);
		}
		base.Serialise(writer);
	}

	// Token: 0x060030D6 RID: 12502 RVA: 0x000E5540 File Offset: 0x000E3940
	public override bool Deserialise(BitStreamReader reader) {
		this.m_progress = (MixedCompositeOrderNode.MixingProgress) reader.ReadUInt32(4);
		if (reader.ReadBit()) {
			this.m_recordedProgress = new float?(reader.ReadFloat32());
		} else {
			this.m_recordedProgress = null;
		}
		return base.Deserialise(reader);
	}

	// Token: 0x060030D7 RID: 12503 RVA: 0x000E5594 File Offset: 0x000E3994
	protected override bool IsMatch(AssembledDefinitionNode _subject) {
		MixedCompositeAssembledNode mixedCompositeAssembledNode = _subject as MixedCompositeAssembledNode;
		return mixedCompositeAssembledNode != null && base.AssumeTypeMatch(mixedCompositeAssembledNode) && this.m_progress == mixedCompositeAssembledNode.m_progress;
	}

	// Token: 0x060030D8 RID: 12504 RVA: 0x000E55CC File Offset: 0x000E39CC
	public override void ReplaceData(AssembledDefinitionNode _node) {
		MixedCompositeAssembledNode mixedCompositeAssembledNode = _node as MixedCompositeAssembledNode;
		this.m_progress = mixedCompositeAssembledNode.m_progress;
		this.m_recordedProgress = mixedCompositeAssembledNode.m_recordedProgress;
		base.ReplaceData(_node);
	}

	// Token: 0x060030D9 RID: 12505 RVA: 0x000E5600 File Offset: 0x000E3A00
	public override AssembledDefinitionNode Simpilfy() {
		bool flag = this.m_progress != MixedCompositeOrderNode.MixingProgress.Unmixed;
		CompositeAssembledNode compositeAssembledNode;
		if (flag) {
			compositeAssembledNode = new MixedCompositeAssembledNode {
				m_progress = this.m_progress,
					m_recordedProgress = this.m_recordedProgress
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
			if (compositeAssembledNode.m_composition.Length == 1 && compositeAssembledNode.m_composition[0] is CompositeAssembledNode) {
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

	// Token: 0x04002709 RID: 9993
	public MixedCompositeOrderNode.MixingProgress m_progress = MixedCompositeOrderNode.MixingProgress.Mixed;

	// Token: 0x0400270A RID: 9994
	public float? m_recordedProgress = new float?(0f);
}