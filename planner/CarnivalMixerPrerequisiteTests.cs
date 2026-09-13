using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int MixerPrerequisiteSelfTest(JsonObject at9469, JsonObject at9616, JsonObject at9929)
    {
        int checks = 0;
        void Check(bool condition, string reason) { if (!condition) throw new InvalidOperationException("Mixer prerequisite regression: " + reason); checks++; }
        void Reject(Action action, string reason) { bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; } Check(rejected, reason); }
        CarnivalPlanner Make(JsonObject snapshot)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline mixer fixture attempted game I/O."), null)
            { response = snapshot.DeepClone().AsObject(), options = new(Seed:59, DirectVesselThrows:true, PantryChopping:true) };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560), 30).ToArray();
            p.recipes[13] = p.recipes[18] = CarnivalRecipes.GetRecipe(228996); p.recipes[12] = CarnivalRecipes.GetRecipe(130976);
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline mixer fixture attempted game I/O."), null);
            foreach (var bowl in p.Stations("bowl")) p.bowlHomes[bowl.EntityId] = p.AttachmentParent(bowl.EntityId);
            p.bowlAssignments[3] = 13; p.bowlAssignments[6] = 18;
            p.bowlFlavors[3] = p.bowlFlavors[6] = CarnivalRecipes.Chocolate.Id;
            p.CaptureSupplyTopology(false);
            return p;
        }
        JsonObject Leaf(int ingredient) => new() { ["type"] = "IngredientAssembledNode", ["id"] = ingredient, ["children"] = new JsonArray() };
        JsonObject MixedNode(JsonObject food) => food["type"]?.ToString() == "MixedCompositeAssembledNode" ? food :
            food["children"]!.AsArray().OfType<JsonObject>().Select(MixedNode).First();
        void Add(CarnivalPlanner p, int bowl, int ingredient) => MixedNode(p.Entity(bowl)!["composition"]!.AsObject())["children"]!.AsArray().Add(Leaf(ingredient));
        void StageEgg(CarnivalPlanner p)
        {
            // Explicit synthetic successor observation using the recorded
            // prepared entity's stable identity as an addressed raw egg.
            var item = p.Entity(337)!; item["name"] = "Egg"; item["composition"] = Leaf(CarnivalRecipes.Egg.Id);
            item["contents"] = new JsonArray(Leaf(CarnivalRecipes.Egg.Id));
            p.Entity(24)!["attachedEntityId"] = 0; p.Entity(48)!["attachedEntityId"] = 337;
            p.counterSupplies[48] = (3, CarnivalRecipes.Egg.Id);
        }
        var supplier = Make(at9469);
        Check(supplier.Food(3).IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Flour.Id }) && supplier.Food(6).IngredientIds.Length == 2,
            "captured9469 distinguishes old Flour-only far bowl from young Flour+Egg near bowl");
        Check(supplier.TrySupplyUrgentMixerPrerequisite() && supplier.workers[1]?.Name == "supply-Egg", "idle native P1 produces old bowl's missing Egg before near flavor or washing");
        Check(supplier.counterSupplies[48] == (3, CarnivalRecipes.Egg.Id) && supplier.workers[1]!.Actions.Last()["station"]?.ToString() == "48",
            "far Egg uses the ordinary exact addressed handoff rather than a far-bowl throw");
        Check(supplier.workers[1]!.OwnedResources.Contains(3) && supplier.workers[0] is null && supplier.workers[3] is null,
            "supplier retains exact vessel ownership and never resets a center action");
        var occupied = Make(at9469); occupied.Start(1, "existing-input-action", [occupied.A("chop",24)], [24]); var existing = occupied.workers[1];
        Check(!occupied.TrySupplyUrgentMixerPrerequisite() && ReferenceEquals(existing, occupied.workers[1]), "ongoing pantry input action is preserved");
        var young = Make(at9469); young.Entity(3)!["mixingProgress"] = 8.9;
        Check(!young.TrySupplyUrgentMixerPrerequisite(), "normal young-bowl ordering remains unchanged");
        var detached = Make(at9469); detached.Entity(14)!["attachedEntityId"] = 0;
        Check(!detached.TrySupplyUrgentMixerPrerequisite(), "off-mixer partial dough does not create an urgent live clock");
        var queued = Make(at9469); queued.counterSupplies[48] = (3, CarnivalRecipes.Egg.Id);
        Check(queued.TrySupplyUrgentMixerPrerequisite() && queued.workers[1] is null, "pending exact addressed Egg is neither duplicated nor abandoned for washing");

        var prepared = Make(at9616);
        Check(prepared.ExactMissingFlavorBoard(3) == 24 && prepared.Attached(24) == 337,
            "actual9616 chopped Chocolate is admissible for Flour-only bowl without pretending Egg is present");
        var heat = prepared.NativeHeatObligations().Single(h => h.Vessel == 3);
        var feasible = new[] {0,3}.Select(player => (player, path:prepared.HeatInteractionPath(player, heat)))
            .Where(p => p.path.Success && p.path.Length / (N(prepared.chefs[p.player]["runSpeed"]) * N(prepared.chefs[p.player]["surfaceSpeedMultiplier"])) + 2 < heat.Remaining).ToArray();
        Check(feasible.Length > 0, "actual9616 native board-to-old-bowl placement fits at least one existing guarded walking budget");
        prepared.DispatchNativeHeatSafety();
        int owner = Array.FindIndex(prepared.workers, w => w?.Name == "heat-deadline-add-exact-flavor-14");
        Check(owner >= 0 && prepared.workers[owner]!.OwnedResources.SetEquals(new[] {3,24,337}), "common earliest-deadline arbitration assigns one exact native flavor transfer");
        Check(!prepared.NativeHeatObligations().Any(h => h.Vessel == 3), "claimed partial bowl cannot be assigned to both centers");
        Add(prepared,3,CarnivalRecipes.Chocolate.Id); prepared.Entity(24)!["attachedEntityId"] = 0; prepared.Entity(3)!["mixingProgress"] = 6.1;
        prepared.workers[owner]!.Complete!();
        Check(prepared.Food(3).IngredientIds.Order().SequenceEqual(new[] {18448,22804}) && !prepared.AssignedDoughIngredients(3,13),
            "native accepted flavor completion permits missing Egg and never declares complete dough");

        var bad = Make(at9616); Add(bad,3,CarnivalRecipes.Flour.Id);
        Check(bad.ExactMissingFlavorBoard(3) == 0, "duplicate Flour is not a valid recipe subset");
        var wrong = Make(at9616); Add(wrong,3,CarnivalRecipes.Raspberry.Id);
        Check(wrong.ExactMissingFlavorBoard(3) == 0, "wrong-flavor partial bowl is never repaired as Chocolate");
        var reserved = Make(at9616); reserved.reserved.Add(24);
        Check(!reserved.TryAddDeadlineFlavor(0,3) && reserved.reserved.Contains(24), "active source-board lease is not stolen");
        foreach (string field in new[] { "home", "board", "recipe", "delta" })
        {
            var mutation = Make(at9616); Check(mutation.TryAddDeadlineFlavor(0,3), "fixture flavor transfer starts before " + field + " mutation");
            Add(mutation,3,CarnivalRecipes.Chocolate.Id); mutation.Entity(24)!["attachedEntityId"] = 0;
            if (field == "home") mutation.Entity(14)!["observedOrdinal"] = 999;
            if (field == "board") mutation.Entity(24)!["observedOrdinal"] = 999;
            if (field == "recipe") mutation.bowlAssignments[3] = 18;
            if (field == "delta") Add(mutation,3,CarnivalRecipes.Egg.Id);
            Reject(mutation.workers[0]!.Complete!, "native flavor completion rejects " + field + " mutation");
        }
        var raw = Make(at9616); var item = raw.Entity(337)!; item["name"] = "Chocolate"; item["composition"] = null; item["contents"] = new JsonArray();
        item["components"] = new JsonArray("WorkableItem","ServerWorkableItem"); item["workProgress"] = .4;
        Check(raw.ExactRawFlavorBoard(3) == 24, "exact raw flavor chopping is a prerequisite even when Egg is also missing");

        var relay = Make(at9616); StageEgg(relay);
        Check(relay.ExactMissingMixerSupply(3) == 48 && relay.NativeHeatObligations().Single(h=>h.Vessel==3).Kind == "partial-mixer-ingredient",
            "addressed Egg becomes an actual heat-changing prerequisite");
        Check(relay.TryAddDeadlineMixerSupply(0,3) && relay.workers[0]!.OwnedResources.SetEquals(new[] {3,48,337}),
            "exact Egg relay uses ordinary take/place and reserves all participants");
        Add(relay,3,CarnivalRecipes.Egg.Id); relay.Entity(48)!["attachedEntityId"] = 0; relay.Entity(3)!["mixingProgress"] = 6.2;
        relay.workers[0]!.Complete!();
        Check(!relay.counterSupplies.ContainsKey(48) && !relay.AssignedDoughIngredients(3,13), "accepted Egg clears only its own address without manufacturing Chocolate");
        var otherAddress = Make(at9616); StageEgg(otherAddress); otherAddress.counterSupplies[48] = (6, CarnivalRecipes.Egg.Id);
        Check(otherAddress.ExactMissingMixerSupply(3) == 0, "another bowl's explicit ingredient address cannot be reassigned");
        foreach (string field in new[] {"address","identity","delta"})
        {
            var mutation = Make(at9616); StageEgg(mutation); Check(mutation.TryAddDeadlineMixerSupply(0,3), "fixture Egg relay starts before " + field + " mutation");
            Add(mutation,3,CarnivalRecipes.Egg.Id); mutation.Entity(48)!["attachedEntityId"] = 0;
            if (field == "address") mutation.counterSupplies[48] = (6,CarnivalRecipes.Egg.Id);
            if (field == "identity") mutation.Entity(3)!["observedOrdinal"] = 999;
            if (field == "delta") Add(mutation,3,CarnivalRecipes.Chocolate.Id);
            Reject(mutation.workers[0]!.Complete!, "native Egg completion rejects " + field + " mutation");
        }
        var late = Make(at9929); bool timeout = false;
        try { _ = late.NativeHeatObligations().ToArray(); } catch (TimeoutException) { timeout = true; }
        Check(timeout, "captured9929 keeps the native21-second safety guard; late state is never silently restored");
        return checks;
    }
}
