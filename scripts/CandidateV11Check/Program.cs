using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try
{
    JsonObject Load(string name) => JsonNode.Parse(File.ReadAllText("artifacts/" + name + ".json"))!.AsObject();
    int total = 0;
    void Passed(string name, int count) { Console.WriteLine(name + ": " + count + " assertions passed"); total += count; }
    Passed("Raw sausage reserve", CarnivalPlanner.SausageBufferSelfTest(Load("v9-sausage-buffer-gf7454"), Load("v7-unplated-lease-gf1108")));
    Passed("Shared pantry chopping", CarnivalPlanner.SharedPantrySelfTest(Load("v7-pantry-placement-gf684")));
    Passed("Imminent FIFO service wait", CarnivalPlanner.ImminentHeadSelfTest(Load("v9-service-boundary-gf4883"), Load("v9-service-boundary-gf8425"), Load("v9-service-boundary-gf8428")));
    Passed("FIFO washer and native ruin", CarnivalPlanner.FifoSafetySelfTest(Load("cycle-start"), Load("v10-fryer-8-cooked-gf2334"), Load("v10-fryer-8-burnt-gf2935")));
    Passed("Native fryer rescue", CarnivalPlanner.FryerRescueSelfTest(Load("v10-fryer-rescue-opportunity-gf2336")));
    Console.WriteLine("Total new V11 feature assertions: " + total);
}
catch (Exception error) { Console.Error.WriteLine(error); Environment.ExitCode = 1; }
