using HarmonyLib;
using SuperchargedPatch.AlteredComponents;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Team17.Online.Multiplayer.Messaging;

namespace SuperchargedPatch
{
    [HarmonyPatch(typeof(SerialisationRegistry<EntityType>), "RegisterMessageType")]
    public static class PatchSerialisationRegistry
    {
        public static void Prefix(EntityType type, ref Serialisable message)
        {
            switch (type)
            {
                case EntityType.Cannon:
                    message = new CannonModMessage();
                    break;
            }
        }
    }

    [HarmonyPatch(typeof(EntitySerialisationRegistry), "AddSynchronisedType", new[] {typeof(Type), typeof(SynchroniserConfig)} )]
    public static class PatchEntitySerialisationRegistry
    {
        public static void Prefix(Type gameType, ref SynchroniserConfig config)
        {
            if (gameType == typeof(Cannon))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonMod), typeof(ClientCannonMod));
            } else if (gameType == typeof(CannonCosmeticDecisions))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonCosmeticDecisionsMod), typeof(ClientCannonCosmeticDecisionsMod));
            } else if (gameType == typeof(CannonSessionInteractable))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonSessionInteractableMod), typeof(ClientCannonSessionInteractableMod));
            } else if (gameType == typeof(CannonPlayerHandler))
            {
                config = new SynchroniserConfig(InstancesPerGameObject.Single, typeof(ServerCannonPlayerHandlerMod), typeof(ClientCannonPlayerHandlerMod));
            }
        }
    }
}
