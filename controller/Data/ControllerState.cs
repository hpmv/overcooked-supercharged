using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public struct ControllerState {
        private static TimeSpan AXIS_SPEED = TimeSpan.FromMilliseconds(50);
        private static TimeSpan BUTTON_MIN_HOLD = TimeSpan.FromMilliseconds(0);
        private static TimeSpan BUTTON_COOLDOWN = TimeSpan.FromMilliseconds(0);
        private static TimeSpan PICKUP_COOLDOWN = TimeSpan.FromMilliseconds(500);
        private static TimeSpan AXIS_COOLDOWN = TimeSpan.FromMilliseconds(0);

        private static ControllerState Initial {
            get {
                return new ControllerState {
                    buttonCooldown = TimeSpan.Zero,
                    pickupCooldown = TimeSpan.Zero,
                    primaryButtonDown = false,
                    secondaryButtonDown = false,
                    buttonDownDurationLeft = TimeSpan.Zero,
                };
            }
        }

        public Vector2 axes;
        public TimeSpan buttonCooldown;
        public TimeSpan pickupCooldown;
        public bool primaryButtonDown;
        public bool secondaryButtonDown;
        public TimeSpan buttonDownDurationLeft;
        public Vector2 axesVelocity;
        public Vector2 axesAcceleration;
        public TimeSpan axesCooldown;
        public bool axesDown;

        private const float desiredAccel = 1500;
        private const float accelerationDeltaSpeed = 80000;
        private const float axesRestoreSpeed = 20;

        private ControllerState AdvanceFrame() {
            //TimeSpan elapsed = DateTime.Now - prevTime;
            TimeSpan elapsed = TimeSpan.FromMilliseconds(1000.0 / Config.FRAMERATE);
            var copy = this;
            copy.buttonCooldown -= elapsed;
            copy.pickupCooldown -= elapsed;
            copy.buttonDownDurationLeft -= elapsed;
            copy.axesCooldown -= elapsed;
            return copy;
        }

        private void HumanizeAxes(Vector2 newAxes) {
            // var elapsed = 1.0f / Config.FRAMERATE;
            // bool shouldHold = newAxes.Length() > 0.01;
            // if (shouldHold && (axesDown || axesCooldown <= TimeSpan.Zero)) {
            //     axesDown = true;
            //     var targetPos = newAxes * 2f;
            //     var targetAccel = Vector2.Normalize(targetPos - axes) * desiredAccel;
            //     var accelDelta = targetAccel - axesAcceleration;
            //     Vector2 actualAccel;
            //     if (accelDelta.Length() <= accelerationDeltaSpeed * elapsed) {
            //         actualAccel = targetAccel;
            //     } else {
            //         actualAccel = axesAcceleration + Vector2.Normalize(accelDelta) * (accelerationDeltaSpeed * elapsed);
            //     }
            //     axesAcceleration = actualAccel;
            //     // if (axes.Length() >= 0.99) {
            //     //     var tangentAccel = axesAcceleration - Vector2.Dot(axesAcceleration, Vector2.Normalize(axes)) * axes;
            //     //     axesAcceleration = (axesAcceleration - tangentAccel) + tangentAccel * 0.5f;  // friction.
            //     // }
            //     axesVelocity += axesAcceleration * elapsed;
            //     axes += axesVelocity * elapsed;
            //     if (axes.Length() > 1) {
            //         axes = Vector2.Normalize(axes);
            //         axesVelocity = axesVelocity - Vector2.Dot(axesVelocity, axes) * axes;
            //         if ((axes - newAxes).Length() < 0.1) {
            //             axes = newAxes;
            //             axesVelocity = default;
            //         }
            //     }
            // }
            // if (!shouldHold && axesDown) {
            //     axesDown = false;
            //     axesCooldown = AXIS_COOLDOWN;
            // }

            // if (!axesDown) {
            //     if (axes.Length() > axesRestoreSpeed * elapsed) {
            //         axes -= Vector2.Normalize(axes) * (axesRestoreSpeed * elapsed);
            //     } else {
            //         axes = Vector2.Zero;
            //     }
            //     axesAcceleration = Vector2.Zero;
            //     axesVelocity = Vector2.Zero;
            // }

            // TimeSpan elapsed = TimeSpan.FromMilliseconds(1000.0 / Config.FRAMERATE);
            // Vector2 direction = newAxes - axes;
            // if (direction.Length() <= 2.0 / AXIS_SPEED.Milliseconds * elapsed.Milliseconds) {
            //     axes = newAxes;
            // } else {
            //     axes = direction / direction.Length() * (2.0f / AXIS_SPEED.Milliseconds * elapsed.Milliseconds) + axes;
            // }
            axes = newAxes;
        }

        public bool PrimaryButtonDown { get { return primaryButtonDown; } }
        public bool SecondaryButtonDown { get { return secondaryButtonDown; } }

        public bool RequestButtonDown() {
            if (PrimaryButtonDown || SecondaryButtonDown) {
                return false;
                // throw new InvalidOperationException("Cannot request button down when any button is down");
            }
            if (buttonCooldown > TimeSpan.Zero) {
                return false;
            }
            return true;
        }

        public bool RequestButtonUp() {
            if (!PrimaryButtonDown && !SecondaryButtonDown) {
                return false;
                // throw new InvalidOperationException("Cannot request button up unless button is down");
            }
            if (buttonDownDurationLeft > TimeSpan.Zero) {
                return false;
            }
            return true;
        }

        public bool RequestButtonDownForPickup() {
            if (PrimaryButtonDown || SecondaryButtonDown) {
                return false;
                // throw new InvalidOperationException("Cannot request button down when button is down");
            }
            if (buttonCooldown > TimeSpan.Zero || pickupCooldown > TimeSpan.Zero) {
                return false;
            }
            return true;
        }

        public (ControllerState, ActualControllerInput) ApplyInputAndAdvanceFrame(DesiredControllerInput input) {
            var output = new ActualControllerInput();
            var copy = this;
            copy.HumanizeAxes(input.axes);
            output.axes = copy.axes;

            if (input.primaryDown) {
                output.primary.justPressed = true;
                copy.primaryButtonDown = true;
                copy.buttonDownDurationLeft = BUTTON_MIN_HOLD;
                if (input.primaryDownIsForPickup) {
                    copy.pickupCooldown = PICKUP_COOLDOWN;
                }
            } else if (input.secondaryDown) {
                output.secondary.justPressed = true;
                copy.secondaryButtonDown = true;
                copy.buttonDownDurationLeft = BUTTON_MIN_HOLD;
            } else if (input.primaryUp) {
                output.primary.justReleased = true;
                copy.primaryButtonDown = false;
                copy.buttonCooldown = BUTTON_COOLDOWN;
            } else if (input.secondaryUp) {
                output.secondary.justReleased = true;
                copy.secondaryButtonDown = false;
                copy.buttonCooldown = BUTTON_COOLDOWN;
            }
            output.primary.isDown = copy.primaryButtonDown;
            output.secondary.isDown = copy.secondaryButtonDown;
            output.dash.justPressed = input.dash;
            return (copy.AdvanceFrame(), output);
        }

        public Save.ControllerState ToProto() {
            return new Save.ControllerState {
                Axes = axes.ToProto(),
                ButtonCooldown = buttonCooldown.TotalMilliseconds,
                PickupCooldown = pickupCooldown.TotalMilliseconds,
                PrimaryButtonDown = primaryButtonDown,
                SecondaryButtonDown = secondaryButtonDown,
                ButtonDownDurationLeft = buttonDownDurationLeft.TotalMilliseconds,
                AxesVelocity = axesVelocity.ToProto(),
                AxesAcceleration = axesAcceleration.ToProto(),
                AxesDown = axesDown,
                AxesCooldown = axesCooldown.TotalMilliseconds,
            };
        }
    }

    public static class ControllerStateFromProto {
        public static ControllerState FromProto(this Save.ControllerState state) {
            return new ControllerState {
                axes = state.Axes.FromProto(),
                buttonCooldown = TimeSpan.FromMilliseconds(state.ButtonCooldown),
                pickupCooldown = TimeSpan.FromMilliseconds(state.PickupCooldown),
                primaryButtonDown = state.PrimaryButtonDown,
                secondaryButtonDown = state.SecondaryButtonDown,
                buttonDownDurationLeft = TimeSpan.FromMilliseconds(state.ButtonDownDurationLeft),
                axesVelocity = state.AxesVelocity?.FromProto() ?? default,
                axesAcceleration = state.AxesAcceleration?.FromProto() ?? default,
                axesDown = state.AxesDown,
                axesCooldown = TimeSpan.FromMilliseconds(state.AxesCooldown),
            };
        }
    }
}