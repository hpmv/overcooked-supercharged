using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum CentralPreferenceKind { Fire, RawRelay, BunBuffer }
    private sealed record CentralTaskPreference(int Frame,int DeferredPlayer,int PreferredPlayer,CentralPreferenceKind Kind,int Target,int TargetOrdinal,int[] Resources,double CurrentSeconds,double PreferredSeconds);
    private readonly Dictionary<int,CentralTaskPreference> centralTaskPreferences=[];

    private bool NearestCentralAvailable(int player)=>player is 0 or 3&&HeatCookAvailable(player)&&IdleTrafficHelper(player)&&
        !HeatSafetyBlocks(player)&&trafficYield is null;

    private bool VisibleHigherCentralWork(CentralPreferenceKind kind)
    {
        if(kind==CentralPreferenceKind.Fire)return false;
        if(new[]{"left","right"}.Any(side=>
        {
            int cannon=CannonId(side),passenger=side=="left"?2:1;
            return I(Entity(cannon)?["cannonLoadedEntityId"])==I(chefs[passenger]["entityId"])&&
                !B(chefs[passenger]["controlsEnabled"])&&!B(Entity(cannon)?["cannonFlying"])&&
                Entity(cannon)?["cannonState"]?.ToString()=="Load"&&Free(cannon,I(Entity(cannon)?["cannonButtonEntityId"]));
        }))return true;
        int clean=Counter(15.6,-20.4),plate=Attached(clean);
        if(plate!=0&&IsPlate(Entity(plate))&&EmptyFood(plate)&&Free(clean,plate))return true;
        if(AvailablePlates().Length>0&&Pending().Any(p=>CanAllocatePlate(p.Index)&&
            (IsDonut(p.Recipe)?Stations("basket").Any(b=>Free(b.EntityId)&&CarnivalRecipes.MatchRecipe(Entity(b.EntityId),p.Recipe.Id,false).ReadyToDeliver):
                mealFoods.TryGetValue(p.Index,out int food)&&AttachmentParent(food) is int source&&source!=0&&Free(food,source)&&
                CarnivalRecipes.MatchRecipe(Entity(food),p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion)?472326:296560,false).ReadyToDeliver)))return true;
        if(Stations("chop").Any(s=>Free(s.EntityId,Attached(s.EntityId))&&Entity(Attached(s.EntityId)) is {} raw&&KitchenModel.Components(raw).Contains("WorkableItem")))return true;
        if(Stations("pot").Any(p=>Cooked(p.EntityId,CarnivalRecipes.Frankfurter.Id))&&
            ComponentEntities().Any(e=>IsLooseChoppedBun(Id(e))&&Free(Id(e),AttachmentParent(Id(e)))))return true;
        if((options.EarlyOnionHeat||options.ParallelOnionHeat)&&ParallelOnionCandidates().Any()&&
            Stations("chop").Any(s=>Free(s.EntityId,Attached(s.EntityId))&&Chopped(Attached(s.EntityId),CarnivalRecipes.Onion.Id)))return true;
        if(Pending().Any(p=>p.Recipe.RequiredInputs.Contains(CarnivalRecipes.Onion)&&mealFoods.TryGetValue(p.Index,out int food)&&
                Free(food,AttachmentParent(food))&&!Food(food).IngredientIds.Contains(CarnivalRecipes.Onion.Id))&&
            Stations("pan").Any(s=>Free(s.EntityId)&&Cooked(s.EntityId,CarnivalRecipes.Onion.Id)))return true;
        if(Stations("bowl").Any(b=>Free(b.EntityId)&&bowlAssignments.TryGetValue(b.EntityId,out int index)&&index>=0&&index<recipes.Length&&
            AssignedDoughReady(Food(b.EntityId),recipes[index])&&Stations("basket").Any(f=>Free(f.EntityId)&&EmptyFood(f.EntityId))))return true;
        if(bowlFlavors.Any(b=>Free(b.Key)&&!Food(b.Key).IngredientIds.Contains(b.Value)&&
            Stations("chop").Any(s=>Free(s.EntityId)&&Chopped(Attached(s.EntityId),b.Value))))return true;
        // Buffer placement follows raw relays. Do not promise a chef who can
        // already see an earlier addressed transfer at either native handoff.
        if(kind==CentralPreferenceKind.BunBuffer&&counterSupplies.Any(a=>Attached(a.Key)!=0&&Free(a.Key,Attached(a.Key),a.Value.Vessel)))return true;
        return false;
    }

    private bool PreferOtherCentral(CentralPreferenceKind kind,int player,int target,params int[] resources)
    {
        if(!options.NearestCentralTaskPreference||!NearestCentralAvailable(player))return false;
        int other=player==0?3:0;
        if(!NearestCentralAvailable(other)||!Free(resources)||Station(target) is not {} station||
            Entity(target)?["observedOrdinal"] is null||I(Entity(target)?["observedOrdinal"])<0||!B(Entity(target)?["active"])||
            NativeHeatObligations().Any()||VisibleHigherCentralWork(kind))return false;
        // Earlier priorities in this same real dispatch pass get first claim
        // on a proposed helper. These are hints, never native/resource leases.
        if(centralTaskPreferences.TryGetValue(other,out var prior)&&prior.Frame==Frame&&
            (prior.Kind!=kind||prior.Target!=target))return false;
        var current=Navigation.ToStation(model,Position(player),station,TrafficObstacles(player));
        var preferred=Navigation.ToStation(model,Position(other),station,TrafficObstacles(other));
        double speed=N(chefs[player]["runSpeed"])*N(chefs[player]["surfaceSpeedMultiplier"]),otherSpeed=N(chefs[other]["runSpeed"])*N(chefs[other]["surfaceSpeedMultiplier"]);
        if(!current.Success||!preferred.Success||!(speed>0)||!(otherSpeed>0)||!double.IsFinite(speed)||!double.IsFinite(otherSpeed))return false;
        if(kind!=CentralPreferenceKind.Fire)
        {
            if(resources.Length==0||Station(resources[^1]) is not {} destination||
                !Navigation.ToStation(model,preferred.Points[^1],destination,TrafficObstacles(other)).Success)return false;
        }
        double own=current.Length/speed,alternative=preferred.Length/otherSpeed;
        // Strict advantage only. Equal cost never defers, preserving ordinary
        // stable player0-first admission and excluding mutual deferral.
        if(!double.IsFinite(own)||!double.IsFinite(alternative)||alternative+1e-5>=own)return false;
        var choice=new CentralTaskPreference(Frame,player,other,kind,target,I(Entity(target)?["observedOrdinal"]),resources.Distinct().ToArray(),own,alternative);
        centralTaskPreferences[other]=choice;
        Log("nearestCentralTaskDeferred",new JsonObject{["frame"]=Frame,["kind"]=kind.ToString(),["deferredPlayer"]=player,["preferredPlayer"]=other,
            ["target"]=target,["targetOrdinal"]=choice.TargetOrdinal,["resources"]=JsonSerializer.SerializeToNode(choice.Resources),
            ["currentPath"]=JsonSerializer.SerializeToNode(current),["preferredPath"]=JsonSerializer.SerializeToNode(preferred),
            ["currentWalkingSeconds"]=own,["preferredWalkingSeconds"]=alternative,
            ["qualification"]="Read-only preference; no Work transferred and no resource or native state changed"});
        return true;
    }
}
