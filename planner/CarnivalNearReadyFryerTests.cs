using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Native V15 admission geometry and separate native fryer-probe observations; explicitly reconstructed controller ownership, no game I/O.</summary>
    public static int NearReadyFryerSelfTest(JsonObject at2409, JsonObject probe1250, JsonObject probe1263,
        JsonObject probe1709, JsonObject probe1710, JsonObject probe1718, JsonObject probe1742)
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Near-ready fryer regression: " + message); count++; }
        void Reject(Action action, string message)
        { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; } Check(rejected, message); }
        CarnivalPlanner Make()
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline fryer fixture attempted I/O."), null) {
                response = at2409.DeepClone().AsObject(), options = new(ServiceSideHead: true, NearReadyFryerHarvest: true),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560), 24).ToArray(), delivered = 2 };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline fryer fixture attempted I/O."), null);
            p.CaptureFryerHomes(); p.recipes[7] = CarnivalRecipes.GetRecipe(130976); p.basketAssignments[5] = 7;
            p.mealPlates[3] = 13; p.assembling.Add(2); p.plating.Add(2);
            p.workers[3] = new Work("captured-head-2-assembly", [], [11, 161, 38, 47, 72, 79], null);
            p.reserved.UnionWith([11, 161, 38, 47, 72, 79, 69, 56, 7, 19, 33, 4, 16, 61]);
            return p;
        }
        var p = Make(); string before = p.response.ToJsonString();
        Check(p.Frame == 2409 && p.NativeNearReadyFryer(5, 7) && !CarnivalRecipes.MatchRecipe(p.Entity(5), 130976, false).ReadyToDeliver,
            "actual basket5 has exact Mixed Raspberry but is not yet native Cooked");
        Check(p.AvailablePlate(0) == 170 && p.Attached(44) == 170 && p.MealOutput(170, 7) == 49,
            "recorded sole free clean plate170 on44 and free output49 remain exact");
        Check(p.TryRescueFryer(0, 5), "measured9.700024-second basket admits direct native wait/plating before offheat parking");
        var plan = p.nearReadyFryers.Values.Single(); var work = p.workers[0]!;
        Check(plan.Plate == 170 && plan.Source == 44 && plan.Home == 15 && plan.Basket == 5 && plan.Index == 7 && plan.Output == 49,
            "same native source, plate, basket, stove, FIFO assignment and output are selected");
        Check(work.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "navigate", "cook", "combine", "place" }) &&
            I(work.Actions.First()["station"]) == 170 && I(work.Actions.ElementAt(1)["station"]) == 15 && I(work.Actions.ElementAt(3)["station"]) == 5,
            "input plan takes exact plate, approaches original stove, waits native Cooked, then combines without basket pickup");
        Check(work.OwnedResources.SetEquals([170, 5, 15, 49]) && !p.reserved.Contains(44) && p.fryerRescues.Count == 0,
            "direct harvest exclusively owns basket/home/plate/output without an offheat counter or unnecessary source lease");
        Check(N(plan.Evidence["estimatedSecondsToConsumption"]) < N(plan.Evidence["guardRemainingSeconds"]) &&
            Math.Abs(N(plan.Evidence["estimatedSecondsToConsumption"]) - 4.9085422207) < .01,
            "both full-clearance walking legs plus remaining native cook time and two seconds fit existing19-second guard");
        Check(before == p.response.ToJsonString() && p.workers[3]!.Name == "captured-head-2-assembly" && p.reserved.Contains(79),
            "admission alters only planner ownership and leaves observed native state and other active work untouched");
        p.ObserveNearReadyFryerHarvests(); Check(!plan.PickedUp && !plan.CookedObserved, "admission cannot invent pickup or native completion");
        // Explicit synthetic continuation of the captured production admission,
        // including its actual TryAssemble completion callback and order maps.
        p.Entity(44)!["attachedEntityId"] = 0; p.chefs[0]["heldEntityId"] = 170; p.ObserveNearReadyFryerHarvests();
        p.Entity(5)!["composition"]!["state"] = "Cooked"; p.Entity(5)!["composition"]!["progress"] = 1.01;
        p.Entity(5)!["cookingProgress"] = 10.1; p.ObserveNearReadyFryerHarvests();
        p.Entity(170)!["composition"] = p.Entity(5)!["composition"]!.DeepClone();
        p.Entity(5)!["composition"] = probe1718["state"]!["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == 5)["composition"]!.DeepClone();
        p.Entity(5)!["contents"] = new JsonArray(); p.Entity(5)!["cookingProgress"] = 0; p.ObserveNearReadyFryerHarvests();
        // The washer's successor uses source44 after pickup. The original
        // direct harvest must never own or release that successor's resources.
        var successor = new Work("successor-clean-plate", [], [44, 1000], null);
        p.workers[1] = successor; p.reserved.UnionWith([44, 1000]); p.Entity(44)!["attachedEntityId"] = 1000;
        p.chefs[0]["heldEntityId"] = 0; p.Entity(49)!["attachedEntityId"] = 170; p.ObserveNearReadyFryerHarvests();
        var last = work.Actions.Last().DeepClone().AsObject(); last["player"] = 0;
        work.Actions.Clear(); work.Active = p.runner.CreateAction(last); work.Active.IsDone = true; p.CompleteWork();
        Check(p.mealPlates.GetValueOrDefault(7) == 170 && !p.assembling.Contains(7) && !p.plating.Contains(7) && p.nearReadyFryers.Count == 0,
            "real TryAssemble completion callback records the exact finished plate and retires its observer before recipe ownership clears");
        Check(p.workers[0] is null && p.Free(5, 15, 170, 49) && ReferenceEquals(p.workers[1], successor) && p.reserved.Contains(44) && p.reserved.Contains(1000),
            "ordinary completion frees only original leases while washer successor remains owned");
        Check(p.fryerRescues.Count == 0 && p.Attached(15) == 5, "direct route never creates a restore-empty-fryer job");

        foreach (string mutation in new[] { "disabled", "busy", "holding", "controls", "home-empty", "basket-moved", "basket-reused", "home-reused",
            "plate-leased", "source-leased", "home-leased", "output-leased", "last-head-plate", "wrong-recipe", "missing-egg", "extra-egg",
            "wrong-cook-step", "wrong-duration", "young", "already-cooked-time", "no-speed", "slow-route", "plate-inactive", "output-inactive" })
        {
            var q = Make();
            switch (mutation)
            {
                case "disabled": q.options = q.options with { NearReadyFryerHarvest = false }; break;
                case "busy": q.workers[0] = new Work("existing-job", [], [], null); break;
                case "holding": q.chefs[0]["heldEntityId"] = 170; break;
                case "controls": q.chefs[0]["controlsEnabled"] = false; break;
                case "home-empty": q.Entity(15)!["attachedEntityId"] = 0; break;
                case "basket-moved": q.chefs[1]["heldEntityId"] = 5; break;
                case "basket-reused": q.Entity(5)!["observedOrdinal"] = 5000; break;
                case "home-reused": q.Entity(15)!["observedOrdinal"] = 5001; break;
                case "plate-leased": q.reserved.Add(170); break;
                case "source-leased": q.reserved.Add(44); break;
                case "home-leased": q.reserved.Add(15); break;
                case "output-leased": q.reserved.Add(49); break;
                case "last-head-plate": q.plating.Remove(2); break;
                case "wrong-recipe": q.recipes[7] = CarnivalRecipes.GetRecipe(228996); break;
                case "missing-egg": q.Entity(5)!["composition"]!["children"]![0]!["children"]!.AsArray().RemoveAt(1); break;
                case "extra-egg": q.Entity(5)!["composition"]!["children"]![0]!["children"]!.AsArray().Add(q.Entity(5)!["composition"]!["children"]![0]!["children"]![1]!.DeepClone()); break;
                case "wrong-cook-step": q.Entity(5)!["cookingTypeId"] = CarnivalRecipes.PotCookingStepId; break;
                case "wrong-duration": q.Entity(5)!["cookingTime"] = 12; break;
                case "young": q.Entity(5)!["cookingProgress"] = 8.99; break;
                case "already-cooked-time": q.Entity(5)!["cookingProgress"] = 10; break;
                case "no-speed": q.chefs[0]["runSpeed"] = 0; break;
                case "slow-route": q.chefs[0]["runSpeed"] = .5; break;
                case "plate-inactive": q.Entity(170)!["active"] = false; break;
                case "output-inactive": q.Entity(49)!["active"] = false; break;
            }
            Check(!q.TryAssemble(0, exactIndex: 7, exactPlate: 170, exactBasket: 5, waitForNativeFryer: true) && q.nearReadyFryers.Count == 0,
                "unsafe admission rejects without partial leases: " + mutation);
        }

        // The following sequence replays actual native observations from the
        // separate mechanism probe, not invented success states. Its earlier
        // low-progress start tests the observer, not9..<10 production admission.
        CarnivalPlanner Probe()
        {
            var q = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline native lifecycle attempted I/O."), null) {
                response = probe1250.DeepClone().AsObject(), options = new(NearReadyFryerHarvest: true),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(228996), 16).ToArray() };
            q.Refresh(); q.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline native lifecycle attempted I/O."), null);
            q.CaptureFryerHomes(); q.basketAssignments[5] = 0; q.assembling.Add(0); q.plating.Add(0);
            var selected = new NearReadyFryer(3, 0, 10, 38, 5, 15, 49, q.Frame, new JsonObject { ["qualification"] = "Recorded mechanism lifecycle; not production admission at this early frame" });
            foreach (int id in new[] { 10, 38, 5, 15, 49 }) selected.Identities[id] = I(q.Entity(id)?["observedOrdinal"]);
            q.Start(3, "native-observer-fixture", [q.A("place", 49)], selected.Resources, () => q.CompleteNearReadyFryer(selected));
            q.RegisterNearReadyFryer(selected, q.workers[3]!); return q;
        }
        void Apply(CarnivalPlanner q, JsonObject observation)
        { q.response = observation.DeepClone().AsObject(); q.Refresh(); q.ObserveNearReadyFryerHarvests(); }
        var native = Probe(); var observed = native.nearReadyFryers.Values.Single();
        native.ObserveNearReadyFryerHarvests(); Apply(native, probe1263);
        Check(observed.PickedUp && native.Held(3) == 10 && native.Attached(15) == 5 && !observed.CookedObserved,
            "native1263 proves exact clean plate pickup while original basket stays on heat");
        Apply(native, probe1709); Check(!observed.CookedObserved && !observed.Harvested, "native9.983-second state cannot skip cooking wait");
        Apply(native, probe1710); Check(observed.CookedObserved && observed.CookedFrame == 1710 && !observed.Harvested, "native1710 proves fully Cooked recipe before consumption");
        Apply(native, probe1718); Check(observed.Harvested && observed.HarvestFrame == 1718 && native.EmptyFood(5) && native.Held(3) == 10,
            "native1718 proves original basket empty and exact cooked recipe on same held plate");
        Apply(native, probe1742); Check(native.Attached(49) == 10 && native.Attached(15) == 5 && native.Held(3) == 0,
            "native1742 proves finished plate at selected output and original basket never moved");
        var nativeWork = native.workers[3]!; var final = nativeWork.Actions.Last().DeepClone().AsObject(); final["player"] = 3;
        nativeWork.Actions.Clear(); nativeWork.Active = native.runner.CreateAction(final); nativeWork.Active.IsDone = true; native.CompleteWork();
        Check(native.nearReadyFryers.Count == 0 && native.workers[3] is null && native.Free(10, 5, 15, 49),
            "actual CompleteWork callback validates before releasing exact completed observer and basket leases");

        foreach (string mutation in new[] { "no-cooked-sample", "wrong-catcher", "basket-detached", "basket-reused", "plate-reused", "recipe-reassigned",
            "lost-home-lease", "duplicate-owner", "lost-worker", "burn-guard", "invalid-progress", "output-stolen", "refilled-before-release" })
        {
            var q = Probe(); Apply(q, probe1263);
            if (mutation == "no-cooked-sample") { Reject(() => Apply(q, probe1718), "consumption without earlier Cooked evidence rejects"); continue; }
            if (mutation == "refilled-before-release") { Apply(q, probe1710); Apply(q, probe1718); q.Entity(5)!["composition"] = probe1710["state"]!["entities"]!.AsArray().OfType<JsonObject>().Single(e => I(e["id"]) == 5)["composition"]!.DeepClone(); }
            else switch (mutation)
            {
                case "wrong-catcher": q.chefs[3]["heldEntityId"] = 0; q.chefs[0]["heldEntityId"] = 10; break;
                case "basket-detached": q.Entity(15)!["attachedEntityId"] = 0; break;
                case "basket-reused": q.Entity(5)!["observedOrdinal"] = 5000; break;
                case "plate-reused": q.Entity(10)!["observedOrdinal"] = 5000; break;
                case "recipe-reassigned": q.basketAssignments[5] = 1; break;
                case "lost-home-lease": q.reserved.Remove(15); break;
                case "duplicate-owner": q.workers[0] = new Work("illegal-successor", [], [5], null); break;
                case "lost-worker": q.workers[3] = null; break;
                case "burn-guard": q.Entity(5)!["cookingProgress"] = 19; break;
                case "invalid-progress": q.Entity(5)!["cookingProgress"] = null; break;
                case "output-stolen": q.Entity(49)!["attachedEntityId"] = 13; break;
            }
            Reject(q.ObserveNearReadyFryerHarvests, "lifecycle failure remains fail-closed: " + mutation);
        }
        var original = Make(); original.options = original.options with { NearReadyFryerHarvest = false };
        Check(original.TryRescueFryer(0, 5) && original.fryerRescues.ContainsKey(5) && original.nearReadyFryers.Count == 0,
            "default-off preserves the existing whole-basket rescue fallback");
        return count;
    }
}
