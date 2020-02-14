using System.Collections.Generic;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class WaitForCookedAction : GameAction {
        public EntityToken Entity { get; set; }

        public void InitializeState(GameActionState state) {
            state.Action = this;
            state.Description = $"Wait for {Entity} to be cooked";
        }

        public IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            while (true) {
                var entityId = Entity.GetEntityId(context);
                var cookingState = (CookingStateMessage) context.Entities.entities[entityId].data[EntityType.CookingState];
                if (cookingState.m_cookingState == CookingUIController.State.Completed || cookingState.m_cookingState == CookingUIController.State.OverDoing) {
                    yield return null;
                    yield return null;
                    yield return null;
                    yield break;
                }
                yield return null;
            }
        }
    }

    public class WaitForMixedAction : GameAction {
        public EntityToken Entity { get; set; }

        public void InitializeState(GameActionState state) {
            state.Action = this;
            state.Description = $"Wait for {Entity} to be mixed";
        }

        public IEnumerator<ControllerInput> Perform(GameActionState state, GameActionContext context) {
            while (true) {
                var entityId = Entity.GetEntityId(context);
                var mixingState = (MixingStateMessage) context.Entities.entities[entityId].data[EntityType.MixingState];
                if (mixingState.m_mixingState == CookingUIController.State.Completed || mixingState.m_mixingState == CookingUIController.State.OverDoing) {
                    yield return null;
                    yield return null;
                    yield return null;
                    yield break;
                }
                yield return null;
            }
        }
    }
}