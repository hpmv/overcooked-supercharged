using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public interface EntityToken {
        int GetEntityId(GameActionContext context);
    }

    public class LiteralEntityToken : EntityToken {
        public int EntityId;

        public LiteralEntityToken(int entityId) {
            EntityId = entityId;
        }

        public int GetEntityId(GameActionContext context) {
            return EntityId;
        }

        public override string ToString() {
            return $"#{EntityId}";
        }
    }

    public class SpawnedEntityToken : EntityToken {
        public int ActionId;

        public SpawnedEntityToken(int actionId) {
            ActionId = actionId;
        }

        public int GetEntityId(GameActionContext context) {
            return context.EntityIdReturnValueForAction[ActionId];
        }
        public override string ToString() {
            return $"?{ActionId}";
        }
    }
}