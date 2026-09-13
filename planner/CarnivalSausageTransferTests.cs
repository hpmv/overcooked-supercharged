using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int SausageTransferBudgetSelfTest(JsonObject native2497, JsonObject native2738, JsonObject? evidence = null)
    {
        int count = 0;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Sausage transfer budget regression: " + message); count++; }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, message);
        }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline buffer regression attempted native I/O."), null)
            {
                response = native2497.DeepClone().AsObject(), options = new(SausageBufferSize: 1),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(158500), 96).ToArray()
            };
            if (p.response["state"] is null) p.response = new() { ["state"] = p.response };
            p.Refresh(); p.runner = new(_ => throw new InvalidOperationException("Offline buffer route attempted native I/O."), null);
            p.CaptureSausagePotHomes();
            var lease = new SausageLease(45, 38, 2446, I(p.Entity(45)!["observedOrdinal"]), I(p.Entity(38)!["observedOrdinal"]))
            {
                Phase = SausagePhase.Handoff, PhaseFrame = 2497, Owner = -1,
                Food = 178, FoodOrdinal = 177
            };
            lease.Owned.Add(178); p.sausageBuffers.Add(lease);
            foreach (int resource in lease.Owned) p.reserved.Add(resource);
            return p;
        }
        void ObserveNativeTail(CarnivalPlanner p)
        {
            p.response = native2738.DeepClone().AsObject();
            if (p.response["state"] is null) p.response = new() { ["state"] = p.response };
            p.Refresh(); p.AdvanceSausageBuffers();
        }
        CarnivalPlanner Active()
        {
            var p = Make();
            Check(p.TryParkBufferedSausage(3), "exact V18 GF2497 combined legal paths admit parking");
            return p;
        }

        var actual = Active(); var lease = actual.sausageBuffers.Single(); var transfer = lease.Transfer!;
        if (evidence is not null)
        {
            evidence["admission"] = actual.SausageStatus(lease);
            evidence["scope"] = "Recorded native admission and old failure are observed unchanged; final attachment continuation is a synthetic lifecycle fixture, not a successful native rerun.";
        }
        Check(actual.Frame == 2497 && actual.Attached(45) == 178 && actual.Attached(38) == 0 && actual.Held(3) == 0,
            "native admission retains exact raw/pass/storage identity and empty owner");
        Check(transfer.Budget > 241 && transfer.Budget <= SausageMaximumTransferFrames && transfer.Deadline == 2497 + transfer.Budget,
            "both planned paths receive one finite deadline that covers the witnessed old failure");
        Check(transfer.Source == 45 && transfer.Destination == 38 &&
            N(transfer.Evidence["pickupPath"]?["Length"]) > 9 && N(transfer.Evidence["placementPath"]?["Length"]) > 1,
            "budget evidence includes both actual native-geometry walking paths");
        Check(actual.workers[3]!.Actions.Count == 2 && actual.workers[3]!.Actions.All(a => I(a["timeoutFrames"]) == transfer.Budget),
            "each action is bounded and the observer imposes the single tighter total");
        int deadline = transfer.Deadline; var originalWork = actual.workers[3];
        ObserveNativeTail(actual);
        Check(actual.Frame == 2738 && actual.Held(3) == 178 && actual.Attached(45) == 0 && actual.Attached(38) == 0 && transfer.PickedUp,
            "actual old-failure snapshot has the same raw ingredient in the correct owner's hand");
        Check(ReferenceEquals(originalWork, actual.workers[3]) && transfer.Deadline == deadline && lease.PhaseFrame == 2497,
            "observed pickup after the detour retains original Work and never resets its deadline");
        actual.AdvanceSausageBuffers();
        Check(transfer.Deadline == deadline, "repeated observations cannot extend the deadline");
        actual.state["gameplayFrame"] = deadline; actual.AdvanceSausageBuffers();
        Check(transfer.Deadline == deadline, "same exact native hand state is accepted at the final allowed frame");
        actual.state["gameplayFrame"] = deadline + 1;
        Reject(actual.AdvanceSausageBuffers, "one frame beyond the immutable total aborts even with valid identity");

        foreach (var mutate in new Action<CarnivalPlanner>[] {
            p => p.workers[3] = null,
            p => p.reserved.Remove(38),
            p => p.Entity(45)!["observedOrdinal"] = 123456,
            p => p.Entity(38)!["observedOrdinal"] = 123456,
            p => p.Entity(38)!["active"] = false,
            p => p.Entity(178)!["observedOrdinal"] = 123456,
            p => p.chefs[3]["controlsEnabled"] = false,
            p => p.sausageBuffers.Single().PhaseFrame++,
            p => { p.Entity(45)!["attachedEntityId"] = 0; p.chefs[0]["heldEntityId"] = 178; },
            p => p.Entity(38)!["attachedEntityId"] = 99999
        })
        {
            var p = Active(); mutate(p);
            Reject(p.AdvanceSausageBuffers, "source/counter/food/worker/ownership mutation cannot spend the larger deadline");
        }
        var replacement = Active(); var old = replacement.workers[3]!;
        replacement.workers[3] = null;
        Check(replacement.Start(3, "foreign-work", [replacement.A("take", 45)], []), "foreign Work fixture can replace an unowned worker");
        Check(!ReferenceEquals(old, replacement.workers[3]), "replacement Work has a different identity");
        Reject(replacement.AdvanceSausageBuffers, "same owner with a different Work cannot continue the original transfer");

        var blocked = Make(); blocked.chefs[3]["runSpeed"] = .01;
        Check(!blocked.TryParkBufferedSausage(3) && blocked.workers[3] is null && blocked.sausageBuffers.Single().Phase == SausagePhase.Handoff &&
            blocked.reserved.SetEquals([38, 45, 178]), "infeasible combined walking time declines without changing stock ownership");
        var noIdentity = Make(); noIdentity.Entity(45)!["active"] = false;
        Check(!noIdentity.TryParkBufferedSausage(3) && noIdentity.workers[3] is null, "inactive source never starts a transfer");
        foreach (var values in new[] { (double.NaN, 6d), (double.PositiveInfinity, 6d), (-1d, 6d), (1d, 0d), (1d, double.NaN), (100d, 6d) })
            Check(SausagePathFrames(values.Item1, values.Item2) == 0, "invalid or over-cap path admission fails closed");
        Check(SausagePathFrames(0, 6) == 270 && SausagePathFrames(33, 6) == 600 && SausagePathFrames(33.01, 6) == 0,
            "budget arithmetic preserves both allowances and the strict ten-second cap");

        var finished = Active(); ObserveNativeTail(finished); var completed = finished.sausageBuffers.Single();
        finished.chefs[3]["heldEntityId"] = 0; finished.Entity(38)!["attachedEntityId"] = 178;
        finished.AdvanceSausageBuffers();
        var work = finished.workers[3]!; finished.ReleaseRemainingWorkResources(work); finished.workers[3] = null; work.Complete?.Invoke();
        Check(completed.Phase == SausagePhase.Parked && completed.Owner == -1 && finished.Free(45) && completed.Owned.SetEquals([38, 178]),
            "synthetic native final-attachment continuation releases only the pass after exact pickup evidence");
        Check(finished.Start(2, "successor-pass", [finished.A("take", 45)], [45]), "new pantry job can own released pass");
        finished.AdvanceSausageBuffers();
        Check(finished.reserved.Contains(45) && finished.workers[2]!.OwnedResources.Contains(45), "stationary stock cannot erase the newer pass lease");
        return count;
    }

    public static int SausageTransferNativeTraceSelfTest(IEnumerable<JsonObject> nativeResponses)
    {
        int count = 0, parkingFrames = 0, loadingFrames = 0;
        CarnivalPlanner? p = null; SausageLease? lease = null;
        void Check(bool value, string message)
        { if (!value) throw new InvalidOperationException("Sausage recorded transfer regression: " + message); count++; }
        foreach (var response in nativeResponses)
        {
            int frame = I(KitchenModel.SnapshotState(response)["gameplayFrame"]);
            bool parking = frame >= 2497;
            if (p is null || parking && p.sausageBuffers.Count == 0)
            {
                p = new(_ => throw new InvalidOperationException("Offline recorded transfer attempted native I/O."), null)
                { response = response.DeepClone().AsObject(), options = new(SausageBufferSize: 1),
                    recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(158500), 96).ToArray() };
                p.Refresh(); p.runner = new(_ => throw new InvalidOperationException("Offline recorded route attempted native I/O."), null);
                p.CaptureSausagePotHomes();
                int counter = parking ? 38 : 32, food = parking ? 178 : 145;
                lease = new(45, counter, parking ? 2446 : 495, I(p.Entity(45)!["observedOrdinal"]), I(p.Entity(counter)!["observedOrdinal"]))
                { Phase = parking ? SausagePhase.Handoff : SausagePhase.Parked, PhaseFrame = frame, Owner = -1,
                    Food = food, FoodOrdinal = food - 1 };
                if (!parking) lease.Owned.Remove(45);
                lease.Owned.Add(food); p.sausageBuffers.Add(lease);
                foreach (int resource in lease.Owned) p.reserved.Add(resource);
                Check(parking ? p.TryParkBufferedSausage(3) : p.TryLoadBufferedSausage(0), "actual recorded native transfer admits with both legal path legs");
            }
            else
            { p.response = response.DeepClone().AsObject(); p.Refresh(); }
            p.AdvanceSausageBuffers();
            Check(lease!.Transfer!.Deadline == lease.Transfer.Started + lease.Transfer.Budget && ReferenceEquals(p.workers[lease.Owner], lease.Work),
                "every native sample retains its fixed deadline and original work");
            if (parking) parkingFrames++; else loadingFrames++;
            if (frame == 2155)
            {
                Check(lease.Transfer.PickedUp && lease.Transfer.ConsumedFrame >= 0 && !B(p.Entity(145)?["active"]) &&
                    p.Food(2).IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Frankfurter.Id }),
                    "earlier successful native loading observes held raw145 consumed into exact pot2");
                var work = p.workers[0]!; p.ReleaseRemainingWorkResources(work); p.workers[0] = null; work.Complete?.Invoke();
                Check(p.sausageBuffers.Count == 0 && p.Free(32, 145, 2, 17), "actual native loading completion releases only its old stock and pot/home leases");
            }
        }
        Check(loadingFrames == 63 && parkingFrames == 242, "all 305 consecutive loading/parking native samples were checked");
        Check(lease is { Phase: SausagePhase.Parking } && lease.Transfer!.PickedUp && p!.Frame == 2738 && p.Held(3) == 178,
            "failed native run remains honestly unfinished while the valid held-raw transfer no longer times out at 241 frames");
        return count;
    }
}
