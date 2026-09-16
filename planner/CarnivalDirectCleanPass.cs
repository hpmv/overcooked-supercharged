using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class DirectCleanPass(int player,int index,int recipeId,int plate,int source,int output,int plateOrdinal,int sourceOrdinal,Point2 sourcePosition,Work work)
    {
        public readonly int Player=player,Index=index,RecipeId=recipeId,Plate=plate,Source=source,Output=output,PlateOrdinal=plateOrdinal,SourceOrdinal=sourceOrdinal;
        public readonly Point2 SourcePosition=sourcePosition;
        public readonly Work Work=work;
        public bool PickedUp;
    }
    private readonly Dictionary<Work,DirectCleanPass> directCleanPasses=[];

    private bool TryDirectCleanPassAssembly(int player)
    {
        if(!options.DirectCleanPassAssembly||!HeatCookAvailable(player)||!TrafficControlsReady(player)||HeatSafetyBlocks(player))return false;
        int source=Counter(15.6,-20.4),plate=Attached(source);
        if(plate==0||!Free(source,plate)||!EmptyFood(plate)||!IsPlate(Entity(plate))||HeldByAnyone(plate)||
            Entity(plate)?["observedOrdinal"] is null||Entity(source)?["observedOrdinal"] is null||
            I(Entity(plate)?["observedOrdinal"])<0||I(Entity(source)?["observedOrdinal"])<0||
            !AvailablePlates().Any(e=>Id(e)==plate))return false;
        foreach(var (index,recipe) in Pending())
        {
            // Keep the first option to the existing serial zero/one-sauce path.
            // Cooperative two-sauce ownership remains in its current scheduler.
            if(recipe.RequiredInputs.Count(i=>i==CarnivalRecipes.Mustard||i==CarnivalRecipes.Ketchup)>1)
            {
                // V17 GF14995 skipped a ready earlier Both meal and spent the
                // only clean plate on a later donut. Preserve ordinary pending
                // order whenever that same plate admits the earlier meal;
                // an unready/leased/output-blocked earlier meal may be skipped.
                if(AvailablePlates().Length==1&&TryAssemble(player,exactIndex:index,exactPlate:plate,inspectTwoSauceOnly:true))return false;
                continue;
            }
            if(!TryAssemble(player,exactIndex:index,exactPlate:plate))continue;
            var work=workers[player]??throw new InvalidOperationException("Direct clean-pass assembly did not create its ordinary serial Work.");
            int output=I(work.Actions.Last()["station"]);
            var selected=new DirectCleanPass(player,index,recipe.Id,plate,source,output,I(Entity(plate)?["observedOrdinal"]),
                I(Entity(source)?["observedOrdinal"]),Station(source)!.Position,work);
            directCleanPasses.Add(work,selected);
            Log("directCleanPassAssemblyStarted",DirectCleanPassStatus(selected));return true;
        }
        return false;
    }

    private void RequireDirectCleanPass(DirectCleanPass selected,bool requireWork)
    {
        if(!SameObservedEntity(selected.Plate,selected.PlateOrdinal)||!IsPlate(Entity(selected.Plate))||
            !SameObservedEntity(selected.Source,selected.SourceOrdinal)||Station(selected.Source) is not {} source||
            source.Position.Distance(selected.SourcePosition)>.05||selected.Index<delivered||selected.Index>=recipes.Length||
            recipes[selected.Index].Id!=selected.RecipeId)
            throw new InvalidOperationException("Direct clean-pass assembly changed its exact plate/source identity or recipe index.");
        if(requireWork&&(!ReferenceEquals(workers[selected.Player],selected.Work)&&
                !cannonInterruptions.Values.Any(i=>i.Player==selected.Player&&ReferenceEquals(i.Original,selected.Work))||
            !selected.Work.OwnedResources.Contains(selected.Plate)||!reserved.Contains(selected.Plate)||
            workers.OfType<Work>().Any(w=>!ReferenceEquals(w,selected.Work)&&w.OwnedResources.Contains(selected.Plate))))
            throw new InvalidOperationException("Direct clean-pass assembly lost its original ordinary plate owner.");
        if(!selected.PickedUp)
        {
            if(Held(selected.Player)==selected.Plate&&Attached(selected.Source)==0&&EmptyFood(selected.Plate))
            {
                selected.PickedUp=true;Log("directCleanPassPlatePickedUp",DirectCleanPassStatus(selected));
            }
            else if(Attached(selected.Source)!=selected.Plate||HeldByAnyone(selected.Plate)||!EmptyFood(selected.Plate))
                throw new InvalidOperationException("Exact clean plate left its native handoff before the selected chef picked it up.");
        }
        // The plate lease and native occupied-surface gate protect pickup.
        // Its now-empty source may immediately receive another washer plate;
        // it is intentionally not locked throughout a condiment/output trip.
    }
    private void ObserveDirectCleanPasses()
    {
        foreach(var selected in directCleanPasses.Values)RequireDirectCleanPass(selected,true);
    }
    private void CompleteDirectCleanPass(int player,int plate,int recipe)
    {
        var selected=directCleanPasses.Values.SingleOrDefault(s=>s.Player==player&&s.Plate==plate);
        if(selected is null)return;
        RequireDirectCleanPass(selected,false);
        if(recipe!=selected.RecipeId||!selected.PickedUp||Held(player)!=0||Attached(selected.Output)!=plate||
            !CarnivalRecipes.MatchRecipe(Entity(plate),recipe).ReadyToDeliver)
            throw new InvalidOperationException("Direct clean-pass assembly lacks its same native prepared plate at the original output.");
        directCleanPasses.Remove(selected.Work);Log("directCleanPassAssemblyComplete",DirectCleanPassStatus(selected));
    }
    private JsonObject DirectCleanPassStatus(DirectCleanPass s)=>new(){["frame"]=Frame,["player"]=s.Player,["index"]=s.Index,
        ["recipeId"]=s.RecipeId,["plate"]=s.Plate,["plateOrdinal"]=s.PlateOrdinal,["source"]=s.Source,["sourceOrdinal"]=s.SourceOrdinal,["output"]=s.Output,
        ["pickedUp"]=s.PickedUp,["job"]=s.Work.Name,["qualification"]="Optional ordinary exact-plate assembly; no storage stop or claimed timing gain"};
}
