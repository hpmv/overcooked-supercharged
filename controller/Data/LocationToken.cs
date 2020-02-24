using System;
using System.Numerics;

namespace Hpmv {
    public interface LocationToken {
        Vector2[] GetLocation(GameActionInput input);
    }

    public class EntityLocationToken : LocationToken {
        public IEntityReference entity;

        public EntityLocationToken(IEntityReference entity) {
            this.entity = entity;
        }

        public Vector2[] GetLocation(GameActionInput input) {
            return new[] { entity.GetEntityRecord(input).position[input.Frame].XZ() };
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

        public Vector2[] GetLocation(GameActionInput input) {
            return new[] { location };
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

        public Vector2[] GetLocation(GameActionInput input) {
            return new[] { input.Map.GridPos(x, y) };
        }

        public override string ToString() {
            return $"({x}, {y})";
        }
    }

    public class InteractionPointsLocationToken : LocationToken {
        public IEntityReference entity;

        public InteractionPointsLocationToken(IEntityReference entity) {
            this.entity = entity;
        }

        public Vector2[] GetLocation(GameActionInput input) {
            return input.Map.GetInteractionPointsForBlockEntity(entity.GetEntityRecord(input).position[input.Frame].XZ()).ToArray();
        }

        public override string ToString() {
            return "around " + entity;
        }
    }
}