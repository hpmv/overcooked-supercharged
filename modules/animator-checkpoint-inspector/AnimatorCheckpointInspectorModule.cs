using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only diagnostic for an already-loaded ChefAnimatorCheckpoint revision.
    // It deliberately uses reflection so the retained original/replay blobs can
    // be inspected without retiring that active module and losing its history.
    public sealed class AnimatorCheckpointInspectorModule : IAuthoringModule
    {
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeOwnerGraphReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerConstant,Descriptors,Graph;
            public uint LayerCount,RecordCount,ByteSize,Hash,GraphDirty58,FailureRecord;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeOwnerGraphRecord
        {
            public uint RecordKind,LayerIndex,StateMachineIndex,BranchIndex,InputIndex;
            public UIntPtr Self,Vtable,Internal,Graph,InputEntries;
            public uint InputCount,InputCapacityRaw;
            public UIntPtr OutputEntries;
            public uint OutputCount,OutputCapacityRaw,Flags7C,Dirty9093,RawA0A3,RawA4A7,Word50;
            public UIntPtr Clip108,EntryAddress;
            public uint EntryWeightBits;
            public UIntPtr EntryPlayable;
            public uint EntryPortRaw;
            public UIntPtr ResolverOrigin,ResolverResult;
            public uint ResolverDepth,ResolverStatus;
            public uint CurrentTime28Low,CurrentTime28High,PreviousTime30Low,PreviousTime30High;
            public uint Duration38Low,Duration38High,Mode80,Raw9497;
            public UIntPtr BindingA8,BindingAC,BindingB0,BindingB4;
            public uint ClipCacheBC,ClipCacheC0,ClipInternalWeightBits,ClipFlags10C,BindingCachePresence;
            public UIntPtr BindingACInner;
            public uint BindingACCount,BindingACHash;
            public UIntPtr BindingB0Inner;
            public uint BindingB0Count,BindingB0Hash,BindingB4Hash;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeOverrideClipReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerMemory,Graph;
            public uint Stage,MemorySizeBefore,MemorySizeAfter,MemoryHashBefore,MemoryHashAfter;
            public uint OwnerCountBefore,OwnerCountAfter,OwnerHashBefore,OwnerHashAfter;
            public uint GraphDirtyBefore,GraphDirtyAfter,ControllerDirty9093Before,ControllerDirty9093After;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeEndTransitionReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerConstant,Descriptors,Graph;
            public uint Stage,TargetTopologyCount,CurrentTopologyCount,TargetOwnerCount,CurrentOwnerCount;
            public uint ProjectedOwnerCount,AfterOwnerCount,PlannedTransitionCount,CompletedTransitionCount;
            public uint TargetTopologyHash,CurrentTopologyHash,TargetOwnerHash,CurrentOwnerHash,ProjectedOwnerHash,AfterOwnerHash;
            public uint GraphDirtyBefore,GraphDirtyProjected,GraphDirtyAfter;
            public uint FailureLayer,FailureStateMachine,FailureRecord,FailureByteOffset,ExpectedWord,ActualWord;
            public uint MutationStarted,PlannedReboundClipCount,CompletedReboundClipCount;
            public uint PlannedWeightPlanCount,CompletedWeightPlanCount,PrimedWeightWriteCount;
            public uint RolledBackWeightWriteCount,RollbackFailure;
            public uint ControllerDirtyBefore,ControllerDirtyAfterEnd,ControllerDirtyProjected,ControllerDirtyAfter;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativePlayableTimeReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerConstant,Descriptors,Graph;
            public uint Stage,TargetOwnerCount,CurrentOwnerCount,ProjectedOwnerCount,AfterOwnerCount;
            public uint TargetOwnerHash,CurrentOwnerHash,ProjectedOwnerHash,AfterOwnerHash;
            public uint GraphDirtyBefore,GraphDirtyAfter;
            public uint UniqueTargetNodes,UniqueCurrentNodes,PlannedNodeCount,CompletedNodeCount;
            public uint OrdinaryAdvanceRecipes,SeekOnlyRecipes;
            public uint FailureRecord,FailureTargetRecord,FailureByteOffset,ExpectedWord,ActualWord;
            public UIntPtr FailureNode;
            public uint MutationStarted;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeTargetNullClipReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerConstant,Descriptors,Graph;
            public uint Stage,TargetOwnerCount,CurrentOwnerCount,ProjectedOwnerCount,StableOwnerCount,AfterOwnerCount;
            public uint TargetOwnerHash,CurrentOwnerHash,ProjectedOwnerHash,StableOwnerHash,AfterOwnerHash;
            public uint GraphDirtyBefore,GraphDirtyProjected,GraphDirtyAfter,PlannedClipCount,CompletedClipCount;
            public uint FailureLayer,FailureStateMachine,FailureRecord,FailureByteOffset,ExpectedWord,ActualWord;
            public uint MutationStarted,ExactStateMachineCount,RotatedStateMachineCount;
            public uint ControllerDirtyBefore,ControllerDirtyProjected,ControllerDirtyAfter;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NativeApiVersion();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeOwnerGraphCapture(
            UIntPtr unityBase,UIntPtr animator,IntPtr output,uint capacity,ref NativeOwnerGraphReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeOverrideClipProbe(
            UIntPtr unityBase,UIntPtr animator,ref NativeOverrideClipReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeSettledEndTransitionNormalize(
            UIntPtr unityBase,UIntPtr animator,IntPtr targetTopology,uint targetTopologySize,
            IntPtr targetOwner,uint targetOwnerSize,uint requireNoPlan,ref NativeEndTransitionReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativePlayableTimeRestore(
            UIntPtr unityBase,UIntPtr animator,IntPtr targetOwner,uint targetOwnerSize,
            ref NativePlayableTimeReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeTargetNullClipRestore(
            UIntPtr unityBase,UIntPtr animator,IntPtr targetOwner,uint targetOwnerSize,
            ref NativeTargetNullClipReceipt receipt);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32",SetLastError=true)] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);

        private bool disposed;

        public string Name { get { return "animator-checkpoint-inspector-v10-update-zero-probe"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("AnimatorCheckpointInspectorModule");
            if(args==null)args=new Dictionary<string,object>();
            RequireFence();
            if(operation=="status")
            {
                if(args.Count!=0)throw new ArgumentException("status takes no arguments.");
                return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},{"active",true},
                    {"scope","Read-only reflection over retained ChefAnimatorCheckpoint history/resume state plus explicit acknowledgement-gated sacrificial native probes. Read-only operations do not capture or write live state; probe-override-clip-playables mutates one Animator Playable graph and requires a fresh process/session before any parity claim."}};
            }
            if(operation=="recorder-status")
            {
                if(args.Count!=0)throw new ArgumentException("recorder-status takes no arguments.");
                return RecorderStatus();
            }
            if(operation=="dump-resume-ready-native-state")
            {
                if(args.Count!=0)throw new ArgumentException("dump-resume-ready-native-state takes no arguments.");
                return DumpResumeReadyNativeState();
            }
            if(operation=="dump-resume-ready-owner-state")
            {
                if(args.Count!=0)throw new ArgumentException("dump-resume-ready-owner-state takes no arguments.");
                return DumpResumeReadyOwnerState();
            }
            if(operation=="dump-resume-ready-public-state")
            {
                if(args.Count!=0)throw new ArgumentException("dump-resume-ready-public-state takes no arguments.");
                return DumpResumeReadyPublicState();
            }
            if(operation=="probe-override-clip-playables")return ProbeOverrideClipPlayables(args);
            if(operation=="probe-animator-update-zero")return ProbeAnimatorUpdateZero(args);
            if(operation=="probe-animator-rebind")return ProbeAnimatorRebind(args);
            if(operation=="probe-target-null-clip-restore")return ProbeTargetNullClipRestore(args);
            if(operation=="probe-normalize-retained-target")return ProbeNormalizeRetainedTarget(args);
            if(operation=="probe-restore-times-and-normalize-retained-target")return ProbeRestoreTimesAndNormalizeRetainedTarget(args);
            if(operation=="dump-live-owner-graph")return DumpLiveOwnerGraph(args);
            if(operation=="dump-history-frame")
            {
                if(args.Count!=1||!args.ContainsKey("frame"))throw new ArgumentException("dump-history-frame requires exactly {frame}.");
                return DumpHistoryFrame(Convert.ToInt32(args["frame"]));
            }
            if(operation=="dump-movement-frame")
            {
                if(args.Count!=1||!args.ContainsKey("frame"))throw new ArgumentException("dump-movement-frame requires exactly {frame}.");
                return DumpMovementFrame(Convert.ToInt32(args["frame"]));
            }
            if(operation!="dump-frame"||args.Count!=1||!args.ContainsKey("frame"))
                throw new ArgumentException("Use status {}, recorder-status {}, dump-resume-ready-native-state {}, dump-resume-ready-owner-state {}, dump-resume-ready-public-state {}, probe-animator-update-zero {animatorPath,acknowledgeSacrificialProcess:true}, probe-override-clip-playables, probe-target-null-clip-restore, probe-normalize-retained-target, or probe-restore-times-and-normalize-retained-target with {nativePath,sha256,animatorPath,acknowledgeSacrificialProcess:true}, dump-live-owner-graph {nativePath,sha256}, dump-history-frame {frame}, dump-movement-frame {frame}, or dump-frame {frame}.");
            return DumpFrame(Convert.ToInt32(args["frame"]));
        }

        private object ProbeAnimatorUpdateZero(Dictionary<string,object> args)
        {
            if(args.Count!=2||!args.ContainsKey("animatorPath")||
               !args.ContainsKey("acknowledgeSacrificialProcess")||
               !Convert.ToBoolean(args["acknowledgeSacrificialProcess"]))
                throw new ArgumentException("probe-animator-update-zero requires exactly animatorPath and acknowledgeSacrificialProcess:true.");
            string requestedPath=Convert.ToString(args["animatorPath"]);
            object checkpoint=ActiveCheckpoint();Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained failed resume boundary for the sacrificial zero-update probe.");
            object selected=null;
            foreach(object state in (Array)Field(ready.GetType(),"Animators").GetValue(ready))
                if(Convert.ToString(Field(state.GetType(),"Path").GetValue(state))==requestedPath)
                {if(selected!=null)throw new InvalidOperationException("Animator path is not unique: "+requestedPath);selected=state;}
            if(selected==null)throw new InvalidOperationException("Retained Animator path was not found: "+requestedPath);
            UnityEngine.Animator animator=(UnityEngine.Animator)Field(selected.GetType(),"Animator").GetValue(selected);
            int expectedInstanceId=Convert.ToInt32(Field(selected.GetType(),"InstanceId").GetValue(selected));
            if(animator==null||animator.GetInstanceID()!=expectedInstanceId)
                throw new InvalidOperationException("Animator incarnation changed before sacrificial zero update: "+requestedPath);

            MethodInfo captureOwner=Method(moduleType,"CaptureOwnerGraph");
            MethodInfo captureMemory=Method(moduleType,"CaptureControllerMemory");
            MethodInfo captureTopology=Method(moduleType,"CaptureTransitionTopology");
            MethodInfo captureMixer=Method(moduleType,"CaptureMixerGraph");
            byte[] targetOwner=StateBytes(Field(selected.GetType(),"OwnerGraph").GetValue(selected));
            byte[] targetMemory=(byte[])Field(selected.GetType(),"ControllerMemory").GetValue(selected);
            byte[] targetTopology=StateBytes(Field(selected.GetType(),"TransitionTopology").GetValue(selected));
            byte[] targetMixer=StateBytes(Field(selected.GetType(),"MixerGraph").GetValue(selected));
            // A prior sacrificial Rebind can intentionally leave the live
            // controller between graph initialisation phases.  This probe is
            // about the fresh Rebind postimage, so do not require a readable
            // preimage before issuing the operation again.
            byte[] ownerBefore=new byte[0],memoryBefore=new byte[0],topologyBefore=new byte[0],mixerBefore=new byte[0];

            animator.Update(1f/60f);

            byte[] ownerAfter=StateBytes(Invoke(captureOwner,checkpoint,new object[]{animator}));
            byte[] memoryAfter=(byte[])Invoke(captureMemory,checkpoint,new object[]{animator});
            byte[] topologyAfter=StateBytes(Invoke(captureTopology,checkpoint,new object[]{animator}));
            byte[] mixerAfter=StateBytes(Invoke(captureMixer,checkpoint,new object[]{animator}));
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                {"operation","probe-animator-update-zero"},{"processSacrificial",true},
                {"animatorPath",requestedPath},{"animatorInstanceId",expectedInstanceId},
                {"owner",CompareProbeState(targetOwner,ownerBefore,ownerAfter)},
                {"controllerMemory",CompareProbeState(targetMemory,memoryBefore,memoryAfter)},
                {"transitionTopology",CompareProbeState(targetTopology,topologyBefore,topologyAfter)},
                {"mixerGraph",CompareProbeState(targetMixer,mixerBefore,mixerAfter)},
                {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                {"scope","This call invoked UnityEngine.Animator.Update(0) on exactly one retained Animator and compared the native checkpoint tuple immediately before and after. The process is diagnostic-only regardless of result; reload before any rewind-parity claim."}};
        }

        private object ProbeAnimatorRebind(Dictionary<string,object> args)
        {
            if(args.Count!=2||!args.ContainsKey("animatorPath")||
               !args.ContainsKey("acknowledgeSacrificialProcess")||
               !Convert.ToBoolean(args["acknowledgeSacrificialProcess"]))
                throw new ArgumentException("probe-animator-rebind requires exactly animatorPath and acknowledgeSacrificialProcess:true.");
            string requestedPath=Convert.ToString(args["animatorPath"]);
            object checkpoint=ActiveCheckpoint();Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained failed resume boundary for the sacrificial rebind probe.");
            object selected=null;
            foreach(object state in (Array)Field(ready.GetType(),"Animators").GetValue(ready))
                if(Convert.ToString(Field(state.GetType(),"Path").GetValue(state))==requestedPath)
                {if(selected!=null)throw new InvalidOperationException("Animator path is not unique: "+requestedPath);selected=state;}
            if(selected==null)throw new InvalidOperationException("Retained Animator path was not found: "+requestedPath);
            UnityEngine.Animator animator=(UnityEngine.Animator)Field(selected.GetType(),"Animator").GetValue(selected);
            int expectedInstanceId=Convert.ToInt32(Field(selected.GetType(),"InstanceId").GetValue(selected));
            if(animator==null||animator.GetInstanceID()!=expectedInstanceId)
                throw new InvalidOperationException("Animator incarnation changed before sacrificial rebind: "+requestedPath);

            MethodInfo captureOwner=Method(moduleType,"CaptureOwnerGraph");
            MethodInfo captureMemory=Method(moduleType,"CaptureControllerMemory");
            MethodInfo captureTopology=Method(moduleType,"CaptureTransitionTopology");
            MethodInfo captureMixer=Method(moduleType,"CaptureMixerGraph");
            byte[] targetOwner=StateBytes(Field(selected.GetType(),"OwnerGraph").GetValue(selected));
            byte[] targetMemory=(byte[])Field(selected.GetType(),"ControllerMemory").GetValue(selected);
            byte[] targetTopology=StateBytes(Field(selected.GetType(),"TransitionTopology").GetValue(selected));
            byte[] targetMixer=StateBytes(Field(selected.GetType(),"MixerGraph").GetValue(selected));
            byte[] ownerBefore=StateBytes(Invoke(captureOwner,checkpoint,new object[]{animator}));
            byte[] memoryBefore=(byte[])Invoke(captureMemory,checkpoint,new object[]{animator});
            byte[] topologyBefore=StateBytes(Invoke(captureTopology,checkpoint,new object[]{animator}));
            byte[] mixerBefore=StateBytes(Invoke(captureMixer,checkpoint,new object[]{animator}));

            float speedBefore=animator.speed;
            animator.speed=1f;
            animator.Rebind();
            animator.Update(0f);
            animator.speed=speedBefore;

            byte[] ownerAfter=StateBytes(Invoke(captureOwner,checkpoint,new object[]{animator}));
            byte[] memoryAfter=(byte[])Invoke(captureMemory,checkpoint,new object[]{animator});
            byte[] topologyAfter=StateBytes(Invoke(captureTopology,checkpoint,new object[]{animator}));
            byte[] mixerAfter=StateBytes(Invoke(captureMixer,checkpoint,new object[]{animator}));
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                {"operation","probe-animator-rebind"},{"processSacrificial",true},
                {"animatorPath",requestedPath},{"animatorInstanceId",expectedInstanceId},
                {"owner",CompareProbeState(targetOwner,ownerBefore,ownerAfter)},
                {"controllerMemory",CompareProbeState(targetMemory,memoryBefore,memoryAfter)},
                {"transitionTopology",CompareProbeState(targetTopology,topologyBefore,topologyAfter)},
                {"mixerGraph",CompareProbeState(targetMixer,mixerBefore,mixerAfter)},
                {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                {"scope","This call invoked UnityEngine.Animator.Rebind followed by one 1/60-second Animator.Update at temporary speed one on exactly one retained Animator and compared the native checkpoint tuple immediately before and after. The process is diagnostic-only regardless of result; reload before any rewind-parity claim."}};
        }

        private static object CompareProbeState(byte[] target,byte[] before,byte[] after)
        {
            return new Dictionary<string,object>{{"targetBytes",target.Length},{"beforeBytes",before.Length},{"afterBytes",after.Length},
                {"targetHash",Hash(target)},{"beforeHash",Hash(before)},{"afterHash",Hash(after)},
                {"targetExactBefore",target.SequenceEqual(before)},{"targetExactAfter",target.SequenceEqual(after)},
                {"changed",!before.SequenceEqual(after)},{"targetBeforeFirstDifference",FirstDifference(target,before)},
                {"targetAfterFirstDifference",FirstDifference(target,after)},{"beforeAfterFirstDifference",FirstDifference(before,after)}};
        }

        private object ProbeTargetNullClipRestore(Dictionary<string,object> args)
        {
            if(args.Count!=4||!args.ContainsKey("nativePath")||!args.ContainsKey("sha256")||
               !args.ContainsKey("animatorPath")||!args.ContainsKey("acknowledgeSacrificialProcess")||
               !Convert.ToBoolean(args["acknowledgeSacrificialProcess"]))
                throw new ArgumentException("probe-target-null-clip-restore requires exactly nativePath, sha256, animatorPath, and acknowledgeSacrificialProcess:true.");
            string path=Path.GetFullPath(Convert.ToString(args["nativePath"]));
            string expectedHash=Convert.ToString(args["sha256"]).ToUpperInvariant();
            string requestedPath=Convert.ToString(args["animatorPath"]);
            if(!File.Exists(path))throw new FileNotFoundException("Native Animator probe helper is missing.",path);
            string actualHash=FileHash(path);
            if(actualHash!=expectedHash)throw new InvalidOperationException("Native Animator probe helper hash mismatch: "+actualHash);
            if(IntPtr.Size!=4)throw new InvalidOperationException("Native Animator probe requires the x86 player.");

            object checkpoint=ActiveCheckpoint();Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained failed resume boundary for the sacrificial probe.");
            object selected=null;
            foreach(object state in (Array)Field(ready.GetType(),"Animators").GetValue(ready))
                if(Convert.ToString(Field(state.GetType(),"Path").GetValue(state))==requestedPath)
                {if(selected!=null)throw new InvalidOperationException("Animator path is not unique: "+requestedPath);selected=state;}
            if(selected==null)throw new InvalidOperationException("Retained Animator path was not found: "+requestedPath);
            UnityEngine.Animator animator=(UnityEngine.Animator)Field(selected.GetType(),"Animator").GetValue(selected);
            int expectedInstanceId=Convert.ToInt32(Field(selected.GetType(),"InstanceId").GetValue(selected));
            if(animator==null||animator.GetInstanceID()!=expectedInstanceId)
                throw new InvalidOperationException("Animator incarnation changed before target-null clip restoration: "+requestedPath);
            byte[] owner=StateBytes(Field(selected.GetType(),"OwnerGraph").GetValue(selected));
            if(owner.Length==0)throw new InvalidOperationException("Retained native target owner blob is empty.");

            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(String.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            uint unityBase=unchecked((uint)unity.BaseAddress.ToInt32());
            IntPtr animatorPointer=(IntPtr)Field(typeof(UnityEngine.Object),"m_CachedPtr").GetValue(animator);
            if(animatorPointer==IntPtr.Zero)throw new InvalidOperationException("Animator native pointer is null: "+requestedPath);

            IntPtr library=LoadLibrary(path);
            if(library==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            GCHandle ownerPin=default(GCHandle);
            try
            {
                NativeApiVersion version=NativeExport<NativeApiVersion>(library,"oc2_animator_checkpoint_api_version");
                NativeTargetNullClipRestore restore=NativeExport<NativeTargetNullClipRestore>(library,"oc2_animator_target_null_clip_restore");
                if(version()!=16)throw new InvalidOperationException("Native Animator target-null clip API version mismatch.");
                int receiptSize=Marshal.SizeOf(typeof(NativeTargetNullClipReceipt));
                if(receiptSize!=152)throw new InvalidOperationException("Managed target-null clip receipt layout differs: "+receiptSize+".");
                ownerPin=GCHandle.Alloc(owner,GCHandleType.Pinned);
                NativeTargetNullClipReceipt receipt=new NativeTargetNullClipReceipt();
                int nativeOk=restore(new UIntPtr(unityBase),new UIntPtr(unchecked((uint)animatorPointer.ToInt32())),
                    ownerPin.AddrOfPinnedObject(),(uint)owner.Length,ref receipt);
                return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                    {"operation","probe-target-null-clip-restore"},{"ok",nativeOk==1&&receipt.Result==1&&receipt.Stage==11},
                    {"processSacrificial",true},{"animatorPath",requestedPath},{"animatorInstanceId",expectedInstanceId},
                    {"nativePath",path},{"nativeSha256",actualHash},{"nativeApiVersion",version()},
                    {"receipt",DescribeTargetNullClipReceipt(receipt)},
                    {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                    {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                    {"scope","Sacrificial direct SetClip(null) restoration for exact-topology, double-zero-weight inactive branch-1 clips only. The helper simulates and calls the hashed read-only RootByType resolver, verifies a strict immediate owner postimage, and leaves cache deallocation to the ordinary paused Unity maintenance frame."}};
            }
            finally
            {
                if(ownerPin.IsAllocated)ownerPin.Free();FreeLibrary(library);
            }
        }

        private static object DescribeTargetNullClipReceipt(NativeTargetNullClipReceipt r)
        {
            return new Dictionary<string,object>{{"apiVersion",r.ApiVersion},{"structSize",r.StructSize},{"result",r.Result},
                {"lastError","0x"+r.LastError.ToString("X8")},{"stage",r.Stage},{"unityBase",Pointer(r.UnityBase)},
                {"animator",Pointer(r.Animator)},{"controller",Pointer(r.Controller)},{"controllerConstant",Pointer(r.ControllerConstant)},
                {"descriptors",Pointer(r.Descriptors)},{"graph",Pointer(r.Graph)},
                {"targetOwnerCount",r.TargetOwnerCount},{"currentOwnerCount",r.CurrentOwnerCount},
                {"projectedOwnerCount",r.ProjectedOwnerCount},{"stableOwnerCount",r.StableOwnerCount},{"afterOwnerCount",r.AfterOwnerCount},
                {"targetOwnerHash","0x"+r.TargetOwnerHash.ToString("X8")},{"currentOwnerHash","0x"+r.CurrentOwnerHash.ToString("X8")},
                {"projectedOwnerHash","0x"+r.ProjectedOwnerHash.ToString("X8")},{"stableOwnerHash","0x"+r.StableOwnerHash.ToString("X8")},
                {"afterOwnerHash","0x"+r.AfterOwnerHash.ToString("X8")},{"graphDirtyBefore","0x"+r.GraphDirtyBefore.ToString("X8")},
                {"graphDirtyProjected","0x"+r.GraphDirtyProjected.ToString("X8")},{"graphDirtyAfter","0x"+r.GraphDirtyAfter.ToString("X8")},
                {"plannedClipCount",r.PlannedClipCount},{"completedClipCount",r.CompletedClipCount},
                {"exactStateMachineCount",r.ExactStateMachineCount},{"rotatedStateMachineCount",r.RotatedStateMachineCount},
                {"controllerDirtyBefore","0x"+r.ControllerDirtyBefore.ToString("X8")},
                {"controllerDirtyProjected","0x"+r.ControllerDirtyProjected.ToString("X8")},
                {"controllerDirtyAfter","0x"+r.ControllerDirtyAfter.ToString("X8")},
                {"failureLayer",r.FailureLayer==UInt32.MaxValue?-1:(long)r.FailureLayer},
                {"failureStateMachine",r.FailureStateMachine==UInt32.MaxValue?-1:(long)r.FailureStateMachine},
                {"failureRecord",r.FailureRecord==UInt32.MaxValue?-1:(long)r.FailureRecord},
                {"failureByteOffset",r.FailureByteOffset==UInt32.MaxValue?-1:(long)r.FailureByteOffset},
                {"expectedWord","0x"+r.ExpectedWord.ToString("X8")},{"actualWord","0x"+r.ActualWord.ToString("X8")},
                {"mutationStarted",r.MutationStarted!=0}};
        }

        private object DumpResumeReadyPublicState()
        {
            object checkpoint=ActiveCheckpoint();Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained failed resume boundary.");

            MethodInfo capture=moduleType.GetMethods(BindingFlags.Instance|BindingFlags.NonPublic)
                .Single(value=>value.Name=="Capture"&&value.GetParameters().Length==3&&
                    value.GetParameters()[0].ParameterType==typeof(UnityEngine.Animator)&&
                    value.GetParameters()[1].ParameterType==typeof(bool)&&
                    value.GetParameters()[2].ParameterType==typeof(bool));
            var rows=new List<object>();
            foreach(object expected in (Array)Field(ready.GetType(),"Animators").GetValue(ready))
            {
                UnityEngine.Animator animator=(UnityEngine.Animator)Field(expected.GetType(),"Animator").GetValue(expected);
                object actual=capture.Invoke(checkpoint,new object[]{animator,true,false});
                Dictionary<string,object> comparison=ComparePublicState(expected,actual);
                rows.Add(new Dictionary<string,object>{
                    {"path",Convert.ToString(Field(expected.GetType(),"Path").GetValue(expected))},
                    {"differenceCount",comparison["count"]},{"firstDifference",comparison["first"]},
                    {"differences",comparison["all"]},
                    {"expectedTransformCount",((Array)Field(expected.GetType(),"Transforms").GetValue(expected)).Length},
                    {"actualTransformCount",((Array)Field(actual.GetType(),"Transforms").GetValue(actual)).Length}
                });
            }
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                {"operation","dump-resume-ready-public-state"},{"pendingResumeRestore",pending},
                {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                {"animators",rows.ToArray()},
                {"scope","Read-only exact comparison of retained checkpoint Animator public state, parameters, layers, and descendant local transforms against a fresh live capture."}};
        }

        private object ProbeRestoreTimesAndNormalizeRetainedTarget(Dictionary<string,object> args)
        {
            if(args.Count!=4||!args.ContainsKey("nativePath")||!args.ContainsKey("sha256")||
               !args.ContainsKey("animatorPath")||!args.ContainsKey("acknowledgeSacrificialProcess")||
               !Convert.ToBoolean(args["acknowledgeSacrificialProcess"]))
                throw new ArgumentException("probe-restore-times-and-normalize-retained-target requires exactly nativePath, sha256, animatorPath, and acknowledgeSacrificialProcess:true.");
            string path=Path.GetFullPath(Convert.ToString(args["nativePath"]));
            string expectedHash=Convert.ToString(args["sha256"]).ToUpperInvariant();
            string requestedPath=Convert.ToString(args["animatorPath"]);
            if(!File.Exists(path))throw new FileNotFoundException("Native Animator probe helper is missing.",path);
            string actualHash=FileHash(path);
            if(actualHash!=expectedHash)throw new InvalidOperationException("Native Animator probe helper hash mismatch: "+actualHash);
            if(IntPtr.Size!=4)throw new InvalidOperationException("Native Animator probe requires the x86 player.");

            object checkpoint=ActiveCheckpoint();Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained failed resume boundary for the sacrificial probe.");
            object selected=null;
            foreach(object state in (Array)Field(ready.GetType(),"Animators").GetValue(ready))
                if(Convert.ToString(Field(state.GetType(),"Path").GetValue(state))==requestedPath)
                {if(selected!=null)throw new InvalidOperationException("Animator path is not unique: "+requestedPath);selected=state;}
            if(selected==null)throw new InvalidOperationException("Retained Animator path was not found: "+requestedPath);
            UnityEngine.Animator animator=(UnityEngine.Animator)Field(selected.GetType(),"Animator").GetValue(selected);
            int expectedInstanceId=Convert.ToInt32(Field(selected.GetType(),"InstanceId").GetValue(selected));
            if(animator==null||animator.GetInstanceID()!=expectedInstanceId)
                throw new InvalidOperationException("Animator incarnation changed before sacrificial time restoration: "+requestedPath);
            byte[] topology=StateBytes(Field(selected.GetType(),"TransitionTopology").GetValue(selected));
            byte[] owner=StateBytes(Field(selected.GetType(),"OwnerGraph").GetValue(selected));
            if(topology.Length==0||owner.Length==0)throw new InvalidOperationException("Retained native target blobs are empty.");

            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(String.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            uint unityBase=unchecked((uint)unity.BaseAddress.ToInt32());
            IntPtr animatorPointer=(IntPtr)Field(typeof(UnityEngine.Object),"m_CachedPtr").GetValue(animator);
            if(animatorPointer==IntPtr.Zero)throw new InvalidOperationException("Animator native pointer is null: "+requestedPath);

            IntPtr library=LoadLibrary(path);
            if(library==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            GCHandle topologyPin=default(GCHandle),ownerPin=default(GCHandle);
            try
            {
                NativeApiVersion version=NativeExport<NativeApiVersion>(library,"oc2_animator_checkpoint_api_version");
                NativePlayableTimeRestore restoreTime=NativeExport<NativePlayableTimeRestore>(library,"oc2_animator_playable_time_restore");
                NativeSettledEndTransitionNormalize normalize=NativeExport<NativeSettledEndTransitionNormalize>(library,"oc2_animator_settled_end_transition_normalize");
                if(version()!=16)throw new InvalidOperationException("Native Animator probe API version mismatch.");
                int timeReceiptSize=Marshal.SizeOf(typeof(NativePlayableTimeReceipt));
                int transitionReceiptSize=Marshal.SizeOf(typeof(NativeEndTransitionReceipt));
                if(timeReceiptSize!=136||transitionReceiptSize!=184)
                    throw new InvalidOperationException("Managed native receipt layout differs: time="+timeReceiptSize+", transition="+transitionReceiptSize+".");
                topologyPin=GCHandle.Alloc(topology,GCHandleType.Pinned);ownerPin=GCHandle.Alloc(owner,GCHandleType.Pinned);
                NativePlayableTimeReceipt timeReceipt=new NativePlayableTimeReceipt();
                int timeOk=restoreTime(new UIntPtr(unityBase),new UIntPtr(unchecked((uint)animatorPointer.ToInt32())),
                    ownerPin.AddrOfPinnedObject(),(uint)owner.Length,ref timeReceipt);
                NativeEndTransitionReceipt transitionReceipt=new NativeEndTransitionReceipt();
                int transitionOk=0;
                if(timeOk==1&&timeReceipt.Result==1&&timeReceipt.Stage==10)
                    transitionOk=normalize(new UIntPtr(unityBase),new UIntPtr(unchecked((uint)animatorPointer.ToInt32())),
                        topologyPin.AddrOfPinnedObject(),(uint)topology.Length,ownerPin.AddrOfPinnedObject(),(uint)owner.Length,
                        0u,ref transitionReceipt);
                return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                    {"operation","probe-restore-times-and-normalize-retained-target"},
                    {"ok",timeOk==1&&timeReceipt.Result==1&&transitionOk==1&&transitionReceipt.Result==1},
                    {"processSacrificial",true},{"animatorPath",requestedPath},{"animatorInstanceId",expectedInstanceId},
                    {"nativePath",path},{"nativeSha256",actualHash},{"nativeApiVersion",version()},
                    {"timeRestoreOk",timeOk==1&&timeReceipt.Result==1},{"timeReceipt",DescribePlayableTimeReceipt(timeReceipt)},
                    {"normalizationAttempted",timeOk==1&&timeReceipt.Result==1},
                    {"normalizationOk",transitionOk==1&&transitionReceipt.Result==1},
                    {"normalizationReceipt",DescribeEndTransitionReceipt(transitionReceipt)},
                    {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                    {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                    {"scope","Sacrificial same-callback probe: restore each unique native Playable clock through virtual SetTime/OnAdvanceTime, verify its exact projected owner postimage, then immediately pass the retained target to guarded EndTransition normalization. MutationStarted or RollbackFailure requires reload before a parity claim; stage 10 alone is reversible weight priming."}};
            }
            finally
            {
                if(ownerPin.IsAllocated)ownerPin.Free();if(topologyPin.IsAllocated)topologyPin.Free();FreeLibrary(library);
            }
        }

        private static object DescribePlayableTimeReceipt(NativePlayableTimeReceipt r)
        {
            return new Dictionary<string,object>{{"apiVersion",r.ApiVersion},{"structSize",r.StructSize},{"result",r.Result},
                {"lastError","0x"+r.LastError.ToString("X8")},{"stage",r.Stage},{"unityBase",Pointer(r.UnityBase)},
                {"animator",Pointer(r.Animator)},{"controller",Pointer(r.Controller)},{"controllerConstant",Pointer(r.ControllerConstant)},
                {"descriptors",Pointer(r.Descriptors)},{"graph",Pointer(r.Graph)},
                {"targetOwnerCount",r.TargetOwnerCount},{"currentOwnerCount",r.CurrentOwnerCount},
                {"projectedOwnerCount",r.ProjectedOwnerCount},{"afterOwnerCount",r.AfterOwnerCount},
                {"targetOwnerHash","0x"+r.TargetOwnerHash.ToString("X8")},{"currentOwnerHash","0x"+r.CurrentOwnerHash.ToString("X8")},
                {"projectedOwnerHash","0x"+r.ProjectedOwnerHash.ToString("X8")},{"afterOwnerHash","0x"+r.AfterOwnerHash.ToString("X8")},
                {"graphDirtyBefore",r.GraphDirtyBefore},{"graphDirtyAfter",r.GraphDirtyAfter},
                {"uniqueTargetNodes",r.UniqueTargetNodes},{"uniqueCurrentNodes",r.UniqueCurrentNodes},
                {"plannedNodeCount",r.PlannedNodeCount},{"completedNodeCount",r.CompletedNodeCount},
                {"ordinaryAdvanceRecipes",r.OrdinaryAdvanceRecipes},{"seekOnlyRecipes",r.SeekOnlyRecipes},
                {"failureRecord",r.FailureRecord==UInt32.MaxValue?-1:(long)r.FailureRecord},
                {"failureTargetRecord",r.FailureTargetRecord==UInt32.MaxValue?-1:(long)r.FailureTargetRecord},
                {"failureByteOffset",r.FailureByteOffset==UInt32.MaxValue?-1:(long)r.FailureByteOffset},
                {"expectedWord","0x"+r.ExpectedWord.ToString("X8")},{"actualWord","0x"+r.ActualWord.ToString("X8")},
                {"failureNode",Pointer(r.FailureNode)},{"mutationStarted",r.MutationStarted!=0}};
        }

        private object ProbeNormalizeRetainedTarget(Dictionary<string,object> args)
        {
            if(args.Count!=4||!args.ContainsKey("nativePath")||!args.ContainsKey("sha256")||
               !args.ContainsKey("animatorPath")||!args.ContainsKey("acknowledgeSacrificialProcess")||
               !Convert.ToBoolean(args["acknowledgeSacrificialProcess"]))
                throw new ArgumentException("probe-normalize-retained-target requires exactly nativePath, sha256, animatorPath, and acknowledgeSacrificialProcess:true.");
            string path=Path.GetFullPath(Convert.ToString(args["nativePath"]));
            string expectedHash=Convert.ToString(args["sha256"]).ToUpperInvariant();
            string requestedPath=Convert.ToString(args["animatorPath"]);
            if(!File.Exists(path))throw new FileNotFoundException("Native Animator probe helper is missing.",path);
            string actualHash=FileHash(path);
            if(actualHash!=expectedHash)throw new InvalidOperationException("Native Animator probe helper hash mismatch: "+actualHash);
            if(IntPtr.Size!=4)throw new InvalidOperationException("Native Animator probe requires the x86 player.");

            object checkpoint=ActiveCheckpoint();Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained failed resume boundary for the sacrificial probe.");
            object selected=null;
            foreach(object state in (Array)Field(ready.GetType(),"Animators").GetValue(ready))
                if(Convert.ToString(Field(state.GetType(),"Path").GetValue(state))==requestedPath)
                {if(selected!=null)throw new InvalidOperationException("Animator path is not unique: "+requestedPath);selected=state;}
            if(selected==null)throw new InvalidOperationException("Retained Animator path was not found: "+requestedPath);
            UnityEngine.Animator animator=(UnityEngine.Animator)Field(selected.GetType(),"Animator").GetValue(selected);
            int expectedInstanceId=Convert.ToInt32(Field(selected.GetType(),"InstanceId").GetValue(selected));
            if(animator==null||animator.GetInstanceID()!=expectedInstanceId)
                throw new InvalidOperationException("Animator incarnation changed before sacrificial normalization: "+requestedPath);
            byte[] topology=StateBytes(Field(selected.GetType(),"TransitionTopology").GetValue(selected));
            byte[] owner=StateBytes(Field(selected.GetType(),"OwnerGraph").GetValue(selected));
            if(topology.Length==0||owner.Length==0)throw new InvalidOperationException("Retained native target blobs are empty.");

            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(String.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            uint unityBase=unchecked((uint)unity.BaseAddress.ToInt32());
            IntPtr animatorPointer=(IntPtr)Field(typeof(UnityEngine.Object),"m_CachedPtr").GetValue(animator);
            if(animatorPointer==IntPtr.Zero)throw new InvalidOperationException("Animator native pointer is null: "+requestedPath);

            IntPtr library=LoadLibrary(path);
            if(library==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            GCHandle topologyPin=default(GCHandle),ownerPin=default(GCHandle);
            try
            {
                NativeApiVersion version=NativeExport<NativeApiVersion>(library,"oc2_animator_checkpoint_api_version");
                NativeSettledEndTransitionNormalize normalize=NativeExport<NativeSettledEndTransitionNormalize>(library,"oc2_animator_settled_end_transition_normalize");
                if(version()!=16)throw new InvalidOperationException("Native Animator probe API version mismatch.");
                int receiptSize=Marshal.SizeOf(typeof(NativeEndTransitionReceipt));
                if(receiptSize!=184)throw new InvalidOperationException("Managed EndTransition receipt layout differs: "+receiptSize);
                topologyPin=GCHandle.Alloc(topology,GCHandleType.Pinned);ownerPin=GCHandle.Alloc(owner,GCHandleType.Pinned);
                NativeEndTransitionReceipt receipt=new NativeEndTransitionReceipt();
                int nativeOk=normalize(new UIntPtr(unityBase),new UIntPtr(unchecked((uint)animatorPointer.ToInt32())),
                    topologyPin.AddrOfPinnedObject(),(uint)topology.Length,ownerPin.AddrOfPinnedObject(),(uint)owner.Length,
                    0u,ref receipt);
                return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                    {"operation","probe-normalize-retained-target"},{"ok",nativeOk==1&&receipt.Result==1},
                    {"processSacrificial",true},{"animatorPath",requestedPath},{"animatorInstanceId",expectedInstanceId},
                    {"nativePath",path},{"nativeSha256",actualHash},{"nativeApiVersion",version()},
                    {"receipt",DescribeEndTransitionReceipt(receipt)},
                    {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                    {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                    {"scope","This call passed the retained exact target topology/owner blobs to the guarded Unity EndTransition normalizer for one Animator. Stage 10 or later means native mutation began; reload before any rewind-parity claim."}};
            }
            finally
            {
                if(ownerPin.IsAllocated)ownerPin.Free();if(topologyPin.IsAllocated)topologyPin.Free();FreeLibrary(library);
            }
        }

        private static object DescribeEndTransitionReceipt(NativeEndTransitionReceipt r)
        {
            return new Dictionary<string,object>{{"apiVersion",r.ApiVersion},{"structSize",r.StructSize},{"result",r.Result},
                {"lastError","0x"+r.LastError.ToString("X8")},{"stage",r.Stage},{"unityBase",Pointer(r.UnityBase)},
                {"animator",Pointer(r.Animator)},{"controller",Pointer(r.Controller)},{"controllerConstant",Pointer(r.ControllerConstant)},
                {"descriptors",Pointer(r.Descriptors)},{"graph",Pointer(r.Graph)},{"targetTopologyCount",r.TargetTopologyCount},
                {"currentTopologyCount",r.CurrentTopologyCount},{"targetOwnerCount",r.TargetOwnerCount},{"currentOwnerCount",r.CurrentOwnerCount},
                {"projectedOwnerCount",r.ProjectedOwnerCount},{"afterOwnerCount",r.AfterOwnerCount},
                {"plannedTransitionCount",r.PlannedTransitionCount},{"completedTransitionCount",r.CompletedTransitionCount},
                {"plannedReboundClipCount",r.PlannedReboundClipCount},{"completedReboundClipCount",r.CompletedReboundClipCount},
                {"plannedWeightPlanCount",r.PlannedWeightPlanCount},{"completedWeightPlanCount",r.CompletedWeightPlanCount},
                {"primedWeightWriteCount",r.PrimedWeightWriteCount},{"rolledBackWeightWriteCount",r.RolledBackWeightWriteCount},
                {"rollbackFailure",r.RollbackFailure},{"mutationStarted",r.MutationStarted!=0u},
                {"targetTopologyHash","0x"+r.TargetTopologyHash.ToString("X8")},{"currentTopologyHash","0x"+r.CurrentTopologyHash.ToString("X8")},
                {"targetOwnerHash","0x"+r.TargetOwnerHash.ToString("X8")},{"currentOwnerHash","0x"+r.CurrentOwnerHash.ToString("X8")},
                {"projectedOwnerHash","0x"+r.ProjectedOwnerHash.ToString("X8")},{"afterOwnerHash","0x"+r.AfterOwnerHash.ToString("X8")},
                {"graphDirtyBefore",r.GraphDirtyBefore},{"graphDirtyProjected",r.GraphDirtyProjected},{"graphDirtyAfter",r.GraphDirtyAfter},
                {"controllerDirtyBefore",r.ControllerDirtyBefore},{"controllerDirtyProjected",r.ControllerDirtyProjected},
                {"controllerDirtyExpectedAfterEnd",r.ControllerDirtyAfterEnd},
                {"controllerDirtyAfter",r.ControllerDirtyAfter},
                {"failureLayer",r.FailureLayer==UInt32.MaxValue?-1:(long)r.FailureLayer},
                {"failureStateMachine",r.FailureStateMachine==UInt32.MaxValue?-1:(long)r.FailureStateMachine},
                {"failureRecord",r.FailureRecord==UInt32.MaxValue?-1:(long)r.FailureRecord},
                {"failureByteOffset",r.FailureByteOffset==UInt32.MaxValue?-1:(long)r.FailureByteOffset},
                {"expectedWord","0x"+r.ExpectedWord.ToString("X8")},{"actualWord","0x"+r.ActualWord.ToString("X8")}};
        }

        private object ProbeOverrideClipPlayables(Dictionary<string,object> args)
        {
            if(args.Count!=4||!args.ContainsKey("nativePath")||!args.ContainsKey("sha256")||
               !args.ContainsKey("animatorPath")||!args.ContainsKey("acknowledgeSacrificialProcess")||
               !Convert.ToBoolean(args["acknowledgeSacrificialProcess"]))
                throw new ArgumentException("probe-override-clip-playables requires exactly nativePath, sha256, animatorPath, and acknowledgeSacrificialProcess:true.");
            string path=Path.GetFullPath(Convert.ToString(args["nativePath"]));
            string expectedHash=Convert.ToString(args["sha256"]).ToUpperInvariant();
            string requestedPath=Convert.ToString(args["animatorPath"]);
            if(!File.Exists(path))throw new FileNotFoundException("Native Animator probe helper is missing.",path);
            string actualHash=FileHash(path);
            if(actualHash!=expectedHash)throw new InvalidOperationException("Native Animator probe helper hash mismatch: "+actualHash);
            if(IntPtr.Size!=4)throw new InvalidOperationException("Native Animator probe requires the x86 player.");

            object checkpoint=ActiveCheckpoint();
            Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained failed resume boundary for the sacrificial probe.");
            Array states=(Array)Field(ready.GetType(),"Animators").GetValue(ready);
            object selected=null;
            foreach(object state in states)
            {
                string candidate=Convert.ToString(Field(state.GetType(),"Path").GetValue(state));
                if(candidate==requestedPath)
                {
                    if(selected!=null)throw new InvalidOperationException("Animator path is not unique: "+requestedPath);
                    selected=state;
                }
            }
            if(selected==null)throw new InvalidOperationException("Retained Animator path was not found: "+requestedPath);
            UnityEngine.Animator animator=(UnityEngine.Animator)Field(selected.GetType(),"Animator").GetValue(selected);
            int expectedInstanceId=Convert.ToInt32(Field(selected.GetType(),"InstanceId").GetValue(selected));
            if(animator==null||animator.GetInstanceID()!=expectedInstanceId)
                throw new InvalidOperationException("Animator incarnation changed before sacrificial probe: "+requestedPath);

            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(String.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            uint unityBase=unchecked((uint)unity.BaseAddress.ToInt32());
            FieldInfo cachedPtr=Field(typeof(UnityEngine.Object),"m_CachedPtr");
            IntPtr animatorPointer=(IntPtr)cachedPtr.GetValue(animator);
            if(animatorPointer==IntPtr.Zero)throw new InvalidOperationException("Animator native pointer is null: "+requestedPath);

            IntPtr library=LoadLibrary(path);
            if(library==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            try
            {
                NativeApiVersion version=NativeExport<NativeApiVersion>(library,"oc2_animator_checkpoint_api_version");
                NativeOverrideClipProbe probe=NativeExport<NativeOverrideClipProbe>(library,"oc2_animator_override_clip_playables_probe");
                if(version()!=16)throw new InvalidOperationException("Native Animator probe API version mismatch.");
                int receiptSize=Marshal.SizeOf(typeof(NativeOverrideClipReceipt));
                if(receiptSize!=88)throw new InvalidOperationException("Managed OverrideClip receipt layout differs: "+receiptSize);
                NativeOverrideClipReceipt receipt=new NativeOverrideClipReceipt();
                int nativeOk=probe(new UIntPtr(unityBase),new UIntPtr(unchecked((uint)animatorPointer.ToInt32())),ref receipt);
                return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                    {"operation","probe-override-clip-playables"},{"ok",nativeOk==1&&receipt.Result==1},
                    {"processSacrificial",true},{"animatorPath",requestedPath},{"animatorInstanceId",expectedInstanceId},
                    {"nativePath",path},{"nativeSha256",actualHash},{"nativeApiVersion",version()},
                    {"receipt",new Dictionary<string,object>{{"apiVersion",receipt.ApiVersion},{"structSize",receipt.StructSize},
                        {"result",receipt.Result},{"lastError","0x"+receipt.LastError.ToString("X8")},{"stage",receipt.Stage},
                        {"unityBase",Pointer(receipt.UnityBase)},{"animator",Pointer(receipt.Animator)},
                        {"controller",Pointer(receipt.Controller)},{"controllerMemory",Pointer(receipt.ControllerMemory)},
                        {"graph",Pointer(receipt.Graph)},{"memorySizeBefore",receipt.MemorySizeBefore},
                        {"memorySizeAfter",receipt.MemorySizeAfter},{"memoryHashBefore","0x"+receipt.MemoryHashBefore.ToString("X8")},
                        {"memoryHashAfter","0x"+receipt.MemoryHashAfter.ToString("X8")},{"ownerCountBefore",receipt.OwnerCountBefore},
                        {"ownerCountAfter",receipt.OwnerCountAfter},{"ownerHashBefore","0x"+receipt.OwnerHashBefore.ToString("X8")},
                        {"ownerHashAfter","0x"+receipt.OwnerHashAfter.ToString("X8")},{"graphDirtyBefore",receipt.GraphDirtyBefore},
                        {"graphDirtyAfter",receipt.GraphDirtyAfter},{"controllerDirty9093Before","0x"+receipt.ControllerDirty9093Before.ToString("X8")},
                        {"controllerDirty9093After","0x"+receipt.ControllerDirty9093After.ToString("X8")}}},
                    {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                    {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                    {"scope","This call invoked Unity's original AnimatorControllerPlayable::OverrideClipPlayables on exactly one retained Animator. The current process is diagnostic-only after stage 5 regardless of result; reload before any rewind-parity claim."}};
            }
            finally{FreeLibrary(library);}
        }

        private object DumpResumeReadyOwnerState()
        {
            object checkpoint=ActiveCheckpoint();
            Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained pending resume-ready boundary.");
            MethodInfo captureOwner=Method(moduleType,"CaptureOwnerGraph");
            Array animators=(Array)Field(ready.GetType(),"Animators").GetValue(ready);
            var rows=new List<object>();
            foreach(object expectedState in animators)
            {
                string path=Convert.ToString(Field(expectedState.GetType(),"Path").GetValue(expectedState));
                UnityEngine.Animator animator=(UnityEngine.Animator)Field(expectedState.GetType(),"Animator").GetValue(expectedState);
                object expectedOwner=Field(expectedState.GetType(),"OwnerGraph").GetValue(expectedState);
                object actualOwner=Invoke(captureOwner,checkpoint,new object[]{animator});
                byte[] expectedBytes=StateBytes(expectedOwner),actualBytes=StateBytes(actualOwner);
                rows.Add(new Dictionary<string,object>{{"path",path},
                    {"expected",DescribeOwnerState(expectedOwner,expectedBytes)},
                    {"actual",DescribeOwnerState(actualOwner,actualBytes)},
                    {"firstDifference",FirstDifference(expectedBytes,actualBytes)}});
            }
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                {"operation","dump-resume-ready-owner-state"},{"pendingResumeRestore",pending},
                {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                {"animators",rows.ToArray()},
                {"scope","Read-only decode of the retained resume-ready owner graph plus a fresh native owner capture; no Animator, graph, Transform, or physics state is written."}};
        }

        private static object DescribeOwnerState(object state,byte[] bytes)
        {
            int recordSize=Marshal.SizeOf(typeof(NativeOwnerGraphRecord));
            if(recordSize!=212||bytes.Length%recordSize!=0)
                throw new InvalidOperationException("Owner-graph byte array does not contain whole 212-byte records.");
            object[] records=new object[bytes.Length/recordSize];
            GCHandle pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
            try
            {
                IntPtr start=pin.AddrOfPinnedObject();
                for(int index=0;index<records.Length;++index)
                    records[index]=DescribeOwnerRecord(index,(NativeOwnerGraphRecord)Marshal.PtrToStructure(
                        new IntPtr(start.ToInt64()+(long)index*recordSize),typeof(NativeOwnerGraphRecord)));
            }
            finally{pin.Free();}
            return new Dictionary<string,object>{{"recordCount",records.Length},{"byteSize",bytes.Length},
                {"hash",Hash(bytes)},{"stateRecordCount",Field(state.GetType(),"RecordCount").GetValue(state)},
                {"graphDirty58",Field(state.GetType(),"GraphDirty58").GetValue(state)},{"records",records}};
        }

        private object DumpResumeReadyNativeState()
        {
            object checkpoint=ActiveCheckpoint();
            Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            object boundary=Field(moduleType,"resumeFrame").GetValue(checkpoint);
            bool pending=Convert.ToBoolean(Field(moduleType,"pendingResumeRestore").GetValue(checkpoint));
            if(!pending||ready==null||boundary==null)
                throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained pending resume-ready boundary.");
            MethodInfo captureInput=Method(moduleType,"CaptureControllerInput");
            MethodInfo captureMemory=Method(moduleType,"CaptureControllerMemory");
            MethodInfo captureMixer=Method(moduleType,"CaptureMixerGraph");
            Array animators=(Array)Field(ready.GetType(),"Animators").GetValue(ready);
            var rows=new List<object>();
            foreach(object expectedState in animators)
            {
                string path=Convert.ToString(Field(expectedState.GetType(),"Path").GetValue(expectedState));
                UnityEngine.Animator animator=(UnityEngine.Animator)Field(expectedState.GetType(),"Animator").GetValue(expectedState);
                object expectedInput=Field(expectedState.GetType(),"ControllerInput").GetValue(expectedState);
                object expectedMixer=Field(expectedState.GetType(),"MixerGraph").GetValue(expectedState);
                byte[] expectedMemory=(byte[])Field(expectedState.GetType(),"ControllerMemory").GetValue(expectedState);
                object actualInput=Invoke(captureInput,checkpoint,new object[]{animator});
                byte[] actualMemory=(byte[])Invoke(captureMemory,checkpoint,new object[]{animator});
                object actualMixer=Invoke(captureMixer,checkpoint,new object[]{animator});
                byte[] expectedInputBytes=StateBytes(expectedInput),actualInputBytes=StateBytes(actualInput);
                byte[] expectedMixerBytes=StateBytes(expectedMixer),actualMixerBytes=StateBytes(actualMixer);
                uint inputRecordCount=Convert.ToUInt32(Field(expectedInput.GetType(),"RecordCount").GetValue(expectedInput));
                rows.Add(new Dictionary<string,object>{{"path",path},
                    {"controllerMemory",new Dictionary<string,object>{
                        {"expectedHash",Hash(expectedMemory)},{"actualHash",Hash(actualMemory)},
                        {"firstDifference",FirstDifference(expectedMemory,actualMemory)},
                        {"expectedLayers",DescribeControllerLayers(expectedMemory,inputRecordCount)},
                        {"actualLayers",DescribeControllerLayers(actualMemory,inputRecordCount)}}},
                    {"controllerInput",new Dictionary<string,object>{
                        {"expectedHash",Hash(expectedInputBytes)},{"actualHash",Hash(actualInputBytes)},
                        {"firstDifference",FirstDifference(expectedInputBytes,actualInputBytes)},
                        {"expectedPrefix",HexSlice(expectedInputBytes,0,12)},{"actualPrefix",HexSlice(actualInputBytes,0,12)},
                        {"expectedRecords",DescribeControllerInputRecords(expectedInputBytes,inputRecordCount)},
                        {"actualRecords",DescribeControllerInputRecords(actualInputBytes,inputRecordCount)}}},
                    {"mixerGraph",new Dictionary<string,object>{
                        {"expectedHash",Hash(expectedMixerBytes)},{"actualHash",Hash(actualMixerBytes)},
                        {"firstDifference",FirstDifference(expectedMixerBytes,actualMixerBytes)},
                        {"expectedBytes",HexSlice(expectedMixerBytes,0,expectedMixerBytes.Length)},
                        {"actualBytes",HexSlice(actualMixerBytes,0,actualMixerBytes.Length)}}}});
            }
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                {"operation","dump-resume-ready-native-state"},{"pendingResumeRestore",pending},
                {"boundaryFrame",Convert.ToInt32(Field(boundary.GetType(),"Frame").GetValue(boundary))},
                {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                {"animators",rows.ToArray()},
                {"scope","Read-only reflection over the retained resume-ready tuple plus fresh native captures; no Animator, graph, Transform, or physics state is written."}};
        }

        private object DumpLiveOwnerGraph(Dictionary<string,object> args)
        {
            if(args.Count!=2||!args.ContainsKey("nativePath")||!args.ContainsKey("sha256"))
                throw new ArgumentException("dump-live-owner-graph requires exactly nativePath and sha256.");
            string path=Path.GetFullPath(Convert.ToString(args["nativePath"]));
            string expectedHash=Convert.ToString(args["sha256"]).ToUpperInvariant();
            if(!File.Exists(path))throw new FileNotFoundException("Native owner-graph observer is missing.",path);
            string actualHash=FileHash(path);
            if(actualHash!=expectedHash)throw new InvalidOperationException("Native owner-graph observer hash mismatch: "+actualHash);
            if(IntPtr.Size!=4)throw new InvalidOperationException("Native owner-graph observation requires the x86 player.");
            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(String.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            uint unityBase=unchecked((uint)unity.BaseAddress.ToInt32());
            object checkpoint=ActiveCheckpoint();
            Type moduleType=checkpoint.GetType();
            object ready=Field(moduleType,"resumeReadyFrame").GetValue(checkpoint);
            if(ready==null)throw new InvalidOperationException("ChefAnimatorCheckpoint has no retained resume-ready frame.");
            Array animators=(Array)Field(ready.GetType(),"Animators").GetValue(ready);
            FieldInfo cachedPtr=Field(typeof(UnityEngine.Object),"m_CachedPtr");
            IntPtr library=LoadLibrary(path);
            if(library==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error());
            try
            {
                NativeApiVersion version=NativeExport<NativeApiVersion>(library,"oc2_animator_checkpoint_api_version");
                NativeOwnerGraphCapture capture=NativeExport<NativeOwnerGraphCapture>(library,"oc2_animator_owner_graph_capture");
                if(version()!=16)throw new InvalidOperationException("Native owner-graph API version mismatch.");
                int recordSize=Marshal.SizeOf(typeof(NativeOwnerGraphRecord));
                if(recordSize!=212)throw new InvalidOperationException("Managed owner-graph record layout differs: "+recordSize);
                var rows=new List<object>();
                foreach(object expectedState in animators)
                {
                    string animatorPath=Convert.ToString(Field(expectedState.GetType(),"Path").GetValue(expectedState));
                    UnityEngine.Animator animator=(UnityEngine.Animator)Field(expectedState.GetType(),"Animator").GetValue(expectedState);
                    IntPtr animatorPointer=(IntPtr)cachedPtr.GetValue(animator);
                    if(animatorPointer==IntPtr.Zero)throw new InvalidOperationException("Animator native pointer is null: "+animatorPath);
                    NativeOwnerGraphReceipt sizeReceipt=new NativeOwnerGraphReceipt();
                    int sizeResult=capture(new UIntPtr(unityBase),new UIntPtr(unchecked((uint)animatorPointer.ToInt32())),
                        IntPtr.Zero,0,ref sizeReceipt);
                    if(sizeResult!=0||sizeReceipt.Result!=6||sizeReceipt.RecordCount==0||
                       sizeReceipt.ByteSize!=checked(sizeReceipt.RecordCount*(uint)recordSize))
                        throw new InvalidOperationException("Owner-graph size query failed for "+animatorPath+
                            ": result="+sizeReceipt.Result+", error=0x"+sizeReceipt.LastError.ToString("X8")+
                            ", failureRecord="+sizeReceipt.FailureRecord+".");
                    IntPtr buffer=Marshal.AllocHGlobal(checked((int)sizeReceipt.ByteSize));
                    try
                    {
                        NativeOwnerGraphReceipt receipt=new NativeOwnerGraphReceipt();
                        int ok=capture(new UIntPtr(unityBase),new UIntPtr(unchecked((uint)animatorPointer.ToInt32())),
                            buffer,sizeReceipt.ByteSize,ref receipt);
                        if(ok==0||receipt.Result!=1||receipt.ByteSize!=sizeReceipt.ByteSize||
                           receipt.RecordCount!=sizeReceipt.RecordCount)
                            throw new InvalidOperationException("Owner-graph capture failed for "+animatorPath+
                                ": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+
                                ", failureRecord="+receipt.FailureRecord+".");
                        object[] records=new object[checked((int)receipt.RecordCount)];
                        for(int index=0;index<records.Length;++index)
                        {
                            NativeOwnerGraphRecord record=(NativeOwnerGraphRecord)Marshal.PtrToStructure(
                                new IntPtr(buffer.ToInt64()+(long)index*recordSize),typeof(NativeOwnerGraphRecord));
                            records[index]=DescribeOwnerRecord(index,record);
                        }
                        rows.Add(new Dictionary<string,object>{{"path",animatorPath},{"animator",Pointer(new UIntPtr(unchecked((uint)animatorPointer.ToInt32())))},
                            {"controller",Pointer(receipt.Controller)},{"controllerConstant",Pointer(receipt.ControllerConstant)},
                            {"descriptors",Pointer(receipt.Descriptors)},{"graph",Pointer(receipt.Graph)},
                            {"graphDirty58",receipt.GraphDirty58},{"layerCount",receipt.LayerCount},
                            {"recordCount",receipt.RecordCount},{"byteSize",receipt.ByteSize},
                            {"hash","0x"+receipt.Hash.ToString("X8")},{"records",records}});
                    }
                    finally{Marshal.FreeHGlobal(buffer);}
                }
                return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},
                    {"operation","dump-live-owner-graph"},{"nativePath",path},{"nativeSha256",actualHash},
                    {"nativeApiVersion",version()},{"nativeRecordSize",recordSize},{"unityPlayerBase","0x"+unityBase.ToString("X8")},
                    {"resumeReadyFrame",Convert.ToInt32(Field(ready.GetType(),"Frame").GetValue(ready))},
                    {"animators",rows.ToArray()},
                    {"scope","Paused, read-only traversal of the live native Animator Playable ownership graph, including reciprocal connection records, clip pointers, and dirty fields. No Animator, graph, Transform, or physics state is written."}};
            }
            finally{FreeLibrary(library);}
        }

        private static object DescribeOwnerRecord(int index,NativeOwnerGraphRecord value)
        {
            return new Dictionary<string,object>{{"index",index},{"kind",OwnerKind(value.RecordKind)},{"kindId",value.RecordKind},
                {"layer",value.LayerIndex},{"stateMachine",value.StateMachineIndex},{"branch",value.BranchIndex},
                {"input",value.InputIndex},{"self",Pointer(value.Self)},{"vtable",Pointer(value.Vtable)},
                {"internal",Pointer(value.Internal)},{"graph",Pointer(value.Graph)},
                {"inputEntries",Pointer(value.InputEntries)},{"inputCount",value.InputCount},{"inputCapacityRaw",value.InputCapacityRaw},
                {"outputEntries",Pointer(value.OutputEntries)},{"outputCount",value.OutputCount},{"outputCapacityRaw",value.OutputCapacityRaw},
                {"flags7C","0x"+value.Flags7C.ToString("X8")},{"dirty9093","0x"+value.Dirty9093.ToString("X8")},
                {"rawA0A3","0x"+value.RawA0A3.ToString("X8")},{"rawA4A7","0x"+value.RawA4A7.ToString("X8")},
                {"word50","0x"+value.Word50.ToString("X8")},{"clip108",Pointer(value.Clip108)},
                {"entryAddress",Pointer(value.EntryAddress)},{"entryWeightBits","0x"+value.EntryWeightBits.ToString("X8")},
                {"entryPlayable",Pointer(value.EntryPlayable)},{"entryPortRaw","0x"+value.EntryPortRaw.ToString("X8")},
                {"resolverOrigin",Pointer(value.ResolverOrigin)},{"resolverResult",Pointer(value.ResolverResult)},
                {"resolverDepth",value.ResolverDepth},{"resolverStatus",value.ResolverStatus},
                {"currentTime28Bits","0x"+value.CurrentTime28High.ToString("X8")+value.CurrentTime28Low.ToString("X8")},
                {"previousTime30Bits","0x"+value.PreviousTime30High.ToString("X8")+value.PreviousTime30Low.ToString("X8")},
                {"duration38Bits","0x"+value.Duration38High.ToString("X8")+value.Duration38Low.ToString("X8")},
                {"mode80","0x"+value.Mode80.ToString("X8")},{"raw9497","0x"+value.Raw9497.ToString("X8")},
                {"bindingA8",Pointer(value.BindingA8)},{"bindingAC",Pointer(value.BindingAC)},
                {"bindingB0",Pointer(value.BindingB0)},{"bindingB4",Pointer(value.BindingB4)},
                {"clipCacheBC","0x"+value.ClipCacheBC.ToString("X8")},
                {"clipCacheC0","0x"+value.ClipCacheC0.ToString("X8")},
                {"clipInternalWeightBits","0x"+value.ClipInternalWeightBits.ToString("X8")},
                {"clipFlags10C","0x"+value.ClipFlags10C.ToString("X8")},
                {"bindingCachePresence","0x"+value.BindingCachePresence.ToString("X8")},
                {"bindingACInner",Pointer(value.BindingACInner)},{"bindingACCount",value.BindingACCount},
                {"bindingACHash",value.BindingACHash.ToString("X8")},
                {"bindingB0Inner",Pointer(value.BindingB0Inner)},{"bindingB0Count",value.BindingB0Count},
                {"bindingB0Hash",value.BindingB0Hash.ToString("X8")},{"bindingB4Hash",value.BindingB4Hash.ToString("X8")}};
        }

        private static string OwnerKind(uint kind)
        {
            switch(kind)
            {
                case 1:return "outer-node";case 2:return "outer-input";case 3:return "outer-output";
                case 10:return "branch-node";case 11:return "branch-input";case 12:return "branch-output";
                case 20:return "child-node";case 21:return "child-input";case 22:return "child-output";
                case 30:return "resolver-node";case 31:return "resolver-input";case 32:return "resolver-output";
                case 33:return "resolver-decision";case 34:return "resolver-terminal";default:return "unknown";
            }
        }

        private static byte[] StateBytes(object state)
        {
            if(state==null)throw new InvalidOperationException("Retained Animator native state is missing.");
            byte[] bytes=(byte[])Field(state.GetType(),"Bytes").GetValue(state);
            if(bytes==null)throw new InvalidOperationException("Retained Animator native byte array is missing.");
            return bytes;
        }

        private static object[] DescribeControllerLayers(byte[] memory,uint expectedCount)
        {
            if(memory==null||memory.Length<8)throw new InvalidOperationException("ControllerMemory is too small for its root table.");
            uint count=BitConverter.ToUInt32(memory,0);
            if(count!=expectedCount)throw new InvalidOperationException("ControllerMemory layer count does not match ControllerInput record count.");
            long entries=4L+BitConverter.ToInt32(memory,4);
            if(entries<0||entries+4L*count>memory.Length)throw new InvalidOperationException("ControllerMemory layer table is out of bounds.");
            object[] rows=new object[count];
            for(uint layer=0;layer<count;layer++)
            {
                int entry=checked((int)(entries+4L*layer));
                long state=entry+(long)BitConverter.ToInt32(memory,entry);
                if(state<0||state+0x6C>memory.Length)throw new InvalidOperationException("ControllerMemory layer state is out of bounds.");
                int offset=checked((int)state);
                rows[layer]=new Dictionary<string,object>{{"layer",layer},{"blobOffset",offset},
                    {"pendingGotoState",memory[offset+0x6B]!=0},{"pendingGotoStateRaw",memory[offset+0x6B]},
                    {"currentState",BitConverter.ToInt32(memory,offset+0x08)},
                    {"nextState",BitConverter.ToInt32(memory,offset+0x0C)},
                    {"inDynamicTransitionRaw",memory[offset+0x6A]}};
            }
            return rows;
        }

        private static object[] DescribeControllerInputRecords(byte[] bytes,uint count)
        {
            if(bytes==null||bytes.Length!=checked(12+(int)count*24))
                throw new InvalidOperationException("ControllerInput byte size does not match its record count.");
            object[] rows=new object[count];
            for(uint layer=0;layer<count;layer++)
            {
                int offset=checked(12+(int)layer*24);
                rows[layer]=new Dictionary<string,object>{{"layer",layer},{"hex",HexSlice(bytes,offset,24)},
                    {"word0","0x"+BitConverter.ToUInt32(bytes,offset).ToString("X8")},
                    {"normalizedTimeOffsetBits","0x"+BitConverter.ToUInt32(bytes,offset+4).ToString("X8")},
                    {"fixedTimeOffsetBits","0x"+BitConverter.ToUInt32(bytes,offset+8).ToString("X8")},
                    {"wordC","0x"+BitConverter.ToUInt32(bytes,offset+12).ToString("X8")},
                    {"word10","0x"+BitConverter.ToUInt32(bytes,offset+16).ToString("X8")},
                    {"word14","0x"+BitConverter.ToUInt32(bytes,offset+20).ToString("X8")},
                    {"fixedTimeRaw",bytes[offset+20]}};
            }
            return rows;
        }

        private static object FirstDifference(byte[] expected,byte[] actual)
        {
            int common=Math.Min(expected.Length,actual.Length);
            for(int offset=0;offset<common;offset++)if(expected[offset]!=actual[offset])
                return new Dictionary<string,object>{{"offset",offset},{"expected",expected[offset]},{"actual",actual[offset]}};
            if(expected.Length!=actual.Length)return new Dictionary<string,object>{{"offset",common},
                {"expectedLength",expected.Length},{"actualLength",actual.Length}};
            return null;
        }

        private object RecorderStatus()
        {
            object active=ActiveCheckpoint();
            Type moduleType=active.GetType();
            object history=Field(moduleType,"history").GetValue(active);
            IEnumerable entries=history as IEnumerable;
            if(entries==null)throw new InvalidOperationException("ChefAnimatorCheckpoint history is unavailable.");
            object latest=null;
            foreach(object entry in entries)
            {
                object value=entry.GetType().GetProperty("Value").GetValue(entry,null);
                latest=value;
            }
            if(latest==null)throw new InvalidOperationException("ChefAnimatorCheckpoint history is empty.");
            Array animators=(Array)Field(latest.GetType(),"Animators").GetValue(latest);
            var rows=new List<object>();
            foreach(object state in animators)
            {
                UnityEngine.Animator animator=(UnityEngine.Animator)Field(state.GetType(),"Animator").GetValue(state);
                rows.Add(new Dictionary<string,object>{{"path",Convert.ToString(Field(state.GetType(),"Path").GetValue(state))},
                    {"recorderMode",animator.recorderMode.ToString()},{"playbackTime",animator.playbackTime},
                    {"recorderStartTime",animator.recorderStartTime},{"recorderStopTime",animator.recorderStopTime}});
            }
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},{"operation","recorder-status"},
                {"animators",rows.ToArray()},
                {"scope","Read-only public Animator recorder properties for the live chef Animators retained by ChefAnimatorCheckpoint."}};
        }

        private static object ActiveCheckpoint()
        {
            object active=null;
            foreach(Assembly candidate in AppDomain.CurrentDomain.GetAssemblies().Where(value=>
                value.GetName().Name.StartsWith("ChefAnimatorCheckpoint.r",StringComparison.Ordinal)))
            {
                Type candidateType=candidate.GetType("SuperchargedPatch.Authoring.Modules.ChefAnimatorCheckpointModule",false);
                if(candidateType==null)continue;
                object candidateActive=Field(candidateType,"active").GetValue(null);
                if(candidateActive==null)continue;
                if(active!=null)throw new InvalidOperationException("Multiple ChefAnimatorCheckpoint revisions report active instances.");
                active=candidateActive;
            }
            if(active==null)throw new InvalidOperationException("ChefAnimatorCheckpoint has no active instance.");
            return active;
        }

        private object DumpHistoryFrame(int frame)
        {
            object active=ActiveCheckpoint();
            Type moduleType=active.GetType();
            object history=Field(moduleType,"history").GetValue(active);
            object retained=DictionaryItem(history,frame);
            if(retained==null)throw new InvalidOperationException("Requested frame is not retained in ChefAnimatorCheckpoint history: "+frame+".");
            int retainedFrame=Convert.ToInt32(Field(retained.GetType(),"Frame").GetValue(retained));
            if(retainedFrame!=frame)throw new InvalidOperationException("Retained ChefAnimatorCheckpoint history key/frame mismatch: requested "+frame+", snapshot "+retainedFrame+".");
            Array animators=(Array)Field(retained.GetType(),"Animators").GetValue(retained);
            if(animators==null)throw new InvalidOperationException("Retained ChefAnimatorCheckpoint frame has no Animator array: "+frame+".");
            MethodInfo describe=moduleType.GetMethods(BindingFlags.Static|BindingFlags.NonPublic).SingleOrDefault(value=>
                value.Name=="DescribeResumePrefix"&&value.GetParameters().Length==1&&
                value.GetParameters()[0].ParameterType==animators.GetType());
            if(describe==null)throw new MissingMethodException(moduleType.FullName,"DescribeResumePrefix("+animators.GetType().FullName+")");
            object described=Invoke(describe,null,new object[]{animators});
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},{"operation","dump-history-frame"},
                {"frame",frame},{"checkpointAssembly",moduleType.Assembly.GetName().Name},{"animators",described},
                {"scope","Read-only description of one exact retained ChefAnimatorCheckpoint history frame through that module's private static DescribeResumePrefix descriptor. This operation does not consult replayReference, capture live Animator state, or mutate the retained snapshot or runtime."}};
        }

        private object DumpMovementFrame(int frame)
        {
            Assembly assembly=null;Type moduleType=null;object active=null;
            foreach(Assembly candidate in AppDomain.CurrentDomain.GetAssemblies().Where(value=>
                value.GetName().Name.StartsWith("ChefMovementHistoryCheckpoint.r",StringComparison.Ordinal)))
            {
                Type candidateType=candidate.GetType("SuperchargedPatch.Authoring.Modules.ChefMovementHistoryCheckpointModule",false);
                if(candidateType==null)continue;
                object candidateActive=Field(candidateType,"active").GetValue(null);
                if(candidateActive==null)continue;
                if(active!=null)throw new InvalidOperationException("Multiple ChefMovementHistoryCheckpoint revisions report active instances.");
                assembly=candidate;moduleType=candidateType;active=candidateActive;
            }
            if(active==null)throw new InvalidOperationException("ChefMovementHistoryCheckpoint has no active instance.");
            object replayReference=Field(moduleType,"replayReference").GetValue(active);
            object history=Field(moduleType,"history").GetValue(active);
            int warpTarget=Convert.ToInt32(Field(moduleType,"warpTarget").GetValue(active));
            object expected=OriginalFrame(history,replayReference,warpTarget,frame);
            object actual=DictionaryItem(history,frame);
            if(expected==null||actual==null)
                throw new InvalidOperationException("Requested frame is not retained in both original and replay movement histories: "+frame+".");
            Array expectedChefs=(Array)Field(expected.GetType(),"Chefs").GetValue(expected);
            Array actualChefs=(Array)Field(actual.GetType(),"Chefs").GetValue(actual);
            if(expectedChefs.Length!=actualChefs.Length)throw new InvalidOperationException("Movement-history chef membership count differs.");
            var rows=new List<object>();
            for(int index=0;index<expectedChefs.Length;index++)
            {
                object left=expectedChefs.GetValue(index),right=actualChefs.GetValue(index);
                string path=Convert.ToString(Field(left.GetType(),"Path").GetValue(left));
                UnityEngine.Vector3 expectedPrevious=(UnityEngine.Vector3)Field(left.GetType(),"PreviousPosition").GetValue(left);
                UnityEngine.Vector3 actualPrevious=(UnityEngine.Vector3)Field(right.GetType(),"PreviousPosition").GetValue(right);
                UnityEngine.Vector3 expectedVelocity=(UnityEngine.Vector3)Field(left.GetType(),"LocalVelocity").GetValue(left);
                UnityEngine.Vector3 actualVelocity=(UnityEngine.Vector3)Field(right.GetType(),"LocalVelocity").GetValue(right);
                float expectedSpeed=Convert.ToSingle(Field(left.GetType(),"XzSpeed").GetValue(left));
                float actualSpeed=Convert.ToSingle(Field(right.GetType(),"XzSpeed").GetValue(right));
                var alignment=new List<object>();
                for(int candidate=Math.Max(0,frame-6);candidate<=frame+6;candidate++)
                {
                    object candidateFrame=OriginalFrame(history,replayReference,warpTarget,candidate);
                    if(candidateFrame==null)continue;
                    Array candidateChefs=(Array)Field(candidateFrame.GetType(),"Chefs").GetValue(candidateFrame);
                    object candidateChef=candidateChefs.GetValue(index);
                    UnityEngine.Vector3 candidatePrevious=(UnityEngine.Vector3)Field(candidateChef.GetType(),"PreviousPosition").GetValue(candidateChef);
                    UnityEngine.Vector3 candidateVelocity=(UnityEngine.Vector3)Field(candidateChef.GetType(),"LocalVelocity").GetValue(candidateChef);
                    float candidateSpeed=Convert.ToSingle(Field(candidateChef.GetType(),"XzSpeed").GetValue(candidateChef));
                    alignment.Add(new Dictionary<string,object>{{"originalFrame",candidate},
                        {"previousPosition",PointWithBits(candidatePrevious)},{"localVelocity",PointWithBits(candidateVelocity)},
                        {"xzSpeed",FloatWithBits(candidateSpeed)}});
                }
                rows.Add(new Dictionary<string,object>{{"path",path},
                    {"expectedControlsId",Field(left.GetType(),"ControlsId").GetValue(left)},
                    {"actualControlsId",Field(right.GetType(),"ControlsId").GetValue(right)},
                    {"expectedPreviousPosition",PointWithBits(expectedPrevious)},{"actualPreviousPosition",PointWithBits(actualPrevious)},
                    {"expectedLocalVelocity",PointWithBits(expectedVelocity)},{"actualLocalVelocity",PointWithBits(actualVelocity)},
                    {"expectedXzSpeed",FloatWithBits(expectedSpeed)},{"actualXzSpeed",FloatWithBits(actualSpeed)},
                    {"expectedRunSpeed",FloatWithBits(Convert.ToSingle(Field(left.GetType(),"RunSpeed").GetValue(left)))},
                    {"actualRunSpeed",FloatWithBits(Convert.ToSingle(Field(right.GetType(),"RunSpeed").GetValue(right)))},
                    {"alignment",alignment.ToArray()}});
            }
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},{"operation","dump-movement-frame"},
                {"frame",frame},{"warpTarget",warpTarget},{"checkpointAssembly",assembly.GetName().Name},{"chefs",rows.ToArray()},
                {"scope","Read-only exact float-bit comparison of retained original and replay PlayerControls movement-history snapshots."}};
        }

        private static object PointWithBits(UnityEngine.Vector3 value)
        {
            return new Dictionary<string,object>{{"value",new[]{value.x,value.y,value.z}},
                {"bits",new[]{FloatBits(value.x),FloatBits(value.y),FloatBits(value.z)}}};
        }

        private static object FloatWithBits(float value)
        {
            return new Dictionary<string,object>{{"value",value},{"bits",FloatBits(value)}};
        }

        private static string FloatBits(float value)
        {
            return "0x"+BitConverter.ToUInt32(BitConverter.GetBytes(value),0).ToString("X8");
        }

        private object DumpFrame(int frame)
        {
            Assembly assembly=null;Type moduleType=null;object active=null;
            foreach(Assembly candidate in AppDomain.CurrentDomain.GetAssemblies().Where(value=>
                value.GetName().Name.StartsWith("ChefAnimatorCheckpoint.r",StringComparison.Ordinal)))
            {
                Type candidateType=candidate.GetType("SuperchargedPatch.Authoring.Modules.ChefAnimatorCheckpointModule",false);
                if(candidateType==null)continue;
                object candidateActive=Field(candidateType,"active").GetValue(null);
                if(candidateActive==null)continue;
                if(active!=null)throw new InvalidOperationException("Multiple ChefAnimatorCheckpoint revisions report active instances.");
                assembly=candidate;moduleType=candidateType;active=candidateActive;
            }
            if(active==null)throw new InvalidOperationException("ChefAnimatorCheckpoint has no active instance.");
            object replayReference=Field(moduleType,"replayReference").GetValue(active);
            object history=Field(moduleType,"history").GetValue(active);
            int warpTarget=Convert.ToInt32(Field(moduleType,"warpTarget").GetValue(active));
            object expected=OriginalFrame(history,replayReference,warpTarget,frame);
            object actual=DictionaryItem(history,frame);
            if(expected==null||actual==null)throw new InvalidOperationException("Requested frame is not retained in both original and replay histories: "+frame+".");
            Array expectedAnimators=(Array)Field(expected.GetType(),"Animators").GetValue(expected);
            Array actualAnimators=(Array)Field(actual.GetType(),"Animators").GetValue(actual);
            if(expectedAnimators.Length!=actualAnimators.Length)throw new InvalidOperationException("Animator membership count differs.");
            var rows=new List<object>();
            for(int index=0;index<expectedAnimators.Length;index++)
            {
                object a=expectedAnimators.GetValue(index),b=actualAnimators.GetValue(index);
                string path=Convert.ToString(Field(a.GetType(),"Path").GetValue(a));
                UnityEngine.Animator liveAnimator=(UnityEngine.Animator)Field(b.GetType(),"Animator").GetValue(b);
                byte[] before=(byte[])Field(a.GetType(),"ControllerMemory").GetValue(a);
                byte[] after=(byte[])Field(b.GetType(),"ControllerMemory").GetValue(b);
                Dictionary<string,object> publicComparison=ComparePublicState(a,b);
                var differences=new List<object>();
                int count=Math.Min(before.Length,after.Length);
                for(int offset=0;offset<count;offset++)if(before[offset]!=after[offset])
                    differences.Add(new Dictionary<string,object>{{"offset",offset},{"expected",before[offset]},{"actual",after[offset]},
                        {"expectedContext",HexSlice(before,Math.Max(0,offset-16),33)},
                        {"actualContext",HexSlice(after,Math.Max(0,offset-16),33)}});
                var alignment=new List<object>();
                for(int candidate=Math.Max(0,frame-6);candidate<=frame+6;candidate++)
                {
                    object candidateFrame=OriginalFrame(history,replayReference,warpTarget,candidate);
                    if(candidateFrame==null)continue;
                    Array candidateAnimators=(Array)Field(candidateFrame.GetType(),"Animators").GetValue(candidateFrame);
                    byte[] candidateBytes=(byte[])Field(candidateAnimators.GetValue(index).GetType(),"ControllerMemory").GetValue(candidateAnimators.GetValue(index));
                    alignment.Add(new Dictionary<string,object>{{"originalFrame",candidate},{"hash",Hash(candidateBytes)},
                        {"differenceCount",DifferenceCount(candidateBytes,after)}});
                }
                rows.Add(new Dictionary<string,object>{{"path",path},{"expectedBytes",before.Length},{"actualBytes",after.Length},
                    {"expectedHash",Hash(before)},{"actualHash",Hash(after)},{"differenceCount",differences.Count+Math.Abs(before.Length-after.Length)},
                    {"differences",differences.ToArray()},{"expectedFirst128",HexSlice(before,0,128)},
                    {"actualFirst128",HexSlice(after,0,128)},{"alignment",alignment.ToArray()},
                    {"parameterNames",liveAnimator.parameters.Select(value=>(object)value.name).ToArray()},
                    {"publicDifferenceCount",publicComparison["count"]},{"firstPublicDifference",publicComparison["first"]},
                    {"publicDifferences",publicComparison["all"]}});
            }
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",ApiVersion},{"operation","dump-frame"},{"frame",frame},
                {"checkpointAssembly",assembly.GetName().Name},{"animators",rows.ToArray()},
                {"scope","Read-only comparison of controller blobs retained by the original and replay frame snapshots."}};
        }

        private static Dictionary<string,object> ComparePublicState(object expected,object actual)
        {
            int count=0;object first=null;var all=new List<object>();
            CompareFields(expected,actual,new[]{"Enabled","ApplyRootMotion","Speed","UpdateMode","CullingMode"},"animator",ref count,ref first,all);
            CompareArrays(Field(expected.GetType(),"Parameters").GetValue(expected) as Array,
                Field(actual.GetType(),"Parameters").GetValue(actual) as Array,
                new[]{"Hash","Type","Float","Int","Bool"},"parameter",ref count,ref first,all);
            CompareArrays(Field(expected.GetType(),"Layers").GetValue(expected) as Array,
                Field(actual.GetType(),"Layers").GetValue(actual) as Array,
                new[]{"FullPathHash","ShortNameHash","TagHash","NormalizedTime","Weight","Loop"},"layer",ref count,ref first,all);
            CompareArrays(Field(expected.GetType(),"Transforms").GetValue(expected) as Array,
                Field(actual.GetType(),"Transforms").GetValue(actual) as Array,
                new[]{"InstanceId","Path","LocalPosition","LocalRotation","LocalScale"},"transform",ref count,ref first,all);
            return new Dictionary<string,object>{{"count",count},{"first",first},{"all",all.ToArray()}};
        }

        private static void CompareArrays(Array expected,Array actual,string[] fields,string label,ref int count,ref object first,List<object> all)
        {
            if(expected==null||actual==null||expected.Length!=actual.Length)
            {
                count++;
                object difference=new Dictionary<string,object>{{"component",label+"Count"},
                    {"expected",expected==null?-1:expected.Length},{"actual",actual==null?-1:actual.Length}};
                all.Add(difference);if(first==null)first=difference;
                return;
            }
            for(int index=0;index<expected.Length;index++)
                CompareFields(expected.GetValue(index),actual.GetValue(index),fields,label+"["+index+"]",ref count,ref first,all);
        }

        private static void CompareFields(object expected,object actual,string[] fields,string label,ref int count,ref object first,List<object> all)
        {
            foreach(string name in fields)
            {
                object left=Field(expected.GetType(),name).GetValue(expected);
                object right=Field(actual.GetType(),name).GetValue(actual);
                if(Object.Equals(left,right))continue;
                count++;
                object difference=new Dictionary<string,object>{{"component",label+"."+name},
                    {"expected",Convert.ToString(left,System.Globalization.CultureInfo.InvariantCulture)},
                    {"actual",Convert.ToString(right,System.Globalization.CultureInfo.InvariantCulture)}};
                all.Add(difference);if(first==null)first=difference;
            }
        }

        private static object OriginalFrame(object history,object replayReference,int warpTarget,int frame)
        {
            return DictionaryItem(frame>warpTarget?replayReference:history,frame);
        }

        private static int DifferenceCount(byte[] left,byte[] right)
        {
            int count=Math.Abs(left.Length-right.Length),common=Math.Min(left.Length,right.Length);
            for(int index=0;index<common;index++)if(left[index]!=right[index])count++;
            return count;
        }

        private static object DictionaryItem(object dictionary,int key)
        {
            PropertyInfo item=dictionary.GetType().GetProperty("Item",BindingFlags.Instance|BindingFlags.Public);
            try{return item.GetValue(dictionary,new object[]{key});}
            catch(TargetInvocationException error)
            {
                if(error.InnerException is KeyNotFoundException)return null;
                throw;
            }
        }

        private static FieldInfo Field(Type type,string name)
        {
            FieldInfo field=type.GetField(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(field==null)throw new MissingFieldException(type.FullName,name);
            return field;
        }

        private static MethodInfo Method(Type type,string name)
        {
            MethodInfo method=type.GetMethod(name,BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic);
            if(method==null)throw new MissingMethodException(type.FullName,name);
            return method;
        }

        private static T NativeExport<T>(IntPtr library,string name) where T:class
        {
            IntPtr pointer=GetProcAddress(library,name);
            if(pointer==IntPtr.Zero)throw new MissingMethodException("Native export missing: "+name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer,typeof(T));
        }

        private static object Invoke(MethodInfo method,object target,object[] args)
        {
            try{return method.Invoke(target,args);}
            catch(TargetInvocationException error){throw error.InnerException??error;}
        }

        private static string HexSlice(byte[] bytes,int start,int maximum)
        {
            int count=Math.Min(maximum,Math.Max(0,bytes.Length-start));
            return BitConverter.ToString(bytes,start,count).Replace("-","");
        }

        private static string Hash(byte[] bytes)
        {
            uint hash=2166136261u;
            foreach(byte value in bytes){hash^=value;hash*=16777619u;}
            return hash.ToString("X8");
        }

        private static string Pointer(UIntPtr value){return "0x"+value.ToUInt32().ToString("X8");}

        private static string FileHash(string path)
        {
            using(SHA256 sha=SHA256.Create())using(Stream stream=File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");
        }

        private static void RequireFence()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Animator checkpoint inspection requires the authoring pause fence.");
        }

        public void Dispose(){disposed=true;}
    }
}
