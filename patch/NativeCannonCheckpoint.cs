using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using BitStream;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Observes the installed cannon implementation. No ordinary input, component
    // registration, native callback or native message is replaced here.
    public static class NativeCannonCheckpoint
    {
        public static string LastObservationError = "";
        private static readonly Dictionary<ServerCannon,Snapshot> observationBatch = new Dictionary<ServerCannon,Snapshot>();
        private static bool observationBatchActive;
        private static int observationUnityFrame,observationThread,observationFrame;
        private static long deepCaptures,captureReuses,deepCaptureTicks,reuseValidationTicks,observationBatches;

        // This is a synchronous collector scope, never a frame-number cache.
        // Repeated paused callbacks at the same logical frame get fresh snapshots.
        internal static void BeginObservationBatch(int frame)
        {
            if(observationBatchActive) {
                EndObservationBatch();
                throw new InvalidOperationException("Nested native cannon observation batch.");
            }
            observationBatch.Clear();
            observationFrame=frame; observationUnityFrame=Time.frameCount;
            observationThread=System.Threading.Thread.CurrentThread.ManagedThreadId;
            observationBatchActive=true; observationBatches++;
        }
        internal static void EndObservationBatch()
        {
            observationBatchActive=false;
            observationBatch.Clear();
        }
        public static object CaptureDiagnostics()
        {
            return new Dictionary<string,object> {
                {"deepCaptures",deepCaptures},{"captureReuses",captureReuses},
                {"deepCaptureTicks",deepCaptureTicks},{"reuseValidationTicks",reuseValidationTicks},
                {"tickFrequency",System.Diagnostics.Stopwatch.Frequency},{"observationBatches",observationBatches},
                {"batchActive",observationBatchActive},{"batchFrame",observationBatchActive?(object)observationFrame:null}
            };
        }
        public static void ObserveFrame(ServerCannon server)
        {
            try { server.SendAuxMessage(Observe(server).Message()); }
            catch(Exception error) { EndObservationBatch(); LastObservationError=error.ToString(); }
        }
        public sealed class Snapshot
        {
            internal ServerCannon Server;
            internal EntitySerialisationEntry Entry;
            internal ClientCannon Client;
            internal ServerCannonSessionInteractable Session;
            internal ClientCannonSessionInteractable ClientSession;
            internal ClientCannonPlayerHandler PlayerHandler;
            internal ClientCannonCosmeticDecisions Cosmetics;
            internal Cannon Cannon;
            internal Animator Animator;
            internal PilotRotation Pilot;
            internal ServerPilotRotation ServerPilot;
            internal ClientPilotRotation ClientPilot;
            internal object[] ServerPilotFields,ClientPilotFields,PilotMotionFields;
            internal LocalPose[] LocalPoses;
            internal int Id, Instance, AnimatorState;
            internal float AnimatorTime;
            internal Quaternion PilotRotation;
            internal GameObject ServerLoaded, ClientLoaded;
            internal EntitySerialisationEntry[] PassengerEntries;
            internal CannonMessage ServerMessage, ClientMessage;
            internal Vector3 ExitPosition;
            internal Quaternion ExitRotation;
            internal bool ServerSessionActive, ClientSessionActive, Flying, Ready, Settled;
            internal int Launches;
            internal string Reason;
            internal NativeCannonFlightCheckpoint.Snapshot Flight;
            internal bool AnimatorOccupied;

            public NativeCannonAuxMessage Message()
            {
                var entry = ServerLoaded == null ? null : EntitySerialisationRegistry.GetEntry(ServerLoaded);
                return new NativeCannonAuxMessage { LoadedEntityId = entry == null ? 0u : entry.m_Header.m_uEntityID,
                    ServerSessionActive = ServerSessionActive, ClientSessionActive = ClientSessionActive,
                    Flying = Flying, Ready = Ready, ActiveLaunches = (uint)Launches, Settled = Settled,
                    Angle = Pilot == null ? 0f : PilotRotation.eulerAngles.y, LoadNormalizedTime = AnimatorTime };
            }
        }

        public static Snapshot Observe(ServerCannon server)
        {
            try {
                if(observationBatchActive) {
                    if(observationThread!=System.Threading.Thread.CurrentThread.ManagedThreadId || observationUnityFrame!=Time.frameCount)
                        throw new InvalidOperationException("Native cannon observation batch escaped its synchronous callback.");
                    Snapshot cached;
                    if(observationBatch.TryGetValue(server,out cached)) {
                        long started=System.Diagnostics.Stopwatch.GetTimestamp();
                        try { ValidateObservationIdentity(server,cached); }
                        finally { reuseValidationTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-started; }
                        captureReuses++;
                        return cached;
                    }
                }
                long start=System.Diagnostics.Stopwatch.GetTimestamp();
                Snapshot snapshot;
                deepCaptures++;
                try { snapshot=ObserveFresh(server); }
                finally { deepCaptureTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-start; }
                if(observationBatchActive) observationBatch.Add(server,snapshot);
                return snapshot;
            }
            catch { EndObservationBatch(); throw; }
        }

        private static void ValidateObservationIdentity(ServerCannon server,Snapshot s)
        {
            if(server==null || !ReferenceEquals(server,s.Server))
                throw new InvalidOperationException("Native cannon observation object changed.");
            var obj=server.gameObject;
            var entry=EntitySerialisationRegistry.GetEntry(obj);
            if(obj==null || s.Client==null || s.Session==null || s.ClientSession==null || s.PlayerHandler==null
                || s.Cosmetics==null || s.Cannon==null || s.Animator==null
                || (!ReferenceEquals(s.Pilot,null) && (s.Pilot==null || s.ServerPilot==null || s.ClientPilot==null))
                || obj.GetInstanceID()!=s.Instance || !ReferenceEquals(entry,s.Entry)
                || entry==null || entry.m_Header.m_uEntityID!=(uint)s.Id
                || obj.GetComponent<ServerCannon>()!=s.Server || obj.GetComponent<ClientCannon>()!=s.Client
                || obj.GetComponent<ServerCannonSessionInteractable>()!=s.Session || obj.GetComponent<ClientCannonSessionInteractable>()!=s.ClientSession
                || obj.GetComponent<ClientCannonPlayerHandler>()!=s.PlayerHandler || obj.GetComponent<ClientCannonCosmeticDecisions>()!=s.Cosmetics
                || obj.GetComponent<Cannon>()!=s.Cannon || obj.GetComponent<CannonCosmeticDecisions>()==null
                || obj.GetComponent<CannonCosmeticDecisions>().m_cannonAnimator!=s.Animator
                || obj.RequestComponentRecursive<PilotRotation>()!=s.Pilot
                || (s.Pilot!=null && (s.Pilot.GetComponent<ServerPilotRotation>()!=s.ServerPilot || s.Pilot.GetComponent<ClientPilotRotation>()!=s.ClientPilot)))
                throw new InvalidOperationException("Native cannon observation incarnation changed.");
            foreach(var pose in s.LocalPoses)
                if(pose.Transform==null || pose.Transform.parent!=pose.Parent)
                    throw new InvalidOperationException("Native cannon observation hierarchy changed.");
            var passengers=new[] {s.ServerLoaded,s.ClientLoaded,s.ServerMessage.m_loadedObject,s.ClientMessage.m_loadedObject};
            for(int p=0;p<passengers.Length;p++) ValidateReference(passengers[p],s.PassengerEntries[p]);
        }

        private static Snapshot ObserveFresh(ServerCannon server)
        {
            var obj = server.gameObject;
            var entry = EntitySerialisationRegistry.GetEntry(obj);
            var s = new Snapshot { Server=server, Entry=entry, Id=entry == null ? -1 : (int)entry.m_Header.m_uEntityID,
                Instance=obj.GetInstanceID(), Client=obj.GetComponent<ClientCannon>(), Cannon=obj.GetComponent<Cannon>(),
                Session=obj.GetComponent<ServerCannonSessionInteractable>(), ClientSession=obj.GetComponent<ClientCannonSessionInteractable>(),
                PlayerHandler=obj.GetComponent<ClientCannonPlayerHandler>(), Cosmetics=obj.GetComponent<ClientCannonCosmeticDecisions>(),
                Pilot=obj.RequestComponentRecursive<PilotRotation>() };
            if (s.Client == null || s.Session == null || s.ClientSession == null || s.PlayerHandler == null || s.Cosmetics == null || s.Cannon == null || entry == null)
                throw new InvalidOperationException("Incomplete installed native cannon component set on " + s.Id);
            s.Animator = obj.GetComponent<CannonCosmeticDecisions>().m_cannonAnimator;
            s.ServerSessionActive = Read(typeof(ServerSessionInteractable), s.Session, "m_session") != null;
            s.ClientSessionActive = s.ClientSession.HasSession;
            s.Launches = ((IList)Read(typeof(ClientCannon), s.Client, "m_launches")).Count;
            s.Flying = server.IsFlying(); s.Ready = (bool)Read(typeof(ServerCannon),server,"m_readyToLaunch");
            s.ServerLoaded = (GameObject)Read(typeof(ServerCannon),server,"m_loadedObject");
            s.ClientLoaded = (GameObject)Read(typeof(ClientCannon),s.Client,"m_loadedObject");
            s.ServerMessage = Copy((CannonMessage)Read(typeof(ServerCannon),server,"m_message"));
            s.ClientMessage = Copy((CannonMessage)Read(typeof(ClientCannon),s.Client,"m_message"));
            s.PassengerEntries = new[] { s.ServerLoaded,s.ClientLoaded,s.ServerMessage.m_loadedObject,s.ClientMessage.m_loadedObject }
                .Select(value=>ReferenceEquals(value,null) ? null : EntitySerialisationRegistry.GetEntry(value)).ToArray();
            s.ExitPosition = (Vector3)Read(typeof(ClientCannon),s.Client,"m_exitPosition");
            s.ExitRotation = (Quaternion)Read(typeof(ClientCannon),s.Client,"m_exitRotation");
            s.PilotRotation = s.Pilot == null ? Quaternion.identity : s.Pilot.m_transformToRotate.rotation;
            if(s.Pilot != null) {
                s.ServerPilot=s.Pilot.GetComponent<ServerPilotRotation>();
                s.ClientPilot=s.Pilot.GetComponent<ClientPilotRotation>();
                if(s.ServerPilot==null || s.ClientPilot==null) throw new InvalidOperationException("Native cannon pilot components missing.");
                s.ServerPilotFields=CaptureFields(typeof(ServerPilotRotation),s.ServerPilot,serverPilotFields);
                s.ClientPilotFields=CaptureFields(typeof(ClientPilotRotation),s.ClientPilot,clientPilotFields);
                s.PilotMotionFields=CaptureFields(typeof(PilotRotation),s.Pilot,pilotMotionFields);
            }
            s.LocalPoses=CaptureLocalPoses(obj.transform,s.Pilot);
            var animation = s.Animator.GetCurrentAnimatorStateInfo(0);
            s.AnimatorState=animation.fullPathHash; s.AnimatorTime=animation.normalizedTime;
            s.AnimatorOccupied=s.Animator.GetBool("IsOccupied");
            bool parented = false;
            var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++) {
                var chef=entries._items[i].m_GameObject;
                if(chef != null && chef.GetComponent<PlayerControls>() != null && chef.transform.IsChildOf(s.Cannon.m_attachPoint)) parented=true;
            }
            s.Reason = NativeCannonWarpGuard.UnsettledReason(s.ServerSessionActive,s.ClientSessionActive,s.Flying,s.Launches,parented,
                Read(typeof(ServerCannon),server,"OnInteractionEnd") != null,
                Read(typeof(ClientCannonPlayerHandler),s.PlayerHandler,"m_controls") != null || (bool)Read(typeof(ClientCannonPlayerHandler),s.PlayerHandler,"m_inCannon"),
                s.Animator.IsInTransition(0) || s.Animator.GetBool("IsOccupied") || !Finite(s.AnimatorTime));
            s.Settled=s.Reason == null;
            if(s.Flying && !s.Animator.IsInTransition(0) && Finite(s.AnimatorTime))
                s.Flight=NativeCannonFlightCheckpoint.TryCapture(server);
            return s;
        }

        public static Snapshot[] CaptureAll()
        {
            return NativeCheckpointComponentBatch.Find<ServerCannon>().Select(Observe).OrderBy(s=>s.Id).ToArray();
        }
        public static void Validate(Snapshot[] saved)
        {
            // Warp preparation and restoration always inspect fresh native state,
            // even if a caller accidentally enters them during collection.
            EndObservationBatch();
            var current=CaptureAll();
            if(current.Length != saved.Length) throw new InvalidOperationException("Native cannon checkpoint membership changed.");
            for(int i=0;i<saved.Length;i++) {
                var a=saved[i]; var b=current[i];
                if((!a.Settled && a.Flight==null) || (!b.Settled && b.Flight==null)
                    || (a.Flight==null && b.Flight!=null)
                    || (a.Flight!=null && b.Flight!=null && a.Flight.Passenger!=b.Flight.Passenger))
                    throw new InvalidOperationException("Native cannon "+a.Id+" cannot warp: "+(a.Reason ?? b.Reason));
                if(a.Flight!=null) NativeCannonFlightCheckpoint.Validate(a.Flight);
                if(a.Id!=b.Id || a.Instance!=b.Instance || a.Server!=b.Server || a.Client!=b.Client || a.Session!=b.Session
                    || a.ClientSession!=b.ClientSession || a.PlayerHandler!=b.PlayerHandler || a.Cosmetics!=b.Cosmetics
                    || a.Animator!=b.Animator || a.Pilot!=b.Pilot || a.ServerPilot!=b.ServerPilot || a.ClientPilot!=b.ClientPilot)
                    throw new InvalidOperationException("Native cannon checkpoint incarnation changed.");
                foreach(var pose in a.LocalPoses)
                    if(pose.Transform==null || pose.Transform.parent!=pose.Parent)
                        throw new InvalidOperationException("Native cannon transform hierarchy changed.");
                var passengers=new[] {a.ServerLoaded,a.ClientLoaded,a.ServerMessage.m_loadedObject,a.ClientMessage.m_loadedObject};
                for(int p=0;p<passengers.Length;p++) ValidateReference(passengers[p],a.PassengerEntries[p]);
            }
        }
        public static void Restore(Snapshot[] saved)
        {
            Validate(saved);
            foreach(var s in saved) {
                Write(typeof(ServerCannon),s.Server,"m_loadedObject",s.ServerLoaded);
                Write(typeof(ServerCannon),s.Server,"m_message",Copy(s.ServerMessage));
                Write(typeof(ServerCannon),s.Server,"m_readyToLaunch",s.Ready);
                Write(typeof(ServerCannon),s.Server,"m_flying",s.Flying);
                Write(typeof(ClientCannon),s.Client,"m_loadedObject",s.ClientLoaded);
                Write(typeof(ClientCannon),s.Client,"m_message",Copy(s.ClientMessage));
                Write(typeof(ClientCannon),s.Client,"m_exitPosition",s.ExitPosition);
                Write(typeof(ClientCannon),s.Client,"m_exitRotation",s.ExitRotation);
                if(s.Pilot != null) s.Pilot.m_transformToRotate.rotation=s.PilotRotation;
                // No native load/fire/session callback or iterator is advanced.
                // A steady flight receives a fresh copy of its saved iterator.
                if(s.Flight!=null) s.Animator.SetBool("IsOccupied",s.AnimatorOccupied);
                s.Animator.ResetTrigger("CannonFire"); s.Animator.Play(s.AnimatorState,0,s.AnimatorTime);
                // Play schedules evaluation. Flush the restored idle pose at
                // zero elapsed time before the checkpoint acknowledgement.
                s.Animator.Update(0f);
                if(s.Pilot!=null) {
                    RestoreFields(typeof(ServerPilotRotation),s.ServerPilot,serverPilotFields,s.ServerPilotFields);
                    RestoreFields(typeof(ClientPilotRotation),s.ClientPilot,clientPilotFields,s.ClientPilotFields);
                    RestoreFields(typeof(PilotRotation),s.Pilot,pilotMotionFields,s.PilotMotionFields);
                }
                // Restore the native local hierarchy, not rounded network Euler
                // angles or world-to-local conversions of attached points.
                foreach(var pose in s.LocalPoses) {
                    if(!SameVector(pose.Transform.localPosition,pose.Position)) pose.Transform.localPosition=pose.Position;
                    if(!SameQuaternion(pose.Transform.localRotation,pose.Rotation)) pose.Transform.localRotation=pose.Rotation;
                }
                if(s.Flight!=null) NativeCannonFlightCheckpoint.Restore(s.Flight);
            }
        }
        public static void VerifyRestored(Snapshot[] saved)
        {
            EndObservationBatch();
            var current=CaptureAll();
            if(current.Length!=saved.Length) throw new InvalidOperationException("Native cannon restore membership mismatch.");
            for(int i=0;i<saved.Length;i++) {
                var a=saved[i]; var b=current[i];
                var differences = new List<string>();
                if(a.Server!=b.Server) differences.Add("server");
                if(a.Flight==null && !b.Settled) differences.Add("settled:"+b.Reason);
                if(a.Flying!=b.Flying) differences.Add("flying");
                if(!NativeCannonFlightCheckpoint.Same(a.Flight,b.Flight)) differences.Add("steadyFlightState");
                if(a.Ready!=b.Ready) differences.Add("ready");
                if(a.ServerLoaded!=b.ServerLoaded) differences.Add("serverLoaded");
                if(a.ClientLoaded!=b.ClientLoaded) differences.Add("clientLoaded");
                if(!SameMessage(a.ServerMessage,b.ServerMessage)) differences.Add("serverMessage");
                if(!SameMessage(a.ClientMessage,b.ClientMessage)) differences.Add("clientMessage");
                if(!SameVector(a.ExitPosition,b.ExitPosition)) differences.Add("exitPosition:"+a.ExitPosition+" -> "+b.ExitPosition);
                if(!SameQuaternion(a.ExitRotation,b.ExitRotation)) differences.Add("exitRotation:"+a.ExitRotation+" -> "+b.ExitRotation);
                if(!SameQuaternion(a.PilotRotation,b.PilotRotation)) differences.Add("pilotRotation:"+a.PilotRotation+" -> "+b.PilotRotation);
                if(a.AnimatorState!=b.AnimatorState) differences.Add("animatorState");
                if(a.AnimatorTime!=b.AnimatorTime) differences.Add("animatorTime:"+a.AnimatorTime.ToString("R")+" -> "+b.AnimatorTime.ToString("R"));
                if(a.AnimatorOccupied!=b.AnimatorOccupied) differences.Add("animatorOccupied");
                if(!SameFields(a.ServerPilotFields,b.ServerPilotFields) || !SameFields(a.ClientPilotFields,b.ClientPilotFields)
                    || !SameFields(a.PilotMotionFields,b.PilotMotionFields)) differences.Add("pilotPrivateState");
                if(!SamePoses(a.LocalPoses,b.LocalPoses)) differences.Add("localHierarchy");
                if(differences.Count!=0) throw new InvalidOperationException("Native cannon "+a.Id+" restoration postcondition failed: "+string.Join(", ",differences.ToArray()));
            }
        }
        public static bool SameBoundary(Snapshot[] a,Snapshot[] b)
        {
            if(a.Length!=b.Length) return false;
            for(int i=0;i<a.Length;i++)
                if(a[i].Server!=b[i].Server || a[i].ServerSessionActive!=b[i].ServerSessionActive || a[i].ClientSessionActive!=b[i].ClientSessionActive
                    || a[i].Flying!=b[i].Flying || a[i].Ready!=b[i].Ready || a[i].Launches!=b[i].Launches || a[i].Settled!=b[i].Settled
                    || a[i].ServerLoaded!=b[i].ServerLoaded || a[i].ClientLoaded!=b[i].ClientLoaded || !SameQuaternion(a[i].PilotRotation,b[i].PilotRotation)
                    || !SameMessage(a[i].ServerMessage,b[i].ServerMessage) || !SameMessage(a[i].ClientMessage,b[i].ClientMessage)
                    || !NativeCannonFlightCheckpoint.Same(a[i].Flight,b[i].Flight)) return false;
            return true;
        }
        private static bool SameMessage(CannonMessage a,CannonMessage b) { return a.m_state==b.m_state && a.m_angle==b.m_angle && a.m_loadedObject==b.m_loadedObject; }
        private static readonly string[] serverPilotFields={"m_angle","m_startAngle","m_startRightDirection","m_message","m_controlScheme"};
        private static readonly string[] clientPilotFields={"m_nextRotation","m_message","m_avatar"};
        private static readonly string[] pilotMotionFields={"m_previousPose","m_previousPoseDifference","m_velocityAverage","m_belowThresholdCounter","m_bEstimateVelocityInX","m_directionModifier"};
        internal sealed class LocalPose { internal Transform Transform,Parent;internal Vector3 Position;internal Quaternion Rotation; }
        private static LocalPose[] CaptureLocalPoses(Transform root,PilotRotation pilot) {
            var transforms=new HashSet<Transform>();
            Action<Transform> add=value=>{while(value!=null && value.IsChildOf(root)){transforms.Add(value);if(value==root)break;value=value.parent;}};
            add(root);if(pilot!=null)add(pilot.m_transformToRotate);
            var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)add(entries._items[i].m_GameObject.transform);
            return transforms.OrderBy(Depth).ThenBy(t=>t.GetInstanceID()).Select(t=>new LocalPose {Transform=t,Parent=t.parent,Position=t.localPosition,Rotation=t.localRotation}).ToArray();
        }
        private static int Depth(Transform value) {int result=0;while(value.parent!=null){result++;value=value.parent;}return result;}
        private static object CopyPilotField(object value) {var message=value as PilotRotationMessage;return message==null?value:new PilotRotationMessage {m_angle=message.m_angle};}
        private static object[] CaptureFields(Type type,object target,string[] names) {return names.Select(name=>CopyPilotField(Read(type,target,name))).ToArray();}
        private static void RestoreFields(Type type,object target,string[] names,object[] values) {for(int i=0;i<names.Length;i++)Write(type,target,names[i],CopyPilotField(values[i]));}
        private static bool SameFields(object[] a,object[] b) {
            if(a==null || b==null)return a==b;if(a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++) {var x=a[i] as PilotRotationMessage;var y=b[i] as PilotRotationMessage;
                if(x!=null || y!=null){if(x==null || y==null || x.m_angle!=y.m_angle)return false;}
                else if(!object.Equals(a[i],b[i]))return false;
            }return true;
        }
        private static bool SamePoses(LocalPose[] a,LocalPose[] b) {
            if(a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(a[i].Transform!=b[i].Transform || a[i].Parent!=b[i].Parent
                || !SameVector(a[i].Position,b[i].Position) || !SameQuaternion(a[i].Rotation,b[i].Rotation))return false;
            return true;
        }
        // Unity's Quaternion operator compares normalized orientations; the
        // native inactive exit quaternion can be (0,0,0,0). Compare stored fields.
        private static bool SameQuaternion(Quaternion a,Quaternion b) { return a.x==b.x && a.y==b.y && a.z==b.z && a.w==b.w; }
        private static bool SameVector(Vector3 a,Vector3 b) { return a.x==b.x && a.y==b.y && a.z==b.z; }
        private static CannonMessage Copy(CannonMessage m) { return new CannonMessage { m_state=m.m_state,m_angle=m.m_angle,m_loadedObject=m.m_loadedObject }; }
        private static void ValidateReference(GameObject value,EntitySerialisationEntry originalEntry) {
            if(!ReferenceEquals(value,null) && (value==null || originalEntry==null || !ReferenceEquals(EntitySerialisationRegistry.GetEntry(value),originalEntry)))
                throw new InvalidOperationException("Native cannon saved passenger incarnation no longer exists.");
        }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static object Read(Type type,object target,string field) { var f=AccessTools.Field(type,field); if(f==null) throw new MissingFieldException(type.FullName,field); return f.GetValue(target); }
        private static void Write(Type type,object target,string field,object value) { AccessTools.Field(type,field).SetValue(target,value); }
    }

    public sealed class NativeCannonAuxMessage : AuxMessageBase
    {
        public uint LoadedEntityId,ActiveLaunches;
        public bool ServerSessionActive,ClientSessionActive,Flying,Ready,Settled;
        public float Angle,LoadNormalizedTime;
        public override AuxEntityType GetAuxEntityType() { return AuxEntityType.NativeCannonAux; }
        public override void Serialise(BitStreamWriter writer) {
            writer.Write(1u,32);writer.Write(LoadedEntityId,32);writer.Write(ServerSessionActive);writer.Write(ClientSessionActive);
            writer.Write(Flying);writer.Write(Ready);writer.Write(ActiveLaunches,32);writer.Write(Settled);writer.Write(Angle);writer.Write(LoadNormalizedTime);
        }
    }
}
