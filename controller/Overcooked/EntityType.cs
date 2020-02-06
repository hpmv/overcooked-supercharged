using System;
using System.Collections.Generic;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009A6 RID: 2470
    public enum EntityType : byte {
        // Token: 0x0400272F RID: 10031
        Unknown,
        // Token: 0x04002730 RID: 10032
        WorldObject,
        // Token: 0x04002731 RID: 10033
        SprayingUtensil,
        // Token: 0x04002732 RID: 10034
        Chef,
        // Token: 0x04002733 RID: 10035
        RespawnBehaviour,
        // Token: 0x04002734 RID: 10036
        Flammable,
        // Token: 0x04002735 RID: 10037
        Workstation,
        // Token: 0x04002736 RID: 10038
        Workable,
        // Token: 0x04002737 RID: 10039
        PlateStation,
        // Token: 0x04002738 RID: 10040
        PlateStack,
        // Token: 0x04002739 RID: 10041
        WashingStation,
        // Token: 0x0400273A RID: 10042
        PhysicalAttach,
        // Token: 0x0400273B RID: 10043
        IngredientContainer,
        // Token: 0x0400273C RID: 10044
        CookingState,
        // Token: 0x0400273D RID: 10045
        MixingState,
        // Token: 0x0400273E RID: 10046
        AttachStation,
        // Token: 0x0400273F RID: 10047
        CookingStation,
        // Token: 0x04002740 RID: 10048
        HeatedStation,
        // Token: 0x04002741 RID: 10049
        ConveyorStation,
        // Token: 0x04002742 RID: 10050
        ConveyorAnimator,
        // Token: 0x04002743 RID: 10051
        TimedQueue,
        // Token: 0x04002744 RID: 10052
        TriggerDisable,
        // Token: 0x04002745 RID: 10053
        TriggerZone,
        // Token: 0x04002746 RID: 10054
        TriggerOnAnimator,
        // Token: 0x04002747 RID: 10055
        TriggerToggleOnAnimator,
        // Token: 0x04002748 RID: 10056
        TriggerMoveSpawn,
        // Token: 0x04002749 RID: 10057
        TriggerDialogue,
        // Token: 0x0400274A RID: 10058
        AnimatorVariable,
        // Token: 0x0400274B RID: 10059
        PlayerSlip,
        // Token: 0x0400274C RID: 10060
        ChefCarry,
        // Token: 0x0400274D RID: 10061
        InputEvent,
        // Token: 0x0400274E RID: 10062
        FlowController,
        // Token: 0x0400274F RID: 10063
        ThrowableItem,
        // Token: 0x04002750 RID: 10064
        Teleportal,
        // Token: 0x04002751 RID: 10065
        Cutscene,
        // Token: 0x04002752 RID: 10066
        Dialogue,
        // Token: 0x04002753 RID: 10067
        TutorialPopup,
        // Token: 0x04002754 RID: 10068
        AttachCatcher,
        // Token: 0x04002755 RID: 10069
        SessionInteractable,
        // Token: 0x04002756 RID: 10070
        WorldMapVanControls,
        // Token: 0x04002757 RID: 10071
        WorldMapVanAvatar,
        // Token: 0x04002758 RID: 10072
        SwitchMapNode,
        // Token: 0x04002759 RID: 10073
        PortalMapNode,
        // Token: 0x0400275A RID: 10074
        LevelPortalMapNode,
        // Token: 0x0400275B RID: 10075
        MiniLevelPortalMapNode,
        // Token: 0x0400275C RID: 10076
        MultiLevelMiniPortalMapNode,
        // Token: 0x0400275D RID: 10077
        WorldPopup,
        // Token: 0x0400275E RID: 10078
        PhysicsObject,
        // Token: 0x0400275F RID: 10079
        RubbishBin,
        // Token: 0x04002760 RID: 10080
        Washable,
        // Token: 0x04002761 RID: 10081
        MixingStation,
        // Token: 0x04002762 RID: 10082
        CookingRegion,
        // Token: 0x04002763 RID: 10083
        RespawnCollider,
        // Token: 0x04002764 RID: 10084
        AutoWorkstation,
        // Token: 0x04002765 RID: 10085
        PlacementItemSpawner,
        // Token: 0x04002766 RID: 10086
        HordeFlowController,
        // Token: 0x04002767 RID: 10087
        HordeTarget,
        // Token: 0x04002768 RID: 10088
        HordeEnemy,
        // Token: 0x04002769 RID: 10089
        HordeLockable,
        // Token: 0x0400276A RID: 10090
        PickupItemSwitcher,
        // Token: 0x0400276B RID: 10091
        Cannon,
        // Token: 0x0400276C RID: 10092
        PilotRotation,
        // Token: 0x0400276D RID: 10093
        TriggerColourCycle,
        // Token: 0x0400276E RID: 10094
        MultiTriggerDisable,
        // Token: 0x0400276F RID: 10095
        COUNT
    }
}

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009A7 RID: 2471
    public class EntityTypeComparer : IEqualityComparer<EntityType> {
        // Token: 0x06002F3F RID: 12095 RVA: 0x00007759 File Offset: 0x00005959
        public bool Equals(EntityType x, EntityType y) {
            return x == y;
        }

        // Token: 0x06002F40 RID: 12096 RVA: 0x000033BA File Offset: 0x000015BA
        public int GetHashCode(EntityType obj) {
            return (int) obj;
        }
    }
}