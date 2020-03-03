using System;
using System.Collections.Generic;

namespace Hpmv {
    public class EditorState {
        public GameEntityRecord SelectedChef { get; set; }
        public int SelectedActionIndex { get; set; }
        public int SelectedFrame { get; set; }
        public int LastSimulatedFrame { get; set; }

        public GameActionSequences Sequences { get; set; }
        public GameEntityRecords Records { get; set; }

        public List<ActionTemplate> GetActionTemplatesForEntity(GameEntityRecord entity) {
            var templates = new List<ActionTemplate>();
            var data = entity.data[SelectedFrame];
            if (data.attachmentParent != null && data.attachmentParent.chefState != null) {
                return templates;
            }
            templates.Add(new ThrowTowardsActionTemplate(entity));
            if (entity.prefab.CanUse) {
                templates.Add(new UseItemActionTemplate(entity));
            }
            if (entity.prefab.IsCrate) {
                templates.Add(new GetFromCrateActionTemplate(entity));
            } else {
                templates.Add(new PickupItemActionTemplate(entity));
            }
            templates.Add(new PlaceItemActionTemplate(entity));
            templates.Add(new PrepareToInteractActionTemplate(entity));
            if (entity.prefab.Spawns.Count > 0) {
                templates.Add(new WaitForSpawnActionTemplate(entity));
            }
            if (entity.prefab.MaxProgress > 0) {
                templates.Add(new WaitForMaxProgressActionTemplate(entity));
            }
            return templates;
        }

        public void ApplyActionTemplate(ActionTemplate template) {
            var generated = template.GenerateActions(SelectedFrame);
            foreach (var action in generated) {
                Sequences.InsertAction(SelectedChef, SelectedActionIndex, action);
                SelectedActionIndex++;
            }
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

    public class WaitForMaxProgressActionTemplate : ActionTemplate {
        public GameEntityRecord Record { get; set; }

        public WaitForMaxProgressActionTemplate(GameEntityRecord record) {
            Record = record;
        }

        public string Describe() {
            return $"Wait for {Record.displayName} to complete";
        }

        public List<GameAction> GenerateActions(int frame) {
            return new List<GameAction>{
                new WaitForProgressAction {
                    Entity = Record.ReverseEngineerStableEntityReference(frame),
                    Progress = Record.prefab.MaxProgress
                }
            };
        }
    }
}