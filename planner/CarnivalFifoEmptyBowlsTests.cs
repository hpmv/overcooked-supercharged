using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int FifoEmptyBowlsSelfTest(JsonObject at13165, JsonObject at11738, JsonObject at12355,
        JsonObject at13247, JsonObject preview, JsonArray events)
    {
        int count = 0;
        void Check(bool value, string reason)
        { if (!value) throw new InvalidOperationException("Empty-bowl FIFO regression: " + reason); count++; }
        CarnivalPlanner Make(JsonObject? fixture = null)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline bowl FIFO fixture attempted native I/O."), null)
            {
                response = (fixture ?? at13165).DeepClone().AsObject(),
                options = new(Lookahead: 8, FifoEmptyBowls: true, DirectVesselThrows: true, PantryChopping: true, DirectPreparedFlavorThrows: true)
            };
            if (p.response["state"] is null) p.response = new() { ["state"] = p.response };
            p.recipes = (preview["response"]?["preview"] ?? preview["preview"] ?? preview)["recipes"]!.AsArray()
                .OfType<JsonObject>().Select(r => CarnivalRecipes.GetRecipe(I(r["recipeId"]))).ToArray();
            p.Refresh(); p.runner = new(_ => throw new InvalidOperationException("Offline bowl FIFO action attempted native I/O."), null);
            foreach (var bowl in p.Stations("bowl")) p.bowlHomes[bowl.EntityId] = p.AttachmentParent(bowl.EntityId);
            p.CaptureSupplyTopology(false);
            // Existing sticky metadata reconstructed from the native issue and
            // urgent-FIFO-return records. Actual trace has no assignment event.
            p.bowlAssignments[3] = 20; p.bowlFlavors[3] = CarnivalRecipes.Raspberry.Id;
            p.bowlAssignments[6] = 24; p.bowlFlavors[6] = CarnivalRecipes.Chocolate.Id;
            if (!p.EmptyFood(8)) p.basketAssignments[8] = 19;
            // Job ownership immediately after CompleteWork, before this frame's
            // new dispatch. Initial resources/actions conservatively retain any
            // already-released resources; no bowl references are dropped.
            var live = new Dictionary<int, JsonObject>();
            foreach (var e in events.OfType<JsonObject>())
            {
                int f = I(e["nativeFrameNearEvent"]); if (f > p.Frame) break;
                if (e["value"] is not JsonObject value) continue;
                int player = I(value["player"]); string? name = e["name"]?.ToString();
                if (name == "plannerJobStart" && f < p.Frame) live[player] = value;
                if (name == "plannerJobComplete" && live.TryGetValue(player, out var prior) && prior["name"]?.ToString() == value["name"]?.ToString()) live.Remove(player);
            }
            foreach (var (player, value) in live)
            {
                var w = new Work(value["name"]!.ToString(), value["actions"]!.AsArray().OfType<JsonObject>().Select(a => a.DeepClone().AsObject()),
                    value["resources"]!.AsArray().Select(I), null);
                p.workers[player] = w; p.reserved.UnionWith(w.OwnedResources);
            }
            return p;
        }
        string Mapping(CarnivalPlanner p) => JsonSerializer.Serialize(new { assignments = p.bowlAssignments.OrderBy(x => x.Key), flavors = p.bowlFlavors.OrderBy(x => x.Key) });
        var disabled = Make(); disabled.options = disabled.options with { FifoEmptyBowls = false };
        string map = Mapping(disabled), native = disabled.response.ToJsonString();
        Check(!disabled.TryReprioritizeEmptyBowls() && Mapping(disabled) == map && disabled.response.ToJsonString() == native, "default off changes neither metadata nor native observations");
        var q = Make(); map = Mapping(q); native = q.response.ToJsonString();
        var originalWorkers = q.workers.ToArray(); var originalReservations = q.reserved.ToHashSet();
        string other = JsonSerializer.Serialize(new { q.bowlHomes, q.basketAssignments, q.mealPlates, q.assembling, q.plating, q.counterSupplies });
        Check(q.Frame == 13165 && q.delivered == 20 && q.workers[1] is null && q.Region(1) == "upper-right", "actual native post-return boundary has idle supplier and head21");
        Check(q.InspectFifoEmptyBowls() is { Near: 6, Far: 3, NearIndex: 24, FarIndex: 20 } && Mapping(q) == map, "pure eligibility selects exact inversion without assigning");
        Check(q.TryReprioritizeEmptyBowls(), "captured GF13165 permits earliest Raspberry in near6");
        Check(q.bowlAssignments[6] == 20 && q.bowlFlavors[6] == CarnivalRecipes.Raspberry.Id && q.bowlAssignments[3] == 24 && q.bowlFlavors[3] == CarnivalRecipes.Chocolate.Id, "both assignment and flavor pairs move atomically");
        Check(q.response.ToJsonString() == native && originalReservations.SetEquals(q.reserved) && originalWorkers.Zip(q.workers).All(p => ReferenceEquals(p.First, p.Second)) &&
            other == JsonSerializer.Serialize(new { q.bowlHomes, q.basketAssignments, q.mealPlates, q.assembling, q.plating, q.counterSupplies }), "no native input/food/clock/order changes and all unrelated ownership/metadata retained");
        map = Mapping(q); q.AssignBowls(); q.AssignBowls();
        Check(!q.TryReprioritizeEmptyBowls() && Mapping(q) == map, "normal assignment and repeated observation never oscillate the swapped pair");
        q.BakeryAndWash("upper-right");
        Check(q.workers[1]?.Name == "supply-Flour" && q.workers[1]!.OwnedResources.Contains(6) && q.workers[1]!.OwnedResources.Contains(18) &&
            q.workers[1]!.Actions.Last()["targetEntityId"]?.GetValue<int>() == 6 && q.bowlAssignments[6] == 20, "unchanged native near supply route now starts actual FIFO batch");

        var assign = Make(); assign.bowlAssignments.Remove(6); assign.bowlFlavors.Remove(6); assign.AssignBowls();
        Check(assign.bowlAssignments[6] == 20 && assign.bowlAssignments[3] == 24, "end-of-AssignBowls integration corrects newly rebound empty near lane");
        var lower = Make(at12355);
        Check(lower.workers[1] is null && lower.Region(1) == "lower-left" && lower.TryReprioritizeEmptyBowls() && lower.bowlAssignments[6] == 20, "captured lower-left idle boundary can update metadata before predictive return");
        var washing = Make(at11738); map = Mapping(washing);
        Check(washing.workers[1]?.Name == "wash-one-native-plate" && !washing.TryReprioritizeEmptyBowls() && Mapping(washing) == map, "GF11738 does not interrupt the actual active washer despite empty bowls");
        var issued = Make(at13247); map = Mapping(issued);
        Check(issued.Food(6).IngredientIds.SequenceEqual(new[] { CarnivalRecipes.Flour.Id }) && !issued.TryReprioritizeEmptyBowls() && Mapping(issued) == map, "actual first Flour acceptance irreversibly closes this swap window");

        foreach (string mutation in new[] { "supplier-work", "supplier-held", "supplier-moving", "supplier-not-controlled", "supplier-use", "missing-pad", "predictive-visit", "unknown-topology", "wrong-original-home", "bowl-ordinal", "home-ordinal", "missing-ordinal", "inactive-bowl", "inactive-home", "detached", "duplicate-parent", "bowl-moved", "home-moved", "missing-clock", "negative-clock", "infinite-clock", "nonzero-clock", "wrong-duration", "food-state", "food-child", "bowl-held", "wrong-flavor", "expired-index", "future-index", "non-donut-index", "duplicate-index", "earlier-pending-donut", "head-in-basket", "near-in-basket", "head-plating", "near-assembling", "head-already-plated", "bowl-reserved", "home-reserved", "board-reserved", "pass-reserved", "pass-occupied", "board-occupied", "addressed-ingredient", "loose-flavor", "linked-work", "linked-queued-action", "linked-active-action", "suspended-work", "bakery-lease", "near-dough-lease", "prepared-flavor-lease" })
        {
            var bad = Make();
            switch (mutation)
            {
                case "supplier-work": bad.workers[1] = new("wash", [], [], null); break;
                case "supplier-held": bad.chefs[1]["heldEntityId"] = 24; break;
                case "supplier-moving": bad.chefs[1]["lastVelocity"]!["x"] = 1; break;
                case "supplier-not-controlled": bad.chefs[1]["controlsEnabled"] = false; break;
                case "supplier-use": bad.response["inputs"]![1]!["use"] = true; break;
                case "missing-pad": bad.response.Remove("inputs"); break;
                case "predictive-visit": bad.predictiveBakeryVisit = new(6, 18, 24, CarnivalRecipes.Chocolate.Id, bad.Frame, bad.Frame + 120, new()); break;
                case "unknown-topology": bad.supplyHomes.Clear(); break;
                case "wrong-original-home": bad.bowlHomes[6] = 14; break;
                case "bowl-ordinal": bad.Entity(6)!["observedOrdinal"] = 600; break;
                case "home-ordinal": bad.Entity(18)!["observedOrdinal"] = 1800; break;
                case "missing-ordinal": bad.Entity(3)!.Remove("observedOrdinal"); break;
                case "inactive-bowl": bad.Entity(3)!["active"] = false; break;
                case "inactive-home": bad.Entity(14)!["active"] = false; break;
                case "detached": bad.Entity(18)!["attachedEntityId"] = 0; break;
                case "duplicate-parent": bad.Entity(24)!["attachedEntityId"] = 6; break;
                case "bowl-moved": bad.Entity(6)!["position"]!["x"] = 22; break;
                case "home-moved": bad.Entity(18)!["position"]!["x"] = 22; break;
                case "missing-clock": bad.Entity(6)!.Remove("mixingProgress"); break;
                case "negative-clock": bad.Entity(6)!["mixingProgress"] = -.1; break;
                case "infinite-clock": bad.Entity(6)!["mixingProgress"] = "Infinity"; break;
                case "nonzero-clock": bad.Entity(6)!["mixingProgress"] = .01; break;
                case "wrong-duration": bad.Entity(6)!["mixingTime"] = 10; break;
                case "food-state": bad.Entity(6)!["composition"]!["state"] = "Mixed"; break;
                case "food-child": bad.Entity(6)!["composition"]!["children"]!.AsArray().Add(new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = CarnivalRecipes.Flour.Id }); break;
                case "bowl-held": bad.chefs[0]["heldEntityId"] = 6; break;
                case "wrong-flavor": bad.bowlFlavors[3] = CarnivalRecipes.Flour.Id; break;
                case "expired-index": bad.bowlAssignments[3] = bad.delivered - 1; break;
                case "future-index": bad.bowlAssignments[6] = 1000; break;
                case "non-donut-index": bad.bowlAssignments[3] = 21; break;
                case "duplicate-index": bad.bowlAssignments[999] = 20; break;
                case "earlier-pending-donut": bad.state["delivered"] = 19; bad.Refresh(); bad.basketAssignments.Clear(); break;
                case "head-in-basket": bad.basketAssignments[5] = 20; break;
                case "near-in-basket": bad.basketAssignments[5] = 24; break;
                case "head-plating": bad.plating.Add(20); break;
                case "near-assembling": bad.assembling.Add(24); break;
                case "head-already-plated": bad.mealPlates[20] = 430; break;
                case "bowl-reserved": bad.reserved.Add(3); break;
                case "home-reserved": bad.reserved.Add(18); break;
                case "board-reserved": bad.reserved.Add(24); break;
                case "pass-reserved": bad.reserved.Add(48); break;
                case "pass-occupied": bad.Entity(48)!["attachedEntityId"] = 430; break;
                case "board-occupied": bad.Entity(24)!["attachedEntityId"] = 430; break;
                case "addressed-ingredient": bad.counterSupplies[48] = (3, CarnivalRecipes.Flour.Id); break;
                case "loose-flavor": bad.Entity(434)!["composition"] = new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = CarnivalRecipes.Raspberry.Id }; break;
                case "linked-work": bad.workers[0] = new("pending-bowl", [], [3], null); break;
                case "linked-queued-action": bad.workers[0] = new("pending-bowl", [bad.A("take", 3)], [], null); break;
                case "linked-active-action": bad.workers[0] = new("pending-bowl", [], [], null) { Active = bad.runner.CreateAction(new JsonObject { ["type"] = "take", ["player"] = 0, ["station"] = "3" }) }; break;
                case "suspended-work": bad.cannonInterruptions[0] = new(0, new("paused-bowl", [], [3], null), 84, 78, bad.Frame); break;
                case "bakery-lease": bad.bakeryLeases[6] = new(6, 24, 18, 37, 5, 17, 36, bad.Frame); break;
                case "near-dough-lease": var work = new Work("dough", [], [], null); bad.nearReadyDoughTransfers[work] = new(0, 6, 18, 8, 20, 24, bad.Frame, []); break;
                case "prepared-flavor-lease": var w = new Work("flavor", [], [], null); bad.preparedFlavorDeliveries[w] = new(6, 18, 24, 71, 24, CarnivalRecipes.Chocolate.Id, bad.Frame, new()); break;
            }
            map = Mapping(bad); native = bad.response.ToJsonString();
            Check(!bad.TryReprioritizeEmptyBowls() && Mapping(bad) == map && bad.response.ToJsonString() == native, "fail-closed nonmutating refusal: " + mutation);
        }
        var same = Make(); same.recipes[24] = same.recipes[20]; same.bowlFlavors[6] = CarnivalRecipes.Raspberry.Id; map = Mapping(same);
        Check(!same.TryReprioritizeEmptyBowls() && Mapping(same) == map, "same flavor is an explicit no-op");
        var ordered = Make(); (ordered.bowlAssignments[3], ordered.bowlAssignments[6]) = (24, 20); (ordered.bowlFlavors[3], ordered.bowlFlavors[6]) = (CarnivalRecipes.Chocolate.Id, CarnivalRecipes.Raspberry.Id); map = Mapping(ordered);
        Check(!ordered.TryReprioritizeEmptyBowls() && Mapping(ordered) == map, "already FIFO near assignment is an explicit no-op");
        return count;
    }
}
