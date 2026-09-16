using System.Text.Json.Nodes;
using System.IO.Compression;
using OvercookedTAS.Controller;
JsonObject Read(string name) => JsonNode.Parse(File.ReadAllText(Path.Combine("artifacts", name + ".json")))!.AsObject();
Console.WriteLine("Existing sausage lifecycle: " + CarnivalPlanner.SausageBufferSelfTest(Read("v9-sausage-buffer-gf7454"), Read("v7-unplated-lease-gf1108")));
var evidence = new JsonObject();
Console.WriteLine("Fixed transfer budget: " + CarnivalPlanner.SausageTransferBudgetSelfTest(Read("v18-buffer1-gf2497"), Read("v18-buffer1-gf2738"), evidence));
IEnumerable<JsonObject> NativeResponses()
{
    using var file = File.OpenRead("artifacts/v18-buffer1-transfer-native-fixtures.jsonl.gz");
    using var gzip = new GZipStream(file, CompressionMode.Decompress); using var reader = new StreamReader(gzip);
    while (reader.ReadLine() is { } line) yield return JsonNode.Parse(line)!["response"]!.AsObject();
}
Console.WriteLine("Recorded transfer samples: " + CarnivalPlanner.SausageTransferNativeTraceSelfTest(NativeResponses()));
Console.WriteLine(evidence.ToJsonString());
