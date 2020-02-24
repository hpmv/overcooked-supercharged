using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public struct ControllerState {
        private static TimeSpan AXIS_SPEED = TimeSpan.FromMilliseconds(50);
        private static TimeSpan BUTTON_MIN_HOLD = TimeSpan.FromMilliseconds(40);
        private static TimeSpan BUTTON_COOLDOWN = TimeSpan.FromMilliseconds(70);
        private static TimeSpan PICKUP_COOLDOWN = TimeSpan.FromMilliseconds(500);

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

        private Vector2 axes;
        private TimeSpan buttonCooldown;
        private TimeSpan pickupCooldown;
        private bool primaryButtonDown;
        private bool secondaryButtonDown;
        private TimeSpan buttonDownDurationLeft;

        private ControllerState AdvanceFrame() {
            //TimeSpan elapsed = DateTime.Now - prevTime;
            TimeSpan elapsed = TimeSpan.FromMilliseconds(1000.0 / 60);
            var copy = this;
            copy.buttonCooldown -= elapsed;
            copy.pickupCooldown -= elapsed;
            copy.buttonDownDurationLeft -= elapsed;
            return copy;
        }

        private static Vector2 HumanizeAxes(Vector2 axes, Vector2 newAxes) {
            TimeSpan elapsed = TimeSpan.FromMilliseconds(1000.0 / 60);
            Vector2 direction = newAxes - axes;
            if (direction.Length() <= 2.0 / AXIS_SPEED.Milliseconds * elapsed.Milliseconds) {
                axes = newAxes;
            } else {
                axes = direction / direction.Length() * (2.0f / AXIS_SPEED.Milliseconds * elapsed.Milliseconds) + axes;
            }
            return axes;
        }

        public bool PrimaryButtonDown { get { return primaryButtonDown; } }
        public bool SecondaryButtonDown { get { return secondaryButtonDown; } }

        public bool RequestButtonDown() {
            if (PrimaryButtonDown || SecondaryButtonDown) {
                throw new InvalidOperationException("Cannot request button down when any button is down");
            }
            if (buttonCooldown > TimeSpan.Zero) {
                return false;
            }
            return true;
        }

        public bool RequestButtonUp() {
            if (!PrimaryButtonDown && !SecondaryButtonDown) {
                throw new InvalidOperationException("Cannot request button up unless button is down");
            }
            if (buttonDownDurationLeft > TimeSpan.Zero) {
                return false;
            }
            return true;
        }

        public bool RequestButtonDownForPickup() {
            if (PrimaryButtonDown || SecondaryButtonDown) {
                throw new InvalidOperationException("Cannot request button down when button is down");
            }
            if (buttonCooldown > TimeSpan.Zero || pickupCooldown > TimeSpan.Zero) {
                return false;
            }
            return true;
        }

        public (ControllerState, ActualControllerInput) ApplyInputAndAdvanceFrame(DesiredControllerInput input) {
            var output = new ActualControllerInput();
            var copy = this;
            copy.axes = HumanizeAxes(copy.axes, input.axes);
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
    }
}