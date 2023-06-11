using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public class EditorState {
        public GameEntityRecord SelectedChef { get; set; }
        public int SelectedActionIndex { get; set; }
        public int SelectedFrame { get; set; }

        public GameActionSequences Sequences { get; set; }
        public GameEntityRecords Records { get; set; }
        public Dictionary<int, GameMap> MapByChef { get; set; }
        public GameMapGeometry Geometry { get; set; }

        public List<ActionTemplate> GetActionTemplatesForEntity(GameEntityRecord entity) {
            var templates = new List<ActionTemplate>();
            var data = entity.data[SelectedFrame];
            if (data.attachmentParent != null && data.attachmentParent.chefState != null) {
                return templates;
            }
            templates.Add(new ThrowTowardsActionTemplate(entity));
            if (entity.prefab.CanUse || entity.prefab.IsCannon) {
                templates.Add(new UseItemActionTemplate(entity));
            }
            if (entity.prefab.IsCrate) {
                templates.Add(new GetFromCrateActionTemplate(entity));
            } else {
                templates.Add(new PickupItemActionTemplate(entity));
            }
            if (entity.prefab.IsCannon) {
                templates.Add(new PilotRotationActionTemplate(entity));
            }
            templates.Add(new PlaceItemActionTemplate(entity));
            templates.Add(new PrepareToInteractActionTemplate(entity));
            if (entity.prefab.Name == "Dirty Plate Spawner") {
                templates.Add(new WaitForDirtyPlateActionTemplate(entity, 1));
                templates.Add(new WaitForDirtyPlateActionTemplate(entity, 2));
                templates.Add(new WaitForDirtyPlateActionTemplate(entity, 3));
                templates.Add(new WaitForDirtyPlateActionTemplate(entity, 4));
            } else if (entity.prefab.Name == "Clean Plate Spawner") {
                templates.Add(new WaitForCleanPlateActionTemplate(entity));
            } else {
                if (entity.prefab.Spawns.Count > 0) {
                    templates.Add(new WaitForSpawnActionTemplate(entity));
                }
            }
            if (entity.prefab.IsWashingStation) {
                templates.Add(new WaitForWashingProgressActionTemplate(entity));
            }
            if (entity.prefab.IsChoppable) {
                templates.Add(new WaitForChoppingProgressActionTemplate(entity));
            }
            if (entity.prefab.IsCookingHandler) {
                templates.Add(new WaitForCookingProgressActionTemplate(entity));
            }
            if (entity.prefab.IsMixer) {
                templates.Add(new WaitForMixingProgressActionTemplate(entity));
            }
            return templates;
        }

        public List<ActionTemplate> GetActionTemplatesForPosition(Vector2 pos) {
            var templates = new List<ActionTemplate>();
            var (gridX, gridY) = Geometry.CoordsToGridPosRounded(pos);
            templates.Add(new ThrowTowardsPositionActionTemplate(pos));
            templates.Add(new DropTowardsPositionActionTemplate(pos));
            templates.Add(new GotoPosActionTemplate(gridX, gridY));
            templates.Add(new WaitForFramesActionTemplate(5));
            return templates;
        }

        public void ApplyActionTemplate(ActionTemplate template) {
            var generated = template.GenerateActions(SelectedFrame);
            foreach (var action in generated) {
                Sequences.InsertAction(SelectedChef, SelectedActionIndex, action);
                SelectedActionIndex++;
            }
        }

        public int ResimulationFrame() {
            if (SelectedActionIndex == 0 || SelectedChef == null) {
                return 0;
            }
            return Sequences.Actions[Sequences.ChefIndexByChef[SelectedChef]][SelectedActionIndex - 1].Predictions.EndFrame ?? 0;
        }
    }

    public interface ActionTemplate {
        string Describe();
        List<GameAction> GenerateActions(int frame);
    }

    public class ThrowTowardsActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public ThrowTowardsActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Throw towards {Record.displayName}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                 new ThrowAction{
                     Location = new EntityLocationToken(Record.ReverseEngineerStableEntityReference(frame))}
                 };
        }
    }

    public class DropTowardsPositionActionTemplate : ActionTemplate {
        public Vector2 Pos { get; set; }

        public DropTowardsPositionActionTemplate(Vector2 pos) {
            Pos = pos;
        }

        public string Describe() {
            return $"Drop towards {Pos}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                 new DropAction{
                     Location = new LiteralLocationToken(Pos)}
                 };
        }
    }

    public class ThrowTowardsPositionActionTemplate : ActionTemplate {
        public Vector2 Pos { get; set; }

        public ThrowTowardsPositionActionTemplate(Vector2 pos) {
            Pos = pos;
        }

        public string Describe() {
            return $"Throw towards {Pos}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                 new ThrowAction{
                     Location = new LiteralLocationToken(Pos)}
                 };
        }
    }

    public class GotoPosActionTemplate : ActionTemplate {
        public int GridX { get; set; }
        public int GridY { get; set; }

        public GotoPosActionTemplate(int gridX, int gridY) {
            GridX = gridX;
            GridY = gridY;
        }

        public string Describe() {
            return $"Goto grid ({GridX}, {GridY})";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                 new GotoAction{
                     DesiredPos = new GridPosLocationToken(GridX, GridY)}
                 };
        }
    }

    public class UseItemActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public UseItemActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Use {Record.displayName}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new InteractAction { Primary = false, Subject = Record.ReverseEngineerStableEntityReference(frame)}
            };
        }
    }

    public class PickupItemActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public PickupItemActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Pickup {Record.displayName}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new InteractAction {
                    Primary = true,
                    IsPickup = true,
                    Subject = Record.ReverseEngineerStableEntityReference(frame)}
            };
        }
    }

    public class GetFromCrateActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public GetFromCrateActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Get {Record.prefab.Spawns[0].Name} from create";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new InteractAction {
                    Primary = true,
                    IsPickup = true,
                    ExpectSpawn = true,
                    Subject = Record.ReverseEngineerStableEntityReference(frame)}
            };
        }
    }

    public class PlaceItemActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public PlaceItemActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Place {Record.displayName}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new InteractAction {
                    Primary = true,
                    IsPickup = false,
                    Subject = Record.ReverseEngineerStableEntityReference(frame)}
            };
        }
    }

    public class PrepareToInteractActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public PrepareToInteractActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Prepare to interact with {Record.displayName}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new InteractAction {
                    Prepare = true,
                    Subject = Record.ReverseEngineerStableEntityReference(frame)}
            };
        }
    }

    public class WaitForSpawnActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public WaitForSpawnActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Wait for {Record.prefab.Spawns[0].Name}";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForSpawnAction {
                    Spawner = Record.ReverseEngineerStableEntityReference(frame)
                }
            };
        }
    }

    public class WaitForWashingProgressActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public WaitForWashingProgressActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Wait for {Record.displayName} washing progress 100%";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForWashingProgressAction {
                    Entity = Record.ReverseEngineerStableEntityReference(frame),
                    WashingProgress = Record.prefab.MaxProgress
                }
            };
        }
    }

    public class WaitForChoppingProgressActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public WaitForChoppingProgressActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Wait for {Record.displayName} chopping progress 100%";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForChoppingProgressAction {
                    Entity = Record.ReverseEngineerStableEntityReference(frame),
                    ChoppingProgress = Record.prefab.MaxProgress
                }
            };
        }
    }

    public class WaitForCookingProgressActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public WaitForCookingProgressActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Wait for {Record.displayName} cooking progress 100%";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForCookingProgressAction {
                    Entity = Record.ReverseEngineerStableEntityReference(frame),
                    CookingProgress = Record.prefab.MaxProgress
                }
            };
        }
    }

    public class WaitForMixingProgressActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public WaitForMixingProgressActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Wait for {Record.displayName} mixing progress 100%";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForMixingProgressAction {
                    Entity = Record.ReverseEngineerStableEntityReference(frame),
                    MixingProgress = Record.prefab.MaxProgress
                }
            };
        }
    }

    public class WaitForDirtyPlateActionTemplate : ActionTemplate {
        public GameEntityRecord Spawner { get; set; }
        public int NumPlates { get; set; }

        public WaitForDirtyPlateActionTemplate(GameEntityRecord spawner, int numPlates) {
            Spawner = spawner;
            NumPlates = numPlates;
        }

        public string Describe() {
            return $"Wait for {NumPlates} dirty plates";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForDirtyPlateAction {
                    DirtyPlateSpawner = Spawner,
                    Count = NumPlates
                }
            };
        }
    }

    public class WaitForCleanPlateActionTemplate : ActionTemplate {
        public GameEntityRecord Spawner { get; set; }

        public WaitForCleanPlateActionTemplate(GameEntityRecord spawner) {
            Spawner = spawner;
        }

        public string Describe() {
            return $"Wait for clean plate";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForCleanPlateAction {
                    DryingPart = Spawner,
                }
            };
        }
    }


    public class WaitForFramesActionTemplate : ActionTemplate {
        public int Frames { get; set; } = 10;

        public WaitForFramesActionTemplate(int frames) {
            Frames = frames;
        }


        public string Describe() {
            return $"Sleep {Frames} frames";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitAction {
                    NumFrames = Frames
                }
            };
        }
    }

    public class PilotRotationActionTemplate : ActionTemplate
    {
        public GameEntityRecord Entity { get; set; }

        public PilotRotationActionTemplate(GameEntityRecord entity)
        {
            Entity = entity;
        }

        public string Describe()
        {
            return "Rotate cannon";
        }

        public List<GameAction> GenerateActions(int frame)
        {
            return new List<GameAction>{
                new PilotRotationAction {
                    PilotRotationEntity = Entity.ReverseEngineerStableEntityReference(frame),
                    TargetAngle = Entity.data[frame].pilotRotationAngle,
                }
            };
        }
    }
}