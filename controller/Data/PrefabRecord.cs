using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Hpmv {
    public class PrefabRecord {
        public string Name { get; set; }
        public string ClassName { get; set; }
        public List<PrefabRecord> Spawns = new List<PrefabRecord>();
        public SpawningPath SpawningPath { get; set; }
        public bool CanUse { get; set; }
        public bool IsCrate { get; set; }
        public double MaxProgress { get; set; }
        public bool CanContainIngredients { get; set; }
        public int MaxIngredientCount { get; set; }
        public bool IsIngredient { get; set; }
        public int IngredientId { get; set; }
        public int CookingStage { get; set; }
        public bool IsBoard { get; set; }
        public bool IsWashingStation { get; set; }
        public bool IsMixerStation { get; set; }
        public bool IsHeatingStation { get; set; }
        public bool IsChoppable { get; set; }
        public bool IsButton { get; set; }
        public bool IsAttachStation { get; set; }
        public bool Ignore { get; set; }
        public Vector2[] OccupiedGridPoints { get; set; } = new[] { Vector2.Zero };

        public PrefabRecord(string name, string className = "") {
            Name = name;
            ClassName = className;
        }

        public Save.PrefabRecord ToProto() {
            var result = new Save.PrefabRecord {
                Name = Name,
                ClassName = ClassName,
                SpawningPath = SpawningPath?.ToProto(),
                CanUse = CanUse,
                IsCrate = IsCrate,
                MaxProgress = MaxProgress,
                CanContainIngredients = CanContainIngredients,
                MaxIngredientCount = MaxIngredientCount,
                IsIngredient = IsIngredient,
                IngredientId = IngredientId,
                CookingStage = CookingStage,
                IsBoard = IsBoard,
                IsWashingStation = IsWashingStation,
                IsMixerStation = IsMixerStation,
                IsHeatingStation = IsHeatingStation,
                IsChoppable = IsChoppable,
                IsButton = IsButton,
                IsAttachStation = IsAttachStation,
                Ignore = Ignore,
            };
            foreach (var prefab in Spawns) {
                result.Spawns.Add(prefab.ToProto());
            }
            result.OccupiedGridPoints.AddRange(OccupiedGridPoints.Select(x => x.ToProto()));
            return result;
        }

        public void CalculateSpawningPathsForSpawnsRecursively()
        {
            if (SpawningPath == null) {return;}
            for (int i = 0; i < Spawns.Count; i++)
            {
                Spawns[i].SpawningPath = SpawningPath.Concat(i);
                Spawns[i].CalculateSpawningPathsForSpawnsRecursively();
            }
        }
    }

    public static class PrefabRecordFromProto {
        public static PrefabRecord FromProto(this Save.PrefabRecord record) {
            return new PrefabRecord(record.Name, record.ClassName) {
                Spawns = record.Spawns.Select(s => s.FromProto()).ToList(),
                SpawningPath = record.SpawningPath?.FromProto(),
                CanUse = record.CanUse,
                IsCrate = record.IsCrate,
                MaxProgress = record.MaxProgress,
                CanContainIngredients = record.CanContainIngredients,
                MaxIngredientCount = record.MaxIngredientCount,
                IsIngredient = record.IsIngredient,
                IngredientId = record.IngredientId,
                CookingStage = record.CookingStage,
                IsBoard = record.IsBoard,
                IsWashingStation = record.IsWashingStation,
                IsMixerStation = record.IsMixerStation,
                IsHeatingStation = record.IsHeatingStation,
                IsChoppable = record.IsChoppable,
                IsButton = record.IsButton,
                IsAttachStation = record.IsAttachStation,
                Ignore = record.Ignore,
                OccupiedGridPoints = record.OccupiedGridPoints.Count == 0 ? new Vector2[] { default } : record.OccupiedGridPoints.Select(x => x.FromProto()).ToArray(),
            };
        }
    }
}
