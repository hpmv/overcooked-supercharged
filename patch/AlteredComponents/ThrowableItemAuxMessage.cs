using BitStream;
using System;
using System.Linq;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.AlteredComponents
{
    class ThrowableItemAuxMessage : AuxMessageBase
    {
        public override void Serialise(BitStreamWriter writer)
        {
            var validColliders = m_colliders.Select(GetEntityIdFromCollider).Where(x => x != null).ToArray();
            writer.Write((uint)validColliders.Length, 8);
            foreach (var collider in validColliders)
            {
                collider.Value.entity.Serialise(writer);
                writer.Write((uint)collider.Value.colliderIndex, 8);
            }
        }

        public Collider[] m_colliders;

        private static EncodedCollider? GetEntityIdFromCollider(Collider collider)
        {
            var entry = EntitySerialisationRegistry.GetEntry(collider.gameObject);
            if (entry == null)
            {
                Debug.Log("Could not find a serialisation entry for collider of " + collider.gameObject.name);
                return null;
            }
            var colliders = entry.m_GameObject.GetComponents<Collider>();
            var colliderIndex = Array.IndexOf(colliders, collider);
            if (colliderIndex == -1)
            {
                Debug.Log("Could not find collider index for collider of " + collider.gameObject.name);
                return null;
            }
            return new EncodedCollider
            {
                entity = entry.m_Header,
                colliderIndex = colliderIndex
            };
        }

        public override AuxEntityType GetAuxEntityType()
        {
            return AuxEntityType.ThrowableItemAux;
        }

        private struct EncodedCollider
        {
            public EntityMessageHeader entity;
            public int colliderIndex;
        }
    }
}
