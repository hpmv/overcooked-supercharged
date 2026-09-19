using System;
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
    // Authoring-only sidecar for visual chef Animator state. Ordinary forward
    // evaluation is untouched. A successful development rewind restores the
    // state-machine boundary captured for that exact output frame before the
    // replay is unpaused.
    public sealed class ChefAnimatorCheckpointModule : IAuthoringModule
    {
        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeControllerReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,MemoryBefore,MemoryAfter,Allocator;
            public uint BlobSizeBefore,BlobSizeAfter,HashBefore,HashAfter,FirstDifference,BeforeByte,AfterByte;
            public uint DifferenceCount,FirstDifferenceExceptFirstEvaluationFlag;
            public uint BeforeByteExceptFirstEvaluationFlag,AfterByteExceptFirstEvaluationFlag;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeControllerInputReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerConstant,ControllerInput,Records;
            public uint RecordCount,OuterCount,ByteSize,Hash;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeTransitionTopologyReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerMemory,ControllerGraphMemory,LiveLayers;
            public uint BlobCapacity,BlobLayerCount,GraphLayerCount,LiveLayerCount,ByteSize,Hash,TopologyMismatchCount;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeMixerGraphReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerConstant,Descriptors;
            public uint LayerCount,RecordCount,ByteSize,Hash,MismatchCount,RestoredWeightCount;
            public uint MismatchRecordIndex,MismatchByteOffset,ExpectedWord,ActualWord;
        }

        [StructLayout(LayoutKind.Sequential,Pack=8)]
        private struct NativeOwnerGraphReceipt
        {
            public uint ApiVersion,StructSize,Result,LastError;
            public UIntPtr UnityBase,Animator,Controller,ControllerConstant,Descriptors,Graph;
            public uint LayerCount,RecordCount,ByteSize,Hash,GraphDirty58,FailureRecord;
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
            public uint PlannedAlreadyNullClipCount,PlannedEmptyOutputCount,PlannedScalarWriteCount;
            public uint CompletedScalarWriteCount,RolledBackScalarWriteCount,ScalarRollbackFailure;
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

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint NativeApiVersion();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeControllerRoundtrip(
            UIntPtr unityBase,UIntPtr animator,ref NativeControllerReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeControllerCapture(
            UIntPtr unityBase,UIntPtr animator,IntPtr output,uint capacity,ref NativeControllerReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeControllerInputCapture(
            UIntPtr unityBase,UIntPtr animator,IntPtr output,uint capacity,ref NativeControllerInputReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeTransitionTopologyCapture(
            UIntPtr unityBase,UIntPtr animator,IntPtr output,uint capacity,ref NativeTransitionTopologyReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeMixerGraphCapture(
            UIntPtr unityBase,UIntPtr animator,IntPtr output,uint capacity,ref NativeMixerGraphReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeMixerGraphRestore(
            UIntPtr unityBase,UIntPtr animator,IntPtr blob,uint size,ref NativeMixerGraphReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeOwnerGraphCapture(
            UIntPtr unityBase,UIntPtr animator,IntPtr output,uint capacity,ref NativeOwnerGraphReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeSettledEndTransitionNormalize(
            UIntPtr unityBase,UIntPtr animator,IntPtr targetTopology,uint targetTopologySize,
            IntPtr targetOwner,uint targetOwnerSize,uint requireNoPlan,ref NativeEndTransitionReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativePlayableTimeRestore(
            UIntPtr unityBase,UIntPtr animator,IntPtr targetOwner,uint targetOwnerSize,
            ref NativePlayableTimeReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeTargetNullClipRestore(
            UIntPtr unityBase,UIntPtr animator,IntPtr targetOwner,uint targetOwnerSize,
            ref NativeTargetNullClipReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeOverrideClipPlayables(
            UIntPtr unityBase,UIntPtr animator,ref NativeOverrideClipReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeControllerNormalize(
            UIntPtr unityBase,UIntPtr animator,ref NativeControllerReceipt receipt);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int NativeControllerRestore(
            UIntPtr unityBase,UIntPtr animator,IntPtr blob,uint size,ref NativeControllerReceipt receipt);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Unicode)] private static extern IntPtr LoadLibrary(string path);
        [DllImport("kernel32",SetLastError=true)] private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32",SetLastError=true,CharSet=CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module,string name);

        private sealed class ParameterState
        {
            internal int Hash;
            internal AnimatorControllerParameterType Type;
            internal float Float;
            internal int Int;
            internal bool Bool;
        }

        private sealed class LayerState
        {
            internal int FullPathHash,ShortNameHash,TagHash;
            internal float NormalizedTime,Weight;
            internal bool Loop;
        }

        private sealed class AnimatorState
        {
            internal Animator Animator;
            internal RuntimeAnimatorController Controller;
            internal Avatar Avatar;
            internal int InstanceId;
            internal string Path;
            internal bool Enabled,ApplyRootMotion;
            internal float Speed;
            internal AnimatorUpdateMode UpdateMode;
            internal AnimatorCullingMode CullingMode;
            internal ParameterState[] Parameters;
            internal LayerState[] Layers;
            internal TransformState[] Transforms;
            internal int DescendantTransformCount,ForeignTransformCount;
            internal byte[] ControllerMemory;
            internal ControllerInputState ControllerInput;
            internal TransitionTopologyState TransitionTopology;
            internal MixerGraphState MixerGraph;
            internal OwnerGraphState OwnerGraph;
        }

        private sealed class ControllerInputState
        {
            internal uint Animator,Controller,ControllerConstant,ControllerInput,Records;
            internal uint RecordCount,OuterCount,Hash;
            internal byte[] Bytes;
        }

        private sealed class TransitionTopologyState
        {
            internal uint Animator,Controller,ControllerMemory,ControllerGraphMemory,LiveLayers;
            internal uint BlobCapacity,BlobLayerCount,GraphLayerCount,LiveLayerCount,Hash,TopologyMismatchCount;
            internal byte[] Bytes;
        }

        private sealed class MixerGraphState
        {
            internal uint Animator,Controller,ControllerConstant,Descriptors;
            internal uint LayerCount,RecordCount,Hash;
            internal byte[] Bytes;
        }

        private sealed class OwnerGraphState
        {
            internal uint Animator,Controller,ControllerConstant,Descriptors,Graph;
            internal uint LayerCount,RecordCount,Hash,GraphDirty58;
            internal byte[] Bytes;
        }

        private sealed class TransformState
        {
            internal Transform Transform;
            internal int InstanceId;
            internal string Path;
            internal Vector3 LocalPosition,LocalScale;
            internal Quaternion LocalRotation;
        }

        private sealed class FrameState
        {
            internal int Frame;
            internal AnimatorState[] Animators;
            internal UnityEngine.Random.State RandomState;
            internal int ChefRandomizeEventCount;
            internal bool RequiresFinalControllerRestore;
        }

        private sealed class ChefRandomizeEvent
        {
            internal int AnimatorInstanceId,Layer,FullPathHash,ShortNameHash,ParameterHash,ParameterBefore,ParameterValue;
            internal int LastCapturedOutputFrame;
            internal string AnimatorPath;
            internal UnityEngine.Random.State Before,After;
        }

        public sealed class RandomizeCallbackState
        {
            internal bool Replay,Record,Chef;
            internal int ReferenceIndex=-1;
            internal int ParameterHash,ParameterBefore,LastCapturedOutputFrame;
            internal string AnimatorPath,Mode;
            internal UnityEngine.Random.State AmbientBefore,EffectiveBefore;
        }

        private enum ChefRandomizeMode
        {
            Record,
            ReplayStaged,
            ReplayActive,
            Faulted
        }

        private sealed class ResumePrefixState
        {
            internal FrameState Boundary;
            internal FrameState Snapshot;
        }

        private static ChefAnimatorCheckpointModule active;
        private static readonly FieldInfo[] unityRandomStateFields=typeof(UnityEngine.Random.State)
            .GetFields(BindingFlags.Instance|BindingFlags.NonPublic)
            .Where(value=>value.FieldType==typeof(int))
            .OrderBy(value=>value.Name,StringComparer.Ordinal).ToArray();
        private static readonly FieldInfo randomizeParameterHashField=typeof(RandomizeAnimParam)
            .GetField("m_parameterHash",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly FieldInfo cachedPtr=typeof(UnityEngine.Object).GetField("m_CachedPtr",BindingFlags.Instance|BindingFlags.NonPublic);
        private readonly SortedDictionary<int,FrameState> history=new SortedDictionary<int,FrameState>();
        private readonly SortedDictionary<int,FrameState> replayReference=new SortedDictionary<int,FrameState>();
        private readonly SortedDictionary<int,ResumePrefixState> resumePrefixReference=new SortedDictionary<int,ResumePrefixState>();
        private readonly HashSet<int> ambiguousResumePrefix=new HashSet<int>();
        private Harmony harmony;
        private IntPtr nativeLibrary;
        private NativeApiVersion nativeApiVersion;
        private NativeControllerRoundtrip nativeControllerRoundtrip;
        private NativeControllerCapture nativeControllerCapture;
        private NativeControllerInputCapture nativeControllerInputCapture;
        private NativeTransitionTopologyCapture nativeTransitionTopologyCapture;
        private NativeMixerGraphCapture nativeMixerGraphCapture;
        private NativeMixerGraphRestore nativeMixerGraphRestore;
        private NativeOwnerGraphCapture nativeOwnerGraphCapture;
        private NativeSettledEndTransitionNormalize nativeSettledEndTransitionNormalize;
        private NativePlayableTimeRestore nativePlayableTimeRestore;
        private NativeTargetNullClipRestore nativeTargetNullClipRestore;
        private NativeOverrideClipPlayables nativeOverrideClipPlayables;
        private NativeControllerNormalize nativeControllerNormalize;
        private NativeControllerRestore nativeControllerRestore;
        private uint unityPlayerBase;
        private string nativePath,nativeSha256;
        private string sceneIdentity,normalizedSceneIdentity,failure,resumeFailure;
        private int warpTarget=-1;
        private int scheduledResumeReadyCaptureFrame=-1,scheduledResumeReadyLastObservedFrame=-1;
        private bool warpEligible,restoreApplied,pendingResumeRestore,resumeRestoreApplied,disposed;
        private int resumeRestoreStage,resumeStageAUnityFrame=-1,resumeStageBUnityFrame=-1,resumeArmedUnityFrame=-1,resumeArmedPhase=-1;
        private object resumeCoordinationToken,resumeCoordinationServer,resumeCoordinationInput;
        private long resumeCoordinationEpoch,resumeCoordinationExchange;
        private int resumeCoordinationOriginFrame=-1,resumeCoordinationOriginPhase=-1,resumeCoordinationTargetPhase=-1;
        private FrameState resumeFrame,resumeReadyFrame;
        private long captures,duplicateCaptures,restores,resumeControllerRestores,poseAssignments,posePreimageAdjustments;
        private long controllerCaptures,controllerCaptureBytes,controllerRestores;
        private long controllerInputCaptures,controllerInputCaptureBytes;
        private long transitionTopologyCaptures,transitionTopologyCaptureBytes;
        private long mixerGraphCaptures,mixerGraphCaptureBytes,mixerGraphRestores,mixerWeightWrites;
        private long ownerGraphCaptures,ownerGraphCaptureBytes,ownerGraphSceneCaptureBytes;
        private long settledEndTransitionNormalizeAttempts,settledEndTransitionPlanned,settledEndTransitionCompleted;
        private long playableTimeRestoreAttempts,playableTimeRestorePlans,playableTimeRestoreCompleted;
        private long targetNullClipRestoreAttempts,targetNullClipRestorePlans,targetNullClipRestoreCompleted;
        private long targetNullScalarRestorePlans,targetNullScalarRestoreCompleted;
        private long overrideClipAttempts,overrideClipMutations,resumeStageAHolds,resumeStageBHolds,resumeFinalizationArms;
        private long controllerNormalizations;
        private long replayFrameComparisons;
        private long resumePrefixReferenceCaptures,resumePrefixReferencePostCaptures,resumePrefixReplayPreCaptures,resumePrefixReplayPostCaptures,resumePrefixObserverFailures;
        private long scheduledResumeReadyCaptureArms,scheduledResumeReadyCaptureTriggers;
        private long resumeReadyRestores,resumeCompletions,resumeCompletionFailures,unityRandomStateRestores,unityRandomBoundaryCorrections;
        private object lastRestore,lastControllerRestore,lastMixerGraphRestore,lastControllerNormalization,lastConfigurationObservation,lastPoseRestore,lastPoseFailure,lastProbe,lastControllerProbe;
        private object lastResumePrefixObservation,lastResumePrefixReferencePostObservation,lastResumeCompletion,lastScheduledResumeReadyCapture;
        private object lastSettledEndTransitionNormalize;
        private object lastPlayableTimeRestore;
        private object lastTargetNullClipRestore;
        private object lastOverrideClipRestore,lastResumeCoordination,lastUnityRandomStateRestore,lastUnityRandomBoundaryCorrection;
        private object firstReplayDifference;
        private object firstRandomStateReplayDifference;
        private object firstRandomStateReplayPreCorrectionDifference;
        private object firstChefRandomizeReplayDifference,chefRandomizeFailure;
        private object firstControllerInputReplayDifference;
        private object firstTransitionTopologyReplayDifference;
        private object firstMixerGraphReplayDifference;
        private object firstOwnerGraphReplayDifference;
        private object firstResumePrefixPreDifference,firstResumePrefixPreControllerInputDifference;
        private object firstResumePrefixPreRandomStateDifference;
        private object firstResumePrefixPreTransitionTopologyDifference,firstResumePrefixPreMixerGraphDifference;
        private object firstResumePrefixPreOwnerGraphDifference;
        private object firstResumePrefixPostDifference,firstResumePrefixPostControllerInputDifference;
        private object firstResumePrefixPostRandomStateDifference;
        private object firstResumePrefixPostTransitionTopologyDifference,firstResumePrefixPostMixerGraphDifference;
        private object firstResumePrefixPostOwnerGraphDifference;
        private string resumePrefixObserverFailure,scheduledResumeReadyCaptureFailure;
        private readonly List<object> controllerInputRestoreObservations=new List<object>();
        private readonly List<object> transitionTopologyRestoreObservations=new List<object>();
        private readonly List<object> mixerGraphRestoreObservations=new List<object>();
        private readonly List<object> settledEndTransitionNormalizeObservations=new List<object>();
        private readonly List<object> playableTimeRestoreObservations=new List<object>();
        private readonly List<object> targetNullClipRestoreObservations=new List<object>();
        private readonly List<object> overrideClipRestoreObservations=new List<object>();
        private readonly List<object> randomizeAnimParamObservations=new List<object>();
        private readonly List<ChefRandomizeEvent> chefRandomizeReference=new List<ChefRandomizeEvent>();
        private readonly HashSet<int> trackedChefAnimatorIds=new HashSet<int>();
        private int replayChefRandomizeCursor,replayChefRandomizeLimit;
        private ChefRandomizeMode chefRandomizeMode=ChefRandomizeMode.Record;
        private long chefRandomizeReplayRebases,replayPrefixCommits;
        private object lastReplayPrefixCommit;

        private const uint MixerGraphRecordSize=92u;
        private const uint OwnerGraphRecordSize=212u;
        private const uint NativeAnimatorApiVersion=17u;
        private const uint MaximumOwnerGraphBytes=65536u*OwnerGraphRecordSize;
        private const long MaximumOwnerGraphCaptureBytes=256L*1024L*1024L;

        public string Name { get { return "chef-animator-checkpoint-v57-scheduled-final-controller"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("ChefAnimatorCheckpointModule");
            if(args==null)args=new Dictionary<string,object>();
            if(operation=="probe-controller-memory")ProbeControllerMemory(args);
            else if(operation=="activate")
            {
                BindNativeControllerMemory(args);Activate();
            }
            else if(operation=="commit-replay-prefix")CommitReplayPrefix(args);
            else if(operation=="capture-resume-ready-at-frame")ArmResumeReadyCaptureAtFrame(args);
            else
            {
                if(args.Count!=0)throw new ArgumentException("Operation takes no arguments.");
                if(operation=="deactivate")Deactivate();
                else if(operation=="probe-pose")ProbePose();
                else if(operation!="status")throw new ArgumentException("Use activate, deactivate, capture-resume-ready-at-frame, commit-replay-prefix, probe-pose, probe-controller-memory or status.");
            }
            return Status(operation);
        }

        private void ArmResumeReadyCaptureAtFrame(Dictionary<string,object> args)
        {
            RequireFence();
            if(args.Count!=1||!args.ContainsKey("frame")||args["frame"]==null||
                (args["frame"].GetType()!=typeof(int)&&args["frame"].GetType()!=typeof(long)))
                throw new ArgumentException("capture-resume-ready-at-frame requires exactly one whole-number frame.");
            long requestedFrame=Convert.ToInt64(args["frame"]);
            if(requestedFrame<int.MinValue||requestedFrame>int.MaxValue)
                throw new ArgumentOutOfRangeException("frame","Scheduled Animator resume-ready capture frame is outside Int32 range.");
            if(!ReferenceEquals(active,this)||history.Count==0||sceneIdentity==null)
                throw new InvalidOperationException("Scheduled Animator resume-ready capture requires an active scene with a retained boundary.");
            if(scheduledResumeReadyCaptureFrame>=0)
                throw new InvalidOperationException("An Animator resume-ready capture is already scheduled.");
            if(pendingResumeRestore||resumeFrame!=null||resumeReadyFrame!=null||resumeRestoreApplied||
                chefRandomizeMode!=ChefRandomizeMode.Record||failure!=null||resumeFailure!=null||
                chefRandomizeFailure!=null||resumePrefixObserverFailure!=null)
                throw new InvalidOperationException("Scheduled Animator resume-ready capture requires an unfaulted forward-recording state.");
            int current=history.Keys.Last();
            int target=(int)requestedFrame;
            if(target<=current)
                throw new InvalidOperationException("Scheduled Animator resume-ready capture target must be after current captured frame "+current+".");
            scheduledResumeReadyCaptureFrame=target;
            scheduledResumeReadyLastObservedFrame=current;
            scheduledResumeReadyCaptureFailure=null;
            scheduledResumeReadyCaptureArms++;
            lastScheduledResumeReadyCapture=new Dictionary<string,object>{{"pending","scheduled-capture"},
                {"currentFrame",current},{"frame",target},{"gameStateMutation",false},{"exact",false}};
        }

        // An unwind may intentionally abandon the remainder of an older future.
        // This authoring-only transaction either commits an exact replayed prefix,
        // or commits the restored target before its first divergent advancing
        // frame. It mutates only this module's retained comparison history.
        private void CommitReplayPrefix(Dictionary<string,object> args)
        {
            RequireFence();
            if(args.Count!=1||!args.ContainsKey("frame"))
                throw new ArgumentException("commit-replay-prefix requires exactly the controller's paused output frame.");
            int frame=Convert.ToInt32(args["frame"]);
            if(chefRandomizeMode==ChefRandomizeMode.Faulted||chefRandomizeFailure!=null)
                throw new InvalidOperationException("Chef RandomizeAnimParam replay is faulted until the scene is reset.");
            if(failure!=null||resumeFailure!=null||resumePrefixObserverFailure!=null)
                throw new InvalidOperationException("Replay-prefix commit is unavailable while an Animator parity failure is retained.");
            if(chefRandomizeMode!=ChefRandomizeMode.ReplayStaged&&chefRandomizeMode!=ChefRandomizeMode.ReplayActive)
                throw new InvalidOperationException("No staged or active replay prefix is available to commit.");
            if(Hpmv.Injector.Server==null)
                throw new InvalidOperationException("Replay-prefix commit requires the live controller server.");
            int commitCount,verifiedPrefixFrames=0;
            string priorMode=chefRandomizeMode.ToString();
            if(chefRandomizeMode==ChefRandomizeMode.ReplayStaged)
            {
                if(!pendingResumeRestore||resumeFrame==null||resumeReadyFrame==null||resumeRestoreApplied||
                    frame!=warpTarget||frame!=resumeFrame.Frame||frame!=resumeReadyFrame.Frame||
                    replayFrameComparisons!=0||resumeRestoreStage!=1||warpEligible||restoreApplied||
                    resumeCoordinationToken!=null||resumeCoordinationServer!=null||resumeCoordinationInput!=null||
                    history.Count==0||history.Keys.Last()!=frame||!history.ContainsKey(frame)||
                    !ReferenceEquals(resumeFrame,history[frame]))
                    throw new InvalidOperationException("Staged replay-prefix commit is not at the exact restored target before resume.");
                ResumePrefixState stagedPrefix;
                if(!resumePrefixReference.TryGetValue(frame,out stagedPrefix)||
                    !ReferenceEquals(stagedPrefix.Boundary,resumeFrame)||
                    !ReferenceEquals(stagedPrefix.Snapshot,resumeReadyFrame)||ambiguousResumePrefix.Contains(frame))
                    throw new InvalidOperationException("Staged replay-prefix commit has no exact linked resume-ready checkpoint.");
                commitCount=resumeReadyFrame.ChefRandomizeEventCount;
                if(replayChefRandomizeCursor!=commitCount)
                    throw new InvalidOperationException("Staged replay-prefix callback cursor differs from the restored target.");
            }
            else
            {
                if(pendingResumeRestore||resumeFrame!=null||resumeReadyFrame!=null||resumeRestoreApplied)
                    throw new InvalidOperationException("Active replay-prefix commit still has a pending resume transaction.");
                if(resumeRestoreStage!=0||resumeCoordinationToken!=null||resumeCoordinationServer!=null||
                    resumeCoordinationInput!=null)
                    throw new InvalidOperationException("Active replay-prefix commit has non-quiescent resume coordination state.");
                FrameState expected,observed;
                if(!replayReference.TryGetValue(frame,out expected)||!history.TryGetValue(frame,out observed)||
                    history.Count==0||history.Keys.Last()!=frame)
                    throw new InvalidOperationException("Active replay-prefix commit is not at a captured reference output boundary.");
                int[] expectedKeys=replayReference.Keys.Where(value=>value<=frame).ToArray();
                int[] observedKeys=history.Keys.Where(value=>value>warpTarget&&value<=frame).ToArray();
                verifiedPrefixFrames=expectedKeys.Length;
                if(verifiedPrefixFrames==0||replayFrameComparisons!=verifiedPrefixFrames||
                    !expectedKeys.SequenceEqual(observedKeys))
                    throw new InvalidOperationException("Active replay-prefix coverage is incomplete at the commit boundary.");
                if(firstReplayDifference!=null||firstRandomStateReplayDifference!=null||
                    firstChefRandomizeReplayDifference!=null||firstControllerInputReplayDifference!=null||
                    firstTransitionTopologyReplayDifference!=null||firstMixerGraphReplayDifference!=null||
                    firstOwnerGraphReplayDifference!=null||firstResumePrefixPostDifference!=null||
                    firstResumePrefixPostRandomStateDifference!=null||firstResumePrefixPostControllerInputDifference!=null||
                    firstResumePrefixPostTransitionTopologyDifference!=null||firstResumePrefixPostMixerGraphDifference!=null||
                    firstResumePrefixPostOwnerGraphDifference!=null)
                    throw new InvalidOperationException("Active replay prefix contains a semantic difference and cannot become a branch.");
                foreach(int key in expectedKeys)
                {
                    FrameState reference=replayReference[key],capture=history[key];
                    if(FirstDifference(reference,capture)!=null||FirstRandomStateDifference(reference,capture)!=null||
                        FirstControllerInputDifference(reference,capture)!=null||
                        FirstTransitionTopologyDifference(reference,capture)!=null||
                        FirstMixerGraphDifference(reference,capture)!=null||FirstOwnerGraphDifference(reference,capture)!=null||
                        reference.ChefRandomizeEventCount!=capture.ChefRandomizeEventCount)
                        throw new InvalidOperationException("Active replay prefix is not exact at output frame "+key+".");
                }
                commitCount=expected.ChefRandomizeEventCount;
                if(observed.ChefRandomizeEventCount!=commitCount||replayChefRandomizeCursor!=commitCount)
                    throw new InvalidOperationException("Active replay-prefix callback count differs at the commit boundary.");
            }
            if(commitCount<0||commitCount>chefRandomizeReference.Count||
                replayChefRandomizeCursor!=commitCount||replayChefRandomizeLimit<commitCount)
                throw new InvalidOperationException("Replay-prefix callback bounds are invalid.");
            int callbackTail=chefRandomizeReference.Count-commitCount;
            int futureFrames=replayReference.Keys.Count(value=>value>frame);
            int retainedReferenceFrames=replayReference.Count;
            ResumePrefixState commitPrefix;
            bool prefixAvailable=resumePrefixReference.TryGetValue(frame,out commitPrefix)&&
                history.ContainsKey(frame)&&ReferenceEquals(commitPrefix.Boundary,history[frame])&&
                !ambiguousResumePrefix.Contains(frame);
            if(callbackTail>0)chefRandomizeReference.RemoveRange(commitCount,callbackTail);
            replayReference.Clear();replayChefRandomizeLimit=commitCount;
            if(chefRandomizeMode==ChefRandomizeMode.ReplayActive)chefRandomizeMode=ChefRandomizeMode.Record;
            replayPrefixCommits++;
            lastReplayPrefixCommit=new Dictionary<string,object>{{"frame",frame},{"priorMode",priorMode},
                {"resultMode",chefRandomizeMode.ToString()},{"verifiedPrefixFrames",verifiedPrefixFrames},
                {"retainedReferenceFramesBefore",retainedReferenceFrames},{"discardedFutureFrames",futureFrames},
                {"callbackCount",commitCount},{"discardedCallbackTail",callbackTail},
                {"resumePrefixAvailableAtCommit",prefixAvailable},
                {"resumePrefixPolicy",prefixAvailable?"existing-exact-linked-prefix":"captured-on-next-ordinary-resume"},
                {"liveServerFrame",Hpmv.Injector.Server.CurrentFrameData.FrameNumber},
                {"frameSource","explicit-controller-paused-output-boundary"},
                {"gameStateMutation",false},{"exact",true}};
        }

        private void Activate()
        {
            RequireFence();
            if(unityRandomStateFields.Length!=4||unityRandomStateFields[0].Name!="s0"||unityRandomStateFields[1].Name!="s1"||
                unityRandomStateFields[2].Name!="s2"||unityRandomStateFields[3].Name!="s3"||randomizeParameterHashField==null||
                randomizeParameterHashField.FieldType!=typeof(int))
                throw new InvalidOperationException("UnityEngine.Random.State layout differs from the verified four-word Unity 2017 layout.");
            if(nativeLibrary==IntPtr.Zero||nativeControllerCapture==null||nativeControllerInputCapture==null||nativeTransitionTopologyCapture==null||
                nativeMixerGraphCapture==null||nativeMixerGraphRestore==null||nativeOwnerGraphCapture==null||nativeSettledEndTransitionNormalize==null||
                nativePlayableTimeRestore==null||nativeTargetNullClipRestore==null||
                nativeOverrideClipPlayables==null||nativeControllerNormalize==null||nativeControllerRestore==null)
                throw new InvalidOperationException("Activate requires the verified native Animator controller helper.");
            if(ReferenceEquals(active,this))return;
            if(active!=null)throw new InvalidOperationException("Another chef Animator checkpoint module is active.");
            var capture=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"CaptureFrame",new[]{typeof(int)});
            var prepare=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"Prepare",new[]{typeof(Hpmv.WarpSpec)});
            var complete=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan),"Complete",Type.EmptyTypes);
            var restoreFailure=AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint),"RecordRestoreFailure",new[]{typeof(int),typeof(Exception),typeof(bool)});
            Type helpers=typeof(NativeSessionBridge).Assembly.GetType("SuperchargedPatch.Helpers",true);
            var resume=AccessTools.DeclaredMethod(helpers,"Resume",Type.EmptyTypes);
            var randomize=AccessTools.DeclaredMethod(typeof(RandomizeAnimParam),"OnStateEnter",
                new[]{typeof(Animator),typeof(AnimatorStateInfo),typeof(int)});
            if(capture==null||prepare==null||complete==null||restoreFailure==null||resume==null||resume.ReturnType!=typeof(void)||randomize==null)
                throw new InvalidOperationException("Installed native checkpoint lifecycle contract differs.");
            harmony=new Harmony("supercharged.authoring.chef-animator-checkpoint."+GetType().Assembly.GetName().Name);
            harmony.Patch(capture,postfix:new HarmonyMethod(GetType().GetMethod("AfterCaptureFrame",BindingFlags.Public|BindingFlags.Static)));
            harmony.Patch(prepare,prefix:new HarmonyMethod(GetType().GetMethod("BeforePrepare",BindingFlags.Public|BindingFlags.Static)));
            harmony.Patch(prepare,postfix:new HarmonyMethod(GetType().GetMethod("AfterPrepare",BindingFlags.Public|BindingFlags.Static)));
            harmony.Patch(complete,postfix:new HarmonyMethod(GetType().GetMethod("AfterRestoreComplete",BindingFlags.Public|BindingFlags.Static)));
            harmony.Patch(restoreFailure,postfix:new HarmonyMethod(GetType().GetMethod("AfterRestoreFailure",BindingFlags.Public|BindingFlags.Static)));
            var resumePrefix=new HarmonyMethod(GetType().GetMethod("BeforeAuthoringResume",BindingFlags.Public|BindingFlags.Static));
            var resumePostfix=new HarmonyMethod(GetType().GetMethod("AfterAuthoringResume",BindingFlags.Public|BindingFlags.Static));
            resumePrefix.priority=Priority.Last;resumePostfix.priority=Priority.Last;
            harmony.Patch(resume,prefix:resumePrefix,postfix:resumePostfix);
            harmony.Patch(randomize,
                prefix:new HarmonyMethod(GetType().GetMethod("BeforeRandomizeAnimParam",BindingFlags.Public|BindingFlags.Static)),
                postfix:new HarmonyMethod(GetType().GetMethod("AfterRandomizeAnimParam",BindingFlags.Public|BindingFlags.Static)));
            active=this;
        }

        public static void BeforeRandomizeAnimParam(RandomizeAnimParam __instance,Animator animator,
            AnimatorStateInfo stateInfo,int layerIndex,out RandomizeCallbackState __state)
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null){__state=null;return;}
            string path=animator==null?null:PathOf(animator.transform);
            int parameterHash=(int)randomizeParameterHashField.GetValue(__instance);
            int parameterBefore=animator==null?0:animator.GetInteger(parameterHash);
            int animatorInstanceId=animator==null?0:animator.GetInstanceID();
            bool chef=animator!=null&&module.trackedChefAnimatorIds.Contains(animatorInstanceId);
            bool replay=module.chefRandomizeMode==ChefRandomizeMode.ReplayActive;
            bool record=module.chefRandomizeMode==ChefRandomizeMode.Record;
            int lastCapturedOutputFrame=module.history.Count==0?-1:module.history.Keys.Last();
            __state=new RandomizeCallbackState { Replay=replay,Record=record,Chef=chef,Mode=module.chefRandomizeMode.ToString(),
                AnimatorPath=path,ParameterHash=parameterHash,ParameterBefore=parameterBefore,
                LastCapturedOutputFrame=lastCapturedOutputFrame,
                AmbientBefore=UnityEngine.Random.state,EffectiveBefore=UnityEngine.Random.state };
            if(!chef||!replay)return;
            if(module.replayChefRandomizeCursor<0||module.replayChefRandomizeCursor>=module.replayChefRandomizeLimit||
                module.replayChefRandomizeCursor>=module.chefRandomizeReference.Count)
                module.FailChefRandomize("Replay emitted a chef RandomizeAnimParam callback beyond the retained interval.",path,null);
            int index=module.replayChefRandomizeCursor++;
            ChefRandomizeEvent expected=module.chefRandomizeReference[index];
            bool identity=expected.AnimatorInstanceId==animatorInstanceId&&expected.AnimatorPath==path&&
                expected.Layer==layerIndex&&expected.FullPathHash==stateInfo.fullPathHash&&
                expected.ShortNameHash==stateInfo.shortNameHash&&expected.ParameterHash==parameterHash&&
                expected.ParameterBefore==parameterBefore&&expected.LastCapturedOutputFrame==lastCapturedOutputFrame;
            if(!identity)
                module.FailChefRandomize("Replay chef RandomizeAnimParam callback identity differs from the retained reference stream.",path,
                    new Dictionary<string,object>{{"referenceIndex",index},{"expected",Describe(expected)},
                        {"actual",new Dictionary<string,object>{{"animatorInstanceId",animatorInstanceId},{"animator",path},
                            {"layer",layerIndex},{"fullPathHash",stateInfo.fullPathHash},{"shortNameHash",stateInfo.shortNameHash},
                            {"parameterHash",parameterHash},{"parameterBefore",parameterBefore},
                            {"lastCapturedOutputFrame",lastCapturedOutputFrame}}}});
            UnityEngine.Random.state=expected.Before;
            if(!SameRandomState(UnityEngine.Random.state,expected.Before))
                module.FailChefRandomize("Replay chef RandomizeAnimParam RNG preimage did not restore exactly.",path,null);
            __state.ReferenceIndex=index;__state.EffectiveBefore=UnityEngine.Random.state;
            module.chefRandomizeReplayRebases++;
        }

        public static void AfterRandomizeAnimParam(RandomizeAnimParam __instance,Animator animator,
            AnimatorStateInfo stateInfo,int layerIndex,RandomizeCallbackState __state)
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null||__state==null)return;
            int parameterHash=(int)randomizeParameterHashField.GetValue(__instance);
            UnityEngine.Random.State after=UnityEngine.Random.state;
            int parameterValue=animator==null?0:animator.GetInteger(parameterHash);
            if(__state.Chef)
            {
                if(__state.Replay)
                {
                    ChefRandomizeEvent expected=module.chefRandomizeReference[__state.ReferenceIndex];
                    if(parameterValue!=expected.ParameterValue||!SameRandomState(after,expected.After))
                        module.FailChefRandomize("Replay chef RandomizeAnimParam callback result differs from the retained reference stream.",
                            __state.AnimatorPath,new Dictionary<string,object>{{"referenceIndex",__state.ReferenceIndex},
                                {"expected",Describe(expected)},{"actualParameterValue",parameterValue},
                                {"actualRandomAfter",DescribeRandomState(after)}});
                }
                else if(__state.Record)
                {
                    module.chefRandomizeReference.Add(new ChefRandomizeEvent { AnimatorInstanceId=animator.GetInstanceID(),
                        AnimatorPath=__state.AnimatorPath,Layer=layerIndex,FullPathHash=stateInfo.fullPathHash,
                        ShortNameHash=stateInfo.shortNameHash,ParameterHash=parameterHash,
                        ParameterBefore=__state.ParameterBefore,ParameterValue=parameterValue,
                        LastCapturedOutputFrame=__state.LastCapturedOutputFrame,
                        Before=__state.EffectiveBefore,After=after });
                }
            }
            int outputFrame=NativeSessionBridge.KitchenReady&&Hpmv.Injector.Server!=null?
                Hpmv.Injector.Server.CurrentFrameData.FrameNumber:-1;
            module.randomizeAnimParamObservations.Add(new Dictionary<string,object>{
                {"mode",__state.Mode},{"replay",__state.Replay},{"record",__state.Record},
                {"chef",__state.Chef},{"referenceIndex",__state.ReferenceIndex},
                {"outputFrame",outputFrame},{"unityFrame",Time.frameCount},
                {"animator",animator==null?null:PathOf(animator.transform)},{"layer",layerIndex},
                {"stateFullPathHash",stateInfo.fullPathHash},{"stateShortNameHash",stateInfo.shortNameHash},
                {"parameterHash",parameterHash},{"parameterBefore",__state.ParameterBefore},{"parameterValue",parameterValue},
                {"lastCapturedOutputFrame",__state.LastCapturedOutputFrame},
                {"ambientRandomBefore",DescribeRandomState(__state.AmbientBefore)},
                {"randomBefore",DescribeRandomState(__state.EffectiveBefore)},{"randomAfter",DescribeRandomState(after)}});
            if(module.randomizeAnimParamObservations.Count>4096)module.randomizeAnimParamObservations.RemoveAt(0);
        }

        private void FailChefRandomize(string message,string path,object detail)
        {
            chefRandomizeFailure=new Dictionary<string,object>{{"message",message},{"animator",path},
                {"mode",chefRandomizeMode.ToString()},{"referenceCursor",replayChefRandomizeCursor},
                {"referenceLimit",replayChefRandomizeLimit},{"referenceCount",chefRandomizeReference.Count},{"detail",detail}};
            chefRandomizeMode=ChefRandomizeMode.Faulted;
            failure=message;throw new InvalidOperationException(message);
        }

        public static void AfterCaptureFrame(int __0)
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null||!NativeSessionBridge.KitchenReady)return;
            try
            {
                module.CaptureFrame(__0);
                module.CaptureScheduledResumeReadyAtFrame(__0);
                if(module.resumeFailure==null&&module.chefRandomizeFailure==null)module.failure=null;
            }
            catch(Exception error)
            {
                if(module.scheduledResumeReadyCaptureFrame==__0&&module.scheduledResumeReadyCaptureFailure==null)
                {
                    module.scheduledResumeReadyCaptureFailure=error.ToString();
                    module.lastScheduledResumeReadyCapture=new Dictionary<string,object>{{"pending","scheduled-capture"},
                        {"frame",__0},{"stage","exact-boundary-or-resume-ready-capture"},
                        {"error",module.scheduledResumeReadyCaptureFailure},{"gameStateMutation",false},{"exact",false}};
                }
                module.failure=error.ToString();
            }
        }

        private void CaptureScheduledResumeReadyAtFrame(int observedFrame)
        {
            int target=scheduledResumeReadyCaptureFrame;
            if(target<0)return;
            scheduledResumeReadyLastObservedFrame=observedFrame;
            if(observedFrame<target)return;
            if(scheduledResumeReadyCaptureFailure!=null)
                throw new InvalidOperationException("Scheduled Animator resume-ready capture already failed at exact output frame "+target+
                    ": "+scheduledResumeReadyCaptureFailure);
            if(observedFrame>target)
                throw new InvalidOperationException("Scheduled Animator resume-ready capture skipped exact output frame "+target+
                    "; next observed frame was "+observedFrame+".");
            FrameState boundary;
            if(!history.TryGetValue(target,out boundary))
                throw new InvalidOperationException("Scheduled Animator resume-ready target has no exact boundary checkpoint.");
            if(pendingResumeRestore||resumeFrame!=null||resumeReadyFrame!=null||resumeRestoreApplied||
                chefRandomizeMode!=ChefRandomizeMode.Record)
                throw new InvalidOperationException("Scheduled Animator resume-ready target was reached outside forward-recording state.");
            bool prefix=ObserveResumePrefix(target,"reference-prefix");
            bool post=prefix&&ObserveResumePrefix(target,"reference-post-observer");
            ResumePrefixState reference;
            bool linked=resumePrefixReference.TryGetValue(target,out reference)&&
                ReferenceEquals(reference.Boundary,boundary);
            if(!prefix||!post||!linked||ambiguousResumePrefix.Contains(target)||resumePrefixObserverFailure!=null)
                throw new InvalidOperationException("Scheduled Animator resume-ready capture was not exact at output frame "+target+
                    ": "+(resumePrefixObserverFailure??"linked tuple validation failed."));
            object pauseProjection=ProjectScheduledResumeReadyPause(reference.Snapshot);
            // Clear only after both synchronous read-only observations have
            // produced one unambiguous template linked to this exact boundary.
            // Unlike an ordinary Helpers.Resume-prefix capture, this snapshot
            // was taken while the Animator was advancing. Project only the
            // TimeManager-owned public/native speed word into its paused form;
            // every other component remains the exact captured tuple.
            scheduledResumeReadyCaptureFrame=-1;
            scheduledResumeReadyCaptureFailure=null;
            scheduledResumeReadyCaptureTriggers++;
            lastScheduledResumeReadyCapture=new Dictionary<string,object>{{"pending",false},
                {"frame",target},{"boundaryIdentityLinked",true},{"ambiguous",false},
                {"pauseProjection",pauseProjection},{"gameStateMutation",false},{"exact",true}};
        }

        private object ProjectScheduledResumeReadyPause(FrameState snapshot)
        {
            if(snapshot==null||snapshot.Animators==null)
                throw new InvalidOperationException("Scheduled Animator resume-ready projection has no captured tuple.");
            var rows=new List<object>();
            foreach(AnimatorState state in snapshot.Animators)
            {
                ControllerInputState input=state.ControllerInput;
                if(input==null||input.Bytes==null)
                    throw new InvalidOperationException("Scheduled Animator resume-ready projection lacks ControllerInput for "+state.Path+".");
                byte[] projected;string error;
                if(!AnimatorResumeSemanticState.TryProjectPausedControllerInput(input.Bytes,state.Speed,out projected,out error))
                    throw new InvalidOperationException("Scheduled Animator pause projection failed for "+state.Path+": "+error);
                float advancingSpeed=state.Speed;uint advancingHash=input.Hash;
                state.Speed=0f;input.Bytes=projected;input.Hash=ByteHash(projected);
                rows.Add(new Dictionary<string,object>{{"path",state.Path},{"advancingSpeed",advancingSpeed},
                    {"pausedSpeed",state.Speed},{"advancingHash",advancingHash.ToString("X8")},
                    {"pausedHash",input.Hash.ToString("X8")},{"projectedByteCount",4},{"exact",true}});
            }
            snapshot.RequiresFinalControllerRestore=true;
            return new Dictionary<string,object>{{"owner","TimeManager main pause"},
                {"source","scheduled advancing output boundary"},{"projectedField","ControllerInput+0/public Animator.speed"},
                {"requiresFinalControllerRestore",true},{"gameStateMutation",false},{"animators",rows.ToArray()}};
        }

        public static void BeforePrepare(Hpmv.WarpSpec __0)
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null)return;
            if(module.scheduledResumeReadyCaptureFrame>=0)
                throw new InvalidOperationException("An exact-frame Animator resume-ready capture is still pending.");
            if(module.pendingResumeRestore||module.resumeFrame!=null||module.resumeReadyFrame!=null||module.resumeRestoreApplied)
                throw new InvalidOperationException("A previous chef Animator resume-ready restore is still pending.");
            if(module.chefRandomizeMode==ChefRandomizeMode.Faulted||module.chefRandomizeFailure!=null)
                throw new InvalidOperationException("Chef RandomizeAnimParam replay is faulted until the scene is reset.");
            if(module.chefRandomizeMode!=ChefRandomizeMode.Record)
                throw new InvalidOperationException("Chef RandomizeAnimParam replay did not return to record mode before a new rewind.");
            module.warpTarget=__0==null?-1:__0.Frame;
            FrameState boundary;ResumePrefixState resumeReady;
            bool hasBoundary=module.history.TryGetValue(module.warpTarget,out boundary);
            bool hasResumeReady=module.resumePrefixReference.TryGetValue(module.warpTarget,out resumeReady);
            module.warpEligible=hasBoundary&&hasResumeReady&&!module.ambiguousResumePrefix.Contains(module.warpTarget)&&
                ReferenceEquals(resumeReady.Boundary,boundary);
            module.restoreApplied=false;
            module.pendingResumeRestore=false;
            module.resumeRestoreApplied=false;module.resumeFrame=null;module.resumeReadyFrame=null;
            module.ClearResumeCoordination();
            module.replayReference.Clear();module.replayFrameComparisons=0;module.firstReplayDifference=null;
            module.firstRandomStateReplayDifference=null;module.firstRandomStateReplayPreCorrectionDifference=null;
            module.firstChefRandomizeReplayDifference=null;
            module.replayChefRandomizeCursor=0;module.replayChefRandomizeLimit=0;
            module.firstControllerInputReplayDifference=null;module.controllerInputRestoreObservations.Clear();
            module.firstTransitionTopologyReplayDifference=null;module.transitionTopologyRestoreObservations.Clear();
            module.firstMixerGraphReplayDifference=null;module.mixerGraphRestoreObservations.Clear();
            module.firstOwnerGraphReplayDifference=null;
            module.settledEndTransitionNormalizeObservations.Clear();module.lastSettledEndTransitionNormalize=null;
            module.playableTimeRestoreObservations.Clear();module.lastPlayableTimeRestore=null;
            module.targetNullClipRestoreObservations.Clear();module.lastTargetNullClipRestore=null;
            module.overrideClipRestoreObservations.Clear();module.lastOverrideClipRestore=null;
            module.firstResumePrefixPreDifference=null;module.firstResumePrefixPreControllerInputDifference=null;
            module.firstResumePrefixPreRandomStateDifference=null;
            module.firstResumePrefixPreTransitionTopologyDifference=null;module.firstResumePrefixPreMixerGraphDifference=null;
            module.firstResumePrefixPreOwnerGraphDifference=null;
            module.firstResumePrefixPostDifference=null;module.firstResumePrefixPostControllerInputDifference=null;
            module.firstResumePrefixPostRandomStateDifference=null;
            module.firstResumePrefixPostTransitionTopologyDifference=null;module.firstResumePrefixPostMixerGraphDifference=null;
            module.firstResumePrefixPostOwnerGraphDifference=null;
            if(module.warpEligible)
                foreach(var pair in module.history.Where(value=>value.Key>module.warpTarget))
                    module.replayReference.Add(pair.Key,pair.Value);
            if(!module.warpEligible)
                throw new InvalidOperationException("No unambiguous linked chef Animator boundary and resume-ready checkpoint at output frame "+module.warpTarget+".");
            module.replayChefRandomizeCursor=resumeReady.Snapshot.ChefRandomizeEventCount;
            module.replayChefRandomizeLimit=module.replayReference.Count==0?module.replayChefRandomizeCursor:
                module.replayReference.Values.Last().ChefRandomizeEventCount;
            int priorCount=module.replayChefRandomizeCursor;
            bool monotonic=true;
            foreach(FrameState value in module.replayReference.Values)
            {
                if(value.ChefRandomizeEventCount<priorCount||value.ChefRandomizeEventCount>module.replayChefRandomizeLimit)
                    monotonic=false;
                priorCount=value.ChefRandomizeEventCount;
            }
            if(module.replayChefRandomizeCursor<0||module.replayChefRandomizeLimit<module.replayChefRandomizeCursor||
                module.replayChefRandomizeLimit>module.chefRandomizeReference.Count||!monotonic)
                throw new InvalidOperationException("Retained chef RandomizeAnimParam reference stream does not cover the replay interval.");
            module.chefRandomizeMode=ChefRandomizeMode.ReplayStaged;
        }

        public static void AfterPrepare()
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null||!module.warpEligible)return;
            try{module.Restore(module.history[module.warpTarget],"after-prepare");module.restoreApplied=true;module.failure=null;}
            catch(Exception error)
            {
                module.failure=error.ToString();module.chefRandomizeMode=ChefRandomizeMode.Faulted;
                if(module.chefRandomizeFailure==null)
                    module.chefRandomizeFailure=new Dictionary<string,object>{{"message","Initial Animator checkpoint restoration failed."},
                        {"mode",module.chefRandomizeMode.ToString()},{"referenceCursor",module.replayChefRandomizeCursor},
                        {"referenceLimit",module.replayChefRandomizeLimit},{"referenceCount",module.chefRandomizeReference.Count},
                        {"detail",error.ToString()}};
                throw;
            }
        }

        public static void AfterRestoreComplete()
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null||!module.warpEligible)return;
            try
            {
                if(!module.restoreApplied)throw new InvalidOperationException("Chef Animator state was not restored before native warp mutation.");
                FrameState target=module.history[module.warpTarget];
                ResumePrefixState resumeReady=module.resumePrefixReference[module.warpTarget];
                if(module.ambiguousResumePrefix.Contains(module.warpTarget)||!ReferenceEquals(resumeReady.Boundary,target))
                    throw new InvalidOperationException("Chef Animator resume-ready checkpoint linkage changed during restore.");
                foreach(int frame in module.history.Keys.Where(value=>value>module.warpTarget).ToArray())module.history.Remove(frame);
                foreach(int frame in module.resumePrefixReference.Keys.Where(value=>value>module.warpTarget).ToArray())module.resumePrefixReference.Remove(frame);
                module.ambiguousResumePrefix.RemoveWhere(value=>value>module.warpTarget);
                module.resumeFrame=target;module.resumeReadyFrame=resumeReady.Snapshot;
                module.pendingResumeRestore=true;module.resumeRestoreApplied=false;
                module.resumeRestoreStage=1;
                module.lastRestore=new Dictionary<string,object>{{"frame",module.warpTarget},{"animatorCount",4},
                    {"controllerBlobBytes",target.Animators.Sum(value=>value.ControllerMemory.Length)},
                    {"capturedTransforms",target.Animators.Sum(value=>value.Transforms.Length)},
                    {"publicPoseAssignments",0},{"verified",true}};
                module.restores++;module.resumeFailure=null;module.failure=null;
            }
            catch(Exception error)
            {
                module.failure=error.ToString();module.chefRandomizeMode=ChefRandomizeMode.Faulted;
                if(module.chefRandomizeFailure==null)
                    module.chefRandomizeFailure=new Dictionary<string,object>{{"message","Animator restore completion failed."},
                        {"mode",module.chefRandomizeMode.ToString()},{"referenceCursor",module.replayChefRandomizeCursor},
                        {"referenceLimit",module.replayChefRandomizeLimit},{"referenceCount",module.chefRandomizeReference.Count},
                        {"detail",error.ToString()}};
                throw;
            }
            finally{module.warpEligible=false;module.restoreApplied=false;}
        }

        public static void AfterRestoreFailure()
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null)return;
            module.warpEligible=false;module.restoreApplied=false;module.warpTarget=-1;
            module.pendingResumeRestore=false;module.resumeRestoreApplied=false;module.resumeFrame=null;module.resumeReadyFrame=null;
            module.chefRandomizeMode=ChefRandomizeMode.Faulted;
            if(module.chefRandomizeFailure==null)
                module.chefRandomizeFailure=new Dictionary<string,object>{{"message","Checkpoint restore failed while chef callback replay was staged."},
                    {"mode",module.chefRandomizeMode.ToString()},{"referenceCursor",module.replayChefRandomizeCursor},
                    {"referenceLimit",module.replayChefRandomizeLimit},{"referenceCount",module.chefRandomizeReference.Count}};
            module.ClearResumeCoordination();
        }

        // Versioned reflection seam consumed by ResumePhase. Returning zero
        // lets stale, retired module assemblies remain loaded without becoming
        // eligible coordination providers.
        public static int ResumeCoordinationVersion()
        {
            return active==null?0:2;
        }

        // Stages native restoration only after ResumePhase has bound an exact
        // accepted RPC resume. No Animator time or graph evaluation is driven
        // here; the two post-mutation waits are ordinary paused Unity frames.
        // Stage A is first aligned two six-frame scheduler phases before the
        // original resume phase so Stage B and finalization can follow on the
        // next two Unity frames without adding extra post-mutation visits.
        public static int AdvanceAcceptedResumeRestore(object token,Hpmv.InjectorServer server,Hpmv.InputData acceptedInput,
            long epoch,long exchange,int originFrame,int originPhase,int targetPhase,int currentPhase,int unityFrame,bool mayArmFinalization)
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null)return 0;
            try{return module.AdvanceResumeRestore(token,server,acceptedInput,epoch,exchange,
                originFrame,originPhase,targetPhase,currentPhase,unityFrame,mayArmFinalization);}
            catch(Exception error)
            {
                module.FailResumeCoordination(error.ToString());
                throw;
            }
        }

        public static void AcknowledgeAcceptedResumeRestore(object token)
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null)return;
            if(!ReferenceEquals(module.resumeCoordinationToken,token)||module.resumeRestoreStage!=6)
                throw new InvalidOperationException("Animator resume acknowledgement does not match the completed transaction.");
            module.resumeRestoreStage=7;
            module.lastResumeCoordination=module.ResumeCoordinationReceipt("acknowledged");
            module.pendingResumeRestore=false;module.resumeRestoreApplied=false;
            module.resumeFrame=null;module.resumeReadyFrame=null;module.resumeFailure=null;module.failure=null;
            module.ClearResumeCoordination();
        }

        public static void AbortAcceptedResumeRestore(object token,string reason)
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null)return;
            if(module.resumeCoordinationToken!=null&&token!=null&&!ReferenceEquals(module.resumeCoordinationToken,token))
                reason="Animator resume abort token mismatch. "+reason;
            module.FailResumeCoordination(reason);
        }

        private int AdvanceResumeRestore(object token,Hpmv.InjectorServer server,Hpmv.InputData acceptedInput,long epoch,long exchange,
            int originFrame,int originPhase,int targetPhase,int currentPhase,int unityFrame,bool mayArmFinalization)
        {
            if(!pendingResumeRestore)return 0;
            if(resumeFailure!=null||resumeRestoreStage<1)
                throw new InvalidOperationException("Animator resume restoration is already invalid: "+resumeFailure);
            if(token==null||server==null||acceptedInput==null||!ReferenceEquals(server,Hpmv.Injector.Server))
                throw new InvalidOperationException("Animator resume coordination has invalid accepted-input provenance.");
            if(unityFrame!=Time.frameCount||currentPhase!=Hpmv.Injector.Server.CurrentFrameData.FramesSinceLastNoPhysicsFrame)
                throw new InvalidOperationException("Animator resume coordination callback timing changed during dispatch.");
            if(originFrame!=resumeFrame.Frame||originFrame!=resumeReadyFrame.Frame||originPhase<0||originPhase>=6||targetPhase<0||targetPhase>=6)
                throw new InvalidOperationException("Animator resume coordination origin does not match the pending checkpoint.");
            if(resumeCoordinationToken==null)
            {
                resumeCoordinationToken=token;resumeCoordinationServer=server;resumeCoordinationInput=acceptedInput;
                resumeCoordinationEpoch=epoch;resumeCoordinationExchange=exchange;
                resumeCoordinationOriginFrame=originFrame;resumeCoordinationOriginPhase=originPhase;resumeCoordinationTargetPhase=targetPhase;
            }
            else if(!ReferenceEquals(resumeCoordinationToken,token)||!ReferenceEquals(resumeCoordinationServer,server)||
                !ReferenceEquals(resumeCoordinationInput,acceptedInput)||resumeCoordinationEpoch!=epoch||
                resumeCoordinationExchange!=exchange||resumeCoordinationOriginFrame!=originFrame||
                resumeCoordinationOriginPhase!=originPhase||resumeCoordinationTargetPhase!=targetPhase)
                throw new InvalidOperationException("Animator resume coordination identity changed while restoration was held.");
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Animator resume staging requires the main pause.");
            int finalizationPhase=targetPhase;
            int stageAPhase=(finalizationPhase+4)%6;
            if(resumeRestoreStage==1)
            {
                if(currentPhase!=stageAPhase){resumeStageAHolds++;return 1;}
                if(!ObserveResumePrefix(originFrame,"replay-pre-restore"))
                    throw new InvalidOperationException("Animator replay pre-restore tuple could not be observed.");
                ValidateConfiguration(resumeReadyFrame,"resume-stage-a");
                RestoreControllerMemory(resumeReadyFrame,"resume-stage-a");
                OverrideClipPlayables(resumeReadyFrame,"resume-stage-a");
                RestoreTargetNullClips(resumeReadyFrame,"resume-stage-a");
                resumeStageAUnityFrame=unityFrame;resumeRestoreStage=2;resumeStageAHolds++;
                lastResumeCoordination=ResumeCoordinationReceipt("after-stage-a");
                return 1;
            }
            if(resumeRestoreStage==2)
            {
                if(unityFrame<=resumeStageAUnityFrame){resumeStageAHolds++;return 1;}
                if(unityFrame!=resumeStageAUnityFrame+1)
                    throw new InvalidOperationException("Animator resume Stage A did not receive exactly one paused Unity maintenance frame.");
                if(currentPhase!=(stageAPhase+1)%6)
                    throw new InvalidOperationException("Animator resume Stage B did not follow the aligned six-frame scheduler phase.");
                RestorePlayableTimes(resumeReadyFrame,"resume-stage-b-before-transition");
                NormalizeSettledEndTransitions(resumeReadyFrame,"resume-stage-b",false);
                resumeStageBUnityFrame=unityFrame;resumeRestoreStage=3;resumeStageBHolds++;
                lastResumeCoordination=ResumeCoordinationReceipt("after-stage-b");
                return 1;
            }
            if(resumeRestoreStage==3)
            {
                if(unityFrame<=resumeStageBUnityFrame){resumeStageBHolds++;return 1;}
                if(unityFrame!=resumeStageBUnityFrame+1)
                    throw new InvalidOperationException("Animator resume Stage B did not receive exactly one paused Unity maintenance frame.");
                if(!mayArmFinalization||currentPhase!=finalizationPhase)
                    throw new InvalidOperationException("Animator finalization did not reach the aligned original resume phase.");
                resumeArmedUnityFrame=unityFrame;resumeArmedPhase=currentPhase;resumeRestoreStage=4;
                resumeFinalizationArms++;lastResumeCoordination=ResumeCoordinationReceipt("finalization-armed");
                return 2;
            }
            if(resumeRestoreStage==4&&mayArmFinalization&&unityFrame==resumeArmedUnityFrame&&currentPhase==resumeArmedPhase)return 2;
            throw new InvalidOperationException("Animator resume restoration was advanced from an invalid stage.");
        }

        private object ResumeCoordinationReceipt(string phase)
        {
            return new Dictionary<string,object>{{"phase",phase},{"stage",resumeRestoreStage},
                {"tokenIdentity",resumeCoordinationToken==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(resumeCoordinationToken)},
                {"serverIdentity",resumeCoordinationServer==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(resumeCoordinationServer)},
                {"acceptedInputIdentity",resumeCoordinationInput==null?0:System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(resumeCoordinationInput)},
                {"epoch",resumeCoordinationEpoch},{"exchange",resumeCoordinationExchange},
                {"originFrame",resumeCoordinationOriginFrame},{"observedRpcPhase",resumeCoordinationOriginPhase},
                {"targetPhase",resumeCoordinationTargetPhase},
                {"stageAUnityFrame",resumeStageAUnityFrame},{"stageBUnityFrame",resumeStageBUnityFrame},
                {"armedUnityFrame",resumeArmedUnityFrame},{"armedPhase",resumeArmedPhase},
                {"currentUnityFrame",Time.frameCount},
                {"currentPhase",Hpmv.Injector.Server==null?-1:Hpmv.Injector.Server.CurrentFrameData.FramesSinceLastNoPhysicsFrame}};
        }

        private void FailResumeCoordination(string reason)
        {
            if(String.IsNullOrEmpty(resumeFailure))resumeFailure=reason;
            failure=resumeFailure;resumeRestoreStage=-1;
            chefRandomizeMode=ChefRandomizeMode.Faulted;
            if(chefRandomizeFailure==null)
                chefRandomizeFailure=new Dictionary<string,object>{{"message","Animator resume coordination failed while chef callback replay was staged."},
                    {"mode",chefRandomizeMode.ToString()},{"referenceCursor",replayChefRandomizeCursor},
                    {"referenceLimit",replayChefRandomizeLimit},{"referenceCount",chefRandomizeReference.Count},
                    {"detail",reason}};
            lastResumeCoordination=ResumeCoordinationReceipt("failed");
            StateInvalidityManager.InvalidReason="AUTHORING_ANIMATOR_RESUME_FAILED: "+resumeFailure;
            NativeSessionBridge.ForceNeutral("animator-resume-failed");
        }

        private void ClearResumeCoordination()
        {
            resumeRestoreStage=0;resumeStageAUnityFrame=-1;resumeStageBUnityFrame=-1;
            resumeArmedUnityFrame=-1;resumeArmedPhase=-1;resumeCoordinationToken=null;
            resumeCoordinationServer=null;resumeCoordinationInput=null;resumeCoordinationEpoch=0;
            resumeCoordinationExchange=0;resumeCoordinationOriginFrame=-1;resumeCoordinationOriginPhase=-1;resumeCoordinationTargetPhase=-1;
        }

        public static void BeforeAuthoringResume()
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null||!NativeSessionBridge.KitchenReady||module.warpEligible||
                !TimeManager.IsPaused(TimeManager.PauseLayer.Main))return;
            bool hasResumeFrame=module.resumeFrame!=null,hasResumeReadyFrame=module.resumeReadyFrame!=null;
            if(hasResumeFrame!=hasResumeReadyFrame||module.pendingResumeRestore!=(hasResumeFrame&&hasResumeReadyFrame))
            {
                module.resumePrefixObserverFailures++;
                module.resumePrefixObserverFailure="Animator resume-prefix pending boundary/resume-ready invariant failed.";
                module.lastResumePrefixObservation=new Dictionary<string,object>{{"phase","classification"},
                    {"pendingResumeRestore",module.pendingResumeRestore},{"resumeFrame",module.resumeFrame==null?-1:module.resumeFrame.Frame},
                    {"resumeReadyFrame",module.resumeReadyFrame==null?-1:module.resumeReadyFrame.Frame},
                    {"error",module.resumePrefixObserverFailure}};
                throw new InvalidOperationException(module.resumePrefixObserverFailure);
            }
            bool replay=module.pendingResumeRestore;
            if(!replay&&module.history.Count==0)return;
            int frame=Hpmv.Injector.Server.CurrentFrameData.FrameNumber;
            // NativeKitchenCheckpoint already rejects output frame zero because
            // more than one advancing engine state can share that synthetic
            // logical frame during startup. Do not manufacture an Animator
            // resume template (or an observer fault) for the same ineligible
            // boundary.
            if(!replay&&frame==0)return;
            if(!replay)
            {
                module.ObserveResumePrefix(frame,"reference-prefix");
                module.ObserveResumePrefix(frame,"reference-post-observer");
                return;
            }
            try
            {
                if(module.resumeRestoreStage!=4||module.resumeCoordinationToken==null||
                    Time.frameCount!=module.resumeArmedUnityFrame||
                    Hpmv.Injector.Server.CurrentFrameData.FramesSinceLastNoPhysicsFrame!=module.resumeArmedPhase||
                    module.resumeArmedPhase!=module.resumeCoordinationTargetPhase)
                    throw new InvalidOperationException("Helpers.Resume did not match the armed Animator finalization callback.");
                FrameState ready=module.resumeReadyFrame;
                module.ValidateConfiguration(ready,"before-authoring-resume-ready");
                if(ready.RequiresFinalControllerRestore)
                    module.RestoreControllerMemory(ready,"before-authoring-resume-ready-scheduled");
                module.RestoreMixerGraph(ready,"before-authoring-resume-ready");
                module.RestorePlayableTimes(ready,"before-authoring-resume-ready");
                module.NormalizeSettledEndTransitions(ready,"resume-final-owner-verification",true);
                module.ObserveControllerInput(ready,"before-authoring-resume-ready",true);
                module.ObserveTransitionTopology(ready,"before-authoring-resume-ready",true);
                long poseAssignmentsBefore=module.poseAssignments;
                module.ApplyPose(ready,"before-authoring-resume-ready");
                module.lastPoseRestore=new Dictionary<string,object>{{"phase","before-authoring-resume-ready"},
                    {"frame",ready.Frame},{"capturedTransforms",ready.Animators.Sum(value=>value.Transforms.Length)},
                    {"assignments",module.poseAssignments-poseAssignmentsBefore}};
                module.RestoreUnityRandomState(ready,"before-authoring-resume-ready");
                if(!module.ObserveResumePrefix(frame,"replay-post-restore",true))
                    throw new InvalidOperationException("Resume-ready Animator tuple could not be verified at the replay resume prefix.");
                if(module.chefRandomizeMode!=ChefRandomizeMode.ReplayStaged||
                    module.replayChefRandomizeCursor!=ready.ChefRandomizeEventCount)
                    module.FailChefRandomize("Chef callback replay staging changed before the final resume-ready tuple was verified.",null,
                        new Dictionary<string,object>{{"resumeReadyCount",ready.ChefRandomizeEventCount}});
                if(module.replayReference.Count==0)
                {
                    if(module.replayChefRandomizeCursor!=module.chefRandomizeReference.Count)
                        module.FailChefRandomize("A zero-length replay cannot safely return to recording with an unused callback-stream tail.",null,null);
                    module.chefRandomizeMode=ChefRandomizeMode.Record;
                }
                else module.chefRandomizeMode=ChefRandomizeMode.ReplayActive;
                module.resumeRestoreApplied=true;module.resumeRestoreStage=5;module.resumeReadyRestores++;
                module.lastResumeCoordination=module.ResumeCoordinationReceipt("final-tuple-verified");
                module.resumeFailure=null;module.failure=null;
            }
            catch(Exception error)
            {
                module.resumeFailure=error.ToString();module.failure=module.resumeFailure;
                module.chefRandomizeMode=ChefRandomizeMode.Faulted;
                if(module.chefRandomizeFailure==null)
                    module.chefRandomizeFailure=new Dictionary<string,object>{{"message","Final Animator resume-ready restoration failed."},
                        {"mode",module.chefRandomizeMode.ToString()},{"referenceCursor",module.replayChefRandomizeCursor},
                        {"referenceLimit",module.replayChefRandomizeLimit},{"referenceCount",module.chefRandomizeReference.Count},
                        {"detail",error.ToString()}};
                throw;
            }
        }

        public static void AfterAuthoringResume()
        {
            ChefAnimatorCheckpointModule module=active;
            if(module==null||!module.pendingResumeRestore)return;
            if(!module.resumeRestoreApplied||module.resumeRestoreStage!=5||TimeManager.IsPaused(TimeManager.PauseLayer.Main))
            {
                module.resumeCompletionFailures++;
                module.resumeFailure="Helpers.Resume returned without completing the pending resume-ready Animator restore and main-pause release.";
                module.failure=module.resumeFailure;
                module.chefRandomizeMode=ChefRandomizeMode.Faulted;
                if(module.chefRandomizeFailure==null)
                    module.chefRandomizeFailure=new Dictionary<string,object>{{"message",module.resumeFailure},
                        {"mode",module.chefRandomizeMode.ToString()},{"referenceCursor",module.replayChefRandomizeCursor},
                        {"referenceLimit",module.replayChefRandomizeLimit},{"referenceCount",module.chefRandomizeReference.Count}};
                module.lastResumeCompletion=new Dictionary<string,object>{{"frame",module.resumeFrame==null?-1:module.resumeFrame.Frame},
                    {"restoreApplied",module.resumeRestoreApplied},{"mainPaused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},
                    {"completed",false},{"error",module.resumeFailure}};
                return;
            }
            int frame=module.resumeFrame.Frame;
            module.lastResumeCompletion=new Dictionary<string,object>{{"frame",frame},{"restoreApplied",true},{"mainPaused",false},{"completed",true}};
            module.resumeControllerRestores++;module.resumeCompletions++;
            module.resumeRestoreStage=6;
            module.lastResumeCoordination=module.ResumeCoordinationReceipt("helpers-resume-observed");
            module.resumeFailure=null;module.failure=null;
        }

        private bool ObserveResumePrefix(int frame,string phase)
        {
            return ObserveResumePrefix(frame,phase,false);
        }

        private bool ObserveResumePrefix(int frame,string phase,bool requireReferenceExact)
        {
            try
            {
                if(frame<0)throw new InvalidOperationException("Resume-prefix observation has no current output frame.");
                bool referencePrefix=phase=="reference-prefix";
                bool referencePost=phase=="reference-post-observer";
                bool replayPre=phase=="replay-pre-restore";
                bool replayPost=phase=="replay-post-restore";
                if(!referencePrefix&&!referencePost&&!replayPre&&!replayPost)
                    throw new InvalidOperationException("Unknown resume-prefix observation phase "+phase+".");
                Animator[] animators=FindAnimators();
                FrameState boundary;
                bool hasBoundary=history.TryGetValue(frame,out boundary);
                if(!hasBoundary)
                    throw new InvalidOperationException("Resume-prefix output frame "+frame+" has no exact Animator boundary checkpoint.");
                if((replayPre||replayPost)&&(resumeFrame==null||resumeFrame.Frame!=frame))
                    throw new InvalidOperationException("Replay resume-prefix frame does not match the pending Animator restore target: current="+
                        frame+", pending="+(resumeFrame==null?-1:resumeFrame.Frame)+".");
                ResumePrefixState referenceState;
                bool hasReference=resumePrefixReference.TryGetValue(frame,out referenceState);
                if(!referencePrefix&&!hasReference)
                    throw new InvalidOperationException("Resume-prefix comparison has no reference template for output frame "+frame+".");
                if(hasReference&&!ReferenceEquals(referenceState.Boundary,boundary))
                    throw new InvalidOperationException("Resume-prefix template is not linked to the current boundary object at output frame "+frame+".");

                // The full replay-pre traversal is empirically part of the
                // proven resume transaction. The native helper performs only
                // reads, hashing, and temporary allocation, but removing this
                // call changed the following paused maintenance result in the
                // T<-S matrix cell. Keep its ordering/timing effect fail-closed
                // until that dependency is explained independently.
                bool captureOwnerGraph=referencePrefix||replayPre||replayPost;
                FrameState snapshot=new FrameState { Frame=frame,
                    Animators=animators.Select(value=>Capture(value,true,true,captureOwnerGraph)).ToArray(),
                    RandomState=UnityEngine.Random.state,
                    ChefRandomizeEventCount=CurrentChefRandomizeEventCount() };
                object duplicateDifference=null,duplicateRandomStateDifference=null,duplicateControllerInputDifference=null;
                object duplicateTopologyDifference=null,duplicateMixerDifference=null,duplicateOwnerDifference=null;
                if(referencePrefix)
                {
                    resumePrefixReferenceCaptures++;
                    if(!hasReference)
                    {
                        referenceState=new ResumePrefixState { Boundary=boundary,Snapshot=snapshot };
                        resumePrefixReference.Add(frame,referenceState);hasReference=true;
                    }
                    else
                    {
                        duplicateDifference=FirstDifference(referenceState.Snapshot,snapshot);
                        duplicateRandomStateDifference=FirstRandomStateDifference(referenceState.Snapshot,snapshot);
                        duplicateControllerInputDifference=FirstControllerInputDifference(referenceState.Snapshot,snapshot);
                        duplicateTopologyDifference=FirstTransitionTopologyDifference(referenceState.Snapshot,snapshot);
                        duplicateMixerDifference=FirstMixerGraphDifference(referenceState.Snapshot,snapshot);
                        duplicateOwnerDifference=FirstOwnerGraphDifference(referenceState.Snapshot,snapshot);
                        if(duplicateDifference!=null||duplicateRandomStateDifference!=null||duplicateControllerInputDifference!=null||
                            duplicateTopologyDifference!=null||duplicateMixerDifference!=null||duplicateOwnerDifference!=null)
                        {
                            ambiguousResumePrefix.Add(frame);
                            resumePrefixObserverFailures++;
                            resumePrefixObserverFailure="Non-identical duplicate reference resume-prefix capture at output frame "+frame+".";
                        }
                    }
                }
                else if(referencePost)resumePrefixReferencePostCaptures++;
                else if(replayPre)resumePrefixReplayPreCaptures++;
                else if(replayPost)resumePrefixReplayPostCaptures++;

                FrameState reference=referenceState.Snapshot;
                object difference=FirstDifference(reference,snapshot);
                object randomStateDifference=FirstRandomStateDifference(reference,snapshot);
                object controllerInputDifference=FirstControllerInputDifference(reference,snapshot);
                object topologyDifference=FirstTransitionTopologyDifference(reference,snapshot);
                object mixerDifference=FirstMixerGraphDifference(reference,snapshot);
                object ownerDifference=captureOwnerGraph?FirstOwnerGraphDifference(reference,snapshot):null;
                if(replayPre)
                {
                    if(firstResumePrefixPreDifference==null)firstResumePrefixPreDifference=difference;
                    if(firstResumePrefixPreRandomStateDifference==null)firstResumePrefixPreRandomStateDifference=randomStateDifference;
                    if(firstResumePrefixPreControllerInputDifference==null)firstResumePrefixPreControllerInputDifference=controllerInputDifference;
                    if(firstResumePrefixPreTransitionTopologyDifference==null)firstResumePrefixPreTransitionTopologyDifference=topologyDifference;
                    if(firstResumePrefixPreMixerGraphDifference==null)firstResumePrefixPreMixerGraphDifference=mixerDifference;
                    if(firstResumePrefixPreOwnerGraphDifference==null)firstResumePrefixPreOwnerGraphDifference=ownerDifference;
                }
                else if(replayPost)
                {
                    if(firstResumePrefixPostDifference==null)firstResumePrefixPostDifference=difference;
                    if(firstResumePrefixPostRandomStateDifference==null)firstResumePrefixPostRandomStateDifference=randomStateDifference;
                    if(firstResumePrefixPostControllerInputDifference==null)firstResumePrefixPostControllerInputDifference=controllerInputDifference;
                    if(firstResumePrefixPostTransitionTopologyDifference==null)firstResumePrefixPostTransitionTopologyDifference=topologyDifference;
                    if(firstResumePrefixPostMixerGraphDifference==null)firstResumePrefixPostMixerGraphDifference=mixerDifference;
                    if(firstResumePrefixPostOwnerGraphDifference==null)firstResumePrefixPostOwnerGraphDifference=ownerDifference;
                }
                object observation=new Dictionary<string,object>{{"phase",phase},{"frame",frame},{"ownerGraphCaptured",captureOwnerGraph},
                    {"hasBoundary",true},{"hasReference",true},{"boundaryIdentityLinked",ReferenceEquals(referenceState.Boundary,boundary)},
                    {"differenceFromBoundary",FirstDifference(boundary,snapshot)},
                    {"randomStateDifferenceFromBoundary",FirstRandomStateDifference(boundary,snapshot)},
                    {"differenceFromReference",difference},{"randomStateDifferenceFromReference",randomStateDifference},
                    {"controllerInputDifferenceFromReference",controllerInputDifference},
                    {"transitionTopologyDifferenceFromReference",topologyDifference},{"mixerGraphDifferenceFromReference",mixerDifference},
                    {"ownerGraphDifferenceFromReference",ownerDifference},
                    {"duplicateReferenceDifference",duplicateDifference},
                    {"duplicateReferenceRandomStateDifference",duplicateRandomStateDifference},
                    {"duplicateReferenceControllerInputDifference",duplicateControllerInputDifference},
                    {"duplicateReferenceTransitionTopologyDifference",duplicateTopologyDifference},
                    {"duplicateReferenceMixerGraphDifference",duplicateMixerDifference},
                    {"duplicateReferenceOwnerGraphDifference",duplicateOwnerDifference},
                    {"animators",DescribeResumePrefix(snapshot.Animators)}};
                lastResumePrefixObservation=observation;
                if(referencePost)lastResumePrefixReferencePostObservation=observation;
                if(referencePost&&(difference!=null||randomStateDifference!=null||controllerInputDifference!=null||topologyDifference!=null||mixerDifference!=null||ownerDifference!=null))
                {
                    ambiguousResumePrefix.Add(frame);resumePrefixObserverFailures++;
                    resumePrefixObserverFailure="Reference resume-prefix observation perturbed its own complete tuple at output frame "+frame+".";
                }
                if(requireReferenceExact&&(difference!=null||randomStateDifference!=null||controllerInputDifference!=null||topologyDifference!=null||mixerDifference!=null||ownerDifference!=null))
                    throw new InvalidOperationException("Resume-prefix tuple is not exact to its linked reference at output frame "+frame+".");
                return true;
            }
            catch(Exception error)
            {
                resumePrefixObserverFailures++;
                resumePrefixObserverFailure=error.ToString();
                if(phase=="reference-prefix"||phase=="reference-post-observer")ambiguousResumePrefix.Add(frame);
                lastResumePrefixObservation=new Dictionary<string,object>{{"phase",phase},{"frame",frame},{"error",resumePrefixObserverFailure}};
                return false;
            }
        }

        private void CaptureFrame(int frame)
        {
            if(frame<0)return;
            Animator[] animators=FindAnimators();
            string identity=String.Join("|",animators.Select(value=>value.GetInstanceID().ToString()).ToArray());
            if(normalizedSceneIdentity!=identity)
            {
                NormalizeControllerStorage(animators,"scene-initialization");
                normalizedSceneIdentity=identity;
            }
            if(sceneIdentity!=identity)
            {
                var animatorIds=new HashSet<int>(animators.Select(value=>value.GetInstanceID()));
                chefRandomizeReference.Clear();trackedChefAnimatorIds.Clear();
                foreach(int animatorId in animatorIds)trackedChefAnimatorIds.Add(animatorId);
                history.Clear();replayReference.Clear();resumePrefixReference.Clear();ambiguousResumePrefix.Clear();sceneIdentity=identity;warpTarget=-1;warpEligible=false;
                scheduledResumeReadyCaptureFrame=-1;scheduledResumeReadyLastObservedFrame=-1;
                scheduledResumeReadyCaptureFailure=null;
                ownerGraphSceneCaptureBytes=0;
                restoreApplied=false;pendingResumeRestore=false;resumeRestoreApplied=false;resumeFrame=null;resumeReadyFrame=null;
                ClearResumeCoordination();
                failure=null;resumeFailure=null;resumePrefixObserverFailure=null;
                firstReplayDifference=null;firstRandomStateReplayDifference=null;firstRandomStateReplayPreCorrectionDifference=null;
                firstControllerInputReplayDifference=null;firstTransitionTopologyReplayDifference=null;
                firstMixerGraphReplayDifference=null;firstOwnerGraphReplayDifference=null;
                firstResumePrefixPreDifference=null;firstResumePrefixPreControllerInputDifference=null;
                firstResumePrefixPreRandomStateDifference=null;firstResumePrefixPreTransitionTopologyDifference=null;
                firstResumePrefixPreMixerGraphDifference=null;firstResumePrefixPreOwnerGraphDifference=null;
                firstResumePrefixPostDifference=null;firstResumePrefixPostControllerInputDifference=null;
                firstResumePrefixPostRandomStateDifference=null;firstResumePrefixPostTransitionTopologyDifference=null;
                firstResumePrefixPostMixerGraphDifference=null;firstResumePrefixPostOwnerGraphDifference=null;
                lastRestore=null;lastControllerRestore=null;lastMixerGraphRestore=null;lastConfigurationObservation=null;
                lastPoseRestore=null;lastPoseFailure=null;lastProbe=null;lastControllerProbe=null;
                lastResumePrefixObservation=null;lastResumePrefixReferencePostObservation=null;lastResumeCompletion=null;
                lastSettledEndTransitionNormalize=null;lastPlayableTimeRestore=null;lastTargetNullClipRestore=null;
                lastOverrideClipRestore=null;lastResumeCoordination=null;lastUnityRandomStateRestore=null;
                lastUnityRandomBoundaryCorrection=null;lastReplayPrefixCommit=null;
                lastScheduledResumeReadyCapture=null;
                controllerInputRestoreObservations.Clear();transitionTopologyRestoreObservations.Clear();
                mixerGraphRestoreObservations.Clear();settledEndTransitionNormalizeObservations.Clear();
                playableTimeRestoreObservations.Clear();targetNullClipRestoreObservations.Clear();
                overrideClipRestoreObservations.Clear();randomizeAnimParamObservations.Clear();
                replayFrameComparisons=0;resumePrefixReferenceCaptures=0;resumePrefixReferencePostCaptures=0;
                resumePrefixReplayPreCaptures=0;resumePrefixReplayPostCaptures=0;resumePrefixObserverFailures=0;
                scheduledResumeReadyCaptureArms=0;scheduledResumeReadyCaptureTriggers=0;
                chefRandomizeMode=ChefRandomizeMode.Record;replayChefRandomizeCursor=0;replayChefRandomizeLimit=0;
                firstChefRandomizeReplayDifference=null;chefRandomizeFailure=null;
            }
            else
            {
                trackedChefAnimatorIds.Clear();
                foreach(Animator animator in animators)trackedChefAnimatorIds.Add(animator.GetInstanceID());
            }
            FrameState prior;
            if(history.TryGetValue(frame,out prior))
            {
                duplicateCaptures++;return;
            }
            FrameState snapshot=new FrameState { Frame=frame,
                Animators=animators.Select(value=>Capture(value,true,true,false)).ToArray(),
                RandomState=UnityEngine.Random.state,
                ChefRandomizeEventCount=CurrentChefRandomizeEventCount() };
            FrameState expected;
            if(replayReference.TryGetValue(frame,out expected))
            {
                replayFrameComparisons++;
                if(firstReplayDifference==null)firstReplayDifference=FirstDifference(expected,snapshot);
                if(firstChefRandomizeReplayDifference==null&&expected.ChefRandomizeEventCount!=snapshot.ChefRandomizeEventCount)
                    firstChefRandomizeReplayDifference=Difference(frame,null,"chefRandomizeEventCount",
                        expected.ChefRandomizeEventCount,snapshot.ChefRandomizeEventCount);
                if(expected.ChefRandomizeEventCount!=snapshot.ChefRandomizeEventCount)
                    FailChefRandomize("Replay chef RandomizeAnimParam callback count differs at an output boundary.",null,
                        firstChefRandomizeReplayDifference);
                object randomStatePreCorrection=FirstRandomStateDifference(expected,snapshot);
                if(firstRandomStateReplayPreCorrectionDifference==null)
                    firstRandomStateReplayPreCorrectionDifference=randomStatePreCorrection;
                if(randomStatePreCorrection!=null)
                {
                    object before=DescribeRandomState(snapshot.RandomState);
                    RestoreUnityRandomState(expected,"replay-output-boundary");
                    snapshot.RandomState=UnityEngine.Random.state;
                    unityRandomBoundaryCorrections++;
                    lastUnityRandomBoundaryCorrection=new Dictionary<string,object>{{"frame",frame},
                        {"before",before},{"target",DescribeRandomState(expected.RandomState)},
                        {"after",DescribeRandomState(snapshot.RandomState)},{"exact",SameRandomState(expected.RandomState,snapshot.RandomState)}};
                }
                if(firstRandomStateReplayDifference==null)firstRandomStateReplayDifference=FirstRandomStateDifference(expected,snapshot);
                if(firstControllerInputReplayDifference==null)
                    firstControllerInputReplayDifference=FirstControllerInputDifference(expected,snapshot);
                if(firstTransitionTopologyReplayDifference==null)
                    firstTransitionTopologyReplayDifference=FirstTransitionTopologyDifference(expected,snapshot);
                if(firstMixerGraphReplayDifference==null)
                    firstMixerGraphReplayDifference=FirstMixerGraphDifference(expected,snapshot);
                if(firstOwnerGraphReplayDifference==null)
                    firstOwnerGraphReplayDifference=FirstOwnerGraphDifference(expected,snapshot);
            }
            if(history.Count!=0&&frame<history.Keys.Last())
                throw new InvalidOperationException("Chef Animator output frame regressed without a completed rewind.");
            history.Add(frame,snapshot);captures++;
            if(chefRandomizeMode==ChefRandomizeMode.ReplayActive&&replayReference.Count!=0&&frame>=replayReference.Keys.Last())
            {
                int endpoint=replayReference.Keys.Last();
                if(frame!=endpoint||replayChefRandomizeCursor!=replayChefRandomizeLimit)
                    FailChefRandomize("Replay reached its final output boundary without consuming the exact callback interval.",null,
                        new Dictionary<string,object>{{"frame",frame},{"endpoint",endpoint}});
                if(replayChefRandomizeLimit!=chefRandomizeReference.Count)
                    FailChefRandomize("Replay cannot safely return to recording with an unused callback-stream tail.",null,null);
                chefRandomizeMode=ChefRandomizeMode.Record;
            }
        }

        private int CurrentChefRandomizeEventCount()
        {
            return chefRandomizeMode==ChefRandomizeMode.Record?chefRandomizeReference.Count:replayChefRandomizeCursor;
        }

        private void NormalizeControllerStorage(Animator[] animators,string phase)
        {
            var rows=new List<object>();
            foreach(Animator animator in animators)
            {
                uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(animator)).ToInt32());
                var receipt=new NativeControllerReceipt();
                int nativeOk=nativeControllerNormalize(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),ref receipt);
                bool exact=nativeOk==1&&receipt.Result==1&&receipt.DifferenceCount==0&&
                    receipt.FirstDifference==UInt32.MaxValue&&receipt.BlobSizeBefore==receipt.BlobSizeAfter;
                if(receipt.ApiVersion!=NativeAnimatorApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeControllerReceipt))||!exact)
                    throw new InvalidOperationException("Native Animator controller storage normalization failed at "+phase+" for "+
                        PathOf(animator.transform)+": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+
                        ", differences="+receipt.DifferenceCount+", first="+receipt.FirstDifference+".");
                rows.Add(new Dictionary<string,object>{{"path",PathOf(animator.transform)},
                    {"memoryBefore",Pointer(receipt.MemoryBefore)},{"memoryAfter",Pointer(receipt.MemoryAfter)},
                    {"blobBytes",receipt.BlobSizeAfter},{"hashBefore",receipt.HashBefore.ToString("X8")},
                    {"hashAfter",receipt.HashAfter.ToString("X8")},{"byteExact",true}});
                controllerNormalizations++;
            }
            lastControllerNormalization=new Dictionary<string,object>{{"phase",phase},{"animators",rows.ToArray()},
                {"scope","One-time per-Animator ownership normalization through Unity's native non-rehydrating controller-memory path; serialized state is verified byte-exact before capture."}};
        }

        private AnimatorState[] CaptureAll()
        {
            return CaptureAll(FindAnimators(),true);
        }

        private AnimatorState[] CaptureAll(Animator[] animators,bool captureTransforms)
        {
            return animators.Select(value=>Capture(value,captureTransforms)).ToArray();
        }

        private static Animator[] FindAnimators()
        {
            ServerChefSynchroniser[] chefs=(ServerChefSynchroniser[])UnityEngine.Object.FindObjectsOfType(typeof(ServerChefSynchroniser));
            if(chefs.Length!=4)throw new InvalidOperationException("Animator checkpoint requires exactly four live chefs.");
            var animators=new List<Animator>();
            foreach(ServerChefSynchroniser chef in chefs)
            {
                Animator[] found=chef.GetComponentsInChildren<Animator>(true);
                Animator[] owned=found.Where(value=>value!=null&&value.transform.parent==chef.transform).ToArray();
                if(owned.Length!=1)throw new InvalidOperationException("Each chef must have exactly one directly owned Animator: "+
                    PathOf(chef.transform)+"; direct="+String.Join(",",owned.Select(value=>PathOf(value.transform)).ToArray())+
                    "; descendants="+String.Join(",",found.Select(value=>PathOf(value.transform)).ToArray())+".");
                animators.Add(owned[0]);
            }
            if(animators.Select(value=>value.GetInstanceID()).Distinct().Count()!=4)
                throw new InvalidOperationException("Chef Animator identities are not distinct.");
            return animators.OrderBy(value=>PathOf(value.transform),StringComparer.Ordinal).ToArray();
        }

        private AnimatorState Capture(Animator animator,bool captureTransforms)
        {
            return Capture(animator,captureTransforms,!captureTransforms);
        }

        private AnimatorState Capture(Animator animator,bool captureTransforms,bool captureControllerMemory)
        {
            return Capture(animator,captureTransforms,captureControllerMemory,captureControllerMemory);
        }

        private AnimatorState Capture(Animator animator,bool captureTransforms,bool captureControllerMemory,bool captureOwnerGraph)
        {
            if(captureOwnerGraph&&!captureControllerMemory)
                throw new InvalidOperationException("Animator owner-graph capture requires the rest of the native tuple.");
            var parameters=new List<ParameterState>();
            foreach(AnimatorControllerParameter parameter in animator.parameters)
            {
                var value=new ParameterState { Hash=parameter.nameHash,Type=parameter.type };
                if(parameter.type==AnimatorControllerParameterType.Float)value.Float=animator.GetFloat(parameter.nameHash);
                else if(parameter.type==AnimatorControllerParameterType.Int)value.Int=animator.GetInteger(parameter.nameHash);
                else if(parameter.type==AnimatorControllerParameterType.Bool||parameter.type==AnimatorControllerParameterType.Trigger)value.Bool=animator.GetBool(parameter.nameHash);
                else throw new InvalidOperationException("Unsupported Animator parameter type "+parameter.type+".");
                parameters.Add(value);
            }
            var layers=new LayerState[animator.layerCount];
            for(int layer=0;layer<layers.Length;layer++)
            {
                AnimatorStateInfo state=animator.GetCurrentAnimatorStateInfo(layer);
                if(state.fullPathHash==0||Single.IsNaN(state.normalizedTime)||Single.IsInfinity(state.normalizedTime))
                    throw new InvalidOperationException("Animator layer state is invalid: "+PathOf(animator.transform)+" layer "+layer+".");
                layers[layer]=new LayerState { FullPathHash=state.fullPathHash,ShortNameHash=state.shortNameHash,TagHash=state.tagHash,
                    NormalizedTime=state.normalizedTime,Weight=animator.GetLayerWeight(layer),Loop=state.loop };
            }
            int descendantTransformCount=0,foreignTransformCount=0;
            TransformState[] transforms=captureTransforms?
                CaptureTransforms(animator,out descendantTransformCount,out foreignTransformCount):new TransformState[0];
            AnimatorState captured=new AnimatorState { Animator=animator,Controller=animator.runtimeAnimatorController,Avatar=animator.avatar,
                InstanceId=animator.GetInstanceID(),Path=PathOf(animator.transform),Enabled=animator.enabled,
                ApplyRootMotion=animator.applyRootMotion,Speed=animator.speed,UpdateMode=animator.updateMode,CullingMode=animator.cullingMode,
                Parameters=parameters.ToArray(),Layers=layers,
                Transforms=transforms,DescendantTransformCount=descendantTransformCount,ForeignTransformCount=foreignTransformCount,
                ControllerMemory=captureControllerMemory?CaptureControllerMemory(animator):new byte[0],
                ControllerInput=captureControllerMemory?CaptureControllerInput(animator):null,
                TransitionTopology=captureControllerMemory?CaptureTransitionTopology(animator):null,
                MixerGraph=captureControllerMemory?CaptureMixerGraph(animator):null,
                OwnerGraph=captureOwnerGraph?CaptureOwnerGraph(animator):null };
            if(captureControllerMemory)ValidateNativeTuple(captured,captureOwnerGraph);
            return captured;
        }

        private static void ValidateNativeTuple(AnimatorState state,bool requireOwnerGraph)
        {
            ControllerInputState input=state.ControllerInput;TransitionTopologyState topology=state.TransitionTopology;
            MixerGraphState mixer=state.MixerGraph;OwnerGraphState owner=state.OwnerGraph;
            if(input==null||topology==null||mixer==null||(requireOwnerGraph&&owner==null))
                throw new InvalidOperationException("Animator native tuple is incomplete: "+state.Path+".");
            bool identities=input.Animator==mixer.Animator&&input.Controller==mixer.Controller&&input.ControllerConstant==mixer.ControllerConstant&&
                topology.Animator==mixer.Animator&&topology.Controller==mixer.Controller&&topology.LiveLayers==mixer.Descriptors;
            uint publicLayerCount=checked((uint)state.Layers.Length);
            bool layers=mixer.LayerCount==input.RecordCount&&mixer.LayerCount==topology.BlobLayerCount&&
                mixer.LayerCount==topology.GraphLayerCount&&mixer.LayerCount==input.OuterCount&&
                mixer.LayerCount==publicLayerCount&&topology.LiveLayerCount<=mixer.LayerCount;
            if(owner!=null)
            {
                identities=identities&&owner.Animator==mixer.Animator&&owner.Controller==mixer.Controller&&
                    owner.ControllerConstant==mixer.ControllerConstant&&owner.Descriptors==mixer.Descriptors;
                layers=layers&&owner.LayerCount==mixer.LayerCount;
            }
            if(!identities||!layers||(owner!=null&&owner.Graph==0))
                throw new InvalidOperationException("Animator native tuple identity/layer invariant failed: "+state.Path+".");
            ControllerStateOffsets(state.ControllerMemory,input.RecordCount);
        }

        private static TransformState[] CaptureTransforms(Animator animator,out int descendantCount,out int foreignCount)
        {
            Transform[] descendants=animator.GetComponentsInChildren<Transform>(true);
            descendantCount=descendants.Length;
            Transform[] owned=descendants.Where(value=>IsAnimationOwnedTransform(animator.transform,value)).ToArray();
            foreignCount=descendantCount-owned.Length;
            return owned
                .Select(value=>new TransformState { Transform=value,InstanceId=value.GetInstanceID(),Path=PathOf(value),
                    LocalPosition=value.localPosition,LocalRotation=value.localRotation,LocalScale=value.localScale })
                .OrderBy(value=>value.Path,StringComparer.Ordinal).ToArray();
        }

        private static bool IsAnimationOwnedTransform(Transform animatorRoot,Transform value)
        {
            // Runtime gameplay objects are parented below animated attachment
            // bones.  They inherit the bone pose, but they are not members of
            // the Animator's rig and can be semantically reconstructed by the
            // attachment/food restore path.  Crossing a nested network-entity
            // root therefore ends Animator pose ownership.  The Animator root
            // itself is deliberately excluded from this test because the chef
            // synchronisers live above or at the chef hierarchy boundary.
            for(Transform current=value;current!=null&&!ReferenceEquals(current,animatorRoot);current=current.parent)
                if(current.GetComponent<ServerWorldObjectSynchroniser>()!=null||
                    current.GetComponent<ClientWorldObjectSynchroniser>()!=null)
                    return false;
            return true;
        }

        private byte[] CaptureControllerMemory(Animator animator)
        {
            if(nativeControllerCapture==null)throw new InvalidOperationException("Native Animator controller capture is not bound.");
            uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(animator)).ToInt32());
            byte[] blob=new byte[512];NativeControllerReceipt receipt=new NativeControllerReceipt();int nativeOk;
            while(true)
            {
                GCHandle pinned=GCHandle.Alloc(blob,GCHandleType.Pinned);
                try{nativeOk=nativeControllerCapture(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),pinned.AddrOfPinnedObject(),(uint)blob.Length,ref receipt);}
                finally{pinned.Free();}
                if(nativeOk==1&&receipt.Result==1)break;
                if(receipt.Result!=6||receipt.BlobSizeBefore<=blob.Length||receipt.BlobSizeBefore>1024u*1024u)
                    throw new InvalidOperationException("Native Animator controller capture failed for "+PathOf(animator.transform)+": result="+receipt.Result+
                        ", error=0x"+receipt.LastError.ToString("X8")+", size="+receipt.BlobSizeBefore+".");
                blob=new byte[checked((int)receipt.BlobSizeBefore)];receipt=new NativeControllerReceipt();
            }
            if(receipt.ApiVersion!=NativeAnimatorApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeControllerReceipt))||
                receipt.BlobSizeBefore<0x20||receipt.BlobSizeBefore>blob.Length)
                throw new InvalidOperationException("Native Animator controller capture returned an invalid receipt for "+PathOf(animator.transform)+".");
            if(receipt.BlobSizeBefore!=blob.Length)Array.Resize(ref blob,checked((int)receipt.BlobSizeBefore));
            controllerCaptures++;controllerCaptureBytes+=blob.Length;return blob;
        }

        private ControllerInputState CaptureControllerInput(Animator animator)
        {
            if(nativeControllerInputCapture==null)throw new InvalidOperationException("Native Animator controller-input capture is not bound.");
            uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(animator)).ToInt32());
            byte[] bytes=new byte[512];NativeControllerInputReceipt receipt=new NativeControllerInputReceipt();int nativeOk;
            while(true)
            {
                GCHandle pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                try{nativeOk=nativeControllerInputCapture(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),pinned.AddrOfPinnedObject(),(uint)bytes.Length,ref receipt);}
                finally{pinned.Free();}
                if(nativeOk==1&&receipt.Result==1)break;
                if(receipt.Result!=6||receipt.ByteSize<=bytes.Length||receipt.ByteSize>1024u*1024u)
                    throw new InvalidOperationException("Native Animator controller-input capture failed for "+PathOf(animator.transform)+": result="+receipt.Result+
                        ", error=0x"+receipt.LastError.ToString("X8")+", size="+receipt.ByteSize+".");
                bytes=new byte[checked((int)receipt.ByteSize)];receipt=new NativeControllerInputReceipt();
            }
            uint expectedSize=checked(12u+receipt.RecordCount*24u);
            if(receipt.ApiVersion!=NativeAnimatorApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeControllerInputReceipt))||
                receipt.ByteSize!=expectedSize||receipt.ByteSize>bytes.Length)
                throw new InvalidOperationException("Native Animator controller-input capture returned an invalid receipt for "+PathOf(animator.transform)+".");
            if(receipt.ByteSize!=bytes.Length)Array.Resize(ref bytes,checked((int)receipt.ByteSize));
            uint hash=ByteHash(bytes);
            if(hash!=receipt.Hash)throw new InvalidOperationException("Native Animator controller-input hash mismatch for "+PathOf(animator.transform)+".");
            controllerInputCaptures++;controllerInputCaptureBytes+=bytes.Length;
            return new ControllerInputState { Animator=receipt.Animator.ToUInt32(),Controller=receipt.Controller.ToUInt32(),
                ControllerConstant=receipt.ControllerConstant.ToUInt32(),ControllerInput=receipt.ControllerInput.ToUInt32(),Records=receipt.Records.ToUInt32(),
                RecordCount=receipt.RecordCount,OuterCount=receipt.OuterCount,Hash=receipt.Hash,Bytes=bytes };
        }

        private void ObserveControllerInput(FrameState frame,string phase)
        {
            ObserveControllerInput(frame,phase,false);
        }

        private void ObserveControllerInput(FrameState frame,string phase,bool requireExact)
        {
            Animator[] current=FindAnimators();var rows=new List<object>();bool allExact=true;
            if(current.Length!=frame.Animators.Length)throw new InvalidOperationException("Chef Animator membership changed during controller-input observation.");
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];ControllerInputState expected=saved.ControllerInput;
                if(expected==null)throw new InvalidOperationException("Chef Animator checkpoint lacks controller-input state: "+saved.Path+".");
                ControllerInputState actual=CaptureControllerInput(current[i]);
                int difference=FirstByteDifference(expected.Bytes,actual.Bytes);
                bool identities=expected.Animator==actual.Animator&&expected.Controller==actual.Controller&&
                    expected.ControllerConstant==actual.ControllerConstant&&expected.ControllerInput==actual.ControllerInput&&expected.Records==actual.Records;
                bool shape=expected.RecordCount==actual.RecordCount&&expected.OuterCount==actual.OuterCount&&expected.Bytes.Length==actual.Bytes.Length;
                byte[] actualMemory=CaptureControllerMemory(current[i]);string semanticError=null;
                bool semanticEqual=shape&&AnimatorResumeSemanticState.ControllerInputsEqual(expected.Bytes,actual.Bytes,
                    PendingGotoStateGates(saved.ControllerMemory,expected.RecordCount),
                    PendingGotoStateGates(actualMemory,actual.RecordCount),out semanticError);
                bool exact=identities&&shape&&semanticEqual;allExact=allExact&&exact;
                rows.Add(new Dictionary<string,object>{{"path",saved.Path},{"exact",exact},{"byteExact",difference<0},
                    {"semanticEqual",semanticEqual},{"semanticError",semanticError},{"identitiesExact",identities},{"shapeExact",shape},
                    {"firstByteDifference",difference},{"expectedHash",expected.Hash.ToString("X8")},{"actualHash",actual.Hash.ToString("X8")},
                    {"expectedController",Pointer(expected.Controller)},{"actualController",Pointer(actual.Controller)},
                    {"expectedControllerConstant",Pointer(expected.ControllerConstant)},{"actualControllerConstant",Pointer(actual.ControllerConstant)},
                    {"expectedControllerInput",Pointer(expected.ControllerInput)},{"actualControllerInput",Pointer(actual.ControllerInput)},
                    {"expectedRecords",Pointer(expected.Records)},{"actualRecords",Pointer(actual.Records)},
                    {"expectedRecordCount",expected.RecordCount},{"actualRecordCount",actual.RecordCount},
                    {"expectedOuterCount",expected.OuterCount},{"actualOuterCount",actual.OuterCount},
                    {"expectedByteSize",expected.Bytes.Length},{"actualByteSize",actual.Bytes.Length}});
            }
            controllerInputRestoreObservations.Add(new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"allExact",allExact},{"animators",rows.ToArray()}});
            if(requireExact&&!allExact)throw new InvalidOperationException("Chef Animator ControllerInput is not exact at "+phase+".");
        }

        private TransitionTopologyState CaptureTransitionTopology(Animator animator)
        {
            if(nativeTransitionTopologyCapture==null)throw new InvalidOperationException("Native Animator transition-topology capture is not bound.");
            uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(animator)).ToInt32());
            byte[] bytes=new byte[128];NativeTransitionTopologyReceipt receipt=new NativeTransitionTopologyReceipt();int nativeOk;
            while(true)
            {
                GCHandle pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                try{nativeOk=nativeTransitionTopologyCapture(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),pinned.AddrOfPinnedObject(),(uint)bytes.Length,ref receipt);}
                finally{pinned.Free();}
                if(nativeOk==1&&receipt.Result==1)break;
                if(receipt.Result!=6||receipt.ByteSize<=bytes.Length||receipt.ByteSize>64u*28u)
                    throw new InvalidOperationException("Native Animator transition-topology capture failed for "+PathOf(animator.transform)+": result="+receipt.Result+
                        ", error=0x"+receipt.LastError.ToString("X8")+", size="+receipt.ByteSize+".");
                bytes=new byte[checked((int)receipt.ByteSize)];receipt=new NativeTransitionTopologyReceipt();
            }
            uint expectedSize=checked(receipt.LiveLayerCount*28u);
            if(receipt.ApiVersion!=NativeAnimatorApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeTransitionTopologyReceipt))||
                receipt.ByteSize!=expectedSize||receipt.ByteSize>bytes.Length||receipt.BlobLayerCount!=receipt.GraphLayerCount||
                receipt.LiveLayerCount>receipt.BlobLayerCount)
                throw new InvalidOperationException("Native Animator transition-topology capture returned an invalid receipt for "+PathOf(animator.transform)+".");
            if(receipt.ByteSize!=bytes.Length)Array.Resize(ref bytes,checked((int)receipt.ByteSize));
            uint hash=ByteHash(bytes);
            if(hash!=receipt.Hash)throw new InvalidOperationException("Native Animator transition-topology hash mismatch for "+PathOf(animator.transform)+".");
            transitionTopologyCaptures++;transitionTopologyCaptureBytes+=bytes.Length;
            return new TransitionTopologyState { Animator=receipt.Animator.ToUInt32(),Controller=receipt.Controller.ToUInt32(),
                ControllerMemory=receipt.ControllerMemory.ToUInt32(),ControllerGraphMemory=receipt.ControllerGraphMemory.ToUInt32(),
                LiveLayers=receipt.LiveLayers.ToUInt32(),BlobCapacity=receipt.BlobCapacity,BlobLayerCount=receipt.BlobLayerCount,
                GraphLayerCount=receipt.GraphLayerCount,LiveLayerCount=receipt.LiveLayerCount,Hash=receipt.Hash,
                TopologyMismatchCount=receipt.TopologyMismatchCount,Bytes=bytes };
        }

        private void ObserveTransitionTopology(FrameState frame,string phase)
        {
            ObserveTransitionTopology(frame,phase,false);
        }

        private void ObserveTransitionTopology(FrameState frame,string phase,bool requireExact)
        {
            Animator[] current=FindAnimators();var rows=new List<object>();bool allExact=true;
            if(current.Length!=frame.Animators.Length)throw new InvalidOperationException("Chef Animator membership changed during transition-topology observation.");
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];TransitionTopologyState expected=saved.TransitionTopology;
                if(expected==null)throw new InvalidOperationException("Chef Animator checkpoint lacks transition-topology state: "+saved.Path+".");
                TransitionTopologyState actual=CaptureTransitionTopology(current[i]);
                int difference=FirstByteDifference(expected.Bytes,actual.Bytes);
                bool identities=expected.Animator==actual.Animator&&expected.Controller==actual.Controller&&
                    expected.ControllerMemory==actual.ControllerMemory&&expected.ControllerGraphMemory==actual.ControllerGraphMemory&&expected.LiveLayers==actual.LiveLayers;
                bool shape=expected.BlobCapacity==actual.BlobCapacity&&expected.BlobLayerCount==actual.BlobLayerCount&&
                    expected.GraphLayerCount==actual.GraphLayerCount&&expected.LiveLayerCount==actual.LiveLayerCount&&expected.Bytes.Length==actual.Bytes.Length;
                bool exact=identities&&shape&&difference<0;allExact=allExact&&exact;
                rows.Add(new Dictionary<string,object>{{"path",saved.Path},{"exact",exact},{"identitiesExact",identities},{"shapeExact",shape},
                    {"firstByteDifference",difference},{"expectedHash",expected.Hash.ToString("X8")},{"actualHash",actual.Hash.ToString("X8")},
                    {"expectedTopologyMismatchCount",expected.TopologyMismatchCount},{"actualTopologyMismatchCount",actual.TopologyMismatchCount},
                    {"expectedControllerMemory",Pointer(expected.ControllerMemory)},{"actualControllerMemory",Pointer(actual.ControllerMemory)},
                    {"expectedControllerGraphMemory",Pointer(expected.ControllerGraphMemory)},{"actualControllerGraphMemory",Pointer(actual.ControllerGraphMemory)},
                    {"expectedLiveLayers",Pointer(expected.LiveLayers)},{"actualLiveLayers",Pointer(actual.LiveLayers)},
                    {"expectedBlobCapacity",expected.BlobCapacity},{"actualBlobCapacity",actual.BlobCapacity},
                    {"expectedLayers",DescribeTransitionTopologyLayers(expected.Bytes)},{"actualLayers",DescribeTransitionTopologyLayers(actual.Bytes)}});
            }
            transitionTopologyRestoreObservations.Add(new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"allExact",allExact},{"animators",rows.ToArray()}});
            if(requireExact&&!allExact)throw new InvalidOperationException("Chef Animator transition topology is not exact at "+phase+".");
        }

        private MixerGraphState CaptureMixerGraph(Animator animator)
        {
            if(nativeMixerGraphCapture==null)throw new InvalidOperationException("Native Animator mixer-graph capture is not bound.");
            uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(animator)).ToInt32());
            byte[] bytes=new byte[4096];NativeMixerGraphReceipt receipt=new NativeMixerGraphReceipt();int nativeOk;
            while(true)
            {
                GCHandle pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                try{nativeOk=nativeMixerGraphCapture(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),pinned.AddrOfPinnedObject(),(uint)bytes.Length,ref receipt);}
                finally{pinned.Free();}
                if(nativeOk==1&&receipt.Result==1)break;
                if(receipt.Result!=6||receipt.ByteSize<=bytes.Length||receipt.ByteSize>2u*1024u*1024u)
                    throw new InvalidOperationException("Native Animator mixer-graph capture failed for "+PathOf(animator.transform)+": result="+receipt.Result+
                        ", error=0x"+receipt.LastError.ToString("X8")+", size="+receipt.ByteSize+", mismatches="+receipt.MismatchCount+".");
                bytes=new byte[checked((int)receipt.ByteSize)];receipt=new NativeMixerGraphReceipt();
            }
            uint expectedSize=checked(receipt.RecordCount*MixerGraphRecordSize);
            if(receipt.ApiVersion!=NativeAnimatorApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeMixerGraphReceipt))||
                receipt.ByteSize!=expectedSize||receipt.ByteSize>bytes.Length||receipt.RecordCount==0||receipt.MismatchCount!=0)
                throw new InvalidOperationException("Native Animator mixer-graph capture returned an invalid receipt for "+PathOf(animator.transform)+".");
            if(receipt.ByteSize!=bytes.Length)Array.Resize(ref bytes,checked((int)receipt.ByteSize));
            uint hash=ByteHash(bytes);
            if(hash!=receipt.Hash)throw new InvalidOperationException("Native Animator mixer-graph hash mismatch for "+PathOf(animator.transform)+".");
            mixerGraphCaptures++;mixerGraphCaptureBytes+=bytes.Length;
            return new MixerGraphState { Animator=receipt.Animator.ToUInt32(),Controller=receipt.Controller.ToUInt32(),
                ControllerConstant=receipt.ControllerConstant.ToUInt32(),Descriptors=receipt.Descriptors.ToUInt32(),
                LayerCount=receipt.LayerCount,RecordCount=receipt.RecordCount,Hash=receipt.Hash,Bytes=bytes };
        }

        private OwnerGraphState CaptureOwnerGraph(Animator animator)
        {
            if(nativeOwnerGraphCapture==null)throw new InvalidOperationException("Native Animator owner-graph capture is not bound.");
            uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(animator)).ToInt32());
            byte[] bytes=new byte[65536];NativeOwnerGraphReceipt receipt=new NativeOwnerGraphReceipt();int nativeOk;
            while(true)
            {
                GCHandle pinned=GCHandle.Alloc(bytes,GCHandleType.Pinned);
                try{nativeOk=nativeOwnerGraphCapture(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),pinned.AddrOfPinnedObject(),(uint)bytes.Length,ref receipt);}
                finally{pinned.Free();}
                if(nativeOk==1&&receipt.Result==1)break;
                if(receipt.Result!=6||receipt.ByteSize<=bytes.Length||receipt.ByteSize>MaximumOwnerGraphBytes)
                    throw new InvalidOperationException("Native Animator owner-graph capture failed for "+PathOf(animator.transform)+": result="+receipt.Result+
                        ", error=0x"+receipt.LastError.ToString("X8")+", size="+receipt.ByteSize+", failureRecord="+receipt.FailureRecord+".");
                bytes=new byte[checked((int)receipt.ByteSize)];receipt=new NativeOwnerGraphReceipt();
            }
            uint expectedSize=checked(receipt.RecordCount*OwnerGraphRecordSize);
            if(receipt.ApiVersion!=NativeAnimatorApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeOwnerGraphReceipt))||
                receipt.ByteSize!=expectedSize||receipt.ByteSize>bytes.Length||receipt.RecordCount==0||receipt.Graph==UIntPtr.Zero||
                receipt.FailureRecord!=UInt32.MaxValue)
                throw new InvalidOperationException("Native Animator owner-graph capture returned an invalid receipt for "+PathOf(animator.transform)+".");
            if(receipt.ByteSize!=bytes.Length)Array.Resize(ref bytes,checked((int)receipt.ByteSize));
            uint hash=ByteHash(bytes);
            if(hash!=receipt.Hash)throw new InvalidOperationException("Native Animator owner-graph hash mismatch for "+PathOf(animator.transform)+".");
            if(ownerGraphSceneCaptureBytes>MaximumOwnerGraphCaptureBytes-bytes.Length)
                throw new InvalidOperationException("Native Animator owner-graph capture exceeded its bounded 256 MiB safety budget for the current scene.");
            ownerGraphCaptures++;ownerGraphCaptureBytes+=bytes.Length;ownerGraphSceneCaptureBytes+=bytes.Length;
            return new OwnerGraphState { Animator=receipt.Animator.ToUInt32(),Controller=receipt.Controller.ToUInt32(),
                ControllerConstant=receipt.ControllerConstant.ToUInt32(),Descriptors=receipt.Descriptors.ToUInt32(),Graph=receipt.Graph.ToUInt32(),
                LayerCount=receipt.LayerCount,RecordCount=receipt.RecordCount,Hash=receipt.Hash,GraphDirty58=receipt.GraphDirty58,Bytes=bytes };
        }

        private void RestoreMixerGraph(FrameState frame,string phase)
        {
            if(nativeMixerGraphRestore==null)throw new InvalidOperationException("Native Animator mixer-graph restore is not bound.");
            Animator[] current=FindAnimators();var rows=new List<object>();bool allExact=true;
            if(current.Length!=frame.Animators.Length)throw new InvalidOperationException("Chef Animator membership changed during mixer-graph restore.");
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];MixerGraphState expected=saved.MixerGraph;
                if(expected==null||expected.Bytes==null||expected.Bytes.Length==0||expected.Bytes.Length%MixerGraphRecordSize!=0)
                    throw new InvalidOperationException("Chef Animator checkpoint lacks mixer-graph state: "+saved.Path+".");
                Animator live=current[i];
                if(live.GetInstanceID()!=saved.InstanceId||PathOf(live.transform)!=saved.Path||!ReferenceEquals(live,saved.Animator))
                    throw new InvalidOperationException("Chef Animator incarnation changed during mixer-graph restore: "+saved.Path+".");
                uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(live)).ToInt32());
                NativeMixerGraphReceipt receipt=new NativeMixerGraphReceipt();int nativeOk;
                GCHandle pinned=GCHandle.Alloc(expected.Bytes,GCHandleType.Pinned);
                try{nativeOk=nativeMixerGraphRestore(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),pinned.AddrOfPinnedObject(),
                    (uint)expected.Bytes.Length,ref receipt);}
                finally{pinned.Free();}
                bool identities=expected.Animator==receipt.Animator.ToUInt32()&&expected.Controller==receipt.Controller.ToUInt32()&&
                    expected.ControllerConstant==receipt.ControllerConstant.ToUInt32()&&expected.Descriptors==receipt.Descriptors.ToUInt32();
                bool shape=expected.LayerCount==receipt.LayerCount&&expected.RecordCount==receipt.RecordCount&&
                    receipt.ByteSize==expected.Bytes.Length;
                bool exact=nativeOk==1&&receipt.Result==1&&receipt.ApiVersion==NativeAnimatorApiVersion&&
                    receipt.StructSize==(uint)Marshal.SizeOf(typeof(NativeMixerGraphReceipt))&&identities&&shape&&
                    receipt.Hash==expected.Hash&&receipt.MismatchCount==0;
                allExact=allExact&&exact;
                rows.Add(new Dictionary<string,object>{{"path",saved.Path},{"exact",exact},{"identitiesExact",identities},
                    {"shapeExact",shape},{"result",receipt.Result},{"lastError","0x"+receipt.LastError.ToString("X8")},
                    {"expectedHash",expected.Hash.ToString("X8")},{"actualHash",receipt.Hash.ToString("X8")},
                    {"recordCount",receipt.RecordCount},{"mismatchCount",receipt.MismatchCount},
                    {"restoredWeightCount",receipt.RestoredWeightCount},
                    {"mismatchRecordIndex",receipt.MismatchRecordIndex==UInt32.MaxValue?-1:(long)receipt.MismatchRecordIndex},
                    {"mismatchByteOffset",receipt.MismatchByteOffset==UInt32.MaxValue?-1:(long)receipt.MismatchByteOffset},
                    {"expectedWord","0x"+receipt.ExpectedWord.ToString("X8")},{"actualWord","0x"+receipt.ActualWord.ToString("X8")}});
                if(!exact)
                {
                    lastMixerGraphRestore=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"allExact",false},{"animators",rows.ToArray()}};
                    throw new InvalidOperationException("Native Animator mixer-graph restore failed at "+phase+" for "+saved.Path+
                        ": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+", mismatches="+receipt.MismatchCount+
                        ", record="+receipt.MismatchRecordIndex+", byte="+receipt.MismatchByteOffset+
                        ", expected=0x"+receipt.ExpectedWord.ToString("X8")+", actual=0x"+receipt.ActualWord.ToString("X8")+".");
                }
                mixerGraphRestores++;mixerWeightWrites+=receipt.RestoredWeightCount;
            }
            object observation=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"allExact",allExact},{"animators",rows.ToArray()}};
            mixerGraphRestoreObservations.Add(observation);lastMixerGraphRestore=observation;
        }

        private void OverrideClipPlayables(FrameState frame,string phase)
        {
            if(nativeOverrideClipPlayables==null)
                throw new InvalidOperationException("Native Animator OverrideClipPlayables helper is not bound.");
            Animator[] current=FindAnimators();var rows=new List<object>();bool allAccepted=true;
            if(current.Length!=frame.Animators.Length)
                throw new InvalidOperationException("Chef Animator membership changed during clip-binding restoration.");
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];Animator live=current[i];OwnerGraphState expected=saved.OwnerGraph;
                if(expected==null||expected.Bytes==null||expected.Bytes.Length==0||expected.Bytes.Length%OwnerGraphRecordSize!=0)
                    throw new InvalidOperationException("Chef Animator checkpoint lacks an owner graph: "+saved.Path+".");
                if(live.GetInstanceID()!=saved.InstanceId||PathOf(live.transform)!=saved.Path||!ReferenceEquals(live,saved.Animator))
                    throw new InvalidOperationException("Chef Animator incarnation changed during clip-binding restoration: "+saved.Path+".");
                OwnerGraphState before=CaptureOwnerGraph(live);
                bool alreadyExact=expected.Animator==before.Animator&&expected.Controller==before.Controller&&
                    expected.ControllerConstant==before.ControllerConstant&&expected.Descriptors==before.Descriptors&&
                    expected.Graph==before.Graph&&expected.LayerCount==before.LayerCount&&
                    expected.RecordCount==before.RecordCount&&expected.Hash==before.Hash&&
                    expected.GraphDirty58==before.GraphDirty58&&expected.Bytes.SequenceEqual(before.Bytes);
                if(alreadyExact)
                {
                    rows.Add(new Dictionary<string,object>{{"path",saved.Path},{"skipped",true},{"accepted",true},
                        {"reason","owner graph already exact"},{"ownerHash",before.Hash.ToString("X8")}});
                    continue;
                }
                uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(live)).ToInt32());
                NativeOverrideClipReceipt receipt=new NativeOverrideClipReceipt();
                int nativeOk=nativeOverrideClipPlayables(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),ref receipt);
                overrideClipAttempts++;
                uint memoryHash=ByteHash(saved.ControllerMemory);
                bool identities=receipt.Animator.ToUInt32()==expected.Animator&&receipt.Controller.ToUInt32()==expected.Controller&&
                    receipt.ControllerMemory.ToUInt32()!=0&&receipt.Graph.ToUInt32()==expected.Graph;
                bool beforeExact=receipt.MemorySizeBefore==(uint)saved.ControllerMemory.Length&&
                    receipt.MemoryHashBefore==memoryHash&&receipt.OwnerCountBefore==before.RecordCount&&
                    receipt.OwnerHashBefore==before.Hash&&receipt.GraphDirtyBefore==before.GraphDirty58;
                bool exactNoOpDigest=receipt.ControllerDirty9093After==receipt.ControllerDirty9093Before&&
                    receipt.MemorySizeAfter==receipt.MemorySizeBefore&&receipt.MemoryHashAfter==receipt.MemoryHashBefore&&
                    receipt.OwnerCountAfter==receipt.OwnerCountBefore&&receipt.OwnerHashAfter==receipt.OwnerHashBefore&&
                    receipt.GraphDirtyAfter==receipt.GraphDirtyBefore;
                bool exactNoOp=false;
                if(exactNoOpDigest)
                {
                    OwnerGraphState afterNoOp=CaptureOwnerGraph(live);
                    exactNoOp=afterNoOp.Animator==before.Animator&&afterNoOp.Controller==before.Controller&&
                        afterNoOp.ControllerConstant==before.ControllerConstant&&afterNoOp.Descriptors==before.Descriptors&&
                        afterNoOp.Graph==before.Graph&&afterNoOp.LayerCount==before.LayerCount&&
                        afterNoOp.RecordCount==before.RecordCount&&afterNoOp.Hash==before.Hash&&
                        afterNoOp.GraphDirty58==before.GraphDirty58&&afterNoOp.Bytes.SequenceEqual(before.Bytes);
                }
                bool normalMutation=nativeOk==1&&receipt.Result==1&&receipt.Stage==7&&
                    receipt.ControllerDirty9093After==0x01000000u;
                bool verifiedNoOp=nativeOk==0&&receipt.Result==11&&receipt.Stage==6&&
                    receipt.ControllerDirty9093After==0u&&exactNoOp;
                bool ownerCardinalityStable=receipt.OwnerCountAfter==receipt.OwnerCountBefore;
                bool accepted=receipt.ApiVersion==NativeAnimatorApiVersion&&
                    receipt.StructSize==(uint)Marshal.SizeOf(typeof(NativeOverrideClipReceipt))&&identities&&beforeExact&&
                    receipt.MemorySizeAfter==receipt.MemorySizeBefore&&receipt.MemoryHashAfter==receipt.MemoryHashBefore&&
                    ownerCardinalityStable&&receipt.GraphDirtyAfter==receipt.GraphDirtyBefore&&
                    receipt.ControllerDirty9093Before==0u&&(normalMutation||verifiedNoOp);
                if(receipt.Stage>=5)overrideClipMutations++;
                allAccepted=allAccepted&&accepted;
                var row=new Dictionary<string,object>{{"path",saved.Path},{"skipped",false},{"accepted",accepted},
                    {"apiVersion",receipt.ApiVersion},{"result",receipt.Result},{"lastError","0x"+receipt.LastError.ToString("X8")},
                    {"stage",receipt.Stage},{"identitiesExact",identities},{"beforeExact",beforeExact},
                    {"memorySizeBefore",receipt.MemorySizeBefore},{"memorySizeAfter",receipt.MemorySizeAfter},
                    {"memoryHashBefore",receipt.MemoryHashBefore.ToString("X8")},{"memoryHashAfter",receipt.MemoryHashAfter.ToString("X8")},
                    {"ownerCountBefore",receipt.OwnerCountBefore},{"ownerCountAfter",receipt.OwnerCountAfter},
                    {"targetOwnerCount",expected.RecordCount},{"ownerCardinalityStable",ownerCardinalityStable},
                    {"ownerHashBefore",receipt.OwnerHashBefore.ToString("X8")},{"ownerHashAfter",receipt.OwnerHashAfter.ToString("X8")},
                    {"graphDirtyBefore","0x"+receipt.GraphDirtyBefore.ToString("X8")},
                    {"graphDirtyAfter","0x"+receipt.GraphDirtyAfter.ToString("X8")},
                    {"controllerDirty9093Before","0x"+receipt.ControllerDirty9093Before.ToString("X8")},
                    {"controllerDirty9093After","0x"+receipt.ControllerDirty9093After.ToString("X8")},
                    {"outcome",normalMutation?"changed":verifiedNoOp?"idempotent-no-op":"rejected"},{"exactNoOp",exactNoOp},
                    {"mutationStarted",receipt.Stage>=5},{"unexpectedPartialMutation",receipt.Stage>=5&&!accepted}};
                rows.Add(row);
                if(!accepted)
                {
                    lastOverrideClipRestore=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                        {"allAccepted",false},{"animators",rows.ToArray()}};
                    throw new InvalidOperationException("Native Animator clip-binding restoration failed at "+phase+" for "+saved.Path+
                        ": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+", stage="+receipt.Stage+".");
                }
            }
            object observation=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                {"allAccepted",allAccepted},{"animators",rows.ToArray()}};
            overrideClipRestoreObservations.Add(observation);lastOverrideClipRestore=observation;
        }

        private void RestoreTargetNullClips(FrameState frame,string phase)
        {
            if(nativeTargetNullClipRestore==null)
                throw new InvalidOperationException("Native Animator target-null clip restore is not bound.");
            Animator[] current=FindAnimators();var rows=new List<object>();bool allExact=true;
            if(current.Length!=frame.Animators.Length)
                throw new InvalidOperationException("Chef Animator membership changed during target-null clip restoration.");
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];OwnerGraphState owner=saved.OwnerGraph;Animator live=current[i];
                if(owner==null||owner.Bytes==null||owner.Bytes.Length==0||owner.Bytes.Length%OwnerGraphRecordSize!=0)
                    throw new InvalidOperationException("Chef Animator checkpoint lacks an owner graph: "+saved.Path+".");
                if(live.GetInstanceID()!=saved.InstanceId||PathOf(live.transform)!=saved.Path||!ReferenceEquals(live,saved.Animator))
                    throw new InvalidOperationException("Chef Animator incarnation changed during target-null clip restoration: "+saved.Path+".");
                OwnerGraphState liveBefore=CaptureOwnerGraph(live);
                if(liveBefore.RecordCount!=owner.RecordCount)
                {
                    rows.Add(new Dictionary<string,object>{{"path",saved.Path},{"exact",true},{"deferred",true},
                        {"reason","owner cardinality differs before transition normalization"},
                        {"targetOwnerCount",owner.RecordCount},{"currentOwnerCount",liveBefore.RecordCount},
                        {"targetOwnerHash",owner.Hash.ToString("X8")},{"currentOwnerHash",liveBefore.Hash.ToString("X8")}});
                    continue;
                }
                uint targetHash=ByteHash(owner.Bytes),outerCount=0;
                for(int offset=0;offset<owner.Bytes.Length;offset+=(int)OwnerGraphRecordSize)
                    if(BitConverter.ToUInt32(owner.Bytes,offset)==1u)outerCount++;
                uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(live)).ToInt32());
                NativeTargetNullClipReceipt receipt=new NativeTargetNullClipReceipt();int nativeOk;
                GCHandle ownerPinned=GCHandle.Alloc(owner.Bytes,GCHandleType.Pinned);
                try
                {
                    nativeOk=nativeTargetNullClipRestore(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),
                        ownerPinned.AddrOfPinnedObject(),(uint)owner.Bytes.Length,ref receipt);
                }
                finally{ownerPinned.Free();}
                targetNullClipRestoreAttempts++;targetNullClipRestorePlans+=receipt.PlannedClipCount;
                targetNullClipRestoreCompleted+=receipt.CompletedClipCount;
                targetNullScalarRestorePlans+=receipt.PlannedScalarWriteCount;
                targetNullScalarRestoreCompleted+=receipt.CompletedScalarWriteCount;
                bool identities=receipt.UnityBase.ToUInt32()==unityPlayerBase&&owner.Animator==receipt.Animator.ToUInt32()&&
                    owner.Controller==receipt.Controller.ToUInt32()&&owner.ControllerConstant==receipt.ControllerConstant.ToUInt32()&&
                    owner.Descriptors==receipt.Descriptors.ToUInt32()&&owner.Graph==receipt.Graph.ToUInt32();
                bool targets=owner.Hash==targetHash&&receipt.TargetOwnerCount==owner.RecordCount&&receipt.TargetOwnerHash==owner.Hash;
                bool counts=receipt.CurrentOwnerCount==owner.RecordCount&&receipt.ProjectedOwnerCount==owner.RecordCount&&
                    receipt.StableOwnerCount==owner.RecordCount&&receipt.AfterOwnerCount==owner.RecordCount;
                bool stateMachines=(ulong)receipt.ExactStateMachineCount+(ulong)receipt.RotatedStateMachineCount==(ulong)outerCount;
                bool hashes=receipt.StableOwnerHash==receipt.CurrentOwnerHash&&receipt.AfterOwnerHash==receipt.ProjectedOwnerHash;
                bool graphDirty=receipt.GraphDirtyBefore==owner.GraphDirty58&&receipt.GraphDirtyProjected==owner.GraphDirty58&&
                    receipt.GraphDirtyAfter==owner.GraphDirty58;
                uint projectedDirty=receipt.ControllerDirtyBefore|(receipt.PlannedClipCount==0?0u:0x01000000u);
                bool controllerDirty=(receipt.PlannedClipCount==0||receipt.ControllerDirtyBefore==0x01000000u)&&
                    receipt.ControllerDirtyProjected==projectedDirty&&receipt.ControllerDirtyAfter==projectedDirty;
                bool clearedFailure=receipt.FailureLayer==UInt32.MaxValue&&receipt.FailureStateMachine==UInt32.MaxValue&&
                    receipt.FailureRecord==UInt32.MaxValue&&receipt.FailureByteOffset==UInt32.MaxValue&&
                    receipt.ExpectedWord==0u&&receipt.ActualWord==0u;
                bool scalarCounts=receipt.PlannedScalarWriteCount==receipt.PlannedAlreadyNullClipCount*2u+
                    receipt.PlannedEmptyOutputCount&&receipt.CompletedScalarWriteCount==receipt.PlannedScalarWriteCount&&
                    receipt.RolledBackScalarWriteCount==0u&&receipt.ScalarRollbackFailure==0u;
                bool exact=nativeOk==1&&receipt.ApiVersion==NativeAnimatorApiVersion&&receipt.StructSize==176u&&
                    receipt.StructSize==(uint)Marshal.SizeOf(typeof(NativeTargetNullClipReceipt))&&receipt.Result==1&&
                    receipt.LastError==0u&&receipt.Stage==11&&identities&&targets&&counts&&stateMachines&&hashes&&graphDirty&&
                    receipt.CompletedClipCount==receipt.PlannedClipCount&&scalarCounts&&
                    (receipt.MutationStarted!=0)==(receipt.PlannedClipCount!=0||receipt.PlannedScalarWriteCount!=0)&&
                    controllerDirty&&clearedFailure;
                allExact=allExact&&exact;
                var row=new Dictionary<string,object>{{"path",saved.Path},{"exact",exact},{"identitiesExact",identities},
                    {"targetsExact",targets},{"countsExact",counts},{"stateMachinesCovered",stateMachines},
                    {"hashesStable",hashes},{"graphDirtyExact",graphDirty},{"controllerDirtyExact",controllerDirty},
                    {"failureCleared",clearedFailure},{"apiVersion",receipt.ApiVersion},{"structSize",receipt.StructSize},
                    {"result",receipt.Result},{"lastError","0x"+receipt.LastError.ToString("X8")},{"stage",receipt.Stage},
                    {"targetOwnerCount",receipt.TargetOwnerCount},{"currentOwnerCount",receipt.CurrentOwnerCount},
                    {"projectedOwnerCount",receipt.ProjectedOwnerCount},{"stableOwnerCount",receipt.StableOwnerCount},
                    {"afterOwnerCount",receipt.AfterOwnerCount},{"targetOwnerHash",receipt.TargetOwnerHash.ToString("X8")},
                    {"currentOwnerHash",receipt.CurrentOwnerHash.ToString("X8")},{"projectedOwnerHash",receipt.ProjectedOwnerHash.ToString("X8")},
                    {"stableOwnerHash",receipt.StableOwnerHash.ToString("X8")},{"afterOwnerHash",receipt.AfterOwnerHash.ToString("X8")},
                    {"graphDirtyBefore","0x"+receipt.GraphDirtyBefore.ToString("X8")},
                    {"graphDirtyProjected","0x"+receipt.GraphDirtyProjected.ToString("X8")},
                    {"graphDirtyAfter","0x"+receipt.GraphDirtyAfter.ToString("X8")},
                    {"plannedClipCount",receipt.PlannedClipCount},{"completedClipCount",receipt.CompletedClipCount},
                    {"plannedAlreadyNullClipCount",receipt.PlannedAlreadyNullClipCount},
                    {"plannedEmptyOutputCount",receipt.PlannedEmptyOutputCount},
                    {"plannedScalarWriteCount",receipt.PlannedScalarWriteCount},
                    {"completedScalarWriteCount",receipt.CompletedScalarWriteCount},
                    {"rolledBackScalarWriteCount",receipt.RolledBackScalarWriteCount},
                    {"scalarRollbackFailure",receipt.ScalarRollbackFailure!=0},{"scalarCountsExact",scalarCounts},
                    {"expectedStateMachineCount",outerCount},{"exactStateMachineCount",receipt.ExactStateMachineCount},
                    {"rotatedStateMachineCount",receipt.RotatedStateMachineCount},
                    {"controllerDirtyBefore","0x"+receipt.ControllerDirtyBefore.ToString("X8")},
                    {"controllerDirtyProjected","0x"+receipt.ControllerDirtyProjected.ToString("X8")},
                    {"controllerDirtyAfter","0x"+receipt.ControllerDirtyAfter.ToString("X8")},
                    {"failureLayer",receipt.FailureLayer==UInt32.MaxValue?-1:(long)receipt.FailureLayer},
                    {"failureStateMachine",receipt.FailureStateMachine==UInt32.MaxValue?-1:(long)receipt.FailureStateMachine},
                    {"failureRecord",receipt.FailureRecord==UInt32.MaxValue?-1:(long)receipt.FailureRecord},
                    {"failureByteOffset",receipt.FailureByteOffset==UInt32.MaxValue?-1:(long)receipt.FailureByteOffset},
                    {"expectedWord","0x"+receipt.ExpectedWord.ToString("X8")},{"actualWord","0x"+receipt.ActualWord.ToString("X8")},
                    {"mutationStarted",receipt.MutationStarted!=0},{"unexpectedPartialMutation",receipt.MutationStarted!=0&&!exact}};
                rows.Add(row);
                if(!exact)
                {
                    lastTargetNullClipRestore=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                        {"allExact",false},{"animators",rows.ToArray()}};
                    throw new InvalidOperationException("Native Animator target-null clip restoration failed at "+phase+" for "+saved.Path+
                        ": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+", stage="+receipt.Stage+
                        ", planned="+receipt.PlannedClipCount+", completed="+receipt.CompletedClipCount+
                        ", scalarPlanned="+receipt.PlannedScalarWriteCount+", scalarCompleted="+receipt.CompletedScalarWriteCount+
                        ", record="+receipt.FailureRecord+", byte="+receipt.FailureByteOffset+
                        ", expected=0x"+receipt.ExpectedWord.ToString("X8")+", actual=0x"+receipt.ActualWord.ToString("X8")+".");
                }
            }
            object observation=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                {"allExact",allExact},{"animators",rows.ToArray()}};
            targetNullClipRestoreObservations.Add(observation);lastTargetNullClipRestore=observation;
        }

        private void NormalizeSettledEndTransitions(FrameState frame,string phase,bool requireNoPlan)
        {
            if(nativeSettledEndTransitionNormalize==null)
                throw new InvalidOperationException("Native settled EndTransition normalizer is not bound.");
            Animator[] current=FindAnimators();var rows=new List<object>();bool allExact=true;
            if(current.Length!=frame.Animators.Length)
                throw new InvalidOperationException("Chef Animator membership changed during settled EndTransition normalization.");
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];TransitionTopologyState topology=saved.TransitionTopology;
                OwnerGraphState owner=saved.OwnerGraph;Animator live=current[i];
                if(topology==null||topology.Bytes==null||topology.Bytes.Length==0||topology.Bytes.Length%28!=0||
                    owner==null||owner.Bytes==null||owner.Bytes.Length==0||owner.Bytes.Length%OwnerGraphRecordSize!=0)
                    throw new InvalidOperationException("Chef Animator checkpoint lacks transition topology or owner graph: "+saved.Path+".");
                if(live.GetInstanceID()!=saved.InstanceId||PathOf(live.transform)!=saved.Path||!ReferenceEquals(live,saved.Animator))
                    throw new InvalidOperationException("Chef Animator incarnation changed during settled EndTransition normalization: "+saved.Path+".");
                uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(live)).ToInt32());
                NativeEndTransitionReceipt receipt=new NativeEndTransitionReceipt();int nativeOk;
                GCHandle topologyPinned=GCHandle.Alloc(topology.Bytes,GCHandleType.Pinned);
                GCHandle ownerPinned=GCHandle.Alloc(owner.Bytes,GCHandleType.Pinned);
                try
                {
                    nativeOk=nativeSettledEndTransitionNormalize(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),
                        topologyPinned.AddrOfPinnedObject(),(uint)topology.Bytes.Length,
                        ownerPinned.AddrOfPinnedObject(),(uint)owner.Bytes.Length,requireNoPlan?1u:0u,ref receipt);
                }
                finally{ownerPinned.Free();topologyPinned.Free();}
                settledEndTransitionNormalizeAttempts++;
                settledEndTransitionPlanned+=receipt.PlannedTransitionCount;
                settledEndTransitionCompleted+=receipt.CompletedTransitionCount;
                bool identities=owner.Animator==receipt.Animator.ToUInt32()&&owner.Controller==receipt.Controller.ToUInt32()&&
                    owner.ControllerConstant==receipt.ControllerConstant.ToUInt32()&&owner.Descriptors==receipt.Descriptors.ToUInt32()&&
                    owner.Graph==receipt.Graph.ToUInt32();
                uint topologyCount=checked((uint)(topology.Bytes.Length/28));
                bool targets=receipt.TargetTopologyCount==topologyCount&&receipt.TargetTopologyHash==topology.Hash&&
                    receipt.TargetOwnerCount==owner.RecordCount&&receipt.TargetOwnerHash==owner.Hash;
                bool immediateOwnerExact=receipt.ProjectedOwnerHash==owner.Hash&&receipt.AfterOwnerHash==owner.Hash;
                bool projectedPostimageStable=receipt.ProjectedOwnerHash==receipt.AfterOwnerHash;
                bool accepted=nativeOk==1&&receipt.Result==1&&receipt.Stage==16&&
                    receipt.ApiVersion==NativeAnimatorApiVersion&&
                    receipt.StructSize==(uint)Marshal.SizeOf(typeof(NativeEndTransitionReceipt))&&identities&&targets&&
                    (!requireNoPlan||receipt.PlannedTransitionCount==0)&&
                    receipt.CompletedTransitionCount==receipt.PlannedTransitionCount&&
                    receipt.CompletedReboundClipCount==receipt.PlannedReboundClipCount&&
                    receipt.CompletedWeightPlanCount==receipt.PlannedWeightPlanCount&&
                    receipt.RollbackFailure==0u&&
                    receipt.ProjectedOwnerCount==owner.RecordCount&&receipt.AfterOwnerCount==owner.RecordCount&&
                    projectedPostimageStable&&
                    receipt.GraphDirtyProjected==owner.GraphDirty58&&receipt.GraphDirtyAfter==owner.GraphDirty58&&
                    receipt.ControllerDirtyAfter==receipt.ControllerDirtyProjected;
                allExact=allExact&&accepted;
                var row=new Dictionary<string,object>{{"path",saved.Path},{"accepted",accepted},{"requireNoPlan",requireNoPlan},{"immediateOwnerExact",immediateOwnerExact},
                    {"projectedPostimageStable",projectedPostimageStable},{"identitiesExact",identities},
                    {"targetsExact",targets},{"result",receipt.Result},{"lastError","0x"+receipt.LastError.ToString("X8")},
                    {"stage",receipt.Stage},{"plannedTransitionCount",receipt.PlannedTransitionCount},
                    {"completedTransitionCount",receipt.CompletedTransitionCount},
                    {"plannedReboundClipCount",receipt.PlannedReboundClipCount},
                    {"completedReboundClipCount",receipt.CompletedReboundClipCount},
                    {"plannedWeightPlanCount",receipt.PlannedWeightPlanCount},
                    {"completedWeightPlanCount",receipt.CompletedWeightPlanCount},
                    {"primedWeightWriteCount",receipt.PrimedWeightWriteCount},
                    {"rolledBackWeightWriteCount",receipt.RolledBackWeightWriteCount},
                    {"rollbackFailure",receipt.RollbackFailure},
                    {"targetTopologyHash",receipt.TargetTopologyHash.ToString("X8")},
                    {"currentTopologyHash",receipt.CurrentTopologyHash.ToString("X8")},
                    {"targetOwnerHash",receipt.TargetOwnerHash.ToString("X8")},
                    {"currentOwnerHash",receipt.CurrentOwnerHash.ToString("X8")},
                    {"projectedOwnerHash",receipt.ProjectedOwnerHash.ToString("X8")},
                    {"afterOwnerHash",receipt.AfterOwnerHash.ToString("X8")},
                    {"graphDirtyBefore","0x"+receipt.GraphDirtyBefore.ToString("X8")},
                    {"graphDirtyProjected","0x"+receipt.GraphDirtyProjected.ToString("X8")},
                    {"graphDirtyAfter","0x"+receipt.GraphDirtyAfter.ToString("X8")},
                    {"controllerDirtyBefore","0x"+receipt.ControllerDirtyBefore.ToString("X8")},
                    {"controllerDirtyExpectedAfterEnd","0x"+receipt.ControllerDirtyAfterEnd.ToString("X8")},
                    {"controllerDirtyProjected","0x"+receipt.ControllerDirtyProjected.ToString("X8")},
                    {"controllerDirtyAfter","0x"+receipt.ControllerDirtyAfter.ToString("X8")},
                    {"failureLayer",receipt.FailureLayer==UInt32.MaxValue?-1:(long)receipt.FailureLayer},
                    {"failureStateMachine",receipt.FailureStateMachine==UInt32.MaxValue?-1:(long)receipt.FailureStateMachine},
                    {"failureRecord",receipt.FailureRecord==UInt32.MaxValue?-1:(long)receipt.FailureRecord},
                    {"failureByteOffset",receipt.FailureByteOffset==UInt32.MaxValue?-1:(long)receipt.FailureByteOffset},
                    {"expectedWord","0x"+receipt.ExpectedWord.ToString("X8")},{"actualWord","0x"+receipt.ActualWord.ToString("X8")},
                    {"mutationStarted",receipt.MutationStarted!=0u},
                    {"unexpectedPartialMutation",(receipt.MutationStarted!=0u&&!accepted)||receipt.RollbackFailure!=0u}};
                rows.Add(row);
                if(!accepted)
                {
                    lastSettledEndTransitionNormalize=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                        {"allExact",false},{"animators",rows.ToArray()}};
                    throw new InvalidOperationException("Native settled EndTransition normalization failed at "+phase+" for "+saved.Path+
                        ": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+", stage="+receipt.Stage+
                        ", planned="+receipt.PlannedTransitionCount+", completed="+receipt.CompletedTransitionCount+
                        ", rebinds="+receipt.CompletedReboundClipCount+"/"+receipt.PlannedReboundClipCount+
                        ", weights="+receipt.CompletedWeightPlanCount+"/"+receipt.PlannedWeightPlanCount+
                        ", record="+receipt.FailureRecord+", byte="+receipt.FailureByteOffset+
                        ", expected=0x"+receipt.ExpectedWord.ToString("X8")+", actual=0x"+receipt.ActualWord.ToString("X8")+".");
                }
            }
            object observation=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"allExact",allExact},{"animators",rows.ToArray()}};
            settledEndTransitionNormalizeObservations.Add(observation);lastSettledEndTransitionNormalize=observation;
        }

        private void RestorePlayableTimes(FrameState frame,string phase)
        {
            if(nativePlayableTimeRestore==null)
                throw new InvalidOperationException("Native Animator Playable-time restore is not bound.");
            Animator[] current=FindAnimators();var rows=new List<object>();bool allExact=true;
            if(current.Length!=frame.Animators.Length)
                throw new InvalidOperationException("Chef Animator membership changed during Playable-time restoration.");
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];OwnerGraphState owner=saved.OwnerGraph;Animator live=current[i];
                if(owner==null||owner.Bytes==null||owner.Bytes.Length==0||owner.Bytes.Length%OwnerGraphRecordSize!=0)
                    throw new InvalidOperationException("Chef Animator checkpoint lacks an owner graph: "+saved.Path+".");
                if(live.GetInstanceID()!=saved.InstanceId||PathOf(live.transform)!=saved.Path||!ReferenceEquals(live,saved.Animator))
                    throw new InvalidOperationException("Chef Animator incarnation changed during Playable-time restoration: "+saved.Path+".");
                uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(live)).ToInt32());
                NativePlayableTimeReceipt receipt=new NativePlayableTimeReceipt();int nativeOk;
                GCHandle ownerPinned=GCHandle.Alloc(owner.Bytes,GCHandleType.Pinned);
                try
                {
                    nativeOk=nativePlayableTimeRestore(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),
                        ownerPinned.AddrOfPinnedObject(),(uint)owner.Bytes.Length,ref receipt);
                }
                finally{ownerPinned.Free();}
                playableTimeRestoreAttempts++;playableTimeRestorePlans+=receipt.PlannedNodeCount;
                playableTimeRestoreCompleted+=receipt.CompletedNodeCount;
                bool identities=owner.Animator==receipt.Animator.ToUInt32()&&owner.Controller==receipt.Controller.ToUInt32()&&
                    owner.ControllerConstant==receipt.ControllerConstant.ToUInt32()&&owner.Descriptors==receipt.Descriptors.ToUInt32()&&
                    owner.Graph==receipt.Graph.ToUInt32();
                bool targets=receipt.TargetOwnerCount==owner.RecordCount&&receipt.TargetOwnerHash==owner.Hash;
                bool liveCardinalityStable=receipt.CurrentOwnerCount==receipt.ProjectedOwnerCount&&
                    receipt.ProjectedOwnerCount==receipt.AfterOwnerCount;
                bool exact=nativeOk==1&&receipt.Result==1&&receipt.Stage==10&&
                    receipt.ApiVersion==NativeAnimatorApiVersion&&
                    receipt.StructSize==(uint)Marshal.SizeOf(typeof(NativePlayableTimeReceipt))&&identities&&targets&&
                    liveCardinalityStable&&receipt.UniqueTargetNodes>0&&
                    receipt.UniqueTargetNodes<=receipt.UniqueCurrentNodes&&
                    receipt.CompletedNodeCount==receipt.PlannedNodeCount&&receipt.ProjectedOwnerHash==receipt.AfterOwnerHash&&
                    (ulong)receipt.OrdinaryAdvanceRecipes+(ulong)receipt.SeekOnlyRecipes<=(ulong)receipt.PlannedNodeCount&&
                    receipt.GraphDirtyBefore==owner.GraphDirty58&&receipt.GraphDirtyAfter==owner.GraphDirty58&&
                    (receipt.MutationStarted!=0)==(receipt.PlannedNodeCount!=0);
                allExact=allExact&&exact;
                var row=new Dictionary<string,object>{{"path",saved.Path},{"exact",exact},{"identitiesExact",identities},
                    {"targetsExact",targets},{"result",receipt.Result},{"lastError","0x"+receipt.LastError.ToString("X8")},
                    {"stage",receipt.Stage},{"targetOwnerHash",receipt.TargetOwnerHash.ToString("X8")},
                    {"currentOwnerHash",receipt.CurrentOwnerHash.ToString("X8")},
                    {"projectedOwnerHash",receipt.ProjectedOwnerHash.ToString("X8")},
                    {"afterOwnerHash",receipt.AfterOwnerHash.ToString("X8")},
                    {"uniqueTargetNodes",receipt.UniqueTargetNodes},{"uniqueCurrentNodes",receipt.UniqueCurrentNodes},
                    {"targetNodesAreLiveSubset",receipt.UniqueTargetNodes>0&&receipt.UniqueTargetNodes<=receipt.UniqueCurrentNodes},
                    {"targetOwnerCount",receipt.TargetOwnerCount},{"currentOwnerCount",receipt.CurrentOwnerCount},
                    {"projectedOwnerCount",receipt.ProjectedOwnerCount},{"afterOwnerCount",receipt.AfterOwnerCount},
                    {"liveCardinalityStable",liveCardinalityStable},
                    {"plannedNodeCount",receipt.PlannedNodeCount},{"completedNodeCount",receipt.CompletedNodeCount},
                    {"ordinaryAdvanceRecipes",receipt.OrdinaryAdvanceRecipes},{"seekOnlyRecipes",receipt.SeekOnlyRecipes},
                    {"graphDirtyBefore","0x"+receipt.GraphDirtyBefore.ToString("X8")},
                    {"graphDirtyAfter","0x"+receipt.GraphDirtyAfter.ToString("X8")},
                    {"failureRecord",receipt.FailureRecord==UInt32.MaxValue?-1:(long)receipt.FailureRecord},
                    {"failureTargetRecord",receipt.FailureTargetRecord==UInt32.MaxValue?-1:(long)receipt.FailureTargetRecord},
                    {"failureByteOffset",receipt.FailureByteOffset==UInt32.MaxValue?-1:(long)receipt.FailureByteOffset},
                    {"expectedWord","0x"+receipt.ExpectedWord.ToString("X8")},{"actualWord","0x"+receipt.ActualWord.ToString("X8")},
                    {"failureNode",Pointer(receipt.FailureNode.ToUInt32())},{"mutationStarted",receipt.MutationStarted!=0}};
                rows.Add(row);
                if(!exact)
                {
                    lastPlayableTimeRestore=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                        {"allExact",false},{"animators",rows.ToArray()}};
                    throw new InvalidOperationException("Native Animator Playable-time restoration failed at "+phase+" for "+saved.Path+
                        ": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+", stage="+receipt.Stage+
                        ", record="+receipt.FailureRecord+", byte="+receipt.FailureByteOffset+
                        ", expected=0x"+receipt.ExpectedWord.ToString("X8")+", actual=0x"+receipt.ActualWord.ToString("X8")+".");
                }
            }
            object observation=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                {"allExact",allExact},{"animators",rows.ToArray()}};
            playableTimeRestoreObservations.Add(observation);lastPlayableTimeRestore=observation;
        }

        private void RestoreControllerMemory(FrameState frame,string phase)
        {
            if(nativeControllerRestore==null)throw new InvalidOperationException("Native Animator controller restore is not bound.");
            Animator[] current=FindAnimators();
            if(current.Length!=frame.Animators.Length)throw new InvalidOperationException("Chef Animator membership changed at "+phase+".");
            var rows=new List<object>();
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];Animator live=current[i];
                if(live.GetInstanceID()!=saved.InstanceId||PathOf(live.transform)!=saved.Path||!ReferenceEquals(live,saved.Animator)||
                    saved.ControllerMemory==null||saved.ControllerMemory.Length<0x20)
                    throw new InvalidOperationException("Chef Animator controller incarnation or blob changed at "+phase+": "+saved.Path+".");
                uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(live)).ToInt32());
                NativeControllerReceipt receipt=new NativeControllerReceipt();int nativeOk;
                GCHandle pinned=GCHandle.Alloc(saved.ControllerMemory,GCHandleType.Pinned);
                try{nativeOk=nativeControllerRestore(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),pinned.AddrOfPinnedObject(),
                    (uint)saved.ControllerMemory.Length,ref receipt);}
                finally{pinned.Free();}
                bool exact=nativeOk==1&&receipt.Result==1&&receipt.DifferenceCount==0&&receipt.FirstDifference==UInt32.MaxValue;
                bool onlyExpectedFlag=nativeOk==0&&receipt.Result==8&&receipt.DifferenceCount==1&&receipt.FirstDifference==0x18&&
                    receipt.BeforeByte==0&&receipt.AfterByte==1&&receipt.FirstDifferenceExceptFirstEvaluationFlag==UInt32.MaxValue;
                if(receipt.ApiVersion!=NativeAnimatorApiVersion||receipt.StructSize!=(uint)Marshal.SizeOf(typeof(NativeControllerReceipt))||!exact)
                    throw new InvalidOperationException("Native Animator controller restore failed at "+phase+" for "+saved.Path+
                        ": result="+receipt.Result+", error=0x"+receipt.LastError.ToString("X8")+", differences="+receipt.DifferenceCount+
                        ", first="+receipt.FirstDifference+".");
                rows.Add(new Dictionary<string,object>{{"path",saved.Path},{"blobBytes",saved.ControllerMemory.Length},
                    {"byteExact",exact},{"onlyExpectedFirstEvaluationFlagDifference",onlyExpectedFlag},
                    {"memoryBefore",Pointer(receipt.MemoryBefore)},{"memoryAfter",Pointer(receipt.MemoryAfter)},
                    {"hashBefore",receipt.HashBefore.ToString("X8")},{"hashAfter",receipt.HashAfter.ToString("X8")}});
                controllerRestores++;
            }
            lastControllerRestore=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"animators",rows.ToArray()}};
        }

        private void Restore(FrameState frame,string phase)
        {
            ValidateConfiguration(frame,phase);
            RestoreControllerMemory(frame,phase);
            ObserveControllerInput(frame,phase);
            ObserveTransitionTopology(frame,phase);
        }

        private void RestoreUnityRandomState(FrameState frame,string phase)
        {
            UnityEngine.Random.State before=UnityEngine.Random.state;
            UnityEngine.Random.state=frame.RandomState;
            UnityEngine.Random.State after=UnityEngine.Random.state;
            bool exact=SameRandomState(frame.RandomState,after);
            lastUnityRandomStateRestore=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},
                {"before",DescribeRandomState(before)},{"target",DescribeRandomState(frame.RandomState)},
                {"after",DescribeRandomState(after)},{"exact",exact}};
            if(!exact)throw new InvalidOperationException("UnityEngine.Random.state did not restore exactly at "+phase+".");
            unityRandomStateRestores++;
        }

        private void ValidateConfiguration(FrameState frame,string phase)
        {
            Animator[] current=FindAnimators();
            if(current.Length!=frame.Animators.Length)throw new InvalidOperationException("Chef Animator membership changed.");
            var rows=new List<object>();
            for(int i=0;i<current.Length;i++)
            {
                AnimatorState saved=frame.Animators[i];Animator live=current[i];
                AnimatorControllerParameter[] parameters=live.parameters;
                bool instanceMatch=live.GetInstanceID()==saved.InstanceId;
                bool pathMatch=PathOf(live.transform)==saved.Path;
                bool animatorReferenceMatch=ReferenceEquals(live,saved.Animator);
                bool controllerReferenceMatch=ReferenceEquals(live.runtimeAnimatorController,saved.Controller);
                bool avatarReferenceMatch=ReferenceEquals(live.avatar,saved.Avatar);
                bool parameterCountMatch=parameters.Length==saved.Parameters.Length;
                bool layerCountMatch=live.layerCount==saved.Layers.Length;
                bool stable=instanceMatch&&pathMatch&&animatorReferenceMatch&&controllerReferenceMatch&&avatarReferenceMatch&&
                    parameterCountMatch&&layerCountMatch;
                rows.Add(new Dictionary<string,object>{{"path",saved.Path},{"stable",stable},{"instanceMatch",instanceMatch},
                    {"pathMatch",pathMatch},{"animatorReferenceMatch",animatorReferenceMatch},
                    {"controllerReferenceMatch",controllerReferenceMatch},{"avatarReferenceMatch",avatarReferenceMatch},
                    {"parameterCountMatch",parameterCountMatch},{"layerCountMatch",layerCountMatch},
                    {"savedEnabled",saved.Enabled},{"liveEnabled",live.enabled},
                    {"savedApplyRootMotion",saved.ApplyRootMotion},{"liveApplyRootMotion",live.applyRootMotion},
                    {"savedSpeed",saved.Speed},{"liveSpeed",live.speed},
                    {"savedUpdateMode",saved.UpdateMode.ToString()},{"liveUpdateMode",live.updateMode.ToString()},
                    {"savedCullingMode",saved.CullingMode.ToString()},{"liveCullingMode",live.cullingMode.ToString()}});
                if(!stable)
                {
                    lastConfigurationObservation=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"animators",rows.ToArray()}};
                    throw new InvalidOperationException("Chef Animator stable schema or incarnation changed: "+saved.Path+".");
                }
                for(int p=0;p<saved.Parameters.Length;p++)
                {
                    ParameterState expected=saved.Parameters[p];AnimatorControllerParameter actual=parameters[p];
                    if(actual.nameHash!=expected.Hash||actual.type!=expected.Type)
                        throw new InvalidOperationException("Chef Animator parameter schema changed: "+saved.Path+".");
                }
            }
            lastConfigurationObservation=new Dictionary<string,object>{{"phase",phase},{"frame",frame.Frame},{"animators",rows.ToArray()}};
        }

        private void ApplyPose(FrameState frame,string phase)
        {
            ApplyPose(frame,phase,false);
        }

        private void ApplyPose(FrameState frame,string phase,bool forceWrites)
        {
            int assigned=0;
            lastPoseFailure=null;
            foreach(AnimatorState animator in frame.Animators)
            {
                foreach(TransformState saved in animator.Transforms)
                {
                    Transform live=saved.Transform;
                    if(live==null||live.GetInstanceID()!=saved.InstanceId||PathOf(live)!=saved.Path)
                        throw new InvalidOperationException("Chef skeleton Transform incarnation changed at "+phase+": "+saved.Path+".");
                    Vector3 beforePosition=live.localPosition;
                    if(forceWrites||!Exact(beforePosition,saved.LocalPosition))
                    {
                        live.localPosition=saved.LocalPosition;Vector3 after=live.localPosition;assigned++;
                        if(!Exact(after,saved.LocalPosition))PoseFailure(phase,saved.Path,"localPosition",Describe(beforePosition),Describe(saved.LocalPosition),Describe(after));
                    }
                    Quaternion beforeRotation=live.localRotation;
                    if(forceWrites||!Exact(beforeRotation,saved.LocalRotation))
                    {
                        RestoreExactLocalRotation(live,saved,phase,beforeRotation,ref assigned);
                    }
                    Vector3 beforeScale=live.localScale;
                    if(forceWrites||!Exact(beforeScale,saved.LocalScale))
                    {
                        live.localScale=saved.LocalScale;Vector3 after=live.localScale;assigned++;
                        if(!Exact(after,saved.LocalScale))PoseFailure(phase,saved.Path,"localScale",Describe(beforeScale),Describe(saved.LocalScale),Describe(after));
                    }
                }
            }
            poseAssignments+=assigned;
        }

        private void RestoreExactLocalRotation(Transform live,TransformState saved,string phase,Quaternion before,ref int assigned)
        {
            Quaternion candidate=saved.LocalRotation,after=before;
            float previous=Single.PositiveInfinity;
            for(int attempt=0;attempt<4;attempt++)
            {
                live.localRotation=candidate;after=live.localRotation;assigned++;
                if(Exact(after,saved.LocalRotation))return;
                Quaternion residual=new Quaternion(saved.LocalRotation.x-after.x,saved.LocalRotation.y-after.y,
                    saved.LocalRotation.z-after.z,saved.LocalRotation.w-after.w);
                float size=Math.Max(Math.Max(Math.Abs(residual.x),Math.Abs(residual.y)),
                    Math.Max(Math.Abs(residual.z),Math.Abs(residual.w)));
                if(Single.IsNaN(size)||Single.IsInfinity(size)||size>=previous)
                    PoseFailure(phase,saved.Path,"localRotation",Describe(before),Describe(saved.LocalRotation),Describe(after));
                previous=size;
                Quaternion next=new Quaternion(candidate.x+residual.x,candidate.y+residual.y,
                    candidate.z+residual.z,candidate.w+residual.w);
                if(Exact(next,candidate))
                    PoseFailure(phase,saved.Path,"localRotation",Describe(before),Describe(saved.LocalRotation),Describe(after));
                candidate=next;posePreimageAdjustments++;
            }
            PoseFailure(phase,saved.Path,"localRotation",Describe(before),Describe(saved.LocalRotation),Describe(after));
        }

        private void PoseFailure(string phase,string path,string component,object before,object saved,object after)
        {
            lastPoseFailure=new Dictionary<string,object>{{"phase",phase},{"path",path},{"component",component},
                {"before",before},{"saved",saved},{"after",after}};
            throw new InvalidOperationException("Chef skeleton "+component+" lacks exact public readback at "+phase+": "+path+".");
        }

        private void ProbePose()
        {
            RequireFence();FrameState snapshot=new FrameState { Frame=-1,Animators=CaptureAll(),RandomState=UnityEngine.Random.state,
                ChefRandomizeEventCount=CurrentChefRandomizeEventCount() };
            try
            {
                ApplyPose(snapshot,"probe-force-public-roundtrip",true);
                lastProbe=new Dictionary<string,object>{{"ok",true},{"animatorCount",snapshot.Animators.Length},
                    {"transformCount",snapshot.Animators.Sum(value=>value.Transforms.Length)}};
                if(resumeFailure==null)failure=null;
            }
            catch(Exception error)
            {
                failure=error.ToString();lastProbe=new Dictionary<string,object>{{"ok",false},{"error",error.Message},
                    {"poseFailure",lastPoseFailure}};
            }
        }

        private void ProbeControllerMemory(Dictionary<string,object> args)
        {
            RequireFence();BindNativeControllerMemory(args);
            var rows=new List<object>();bool ok=true;
            foreach(Animator animator in FindAnimators())
            {
                object row;
                try
                {
                    uint animatorPointer=unchecked((uint)((IntPtr)cachedPtr.GetValue(animator)).ToInt32());
                    var receipt=new NativeControllerReceipt();
                    int nativeOk=nativeControllerRoundtrip(new UIntPtr(unityPlayerBase),new UIntPtr(animatorPointer),ref receipt);
                    bool exact=nativeOk==1&&receipt.Result==1&&receipt.DifferenceCount==0&&receipt.FirstDifference==UInt32.MaxValue;
                    bool accepted=receipt.ApiVersion==NativeAnimatorApiVersion&&nativeOk==1&&receipt.Result==1&&exact;
                    row=new Dictionary<string,object>{{"ok",accepted},{"byteExact",exact},{"path",PathOf(animator.transform)},
                        {"apiVersion",receipt.ApiVersion},{"structSize",receipt.StructSize},{"result",receipt.Result},
                        {"lastError","0x"+receipt.LastError.ToString("X8")},{"animator",Pointer(receipt.Animator)},
                        {"controller",Pointer(receipt.Controller)},{"memoryBefore",Pointer(receipt.MemoryBefore)},
                        {"memoryAfter",Pointer(receipt.MemoryAfter)},{"allocator",Pointer(receipt.Allocator)},
                        {"blobSizeBefore",receipt.BlobSizeBefore},{"blobSizeAfter",receipt.BlobSizeAfter},
                        {"hashBefore",receipt.HashBefore.ToString("X8")},{"hashAfter",receipt.HashAfter.ToString("X8")},
                        {"firstDifference",receipt.FirstDifference==UInt32.MaxValue?-1:(long)receipt.FirstDifference},
                        {"beforeByte",receipt.BeforeByte==UInt32.MaxValue?-1:(long)receipt.BeforeByte},
                        {"afterByte",receipt.AfterByte==UInt32.MaxValue?-1:(long)receipt.AfterByte},
                        {"differenceCount",receipt.DifferenceCount},
                        {"firstDifferenceExceptFirstEvaluationFlag",receipt.FirstDifferenceExceptFirstEvaluationFlag==UInt32.MaxValue?-1:(long)receipt.FirstDifferenceExceptFirstEvaluationFlag},
                        {"beforeByteExceptFirstEvaluationFlag",receipt.BeforeByteExceptFirstEvaluationFlag==UInt32.MaxValue?-1:(long)receipt.BeforeByteExceptFirstEvaluationFlag},
                        {"afterByteExceptFirstEvaluationFlag",receipt.AfterByteExceptFirstEvaluationFlag==UInt32.MaxValue?-1:(long)receipt.AfterByteExceptFirstEvaluationFlag}};
                    ok=ok&&accepted;
                }
                catch(Exception error)
                {
                    ok=false;row=new Dictionary<string,object>{{"ok",false},{"path",PathOf(animator.transform)},
                        {"error",error.ToString()}};
                }
                rows.Add(row);
            }
            lastControllerProbe=new Dictionary<string,object>{{"ok",ok},{"nativePath",nativePath},{"nativeSha256",nativeSha256},
                {"unityPlayerBase","0x"+unityPlayerBase.ToString("X8")},{"animators",rows.ToArray()}};
            failure=ok?resumeFailure:"Controller-memory ownership normalization was not byte-exact; see lastControllerProbe.";
        }

        private void BindNativeControllerMemory(Dictionary<string,object> args)
        {
            if(nativeLibrary!=IntPtr.Zero)
            {
                if(args.Count!=0)throw new ArgumentException("Native controller helper is already bound; probe takes no arguments.");
                return;
            }
            if(IntPtr.Size!=4||cachedPtr==null)throw new InvalidOperationException("Controller-memory probe requires the x86 Unity object ABI.");
            if(args.Count!=2||!args.ContainsKey("nativePath")||!args.ContainsKey("sha256"))
                throw new ArgumentException("First controller-memory probe requires nativePath and sha256 only.");
            string path=Path.GetFullPath(Convert.ToString(args["nativePath"]));
            string expected=Convert.ToString(args["sha256"]).ToUpperInvariant();
            if(!File.Exists(path))throw new FileNotFoundException("Native Animator checkpoint helper missing.",path);
            string actual=FileHash(path);if(actual!=expected)throw new InvalidOperationException("Native Animator checkpoint helper hash mismatch: "+actual+".");
            ProcessModule unity=null;
            foreach(ProcessModule module in Process.GetCurrentProcess().Modules)
                if(String.Equals(module.ModuleName,"UnityPlayer.dll",StringComparison.OrdinalIgnoreCase)){unity=module;break;}
            if(unity==null)throw new InvalidOperationException("UnityPlayer.dll is not loaded.");
            unityPlayerBase=unchecked((uint)unity.BaseAddress.ToInt32());
            nativeLibrary=LoadLibrary(path);if(nativeLibrary==IntPtr.Zero)throw new InvalidOperationException("LoadLibrary failed: "+Marshal.GetLastWin32Error()+".");
            try
            {
                nativeApiVersion=Export<NativeApiVersion>("oc2_animator_checkpoint_api_version");
                nativeControllerRoundtrip=Export<NativeControllerRoundtrip>("oc2_animator_controller_roundtrip");
                nativeControllerCapture=Export<NativeControllerCapture>("oc2_animator_controller_capture");
                nativeControllerInputCapture=Export<NativeControllerInputCapture>("oc2_animator_controller_input_capture");
                nativeTransitionTopologyCapture=Export<NativeTransitionTopologyCapture>("oc2_animator_transition_topology_capture");
                nativeMixerGraphCapture=Export<NativeMixerGraphCapture>("oc2_animator_mixer_graph_capture");
                nativeMixerGraphRestore=Export<NativeMixerGraphRestore>("oc2_animator_mixer_graph_restore");
                nativeOwnerGraphCapture=Export<NativeOwnerGraphCapture>("oc2_animator_owner_graph_capture");
                nativeSettledEndTransitionNormalize=Export<NativeSettledEndTransitionNormalize>("oc2_animator_settled_end_transition_normalize");
                nativePlayableTimeRestore=Export<NativePlayableTimeRestore>("oc2_animator_playable_time_restore");
                nativeTargetNullClipRestore=Export<NativeTargetNullClipRestore>("oc2_animator_target_null_clip_restore");
                nativeOverrideClipPlayables=Export<NativeOverrideClipPlayables>("oc2_animator_override_clip_playables_probe");
                nativeControllerNormalize=Export<NativeControllerNormalize>("oc2_animator_controller_normalize");
                nativeControllerRestore=Export<NativeControllerRestore>("oc2_animator_controller_restore");
                if(nativeApiVersion()!=NativeAnimatorApiVersion)throw new InvalidOperationException("Native Animator checkpoint API version mismatch.");
                nativePath=path;nativeSha256=actual;
            }
            catch
            {
                FreeLibrary(nativeLibrary);nativeLibrary=IntPtr.Zero;nativeApiVersion=null;nativeControllerRoundtrip=null;
                nativeControllerCapture=null;nativeControllerInputCapture=null;nativeTransitionTopologyCapture=null;
                nativeMixerGraphCapture=null;nativeMixerGraphRestore=null;nativeOwnerGraphCapture=null;
                nativeSettledEndTransitionNormalize=null;nativePlayableTimeRestore=null;nativeTargetNullClipRestore=null;
                nativeOverrideClipPlayables=null;
                nativeControllerNormalize=null;nativeControllerRestore=null;throw;
            }
        }

        private T Export<T>(string name) where T:class
        {
            IntPtr pointer=GetProcAddress(nativeLibrary,name);if(pointer==IntPtr.Zero)throw new MissingMethodException("Native export missing: "+name);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(pointer,typeof(T));
        }
        private static string FileHash(string path){using(var sha=SHA256.Create())using(var stream=File.OpenRead(path))return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        private static string Pointer(UIntPtr value){return value==UIntPtr.Zero?null:"0x"+value.ToUInt32().ToString("X8");}
        private static string Pointer(uint value){return value==0?null:"0x"+value.ToString("X8");}

        private static uint ByteHash(byte[] bytes)
        {
            unchecked
            {
                uint hash=2166136261u;
                for(int i=0;i<bytes.Length;i++){hash^=bytes[i];hash*=16777619u;}
                return hash;
            }
        }

        private static int FirstByteDifference(byte[] expected,byte[] actual)
        {
            int count=Math.Min(expected.Length,actual.Length);
            for(int i=0;i<count;i++)if(expected[i]!=actual[i])return i;
            return expected.Length==actual.Length?-1:count;
        }

        private static object Describe(Vector3 value)
        {
            return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z},
                {"xBits",Bits(value.x)},{"yBits",Bits(value.y)},{"zBits",Bits(value.z)}};
        }

        private static object Describe(Quaternion value)
        {
            return new Dictionary<string,object>{{"x",value.x},{"y",value.y},{"z",value.z},{"w",value.w},
                {"xBits",Bits(value.x)},{"yBits",Bits(value.y)},{"zBits",Bits(value.z)},{"wBits",Bits(value.w)}};
        }

        private static string Bits(float value)
        {
            return BitConverter.ToInt32(BitConverter.GetBytes(value),0).ToString("X8");
        }

        private static bool Same(FrameState left,FrameState right)
        {
            if(left.Frame!=right.Frame||left.Animators.Length!=right.Animators.Length)return false;
            for(int i=0;i<left.Animators.Length;i++)
            {
                AnimatorState a=left.Animators[i],b=right.Animators[i];
                if(a.InstanceId!=b.InstanceId||a.Path!=b.Path||!ReferenceEquals(a.Controller,b.Controller)||!ReferenceEquals(a.Avatar,b.Avatar)||
                    a.Enabled!=b.Enabled||a.ApplyRootMotion!=b.ApplyRootMotion||a.Speed!=b.Speed||a.UpdateMode!=b.UpdateMode||a.CullingMode!=b.CullingMode||
                    a.Parameters.Length!=b.Parameters.Length||a.Layers.Length!=b.Layers.Length)return false;
                for(int p=0;p<a.Parameters.Length;p++)if(!Same(a.Parameters[p],b.Parameters[p]))return false;
                for(int layer=0;layer<a.Layers.Length;layer++)if(!Same(a.Layers[layer],b.Layers[layer]))return false;
            }
            return true;
        }

        private static bool Same(ParameterState a,ParameterState b)
        {
            return a.Hash==b.Hash&&a.Type==b.Type&&a.Float==b.Float&&a.Int==b.Int&&a.Bool==b.Bool;
        }

        private static bool Same(LayerState a,LayerState b)
        {
            return a.FullPathHash==b.FullPathHash&&a.ShortNameHash==b.ShortNameHash&&a.TagHash==b.TagHash&&
                a.NormalizedTime==b.NormalizedTime&&a.Weight==b.Weight&&a.Loop==b.Loop;
        }

        private static bool SamePose(FrameState left,FrameState right)
        {
            if(left.Animators.Length!=right.Animators.Length)return false;
            for(int i=0;i<left.Animators.Length;i++)
            {
                TransformState[] a=left.Animators[i].Transforms,b=right.Animators[i].Transforms;
                if(a.Length!=b.Length)return false;
                for(int j=0;j<a.Length;j++)
                    if(a[j].InstanceId!=b[j].InstanceId||a[j].Path!=b[j].Path||!Exact(a[j].LocalPosition,b[j].LocalPosition)||
                        !Exact(a[j].LocalRotation,b[j].LocalRotation)||!Exact(a[j].LocalScale,b[j].LocalScale))return false;
            }
            return true;
        }

        private static object FirstDifference(FrameState expected,FrameState actual)
        {
            if(expected.ChefRandomizeEventCount!=actual.ChefRandomizeEventCount)
                return Difference(actual.Frame,null,"chefRandomizeEventCount",
                    expected.ChefRandomizeEventCount,actual.ChefRandomizeEventCount);
            if(expected.Animators.Length!=actual.Animators.Length)
                return Difference(actual.Frame,null,"animatorCount",expected.Animators.Length,actual.Animators.Length);
            for(int i=0;i<expected.Animators.Length;i++)
            {
                AnimatorState a=expected.Animators[i],b=actual.Animators[i];string path=a.Path;
                if(a.InstanceId!=b.InstanceId)return Difference(actual.Frame,path,"instanceId",a.InstanceId,b.InstanceId);
                if(a.Path!=b.Path)return Difference(actual.Frame,path,"path",a.Path,b.Path);
                if(a.Enabled!=b.Enabled)return Difference(actual.Frame,path,"enabled",a.Enabled,b.Enabled);
                if(a.ApplyRootMotion!=b.ApplyRootMotion)return Difference(actual.Frame,path,"applyRootMotion",a.ApplyRootMotion,b.ApplyRootMotion);
                if(!a.Speed.Equals(b.Speed))return Difference(actual.Frame,path,"speed",Describe(a.Speed),Describe(b.Speed));
                if(a.UpdateMode!=b.UpdateMode)return Difference(actual.Frame,path,"updateMode",a.UpdateMode.ToString(),b.UpdateMode.ToString());
                if(a.CullingMode!=b.CullingMode)return Difference(actual.Frame,path,"cullingMode",a.CullingMode.ToString(),b.CullingMode.ToString());
                if(a.ControllerMemory.Length!=b.ControllerMemory.Length)
                    return Difference(actual.Frame,path,"controllerMemoryLength",a.ControllerMemory.Length,b.ControllerMemory.Length);
                for(int offset=0;offset<a.ControllerMemory.Length;offset++)
                    if(a.ControllerMemory[offset]!=b.ControllerMemory[offset])
                        return new Dictionary<string,object>{{"frame",actual.Frame},{"animator",path},{"component","controllerMemory"},
                            {"offset",offset},{"expected",a.ControllerMemory[offset]},{"actual",b.ControllerMemory[offset]}};
                if(a.Parameters.Length!=b.Parameters.Length)
                    return Difference(actual.Frame,path,"parameterCount",a.Parameters.Length,b.Parameters.Length);
                for(int p=0;p<a.Parameters.Length;p++)
                    if(!Same(a.Parameters[p],b.Parameters[p]))
                        return Difference(actual.Frame,path,"parameter["+p+"]",Describe(a.Parameters[p]),Describe(b.Parameters[p]));
                if(a.Layers.Length!=b.Layers.Length)
                    return Difference(actual.Frame,path,"layerCount",a.Layers.Length,b.Layers.Length);
                for(int layer=0;layer<a.Layers.Length;layer++)
                    if(!Same(a.Layers[layer],b.Layers[layer]))
                        return Difference(actual.Frame,path,"layer["+layer+"]",Describe(a.Layers[layer]),Describe(b.Layers[layer]));
                if(a.Transforms.Length!=b.Transforms.Length)
                    return Difference(actual.Frame,path,"transformCount",a.Transforms.Length,b.Transforms.Length);
                for(int t=0;t<a.Transforms.Length;t++)
                {
                    TransformState x=a.Transforms[t],y=b.Transforms[t];
                    if(x.InstanceId!=y.InstanceId||x.Path!=y.Path)
                        return Difference(actual.Frame,path,"transformIdentity["+t+"]",x.Path,y.Path);
                    if(!Exact(x.LocalPosition,y.LocalPosition))return Difference(actual.Frame,x.Path,"localPosition",Describe(x.LocalPosition),Describe(y.LocalPosition));
                    if(!Exact(x.LocalRotation,y.LocalRotation))return Difference(actual.Frame,x.Path,"localRotation",Describe(x.LocalRotation),Describe(y.LocalRotation));
                    if(!Exact(x.LocalScale,y.LocalScale))return Difference(actual.Frame,x.Path,"localScale",Describe(x.LocalScale),Describe(y.LocalScale));
                }
            }
            return null;
        }

        private static object FirstRandomStateDifference(FrameState expected,FrameState actual)
        {
            return SameRandomState(expected.RandomState,actual.RandomState)?null:
                Difference(actual.Frame,null,"unityRandomState",DescribeRandomState(expected.RandomState),DescribeRandomState(actual.RandomState));
        }

        private static object FirstControllerInputDifference(FrameState expected,FrameState actual)
        {
            if(expected.Animators.Length!=actual.Animators.Length)
                return Difference(actual.Frame,null,"controllerInputAnimatorCount",expected.Animators.Length,actual.Animators.Length);
            for(int i=0;i<expected.Animators.Length;i++)
            {
                AnimatorState left=expected.Animators[i],right=actual.Animators[i];
                ControllerInputState a=left.ControllerInput,b=right.ControllerInput;
                if(a==null||b==null)return Difference(actual.Frame,left.Path,"controllerInputPresence",a!=null,b!=null);
                if(a.Animator!=b.Animator)return Difference(actual.Frame,left.Path,"controllerInputAnimator",Pointer(a.Animator),Pointer(b.Animator));
                if(a.Controller!=b.Controller)return Difference(actual.Frame,left.Path,"controllerInputController",Pointer(a.Controller),Pointer(b.Controller));
                if(a.ControllerConstant!=b.ControllerConstant)return Difference(actual.Frame,left.Path,"controllerInputConstant",Pointer(a.ControllerConstant),Pointer(b.ControllerConstant));
                if(a.ControllerInput!=b.ControllerInput)return Difference(actual.Frame,left.Path,"controllerInputPointer",Pointer(a.ControllerInput),Pointer(b.ControllerInput));
                if(a.Records!=b.Records)return Difference(actual.Frame,left.Path,"controllerInputRecords",Pointer(a.Records),Pointer(b.Records));
                if(a.RecordCount!=b.RecordCount)return Difference(actual.Frame,left.Path,"controllerInputRecordCount",a.RecordCount,b.RecordCount);
                if(a.OuterCount!=b.OuterCount)return Difference(actual.Frame,left.Path,"controllerInputOuterCount",a.OuterCount,b.OuterCount);
                string semanticError;
                if(AnimatorResumeSemanticState.ControllerInputsEqual(a.Bytes,b.Bytes,
                    PendingGotoStateGates(left.ControllerMemory,a.RecordCount),
                    PendingGotoStateGates(right.ControllerMemory,b.RecordCount),out semanticError))continue;
                int offset=FirstByteDifference(a.Bytes,b.Bytes);
                if(offset>=0)return new Dictionary<string,object>{{"frame",actual.Frame},{"animator",left.Path},{"component","controllerInput"},
                    {"offset",offset},{"expected",offset<a.Bytes.Length?(object)a.Bytes[offset]:null},{"actual",offset<b.Bytes.Length?(object)b.Bytes[offset]:null},
                    {"expectedHash",a.Hash.ToString("X8")},{"actualHash",b.Hash.ToString("X8")},{"semanticError",semanticError}};
            }
            return null;
        }

        private static object FirstTransitionTopologyDifference(FrameState expected,FrameState actual)
        {
            if(expected.Animators.Length!=actual.Animators.Length)
                return Difference(actual.Frame,null,"transitionTopologyAnimatorCount",expected.Animators.Length,actual.Animators.Length);
            for(int i=0;i<expected.Animators.Length;i++)
            {
                AnimatorState left=expected.Animators[i],right=actual.Animators[i];
                TransitionTopologyState a=left.TransitionTopology,b=right.TransitionTopology;
                if(a==null||b==null)return Difference(actual.Frame,left.Path,"transitionTopologyPresence",a!=null,b!=null);
                if(a.Animator!=b.Animator)return Difference(actual.Frame,left.Path,"transitionTopologyAnimator",Pointer(a.Animator),Pointer(b.Animator));
                if(a.Controller!=b.Controller)return Difference(actual.Frame,left.Path,"transitionTopologyController",Pointer(a.Controller),Pointer(b.Controller));
                if(a.ControllerMemory!=b.ControllerMemory)return Difference(actual.Frame,left.Path,"transitionTopologyControllerMemory",Pointer(a.ControllerMemory),Pointer(b.ControllerMemory));
                if(a.ControllerGraphMemory!=b.ControllerGraphMemory)return Difference(actual.Frame,left.Path,"transitionTopologyGraphMemory",Pointer(a.ControllerGraphMemory),Pointer(b.ControllerGraphMemory));
                if(a.LiveLayers!=b.LiveLayers)return Difference(actual.Frame,left.Path,"transitionTopologyLiveLayers",Pointer(a.LiveLayers),Pointer(b.LiveLayers));
                if(a.BlobCapacity!=b.BlobCapacity)return Difference(actual.Frame,left.Path,"transitionTopologyBlobCapacity",a.BlobCapacity,b.BlobCapacity);
                if(a.BlobLayerCount!=b.BlobLayerCount||a.GraphLayerCount!=b.GraphLayerCount||a.LiveLayerCount!=b.LiveLayerCount)
                    return Difference(actual.Frame,left.Path,"transitionTopologyLayerCounts",
                        new[]{a.BlobLayerCount,a.GraphLayerCount,a.LiveLayerCount},new[]{b.BlobLayerCount,b.GraphLayerCount,b.LiveLayerCount});
                int offset=FirstByteDifference(a.Bytes,b.Bytes);
                if(offset>=0)return new Dictionary<string,object>{{"frame",actual.Frame},{"animator",left.Path},{"component","transitionTopology"},
                    {"offset",offset},{"expected",offset<a.Bytes.Length?(object)a.Bytes[offset]:null},{"actual",offset<b.Bytes.Length?(object)b.Bytes[offset]:null},
                    {"expectedHash",a.Hash.ToString("X8")},{"actualHash",b.Hash.ToString("X8")},
                    {"expectedLayers",DescribeTransitionTopologyLayers(a.Bytes)},{"actualLayers",DescribeTransitionTopologyLayers(b.Bytes)}};
            }
            return null;
        }

        private static object FirstMixerGraphDifference(FrameState expected,FrameState actual)
        {
            if(expected.Animators.Length!=actual.Animators.Length)
                return Difference(actual.Frame,null,"mixerGraphAnimatorCount",expected.Animators.Length,actual.Animators.Length);
            for(int i=0;i<expected.Animators.Length;i++)
            {
                AnimatorState left=expected.Animators[i],right=actual.Animators[i];
                MixerGraphState a=left.MixerGraph,b=right.MixerGraph;
                if(a==null||b==null)return Difference(actual.Frame,left.Path,"mixerGraphPresence",a!=null,b!=null);
                if(a.Animator!=b.Animator)return Difference(actual.Frame,left.Path,"mixerGraphAnimator",Pointer(a.Animator),Pointer(b.Animator));
                if(a.Controller!=b.Controller)return Difference(actual.Frame,left.Path,"mixerGraphController",Pointer(a.Controller),Pointer(b.Controller));
                if(a.ControllerConstant!=b.ControllerConstant)return Difference(actual.Frame,left.Path,"mixerGraphControllerConstant",Pointer(a.ControllerConstant),Pointer(b.ControllerConstant));
                if(a.Descriptors!=b.Descriptors)return Difference(actual.Frame,left.Path,"mixerGraphDescriptors",Pointer(a.Descriptors),Pointer(b.Descriptors));
                if(a.LayerCount!=b.LayerCount||a.RecordCount!=b.RecordCount||a.Bytes.Length!=b.Bytes.Length)
                    return Difference(actual.Frame,left.Path,"mixerGraphShape",
                        new object[]{a.LayerCount,a.RecordCount,a.Bytes.Length},new object[]{b.LayerCount,b.RecordCount,b.Bytes.Length});
                int offset=FirstByteDifference(a.Bytes,b.Bytes);
                if(offset>=0)
                {
                    int record=offset/(int)MixerGraphRecordSize;
                    return new Dictionary<string,object>{{"frame",actual.Frame},{"animator",left.Path},{"component","mixerGraph"},
                        {"offset",offset},{"record",record},{"recordOffset",offset%(int)MixerGraphRecordSize},
                        {"expected",offset<a.Bytes.Length?(object)a.Bytes[offset]:null},{"actual",offset<b.Bytes.Length?(object)b.Bytes[offset]:null},
                        {"expectedHash",a.Hash.ToString("X8")},{"actualHash",b.Hash.ToString("X8")},
                        {"expectedRecord",DescribeMixerGraphRecord(a.Bytes,record)},{"actualRecord",DescribeMixerGraphRecord(b.Bytes,record)}};
                }
            }
            return null;
        }

        private static object FirstOwnerGraphDifference(FrameState expected,FrameState actual)
        {
            if(expected.Animators.Length!=actual.Animators.Length)
                return Difference(actual.Frame,null,"ownerGraphAnimatorCount",expected.Animators.Length,actual.Animators.Length);
            for(int i=0;i<expected.Animators.Length;i++)
            {
                AnimatorState left=expected.Animators[i],right=actual.Animators[i];
                OwnerGraphState a=left.OwnerGraph,b=right.OwnerGraph;
                if(a==null||b==null)
                {
                    if(a==null&&b==null)continue;
                    return Difference(actual.Frame,left.Path,"ownerGraphPresence",a!=null,b!=null);
                }
                if(a.Animator!=b.Animator)return Difference(actual.Frame,left.Path,"ownerGraphAnimator",Pointer(a.Animator),Pointer(b.Animator));
                if(a.Controller!=b.Controller)return Difference(actual.Frame,left.Path,"ownerGraphController",Pointer(a.Controller),Pointer(b.Controller));
                if(a.ControllerConstant!=b.ControllerConstant)return Difference(actual.Frame,left.Path,"ownerGraphControllerConstant",Pointer(a.ControllerConstant),Pointer(b.ControllerConstant));
                if(a.Descriptors!=b.Descriptors)return Difference(actual.Frame,left.Path,"ownerGraphDescriptors",Pointer(a.Descriptors),Pointer(b.Descriptors));
                if(a.Graph!=b.Graph)return Difference(actual.Frame,left.Path,"ownerGraphGraph",Pointer(a.Graph),Pointer(b.Graph));
                if(a.GraphDirty58!=b.GraphDirty58)return Difference(actual.Frame,left.Path,"ownerGraphGraphDirty58",
                    "0x"+a.GraphDirty58.ToString("X8"),"0x"+b.GraphDirty58.ToString("X8"));
                if(a.LayerCount!=b.LayerCount||a.RecordCount!=b.RecordCount||a.Bytes.Length!=b.Bytes.Length)
                    return Difference(actual.Frame,left.Path,"ownerGraphShape",
                        new object[]{a.LayerCount,a.RecordCount,a.Bytes.Length},new object[]{b.LayerCount,b.RecordCount,b.Bytes.Length});
                int offset=FirstOwnerGraphSemanticDifference(a.Bytes,b.Bytes);
                if(offset>=0)
                {
                    int record=offset/(int)OwnerGraphRecordSize;
                    return new Dictionary<string,object>{{"frame",actual.Frame},{"animator",left.Path},{"component","ownerGraph"},
                        {"offset",offset},{"record",record},{"recordOffset",offset%(int)OwnerGraphRecordSize},
                        {"expected",offset<a.Bytes.Length?(object)a.Bytes[offset]:null},{"actual",offset<b.Bytes.Length?(object)b.Bytes[offset]:null},
                        {"expectedHash",a.Hash.ToString("X8")},{"actualHash",b.Hash.ToString("X8")},
                        {"expectedRecord",DescribeOwnerGraphRecord(a.Bytes,record)},{"actualRecord",DescribeOwnerGraphRecord(b.Bytes,record)}};
                }
            }
            return null;
        }

        private static int FirstOwnerGraphSemanticDifference(byte[] expected,byte[] actual)
        {
            int count=Math.Min(expected.Length,actual.Length);
            for(int offset=0;offset<count;offset++)
            {
                if(expected[offset]==actual[offset])continue;
                int record=offset/(int)OwnerGraphRecordSize;
                int recordOffset=offset%(int)OwnerGraphRecordSize;
                if(IsOwnerAllocatorPointerByte(recordOffset)&&EquivalentOwnerAllocatorRebase(expected,actual,record))continue;
                return offset;
            }
            return expected.Length==actual.Length?-1:count;
        }

        private static bool IsOwnerAllocatorPointerByte(int recordOffset)
        {
            int[] offsets={152,184,156,196,160};
            for(int i=0;i<offsets.Length;i++)if(recordOffset>=offsets[i]&&recordOffset<offsets[i]+4)return true;
            return false;
        }

        private static bool EquivalentOwnerAllocatorRebase(byte[] expected,byte[] actual,int record)
        {
            int offset=checked(record*(int)OwnerGraphRecordSize);
            if(offset<0||offset+(int)OwnerGraphRecordSize>expected.Length||offset+(int)OwnerGraphRecordSize>actual.Length)return false;
            uint expectedKind=BitConverter.ToUInt32(expected,offset),actualKind=BitConverter.ToUInt32(actual,offset);
            if(expectedKind<20u||expectedKind>22u||actualKind!=expectedKind)return false;
            int[] exactWords={180,188,192,200,204,208};
            for(int i=0;i<exactWords.Length;i++)
                if(BitConverter.ToUInt32(expected,offset+exactWords[i])!=BitConverter.ToUInt32(actual,offset+exactWords[i]))return false;
            uint presence=BitConverter.ToUInt32(expected,offset+180);
            int[] pointerOffsets={152,184,156,196,160};uint[] bits={1u,2u,4u,8u,16u};
            uint[] expectedPointers=new uint[pointerOffsets.Length],actualPointers=new uint[pointerOffsets.Length];
            for(int i=0;i<pointerOffsets.Length;i++)
            {
                expectedPointers[i]=BitConverter.ToUInt32(expected,offset+pointerOffsets[i]);
                actualPointers[i]=BitConverter.ToUInt32(actual,offset+pointerOffsets[i]);
                bool expectedPresent=expectedPointers[i]!=0,actualPresent=actualPointers[i]!=0,marked=(presence&bits[i])!=0;
                if(expectedPresent!=marked||actualPresent!=marked)return false;
                for(int prior=0;prior<i;prior++)
                    if((expectedPointers[i]==expectedPointers[prior])!=(actualPointers[i]==actualPointers[prior]))return false;
            }
            return true;
        }

        private static object DescribeOwnerGraphRecord(byte[] bytes,int index)
        {
            int offset=checked(index*(int)OwnerGraphRecordSize);
            if(bytes==null||offset<0||offset+(int)OwnerGraphRecordSize>bytes.Length)return null;
            return new Dictionary<string,object>{{"kind",BitConverter.ToUInt32(bytes,offset)},
                {"layer",BitConverter.ToUInt32(bytes,offset+4)},{"stateMachine",BitConverter.ToUInt32(bytes,offset+8)},
                {"branch",BitConverter.ToUInt32(bytes,offset+12)},{"input",BitConverter.ToUInt32(bytes,offset+16)},
                {"self",Pointer(BitConverter.ToUInt32(bytes,offset+20))},{"vtable",Pointer(BitConverter.ToUInt32(bytes,offset+24))},
                {"internal",Pointer(BitConverter.ToUInt32(bytes,offset+28))},{"graph",Pointer(BitConverter.ToUInt32(bytes,offset+32))},
                {"inputEntries",Pointer(BitConverter.ToUInt32(bytes,offset+36))},{"inputCount",BitConverter.ToUInt32(bytes,offset+40)},
                {"inputCapacityRaw","0x"+BitConverter.ToUInt32(bytes,offset+44).ToString("X8")},
                {"outputEntries",Pointer(BitConverter.ToUInt32(bytes,offset+48))},{"outputCount",BitConverter.ToUInt32(bytes,offset+52)},
                {"outputCapacityRaw","0x"+BitConverter.ToUInt32(bytes,offset+56).ToString("X8")},
                {"flags7C","0x"+BitConverter.ToUInt32(bytes,offset+60).ToString("X8")},
                {"dirty9093","0x"+BitConverter.ToUInt32(bytes,offset+64).ToString("X8")},
                {"rawA0A3","0x"+BitConverter.ToUInt32(bytes,offset+68).ToString("X8")},
                {"rawA4A7","0x"+BitConverter.ToUInt32(bytes,offset+72).ToString("X8")},
                {"word50","0x"+BitConverter.ToUInt32(bytes,offset+76).ToString("X8")},
                {"clip108",Pointer(BitConverter.ToUInt32(bytes,offset+80))},{"entryAddress",Pointer(BitConverter.ToUInt32(bytes,offset+84))},
                {"entryWeightBits","0x"+BitConverter.ToUInt32(bytes,offset+88).ToString("X8")},
                {"entryPlayable",Pointer(BitConverter.ToUInt32(bytes,offset+92))},{"entryPortRaw","0x"+BitConverter.ToUInt32(bytes,offset+96).ToString("X8")},
                {"resolverOrigin",Pointer(BitConverter.ToUInt32(bytes,offset+100))},{"resolverResult",Pointer(BitConverter.ToUInt32(bytes,offset+104))},
                {"resolverDepth",BitConverter.ToUInt32(bytes,offset+108)},{"resolverStatus",BitConverter.ToUInt32(bytes,offset+112)},
                {"currentTime28Bits","0x"+BitConverter.ToUInt32(bytes,offset+120).ToString("X8")+BitConverter.ToUInt32(bytes,offset+116).ToString("X8")},
                {"previousTime30Bits","0x"+BitConverter.ToUInt32(bytes,offset+128).ToString("X8")+BitConverter.ToUInt32(bytes,offset+124).ToString("X8")},
                {"duration38Bits","0x"+BitConverter.ToUInt32(bytes,offset+136).ToString("X8")+BitConverter.ToUInt32(bytes,offset+132).ToString("X8")},
                {"mode80","0x"+BitConverter.ToUInt32(bytes,offset+140).ToString("X8")},
                {"raw9497","0x"+BitConverter.ToUInt32(bytes,offset+144).ToString("X8")},
                {"bindingA8",Pointer(BitConverter.ToUInt32(bytes,offset+148))},{"bindingAC",Pointer(BitConverter.ToUInt32(bytes,offset+152))},
                {"bindingB0",Pointer(BitConverter.ToUInt32(bytes,offset+156))},{"bindingB4",Pointer(BitConverter.ToUInt32(bytes,offset+160))},
                {"clipCacheBC","0x"+BitConverter.ToUInt32(bytes,offset+164).ToString("X8")},
                {"clipCacheC0","0x"+BitConverter.ToUInt32(bytes,offset+168).ToString("X8")},
                {"clipInternalWeightBits","0x"+BitConverter.ToUInt32(bytes,offset+172).ToString("X8")},
                {"clipFlags10C","0x"+BitConverter.ToUInt32(bytes,offset+176).ToString("X8")},
                {"bindingCachePresence","0x"+BitConverter.ToUInt32(bytes,offset+180).ToString("X8")},
                {"bindingACInner",Pointer(BitConverter.ToUInt32(bytes,offset+184))},{"bindingACCount",BitConverter.ToUInt32(bytes,offset+188)},
                {"bindingACHash",BitConverter.ToUInt32(bytes,offset+192).ToString("X8")},
                {"bindingB0Inner",Pointer(BitConverter.ToUInt32(bytes,offset+196))},{"bindingB0Count",BitConverter.ToUInt32(bytes,offset+200)},
                {"bindingB0Hash",BitConverter.ToUInt32(bytes,offset+204).ToString("X8")},
                {"bindingB4Hash",BitConverter.ToUInt32(bytes,offset+208).ToString("X8")}};
        }

        private static object DescribeMixerGraphRecord(byte[] bytes,int index)
        {
            int offset=checked(index*(int)MixerGraphRecordSize);
            if(bytes==null||offset<0||offset+(int)MixerGraphRecordSize>bytes.Length)return null;
            return new Dictionary<string,object>{{"kind",BitConverter.ToUInt32(bytes,offset)},
                {"layer",BitConverter.ToUInt32(bytes,offset+4)},{"stateMachine",BitConverter.ToUInt32(bytes,offset+8)},
                {"trueBranch",BitConverter.ToUInt32(bytes,offset+12)},{"input",BitConverter.ToUInt32(bytes,offset+16)},
                {"outer",Pointer(BitConverter.ToUInt32(bytes,offset+20))},{"outerInternal",Pointer(BitConverter.ToUInt32(bytes,offset+24))},
                {"outerEntries",Pointer(BitConverter.ToUInt32(bytes,offset+28))},{"outerInputCount",BitConverter.ToUInt32(bytes,offset+32)},
                {"outerEntriesHash",BitConverter.ToUInt32(bytes,offset+36).ToString("X8")},{"outerModeA0",BitConverter.ToUInt32(bytes,offset+40)},
                {"outerArgumentA4",BitConverter.ToUInt32(bytes,offset+44)},{"outerWeightBits",BitConverter.ToUInt32(bytes,offset+48).ToString("X8")},
                {"outerChild",Pointer(BitConverter.ToUInt32(bytes,offset+52))},{"outerWord8",BitConverter.ToUInt32(bytes,offset+56).ToString("X8")},
                {"mixer",Pointer(BitConverter.ToUInt32(bytes,offset+60))},{"mixerInternal",Pointer(BitConverter.ToUInt32(bytes,offset+64))},
                {"mixerEntries",Pointer(BitConverter.ToUInt32(bytes,offset+68))},{"mixerInputCount",BitConverter.ToUInt32(bytes,offset+72)},
                {"mixerFlagA5",BitConverter.ToUInt32(bytes,offset+76)},{"mixerWeightBits",BitConverter.ToUInt32(bytes,offset+80).ToString("X8")},
                {"mixerChild",Pointer(BitConverter.ToUInt32(bytes,offset+84))},{"mixerWord8",BitConverter.ToUInt32(bytes,offset+88).ToString("X8")}};
        }

        private static object[] DescribeTransitionTopologyLayers(byte[] bytes)
        {
            if(bytes==null||bytes.Length%28!=0)return new object[0];
            object[] rows=new object[bytes.Length/28];
            for(int i=0;i<rows.Length;i++)
            {
                int offset=i*28;uint blob=BitConverter.ToUInt32(bytes,offset+12),mode=BitConverter.ToUInt32(bytes,offset+20);
                rows[i]=new Dictionary<string,object>{{"index",BitConverter.ToUInt32(bytes,offset)},
                    {"live",Pointer(BitConverter.ToUInt32(bytes,offset+4))},{"node",Pointer(BitConverter.ToUInt32(bytes,offset+8))},
                    {"blobInterruptedRaw",blob},{"nodeStartArgumentRaw",BitConverter.ToUInt32(bytes,offset+16)},
                    {"liveModeRaw",mode},{"liveSecondaryArgumentRaw",BitConverter.ToUInt32(bytes,offset+24)},
                    {"topologyConsistent",(blob!=0)==(mode==0)}};
            }
            return rows;
        }

        private static int[] ControllerStateOffsets(byte[] memory,uint expectedLayerCount)
        {
            if(memory==null||memory.Length<8)throw new InvalidOperationException("ControllerMemory is too small for its root table.");
            uint count=BitConverter.ToUInt32(memory,0);
            if(count!=expectedLayerCount)throw new InvalidOperationException("ControllerMemory layer count does not match the native layer count.");
            long entries=4L+BitConverter.ToInt32(memory,4);
            if(entries<0||entries+4L*count>memory.Length)throw new InvalidOperationException("ControllerMemory layer table is out of bounds.");
            int[] offsets=new int[checked((int)count)];
            for(uint layer=0;layer<count;++layer)
            {
                int entry=checked((int)(entries+4L*layer));
                long state=entry+(long)BitConverter.ToInt32(memory,entry);
                if(state<0||state+0x70>memory.Length)throw new InvalidOperationException("ControllerMemory layer state is out of bounds.");
                offsets[checked((int)layer)]=checked((int)state);
            }
            return offsets;
        }

        private static bool[] PendingGotoStateGates(byte[] memory,uint expectedLayerCount)
        {
            int[] offsets=ControllerStateOffsets(memory,expectedLayerCount);
            bool[] result=new bool[offsets.Length];
            for(int i=0;i<offsets.Length;++i)result[i]=memory[offsets[i]+0x6B]!=0;
            return result;
        }

        private static object[] DescribeControllerLifecycleLayers(byte[] memory,uint expectedLayerCount)
        {
            int[] offsets=ControllerStateOffsets(memory,expectedLayerCount);
            object[] rows=new object[offsets.Length];
            for(int layer=0;layer<offsets.Length;++layer)
            {
                int offset=offsets[layer];
                byte interrupted=memory[offset+0x68],inTransition=memory[offset+0x69];
                byte dynamicTransition=memory[offset+0x6A],activeGoto=memory[offset+0x6B];
                byte fixedTransition=memory[offset+0x6C],cleanAfterTransition=memory[offset+0x6D];
                byte resetPlayableGraph=memory[offset+0x6E];
                string phase;
                if(inTransition!=0)
                    phase=interrupted!=0
                        ?(dynamicTransition!=0?"transitioning-interrupted-dynamic":"transitioning-interrupted-authored")
                        :(dynamicTransition!=0?"transitioning-dynamic":"transitioning-authored");
                else if(cleanAfterTransition!=0)
                    phase=interrupted!=0?"settled-pending-clean-interrupted":"settled-pending-clean";
                else phase=interrupted!=0?"settled-interrupted-topology":"settled-clean";
                bool dynamicWithoutTransition=dynamicTransition!=0&&inTransition==0;
                rows[layer]=new Dictionary<string,object>{{"layer",layer},{"blobOffset",offset},{"phase",phase},
                    {"currentState",BitConverter.ToInt32(memory,offset+0x08)},{"nextState",BitConverter.ToInt32(memory,offset+0x0C)},
                    {"transitionIndex",BitConverter.ToInt32(memory,offset+0x18)},
                    {"transitionStartTimeBits","0x"+BitConverter.ToUInt32(memory,offset+0x58).ToString("X8")},
                    {"transitionTimeBits","0x"+BitConverter.ToUInt32(memory,offset+0x5C).ToString("X8")},
                    {"transitionDurationBits","0x"+BitConverter.ToUInt32(memory,offset+0x60).ToString("X8")},
                    {"transitionOffsetBits","0x"+BitConverter.ToUInt32(memory,offset+0x64).ToString("X8")},
                    {"inInterruptedTransitionRaw",interrupted},{"inTransitionRaw",inTransition},
                    {"inDynamicTransitionRaw",dynamicTransition},{"activeGotoStateRaw",activeGoto},
                    {"fixedTransitionRaw",fixedTransition},{"cleanAfterTransitionRaw",cleanAfterTransition},
                    {"resetPlayableGraphRaw",resetPlayableGraph},{"dynamicWithoutTransitionInvariantWarning",dynamicWithoutTransition}};
            }
            return rows;
        }

        private static object Difference(int frame,string animator,string component,object expected,object actual)
        {
            return new Dictionary<string,object>{{"frame",frame},{"animator",animator},{"component",component},
                {"expected",expected},{"actual",actual}};
        }

        private static object Describe(float value)
        {
            return new Dictionary<string,object>{{"value",value},{"bits",Bits(value)}};
        }

        private static object Describe(ParameterState value)
        {
            return new Dictionary<string,object>{{"hash",value.Hash},{"type",value.Type.ToString()},
                {"float",Describe(value.Float)},{"int",value.Int},{"bool",value.Bool}};
        }

        private static object Describe(LayerState value)
        {
            return new Dictionary<string,object>{{"fullPathHash",value.FullPathHash},{"shortNameHash",value.ShortNameHash},
                {"tagHash",value.TagHash},{"normalizedTime",Describe(value.NormalizedTime)},
                {"weight",Describe(value.Weight)},{"loop",value.Loop}};
        }

        private static int[] RandomStateWords(UnityEngine.Random.State state)
        {
            object boxed=state;
            return unityRandomStateFields.Select(value=>(int)value.GetValue(boxed)).ToArray();
        }

        private static bool SameRandomState(UnityEngine.Random.State left,UnityEngine.Random.State right)
        {
            int[] a=RandomStateWords(left),b=RandomStateWords(right);
            return a.Length==b.Length&&a.SequenceEqual(b);
        }

        private static object DescribeRandomState(UnityEngine.Random.State state)
        {
            return RandomStateWords(state).Select(value=>(object)("0x"+unchecked((uint)value).ToString("X8"))).ToArray();
        }

        private static object DescribeRandomState(int[] words)
        {
            return words.Select(value=>(object)("0x"+unchecked((uint)value).ToString("X8"))).ToArray();
        }

        private static object Describe(ChefRandomizeEvent value)
        {
            return new Dictionary<string,object>{{"animatorInstanceId",value.AnimatorInstanceId},{"animator",value.AnimatorPath},
                {"layer",value.Layer},{"fullPathHash",value.FullPathHash},{"shortNameHash",value.ShortNameHash},
                {"parameterHash",value.ParameterHash},{"parameterBefore",value.ParameterBefore},
                {"parameterValue",value.ParameterValue},{"lastCapturedOutputFrame",value.LastCapturedOutputFrame},
                {"randomBefore",DescribeRandomState(value.Before)},{"randomAfter",DescribeRandomState(value.After)}};
        }

        private static bool Exact(Vector3 left,Vector3 right)
        {
            return left.x.Equals(right.x)&&left.y.Equals(right.y)&&left.z.Equals(right.z);
        }

        private static bool Exact(Quaternion left,Quaternion right)
        {
            return left.x.Equals(right.x)&&left.y.Equals(right.y)&&left.z.Equals(right.z)&&left.w.Equals(right.w);
        }

        private object Status(string operation)
        {
            object[] live=new object[0];string liveError=null;
            try{live=Describe(CaptureAll());}
            catch(Exception error){liveError=error.Message;}
            return new Dictionary<string,object> {
                {"name",Name},{"apiVersion",1},{"operation",operation},{"active",ReferenceEquals(active,this)},
                {"liveServerFrame",Hpmv.Injector.Server==null?-1:Hpmv.Injector.Server.CurrentFrameData.FrameNumber},
                {"historyCount",history.Count},{"firstFrame",history.Count==0?-1:history.Keys.First()},
                {"lastFrame",history.Count==0?-1:history.Keys.Last()},{"captures",captures},{"duplicateCaptures",duplicateCaptures},
                {"restores",restores},{"resumeControllerRestores",resumeControllerRestores},
                {"controllerCaptures",controllerCaptures},{"controllerCaptureBytes",controllerCaptureBytes},
                {"controllerInputCaptures",controllerInputCaptures},{"controllerInputCaptureBytes",controllerInputCaptureBytes},
                {"transitionTopologyCaptures",transitionTopologyCaptures},{"transitionTopologyCaptureBytes",transitionTopologyCaptureBytes},
                {"mixerGraphCaptures",mixerGraphCaptures},{"mixerGraphCaptureBytes",mixerGraphCaptureBytes},
                {"mixerGraphRestores",mixerGraphRestores},{"mixerWeightWrites",mixerWeightWrites},
                {"ownerGraphCaptures",ownerGraphCaptures},{"ownerGraphCaptureBytes",ownerGraphCaptureBytes},
                {"ownerGraphSceneCaptureBytes",ownerGraphSceneCaptureBytes},
                {"settledEndTransitionNormalizeAttempts",settledEndTransitionNormalizeAttempts},
                {"settledEndTransitionPlanned",settledEndTransitionPlanned},
                {"settledEndTransitionCompleted",settledEndTransitionCompleted},
                {"settledEndTransitionNormalizeObservations",settledEndTransitionNormalizeObservations.ToArray()},
                {"lastSettledEndTransitionNormalize",lastSettledEndTransitionNormalize},
                {"playableTimeRestoreAttempts",playableTimeRestoreAttempts},{"playableTimeRestorePlans",playableTimeRestorePlans},
                {"playableTimeRestoreCompleted",playableTimeRestoreCompleted},
                {"playableTimeRestoreObservations",playableTimeRestoreObservations.ToArray()},
                {"lastPlayableTimeRestore",lastPlayableTimeRestore},
                {"targetNullClipRestoreAttempts",targetNullClipRestoreAttempts},{"targetNullClipRestorePlans",targetNullClipRestorePlans},
                {"targetNullClipRestoreCompleted",targetNullClipRestoreCompleted},
                {"targetNullScalarRestorePlans",targetNullScalarRestorePlans},
                {"targetNullScalarRestoreCompleted",targetNullScalarRestoreCompleted},
                {"targetNullClipRestoreObservations",targetNullClipRestoreObservations.ToArray()},
                {"lastTargetNullClipRestore",lastTargetNullClipRestore},
                {"overrideClipAttempts",overrideClipAttempts},{"overrideClipMutations",overrideClipMutations},
                {"overrideClipRestoreObservations",overrideClipRestoreObservations.ToArray()},
                {"lastOverrideClipRestore",lastOverrideClipRestore},
                {"ownerGraphCaptureBudgetBytes",MaximumOwnerGraphCaptureBytes},
                {"ownerGraphCaptureBudgetRemainingBytes",Math.Max(0L,MaximumOwnerGraphCaptureBytes-ownerGraphSceneCaptureBytes)},
                {"ownerGraphCapturePolicy",new Dictionary<string,object>{{"outputBoundary",false},{"referencePrefix",true},
                    {"referencePost",false},{"replayPre",true},{"replayPost",true},{"overrideClipGuard",true}}},
                {"controllerNormalizations",controllerNormalizations},{"controllerRestores",controllerRestores},{"poseAssignments",poseAssignments},
                {"posePreimageAdjustments",posePreimageAdjustments},{"resumeFrame",resumeFrame==null?-1:resumeFrame.Frame},
                {"resumeReadyFrame",resumeReadyFrame==null?-1:resumeReadyFrame.Frame},{"resumeRestoreApplied",resumeRestoreApplied},
                {"resumeRestoreStage",resumeRestoreStage},{"resumeStageAUnityFrame",resumeStageAUnityFrame},
                {"resumeStageBUnityFrame",resumeStageBUnityFrame},{"resumeArmedUnityFrame",resumeArmedUnityFrame},
                {"resumeArmedPhase",resumeArmedPhase},{"resumeStageAHolds",resumeStageAHolds},
                {"resumeStageBHolds",resumeStageBHolds},{"resumeFinalizationArms",resumeFinalizationArms},
                {"lastResumeCoordination",lastResumeCoordination},
                {"resumeReadyRestores",resumeReadyRestores},{"resumeCompletions",resumeCompletions},
                {"resumeCompletionFailures",resumeCompletionFailures},{"lastResumeCompletion",lastResumeCompletion},
                {"unityRandomStateRestores",unityRandomStateRestores},{"lastUnityRandomStateRestore",lastUnityRandomStateRestore},
                {"unityRandomBoundaryCorrections",unityRandomBoundaryCorrections},
                {"lastUnityRandomBoundaryCorrection",lastUnityRandomBoundaryCorrection},
                {"liveUnityRandomState",DescribeRandomState(UnityEngine.Random.state)},
                {"replayReferenceFrames",replayReference.Count},{"replayFrameComparisons",replayFrameComparisons},
                {"resumePrefixReferenceFrames",resumePrefixReference.Keys.ToArray()},
                {"ambiguousResumePrefixFrames",ambiguousResumePrefix.OrderBy(value=>value).ToArray()},
                {"scheduledResumeReadyCapturePending",scheduledResumeReadyCaptureFrame>=0},
                {"scheduledResumeReadyCaptureFrame",scheduledResumeReadyCaptureFrame},
                {"scheduledResumeReadyLastObservedFrame",scheduledResumeReadyLastObservedFrame},
                {"scheduledResumeReadyCaptureArms",scheduledResumeReadyCaptureArms},
                {"scheduledResumeReadyCaptureTriggers",scheduledResumeReadyCaptureTriggers},
                {"scheduledResumeReadyCaptureFailure",scheduledResumeReadyCaptureFailure},
                {"lastScheduledResumeReadyCapture",lastScheduledResumeReadyCapture},
                {"resumePrefixReferenceCaptures",resumePrefixReferenceCaptures},
                {"resumePrefixReferencePostCaptures",resumePrefixReferencePostCaptures},
                {"resumePrefixReplayPreCaptures",resumePrefixReplayPreCaptures},
                {"resumePrefixReplayPostCaptures",resumePrefixReplayPostCaptures},
                {"resumePrefixObserverFailures",resumePrefixObserverFailures},
                {"resumePrefixObserverFailure",resumePrefixObserverFailure},
                {"lastResumePrefixObservation",lastResumePrefixObservation},
                {"lastResumePrefixReferencePostObservation",lastResumePrefixReferencePostObservation},
                {"firstResumePrefixPreDifference",firstResumePrefixPreDifference},
                {"firstResumePrefixPreRandomStateDifference",firstResumePrefixPreRandomStateDifference},
                {"firstResumePrefixPreControllerInputDifference",firstResumePrefixPreControllerInputDifference},
                {"firstResumePrefixPreTransitionTopologyDifference",firstResumePrefixPreTransitionTopologyDifference},
                {"firstResumePrefixPreMixerGraphDifference",firstResumePrefixPreMixerGraphDifference},
                {"firstResumePrefixPreOwnerGraphDifference",firstResumePrefixPreOwnerGraphDifference},
                {"firstResumePrefixPostDifference",firstResumePrefixPostDifference},
                {"firstResumePrefixPostRandomStateDifference",firstResumePrefixPostRandomStateDifference},
                {"firstResumePrefixPostControllerInputDifference",firstResumePrefixPostControllerInputDifference},
                {"firstResumePrefixPostTransitionTopologyDifference",firstResumePrefixPostTransitionTopologyDifference},
                {"firstResumePrefixPostMixerGraphDifference",firstResumePrefixPostMixerGraphDifference},
                {"firstResumePrefixPostOwnerGraphDifference",firstResumePrefixPostOwnerGraphDifference},
                {"firstReplayDifference",firstReplayDifference},
                {"firstRandomStateReplayDifference",firstRandomStateReplayDifference},
                {"firstRandomStateReplayPreCorrectionDifference",firstRandomStateReplayPreCorrectionDifference},
                {"firstChefRandomizeReplayDifference",firstChefRandomizeReplayDifference},
                {"chefRandomizeFailure",chefRandomizeFailure},
                {"chefRandomizeReferenceCount",chefRandomizeReference.Count},
                {"trackedChefAnimatorIds",trackedChefAnimatorIds.OrderBy(value=>value).ToArray()},
                {"replayChefRandomizeCursor",replayChefRandomizeCursor},
                {"replayChefRandomizeLimit",replayChefRandomizeLimit},
                {"chefRandomizeMode",chefRandomizeMode.ToString()},
                {"replayChefRandomizeActive",chefRandomizeMode==ChefRandomizeMode.ReplayActive},
                {"chefRandomizeReplayRebases",chefRandomizeReplayRebases},
                {"replayPrefixCommits",replayPrefixCommits},{"lastReplayPrefixCommit",lastReplayPrefixCommit},
                {"firstControllerInputReplayDifference",firstControllerInputReplayDifference},
                {"firstTransitionTopologyReplayDifference",firstTransitionTopologyReplayDifference},
                {"firstMixerGraphReplayDifference",firstMixerGraphReplayDifference},
                {"firstOwnerGraphReplayDifference",firstOwnerGraphReplayDifference},
                {"controllerInputRestoreObservations",controllerInputRestoreObservations.ToArray()},
                {"transitionTopologyRestoreObservations",transitionTopologyRestoreObservations.ToArray()},
                {"mixerGraphRestoreObservations",mixerGraphRestoreObservations.ToArray()},
                {"randomizeAnimParamObservations",randomizeAnimParamObservations.ToArray()},
                {"pendingResumeRestore",pendingResumeRestore},{"warpTarget",warpTarget},{"warpEligible",warpEligible},{"restoreApplied",restoreApplied},{"failure",failure},{"lastRestore",lastRestore},
                {"lastControllerRestore",lastControllerRestore},{"lastMixerGraphRestore",lastMixerGraphRestore},
                {"lastControllerNormalization",lastControllerNormalization},{"lastConfigurationObservation",lastConfigurationObservation},
                {"lastPoseRestore",lastPoseRestore},
                {"resumeFailure",resumeFailure},{"lastPoseFailure",lastPoseFailure},{"lastProbe",lastProbe},
                {"lastControllerProbe",lastControllerProbe},
                {"nativePath",nativePath},{"nativeSha256",nativeSha256},{"unityPlayerBase",unityPlayerBase==0?null:"0x"+unityPlayerBase.ToString("X8")},
                {"live",live},{"liveError",liveError},
                {"scope","Authoring-only chef Animator rewind checkpoint, including transition frames. Revision 57 distinguishes an uninterrupted scheduled output-boundary template from a naturally paused resume-prefix template: only the scheduled form receives one final byte-exact ControllerMemory restore at the last Helpers.Resume prefix, after the two required paused maintenance frames and before the existing mixer, Playable-time, transition, input, topology, owner, pose, and RNG verification. Ordinary resume-prefix checkpoints retain their established lifecycle unchanged. Revision 56 projects a scheduled advancing-boundary resume tuple into the exact TimeManager-owned paused speed state by first proving ControllerInput +0 equals the captured public Animator speed, then cloning checkpoint data and zeroing only that four-byte word plus the saved public speed; no live Animator or game state is written, and every other tuple component remains strict. Revision 55 identifies each chef's directly owned Player/Chef Animator rather than rejecting frames where a held or attached object temporarily contributes another descendant Animator; this changes checkpoint observation membership only and does not write game state. Revision 54 may arm one pause-fenced read-only capture for an exact future output boundary, allowing a resume-ready tuple inside a continuous logical-input chunk without inserting a behavior-changing pause. It publishes the template only after the ordinary boundary exists and the established prefix/post observations remain exact; skipped, ambiguous, or scene-invalidated work fails closed. Revision 53 limits public pose capture and restoration to the Animator-owned rig: a nested server/client world-object synchroniser ends pose ownership, so runtime-held plates and food visuals inherit the exact animated attachment-bone pose but remain owned by the attachment/body/lifecycle restorers. This filter is observational during ordinary forward play and does not alter the attachment point or plate physics. Revision 52 added an explicit fail-closed branch transaction keyed by the controller's paused output frame: an exact paused replay prefix, or the exact restored target before its first divergent advancing frame, may discard only the abandoned future comparison frames and RandomizeAnimParam callback tail. The explicit frame must be the latest captured output boundary; the diagnostic live CurrentFrameData value is retained separately because a paused hot-call can observe the following exchange. The transaction mutates module-owned reference bookkeeping only and reports gameStateMutation=false; divergent or incomplete prefixes are rejected before truncation. The revision otherwise retains the exact original RNG preimage, post-state, callback identity, parameter preimage/result, and last captured output-boundary watermark for every tracked chef RandomizeAnimParam.OnStateEnter callback. Exact Animator instance membership and explicit Record, ReplayStaged, ReplayActive, and Faulted lifecycle states prevent paused restore maintenance or callbacks outside the retained interval from recording or consuming events. Replay arms only after the final resume-ready tuple verifies, and boundary counts plus the fixed interval endpoint fail closed before RNG correction. Exact output-boundary RNG correction remains a second guard against unrewound decorative NPC and traffic Animator callbacks that share UnityEngine.Random but run on unrelated render-frame timing. Ordinary forward callbacks and random draws are observed but never changed. Complete owner graphs are retained for stored reference resume-prefix snapshots, replay-pre transaction observation, final replay-post verification, and OverrideClipPlayables mutation guards; ordinary output boundaries and diagnostic reference-post observations retain the rest of the native tuple without traversing the owner graph. Replay-pre traversal remains because removing the native read-only capture changed the subsequent paused-maintenance result in the transitioning-target-from-settled matrix cell; its ordering or timing dependency is not yet explained. Stage A admits Unity's idempotent OverrideClipPlayables no-op only when native before/after digests and a fresh managed full owner-graph byte capture are exact; changed bindings retain the original dirty-bit contract. Stage-B Playable clock restoration requires every saved physical node to exist, restores only those saved nodes, and leaves extra live resolver-only nodes untouched for the guarded EndTransition transaction; the final no-plan verification remains exact after those nodes become unreachable. Stage A and Stage B otherwise retain the guarded native target-null, Playable clock, EndTransition, mixer, owner, and pose restoration transactions. The null-inactive scalar finalizer skips rotated state machines only after validating their stable entry storage and port topology; rotated outer playable identities are intentionally classified after that structural check. Opaque branch-output port storage must match the checkpoint and remain exact through live preflight, but is not assumed to be zero; only the separately addressed output-weight word is writable. Accepted input provenance and exactly-once commit are owned by ResumePhase. Gameplay input, physics, score, and online synchronization behavior are untouched; decorative Animator visual parity is not claimed and this development revision is not search-qualified."}
            };
        }

        private static object[] Describe(AnimatorState[] states)
        {
            var rows=new object[states.Length];
            for(int i=0;i<states.Length;i++)
            {
                AnimatorState state=states[i];
                rows[i]=new Dictionary<string,object>{{"instanceId",state.InstanceId},{"path",state.Path},{"enabled",state.Enabled},
                    {"speed",state.Speed},{"updateMode",state.UpdateMode.ToString()},{"cullingMode",state.CullingMode.ToString()},
                    {"parameterCount",state.Parameters.Length},{"transformCount",state.Transforms.Length},
                    {"descendantTransformCount",state.DescendantTransformCount},{"foreignTransformCount",state.ForeignTransformCount},
                    {"layers",state.Layers.Select(value=>(object)new Dictionary<string,object>{
                        {"fullPathHash",value.FullPathHash},{"shortNameHash",value.ShortNameHash},{"normalizedTime",value.NormalizedTime},
                        {"weight",value.Weight},{"tagHash",value.TagHash},{"loop",value.Loop}}).ToArray()}};
            }
            return rows;
        }

        private static object[] DescribeResumePrefix(AnimatorState[] states)
        {
            var rows=new object[states.Length];
            for(int i=0;i<states.Length;i++)
            {
                AnimatorState state=states[i];byte[] memory=state.ControllerMemory;
                int[] stateOffsets=state.ControllerInput==null?new int[0]:ControllerStateOffsets(memory,state.ControllerInput.RecordCount);
                int layer0DurationOffset=stateOffsets.Length==0?-1:stateOffsets[0]+0x34;
                object layer0DurationBits=layer0DurationOffset<0?null:
                    (object)("0x"+BitConverter.ToUInt32(memory,layer0DurationOffset).ToString("X8"));
                rows[i]=new Dictionary<string,object>{{"instanceId",state.InstanceId},{"path",state.Path},
                    {"enabled",state.Enabled},{"publicSpeed",Describe(state.Speed)},
                    {"controllerMemoryBytes",memory==null?0:memory.Length},
                    {"controllerMemoryHash",memory==null?null:ByteHash(memory).ToString("X8")},
                    {"layer0CurrentStateDurationBlobOffset",layer0DurationOffset<0?null:(object)("0x"+layer0DurationOffset.ToString("X"))},
                    {"layer0CurrentStateDurationBits",layer0DurationBits},
                    {"controllerInputHash",state.ControllerInput==null?null:state.ControllerInput.Hash.ToString("X8")},
                    {"transitionTopologyHash",state.TransitionTopology==null?null:state.TransitionTopology.Hash.ToString("X8")},
                    {"mixerGraphHash",state.MixerGraph==null?null:state.MixerGraph.Hash.ToString("X8")},
                    {"ownerGraphHash",state.OwnerGraph==null?null:state.OwnerGraph.Hash.ToString("X8")},
                    {"ownerGraphRecordCount",state.OwnerGraph==null?0:state.OwnerGraph.RecordCount},
                    {"ownerGraphByteSize",state.OwnerGraph==null?0:state.OwnerGraph.Bytes.Length},
                    {"ownerGraphLayerCount",state.OwnerGraph==null?0:state.OwnerGraph.LayerCount},
                    {"ownerGraphGraph",state.OwnerGraph==null?null:Pointer(state.OwnerGraph.Graph)},
                    {"ownerGraphDirty58",state.OwnerGraph==null?null:(object)("0x"+state.OwnerGraph.GraphDirty58.ToString("X8"))},
                    {"controllerLifecycle",state.ControllerInput==null?new object[0]:DescribeControllerLifecycleLayers(memory,state.ControllerInput.RecordCount)},
                    {"parameterCount",state.Parameters.Length},{"layerCount",state.Layers.Length},
                    {"transformCount",state.Transforms.Length},{"descendantTransformCount",state.DescendantTransformCount},
                    {"foreignTransformCount",state.ForeignTransformCount},{"poseHash",PoseHash(state.Transforms).ToString("X8")}};
            }
            return rows;
        }

        private static uint PoseHash(TransformState[] transforms)
        {
            unchecked
            {
                uint hash=2166136261u;
                foreach(TransformState transform in transforms)
                {
                    hash^=(uint)transform.InstanceId;hash*=16777619u;
                    float[] values={transform.LocalPosition.x,transform.LocalPosition.y,transform.LocalPosition.z,
                        transform.LocalRotation.x,transform.LocalRotation.y,transform.LocalRotation.z,transform.LocalRotation.w,
                        transform.LocalScale.x,transform.LocalScale.y,transform.LocalScale.z};
                    foreach(float value in values)
                    {
                        byte[] bytes=BitConverter.GetBytes(value);
                        for(int i=0;i<bytes.Length;i++){hash^=bytes[i];hash*=16777619u;}
                    }
                }
                return hash;
            }
        }

        private void Deactivate()
        {
            RequireFence();
            if(harmony!=null)harmony.UnpatchSelf();
            harmony=null;if(ReferenceEquals(active,this))active=null;
            history.Clear();replayReference.Clear();resumePrefixReference.Clear();ambiguousResumePrefix.Clear();sceneIdentity=null;normalizedSceneIdentity=null;ownerGraphSceneCaptureBytes=0;warpTarget=-1;warpEligible=false;restoreApplied=false;
            scheduledResumeReadyCaptureFrame=-1;scheduledResumeReadyLastObservedFrame=-1;lastScheduledResumeReadyCapture=null;
            scheduledResumeReadyCaptureFailure=null;
            pendingResumeRestore=false;resumeRestoreApplied=false;resumeFrame=null;resumeReadyFrame=null;resumeFailure=null;
            chefRandomizeMode=ChefRandomizeMode.Record;replayChefRandomizeCursor=0;replayChefRandomizeLimit=0;
            chefRandomizeReference.Clear();trackedChefAnimatorIds.Clear();chefRandomizeFailure=null;
            lastReplayPrefixCommit=null;
            ClearResumeCoordination();
            nativeApiVersion=null;nativeControllerRoundtrip=null;nativeControllerCapture=null;nativeControllerInputCapture=null;nativeTransitionTopologyCapture=null;
            nativeMixerGraphCapture=null;nativeMixerGraphRestore=null;nativeOwnerGraphCapture=null;
            nativeSettledEndTransitionNormalize=null;nativePlayableTimeRestore=null;nativeTargetNullClipRestore=null;
            nativeOverrideClipPlayables=null;
            nativeControllerNormalize=null;nativeControllerRestore=null;
            if(nativeLibrary!=IntPtr.Zero){FreeLibrary(nativeLibrary);nativeLibrary=IntPtr.Zero;}
            nativePath=null;nativeSha256=null;unityPlayerBase=0;
        }

        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}

        private static void RequireFence()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Chef Animator checkpoint control requires the authoring pause fence.");
        }

        private static string PathOf(Transform value)
        {
            string path=value.name;while(value.parent!=null){value=value.parent;path=value.name+"/"+path;}return path;
        }
    }
}
