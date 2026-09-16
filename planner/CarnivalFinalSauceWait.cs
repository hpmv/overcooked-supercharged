using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class FinalSauceHeadWait(ImminentHeadWait wait, int recipe, int baseRecipe, int condiment, int dispenser, int button, int dispenserOrdinal, int buttonOrdinal, int[] resources)
    {
        public readonly ImminentHeadWait Wait = wait;
        public readonly int Recipe = recipe, BaseRecipe = baseRecipe, Condiment = condiment, Dispenser = dispenser, Button = button,
            DispenserOrdinal = dispenserOrdinal, ButtonOrdinal = buttonOrdinal;
        public readonly int[] Resources = resources;
        public bool NativeApplied;
        public Point2 AdmissionPose;
        public double AdmissionWalkingSpeed, AdmissionFixedDelta;
        public JsonObject? AdmissionEvidence;
    }
    private FinalSauceHeadWait? finalSauceHead;
    private const int FinalSauceApplyAllowance = 12;

    private bool FinalHeadApply(Work work, int condiment, int dispenser, out int output)
    {
        output = 0;
        JsonObject? applying; JsonObject? placing;
        if (work.Active is { Error: null } active && work.Actions.Count == 1)
        { applying = active.Specification; placing = work.Actions.Peek(); }
        else if (work.Active is null && work.Actions.Count == 2)
        { applying = work.Actions.First(); placing = work.Actions.Last(); }
        else return false;
        return applying["type"]?.ToString() == "apply" && I(applying["expectedIngredientId"]) == condiment &&
            int.TryParse(applying["station"]?.ToString(), out int station) && station == dispenser &&
            placing["type"]?.ToString() == "place" && int.TryParse(placing["station"]?.ToString(), out output);
    }

    private bool FinalSauceOwnerAndLeases(FinalSauceHeadWait selected)
    {
        var wait = selected.Wait;
        return delivered == wait.Index && wait.Index < recipes.Length && recipes[wait.Index].Id == selected.Recipe &&
            ReferenceEquals(workers[wait.Owner], wait.Work) && wait.Work.Name.StartsWith("assemble-meal-" + (wait.Index + 1) + "-", StringComparison.Ordinal) &&
            plating.Contains(wait.Index) && assembling.Contains(wait.Index) &&
            SameObservedEntity(wait.Plate, wait.Ordinal) && B(Entity(wait.Plate)?["active"]) && IsPlate(Entity(wait.Plate)) &&
            SameObservedEntity(wait.Output, wait.OutputOrdinal) && B(Entity(wait.Output)?["active"]) &&
            SameObservedEntity(selected.Dispenser, selected.DispenserOrdinal) && B(Entity(selected.Dispenser)?["active"]) &&
            SameObservedEntity(selected.Button, selected.ButtonOrdinal) && B(Entity(selected.Button)?["active"]) &&
            (wait.Registration < 0 || PlateRegistrationSequence(wait.Plate) < 0 || wait.Registration == PlateRegistrationSequence(wait.Plate)) &&
            wait.Work.OwnedResources.SetEquals(selected.Resources) && selected.Resources.All(reserved.Contains) &&
            workers.OfType<Work>().All(w => ReferenceEquals(w, wait.Work) || !w.OwnedResources.Overlaps(selected.Resources));
    }

    private bool ValidateFinalSauceHead(FinalSauceHeadWait selected, out JsonObject evidence, out string reason, out bool finalPlace)
    {
        var wait = selected.Wait; evidence = new(); finalPlace = false; reason = "final-sauce-native-owner-or-lease-changed";
        if (!FinalSauceOwnerAndLeases(selected) || Frame - wait.Start >= ImminentHeadLimit) return false;
        bool prepared = CarnivalRecipes.MatchRecipe(Entity(wait.Plate), selected.Recipe).ReadyToDeliver;
        if (prepared && !selected.NativeApplied)
        {
            selected.NativeApplied = true;
            Log("imminentFinalSauceNativeApplied", new JsonObject { ["frame"] = Frame, ["startedFrame"] = wait.Start,
                ["owner"] = wait.Owner, ["plate"] = wait.Plate, ["condiment"] = selected.Condiment, ["recipeIndex"] = wait.Index });
        }
        if (prepared && FinalHeadPlace(wait.Owner, wait.Work, wait.Index, wait.Plate, out int finalOutput, out _) && finalOutput == wait.Output)
        { finalPlace = true; return true; }
        reason = "not-the-existing-final-native-condiment-application";
        if (!FinalHeadApply(wait.Work, selected.Condiment, selected.Dispenser, out int output) || output != wait.Output ||
            Held(wait.Owner) != wait.Plate || Region(wait.Owner) != "center" || !B(chefs[wait.Owner]["controlsEnabled"]) ||
            !B(chefs[wait.Owner]["canAcceptInput"]) || B(chefs[wait.Owner]["inputSuppressed"]) || B(chefs[wait.Owner]["aimingThrow"]) ||
            B(chefs[wait.Owner]["respawning"]) || NativeCannonFlight(wait.Owner) || Attached(output) != 0 ||
            I(chefs[wait.Owner]["placementTargetId"]) != selected.Dispenser) return false;
        if (!prepared && (selected.NativeApplied || !CarnivalRecipes.MatchRecipe(Entity(wait.Plate), selected.BaseRecipe).ReadyToDeliver))
        { reason = "prepared-base-or-single-missing-condiment-changed"; return false; }
        int expectedSwitch = selected.Condiment == CarnivalRecipes.Ketchup.Id ? CarnivalRecipes.KetchupSwitchIndex : CarnivalRecipes.MustardSwitchIndex;
        if (I(Entity(selected.Dispenser)?["switchIndex"]) != expectedSwitch || !Food(selected.Dispenser).IngredientIds.SequenceEqual([selected.Condiment]))
        { reason = "native-dispenser-no-longer-offers-exact-final-condiment"; return false; }
        double speed = N(chefs[wait.Owner]["runSpeed"]) * N(chefs[wait.Owner]["surfaceSpeedMultiplier"]);
        double fixedDelta = N(state["fixedDeltaTime"]);
        var position = Position(wait.Owner);
        var cached = KitchenModel.Position(chefs[wait.Owner]["lastVelocity"]);
        var velocity = KitchenModel.Position(chefs[wait.Owner]["velocity"]);
        var impact = KitchenModel.Position(chefs[wait.Owner]["impactVelocity"]);
        if (!double.IsFinite(speed) || speed <= 0 || !double.IsFinite(fixedDelta) || Math.Abs(fixedDelta - .02) > .00001 ||
            !double.IsFinite(position.X) || !double.IsFinite(position.Z) ||
            !double.IsFinite(cached.Distance(default)) || cached.Distance(default) > speed + .001 ||
            !double.IsFinite(velocity.Distance(default)) || velocity.Distance(default) > speed + .001 ||
            !double.IsFinite(impact.Distance(default)) || impact.Distance(default) > .001 ||
            !double.IsFinite(N(chefs[wait.Owner]["dashTimer"])) || N(chefs[wait.Owner]["dashTimer"]) > 0)
        { reason = "native-final-application-motion-is-not-bounded-walking"; return false; }
        if (selected.AdmissionEvidence is { } initial)
        {
            // A native application can finish inside the conservative contact
            // margin (captured11104), before its next physics step separates the
            // chef. Preserve only the service chef's wait for this same action:
            // no synthetic path, owner movement or new deadline is introduced.
            int elapsed = Frame - wait.Start;
            double displacement = position.Distance(selected.AdmissionPose);
            double bound = 2 * selected.AdmissionWalkingSpeed * selected.AdmissionFixedDelta;
            if (elapsed < 0 || elapsed >= FinalSauceApplyAllowance || displacement > bound ||
                Math.Abs(speed - selected.AdmissionWalkingSpeed) > .00001 || Math.Abs(fixedDelta - selected.AdmissionFixedDelta) > .000001)
            { reason = "original-final-application-envelope-exhausted-or-changed"; return false; }
            evidence = initial.DeepClone().AsObject();
            evidence["nativeFinalRecipeObserved"] = prepared;
            evidence["originalAdmissionEvidenceRetained"] = true;
            evidence["pendingApplicationElapsedFrames"] = elapsed;
            evidence["pendingApplicationDisplacement"] = displacement;
            evidence["pendingApplicationDisplacementLimit"] = bound;
            evidence["applyAllowanceFrames"] = FinalSauceApplyAllowance - elapsed;
            evidence["remainingWaitFrames"] = ImminentHeadLimit - elapsed;
            return true;
        }
        var path = Navigation.ToStation(model, position, Station(output)!, TrafficObstacles(wait.Owner));
        double budget = speed > 0 && double.IsFinite(speed) ? path.Length / speed * 60 + ImminentTransferAllowance + (prepared ? 0 : FinalSauceApplyAllowance) : double.PositiveInfinity;
        int remaining = ImminentHeadLimit - (Frame - wait.Start);
        if (!path.Success || budget > remaining) { reason = "final-apply-clear-walk-and-place-budget-exceeded"; return false; }
        evidence = new JsonObject { ["nativeFinalSauceTargetObserved"] = true, ["condiment"] = selected.Condiment, ["dispenser"] = selected.Dispenser,
            ["baseRecipeId"] = selected.BaseRecipe, ["nativeFinalRecipeObserved"] = prepared, ["path"] = JsonSerializer.SerializeToNode(path),
            ["walkingSpeed"] = speed, ["applyAllowanceFrames"] = prepared ? 0 : FinalSauceApplyAllowance,
            ["estimatedRemainingFramesWithTransferAllowance"] = budget, ["remainingWaitFrames"] = remaining };
        selected.AdmissionPose = position; selected.AdmissionWalkingSpeed = speed; selected.AdmissionFixedDelta = fixedDelta;
        selected.AdmissionEvidence = evidence.DeepClone().AsObject();
        return true;
    }

    private bool TryBeginFinalSauceHead(out string reason)
    {
        reason = "no-central-chef-holds-the-exact-complete-FIFO-plate";
        if (!options.WaitForFinalSauceHead || !options.WaitForImminentHead || finalSauceHead is not null ||
            delivered >= recipes.Length || IsDonut(recipes[delivered])) return false;
        var recipe = recipes[delivered];
        foreach (int owner in new[] { 0, 3 })
        {
            int plate = Held(owner);
            if (plate == 0 || workers[owner] is not { } work || !IsPlate(Entity(plate)) ||
                Entity(plate)?["observedOrdinal"] is null || I(Entity(plate)?["observedOrdinal"]) < 0) continue;
            var missing = recipe.RequiredInputs.Select(i => i.Id).Except(Food(plate).IngredientIds).ToArray();
            if (missing.Length != 1 || missing[0] != CarnivalRecipes.Mustard.Id && missing[0] != CarnivalRecipes.Ketchup.Id) continue;
            int condiment = missing[0], dispenser = Single("condiment"), button = Single("condiment-switch");
            var requiredBase = recipe.RequiredInputs.Select(i => i.Id).Where(id => id != condiment).Order().ToArray();
            var baseRecipe = CarnivalRecipes.All.FirstOrDefault(r => r.RequiredInputs.Select(i => i.Id).Order().SequenceEqual(requiredBase));
            if (baseRecipe is null || !CarnivalRecipes.MatchRecipe(Entity(plate), baseRecipe.Id).ReadyToDeliver ||
                !FinalHeadApply(work, condiment, dispenser, out int output) || Station(output) is not { Role: "counter" } station ||
                !station.Regions.Contains("center") || !station.Regions.Contains("lower-right") ||
                new[] { output, dispenser, button }.Any(id => Entity(id)?["observedOrdinal"] is null || I(Entity(id)?["observedOrdinal"]) < 0) ||
                !new[] { plate, output, dispenser, button }.All(work.OwnedResources.Contains)) continue;
            var wait = new ImminentHeadWait(delivered, owner, plate, output, I(Entity(plate)?["observedOrdinal"]),
                I(Entity(output)?["observedOrdinal"]), PlateRegistrationSequence(plate), work, Frame);
            var selected = new FinalSauceHeadWait(wait, recipe.Id, baseRecipe.Id, condiment, dispenser, button,
                I(Entity(dispenser)?["observedOrdinal"]), I(Entity(button)?["observedOrdinal"]), work.OwnedResources.Order().ToArray());
            if (!ValidateFinalSauceHead(selected, out var evidence, out reason, out _)) continue;
            finalSauceHead = selected; imminentHead = wait;
            var admitted = ImminentHeadStatus(wait); admitted["evidence"] = evidence;
            admitted["qualification"] = "Optional wait for this existing exact final condiment action and output; original120-frame visit/head bound retained";
            Log("imminentFinalSauceWaitStarted", admitted); return true;
        }
        return false;
    }
}
