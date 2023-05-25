using System;
using System.Collections.Generic;

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009C3 RID: 2499
    public enum MessageType {
        // Token: 0x040027DD RID: 10205
        Example,
        // Token: 0x040027DE RID: 10206
        LevelLoadByIndex,
        // Token: 0x040027DF RID: 10207
        LevelLoadByName,
        // Token: 0x040027E0 RID: 10208
        EntitySynchronisation,
        // Token: 0x040027E1 RID: 10209
        EntityEvent,
        // Token: 0x040027E2 RID: 10210
        SpawnEntity,
        // Token: 0x040027E3 RID: 10211
        DestroyEntity,
        // Token: 0x040027E4 RID: 10212
        ChefOwnership,
        // Token: 0x040027E5 RID: 10213
        UsersChanged,
        // Token: 0x040027E6 RID: 10214
        UsersAdded,
        // Token: 0x040027E7 RID: 10215
        Input,
        // Token: 0x040027E8 RID: 10216
        GameState,
        // Token: 0x040027E9 RID: 10217
        ChefAvatar,
        // Token: 0x040027EA RID: 10218
        LatencyMeasure,
        // Token: 0x040027EB RID: 10219
        TimeSync,
        // Token: 0x040027EC RID: 10220
        ControllerSettings,
        // Token: 0x040027ED RID: 10221
        TriggerEvent,
        // Token: 0x040027EE RID: 10222
        LobbyServer,
        // Token: 0x040027EF RID: 10223
        LobbyClient,
        // Token: 0x040027F0 RID: 10224
        ChefEvent,
        // Token: 0x040027F1 RID: 10225
        ChefEffect,
        // Token: 0x040027F2 RID: 10226
        MapAvatar,
        // Token: 0x040027F3 RID: 10227
        MapAvatarHorn,
        // Token: 0x040027F4 RID: 10228
        DynamicLevel,
        // Token: 0x040027F5 RID: 10229
        GameSetup,
        // Token: 0x040027F6 RID: 10230
        GameProgressData,
        // Token: 0x040027F7 RID: 10231
        EmoteWheel,
        // Token: 0x040027F8 RID: 10232
        SetupCoopSession,
        // Token: 0x040027F9 RID: 10233
        FlowInput,
        // Token: 0x040027FA RID: 10234
        Achievement,
        // Token: 0x040027FB RID: 10235
        TriggerAudio,
        // Token: 0x040027FC RID: 10236
        SpawnPhysicalAttachment,
        // Token: 0x040027FD RID: 10237
        ResumeWorldObjectSync,
        // Token: 0x040027FE RID: 10238
        ResumeChefPositionSync,
        // Token: 0x040027FF RID: 10239
        ResumePhysicsObjectSync,
        // Token: 0x04002800 RID: 10240
        BossLevel,
        // Token: 0x04002801 RID: 10241
        DestroyChef,
        // Token: 0x04002802 RID: 10242
        HighScores,
        // Token: 0x04002803 RID: 10243
        DestroyEntities,
        // Token: 0x04002804 RID: 10244
        ResumeEntitySync,
        // Token: 0x04002805 RID: 10245
        SessionConfigSync,
        // Token: 0x04002806 RID: 10246
        HordeSpawn,
        // Token: 0x04002807 RID: 10247
        COUNT,

        EntityAuxMessage,  // Mod.
    }
}

namespace Team17.Online.Multiplayer.Messaging {
    // Token: 0x020009C4 RID: 2500
    public class MessageTypeComparer : IEqualityComparer<MessageType> {
        // Token: 0x06002FAC RID: 12204 RVA: 0x000072D5 File Offset: 0x000054D5
        public bool Equals(MessageType x, MessageType y) {
            return x == y;
        }

        // Token: 0x06002FAD RID: 12205 RVA: 0x00002F36 File Offset: 0x00001136
        public int GetHashCode(MessageType obj) {
            return (int) obj;
        }
    }
}