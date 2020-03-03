using System;
using System.Numerics;

namespace Hpmv {
    public interface LocationToken {
        Vector2[] GetLocation(GameActionInput input);
        Save.LocationToken ToProto();
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

        public Save.LocationToken ToProto() {
            return new Save.LocationToken {
                Entity = entity.ToProto()
            };
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

        public Save.LocationToken ToProto() {
            return new Save.LocationToken {
                Literal = location.ToProto()
            };
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

        public Save.LocationToken ToProto() {
            return new Save.LocationToken {
                Grid = new Save.GridPos {
                    X = x,
                    Y = y
                }
            };
        }
    }

    public class InteractionPointsLocationToken : LocationToken {
        public IEntityReference entity;

        public InteractionPointsLocationToken(IEntityReference entity) {
            this.entity = entity;
        }

        public Vector2[] GetLocation(GameActionInput input) {
            var locations = input.Map.GetInteractionPointsForBlockEntity(entity.GetEntityRecord(input).position[input.Frame].XZ()).ToArray();
            // Console.WriteLine($"[{input.Frame}, {this}] {string.Join(" or ", locations)}");
            return locations;
        }

        public override string ToString() {
            return "around " + entity;
        }

        public Save.LocationToken ToProto() {
            throw new NotImplementedException();
        }
    }

    public static class LocationTokenFromProto {
        public static LocationToken FromProto(this Save.LocationToken token, LoadContext context) {
            if (token.KindCase == Save.LocationToken.KindOneofCase.Entity) {
                return new EntityLocationToken(token.Entity.FromProto(context));
            } else if (token.KindCase == Save.LocationToken.KindOneofCase.Literal) {
                return new LiteralLocationToken(token.Literal.FromProto());
            } else {
                return new GridPosLocationToken(token.Grid.X, token.Grid.Y);
            }
        }
    }
}