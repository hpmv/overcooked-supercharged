using System.Collections.Generic;

namespace Hpmv {
    class WaitForSpawnAction : GameAction {
        public EntityToken SourceEntity;

        public void InitializeState(GameActionState state) {
            state.Action = this;
            state.Description = $"Wait for item to spawn from {SourceEntity}";
        }
        public IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            var sourceId = SourceEntity.GetEntityId(context);
            state.Description = $"Wait for item to spawn from {sourceId}";
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