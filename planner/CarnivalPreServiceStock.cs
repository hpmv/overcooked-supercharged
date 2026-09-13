using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private const int PreServiceDelayFrames = 240, PreServicePotFrames = 120, PreServiceBunFrames = 240;
    private const double PreServiceTravelSeconds = 10, PreServiceSafetySeconds = 1;
    private sealed record PreServiceStockLease(Work Work, int Head, int Source, int HeadOrdinal, int Index,
        int Order, int Recipe, int TipBand, int Ingredient, int Destination, int StartFrame, int MaximumFrames);
    private PreServiceStockLease? preServiceStock;
    private bool preServiceUsed;
    private int preServiceReadyIndex = -1, preServiceReadyFrame;

    private void ResetPreServiceVisit()
    {
        if (preServiceStock is not null) throw new InvalidOperationException("Cannot reset pantry visit with unfinished pre-service stock.");
        preServiceUsed = false; preServiceReadyIndex = -1;
    }
    private bool AvailablePreServiceHead(out int head, out int source)
    {
        head = mealPlates.GetValueOrDefault(delivered); source = AttachmentParent(head);
        return head != 0 && source != 0 && IsHeadMeal(head) && Free(head, source) &&
            (Station(source)?.Regions.Contains("upper-left") == true || HasAccessibleHead("lower-right"));
    }
    private JsonObject? PreServiceNativeOrder() => (state["orders"] as JsonArray)?.OfType<JsonObject>()
        .OrderBy(o => I(o["id"])).FirstOrDefault();
    private static bool PreServiceTipWindow(JsonObject? order, int recipe, double jobSeconds, int? requiredTip = null)
    {
        if (order is null || I(order["recipeId"]) != recipe || order["remaining"] is null || order["lifetime"] is null) return false;
        double remaining = N(order["remaining"]), lifetime = N(order["lifetime"]);
        if (!double.IsFinite(remaining) || !double.IsFinite(lifetime) || remaining <= 0 || lifetime <= 0) return false;
        int tip = CarnivalRecipes.TipForRemainingFraction(remaining / lifetime);
        double projected = remaining - jobSeconds - PreServiceTravelSeconds - PreServiceSafetySeconds;
        return tip > 0 && (!requiredTip.HasValue || tip == requiredTip.Value) && projected > 0 &&
            CarnivalRecipes.TipForRemainingFraction(projected / lifetime) == tip;
    }
    private int PreServiceFutureBaseDemand() => Math.Min(2, Pending().Count(p => p.Index > delivered && !IsDonut(p.Recipe) && !mealFoods.ContainsKey(p.Index)));
    private int PreServiceUnassignedStock(int ingredient) => ComponentEntities().Count(e =>
        Food(Id(e)).IngredientIds is [var only] && only == ingredient &&
        !workers.Any(w => w is not null && w.Name.StartsWith("unplated-hotdog-", StringComparison.Ordinal) && w.OwnedResources.Contains(Id(e))));

    private void AdvancePreServiceStock()
    {
        if (!options.PreServiceStock) return;
        if (preServiceStock is { } lease)
        {
            var order = PreServiceNativeOrder();
            if (delivered != lease.Index || !SameObservedEntity(lease.Head, lease.HeadOrdinal) || !IsHeadMeal(lease.Head) ||
                AttachmentParent(lease.Head) != lease.Source || !reserved.Contains(lease.Head) || !reserved.Contains(lease.Source) ||
                order is null || I(order["id"]) != lease.Order || !PreServiceTipWindow(order, lease.Recipe, 0, lease.TipBand))
                throw new InvalidOperationException("Pre-service stock lost its reserved FIFO meal or protected native order/tip window.");
            if (Frame - preServiceReadyFrame > PreServiceDelayFrames || Frame - lease.StartFrame > lease.MaximumFrames)
                throw new TimeoutException("Pre-service stock exceeded its bounded refill or ready-head delay; candidate stopped before further waiting.");
            if (workers[2] == lease.Work) return;
            if (workers[2] is not null || Held(2) != 0)
                throw new InvalidOperationException("Pre-service stock completion did not release its exact pantry job with empty hands.");
            bool deliveredStock;
            if (lease.Ingredient == CarnivalRecipes.Bun.Id)
            {
                int item = Attached(lease.Destination);
                deliveredStock = Food(item).IngredientIds.SequenceEqual(new[] { lease.Ingredient }) &&
                    (!options.PantryChopping || Chopped(item, lease.Ingredient));
            }
            else
            {
                int pass = Counter(15.6, -13.2);
                deliveredStock = Food(lease.Destination).IngredientIds.SequenceEqual(new[] { lease.Ingredient }) ||
                    counterSupplies.TryGetValue(pass, out var address) && address == (lease.Destination, lease.Ingredient) &&
                    Food(Attached(pass)).IngredientIds.SequenceEqual(new[] { lease.Ingredient });
            }
            if (!deliveredStock) throw new InvalidOperationException("Pre-service stock has no matching native vessel contents or exact addressed pantry handoff.");
            reserved.Remove(lease.Head); reserved.Remove(lease.Source);
            Log("preServiceStockComplete", PreServiceStockStatus()); preServiceStock = null;
        }
        if (Region(2) == "upper-left" && AvailablePreServiceHead(out _, out _))
        {
            if (preServiceReadyIndex != delivered)
            {
                preServiceReadyIndex = delivered; preServiceReadyFrame = Frame;
                Log("preServiceHeadObserved", PreServiceStockStatus());
            }
        }
        else if (preServiceStock is null) preServiceReadyIndex = -1;
    }

    private bool TryPreServiceStock()
    {
        if (!options.PreServiceStock || preServiceUsed || preServiceStock is not null || workers[2] is not null || Held(2) != 0 ||
            Region(2) != "upper-left" || !B(chefs[2]["controlsEnabled"]) || NativeCannonFlight(2) ||
            !AvailablePreServiceHead(out int head, out int source)) return false;
        if (preServiceReadyIndex != delivered) { preServiceReadyIndex = delivered; preServiceReadyFrame = Frame; }
        int demand = PreServiceFutureBaseDemand();
        if (demand == 0) return false;
        var candidates = new List<(NativeIngredient Ingredient, int Destination, int Budget)>();
        if (PreServiceUnassignedStock(CarnivalRecipes.Frankfurter.Id) < demand)
        {
            int destination = Stations("pot").OrderBy(s => s.Position.X).Select(s => s.EntityId).FirstOrDefault(id =>
                Free(id) && Entity(id)?["composition"] is not null && EmptyFood(id) && !HeldByAnyone(id) &&
                !counterSupplies.Values.Any(address => address.Vessel == id));
            if (destination != 0) candidates.Add((CarnivalRecipes.Frankfurter, destination, PreServicePotFrames));
        }
        if (PreServiceUnassignedStock(CarnivalRecipes.Bun.Id) < demand)
        {
            int board = Board("upper-left", false);
            if (Free(board) && Entity(board)?["attachedEntityId"] is not null && EmptyAttachment(board))
                candidates.Add((CarnivalRecipes.Bun, board, PreServiceBunFrames));
        }
        foreach (var (ingredient, destination, budget) in candidates)
        {
            if (Frame - preServiceReadyFrame + budget > PreServiceDelayFrames ||
                N(state["timer"]) <= budget / 60d + CarnivalRecipes.BoilSeconds + PreServiceTravelSeconds + PreServiceSafetySeconds) continue;
            var order = PreServiceNativeOrder();
            if (!PreServiceTipWindow(order, recipes[delivered].Id, budget / 60d)) continue;
            // Protect the ready meal; Supply retains exact native throw, lane,
            // handoff and chopping gates. Failed admission never waits for stock.
            reserved.Add(head); reserved.Add(source);
            if (!Supply(2, ingredient, destination, ingredient == CarnivalRecipes.Frankfurter))
            { reserved.Remove(head); reserved.Remove(source); continue; }
            var work = workers[2]!;
            foreach (var action in work.Actions) action["timeoutFrames"] = Math.Min(I(action["timeoutFrames"]) > 0 ? I(action["timeoutFrames"]) : budget, budget);
            preServiceStock = new(work, head, source, Entity(head)?["observedOrdinal"]?.GetValue<int>() ?? -1,
                delivered, I(order!["id"]), recipes[delivered].Id, CarnivalRecipes.TipForRemainingFraction(N(order["remaining"]) / N(order["lifetime"])),
                ingredient.Id, destination, Frame, budget);
            preServiceUsed = true;
            Log("preServiceStockAdmitted", PreServiceStockStatus()); return true;
        }
        return false;
    }
    private JsonObject PreServiceStockStatus() => new()
    {
        ["enabled"] = options.PreServiceStock, ["usedThisVisit"] = preServiceUsed, ["readyIndex"] = preServiceReadyIndex,
        ["readyFrame"] = preServiceReadyFrame, ["maximumReadyDelayFrames"] = PreServiceDelayFrames,
        ["activeJob"] = preServiceStock?.Work.Name, ["ingredient"] = preServiceStock?.Ingredient,
        ["destination"] = preServiceStock?.Destination, ["jobStartFrame"] = preServiceStock?.StartFrame,
        ["maximumJobFrames"] = preServiceStock?.MaximumFrames, ["reservedHeadPlate"] = preServiceStock?.Head,
        ["protectedTipBand"] = preServiceStock?.TipBand, ["frame"] = Frame,
        ["qualification"] = "Bounded optional native refill; travel allowance is an admission budget, not a guaranteed delivery time."
    };
}
