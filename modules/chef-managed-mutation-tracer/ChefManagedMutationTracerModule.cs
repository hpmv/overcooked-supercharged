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
            public Vector3 GroundPoint, GroundNormal, SurfaceVelocity, LastVelocity;
            public Quaternion BodyRotation, TransformRotation, LocalRotation;
            public float XzSpeed, GroundDistance, LeftOverTime, DashTimer, ImpactTimer;
            public int GroundColliderId;
            public bool Kinematic, Gravity, Sleeping, GroundCurrent, ApplyGravity;
        }

        public sealed class TraceCall
        {
            public string Method;
            public BodyState[] Before;
        }

        private sealed class TraceDriver : MonoBehaviour
        {
            private void FixedUpdate() { var value=active;if(value!=null&&value.driver==this)value.AddPhase("fixed-update"); }
            private void Update() { var value=active;if(value!=null&&value.driver==this)value.AddPhase("update"); }
            private void LateUpdate() { var value=active;if(value!=null&&value.driver==this)value.AddPhase("late-update"); }
        }

        private static ChefManagedMutationTracerModule active, installed;
        private Harmony harmony;
        private FieldInfo previousPosition, localVelocity, xzSpeed, applyGravity;
        private FieldInfo groundCastField, surfaceMovableField, groundCollider, groundPoint, groundNormal;
        private FieldInfo groundDistance, groundCurrent, surfaceVelocity, leftOverTime, lastVelocity;
        private FieldInfo dashTimer, impactTimer;
        private GameObject driverObject;
        private TraceDriver driver;
        private readonly List<Rigidbody> chefs = new List<Rigidbody>();
        private readonly Dictionary<Rigidbody,int> entityIds = new Dictionary<Rigidbody,int>();
        private readonly Dictionary<Rigidbody,PlayerControls> controlsByBody = new Dictionary<Rigidbody,PlayerControls>();
        private readonly List<object> calls = new List<object>();
        private readonly List<object> phases = new List<object>();
        private readonly List<string> patched = new List<string>();
        private bool disposed;
        private long sequence, observedCalls;
        private int discardedCalls, discardedPhases;
        private string segment="setup", failure;

        public string Name { get { return "local-chef-managed-mutation-tracer-v5-ground-force-only"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("ChefManagedMutationTracerModule");
            if(operation=="activate")Activate(args);
            else if(operation=="suspend")Suspend(args);
            else if(operation=="resume")Resume(args);
            else if(operation=="mark")Mark(args);
            else if(operation=="clear")Clear(args);
            else if(operation=="deactivate")Deactivate();
            else if(operation!="status")throw new ArgumentException("Use activate, suspend, resume, mark, clear, status or deactivate.");
            else RequireNone(args);
            return Status();
        }

        private void Activate(Dictionary<string,object> args)
        {
            RequireNone(args);
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Chef mutation tracer activation requires the authoring pause fence.");
            if(harmony!=null){Resume(args);return;}
            if(installed!=null||active!=null)throw new InvalidOperationException("Another chef mutation tracer is installed.");
            BindChefs();
            previousPosition=AccessTools.Field(typeof(PlayerControls),"m_previousPosition");
            localVelocity=AccessTools.Field(typeof(PlayerControls),"m_localVelocity");
            xzSpeed=AccessTools.Field(typeof(PlayerControls),"m_xzSpeed");
            applyGravity=AccessTools.Field(typeof(PlayerControls),"m_bApplyGravity");
            groundCastField=AccessTools.Field(typeof(PlayerControls),"m_groundCast");
            surfaceMovableField=AccessTools.Field(typeof(PlayerControls),"m_surfaceMovable");
            groundCollider=AccessTools.Field(typeof(GroundCast),"m_groundCollider");
            groundPoint=AccessTools.Field(typeof(GroundCast),"m_groundPoint");
            groundNormal=AccessTools.Field(typeof(GroundCast),"m_groundNormal");
            groundDistance=AccessTools.Field(typeof(GroundCast),"m_groundDistance");
            groundCurrent=AccessTools.Field(typeof(GroundCast),"m_isCurrent");
            surfaceVelocity=AccessTools.Field(typeof(SurfaceMovable),"m_surfaceVelocity");
            leftOverTime=AccessTools.Field(typeof(ClientPlayerControlsImpl_Default),"m_LeftOverTime");
            lastVelocity=AccessTools.Field(typeof(ClientPlayerControlsImpl_Default),"m_lastVelocity");
            dashTimer=AccessTools.Field(typeof(ClientPlayerControlsImpl_Default),"m_dashTimer");
            impactTimer=AccessTools.Field(typeof(ClientPlayerControlsImpl_Default),"m_impactTimer");
            if(previousPosition==null||previousPosition.FieldType!=typeof(Vector3)
                ||localVelocity==null||localVelocity.FieldType!=typeof(Vector3)
                ||xzSpeed==null||xzSpeed.FieldType!=typeof(float)
                ||applyGravity==null||applyGravity.FieldType!=typeof(bool)
                ||groundCastField==null||groundCastField.FieldType!=typeof(GroundCast)
                ||surfaceMovableField==null||surfaceMovableField.FieldType!=typeof(SurfaceMovable)
                ||groundCollider==null||groundCollider.FieldType!=typeof(Collider)
                ||groundPoint==null||groundPoint.FieldType!=typeof(Vector3)
                ||groundNormal==null||groundNormal.FieldType!=typeof(Vector3)
                ||groundDistance==null||groundDistance.FieldType!=typeof(float)
                ||groundCurrent==null||groundCurrent.FieldType!=typeof(bool)
                ||surfaceVelocity==null||surfaceVelocity.FieldType!=typeof(Vector3)
                ||leftOverTime==null||leftOverTime.FieldType!=typeof(float)
                ||lastVelocity==null||lastVelocity.FieldType!=typeof(Vector3)
                ||dashTimer==null||dashTimer.FieldType!=typeof(float)
                ||impactTimer==null||impactTimer.FieldType!=typeof(float))
                throw new InvalidOperationException("Installed movement trace contract differs.");
            harmony=new Harmony("supercharged.authoring.chef-managed-mutation-tracer."+GetType().Assembly.GetName().Name);
            installed=active=this;
            try
            {
                // Patch only methods that execute during ordinary advancing chef
                // movement. In particular, never detour authoring warp, checkpoint,
                // synchronizer, attachment or FrozenPhysicsData methods: rebuilding
                // those Harmony chains changed the kinematic restore experiment.
                AddNamed(typeof(RigidbodyMotion),"SetVelocity","AddVelocity","Accelerate","Movement");
                AddNamed(typeof(GroundCast),"Update","ForceUpdateNow","FindGround","ProcessGroundHit","HitGround");
                AddNamed(typeof(SurfaceMovable),"Update","OnGroundChanged");
                AddNamed(typeof(ClientPlayerControlsImpl_Default),"Update_Impl",
                    "Update_Movement","ApplyGravityForce","ApplyGroundMovement");
                AddNamed(typeof(PlayerControls),"FixedUpdate");
                if(patched.Count<10)throw new InvalidOperationException("Unexpectedly small managed movement target set.");
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

        private void AddNamed(Type type,params string[] names)
        {
            const BindingFlags flags=BindingFlags.Static|BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly;
            foreach(string name in names)
            {
                var methods=type.GetMethods(flags).Where(method=>method.Name==name).Cast<MethodBase>().ToArray();
                foreach(var method in methods)Add(method);
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
                Before=module.ReadAll()};}
            catch(Exception error){module.failure="Managed prefix: "+error.Message;throw;}
        }

        public static void AfterManagedCall(TraceCall __state)
        {
            var module=active;if(module==null||__state==null)return;
            try{module.AddCall(__state,module.ReadAll());}
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
                var ground=(GroundCast)groundCastField.GetValue(controls);
                var surface=(SurfaceMovable)surfaceMovableField.GetValue(controls);
                var client=body.GetComponent<ClientPlayerControlsImpl_Default>();
                if(ground==null||surface==null||client==null)
                    throw new InvalidOperationException("Local chef movement dependencies changed.");
                var collider=(Collider)groundCollider.GetValue(ground);
                result[i]=new BodyState{EntityId=entityIds[body],BodyId=body.GetInstanceID(),
                    BodyPosition=body.position,TransformPosition=body.transform.position,LocalPosition=body.transform.localPosition,
                    BodyRotation=body.rotation,TransformRotation=body.transform.rotation,LocalRotation=body.transform.localRotation,
                    Velocity=body.velocity,AngularVelocity=body.angularVelocity,Kinematic=body.isKinematic,
                    Gravity=body.useGravity,Sleeping=body.IsSleeping(),
                    PreviousPosition=(Vector3)previousPosition.GetValue(controls),
                    LocalVelocity=(Vector3)localVelocity.GetValue(controls),XzSpeed=(float)xzSpeed.GetValue(controls),
                    GroundColliderId=collider==null?0:collider.GetInstanceID(),
                    GroundPoint=(Vector3)groundPoint.GetValue(ground),GroundNormal=(Vector3)groundNormal.GetValue(ground),
                    GroundDistance=(float)groundDistance.GetValue(ground),GroundCurrent=(bool)groundCurrent.GetValue(ground),
                    SurfaceVelocity=(Vector3)surfaceVelocity.GetValue(surface),ApplyGravity=(bool)applyGravity.GetValue(controls),
                    LeftOverTime=(float)leftOverTime.GetValue(client),LastVelocity=(Vector3)lastVelocity.GetValue(client),
                    DashTimer=(float)dashTimer.GetValue(client),ImpactTimer=(float)impactTimer.GetValue(client)};
            }
            return result;
        }

        private void AddCall(TraceCall call,BodyState[] after)
        {
            observedCalls++;
            calls.Add(new Dictionary<string,object>{{"sequence",++sequence},{"segment",segment},
                {"method",call.Method},{"unityFrame",Time.frameCount},{"time",Time.time},{"fixedTime",Time.fixedTime},
                {"outputFrame",OutputFrame()},{"outputPhase",OutputPhase()},
                {"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},
                {"changed",!Same(call.Before,after)},
                {"before",Encode(call.Before)},{"after",Encode(after)}});
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
                    {"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},{"chefs",Encode(ReadAll())}});
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
                &&Exact(a.LocalVelocity,b.LocalVelocity)&&a.XzSpeed==b.XzSpeed
                &&a.GroundColliderId==b.GroundColliderId&&Exact(a.GroundPoint,b.GroundPoint)
                &&Exact(a.GroundNormal,b.GroundNormal)&&a.GroundDistance==b.GroundDistance
                &&a.GroundCurrent==b.GroundCurrent&&Exact(a.SurfaceVelocity,b.SurfaceVelocity)
                &&a.ApplyGravity==b.ApplyGravity&&a.LeftOverTime==b.LeftOverTime
                &&Exact(a.LastVelocity,b.LastVelocity)&&a.DashTimer==b.DashTimer&&a.ImpactTimer==b.ImpactTimer;
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
                {"xzSpeed",value.XzSpeed},{"groundColliderInstanceId",value.GroundColliderId},
                {"groundPoint",Point(value.GroundPoint)},{"groundNormal",Point(value.GroundNormal)},
                {"groundDistance",value.GroundDistance},{"groundCurrent",value.GroundCurrent},
                {"surfaceVelocity",Point(value.SurfaceVelocity)},{"applyGravity",value.ApplyGravity},
                {"leftOverTime",value.LeftOverTime},{"lastVelocity",Point(value.LastVelocity)},
                {"dashTimer",value.DashTimer},{"impactTimer",value.ImpactTimer},
                {"bodyPositionBits",PointBits(value.BodyPosition)},
                {"transformPositionBits",PointBits(value.TransformPosition)},{"localPositionBits",PointBits(value.LocalPosition)},
                {"previousPositionBits",PointBits(value.PreviousPosition)},{"localVelocityBits",PointBits(value.LocalVelocity)},
                {"xzSpeedBits",Bits(value.XzSpeed)},{"groundPointBits",PointBits(value.GroundPoint)},
                {"groundNormalBits",PointBits(value.GroundNormal)},{"groundDistanceBits",Bits(value.GroundDistance)},
                {"surfaceVelocityBits",PointBits(value.SurfaceVelocity)},{"leftOverTimeBits",Bits(value.LeftOverTime)},
                {"lastVelocityBits",PointBits(value.LastVelocity)},{"dashTimerBits",Bits(value.DashTimer)},
                {"impactTimerBits",Bits(value.ImpactTimer)},
                {"bodyYBits",Bits(value.BodyPosition.y)},{"transformYBits",Bits(value.TransformPosition.y)},
                {"localYBits",Bits(value.LocalPosition.y)},{"kinematic",value.Kinematic},{"gravity",value.Gravity},
                {"sleeping",value.Sleeping}}).ToArray();
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
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",1},{"installed",ReferenceEquals(installed,this)},
                {"active",ReferenceEquals(active,this)},
                {"segment",segment},{"chefEntityIds",entityIds.Values.OrderBy(x=>x).ToArray()},
                {"patchedMethods",patched.ToArray()},{"observedCalls",observedCalls},{"discardedCalls",discardedCalls},
                {"discardedPhases",discardedPhases},{"failure",failure},{"current",Encode(ReadAll())},
                {"calls",calls.ToArray()},{"phases",phases.ToArray()},
                {"scope","Read-only local-chef public Rigidbody/Transform, GroundCast, surface and gravity-call state around ordinary advancing movement methods, interleaved with FixedUpdate/Update/LateUpdate samples. No authoring warp, checkpoint, synchronizer or attachment method is patched; no Unity setter, physics step, input, clock or gameplay-state write."}};
        }

        private void Suspend(Dictionary<string,object> args)
        {
            RequireNone(args);
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Chef mutation tracer suspension requires the authoring pause fence.");
            if(harmony==null||!ReferenceEquals(installed,this))
                throw new InvalidOperationException("Chef mutation tracer is not installed.");
            if(active!=null&&!ReferenceEquals(active,this))
                throw new InvalidOperationException("Another chef mutation tracer is active.");
            if(ReferenceEquals(active,this)){AddPhase("suspended");active=null;}
        }

        private void Resume(Dictionary<string,object> args)
        {
            RequireNone(args);
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main)||!NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Chef mutation tracer resume requires the authoring pause fence.");
            if(harmony==null||!ReferenceEquals(installed,this))
                throw new InvalidOperationException("Chef mutation tracer is not installed.");
            if(active!=null&&!ReferenceEquals(active,this))
                throw new InvalidOperationException("Another chef mutation tracer is active.");
            active=this;AddPhase("resumed");
        }

        private static void RequireNone(Dictionary<string,object> args){if(args!=null&&args.Count!=0)throw new ArgumentException("Operation takes no arguments.");}
        private static object Point(Vector3 value){return new[]{value.x,value.y,value.z};}
        private static object PointBits(Vector3 value){return new[]{Bits(value.x),Bits(value.y),Bits(value.z)};}
        private static object Rotation(Quaternion value){return new[]{value.x,value.y,value.z,value.w};}
        private static int Bits(float value){return BitConverter.ToInt32(BitConverter.GetBytes(value),0);}
        private static int OutputFrame(){return Hpmv.Injector.Server==null?-1:Hpmv.Injector.Server.CurrentFrameData.FrameNumber;}
        private static int OutputPhase(){return Hpmv.Injector.Server==null?-1:Hpmv.Injector.Server.CurrentFrameData.FramesSinceLastNoPhysicsFrame;}

        private void Deactivate()
        {
            if(harmony!=null)harmony.UnpatchSelf();harmony=null;
            if(driverObject!=null)UnityEngine.Object.Destroy(driverObject);driverObject=null;driver=null;
            if(ReferenceEquals(active,this))active=null;
            if(ReferenceEquals(installed,this))installed=null;
        }

        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
