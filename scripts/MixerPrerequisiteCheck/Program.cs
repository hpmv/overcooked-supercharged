using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
var reports = new Dictionary<string,int>
{
    ["mixerPrerequisites"] = CarnivalPlanner.MixerPrerequisiteSelfTest(
        Read("artifacts/v14-seed59-partial-mixer-gf9469.json"),
        Read("artifacts/v14-seed59-prerequisite-context-gf9616.json"),
        Read("artifacts/v14-seed59-partial-mixer-gf9929.json")),
    ["existingHeatArbitration"] = CarnivalPlanner.HeatArbitrationSelfTest(
        Read("artifacts/v11-heat-boundary-gf2384.json"),Read("artifacts/v11-heat-boundary-gf2489.json"),
        Read("artifacts/v11-heat-boundary-gf2604.json"),Read("artifacts/cycle-start.json")),
    ["nearReadyDough"] = CarnivalPlanner.NearReadyDoughSelfTest(Read("artifacts/v12-heat-boundary-gf892.json")),
    ["existingPlanner"] = CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json")),
};
Console.WriteLine(JsonSerializer.Serialize(new { ok=true, reports, total=reports.Values.Sum(), qualification="Offline captured-state and explicitly synthetic mutation tests; no native run or score qualification." },new JsonSerializerOptions{WriteIndented=true}));
