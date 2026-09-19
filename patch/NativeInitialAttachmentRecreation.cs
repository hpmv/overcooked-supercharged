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
    // The initial-attachment core cannot prove scheduler/free-ID history by
    // itself.  A scheduler sidecar may issue this single-attempt capability
    // only after validating the complete target-vs-live membership delta.
    // NativeKitchenCheckpoint clears it at every attempt boundary and the
    // recreation plan consumes it before any authoring mutation.
    public static class NativeInitialAttachmentDeletionAuthorization
    {
        private static WarpSpec authorizedWarp;
        private static int ownerId,containerId;
        private static int[] deletionOwnerIds,concurrentHistoricalEntityIds;

        public static void Authorize(WarpSpec warp,int historicalOwnerId,int historicalContainerId,
            IEnumerable<int> futureDeletionOwnerIds)
        {
            Authorize(warp,historicalOwnerId,historicalContainerId,futureDeletionOwnerIds,new int[0]);
        }

        public static void Authorize(WarpSpec warp,int historicalOwnerId,int historicalContainerId,
            IEnumerable<int> futureDeletionOwnerIds,IEnumerable<int> concurrentMissingHistoricalEntityIds)
        {
            if(warp==null||historicalOwnerId<=0||historicalContainerId<=0||futureDeletionOwnerIds==null
                ||concurrentMissingHistoricalEntityIds==null)
                throw new InvalidOperationException("Initial-attachment deletion authorization is incomplete.");
            var ids=futureDeletionOwnerIds.ToArray();
            var concurrent=concurrentMissingHistoricalEntityIds.ToArray();
            if((ids.Length==0&&concurrent.Length==0)
                ||ids.Any(value=>value<=0||value==historicalOwnerId||value==historicalContainerId)
                ||ids.Distinct().Count()!=ids.Length)
                throw new InvalidOperationException("Initial-attachment deletion authorization is not a distinct future-only owner set.");
            // The only admitted composite currently contains one additional
            // historical PhysicalAttachment owner/body pair.  Its identities
            // are absent now, but will be recreated by the independently
            // authenticated scheduler transaction before core finalization.
            if(concurrent.Length!=0&&concurrent.Length!=2
                ||concurrent.Any(value=>value<=0||value==historicalOwnerId||value==historicalContainerId
                    ||EntitySerialisationRegistry.GetEntry((uint)value)!=null)
                ||concurrent.Distinct().Count()!=concurrent.Length
                ||concurrent.Any(value=>ids.Contains(value)))
                throw new InvalidOperationException("Initial-attachment concurrent historical recreation set is not one exact absent pair.");
            authorizedWarp=warp;ownerId=historicalOwnerId;containerId=historicalContainerId;
            deletionOwnerIds=ids.OrderBy(value=>value).ToArray();
            concurrentHistoricalEntityIds=concurrent.OrderBy(value=>value).ToArray();
        }

        internal static bool Consume(WarpSpec warp,int historicalOwnerId,int historicalContainerId,
            out int[] futureDeletionOwnerIds,out int[] concurrentMissingHistoricalEntityIds)
        {
            var expected=deletionOwnerIds;var concurrent=concurrentHistoricalEntityIds;
            bool admitted=ReferenceEquals(warp,authorizedWarp)&&ownerId==historicalOwnerId
                &&containerId==historicalContainerId&&expected!=null&&concurrent!=null&&warp.EntitiesToDelete!=null
                &&warp.EntitiesToDelete.OrderBy(value=>value).SequenceEqual(expected);
            futureDeletionOwnerIds=admitted?(int[])expected.Clone():null;
            concurrentMissingHistoricalEntityIds=admitted?(int[])concurrent.Clone():null;
            Clear();
            return admitted;
        }

        internal static void Clear()
        {
            authorizedWarp=null;ownerId=0;containerId=0;deletionOwnerIds=null;concurrentHistoricalEntityIds=null;
        }
    }

    // A delivered initial plate is destroyed together with its separately
    // registered Rigidbody container.  The ordinary WarpSpec already carries
    // the observed spawn path needed to reconstruct that logical entity.  This
    // plan admits only one exact missing initial PhysicalAttachment/body pair
    // and transfers checkpoint values onto the freshly registered managed
    // objects after the native spawner has recreated them.
    internal sealed class NativeInitialAttachmentRecreation
    {
        internal sealed class TopologySnapshot
        {
            internal sealed class IngredientRecord
            {
                internal object Value;
                internal EntitySerialisationEntry Entry;
            }
            internal sealed class PhysicsRecord
            {
                internal object Value;
                internal EntitySerialisationEntry Entry;
                internal Transform Transform;
            }
            internal sealed class PlateRecord
            {
                internal EntitySerialisationEntry Entry;
                internal Type[] Components,AuthoringComponents,ColliderTypes;
                internal PlatingStepData Step;
                internal GameObject ModelPrefab;
            }
            internal sealed class RestoreTransaction
            {
                internal FutureDeletion[] Deletions;
                internal HistoricalRecreation[] ConcurrentRecreations;
                internal CanonicalRebind InitialRebind;
                internal CanonicalRebind[] ConcurrentRebinds;
                internal object[] RegistryPreimage,IngredientPreimage,PhysicsPreimage;
                internal int RetiredFuturePhysicsPairs,AlreadyRetiredFuturePhysicsPairs;
                internal bool Rebound,Finalized;
            }
            internal sealed class FutureDeletion
            {
                internal int OwnerId,ContainerId;
                internal EntitySerialisationEntry OwnerEntry,ContainerEntry;
                internal object Ingredient,Physics;
                internal Transform PhysicsTransform;
            }
            internal sealed class PhysicalPairRecord
            {
                internal EntitySerialisationEntry OwnerEntry,ContainerEntry;
                internal Transform OwnerTransform;
                internal Type[] OwnerComponents,ContainerComponents,OwnerColliderTypes,ContainerColliderTypes;
            }
            internal sealed class HistoricalRecreation
            {
                internal int OwnerId,ContainerId;
                internal EntitySerialisationEntry OwnerEntry,ContainerEntry;
                internal IngredientRecord Ingredient;
                internal PhysicsRecord Physics;
                internal PhysicalPairRecord Pair;
            }
            internal sealed class CanonicalRebind
            {
                internal int OwnerIndex,ContainerIndex;
                internal EntitySerialisationEntry HistoricalOwner,HistoricalContainer,Owner,Container;
                internal IngredientRecord IngredientRecord;
                internal object IngredientValue;
                internal PhysicsRecord PhysicsRecord;
                internal object PhysicsValue;
                internal Transform PhysicsTransform;
                internal PlateRecord PlateRecord;
                internal PhysicalPairRecord PairRecord;
            }

            private readonly FastList<EntitySerialisationEntry> registry;
            private readonly EntitySerialisationEntry[] registryEntries;
            private readonly string[] registryNames;
            private readonly IList ingredientList,physicsList;
            private readonly IngredientRecord[] ingredients;
            private readonly PhysicsRecord[] physics;
            private readonly PhysicalPairRecord[] physicalPairs;
            private readonly PlateRecord[] plates;
            private readonly FieldInfo physicsEntry,physicsTransform;

            private TopologySnapshot(FastList<EntitySerialisationEntry> registry,
                EntitySerialisationEntry[] registryEntries,string[] registryNames,IList ingredientList,
                IngredientRecord[] ingredients,IList physicsList,PhysicsRecord[] physics,
                FieldInfo physicsEntry,FieldInfo physicsTransform,PhysicalPairRecord[] physicalPairs,
                PlateRecord[] plates)
            {
                this.registry=registry;this.registryEntries=registryEntries;this.registryNames=registryNames;
                this.ingredientList=ingredientList;this.ingredients=ingredients;
                this.physicsList=physicsList;this.physics=physics;
                this.physicsEntry=physicsEntry;this.physicsTransform=physicsTransform;
                this.physicalPairs=physicalPairs;this.plates=plates;
            }

            internal static TopologySnapshot Capture()
            {
                var registry=EntitySerialisationRegistry.m_EntitiesList;
                if(registry==null||registry._items==null)
                    throw new InvalidOperationException("Native entity registry list is absent.");
                var ingredientField=AccessTools.Field(typeof(ServerIngredientContainer),"ms_AllIngredientContainers");
                var physicsField=AccessTools.Field(typeof(ServerPhysicsObjectSynchroniser),"ms_ServerPhysicsObjectSytnchroniserTransforms");
                var ingredientList=ingredientField==null?null:ingredientField.GetValue(null) as IList;
                var physicsList=physicsField==null?null:physicsField.GetValue(null) as IList;
                if(ingredientList==null||physicsList==null)
                    throw new InvalidOperationException("Native ingredient/physics canonical list contract differs.");
                var pairType=typeof(ServerPhysicsObjectSynchroniser).GetNestedType("SerialisationEntryTransformPair",BindingFlags.Public|BindingFlags.NonPublic);
                var physicsEntry=pairType==null?null:AccessTools.Field(pairType,"m_Entry");
                var physicsTransform=pairType==null?null:AccessTools.Field(pairType,"m_Transform");
                if(physicsEntry==null||physicsEntry.FieldType!=typeof(EntitySerialisationEntry)
                    ||physicsTransform==null||physicsTransform.FieldType!=typeof(Transform))
                    throw new InvalidOperationException("Native physics canonical pair contract differs.");
                var ingredientRecords=new IngredientRecord[ingredientList.Count];
                for(int i=0;i<ingredientRecords.Length;i++)
                {
                    var value=ingredientList[i] as ServerIngredientContainer;
                    var entry=value==null?null:EntitySerialisationRegistry.GetEntry(value.gameObject);
                    if(value==null||entry==null)throw new InvalidOperationException("Native ingredient canonical list contains an unregistered value.");
                    ingredientRecords[i]=new IngredientRecord {Value=value,Entry=entry};
                }
                var physicsRecords=new PhysicsRecord[physicsList.Count];
                for(int i=0;i<physicsRecords.Length;i++)
                {
                    var value=physicsList[i];
                    var entry=value==null?null:(EntitySerialisationEntry)physicsEntry.GetValue(value);
                    var transform=value==null?null:(Transform)physicsTransform.GetValue(value);
                    if(value==null||entry==null||transform==null||entry.m_GameObject==null
                        ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID),entry))
                        throw new InvalidOperationException("Native physics canonical list contains an incomplete or deferred-destroy pair.");
                    physicsRecords[i]=new PhysicsRecord {Value=value,Entry=entry,Transform=transform};
                }
                var registryEntries=registry._items.Take(registry.Count).ToArray();
                if(registryEntries.Any(value=>value==null||value.m_GameObject==null))
                    throw new InvalidOperationException("Native entity registry contains an incomplete value.");
                var physicalPairs=registryEntries.Select(CapturePhysicalPair).Where(value=>value!=null).ToArray();
                var plates=registryEntries.Select(CapturePlate).Where(value=>value!=null).ToArray();
                return new TopologySnapshot(registry,registryEntries,registryEntries.Select(value=>value.m_GameObject.name).ToArray(),
                    ingredientList,ingredientRecords,physicsList,physicsRecords,physicsEntry,physicsTransform,
                    physicalPairs,plates);
            }

            private static PhysicalPairRecord CapturePhysicalPair(EntitySerialisationEntry entry)
            {
                var obj=entry.m_GameObject;
                var attachments=obj.GetComponents<PhysicalAttachment>();
                if(attachments.Length!=1||attachments[0].m_container==null)return null;
                var containerObject=attachments[0].m_container.gameObject;
                var containerEntry=containerObject==null?null:EntitySerialisationRegistry.GetEntry(containerObject);
                if(containerEntry==null||!ReferenceEquals(containerEntry.m_GameObject,containerObject))return null;
                return new PhysicalPairRecord {OwnerEntry=entry,ContainerEntry=containerEntry,OwnerTransform=obj.transform,
                    OwnerComponents=ComponentTypes(obj),ContainerComponents=ComponentTypes(containerObject),
                    OwnerColliderTypes=ColliderTypes(obj),
                    // A newly spawned PhysicalAttachment is temporarily below
                    // its container until the ordinary attachment callback
                    // restores the target parent.  Record only colliders owned
                    // independently by the container so this immutable
                    // topology does not depend on that lifecycle phase.
                    ContainerColliderTypes=ColliderTypesExcluding(containerObject,obj.transform)};
            }

            private static PlateRecord CapturePlate(EntitySerialisationEntry entry)
            {
                var obj=entry.m_GameObject;var plate=obj.GetComponent<Plate>();
                if(plate==null)return null;
                var model=obj.GetComponent<InheritFromModelPrefab>();
                return new PlateRecord {Entry=entry,Components=ComponentTypes(obj),
                    AuthoringComponents=AuthoringComponentTypes(obj),
                    ColliderTypes=ColliderTypes(obj),Step=plate.m_platingStep,
                    ModelPrefab=model==null?null:model.GetParentPrefab()};
            }

            private static Type[] ComponentTypes(GameObject obj)
            {return obj.GetComponents<Component>().Where(value=>value!=null).Select(value=>value.GetType()).ToArray();}
            private static Type[] AuthoringComponentTypes(GameObject obj)
            {return ComponentTypes(obj).Where(value=>!RuntimeComponent(value)).ToArray();}
            private static Type[] ColliderTypes(GameObject obj)
            {return obj.GetComponentsInChildren<Collider>(true).Where(value=>value!=null).Select(value=>value.GetType()).OrderBy(value=>value.FullName).ToArray();}
            private static Type[] ColliderTypesExcluding(GameObject obj,Transform excludedRoot)
            {return obj.GetComponentsInChildren<Collider>(true).Where(value=>value!=null
                &&!HasAncestor(value.transform,excludedRoot)).Select(value=>value.GetType())
                .OrderBy(value=>value.FullName).ToArray();}
            private static bool HasAncestor(Transform value,Transform ancestor)
            {for(var cursor=value;cursor!=null;cursor=cursor.parent)if(ReferenceEquals(cursor,ancestor))return true;return false;}
            private static Type[] BehavioralComponentTypes(Type[] values)
            {return values.Where(value=>value!=typeof(EntityPathReferenceMarker)).ToArray();}
            private static bool RuntimeComponent(Type type)
            {
                return typeof(ServerSynchroniserBase).IsAssignableFrom(type)
                    ||typeof(ClientSynchroniserBase).IsAssignableFrom(type)
                    // Entity registration also installs several client-side
                    // helper components which do not derive from
                    // ClientSynchroniserBase.  ComponentCacheRegistry adds
                    // CachedObject after registration.  None of these are
                    // serialized members of the plate prefab, so retain them
                    // in the exact runtime component snapshot but omit them
                    // only from the authoring-asset signature comparison.
                    ||type==typeof(RendererSceneInfo)||type==typeof(EmptyLerp)
                    ||type==typeof(CachedObject)||type==typeof(SpawnableEntityCollection)
                    ||type==typeof(EntityPathReferenceMarker);
            }

            internal static string ComponentDifference(Type[] actual,Type[] expected)
            {
                if(actual==null||expected==null)return "one signature is null";
                int common=Math.Min(actual.Length,expected.Length);
                for(int i=0;i<common;i++)if(actual[i]!=expected[i])
                    return "index "+i+" expected "+TypeName(expected[i])+" but found "+TypeName(actual[i])
                        +" (expectedCount="+expected.Length+", actualCount="+actual.Length+")";
                if(actual.Length!=expected.Length)
                    return "matching prefix length "+common+" but expectedCount="+expected.Length
                        +" and actualCount="+actual.Length;
                return null;
            }

            private static string TypeName(Type value)
            {return value==null?"<null>":value.FullName;}

            internal PlateRecord Plate(EntitySerialisationEntry entry)
            {
                var matches=plates.Where(value=>ReferenceEquals(value.Entry,entry)).ToArray();
                if(matches.Length>1)throw new InvalidOperationException("Initial plate checkpoint membership is ambiguous.");
                return matches.SingleOrDefault();
            }

            internal void ValidateSurvivingPlatePeers(PlateRecord target)
            {
                if(target==null||target.Components==null||target.AuthoringComponents==null
                    ||target.ColliderTypes==null||target.Step==null||target.ModelPrefab==null)
                    throw new InvalidOperationException("Initial plate factory checkpoint signature is incomplete.");
                var peers=plates.Where(value=>!ReferenceEquals(value,target)
                    &&ReferenceEquals(value.Step,target.Step)&&ReferenceEquals(value.ModelPrefab,target.ModelPrefab)
                    &&value.Components.SequenceEqual(target.Components)
                    &&value.AuthoringComponents.SequenceEqual(target.AuthoringComponents)
                    &&value.ColliderTypes.SequenceEqual(target.ColliderTypes)).ToArray();
                if(peers.Length==0)throw new InvalidOperationException("Initial plate factory has no checkpoint-identical surviving peer.");
                foreach(var peer in peers)
                {
                    var entry=EntitySerialisationRegistry.GetEntry(peer.Entry.m_Header.m_uEntityID);
                    var obj=entry==null?null:entry.m_GameObject;
                    var plate=obj==null?null:obj.GetComponent<Plate>();
                    var model=obj==null?null:obj.GetComponent<InheritFromModelPrefab>();
                    if(!ReferenceEquals(entry,peer.Entry)||obj==null
                        ||!ComponentTypes(obj).SequenceEqual(peer.Components)
                        ||!AuthoringComponentTypes(obj).SequenceEqual(peer.AuthoringComponents)
                        ||!ColliderTypes(obj).SequenceEqual(peer.ColliderTypes)
                        ||plate==null||!ReferenceEquals(plate.m_platingStep,peer.Step)
                        ||model==null||!ReferenceEquals(model.GetParentPrefab(),peer.ModelPrefab))
                        throw new InvalidOperationException("Initial plate factory surviving-peer signature changed.");
                }
            }

            internal static Type[] FactoryAuthoringComponents(GameObject obj)
            {return AuthoringComponentTypes(obj);}
            internal static Type[] FactoryColliderTypes(GameObject obj)
            {return ColliderTypes(obj);}

            internal RestoreTransaction ValidateMissing(NativeAttachmentPoseCheckpoint.Snapshot attachment,
                NativeBodyPoseCheckpoint.Snapshot body,int[] futureDeletionOwnerIds,
                int[] concurrentMissingHistoricalEntityIds)
            {
                if(futureDeletionOwnerIds==null||concurrentMissingHistoricalEntityIds==null)
                    throw new InvalidOperationException("Initial PhysicalAttachment future-deletion topology is absent.");
                if(!ReferenceEquals(EntitySerialisationRegistry.m_EntitiesList,registry)
                    ||registryEntries.Count(value=>ReferenceEquals(value,attachment.Entry))!=1
                    ||registryEntries.Count(value=>ReferenceEquals(value,body.Entry))!=1)
                    throw new InvalidOperationException("Initial PhysicalAttachment registry topology checkpoint differs.");
                var missingIngredient=ingredients.Where(value=>ReferenceEquals(value.Entry,attachment.Entry)).ToArray();
                var missingPhysics=physics.Where(value=>ReferenceEquals(value.Entry,body.Entry)
                    &&ReferenceEquals(value.Transform,body.Transform)).ToArray();
                if(missingIngredient.Length!=1||missingPhysics.Length!=1)
                    throw new InvalidOperationException("Initial PhysicalAttachment canonical-list membership is not unique.");
                var concurrent=CaptureConcurrentHistoricalRecreations(concurrentMissingHistoricalEntityIds,
                    attachment,body);
                var deletions=CaptureFutureDeletions(futureDeletionOwnerIds,attachment,body);
                var registryPreimage=CurrentRegistry();
                var ingredientPreimage=Current(ingredientList);
                var physicsPreimage=Current(physicsList);
                RequireExactExtension(registryPreimage,registryEntries.Where(value=>!ReferenceEquals(value,attachment.Entry)
                    &&!ReferenceEquals(value,body.Entry)
                    &&!concurrent.Any(pair=>ReferenceEquals(value,pair.OwnerEntry)
                        ||ReferenceEquals(value,pair.ContainerEntry))).Cast<object>().ToArray(),deletions.SelectMany(value=>
                        new object[]{value.OwnerEntry,value.ContainerEntry}).ToArray(),"entity registry survivor order");
                RequireExactExtension(ingredientPreimage,ingredients.Where(value=>!ReferenceEquals(value,missingIngredient[0]))
                    .Where(value=>!concurrent.Any(pair=>ReferenceEquals(value,pair.Ingredient)))
                    .Select(value=>value.Value).ToArray(),deletions.Where(value=>value.Ingredient!=null)
                        .Select(value=>value.Ingredient).ToArray(),
                    "ingredient survivor order");
                RequireExactExtension(physicsPreimage,physics.Where(value=>!ReferenceEquals(value,missingPhysics[0]))
                    .Where(value=>!concurrent.Any(pair=>ReferenceEquals(value,pair.Physics)))
                    .Select(value=>value.Value).ToArray(),deletions.Select(value=>value.Physics).ToArray(),
                    "physics survivor order");
                return new RestoreTransaction {Deletions=deletions,ConcurrentRecreations=concurrent,
                    RegistryPreimage=registryPreimage,
                    IngredientPreimage=ingredientPreimage,PhysicsPreimage=physicsPreimage};
            }

            private HistoricalRecreation[] CaptureConcurrentHistoricalRecreations(int[] entityIds,
                NativeAttachmentPoseCheckpoint.Snapshot attachment,NativeBodyPoseCheckpoint.Snapshot body)
            {
                if(entityIds.Length==0)return new HistoricalRecreation[0];
                if(entityIds.Length!=2||entityIds.Any(value=>value<=0||value>ushort.MaxValue
                    ||value==attachment.EntityId||value==body.EntityId)
                    ||entityIds.Distinct().Count()!=entityIds.Length)
                    throw new InvalidOperationException("Concurrent historical recreation is not one distinct owner/container pair.");
                var selected=registryEntries.Where(entry=>entityIds.Contains((int)entry.m_Header.m_uEntityID)).ToArray();
                if(selected.Length!=2||selected.Any(entry=>EntitySerialisationRegistry.GetEntry(entry.m_Header.m_uEntityID)!=null))
                    throw new InvalidOperationException("Concurrent historical recreation identities are not exactly absent from the live registry.");
                var pairs=physicalPairs.Where(pair=>selected.Contains(pair.OwnerEntry)
                    &&selected.Contains(pair.ContainerEntry)).ToArray();
                if(pairs.Length!=1||ReferenceEquals(pairs[0].OwnerEntry,pairs[0].ContainerEntry))
                    throw new InvalidOperationException("Concurrent historical recreation lacks one exact captured PhysicalAttachment pair.");
                var capturedPair=pairs[0];
                var ownerIngredients=ingredients.Where(value=>ReferenceEquals(value.Entry,capturedPair.OwnerEntry)).ToArray();
                var containerIngredients=ingredients.Where(value=>ReferenceEquals(value.Entry,capturedPair.ContainerEntry)).ToArray();
                var ownerPhysics=physics.Where(value=>ReferenceEquals(value.Entry,capturedPair.OwnerEntry)).ToArray();
                var containerPhysics=physics.Where(value=>ReferenceEquals(value.Entry,capturedPair.ContainerEntry)).ToArray();
                if(ownerIngredients.Length>1||containerIngredients.Length!=0||ownerPhysics.Length!=0
                    ||containerPhysics.Length!=1)
                    throw new InvalidOperationException("Concurrent historical PhysicalAttachment canonical-list roles differ.");
                return new[]{new HistoricalRecreation {
                    OwnerId=(int)capturedPair.OwnerEntry.m_Header.m_uEntityID,
                    ContainerId=(int)capturedPair.ContainerEntry.m_Header.m_uEntityID,
                    OwnerEntry=capturedPair.OwnerEntry,ContainerEntry=capturedPair.ContainerEntry,
                    Ingredient=ownerIngredients.SingleOrDefault(),Physics=containerPhysics[0],Pair=capturedPair}};
            }

            private FutureDeletion[] CaptureFutureDeletions(int[] ownerIds,
                NativeAttachmentPoseCheckpoint.Snapshot attachment,NativeBodyPoseCheckpoint.Snapshot body)
            {
                if(ownerIds.Any(value=>value<=0||value>ushort.MaxValue
                    ||value==attachment.EntityId||value==body.EntityId)
                    ||ownerIds.Distinct().Count()!=ownerIds.Length)
                    throw new InvalidOperationException("Initial PhysicalAttachment future-deletion owner set is invalid.");
                var targetIds=new HashSet<uint>(registryEntries.Select(value=>value.m_Header.m_uEntityID));
                var seenIds=new HashSet<uint>();var result=new List<FutureDeletion>();
                foreach(var rawOwnerId in ownerIds)
                {
                    uint ownerId=(uint)rawOwnerId;
                    var ownerEntry=EntitySerialisationRegistry.GetEntry(ownerId);
                    var ownerObject=ownerEntry==null?null:ownerEntry.m_GameObject;
                    var attachments=ownerObject==null?new PhysicalAttachment[0]:ownerObject.GetComponents<PhysicalAttachment>();
                    var physical=attachments.Length==1?attachments[0]:null;
                    var containerObject=physical==null||physical.m_container==null?null:physical.m_container.gameObject;
                    var containerEntry=containerObject==null?null:EntitySerialisationRegistry.GetEntry(containerObject);
                    uint containerId=containerEntry==null?0:containerEntry.m_Header.m_uEntityID;
                    if(ownerEntry==null||ownerObject==null||ownerEntry.m_Header.m_uEntityID!=ownerId
                        ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(ownerId),ownerEntry)
                        ||attachments.Length!=1||containerEntry==null||containerObject==null
                        ||containerId==0||containerId>ushort.MaxValue
                        ||!ReferenceEquals(containerEntry.m_GameObject,containerObject)
                        ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(containerId),containerEntry)
                        ||targetIds.Contains(ownerId)||targetIds.Contains(containerId)
                        ||!seenIds.Add(ownerId)||!seenIds.Add(containerId))
                        throw new InvalidOperationException("Initial PhysicalAttachment future deletion is not one exact future-only owner/container pair: "+rawOwnerId+".");
                    var ownerIngredients=ownerObject.GetComponents<ServerIngredientContainer>();
                    if(ownerIngredients.Length>1)
                        throw new InvalidOperationException("Initial PhysicalAttachment future deletion has ambiguous ingredient-container membership: "+rawOwnerId+".");
                    var ingredientMatches=ownerIngredients.Length==0?new object[0]:Current(ingredientList)
                        .Where(value=>ReferenceEquals(value,ownerIngredients[0])).ToArray();
                    var currentPhysics=Current(physicsList);
                    var physicsMatches=currentPhysics.Where(value=>value!=null
                        &&ReferenceEquals(physicsEntry.GetValue(value),containerEntry)
                        &&ReferenceEquals(physicsTransform.GetValue(value),containerObject.transform)).ToArray();
                    var physicsEntryMatches=currentPhysics.Where(value=>value!=null
                        &&ReferenceEquals(physicsEntry.GetValue(value),containerEntry)).ToArray();
                    var physicsTransformMatches=currentPhysics.Where(value=>value!=null
                        &&ReferenceEquals(physicsTransform.GetValue(value),containerObject.transform)).ToArray();
                    if(ingredientMatches.Length!=ownerIngredients.Length||physicsMatches.Length!=1
                        ||physicsEntryMatches.Length!=1||physicsTransformMatches.Length!=1)
                        throw new InvalidOperationException("Initial PhysicalAttachment future deletion canonical-list membership is not unique: "+rawOwnerId+".");
                    result.Add(new FutureDeletion {OwnerId=rawOwnerId,ContainerId=(int)containerId,
                        OwnerEntry=ownerEntry,ContainerEntry=containerEntry,
                        Ingredient=ingredientMatches.Length==0?null:ingredientMatches[0],
                        Physics=physicsMatches[0],PhysicsTransform=containerObject.transform});
                }
                return result.ToArray();
            }

            internal void Rebind(NativeAttachmentPoseCheckpoint.Snapshot attachment,
                NativeBodyPoseCheckpoint.Snapshot body,EntitySerialisationEntry owner,
                EntitySerialisationEntry container,RestoreTransaction transaction)
            {
                if(transaction==null||transaction.Deletions==null||transaction.RegistryPreimage==null
                    ||transaction.IngredientPreimage==null||transaction.PhysicsPreimage==null
                    ||transaction.Rebound||transaction.Finalized)
                    throw new InvalidOperationException("Initial PhysicalAttachment topology transaction lifecycle differs before rebind.");
                ValidateListIdentity();
                ValidateFutureDeletionPreimage(transaction.Deletions);
                var oldIngredient=ingredients.Single(value=>ReferenceEquals(value.Entry,attachment.Entry));
                var oldPhysics=physics.Single(value=>ReferenceEquals(value.Entry,body.Entry)
                    &&ReferenceEquals(value.Transform,body.Transform));
                var oldPlate=plates.Single(value=>ReferenceEquals(value.Entry,attachment.Entry));
                var oldPair=physicalPairs.Single(value=>ReferenceEquals(value.OwnerEntry,attachment.Entry)
                    &&ReferenceEquals(value.ContainerEntry,body.Entry));
                var newIngredient=owner.m_GameObject.GetComponent<ServerIngredientContainer>();
                var newPhysics=Current(physicsList).Where(value=>ReferenceEquals(physicsEntry.GetValue(value),container)
                    &&ReferenceEquals(physicsTransform.GetValue(value),container.m_GameObject.transform)).ToArray();
                if(newIngredient==null||newPhysics.Length!=1)
                    throw new InvalidOperationException("Recreated initial PhysicalAttachment canonical-list values are absent.");
                var concurrent=ValidateConcurrentHistoricalRecreations(transaction.ConcurrentRecreations);
                RequireExactExtension(CurrentRegistry(),transaction.RegistryPreimage,
                    new object[]{owner,container}.Concat(concurrent.SelectMany(value=>
                        new object[]{value.Owner,value.Container})).ToArray(),"recreated entity registry membership");
                RequireExactExtension(Current(ingredientList),transaction.IngredientPreimage,
                    new object[]{newIngredient}.Concat(concurrent.Where(value=>value.IngredientRecord!=null)
                        .Select(value=>value.IngredientValue)).ToArray(),"recreated ingredient membership");
                RequireExactExtension(Current(physicsList),transaction.PhysicsPreimage,
                    new object[]{newPhysics[0]}.Concat(concurrent.Select(value=>value.PhysicsValue)).ToArray(),
                    "recreated physics membership");

                int ownerIndex=Array.IndexOf(registryEntries,attachment.Entry);
                int bodyIndex=Array.IndexOf(registryEntries,body.Entry);
                owner.m_GameObject.name=registryNames[ownerIndex];
                container.m_GameObject.name=registryNames[bodyIndex];
                transaction.InitialRebind=new CanonicalRebind {
                    OwnerIndex=ownerIndex,ContainerIndex=bodyIndex,
                    HistoricalOwner=attachment.Entry,HistoricalContainer=body.Entry,
                    Owner=owner,Container=container,IngredientRecord=oldIngredient,IngredientValue=newIngredient,
                    PhysicsRecord=oldPhysics,PhysicsValue=newPhysics[0],PhysicsTransform=container.m_GameObject.transform,
                    PlateRecord=oldPlate,PairRecord=oldPair};
                transaction.ConcurrentRebinds=concurrent;
                transaction.Rebound=true;
                if(owner.m_GameObject.name!=registryNames[ownerIndex]||container.m_GameObject.name!=registryNames[bodyIndex])
                    throw new InvalidOperationException("Recreated initial PhysicalAttachment names were not restored exactly.");
                if(transaction.Deletions.Length==0)FinalizeAfterDeletions(transaction);
            }

            private void ValidateFutureDeletionPreimage(FutureDeletion[] deletions)
            {
                foreach(var deletion in deletions)
                {
                    var ownerObject=deletion==null||deletion.OwnerEntry==null?null:deletion.OwnerEntry.m_GameObject;
                    var attachments=ownerObject==null?new PhysicalAttachment[0]:ownerObject.GetComponents<PhysicalAttachment>();
                    var attachment=attachments.Length==1?attachments[0]:null;
                    var containerObject=attachment==null||attachment.m_container==null?null:attachment.m_container.gameObject;
                    var ingredient=deletion==null?null:deletion.Ingredient as ServerIngredientContainer;
                    var ownerIngredients=ownerObject==null?new ServerIngredientContainer[0]:ownerObject.GetComponents<ServerIngredientContainer>();
                    if(deletion==null||ownerObject==null||deletion.ContainerEntry==null
                        ||attachments.Length!=1
                        ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)deletion.OwnerId),deletion.OwnerEntry)
                        ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)deletion.ContainerId),deletion.ContainerEntry)
                        ||!ReferenceEquals(containerObject,deletion.ContainerEntry.m_GameObject)
                        ||ownerIngredients.Length!=(ingredient==null?0:1)
                        ||(ingredient!=null&&(!ReferenceEquals(ownerIngredients[0],ingredient)
                            ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(ingredient.gameObject),deletion.OwnerEntry)))
                        ||deletion.Physics==null
                        ||!ReferenceEquals(physicsEntry.GetValue(deletion.Physics),deletion.ContainerEntry)
                        ||!ReferenceEquals(physicsTransform.GetValue(deletion.Physics),deletion.PhysicsTransform)
                        ||!ReferenceEquals(deletion.PhysicsTransform,containerObject.transform))
                        throw new InvalidOperationException("An authorized future PhysicalAttachment deletion changed before native spawn.");
                }
            }

            internal void FinalizeAfterDeletions(RestoreTransaction transaction)
            {
                if(transaction==null||transaction.Deletions==null||!transaction.Rebound||transaction.Finalized)
                    throw new InvalidOperationException("Initial PhysicalAttachment topology transaction lifecycle differs at finalization.");
                ValidateListIdentity();
                foreach(var deletion in transaction.Deletions)
                {
                    if(deletion==null||deletion.OwnerEntry==null||deletion.ContainerEntry==null
                        ||EntitySerialisationRegistry.GetEntry((uint)deletion.OwnerId)!=null
                        ||EntitySerialisationRegistry.GetEntry((uint)deletion.ContainerId)!=null)
                        throw new InvalidOperationException("An authorized future PhysicalAttachment deletion remains registered: "+
                            (deletion==null?0:deletion.OwnerId)+".");
                }
                var concurrentObserved=ValidateConcurrentHistoricalRecreations(transaction.ConcurrentRecreations);
                if(transaction.ConcurrentRebinds==null
                    ||transaction.ConcurrentRebinds.Length!=concurrentObserved.Length)
                    throw new InvalidOperationException("Concurrent historical PhysicalAttachment replacement lifecycle differs.");
                for(int i=0;i<concurrentObserved.Length;i++)
                    if(!ReferenceEquals(transaction.ConcurrentRebinds[i].HistoricalOwner,concurrentObserved[i].HistoricalOwner)
                        ||!ReferenceEquals(transaction.ConcurrentRebinds[i].HistoricalContainer,concurrentObserved[i].HistoricalContainer)
                        ||!ReferenceEquals(transaction.ConcurrentRebinds[i].Owner,concurrentObserved[i].Owner)
                        ||!ReferenceEquals(transaction.ConcurrentRebinds[i].Container,concurrentObserved[i].Container)
                        ||!ReferenceEquals(transaction.ConcurrentRebinds[i].IngredientValue,concurrentObserved[i].IngredientValue)
                        ||!ReferenceEquals(transaction.ConcurrentRebinds[i].PhysicsValue,concurrentObserved[i].PhysicsValue)
                        ||!ReferenceEquals(transaction.ConcurrentRebinds[i].PhysicsTransform,concurrentObserved[i].PhysicsTransform))
                        throw new InvalidOperationException("Concurrent historical PhysicalAttachment replacement changed before finalization.");
                var concurrentRebinds=transaction.ConcurrentRebinds;
                var rebinds=new[]{transaction.InitialRebind}.Concat(concurrentRebinds).ToArray();
                if(rebinds.Any(value=>value==null)
                    ||rebinds.SelectMany(value=>new[]{value.HistoricalOwner,value.HistoricalContainer}).Distinct().Count()!=rebinds.Length*2
                    ||rebinds.SelectMany(value=>new[]{value.Owner,value.Container}).Distinct().Count()!=rebinds.Length*2)
                    throw new InvalidOperationException("Historical PhysicalAttachment replacement identities overlap.");
                var targetRegistry=registryEntries.Select(value=>(object)ReplacementEntry(value,rebinds)).ToArray();
                var targetIngredients=ingredients.Select(value=>ReplacementIngredient(value,rebinds)).ToArray();
                var targetPhysics=physics.Select(value=>ReplacementPhysics(value,rebinds)).ToArray();
                RequireSameMembers(CurrentRegistry(),targetRegistry,"restored entity registry membership");
                RequireSameMembers(Current(ingredientList),targetIngredients,"restored ingredient membership");
                // NetworkUtils unregisters a destroyed PhysicalAttachment body
                // synchronously, while ServerPhysicsObjectSynchroniser.OnDestroy
                // removes its canonical transform-pair at the end of the Unity
                // frame.  At a paused authoring boundary, retire only the exact
                // scheduler-authorized future pairs proved during preflight.
                // OnDestroy later performs its normal remove-if-found and sees
                // no row; independent transform uniqueness ensures it cannot
                // consume a checkpoint survivor instead.
                var pendingPhysics=transaction.Deletions.Select(value=>value.Physics).ToArray();
                if(pendingPhysics.Length!=0)
                {
                    var currentPhysics=Current(physicsList);
                    RequireUnique(currentPhysics,"restored physics membership actual");
                    RequireUnique(pendingPhysics,"authorized deferred-destroy physics rows");
                    if(targetPhysics.Any(value=>ContainsReference(pendingPhysics,value)))
                        throw new InvalidOperationException("Native authorized deferred-destroy physics rows overlap the checkpoint.");
                    var residualPhysics=pendingPhysics.Where(value=>ContainsReference(currentPhysics,value)).ToArray();
                    RequireSamePhysicsMembers(currentPhysics.Where(value=>!ContainsReference(pendingPhysics,value)).ToArray(),
                        targetPhysics,"restored physics membership without authorized deferred-destroy rows");
                    foreach(var deletion in transaction.Deletions.Where(value=>ContainsReference(residualPhysics,value.Physics)))
                    {
                        if(deletion.Physics==null||deletion.ContainerEntry==null||deletion.PhysicsTransform==null
                            ||!ReferenceEquals(physicsEntry.GetValue(deletion.Physics),deletion.ContainerEntry)
                            ||!ReferenceEquals(physicsTransform.GetValue(deletion.Physics),deletion.PhysicsTransform)
                            ||currentPhysics.Count(value=>ReferenceEquals(physicsEntry.GetValue(value),deletion.ContainerEntry))!=1
                            ||currentPhysics.Count(value=>ReferenceEquals(physicsTransform.GetValue(value),deletion.PhysicsTransform))!=1)
                            throw new InvalidOperationException("An authorized future PhysicalAttachment deferred physics row is no longer independently unique: "+deletion.OwnerId+".");
                    }
                    if(residualPhysics.Length!=0)
                        NativeDynamicWarpRules.RetireExactCanonicalListValues(physicsList,physicsList,
                            currentPhysics,residualPhysics);
                    transaction.RetiredFuturePhysicsPairs=residualPhysics.Length;
                    transaction.AlreadyRetiredFuturePhysicsPairs=pendingPhysics.Length-residualPhysics.Length;
                }
                RequireSamePhysicsMembers(Current(physicsList),targetPhysics,"restored physics membership");
                // The live canonical lists must receive the replacement-aware
                // targets.  The retained historical records are updated only
                // after those live writes and exact readbacks succeed, so a
                // failed transaction cannot poison a reusable checkpoint.
                for(int i=0;i<targetRegistry.Length;i++)
                    registry._items[i]=(EntitySerialisationEntry)targetRegistry[i];
                for(int i=0;i<targetIngredients.Length;i++)ingredientList[i]=targetIngredients[i];
                for(int i=0;i<targetPhysics.Length;i++)physicsList[i]=targetPhysics[i];
                RequireSame(CurrentRegistry(),targetRegistry,"restored entity registry order");
                RequireSame(Current(ingredientList),targetIngredients,"restored ingredient order");
                RequireSamePhysics(Current(physicsList),targetPhysics,"restored physics order");
                // Commit retained checkpoint identities only after every live
                // membership/order proof succeeds. A failed attempt therefore
                // cannot poison a reusable historical snapshot halfway through
                // a mixed recreation.
                foreach(var rebind in rebinds)
                {
                    registryEntries[rebind.OwnerIndex]=rebind.Owner;
                    registryEntries[rebind.ContainerIndex]=rebind.Container;
                    if(rebind.IngredientRecord!=null)
                    {
                        rebind.IngredientRecord.Value=rebind.IngredientValue;
                        rebind.IngredientRecord.Entry=rebind.Owner;
                    }
                    rebind.PhysicsRecord.Value=rebind.PhysicsValue;
                    rebind.PhysicsRecord.Entry=rebind.Container;
                    rebind.PhysicsRecord.Transform=rebind.PhysicsTransform;
                    if(rebind.PlateRecord!=null)rebind.PlateRecord.Entry=rebind.Owner;
                    if(rebind.PairRecord!=null)
                    {
                        rebind.PairRecord.OwnerEntry=rebind.Owner;
                        rebind.PairRecord.ContainerEntry=rebind.Container;
                        rebind.PairRecord.OwnerTransform=rebind.Owner.m_GameObject.transform;
                    }
                }
                transaction.Finalized=true;
            }

            private CanonicalRebind[] ValidateConcurrentHistoricalRecreations(HistoricalRecreation[] recreations)
            {
                if(recreations==null)
                    throw new InvalidOperationException("Concurrent historical recreation topology is absent.");
                var result=new List<CanonicalRebind>();
                foreach(var recreation in recreations)
                {
                    var owner=EntitySerialisationRegistry.GetEntry((uint)recreation.OwnerId);
                    var container=EntitySerialisationRegistry.GetEntry((uint)recreation.ContainerId);
                    var ownerObject=owner==null?null:owner.m_GameObject;
                    var containerObject=container==null?null:container.m_GameObject;
                    var attachments=ownerObject==null?new PhysicalAttachment[0]:ownerObject.GetComponents<PhysicalAttachment>();
                    var physical=attachments.Length==1?attachments[0]:null;
                    var physicalContainer=physical==null||physical.m_container==null?null:physical.m_container.gameObject;
                    if(recreation==null||recreation.Pair==null||recreation.OwnerEntry==null
                        ||recreation.ContainerEntry==null||recreation.Physics==null
                        ||owner==null||container==null||ownerObject==null||containerObject==null
                        ||ReferenceEquals(owner,recreation.OwnerEntry)||ReferenceEquals(container,recreation.ContainerEntry)
                        ||owner.m_Header.m_uEntityID!=recreation.OwnerId
                        ||container.m_Header.m_uEntityID!=recreation.ContainerId
                        ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)recreation.OwnerId),owner)
                        ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)recreation.ContainerId),container)
                        ||CurrentRegistry().Count(value=>ReferenceEquals(value,owner))!=1
                        ||CurrentRegistry().Count(value=>ReferenceEquals(value,container))!=1
                        ||attachments.Length!=1||!ReferenceEquals(physicalContainer,containerObject)
                        ||!BehavioralComponentTypes(ComponentTypes(ownerObject)).SequenceEqual(
                            BehavioralComponentTypes(recreation.Pair.OwnerComponents))
                        ||!BehavioralComponentTypes(ComponentTypes(containerObject)).SequenceEqual(
                            BehavioralComponentTypes(recreation.Pair.ContainerComponents))
                        ||!ColliderTypes(ownerObject).SequenceEqual(recreation.Pair.OwnerColliderTypes)
                        ||!ColliderTypesExcluding(containerObject,ownerObject.transform).SequenceEqual(
                            recreation.Pair.ContainerColliderTypes))
                        throw new InvalidOperationException("Concurrent historical PhysicalAttachment replacement differs from its checkpoint topology: "+
                            (recreation==null?0:recreation.OwnerId)+".");
                    int ownerIndex=Array.IndexOf(registryEntries,recreation.OwnerEntry);
                    int containerIndex=Array.IndexOf(registryEntries,recreation.ContainerEntry);
                    if(ownerIndex<0||containerIndex<0||ownerIndex==containerIndex
                        ||ownerObject.name!=registryNames[ownerIndex]||containerObject.name!=registryNames[containerIndex])
                        throw new InvalidOperationException("Concurrent historical PhysicalAttachment registry identity/name differs: "+recreation.OwnerId+".");

                    var ownerIngredients=ownerObject.GetComponents<ServerIngredientContainer>();
                    var currentIngredientValues=Current(ingredientList);
                    var currentIngredients=ownerIngredients.Length==1?currentIngredientValues
                        .Where(value=>ReferenceEquals(value,ownerIngredients[0])).ToArray():new object[0];
                    if(ownerIngredients.Length!=(recreation.Ingredient==null?0:1)
                        ||currentIngredients.Length!=ownerIngredients.Length)
                        throw new InvalidOperationException("Concurrent historical PhysicalAttachment ingredient membership differs: "+recreation.OwnerId+".");
                    var currentPhysics=Current(physicsList);
                    var replacementPhysics=currentPhysics.Where(value=>value!=null
                        &&ReferenceEquals(physicsEntry.GetValue(value),container)
                        &&ReferenceEquals(physicsTransform.GetValue(value),containerObject.transform)).ToArray();
                    if(replacementPhysics.Length!=1
                        ||currentPhysics.Count(value=>ReferenceEquals(physicsEntry.GetValue(value),container))!=1
                        ||currentPhysics.Count(value=>ReferenceEquals(physicsTransform.GetValue(value),containerObject.transform))!=1)
                        throw new InvalidOperationException("Concurrent historical PhysicalAttachment physics membership differs: "+recreation.OwnerId+".");

                    var historicalPlates=plates.Where(value=>ReferenceEquals(value.Entry,recreation.OwnerEntry)
                        ||ReferenceEquals(value.Entry,recreation.ContainerEntry)).ToArray();
                    if(historicalPlates.Length>1||historicalPlates.Any(value=>ReferenceEquals(value.Entry,recreation.ContainerEntry)))
                        throw new InvalidOperationException("Concurrent historical PhysicalAttachment plate role is ambiguous: "+recreation.OwnerId+".");
                    var plate=historicalPlates.SingleOrDefault();
                    if(plate!=null)
                    {
                        var observed=CapturePlate(owner);
                        if(observed==null||!observed.Components.SequenceEqual(plate.Components)
                            ||!observed.AuthoringComponents.SequenceEqual(plate.AuthoringComponents)
                            ||!observed.ColliderTypes.SequenceEqual(plate.ColliderTypes)
                            ||!ReferenceEquals(observed.Step,plate.Step)||!ReferenceEquals(observed.ModelPrefab,plate.ModelPrefab))
                            throw new InvalidOperationException("Concurrent historical plate replacement signature differs: "+recreation.OwnerId+".");
                    }
                    result.Add(new CanonicalRebind {OwnerIndex=ownerIndex,ContainerIndex=containerIndex,
                        HistoricalOwner=recreation.OwnerEntry,HistoricalContainer=recreation.ContainerEntry,
                        Owner=owner,Container=container,IngredientRecord=recreation.Ingredient,
                        IngredientValue=currentIngredients.SingleOrDefault(),PhysicsRecord=recreation.Physics,
                        PhysicsValue=replacementPhysics[0],PhysicsTransform=containerObject.transform,
                        PlateRecord=plate,PairRecord=recreation.Pair});
                }
                return result.ToArray();
            }

            private static EntitySerialisationEntry ReplacementEntry(EntitySerialisationEntry value,
                CanonicalRebind[] rebinds)
            {
                var owner=rebinds.Where(rebind=>ReferenceEquals(value,rebind.HistoricalOwner)).ToArray();
                var container=rebinds.Where(rebind=>ReferenceEquals(value,rebind.HistoricalContainer)).ToArray();
                if(owner.Length+container.Length>1)
                    throw new InvalidOperationException("Historical registry replacement mapping is ambiguous.");
                return owner.Length==1?owner[0].Owner:container.Length==1?container[0].Container:value;
            }

            private static object ReplacementIngredient(IngredientRecord value,CanonicalRebind[] rebinds)
            {
                var matches=rebinds.Where(rebind=>ReferenceEquals(value,rebind.IngredientRecord)).ToArray();
                if(matches.Length>1)throw new InvalidOperationException("Historical ingredient replacement mapping is ambiguous.");
                return matches.Length==1?matches[0].IngredientValue:value.Value;
            }

            private static object ReplacementPhysics(PhysicsRecord value,CanonicalRebind[] rebinds)
            {
                var matches=rebinds.Where(rebind=>ReferenceEquals(value,rebind.PhysicsRecord)).ToArray();
                if(matches.Length>1)throw new InvalidOperationException("Historical physics replacement mapping is ambiguous.");
                return matches.Length==1?matches[0].PhysicsValue:value.Value;
            }

            private void ValidateListIdentity()
            {
                var ingredientField=AccessTools.Field(typeof(ServerIngredientContainer),"ms_AllIngredientContainers");
                var physicsField=AccessTools.Field(typeof(ServerPhysicsObjectSynchroniser),"ms_ServerPhysicsObjectSytnchroniserTransforms");
                if(!ReferenceEquals(EntitySerialisationRegistry.m_EntitiesList,registry)
                    ||ingredientField==null||!ReferenceEquals(ingredientField.GetValue(null),ingredientList)
                    ||physicsField==null||!ReferenceEquals(physicsField.GetValue(null),physicsList))
                    throw new InvalidOperationException("Native canonical list incarnation changed before initial attachment rebind.");
            }
            private object[] CurrentRegistry()
            { return registry._items.Take(registry.Count).Cast<object>().ToArray(); }
            private static object[] Current(IList values)
            { var result=new object[values.Count];values.CopyTo(result,0);return result; }
            private static void RequireSame(object[] actual,object[] expected,string name)
            {
                if(actual.Length!=expected.Length)throw new InvalidOperationException("Native "+name+" cardinality differs.");
                for(int i=0;i<actual.Length;i++)if(!ReferenceEquals(actual[i],expected[i]))
                    throw new InvalidOperationException("Native "+name+" differs at index "+i+".");
            }

            private static void RequireExactExtension(object[] actual,object[] survivors,object[] extras,string name)
            {
                if(actual==null||survivors==null||extras==null
                    ||actual.Length!=survivors.Length+extras.Length)
                    throw new InvalidOperationException("Native "+name+" cardinality differs.");
                RequireUnique(survivors,name+" checkpoint");RequireUnique(extras,name+" authorized extras");
                if(survivors.Any(value=>ContainsReference(extras,value)))
                    throw new InvalidOperationException("Native "+name+" authorized extras overlap the checkpoint.");
                foreach(var extra in extras)
                    if(actual.Count(value=>ReferenceEquals(value,extra))!=1)
                        throw new InvalidOperationException("Native "+name+" lacks one exact authorized future value.");
                RequireSame(actual.Where(value=>!ContainsReference(extras,value)).ToArray(),survivors,name);
            }

            private static void RequireSameMembers(object[] actual,object[] expected,string name)
            {
                if(actual==null||expected==null||actual.Length!=expected.Length)
                    throw new InvalidOperationException("Native "+name+" cardinality differs.");
                RequireUnique(actual,name+" actual");RequireUnique(expected,name+" target");
                if(actual.Any(value=>!ContainsReference(expected,value))
                    ||expected.Any(value=>!ContainsReference(actual,value)))
                    throw new InvalidOperationException("Native "+name+" differs.");
            }

            private void RequireSamePhysicsMembers(object[] actual,object[] expected,string name)
            {
                try { RequireSameMembers(actual,expected,name); }
                catch(InvalidOperationException error)
                {
                    throw new InvalidOperationException(error.Message+" actual="+PhysicsSignature(actual)
                        +", expected="+PhysicsSignature(expected)+".",error);
                }
            }

            private static void RequireUnique(object[] values,string name)
            {
                if(values.Any(value=>value==null))
                    throw new InvalidOperationException("Native "+name+" contains a null value.");
                for(int i=0;i<values.Length;i++)
                    if(values.Take(i).Any(value=>ReferenceEquals(value,values[i])))
                        throw new InvalidOperationException("Native "+name+" contains duplicate identity.");
            }

            private static bool ContainsReference(object[] values,object target)
            {return values.Any(value=>ReferenceEquals(value,target));}

            private void RequireSamePhysics(object[] actual,object[] expected,string name)
            {
                if(actual.Length!=expected.Length)
                    throw new InvalidOperationException("Native "+name+" cardinality differs: actualCount="
                        +actual.Length+", expectedCount="+expected.Length+", actual="+PhysicsSignature(actual)
                        +", expected="+PhysicsSignature(expected)+".");
                for(int i=0;i<actual.Length;i++)if(!ReferenceEquals(actual[i],expected[i]))
                    throw new InvalidOperationException("Native "+name+" differs at index "+i
                        +": actual="+PhysicsSignature(actual)+", expected="+PhysicsSignature(expected)+".");
            }

            private string PhysicsSignature(object[] values)
            {
                if(values==null)return "<null>";
                var rows=new string[values.Length];
                for(int i=0;i<values.Length;i++)
                {
                    var value=values[i];
                    var entry=value==null?null:physicsEntry.GetValue(value) as EntitySerialisationEntry;
                    var transform=value==null?null:physicsTransform.GetValue(value) as Transform;
                    rows[i]=i+":"+(entry==null?"entry-null":entry.m_Header.m_uEntityID.ToString())
                        +":"+(entry==null||entry.m_GameObject==null?"object-null":entry.m_GameObject.name)
                        +":"+(transform==null?"transform-null":transform.gameObject.name)
                        +":"+(transform==null?"0":transform.GetInstanceID().ToString());
                }
                return "["+string.Join(",",rows)+"]";
            }
        }

        private sealed class ConcurrentColliderRecreation
        {
            internal int OwnerId;
            internal Transform HistoricalOwner;
            internal NativeBodyPoseCheckpoint.Snapshot AffectedBody;
            internal NativeBodyColliderCheckpoint.Shape[] AffectedShapes;
        }

        private readonly int ownerId, containerId;
        private readonly EntityPathReference path;
        private readonly TopologySnapshot topology;
        private readonly TopologySnapshot.RestoreTransaction topologyRestore;
        private readonly Transform historicalOwner;
        private readonly EntitySerialisationEntry historicalOwnerEntry,historicalBodyEntry;
        private readonly TopologySnapshot.PlateRecord initialPlate;
        private NativeAttachmentPoseCheckpoint.Snapshot attachment;
        private NativeBodyPoseCheckpoint.Snapshot body,affectedBody;
        private NativeBodyColliderCheckpoint.Shape[] affectedOwnerShapes;
        private readonly ConcurrentColliderRecreation[] concurrentColliderRecreations;
        private readonly bool detachedOnContainer;
        private EntitySerialisationEntry recreatedOwner,recreatedContainer;
        private bool attachmentIdentityRebound,bodyIdentityRebound,collidersRebound,colliderTransformSyncRequired,lineageCommitted;

        private NativeInitialAttachmentRecreation(int ownerId, int containerId,
            EntityPathReference path, NativeAttachmentPoseCheckpoint.Snapshot attachment,
            NativeBodyPoseCheckpoint.Snapshot body,NativeBodyPoseCheckpoint.Snapshot affectedBody,
            NativeBodyColliderCheckpoint.Shape[] affectedOwnerShapes,TopologySnapshot topology,
            TopologySnapshot.PlateRecord initialPlate,TopologySnapshot.RestoreTransaction topologyRestore,
            bool detachedOnContainer,ConcurrentColliderRecreation[] concurrentColliders)
        {
            this.ownerId=ownerId;this.containerId=containerId;this.path=path;
            this.attachment=attachment;this.body=body;this.affectedBody=affectedBody;
            this.affectedOwnerShapes=affectedOwnerShapes;this.topology=topology;
            this.initialPlate=initialPlate;this.topologyRestore=topologyRestore;
            this.detachedOnContainer=detachedOnContainer;
            concurrentColliderRecreations=concurrentColliders??new ConcurrentColliderRecreation[0];
            historicalOwner=attachment.Transform;
            historicalOwnerEntry=attachment.Entry;historicalBodyEntry=body.Entry;
        }

        internal static NativeInitialAttachmentRecreation Prepare(WarpSpec warp,
            NativeAttachmentPoseCheckpoint.Snapshot[] attachments,
            NativeBodyPoseCheckpoint.Snapshot[] bodies,
            TopologySnapshot topology,
            out NativeAttachmentPoseCheckpoint.Snapshot[] survivingAttachments,
            out NativeBodyPoseCheckpoint.Snapshot[] survivingBodies)
        {
            if(warp==null)throw new ArgumentNullException("warp");
            if(attachments==null)throw new ArgumentNullException("attachments");
            if(bodies==null)throw new ArgumentNullException("bodies");
            if(topology==null)throw new ArgumentNullException("topology");
            var missingAttachments=attachments.Where(AttachmentMissing).ToArray();
            var missingBodies=bodies.Where(BodyMissing).ToArray();
            survivingAttachments=attachments.Except(missingAttachments).ToArray();
            survivingBodies=bodies.Except(missingBodies).ToArray();
            if(missingAttachments.Length==0&&missingBodies.Length==0)return null;
            if(missingAttachments.Length!=1||missingBodies.Length!=1)
                throw new InvalidOperationException("Unsupported missing initial PhysicalAttachment/body membership.");

            var attachment=missingAttachments[0];var body=missingBodies[0];
            if(!ReferenceEquals(attachment.Container,body.Body)||attachment.EntityId<=0||body.EntityId<=0
                ||attachment.EntityId>ushort.MaxValue||body.EntityId>ushort.MaxValue)
                throw new InvalidOperationException("Missing initial PhysicalAttachment does not own the missing initial body.");
            // A target-side detached pair is missing precisely because both historical
            // Unity objects were destroyed later in the retained future. Unity's
            // overloaded null operator reports those still-addressable checkpoint
            // references as null, so qualification must use CLR reference-null checks.
            bool detached=!attachment.Attached&&!ReferenceEquals(attachment.ParentEntry,null)
                &&!ReferenceEquals(attachment.ParentObject,null)
                &&ReferenceEquals(attachment.ParentEntry,body.Entry)&&ReferenceEquals(attachment.ParentObject,body.Object)
                &&ReferenceEquals(attachment.Parent,body.Transform);
            bool attached=attachment.Attached&&!ReferenceEquals(attachment.ParentEntry,null)
                &&!ReferenceEquals(attachment.ParentObject,null)
                &&!ReferenceEquals(attachment.ParentEntry,body.Entry);
            if(attachment.Unsupported!=null||(!attached&&!detached)
                ||attachment.Prediction!=null||attachment.PredictionTransform!=null
                ||attachment.PredictionQueueCount!=0||body.Colliders.Length!=0)
                throw new InvalidOperationException("Missing initial PhysicalAttachment checkpoint is not an exact attached-or-container-detached, predictor-free, colliderless-container pair.");
            NativeBodyPoseCheckpoint.Snapshot affectedBody=null;
            NativeBodyColliderCheckpoint.Shape[] affectedShapes=new NativeBodyColliderCheckpoint.Shape[0];
            if(attached)
            {
                if(!ReferenceEquals(EntitySerialisationRegistry.GetEntry(attachment.ParentObject),attachment.ParentEntry))
                    throw new InvalidOperationException("Missing initial PhysicalAttachment target parent incarnation changed.");
                var affected=bodies.Where(value=>!ReferenceEquals(value,body))
                    .Select(value=>new {Body=value,Shapes=value.Colliders.Where(shape=>
                        NativeBodyColliderCheckpoint.HasAncestor(shape,attachment.Transform)).ToArray()})
                    .Where(value=>value.Shapes.Length!=0).ToArray();
                if(affected.Length!=1||!ReferenceEquals(affected[0].Body.Entry,attachment.ParentEntry)
                    ||affected[0].Shapes.Length==0)
                    throw new InvalidOperationException("Missing initial attachment collider ownership is absent or ambiguous.");
                foreach(var shape in affected[0].Shapes)
                    if(!NativeBodyColliderCheckpoint.HasUniqueNonBodyAncestor(shape,attachment.Transform)
                        ||!NativeBodyColliderCheckpoint.Destroyed(shape))
                        throw new InvalidOperationException("Missing initial attachment collider ancestry/incarnation differs.");
                var unaffected=affected[0].Body.Colliders.Except(affected[0].Shapes).ToArray();
                NativeBodyColliderCheckpoint.ValidateCaptured(unaffected);
                NativeBodyColliderCheckpoint.RequireExactMembership(affected[0].Body.Body,unaffected);
                int affectedIndex=Array.IndexOf(survivingBodies,affected[0].Body);
                if(affectedIndex<0||survivingBodies.Count(value=>ReferenceEquals(value,affected[0].Body))!=1)
                    throw new InvalidOperationException("Missing initial attachment collider body membership changed.");
                survivingBodies[affectedIndex]=NativeBodyPoseCheckpoint.CloneWithColliders(affected[0].Body,unaffected);
                affectedBody=affected[0].Body;affectedShapes=affected[0].Shapes;
            }
            if(warp.EntitiesToDelete==null||warp.EntitiesToDelete.Any(value=>value<=0
                ||value==attachment.EntityId||value==body.EntityId)
                ||warp.EntitiesToDelete.Distinct().Count()!=warp.EntitiesToDelete.Count)
                throw new InvalidOperationException("Initial PhysicalAttachment recreation has an invalid deletion owner set.");
            var futureDeletionOwnerIds=new int[0];var concurrentMissingHistoricalEntityIds=new int[0];
            bool authorized=NativeInitialAttachmentDeletionAuthorization.Consume(
                warp,attachment.EntityId,body.EntityId,out futureDeletionOwnerIds,
                out concurrentMissingHistoricalEntityIds);
            if(warp.EntitiesToDelete.Count!=0&&!authorized)
                throw new InvalidOperationException("Initial PhysicalAttachment recreation with deletions lacks an exact scheduler authorization.");
            if(!authorized)
            {
                futureDeletionOwnerIds=new int[0];concurrentMissingHistoricalEntityIds=new int[0];
            }
            // A backward warp can recreate this initial plate alongside an
            // unrelated dynamic owner. Select by the plate's exact logical root
            // instead of requiring it to be the warp's only spawn row; the
            // dynamic plan independently authenticates every other row.
            var spawned=warp.Entities.Where(value=>value!=null&&!value.__isset.entityId
                &&value.__isset.entityPathReference&&value.EntityPathReference!=null
                &&value.EntityPathReference.Ids!=null&&value.EntityPathReference.Ids.Count==1
                &&value.EntityPathReference.Ids[0]==attachment.EntityId).ToArray();
            if(spawned.Length!=1||spawned[0].SpawningPath==null||spawned[0].SpawningPath.Count<2)
                throw new InvalidOperationException("Initial PhysicalAttachment recreation requires one exact observed spawn path for the missing owner.");
            var thriftPath=spawned[0].EntityPathReference;
            var logicalPath=thriftPath.FromThrift();
            var topologyRestore=topology.ValidateMissing(attachment,body,futureDeletionOwnerIds,
                concurrentMissingHistoricalEntityIds);
            var concurrentColliders=PrepareConcurrentColliderRecreations(topologyRestore,bodies,
                affectedBody,survivingBodies);
            return new NativeInitialAttachmentRecreation(attachment.EntityId,body.EntityId,
                logicalPath,attachment,body,affectedBody,affectedShapes,topology,
                topology.Plate(attachment.Entry),topologyRestore,detached,concurrentColliders);
        }

        private static ConcurrentColliderRecreation[] PrepareConcurrentColliderRecreations(
            TopologySnapshot.RestoreTransaction transaction,NativeBodyPoseCheckpoint.Snapshot[] bodies,
            NativeBodyPoseCheckpoint.Snapshot initialAffectedBody,
            NativeBodyPoseCheckpoint.Snapshot[] survivingBodies)
        {
            if(transaction==null||transaction.ConcurrentRecreations==null)
                throw new InvalidOperationException("Concurrent collider recreation topology is absent.");
            var result=new List<ConcurrentColliderRecreation>();
            foreach(var recreation in transaction.ConcurrentRecreations)
            {
                var historicalOwner=recreation==null||recreation.Pair==null?null:recreation.Pair.OwnerTransform;
                if(ReferenceEquals(historicalOwner,null)||!NativeBodyColliderCheckpoint.IsDestroyedUnityWrapper(historicalOwner))
                    throw new InvalidOperationException("Concurrent recreated attachment lacks one destroyed historical owner transform.");
                var affected=bodies.Select(value=>new {Body=value,Shapes=value.Colliders.Where(shape=>
                        NativeBodyColliderCheckpoint.HasAncestor(shape,historicalOwner)).ToArray()})
                    .Where(value=>value.Shapes.Length!=0).ToArray();
                if(affected.Length!=1||ReferenceEquals(affected[0].Body,initialAffectedBody)
                    ||affected[0].Shapes.Length==0)
                    throw new InvalidOperationException("Concurrent recreated attachment collider ownership is absent, shared, or ambiguous: "+recreation.OwnerId+".");
                foreach(var shape in affected[0].Shapes)
                    if(!NativeBodyColliderCheckpoint.HasUniqueNonBodyAncestor(shape,historicalOwner)
                        ||!NativeBodyColliderCheckpoint.Destroyed(shape))
                        throw new InvalidOperationException("Concurrent recreated attachment collider ancestry/incarnation differs: "+recreation.OwnerId+".");
                int affectedIndex=Array.FindIndex(survivingBodies,value=>ReferenceEquals(value,affected[0].Body));
                if(affectedIndex<0||survivingBodies.Count(value=>ReferenceEquals(value,affected[0].Body))!=1)
                    throw new InvalidOperationException("Concurrent recreated attachment collider body membership changed: "+recreation.OwnerId+".");
                var unaffected=survivingBodies[affectedIndex].Colliders.Except(affected[0].Shapes).ToArray();
                NativeBodyColliderCheckpoint.ValidateCaptured(unaffected);
                NativeBodyColliderCheckpoint.RequireExactMembership(affected[0].Body.Body,unaffected);
                survivingBodies[affectedIndex]=NativeBodyPoseCheckpoint.CloneWithColliders(affected[0].Body,unaffected);
                result.Add(new ConcurrentColliderRecreation {OwnerId=recreation.OwnerId,
                    HistoricalOwner=historicalOwner,AffectedBody=affected[0].Body,
                    AffectedShapes=affected[0].Shapes});
            }
            if(result.Select(value=>value.OwnerId).Distinct().Count()!=result.Count
                ||result.Select(value=>value.AffectedBody).Distinct().Count()!=result.Count)
                throw new InvalidOperationException("Concurrent recreated attachment collider mappings overlap.");
            return result.ToArray();
        }

        private static bool AttachmentMissing(NativeAttachmentPoseCheckpoint.Snapshot row)
        {
            if(row==null)throw new InvalidOperationException("Initial attachment checkpoint row is null.");
            var current=EntitySerialisationRegistry.GetEntry((uint)row.EntityId);
            if(ReferenceEquals(current,row.Entry)&&row.Object!=null)return false;
            if(current==null)return true;
            throw new InvalidOperationException("Initial PhysicalAttachment ID was reused before restore: "+row.EntityId);
        }

        private static bool BodyMissing(NativeBodyPoseCheckpoint.Snapshot row)
        {
            if(row==null)throw new InvalidOperationException("Initial body checkpoint row is null.");
            var current=EntitySerialisationRegistry.GetEntry((uint)row.EntityId);
            if(ReferenceEquals(current,row.Entry)&&row.Object!=null)return false;
            if(current==null)return true;
            throw new InvalidOperationException("Initial Rigidbody ID was reused before restore: "+row.EntityId);
        }

        internal bool ValidateMissingFixedEntity(EntityWarpSpec spec)
        {
            return NativeInitialAttachmentMissingEntityValidator.Validate(spec,ownerId,containerId,
                body.BodyPosition,body.BodyRotation,body.RawVelocity,body.RawAngularVelocity);
        }

        internal EntityPathReference ResolveMissingFixedEntityOwnerPath(EntityWarpSpec spec)
        {
            return ValidateMissingFixedEntity(spec)?path:null;
        }

        internal NativeInitialAttachmentSpawnChain ResolveLatentSpawnChain(EntityWarpSpec spec,
            EntitySerialisationEntry root)
        {
            if(spec==null||root==null||root.m_GameObject==null||initialPlate==null
                ||!spec.__isset.entityPathReference||spec.EntityPathReference==null
                ||!spec.EntityPathReference.FromThrift().Equals(path)
                ||spec.SpawningPath==null||spec.SpawningPath.Count!=3
                ||spec.SpawningPath[0]!=(int)root.m_Header.m_uEntityID
                ||spec.SpawningPath[1]!=0||spec.SpawningPath[2]!=0
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(root.m_Header.m_uEntityID),root))
                throw new InvalidOperationException("Latent initial-plate factory target/path identity differs.");

            var rootObject=root.m_GameObject;
            var stations=rootObject.GetComponents<PlateReturnStation>();
            var servers=rootObject.GetComponents<ServerPlateReturnStation>();
            var collections=rootObject.GetComponents<SpawnableEntityCollection>();
            if(stations.Length!=1||servers.Length!=1||collections.Length!=1
                ||stations[0].m_startingPlateNumber!=0)
                throw new InvalidOperationException("Latent initial-plate factory root is not one empty native plate-return station.");
            var rootChildren=collections[0].GetSpawnables().ToArray();
            var stackPrefab=stations[0].m_stackPrefab;
            if(rootChildren.Length!=1||stackPrefab==null||!ReferenceEquals(rootChildren[0],stackPrefab))
                throw new InvalidOperationException("Latent initial-plate factory root collection/prefab differs.");

            var cleanStacks=stackPrefab.GetComponents<CleanPlateStack>();
            var plateStacks=stackPrefab.GetComponents<PlateStackBase>();
            var stackAttachments=stackPrefab.GetComponents<PhysicalAttachment>();
            var stackColliders=stackPrefab.GetComponentsInChildren<Collider>(true).Where(value=>value!=null).ToArray();
            if(cleanStacks.Length!=1||plateStacks.Length!=1||!ReferenceEquals(cleanStacks[0],plateStacks[0])
                ||stackAttachments.Length!=1||stackColliders.Length!=1)
                throw new InvalidOperationException("Latent initial-plate stack prefab topology differs.");
            var platePrefab=plateStacks[0].m_platePrefab;
            var plates=platePrefab==null?new Plate[0]:platePrefab.GetComponents<Plate>();
            var plateAttachments=platePrefab==null?new PhysicalAttachment[0]:platePrefab.GetComponents<PhysicalAttachment>();
            var models=platePrefab==null?new InheritFromModelPrefab[0]:platePrefab.GetComponents<InheritFromModelPrefab>();
            if(plates.Length!=1||plateAttachments.Length!=1||models.Length!=1)
                throw new InvalidOperationException("Latent initial-plate prefab root component cardinality differs: plate="
                    +plates.Length+", physicalAttachment="+plateAttachments.Length+", model="+models.Length+".");
            if(initialPlate.Step==null||initialPlate.ModelPrefab==null)
                throw new InvalidOperationException("Latent initial-plate checkpoint asset signature is incomplete.");
            if(!ReferenceEquals(plates[0].m_platingStep,initialPlate.Step))
                throw new InvalidOperationException("Latent initial-plate prefab plating-step asset differs.");
            if(!ReferenceEquals(cleanStacks[0].GetPlatingStep(),initialPlate.Step))
                throw new InvalidOperationException("Latent initial-plate stack plating-step asset differs.");
            if(!ReferenceEquals(models[0].GetParentPrefab(),initialPlate.ModelPrefab))
                throw new InvalidOperationException("Latent initial-plate model-prefab asset differs.");
            var factoryAuthoring=TopologySnapshot.FactoryAuthoringComponents(platePrefab);
            var authoringDifference=TopologySnapshot.ComponentDifference(factoryAuthoring,initialPlate.AuthoringComponents);
            if(authoringDifference!=null)
                throw new InvalidOperationException("Latent initial-plate authoring component signature differs: "+authoringDifference+".");
            var factoryColliderTypes=TopologySnapshot.FactoryColliderTypes(platePrefab);
            var colliderTypeDifference=TopologySnapshot.ComponentDifference(factoryColliderTypes,initialPlate.ColliderTypes);
            if(colliderTypeDifference!=null)
                throw new InvalidOperationException("Latent initial-plate collider type signature differs: "+colliderTypeDifference+".");
            if(detachedOnContainer)
            {
                if(affectedBody!=null||affectedOwnerShapes.Length!=0||initialPlate.ColliderTypes.Length==0)
                    throw new InvalidOperationException("Latent detached initial-plate checkpoint has an unexpected collider-owner witness.");
            }
            else if(!FactoryCollidersMatch(platePrefab,affectedOwnerShapes,historicalOwner))
                throw new InvalidOperationException("Latent initial-plate collider topology or serialized properties differ.");
            topology.ValidateSurvivingPlatePeers(initialPlate);

            return new NativeInitialAttachmentSpawnChain {
                Prefabs=new[]{stackPrefab,platePrefab},
                Children=new[]{new[]{platePrefab},new GameObject[0]},
                Components=new[]{stackPrefab.GetComponents<Component>().Where(value=>value!=null)
                    .Select(value=>value.GetType()).ToArray(),(Type[])initialPlate.Components.Clone()},
                Colliders=new[]{stackColliders.Length,initialPlate.ColliderTypes.Length}
            };
        }

        private static bool FactoryCollidersMatch(GameObject prefab,
            NativeBodyColliderCheckpoint.Shape[] historical,Transform historicalOwner)
        {
            if(prefab==null||historical==null
                ||!NativeBodyColliderCheckpoint.IsDestroyedUnityWrapper(historicalOwner))return false;
            // The owner is necessarily a destroyed Unity wrapper here: its
            // CLR identity is retained by the checkpoint so the destroyed
            // collider ancestry can be matched, while Unity's overloaded
            // equality deliberately reports it as null.  A live owner would
            // mean this is not the qualified missing-owner recreation path.
            var current=prefab.GetComponentsInChildren<Collider>(true).Where(value=>value!=null).ToArray();
            if(current.Length!=historical.Length||current.Length==0)return false;
            var used=new HashSet<NativeBodyColliderCheckpoint.Shape>();
            foreach(var collider in current)
            {
                var matches=historical.Where(value=>!used.Contains(value)
                    &&SameFactoryCollider(collider,prefab.transform,value,historicalOwner)).ToArray();
                if(matches.Length!=1)return false;
                used.Add(matches[0]);
            }
            return used.Count==historical.Length;
        }

        private static bool SameFactoryCollider(Collider collider,Transform root,
            NativeBodyColliderCheckpoint.Shape historical,Transform historicalOwner)
        {
            if(collider==null||root==null||historical==null||historical.Ancestors==null)return false;
            var chain=new List<Transform>();
            for(var cursor=collider.transform;cursor!=null;cursor=cursor.parent)
            {
                chain.Add(cursor);if(ReferenceEquals(cursor,root))break;
            }
            if(chain.Count==0||!ReferenceEquals(chain[chain.Count-1],root))return false;
            int ownerIndex=Array.FindIndex(historical.Ancestors,value=>ReferenceEquals(value,historicalOwner));
            if(ownerIndex!=chain.Count-1||historical.ComponentIndex!=Array.FindIndex(
                collider.gameObject.GetComponents<Collider>(),value=>ReferenceEquals(value,collider)))return false;
            string kind;float[] geometry;Mesh mesh=null;bool convex=false;
            var box=collider as BoxCollider;var sphere=collider as SphereCollider;
            var capsule=collider as CapsuleCollider;var meshCollider=collider as MeshCollider;
            if(box!=null){kind="BoxCollider";geometry=V(box.center).Concat(V(box.size)).ToArray();}
            else if(sphere!=null){kind="SphereCollider";geometry=V(sphere.center).Concat(new[]{sphere.radius}).ToArray();}
            else if(capsule!=null){kind="CapsuleCollider";geometry=V(capsule.center).Concat(new[]{capsule.radius,capsule.height,(float)capsule.direction}).ToArray();}
            else if(meshCollider!=null){kind="MeshCollider";geometry=new float[0];mesh=meshCollider.sharedMesh;convex=meshCollider.convex;}
            else return false;
            if(kind!=historical.Kind||!geometry.SequenceEqual(historical.Geometry)
                ||collider.enabled!=historical.Enabled||collider.isTrigger!=historical.Trigger
                ||mesh!=historical.SharedMesh||convex!=historical.Convex)return false;
            for(int i=0;i<chain.Count;i++)
            {
                if(chain[i].gameObject.activeSelf!=historical.AncestorActive[i])return false;
                // The owner's attachment pose and scene name differ by design.
                // Layer and solid-collider material also differ legitimately:
                // PlacementLayerSwapper and PlacementCollisionSwapper change
                // them when the checkpointed plate is carried.  The normal
                // attachment callbacks reproduce those values after spawn,
                // and RequireRecreatedStaticContract then verifies their exact
                // historical postimage before the collider snapshot is rebound.
                // Every immutable descendant transform and collider property
                // must still be the exact serialized prefab topology here.
                if(i==chain.Count-1)continue;
                if(chain[i].gameObject.name!=historical.AncestorNames[i]
                    ||chain[i].GetSiblingIndex()!=historical.AncestorSiblingIndices[i]
                    ||!Same(chain[i].localPosition,historical.AncestorPositions[i])
                    ||!Same(chain[i].localRotation,historical.AncestorRotations[i])
                    ||!Same(chain[i].localScale,historical.AncestorScales[i]))return false;
            }
            return true;
        }

        private static float[] V(Vector3 value){return new[]{value.x,value.y,value.z};}
        private static bool Same(Vector3 a,Vector3 b){return a.x==b.x&&a.y==b.y&&a.z==b.z;}
        private static bool Same(UnityEngine.Quaternion a,UnityEngine.Quaternion b){return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w;}

        internal void Rebind(Dictionary<EntityPathReference,EntitySerialisationEntry> references,
            NativeAttachmentPoseCheckpoint.Snapshot[] attachments,
            NativeBodyPoseCheckpoint.Snapshot[] bodies)
        {
            if(attachmentIdentityRebound||bodyIdentityRebound)
                throw new InvalidOperationException("Initial PhysicalAttachment recreation identity was rebound twice.");
            if(references==null)throw new ArgumentNullException("references");
            EntitySerialisationEntry owner;
            if(!references.TryGetValue(path,out owner)||owner==null||owner.m_GameObject==null
                ||owner.m_Header.m_uEntityID!=ownerId
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)ownerId),owner))
                throw new InvalidOperationException("Recreated initial PhysicalAttachment owner is absent or has the wrong historical ID.");
            var physical=owner.m_GameObject.GetComponent<PhysicalAttachment>();
            var containerObject=physical==null||physical.m_container==null?null:physical.m_container.gameObject;
            var container=containerObject==null?null:EntitySerialisationRegistry.GetEntry(containerObject);
            if(container==null||container.m_Header.m_uEntityID!=containerId
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)containerId),container))
                throw new InvalidOperationException("Recreated initial PhysicalAttachment container has the wrong historical ID.");

            // A freshly spawned loose plate initially owns its two colliders on
            // the new PhysicalAttachment container.  The ordinary attachment
            // callback below will move them to the saved chef body.  Rebind the
            // addressable attachment now, but defer the body's strict
            // colliderless certification until that callback has run.
            var reboundAttachment=NativeAttachmentPoseCheckpoint.RebindDestroyed(
                attachment,owner,detachedOnContainer?container:null);
            if(detachedOnContainer)
            {
                var replacementParent=container.m_GameObject.transform;
                if(replacementParent==null||reboundAttachment.CachedClientParent==null
                    ||!ReferenceEquals(reboundAttachment.ParentEntry,container)
                    ||!ReferenceEquals(reboundAttachment.ParentObject,container.m_GameObject)
                    ||reboundAttachment.Parent==null)
                    throw new InvalidOperationException("Recreated detached initial attachment parent/cache contract is incomplete.");
            }
            topology.Rebind(attachment,body,owner,container,topologyRestore);
            Replace(attachments,attachment,reboundAttachment,"attachment");
            attachment=reboundAttachment;recreatedOwner=owner;recreatedContainer=container;
            attachmentIdentityRebound=true;
        }

        internal void FinalizeTopologyAfterDeletions()
        {
            // The no-deletion path preserves its old behavior by restoring
            // canonical order immediately after spawn. Complete reaches this
            // method again and has nothing left to defer.
            if(topologyRestore.Finalized)return;
            topology.FinalizeAfterDeletions(topologyRestore);
        }

        internal void FinalizeColliders(NativeBodyPoseCheckpoint.Snapshot[] bodies)
        {
            if(!attachmentIdentityRebound||bodyIdentityRebound||collidersRebound
                ||recreatedOwner==null||recreatedOwner.m_GameObject==null
                ||recreatedContainer==null||recreatedContainer.m_GameObject==null)
                throw new InvalidOperationException("Initial PhysicalAttachment collider rebind lifecycle differs.");
            var reboundContainerBody=NativeBodyPoseCheckpoint.RebindDestroyedColliderless(body,recreatedContainer);
            Replace(bodies,body,reboundContainerBody,"body");
            body=reboundContainerBody;bodyIdentityRebound=true;
            if(detachedOnContainer)
            {
                if(affectedBody!=null||affectedOwnerShapes.Length!=0)
                    throw new InvalidOperationException("Detached initial attachment unexpectedly retained a separate collider-owner checkpoint.");
            }
            else
            {
                var recreatedOwnerTransform=recreatedOwner.m_GameObject.transform;
                var reboundBody=RebindColliderOwner(affectedBody,affectedOwnerShapes,
                    historicalOwner,recreatedOwnerTransform,"initial attachment");
                Replace(bodies,affectedBody,reboundBody,"collider owner body");
                affectedBody=reboundBody;affectedOwnerShapes=reboundBody.Colliders.Where(shape=>
                    NativeBodyColliderCheckpoint.HasAncestor(shape,recreatedOwnerTransform)).ToArray();
            }
            foreach(var concurrent in concurrentColliderRecreations)
            {
                var entry=EntitySerialisationRegistry.GetEntry((uint)concurrent.OwnerId);
                var ownerObject=entry==null?null:entry.m_GameObject;
                if(ownerObject==null||entry.m_Header.m_uEntityID!=concurrent.OwnerId
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)concurrent.OwnerId),entry))
                    throw new InvalidOperationException("Concurrent recreated collider owner is absent: "+concurrent.OwnerId+".");
                var currentOwner=ownerObject.transform;
                var reboundBody=RebindColliderOwner(concurrent.AffectedBody,concurrent.AffectedShapes,
                    concurrent.HistoricalOwner,currentOwner,"concurrent attachment "+concurrent.OwnerId);
                Replace(bodies,concurrent.AffectedBody,reboundBody,"concurrent collider owner body");
                concurrent.AffectedBody=reboundBody;
                concurrent.AffectedShapes=reboundBody.Colliders.Where(shape=>
                    NativeBodyColliderCheckpoint.HasAncestor(shape,currentOwner)).ToArray();
            }
            collidersRebound=true;
        }

        private NativeBodyPoseCheckpoint.Snapshot RebindColliderOwner(
            NativeBodyPoseCheckpoint.Snapshot targetBody,NativeBodyColliderCheckpoint.Shape[] targetShapes,
            Transform oldOwner,Transform newOwner,string kind)
        {
            if(targetBody==null||targetShapes==null||targetShapes.Length==0
                ||ReferenceEquals(oldOwner,null)||newOwner==null)
                throw new InvalidOperationException("Recreated "+kind+" collider mapping is incomplete.");
            var hierarchyShapes=NativeBodyColliderCheckpoint.CaptureOwnerHierarchy(targetBody.Body,newOwner);
            if(hierarchyShapes.Length!=targetShapes.Length||hierarchyShapes.Length==0)
                throw new InvalidOperationException("Recreated "+kind+" collider hierarchy cardinality differs.");
            var topologyMatches=new HashSet<NativeBodyColliderCheckpoint.Shape>();
            var poseMappings=new List<KeyValuePair<NativeBodyColliderCheckpoint.Shape,NativeBodyColliderCheckpoint.Shape>>();
            foreach(var value in hierarchyShapes)
            {
                var matches=targetShapes.Where(target=>!topologyMatches.Contains(target)
                    &&NativeBodyColliderCheckpoint.SameRecreatedOwnerTopology(target,value,
                        oldOwner,newOwner)).ToArray();
                if(matches.Length!=1)
                    throw new InvalidOperationException("Recreated "+kind+" collider hierarchy mapping is ambiguous.");
                topologyMatches.Add(matches[0]);
                poseMappings.Add(new KeyValuePair<NativeBodyColliderCheckpoint.Shape,NativeBodyColliderCheckpoint.Shape>(matches[0],value));
            }
            bool posesChanged=NativeBodyColliderCheckpoint.RestoreRecreatedOwnerPoses(poseMappings,
                oldOwner,newOwner,targetBody.Body);
            if(posesChanged||hierarchyShapes.Any(value=>value.Collider.attachedRigidbody!=targetBody.Body))
            {
                colliderTransformSyncRequired=true;Physics.SyncTransforms();
            }
            var currentRows=NativeBodyPoseCheckpoint.Capture(new HashSet<int>{targetBody.EntityId});
            if(currentRows.Length!=1)throw new InvalidOperationException("Recreated "+kind+" collider body capture differs.");
            var current=currentRows[0];
            var currentOwnerShapes=current.Colliders.Where(shape=>
                NativeBodyColliderCheckpoint.HasAncestor(shape,newOwner)).ToArray();
            if(currentOwnerShapes.Length!=targetShapes.Length||currentOwnerShapes.Length==0
                ||current.Colliders.Length!=targetBody.Colliders.Length)
                throw new InvalidOperationException("Recreated "+kind+" collider membership cardinality differs.");
            var historicalOtherShapes=targetBody.Colliders.Except(targetShapes).ToArray();
            var usedOwnerShapes=new HashSet<NativeBodyColliderCheckpoint.Shape>();
            var usedOtherShapes=new HashSet<NativeBodyColliderCheckpoint.Shape>();
            var reboundShapes=new NativeBodyColliderCheckpoint.Shape[current.Colliders.Length];
            for(int i=0;i<current.Colliders.Length;i++)
            {
                var value=current.Colliders[i];
                if(NativeBodyColliderCheckpoint.HasAncestor(value,newOwner))
                {
                    var matches=targetShapes.Where(target=>!usedOwnerShapes.Contains(target)
                        &&NativeBodyColliderCheckpoint.SameRecreatedOwnerTopology(target,value,
                            oldOwner,newOwner)).ToArray();
                    if(matches.Length!=1)
                        throw new InvalidOperationException("Recreated "+kind+" collider path/type/component mapping is ambiguous.");
                    usedOwnerShapes.Add(matches[0]);
                    reboundShapes[i]=NativeBodyColliderCheckpoint.RebindRecreatedOwnerShape(matches[0],value,
                        oldOwner,newOwner,current.Body);
                }
                else
                {
                    var matches=historicalOtherShapes.Where(target=>!usedOtherShapes.Contains(target)
                        &&ReferenceEquals(target.Collider,value.Collider)).ToArray();
                    if(matches.Length!=1)
                        throw new InvalidOperationException("Surviving collider identity/order mapping changed during "+kind+" recreation.");
                    usedOtherShapes.Add(matches[0]);reboundShapes[i]=matches[0];
                }
            }
            if(usedOwnerShapes.Count!=targetShapes.Length||usedOtherShapes.Count!=historicalOtherShapes.Length)
                throw new InvalidOperationException("Recreated "+kind+" collider mapping is not bijective.");
            return NativeBodyPoseCheckpoint.RebindSurvivingWithRecreatedColliders(targetBody,current,reboundShapes);
        }

        internal void CommitLineage()
        {
            if(lineageCommitted||!attachmentIdentityRebound||!bodyIdentityRebound||!collidersRebound
                ||!topologyRestore.Finalized||recreatedOwner==null||recreatedContainer==null)
                throw new InvalidOperationException("Initial PhysicalAttachment lineage commit lifecycle differs.");
            NativeSceneMetadata.RebindInitialPhysicalAttachmentLineage(historicalOwnerEntry,recreatedOwner,
                historicalBodyEntry,recreatedContainer);
            lineageCommitted=true;
        }

        private static void Replace<T>(T[] values,T oldValue,T newValue,string kind) where T:class
        {
            int index=Array.IndexOf(values,oldValue);
            if(index<0||values.Count(value=>ReferenceEquals(value,oldValue))!=1)
                throw new InvalidOperationException("Initial PhysicalAttachment "+kind+" checkpoint membership changed before rebind.");
            values[index]=newValue;
        }

        internal object Diagnostics
        {
            get {return new Dictionary<string,object>{{"ownerId",ownerId},{"containerId",containerId},
                {"path",path.ToThrift().Ids.ToArray()},
                {"detachedOnContainer",detachedOnContainer},
                {"attachmentIdentityRebound",attachmentIdentityRebound},{"bodyIdentityRebound",bodyIdentityRebound},
                {"identityRebound",attachmentIdentityRebound&&bodyIdentityRebound},
                {"futureDeletionOwnerIds",topologyRestore.Deletions.Select(value=>value.OwnerId).ToArray()},
                {"retiredFuturePhysicsPairs",topologyRestore.RetiredFuturePhysicsPairs},
                {"alreadyRetiredFuturePhysicsPairs",topologyRestore.AlreadyRetiredFuturePhysicsPairs},
                {"topologyFinalized",topologyRestore.Finalized},
                {"colliderTransformSyncRequired",colliderTransformSyncRequired},
                {"collidersRebound",collidersRebound},
                {"lineageCommitted",lineageCommitted},
                {"rebound",attachmentIdentityRebound&&bodyIdentityRebound&&collidersRebound}};}
        }
    }
}
