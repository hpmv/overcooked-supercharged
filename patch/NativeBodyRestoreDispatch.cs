using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace SuperchargedPatch
{
    public static partial class NativeBodyPoseCheckpoint
    {
        private static int authoringThread;
        private static long strategyGeneration,dispatchSerial;
        private static IBodyRestoreStrategy installedStrategy;
        private static bool dispatchActive;
        private static readonly List<object> dispatchReceipts=new List<object>();

        private static void BindAuthoringThread()
        {
            int current=Thread.CurrentThread.ManagedThreadId;
            if(authoringThread==0)authoringThread=current;
            if(authoringThread!=current)throw new InvalidOperationException("Native body checkpoints require their original Unity thread.");
        }
        private static void RequireStrategyBoundary()
        {
            if(authoringThread==0||authoringThread!=Thread.CurrentThread.ManagedThreadId)
                throw new InvalidOperationException("Body strategy registration requires the captured Unity thread.");
            if(dispatchActive||!Helpers.IsPaused())
                throw new InvalidOperationException("Body strategy registration requires a paused non-restoring boundary.");
        }

        // The hot-module loader fences inputs and calls this on Unity's thread.
        // Failed strategy calls never retry with builtin code after mutation.
        public static IDisposable InstallRestoreStrategy(IBodyRestoreStrategy strategy)
        {
            RequireStrategyBoundary();
            if(strategy==null||strategy.ApiVersion!=1||string.IsNullOrEmpty(strategy.Name)||strategy.Name.Length>128)
                throw new InvalidOperationException("Unsupported native body strategy contract.");
            installedStrategy=strategy;long generation=++strategyGeneration;
            return new StrategyLease(strategy,generation);
        }
        private sealed class StrategyLease:IDisposable
        {
            private readonly IBodyRestoreStrategy owner;private readonly long generation;private bool disposed;
            internal StrategyLease(IBodyRestoreStrategy owner,long generation){this.owner=owner;this.generation=generation;}
            public void Dispose()
            {
                if(disposed)return;
                // Disposing the replaced DLL cannot clear its successor.
                if(installedStrategy==owner&&strategyGeneration==generation){
                    RequireStrategyBoundary();installedStrategy=null;++strategyGeneration;
                }
                disposed=true;
            }
        }
        public static object RestoreStrategyDiagnostics {get{return new Dictionary<string,object>{
            {"apiVersion",1},{"name",installedStrategy==null?"builtin":installedStrategy.Name},
            {"assembly",installedStrategy==null?typeof(NativeBodyPoseCheckpoint).Assembly.FullName:installedStrategy.GetType().Assembly.FullName},
            {"generation",strategyGeneration},{"active",dispatchActive},{"calls",dispatchSerial},
            {"maximumReceipts",32},{"receipts",dispatchReceipts.ToArray()}};}}

        private sealed class MotionPin
        {
            internal Snapshot Target;internal Vector3 Velocity,Angular;internal bool Kinematic,Gravity;
        }
        public static void Restore(Snapshot[] saved)
        {
            BindAuthoringThread();
            if(dispatchActive)throw new InvalidOperationException("Nested native body restore is unsupported.");
            Validate(saved);
            var targets=(Snapshot[])saved.Clone();
            var pins=targets.Select(row=>new MotionPin{Target=row,Velocity=row.Body.velocity,Angular=row.Body.angularVelocity,
                Kinematic=row.Body.isKinematic,Gravity=row.Body.useGravity}).ToArray();
            foreach(var pin in pins){
                if(!Finite(pin.Velocity)||!Finite(pin.Angular))throw new InvalidOperationException("Nonfinite body motion at restore boundary.");
                if(!SameMassFrame(pin.Target.Invariants,CaptureInvariants(pin.Target.Body)))
                    NativeBodyColliderCheckpoint.RequireRestored(pin.Target.Body,pin.Target.Colliders,false);
            }
            var strategy=installedStrategy;
            var receipt=new Dictionary<string,object>{{"call",++dispatchSerial},{"frame",stageFrame},{"generation",strategyGeneration},
                {"strategy",strategy==null?"builtin":strategy.Name},{"bodyCount",targets.Length},{"verified",false}};
            dispatchReceipts.Add(receipt);if(dispatchReceipts.Count>32)dispatchReceipts.RemoveAt(0);
            dispatchActive=true;
            try {
                if(strategy==null)RestoreBuiltin(targets);
                else strategy.Restore((Snapshot[])targets.Clone());
                // The module owns its algorithm, never the permanent target
                // array or acceptance policy. Child collider pose verification
                // still happens in VerifyRestored after the cannon sidecar.
                VerifyPosePostconditions(targets,false);
                foreach(var pin in pins)RequireMotionUnchanged(pin.Target,pin.Velocity,pin.Angular,pin.Kinematic,pin.Gravity);
                receipt["verified"]=true;
            }
            catch(Exception error){receipt["error"]=error.ToString();throw;}
            finally{dispatchActive=false;}
        }
    }
}
