using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Actual V12 full-storage geometry with explicit offline native-transfer observations.</summary>
    public static int HeatRecoverySelfTest(JsonObject at1104, JsonObject at1200)
    {
        int count = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Heat storage recovery: " + message); count++; }
        CarnivalPlanner Make(JsonObject snapshot)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline recovery fixture attempted game I/O."), null)
            {
                response = snapshot.DeepClone().AsObject(), options = new(StagedResourceRelease: true, BufferChoppedBuns: true, BakeryLookahead: 12),
                recipes = new[] { 158500, 125780, 224216, 228996, 47642, 472326, 257844, 130976 }.Select(CarnivalRecipes.GetRecipe).ToArray()
            };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline recovery fixture attempted I/O."), null);
            p.CapturePotHomes(); p.mealFoods[0] = 141;
            // Reconstruct actual ownership from the closed V12 trace. The
            // raw sausage reserves and parked/parking dough own these slots.
            p.reserved.UnionWith(new[] { 32, 145, 61, 153, 18, 37 });
            p.bowlAssignments[6] = 3; p.bowlFlavors[6] = CarnivalRecipes.Chocolate.Id;
            p.bowlHomes[6] = 18; p.bowlHomes[3] = 14;
            if (p.Frame == 1104)
            {
                p.Start(3, "captured-active-dough-parking", [p.A("place", 37)], []);
                p.workers[3]!.Active = p.runner.CreateAction(p.A("place", 37));
            }
            return p;
        }
        void CompleteNext(CarnivalPlanner p, int player)
        {
            var work = p.workers[player]!; var spec = work.Actions.Dequeue(); spec["player"] = player;
            work.Active = p.runner.CreateAction(spec); work.Active.IsDone = true; p.CompleteWork();
        }
        void ObserveNativePickup(CarnivalPlanner p)
        { p.Entity(23)!["attachedEntityId"] = 0; p.chefs[0]["heldEntityId"] = 151; CompleteNext(p, 0); }
        void ObserveNativeCombine(CarnivalPlanner p)
        {
            // Synthetic post-action observation uses the actual complete
            // unplated food already present in this native capture as shape.
            p.Entity(151)!["composition"] = p.Entity(141)!["composition"]!.DeepClone();
            p.Entity(2)!["composition"]!["children"] = new JsonArray();
            p.Entity(2)!["composition"]!["state"] = "Raw";
            p.Entity(2)!["composition"]!["progress"] = 0;
            p.Entity(2)!["cookingProgress"] = 0;
            p.Entity(2)!["ingredientIds"] = new JsonArray(); p.Entity(2)!["contents"] = new JsonArray();
            CompleteNext(p, 0);
        }
        foreach (var snapshot in new[] { at1104, at1200 })
        {
            var p = Make(snapshot); var other = p.workers[3]; var active = other?.Active;
            string? queue = other is null ? null : JsonSerializer.Serialize(other.Actions);
            int[] otherLocks = other?.OwnedResources.Order().ToArray() ?? [];
            Check(p.EmptyCenterCounter(new(18.8, -13.2)) == 0 && p.Attached(42) == 0 && p.Free(42),
                "actual ordinary storage is full while the protected head workspace remains empty");
            Check(p.Chopped(151, CarnivalRecipes.Bun.Id) && p.NativeSausagePot(2, true) && p.DirectPotHarvestHasTime(0, 2),
                "actual chopped bun and selected cooked pot fit the native route/input allowance");
            Check(!p.BuildUnplatedHotdog(0), "ordinary future production retains its storage restriction");
            p.DispatchNativeHeatSafety();
            var work = p.workers[0]!;
            Check(work.Name == "unplated-hotdog-2" && work.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "combine", "place" }) &&
                StationIs(work.Actions.ElementAt(0), 23) && StationIs(work.Actions.ElementAt(1), 2) && StationIs(work.Actions.ElementAt(2), 23),
                "captured deadlock admits exact pot2 into its own bun board for the addressed second meal");
            Check(work.OwnedResources.SetEquals(new[] { 23, 151, 2 }) && p.Free(42) && p.potRescues.Count == 0,
                "recovery owns only its source bun/board and selected pot; no workspace or parking lease is stolen");
            Check(other is null || ReferenceEquals(other, p.workers[3]) && ReferenceEquals(active, other.Active) &&
                queue == JsonSerializer.Serialize(other.Actions) && otherLocks.SequenceEqual(other.OwnedResources.Order()),
                "the other chef's active work, queue and ownership remain unchanged");
            ObserveNativePickup(p); p.ReleaseCompletedUnplatedResources();
            Check(work.OwnedResources.Contains(23) && p.reserved.Contains(23) && work.OwnedResources.Contains(2),
                "native bun pickup retains the source because its final placement still uses that board");
            Check(!p.Start(2, "invalid-bun-refill", [p.A("place", 23)], [23]), "supplier cannot refill the temporarily empty recovery output");
            // Composition alone cannot release the pot before its ordinary
            // combine action has completed and native progress has reset.
            p.Entity(151)!["composition"] = p.Entity(141)!["composition"]!.DeepClone();
            p.ReleaseCompletedUnplatedResources();
            Check(work.OwnedResources.Contains(2), "held hotdog appearance alone does not release an uncompleted pot transfer");
            ObserveNativeCombine(p); p.ReleaseCompletedUnplatedResources();
            Check(!work.OwnedResources.Contains(2) && p.Free(2) && work.OwnedResources.SetEquals(new[] { 23, 151 }),
                "only completed native combine with empty/reset original pot releases its heat resource");
            Check(p.Start(2, "ordinary-successor-pot-refill", [p.A("place", 2)], [2]), "a verified empty original pot can be refilled while its hotdog is staged");
            var successor = p.workers[2];
            p.Entity(23)!["attachedEntityId"] = 151; p.chefs[0]["heldEntityId"] = 0; CompleteNext(p, 0);
            Check(p.mealFoods[1] == 151 && p.mealFoods[0] == 141 && !p.assembling.Contains(1) && p.Free(23, 151),
                "verified final native placement retains both recipe addresses and releases its board once");
            Check(p.reserved.Contains(2) && ReferenceEquals(successor, p.workers[2]), "finished recovery never releases the successor pot loader's ownership");
        }
        foreach (string defect in new[] { "leased-board", "wrong-bun", "late-pot", "missing-source-identity", "wrong-pot", "occupied-chef" })
        {
            var bad = Make(at1200);
            switch (defect)
            {
                case "leased-board": bad.reserved.Add(23); break;
                case "wrong-bun": bad.Entity(151)!["composition"] = bad.Entity(141)!["composition"]!.DeepClone(); break;
                case "late-pot": bad.Entity(2)!["cookingProgress"] = 22; break;
                case "missing-source-identity": bad.Entity(23)!.Remove("observedOrdinal"); break;
                case "wrong-pot": bad.Entity(2)!["composition"]!["children"]![0]!["id"] = CarnivalRecipes.Onion.Id; break;
                case "occupied-chef": bad.Start(0, "existing-work", [bad.A("take", 12)], [12]); break;
            }
            Check(!bad.CanStageExactPotHarvestOnSource(0, 2, 23, 151), defect + " denies the fallback without game or lease mutation");
        }
        var malformed = Make(at1200); malformed.DispatchNativeHeatSafety();
        ObserveNativePickup(malformed); ObserveNativeCombine(malformed);
        malformed.chefs[0]["heldEntityId"] = 0;
        bool rejected = false;
        try { CompleteNext(malformed, 0); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected && !malformed.mealFoods.ContainsKey(1), "missing final board attachment fails instead of claiming a staged future meal");
        return count;
    }
}
