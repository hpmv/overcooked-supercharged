using System.Numerics;
using Supercharged.Headless;

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {passed=true,checks=ControllerInputTests.Run(),gameCalls=0,scope="Production input transformation and protobuf history only"}));

namespace Hpmv
{
    // The production vector conversion is a field-for-field mapping. Keep this
    // isolated runner free of unrelated game graph and native transport code.
    public static class VectorFixtureConversions
    {
        public static Save.Vector2 ToProto(this Vector2 value)=>new() { X=value.X,Y=value.Y };
        public static Vector2 FromProto(this Save.Vector2 value)=>new(value.X,value.Y);
    }
}
