﻿using System;
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
        public Versioned<ISpawnClaimingAction> spawnOwner = new Versioned<ISpawnClaimingAction>(null);
        public Versioned<Vector3> position;
        public Versioned<Vector3> velocity = new Versioned<Vector3>(default);
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
                Velocity = velocity.ToProto(),
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
            spawnOwner = record.SpawnOwner.FromProto(context);
            position = record.Position.FromProto();
            velocity = record.Velocity.FromProto();
            existed = record.Existed.FromProto();
            data = record.Data.FromProto(context);
            chefState = record.ChefState?.FromProto(context);
            progress = record.Progress.FromProto();
        }

        public void CleanRecordsFromFrameRecursively(int frame) {
            spawnOwner.RemoveAllFrom(frame);
            position.RemoveAllFrom(frame);
            velocity.RemoveAllFrom(frame);
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

        public IEntityReference ReverseEngineerStableEntityReference(int frame) {
            if (spawner == null) {
                return new LiteralEntityReference(this);
            }
            if (spawnOwner[frame] == null) {
                return null;
            }
            return new SpawnedEntityReference(spawnOwner[frame]);
        }
    }

    public struct SpecificEntityData {
        public GameEntityRecord attachmentParent;
        public GameEntityRecord attachment;
        public List<int> contents;
        // 0.2 seconds per interaction, 7 max progress.
        public Dictionary<GameEntityRecord, TimeSpan> chopInteracters;
        public GameEntityRecord itemBeingChopped;
        public HashSet<GameEntityRecord> washers;

        public Save.SpecificEntityData ToProto() {
            var result = new Save.SpecificEntityData {
                AttachmentParent = attachmentParent?.path?.ToProto(),
                Attachment = attachment?.path?.ToProto(),
                ItemBeingChopped = itemBeingChopped?.path?.ToProto(),
            };
            if (contents != null) {
                result.Contents.AddRange(contents);
            }
            if (chopInteracters != null) {
                foreach (var (chef, time) in chopInteracters) {
                    result.ChopInteracters[chef.path.ids[0]] = time.TotalMilliseconds;
                }
            }
            if (washers != null) {
                foreach (var washer in washers) {
                    result.Washers.Add(washer.path.ToProto());
                }
            }
            return result;
        }
    }

    public static class SpecificEntityDataFromProto {
        public static SpecificEntityData FromProto(this Save.SpecificEntityData data, LoadContext context) {
            return new SpecificEntityData {
                attachmentParent = data.AttachmentParent.FromProtoRef(context),
                attachment = data.Attachment.FromProtoRef(context),
                itemBeingChopped = data.ItemBeingChopped.FromProtoRef(context),
                contents = data.Contents.Count == 0 ? null : data.Contents.ToList(),
                chopInteracters = data.ChopInteracters.Count == 0 ? null :
                    data.ChopInteracters.ToDictionary(kv => context.GetRootRecord(kv.Key), kv => TimeSpan.FromMilliseconds(kv.Value)),
                washers = data.Washers.Count == 0 ? null : data.Washers.Select(washer => washer.FromProtoRef(context)).ToHashSet()
            };
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