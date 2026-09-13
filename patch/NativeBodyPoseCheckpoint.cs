using System;
using System.Collections.Generic;
using System.Linq;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Authoring-only restore for the exact fixed bodies selected from the native
    // round's initial registry. New dynamic bodies are outside this identity set.
    public static partial class NativeBodyPoseCheckpoint
    {
        private const int MaximumRotationAssignments=4;
        private const float MaximumRotationResidual=0.000001f;
        private static readonly List<object> rotationRestores=new List<object>();
        private static long restoreCall,discardedRotationRecords;
        private static readonly List<object> massRestores=new List<object>();
        public static object LastMassFrameRestores {get{return new Dictionary<string,object>{
            {"scope","exact-native-automatic-mass-frame-recomputation"},{"maximumRecords",128},
            {"mode",NativeBodyMassMode.Diagnostics()},{"restoreStrategy",RestoreStrategyDiagnostics},
            {"attemptScope","builtin-only"},{"attempts",massRestores.ToArray()}};}}
        private static int stageFrame=-1,discardedStages;
        private static readonly List<object> stageObservations=new List<object>();
        public static object StageDiagnostics { get { return new Dictionary<string,object> {
            {"scope","read-only-current-authoring-warp-stage-observations"},{"frame",stageFrame},
            {"maximumStages",32},{"discardedOlderStages",discardedStages},{"stages",stageObservations.ToArray()} }; } }
        public static void BeginStageObservations(int frame)
        {
            stageFrame=frame;discardedStages=0;stageObservations.Clear();
        }
        public static void ObserveStage(Snapshot[] saved,string stage)
        {
            var receipt=new Dictionary<string,object>{{"stage",stage},{"captured",false}};
            stageObservations.Add(receipt);
            if(stageObservations.Count>32){stageObservations.RemoveAt(0);discardedStages++;}
            try {
                if(saved==null)throw new ArgumentNullException("saved");
                var current=Capture(new HashSet<int>(saved.Select(row=>row.EntityId)));
                receipt["current"]=Diagnostics(current);receipt["captured"]=true;
            }
            catch(Exception error) {
                // Diagnostics must preserve the original native failure. No
                // guard, pose setter, simulation tick or pause transition runs.
                receipt["error"]=error.ToString();
            }
        }
        public static object LastRotationRestores { get { return new Dictionary<string,object> {
            {"scope","bounded-authoring-rotation-setter-preimage"},{"maximumAssignments",MaximumRotationAssignments},
            {"maximumComponentResidual",MaximumRotationResidual},{"restoreCalls",restoreCall},
            {"discardedOlderRecords",discardedRotationRecords},{"attempts",rotationRestores.ToArray()} }; } }

        public sealed class Snapshot
        {
            public int EntityId { get; internal set; }
            internal EntitySerialisationEntry Entry;
            public GameObject Object {get; internal set;}
            public Rigidbody Body {get; internal set;}
            public Transform Transform {get; internal set;}
            public Transform Parent {get; internal set;}
            public Vector3 BodyPosition {get; internal set;}
            public Vector3 LocalPosition {get; internal set;}
            public Vector3 WorldPosition {get; internal set;}
            public Vector3 RawVelocity {get; internal set;}
            public Vector3 RawAngularVelocity {get; internal set;}
            public Vector3 LocalScale {get; internal set;}
            public bool RawIsKinematic {get; internal set;}
            public bool RawUseGravity {get; internal set;}
            public Quaternion BodyRotation {get; internal set;}
            public Quaternion LocalRotation {get; internal set;}
            public Quaternion WorldRotation {get; internal set;}
            public BodyInvariants Invariants {get; internal set;}
            private NativeBodyColliderCheckpoint.Shape[] colliders;
            public NativeBodyColliderCheckpoint.Shape[] Colliders {get{return (NativeBodyColliderCheckpoint.Shape[])colliders.Clone();} internal set{colliders=value;}}
            public bool HasAnalyticColliders {get{return colliders.All(shape=>shape.Kind!="MeshCollider");}}
        }

        public sealed class BodyInvariants
        {
            public float Mass {get; internal set;}
            public float Drag {get; internal set;}
            public float AngularDrag {get; internal set;}
            public float SleepThreshold {get; internal set;}
            public float MaxAngularVelocity {get; internal set;}
            public Vector3 CenterOfMass {get; internal set;}
            public Vector3 InertiaTensor {get; internal set;}
            public Quaternion InertiaTensorRotation {get; internal set;}
            public RigidbodyConstraints Constraints {get; internal set;}
            public RigidbodyInterpolation Interpolation {get; internal set;}
            public CollisionDetectionMode CollisionDetection {get; internal set;}
            public bool DetectCollisions {get; internal set;}
        }
        private static BodyInvariants CaptureInvariants(Rigidbody body)
        {
            var result=new BodyInvariants {Mass=body.mass,Drag=body.drag,AngularDrag=body.angularDrag,
                CenterOfMass=body.centerOfMass,InertiaTensor=body.inertiaTensor,InertiaTensorRotation=body.inertiaTensorRotation,
                Constraints=body.constraints,Interpolation=body.interpolation,CollisionDetection=body.collisionDetectionMode,
                DetectCollisions=body.detectCollisions,SleepThreshold=body.sleepThreshold,MaxAngularVelocity=body.maxAngularVelocity};
            if(!Finite(result.Mass)||!Finite(result.Drag)||!Finite(result.AngularDrag)||!Finite(result.CenterOfMass)
                ||!Finite(result.InertiaTensor)||!Finite(result.InertiaTensorRotation)||!Finite(result.SleepThreshold)||!Finite(result.MaxAngularVelocity))
                throw new InvalidOperationException("Nonfinite native rigidbody mass/inertia/settings.");
            return result;
        }
        // Read-only value copy for independently compiled authoring strategies.
        public static BodyInvariants ObserveBodyProperties(Rigidbody body){return CaptureInvariants(body);}
        private static bool SameMassFrame(BodyInvariants a,BodyInvariants b)
        {
            return a.Mass==b.Mass&&Same(a.CenterOfMass,b.CenterOfMass)&&Same(a.InertiaTensor,b.InertiaTensor)
                &&Same(a.InertiaTensorRotation,b.InertiaTensorRotation);
        }
        private static void ValidateInvariants(Snapshot row,bool requireMassFrame=false)
        {
            var a=row.Invariants;var b=CaptureInvariants(row.Body);
            if(a==null || (requireMassFrame&&!SameMassFrame(a,b)) || a.Drag!=b.Drag || a.AngularDrag!=b.AngularDrag
                || a.Constraints!=b.Constraints || a.Interpolation!=b.Interpolation || a.CollisionDetection!=b.CollisionDetection
                || a.DetectCollisions!=b.DetectCollisions || a.SleepThreshold!=b.SleepThreshold || a.MaxAngularVelocity!=b.MaxAngularVelocity)
                throw new InvalidOperationException("Initial native body "+row.EntityId+" "+(requireMassFrame?"mass/inertia/settings":"immutable settings")+" differ.");
        }

        // Call once without a selection only when establishing the native round's
        // initial fixed-body set. Later captures pass precisely those IDs.
        public static Snapshot[] Capture(HashSet<int> selectedIds = null)
        {
            BindAuthoringThread();
            var rows = new List<Snapshot>();
            var ids = new HashSet<int>();
            var bodies = new HashSet<Rigidbody>();
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries._items[i];
                int id = (int)entry.m_Header.m_uEntityID;
                if (selectedIds != null && !selectedIds.Contains(id)) continue;
                var obj = entry.m_GameObject;
                if (obj == null) continue;
                // A native physics container is registered independently. Do not
                // visit it again through another item's PhysicalAttachment.
                var body = obj.GetComponent<Rigidbody>();
                if (body == null) continue;
                if (!ids.Add(id) || !bodies.Add(body))
                    throw new InvalidOperationException("Duplicate registered native fixed body.");
                var transform = obj.transform;
                NativeBodyMassMode.RequireAutomatic(body);
                var row = new Snapshot {
                    EntityId = id, Entry = entry, Object = obj, Body = body, Transform = transform, Parent = transform.parent,
                    BodyPosition = body.position, BodyRotation = body.rotation,
                    LocalPosition = transform.localPosition, LocalRotation = transform.localRotation,
                    WorldPosition = transform.position, WorldRotation = transform.rotation, Invariants=CaptureInvariants(body),
                    RawVelocity=body.velocity,RawAngularVelocity=body.angularVelocity,RawIsKinematic=body.isKinematic,RawUseGravity=body.useGravity,
                    Colliders=NativeBodyColliderCheckpoint.Capture(body),LocalScale=transform.localScale
                };
                if (!Finite(row.BodyPosition) || !Finite(row.BodyRotation) || !Finite(row.LocalPosition)
                    || !Finite(row.LocalRotation) || !Finite(row.WorldPosition) || !Finite(row.WorldRotation)
                    || !Finite(row.RawVelocity) || !Finite(row.RawAngularVelocity)||!Finite(row.LocalScale))
                    throw new InvalidOperationException("Nonfinite registered native body/transform pose: " + id);
                rows.Add(row);
            }
            if (selectedIds != null && !selectedIds.SetEquals(ids))
                throw new InvalidOperationException("Selected initial native rigidbody membership changed.");
            return rows.OrderBy(s => s.EntityId).ToArray();
        }

        public static void Validate(Snapshot[] saved)
        {
            if (saved == null) throw new ArgumentNullException("saved");
            var seen = new HashSet<int>();
            foreach (var row in saved)
            {
                if (row == null || !seen.Add(row.EntityId) || row.Object == null || row.Body == null || row.Transform == null
                    || !ReferenceEquals(EntitySerialisationRegistry.GetEntry(row.Object), row.Entry)
                    || (int)row.Entry.m_Header.m_uEntityID != row.EntityId
                    || row.Object.GetComponent<Rigidbody>() != row.Body || row.Object.transform != row.Transform
                    || (!ReferenceEquals(row.Parent, null) && row.Parent == null) || row.Transform.parent != row.Parent)
                    throw new InvalidOperationException("Initial native rigidbody incarnation or parent changed.");
                ValidateInvariants(row);
                NativeBodyMassMode.RequireAutomatic(row.Body);
                NativeBodyColliderCheckpoint.ValidateCaptured(row.Colliders);
                if(!Same(row.Transform.localScale,row.LocalScale))throw new InvalidOperationException("Initial native body scale changed.");
            }
        }

        private static void RestoreBuiltin(Snapshot[] saved)
        {
            Validate(saved);
            // Require target compound geometry before any mass recomputation.
            // Unchanged-mass early pose restoration precedes the cannon sidecar;
            // final verification checks its restored child hierarchy as well.
            foreach(var row in saved)
                if(!SameMassFrame(row.Invariants,CaptureInvariants(row.Body)))
                    NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,false);
            long call=++restoreCall;
            foreach (var row in saved)
            {
                var velocity=row.Body.velocity;var angular=row.Body.angularVelocity;
                bool kinematic=row.Body.isKinematic,gravity=row.Body.useGravity;
                if(!Finite(velocity)||!Finite(angular))throw new InvalidOperationException("Nonfinite current native body velocity: "+row.EntityId);
                // Unity Rigidbody and Transform expose distinct stored poses.
                // Restore the observed local transform before assigning the body;
                // do not infer either pose from the other or synchronize PhysX.
                if (!Same(row.Transform.localPosition, row.LocalPosition)) row.Transform.localPosition = row.LocalPosition;
                if (!Same(row.Transform.localRotation, row.LocalRotation)) row.Transform.localRotation = row.LocalRotation;
                if (!Same(row.Body.position, row.BodyPosition)) row.Body.position = row.BodyPosition;
                int provisionalAssignments=0;
                if(!SameMassFrame(row.Invariants,CaptureInvariants(row.Body))) {
                    provisionalAssignments=RecomputeMassFrame(row,call,velocity,angular,kinematic,gravity);
                }
                RestoreRotation(row,call,velocity,angular,kinematic,gravity,provisionalAssignments);
                ValidateUnchanged(row,velocity,angular,kinematic,gravity);
            }
        }

        private static void ValidateUnchanged(Snapshot row,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            // Native pause temporarily changes these fields. Pin their values at
            // this restore call, rather than replacing them with advancing values.
            Validate(new[] {row});
            ValidateInvariants(row,true);
            if(!Same(row.Body.velocity,velocity)||!Same(row.Body.angularVelocity,angular)
                ||row.Body.isKinematic!=kinematic||row.Body.useGravity!=gravity)
                throw new InvalidOperationException("Native rotation restore changed non-pose physics state: "+row.EntityId);
        }
        private static int RecomputeMassFrame(Snapshot row,long call,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            var log=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},{"before",MassFrame(row.Body)},
                {"target",MassFrame(row.Invariants)},{"exact",false},{"resetCenterOfMass",false},{"resetInertiaTensor",false}};
            massRestores.Add(log);if(massRestores.Count>128)massRestores.RemoveAt(0);
            int assignments=0;
            try {
                if(row.Colliders.Any(shape=>shape.Kind=="MeshCollider"))
                    throw new InvalidOperationException("Automatic mass reset requires captured analytic collider geometry; mutable mesh contents are not checkpointed.");
                // The exact target transform is already installed. Put the
                // captured body orientation in place before native recompute;
                // a small setter readback residual is provisional, never success.
                if(!Same(row.Body.rotation,row.BodyRotation)) {
                    assignments=1;var rotationLog=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},
                        {"attempt",1},{"phase","before-native-mass-reset"},{"target",Rotation(row.BodyRotation)},
                        {"input",Rotation(row.BodyRotation)},{"exact",false}};
                    rotationRestores.Add(rotationLog);if(rotationRestores.Count>256){rotationRestores.RemoveAt(0);discardedRotationRecords++;}
                    row.Body.rotation=row.BodyRotation;var observed=row.Body.rotation;var residual=Subtract(row.BodyRotation,observed);
                    rotationLog["readback"]=DiagnosticRotation(observed);rotationLog["residual"]=DiagnosticRotation(residual);
                    rotationLog["exact"]=Same(observed,row.BodyRotation);
                    if(!Finite(observed)||!Finite(residual)||MaxAbs(residual)>MaximumRotationResidual)
                        throw new InvalidOperationException("Native provisional body pose is outside its exact-restoration envelope.");
                }
                Validate(new[]{row});RequireMotionUnchanged(row,velocity,angular,kinematic,gravity);
                NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                if(!Same(row.Body.position,row.BodyPosition)||!Same(row.Transform.position,row.WorldPosition)||!Same(row.Transform.rotation,row.WorldRotation))
                    throw new InvalidOperationException("Native body/transform pose differs before automatic mass reset.");
                if(row.Body.mass!=row.Invariants.Mass)row.Body.mass=row.Invariants.Mass;
                row.Body.ResetCenterOfMass();log["resetCenterOfMass"]=true;
                row.Body.ResetInertiaTensor();log["resetInertiaTensor"]=true;
                log["after"]=MassFrame(row.Body);
                ValidateUnchanged(row,velocity,angular,kinematic,gravity);
                NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                log["exact"]=true;return assignments;
            }
            catch(Exception error){try{log["after"]=MassFrame(row.Body);}catch(Exception readError){log["afterError"]=readError.Message;}log["error"]=error.Message;throw;}
        }
        private static void RequireMotionUnchanged(Snapshot row,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity)
        {
            if(!Same(row.Body.velocity,velocity)||!Same(row.Body.angularVelocity,angular)||row.Body.isKinematic!=kinematic||row.Body.useGravity!=gravity)
                throw new InvalidOperationException("Native mass reset changed non-mass physics state: "+row.EntityId);
        }
        private static object MassFrame(Rigidbody body){return MassFrame(CaptureInvariants(body));}
        private static object MassFrame(BodyInvariants a){return new Dictionary<string,object>{{"mass",a.Mass},{"centerOfMass",Point(a.CenterOfMass)},
            {"inertiaTensor",Point(a.InertiaTensor)},{"inertiaTensorRotation",Rotation(a.InertiaTensorRotation)}};}
        private static void RestoreRotation(Snapshot row,long call,Vector3 velocity,Vector3 angular,bool kinematic,bool gravity,int priorAssignments=0)
        {
            if(Same(row.Body.rotation,row.BodyRotation))return;
            Quaternion candidate=row.BodyRotation;
            float previous=float.PositiveInfinity;
            for(int attempt=priorAssignments+1;attempt<=MaximumRotationAssignments;attempt++) {
                if(!Finite(candidate)||MaxAbs(Subtract(candidate,row.BodyRotation))>MaximumRotationResidual)
                    throw new InvalidOperationException("Native rotation setter preimage escaped its small candidate envelope: "+row.EntityId);
                var log=new Dictionary<string,object>{{"restoreCall",call},{"entityId",row.EntityId},{"attempt",attempt},
                    {"target",Rotation(row.BodyRotation)},{"input",Rotation(candidate)},{"readback",null},{"exact",false}};
                rotationRestores.Add(log);
                if(rotationRestores.Count>256){rotationRestores.RemoveAt(0);discardedRotationRecords++;}
                try {
                    row.Body.rotation=candidate;
                    Quaternion observed=row.Body.rotation,residual=Subtract(row.BodyRotation,observed);
                    log["readback"]=DiagnosticRotation(observed);log["residual"]=DiagnosticRotation(residual);
                    bool finite=Finite(observed)&&Finite(residual);float size=finite?MaxAbs(residual):float.PositiveInfinity;
                    log["finite"]=finite;log["maximumAbsoluteResidual"]=finite?(object)size:null;
                    ValidateUnchanged(row,velocity,angular,kinematic,gravity);
                    if(!Same(row.Body.position,row.BodyPosition)||!Same(row.Transform.localPosition,row.LocalPosition)
                        ||!Same(row.Transform.localRotation,row.LocalRotation)||!Same(row.Transform.position,row.WorldPosition)
                        ||!Same(row.Transform.rotation,row.WorldRotation))
                        throw new InvalidOperationException("Native rotation setter changed another restored pose: "+row.EntityId);
                    if(Same(observed,row.BodyRotation)){log["exact"]=true;return;}
                    if(!finite||size>MaximumRotationResidual)
                        throw new InvalidOperationException("Native rotation readback residual is nonfinite or outside small envelope: "+row.EntityId);
                    if(size>=previous)
                        throw new InvalidOperationException("Native rotation setter preimage stagnated or worsened: "+row.EntityId);
                    if(attempt==MaximumRotationAssignments)
                        throw new InvalidOperationException("Native rotation setter has no exact readback within four assignments: "+row.EntityId);
                    Quaternion next=new Quaternion(candidate.x+residual.x,candidate.y+residual.y,candidate.z+residual.z,candidate.w+residual.w);
                    if(Same(next,candidate))throw new InvalidOperationException("Native rotation setter preimage cannot progress at float precision: "+row.EntityId);
                    candidate=next;previous=size;
                }
                catch(Exception error){log["error"]=error.Message;throw;}
            }
        }
        private static Quaternion Subtract(Quaternion a,Quaternion b){return new Quaternion(a.x-b.x,a.y-b.y,a.z-b.z,a.w-b.w);}
        private static float MaxAbs(Quaternion q){return Math.Max(Math.Max(Math.Abs(q.x),Math.Abs(q.y)),Math.Max(Math.Abs(q.z),Math.Abs(q.w)));}

        public static void VerifyRestored(Snapshot[] saved)
        {
            VerifyPosePostconditions(saved,true);
        }

        internal static Snapshot RebindDestroyedColliderless(Snapshot target,EntitySerialisationEntry currentEntry)
        {
            if(target==null||currentEntry==null||currentEntry.m_GameObject==null
                ||currentEntry.m_Header.m_uEntityID!=target.EntityId
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)target.EntityId),currentEntry)
                ||target.Colliders.Length!=0)
                throw new InvalidOperationException("Destroyed initial body rebind target/current contract differs.");
            var current=Capture(new HashSet<int>{target.EntityId});
            if(current.Length!=1||current[0].EntityId!=target.EntityId||current[0].Colliders.Length!=0)
                throw new InvalidOperationException("Recreated initial body is not one colliderless registered Rigidbody.");
            var row=current[0];
            if(!ReferenceEquals(row.Entry,currentEntry)||!ReferenceEquals(row.Parent,target.Parent)
                ||!Same(row.LocalScale,target.LocalScale))
                throw new InvalidOperationException("Recreated initial body hierarchy/scale differs from the checkpoint.");
            row.BodyPosition=target.BodyPosition;row.LocalPosition=target.LocalPosition;row.WorldPosition=target.WorldPosition;
            row.RawVelocity=target.RawVelocity;row.RawAngularVelocity=target.RawAngularVelocity;
            row.RawIsKinematic=target.RawIsKinematic;row.RawUseGravity=target.RawUseGravity;
            row.BodyRotation=target.BodyRotation;row.LocalRotation=target.LocalRotation;row.WorldRotation=target.WorldRotation;
            row.Invariants=target.Invariants;row.Colliders=target.Colliders;row.LocalScale=target.LocalScale;
            Validate(new[]{row});
            return row;
        }
        internal static Snapshot CloneWithColliders(Snapshot target,NativeBodyColliderCheckpoint.Shape[] colliders)
        {
            if(target==null||colliders==null)throw new ArgumentNullException(target==null?"target":"colliders");
            return CopyTargetOnto(target,new Snapshot {EntityId=target.EntityId,Entry=target.Entry,Object=target.Object,
                Body=target.Body,Transform=target.Transform,Parent=target.Parent,Colliders=colliders});
        }
        internal static Snapshot RebindSurvivingWithRecreatedColliders(Snapshot target,Snapshot current,
            NativeBodyColliderCheckpoint.Shape[] colliders)
        {
            if(target==null||current==null||colliders==null
                ||target.EntityId!=current.EntityId||!ReferenceEquals(target.Entry,current.Entry)
                ||!ReferenceEquals(target.Object,current.Object)||!ReferenceEquals(target.Body,current.Body)
                ||!ReferenceEquals(target.Transform,current.Transform)||!ReferenceEquals(target.Parent,current.Parent))
                throw new InvalidOperationException("Surviving initial body identity changed during attachment collider recreation.");
            current.Colliders=colliders;
            CopyTargetOnto(target,current);
            Validate(new[]{current});
            NativeBodyColliderCheckpoint.RequireExactMembership(current.Body,current.Colliders);
            return current;
        }
        private static Snapshot CopyTargetOnto(Snapshot target,Snapshot current)
        {
            current.BodyPosition=target.BodyPosition;current.LocalPosition=target.LocalPosition;current.WorldPosition=target.WorldPosition;
            current.RawVelocity=target.RawVelocity;current.RawAngularVelocity=target.RawAngularVelocity;
            current.RawIsKinematic=target.RawIsKinematic;current.RawUseGravity=target.RawUseGravity;
            current.BodyRotation=target.BodyRotation;current.LocalRotation=target.LocalRotation;current.WorldRotation=target.WorldRotation;
            current.Invariants=target.Invariants;current.LocalScale=target.LocalScale;
            return current;
        }
        private static void VerifyPosePostconditions(Snapshot[] saved,bool checkColliders)
        {
            Validate(saved);
            foreach (var row in saved)
            {
                ValidateInvariants(row,true);
                if(checkColliders)NativeBodyColliderCheckpoint.RequireRestored(row.Body,row.Colliders,true);
                var differences = new List<string>();
                if (!Same(row.Transform.localPosition, row.LocalPosition)) differences.Add("transform.localPosition");
                if (!Same(row.Transform.localRotation, row.LocalRotation)) differences.Add("transform.localRotation");
                if (!Same(row.Transform.position, row.WorldPosition)) differences.Add("transform.position");
                if (!Same(row.Transform.rotation, row.WorldRotation)) differences.Add("transform.rotation");
                if (!Same(row.Body.position, row.BodyPosition)) differences.Add("rigidbody.position");
                if (!Same(row.Body.rotation, row.BodyRotation)) differences.Add("rigidbody.rotation expected="+Exact(row.BodyRotation)+" actual="+Exact(row.Body.rotation)+" inertia="+Exact(row.Body.inertiaTensorRotation));
                if (differences.Count != 0)
                    throw new InvalidOperationException("Initial native body " + row.EntityId + " pose restoration differs: " + string.Join(", ", differences.ToArray()));
            }
        }

        public static object Diagnostics(Snapshot[] snapshots)
        {
            return new Dictionary<string, object> {
                { "source", "native-registered-Rigidbody-and-Transform" },
                { "bodies", snapshots.Select(row => (object)new Dictionary<string, object> {
                    { "entityId", row.EntityId }, { "bodyInstanceId", row.Body.GetInstanceID() },
                    { "transformInstanceId", row.Transform.GetInstanceID() },
                    { "parentInstanceId", row.Parent == null ? (object)null : row.Parent.GetInstanceID() },
                    { "rigidbodyPosition", Point(row.BodyPosition) }, { "rigidbodyRotation", Rotation(row.BodyRotation) },
                    { "transformLocalPosition", Point(row.LocalPosition) }, { "transformLocalRotation", Rotation(row.LocalRotation) },
                    { "transformPosition", Point(row.WorldPosition) }, { "transformRotation", Rotation(row.WorldRotation) },
                    { "rawVelocity",Point(row.RawVelocity)},{"rawAngularVelocity",Point(row.RawAngularVelocity)},
                    { "rawIsKinematic",row.RawIsKinematic},{"rawUseGravity",row.RawUseGravity},
                    { "mass",row.Invariants.Mass},{"centerOfMass",Point(row.Invariants.CenterOfMass)},
                    { "inertiaTensor",Point(row.Invariants.InertiaTensor)},{"inertiaTensorRotation",Rotation(row.Invariants.InertiaTensorRotation)},
                    { "drag",row.Invariants.Drag},{"angularDrag",row.Invariants.AngularDrag},{"constraints",(int)row.Invariants.Constraints},
                    { "interpolation",(int)row.Invariants.Interpolation},{"collisionDetectionMode",(int)row.Invariants.CollisionDetection},
                    { "detectCollisions",row.Invariants.DetectCollisions},{"sleepThreshold",row.Invariants.SleepThreshold},
                    { "maxAngularVelocity",row.Invariants.MaxAngularVelocity},
                    { "colliders",NativeBodyColliderCheckpoint.Diagnostics(row.Colliders)},
                    { "transformLocalScale",Point(row.LocalScale)}
                }).ToArray() }
            };
        }
        private static object Point(Vector3 v) { return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z } }; }
        private static object Rotation(Quaternion q) { return new Dictionary<string, object> { { "x", q.x }, { "y", q.y }, { "z", q.z }, { "w", q.w } }; }
        private static object DiagnosticRotation(Quaternion q) {return Finite(q)?Rotation(q):(object)Exact(q);}
        private static bool Same(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        private static string Exact(Quaternion q) { return string.Join(",",new[] {q.x.ToString("R",System.Globalization.CultureInfo.InvariantCulture),q.y.ToString("R",System.Globalization.CultureInfo.InvariantCulture),q.z.ToString("R",System.Globalization.CultureInfo.InvariantCulture),q.w.ToString("R",System.Globalization.CultureInfo.InvariantCulture)}); }
        private static bool Same(Quaternion a, Quaternion b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private static bool Finite(Quaternion q) { return Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w); }
    }
}
