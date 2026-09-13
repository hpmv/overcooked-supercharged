using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed record SauceCounterIdentity(int Id, int Ordinal, Point2 Position);
    private sealed class SauceStagingSelection(int player, int index, int source, int food, int plate, int output,
        int counter, int dispenser, int button, int baseRecipe, int frame, SauceCounterIdentity[] stations,
        int foodOrdinal, int plateOrdinal, JsonObject evidence)
    {
        public readonly int Player = player, Index = index, Source = source, Food = food, Plate = plate, Output = output,
            Counter = counter, Dispenser = dispenser, Button = button, BaseRecipe = baseRecipe, Frame = frame,
            FoodOrdinal = foodOrdinal, PlateOrdinal = plateOrdinal;
        public readonly SauceCounterIdentity[] Stations = stations;
        public readonly JsonObject Evidence = evidence;
        public bool AssemblyObserved;
        public Work? Work;
        public int[] Resources => [Source, Food, Plate, Output, Counter, Dispenser, Button];
    }
    private readonly Dictionary<Work, SauceStagingSelection> sauceStaging = [];

    private bool ExactSauceStation(int id) => Entity(id) is { } e && B(e["active"]) &&
        e["observedOrdinal"] is not null && Station(id) is not null && !HeldByAnyone(id);

    private SauceStagingSelection? SelectNearSauceStaging(int player, int index, int source, int food, int plate,
        int output, int baseRecipe, IReadOnlyCollection<int> resources, bool emitRefusal = true)
    {
        int dispenserId = Single("condiment"), buttonId = Single("condiment-switch");
        int[] required = [source, output, dispenserId, buttonId];
        if (player is not (0 or 3) || workers[player] is not null || Held(player) != 0 || Region(player) != "center" ||
            !B(chefs[player]["controlsEnabled"]) || !Free(resources.ToArray()) || required.Any(id => !ExactSauceStation(id)) ||
            food == 0 || Attached(source) != food || HeldByAnyone(food) || !CarnivalRecipes.MatchRecipe(Entity(food), baseRecipe, false).ReadyToDeliver ||
            Entity(food)?["observedOrdinal"] is null || Entity(plate)?["observedOrdinal"] is null || !IsPlate(Entity(plate))) return null;
        var dispenser = Station(dispenserId)!; var button = Station(buttonId)!; var obstacles = TrafficObstacles(player);
        // The eventual first application approach depends on other chefs'
        // future movement. Compare every counter from the same currently
        // reachable centered dispenser approach, without using future frames.
        var starts = dispenser.Approaches.Where(a => a.Region == "center")
            .OrderBy(a => a.Position.Distance(dispenser.Position)).ThenBy(a => a.Position.X).ThenBy(a => a.Position.Z)
            .Select(a => (a.Position, Path: Navigation.FindPath(model, Position(player), a.Position, .15, obstacles)))
            .Where(a => a.Path.Success).ToArray();
        if (starts.Length == 0) return null;
        Point2 start = starts[0].Position;
        var candidates = Stations("counter").Where(s => s.Regions.SequenceEqual(new[] { "center" }) &&
                s.Position.X > 18.5 && s.Position.X < 22.1).Select(s => s.EntityId).Append(source).Distinct().Order().ToArray();
        var evidence = new JsonArray(); var feasible = new List<(int Counter, double Length)>();
        foreach (int counter in candidates)
        {
            var station = Station(counter);
            string? reason = counter == output ? "reserved final output" :
                station is null || !ExactSauceStation(counter) ? "missing native station identity" :
                station.Position.Distance(new Point2(20.4, -16.8)) <= .1 ? "protected workspace" :
                !Free(counter) ? "already reserved by another job or persistent lease" :
                NativeCookingStation(Entity(counter)) ? "native processing station" :
                counter == source ? Attached(source) == food ? null : "original source no longer holds the exact base" :
                !EmptyAttachment(counter) ? "not empty at admission" : null;
            var legs = new JsonArray(); double length = 0; Point2 point = start;
            if (reason is null)
            {
                foreach (var target in new[] { station!, button, station!, dispenser })
                {
                    var path = Navigation.ToStation(model, point, target, obstacles);
                    legs.Add(JsonSerializer.SerializeToNode(new { target = target.EntityId, path }));
                    if (!path.Success) { reason = "a required loop leg has no collision-safe approach"; break; }
                    length += path.Length; point = path.Points[^1];
                }
            }
            evidence.Add(new JsonObject { ["counter"] = counter, ["sourceFallback"] = counter == source,
                ["eligible"] = reason is null, ["reason"] = reason, ["totalPathLength"] = reason is null ? length : null,
                ["nativeObservedOrdinal"] = Entity(counter)?["observedOrdinal"]?.DeepClone(), ["legs"] = legs });
            if (reason is null) feasible.Add((counter, length));
        }
        // Micrometre-level path sums are rounded only for deterministic ranking,
        // never to relax native collision clearance or an action success check.
        int selected = BestSauceStagingCounter(feasible);
        var details = new JsonObject { ["player"] = player, ["mealIndex"] = index, ["frame"] = Frame,
            ["source"] = source, ["food"] = food, ["plate"] = plate, ["output"] = output, ["selectedCounter"] = selected,
            ["commonDispenserApproach"] = JsonSerializer.SerializeToNode(start), ["candidates"] = evidence,
            ["reason"] = selected == 0 ? "no complete legal staging loop" : selected == source ? "original source remains best feasible fallback" : "shortest complete feasible temporary-counter loop",
            ["qualification"] = "Admission geometry only; actual ordinary-input navigation rechecks live obstacles. No predicted native time or score." };
        if (selected == 0) { if(emitRefusal)Log("nearSauceStagingRefused", details); return null; }
        var identities = required.Append(selected).Distinct().Select(id =>
            new SauceCounterIdentity(id, I(Entity(id)!["observedOrdinal"]), KitchenModel.Position(Entity(id)!["position"]))).ToArray();
        return new(player, index, source, food, plate, output, selected, dispenserId, buttonId, baseRecipe, Frame,
            identities, I(Entity(food)!["observedOrdinal"]), I(Entity(plate)!["observedOrdinal"]), details);
    }

    private static int BestSauceStagingCounter(IEnumerable<(int Counter, double Length)> candidates) =>
        candidates.OrderBy(c => Math.Round(c.Length, 6)).ThenBy(c => c.Counter).Select(c => c.Counter).FirstOrDefault();

    private void BeginSauceStaging(SauceStagingSelection selection, Work work)
    {
        if (selection.Work is not null || sauceStaging.ContainsKey(work))
            throw new InvalidOperationException("Serial sauce staging was attached to more than one work owner.");
        selection.Work = work; sauceStaging.Add(work, selection);
        RequireSauceStaging(selection, true);
        Log("nearSauceStagingAdmitted", selection.Evidence.DeepClone().AsObject());
    }

    private void RequireSauceStaging(SauceStagingSelection selection, bool ownership)
    {
        foreach (var station in selection.Stations)
            if (!ExactSauceStation(station.Id) || !SameObservedEntity(station.Id, station.Ordinal) ||
                KitchenModel.Position(Entity(station.Id)!["position"]).Distance(station.Position) > .00001)
                throw new InvalidOperationException("Serial sauce staging lost an exact original station identity or position.");
        if (!SameObservedEntity(selection.Plate, selection.PlateOrdinal) || !IsPlate(Entity(selection.Plate)) ||
            Entity(selection.Food) is { } food && I(food["observedOrdinal"]) != selection.FoodOrdinal)
            throw new InvalidOperationException("Serial sauce staging lost its original native plate or reused the consumed base identity.");
        if (ownership && (selection.Work is not { } work ||
            !ReferenceEquals(workers[selection.Player], work) &&
                !cannonInterruptions.Values.Any(i => i.Player == selection.Player && ReferenceEquals(i.Original, work)) ||
            selection.Resources.Any(id => !reserved.Contains(id) || !work.OwnedResources.Contains(id)) ||
            workers.OfType<Work>().Any(other => !ReferenceEquals(other, work) && other.OwnedResources.Overlaps(selection.Resources))))
            throw new InvalidOperationException("Serial sauce staging lost its sole original resource/work owner.");
        if (!selection.AssemblyObserved && CarnivalRecipes.MatchRecipe(Entity(selection.Plate), selection.BaseRecipe).ReadyToDeliver)
            selection.AssemblyObserved = true;
        if (!selection.AssemblyObserved && (Attached(selection.Source) != selection.Food || !SameObservedEntity(selection.Food, selection.FoodOrdinal)))
            throw new InvalidOperationException("Serial sauce base disappeared before its exact native plate assembly was observed.");
        int source = Attached(selection.Source), temp = Attached(selection.Counter), output = Attached(selection.Output);
        if (source != 0 && source != selection.Food && source != selection.Plate ||
            temp != 0 && temp != selection.Plate && !(selection.Counter == selection.Source && temp == selection.Food) ||
            output != 0 && output != selection.Plate)
            throw new InvalidOperationException("Serial sauce staging attachment was occupied by an unrelated native item.");
    }

    private void ObserveSauceStaging()
    {
        foreach (var selection in sauceStaging.Values) RequireSauceStaging(selection, true);
    }

    private void CompleteSauceStaging(SauceStagingSelection? selection, int recipe)
    {
        if (selection is null) return;
        RequireSauceStaging(selection, false);
        if (selection.Work is null || !sauceStaging.ContainsKey(selection.Work) || !selection.AssemblyObserved ||
            Held(selection.Player) != 0 || Attached(selection.Output) != selection.Plate || Attached(selection.Counter) != 0 ||
            !CarnivalRecipes.MatchRecipe(Entity(selection.Plate), recipe).ReadyToDeliver)
            throw new InvalidOperationException("Serial sauce completion lacks its exact finished plate, output and emptied temporary counter.");
        sauceStaging.Remove(selection.Work);
        Log("nearSauceStagingComplete", new JsonObject { ["player"] = selection.Player, ["mealIndex"] = selection.Index,
            ["counter"] = selection.Counter, ["plate"] = selection.Plate, ["output"] = selection.Output, ["frame"] = Frame });
    }
}
