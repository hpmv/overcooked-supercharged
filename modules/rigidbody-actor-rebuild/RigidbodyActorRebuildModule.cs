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
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeManifoldPoolCaptureSnapshot(
            UIntPtr unityBase,UIntPtr context,uint poolKind,IntPtr snapshot,uint capacity,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeManifoldPoolRestoreSnapshot(
            UIntPtr unityBase,UIntPtr context,uint poolKind,UIntPtr expectedPool,IntPtr snapshot,uint count,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeContextObserverAction(
            UIntPtr unityBase,IntPtr receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeDirtyInteractionAction(
            UIntPtr unityBase,IntPtr receipt);
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
            internal uint PoolKind,Pool,OrderHash;
            internal uint[] Order;
        }

        private sealed class DirtyInteractionState
        {
            internal uint NPhaseCore,Entries,EntriesNext,Hash,EntriesCapacity,HashSize,OrderHash;
            internal NativeDirtyInteractionKey[] Keys;
        }

        private sealed class CheckpointSidecar
        {
            internal int Frame;
            internal uint Context,FreeArray,OrderHash;
            internal uint[] ContactPoolOrder;
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
        private NativeManifoldPoolCaptureSnapshot captureManifoldPoolSnapshot;
        private NativeManifoldPoolRestoreSnapshot restoreManifoldPoolSnapshot;
        private NativeContextObserverAction installContextObserver,statusContextObserver,uninstallContextObserver;
        private NativeDirtyInteractionAction installDirtyInteractionOrder,statusDirtyInteractionOrder;
        private NativeDirtyInteractionAction armDirtyInteractionCapture,cancelDirtyInteractionOrder,uninstallDirtyInteractionOrder;
        private NativeDirtyInteractionCaptureCopy copyDirtyInteractionCapture;
        private NativeDirtyInteractionRestoreArm armDirtyInteractionRestore;
        private uint unityPlayerBase;
        private uint dirtyInteractionRestoreMode=DirtyInteractionRestoreExact;
        private uint contactManagerContext,contextObservations;
        private string nativePath,nativeSha256,failure;
        private bool automatic,automaticGroundCollider,observeContactManagerContext,contextObserverInstalled;
        private bool automaticContactPoolRestore,automaticTransformDispatchRestore,automaticRestorePending,warpInProgress,warpTargetRestoreEligible,disposed;
        private bool dirtyInteractionHookInstalled,dirtyRestorePendingValidation;
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
        private CheckpointSidecar pendingDirtyCaptureSidecar;
        private DirtyInteractionState pendingDirtyRestoreState;
        private uint pendingDirtyCaptureOrdinal,pendingDirtyRestoreOrdinal;
        private long transformDispatchCaptures,transformDispatchRestores;
        private readonly List<object> transformDispatchReceipts=new List<object>();
        private object lastContextObserverReceipt,lastSceneOwnedReset;
        private readonly List<object> dirtyInteractionReceipts=new List<object>();
        private long dirtyInteractionCaptures,dirtyInteractionRestores;
        private long scheduledContactPoolCaptureArms,scheduledContactPoolCaptureTriggers;

        public string Name { get { return "authoring-rigidbody-actor-rebuild-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("RigidbodyActorRebuildModule");
            if(args==null)args=new Dictionary<string,object>();
            object result=null;
            if(operation=="activate")Activate(args);
            else if(operation=="rebuild")result=RebuildOne(args);
            else if(operation=="capture-contact-pool-next")result=ArmContactPoolAction(args,1);
            else if(operation=="capture-contact-pool-at-frame")result=ArmContactPoolCaptureAtFrame(args);
            else if(operation=="restore-contact-pool-next")result=ArmContactPoolAction(args,2);
            else if(operation=="cancel-contact-pool-next")result=CancelContactPoolAction(args);
            else if(operation=="checkpoint-status")result=CheckpointStatus(args);
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, rebuild, capture-contact-pool-next, capture-contact-pool-at-frame, restore-contact-pool-next, cancel-contact-pool-next, checkpoint-status, status or deactivate.");
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
                captureManifoldPoolSnapshot=Export<NativeManifoldPoolCaptureSnapshot>("oc2_manifold_pool_capture_snapshot");
                restoreManifoldPoolSnapshot=Export<NativeManifoldPoolRestoreSnapshot>("oc2_manifold_pool_restore_snapshot");
                installContextObserver=Export<NativeContextObserverAction>("oc2_contact_manager_context_observer_install");
                statusContextObserver=Export<NativeContextObserverAction>("oc2_contact_manager_context_observer_status");
                uninstallContextObserver=Export<NativeContextObserverAction>("oc2_contact_manager_context_observer_uninstall");
                installDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_install");
                statusDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_status");
                armDirtyInteractionCapture=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_capture_arm");
                copyDirtyInteractionCapture=Export<NativeDirtyInteractionCaptureCopy>("oc2_dirty_interaction_order_capture_copy");
                armDirtyInteractionRestore=Export<NativeDirtyInteractionRestoreArm>("oc2_dirty_interaction_order_restore_arm");
                cancelDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_cancel");
                uninstallDirtyInteractionOrder=Export<NativeDirtyInteractionAction>("oc2_dirty_interaction_order_uninstall");
                if(apiVersion()!=11)throw new InvalidOperationException("Native actor-rebuild API version mismatch.");
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
            module.CancelDirtyInteractionWork();
        }

        public static void AfterCaptureFrame(int __0)
        {
            var module=active;
            if(module==null||!module.automaticContactPoolRestore||module.scheduledContactPoolCaptureFrame<0)return;
            try
            {
                module.ObserveSceneGeneration();
                module.ObserveCoreRoundIdentity();
                // Either observer may have recognized a new scene/round and
                // transactionally cancelled scene-owned scheduled work.
                if(module.scheduledContactPoolCaptureFrame<0)return;
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
            if(args.Count!=1||!args.ContainsKey("frame")||args["frame"]==null||args["frame"].GetType()!=typeof(int))
                throw new ArgumentException("capture-contact-pool-at-frame requires exactly frame:int.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Scheduled contact-pool capture requires the authoring pause fence.");
            if(!ReferenceEquals(active,this)||!automaticContactPoolRestore)
                throw new InvalidOperationException("Scheduled contact-pool capture requires the active automatic contact-pool restore module.");
            ObserveSceneGeneration();
            ObserveCoreRoundIdentity();
            RefreshObservedContactManagerContext();
            if(!ReferenceEquals(active,this)||contactManagerContext==0)
                throw new InvalidOperationException("Scheduled contact-pool capture requires an active observed or explicit context.");
            if(pendingContactPoolAction!=0||scheduledContactPoolCaptureFrame>=0||
                pendingContactPoolFrame>=0||pendingCoreSnapshot!=null||pendingDirtyCaptureSidecar!=null||
                dirtyRestorePendingValidation||automaticRestorePending||warpInProgress)
                throw new InvalidOperationException("Another contact-pool capture, restore, or dirty-interaction action is pending or scheduled.");
            int target=(int)args["frame"];
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
                pendingDirtyCaptureSidecar!=null||dirtyRestorePendingValidation||automaticRestorePending||warpInProgress)
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
                captureManifoldPoolSnapshot==null||restoreManifoldPoolSnapshot==null)
                throw new InvalidOperationException("Native caller-owned physics-pool helper is not active.");
            int actionFrame=action==1?pendingContactPoolFrame:selected.Frame;
            object actionCoreSnapshot=action==1?pendingCoreSnapshot:selected.CoreSnapshot;
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
                    throw new InvalidOperationException("Native contact-pool action failed: result="+receipt.Result+", Win32/error="+receipt.LastError+".");
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
            if(receipt.ApiVersion!=11||receipt.StructSize!=(uint)size||receipt.Context.ToUInt32()!=contactManagerContext)
                throw new InvalidOperationException("Native contact-pool receipt contract differs.");
            RecordContactPoolReceipt(action,actionFrame,receipt);
            if(action==1)
            {
                uint orderHash=ContactPoolOrderHash(capturedOrder);
                if(orderHash!=receipt.OrderHashBefore||receipt.OrderHashAfter!=receipt.OrderHashBefore)
                    throw new InvalidOperationException("Managed contact-pool snapshot hash differs from the native capture receipt.");
                ManifoldPoolState large=RunManifoldPoolAction(1,LargeManifoldPoolKind,null,actionFrame);
                ManifoldPoolState sphere=RunManifoldPoolAction(1,SphereManifoldPoolKind,null,actionFrame);
                TransformDispatchState dispatch=(automaticTransformDispatchRestore||requireTransformCapture)
                    ?RunTransformDispatchAction(1,null):null;
                var captured=new CheckpointSidecar {Frame=actionFrame,
                    Context=contactManagerContext,FreeArray=receipt.FreeArray.ToUInt32(),
                    OrderHash=orderHash,ContactPoolOrder=capturedOrder,
                    LargeManifoldPool=large,SphereManifoldPool=sphere,
                    TransformDispatch=dispatch,CoreSnapshot=actionCoreSnapshot};
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
            if(receipt.ApiVersion!=11||receipt.StructSize!=(uint)size||
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
                    OrderHash=orderHash,Order=capturedOrder};
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
            if(receipt.ApiVersion!=11||receipt.StructSize!=(uint)size||receipt.UnityBase.ToUInt32()!=unityPlayerBase)
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
            if(receipt.ApiVersion!=11||receipt.StructSize!=(uint)size||receipt.UnityBase.ToUInt32()!=unityPlayerBase)
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
                value.Order.Length>MaximumManifolds||value.OrderHash!=ContactPoolOrderHash(value.Order))
                throw new InvalidOperationException("The "+poolName+" manifold-pool checkpoint sidecar is incomplete.");
            if(value.Order.Any(pointer=>pointer==0)||value.Order.Distinct().Count()!=value.Order.Length)
                throw new InvalidOperationException("The "+poolName+" manifold-pool checkpoint contains a null or duplicate free element.");
        }

        private static object DescribeManifoldPoolState(ManifoldPoolState value)
        {
            if(value==null)return null;
            object[] top=value.Order.Take(16).Select(pointer=>(object)("0x"+pointer.ToString("X8"))).ToArray();
            return new Dictionary<string,object>{{"poolKind",value.PoolKind},
                {"pool","0x"+value.Pool.ToString("X8")},{"freeCount",value.Order.Length},
                {"freeHead",value.Order.Length==0?"0x00000000":"0x"+value.Order[0].ToString("X8")},
                {"orderHash","0x"+value.OrderHash.ToString("X8")},{"top",top}};
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
            ValidateDirtyInteractionState(value.DirtyInteractions);
            if(!ReferenceEquals(value.CoreSnapshot,CoreCheckpointSnapshot(value.Frame)))
                throw new InvalidOperationException("Contact-pool checkpoint sidecar no longer owns the retained core snapshot.");
            CheckpointSidecar previous;
            if(checkpointSidecars.TryGetValue(value.Frame,out previous))
            {
                bool same=ReferenceEquals(previous.CoreSnapshot,value.CoreSnapshot)&&
                    previous.Context==value.Context&&previous.FreeArray==value.FreeArray&&
                    previous.OrderHash==value.OrderHash&&
                    previous.ContactPoolOrder.SequenceEqual(value.ContactPoolOrder)&&
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

        private static bool SameManifoldPoolSnapshot(ManifoldPoolState left,ManifoldPoolState right)
        {
            if(left==null||right==null)return left==right;
            return left.PoolKind==right.PoolKind&&left.Pool==right.Pool&&left.OrderHash==right.OrderHash&&
                left.Order!=null&&right.Order!=null&&left.Order.SequenceEqual(right.Order);
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
                {"nativeSha256",nativeSha256},{"nativeApiVersion",library==IntPtr.Zero?(object)null:11},
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
            checkpointSidecars.Clear();warpTargetSidecar=null;contactManagerContext=0;
            coreRoundIdentity=null;sceneMetadataGeneration=-1;
            apiVersion=null;rebuildBatch=null;actorShapes=null;captureContactPoolSnapshot=null;restoreContactPoolSnapshot=null;
            captureManifoldPoolSnapshot=null;restoreManifoldPoolSnapshot=null;
            installContextObserver=null;statusContextObserver=null;uninstallContextObserver=null;
            installDirtyInteractionOrder=null;statusDirtyInteractionOrder=null;armDirtyInteractionCapture=null;
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
