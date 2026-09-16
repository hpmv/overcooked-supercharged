using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed record BakeryPlateToken(int Plate,int Ordinal,long Registration,int Index,string Kind);
    private sealed class PredictiveBakeryVisit(int bowl,int home,int index,int flavor,int frame,int deadline,JsonObject evidence)
    {
        public readonly int Bowl=bowl,Home=home,Index=index,Flavor=flavor,Started=frame,Deadline=deadline;
        public readonly JsonObject Evidence=evidence;
        public readonly Dictionary<int,int> Identities=[];
        public Work? CurrentWork;
        public readonly HashSet<int> CurrentResources=[];
        public bool Issued,Arrived;
        public int ExpectedIngredient;
        public int[] LastIngredients=[];
    }
    private PredictiveBakeryVisit? predictiveBakeryVisit;
    private string lastPredictiveBakeryGate="";

    private static void ValidatePredictiveBakeryOption(CarnivalPlannerOptions configuration)
    {
        if(configuration.PredictiveBakeryReturn&&(!configuration.DirectVesselThrows||!configuration.PantryChopping||!configuration.DirectPreparedFlavorThrows))
            throw new ArgumentException("Predictive bakery return requires direct vessel throws, pantry chopping and direct flavor throws for its measured one-kit route.");
    }

    private BakeryPlateToken[] PredictivePlatePipeline()
    {
        var tokens=new List<BakeryPlateToken>();
        foreach(var entity in entities.Values.Where(IsPlate))
        {
            int id=Id(entity),ordinal=entity["observedOrdinal"]?.GetValue<int>()??-1;
            if(!B(entity["active"])||ordinal<0||retiredPlates.ContainsKey(id)||!CanAdoptPlate(entity)||
                Food(id).IsRuined||!HeldByAnyone(id)&&AttachmentParent(id)==0)continue;
            var meals=mealPlates.Where(p=>p.Value==id&&p.Key>=delivered&&p.Key<recipes.Length).ToArray();
            if(meals.Length>1)return []; // One real plate cannot satisfy two meal commitments.
            if(meals.Length==1&&CarnivalRecipes.MatchRecipe(entity,recipes[meals[0].Key].Id).ReadyToDeliver)
            {tokens.Add(new(id,ordinal,PlateRegistrationSequence(id),meals[0].Key,"native-complete-unserved"));continue;}
            if(!EmptyFood(id))continue;
            if(AvailablePlates().Any(p=>Id(p)==id))
            {tokens.Add(new(id,ordinal,PlateRegistrationSequence(id),-1,"native-clean-available"));continue;}
            var owners=workers.OfType<Work>().Where(w=>w.OwnedResources.Contains(id)&&w.Name.StartsWith("assemble-meal-",StringComparison.Ordinal)).ToArray();
            if(owners.Length!=1||!reserved.Contains(id))continue;
            int[] indices=assembling.Where(index=>index>=delivered&&index<recipes.Length&&
                owners[0].Name=="assemble-meal-"+(index+1)+"-"+recipes[index].Name&&plating.Contains(index)).ToArray();
            if(indices.Length==1&&chefs.Where(c=>I(c["heldEntityId"])==id).Any(c=>ReferenceEquals(workers[I(c["playerId"])],owners[0])))
                tokens.Add(new(id,ordinal,PlateRegistrationSequence(id),indices[0],"native-clean-in-exact-assembly"));
        }
        if(tokens.Select(t=>t.Plate).Distinct().Count()!=tokens.Count||tokens.Select(t=>t.Ordinal).Distinct().Count()!=tokens.Count)return [];
        return tokens.OrderBy(t=>t.Plate).ToArray();
    }

    private bool NoPredictiveDirtyWork()=>DirtyCount()==0&&I(Entity(Single("sink"))?["plateCount"])==0&&
        I(Entity(Single("drying"))?["plateCount"])==0&&Attached(Counter(15.6,-19.2))==0&&
        !chefs.Any(c=>IsDirty(I(c["heldEntityId"])));
    private int PredictiveCrate(NativeIngredient ingredient)
    {var matches=model.Stations.Where(s=>s.Ingredient==ingredient.Name).ToArray();return matches.Length==1?matches[0].EntityId:0;}

    private JsonObject? PredictiveBakeryEvidence()
    {
        lastPredictiveBakeryGate="supplier and immediate washing work";
        if(!options.PredictiveBakeryReturn||predictiveBakeryVisit is not null||workers[1] is not null||Held(1)!=0||
            Region(1)!="lower-left"||!TrafficControlsReady(1)||NativeCannonFlight(1)||!NoPredictiveDirtyWork()||
            IsSauceParticipant(1)||IsEarlyOnionParticipant(1)||IsBakeryParticipant(1)||IsFryerParticipant(1)||IsPotParticipant(1)||
            IsCannonArrivalPassenger(1)||trafficYield is not null||ActiveBakeryCommitments().Length!=0)return null;
        var tokens=PredictivePlatePipeline();
        lastPredictiveBakeryGate="plate pipeline";
        if(tokens.Length<3||!tokens.Any(t=>t.Kind=="native-clean-available")||!tokens.Any(t=>t.Index==delivered))return null;
        var next=Pending().Where(p=>IsDonut(p.Recipe)&&!basketAssignments.Values.Contains(p.Index)).OrderBy(p=>p.Index).FirstOrDefault();
        lastPredictiveBakeryGate="earliest donut";
        if(next.Recipe is null)return null;
        AssignBowls();
        int bowl=NearSupplyVessel(1);
        lastPredictiveBakeryGate="exact empty bowl/home/assignment";
        if(!bowlAssignments.TryGetValue(bowl,out int index)||index!=next.Index||!bowlHomes.TryGetValue(bowl,out int home)||
            bowlAssignments.Count(p=>p.Value==index)!=1||
            !bowlFlavors.TryGetValue(bowl,out int flavor)||DirectSupplyGuard(1,bowl) is not {} supply||
            !EmptyFood(bowl)||Attached(home)!=bowl||!Free(bowl,home)||
            Entity(bowl)?["mixingProgress"] is not {} progress||!double.IsFinite(N(progress))||N(progress)!=0||N(Entity(bowl)?["mixingTime"])!=12||
            counterSupplies.Values.Any(s=>s.Vessel==bowl)||bakeryLeases.ContainsKey(bowl))return null;
        int board=Board("upper-right",false);
        lastPredictiveBakeryGate="ingredient sources and chopping space";
        int[] crates=next.Recipe.RequiredInputs.Select(PredictiveCrate).ToArray();
        if(crates.Contains(0)||!EmptyAttachment(board)||!Free(crates.Append(board).ToArray())||LooseIngredientCount(flavor)>0)return null;
        var edge=model.Transitions.SingleOrDefault(e=>e.Kind=="portal"&&e.Verified&&e.FromRegion=="lower-left"&&e.ToRegion=="upper-right");
        lastPredictiveBakeryGate="observed portal arrival";
        if(edge?.DestinationKey is null||bakeryPortalArrival is not {} arrival||arrival.Frame>=Frame||
            model.Resolve(edge.DestinationKey).EntityId!=arrival.Receiver||!SameObservedEntity(arrival.Receiver,arrival.ReceiverOrdinal))return null;
        int portal=model.Resolve(edge.SourceKey).EntityId;
        int[] identityIds=crates.Concat(new[]{bowl,home,board,portal,arrival.Receiver}).Distinct().ToArray();
        lastPredictiveBakeryGate="active source identities";
        if(identityIds.Any(id=>Entity(id)?["observedOrdinal"] is null||I(Entity(id)?["observedOrdinal"])<0||!B(Entity(id)?["active"])))return null;
        double speed=N(chefs[1]["runSpeed"])*N(chefs[1]["surfaceSpeedMultiplier"]);
        if(!double.IsFinite(speed)||speed<=0)return null;
        var obstacles=TrafficObstacles(1);var portalPath=Navigation.ToStation(model,Position(1),Station(portal)!,obstacles);
        lastPredictiveBakeryGate="portal walking path";
        if(!portalPath.Success)return null;
        Point2 cursor=arrival.Position,target=KitchenModel.Position(ThrowStaging(1)["target"]);double length=portalPath.Length;
        var paths=new List<NavigationPath>{portalPath};
        foreach(var ingredient in new[]{CarnivalRecipes.Flour,CarnivalRecipes.Egg,CarnivalRecipes.Ingredients[flavor]})
        {
            lastPredictiveBakeryGate="ingredient walking path: "+ingredient.Name;
            int crate=PredictiveCrate(ingredient);
            var pickup=Navigation.ToStation(model,cursor,Station(crate)!,obstacles);if(!pickup.Success)return null;
            length+=pickup.Length;paths.Add(pickup);cursor=pickup.Points[^1];
            if(ingredient.ChopSeconds>0)
            {var chop=Navigation.ToStation(model,cursor,Station(board)!,obstacles);if(!chop.Success)return null;length+=chop.Length;paths.Add(chop);cursor=chop.Points[^1];}
            var staging=Navigation.FindPath(model,cursor,target,dynamic:obstacles);if(!staging.Success)return null;
            length+=staging.Length;paths.Add(staging);cursor=staging.Points[^1];
        }
        double kitSeconds=length/speed+2+6+CarnivalRecipes.Ingredients[flavor].ChopSeconds;
        // Bound the downstream walking using actual clear center routes. This
        // is admission headroom, not a promise that future chefs are idle.
        var downstream=PredictiveDownstreamBudget(bowl,tokens);
        if(downstream is not null)lastPredictiveBakeryGate="downstream budget: "+downstream.ToJsonString();
        if(downstream is null)return null;
        double production=kitSeconds+12+10+N(downstream["seconds"])+4;
        if(!double.IsFinite(N(state["timer"]))||production>=N(state["timer"]))return null;
        return new(){["frame"]=Frame,["bowl"]=bowl,["home"]=home,["index"]=index,["recipeId"]=next.Recipe.Id,["flavor"]=flavor,
            ["plateTokens"]=JsonSerializer.SerializeToNode(tokens),["identities"]=JsonSerializer.SerializeToNode(identityIds.ToDictionary(id=>id,id=>I(Entity(id)?["observedOrdinal"]))),
            ["nativeSupplyHome"]=supply,["priorNativeArrival"]=JsonSerializer.SerializeToNode(arrival),["paths"]=JsonSerializer.SerializeToNode(paths),
            ["kitBudgetSeconds"]=kitSeconds,["downstreamBudget"]=downstream,["productionBudgetSeconds"]=production,["remainingNativeSeconds"]=state["timer"]!.DeepClone(),
            ["qualification"]="One existing ordinary-window bowl and native plate pipeline; measured walking plus native waits and input headroom, not guaranteed future scheduling"};
    }

    private JsonObject? PredictiveDownstreamBudget(int bowl,BakeryPlateToken[] tokens)
    {
        lastPredictiveBakeryGate="downstream service chef";
        if(Region(2)!="lower-right"||!B(chefs[2]["controlsEnabled"])||!B(chefs[2]["directlyControlled"])||
            !B(chefs[2]["canAcceptInput"])||B(chefs[2]["inputSuppressed"])||B(chefs[2]["respawning"])||NativeCannonFlight(2))return null;
        int clean=tokens.Where(t=>t.Kind=="native-clean-available").Select(t=>t.Plate).First();
        int output=new[]{Counter(25.2,-18),Counter(25.2,-20.4)}.FirstOrDefault(id=>EmptyAttachment(id)&&Free(id));
        int basket=Stations("basket").Select(s=>s.EntityId).FirstOrDefault(id=>EmptyFood(id)&&Free(id)&&
            fryerHomes.TryGetValue(id,out var home)&&Attached(home)==id&&Free(home)&&!fryerRescues.ContainsKey(id));
        lastPredictiveBakeryGate="downstream output "+output+" basket "+basket;
        if(output==0||basket==0||N(Entity(basket)?["cookingTime"])!=10)return null;
        int basketHome=fryerHomes[basket],bowlHome=bowlHomes[bowl];
        var estimates=new List<(int Player,double Seconds)>();
        foreach(int player in new[]{0,3})
        {
            double speed=N(chefs[player]["runSpeed"])*N(chefs[player]["surfaceSpeedMultiplier"]);
            if(!double.IsFinite(speed)||speed<=0||Region(player)!="center")return null;
            Point2 cursor=Position(player);double length=0;bool success=true;
            foreach(int target in new[]{bowl,basketHome,bowlHome,clean,basket,output})
            {
                var station=Station(target)??Station(AttachmentParent(target));
                if(station is null){success=false;break;}
                var path=Navigation.ToStation(model,cursor,station,TrafficObstacles(player));
                if(!path.Success){lastPredictiveBakeryGate="downstream chef "+player+" target "+target+": "+path.Error;success=false;break;}length+=path.Length;cursor=path.Points[^1];
            }
            if(success)estimates.Add((player,length/speed+8));
        }
        if(estimates.Count==0)return null;
        var take=Navigation.ToStation(model,Position(2),Station(output)!,TrafficObstacles(2));
        int delivery=Single("delivery");
        var serve=take.Success?Navigation.ToStation(model,take.Points[^1],Station(delivery)!,TrafficObstacles(2)):take;
        double run=N(chefs[2]["runSpeed"])*N(chefs[2]["surfaceSpeedMultiplier"]);
        lastPredictiveBakeryGate="downstream service: take="+take.Error+" serve="+serve.Error;
        if(!take.Success||!serve.Success||!double.IsFinite(run)||run<=0)return null;
        double service=(take.Length+serve.Length)/run+2;
        return new(){["seconds"]=estimates.Max(e=>e.Seconds)+service,["centerRoutes"]=JsonSerializer.SerializeToNode(estimates.Select(e=>new{player=e.Player,seconds=e.Seconds})),
            ["serviceSeconds"]=service,["emptyOriginalBasket"]=basket,["basketHome"]=basketHome,["cleanPlate"]=clean,["candidateOutput"]=output,
            ["qualification"]="Maximum successful current-center route plus8s native transfer allowance, current lower-right service route plus2s; resources are not reserved by this estimate"};
    }

    private bool TryPredictiveBakeryReturn()
    {
        var evidence=PredictiveBakeryEvidence();if(evidence is null)return false;
        var visit=new PredictiveBakeryVisit(I(evidence["bowl"]),I(evidence["home"]),I(evidence["index"]),I(evidence["flavor"]),Frame,
            Frame+(int)Math.Ceiling(N(evidence["kitBudgetSeconds"])*60)+120,evidence);
        foreach(var p in evidence["identities"]!.AsObject())visit.Identities.Add(int.Parse(p.Key,System.Globalization.CultureInfo.InvariantCulture),I(p.Value));
        if(!Start(1,"predictive-one-kit-return-to-bakery",[Portal("upper-right")],[]))return false;
        visit.CurrentWork=workers[1];predictiveBakeryVisit=visit;Log("predictiveBakeryVisitStarted",PredictiveBakeryStatus());return true;
    }

    private void ObservePredictiveBakeryVisit()
    {
        if(predictiveBakeryVisit is not {} v)return;
        if(v.Identities.Any(p=>!SameObservedEntity(p.Key,p.Value)||!B(Entity(p.Key)?["active"]))||
            NativeSupplyHome.Invalid(state,v.Evidence["nativeSupplyHome"]!.AsObject()) is not null||
            v.Index<delivered||v.Index>=recipes.Length||recipes[v.Index].Id!=I(v.Evidence["recipeId"])||
            bowlAssignments.GetValueOrDefault(v.Bowl,-1)!=v.Index||bowlFlavors.GetValueOrDefault(v.Bowl)!=v.Flavor)
            throw new InvalidOperationException("Predictive bakery visit lost its exact bowl/home, source identity or assignment.");
        if(Frame>v.Deadline)throw new TimeoutException("Predictive bakery visit exceeded its measured one-kit deadline.");
        double progress=N(Entity(v.Bowl)?["mixingProgress"]);
        if(Entity(v.Bowl)?["mixingProgress"] is null||!double.IsFinite(progress)||progress<0||progress>=21||N(Entity(v.Bowl)?["mixingTime"])!=12||Food(v.Bowl).IsRuined)
            throw new InvalidOperationException("Predictive bakery visit lacks a safe finite native mixing observation.");
        int[] required=recipes[v.Index].RequiredInputs.Select(i=>i.Id).Order().ToArray(),actual=Food(v.Bowl).IngredientIds.Order().ToArray();
        if(actual.Length!=actual.Distinct().Count()||actual.Any(i=>!required.Contains(i))||v.LastIngredients.Any(i=>!actual.Contains(i)))
            throw new InvalidOperationException("Predictive bakery visit changed outside its exact accumulating ingredient kit.");
        int[] gained=actual.Except(v.LastIngredients).ToArray();
        if(gained.Length>0&&(!v.Issued||gained.Length!=1||gained[0]!=v.ExpectedIngredient))
            throw new InvalidOperationException("Predictive bakery visit gained an ingredient outside its exact issued native supply job.");
        if(gained.Length==1)v.ExpectedIngredient=0;
        v.LastIngredients=actual;
        if(workers[1] is {} work&&(!ReferenceEquals(v.CurrentWork,work)||!work.OwnedResources.SetEquals(v.CurrentResources)||
            v.CurrentResources.Any(id=>!reserved.Contains(id))||workers.OfType<Work>().Any(w=>!ReferenceEquals(work,w)&&w.OwnedResources.Overlaps(v.CurrentResources))))
            throw new InvalidOperationException("Predictive bakery visit's supplier was assigned unrelated work.");
        if(workers[1] is null){v.CurrentWork=null;v.CurrentResources.Clear();}
        if(Region(1)=="upper-right"&&TrafficControlsReady(1)&&!NativeCannonFlight(1))v.Arrived=true;
        if(actual.SequenceEqual(required)&&Held(1)==0&&workers[1] is null)
        {Log("predictiveBakeryKitComplete",PredictiveBakeryStatus());predictiveBakeryVisit=null;}
    }

    private bool ContinuePredictiveBakeryVisit()
    {
        if(predictiveBakeryVisit is not {} v)return false;
        ObservePredictiveBakeryVisit();if(predictiveBakeryVisit is null)return false;
        if(workers[1] is not null||Held(1)!=0)return true;
        if(!v.Arrived||Region(1)!="upper-right"||!TrafficControlsReady(1)||NativeCannonFlight(1))return true;
        if(!Free(v.Bowl,v.Home)||counterSupplies.Values.Any(a=>a.Vessel==v.Bowl))return true;
        var missing=recipes[v.Index].RequiredInputs.Where(i=>!Food(v.Bowl).IngredientIds.Contains(i.Id))
            .OrderBy(i=>i.ChopSeconds>0?1:0).ThenBy(i=>i==CarnivalRecipes.Flour?0:1).FirstOrDefault();
        if(missing is null)return true;
        bool started=missing.ChopSeconds==0?Supply(1,missing,v.Bowl,true):TrySupplyPreparedFlavor(v.Bowl,missing);
        if(started)
        {v.Issued=true;v.ExpectedIngredient=missing.Id;v.CurrentWork=workers[1];v.CurrentResources.UnionWith(workers[1]!.OwnedResources);Log("predictiveBakeryIngredientIssued",PredictiveBakeryStatus());}
        return true;
    }

    private void CancelPredictiveBakeryVisit(string reason)
    {
        if(predictiveBakeryVisit is null)return;
        var report=PredictiveBakeryStatus();report["reason"]=reason;Log("predictiveBakeryVisitCancelled",report);
        // This is a scheduling commitment, never an extra resource owner.
        // Existing ordinary Work and native inputs remain with their owners.
        predictiveBakeryVisit=null;
    }
    private JsonObject PredictiveBakeryStatus()=>predictiveBakeryVisit is not {} v?new():new(){["frame"]=Frame,["bowl"]=v.Bowl,["home"]=v.Home,
        ["index"]=v.Index,["flavor"]=v.Flavor,["started"]=v.Started,["deadline"]=v.Deadline,["arrived"]=v.Arrived,["issued"]=v.Issued,
        ["currentJob"]=v.CurrentWork?.Name,["expectedIngredient"]=v.ExpectedIngredient,["ingredients"]=JsonSerializer.SerializeToNode(v.LastIngredients),["admission"]=v.Evidence.DeepClone()};
}
