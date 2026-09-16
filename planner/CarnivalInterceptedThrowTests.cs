using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Actual V14 before-throw/flight/catch snapshots; completion and negative mutations are explicitly synthetic, with no game I/O.</summary>
    public static int InterceptedPotSelfTest(JsonObject before15497, JsonObject flight15519, JsonObject caught15520)
    {
        int count = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException("Intercepted pot regression: " + message); count++; }
        void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, message); }
        CarnivalPlanner Make(bool caught = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline intercepted-pot fixture attempted I/O."), null)
            { response = before15497.DeepClone().AsObject(), options = new(), recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560), 96).ToArray() };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline intercepted-pot fixture attempted I/O."), null);
            p.CaptureSupplyTopology(false);
            var guard = p.DirectSupplyGuard(2, 7) ?? throw new InvalidOperationException("Actual native pot home absent.");
            var spec = new JsonObject { ["type"] = "throw", ["player"] = 2, ["targetEntityId"] = 7, ["nativeSupplyHome"] = guard };
            p.Start(2, "supply-Frankfurter", [spec], [70, 7, 19]);
            p.Start(0, "buffer-native-chopped-bun", [p.A("take", 23), p.A("place", 32)], [23, 441, 32]);
            p.Start(1, "receive-dirty-stack", [p.A("take", 46), p.A("place", 75)], [46, 75, 429]);
            var work = p.workers[2]!; work.Active = p.runner.CreateAction(work.Actions.Dequeue());
            _ = p.runner.Tick(work.Active, p.response); // Capture native held443 registration/ordinal before the recorded release.
            // Reconstruct only the controller action's known release boundary.
            // Native flight/catch contents and transforms remain the actual saved observations.
            work.Active.Action.Stage = "throw-await-flight"; work.Active.Action.Frames = 22;
            p.response = flight15519.DeepClone().AsObject(); p.Refresh();
            _ = p.runner.Tick(work.Active, p.response);
            if (caught) { p.response = caught15520.DeepClone().AsObject(); p.Refresh(); }
            return p;
        }
        void NativeInsert(CarnivalPlanner p)
        {
            var ingredient = p.Entity(443)!["composition"]!.DeepClone();
            p.Entity(7)!["contents"] = new JsonArray(ingredient);
            p.Entity(7)!["composition"] = p.Entity(2)!["composition"]!.DeepClone();
            p.Entity(7)!["cookingProgress"] = 0;
            var all = p.state["entities"]!.AsArray(); all.Remove(p.Entity(443));
            p.chefs[3]["heldEntityId"] = 0; p.state["gameplayFrame"] = p.Frame + 1; p.Refresh();
        }
        var flight = Make(false); var flightWork = flight.workers[2];
        flight.AdvanceInterceptedThrows();
        Check(flight.interceptedSupplies.Count == 0 && flight.workers[3] is null && ReferenceEquals(flight.workers[2], flightWork), "uncaught native flight cannot start a receiver job");
        var actual = Make(); var original = actual.workers[2]!; var originalAction = original.Active!;
        var other0 = actual.workers[0]; var other1 = actual.workers[1]; string nativeBefore = actual.response.ToJsonString();
        Check(actual.runner.ObserveInterceptedPotThrow(originalAction, actual.response) is not null,
            "native throw receipt exists: " + originalAction.Stage + "/" + originalAction.Error);
        Check(actual.TrafficControlsReady(3) && actual.Region(3) == "center", "actual idle catcher retains native control");
        actual.AdvanceInterceptedThrows();
        Check(actual.interceptedSupplies.Count == 1, "actual native15520 catch admits its exact original pot handoff");
        var lease = actual.interceptedSupplies.Single();
        Check(lease.Receipt.Source == 443 && lease.Receipt.SourceOrdinal == 442 && lease.Receipt.SourceRegistration == 1759 &&
            lease.Receipt.Thrower == 105 && lease.Receipt.Receiver == 3 && lease.Receipt.Vessel == 7 && lease.Receipt.Home == 19,
            "receipt pins captured source incarnation, native thrower, catcher and receiving home");
        Check(ReferenceEquals(original, actual.workers[2]) && ReferenceEquals(originalAction, actual.workers[2]!.Active) &&
            ReferenceEquals(other0, actual.workers[0]) && ReferenceEquals(other1, actual.workers[1]), "all existing work/action objects remain unchanged");
        Check(original.OwnedResources.SetEquals([70, 7, 19]) && actual.workers[3]!.OwnedResources.SetEquals([443]), "supplier retains exclusive original pot/home ownership and receiver owns only intercepted source");
        Check(actual.workers[3]!.Actions.Count == 1 && StationIs(actual.workers[3]!.Actions.Peek(), 19) &&
            actual.workers[3]!.Actions.Peek()["type"]?.ToString() == "place", "recovery is one ordinary exact-home placement");
        Check(nativeBefore == actual.response.ToJsonString(), "admission does not modify any observed native state");
        Check(!actual.runner.ValidateInterceptedPotThrow(originalAction, lease.Receipt, actual.response) && !originalAction.IsDone,
            "native chef catch is not falsely reported as vessel arrival");
        foreach (string mutation in new[] { "ordinal", "registration", "thrower", "home", "home-ordinal", "pot-lease", "home-lease", "busy-receiver", "receiver-controls", "source-lease", "missing-flight", "wrong-source" })
        {
            var p = Make();
            switch (mutation)
            {
                case "ordinal": p.Entity(443)!["observedOrdinal"] = 9999; break;
                case "registration":
                    var register = p.state["entityRegistration"]!["events"]!.AsArray().OfType<JsonObject>().Last(e => I(e["entity"]?["entityId"]) == 443);
                    register["sequence"] = 99999L; break;
                case "thrower": p.Entity(443)!["previousThrowerEntityId"] = 104; break;
                case "home": p.Entity(19)!["attachedEntityId"] = 0; break;
                case "home-ordinal": p.Entity(19)!["observedOrdinal"] = 9999; break;
                case "pot-lease": p.workers[2]!.OwnedResources.Remove(7); break;
                case "home-lease": p.workers[2]!.OwnedResources.Remove(19); break;
                case "busy-receiver": p.Start(3, "existing-catcher-work", [p.A("place", 19)], []); break;
                case "receiver-controls": p.chefs[3]["controlsEnabled"] = false; break;
                case "source-lease": p.reserved.Add(443); break;
                case "missing-flight": p.workers[2]!.Active = p.runner.CreateAction(p.workers[2]!.Active!.Specification); break;
                case "wrong-source": p.chefs[3]["heldEntityId"] = 441; break;
            }
            var owner = p.workers[2]; var receiver = p.workers[3];
            p.AdvanceInterceptedThrows();
            Check(p.interceptedSupplies.Count == 0 && ReferenceEquals(owner, p.workers[2]) && ReferenceEquals(receiver, p.workers[3]), mutation + " denied without taking ownership");
        }
        var unrelated = Make(); unrelated.Entity(443)!["previousThrowerEntityId"] = 104;
        unrelated.AdvanceInterceptedThrows(); Reject(() => unrelated.Central(3), "unrelated caught food retains generic fail-closed behavior");
        foreach (string mutation in new[] { "source-ordinal", "supplier-work", "receiver-work", "pot-lease", "home-lease", "changed-home", "wrong-holder", "lost-source", "duplicate-pot" })
        {
            var p = Make(); p.AdvanceInterceptedThrows();
            switch (mutation)
            {
                case "source-ordinal": p.Entity(443)!["observedOrdinal"] = 1000; break;
                case "supplier-work": p.workers[2] = new Work("replacement", [], [], null); break;
                case "receiver-work": p.workers[3] = new Work("replacement", [], [], null); break;
                case "pot-lease": p.workers[2]!.OwnedResources.Remove(7); break;
                case "home-lease": p.workers[2]!.OwnedResources.Remove(19); break;
                case "changed-home": p.Entity(19)!["attachedEntityId"] = 0; break;
                case "wrong-holder": p.chefs[0]["heldEntityId"] = 443; p.chefs[3]["heldEntityId"] = 0; break;
                case "lost-source": p.Entity(443)!["active"] = false; p.chefs[3]["heldEntityId"] = 0; break;
                case "duplicate-pot": p.Entity(7)!["contents"] = new JsonArray(p.Entity(443)!["composition"]!.DeepClone()); break;
            }
            Reject(p.AdvanceInterceptedThrows, "active recovery rejects " + mutation);
        }
        var timed = Make(); timed.AdvanceInterceptedThrows(); timed.state["gameplayFrame"] = timed.Frame + 121;
        bool timeout = false; try { timed.AdvanceInterceptedThrows(); } catch (TimeoutException) { timeout = true; }
        Check(timeout, "recovery has a fixed two-second deadline");
        NativeInsert(actual);
        Check(actual.runner.ValidateInterceptedPotThrow(originalAction, lease.Receipt, actual.response), "synthetic native insertion proves only original-source consumption plus exact pot delta");
        _ = actual.runner.Tick(originalAction, actual.response);
        Check(!originalAction.IsDone, "original native arrival barrier still requires its second observation");
        _ = actual.runner.Tick(originalAction, actual.response);
        Check(originalAction.IsDone && originalAction.Error is null, "unchanged original throw barrier completes after exact vessel evidence");
        actual.CompleteWork();
        Check(ReferenceEquals(original, actual.workers[2]) && actual.reserved.Contains(7) && actual.reserved.Contains(19), "supplier completion is held until receiver ordinary placement completes");
        var receiverWork = actual.workers[3]!; var placement = receiverWork.Actions.Dequeue(); placement["player"] = 3;
        receiverWork.Active = actual.runner.CreateAction(placement); receiverWork.Active.IsDone = true;
        actual.CompleteWork();
        Check(lease.ReceiverCompleted && actual.workers[3] is null && !actual.reserved.Contains(443) && actual.reserved.Contains(7), "receiver completion releases its source lease while supplier keeps pot/home");
        actual.AdvanceInterceptedThrows(); actual.CompleteWork();
        Check(actual.interceptedSupplies.Count == 0 && actual.workers[2] is null && !actual.reserved.Contains(7) && !actual.reserved.Contains(19), "only both completed native barriers release original supplier resources");
        Check(ReferenceEquals(other0, actual.workers[0]) && ReferenceEquals(other1, actual.workers[1]), "unrelated jobs survive the entire cooperative handoff");
        return count;
    }
}
