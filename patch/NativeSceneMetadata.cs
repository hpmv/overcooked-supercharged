using System;
using System.Collections.Generic;
using System.Linq;
using Hpmv;
using SuperchargedPatch.Extensions;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Registration happens before some native components finish initialization.
    // Publish one additional observation after the kitchen is ready. The host
    // retains first registration positions separately from this settled metadata.
    public static class NativeSceneMetadata
    {
        public static int Refreshes { get; private set; }
        internal static readonly HashSet<int> InitialRigidBodyIds = new HashSet<int>();
        internal static readonly HashSet<int> InitialPhysicalAttachmentIds = new HashSet<int>();
        private static readonly Dictionary<int, List<EntitySerialisationEntry>> initialRigidBodyLineages =
            new Dictionary<int, List<EntitySerialisationEntry>>();
        private static readonly Dictionary<int, List<EntitySerialisationEntry>> initialPhysicalAttachmentLineages =
            new Dictionary<int, List<EntitySerialisationEntry>>();
        private static readonly Dictionary<int, int> initialAttachmentBodyIds = new Dictionary<int, int>();

        internal sealed class InitialMembership
        {
            internal readonly HashSet<int> RigidBodyIds, PhysicalAttachmentIds;
            internal InitialMembership(HashSet<int> bodies, HashSet<int> attachments)
            { RigidBodyIds = bodies; PhysicalAttachmentIds = attachments; }
        }

        public static void Refresh()
        {
            InitialRigidBodyIds.Clear();
            InitialPhysicalAttachmentIds.Clear();
            initialRigidBodyLineages.Clear();
            initialPhysicalAttachmentLineages.Clear();
            initialAttachmentBodyIds.Clear();
            var data = Injector.Server.CurrentFrameData;
            if (data.EntityRegistry == null) data.EntityRegistry = new List<EntityRegistryData>();
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries._items[i];
                var obj = entry.m_GameObject;
                if (obj == null) continue;
                int id = (int)entry.m_Header.m_uEntityID;
                if (obj.GetComponent<Rigidbody>() != null)
                {
                    if (!InitialRigidBodyIds.Add(id))
                        throw new InvalidOperationException("Initial Rigidbody registration ID is not unique: " + id + ".");
                    initialRigidBodyLineages.Add(id, new List<EntitySerialisationEntry> { entry });
                }
                var physical = obj.GetComponents<PhysicalAttachment>();
                if (physical.Length != 0)
                {
                    if (physical.Length != 1 || !InitialPhysicalAttachmentIds.Add(id))
                        throw new InvalidOperationException("Initial PhysicalAttachment registration is not unique: " + id + ".");
                    initialPhysicalAttachmentLineages.Add(id, new List<EntitySerialisationEntry> { entry });
                }
                var record = new EntityRegistryData {
                    EntityId = (int)entry.m_Header.m_uEntityID, Name = obj.name,
                    Pos = obj.transform.position.ToThrift(), Components = new List<string>(),
                    SyncEntityTypes = new List<int>(), SpawnNames = new List<string>()
                };
                foreach (var component in obj.GetComponents<Component>())
                    if (component != null) record.Components.Add(component.GetType().Name);
                for (int j = 0; j < entry.m_ServerSynchronisedComponents.Count; j++)
                    record.SyncEntityTypes.Add((int)entry.m_ServerSynchronisedComponents._items[j].GetEntityType());
                var collection = obj.GetComponent<SpawnableEntityCollection>();
                if (collection != null)
                    foreach (var spawnable in collection.GetSpawnables())
                        record.SpawnNames.Add(spawnable == null ? "" : spawnable.name);
                data.EntityRegistry.Add(record);
            }
            var pairedBodies = new HashSet<int>();
            foreach (int ownerId in InitialPhysicalAttachmentIds)
            {
                var owner = EntitySerialisationRegistry.GetEntry((uint)ownerId);
                var physical = owner == null || owner.m_GameObject == null
                    ? null : owner.m_GameObject.GetComponent<PhysicalAttachment>();
                var bodyObject = physical == null || physical.m_container == null
                    ? null : physical.m_container.gameObject;
                var body = bodyObject == null ? null : EntitySerialisationRegistry.GetEntry(bodyObject);
                int bodyId = body == null ? 0 : (int)body.m_Header.m_uEntityID;
                if (body == null || bodyObject == null || bodyId <= 0
                    || !InitialRigidBodyIds.Contains(bodyId) || !pairedBodies.Add(bodyId)
                    || !ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)bodyId), body)
                    || !ReferenceEquals(body.m_GameObject, bodyObject))
                    throw new InvalidOperationException("Initial PhysicalAttachment container registration is incomplete or aliased: " + ownerId + ".");
                initialAttachmentBodyIds.Add(ownerId, bodyId);
            }
            Refreshes++;
            // The host may begin reconstructing only at InLevel. Values last
            // changed during loading must still appear in its initial snapshot.
            ActiveStateCollector.RequestFullObservation();
        }

        // Fixed checkpoints retain the immutable load-time ID partition, but
        // select only currently live, proven incarnations.  The only admitted
        // absence is an entire load-time PhysicalAttachment owner/container
        // pair; returned runtime plates therefore remain dynamic sidecar state.
        internal static InitialMembership CaptureCurrentInitialMembership()
        {
            ValidateLineageShape();
            var bodies = new Dictionary<int, EntitySerialisationEntry>();
            var attachments = new Dictionary<int, EntitySerialisationEntry>();
            foreach (int id in InitialRigidBodyIds)
            {
                var current = CurrentLineageEntry(id, initialRigidBodyLineages, "Rigidbody");
                if (current != null) bodies.Add(id, current);
            }
            foreach (int id in InitialPhysicalAttachmentIds)
            {
                var current = CurrentLineageEntry(id, initialPhysicalAttachmentLineages, "PhysicalAttachment");
                if (current != null) attachments.Add(id, current);
            }

            var pairedBodyIds = new HashSet<int>(initialAttachmentBodyIds.Values);
            foreach (int bodyId in InitialRigidBodyIds.Where(value => !pairedBodyIds.Contains(value)))
                if (!bodies.ContainsKey(bodyId))
                    throw new InvalidOperationException("Unpaired initial Rigidbody is absent: " + bodyId + ".");
            foreach (var pair in initialAttachmentBodyIds)
            {
                EntitySerialisationEntry owner, body;
                bool ownerLive = attachments.TryGetValue(pair.Key, out owner);
                bool bodyLive = bodies.TryGetValue(pair.Value, out body);
                if (ownerLive != bodyLive)
                    throw new InvalidOperationException("Initial PhysicalAttachment owner/container pair is only partly present: "
                        + pair.Key + "/" + pair.Value + ".");
                if (!ownerLive) continue;
                var physical = owner.m_GameObject.GetComponents<PhysicalAttachment>();
                var container = physical.Length == 1 ? physical[0].m_container : null;
                if (container == null || !ReferenceEquals(container.gameObject, body.m_GameObject)
                    || !ReferenceEquals(EntitySerialisationRegistry.GetEntry(container.gameObject), body))
                    throw new InvalidOperationException("Initial PhysicalAttachment owner/container link changed: "
                        + pair.Key + "/" + pair.Value + ".");
            }
            return new InitialMembership(new HashSet<int>(bodies.Keys), new HashSet<int>(attachments.Keys));
        }

        internal static void RequireCurrentInitialMembershipSubset(IEnumerable<int> targetBodyIds,
            IEnumerable<int> targetAttachmentIds)
        {
            if (targetBodyIds == null || targetAttachmentIds == null)
                throw new ArgumentNullException("target initial membership");
            var targetBodies = new HashSet<int>(targetBodyIds);
            var targetAttachments = new HashSet<int>(targetAttachmentIds);
            var current = CaptureCurrentInitialMembership();
            if (current.RigidBodyIds.Any(value => !targetBodies.Contains(value))
                || current.PhysicalAttachmentIds.Any(value => !targetAttachments.Contains(value)))
                throw new InvalidOperationException("A live initial owner/body pair is absent from the target checkpoint.");
        }

        internal static void RequireCurrentInitialMembershipExact(IEnumerable<int> targetBodyIds,
            IEnumerable<int> targetAttachmentIds)
        {
            if (targetBodyIds == null || targetAttachmentIds == null)
                throw new ArgumentNullException("target initial membership");
            var current = CaptureCurrentInitialMembership();
            if (!current.RigidBodyIds.SetEquals(targetBodyIds)
                || !current.PhysicalAttachmentIds.SetEquals(targetAttachmentIds))
                throw new InvalidOperationException("Restored initial owner/body membership differs from the target checkpoint.");
        }

        // The controller historically retains the separately registered
        // Rigidbody row after an initial PhysicalAttachment owner/container
        // pair is destroyed.  Qualify only body IDs which are absent as a
        // complete pair in both the exact target checkpoint and the current
        // proven lineage.  The caller must still validate the corresponding
        // WarpSpec as the exact pose-only controller residue before ignoring it.
        internal static HashSet<int> QualifyTargetAbsentInitialBodyIds(
            IEnumerable<int> targetBodyIds, IEnumerable<int> targetAttachmentIds)
        {
            if (targetBodyIds == null || targetAttachmentIds == null)
                throw new ArgumentNullException("target initial membership");
            ValidateLineageShape();
            var bodyRows = targetBodyIds.ToArray();
            var attachmentRows = targetAttachmentIds.ToArray();
            var targetBodies = new HashSet<int>(bodyRows);
            var targetAttachments = new HashSet<int>(attachmentRows);
            if (targetBodies.Count != bodyRows.Length || targetAttachments.Count != attachmentRows.Length
                || targetBodies.Any(value => !InitialRigidBodyIds.Contains(value))
                || targetAttachments.Any(value => !InitialPhysicalAttachmentIds.Contains(value)))
                throw new InvalidOperationException("Target initial owner/body membership is duplicate or outside the immutable load-time partition.");

            var pairedBodies = new HashSet<int>(initialAttachmentBodyIds.Values);
            if (InitialRigidBodyIds.Any(value => !pairedBodies.Contains(value) && !targetBodies.Contains(value)))
                throw new InvalidOperationException("Target checkpoint omits an unpaired initial Rigidbody.");
            foreach (var pair in initialAttachmentBodyIds)
                if (targetAttachments.Contains(pair.Key) != targetBodies.Contains(pair.Value))
                    throw new InvalidOperationException("Target checkpoint contains only half of an initial owner/body pair: "
                        + pair.Key + "/" + pair.Value + ".");

            var current = CaptureCurrentInitialMembership();
            var result = new HashSet<int>();
            foreach (var pair in initialAttachmentBodyIds)
            {
                if (targetAttachments.Contains(pair.Key)) continue;
                if (current.PhysicalAttachmentIds.Contains(pair.Key) || current.RigidBodyIds.Contains(pair.Value))
                    throw new InvalidOperationException("A live initial owner/body pair cannot be treated as absent controller residue: "
                        + pair.Key + "/" + pair.Value + ".");
                result.Add(pair.Value);
            }
            return result;
        }

        // Called only after the qualified historical plate transaction has
        // rebound and verified both managed rows.  Validate both sides first,
        // then replace both lineage lists as one main-thread commit.
        internal static void RebindInitialPhysicalAttachmentLineage(
            EntitySerialisationEntry historicalOwner, EntitySerialisationEntry currentOwner,
            EntitySerialisationEntry historicalBody, EntitySerialisationEntry currentBody)
        {
            ValidateLineageShape();
            if (historicalOwner == null || currentOwner == null || historicalBody == null || currentBody == null)
                throw new InvalidOperationException("Initial PhysicalAttachment lineage rebind is incomplete.");
            int ownerId = (int)historicalOwner.m_Header.m_uEntityID;
            int bodyId = (int)historicalBody.m_Header.m_uEntityID;
            List<EntitySerialisationEntry> ownerLineage, bodyLineage;
            int pairedBodyId;
            if (currentOwner.m_Header.m_uEntityID != historicalOwner.m_Header.m_uEntityID
                || currentBody.m_Header.m_uEntityID != historicalBody.m_Header.m_uEntityID
                || !initialAttachmentBodyIds.TryGetValue(ownerId, out pairedBodyId) || pairedBodyId != bodyId
                || !initialPhysicalAttachmentLineages.TryGetValue(ownerId, out ownerLineage)
                || !initialRigidBodyLineages.TryGetValue(bodyId, out bodyLineage)
                || !ContainsReference(ownerLineage, historicalOwner)
                || !ContainsReference(bodyLineage, historicalBody)
                || ContainsReference(ownerLineage, currentOwner) || ContainsReference(bodyLineage, currentBody)
                || !ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)ownerId), currentOwner)
                || !ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)bodyId), currentBody)
                || currentOwner.m_GameObject == null || currentBody.m_GameObject == null)
                throw new InvalidOperationException("Initial PhysicalAttachment lineage rebind identity differs.");
            var physical = currentOwner.m_GameObject.GetComponents<PhysicalAttachment>();
            if (physical.Length != 1 || physical[0].m_container == null
                || !ReferenceEquals(physical[0].m_container.gameObject, currentBody.m_GameObject))
                throw new InvalidOperationException("Initial PhysicalAttachment lineage rebind pair differs.");
            var nextOwnerLineage = new List<EntitySerialisationEntry>(ownerLineage) { currentOwner };
            var nextBodyLineage = new List<EntitySerialisationEntry>(bodyLineage) { currentBody };
            initialPhysicalAttachmentLineages[ownerId] = nextOwnerLineage;
            initialRigidBodyLineages[bodyId] = nextBodyLineage;
        }

        private static EntitySerialisationEntry CurrentLineageEntry(int id,
            Dictionary<int, List<EntitySerialisationEntry>> lineages, string kind)
        {
            List<EntitySerialisationEntry> lineage;
            if (!lineages.TryGetValue(id, out lineage) || lineage.Count == 0)
                throw new InvalidOperationException("Initial " + kind + " lineage is absent: " + id + ".");
            var current = EntitySerialisationRegistry.GetEntry((uint)id);
            if (current == null) return null;
            if (current.m_GameObject == null || current.m_Header.m_uEntityID != (uint)id
                || !ContainsReference(lineage, current))
                throw new InvalidOperationException("Initial " + kind + " ID was reused by an unproved incarnation: " + id + ".");
            return current;
        }

        private static void ValidateLineageShape()
        {
            if (!InitialRigidBodyIds.SetEquals(initialRigidBodyLineages.Keys)
                || !InitialPhysicalAttachmentIds.SetEquals(initialPhysicalAttachmentLineages.Keys)
                || !InitialPhysicalAttachmentIds.SetEquals(initialAttachmentBodyIds.Keys)
                || initialAttachmentBodyIds.Values.Distinct().Count() != initialAttachmentBodyIds.Count
                || initialAttachmentBodyIds.Values.Any(value => !InitialRigidBodyIds.Contains(value)))
                throw new InvalidOperationException("Initial fixed-body lineage metadata changed.");
        }

        private static bool ContainsReference(IEnumerable<EntitySerialisationEntry> values,
            EntitySerialisationEntry target)
        { return values.Any(value => ReferenceEquals(value, target)); }
    }
}
