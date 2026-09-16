using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Hpmv;
using SuperchargedPatch.Extensions;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // A fail-closed description of one latent native factory chain.  It is
    // supplied only by the initial-attachment checkpoint after that checkpoint
    // has proved the historical owner, the live factory root and both prefab
    // asset references.  It is deliberately not inserted into the process-wide
    // observed-spawn cache.
    internal sealed class NativeInitialAttachmentSpawnChain
    {
        internal GameObject[] Prefabs;
        internal GameObject[][] Children;
        internal Type[][] Components;
        internal int[] Colliders;
    }

    // Observations contain asset references and copied metadata, not saved live
    // clones. All world mutations below are explicit authoring-warp operations.
    [HarmonyPatch(typeof(NetworkUtils), "ServerSpawnPrefab", new Type[] { typeof(GameObject), typeof(GameObject), typeof(Vector3), typeof(UnityEngine.Quaternion) })]
    public static class NativeSpawnObservationPatch
    {
        public static void Postfix(GameObject _prefab, GameObject __result)
        {
            try { NativeDynamicWarpPlan.Observe(_prefab, __result); }
            catch (Exception error) { NativeDynamicWarpPlan.ObservationError = error.ToString(); }
        }
    }

    public sealed class NativeDynamicWarpPlan
    {
        private sealed class Profile
        {
            public GameObject Prefab;
            public GameObject[] Children;
            public Type[] Components;
            public int Colliders;
            public bool ExactChildren;
            public bool LatentInitialFactory;
        }
        private sealed class Target
        {
            public EntityWarpSpec Spec;
            public EntitySerialisationEntry Existing;
            public EntitySerialisationEntry Root;
            public Profile[] Chain;
            public GameObject Actual;
            public EntityPathReference DeferredInitialAttachmentOwnerPath;
            public bool RetiredLatentIntermediatePhysicsPair;
        }
        private sealed class PendingPhysicsPair
        {
            public IList List;
            public object[] Preimage;
            public int Index;
            public object Pair;
            public EntitySerialisationEntry Entry;
            public Transform Transform;
            public FieldInfo ListField,EntryField,TransformField;
            public bool Retired;
        }
        private sealed class Removal
        {
            public int Id;
            public int ContainerId;
            public GameObject Object;
            public GameObject Container;
            public PendingPhysicsPair PendingPhysics;
        }
        private static readonly Dictionary<GameObject, Profile> profiles = new Dictionary<GameObject, Profile>();
        private static ServerKitchenFlowControllerBase epoch;
        // Native level startup can spawn initial entities before the new flow
        // controller is discoverable.  The first observation after the old flow
        // disappears clears the previous scene and opens this adoption window;
        // the later CLR-null -> new-flow transition belongs to the same scene
        // and must not discard those early native spawn receipts.
        private static bool awaitingFlow = true;
        public static string ObservationError = "";
        public static object LastReceipt;
        private readonly List<Target> targets = new List<Target>();
        private readonly List<Removal> removals = new List<Removal>();
        private readonly Dictionary<int, Target> retainedReferences = new Dictionary<int, Target>();
        private readonly Dictionary<EntityPathReference, EntitySerialisationEntry> references;
        private readonly List<object> spawnedReceipt = new List<object>();
        private readonly List<object> removedReceipt = new List<object>();
        private readonly Action flush;

        public static void Observe(GameObject prefab, GameObject spawned)
        {
            var flow = UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
            RefreshObservationEpoch(flow);
            if (prefab == null || spawned == null) throw new InvalidOperationException("Incomplete native spawn observation.");
            var collection = spawned.GetComponent<SpawnableEntityCollection>();
            var profile = new Profile {
                Prefab = prefab, Children = collection == null ? new GameObject[0] : collection.GetSpawnables().ToArray(),
                Components = spawned.GetComponents<Component>().Where(c => c != null).Select(c => c.GetType()).ToArray(),
                Colliders = spawned.GetComponentsInChildren<Collider>().Length
            };
            Profile previous;
            if (profiles.TryGetValue(prefab, out previous) && (!previous.Children.SequenceEqual(profile.Children)
                || !previous.Components.OrderBy(t => t.FullName).SequenceEqual(profile.Components.OrderBy(t => t.FullName))))
                throw new InvalidOperationException("Native prefab spawn signature changed: " + prefab.name);
            profiles[prefab] = profile;
            foreach (GameObject dead in liveSpawned.Keys.Where(obj => obj == null).ToArray()) liveSpawned.Remove(dead);
            liveSpawned[spawned] = prefab;
        }

        private static void RefreshObservationEpoch(ServerKitchenFlowControllerBase flow)
        {
            if (ReferenceEquals(epoch, flow)) return;
            if (ReferenceEquals(epoch, null) && !ReferenceEquals(flow, null) && awaitingFlow)
            {
                epoch = flow;
                awaitingFlow = false;
                return;
            }
            profiles.Clear();
            liveSpawned.Clear();
            ObservationError = "";
            epoch = flow;
            awaitingFlow = ReferenceEquals(flow, null);
        }

        private NativeDynamicWarpPlan(Dictionary<EntityPathReference, EntitySerialisationEntry> references, Action flush)
        { this.references = references; this.flush = flush; }

        public static NativeDynamicWarpPlan Prepare(WarpSpec warp, Dictionary<EntityPathReference, EntitySerialisationEntry> references, Action flush)
        {
            return Prepare(warp,references,flush,null,null,null);
        }

        internal static NativeDynamicWarpPlan Prepare(WarpSpec warp, Dictionary<EntityPathReference, EntitySerialisationEntry> references,
            Action flush,Func<EntityWarpSpec,EntityPathReference> missingFixedEntityOwnerPath)
        {
            return Prepare(warp,references,flush,missingFixedEntityOwnerPath,null,null);
        }

        internal static NativeDynamicWarpPlan Prepare(WarpSpec warp, Dictionary<EntityPathReference, EntitySerialisationEntry> references,
            Action flush,Func<EntityWarpSpec,EntityPathReference> missingFixedEntityOwnerPath,
            Func<EntityWarpSpec,EntitySerialisationEntry,NativeInitialAttachmentSpawnChain> latentInitialAttachmentFactory)
        {
            return Prepare(warp,references,flush,missingFixedEntityOwnerPath,latentInitialAttachmentFactory,null);
        }

        internal static NativeDynamicWarpPlan Prepare(WarpSpec warp, Dictionary<EntityPathReference, EntitySerialisationEntry> references,
            Action flush,Func<EntityWarpSpec,EntityPathReference> missingFixedEntityOwnerPath,
            Func<EntityWarpSpec,EntitySerialisationEntry,NativeInitialAttachmentSpawnChain> latentInitialAttachmentFactory,
            Func<EntityWarpSpec,bool> ignoreAbsentFixedEntity)
        {
            LastReceipt = null;
            var plan = new NativeDynamicWarpPlan(references, flush);
            var currentIds = new HashSet<int>();
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++) currentIds.Add((int)entries._items[i].m_Header.m_uEntityID);
            var ignored=warp.Entities.Where(value=>ignoreAbsentFixedEntity!=null&&ignoreAbsentFixedEntity(value)).ToArray();
            if(ignored.Any(value=>value==null||!value.__isset.entityId||currentIds.Contains(value.EntityId))
                ||ignored.Select(value=>value.EntityId).Distinct().Count()!=ignored.Length)
                throw new InvalidOperationException("Ignored absent fixed-entity rows are live, incomplete or duplicated.");
            var effectiveEntities=warp.Entities.Where(value=>!ignored.Any(skip=>ReferenceEquals(skip,value))).ToArray();
            var deferred = new Dictionary<EntityWarpSpec,EntityPathReference>();
            var validationIds = new HashSet<int>(currentIds);
            foreach(var spec in effectiveEntities)
            {
                if(!spec.__isset.entityId||currentIds.Contains(spec.EntityId))continue;
                var ownerPath=missingFixedEntityOwnerPath==null?null:missingFixedEntityOwnerPath(spec);
                if(ownerPath==null)continue;
                deferred.Add(spec,ownerPath);
                validationIds.Add(spec.EntityId);
            }
            EntityWarpSpec initialRootRecreation=null;
            if(deferred.Count>1)
                throw new InvalidOperationException("Multiple missing initial attachment containers were qualified.");
            if(deferred.Count==1)
            {
                var ownerPath=deferred.Single().Value;
                var ownerIds=ownerPath.ToThrift().Ids;
                if(ownerIds.Count==1)
                {
                    var matches=effectiveEntities.Where(value=>!value.__isset.entityId
                        &&value.__isset.entityPathReference&&value.EntityPathReference!=null
                        &&value.EntityPathReference.FromThrift().Equals(ownerPath)).ToArray();
                    if(matches.Length!=1)
                        throw new InvalidOperationException("Missing initial attachment container has no unique initial-root owner spawn target.");
                    initialRootRecreation=matches[0];
                }
            }
            NativeDynamicWarpRules.Validate(effectiveEntities.Select(s => new NativeDynamicWarpRules.Target {
                ExistingId = s.__isset.entityId ? (int?)s.EntityId : null,
                Path = s.__isset.entityPathReference && s.EntityPathReference != null ? s.EntityPathReference.Ids.ToArray() : null,
                SpawnPath = s.SpawningPath == null ? null : s.SpawningPath.ToArray(),
                InitialRootRecreation=ReferenceEquals(s,initialRootRecreation)
            }).ToArray(), warp.EntitiesToDelete.ToArray(), validationIds);
            foreach (int id in warp.EntitiesToDelete)
            {
                var obj = EntitySerialisationRegistry.GetEntry((uint)id).m_GameObject;
                // Static scene geometry, chefs and flow have no native spawn receipt.
                // Only runtime instances observed through the real spawner may be deleted.
                if (!liveSpawned.ContainsKey(obj)) throw new InvalidOperationException("Authoring deletion lacks a native spawn receipt: " + id);
                plan.removals.Add(CaptureRemoval(obj));
            }
            var problems = new List<string>();
            foreach (EntityWarpSpec spec in effectiveEntities)
            {
                var target = new Target { Spec = spec };
                if (spec.__isset.entityId)
                {
                    target.Existing = EntitySerialisationRegistry.GetEntry((uint)spec.EntityId);
                    if(target.Existing==null)
                    {
                        if(!deferred.TryGetValue(spec,out target.DeferredInitialAttachmentOwnerPath))
                            throw new InvalidOperationException("Warp entity is no longer registered: "+spec.EntityId);
                    }
                    else target.Actual = target.Existing.m_GameObject;
                }
                else
                {
                    var flow = UnityEngine.Object.FindObjectOfType<ServerKitchenFlowControllerBase>();
                    RefreshObservationEpoch(flow);
                    if (ObservationError.Length != 0 || flow == null || !ReferenceEquals(epoch, flow))
                        throw new InvalidOperationException("Native dynamic spawn observations unavailable: " + ObservationError);
                    target.Root = EntitySerialisationRegistry.GetEntry((uint)spec.SpawningPath[0]);
                    var collection = target.Root.m_GameObject.GetComponent<SpawnableEntityCollection>();
                    if (collection == null) throw new InvalidOperationException("Native spawn root has no ordered prefab collection.");
                    GameObject[] next = collection.GetSpawnables().ToArray();
                    var chain = new List<Profile>();
                    var latent=ReferenceEquals(spec,initialRootRecreation)&&latentInitialAttachmentFactory!=null
                        ?latentInitialAttachmentFactory(spec,target.Root):null;
                    if(latent!=null)
                    {
                        var indices=spec.SpawningPath.Skip(1).ToArray();
                        if(latent.Prefabs==null||latent.Children==null||latent.Components==null
                            ||latent.Colliders==null||latent.Prefabs.Length==0||latent.Prefabs.Length!=indices.Length
                            ||latent.Children.Length!=indices.Length||latent.Components.Length!=indices.Length
                            ||latent.Colliders.Length!=indices.Length)
                            throw new InvalidOperationException("Unobserved or invalid native authoring spawn chain: " + string.Join(".", spec.SpawningPath.Select(x => x.ToString()).ToArray()));
                        next=collection.GetSpawnables().ToArray();chain.Clear();
                        for(int i=0;i<indices.Length;i++)
                        {
                            int index=indices[i];
                            if(index<0||index>=next.Length||next[index]==null
                                ||!ReferenceEquals(next[index],latent.Prefabs[i])||latent.Children[i]==null
                                ||latent.Components[i]==null||latent.Components[i].Any(type=>type==null)
                                ||latent.Colliders[i]<0)
                                throw new InvalidOperationException("Invalid latent initial-attachment factory chain: " + string.Join(".", spec.SpawningPath.Select(x => x.ToString()).ToArray()));
                            chain.Add(new Profile {Prefab=latent.Prefabs[i],Children=latent.Children[i],
                                Components=latent.Components[i],Colliders=latent.Colliders[i],
                                ExactChildren=i<indices.Length-1,LatentInitialFactory=true});
                            next=latent.Children[i];
                        }
                    }
                    else
                    {
                        foreach (int index in spec.SpawningPath.Skip(1))
                        {
                            Profile profile;
                            if (index < 0 || index >= next.Length || next[index] == null || !profiles.TryGetValue(next[index], out profile))
                                throw new InvalidOperationException("Unobserved or invalid native authoring spawn chain: "
                                    +string.Join(".", spec.SpawningPath.Select(x => x.ToString()).ToArray()));
                            chain.Add(profile); next = profile.Children;
                        }
                    }
                    target.Chain = chain.ToArray();
                    if(target.Chain.Any(value=>value.LatentInitialFactory)
                        &&(target.Chain.Length!=2||target.Chain.Any(value=>!value.LatentInitialFactory)))
                        throw new InvalidOperationException("Latent initial-attachment factory must be one exact two-stage chain.");
                    Type[] types = target.Chain.Last().Components;
                    NativeKitchenCheckpoint.ValidateComponentBlocks(t => types.Any(t.IsAssignableFrom), spec, problems);
                }
                plan.targets.Add(target);
            }
            if (problems.Count != 0) throw new InvalidOperationException("Recreated native component blocks mismatch: " + string.Join("; ", problems.ToArray()));
            foreach(Target target in plan.targets.Where(value=>value.DeferredInitialAttachmentOwnerPath!=null))
            {
                var owners=plan.targets.Where(value=>value.Chain!=null&&value.Spec.__isset.entityPathReference
                    &&value.Spec.EntityPathReference.FromThrift().Equals(target.DeferredInitialAttachmentOwnerPath)).ToArray();
                if(owners.Length!=1)
                    throw new InvalidOperationException("Missing initial attachment container has no unique qualified owner spawn target.");
            }
            foreach (Target target in plan.targets) plan.ValidateReferences(target.Spec);
            // A marker left by an earlier rewind cannot be silently aliased to a
            // second live target. Deletions are explicit and complete first.
            var pathOwners = new Dictionary<string, GameObject>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries._items[i];
                if (warp.EntitiesToDelete.Contains((int)entry.m_Header.m_uEntityID)) continue;
                var marker = entry.m_GameObject.GetComponent<EntityPathReferenceMarker>();
                if (marker == null || marker.EntityPath == null) continue;
                string key = marker.EntityPath.ToString();
                if (pathOwners.ContainsKey(key)) throw new InvalidOperationException("Duplicate live native logical-path marker: " + key);
                pathOwners.Add(key, entry.m_GameObject);
            }
            foreach (Target target in plan.targets.Where(t => t.Spec.__isset.entityPathReference))
            {
                GameObject owner;
                if (pathOwners.TryGetValue(target.Spec.EntityPathReference.FromThrift().ToString(), out owner) && owner != target.Actual)
                    throw new InvalidOperationException("Requested logical path is still owned by another retained native entity.");
            }
            return plan;
        }

        private static readonly Dictionary<GameObject, GameObject> liveSpawned = new Dictionary<GameObject, GameObject>();
        private static Removal CaptureRemoval(GameObject obj,bool capturePendingPhysics=false)
        {
            var attachment = obj.GetComponent<PhysicalAttachment>();
            GameObject container = attachment == null || attachment.m_container == null ? null : attachment.m_container.gameObject;
            var result=new Removal { Object = obj, Id = (int)EntitySerialisationRegistry.GetId(obj), Container = container,
                ContainerId = container == null ? 0 : (int)EntitySerialisationRegistry.GetId(container) };
            if(capturePendingPhysics)result.PendingPhysics=CapturePendingPhysicsPair(obj,attachment,container);
            return result;
        }

        private static PendingPhysicsPair CapturePendingPhysicsPair(GameObject obj,PhysicalAttachment attachment,GameObject container)
        {
            var ownerEntry=obj==null?null:EntitySerialisationRegistry.GetEntry(obj);
            var containerEntry=container==null?null:EntitySerialisationRegistry.GetEntry(container);
            if(obj==null||attachment==null||container==null||ownerEntry==null||containerEntry==null
                ||obj.GetComponents<PhysicalAttachment>().Length!=1
                ||container.GetComponents<ServerPhysicsObjectSynchroniser>().Length!=1)
                throw new InvalidOperationException("Latent initial-attachment intermediate physics owner/container differs.");
            var listField=AccessTools.Field(typeof(ServerPhysicsObjectSynchroniser),"ms_ServerPhysicsObjectSytnchroniserTransforms");
            var pairType=typeof(ServerPhysicsObjectSynchroniser).GetNestedType("SerialisationEntryTransformPair",BindingFlags.Public|BindingFlags.NonPublic);
            var entryField=pairType==null?null:AccessTools.Field(pairType,"m_Entry");
            var transformField=pairType==null?null:AccessTools.Field(pairType,"m_Transform");
            var list=listField==null?null:listField.GetValue(null) as IList;
            if(list==null||entryField==null||entryField.FieldType!=typeof(EntitySerialisationEntry)
                ||transformField==null||transformField.FieldType!=typeof(Transform))
                throw new InvalidOperationException("Latent initial-attachment intermediate physics-list contract differs.");
            var preimage=new object[list.Count];list.CopyTo(preimage,0);
            var matches=new List<int>();int entryMatches=0,transformMatches=0;
            for(int i=0;i<preimage.Length;i++)
            {
                var pair=preimage[i];
                if(pair==null)continue;
                bool sameEntry=ReferenceEquals(entryField.GetValue(pair),containerEntry);
                bool sameTransform=ReferenceEquals(transformField.GetValue(pair),container.transform);
                if(sameEntry)entryMatches++;
                if(sameTransform)transformMatches++;
                if(sameEntry&&sameTransform)matches.Add(i);
            }
            // OnDestroy removes by transform alone.  Independent uniqueness is
            // required so its later remove-if-found cannot consume an aliased
            // unrelated row after this exact row has already been retired.
            if(matches.Count!=1||entryMatches!=1||transformMatches!=1)
                throw new InvalidOperationException("Latent initial-attachment intermediate physics-list membership is not unique.");
            int index=matches[0];
            return new PendingPhysicsPair {List=list,Preimage=preimage,Index=index,Pair=preimage[index],
                Entry=containerEntry,Transform=container.transform,ListField=listField,EntryField=entryField,
                TransformField=transformField};
        }

        private Target ReferenceTarget(EntityIdOrRef value)
        {
            if (value == null || value.__isset.entityId == value.__isset.entityPathReference)
                throw new InvalidOperationException("Invalid native authoring reference.");
            Target result = value.__isset.entityId ? targets.SingleOrDefault(t => t.Spec.__isset.entityId && t.Spec.EntityId == value.EntityId)
                : targets.SingleOrDefault(t => t.Spec.__isset.entityPathReference && t.Spec.EntityPathReference.FromThrift().Equals(value.EntityPathReference.FromThrift()));
            if (result == null && value.__isset.entityId && !removals.Any(r => r.Id == value.EntityId))
            {
                // WarpSpec intentionally contains only the mutable subset. A
                // native station may reference an unchanged retained object such
                // as the preplaced extinguisher without giving it a warp block.
                if (!retainedReferences.TryGetValue(value.EntityId, out result))
                {
                    var entry = EntitySerialisationRegistry.GetEntry((uint)value.EntityId);
                    if (entry != null && entry.m_GameObject != null)
                    {
                        result = new Target { Spec = new EntityWarpSpec { EntityId = value.EntityId }, Existing = entry, Actual = entry.m_GameObject };
                        retainedReferences.Add(value.EntityId, result);
                    }
                }
            }
            if (result == null) throw new InvalidOperationException("Dangling native authoring reference: " + value);
            if(result.DeferredInitialAttachmentOwnerPath!=null)
                throw new InvalidOperationException("Missing initial attachment container cannot be used as an authoring entity reference.");
            return result;
        }
        private void ValidateReferences(EntityWarpSpec spec)
        {
            var items = new List<EntityIdOrRef>();
            if (spec.AttachStation != null && spec.AttachStation.Item != null) items.Add(spec.AttachStation.Item);
            if (spec.ChefCarry != null && spec.ChefCarry.CarriedItem != null) items.Add(spec.ChefCarry.CarriedItem);
            if (spec.Workstation != null && spec.Workstation.Item != null) items.Add(spec.Workstation.Item);
            if (spec.PlateReturnStation != null && spec.PlateReturnStation.Stack != null) items.Add(spec.PlateReturnStation.Stack);
            if (spec.Stack != null) { if (spec.Stack.StackContents == null) throw new InvalidOperationException("Missing stack contents."); items.AddRange(spec.Stack.StackContents); }
            foreach (EntityIdOrRef item in items) ReferenceTarget(item);
            if (spec.ThrowableItem != null)
            {
                if (spec.ThrowableItem.ThrowStartColliders == null) throw new InvalidOperationException("Missing native throw collision exclusions.");
                foreach (ColliderRef collider in spec.ThrowableItem.ThrowStartColliders)
                {
                    Target target = ReferenceTarget(collider.Entity);
                    int count = target.Actual != null ? target.Actual.GetComponentsInChildren<Collider>().Length : target.Chain.Last().Colliders;
                    if (collider.ColliderIndex < 0 || collider.ColliderIndex >= count)
                        throw new InvalidOperationException("Native authoring throw collider index is out of range.");
                }
            }
        }

        public void Spawn()
        {
            foreach (Target target in targets.Concat(retainedReferences.Values))
            {
                if(target.DeferredInitialAttachmentOwnerPath!=null)
                {
                    if(target.Existing!=null||target.Actual!=null||EntitySerialisationRegistry.GetEntry((uint)target.Spec.EntityId)!=null)
                        throw new InvalidOperationException("Missing initial attachment container changed after authoring preflight.");
                    continue;
                }
                if (target.Existing != null && EntitySerialisationRegistry.GetEntry(target.Existing.m_Header.m_uEntityID)?.m_GameObject != target.Actual)
                    throw new InvalidOperationException("Retained native entity changed after authoring preflight.");
                if (target.Root != null && EntitySerialisationRegistry.GetEntry(target.Root.m_Header.m_uEntityID)?.m_GameObject != target.Root.m_GameObject)
                    throw new InvalidOperationException("Native spawn root changed after authoring preflight.");
            }
            var transaction = new NativeSpawnTransaction<GameObject>(obj => Destroy(CaptureRemoval(obj)));
            try
            {
                foreach (Target target in targets)
                {
                    if (target.Chain != null)
                    {
                        GameObject source = target.Root.m_GameObject;
                        for (int i = 0; i < target.Chain.Length; i++)
                        {
                            Profile profile = target.Chain[i];
                            var collection = source.GetComponent<SpawnableEntityCollection>();
                            if (collection == null || collection.GetSpawnableEntityByIndex(target.Spec.SpawningPath[i + 1]) != profile.Prefab)
                                throw new InvalidOperationException("Native ordered spawn chain changed after preflight.");
                            GameObject parent = source;
                            GameObject spawned = transaction.Create(() => SpawnOne(parent, profile.Prefab));
                            var physical = spawned.GetComponent<ServerPhysicalAttachment>();
                            if (physical != null) physical.ManualEnable();
                            if (EntitySerialisationRegistry.GetEntry(spawned) == null) throw new InvalidOperationException("Native authoring spawn has no registry entry.");
                            if(profile.ExactChildren)
                            {
                                var spawnedCollection=spawned.GetComponent<SpawnableEntityCollection>();
                                var actualChildren=spawnedCollection==null?null:spawnedCollection.GetSpawnables().ToArray();
                                if(actualChildren==null||!actualChildren.SequenceEqual(profile.Children))
                                    throw new InvalidOperationException("Latent initial-attachment factory registered a different ordered child collection.");
                            }
                            if (i > 0)
                            {
                                if(target.Chain[0].LatentInitialFactory)
                                {
                                    var intermediate=CaptureRemoval(source,true);
                                    transaction.RemoveIntermediate(source,value=>Destroy(intermediate));
                                    if(intermediate.PendingPhysics==null||!intermediate.PendingPhysics.Retired)
                                        throw new InvalidOperationException("Latent initial-attachment intermediate physics pair was not retired.");
                                    target.RetiredLatentIntermediatePhysicsPair=true;
                                }
                                else transaction.RemoveIntermediate(source);
                            }
                            source = spawned;
                        }
                        target.Actual = source;
                        var problems = new List<string>();
                        NativeKitchenCheckpoint.ValidateComponentBlocks(t => source.GetComponent(t) != null, target.Spec, problems);
                        if (problems.Count != 0) throw new InvalidOperationException("Spawned native components differ from preflight: " + string.Join("; ", problems.ToArray()));
                        var finalPhysical = source.GetComponent<PhysicalAttachment>();
                        spawnedReceipt.Add(new Dictionary<string, object> { { "id", (int)EntitySerialisationRegistry.GetId(source) },
                            { "containerId", finalPhysical == null || finalPhysical.m_container == null ? 0 : (int)EntitySerialisationRegistry.GetId(finalPhysical.m_container.gameObject) },
                            { "path", target.Spec.EntityPathReference.Ids.ToArray() }, { "spawnPath", target.Spec.SpawningPath.ToArray() }, { "name", source.name },
                            { "latentInitialFactory", target.Chain.Any(value=>value.LatentInitialFactory) },
                            { "retiredLatentIntermediatePhysicsPair", target.RetiredLatentIntermediatePhysicsPair } });
                    }
                    if (target.Spec.__isset.entityPathReference)
                    {
                        var path = target.Spec.EntityPathReference.FromThrift();
                        references.Add(path, EntitySerialisationRegistry.GetEntry(target.Actual));
                        var marker = target.Actual.GetComponent<EntityPathReferenceMarker>() ?? target.Actual.AddComponent<EntityPathReferenceMarker>();
                        marker.EntityPath = path;
                    }
                }
                transaction.Commit();
            }
            catch (Exception original)
            {
                try { transaction.Cleanup(); }
                catch (Exception cleanup) { throw new InvalidOperationException(original.Message + " | " + cleanup.Message, original); }
                throw;
            }
        }

        internal void BindRecreatedInitialAttachments()
        {
            foreach(Target target in targets.Where(value=>value.DeferredInitialAttachmentOwnerPath!=null))
            {
                if(target.Existing!=null||target.Actual!=null)
                    throw new InvalidOperationException("Missing initial attachment container was rebound twice.");
                EntitySerialisationEntry owner;
                if(!references.TryGetValue(target.DeferredInitialAttachmentOwnerPath,out owner)
                    ||owner==null||owner.m_GameObject==null)
                    throw new InvalidOperationException("Recreated initial attachment owner is absent from the native dynamic plan.");
                var physical=owner.m_GameObject.GetComponent<PhysicalAttachment>();
                var containerObject=physical==null||physical.m_container==null?null:physical.m_container.gameObject;
                var container=containerObject==null?null:EntitySerialisationRegistry.GetEntry(containerObject);
                if(container==null||container.m_Header.m_uEntityID!=(uint)target.Spec.EntityId
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)target.Spec.EntityId),container))
                    throw new InvalidOperationException("Recreated initial attachment container has the wrong fixed-entity identity.");
                target.Existing=container;
                target.Actual=containerObject;
            }
        }

        private GameObject SpawnOne(GameObject parent, GameObject prefab)
        {
            // Native callbacks may throw after registration but before returning
            // the clone. No Unity yield occurs here: new entries belong to this
            // synchronous spawn operation and are removed through native APIs.
            var before = new HashSet<GameObject>();
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++) before.Add(entries._items[i].m_GameObject);
            try { return NetworkUtils.ServerSpawnPrefab(parent, prefab); }
            catch (Exception original)
            {
                var created = new List<GameObject>();
                for (int i = 0; i < entries.Count; i++)
                    if (!before.Contains(entries._items[i].m_GameObject)) created.Add(entries._items[i].m_GameObject);
                var failures = new List<string>();
                foreach (GameObject obj in created)
                    try { if (obj != null && EntitySerialisationRegistry.GetEntry(obj) != null) Destroy(CaptureRemoval(obj)); }
                    catch (Exception error) { failures.Add(error.Message); }
                if (failures.Count != 0) throw new InvalidOperationException(original.Message + " | partial native spawn cleanup: " + string.Join("; ", failures.ToArray()), original);
                throw;
            }
        }

        private void Destroy(Removal removal)
        {
            var entry = EntitySerialisationRegistry.GetEntry((uint)removal.Id);
            if (entry != null && entry.m_GameObject != removal.Object) throw new InvalidOperationException("Native deletion ID changed incarnation.");
            if (entry != null) { NetworkUtils.DestroyObject(removal.Object); flush(); }
            // Native PhysicalAttachment.OnDestroy schedules its container at end
            // of the Unity frame. Send the container's own native destroy receipt
            // now so the paused checkpoint cannot retain a registered orphan.
            if (removal.Container != null && EntitySerialisationRegistry.GetEntry(removal.Container) != null)
            { NetworkUtils.DestroyObject(removal.Container); flush(); }
            if (EntitySerialisationRegistry.GetEntry((uint)removal.Id) != null ||
                (removal.Container != null && EntitySerialisationRegistry.GetEntry(removal.Container) != null))
                throw new InvalidOperationException("Native authoring deletion did not unregister object/container.");
            if(removal.PendingPhysics!=null)RetirePendingPhysicsPair(removal);
            liveSpawned.Remove(removal.Object);
        }

        private static void RetirePendingPhysicsPair(Removal removal)
        {
            var pending=removal.PendingPhysics;
            var current=pending.ListField.GetValue(null) as IList;
            if(pending.Retired||!ReferenceEquals(pending.Entry.m_GameObject,removal.Container)
                ||!ReferenceEquals(pending.Transform,removal.Container.transform)
                ||!ReferenceEquals(pending.EntryField.GetValue(pending.Pair),pending.Entry)
                ||!ReferenceEquals(pending.TransformField.GetValue(pending.Pair),pending.Transform)
                ||EntitySerialisationRegistry.GetEntry(pending.Entry.m_Header.m_uEntityID)!=null)
                throw new InvalidOperationException("Latent initial-attachment intermediate physics pair changed before retirement.");
            NativeDynamicWarpRules.RetireExactCanonicalListValue(current,pending.List,pending.Preimage,
                pending.Index,pending.Pair);
            pending.Retired=true;
        }

        public void DeleteAndVerify()
        {
            foreach (Removal removal in removals)
            {
                Destroy(removal);
                removedReceipt.Add(new Dictionary<string, object> { { "id", removal.Id }, { "containerId", removal.ContainerId }, { "containerRemoved", true } });
            }
            foreach (Target target in targets.Concat(retainedReferences.Values))
                if (target.Actual == null || EntitySerialisationRegistry.GetEntry(target.Actual) == null)
                    throw new InvalidOperationException("Restored native logical target disappeared.");
            LastReceipt = new Dictionary<string, object> { { "spawned", spawnedReceipt.ToArray() }, { "deleted", removedReceipt.ToArray() },
                { "verified", true }, { "scope", "Native spawn/path/removal authoring transaction; raw ID allocator and PhysX internal state are not rewound." } };
        }
    }
}
