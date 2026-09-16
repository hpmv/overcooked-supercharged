using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
    Console.WriteLine("Washer plating: " + CarnivalPlanner.WasherPlatingSelfTest(Read("washer-plating-gf1143"), Read("washer-plating-gf1159"), Read("washer-plating-gf1194"), Read("washer-plating-gf1202"), Read("washer-plating-gf1211"), Read("washer-plating-gf1299")));
    Console.WriteLine("Planner: " + CarnivalPlanner.SelfTest(Read("cycle-start")));
    Console.WriteLine("Intercepted pot: " + CarnivalPlanner.InterceptedPotSelfTest(Read("v14-held443-gf15497"), Read("v14-held443-gf15519"), Read("v14-held443-gf15520")));
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
