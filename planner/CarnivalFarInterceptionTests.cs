using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Actual V18 throw/catch observations; completion mutations are synthetic. No game calls.</summary>
    public static int FarInterceptionSelfTest(JsonObject before4120, JsonObject flight4130, JsonObject caught4138)
    {
        int count = 0;
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException("Far interception: " + text); count++; }
        CarnivalPlanner Make(bool caught = true, bool metadata = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline fixture attempted I/O."), null)
            { response = before4120.DeepClone().AsObject(), options = new(ConditionalFarPotThrows: true),
              recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560), 96).ToArray() };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline fixture attempted I/O."), null);
            p.CaptureSupplyTopology(false);
            var guard = FarPotLane.Describe(p.state);
            var action = new JsonObject { ["type"] = "throw", ["player"] = 2, ["targetEntityId"] = 2, ["farPotLane"] = guard };
            if (metadata) action["nativeSupplyHome"] = p.CapturedSupplyHomeGuard(2);
            p.Start(2, "supply-Frankfurter-conditional-far-pot", [action], [70, 7, 2, 56, 45, 19, 17], () =>
            {
                if (!p.Food(2).IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }) || p.Attached(45) != 0)
                    throw new InvalidOperationException("Original conditional supply callback lacks native pot evidence.");
                p.counterSupplies.Remove(45);
            });
            p.counterSupplies.Add(45, (2, CarnivalRecipes.Frankfurter.Id));
            var work = p.workers[2]!;
            work.Active = p.runner.CreateAction(work.Actions.Dequeue());
            _ = p.runner.Tick(work.Active, p.response);
            work.Active.Action.Stage = "throw-await-flight"; work.Active.Action.Frames = 14;
            p.response = flight4130.DeepClone().AsObject(); p.Refresh(); _ = p.runner.Tick(work.Active, p.response);
            if (caught) { p.response = caught4138.DeepClone().AsObject(); p.Refresh(); }
            return p;
        }
        var old = Make(metadata: false); old.AdvanceInterceptedThrows();
        Check(old.interceptedSupplies.Count == 0, "old far throw without captured home metadata reproduces missing recovery");
        var flight = Make(false); flight.AdvanceInterceptedThrows();
        Check(flight.interceptedSupplies.Count == 0, "flight alone never issues a catcher placement");
        var actual = Make(); var original = actual.workers[2]!; var originalAction = original.Active!;
        string nativeBefore = actual.response.ToJsonString(); actual.AdvanceInterceptedThrows();
        Check(actual.interceptedSupplies.Count == 1, "actual V18 native catch admits existing bounded recovery");
        var lease = actual.interceptedSupplies.Single();
        Check(lease.Receipt.Source == 215 && lease.Receipt.SourceOrdinal == 214 && lease.Receipt.Thrower == 105 &&
              lease.Receipt.Receiver == 3 && lease.Receipt.Vessel == 2 && lease.Receipt.Home == 17,
              "receipt binds original far pot, source and native catcher");
        Check(ReferenceEquals(actual.workers[2], original) && original.OwnedResources.SetEquals([70, 7, 2, 56, 45, 19, 17]),
              "all original far-lane leases and Work remain owned until completion");
        Check(actual.workers[3]!.OwnedResources.SetEquals([215]) && I(actual.workers[3]!.Actions.Single()["station"]) == 17,
              "catcher gets only exact source lease and an ordinary original-home placement");
        Check(actual.counterSupplies[45] == (2, CarnivalRecipes.Frankfurter.Id) && nativeBefore == actual.response.ToJsonString(),
              "admission retains fallback address and does not change native state");
        foreach (string mutation in new[] { "home", "ordinal", "thrower", "busy", "disabled", "pot-lease", "source-lease", "near-direct" })
        {
            var p = Make();
            switch (mutation)
            {
                case "home": p.Entity(17)!["attachedEntityId"] = 0; break;
                case "ordinal": p.Entity(215)!["observedOrdinal"] = 9999; break;
                case "thrower": p.Entity(215)!["previousThrowerEntityId"] = 104; break;
                case "busy": p.Start(3, "unrelated", [p.A("place", 17)], []); break;
                case "disabled": p.chefs[3]["controlsEnabled"] = false; break;
                case "pot-lease": p.workers[2]!.OwnedResources.Remove(2); break;
                case "source-lease": p.reserved.Add(215); break;
                case "near-direct": Check(p.DirectSupplyGuard(2, 2) is null, "far identity metadata does not broaden near direct admission"); continue;
            }
            p.AdvanceInterceptedThrows(); Check(p.interceptedSupplies.Count == 0, mutation + " retains fail-closed admission");
        }
        var timed = Make(); timed.AdvanceInterceptedThrows(); timed.state["gameplayFrame"] = timed.Frame + 121;
        bool timeout = false; try { timed.AdvanceInterceptedThrows(); } catch (TimeoutException) { timeout = true; }
        Check(timeout, "far recovery retains fixed 120-frame timeout");
        // This is explicitly synthetic native consumption, used to test only
        // lifecycle/resource semantics, never reported as native probe success.
        var source = actual.Entity(215)!["composition"]!.DeepClone();
        actual.Entity(2)!["contents"] = new JsonArray(source);
        actual.Entity(2)!["composition"] = actual.Entity(7)!["composition"]!.DeepClone();
        actual.Entity(2)!["cookingProgress"] = 0;
        actual.state["entities"]!.AsArray().Remove(actual.Entity(215)); actual.chefs[3]["heldEntityId"] = 0;
        actual.Refresh(); _ = actual.runner.Tick(originalAction, actual.response); _ = actual.runner.Tick(originalAction, actual.response);
        Check(originalAction.IsDone && originalAction.Error is null, "unchanged original two-sample vessel arrival barrier completes");
        actual.CompleteWork();
        Check(ReferenceEquals(actual.workers[2], original) && actual.reserved.Contains(17) && actual.counterSupplies.ContainsKey(45),
              "supplier cannot release original lane before catcher work completes");
        var receiver = actual.workers[3]!; receiver.Active = actual.runner.CreateAction(receiver.Actions.Dequeue()); receiver.Active.IsDone = true;
        actual.CompleteWork(); actual.AdvanceInterceptedThrows(); actual.CompleteWork();
        Check(actual.workers[2] is null && actual.workers[3] is null && actual.interceptedSupplies.Count == 0 &&
              !actual.counterSupplies.ContainsKey(45) && new[] {70, 7, 2, 56, 45, 19, 17, 215}.All(i => actual.Free(i)),
              "both callbacks finish and release exact original lane/source leases");
        return count;
    }
}
