using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    /// <summary>Captured ready native meal with synthetic attachment/arrival observations; no game requests.</summary>
    public static int ServiceSideHeadSelfTest(JsonObject ready1108)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("Service-side head fixture: "+message);checks++;}
        CarnivalPlanner Make(bool enabled)
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline service fixture attempted I/O."),null)
                {response=ready1108.DeepClone().AsObject(),options=new(ServiceSideHead:enabled),recipes=new[]{158500,125780,224216,228996}.Select(CarnivalRecipes.GetRecipe).ToArray()};
            if(p.response["state"] is null)p.response=new JsonObject{["state"]=p.response};p.Refresh();
            p.runner=new RouteRunner(_=>throw new InvalidOperationException("Offline service fixture attempted I/O."),null);
            p.mealPlates[0]=10;return p;
        }
        var p=Make(true);int old=45,plate=10,headSlot=p.Counter(25.2,-19.2);
        Check(p.Frame==1108&&p.IsHeadMeal(plate)&&p.AttachmentParent(plate)==old,"actual frame1108 contains the completed native FIFO mustard plate on its original UL handoff");
        // Plate is lifted only in this offline observation fixture to ask where
        // the planner would reserve its final placement before assembly starts.
        p.Entity(old)!["attachedEntityId"]=0;
        var baseline=Make(false);baseline.Entity(old)!["attachedEntityId"]=0;
        Check(baseline.MealOutput(plate,0)==old&&p.MealOutput(plate,0)==headSlot,"default behavior and optional head-side selection have distinct intended existing counters");
        Check(p.Station(headSlot)!.Regions.Contains("lower-right")&&p.Station(headSlot)!.Regions.Contains("center"),"selected head slot has measured approaches from both center and service island");
        p.Entity(headSlot)!["attachedEntityId"]=plate;p.Entity(plate)!["position"]=p.Entity(headSlot)!["position"]!.DeepClone();p.Refresh();
        Check(p.HasAccessibleHead("lower-right")&&p.EmptyAttachment(old)&&p.Free(old),"observed service-side placement frees the exact far-pot fallback lane");
        p.serviceVisitStart=-3;p.PantryAndService("upper-left");var board=p.workers[2]!;
        Check(board.Name=="board-empty-for-service-wave"&&p.serviceVisitStart==p.delivered&&p.Held(2)==0,"UL dispatch boards empty for the same FIFO head and starts a new visit count");
        Check(board.Resources.SequenceEqual(new[]{p.CannonId("left")})&&!p.reserved.Contains(plate)&&!p.reserved.Contains(headSlot),"empty boarding owns only its cannon and does not misclaim the staged plate");
        p.ReleaseRemainingWorkResources(board);p.workers[2]=null;
        p.chefs[2]["position"]=new JsonObject{["x"]=26.8,["y"]=.05,["z"]=-20.3};p.Refresh();
        p.PantryAndService("lower-right");var collect=p.workers[2]!;
        Check(collect.Name=="collect-fifo-0"&&collect.Resources.Contains(plate),"ordinary LR arrival collects the unchanged native FIFO plate");
        p.ReleaseRemainingWorkResources(collect);p.workers[2]=null;p.Entity(headSlot)!["attachedEntityId"]=0;p.chefs[2]["heldEntityId"]=plate;
        p.PantryAndService("lower-right");var serve=p.workers[2]!;
        Check(serve.Name=="serve-fifo-0"&&I(serve.Actions.Single()["station"])==p.Single("delivery"),"the collected head follows the existing native delivery action");
        p.ReleaseRemainingWorkResources(serve);p.workers[2]=null;p.chefs[2]["heldEntityId"]=0;p.mealPlates.Clear();p.delivered=3;p.preServiceUsed=true;
        p.PantryAndService("lower-right");var back=p.workers[2]!;
        Check(back.Name=="service-return-to-pantry"&&back.Actions.Single()["type"]?.ToString()=="portal","finished service batch uses the existing legal return portal");
        p.chefs[2]["position"]=new JsonObject{["x"]=12,["y"]=.05,["z"]=-11.55};back.Complete!();
        Check(p.serviceVisitStart==3&&!p.preServiceUsed&&p.preServiceReadyIndex==-1,"normal portal completion resets visit and optional pre-service-stock admission");
        var future=Make(true);future.reserved.Add(future.Counter(25.2,-20.4));future.reserved.Add(future.Counter(25.2,-18));
        Check(future.MealOutput(plate,1)==0&&future.MealOutput(plate,0)==headSlot,"future meals cannot consume the existing guaranteed FIFO head service slot");
        return checks;
    }
}
