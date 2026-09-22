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
        // Four active chef actors plus Unity's one replacement allocation form
        // the observed five-address cycle. Rebuilding five times removes every
        // chef actor/contact set while restoring the incoming chef/address map.
        private const int AutomaticCanonicalCycles=5;
        private const int MaximumContactManagers=4096;
        private const int MaximumManifolds=4096;
        private const int MaximumShapeInstancePairs=4096;
        private const int MaximumDirtyInteractions=4096;
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
        }

        private sealed class LiveContactPoolState
        {
            internal NativeContactPoolReceipt Receipt;
            internal uint[] Order;
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
            internal ManifoldPoolState LargeManifoldPool,SphereManifoldPool;
            internal DirtyInteractionState DirtyInteractions;
            internal TransformDispatchState TransformDispatch;
            internal object CoreSnapshot;
        }

        private static RigidbodyActorRebuildModule active;
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
        private NativeDirtyInteractionRestoreArm armDirtyInteractionRestore;
        private uint unityPlayerBase;
        private uint dirtyNPhaseObservationFloor;
        private uint dirtyInteractionRestoreMode=DirtyInteractionRestoreExact;
        private uint contactManagerContext,contextObservations;
        private string nativePath,nativeSha256,failure;
        private bool automatic,automaticGroundCollider,observeContactManagerContext,contextObserverInstalled;
        private bool automaticContactPoolRestore,automaticTransformDispatchRestore,automaticRestorePending,warpInProgress,warpTargetRestoreEligible,disposed;
        private bool dirtyInteractionHookInstalled,dirtyRestorePendingValidation;
        private bool contactRecreatePendingValidation;
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
        private DirtyInteractionState pendingDirtyRestoreState;
        private uint pendingDirtyCaptureOrdinal,pendingDirtyRestoreOrdinal;
        private long transformDispatchCaptures,transformDispatchRestores;
        private readonly List<object> transformDispatchReceipts=new List<object>();
        private object lastContextObserverReceipt,lastSceneOwnedReset;
        private readonly List<object> dirtyInteractionReceipts=new List<object>();
        private readonly List<object> contactRecreateReceipts=new List<object>();
        private long dirtyInteractionCaptures,dirtyInteractionRestores;
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
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, rebuild, capture-contact-pool-next, capture-contact-pool-at-frame, restore-contact-pool-next, cancel-contact-pool-next, checkpoint-status, audit-restore-readiness, status or deactivate.");
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
                armDirtyInteractionRestore=Export<NativeDirtyInteractionRestoreArm>("oc2_dirty_interaction_order_restore_arm");
                cancelDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_cancel");
                uninstallDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_uninstall");
                if(apiVersion()!=13)throw new InvalidOperationException("Native actor-rebuild API version mismatch.");
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
                    RunContextObserver(installContextObserver,"install");
                    contextObserverInstalled=true;
                    RefreshObservedContactManagerContext();
                }
                if(automaticContactPoolRestore)
                {
                    NativeDirtyInteractionReceipt dirty=InstallDirtyInteractionHook();
                    if(dirty.Result!=1||dirty.Installed!=1)
                        throw new InvalidOperationException("Native dirty-interaction hook did not install.");
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
                throw new InvalidOperationException("No exact physics-pool/Transform-dispatch sidecar exists for output frame "+module.warpTargetFrame+".");
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
            module.CancelDirtyInteractionWork();
        }

        public static void AfterCaptureFrame(int __0)
        {
            var module=active;
            if(module==null||!module.automaticContactPoolRestore||
                (!module.contactRecreatePendingValidation&&module.scheduledContactPoolCaptureFrame<0))return;
            try
            {
                module.ObserveSceneGeneration();
                module.ObserveCoreRoundIdentity();
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
            if(!ReferenceEquals(active,this)||!automaticContactPoolRestore||!dirtyInteractionHookInstalled)
                throw new InvalidOperationException("Scheduled contact-pool capture requires the active automatic contact-pool restore module.");
            ObserveSceneGeneration();
            ObserveCoreRoundIdentity();
            RefreshObservedContactManagerContext();
            if(!ReferenceEquals(active,this)||contactManagerContext==0)
                throw new InvalidOperationException("Scheduled contact-pool capture requires an active observed or explicit context.");
            if(pendingContactPoolAction!=0||scheduledContactPoolCaptureFrame>=0||
                pendingContactPoolFrame>=0||pendingCoreSnapshot!=null||pendingDirtyCaptureSidecar!=null||
                pendingDirtyCaptureOrdinal!=0||dirtyRestorePendingValidation||pendingDirtyRestoreOrdinal!=0||
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
                    pendingDirtyCaptureSidecar.Frame!=target||
                    !ReferenceEquals(pendingDirtyCaptureSidecar.CoreSnapshot,core)||
                    pendingDirtyCaptureSidecar.ShapeInstancePairPool==null||
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
                CancelDirtyInteractionWork();
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
                captureActorPairPoolSnapshot==null||captureActorPairReportPoolSnapshot==null)
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
            if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||receipt.Context.ToUInt32()!=contactManagerContext)
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
                    LargeManifoldPool=large,SphereManifoldPool=sphere,
                    TransformDispatch=dispatch,CoreSnapshot=actionCoreSnapshot};
                uint afterObservation;
                uint afterNPhase=ReadObservedNPhaseCore(out afterObservation);
                if(afterNPhase!=captureNPhase||afterObservation!=captureNPhaseObservation)
                    throw new InvalidOperationException("A physics update crossed the synchronous checkpoint pool-capture interval.");
                ValidateCheckpointPoolCoherence(captured);
                // Do not publish a partially captured sidecar.  The native
                // dirty-list sample completes on the next physics update and
                // FinalizePendingDirtyInteractionCapture publishes the whole
                // sidecar as one transaction.
                ArmDirtyInteractionCapture(captured);
                pendingContactPoolFrame=-1;pendingCoreSnapshot=null;contactPoolCaptures++;
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
                if(ok==0||receipt.Result!=1||receipt.ApiVersion!=13||
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
                if(ok==0||receipt.Result!=1||receipt.ApiVersion!=13||
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
                    try{CancelDirtyInteractionWork();}catch{}
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

            string[] futureFamilies={"interaction-registration-order",
                "island-edge-allocator-and-change-queues","transform-cache-id-pool",
                "broadphase-created-overlap-order","dirty-interaction-live-projection",
                "contact-report-lists-and-buffer",
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
            "rigidbody.actor-pair.allocation-binding"
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
                new Dictionary<string,object>{{"contractVersion",2},
                    {"required",requiredIds.Cast<object>().ToArray()},
                    {"uncovered",missing.Cast<object>().ToArray()},
                    {"duplicates",duplicates.Cast<object>().ToArray()}});
            return new Dictionary<string,object>{{"contractVersion",2},
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
                dirtyInteractionCaptures.ToString(),dirtyInteractionRestores.ToString(),
                pendingContactPoolAction.ToString(),pendingContactPoolFrame.ToString(),
                scheduledContactPoolCaptureFrame.ToString(),automaticRestorePending.ToString(),
                warpInProgress.ToString(),warpTargetFrame.ToString(),warpTargetRestoreEligible.ToString(),
                contactRecreatePendingValidation.ToString(),dirtyRestorePendingValidation.ToString(),
                contactPoolReceipts.Count.ToString(),manifoldPoolReceipts.Count.ToString(),
                transformDispatchReceipts.Count.ToString(),dirtyInteractionReceipts.Count.ToString(),
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
                try{CancelDirtyInteractionWork();}catch{}
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
            if(recordSize!=160||receiptSize!=156)
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
                if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)receiptSize||
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
                return new ContactManagerOwnerState {Receipt=receipt,Records=records,
                    ShapeOwners0=owners0,ShapeOwners1=owners1,
                    Endpoints0=endpoints0,Endpoints1=endpoints1};
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
            }
            return result;
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
            if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||
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
            if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||
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
                if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||
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
                if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||
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
                if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||
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

        private void FinalizePendingDirtyInteractionCapture()
        {
            if(pendingDirtyCaptureSidecar==null)return;
            NativeDirtyInteractionReceipt status=CallDirtyInteractionAction(
                statusDirtyInteractionOrder,"capture-status");
            if(status.Result!=1||status.Armed!=0||status.Action!=0||
                status.Captures!=pendingDirtyCaptureOrdinal)
                throw new InvalidOperationException("Native dirty-interaction capture did not complete exactly once: result="+
                    status.Result+", armed="+status.Armed+", captures="+status.Captures+".");
            if(status.Count>MaximumDirtyInteractions)
                throw new InvalidOperationException("Native dirty-interaction capture count exceeds the supported bound.");
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
            StoreCheckpointSidecar(pendingDirtyCaptureSidecar);
            pendingDirtyCaptureSidecar=null;pendingDirtyCaptureOrdinal=0;
            dirtyInteractionCaptures++;
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
            if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||receipt.UnityBase.ToUInt32()!=unityPlayerBase)
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
            if(receipt.ApiVersion!=13||receipt.StructSize!=(uint)size||receipt.UnityBase.ToUInt32()!=unityPlayerBase)
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
            uint observed=receipt.ObservedContext.ToUInt32();
            if(observed==0)return;
            if(observed!=contactManagerContext)
            {
                contactManagerContext=observed;
                if(checkpointSidecars.Count!=0&&checkpointSidecars.Values.Any(value=>value.Context!=observed))
                {
                    contextSnapshotInvalidations++;
                    ResetSceneOwnedCheckpointState("contact-manager-context-changed",false);
                }
            }
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
            CancelDirtyInteractionWork();
            pendingContactPoolAction=0;pendingContactPoolFrame=-1;pendingCoreSnapshot=null;
            scheduledContactPoolCaptureFrame=-1;scheduledContactPoolLastObservedFrame=-1;
            checkpointSidecars.Clear();warpTargetSidecar=null;automaticRestorePending=false;
            warpInProgress=false;warpTargetRestoreEligible=false;warpTargetFrame=-1;
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

        private static void ValidateContactManagerOwnerState(ContactManagerOwnerState value)
        {
            if(value==null||value.Records==null||value.ShapeOwners0==null||value.ShapeOwners1==null||
                value.Endpoints0==null||value.Endpoints1==null||
                value.Records.Length!=value.ShapeOwners0.Length||value.Records.Length!=value.ShapeOwners1.Length||
                value.Records.Length!=value.Endpoints0.Length||value.Records.Length!=value.Endpoints1.Length||
                value.Endpoints0.Any(endpoint=>endpoint==null)||value.Endpoints1.Any(endpoint=>endpoint==null)||
                value.Receipt.Result!=1||value.Receipt.ConsistencyFlags!=0xFFu||
                value.Receipt.RecordsWritten!=(uint)value.Records.Length||
                value.Receipt.RecordsRequired!=value.Receipt.RecordsWritten||
                value.Receipt.UsedCount!=value.Receipt.RecordsWritten||
                value.Receipt.ActiveCount!=value.Receipt.UsedCount||
                value.Receipt.FreeCount+value.Receipt.UsedCount!=value.Receipt.TotalSlots)
                throw new InvalidOperationException("Contact-manager owner checkpoint sidecar is incomplete.");
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
            ValidateContactManagerOwnerState(value.ContactManagerOwners);
            ValidateDirtyInteractionState(value.DirtyInteractions);
            if(value.ShapeInstancePairPool.NPhaseCore!=value.DirtyInteractions.NPhaseCore||
                value.ActorPairPool.NPhaseCore!=value.DirtyInteractions.NPhaseCore||
                value.ActorPairReportPool.NPhaseCore!=value.DirtyInteractions.NPhaseCore)
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
                    SameManifoldPoolSnapshot(previous.LargeManifoldPool,value.LargeManifoldPool)&&
                    SameManifoldPoolSnapshot(previous.SphereManifoldPool,value.SphereManifoldPool)&&
                    SameTransformDispatchSnapshot(previous.TransformDispatch,value.TransformDispatch)&&
                    SameDirtyInteractionSnapshot(previous.DirtyInteractions,value.DirtyInteractions);
                if(!same)throw new InvalidOperationException("A differing physics-pool/Transform-dispatch sidecar already owns output frame "+value.Frame+".");
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
                value.LargeManifoldPool==null||value.LargeManifoldPool.Order==null||
                value.SphereManifoldPool==null||value.SphereManifoldPool.Order==null)
                throw new InvalidOperationException("Checkpoint pool-coherence inputs are incomplete.");
            NativeContactManagerOwnerRecord[] owners=value.ContactManagerOwners.Records;
            var contactFree=new HashSet<uint>(value.ContactPoolOrder);
            var sipFree=new HashSet<uint>(value.ShapeInstancePairPool.Order);
            var actorPairFree=new HashSet<uint>(value.ActorPairPool.FreeOrder);
            var actorPairAllocated=new HashSet<uint>(value.ActorPairPool.AllocatedOrder);
            var actorPairReportFree=new HashSet<uint>(value.ActorPairReportPool.FreeOrder);
            var actorPairReportAllocated=new HashSet<uint>(value.ActorPairReportPool.AllocatedOrder);
            var largeFree=new HashSet<uint>(value.LargeManifoldPool.Order);
            var sphereFree=new HashSet<uint>(value.SphereManifoldPool.Order);
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
                !actorPairRowsCoherent||
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
                    a.ActorPairReportData!=b.ActorPairReportData||a.ActorPairHash!=b.ActorPairHash)
                    return false;
                if(!ReferenceEquals(left.Endpoints0[i].Collider,right.Endpoints0[i].Collider)||
                    !ReferenceEquals(left.Endpoints1[i].Collider,right.Endpoints1[i].Collider))return false;
            }
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
                {"largeManifoldPool",found?DescribeManifoldPoolState(value.LargeManifoldPool):null},
                {"sphereManifoldPool",found?DescribeManifoldPoolState(value.SphereManifoldPool):null},
                {"transformDispatchCaptured",found&&value.TransformDispatch!=null},
                {"dirtyInteractions",found?DescribeDirtyInteractionState(value.DirtyInteractions):null}};
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
                {"nativeSha256",nativeSha256},{"nativeApiVersion",library==IntPtr.Zero?(object)null:13},
                {"unityPlayerBase","0x"+unityPlayerBase.ToString("X8")},
                {"rebuilds",rebuilds},{"failure",failure},{"receipts",receipts.ToArray()},
                {"contactManagerContext",contactManagerContext==0?null:"0x"+contactManagerContext.ToString("X8")},
                {"observeContactManagerContext",observeContactManagerContext},{"contextObserverInstalled",contextObserverInstalled},
                {"contextObservations",contextObservations},{"lastContextObserverReceipt",lastContextObserverReceipt},
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
                {"scope","Optional batched Unity Create(false)/Create(true) actor replacement plus exact capsule re-registration, restricted to explicit paused one-shot targets or, only when automaticChefs is enabled, five canonical cycles for the four local chefs during a checkpoint restore's internal main-physics unfreeze. Ordinary forward and replay unpauses never rebuild actors. The pass-through native observer records only the current PxsContext. Caller-owned, bounded sidecars retain the complete contact-manager, large-manifold and sphere-manifold free-list orders plus TransformChangeDispatch and PhysX dirty-interaction order for each exact core checkpoint object. One pause-fenced command may schedule the same read-only capture transaction at an exact future NativeKitchenCheckpoint output boundary without splitting input; it binds the already-published exact core snapshot, never runs early or late, and fails closed if that output frame is skipped. A successful rewind prunes only future sidecars; the next replay unpause restores the selected pool state and projects surviving dirty interactions into checkpoint-relative order while preserving current-only slots when explicitly enabled. Forward game data, score and input are not rewritten."}};
            if(result!=null)value.Add("result",result);return value;
        }

        private void Deactivate()
        {
            CancelContactRecreateWork();
            if(dirtyInteractionHookInstalled)
            {
                CallDirtyInteractionAction(uninstallDirtyInteractionOrder,"uninstall",1);
                dirtyInteractionHookInstalled=false;
            }
            if(contextObserverInstalled)
            {
                RunContextObserver(uninstallContextObserver,"uninstall");contextObserverInstalled=false;
            }
            if(harmony!=null)harmony.UnpatchSelf();harmony=null;automatic=false;automaticGroundCollider=false;
            observeContactManagerContext=false;automaticContactPoolRestore=false;automaticTransformDispatchRestore=false;automaticRestorePending=false;
            dirtyInteractionRestoreMode=DirtyInteractionRestoreExact;
            warpInProgress=false;warpTargetRestoreEligible=false;warpTargetFrame=-1;
            pendingContactPoolAction=0;pendingContactPoolFrame=-1;pendingCoreSnapshot=null;
            scheduledContactPoolCaptureFrame=-1;scheduledContactPoolLastObservedFrame=-1;
            pendingDirtyCaptureSidecar=null;pendingDirtyCaptureOrdinal=0;
            dirtyRestorePendingValidation=false;pendingDirtyRestoreOrdinal=0;pendingDirtyRestoreState=null;
            contactRecreatePendingValidation=false;pendingContactRecreateSidecar=null;
            checkpointSidecars.Clear();warpTargetSidecar=null;contactManagerContext=0;
            coreRoundIdentity=null;sceneMetadataGeneration=-1;
            apiVersion=null;rebuildBatch=null;actorShapes=null;captureContactPoolSnapshot=null;restoreContactPoolSnapshot=null;
            captureContactManagerActiveOwners=null;
            captureManifoldPoolSnapshot=null;restoreManifoldPoolSnapshot=null;captureSipPoolSnapshot=null;
            captureActorPairPoolSnapshot=null;
            captureActorPairReportPoolSnapshot=null;
            installContextObserver=null;statusContextObserver=null;uninstallContextObserver=null;
            contactRecreateApiVersion=null;contactRecreateAuditApiVersion=null;auditContactRecreate=null;
            armContactRecreate=null;statusContactRecreate=null;cancelContactRecreate=null;
            installDirtyInteractionOrder=null;statusDirtyInteractionOrder=null;armDirtyInteractionCapture=null;
            lastObservedDirtyNPhase=null;dirtyNPhaseObservationFloor=0;
            copyDirtyInteractionCapture=null;armDirtyInteractionRestore=null;cancelDirtyInteractionOrder=null;
            uninstallDirtyInteractionOrder=null;
            if(library!=IntPtr.Zero){FreeLibrary(library);library=IntPtr.Zero;}
            if(ReferenceEquals(active,this))active=null;
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
