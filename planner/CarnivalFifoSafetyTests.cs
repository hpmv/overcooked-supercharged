using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Native scene/cook snapshots with explicitly synthetic scheduling inventories; never emits game requests.</summary>
    public static int FifoSafetySelfTest(JsonObject initial, JsonObject cooked2334, JsonObject burnt2935)
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new InvalidOperationException("FIFO safety: " + reason); checks++; }
        CarnivalPlanner Make(JsonObject source)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline safety fixture attempted native I/O."), null)
            { response = source.DeepClone().AsObject(), options = new(), recipes = new[] { 130976, 158500, 224216, 125780 }.Select(CarnivalRecipes.GetRecipe).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); return p;
        }
        JsonObject Leaf(int id) => new() { ["type"] = "IngredientAssembledNode", ["id"] = id, ["progress"] = -1, ["children"] = new JsonArray() };
        JsonObject Mix(params int[] ids) => new() { ["type"] = "MixedCompositeAssembledNode", ["state"] = "Unmixed", ["progress"] = .2,
            ["children"] = new JsonArray(ids.Select(id => (JsonNode?)Leaf(id)).ToArray()) };
        CarnivalPlanner Waiting()
        {
            var p = Make(initial); p.chefs[1]["position"] = new JsonObject { ["x"] = 12.8, ["y"] = .05, ["z"] = -20.3 };
            p.chefs[1]["heldEntityId"] = 0; p.chefs[1]["controlsEnabled"] = true;
            var plates = p.AvailablePlates().OrderBy(Id).ToArray();
            if (plates.Length != 4) throw new InvalidOperationException("Expected actual fresh four-plate inventory.");
            // Explicit synthetic counterexample: three native plates are
            // unavailable to the head; one observed clean plate remains free.
            foreach (var plate in plates.Skip(1)) p.reserved.Add(Id(plate));
            int bowl = p.Stations("bowl")[0].EntityId; p.bowlAssignments[bowl] = 0; p.bowlFlavors[bowl] = CarnivalRecipes.Raspberry.Id;
            p.Entity(bowl)!["composition"] = Mix(CarnivalRecipes.Flour.Id, CarnivalRecipes.Egg.Id);
            p.Refresh(); return p;
        }
        var deadlock = Waiting(); int headBowl = deadlock.bowlAssignments.Single().Key;
        Check(deadlock.AvailablePlates().Length == 1 && deadlock.CanAllocatePlate(0) && !deadlock.CanAllocatePlate(1), "last real plate remains reserved for FIFO while future allocation is denied");
        Check(deadlock.RightSupplyNeeded() && deadlock.DirtyCount() == 0 && deadlock.AvailablePlates().Length < 2,
            "synthetic inventory meets the demonstrated structural wait, not the former return predicate");
        var evidence = deadlock.UrgentFifoBakeryEvidence();
        Check(evidence is not null && I(evidence["bowl"]) == headBowl && evidence["missingSupplyIngredientIds"]!.AsArray().Single()!.GetValue<int>() == CarnivalRecipes.Raspberry.Id,
            "escape identifies exact head bowl, missing flavor and observed clean plate");
        deadlock.BakeryAndWash("lower-left");
        Check(deadlock.workers[1]?.Name == "urgent-fifo-return-to-bakery" && deadlock.workers[1]!.Actions.Single()["type"]?.ToString() == "portal",
            "urgent escape uses the ordinary portal action and leaves native inventory untouched");
        var noPlate = Waiting(); foreach (var plate in noPlate.AvailablePlates()) noPlate.reserved.Add(Id(plate));
        Check(noPlate.UrgentFifoBakeryEvidence() is null, "CanAllocatePlate(head) alone cannot substitute for a real clean plate");
        var future = Waiting(); future.recipes[0] = CarnivalRecipes.GetRecipe(158500);
        Check(future.UrgentFifoBakeryEvidence() is null, "future donut demand cannot trigger an urgent departure for a hotdog head");
        foreach (string role in new[] { "sink", "drying" })
        { var p = Waiting(); p.Entity(p.Single(role))!["plateCount"] = 1; Check(p.UrgentFifoBakeryEvidence() is null, "immediate " + role + " work retains priority"); }
        var handoff = Waiting(); handoff.Entity(handoff.Counter(15.6, -19.2))!["attachedEntityId"] = 999;
        Check(handoff.UrgentFifoBakeryEvidence() is null, "occupied dirty handoff retains priority even if metadata is incomplete");
        var full = Waiting(); full.Entity(headBowl)!["composition"] = Mix(CarnivalRecipes.Flour.Id, CarnivalRecipes.Egg.Id, CarnivalRecipes.Raspberry.Id);
        Check(full.UrgentFifoBakeryEvidence() is null, "complete dough still mixing requires processing rather than pantry ingredients");
        var addressed = Waiting(); addressed.counterSupplies[48] = (headBowl, CarnivalRecipes.Raspberry.Id);
        Check(addressed.UrgentFifoBakeryEvidence() is null, "already addressed ingredient supply is not duplicated");
        var loose = Waiting(); var looseEntity = new JsonObject { ["id"] = 999, ["active"] = true, ["name"] = "Raspberry_Chopped", ["components"] = new JsonArray("IngredientContainer"),
            ["composition"] = Leaf(CarnivalRecipes.Raspberry.Id), ["position"] = new JsonObject { ["x"] = 25.2, ["y"] = 1, ["z"] = -12 } };
        loose.entities[999] = looseEntity;
        Check(loose.UrgentFifoBakeryEvidence() is null, "native loose flavor awaits central transfer rather than supplier travel");
        var carried = Waiting(); carried.entities[999] = new JsonObject { ["id"] = 999, ["components"] = new JsonArray("DirtyPlateStack"), ["plateStackKind"] = "dirty", ["plateCount"] = 2 };
        carried.chefs[0]["heldEntityId"] = 999;
        Check(I(carried.UrgentFifoBakeryEvidence()?["carriedDirtyPlateCount"]) == 2, "critical FIFO excursion explicitly records the carried dirty stack it precedes");
        var cooking = Make(cooked2334);
        Check(cooking.ActiveRuinedFood().Length == 0, "actual native cooked frame2334 is accepted");
        var burnt = Make(burnt2935);
        Check(burnt.ActiveRuinedFood().Select(Id).Contains(8), "actual native basket8 Burnt frame2935 is detected");
        bool threw = false; try { burnt.RequireNoActiveRuinedFood(); } catch (InvalidOperationException ex) { threw = ex.Message.Contains("8 DLC08_FrierBasket", StringComparison.Ordinal); }
        Check(threw, "candidate stops with exact entity identity when native ruin is observed");
        foreach (string ignored in new[] { "inactive", "missing-active", "no-composition", "proxy", "empty", "detached-disabled" })
        {
            var p = Make(burnt2935); var e = p.Entity(8)!;
            if (ignored == "inactive") e["active"] = false;
            if (ignored == "missing-active") e.Remove("active");
            if (ignored == "no-composition") e.Remove("composition");
            if (ignored == "proxy") e["name"] = "DLC08_FrierBasket_Rigidbody";
            if (ignored == "empty") e["composition"]!["children"] = new JsonArray();
            if (ignored == "detached-disabled")
            {
                foreach (var parent in p.entities.Values.Where(v => I(v["attachedEntityId"]) == 8)) parent["attachedEntityId"] = 0;
                e["colliders"] = new JsonArray(new JsonObject { ["trigger"] = false, ["enabled"] = false, ["active"] = true });
            }
            Check(!p.ActiveRuinedFood().Select(Id).Contains(8), "discarded or unproven " + ignored + " food does not cause a false failure");
        }
        var warning = Make(burnt2935); warning.Entity(8)!["composition"]!["state"] = "OverDoing"; warning.Entity(8)!["composition"]!["progress"] = 1.5;
        Check(warning.ActiveRuinedFood().Length == 0, "OverDoing warning is not native ruin");
        var overmix = Make(burnt2935); overmix.Entity(8)!["composition"]!["state"] = "Raw";
        overmix.Entity(8)!["composition"]!["children"]![0]!["state"] = "Overmixed";
        Check(overmix.ActiveRuinedFood().Select(Id).Contains(8), "native Overmixed descendant is detected beneath an outer raw cooking wrapper");
        var attached = Make(burnt2935); attached.Entity(8)!["colliders"] = new JsonArray(new JsonObject { ["trigger"] = false, ["enabled"] = false, ["active"] = true });
        Check(attached.ActiveRuinedFood().Select(Id).Contains(8), "native attachment proves a live vessel even when its carry collider is disabled");
        return checks;
    }
}
