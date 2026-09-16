using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using SuperchargedPatch.Authoring;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Read-only phase tracer for the fixed Story 1-1 plate physics container.
    // It observes Transform before and after the Rigidbody getters so the trace
    // itself can prove whether a getter changes the exposed Transform pose.
    public sealed class PhysicsCaptureTraceModule : IAuthoringModule
    {
        private static PhysicsCaptureTraceModule active;
        private Harmony harmony;
        private readonly List<object> receipts=new List<object>();
        private bool disposed;
        private bool armed,handled,warpHandled,traceBridgeCapture;
        private int skippedBridgeCaptures,skippedBridgeHandles;
        private long samples;
        private string failure;

        public string Name { get { return "entity47-physics-capture-phase-trace-v5-post-warp-request"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation,Dictionary<string,object> args)
        {
            if(disposed)throw new ObjectDisposedException("PhysicsCaptureTraceModule");
            if(args!=null&&args.Count!=0)throw new ArgumentException("Physics-capture-trace operations take no arguments.");
            if(operation=="activate")Activate();
            else if(operation=="deactivate")Deactivate();
            else if(operation=="clear"){receipts.Clear();armed=false;handled=false;warpHandled=false;traceBridgeCapture=false;skippedBridgeCaptures=0;skippedBridgeHandles=0;}
            else if(operation=="arm")
            {
                receipts.Clear();armed=true;handled=false;warpHandled=false;traceBridgeCapture=false;skippedBridgeHandles=0;
                // The arm operation's own response performs one bridge Capture.
                // Ignore it; trace the next request from the outer LateUpdate entry.
                skippedBridgeCaptures=1;
            }
            else if(operation=="arm-warp")
            {
                receipts.Clear();armed=true;handled=false;warpHandled=false;traceBridgeCapture=false;
                // Ignore this hot-call response and the bridge arm request that
                // must precede the controller's warp directive.
                skippedBridgeCaptures=2;skippedBridgeHandles=1;
            }
            else if(operation!="status")throw new ArgumentException("Use activate, deactivate, clear, arm, arm-warp or status.");
            return new Dictionary<string,object>{{"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"armed",armed},{"handled",handled},{"warpHandled",warpHandled},{"samples",samples},{"failure",failure},{"receipts",receipts.ToArray()},
                {"scope","Explicitly armed, read-only entity47 direct Rigidbody/Transform values across TASPatcher LateUpdate, bridge queue dispatch/handler/capture, controller collection and native-physics observation boundaries. Each sample reads Transform, Rigidbody, then Transform again."}};
        }

        private void Activate()
        {
            if(!TimeManager.IsPaused(TimeManager.PauseLayer.Main))
                throw new InvalidOperationException("Physics-capture trace activation requires native pause.");
            if(harmony!=null)return;
            if(active!=null)throw new InvalidOperationException("Another physics-capture trace is active.");
            var bridge=AccessTools.TypeByName("SuperchargedPatch.Bridge.NativeSessionBridge");
            var bridgeCapture=bridge==null?null:AccessTools.DeclaredMethod(bridge,"Capture",Type.EmptyTypes);
            var bridgeLateUpdate=bridge==null?null:AccessTools.DeclaredMethod(bridge,"LateUpdate",Type.EmptyTypes);
            var bridgeHandle=bridge==null?null:AccessTools.DeclaredMethod(bridge,"Handle");
            var tasPatcher=AccessTools.TypeByName("SuperchargedPatch.TASPatcher");
            var tasLateUpdate=tasPatcher==null?null:AccessTools.DeclaredMethod(tasPatcher,"LateUpdate",Type.EmptyTypes);
            var physicsCapture=AccessTools.DeclaredMethod(typeof(NativePhysicsObservation),"CaptureRegistry",Type.EmptyTypes);
            var collect=AccessTools.DeclaredMethod(typeof(ActiveStateCollector),"CollectDataForFrame",new[]{typeof(Hpmv.OutputData)});
            var controller=AccessTools.DeclaredMethod(typeof(ControllerHandler),"LateUpdate",Type.EmptyTypes);
            var warp=AccessTools.DeclaredMethod(typeof(WarpHandler),"HandleWarpRequestIfAny",Type.EmptyTypes);
            if(bridgeCapture==null||bridgeLateUpdate==null||bridgeHandle==null||tasLateUpdate==null
                ||physicsCapture==null||collect==null||controller==null||warp==null)
                throw new InvalidOperationException("Installed capture method contract differs.");
            harmony=new Harmony("supercharged.authoring.physics-capture-trace."+GetType().Assembly.GetName().Name);
            active=this;
            try
            {
                harmony.Patch(tasLateUpdate,prefix:Hook("BeforeTasLateUpdate"),postfix:Hook("AfterTasLateUpdate"));
                harmony.Patch(bridgeLateUpdate,prefix:Hook("BeforeBridgeLateUpdate"),postfix:Hook("AfterBridgeLateUpdate"));
                harmony.Patch(bridgeHandle,prefix:Hook("BeforeBridgeHandle"),postfix:Hook("AfterBridgeHandle"));
                harmony.Patch(bridgeCapture,prefix:Hook("BeforeBridgeCapture"),postfix:Hook("AfterBridgeCapture"));
                harmony.Patch(physicsCapture,prefix:Hook("BeforePhysicsCapture"),postfix:Hook("AfterPhysicsCapture"));
                harmony.Patch(collect,prefix:Hook("BeforeCollection"),postfix:Hook("AfterCollection"));
                harmony.Patch(controller,postfix:Hook("AfterControllerLateUpdate"));
                harmony.Patch(warp,prefix:Hook("BeforeWarp"),postfix:Hook("AfterWarp"));
            }
            catch{Deactivate();throw;}
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name,BindingFlags.Public|BindingFlags.Static));
        }

        public static void BeforeTasLateUpdate(out bool __state){__state=IsArmed();if(__state)Sample("before-tas-late-update");}
        public static void AfterTasLateUpdate(bool __state)
        {
            var module=active;if(module==null||!__state)return;
            Sample("after-tas-late-update");
            // A controller warp has no bridge Handle call in its execution frame.
            // Keep tracing across that frame boundary and stop only after the first
            // subsequent bridge request has been handled.
            if(module.handled)module.armed=false;
        }
        public static void BeforeBridgeLateUpdate(out bool __state){__state=IsArmed();if(__state)Sample("before-bridge-late-update");}
        public static void AfterBridgeLateUpdate(bool __state){if(__state)Sample("after-bridge-late-update");}
        public static void BeforeBridgeHandle(out bool __state)
        {
            var module=active;__state=false;if(module==null||!module.armed)return;
            if(module.skippedBridgeHandles>0){module.skippedBridgeHandles--;return;}
            __state=true;Sample("before-bridge-handle");
        }
        public static void AfterBridgeHandle(bool __state)
        {
            if(!__state)return;Sample("after-bridge-handle");var module=active;if(module!=null)module.handled=true;
        }
        public static void BeforeBridgeCapture(out bool __state)
        {
            var module=active;__state=false;if(module==null||!module.armed)return;
            if(module.skippedBridgeCaptures>0){module.skippedBridgeCaptures--;module.traceBridgeCapture=false;return;}
            module.traceBridgeCapture=true;__state=true;Sample("before-bridge-capture");
        }
        public static void AfterBridgeCapture(bool __state)
        {
            if(!__state)return;Sample("after-bridge-capture");var module=active;if(module!=null)module.traceBridgeCapture=false;
        }
        public static void BeforePhysicsCapture(){if(IsTracingBridgeCapture())Sample("before-native-physics-capture");}
        public static void AfterPhysicsCapture(){if(IsTracingBridgeCapture())Sample("after-native-physics-capture");}
        public static void BeforeCollection(){if(IsArmed())Sample("before-active-state-collection");}
        public static void AfterCollection(){if(IsArmed())Sample("after-active-state-collection");}
        public static void AfterControllerLateUpdate(){if(IsArmed())Sample("after-controller-late-update");}
        public static void BeforeWarp(out bool __state)
        {
            __state=IsArmed()&&Hpmv.Injector.Server.CurrentInput!=null&&Hpmv.Injector.Server.CurrentInput.Warp!=null;
            if(__state)Sample("before-warp-handler");
        }
        public static void AfterWarp(bool __state)
        {
            if(!__state)return;Sample("after-warp-handler");var module=active;if(module!=null)module.warpHandled=true;
        }

        private static bool IsArmed(){var module=active;return module!=null&&module.armed;}
        private static bool IsTracingBridgeCapture(){var module=active;return module!=null&&module.traceBridgeCapture;}

        private static void Sample(string phase)
        {
            var module=active;if(module==null)return;
            try
            {
                GameObject obj=null;var entries=EntitySerialisationRegistry.m_EntitiesList;
                for(int i=0;i<entries.Count;i++)
                {
                    var entry=entries._items[i];
                    if(entry!=null&&entry.m_GameObject!=null&&(int)entry.m_Header.m_uEntityID==47)
                    {if(obj!=null)throw new InvalidOperationException("Entity47 is duplicated.");obj=entry.m_GameObject;}
                }
                if(obj==null)return;
                var body=obj.GetComponent<Rigidbody>();var transform=obj.transform;
                if(body==null||transform==null)throw new InvalidOperationException("Entity47 lacks its direct body or Transform.");
                Vector3 transformBefore=transform.localPosition;
                Vector3 bodyPosition=body.position;
                Quaternion bodyRotation=body.rotation;
                Vector3 center=body.centerOfMass;
                Vector3 worldCenter=body.worldCenterOfMass;
                Vector3 inertia=body.inertiaTensor;
                Quaternion inertiaRotation=body.inertiaTensorRotation;
                Vector3 transformAfter=transform.localPosition;
                module.samples++;
                var receipt=new Dictionary<string,object>{{"sample",module.samples},{"phase",phase},
                    {"unityFrame",Time.frameCount},{"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},
                    {"bodyInstanceId",body.GetInstanceID()},{"transformBefore",Point(transformBefore)},
                    {"bodyPosition",Point(bodyPosition)},{"bodyRotation",Rotation(bodyRotation)},
                    {"centerOfMass",Point(center)},{"worldCenterOfMass",Point(worldCenter)},
                    {"inertiaTensor",Point(inertia)},{"inertiaTensorRotation",Rotation(inertiaRotation)},
                    {"transformAfter",Point(transformAfter)},{"getterChangedTransform",transformBefore!=transformAfter},
                    {"transformDiffersFromBody",transformBefore!=bodyPosition||transformAfter!=bodyPosition}};
                module.receipts.Add(receipt);
                if(module.receipts.Count>128)module.receipts.RemoveAt(0);
                module.failure=null;
            }
            catch(Exception error){module.failure=phase+": "+error.Message;}
        }

        private static object Point(Vector3 value){return new[]{value.x,value.y,value.z};}
        private static object Rotation(Quaternion value){return new[]{value.x,value.y,value.z,value.w};}

        private void Deactivate()
        {
            if(harmony!=null)harmony.UnpatchSelf();harmony=null;if(ReferenceEquals(active,this))active=null;
        }

        public void Dispose(){if(disposed)return;Deactivate();disposed=true;}
    }
}
