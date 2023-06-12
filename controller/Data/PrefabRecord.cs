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
        public bool IsIngredient { get; set; }
        public int IngredientId { get; set; }
        public bool IsBoard { get; set; }
        public bool IsWashingStation { get; set; }
        public bool IsChoppable { get; set; }
        public bool IsButton { get; set; }
        public bool IsAttachStation { get; set; }
        public bool CanBeAttached { get; set; } // whether the prefab has PhysicalAttachment
        public bool IsChef { get; set; }
        public bool IsCannon { get; set; }
        public bool IsThrowable { get; set; }
        public bool IsTerminal { get; set; }
        public bool IsMixer { get; set; }
        public bool IsCookingHandler { get; set; }
        public bool IsPickupItemSwitcher { get; set; }
        public bool HasTriggerColorCycle { get; set; }
        public bool IsKitchenFlowController { get; set; }
        public bool IsStack { get; set; }
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
                IsIngredient = IsIngredient,
                IngredientId = IngredientId,
                IsBoard = IsBoard,
                IsWashingStation = IsWashingStation,
                IsChoppable = IsChoppable,
                IsButton = IsButton,
                IsAttachStation = IsAttachStation,
                CanBeAttached = CanBeAttached,
                IsChef = IsChef,
                IsCannon = IsCannon,
                IsThrowable = IsThrowable,
                IsTerminal = IsTerminal,
                IsMixer = IsMixer,
                IsCookingHandler = IsCookingHandler,
                IsPickupItemSwitcher = IsPickupItemSwitcher,
                HasTriggerColorCycle = HasTriggerColorCycle,
                IsKitchenFlowController = IsKitchenFlowController,
                IsStack = IsStack,
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
                IsIngredient = record.IsIngredient,
                IngredientId = record.IngredientId,
                IsBoard = record.IsBoard,
                IsWashingStation = record.IsWashingStation,
                IsChoppable = record.IsChoppable,
                IsButton = record.IsButton,
                IsAttachStation = record.IsAttachStation,
                CanBeAttached = record.CanBeAttached,
                IsChef = record.IsChef,
                IsCannon = record.IsCannon,
                IsThrowable = record.IsThrowable,
                IsTerminal = record.IsTerminal,
                IsMixer = record.IsMixer,
                IsCookingHandler = record.IsCookingHandler,
                IsPickupItemSwitcher = record.IsPickupItemSwitcher,
                HasTriggerColorCycle = record.HasTriggerColorCycle,
                IsKitchenFlowController = record.IsKitchenFlowController,
                IsStack = record.IsStack,
                Ignore = record.Ignore,
                OccupiedGridPoints = record.OccupiedGridPoints.Count == 0 ? new Vector2[] { default } : record.OccupiedGridPoints.Select(x => x.FromProto()).ToArray(),
            };
        }
    }
}
