using System;
using System.Collections;
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
    // Same-process negative control for the Story 1-1 chef/floor contact.
    // The probe shares the game's Unity/PhysX world and TimeManager lifecycle,
    // but every probe/game collider pair is ignored. Only the probe capsule and
    // its private floor can contact each other.
    public sealed class InProcessPhysicsReproModule : IAuthoringModule
    {
        private sealed class ProbeDriver : MonoBehaviour
        {
            private void LateUpdate()
            {
                var module = active;
                if (module == null || module.driver != this) return;
                module.IgnoreNewGameColliders();
                module.AddSample("late-update");
            }
        }

        private sealed class PreFreeze
        {
            internal Vector3 Position, Velocity, AngularVelocity;
            internal Quaternion Rotation;
            internal bool Kinematic, Gravity;
        }

        private sealed class Saved
        {
            internal object Native;
            internal int Frame;
            internal Vector3 BodyPosition, LocalPosition, Velocity, AngularVelocity;
            internal Quaternion BodyRotation, LocalRotation;
            internal bool ResumeKinematic, ResumeGravity;
        }

        private static InProcessPhysicsReproModule active;
        private Harmony harmony;
        private GameObject root, actor;
        private Rigidbody body;
        private CapsuleCollider capsule;
        private BoxCollider floor;
        private ProbeDriver driver;
        private readonly HashSet<int> ignoredColliderIds = new HashSet<int>();
        private readonly Dictionary<object, Saved> saved = new Dictionary<object, Saved>();
        private readonly List<object> samples = new List<object>();
        private readonly List<object> events = new List<object>();
        private FieldInfo frozenBody, history, planSnapshot;
        private PreFreeze latestPreFreeze;
        private bool disposed, needsInitialUnfreeze;
        private long sequence, captures, restores, freezeCalls, unfreezeCalls;
        private int discardedSamples, discardedEvents;
        private string segment = "setup", failure;

        public string Name { get { return "in-process-unity-physics-rewind-control-v1"; } }
        public int ApiVersion { get { return 1; } }

        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("InProcessPhysicsReproModule");
            if (operation == "activate") Activate(args);
            else if (operation == "mark") Mark(args);
            else if (operation == "clear") Clear(args);
            else if (operation == "deactivate") Deactivate();
            else if (operation != "status") throw new ArgumentException("Use activate, mark, clear, status or deactivate.");
            else RequireNoArguments(args);
            return Status();
        }

        private void Activate(Dictionary<string, object> args)
        {
            RequireNoArguments(args);
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main) || !NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("In-process physics repro activation requires the authoring pause fence.");
            if (harmony != null) return;
            if (active != null) throw new InvalidOperationException("Another in-process physics repro is active.");

            var frozen = typeof(TimeManager).GetNestedType("FrozenPhysicsData", BindingFlags.NonPublic);
            var constructor = frozen == null ? null : AccessTools.Constructor(frozen, new[] {typeof(Rigidbody), typeof(int)});
            var unfreeze = frozen == null ? null : AccessTools.DeclaredMethod(frozen, "Unfreeze", Type.EmptyTypes);
            var capture = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint), "CaptureFrame", new[] {typeof(int)});
            var complete = AccessTools.DeclaredMethod(typeof(NativeKitchenCheckpoint.RestorePlan), "Complete", Type.EmptyTypes);
            var setPaused = AccessTools.DeclaredMethod(typeof(TimeManager), "SetPaused",
                new[] {typeof(TimeManager.PauseLayer), typeof(bool), typeof(object)});
            frozenBody = frozen == null ? null : frozen.GetField("m_frozenBody", BindingFlags.Instance | BindingFlags.NonPublic);
            history = typeof(NativeKitchenCheckpoint).GetField("history", BindingFlags.Static | BindingFlags.NonPublic);
            planSnapshot = typeof(NativeKitchenCheckpoint.RestorePlan).GetField("snapshot", BindingFlags.Instance | BindingFlags.NonPublic);
            if (constructor == null || unfreeze == null || capture == null || complete == null || setPaused == null
                || frozenBody == null || frozenBody.FieldType != typeof(Rigidbody)
                || history == null || !typeof(IDictionary).IsAssignableFrom(history.FieldType) || planSnapshot == null)
                throw new InvalidOperationException("Frozen-X pause/checkpoint contract differs.");

            active = this;
            try
            {
                CreateProbe();
                harmony = new Harmony("supercharged.authoring.inprocess-physics-repro." + GetType().Assembly.GetName().Name);
                harmony.Patch(constructor, prefix: Hook("BeforeFreeze"), postfix: Hook("AfterFreeze"));
                harmony.Patch(unfreeze, prefix: Hook("BeforeUnfreeze"), postfix: Hook("AfterUnfreeze"));
                harmony.Patch(capture, postfix: Hook("AfterCapture"));
                harmony.Patch(complete, postfix: Hook("AfterComplete"));
                harmony.Patch(setPaused, postfix: Hook("AfterSetPaused"));
                AddEvent("activated", null);
                AddSample("activated");
            }
            catch
            {
                Deactivate();
                throw;
            }
        }

        private HarmonyMethod Hook(string name)
        {
            return new HarmonyMethod(GetType().GetMethod(name, BindingFlags.Public | BindingFlags.Static));
        }

        private void CreateProbe()
        {
            var chefBodies = UnityEngine.Object.FindObjectsOfType<Rigidbody>()
                .Where(IsLocalChef).OrderBy(value => value.GetInstanceID()).ToArray();
            if (chefBodies.Length != 4) throw new InvalidOperationException("Probe requires exactly four live local chefs.");
            var sourceBody = chefBodies[0];
            var sourceCapsule = sourceBody.GetComponent<CapsuleCollider>();
            if (sourceCapsule == null) throw new InvalidOperationException("Local chef lacks its CapsuleCollider.");
            var existing = UnityEngine.Object.FindObjectsOfType<Collider>();

            root = new GameObject("__TAS_InProcessPhysicsRepro");
            root.transform.position = new Vector3(1000f, 0f, 1000f);
            root.layer = sourceBody.gameObject.layer;

            var floorObject = new GameObject("ProbeFloor");
            floorObject.layer = root.layer;
            floorObject.transform.SetParent(root.transform, false);
            floor = floorObject.AddComponent<BoxCollider>();
            floor.center = new Vector3(0f, -0.2f, 0f);
            floor.size = new Vector3(16.7f, 0.5f, 12.6f);
            floor.isTrigger = false;

            var parent = new GameObject("ProbeChefParent");
            parent.layer = root.layer;
            parent.transform.SetParent(root.transform, false);
            parent.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            actor = new GameObject("ProbeChef");
            actor.layer = root.layer;
            actor.transform.SetParent(parent.transform, false);
            actor.transform.localPosition = new Vector3(0f, -0.75f, 0f);
            actor.transform.localRotation = Quaternion.identity;

            body = actor.AddComponent<Rigidbody>();
            body.mass = sourceBody.mass;
            body.drag = sourceBody.drag;
            body.angularDrag = sourceBody.angularDrag;
            body.useGravity = false;
            body.constraints = sourceBody.constraints;
            body.interpolation = sourceBody.interpolation;
            body.collisionDetectionMode = sourceBody.collisionDetectionMode;
            body.detectCollisions = sourceBody.detectCollisions;
            body.sleepThreshold = sourceBody.sleepThreshold;
            body.maxAngularVelocity = sourceBody.maxAngularVelocity;
            body.solverIterations = sourceBody.solverIterations;
            body.solverVelocityIterations = sourceBody.solverVelocityIterations;

            capsule = actor.AddComponent<CapsuleCollider>();
            capsule.center = sourceCapsule.center;
            capsule.radius = sourceCapsule.radius;
            capsule.height = sourceCapsule.height;
            capsule.direction = sourceCapsule.direction;
            capsule.contactOffset = sourceCapsule.contactOffset;
            capsule.sharedMaterial = sourceCapsule.sharedMaterial;
            capsule.isTrigger = false;

            // The module is loaded while TimeManager is already paused, so this
            // new body is not in the existing FrozenPhysicsData list. Keep it
            // inert until the first real Main-layer unpause, then enter the
            // ordinary dynamic lifecycle.
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
            body.useGravity = false;
            needsInitialUnfreeze = true;

            foreach (var other in existing) Ignore(other);
            Physics.IgnoreCollision(capsule, floor, false);
            driver = root.AddComponent<ProbeDriver>();
        }

        private static bool IsLocalChef(Rigidbody value)
        {
            return value != null && value.GetComponent<ServerChefSynchroniser>() != null
                && value.GetComponent<ClientOnTheServerChefSynchroniser>() != null;
        }

        private bool IsProbe(Rigidbody value)
        {
            return value != null && body != null && ReferenceEquals(value, body) && value.GetInstanceID() == body.GetInstanceID();
        }

        private void IgnoreNewGameColliders()
        {
            if (capsule == null || floor == null) return;
            foreach (var other in UnityEngine.Object.FindObjectsOfType<Collider>()) Ignore(other);
        }

        private void Ignore(Collider other)
        {
            if (other == null || other == capsule || other == floor) return;
            int id = other.GetInstanceID();
            if (!ignoredColliderIds.Add(id)) return;
            Physics.IgnoreCollision(capsule, other, true);
            Physics.IgnoreCollision(floor, other, true);
        }

        public static void BeforeFreeze(Rigidbody __0, out bool __state)
        {
            __state = false;
            var module = active;
            if (module == null || !module.IsProbe(__0)) return;
            __state = true;
            module.latestPreFreeze = new PreFreeze {
                Position=__0.position,Rotation=__0.rotation,Velocity=__0.velocity,
                AngularVelocity=__0.angularVelocity,Kinematic=__0.isKinematic,Gravity=__0.useGravity
            };
            module.freezeCalls++;
            module.AddEvent("freeze-before", null);
        }

        public static void AfterFreeze(Rigidbody __0, bool __state)
        {
            var module = active;
            if (module == null || !__state || !module.IsProbe(__0)) return;
            module.needsInitialUnfreeze = false;
            module.AddEvent("freeze-after", null);
            module.AddSample("freeze-after");
        }

        public static void BeforeUnfreeze(object __instance, out Rigidbody __state)
        {
            __state = null;
            var module = active;
            if (module == null || __instance == null) return;
            var candidate = (Rigidbody)module.frozenBody.GetValue(__instance);
            if (!module.IsProbe(candidate)) return;
            __state = candidate;
            module.unfreezeCalls++;
            module.AddEvent("unfreeze-before", null);
        }

        public static void AfterUnfreeze(Rigidbody __state)
        {
            var module = active;
            if (module == null || !module.IsProbe(__state)) return;
            module.AddEvent("unfreeze-after", null);
            module.AddSample("unfreeze-after");
        }

        public static void AfterSetPaused(TimeManager.PauseLayer __0, bool __1)
        {
            var module = active;
            if (module == null || __0 != TimeManager.PauseLayer.Main || __1
                || TimeManager.IsPaused(TimeManager.PauseLayer.Main) || !module.needsInitialUnfreeze || module.body == null) return;
            try
            {
                module.body.velocity = Vector3.zero;
                module.body.angularVelocity = Vector3.zero;
                module.body.isKinematic = false;
                module.body.useGravity = false;
                module.needsInitialUnfreeze = false;
                module.AddEvent("initial-unfreeze", null);
                module.AddSample("initial-unfreeze");
            }
            catch (Exception error) { module.failure = "Initial unfreeze: " + error.Message; throw; }
        }

        public static void AfterCapture(int __0)
        {
            var module = active;
            if (module == null || module.body == null) return;
            try { module.Capture(__0); }
            catch (Exception error) { module.failure = "Capture: " + error.Message; }
        }

        private void Capture(int frame)
        {
            object native = ((IDictionary)history.GetValue(null))[frame];
            if (native == null || saved.ContainsKey(native)) return;
            bool resumeKinematic = latestPreFreeze == null ? false : latestPreFreeze.Kinematic;
            bool resumeGravity = latestPreFreeze == null ? false : latestPreFreeze.Gravity;
            Vector3 resumeVelocity = latestPreFreeze == null ? Vector3.zero : latestPreFreeze.Velocity;
            Vector3 resumeAngular = latestPreFreeze == null ? Vector3.zero : latestPreFreeze.AngularVelocity;
            var value = new Saved {
                Native=native,Frame=frame,BodyPosition=body.position,BodyRotation=body.rotation,
                LocalPosition=actor.transform.localPosition,LocalRotation=actor.transform.localRotation,
                Velocity=resumeVelocity,AngularVelocity=resumeAngular,
                ResumeKinematic=resumeKinematic,ResumeGravity=resumeGravity
            };
            saved.Add(native, value);
            captures++;
            if (saved.Count > 20000) throw new InvalidOperationException("Probe checkpoint bound reached.");
        }

        public static void AfterComplete(NativeKitchenCheckpoint.RestorePlan __instance)
        {
            var module = active;
            if (module == null) return;
            try { module.Restore(__instance); }
            catch (Exception error) { module.failure = "Restore: " + error.Message; throw; }
        }

        private void Restore(NativeKitchenCheckpoint.RestorePlan plan)
        {
            object native = planSnapshot.GetValue(plan);
            Saved target;
            if (native == null || !saved.TryGetValue(native, out target))
                throw new InvalidOperationException("Completed native restore lacks its probe checkpoint.");
            if (body == null || actor == null) throw new InvalidOperationException("Probe incarnation was destroyed.");
            AddEvent("restore-before", target.Frame);
            body.isKinematic = target.ResumeKinematic;
            body.useGravity = target.ResumeGravity;
            actor.transform.localPosition = target.LocalPosition;
            actor.transform.localRotation = target.LocalRotation;
            body.position = target.BodyPosition;
            body.rotation = target.BodyRotation;
            body.velocity = target.Velocity;
            body.angularVelocity = target.AngularVelocity;
            if (body.position != target.BodyPosition || body.rotation != target.BodyRotation
                || actor.transform.localPosition != target.LocalPosition || actor.transform.localRotation != target.LocalRotation
                || body.velocity != target.Velocity || body.angularVelocity != target.AngularVelocity
                || body.isKinematic != target.ResumeKinematic || body.useGravity != target.ResumeGravity)
                throw new InvalidOperationException("Probe public restore readback differs.");
            latestPreFreeze = new PreFreeze {Position=target.BodyPosition,Rotation=target.BodyRotation,
                Velocity=target.Velocity,AngularVelocity=target.AngularVelocity,
                Kinematic=target.ResumeKinematic,Gravity=target.ResumeGravity};
            restores++;
            AddEvent("restore-after", target.Frame);
            AddSample("restore-after");
            foreach (var key in saved.Where(item => item.Value.Frame > target.Frame).Select(item => item.Key).ToArray()) saved.Remove(key);
        }

        private void Mark(Dictionary<string, object> args)
        {
            if (args == null || args.Count != 1 || !args.ContainsKey("label"))
                throw new ArgumentException("mark requires only a bounded label.");
            var value = args["label"] as string;
            if (String.IsNullOrEmpty(value) || value.Length > 32 || value.Any(c => !Char.IsLetterOrDigit(c) && c != '-' && c != '_'))
                throw new ArgumentException("mark label must contain only letters, digits, hyphens or underscores and be at most32 characters.");
            segment = value;
            AddEvent("mark", value);
        }

        private void Clear(Dictionary<string, object> args)
        {
            RequireNoArguments(args);
            samples.Clear(); events.Clear(); discardedSamples=discardedEvents=0; sequence=0; failure=null;
            AddEvent("cleared", null);
            AddSample("cleared");
        }

        private static void RequireNoArguments(Dictionary<string, object> args)
        {
            if (args != null && args.Count != 0) throw new ArgumentException("Operation takes no arguments.");
        }

        private void AddEvent(string stage, object detail)
        {
            var row = new Dictionary<string, object> {
                {"sequence",++sequence},{"stage",stage},{"segment",segment},
                {"unityFrame",Time.frameCount},{"fixedTime",Time.fixedTime},{"detail",detail}
            };
            if (body != null) row["bodyYBits"] = Bits(body.position.y);
            events.Add(row);
            if (events.Count > 512) { events.RemoveAt(0); discardedEvents++; }
        }

        private void AddSample(string stage)
        {
            if (body == null || actor == null) return;
            var p=body.position;var t=actor.transform.position;var l=actor.transform.localPosition;
            var v=body.velocity;var a=body.angularVelocity;
            samples.Add(new Dictionary<string, object> {
                {"sequence",++sequence},{"stage",stage},{"segment",segment},
                {"unityFrame",Time.frameCount},{"time",Time.time},{"fixedTime",Time.fixedTime},
                {"paused",TimeManager.IsPaused(TimeManager.PauseLayer.Main)},
                {"position",Point(p)},{"transformPosition",Point(t)},{"localPosition",Point(l)},
                {"velocity",Point(v)},{"angularVelocity",Point(a)},
                {"bodyYBits",Bits(p.y)},{"transformYBits",Bits(t.y)},{"localYBits",Bits(l.y)},
                {"kinematic",body.isKinematic},{"gravity",body.useGravity},{"sleeping",body.IsSleeping()}
            });
            if (samples.Count > 1024) { samples.RemoveAt(0); discardedSamples++; }
        }

        private object Status()
        {
            return new Dictionary<string, object> {
                {"name",Name},{"apiVersion",1},{"active",ReferenceEquals(active,this)},
                {"probeAlive",root!=null && actor!=null && body!=null && capsule!=null && floor!=null},
                {"rootInstanceId",root==null?0:root.GetInstanceID()},{"bodyInstanceId",body==null?0:body.GetInstanceID()},
                {"floorInstanceId",floor==null?0:floor.GetInstanceID()},{"segment",segment},
                {"captures",captures},{"restores",restores},{"savedSnapshots",saved.Count},
                {"freezeCalls",freezeCalls},{"unfreezeCalls",unfreezeCalls},
                {"ignoredGameColliders",ignoredColliderIds.Count},{"needsInitialUnfreeze",needsInitialUnfreeze},
                {"failure",failure},{"discardedSamples",discardedSamples},{"discardedEvents",discardedEvents},
                {"current",body==null?null:Current()},{"samples",samples.ToArray()},{"events",events.ToArray()},
                {"physics",new Dictionary<string,object> {
                    {"gravity",Point(Physics.gravity)},{"defaultContactOffset",Physics.defaultContactOffset},
                    {"defaultSolverIterations",Physics.defaultSolverIterations},
                    {"defaultSolverVelocityIterations",Physics.defaultSolverVelocityIterations},
                    {"autoSimulation",Physics.autoSimulation},{"queriesHitTriggers",Physics.queriesHitTriggers}
                }},
                {"scope","Invisible capsule/floor negative control in the original game's single global Unity 2017 PhysX world. Probe/game collider pairs are ignored; no registered entity, renderer, gameplay component, score, input, clock, synchronizer or game Rigidbody is written."}
            };
        }

        private object Current()
        {
            var p=body.position;var t=actor.transform.position;var l=actor.transform.localPosition;
            return new Dictionary<string,object> {
                {"position",Point(p)},{"transformPosition",Point(t)},{"localPosition",Point(l)},
                {"bodyYBits",Bits(p.y)},{"transformYBits",Bits(t.y)},{"localYBits",Bits(l.y)},
                {"kinematic",body.isKinematic},{"gravity",body.useGravity},{"sleeping",body.IsSleeping()},
                {"capsuleCenter",Point(capsule.center)},{"capsuleRadius",capsule.radius},{"capsuleHeight",capsule.height},
                {"capsuleContactOffset",capsule.contactOffset},{"floorCenter",Point(floor.center)},{"floorSize",Point(floor.size)}
            };
        }

        private static object Point(Vector3 value) { return new[] {value.x,value.y,value.z}; }
        private static int Bits(float value) { return BitConverter.ToInt32(BitConverter.GetBytes(value),0); }

        private void Deactivate()
        {
            if (harmony != null) harmony.UnpatchSelf();
            harmony = null;
            if (root != null) UnityEngine.Object.Destroy(root);
            root=null;actor=null;body=null;capsule=null;floor=null;driver=null;
            ignoredColliderIds.Clear();saved.Clear();latestPreFreeze=null;needsInitialUnfreeze=false;
            if (ReferenceEquals(active,this)) active=null;
        }

        public void Dispose()
        {
            if (disposed) return;
            Deactivate();
            disposed=true;
        }
    }
}
