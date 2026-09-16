using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Native V16 final-placement observations, with explicitly synthetic planner ownership; never advances a game.</summary>
    public static int ImminentTargetSelfTest(JsonObject at1440,JsonObject at1507,JsonObject at1508,JsonObject at1509,JsonObject at1510)
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new InvalidOperationException("Imminent native target regression: "+message);checks++;}
        CarnivalPlanner Make()
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline imminent fixture attempted I/O."),null)
            {response=at1440.DeepClone().AsObject(),options=new(WaitForImminentHead:true),recipes=Enumerable.Repeat(CarnivalRecipes.GetRecipe(296560),96).ToArray()};
            p.recipes[1]=CarnivalRecipes.GetRecipe(125780);
            p.Refresh();p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline imminent fixture attempted I/O."),null);
            // Reconstruct only the original serial final place and its retained
            // exact plate/output lease from the recorded assembly job.
            var action=new JsonObject{["type"]="place",["station"]="49",["player"]=3};
            p.workers[3]=new Work("assemble-meal-2-Hotdog_Ketchup_Mustard",[action],[13,49],null);
            p.reserved.UnionWith([13,49]);p.plating.Add(1);p.assembling.Add(1);
            return p;
        }
        void Apply(CarnivalPlanner p,JsonObject snapshot){p.response=snapshot.DeepClone().AsObject();p.Refresh();}
        var p=Make();string original=p.response.ToJsonString();
        Check(p.TryWaitForImminentHead(),"actual1440 complete FIFO plate admits the original clear-walk bounded wait");
        var wait=p.imminentHead!;
        Check(wait.Start==1440&&wait.Index==1&&wait.Plate==13&&wait.Output==49&&wait.Owner==3,"wait retains exact original identities and start");
        Check(original==p.response.ToJsonString(),"wait admission never changes native observations");
        Apply(p,at1507);
        Check(I(p.chefs[3]["placementTargetId"])==20,"preceding actual frame has another native placement target");
        Check(p.ValidateImminentHead(wait,out var preceding,out _)&&!B(preceding["nativePlacementTargetObserved"]),"frame1507 still uses ordinary reachable-walk evidence, not a broad proximity shortcut");
        Apply(p,at1508);original=p.response.ToJsonString();
        var oldPath=Navigation.ToStation(p.model,p.Position(3),p.Station(49)!,p.TrafficObstacles(3));
        Check(!oldPath.Success,"captured1508 reproduces the conservative pathfinding rejection");
        Check(I(p.chefs[3]["placementTargetId"])==49&&p.Held(3)==13&&p.Attached(49)==0,"native placement target and held/empty attachment evidence are exact at1508");
        Check(p.TryWaitForImminentHead()&&ReferenceEquals(p.imminentHead,wait)&&wait.Start==1440,"native target retains the existing wait without resetting its bound");
        Check(p.ValidateImminentHead(wait,out var target,out _)&&B(target["nativePlacementTargetObserved"])&&I(target["remainingWaitFrames"])==52,"native target evidence replaces only the now-unnecessary path calculation");
        Check(original==p.response.ToJsonString(),"fast path changes no input, transform, recipe or native state");
        Apply(p,at1509);
        Check(p.Held(3)==0&&p.Attached(49)==13&&p.TryWaitForImminentHead(),"native same-plate attachment at1509 remains accepted before ordinary callback");
        Apply(p,at1510);p.mealPlates[1]=13;p.workers[3]=null;p.reserved.ExceptWith([13,49]);p.plating.Remove(1);p.assembling.Remove(1);
        p.ObserveImminentHeadArrival();
        Check(p.imminentHead is null&&p.HasAccessibleHead("lower-right"),"ordinary completed meal callback ends wait for normal collection");
        p.PantryAndService("lower-right");Check(p.workers[2]?.Name=="collect-fifo-1","service chef can start the existing legal collection after completion");
        foreach(string mutation in new[]{"wrong-target","missing-target","held-other","output-occupied","plate-incarnation","output-incarnation","plate-inactive","output-inactive","owner-replaced","plate-lease","output-lease","wrong-food","wrong-recipe","not-final","owner-disabled","bound"})
        {
            var q=Make();Check(q.TryWaitForImminentHead(),"negative fixture starts original wait: "+mutation);Apply(q,at1508);
            switch(mutation)
            {
                case "wrong-target":q.chefs[3]["placementTargetId"]=20;break;
                case "missing-target":q.chefs[3].Remove("placementTargetId");break;
                case "held-other":q.chefs[3]["heldEntityId"]=12;break;
                case "output-occupied":q.Entity(49)!["attachedEntityId"]=12;break;
                case "plate-incarnation":q.Entity(13)!["observedOrdinal"]=999;break;
                case "output-incarnation":q.Entity(49)!["observedOrdinal"]=999;break;
                case "plate-inactive":q.Entity(13)!["active"]=false;q.Refresh();break;
                case "output-inactive":q.Entity(49)!["active"]=false;q.Refresh();break;
                case "owner-replaced":q.workers[3]=new Work("replacement",[],[13,49],null);break;
                case "plate-lease":q.reserved.Remove(13);break;
                case "output-lease":q.reserved.Remove(49);break;
                case "wrong-food":q.Entity(13)!["composition"]=new JsonObject{["type"]="CompositeAssembledNode",["children"]=new JsonArray()};break;
                case "wrong-recipe":q.recipes[1]=CarnivalRecipes.GetRecipe(296560);break;
                case "not-final":q.workers[3]!.Actions.Enqueue(new JsonObject{["type"]="place",["station"]="52"});break;
                case "owner-disabled":q.chefs[3]["controlsEnabled"]=false;break;
                case "bound":q.state["gameplayFrame"]=1560;break;
            }
            Check(!q.TryWaitForImminentHead()&&q.imminentHead is null,"same target cannot override "+mutation);
        }
        return checks;
    }
}
