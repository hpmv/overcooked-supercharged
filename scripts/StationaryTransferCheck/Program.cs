using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;

int[] frames = [435,436,437,438,444,464,465,471,472,473,474,785,786,792,1075,1076,1082,1156,1157,1163];
var captured = frames.ToDictionary(f => f, f => JsonNode.Parse(File.ReadAllText(f is 435 or 436 ? $"artifacts/early-transfer-b-gf{f}.json" : $"artifacts/stationary-transfer-b-gf{f}.json"))!.AsObject());
int focused = RouteRunner.StationaryTransferSelfTest(captured);
int defaults = RouteRunner.StationaryTransferDefaultsSelfTest(captured[437], captured[438]);
int waypoints = NativeWaypointContinuationTests.Run();
Console.WriteLine($"Stationary transfer: {focused}; planner default propagation: {defaults}; existing waypoint guard: {waypoints} checks passed.");
string path = typeof(RouteRunner).Assembly.Location;
Console.WriteLine("Assembly: " + path);
Console.WriteLine("SHA256: " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
