using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int BakeryDepartureSelfTest(JsonObject at11688, JsonObject at12116, JsonObject at12167, JsonObject at12398, JsonObject at11220)
    {
        int checks = 0;
        void Check(bool test, string message) { if (!test) throw new InvalidOperationException("Bakery departure regression: " + message); checks++; }
        CarnivalPlanner Make(JsonObject snapshot)
        {
            var p = new CarnivalPlanner(_ => throw new InvalidOperationException("Offline bakery departure test attempted native I/O."), null)
            { response = snapshot.DeepClone().AsObject(), options = new(DirectVesselThrows:true, PantryChopping:true),
                recipes = Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560),40).ToArray() };
            if (p.response["state"] is null) p.response = new JsonObject { ["state"] = p.response };
            p.recipes[20]=CarnivalRecipes.GetRecipe(130976);p.recipes[24]=CarnivalRecipes.GetRecipe(228996);
            p.Refresh();p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline route emitted native I/O."),null);
            foreach(var b in p.Stations("bowl"))p.bowlHomes[b.EntityId]=p.AttachmentParent(b.EntityId);
            p.bowlAssignments[3]=20;p.bowlFlavors[3]=CarnivalRecipes.Raspberry.Id;p.bowlAssignments[6]=24;p.bowlFlavors[6]=CarnivalRecipes.Chocolate.Id;
            p.CaptureSupplyTopology(false);
            var observed=KitchenModel.SnapshotState(at11220);var arrived=observed["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>I(c["playerId"])==1);
            p.bakeryPortalArrival=new(KitchenModel.Position(arrived["position"]),91,I(observed["entities"]!.AsArray().OfType<JsonObject>().Single(e=>I(e["id"])==91)["observedOrdinal"]),I(observed["gameplayFrame"]));
            return p;
        }
        JsonObject Mix(params int[] ids)=>new(){["type"]="MixedCompositeAssembledNode",["state"]="Unmixed",["progress"]=.3,
            ["children"]=new JsonArray(ids.Select(id=>(JsonNode)new JsonObject{["type"]="IngredientAssembledNode",["id"]=id,["children"]=new JsonArray()}).ToArray())};
        var actual=Make(at11688);
        Check(actual.Region(1)=="upper-right"&&actual.Held(1)==0&&N(actual.Entity(3)?["mixingProgress"]) is >4.5 and <4.7&&actual.Food(3).IngredientIds.SequenceEqual(new[]{18448}),"native young far Flour-only batch establishes missed departure guard");
        Check(!actual.TrySupplyUrgentMixerPrerequisite(),"existing nine-second urgency deliberately does not cover this earlier boundary");
        Check(actual.TryFinishActiveBakeryBeforeWash()&&actual.workers[1]?.Name=="supply-Egg","departure gate starts exact missing Egg through ordinary native actions");
        Check(actual.counterSupplies[48]==(3,16620)&&actual.workers[1]!.OwnedResources.Contains(3)&&actual.workers[1]!.Actions.Last()["station"]?.ToString()=="48","far Egg retains exact bowl/address/ordinary shared handoff");
        Check(actual.Entity(3)!["composition"]!.ToJsonString()==(at11688["state"]??at11688)["entities"]!.AsArray().OfType<JsonObject>().Single(e=>I(e["id"])==3)["composition"]!.ToJsonString(),"admission never changes native food or progress");
        var integrated=Make(at11688);integrated.BakeryAndWash("upper-right");
        Check(integrated.workers[1]?.Name=="supply-Egg","actual normal washing dispatch now finishes the young active kit");
        var pending=Make(at11688);pending.Entity(3)!["composition"]=Mix();pending.Entity(3)!["mixingProgress"]=0;pending.counterSupplies[48]=(3,18448);
        Check(pending.ActiveBakeryCommitments().SequenceEqual(new[]{3})&&pending.TryFinishActiveBakeryBeforeWash()&&pending.workers[1] is null,"already-issued raw address blocks departure before it enters an empty bowl");
        Check(pending.counterSupplies[48]==(3,18448),"pending exact address is preserved without duplicate supply");
        var waiting=Make(at11688);waiting.counterSupplies[48]=(3,16620);
        Check(waiting.TryFinishActiveBakeryBeforeWash()&&waiting.workers[1] is null,"outstanding same-bowl Egg waits for normal acceptance");
        var occupied=Make(at11688);occupied.Start(1,"existing-supply",[occupied.A("take",24)],[24]);var existing=occupied.workers[1];
        Check(!occupied.TryFinishActiveBakeryBeforeWash()&&ReferenceEquals(existing,occupied.workers[1]),"active supplier action and reservations are never preempted");
        var reservedBowl=Make(at11688);reservedBowl.reserved.Add(3);
        Check(reservedBowl.TryFinishActiveBakeryBeforeWash()&&reservedBowl.workers[1] is null&&reservedBowl.reserved.Contains(3),"central vessel ownership keeps supplier waiting without theft");
        var detached=Make(at11688);detached.Entity(14)!["attachedEntityId"]=0;
        Check(!detached.TryFinishActiveBakeryBeforeWash(),"parked off-mixer batches do not retain supplier at a nonexistent processing clock");
        var empty=Make(at11688);empty.Entity(3)!["composition"]=Mix();empty.Entity(3)!["mixingProgress"]=0;
        Check(!empty.TryFinishActiveBakeryBeforeWash(),"empty future assignment without issued ingredients preserves original departure behavior");
        var complete=Make(at11688);complete.Entity(3)!["composition"]=Mix(18448,16620,129618);
        Check(!complete.TryFinishActiveBakeryBeforeWash(),"complete recipe ingredients need central processing, not supplier retention");
        var flavor=Make(at11688);flavor.Entity(3)!["composition"]=Mix(18448,16620);
        Check(flavor.TryFinishActiveBakeryBeforeWash()&&flavor.workers[1]?.Name=="supply-Raspberry","missing exact flavor is prepared before washing after raw kit acceptance");
        var otherAddress=Make(at11688);otherAddress.counterSupplies[48]=(6,16620);
        Check(otherAddress.TryFinishActiveBakeryBeforeWash()&&otherAddress.counterSupplies[48]==(6,16620),"another bowl's explicit raw handoff is never overwritten");
        var returned=Make(at12116);var evidence=returned.ActiveMixerReturnEvidence();
        Check(evidence is not null&&I(evidence["bowl"])==3&&I(evidence["ingredient"])==16620,"captured first wash completion has feasible exact missing-Egg supplier escape: "+returned.lastMixerReturnEvaluation?.ToJsonString());
        Check(N(evidence!["estimatedPortalSupplyAndRelaySeconds"])<N(evidence["remainingGuardSeconds"]),"full native walking/portal/edges and center relay budget fits before unchanged21s guard");
        int clean=returned.Attached(returned.Counter(15.6,-20.4));int dirty=returned.Attached(returned.Counter(15.6,-19.2));
        returned.BakeryAndWash("lower-left");
        Check(returned.workers[1]?.Name=="active-mixer-return-to-bakery"&&returned.workers[1]!.Actions.Single()["type"]?.ToString()=="portal","budgeted return precedes another native wash/stack pickup");
        Check(returned.Attached(returned.Counter(15.6,-20.4))==clean&&returned.Attached(returned.Counter(15.6,-19.2))==dirty,"native stored clean and dirty plates remain unchanged");
        var late=Make(at12398);
        Check(late.ActiveMixerReturnEvidence() is null&&!late.TryReturnForActiveMixer(),"late closed-cycle state is rejected when complete return/supply/relay cannot fit");
        var afterCollection=Make(at12167);
        Check(afterCollection.ActiveMixerReturnEvidence() is null,"later clean-plate collection boundary rejects its insufficient full walking budget");
        var busy=Make(at12116);foreach(int p in new[]{0,3})busy.Start(p,"busy-center",[busy.A("take",32)],[]);
        Check(busy.ActiveMixerReturnEvidence() is null,"return budget cannot invent an immediately available receiving chef");
        var held=Make(at12116);held.chefs[1]["heldEntityId"]=408;
        Check(held.ActiveMixerReturnEvidence() is null,"existing held plate must be handled before a supplier excursion");
        var endpoint=Make(at12116);endpoint.Entity(94)!["portalDestinationId"]=0;endpoint.Refresh();
        Check(endpoint.ActiveMixerReturnEvidence() is null,"unknown native portal destination cannot receive a budgeted return");
        var unseen=Make(at12116);unseen.bakeryPortalArrival=null;
        Check(unseen.ActiveMixerReturnEvidence() is null,"animation teleport point cannot substitute for a measured controlled arrival");
        var measured=Make(at11220);measured.bakeryPortalArrival=null;measured.ObserveBakeryPortalArrival(1,measured.Portal("upper-right"));
        Check(measured.bakeryPortalArrival is { Receiver:91,Frame:11220 } arrival&&arrival.Position.Distance(new(28.8,-11.2999992))<.001,"completed native portal action records actual controlled landing instead of animation start");
        var remembered=measured.bakeryPortalArrival;measured.ObserveBakeryPortalArrival(1,measured.A("take",24));
        Check(ReferenceEquals(remembered,measured.bakeryPortalArrival),"unrelated completed actions cannot forge arrival observations");
        var reused=Make(at12116);reused.Entity(91)!["observedOrdinal"]=999;
        Check(reused.ActiveMixerReturnEvidence() is null,"receiver identity reuse invalidates an old measured arrival");
        return checks;
    }
}
