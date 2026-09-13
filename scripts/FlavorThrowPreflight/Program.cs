using System.Security.Cryptography;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;
try {
if (args.Length != 4) throw new ArgumentException("Usage: PLAN INITIAL_SNAPSHOT OBSERVED_CHOPPED_SNAPSHOT OUTPUT");
JsonObject Read(string p) => JsonNode.Parse(File.ReadAllText(p))!.AsObject();
JsonObject State(JsonObject p) => p["state"] as JsonObject ?? p;
int I(JsonNode? n) => n?.GetValue<int>() ?? 0;
string[] Components(JsonObject e) => e["components"]!.AsArray().Select(c => c!.ToString()).ToArray();
void Check(bool v, string m) { if (!v) throw new InvalidOperationException(m); }
string Hash(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
var plan = Read(args[0]); var initial = State(Read(args[1])); var observed = State(Read(args[2])); var model = KitchenModel.Build(initial);
Check(model.Scene == "s_Day_3_4" && initial["chefs"]!.AsArray().Count == 4, "Native Carnival four-chef geometry is required.");
var jobs = plan["jobs"]!.AsArray().OfType<JsonObject>().ToArray();
Check(jobs.Length == 4 && I(plan["timeoutFrames"]) == 3000, "Unexpected authored job count or bounds.");
string flavor = jobs[2]["actions"]![0]!["station"]!.ToString();
int flavorId = flavor == "Chocolate" ? CarnivalRecipes.Chocolate.Id : flavor == "Raspberry" ? CarnivalRecipes.Raspberry.Id : throw new InvalidOperationException("Wrong chopped flavor.");
var actual = observed["entities"]!.AsArray().OfType<JsonObject>().Where(e => {
 var f = CarnivalRecipes.ClassifyEntity(e); return f.EvidenceGaps.Length == 0 && f.Food.Preparation == FoodPreparation.Chopped && f.Food.IngredientIds.SequenceEqual(new[] { flavorId });
}).Single();
Check(Components(actual).Contains("ThrowableItem") && actual["composition"] is JsonObject && actual["throwFlightTime"]!.GetValue<float>() >= 0,
    "The actual chopped native flavor lacks throwable/composition telemetry.");
var entities = initial["entities"]!.AsArray().OfType<JsonObject>().ToDictionary(e => I(e["id"]));
var positions = initial["chefs"]!.AsArray().OfType<JsonObject>().ToDictionary(c => I(c["playerId"]), c => KitchenModel.Position(c["position"]));
var checks = new JsonArray();
for (int i = 0; i < jobs.Length; i++) {
 var job = jobs[i]; int player = I(job["player"]);
 Check(player == (i < 3 ? 1 : 3) && job["dependencies"]!.AsArray().Select(n => n!.ToString()).SequenceEqual(i == 0 ? [] : new[] { jobs[i-1]["id"]!.ToString() }), "Invalid chef/dependency chain.");
 foreach (var a in job["actions"]!.AsArray().OfType<JsonObject>()) {
  string type = a["type"]!.ToString(); Check(I(a["timeoutFrames"]) > 0, "Unbounded native action.");
  if (a["target"] is JsonObject point) { var to = KitchenModel.Position(point); var path = Navigation.FindPath(model, positions[player], to); Check(path.Success, "Unreachable throw staging: " + path.Error); positions[player] = to; continue; }
  var station = model.Resolve(a["station"]!.ToString());
  if (type is not ("throw" or "mix" or "cook")) { var path = Navigation.ToStation(model, positions[player], station); Check(path.Success, "Station path unavailable: " + path.Error); positions[player] = path.Points[^1]; }
  if (type == "throw") {
   int bowl = I(entities[station.EntityId]["attachedEntityId"]); var target = entities[bowl];
   Check(Components(entities[station.EntityId]).Contains("MixingStation") && Components(target).Contains("IngredientCatcher") &&
     Components(target).Contains("MixableContainer"), "Measured mixer does not hold a native catchable bowl.");
   double distance = positions[player].Distance(KitchenModel.Position(target["position"]));
   Check(distance > .25 && distance < 4, "Staging differs from the already observed short near-bowl pantry lane.");
   checks.Add(new JsonObject { ["job"] = job["id"]!.DeepClone(), ["bowlId"] = bowl, ["mixerId"] = station.EntityId, ["horizontalDistance"] = distance });
  }
 }
}
var chef = initial["chefs"]!.AsArray().OfType<JsonObject>().Single(c => I(c["playerId"]) == 1);
var report = new JsonObject { ["ok"] = true, ["classification"] = "Read-only actual static geometry and observed prepared ThrowableItem; native flavor catch still unproven",
 ["flavor"] = flavor, ["flavorId"] = flavorId, ["observedPreparedItem"] = I(actual["id"]), ["observedPreparedOrdinal"] = I(actual["observedOrdinal"]),
 ["nativeThrowForce"] = chef["throwForce"]?.DeepClone(), ["nativeThrowInclination"] = chef["throwInclination"]?.DeepClone(),
 ["nativeVelocityPolicy"] = "ServerAttachmentThrower uses horizontal force and vertical tan(inclination)*force. This is not a collision/catch prediction.",
 ["throws"] = checks, ["planSha256"] = Hash(args[0]), ["initialSnapshotSha256"] = Hash(args[1]), ["preparedSnapshotSha256"] = Hash(args[2]),
 ["classifierSha256"] = Hash(typeof(CarnivalRecipes).Assembly.Location) };
File.WriteAllText(args[3], report.ToJsonString(new(){WriteIndented=true})); Console.WriteLine(report.ToJsonString());
} catch (Exception e) { Console.Error.WriteLine(e); Environment.ExitCode = 1; }
