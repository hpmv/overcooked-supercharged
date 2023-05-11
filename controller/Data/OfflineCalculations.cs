using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Team17.Online.Multiplayer.Messaging;

namespace Hpmv {
    public class OfflineCalculations {
        public static (Vector3 pos, Vector3 velocity, Vector2 fwd) PredictChefPositionAfterInput(ChefState chefState, Vector3 position, Vector3 velocity, GameMap map, ActualControllerInput input) {
            var newPosition = CalculateNewChefPositionAfterMovement(position, velocity, map);
            var (newVelocity, forward) = CalculateNewChefVelocityAndForward(chefState, input);
            return (newPosition, newVelocity, forward);
        }

        public static ChefState CalculateHighlightedObjects(Vector3 position, Vector2 forward, GameMapGeometry geometry, IEnumerable<GameEntityRecord> entities) {
            // Find objects:
            //   - Grid objects:
            //       - Get discrete grid pos of chef, then look at four neighbors, score by
            //         how much the chef is facing the object - objects more than 135 degrees
            //         away are considered the same.
            //
            //   - Non-grid objects:
            //       - Scan around as a sphere, then check intersection with a forward arc.
            //
            // Finally, filter for objects that are interactable / placable, and then pick
            // the closest one by position.

            // TODO. We'll only do grid selection for now. Much easier.
            var coord = geometry.CoordsToGridPosRounded(position.XZ());
            GameEntityRecord bestGrid = null;
            double bestDot = -1;
            foreach (var entity in entities) {
                if (entity.chefState != null) {
                    continue;
                }
                if (entity.prefab.Ignore) {
                    continue;
                }
                if (entity.prefab.Name == "Dirty Plate") {
                    // This is a bug - in real simulation, somehow I couldn't get dirty plates to destroy themselves.
                    continue;
                }
                if (!entity.IsGridOccupant()) {
                    continue;
                }
                foreach (var occupiedGridOffset in entity.prefab.OccupiedGridPoints) {
                    var entityCoord = geometry.CoordsToGridPosRounded(entity.position.Last().XZ() + occupiedGridOffset);
                    if (Math.Abs(entityCoord.x - coord.x) + Math.Abs(entityCoord.y - coord.y) == 1) {
                        if (entity.data.Last().attachmentParent == null) {
                            var gridOffsetDirection = new Vector2(1.0f * (entityCoord.x - coord.x), -1.0f * (entityCoord.y - coord.y));
                            var dot = Vector2.Dot(gridOffsetDirection, forward);
                            if (bestGrid == null || dot > bestDot) {
                                bestGrid = entity;
                                bestDot = dot;
                            }
                        }
                    }
                }
            }

            var eligible = new List<GameEntityRecord>();
            if (bestGrid != null) {
                eligible.Add(bestGrid);
            }

            foreach (var entity in entities) {
                if (entity.chefState != null) {
                    continue;
                }
                if (entity.prefab.Ignore) {
                    continue;
                }
                if (entity.IsGridOccupant()) {
                    continue;
                }
                if (entity.data.Last().attachmentParent != null) {
                    continue;
                }
                if ((entity.position.Last() - position).LengthSquared() > 4) {
                    continue;
                }
                var dist = (entity.position.Last() - position).XZ().LengthSquared();
                if (dist > 1.36f * 1.36f) {
                    continue;
                }
                if (Vector2.Dot((entity.position.Last() - position).XZ(), forward) < 0) {
                    continue;
                }
                eligible.Add(entity);
            }
            var best = eligible.OrderBy(entity => (entity.position.Last() - position).LengthSquared()).FirstOrDefault();

            var chefState = new ChefState();
            chefState.highlightedForPickup = best;
            chefState.highlightedForPlacement = best;
            chefState.highlightedForUse = null;
            if (best != null) {
                if (best.prefab.IsBoard) {
                    if (best.data.Last().attachment is GameEntityRecord attachment && attachment.prefab.IsChoppable && attachment.prefab.MaxProgress > 0) {
                        chefState.highlightedForUse = best;
                    }
                } else if (best.prefab.IsWashingStation) {
                    if (best.data.Last().numPlates > 0) {
                        chefState.highlightedForUse = best;
                    }
                }
            }
            return chefState;
        }

        public static (Vector3 velocity, Vector2 forward) CalculateNewChefVelocityAndForward(ChefState chefState, ActualControllerInput input) {
            var axes = new Vector2(input.axes.X, -input.axes.Y);
            if (axes.Length() < 0.01) {
                axes = default;
            } else {
                axes = Vector2.Normalize(axes);
            }
            var newForward = CalculateNewChefRotation(chefState.forward, axes);
            // Skipping movement suppression, gravity force, wind force.
            var desiredVelocity = axes * 6;  // run speed
            if (chefState.isAiming) {
                desiredVelocity = default;
            }
            if (chefState.dashTimer > 0) {
                var dashVelocity = newForward * 18; // dash speed
                var ratio = MathUtils.SinusoidalSCurve((float)(chefState.dashTimer / 0.3));  // dash time
                desiredVelocity = (1 - ratio) * desiredVelocity + ratio * dashVelocity;
            }
            // skipping impact timer
            if (Vector2.Distance(desiredVelocity, default) > 18) { // max speed
                desiredVelocity = Vector2.Normalize(desiredVelocity) * 18;
            }
            // Ignoring ProgressVelocityWrtFriction.
            return (desiredVelocity.ToXZVector3(), newForward);
        }

        public static Vector2 CalculateNewChefRotation(Vector2 forward, Vector2 input) {
            if (input != default) {
                var cross = forward.X * input.Y - input.X * forward.Y;
                var dot = Vector2.Dot(input, forward);
                if (Math.Abs(cross) < 1e-6) {
                    cross = 1;
                }
                var maxAngle = Math.Acos(Math.Clamp(dot, -1, 1));
                var angle = Math.Min(20 * (1.0 / Config.FRAMERATE), maxAngle);
                var actualAngle = (float)(cross > 0 ? angle : -angle);
                var newForward = Vector2.TransformNormal(forward, Matrix3x2.CreateRotation(actualAngle));
                return newForward;
            }
            return forward;
        }

        public static Vector3 CalculateNewChefPositionAfterMovement(Vector3 position, Vector3 velocity, GameMap map) {
            var desired = position + velocity * (1.0f / Config.FRAMERATE);
            if (!map.IsInsideMap(desired.XZ())) {
                desired = position + new Vector3(velocity.X, 0, 0) * (1.0f / Config.FRAMERATE);
                if (!map.IsInsideMap(desired.XZ())) {
                    desired = position + new Vector3(0, 0, velocity.Z) * (1.0f / Config.FRAMERATE);
                    if (!map.IsInsideMap(desired.XZ())) {
                        desired = position;
                    }
                }
            }
            return desired;
        }
    }
}