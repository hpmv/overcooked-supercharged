using System;
using System.Runtime.CompilerServices;

// Token: 0x02000AC3 RID: 2755
public static class AssembledDefinitionNodeFactory {
    // Token: 0x060034D2 RID: 13522 RVA: 0x00021BEA File Offset: 0x0001FDEA
    private static T CreateNode<T>() where T : AssembledDefinitionNode, new() {
        return Activator.CreateInstance<T>();
    }

    // Token: 0x060034D3 RID: 13523 RVA: 0x000E9D68 File Offset: 0x000E7F68
    public static int GetNodeType(AssembledDefinitionNode node) {
        return AssembledDefinitionNodeFactory.m_typeLookup.FindIndex_Predicate((Type x) => x == node.GetType());
    }

    // Token: 0x060034D4 RID: 13524 RVA: 0x00021BF1 File Offset: 0x0001FDF1
    public static AssembledDefinitionNode CreateNode(int type) {
        return AssembledDefinitionNodeFactory.m_createLookup[type]();
    }

    // Token: 0x060034D5 RID: 13525 RVA: 0x000E9D98 File Offset: 0x000E7F98
    // Note: this type is marked as 'beforefieldinit'.
    static AssembledDefinitionNodeFactory() {
        AssembledDefinitionNodeFactory.CreateMethod[] array = new AssembledDefinitionNodeFactory.CreateMethod[6];
        int num = 0;
        if (AssembledDefinitionNodeFactory.fmg_cache0 == null) {
            AssembledDefinitionNodeFactory.fmg_cache0 = new AssembledDefinitionNodeFactory.CreateMethod(AssembledDefinitionNodeFactory.CreateNode<NullAssembledNode>);
        }
        array[num] = AssembledDefinitionNodeFactory.fmg_cache0;
        int num2 = 1;
        if (AssembledDefinitionNodeFactory.fmg_cache1 == null) {
            AssembledDefinitionNodeFactory.fmg_cache1 = new AssembledDefinitionNodeFactory.CreateMethod(AssembledDefinitionNodeFactory.CreateNode<IngredientAssembledNode>);
        }
        array[num2] = AssembledDefinitionNodeFactory.fmg_cache1;
        int num3 = 2;
        if (AssembledDefinitionNodeFactory.fmg_cache2 == null) {
            AssembledDefinitionNodeFactory.fmg_cache2 = new AssembledDefinitionNodeFactory.CreateMethod(AssembledDefinitionNodeFactory.CreateNode<CompositeAssembledNode>);
        }
        array[num3] = AssembledDefinitionNodeFactory.fmg_cache2;
        int num4 = 3;
        if (AssembledDefinitionNodeFactory.fmg_cache3 == null) {
            AssembledDefinitionNodeFactory.fmg_cache3 = new AssembledDefinitionNodeFactory.CreateMethod(AssembledDefinitionNodeFactory.CreateNode<CookedCompositeAssembledNode>);
        }
        array[num4] = AssembledDefinitionNodeFactory.fmg_cache3;
        int num5 = 4;
        if (AssembledDefinitionNodeFactory.fmg_cache4 == null) {
            AssembledDefinitionNodeFactory.fmg_cache4 = new AssembledDefinitionNodeFactory.CreateMethod(AssembledDefinitionNodeFactory.CreateNode<MixedCompositeAssembledNode>);
        }
        array[num5] = AssembledDefinitionNodeFactory.fmg_cache4;
        int num6 = 5;
        if (AssembledDefinitionNodeFactory.fmg_cache5 == null) {
            AssembledDefinitionNodeFactory.fmg_cache5 = new AssembledDefinitionNodeFactory.CreateMethod(AssembledDefinitionNodeFactory.CreateNode<ItemAssembledNode>);
        }
        array[num6] = AssembledDefinitionNodeFactory.fmg_cache5;
        AssembledDefinitionNodeFactory.m_createLookup = array;
    }

    // Token: 0x04002C2E RID: 11310
    public const int kBitsPerType = 4;

    // Token: 0x04002C2F RID: 11311
    private static Type[] m_typeLookup = new Type[] {
        typeof(NullAssembledNode),
        typeof(IngredientAssembledNode),
        typeof(CompositeAssembledNode),
        typeof(CookedCompositeAssembledNode),
        typeof(MixedCompositeAssembledNode),
        typeof(ItemAssembledNode)
    };

    // Token: 0x04002C30 RID: 11312
    private static AssembledDefinitionNodeFactory.CreateMethod[] m_createLookup;

    // Token: 0x04002C31 RID: 11313
    private static AssembledDefinitionNodeFactory.CreateMethod fmg_cache0;

    // Token: 0x04002C32 RID: 11314
    private static AssembledDefinitionNodeFactory.CreateMethod fmg_cache1;

    // Token: 0x04002C33 RID: 11315
    private static AssembledDefinitionNodeFactory.CreateMethod fmg_cache2;

    // Token: 0x04002C34 RID: 11316
    private static AssembledDefinitionNodeFactory.CreateMethod fmg_cache3;

    // Token: 0x04002C35 RID: 11317
    private static AssembledDefinitionNodeFactory.CreateMethod fmg_cache4;

    // Token: 0x04002C36 RID: 11318
    private static AssembledDefinitionNodeFactory.CreateMethod fmg_cache5;

    // Token: 0x02000AC4 RID: 2756
    // (Invoke) Token: 0x060034D7 RID: 13527
    private delegate AssembledDefinitionNode CreateMethod();
}