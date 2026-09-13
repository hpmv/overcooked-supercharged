using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum WasherPlatePhase { StagingFood, ReadyForWasher, WasherPlating, ReadyForCenter, Recovering }
    private sealed record WasherStationIdentity(int Id, int Ordinal, Point2 Position);
    private sealed class WasherPlateLease(int index, int recipe, int food, int plate, int source, int cleanPass,
        int shared, int output, int stager, int frame, int foodOrdinal, int plateOrdinal, WasherStationIdentity[] stations)
    {
        public readonly int Index = index, Recipe = recipe, Food = food, Plate = plate, Source = source,
            CleanPass = cleanPass, Shared = shared, Output = output, Stager = stager, StartFrame = frame,
            FoodOrdinal = foodOrdinal, PlateOrdinal = plateOrdinal;
        public readonly WasherStationIdentity[] Stations = stations;
        public int[] Resources => new[] { Food, Plate, Source, CleanPass, Shared, Output }.Distinct().ToArray();
        public WasherPlatePhase Phase = WasherPlatePhase.StagingFood;
        public Work? StagingWork, WasherWaiter, WasherWork, RecoveryWork;
        public int RecoveryPlayer = -1, PhaseFrame = frame;
    }
    private WasherPlateLease? washerPlating;

    private JsonObject WasherPlatingStatus() => washerPlating is { } l ? new()
    {
        ["enabled"] = options.WasherSidePlating, ["phase"] = l.Phase.ToString(), ["index"] = l.Index,
        ["recipe"] = l.Recipe, ["food"] = l.Food, ["foodOrdinal"] = l.FoodOrdinal,
        ["plate"] = l.Plate, ["plateOrdinal"] = l.PlateOrdinal, ["source"] = l.Source,
        ["cleanPass"] = l.CleanPass, ["sharedCounter"] = l.Shared, ["output"] = l.Output,
        ["stager"] = l.Stager, ["recoveryPlayer"] = l.RecoveryPlayer, ["startFrame"] = l.StartFrame,
        ["phaseFrame"] = l.PhaseFrame, ["frame"] = Frame
    } : new() { ["enabled"] = options.WasherSidePlating };

    private bool WasherHasImmediateWork() => I(Entity(Single("sink"))?["plateCount"]) > 0 ||
        I(Entity(Single("drying"))?["plateCount"]) > 0 || Attached(Counter(15.6, -19.2)) != 0;

    private (bool Success, double Seconds, JsonArray Legs) WasherPathBudget(int player, Point2 start, params int[] stations)
    {
        double speed = N(chefs[player]["runSpeed"]) * N(chefs[player]["surfaceSpeedMultiplier"]);
        double length = 0; var legs = new JsonArray(); var obstacles = TrafficObstacles(player);
        foreach (int id in stations)
        {
            if (Station(id) is not { } station) return (false, 0, legs);
            var path = Navigation.ToStation(model, start, station, obstacles);
            legs.Add(JsonSerializer.SerializeToNode(new { station = id, path }));
            if (!path.Success || path.Points.Length == 0) return (false, 0, legs);
            length += path.Length; start = path.Points[^1];
        }
        return (speed > 0 && double.IsFinite(speed), speed > 0 ? length / speed : 0, legs);
    }

    private bool TryBeginWasherPlating()
    {
        if (!options.WasherSidePlating || washerPlating is not null || workers[1] is not null || Held(1) != 0 ||
            Region(1) != "lower-left" || !TrafficControlsReady(1) || WasherHasImmediateWork() ||
            trafficYield?.Helper == 1 || trafficYield?.Winner == 1 || NativeHeatObligations().Any()) return false;
        int cleanPass = Counter(15.6, -20.4), shared = Counter(15.6, -18), plate = Attached(cleanPass);
        if (plate == 0 || !IsPlate(Entity(plate)) || !EmptyFood(plate) || HeldByAnyone(plate) ||
            Entity(plate)?["observedOrdinal"] is null || !Free(plate, cleanPass, shared) ||
            Station(shared) is not { } common || !common.Regions.Contains("center") || !common.Regions.Contains("lower-left")) return false;
        foreach (var (index, recipe) in Pending())
        {
            // The native probe proves whole prepared bun assembly. Keep this
            // first option to fully prepared sauce-free meals, so the receiver
            // never needs a condiment operation unavailable on her platform.
            if (IsDonut(recipe) || recipe.RequiredInputs.Any(i => i == CarnivalRecipes.Mustard || i == CarnivalRecipes.Ketchup) ||
                !CanAllocatePlate(index)) continue;
            int food = mealFoods.GetValueOrDefault(index), source = AttachmentParent(food);
            if (food == 0 || source == 0 || !Free(food, source) || Entity(food)?["observedOrdinal"] is null ||
                !CarnivalRecipes.MatchRecipe(Entity(food), recipe.Id, false).ReadyToDeliver ||
                Station(source)?.Regions.Contains("center") != true || source != shared && !EmptyAttachment(shared)) continue;
            int output = MealOutput(plate, index);
            if (output == 0 || output == source || output == cleanPass || output == shared) continue;
            int[] resources = new[] { food, plate, source, cleanPass, shared, output }.Distinct().ToArray();
            if (!Free(resources)) continue;
            var identities = resources.Where(id => id != food && id != plate).Select(id => new WasherStationIdentity(id,
                Entity(id)?["observedOrdinal"]?.GetValue<int>() ?? -1, Station(id)!.Position)).ToArray();
            if (identities.Any(i => i.Ordinal < 0 || Entity(i.Id)?["active"]?.GetValue<bool>() != true || NativeCookingStation(Entity(i.Id)))) continue;
            var washerPath = WasherPathBudget(1, Position(1), cleanPass, shared);
            if (!washerPath.Success || washerPath.Seconds + 2 > 4) continue;
            var choices = new[] { 0, 3 }.Where(p => HeatCookAvailable(p) && TrafficControlsReady(p)).Select(player =>
            {
                var stage = source == shared ? (Success: true, Seconds: 0d, Legs: new JsonArray()) : WasherPathBudget(player, Position(player), source, shared);
                var recover = WasherPathBudget(player, Station(shared)!.Approaches.First(a => a.Region == "center").Position, output);
                return (Player: player, Stage: stage, Recover: recover);
            }).Where(c => c.Stage.Success && c.Recover.Success && c.Stage.Seconds + c.Recover.Seconds + washerPath.Seconds + 4 < 10)
              .OrderBy(c => c.Stage.Seconds).ThenBy(c => c.Player).ToArray();
            if (choices.Length == 0) continue;
            var choice = choices[0];
            var lease = new WasherPlateLease(index, recipe.Id, food, plate, source, cleanPass, shared, output, choice.Player, Frame,
                I(Entity(food)?["observedOrdinal"]), I(Entity(plate)?["observedOrdinal"]), identities);
            foreach (int id in resources) reserved.Add(id);
            assembling.Add(index); plating.Add(index); washerPlating = lease;
            lease.WasherWaiter = new Work("washer-await-prepared-food", [], [], null); workers[1] = lease.WasherWaiter;
            if (source == shared) lease.Phase = WasherPlatePhase.ReadyForWasher;
            else
            {
                if (!Start(choice.Player, "stage-unplated-for-washer-" + (index + 1), [WasherPlateAction("take", source), WasherPlateAction("place", shared)], [], () =>
                {
                    if (Held(choice.Player) != 0 || Attached(shared) != food || !SameObservedEntity(food, lease.FoodOrdinal) ||
                        !CarnivalRecipes.MatchRecipe(Entity(food), recipe.Id, false).ReadyToDeliver)
                        throw new InvalidOperationException("Washer staging failed its exact native unplated food handoff.");
                    lease.Phase = WasherPlatePhase.ReadyForWasher; lease.PhaseFrame = Frame;
                    Log("plannerWasherFoodStaged", WasherPlatingStatus());
                })) throw new InvalidOperationException("Washer staging lost its checked available center chef.");
                lease.StagingWork = workers[choice.Player];
            }
            var evidence = WasherPlatingStatus(); evidence["stagingPath"] = choice.Stage.Legs;
            evidence["washerPath"] = washerPath.Legs; evidence["recoveryPathAtAdmission"] = choice.Recover.Legs;
            evidence["estimatedTotalSeconds"] = choice.Stage.Seconds + choice.Recover.Seconds + washerPath.Seconds + 4;
            evidence["qualification"] = "Optional legal washer-side assembly; sampled path budget is not a throughput or native score proof.";
            Log("plannerWasherPlatingStarted", evidence);
            return true;
        }
        return false;
    }

    private JsonObject WasherPlateAction(string type, int target)
    {
        var action = A(type, target); action["dash"] = false; action["shortDash"] = false; action["timeoutFrames"] = 300;
        return action;
    }

    private void ObserveWasherPlating(WasherPlateLease l)
    {
        if (Frame - l.StartFrame > 900 || Frame - l.PhaseFrame > 360)
            throw new TimeoutException("Washer-side plating exceeded its fixed native phase/transaction deadline.");
        if (l.Index < delivered || recipes[l.Index].Id != l.Recipe || !assembling.Contains(l.Index) || !plating.Contains(l.Index) ||
            l.Resources.Any(id => !reserved.Contains(id)) || !SameObservedEntity(l.Plate, l.PlateOrdinal) || !IsPlate(Entity(l.Plate)) ||
            l.Stations.Any(s => !SameObservedEntity(s.Id, s.Ordinal) || Station(s.Id) is null ||
                Station(s.Id)!.Position.Distance(s.Position) > .05))
            throw new InvalidOperationException("Washer-side plating lost exact recipe, plate, station or resource ownership.");
        bool preparedPlate = CarnivalRecipes.MatchRecipe(Entity(l.Plate), l.Recipe).ReadyToDeliver;
        bool foodPresent = Entity(l.Food) is not null;
        if (foodPresent && (!SameObservedEntity(l.Food, l.FoodOrdinal) || !CarnivalRecipes.MatchRecipe(Entity(l.Food), l.Recipe, false).ReadyToDeliver) ||
            !foodPresent && !preparedPlate || !EmptyFood(l.Plate) && !preparedPlate)
            throw new InvalidOperationException("Washer-side plating changed the exact prepared composition before verified native assembly.");
        int plateHolder = Array.FindIndex(chefs, c => I(c["heldEntityId"]) == l.Plate), plateParent = AttachmentParent(l.Plate);
        int foodHolder = Array.FindIndex(chefs, c => I(c["heldEntityId"]) == l.Food), foodParent = AttachmentParent(l.Food);
        if (l.Phase is WasherPlatePhase.StagingFood or WasherPlatePhase.ReadyForWasher)
        {
            if (!ReferenceEquals(workers[1], l.WasherWaiter) || plateHolder != -1 || plateParent != l.CleanPass || !EmptyFood(l.Plate) ||
                l.Phase == WasherPlatePhase.StagingFood && !ReferenceEquals(workers[l.Stager], l.StagingWork) ||
                !foodPresent || !(foodHolder == l.Stager || foodHolder == -1 && (foodParent == l.Source || foodParent == l.Shared)))
                throw new InvalidOperationException("Washer's clean plate or staged unplated source left its assigned native handoff.");
        }
        else if (l.Phase == WasherPlatePhase.WasherPlating)
        {
            if (!ReferenceEquals(workers[1], l.WasherWork) || plateHolder is not (-1 or 1) ||
                plateHolder == -1 && plateParent != l.CleanPass && plateParent != l.Shared ||
                foodPresent && (foodHolder != -1 || foodParent != l.Shared))
                throw new InvalidOperationException("Washer native assembly changed its designated catcher, plate or shared counter.");
        }
        else
        {
            if (!preparedPlate || foodPresent ||
                l.Phase == WasherPlatePhase.ReadyForCenter && (plateHolder != -1 || plateParent != l.Shared) ||
                l.Phase == WasherPlatePhase.Recovering && (!ReferenceEquals(workers[l.RecoveryPlayer], l.RecoveryWork) ||
                    plateHolder != -1 && plateHolder != l.RecoveryPlayer ||
                    plateHolder == -1 && plateParent != l.Shared && plateParent != l.Output))
                throw new InvalidOperationException("Washer-prepared plate left its exact recovery handoff or changed recipe.");
        }
    }

    private void AdvanceWasherPlating()
    {
        if (washerPlating is not { } l) return;
        ObserveWasherPlating(l);
        if (l.Phase == WasherPlatePhase.ReadyForWasher)
        {
            if (Attached(l.Shared) != l.Food || Held(1) != 0 || !TrafficControlsReady(1))
                throw new InvalidOperationException("Washer food staging completed without a controlled native receiver and exact shared food.");
            workers[1] = null;
            var assemble = WasherPlateAction("assemble", l.Shared); assemble["expectedRecipeId"] = l.Recipe;
            if (!Start(1, "washer-assemble-prepared-hotdog-" + (l.Index + 1),
                [WasherPlateAction("take", l.CleanPass), assemble, WasherPlateAction("place", l.Shared)], [], () =>
            {
                if (Held(1) != 0 || Attached(l.Shared) != l.Plate || !CarnivalRecipes.MatchRecipe(Entity(l.Plate), l.Recipe).ReadyToDeliver || Entity(l.Food) is not null)
                    throw new InvalidOperationException("Washer did not stage the same fully prepared native plate after consuming the original food.");
                l.Phase = WasherPlatePhase.ReadyForCenter; l.PhaseFrame = Frame;
                Log("plannerWasherPreparedPlateStaged", WasherPlatingStatus());
            })) throw new InvalidOperationException("Washer lost its reserved native assembly slot.");
            l.WasherWork = workers[1]; l.Phase = WasherPlatePhase.WasherPlating; l.PhaseFrame = Frame;
        }
    }

    private bool TryRecoverWasherPlate()
    {
        if (washerPlating is not { Phase: WasherPlatePhase.ReadyForCenter } l) return false;
        foreach (int player in new[] { 0, 3 }.Where(p => HeatCookAvailable(p) && !HeatSafetyBlocks(p) && TrafficControlsReady(p)).OrderBy(p => Position(p).Distance(Station(l.Shared)!.Position)))
        {
            var path = WasherPathBudget(player, Position(player), l.Shared, l.Output);
            if (!path.Success || path.Seconds + 1 > 5) continue;
            if (!Start(player, "recover-washer-plated-meal-" + (l.Index + 1),
                [WasherPlateAction("take", l.Shared), WasherPlateAction("place", l.Output)], [], () =>
            {
                if (Held(player) != 0 || Attached(l.Output) != l.Plate || !SameObservedEntity(l.Plate, l.PlateOrdinal) ||
                    !CarnivalRecipes.MatchRecipe(Entity(l.Plate), l.Recipe).ReadyToDeliver)
                    throw new InvalidOperationException("Washer plate recovery did not preserve the exact native meal and final output.");
                mealPlates[l.Index] = l.Plate; mealFoods.Remove(l.Index); assembling.Remove(l.Index); plating.Remove(l.Index);
                Log("plannerWasherPlatingComplete", WasherPlatingStatus());
                foreach (int id in l.Resources) reserved.Remove(id);
                washerPlating = null;
            })) continue;
            l.RecoveryPlayer = player; l.RecoveryWork = workers[player]; l.Phase = WasherPlatePhase.Recovering; l.PhaseFrame = Frame;
            return true;
        }
        return false;
    }
}
