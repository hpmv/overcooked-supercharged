using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

internal static class FarPotLane
{
    internal static JsonObject Describe(JsonObject snapshot)
    {
        var model=KitchenModel.Build(snapshot);var state=KitchenModel.SnapshotState(snapshot);
        int At(string role,double x,double z)=>model.Stations.Single(s=>s.Role==role&&s.Position.Distance(new(x,z))<.05).EntityId;
        int near=At("pot",16.8,-10.8),far=At("pot",18,-10.8),board=At("chop",15.6,-12),pass=At("counter",15.6,-13.2);
        var entities=state["entities"]!.AsArray().OfType<JsonObject>().ToArray();
        int Home(int id)=>entities.Single(e=>e["attachedEntityId"]?.GetValue<int>()==id)["id"]!.GetValue<int>();
        JsonObject Identity(int id)=>new(){["id"]=id,["ordinal"]=entities.Single(e=>e["id"]!.GetValue<int>()==id)["observedOrdinal"]?.DeepClone()};
        int nearHome=Home(near),farHome=Home(far);
        return new JsonObject { ["nearPot"]=Identity(near),["farPot"]=Identity(far),["board"]=Identity(board),["fallback"]=Identity(pass),
            ["nearHome"]=nearHome,["farHome"]=farHome,["nearHomeOrdinal"]=Identity(nearHome)["ordinal"]?.DeepClone(),
            ["farHomeOrdinal"]=Identity(farHome)["ordinal"]?.DeepClone(),["evidence"]="empty-lane-far-pot-a: one native mechanism probe" };
    }
    internal static int Id(JsonObject guard,string key)=>guard[key]?["id"]?.GetValue<int>()??0;
    internal static string? Invalid(JsonObject snapshot,JsonObject guard,bool requireStaging)
    {
        try { return InvalidCore(snapshot,guard,requireStaging); }
        catch(Exception error) when(error is InvalidOperationException or ArgumentException or KeyNotFoundException or NullReferenceException)
        { return "missing or ambiguous lane telemetry: "+error.Message; }
    }
    internal static string? FallbackInvalid(JsonObject snapshot,JsonObject guard)
    {
        var state=KitchenModel.SnapshotState(snapshot);
        var counter=state["entities"]?.AsArray().OfType<JsonObject>().SingleOrDefault(e=>e["id"]?.GetValue<int>()==Id(guard,"fallback"));
        if(counter is null||counter["active"]?.GetValue<bool>()!=true||counter["attachedEntityId"]?.GetValue<int>()!=0||counter["observedOrdinal"] is null||
            counter["observedOrdinal"]?.ToJsonString()!=guard["fallback"]?["ordinal"]?.ToJsonString()||
            KitchenModel.Position(counter["position"]).Distance(new(15.6,-13.2))>.05)return "reserved counter identity, position or empty attachment changed";
        return null;
    }
    private static string? InvalidCore(JsonObject snapshot,JsonObject guard,bool requireStaging)
    {
        var state=KitchenModel.SnapshotState(snapshot);
        if(state["scene"]?.ToString()!="s_Day_3_4")return "unvalidated scene";
        var model=KitchenModel.Build(state);
        var entities=state["entities"]!.AsArray().OfType<JsonObject>().ToArray();
        JsonObject? Entity(int id)=>entities.SingleOrDefault(e=>e["id"]?.GetValue<int>()==id);
        foreach(var expected in new[]{("nearPot","pot",16.8,-10.8),("farPot","pot",18d,-10.8),("board","chop",15.6,-12d),("fallback","counter",15.6,-13.2)})
        {
            int id=Id(guard,expected.Item1);var e=Entity(id);var station=model.Stations.SingleOrDefault(s=>s.EntityId==id);
            if(e is null||e["active"]?.GetValue<bool>()!=true||station?.Role!=expected.Item2||station.Position.Distance(new(expected.Item3,expected.Item4))>.05)
                return "unvalidated "+expected.Item1+" identity or geometry";
            if(guard[expected.Item1]?["ordinal"] is not JsonValue ordinal||e["observedOrdinal"] is null||
                ordinal.GetValue<int>()!=e["observedOrdinal"]!.GetValue<int>())return "changed or unknown "+expected.Item1+" incarnation";
        }
        int near=Id(guard,"nearPot"),far=Id(guard,"farPot");
        foreach(var pair in new[]{(near,"nearHome",16.8),(far,"farHome",18d)})
        {
            var home=Entity(guard[pair.Item2]?.GetValue<int>()??0);
            if(home?["attachedEntityId"]?.GetValue<int>()!=pair.Item1 || home["active"]?.GetValue<bool>()!=true ||
                !KitchenModel.Components(home).Contains("CookingStation") || home["observedOrdinal"] is null || guard[pair.Item2+"Ordinal"] is null ||
                home["observedOrdinal"]!.ToJsonString()!=guard[pair.Item2+"Ordinal"]!.ToJsonString() ||
                KitchenModel.Position(home["position"]).Distance(new(pair.Item3,-10.8))>.05)
                return "pot detached from its original native stove or stove identity/geometry changed";
        }
        var chefs=state["chefs"]!.AsArray().OfType<JsonObject>().ToArray();
        if(chefs.Any(c=>new[]{near,far}.Contains(c["heldEntityId"]?.GetValue<int>()??0)))return "a receiving pot is held";
        var nearFood=CarnivalRecipes.ClassifyEntity(Entity(near)).Food;
        if(nearFood.IsRuined||!nearFood.IngredientIds.SequenceEqual(new[]{CarnivalRecipes.Frankfurter.Id}))return "near pot is not occupied by exactly one native sausage";
        if(Entity(far)?["contents"] is not JsonArray farContents||farContents.Count!=0||CarnivalRecipes.ClassifyEntity(Entity(far)).Food.IngredientIds.Length!=0)
            return "far pot is not observably empty";
        if(Entity(Id(guard,"board"))?["attachedEntityId"]?.GetValue<int>()!=0)return "onion board is occupied or unknown";
        if(Entity(Id(guard,"fallback"))?["attachedEntityId"]?.GetValue<int>()!=0)return "reserved fallback counter is occupied or unknown";
        if(requireStaging)
        {
            var chef=chefs.Single(c=>c["playerId"]?.GetValue<int>()==2);
            if(KitchenModel.Position(chef["position"]).Distance(new(13.6,-12.2))>.16||
                Math.Abs(KitchenModel.N(chef["position"]?["y"])-.05)>.15)return "thrower is outside the demonstrated staging area";
            if(Math.Abs(KitchenModel.N(chef["throwForce"])-18)>.001||Math.Abs(KitchenModel.N(chef["throwInclination"])-12)>.001)
                return "throw profile differs from the demonstrated native force and inclination";
            var held=Entity(chef["heldEntityId"]?.GetValue<int>()??0);
            if(!CarnivalRecipes.ClassifyEntity(held).Food.IngredientIds.SequenceEqual(new[]{CarnivalRecipes.Frankfurter.Id}))return "held ingredient is not exactly one native sausage";
        }
        return null;
    }
}
