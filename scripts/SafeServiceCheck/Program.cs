using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;
JsonObject Read(string path)=>JsonNode.Parse(File.ReadAllText(path))!.AsObject();
try {
var evidence=new JsonObject();
int checks=CarnivalPlanner.SafeServiceHeatSelfTest(Read("artifacts/v14-safe-service-gf1872.json"),evidence);
var related=new JsonObject {
 ["nearReadyDough"]=CarnivalPlanner.NearReadyDoughSelfTest(Read("artifacts/v12-heat-boundary-gf892.json")),
 ["cannonFlight"]=CarnivalPlanner.CannonFlightSelfTest(Read("artifacts/v7-cannon-boundary-postneutral-gf1287.json")),
 ["heatArbitration"]=CarnivalPlanner.HeatArbitrationSelfTest(Read("artifacts/v11-heat-boundary-gf2384.json"),Read("artifacts/v11-heat-boundary-gf2489.json"),Read("artifacts/v11-heat-boundary-gf2604.json"),Read("artifacts/cycle-start.json")),
 ["directEarlyOnion"]=CarnivalPlanner.DirectEarlyOnionSelfTest(Read("artifacts/v13-onion-direct-admission-gf4216.json"),Read("artifacts/v13-onion-direct-admission-gf2901.json")),
 ["ordinaryCounterOnion"]=CarnivalPlanner.DirectEarlyOnionCounterSelfTest(Read("artifacts/v14-direct-onion-counter-gf3581.json")) };
var report=new JsonObject{["ok"]=true,["safeServiceHeat"]=checks,["related"]=related,["assemblySha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(typeof(CarnivalPlanner).Assembly.Location))),["qualification"]="Captured native admission and offline lifecycle mutations; no alternative native timing result."};
File.WriteAllText("artifacts/safe-service-check/tests.json",report.ToJsonString());File.WriteAllText("artifacts/safe-service-check/captured-budget.json",evidence.ToJsonString(new(){WriteIndented=true}));Console.WriteLine(report);
} catch(Exception error) { Console.Error.WriteLine(error); Environment.ExitCode=1; }
