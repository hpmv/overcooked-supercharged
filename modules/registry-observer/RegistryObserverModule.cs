using System;
using System.Collections.Generic;
using System.Reflection;
using Hpmv;
using SuperchargedPatch.Authoring;
using SuperchargedPatch.Bridge;
using SuperchargedPatch.Extensions;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

namespace SuperchargedPatch.Authoring.Modules
{
    // Explicit paused observation only. No hooks, native callbacks, initial-set
    // updates, input/frame commits, cache resets or world writes.
    public sealed partial class RegistryObserverModule : IAuthoringModule
    {
        private bool disposed;
        private int refreshes;
        private object last;
        public string Name { get { return "native-registry-metadata-observer-v1"; } }
        public int ApiVersion { get { return 1; } }
        public void Dispose() { disposed = true; }
        private sealed class Row
        {
            internal GameObject Object;
            internal int Id, Instance;
            internal EntityRegistryData Data;
        }
        private static void RequireBoundary()
        {
            if (!TimeManager.IsPaused(TimeManager.PauseLayer.Main) || !NativeSessionBridge.InputBlocked)
                throw new InvalidOperationException("Registry refresh requires native pause and the bridge input fence.");
        }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static HashSet<int> InitialSet(string name)
        {
            var field=typeof(NativeSceneMetadata).GetField(name,BindingFlags.Static|BindingFlags.NonPublic);
            if(field==null || field.FieldType!=typeof(HashSet<int>)) throw new InvalidOperationException("Frozen core initial-set contract differs.");
            return (HashSet<int>)field.GetValue(null);
        }
        public object Invoke(string operation, Dictionary<string, object> args)
        {
            if (disposed) throw new ObjectDisposedException("RegistryObserverModule");
            if (args == null) throw new ArgumentException("Explicit arguments required.");
            if (operation == "status") {
                if(args.Count!=0) throw new ArgumentException("Status takes empty arguments.");
                return new Dictionary<string, object> { {"name",Name}, {"refreshes",refreshes}, {"sceneMetadataRefreshes",NativeSceneMetadata.Refreshes}, {"last",last} };
            }
            if (operation == "observe-absent") return ObserveAbsent(args);
            if (operation != "refresh") throw new ArgumentException("Use refresh, observe-absent or status.");
            object selected;
            if(args.Count!=1 || !args.TryGetValue("entityId",out selected) || !(selected is int || selected is long)) throw new ArgumentException("Refresh requires one integer entityId.");
            int selectedId=Convert.ToInt32(selected);
            if(selectedId<=0) throw new ArgumentException("Positive native entityId required.");
            RequireBoundary();
            var bodies=InitialSet("InitialRigidBodyIds"); var attachments=InitialSet("InitialPhysicalAttachmentIds");
            var beforeBodies=new HashSet<int>(bodies); var beforeAttachments=new HashSet<int>(attachments);
            var server = Injector.Server;
            var output = server.CurrentFrameData;
            var entries = EntitySerialisationRegistry.m_EntitiesList;
            int count = entries.Count;
            if (count < 1 || count > 4096) throw new InvalidOperationException("Native registry size is outside1..4096.");
            var rows = new List<Row>(); var ids = new HashSet<int>(); var descriptions = new List<object>();
            for (int i = 0; i < count; i++)
            {
                var entry = entries._items[i]; var obj = entry.m_GameObject;
                int id = (int)entry.m_Header.m_uEntityID;
                if(id!=selectedId) continue;
                if (obj == null) throw new InvalidOperationException("Selected native registry object is missing.");
                if (id <= 0 || !ids.Add(id)) throw new InvalidOperationException("Native registry ID is nonpositive or duplicated.");
                Vector3 p = obj.transform.position;
                if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) || String.IsNullOrEmpty(obj.name)) throw new InvalidOperationException("Invalid observed native pose/name.");
                var data = new EntityRegistryData { EntityId=id, Name=obj.name, Pos=new Point {X=p.x,Y=p.y,Z=p.z}, Components=new List<string>(), SyncEntityTypes=new List<int>() };
                foreach (var c in obj.GetComponents<Component>()) if (c != null) data.Components.Add(c.GetType().Name);
                for (int j = 0; j < entry.m_ServerSynchronisedComponents.Count; j++)
                    data.SyncEntityTypes.Add((int)entry.m_ServerSynchronisedComponents._items[j].GetEntityType());
                var collection = obj.GetComponent<SpawnableEntityCollection>();
                if (collection != null)
                {
                    var spawnables = collection.GetSpawnables();
                    if (spawnables == null || spawnables.Count > 4096) throw new InvalidOperationException("Observed spawn collection is missing or excessive.");
                    data.SpawnNames = new List<string>();
                    foreach (var child in spawnables) data.SpawnNames.Add(child == null ? "" : child.name);
                }
                // Absent collection remains absent, not a fabricated empty list.
                rows.Add(new Row { Object=obj, Id=id, Instance=obj.GetInstanceID(), Data=data });
                descriptions.Add(new Dictionary<string, object> { {"id",id}, {"objectInstanceId",obj.GetInstanceID()},
                    {"components",data.Components.ToArray()}, {"hasSpawnCollection",collection != null},
                    {"spawnNames",data.SpawnNames == null ? null : data.SpawnNames.ToArray()} });
            }
            if(rows.Count!=1) throw new InvalidOperationException("EntityId must identify exactly one registered native object.");
            RequireBoundary();
            if (!object.ReferenceEquals(server.CurrentFrameData,output) || !object.ReferenceEquals(EntitySerialisationRegistry.m_EntitiesList,entries) || entries.Count != count)
                throw new InvalidOperationException("Native observation boundary or registry changed before publication.");
            int matches=0;
            for (int i = 0; i < count; i++)
                if ((int)entries._items[i].m_Header.m_uEntityID == selectedId) {
                    matches++;
                    if(!object.ReferenceEquals(entries._items[i].m_GameObject,rows[0].Object) || rows[0].Object.GetInstanceID()!=rows[0].Instance)
                        throw new InvalidOperationException("Selected native registry incarnation changed before publication.");
                }
            if(matches!=1 || !object.ReferenceEquals(bodies,InitialSet("InitialRigidBodyIds")) || !object.ReferenceEquals(attachments,InitialSet("InitialPhysicalAttachmentIds")) || !bodies.SetEquals(beforeBodies) || !attachments.SetEquals(beforeAttachments))
                throw new InvalidOperationException("Native identity or initial fixed sets changed during read-only observation.");
            int prior = output.EntityRegistry == null ? 0 : output.EntityRegistry.Count;
            if (prior + 1 > 8192) throw new InvalidOperationException("Pending observation queue is full; wait for its ordinary delivery.");
            var merged = new List<EntityRegistryData>(prior + 1);
            if (output.EntityRegistry != null) merged.AddRange(output.EntityRegistry);
            foreach (var row in rows) merged.Add(row.Data);
            output.EntityRegistry = merged; // The sole external write: ordinary observer data.
            refreshes++;
            return last = new Dictionary<string, object> { {"ok",true}, {"refresh",refreshes}, {"published",1}, {"entityId",selectedId}, {"previousQueued",prior},
                {"entries",descriptions.ToArray()}, {"nativeStateChanged",false}, {"initialSetsChanged",false},
                {"delivery","Queued for the original paused controller exchange; caller must observe the updated registry before using it."} };
        }
    }
}
