using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Offline scheduling/lease fixtures; never issues a game request or changes a native object.</summary>
    public static int BakeryLookaheadSelfTest(JsonObject initialSnapshot,JsonObject nativeOffmixProof)
    {
        int checks=0;
        void Check(bool value,string reason){if(!value)throw new InvalidOperationException("Bakery lookahead regression: "+reason);checks++;}
        void Reject(Action test,string reason){bool rejected=false;try{test();}catch(InvalidOperationException){rejected=true;}catch(TimeoutException){rejected=true;}catch(ArgumentException){rejected=true;}Check(rejected,reason);}
        CarnivalPlanner Make(int ahead=8,int normal=4)
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline bakery fixture attempted I/O"),null)
            {response=initialSnapshot.DeepClone().AsObject(),options=new(Lookahead:normal,BakeryLookahead:ahead)};
            if(p.response["state"] is null)p.response=new JsonObject{["state"]=p.response};
            p.recipes=new[]{296560,296560,296560,296560,228996,130976,130976,296560,228996,130976}.Select(CarnivalRecipes.GetRecipe).ToArray();
            p.Refresh();p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline bakery action attempted I/O"),null);
            foreach(var bowl in p.Stations("bowl"))p.bowlHomes[bowl.EntityId]=p.Stations("mix-station").OrderBy(s=>s.Position.Distance(bowl.Position)).First().EntityId;
            return p;
        }
        void Advance(CarnivalPlanner p){p.state["gameplayFrame"]=p.Frame+1;p.state["timer"]=N(p.state["timer"])-1.0/60;p.Refresh();p.AdvanceBakeryLookahead();}
        void Finish(CarnivalPlanner p,int player)
        {
            var work=p.workers[player]??throw new InvalidOperationException("Missing bakery fixture work");
            foreach(int id in work.OwnedResources)p.reserved.Remove(id);p.workers[player]=null;work.Complete?.Invoke();
        }
        void Dough(CarnivalPlanner p,int bowl,bool mixed,double progress)
        {
            var food=nativeOffmixProof["offmix"]!["nativeComposition"]!.DeepClone();
            void Modify(JsonNode? node)
            {
                if(node is not JsonObject obj)return;
                if(obj["type"]?.ToString()=="MixedCompositeAssembledNode"){obj["state"]=mixed?"Mixed":"Unmixed";obj["progress"]=progress/12;}
                foreach(var child in obj["children"]?.AsArray()??[])Modify(child);
            }
            Modify(food);p.Entity(bowl)!["composition"]=food;p.Entity(bowl)!["mixingProgress"]=progress;
        }
        void Park(CarnivalPlanner p,BakeryLease lease)
        {
            p.Entity(lease.Home)!["attachedEntityId"]=0;p.Entity(lease.Counter)!["attachedEntityId"]=lease.Bowl;
            p.Entity(lease.Bowl)!["position"]=p.Entity(lease.Counter)!["position"]!.DeepClone();
            p.chefs[lease.Owner]["heldEntityId"]=0;Dough(p,lease.Bowl,true,12.1374874);p.Refresh();
            Finish(p,lease.Owner);
            Check(p.IsBakeryParticipant(lease.Owner)&&!p.EarlyCookAvailable(lease.Owner)&&!p.BakeryCookAvailable(lease.Owner),"offmix verification owner cannot be reassigned to another rescue");
            Advance(p);Check(lease.Phase==BakeryPhase.Verifying&&lease.StableSamples==1,"one advancing sample cannot complete offmix proof");
            Advance(p);
        }
        Check(new CarnivalPlannerOptions().BakeryLookahead==0,"feature is disabled by default");
        foreach(int value in new[]{0,2,32}){ValidateBakeryOption(new(BakeryLookahead:value));Check(true,"accepted bounded option");}
        foreach(int value in new[]{-1,1,33})Reject(()=>ValidateBakeryOption(new(BakeryLookahead:value)),"invalid option rejected");
        var baseline=Make(0);Check(baseline.BakeryPending().Select(p=>p.Index).SequenceEqual(baseline.Pending().Select(p=>p.Index)),"zero option uses exactly ordinary candidate enumeration");
        baseline.AssignBowls();Check(baseline.bowlAssignments.Count==0&&baseline.bakeryLeases.Count==0&&baseline.reserved.Count==0,"default never sees beyond-window recipes");
        var existing=Make(0,8);var same=Make(8,8);existing.AssignBowls();same.AssignBowls();
        Check(existing.bowlAssignments.OrderBy(p=>p.Key).SequenceEqual(same.bowlAssignments.OrderBy(p=>p.Key))&&same.bakeryLeases.Count==0,"ordinary in-window assignments unchanged");
        int existingNear=existing.Stations("bowl").OrderByDescending(b=>b.Position.X).First().EntityId;
        Check(existing.Supply(1,CarnivalRecipes.Flour,existingNear,true)&&same.Supply(1,CarnivalRecipes.Flour,existingNear,true),"ordinary bowl supply remains admitted");
        Check(JsonNode.DeepEquals(new JsonArray(existing.workers[1]!.Actions.Select(a=>(JsonNode?)a.DeepClone()).ToArray()),new JsonArray(same.workers[1]!.Actions.Select(a=>(JsonNode?)a.DeepClone()).ToArray())),"same ordinary supply emits identical actions");
        var extended=Make();extended.AssignBowls();
        Check(extended.bowlAssignments.Count==2&&extended.bowlAssignments.Values.Order().SequenceEqual(new[]{4,5}),"two existing bowls get next distinct future indices");
        Check(extended.bakeryLeases.Count==2&&extended.bakeryLeases.Values.Select(l=>l.Counter).Distinct().Count()==2,"two disjoint ordinary parking counters reserved");
        Check(extended.Pending().Select(p=>p.Index).SequenceEqual(new[]{0,1,2,3})&&extended.mealPlates.Count==0&&extended.basketAssignments.Count==0,"ordinary meals/plates/fryers remain unchanged");
        var assignments=extended.bowlAssignments.OrderBy(p=>p.Key).ToArray();extended.AssignBowls();Check(assignments.SequenceEqual(extended.bowlAssignments.OrderBy(p=>p.Key)),"future indices remain stable across repeated assignment");
        var duplicate=Make();duplicate.recipes[4]=duplicate.recipes[5]=CarnivalRecipes.GetRecipe(130976);duplicate.AssignBowls();
        Check(duplicate.bowlAssignments.Values.Distinct().Count()==2&&duplicate.bowlFlavors.Values.Distinct().Count()==1,"equal flavors retain separate stable order ownership");
        var noSpace=Make();foreach(var station in noSpace.Stations("counter"))noSpace.reserved.Add(station.EntityId);noSpace.AssignBowls();
        Check(noSpace.bowlAssignments.Count==0&&noSpace.bakeryLeases.Count==0,"no future bowl admitted without storage");
        var lease=extended.bakeryLeases.Values.Single(l=>l.Index==4);Dough(extended,lease.Bowl,false,9);extended.AdvanceBakeryLookahead();
        int owner=lease.Owner;Check(owner is 0 or 3&&lease.Phase==BakeryPhase.Parking,"one available central chef owns native rescue");
        Check(extended.workers[owner]!.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"navigate","mix","take","place"}),"rescue consists only of ordinary input actions");
        Check(extended.workers[owner]!.OwnedResources.SetEquals(new[]{lease.Bowl})&&lease.Resources.All(extended.reserved.Contains),"worker and persistent storage leases have separate ownership");
        Park(extended,lease);Check(lease.Phase==BakeryPhase.Parked&&lease.Owner==-1&&extended.workers.All(w=>w is null),"two advancing proof frames release the parked bowl's chef");
        Check(!extended.PrepareDonut(0)&&extended.basketAssignments.Count==0&&extended.workers.All(w=>w is null),"future ready dough cannot enter a fryer before ordinary eligibility");
        Check(extended.AvailablePlates().Length==baseline.AvailablePlates().Length&&extended.mealPlates.Count==0,"future storage allocates no plates");
        extended.Entity(lease.Bowl)!["mixingProgress"]=12.2;Reject(extended.AdvanceBakeryLookahead,"parked native progress changes rejected");extended.Entity(lease.Bowl)!["mixingProgress"]=12.1374874;
        extended.state["delivered"]=1;extended.Refresh();Check(extended.PrepareDonut(0),"same parked index enters ordinary frying window");
        Check(lease.Phase==BakeryPhase.Transferring&&extended.workers[0]!.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"take","combine","place"}),"eligible dough uses existing native pour and empty-bowl return actions");
        var transfer=extended.workers[0]!;int basket=transfer.Resources.Single(id=>id!=lease.Bowl);
        extended.Entity(lease.Counter)!["attachedEntityId"]=0;extended.Entity(lease.Home)!["attachedEntityId"]=lease.Bowl;
        extended.Entity(lease.Bowl)!["composition"]=baseline.Entity(lease.Bowl)!["composition"]!.DeepClone();extended.Entity(lease.Bowl)!["mixingProgress"]=0;
        extended.Entity(basket)!["composition"]=nativeOffmixProof["transfer"]!["basketNativeComposition"]!.DeepClone();extended.chefs[0]["heldEntityId"]=0;
        Finish(extended,0);Check(!extended.bakeryLeases.ContainsKey(lease.Bowl)&&!extended.reserved.Contains(lease.Home)&&!extended.reserved.Contains(lease.Counter)&&extended.basketAssignments[basket]==4,"native transfer releases only its exact storage lease and preserves order index");
        Check(extended.bakeryLeases.Count==1&&extended.bakeryLeases.Values.Single().Resources.All(extended.reserved.Contains),"other future bowl reservations survive completion");
        var deadline=Make();deadline.AssignBowls();var due=deadline.bakeryLeases.Values.Single(l=>l.Index==4);Dough(deadline,due.Bowl,true,21);
        Reject(deadline.AdvanceBakeryLookahead,"native21-second rescue deadline rejects before overmix");
        var missing=Make();missing.AssignBowls();var changed=missing.bakeryLeases.Values.First();missing.Entity(changed.Counter)!["observedOrdinal"]=changed.CounterOrdinal+1;
        Reject(missing.AdvanceBakeryLookahead,"recycled parking identity rejected");
        var horizon=Make();horizon.AssignBowls();var promoted=horizon.bakeryLeases.Values.Single(l=>l.Index==4);horizon.state["delivered"]=1;horizon.Refresh();horizon.AdvanceBakeryLookahead();
        Check(!horizon.bakeryLeases.ContainsKey(promoted.Bowl)&&horizon.bowlAssignments[promoted.Bowl]==4&&!horizon.reserved.Contains(promoted.Counter),"ordinary promotion before rescue releases unneeded parking without changing assignment");
        Check(I(extended.state["score"])==I(baseline.state["score"]),"offline policy never synthesizes score");
        return checks;
    }
}
