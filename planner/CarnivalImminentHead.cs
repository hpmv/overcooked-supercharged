using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class ImminentHeadWait(int index, int owner, int plate, int output, int ordinal, int outputOrdinal, long registration, Work work, int frame)
    {
        public readonly int Index = index, Owner = owner, Plate = plate, Output = output, Ordinal = ordinal, OutputOrdinal = outputOrdinal, Start = frame;
        public readonly long Registration = registration;
        public readonly Work Work = work;
    }
    private ImminentHeadWait? imminentHead;
    private readonly HashSet<int> imminentAttempted = [];
    private const int ImminentHeadLimit = 120, ImminentTransferAllowance = 12;

    private void ResetImminentHeadVisit() { imminentHead = null; finalSauceHead = null; imminentAttempted.Clear(); }
    private void ObserveImminentHeadArrival()
    {
        if (imminentHead is not { } wait) return;
        if (delivered != wait.Index || HasAccessibleHead("lower-right"))
        { EndImminentHeadWait(delivered != wait.Index ? "FIFO-head-changed" : "native-FIFO-head-ready-for-ordinary-collection"); }
    }
    private void EndImminentHeadWait(string reason)
    {
        if (imminentHead is not { } wait) return;
        var e = ImminentHeadStatus(wait); e["reason"] = reason; Log("imminentHeadWaitEnded", e); imminentHead = null; finalSauceHead = null;
    }
    private JsonObject ImminentHeadStatus(ImminentHeadWait wait) => new() { ["frame"] = Frame, ["orderIndex"] = wait.Index,
        ["owner"] = wait.Owner, ["plate"] = wait.Plate, ["plateOrdinal"] = wait.Ordinal, ["registrationSequence"] = wait.Registration,
        ["output"] = wait.Output, ["outputOrdinal"] = wait.OutputOrdinal, ["startedFrame"] = wait.Start, ["elapsedFrames"] = Frame - wait.Start,
        ["limitFrames"] = ImminentHeadLimit, ["ownerJob"] = wait.Work.Name };

    private bool FinalHeadPlace(int owner, Work work, int index, int plate, out int output, out string reason)
    {
        output = 0; reason = "not-exclusively-final-placement";
        var action = work.Active is { } active && work.Actions.Count == 0 && active.Error is null ? active.Specification :
            work.Active is null && work.Actions.Count == 1 ? work.Actions.Peek() : null;
        if (action?["type"]?.ToString() != "place" || !int.TryParse(action["station"]?.ToString(), out output)) return false;
        if (Station(output) is not { Role: "counter" } station || !station.Regions.Contains("lower-right") || !station.Regions.Contains("center") ||
            !B(Entity(output)?["active"]) || !reserved.Contains(output) || !reserved.Contains(plate)) { reason = "output-or-plate-lease-unavailable"; return false; }
        bool serial = work.Name.StartsWith("assemble-meal-" + (index + 1) + "-", StringComparison.Ordinal) && plating.Contains(index) &&
            work.OwnedResources.Contains(plate) && work.OwnedResources.Contains(output);
        bool cooperative = sauceLease is { Phase: SaucePhase.StageMeal } lease && lease.Index == index && lease.Owner == owner &&
            lease.Plate == plate && lease.Output == output && lease.Resources.Contains(plate) && lease.Resources.Contains(output) &&
            work.Name == "cooperative-sauce-" + (index + 1) + "-stage-complete-meal";
        if (!serial && !cooperative) { reason = "not-the-FIFO-assembly-owner"; return false; }
        return true;
    }

    private bool ValidateImminentHead(ImminentHeadWait wait, out JsonObject evidence, out string reason)
    {
        evidence = new(); reason = "native-identity-recipe-or-owner-changed";
        if (finalSauceHead is { } sauce && ReferenceEquals(sauce.Wait, wait))
        {
            if (!ValidateFinalSauceHead(sauce, out evidence, out reason, out bool finalPlace)) return false;
            if (!finalPlace) return true;
        }
        if (delivered != wait.Index || wait.Index >= recipes.Length || !ReferenceEquals(workers[wait.Owner], wait.Work) ||
            Entity(wait.Plate) is not { } plate || !B(plate["active"]) || I(plate["observedOrdinal"]) != wait.Ordinal ||
            I(Entity(wait.Output)?["observedOrdinal"]) != wait.OutputOrdinal ||
            wait.Registration >= 0 && PlateRegistrationSequence(wait.Plate) >= 0 && PlateRegistrationSequence(wait.Plate) != wait.Registration ||
            !CarnivalRecipes.MatchRecipe(plate, recipes[wait.Index].Id).ReadyToDeliver ||
            !FinalHeadPlace(wait.Owner, wait.Work, wait.Index, wait.Plate, out int output, out reason) || output != wait.Output) return false;
        // Native placement briefly precedes the action-completion callback. The
        // exact reserved plate attached to its intended output is success in
        // progress, not an unexpected loss of the held object. Collection still
        // waits for the ordinary meal callback and no new timer is started.
        if (Held(wait.Owner) == 0 && Attached(output) == wait.Plate)
        { evidence["nativeAttachmentObserved"] = true; return true; }
        if (Held(wait.Owner) != wait.Plate || Attached(output) != 0 || Region(wait.Owner) != "center" || !B(chefs[wait.Owner]["controlsEnabled"]))
        { reason = "held-owner-or-empty-output-changed"; return false; }
        // Native final-placement targeting can be valid after the chef steps
        // inside our extra pathfinding margin. V16 frame1508 already targeted
        // exact49, then ordinary pickup placed the same plate at1509, although
        // recomputing a walk to a conservative approach rejected the wait.
        // Keep the original deadline and every owner/recipe/output check above;
        // this observation authorizes only waiting for the existing action.
        if (I(chefs[wait.Owner]["placementTargetId"]) == output)
        {
            evidence["nativePlacementTargetObserved"] = true;
            evidence["remainingWaitFrames"] = ImminentHeadLimit - (Frame - wait.Start);
            return Frame - wait.Start < ImminentHeadLimit;
        }
        var path = Navigation.ToStation(model, Position(wait.Owner), Station(output)!, TrafficObstacles(wait.Owner));
        double speed = N(chefs[wait.Owner]["runSpeed"]) * N(chefs[wait.Owner]["surfaceSpeedMultiplier"]);
        double budget = speed > 0 && double.IsFinite(speed) ? path.Length / speed * 60 + ImminentTransferAllowance : double.PositiveInfinity;
        int remaining = ImminentHeadLimit - (Frame - wait.Start);
        if (!path.Success || budget > remaining) { reason = "remaining-clear-walk-and-transfer-budget-exceeded"; return false; }
        evidence["path"] = JsonSerializer.SerializeToNode(path); evidence["walkingSpeed"] = speed;
        evidence["estimatedRemainingFramesWithTransferAllowance"] = budget; evidence["remainingWaitFrames"] = remaining;
        return true;
    }

    private bool TryWaitForImminentHead()
    {
        if (!options.WaitForImminentHead || Held(2) != 0 || Region(2) != "lower-right") return false;
        if (imminentHead is { } current)
        {
            if (Frame - current.Start >= ImminentHeadLimit) { EndImminentHeadWait("120-native-frame-bound-exhausted"); return false; }
            if (!ValidateImminentHead(current, out _, out string reason)) { EndImminentHeadWait(reason); return false; }
            return true;
        }
        if (delivered >= recipes.Length || !imminentAttempted.Add(delivered)) return false;
        string lastReason = "no-central-chef-holds-the-exact-complete-FIFO-plate";
        foreach (int owner in new[] { 0, 3 })
        {
            int plate = Held(owner);
            if (plate == 0 || workers[owner] is not { } work || !CarnivalRecipes.MatchRecipe(Entity(plate), recipes[delivered].Id).ReadyToDeliver ||
                !FinalHeadPlace(owner, work, delivered, plate, out int output, out lastReason)) continue;
            var wait = new ImminentHeadWait(delivered, owner, plate, output, I(Entity(plate)?["observedOrdinal"]), I(Entity(output)?["observedOrdinal"]),
                PlateRegistrationSequence(plate), work, Frame);
            if (!ValidateImminentHead(wait, out var evidence, out lastReason)) continue;
            imminentHead = wait; var started = ImminentHeadStatus(wait); started["evidence"] = evidence;
            Log("imminentHeadWaitStarted", started); return true;
        }
        if (TryBeginFinalSauceHead(out lastReason)) return true;
        Log("imminentHeadWaitRejected", new JsonObject { ["frame"] = Frame, ["orderIndex"] = delivered, ["reason"] = lastReason });
        return false;
    }
}
