using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

int I(JsonNode? n) => n is null ? 0 : int.Parse(n.ToString());
double N(JsonNode? n) => n is null ? 0 : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
bool B(JsonNode? n) => n?.ToString() == "true";
string Hash(string path) => Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)));
const string Root = "artifacts/v16-batch-wait-witnesses";
var manifest = JsonNode.Parse(File.ReadAllText(Root + "/manifest.json"))!.AsObject();
var reports = new List<object>();
foreach ((int frame, int nextReady) in new[] { (4304, 4750), (12161, 12604) })
{
    string path = $"{Root}/gf{frame}.json";
    var entry = manifest["snapshots"]!.AsArray().Single(s => I(s!["frame"]) == frame)!;
    if (Hash(path) != entry["responseSha256"]!.ToString()) throw new InvalidOperationException("Snapshot receipt mismatch.");
    var state = JsonNode.Parse(File.ReadAllText(path))!["state"]!.AsObject();
    var entities = state["entities"]!.AsArray().OfType<JsonObject>().Where(e => B(e["active"])).ToArray();
    var chefs = state["chefs"]!.AsArray().OfType<JsonObject>().ToArray();
    var orders = state["orders"]!.AsArray().OfType<JsonObject>().OrderBy(o => I(o["id"])).ToArray();
    var head = orders[0]; double remaining = N(head["remaining"]), lifetime = N(head["lifetime"]);
    int tip = CarnivalRecipes.TipForRemainingFraction(remaining / lifetime);
    double threshold = lifetime * (tip == 8 ? .66 : tip == 5 ? .33 : 0);
    var tokens = new List<object>();
    var tokenIds = new HashSet<int>(); var ordinals = new HashSet<int>();
    foreach (var e in entities)
    {
        var food = CarnivalRecipes.ClassifyEntity(e);
        int id = I(e["id"]), ordinal = I(e["observedOrdinal"]);
        if (!food.IsPlate || food.EvidenceGaps.Length != 0 || food.Food.IsRuined || e["observedOrdinal"] is null || ordinal < 0) continue;
        int parent = entities.Where(p => I(p["attachedEntityId"]) == id).Select(p => I(p["id"])).FirstOrDefault();
        int[] holders = chefs.Where(c => I(c["heldEntityId"]) == id).Select(c => I(c["playerId"])).ToArray();
        if (parent == 0 && holders.Length == 0) continue; // Exclude unowned delivery residue, not a clean token.
        bool clean = food.Food.IngredientIds.Length == 0;
        int[] matches = orders.Where(o => CarnivalRecipes.MatchRecipe(e, I(o["recipeId"])).ReadyToDeliver).Select(o => I(o["id"])).ToArray();
        if (!clean && matches.Length == 0) continue;
        if (!tokenIds.Add(id) || !ordinals.Add(ordinal)) throw new InvalidOperationException("Duplicate native plate identity.");
        tokens.Add(new { plate = id, ordinal, parent, holders, clean, nativeOrderMatches = matches, nativeIngredients = food.Food.IngredientIds });
    }
    if (tokens.Count != 3) throw new InvalidOperationException("Captured three-token capacity changed.");
    double observedWait = (nextReady - frame) / 60d;
    bool SameTip(double wait) => CarnivalRecipes.TipForRemainingFraction(Math.Max(0, remaining - wait - 11) / lifetime) == tip;
    if (SameTip(observedWait)) throw new InvalidOperationException("Expected native tip-band rejection no longer holds.");
    reports.Add(new {
        frame, nextReady, nativeOrderId = I(head["id"]), recipeId = I(head["recipeId"]), nativeRecipe = head["recipe"]!.ToString(),
        nativeRemaining = remaining, nativeLifetime = lifetime, nativeTipBand = tip, tipBandLowerThreshold = threshold,
        allowanceSeconds = 11, observedNextReadyDelaySeconds = observedWait,
        strictExtraWaitUpperBoundSeconds = remaining - threshold - 11,
        immediateAllowancePreservesTip = SameTip(0), sixSecondWaitPreservesTip = SameTip(6),
        eightSecondWaitPreservesTip = SameTip(8), observedNextReadyWaitPreservesTip = SameTip(observedWait),
        tokens, receipt = entry.DeepClone(),
    });
}
Console.WriteLine(JsonSerializer.Serialize(new {
    qualification = "Read-only native identity/recipe and tip-window review. Three real attached or held clean/unserved plates are physical capacity, not reconstructed free planner leases. Existing assembly callback readiness is not a counterfactual delivery guarantee. Both screened cases fail the proposed same-tip 10+1-second admission budget.",
    controllerSha256 = Hash(typeof(CarnivalPlanner).Assembly.Location),
    manifestSha256 = Hash(Root + "/manifest.json"), sourceSha256 = manifest["sourceCompressedSha256"]!.ToString(), reports,
}, new JsonSerializerOptions { WriteIndented = true }));
