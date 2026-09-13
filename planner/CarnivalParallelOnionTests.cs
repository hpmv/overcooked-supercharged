using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Offline parallel admission and independent native-observation lifecycle fixtures; never calls the game.</summary>
    public static int ParallelOnionSelfTest(JsonObject initialSnapshot)
    {
        int count = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Parallel onion regression: " + message); count++; }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, message);
        }
        var plain = CarnivalRecipes.GetRecipe(296560);
        var complete = CarnivalRecipes.GetRecipe(472326);
        var cooked = complete.ExpectedFood.Children.Single(f => f.CookingStepId == CarnivalRecipes.PanCookingStepId);
        var empty = new FoodObservation(FoodNodeKind.Composite, FoodPreparation.Unknown, 0, "", 0, 0, []);
        JsonObject NativeFood(FoodObservation food) => new()
        {
            ["type"] = food.Kind switch { FoodNodeKind.Ingredient => "IngredientAssembledNode",
                FoodNodeKind.Cooked => "CookedCompositeAssembledNode", _ => "CompositeAssembledNode" },
            ["id"] = food.IngredientId, ["state"] = food.Preparation.ToString(), ["cookingStepId"] = food.CookingStepId,
            ["progress"] = food.Progress, ["children"] = new JsonArray(food.Children.Select(c => (JsonNode?)NativeFood(c)).ToArray())
        };
        int PutFood(CarnivalPlanner p, int parent, FoodObservation food)
        {
            int id = p.entities.Keys.Max() + 1;
            p.entities[id] = new JsonObject { ["id"] = id, ["active"] = true, ["components"] = new JsonArray("CarryableItem"),
                ["composition"] = NativeFood(food), ["position"] = p.Entity(parent)!["position"]!.DeepClone() };
            p.Entity(parent)!["attachedEntityId"] = id;
            return id;
        }
        CarnivalPlanner Make(bool parallel = true, bool baseline = false)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline parallel fixture attempted I/O."), null)
            { response = initialSnapshot.DeepClone().AsObject(), options = new(ParallelOnionHeat: parallel, EarlyOnionHeat: baseline),
                recipes = [complete, complete, complete, plain] };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline parallel action attempted I/O."), null);
            return p;
        }
        void Stock(CarnivalPlanner p) => PutFood(p, p.Board("upper-left", true), cooked.Children.Single());
        void FinishWork(CarnivalPlanner p, int owner)
        {
            var work = p.workers[owner] ?? throw new InvalidOperationException("Missing parallel fixture work.");
            foreach (int id in work.Resources) p.reserved.Remove(id);
            p.workers[owner] = null; work.Complete?.Invoke();
        }
        void PanFood(CarnivalPlanner p, int pan, double progress)
        {
            p.Entity(pan)!["cookingProgress"] = progress;
            p.Entity(pan)!["composition"] = NativeFood(cooked with { Preparation = progress > 12 ? FoodPreparation.Cooked : FoodPreparation.Raw });
        }
        void FinishLoad(CarnivalPlanner p, EarlyOnionLease lease)
        {
            PanFood(p, lease.Pan, .05);
            p.Entity(p.Board("upper-left", true))!["attachedEntityId"] = 0;
            FinishWork(p, lease.Owner);
        }
        CarnivalPlanner TwoHeating()
        {
            var p = Make(); Stock(p);
            Check(p.TryStartParallelOnion(0), "first native empty pan admits a prepared onion");
            FinishLoad(p, p.earlyOnion!); Stock(p);
            Check(p.TryStartParallelOnion(3), "second pan admits a distinct meal while first pan cooks");
            FinishLoad(p, p.secondEarlyOnion!);
            return p;
        }
        void MoveOffHeat(CarnivalPlanner p, EarlyOnionLease lease)
        {
            p.Entity(lease.Home)!["attachedEntityId"] = 0;
            p.Entity(lease.Counter)!["attachedEntityId"] = lease.Pan;
            FinishWork(p, lease.Owner);
        }

        var disabled = Make(false); Stock(disabled);
        Check(!disabled.TryStartParallelOnion(0) && disabled.reserved.Count == 0 && disabled.workers.All(w => w is null),
            "new option defaults off without jobs or resource changes");
        var baseline = Make(false, true); Stock(baseline);
        baseline.Start(0, "finish-prior-meal", [baseline.A("navigate", baseline.Board("upper-left", false))], []);
        Check(!baseline.TryStartEarlyOnion(3), "existing early-onion option keeps its both-central-idle gate");
        var busy = Make(); Stock(busy);
        busy.delivered = 4; busy.recipes = [plain, plain, plain, plain, complete, complete, plain, complete];
        busy.assembling.Add(4);
        busy.Start(0, "assemble-meal-5", [busy.A("navigate", busy.Board("upper-left", false))], []);
        var originalWork = busy.workers[0];
        Check(busy.TryStartParallelOnion(3) && busy.earlyOnion!.Index == 5,
            "V6 admission blocker: one free central chef can heat meal six while meal five is assembling");
        Check(busy.workers[0] == originalWork && busy.earlyOnion!.Owner == 3 && busy.assembling.Contains(4),
            "single-loader admission preserves the other chef's current meal and job");
        Check(busy.workers[3]!.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "place" }),
            "parallel admission schedules only legal prepared-onion transfer actions");
        var uncontrolled = Make(); Stock(uncontrolled); uncontrolled.chefs[3]["controlsEnabled"] = false;
        Check(!uncontrolled.TryStartParallelOnion(0) && uncontrolled.reserved.Count == 0,
            "admission still requires two native controlled central chefs, even though only the loader must be idle");
        var unavailable = Make(); Stock(unavailable); unavailable.chefs[0]["heldEntityId"] = 998;
        Check(!unavailable.TryStartParallelOnion(0), "busy hands cannot be assigned a new onion loader");
        var noParking = Make(); Stock(noParking);
        foreach (var s in noParking.Stations("counter")) noParking.reserved.Add(s.EntityId);
        int reservedBefore = noParking.reserved.Count;
        Check(!noParking.TryStartParallelOnion(0) && noParking.earlyOnion is null && noParking.reserved.Count == reservedBefore,
            "no new cooking without a separate actual empty reserved parking counter");

        var paired = TwoHeating(); var first = paired.earlyOnion!; var second = paired.secondEarlyOnion!;
        Check(first.Index == 0 && second.Index == 1 && first.Pan != second.Pan && !first.Resources.Intersect(second.Resources).Any(),
            "each native pan owns a distinct meal, original stove and storage counter");
        Check(first.Resources.Concat(second.Resources).All(paired.reserved.Contains) && paired.HasEarlyOnionLease(0) && paired.HasEarlyOnionLease(1),
            "ordinary onion assignment excludes both leased meals");
        Check(paired.workers.All(w => w is null) && first.Owner == -1 && second.Owner == -1,
            "native heating releases both loaders for independent work");
        Stock(paired);
        Check(!paired.TryStartParallelOnion(0) && paired.EarlyOnionLeases().Count() == 2,
            "a third chopped onion remains off heat when both pan leases exist");
        Reject(() => paired.RegisterParallelOnion(new(first.Index, second.Pan, second.Home, second.Counter, second.Ordinal, 0, paired.Frame)),
            "duplicate meal or physical resources cannot acquire another lease");

        PanFood(paired, first.Pan, 9.1); PanFood(paired, second.Pan, 11.5);
        int closest = new[] { 0, 3 }.OrderBy(p => paired.Position(p).Distance(paired.Station(second.Pan)!.Position)).First();
        paired.AdvanceEarlyOnion();
        Check(second.Owner == closest && first.Owner != second.Owner && first.Owner >= 0,
            "the hotter pan gets first claim on the nearest free rescue chef; the other gets a distinct owner");
        Check(first.Phase == EarlyOnionPhase.Approach && second.Phase == EarlyOnionPhase.Approach &&
            paired.IsEarlyOnionParticipant(0) && paired.IsEarlyOnionParticipant(3), "both rescue owners are excluded from ordinary dispatch");
        Check(new[] { first, second }.All(l => paired.workers[l.Owner]!.Actions.All(a => !B(a["dash"]) && !B(a["shortDash"]) && I(a["timeoutFrames"]) == 240)),
            "both fine rescue paths retain walking and finite action limits");
        FinishWork(paired, first.Owner); FinishWork(paired, second.Owner);
        PanFood(paired, first.Pan, 12); PanFood(paired, second.Pan, 12);
        paired.AdvanceEarlyOnion();
        Check(first.Phase == EarlyOnionPhase.Waiting && second.Phase == EarlyOnionPhase.Waiting,
            "neither rescue bypasses the native strictly-greater-than-twelve cooking boundary");
        PanFood(paired, first.Pan, 12.02); PanFood(paired, second.Pan, 12.08); paired.AdvanceEarlyOnion();
        Check(first.Phase == EarlyOnionPhase.Rescuing && second.Phase == EarlyOnionPhase.Rescuing,
            "both native cooked observations schedule whole-pan pickup and reserved-counter placement");
        Reject(() => paired.workers[second.Owner]!.Complete!(), "second rescue cannot finish on job completion without observed attachment transfer");
        MoveOffHeat(paired, first); MoveOffHeat(paired, second);
        paired.AdvanceEarlyOnion(); paired.AdvanceEarlyOnion();
        Check(first.StableSamples == 0 && second.StableSamples == 0, "duplicate captures cannot fabricate either offheat proof");
        int frame = paired.Frame; double timer = N(paired.state["timer"]);
        for (int step = 1; step <= 2; step++)
        { paired.state["gameplayFrame"] = frame + step; paired.state["timer"] = timer - step / 60d; paired.AdvanceEarlyOnion(); }
        Check(first.Phase == EarlyOnionPhase.Parked && second.Phase == EarlyOnionPhase.Parked &&
            first.StableSamples == 2 && second.StableSamples == 2 && first.Owner == -1 && second.Owner == -1,
            "each exact cooked pan separately proves unchanged progress and contents across advancing native time");
        Check(first.ParkedProgress != second.ParkedProgress && first.ProofFrame == second.ProofFrame,
            "same-frame rescues retain independent pan progress records");
        foreach (var lease in new[] { first, second })
        {
            paired.Entity(lease.Pan)!["cookingProgress"] = lease.ParkedProgress + .001;
            Reject(paired.AdvanceEarlyOnion, "any extra native heating in pan " + lease.Pan + " fails its own offheat invariant");
            paired.Entity(lease.Pan)!["cookingProgress"] = lease.ParkedProgress;
        }

        // Finish the primary lease first. The second must survive primary=null,
        // retain all exclusions, and later release only its own native resources.
        foreach (var lease in new[] { first, second })
        {
            int source = paired.EmptyCenterCounter(new(19.2, -10.8));
            int meal = PutFood(paired, source, plain.ExpectedFood); paired.mealFoods[lease.Index] = meal;
            Check(paired.TryCombineEarlyOnion(0) && lease.Phase == EarlyOnionPhase.Combining &&
                I(paired.workers[0]!.Actions.ElementAt(1)["station"]) == lease.Pan,
                "assigned plain meal consumes its own parked pan " + lease.Pan);
            paired.Entity(meal)!["composition"] = NativeFood(complete.ExpectedFood);
            paired.Entity(lease.Pan)!["composition"] = NativeFood(empty); paired.Entity(lease.Pan)!["cookingProgress"] = 0;
            FinishWork(paired, 0);
            Check(lease.Phase == EarlyOnionPhase.Restoring && lease.Resources.All(paired.reserved.Contains),
                "native empty-pan proof retains ownership through restoration for pan " + lease.Pan);
            paired.Entity(lease.Counter)!["attachedEntityId"] = 0; paired.Entity(lease.Home)!["attachedEntityId"] = lease.Pan;
            FinishWork(paired, 0);
            Check(!paired.HasEarlyOnionLease(lease.Index) && lease.Resources.All(id => !paired.reserved.Contains(id)),
                "observed native empty restoration releases only the completed lease " + lease.Pan);
            if (lease == first)
            {
                Check(paired.earlyOnion is null && paired.secondEarlyOnion == second && second.Resources.All(paired.reserved.Contains) &&
                    paired.HasEarlyOnionLease(second.Index) && I(paired.EarlyOnionStatus()["pan"]) == second.Pan,
                    "remaining second pan stays fully visible and reserved after primary lease is removed");
                paired.AdvanceEarlyOnion();
            }
        }
        Check(!paired.EarlyOnionLeases().Any() && paired.reserved.Count == 0, "both complete native lifecycles release all resources independently");
        var deadline = TwoHeating(); PanFood(deadline, deadline.secondEarlyOnion!.Pan, 21);
        Reject(deadline.AdvanceEarlyOnion, "second pan enforces the same pre-burn deadline before any input frame");
        Check(deadline.secondEarlyOnion.StableSamples == 0 && deadline.secondEarlyOnion.Phase == EarlyOnionPhase.Heating,
            "deadline rejection does not claim second pan was rescued");

        CarnivalPlanner Ordinary(bool hasBase = true, double progress = 3)
        {
            var p = Make(); Stock(p); int pan = p.Stations("pan").First().EntityId; PanFood(p, pan, progress);
            if (hasBase) p.mealFoods[0] = PutFood(p, p.EmptyCenterCounter(new(19.2, -10.8)), plain.ExpectedFood);
            return p;
        }
        var ordinary = Ordinary(); int ordinaryPan = ordinary.Stations("pan").First().EntityId;
        Check(ordinary.TryStartParallelOnion(0) && ordinary.EarlyOnionLeases().Count() == 2,
            "V6 second blocker: one observed ordinary onion plus its exact plain base permits loading the empty second pan");
        Check(ordinary.earlyOnion!.Index == 0 && ordinary.earlyOnion.Pan == ordinaryPan && ordinary.earlyOnion.Owner == -1 &&
            ordinary.earlyOnion.Phase == EarlyOnionPhase.Heating && N(ordinary.Entity(ordinaryPan)?["cookingProgress"]) == 3 && ordinary.secondEarlyOnion!.Index == 1,
            "ordinary adoption assigns existing food and preserves native heat progress without input or timer changes");
        Check(!ordinary.earlyOnion.Resources.Intersect(ordinary.secondEarlyOnion!.Resources).Any(),
            "adopted ordinary pan also requires a distinct actual parking reservation");
        var noBase = Ordinary(false);
        Check(!noBase.TryStartParallelOnion(0) && noBase.reserved.Count == 0 && !noBase.EarlyOnionLeases().Any(),
            "ordinary pan is not speculatively assigned from recipe counts without an observed plain base");
        var mature = Ordinary(progress: 12.02);
        Check(!mature.TryStartParallelOnion(0) && mature.reserved.Count == 0,
            "mature ordinary onion stays with existing immediate harvest instead of late adoption");
        var locked = Ordinary(); locked.reserved.Add(locked.Stations("pan").First().EntityId);
        Check(!locked.TryStartParallelOnion(0) && !locked.EarlyOnionLeases().Any(),
            "ordinary pan under another native transfer job cannot be stolen");
        var occupied = Ordinary(); PanFood(occupied, occupied.Stations("pan").Last().EntityId, 2);
        Check(!occupied.TryStartParallelOnion(0) && occupied.reserved.Count == 0,
            "adoption does not modify ownership when no physical empty pan can admit additional heat");

        var priority = Make(); priority.recipes = [complete, plain, plain]; Stock(priority);
        priority.mealFoods[0] = PutFood(priority, priority.EmptyCenterCounter(new(19.2, -10.8)), plain.ExpectedFood);
        int bunBoard = priority.Board("upper-left", false), pot = priority.Stations("pot").First().EntityId;
        PutFood(priority, bunBoard, plain.ExpectedFood.Children.Single(n => n.IngredientId == CarnivalRecipes.Bun.Id));
        priority.Entity(pot)!["composition"] = NativeFood(plain.ExpectedFood.Children.Single(n => n.Kind == FoodNodeKind.Cooked));
        priority.Central(0);
        Check(priority.workers[0]?.Name == "early-onion-load-1" && priority.earlyOnion!.Index == 0 && priority.Free(pot, bunBoard),
            "eligible onion heat begins before unrelated future bun assembly while preserving its prepared inputs");
        var earlier = Make(); earlier.recipes = [plain, complete, plain]; Stock(earlier);
        int earlierBoard = earlier.Board("upper-left", false), earlierPot = earlier.Stations("pot").First().EntityId;
        PutFood(earlier, earlierBoard, plain.ExpectedFood.Children.Single(n => n.IngredientId == CarnivalRecipes.Bun.Id));
        earlier.Entity(earlierPot)!["composition"] = NativeFood(plain.ExpectedFood.Children.Single(n => n.Kind == FoodNodeKind.Cooked));
        earlier.Central(0);
        Check(earlier.workers[0]?.Name == "unplated-hotdog-1" && !earlier.EarlyOnionLeases().Any(),
            "an earlier missing hotdog base keeps priority over a later onion's heat window");
        return count;
    }
}
