using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    // The pan/home/empty parking counter remain owned by the original lease.
    // Only the exact plain food and its current source belong to this Work.
    private sealed class DirectEarlyOnion(EarlyOnionLease lease,int owner,int source,int food,Dictionary<int,int> identities)
    {
        public readonly EarlyOnionLease Lease=lease;
        public readonly int Owner=owner,Source=source,Food=food;
        public readonly Dictionary<int,int> Identities=identities;
        public Work Work=null!;
        public bool CookedObserved,Harvested;
        public int[] Resources => [Source,Food];
    }
    private readonly Dictionary<EarlyOnionLease,DirectEarlyOnion> directEarlyOnions=[];

    private bool NativeDirectOnionSource(int source) => Station(source) is { } station &&
        station.Role is "counter" or "chop" && station.Regions.Contains("center") &&
        KitchenModel.Components(Entity(source)!).Contains("AttachStation") && !NativeCookingStation(Entity(source)) &&
        !KitchenModel.Components(Entity(source)!).Any(c=>c is "MixingStation" or "ServerMixingStation" or "ClientMixingStation");

    private bool TryDirectEarlyOnionHarvest(EarlyOnionLease lease,int player)
    {
        if(lease.Phase!=EarlyOnionPhase.Heating || lease.Owner>=0 || !EarlyCookAvailable(player) ||
            !mealFoods.TryGetValue(lease.Index,out int food) || assembling.Contains(lease.Index) ||
            lease.Index<delivered || lease.Index>=recipes.Length || !recipes[lease.Index].RequiredInputs.Contains(CarnivalRecipes.Onion) ||
            !CarnivalRecipes.MatchRecipe(Entity(food),296560,false).ReadyToDeliver || CarnivalRecipes.ClassifyEntity(Entity(food)).IsPlate ||
            HeldByAnyone(food) || !PureOnion(lease.Pan) || Attached(lease.Home)!=lease.Pan || Attached(lease.Counter)!=0) return false;
        int source=AttachmentParent(food);
        if(source==0 || !Free(source,food) || !NativeDirectOnionSource(source) || Station(source) is not {} sourceStation || Station(lease.Pan) is not {} panStation ||
            source==lease.Pan || lease.Resources.Contains(source)) return false;
        int[] ids=[lease.Pan,lease.Home,lease.Counter,source,food];
        if(ids.Distinct().Count()!=ids.Length || ids.Any(id=>Entity(id)?["observedOrdinal"] is null || !B(Entity(id)?["active"])))return false;
        RequireEarlyOnionIdentity(lease);
        double progress=N(Entity(lease.Pan)?["cookingProgress"]),speed=N(chefs[player]["runSpeed"])*N(chefs[player]["surfaceSpeedMultiplier"]);
        if(progress is <9 or >=18 || !double.IsFinite(progress) || !double.IsFinite(speed) || speed<=0 || N(Entity(lease.Pan)?["cookingTime"])!=12)return false;
        var obstacles=TrafficObstacles(player);
        var take=Navigation.ToStation(model,Position(player),sourceStation,obstacles);
        if(!take.Success)return false;
        var combine=Navigation.ToStation(model,take.Points[^1],panStation,obstacles);
        if(!combine.Success)return false;
        var put=Navigation.ToStation(model,combine.Points[^1],sourceStation,obstacles);
        if(!put.Success)return false;
        double wait=Math.Max(0,12-progress),heatSeconds=(take.Length+combine.Length)/speed+wait+2;
        double wholeSeconds=heatSeconds+put.Length/speed+1;
        // Walking estimates remain conservative even with optional legal dash.
        // An unavailable or late source falls back to the proven whole-pan path.
        if(!double.IsFinite(heatSeconds) || heatSeconds>=21-progress || wholeSeconds>=12)return false;
        var plan=new DirectEarlyOnion(lease,player,source,food,ids.ToDictionary(id=>id,id=>I(Entity(id)?["observedOrdinal"])));
        var actions=new[]{OnionAction("take",source),OnionAction("navigate",lease.Pan),OnionAction("cook",lease.Pan),OnionAction("combine",lease.Pan),OnionAction("place",source)};
        if(!Start(player,"direct-early-onion-harvest-"+(lease.Index+1),actions,plan.Resources,()=>
        {
            ObserveDirectEarlyOnion(lease,requireWork:false);
            if(!plan.CookedObserved || !plan.Harvested || Held(player)!=0 || Attached(source)!=food ||
                !EmptyFood(lease.Pan) || N(Entity(lease.Pan)?["cookingProgress"])!=0 || Attached(lease.Home)!=lease.Pan || Attached(lease.Counter)!=0 ||
                !CarnivalRecipes.MatchRecipe(Entity(food),472326,false).ReadyToDeliver)
                throw new InvalidOperationException("Direct leased-onion harvest lacks its exact native cooked hotdog, empty home pan and original source return.");
            Log("nativeDirectEarlyOnionComplete",DirectEarlyOnionStatus(plan));
            assembling.Remove(lease.Index);
            directEarlyOnions.Remove(lease);
            foreach(int id in lease.Resources)reserved.Remove(id);
            if(ReferenceEquals(earlyOnion,lease))earlyOnion=null;
            else if(ReferenceEquals(secondEarlyOnion,lease))secondEarlyOnion=null;
            else throw new InvalidOperationException("Direct onion harvest completed an unregistered lease.");
        }))return false;
        plan.Work=workers[player]!;directEarlyOnions.Add(lease,plan);assembling.Add(lease.Index);
        SetEarlyOnionPhase(lease,EarlyOnionPhase.DirectHarvest,player);
        var description=DirectEarlyOnionStatus(plan);description["estimatedHeatSeconds"]=heatSeconds;description["estimatedWholeSeconds"]=wholeSeconds;
        description["nativeRemainingCookSeconds"]=wait;description["guardSeconds"]=21;
        description["sourcePathLength"]=take.Length;description["panPathLength"]=combine.Length;description["returnPathLength"]=put.Length;
        Log("nativeDirectEarlyOnionAdmitted",description);
        return true;
    }

    private void ObserveDirectEarlyOnion(EarlyOnionLease lease,bool requireWork=true)
    {
        if(!directEarlyOnions.TryGetValue(lease,out var plan) || lease.Phase!=EarlyOnionPhase.DirectHarvest || lease.Owner!=plan.Owner ||
            !EarlyOnionLeases().Any(l=>ReferenceEquals(l,lease)) || plan.Identities.Any(p=>!SameObservedEntity(p.Key,p.Value) || !B(Entity(p.Key)?["active"])) ||
            mealFoods.GetValueOrDefault(lease.Index)!=plan.Food || !assembling.Contains(lease.Index) || !NativeDirectOnionSource(plan.Source))
            throw new InvalidOperationException("Direct onion harvest lost its exact leased meal or observed identities.");
        RequireEarlyOnionIdentity(lease);
        if(requireWork && (!ReferenceEquals(workers[plan.Owner],plan.Work) || !plan.Work.OwnedResources.SetEquals(plan.Resources) ||
            plan.Resources.Any(id=>!reserved.Contains(id)) || workers.OfType<Work>().Any(w=>w!=plan.Work && w.OwnedResources.Overlaps(plan.Resources.Concat(lease.Resources)))))
            throw new InvalidOperationException("Direct onion harvest lost its original Work or exclusive food/source ownership.");
        if(Attached(lease.Home)!=lease.Pan || AttachmentParent(lease.Pan)!=lease.Home || Attached(lease.Counter)!=0 || HeldByAnyone(lease.Pan) ||
            !B(chefs[plan.Owner]["controlsEnabled"]) || Region(plan.Owner)!="center" || NativeCannonFlight(plan.Owner) ||
            N(Entity(lease.Pan)?["cookingTime"])!=12 || Food(lease.Pan).IsRuined || Food(plan.Food).IsRuined ||
            Held(plan.Owner)!=plan.Food && !(Held(plan.Owner)==0 && Attached(plan.Source)==plan.Food) ||
            Attached(plan.Source)!=0 && Attached(plan.Source)!=plan.Food ||
            new[]{0,1,2,3}.Any(p=>p!=plan.Owner && Held(p)==plan.Food))
            throw new InvalidOperationException("Direct onion harvest changed native pan attachment, carried food, source or chef controls.");
        double progress=N(Entity(lease.Pan)?["cookingProgress"]);
        if(!double.IsFinite(progress) || progress>=21 || Frame-lease.PhaseFrame>720)
            throw new TimeoutException("Direct onion harvest exceeded its native heat/action deadline.");
        bool empty=EmptyFood(lease.Pan),plain=CarnivalRecipes.MatchRecipe(Entity(plan.Food),296560,false).ReadyToDeliver;
        if(!empty)
        {
            if(plan.Harvested || !plain || !PureOnion(lease.Pan))throw new InvalidOperationException("Direct onion food changed before native pan consumption.");
            if(NativeOnionCooked(lease.Pan) && !plan.CookedObserved){plan.CookedObserved=true;Log("nativeDirectEarlyOnionCooked",DirectEarlyOnionStatus(plan));}
        }
        else
        {
            if(!plan.CookedObserved || !CarnivalRecipes.MatchRecipe(Entity(plan.Food),472326,false).ReadyToDeliver || progress!=0 ||
                !plan.Harvested && Held(plan.Owner)!=plan.Food)
                throw new InvalidOperationException("Direct onion disappearance lacks prior native cooking and exact held onion-hotdog contents.");
            if(!plan.Harvested){plan.Harvested=true;Log("nativeDirectEarlyOnionConsumed",DirectEarlyOnionStatus(plan));}
        }
    }
    private JsonObject DirectEarlyOnionStatus(DirectEarlyOnion p)=>new(){["frame"]=Frame,["player"]=p.Owner,["mealIndex"]=p.Lease.Index,
        ["pan"]=p.Lease.Pan,["home"]=p.Lease.Home,["unusedParkingCounter"]=p.Lease.Counter,["source"]=p.Source,["food"]=p.Food,
        ["nativeProgress"]=Entity(p.Lease.Pan)?["cookingProgress"]?.DeepClone(),["cookedObserved"]=p.CookedObserved,["consumedIntoExactMeal"]=p.Harvested,
        ["originalPanStayedOnHome"]=Attached(p.Lease.Home)==p.Lease.Pan};
}
