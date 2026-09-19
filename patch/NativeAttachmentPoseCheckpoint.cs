using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch
{
    // An attached logical item has no active Rigidbody of its own. Its native
    // parent callback and its separately registered physics container do not
    // restore the item's observed local transform. Authoring only; never tick
    // interpolation, force a placement callback, or infer an attachment offset.
    public static class NativeAttachmentPoseCheckpoint
    {
        private static readonly FieldInfo ServerPredicted = Field(typeof(ServerPhysicalAttachment), "m_bIsClientSidePredicted", typeof(bool));
        private static readonly FieldInfo ClientPredicted = Field(typeof(ClientPhysicalAttachment), "m_bClientSidePredicted", typeof(bool));
        private static readonly FieldInfo ClientParent = Field(typeof(ClientPhysicalAttachment), "m_Parent", typeof(IParentable));
        private static readonly FieldInfo ConveyorRemaining = Field(typeof(ConveyorPrediction), "m_RemainingMove", typeof(float));
        private static readonly FieldInfo ConveyorCalculated = Field(typeof(ConveyorPrediction), "m_bCalculatedDistance", typeof(bool));

        public sealed class Snapshot
        {
            public int EntityId { get; internal set; }
            internal EntitySerialisationEntry Entry, ParentEntry;
            internal GameObject Object, ParentObject;
            internal PhysicalAttachment Physical;
            internal ServerPhysicalAttachment Server;
            internal ClientPhysicalAttachment Client;
            internal Rigidbody Container;
            internal Transform Transform, Parent;
            internal Vector3 LocalPosition, WorldPosition, LocalScale, WorldScale;
            internal Quaternion LocalRotation, WorldRotation;
            internal bool Attached;
            internal bool ServerPredictionMode, ClientPredictionMode, ConveyorDistanceCalculated;
            internal IParentable CachedClientParent;
            internal IClientSidePredicted Prediction;
            internal Transform PredictionTransform;
            internal float ConveyorRemainingMove;
            internal int PredictionQueueCount;
            internal string Unsupported;
        }

        public static Snapshot[] Capture(HashSet<int> selectedIds)
        {
            if (selectedIds == null) throw new ArgumentNullException("selectedIds");
            var rows = new List<Snapshot>();
            var ids = new HashSet<int>();
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries._items[i];
                int id = (int)entry.m_Header.m_uEntityID;
                if (!selectedIds.Contains(id)) continue;
                var obj = entry.m_GameObject;
                if (obj == null || !ids.Add(id)) throw new InvalidOperationException("Initial native attachment membership changed.");
                var physical = obj.GetComponent<PhysicalAttachment>();
                var server = obj.GetComponent<ServerPhysicalAttachment>();
                var client = obj.GetComponent<ClientPhysicalAttachment>();
                if (physical == null || server == null || client == null || physical.m_container == null)
                    throw new InvalidOperationException("Initial native physical attachment components missing: " + id);
                var transform = obj.transform;
                var parent = transform.parent;
                EntitySerialisationEntry parentEntry = null;
                for (var ancestor = parent; ancestor != null && parentEntry == null; ancestor = ancestor.parent)
                    parentEntry = EntitySerialisationRegistry.GetEntry(ancestor.gameObject);
                var row = new Snapshot {
                    EntityId = id, Entry = entry, Object = obj, Physical = physical, Server = server, Client = client,
                    Container = physical.m_container, Transform = transform, Parent = parent, ParentEntry = parentEntry,
                    ParentObject = parentEntry == null ? null : parentEntry.m_GameObject,
                    LocalPosition = transform.localPosition, WorldPosition = transform.position,
                    LocalRotation = transform.localRotation, WorldRotation = transform.rotation,
                    LocalScale = transform.localScale, WorldScale = transform.lossyScale, Attached = server.IsAttached(),
                    ServerPredictionMode = (bool)ServerPredicted.GetValue(server), ClientPredictionMode = (bool)ClientPredicted.GetValue(client),
                    CachedClientParent = (IParentable)ClientParent.GetValue(client), Prediction = client.GetClientSidePrediction()
                };
                var conveyor = row.Prediction as ConveyorPrediction;
                if (conveyor != null) {
                    row.PredictionQueueCount = conveyor.m_Destinations.Count;
                    row.PredictionTransform = conveyor.m_Transform;
                    row.ConveyorRemainingMove = (float)ConveyorRemaining.GetValue(conveyor);
                    row.ConveyorDistanceCalculated = (bool)ConveyorCalculated.GetValue(conveyor);
                }
                if (!Finite(row.LocalPosition) || !Finite(row.WorldPosition) || !Finite(row.LocalRotation)
                    || !Finite(row.WorldRotation) || !Finite(row.LocalScale) || !Finite(row.WorldScale))
                    row.Unsupported = "nonfinite native logical attachment pose";
                else if (parent != null && parentEntry == null)
                    row.Unsupported = "unregistered native attachment parent ancestry";
                else if (server.IsAttached() != client.IsAttached())
                    row.Unsupported = "native server/client attachment transition";
                else row.Unsupported = UnsupportedPrediction(row);
                rows.Add(row);
            }
            if (!selectedIds.SetEquals(ids)) throw new InvalidOperationException("Selected initial native attachment membership changed.");
            return rows.OrderBy(r => r.EntityId).ToArray();
        }

        public static void Validate(Snapshot[] saved)
        {
            if (saved == null) throw new ArgumentNullException("saved");
            var ids = new HashSet<int>();
            foreach (var row in saved)
            {
                if (row == null || !ids.Add(row.EntityId) || row.Object == null || row.Transform == null
                    || row.Physical == null || row.Server == null || row.Client == null || row.Container == null
                    || !ReferenceEquals(EntitySerialisationRegistry.GetEntry(row.Object), row.Entry)
                    || (int)row.Entry.m_Header.m_uEntityID != row.EntityId || row.Object.transform != row.Transform
                    || row.Object.GetComponent<PhysicalAttachment>() != row.Physical
                    || row.Object.GetComponent<ServerPhysicalAttachment>() != row.Server
                    || row.Object.GetComponent<ClientPhysicalAttachment>() != row.Client
                    || row.Physical.m_container != row.Container
                    || Destroyed(row.CachedClientParent as UnityEngine.Object)
                    || Destroyed(row.PredictionTransform)
                    || (!ReferenceEquals(row.Parent, null) && row.Parent == null)
                    || (row.ParentEntry != null && (row.ParentObject == null
                        || !ReferenceEquals(EntitySerialisationRegistry.GetEntry(row.ParentObject), row.ParentEntry)
                        || !HasAncestor(row.Parent, row.ParentObject.transform))))
                    throw new InvalidOperationException("Initial native attachment incarnation or captured parent changed.");
                if (row.Unsupported != null)
                    throw new InvalidOperationException("Attachment " + row.EntityId + " checkpoint unsupported: " + row.Unsupported);
                // A future current state may have different native parenting or
                // prediction. Ordinary callbacks must retire it before Restore;
                // preflight qualification concerns the captured target.
            }
        }

        internal static Snapshot RebindDestroyed(Snapshot target,EntitySerialisationEntry currentEntry,
            EntitySerialisationEntry replacementParentEntry=null)
        {
            if(target==null||currentEntry==null||currentEntry.m_GameObject==null
                ||currentEntry.m_Header.m_uEntityID!=target.EntityId
                ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry((uint)target.EntityId),currentEntry)
                ||target.Unsupported!=null||target.Prediction!=null||target.PredictionTransform!=null
                ||target.PredictionQueueCount!=0)
                throw new InvalidOperationException("Destroyed initial attachment rebind target/current contract differs.");
            var current=Capture(new HashSet<int>{target.EntityId});
            if(current.Length!=1||current[0].EntityId!=target.EntityId||current[0].Prediction!=null
                ||current[0].PredictionTransform!=null||current[0].PredictionQueueCount!=0)
                throw new InvalidOperationException("Recreated initial attachment is not one predictor-free PhysicalAttachment.");
            var row=current[0];
            if(!ReferenceEquals(row.Entry,currentEntry))
                throw new InvalidOperationException("Recreated initial attachment registration differs.");
            if(replacementParentEntry==null)
            {
                row.Parent=target.Parent;row.ParentEntry=target.ParentEntry;row.ParentObject=target.ParentObject;
            }
            else
            {
                var replacementParentObject=replacementParentEntry.m_GameObject;
                var replacementParent=replacementParentObject==null?null:replacementParentObject.transform;
                if(target.ParentEntry==null||!Destroyed(target.Parent)||!Destroyed(target.ParentObject)
                    ||target.ParentEntry.m_Header.m_uEntityID!=replacementParentEntry.m_Header.m_uEntityID
                    ||replacementParentObject==null||replacementParent==null
                    ||!ReferenceEquals(EntitySerialisationRegistry.GetEntry(replacementParentObject),replacementParentEntry)
                    ||!ReferenceEquals(row.ParentEntry,replacementParentEntry)
                    ||!ReferenceEquals(row.ParentObject,replacementParentObject)
                    ||row.Parent==null||!HasAncestor(row.Parent,replacementParent))
                    throw new InvalidOperationException("Recreated initial attachment replacement parent contract differs.");
                // Preserve the freshly captured live parent lineage.  The
                // historical parent has the same entity ID but its Unity
                // wrapper was destroyed with the old attachment container.
            }
            row.LocalPosition=target.LocalPosition;row.WorldPosition=target.WorldPosition;
            row.LocalRotation=target.LocalRotation;row.WorldRotation=target.WorldRotation;
            row.LocalScale=target.LocalScale;row.WorldScale=target.WorldScale;row.Attached=target.Attached;
            row.ServerPredictionMode=target.ServerPredictionMode;row.ClientPredictionMode=target.ClientPredictionMode;
            row.CachedClientParent=target.CachedClientParent;row.Prediction=null;row.PredictionTransform=null;
            row.PredictionQueueCount=0;row.ConveyorRemainingMove=0f;row.ConveyorDistanceCalculated=false;
            row.Unsupported=target.Unsupported;
            Validate(new[]{row});
            return row;
        }

        public static void Restore(Snapshot[] saved)
        {
            Validate(saved);
            // Validate every parent/attachment before writing any transform.
            foreach (var row in saved) RequireRestoredParent(row);
            foreach (var row in saved)
            {
                // These flags are native prediction modes, not activity. A
                // true mode with no predictor (ordinary countertops included)
                // does not run a prediction FSM. Restore the actual modes and
                // cached parent, retaining the original native predictor object.
                ServerPredicted.SetValue(row.Server, row.ServerPredictionMode);
                ClientPredicted.SetValue(row.Client, row.ClientPredictionMode);
                ClientParent.SetValue(row.Client, row.CachedClientParent);
                row.Client.m_Prediction = row.Prediction;
                var conveyor = row.Prediction as ConveyorPrediction;
                if (conveyor != null) {
                    conveyor.m_Destinations.Clear();
                    conveyor.m_Transform = row.PredictionTransform;
                    ConveyorRemaining.SetValue(conveyor, row.ConveyorRemainingMove);
                    ConveyorCalculated.SetValue(conveyor, row.ConveyorDistanceCalculated);
                }
                if (!Same(row.Transform.localPosition, row.LocalPosition)) row.Transform.localPosition = row.LocalPosition;
                if (!Same(row.Transform.localRotation, row.LocalRotation)) row.Transform.localRotation = row.LocalRotation;
                if (!Same(row.Transform.localScale, row.LocalScale)) row.Transform.localScale = row.LocalScale;
            }
        }

        public static void VerifyRestored(Snapshot[] saved)
        {
            Validate(saved);
            foreach (var row in saved)
            {
                RequireRestoredParent(row);
                var differences = new List<string>();
                if (!Same(row.Transform.localPosition, row.LocalPosition)) differences.Add("localPosition");
                if (!Same(row.Transform.localRotation, row.LocalRotation)) differences.Add("localRotation");
                if (!Same(row.Transform.localScale, row.LocalScale)) differences.Add("localScale");
                if (!Same(row.Transform.position, row.WorldPosition)) differences.Add("worldPosition");
                if (!Same(row.Transform.rotation, row.WorldRotation)) differences.Add("worldRotation");
                if (!Same(row.Transform.lossyScale, row.WorldScale)) differences.Add("lossyScale");
                if ((bool)ServerPredicted.GetValue(row.Server) != row.ServerPredictionMode
                    || (bool)ClientPredicted.GetValue(row.Client) != row.ClientPredictionMode
                    || !ReferenceEquals(ClientParent.GetValue(row.Client), row.CachedClientParent)
                    || !ReferenceEquals(row.Client.m_Prediction, row.Prediction)) differences.Add("nativePredictionModeOrIdentity");
                var conveyor = row.Prediction as ConveyorPrediction;
                if (conveyor != null && (conveyor.m_Destinations.Count != 0 || conveyor.m_Transform != row.PredictionTransform
                    || (float)ConveyorRemaining.GetValue(conveyor) != row.ConveyorRemainingMove
                    || (bool)ConveyorCalculated.GetValue(conveyor) != row.ConveyorDistanceCalculated)) differences.Add("nativeEmptyPredictionState");
                if (differences.Count != 0) throw new InvalidOperationException("Native attachment " + row.EntityId
                    + " pose restoration differs: " + string.Join(", ", differences.ToArray()));
            }
        }

        public static object Diagnostics(Snapshot[] saved)
        {
            return new Dictionary<string, object> {
                { "source", "native-initial-PhysicalAttachment-logical-Transform" },
                { "attachments", saved.Select(r => (object)new Dictionary<string, object> {
                    { "entityId", r.EntityId }, { "objectInstanceId", r.Object.GetInstanceID() },
                    { "parentInstanceId", r.Parent == null ? (object)null : r.Parent.GetInstanceID() },
                    { "parentEntityId", r.ParentEntry == null ? (object)null : (int)r.ParentEntry.m_Header.m_uEntityID },
                    { "attached", r.Attached }, { "unsupported", r.Unsupported },
                    { "serverPredictionMode", r.ServerPredictionMode }, { "clientPredictionMode", r.ClientPredictionMode },
                    { "predictionType", r.Prediction == null ? null : r.Prediction.GetType().FullName },
                    { "capturedPredictionQueueCount", r.Prediction == null ? (object)null : r.PredictionQueueCount },
                    { "localPosition", Point(r.LocalPosition) }, { "worldPosition", Point(r.WorldPosition) },
                    { "localRotation", Rotation(r.LocalRotation) }, { "worldRotation", Rotation(r.WorldRotation) },
                    { "localScale", Point(r.LocalScale) }, { "lossyScale", Point(r.WorldScale) }
                }).ToArray() }
            };
        }

        private static void RequireRestoredParent(Snapshot row)
        {
            if (row.Transform.parent != row.Parent || row.Server.IsAttached() != row.Attached || row.Client.IsAttached() != row.Attached)
                throw new InvalidOperationException("Native attachment " + row.EntityId + " target parent/attached state was not restored by native callbacks.");
            var current = row.Client.GetClientSidePrediction();
            if (current != null && (current.GetType() != typeof(ConveyorPrediction) || ((ConveyorPrediction)current).m_Destinations.Count != 0))
                throw new InvalidOperationException("Native attachment " + row.EntityId + " prediction queue was not retired by native callbacks.");
        }
        private static string UnsupportedPrediction(Snapshot row)
        {
            if (row.Prediction != null) {
                var conveyor = row.Prediction as ConveyorPrediction;
                if (conveyor == null || row.Prediction.GetType() != typeof(ConveyorPrediction)) return "unknown native attachment predictor";
                if (conveyor.m_Destinations.Count != 0) return "nonempty native conveyor prediction queue";
                if (row.PredictionTransform != row.Transform || !Finite(row.ConveyorRemainingMove)) return "invalid native empty prediction owner/state";
            }
            if (row.Physical.m_meshLerper != null && row.Physical.GetFakeMeshActive()) return "active native mesh interpolation";
            var lerpers = row.Object.GetComponents<EmptyLerp>();
            if (lerpers.Any(l => l.GetType() != typeof(EmptyLerp))) return "stateful native world interpolation";
            return null;
        }
        private static bool HasAncestor(Transform child, Transform parent)
        { for (var t = child; t != null; t = t.parent) if (t == parent) return true; return false; }
        private static bool Destroyed(UnityEngine.Object value) { return !ReferenceEquals(value, null) && value == null; }
        private static FieldInfo Field(Type type, string name, Type expected)
        {
            var field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field == null || field.FieldType != expected) throw new InvalidOperationException("Native attachment field layout differs: " + type.Name + "." + name);
            return field;
        }
        private static object Point(Vector3 v) { return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z } }; }
        private static object Rotation(Quaternion q) { return new Dictionary<string, object> { { "x", q.x }, { "y", q.y }, { "z", q.z }, { "w", q.w } }; }
        private static bool Same(Vector3 a, Vector3 b) { return a.x == b.x && a.y == b.y && a.z == b.z; }
        private static bool Same(Quaternion a, Quaternion b) { return a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w; }
        private static bool Finite(float v) { return !float.IsNaN(v) && !float.IsInfinity(v); }
        private static bool Finite(Vector3 v) { return Finite(v.x) && Finite(v.y) && Finite(v.z); }
        private static bool Finite(Quaternion q) { return Finite(q.x) && Finite(q.y) && Finite(q.z) && Finite(q.w); }
    }
}
