using System;
using System.Collections.Generic;
using System.Numerics;

namespace Hpmv {
    public class Humanizer {
        private TimeSpan AXIS_SPEED = TimeSpan.FromMilliseconds(50);
        private TimeSpan BUTTON_MIN_HOLD = TimeSpan.FromMilliseconds(40);
        private TimeSpan BUTTON_COOLDOWN = TimeSpan.FromMilliseconds(70);
        private TimeSpan TWO_BUTTON_COOLDOWN = TimeSpan.FromMilliseconds(500);

        private DateTime prevTime = DateTime.Now;
        private TimeSpan axesElapsed = TimeSpan.Zero;
        private Vector2 prevAxes = Vector2.Zero;
        private TimeSpan buttonCooldown = TimeSpan.Zero;
        private TimeSpan pickupCooldown = TimeSpan.Zero;

        public void AdvanceFrame() {
            //TimeSpan elapsed = DateTime.Now - prevTime;
            TimeSpan elapsed = TimeSpan.FromMilliseconds(1000.0 / 60);
            axesElapsed = elapsed;
            buttonCooldown -= elapsed;
            prevTime += elapsed;
            pickupCooldown -= elapsed;
        }

        public Vector2 HumanizeAxes(Vector2 axes) {
            Vector2 direction = axes - prevAxes;
            if (direction.Length() <= 2.0 / AXIS_SPEED.Milliseconds * axesElapsed.Milliseconds) {
                prevAxes = axes;
            } else {
                prevAxes = direction / direction.Length() * (2.0f / AXIS_SPEED.Milliseconds * axesElapsed.Milliseconds) + prevAxes;
            }
            return prevAxes;
        }

        public IEnumerable<ButtonOutput> PressButtonForPickup() {
            while (buttonCooldown > TimeSpan.Zero || pickupCooldown > TimeSpan.Zero) {
                yield return new ButtonOutput();
            }
            var curTime = prevTime;
            bool first = true;
            pickupCooldown = TWO_BUTTON_COOLDOWN;
            while (prevTime - curTime < BUTTON_MIN_HOLD) {
                yield return new ButtonOutput { isDown = true, justPressed = first, justReleased = false };
                first = false;
            }
            buttonCooldown = BUTTON_COOLDOWN;
            yield return new ButtonOutput { isDown = false, justPressed = false, justReleased = true };
        }

        public IEnumerable<ButtonOutput> PressButton(Func<bool> canRelease) {
            while (buttonCooldown > TimeSpan.Zero) {
                yield return new ButtonOutput();
            }
            var curTime = prevTime;
            bool first = true;
            while (prevTime - curTime < BUTTON_MIN_HOLD || !canRelease()) {
                yield return new ButtonOutput { isDown = true, justPressed = first, justReleased = false };
                first = false;
            }
            buttonCooldown = BUTTON_COOLDOWN;
            yield return new ButtonOutput { isDown = false, justPressed = false, justReleased = true };
        }
    }
}