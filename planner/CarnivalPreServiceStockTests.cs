using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int PreServiceStockSelfTest(JsonObject ready1108)
    {
        int count = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Pre-service stock regression: " + message); count++; }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, message);
        }
        CarnivalPlanner Make(bool enabled = true, bool clearHandoff = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline stock fixture attempted I/O."), null)
            {
                response = ready1108.DeepClone().AsObject(),
                options = new(PreServiceStock: enabled, PantryChopping: true, ConditionalFarPotThrows: true),
                recipes = new[] { 158500, 125780, 224216, 228996, 47642, 472326, 257844, 130976 }.Select(CarnivalRecipes.GetRecipe).ToArray()
            };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline stock action attempted I/O."), null);
            p.mealPlates[0] = 10; p.assembling.Add(1);
            if (clearHandoff)
            {
                // Hypothetical already-staged meal at a separate native UL
                // counter. No plate-moving route is implemented or claimed.
                p.Entity(45)!["attachedEntityId"] = 0; p.Entity(51)!["attachedEntityId"] = 10;
                p.Entity(10)!["position"] = p.Entity(51)!["position"]!.DeepClone();
            }
            return p;
        }
        void Elapse(CarnivalPlanner p, int frames)
        {
            p.state["gameplayFrame"] = p.Frame + frames; p.state["timer"] = N(p.state["timer"]) - frames / 60d;
            foreach (var order in (p.state["orders"] as JsonArray)!.OfType<JsonObject>()) order["remaining"] = N(order["remaining"]) - frames / 60d;
        }
        void FinishSupply(CarnivalPlanner p, bool observeContents = true)
        {
            var lease = p.preServiceStock!;
            if (observeContents)
            {
                var leaf = new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = lease.Ingredient, ["state"] = "Raw", ["children"] = new JsonArray() };
                int pass = p.Counter(15.6, -13.2);
                if (p.counterSupplies.ContainsKey(pass))
                {
                    int food = p.entities.Keys.Max() + 1;
                    p.entities[food] = new JsonObject { ["id"] = food, ["active"] = true, ["composition"] = leaf,
                        ["components"] = new JsonArray("CarryableItem"), ["position"] = p.Entity(pass)!["position"]!.DeepClone() };
                    p.Entity(pass)!["attachedEntityId"] = food;
                }
                else p.Entity(lease.Destination)!["composition"] = new JsonObject { ["type"] = "CookedCompositeAssembledNode", ["state"] = "Raw",
                    ["cookingStepId"] = CarnivalRecipes.PotCookingStepId, ["children"] = new JsonArray(leaf) };
            }
            p.ReleaseRemainingWorkResources(lease.Work); p.workers[2] = null; lease.Work.Complete?.Invoke();
        }
        var actual = Make(clearHandoff: false);
        Check(actual.Attached(45) == 10 && actual.Attached(56) == 135 && actual.TryPreServiceStock() &&
            actual.preServiceStock!.Ingredient == CarnivalRecipes.Bun.Id,
            "actual V7 ready meal occupies far-pot fallback while onion blocks lane; only the critical bun is admissible");
        var blockedTail = Make(clearHandoff: false); blockedTail.AdvancePreServiceStock(); Elapse(blockedTail, 100);
        Check(!blockedTail.TryPreServiceStock() && blockedTail.preServiceStock is null,
            "with an existing bun-job tail, actual blocked far supply cannot fabricate a refill before departure");
        var near = Make(clearHandoff: false);
        var loaded = near.Entity(7)!["composition"]!.DeepClone();
        near.Entity(7)!["composition"] = near.Entity(2)!["composition"]!.DeepClone();
        near.Entity(2)!["composition"] = loaded;
        Check(near.TryPreServiceStock() && near.preServiceStock!.Destination == 7 && near.preServiceStock.Ingredient == CarnivalRecipes.Frankfurter.Id,
            "an independently empty near pot can be legally refilled while the ready meal keeps occupying shared counter45");
        Check(near.workers[2]!.Actions.Last()["type"]!.ToString() == "throw" && near.Attached(45) == 10,
            "near-pot refill preserves exact native throw action and does not move the ready meal");
        var observed = Make();
        Check(observed.Frame == 1108 && observed.Held(2) == 0 && observed.AvailablePreServiceHead(out int head, out int source) && head == 10,
            "V7-based fixture keeps exact native meal and empty controlled chef, with a separately staged head for the refill lifecycle");
        Check(observed.PreServiceFutureBaseDemand() == 2 && observed.PreServiceUnassignedStock(CarnivalRecipes.Frankfurter.Id) == 1,
            "next two unprepared hotdogs, skipping donut and active base, have only one observed sausage stock");
        observed.AdvancePreServiceStock();
        Check(observed.preServiceReadyFrame == 1108 && observed.preServiceReadyIndex == 0,
            "first-ready observation starts the total delay budget");
        Check(observed.TryPreServiceStock() && observed.preServiceStock!.Ingredient == CarnivalRecipes.Frankfurter.Id && observed.preServiceStock.Destination == 2,
            "actual empty far pot takes priority over bun stock once the hypothetical fallback counter is free");
        var lease = observed.preServiceStock!;
        Check(lease.MaximumFrames == 120 && observed.workers[2]!.Actions.All(a => I(a["timeoutFrames"]) == 120),
            "one sausage supply retains native actions and finite individual plus total two-second bounds");
        Check(observed.reserved.Contains(lease.Head) && observed.reserved.Contains(lease.Source) && lease.TipBand == 8 &&
            lease.Work.Resources.All(observed.reserved.Contains), "ready FIFO meal and native supply resources are protected independently");
        Check(observed.counterSupplies.GetValueOrDefault(observed.Counter(15.6, -13.2)).Vessel == 2 &&
            lease.Work.Actions.Last()["type"]!.ToString() == "place",
            "occupied actual onion-board lane keeps the existing addressed far-pot fallback");
        Elapse(observed, 55); FinishSupply(observed); observed.AdvancePreServiceStock();
        Check(observed.preServiceStock is null && observed.preServiceUsed && observed.Free(lease.Head, lease.Source),
            "native-equivalent addressed ingredient proof releases ready meal while remembering the one-job allowance");
        Check(!observed.TryPreServiceStock(), "no second refill is admitted during the same pantry visit");
        observed.PantryAndService("upper-left");
        Check(observed.workers[2]?.Name == "take-cannon-fifo", "ready FIFO departure resumes immediately after the single stock job");

        var baseline = Make(false); baseline.PantryAndService("upper-left");
        Check(baseline.workers[2]?.Name == "take-cannon-fifo" && baseline.preServiceStock is null,
            "default-off preserves immediate ready-head departure");
        var tail = Make();
        tail.Start(2, "existing-bun-supply", [tail.A("chop", tail.Board("upper-left", false))], []);
        tail.AdvancePreServiceStock(); Elapse(tail, 100); tail.workers[2] = null;
        Check(tail.TryPreServiceStock() && tail.preServiceReadyFrame == 1108 && tail.preServiceStock!.MaximumFrames == 120,
            "ready-head time during an existing bun job is counted, leaving a bounded short pot refill opportunity");
        var longTail = Make(); longTail.AdvancePreServiceStock(); Elapse(longTail, 121);
        Check(!longTail.TryPreServiceStock() && longTail.workers[2] is null && !longTail.preServiceUsed,
            "existing job tail leaves too little of240frames for a complete pot budget, so service is not postponed");
        longTail.PantryAndService("upper-left");
        Check(longTail.workers[2]?.Name == "take-cannon-fifo", "insufficient budget falls straight through to normal FIFO service");
        var noEvidence = Make(); noEvidence.TryPreServiceStock(); FinishSupply(noEvidence, false);
        Reject(noEvidence.AdvancePreServiceStock, "local job completion alone cannot certify native stock arrival");
        var timeout = Make(); timeout.TryPreServiceStock(); Elapse(timeout, 121);
        Reject(timeout.AdvancePreServiceStock, "blocked native refill cannot occupy the pantry chef beyond its total job bound");
        Check(timeout.preServiceStock is not null && timeout.preServiceUsed, "timeout never claims stock completion or silently starts another job");
        var wrongHead = Make(); wrongHead.TryPreServiceStock(); wrongHead.Entity(10)!["observedOrdinal"] = 99999;
        Reject(wrongHead.AdvancePreServiceStock, "native head plate ID reuse invalidates the protected service reservation");
        var stolen = Make(); stolen.TryPreServiceStock(); stolen.reserved.Remove(stolen.preServiceStock!.Source);
        Reject(stolen.AdvancePreServiceStock, "lost source ownership cannot be ignored while the head waits");
        var changedOrder = Make(); changedOrder.TryPreServiceStock(); changedOrder.PreServiceNativeOrder()!["id"] = 0;
        Reject(changedOrder.AdvancePreServiceStock, "order replacement cannot reuse another head's timing allowance");
        var reset = Make(); reset.TryPreServiceStock();
        Reject(reset.ResetPreServiceVisit, "visit allowance cannot reset while its stock job still owns the ready meal");

        foreach (string reason in new[] { "tip", "missing-time", "wrong-recipe", "round-time", "no-demand", "pot-reserved", "occupied-handoff" })
        {
            var denied = Make();
            switch (reason)
            {
                case "tip": denied.PreServiceNativeOrder()!["remaining"] = .66 * 136 + 12.99; break;
                case "missing-time": denied.PreServiceNativeOrder()!.Remove("remaining"); break;
                case "wrong-recipe": denied.PreServiceNativeOrder()!["recipeId"] = 296560; break;
                case "round-time": denied.state["timer"] = 25; break;
                case "no-demand": denied.recipes = [CarnivalRecipes.GetRecipe(158500), CarnivalRecipes.GetRecipe(228996)]; break;
                case "pot-reserved": denied.reserved.Add(2); denied.reserved.Add(denied.Board("upper-left", false)); break;
                case "occupied-handoff": denied.Entity(denied.Counter(15.6, -13.2))!["attachedEntityId"] = 998;
                    denied.reserved.Add(denied.Board("upper-left", false)); break;
            }
            int owned = denied.reserved.Count;
            Check(!denied.TryPreServiceStock() && denied.workers[2] is null && denied.preServiceStock is null && denied.reserved.Count == owned,
                "failed admission does not wait, consume allowance or leak head locks: " + reason);
        }
        var boundary = Make(); var orderBoundary = boundary.PreServiceNativeOrder()!;
        orderBoundary["remaining"] = .66 * 136 + 13.01;
        Check(PreServiceTipWindow(orderBoundary, 158500, 2), "same native tip tier with service and safety allowance permits a bounded refill");
        orderBoundary["remaining"] = .66 * 136 + 12.99;
        Check(!PreServiceTipWindow(orderBoundary, 158500, 2), "crossing native strict66percent tier after projected delay rejects optional stock");
        var tipLoss = Make(); tipLoss.TryPreServiceStock(); tipLoss.PreServiceNativeOrder()!["remaining"] = .66 * 136 + 10.9;
        Reject(tipLoss.AdvancePreServiceStock, "live native timing continually protects the original tip tier and service allowance");

        var bun = Make();
        bun.Entity(2)!["composition"] = bun.Entity(7)!["composition"]!.DeepClone();
        Check(bun.PreServiceUnassignedStock(CarnivalRecipes.Frankfurter.Id) >= 2 && bun.TryPreServiceStock() &&
            bun.preServiceStock!.Ingredient == CarnivalRecipes.Bun.Id && bun.preServiceStock.MaximumFrames == 240,
            "critical missing bun can use the single four-second allowance when sausage stock is already sufficient");
        Check(bun.workers[2]!.Actions.Last()["type"]!.ToString() == "chop", "bun option retains the validated pantry native chopping completion gate");
        var enough = Make(); enough.recipes = [CarnivalRecipes.GetRecipe(158500), CarnivalRecipes.GetRecipe(228996), CarnivalRecipes.GetRecipe(224216)];
        enough.mealFoods[2] = 153;
        Check(!enough.TryPreServiceStock(), "an already prepared future base cannot request duplicate optional raw ingredients");
        return count;
    }
}
