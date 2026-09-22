using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Rebuilds a Rigidbody's PxRigidDynamic through Unity's own active-state
    // Create(false) -> Create(true) replacement path, then re-registers its
    // capsule on the new active actor. Automatic mode is restricted to local
    // chefs during the checkpoint restore's internal main-physics unfreeze.
    public sealed class RigidbodyActorRebuildModule : IAuthoringModule
    {
        private const uint NativeAbiVersion=20;
        // Four active chef actors plus Unity's one replacement allocation form
        // the observed five-address cycle. Rebuilding five times removes every
        // chef actor/contact set while restoring the incoming chef/address map.
        private const int AutomaticCanonicalCycles=5;
        private const int MaximumContactManagers=4096;
        private const int MaximumManifolds=4096;
        private const int MaximumShapeInstancePairs=4096;
        private const int MaximumNPhasePoolSlabs=128;
        private const int MaximumNPhasePoolSlabBytes=MaximumNPhasePoolSlabs*0x880;
        private const int NPhasePoolKindCount=5;
        private const int MaximumContactReportBufferSize=64*1024*1024;
        private const int MaximumDirtyInteractions=4096;
        private const int MaximumInteractionGraphActors=4096;
        private const int MaximumInteractionGraphInteractions=16384;
        private const int MaximumInteractionGraphActorSlots=32768;
        private const int MaximumInteractionGraphPoolEntries=65536;
        private const int MaximumTransformCacheIds=16384;
        private const int MaximumTransformCacheBindings=MaximumInteractionGraphInteractions*2;
        private const int MaximumBroadPhaseOverlaps=4096;
        private const int MaximumIslandNodes=16384;
        private const int MaximumIslandEdges=65536;
        private const int MaximumIslands=16384;
        private const int MaximumIslandRoots=16384;
        private const int MaximumIslandQueueEntries=65536;
        private const int MaximumIslandBindings=16384;
        private const int MaximumIslandJournalRecords=65536;
        private const uint IslandPhaseSettled=1;
        private const uint IslandPhasePreUpdate=2;
        private const uint IslandPhasePostUpdate=3;
        private const uint DirtyInteractionRestoreExact=1;
        private const uint DirtyInteractionRestoreProjection=2;
        private const int MaximumCheckpointSidecars=20000;
        private const uint LargeManifoldPoolKind=0;
        private const uint SphereManifoldPoolKind=1;

        [StructLayout(LayoutKind.Sequential, Pack=8)]
        private struct NativeReceipt
        {
            public uint ApiVersion, StructSize, Result, LastError;
            public UIntPtr UnityBase, Rigidbody, ActorBefore, ActorAfterInactiveCreate, ActorAfterActiveCreate;
        }

        [StructLayout(LayoutKind.Sequential, Pack=8)]
        private struct NativeContactPoolReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr Context,FreeArray;
            public uint FreeCount,OrderHashBefore,OrderHashAfter;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] Top;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeContactManagerOwnerRecord
        {
            public uint Slot,Membership;
            public UIntPtr Manager,Sip,PrimaryVtable,SecondaryVtable,ShapeSim0,ShapeSim1;
            public UIntPtr PxsShapeCore0,PxsShapeCore1,PxShape0,PxShape1;
            public UIntPtr RigidBody0,RigidBody1,RigidCore0,RigidCore1,Manifold,CachePointer;
            public uint PairData,TransformCache0,TransformCache1,ManagerFlags,SipFlags;
            public uint ContactReportStamp,ReportPairIndex,ReportStreamIndex;
            public UIntPtr ActorPair;
            public uint ManagerHash,SipHash,ManifoldHash,ManifoldBytes,CacheHash;
            public ushort CacheSize,ContactCount,WorkUnitFlags,StatusFlags;
            public byte InteractionType,InteractionFlags,GeomType0,GeomType1,DisableResponse,DisableCcd;
            public ushort ValidationFlags;
            public UIntPtr ActorPairActor0,ActorPairActor1,ActorPairScene;
            public ushort ActorPairInternalFlags,ActorPairTouchCount,ActorPairRefCount,ActorPairReserved;
            public UIntPtr ActorPairReportData;
            public uint ActorPairHash;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeContactManagerOwnerReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Context,Pool,FreeArray,Slabs,UseBitmap,ActiveBitmap,TouchBitmap,ModifiableBitmap;
            public uint ElementsPerSlab,MaximumSlabs,SlabCount,Log2ElementsPerSlab,FreeCount;
            public uint UseWordCount,ActiveWordCount,TouchWordCount,ModifiableWordCount;
            public uint TotalSlots,UsedCount,ActiveCount,TouchCount,ModifiableCount;
            public uint RecordsRequired,RecordsWritten,FreeOrderHash,FreeIndexOrderHash;
            public uint UseBitmapHash,ActiveBitmapHash,TouchBitmapHash,ModifiableBitmapHash,OwnerHash;
            public uint ConsistencyFlags,InvalidSlot,Detail;
        }

        [StructLayout(LayoutKind.Sequential, Pack=8)]
        private struct NativeManifoldPoolReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Context,Pool;
            public uint PoolKind;
            public UIntPtr FreeHeadBefore,FreeHeadAfter;
            public uint ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize,TraversedCount,OrderHashBefore,OrderHashAfter;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] TopBefore;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] TopAfter;
        }

        [StructLayout(LayoutKind.Sequential, Pack=8)]
        private struct NativeContextObserverReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,ObservedContext;
            public uint Observations,Installed;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeContactRecreatePlanRow
        {
            public UIntPtr PxsShapeCoreLow,PxsShapeCoreHigh,TargetManager,TargetManifold,TargetSip;
            public uint TargetSlot,ManifoldBytes,TargetManagerFlags,Flags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeContactRecreateReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Context,FreeArray,LargePool;
            public uint State,RowCount,MatchedCount,RemainingCount;
            public uint ContactCountBefore,TargetContactCount,ContactCountCurrent;
            public uint ContactHashBefore,TargetContactHash,ContactHashCurrent;
            public uint LargeCountBefore,TargetLargeCount,LargeCountCurrent;
            public uint LargeHashBefore,TargetLargeHash,LargeHashCurrent;
            public uint LargeUsedBefore,TargetLargeUsed,LargeUsedCurrent;
            public uint LargeUnreleasedBefore,TargetLargeUnreleased,LargeUnreleasedCurrent;
            public uint MatchedMask,ThreadId;
            public UIntPtr LastSip,LastShapeLow,LastShapeHigh,LastManager,LastManifold;
            public uint InvalidRow,Detail,Installed,Armed;
            public uint ObserverEntries,AttemptOrdinal;
            public UIntPtr AttemptSip,AttemptShapeLow,AttemptShapeHigh;
            public uint AttemptRow,AttemptMatchedMask,AttemptFreeCount,AttemptManagerIndex;
            public UIntPtr AttemptLargeHead;
            public uint AttemptManifoldIndex;
            public UIntPtr NPhaseCore,SipPool;
            public uint SipMatchedCount,SipRemainingCount;
            public uint TargetSipCount,TargetSipHash,TargetSipUsed,TargetSipUnreleased;
            public uint SipCountCurrent,SipHashCurrent,SipUsedCurrent,SipUnreleasedCurrent;
            public uint SipMatchedMask,SipObserverEntries;
            public UIntPtr LastAllocatedSip;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeContactRecreateAuditReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Context,NPhaseCore,SipPool,FreeArray,LargePool;
            public uint EvaluatedMask,IssueMask,RecreateState,ObserverInstalled;
            public uint RowCount,InvalidRowCount,DuplicateRowCount;
            public uint TargetFreeSipRowCount,ActiveSipRowCount;
            public uint TargetSipCount,TargetSipHash,TargetSipUsed,TargetSipUnreleased;
            public uint LiveSipCount,LiveSipHash,LiveSipUsed,LiveSipUnreleased;
            public uint ExpectedSemanticSipCount,ExpectedLegacySipCount;
            public uint SipMissingCount,SipExtraCount;
            public uint TargetContactCount,TargetContactHash;
            public uint LiveContactCount,LiveContactHash,ExpectedContactCount;
            public uint ContactMissingCount,ContactExtraCount;
            public uint UseBitmapCount,ActiveBitmapCount,TouchBitmapCount,ModifiableBitmapCount;
            public uint TargetLargeCount,TargetLargeHash,TargetLargeUsed,TargetLargeUnreleased;
            public uint LiveLargeCount,LiveLargeHash,LiveLargeUsed,LiveLargeUnreleased;
            public uint ExpectedLargeCount,LargeMissingCount,LargeExtraCount;
            public uint UnwritableCount,FirstResult,FirstError,FirstRow,FirstDetail;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeSipPoolReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,Pool,FreeHead;
            public uint ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize;
            public uint TraversedCount,OrderHash,ValidationFlags;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] Top;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeActorPairPoolReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,Pool,FreeHead,Slabs;
            public uint ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize,SlabCount;
            public uint TotalElements,FreeCount,FreeOrderHash,AllocatedCount,AllocatedOrderHash,ValidationFlags;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] TopFree;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] TopAllocated;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeActorPairReportPoolReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,Pool,FreeHead,Slabs;
            public uint ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize,SlabCount;
            public uint TotalElements,FreeCount,FreeOrderHash,AllocatedCount,AllocatedOrderHash,ValidationFlags;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] TopFree;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=16)] public UIntPtr[] TopAllocated;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeNPhasePoolSnapshotBuffers
        {
            public IntPtr SlabBases;public uint SlabBaseCapacity;
            public IntPtr FreeSlots;public uint FreeSlotCapacity;
            public IntPtr AllocationWords;public uint AllocationWordCapacity;
            public IntPtr SlabBytes;public uint SlabByteCapacity;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeNPhasePoolSnapshotReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,Pool,SlabsData,FreeHead;
            public uint PoolKind,PoolOffset,ElementSize,ElementsPerSlab,SlabSize;
            public uint SlabCount,SlabCapacityRaw,TotalSlots,Used,Unreleased,FreeHeadSlot;
            public uint SlabBasesRequired,SlabBasesWritten;
            public uint FreeSlotsRequired,FreeSlotsWritten;
            public uint AllocationWordsRequired,AllocationWordsWritten;
            public uint SlabBytesRequired,SlabBytesWritten;
            public uint MetadataHash,SlabBaseHash,FreeSlotOrderHash;
            public uint AllocationBitmapHash,SlabByteHash,SnapshotHash;
            public uint ValidationFlags,InvalidKind,InvalidIndex,Detail;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeNPhaseReportStateReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,OwnerScene,ActorPairData;
            public uint ActorPairCount,ActorPairCapacityRaw;
            public UIntPtr PersistentData;
            public uint PersistentCount,PersistentCapacityRaw,NextFramePersistentIndex;
            public UIntPtr ForceThresholdData;
            public uint ForceThresholdCount,ForceThresholdCapacityRaw;
            public UIntPtr ReportBuffer;
            public uint ReportBufferCurrentIndex,ReportBufferCurrentSize;
            public uint ReportBufferDefaultSize,ReportBufferLastIndex;
            public uint ReportBufferAllocationLocked;
            public uint ActorPairOrderHash,PersistentOrderHash,ForceThresholdOrderHash;
            public uint ReportBufferActiveHash,ReportBufferAllocationHash,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeInteractionGraphActorRecord
        {
            public UIntPtr Actor,Vtable;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=4)] public UIntPtr[] InlineSlots;
            public UIntPtr InteractionsData,FirstElement,InteractionScene;
            public uint SceneArrayIndex,InteractionOutputStart,InteractionCount,InteractionCapacity;
            public uint ActiveBodyIndex,InteractionOrderHash;
            public ushort TransferringCount,UniqueCount,CountedCount;
            public byte ActorType,IslandNodeInfo;
            public uint ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeInteractionGraphInteractionRecord
        {
            public UIntPtr Interaction,Vtable,Actor0,Actor1,Element0,Element1;
            public UIntPtr ShapeCore0,ShapeCore1,PxsShapeCore0,PxsShapeCore1;
            public UIntPtr SemanticLow,SemanticHigh;
            public uint SceneId,GlobalIndex,Active;
            public ushort ActorId0,ActorId1;
            public byte InteractionType,InteractionFlags;
            public ushort Reserved;
            public uint ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeInteractionGraphPoolReceipt
        {
            public UIntPtr Pool,SlabData,FreeHead;
            public uint BlockCapacity,BlockBytes,InlineBufferUsed,SlabCount,SlabCapacityRaw;
            public uint ElementsPerSlab,Used,UnreleasedFree,SlabSize,TotalElements,FreeCount;
            public uint SlabOutputStart,FreeOutputStart,SlabOrderHash,FreeOrderHash,UsedOwnerHash;
            public uint ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeInteractionGraphReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,OwnerScene,InteractionScene,LlContext;
            public uint Timestamp;
            public UIntPtr ActiveBodiesData;
            public uint ActiveBodiesCount,ActiveBodiesCapacityRaw,ActiveTwoWayStart;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)] public UIntPtr[] GlobalData;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)] public uint[] GlobalCount;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)] public uint[] GlobalCapacityRaw;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)] public uint[] GlobalActiveCount;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=6)] public uint[] GlobalOrderHash;
            public uint ActiveBodiesRequired,ActiveBodiesWritten,ActorsRequired,ActorsWritten;
            public uint InteractionsRequired,InteractionsWritten,ActorSlotsRequired,ActorSlotsWritten;
            public uint PoolSlabsRequired,PoolSlabsWritten,PoolFreeRequired,PoolFreeWritten;
            public uint ActorHash,InteractionHash,ActorSlotHash,PoolHash,GraphHash;
            public uint ValidationFlags,InvalidKind,InvalidIndex,Detail;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=3)] public NativeInteractionGraphPoolReceipt[] Pools;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeTransformCacheEntryRecord
        {
            public uint Id,RefCount;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=4)] public float[] Rotation;
            [MarshalAs(UnmanagedType.ByValArray,SizeConst=3)] public float[] Position;
            public uint PoseHash,BindingCount,StateFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeTransformCacheBindingRecord
        {
            public UIntPtr ShapeSim,ShapeCore,PxsShapeCore,Interaction;
            public uint InteractionIndex,EndpointIndex,CacheId,RefCount,PoseHash,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeTransformCacheReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,OwnerScene,InteractionScene,Context,TransformCache;
            public uint CurrentId;
            public UIntPtr FreeData;
            public uint FreeCount,FreeCapacityRaw;
            public UIntPtr TransformsData;
            public uint TransformsCount,TransformsCapacityRaw;
            public UIntPtr RefCountsData;
            public uint RefCountsCount,RefCountsCapacityRaw;
            public uint EntriesRequired,EntriesWritten,FreeRequired,FreeWritten;
            public uint BindingsRequired,BindingsWritten,LiveCount,TotalRefCount;
            public uint EntryHash,FreeOrderHash,BindingHash,SnapshotHash;
            public uint ValidationFlags,InvalidKind,InvalidIndex,Detail;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeBroadPhaseOverlapRecord
        {
            public UIntPtr UserData0,UserData1,ShapeCore0,ShapeCore1,PxsShapeCore0,PxsShapeCore1;
            public uint CacheId0,CacheId1,PairHash,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeFinishBroadPhaseObserverReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,ExpectedScene,ExpectedContext,ExpectedNPhaseCore;
            public UIntPtr ObservedScene,ObservedContext,ObservedNPhaseCore,AabbManager;
            public UIntPtr InteractionScene,TransformCache;
            public uint Installed,State,ExpectedPass,ArmedThreadId,ArmedOrdinal;
            public uint ObservationOrdinal,SlotIndex,Pass,ThreadId;
            public uint CreatedRequired,CreatedWritten,DeletedRequired,DeletedWritten;
            public uint CreatedHash,DeletedHash,PreCacheHash,PostCacheHash;
            public uint PreGraphHash,PostGraphHash,ValidationFlags,InvalidKind,InvalidIndex,Detail;
            public uint DroppedObservations;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandNodeSlotRecord
        {
            public uint Id,OwnerRaw,IslandId,RawFlagsWord,FreeNext,NextNode,SlotFlags,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandEdgeSlotRecord
        {
            public uint Id,Node0,Node1,TaggedRaw,FreeNext,NextEdge,SlotFlags,SemanticBindingIndex,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandSlotRecord
        {
            public uint Id,StartNode,StartEdge,EndNode,EndEdge,FreeNext,SlotFlags,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandRootSlotRecord
        {
            public uint Id,LinkHandle,Owner,FreeNext,SlotFlags,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandSipBindingRecord
        {
            public uint EdgeId,EdgeType,Sip,HookAddress,ShapeSim0,ShapeSim1;
            public uint PxsLow,PxsHigh,ContactManager,TaggedRaw,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandJournalRecord
        {
            public uint Ordinal,EventKind,ObserverPhase,ThreadId,EdgeType,Node0,Node1;
            public uint PreEdgeId,PostEdgeId,HookAddress,OwnerObject,PxsLow,PxsHigh,ValidationFlags;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandSnapshotBuffers
        {
            public IntPtr Nodes;public uint NodeCapacity;
            public IntPtr Edges;public uint EdgeCapacity;
            public IntPtr Islands;public uint IslandCapacity;
            public IntPtr Roots;public uint RootCapacity;
            public IntPtr KinematicWords;public uint KinematicWordCapacity;
            public IntPtr KinematicChangeWords;public uint KinematicChangeWordCapacity;
            public IntPtr NotReadyWords;public uint NotReadyWordCapacity;
            public IntPtr NotReadyChangeWords;public uint NotReadyChangeWordCapacity;
            public IntPtr IslandWords;public uint IslandWordCapacity;
            public IntPtr NodeCreated;public uint NodeCreatedCapacity;
            public IntPtr NodeDeleted;public uint NodeDeletedCapacity;
            public IntPtr EdgeCreated;public uint EdgeCreatedCapacity;
            public IntPtr EdgeDeleted;public uint EdgeDeletedCapacity;
            public IntPtr EdgeBroken;public uint EdgeBrokenCapacity;
            public IntPtr EdgeJoined;public uint EdgeJoinedCapacity;
            public IntPtr Bindings;public uint BindingCapacity;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandElementManagerReceipt
        {
            public UIntPtr Vtable,Elements,FreeNext,NextList;
            public uint Capacity,FreeHead,FreeCount,Required,Written,ElementHash,FreeChainHash,NextHash;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandQueueReceipt
        {
            public UIntPtr Data;
            public uint Count,Capacity,DefaultCapacity,Required,Written,Hash;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandBitmapReceipt
        {
            public UIntPtr Data;
            public uint WordCount,Required,Written,Hash;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandSnapshotReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,OwnerScene,InteractionScene,Context,IslandManager;
            public uint Phase,ObserverSequence,ObservationOrdinal,CaptureThreadId,Epoch;
            public NativeIslandElementManagerReceipt NodeManager,EdgeManager,IslandManagerReceipt,RootManager;
            public NativeIslandQueueReceipt NodeCreated,NodeDeleted,EdgeCreated,EdgeDeleted,EdgeBroken,EdgeJoined;
            public NativeIslandBitmapReceipt Kinematic,KinematicChange,NotReady,NotReadyChange,IslandBitmap;
            public uint NumAddedRBodies,NumAddedArtics,NumAddedKinematics;
            public uint NumAddedEdgesContact,NumAddedEdgesConstraint,NumAddedEdgesArticulation;
            public uint NumEdgeRefsToKinematic,NumRequiredKinematicDuplicates;
            public uint EverythingAsleep,HasAnythingChanged,PerformIslandUpdate;
            public uint LiveContactEdges,LiveConstraintEdges,LiveArticulationEdges;
            public uint BindingsRequired,BindingsWritten,BindingHash;
            public uint JournalBeginOrdinal,JournalEndOrdinal,JournalOverflowCount;
            public uint SnapshotHash,ValidationFlags,InvalidKind,InvalidIndex,Detail;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandObserverReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,ExpectedManager,ExpectedContext,ExpectedNPhase;
            public UIntPtr ObservedManager,ObservedContext,ObservedNPhase;
            public uint Installed,State,ExpectedPass,ArmedThreadId,ObserverSequence,ArmedOrdinal;
            public uint ObservationOrdinal,SlotIndex,Pass,ThreadId,PreResult,PostResult;
            public uint PreSnapshotHash,PostSnapshotHash,JournalBeginOrdinal,JournalEndOrdinal;
            public uint ValidationFlags,InvalidKind,InvalidIndex,Detail,InFlight;
        }

        [StructLayout(LayoutKind.Sequential,Pack=4)]
        private struct NativeIslandJournalReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,ExpectedManager;
            public uint Installed,State,FirstOrdinal,NextOrdinal,RequestedBegin;
            public uint RecordsRequired,RecordsWritten,OverflowCount,AddCount,RemoveCount,RecordHash;
            public uint ValidationFlags,InvalidKind,InvalidIndex,Detail;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeDirtyInteractionKey
        {
            public UIntPtr ElementLow,ElementHigh,PrimaryVtable;
            public uint InteractionType;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeDirtyInteractionReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,NPhaseCore,Set,Entries,EntriesNext,Hash;
            public uint EntriesCapacity,HashSize,Count,Action,Captures,Restores;
            public uint OrderHashBefore,OrderHashAfter,Installed,Armed;
            public uint RestoreMode,MatchedCount,CapturedOnlyCount,LiveOnlyCount;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NativeApiVersion();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeRebuildBatch(
            UIntPtr unityBase, IntPtr rigidbodies, uint count, IntPtr receipts);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeActorShapes(
            UIntPtr unityBase,UIntPtr actor,IntPtr shapes,uint capacity,out uint count,out uint error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContactPoolCaptureSnapshot(
            UIntPtr context,IntPtr snapshot,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContactPoolRestoreSnapshot(
            UIntPtr context,UIntPtr expectedFreeArray,IntPtr snapshot,uint count,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContactManagerActiveOwners(
            UIntPtr unityBase,UIntPtr context,IntPtr records,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeManifoldPoolCaptureSnapshot(
            UIntPtr unityBase,UIntPtr context,uint poolKind,IntPtr snapshot,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeManifoldPoolRestoreSnapshot(
            UIntPtr unityBase,UIntPtr context,uint poolKind,UIntPtr expectedPool,IntPtr snapshot,uint count,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeSipPoolCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr snapshot,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeActorPairPoolCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr freeSnapshot,uint freeCapacity,
            IntPtr allocatedSnapshot,uint allocatedCapacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeActorPairReportPoolCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr freeSnapshot,uint freeCapacity,
            IntPtr allocatedSnapshot,uint allocatedCapacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeNPhasePoolCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,uint poolKind,IntPtr buffers,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeNPhaseReportStateCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr actorPairs,uint actorPairCapacity,
            IntPtr persistentSips,uint persistentCapacity,IntPtr forceThresholdSips,
            uint forceThresholdCapacity,IntPtr reportBufferBytes,uint reportBufferCapacity,
            IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeInteractionGraphCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr activeBodies,uint activeBodyCapacity,
            IntPtr actors,uint actorCapacity,IntPtr interactions,uint interactionCapacity,
            IntPtr actorSlots,uint actorSlotCapacity,IntPtr poolSlabs,uint poolSlabCapacity,
            IntPtr poolFree,uint poolFreeCapacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeTransformCacheCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr entries,uint entryCapacity,
            IntPtr freeIds,uint freeCapacity,IntPtr bindings,uint bindingCapacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeFinishBroadPhaseObserverAction(
            UIntPtr unityBase,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeFinishBroadPhaseObserverArm(
            UIntPtr unityBase,UIntPtr expectedScene,UIntPtr expectedContext,
            UIntPtr expectedNPhaseCore,uint expectedPass,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeFinishBroadPhaseObserverCopy(
            UIntPtr unityBase,uint exactObservationOrdinal,IntPtr created,uint createdCapacity,
            IntPtr deleted,uint deletedCapacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIslandCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,uint expectedPhase,IntPtr buffers,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIslandRestoreSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr request,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIslandObserverInstall(
            UIntPtr unityBase,UIntPtr expectedManager,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIslandObserverAction(
            UIntPtr unityBase,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIslandObserverArm(
            UIntPtr unityBase,UIntPtr expectedManager,UIntPtr expectedNphase,uint expectedPass,
            IntPtr preBuffers,IntPtr preReceipt,IntPtr postBuffers,IntPtr postReceipt,IntPtr observerReceipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIslandObserverCopy(
            UIntPtr unityBase,uint exactObservationOrdinal,IntPtr preBuffers,IntPtr preReceipt,
            IntPtr postBuffers,IntPtr postReceipt,IntPtr observerReceipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeIslandJournalCopy(
            UIntPtr unityBase,uint beginOrdinal,IntPtr records,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContextObserverAction(
            UIntPtr unityBase,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContactRecreateArm(
            UIntPtr unityBase,UIntPtr context,UIntPtr nphaseCore,UIntPtr expectedSipPool,
            IntPtr targetSip,uint targetSipCount,uint targetSipUsed,uint targetSipUnreleased,
            UIntPtr expectedFreeArray,
            IntPtr targetContact,uint targetContactCount,UIntPtr expectedLargePool,
            IntPtr targetLarge,uint targetLargeCount,uint targetLargeUsed,uint targetLargeUnreleased,
            IntPtr rows,uint rowCount,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContactRecreateAudit(
            UIntPtr unityBase,UIntPtr context,UIntPtr nphaseCore,UIntPtr expectedSipPool,
            IntPtr targetSip,uint targetSipCount,uint targetSipUsed,uint targetSipUnreleased,
            UIntPtr expectedFreeArray,IntPtr targetContact,uint targetContactCount,
            UIntPtr expectedLargePool,IntPtr targetLarge,uint targetLargeCount,
            uint targetLargeUsed,uint targetLargeUnreleased,IntPtr rows,uint rowCount,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContactRecreateStatus(
            UIntPtr unityBase,UIntPtr context,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContactRecreateCancel(
            UIntPtr unityBase,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeDirtyInteractionAction(
            UIntPtr unityBase,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeDirtyInteractionLastNPhase(
            UIntPtr unityBase,out UIntPtr nphaseCore,out uint observations);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeDirtyInteractionCaptureCopy(
            UIntPtr unityBase,IntPtr keys,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeDirtyInteractionCaptureSnapshot(
            UIntPtr unityBase,UIntPtr nphaseCore,IntPtr keys,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeDirtyInteractionRestoreArm(
            UIntPtr unityBase,UIntPtr nphaseCore,UIntPtr entries,UIntPtr entriesNext,UIntPtr hash,
            uint entriesCapacity,uint hashSize,IntPtr keys,uint count,uint restoreMode,IntPtr receipt);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32",SetLastError=true)] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);
        [DllImport("kernel32",SetLastError=true)] private static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32",SetLastError=true)] private static extern bool ReadProcessMemory(
            IntPtr process,IntPtr address,[Out] byte[] buffer,UIntPtr size,out UIntPtr bytesRead);
        [DllImport("kernel32",SetLastError=true)] private static extern bool WriteProcessMemory(
            IntPtr process,IntPtr address,byte[] buffer,UIntPtr size,out UIntPtr bytesWritten);

        private sealed class BodyState
        {
            internal Vector3 Position,Velocity,AngularVelocity;
            internal Quaternion Rotation;
            internal float Mass,Drag,AngularDrag,SleepThreshold,MaxAngularVelocity;
            internal bool Kinematic,Gravity,DetectCollisions,Sleeping;
            internal RigidbodyConstraints Constraints;
            internal RigidbodyInterpolation Interpolation;
            internal CollisionDetectionMode CollisionDetection;
            internal int SolverIterations,SolverVelocityIterations;
        }

        private sealed class CapsuleState
        {
            internal int InstanceId,MaterialId,Direction;
            internal Rigidbody Attached;
            internal Vector3 Center;
            internal float Radius,Height,ContactOffset;
            internal bool Enabled,Trigger;
        }

        private sealed class TransformDispatchEntryState
        {
            internal uint Hierarchy,MaskLow,MaskHigh;
        }

        private sealed class TransformDispatchState
        {
            internal uint Dispatch,Array,Capacity,Count,GlobalMaskLow,GlobalMaskHigh;
            internal TransformDispatchEntryState[] Entries;
        }

        private sealed class ManifoldPoolState
        {
            internal uint PoolKind,Pool,OrderHash,ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize;
            internal uint[] Order;
        }

        private sealed class SipPoolState
        {
            internal uint NPhaseCore,Pool,OrderHash,ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize;
            internal uint[] Order;
        }

        private sealed class ActorPairPoolState
        {
            internal uint NPhaseCore,Pool,FreeOrderHash,AllocatedOrderHash;
            internal uint ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize,SlabCount,TotalElements;
            internal uint[] FreeOrder,AllocatedOrder;
        }

        private sealed class ActorPairReportPoolState
        {
            internal uint NPhaseCore,Pool,FreeOrderHash,AllocatedOrderHash;
            internal uint ElementSize,ElementsPerSlab,Used,Unreleased,SlabSize,SlabCount,TotalElements;
            internal uint[] FreeOrder,AllocatedOrder;
            internal byte[][] AllocatedBytes;
        }

        private sealed class NPhasePoolImageState
        {
            internal NativeNPhasePoolSnapshotReceipt Receipt;
            internal uint[] SlabBases,FreeSlots,AllocationWords;
            internal byte[] SlabBytes;
        }

        private sealed class NPhaseReportState
        {
            internal NativeNPhaseReportStateReceipt Receipt;
            internal uint[] ActorPairs,PersistentSips,ForceThresholdSips;
            internal byte[] ReportBufferBytes;
        }

        private sealed class InteractionGraphState
        {
            internal NativeInteractionGraphReceipt Receipt;
            internal uint[] ActiveBodies,ActorSlots,PoolSlabs,PoolFree;
            internal NativeInteractionGraphActorRecord[] Actors;
            internal NativeInteractionGraphInteractionRecord[] Interactions;
            // InteractionScene stores the secondary Interaction base for rigid
            // element pairs.  Retain each primary object's bounded image so a
            // predecessor restore has the dirty/core words, trigger cache, and
            // marker history that the public graph fields do not expose.
            internal byte[][] PrimaryBytes;
        }

        private sealed class TransformCacheState
        {
            internal NativeTransformCacheReceipt Receipt;
            internal NativeTransformCacheEntryRecord[] Entries;
            internal uint[] FreeIds;
            internal NativeTransformCacheBindingRecord[] Bindings;
        }

        private sealed class FinishBroadPhaseState
        {
            internal NativeFinishBroadPhaseObserverReceipt Receipt;
            internal NativeBroadPhaseOverlapRecord[] Created,Deleted;
        }

        private sealed class IslandSnapshotState
        {
            internal NativeIslandSnapshotReceipt Receipt;
            internal NativeIslandNodeSlotRecord[] Nodes;
            internal NativeIslandEdgeSlotRecord[] Edges;
            internal NativeIslandSlotRecord[] Islands;
            internal NativeIslandRootSlotRecord[] Roots;
            internal uint[] KinematicWords,KinematicChangeWords,NotReadyWords,NotReadyChangeWords,IslandWords;
            internal uint[] NodeCreated,NodeDeleted,EdgeCreated,EdgeDeleted,EdgeBroken,EdgeJoined;
            internal NativeIslandSipBindingRecord[] Bindings;
            internal byte[] RawBytes;
        }

        private sealed class IslandTransitionState
        {
            internal NativeIslandObserverReceipt Receipt;
            internal IslandSnapshotState Pre,Post;
            internal NativeIslandJournalReceipt JournalReceipt;
            internal NativeIslandJournalRecord[] Journal;
            internal byte[] JournalRawBytes;
        }

        private sealed class IslandSnapshotBufferOwner : IDisposable
        {
            internal NativeIslandSnapshotBuffers Buffers;
            internal IntPtr BuffersPointer,ReceiptPointer;
            internal IntPtr Nodes,Edges,Islands,Roots;
            internal IntPtr KinematicWords,KinematicChangeWords,NotReadyWords,NotReadyChangeWords,IslandWords;
            internal IntPtr NodeCreated,NodeDeleted,EdgeCreated,EdgeDeleted,EdgeBroken,EdgeJoined,Bindings;
            private bool disposed;

            internal static IslandSnapshotBufferOwner Create()
            {
                ValidateIslandManagedAbiSizes();
                var value=new IslandSnapshotBufferOwner();
                try
                {
                    value.Nodes=Alloc(MaximumIslandNodes,Marshal.SizeOf(typeof(NativeIslandNodeSlotRecord)));
                    value.Edges=Alloc(MaximumIslandEdges,Marshal.SizeOf(typeof(NativeIslandEdgeSlotRecord)));
                    value.Islands=Alloc(MaximumIslands,Marshal.SizeOf(typeof(NativeIslandSlotRecord)));
                    value.Roots=Alloc(MaximumIslandRoots,Marshal.SizeOf(typeof(NativeIslandRootSlotRecord)));
                    int nodeWords=(MaximumIslandNodes+31)/32,islandWords=(MaximumIslands+31)/32;
                    value.KinematicWords=Alloc(nodeWords,sizeof(uint));
                    value.KinematicChangeWords=Alloc(nodeWords,sizeof(uint));
                    value.NotReadyWords=Alloc(nodeWords,sizeof(uint));
                    value.NotReadyChangeWords=Alloc(nodeWords,sizeof(uint));
                    value.IslandWords=Alloc(islandWords,sizeof(uint));
                    value.NodeCreated=Alloc(MaximumIslandQueueEntries,sizeof(uint));
                    value.NodeDeleted=Alloc(MaximumIslandQueueEntries,sizeof(uint));
                    value.EdgeCreated=Alloc(MaximumIslandQueueEntries,sizeof(uint));
                    value.EdgeDeleted=Alloc(MaximumIslandQueueEntries,sizeof(uint));
                    value.EdgeBroken=Alloc(MaximumIslandQueueEntries,sizeof(uint));
                    value.EdgeJoined=Alloc(MaximumIslandQueueEntries,sizeof(uint));
                    value.Bindings=Alloc(MaximumIslandBindings,
                        Marshal.SizeOf(typeof(NativeIslandSipBindingRecord)));
                    value.Buffers=new NativeIslandSnapshotBuffers {
                        Nodes=value.Nodes,NodeCapacity=MaximumIslandNodes,
                        Edges=value.Edges,EdgeCapacity=MaximumIslandEdges,
                        Islands=value.Islands,IslandCapacity=MaximumIslands,
                        Roots=value.Roots,RootCapacity=MaximumIslandRoots,
                        KinematicWords=value.KinematicWords,KinematicWordCapacity=(uint)nodeWords,
                        KinematicChangeWords=value.KinematicChangeWords,KinematicChangeWordCapacity=(uint)nodeWords,
                        NotReadyWords=value.NotReadyWords,NotReadyWordCapacity=(uint)nodeWords,
                        NotReadyChangeWords=value.NotReadyChangeWords,NotReadyChangeWordCapacity=(uint)nodeWords,
                        IslandWords=value.IslandWords,IslandWordCapacity=(uint)islandWords,
                        NodeCreated=value.NodeCreated,NodeCreatedCapacity=MaximumIslandQueueEntries,
                        NodeDeleted=value.NodeDeleted,NodeDeletedCapacity=MaximumIslandQueueEntries,
                        EdgeCreated=value.EdgeCreated,EdgeCreatedCapacity=MaximumIslandQueueEntries,
                        EdgeDeleted=value.EdgeDeleted,EdgeDeletedCapacity=MaximumIslandQueueEntries,
                        EdgeBroken=value.EdgeBroken,EdgeBrokenCapacity=MaximumIslandQueueEntries,
                        EdgeJoined=value.EdgeJoined,EdgeJoinedCapacity=MaximumIslandQueueEntries,
                        Bindings=value.Bindings,BindingCapacity=MaximumIslandBindings
                    };
                    value.BuffersPointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeIslandSnapshotBuffers)));
                    value.ReceiptPointer=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(NativeIslandSnapshotReceipt)));
                    Marshal.StructureToPtr(value.Buffers,value.BuffersPointer,false);
                    Zero(value.ReceiptPointer,Marshal.SizeOf(typeof(NativeIslandSnapshotReceipt)));
                    return value;
                }
                catch{value.Dispose();throw;}
            }

            private static void ValidateIslandManagedAbiSizes()
            {
                if(Marshal.SizeOf(typeof(NativeIslandNodeSlotRecord))!=32||
                    Marshal.SizeOf(typeof(NativeIslandEdgeSlotRecord))!=36||
                    Marshal.SizeOf(typeof(NativeIslandSlotRecord))!=32||
                    Marshal.SizeOf(typeof(NativeIslandRootSlotRecord))!=24||
                    Marshal.SizeOf(typeof(NativeIslandSipBindingRecord))!=44||
                    Marshal.SizeOf(typeof(NativeIslandJournalRecord))!=56||
                    Marshal.SizeOf(typeof(NativeIslandSnapshotBuffers))!=128||
                    Marshal.SizeOf(typeof(NativeIslandElementManagerReceipt))!=48||
                    Marshal.SizeOf(typeof(NativeIslandQueueReceipt))!=28||
                    Marshal.SizeOf(typeof(NativeIslandBitmapReceipt))!=20||
                    Marshal.SizeOf(typeof(NativeIslandSnapshotReceipt))!=620||
                    Marshal.SizeOf(typeof(NativeIslandObserverReceipt))!=128||
                    Marshal.SizeOf(typeof(NativeIslandJournalReceipt))!=84)
                    throw new InvalidOperationException("Managed island API17 ABI sizes differ.");
            }

            private static IntPtr Alloc(int count,int size)
            {
                return Marshal.AllocHGlobal(checked(count*size));
            }

            internal static void Zero(IntPtr pointer,int size)
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(pointer,i,0);
            }

            public void Dispose()
            {
                if(disposed)return;disposed=true;
                Free(ref ReceiptPointer);Free(ref BuffersPointer);Free(ref Bindings);
                Free(ref EdgeJoined);Free(ref EdgeBroken);Free(ref EdgeDeleted);Free(ref EdgeCreated);
                Free(ref NodeDeleted);Free(ref NodeCreated);Free(ref IslandWords);
                Free(ref NotReadyChangeWords);Free(ref NotReadyWords);
                Free(ref KinematicChangeWords);Free(ref KinematicWords);
                Free(ref Roots);Free(ref Islands);Free(ref Edges);Free(ref Nodes);
            }

            private static void Free(ref IntPtr pointer)
            {
                if(pointer==IntPtr.Zero)return;Marshal.FreeHGlobal(pointer);pointer=IntPtr.Zero;
            }
        }

        private sealed class PendingIslandObservation
        {
            internal CheckpointSidecar Sidecar;
            internal IslandSnapshotBufferOwner Pre,Post;
            internal IntPtr Library;
            internal uint ExpectedManager,ExpectedContext,ExpectedNphase,ArmedOrdinal,ObserverSequence,ArmedThreadId;
        }

        private sealed class ActorPairReuseScanState
        {
            internal uint ActorPair,Actor0,Actor1,ScannedActor,OtherActor;
            internal uint InteractionCount,SipCandidates,MatchingSipCandidates;
            internal uint[] MatchingActorPairs;
        }

        private sealed class ColliderEndpointState
        {
            internal Collider Collider;
            internal int InstanceId;
            internal string Path,TypeName,Identity;
            internal uint PxShape;
        }

        private sealed class DirtyInteractionState
        {
            internal uint NPhaseCore,Entries,EntriesNext,Hash,EntriesCapacity,HashSize,OrderHash;
            internal NativeDirtyInteractionKey[] Keys;
        }

        private sealed class ContactManagerOwnerState
        {
            internal NativeContactManagerOwnerReceipt Receipt;
            internal NativeContactManagerOwnerRecord[] Records;
            internal string[] ShapeOwners0,ShapeOwners1;
            internal ColliderEndpointState[] Endpoints0,Endpoints1;
            // The scalar owner records deliberately expose every pointer that
            // must be rebound, while these bounded images retain the opaque
            // solver/contact history living between those fields.  A later
            // restore may copy an image only after replacing its captured
            // pointers with the current semantic incarnations.
            internal byte[][] ManagerBytes,SipBytes,ActorPairBytes,ManifoldBytes,CacheBytes;
        }

        private sealed class LiveContactPoolState
        {
            internal NativeContactPoolReceipt Receipt;
            internal uint[] Order;
        }

        // Exact synchronous physics-family image at one managed output
        // boundary.  It retains that output's exact core snapshot, but does
        // not own the following transition's passive observations.
        private sealed class PhysicsPhaseSnapshot
        {
            internal int Frame;
            internal uint Context,FreeArray,OrderHash;
            internal uint[] ContactPoolOrder;
            internal ContactManagerOwnerState ContactManagerOwners;
            internal SipPoolState ShapeInstancePairPool;
            internal ActorPairPoolState ActorPairPool;
            internal ActorPairReportPoolState ActorPairReportPool;
            internal NPhasePoolImageState[] NPhasePoolImages;
            internal NPhaseReportState NPhaseReports;
            internal InteractionGraphState InteractionGraph;
            internal TransformCacheState TransformCache;
            internal IslandSnapshotState IslandSnapshot;
            internal ManifoldPoolState LargeManifoldPool,SphereManifoldPool;
            internal TransformDispatchState TransformDispatch;
            internal DirtyInteractionState DirtyInteractions;
            internal object CoreSnapshot;
        }

        private sealed class CheckpointSidecar
        {
            internal int Frame;
            internal uint Context,FreeArray,OrderHash;
            internal uint[] ContactPoolOrder;
            internal ContactManagerOwnerState ContactManagerOwners;
            internal SipPoolState ShapeInstancePairPool;
            internal ActorPairPoolState ActorPairPool;
            internal ActorPairReportPoolState ActorPairReportPool;
            internal NPhasePoolImageState[] NPhasePoolImages;
            internal NPhaseReportState NPhaseReports;
            internal InteractionGraphState InteractionGraph;
            internal TransformCacheState TransformCache;
            internal FinishBroadPhaseState FinishBroadPhase;
            internal IslandSnapshotState IslandSnapshot;
            internal IslandTransitionState IslandTransition;
            internal ManifoldPoolState LargeManifoldPool,SphereManifoldPool;
            internal DirtyInteractionState DirtyInteractions;
            internal TransformDispatchState TransformDispatch;
            internal PhysicsPhaseSnapshot PostTransitionSnapshot;
            internal object CoreSnapshot;
        }

        private static RigidbodyActorRebuildModule active;
        // A finishBroadPhase caller can have followed the native JMP before
        // uninstall restores the entry bytes but before the hook increments
        // any observable in-flight counter.  Keep one loader reference for
        // every DLL image that has ever owned this hook so such a caller can
        // never resume into unmapped code.
        private static readonly HashSet<IntPtr> processPinnedNativeLibraries=
            new HashSet<IntPtr>();
        // If native cannot prove an armed observer quiescent, retaining its
        // caller-owned buffers is safer than freeing memory a hook may still
        // reference.  Successful copy/cancel/uninstall fences dispose normally.
        private static readonly List<PendingIslandObservation> processRetainedIslandBuffers=
            new List<PendingIslandObservation>();
        private readonly FieldInfo cachedPtr=typeof(UnityEngine.Object).GetField("m_CachedPtr",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly FieldInfo groundColliderField=typeof(GroundCast).GetField("m_groundCollider",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly List<object> receipts=new List<object>();
        private Harmony harmony;
        private IntPtr library;
        private NativeApiVersion apiVersion;
        private NativeRebuildBatch rebuildBatch;
        private NativeActorShapes actorShapes;
        private NativeContactPoolCaptureSnapshot captureContactPoolSnapshot;
        private NativeContactPoolRestoreSnapshot restoreContactPoolSnapshot;
        private NativeContactManagerActiveOwners captureContactManagerActiveOwners;
        private NativeManifoldPoolCaptureSnapshot captureManifoldPoolSnapshot;
        private NativeManifoldPoolRestoreSnapshot restoreManifoldPoolSnapshot;
        private NativeSipPoolCaptureSnapshot captureSipPoolSnapshot;
        private NativeActorPairPoolCaptureSnapshot captureActorPairPoolSnapshot;
        private NativeActorPairReportPoolCaptureSnapshot captureActorPairReportPoolSnapshot;
        private NativeNPhasePoolCaptureSnapshot captureNPhasePoolSnapshot;
        private NativeNPhaseReportStateCaptureSnapshot captureNPhaseReportStateSnapshot;
        private NativeInteractionGraphCaptureSnapshot captureInteractionGraphSnapshot;
        private NativeTransformCacheCaptureSnapshot captureTransformCacheSnapshot;
        private NativeFinishBroadPhaseObserverAction installFinishBroadPhaseObserver;
        private NativeFinishBroadPhaseObserverAction statusFinishBroadPhaseObserver;
        private NativeFinishBroadPhaseObserverArm armFinishBroadPhaseObserver;
        private NativeFinishBroadPhaseObserverCopy copyFinishBroadPhaseObserver;
        private NativeFinishBroadPhaseObserverAction cancelFinishBroadPhaseObserver;
        private NativeFinishBroadPhaseObserverAction uninstallFinishBroadPhaseObserver;
        private NativeIslandCaptureSnapshot captureIslandSnapshot;
        private NativeIslandRestoreSnapshot restoreIslandSnapshot;
        private NativeIslandObserverInstall installIslandObserver;
        private NativeIslandObserverAction statusIslandObserver,cancelIslandObserver,uninstallIslandObserver;
        private NativeIslandObserverArm armIslandObserver;
        private NativeIslandObserverCopy copyIslandObserver;
        private NativeIslandJournalCopy copyIslandJournal;
        private NativeContextObserverAction installContextObserver,statusContextObserver,uninstallContextObserver;
        private NativeApiVersion contactRecreateApiVersion;
        private NativeApiVersion contactRecreateAuditApiVersion;
        private NativeContactRecreateAudit auditContactRecreate;
        private NativeContactRecreateArm armContactRecreate;
        private NativeContactRecreateStatus statusContactRecreate;
        private NativeContactRecreateCancel cancelContactRecreate;
        private NativeDirtyInteractionAction installDirtyInteractionOrder,statusDirtyInteractionOrder;
        private NativeDirtyInteractionLastNPhase lastObservedDirtyNPhase;
        private NativeDirtyInteractionAction armDirtyInteractionCapture,cancelDirtyInteractionOrder,uninstallDirtyInteractionOrder;
        private NativeDirtyInteractionCaptureCopy copyDirtyInteractionCapture;
        private NativeDirtyInteractionCaptureSnapshot captureDirtyInteractionSnapshot;
        private NativeDirtyInteractionRestoreArm armDirtyInteractionRestore;
        private uint unityPlayerBase;
        private uint dirtyNPhaseObservationFloor;
        private uint dirtyInteractionRestoreMode=DirtyInteractionRestoreExact;
        private uint contactManagerContext,contextObservations;
        private uint islandObserverManager;
        private string nativePath,nativeSha256,failure;
        private bool automatic,automaticGroundCollider,observeContactManagerContext,contextObserverInstalled;
        private bool automaticContactPoolRestore,automaticTransformDispatchRestore,automaticRestorePending,warpInProgress,warpTargetRestoreEligible,disposed;
        private bool dirtyInteractionHookInstalled,finishBroadPhaseObserverInstalled,islandObserverInstalled;
        private bool finishBroadPhaseObserverWasInstalled,dirtyRestorePendingValidation;
        private bool contactRecreatePendingValidation;
        private uint contextObservationFloor;
        private int pendingContactPoolAction;
        private int pendingContactPoolFrame=-1,warpTargetFrame=-1;
        private int scheduledContactPoolCaptureFrame=-1,scheduledContactPoolLastObservedFrame=-1;
        private object pendingCoreSnapshot;
        private object coreRoundIdentity;
        private int sceneMetadataGeneration=-1;
        private long rebuilds,contactPoolCaptures,contactPoolRestores,manifoldPoolCaptures,manifoldPoolRestores;
        private long sceneOwnedResets,contextSnapshotInvalidations;
        private readonly List<object> contactPoolReceipts=new List<object>();
        private readonly List<object> manifoldPoolReceipts=new List<object>();
        private readonly Dictionary<int,CheckpointSidecar> checkpointSidecars=new Dictionary<int,CheckpointSidecar>();
        private CheckpointSidecar warpTargetSidecar;
        private CheckpointSidecar pendingContactRecreateSidecar;
        private CheckpointSidecar pendingDirtyCaptureSidecar;
        private PendingIslandObservation pendingIslandObservation;
        private CheckpointSidecar pendingIslandTransitionAuditSidecar;
        private IslandTransitionState lastIslandTransitionAudit;
        private FinishBroadPhaseState lastFinishBroadPhaseTransitionAudit;
        private PhysicsPhaseSnapshot lastTargetPostTransitionAudit;
        private PhysicsPhaseSnapshot lastRestoredPostTransitionAudit;
        private int lastIslandTransitionAuditCheckpointFrame=-1;
        private int lastIslandTransitionAuditTransitionFrame=-1;
        private int lastIslandTransitionAuditCapturedAtOutputFrame=-1;
        private DirtyInteractionState pendingDirtyRestoreState;
        private uint pendingDirtyCaptureOrdinal,pendingDirtyRestoreOrdinal;
        private long transformDispatchCaptures,transformDispatchRestores;
        private readonly List<object> transformDispatchReceipts=new List<object>();
        private object lastContextObserverReceipt,lastSceneOwnedReset;
        private readonly List<object> dirtyInteractionReceipts=new List<object>();
        private readonly List<object> finishBroadPhaseReceipts=new List<object>();
        private readonly List<object> contactRecreateReceipts=new List<object>();
        private long dirtyInteractionCaptures,dirtyInteractionRestores;
        private long transformCacheCaptures,finishBroadPhaseCaptures,islandSnapshotCaptures,islandTransitionCaptures;
        private long islandTransitionAuditArms,islandTransitionAuditCaptures;
        private long transitionPostSnapshotCaptures;
        private uint pendingFinishBroadPhaseOrdinal;
        private long scheduledContactPoolCaptureArms,scheduledContactPoolCaptureTriggers;

        public string Name { get { return "authoring-rigidbody-actor-rebuild-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("RigidbodyActorRebuildModule");
            if(args==null)args=new Dictionary<string,object>();
            // This operation intentionally bypasses Status(): several status
            // providers refresh diagnostic state.  A readiness audit must be
            // repeatable without touching module or native hook state.
            if(operation=="audit-restore-readiness")return AuditRestoreReadiness(args);
            object result=null;
            if(operation=="activate")Activate(args);
            else if(operation=="rebuild")result=RebuildOne(args);
            else if(operation=="capture-contact-pool-next")result=ArmContactPoolAction(args,1);
            else if(operation=="capture-contact-pool-at-frame")result=ArmContactPoolCaptureAtFrame(args);
            else if(operation=="restore-contact-pool-next")result=ArmContactPoolAction(args,2);
            else if(operation=="cancel-contact-pool-next")result=CancelContactPoolAction(args);
            else if(operation=="checkpoint-status")result=CheckpointStatus(args);
            else if(operation=="arm-first-replay-island-audit")result=ArmFirstReplayIslandAudit(args);
            else if(operation=="copy-first-replay-island-audit")result=CopyFirstReplayIslandAudit(args);
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, rebuild, capture-contact-pool-next, capture-contact-pool-at-frame, restore-contact-pool-next, cancel-contact-pool-next, checkpoint-status, audit-restore-readiness, arm-first-replay-island-audit, copy-first-replay-island-audit, status or deactivate.");
            else RequireNoArgs(args);
            return Status(operation,result);
        }

        private void Activate(Dictionary<string,object> args)
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Actor-rebuild activation requires the authoring pause fence.");
            if(library!=IntPtr.Zero)return;
            if(active!=null)throw new InvalidOperationException("Another actor-rebuild module is active.");
            string path=Path.GetFullPath(String(args,"nativePath"));
            string expected=String(args,"sha256").ToUpperInvariant();
            bool auto=args.ContainsKey("automaticChefs")&&Convert.ToBoolean(args["automaticChefs"]);
            bool autoGround=args.ContainsKey("automaticGroundCollider")&&Convert.ToBoolean(args["automaticGroundCollider"]);
            bool observeContext=args.ContainsKey("observeContactManagerContext")&&Convert.ToBoolean(args["observeContactManagerContext"]);
            bool autoPoolRestore=args.ContainsKey("automaticContactPoolRestore")&&Convert.ToBoolean(args["automaticContactPoolRestore"]);
            bool autoDispatchRestore=args.ContainsKey("automaticTransformDispatchRestore")&&Convert.ToBoolean(args["automaticTransformDispatchRestore"]);
            bool projectDirtyInteractions=args.ContainsKey("dirtyInteractionRestoreProjection")&&
                Convert.ToBoolean(args["dirtyInteractionRestoreProjection"]);
            uint context=args.ContainsKey("contactManagerContext")?Pointer(args,"contactManagerContext"):0;
            if(autoPoolRestore&&!observeContext&&context==0)
                throw new InvalidOperationException("Automatic contact-pool restore requires context observation or an explicit context.");
            if(autoDispatchRestore&&!autoPoolRestore)
                throw new InvalidOperationException("Transform-dispatch restore currently requires the paired contact-pool checkpoint action.");
            if(!File.Exists(path))throw new FileNotFoundException("Native actor-rebuild DLL missing.",path);
            string actual=Hash(path);
            if(actual!=expected)throw new InvalidOperationException("Native actor-rebuild DLL hash mismatch: "+actual);
            if(IntPtr.Size!=4||cachedPtr==null)throw new InvalidOperationException("Actor rebuild requires the x86 Unity object ABI.");
            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(string.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            unityPlayerBase=unchecked((uint)unity.BaseAddress.ToInt32());
            library=LoadLibrary(path);
            if(library==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            try
            {
                apiVersion=Export<NativeApiVersion>("oc2_rigidbody_rebuild_api_version");
                rebuildBatch=Export<NativeRebuildBatch>("oc2_rigidbody_rebuild_batch");
                actorShapes=Export<NativeActorShapes>("oc2_rigidbody_actor_shapes");
                captureContactPoolSnapshot=Export<NativeContactPoolCaptureSnapshot>("oc2_contact_manager_pool_capture_snapshot");
                restoreContactPoolSnapshot=Export<NativeContactPoolRestoreSnapshot>("oc2_contact_manager_pool_restore_snapshot");
                captureContactManagerActiveOwners=Export<NativeContactManagerActiveOwners>("oc2_contact_manager_active_owners");
                captureManifoldPoolSnapshot=Export<NativeManifoldPoolCaptureSnapshot>("oc2_manifold_pool_capture_snapshot");
                restoreManifoldPoolSnapshot=Export<NativeManifoldPoolRestoreSnapshot>("oc2_manifold_pool_restore_snapshot");
                captureSipPoolSnapshot=Export<NativeSipPoolCaptureSnapshot>("oc2_shape_instance_pair_pool_capture_snapshot");
                captureActorPairPoolSnapshot=Export<NativeActorPairPoolCaptureSnapshot>("oc2_actor_pair_pool_capture_snapshot");
                captureActorPairReportPoolSnapshot=Export<NativeActorPairReportPoolCaptureSnapshot>(
                    "oc2_actor_pair_report_pool_capture_snapshot");
                captureNPhasePoolSnapshot=Export<NativeNPhasePoolCaptureSnapshot>(
                    "oc2_nphase_pool_capture_snapshot_v1");
                captureNPhaseReportStateSnapshot=Export<NativeNPhaseReportStateCaptureSnapshot>(
                    "oc2_nphase_report_state_capture_snapshot");
                captureInteractionGraphSnapshot=Export<NativeInteractionGraphCaptureSnapshot>(
                    "oc2_interaction_graph_capture_snapshot");
                captureTransformCacheSnapshot=Export<NativeTransformCacheCaptureSnapshot>(
                    "oc2_transform_cache_capture_snapshot");
                installFinishBroadPhaseObserver=Export<NativeFinishBroadPhaseObserverAction>(
                    "oc2_finish_broad_phase_observer_install");
                statusFinishBroadPhaseObserver=Export<NativeFinishBroadPhaseObserverAction>(
                    "oc2_finish_broad_phase_observer_status");
                armFinishBroadPhaseObserver=Export<NativeFinishBroadPhaseObserverArm>(
                    "oc2_finish_broad_phase_observer_arm");
                copyFinishBroadPhaseObserver=Export<NativeFinishBroadPhaseObserverCopy>(
                    "oc2_finish_broad_phase_observer_copy");
                cancelFinishBroadPhaseObserver=Export<NativeFinishBroadPhaseObserverAction>(
                    "oc2_finish_broad_phase_observer_cancel");
                uninstallFinishBroadPhaseObserver=Export<NativeFinishBroadPhaseObserverAction>(
                    "oc2_finish_broad_phase_observer_uninstall");
                captureIslandSnapshot=Export<NativeIslandCaptureSnapshot>(
                    "oc2_island_capture_snapshot_v1");
                restoreIslandSnapshot=Export<NativeIslandRestoreSnapshot>(
                    "oc2_island_restore_snapshot_v1");
                installIslandObserver=Export<NativeIslandObserverInstall>(
                    "oc2_island_update_observer_install");
                statusIslandObserver=Export<NativeIslandObserverAction>(
                    "oc2_island_update_observer_status");
                armIslandObserver=Export<NativeIslandObserverArm>(
                    "oc2_island_update_observer_arm");
                copyIslandObserver=Export<NativeIslandObserverCopy>(
                    "oc2_island_update_observer_copy");
                cancelIslandObserver=Export<NativeIslandObserverAction>(
                    "oc2_island_update_observer_cancel");
                uninstallIslandObserver=Export<NativeIslandObserverAction>(
                    "oc2_island_update_observer_uninstall");
                copyIslandJournal=Export<NativeIslandJournalCopy>(
                    "oc2_island_edge_journal_copy");
                installContextObserver=Export<NativeContextObserverAction>("oc2_contact_manager_context_observer_install");
                statusContextObserver=Export<NativeContextObserverAction>("oc2_contact_manager_context_observer_status");
                uninstallContextObserver=Export<NativeContextObserverAction>("oc2_contact_manager_context_observer_uninstall");
                contactRecreateApiVersion=Export<NativeApiVersion>("oc2_contact_recreate_api_version");
                contactRecreateAuditApiVersion=Export<NativeApiVersion>("oc2_contact_recreate_audit_api_version");
                auditContactRecreate=Export<NativeContactRecreateAudit>("oc2_contact_recreate_audit");
                armContactRecreate=Export<NativeContactRecreateArm>("oc2_contact_recreate_arm");
                statusContactRecreate=Export<NativeContactRecreateStatus>("oc2_contact_recreate_status");
                cancelContactRecreate=Export<NativeContactRecreateCancel>("oc2_contact_recreate_cancel");
                installDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_install");
                statusDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_status");
                lastObservedDirtyNPhase=Export<NativeDirtyInteractionLastNPhase>("oc2_dirty_interaction_last_nphase");
                armDirtyInteractionCapture=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_capture_arm");
                copyDirtyInteractionCapture=Export<NativeDirtyInteractionCaptureCopy>("oc2_dirty_interaction_order_capture_copy");
                captureDirtyInteractionSnapshot=Export<NativeDirtyInteractionCaptureSnapshot>(
                    "oc2_dirty_interaction_order_capture_snapshot");
                armDirtyInteractionRestore=Export<NativeDirtyInteractionRestoreArm>("oc2_dirty_interaction_order_restore_arm");
                cancelDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_cancel");
                uninstallDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_uninstall");
                if(apiVersion()!=NativeAbiVersion)throw new InvalidOperationException("Native actor-rebuild API version mismatch.");
                if(contactRecreateApiVersion()!=2)throw new InvalidOperationException("Native contact-recreate API version mismatch.");
                if(contactRecreateAuditApiVersion()!=1)throw new InvalidOperationException("Native contact-recreate audit API version mismatch.");
                nativePath=path;nativeSha256=actual;automatic=auto;automaticGroundCollider=autoGround;
                observeContactManagerContext=observeContext;automaticContactPoolRestore=autoPoolRestore;
                dirtyInteractionRestoreMode=projectDirtyInteractions?
                    DirtyInteractionRestoreProjection:DirtyInteractionRestoreExact;
                automaticTransformDispatchRestore=autoDispatchRestore;sceneMetadataGeneration=NativeSceneMetadata.Refreshes;
                contactManagerContext=context;coreRoundIdentity=CoreRoundIdentity();active=this;
                if(observeContactManagerContext)
                {
                    NativeContextObserverReceipt contextInstall=
                        RunContextObserver(installContextObserver,"install");
                    // Native hooks remain resident across managed revisions and
                    // their last value is diagnostic history, not evidence for
                    // this activation.  Require a post-install observation.
                    contextObservationFloor=contextInstall.Observations;
                    contextObserverInstalled=true;
                    RefreshObservedContactManagerContext();
                }
                if(automaticContactPoolRestore)
                {
                    NativeDirtyInteractionReceipt dirty=InstallDirtyInteractionHook();
                    if(dirty.Result!=1||dirty.Installed!=1)
                        throw new InvalidOperationException("Native dirty-interaction hook did not install.");
                    NativeFinishBroadPhaseObserverReceipt broad=InstallFinishBroadPhaseObserver();
                    if(broad.Result!=1||broad.Installed!=1||broad.State!=1)
                        throw new InvalidOperationException("Native finishBroadPhase observer did not install.");
                    // A freshly installed context observer has legitimately seen no
                    // updateContactManager call while the authoring fence is paused.
                    // Install immediately only when an explicit or prior observation
                    // supplied a context; otherwise the first capture admission will
                    // install the island observer after a real context is observed.
                    if(contactManagerContext!=0)EnsureIslandObserverForCurrentContext();
                }
                if(auto||autoPoolRestore||autoDispatchRestore)InstallAutomaticHook();
            }
            catch{Deactivate();throw;}
        }

        private void InstallAutomaticHook()
        {
            var setPaused=AccessTools.DeclaredMethod(typeof(TimeManager),"SetPaused",
                new[]{typeof(TimeManager.PauseLayer),typeof(bool),typeof(object)});
            if(setPaused==null||setPaused.ReturnType!=typeof(void))
                throw new InvalidOperationException("Installed native pause contract differs.");
            harmony=new Harmony("supercharged.authoring.rigidbody-actor-rebuild."+GetType().Assembly.GetName().Name);
            harmony.Patch(setPaused,
                postfix:new HarmonyMethod(GetType().GetMethod("AfterSetPaused",BindingFlags.Public|BindingFlags.Static)));
            if(automaticContactPoolRestore)
            {
                var prepare=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"Prepare",new[]{typeof(Hpmv.WarpSpec)});
                var complete=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan),"Complete",Type.EmptyTypes);
                var failureMethod=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"RecordRestoreFailure",
                    new[]{typeof(int),typeof(Exception),typeof(bool)});
                var capture=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"CaptureFrame",new[]{typeof(int)});
                if(prepare==null||complete==null||failureMethod==null||capture==null||capture.ReturnType!=typeof(void))
                    throw new InvalidOperationException("Installed native checkpoint lifecycle contract differs.");
                harmony.Patch(prepare,prefix:new HarmonyMethod(GetType().GetMethod("BeforePrepare",BindingFlags.Public|BindingFlags.Static)));
                harmony.Patch(complete,postfix:new HarmonyMethod(GetType().GetMethod("AfterRestoreComplete",BindingFlags.Public|BindingFlags.Static)));
                harmony.Patch(failureMethod,postfix:new HarmonyMethod(GetType().GetMethod("AfterRestoreFailure",BindingFlags.Public|BindingFlags.Static)));
                // Run after the ordinary core and module capture postfixes.  The
                // original CaptureFrame has already published its exact snapshot,
                // and Priority.Last prevents this targeted native observation from
                // preceding default-priority delivery/history observers.
                var capturePostfix=new HarmonyMethod(GetType().GetMethod("AfterCaptureFrame",BindingFlags.Public|BindingFlags.Static));
                capturePostfix.priority=Priority.Last;
                harmony.Patch(capture,postfix:capturePostfix);
            }
        }

        public static void AfterSetPaused(TimeManager.PauseLayer __0,bool __1)
        {
            var module=active;
            if(module==null||__0!=TimeManager.PauseLayer.Main)return;
            module.ObserveSceneGeneration();
            module.ObserveCoreRoundIdentity();
            if((!module.automatic&&!module.automaticContactPoolRestore)||!NativeSessionBridge.KitchenReady)return;
            if(__1)
            {
                if(!module.warpInProgress)
                {
                    try
                    {
                        module.FinalizePendingDirtyInteractionCapture();
                        module.ValidatePendingContactRecreate("status-paused",-1,false);
                        module.ValidatePendingDirtyInteractionRestore();
                        module.failure=null;
                    }
                    catch(Exception error){module.failure=error.Message;throw;}
                }
                if(module.warpInProgress)module.warpInProgress=false;
                return;
            }
            if(TimeManager.IsPaused(TimeManager.PauseLayer.Main))return;
            try
            {
                if(module.automatic&&module.warpInProgress)
                {
                    var bodies=UnityEngine.Object.FindObjectsOfType<ServerChefSynchroniser>()
                        .Select(value=>value.GetComponent<Rigidbody>()).Where(IsLocalChef)
                        .OrderBy(value=>PathOf(value.transform),StringComparer.Ordinal).ToArray();
                    if(bodies.Length!=4||bodies.Select(value=>value.GetInstanceID()).Distinct().Count()!=4)
                        throw new InvalidOperationException("Automatic actor rebuild requires exactly four distinct local chefs.");
                    for(int cycle=1;cycle<=AutomaticCanonicalCycles;cycle++)
                        module.RebuildBodies(bodies,"main-unpause-cycle-"+cycle);
                    if(module.automaticGroundCollider)module.RebuildSharedGroundCollider(bodies);
                }
                int contactPoolAction=module.pendingContactPoolAction;
                bool automaticContactPoolAction=false;
                module.pendingContactPoolAction=0;
                if(contactPoolAction==0&&module.automaticContactPoolRestore&&module.automaticRestorePending&&!module.warpInProgress)
                {
                    contactPoolAction=2;automaticContactPoolAction=true;module.automaticRestorePending=false;
                }
                if(contactPoolAction!=0)module.RunContactPoolAction(contactPoolAction,automaticContactPoolAction);
                module.failure=null;
            }
            catch(Exception error){module.failure=error.Message;throw;}
        }

        public static void BeforePrepare(Hpmv.WarpSpec __0)
        {
            var module=active;
            if(module==null||!module.automaticContactPoolRestore)return;
            module.ObserveCoreRoundIdentity();
            module.FinalizePendingDirtyInteractionCapture();
            module.warpInProgress=true;
            module.warpTargetFrame=__0==null?-1:__0.Frame;
            module.warpTargetRestoreEligible=module.checkpointSidecars.TryGetValue(
                module.warpTargetFrame,out module.warpTargetSidecar);
            object core=CoreCheckpointSnapshot(module.warpTargetFrame);
            if(!module.warpTargetRestoreEligible||core==null||
                !ReferenceEquals(module.warpTargetSidecar.CoreSnapshot,core))
                throw new InvalidOperationException("No exact physics/Transform/transition sidecar exists for output frame "+module.warpTargetFrame+".");
            ValidateDirtyInteractionState(module.warpTargetSidecar.DirtyInteractions);
        }

        public static void AfterRestoreComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module=active;
            if(module==null||!module.automaticContactPoolRestore)return;
            if(__instance==null||module.warpTargetSidecar==null||
                !ReferenceEquals(module.warpTargetSidecar.CoreSnapshot,RestorePlanSnapshot(__instance)))
                throw new InvalidOperationException("Completed native restore plan differs from the selected physics-pool sidecar.");
            module.automaticRestorePending=module.warpTargetRestoreEligible;
            module.PruneCheckpointSidecarsAfter(module.warpTargetFrame);
        }

        public static void AfterRestoreFailure()
        {
            var module=active;
            if(module==null||!module.automaticContactPoolRestore)return;
            module.automaticRestorePending=false;module.warpTargetRestoreEligible=false;
            module.warpTargetSidecar=null;
            module.CancelContactRecreateWork();
            module.CancelCheckpointObservationWork();
        }

        public static void AfterCaptureFrame(int __0)
        {
            var module=active;
            if(module==null||!module.automaticContactPoolRestore)return;
            bool pendingLifecycle=module.contactRecreatePendingValidation||
                module.scheduledContactPoolCaptureFrame>=0||
                module.pendingIslandTransitionAuditSidecar!=null||
                module.pendingDirtyCaptureSidecar!=null;
            try
            {
                module.ObserveSceneGeneration();
                module.ObserveCoreRoundIdentity();
                // The context hook first learns PxsContext during physics.  This
                // Priority.Last output boundary is the earliest managed point at
                // which it is safe to install or rebind the island hook.  Keeping
                // it current from the first observed output also retains the
                // longest possible removeEdge history for later D-queue capture.
                module.RefreshObservedContactManagerContext();
                if(module.contactManagerContext!=0)
                    module.EnsureIslandObserverForCurrentContext();
                if(!pendingLifecycle)return;
                // The entry sidecar is captured at N and remains unpublished
                // while its passive transition observers run.  Capture every
                // synchronously readable mutable family at output N+1, before
                // any later validation or pause can change allocator membership.
                if(module.pendingDirtyCaptureSidecar!=null)
                {
                    module.CapturePendingPostTransitionSnapshotAtOutput(__0);
                    module.FinalizePendingDirtyInteractionCapture();
                }
                // Persist the first replay's native transition before contact
                // validation or the later pause fence can cancel its one-shot
                // observer state.  This is read-only and remains bound to the
                // exact checkpoint-to-next-output transaction.
                if(module.pendingIslandTransitionAuditSidecar!=null)
                    module.CaptureFirstReplayTransitionAuditAtOutput(__0);
                // Either observer may have recognized a new scene/round and
                // transactionally cancelled scene-owned work.  Contact owners
                // are an advancing-output property: the recreation hook runs
                // during the first replayed physics step, and those contacts
                // may all be returned again before the later authoring pause.
                // Validate here, after the core has published that output, at
                // the same lifecycle boundary used to capture the sidecar.
                if(module.contactRecreatePendingValidation)
                {
                    if(module.ValidatePendingContactRecreate("status-output",__0,true))
                        module.ValidatePendingDirtyInteractionRestore();
                }
                if(module.scheduledContactPoolCaptureFrame>=0)
                    module.CaptureContactPoolAtScheduledFrame(__0);
                module.failure=null;
            }
            catch(Exception error)
            {
                module.failure=error.ToString();
                throw;
            }
        }

        private object RebuildOne(Dictionary<string,object> args)
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("One-shot actor rebuild requires the authoring pause fence.");
            if(args.Count!=1||!args.ContainsKey("bodyInstanceId"))throw new ArgumentException("rebuild requires only bodyInstanceId.");
            int id=Convert.ToInt32(args["bodyInstanceId"]);
            var body=UnityEngine.Object.FindObjectsOfType<Rigidbody>().FirstOrDefault(value=>value.GetInstanceID()==id);
            if(body==null)throw new InvalidOperationException("Requested Rigidbody is not live.");
            return RebuildBodies(new[]{body},"one-shot")[0];
        }

        private object ArmContactPoolAction(Dictionary<string,object> args,int action)
        {
            RequireNoArgs(args);
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Contact-pool action requires the authoring pause fence.");
            if(!ReferenceEquals(active,this)||contactManagerContext==0)
            {
                RefreshObservedContactManagerContext();
                if(!ReferenceEquals(active,this)||contactManagerContext==0)
                    throw new InvalidOperationException("Contact-pool action requires an active module and an observed or explicit context.");
            }
            ObserveCoreRoundIdentity();
            if(pendingContactPoolAction!=0||scheduledContactPoolCaptureFrame>=0)
                throw new InvalidOperationException("A contact-pool action is already pending or scheduled.");
            if(action==2&&checkpointSidecars.Count==0)
                throw new InvalidOperationException("No physics-pool snapshot has been captured.");
            if(action==1)
            {
                pendingContactPoolFrame=CurrentCheckpointFrame();
                if(pendingContactPoolFrame<0)throw new InvalidOperationException("No native checkpoint frame exists for contact-pool capture.");
                pendingCoreSnapshot=CoreCheckpointSnapshot(pendingContactPoolFrame);
                if(pendingCoreSnapshot==null)
                    throw new InvalidOperationException("The native checkpoint frame has no retained core snapshot.");
            }
            pendingContactPoolAction=action;
            CheckpointSidecar latest=LatestCheckpointSidecar();
            return new Dictionary<string,object>{{"pending",action==1?"capture":"restore"},
                {"context","0x"+contactManagerContext.ToString("X8")},{"frame",action==1?(object)pendingContactPoolFrame:latest.Frame}};
        }

        private object ArmContactPoolCaptureAtFrame(Dictionary<string,object> args)
        {
            if(args.Count!=1||!args.ContainsKey("frame")||args["frame"]==null||
                (args["frame"].GetType()!=typeof(int)&&args["frame"].GetType()!=typeof(long)))
                throw new ArgumentException("capture-contact-pool-at-frame requires exactly one whole-number frame.");
            long requestedFrame=Convert.ToInt64(args["frame"]);
            if(requestedFrame<int.MinValue||requestedFrame>int.MaxValue)
                throw new ArgumentOutOfRangeException("frame","Scheduled contact-pool capture frame is outside Int32 range.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Scheduled contact-pool capture requires the authoring pause fence.");
            if(!ReferenceEquals(active,this)||!automaticContactPoolRestore||!dirtyInteractionHookInstalled||
                !finishBroadPhaseObserverInstalled)
                throw new InvalidOperationException("Scheduled contact-pool capture requires the active automatic contact-pool restore module.");
            ObserveSceneGeneration();
            ObserveCoreRoundIdentity();
            RefreshObservedContactManagerContext();
            if(!ReferenceEquals(active,this)||contactManagerContext==0)
                throw new InvalidOperationException("Scheduled contact-pool capture requires an active observed or explicit context.");
            EnsureIslandObserverForCurrentContext();
            if(pendingContactPoolAction!=0||scheduledContactPoolCaptureFrame>=0||
                pendingContactPoolFrame>=0||pendingCoreSnapshot!=null||pendingDirtyCaptureSidecar!=null||
                pendingDirtyCaptureOrdinal!=0||pendingFinishBroadPhaseOrdinal!=0||
                pendingIslandObservation!=null||
                dirtyRestorePendingValidation||pendingDirtyRestoreOrdinal!=0||
                pendingDirtyRestoreState!=null||contactRecreatePendingValidation||
                pendingContactRecreateSidecar!=null||automaticRestorePending||warpInProgress)
                throw new InvalidOperationException("Another contact-pool capture, restore, or dirty-interaction action is pending or scheduled.");
            int target=(int)requestedFrame;
            int current=CurrentCheckpointFrame();
            if(target<=current)
                throw new InvalidOperationException("Scheduled contact-pool capture target must be after current checkpoint frame "+current+".");
            scheduledContactPoolCaptureFrame=target;
            scheduledContactPoolLastObservedFrame=current;
            scheduledContactPoolCaptureArms++;
            return new Dictionary<string,object>{{"pending","scheduled-capture"},
                {"context","0x"+contactManagerContext.ToString("X8")},{"currentFrame",current},{"frame",target}};
        }

        private void CaptureContactPoolAtScheduledFrame(int observedFrame)
        {
            int target=scheduledContactPoolCaptureFrame;
            if(target<0)return;
            scheduledContactPoolLastObservedFrame=observedFrame;
            if(observedFrame<target)return;
            if(observedFrame>target)
                throw new InvalidOperationException("Scheduled contact-pool capture skipped exact output frame "+target+
                    "; next observed frame was "+observedFrame+".");
            if(pendingContactPoolAction!=0||pendingContactPoolFrame>=0||pendingCoreSnapshot!=null||
                pendingDirtyCaptureSidecar!=null||pendingDirtyCaptureOrdinal!=0||
                pendingFinishBroadPhaseOrdinal!=0||
                pendingIslandObservation!=null||
                dirtyRestorePendingValidation||pendingDirtyRestoreOrdinal!=0||pendingDirtyRestoreState!=null||
                contactRecreatePendingValidation||pendingContactRecreateSidecar!=null||
                automaticRestorePending||warpInProgress)
                throw new InvalidOperationException("Scheduled contact-pool capture reached its target while another native checkpoint action was pending.");

            RefreshObservedContactManagerContext();
            if(scheduledContactPoolCaptureFrame!=target)return;
            if(contactManagerContext==0)
                throw new InvalidOperationException("Scheduled contact-pool capture reached its target without an observed context.");
            if(CurrentCheckpointFrame()!=target)
                throw new InvalidOperationException("Native checkpoint output-frame watermark differs from scheduled contact-pool target "+target+".");
            object core=CoreCheckpointSnapshot(target);
            if(core==null)
                throw new InvalidOperationException("Scheduled contact-pool capture target has no retained exact core snapshot.");

            pendingContactPoolFrame=target;
            pendingCoreSnapshot=core;
            try
            {
                RunContactPoolAction(1,false,true);
                if(pendingContactPoolFrame!=-1||pendingCoreSnapshot!=null||pendingDirtyCaptureSidecar==null||
                    pendingDirtyCaptureOrdinal==0||pendingFinishBroadPhaseOrdinal==0||
                    pendingIslandObservation==null||
                    pendingDirtyCaptureSidecar.Frame!=target||
                    !ReferenceEquals(pendingDirtyCaptureSidecar.CoreSnapshot,core)||
                    pendingDirtyCaptureSidecar.ShapeInstancePairPool==null||
                    pendingDirtyCaptureSidecar.TransformCache==null||
                    pendingDirtyCaptureSidecar.IslandSnapshot==null||
                    pendingDirtyCaptureSidecar.TransformDispatch==null)
                    throw new InvalidOperationException("Scheduled contact-pool capture did not stage one complete exact-frame sidecar transaction.");
                // Clear only after all synchronous read-only captures succeeded
                // and the matching dirty-interaction sample was armed.
                scheduledContactPoolCaptureFrame=-1;
                scheduledContactPoolCaptureTriggers++;
            }
            catch
            {
                pendingContactPoolFrame=-1;
                pendingCoreSnapshot=null;
                // Admission guarantees no unrelated dirty work existed, so an
                // arm that failed after touching native hook state is safe to
                // cancel without disturbing another transaction.
                CancelCheckpointObservationWork();
                throw;
            }
        }

        private object CancelContactPoolAction(Dictionary<string,object> args)
        {
            RequireNoArgs(args);int prior=pendingContactPoolAction;int scheduled=scheduledContactPoolCaptureFrame;
            pendingContactPoolAction=0;pendingContactPoolFrame=-1;pendingCoreSnapshot=null;
            scheduledContactPoolCaptureFrame=-1;scheduledContactPoolLastObservedFrame=-1;
            return new Dictionary<string,object>{{"cancelled",scheduled>=0?"scheduled-capture":prior==0?"none":prior==1?"capture":"restore"},
                {"frame",scheduled>=0?(object)scheduled:null}};
        }

        private object ArmFirstReplayIslandAudit(Dictionary<string,object> args)
        {
            RequireNoArgs(args);
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("First-replay island audit requires the authoring pause fence.");
            if(!ReferenceEquals(active,this)||!automaticContactPoolRestore||!automaticRestorePending||
                warpTargetSidecar==null||warpTargetSidecar.Frame!=CurrentCheckpointFrame())
                throw new InvalidOperationException(
                    "First-replay island audit must be armed at the exact restored checkpoint before its automatic physics restore.");
            if(pendingIslandObservation!=null||pendingIslandTransitionAuditSidecar!=null||
                pendingDirtyCaptureSidecar!=null||pendingDirtyCaptureOrdinal!=0||
                pendingFinishBroadPhaseOrdinal!=0||contactRecreatePendingValidation||
                pendingContactRecreateSidecar!=null)
                throw new InvalidOperationException("Another checkpoint observation or contact recreation is pending.");
            ObserveSceneGeneration();
            ObserveCoreRoundIdentity();
            RefreshObservedContactManagerContext();
            EnsureIslandObserverForCurrentContext();
            CheckpointSidecar selected=warpTargetSidecar;
            ValidateIslandSnapshotState(selected.IslandSnapshot,IslandPhaseSettled);
            lastIslandTransitionAudit=null;
            lastFinishBroadPhaseTransitionAudit=null;
            lastTargetPostTransitionAudit=null;
            lastRestoredPostTransitionAudit=null;
            lastIslandTransitionAuditCheckpointFrame=-1;
            lastIslandTransitionAuditTransitionFrame=-1;
            lastIslandTransitionAuditCapturedAtOutputFrame=-1;
            pendingIslandTransitionAuditSidecar=selected;
            try{ArmFirstReplayTransitionObservers(selected);}
            catch{pendingIslandTransitionAuditSidecar=null;throw;}
            islandTransitionAuditArms++;
            return new Dictionary<string,object>{{"armed",true},{"frame",selected.Frame},
                {"islandObservationOrdinal",pendingIslandObservation.ArmedOrdinal},
                {"finishBroadPhaseObservationOrdinal",pendingFinishBroadPhaseOrdinal},
                {"observerSequence",pendingIslandObservation.ObserverSequence}};
        }

        private object CopyFirstReplayIslandAudit(Dictionary<string,object> args)
        {
            RequireNoArgs(args);
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("First-replay island audit copy requires the authoring pause fence.");
            if(lastIslandTransitionAudit==null||lastFinishBroadPhaseTransitionAudit==null||
                lastTargetPostTransitionAudit==null||lastRestoredPostTransitionAudit==null||
                lastIslandTransitionAuditCheckpointFrame<0||
                lastIslandTransitionAuditTransitionFrame<0||
                lastIslandTransitionAuditCapturedAtOutputFrame<0)
                throw new InvalidOperationException("No completed first-replay transition audit is available.");
            return DescribeFirstReplayTransitionAudit(CurrentCheckpointFrame());
        }

        private void ArmFirstReplayTransitionObservers(CheckpointSidecar sidecar)
        {
            ValidateTransformCacheState(sidecar==null?null:sidecar.TransformCache);
            ValidateIslandSnapshotState(sidecar==null?null:sidecar.IslandSnapshot,IslandPhaseSettled);
            if(!finishBroadPhaseObserverInstalled||!islandObserverInstalled||sidecar.InteractionGraph==null)
                throw new InvalidOperationException("First-replay transition audit requires both installed native observers.");
            NativeInteractionGraphReceipt graph=sidecar.InteractionGraph.Receipt;
            int size=Marshal.SizeOf(typeof(NativeFinishBroadPhaseObserverReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);
            NativeFinishBroadPhaseObserverReceipt receipt=new NativeFinishBroadPhaseObserverReceipt();
            int ok=0;
            try
            {
                ArmIslandObservationCapture(sidecar,false);
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                ok=armFinishBroadPhaseObserver(new UIntPtr(unityPlayerBase),graph.OwnerScene,
                    graph.LlContext,graph.NPhaseCore,0,buffer);
                receipt=(NativeFinishBroadPhaseObserverReceipt)Marshal.PtrToStructure(
                    buffer,typeof(NativeFinishBroadPhaseObserverReceipt));
            }
            catch
            {
                try{if(ok!=0||pendingIslandObservation!=null)CancelFirstReplayTransitionObservationWork();}
                finally{pendingFinishBroadPhaseOrdinal=0;}
                throw;
            }
            finally{Marshal.FreeHGlobal(buffer);}
            try
            {
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native first-replay finishBroadPhase observer arm failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+", state="+receipt.State+".");
                ValidateFinishBroadPhaseReceiptContract(receipt,size);
                RecordFinishBroadPhaseReceipt("audit-arm",receipt);
                if(receipt.Installed!=1||receipt.State!=2||receipt.ExpectedPass!=0||
                    receipt.ExpectedScene!=graph.OwnerScene||receipt.ExpectedContext!=graph.LlContext||
                    receipt.ExpectedNPhaseCore!=graph.NPhaseCore||receipt.ArmedOrdinal==0)
                    throw new InvalidOperationException(
                        "Native first-replay finishBroadPhase observer arm differs from the sealed checkpoint identities.");
                pendingFinishBroadPhaseOrdinal=receipt.ArmedOrdinal;
            }
            catch
            {
                try{if(ok!=0||pendingIslandObservation!=null)CancelFirstReplayTransitionObservationWork();}
                finally{pendingFinishBroadPhaseOrdinal=0;}
                throw;
            }
        }

        private void CaptureFirstReplayTransitionAuditAtOutput(int observedFrame)
        {
            CheckpointSidecar selected=pendingIslandTransitionAuditSidecar;
            if(selected==null)return;
            int transitionFrame=checked(selected.Frame+1);
            if(observedFrame<transitionFrame)return;
            if(observedFrame!=transitionFrame)
                throw new InvalidOperationException("First-replay transition audit skipped exact output frame "+
                    transitionFrame+"; next observed frame was "+observedFrame+".");
            FinishBroadPhaseState broadPhase=null;
            IslandTransitionState island=null;
            PhysicsPhaseSnapshot restoredPost=null;
            try
            {
                broadPhase=CopyFinishBroadPhaseCapture(selected,pendingFinishBroadPhaseOrdinal);
                island=CopyIslandTransitionAudit(selected);
                if(selected.PostTransitionSnapshot==null)
                    throw new InvalidOperationException(
                        "The selected checkpoint lacks its exact post-transition snapshot.");
                restoredPost=CapturePhysicsPhaseSnapshot(selected,observedFrame);
                lastFinishBroadPhaseTransitionAudit=broadPhase;
                lastIslandTransitionAudit=island;
                lastTargetPostTransitionAudit=selected.PostTransitionSnapshot;
                lastRestoredPostTransitionAudit=restoredPost;
                lastIslandTransitionAuditCheckpointFrame=selected.Frame;
                lastIslandTransitionAuditTransitionFrame=transitionFrame;
                lastIslandTransitionAuditCapturedAtOutputFrame=observedFrame;
                islandTransitionAuditCaptures++;
                transitionPostSnapshotCaptures++;
            }
            finally
            {
                CancelFirstReplayTransitionObservationWork();
                pendingIslandTransitionAuditSidecar=null;
            }
        }

        private object DescribeFirstReplayTransitionAudit(int readAtFrame)
        {
            return new Dictionary<string,object>{{"captured",true},
                {"checkpointFrame",lastIslandTransitionAuditCheckpointFrame},
                {"transitionFrame",lastIslandTransitionAuditTransitionFrame},
                {"capturedAtOutputFrame",lastIslandTransitionAuditCapturedAtOutputFrame},
                {"readAtFrame",readAtFrame},
                {"broadPhase",DescribeFinishBroadPhaseState(lastFinishBroadPhaseTransitionAudit)},
                {"transition",DescribeIslandTransitionState(lastIslandTransitionAudit)},
                {"targetPostSnapshot",DescribePhysicsPhaseSnapshot(lastTargetPostTransitionAudit)},
                {"restoredPostSnapshot",DescribePhysicsPhaseSnapshot(lastRestoredPostTransitionAudit)},
                {"postSnapshotComparison",DescribePhysicsPhaseComparison(
                    lastTargetPostTransitionAudit,lastRestoredPostTransitionAudit)}};
        }

        private void RunContactPoolAction(int action,bool automaticAction,bool requireTransformCapture=false)
        {
            if(action==1)FinalizePendingDirtyInteractionCapture();
            uint captureNPhase=0,captureNPhaseObservation=0;
            if(action==1)
            {
                captureNPhase=ReadObservedNPhaseCore(out captureNPhaseObservation);
                if(captureNPhaseObservation<=dirtyNPhaseObservationFloor)
                    throw new InvalidOperationException("No current-scene NPhaseCore observation is available for an exact pool checkpoint.");
            }
            RefreshObservedContactManagerContext();
            if(action==1)
            {
                if(!automaticContactPoolRestore||!dirtyInteractionHookInstalled||
                    !finishBroadPhaseObserverInstalled)
                    throw new InvalidOperationException(
                        "Contact-pool capture requires the complete native observation stack.");
                EnsureIslandObserverForCurrentContext();
            }
            CheckpointSidecar selected=null;
            if(action==2)
            {
                selected=automaticAction?warpTargetSidecar:LatestCheckpointSidecar();
                if(selected==null||selected.Context==0||selected.Context!=contactManagerContext)
                    throw new InvalidOperationException("Contact-pool restore snapshot does not belong to the current observed context.");
                ValidateManifoldPoolState(selected.LargeManifoldPool,LargeManifoldPoolKind,"large");
                ValidateManifoldPoolState(selected.SphereManifoldPool,SphereManifoldPoolKind,"sphere");
            }
            if(captureContactPoolSnapshot==null||restoreContactPoolSnapshot==null||
                captureContactManagerActiveOwners==null||captureManifoldPoolSnapshot==null||
                restoreManifoldPoolSnapshot==null||captureSipPoolSnapshot==null||
                captureActorPairPoolSnapshot==null||captureActorPairReportPoolSnapshot==null||
                captureNPhasePoolSnapshot==null)
                throw new InvalidOperationException("Native caller-owned physics-pool helper is not active.");
            int actionFrame=action==1?pendingContactPoolFrame:selected.Frame;
            object actionCoreSnapshot=action==1?pendingCoreSnapshot:selected.CoreSnapshot;
            if(action==2&&automaticAction&&RequiresContactRecreate(selected))
            {
                RunContactRecreateRestore(selected,actionFrame);
                contactPoolRestores++;
                warpTargetSidecar=null;warpTargetRestoreEligible=false;
                return;
            }
            int size=Marshal.SizeOf(typeof(NativeContactPoolReceipt));
            int capacity=action==1?MaximumContactManagers:selected.ContactPoolOrder.Length;
            if(capacity<1||capacity>MaximumContactManagers)
                throw new InvalidOperationException("Contact-pool snapshot count is outside the supported range.");
            IntPtr buffer=Marshal.AllocHGlobal(size);
            IntPtr snapshotBuffer=Marshal.AllocHGlobal(capacity*IntPtr.Size);
            NativeContactPoolReceipt receipt;
            uint[] capturedOrder=null;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                if(action==2)
                    for(int i=0;i<capacity;i++)Marshal.WriteInt32(snapshotBuffer,i*IntPtr.Size,
                        unchecked((int)selected.ContactPoolOrder[i]));
                int ok=action==1
                    ?captureContactPoolSnapshot(new UIntPtr(contactManagerContext),snapshotBuffer,
                        (uint)capacity,buffer)
                    :restoreContactPoolSnapshot(new UIntPtr(contactManagerContext),
                        new UIntPtr(selected.FreeArray),snapshotBuffer,(uint)capacity,buffer);
                receipt=(NativeContactPoolReceipt)Marshal.PtrToStructure(buffer,typeof(NativeContactPoolReceipt));
                if(ok==0||receipt.Result!=1)
                {
                    string liveTop=string.Join(",",(receipt.Top??new UIntPtr[0]).Select(Hex).ToArray());
                    string expectedTop=selected==null?"":string.Join(",",selected.ContactPoolOrder
                        .Take(16).Select(value=>"0x"+value.ToString("X8")).ToArray());
                    RecordContactPoolReceipt(action,actionFrame,receipt);
                    string membershipDifference="";
                    if(action==2&&selected!=null)
                    {
                        try{membershipDifference=DescribeContactPoolMembershipDifference(selected);}
                        catch(Exception diagnosticError)
                        {
                            membershipDifference=", membershipDiagnosticFailure="+
                                diagnosticError.GetType().Name+": "+diagnosticError.Message;
                        }
                    }
                    throw new InvalidOperationException("Native contact-pool action failed: result="+receipt.Result+
                        ", Win32/error="+receipt.LastError+", liveCount="+receipt.FreeCount+
                        ", expectedCount="+capacity+", liveArray="+Hex(receipt.FreeArray)+
                        ", expectedArray="+(selected==null?"":("0x"+selected.FreeArray.ToString("X8")))+
                        ", liveHash=0x"+receipt.OrderHashBefore.ToString("X8")+
                        ", expectedHash="+(selected==null?"":("0x"+selected.OrderHash.ToString("X8")))+
                        ", liveTop=["+liveTop+"], expectedTop=["+expectedTop+"]"+
                        membershipDifference+".");
                }
                if(action==1)
                {
                    if(receipt.FreeCount<1||receipt.FreeCount>MaximumContactManagers)
                        throw new InvalidOperationException("Native contact-pool capture returned an invalid count.");
                    capturedOrder=new uint[receipt.FreeCount];
                    for(int i=0;i<capturedOrder.Length;i++)capturedOrder[i]=
                        unchecked((uint)Marshal.ReadInt32(snapshotBuffer,i*IntPtr.Size));
                }
            }
            finally{Marshal.FreeHGlobal(snapshotBuffer);Marshal.FreeHGlobal(buffer);}
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||receipt.Context.ToUInt32()!=contactManagerContext)
                throw new InvalidOperationException("Native contact-pool receipt contract differs.");
            RecordContactPoolReceipt(action,actionFrame,receipt);
            if(action==1)
            {
                uint orderHash=ContactPoolOrderHash(capturedOrder);
                if(orderHash!=receipt.OrderHashBefore||receipt.OrderHashAfter!=receipt.OrderHashBefore)
                    throw new InvalidOperationException("Managed contact-pool snapshot hash differs from the native capture receipt.");
                ContactManagerOwnerState owners=CaptureContactManagerOwners(actionFrame);
                SipPoolState sip=CaptureSipPoolState(captureNPhase,actionFrame);
                ActorPairPoolState actorPairs=CaptureActorPairPoolState(captureNPhase,actionFrame);
                ActorPairReportPoolState actorPairReports=CaptureActorPairReportPoolState(
                    captureNPhase,actionFrame);
                NPhasePoolImageState[] nphasePoolImages=CaptureNPhasePoolImages(
                    captureNPhase,actionFrame);
                NPhaseReportState nphaseReports=CaptureNPhaseReportState(captureNPhase,actionFrame);
                InteractionGraphState interactionGraph=CaptureInteractionGraphState(
                    captureNPhase,actionFrame);
                TransformCacheState transformCache=CaptureTransformCacheState(
                    captureNPhase,actionFrame);
                IslandSnapshotState islandSnapshot=CaptureIslandSnapshotState(
                    captureNPhase,IslandPhaseSettled,actionFrame);
                ManifoldPoolState large=RunManifoldPoolAction(1,LargeManifoldPoolKind,null,actionFrame);
                ManifoldPoolState sphere=RunManifoldPoolAction(1,SphereManifoldPoolKind,null,actionFrame);
                TransformDispatchState dispatch=(automaticTransformDispatchRestore||requireTransformCapture)
                    ?RunTransformDispatchAction(1,null):null;
                var captured=new CheckpointSidecar {Frame=actionFrame,
                    Context=contactManagerContext,FreeArray=receipt.FreeArray.ToUInt32(),
                    OrderHash=orderHash,ContactPoolOrder=capturedOrder,
                    ContactManagerOwners=owners,
                    ShapeInstancePairPool=sip,
                    ActorPairPool=actorPairs,
                    ActorPairReportPool=actorPairReports,
                    NPhasePoolImages=nphasePoolImages,
                    NPhaseReports=nphaseReports,
                    InteractionGraph=interactionGraph,
                    TransformCache=transformCache,
                    IslandSnapshot=islandSnapshot,
                    LargeManifoldPool=large,SphereManifoldPool=sphere,
                    TransformDispatch=dispatch,CoreSnapshot=actionCoreSnapshot};
                uint afterObservation;
                uint afterNPhase=ReadObservedNPhaseCore(out afterObservation);
                if(afterNPhase!=captureNPhase||afterObservation!=captureNPhaseObservation)
                    throw new InvalidOperationException("A physics update crossed the synchronous checkpoint pool-capture interval.");
                ValidateCheckpointPoolCoherence(captured);
                // Do not publish a partially captured sidecar.  The native
                // The dirty-list and finishBroadPhase samples both complete on
                // the next physics update.  Publish only after the two native
                // observers prove that they describe this same sealed sidecar.
                ArmCheckpointObservationCapture(captured);
                pendingContactPoolFrame=-1;pendingCoreSnapshot=null;
                contactPoolCaptures++;transformCacheCaptures++;islandSnapshotCaptures++;
            }
            else
            {
                if(receipt.FreeArray.ToUInt32()!=selected.FreeArray||receipt.FreeCount!=(uint)capacity||
                    receipt.OrderHashAfter!=selected.OrderHash)
                    throw new InvalidOperationException("Native contact-pool restore receipt differs from the selected checkpoint sidecar.");
                // Restore the pools in their native allocation dependency order:
                // contact managers own pair caches which point at manifolds.
                RunManifoldPoolAction(2,LargeManifoldPoolKind,selected.LargeManifoldPool,actionFrame);
                RunManifoldPoolAction(2,SphereManifoldPoolKind,selected.SphereManifoldPool,actionFrame);
                if(automaticTransformDispatchRestore)RunTransformDispatchAction(2,selected.TransformDispatch);
                ArmDirtyInteractionRestore(selected.DirtyInteractions);
                contactPoolRestores++;
                if(automaticAction){warpTargetSidecar=null;warpTargetRestoreEligible=false;}
            }
        }

        private string DescribeContactPoolMembershipDifference(CheckpointSidecar expected)
        {
            if(expected==null||expected.ContactPoolOrder==null)
                throw new InvalidOperationException("The expected contact-pool sidecar is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeContactPoolReceipt));
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr orderBuffer=Marshal.AllocHGlobal(MaximumContactManagers*IntPtr.Size);
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureContactPoolSnapshot(new UIntPtr(contactManagerContext),orderBuffer,
                    MaximumContactManagers,receiptBuffer);
                var receipt=(NativeContactPoolReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeContactPoolReceipt));
                if(ok==0||receipt.Result!=1||receipt.ApiVersion!=NativeAbiVersion||
                    receipt.StructSize!=(uint)size||receipt.Context.ToUInt32()!=contactManagerContext||
                    receipt.FreeArray.ToUInt32()!=expected.FreeArray||
                    receipt.FreeCount<1||receipt.FreeCount>MaximumContactManagers)
                    throw new InvalidOperationException("Read-only live contact-pool capture failed its receipt contract: result="+
                        receipt.Result+", error="+receipt.LastError+", count="+receipt.FreeCount+".");

                var live=new uint[receipt.FreeCount];
                for(int i=0;i<live.Length;i++)live[i]=unchecked((uint)Marshal.ReadInt32(
                    orderBuffer,i*IntPtr.Size));
                if(live.Any(pointer=>pointer==0)||live.Distinct().Count()!=live.Length||
                    ContactPoolOrderHash(live)!=receipt.OrderHashBefore||
                    receipt.OrderHashAfter!=receipt.OrderHashBefore)
                    throw new InvalidOperationException("Read-only live contact-pool capture returned an invalid ordered set.");

                var liveSet=new HashSet<uint>(live);
                var expectedSet=new HashSet<uint>(expected.ContactPoolOrder);
                uint[] liveOnly=live.Where(pointer=>!expectedSet.Contains(pointer)).ToArray();
                uint[] expectedOnly=expected.ContactPoolOrder.Where(pointer=>!liveSet.Contains(pointer)).ToArray();
                string liveOnlyText=string.Join(",",liveOnly.Take(16)
                    .Select(pointer=>"0x"+pointer.ToString("X8")).ToArray());
                string expectedOnlyText=string.Join(",",expectedOnly.Take(16)
                    .Select(pointer=>"0x"+pointer.ToString("X8")).ToArray());
                return ", liveOnlyCount="+liveOnly.Length+", checkpointOnlyCount="+expectedOnly.Length+
                    ", liveOnly=["+liveOnlyText+"], checkpointOnly=["+expectedOnlyText+"]";
            }
            finally
            {
                Marshal.FreeHGlobal(orderBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
        }

        private LiveContactPoolState CaptureLiveContactPoolState()
        {
            int size=Marshal.SizeOf(typeof(NativeContactPoolReceipt));
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr orderBuffer=Marshal.AllocHGlobal(MaximumContactManagers*IntPtr.Size);
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureContactPoolSnapshot(new UIntPtr(contactManagerContext),orderBuffer,
                    MaximumContactManagers,receiptBuffer);
                var receipt=(NativeContactPoolReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeContactPoolReceipt));
                if(ok==0||receipt.Result!=1||receipt.ApiVersion!=NativeAbiVersion||
                    receipt.StructSize!=(uint)size||receipt.Context.ToUInt32()!=contactManagerContext||
                    receipt.FreeCount<1||receipt.FreeCount>MaximumContactManagers)
                    throw new InvalidOperationException("Read-only live contact-pool capture failed its receipt contract: result="+
                        receipt.Result+", error="+receipt.LastError+", count="+receipt.FreeCount+".");
                var order=new uint[receipt.FreeCount];
                for(int i=0;i<order.Length;i++)order[i]=unchecked((uint)Marshal.ReadInt32(
                    orderBuffer,i*IntPtr.Size));
                if(order.Any(pointer=>pointer==0)||order.Distinct().Count()!=order.Length||
                    ContactPoolOrderHash(order)!=receipt.OrderHashBefore||
                    receipt.OrderHashAfter!=receipt.OrderHashBefore)
                    throw new InvalidOperationException("Read-only live contact-pool capture returned an invalid ordered set.");
                return new LiveContactPoolState {Receipt=receipt,Order=order};
            }
            finally
            {
                Marshal.FreeHGlobal(orderBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
        }

        private void CapturePendingPostTransitionSnapshotAtOutput(int observedFrame)
        {
            CheckpointSidecar entry=pendingDirtyCaptureSidecar;
            if(entry==null||entry.PostTransitionSnapshot!=null)return;
            int expected=checked(entry.Frame+1);
            if(observedFrame<expected)return;
            if(observedFrame!=expected)
                throw new InvalidOperationException("Checkpoint post-transition snapshot skipped exact output frame "+
                    expected+"; next observed frame was "+observedFrame+".");
            entry.PostTransitionSnapshot=CapturePhysicsPhaseSnapshot(entry,observedFrame);
            transitionPostSnapshotCaptures++;
        }

        private PhysicsPhaseSnapshot CapturePhysicsPhaseSnapshot(
            CheckpointSidecar entry,int frame)
        {
            if(entry==null||entry.ShapeInstancePairPool==null||
                entry.ShapeInstancePairPool.NPhaseCore==0)
                throw new InvalidOperationException(
                    "Physics phase capture requires one exact checkpoint entry image.");
            uint observationBefore;
            uint nphase=ReadObservedNPhaseCore(out observationBefore);
            if(nphase!=entry.ShapeInstancePairPool.NPhaseCore)
                throw new InvalidOperationException(
                    "Physics phase capture observed a different NPhaseCore than its checkpoint entry.");
            LiveContactPoolState contact=CaptureLiveContactPoolState();
            if(contact.Receipt.Context.ToUInt32()!=entry.Context||
                contact.Receipt.FreeArray.ToUInt32()!=entry.FreeArray)
                throw new InvalidOperationException(
                    "Physics phase capture observed a different contact allocator than its checkpoint entry.");
            if(CurrentCheckpointFrame()!=frame)
                throw new InvalidOperationException(
                    "Physics phase capture does not own the requested core output boundary.");
            object core=CoreCheckpointSnapshot(frame);
            if(core==null)
                throw new InvalidOperationException(
                    "Physics phase capture requires the exact retained core output snapshot.");
            var value=new PhysicsPhaseSnapshot {
                Frame=frame,
                Context=contact.Receipt.Context.ToUInt32(),
                FreeArray=contact.Receipt.FreeArray.ToUInt32(),
                OrderHash=contact.Receipt.OrderHashBefore,
                ContactPoolOrder=contact.Order,
                ContactManagerOwners=CaptureContactManagerOwners(frame),
                ShapeInstancePairPool=CaptureSipPoolState(nphase,frame),
                ActorPairPool=CaptureActorPairPoolState(nphase,frame),
                ActorPairReportPool=CaptureActorPairReportPoolState(nphase,frame),
                NPhasePoolImages=CaptureNPhasePoolImages(nphase,frame),
                NPhaseReports=CaptureNPhaseReportState(nphase,frame),
                InteractionGraph=CaptureInteractionGraphState(nphase,frame),
                TransformCache=CaptureTransformCacheState(nphase,frame),
                IslandSnapshot=CaptureIslandSnapshotState(nphase,IslandPhaseSettled,frame),
                LargeManifoldPool=CaptureManifoldPoolStateReadOnly(
                    LargeManifoldPoolKind,frame),
                SphereManifoldPool=CaptureManifoldPoolStateReadOnly(
                    SphereManifoldPoolKind,frame),
                TransformDispatch=entry.TransformDispatch==null?null:
                    RunTransformDispatchAction(1,null),
                DirtyInteractions=CaptureDirtyInteractionStateReadOnly(nphase),
                CoreSnapshot=core
            };
            uint observationAfter;
            uint nphaseAfter=ReadObservedNPhaseCore(out observationAfter);
            if(nphaseAfter!=nphase||observationAfter!=observationBefore)
                throw new InvalidOperationException(
                    "A physics update crossed the synchronous post-transition capture interval.");
            ValidatePhysicsPhaseSnapshot(value);
            return value;
        }

        private static void ValidatePhysicsPhaseSnapshot(PhysicsPhaseSnapshot value)
        {
            if(value==null||value.Frame<0||value.Context==0||value.FreeArray==0||
                value.CoreSnapshot==null||
                value.ContactPoolOrder==null||value.ContactPoolOrder.Length<1||
                value.ContactPoolOrder.Length>MaximumContactManagers||
                value.TransformDispatch==null||value.TransformDispatch.Dispatch==0||
                value.TransformDispatch.Entries==null||
                value.TransformDispatch.Count!=(uint)value.TransformDispatch.Entries.Length||
                value.TransformDispatch.Count>value.TransformDispatch.Capacity||
                value.OrderHash!=ContactPoolOrderHash(value.ContactPoolOrder))
                throw new InvalidOperationException("Physics phase snapshot is incomplete.");
            if(value.TransformDispatch.Entries.Any(item=>item.Hierarchy==0)||
                value.TransformDispatch.Entries.Select(item=>item.Hierarchy).Distinct().Count()!=
                    value.TransformDispatch.Entries.Length)
                throw new InvalidOperationException(
                    "Physics phase Transform-dispatch snapshot contains an invalid queue.");
            ValidateContactManagerOwnerState(value.ContactManagerOwners);
            ValidateSipPoolState(value.ShapeInstancePairPool);
            ValidateActorPairPoolState(value.ActorPairPool);
            ValidateActorPairReportPoolState(value.ActorPairReportPool);
            ValidateNPhasePoolImages(value.NPhasePoolImages);
            ValidateNPhaseReportState(value.NPhaseReports);
            ValidateInteractionGraphState(value.InteractionGraph);
            bool completePoolsCoherent=NPhasePoolImagesCoherentWithLegacy(
                value.NPhasePoolImages,value.ShapeInstancePairPool,value.ActorPairPool,
                value.ActorPairReportPool,value.InteractionGraph);
            if(!completePoolsCoherent)
                throw new InvalidOperationException(
                    "The complete NPhase-pool images disagree with the legacy pool or interaction-graph captures.");
            ValidateTransformCacheState(value.TransformCache);
            ValidateIslandSnapshotState(value.IslandSnapshot,IslandPhaseSettled);
            ValidateManifoldPoolState(value.LargeManifoldPool,LargeManifoldPoolKind,"large");
            ValidateManifoldPoolState(value.SphereManifoldPool,SphereManifoldPoolKind,"sphere");
            ValidateDirtyInteractionState(value.DirtyInteractions);
            var coherence=new CheckpointSidecar {
                Frame=value.Frame,Context=value.Context,FreeArray=value.FreeArray,
                OrderHash=value.OrderHash,ContactPoolOrder=value.ContactPoolOrder,
                ContactManagerOwners=value.ContactManagerOwners,
                ShapeInstancePairPool=value.ShapeInstancePairPool,
                ActorPairPool=value.ActorPairPool,
                ActorPairReportPool=value.ActorPairReportPool,
                NPhasePoolImages=value.NPhasePoolImages,
                NPhaseReports=value.NPhaseReports,
                InteractionGraph=value.InteractionGraph,
                TransformCache=value.TransformCache,
                IslandSnapshot=value.IslandSnapshot,
                LargeManifoldPool=value.LargeManifoldPool,
                SphereManifoldPool=value.SphereManifoldPool,
                TransformDispatch=value.TransformDispatch,
                DirtyInteractions=value.DirtyInteractions,
                CoreSnapshot=value.CoreSnapshot
            };
            ValidateCheckpointPoolCoherence(coherence);
            if(value.ShapeInstancePairPool.NPhaseCore!=value.DirtyInteractions.NPhaseCore)
                throw new InvalidOperationException(
                    "Physics phase snapshot families belong to different NPhaseCore instances.");
        }

        private bool RequiresContactRecreate(CheckpointSidecar selected)
        {
            if(contactRecreatePendingValidation||pendingContactRecreateSidecar!=null)
                throw new InvalidOperationException("A contact-manager recreation plan is already pending validation.");
            LiveContactPoolState live=CaptureLiveContactPoolState();
            if(live.Receipt.FreeArray.ToUInt32()!=selected.FreeArray)
                throw new InvalidOperationException("Live contact-manager free-array identity differs from the rewind checkpoint.");
            if(live.Order.Length==selected.ContactPoolOrder.Length)return false;
            ValidateContactManagerOwnerState(selected.ContactManagerOwners);
            uint[] managers=selected.ContactManagerOwners.Records.Select(
                value=>value.Manager.ToUInt32()).ToArray();
            if(live.Order.Length!=selected.ContactPoolOrder.Length+managers.Length)
                throw new InvalidOperationException("Live contact-manager membership is neither exact nor the checkpoint-owner loss shape.");
            var expected=new HashSet<uint>(selected.ContactPoolOrder);
            if(expected.Count!=selected.ContactPoolOrder.Length||managers.Any(value=>!expected.Add(value))||
                live.Order.Any(value=>!expected.Contains(value))||live.Order.Distinct().Count()!=live.Order.Length)
                throw new InvalidOperationException("Live contact-manager membership is not the exact checkpoint-free union checkpoint-owned-manager set.");
            return true;
        }

        private void RunContactRecreateRestore(CheckpointSidecar selected,int frame)
        {
            if(armContactRecreate==null||statusContactRecreate==null||cancelContactRecreate==null)
                throw new InvalidOperationException("Native contact-manager recreation exports are unavailable.");
            ValidateContactManagerOwnerState(selected.ContactManagerOwners);
            ValidateSipPoolState(selected.ShapeInstancePairPool);
            ValidateManifoldPoolState(selected.LargeManifoldPool,LargeManifoldPoolKind,"large");
            NativeContactRecreatePlanRow[] rows=BuildContactRecreateRows(selected);
            int receiptSize=Marshal.SizeOf(typeof(NativeContactRecreateReceipt));
            int rowSize=Marshal.SizeOf(typeof(NativeContactRecreatePlanRow));
            if(receiptSize!=268||rowSize!=36)
                throw new InvalidOperationException("Managed contact-recreate ABI size differs.");
            // Capture the two independently restored structures before native
            // contact/large-manifold priming.  A later arm or validation
            // failure can then return the entire pre-attempt paused state,
            // rather than only rolling back the two native allocators.
            ManifoldPoolState sphereBefore=RunManifoldPoolAction(
                1,SphereManifoldPoolKind,null,frame);
            TransformDispatchState dispatchBefore=automaticTransformDispatchRestore
                ?RunTransformDispatchAction(1,null):null;
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            IntPtr sipBuffer=Marshal.AllocHGlobal(Math.Max(1,
                selected.ShapeInstancePairPool.Order.Length)*IntPtr.Size);
            IntPtr contactBuffer=Marshal.AllocHGlobal(selected.ContactPoolOrder.Length*IntPtr.Size);
            IntPtr largeBuffer=Marshal.AllocHGlobal(Math.Max(1,selected.LargeManifoldPool.Order.Length)*IntPtr.Size);
            IntPtr rowBuffer=Marshal.AllocHGlobal(rows.Length*rowSize);
            NativeContactRecreateReceipt receipt;
            int ok;
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                for(int i=0;i<selected.ShapeInstancePairPool.Order.Length;i++)Marshal.WriteInt32(
                    sipBuffer,i*IntPtr.Size,unchecked((int)selected.ShapeInstancePairPool.Order[i]));
                for(int i=0;i<selected.ContactPoolOrder.Length;i++)Marshal.WriteInt32(
                    contactBuffer,i*IntPtr.Size,unchecked((int)selected.ContactPoolOrder[i]));
                for(int i=0;i<selected.LargeManifoldPool.Order.Length;i++)Marshal.WriteInt32(
                    largeBuffer,i*IntPtr.Size,unchecked((int)selected.LargeManifoldPool.Order[i]));
                for(int i=0;i<rows.Length;i++)Marshal.StructureToPtr(rows[i],
                    new IntPtr(rowBuffer.ToInt64()+i*rowSize),false);
                ok=armContactRecreate(new UIntPtr(unityPlayerBase),new UIntPtr(contactManagerContext),
                    new UIntPtr(selected.ShapeInstancePairPool.NPhaseCore),
                    new UIntPtr(selected.ShapeInstancePairPool.Pool),sipBuffer,
                    (uint)selected.ShapeInstancePairPool.Order.Length,
                    selected.ShapeInstancePairPool.Used,selected.ShapeInstancePairPool.Unreleased,
                    new UIntPtr(selected.FreeArray),contactBuffer,(uint)selected.ContactPoolOrder.Length,
                    new UIntPtr(selected.LargeManifoldPool.Pool),largeBuffer,
                    (uint)selected.LargeManifoldPool.Order.Length,selected.LargeManifoldPool.Used,
                    selected.LargeManifoldPool.Unreleased,rowBuffer,(uint)rows.Length,receiptBuffer);
                receipt=(NativeContactRecreateReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeContactRecreateReceipt));
            }
            finally
            {
                Marshal.FreeHGlobal(rowBuffer);Marshal.FreeHGlobal(largeBuffer);
                Marshal.FreeHGlobal(contactBuffer);Marshal.FreeHGlobal(sipBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            RecordContactRecreateReceipt("arm",frame,receipt);
            bool nativeArmed=ok!=0&&receipt.Result==1&&receipt.State==1&&receipt.Armed==1;
            if(nativeArmed)
            {
                // Establish managed ownership before checking the rest of the
                // receipt so every later exception can cancel native priming.
                pendingContactRecreateSidecar=selected;
                contactRecreatePendingValidation=true;
            }
            try
            {
                if(!nativeArmed||receipt.ApiVersion!=2||
                    receipt.StructSize!=(uint)receiptSize||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||receipt.Context.ToUInt32()!=contactManagerContext||
                    receipt.NPhaseCore.ToUInt32()!=selected.ShapeInstancePairPool.NPhaseCore||
                    receipt.SipPool.ToUInt32()!=selected.ShapeInstancePairPool.Pool||
                    receipt.FreeArray.ToUInt32()!=selected.FreeArray||
                    receipt.LargePool.ToUInt32()!=selected.LargeManifoldPool.Pool||
                    receipt.RowCount!=(uint)rows.Length||receipt.MatchedCount!=0||
                    receipt.SipMatchedCount!=0)
                    throw new InvalidOperationException("Native contact-manager recreation arm failed: result="+
                        receipt.Result+", error="+receipt.LastError+", state="+receipt.State+
                        ", invalidRow="+receipt.InvalidRow+", detail="+receipt.Detail+".");
                // The admitted delta contains only large manifolds.  The sphere
                // pool remains an ordinary exact checkpoint restore.
                RunManifoldPoolAction(2,SphereManifoldPoolKind,selected.SphereManifoldPool,frame);
                if(automaticTransformDispatchRestore)RunTransformDispatchAction(2,selected.TransformDispatch);
                ArmDirtyInteractionRestore(selected.DirtyInteractions);
            }
            catch
            {
                if(nativeArmed)
                {
                    // Dirty restoration is only an armed one-shot at this
                    // point, so cancel it before restoring its input queues.
                    try{CancelCheckpointObservationWork();}catch{}
                    if(automaticTransformDispatchRestore)
                        try{RunTransformDispatchAction(2,dispatchBefore);}catch{}
                    try{RunManifoldPoolAction(2,SphereManifoldPoolKind,sphereBefore,frame);}catch{}
                    try{CancelContactRecreateWork();}catch{}
                }
                else
                {
                    // A rejected arm may leave a diagnostic Poisoned state,
                    // but it has not acquired managed pending ownership and
                    // has not changed either target structure.
                    try{CallContactRecreateCancel("rejected-arm",frame);}catch{}
                }
                throw;
            }
        }

        private NativeContactRecreatePlanRow[] BuildContactRecreateRows(CheckpointSidecar selected)
        {
            if(selected==null)throw new ArgumentNullException("selected");
            ValidateContactManagerOwnerState(selected.ContactManagerOwners);
            NativeContactManagerOwnerRecord[] owners=selected.ContactManagerOwners.Records;
            if(owners.Length<1||owners.Length>32)
                throw new InvalidOperationException("Contact-manager recreation row count is outside the supported range.");
            var rows=new NativeContactRecreatePlanRow[owners.Length];
            for(int i=0;i<owners.Length;i++)
            {
                NativeContactManagerOwnerRecord owner=owners[i];
                if(owner.Membership!=3u||owner.ContactCount!=0||owner.CacheSize!=0||
                    owner.ManifoldBytes!=0xF0u||(owner.ManagerFlags&1u)!=0||
                    owner.Manifold==UIntPtr.Zero)
                    throw new InvalidOperationException("Checkpoint contact owner "+owner.Slot+
                        " is outside the narrow zero-touch, cacheless large-manifold recreation contract.");
                uint shape0=ResolveCurrentPxShape(selected.ContactManagerOwners.Endpoints0[i]);
                uint shape1=ResolveCurrentPxShape(selected.ContactManagerOwners.Endpoints1[i]);
                uint core0=checked(shape0+0x50u),core1=checked(shape1+0x50u);
                rows[i]=new NativeContactRecreatePlanRow {
                    PxsShapeCoreLow=new UIntPtr(Math.Min(core0,core1)),
                    PxsShapeCoreHigh=new UIntPtr(Math.Max(core0,core1)),
                    TargetManager=owner.Manager,TargetManifold=owner.Manifold,TargetSip=owner.Sip,
                    TargetSlot=owner.Slot,ManifoldBytes=owner.ManifoldBytes,
                    TargetManagerFlags=owner.ManagerFlags,Flags=0
                };
            }
            return rows;
        }

        private object AuditRestoreReadiness(Dictionary<string,object> args)
        {
            if(!ReferenceEquals(active,this)||library==IntPtr.Zero||auditContactRecreate==null)
                throw new InvalidOperationException("The actor-rebuild readiness provider is not active.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Restore-readiness audit requires the authoring pause fence.");
            if(!args.ContainsKey("frame"))throw new ArgumentException("audit-restore-readiness requires frame.");
            foreach(string key in args.Keys)
                if(key!="frame"&&key!="phase"&&key!="sourceFrame")
                    throw new ArgumentException("Unsupported audit-restore-readiness argument "+key+".");
            int frame=Convert.ToInt32(args["frame"]);
            int sourceFrame=args.ContainsKey("sourceFrame")?Convert.ToInt32(args["sourceFrame"]):CurrentCheckpointFrame();
            string phase=args.ContainsKey("phase")?Convert.ToString(args["phase"]):"target-paused";
            if(phase!="source-paused"&&phase!="target-paused")
                throw new ArgumentException("Readiness phase must be source-paused or target-paused.");

            string before=AuditModuleFingerprint();
            var checks=new List<object>();
            var blockers=new List<object>();
            var deferred=new List<object>();
            CheckpointSidecar selected=null;
            object core=CoreCheckpointSnapshot(frame);
            bool found=checkpointSidecars.TryGetValue(frame,out selected);
            AddReadinessCheck(checks,blockers,deferred,"rigidbody.target-binding",phase,
                found&&core!=null&&ReferenceEquals(selected.CoreSnapshot,core)?"pass":"fail","blocker",
                found?"TARGET_CORE_IDENTITY":"TARGET_SIDECAR_MISSING",
                found?"The retained physics sidecar is bound to the exact core checkpoint object.":
                    "No retained physics sidecar is available for the requested frame.",
                new Dictionary<string,object>{{"frame",frame},{"captured",found},
                    {"coreSnapshotPresent",core!=null},{"coreSnapshotMatches",found&&core!=null&&
                        ReferenceEquals(selected.CoreSnapshot,core)}});

            NativeContactRecreatePlanRow[] rows=null;
            if(found)
            {
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-contact-sidecar",phase,
                    delegate { ValidateContactManagerOwnerState(selected.ContactManagerOwners); },
                    "TARGET_CONTACT_SIDECAR_VALID","The contact-manager owner sidecar is structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-sip-sidecar",phase,
                    delegate { ValidateSipPoolState(selected.ShapeInstancePairPool); },
                    "TARGET_SIP_SIDECAR_VALID","The ShapeInstancePair pool sidecar is structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-actor-pair-sidecar",phase,
                    delegate { ValidateActorPairPoolState(selected.ActorPairPool); },
                    "TARGET_ACTOR_PAIR_SIDECAR_VALID","The ActorPair pool sidecar is structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-actor-pair-report-sidecar",phase,
                    delegate { ValidateActorPairReportPoolState(selected.ActorPairReportPool); },
                    "TARGET_ACTOR_PAIR_REPORT_SIDECAR_VALID",
                    "The ActorPair contact-report pool sidecar is structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-nphase-report-sidecar",phase,
                    delegate { ValidateNPhaseReportState(selected.NPhaseReports); },
                    "TARGET_NPHASE_REPORT_SIDECAR_VALID",
                    "The NPhase contact-report lists and full backing buffer are structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-interaction-graph-sidecar",phase,
                    delegate { ValidateInteractionGraphState(selected.InteractionGraph); },
                    "TARGET_INTERACTION_GRAPH_SIDECAR_VALID",
                    "The active-body, interaction, actor-slot, and pointer-pool graph is structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-transform-cache-sidecar",phase,
                    delegate { ValidateTransformCacheState(selected.TransformCache);
                        ValidateCheckpointPoolCoherence(selected); },
                    "TARGET_TRANSFORM_CACHE_SIDECAR_VALID",
                    "The transform-cache IDs, poses, free order, graph endpoints, and owner refcounts are structurally coherent.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-broadphase-transition-sidecar",phase,
                    delegate { ValidateFinishBroadPhaseState(selected.FinishBroadPhase,selected); },
                    "TARGET_BROADPHASE_TRANSITION_SIDECAR_VALID",
                    "The exact subsequent pass-zero finishBroadPhase transition is linked to the target Scene transaction.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-island-sidecar",phase,
                    delegate { ValidateIslandSnapshotState(selected.IslandSnapshot,IslandPhaseSettled);
                        ValidateIslandSnapshotCoherence(selected,selected.IslandSnapshot); },
                    "TARGET_ISLAND_SIDECAR_VALID",
                    "The settled island allocators, topology, bitmaps, queues, and semantic contact edges are coherent.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-island-transition-sidecar",phase,
                    delegate { ValidateIslandTransitionState(selected); },
                    "TARGET_ISLAND_TRANSITION_SIDECAR_VALID",
                    "The exact first island-update pre/post snapshots and add/remove journal are retained.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-large-sidecar",phase,
                    delegate { ValidateManifoldPoolState(selected.LargeManifoldPool,
                        LargeManifoldPoolKind,"large"); },
                    "TARGET_LARGE_SIDECAR_VALID","The large-manifold pool sidecar is structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-sphere-sidecar",phase,
                    delegate { ValidateManifoldPoolState(selected.SphereManifoldPool,
                        SphereManifoldPoolKind,"sphere"); },
                    "TARGET_SPHERE_SIDECAR_VALID","The sphere-manifold pool sidecar is structurally valid.");
                AddValidatorCheck(checks,blockers,deferred,"rigidbody.target-dirty-sidecar",phase,
                    delegate { ValidateDirtyInteractionState(selected.DirtyInteractions); },
                    "TARGET_DIRTY_SIDECAR_VALID","The dirty-interaction sidecar is structurally valid.");

                try
                {
                    NativeContactManagerOwnerRecord[] owners=selected.ContactManagerOwners.Records;
                    var contactFree=new HashSet<uint>(selected.ContactPoolOrder);
                    var sipFree=new HashSet<uint>(selected.ShapeInstancePairPool.Order);
                    var actorPairFree=new HashSet<uint>(selected.ActorPairPool.FreeOrder);
                    var actorPairAllocated=new HashSet<uint>(selected.ActorPairPool.AllocatedOrder);
                    var actorPairReportFree=new HashSet<uint>(selected.ActorPairReportPool.FreeOrder);
                    var actorPairReportAllocated=new HashSet<uint>(selected.ActorPairReportPool.AllocatedOrder);
                    var largeFree=new HashSet<uint>(selected.LargeManifoldPool.Order);
                    uint[] managerOverlap=owners.Select(value=>value.Manager.ToUInt32())
                        .Where(contactFree.Contains).ToArray();
                    uint[] sipOverlap=owners.Select(value=>value.Sip.ToUInt32())
                        .Where(sipFree.Contains).ToArray();
                    uint[] actorPairs=owners.Select(value=>value.ActorPair.ToUInt32()).Distinct().ToArray();
                    uint[] actorPairOverlap=actorPairs.Where(actorPairFree.Contains).ToArray();
                    uint[] actorPairMissing=actorPairs.Where(value=>!actorPairAllocated.Contains(value)).ToArray();
                    uint[] actorPairReports=owners.Select(value=>value.ActorPairReportData.ToUInt32())
                        .Where(value=>value!=0).Distinct().ToArray();
                    uint[] actorPairReportOverlap=actorPairReports.Where(actorPairReportFree.Contains).ToArray();
                    uint[] actorPairReportMissing=actorPairReports.Where(value=>
                        !actorPairReportAllocated.Contains(value)).ToArray();
                    uint[] manifoldOverlap=owners.Select(value=>value.Manifold.ToUInt32())
                        .Where(largeFree.Contains).ToArray();
                    bool coherent=managerOverlap.Length==0&&sipOverlap.Length==0&&
                        actorPairOverlap.Length==0&&actorPairMissing.Length==0&&
                        actorPairReportOverlap.Length==0&&actorPairReportMissing.Length==0&&
                        manifoldOverlap.Length==0&&
                        selected.ContactManagerOwners.Receipt.UsedCount==(uint)owners.Length&&
                        selected.ShapeInstancePairPool.Used>=(uint)owners.Select(value=>value.Sip).Distinct().Count()&&
                        selected.ActorPairPool.Used==(uint)actorPairs.Length&&
                        selected.ActorPairReportPool.Used==(uint)actorPairReports.Length&&
                        selected.LargeManifoldPool.Used>=(uint)owners.Select(value=>value.Manifold).Distinct().Count();
                    AddReadinessCheck(checks,blockers,deferred,
                        "rigidbody.target-cross-pool-coherence",phase,coherent?"pass":"fail","blocker",
                        coherent?"TARGET_POOL_OWNERSHIP_COHERENT":"TARGET_POOL_PHASE_MIX",
                        coherent?"Every saved owner is allocated in the same saved pool partition.":
                            "The target sidecar combines owner and allocator observations from incompatible physics phases.",
                        new Dictionary<string,object>{{"managerRows",owners.Length},
                            {"contactUsed",selected.ContactManagerOwners.Receipt.UsedCount},
                            {"sipUsed",selected.ShapeInstancePairPool.Used},
                            {"actorPairUsed",selected.ActorPairPool.Used},
                            {"actorPairReportUsed",selected.ActorPairReportPool.Used},
                            {"largeUsed",selected.LargeManifoldPool.Used},
                            {"managerPointersInTargetFree",HexArray(managerOverlap)},
                            {"sipPointersInTargetFree",HexArray(sipOverlap)},
                            {"actorPairPointersInTargetFree",HexArray(actorPairOverlap)},
                            {"actorPairPointersMissingFromTargetAllocated",HexArray(actorPairMissing)},
                            {"actorPairReportPointersInTargetFree",HexArray(actorPairReportOverlap)},
                            {"actorPairReportPointersMissingFromTargetAllocated",HexArray(actorPairReportMissing)},
                            {"manifoldPointersInTargetFree",HexArray(manifoldOverlap)}});

                    bool narrow=owners.All(value=>value.ContactCount==0&&value.CacheSize==0&&
                        value.ManifoldBytes==0xF0u&&(value.ManagerFlags&1u)==0);
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.zero-cache-scope",phase,
                        narrow?"pass":"fail","blocker",
                        narrow?"ZERO_CACHE_RECREATE_SCOPE":"UNSUPPORTED_CONTACT_CACHE_STATE",
                        narrow?"All target managers are zero-contact, cacheless, non-modifiable large-manifold pairs.":
                            "At least one target pair carries unsupported contact/cache/manifold state.",
                        new Dictionary<string,object>{{"rows",owners.Length},
                            {"nonzeroContacts",owners.Count(value=>value.ContactCount!=0)},
                            {"nonzeroCaches",owners.Count(value=>value.CacheSize!=0)},
                            {"nonLargeManifolds",owners.Count(value=>value.ManifoldBytes!=0xF0u)},
                            {"modifiableManagers",owners.Count(value=>(value.ManagerFlags&1u)!=0)}});
                }
                catch(Exception error)
                {
                    AddReadinessCheck(checks,blockers,deferred,
                        "rigidbody.target-cross-pool-coherence",phase,"fail","blocker",
                        "TARGET_POOL_COHERENCE_EXCEPTION",error.GetType().Name+": "+error.Message,null);
                }

                try
                {
                    rows=BuildContactRecreateRows(selected);
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.resolved-plan-rows",phase,
                        "pass","blocker","PLAN_ROWS_RESOLVED",
                        "Every target contact row resolves to its current exact Collider/PxShape incarnation.",
                        new Dictionary<string,object>{{"rowCount",rows.Length}});
                }
                catch(Exception error)
                {
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.resolved-plan-rows",phase,
                        "fail","blocker","PLAN_ROW_RESOLUTION_FAILED",
                        error.GetType().Name+": "+error.Message,null);
                }
            }

            NativeContactRecreateAuditReceipt nativeAudit=new NativeContactRecreateAuditReceipt();
            bool nativeAuditAvailable=phase=="target-paused"&&found&&rows!=null;
            if(nativeAuditAvailable)
            {
                try
                {
                    nativeAudit=CallContactRecreateAudit(selected,rows);
                    NativeContactRecreateAuditReceipt repeated=CallContactRecreateAudit(selected,rows);
                    bool repeatedExact=nativeAudit.Equals(repeated);
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.native-audit-repeatability",phase,
                        repeatedExact?"pass":"fail","blocker",
                        repeatedExact?"NATIVE_AUDIT_REPEATABLE":"NATIVE_AUDIT_CHANGED_STATE",
                        repeatedExact?"Two consecutive native audits returned byte-equivalent receipts.":
                            "Repeating the native audit changed its receipt, so it is not observationally stable.",
                        new Dictionary<string,object>{{"first",DescribeContactRecreateAudit(nativeAudit)},
                            {"second",DescribeContactRecreateAudit(repeated)}});
                    AppendNativeAuditChecks(checks,blockers,deferred,phase,nativeAudit);
                }
                catch(Exception error)
                {
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.native-contact-plan",phase,
                        "fail","blocker","NATIVE_AUDIT_CALL_FAILED",
                        error.GetType().Name+": "+error.Message,null);
                }
            }
            else
            {
                AppendDeferredNativeAuditChecks(checks,blockers,deferred,phase,
                    phase=="source-paused"?"Requires the restored target-paused allocator state.":
                        "Requires a valid resolved target plan.");
            }

            AppendActorPairReadiness(checks,blockers,deferred,phase,found?selected:null,
                nativeAuditAvailable?nativeAudit:(NativeContactRecreateAuditReceipt?)null);
            AppendNPhaseReportReadiness(checks,blockers,deferred,phase,found?selected:null);
            AppendInteractionGraphReadiness(checks,blockers,deferred,phase,found?selected:null);
            AppendTransformCacheReadiness(checks,blockers,deferred,phase,found?selected:null);
            AppendFinishBroadPhaseReadiness(checks,blockers,deferred,phase,found?selected:null);
            AppendIslandReadiness(checks,blockers,deferred,phase,found?selected:null);

            if(found&&phase=="target-paused")
            {
                try
                {
                    ManifoldPoolState sphere=CaptureManifoldPoolStateReadOnly(
                        SphereManifoldPoolKind,frame);
                    bool exact=SameManifoldPoolSnapshot(sphere,selected.SphereManifoldPool);
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.sphere-pool",phase,
                        exact?"pass":"fail","blocker",exact?"SPHERE_POOL_EXACT":"SPHERE_POOL_MISMATCH",
                        exact?"The live sphere-manifold allocator already has exact target state.":
                            "The sphere-manifold allocator cannot be restored by an order-only operation.",
                        new Dictionary<string,object>{{"target",DescribeManifoldPoolState(selected.SphereManifoldPool)},
                            {"live",DescribeManifoldPoolState(sphere)}});
                }
                catch(Exception error)
                {
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.sphere-pool",phase,
                        "fail","blocker","SPHERE_POOL_AUDIT_FAILED",
                        error.GetType().Name+": "+error.Message,null);
                }
                try
                {
                    TransformDispatchState live=ReadTransformDispatchState();
                    TransformDispatchState target=selected.TransformDispatch;
                    bool targetValid=target!=null&&live.Dispatch==target.Dispatch&&
                        live.Capacity>=target.Count&&target.Entries!=null&&
                        target.Count==(uint)target.Entries.Length&&
                        target.Entries.Select(value=>value.Hierarchy).Distinct().Count()==target.Entries.Length;
                    if(targetValid)foreach(TransformDispatchEntryState entry in target.Entries)
                    {
                        ReadWord(entry.Hierarchy+0x1Cu);ReadWord(entry.Hierarchy+0x20u);
                        ReadWord(entry.Hierarchy+0x24u);
                    }
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-dispatch",phase,
                        targetValid?"pass":"fail","blocker",
                        targetValid?"TRANSFORM_DISPATCH_WRITABLE":"TRANSFORM_DISPATCH_INCOMPATIBLE",
                        targetValid?"The live dispatch can hold every exact target hierarchy entry.":
                            "The exact target Transform dispatch cannot be reconstructed in the live container.",
                        new Dictionary<string,object>{{"target",target==null?null:DescribeTransformDispatchState(target)},
                            {"live",DescribeTransformDispatchState(live)}});
                }
                catch(Exception error)
                {
                    AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-dispatch",phase,
                        "fail","blocker","TRANSFORM_DISPATCH_AUDIT_FAILED",
                        error.GetType().Name+": "+error.Message,null);
                }
            }
            else
            {
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.sphere-pool",phase,
                    "deferred","blocker","TARGET_PHASE_REQUIRED",
                    "Sphere-manifold live admission requires the restored target-paused state.",null);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-dispatch",phase,
                    "deferred","blocker","TARGET_PHASE_REQUIRED",
                    "Transform-dispatch live admission requires the restored target-paused state.",null);
            }

            string[] futureFamilies={"dirty-interaction-live-projection",
                "first-output-owner-convergence"};
            foreach(string family in futureFamilies)
                AddReadinessCheck(checks,blockers,deferred,"rigidbody."+family,phase,
                    "deferred","blocker","INSTRUMENTATION_REQUIRED",
                    "This PhysX history family is source-proven relevant but has no read-only live planner yet.",
                    new Dictionary<string,object>{{"requiredPhase",family.StartsWith("first-")?
                        "first-advancing-output":"target-paused"}});

            string after=AuditModuleFingerprint();
            bool stateExact=before==after;
            AddReadinessCheck(checks,blockers,deferred,"rigidbody.audit-state-proof",phase,
                stateExact?"pass":"fail","blocker",
                stateExact?"AUDIT_STATE_UNCHANGED":"AUDIT_MUTATED_MODULE_STATE",
                stateExact?"The audit left all tracked managed pending state and receipt counters unchanged.":
                    "The audit changed tracked module state.",
                new Dictionary<string,object>{{"before",before},{"after",after}});

            object coverage=FinalizeReadinessCoverage(checks,blockers,deferred,phase);

            return new Dictionary<string,object>{{"schemaVersion",1},{"provider",Name},
                {"operation","audit-restore-readiness"},{"phase",phase},{"sourceFrame",sourceFrame},
                {"targetFrame",frame},{"currentFrame",CurrentCheckpointFrame()},
                {"passed",blockers.Count==0},{"complete",deferred.Count==0},
                {"checks",checks.ToArray()},{"blockers",blockers.ToArray()},
                {"deferred",deferred.ToArray()},
                {"coverage",coverage},
                {"mutation",new Dictionary<string,object>{{"gameState",false},
                    {"moduleState",!stateExact},{"nativeState",false}}},
                {"targetBinding",new Dictionary<string,object>{{"frame",frame},
                    {"coreSnapshotIdentity",found&&core!=null&&ReferenceEquals(selected.CoreSnapshot,core)},
                    {"sceneMetadataGeneration",sceneMetadataGeneration},
                    {"context",contactManagerContext==0?null:"0x"+contactManagerContext.ToString("X8")}}},
                {"nativeAudit",nativeAuditAvailable?DescribeContactRecreateAudit(nativeAudit):null}};
        }

        private static readonly string[] RequiredReadinessCheckIds={
            "rigidbody.target-binding","rigidbody.actor-pair-pool",
            "rigidbody.zero-cache-scope","rigidbody.sphere-pool",
            "rigidbody.transform-dispatch",
            "rigidbody.interaction-registration-order",
            "rigidbody.island-edge-allocator-and-change-queues",
            "rigidbody.island.capture-repeatability","rigidbody.island.layout-identity",
            "rigidbody.island.node-topology","rigidbody.island.edge-topology",
            "rigidbody.island.island-topology","rigidbody.island.allocator-free-order",
            "rigidbody.island.node-bitmaps","rigidbody.island.change-queue-order",
            "rigidbody.island.sip-edge-bindings","rigidbody.island.edge-type-scope",
            "rigidbody.island.body-owner-coherence","rigidbody.island.transition-capture",
            "rigidbody.island.transition-identity","rigidbody.island.transition-pre-state",
            "rigidbody.island.transition-post-state","rigidbody.island.edge-journal",
            "rigidbody.transform-cache-id-pool",
            "rigidbody.broadphase-created-overlap-order",
            "rigidbody.dirty-interaction-live-projection",
            "rigidbody.contact-report-lists-and-buffer",
            "rigidbody.first-output-owner-convergence",
            "rigidbody.audit-state-proof"
        };

        private static readonly string[] RequiredTargetReadinessCheckIds={
            "rigidbody.target-contact-sidecar","rigidbody.target-sip-sidecar",
            "rigidbody.target-actor-pair-sidecar","rigidbody.target-actor-pair-report-sidecar",
            "rigidbody.target-nphase-report-sidecar","rigidbody.target-interaction-graph-sidecar",
            "rigidbody.target-transform-cache-sidecar",
            "rigidbody.target-broadphase-transition-sidecar",
            "rigidbody.target-island-sidecar","rigidbody.target-island-transition-sidecar",
            "rigidbody.target-large-sidecar","rigidbody.target-sphere-sidecar",
            "rigidbody.target-dirty-sidecar","rigidbody.target-cross-pool-coherence",
            "rigidbody.resolved-plan-rows","rigidbody.native-audit-repeatability",
            "rigidbody.native.arguments","rigidbody.native.observer",
            "rigidbody.native.recreate-state","rigidbody.native.revisions",
            "rigidbody.native.target-buffers","rigidbody.native.target-uniqueness",
            "rigidbody.native.rows","rigidbody.native.row-uniqueness",
            "rigidbody.native.sip-capture","rigidbody.native.sip-identity",
            "rigidbody.native.sip-semantic-counts","rigidbody.native.sip-membership",
            "rigidbody.native.sip-legacy-arm","rigidbody.native.contact-capture",
            "rigidbody.native.contact-identity","rigidbody.native.contact-bitmaps",
            "rigidbody.native.contact-membership","rigidbody.native.large-capture",
            "rigidbody.native.large-identity","rigidbody.native.large-membership",
            "rigidbody.native.writability",
            "rigidbody.actor-pair.repeatability","rigidbody.actor-pair.pool-identity",
            "rigidbody.actor-pair.target-partition","rigidbody.actor-pair.live-partition",
            "rigidbody.actor-pair.reachability","rigidbody.actor-pair.reference-counts",
            "rigidbody.actor-pair.target-coherence","rigidbody.actor-pair.touch-state",
            "rigidbody.actor-pair.internal-flags","rigidbody.actor-pair-report.repeatability",
            "rigidbody.actor-pair.report-data-pool","rigidbody.actor-pair.reuse-decision",
            "rigidbody.actor-pair.allocation-binding",
            "rigidbody.nphase-report.repeatability","rigidbody.nphase-report.layout-identity",
            "rigidbody.nphase-report.target-membership","rigidbody.nphase-report.live-projection",
            "rigidbody.nphase-report.scene-timestamps",
            "rigidbody.interaction-graph.repeatability","rigidbody.interaction-graph.layout-identity",
            "rigidbody.interaction-graph.active-body-order","rigidbody.interaction-graph.global-order",
            "rigidbody.interaction-graph.actor-order-and-cached-indices",
            "rigidbody.interaction-graph.sip-semantic-keys",
            "rigidbody.interaction-graph.pointer-pool-topology",
            "rigidbody.transform-cache.repeatability","rigidbody.transform-cache.layout-identity",
            "rigidbody.transform-cache.free-id-order",
            "rigidbody.transform-cache.binding-and-refcounts",
            "rigidbody.transform-cache.active-transforms",
            "rigidbody.broadphase.capture","rigidbody.broadphase.identity",
            "rigidbody.broadphase.created-order","rigidbody.broadphase.deleted-order",
            "rigidbody.broadphase.post-state"
        };

        private static object FinalizeReadinessCoverage(List<object> checks,List<object> blockers,
            List<object> deferred,string phase)
        {
            string[] ids=checks.Cast<Dictionary<string,object>>()
                .Select(value=>Convert.ToString(value["id"])).ToArray();
            string[] duplicates=ids.GroupBy(value=>value,StringComparer.Ordinal)
                .Where(group=>group.Count()>1).Select(group=>group.Key).ToArray();
            string[] requiredIds=(phase=="target-paused"?
                RequiredReadinessCheckIds.Concat(RequiredTargetReadinessCheckIds):
                RequiredReadinessCheckIds).ToArray();
            string[] missing=requiredIds.Where(required=>
                !ids.Contains(required,StringComparer.Ordinal)).ToArray();
            foreach(string id in missing)
                AddReadinessCheck(checks,blockers,deferred,id,phase,"fail","blocker",
                    "MISSING_REQUIRED_CHECK","The provider omitted a required readiness family.",null);
            AddReadinessCheck(checks,blockers,deferred,"rigidbody.coverage-manifest",phase,
                duplicates.Length==0&&missing.Length==0?"pass":"fail","blocker",
                duplicates.Length!=0?"DUPLICATE_CHECK_ID":
                    missing.Length!=0?"MISSING_REQUIRED_CHECK":"REQUIRED_COVERAGE_ENUMERATED",
                duplicates.Length==0&&missing.Length==0?
                    "Every required readiness family is explicitly represented.":
                    "The provider omitted or duplicated required readiness identifiers.",
                new Dictionary<string,object>{{"contractVersion",6},
                    {"required",requiredIds.Cast<object>().ToArray()},
                    {"uncovered",missing.Cast<object>().ToArray()},
                    {"duplicates",duplicates.Cast<object>().ToArray()}});
            return new Dictionary<string,object>{{"contractVersion",6},
                {"required",requiredIds.Cast<object>().ToArray()},
                {"uncovered",missing.Cast<object>().ToArray()},
                {"duplicates",duplicates.Cast<object>().ToArray()}};
        }

        private delegate void AuditValidator();

        private static void AddValidatorCheck(List<object> checks,List<object> blockers,
            List<object> deferred,string id,string phase,AuditValidator validator,
            string code,string message)
        {
            try
            {
                validator();
                AddReadinessCheck(checks,blockers,deferred,id,phase,"pass","blocker",code,message,null);
            }
            catch(Exception error)
            {
                AddReadinessCheck(checks,blockers,deferred,id,phase,"fail","blocker",
                    code+"_FAILED",error.GetType().Name+": "+error.Message,null);
            }
        }

        private static void AddReadinessCheck(List<object> checks,List<object> blockers,
            List<object> deferred,string id,string phase,string status,string severity,
            string code,string message,object evidence)
        {
            var value=new Dictionary<string,object>{{"id",id},{"module","rigidbody-actor-rebuild"},
                {"phase",phase},{"status",status},{"severity",severity},{"code",code},
                {"message",message},{"evidence",evidence},
                {"mutation",new Dictionary<string,object>{{"gameState",false},
                    {"moduleState",false},{"nativeState",false}}}};
            checks.Add(value);
            if(status=="fail"&&severity=="blocker")blockers.Add(id);
            if(status=="deferred")deferred.Add(id);
        }

        private static readonly string[] NativeAuditCheckNames={"arguments","observer",
            "recreate-state","revisions","target-buffers","target-uniqueness","rows",
            "row-uniqueness","sip-capture","sip-identity","sip-semantic-counts",
            "sip-membership","sip-legacy-arm","contact-capture","contact-identity",
            "contact-bitmaps","contact-membership","large-capture","large-identity",
            "large-membership","writability"};

        private static void AppendNativeAuditChecks(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,NativeContactRecreateAuditReceipt receipt)
        {
            object evidence=DescribeContactRecreateAudit(receipt);
            for(int i=0;i<NativeAuditCheckNames.Length;i++)
            {
                uint bit=1u<<i;
                bool evaluated=(receipt.EvaluatedMask&bit)!=0;
                bool issue=(receipt.IssueMask&bit)!=0;
                string status=!evaluated?"deferred":issue?"fail":"pass";
                string name=NativeAuditCheckNames[i];
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.native."+name,phase,status,"blocker",
                    !evaluated?"NATIVE_CHECK_DEFERRED":issue?"NATIVE_CHECK_FAILED":"NATIVE_CHECK_PASSED",
                    !evaluated?"A prerequisite prevented this native check from being evaluated safely.":
                        issue?"The native read-only planner found an incompatible restore condition.":
                            "The native read-only planner accepted this condition.",evidence);
            }
        }

        private static void AppendDeferredNativeAuditChecks(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,string reason)
        {
            foreach(string name in NativeAuditCheckNames)
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.native."+name,phase,
                    "deferred","blocker","TARGET_PHASE_REQUIRED",reason,null);
        }

        private static ActorPairReuseScanState ScanActorPairReuseCandidate(
            IGrouping<uint,NativeContactManagerOwnerRecord> group)
        {
            NativeContactManagerOwnerRecord first=group.First();
            uint actor0=first.ActorPairActor0.ToUInt32(),actor1=first.ActorPairActor1.ToUInt32();
            if(actor0==0||actor1==0||actor0==actor1)
                throw new InvalidOperationException("ActorPair endpoints are null or identical.");
            uint count0=ReadWord(actor0+0x1Cu),count1=ReadWord(actor1+0x1Cu);
            if(count0>MaximumShapeInstancePairs||count1>MaximumShapeInstancePairs)
                throw new InvalidOperationException("Actor interaction count is outside the guarded bound.");
            // Exact shipped findActorPair choice: ties scan actor1.
            uint scanned=count0<count1?actor0:actor1;
            uint other=scanned==actor0?actor1:actor0;
            uint count=scanned==actor0?count0:count1;
            uint data=ReadWord(scanned+0x14u);
            if(count!=0&&data==0)
                throw new InvalidOperationException("Actor interaction array is null with a nonzero count.");
            uint sipCandidates=0,matching=0;
            var matchingActorPairs=new List<uint>();
            for(uint i=0;i<count;i++)
            {
                uint interaction=ReadWord(data+i*4u);
                if(interaction==0)throw new InvalidOperationException("Actor interaction array contains null.");
                uint endpoint0=ReadWord(interaction+0x04u),endpoint1=ReadWord(interaction+0x08u);
                byte flags=ReadBytes(interaction+0x15u,1)[0];
                if((flags&0x10)==0)continue;
                sipCandidates++;
                if(!((endpoint0==scanned&&endpoint1==other)||
                    (endpoint0==other&&endpoint1==scanned)))continue;
                matching++;
                matchingActorPairs.Add(ReadWord(interaction+0x28u));
            }
            return new ActorPairReuseScanState {ActorPair=group.Key,Actor0=actor0,Actor1=actor1,
                ScannedActor=scanned,OtherActor=other,InteractionCount=count,
                SipCandidates=sipCandidates,MatchingSipCandidates=matching,
                MatchingActorPairs=matchingActorPairs.ToArray()};
        }

        private static object DescribeActorPairReuseScan(ActorPairReuseScanState value)
        {
            return new Dictionary<string,object>{{"targetActorPair","0x"+value.ActorPair.ToString("X8")},
                {"actor0","0x"+value.Actor0.ToString("X8")},{"actor1","0x"+value.Actor1.ToString("X8")},
                {"scannedActor","0x"+value.ScannedActor.ToString("X8")},
                {"otherActor","0x"+value.OtherActor.ToString("X8")},
                {"interactionCount",value.InteractionCount},{"sipCandidates",value.SipCandidates},
                {"matchingSipCandidates",value.MatchingSipCandidates},
                {"matchingActorPairs",HexArray(value.MatchingActorPairs)}};
        }

        private void AppendActorPairReadiness(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,CheckpointSidecar selected,
            NativeContactRecreateAuditReceipt? nativeAudit)
        {
            if(phase!="target-paused"||selected==null)
            {
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair-pool",phase,
                    "deferred","blocker","TARGET_PHASE_REQUIRED",
                    "ActorPair live admission requires the restored target-paused allocator state.",null);
                return;
            }
            try
            {
                ValidateActorPairPoolState(selected.ActorPairPool);
                ActorPairPoolState first=CaptureActorPairPoolState(
                    selected.ActorPairPool.NPhaseCore,selected.Frame);
                ActorPairPoolState second=CaptureActorPairPoolState(
                    selected.ActorPairPool.NPhaseCore,selected.Frame);
                bool repeatable=SameActorPairPoolSnapshot(first,second);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.repeatability",phase,
                    repeatable?"pass":"fail","blocker",
                    repeatable?"ACTOR_PAIR_CAPTURE_REPEATABLE":"ACTOR_PAIR_CAPTURE_CHANGED",
                    repeatable?"Two caller-owned ActorPair pool captures are byte-equivalent.":
                        "Repeated ActorPair pool capture observed a changing allocator.",
                    new Dictionary<string,object>{{"first",DescribeActorPairPoolState(first)},
                        {"second",DescribeActorPairPoolState(second)}});

                ActorPairPoolState target=selected.ActorPairPool;
                bool identity=first.NPhaseCore==target.NPhaseCore&&first.Pool==target.Pool&&
                    first.ElementSize==target.ElementSize&&
                    first.ElementsPerSlab==target.ElementsPerSlab&&
                    first.SlabSize==target.SlabSize&&first.SlabCount==target.SlabCount&&
                    first.TotalElements==target.TotalElements;
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.pool-identity",phase,
                    identity?"pass":"fail","blocker",
                    identity?"ACTOR_PAIR_POOL_IDENTITY":"ACTOR_PAIR_POOL_CHANGED",
                    identity?"The target and live ActorPair partitions belong to the same exact pool.":
                        "The live ActorPair allocator identity or layout differs from the target.",
                    new Dictionary<string,object>{{"target",DescribeActorPairPoolState(target)},
                        {"live",DescribeActorPairPoolState(first)}});

                uint[] targetPointers=selected.ContactManagerOwners.Records
                    .Select(value=>value.ActorPair.ToUInt32()).Distinct().ToArray();
                var targetAllocated=new HashSet<uint>(target.AllocatedOrder);
                bool targetPartition=target.Used==(uint)targetPointers.Length&&
                    targetPointers.All(targetAllocated.Contains)&&
                    targetPointers.Length==targetAllocated.Count;
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.target-partition",phase,
                    targetPartition?"pass":"fail","blocker",
                    targetPartition?"ACTOR_PAIR_TARGET_PARTITION_EXACT":"ACTOR_PAIR_TARGET_PARTITION_MISMATCH",
                    targetPartition?"Every target ActorPair is exactly the target allocated partition.":
                        "The target owner rows and target ActorPair allocated partition disagree.",
                    new Dictionary<string,object>{{"targetPointers",HexArray(targetPointers)},
                        {"targetAllocated",HexArray(target.AllocatedOrder)}});

                var expectedLiveFree=new HashSet<uint>(target.FreeOrder);
                expectedLiveFree.UnionWith(target.AllocatedOrder);
                var liveFree=new HashSet<uint>(first.FreeOrder);
                bool livePartition=first.Used==0&&first.AllocatedOrder.Length==0&&
                    liveFree.SetEquals(expectedLiveFree);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.live-partition",phase,
                    livePartition?"pass":"fail","blocker",
                    livePartition?"ACTOR_PAIR_LIVE_PARTITION_EXACT":"ACTOR_PAIR_LIVE_PARTITION_MISMATCH",
                    livePartition?"The live free partition is exactly target-free plus every ActorPair to recreate.":
                        "The restored ActorPair pool contains a missing, extra, or still-allocated element.",
                    new Dictionary<string,object>{{"expectedLiveFreeCount",expectedLiveFree.Count},
                        {"liveFreeCount",first.FreeOrder.Length},{"liveAllocated",HexArray(first.AllocatedOrder)}});

                uint[] missing=targetPointers.Where(value=>!liveFree.Contains(value)).ToArray();
                bool reachable=missing.Length==0;
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.reachability",phase,
                    reachable?"pass":"fail","blocker",
                    reachable?"ACTOR_PAIR_TARGETS_REACHABLE":"ACTOR_PAIR_TARGET_MISSING",
                    reachable?"Every exact target ActorPair is reachable in the live free list.":
                        "At least one exact target ActorPair cannot be selected from the live allocator.",
                    new Dictionary<string,object>{{"uniqueTargets",targetPointers.Length},
                        {"missing",HexArray(missing)},
                        {"liveFreeIndices",targetPointers.Select(value=>(object)Array.IndexOf(first.FreeOrder,value)).ToArray()}});

                NativeContactManagerOwnerRecord[] owners=selected.ContactManagerOwners.Records;
                var groups=owners.GroupBy(value=>value.ActorPair.ToUInt32()).ToArray();
                const uint SipHasTouch=0x00200000u;
                bool referenceCounts=groups.All(group=>
                {
                    NativeContactManagerOwnerRecord item=group.First();
                    int reportSetReference=(item.ActorPairInternalFlags&1u)!=0?1:0;
                    return item.ActorPairRefCount==group.Count()+reportSetReference;
                });
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.reference-counts",phase,
                    referenceCounts?"pass":"fail","blocker",
                    referenceCounts?"ACTOR_PAIR_REFERENCE_COUNTS_EXACT":"ACTOR_PAIR_REFERENCE_COUNT_MISMATCH",
                    referenceCounts?"Every ActorPair reference count equals its complete SIP ownership plus any report-set reference.":
                        "At least one ActorPair reference count contradicts its captured owners and report-set flag.",
                    new Dictionary<string,object>{{"uniqueTargets",groups.Length},{"rows",owners.Length}});

                bool touchCoherent=groups.All(group=>
                    group.First().ActorPairTouchCount==group.Count(value=>(value.SipFlags&SipHasTouch)!=0));
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.target-coherence",phase,
                    touchCoherent&&referenceCounts?"pass":"fail","blocker",
                    touchCoherent&&referenceCounts?"ACTOR_PAIR_TARGET_COHERENT":"ACTOR_PAIR_TARGET_CONTRADICTION",
                    touchCoherent&&referenceCounts?
                        "ActorPair touch counts, SIP touch flags, references, endpoints, and allocated ownership are coherent.":
                        "The target ActorPair fields contradict the captured SIP or ownership state.",
                    new Dictionary<string,object>{{"touchCounts",groups.Select(group=>(object)
                            group.First().ActorPairTouchCount).ToArray()},
                        {"sipTouchCounts",groups.Select(group=>(object)
                            group.Count(value=>(value.SipFlags&SipHasTouch)!=0)).ToArray()}});

                int touched=groups.Count(group=>group.First().ActorPairTouchCount!=0);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.touch-state",phase,
                    !touchCoherent?"fail":touched==0?"pass":"deferred","blocker",
                    !touchCoherent?"ACTOR_PAIR_TOUCH_STATE_CONTRADICTORY":
                        touched==0?"ACTOR_PAIR_TOUCH_STATE_EMPTY":"ACTOR_PAIR_TOUCH_RESTORE_REQUIRED",
                    !touchCoherent?"ActorPair and SIP touch state disagree.":
                        touched==0?"No target ActorPair carries persistent touch history.":
                            "Valid target ActorPairs carry touch history; exact direct reconstruction is not implemented yet.",
                    new Dictionary<string,object>{{"touchedActorPairs",touched},{"uniqueTargets",groups.Length}});

                int flagged=groups.Count(group=>group.First().ActorPairInternalFlags!=0);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.internal-flags",phase,
                    flagged==0?"not-applicable":"deferred","blocker",
                    flagged==0?"ACTOR_PAIR_NO_PENDING_REPORT_SET":"ACTOR_PAIR_REPORT_SET_RESTORE_REQUIRED",
                    flagged==0?"No target ActorPair is pending in a report or threshold set.":
                        "Target ActorPair flags require exact report/threshold set membership reconstruction.",
                    new Dictionary<string,object>{{"flaggedActorPairs",flagged}});

                ActorPairReportPoolState targetReports=selected.ActorPairReportPool;
                ValidateActorPairReportPoolState(targetReports);
                ActorPairReportPoolState liveReports=CaptureActorPairReportPoolState(
                    targetReports.NPhaseCore,selected.Frame);
                ActorPairReportPoolState liveReportsRepeated=CaptureActorPairReportPoolState(
                    targetReports.NPhaseCore,selected.Frame);
                bool reportRepeatable=SameActorPairReportPoolSnapshot(liveReports,liveReportsRepeated);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair-report.repeatability",phase,
                    reportRepeatable?"pass":"fail","blocker",
                    reportRepeatable?"ACTOR_PAIR_REPORT_CAPTURE_REPEATABLE":"ACTOR_PAIR_REPORT_CAPTURE_CHANGED",
                    reportRepeatable?"Two caller-owned ActorPair report-pool captures are byte-equivalent.":
                        "Repeated ActorPair report-pool capture observed changing state.",
                    new Dictionary<string,object>{{"first",DescribeActorPairReportPoolState(liveReports)},
                        {"second",DescribeActorPairReportPoolState(liveReportsRepeated)}});
                uint[] targetReportPointers=groups.Select(group=>group.First().ActorPairReportData.ToUInt32())
                    .Where(value=>value!=0).ToArray();
                bool reportTargetPartition=targetReports.Used==(uint)targetReportPointers.Length&&
                    new HashSet<uint>(targetReports.AllocatedOrder).SetEquals(targetReportPointers);
                var expectedLiveReports=new HashSet<uint>(targetReports.FreeOrder);
                expectedLiveReports.UnionWith(targetReports.AllocatedOrder);
                bool reportLivePartition=liveReports.Used==0&&
                    new HashSet<uint>(liveReports.FreeOrder).SetEquals(expectedLiveReports);
                bool reportStructural=reportRepeatable&&reportTargetPartition&&reportLivePartition&&
                    liveReports.Pool==targetReports.Pool&&liveReports.ElementSize==targetReports.ElementSize&&
                    liveReports.SlabCount==targetReports.SlabCount;
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.report-data-pool",phase,
                    reportStructural?"deferred":"fail","blocker",
                    reportStructural?"ACTOR_PAIR_REPORT_BINDING_REQUIRED":"ACTOR_PAIR_REPORT_POOL_MISMATCH",
                    reportStructural?
                        "The exact target report objects, bytes, and live free reachability are captured; semantic address binding and reconstruction remain.":
                        "ActorPair report-data pool identity, partition, or owner mapping is inconsistent.",
                    new Dictionary<string,object>{{"target",DescribeActorPairReportPoolState(targetReports)},
                        {"live",DescribeActorPairReportPoolState(liveReports)},
                        {"ownerReportPointers",HexArray(targetReportPointers)}});

                bool sipAllocatorEmpty=nativeAudit.HasValue&&nativeAudit.Value.LiveSipUsed==0;
                ActorPairReuseScanState[] reuseScans=groups.Select(ScanActorPairReuseCandidate).ToArray();
                bool reuseExact=sipAllocatorEmpty&&reuseScans.All(value=>value.MatchingSipCandidates==0);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.reuse-decision",phase,
                    reuseExact?"pass":"fail","blocker",
                    reuseExact?"ACTOR_PAIR_ALLOCATE_DECISION_PROVED":"ACTOR_PAIR_REUSE_CANDIDATE_PRESENT",
                    reuseExact?"The live SIP allocator and exact actor-interaction scans prove every target pair must allocate, not reuse.":
                        "A live matching SIP or nonempty allocator makes the target allocate-versus-reuse decision incompatible.",
                    new Dictionary<string,object>{{"liveSipUsed",nativeAudit.HasValue?
                            (object)nativeAudit.Value.LiveSipUsed:null},
                        {"scans",reuseScans.Select(value=>DescribeActorPairReuseScan(value)).ToArray()}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair.allocation-binding",phase,
                    "deferred","blocker","ACTOR_PAIR_SEMANTIC_STEERING_REQUIRED",
                    "All target addresses are reachable, but the current hook does not yet bind each semantic pair to its exact ActorPair before PhysX allocation.",
                    new Dictionary<string,object>{{"freeIndices",targetPointers.Select(value=>(object)
                        Array.IndexOf(first.FreeOrder,value)).ToArray()}});

                bool structural=repeatable&&identity&&targetPartition&&livePartition&&reachable&&
                    touchCoherent&&referenceCounts&&reportStructural&&reuseExact;
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair-pool",phase,
                    structural?"deferred":"fail","blocker",
                    structural?"ACTOR_PAIR_RECONSTRUCTION_REQUIRED":"ACTOR_PAIR_STRUCTURAL_MISMATCH",
                    structural?"ActorPair and report-data capture, coherence, partitions, reachability, and reuse decisions are exact; direct state reconstruction and address binding remain.":
                        "ActorPair structural admission failed before replay.",
                    new Dictionary<string,object>{{"target",DescribeActorPairPoolState(target)},
                        {"live",DescribeActorPairPoolState(first)},
                        {"uniqueTargetActorPairs",targetPointers.Length}});
            }
            catch(Exception error)
            {
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.actor-pair-pool",phase,
                    "fail","blocker","ACTOR_PAIR_AUDIT_FAILED",
                    error.GetType().Name+": "+error.Message,null);
            }
        }

        private void AppendNPhaseReportReadiness(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,CheckpointSidecar selected)
        {
            if(phase!="target-paused"||selected==null)
            {
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.contact-report-lists-and-buffer",phase,
                    "deferred","blocker","TARGET_PHASE_REQUIRED",
                    "NPhase contact-report live admission requires the restored target-paused state.",null);
                return;
            }
            string[] detailIds={"rigidbody.nphase-report.repeatability",
                "rigidbody.nphase-report.layout-identity",
                "rigidbody.nphase-report.target-membership",
                "rigidbody.nphase-report.live-projection",
                "rigidbody.nphase-report.scene-timestamps",
                "rigidbody.contact-report-lists-and-buffer"};
            try
            {
                NPhaseReportState target=selected.NPhaseReports;
                ValidateNPhaseReportState(target);
                uint nphase=target.Receipt.NPhaseCore.ToUInt32();
                NPhaseReportState first=CaptureNPhaseReportState(nphase,selected.Frame);
                NPhaseReportState second=CaptureNPhaseReportState(nphase,selected.Frame);
                bool repeatable=SameNPhaseReportState(first,second);
                NativeNPhaseReportStateReceipt targetReceipt=target.Receipt;
                NativeNPhaseReportStateReceipt liveReceipt=first.Receipt;
                bool layout=targetReceipt.NPhaseCore==liveReceipt.NPhaseCore&&
                    targetReceipt.OwnerScene==liveReceipt.OwnerScene&&
                    targetReceipt.ActorPairData==liveReceipt.ActorPairData&&
                    targetReceipt.ActorPairCapacityRaw==liveReceipt.ActorPairCapacityRaw&&
                    targetReceipt.PersistentData==liveReceipt.PersistentData&&
                    targetReceipt.PersistentCapacityRaw==liveReceipt.PersistentCapacityRaw&&
                    targetReceipt.ForceThresholdData==liveReceipt.ForceThresholdData&&
                    targetReceipt.ForceThresholdCapacityRaw==liveReceipt.ForceThresholdCapacityRaw&&
                    targetReceipt.ReportBuffer==liveReceipt.ReportBuffer&&
                    targetReceipt.ReportBufferCurrentSize==liveReceipt.ReportBufferCurrentSize&&
                    targetReceipt.ReportBufferDefaultSize==liveReceipt.ReportBufferDefaultSize;

                NativeContactManagerOwnerRecord[] owners=selected.ContactManagerOwners.Records;
                var bySip=owners.ToDictionary(value=>value.Sip.ToUInt32());
                uint[] expectedActorPairs=owners.GroupBy(value=>value.ActorPair.ToUInt32())
                    .Where(group=>(group.First().ActorPairInternalFlags&1u)!=0)
                    .Select(group=>group.Key).ToArray();
                uint[] expectedPersistent=owners.Where(value=>(value.SipFlags&0x00200000u)!=0)
                    .Select(value=>value.Sip.ToUInt32()).ToArray();
                uint[] expectedForce=owners.Where(value=>(value.SipFlags&0x00800000u)!=0)
                    .Select(value=>value.Sip.ToUInt32()).ToArray();
                bool reportIndices=true;
                for(int i=0;i<target.PersistentSips.Length;i++)
                {
                    NativeContactManagerOwnerRecord owner;
                    if(!bySip.TryGetValue(target.PersistentSips[i],out owner)||
                        owner.ReportPairIndex!=(uint)i)reportIndices=false;
                }
                for(int i=0;i<target.ForceThresholdSips.Length;i++)
                {
                    NativeContactManagerOwnerRecord owner;
                    if(!bySip.TryGetValue(target.ForceThresholdSips[i],out owner)||
                        owner.ReportPairIndex!=(uint)i)reportIndices=false;
                }
                bool unlistedIndices=owners.Where(value=>(value.SipFlags&0x00A00000u)==0)
                    .All(value=>value.ReportPairIndex==0xFFFFFFFFu);
                bool membership=new HashSet<uint>(target.ActorPairs).SetEquals(expectedActorPairs)&&
                    new HashSet<uint>(target.PersistentSips).SetEquals(expectedPersistent)&&
                    new HashSet<uint>(target.ForceThresholdSips).SetEquals(expectedForce)&&
                    reportIndices&&unlistedIndices;
                bool exact=SameNPhaseReportState(first,target);

                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.nphase-report.repeatability",phase,
                    repeatable?"pass":"fail","blocker",
                    repeatable?"NPHASE_REPORT_CAPTURE_REPEATABLE":"NPHASE_REPORT_CAPTURE_CHANGED",
                    repeatable?"Two caller-owned NPhase report captures are byte-equivalent.":
                        "Repeated NPhase report capture observed changing lists or buffer bytes.",
                    new Dictionary<string,object>{{"first",DescribeNPhaseReportState(first)},
                        {"second",DescribeNPhaseReportState(second)}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.nphase-report.layout-identity",phase,
                    layout?"pass":"fail","blocker",
                    layout?"NPHASE_REPORT_LAYOUT_STABLE":"NPHASE_REPORT_LAYOUT_CHANGED",
                    layout?"Target and live report containers share exact identities, capacities, and backing allocation.":
                        "A report container moved or changed capacity, so direct reconstruction is unsafe.",
                    new Dictionary<string,object>{{"target",DescribeNPhaseReportState(target)},
                        {"live",DescribeNPhaseReportState(first)}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.nphase-report.target-membership",phase,
                    membership?"pass":"fail","blocker",
                    membership?"NPHASE_REPORT_MEMBERSHIP_COHERENT":"NPHASE_REPORT_MEMBERSHIP_CONTRADICTION",
                    membership?"ActorPair and SIP flags, report-list membership, and physical report indices agree.":
                        "Saved report lists contradict ActorPair/SIP flags or report indices.",
                    new Dictionary<string,object>{{"actorPairMembers",target.ActorPairs.Length},
                        {"persistentMembers",target.PersistentSips.Length},
                        {"nextFramePersistentIndex",target.Receipt.NextFramePersistentIndex},
                        {"forceThresholdMembers",target.ForceThresholdSips.Length},
                        {"reportIndicesExact",reportIndices},{"unlistedIndicesExact",unlistedIndices}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.nphase-report.live-projection",phase,
                    !layout?"fail":exact?"pass":"deferred","blocker",
                    !layout?"NPHASE_REPORT_PROJECTION_UNSAFE":
                        exact?"NPHASE_REPORT_ALREADY_EXACT":"NPHASE_REPORT_PROJECTION_REQUIRED",
                    !layout?"The live allocation cannot safely receive the target report state.":
                        exact?"The live NPhase report lists and entire backing allocation already equal the target.":
                            "The exact target lists and full buffer are captured, but atomic projection is not implemented yet.",
                    new Dictionary<string,object>{{"exact",exact},
                        {"target",DescribeNPhaseReportState(target)},
                        {"live",DescribeNPhaseReportState(first)}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.nphase-report.scene-timestamps",phase,
                    "deferred","blocker","NPHASE_SCENE_TIMESTAMPS_UNMAPPED",
                    "Per-SIP report stamps are captured, but the shipped Scene-level report timestamp offsets remain intentionally unmapped.",
                    new Dictionary<string,object>{{"capturedSipStamps",owners.Length},
                        {"sceneOffsetsGuessed",false}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.contact-report-lists-and-buffer",phase,
                    !repeatable||!layout||!membership?"fail":exact?"pass":"deferred","blocker",
                    !repeatable||!layout||!membership?"NPHASE_REPORT_ADMISSION_FAILED":
                        exact?"NPHASE_REPORT_STATE_EXACT":"NPHASE_REPORT_RECONSTRUCTION_REQUIRED",
                    !repeatable||!layout||!membership?
                        "NPhase report history failed structural admission before replay.":
                        exact?"NPhase report lists, split boundary, metadata, and full allocation already match.":
                            "Exact report history is captured and coherent; atomic projection and Scene timestamp restoration remain.",
                    new Dictionary<string,object>{{"exact",exact},{"fullBufferBytes",target.ReportBufferBytes.Length}});
            }
            catch(Exception error)
            {
                foreach(string id in detailIds)
                    AddReadinessCheck(checks,blockers,deferred,id,phase,"fail","blocker",
                        "NPHASE_REPORT_AUDIT_FAILED",error.GetType().Name+": "+error.Message,null);
            }
        }

        private void AppendInteractionGraphReadiness(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,CheckpointSidecar selected)
        {
            if(phase!="target-paused"||selected==null)
            {
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.interaction-registration-order",phase,
                    "deferred","blocker","TARGET_PHASE_REQUIRED",
                    "Interaction-graph live admission requires the restored target-paused state.",null);
                return;
            }
            string[] ids={"rigidbody.interaction-graph.repeatability",
                "rigidbody.interaction-graph.layout-identity",
                "rigidbody.interaction-graph.active-body-order",
                "rigidbody.interaction-graph.global-order",
                "rigidbody.interaction-graph.actor-order-and-cached-indices",
                "rigidbody.interaction-graph.sip-semantic-keys",
                "rigidbody.interaction-graph.pointer-pool-topology",
                "rigidbody.interaction-registration-order"};
            try
            {
                InteractionGraphState target=selected.InteractionGraph;
                ValidateInteractionGraphState(target);
                uint nphase=target.Receipt.NPhaseCore.ToUInt32();
                InteractionGraphState first=CaptureInteractionGraphState(nphase,selected.Frame);
                InteractionGraphState second=CaptureInteractionGraphState(nphase,selected.Frame);
                bool repeatable=SameInteractionGraphState(first,second);
                NativeInteractionGraphReceipt a=target.Receipt,b=first.Receipt;
                bool poolLayout=a.Pools.Length==b.Pools.Length;
                if(poolLayout)for(int i=0;i<a.Pools.Length;i++)
                {
                    NativeInteractionGraphPoolReceipt x=a.Pools[i],y=b.Pools[i];
                    if(x.Pool!=y.Pool||x.SlabData!=y.SlabData||
                        x.BlockCapacity!=y.BlockCapacity||x.BlockBytes!=y.BlockBytes||
                        x.SlabCapacityRaw!=y.SlabCapacityRaw||x.ElementsPerSlab!=y.ElementsPerSlab||
                        x.SlabSize!=y.SlabSize)poolLayout=false;
                }
                bool layout=a.NPhaseCore==b.NPhaseCore&&a.OwnerScene==b.OwnerScene&&
                    a.InteractionScene==b.InteractionScene&&a.LlContext==b.LlContext&&
                    a.ActiveBodiesData==b.ActiveBodiesData&&
                    a.ActiveBodiesCapacityRaw==b.ActiveBodiesCapacityRaw&&
                    a.GlobalData.SequenceEqual(b.GlobalData)&&
                    a.GlobalCapacityRaw.SequenceEqual(b.GlobalCapacityRaw)&&poolLayout;
                bool activeExact=target.ActiveBodies.SequenceEqual(first.ActiveBodies)&&
                    a.ActiveTwoWayStart==b.ActiveTwoWayStart;
                bool globalExact=a.GlobalCount.SequenceEqual(b.GlobalCount)&&
                    a.GlobalActiveCount.SequenceEqual(b.GlobalActiveCount)&&
                    a.GlobalOrderHash.SequenceEqual(b.GlobalOrderHash)&&
                    target.Interactions.Length==first.Interactions.Length;
                if(globalExact)for(int i=0;i<target.Interactions.Length;i++)
                    if(!SameInteractionGraphInteraction(target.Interactions[i],first.Interactions[i]))
                    {globalExact=false;break;}
                bool actorExact=target.Actors.Length==first.Actors.Length&&
                    target.ActorSlots.SequenceEqual(first.ActorSlots);
                if(actorExact)for(int i=0;i<target.Actors.Length;i++)
                    if(!SameInteractionGraphActor(target.Actors[i],first.Actors[i]))
                    {actorExact=false;break;}
                bool poolsExact=target.PoolSlabs.SequenceEqual(first.PoolSlabs)&&
                    target.PoolFree.SequenceEqual(first.PoolFree)&&a.PoolHash==b.PoolHash;
                if(poolsExact)for(int i=0;i<a.Pools.Length;i++)
                    if(!SameInteractionGraphPool(a.Pools[i],b.Pools[i])){poolsExact=false;break;}

                NativeContactManagerOwnerRecord[] owners=selected.ContactManagerOwners.Records;
                var targetByPointer=target.Interactions.ToDictionary(item=>item.Interaction.ToUInt32());
                bool targetSemantic=owners.All(owner=>
                {
                    NativeInteractionGraphInteractionRecord item;
                    if(!targetByPointer.TryGetValue(unchecked(owner.Sip.ToUInt32()+8u),out item)||
                        item.InteractionType!=0||(item.InteractionFlags&0x10u)==0)return false;
                    uint low=Math.Min(owner.PxsShapeCore0.ToUInt32(),owner.PxsShapeCore1.ToUInt32());
                    uint high=Math.Max(owner.PxsShapeCore0.ToUInt32(),owner.PxsShapeCore1.ToUInt32());
                    return item.SemanticLow.ToUInt32()==low&&item.SemanticHigh.ToUInt32()==high;
                });
                var liveKeys=new HashSet<string>(first.Interactions.Where(item=>item.InteractionType==0&&
                    (item.InteractionFlags&0x10u)!=0).Select(item=>item.SemanticLow.ToUInt32().ToString("X8")+
                        ":"+item.SemanticHigh.ToUInt32().ToString("X8")),StringComparer.Ordinal);
                string[] targetKeys=owners.Select(owner=>Math.Min(owner.PxsShapeCore0.ToUInt32(),
                        owner.PxsShapeCore1.ToUInt32()).ToString("X8")+":"+
                    Math.Max(owner.PxsShapeCore0.ToUInt32(),owner.PxsShapeCore1.ToUInt32()).ToString("X8"))
                    .Distinct(StringComparer.Ordinal).ToArray();
                string[] missingKeys=targetKeys.Where(key=>!liveKeys.Contains(key)).ToArray();
                bool exact=SameInteractionGraphState(target,first);

                AddReadinessCheck(checks,blockers,deferred,"rigidbody.interaction-graph.repeatability",phase,
                    repeatable?"pass":"fail","blocker",
                    repeatable?"INTERACTION_GRAPH_CAPTURE_REPEATABLE":"INTERACTION_GRAPH_CAPTURE_CHANGED",
                    repeatable?"Two complete caller-owned graph captures are byte-equivalent.":
                        "Repeated graph capture observed a changing physics phase.",
                    new Dictionary<string,object>{{"firstGraphHash","0x"+b.GraphHash.ToString("X8")},
                        {"secondGraphHash","0x"+second.Receipt.GraphHash.ToString("X8")}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.interaction-graph.layout-identity",phase,
                    layout?"pass":"fail","blocker",
                    layout?"INTERACTION_GRAPH_LAYOUT_STABLE":"INTERACTION_GRAPH_LAYOUT_CHANGED",
                    layout?"Target and live graphs share the same Scene, arrays, and pointer-pool allocations.":
                        "An interaction container moved or changed capacity, so projection is unsafe.",
                    new Dictionary<string,object>{{"targetScene",Hex(a.InteractionScene)},
                        {"liveScene",Hex(b.InteractionScene)},{"poolLayout",poolLayout}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.interaction-graph.active-body-order",phase,
                    activeExact?"pass":"deferred","blocker",
                    activeExact?"ACTIVE_BODY_ORDER_EXACT":"ACTIVE_BODY_ORDER_PROJECTION_REQUIRED",
                    activeExact?"The active-body order and one-way/two-way split already match.":
                        "The exact target active-body order is captured but not projected yet.",
                    new Dictionary<string,object>{{"target",HexArray(target.ActiveBodies)},
                        {"live",HexArray(first.ActiveBodies)},{"targetSplit",a.ActiveTwoWayStart},
                        {"liveSplit",b.ActiveTwoWayStart}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.interaction-graph.global-order",phase,
                    globalExact?"pass":"deferred","blocker",
                    globalExact?"GLOBAL_INTERACTION_ORDER_EXACT":"GLOBAL_INTERACTION_ORDER_PROJECTION_REQUIRED",
                    globalExact?"All six global interaction arrays and active prefixes already match.":
                        "The target global interaction order is exact, but live registration differs.",
                    new Dictionary<string,object>{{"targetCounts",a.GlobalCount.Cast<object>().ToArray()},
                        {"liveCounts",b.GlobalCount.Cast<object>().ToArray()},
                        {"targetHashes",a.GlobalOrderHash.Select(x=>(object)("0x"+x.ToString("X8"))).ToArray()},
                        {"liveHashes",b.GlobalOrderHash.Select(x=>(object)("0x"+x.ToString("X8"))).ToArray()}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.interaction-graph.actor-order-and-cached-indices",phase,
                    actorExact?"pass":"deferred","blocker",
                    actorExact?"ACTOR_INTERACTION_SLOTS_EXACT":"ACTOR_INTERACTION_SLOT_PROJECTION_REQUIRED",
                    actorExact?"Every actor interaction order and cached bilateral index already matches.":
                        "Per-actor arrays are internally valid but differ from the target order.",
                    new Dictionary<string,object>{{"targetActors",target.Actors.Length},
                        {"liveActors",first.Actors.Length},{"targetSlots",target.ActorSlots.Length},
                        {"liveSlots",first.ActorSlots.Length}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.interaction-graph.sip-semantic-keys",phase,
                    !targetSemantic?"fail":missingKeys.Length==0?"pass":"deferred","blocker",
                    !targetSemantic?"TARGET_SIP_GRAPH_CONTRADICTION":
                        missingKeys.Length==0?"SIP_GRAPH_SEMANTICS_PRESENT":"SIP_GRAPH_RECONSTRUCTION_REQUIRED",
                    !targetSemantic?"Saved manager/SIP owners contradict the saved global graph.":
                        missingKeys.Length==0?"Every target SIP semantic pair is present in the live graph.":
                            "The saved graph proves which semantic SIP registrations must be reconstructed.",
                    new Dictionary<string,object>{{"targetKeys",targetKeys.Cast<object>().ToArray()},
                        {"missingKeys",missingKeys.Cast<object>().ToArray()}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.interaction-graph.pointer-pool-topology",phase,
                    poolsExact?"pass":"deferred","blocker",
                    poolsExact?"POINTER_POOLS_EXACT":"POINTER_POOL_TOPOLOGY_PROJECTION_REQUIRED",
                    poolsExact?"The 8/16/32 actor-array pointer pools already match exactly.":
                        "Exact slab and free-chain order is captured, but live allocator topology differs.",
                    new Dictionary<string,object>{{"targetPoolHash","0x"+a.PoolHash.ToString("X8")},
                        {"livePoolHash","0x"+b.PoolHash.ToString("X8")}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.interaction-registration-order",phase,
                    !repeatable||!layout||!targetSemantic?"fail":exact?"pass":"deferred","blocker",
                    !repeatable||!layout||!targetSemantic?"INTERACTION_GRAPH_ADMISSION_FAILED":
                        exact?"INTERACTION_GRAPH_EXACT":"INTERACTION_GRAPH_PROJECTION_REQUIRED",
                    !repeatable||!layout||!targetSemantic?
                        "The interaction graph failed structural admission before replay.":
                        exact?"The complete active/global/actor/pool interaction graph already matches.":
                            "The full target graph is captured and coherent; atomic projection remains.",
                    new Dictionary<string,object>{{"exact",exact},
                        {"targetGraphHash","0x"+a.GraphHash.ToString("X8")},
                        {"liveGraphHash","0x"+b.GraphHash.ToString("X8")}});
            }
            catch(Exception error)
            {
                foreach(string id in ids)
                    AddReadinessCheck(checks,blockers,deferred,id,phase,"fail","blocker",
                        "INTERACTION_GRAPH_AUDIT_FAILED",error.GetType().Name+": "+error.Message,null);
            }
        }

        private void AppendTransformCacheReadiness(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,CheckpointSidecar selected)
        {
            string[] ids={"rigidbody.transform-cache.repeatability",
                "rigidbody.transform-cache.layout-identity",
                "rigidbody.transform-cache.free-id-order",
                "rigidbody.transform-cache.binding-and-refcounts",
                "rigidbody.transform-cache.active-transforms",
                "rigidbody.transform-cache-id-pool"};
            if(phase!="target-paused"||selected==null)
            {
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-cache-id-pool",phase,
                    "deferred","blocker","TARGET_PHASE_REQUIRED",
                    "Transform-cache live admission requires the restored target-paused state.",null);
                return;
            }
            try
            {
                TransformCacheState target=selected.TransformCache;
                ValidateTransformCacheState(target);
                uint nphase=target.Receipt.NPhaseCore.ToUInt32();
                TransformCacheState first=CaptureTransformCacheState(nphase,selected.Frame);
                TransformCacheState second=CaptureTransformCacheState(nphase,selected.Frame);
                bool repeatable=SameTransformCacheState(first,second);
                NativeTransformCacheReceipt a=target.Receipt,b=first.Receipt;
                bool layout=a.NPhaseCore==b.NPhaseCore&&a.OwnerScene==b.OwnerScene&&
                    a.InteractionScene==b.InteractionScene&&a.Context==b.Context&&
                    a.TransformCache==b.TransformCache&&a.FreeData==b.FreeData&&
                    a.FreeCapacityRaw==b.FreeCapacityRaw&&a.TransformsData==b.TransformsData&&
                    a.TransformsCapacityRaw==b.TransformsCapacityRaw&&
                    a.RefCountsData==b.RefCountsData&&a.RefCountsCapacityRaw==b.RefCountsCapacityRaw;
                bool freeExact=target.FreeIds.SequenceEqual(first.FreeIds)&&
                    a.CurrentId==b.CurrentId;
                bool bindingsExact=SameTransformCacheBindings(target.Bindings,first.Bindings)&&
                    a.TotalRefCount==b.TotalRefCount;
                bool transformsExact=SameActiveTransformCacheEntries(target.Entries,first.Entries);
                // Free-slot pose words are stale forensic bytes and are
                // overwritten before reuse.  Canonical equivalence therefore
                // combines allocator order, live entries, and bindings rather
                // than requiring stale free-slot transforms to match.
                bool exact=freeExact&&bindingsExact&&transformsExact;

                AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-cache.repeatability",phase,
                    repeatable?"pass":"fail","blocker",
                    repeatable?"TRANSFORM_CACHE_CAPTURE_REPEATABLE":"TRANSFORM_CACHE_CAPTURE_CHANGED",
                    repeatable?"Two complete transform-cache captures are raw-bit equivalent.":
                        "Repeated transform-cache capture observed a changing physics phase.",
                    new Dictionary<string,object>{{"firstSnapshotHash","0x"+b.SnapshotHash.ToString("X8")},
                        {"secondSnapshotHash","0x"+second.Receipt.SnapshotHash.ToString("X8")}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-cache.layout-identity",phase,
                    layout?"pass":"fail","blocker",
                    layout?"TRANSFORM_CACHE_LAYOUT_STABLE":"TRANSFORM_CACHE_LAYOUT_CHANGED",
                    layout?"Target and live cache share the exact Scene identities and backing allocations.":
                        "The transform-cache allocation moved or changed capacity, so direct projection is unsafe.",
                    new Dictionary<string,object>{{"target",DescribeTransformCacheState(target)},
                        {"live",DescribeTransformCacheState(first)}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-cache.free-id-order",phase,
                    freeExact?"pass":"deferred","blocker",
                    freeExact?"TRANSFORM_CACHE_FREE_ORDER_EXACT":"TRANSFORM_CACHE_FREE_ORDER_PROJECTION_REQUIRED",
                    freeExact?"The cache-ID watermark and LIFO free-ID order already match.":
                        "The exact target cache-ID watermark and free-ID order are captured but not projected.",
                    new Dictionary<string,object>{{"targetCurrentId",a.CurrentId},{"liveCurrentId",b.CurrentId},
                        {"targetFreeIds",target.FreeIds.Cast<object>().ToArray()},
                        {"liveFreeIds",first.FreeIds.Cast<object>().ToArray()}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.transform-cache.binding-and-refcounts",phase,
                    bindingsExact?"pass":"deferred","blocker",
                    bindingsExact?"TRANSFORM_CACHE_BINDINGS_EXACT":"TRANSFORM_CACHE_BINDING_PROJECTION_REQUIRED",
                    bindingsExact?"Every ordered type-zero graph endpoint already has its exact cache ID and refcount.":
                        "Graph endpoint-to-cache bindings and refcounts are exact in the sidecar but differ live.",
                    new Dictionary<string,object>{{"targetBindings",target.Bindings.Length},
                        {"liveBindings",first.Bindings.Length},{"targetRefs",a.TotalRefCount},
                        {"liveRefs",b.TotalRefCount}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-cache.active-transforms",phase,
                    transformsExact?"pass":"deferred","blocker",
                    transformsExact?"TRANSFORM_CACHE_POSES_EXACT":"TRANSFORM_CACHE_POSE_PROJECTION_REQUIRED",
                    transformsExact?"Every addressable cache entry already has exact raw pose bits, flags, and refcount.":
                        "The exact target transform words are captured but differ in the live cache.",
                    new Dictionary<string,object>{{"targetEntryHash","0x"+a.EntryHash.ToString("X8")},
                        {"liveEntryHash","0x"+b.EntryHash.ToString("X8")},
                        {"targetLiveCount",a.LiveCount},{"liveLiveCount",b.LiveCount},
                        {"comparesLiveEntriesOnly",true}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.transform-cache-id-pool",phase,
                    !repeatable||!layout?"fail":exact?"pass":"deferred","blocker",
                    !repeatable||!layout?"TRANSFORM_CACHE_ADMISSION_FAILED":
                        exact?"TRANSFORM_CACHE_EXACT":"TRANSFORM_CACHE_RECONSTRUCTION_REQUIRED",
                    !repeatable||!layout?"Transform-cache history failed structural admission before replay.":
                        exact?"The canonical transform-cache allocator, live poses, and bindings already match the target.":
                            "The exact cache IDs, free order, bindings, refcounts, and pose words are captured; atomic projection remains.",
                    new Dictionary<string,object>{{"exact",exact},
                        {"targetSnapshotHash","0x"+a.SnapshotHash.ToString("X8")},
                        {"liveSnapshotHash","0x"+b.SnapshotHash.ToString("X8")}});
            }
            catch(Exception error)
            {
                foreach(string id in ids)
                    AddReadinessCheck(checks,blockers,deferred,id,phase,"fail","blocker",
                        "TRANSFORM_CACHE_AUDIT_FAILED",error.GetType().Name+": "+error.Message,null);
            }
        }

        private static void AppendFinishBroadPhaseReadiness(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,CheckpointSidecar selected)
        {
            string[] ids={"rigidbody.broadphase.capture","rigidbody.broadphase.identity",
                "rigidbody.broadphase.created-order","rigidbody.broadphase.deleted-order",
                "rigidbody.broadphase.post-state","rigidbody.broadphase-created-overlap-order"};
            if(selected==null)
            {
                if(phase=="target-paused")foreach(string id in ids)
                    AddReadinessCheck(checks,blockers,deferred,id,phase,"fail","blocker",
                        "BROADPHASE_SIDECAR_MISSING","No exact subsequent finishBroadPhase observation is retained.",null);
                else AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.broadphase-created-overlap-order",phase,"deferred","blocker",
                    "BROADPHASE_SIDECAR_REQUIRED",
                    "The exact subsequent finishBroadPhase observation has not been published.",null);
                return;
            }
            try
            {
                FinishBroadPhaseState state=selected.FinishBroadPhase;
                ValidateFinishBroadPhaseState(state,selected);
                NativeFinishBroadPhaseObserverReceipt receipt=state.Receipt;
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.broadphase.capture",phase,
                    "pass","blocker","BROADPHASE_CAPTURE_EXACT_ORDINAL",
                    "Two copies of the exact committed observation ordinal were byte-equivalent before publication.",
                    new Dictionary<string,object>{{"observationOrdinal",receipt.ObservationOrdinal},
                        {"slotIndex",receipt.SlotIndex},{"droppedObservations",receipt.DroppedObservations}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.broadphase.identity",phase,
                    "pass","blocker","BROADPHASE_IDENTITY_EXACT",
                    "The pass-zero observation belongs to the checkpoint Scene, Context, NPhaseCore, interaction graph, and transform cache.",
                    new Dictionary<string,object>{{"scene",Hex(receipt.ObservedScene)},
                        {"context",Hex(receipt.ObservedContext)},
                        {"nphaseCore",Hex(receipt.ObservedNPhaseCore)},
                        {"pass",receipt.Pass},{"threadId",receipt.ThreadId}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.broadphase.created-order",phase,
                    "pass","blocker","BROADPHASE_CREATED_ORDER_CAPTURED",
                    "The exact oriented created-overlap array is retained in native order.",
                    new Dictionary<string,object>{{"count",state.Created.Length},
                        {"hash","0x"+receipt.CreatedHash.ToString("X8")}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.broadphase.deleted-order",phase,
                    "pass","blocker","BROADPHASE_DELETED_ORDER_CAPTURED",
                    "The exact oriented deleted-overlap array is retained in native order.",
                    new Dictionary<string,object>{{"count",state.Deleted.Length},
                        {"hash","0x"+receipt.DeletedHash.ToString("X8")}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.broadphase.post-state",phase,
                    "pass","blocker","BROADPHASE_PRE_POST_HASHES_CAPTURED",
                    "Entry-time and post-call cache/graph hashes were captured around one original finishBroadPhase call.",
                    new Dictionary<string,object>{{"preCacheHash","0x"+receipt.PreCacheHash.ToString("X8")},
                        {"postCacheHash","0x"+receipt.PostCacheHash.ToString("X8")},
                        {"preGraphHash","0x"+receipt.PreGraphHash.ToString("X8")},
                        {"postGraphHash","0x"+receipt.PostGraphHash.ToString("X8")},
                        {"settledF444HashCompared",false}});
                AddReadinessCheck(checks,blockers,deferred,
                    "rigidbody.broadphase-created-overlap-order",phase,
                    "deferred","blocker","BROADPHASE_REPLAY_STEERING_REQUIRED",
                    "The exact f444-to-f445 overlap event is source-proven, but replay steering/restoration is not implemented.",
                    new Dictionary<string,object>{{"created",state.Created.Length},
                        {"deleted",state.Deleted.Length},{"observationOrdinal",receipt.ObservationOrdinal}});
            }
            catch(Exception error)
            {
                foreach(string id in ids)
                    AddReadinessCheck(checks,blockers,deferred,id,phase,"fail","blocker",
                        "BROADPHASE_AUDIT_FAILED",error.GetType().Name+": "+error.Message,null);
            }
        }

        private void AppendIslandReadiness(List<object> checks,List<object> blockers,
            List<object> deferred,string phase,CheckpointSidecar selected)
        {
            string[] ids={"rigidbody.island.capture-repeatability","rigidbody.island.layout-identity",
                "rigidbody.island.node-topology","rigidbody.island.edge-topology",
                "rigidbody.island.island-topology","rigidbody.island.allocator-free-order",
                "rigidbody.island.node-bitmaps","rigidbody.island.change-queue-order",
                "rigidbody.island.sip-edge-bindings","rigidbody.island.edge-type-scope",
                "rigidbody.island.body-owner-coherence","rigidbody.island.transition-capture",
                "rigidbody.island.transition-identity","rigidbody.island.transition-pre-state",
                "rigidbody.island.transition-post-state","rigidbody.island.edge-journal",
                "rigidbody.island-edge-allocator-and-change-queues"};
            if(selected==null)
            {
                foreach(string id in ids)AddReadinessCheck(checks,blockers,deferred,id,phase,
                    phase=="target-paused"?"fail":"deferred","blocker","ISLAND_SIDECAR_REQUIRED",
                    "The settled island snapshot and first-update transition sidecar are required.",null);
                return;
            }
            try
            {
                IslandSnapshotState target=selected.IslandSnapshot;
                IslandTransitionState transition=selected.IslandTransition;
                ValidateIslandSnapshotState(target,IslandPhaseSettled);
                ValidateIslandSnapshotCoherence(selected,target);
                ValidateIslandTransitionState(selected);
                NativeIslandSnapshotReceipt targetReceipt=target.Receipt;
                bool supportedTypes=targetReceipt.LiveConstraintEdges==0&&
                    targetReceipt.LiveArticulationEdges==0&&
                    transition.Pre.Receipt.LiveConstraintEdges==0&&
                    transition.Pre.Receipt.LiveArticulationEdges==0&&
                    transition.Post.Receipt.LiveConstraintEdges==0&&
                    transition.Post.Receipt.LiveArticulationEdges==0&&
                    transition.Journal.All(value=>value.EdgeType==0);
                uint[] allocatedOwners=target.Nodes.Where(value=>(value.SlotFlags&1u)!=0&&
                    (value.RawFlagsWord&(0x02u|0x04u|0x20u))==0&&value.OwnerRaw!=0)
                    .Select(value=>value.OwnerRaw).ToArray();
                uint[] activeBodies=selected.InteractionGraph.ActiveBodies;
                uint[] ownerLinks=allocatedOwners.Intersect(activeBodies).ToArray();
                bool ownerCoherent=allocatedOwners.Distinct().Count()==allocatedOwners.Length;

                IslandSnapshotState live=null,repeated=null;
                bool repeatable=true,layout=true,nodeExact=true,edgeExact=true,islandExact=true;
                bool freeExact=true,bitmapsExact=true,queuesExact=true,bindingsExact=true,scalarExact=true;
                if(phase=="target-paused")
                {
                    live=CaptureIslandSnapshotState(targetReceipt.NPhaseCore.ToUInt32(),
                        IslandPhaseSettled,selected.Frame);
                    repeated=CaptureIslandSnapshotState(targetReceipt.NPhaseCore.ToUInt32(),
                        IslandPhaseSettled,selected.Frame);
                    repeatable=SameIslandSnapshotState(live,repeated);
                    layout=SameIslandSnapshotLayout(targetReceipt,live.Receipt);
                    nodeExact=SameStructArray(target.Nodes,live.Nodes);
                    edgeExact=SameStructArray(target.Edges,live.Edges);
                    islandExact=SameStructArray(target.Islands,live.Islands)&&
                        SameStructArray(target.Roots,live.Roots);
                    freeExact=SameIslandFreeState(target,live);
                    bitmapsExact=target.KinematicWords.SequenceEqual(live.KinematicWords)&&
                        target.KinematicChangeWords.SequenceEqual(live.KinematicChangeWords)&&
                        target.NotReadyWords.SequenceEqual(live.NotReadyWords)&&
                        target.NotReadyChangeWords.SequenceEqual(live.NotReadyChangeWords)&&
                        target.IslandWords.SequenceEqual(live.IslandWords);
                    queuesExact=target.NodeCreated.SequenceEqual(live.NodeCreated)&&
                        target.NodeDeleted.SequenceEqual(live.NodeDeleted)&&
                        target.EdgeCreated.SequenceEqual(live.EdgeCreated)&&
                        target.EdgeDeleted.SequenceEqual(live.EdgeDeleted)&&
                        target.EdgeBroken.SequenceEqual(live.EdgeBroken)&&
                        target.EdgeJoined.SequenceEqual(live.EdgeJoined);
                    bindingsExact=SameStructArray(target.Bindings,live.Bindings)&&
                        targetReceipt.LiveContactEdges==live.Receipt.LiveContactEdges&&
                        targetReceipt.LiveConstraintEdges==live.Receipt.LiveConstraintEdges&&
                        targetReceipt.LiveArticulationEdges==live.Receipt.LiveArticulationEdges;
                    scalarExact=SameIslandScalarState(targetReceipt,live.Receipt);
                    supportedTypes=supportedTypes&&live.Receipt.LiveConstraintEdges==0&&
                        live.Receipt.LiveArticulationEdges==0;
                }

                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.capture-repeatability",phase,
                    repeatable?"pass":"fail","blocker",
                    repeatable?"ISLAND_CAPTURE_REPEATABLE":"ISLAND_CAPTURE_CHANGED",
                    repeatable?"The snapshot is native-stable and repeated live captures are byte-identical.":
                        "Two consecutive settled island captures differed.",
                    new Dictionary<string,object>{{"targetHash","0x"+targetReceipt.SnapshotHash.ToString("X8")},
                        {"liveHash",live==null?null:"0x"+live.Receipt.SnapshotHash.ToString("X8")},
                        {"repeatedHash",repeated==null?null:"0x"+repeated.Receipt.SnapshotHash.ToString("X8")}});
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.layout-identity",phase,
                    layout,"ISLAND_LAYOUT",target,live);
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.node-topology",phase,
                    nodeExact,"ISLAND_NODE_TOPOLOGY",target,live);
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.edge-topology",phase,
                    edgeExact,"ISLAND_EDGE_TOPOLOGY",target,live);
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.island-topology",phase,
                    islandExact,"ISLAND_LIST_TOPOLOGY",target,live);
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.allocator-free-order",phase,
                    freeExact,"ISLAND_FREE_ORDER",target,live);
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.node-bitmaps",phase,
                    bitmapsExact,"ISLAND_BITMAPS",target,live);
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.change-queue-order",phase,
                    queuesExact,"ISLAND_CHANGE_QUEUES",target,live);
                AddIslandExactReadiness(checks,blockers,deferred,"rigidbody.island.sip-edge-bindings",phase,
                    bindingsExact,"ISLAND_SIP_BINDINGS",target,live);
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.edge-type-scope",phase,
                    supportedTypes?"pass":"fail","blocker",
                    supportedTypes?"CONTACT_EDGE_SCOPE_PROVEN":"UNRESOLVED_ISLAND_EDGE_OWNER_SEMANTICS",
                    supportedTypes?"Every retained edge requiring semantic rebinding is a resolved contact SIP edge.":
                        "Constraint or articulation edge ownership is present; restoration must fail closed until its semantic key is resolved.",
                    new Dictionary<string,object>{{"settledContact",targetReceipt.LiveContactEdges},
                        {"settledConstraint",targetReceipt.LiveConstraintEdges},
                        {"settledArticulation",targetReceipt.LiveArticulationEdges},
                        {"journalConstraintOrArticulation",transition.Journal.Count(value=>value.EdgeType!=0)}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.body-owner-coherence",phase,
                    ownerCoherent?"pass":"fail","blocker",
                    ownerCoherent?"ISLAND_NODE_OWNERS_COHERENT":"DUPLICATE_ISLAND_NODE_OWNER",
                    ownerCoherent?"Allocated non-null island node owners are unique; graph/body owner cross-links are retained as evidence.":
                        "Multiple allocated island nodes claim the same non-null owner.",
                    new Dictionary<string,object>{{"allocatedOwners",allocatedOwners.Length},
                        {"activeGraphBodies",activeBodies.Length},{"crossLinkedOwners",ownerLinks.Length},
                        {"deletedNonArticulatedNodes",target.Nodes.Count(value=>(value.SlotFlags&1u)!=0&&
                            (value.RawFlagsWord&(0x02u|0x04u))==0&&(value.RawFlagsWord&0x20u)!=0)},
                        {"deletedNodesRemainAllocatedUntilQueueConsumption",true}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.transition-capture",phase,
                    "pass","blocker","ISLAND_TRANSITION_DOUBLE_COPY_EXACT",
                    "Two copies of the committed first-update observation were byte-identical before publication.",
                    new Dictionary<string,object>{{"ordinal",transition.Receipt.ObservationOrdinal},
                        {"observerSequence",transition.Receipt.ObserverSequence},{"slotIndex",transition.Receipt.SlotIndex}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.transition-identity",phase,
                    "pass","blocker","ISLAND_TRANSITION_IDENTITY_EXACT",
                    "The update belongs to the same Scene, Context, NPhaseCore, island manager, thread, pass, and ordinal.",
                    new Dictionary<string,object>{{"manager",Hex(transition.Receipt.ObservedManager)},
                        {"threadId",transition.Receipt.ThreadId},{"pass",transition.Receipt.Pass}});
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.transition-pre-state",phase,
                    "pass","blocker","ISLAND_PRE_STATE_CAPTURED",
                    "The complete independently validated pre-update snapshot is retained.",
                    DescribeIslandSnapshotState(transition.Pre));
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.transition-post-state",phase,
                    "pass","blocker","ISLAND_POST_STATE_CAPTURED",
                    "The complete independently re-resolved post-update snapshot is retained.",
                    DescribeIslandSnapshotState(transition.Post));
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island.edge-journal",phase,
                    "pass","blocker","ISLAND_EDGE_JOURNAL_INTERVAL_EXACT",
                    "The arm-to-post half-open add/remove journal interval is contiguous, retained, and byte-repeatable.",
                    new Dictionary<string,object>{{"begin",transition.Receipt.JournalBeginOrdinal},
                        {"end",transition.Receipt.JournalEndOrdinal},{"records",transition.Journal.Length},
                        {"add",transition.Journal.Count(value=>value.EventKind==1)},
                        {"remove",transition.Journal.Count(value=>value.EventKind==2)},
                        {"overflowCount",transition.JournalReceipt.OverflowCount}});
                bool exact=layout&&nodeExact&&edgeExact&&islandExact&&freeExact&&bitmapsExact&&
                    queuesExact&&bindingsExact&&scalarExact;
                AddReadinessCheck(checks,blockers,deferred,"rigidbody.island-edge-allocator-and-change-queues",phase,
                    !repeatable||!supportedTypes||!ownerCoherent?"fail":
                        phase=="target-paused"&&exact?"pass":"deferred","blocker",
                    !repeatable||!supportedTypes||!ownerCoherent?"ISLAND_ADMISSION_FAILED":
                        phase=="target-paused"&&exact?"ISLAND_STATE_EXACT":"ISLAND_PROJECTION_REQUIRED",
                    !repeatable||!supportedTypes||!ownerCoherent?
                        "Island state failed structural or semantic restore admission.":
                        phase=="target-paused"&&exact?
                            "The complete canonical settled island state already matches the target.":
                            "The exact island state and transition are captured; atomic restore mutation is not implemented.",
                    new Dictionary<string,object>{{"exact",exact},{"scalarExact",scalarExact},
                        {"mutationImplemented",false}});
            }
            catch(Exception error)
            {
                foreach(string id in ids)AddReadinessCheck(checks,blockers,deferred,id,phase,
                    "fail","blocker","ISLAND_AUDIT_FAILED",error.GetType().Name+": "+error.Message,null);
            }
        }

        private static void AddIslandExactReadiness(List<object> checks,List<object> blockers,
            List<object> deferred,string id,string phase,bool exact,string code,
            IslandSnapshotState target,IslandSnapshotState live)
        {
            string status=phase!="target-paused"?"pass":exact?"pass":"deferred";
            AddReadinessCheck(checks,blockers,deferred,id,phase,status,"blocker",
                status=="pass"?code+"_CAPTURED":code+"_PROJECTION_REQUIRED",
                status=="pass"?"This ordered island family is structurally captured and exact for the audited phase.":
                    "This ordered island family differs live; its exact target state is captured but mutation is not implemented.",
                new Dictionary<string,object>{{"targetSnapshotHash","0x"+target.Receipt.SnapshotHash.ToString("X8")},
                    {"liveSnapshotHash",live==null?null:"0x"+live.Receipt.SnapshotHash.ToString("X8")}});
        }

        private NativeContactRecreateAuditReceipt CallContactRecreateAudit(
            CheckpointSidecar selected,NativeContactRecreatePlanRow[] rows)
        {
            int receiptSize=Marshal.SizeOf(typeof(NativeContactRecreateAuditReceipt));
            int rowSize=Marshal.SizeOf(typeof(NativeContactRecreatePlanRow));
            if(receiptSize!=232||rowSize!=36)
                throw new InvalidOperationException("Managed contact-recreate audit ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            IntPtr sipBuffer=Marshal.AllocHGlobal(Math.Max(1,
                selected.ShapeInstancePairPool.Order.Length)*IntPtr.Size);
            IntPtr contactBuffer=Marshal.AllocHGlobal(selected.ContactPoolOrder.Length*IntPtr.Size);
            IntPtr largeBuffer=Marshal.AllocHGlobal(Math.Max(1,
                selected.LargeManifoldPool.Order.Length)*IntPtr.Size);
            IntPtr rowBuffer=Marshal.AllocHGlobal(rows.Length*rowSize);
            NativeContactRecreateAuditReceipt receipt;
            int ok;
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                for(int i=0;i<selected.ShapeInstancePairPool.Order.Length;i++)Marshal.WriteInt32(
                    sipBuffer,i*IntPtr.Size,unchecked((int)selected.ShapeInstancePairPool.Order[i]));
                for(int i=0;i<selected.ContactPoolOrder.Length;i++)Marshal.WriteInt32(
                    contactBuffer,i*IntPtr.Size,unchecked((int)selected.ContactPoolOrder[i]));
                for(int i=0;i<selected.LargeManifoldPool.Order.Length;i++)Marshal.WriteInt32(
                    largeBuffer,i*IntPtr.Size,unchecked((int)selected.LargeManifoldPool.Order[i]));
                for(int i=0;i<rows.Length;i++)Marshal.StructureToPtr(rows[i],
                    new IntPtr(rowBuffer.ToInt64()+i*rowSize),false);
                ok=auditContactRecreate(new UIntPtr(unityPlayerBase),new UIntPtr(contactManagerContext),
                    new UIntPtr(selected.ShapeInstancePairPool.NPhaseCore),
                    new UIntPtr(selected.ShapeInstancePairPool.Pool),sipBuffer,
                    (uint)selected.ShapeInstancePairPool.Order.Length,
                    selected.ShapeInstancePairPool.Used,selected.ShapeInstancePairPool.Unreleased,
                    new UIntPtr(selected.FreeArray),contactBuffer,(uint)selected.ContactPoolOrder.Length,
                    new UIntPtr(selected.LargeManifoldPool.Pool),largeBuffer,
                    (uint)selected.LargeManifoldPool.Order.Length,selected.LargeManifoldPool.Used,
                    selected.LargeManifoldPool.Unreleased,rowBuffer,(uint)rows.Length,receiptBuffer);
                receipt=(NativeContactRecreateAuditReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeContactRecreateAuditReceipt));
            }
            finally
            {
                Marshal.FreeHGlobal(rowBuffer);Marshal.FreeHGlobal(largeBuffer);
                Marshal.FreeHGlobal(contactBuffer);Marshal.FreeHGlobal(sipBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            if(ok==0||receipt.ApiVersion!=1||receipt.StructSize!=(uint)receiptSize||
                (receipt.Result!=1&&receipt.Result!=2))
                throw new InvalidOperationException("Native contact-recreate audit failed its receipt contract: result="+
                    receipt.Result+", error="+receipt.LastError+".");
            return receipt;
        }

        private static object DescribeContactRecreateAudit(NativeContactRecreateAuditReceipt value)
        {
            return new Dictionary<string,object>{{"apiVersion",value.ApiVersion},{"result",value.Result},
                {"lastError",value.LastError},{"evaluatedMask","0x"+value.EvaluatedMask.ToString("X8")},
                {"issueMask","0x"+value.IssueMask.ToString("X8")},
                {"recreateState",value.RecreateState},{"observerInstalled",value.ObserverInstalled},
                {"rowCount",value.RowCount},{"invalidRowCount",value.InvalidRowCount},
                {"duplicateRowCount",value.DuplicateRowCount},
                {"targetFreeSipRowCount",value.TargetFreeSipRowCount},
                {"activeSipRowCount",value.ActiveSipRowCount},
                {"targetSipCount",value.TargetSipCount},{"targetSipHash","0x"+value.TargetSipHash.ToString("X8")},
                {"targetSipUsed",value.TargetSipUsed},{"targetSipUnreleased",value.TargetSipUnreleased},
                {"liveSipCount",value.LiveSipCount},{"liveSipHash","0x"+value.LiveSipHash.ToString("X8")},
                {"liveSipUsed",value.LiveSipUsed},{"liveSipUnreleased",value.LiveSipUnreleased},
                {"expectedSemanticSipCount",value.ExpectedSemanticSipCount},
                {"expectedLegacySipCount",value.ExpectedLegacySipCount},
                {"sipMissingCount",value.SipMissingCount},{"sipExtraCount",value.SipExtraCount},
                {"targetContactCount",value.TargetContactCount},{"targetContactHash","0x"+value.TargetContactHash.ToString("X8")},
                {"liveContactCount",value.LiveContactCount},{"liveContactHash","0x"+value.LiveContactHash.ToString("X8")},
                {"expectedContactCount",value.ExpectedContactCount},
                {"contactMissingCount",value.ContactMissingCount},{"contactExtraCount",value.ContactExtraCount},
                {"useBitmapCount",value.UseBitmapCount},{"activeBitmapCount",value.ActiveBitmapCount},
                {"touchBitmapCount",value.TouchBitmapCount},{"modifiableBitmapCount",value.ModifiableBitmapCount},
                {"targetLargeCount",value.TargetLargeCount},{"targetLargeHash","0x"+value.TargetLargeHash.ToString("X8")},
                {"targetLargeUsed",value.TargetLargeUsed},{"targetLargeUnreleased",value.TargetLargeUnreleased},
                {"liveLargeCount",value.LiveLargeCount},{"liveLargeHash","0x"+value.LiveLargeHash.ToString("X8")},
                {"liveLargeUsed",value.LiveLargeUsed},{"liveLargeUnreleased",value.LiveLargeUnreleased},
                {"expectedLargeCount",value.ExpectedLargeCount},{"largeMissingCount",value.LargeMissingCount},
                {"largeExtraCount",value.LargeExtraCount},{"unwritableCount",value.UnwritableCount},
                {"firstResult",value.FirstResult},{"firstError",value.FirstError},
                {"firstRow",value.FirstRow},{"firstDetail",value.FirstDetail}};
        }

        private static object[] HexArray(IEnumerable<uint> values)
        {
            return values.Select(value=>(object)("0x"+value.ToString("X8"))).ToArray();
        }

        private string AuditModuleFingerprint()
        {
            return string.Join("|",new[]{contactPoolCaptures.ToString(),contactPoolRestores.ToString(),
                manifoldPoolCaptures.ToString(),manifoldPoolRestores.ToString(),
                transformDispatchCaptures.ToString(),transformDispatchRestores.ToString(),
                transformCacheCaptures.ToString(),finishBroadPhaseCaptures.ToString(),
                islandSnapshotCaptures.ToString(),islandTransitionCaptures.ToString(),
                dirtyInteractionCaptures.ToString(),dirtyInteractionRestores.ToString(),
                pendingContactPoolAction.ToString(),pendingContactPoolFrame.ToString(),
                scheduledContactPoolCaptureFrame.ToString(),automaticRestorePending.ToString(),
                warpInProgress.ToString(),warpTargetFrame.ToString(),warpTargetRestoreEligible.ToString(),
                contactRecreatePendingValidation.ToString(),dirtyRestorePendingValidation.ToString(),
                pendingFinishBroadPhaseOrdinal.ToString(),
                (pendingIslandObservation==null?0:pendingIslandObservation.ArmedOrdinal).ToString(),
                processRetainedIslandBuffers.Count.ToString(),
                contactPoolReceipts.Count.ToString(),manifoldPoolReceipts.Count.ToString(),
                transformDispatchReceipts.Count.ToString(),dirtyInteractionReceipts.Count.ToString(),
                finishBroadPhaseReceipts.Count.ToString(),
                contactRecreateReceipts.Count.ToString(),checkpointSidecars.Count.ToString(),
                failure??"<null>"});
        }

        private uint ResolveCurrentPxShape(ColliderEndpointState endpoint)
        {
            if(endpoint==null||ReferenceEquals(endpoint.Collider,null)||endpoint.Collider==null||
                endpoint.Collider.GetInstanceID()!=endpoint.InstanceId||
                endpoint.Collider.GetType().FullName!=endpoint.TypeName||
                PathOf(endpoint.Collider.transform)!=endpoint.Path)
                throw new InvalidOperationException("A checkpoint contact endpoint is no longer the exact live Collider incarnation.");
            IntPtr native=(IntPtr)cachedPtr.GetValue(endpoint.Collider);
            uint shape=native==IntPtr.Zero?0u:unchecked((uint)Marshal.ReadInt32(native,0x24));
            if(shape==0)throw new InvalidOperationException("A checkpoint contact endpoint has no live PxShape.");
            return shape;
        }

        private bool ValidatePendingContactRecreate(string action,int observedFrame,
            bool allowCleanIntermediate)
        {
            if(!contactRecreatePendingValidation)return true;
            CheckpointSidecar selected=pendingContactRecreateSidecar;
            if(selected==null)throw new InvalidOperationException("Pending contact recreation lost its checkpoint sidecar.");
            try
            {
                NativeContactRecreateReceipt receipt=CallContactRecreateStatus(action,
                    observedFrame<0?selected.Frame:observedFrame);
                uint expectedRows=(uint)selected.ContactManagerOwners.Records.Length;
                bool cleanIntermediate=receipt.Result==1&&receipt.State==1&&receipt.Armed==1&&
                    receipt.RowCount==expectedRows&&receipt.MatchedCount<receipt.RowCount&&
                    receipt.MatchedCount+receipt.RemainingCount==receipt.RowCount&&
                    receipt.SipMatchedCount<receipt.RowCount&&
                    receipt.SipMatchedCount+receipt.SipRemainingCount==receipt.RowCount&&
                    receipt.InvalidRow==uint.MaxValue&&receipt.Detail==0;
                // Contact managers are created by more than one narrowphase
                // worker and need not all exist after the first replayed step.
                // Keep an authenticated, non-poisoned plan armed while exact
                // progress is still possible.  Completion below remains fully
                // fail-closed, and the next authoring pause admits no partial.
                if(allowCleanIntermediate&&cleanIntermediate)return false;
                if(receipt.Result!=1||receipt.State!=2||receipt.Armed!=0||
                    receipt.MatchedCount!=receipt.RowCount||receipt.RemainingCount!=0||
                    receipt.SipMatchedCount!=receipt.RowCount||receipt.SipRemainingCount!=0||
                    receipt.SipMatchedMask!=receipt.MatchedMask||
                    receipt.RowCount!=expectedRows||
                    receipt.SipCountCurrent!=(uint)selected.ShapeInstancePairPool.Order.Length||
                    receipt.SipHashCurrent!=selected.ShapeInstancePairPool.OrderHash||
                    receipt.SipUsedCurrent!=selected.ShapeInstancePairPool.Used||
                    receipt.SipUnreleasedCurrent!=selected.ShapeInstancePairPool.Unreleased||
                    receipt.ContactCountCurrent!=(uint)selected.ContactPoolOrder.Length||
                    receipt.ContactHashCurrent!=selected.OrderHash||
                    receipt.LargeCountCurrent!=(uint)selected.LargeManifoldPool.Order.Length||
                    receipt.LargeHashCurrent!=selected.LargeManifoldPool.OrderHash||
                    receipt.LargeUsedCurrent!=selected.LargeManifoldPool.Used||
                    receipt.LargeUnreleasedCurrent!=selected.LargeManifoldPool.Unreleased)
                    throw new InvalidOperationException("Native contact-manager recreation did not converge at the first advancing output boundary: result="+
                        receipt.Result+", state="+receipt.State+", matched="+receipt.MatchedCount+
                        "/"+receipt.RowCount+", invalidRow="+receipt.InvalidRow+", detail="+receipt.Detail+".");

                LiveContactPoolState contact=CaptureLiveContactPoolState();
                if(contact.Receipt.FreeArray.ToUInt32()!=selected.FreeArray||
                    contact.Receipt.OrderHashBefore!=selected.OrderHash||
                    !contact.Order.SequenceEqual(selected.ContactPoolOrder))
                    throw new InvalidOperationException("Recreated contact-manager free order differs from the checkpoint.");
                ManifoldPoolState large=RunManifoldPoolAction(1,LargeManifoldPoolKind,null,selected.Frame);
                ManifoldPoolState sphere=RunManifoldPoolAction(1,SphereManifoldPoolKind,null,selected.Frame);
                SipPoolState sip=CaptureSipPoolState(
                    selected.ShapeInstancePairPool.NPhaseCore,selected.Frame);
                if(!SameManifoldPoolSnapshot(large,selected.LargeManifoldPool)||
                    !SameManifoldPoolSnapshot(sphere,selected.SphereManifoldPool)||
                    !SameSipPoolSnapshot(sip,selected.ShapeInstancePairPool))
                    throw new InvalidOperationException("Recreated native pool state differs from the checkpoint.");
                ContactManagerOwnerState current=CaptureContactManagerOwners(selected.Frame);
                ValidateRecreatedOwnerMapping(selected.ContactManagerOwners,current);
                CallContactRecreateCancel("complete",selected.Frame);
                contactRecreatePendingValidation=false;
                pendingContactRecreateSidecar=null;
                return true;
            }
            catch
            {
                try{CancelCheckpointObservationWork();}catch{}
                try{CancelContactRecreateWork();}catch{}
                throw;
            }
        }

        private void ValidateRecreatedOwnerMapping(ContactManagerOwnerState target,
            ContactManagerOwnerState current)
        {
            NativeContactManagerOwnerReceipt a=target.Receipt,b=current.Receipt;
            if(a.Pool!=b.Pool||a.FreeArray!=b.FreeArray||a.Slabs!=b.Slabs||
                a.FreeCount!=b.FreeCount||a.UsedCount!=b.UsedCount||a.ActiveCount!=b.ActiveCount||
                a.TouchCount!=b.TouchCount||a.ModifiableCount!=b.ModifiableCount||
                a.FreeOrderHash!=b.FreeOrderHash||a.UseBitmapHash!=b.UseBitmapHash||
                a.ActiveBitmapHash!=b.ActiveBitmapHash||a.TouchBitmapHash!=b.TouchBitmapHash||
                a.ModifiableBitmapHash!=b.ModifiableBitmapHash||target.Records.Length!=current.Records.Length)
                throw new InvalidOperationException("Recreated contact-manager pool membership differs from the checkpoint.");
            var currentByManager=current.Records.ToDictionary(value=>value.Manager.ToUInt32());
            for(int i=0;i<target.Records.Length;i++)
            {
                NativeContactManagerOwnerRecord expected=target.Records[i],actual;
                if(!currentByManager.TryGetValue(expected.Manager.ToUInt32(),out actual))
                    throw new InvalidOperationException("Checkpoint contact manager "+Hex(expected.Manager)+" was not recreated.");
                uint shape0=ResolveCurrentPxShape(target.Endpoints0[i]);
                uint shape1=ResolveCurrentPxShape(target.Endpoints1[i]);
                uint low=Math.Min(shape0,shape1),high=Math.Max(shape0,shape1);
                uint actualLow=Math.Min(actual.PxShape0.ToUInt32(),actual.PxShape1.ToUInt32());
                uint actualHigh=Math.Max(actual.PxShape0.ToUInt32(),actual.PxShape1.ToUInt32());
                if(actual.Slot!=expected.Slot||actual.Membership!=expected.Membership||
                    actual.Manifold!=expected.Manifold||actual.Sip!=expected.Sip||
                    actual.PrimaryVtable!=expected.PrimaryVtable||actual.SecondaryVtable!=expected.SecondaryVtable||
                    actual.ShapeSim0!=expected.ShapeSim0||actual.ShapeSim1!=expected.ShapeSim1||
                    actual.PxsShapeCore0!=expected.PxsShapeCore0||actual.PxsShapeCore1!=expected.PxsShapeCore1||
                    actual.RigidBody0!=expected.RigidBody0||actual.RigidBody1!=expected.RigidBody1||
                    actual.RigidCore0!=expected.RigidCore0||actual.RigidCore1!=expected.RigidCore1||
                    actual.ActorPair!=expected.ActorPair||actualLow!=low||actualHigh!=high||
                    actual.ManagerFlags!=expected.ManagerFlags||actual.ContactCount!=expected.ContactCount||
                    actual.WorkUnitFlags!=expected.WorkUnitFlags||actual.StatusFlags!=expected.StatusFlags||
                    actual.GeomType0+actual.GeomType1!=expected.GeomType0+expected.GeomType1||
                    actual.DisableResponse!=expected.DisableResponse||actual.DisableCcd!=expected.DisableCcd||
                    actual.CacheSize!=expected.CacheSize||actual.ManagerHash!=expected.ManagerHash||
                    actual.SipHash!=expected.SipHash||actual.ManifoldHash!=expected.ManifoldHash||
                    actual.CacheHash!=expected.CacheHash)
                    throw new InvalidOperationException("Recreated contact owner differs for manager "+Hex(expected.Manager)+".");
            }
        }

        private NativeContactRecreateReceipt CallContactRecreateStatus(string action,int frame)
        {
            int size=Marshal.SizeOf(typeof(NativeContactRecreateReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);NativeContactRecreateReceipt receipt;
            int ok;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                ok=statusContactRecreate(new UIntPtr(unityPlayerBase),new UIntPtr(contactManagerContext),buffer);
                receipt=(NativeContactRecreateReceipt)Marshal.PtrToStructure(buffer,typeof(NativeContactRecreateReceipt));
            }
            finally{Marshal.FreeHGlobal(buffer);}
            RecordContactRecreateReceipt(action,frame,receipt);
            if(ok==0||receipt.ApiVersion!=2||receipt.StructSize!=(uint)size)
                throw new InvalidOperationException("Native contact-recreate status failed: result="+receipt.Result+
                    ", error="+receipt.LastError+", state="+receipt.State+".");
            return receipt;
        }

        private void CallContactRecreateCancel(string action,int frame)
        {
            if(cancelContactRecreate==null)return;
            int size=Marshal.SizeOf(typeof(NativeContactRecreateReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);NativeContactRecreateReceipt receipt;
            int ok;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                ok=cancelContactRecreate(new UIntPtr(unityPlayerBase),buffer);
                receipt=(NativeContactRecreateReceipt)Marshal.PtrToStructure(buffer,typeof(NativeContactRecreateReceipt));
            }
            finally{Marshal.FreeHGlobal(buffer);}
            RecordContactRecreateReceipt(action,frame,receipt);
            if(ok==0||receipt.Result!=1||receipt.ApiVersion!=2||receipt.StructSize!=(uint)size)
                throw new InvalidOperationException("Native contact-recreate cancel failed: result="+
                    receipt.Result+", error="+receipt.LastError+".");
        }

        private void CancelContactRecreateWork()
        {
            if((contactRecreatePendingValidation||pendingContactRecreateSidecar!=null)&&
                cancelContactRecreate!=null)
                CallContactRecreateCancel("cancel",pendingContactRecreateSidecar==null?-1:
                    pendingContactRecreateSidecar.Frame);
            contactRecreatePendingValidation=false;
            pendingContactRecreateSidecar=null;
        }

        private void RecordContactRecreateReceipt(string action,int frame,
            NativeContactRecreateReceipt receipt)
        {
            contactRecreateReceipts.Add(new Dictionary<string,object>{{"action",action},{"frame",frame},
                {"result",receipt.Result},{"lastError",receipt.LastError},{"state",receipt.State},
                {"rowCount",receipt.RowCount},{"matchedCount",receipt.MatchedCount},
                {"remainingCount",receipt.RemainingCount},{"invalidRow",receipt.InvalidRow},{"detail",receipt.Detail},
                {"matchedMask","0x"+receipt.MatchedMask.ToString("X8")},{"threadId",receipt.ThreadId},
                {"contactCountCurrent",receipt.ContactCountCurrent},{"contactHashCurrent","0x"+receipt.ContactHashCurrent.ToString("X8")},
                {"largeCountCurrent",receipt.LargeCountCurrent},{"largeHashCurrent","0x"+receipt.LargeHashCurrent.ToString("X8")},
                {"largeUsedCurrent",receipt.LargeUsedCurrent},{"largeUnreleasedCurrent",receipt.LargeUnreleasedCurrent},
                {"lastSip",Hex(receipt.LastSip)},{"lastShapeLow",Hex(receipt.LastShapeLow)},
                {"lastShapeHigh",Hex(receipt.LastShapeHigh)},{"lastManager",Hex(receipt.LastManager)},
                {"lastManifold",Hex(receipt.LastManifold)},{"observerEntries",receipt.ObserverEntries},
                {"attemptOrdinal",receipt.AttemptOrdinal},{"attemptSip",Hex(receipt.AttemptSip)},
                {"attemptShapeLow",Hex(receipt.AttemptShapeLow)},{"attemptShapeHigh",Hex(receipt.AttemptShapeHigh)},
                {"attemptRow",receipt.AttemptRow},{"attemptMatchedMask","0x"+receipt.AttemptMatchedMask.ToString("X8")},
                {"attemptFreeCount",receipt.AttemptFreeCount},{"attemptManagerIndex",receipt.AttemptManagerIndex},
                {"attemptLargeHead",Hex(receipt.AttemptLargeHead)},
                {"attemptManifoldIndex",receipt.AttemptManifoldIndex},
                {"nphaseCore",Hex(receipt.NPhaseCore)},{"sipPool",Hex(receipt.SipPool)},
                {"sipMatchedCount",receipt.SipMatchedCount},{"sipRemainingCount",receipt.SipRemainingCount},
                {"targetSipCount",receipt.TargetSipCount},{"targetSipHash","0x"+receipt.TargetSipHash.ToString("X8")},
                {"sipCountCurrent",receipt.SipCountCurrent},{"sipHashCurrent","0x"+receipt.SipHashCurrent.ToString("X8")},
                {"sipUsedCurrent",receipt.SipUsedCurrent},{"sipUnreleasedCurrent",receipt.SipUnreleasedCurrent},
                {"sipMatchedMask","0x"+receipt.SipMatchedMask.ToString("X8")},
                {"sipObserverEntries",receipt.SipObserverEntries},{"lastAllocatedSip",Hex(receipt.LastAllocatedSip)}});
            if(contactRecreateReceipts.Count>24)contactRecreateReceipts.RemoveAt(0);
        }

        private ContactManagerOwnerState CaptureContactManagerOwners(int frame)
        {
            int receiptSize=Marshal.SizeOf(typeof(NativeContactManagerOwnerReceipt));
            int recordSize=Marshal.SizeOf(typeof(NativeContactManagerOwnerRecord));
            if(recordSize!=172||receiptSize!=156)
                throw new InvalidOperationException("Managed contact-manager owner ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            IntPtr recordBuffer=Marshal.AllocHGlobal(MaximumContactManagers*recordSize);
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureContactManagerActiveOwners(new UIntPtr(unityPlayerBase),
                    new UIntPtr(contactManagerContext),recordBuffer,MaximumContactManagers,receiptBuffer);
                var receipt=(NativeContactManagerOwnerReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeContactManagerOwnerReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native contact-manager owner capture failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+", invalidSlot="+
                        receipt.InvalidSlot+", detail="+receipt.Detail+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)receiptSize||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.Context.ToUInt32()!=contactManagerContext||receipt.TotalSlots<1||
                    receipt.TotalSlots>MaximumContactManagers||receipt.RecordsRequired!=receipt.UsedCount||
                    receipt.RecordsWritten!=receipt.RecordsRequired||
                    receipt.FreeCount+receipt.UsedCount!=receipt.TotalSlots||
                    receipt.ActiveCount!=receipt.UsedCount||receipt.ConsistencyFlags!=0xFFu)
                    throw new InvalidOperationException("Native contact-manager owner receipt contract differs at frame "+frame+".");
                var records=new NativeContactManagerOwnerRecord[receipt.RecordsWritten];
                for(int i=0;i<records.Length;i++)records[i]=(NativeContactManagerOwnerRecord)
                    Marshal.PtrToStructure(new IntPtr(recordBuffer.ToInt64()+i*recordSize),
                        typeof(NativeContactManagerOwnerRecord));
                if(records.Any(value=>value.Slot>=receipt.TotalSlots||
                        (value.Membership&3u)!=3u||value.Manager==UIntPtr.Zero||
                        value.Sip==UIntPtr.Zero||value.ValidationFlags!=0xFFu)||
                    records.Select(value=>value.Slot).Distinct().Count()!=records.Length)
                    throw new InvalidOperationException("Native contact-manager owner records failed the managed set contract.");
                Dictionary<uint,ColliderEndpointState> colliders=CurrentColliderShapeOwners();
                ColliderEndpointState[] endpoints0=records.Select(value=>ColliderOwner(colliders,value.PxShape0)).ToArray();
                ColliderEndpointState[] endpoints1=records.Select(value=>ColliderOwner(colliders,value.PxShape1)).ToArray();
                if(endpoints0.Any(value=>value==null)||endpoints1.Any(value=>value==null))
                    throw new InvalidOperationException("An active contact manager endpoint has no unique live Collider owner.");
                string[] owners0=endpoints0.Select(value=>value.Identity).ToArray();
                string[] owners1=endpoints1.Select(value=>value.Identity).ToArray();
                byte[][] managerBytes=records.Select(value=>
                    ReadBytes(value.Manager.ToUInt32(),0x80)).ToArray();
                byte[][] sipBytes=records.Select(value=>
                    ReadBytes(value.Sip.ToUInt32(),0x44)).ToArray();
                byte[][] actorPairBytes=records.Select(value=>
                    ReadBytes(value.ActorPair.ToUInt32(),0x18)).ToArray();
                byte[][] manifoldBytes=records.Select(value=>value.ManifoldBytes==0?
                    new byte[0]:ReadBytes(value.Manifold.ToUInt32(),
                        checked((int)value.ManifoldBytes))).ToArray();
                byte[][] cacheBytes=records.Select(value=>value.CacheSize==0?
                    new byte[0]:ReadBytes(value.CachePointer.ToUInt32(),value.CacheSize)).ToArray();
                return new ContactManagerOwnerState {Receipt=receipt,Records=records,
                    ShapeOwners0=owners0,ShapeOwners1=owners1,
                    Endpoints0=endpoints0,Endpoints1=endpoints1,
                    ManagerBytes=managerBytes,SipBytes=sipBytes,
                    ActorPairBytes=actorPairBytes,ManifoldBytes=manifoldBytes,
                    CacheBytes=cacheBytes};
            }
            finally
            {
                Marshal.FreeHGlobal(recordBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
        }

        private Dictionary<uint,ColliderEndpointState> CurrentColliderShapeOwners()
        {
            var result=new Dictionary<uint,ColliderEndpointState>();
            foreach(Collider collider in UnityEngine.Object.FindObjectsOfType(typeof(Collider)))
            {
                if(collider==null)continue;
                IntPtr native=(IntPtr)cachedPtr.GetValue(collider);
                if(native==IntPtr.Zero)continue;
                uint shape=unchecked((uint)Marshal.ReadInt32(native,0x24));
                if(shape==0)continue;
                string path=PathOf(collider.transform);
                string typeName=collider.GetType().FullName;
                string owner=path+"|"+collider.GetType().Name+"#"+collider.GetInstanceID();
                ColliderEndpointState previous;
                if(result.TryGetValue(shape,out previous)&&previous.Identity!=owner)
                    throw new InvalidOperationException("Two live Colliders own PxShape 0x"+
                        shape.ToString("X8")+": "+previous.Identity+" and "+owner+".");
                result[shape]=new ColliderEndpointState {Collider=collider,
                    InstanceId=collider.GetInstanceID(),Path=path,TypeName=typeName,
                    Identity=owner,PxShape=shape};
            }
            return result;
        }

        private static ColliderEndpointState ColliderOwner(
            Dictionary<uint,ColliderEndpointState> owners,UIntPtr shape)
        {
            ColliderEndpointState result;
            return owners.TryGetValue(shape.ToUInt32(),out result)?result:null;
        }

        private static object DescribeContactManagerOwnerState(ContactManagerOwnerState value,bool includeRecords)
        {
            if(value==null)return null;
            var receipt=value.Receipt;
            var result=new Dictionary<string,object>{{"pool",Hex(receipt.Pool)},
                {"freeArray",Hex(receipt.FreeArray)},{"slabs",Hex(receipt.Slabs)},
                {"elementsPerSlab",receipt.ElementsPerSlab},{"slabCount",receipt.SlabCount},
                {"totalSlots",receipt.TotalSlots},{"freeCount",receipt.FreeCount},
                {"usedCount",receipt.UsedCount},{"activeCount",receipt.ActiveCount},
                {"touchCount",receipt.TouchCount},{"modifiableCount",receipt.ModifiableCount},
                {"freeOrderHash","0x"+receipt.FreeOrderHash.ToString("X8")},
                {"freeIndexOrderHash","0x"+receipt.FreeIndexOrderHash.ToString("X8")},
                {"useBitmapHash","0x"+receipt.UseBitmapHash.ToString("X8")},
                {"activeBitmapHash","0x"+receipt.ActiveBitmapHash.ToString("X8")},
                {"touchBitmapHash","0x"+receipt.TouchBitmapHash.ToString("X8")},
                {"modifiableBitmapHash","0x"+receipt.ModifiableBitmapHash.ToString("X8")},
                {"ownerHash","0x"+receipt.OwnerHash.ToString("X8")},
                {"consistencyFlags","0x"+receipt.ConsistencyFlags.ToString("X8")}};
            if(includeRecords)
            {
                var rows=new object[value.Records.Length];
                for(int i=0;i<rows.Length;i++)rows[i]=DescribeContactManagerOwnerRecord(
                    value.Records[i],value.ShapeOwners0[i],value.ShapeOwners1[i]);
                result.Add("records",rows);
                result.Add("managerContentSha256",value.ManagerBytes.Select(ContentSha256).ToArray());
                result.Add("sipContentSha256",value.SipBytes.Select(ContentSha256).ToArray());
                result.Add("actorPairContentSha256",value.ActorPairBytes.Select(ContentSha256).ToArray());
                result.Add("manifoldContentSha256",value.ManifoldBytes.Select(ContentSha256).ToArray());
                result.Add("cacheContentSha256",value.CacheBytes.Select(ContentSha256).ToArray());
            }
            return result;
        }

        private static string ContentSha256(byte[] bytes)
        {
            using(var algorithm=SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-","");
        }

        private static object DescribeContactManagerOwnerRecord(
            NativeContactManagerOwnerRecord value,string owner0,string owner1)
        {
            return new Dictionary<string,object>{{"slot",value.Slot},{"membership","0x"+value.Membership.ToString("X8")},
                {"manager",Hex(value.Manager)},{"sip",Hex(value.Sip)},
                {"shapeSim0",Hex(value.ShapeSim0)},{"shapeSim1",Hex(value.ShapeSim1)},
                {"pxShape0",Hex(value.PxShape0)},{"pxShape1",Hex(value.PxShape1)},
                {"shapeOwner0",owner0},{"shapeOwner1",owner1},
                {"rigidBody0",Hex(value.RigidBody0)},{"rigidBody1",Hex(value.RigidBody1)},
                {"rigidCore0",Hex(value.RigidCore0)},{"rigidCore1",Hex(value.RigidCore1)},
                {"actorPair",Hex(value.ActorPair)},{"managerFlags","0x"+value.ManagerFlags.ToString("X8")},
                {"actorPairActor0",Hex(value.ActorPairActor0)},
                {"actorPairActor1",Hex(value.ActorPairActor1)},
                {"actorPairScene",Hex(value.ActorPairScene)},
                {"actorPairInternalFlags","0x"+value.ActorPairInternalFlags.ToString("X4")},
                {"actorPairTouchCount",value.ActorPairTouchCount},
                {"actorPairRefCount",value.ActorPairRefCount},
                {"actorPairReportData",Hex(value.ActorPairReportData)},
                {"actorPairHash","0x"+value.ActorPairHash.ToString("X8")},
                {"sipFlags","0x"+value.SipFlags.ToString("X8")},
                {"contactReportStamp",value.ContactReportStamp},
                {"reportPairIndex","0x"+value.ReportPairIndex.ToString("X8")},
                {"reportStreamIndex","0x"+value.ReportStreamIndex.ToString("X4")},
                {"interactionType",value.InteractionType},{"interactionFlags","0x"+value.InteractionFlags.ToString("X2")},
                {"geomType0",value.GeomType0},{"geomType1",value.GeomType1},
                {"transformCache0",value.TransformCache0},{"transformCache1",value.TransformCache1},
                {"contactCount",value.ContactCount},{"workUnitFlags","0x"+value.WorkUnitFlags.ToString("X4")},
                {"statusFlags","0x"+value.StatusFlags.ToString("X4")},
                {"manifold",Hex(value.Manifold)},{"manifoldBytes",value.ManifoldBytes},
                {"cachePointer",Hex(value.CachePointer)},{"cacheSize",value.CacheSize},
                {"pairData","0x"+value.PairData.ToString("X8")},
                {"managerHash","0x"+value.ManagerHash.ToString("X8")},
                {"sipHash","0x"+value.SipHash.ToString("X8")},
                {"manifoldHash","0x"+value.ManifoldHash.ToString("X8")},
                {"cacheHash","0x"+value.CacheHash.ToString("X8")}};
        }

        private void RecordContactPoolReceipt(int action,int frame,NativeContactPoolReceipt receipt)
        {
            object[] top=(receipt.Top??new UIntPtr[0]).Select(Hex).Cast<object>().ToArray();
            var value=new Dictionary<string,object>{{"action",action==1?"capture":"restore"},
                {"apiVersion",receipt.ApiVersion},{"structSize",receipt.StructSize},
                {"result",receipt.Result},{"lastError",receipt.LastError},
                {"context",Hex(receipt.Context)},{"freeArray",Hex(receipt.FreeArray)},
                {"frame",frame},{"freeCount",receipt.FreeCount},
                {"orderHashBefore","0x"+receipt.OrderHashBefore.ToString("X8")},
                {"orderHashAfter","0x"+receipt.OrderHashAfter.ToString("X8")},{"top",top}};
            contactPoolReceipts.Add(value);if(contactPoolReceipts.Count>16)contactPoolReceipts.RemoveAt(0);
        }

        private ManifoldPoolState CaptureManifoldPoolStateReadOnly(uint poolKind,int frame)
        {
            string poolName=poolKind==LargeManifoldPoolKind?"large":
                poolKind==SphereManifoldPoolKind?"sphere":null;
            if(poolName==null)throw new InvalidOperationException("Unsupported manifold-pool kind "+poolKind+".");
            int size=Marshal.SizeOf(typeof(NativeManifoldPoolReceipt));
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr snapshotBuffer=Marshal.AllocHGlobal(MaximumManifolds*IntPtr.Size);
            NativeManifoldPoolReceipt receipt;
            uint[] order;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureManifoldPoolSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(contactManagerContext),poolKind,snapshotBuffer,
                    MaximumManifolds,receiptBuffer);
                receipt=(NativeManifoldPoolReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeManifoldPoolReceipt));
                if(ok==0||receipt.Result!=1||receipt.TraversedCount>MaximumManifolds)
                    throw new InvalidOperationException("Read-only "+poolName+
                        " manifold-pool audit failed at frame "+frame+": result="+
                        receipt.Result+", Win32/error="+receipt.LastError+".");
                order=new uint[receipt.TraversedCount];
                for(int i=0;i<order.Length;i++)order[i]=unchecked((uint)Marshal.ReadInt32(
                    snapshotBuffer,i*IntPtr.Size));
            }
            finally
            {
                Marshal.FreeHGlobal(snapshotBuffer);Marshal.FreeHGlobal(receiptBuffer);
            }
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||
                receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                receipt.Context.ToUInt32()!=contactManagerContext||receipt.PoolKind!=poolKind||
                receipt.Pool==UIntPtr.Zero||receipt.FreeHeadBefore.ToUInt32()!=
                    (order.Length==0?0u:order[0])||receipt.FreeHeadAfter!=receipt.FreeHeadBefore||
                receipt.OrderHashBefore!=ContactPoolOrderHash(order)||
                receipt.OrderHashAfter!=receipt.OrderHashBefore)
                throw new InvalidOperationException("Read-only "+poolName+
                    " manifold-pool audit receipt differs.");
            var state=new ManifoldPoolState {PoolKind=poolKind,Pool=receipt.Pool.ToUInt32(),
                OrderHash=receipt.OrderHashBefore,Order=order,ElementSize=receipt.ElementSize,
                ElementsPerSlab=receipt.ElementsPerSlab,Used=receipt.Used,
                Unreleased=receipt.Unreleased,SlabSize=receipt.SlabSize};
            ValidateManifoldPoolState(state,poolKind,poolName);
            return state;
        }

        private ManifoldPoolState RunManifoldPoolAction(int action,uint poolKind,ManifoldPoolState snapshot,int frame)
        {
            string poolName=poolKind==LargeManifoldPoolKind?"large":
                poolKind==SphereManifoldPoolKind?"sphere":null;
            if(poolName==null)throw new InvalidOperationException("Unsupported manifold-pool kind "+poolKind+".");
            if(action==2)ValidateManifoldPoolState(snapshot,poolKind,poolName);
            int size=Marshal.SizeOf(typeof(NativeManifoldPoolReceipt));
            int count=action==1?MaximumManifolds:snapshot.Order.Length;
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr snapshotBuffer=Marshal.AllocHGlobal(Math.Max(1,count)*IntPtr.Size);
            NativeManifoldPoolReceipt receipt=new NativeManifoldPoolReceipt();
            uint[] capturedOrder=null;
            int ok=0;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                if(action==2)
                    for(int i=0;i<count;i++)Marshal.WriteInt32(snapshotBuffer,i*IntPtr.Size,
                        unchecked((int)snapshot.Order[i]));
                ok=action==1
                    ?captureManifoldPoolSnapshot(new UIntPtr(unityPlayerBase),new UIntPtr(contactManagerContext),
                        poolKind,snapshotBuffer,(uint)count,receiptBuffer)
                    :restoreManifoldPoolSnapshot(new UIntPtr(unityPlayerBase),new UIntPtr(contactManagerContext),
                        poolKind,new UIntPtr(snapshot.Pool),snapshotBuffer,(uint)count,receiptBuffer);
                receipt=(NativeManifoldPoolReceipt)Marshal.PtrToStructure(receiptBuffer,typeof(NativeManifoldPoolReceipt));
                if(action==1&&ok!=0&&receipt.Result==1)
                {
                    if(receipt.TraversedCount>MaximumManifolds)
                        throw new InvalidOperationException("Native "+poolName+" manifold-pool capture returned an invalid count.");
                    capturedOrder=new uint[receipt.TraversedCount];
                    for(int i=0;i<capturedOrder.Length;i++)capturedOrder[i]=
                        unchecked((uint)Marshal.ReadInt32(snapshotBuffer,i*IntPtr.Size));
                }
            }
            finally{Marshal.FreeHGlobal(snapshotBuffer);Marshal.FreeHGlobal(receiptBuffer);}

            RecordManifoldPoolReceipt(action,frame,poolName,receipt);
            if(ok==0||receipt.Result!=1)
                throw new InvalidOperationException("Native "+poolName+" manifold-pool action failed: result="+
                    receipt.Result+", Win32/error="+receipt.LastError+".");
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||
                receipt.UnityBase.ToUInt32()!=unityPlayerBase||receipt.Context.ToUInt32()!=contactManagerContext||
                receipt.PoolKind!=poolKind||receipt.Pool==UIntPtr.Zero)
                throw new InvalidOperationException("Native "+poolName+" manifold-pool receipt contract differs.");

            if(action==1)
            {
                uint orderHash=ContactPoolOrderHash(capturedOrder);
                uint freeHead=capturedOrder.Length==0?0u:capturedOrder[0];
                if(receipt.FreeHeadBefore.ToUInt32()!=freeHead||receipt.FreeHeadAfter.ToUInt32()!=freeHead||
                    orderHash!=receipt.OrderHashBefore||
                    receipt.OrderHashAfter!=receipt.OrderHashBefore)
                    throw new InvalidOperationException("Managed "+poolName+" manifold-pool snapshot differs from the native capture receipt.");
                manifoldPoolCaptures++;
                return new ManifoldPoolState {PoolKind=poolKind,Pool=receipt.Pool.ToUInt32(),
                    OrderHash=orderHash,Order=capturedOrder,ElementSize=receipt.ElementSize,
                    ElementsPerSlab=receipt.ElementsPerSlab,Used=receipt.Used,
                    Unreleased=receipt.Unreleased,SlabSize=receipt.SlabSize};
            }

            uint expectedHead=snapshot.Order.Length==0?0u:snapshot.Order[0];
            if(receipt.Pool.ToUInt32()!=snapshot.Pool||receipt.FreeHeadAfter.ToUInt32()!=expectedHead||
                receipt.TraversedCount!=(uint)snapshot.Order.Length||receipt.OrderHashAfter!=snapshot.OrderHash)
                throw new InvalidOperationException("Native "+poolName+" manifold-pool restore receipt differs from the selected checkpoint sidecar.");
            manifoldPoolRestores++;
            return snapshot;
        }

        private void RecordManifoldPoolReceipt(int action,int frame,string poolName,NativeManifoldPoolReceipt receipt)
        {
            object[] topBefore=(receipt.TopBefore??new UIntPtr[0]).Select(Hex).Cast<object>().ToArray();
            object[] topAfter=(receipt.TopAfter??new UIntPtr[0]).Select(Hex).Cast<object>().ToArray();
            var value=new Dictionary<string,object>{{"action",action==1?"capture":"restore"},
                {"poolKind",receipt.PoolKind},{"poolName",poolName},{"frame",frame},
                {"apiVersion",receipt.ApiVersion},{"structSize",receipt.StructSize},
                {"result",receipt.Result},{"lastError",receipt.LastError},
                {"unityBase",Hex(receipt.UnityBase)},{"context",Hex(receipt.Context)},
                {"pool",Hex(receipt.Pool)},{"freeHeadBefore",Hex(receipt.FreeHeadBefore)},
                {"freeHeadAfter",Hex(receipt.FreeHeadAfter)},{"elementSize",receipt.ElementSize},
                {"elementsPerSlab",receipt.ElementsPerSlab},{"used",receipt.Used},
                {"unreleased",receipt.Unreleased},{"slabSize",receipt.SlabSize},
                {"traversedCount",receipt.TraversedCount},
                {"orderHashBefore","0x"+receipt.OrderHashBefore.ToString("X8")},
                {"orderHashAfter","0x"+receipt.OrderHashAfter.ToString("X8")},
                {"topBefore",topBefore},{"topAfter",topAfter}};
            manifoldPoolReceipts.Add(value);if(manifoldPoolReceipts.Count>32)manifoldPoolReceipts.RemoveAt(0);
        }

        private SipPoolState CaptureSipPoolState(uint nphaseCore,int frame)
        {
            if(captureSipPoolSnapshot==null||nphaseCore==0)
                throw new InvalidOperationException("Native shape-pair-pool capture is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeSipPoolReceipt));
            if(size!=128)throw new InvalidOperationException("Managed shape-pair-pool ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr orderBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            NativeSipPoolReceipt receipt;
            uint[] order=null;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureSipPoolSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphaseCore),orderBuffer,MaximumShapeInstancePairs,receiptBuffer);
                receipt=(NativeSipPoolReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeSipPoolReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native shape-pair-pool capture failed at frame "+frame+
                        ": result="+receipt.Result+", Win32/error="+receipt.LastError+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||receipt.Pool==UIntPtr.Zero||
                    receipt.TraversedCount>MaximumShapeInstancePairs||receipt.ValidationFlags!=0x1Fu)
                    throw new InvalidOperationException("Native shape-pair-pool receipt contract differs.");
                order=new uint[receipt.TraversedCount];
                for(int i=0;i<order.Length;i++)order[i]=unchecked((uint)Marshal.ReadInt32(
                    orderBuffer,i*IntPtr.Size));
            }
            finally
            {
                Marshal.FreeHGlobal(orderBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            var state=new SipPoolState {NPhaseCore=nphaseCore,Pool=receipt.Pool.ToUInt32(),
                OrderHash=receipt.OrderHash,ElementSize=receipt.ElementSize,
                ElementsPerSlab=receipt.ElementsPerSlab,Used=receipt.Used,
                Unreleased=receipt.Unreleased,SlabSize=receipt.SlabSize,Order=order};
            ValidateSipPoolState(state);
            if(receipt.FreeHead.ToUInt32()!=(order.Length==0?0u:order[0])||
                receipt.OrderHash!=ContactPoolOrderHash(order))
                throw new InvalidOperationException("Managed shape-pair-pool snapshot differs from native capture.");
            return state;
        }

        private ActorPairPoolState CaptureActorPairPoolState(uint nphaseCore,int frame)
        {
            if(captureActorPairPoolSnapshot==null||nphaseCore==0)
                throw new InvalidOperationException("Native ActorPair-pool capture is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeActorPairPoolReceipt));
            if(size!=212)throw new InvalidOperationException("Managed ActorPair-pool ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr freeBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            IntPtr allocatedBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            NativeActorPairPoolReceipt receipt;
            uint[] freeOrder=null,allocatedOrder=null;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureActorPairPoolSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphaseCore),freeBuffer,MaximumShapeInstancePairs,
                    allocatedBuffer,MaximumShapeInstancePairs,receiptBuffer);
                receipt=(NativeActorPairPoolReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeActorPairPoolReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native ActorPair-pool capture failed at frame "+frame+
                        ": result="+receipt.Result+", Win32/error="+receipt.LastError+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||receipt.Pool==UIntPtr.Zero||
                    receipt.FreeCount>MaximumShapeInstancePairs||
                    receipt.AllocatedCount>MaximumShapeInstancePairs||receipt.ValidationFlags!=0xFFu)
                    throw new InvalidOperationException("Native ActorPair-pool receipt contract differs.");
                freeOrder=new uint[receipt.FreeCount];
                allocatedOrder=new uint[receipt.AllocatedCount];
                for(int i=0;i<freeOrder.Length;i++)freeOrder[i]=unchecked((uint)Marshal.ReadInt32(
                    freeBuffer,i*IntPtr.Size));
                for(int i=0;i<allocatedOrder.Length;i++)allocatedOrder[i]=unchecked((uint)Marshal.ReadInt32(
                    allocatedBuffer,i*IntPtr.Size));
            }
            finally
            {
                Marshal.FreeHGlobal(allocatedBuffer);Marshal.FreeHGlobal(freeBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            var state=new ActorPairPoolState {NPhaseCore=nphaseCore,Pool=receipt.Pool.ToUInt32(),
                FreeOrderHash=receipt.FreeOrderHash,AllocatedOrderHash=receipt.AllocatedOrderHash,
                ElementSize=receipt.ElementSize,ElementsPerSlab=receipt.ElementsPerSlab,
                Used=receipt.Used,Unreleased=receipt.Unreleased,SlabSize=receipt.SlabSize,
                SlabCount=receipt.SlabCount,TotalElements=receipt.TotalElements,
                FreeOrder=freeOrder,AllocatedOrder=allocatedOrder};
            ValidateActorPairPoolState(state);
            if(receipt.FreeHead.ToUInt32()!=(freeOrder.Length==0?0u:freeOrder[0])||
                receipt.FreeOrderHash!=ContactPoolOrderHash(freeOrder)||
                receipt.AllocatedOrderHash!=ContactPoolOrderHash(allocatedOrder))
                throw new InvalidOperationException("Managed ActorPair-pool snapshot differs from native capture.");
            return state;
        }

        private ActorPairReportPoolState CaptureActorPairReportPoolState(uint nphaseCore,int frame)
        {
            if(captureActorPairReportPoolSnapshot==null||nphaseCore==0)
                throw new InvalidOperationException("Native ActorPair report-pool capture is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeActorPairReportPoolReceipt));
            if(size!=212)throw new InvalidOperationException("Managed ActorPair report-pool ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr freeBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            IntPtr allocatedBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            NativeActorPairReportPoolReceipt receipt;
            uint[] freeOrder=null,allocatedOrder=null;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureActorPairReportPoolSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphaseCore),freeBuffer,MaximumShapeInstancePairs,
                    allocatedBuffer,MaximumShapeInstancePairs,receiptBuffer);
                receipt=(NativeActorPairReportPoolReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeActorPairReportPoolReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native ActorPair report-pool capture failed at frame "+frame+
                        ": result="+receipt.Result+", Win32/error="+receipt.LastError+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||receipt.Pool==UIntPtr.Zero||
                    receipt.FreeCount>MaximumShapeInstancePairs||
                    receipt.AllocatedCount>MaximumShapeInstancePairs||receipt.ValidationFlags!=0xFFu)
                    throw new InvalidOperationException("Native ActorPair report-pool receipt contract differs.");
                freeOrder=new uint[receipt.FreeCount];
                allocatedOrder=new uint[receipt.AllocatedCount];
                for(int i=0;i<freeOrder.Length;i++)freeOrder[i]=unchecked((uint)Marshal.ReadInt32(
                    freeBuffer,i*IntPtr.Size));
                for(int i=0;i<allocatedOrder.Length;i++)allocatedOrder[i]=unchecked((uint)Marshal.ReadInt32(
                    allocatedBuffer,i*IntPtr.Size));
            }
            finally
            {
                Marshal.FreeHGlobal(allocatedBuffer);Marshal.FreeHGlobal(freeBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            byte[][] allocatedBytes=allocatedOrder.Select(pointer=>ReadBytes(pointer,0x24)).ToArray();
            var state=new ActorPairReportPoolState {NPhaseCore=nphaseCore,Pool=receipt.Pool.ToUInt32(),
                FreeOrderHash=receipt.FreeOrderHash,AllocatedOrderHash=receipt.AllocatedOrderHash,
                ElementSize=receipt.ElementSize,ElementsPerSlab=receipt.ElementsPerSlab,
                Used=receipt.Used,Unreleased=receipt.Unreleased,SlabSize=receipt.SlabSize,
                SlabCount=receipt.SlabCount,TotalElements=receipt.TotalElements,
                FreeOrder=freeOrder,AllocatedOrder=allocatedOrder,AllocatedBytes=allocatedBytes};
            ValidateActorPairReportPoolState(state);
            if(receipt.FreeHead.ToUInt32()!=(freeOrder.Length==0?0u:freeOrder[0])||
                receipt.FreeOrderHash!=ContactPoolOrderHash(freeOrder)||
                receipt.AllocatedOrderHash!=ContactPoolOrderHash(allocatedOrder))
                throw new InvalidOperationException("Managed ActorPair report-pool snapshot differs from native capture.");
            return state;
        }

        private NPhasePoolImageState[] CaptureNPhasePoolImages(uint nphaseCore,int frame)
        {
            var values=new NPhasePoolImageState[NPhasePoolKindCount];
            for(uint kind=0;kind<NPhasePoolKindCount;kind++)
                values[kind]=CaptureNPhasePoolImage(nphaseCore,kind,frame);
            ValidateNPhasePoolImages(values);
            return values;
        }

        private NPhasePoolImageState CaptureNPhasePoolImage(
            uint nphaseCore,uint poolKind,int frame)
        {
            if(captureNPhasePoolSnapshot==null||nphaseCore==0||
                poolKind>=NPhasePoolKindCount)
                throw new InvalidOperationException(
                    "Native complete NPhase-pool capture is unavailable.");
            int bufferSize=Marshal.SizeOf(typeof(NativeNPhasePoolSnapshotBuffers));
            int receiptSize=Marshal.SizeOf(typeof(NativeNPhasePoolSnapshotReceipt));
            if(bufferSize!=32||receiptSize!=152)
                throw new InvalidOperationException(
                    "Managed complete NPhase-pool ABI size differs.");
            IntPtr slabBaseBuffer=Marshal.AllocHGlobal(
                MaximumNPhasePoolSlabs*IntPtr.Size);
            IntPtr freeSlotBuffer=Marshal.AllocHGlobal(
                MaximumShapeInstancePairs*sizeof(uint));
            IntPtr allocationBuffer=Marshal.AllocHGlobal(
                MaximumNPhasePoolSlabs*sizeof(uint));
            IntPtr slabByteBuffer=Marshal.AllocHGlobal(MaximumNPhasePoolSlabBytes);
            IntPtr buffersPointer=Marshal.AllocHGlobal(bufferSize);
            IntPtr receiptPointer=Marshal.AllocHGlobal(receiptSize);
            NativeNPhasePoolSnapshotReceipt receipt;
            uint[] slabBases=null,freeSlots=null,allocationWords=null;
            byte[] slabBytes=null;
            try
            {
                var buffers=new NativeNPhasePoolSnapshotBuffers {
                    SlabBases=slabBaseBuffer,
                    SlabBaseCapacity=MaximumNPhasePoolSlabs,
                    FreeSlots=freeSlotBuffer,
                    FreeSlotCapacity=MaximumShapeInstancePairs,
                    AllocationWords=allocationBuffer,
                    AllocationWordCapacity=MaximumNPhasePoolSlabs,
                    SlabBytes=slabByteBuffer,
                    SlabByteCapacity=MaximumNPhasePoolSlabBytes
                };
                Marshal.StructureToPtr(buffers,buffersPointer,false);
                IslandSnapshotBufferOwner.Zero(receiptPointer,receiptSize);
                int ok=captureNPhasePoolSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphaseCore),poolKind,buffersPointer,receiptPointer);
                receipt=(NativeNPhasePoolSnapshotReceipt)Marshal.PtrToStructure(
                    receiptPointer,typeof(NativeNPhasePoolSnapshotReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException(
                        "Native complete NPhase-pool capture failed at frame "+frame+
                        " for kind "+poolKind+": result="+receipt.Result+
                        ", Win32/error="+receipt.LastError+", invalidKind="+
                        receipt.InvalidKind+", invalidIndex="+receipt.InvalidIndex+
                        ", detail="+receipt.Detail+".");
                if(receipt.ApiVersion!=NativeAbiVersion||
                    receipt.StructSize!=(uint)receiptSize||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||
                    receipt.PoolKind!=poolKind||receipt.Pool==UIntPtr.Zero||
                    receipt.SlabBasesRequired>MaximumNPhasePoolSlabs||
                    receipt.FreeSlotsRequired>MaximumShapeInstancePairs||
                    receipt.AllocationWordsRequired>MaximumNPhasePoolSlabs||
                    receipt.SlabBytesRequired>MaximumNPhasePoolSlabBytes||
                    receipt.SlabBasesWritten!=receipt.SlabBasesRequired||
                    receipt.FreeSlotsWritten!=receipt.FreeSlotsRequired||
                    receipt.AllocationWordsWritten!=receipt.AllocationWordsRequired||
                    receipt.SlabBytesWritten!=receipt.SlabBytesRequired||
                    receipt.ValidationFlags!=0xFFu)
                    throw new InvalidOperationException(
                        "Native complete NPhase-pool receipt contract differs.");
                slabBases=ReadPointerBuffer(slabBaseBuffer,
                    receipt.SlabBasesWritten);
                freeSlots=ReadUInt32Buffer(freeSlotBuffer,
                    receipt.FreeSlotsWritten);
                allocationWords=ReadUInt32Buffer(allocationBuffer,
                    receipt.AllocationWordsWritten);
                slabBytes=new byte[checked((int)receipt.SlabBytesWritten)];
                if(slabBytes.Length!=0)
                    Marshal.Copy(slabByteBuffer,slabBytes,0,slabBytes.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(receiptPointer);
                Marshal.FreeHGlobal(buffersPointer);
                Marshal.FreeHGlobal(slabByteBuffer);
                Marshal.FreeHGlobal(allocationBuffer);
                Marshal.FreeHGlobal(freeSlotBuffer);
                Marshal.FreeHGlobal(slabBaseBuffer);
            }
            var state=new NPhasePoolImageState {Receipt=receipt,
                SlabBases=slabBases,FreeSlots=freeSlots,
                AllocationWords=allocationWords,SlabBytes=slabBytes};
            ValidateNPhasePoolImage(state,poolKind);
            return state;
        }

        private NPhaseReportState CaptureNPhaseReportState(uint nphaseCore,int frame)
        {
            if(captureNPhaseReportStateSnapshot==null||nphaseCore==0)
                throw new InvalidOperationException("Native NPhase report-state capture is unavailable.");
            uint bufferSize=ReadWord(checked(nphaseCore+0x34u));
            if(bufferSize==0||bufferSize>MaximumContactReportBufferSize)
                throw new InvalidOperationException("NPhase contact-report buffer size is outside the supported range.");
            int size=Marshal.SizeOf(typeof(NativeNPhaseReportStateReceipt));
            if(size!=116)throw new InvalidOperationException("Managed NPhase report-state ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(size);
            IntPtr actorPairBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            IntPtr persistentBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            IntPtr forceBuffer=Marshal.AllocHGlobal(MaximumShapeInstancePairs*IntPtr.Size);
            IntPtr bytesBuffer=Marshal.AllocHGlobal(checked((int)bufferSize));
            NativeNPhaseReportStateReceipt receipt;
            uint[] actorPairs=null,persistent=null,force=null;
            byte[] bytes=null;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureNPhaseReportStateSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphaseCore),actorPairBuffer,MaximumShapeInstancePairs,
                    persistentBuffer,MaximumShapeInstancePairs,forceBuffer,
                    MaximumShapeInstancePairs,bytesBuffer,bufferSize,receiptBuffer);
                receipt=(NativeNPhaseReportStateReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeNPhaseReportStateReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native NPhase report-state capture failed at frame "+frame+
                        ": result="+receipt.Result+", Win32/error="+receipt.LastError+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||receipt.OwnerScene==UIntPtr.Zero||
                    receipt.ActorPairCount>MaximumShapeInstancePairs||
                    receipt.PersistentCount>MaximumShapeInstancePairs||
                    receipt.ForceThresholdCount>MaximumShapeInstancePairs||
                    receipt.ReportBufferCurrentSize!=bufferSize||
                    receipt.ReportBufferCurrentSize>MaximumContactReportBufferSize||
                    receipt.ValidationFlags!=0x7Fu)
                    throw new InvalidOperationException("Native NPhase report-state receipt contract differs.");
                actorPairs=ReadPointerBuffer(actorPairBuffer,receipt.ActorPairCount);
                persistent=ReadPointerBuffer(persistentBuffer,receipt.PersistentCount);
                force=ReadPointerBuffer(forceBuffer,receipt.ForceThresholdCount);
                bytes=new byte[checked((int)receipt.ReportBufferCurrentSize)];
                Marshal.Copy(bytesBuffer,bytes,0,bytes.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(bytesBuffer);Marshal.FreeHGlobal(forceBuffer);
                Marshal.FreeHGlobal(persistentBuffer);Marshal.FreeHGlobal(actorPairBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            var state=new NPhaseReportState {Receipt=receipt,ActorPairs=actorPairs,
                PersistentSips=persistent,ForceThresholdSips=force,ReportBufferBytes=bytes};
            ValidateNPhaseReportState(state);
            return state;
        }

        private InteractionGraphState CaptureInteractionGraphState(uint nphaseCore,int frame)
        {
            if(captureInteractionGraphSnapshot==null||nphaseCore==0)
                throw new InvalidOperationException("Native interaction-graph capture is unavailable.");
            int receiptSize=Marshal.SizeOf(typeof(NativeInteractionGraphReceipt));
            int actorSize=Marshal.SizeOf(typeof(NativeInteractionGraphActorRecord));
            int interactionSize=Marshal.SizeOf(typeof(NativeInteractionGraphInteractionRecord));
            int poolSize=Marshal.SizeOf(typeof(NativeInteractionGraphPoolReceipt));
            if(receiptSize!=500||actorSize!=72||interactionSize!=72||poolSize!=80)
                throw new InvalidOperationException("Managed interaction-graph ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            IntPtr activeBuffer=Marshal.AllocHGlobal(MaximumInteractionGraphActors*IntPtr.Size);
            IntPtr actorBuffer=Marshal.AllocHGlobal(MaximumInteractionGraphActors*actorSize);
            IntPtr interactionBuffer=Marshal.AllocHGlobal(MaximumInteractionGraphInteractions*interactionSize);
            IntPtr actorSlotBuffer=Marshal.AllocHGlobal(MaximumInteractionGraphActorSlots*IntPtr.Size);
            IntPtr poolSlabBuffer=Marshal.AllocHGlobal(MaximumInteractionGraphPoolEntries*IntPtr.Size);
            IntPtr poolFreeBuffer=Marshal.AllocHGlobal(MaximumInteractionGraphPoolEntries*IntPtr.Size);
            NativeInteractionGraphReceipt receipt;
            uint[] activeBodies=null,actorSlots=null,poolSlabs=null,poolFree=null;
            NativeInteractionGraphActorRecord[] actors=null;
            NativeInteractionGraphInteractionRecord[] interactions=null;
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureInteractionGraphSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphaseCore),activeBuffer,MaximumInteractionGraphActors,
                    actorBuffer,MaximumInteractionGraphActors,
                    interactionBuffer,MaximumInteractionGraphInteractions,
                    actorSlotBuffer,MaximumInteractionGraphActorSlots,
                    poolSlabBuffer,MaximumInteractionGraphPoolEntries,
                    poolFreeBuffer,MaximumInteractionGraphPoolEntries,receiptBuffer);
                receipt=(NativeInteractionGraphReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeInteractionGraphReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native interaction-graph capture failed at frame "+frame+
                        ": result="+receipt.Result+", Win32/error="+receipt.LastError+
                        ", kind="+receipt.InvalidKind+", index="+receipt.InvalidIndex+
                        ", detail="+receipt.Detail+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)receiptSize||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||receipt.OwnerScene==UIntPtr.Zero||
                    receipt.InteractionScene==UIntPtr.Zero||receipt.LlContext==UIntPtr.Zero||
                    receipt.ActiveBodiesWritten!=receipt.ActiveBodiesRequired||
                    receipt.ActorsWritten!=receipt.ActorsRequired||
                    receipt.InteractionsWritten!=receipt.InteractionsRequired||
                    receipt.ActorSlotsWritten!=receipt.ActorSlotsRequired||
                    receipt.PoolSlabsWritten!=receipt.PoolSlabsRequired||
                    receipt.PoolFreeWritten!=receipt.PoolFreeRequired||
                    receipt.ActiveBodiesWritten>MaximumInteractionGraphActors||
                    receipt.ActorsWritten>MaximumInteractionGraphActors||
                    receipt.InteractionsWritten>MaximumInteractionGraphInteractions||
                    receipt.ActorSlotsWritten>MaximumInteractionGraphActorSlots||
                    receipt.PoolSlabsWritten>MaximumInteractionGraphPoolEntries||
                    receipt.PoolFreeWritten>MaximumInteractionGraphPoolEntries||
                    receipt.ValidationFlags!=0xFFu)
                    throw new InvalidOperationException("Native interaction-graph receipt contract differs at frame "+frame+".");
                activeBodies=ReadPointerBuffer(activeBuffer,receipt.ActiveBodiesWritten);
                actorSlots=ReadPointerBuffer(actorSlotBuffer,receipt.ActorSlotsWritten);
                poolSlabs=ReadPointerBuffer(poolSlabBuffer,receipt.PoolSlabsWritten);
                poolFree=ReadPointerBuffer(poolFreeBuffer,receipt.PoolFreeWritten);
                actors=new NativeInteractionGraphActorRecord[receipt.ActorsWritten];
                for(int i=0;i<actors.Length;i++)actors[i]=(NativeInteractionGraphActorRecord)
                    Marshal.PtrToStructure(new IntPtr(actorBuffer.ToInt64()+i*actorSize),
                        typeof(NativeInteractionGraphActorRecord));
                interactions=new NativeInteractionGraphInteractionRecord[receipt.InteractionsWritten];
                for(int i=0;i<interactions.Length;i++)interactions[i]=(NativeInteractionGraphInteractionRecord)
                    Marshal.PtrToStructure(new IntPtr(interactionBuffer.ToInt64()+i*interactionSize),
                        typeof(NativeInteractionGraphInteractionRecord));
            }
            finally
            {
                Marshal.FreeHGlobal(poolFreeBuffer);Marshal.FreeHGlobal(poolSlabBuffer);
                Marshal.FreeHGlobal(actorSlotBuffer);Marshal.FreeHGlobal(interactionBuffer);
                Marshal.FreeHGlobal(actorBuffer);Marshal.FreeHGlobal(activeBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            byte[][] primaryBytes=interactions.Select(CaptureInteractionPrimaryBytes).ToArray();
            var state=new InteractionGraphState {Receipt=receipt,ActiveBodies=activeBodies,
                Actors=actors,Interactions=interactions,ActorSlots=actorSlots,
                PoolSlabs=poolSlabs,PoolFree=poolFree,PrimaryBytes=primaryBytes};
            ValidateInteractionGraphState(state);
            return state;
        }

        private static byte[] CaptureInteractionPrimaryBytes(
            NativeInteractionGraphInteractionRecord value)
        {
            int bytes=value.InteractionType==0?0x44:value.InteractionType==2?0x3C:
                value.InteractionType==3?0x28:0;
            if(bytes==0)return new byte[0];
            uint interaction=value.Interaction.ToUInt32();
            if(interaction<8u)throw new InvalidOperationException(
                "A rigid interaction has no valid primary-object base.");
            return ReadBytes(interaction-8u,bytes);
        }

        private TransformCacheState CaptureTransformCacheState(uint nphaseCore,int frame)
        {
            if(captureTransformCacheSnapshot==null||nphaseCore==0)
                throw new InvalidOperationException("Native transform-cache capture is unavailable.");
            int receiptSize=Marshal.SizeOf(typeof(NativeTransformCacheReceipt));
            int entrySize=Marshal.SizeOf(typeof(NativeTransformCacheEntryRecord));
            int bindingSize=Marshal.SizeOf(typeof(NativeTransformCacheBindingRecord));
            if(receiptSize!=144||entrySize!=48||bindingSize!=40)
                throw new InvalidOperationException("Managed transform-cache ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            IntPtr entryBuffer=Marshal.AllocHGlobal(MaximumTransformCacheIds*entrySize);
            IntPtr freeBuffer=Marshal.AllocHGlobal(MaximumTransformCacheIds*sizeof(uint));
            IntPtr bindingBuffer=Marshal.AllocHGlobal(MaximumTransformCacheBindings*bindingSize);
            NativeTransformCacheReceipt receipt;
            NativeTransformCacheEntryRecord[] entries=null;
            NativeTransformCacheBindingRecord[] bindings=null;
            uint[] freeIds=null;
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureTransformCacheSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphaseCore),entryBuffer,MaximumTransformCacheIds,
                    freeBuffer,MaximumTransformCacheIds,bindingBuffer,
                    MaximumTransformCacheBindings,receiptBuffer);
                receipt=(NativeTransformCacheReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeTransformCacheReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native transform-cache capture failed at frame "+frame+
                        ": result="+receipt.Result+", Win32/error="+receipt.LastError+
                        ", kind="+receipt.InvalidKind+", index="+receipt.InvalidIndex+
                        ", detail="+receipt.Detail+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)receiptSize||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||receipt.OwnerScene==UIntPtr.Zero||
                    receipt.InteractionScene==UIntPtr.Zero||receipt.Context==UIntPtr.Zero||
                    receipt.TransformCache==UIntPtr.Zero||
                    receipt.EntriesWritten!=receipt.EntriesRequired||
                    receipt.FreeWritten!=receipt.FreeRequired||
                    receipt.BindingsWritten!=receipt.BindingsRequired||
                    receipt.EntriesWritten>MaximumTransformCacheIds||
                    receipt.FreeWritten>MaximumTransformCacheIds||
                    receipt.BindingsWritten>MaximumTransformCacheBindings)
                    throw new InvalidOperationException("Native transform-cache receipt contract differs at frame "+frame+".");
                entries=new NativeTransformCacheEntryRecord[receipt.EntriesWritten];
                for(int i=0;i<entries.Length;i++)entries[i]=(NativeTransformCacheEntryRecord)
                    Marshal.PtrToStructure(new IntPtr(entryBuffer.ToInt64()+i*entrySize),
                        typeof(NativeTransformCacheEntryRecord));
                freeIds=new uint[receipt.FreeWritten];
                for(int i=0;i<freeIds.Length;i++)freeIds[i]=unchecked((uint)
                    Marshal.ReadInt32(freeBuffer,i*sizeof(uint)));
                bindings=new NativeTransformCacheBindingRecord[receipt.BindingsWritten];
                for(int i=0;i<bindings.Length;i++)bindings[i]=(NativeTransformCacheBindingRecord)
                    Marshal.PtrToStructure(new IntPtr(bindingBuffer.ToInt64()+i*bindingSize),
                        typeof(NativeTransformCacheBindingRecord));
            }
            finally
            {
                Marshal.FreeHGlobal(bindingBuffer);Marshal.FreeHGlobal(freeBuffer);
                Marshal.FreeHGlobal(entryBuffer);Marshal.FreeHGlobal(receiptBuffer);
            }
            var state=new TransformCacheState {Receipt=receipt,Entries=entries,
                FreeIds=freeIds,Bindings=bindings};
            ValidateTransformCacheState(state);
            return state;
        }

        private static uint[] ReadPointerBuffer(IntPtr buffer,uint count)
        {
            var values=new uint[checked((int)count)];
            for(int i=0;i<values.Length;i++)values[i]=unchecked((uint)Marshal.ReadInt32(
                buffer,i*IntPtr.Size));
            return values;
        }

        private static uint[] ReadUInt32Buffer(IntPtr buffer,uint count)
        {
            var values=new uint[checked((int)count)];
            for(int i=0;i<values.Length;i++)values[i]=unchecked((uint)
                Marshal.ReadInt32(buffer,i*sizeof(uint)));
            return values;
        }

        private TransformDispatchState RunTransformDispatchAction(int action,TransformDispatchState snapshot)
        {
            TransformDispatchState before=ReadTransformDispatchState();
            TransformDispatchState after=before;
            if(action==1)
            {
                transformDispatchCaptures++;
                snapshot=before;
            }
            else
            {
                if(snapshot==null)
                    throw new InvalidOperationException("No transform-dispatch snapshot has been captured.");
                if(before.Dispatch!=snapshot.Dispatch)
                    throw new InvalidOperationException("Transform-dispatch object changed since capture.");
                if(before.Capacity<snapshot.Count||before.Array==0)
                    throw new InvalidOperationException("Current transform-dispatch array cannot hold the captured queue.");
                if(snapshot.Entries==null||snapshot.Count!=(uint)snapshot.Entries.Length)
                    throw new InvalidOperationException("Transform-dispatch checkpoint count is inconsistent.");

                var snapshotPointers=new HashSet<uint>(snapshot.Entries.Select(item=>item.Hierarchy));
                if(snapshotPointers.Count!=snapshot.Entries.Length||snapshotPointers.Contains(0u))
                    throw new InvalidOperationException("Transform-dispatch checkpoint contains a null or duplicate hierarchy.");
                // Check every saved hierarchy before the first write. An older
                // checkpoint may name a hierarchy that has since been retired;
                // such a target must fail without partially changing the queue.
                foreach(TransformDispatchEntryState entry in snapshot.Entries)
                {
                    ReadWord(entry.Hierarchy+0x1Cu);
                    ReadWord(entry.Hierarchy+0x20u);
                    ReadWord(entry.Hierarchy+0x24u);
                }
                foreach(TransformDispatchEntryState entry in before.Entries)
                {
                    WriteWord(entry.Hierarchy+0x1Cu,0xFFFFFFFFu);
                    if(!snapshotPointers.Contains(entry.Hierarchy))
                    {
                        WriteWord(entry.Hierarchy+0x20u,0u);
                        WriteWord(entry.Hierarchy+0x24u,0u);
                    }
                }
                for(int i=0;i<snapshot.Entries.Length;i++)
                {
                    TransformDispatchEntryState entry=snapshot.Entries[i];
                    WriteWord(entry.Hierarchy+0x20u,entry.MaskLow);
                    WriteWord(entry.Hierarchy+0x24u,entry.MaskHigh);
                    WriteWord(entry.Hierarchy+0x1Cu,(uint)i);
                    WriteWord(before.Array+(uint)(i*4),entry.Hierarchy);
                }
                for(uint i=snapshot.Count;i<before.Count;i++)
                    WriteWord(before.Array+i*4u,0u);
                WriteWord(before.Dispatch+0x10u,snapshot.Count);
                WriteWord(before.Dispatch+0x00u,snapshot.GlobalMaskLow);
                WriteWord(before.Dispatch+0x04u,snapshot.GlobalMaskHigh);
                after=ReadTransformDispatchState();
                if(!SameTransformDispatchState(snapshot,after))
                    throw new InvalidOperationException("Transform-dispatch restore readback differs from the checkpoint snapshot.");
                transformDispatchRestores++;
            }
            var value=new Dictionary<string,object> {
                {"action",action==1?"capture":"restore"},
                {"before",DescribeTransformDispatchState(before)},
                {"after",DescribeTransformDispatchState(after)}
            };
            transformDispatchReceipts.Add(value);
            if(transformDispatchReceipts.Count>16)transformDispatchReceipts.RemoveAt(0);
            return snapshot;
        }

        private TransformDispatchState ReadTransformDispatchState()
        {
            uint[] expectedHandles={10u,11u,12u,13u};
            uint[] handleRvas={0xFA2E34u,0xFA2E38u,0xFA2E3Cu,0xFA2E40u};
            for(int i=0;i<handleRvas.Length;i++)
                if(ReadWord(unityPlayerBase+handleRvas[i])!=expectedHandles[i])
                    throw new InvalidOperationException("Unity physics transform handle contract differs at RVA 0x"+handleRvas[i].ToString("X8")+".");
            uint dispatch=ReadWord(unityPlayerBase+0xFF0D28u);
            if(dispatch==0)throw new InvalidOperationException("Unity transform-dispatch object is unavailable.");
            var state=new TransformDispatchState {
                Dispatch=dispatch,
                GlobalMaskLow=ReadWord(dispatch+0x00u),GlobalMaskHigh=ReadWord(dispatch+0x04u),
                Array=ReadWord(dispatch+0x08u),Capacity=ReadWord(dispatch+0x0Cu),Count=ReadWord(dispatch+0x10u)
            };
            if(state.Count>state.Capacity||state.Capacity>4096u||(state.Count!=0&&state.Array==0))
                throw new InvalidOperationException("Unity transform-dispatch queue bounds are invalid.");
            var entries=new TransformDispatchEntryState[state.Count];
            var seen=new HashSet<uint>();
            for(uint i=0;i<state.Count;i++)
            {
                uint hierarchy=ReadWord(state.Array+i*4u);
                if(hierarchy==0||!seen.Add(hierarchy))
                    throw new InvalidOperationException("Unity transform-dispatch queue contains a null or duplicate hierarchy.");
                uint queueIndex=ReadWord(hierarchy+0x1Cu);
                if(queueIndex!=i)
                    throw new InvalidOperationException("Unity transform hierarchy queue index differs from its ordered slot.");
                entries[i]=new TransformDispatchEntryState {
                    Hierarchy=hierarchy,MaskLow=ReadWord(hierarchy+0x20u),MaskHigh=ReadWord(hierarchy+0x24u)
                };
            }
            state.Entries=entries;
            return state;
        }

        private static bool SameTransformDispatchState(TransformDispatchState left,TransformDispatchState right)
        {
            if(left.Dispatch!=right.Dispatch||left.Count!=right.Count||
                left.GlobalMaskLow!=right.GlobalMaskLow||left.GlobalMaskHigh!=right.GlobalMaskHigh||
                left.Entries.Length!=right.Entries.Length)return false;
            for(int i=0;i<left.Entries.Length;i++)
                if(left.Entries[i].Hierarchy!=right.Entries[i].Hierarchy||
                    left.Entries[i].MaskLow!=right.Entries[i].MaskLow||
                    left.Entries[i].MaskHigh!=right.Entries[i].MaskHigh)return false;
            return true;
        }

        private static object DescribeTransformDispatchState(TransformDispatchState state)
        {
            uint hash=2166136261u;
            var entries=new object[state.Entries.Length];
            for(int i=0;i<state.Entries.Length;i++)
            {
                TransformDispatchEntryState entry=state.Entries[i];
                foreach(uint word in new[]{entry.Hierarchy,entry.MaskLow,entry.MaskHigh})
                {
                    hash^=word;hash*=16777619u;
                }
                entries[i]=new Dictionary<string,object> {
                    {"index",i},{"hierarchy","0x"+entry.Hierarchy.ToString("X8")},
                    {"maskLow","0x"+entry.MaskLow.ToString("X8")},{"maskHigh","0x"+entry.MaskHigh.ToString("X8")}
                };
            }
            return new Dictionary<string,object> {
                {"dispatch","0x"+state.Dispatch.ToString("X8")},{"array","0x"+state.Array.ToString("X8")},
                {"capacity",state.Capacity},{"count",state.Count},
                {"globalMaskLow","0x"+state.GlobalMaskLow.ToString("X8")},
                {"globalMaskHigh","0x"+state.GlobalMaskHigh.ToString("X8")},
                {"orderedStateHash","0x"+hash.ToString("X8")},{"entries",entries}
            };
        }

        private NativeDirtyInteractionReceipt CallDirtyInteractionAction(
            NativeDirtyInteractionAction callback,string action,params uint[] acceptedResults)
        {
            if(callback==null)throw new InvalidOperationException("Native dirty-interaction export is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeDirtyInteractionReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);NativeDirtyInteractionReceipt receipt;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                int ok=callback(new UIntPtr(unityPlayerBase),buffer);
                receipt=(NativeDirtyInteractionReceipt)Marshal.PtrToStructure(buffer,typeof(NativeDirtyInteractionReceipt));
                if(ok==0)throw new InvalidOperationException("Native dirty-interaction "+action+
                    " failed: result="+receipt.Result+", Win32/error="+receipt.LastError+".");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            ValidateDirtyInteractionReceiptContract(receipt,size);
            RecordDirtyInteractionReceipt(action,receipt);
            if(acceptedResults!=null&&acceptedResults.Length!=0&&!acceptedResults.Contains(receipt.Result))
                throw new InvalidOperationException("Native dirty-interaction "+action+
                    " returned unexpected result="+receipt.Result+", Win32/error="+receipt.LastError+".");
            return receipt;
        }

        private NativeDirtyInteractionReceipt InstallDirtyInteractionHook()
        {
            if(installDirtyInteractionOrder==null)
                throw new InvalidOperationException("Native dirty-interaction install export is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeDirtyInteractionReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);NativeDirtyInteractionReceipt receipt;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                int ok=installDirtyInteractionOrder(new UIntPtr(unityPlayerBase),buffer);
                // A nonzero native return means UnityPlayer has already been
                // patched.  Claim ownership before unmarshalling or validating
                // the receipt so activation cleanup can never unload a DLL
                // whose hook is still live.
                if(ok!=0)dirtyInteractionHookInstalled=true;
                receipt=(NativeDirtyInteractionReceipt)Marshal.PtrToStructure(
                    buffer,typeof(NativeDirtyInteractionReceipt));
                if(ok==0)throw new InvalidOperationException("Native dirty-interaction install failed: result="+
                    receipt.Result+", Win32/error="+receipt.LastError+".");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            ValidateDirtyInteractionReceiptContract(receipt,size);
            RecordDirtyInteractionReceipt("install",receipt);
            return receipt;
        }

        private NativeFinishBroadPhaseObserverReceipt CallFinishBroadPhaseObserverAction(
            NativeFinishBroadPhaseObserverAction callback,string action)
        {
            if(callback==null)
                throw new InvalidOperationException("Native finishBroadPhase observer export is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeFinishBroadPhaseObserverReceipt));
            if(size!=152)throw new InvalidOperationException("Managed finishBroadPhase observer ABI size differs.");
            IntPtr buffer=Marshal.AllocHGlobal(size);
            NativeFinishBroadPhaseObserverReceipt receipt;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                int ok=callback(new UIntPtr(unityPlayerBase),buffer);
                receipt=(NativeFinishBroadPhaseObserverReceipt)Marshal.PtrToStructure(
                    buffer,typeof(NativeFinishBroadPhaseObserverReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native finishBroadPhase observer "+action+
                        " failed: result="+receipt.Result+", Win32/error="+receipt.LastError+
                        ", state="+receipt.State+", detail="+receipt.Detail+".");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            ValidateFinishBroadPhaseReceiptContract(receipt,size);
            RecordFinishBroadPhaseReceipt(action,receipt);
            return receipt;
        }

        private NativeFinishBroadPhaseObserverReceipt InstallFinishBroadPhaseObserver()
        {
            if(installFinishBroadPhaseObserver==null)
                throw new InvalidOperationException("Native finishBroadPhase observer install export is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeFinishBroadPhaseObserverReceipt));
            if(size!=152)throw new InvalidOperationException("Managed finishBroadPhase observer ABI size differs.");
            IntPtr buffer=Marshal.AllocHGlobal(size);
            NativeFinishBroadPhaseObserverReceipt receipt;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                int ok=installFinishBroadPhaseObserver(new UIntPtr(unityPlayerBase),buffer);
                // As with the dirty hook, claim ownership before validating so
                // activation cleanup cannot unload a still-patched DLL.
                if(ok!=0)
                {
                    finishBroadPhaseObserverInstalled=true;
                    finishBroadPhaseObserverWasInstalled=true;
                }
                receipt=(NativeFinishBroadPhaseObserverReceipt)Marshal.PtrToStructure(
                    buffer,typeof(NativeFinishBroadPhaseObserverReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native finishBroadPhase observer install failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+".");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            ValidateFinishBroadPhaseReceiptContract(receipt,size);
            RecordFinishBroadPhaseReceipt("install",receipt);
            return receipt;
        }

        private NativeIslandObserverReceipt InstallIslandObserver(uint expectedManager)
        {
            if(installIslandObserver==null||expectedManager==0)
                throw new InvalidOperationException("Native island observer install export or manager is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeIslandObserverReceipt));
            if(size!=128)throw new InvalidOperationException("Managed island observer ABI size differs.");
            IntPtr buffer=Marshal.AllocHGlobal(size);NativeIslandObserverReceipt receipt;
            try
            {
                IslandSnapshotBufferOwner.Zero(buffer,size);
                int ok=installIslandObserver(new UIntPtr(unityPlayerBase),new UIntPtr(expectedManager),buffer);
                if(ok!=0)islandObserverInstalled=true;
                receipt=(NativeIslandObserverReceipt)Marshal.PtrToStructure(
                    buffer,typeof(NativeIslandObserverReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native island observer install failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+", state="+receipt.State+".");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            ValidateIslandObserverReceiptContract(receipt);
            if(receipt.ExpectedManager.ToUInt32()!=expectedManager||receipt.Installed!=1||
                receipt.State!=1||receipt.ValidationFlags!=1u||receipt.InFlight!=0)
                throw new InvalidOperationException("Native island observer install receipt differs.");
            islandObserverManager=expectedManager;
            ReleaseRetainedIslandBuffers(library);
            return receipt;
        }

        private static void ReleaseRetainedIslandBuffers(IntPtr ownerLibrary)
        {
            lock(processRetainedIslandBuffers)
            {
                for(int i=processRetainedIslandBuffers.Count-1;i>=0;i--)
                {
                    PendingIslandObservation value=processRetainedIslandBuffers[i];
                    if(value.Library!=ownerLibrary)continue;
                    value.Pre.Dispose();value.Post.Dispose();
                    processRetainedIslandBuffers.RemoveAt(i);
                }
            }
        }

        private NativeIslandObserverReceipt CallIslandObserverAction(
            NativeIslandObserverAction callback,string action,params uint[] acceptedResults)
        {
            if(callback==null)throw new InvalidOperationException("Native island observer "+action+" export is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeIslandObserverReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);NativeIslandObserverReceipt receipt;
            try
            {
                IslandSnapshotBufferOwner.Zero(buffer,size);
                callback(new UIntPtr(unityPlayerBase),buffer);
                receipt=(NativeIslandObserverReceipt)Marshal.PtrToStructure(
                    buffer,typeof(NativeIslandObserverReceipt));
            }
            finally{Marshal.FreeHGlobal(buffer);}
            ValidateIslandObserverReceiptContract(receipt);
            if(acceptedResults!=null&&acceptedResults.Length!=0&&!acceptedResults.Contains(receipt.Result))
                throw new InvalidOperationException("Native island observer "+action+
                    " failed: result="+receipt.Result+", Win32/error="+receipt.LastError+
                    ", state="+receipt.State+", inFlight="+receipt.InFlight+".");
            return receipt;
        }

        private void ValidateIslandObserverReceiptContract(NativeIslandObserverReceipt receipt)
        {
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=128u||
                receipt.UnityBase.ToUInt32()!=unityPlayerBase)
                throw new InvalidOperationException("Native island observer receipt contract differs.");
        }

        private IslandSnapshotState CaptureIslandSnapshotState(uint nphaseCore,uint phase,int frame)
        {
            if(captureIslandSnapshot==null||nphaseCore==0)
                throw new InvalidOperationException("Native island snapshot export or NPhaseCore is unavailable.");
            using(IslandSnapshotBufferOwner owner=IslandSnapshotBufferOwner.Create())
            {
                int ok=captureIslandSnapshot(new UIntPtr(unityPlayerBase),new UIntPtr(nphaseCore),
                    phase,owner.BuffersPointer,owner.ReceiptPointer);
                NativeIslandSnapshotReceipt receipt=(NativeIslandSnapshotReceipt)Marshal.PtrToStructure(
                    owner.ReceiptPointer,typeof(NativeIslandSnapshotReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native island snapshot failed at frame "+frame+
                        ": result="+receipt.Result+", Win32/error="+receipt.LastError+
                        ", invalidKind="+receipt.InvalidKind+", invalidIndex="+receipt.InvalidIndex+
                        ", detail="+receipt.Detail+".");
                if(receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.NPhaseCore.ToUInt32()!=nphaseCore||receipt.Phase!=phase)
                    throw new InvalidOperationException("Native island snapshot returned a different Unity/NPhase/phase identity.");
                IslandSnapshotState state=ReadIslandSnapshotState(owner,receipt);
                ValidateIslandSnapshotState(state,phase);
                return state;
            }
        }

        private static IslandSnapshotState ReadIslandSnapshotState(
            IslandSnapshotBufferOwner owner,NativeIslandSnapshotReceipt receipt)
        {
            var state=new IslandSnapshotState {Receipt=receipt,
                Nodes=ReadStructArray<NativeIslandNodeSlotRecord>(owner.Nodes,receipt.NodeManager.Written),
                Edges=ReadStructArray<NativeIslandEdgeSlotRecord>(owner.Edges,receipt.EdgeManager.Written),
                Islands=ReadStructArray<NativeIslandSlotRecord>(owner.Islands,receipt.IslandManagerReceipt.Written),
                Roots=ReadStructArray<NativeIslandRootSlotRecord>(owner.Roots,receipt.RootManager.Written),
                KinematicWords=ReadUIntArray(owner.KinematicWords,receipt.Kinematic.Written),
                KinematicChangeWords=ReadUIntArray(owner.KinematicChangeWords,receipt.KinematicChange.Written),
                NotReadyWords=ReadUIntArray(owner.NotReadyWords,receipt.NotReady.Written),
                NotReadyChangeWords=ReadUIntArray(owner.NotReadyChangeWords,receipt.NotReadyChange.Written),
                IslandWords=ReadUIntArray(owner.IslandWords,receipt.IslandBitmap.Written),
                NodeCreated=ReadUIntArray(owner.NodeCreated,receipt.NodeCreated.Written),
                NodeDeleted=ReadUIntArray(owner.NodeDeleted,receipt.NodeDeleted.Written),
                EdgeCreated=ReadUIntArray(owner.EdgeCreated,receipt.EdgeCreated.Written),
                EdgeDeleted=ReadUIntArray(owner.EdgeDeleted,receipt.EdgeDeleted.Written),
                EdgeBroken=ReadUIntArray(owner.EdgeBroken,receipt.EdgeBroken.Written),
                EdgeJoined=ReadUIntArray(owner.EdgeJoined,receipt.EdgeJoined.Written),
                Bindings=ReadStructArray<NativeIslandSipBindingRecord>(owner.Bindings,receipt.BindingsWritten)};
            state.RawBytes=IslandSnapshotRawBytes(owner,receipt);
            return state;
        }

        private static T[] ReadStructArray<T>(IntPtr pointer,uint count) where T:struct
        {
            if(count>int.MaxValue)throw new InvalidOperationException("Native array count exceeds Int32.");
            int size=Marshal.SizeOf(typeof(T));T[] values=new T[(int)count];
            for(int i=0;i<values.Length;i++)values[i]=(T)Marshal.PtrToStructure(
                new IntPtr(pointer.ToInt64()+checked(i*size)),typeof(T));
            return values;
        }

        private static uint[] ReadUIntArray(IntPtr pointer,uint count)
        {
            if(count>int.MaxValue)throw new InvalidOperationException("Native word count exceeds Int32.");
            uint[] values=new uint[(int)count];
            for(int i=0;i<values.Length;i++)values[i]=unchecked((uint)Marshal.ReadInt32(pointer,i*4));
            return values;
        }

        private static byte[] IslandSnapshotRawBytes(
            IslandSnapshotBufferOwner owner,NativeIslandSnapshotReceipt receipt)
        {
            IntPtr[] pointers={owner.ReceiptPointer,owner.Nodes,owner.Edges,owner.Islands,owner.Roots,
                owner.KinematicWords,owner.KinematicChangeWords,owner.NotReadyWords,
                owner.NotReadyChangeWords,owner.IslandWords,owner.NodeCreated,owner.NodeDeleted,
                owner.EdgeCreated,owner.EdgeDeleted,owner.EdgeBroken,owner.EdgeJoined,owner.Bindings};
            int[] lengths={620,
                checked((int)receipt.NodeManager.Written*32),checked((int)receipt.EdgeManager.Written*36),
                checked((int)receipt.IslandManagerReceipt.Written*32),checked((int)receipt.RootManager.Written*24),
                checked((int)receipt.Kinematic.Written*4),checked((int)receipt.KinematicChange.Written*4),
                checked((int)receipt.NotReady.Written*4),checked((int)receipt.NotReadyChange.Written*4),
                checked((int)receipt.IslandBitmap.Written*4),checked((int)receipt.NodeCreated.Written*4),
                checked((int)receipt.NodeDeleted.Written*4),checked((int)receipt.EdgeCreated.Written*4),
                checked((int)receipt.EdgeDeleted.Written*4),checked((int)receipt.EdgeBroken.Written*4),
                checked((int)receipt.EdgeJoined.Written*4),checked((int)receipt.BindingsWritten*44)};
            int total=0;foreach(int length in lengths)total=checked(total+length);
            byte[] bytes=new byte[total];int offset=0;
            for(int i=0;i<pointers.Length;i++)
            {
                if(lengths[i]!=0)Marshal.Copy(pointers[i],bytes,offset,lengths[i]);
                offset+=lengths[i];
            }
            return bytes;
        }

        private void ArmCheckpointObservationCapture(CheckpointSidecar sidecar)
        {
            ValidateTransformCacheState(sidecar==null?null:sidecar.TransformCache);
            ValidateIslandSnapshotState(sidecar==null?null:sidecar.IslandSnapshot,IslandPhaseSettled);
            if(!finishBroadPhaseObserverInstalled||!islandObserverInstalled||sidecar.InteractionGraph==null)
                throw new InvalidOperationException("Checkpoint observation requires both installed native observers.");
            NativeInteractionGraphReceipt graph=sidecar.InteractionGraph.Receipt;
            int size=Marshal.SizeOf(typeof(NativeFinishBroadPhaseObserverReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);
            NativeFinishBroadPhaseObserverReceipt receipt=new NativeFinishBroadPhaseObserverReceipt();
            int ok=0;
            try
            {
                ArmIslandObservationCapture(sidecar,true);
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                ok=armFinishBroadPhaseObserver(new UIntPtr(unityPlayerBase),graph.OwnerScene,
                    graph.LlContext,graph.NPhaseCore,0,buffer);
                receipt=(NativeFinishBroadPhaseObserverReceipt)Marshal.PtrToStructure(
                    buffer,typeof(NativeFinishBroadPhaseObserverReceipt));
            }
            catch
            {
                try{if(ok!=0||pendingIslandObservation!=null)CancelCheckpointObservationWork();}
                finally{pendingFinishBroadPhaseOrdinal=0;}
                throw;
            }
            finally{Marshal.FreeHGlobal(buffer);}
            try
            {
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native finishBroadPhase observer arm failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+", state="+receipt.State+".");
                ValidateFinishBroadPhaseReceiptContract(receipt,size);
                RecordFinishBroadPhaseReceipt("arm",receipt);
                if(receipt.Installed!=1||receipt.State!=2||receipt.ExpectedPass!=0||
                    receipt.ExpectedScene!=graph.OwnerScene||receipt.ExpectedContext!=graph.LlContext||
                    receipt.ExpectedNPhaseCore!=graph.NPhaseCore||receipt.ArmedOrdinal==0)
                    throw new InvalidOperationException("Native finishBroadPhase observer arm differs from the sealed checkpoint identities.");
                pendingFinishBroadPhaseOrdinal=receipt.ArmedOrdinal;
                ArmDirtyInteractionCapture(sidecar);
            }
            catch
            {
                try{if(ok!=0||pendingIslandObservation!=null)CancelCheckpointObservationWork();}
                finally{pendingFinishBroadPhaseOrdinal=0;}
                throw;
            }
        }

        private void ArmIslandObservationCapture(CheckpointSidecar sidecar,bool requireAdjacentSequence)
        {
            if(pendingIslandObservation!=null||armIslandObserver==null)
                throw new InvalidOperationException("Another island observation is pending or the arm export is unavailable.");
            NativeIslandSnapshotReceipt settled=sidecar.IslandSnapshot.Receipt;
            var pending=new PendingIslandObservation {Sidecar=sidecar,Library=library,
                ExpectedManager=settled.IslandManager.ToUInt32(),
                ExpectedContext=settled.Context.ToUInt32(),ExpectedNphase=settled.NPhaseCore.ToUInt32()};
            try
            {
                pending.Pre=IslandSnapshotBufferOwner.Create();
                pending.Post=IslandSnapshotBufferOwner.Create();
                pendingIslandObservation=pending;
                int size=Marshal.SizeOf(typeof(NativeIslandObserverReceipt));
                IntPtr receiptBuffer=Marshal.AllocHGlobal(size);NativeIslandObserverReceipt receipt;
                try
                {
                    IslandSnapshotBufferOwner.Zero(receiptBuffer,size);
                    int ok=armIslandObserver(new UIntPtr(unityPlayerBase),settled.IslandManager,
                        settled.NPhaseCore,0,pending.Pre.BuffersPointer,pending.Pre.ReceiptPointer,
                        pending.Post.BuffersPointer,pending.Post.ReceiptPointer,receiptBuffer);
                    receipt=(NativeIslandObserverReceipt)Marshal.PtrToStructure(
                        receiptBuffer,typeof(NativeIslandObserverReceipt));
                    if(ok==0||receipt.Result!=1)
                        throw new InvalidOperationException("Native island observer arm failed: result="+
                            receipt.Result+", Win32/error="+receipt.LastError+", state="+receipt.State+".");
                }
                finally{Marshal.FreeHGlobal(receiptBuffer);}
                ValidateIslandObserverReceiptContract(receipt);
                uint expectedSequence=unchecked(settled.ObserverSequence+1u);
                if(receipt.Installed!=1||receipt.State!=2||receipt.ExpectedPass!=0||
                    receipt.ExpectedManager!=settled.IslandManager||receipt.ExpectedContext!=settled.Context||
                    receipt.ExpectedNPhase!=settled.NPhaseCore||receipt.ArmedOrdinal==0||
                    receipt.ArmedThreadId==0||
                    (requireAdjacentSequence?receipt.ObserverSequence!=expectedSequence:
                        receipt.ObserverSequence<=settled.ObserverSequence)||receipt.InFlight!=0)
                    throw new InvalidOperationException("Native island observer arm differs from the settled checkpoint identities.");
                pending.ArmedOrdinal=receipt.ArmedOrdinal;
                pending.ObserverSequence=receipt.ObserverSequence;
                pending.ArmedThreadId=receipt.ArmedThreadId;
            }
            catch
            {
                if(ReferenceEquals(pendingIslandObservation,pending))
                    CancelIslandObservationWork();
                else{if(pending.Pre!=null)pending.Pre.Dispose();if(pending.Post!=null)pending.Post.Dispose();}
                throw;
            }
        }

        private void ArmDirtyInteractionCapture(CheckpointSidecar sidecar)
        {
            if(!dirtyInteractionHookInstalled||sidecar==null)
                throw new InvalidOperationException("Dirty-interaction capture requires the installed hook and a checkpoint sidecar.");
            NativeDirtyInteractionReceipt receipt=CallDirtyInteractionAction(
                armDirtyInteractionCapture,"capture-arm",18);
            if(receipt.Armed!=1||receipt.Action!=1)
                throw new InvalidOperationException("Native dirty-interaction capture did not remain armed.");
            pendingDirtyCaptureSidecar=sidecar;
            pendingDirtyCaptureOrdinal=receipt.Captures+1;
        }

        private uint ReadObservedNPhaseCore(out uint observations)
        {
            if(!dirtyInteractionHookInstalled||lastObservedDirtyNPhase==null)
                throw new InvalidOperationException("Passive NPhaseCore observation requires the installed dirty-interaction hook.");
            UIntPtr nphase;
            int ok=lastObservedDirtyNPhase(new UIntPtr(unityPlayerBase),out nphase,out observations);
            uint value=nphase.ToUInt32();
            if(ok==0||value==0||observations==0)
                throw new InvalidOperationException("The dirty-interaction hook has not observed a current NPhaseCore yet.");
            return value;
        }

        private DirtyInteractionState CaptureDirtyInteractionStateReadOnly(uint nphase)
        {
            if(nphase==0||captureDirtyInteractionSnapshot==null)
                throw new InvalidOperationException(
                    "Stateless dirty-interaction capture requires one live NPhaseCore and native export.");
            NativeDirtyInteractionReceipt before=CallDirtyInteractionAction(
                statusDirtyInteractionOrder,"snapshot-status-before",1);
            int keySize=Marshal.SizeOf(typeof(NativeDirtyInteractionKey));
            int receiptSize=Marshal.SizeOf(typeof(NativeDirtyInteractionReceipt));
            IntPtr keysBuffer=Marshal.AllocHGlobal(MaximumDirtyInteractions*keySize);
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            NativeDirtyInteractionReceipt receipt;
            NativeDirtyInteractionKey[] keys;
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=captureDirtyInteractionSnapshot(new UIntPtr(unityPlayerBase),
                    new UIntPtr(nphase),keysBuffer,MaximumDirtyInteractions,receiptBuffer);
                receipt=(NativeDirtyInteractionReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeDirtyInteractionReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException(
                        "Native stateless dirty-interaction capture failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+".");
                if(receipt.Count>MaximumDirtyInteractions)
                    throw new InvalidOperationException(
                        "Native stateless dirty-interaction count exceeds the managed bound.");
                keys=new NativeDirtyInteractionKey[receipt.Count];
                for(int i=0;i<keys.Length;i++)keys[i]=(NativeDirtyInteractionKey)
                    Marshal.PtrToStructure(new IntPtr(keysBuffer.ToInt64()+i*keySize),
                        typeof(NativeDirtyInteractionKey));
            }
            finally
            {
                Marshal.FreeHGlobal(receiptBuffer);
                Marshal.FreeHGlobal(keysBuffer);
            }
            ValidateDirtyInteractionReceiptContract(receipt,receiptSize);
            RecordDirtyInteractionReceipt("snapshot-capture",receipt);
            NativeDirtyInteractionReceipt after=CallDirtyInteractionAction(
                statusDirtyInteractionOrder,"snapshot-status-after",1);
            if(!before.Equals(after))
                throw new InvalidOperationException(
                    "Stateless dirty-interaction capture changed the pending hook transaction receipt.");
            var state=new DirtyInteractionState {NPhaseCore=receipt.NPhaseCore.ToUInt32(),
                Entries=receipt.Entries.ToUInt32(),EntriesNext=receipt.EntriesNext.ToUInt32(),
                Hash=receipt.Hash.ToUInt32(),EntriesCapacity=receipt.EntriesCapacity,
                HashSize=receipt.HashSize,OrderHash=receipt.OrderHashAfter,Keys=keys};
            ValidateDirtyInteractionState(state);
            if(state.NPhaseCore!=nphase||DirtyInteractionOrderHash(keys)!=receipt.OrderHashAfter)
                throw new InvalidOperationException(
                    "Stateless dirty-interaction capture differs from its requested NPhaseCore or key order.");
            return state;
        }

        private void FinalizePendingDirtyInteractionCapture()
        {
            try{FinalizePendingDirtyInteractionCaptureCore();}
            catch
            {
                try{CancelCheckpointObservationWork();}catch{}
                throw;
            }
        }

        private void FinalizePendingDirtyInteractionCaptureCore()
        {
            if(pendingDirtyCaptureSidecar==null)return;
            if(pendingFinishBroadPhaseOrdinal==0)
                throw new InvalidOperationException("Pending checkpoint capture lost its finishBroadPhase observation ordinal.");
            NativeDirtyInteractionReceipt status=CallDirtyInteractionAction(
                statusDirtyInteractionOrder,"capture-status");
            if(status.Result!=1||status.Armed!=0||status.Action!=0||
                status.Captures!=pendingDirtyCaptureOrdinal)
                throw new InvalidOperationException("Native dirty-interaction capture did not complete exactly once: result="+
                    status.Result+", armed="+status.Armed+", captures="+status.Captures+".");
            if(status.Count>MaximumDirtyInteractions)
                throw new InvalidOperationException("Native dirty-interaction capture count exceeds the supported bound.");
            FinishBroadPhaseState finishBroadPhase=CopyFinishBroadPhaseCapture(
                pendingDirtyCaptureSidecar,pendingFinishBroadPhaseOrdinal);
            IslandTransitionState islandTransition=CopyIslandTransitionCapture(
                pendingDirtyCaptureSidecar);
            int keySize=Marshal.SizeOf(typeof(NativeDirtyInteractionKey));
            int receiptSize=Marshal.SizeOf(typeof(NativeDirtyInteractionReceipt));
            IntPtr keysBuffer=Marshal.AllocHGlobal(Math.Max(1,(int)status.Count)*keySize);
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            NativeDirtyInteractionReceipt receipt;
            NativeDirtyInteractionKey[] keys=new NativeDirtyInteractionKey[status.Count];
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=copyDirtyInteractionCapture(new UIntPtr(unityPlayerBase),keysBuffer,status.Count,receiptBuffer);
                receipt=(NativeDirtyInteractionReceipt)Marshal.PtrToStructure(receiptBuffer,typeof(NativeDirtyInteractionReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native dirty-interaction capture-copy failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+".");
                for(int i=0;i<keys.Length;i++)keys[i]=(NativeDirtyInteractionKey)Marshal.PtrToStructure(
                    new IntPtr(keysBuffer.ToInt32()+i*keySize),typeof(NativeDirtyInteractionKey));
            }
            finally{Marshal.FreeHGlobal(receiptBuffer);Marshal.FreeHGlobal(keysBuffer);}
            ValidateDirtyInteractionReceiptContract(receipt,receiptSize);
            RecordDirtyInteractionReceipt("capture-copy",receipt);
            var state=new DirtyInteractionState {NPhaseCore=receipt.NPhaseCore.ToUInt32(),
                Entries=receipt.Entries.ToUInt32(),EntriesNext=receipt.EntriesNext.ToUInt32(),
                Hash=receipt.Hash.ToUInt32(),EntriesCapacity=receipt.EntriesCapacity,
                HashSize=receipt.HashSize,OrderHash=receipt.OrderHashAfter,Keys=keys};
            ValidateDirtyInteractionState(state);
            if(DirtyInteractionOrderHash(keys)!=receipt.OrderHashAfter)
                throw new InvalidOperationException("Managed dirty-interaction order hash differs from native capture.");
            if(pendingDirtyCaptureSidecar.ShapeInstancePairPool==null||
                pendingDirtyCaptureSidecar.ShapeInstancePairPool.NPhaseCore!=state.NPhaseCore)
                throw new InvalidOperationException("Dirty-interaction capture completed for a different NPhaseCore than the sealed checkpoint allocator state.");
            pendingDirtyCaptureSidecar.DirtyInteractions=state;
            pendingDirtyCaptureSidecar.FinishBroadPhase=finishBroadPhase;
            pendingDirtyCaptureSidecar.IslandTransition=islandTransition;
            StoreCheckpointSidecar(pendingDirtyCaptureSidecar);
            CompleteIslandObservationWork();
            pendingDirtyCaptureSidecar=null;pendingDirtyCaptureOrdinal=0;
            pendingIslandObservation=null;
            pendingFinishBroadPhaseOrdinal=0;
            dirtyInteractionCaptures++;finishBroadPhaseCaptures++;islandTransitionCaptures++;
        }

        private IslandTransitionState CopyIslandTransitionCapture(CheckpointSidecar sidecar)
        {
            PendingIslandObservation pending=pendingIslandObservation;
            if(pending==null||!ReferenceEquals(pending.Sidecar,sidecar))
                throw new InvalidOperationException("Pending island observation does not belong to the sealed checkpoint.");
            NativeIslandObserverReceipt status=CallIslandObserverAction(
                statusIslandObserver,"capture-status",1);
            // The arm is published from Unity's output callback, while PhysX
            // may run the matching island task on a dispatcher worker.  The
            // armed thread is diagnostic; the callback thread is bound by the
            // exact manager/pass/ordinal and both embedded snapshot receipts.
            if(status.Installed!=1||status.State!=4||status.ExpectedPass!=0||status.Pass!=0||
                status.ArmedOrdinal!=pending.ArmedOrdinal||status.ObservationOrdinal!=pending.ArmedOrdinal||
                status.ObserverSequence!=pending.ObserverSequence||status.ArmedThreadId!=pending.ArmedThreadId||
                status.ThreadId==0||status.InFlight!=0||status.ValidationFlags!=0x7Fu)
                throw new InvalidOperationException("Native island observation did not complete exactly once: state="+
                    status.State+", armedOrdinal="+status.ArmedOrdinal+", observationOrdinal="+
                    status.ObservationOrdinal+", inFlight="+status.InFlight+".");
            IslandTransitionState first=null,second=null;
            try
            {
                first=CopyIslandTransitionOnce(sidecar,pending,"capture-copy-first");
                second=CopyIslandTransitionOnce(sidecar,pending,"capture-copy-second");
                if(!SameIslandTransitionState(first,second))
                    throw new InvalidOperationException("Two exact-ordinal island copies were not byte-equivalent.");
                return first;
            }
            catch
            {
                CancelIslandObservationWork();
                throw;
            }
        }

        private IslandTransitionState CopyIslandTransitionOnce(CheckpointSidecar sidecar,
            PendingIslandObservation pending,string action)
        {
            using(IslandSnapshotBufferOwner pre=IslandSnapshotBufferOwner.Create())
            using(IslandSnapshotBufferOwner post=IslandSnapshotBufferOwner.Create())
            {
                int size=Marshal.SizeOf(typeof(NativeIslandObserverReceipt));
                IntPtr receiptBuffer=Marshal.AllocHGlobal(size);NativeIslandObserverReceipt receipt;
                try
                {
                    IslandSnapshotBufferOwner.Zero(receiptBuffer,size);
                    int ok=copyIslandObserver(new UIntPtr(unityPlayerBase),pending.ArmedOrdinal,
                        pre.BuffersPointer,pre.ReceiptPointer,post.BuffersPointer,post.ReceiptPointer,
                        receiptBuffer);
                    receipt=(NativeIslandObserverReceipt)Marshal.PtrToStructure(
                        receiptBuffer,typeof(NativeIslandObserverReceipt));
                    if(ok==0||receipt.Result!=1)
                        throw new InvalidOperationException("Native island observation "+action+
                            " failed: result="+receipt.Result+", Win32/error="+receipt.LastError+
                            ", state="+receipt.State+".");
                }
                finally{Marshal.FreeHGlobal(receiptBuffer);}
                ValidateIslandObserverReceiptContract(receipt);
                NativeIslandSnapshotReceipt preReceipt=(NativeIslandSnapshotReceipt)Marshal.PtrToStructure(
                    pre.ReceiptPointer,typeof(NativeIslandSnapshotReceipt));
                NativeIslandSnapshotReceipt postReceipt=(NativeIslandSnapshotReceipt)Marshal.PtrToStructure(
                    post.ReceiptPointer,typeof(NativeIslandSnapshotReceipt));
                IslandSnapshotState preState=ReadIslandSnapshotState(pre,preReceipt);
                IslandSnapshotState postState=ReadIslandSnapshotState(post,postReceipt);
                ValidateIslandTransitionReceipts(sidecar,pending,receipt,preState,postState);
                NativeIslandJournalReceipt journalReceipt;
                byte[] journalRaw;
                NativeIslandJournalRecord[] journal=CopyIslandJournalOnce(
                    receipt.JournalBeginOrdinal,receipt.JournalEndOrdinal,pending.ExpectedManager,
                    out journalReceipt,out journalRaw);
                ValidateIslandJournalBindingCoherence(sidecar,preState,postState,journal);
                return new IslandTransitionState {Receipt=receipt,Pre=preState,Post=postState,
                    JournalReceipt=journalReceipt,Journal=journal,JournalRawBytes=journalRaw};
            }
        }

        private IslandTransitionState CopyIslandTransitionAudit(CheckpointSidecar sidecar)
        {
            PendingIslandObservation pending=pendingIslandObservation;
            if(pending==null||!ReferenceEquals(pending.Sidecar,sidecar))
                throw new InvalidOperationException("Pending island audit does not belong to the restored checkpoint.");
            NativeIslandObserverReceipt status=CallIslandObserverAction(
                statusIslandObserver,"audit-status",1);
            if(status.Installed!=1||status.State!=4||status.ExpectedPass!=0||status.Pass!=0||
                status.ArmedOrdinal!=pending.ArmedOrdinal||status.ObservationOrdinal!=pending.ArmedOrdinal||
                status.ObserverSequence!=pending.ObserverSequence||status.ArmedThreadId!=pending.ArmedThreadId||
                status.ThreadId==0||status.InFlight!=0||status.ValidationFlags!=0x7Fu)
                throw new InvalidOperationException("Native first-replay island audit did not complete exactly once: state="+
                    status.State+", armedOrdinal="+status.ArmedOrdinal+", observationOrdinal="+
                    status.ObservationOrdinal+", inFlight="+status.InFlight+".");
            IslandTransitionState first=null,second=null;
            try
            {
                first=CopyIslandTransitionAuditOnce(sidecar,pending,"audit-copy-first");
                second=CopyIslandTransitionAuditOnce(sidecar,pending,"audit-copy-second");
                if(!SameIslandTransitionState(first,second))
                    throw new InvalidOperationException("Two first-replay island audit copies were not byte-equivalent.");
                return first;
            }
            catch
            {
                CancelIslandObservationWork();
                throw;
            }
        }

        private IslandTransitionState CopyIslandTransitionAuditOnce(CheckpointSidecar sidecar,
            PendingIslandObservation pending,string action)
        {
            using(IslandSnapshotBufferOwner pre=IslandSnapshotBufferOwner.Create())
            using(IslandSnapshotBufferOwner post=IslandSnapshotBufferOwner.Create())
            {
                int size=Marshal.SizeOf(typeof(NativeIslandObserverReceipt));
                IntPtr receiptBuffer=Marshal.AllocHGlobal(size);NativeIslandObserverReceipt receipt;
                try
                {
                    IslandSnapshotBufferOwner.Zero(receiptBuffer,size);
                    int ok=copyIslandObserver(new UIntPtr(unityPlayerBase),pending.ArmedOrdinal,
                        pre.BuffersPointer,pre.ReceiptPointer,post.BuffersPointer,post.ReceiptPointer,
                        receiptBuffer);
                    receipt=(NativeIslandObserverReceipt)Marshal.PtrToStructure(
                        receiptBuffer,typeof(NativeIslandObserverReceipt));
                    if(ok==0||receipt.Result!=1)
                        throw new InvalidOperationException("Native first-replay island audit "+action+
                            " failed: result="+receipt.Result+", Win32/error="+receipt.LastError+
                            ", state="+receipt.State+".");
                }
                finally{Marshal.FreeHGlobal(receiptBuffer);}
                ValidateIslandObserverReceiptContract(receipt);
                NativeIslandSnapshotReceipt preReceipt=(NativeIslandSnapshotReceipt)Marshal.PtrToStructure(
                    pre.ReceiptPointer,typeof(NativeIslandSnapshotReceipt));
                NativeIslandSnapshotReceipt postReceipt=(NativeIslandSnapshotReceipt)Marshal.PtrToStructure(
                    post.ReceiptPointer,typeof(NativeIslandSnapshotReceipt));
                IslandSnapshotState preState=ReadIslandSnapshotState(pre,preReceipt);
                IslandSnapshotState postState=ReadIslandSnapshotState(post,postReceipt);
                ValidateIslandTransitionAuditReceipts(sidecar,pending,receipt,preState,postState);
                NativeIslandJournalReceipt journalReceipt;
                byte[] journalRaw;
                NativeIslandJournalRecord[] journal=CopyIslandJournalOnce(
                    receipt.JournalBeginOrdinal,receipt.JournalEndOrdinal,pending.ExpectedManager,
                    out journalReceipt,out journalRaw);
                return new IslandTransitionState {Receipt=receipt,Pre=preState,Post=postState,
                    JournalReceipt=journalReceipt,Journal=journal,JournalRawBytes=journalRaw};
            }
        }

        private NativeIslandJournalRecord[] CopyIslandJournalOnce(uint begin,uint end,
            uint expectedManager,out NativeIslandJournalReceipt receipt,out byte[] raw)
        {
            if(copyIslandJournal==null||end<begin||end-begin>MaximumIslandJournalRecords)
                throw new InvalidOperationException("Island journal interval is unavailable or outside the supported bound.");
            int recordSize=Marshal.SizeOf(typeof(NativeIslandJournalRecord));
            int receiptSize=Marshal.SizeOf(typeof(NativeIslandJournalReceipt));
            IntPtr records=Marshal.AllocHGlobal(MaximumIslandJournalRecords*recordSize);
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            try
            {
                IslandSnapshotBufferOwner.Zero(receiptBuffer,receiptSize);
                int ok=copyIslandJournal(new UIntPtr(unityPlayerBase),begin,records,
                    MaximumIslandJournalRecords,receiptBuffer);
                receipt=(NativeIslandJournalReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeIslandJournalReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native island journal copy failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+".");
                if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=84u||
                    receipt.UnityBase.ToUInt32()!=unityPlayerBase||
                    receipt.ExpectedManager.ToUInt32()!=expectedManager||receipt.Installed!=1||
                    receipt.FirstOrdinal>begin||receipt.RequestedBegin!=begin||
                    receipt.NextOrdinal<end||receipt.RecordsRequired!=receipt.RecordsWritten||
                    receipt.RecordsWritten>MaximumIslandJournalRecords||
                    receipt.ValidationFlags!=0x1Fu||receipt.InvalidKind!=0||receipt.Detail!=0)
                    throw new InvalidOperationException("Native island journal receipt is incomplete or incoherent.");
                NativeIslandJournalRecord[] all=ReadStructArray<NativeIslandJournalRecord>(
                    records,receipt.RecordsWritten);
                int desired=checked((int)(end-begin));
                NativeIslandJournalRecord[] selected=all.Where(value=>value.Ordinal>=begin&&value.Ordinal<end).ToArray();
                if(selected.Length!=desired)
                    throw new InvalidOperationException("Island journal does not cover the exact observer interval.");
                for(int i=0;i<selected.Length;i++)
                {
                    NativeIslandJournalRecord value=selected[i];
                    if(value.Ordinal!=begin+(uint)i||value.ValidationFlags!=0x1Fu||
                        (value.EventKind!=1&&value.EventKind!=2)||value.ThreadId==0||value.EdgeType>2)
                        throw new InvalidOperationException("Island journal record ordering or validation differs.");
                    if(value.EdgeType==0&&(value.HookAddress==0||value.OwnerObject==0||
                        value.PxsLow==0||value.PxsHigh==0||value.PxsLow>=value.PxsHigh))
                        throw new InvalidOperationException("Contact island journal record lacks its semantic SIP binding.");
                }
                raw=new byte[checked(selected.Length*recordSize)];
                if(raw.Length!=0)Marshal.Copy(records,raw,0,raw.Length);
                return selected;
            }
            finally{Marshal.FreeHGlobal(receiptBuffer);Marshal.FreeHGlobal(records);}
        }

        private FinishBroadPhaseState CopyFinishBroadPhaseCapture(
            CheckpointSidecar sidecar,uint expectedOrdinal)
        {
            NativeFinishBroadPhaseObserverReceipt status=CallFinishBroadPhaseObserverAction(
                statusFinishBroadPhaseObserver,"capture-status");
            if(status.Installed!=1||status.State!=4||status.ExpectedPass!=0||status.Pass!=0||
                status.ArmedOrdinal!=expectedOrdinal||status.ObservationOrdinal!=expectedOrdinal||
                status.CreatedRequired>MaximumBroadPhaseOverlaps||
                status.DeletedRequired>MaximumBroadPhaseOverlaps||
                status.DroppedObservations!=0)
                throw new InvalidOperationException("Native finishBroadPhase observation did not complete exactly once: state="+
                    status.State+", armedOrdinal="+status.ArmedOrdinal+", observationOrdinal="+
                    status.ObservationOrdinal+", dropped="+status.DroppedObservations+".");
            FinishBroadPhaseState first=CopyFinishBroadPhaseCaptureOnce(sidecar,expectedOrdinal,
                status.CreatedRequired,status.DeletedRequired,"capture-copy-first");
            FinishBroadPhaseState second=CopyFinishBroadPhaseCaptureOnce(sidecar,expectedOrdinal,
                status.CreatedRequired,status.DeletedRequired,"capture-copy-second");
            if(!SameFinishBroadPhaseState(first,second))
                throw new InvalidOperationException("Two exact-ordinal finishBroadPhase copies were not byte-equivalent.");
            return first;
        }

        private FinishBroadPhaseState CopyFinishBroadPhaseCaptureOnce(
            CheckpointSidecar sidecar,uint expectedOrdinal,uint createdCount,uint deletedCount,
            string action)
        {
            int receiptSize=Marshal.SizeOf(typeof(NativeFinishBroadPhaseObserverReceipt));
            int overlapSize=Marshal.SizeOf(typeof(NativeBroadPhaseOverlapRecord));
            if(receiptSize!=152||overlapSize!=40)
                throw new InvalidOperationException("Managed finishBroadPhase capture ABI size differs.");
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            IntPtr createdBuffer=Marshal.AllocHGlobal(Math.Max(1,(int)createdCount)*overlapSize);
            IntPtr deletedBuffer=Marshal.AllocHGlobal(Math.Max(1,(int)deletedCount)*overlapSize);
            NativeFinishBroadPhaseObserverReceipt receipt;
            NativeBroadPhaseOverlapRecord[] created=new NativeBroadPhaseOverlapRecord[createdCount];
            NativeBroadPhaseOverlapRecord[] deleted=new NativeBroadPhaseOverlapRecord[deletedCount];
            try
            {
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=copyFinishBroadPhaseObserver(new UIntPtr(unityPlayerBase),expectedOrdinal,
                    createdBuffer,createdCount,deletedBuffer,deletedCount,receiptBuffer);
                receipt=(NativeFinishBroadPhaseObserverReceipt)Marshal.PtrToStructure(
                    receiptBuffer,typeof(NativeFinishBroadPhaseObserverReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native finishBroadPhase capture-copy failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+", state="+receipt.State+".");
                for(int i=0;i<created.Length;i++)created[i]=(NativeBroadPhaseOverlapRecord)
                    Marshal.PtrToStructure(new IntPtr(createdBuffer.ToInt64()+i*overlapSize),
                        typeof(NativeBroadPhaseOverlapRecord));
                for(int i=0;i<deleted.Length;i++)deleted[i]=(NativeBroadPhaseOverlapRecord)
                    Marshal.PtrToStructure(new IntPtr(deletedBuffer.ToInt64()+i*overlapSize),
                        typeof(NativeBroadPhaseOverlapRecord));
            }
            finally
            {
                Marshal.FreeHGlobal(deletedBuffer);Marshal.FreeHGlobal(createdBuffer);
                Marshal.FreeHGlobal(receiptBuffer);
            }
            ValidateFinishBroadPhaseReceiptContract(receipt,receiptSize);
            RecordFinishBroadPhaseReceipt(action,receipt);
            var state=new FinishBroadPhaseState {Receipt=receipt,Created=created,Deleted=deleted};
            ValidateFinishBroadPhaseState(state,sidecar);
            return state;
        }

        private void ArmDirtyInteractionRestore(DirtyInteractionState state)
        {
            ValidateDirtyInteractionState(state);
            int keySize=Marshal.SizeOf(typeof(NativeDirtyInteractionKey));
            int receiptSize=Marshal.SizeOf(typeof(NativeDirtyInteractionReceipt));
            IntPtr keysBuffer=Marshal.AllocHGlobal(Math.Max(1,state.Keys.Length)*keySize);
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize);
            NativeDirtyInteractionReceipt receipt;
            try
            {
                for(int i=0;i<state.Keys.Length;i++)Marshal.StructureToPtr(state.Keys[i],
                    new IntPtr(keysBuffer.ToInt32()+i*keySize),false);
                for(int i=0;i<receiptSize;i++)Marshal.WriteByte(receiptBuffer,i,0);
                int ok=armDirtyInteractionRestore(new UIntPtr(unityPlayerBase),new UIntPtr(state.NPhaseCore),
                    new UIntPtr(state.Entries),new UIntPtr(state.EntriesNext),new UIntPtr(state.Hash),
                    state.EntriesCapacity,state.HashSize,keysBuffer,(uint)state.Keys.Length,
                    dirtyInteractionRestoreMode,receiptBuffer);
                receipt=(NativeDirtyInteractionReceipt)Marshal.PtrToStructure(receiptBuffer,typeof(NativeDirtyInteractionReceipt));
                if(ok==0||receipt.Result!=18)
                    throw new InvalidOperationException("Native dirty-interaction restore-arm failed: result="+
                        receipt.Result+", Win32/error="+receipt.LastError+".");
            }
            finally{Marshal.FreeHGlobal(receiptBuffer);Marshal.FreeHGlobal(keysBuffer);}
            ValidateDirtyInteractionReceiptContract(receipt,receiptSize);
            RecordDirtyInteractionReceipt("restore-arm",receipt);
            if(receipt.Armed!=1||receipt.Action!=2||
                receipt.RestoreMode!=dirtyInteractionRestoreMode||
                receipt.NPhaseCore.ToUInt32()!=state.NPhaseCore||
                receipt.Count!=(uint)state.Keys.Length||receipt.OrderHashAfter!=state.OrderHash)
                throw new InvalidOperationException("Native dirty-interaction restore arm differs from the selected sidecar.");
            pendingDirtyRestoreState=state;pendingDirtyRestoreOrdinal=receipt.Restores+1;
            dirtyRestorePendingValidation=true;
        }

        private void ValidatePendingDirtyInteractionRestore()
        {
            if(!dirtyRestorePendingValidation)return;
            NativeDirtyInteractionReceipt receipt=CallDirtyInteractionAction(
                statusDirtyInteractionOrder,"restore-status");
            bool accounting=receipt.MatchedCount+receipt.CapturedOnlyCount==
                    (uint)(pendingDirtyRestoreState==null?0:pendingDirtyRestoreState.Keys.Length)&&
                receipt.MatchedCount+receipt.LiveOnlyCount==receipt.Count;
            bool exact=receipt.CapturedOnlyCount==0&&receipt.LiveOnlyCount==0&&
                pendingDirtyRestoreState!=null&&receipt.OrderHashAfter==pendingDirtyRestoreState.OrderHash;
            if(receipt.Result!=1||receipt.Armed!=0||receipt.Action!=0||
                receipt.RestoreMode!=dirtyInteractionRestoreMode||
                receipt.Restores!=pendingDirtyRestoreOrdinal||pendingDirtyRestoreState==null||
                !accounting||
                (dirtyInteractionRestoreMode==DirtyInteractionRestoreExact&&!exact)||
                (dirtyInteractionRestoreMode==DirtyInteractionRestoreProjection&&
                    receipt.CapturedOnlyCount==0&&receipt.LiveOnlyCount==0&&!exact))
                throw new InvalidOperationException("Native dirty-interaction restore did not complete exactly once: result="+
                    receipt.Result+", armed="+receipt.Armed+", restores="+receipt.Restores+".");
            dirtyInteractionRestores++;
            dirtyRestorePendingValidation=false;pendingDirtyRestoreOrdinal=0;pendingDirtyRestoreState=null;
        }

        private void ValidateDirtyInteractionReceiptContract(NativeDirtyInteractionReceipt receipt,int size)
        {
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||receipt.UnityBase.ToUInt32()!=unityPlayerBase)
                throw new InvalidOperationException("Native dirty-interaction receipt contract differs.");
        }

        private void RecordDirtyInteractionReceipt(string action,NativeDirtyInteractionReceipt receipt)
        {
            dirtyInteractionReceipts.Add(new Dictionary<string,object>{{"action",action},
                {"result",receipt.Result},{"lastError",receipt.LastError},{"nphaseCore",Hex(receipt.NPhaseCore)},
                {"set",Hex(receipt.Set)},{"entries",Hex(receipt.Entries)},{"entriesNext",Hex(receipt.EntriesNext)},
                {"hash",Hex(receipt.Hash)},{"entriesCapacity",receipt.EntriesCapacity},{"hashSize",receipt.HashSize},
                {"count",receipt.Count},{"nativeAction",receipt.Action},{"captures",receipt.Captures},
                {"restores",receipt.Restores},{"orderHashBefore","0x"+receipt.OrderHashBefore.ToString("X8")},
                {"orderHashAfter","0x"+receipt.OrderHashAfter.ToString("X8")},
                {"installed",receipt.Installed!=0},{"armed",receipt.Armed!=0},
                {"restoreMode",receipt.RestoreMode},{"matchedCount",receipt.MatchedCount},
                {"capturedOnlyCount",receipt.CapturedOnlyCount},{"liveOnlyCount",receipt.LiveOnlyCount}});
            if(dirtyInteractionReceipts.Count>24)dirtyInteractionReceipts.RemoveAt(0);
        }

        private NativeContextObserverReceipt RunContextObserver(NativeContextObserverAction callback,string action)
        {
            if(callback==null)throw new InvalidOperationException("Native context observer export is unavailable.");
            int size=Marshal.SizeOf(typeof(NativeContextObserverReceipt));
            IntPtr buffer=Marshal.AllocHGlobal(size);NativeContextObserverReceipt receipt;
            try
            {
                for(int i=0;i<size;i++)Marshal.WriteByte(buffer,i,0);
                int ok=callback(new UIntPtr(unityPlayerBase),buffer);
                receipt=(NativeContextObserverReceipt)Marshal.PtrToStructure(buffer,typeof(NativeContextObserverReceipt));
                if(ok==0||receipt.Result!=1)
                    throw new InvalidOperationException("Native context observer "+action+" failed: result="+receipt.Result+", Win32/error="+receipt.LastError+".");
            }
            finally{Marshal.FreeHGlobal(buffer);}
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||receipt.UnityBase.ToUInt32()!=unityPlayerBase)
                throw new InvalidOperationException("Native context observer receipt contract differs.");
            lastContextObserverReceipt=new Dictionary<string,object>{{"action",action},{"unityBase",Hex(receipt.UnityBase)},
                {"observedContext",Hex(receipt.ObservedContext)},{"observations",receipt.Observations},{"installed",receipt.Installed!=0}};
            contextObservations=receipt.Observations;
            return receipt;
        }

        private void RefreshObservedContactManagerContext()
        {
            if(!contextObserverInstalled)return;
            var receipt=RunContextObserver(statusContextObserver,"status");
            if(receipt.Observations==contextObservationFloor)return;
            contextObservationFloor=receipt.Observations;
            uint observed=receipt.ObservedContext.ToUInt32();
            if(observed==0)return;
            if(observed!=contactManagerContext)
            {
                uint previous=contactManagerContext;
                contactManagerContext=observed;
                // A nonzero context replacement invalidates every scene-owned
                // transaction, including scheduled or armed work that has not
                // published a sidecar yet.  The initial 0 -> observed discovery
                // is not a replacement and must preserve setup state.
                if(previous!=0)
                {
                    contextSnapshotInvalidations++;
                    ResetSceneOwnedCheckpointState("contact-manager-context-changed",false);
                }
            }
            if(islandObserverInstalled&&islandObserverManager!=checked(observed+0x181Cu))
                RebindIslandObserver(checked(observed+0x181Cu));
        }

        private void EnsureIslandObserverForCurrentContext()
        {
            if(contactManagerContext==0)
                throw new InvalidOperationException("Island observer installation requires an observed or explicit PxsContext.");
            uint expectedManager=checked(contactManagerContext+0x181Cu);
            if(islandObserverInstalled)
            {
                if(islandObserverManager!=expectedManager)RebindIslandObserver(expectedManager);
                return;
            }
            NativeIslandObserverReceipt installed=InstallIslandObserver(expectedManager);
            if(installed.Result!=1||installed.Installed!=1||installed.State!=1||installed.InFlight!=0)
                throw new InvalidOperationException("Native island observer did not install idle and quiescent.");
        }

        private void RebindIslandObserver(uint expectedManager)
        {
            if(!islandObserverInstalled||islandObserverManager==expectedManager)return;
            CancelCheckpointObservationWork();
            NativeIslandObserverReceipt receipt=CallIslandObserverAction(
                uninstallIslandObserver,"context-rebind-uninstall",1,9);
            if(receipt.Result!=1||receipt.Installed!=1||receipt.State!=0||receipt.InFlight!=0)
                throw new InvalidOperationException("Island observer context rebind is waiting for dormant quiescence.");
            islandObserverInstalled=false;islandObserverManager=0;
            NativeIslandObserverReceipt installed=InstallIslandObserver(expectedManager);
            if(installed.Result!=1||installed.State!=1||installed.InFlight!=0)
                throw new InvalidOperationException("Island observer did not reactivate for the new PxsContext.");
        }

        private void ObserveSceneGeneration()
        {
            int current=NativeSceneMetadata.Refreshes;
            if(sceneMetadataGeneration==current)return;
            int previous=sceneMetadataGeneration;
            sceneMetadataGeneration=current;
            ResetSceneOwnedCheckpointState("native-scene-metadata-generation-changed",true);
            lastSceneOwnedReset=new Dictionary<string,object>{{"reason","native-scene-metadata-generation-changed"},
                {"previousGeneration",previous},{"currentGeneration",current}};
        }

        private void ObserveCoreRoundIdentity()
        {
            object current=CoreRoundIdentity();
            if(ReferenceEquals(current,coreRoundIdentity))return;
            coreRoundIdentity=current;
            ResetSceneOwnedCheckpointState("native-kitchen-round-identity-changed",true);
        }

        private void ResetSceneOwnedCheckpointState(string reason,bool clearFailure)
        {
            uint ignored;
            try{ReadObservedNPhaseCore(out ignored);dirtyNPhaseObservationFloor=ignored;}
            catch{dirtyNPhaseObservationFloor=0;}
            CancelContactRecreateWork();
            CancelCheckpointObservationWork();
            pendingContactPoolAction=0;pendingContactPoolFrame=-1;pendingCoreSnapshot=null;
            scheduledContactPoolCaptureFrame=-1;scheduledContactPoolLastObservedFrame=-1;
            checkpointSidecars.Clear();warpTargetSidecar=null;automaticRestorePending=false;
            warpInProgress=false;warpTargetRestoreEligible=false;warpTargetFrame=-1;
            lastIslandTransitionAudit=null;
            lastFinishBroadPhaseTransitionAudit=null;
            lastTargetPostTransitionAudit=null;
            lastRestoredPostTransitionAudit=null;
            lastIslandTransitionAuditCheckpointFrame=-1;
            lastIslandTransitionAuditTransitionFrame=-1;
            lastIslandTransitionAuditCapturedAtOutputFrame=-1;
            if(clearFailure)failure=null;
            sceneOwnedResets++;
            lastSceneOwnedReset=new Dictionary<string,object>{{"reason",reason},
                {"generation",sceneMetadataGeneration},{"clearedFailure",clearFailure}};
        }

        private void CancelDirtyInteractionWork()
        {
            if(dirtyInteractionHookInstalled)
                CallDirtyInteractionAction(cancelDirtyInteractionOrder,"cancel");
            pendingDirtyCaptureSidecar=null;pendingDirtyCaptureOrdinal=0;
            dirtyRestorePendingValidation=false;pendingDirtyRestoreOrdinal=0;pendingDirtyRestoreState=null;
        }

        private void CompleteIslandObservationWork()
        {
            // Copy proves the committed bytes stable, but native deliberately
            // retains the armed pointers until a lifecycle fence clears them.
            // Reuse the cancellation fence so HGlobal memory is released only
            // after Idle/inFlight=0 is proven.
            CancelIslandObservationWork();
        }

        private void CancelFirstReplayTransitionObservationWork()
        {
            try
            {
                if(finishBroadPhaseObserverInstalled)
                    CallFinishBroadPhaseObserverAction(cancelFinishBroadPhaseObserver,"audit-cancel");
            }
            finally
            {
                pendingFinishBroadPhaseOrdinal=0;
                CancelIslandObservationWork();
            }
        }

        private void CancelIslandObservationWork()
        {
            PendingIslandObservation pending=pendingIslandObservation;
            if(pending==null)return;
            NativeIslandObserverReceipt receipt=new NativeIslandObserverReceipt();
            bool safe=false;
            try
            {
                // BUSY means a wrapper still owns the caller buffers.  Retry a
                // bounded number of nonblocking lifecycle fences; if native
                // cannot prove quiescence, retain the allocations process-wide.
                for(int attempt=0;attempt<3;attempt++)
                {
                    receipt=CallIslandObserverAction(cancelIslandObserver,"cancel",1,9);
                    if(receipt.Result==1&&receipt.State==1&&receipt.InFlight==0){safe=true;break;}
                    if(receipt.Result!=9)break;
                }
            }
            finally
            {
                pendingIslandObservation=null;
                pendingIslandTransitionAuditSidecar=null;
                if(safe){pending.Pre.Dispose();pending.Post.Dispose();}
                else lock(processRetainedIslandBuffers)processRetainedIslandBuffers.Add(pending);
            }
        }

        private void CancelCheckpointObservationWork()
        {
            try
            {
                if(finishBroadPhaseObserverInstalled)
                    CallFinishBroadPhaseObserverAction(cancelFinishBroadPhaseObserver,"cancel");
            }
            finally
            {
                try{CancelIslandObservationWork();}
                finally
                {
                    pendingFinishBroadPhaseOrdinal=0;
                    CancelDirtyInteractionWork();
                }
            }
        }

        private static int CurrentCheckpointFrame()
        {
            var field=typeof(NativeKitchenCheckpoint).GetField("lastFrame",BindingFlags.Static|BindingFlags.NonPublic);
            if(field==null||field.FieldType!=typeof(int))throw new InvalidOperationException("Installed native checkpoint frame contract differs.");
            return (int)field.GetValue(null);
        }

        private static object CoreCheckpointSnapshot(int frame)
        {
            var field=typeof(NativeKitchenCheckpoint).GetField("history",BindingFlags.Static|BindingFlags.NonPublic);
            var values=field==null?null:field.GetValue(null) as IDictionary;
            if(values==null)throw new InvalidOperationException("Installed native checkpoint history contract differs.");
            return frame>=0&&values.Contains(frame)?values[frame]:null;
        }

        private static object CoreRoundIdentity()
        {
            var field=typeof(NativeKitchenCheckpoint).GetField("roundIdentity",BindingFlags.Static|BindingFlags.NonPublic);
            if(field==null)throw new InvalidOperationException("Installed native checkpoint round-identity contract differs.");
            return field.GetValue(null);
        }

        private static object RestorePlanSnapshot(NativeKitchenCheckpoint.RestorePlan plan)
        {
            var field=typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot",BindingFlags.Instance|BindingFlags.NonPublic);
            if(field==null)throw new InvalidOperationException("Installed native restore-plan contract differs.");
            return field.GetValue(plan);
        }

        private static uint ContactPoolOrderHash(uint[] values)
        {
            if(values==null)throw new ArgumentNullException("values");
            uint hash=2166136261u;
            foreach(uint value in values){hash^=value;hash*=16777619u;}
            return hash;
        }

        private static uint ByteHash(byte[] values,int count)
        {
            if(values==null||count<0||count>values.Length)throw new ArgumentOutOfRangeException("count");
            uint hash=2166136261u;
            for(int i=0;i<count;i++){hash^=values[i];hash*=16777619u;}
            return hash;
        }

        private static uint AppendByteHash(uint hash,byte value)
        {
            hash^=value;return hash*16777619u;
        }

        private static uint AppendUInt32ByteHash(uint hash,uint value)
        {
            for(int shift=0;shift<32;shift+=8)
                hash=AppendByteHash(hash,(byte)(value>>shift));
            return hash;
        }

        private static uint AppendUInt32ArrayByteHash(uint hash,uint[] values)
        {
            if(values==null)throw new ArgumentNullException("values");
            foreach(uint value in values)hash=AppendUInt32ByteHash(hash,value);
            return hash;
        }

        private static uint AppendByteArrayHash(uint hash,byte[] values)
        {
            if(values==null)throw new ArgumentNullException("values");
            foreach(byte value in values)hash=AppendByteHash(hash,value);
            return hash;
        }

        private static void NPhasePoolLayout(uint poolKind,out uint poolOffset,
            out uint elementSize,out uint slabSize)
        {
            if(poolKind==0u){poolOffset=0x90u;elementSize=0x18u;slabSize=0x300u;return;}
            if(poolKind==1u){poolOffset=0x2E0u;elementSize=0x44u;slabSize=0x880u;return;}
            if(poolKind==2u){poolOffset=0x408u;elementSize=0x3Cu;slabSize=0x780u;return;}
            if(poolKind==3u){poolOffset=0x530u;elementSize=0x24u;slabSize=0x480u;return;}
            if(poolKind==4u){poolOffset=0x658u;elementSize=0x28u;slabSize=0x500u;return;}
            throw new InvalidOperationException("Unsupported NPhase pool kind "+poolKind+".");
        }

        private static uint NPhasePoolSlotAddress(NPhasePoolImageState value,uint ordinal)
        {
            NativeNPhasePoolSnapshotReceipt receipt=value.Receipt;
            if(ordinal>=receipt.TotalSlots)throw new InvalidOperationException(
                "NPhase pool slot ordinal is outside its captured partition.");
            uint slab=ordinal/32u,element=ordinal%32u;
            return checked(value.SlabBases[slab]+element*receipt.ElementSize);
        }

        private static uint[] NPhasePoolFreeAddresses(NPhasePoolImageState value)
        {
            return value.FreeSlots.Select(slot=>NPhasePoolSlotAddress(value,slot)).ToArray();
        }

        private static uint[] NPhasePoolAllocatedAddresses(NPhasePoolImageState value)
        {
            var result=new List<uint>();
            for(uint slot=0;slot<value.Receipt.TotalSlots;slot++)
                if((value.AllocationWords[slot>>5]&(1u<<(int)(slot&31u)))!=0u)
                    result.Add(NPhasePoolSlotAddress(value,slot));
            return result.ToArray();
        }

        private static void ValidateNPhasePoolImages(NPhasePoolImageState[] values)
        {
            if(values==null||values.Length!=NPhasePoolKindCount)
                throw new InvalidOperationException(
                    "The complete NPhase-pool image set is incomplete.");
            uint nphase=0;
            for(uint kind=0;kind<NPhasePoolKindCount;kind++)
            {
                ValidateNPhasePoolImage(values[kind],kind);
                uint current=values[kind].Receipt.NPhaseCore.ToUInt32();
                if(kind==0u)nphase=current;
                else if(current!=nphase)throw new InvalidOperationException(
                    "Complete NPhase-pool images belong to different NPhaseCore instances.");
            }
        }

        private static void ValidateNPhasePoolImage(NPhasePoolImageState value,uint poolKind)
        {
            if(value==null||value.SlabBases==null||value.FreeSlots==null||
                value.AllocationWords==null||value.SlabBytes==null)
                throw new InvalidOperationException(
                    "A complete NPhase-pool image is incomplete.");
            NativeNPhasePoolSnapshotReceipt receipt=value.Receipt;
            uint poolOffset,elementSize,slabSize;
            NPhasePoolLayout(poolKind,out poolOffset,out elementSize,out slabSize);
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=152u||
                receipt.Result!=1u||receipt.LastError!=0u||
                receipt.UnityBase==UIntPtr.Zero||receipt.NPhaseCore==UIntPtr.Zero||
                receipt.PoolKind!=poolKind||receipt.PoolOffset!=poolOffset||
                receipt.Pool.ToUInt32()!=checked(receipt.NPhaseCore.ToUInt32()+poolOffset)||
                receipt.ElementSize!=elementSize||receipt.ElementsPerSlab!=32u||
                receipt.SlabSize!=slabSize||receipt.SlabCount>MaximumNPhasePoolSlabs||
                receipt.TotalSlots!=receipt.SlabCount*32u||
                receipt.Used+receipt.Unreleased!=receipt.TotalSlots||
                receipt.SlabCount>(receipt.SlabCapacityRaw&0x7FFFFFFFu)||
                receipt.SlabBasesRequired!=receipt.SlabCount||
                receipt.SlabBasesWritten!=receipt.SlabBasesRequired||
                receipt.FreeSlotsRequired!=receipt.Unreleased||
                receipt.FreeSlotsWritten!=receipt.FreeSlotsRequired||
                receipt.AllocationWordsRequired!=receipt.SlabCount||
                receipt.AllocationWordsWritten!=receipt.AllocationWordsRequired||
                receipt.SlabBytesRequired!=receipt.SlabCount*receipt.SlabSize||
                receipt.SlabBytesWritten!=receipt.SlabBytesRequired||
                receipt.ValidationFlags!=0xFFu||receipt.InvalidKind!=0xFFFFFFFFu||
                receipt.InvalidIndex!=0xFFFFFFFFu||receipt.Detail!=0u||
                value.SlabBases.Length!=receipt.SlabBasesWritten||
                value.FreeSlots.Length!=receipt.FreeSlotsWritten||
                value.AllocationWords.Length!=receipt.AllocationWordsWritten||
                value.SlabBytes.Length!=receipt.SlabBytesWritten)
                throw new InvalidOperationException(
                    "A complete NPhase-pool receipt or image contract differs.");
            if((receipt.SlabCount!=0u&&receipt.SlabsData==UIntPtr.Zero)||
                value.SlabBases.Any(pointer=>pointer==0u)||
                value.SlabBases.Distinct().Count()!=value.SlabBases.Length||
                value.FreeSlots.Any(slot=>slot>=receipt.TotalSlots)||
                value.FreeSlots.Distinct().Count()!=value.FreeSlots.Length)
                throw new InvalidOperationException(
                    "A complete NPhase-pool image contains an invalid slab or free slot.");
            uint[] freeAddresses=NPhasePoolFreeAddresses(value);
            uint expectedHead=freeAddresses.Length==0?0u:freeAddresses[0];
            if(receipt.FreeHead.ToUInt32()!=expectedHead||
                receipt.FreeHeadSlot!=(value.FreeSlots.Length==0?0xFFFFFFFFu:value.FreeSlots[0]))
                throw new InvalidOperationException(
                    "A complete NPhase-pool image disagrees with its free head.");
            uint allocated=0u;
            var freeSet=new HashSet<uint>(value.FreeSlots);
            for(uint slot=0;slot<receipt.TotalSlots;slot++)
            {
                bool marked=(value.AllocationWords[slot>>5]&
                    (1u<<(int)(slot&31u)))!=0u;
                if(marked)allocated++;
                if(marked==freeSet.Contains(slot))
                    throw new InvalidOperationException(
                        "A complete NPhase-pool bitmap disagrees with its free partition.");
            }
            if(allocated!=receipt.Used)
                throw new InvalidOperationException(
                    "A complete NPhase-pool bitmap has the wrong allocated count.");
            for(int index=0;index<value.FreeSlots.Length;index++)
            {
                uint slot=value.FreeSlots[index],slab=slot/32u,element=slot%32u;
                int byteOffset=checked((int)(slab*receipt.SlabSize+
                    element*receipt.ElementSize));
                uint next=BitConverter.ToUInt32(value.SlabBytes,byteOffset);
                uint expected=index+1<value.FreeSlots.Length?
                    NPhasePoolSlotAddress(value,value.FreeSlots[index+1]):0u;
                if(next!=expected)throw new InvalidOperationException(
                    "A complete NPhase-pool image has a free link inconsistent with its ordinal order.");
            }
            if(receipt.SlabBaseHash!=ContactPoolOrderHash(value.SlabBases)||
                receipt.FreeSlotOrderHash!=ContactPoolOrderHash(value.FreeSlots)||
                receipt.AllocationBitmapHash!=ContactPoolOrderHash(value.AllocationWords)||
                receipt.SlabByteHash!=ByteHash(value.SlabBytes,value.SlabBytes.Length))
                throw new InvalidOperationException(
                    "A complete NPhase-pool image differs from its native content hashes.");
            uint metadata=2166136261u;
            metadata=AppendUInt32ByteHash(metadata,receipt.SlabsData.ToUInt32());
            metadata=AppendUInt32ByteHash(metadata,receipt.FreeHead.ToUInt32());
            foreach(uint word in new[]{receipt.PoolKind,receipt.PoolOffset,
                receipt.ElementSize,receipt.ElementsPerSlab,receipt.SlabSize,
                receipt.SlabCount,receipt.SlabCapacityRaw,receipt.TotalSlots,
                receipt.Used,receipt.Unreleased,receipt.FreeHeadSlot})
                metadata=AppendUInt32ByteHash(metadata,word);
            uint snapshot=metadata;
            snapshot=AppendUInt32ArrayByteHash(snapshot,value.SlabBases);
            snapshot=AppendUInt32ArrayByteHash(snapshot,value.FreeSlots);
            snapshot=AppendUInt32ArrayByteHash(snapshot,value.AllocationWords);
            snapshot=AppendByteArrayHash(snapshot,value.SlabBytes);
            if(receipt.MetadataHash!=metadata||receipt.SnapshotHash!=snapshot)
                throw new InvalidOperationException(
                    "A complete NPhase-pool aggregate hash differs from its captured image.");
        }

        private static bool NPhasePoolImagesCoherentWithLegacy(
            NPhasePoolImageState[] images,SipPoolState sipPool,
            ActorPairPoolState actorPairPool,ActorPairReportPoolState reportPool,
            InteractionGraphState graph)
        {
            if(images==null||images.Length!=NPhasePoolKindCount||sipPool==null||
                actorPairPool==null||reportPool==null||graph==null||
                graph.Interactions==null)return false;
            NPhasePoolImageState actorPairImage=images[0];
            NPhasePoolImageState sipImage=images[1];
            NPhasePoolImageState triggerImage=images[2];
            NPhasePoolImageState reportImage=images[3];
            NPhasePoolImageState markerImage=images[4];
            uint[] graphSips=graph.Interactions.Where(item=>item.InteractionType==0)
                .Select(item=>checked(item.Interaction.ToUInt32()-8u)).ToArray();
            uint[] graphTriggers=graph.Interactions.Where(item=>item.InteractionType==2)
                .Select(item=>checked(item.Interaction.ToUInt32()-8u)).ToArray();
            uint[] graphMarkers=graph.Interactions.Where(item=>item.InteractionType==3)
                .Select(item=>checked(item.Interaction.ToUInt32()-8u)).ToArray();
            return actorPairImage.Receipt.NPhaseCore.ToUInt32()==sipPool.NPhaseCore&&
                sipImage.Receipt.NPhaseCore==actorPairImage.Receipt.NPhaseCore&&
                triggerImage.Receipt.NPhaseCore==actorPairImage.Receipt.NPhaseCore&&
                reportImage.Receipt.NPhaseCore==actorPairImage.Receipt.NPhaseCore&&
                markerImage.Receipt.NPhaseCore==actorPairImage.Receipt.NPhaseCore&&
                actorPairImage.Receipt.Pool.ToUInt32()==actorPairPool.Pool&&
                actorPairImage.Receipt.Used==actorPairPool.Used&&
                actorPairImage.Receipt.Unreleased==actorPairPool.Unreleased&&
                NPhasePoolFreeAddresses(actorPairImage).SequenceEqual(
                    actorPairPool.FreeOrder)&&
                NPhasePoolAllocatedAddresses(actorPairImage).SequenceEqual(
                    actorPairPool.AllocatedOrder)&&
                sipImage.Receipt.Pool.ToUInt32()==sipPool.Pool&&
                sipImage.Receipt.Used==sipPool.Used&&
                sipImage.Receipt.Unreleased==sipPool.Unreleased&&
                NPhasePoolFreeAddresses(sipImage).SequenceEqual(sipPool.Order)&&
                new HashSet<uint>(NPhasePoolAllocatedAddresses(sipImage)).SetEquals(
                    graphSips)&&
                reportImage.Receipt.Pool.ToUInt32()==reportPool.Pool&&
                reportImage.Receipt.Used==reportPool.Used&&
                reportImage.Receipt.Unreleased==reportPool.Unreleased&&
                NPhasePoolFreeAddresses(reportImage).SequenceEqual(reportPool.FreeOrder)&&
                NPhasePoolAllocatedAddresses(reportImage).SequenceEqual(
                    reportPool.AllocatedOrder)&&
                new HashSet<uint>(NPhasePoolAllocatedAddresses(triggerImage)).SetEquals(
                    graphTriggers)&&
                new HashSet<uint>(NPhasePoolAllocatedAddresses(markerImage)).SetEquals(
                    graphMarkers);
        }

        private static uint FloatBits(float value)
        {
            return unchecked((uint)BitConverter.ToInt32(BitConverter.GetBytes(value),0));
        }

        private static bool SameFloatBits(float[] left,float[] right)
        {
            if(left==null||right==null||left.Length!=right.Length)return left==right;
            for(int i=0;i<left.Length;i++)if(FloatBits(left[i])!=FloatBits(right[i]))return false;
            return true;
        }

        private static void ValidateManifoldPoolState(ManifoldPoolState value,uint poolKind,string poolName)
        {
            if(value==null||value.PoolKind!=poolKind||value.Pool==0||value.Order==null||
                value.Order.Length>MaximumManifolds||value.OrderHash!=ContactPoolOrderHash(value.Order)||
                value.ElementSize==0||value.ElementsPerSlab==0||value.SlabSize!=value.ElementSize*value.ElementsPerSlab)
                throw new InvalidOperationException("The "+poolName+" manifold-pool checkpoint sidecar is incomplete.");
            if(value.Order.Any(pointer=>pointer==0)||value.Order.Distinct().Count()!=value.Order.Length)
                throw new InvalidOperationException("The "+poolName+" manifold-pool checkpoint contains a null or duplicate free element.");
        }

        private static void ValidateSipPoolState(SipPoolState value)
        {
            if(value==null||value.NPhaseCore==0||value.Pool!=value.NPhaseCore+0x2E0u||
                value.Order==null||value.Order.Length>MaximumShapeInstancePairs||
                value.OrderHash!=ContactPoolOrderHash(value.Order)||value.ElementSize!=0x44u||
                value.ElementsPerSlab!=32u||value.SlabSize!=0x880u||
                value.Unreleased!=(uint)value.Order.Length||
                value.Used+value.Unreleased>MaximumShapeInstancePairs||
                ((value.Used+value.Unreleased)&31u)!=0)
                throw new InvalidOperationException("The shape-pair-pool checkpoint sidecar is incomplete.");
            if(value.Order.Any(pointer=>pointer==0)||value.Order.Distinct().Count()!=value.Order.Length)
                throw new InvalidOperationException("The shape-pair-pool checkpoint contains a null or duplicate free element.");
        }

        private static void ValidateActorPairPoolState(ActorPairPoolState value)
        {
            if(value==null||value.NPhaseCore==0||value.Pool!=value.NPhaseCore+0x90u||
                value.FreeOrder==null||value.AllocatedOrder==null||
                value.FreeOrder.Length>MaximumShapeInstancePairs||
                value.AllocatedOrder.Length>MaximumShapeInstancePairs||
                value.FreeOrderHash!=ContactPoolOrderHash(value.FreeOrder)||
                value.AllocatedOrderHash!=ContactPoolOrderHash(value.AllocatedOrder)||
                value.ElementSize!=0x18u||value.ElementsPerSlab!=32u||value.SlabSize!=0x300u||
                value.SlabCount>128u||value.TotalElements!=value.SlabCount*value.ElementsPerSlab||
                value.Used!=(uint)value.AllocatedOrder.Length||
                value.Unreleased!=(uint)value.FreeOrder.Length||
                value.Used+value.Unreleased!=value.TotalElements)
                throw new InvalidOperationException("The ActorPair-pool checkpoint sidecar is incomplete.");
            uint[] all=value.FreeOrder.Concat(value.AllocatedOrder).ToArray();
            if(all.Any(pointer=>pointer==0)||all.Distinct().Count()!=all.Length)
                throw new InvalidOperationException("The ActorPair-pool checkpoint contains a null, duplicate, or overlapping element.");
        }

        private static void ValidateActorPairReportPoolState(ActorPairReportPoolState value)
        {
            if(value==null||value.NPhaseCore==0||value.Pool!=value.NPhaseCore+0x530u||
                value.FreeOrder==null||value.AllocatedOrder==null||value.AllocatedBytes==null||
                value.FreeOrder.Length>MaximumShapeInstancePairs||
                value.AllocatedOrder.Length>MaximumShapeInstancePairs||
                value.AllocatedBytes.Length!=value.AllocatedOrder.Length||
                value.AllocatedBytes.Any(bytes=>bytes==null||bytes.Length!=0x24)||
                value.FreeOrderHash!=ContactPoolOrderHash(value.FreeOrder)||
                value.AllocatedOrderHash!=ContactPoolOrderHash(value.AllocatedOrder)||
                value.ElementSize!=0x24u||value.ElementsPerSlab!=32u||value.SlabSize!=0x480u||
                value.SlabCount>128u||value.TotalElements!=value.SlabCount*value.ElementsPerSlab||
                value.Used!=(uint)value.AllocatedOrder.Length||
                value.Unreleased!=(uint)value.FreeOrder.Length||
                value.Used+value.Unreleased!=value.TotalElements)
                throw new InvalidOperationException("The ActorPair report-pool checkpoint sidecar is incomplete.");
            uint[] all=value.FreeOrder.Concat(value.AllocatedOrder).ToArray();
            if(all.Any(pointer=>pointer==0)||all.Distinct().Count()!=all.Length)
                throw new InvalidOperationException("The ActorPair report-pool checkpoint contains a null, duplicate, or overlapping element.");
        }

        private static void ValidateNPhaseReportState(NPhaseReportState value)
        {
            if(value==null||value.ActorPairs==null||value.PersistentSips==null||
                value.ForceThresholdSips==null||value.ReportBufferBytes==null)
                throw new InvalidOperationException("The NPhase report-state checkpoint sidecar is incomplete.");
            NativeNPhaseReportStateReceipt receipt=value.Receipt;
            uint actorCapacity=receipt.ActorPairCapacityRaw&0x7FFFFFFFu;
            uint persistentCapacity=receipt.PersistentCapacityRaw&0x7FFFFFFFu;
            uint forceCapacity=receipt.ForceThresholdCapacityRaw&0x7FFFFFFFu;
            if(receipt.Result!=1||receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=116u||
                receipt.UnityBase==UIntPtr.Zero||receipt.NPhaseCore==UIntPtr.Zero||
                receipt.OwnerScene==UIntPtr.Zero||receipt.ReportBuffer==UIntPtr.Zero||
                receipt.ValidationFlags!=0x7Fu||
                receipt.ActorPairCount!=(uint)value.ActorPairs.Length||
                receipt.PersistentCount!=(uint)value.PersistentSips.Length||
                receipt.ForceThresholdCount!=(uint)value.ForceThresholdSips.Length||
                receipt.ActorPairCount>actorCapacity||receipt.PersistentCount>persistentCapacity||
                receipt.ForceThresholdCount>forceCapacity||
                receipt.NextFramePersistentIndex>receipt.PersistentCount||
                receipt.ReportBufferCurrentSize!=(uint)value.ReportBufferBytes.Length||
                receipt.ReportBufferCurrentSize==0||
                receipt.ReportBufferCurrentSize>MaximumContactReportBufferSize||
                receipt.ReportBufferDefaultSize==0||
                receipt.ReportBufferDefaultSize>receipt.ReportBufferCurrentSize||
                receipt.ReportBufferCurrentIndex>receipt.ReportBufferCurrentSize||
                receipt.ReportBufferAllocationLocked>1u||
                (receipt.ReportBufferLastIndex!=0xFFFFFFFFu&&
                    receipt.ReportBufferLastIndex>=receipt.ReportBufferCurrentIndex)||
                receipt.ActorPairOrderHash!=ContactPoolOrderHash(value.ActorPairs)||
                receipt.PersistentOrderHash!=ContactPoolOrderHash(value.PersistentSips)||
                receipt.ForceThresholdOrderHash!=ContactPoolOrderHash(value.ForceThresholdSips)||
                receipt.ReportBufferActiveHash!=ByteHash(value.ReportBufferBytes,
                    checked((int)receipt.ReportBufferCurrentIndex))||
                receipt.ReportBufferAllocationHash!=ByteHash(value.ReportBufferBytes,
                    value.ReportBufferBytes.Length))
                throw new InvalidOperationException("The NPhase report-state checkpoint sidecar is incomplete.");
            uint[] pointers=value.ActorPairs.Concat(value.PersistentSips).Concat(
                value.ForceThresholdSips).ToArray();
            if(pointers.Any(pointer=>pointer==0)||
                value.ActorPairs.Distinct().Count()!=value.ActorPairs.Length||
                value.PersistentSips.Distinct().Count()!=value.PersistentSips.Length||
                value.ForceThresholdSips.Distinct().Count()!=value.ForceThresholdSips.Length||
                value.PersistentSips.Intersect(value.ForceThresholdSips).Any())
                throw new InvalidOperationException("The NPhase report-state checkpoint contains null, duplicate, or conflicting list members.");
        }

        private static void ValidateInteractionGraphState(InteractionGraphState value)
        {
            if(value==null||value.ActiveBodies==null||value.Actors==null||
                value.Interactions==null||value.ActorSlots==null||
                value.PoolSlabs==null||value.PoolFree==null||value.PrimaryBytes==null||
                value.PrimaryBytes.Length!=value.Interactions.Length)
                throw new InvalidOperationException("The interaction-graph checkpoint sidecar is incomplete.");
            NativeInteractionGraphReceipt receipt=value.Receipt;
            if(receipt.Result!=1||receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=500u||
                receipt.UnityBase==UIntPtr.Zero||receipt.NPhaseCore==UIntPtr.Zero||
                receipt.OwnerScene==UIntPtr.Zero||receipt.InteractionScene==UIntPtr.Zero||
                receipt.LlContext==UIntPtr.Zero||receipt.ActiveBodiesData==UIntPtr.Zero||
                receipt.GlobalData==null||receipt.GlobalData.Length!=6||
                receipt.GlobalCount==null||receipt.GlobalCount.Length!=6||
                receipt.GlobalCapacityRaw==null||receipt.GlobalCapacityRaw.Length!=6||
                receipt.GlobalActiveCount==null||receipt.GlobalActiveCount.Length!=6||
                receipt.GlobalOrderHash==null||receipt.GlobalOrderHash.Length!=6||
                receipt.Pools==null||receipt.Pools.Length!=3||
                receipt.ActiveBodiesCount!=(uint)value.ActiveBodies.Length||
                receipt.ActiveBodiesWritten!=receipt.ActiveBodiesRequired||
                receipt.ActiveBodiesWritten!=receipt.ActiveBodiesCount||
                receipt.ActorsWritten!=receipt.ActorsRequired||
                receipt.ActorsWritten!=(uint)value.Actors.Length||
                receipt.InteractionsWritten!=receipt.InteractionsRequired||
                receipt.InteractionsWritten!=(uint)value.Interactions.Length||
                receipt.ActorSlotsWritten!=receipt.ActorSlotsRequired||
                receipt.ActorSlotsWritten!=(uint)value.ActorSlots.Length||
                receipt.PoolSlabsWritten!=receipt.PoolSlabsRequired||
                receipt.PoolSlabsWritten!=(uint)value.PoolSlabs.Length||
                receipt.PoolFreeWritten!=receipt.PoolFreeRequired||
                receipt.PoolFreeWritten!=(uint)value.PoolFree.Length||
                receipt.ActiveTwoWayStart>receipt.ActiveBodiesCount||
                receipt.GlobalCount.Aggregate(0UL,(sum,item)=>sum+item)!=(ulong)value.Interactions.Length||
                receipt.GlobalActiveCount.Where((count,index)=>count>receipt.GlobalCount[index]).Any()||
                receipt.ValidationFlags!=0xFFu)
                throw new InvalidOperationException("The interaction-graph checkpoint receipt is incomplete.");
            if(value.ActiveBodies.Any(pointer=>pointer==0)||
                value.ActiveBodies.Distinct().Count()!=value.ActiveBodies.Length||
                value.Actors.Any(actor=>actor.Actor==UIntPtr.Zero||actor.Vtable==UIntPtr.Zero||
                    actor.InteractionScene!=receipt.InteractionScene||actor.ValidationFlags!=0x7Fu)||
                value.Actors.Select(actor=>actor.Actor.ToUInt32()).Distinct().Count()!=value.Actors.Length||
                value.Interactions.Any(item=>item.Interaction==UIntPtr.Zero||item.Vtable==UIntPtr.Zero||
                    item.Actor0==UIntPtr.Zero||item.Actor1==UIntPtr.Zero||
                    item.InteractionType>5||item.ValidationFlags!=0x1Fu)||
                value.Interactions.Select(item=>item.Interaction.ToUInt32()).Distinct().Count()!=value.Interactions.Length)
                throw new InvalidOperationException("The interaction graph contains null, duplicate, or invalid native rows.");
            var actorByPointer=value.Actors.ToDictionary(actor=>actor.Actor.ToUInt32());
            for(int i=0;i<value.ActiveBodies.Length;i++)
            {
                NativeInteractionGraphActorRecord actor;
                if(!actorByPointer.TryGetValue(value.ActiveBodies[i],out actor)||
                    actor.ActiveBodyIndex!=(uint)i)
                    throw new InvalidOperationException("The interaction graph active-body order disagrees with actor metadata.");
            }
            foreach(NativeInteractionGraphActorRecord actor in value.Actors)
            {
                ulong end=(ulong)actor.InteractionOutputStart+actor.InteractionCount;
                if(end>(ulong)value.ActorSlots.Length||actor.InteractionCount>actor.InteractionCapacity||
                    actor.TransferringCount>actor.InteractionCount||
                    actor.UniqueCount>actor.InteractionCount||actor.CountedCount>actor.InteractionCount||
                    (actor.ActiveBodyIndex!=0xFFFFFFFFu&&actor.ActiveBodyIndex>=value.ActiveBodies.Length))
                    throw new InvalidOperationException("An interaction-graph actor row has invalid counts or offsets.");
            }
            var interactionByPointer=value.Interactions.ToDictionary(item=>item.Interaction.ToUInt32());
            foreach(NativeInteractionGraphInteractionRecord item in value.Interactions)
            {
                NativeInteractionGraphActorRecord actor0,actor1;
                if(!actorByPointer.TryGetValue(item.Actor0.ToUInt32(),out actor0)||
                    !actorByPointer.TryGetValue(item.Actor1.ToUInt32(),out actor1)||
                    item.ActorId0>=actor0.InteractionCount||item.ActorId1>=actor1.InteractionCount||
                    value.ActorSlots[actor0.InteractionOutputStart+item.ActorId0]!=item.Interaction.ToUInt32()||
                    value.ActorSlots[actor1.InteractionOutputStart+item.ActorId1]!=item.Interaction.ToUInt32())
                    throw new InvalidOperationException("An interaction row disagrees with a cached per-actor slot.");
            }
            for(int i=0;i<value.Interactions.Length;i++)
            {
                NativeInteractionGraphInteractionRecord item=value.Interactions[i];
                int expectedBytes=item.InteractionType==0?0x44:item.InteractionType==2?0x3C:
                    item.InteractionType==3?0x28:0;
                byte[] primary=value.PrimaryBytes[i];
                if(primary==null||primary.Length!=expectedBytes)
                    throw new InvalidOperationException(
                        "An interaction primary-object image has the wrong bounded size.");
                if(expectedBytes!=0)
                {
                    uint shape0=item.ShapeCore0.ToUInt32(),shape1=item.ShapeCore1.ToUInt32();
                    uint pxs0=item.PxsShapeCore0.ToUInt32(),pxs1=item.PxsShapeCore1.ToUInt32();
                    if(shape0==0||shape1==0||pxs0!=unchecked(shape0+0x20u)||
                        pxs1!=unchecked(shape1+0x20u)||pxs0==pxs1||
                        item.SemanticLow.ToUInt32()!=Math.Min(pxs0,pxs1)||
                        item.SemanticHigh.ToUInt32()!=Math.Max(pxs0,pxs1))
                        throw new InvalidOperationException(
                            "A rigid interaction row has no exact semantic ShapeCore identity.");
                }
            }
            foreach(uint slot in value.ActorSlots)
                if(slot==0||!interactionByPointer.ContainsKey(slot))
                    throw new InvalidOperationException("An actor slot names an interaction outside the global arrays.");
            uint interactionOffset=0;
            for(uint type=0;type<6;type++)
            {
                uint count=receipt.GlobalCount[type];
                for(uint index=0;index<count;index++)
                {
                    NativeInteractionGraphInteractionRecord item=value.Interactions[interactionOffset+index];
                    if(item.InteractionType!=type||item.GlobalIndex!=index||
                        item.SceneId!=index||item.Active!=(index<receipt.GlobalActiveCount[type]?1u:0u))
                        throw new InvalidOperationException("An interaction row disagrees with global type order or its active prefix.");
                }
                interactionOffset+=count;
            }
            foreach(NativeInteractionGraphPoolReceipt pool in receipt.Pools)
            {
                if(pool.Pool==UIntPtr.Zero||pool.SlabData==UIntPtr.Zero||
                    (pool.BlockCapacity!=8u&&pool.BlockCapacity!=16u&&pool.BlockCapacity!=32u)||
                    pool.BlockBytes!=pool.BlockCapacity*4u||pool.InlineBufferUsed>1u||
                    pool.ElementsPerSlab!=32u||pool.SlabSize!=pool.BlockBytes*32u||
                    pool.TotalElements!=pool.SlabCount*pool.ElementsPerSlab||
                    pool.Used+pool.FreeCount!=pool.TotalElements||
                    (ulong)pool.SlabOutputStart+pool.SlabCount>(ulong)value.PoolSlabs.Length||
                    (ulong)pool.FreeOutputStart+pool.FreeCount>(ulong)value.PoolFree.Length||
                    pool.ValidationFlags!=0x7Fu)
                    throw new InvalidOperationException("An interaction pointer-pool receipt is incomplete.");
            }
        }

        private static void ValidateTransformCacheState(TransformCacheState value)
        {
            if(value==null||value.Entries==null||value.FreeIds==null||value.Bindings==null)
                throw new InvalidOperationException("The transform-cache checkpoint sidecar is incomplete.");
            NativeTransformCacheReceipt receipt=value.Receipt;
            if(receipt.Result!=1||receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=144u||
                receipt.UnityBase==UIntPtr.Zero||receipt.NPhaseCore==UIntPtr.Zero||
                receipt.OwnerScene==UIntPtr.Zero||receipt.InteractionScene==UIntPtr.Zero||
                receipt.Context==UIntPtr.Zero||receipt.TransformCache==UIntPtr.Zero||
                receipt.TransformsData==UIntPtr.Zero||receipt.RefCountsData==UIntPtr.Zero||
                receipt.CurrentId>MaximumTransformCacheIds||
                receipt.EntriesRequired!=receipt.CurrentId||
                receipt.EntriesWritten!=(uint)value.Entries.Length||
                receipt.EntriesRequired!=receipt.EntriesWritten||
                receipt.FreeWritten!=(uint)value.FreeIds.Length||
                receipt.FreeRequired!=receipt.FreeWritten||
                receipt.BindingsWritten!=(uint)value.Bindings.Length||
                receipt.BindingsRequired!=receipt.BindingsWritten||
                receipt.TransformsCount<receipt.CurrentId||receipt.RefCountsCount<receipt.CurrentId||
                receipt.FreeCount!=(uint)value.FreeIds.Length||
                receipt.LiveCount>receipt.CurrentId||
                value.Entries.Any(entry=>entry.Rotation==null||entry.Rotation.Length!=4||
                    entry.Position==null||entry.Position.Length!=3)||
                receipt.EntryHash!=TransformCacheEntryHash(value.Entries)||
                receipt.FreeOrderHash!=ContactPoolOrderHash(value.FreeIds)||
                receipt.BindingHash!=TransformCacheBindingHash(value.Bindings)||
                receipt.SnapshotHash!=TransformCacheSnapshotHash(receipt)||
                receipt.ValidationFlags!=0xFFu)
                throw new InvalidOperationException("The transform-cache checkpoint receipt is incomplete.");
            var free=new HashSet<uint>(value.FreeIds);
            if(free.Count!=value.FreeIds.Length||value.FreeIds.Any(id=>id>=receipt.CurrentId))
                throw new InvalidOperationException("The transform-cache free-ID order contains a duplicate or out-of-range ID.");
            ulong totalRefs=0,totalBindings=0;
            for(int i=0;i<value.Entries.Length;i++)
            {
                NativeTransformCacheEntryRecord entry=value.Entries[i];
                bool isFree=free.Contains(entry.Id);
                bool live=entry.RefCount!=0;
                if(entry.Id!=(uint)i||entry.Rotation==null||entry.Rotation.Length!=4||
                    entry.Position==null||entry.Position.Length!=3||(entry.StateFlags&1u)==0||
                    (((entry.StateFlags&2u)!=0)!=isFree)||
                    (((entry.StateFlags&4u)!=0)!=live)||isFree==live||
                    (isFree&&(entry.BindingCount!=0||entry.RefCount!=0)))
                    throw new InvalidOperationException("A transform-cache entry contradicts its ID partition or native flags.");
                totalRefs+=entry.RefCount;totalBindings+=entry.BindingCount;
            }
            if(value.Entries.Count(entry=>entry.RefCount!=0)!=(int)receipt.LiveCount||
                totalRefs!=receipt.TotalRefCount||totalBindings!=(ulong)value.Bindings.Length)
                throw new InvalidOperationException("Transform-cache refcount or binding totals disagree with its receipt.");
            var bindingCounts=new uint[value.Entries.Length];
            foreach(NativeTransformCacheBindingRecord binding in value.Bindings)
            {
                if(binding.ShapeSim==UIntPtr.Zero||binding.ShapeCore==UIntPtr.Zero||
                    binding.PxsShapeCore==UIntPtr.Zero||binding.Interaction==UIntPtr.Zero||
                    binding.EndpointIndex>1u||binding.CacheId>=receipt.CurrentId||
                    binding.ValidationFlags!=0xFu||free.Contains(binding.CacheId))
                    throw new InvalidOperationException("A transform-cache binding row is null, free, or invalid.");
                NativeTransformCacheEntryRecord entry=value.Entries[binding.CacheId];
                if(binding.RefCount!=entry.RefCount||binding.PoseHash!=entry.PoseHash)
                    throw new InvalidOperationException("A transform-cache binding disagrees with its referenced entry.");
                bindingCounts[binding.CacheId]++;
            }
            for(int i=0;i<bindingCounts.Length;i++)
                if(bindingCounts[i]!=value.Entries[i].BindingCount)
                    throw new InvalidOperationException("Transform-cache binding multiplicity differs from its entry metadata.");
        }

        private static void ValidateIslandSnapshotState(IslandSnapshotState value,uint expectedPhase)
        {
            if(value==null||value.Nodes==null||value.Edges==null||value.Islands==null||
                value.Roots==null||value.KinematicWords==null||value.KinematicChangeWords==null||
                value.NotReadyWords==null||value.NotReadyChangeWords==null||value.IslandWords==null||
                value.NodeCreated==null||value.NodeDeleted==null||value.EdgeCreated==null||
                value.EdgeDeleted==null||value.EdgeBroken==null||value.EdgeJoined==null||
                value.Bindings==null||value.RawBytes==null)
                throw new InvalidOperationException("Island snapshot sidecar is missing an ordered family.");
            NativeIslandSnapshotReceipt receipt=value.Receipt;
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=620u||receipt.Result!=1||
                receipt.UnityBase==UIntPtr.Zero||receipt.NPhaseCore==UIntPtr.Zero||
                receipt.OwnerScene==UIntPtr.Zero||receipt.InteractionScene==UIntPtr.Zero||
                receipt.Context==UIntPtr.Zero||receipt.IslandManager==UIntPtr.Zero||
                receipt.IslandManager.ToUInt32()!=checked(receipt.Context.ToUInt32()+0x181Cu)||
                receipt.Phase!=expectedPhase||receipt.CaptureThreadId==0||
                receipt.ValidationFlags!=0x3FFu||receipt.InvalidKind!=0||receipt.Detail!=0||
                receipt.JournalEndOrdinal<receipt.JournalBeginOrdinal)
                throw new InvalidOperationException("Island snapshot receipt contract differs.");
            ValidateIslandManagerReceipt(receipt.NodeManager,value.Nodes.Length,MaximumIslandNodes,"node");
            ValidateIslandManagerReceipt(receipt.EdgeManager,value.Edges.Length,MaximumIslandEdges,"edge");
            ValidateIslandManagerReceipt(receipt.IslandManagerReceipt,value.Islands.Length,MaximumIslands,"island");
            ValidateIslandManagerReceipt(receipt.RootManager,value.Roots.Length,MaximumIslandRoots,"root");
            ValidateIslandQueueReceipt(receipt.NodeCreated,value.NodeCreated.Length,"node-created");
            ValidateIslandQueueReceipt(receipt.NodeDeleted,value.NodeDeleted.Length,"node-deleted");
            ValidateIslandQueueReceipt(receipt.EdgeCreated,value.EdgeCreated.Length,"edge-created");
            ValidateIslandQueueReceipt(receipt.EdgeDeleted,value.EdgeDeleted.Length,"edge-deleted");
            ValidateIslandQueueReceipt(receipt.EdgeBroken,value.EdgeBroken.Length,"edge-broken");
            ValidateIslandQueueReceipt(receipt.EdgeJoined,value.EdgeJoined.Length,"edge-joined");
            ValidateIslandBitmapReceipt(receipt.Kinematic,value.KinematicWords.Length,"kinematic");
            ValidateIslandBitmapReceipt(receipt.KinematicChange,value.KinematicChangeWords.Length,"kinematic-change");
            ValidateIslandBitmapReceipt(receipt.NotReady,value.NotReadyWords.Length,"not-ready");
            ValidateIslandBitmapReceipt(receipt.NotReadyChange,value.NotReadyChangeWords.Length,"not-ready-change");
            ValidateIslandBitmapReceipt(receipt.IslandBitmap,value.IslandWords.Length,"island");
            if(receipt.BindingsRequired!=receipt.BindingsWritten||
                receipt.BindingsWritten!=(uint)value.Bindings.Length||
                value.Bindings.Length>MaximumIslandBindings||
                receipt.LiveContactEdges!=(uint)value.Bindings.Length||
                receipt.LiveContactEdges+receipt.LiveConstraintEdges+receipt.LiveArticulationEdges>
                    receipt.EdgeManager.Capacity||value.RawBytes.Length<620)
                throw new InvalidOperationException("Island edge-type counts or semantic bindings are incomplete.");
            for(int i=0;i<value.Nodes.Length;i++)
            {
                NativeIslandNodeSlotRecord item=value.Nodes[i];
                if(item.Id!=(uint)i||item.ValidationFlags!=0x1Fu)
                    throw new InvalidOperationException("Island node slot validation differs at "+i+".");
            }
            for(int i=0;i<value.Edges.Length;i++)
            {
                NativeIslandEdgeSlotRecord item=value.Edges[i];
                if(item.Id!=(uint)i||item.ValidationFlags!=0x1Fu||
                    (item.SemanticBindingIndex!=0xFFFFFFFFu&&item.SemanticBindingIndex>=value.Bindings.Length))
                    throw new InvalidOperationException("Island edge slot validation differs at "+i+".");
            }
            for(int i=0;i<value.Islands.Length;i++)
                if(value.Islands[i].Id!=(uint)i||value.Islands[i].ValidationFlags!=0x0Fu)
                    throw new InvalidOperationException("Island slot validation differs at "+i+".");
            for(int i=0;i<value.Roots.Length;i++)
                if(value.Roots[i].Id!=(uint)i||value.Roots[i].ValidationFlags!=0x07u)
                    throw new InvalidOperationException("Island articulation-root slot validation differs at "+i+".");
            var boundEdges=new HashSet<uint>();
            for(int i=0;i<value.Bindings.Length;i++)
            {
                NativeIslandSipBindingRecord binding=value.Bindings[i];
                if(binding.ValidationFlags!=0x1Fu||binding.EdgeType!=0||
                    binding.EdgeId>=(uint)value.Edges.Length||!boundEdges.Add(binding.EdgeId)||
                    binding.Sip==0||binding.HookAddress!=checked(binding.Sip+0x3Cu)||
                    ((binding.ShapeSim0==0)!=(binding.ShapeSim1==0))||
                    ((binding.TaggedRaw&8u)==0&&(binding.ShapeSim0==0||binding.ShapeSim1==0))||
                    binding.PxsLow==0||binding.PxsHigh==0||
                    binding.PxsLow>=binding.PxsHigh||
                    (value.Edges[binding.EdgeId].SlotFlags&1u)==0||
                    (binding.TaggedRaw&1u)!=0||
                    (binding.TaggedRaw&~0xFu)!=binding.ContactManager||
                    value.Edges[binding.EdgeId].SemanticBindingIndex!=(uint)i||
                    value.Edges[binding.EdgeId].TaggedRaw!=binding.TaggedRaw)
                    throw new InvalidOperationException("Island SIP edge binding validation differs at "+i+".");
            }
        }

        private static void ValidateIslandManagerReceipt(NativeIslandElementManagerReceipt receipt,
            int length,int maximum,string name)
        {
            if(receipt.Vtable==UIntPtr.Zero||
                (receipt.Capacity!=0&&(receipt.Elements==UIntPtr.Zero||receipt.FreeNext==UIntPtr.Zero))||
                receipt.Capacity>(uint)maximum||receipt.Required!=receipt.Written||
                receipt.Written!=(uint)length||receipt.Required!=receipt.Capacity||
                receipt.FreeCount>receipt.Capacity||
                (receipt.FreeCount==0&&receipt.FreeHead!=0xFFFFFFFFu)||
                (receipt.FreeCount!=0&&receipt.FreeHead>=receipt.Capacity))
                throw new InvalidOperationException("Island "+name+" manager receipt differs.");
        }

        private static void ValidateIslandQueueReceipt(NativeIslandQueueReceipt receipt,
            int length,string name)
        {
            if(receipt.Count>MaximumIslandQueueEntries||receipt.Required!=receipt.Written||
                receipt.Written!=(uint)length||receipt.Required!=receipt.Count||
                receipt.Count>receipt.Capacity||(receipt.Count!=0&&receipt.Data==UIntPtr.Zero))
                throw new InvalidOperationException("Island "+name+" queue receipt differs.");
        }

        private static void ValidateIslandBitmapReceipt(NativeIslandBitmapReceipt receipt,
            int length,string name)
        {
            if(receipt.Required!=receipt.Written||receipt.Written!=(uint)length||
                receipt.Required!=receipt.WordCount||(receipt.WordCount!=0&&receipt.Data==UIntPtr.Zero))
                throw new InvalidOperationException("Island "+name+" bitmap receipt differs.");
        }

        private void ValidateIslandTransitionReceipts(CheckpointSidecar sidecar,
            PendingIslandObservation pending,NativeIslandObserverReceipt receipt,
            IslandSnapshotState pre,IslandSnapshotState post)
        {
            ValidateIslandSnapshotState(pre,IslandPhasePreUpdate);
            ValidateIslandSnapshotState(post,IslandPhasePostUpdate);
            NativeIslandSnapshotReceipt settled=sidecar.IslandSnapshot.Receipt;
            NativeIslandSnapshotReceipt a=pre.Receipt,b=post.Receipt;
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=128u||receipt.Result!=1||
                receipt.UnityBase.ToUInt32()!=unityPlayerBase||receipt.Installed!=1||receipt.State!=4||
                receipt.ExpectedManager.ToUInt32()!=pending.ExpectedManager||
                receipt.ExpectedContext.ToUInt32()!=pending.ExpectedContext||
                receipt.ExpectedNPhase.ToUInt32()!=pending.ExpectedNphase||
                receipt.ObservedManager!=receipt.ExpectedManager||
                receipt.ObservedContext!=receipt.ExpectedContext||receipt.ObservedNPhase!=receipt.ExpectedNPhase||
                receipt.ExpectedPass!=0||receipt.Pass!=0||receipt.ArmedThreadId!=pending.ArmedThreadId||
                receipt.ThreadId==0||receipt.ObserverSequence!=pending.ObserverSequence||
                receipt.ArmedOrdinal!=pending.ArmedOrdinal||receipt.ObservationOrdinal!=pending.ArmedOrdinal||
                receipt.SlotIndex!=0||receipt.PreResult!=1||receipt.PostResult!=1||
                receipt.PreSnapshotHash!=a.SnapshotHash||receipt.PostSnapshotHash!=b.SnapshotHash||
                receipt.JournalBeginOrdinal!=settled.JournalEndOrdinal||
                receipt.JournalEndOrdinal!=b.JournalEndOrdinal||
                receipt.JournalEndOrdinal<receipt.JournalBeginOrdinal||
                receipt.ValidationFlags!=0x7Fu||receipt.InvalidKind!=0||receipt.Detail!=0||receipt.InFlight!=0)
                throw new InvalidOperationException("Island observer receipt does not identify one exact first-pass transition.");
            foreach(NativeIslandSnapshotReceipt item in new[]{a,b})
                if(item.UnityBase!=settled.UnityBase||item.NPhaseCore!=settled.NPhaseCore||
                    item.OwnerScene!=settled.OwnerScene||item.InteractionScene!=settled.InteractionScene||
                    item.Context!=settled.Context||item.IslandManager!=settled.IslandManager||
                    item.ObserverSequence!=receipt.ObserverSequence||
                    item.ObservationOrdinal!=receipt.ObservationOrdinal||
                    item.CaptureThreadId!=receipt.ThreadId)
                    throw new InvalidOperationException("Island pre/post snapshot identity differs from the settled sidecar transaction.");
            ValidateIslandSnapshotCoherence(sidecar,pre);
            ValidateIslandSnapshotCoherence(sidecar,post);
        }

        private void ValidateIslandTransitionAuditReceipts(CheckpointSidecar sidecar,
            PendingIslandObservation pending,NativeIslandObserverReceipt receipt,
            IslandSnapshotState pre,IslandSnapshotState post)
        {
            ValidateIslandSnapshotState(pre,IslandPhasePreUpdate);
            ValidateIslandSnapshotState(post,IslandPhasePostUpdate);
            NativeIslandSnapshotReceipt settled=sidecar.IslandSnapshot.Receipt;
            NativeIslandSnapshotReceipt a=pre.Receipt,b=post.Receipt;
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=128u||receipt.Result!=1||
                receipt.UnityBase.ToUInt32()!=unityPlayerBase||receipt.Installed!=1||receipt.State!=4||
                receipt.ExpectedManager.ToUInt32()!=pending.ExpectedManager||
                receipt.ExpectedContext.ToUInt32()!=pending.ExpectedContext||
                receipt.ExpectedNPhase.ToUInt32()!=pending.ExpectedNphase||
                receipt.ObservedManager!=receipt.ExpectedManager||
                receipt.ObservedContext!=receipt.ExpectedContext||receipt.ObservedNPhase!=receipt.ExpectedNPhase||
                receipt.ExpectedPass!=0||receipt.Pass!=0||receipt.ArmedThreadId!=pending.ArmedThreadId||
                receipt.ThreadId==0||receipt.ObserverSequence!=pending.ObserverSequence||
                receipt.ArmedOrdinal!=pending.ArmedOrdinal||receipt.ObservationOrdinal!=pending.ArmedOrdinal||
                receipt.SlotIndex!=0||receipt.PreResult!=1||receipt.PostResult!=1||
                receipt.PreSnapshotHash!=a.SnapshotHash||receipt.PostSnapshotHash!=b.SnapshotHash||
                receipt.JournalEndOrdinal!=b.JournalEndOrdinal||
                receipt.JournalEndOrdinal<receipt.JournalBeginOrdinal||
                receipt.ValidationFlags!=0x7Fu||receipt.InvalidKind!=0||receipt.Detail!=0||receipt.InFlight!=0)
                throw new InvalidOperationException(
                    "First-replay island observer receipt does not identify one exact pass-zero transition.");
            foreach(NativeIslandSnapshotReceipt item in new[]{a,b})
                if(item.UnityBase!=settled.UnityBase||item.NPhaseCore!=settled.NPhaseCore||
                    item.OwnerScene!=settled.OwnerScene||item.InteractionScene!=settled.InteractionScene||
                    item.Context!=settled.Context||item.IslandManager!=settled.IslandManager||
                    item.ObserverSequence!=receipt.ObserverSequence||
                    item.ObservationOrdinal!=receipt.ObservationOrdinal||
                    item.CaptureThreadId!=receipt.ThreadId)
                    throw new InvalidOperationException(
                        "First-replay island pre/post identity differs from the restored checkpoint scene.");
        }

        private void ValidateIslandTransitionState(CheckpointSidecar sidecar)
        {
            if(sidecar==null||sidecar.IslandTransition==null||sidecar.IslandSnapshot==null)
                throw new InvalidOperationException("Island transition sidecar is missing.");
            IslandTransitionState value=sidecar.IslandTransition;
            NativeIslandObserverReceipt receipt=value.Receipt;
            var pending=new PendingIslandObservation {Sidecar=sidecar,
                ExpectedManager=receipt.ExpectedManager.ToUInt32(),
                ExpectedContext=receipt.ExpectedContext.ToUInt32(),
                ExpectedNphase=receipt.ExpectedNPhase.ToUInt32(),
                ArmedOrdinal=receipt.ArmedOrdinal,ObserverSequence=receipt.ObserverSequence,
                ArmedThreadId=receipt.ArmedThreadId};
            ValidateIslandTransitionReceipts(sidecar,pending,receipt,value.Pre,value.Post);
            if(value.Journal==null||value.JournalRawBytes==null||
                value.JournalRawBytes.Length!=value.Journal.Length*56||
                value.Journal.Length!=checked((int)(receipt.JournalEndOrdinal-receipt.JournalBeginOrdinal)))
                throw new InvalidOperationException("Island transition journal interval is incomplete.");
            NativeIslandJournalReceipt journal=value.JournalReceipt;
            if(journal.ApiVersion!=NativeAbiVersion||journal.StructSize!=84u||journal.Result!=1||
                journal.UnityBase.ToUInt32()!=unityPlayerBase||journal.ExpectedManager!=receipt.ExpectedManager||
                journal.Installed!=1||journal.FirstOrdinal>receipt.JournalBeginOrdinal||
                journal.RequestedBegin!=receipt.JournalBeginOrdinal||journal.NextOrdinal<receipt.JournalEndOrdinal||
                journal.RecordsRequired!=journal.RecordsWritten||journal.ValidationFlags!=0x1Fu||
                journal.InvalidKind!=0||journal.Detail!=0)
                throw new InvalidOperationException("Island transition journal receipt differs.");
            for(int i=0;i<value.Journal.Length;i++)
            {
                NativeIslandJournalRecord item=value.Journal[i];
                if(item.Ordinal!=receipt.JournalBeginOrdinal+(uint)i||item.ThreadId==0||
                    item.ValidationFlags!=0x1Fu||(item.EventKind!=1&&item.EventKind!=2)||item.EdgeType>2)
                    throw new InvalidOperationException("Island transition journal record differs at "+i+".");
            }
            ValidateIslandJournalBindingCoherence(sidecar,value.Pre,value.Post,value.Journal);
        }

        private static void ValidateIslandJournalBindingCoherence(CheckpointSidecar sidecar,
            IslandSnapshotState pre,IslandSnapshotState post,NativeIslandJournalRecord[] journal)
        {
            NativeIslandSipBindingRecord[] settled=sidecar.IslandSnapshot.Bindings;
            foreach(NativeIslandSipBindingRecord binding in pre.Bindings.Concat(post.Bindings))
            {
                bool alreadyKnown=settled.Any(value=>value.EdgeId==binding.EdgeId&&
                    value.Sip==binding.Sip&&value.PxsLow==binding.PxsLow&&value.PxsHigh==binding.PxsHigh);
                if(alreadyKnown)continue;
                bool journaled=journal.Any(value=>value.EdgeType==0&&
                    value.OwnerObject==binding.Sip&&value.PxsLow==binding.PxsLow&&
                    value.PxsHigh==binding.PxsHigh&&
                    (value.EventKind==1?value.PostEdgeId:value.PreEdgeId)==binding.EdgeId);
                if(!journaled)
                    throw new InvalidOperationException("A transition-only contact edge lacks matching add/remove journal evidence.");
            }
        }

        private static void ValidateIslandSnapshotCoherence(CheckpointSidecar sidecar,
            IslandSnapshotState state)
        {
            NativeIslandSnapshotReceipt island=state.Receipt;
            NativeInteractionGraphReceipt graph=sidecar.InteractionGraph.Receipt;
            if(island.NPhaseCore!=graph.NPhaseCore||island.OwnerScene!=graph.OwnerScene||
                island.InteractionScene!=graph.InteractionScene||island.Context!=graph.LlContext)
                throw new InvalidOperationException("Island snapshot Scene/Context differs from the interaction graph.");
            var owners=sidecar.ContactManagerOwners.Records.ToDictionary(value=>value.Sip.ToUInt32());
            foreach(NativeIslandSipBindingRecord binding in state.Bindings)
            {
                NativeContactManagerOwnerRecord owner;
                bool known=owners.TryGetValue(binding.Sip,out owner)&&
                    binding.PxsLow==Math.Min(owner.PxsShapeCore0.ToUInt32(),owner.PxsShapeCore1.ToUInt32())&&
                    binding.PxsHigh==Math.Max(owner.PxsShapeCore0.ToUInt32(),owner.PxsShapeCore1.ToUInt32());
                bool ownerCoherent=known&&
                    (binding.ShapeSim0==0||
                        (binding.ShapeSim0==owner.ShapeSim0.ToUInt32()&&
                         binding.ShapeSim1==owner.ShapeSim1.ToUInt32()))&&
                    binding.ContactManager!=0&&binding.ContactManager==owner.Manager.ToUInt32();
                NativeInteractionGraphInteractionRecord graphItem=sidecar.InteractionGraph.Interactions
                    .FirstOrDefault(item=>item.Interaction.ToUInt32()==unchecked(binding.Sip+8u)&&
                        item.InteractionType==0&&item.SemanticLow.ToUInt32()==binding.PxsLow&&
                        item.SemanticHigh.ToUInt32()==binding.PxsHigh);
                bool graphKnown=graphItem.Interaction!=UIntPtr.Zero;
                if(state.Receipt.Phase==IslandPhaseSettled&&!ownerCoherent&&!graphKnown&&
                    (binding.TaggedRaw&8u)==0)
                    throw new InvalidOperationException(
                        "Settled island binding has no matching contact-owner or interaction-graph semantic identity.");
            }
        }

        private static uint TransformCacheEntryHash(NativeTransformCacheEntryRecord[] values)
        {
            uint hash=2166136261u;
            foreach(NativeTransformCacheEntryRecord value in values)
                foreach(uint word in new[]{value.Id,value.RefCount,
                    FloatBits(value.Rotation[0]),FloatBits(value.Rotation[1]),
                    FloatBits(value.Rotation[2]),FloatBits(value.Rotation[3]),
                    FloatBits(value.Position[0]),FloatBits(value.Position[1]),
                    FloatBits(value.Position[2]),value.PoseHash,value.BindingCount,value.StateFlags})
                    foreach(byte item in BitConverter.GetBytes(word)){hash^=item;hash*=16777619u;}
            return hash;
        }

        private static uint TransformCacheBindingHash(NativeTransformCacheBindingRecord[] values)
        {
            uint hash=2166136261u;
            foreach(NativeTransformCacheBindingRecord value in values)
                foreach(uint word in new[]{value.ShapeSim.ToUInt32(),value.ShapeCore.ToUInt32(),
                    value.PxsShapeCore.ToUInt32(),value.Interaction.ToUInt32(),
                    value.InteractionIndex,value.EndpointIndex,value.CacheId,value.RefCount,
                    value.PoseHash,value.ValidationFlags})
                    foreach(byte item in BitConverter.GetBytes(word)){hash^=item;hash*=16777619u;}
            return hash;
        }

        private static uint TransformCacheSnapshotHash(NativeTransformCacheReceipt value)
        {
            uint hash=2166136261u;
            foreach(uint word in new[]{value.CurrentId,value.EntryHash,value.FreeOrderHash,value.BindingHash})
                foreach(byte item in BitConverter.GetBytes(word)){hash^=item;hash*=16777619u;}
            return hash;
        }

        private void ValidateFinishBroadPhaseReceiptContract(
            NativeFinishBroadPhaseObserverReceipt receipt,int size)
        {
            if(receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=(uint)size||
                receipt.UnityBase.ToUInt32()!=unityPlayerBase)
                throw new InvalidOperationException("Native finishBroadPhase observer receipt contract differs.");
        }

        private static void ValidateFinishBroadPhaseState(
            FinishBroadPhaseState value,CheckpointSidecar sidecar)
        {
            if(value==null||value.Created==null||value.Deleted==null||sidecar==null||
                sidecar.InteractionGraph==null||sidecar.TransformCache==null)
                throw new InvalidOperationException("The finishBroadPhase checkpoint observation is incomplete.");
            NativeFinishBroadPhaseObserverReceipt receipt=value.Receipt;
            NativeInteractionGraphReceipt graph=sidecar.InteractionGraph.Receipt;
            NativeTransformCacheReceipt cache=sidecar.TransformCache.Receipt;
            // Scene::finishBroadPhase is a PhysX task.  Its callback may run
            // on a different dispatcher worker than the Unity thread that
            // armed this observation; only the callback's own entry/exit
            // thread, exact identities, ordinal and double-copy are semantic.
            if(receipt.Result!=1||receipt.ApiVersion!=NativeAbiVersion||receipt.StructSize!=152u||
                receipt.UnityBase==UIntPtr.Zero||receipt.Installed!=1||receipt.State!=4||
                receipt.ExpectedPass!=0||receipt.Pass!=0||receipt.ArmedOrdinal==0||
                receipt.ArmedOrdinal!=receipt.ObservationOrdinal||
                receipt.SlotIndex!=(receipt.ObservationOrdinal-1u)%4u||
                receipt.ArmedThreadId==0||receipt.ThreadId==0||
                receipt.ExpectedScene!=receipt.ObservedScene||
                receipt.ExpectedContext!=receipt.ObservedContext||
                receipt.ExpectedNPhaseCore!=receipt.ObservedNPhaseCore||
                receipt.AabbManager==UIntPtr.Zero||
                receipt.ExpectedScene!=graph.OwnerScene||receipt.ExpectedContext!=graph.LlContext||
                receipt.ExpectedNPhaseCore!=graph.NPhaseCore||
                receipt.InteractionScene!=graph.InteractionScene||
                receipt.TransformCache!=cache.TransformCache||
                receipt.CreatedRequired!=(uint)value.Created.Length||
                receipt.CreatedWritten!=receipt.CreatedRequired||
                receipt.DeletedRequired!=(uint)value.Deleted.Length||
                receipt.DeletedWritten!=receipt.DeletedRequired||
                receipt.CreatedRequired>MaximumBroadPhaseOverlaps||
                receipt.DeletedRequired>MaximumBroadPhaseOverlaps||
                receipt.CreatedHash!=BroadPhaseOverlapOrderHash(value.Created)||
                receipt.DeletedHash!=BroadPhaseOverlapOrderHash(value.Deleted)||
                receipt.ValidationFlags!=0x3FFu||receipt.InvalidKind!=0||
                receipt.Detail!=0||receipt.DroppedObservations!=0)
                throw new InvalidOperationException("The finishBroadPhase observation receipt is incomplete or belongs to another physics transaction.");
            foreach(NativeBroadPhaseOverlapRecord overlap in value.Created.Concat(value.Deleted))
                if(overlap.UserData0==UIntPtr.Zero||overlap.UserData1==UIntPtr.Zero||
                    overlap.UserData0==overlap.UserData1||
                    overlap.ShapeCore0==UIntPtr.Zero||overlap.ShapeCore1==UIntPtr.Zero||
                    overlap.ShapeCore0==overlap.ShapeCore1||
                    overlap.PxsShapeCore0==UIntPtr.Zero||overlap.PxsShapeCore1==UIntPtr.Zero||
                    overlap.PxsShapeCore0.ToUInt32()!=unchecked(overlap.ShapeCore0.ToUInt32()+0x20u)||
                    overlap.PxsShapeCore1.ToUInt32()!=unchecked(overlap.ShapeCore1.ToUInt32()+0x20u)||
                    overlap.PairHash!=BroadPhasePairHash(overlap)||
                    overlap.ValidationFlags!=0xFu)
                    throw new InvalidOperationException("A finishBroadPhase overlap row is null or invalid.");
            // preCacheHash/preGraphHash are entry-time f445 observations.  The
            // sidecar is a settled f444 capture, so equality is intentionally
            // neither required nor inferred here.
        }

        private static uint BroadPhasePairHash(NativeBroadPhaseOverlapRecord value)
        {
            uint hash=2166136261u;
            foreach(uint word in new[]{value.UserData0.ToUInt32(),value.UserData1.ToUInt32(),
                value.ShapeCore0.ToUInt32(),value.ShapeCore1.ToUInt32(),
                value.PxsShapeCore0.ToUInt32(),value.PxsShapeCore1.ToUInt32(),
                value.CacheId0,value.CacheId1})
                foreach(byte item in BitConverter.GetBytes(word)){hash^=item;hash*=16777619u;}
            return hash;
        }

        private static uint BroadPhaseOverlapOrderHash(NativeBroadPhaseOverlapRecord[] values)
        {
            if(values==null)throw new ArgumentNullException("values");
            uint hash=2166136261u;
            foreach(NativeBroadPhaseOverlapRecord value in values)
                foreach(uint word in new[]{value.UserData0.ToUInt32(),value.UserData1.ToUInt32(),
                    value.ShapeCore0.ToUInt32(),value.ShapeCore1.ToUInt32(),
                    value.PxsShapeCore0.ToUInt32(),value.PxsShapeCore1.ToUInt32(),
                    value.CacheId0,value.CacheId1,value.PairHash,value.ValidationFlags})
                    foreach(byte item in BitConverter.GetBytes(word)){hash^=item;hash*=16777619u;}
            return hash;
        }

        private void RecordFinishBroadPhaseReceipt(string action,
            NativeFinishBroadPhaseObserverReceipt receipt)
        {
            finishBroadPhaseReceipts.Add(new Dictionary<string,object>{{"action",action},
                {"result",receipt.Result},{"lastError",receipt.LastError},{"state",receipt.State},
                {"installed",receipt.Installed!=0},{"expectedScene",Hex(receipt.ExpectedScene)},
                {"expectedContext",Hex(receipt.ExpectedContext)},
                {"expectedNPhaseCore",Hex(receipt.ExpectedNPhaseCore)},
                {"observedScene",Hex(receipt.ObservedScene)},
                {"observedContext",Hex(receipt.ObservedContext)},
                {"observedNPhaseCore",Hex(receipt.ObservedNPhaseCore)},
                {"aabbManager",Hex(receipt.AabbManager)},
                {"interactionScene",Hex(receipt.InteractionScene)},
                {"transformCache",Hex(receipt.TransformCache)},
                {"expectedPass",receipt.ExpectedPass},{"pass",receipt.Pass},
                {"armedThreadId",receipt.ArmedThreadId},{"threadId",receipt.ThreadId},
                {"armedOrdinal",receipt.ArmedOrdinal},{"observationOrdinal",receipt.ObservationOrdinal},
                {"slotIndex",receipt.SlotIndex},{"createdCount",receipt.CreatedWritten},
                {"deletedCount",receipt.DeletedWritten},
                {"createdHash","0x"+receipt.CreatedHash.ToString("X8")},
                {"deletedHash","0x"+receipt.DeletedHash.ToString("X8")},
                {"preCacheHash","0x"+receipt.PreCacheHash.ToString("X8")},
                {"postCacheHash","0x"+receipt.PostCacheHash.ToString("X8")},
                {"preGraphHash","0x"+receipt.PreGraphHash.ToString("X8")},
                {"postGraphHash","0x"+receipt.PostGraphHash.ToString("X8")},
                {"validationFlags","0x"+receipt.ValidationFlags.ToString("X8")},
                {"droppedObservations",receipt.DroppedObservations}});
            if(finishBroadPhaseReceipts.Count>24)finishBroadPhaseReceipts.RemoveAt(0);
        }

        private static void ValidateContactManagerOwnerState(ContactManagerOwnerState value)
        {
            if(value==null||value.Records==null||value.ShapeOwners0==null||value.ShapeOwners1==null||
                value.Endpoints0==null||value.Endpoints1==null||
                value.ManagerBytes==null||value.SipBytes==null||value.ActorPairBytes==null||
                value.ManifoldBytes==null||value.CacheBytes==null||
                value.Records.Length!=value.ShapeOwners0.Length||value.Records.Length!=value.ShapeOwners1.Length||
                value.Records.Length!=value.Endpoints0.Length||value.Records.Length!=value.Endpoints1.Length||
                value.Records.Length!=value.ManagerBytes.Length||value.Records.Length!=value.SipBytes.Length||
                value.Records.Length!=value.ActorPairBytes.Length||
                value.Records.Length!=value.ManifoldBytes.Length||value.Records.Length!=value.CacheBytes.Length||
                value.Endpoints0.Any(endpoint=>endpoint==null)||value.Endpoints1.Any(endpoint=>endpoint==null)||
                value.Receipt.Result!=1||value.Receipt.ConsistencyFlags!=0xFFu||
                value.Receipt.RecordsWritten!=(uint)value.Records.Length||
                value.Receipt.RecordsRequired!=value.Receipt.RecordsWritten||
                value.Receipt.UsedCount!=value.Receipt.RecordsWritten||
                value.Receipt.ActiveCount!=value.Receipt.UsedCount||
                value.Receipt.FreeCount+value.Receipt.UsedCount!=value.Receipt.TotalSlots)
                throw new InvalidOperationException("Contact-manager owner checkpoint sidecar is incomplete.");
            for(int i=0;i<value.Records.Length;i++)
            {
                NativeContactManagerOwnerRecord record=value.Records[i];
                if(value.ManagerBytes[i]==null||value.ManagerBytes[i].Length!=0x80||
                    value.SipBytes[i]==null||value.SipBytes[i].Length!=0x44||
                    value.ActorPairBytes[i]==null||value.ActorPairBytes[i].Length!=0x18||
                    value.ManifoldBytes[i]==null||
                    value.ManifoldBytes[i].Length!=checked((int)record.ManifoldBytes)||
                    value.CacheBytes[i]==null||value.CacheBytes[i].Length!=record.CacheSize)
                    throw new InvalidOperationException(
                        "Contact-manager raw checkpoint image differs at row "+i+".");
            }
        }

        private static object DescribeManifoldPoolState(ManifoldPoolState value)
        {
            if(value==null)return null;
            object[] top=value.Order.Take(16).Select(pointer=>(object)("0x"+pointer.ToString("X8"))).ToArray();
            return new Dictionary<string,object>{{"poolKind",value.PoolKind},
                {"pool","0x"+value.Pool.ToString("X8")},{"freeCount",value.Order.Length},
                {"elementSize",value.ElementSize},{"elementsPerSlab",value.ElementsPerSlab},
                {"used",value.Used},{"unreleased",value.Unreleased},{"slabSize",value.SlabSize},
                {"freeHead",value.Order.Length==0?"0x00000000":"0x"+value.Order[0].ToString("X8")},
                {"orderHash","0x"+value.OrderHash.ToString("X8")},{"top",top}};
        }

        private static object DescribePhysicsPhaseSnapshot(PhysicsPhaseSnapshot value)
        {
            if(value==null)return null;
            return new Dictionary<string,object>{{"frame",value.Frame},
                {"context","0x"+value.Context.ToString("X8")},
                {"freeArray","0x"+value.FreeArray.ToString("X8")},
                {"contactFreeCount",value.ContactPoolOrder==null?0:value.ContactPoolOrder.Length},
                {"contactFreeOrderHash","0x"+value.OrderHash.ToString("X8")},
                {"contactManagerOwners",DescribeContactManagerOwnerState(
                    value.ContactManagerOwners,true)},
                {"shapeInstancePairPool",DescribeSipPoolState(value.ShapeInstancePairPool)},
                {"actorPairPool",DescribeActorPairPoolState(value.ActorPairPool)},
                {"actorPairReportPool",DescribeActorPairReportPoolState(
                    value.ActorPairReportPool)},
                {"nphasePoolImages",DescribeNPhasePoolImages(value.NPhasePoolImages)},
                {"nphaseReports",DescribeNPhaseReportState(value.NPhaseReports)},
                {"interactionGraph",DescribeInteractionGraphState(value.InteractionGraph)},
                {"transformCache",DescribeTransformCacheState(value.TransformCache)},
                {"islandSnapshot",DescribeIslandSnapshotState(value.IslandSnapshot)},
                {"largeManifoldPool",DescribeManifoldPoolState(value.LargeManifoldPool)},
                {"sphereManifoldPool",DescribeManifoldPoolState(value.SphereManifoldPool)},
                {"transformDispatch",DescribeTransformDispatchState(value.TransformDispatch)},
                {"dirtyInteractions",DescribeDirtyInteractionState(value.DirtyInteractions)},
                {"coreSnapshotPresent",value.CoreSnapshot!=null}};
        }

        private static object DescribePhysicsPhaseComparison(PhysicsPhaseSnapshot target,
            PhysicsPhaseSnapshot restored)
        {
            if(target==null||restored==null)return null;
            bool contactFree=target.Context==restored.Context&&
                target.FreeArray==restored.FreeArray&&target.OrderHash==restored.OrderHash&&
                target.ContactPoolOrder!=null&&restored.ContactPoolOrder!=null&&
                target.ContactPoolOrder.SequenceEqual(restored.ContactPoolOrder);
            bool contactOwners=SameContactManagerOwnerSnapshot(
                target.ContactManagerOwners,restored.ContactManagerOwners);
            bool sip=SameSipPoolSnapshot(target.ShapeInstancePairPool,
                restored.ShapeInstancePairPool);
            bool actorPair=SameActorPairPoolSnapshot(target.ActorPairPool,
                restored.ActorPairPool);
            bool actorPairReport=SameActorPairReportPoolSnapshot(
                target.ActorPairReportPool,restored.ActorPairReportPool);
            bool nphasePoolsRaw=SameRawNPhasePoolImages(target.NPhasePoolImages,
                restored.NPhasePoolImages);
            bool nphaseReports=SameNPhaseReportState(target.NPhaseReports,
                restored.NPhaseReports);
            bool graph=SameInteractionGraphState(target.InteractionGraph,
                restored.InteractionGraph);
            bool transformCache=SameTransformCacheState(target.TransformCache,
                restored.TransformCache);
            bool islandRaw=SameIslandSnapshotState(target.IslandSnapshot,
                restored.IslandSnapshot);
            bool island=SameIslandPhysicalSnapshotState(target.IslandSnapshot,
                restored.IslandSnapshot);
            bool large=SameManifoldPoolSnapshot(target.LargeManifoldPool,
                restored.LargeManifoldPool);
            bool sphere=SameManifoldPoolSnapshot(target.SphereManifoldPool,
                restored.SphereManifoldPool);
            bool dispatch=SameTransformDispatchSnapshot(target.TransformDispatch,
                restored.TransformDispatch);
            bool dirty=SameDirtyInteractionSnapshot(target.DirtyInteractions,
                restored.DirtyInteractions);
            bool frame=target.Frame==restored.Frame;
            return new Dictionary<string,object>{{"frameEqual",frame},
                {"contactFreeEqual",contactFree},{"contactOwnersEqual",contactOwners},
                {"shapeInstancePairPoolEqual",sip},{"actorPairPoolEqual",actorPair},
                {"actorPairReportPoolEqual",actorPairReport},
                {"nphasePoolImagesRawEqual",nphasePoolsRaw},
                {"nphaseReportsEqual",nphaseReports},{"interactionGraphEqual",graph},
                {"transformCacheEqual",transformCache},{"islandSnapshotEqual",island},
                {"islandSnapshotRawEqual",islandRaw},
                {"largeManifoldPoolEqual",large},{"sphereManifoldPoolEqual",sphere},
                {"transformDispatchEqual",dispatch},{"dirtyInteractionsEqual",dirty},
                {"allRawFamiliesEqual",frame&&contactFree&&contactOwners&&sip&&actorPair&&
                    actorPairReport&&nphasePoolsRaw&&nphaseReports&&graph&&transformCache&&island&&large&&
                    sphere&&dispatch&&dirty}};
        }

        private static object DescribeSipPoolState(SipPoolState value)
        {
            if(value==null)return null;
            object[] top=value.Order.Take(16).Select(pointer=>(object)("0x"+pointer.ToString("X8"))).ToArray();
            return new Dictionary<string,object>{{"nphaseCore","0x"+value.NPhaseCore.ToString("X8")},
                {"pool","0x"+value.Pool.ToString("X8")},{"freeCount",value.Order.Length},
                {"elementSize",value.ElementSize},{"elementsPerSlab",value.ElementsPerSlab},
                {"used",value.Used},{"unreleased",value.Unreleased},{"slabSize",value.SlabSize},
                {"freeHead",value.Order.Length==0?"0x00000000":"0x"+value.Order[0].ToString("X8")},
                {"orderHash","0x"+value.OrderHash.ToString("X8")},{"top",top}};
        }

        private static object DescribeActorPairPoolState(ActorPairPoolState value)
        {
            if(value==null)return null;
            object[] topFree=value.FreeOrder.Take(16).Select(pointer=>(object)("0x"+pointer.ToString("X8"))).ToArray();
            object[] topAllocated=value.AllocatedOrder.Take(16).Select(pointer=>(object)("0x"+pointer.ToString("X8"))).ToArray();
            return new Dictionary<string,object>{{"nphaseCore","0x"+value.NPhaseCore.ToString("X8")},
                {"pool","0x"+value.Pool.ToString("X8")},{"elementSize",value.ElementSize},
                {"elementsPerSlab",value.ElementsPerSlab},{"slabSize",value.SlabSize},
                {"slabCount",value.SlabCount},{"totalElements",value.TotalElements},
                {"used",value.Used},{"unreleased",value.Unreleased},
                {"freeOrderHash","0x"+value.FreeOrderHash.ToString("X8")},
                {"allocatedOrderHash","0x"+value.AllocatedOrderHash.ToString("X8")},
                {"topFree",topFree},{"topAllocated",topAllocated}};
        }

        private static object DescribeActorPairReportPoolState(ActorPairReportPoolState value)
        {
            if(value==null)return null;
            object[] topFree=value.FreeOrder.Take(16).Select(pointer=>(object)("0x"+pointer.ToString("X8"))).ToArray();
            object[] topAllocated=value.AllocatedOrder.Take(16).Select(pointer=>(object)("0x"+pointer.ToString("X8"))).ToArray();
            string[] contentHashes=value.AllocatedBytes.Select(bytes=>
            {
                using(var algorithm=SHA256.Create())
                    return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-","");
            }).ToArray();
            return new Dictionary<string,object>{{"nphaseCore","0x"+value.NPhaseCore.ToString("X8")},
                {"pool","0x"+value.Pool.ToString("X8")},{"elementSize",value.ElementSize},
                {"elementsPerSlab",value.ElementsPerSlab},{"slabSize",value.SlabSize},
                {"slabCount",value.SlabCount},{"totalElements",value.TotalElements},
                {"used",value.Used},{"unreleased",value.Unreleased},
                {"freeOrderHash","0x"+value.FreeOrderHash.ToString("X8")},
                {"allocatedOrderHash","0x"+value.AllocatedOrderHash.ToString("X8")},
                {"topFree",topFree},{"topAllocated",topAllocated},{"contentSha256",contentHashes}};
        }

        private static object DescribeNPhasePoolImages(NPhasePoolImageState[] values)
        {
            if(values==null)return null;
            return values.Select(value=>(object)new Dictionary<string,object>{
                {"poolKind",value.Receipt.PoolKind},
                {"nphaseCore",Hex(value.Receipt.NPhaseCore)},
                {"pool",Hex(value.Receipt.Pool)},
                {"slabsData",Hex(value.Receipt.SlabsData)},
                {"slabCapacityRaw","0x"+value.Receipt.SlabCapacityRaw.ToString("X8")},
                {"slabCount",value.Receipt.SlabCount},
                {"elementSize",value.Receipt.ElementSize},
                {"slabSize",value.Receipt.SlabSize},
                {"totalSlots",value.Receipt.TotalSlots},
                {"used",value.Receipt.Used},
                {"unreleased",value.Receipt.Unreleased},
                {"freeHead",Hex(value.Receipt.FreeHead)},
                {"freeHeadSlot",value.Receipt.FreeHeadSlot==0xFFFFFFFFu?
                    (object)null:value.Receipt.FreeHeadSlot},
                {"slabBases",HexArray(value.SlabBases)},
                {"freeSlots",value.FreeSlots.Cast<object>().ToArray()},
                {"allocationWords",value.AllocationWords.Select(word=>(object)
                    ("0x"+word.ToString("X8"))).ToArray()},
                {"metadataHash","0x"+value.Receipt.MetadataHash.ToString("X8")},
                {"slabBaseHash","0x"+value.Receipt.SlabBaseHash.ToString("X8")},
                {"freeSlotOrderHash","0x"+value.Receipt.FreeSlotOrderHash.ToString("X8")},
                {"allocationBitmapHash","0x"+
                    value.Receipt.AllocationBitmapHash.ToString("X8")},
                {"slabByteHash","0x"+value.Receipt.SlabByteHash.ToString("X8")},
                {"snapshotHash","0x"+value.Receipt.SnapshotHash.ToString("X8")},
                {"slabBytesSha256",ContentSha256(value.SlabBytes)},
                {"validationFlags","0x"+value.Receipt.ValidationFlags.ToString("X8")}
            }).ToArray();
        }

        private static object DescribeNPhaseReportState(NPhaseReportState value)
        {
            if(value==null)return null;
            NativeNPhaseReportStateReceipt receipt=value.Receipt;
            return new Dictionary<string,object>{
                {"nphaseCore",Hex(receipt.NPhaseCore)},{"ownerScene",Hex(receipt.OwnerScene)},
                {"actorPairData",Hex(receipt.ActorPairData)},
                {"actorPairCount",receipt.ActorPairCount},
                {"actorPairCapacityRaw","0x"+receipt.ActorPairCapacityRaw.ToString("X8")},
                {"actorPairOrderHash","0x"+receipt.ActorPairOrderHash.ToString("X8")},
                {"actorPairs",HexArray(value.ActorPairs)},
                {"persistentData",Hex(receipt.PersistentData)},
                {"persistentCount",receipt.PersistentCount},
                {"persistentCapacityRaw","0x"+receipt.PersistentCapacityRaw.ToString("X8")},
                {"nextFramePersistentIndex",receipt.NextFramePersistentIndex},
                {"persistentOrderHash","0x"+receipt.PersistentOrderHash.ToString("X8")},
                {"persistentSips",HexArray(value.PersistentSips)},
                {"forceThresholdData",Hex(receipt.ForceThresholdData)},
                {"forceThresholdCount",receipt.ForceThresholdCount},
                {"forceThresholdCapacityRaw","0x"+receipt.ForceThresholdCapacityRaw.ToString("X8")},
                {"forceThresholdOrderHash","0x"+receipt.ForceThresholdOrderHash.ToString("X8")},
                {"forceThresholdSips",HexArray(value.ForceThresholdSips)},
                {"reportBuffer",Hex(receipt.ReportBuffer)},
                {"reportBufferCurrentIndex",receipt.ReportBufferCurrentIndex},
                {"reportBufferCurrentSize",receipt.ReportBufferCurrentSize},
                {"reportBufferDefaultSize",receipt.ReportBufferDefaultSize},
                {"reportBufferLastIndex","0x"+receipt.ReportBufferLastIndex.ToString("X8")},
                {"reportBufferAllocationLocked",receipt.ReportBufferAllocationLocked!=0},
                {"reportBufferActiveHash","0x"+receipt.ReportBufferActiveHash.ToString("X8")},
                {"reportBufferAllocationHash","0x"+receipt.ReportBufferAllocationHash.ToString("X8")},
                {"validationFlags","0x"+receipt.ValidationFlags.ToString("X8")}};
        }

        private static object DescribeInteractionGraphState(InteractionGraphState value)
        {
            if(value==null)return null;
            NativeInteractionGraphReceipt receipt=value.Receipt;
            object[] global=Enumerable.Range(0,6).Select(type=>(object)new Dictionary<string,object>{
                {"type",type},{"data",Hex(receipt.GlobalData[type])},
                {"count",receipt.GlobalCount[type]},
                {"capacityRaw","0x"+receipt.GlobalCapacityRaw[type].ToString("X8")},
                {"activeCount",receipt.GlobalActiveCount[type]},
                {"orderHash","0x"+receipt.GlobalOrderHash[type].ToString("X8")}}).ToArray();
            object[] actors=value.Actors.Select(actor=>(object)new Dictionary<string,object>{
                {"actor",Hex(actor.Actor)},{"vtable",Hex(actor.Vtable)},
                {"data",Hex(actor.InteractionsData)},{"count",actor.InteractionCount},
                {"capacity",actor.InteractionCapacity},{"slotStart",actor.InteractionOutputStart},
                {"activeBodyIndex",actor.ActiveBodyIndex==0xFFFFFFFFu?(object)null:actor.ActiveBodyIndex},
                {"transferring",actor.TransferringCount},{"unique",actor.UniqueCount},
                {"counted",actor.CountedCount},{"actorType",actor.ActorType},
                {"islandNodeInfo","0x"+actor.IslandNodeInfo.ToString("X2")},
                {"orderHash","0x"+actor.InteractionOrderHash.ToString("X8")}}).ToArray();
            object[] interactions=value.Interactions.Select(item=>(object)new Dictionary<string,object>{
                {"interaction",Hex(item.Interaction)},{"type",item.InteractionType},
                {"flags","0x"+item.InteractionFlags.ToString("X2")},
                {"globalIndex",item.GlobalIndex},{"active",item.Active!=0},
                {"actor0",Hex(item.Actor0)},{"actor1",Hex(item.Actor1)},
                {"actorId0",item.ActorId0},{"actorId1",item.ActorId1},
                {"element0",Hex(item.Element0)},{"element1",Hex(item.Element1)},
                {"pxsShapeCore0",Hex(item.PxsShapeCore0)},
                {"pxsShapeCore1",Hex(item.PxsShapeCore1)},
                {"semanticLow",Hex(item.SemanticLow)},{"semanticHigh",Hex(item.SemanticHigh)}}).ToArray();
            object[] pools=receipt.Pools.Select(pool=>(object)new Dictionary<string,object>{
                {"pool",Hex(pool.Pool)},{"blockCapacity",pool.BlockCapacity},
                {"blockBytes",pool.BlockBytes},{"slabData",Hex(pool.SlabData)},
                {"slabCount",pool.SlabCount},{"slabCapacityRaw","0x"+pool.SlabCapacityRaw.ToString("X8")},
                {"used",pool.Used},{"unreleasedFree",unchecked((int)pool.UnreleasedFree)},
                {"freeCount",pool.FreeCount},{"freeHead",Hex(pool.FreeHead)},
                {"slabOrderHash","0x"+pool.SlabOrderHash.ToString("X8")},
                {"freeOrderHash","0x"+pool.FreeOrderHash.ToString("X8")},
                {"usedOwnerHash","0x"+pool.UsedOwnerHash.ToString("X8")}}).ToArray();
            return new Dictionary<string,object>{{"nphaseCore",Hex(receipt.NPhaseCore)},
                {"ownerScene",Hex(receipt.OwnerScene)},{"interactionScene",Hex(receipt.InteractionScene)},
                {"llContext",Hex(receipt.LlContext)},{"timestamp",receipt.Timestamp},
                {"activeBodiesData",Hex(receipt.ActiveBodiesData)},
                {"activeBodiesCapacityRaw","0x"+receipt.ActiveBodiesCapacityRaw.ToString("X8")},
                {"activeTwoWayStart",receipt.ActiveTwoWayStart},{"activeBodies",HexArray(value.ActiveBodies)},
                {"global",global},{"actors",actors},{"interactions",interactions},{"pools",pools},
                {"primaryContentSha256",value.PrimaryBytes.Select(ContentSha256).ToArray()},
                {"actorSlots",HexArray(value.ActorSlots)},{"poolSlabs",HexArray(value.PoolSlabs)},
                {"poolFree",HexArray(value.PoolFree)},
                {"actorHash","0x"+receipt.ActorHash.ToString("X8")},
                {"interactionHash","0x"+receipt.InteractionHash.ToString("X8")},
                {"actorSlotHash","0x"+receipt.ActorSlotHash.ToString("X8")},
                {"poolHash","0x"+receipt.PoolHash.ToString("X8")},
                {"graphHash","0x"+receipt.GraphHash.ToString("X8")},
                {"validationFlags","0x"+receipt.ValidationFlags.ToString("X8")}};
        }

        private static object DescribeTransformCacheState(TransformCacheState value)
        {
            if(value==null)return null;
            NativeTransformCacheReceipt receipt=value.Receipt;
            object[] entries=value.Entries.Select(entry=>(object)new Dictionary<string,object>{
                {"id",entry.Id},{"refCount",entry.RefCount},{"bindingCount",entry.BindingCount},
                {"poseHash","0x"+entry.PoseHash.ToString("X8")},
                {"stateFlags","0x"+entry.StateFlags.ToString("X2")},
                {"rotationBits",entry.Rotation.Select(item=>(object)
                    ("0x"+FloatBits(item).ToString("X8"))).ToArray()},
                {"positionBits",entry.Position.Select(item=>(object)
                    ("0x"+FloatBits(item).ToString("X8"))).ToArray()}}).ToArray();
            object[] bindings=value.Bindings.Select(binding=>(object)new Dictionary<string,object>{
                {"interaction",Hex(binding.Interaction)},{"interactionIndex",binding.InteractionIndex},
                {"endpointIndex",binding.EndpointIndex},{"shapeSim",Hex(binding.ShapeSim)},
                {"shapeCore",Hex(binding.ShapeCore)},{"pxsShapeCore",Hex(binding.PxsShapeCore)},
                {"cacheId",binding.CacheId},{"refCount",binding.RefCount},
                {"poseHash","0x"+binding.PoseHash.ToString("X8")}}).ToArray();
            return new Dictionary<string,object>{{"nphaseCore",Hex(receipt.NPhaseCore)},
                {"ownerScene",Hex(receipt.OwnerScene)},{"interactionScene",Hex(receipt.InteractionScene)},
                {"context",Hex(receipt.Context)},{"transformCache",Hex(receipt.TransformCache)},
                {"currentId",receipt.CurrentId},{"liveCount",receipt.LiveCount},
                {"totalRefCount",receipt.TotalRefCount},{"freeIds",value.FreeIds.Cast<object>().ToArray()},
                {"entryHash","0x"+receipt.EntryHash.ToString("X8")},
                {"freeOrderHash","0x"+receipt.FreeOrderHash.ToString("X8")},
                {"bindingHash","0x"+receipt.BindingHash.ToString("X8")},
                {"snapshotHash","0x"+receipt.SnapshotHash.ToString("X8")},
                {"entries",entries},{"bindings",bindings},
                {"validationFlags","0x"+receipt.ValidationFlags.ToString("X8")}};
        }

        private static object DescribeFinishBroadPhaseState(FinishBroadPhaseState value)
        {
            if(value==null)return null;
            NativeFinishBroadPhaseObserverReceipt receipt=value.Receipt;
            Func<NativeBroadPhaseOverlapRecord,object> describe=overlap=>
                new Dictionary<string,object>{{"userData0",Hex(overlap.UserData0)},
                    {"userData1",Hex(overlap.UserData1)},{"shapeCore0",Hex(overlap.ShapeCore0)},
                    {"shapeCore1",Hex(overlap.ShapeCore1)},
                    {"pxsShapeCore0",Hex(overlap.PxsShapeCore0)},
                    {"pxsShapeCore1",Hex(overlap.PxsShapeCore1)},
                    {"cacheId0",overlap.CacheId0},{"cacheId1",overlap.CacheId1},
                    {"pairHash","0x"+overlap.PairHash.ToString("X8")}};
            return new Dictionary<string,object>{{"expectedScene",Hex(receipt.ExpectedScene)},
                {"expectedContext",Hex(receipt.ExpectedContext)},
                {"expectedNPhaseCore",Hex(receipt.ExpectedNPhaseCore)},
                {"observedScene",Hex(receipt.ObservedScene)},
                {"observedContext",Hex(receipt.ObservedContext)},
                {"observedNPhaseCore",Hex(receipt.ObservedNPhaseCore)},
                {"aabbManager",Hex(receipt.AabbManager)},
                {"interactionScene",Hex(receipt.InteractionScene)},
                {"transformCache",Hex(receipt.TransformCache)},
                {"expectedPass",receipt.ExpectedPass},{"pass",receipt.Pass},
                {"armedThreadId",receipt.ArmedThreadId},{"threadId",receipt.ThreadId},
                {"armedOrdinal",receipt.ArmedOrdinal},{"observationOrdinal",receipt.ObservationOrdinal},
                {"slotIndex",receipt.SlotIndex},{"createdHash","0x"+receipt.CreatedHash.ToString("X8")},
                {"deletedHash","0x"+receipt.DeletedHash.ToString("X8")},
                {"preCacheHash","0x"+receipt.PreCacheHash.ToString("X8")},
                {"postCacheHash","0x"+receipt.PostCacheHash.ToString("X8")},
                {"preGraphHash","0x"+receipt.PreGraphHash.ToString("X8")},
                {"postGraphHash","0x"+receipt.PostGraphHash.ToString("X8")},
                {"created",value.Created.Select(describe).ToArray()},
                {"deleted",value.Deleted.Select(describe).ToArray()},
                {"validationFlags","0x"+receipt.ValidationFlags.ToString("X8")},
                {"droppedObservations",receipt.DroppedObservations}};
        }

        private static object DescribeIslandSnapshotState(IslandSnapshotState value)
        {
            if(value==null)return null;
            NativeIslandSnapshotReceipt receipt=value.Receipt;
            object[] bindings=value.Bindings.Select(binding=>(object)new Dictionary<string,object>{
                {"edgeId",binding.EdgeId},{"edgeType",binding.EdgeType},
                {"sip","0x"+binding.Sip.ToString("X8")},{"hookAddress","0x"+binding.HookAddress.ToString("X8")},
                {"shapeSim0","0x"+binding.ShapeSim0.ToString("X8")},
                {"shapeSim1","0x"+binding.ShapeSim1.ToString("X8")},
                {"pxsLow","0x"+binding.PxsLow.ToString("X8")},
                {"pxsHigh","0x"+binding.PxsHigh.ToString("X8")},
                {"contactManager","0x"+binding.ContactManager.ToString("X8")},
                {"taggedRaw","0x"+binding.TaggedRaw.ToString("X8")}}).ToArray();
            return new Dictionary<string,object>{{"nphaseCore",Hex(receipt.NPhaseCore)},
                {"ownerScene",Hex(receipt.OwnerScene)},{"interactionScene",Hex(receipt.InteractionScene)},
                {"context",Hex(receipt.Context)},{"islandManager",Hex(receipt.IslandManager)},
                {"phase",receipt.Phase},{"observerSequence",receipt.ObserverSequence},
                {"observationOrdinal",receipt.ObservationOrdinal},{"captureThreadId",receipt.CaptureThreadId},
                {"epoch",receipt.Epoch},{"snapshotHash","0x"+receipt.SnapshotHash.ToString("X8")},
                {"validationFlags","0x"+receipt.ValidationFlags.ToString("X8")},
                {"nodeCapacity",receipt.NodeManager.Capacity},{"nodeFreeHead",receipt.NodeManager.FreeHead},
                {"nodeFreeCount",receipt.NodeManager.FreeCount},{"nodeHash","0x"+receipt.NodeManager.ElementHash.ToString("X8")},
                {"edgeCapacity",receipt.EdgeManager.Capacity},{"edgeFreeHead",receipt.EdgeManager.FreeHead},
                {"edgeFreeCount",receipt.EdgeManager.FreeCount},{"edgeHash","0x"+receipt.EdgeManager.ElementHash.ToString("X8")},
                {"islandCapacity",receipt.IslandManagerReceipt.Capacity},
                {"islandFreeHead",receipt.IslandManagerReceipt.FreeHead},
                {"islandFreeCount",receipt.IslandManagerReceipt.FreeCount},
                {"rootCapacity",receipt.RootManager.Capacity},{"rootFreeHead",receipt.RootManager.FreeHead},
                {"rootFreeCount",receipt.RootManager.FreeCount},
                {"liveContactEdges",receipt.LiveContactEdges},
                {"liveConstraintEdges",receipt.LiveConstraintEdges},
                {"liveArticulationEdges",receipt.LiveArticulationEdges},
                {"journalBeginOrdinal",receipt.JournalBeginOrdinal},
                {"journalEndOrdinal",receipt.JournalEndOrdinal},
                {"journalOverflowCount",receipt.JournalOverflowCount},
                {"nodeCreated",value.NodeCreated.Cast<object>().ToArray()},
                {"nodeDeleted",value.NodeDeleted.Cast<object>().ToArray()},
                {"edgeCreated",value.EdgeCreated.Cast<object>().ToArray()},
                {"edgeDeleted",value.EdgeDeleted.Cast<object>().ToArray()},
                {"edgeBroken",value.EdgeBroken.Cast<object>().ToArray()},
                {"edgeJoined",value.EdgeJoined.Cast<object>().ToArray()},
                {"bindings",bindings}};
        }

        private static object DescribeIslandTransitionState(IslandTransitionState value)
        {
            if(value==null)return null;
            NativeIslandObserverReceipt receipt=value.Receipt;
            object[] journal=value.Journal.Select(item=>(object)new Dictionary<string,object>{
                {"ordinal",item.Ordinal},{"eventKind",item.EventKind},{"observerPhase",item.ObserverPhase},
                {"threadId",item.ThreadId},{"edgeType",item.EdgeType},{"node0",item.Node0},{"node1",item.Node1},
                {"preEdgeId",item.PreEdgeId},{"postEdgeId",item.PostEdgeId},
                {"hookAddress","0x"+item.HookAddress.ToString("X8")},
                {"ownerObject","0x"+item.OwnerObject.ToString("X8")},
                {"pxsLow","0x"+item.PxsLow.ToString("X8")},
                {"pxsHigh","0x"+item.PxsHigh.ToString("X8")}}).ToArray();
            return new Dictionary<string,object>{{"expectedManager",Hex(receipt.ExpectedManager)},
                {"expectedContext",Hex(receipt.ExpectedContext)},{"expectedNPhase",Hex(receipt.ExpectedNPhase)},
                {"observedManager",Hex(receipt.ObservedManager)},{"observedContext",Hex(receipt.ObservedContext)},
                {"observedNPhase",Hex(receipt.ObservedNPhase)},{"expectedPass",receipt.ExpectedPass},
                {"pass",receipt.Pass},{"armedThreadId",receipt.ArmedThreadId},{"threadId",receipt.ThreadId},
                {"observerSequence",receipt.ObserverSequence},{"armedOrdinal",receipt.ArmedOrdinal},
                {"observationOrdinal",receipt.ObservationOrdinal},{"slotIndex",receipt.SlotIndex},
                {"preResult",receipt.PreResult},{"postResult",receipt.PostResult},
                {"preSnapshotHash","0x"+receipt.PreSnapshotHash.ToString("X8")},
                {"postSnapshotHash","0x"+receipt.PostSnapshotHash.ToString("X8")},
                {"journalBeginOrdinal",receipt.JournalBeginOrdinal},
                {"journalEndOrdinal",receipt.JournalEndOrdinal},
                {"validationFlags","0x"+receipt.ValidationFlags.ToString("X8")},
                {"inFlight",receipt.InFlight},{"pre",DescribeIslandSnapshotState(value.Pre)},
                {"post",DescribeIslandSnapshotState(value.Post)},{"journal",journal},
                {"journalRecordHash","0x"+value.JournalReceipt.RecordHash.ToString("X8")},
                {"journalOverflowCount",value.JournalReceipt.OverflowCount}};
        }

        private static uint DirtyInteractionOrderHash(NativeDirtyInteractionKey[] values)
        {
            if(values==null)throw new ArgumentNullException("values");
            uint hash=2166136261u;
            foreach(NativeDirtyInteractionKey value in values)
            {
                hash^=value.ElementLow.ToUInt32();hash*=16777619u;
                hash^=value.ElementHigh.ToUInt32();hash*=16777619u;
                hash^=value.PrimaryVtable.ToUInt32();hash*=16777619u;
                hash^=value.InteractionType;hash*=16777619u;
            }
            return hash;
        }

        private static void ValidateDirtyInteractionState(DirtyInteractionState value)
        {
            if(value==null||value.NPhaseCore==0||value.Entries==0||value.EntriesNext==0||
                value.Hash==0||value.EntriesCapacity<1||value.EntriesCapacity>MaximumDirtyInteractions||
                value.HashSize<1||(value.HashSize&(value.HashSize-1))!=0||value.Keys==null||
                value.Keys.Length>value.EntriesCapacity||value.Keys.Length>MaximumDirtyInteractions||
                value.OrderHash!=DirtyInteractionOrderHash(value.Keys))
                throw new InvalidOperationException("The dirty-interaction checkpoint sidecar is incomplete.");
            var identities=new HashSet<string>(StringComparer.Ordinal);
            foreach(NativeDirtyInteractionKey key in value.Keys)
            {
                uint low=key.ElementLow.ToUInt32(),high=key.ElementHigh.ToUInt32(),vtable=key.PrimaryVtable.ToUInt32();
                if(low==0||high==0||low>=high||vtable==0||key.InteractionType>5||
                    !identities.Add(low.ToString("X8")+high.ToString("X8")+vtable.ToString("X8")+key.InteractionType))
                    throw new InvalidOperationException("The dirty-interaction checkpoint contains an invalid or duplicate semantic key.");
            }
        }

        private static object DescribeDirtyInteractionState(DirtyInteractionState value)
        {
            if(value==null)return null;
            return new Dictionary<string,object>{{"nphaseCore","0x"+value.NPhaseCore.ToString("X8")},
                {"entries","0x"+value.Entries.ToString("X8")},{"entriesNext","0x"+value.EntriesNext.ToString("X8")},
                {"hash","0x"+value.Hash.ToString("X8")},{"entriesCapacity",value.EntriesCapacity},
                {"hashSize",value.HashSize},{"count",value.Keys==null?0:value.Keys.Length},
                {"orderHash","0x"+value.OrderHash.ToString("X8")}};
        }

        private CheckpointSidecar LatestCheckpointSidecar()
        {
            return checkpointSidecars.Count==0?null:checkpointSidecars[checkpointSidecars.Keys.Max()];
        }

        private void StoreCheckpointSidecar(CheckpointSidecar value)
        {
            if(value==null||value.Frame<0||value.CoreSnapshot==null||
                value.Context==0||value.FreeArray==0||value.ContactPoolOrder==null||
                value.ContactPoolOrder.Length<1||value.ContactPoolOrder.Length>MaximumContactManagers)
                throw new InvalidOperationException("Contact-pool checkpoint sidecar is incomplete.");
            ValidateManifoldPoolState(value.LargeManifoldPool,LargeManifoldPoolKind,"large");
            ValidateManifoldPoolState(value.SphereManifoldPool,SphereManifoldPoolKind,"sphere");
            ValidateSipPoolState(value.ShapeInstancePairPool);
            ValidateActorPairPoolState(value.ActorPairPool);
            ValidateActorPairReportPoolState(value.ActorPairReportPool);
            ValidateNPhasePoolImages(value.NPhasePoolImages);
            ValidateNPhaseReportState(value.NPhaseReports);
            ValidateInteractionGraphState(value.InteractionGraph);
            ValidateTransformCacheState(value.TransformCache);
            ValidateIslandSnapshotState(value.IslandSnapshot,IslandPhaseSettled);
            ValidatePhysicsPhaseSnapshot(value.PostTransitionSnapshot);
            ValidateContactManagerOwnerState(value.ContactManagerOwners);
            ValidateDirtyInteractionState(value.DirtyInteractions);
            ValidateFinishBroadPhaseState(value.FinishBroadPhase,value);
            ValidateIslandTransitionState(value);
            PhysicsPhaseSnapshot post=value.PostTransitionSnapshot;
            NativeIslandSnapshotReceipt entryIsland=value.IslandSnapshot.Receipt;
            NativeIslandSnapshotReceipt postIsland=post.IslandSnapshot.Receipt;
            if(post.Frame!=checked(value.Frame+1)||post.Context!=value.Context||
                post.FreeArray!=value.FreeArray||post.ShapeInstancePairPool.NPhaseCore!=
                    value.ShapeInstancePairPool.NPhaseCore||
                postIsland.OwnerScene!=entryIsland.OwnerScene||
                postIsland.InteractionScene!=entryIsland.InteractionScene||
                postIsland.Context!=entryIsland.Context||
                postIsland.IslandManager!=entryIsland.IslandManager||
                postIsland.ObserverSequence!=value.IslandTransition.Receipt.ObserverSequence||
                postIsland.JournalEndOrdinal<value.IslandTransition.Receipt.JournalEndOrdinal)
                throw new InvalidOperationException(
                    "Checkpoint post-transition snapshot is not phase-linked to its entry and transition.");
            if(!ReferenceEquals(post.CoreSnapshot,CoreCheckpointSnapshot(post.Frame)))
                throw new InvalidOperationException(
                    "Checkpoint post-transition snapshot no longer owns its retained core output image.");
            if(value.ShapeInstancePairPool.NPhaseCore!=value.DirtyInteractions.NPhaseCore||
                value.ActorPairPool.NPhaseCore!=value.DirtyInteractions.NPhaseCore||
                value.ActorPairReportPool.NPhaseCore!=value.DirtyInteractions.NPhaseCore||
                value.NPhasePoolImages.Any(pool=>pool.Receipt.NPhaseCore.ToUInt32()!=
                    value.DirtyInteractions.NPhaseCore)||
                value.NPhaseReports.Receipt.NPhaseCore.ToUInt32()!=value.DirtyInteractions.NPhaseCore||
                value.InteractionGraph.Receipt.NPhaseCore.ToUInt32()!=value.DirtyInteractions.NPhaseCore||
                value.TransformCache.Receipt.NPhaseCore.ToUInt32()!=value.DirtyInteractions.NPhaseCore||
                value.IslandSnapshot.Receipt.NPhaseCore.ToUInt32()!=value.DirtyInteractions.NPhaseCore||
                value.IslandTransition.Receipt.ExpectedNPhase.ToUInt32()!=value.DirtyInteractions.NPhaseCore||
                value.FinishBroadPhase.Receipt.ExpectedNPhaseCore.ToUInt32()!=value.DirtyInteractions.NPhaseCore)
                throw new InvalidOperationException("Checkpoint SIP and dirty-interaction state belong to different NPhaseCore instances.");
            ValidateCheckpointPoolCoherence(value);
            if(!ReferenceEquals(value.CoreSnapshot,CoreCheckpointSnapshot(value.Frame)))
                throw new InvalidOperationException("Contact-pool checkpoint sidecar no longer owns the retained core snapshot.");
            CheckpointSidecar previous;
            if(checkpointSidecars.TryGetValue(value.Frame,out previous))
            {
                bool same=ReferenceEquals(previous.CoreSnapshot,value.CoreSnapshot)&&
                    previous.Context==value.Context&&previous.FreeArray==value.FreeArray&&
                    previous.OrderHash==value.OrderHash&&
                    previous.ContactPoolOrder.SequenceEqual(value.ContactPoolOrder)&&
                    SameContactManagerOwnerSnapshot(previous.ContactManagerOwners,value.ContactManagerOwners)&&
                    SameSipPoolSnapshot(previous.ShapeInstancePairPool,value.ShapeInstancePairPool)&&
                    SameActorPairPoolSnapshot(previous.ActorPairPool,value.ActorPairPool)&&
                    SameActorPairReportPoolSnapshot(previous.ActorPairReportPool,value.ActorPairReportPool)&&
                    SameRawNPhasePoolImages(previous.NPhasePoolImages,value.NPhasePoolImages)&&
                    SameNPhaseReportState(previous.NPhaseReports,value.NPhaseReports)&&
                    SameInteractionGraphState(previous.InteractionGraph,value.InteractionGraph)&&
                    SameTransformCacheState(previous.TransformCache,value.TransformCache)&&
                    SameFinishBroadPhaseState(previous.FinishBroadPhase,value.FinishBroadPhase)&&
                    SameIslandSnapshotState(previous.IslandSnapshot,value.IslandSnapshot)&&
                    SameIslandTransitionState(previous.IslandTransition,value.IslandTransition)&&
                    SamePhysicsPhaseSnapshot(previous.PostTransitionSnapshot,
                        value.PostTransitionSnapshot)&&
                    SameManifoldPoolSnapshot(previous.LargeManifoldPool,value.LargeManifoldPool)&&
                    SameManifoldPoolSnapshot(previous.SphereManifoldPool,value.SphereManifoldPool)&&
                    SameTransformDispatchSnapshot(previous.TransformDispatch,value.TransformDispatch)&&
                    SameDirtyInteractionSnapshot(previous.DirtyInteractions,value.DirtyInteractions);
                if(!same)throw new InvalidOperationException("A differing physics-pool/Transform/transition sidecar already owns output frame "+value.Frame+".");
                return;
            }
            if(checkpointSidecars.Count>=MaximumCheckpointSidecars)
                throw new InvalidOperationException("Physics-pool checkpoint sidecar history reached its fail-closed capacity.");
            checkpointSidecars.Add(value.Frame,value);
        }

        private static void ValidateCheckpointPoolCoherence(CheckpointSidecar value)
        {
            if(value==null||value.ContactManagerOwners==null||
                value.ContactManagerOwners.Records==null||value.ContactPoolOrder==null||
                value.ShapeInstancePairPool==null||value.ShapeInstancePairPool.Order==null||
                value.ActorPairPool==null||value.ActorPairPool.FreeOrder==null||
                value.ActorPairPool.AllocatedOrder==null||
                value.ActorPairReportPool==null||value.ActorPairReportPool.FreeOrder==null||
                value.ActorPairReportPool.AllocatedOrder==null||
                value.NPhasePoolImages==null||
                value.NPhaseReports==null||
                value.InteractionGraph==null||value.InteractionGraph.Interactions==null||
                value.TransformCache==null||value.TransformCache.Entries==null||
                value.TransformCache.FreeIds==null||value.TransformCache.Bindings==null||
                value.IslandSnapshot==null||
                value.LargeManifoldPool==null||value.LargeManifoldPool.Order==null||
                value.SphereManifoldPool==null||value.SphereManifoldPool.Order==null)
                throw new InvalidOperationException("Checkpoint pool-coherence inputs are incomplete.");
            ValidateNPhasePoolImages(value.NPhasePoolImages);
            bool completePoolsCoherent=NPhasePoolImagesCoherentWithLegacy(
                value.NPhasePoolImages,value.ShapeInstancePairPool,value.ActorPairPool,
                value.ActorPairReportPool,value.InteractionGraph);
            NativeContactManagerOwnerRecord[] owners=value.ContactManagerOwners.Records;
            var contactFree=new HashSet<uint>(value.ContactPoolOrder);
            var sipFree=new HashSet<uint>(value.ShapeInstancePairPool.Order);
            var actorPairFree=new HashSet<uint>(value.ActorPairPool.FreeOrder);
            var actorPairAllocated=new HashSet<uint>(value.ActorPairPool.AllocatedOrder);
            var actorPairReportFree=new HashSet<uint>(value.ActorPairReportPool.FreeOrder);
            var actorPairReportAllocated=new HashSet<uint>(value.ActorPairReportPool.AllocatedOrder);
            var largeFree=new HashSet<uint>(value.LargeManifoldPool.Order);
            var sphereFree=new HashSet<uint>(value.SphereManifoldPool.Order);
            ValidateNPhaseReportState(value.NPhaseReports);
            uint[] managers=owners.Select(owner=>owner.Manager.ToUInt32()).ToArray();
            uint[] sips=owners.Select(owner=>owner.Sip.ToUInt32()).ToArray();
            uint[] actorPairs=owners.Select(owner=>owner.ActorPair.ToUInt32()).Distinct().ToArray();
            uint[] actorPairReports=owners.Select(owner=>owner.ActorPairReportData.ToUInt32())
                .Where(pointer=>pointer!=0).Distinct().ToArray();
            uint[] large=owners.Where(owner=>owner.ManifoldBytes==0xF0u&&owner.Manifold.ToUInt32()>1u)
                .Select(owner=>owner.Manifold.ToUInt32()).ToArray();
            uint[] sphere=owners.Where(owner=>owner.ManifoldBytes==0x60u&&owner.Manifold.ToUInt32()>1u)
                .Select(owner=>owner.Manifold.ToUInt32()).ToArray();
            bool actorPairRowsCoherent=owners.GroupBy(owner=>owner.ActorPair.ToUInt32()).All(group=>
            {
                NativeContactManagerOwnerRecord first=group.First();
                return first.ActorPair.ToUInt32()!=0&&
                    first.ActorPairRefCount==group.Count()+((first.ActorPairInternalFlags&1u)!=0?1:0)&&
                    group.All(owner=>owner.ActorPairActor0==first.ActorPairActor0&&
                        owner.ActorPairActor1==first.ActorPairActor1&&
                        owner.ActorPairScene==first.ActorPairScene&&
                        owner.ActorPairInternalFlags==first.ActorPairInternalFlags&&
                        owner.ActorPairTouchCount==first.ActorPairTouchCount&&
                        owner.ActorPairRefCount==first.ActorPairRefCount&&
                        owner.ActorPairReportData==first.ActorPairReportData&&
                    owner.ActorPairHash==first.ActorPairHash);
            });
            var ownerBySip=owners.ToDictionary(owner=>owner.Sip.ToUInt32());
            uint[] expectedReportActorPairs=owners.GroupBy(owner=>owner.ActorPair.ToUInt32())
                .Where(group=>(group.First().ActorPairInternalFlags&1u)!=0)
                .Select(group=>group.Key).ToArray();
            uint[] expectedPersistentSips=owners.Where(owner=>(owner.SipFlags&0x00200000u)!=0)
                .Select(owner=>owner.Sip.ToUInt32()).ToArray();
            uint[] expectedForceSips=owners.Where(owner=>(owner.SipFlags&0x00800000u)!=0)
                .Select(owner=>owner.Sip.ToUInt32()).ToArray();
            bool reportListIndices=true;
            for(int i=0;i<value.NPhaseReports.PersistentSips.Length;i++)
            {
                NativeContactManagerOwnerRecord owner;
                if(!ownerBySip.TryGetValue(value.NPhaseReports.PersistentSips[i],out owner)||
                    owner.ReportPairIndex!=(uint)i)reportListIndices=false;
            }
            for(int i=0;i<value.NPhaseReports.ForceThresholdSips.Length;i++)
            {
                NativeContactManagerOwnerRecord owner;
                if(!ownerBySip.TryGetValue(value.NPhaseReports.ForceThresholdSips[i],out owner)||
                    owner.ReportPairIndex!=(uint)i)reportListIndices=false;
            }
            bool nphaseReportCoherent=
                value.NPhaseReports.Receipt.NPhaseCore.ToUInt32()==value.ShapeInstancePairPool.NPhaseCore&&
                new HashSet<uint>(value.NPhaseReports.ActorPairs).SetEquals(expectedReportActorPairs)&&
                new HashSet<uint>(value.NPhaseReports.PersistentSips).SetEquals(expectedPersistentSips)&&
                new HashSet<uint>(value.NPhaseReports.ForceThresholdSips).SetEquals(expectedForceSips)&&
                reportListIndices&&owners.Where(owner=>(owner.SipFlags&0x00A00000u)==0)
                    .All(owner=>owner.ReportPairIndex==0xFFFFFFFFu);
            ValidateInteractionGraphState(value.InteractionGraph);
            ValidateTransformCacheState(value.TransformCache);
            ValidateIslandSnapshotState(value.IslandSnapshot,IslandPhaseSettled);
            ValidateIslandSnapshotCoherence(value,value.IslandSnapshot);
            NativeInteractionGraphReceipt graphReceipt=value.InteractionGraph.Receipt;
            var graphByPointer=value.InteractionGraph.Interactions.ToDictionary(
                item=>item.Interaction.ToUInt32());
            bool interactionGraphCoherent=graphReceipt.NPhaseCore.ToUInt32()==
                    value.ShapeInstancePairPool.NPhaseCore&&
                graphReceipt.OwnerScene==value.NPhaseReports.Receipt.OwnerScene&&
                graphReceipt.LlContext.ToUInt32()==value.Context&&
                owners.All(owner=>
                {
                    NativeInteractionGraphInteractionRecord item;
                    uint interaction=unchecked(owner.Sip.ToUInt32()+8u);
                    if(!graphByPointer.TryGetValue(interaction,out item)||
                        item.InteractionType!=0||(item.InteractionFlags&0x10u)==0)return false;
                    uint owner0=owner.PxsShapeCore0.ToUInt32();
                    uint owner1=owner.PxsShapeCore1.ToUInt32();
                    uint item0=item.PxsShapeCore0.ToUInt32();
                    uint item1=item.PxsShapeCore1.ToUInt32();
                    return (owner0==item0&&owner1==item1)||(owner0==item1&&owner1==item0);
                });
            NativeTransformCacheReceipt cacheReceipt=value.TransformCache.Receipt;
            uint typeZeroCount=graphReceipt.GlobalCount[0];
            bool transformCacheCoherent=cacheReceipt.NPhaseCore==graphReceipt.NPhaseCore&&
                cacheReceipt.OwnerScene==graphReceipt.OwnerScene&&
                cacheReceipt.InteractionScene==graphReceipt.InteractionScene&&
                cacheReceipt.Context==graphReceipt.LlContext&&
                cacheReceipt.Context.ToUInt32()==value.Context&&
                value.TransformCache.Bindings.Length==checked(owners.Length*2)&&
                value.TransformCache.Bindings.All(binding=>binding.InteractionIndex<typeZeroCount);
            if(transformCacheCoherent)foreach(IGrouping<uint,NativeTransformCacheBindingRecord> group in
                value.TransformCache.Bindings.GroupBy(binding=>binding.InteractionIndex))
            {
                uint index=group.Key;
                NativeInteractionGraphInteractionRecord item=value.InteractionGraph.Interactions[index];
                NativeTransformCacheBindingRecord[] pair=group.OrderBy(binding=>binding.EndpointIndex).ToArray();
                if(pair.Length!=2||pair[0].EndpointIndex!=0||pair[1].EndpointIndex!=1)
                    transformCacheCoherent=false;
                foreach(NativeTransformCacheBindingRecord binding in pair)
                {
                    uint endpoint=binding.EndpointIndex;
                    UIntPtr shapeSim=endpoint==0?item.Element0:item.Element1;
                    UIntPtr shapeCore=endpoint==0?item.ShapeCore0:item.ShapeCore1;
                    UIntPtr pxsShapeCore=endpoint==0?item.PxsShapeCore0:item.PxsShapeCore1;
                    if(binding.Interaction!=item.Interaction||binding.InteractionIndex!=index||
                        binding.EndpointIndex!=endpoint||binding.ShapeSim!=shapeSim||
                        binding.ShapeCore!=shapeCore||binding.PxsShapeCore!=pxsShapeCore)
                        transformCacheCoherent=false;
                }
            }
            if(transformCacheCoherent)foreach(NativeContactManagerOwnerRecord owner in owners)
            {
                uint interaction=unchecked(owner.Sip.ToUInt32()+8u);
                NativeTransformCacheBindingRecord[] pair=value.TransformCache.Bindings
                    .Where(binding=>binding.Interaction.ToUInt32()==interaction).ToArray();
                if(pair.Length!=2)transformCacheCoherent=false;
                else foreach(NativeTransformCacheBindingRecord binding in pair)
                {
                    bool endpoint0=binding.PxsShapeCore==owner.PxsShapeCore0&&
                        binding.CacheId==owner.TransformCache0;
                    bool endpoint1=binding.PxsShapeCore==owner.PxsShapeCore1&&
                        binding.CacheId==owner.TransformCache1;
                    if(!endpoint0&&!endpoint1)transformCacheCoherent=false;
                }
            }
            NativeContactManagerOwnerReceipt receipt=value.ContactManagerOwners.Receipt;
            if(receipt.Context.ToUInt32()!=value.Context||receipt.FreeArray.ToUInt32()!=value.FreeArray||
                receipt.FreeCount!=(uint)value.ContactPoolOrder.Length||receipt.FreeOrderHash!=value.OrderHash||
                managers.Distinct().Count()!=managers.Length||sips.Distinct().Count()!=sips.Length||
                large.Distinct().Count()!=large.Length||sphere.Distinct().Count()!=sphere.Length||
                managers.Any(contactFree.Contains)||sips.Any(sipFree.Contains)||
                actorPairs.Any(actorPairFree.Contains)||actorPairs.Any(pointer=>!actorPairAllocated.Contains(pointer))||
                actorPairReports.Any(actorPairReportFree.Contains)||
                actorPairReports.Any(pointer=>!actorPairReportAllocated.Contains(pointer))||
                value.ActorPairReportPool.Used!=(uint)actorPairReports.Length||
                large.Any(largeFree.Contains)||sphere.Any(sphereFree.Contains)||
                receipt.UsedCount!=(uint)owners.Length||
                value.ShapeInstancePairPool.Used<(uint)sips.Length||
                value.ActorPairPool.Used!=(uint)actorPairs.Length||
                !actorPairRowsCoherent||!nphaseReportCoherent||!interactionGraphCoherent||
                !completePoolsCoherent||!transformCacheCoherent||
                value.LargeManifoldPool.Used<(uint)large.Length||
                value.SphereManifoldPool.Used<(uint)sphere.Length)
                throw new InvalidOperationException("Checkpoint contact owners and allocator partitions were not captured at one coherent physics boundary.");
        }

        private static bool SameManifoldPoolSnapshot(ManifoldPoolState left,ManifoldPoolState right)
        {
            if(left==null||right==null)return left==right;
            return left.PoolKind==right.PoolKind&&left.Pool==right.Pool&&left.OrderHash==right.OrderHash&&
                left.ElementSize==right.ElementSize&&left.ElementsPerSlab==right.ElementsPerSlab&&
                left.Used==right.Used&&left.Unreleased==right.Unreleased&&left.SlabSize==right.SlabSize&&
                left.Order!=null&&right.Order!=null&&left.Order.SequenceEqual(right.Order);
        }

        private static bool SameSipPoolSnapshot(SipPoolState left,SipPoolState right)
        {
            if(left==null||right==null)return left==right;
            return left.NPhaseCore==right.NPhaseCore&&left.Pool==right.Pool&&
                left.OrderHash==right.OrderHash&&left.ElementSize==right.ElementSize&&
                left.ElementsPerSlab==right.ElementsPerSlab&&left.Used==right.Used&&
                left.Unreleased==right.Unreleased&&left.SlabSize==right.SlabSize&&
                left.Order!=null&&right.Order!=null&&left.Order.SequenceEqual(right.Order);
        }

        private static bool SameActorPairPoolSnapshot(ActorPairPoolState left,ActorPairPoolState right)
        {
            if(left==null||right==null)return left==right;
            return left.NPhaseCore==right.NPhaseCore&&left.Pool==right.Pool&&
                left.FreeOrderHash==right.FreeOrderHash&&
                left.AllocatedOrderHash==right.AllocatedOrderHash&&
                left.ElementSize==right.ElementSize&&left.ElementsPerSlab==right.ElementsPerSlab&&
                left.Used==right.Used&&left.Unreleased==right.Unreleased&&
                left.SlabSize==right.SlabSize&&left.SlabCount==right.SlabCount&&
                left.TotalElements==right.TotalElements&&left.FreeOrder!=null&&right.FreeOrder!=null&&
                left.AllocatedOrder!=null&&right.AllocatedOrder!=null&&
                left.FreeOrder.SequenceEqual(right.FreeOrder)&&
                left.AllocatedOrder.SequenceEqual(right.AllocatedOrder);
        }

        private static bool SameActorPairReportPoolSnapshot(ActorPairReportPoolState left,
            ActorPairReportPoolState right)
        {
            if(left==null||right==null)return left==right;
            bool same=left.NPhaseCore==right.NPhaseCore&&left.Pool==right.Pool&&
                left.FreeOrderHash==right.FreeOrderHash&&
                left.AllocatedOrderHash==right.AllocatedOrderHash&&
                left.ElementSize==right.ElementSize&&left.ElementsPerSlab==right.ElementsPerSlab&&
                left.Used==right.Used&&left.Unreleased==right.Unreleased&&
                left.SlabSize==right.SlabSize&&left.SlabCount==right.SlabCount&&
                left.TotalElements==right.TotalElements&&left.FreeOrder!=null&&right.FreeOrder!=null&&
                left.AllocatedOrder!=null&&right.AllocatedOrder!=null&&
                left.AllocatedBytes!=null&&right.AllocatedBytes!=null&&
                left.FreeOrder.SequenceEqual(right.FreeOrder)&&
                left.AllocatedOrder.SequenceEqual(right.AllocatedOrder)&&
                left.AllocatedBytes.Length==right.AllocatedBytes.Length;
            if(!same)return false;
            for(int i=0;i<left.AllocatedBytes.Length;i++)
                if(!left.AllocatedBytes[i].SequenceEqual(right.AllocatedBytes[i]))return false;
            return true;
        }

        private static bool SameRawNPhasePoolImages(NPhasePoolImageState[] left,
            NPhasePoolImageState[] right)
        {
            if(left==null||right==null||left.Length!=right.Length)return left==right;
            for(int i=0;i<left.Length;i++)
                if(!SameRawNPhasePoolImage(left[i],right[i]))return false;
            return true;
        }

        private static bool SameRawNPhasePoolImage(NPhasePoolImageState left,
            NPhasePoolImageState right)
        {
            if(left==null||right==null)return left==right;
            return left.Receipt.Equals(right.Receipt)&&
                left.SlabBases!=null&&right.SlabBases!=null&&
                left.FreeSlots!=null&&right.FreeSlots!=null&&
                left.AllocationWords!=null&&right.AllocationWords!=null&&
                left.SlabBytes!=null&&right.SlabBytes!=null&&
                left.SlabBases.SequenceEqual(right.SlabBases)&&
                left.FreeSlots.SequenceEqual(right.FreeSlots)&&
                left.AllocationWords.SequenceEqual(right.AllocationWords)&&
                left.SlabBytes.SequenceEqual(right.SlabBytes);
        }

        private static bool SameNPhaseReportState(NPhaseReportState left,NPhaseReportState right)
        {
            if(left==null||right==null)return left==right;
            return left.Receipt.Equals(right.Receipt)&&left.ActorPairs!=null&&right.ActorPairs!=null&&
                left.PersistentSips!=null&&right.PersistentSips!=null&&
                left.ForceThresholdSips!=null&&right.ForceThresholdSips!=null&&
                left.ReportBufferBytes!=null&&right.ReportBufferBytes!=null&&
                left.ActorPairs.SequenceEqual(right.ActorPairs)&&
                left.PersistentSips.SequenceEqual(right.PersistentSips)&&
                left.ForceThresholdSips.SequenceEqual(right.ForceThresholdSips)&&
                left.ReportBufferBytes.SequenceEqual(right.ReportBufferBytes);
        }

        private static bool SameInteractionGraphPool(NativeInteractionGraphPoolReceipt a,
            NativeInteractionGraphPoolReceipt b)
        {
            return a.Pool==b.Pool&&a.SlabData==b.SlabData&&a.FreeHead==b.FreeHead&&
                a.BlockCapacity==b.BlockCapacity&&a.BlockBytes==b.BlockBytes&&
                a.InlineBufferUsed==b.InlineBufferUsed&&a.SlabCount==b.SlabCount&&
                a.SlabCapacityRaw==b.SlabCapacityRaw&&a.ElementsPerSlab==b.ElementsPerSlab&&
                a.Used==b.Used&&a.UnreleasedFree==b.UnreleasedFree&&a.SlabSize==b.SlabSize&&
                a.TotalElements==b.TotalElements&&a.FreeCount==b.FreeCount&&
                a.SlabOutputStart==b.SlabOutputStart&&a.FreeOutputStart==b.FreeOutputStart&&
                a.SlabOrderHash==b.SlabOrderHash&&a.FreeOrderHash==b.FreeOrderHash&&
                a.UsedOwnerHash==b.UsedOwnerHash&&a.ValidationFlags==b.ValidationFlags;
        }

        private static bool SameInteractionGraphActor(NativeInteractionGraphActorRecord a,
            NativeInteractionGraphActorRecord b)
        {
            return a.Actor==b.Actor&&a.Vtable==b.Vtable&&a.InlineSlots!=null&&b.InlineSlots!=null&&
                a.InlineSlots.SequenceEqual(b.InlineSlots)&&a.InteractionsData==b.InteractionsData&&
                a.FirstElement==b.FirstElement&&a.InteractionScene==b.InteractionScene&&
                a.SceneArrayIndex==b.SceneArrayIndex&&a.InteractionOutputStart==b.InteractionOutputStart&&
                a.InteractionCount==b.InteractionCount&&a.InteractionCapacity==b.InteractionCapacity&&
                a.ActiveBodyIndex==b.ActiveBodyIndex&&a.InteractionOrderHash==b.InteractionOrderHash&&
                a.TransferringCount==b.TransferringCount&&a.UniqueCount==b.UniqueCount&&
                a.CountedCount==b.CountedCount&&a.ActorType==b.ActorType&&
                a.IslandNodeInfo==b.IslandNodeInfo&&a.ValidationFlags==b.ValidationFlags;
        }

        private static bool SameInteractionGraphInteraction(
            NativeInteractionGraphInteractionRecord a,NativeInteractionGraphInteractionRecord b)
        {
            return a.Interaction==b.Interaction&&a.Vtable==b.Vtable&&a.Actor0==b.Actor0&&
                a.Actor1==b.Actor1&&a.Element0==b.Element0&&a.Element1==b.Element1&&
                a.ShapeCore0==b.ShapeCore0&&a.ShapeCore1==b.ShapeCore1&&
                a.PxsShapeCore0==b.PxsShapeCore0&&a.PxsShapeCore1==b.PxsShapeCore1&&
                a.SemanticLow==b.SemanticLow&&a.SemanticHigh==b.SemanticHigh&&
                a.SceneId==b.SceneId&&a.GlobalIndex==b.GlobalIndex&&a.Active==b.Active&&
                a.ActorId0==b.ActorId0&&a.ActorId1==b.ActorId1&&
                a.InteractionType==b.InteractionType&&a.InteractionFlags==b.InteractionFlags&&
                a.Reserved==b.Reserved&&a.ValidationFlags==b.ValidationFlags;
        }

        private static bool SameInteractionGraphState(InteractionGraphState left,
            InteractionGraphState right)
        {
            if(left==null||right==null)return left==right;
            NativeInteractionGraphReceipt a=left.Receipt,b=right.Receipt;
            bool receipt=a.ApiVersion==b.ApiVersion&&a.StructSize==b.StructSize&&
                a.Result==b.Result&&a.LastError==b.LastError&&a.UnityBase==b.UnityBase&&
                a.NPhaseCore==b.NPhaseCore&&a.OwnerScene==b.OwnerScene&&
                a.InteractionScene==b.InteractionScene&&a.LlContext==b.LlContext&&
                a.Timestamp==b.Timestamp&&a.ActiveBodiesData==b.ActiveBodiesData&&
                a.ActiveBodiesCount==b.ActiveBodiesCount&&
                a.ActiveBodiesCapacityRaw==b.ActiveBodiesCapacityRaw&&
                a.ActiveTwoWayStart==b.ActiveTwoWayStart&&a.GlobalData.SequenceEqual(b.GlobalData)&&
                a.GlobalCount.SequenceEqual(b.GlobalCount)&&
                a.GlobalCapacityRaw.SequenceEqual(b.GlobalCapacityRaw)&&
                a.GlobalActiveCount.SequenceEqual(b.GlobalActiveCount)&&
                a.GlobalOrderHash.SequenceEqual(b.GlobalOrderHash)&&
                a.ActiveBodiesRequired==b.ActiveBodiesRequired&&a.ActorsRequired==b.ActorsRequired&&
                a.InteractionsRequired==b.InteractionsRequired&&a.ActorSlotsRequired==b.ActorSlotsRequired&&
                a.PoolSlabsRequired==b.PoolSlabsRequired&&a.PoolFreeRequired==b.PoolFreeRequired&&
                a.ActorHash==b.ActorHash&&a.InteractionHash==b.InteractionHash&&
                a.ActorSlotHash==b.ActorSlotHash&&a.PoolHash==b.PoolHash&&
                a.GraphHash==b.GraphHash&&a.ValidationFlags==b.ValidationFlags&&
                a.Pools.Length==b.Pools.Length;
            if(!receipt)return false;
            for(int i=0;i<a.Pools.Length;i++)if(!SameInteractionGraphPool(a.Pools[i],b.Pools[i]))return false;
            if(!left.ActiveBodies.SequenceEqual(right.ActiveBodies)||
                !left.ActorSlots.SequenceEqual(right.ActorSlots)||
                !left.PoolSlabs.SequenceEqual(right.PoolSlabs)||
                !left.PoolFree.SequenceEqual(right.PoolFree)||
                !SameByteMatrix(left.PrimaryBytes,right.PrimaryBytes)||
                left.Actors.Length!=right.Actors.Length||
                left.Interactions.Length!=right.Interactions.Length)return false;
            for(int i=0;i<left.Actors.Length;i++)
                if(!SameInteractionGraphActor(left.Actors[i],right.Actors[i]))return false;
            for(int i=0;i<left.Interactions.Length;i++)
                if(!SameInteractionGraphInteraction(left.Interactions[i],right.Interactions[i]))return false;
            return true;
        }

        private static bool SameTransformCacheState(TransformCacheState left,
            TransformCacheState right)
        {
            if(left==null||right==null)return left==right;
            if(!left.Receipt.Equals(right.Receipt)||left.Entries==null||right.Entries==null||
                left.FreeIds==null||right.FreeIds==null||left.Bindings==null||right.Bindings==null||
                left.Entries.Length!=right.Entries.Length||
                left.Bindings.Length!=right.Bindings.Length||
                !left.FreeIds.SequenceEqual(right.FreeIds))return false;
            return SameTransformCacheEntries(left.Entries,right.Entries)&&
                SameTransformCacheBindings(left.Bindings,right.Bindings);
        }

        private static bool SameTransformCacheEntries(NativeTransformCacheEntryRecord[] left,
            NativeTransformCacheEntryRecord[] right)
        {
            if(left==null||right==null||left.Length!=right.Length)return left==right;
            for(int i=0;i<left.Length;i++)
            {
                NativeTransformCacheEntryRecord a=left[i],b=right[i];
                if(a.Id!=b.Id||a.RefCount!=b.RefCount||a.PoseHash!=b.PoseHash||
                    a.BindingCount!=b.BindingCount||a.StateFlags!=b.StateFlags||
                    !SameFloatBits(a.Rotation,b.Rotation)||!SameFloatBits(a.Position,b.Position))
                    return false;
            }
            return true;
        }

        private static bool SameActiveTransformCacheEntries(
            NativeTransformCacheEntryRecord[] left,NativeTransformCacheEntryRecord[] right)
        {
            if(left==null||right==null)return left==right;
            uint[] leftIds=left.Where(entry=>entry.RefCount!=0).Select(entry=>entry.Id).ToArray();
            uint[] rightIds=right.Where(entry=>entry.RefCount!=0).Select(entry=>entry.Id).ToArray();
            if(!leftIds.SequenceEqual(rightIds))return false;
            foreach(uint id in leftIds)
            {
                if(id>=(uint)right.Length)return false;
                NativeTransformCacheEntryRecord a=left[id],b=right[id];
                if(a.Id!=b.Id||a.RefCount!=b.RefCount||a.PoseHash!=b.PoseHash||
                    a.BindingCount!=b.BindingCount||a.StateFlags!=b.StateFlags||
                    !SameFloatBits(a.Rotation,b.Rotation)||!SameFloatBits(a.Position,b.Position))
                    return false;
            }
            return true;
        }

        private static bool SameTransformCacheBindings(NativeTransformCacheBindingRecord[] left,
            NativeTransformCacheBindingRecord[] right)
        {
            if(left==null||right==null||left.Length!=right.Length)return left==right;
            for(int i=0;i<left.Length;i++)
            {
                NativeTransformCacheBindingRecord a=left[i],b=right[i];
                if(a.ShapeSim!=b.ShapeSim||a.ShapeCore!=b.ShapeCore||
                    a.PxsShapeCore!=b.PxsShapeCore||a.Interaction!=b.Interaction||
                    a.InteractionIndex!=b.InteractionIndex||a.EndpointIndex!=b.EndpointIndex||
                    a.CacheId!=b.CacheId||a.RefCount!=b.RefCount||a.PoseHash!=b.PoseHash||
                    a.ValidationFlags!=b.ValidationFlags)return false;
            }
            return true;
        }

        private static bool SameFinishBroadPhaseState(FinishBroadPhaseState left,
            FinishBroadPhaseState right)
        {
            if(left==null||right==null)return left==right;
            if(!left.Receipt.Equals(right.Receipt)||left.Created==null||right.Created==null||
                left.Deleted==null||right.Deleted==null||
                left.Created.Length!=right.Created.Length||left.Deleted.Length!=right.Deleted.Length)
                return false;
            for(int i=0;i<left.Created.Length;i++)
                if(!SameBroadPhaseOverlap(left.Created[i],right.Created[i]))return false;
            for(int i=0;i<left.Deleted.Length;i++)
                if(!SameBroadPhaseOverlap(left.Deleted[i],right.Deleted[i]))return false;
            return true;
        }

        private static bool SameIslandSnapshotState(IslandSnapshotState left,
            IslandSnapshotState right)
        {
            if(left==null||right==null)return left==right;
            return left.RawBytes!=null&&right.RawBytes!=null&&
                left.RawBytes.SequenceEqual(right.RawBytes);
        }

        private static bool SameIslandPhysicalSnapshotState(IslandSnapshotState left,
            IslandSnapshotState right)
        {
            if(left==null||right==null)return left==right;
            if(left.RawBytes==null||right.RawBytes==null||
                left.RawBytes.Length!=right.RawBytes.Length)return false;
            byte[] leftBytes=(byte[])left.RawBytes.Clone();
            byte[] rightBytes=(byte[])right.RawBytes.Clone();
            ClearIslandCaptureProvenance(leftBytes);
            ClearIslandCaptureProvenance(rightBytes);
            return leftBytes.SequenceEqual(rightBytes);
        }

        private static void ClearIslandCaptureProvenance(byte[] bytes)
        {
            // These fields identify the observation transaction, not the
            // persistent island-manager image.  Comparing them would make a
            // later replay unequal even after exact physical restoration.
            foreach(string field in new[]{"ObserverSequence","ObservationOrdinal",
                "CaptureThreadId","Epoch","JournalBeginOrdinal","JournalEndOrdinal",
                "JournalOverflowCount"})
            {
                int offset=Marshal.OffsetOf(typeof(NativeIslandSnapshotReceipt),field).ToInt32();
                Array.Clear(bytes,offset,4);
            }
        }

        private static bool SameIslandTransitionState(IslandTransitionState left,
            IslandTransitionState right)
        {
            if(left==null||right==null)return left==right;
            return left.Receipt.Equals(right.Receipt)&&
                SameIslandSnapshotState(left.Pre,right.Pre)&&
                SameIslandSnapshotState(left.Post,right.Post)&&
                left.JournalReceipt.Equals(right.JournalReceipt)&&
                left.JournalRawBytes!=null&&right.JournalRawBytes!=null&&
                left.JournalRawBytes.SequenceEqual(right.JournalRawBytes);
        }

        private static bool SamePhysicsPhaseSnapshot(PhysicsPhaseSnapshot left,
            PhysicsPhaseSnapshot right)
        {
            if(left==null||right==null)return left==right;
            return left.Frame==right.Frame&&left.Context==right.Context&&
                left.FreeArray==right.FreeArray&&left.OrderHash==right.OrderHash&&
                ReferenceEquals(left.CoreSnapshot,right.CoreSnapshot)&&
                left.ContactPoolOrder!=null&&right.ContactPoolOrder!=null&&
                left.ContactPoolOrder.SequenceEqual(right.ContactPoolOrder)&&
                SameContactManagerOwnerSnapshot(left.ContactManagerOwners,
                    right.ContactManagerOwners)&&
                SameSipPoolSnapshot(left.ShapeInstancePairPool,
                    right.ShapeInstancePairPool)&&
                SameActorPairPoolSnapshot(left.ActorPairPool,right.ActorPairPool)&&
                SameActorPairReportPoolSnapshot(left.ActorPairReportPool,
                    right.ActorPairReportPool)&&
                SameRawNPhasePoolImages(left.NPhasePoolImages,right.NPhasePoolImages)&&
                SameNPhaseReportState(left.NPhaseReports,right.NPhaseReports)&&
                SameInteractionGraphState(left.InteractionGraph,right.InteractionGraph)&&
                SameTransformCacheState(left.TransformCache,right.TransformCache)&&
                SameIslandSnapshotState(left.IslandSnapshot,right.IslandSnapshot)&&
                SameManifoldPoolSnapshot(left.LargeManifoldPool,
                    right.LargeManifoldPool)&&
                SameManifoldPoolSnapshot(left.SphereManifoldPool,
                    right.SphereManifoldPool)&&
                SameTransformDispatchSnapshot(left.TransformDispatch,
                    right.TransformDispatch)&&
                SameDirtyInteractionSnapshot(left.DirtyInteractions,
                    right.DirtyInteractions);
        }

        private static bool SameStructArray<T>(T[] left,T[] right) where T:struct
        {
            if(left==null||right==null||left.Length!=right.Length)return left==right;
            var comparer=EqualityComparer<T>.Default;
            for(int i=0;i<left.Length;i++)if(!comparer.Equals(left[i],right[i]))return false;
            return true;
        }

        private static bool SameIslandSnapshotLayout(NativeIslandSnapshotReceipt a,
            NativeIslandSnapshotReceipt b)
        {
            return a.NPhaseCore==b.NPhaseCore&&a.OwnerScene==b.OwnerScene&&
                a.InteractionScene==b.InteractionScene&&a.Context==b.Context&&
                a.IslandManager==b.IslandManager&&
                SameIslandManagerLayout(a.NodeManager,b.NodeManager)&&
                SameIslandManagerLayout(a.EdgeManager,b.EdgeManager)&&
                SameIslandManagerLayout(a.IslandManagerReceipt,b.IslandManagerReceipt)&&
                SameIslandManagerLayout(a.RootManager,b.RootManager)&&
                SameIslandQueueLayout(a.NodeCreated,b.NodeCreated)&&
                SameIslandQueueLayout(a.NodeDeleted,b.NodeDeleted)&&
                SameIslandQueueLayout(a.EdgeCreated,b.EdgeCreated)&&
                SameIslandQueueLayout(a.EdgeDeleted,b.EdgeDeleted)&&
                SameIslandQueueLayout(a.EdgeBroken,b.EdgeBroken)&&
                SameIslandQueueLayout(a.EdgeJoined,b.EdgeJoined)&&
                SameIslandBitmapLayout(a.Kinematic,b.Kinematic)&&
                SameIslandBitmapLayout(a.KinematicChange,b.KinematicChange)&&
                SameIslandBitmapLayout(a.NotReady,b.NotReady)&&
                SameIslandBitmapLayout(a.NotReadyChange,b.NotReadyChange)&&
                SameIslandBitmapLayout(a.IslandBitmap,b.IslandBitmap);
        }

        private static bool SameIslandManagerLayout(NativeIslandElementManagerReceipt a,
            NativeIslandElementManagerReceipt b)
        {
            return a.Vtable==b.Vtable&&a.Capacity==b.Capacity;
        }

        private static bool SameIslandQueueLayout(NativeIslandQueueReceipt a,
            NativeIslandQueueReceipt b)
        {
            return a.Capacity==b.Capacity&&a.DefaultCapacity==b.DefaultCapacity;
        }

        private static bool SameIslandBitmapLayout(NativeIslandBitmapReceipt a,
            NativeIslandBitmapReceipt b)
        {
            return a.WordCount==b.WordCount;
        }

        private static bool SameIslandFreeState(IslandSnapshotState left,IslandSnapshotState right)
        {
            NativeIslandSnapshotReceipt a=left.Receipt,b=right.Receipt;
            if(a.NodeManager.FreeHead!=b.NodeManager.FreeHead||a.NodeManager.FreeCount!=b.NodeManager.FreeCount||
                a.EdgeManager.FreeHead!=b.EdgeManager.FreeHead||a.EdgeManager.FreeCount!=b.EdgeManager.FreeCount||
                a.IslandManagerReceipt.FreeHead!=b.IslandManagerReceipt.FreeHead||
                a.IslandManagerReceipt.FreeCount!=b.IslandManagerReceipt.FreeCount||
                a.RootManager.FreeHead!=b.RootManager.FreeHead||a.RootManager.FreeCount!=b.RootManager.FreeCount||
                left.Nodes.Length!=right.Nodes.Length||left.Edges.Length!=right.Edges.Length||
                left.Islands.Length!=right.Islands.Length||left.Roots.Length!=right.Roots.Length)return false;
            for(int i=0;i<left.Nodes.Length;i++)if(left.Nodes[i].FreeNext!=right.Nodes[i].FreeNext)return false;
            for(int i=0;i<left.Edges.Length;i++)if(left.Edges[i].FreeNext!=right.Edges[i].FreeNext)return false;
            for(int i=0;i<left.Islands.Length;i++)if(left.Islands[i].FreeNext!=right.Islands[i].FreeNext)return false;
            for(int i=0;i<left.Roots.Length;i++)if(left.Roots[i].FreeNext!=right.Roots[i].FreeNext)return false;
            return true;
        }

        private static bool SameIslandScalarState(NativeIslandSnapshotReceipt a,
            NativeIslandSnapshotReceipt b)
        {
            return a.NumAddedRBodies==b.NumAddedRBodies&&a.NumAddedArtics==b.NumAddedArtics&&
                a.NumAddedKinematics==b.NumAddedKinematics&&
                a.NumAddedEdgesContact==b.NumAddedEdgesContact&&
                a.NumAddedEdgesConstraint==b.NumAddedEdgesConstraint&&
                a.NumAddedEdgesArticulation==b.NumAddedEdgesArticulation&&
                a.NumEdgeRefsToKinematic==b.NumEdgeRefsToKinematic&&
                a.NumRequiredKinematicDuplicates==b.NumRequiredKinematicDuplicates&&
                a.EverythingAsleep==b.EverythingAsleep&&a.HasAnythingChanged==b.HasAnythingChanged&&
                a.PerformIslandUpdate==b.PerformIslandUpdate&&a.LiveContactEdges==b.LiveContactEdges&&
                a.LiveConstraintEdges==b.LiveConstraintEdges&&
                a.LiveArticulationEdges==b.LiveArticulationEdges;
        }

        private static bool SameBroadPhaseOverlap(NativeBroadPhaseOverlapRecord a,
            NativeBroadPhaseOverlapRecord b)
        {
            return a.UserData0==b.UserData0&&a.UserData1==b.UserData1&&
                a.ShapeCore0==b.ShapeCore0&&a.ShapeCore1==b.ShapeCore1&&
                a.PxsShapeCore0==b.PxsShapeCore0&&a.PxsShapeCore1==b.PxsShapeCore1&&
                a.CacheId0==b.CacheId0&&a.CacheId1==b.CacheId1&&
                a.PairHash==b.PairHash&&a.ValidationFlags==b.ValidationFlags;
        }

        private static bool SameContactManagerOwnerSnapshot(ContactManagerOwnerState left,ContactManagerOwnerState right)
        {
            if(left==null||right==null)return left==right;
            if(left.Receipt.OwnerHash!=right.Receipt.OwnerHash||left.Receipt.FreeOrderHash!=right.Receipt.FreeOrderHash||
                left.Receipt.UseBitmapHash!=right.Receipt.UseBitmapHash||left.Records==null||right.Records==null||
                left.Records.Length!=right.Records.Length)return false;
            for(int i=0;i<left.Records.Length;i++)
            {
                NativeContactManagerOwnerRecord a=left.Records[i],b=right.Records[i];
                if(a.Slot!=b.Slot||a.Manager!=b.Manager||a.Sip!=b.Sip||a.PxShape0!=b.PxShape0||
                    a.PxShape1!=b.PxShape1||a.ManagerHash!=b.ManagerHash||a.SipHash!=b.SipHash||
                    a.Manifold!=b.Manifold||a.ManifoldHash!=b.ManifoldHash||a.CacheHash!=b.CacheHash||
                    a.ActorPair!=b.ActorPair||a.ActorPairActor0!=b.ActorPairActor0||
                    a.ActorPairActor1!=b.ActorPairActor1||a.ActorPairScene!=b.ActorPairScene||
                    a.ActorPairInternalFlags!=b.ActorPairInternalFlags||
                    a.ActorPairTouchCount!=b.ActorPairTouchCount||
                    a.ActorPairRefCount!=b.ActorPairRefCount||
                    a.ActorPairReportData!=b.ActorPairReportData||a.ActorPairHash!=b.ActorPairHash||
                    a.ContactReportStamp!=b.ContactReportStamp||
                    a.ReportPairIndex!=b.ReportPairIndex||
                    a.ReportStreamIndex!=b.ReportStreamIndex)
                    return false;
                if(!ReferenceEquals(left.Endpoints0[i].Collider,right.Endpoints0[i].Collider)||
                    !ReferenceEquals(left.Endpoints1[i].Collider,right.Endpoints1[i].Collider))return false;
            }
            return SameByteMatrix(left.ManagerBytes,right.ManagerBytes)&&
                SameByteMatrix(left.SipBytes,right.SipBytes)&&
                SameByteMatrix(left.ActorPairBytes,right.ActorPairBytes)&&
                SameByteMatrix(left.ManifoldBytes,right.ManifoldBytes)&&
                SameByteMatrix(left.CacheBytes,right.CacheBytes);
        }

        private static bool SameByteMatrix(byte[][] left,byte[][] right)
        {
            if(left==null||right==null||left.Length!=right.Length)return left==right;
            for(int i=0;i<left.Length;i++)
                if(left[i]==null||right[i]==null||!left[i].SequenceEqual(right[i]))return false;
            return true;
        }

        private static bool SameTransformDispatchSnapshot(TransformDispatchState left,TransformDispatchState right)
        {
            if(left==null||right==null)return left==right;
            return left.Array==right.Array&&left.Capacity==right.Capacity&&SameTransformDispatchState(left,right);
        }

        private static bool SameDirtyInteractionSnapshot(DirtyInteractionState left,DirtyInteractionState right)
        {
            if(left==null||right==null)return left==right;
            return left.NPhaseCore==right.NPhaseCore&&left.Entries==right.Entries&&
                left.EntriesNext==right.EntriesNext&&left.Hash==right.Hash&&
                left.EntriesCapacity==right.EntriesCapacity&&left.HashSize==right.HashSize&&
                left.OrderHash==right.OrderHash&&SameDirtyInteractionKeys(left.Keys,right.Keys);
        }

        private static bool SameDirtyInteractionKeys(NativeDirtyInteractionKey[] left,NativeDirtyInteractionKey[] right)
        {
            if(left==null||right==null||left.Length!=right.Length)return left==right;
            for(int i=0;i<left.Length;i++)
                if(left[i].ElementLow!=right[i].ElementLow||left[i].ElementHigh!=right[i].ElementHigh||
                    left[i].PrimaryVtable!=right[i].PrimaryVtable||left[i].InteractionType!=right[i].InteractionType)
                    return false;
            return true;
        }

        private void PruneCheckpointSidecarsAfter(int frame)
        {
            foreach(int key in checkpointSidecars.Keys.Where(value=>value>frame).ToArray())
                checkpointSidecars.Remove(key);
        }

        private object CheckpointStatus(Dictionary<string,object> args)
        {
            if(args.Count!=1||!args.ContainsKey("frame"))
                throw new ArgumentException("checkpoint-status requires exactly frame.");
            int frame=Convert.ToInt32(args["frame"]);
            CheckpointSidecar value;
            bool found=checkpointSidecars.TryGetValue(frame,out value);
            object core=CoreCheckpointSnapshot(frame);
            return new Dictionary<string,object>{{"frame",frame},{"captured",found},
                {"coreSnapshotPresent",core!=null},
                {"coreSnapshotMatches",found&&ReferenceEquals(value.CoreSnapshot,core)},
                {"context",found?"0x"+value.Context.ToString("X8"):null},
                {"freeArray",found?"0x"+value.FreeArray.ToString("X8"):null},
                {"freeCount",found?value.ContactPoolOrder.Length:0},
                {"orderHash",found?"0x"+value.OrderHash.ToString("X8"):null},
                {"contactManagerOwners",found?DescribeContactManagerOwnerState(value.ContactManagerOwners,true):null},
                {"shapeInstancePairPool",found?DescribeSipPoolState(value.ShapeInstancePairPool):null},
                {"actorPairPool",found?DescribeActorPairPoolState(value.ActorPairPool):null},
                {"actorPairReportPool",found?DescribeActorPairReportPoolState(value.ActorPairReportPool):null},
                {"nphasePoolImages",found?DescribeNPhasePoolImages(value.NPhasePoolImages):null},
                {"nphaseReports",found?DescribeNPhaseReportState(value.NPhaseReports):null},
                {"interactionGraph",found?DescribeInteractionGraphState(value.InteractionGraph):null},
                {"transformCache",found?DescribeTransformCacheState(value.TransformCache):null},
                {"finishBroadPhase",found?DescribeFinishBroadPhaseState(value.FinishBroadPhase):null},
                {"islandSnapshot",found?DescribeIslandSnapshotState(value.IslandSnapshot):null},
                {"islandTransition",found?DescribeIslandTransitionState(value.IslandTransition):null},
                {"largeManifoldPool",found?DescribeManifoldPoolState(value.LargeManifoldPool):null},
                {"sphereManifoldPool",found?DescribeManifoldPoolState(value.SphereManifoldPool):null},
                {"transformDispatchCaptured",found&&value.TransformDispatch!=null},
                {"dirtyInteractions",found?DescribeDirtyInteractionState(value.DirtyInteractions):null},
                {"postTransitionSnapshot",found?
                    DescribePhysicsPhaseSnapshot(value.PostTransitionSnapshot):null}};
        }

        private object[] RebuildBodies(Rigidbody[] bodies,string reason)
        {
            if(library==IntPtr.Zero||rebuildBatch==null)throw new InvalidOperationException("Native actor-rebuild helper is not active.");
            if(bodies==null||bodies.Length<1||bodies.Length>4||bodies.Any(value=>value==null)||bodies.Distinct().Count()!=bodies.Length)
                throw new InvalidOperationException("Actor rebuild batch requires one to four distinct live bodies.");
            var before=bodies.Select(Capture).ToArray();
            var capsuleComponents=bodies.Select(body=>body.GetComponents<CapsuleCollider>()).ToArray();
            var capsules=new CapsuleState[bodies.Length][];
            var pointers=new IntPtr[bodies.Length];
            for(int i=0;i<bodies.Length;i++)
            {
                if(capsuleComponents[i].Length!=1||!capsuleComponents[i][0].enabled||capsuleComponents[i][0].attachedRigidbody!=bodies[i])
                    throw new InvalidOperationException("Actor rebuild requires one enabled capsule attached to every target Rigidbody.");
                capsules[i]=capsuleComponents[i].Select(Capture).ToArray();
                pointers[i]=(IntPtr)cachedPtr.GetValue(bodies[i]);
                if(pointers[i]==IntPtr.Zero)throw new InvalidOperationException("Rigidbody has no cached native pointer.");
            }

            int receiptSize=Marshal.SizeOf(typeof(NativeReceipt));
            IntPtr pointerBuffer=Marshal.AllocHGlobal(IntPtr.Size*bodies.Length);
            IntPtr receiptBuffer=Marshal.AllocHGlobal(receiptSize*bodies.Length);
            var native=new NativeReceipt[bodies.Length];
            try
            {
                for(int i=0;i<bodies.Length;i++)Marshal.WriteIntPtr(pointerBuffer,i*IntPtr.Size,pointers[i]);
                int ok=rebuildBatch(new UIntPtr(unityPlayerBase),pointerBuffer,(uint)bodies.Length,receiptBuffer);
                for(int i=0;i<bodies.Length;i++)native[i]=(NativeReceipt)Marshal.PtrToStructure(
                    new IntPtr(receiptBuffer.ToInt64()+i*receiptSize),typeof(NativeReceipt));
                if(ok==0||native.Any(value=>value.Result!=1))
                {
                    var failed=native.FirstOrDefault(value=>value.Result!=1);
                    throw new InvalidOperationException("Native actor rebuild failed: result="+failed.Result+", Win32/error="+failed.LastError+".");
                }
                if(native.Any(value=>value.ApiVersion!=10||value.StructSize!=(uint)receiptSize||
                    value.UnityBase.ToUInt32()!=unityPlayerBase))
                    throw new InvalidOperationException("Native actor rebuild receipt contract differs.");
            }
            finally{Marshal.FreeHGlobal(receiptBuffer);Marshal.FreeHGlobal(pointerBuffer);}

            // The batch has now removed the complete target set and reinserted
            // it in deterministic path order. Re-register capsules only after
            // every new active actor exists.
            foreach(var group in capsuleComponents)foreach(var collider in group){collider.enabled=false;collider.enabled=true;}

            // Cleanup/Create intentionally discards the old actor. Reapply only
            // the pose and motion values that live solely on that actor; all
            // serialized Rigidbody/Collider properties must survive natively.
            for(int i=0;i<bodies.Length;i++)
            {
                bodies[i].position=before[i].Position;bodies[i].rotation=before[i].Rotation;
                bodies[i].velocity=before[i].Velocity;bodies[i].angularVelocity=before[i].AngularVelocity;
                if(before[i].Sleeping)bodies[i].Sleep();else bodies[i].WakeUp();
            }
            var output=new object[bodies.Length];
            for(int i=0;i<bodies.Length;i++)
            {
                var body=bodies[i];var after=Capture(body);Verify(before[i],after);
                var afterCapsules=body.GetComponents<CapsuleCollider>().Select(Capture).ToArray();
                if(capsules[i].Length!=afterCapsules.Length)throw new InvalidOperationException("Actor rebuild changed capsule membership.");
                for(int j=0;j<capsules[i].Length;j++)Verify(capsules[i][j],afterCapsules[j]);
                rebuilds++;
                var receipt=new Dictionary<string,object>{
                    {"rebuild",rebuilds},{"batchSize",bodies.Length},{"batchIndex",i},{"reason",reason},
                    {"bodyInstanceId",body.GetInstanceID()},{"path",PathOf(body.transform)},{"localChef",IsLocalChef(body)},
                    {"nativeRigidbody",Hex(native[i].Rigidbody)},{"actorBefore",Hex(native[i].ActorBefore)},
                    {"actorAfterInactiveCreate",Hex(native[i].ActorAfterInactiveCreate)},
                    {"actorAfterActiveCreate",Hex(native[i].ActorAfterActiveCreate)},
                    {"position",Point(after.Position)},{"velocity",Point(after.Velocity)},
                    {"kinematic",after.Kinematic},{"sleeping",after.Sleeping},{"capsules",afterCapsules.Length}
                };
                // The last automatic cycle is the exact state handed back to
                // Unity before PhysicsManager::Simulate. Retain bounded,
                // read-only native bytes so original/replay simulation inputs
                // can be compared without changing the rebuild algorithm.
                if(reason=="main-unpause-cycle-5")
                {
                    receipt.Add("preSimRigidbodyMemory",MemorySnapshot(native[i].Rigidbody,128));
                    receipt.Add("preSimActorMemory",MemorySnapshot(native[i].ActorAfterActiveCreate,256));
                    receipt.Add("preSimShapes",ShapeSnapshots(native[i].ActorAfterActiveCreate));
                }
                receipts.Add(receipt);if(receipts.Count>64)receipts.RemoveAt(0);output[i]=receipt;
            }
            return output;
        }

        private void RebuildSharedGroundCollider(Rigidbody[] bodies)
        {
            if(groundColliderField==null)throw new InvalidOperationException("Installed GroundCast collider field differs.");
            var grounds=bodies.Select(body=>body.GetComponent<GroundCast>()).Select(value=>
                value==null?null:groundColliderField.GetValue(value) as Collider).Where(value=>value!=null).Distinct().ToArray();
            if(grounds.Length!=1||!(grounds[0] is BoxCollider))
                throw new InvalidOperationException("Automatic ground rebuild requires one shared BoxCollider.");
            Collider ground=grounds[0];int instanceId=ground.GetInstanceID();string path=PathOf(ground.transform);
            bool enabled=ground.enabled,trigger=ground.isTrigger;float offset=ground.contactOffset;
            int materialId=ground.sharedMaterial==null?0:ground.sharedMaterial.GetInstanceID();
            Rigidbody attached=ground.attachedRigidbody;
            if(!enabled||trigger||attached!=null)throw new InvalidOperationException("Shared ground collider contract differs.");
            ground.enabled=false;ground.enabled=true;
            if(ground.GetInstanceID()!=instanceId||PathOf(ground.transform)!=path||ground.enabled!=enabled||
                ground.isTrigger!=trigger||ground.contactOffset!=offset||ground.attachedRigidbody!=attached||
                (ground.sharedMaterial==null?0:ground.sharedMaterial.GetInstanceID())!=materialId)
                throw new InvalidOperationException("Ground rebuild changed an exposed Collider field.");
            receipts.Add(new Dictionary<string,object>{{"rebuild",++rebuilds},{"reason","main-unpause-shared-ground"},
                {"colliderInstanceId",instanceId},{"path",path},{"contactOffset",offset},{"materialInstanceId",materialId}});
            if(receipts.Count>64)receipts.RemoveAt(0);
        }

        private static BodyState Capture(Rigidbody body)
        {
            return new BodyState{Position=body.position,Rotation=body.rotation,Velocity=body.velocity,
                AngularVelocity=body.angularVelocity,Mass=body.mass,Drag=body.drag,AngularDrag=body.angularDrag,
                SleepThreshold=body.sleepThreshold,MaxAngularVelocity=body.maxAngularVelocity,
                Kinematic=body.isKinematic,Gravity=body.useGravity,DetectCollisions=body.detectCollisions,
                Sleeping=body.IsSleeping(),Constraints=body.constraints,Interpolation=body.interpolation,
                CollisionDetection=body.collisionDetectionMode,SolverIterations=body.solverIterations,
                SolverVelocityIterations=body.solverVelocityIterations};
        }

        private static CapsuleState Capture(CapsuleCollider value)
        {
            return new CapsuleState{InstanceId=value.GetInstanceID(),MaterialId=value.sharedMaterial==null?0:value.sharedMaterial.GetInstanceID(),
                Direction=value.direction,Attached=value.attachedRigidbody,Center=value.center,Radius=value.radius,Height=value.height,
                ContactOffset=value.contactOffset,Enabled=value.enabled,Trigger=value.isTrigger};
        }

        private static void Verify(BodyState a,BodyState b)
        {
            if(a.Position!=b.Position||a.Rotation!=b.Rotation||a.Velocity!=b.Velocity||a.AngularVelocity!=b.AngularVelocity||
                a.Mass!=b.Mass||a.Drag!=b.Drag||a.AngularDrag!=b.AngularDrag||a.SleepThreshold!=b.SleepThreshold||
                a.MaxAngularVelocity!=b.MaxAngularVelocity||a.Kinematic!=b.Kinematic||a.Gravity!=b.Gravity||
                a.DetectCollisions!=b.DetectCollisions||a.Sleeping!=b.Sleeping||a.Constraints!=b.Constraints||
                a.Interpolation!=b.Interpolation||a.CollisionDetection!=b.CollisionDetection||
                a.SolverIterations!=b.SolverIterations||a.SolverVelocityIterations!=b.SolverVelocityIterations)
                throw new InvalidOperationException("Actor rebuild changed an exposed Rigidbody field.");
        }

        private static void Verify(CapsuleState a,CapsuleState b)
        {
            if(a.InstanceId!=b.InstanceId||a.MaterialId!=b.MaterialId||a.Direction!=b.Direction||a.Attached!=b.Attached||a.Center!=b.Center||
                a.Radius!=b.Radius||a.Height!=b.Height||a.ContactOffset!=b.ContactOffset||a.Enabled!=b.Enabled||a.Trigger!=b.Trigger)
                throw new InvalidOperationException("Actor rebuild changed an exposed CapsuleCollider field.");
        }

        private object Status(string operation,object result)
        {
            if(ReferenceEquals(active,this))try{ObserveCoreRoundIdentity();}catch(Exception error){failure=error.Message;}
            if(contextObserverInstalled)try{RefreshObservedContactManagerContext();}catch(Exception error){failure=error.Message;}
            CheckpointSidecar latest=LatestCheckpointSidecar();
            int firstFrame=checkpointSidecars.Count==0?-1:checkpointSidecars.Keys.Min();
            int lastFrame=latest==null?-1:latest.Frame;
            var value=new Dictionary<string,object>{{"name",Name},{"apiVersion",1},{"operation",operation},
                {"active",ReferenceEquals(active,this)},{"automaticChefs",automatic},{"automaticGroundCollider",automaticGroundCollider},{"nativePath",nativePath},
                {"nativeSha256",nativeSha256},{"nativeApiVersion",library==IntPtr.Zero?(object)null:NativeAbiVersion},
                {"unityPlayerBase","0x"+unityPlayerBase.ToString("X8")},
                {"rebuilds",rebuilds},{"failure",failure},{"receipts",receipts.ToArray()},
                {"contactManagerContext",contactManagerContext==0?null:"0x"+contactManagerContext.ToString("X8")},
                {"observeContactManagerContext",observeContactManagerContext},{"contextObserverInstalled",contextObserverInstalled},
                {"contextObservations",contextObservations},{"contextObservationFloor",contextObservationFloor},
                {"lastContextObserverReceipt",lastContextObserverReceipt},
                {"sceneMetadataGeneration",sceneMetadataGeneration},{"sceneOwnedResets",sceneOwnedResets},
                {"contextSnapshotInvalidations",contextSnapshotInvalidations},{"lastSceneOwnedReset",lastSceneOwnedReset},
                {"contactPoolSnapshotCaptured",latest!=null},
                {"contactPoolSnapshotFrame",lastFrame},{"contactPoolSnapshotContext",latest==null?null:"0x"+latest.Context.ToString("X8")},
                {"contactPoolSnapshotCount",checkpointSidecars.Count},{"contactPoolSnapshotFirstFrame",firstFrame},
                {"contactPoolSnapshotLastFrame",lastFrame},
                {"contactManagerOwnerSnapshot",latest==null?null:
                    DescribeContactManagerOwnerState(latest.ContactManagerOwners,true)},
                {"shapeInstancePairPoolSnapshot",latest==null?null:
                    DescribeSipPoolState(latest.ShapeInstancePairPool)},
                {"actorPairPoolSnapshot",latest==null?null:
                    DescribeActorPairPoolState(latest.ActorPairPool)},
                {"actorPairReportPoolSnapshot",latest==null?null:
                    DescribeActorPairReportPoolState(latest.ActorPairReportPool)},
                {"nphasePoolImagesSnapshot",latest==null?null:
                    DescribeNPhasePoolImages(latest.NPhasePoolImages)},
                {"nphaseReportSnapshot",latest==null?null:
                    DescribeNPhaseReportState(latest.NPhaseReports)},
                {"interactionGraphSnapshot",latest==null?null:
                    DescribeInteractionGraphState(latest.InteractionGraph)},
                {"transformCacheSnapshotCaptured",latest!=null&&latest.TransformCache!=null},
                {"transformCacheSnapshot",latest==null?null:
                    DescribeTransformCacheState(latest.TransformCache)},
                {"transformCacheCaptures",transformCacheCaptures},
                {"finishBroadPhaseObserverInstalled",finishBroadPhaseObserverInstalled},
                {"nativeLibraryUnloadSuppressedForHookSafety",finishBroadPhaseObserverWasInstalled||
                    IsNativeLibraryPinned(library)},
                {"finishBroadPhaseSnapshotCaptured",latest!=null&&latest.FinishBroadPhase!=null},
                {"finishBroadPhaseSnapshot",latest==null?null:
                    DescribeFinishBroadPhaseState(latest.FinishBroadPhase)},
                {"finishBroadPhaseCapturePending",pendingFinishBroadPhaseOrdinal!=0},
                {"pendingFinishBroadPhaseOrdinal",pendingFinishBroadPhaseOrdinal},
                {"finishBroadPhaseCaptures",finishBroadPhaseCaptures},
                {"finishBroadPhaseReceipts",finishBroadPhaseReceipts.ToArray()},
                {"islandObserverInstalled",islandObserverInstalled},
                {"islandSnapshotCaptured",latest!=null&&latest.IslandSnapshot!=null},
                {"islandSnapshot",latest==null?null:DescribeIslandSnapshotState(latest.IslandSnapshot)},
                {"islandTransitionCaptured",latest!=null&&latest.IslandTransition!=null},
                {"islandTransition",latest==null?null:DescribeIslandTransitionState(latest.IslandTransition)},
                {"islandCapturePending",pendingIslandObservation!=null},
                {"pendingIslandObservationOrdinal",pendingIslandObservation==null?0:pendingIslandObservation.ArmedOrdinal},
                {"islandSnapshotCaptures",islandSnapshotCaptures},
                {"islandTransitionCaptures",islandTransitionCaptures},
                {"firstReplayIslandAuditPending",pendingIslandTransitionAuditSidecar!=null},
                {"firstReplayIslandAuditCheckpointFrame",lastIslandTransitionAuditCheckpointFrame},
                {"firstReplayIslandAuditTransitionFrame",lastIslandTransitionAuditTransitionFrame},
                {"firstReplayIslandAuditCapturedAtOutputFrame",lastIslandTransitionAuditCapturedAtOutputFrame},
                {"firstReplayIslandAuditArms",islandTransitionAuditArms},
                {"firstReplayIslandAuditCaptures",islandTransitionAuditCaptures},
                {"firstReplayBroadPhaseAudit",DescribeFinishBroadPhaseState(lastFinishBroadPhaseTransitionAudit)},
                {"firstReplayIslandAudit",DescribeIslandTransitionState(lastIslandTransitionAudit)},
                {"firstReplayTargetPostSnapshot",DescribePhysicsPhaseSnapshot(
                    lastTargetPostTransitionAudit)},
                {"firstReplayRestoredPostSnapshot",DescribePhysicsPhaseSnapshot(
                    lastRestoredPostTransitionAudit)},
                {"transitionPostSnapshotCaptures",transitionPostSnapshotCaptures},
                {"retainedIslandBufferTransactions",processRetainedIslandBuffers.Count},
                {"manifoldPoolSnapshotCaptured",latest!=null&&latest.LargeManifoldPool!=null&&latest.SphereManifoldPool!=null},
                {"manifoldPoolSnapshotFrame",latest==null?-1:lastFrame},
                {"largeManifoldPoolSnapshot",latest==null?null:DescribeManifoldPoolState(latest.LargeManifoldPool)},
                {"sphereManifoldPoolSnapshot",latest==null?null:DescribeManifoldPoolState(latest.SphereManifoldPool)},
                {"pendingContactPoolAction",pendingContactPoolAction==0?"none":pendingContactPoolAction==1?"capture":"restore"},
                {"scheduledContactPoolCapturePending",scheduledContactPoolCaptureFrame>=0},
                {"scheduledContactPoolCaptureFrame",scheduledContactPoolCaptureFrame},
                {"scheduledContactPoolLastObservedFrame",scheduledContactPoolLastObservedFrame},
                {"scheduledContactPoolCaptureArms",scheduledContactPoolCaptureArms},
                {"scheduledContactPoolCaptureTriggers",scheduledContactPoolCaptureTriggers},
                {"automaticContactPoolRestore",automaticContactPoolRestore},{"automaticRestorePending",automaticRestorePending},
                {"automaticTransformDispatchRestore",automaticTransformDispatchRestore},
                {"transformDispatchSnapshotCaptured",latest!=null&&latest.TransformDispatch!=null},
                {"transformDispatchSnapshotFrame",latest==null||latest.TransformDispatch==null?-1:lastFrame},
                {"transformDispatchCaptures",transformDispatchCaptures},{"transformDispatchRestores",transformDispatchRestores},
                {"transformDispatchReceipts",transformDispatchReceipts.ToArray()},
                {"dirtyInteractionHookInstalled",dirtyInteractionHookInstalled},
                {"dirtyInteractionRestoreMode",dirtyInteractionRestoreMode==
                    DirtyInteractionRestoreProjection?"projection":"exact"},
                {"dirtyInteractionSnapshotCaptured",latest!=null&&latest.DirtyInteractions!=null},
                {"dirtyInteractionSnapshot",latest==null?null:DescribeDirtyInteractionState(latest.DirtyInteractions)},
                {"dirtyInteractionCapturePending",pendingDirtyCaptureSidecar!=null},
                {"dirtyInteractionRestorePendingValidation",dirtyRestorePendingValidation},
                {"dirtyInteractionCaptures",dirtyInteractionCaptures},{"dirtyInteractionRestores",dirtyInteractionRestores},
                {"dirtyInteractionReceipts",dirtyInteractionReceipts.ToArray()},
                {"contactRecreatePendingValidation",contactRecreatePendingValidation},
                {"contactRecreateReceipts",contactRecreateReceipts.ToArray()},
                {"warpInProgress",warpInProgress},{"warpTargetFrame",warpTargetFrame},{"warpTargetRestoreEligible",warpTargetRestoreEligible},
                {"contactPoolCaptures",contactPoolCaptures},{"contactPoolRestores",contactPoolRestores},
                {"contactPoolReceipts",contactPoolReceipts.ToArray()},
                {"manifoldPoolCaptures",manifoldPoolCaptures},{"manifoldPoolRestores",manifoldPoolRestores},
                {"manifoldPoolReceipts",manifoldPoolReceipts.ToArray()},
                {"scope","Optional batched Unity Create(false)/Create(true) actor replacement plus exact capsule re-registration, restricted to explicit paused one-shot targets or, only when automaticChefs is enabled, five canonical cycles for the four local chefs during a checkpoint restore's internal main-physics unfreeze. Ordinary forward and replay unpauses never rebuild actors. Pass-through native observers identify the current PxsContext and capture one exact subsequent pass-zero finishBroadPhase transition without replacing the original call. Caller-owned, bounded sidecars retain the complete contact-manager, manifold, ShapeInstancePair, ActorPair, report, InteractionScene, PxsTransformCache, TransformChangeDispatch, dirty-interaction, and created/deleted-overlap orders for each exact core checkpoint object. One pause-fenced command may schedule the read-only capture transaction at an exact future NativeKitchenCheckpoint output boundary without splitting input; it binds the already-published exact core snapshot, never runs early or late, and fails closed if that output frame is skipped or either next-update observation is missing. A successful rewind prunes only future sidecars; implemented restore paths remain limited to the selected pool/dispatch/dirty state, while the new cache and broadphase state are planner evidence only. Forward game data, score and input are not rewritten."}};
            if(result!=null)value.Add("result",result);return value;
        }

        private void Deactivate()
        {
            Exception teardownError=null;
            try{CancelContactRecreateWork();}catch(Exception error){teardownError=error;}
            try{CancelCheckpointObservationWork();}
            catch(Exception error){if(teardownError==null)teardownError=error;}
            if(islandObserverInstalled)
            {
                try
                {
                    NativeIslandObserverReceipt island=new NativeIslandObserverReceipt();
                    bool dormant=false;
                    for(int attempt=0;attempt<3;attempt++)
                    {
                        island=CallIslandObserverAction(uninstallIslandObserver,"uninstall",1,9);
                        if(island.Result==1&&island.Installed==1&&island.State==0&&island.InFlight==0)
                        {dormant=true;break;}
                        if(island.Result!=9)break;
                    }
                    if(!dormant)throw new InvalidOperationException(
                        "Native island observer could not prove dormant quiescence; native resources were retained.");
                }
                catch(Exception error){if(teardownError==null)teardownError=error;}
                islandObserverInstalled=false;
            }
            if(finishBroadPhaseObserverInstalled)
            {
                try{CallFinishBroadPhaseObserverAction(uninstallFinishBroadPhaseObserver,"uninstall");}
                catch(Exception error){if(teardownError==null)teardownError=error;}
                finishBroadPhaseObserverInstalled=false;
            }
            if(dirtyInteractionHookInstalled)
            {
                try{CallDirtyInteractionAction(uninstallDirtyInteractionOrder,"uninstall",1);}
                catch(Exception error){if(teardownError==null)teardownError=error;}
                dirtyInteractionHookInstalled=false;
            }
            if(contextObserverInstalled)
            {
                try{RunContextObserver(uninstallContextObserver,"uninstall");}
                catch(Exception error){if(teardownError==null)teardownError=error;}
                contextObserverInstalled=false;
            }
            if(harmony!=null)try{harmony.UnpatchSelf();}
                catch(Exception error){if(teardownError==null)teardownError=error;}
            harmony=null;automatic=false;automaticGroundCollider=false;
            observeContactManagerContext=false;automaticContactPoolRestore=false;automaticTransformDispatchRestore=false;automaticRestorePending=false;
            dirtyInteractionRestoreMode=DirtyInteractionRestoreExact;
            warpInProgress=false;warpTargetRestoreEligible=false;warpTargetFrame=-1;
            pendingContactPoolAction=0;pendingContactPoolFrame=-1;pendingCoreSnapshot=null;
            scheduledContactPoolCaptureFrame=-1;scheduledContactPoolLastObservedFrame=-1;
            pendingDirtyCaptureSidecar=null;pendingDirtyCaptureOrdinal=0;
            dirtyRestorePendingValidation=false;pendingDirtyRestoreOrdinal=0;pendingDirtyRestoreState=null;
            contactRecreatePendingValidation=false;pendingContactRecreateSidecar=null;
            checkpointSidecars.Clear();warpTargetSidecar=null;contactManagerContext=0;
            pendingIslandTransitionAuditSidecar=null;lastIslandTransitionAudit=null;
            lastFinishBroadPhaseTransitionAudit=null;
            lastTargetPostTransitionAudit=null;
            lastRestoredPostTransitionAudit=null;
            lastIslandTransitionAuditCheckpointFrame=-1;
            lastIslandTransitionAuditTransitionFrame=-1;
            lastIslandTransitionAuditCapturedAtOutputFrame=-1;
            contextObservationFloor=0;
            islandObserverManager=0;
            coreRoundIdentity=null;sceneMetadataGeneration=-1;
            apiVersion=null;rebuildBatch=null;actorShapes=null;captureContactPoolSnapshot=null;restoreContactPoolSnapshot=null;
            captureContactManagerActiveOwners=null;
            captureManifoldPoolSnapshot=null;restoreManifoldPoolSnapshot=null;captureSipPoolSnapshot=null;
            captureActorPairPoolSnapshot=null;
            captureActorPairReportPoolSnapshot=null;
            captureNPhasePoolSnapshot=null;
            captureNPhaseReportStateSnapshot=null;
            captureInteractionGraphSnapshot=null;
            captureTransformCacheSnapshot=null;
            captureIslandSnapshot=null;restoreIslandSnapshot=null;
            installIslandObserver=null;statusIslandObserver=null;
            armIslandObserver=null;copyIslandObserver=null;copyIslandJournal=null;
            cancelIslandObserver=null;uninstallIslandObserver=null;
            installFinishBroadPhaseObserver=null;statusFinishBroadPhaseObserver=null;
            armFinishBroadPhaseObserver=null;copyFinishBroadPhaseObserver=null;
            cancelFinishBroadPhaseObserver=null;uninstallFinishBroadPhaseObserver=null;
            installContextObserver=null;statusContextObserver=null;uninstallContextObserver=null;
            contactRecreateApiVersion=null;contactRecreateAuditApiVersion=null;auditContactRecreate=null;
            armContactRecreate=null;statusContactRecreate=null;cancelContactRecreate=null;
            installDirtyInteractionOrder=null;statusDirtyInteractionOrder=null;armDirtyInteractionCapture=null;
            lastObservedDirtyNPhase=null;dirtyNPhaseObservationFloor=0;
            copyDirtyInteractionCapture=null;captureDirtyInteractionSnapshot=null;
            armDirtyInteractionRestore=null;cancelDirtyInteractionOrder=null;
            uninstallDirtyInteractionOrder=null;
            if(library!=IntPtr.Zero)
            {
                bool retain=false;
                if(finishBroadPhaseObserverWasInstalled)
                    lock(processPinnedNativeLibraries)
                        retain=processPinnedNativeLibraries.Add(library);
                // If this exact image was pinned by an earlier activation,
                // release only the current activation's additional reference.
                if(!retain)FreeLibrary(library);
                library=IntPtr.Zero;
            }
            finishBroadPhaseObserverWasInstalled=false;
            if(ReferenceEquals(active,this))active=null;
            if(teardownError!=null&&string.IsNullOrEmpty(failure))
                failure="Best-effort native teardown retained safe resources: "+
                    teardownError.GetType().Name+": "+teardownError.Message;
        }

        private T Export<T>(string name) where T:class
        {
            IntPtr pointer=GetProcAddress(library,name);if(pointer==IntPtr.Zero)throw new MissingMethodException("Native export missing: "+name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer,typeof(T));
        }

        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
        private static bool IsLocalChef(Rigidbody body){return body!=null&&body.GetComponent<ServerChefSynchroniser>()!=null&&body.GetComponent<ClientOnTheServerChefSynchroniser>()!=null;}
        private static void RequireNoArgs(Dictionary<string,object> args){if(args.Count!=0)throw new ArgumentException("Operation takes no arguments.");}
        private static string String(Dictionary<string,object> args,string key){if(!args.ContainsKey(key)||args[key]==null)throw new ArgumentException("Missing "+key);return Convert.ToString(args[key]);}
        private static uint Pointer(Dictionary<string,object> args,string key)
        {
            string value=String(args,key).Trim();
            if(value.StartsWith("0x",StringComparison.OrdinalIgnoreCase))value=value.Substring(2);
            uint result;if(!UInt32.TryParse(value,System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,out result)||result==0)
                throw new ArgumentException("Invalid nonzero x86 pointer "+key+".");
            return result;
        }
        private static string Hash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        private static bool IsNativeLibraryPinned(IntPtr handle)
        {
            if(handle==IntPtr.Zero)return false;
            lock(processPinnedNativeLibraries)return processPinnedNativeLibraries.Contains(handle);
        }
        private static uint ReadWord(uint address)
        {
            byte[] bytes=new byte[4];UIntPtr read=UIntPtr.Zero;
            if(address==0||!ReadProcessMemory(GetCurrentProcess(),new IntPtr(unchecked((int)address)),
                    bytes,new UIntPtr(4u),out read)||read.ToUInt32()!=4u)
                throw new InvalidOperationException("Native read failed at 0x"+address.ToString("X8")+", Win32/error="+Marshal.GetLastWin32Error()+".");
            return BitConverter.ToUInt32(bytes,0);
        }
        private static byte[] ReadBytes(uint address,int length)
        {
            if(length<0)throw new ArgumentOutOfRangeException("length");
            byte[] bytes=new byte[length];UIntPtr read=UIntPtr.Zero;
            if(address==0||!ReadProcessMemory(GetCurrentProcess(),new IntPtr(unchecked((int)address)),
                    bytes,new UIntPtr((uint)length),out read)||read.ToUInt32()!=(uint)length)
                throw new InvalidOperationException("Native read failed at 0x"+address.ToString("X8")+
                    ", length="+length+", Win32/error="+Marshal.GetLastWin32Error()+".");
            return bytes;
        }
        private static void WriteWord(uint address,uint value)
        {
            byte[] bytes=BitConverter.GetBytes(value);UIntPtr written=UIntPtr.Zero;
            if(address==0||!WriteProcessMemory(GetCurrentProcess(),new IntPtr(unchecked((int)address)),
                    bytes,new UIntPtr(4u),out written)||written.ToUInt32()!=4u)
                throw new InvalidOperationException("Native write failed at 0x"+address.ToString("X8")+", Win32/error="+Marshal.GetLastWin32Error()+".");
        }
        private static object MemorySnapshot(UIntPtr address,int length)
        {
            var bytes=new byte[length];UIntPtr read=UIntPtr.Zero;
            bool ok=address!=UIntPtr.Zero&&ReadProcessMemory(GetCurrentProcess(),
                new IntPtr(unchecked((int)address.ToUInt32())),bytes,new UIntPtr((uint)length),out read)&&
                read.ToUInt32()==(uint)length;
            if(!ok)return new Dictionary<string,object>{{"ok",false},{"address",Hex(address)},
                {"length",length},{"bytesRead",read.ToUInt32()},{"lastError",Marshal.GetLastWin32Error()}};
            string sha;
            using(var algorithm=SHA256.Create())sha=BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-","");
            return new Dictionary<string,object>{{"ok",true},{"address",Hex(address)},
                {"length",length},{"sha256",sha},{"hex",BitConverter.ToString(bytes).Replace("-","")}};
        }
        private object[] ShapeSnapshots(UIntPtr actor)
        {
            const int capacity=8;IntPtr buffer=Marshal.AllocHGlobal(IntPtr.Size*capacity);
            try
            {
                uint count,error;
                if(actorShapes(new UIntPtr(unityPlayerBase),actor,buffer,capacity,out count,out error)==0)
                    return new object[]{new Dictionary<string,object>{{"ok",false},{"error",error},{"actor",Hex(actor)}}};
                var result=new object[(int)count];
                for(int i=0;i<count;i++)
                {
                    var shape=new UIntPtr(unchecked((uint)Marshal.ReadIntPtr(buffer,i*IntPtr.Size).ToInt32()));
                    result[i]=MemorySnapshot(shape,256);
                }
                return result;
            }
            finally{Marshal.FreeHGlobal(buffer);}
        }
        private static string Hex(UIntPtr value){return "0x"+value.ToUInt32().ToString("X8");}
        private static object[] Point(Vector3 value){return new object[]{value.x,value.y,value.z};}
        private static string PathOf(Transform value){string path=value.name;while(value.parent!=null){value=value.parent;path=value.name+"/"+path;}return path;}
    }
}
