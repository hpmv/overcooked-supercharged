using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
JsonObject Read(string p)=>JsonNode.Parse(File.ReadAllText(p))!.AsObject();
try
{
    int count=CarnivalPlanner.DirectEarlyOnionSelfTest(Read("artifacts/v13-onion-direct-admission-gf4216.json"),Read("artifacts/v13-onion-direct-admission-gf2901.json"));
    int prior=CarnivalPlanner.EarlyOnionSelfTest(Read("artifacts/cycle-start.json"));
    int counters=CarnivalPlanner.DirectEarlyOnionCounterSelfTest(Read("artifacts/v14-direct-onion-counter-gf3581.json"));
    var report=new JsonObject{["ok"]=true,["directEarlyOnion"]=count,["ordinaryCounterDirectOnion"]=counters,["priorEarlyOnion"]=prior,["assemblySha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(CarnivalPlanner).Assembly.Location))).ToLowerInvariant(),["qualification"]="Offline captured-native admission plus synthetic lifecycle regressions; no native timing claim."};
    File.WriteAllText("artifacts/direct-onion-check/tests.json",report.ToJsonString(new(){WriteIndented=true})+Environment.NewLine);Console.WriteLine(report.ToJsonString());
}
catch(Exception e){Console.Error.WriteLine(e);Environment.ExitCode=1;}
