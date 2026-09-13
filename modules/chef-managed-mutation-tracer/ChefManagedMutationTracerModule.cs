using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only trace around every managed method that can plausibly write a
    // local chef's Rigidbody during idle rewind, plus Fixed/Update/LateUpdate
    // phase samples. If pose changes between phases without a changing managed
    // call receipt, the change occurred in Unity/PhysX native simulation.
    public sealed class ChefManagedMutationTracerModule : IAuthoringModule
    {
        public sealed class BodyState
        {
            public int EntityId, BodyId;
            public Vector3 BodyPosition, TransformPosition, LocalPosition, Velocity, AngularVelocity;
            public Vector3 PreviousPosition, LocalVelocity;
            public Quaternion BodyRotation, TransformRotation, LocalRotation;
            public float XzSpeed;
            public bool Kinematic, Gravity, Sleeping;
        }

        public sealed class TraceCall
        {
            public string Method;
            public BodyState[] Before;
            public AttachmentState[] AttachmentsBefore;
        }

        public sealed class AttachmentState
        {
            public int EntityId,ServerId,TransformId,BodyId,ParentId,RegisteredAncestorId;
            public string Name,ParentPath;
            public Vector3 Position,LocalPosition,LocalScale,LossyScale,BodyPosition;
            public Quaternion Rotation,LocalRotation,BodyRotation;
            public bool Held,BodyKinematic,BodySleeping;
        }

        private sealed class TraceDriver : MonoBehaviour
        {
            private void FixedUpdate() { var value=active;if(value!=null&&value.driver==this)value.AddPhase("fixed-update"); }
            private void Update() { var value=active;if(value!=null&&value.driver==this)value.AddPhase("update"); }
            private void LateUpdate() { var value=active;if(value!=null&&value.driver==this)value.AddPhase("late-update"); }
        }

        private static ChefManagedMutationTracerModule active;
        private Harmony harmony;
        private FieldInfo previousPosition, localVelocity, xzSpeed, attachmentHeld;
        private GameObject driverObject;
        private TraceDriver driver;
        private readonly List<Rigidbody> chefs = new List<Rigidbody>();
        private readonly Dictionary<Rigidbody,int> entityIds = new Dictionary<Rigidbody,int>();
        private readonly Dictionary<Rigidbody,PlayerControls> controlsByBody = new Dictionary<Rigidbody,PlayerControls>();
        private readonly List<ServerPhysicalAttachment> attachments = new List<ServerPhysicalAttachment>();
        private readonly Dictionary<ServerPhysicalAttachment,int> attachmentEntityIds = new Dictionary<ServerPhysicalAttachment,int>();
        private readonly List<object> calls = new List<object>();
        private readonly List<object> phases = new List<object>();
        private readonly List<string> patched = new List<string>();
        private bool disposed;
        private long sequence, observedCalls;
        private int discardedCalls, discardedPhases;
        private string segment="setup", failure;

        public string Name { get { return "local-chef-attachment-managed-mutation-tracer-v3"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("ChefManagedMutationTracerModule");
            if(operation=="activate")Activate(args);
            else if(operation=="mark")Mark(args);
            else if(operation=="clear")Clear(args);
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, mark, clear, status or deactivate.");
            else RequireNone(args);
            return Status();
        }

        private void Activate(Dictionary<string,object> args)
        {
            RequireNone(args);
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Chef mutation tracer activation requires the authoring pause fence.");
            if(harmony!=null)return;
            if(active!=null)throw new InvalidOperationException("Another chef mutation tracer is active.");
            BindChefs();
            BindAttachments();
            previousPosition=AccessTools.Field(typeof(PlayerControls),"m_previousPosition");
            localVelocity=AccessTools.Field(typeof(PlayerControls),"m_localVelocity");
            xzSpeed=AccessTools.Field(typeof(PlayerControls),"m_xzSpeed");
            attachmentHeld=AccessTools.Field(typeof(ServerPhysicalAttachment),"m_isHeld");
            if(previousPosition==null||previousPosition.FieldType!=typeof(Vector3)
                ||localVelocity==null||localVelocity.FieldType!=typeof(Vector3)
                ||xzSpeed==null||xzSpeed.FieldType!=typeof(float)
                ||attachmentHeld==null||attachmentHeld.FieldType!=typeof(bool))
                throw new InvalidOperationException("Installed movement/attachment trace contract differs.");
            harmony=new Harmony("supercharged.authoring.chef-managed-mutation-tracer."+GetType().Assembly.GetName().Name);
            active=this;
            try
            {
                AddNamed(typeof(RigidbodyMotion),"Awake","OnDisable","SetKinematic","SetVelocity","AddVelocity",
                    "Movement","SetPosition","SetRotation");
                var frozen=typeof(TimeManager).GetNestedType("FrozenPhysicsData",BindingFlags.NonPublic);
                if(frozen==null)throw new InvalidOperationException("Installed FrozenPhysicsData is missing.");
                Add(frozen.GetConstructor(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic,null,
                    new[]{typeof(Rigidbody),typeof(int)},null));
                AddNamed(frozen,"Unfreeze");
                AddNamed(typeof(ClientPlayerControlsImpl_Default),"Awake","StartSynchronising","Disable","Update_Movement");
                AddNamed(typeof(PlayerControls),"FixedUpdate");
                AddNamed(typeof(PlayerAnimationDecisions),"UpdateVariables");
                AddNamed(typeof(PositionRecorder),"Awake","TakeSample","Setup","Clear",
                    "GetLagCompensatedPositionDelta","GetLagCompensatedPositionDeltaParents","Teleport");
                AddNamed(typeof(RemoteChefPositionRecorder),"InternalRestorePosition","InternalSetChefToTime");
                AddNamed(typeof(ClientOnTheServerChefSynchroniser),"StartSynchronising","ApplyServerUpdate",
                    "ApplyServerEvent","ApplyResumeData","HandleMessage","Pause","Resume");
                AddNamed(typeof(ClientWorldObjectSynchroniser),"DoReparenting","ApplyServerUpdate","ApplyServerEvent","ApplyResumeData");
                AddNamed(typeof(ClientWorldObjectSynchroniser),"CorrectScale","ParentingLogic","OnParentChanged","UpdateSynchronising");
                AddNamed(typeof(ServerPhysicalAttachment),"Attach","Detach","AttachToRigidBodyContainer",
                    "DetachFromRigidBodyContainer","OnAttachChanged","UpdateSynchronising");
                AddNamed(typeof(ClientPhysicalAttachment),"ApplyServerEvent","OnParentChanged","UpdateSynchronising");
                AddNamed(typeof(NativeKitchenCheckpoint.RestorePlan),"Complete");
                AddNamed(typeof(NativeBodyPoseCheckpoint),"Restore","RestoreBuiltin","RecomputeMassFrame","RestoreRotation");
                AddNamed(typeof(WarpHandler),"HandleWarpRequestIfAny","WarpChefAndPositions");
                // These are the currently used out-of-core authoring seams.
                // Patch every loaded revision: inactive revisions never call
                // their static callbacks, while this avoids silently omitting
                // a pose experiment merely because its assembly is hot-loaded.
                AddExternalNamed("SuperchargedPatch.Authoring.Modules.BodyRestoreModule",
                    "Restore","RestorePositionBeforeMassReset","RestoreRotation","RestoreEmptyProxyMass");
                AddExternalNamed("SuperchargedPatch.Authoring.Modules.ChefPausePoseModule",
                    "BeforeFreeze","BeforeCapture","BeforeUnfreeze","AfterUnfreeze");
                AddExternalNamed("SuperchargedPatch.Authoring.Modules.ChefContactRefreshModule",
                    "BeforeUnfreeze","AfterUnfreeze");
                AddExternalNamed("SuperchargedPatch.Authoring.Modules.PhysicsSyncAfterRestoreModule",
                    "AfterComplete","AfterHandleWarp","Synchronize");
                if(patched.Count<20)throw new InvalidOperationException("Unexpectedly small managed writer/lifecycle target set.");
                driverObject=new GameObject("__TAS_ChefManagedMutationTracer");
                driver=driverObject.AddComponent<TraceDriver>();
                AddPhase("activated");
            }
            catch(Exception error){failure="Activation: "+error.Message;Deactivate();throw;}
        }

        private void BindChefs()
        {
            chefs.Clear();entityIds.Clear();controlsByBody.Clear();
            var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries._items[i];var obj=entry==null?null:entry.m_GameObject;
                var body=obj==null?null:obj.GetComponent<Rigidbody>();
                if(body==null||body.GetComponent<ServerChefSynchroniser>()==null
                    ||body.GetComponent<ClientOnTheServerChefSynchroniser>()==null)continue;
                chefs.Add(body);entityIds.Add(body,(int)entry.m_Header.m_uEntityID);
                var controls=obj.GetComponent<PlayerControls>();
                if(controls==null)throw new InvalidOperationException("Local chef lacks PlayerControls.");
                controlsByBody.Add(body,controls);
            }
            chefs.Sort((a,b)=>entityIds[a].CompareTo(entityIds[b]));
            if(chefs.Count!=4||entityIds.Values.Distinct().Count()!=4)
                throw new InvalidOperationException("Tracer requires four distinct registered local chefs.");
        }

        private void BindAttachments()
        {
            attachments.Clear();attachmentEntityIds.Clear();
            var entries=EntitySerialisationRegistry.m_EntitiesList;
            for(int i=0;i<entries.Count;i++)
            {
                var entry=entries._items[i];var obj=entry==null?null:entry.m_GameObject;
                var server=obj==null?null:obj.GetComponent<ServerPhysicalAttachment>();
                var physical=obj==null?null:obj.GetComponent<PhysicalAttachment>();
                if(server==null||physical==null||physical.m_container==null)continue;
                if(attachmentEntityIds.ContainsKey(server))throw new InvalidOperationException("Duplicate physical attachment component.");
                attachments.Add(server);attachmentEntityIds.Add(server,(int)entry.m_Header.m_uEntityID);
            }
            attachments.Sort((a,b)=>attachmentEntityIds[a].CompareTo(attachmentEntityIds[b]));
            if(attachments.Count==0)throw new InvalidOperationException("No registered physical attachments were found.");
        }

        private void AddNamed(Type type,params string[] names)
        {
            const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
            foreach(string name in names)
            {
                var methods=type.GetMethods(flags).Where(method=>method.Name==name).Cast<MethodBase>().ToArray();
                foreach(var method in methods)Add(method);
            }
        }

        private void AddExternalNamed(string fullName,params string[] names)
        {
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try{type=assembly.GetType(fullName,false);}
                catch{continue;}
                if(type!=null)AddNamed(type,names);
            }
        }

        private void Add(MethodBase method)
        {
            if(method==null)return;
            string name=method.DeclaringType.FullName+"::"+method;
            if(patched.Contains(name))return;
            harmony.Patch(method,prefix:Hook("BeforeManagedCall"),postfix:Hook("AfterManagedCall"));
            patched.Add(name);
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name,BindingFlags.Public|BindingFlags.Static));
        }

        public static void BeforeManagedCall(MethodBase __originalMethod,out TraceCall __state)
        {
            __state=null;var module=active;
            if(module==null||TimeManager.IsPaused(TimeManager.PauseLayer.Main))return;
            try{__state=new TraceCall{Method=__originalMethod.DeclaringType.FullName+"::"+__originalMethod,
                Before=module.ReadAll(),AttachmentsBefore=module.ReadAttachments()};}
            catch(Exception error){module.failure="Managed prefix: "+error.Message;throw;}
        }

        public static void AfterManagedCall(TraceCall __state)
        {
            var module=active;if(module==null||__state==null)return;
            try{module.AddCall(__state,module.ReadAll(),module.ReadAttachments());}
            catch(Exception error){module.failure="Managed postfix: "+error.Message;throw;}
        }

        private BodyState[] ReadAll()
        {
            var result=new BodyState[chefs.Count];
            for(int i=0;i<chefs.Count;i++)
            {
                var body=chefs[i];
                if(body==null||body.GetInstanceID()==0||!entityIds.ContainsKey(body)
                    ||body.GetComponent<ServerChefSynchroniser>()==null
                    ||body.GetComponent<ClientOnTheServerChefSynchroniser>()==null)
                    throw new InvalidOperationException("Local chef incarnation changed.");
                PlayerControls controls;
                if(!controlsByBody.TryGetValue(body,out controls)||controls==null)
                    throw new InvalidOperationException("Local PlayerControls incarnation changed.");
                result[i]=new BodyState{EntityId=entityIds[body],BodyId=body.GetInstanceID(),
                    BodyPosition=body.position,TransformPosition=body.transform.position,LocalPosition=body.transform.localPosition,
                    BodyRotation=body.rotation,TransformRotation=body.transform.rotation,LocalRotation=body.transform.localRotation,
                    Velocity=body.velocity,AngularVelocity=body.angularVelocity,Kinematic=body.isKinematic,
                    Gravity=body.useGravity,Sleeping=body.IsSleeping(),
                    PreviousPosition=(Vector3)previousPosition.GetValue(controls),
                    LocalVelocity=(Vector3)localVelocity.GetValue(controls),XzSpeed=(float)xzSpeed.GetValue(controls)};
            }
            return result;
        }

        private AttachmentState[] ReadAttachments()
        {
            var result=new AttachmentState[attachments.Count];
            for(int i=0;i<attachments.Count;i++)
            {
                var server=attachments[i];int entityId;
                if(server==null||server.GetInstanceID()==0||!attachmentEntityIds.TryGetValue(server,out entityId))
                    throw new InvalidOperationException("Physical attachment incarnation changed.");
                var transform=server.transform;var physical=server.GetComponent<PhysicalAttachment>();
                var body=physical==null?null:physical.m_container;
                if(transform==null||physical==null||body==null)
                    throw new InvalidOperationException("Physical attachment owner/container contract changed: "+entityId+".");
                Transform parent=transform.parent;
                result[i]=new AttachmentState{EntityId=entityId,ServerId=server.GetInstanceID(),
                    TransformId=transform.GetInstanceID(),BodyId=body.GetInstanceID(),Name=server.name,
                    ParentId=parent==null?0:parent.GetInstanceID(),ParentPath=TransformPath(parent),
                    RegisteredAncestorId=RegisteredAncestor(parent),Position=transform.position,
                    LocalPosition=transform.localPosition,LocalScale=transform.localScale,LossyScale=transform.lossyScale,
                    Rotation=transform.rotation,LocalRotation=transform.localRotation,
                    BodyPosition=body.position,BodyRotation=body.rotation,BodyKinematic=body.isKinematic,
                    BodySleeping=body.IsSleeping(),Held=(bool)attachmentHeld.GetValue(server)};
            }
            return result;
        }

        private void AddCall(TraceCall call,BodyState[] after,AttachmentState[] attachmentsAfter)
        {
            observedCalls++;
            calls.Add(new Dictionary<string,object>{{"sequence",++sequence},{"segment",segment},
                {"method",call.Method},{"unityFrame",Time.frameCount},{"time",Time.time},{"fixedTime",Time.fixedTime},
                {"outputFrame",OutputFrame()},{"outputPhase",OutputPhase()},
                {"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},
                {"changed",!Same(call.Before,after)},{"changedAttachments",!Same(call.AttachmentsBefore,attachmentsAfter)},
                {"before",Encode(call.Before)},{"after",Encode(after)},
                {"attachmentsBefore",Encode(call.AttachmentsBefore)},{"attachmentsAfter",Encode(attachmentsAfter)}});
            if(calls.Count>4096){calls.RemoveAt(0);discardedCalls++;}
        }

        private void AddPhase(string phase)
        {
            if((phase=="fixed-update"||phase=="update"||phase=="late-update")
                &&TimeManager.IsPaused(TimeManager.PauseLayer.Main))return;
            try
            {
                phases.Add(new Dictionary<string,object>{{"sequence",++sequence},{"segment",segment},{"phase",phase},
                    {"unityFrame",Time.frameCount},{"time",Time.time},{"fixedTime",Time.fixedTime},
                    {"outputFrame",OutputFrame()},{"outputPhase",OutputPhase()},
                    {"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},{"chefs",Encode(ReadAll())},
                    {"attachments",Encode(ReadAttachments())}});
                if(phases.Count>2048){phases.RemoveAt(0);discardedPhases++;}
            }
            catch(Exception error){failure="Phase sample: "+error.Message;throw;}
        }

        private static bool Same(BodyState[] a,BodyState[] b)
        {
            if(a==null||b==null||a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(!Same(a[i],b[i]))return false;
            return true;
        }

        private static bool Same(BodyState a,BodyState b)
        {
            return a.EntityId==b.EntityId&&a.BodyId==b.BodyId&&Exact(a.BodyPosition,b.BodyPosition)
                &&Exact(a.TransformPosition,b.TransformPosition)&&Exact(a.LocalPosition,b.LocalPosition)
                &&Exact(a.BodyRotation,b.BodyRotation)&&Exact(a.TransformRotation,b.TransformRotation)&&Exact(a.LocalRotation,b.LocalRotation)
                &&Exact(a.Velocity,b.Velocity)&&Exact(a.AngularVelocity,b.AngularVelocity)&&a.Kinematic==b.Kinematic
                &&a.Gravity==b.Gravity&&a.Sleeping==b.Sleeping&&Exact(a.PreviousPosition,b.PreviousPosition)
                &&Exact(a.LocalVelocity,b.LocalVelocity)&&a.XzSpeed==b.XzSpeed;
        }

        private static bool Same(AttachmentState[] a,AttachmentState[] b)
        {
            if(a==null||b==null||a.Length!=b.Length)return false;
            for(int i=0;i<a.Length;i++)if(!Same(a[i],b[i]))return false;
            return true;
        }

        private static bool Same(AttachmentState a,AttachmentState b)
        {
            return a.EntityId==b.EntityId&&a.ServerId==b.ServerId&&a.TransformId==b.TransformId&&a.BodyId==b.BodyId
                &&a.ParentId==b.ParentId&&a.RegisteredAncestorId==b.RegisteredAncestorId&&a.Name==b.Name&&a.ParentPath==b.ParentPath
                &&Exact(a.Position,b.Position)&&Exact(a.LocalPosition,b.LocalPosition)&&Exact(a.LocalScale,b.LocalScale)&&Exact(a.LossyScale,b.LossyScale)
                &&Exact(a.Rotation,b.Rotation)&&Exact(a.LocalRotation,b.LocalRotation)&&Exact(a.BodyPosition,b.BodyPosition)&&Exact(a.BodyRotation,b.BodyRotation)
                &&a.Held==b.Held&&a.BodyKinematic==b.BodyKinematic&&a.BodySleeping==b.BodySleeping;
        }

        private static bool Exact(Vector3 a,Vector3 b)
        { return a.x==b.x&&a.y==b.y&&a.z==b.z; }
        private static bool Exact(Quaternion a,Quaternion b)
        { return a.x==b.x&&a.y==b.y&&a.z==b.z&&a.w==b.w; }

        private static object[] Encode(BodyState[] values)
        {
            return values.Select(value=>(object)new Dictionary<string,object>{{"entityId",value.EntityId},{"bodyInstanceId",value.BodyId},
                {"bodyPosition",Point(value.BodyPosition)},{"transformPosition",Point(value.TransformPosition)},
                {"localPosition",Point(value.LocalPosition)},{"bodyRotation",Rotation(value.BodyRotation)},
                {"transformRotation",Rotation(value.TransformRotation)},{"localRotation",Rotation(value.LocalRotation)},
                {"velocity",Point(value.Velocity)},{"angularVelocity",Point(value.AngularVelocity)},
                {"previousPosition",Point(value.PreviousPosition)},{"localVelocity",Point(value.LocalVelocity)},
                {"xzSpeed",value.XzSpeed},{"bodyPositionBits",PointBits(value.BodyPosition)},
                {"transformPositionBits",PointBits(value.TransformPosition)},{"localPositionBits",PointBits(value.LocalPosition)},
                {"previousPositionBits",PointBits(value.PreviousPosition)},{"localVelocityBits",PointBits(value.LocalVelocity)},
                {"xzSpeedBits",Bits(value.XzSpeed)},
                {"bodyYBits",Bits(value.BodyPosition.y)},{"transformYBits",Bits(value.TransformPosition.y)},
                {"localYBits",Bits(value.LocalPosition.y)},{"kinematic",value.Kinematic},{"gravity",value.Gravity},
                {"sleeping",value.Sleeping}}).ToArray();
        }

        private static object[] Encode(AttachmentState[] values)
        {
            return values.Select(value=>(object)new Dictionary<string,object>{{"entityId",value.EntityId},
                {"serverInstanceId",value.ServerId},{"transformInstanceId",value.TransformId},{"bodyInstanceId",value.BodyId},
                {"name",value.Name},{"parentInstanceId",value.ParentId},{"parentPath",value.ParentPath},
                {"registeredAncestorEntityId",value.RegisteredAncestorId},{"held",value.Held},
                {"position",Point(value.Position)},{"localPosition",Point(value.LocalPosition)},
                {"localScale",Point(value.LocalScale)},{"lossyScale",Point(value.LossyScale)},
                {"rotation",Rotation(value.Rotation)},{"localRotation",Rotation(value.LocalRotation)},
                {"bodyPosition",Point(value.BodyPosition)},{"bodyRotation",Rotation(value.BodyRotation)},
                {"localScaleBits",PointBits(value.LocalScale)},{"lossyScaleBits",PointBits(value.LossyScale)},
                {"bodyKinematic",value.BodyKinematic},{"bodySleeping",value.BodySleeping}}).ToArray();
        }

        private void Mark(Dictionary<string,object> args)
        {
            if(args==null||args.Count!=1||!args.ContainsKey("label"))throw new ArgumentException("mark requires only label.");
            string value=args["label"] as string;
            if(String.IsNullOrEmpty(value)||value.Length>32||value.Any(c=>!Char.IsLetterOrDigit(c)&&c!='-'&&c!='_'))
                throw new ArgumentException("mark label must be bounded alphanumeric text.");
            segment=value;AddPhase("mark");
        }

        private void Clear(Dictionary<string,object> args)
        {
            RequireNone(args);calls.Clear();phases.Clear();discardedCalls=discardedPhases=0;sequence=observedCalls=0;failure=null;AddPhase("clear");
        }

        private object Status()
        {
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"segment",segment},{"chefEntityIds",entityIds.Values.OrderBy(x=>x).ToArray()},
                {"attachmentEntityIds",attachmentEntityIds.Values.OrderBy(x=>x).ToArray()},
                {"patchedMethods",patched.ToArray()},{"observedCalls",observedCalls},{"discardedCalls",discardedCalls},
                {"discardedPhases",discardedPhases},{"failure",failure},{"current",Encode(ReadAll())},
                {"currentAttachments",Encode(ReadAttachments())},
                {"calls",calls.ToArray()},{"phases",phases.ToArray()},
                {"scope","Read-only local-chef and registered PhysicalAttachment public Rigidbody/Transform state around managed pose, attachment, scale-correction and lifecycle methods, interleaved with FixedUpdate/Update/LateUpdate samples. No Unity setter, physics step, input, clock, synchronizer or gameplay-state write."}};
        }

        private static void RequireNone(Dictionary<string,object> args){if(args!=null&&args.Count!=0)throw new ArgumentException("Operation takes no arguments.");}
        private static object Point(Vector3 value){return new[]{value.x,value.y,value.z};}
        private static object PointBits(Vector3 value){return new[]{Bits(value.x),Bits(value.y),Bits(value.z)};}
        private static object Rotation(Quaternion value){return new[]{value.x,value.y,value.z,value.w};}
        private static int Bits(float value){return BitConverter.ToInt32(BitConverter.GetBytes(value),0);}
        private static int OutputFrame(){return Hpmv.Injector.Server==null?-1:Hpmv.Injector.Server.CurrentFrameData.FrameNumber;}
        private static int OutputPhase(){return Hpmv.Injector.Server==null?-1:Hpmv.Injector.Server.CurrentFrameData.FramesSinceLastNoPhysicsFrame;}

        private static string TransformPath(Transform value)
        {
            if(value==null)return null;
            var names=new List<string>();Transform cursor=value;
            for(int i=0;cursor!=null&&i<64;i++){names.Add(cursor.name+"#"+cursor.GetInstanceID());cursor=cursor.parent;}
            names.Reverse();return String.Join("/",names.ToArray());
        }

        private static int RegisteredAncestor(Transform value)
        {
            var entries=EntitySerialisationRegistry.m_EntitiesList;Transform cursor=value;
            for(int depth=0;cursor!=null&&depth<64;depth++,cursor=cursor.parent)
                for(int i=0;i<entries.Count;i++)
                {
                    var entry=entries._items[i];
                    if(entry!=null&&entry.m_GameObject!=null&&ReferenceEquals(entry.m_GameObject,cursor.gameObject))
                        return (int)entry.m_Header.m_uEntityID;
                }
            return 0;
        }

        private void Deactivate()
        {
            if(harmony!=null)harmony.UnpatchSelf();harmony=null;
            if(driverObject!=null)UnityEngine.Object.Destroy(driverObject);driverObject=null;driver=null;
            if(ReferenceEquals(active,this))active=null;
        }

        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
