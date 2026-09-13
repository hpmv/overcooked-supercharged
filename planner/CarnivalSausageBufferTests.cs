using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int SausageBufferSelfTest(JsonObject idle7454, JsonObject ready1108)
    {
        int count = 0;
        void Check(bool condition, string message)
        { if (!condition) throw new InvalidOperationException("Sausage buffer regression: " + message); count++; }
        void Reject(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (InvalidOperationException) { rejected = true; } catch (TimeoutException) { rejected = true; }
            Check(rejected, message);
        }
        CarnivalPlanner Make(int size = 2, bool ready = false)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline sausage fixture attempted native I/O."), null)
            {
                response = (ready ? ready1108 : idle7454).DeepClone().AsObject(),
                options = new(SausageBufferSize: size),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(158500), 96).ToArray()
            };
            if (p.response["state"] is null) p.response = new() { ["state"] = p.response };
            p.Refresh(); p.runner = new(_ => throw new InvalidOperationException("Offline sausage route attempted I/O."), null);
            p.CaptureSausagePotHomes();
            if (ready)
            {
                p.mealPlates[0] = 10;
                // This branch is an explicit hypothetical separately staged
                // head with both pots full. Original fixtures remain unchanged.
                p.Entity(45)!["attachedEntityId"] = 0; p.Entity(51)!["attachedEntityId"] = 10;
                p.Entity(10)!["position"] = p.Entity(51)!["position"]!.DeepClone();
                p.Entity(2)!["composition"] = p.Entity(7)!["composition"]!.DeepClone();
                p.Entity(2)!["cookingProgress"] = p.Entity(7)!["cookingProgress"]!.DeepClone();
            }
            return p;
        }
        void Elapse(CarnivalPlanner p, int frames)
        {
            p.state["gameplayFrame"] = p.Frame + frames; p.state["timer"] = N(p.state["timer"]) - frames / 60d;
            foreach (var order in (p.state["orders"] as JsonArray)!.OfType<JsonObject>()) order["remaining"] = N(order["remaining"]) - frames / 60d;
        }
        void EmptyPot(CarnivalPlanner p, int pot)
        {
            p.Entity(pot)!["composition"] = new JsonObject { ["type"] = "CookedCompositeAssembledNode", ["state"] = "Raw",
                ["cookingStepId"] = CarnivalRecipes.PotCookingStepId, ["progress"] = 0, ["children"] = new JsonArray() };
            p.Entity(pot)!["cookingProgress"] = 0;
        }
        void Finish(CarnivalPlanner p, int player)
        {
            var work = p.workers[player]!; p.ReleaseRemainingWorkResources(work); p.workers[player] = null; work.Complete?.Invoke();
        }
        int Pickup(CarnivalPlanner p, SausageLease lease, int food = 10001)
        {
            p.entities[food] = new JsonObject { ["id"] = food, ["active"] = true, ["observedOrdinal"] = food + 100,
                ["name"] = "Frankfurter", ["position"] = p.chefs[2]["position"]!.DeepClone(),
                ["components"] = new JsonArray("CarryableItem", "CookableProperties"), ["composition"] = new JsonObject {
                    ["type"] = "IngredientAssembledNode", ["id"] = CarnivalRecipes.Frankfurter.Id, ["children"] = new JsonArray() } };
            p.chefs[2]["heldEntityId"] = food;
            lease.Work!.CompletedBoundaryAction = p.A("take", p.model.Stations.Single(s => s.Ingredient == CarnivalRecipes.Frankfurter.Name).EntityId);
            p.AdvanceSausageBuffers(); return food;
        }
        SausageLease Handoff(CarnivalPlanner p, int food = 10001, bool before = false)
        {
            Check(p.TryAdmitSausageBuffer(before), "fixture admits one bounded native-action raw stock job");
            var lease = p.sausageBuffers.Last(); Pickup(p, lease, food);
            p.chefs[2]["heldEntityId"] = 0; p.Entity(lease.Pass)!["attachedEntityId"] = food; Elapse(p, 55); Finish(p, 2); return lease;
        }
        void Park(CarnivalPlanner p, SausageLease lease)
        {
            Check(p.TryParkBufferedSausage(0), "an idle center cook can start exact take-pass/place-storage");
            p.Entity(lease.Pass)!["attachedEntityId"] = 0; p.chefs[0]["heldEntityId"] = lease.Food;
            p.AdvanceSausageBuffers();
            p.chefs[0]["heldEntityId"] = 0; p.Entity(lease.Counter)!["attachedEntityId"] = lease.Food;
            Elapse(p, 80); Finish(p, 0);
        }
        void Loaded(CarnivalPlanner p, SausageLease lease)
        {
            int source = p.Attached(lease.Pass) == lease.Food ? lease.Pass : lease.Counter;
            p.Entity(source)!["attachedEntityId"] = 0; p.chefs[lease.Owner]["heldEntityId"] = lease.Food;
            p.AdvanceSausageBuffers();
            p.chefs[lease.Owner]["heldEntityId"] = 0; p.Entity(lease.Pot)!["composition"] = new JsonObject {
                ["type"] = "CookedCompositeAssembledNode", ["state"] = "Raw", ["progress"] = .01,
                ["cookingStepId"] = CarnivalRecipes.PotCookingStepId,
                ["children"] = new JsonArray(p.Entity(lease.Food)!["composition"]!.DeepClone()) };
            p.Entity(lease.Food)!["active"] = false; p.Entity(lease.Pot)!["cookingProgress"] = .12;
            Elapse(p, 100); Finish(p, lease.Owner);
        }

        var disabled = Make(0);
        Check(!disabled.TryAdmitSausageBuffer(false) && disabled.reserved.Count == 0, "default-off emits no work or reservations");
        foreach (int invalid in new[] { -1, 3 })
        {
            bool rejected = false;
            try { Make(0).RunAsync(new(SausageBufferSize: invalid)).GetAwaiter().GetResult(); }
            catch (ArgumentException) { rejected = true; }
            Check(rejected, "out-of-range buffer size is rejected before any native I/O");
        }
        var actual = Make();
        Check(actual.Frame == 7454 && actual.Held(2) == 0 && actual.BothNativePotsOccupied() && actual.EmptyAttachment(45),
            "actual V9 GF7454 has idle pantry, two occupied native pots and free pass");
        Check(actual.TryAdmitSausageBuffer(false), "actual recorded stock opportunity admits without moving any existing food");
        var first = actual.sausageBuffers.Single();
        int firstCounter = first.Counter;
        Check(new[] { 33, 40, 43 }.Contains(firstCounter) && firstCounter != actual.Counter(20.4, -16.8) && first.Pot == 0 && first.Home == 0,
            "an actual empty ordinary center counter is selected; protected workspace and future pot are untouched");
        Check(first.Owned.SetEquals([firstCounter, 45]) && actual.workers[2]!.OwnedResources.SetEquals([70]) && actual.Free(2, 7),
            "long storage/pass ownership is independent of transient crate job and full pots remain unreserved");
        Check(actual.workers[2]!.Actions.Select(a => a["type"]!.ToString()).SequenceEqual(new[] { "take", "place" }) &&
            actual.workers[2]!.Actions.All(a => I(a["timeoutFrames"]) == 120) && actual.counterSupplies.Count == 0,
            "buffer uses bounded ordinary crate/counter inputs without full-pot address or throw");
        Check(!actual.TryAdmitSausageBuffer(false), "second supplier cannot claim occupied worker/pass");
        int raw = Pickup(actual, first);
        Check(first.Food == raw && first.FoodOrdinal == raw + 100 && actual.reserved.Contains(raw), "completed native pickup records exact raw identity and ordinal");
        actual.chefs[2]["heldEntityId"] = 0; actual.Entity(45)!["attachedEntityId"] = raw; Elapse(actual, 55); Finish(actual, 2);
        Check(first.Phase == SausagePhase.Handoff && first.Owner == -1 && actual.workers[2] is null && actual.Free(70), "handoff releases pantry worker/crate while preserving stock");
        Check(!actual.TryLoadBufferedSausage(0) && first.Pot == 0, "full pots are a legal waiting condition with no premature target");
        Elapse(actual, 600); actual.AdvanceSausageBuffers();
        Check(first.Phase == SausagePhase.Handoff && actual.reserved.Contains(45), "unstarted raw handoff waits without transfer timeout");
        Park(actual, first);
        Check(first.Phase == SausagePhase.Parked && actual.Free(45) && first.Owned.SetEquals([firstCounter, raw]), "parking releases pass only after native storage attachment proof");
        Check(actual.TryAdmitSausageBuffer(false), "second stock may use the freed pass with independent storage");
        var second = actual.sausageBuffers.Last();
        Check(second.Counter != first.Counter && second.Food == 0 && actual.sausageBuffers.Count == 2, "second lease uses a distinct ordinary counter");
        Pickup(actual, second, 10002); actual.chefs[2]["heldEntityId"] = 0; actual.Entity(45)!["attachedEntityId"] = 10002; Elapse(actual, 55); Finish(actual, 2);
        Park(actual, second);
        Check(!actual.TryAdmitSausageBuffer(false), "size2 bounds total admitted raw stock");
        Elapse(actual, 600); actual.AdvanceSausageBuffers();
        Check(actual.sausageBuffers.Count == 2 && actual.workers[0] is null, "parked full-pot stock retains no chef and has no idle timeout");
        EmptyPot(actual, 2);
        Check(actual.TryLoadBufferedSausage(0) && first.Pot == 2 && first.Home == actual.sausagePotHomes[2].Home,
            "fresh native empty pot selects its original stove at refill time");
        Check(actual.workers[0]!.OwnedResources.SetEquals([2, first.Home]) && first.Owned.SetEquals([firstCounter, raw]),
            "refill Work owns only current empty pot/stove; stock ownership stays with its exact lease");
        actual.Start(2, "new-pass-owner", [actual.A("take", 45)], [45]);
        Loaded(actual, first);
        Check(actual.sausageBuffers.Count == 1 && actual.sausageBuffers[0] == second && second.Owned.All(actual.reserved.Contains),
            "consuming one stock leaves the other exact buffer and resources intact");
        Check(actual.reserved.Contains(45) && actual.workers[2]!.OwnedResources.Contains(45) && actual.Free(firstCounter, raw, 2, first.Home),
            "old stock completion cannot release a newer pass owner's lease");

        var cap = Make(1); var only = Handoff(cap); Park(cap, only);
        Check(!cap.TryAdmitSausageBuffer(false), "size1 stops a second admitted stock");
        var noDemand = Make(); noDemand.recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(228996), 96).ToArray();
        Check(!noDemand.TryAdmitSausageBuffer(false), "no pending hotdog demand cannot occupy stock counters");
        var empty = Make(); EmptyPot(empty, 2);
        Check(!empty.TryAdmitSausageBuffer(false), "normal empty-pot supply retains priority over raw reserve");
        var protectedCounters = Make();
        foreach (var counter in protectedCounters.Stations("counter").Where(s => s.Regions.Contains("center") && s.EntityId != 42)) protectedCounters.reserved.Add(counter.EntityId);
        Check(!protectedCounters.TryAdmitSausageBuffer(false), "early/bakery/meal counter reservations cannot be borrowed, even with workspace42 empty");
        var addressed = Make(); addressed.counterSupplies[45] = (2, CarnivalRecipes.Frankfurter.Id);
        Check(!addressed.TryAdmitSausageBuffer(false), "outstanding addressed pass is excluded even before its item arrives");
        var changed = Make(); var changedLease = Handoff(changed);
        changed.Entity(changedLease.Food)!["observedOrdinal"] = 99999;
        Reject(changed.AdvanceSausageBuffers, "recycled raw network ID is rejected using observed ordinal");
        var moved = Make(); var movedLease = Handoff(moved); moved.Entity(45)!["attachedEntityId"] = 0;
        Reject(moved.AdvanceSausageBuffers, "missing handoff attachment does not masquerade as delivered stock");
        var replacedCounter = Make(); var replacedLease = Handoff(replacedCounter);
        replacedCounter.Entity(replacedLease.Counter)!["observedOrdinal"] = 98765;
        Reject(replacedCounter.AdvanceSausageBuffers, "same counter network ID with a new ordinal cannot retain the old lease");
        var lostLock = Make(); var lostLease = Handoff(lostLock); lostLock.reserved.Remove(lostLease.Food);
        Reject(lostLock.AdvanceSausageBuffers, "exact food cannot be used after its ownership is lost");
        var late = Make(); Check(late.TryAdmitSausageBuffer(false), "timeout fixture admits"); Elapse(late, 121);
        Reject(late.AdvanceSausageBuffers, "active supply has a finite whole-job budget");
        var latePark = Make(); var lateParkLease = Handoff(latePark);
        Check(latePark.TryParkBufferedSausage(0), "parking timeout fixture starts legal native actions"); Elapse(latePark, lateParkLease.Transfer!.Budget + 1);
        Reject(latePark.AdvanceSausageBuffers, "active parking is bounded even though stationary stock may wait indefinitely");
        var atPass = Make(); var atPassLease = Handoff(atPass); EmptyPot(atPass, 7);
        Check(atPass.TryLoadBufferedSausage(0) && atPassLease.Pot == 7 && StationIs(atPass.workers[0]!.Actions.First(), 45),
            "newly empty pot permits direct pass-to-pot without a needless parking trip");
        atPass.Entity(atPassLease.Pot)!["composition"] = atPass.Entity(2)!["composition"]!.DeepClone(); atPass.Entity(45)!["attachedEntityId"] = 0;
        Reject(() => Finish(atPass, 0), "matching contents alone cannot prove transfer while original raw ingredient is still active");
        var stolenPot = Make(); var stolenLease = Handoff(stolenPot); Park(stolenPot, stolenLease); EmptyPot(stolenPot, 2);
        stolenPot.reserved.Add(stolenPot.sausagePotHomes[2].Home);
        Check(!stolenPot.TryLoadBufferedSausage(0), "reserved native stove cannot be loaded through its otherwise free pot");
        stolenPot.reserved.Clear(); foreach (int resource in stolenLease.Owned) stolenPot.reserved.Add(resource);
        stolenPot.Entity(2)!["cookingProgress"] = .1;
        Check(!stolenPot.TryLoadBufferedSausage(0), "empty food without native progress reset is not an eligible pot");
        stolenPot.Entity(2)!["cookingProgress"] = 0; stolenPot.Entity(2)!["observedOrdinal"] = 999;
        Check(!stolenPot.TryLoadBufferedSausage(0), "replacement pot is not the initialized native vessel");

        var ready = Make(ready: true); ready.AdvanceSausageBuffers();
        Check(ready.preServiceReadyFrame == ready.Frame, "buffer-only option observes a ready head without enabling ordinary pre-service stocking");
        var readyLease = Handoff(ready, before: true);
        Check(ready.preServiceUsed && readyLease.HeadGuard is null && ready.Free(10, 51) && ready.workers[2] is null,
            "one bounded native handoff releases FIFO meal and consumes shared per-visit allowance");
        Check(!ready.TryAdmitSausageBuffer(true), "pre-service raw buffering cannot queue a second job before departure");
        var tail = Make(ready: true); tail.AdvanceSausageBuffers(); Elapse(tail, 121);
        Check(!tail.TryAdmitSausageBuffer(true), "older ready-head wait leaves insufficient two-second supply allowance");
        var shared = Make(ready: true); shared.preServiceUsed = true;
        Check(!shared.TryAdmitSausageBuffer(true), "ordinary pre-service supply and raw buffering share one per-visit allowance");
        var tip = Make(ready: true); var order = tip.PreServiceNativeOrder()!; order["remaining"] = .66 * N(order["lifetime"]) + 1;
        Check(!tip.TryAdmitSausageBuffer(true), "raw reserve cannot postpone a head across its current native tip band");
        var headPass = Make(ready: true); headPass.Entity(51)!["attachedEntityId"] = 0; headPass.Entity(45)!["attachedEntityId"] = 10;
        Check(!headPass.TryAdmitSausageBuffer(true), "existing meal at pass45 makes buffer admission decline without moving it");
        var changedHead = Make(ready: true); Check(changedHead.TryAdmitSausageBuffer(true), "ready identity guard fixture admits");
        changedHead.Entity(10)!["observedOrdinal"] = 123456;
        Reject(changedHead.AdvanceSausageBuffers, "pre-service raw supply cannot continue after reserved FIFO identity changes");
        var usedHead = Make(ready: true); Check(usedHead.TryAdmitSausageBuffer(true), "native order guard fixture admits");
        usedHead.PreServiceNativeOrder()!["id"] = 123456;
        Reject(usedHead.AdvanceSausageBuffers, "changed native oldest order invalidates the bounded departure guard");
        return count;
    }
}
