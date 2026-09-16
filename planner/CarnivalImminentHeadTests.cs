using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int ImminentHeadSelfTest(JsonObject plain4883, JsonObject applying8425, JsonObject staged8428)
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException("Imminent FIFO fixture: " + reason); checks++; }
        CarnivalPlanner Make(JsonObject? snapshot = null, bool enabled = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline service fixture attempted I/O."), null)
            { response = (snapshot ?? plain4883).DeepClone().AsObject(), options = new(WaitForImminentHead: enabled) };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline service fixture attempted I/O."), null);
            bool plain = p.Frame == 4883; int index = plain ? 8 : 13, plate = plain ? 226 : 290, output = plain ? 52 : 49;
            p.recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(plain ? 296560 : 125780), 20).ToArray();
            p.plating.Add(index); p.assembling.Add(index); p.serviceVisitStart = p.delivered - 3;
            var place = p.A("place", output); place["player"] = 0;
            if (plain)
                p.Start(0, "assemble-meal-9-Hotdog_Plain", [place], [plate, output]);
            else
            {
                p.sauceLease = new SauceLease(index, p.recipes[index], 0, 3, plate, 286, 38, output, 72, 79, p.Frame, [plate, output]);
                p.sauceLease.Phase = p.Frame == 8428 ? SaucePhase.StageMeal : SaucePhase.ApplySecond;
                p.reserved.UnionWith(p.sauceLease.Resources);
                p.Start(0, p.Frame == 8428 ? "cooperative-sauce-14-stage-complete-meal" : "cooperative-sauce-14-apply-ketchup",
                    p.Frame == 8428 ? [place] : [p.A("apply", 72)], []);
            }
            var work = p.workers[0]!; work.Active = p.runner.CreateAction(work.Actions.Dequeue());
            return p;
        }
        var p = Make(); var original = p.workers[0]!; var active = original.Active; int[] locks = original.OwnedResources.Order().ToArray();
        Check(p.Frame == 4883 && p.delivered == 8 && p.Held(0) == 226 && CarnivalRecipes.MatchRecipe(p.Entity(226), 296560).ReadyToDeliver,
            "actual native departure contains the exact complete held ninth Plain plate");
        Check(p.TryWaitForImminentHead() && p.imminentHead is not null, "actual remaining full-clearance placement route fits the120-frame budget");
        Check(ReferenceEquals(p.workers[0], original) && ReferenceEquals(original.Active, active) && locks.SequenceEqual(original.OwnedResources.Order()) && p.workers[2] is null,
            "waiting emits neutral service input without changing central action, queue or ownership");
        int start = p.imminentHead!.Start; p.state["gameplayFrame"] = start + 1;
        Check(p.TryWaitForImminentHead() && p.imminentHead!.Start == start, "repeated observation never resets the native-frame bound");
        p.state["gameplayFrame"] = start + 120;
        Check(!p.TryWaitForImminentHead() && p.imminentHead is null && !p.TryWaitForImminentHead(), "bound expires once and cannot be reacquired for the same visit/head");
        p.PantryAndService("lower-right");
        Check(p.workers[2]?.Name == "service-return-to-pantry", "expired waiting falls through to the existing legal portal return");
        p.workers[2]!.Complete!();
        Check(p.imminentAttempted.Count == 0, "native return completion resets the visit's attempted-head set");
        var disabled = Make(enabled: false); disabled.PantryAndService("lower-right");
        Check(disabled.workers[2]?.Name == "service-return-to-pantry" && disabled.imminentHead is null, "default-off preserves the original service departure");
        var pending = Make(applying8425);
        Check(!pending.TryWaitForImminentHead(), "actual GF8425 is rejected because Ketchup is not yet on the plate");
        var coop = Make(staged8428);
        Check(coop.TryWaitForImminentHead(), "actual GF8428 complete cooperative StageMeal plate and remaining route qualify");
        foreach (string change in new[] { "plate-id-reused", "output-id-reused", "lost-plate-lock", "lost-output-lock", "wrong-held", "wrong-recipe", "extra-action", "different-job", "blocked-output" })
        {
            var deny = Make(); Check(deny.TryWaitForImminentHead(), "fixture begins before " + change);
            if (change == "plate-id-reused") deny.Entity(226)!["observedOrdinal"] = I(deny.Entity(226)!["observedOrdinal"]) + 1;
            if (change == "output-id-reused") deny.Entity(52)!["observedOrdinal"] = I(deny.Entity(52)!["observedOrdinal"]) + 1;
            if (change == "lost-plate-lock") deny.reserved.Remove(226);
            if (change == "lost-output-lock") deny.reserved.Remove(52);
            if (change == "wrong-held") deny.chefs[0]["heldEntityId"] = 0;
            if (change == "wrong-recipe") deny.Entity(226)!["composition"]!["children"] = new JsonArray();
            if (change == "extra-action") deny.workers[0]!.Actions.Enqueue(deny.A("take", 41));
            if (change == "different-job") deny.workers[0] = new Work("replacement", [deny.A("place", 52)], [226, 52], null);
            if (change == "blocked-output") deny.Entity(52)!["attachedEntityId"] = 999;
            Check(!deny.TryWaitForImminentHead() && deny.imminentHead is null && !deny.TryWaitForImminentHead(), "invalidated " + change + " cancels and cannot restart its bound");
        }
        var handoff = Make(); Check(handoff.TryWaitForImminentHead(), "handoff begins from captured held identity");
        handoff.chefs[0]["heldEntityId"] = 0; handoff.Entity(52)!["attachedEntityId"] = 226;
        Check(handoff.TryWaitForImminentHead(), "exact native attachment may precede ordinary job completion without looking like a lost plate");
        handoff.mealPlates[8] = 226; handoff.ReleaseRemainingWorkResources(handoff.workers[0]!); handoff.workers[0] = null;
        handoff.ObserveImminentHeadArrival(); handoff.PantryAndService("lower-right");
        Check(handoff.imminentHead is null && handoff.workers[2]?.Name == "collect-fifo-8", "completed head uses existing collection before any service batch limit");
        var incomplete = Make(); incomplete.Entity(226)!["composition"]!["children"] = new JsonArray();
        Check(!incomplete.TryWaitForImminentHead(), "ordinary incomplete food cannot justify predicted waiting");
        return checks;
    }
}
