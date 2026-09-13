using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // Authoring support for one already-running local-chef projectile. Loading,
    // native sessions, unload/exit waits and Unity-owned coroutines stay excluded.
    public static class NativeCannonFlightCheckpoint
    {
        private static readonly Guid installedModule = new Guid("114d606a-29d5-472c-8ebe-c2c20fbe3be3");
        private static readonly string[] launchFields = { "_objectToLaunch", "<handler>__0", "_target", "<animation>__0", "<exit>__1", "$this", "$current", "$disposing", "$PC" };
        private static readonly string[] animationFields = { "<time>__0", "<length>__0", "<endTime>__0", "obj", "<startingRotation>__0", "target", "<targetPosition>__0", "<targetRotation>__0", "<startingPosition>__0", "<startingHeight>__0", "<maxHeight>__0", "<position>__1", "<newPosition>__1", "$this", "$current", "$disposing", "$PC" };
        private static readonly string[] controlFields = { "m_directlyUnderPlayerControl", "m_bRespawning", "m_bApplyGravity", "m_movementScale", "m_bServerControlled", "m_previousParent", "m_previousPosition", "m_localVelocity", "m_xzSpeed", "m_allowSwitchWhenDisabled" };
        public static string LastScopeReason = "";

        public sealed class Snapshot
        {
            internal ServerCannon Server;
            internal ClientCannon Client;
            internal GameObject Passenger;
            internal EntitySerialisationEntry PassengerEntry;
            internal PlayerControls Controls;
            internal Rigidbody Body;
            internal Collider Collider;
            internal DynamicLandscapeParenting Parenting;
            internal bool ControlsEnabled, ColliderEnabled, Kinematic, ParentingEnabled;
            internal Vector3 Position, Velocity, AngularVelocity;
            internal Quaternion Rotation;
            internal Transform Parent;
            internal NativeIteratorCopy.Snapshot Iterator;
            internal readonly List<Fields> States = new List<Fields>();
            internal readonly List<UnityEngine.Object> References = new List<UnityEngine.Object>();
            internal float Time, Duration;
            public object Diagnostics() { return new Dictionary<string, object> {
                { "scope", "single-local-chef-steady-projectile" }, { "passengerId", (int)PassengerEntry.m_Header.m_uEntityID },
                { "projectileTime", Time }, { "projectileDuration", Duration }, { "iteratorPhase", 1 },
                { "nativeContinuationUnverified", true } }; }
        }
        internal sealed class Fields
        {
            internal object Target;
            internal FieldInfo[] Members;
            internal object[] Values;
        }

        public static Snapshot TryCapture(ServerCannon server)
        {
            LastScopeReason = "";
            if (!server.IsFlying()) return null;
            try { return Capture(server); }
            catch (Exception error) { LastScopeReason = error.Message; return null; }
        }

        private static Snapshot Capture(ServerCannon server)
        {
            if (typeof(ClientCannon).Module.ModuleVersionId != installedModule)
                throw new InvalidOperationException("Steady-flight iterator schema is not pinned to this native module.");
            var obj = server.gameObject;
            var client = obj.GetComponent<ClientCannon>();
            var handler = obj.GetComponent<ClientCannonPlayerHandler>();
            var cannon = obj.GetComponent<Cannon>();
            var serverSession = obj.GetComponent<ServerCannonSessionInteractable>();
            var clientSession = obj.GetComponent<ClientCannonSessionInteractable>();
            if (client == null || handler == null || cannon == null || serverSession == null || clientSession == null
                || Read(typeof(ServerSessionInteractable), serverSession, "m_session") != null || clientSession.HasSession
                || Read(typeof(ServerCannon), server, "OnInteractionEnd") != null)
                throw new InvalidOperationException("Steady-flight restore excludes native session transitions.");
            var launches = (IList)Read(typeof(ClientCannon), client, "m_launches");
            Type launchType = typeof(ClientCannon).GetNestedType("<LaunchProjectile>c__Iterator1", BindingFlags.NonPublic);
            Type animationType = typeof(ProjectileAnimation).GetNestedType("<Run>c__Iterator0", BindingFlags.NonPublic);
            if (launches.Count != 1 || launches[0] == null || launches[0].GetType() != launchType)
                throw new InvalidOperationException("Steady-flight restore requires exactly one native projectile iterator.");
            object launch = launches[0];
            var animation = Read(launchType, launch, "<animation>__0") as IEnumerator;
            if (animation == null || animation.GetType() != animationType
                || (int)Read(launchType, launch, "$PC") != 1 || (int)Read(animationType, animation, "$PC") != 1
                || Read(launchType, launch, "<exit>__1") != null || Read(launchType, launch, "$current") != null
                || Read(animationType, animation, "$current") != null || (bool)Read(launchType, launch, "$disposing")
                || (bool)Read(animationType, animation, "$disposing"))
                throw new InvalidOperationException("Native projectile is loading, landing, yielding externally or disposing.");
            var passenger = Read(launchType, launch, "_objectToLaunch") as GameObject;
            var controls = passenger == null ? null : passenger.GetComponent<PlayerControls>();
            var body = passenger == null ? null : passenger.GetComponent<Rigidbody>();
            var collider = passenger == null ? null : passenger.GetComponent<Collider>();
            var parenting = passenger == null ? null : passenger.GetComponent<DynamicLandscapeParenting>();
            var sync = passenger == null ? null : passenger.GetComponent<ClientChefSynchroniser>();
            var serverSync = passenger == null ? null : passenger.GetComponent<ServerChefSynchroniser>();
            if (controls == null || body == null || collider == null || parenting == null || sync == null || serverSync == null
                || passenger.transform.parent != null || controls.enabled || collider.enabled || !body.isKinematic || parenting.enabled
                || !(bool)Read(typeof(ClientChefSynchroniser), sync, "m_bLocallyControlled")
                || !(bool)Read(typeof(ClientWorldObjectSynchroniser), sync, "m_bPaused")
                || Read(typeof(ClientWorldObjectSynchroniser), sync, "m_PendingResumeData") != null
                || !ReferenceEquals(Read(typeof(ClientCannonPlayerHandler), handler, "m_controls"), controls)
                || (bool)Read(typeof(ClientCannonPlayerHandler), handler, "m_inCannon")
                || Read(typeof(ServerPlayerControlsImpl_Default), passenger.GetComponent<ServerPlayerControlsImpl_Default>(), "m_lastInteracted") != null)
                throw new InvalidOperationException("Native passenger control/physics/sync state is outside steady local flight.");
            if (!ReferenceEquals(Read(launchType, launch, "$this"), client)
                || !ReferenceEquals(Read(launchType, launch, "<handler>__0"), handler)
                || !ReferenceEquals(Read(launchType, launch, "_target"), cannon.m_target)
                || !ReferenceEquals(Read(animationType, animation, "obj"), passenger)
                || !ReferenceEquals(Read(animationType, animation, "target"), cannon.m_target)
                || !ReferenceEquals(Read(animationType, animation, "$this"), cannon.m_animation))
                throw new InvalidOperationException("Native projectile iterator has inconsistent owner/target references.");
            float time = (float)Read(animationType, animation, "<time>__0");
            float duration = (float)Read(animationType, animation, "<endTime>__0");
            if (!Finite(time) || !Finite(duration) || time <= 0 || time >= duration || duration <= 0)
                throw new InvalidOperationException("Native projectile clock is outside its running interval.");
            var entry = EntitySerialisationRegistry.GetEntry(passenger);
            if (entry == null) throw new InvalidOperationException("Native flying passenger is unregistered.");
            var saved = new Snapshot { Server = server, Client = client, Passenger = passenger, PassengerEntry = entry,
                Controls = controls, Body = body, Collider = collider, Parenting = parenting, ControlsEnabled = controls.enabled,
                ColliderEnabled = collider.enabled, Kinematic = body.isKinematic, ParentingEnabled = parenting.enabled,
                Position = body.position, Rotation = body.rotation, Velocity = body.velocity, AngularVelocity = body.angularVelocity,
                Parent = passenger.transform.parent, Time = time, Duration = duration };
            var pins = new object[] { client, handler, passenger, cannon.m_target, cannon.m_animation };
            var copier = new NativeIteratorCopy(new Dictionary<Type, string[]> { { launchType, launchFields }, { animationType, animationFields } },
                value => pins.Any(pin => ReferenceEquals(pin, value)));
            saved.Iterator = copier.Capture((IEnumerator)launch);
            CaptureFields(saved, typeof(ClientCannonPlayerHandler), handler, null);
            CaptureFields(saved, typeof(ClientWorldObjectSynchroniser), sync, null);
            CaptureFields(saved, typeof(ClientChefSynchroniser), sync, null);
            CaptureFields(saved, typeof(ServerWorldObjectSynchroniser), serverSync, null);
            CaptureFields(saved, typeof(ServerChefSynchroniser), serverSync, null);
            CaptureFields(saved, typeof(PlayerControls), controls, controlFields);
            CaptureFields(saved, typeof(DynamicLandscapeParenting), parenting, null);
            var chefLerp = Read(typeof(ClientChefSynchroniser), sync, "m_ChefLerp") as ChefLerp;
            if (chefLerp != null) CaptureFields(saved, typeof(ChefLerp), chefLerp, null);
            // While m_bPaused, native RunCorrection never reads PositionRecorder
            // history. Local ApplyResumeData clears it before correction resumes;
            // that existing native continuation performs the clear exactly once.
            return saved;
        }

        private static object CopyValue(object value)
        {
            var chef = value as ChefPositionMessage;
            if (chef != null) { var copy = new ChefPositionMessage(); copy.Copy(chef); return copy; }
            var world = value as WorldObjectMessage;
            if (world != null) { var copy = new WorldObjectMessage(); copy.Copy(world); return copy; }
            return value;
        }
        private static void CaptureFields(Snapshot saved, Type type, object target, string[] names)
        {
            if (target == null) throw new InvalidOperationException("Missing native steady-flight state owner.");
            FieldInfo[] fields = names == null ? type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => !f.IsInitOnly).OrderBy(f => f.Name).ToArray() : names.Select(n => AccessTools.Field(type, n)).ToArray();
            if (fields.Any(f => f == null)) throw new InvalidOperationException("Native steady-flight field layout changed.");
            object[] values = fields.Select(f => CopyValue(f.GetValue(target))).ToArray();
            foreach (object value in values)
            {
                if (value is float && !Finite((float)value)) throw new InvalidOperationException("Nonfinite native flight state.");
                var unity = value as UnityEngine.Object;
                if (!ReferenceEquals(unity, null)) { if (unity == null) throw new InvalidOperationException("Destroyed native flight state reference."); saved.References.Add(unity); }
            }
            saved.States.Add(new Fields { Target = target, Members = fields, Values = values });
        }
        public static void Validate(Snapshot saved)
        {
            if (saved == null || saved.Server == null || saved.Client == null || saved.Passenger == null
                || !ReferenceEquals(EntitySerialisationRegistry.GetEntry(saved.Passenger), saved.PassengerEntry)
                || saved.Controls == null || saved.Body == null || saved.Collider == null || saved.Parenting == null
                || saved.References.Any(r => r == null))
                throw new InvalidOperationException("Native steady-flight checkpoint incarnation is no longer valid.");
        }
        public static void Restore(Snapshot saved)
        {
            Validate(saved);
            saved.Controls.enabled = saved.ControlsEnabled;
            saved.Collider.enabled = saved.ColliderEnabled;
            saved.Parenting.enabled = saved.ParentingEnabled;
            saved.Passenger.transform.SetParent(saved.Parent, true);
            saved.Body.isKinematic = saved.Kinematic;
            saved.Body.position = saved.Position; saved.Body.rotation = saved.Rotation;
            saved.Body.velocity = saved.Velocity; saved.Body.angularVelocity = saved.AngularVelocity;
            foreach (Fields state in saved.States)
                for (int i = 0; i < state.Members.Length; i++) state.Members[i].SetValue(state.Target, CopyValue(state.Values[i]));
            var launches = (IList)Read(typeof(ClientCannon), saved.Client, "m_launches");
            launches.Clear(); launches.Add(saved.Iterator.RestoreCopy());
        }
        public static bool Same(Snapshot a, Snapshot b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.Passenger != b.Passenger || a.Server != b.Server || a.Time != b.Time || a.Duration != b.Duration
                || !a.Position.Equals(b.Position) || !a.Rotation.Equals(b.Rotation) || !a.Velocity.Equals(b.Velocity)
                || !a.AngularVelocity.Equals(b.AngularVelocity) || a.Parent != b.Parent
                || a.ControlsEnabled != b.ControlsEnabled || a.ColliderEnabled != b.ColliderEnabled
                || a.Kinematic != b.Kinematic || a.ParentingEnabled != b.ParentingEnabled || a.States.Count != b.States.Count) return false;
            for (int s = 0; s < a.States.Count; s++)
            {
                if (!ReferenceEquals(a.States[s].Target, b.States[s].Target)) return false;
                for (int i = 0; i < a.States[s].Values.Length; i++)
                    if (!SameValue(a.States[s].Values[i], b.States[s].Values[i])) return false;
            }
            return a.Iterator.Matches(b.Iterator.RestoreCopy());
        }
        private static bool SameValue(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a.GetType() != b.GetType()) return false;
            var x = a as ChefPositionMessage; var y = b as ChefPositionMessage;
            if (x != null) return x.Velocity.Equals(y.Velocity) && x.NetworkTime == y.NetworkTime && x.ClientTimeStamp == y.ClientTimeStamp && SameValue(x.WorldObject, y.WorldObject);
            var u = a as WorldObjectMessage; var v = b as WorldObjectMessage;
            if (u != null) return u.LocalPosition.Equals(v.LocalPosition) && u.LocalRotation.Equals(v.LocalRotation)
                && u.HasParent == v.HasParent && u.HasPositions == v.HasPositions && u.ParentEntityID == v.ParentEntityID;
            return a.GetType().IsValueType || a is string ? a.Equals(b) : ReferenceEquals(a, b);
        }
        private static object Read(Type type, object target, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null || target == null) throw new InvalidOperationException("Missing native steady-flight field: " + type.Name + "." + name);
            return field.GetValue(target);
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    }
}
