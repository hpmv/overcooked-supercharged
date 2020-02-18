using System.Numerics;

namespace Hpmv {
    public interface LocationToken {
        Vector2[] GetLocation(GameActionContext context);
    }

    public class EntityLocationToken : LocationToken {
        public EntityToken entity;

        public EntityLocationToken(EntityToken entity) {
            this.entity = entity;
        }

        public Vector2[] GetLocation(GameActionContext context) {
            return new [] { context.Entities.entities[entity.GetEntityId(context)].pos.XZ() };
        }

        public override string ToString() {
            return entity.ToString();
        }
    }

    public class LiteralLocationToken : LocationToken {
        public Vector2 location;

        public LiteralLocationToken(Vector2 location) {
            this.location = location;
        }

        public Vector2[] GetLocation(GameActionContext context) {
            return new [] { location };
        }

        public override string ToString() {
            return "coord: " + location;
        }
    }

    public class GridPosLocationToken : LocationToken {
        public int x;
        public int y;

        public GridPosLocationToken(int x, int y) {
            this.x = x;
            this.y = y;
        }

        public Vector2[] GetLocation(GameActionContext context) {
            return new [] { context.Map.GridPos(x, y) };
        }

        public override string ToString() {
            return $"({x}, {y})";
        }
    }

    public class InteractionPointsLocationToken : LocationToken {
        public EntityToken entity;

        public InteractionPointsLocationToken(EntityToken entity) {
            this.entity = entity;
        }

        public Vector2[] GetLocation(GameActionContext context) {
            return context.Map.GetInteractionPointsForBlockEntity(context.Entities.entities[entity.GetEntityId(context)].pos.XZ()).ToArray();
        }

        public override string ToString() {
            return "around " + entity;
        }
    }
}