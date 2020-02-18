using System.Collections.Generic;

namespace Hpmv {
    class WaitForPlateAction : GameAction {
        public bool Dirty { get; set; }

        public override string Describe() {
            return $"Wait for {(Dirty ? "dirty" : "clean")} plate";
        }

        public override void InitializeState(GameActionState state) {
            state.Action = this;
        }
        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            while (true) {
                foreach (var entity in context.Entities.entities) {
                    if (!entity.Value.spawnClaimed && entity.Value.spawnSourceEntityId != -1) {
                        var spawner = entity.Value.spawnSourceEntityId;
                        if (context.Entities.entities.ContainsKey(spawner)) {
                            var grandparent = context.Entities.entities[spawner].spawnSourceEntityId;
                            if (grandparent != -1) {
                                if (context.Entities.entities.ContainsKey(grandparent)) {
                                    var grandparentData = context.Entities.entities[grandparent];
                                    if ((Dirty && grandparentData.name.Contains("workstation_plate_return")) ||
                                        (!Dirty && grandparentData.name.Contains("DryingPart"))) {
                                        entity.Value.spawnClaimed = true;
                                        context.EntityIdReturnValueForAction[state.ActionId] = Dirty ? spawner : entity.Key;
                                        yield break;
                                    }
                                }
                            }
                        }
                    }
                }
                yield return null;
            }
        }
    }
}