using System.Numerics;

namespace Hpmv {
    public interface LocationToken {
        Vector3 GetLocation(GameActionContext context);
    }

    public class EntityLocationToken : LocationToken {
        public EntityToken entity;

        public EntityLocationToken(EntityToken entity) {
            this.entity = entity;
        }

        public Vector3 GetLocation(GameActionContext context) {
            return context.Entities.entities[entity.GetEntityId(context)].pos;
        }

        public override string ToString() {
            return entity.ToString();
        }
    }

    public class LiteralLocationToken : LocationToken {
        public Vector3 location;

        public LiteralLocationToken(Vector3 location) {
            this.location = location;
        }

        public Vector3 GetLocation(GameActionContext context) {
            return location;
        }

        public override string ToString() {
            return location.ToString();
        }
    }
}