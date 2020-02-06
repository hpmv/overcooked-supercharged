using System;
using BitStream;
using Team17.Online.Multiplayer.Messaging;

// Token: 0x02000834 RID: 2100
public class RecipeList {
    // Token: 0x060027D7 RID: 10199 RVA: 0x00019D38 File Offset: 0x00017F38
    public void OnAfterDeserialize() {
        this.Validate();
    }

    // Token: 0x060027D8 RID: 10200 RVA: 0x00019D38 File Offset: 0x00017F38
    public void OnBeforeSerialize() {
        this.Validate();
    }

    // Token: 0x060027D9 RID: 10201 RVA: 0x00002DF9 File Offset: 0x00000FF9
    private void Validate() { }

    // Token: 0x0400203D RID: 8253
    public RecipeList.Entry[] m_recipes;

    // Token: 0x0400203E RID: 8254
    public RecipeList.Entry[] m_freestyle;

    // Token: 0x02000835 RID: 2101
    [Serializable]
    public class Entry : Serialisable {
        // Token: 0x060027DC RID: 10204 RVA: 0x00019D57 File Offset: 0x00017F57
        public void Serialise(BitStreamWriter writer) {
            writer.Write((uint) this.m_order.m_uID, 32);
            writer.Write((uint) this.m_scoreForMeal, 10);
        }

        // Token: 0x060027DD RID: 10205 RVA: 0x000BA5F0 File Offset: 0x000B87F0
        public bool Deserialise(BitStreamReader reader) {
            int id = (int) reader.ReadUInt32(32);
            this.m_order = new OrderDefinitionNode { m_uID = id };
            this.m_scoreForMeal = (int) reader.ReadUInt32(10);
            return true;
        }

        // Token: 0x060027DE RID: 10206 RVA: 0x00019D7A File Offset: 0x00017F7A
        public void Copy(RecipeList.Entry entry) {
            this.m_order = entry.m_order;
            this.m_weight = entry.m_weight;
            this.m_scoreForMeal = entry.m_scoreForMeal;
        }

        // Token: 0x0400203F RID: 8255
        public OrderDefinitionNode m_order;

        // Token: 0x04002040 RID: 8256
        public float m_weight;

        // Token: 0x04002041 RID: 8257
        public int m_scoreForMeal = 1;

        // Token: 0x04002042 RID: 8258
        private const int kBitsPerOrderNode = 32;

        // Token: 0x04002043 RID: 8259
        private const int kBitsPerScore = 10;
    }
}