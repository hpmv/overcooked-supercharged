using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class PreparedFlavorDelivery(int bowl, int home, int board, int crate, int index, int flavor, int frame, JsonObject guard)
    {
        public readonly int Bowl=bowl,Home=home,Board=board,Crate=crate,Index=index,Flavor=flavor,Started=frame;
        public readonly JsonObject HomeGuard=guard;
        public readonly Dictionary<int,int> Identities=[];
        public Work Work=null!;
        public int Raw,RawOrdinal,Prepared,PreparedOrdinal,NativeChopFrames,FlightFrames;
        public double MinimumWork=double.PositiveInfinity,MaximumWork=double.NegativeInfinity;
        public bool PreparedHeld,Accepted;
        public int[] Resources=>[Bowl,Home,Board,Crate];
    }
    private readonly Dictionary<Work,PreparedFlavorDelivery> preparedFlavorDeliveries=[];

    private bool TrySupplyPreparedFlavor(int bowl,NativeIngredient ingredient)
    {
        if(!options.DirectPreparedFlavorThrows||!options.PantryChopping||workers[1] is not null||Held(1)!=0||Region(1)!="upper-right"||
            !B(chefs[1]["controlsEnabled"])||NativeCannonFlight(1)||ingredient!=CarnivalRecipes.Chocolate&&ingredient!=CarnivalRecipes.Raspberry||
            !bowlAssignments.TryGetValue(bowl,out int index)||index<0||index>=recipes.Length||!bowlFlavors.TryGetValue(bowl,out int flavor)||
            flavor!=ingredient.Id||!WithinOrdinaryWindow(index)||!CompatiblePartialMixer(bowl,out _,out var actual,out var required)||
            !actual.SequenceEqual(new[]{CarnivalRecipes.Egg.Id,CarnivalRecipes.Flour.Id}.Order())||
            !required.SequenceEqual(actual.Append(flavor).Order())||DirectSupplyGuard(1,bowl) is not {} guard)return false;
        int board=Board("upper-right",false),home=I(guard["home"]);
        int crate=model.Stations.Single(s=>s.Ingredient==ingredient.Name).EntityId;
        if(!EmptyAttachment(board)||!Free(bowl,home,board,crate)||HeldByAnyone(bowl)||
            counterSupplies.Values.Any(s=>s.Vessel==bowl)||LooseIngredientCount(flavor)>0||
            N(Entity(bowl)?["mixingTime"])!=12||Entity(bowl)?["observedOrdinal"] is null||
            Entity(board)?["observedOrdinal"] is null||Entity(crate)?["observedOrdinal"] is null)return false;
        double progress=N(Entity(bowl)?["mixingProgress"]),speed=N(chefs[1]["runSpeed"])*N(chefs[1]["surfaceSpeedMultiplier"]);
        if(!double.IsFinite(progress)||progress<0||!double.IsFinite(speed)||speed<=0)return false;
        var obstacles=TrafficObstacles(1);
        var first=Navigation.ToStation(model,Position(1),Station(crate)!,obstacles);
        if(!first.Success)return false;
        var second=Navigation.ToStation(model,first.Points[^1],Station(board)!,obstacles);
        if(!second.Success)return false;
        var staging=ThrowStaging(1);var third=Navigation.FindPath(model,second.Points[^1],KitchenModel.Position(staging["target"]),dynamic:obstacles);
        if(!third.Success)return false;
        double seconds=(first.Length+second.Length+third.Length)/speed+ingredient.ChopSeconds+2;
        if(seconds>=21-progress)return false;
        staging["dash"]=options.UseDash||options.UseShortDash;staging["shortDash"]=options.UseShortDash;
        var delivery=new PreparedFlavorDelivery(bowl,home,board,crate,index,flavor,Frame,guard);
        foreach(int id in delivery.Resources)delivery.Identities[id]=I(Entity(id)?["observedOrdinal"]);
        var actions=new List<JsonObject>{A("take",crate),A("place",board),A("chop",board),A("take",board),staging,
            new(){["type"]="throw",["targetEntityId"]=bowl,["nativeSupplyHome"]=guard.DeepClone()}};
        if(!Start(1,"supply-prepared-flavor-throw-"+(index+1),actions,delivery.Resources,()=>
        {
            ObservePreparedFlavor(delivery,false);
            if(!delivery.Accepted||Held(1)!=0||Attached(board)!=0)
                throw new InvalidOperationException("Prepared-flavor supply lacks its exact native bowl catch and empty controlled supplier.");
            preparedFlavorDeliveries.Remove(delivery.Work);
            Log("nativePreparedFlavorSupplyComplete",PreparedFlavorStatus(delivery));
        }))return false;
        delivery.Work=workers[1]!;preparedFlavorDeliveries.Add(delivery.Work,delivery);
        var report=PreparedFlavorStatus(delivery);report["estimatedWalkingChopAndEdgesSeconds"]=seconds;
        report["nativeMixProgressAtAdmission"]=progress;Log("nativePreparedFlavorSupplyStarted",report);
        return true;
    }

    private void ObservePreparedFlavorDeliveries()
    {
        foreach(var delivery in preparedFlavorDeliveries.Values)ObservePreparedFlavor(delivery,true);
    }

    private void ObservePreparedFlavor(PreparedFlavorDelivery d,bool requireWork)
    {
        if(d.Identities.Any(p=>!SameObservedEntity(p.Key,p.Value)||!B(Entity(p.Key)?["active"]))||NativeSupplyHome.Invalid(state,d.HomeGuard) is not null||
            bowlAssignments.GetValueOrDefault(d.Bowl,-1)!=d.Index||bowlFlavors.GetValueOrDefault(d.Bowl)!=d.Flavor||
            !B(chefs[1]["controlsEnabled"])||Region(1)!="upper-right"||NativeCannonFlight(1)||
            requireWork&&(!ReferenceEquals(workers[1],d.Work)||!d.Work.OwnedResources.SetEquals(d.Resources)||
                d.Resources.Any(id=>!reserved.Contains(id))||workers.OfType<Work>().Any(w=>w!=d.Work&&w.OwnedResources.Overlaps(d.Resources))))
            throw new InvalidOperationException("Prepared-flavor supply lost its exact original home, assignment, supplier or source reservations.");
        var progressValue=Entity(d.Bowl)?["mixingProgress"];
        double progress=N(progressValue);
        if(progressValue is null||!double.IsFinite(progress)||progress<0)
            throw new InvalidOperationException("Prepared-flavor supply lacks a finite nonnegative native mixing observation.");
        if(Frame-d.Started>600||N(Entity(d.Bowl)?["mixingTime"])!=12||progress>=21||Food(d.Bowl).IsRuined)
            throw new TimeoutException("Prepared-flavor supply exceeded its ordinary native mixer/input deadline.");
        int[] before=[CarnivalRecipes.Egg.Id,CarnivalRecipes.Flour.Id],after=before.Append(d.Flavor).Order().ToArray();
        int[] current=Food(d.Bowl).IngredientIds.Order().ToArray();
        if(!current.SequenceEqual(before.Order())&&!current.SequenceEqual(after))
            throw new InvalidOperationException("Prepared-flavor receiving bowl changed outside its exact one-flavor delta.");
        int boardItem=Attached(d.Board);var item=Entity(boardItem);
        if(item is not null&&KitchenModel.Components(item).Contains("WorkableItem"))
        {
            var raw=CarnivalRecipes.ClassifyEntity(item);
            if(raw.Food.Kind!=FoodNodeKind.Ingredient||raw.Food.Preparation!=FoodPreparation.Raw||!raw.Food.IngredientIds.SequenceEqual(new[]{d.Flavor}))
                throw new InvalidOperationException("Prepared-flavor board contains a different raw ingredient.");
            if(d.Raw==0){d.Raw=boardItem;d.RawOrdinal=I(item["observedOrdinal"]);}
            if(d.Raw!=boardItem||d.RawOrdinal!=I(item["observedOrdinal"]))throw new InvalidOperationException("Prepared-flavor raw observation identity changed.");
            d.MinimumWork=Math.Min(d.MinimumWork,N(item["workProgress"]));d.MaximumWork=Math.Max(d.MaximumWork,N(item["workProgress"]));
            if(I(chefs[1]["interactingEntityId"])==d.Board&&I(chefs[1]["serverInteractionId"])==d.Board&&Held(1)==0)d.NativeChopFrames++;
        }
        else if(item is not null&&Chopped(boardItem,d.Flavor))
        {
            if(d.Raw==0||boardItem==d.Raw||d.NativeChopFrames==0||d.MaximumWork-d.MinimumWork<.5||
                !Food(boardItem).IngredientIds.SequenceEqual(new[]{d.Flavor})||!KitchenModel.Components(item).Contains("ThrowableItem"))
                throw new InvalidOperationException("Prepared flavor lacks its exact observed native chopping/replacement evidence.");
            if(d.Prepared==0){d.Prepared=boardItem;d.PreparedOrdinal=I(item["observedOrdinal"]);}
        }
        var prepared=Entity(d.Prepared);
        if(prepared is not null)
        {
            if(I(prepared["observedOrdinal"])!=d.PreparedOrdinal||!Food(d.Prepared).IngredientIds.SequenceEqual(new[]{d.Flavor}))
                throw new InvalidOperationException("Prepared flavor changed before its native throw/catch.");
            if(Held(1)==d.Prepared)d.PreparedHeld=true;
            if(chefs.Where(c=>I(c["playerId"])!=1).Any(c=>I(c["heldEntityId"])==d.Prepared))
                throw new InvalidOperationException("Prepared flavor was intercepted by another chef; this optional path requires its exact native bowl catch.");
            if(B(prepared["throwFlying"])&&I(prepared["throwerEntityId"])==I(chefs[1]["entityId"]))d.FlightFrames++;
        }
        if(current.SequenceEqual(after))
        {
            if(d.Prepared==0||!d.PreparedHeld||d.FlightFrames==0||prepared is not null||Held(1)!=0||boardItem!=0)
                throw new InvalidOperationException("Receiving bowl gained flavor without exact prepared-source consumption and native P1 flight.");
            if(!d.Accepted){d.Accepted=true;Log("nativePreparedFlavorCatchObserved",PreparedFlavorStatus(d));}
        }
    }
    private JsonObject PreparedFlavorStatus(PreparedFlavorDelivery d)=>new(){["frame"]=Frame,["bowl"]=d.Bowl,["home"]=d.Home,["board"]=d.Board,
        ["orderIndex"]=d.Index,["flavor"]=d.Flavor,["rawItem"]=d.Raw,["preparedItem"]=d.Prepared,["preparedOrdinal"]=d.PreparedOrdinal,
        ["nativeChopFrames"]=d.NativeChopFrames,["nativeFlightFrames"]=d.FlightFrames,["accepted"]=d.Accepted,
        ["nativeMixProgress"]=Entity(d.Bowl)?["mixingProgress"]?.DeepClone()};
}
