using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
JsonObject Read(string p)=>JsonNode.Parse(File.ReadAllText(p))!.AsObject();
try {
int checks=CarnivalPlanner.NearReadyPotSelfTest(Read("artifacts/near-ready-pot-audit/native-round-v14-gf789.json"),Read("artifacts/near-ready-pot-audit/native-round-v14-gf843.json"));
var related=new JsonObject {
 ["potRescue"]=CarnivalPlanner.PotRescueSelfTest(Read("artifacts/v11-heat-boundary-gf2384.json")),
 ["stagedRelease"]=CarnivalPlanner.StagedReleaseSelfTest(Read("artifacts/v7-unplated-lease-gf958.json"),Read("artifacts/v7-unplated-lease-gf1038.json"),Read("artifacts/v7-unplated-lease-gf1071.json"),Read("artifacts/v7-unplated-lease-gf1127.json")),
 ["planner"]=CarnivalPlanner.SelfTest(Read("artifacts/cycle-start.json")),
 ["directOnion"]=CarnivalPlanner.DirectEarlyOnionCounterSelfTest(Read("artifacts/v14-direct-onion-counter-gf3581.json")),
 ["safeService"]=CarnivalPlanner.SafeServiceHeatSelfTest(Read("artifacts/v14-safe-service-gf1872.json")) };
var report=new JsonObject{["ok"]=true,["nearReadyPot"]=checks,["related"]=related,["assemblySha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CarnivalPlanner).Assembly.Location))),["qualification"]="Captured native admission plus explicit offline lifecycle mutations; no new native execution or time-saving claim."};
File.WriteAllText("artifacts/near-ready-pot-check/tests.json",report.ToJsonString());Console.WriteLine(report);
}catch(Exception error){Console.Error.WriteLine(error);Environment.ExitCode=1;}
