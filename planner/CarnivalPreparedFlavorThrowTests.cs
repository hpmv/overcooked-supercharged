using System.IO.Compression;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    public static int PreparedFlavorThrowSelfTest(string chocolateTrace,string raspberryTrace)
    {
        int checks=0;
        void Check(bool condition,string reason){if(!condition)throw new InvalidOperationException("Prepared flavor regression: "+reason);checks++;}
        void Reject(Action action,string reason){bool bad=false;try{action();}catch(InvalidOperationException){bad=true;}Check(bad,reason);}
        CarnivalPlanner Make(JsonObject response,NativeIngredient ingredient,bool enabled=true)
        {
            var p=new CarnivalPlanner(_=>throw new InvalidOperationException("Offline flavor observer attempted game I/O."),null)
                {response=response.DeepClone().AsObject(),options=new(DirectPreparedFlavorThrows:enabled,PantryChopping:true),
                 recipes=Enumerable.Repeat(CarnivalRecipes.GetRecipe(ingredient==CarnivalRecipes.Chocolate?228996:130976),8).ToArray()};
            p.Refresh();p.CaptureSupplyTopology(false);int bowl=p.NearSupplyVessel(1);
            p.bowlAssignments[bowl]=0;p.bowlFlavors[bowl]=ingredient.Id;p.bowlHomes[bowl]=p.AttachmentParent(bowl);
            return p;
        }
        void ObserveResponse(CarnivalPlanner p,JsonObject response)
        {
            p.response=response;p.state=response["state"]!.AsObject();
            p.entities=p.state["entities"]!.AsArray().OfType<JsonObject>().Where(e=>B(e["active"])).ToDictionary(Id);
            p.chefs=p.state["chefs"]!.AsArray().OfType<JsonObject>().OrderBy(c=>I(c["playerId"])).ToArray();
        }
        foreach(var (path,ingredient) in new[]{(chocolateTrace,CarnivalRecipes.Chocolate),(raspberryTrace,CarnivalRecipes.Raspberry)})
        {
            using var file=File.OpenRead(path);using var zip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(zip);
            JsonObject? last=null,admission=null;CarnivalPlanner? p=null;PreparedFlavorDelivery? delivery=null;int frames=0;
            while(reader.ReadLine() is {} line)
            {
                var row=JsonNode.Parse(line)!.AsObject();
                if(row["kind"]?.ToString()=="call")
                {
                    last=row["response"]!.AsObject();
                    if(p is not null){ObserveResponse(p,last);p.ObservePreparedFlavorDeliveries();frames++;}
                }
                else if(row["kind"]?.ToString()=="event"&&row["value"]?["id"]?.ToString()=="T03-chop-and-throw-flavor")
                {
                    if(row["name"]?.ToString()=="jobStart")
                    {
                        admission=last!.DeepClone().AsObject();p=Make(admission,ingredient);
                        Check(p.TrySupplyPreparedFlavor(p.NearSupplyVessel(1),ingredient),"actual native "+ingredient.Name+" supply boundary admits optional exact near-bowl path");
                        delivery=p.preparedFlavorDeliveries.Values.Single();
                        Check(delivery.Work.Actions.Select(a=>a["type"]!.ToString()).SequenceEqual(new[]{"take","place","chop","take","navigate","throw"}),
                            "normal raw pickup, chopping, prepared pickup and native throw action order is retained");
                        Check(delivery.Work.OwnedResources.SetEquals(delivery.Resources)&&delivery.Work.PantrySupply is null,
                            "bowl/home/board/crate stay exclusively owned and shared-chop delegation cannot adopt this job");
                    }
                    else if(row["name"]?.ToString()=="jobComplete")break;
                }
            }
            Check(p is not null&&delivery is not null&&admission is not null&&frames>100,"full authored native flavor job frames were observed");
            var result=p!;var d=delivery!;
            Check(d.Accepted&&d.Raw==127&&d.Prepared==129&&d.PreparedOrdinal==128&&d.PreparedHeld&&d.FlightFrames>0&&d.NativeChopFrames>0,
                "actual native source replacement, P1 hold, flight and exact original-bowl catch all satisfy the production observer");
            Check(result.AssignedDoughIngredients(d.Bowl,0)&&result.Held(1)==0&&result.Attached(d.Board)==0,
                "native final flavor catch has exactly the assigned recipe inputs and empty source/supplier");
            JsonObject saved=result.response.DeepClone().AsObject();
            foreach(string change in new[]{"home","recipe","extraIngredient","wrongChef","ownership","noFlight","sourceNotConsumed",
                "inactiveBoard","inactiveCrate","missingProgress","nanProgress","infiniteProgress","negativeProgress"})
            {
                ObserveResponse(result,saved.DeepClone().AsObject());int oldFlight=d.FlightFrames;
                if(change=="home")result.Entity(d.Home)!["attachedEntityId"]=0;
                if(change=="recipe")result.bowlAssignments[d.Bowl]=1;
                if(change=="extraIngredient")
                {
                    var node=result.Entity(d.Bowl)!["composition"]!.AsObject();
                    while(node["type"]?.ToString()!="MixedCompositeAssembledNode")node=node["children"]![0]!.AsObject();
                    node["children"]!.AsArray().Add(new JsonObject{["type"]="IngredientAssembledNode",["id"]=CarnivalRecipes.Onion.Id});
                }
                if(change=="wrongChef")result.chefs[1]["controlsEnabled"]=false;
                if(change=="ownership")result.reserved.Remove(d.Board);
                if(change=="noFlight")d.FlightFrames=0;
                if(change=="inactiveBoard")result.Entity(d.Board)!["active"]=false;
                if(change=="inactiveCrate")result.Entity(d.Crate)!["active"]=false;
                if(change=="missingProgress")result.Entity(d.Bowl)!["mixingProgress"]=null;
                if(change=="nanProgress")result.Entity(d.Bowl)!["mixingProgress"]=double.NaN;
                if(change=="infiniteProgress")result.Entity(d.Bowl)!["mixingProgress"]=double.PositiveInfinity;
                if(change=="negativeProgress")result.Entity(d.Bowl)!["mixingProgress"]=-1;
                if(change=="sourceNotConsumed")result.entities[d.Prepared]=new JsonObject{["id"]=d.Prepared,["active"]=true,["observedOrdinal"]=d.PreparedOrdinal,
                    ["composition"]=new JsonObject{["type"]="IngredientAssembledNode",["id"]=ingredient.Id}};
                Reject(()=>result.ObservePreparedFlavorDeliveries(),"observer rejects "+change+" mutation");
                result.bowlAssignments[d.Bowl]=0;result.reserved.Add(d.Board);d.FlightFrames=oldFlight;
            }
            ObserveResponse(result,saved);result.ReleaseRemainingWorkResources(d.Work);result.workers[1]=null;d.Work.Complete!();
            Check(result.preparedFlavorDeliveries.Count==0&&d.Resources.All(id=>!result.reserved.Contains(id)),
                "normal completed Work releases exact source resources once after native catch proof");
            var off=Make(admission!,ingredient,false);
            Check(!off.TrySupplyPreparedFlavor(off.NearSupplyVessel(1),ingredient)&&off.workers[1] is null,"default-off preserves existing central flavor handoff");
            var late=Make(admission!,ingredient);late.Entity(late.NearSupplyVessel(1))!["mixingProgress"]=20;
            Check(!late.TrySupplyPreparedFlavor(late.NearSupplyVessel(1),ingredient),"guarded ordinary walk+chop+edge budget rejects an imminent21s deadline");
            var busy=Make(admission!,ingredient);busy.reserved.Add(busy.NearSupplyVessel(1));
            Check(!busy.TrySupplyPreparedFlavor(busy.NearSupplyVessel(1),ingredient),"existing receiver reservation cannot be stolen");
            var far=Make(admission!,ingredient);int near=far.NearSupplyVessel(1),other=far.Stations("bowl").Single(s=>s.EntityId!=near).EntityId;
            far.Entity(other)!["composition"]=far.Entity(near)!["composition"]!.DeepClone();far.bowlAssignments[other]=1;far.bowlFlavors[other]=ingredient.Id;
            Check(!far.TrySupplyPreparedFlavor(other,ingredient),"unproven far-bowl flavor throw remains excluded");
            var partial=Make(admission!,ingredient);partial.Entity(partial.NearSupplyVessel(1))!["composition"]!.AsObject()["children"]=new JsonArray();
            Check(!partial.TrySupplyPreparedFlavor(partial.NearSupplyVessel(1),ingredient),"empty/incomplete raw receiving mixture is outside the proved flavor path");
            var addressed=Make(admission!,ingredient);addressed.counterSupplies[48]=(addressed.NearSupplyVessel(1),CarnivalRecipes.Egg.Id);
            Check(!addressed.TrySupplyPreparedFlavor(addressed.NearSupplyVessel(1),ingredient),"existing addressed ingredient producer cannot race optional flavor delivery");
        }
        return checks;
    }
}
