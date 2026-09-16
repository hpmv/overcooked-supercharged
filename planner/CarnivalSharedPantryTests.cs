using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Uses V7 GF684 native placement geometry; synthetic controller ownership does not claim a native delegation run.</summary>
    public static int SharedPantrySelfTest(JsonObject placedSnapshot)
    {
        int checks = 0;
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException("Shared pantry regression: " + message); checks++; }
        void Reject(Action action, string message)
        {
            bool rejected = false; try { action(); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected, message);
        }
        CarnivalPlanner Make(bool enabled = true)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline shared pantry test attempted I/O."), null)
                { response = placedSnapshot.DeepClone().AsObject(), options = new(PantryChopping: true, SharedPantryChopping: enabled) };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.Refresh(); p.runner = new RouteRunner(_ => throw new InvalidOperationException("Offline shared pantry test attempted I/O."), null);
            if (!p.Supply(2, CarnivalRecipes.Bun, 23, false)) throw new InvalidOperationException("Recorded bun supply could not be reconstructed.");
            var work = p.workers[2]!; work.Actions.Dequeue(); var place = work.Actions.Dequeue();
            work.CompletedBoundaryAction = place; work.CompletedBoundaryTarget = 23; work.CompletedBoundaryHeld = p.Attached(23);
            work.NeutralBoundaryFrame = p.Frame;
            return p;
        }
        void MakePrepared(CarnivalPlanner p, SharedPantryChop lease, NativeIngredient? ingredient = null)
        {
            ingredient ??= lease.Supply.Ingredient;
            int preparedId = p.entities.Keys.Max() + 1;
            p.entities.Remove(lease.Raw);
            p.entities[preparedId] = new JsonObject { ["id"] = preparedId, ["active"] = true, ["observedOrdinal"] = 9000,
                ["name"] = ingredient.Name + "_prepared", ["components"] = new JsonArray("CarryableItem"),
                ["composition"] = new JsonObject { ["type"] = "IngredientAssembledNode", ["id"] = ingredient.Id } };
            p.Entity(lease.Supply.Board)!["attachedEntityId"] = preparedId;
        }
        var off = Make(false); string offQueue = System.Text.Json.JsonSerializer.Serialize(off.workers[2]!.Actions);
        Check(!off.TrySharePantryChopping() && off.sharedPantryChops.Count == 0 && off.workers[2]!.PantrySupply is null &&
            System.Text.Json.JsonSerializer.Serialize(off.workers[2]!.Actions) == offQueue && off.reserved.SetEquals([68, 23]),
            "default-off preserves original supplier queue, callback, and all reservations");
        bool invalid = false; try { ValidateSharedPantryOption(new(SharedPantryChopping: true)); } catch (ArgumentException) { invalid = true; }
        Check(invalid, "sharing cannot silently enable or replace pantry chopping");
        var p = Make(); var supplierWork = p.workers[2]!; var originalCallback = supplierWork.Complete;
        Check(p.Frame == 684 && p.Attached(23) == 147 && p.RawPantryItem(147, CarnivalRecipes.Bun), "fixture is the exact native raw-bun placement with zero work progress");
        Check(p.TrySharePantryChopping(), "idle central helper may take the raw board only after the completed placement boundary");
        var lease = p.sharedPantryChops.Values.Single(); var helperWork = p.workers[lease.Helper]!;
        Check(lease.Helper == 0 && p.workers[2] is null && p.playerCompletedAt[2] == 684, "nearby idle center chef0 replaces pantry chef2 only for future chopping");
        Check(p.reserved.SetEquals([23, 147]) && supplierWork.OwnedResources.Count == 0 && helperWork.OwnedResources.SetEquals([23, 147]),
            "board lease transfers intact, exact raw item is reserved, completed source crate is released");
        Check(supplierWork.Actions.Count == 0 && supplierWork.Complete == originalCallback && helperWork.Complete != originalCallback &&
            helperWork.Active is null && helperWork.Actions.Single()["type"]?.ToString() == "chop",
            "no active action is reset and original prepared callback is not invoked at raw placement");
        Check(!p.Start(3, "duplicate-board-job", [], [23]), "another cook cannot take the shared board lease");
        Check(p.Start(2, "next-supplier-stock", [], [68]), "supplier is free for a later ordinary stock job without retaining its crate lock");
        p.Entity(147)!["workProgress"] = .2857143; p.chefs[0]["serverInteractionId"] = 23;
        p.ObserveSharedPantryChopping();
        Check(lease.NativeWorkObserved && lease.WorkingSamples == 1 && lease.MaximumProgress > .28, "actual server interaction and native work progress establish helper participation");
        MakePrepared(p, lease);
        var completed = p.runner.CreateAction(new JsonObject { ["type"] = "chop", ["player"] = 0, ["station"] = "23" }); completed.IsDone = true;
        helperWork.Actions.Clear(); helperWork.Active = completed;
        p.ObserveSharedPantryChopping(); p.CompleteWork();
        Check(p.sharedPantryChops.Count == 0 && p.workers[0] is null && p.reserved.SetEquals([68]) && p.workers[2]!.Name == "next-supplier-stock",
            "native prepared replacement releases only helper board/raw leases and preserves later supplier work");
        var premature = Make(); premature.workers[2]!.Active = premature.runner.CreateAction(new JsonObject { ["type"] = "place", ["player"] = 2, ["station"] = "23" });
        Check(!premature.TrySharePantryChopping(), "incomplete native placement cannot be preempted");
        var oldBoundary = Make(); oldBoundary.workers[2]!.NeutralBoundaryFrame--;
        Check(!oldBoundary.TrySharePantryChopping(), "a stale completed boundary cannot transfer ownership");
        var pendingEdge = Make(); ((JsonObject)pendingEdge.response["inputs"]![2]!)!["pickup"] = true;
        Check(!pendingEdge.TrySharePantryChopping(), "supplier button release must already be observed in the native response");
        var helperEdge = Make(); ((JsonObject)helperEdge.response["inputs"]![0]!)!["use"] = true; helperEdge.workers[3] = new Work("busy", [], [], null);
        Check(!helperEdge.TrySharePantryChopping(), "helper with a held input cannot receive a new chopping edge");
        var busy = Make(); busy.workers[0] = new Work("normal-priority-job", [], [], null); busy.workers[3] = new Work("normal-priority-job", [], [], null);
        Check(!busy.TrySharePantryChopping(), "normal chef priorities retain precedence over optional delegation");
        var remote = Make(); remote.chefs[0]["position"]!["x"] = 23.124; remote.chefs[3]["position"]!["z"] = -20;
        Check(!remote.TrySharePantryChopping(), "helpers requiring over one second of collision-safe native walking remain assigned normally");
        var sourceWrong = Make(); sourceWrong.workers[2]!.PantrySupply = sourceWrong.workers[2]!.PantrySupply! with { Source = 69 };
        Check(!sourceWrong.TrySharePantryChopping(), "source crate and native ingredient family must agree");
        var wrongRaw = Make(); wrongRaw.Entity(147)!["name"] = "DLC08_Onion";
        Check(!wrongRaw.TrySharePantryChopping(), "different raw food cannot satisfy the completed bun placement");
        var progressed = Make(); progressed.Entity(147)!["workProgress"] = .1;
        Check(!progressed.TrySharePantryChopping(), "already-started chopping is never moved between chefs");
        var substituted = Make(); substituted.workers[2]!.CompletedBoundaryHeld = 999;
        Check(!substituted.TrySharePantryChopping(), "board item must be exactly the entity placed by the completed action");
        var identity = Make(); identity.Entity(23)!["observedOrdinal"] = 999;
        Check(!identity.TrySharePantryChopping(), "reused board identity is not silently accepted");
        var extraWork = Make(); extraWork.workers[2]!.Actions.Enqueue(new JsonObject { ["type"] = "take", ["station"] = "23" });
        Check(!extraWork.TrySharePantryChopping(), "only a sole remaining chop can transfer from the supplier");
        var extraLease = Make(); extraLease.workers[2]!.OwnedResources.Add(99); extraLease.reserved.Add(99);
        Check(!extraLease.TrySharePantryChopping(), "a job with additional ownership cannot be partially released");
        var preService = Make(); preService.preServiceStock = new PreServiceStockLease(preService.workers[2]!, 10, 45, 9, 2, 1, 1, 3, 0, 23, 684, 240);
        Check(!preService.TrySharePantryChopping(), "ready-head pre-service callback and timer ownership remain intact");
        var noProof = Make(); noProof.TrySharePantryChopping(); var noProofLease = noProof.sharedPantryChops.Values.Single(); MakePrepared(noProof, noProofLease);
        Reject(() => noProof.CompleteSharedPantryChop(noProofLease), "prepared shape alone does not establish native helper chopping");
        var wrongPrepared = Make(); wrongPrepared.TrySharePantryChopping(); var badLease = wrongPrepared.sharedPantryChops.Values.Single(); badLease.NativeWorkObserved = true;
        MakePrepared(wrongPrepared, badLease, CarnivalRecipes.Onion);
        Reject(() => wrongPrepared.CompleteSharedPantryChop(badLease), "wrong native prepared family fails completion");
        var disappeared = Make(); disappeared.TrySharePantryChopping(); disappeared.Entity(23)!["attachedEntityId"] = 0;
        Reject(disappeared.ObserveSharedPantryChopping, "raw item removed from the board before replacement fails safely");
        var deadline = Make(); deadline.TrySharePantryChopping(); deadline.state["gameplayFrame"] = deadline.Frame + 301;
        invalid = false; try { deadline.ObserveSharedPantryChopping(); } catch (TimeoutException) { invalid = true; }
        Check(invalid, "shared chopping preserves elapsed native time and has a finite failure bound");
        return checks;
    }
}
