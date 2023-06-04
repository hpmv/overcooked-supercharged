using System;
using System.Numerics;

namespace Hpmv {
    public class PilotRotationAction : GameAction {
        public IEntityReference PilotRotationEntity { get; set; }
        public float TargetAngle { get; set; }
        public float AngleAllowance { get; set; } = 90f /* speed */ * 0.017f /* frame time */;

        public override string Describe()
        {
            return $"Rotate {PilotRotationEntity} to {TargetAngle}°";
        }

        public override GameActionOutput Step(GameActionInput input)
        {
            if (input.ControllerState.SecondaryButtonDown) {
                if (input.ControllerState.RequestButtonUp()) {
                    return new GameActionOutput {
                        ControllerInput = new DesiredControllerInput {
                            secondaryUp = true,
                        },
                        Done = true,
                    };
                } else {
                    return new GameActionOutput {};
                }
            }
            var entity = PilotRotationEntity.GetEntityRecord(input);
            if (entity == null) {
                return new GameActionOutput{ };
            }
            var currentAngle = entity.data[input.Frame].pilotRotationAngle;
            var angleDiff = TargetAngle - currentAngle;
            if (angleDiff <= -180) {
                angleDiff += 360;
            }
            if (angleDiff >= 180) {
                angleDiff -= 360;
            }
            if (Math.Abs(angleDiff) <= AngleAllowance) {
                if (input.ControllerState.RequestButtonDown()) {
                    return new GameActionOutput {
                        ControllerInput = new DesiredControllerInput {
                            secondaryDown = true,
                        }
                    };
                } else {
                    return new GameActionOutput {};
                }
            }
            var movementAngle = angleDiff > 0 ? currentAngle : currentAngle - 180;
            var movementAxes = new Vector2(
                (float)Math.Cos(movementAngle * Math.PI / 180f),
                (float)Math.Sin(movementAngle * Math.PI / 180f)
            );
            return new GameActionOutput{
                ControllerInput = new DesiredControllerInput {
                    axes = movementAxes,
                }
            };
        }
    }
}