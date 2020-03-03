namespace Hpmv {
    public struct ButtonOutput {
        public bool isDown;
        public bool justPressed;
        public bool justReleased;

        public override string ToString() {
            if (isDown || justPressed || justReleased) {
                return (isDown ? "D" : "") + (justPressed ? "P" : "") + (justReleased ? "R" : "");
            }
            return "_";
        }

        public Save.ButtonOutput ToProto() {
            return new Save.ButtonOutput {
                IsDown = isDown,
                JustPressed = justPressed,
                JustReleased = justReleased
            };
        }
    }

    public static class ButtonOutputFromProto {
        public static ButtonOutput FromProto(this Save.ButtonOutput p) {
            return new ButtonOutput {
                isDown = p.IsDown,
                justPressed = p.JustPressed,
                justReleased = p.JustReleased
            };
        }
    }
}