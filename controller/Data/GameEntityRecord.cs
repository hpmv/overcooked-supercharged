using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {

    public class GameEntityRecord {
        public EntityPath path;
        public string displayName = "";
        public string className = "";
        public PrefabRecord prefab = null;
        public GameEntityRecord spawner = null;
        public List<GameEntityRecord> spawned = new List<GameEntityRecord>();
        public Versioned<int> nextSpawnId = new Versioned<int>(0);
        /// This is the action ID of the action that awaited for this spawn.
        public Versioned<int> spawnOwner = new Versioned<int>(-1);
        public Versioned<Vector3> position;
        public Versioned<Vector3> velocity = new Versioned<Vector3>(default);
        public Versioned<Vector3> angularVelocity = new Versioned<Vector3>(default);
        public Versioned<System.Numerics.Quaternion> rotation = new Versioned<System.Numerics.Quaternion>(System.Numerics.Quaternion.Identity);
        public Versioned<bool> existed;
        public Versioned<SpecificEntityData> data = new Versioned<SpecificEntityData>(new SpecificEntityData());
        public Versioned<ChefState> chefState = null;
        public Versioned<double> progress = new Versioned<double>(0);

        public Save.GameEntityRecord ToProto() {
            var result = new Save.GameEntityRecord {
                Path = path.ToProto(),
                DisplayName = displayName,
                ClassName = className,
                Prefab = prefab.ToProto(),
                Spawner = spawner?.path?.ToProto(),
                NextSpawnId = nextSpawnId.ToProto(),
                SpawnOwner = spawnOwner.ToProto(),
                Position = position.ToProto(),
                Rotation = rotation.ToProto(),
                Velocity = velocity.ToProto(),
                AngularVelocity = angularVelocity.ToProto(),
                Existed = existed.ToProto(),
                Data = data.ToProto(),
                ChefState = chefState?.ToProto(),
                Progress = progress.ToProto()
            };
            foreach (var spawned in this.spawned) {
                result.Spawned.Add(spawned.path.ToProto());
            }
            return result;
        }

        public void ReadMutableDataFromProto(Save.GameEntityRecord record, LoadContext context) {
            spawner = record.Spawner.FromProtoRef(context);
            spawned = record.Spawned.Select(s => s.FromProtoRef(context)).ToList();
            nextSpawnId = record.NextSpawnId.FromProto();
            spawnOwner = record.SpawnOwner.FromProto();
            position = record.Position.FromProto();
            rotation = record.Rotation?.FromProto() ?? new Versioned<System.Numerics.Quaternion>(System.Numerics.Quaternion.Identity);
            velocity = record.Velocity.FromProto();
            angularVelocity = record.AngularVelocity?.FromProto() ?? new Versioned<Vector3>(default);
            existed = record.Existed.FromProto();
            data = record.Data.FromProto(context);
            chefState = record.ChefState?.FromProto(context);
            progress = record.Progress.FromProto();
        }

        public void CleanRecordsFromFrameRecursively(int frame) {
            spawnOwner.RemoveAllFrom(frame);
            position.RemoveAllFrom(frame);
            rotation.RemoveAllFrom(frame);
            velocity.RemoveAllFrom(frame);
            angularVelocity.RemoveAllFrom(frame);
            existed.RemoveAllFrom(frame);
            data.RemoveAllFrom(frame);
            nextSpawnId.RemoveAllFrom(frame);
            progress.RemoveAllFrom(frame);
            if (chefState != null) {
                chefState.RemoveAllFrom(frame);
            }
            foreach (var spawned in spawned) {
                spawned.CleanRecordsFromFrameRecursively(frame);
            }
        }



        public override string ToString() {
            return displayName;
        }

        public IEnumerable<GameEntityRecord> GenAllEntities() {
            yield return this;
            foreach (var spawn in spawned) {
                foreach (var record in spawn.GenAllEntities()) {
                    yield return record;
                }
            }
        }

        public bool IsChef() {
            return chefState != null;
        }

        public bool IsGridOccupant() {
            // Heuristic: all initial static objects are grid occupants.
            // Except some cases like ingredient containers.
            return path.ids.Length == 1 && !prefab.CanContainIngredients;
        }

        /// This is a very useful quality of life functionality.
        ///
        /// Imagine sequencing three actions:
        ///    1: Pick up object from crate X (entity path of the new object is X.0)
        ///    2: Place the object on counter A
        ///    3: Pick up object from crate X (entity path of the new object is X.1)
        ///    4: Drop the object on the floor
        ///    5: Pick up that same X.1 object
        /// Now, consider what happens if we were to delete actions 1 and 2 in the editor:
        ///    3: Pick up object from crate X (entity path of the new object is **X.0**)
        ///    4: Drop the object on the floor
        ///    5: Pick up that same X.1 object  <<--- FAILS! X.1 does not exist yet.
        /// The reason why this fails is because we store GameEntityRecords by its entity path,
        /// i.e. "the second spawn of the crate X" is always the same GameEntityRecord no matter
        /// what; this is a natural consequence of needing to store all history.
        ///
        /// With the help of this function, instead of storing X.1 for action 5, we store:
        ///    5: Pick up the object referenced as (The entity spawned by Action 3)
        /// This way, this is much less likely to fail when shuffling actions around in the
        /// editor. (Note that action IDs are stable.)
        ///
        /// The way it works is by having certain actions "claim" the entity that it spawns by
        /// storing the "spawnOwner" field on the GameEntityRecord; this is versioned. Then, when
        /// creating action 5, we look up the spawnOwner at that frame, and if there is an owner,
        /// we reference the owning action instead of the GameEntityRecord itself.
        public IEntityReference ReverseEngineerStableEntityReference(int frame) {
            if (spawner == null) {
                return new LiteralEntityReference(this);
            }
            if (spawnOwner[frame] == -1) {
                return null;
            }
            return new SpawnedEntityReference(spawnOwner[frame]);
        }
    }

    public struct ChefState {
        public Vector2 forward;
        public GameEntityRecord highlightedForPickup;
        public GameEntityRecord highlightedForUse;
        public GameEntityRecord highlightedForPlacement;
        public double dashTimer;
        public bool isAiming;

        public Save.ChefState ToProto() {
            return new Save.ChefState {
                Forward = forward.ToProto(),
                HighlightedForPickup = highlightedForPickup?.path?.ToProto(),
                HighlightedForUse = highlightedForUse?.path?.ToProto(),
                HighlightedForPlacement = highlightedForPlacement?.path?.ToProto(),
                DashTimer = dashTimer,
                IsAiming = isAiming
            };
        }
    }

    public static class ChefStateFromProto {
        public static ChefState FromProto(this Save.ChefState state, LoadContext context) {
            return new ChefState {
                forward = state.Forward.FromProto(),
                highlightedForPickup = state.HighlightedForPickup.FromProtoRef(context),
                highlightedForUse = state.HighlightedForUse.FromProtoRef(context),
                highlightedForPlacement = state.HighlightedForPlacement.FromProtoRef(context),
                dashTimer = state.DashTimer,
                isAiming = state.IsAiming
            };
        }
    }
}