using System.Numerics;

namespace Hpmv
{
    public record struct ActualControllerInput
    {
        public Vector2 axes;
        public ButtonOutput primary;
        public ButtonOutput secondary;
        public ButtonOutput dash;

        public override string ToString()
        {
            return $"{axes}, {primary}, {secondary}, {dash}";
        }

        public Save.ActualControllerInput ToProto()
        {
            return new Save.ActualControllerInput
            {
                Axes = axes.ToProto(),
                Primary = primary.ToProto(),
                Secondary = secondary.ToProto(),
                Dash = dash.ToProto()
            };
        }
    }

    public static class ActualControllerInputFromProto
    {
        public static ActualControllerInput FromProto(this Save.ActualControllerInput input)
        {
            return new ActualControllerInput
            {
                axes = input.Axes.FromProto(),
                primary = input.Primary.FromProto(),
                secondary = input.Secondary.FromProto(),
                dash = input.Dash.FromProto()
            };
        }
    }
}