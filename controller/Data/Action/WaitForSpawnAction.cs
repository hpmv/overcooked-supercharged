using System.Collections.Generic;

namespace Hpmv {
    class WaitForSpawnAction : GameAction {
        public EntityToken SourceEntity;
        public string Label { get; set; }

        public override string Describe() {
            return $"Spawn {Label} from {SourceEntity}";
        }

        public override void InitializeState(GameActionState state) {
            state.Action = this;
        }
        public override IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var sourceId = SourceEntity.GetEntityId(context);
            while (true) {
                foreach (var entity in context.Entities.entities) {
                    if (!entity.Value.spawnClaimed && entity.Value.spawnSourceEntityId == sourceId) {
                        entity.Value.spawnClaimed = true;
                        context.EntityIdReturnValueForAction[state.ActionId] = entity.Key;
                        yield break;
                    }
                }
                yield return null;
            }
        }
    }
}