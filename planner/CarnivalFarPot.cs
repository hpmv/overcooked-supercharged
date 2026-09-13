using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private bool TryConditionalFarPot(int source,int destination)
    {
        if(!options.ConditionalFarPotThrows||!options.DirectVesselThrows)return false;
        JsonObject guard;
        try{guard=FarPotLane.Describe(state);}catch(InvalidOperationException){return false;}
        if(destination!=FarPotLane.Id(guard,"farPot")||FarPotLane.Invalid(state,guard,false) is not null)return false;
        if(supplyHomes.Count==0)CaptureSupplyTopology(false);
        if(CapturedSupplyHomeGuard(destination) is not {} homeGuard)return false;
        int near=FarPotLane.Id(guard,"nearPot"),board=FarPotLane.Id(guard,"board"),pass=FarPotLane.Id(guard,"fallback");
        int nearHome=I(guard["nearHome"]),farHome=I(guard["farHome"]);
        int[] resources=[source,near,destination,board,pass,nearHome,farHome];
        if(!Free(resources)||counterSupplies.ContainsKey(pass)||counterSupplies.Values.Any(s=>s.Vessel==destination))return false;
        var staging=ThrowStaging(2);staging["dash"]=options.UseDash||options.UseShortDash;staging["shortDash"]=options.UseShortDash;
        var action=new JsonObject { ["type"]="throw",["targetEntityId"]=destination,["farPotLane"]=guard,["nativeSupplyHome"]=homeGuard };
        if(!Start(2,"supply-Frankfurter-conditional-far-pot",[A("take",source),staging,action],resources,()=>
        {
            bool direct=Food(destination).IngredientIds.SequenceEqual(new[]{CarnivalRecipes.Frankfurter.Id})&&Attached(pass)==0;
            bool fallback=EmptyFood(destination)&&Food(Attached(pass)).IngredientIds.SequenceEqual(new[]{CarnivalRecipes.Frankfurter.Id});
            if(Held(2)!=0||!direct&&!fallback)throw new InvalidOperationException("Conditional far-pot supply lacks exact native delivery or its reserved counter handoff.");
            if(direct)counterSupplies.Remove(pass);
            Log("conditionalFarPotComplete",new JsonObject { ["farPot"]=destination,["counter"]=pass,["outcome"]=direct?"native-exact-vessel-throw":"native-prearm-counter-fallback",["frame"]=Frame });
        }))return false;
        // The address is reserved throughout either branch. It becomes usable
        // only after the job releases the counter, and is removed on direct catch.
        counterSupplies.Add(pass,(destination,CarnivalRecipes.Frankfurter.Id));
        Log("conditionalFarPotLease",new JsonObject { ["guard"]=guard.DeepClone(),["resources"]=new JsonArray(resources.Select(i=>(JsonNode?)JsonValue.Create(i)).ToArray()),["frame"]=Frame });
        return true;
    }
}
