using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    var snapshots = args.Select(path => JsonNode.Parse(File.ReadAllText(path))!.AsObject()).ToArray();
    Console.WriteLine($"FIFO washer return and native ruined-food safety: {CarnivalPlanner.FifoSafetySelfTest(snapshots[0], snapshots[1], snapshots[2])} checks passed.");
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
