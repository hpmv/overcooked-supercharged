using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class PlatedOnion(EarlyOnionLease lease,int player,int food,int source,int plate,int plateSource,int output,
        int sauce,int dispenser,int button,int recipe,int frame,int deadline,JsonObject evidence)
    {
        public readonly EarlyOnionLease Lease=lease;
        public readonly int Player=player,Food=food,Source=source,Plate=plate,PlateSource=plateSource,Output=output,
            Sauce=sauce,Dispenser=dispenser,Button=button,Recipe=recipe,Started=frame,Deadline=deadline;
        public readonly Dictionary<int,int> Identities=[];
        public readonly JsonObject Evidence=evidence;
        public Work Work=null!;
        public long SourceRegistration;
        public int SourceUnityInstance,BaseFrame=-1;
        public bool PickedUp,BaseAcquired,SauceObserved,CookedObserved,Harvested,SourceRemovalObserved;
        public int[] Resources=>new[]{Food,Source,Plate,Output,Dispenser,Button}.Where(id=>id!=0).Distinct().ToArray();
        public int BeforeOnionRecipe=>Sauce==CarnivalRecipes.Mustard.Id?158500:Sauce==CarnivalRecipes.Ketchup.Id?224216:296560;
    }
    private readonly Dictionary<EarlyOnionLease,PlatedOnion> platedOnions=[];

    private static long PlatedSequence(JsonNode? value)=>long.TryParse(value?.ToString(),System.Globalization.NumberStyles.Integer,
        System.Globalization.CultureInfo.InvariantCulture,out long sequence)?sequence:-1;
    private JsonObject? LastPlatedSourceRegistry(int id)=>(state["entityRegistration"]?["events"] as JsonArray)?.OfType<JsonObject>()
        .Where(e=>I(e["entity"]?["entityId"])==id).OrderByDescending(e=>PlatedSequence(e["sequence"])).FirstOrDefault();
    private bool EarlierOrdinaryMealNeedsPlate(int player,int index,int plate)=>AvailablePlates().Length==1&&
        Pending().Where(p=>p.Index<index).Any(p=>TryAssemble(player,exactIndex:p.Index,exactPlate:plate,inspectOnly:true));

    private bool TryPlatedOnionFinish(EarlyOnionLease lease,int player)
    {
        if(!options.PlatedOnionFinish||lease.Phase!=EarlyOnionPhase.Heating||lease.Owner>=0||!HeatCookAvailable(player)||
            !TrafficControlsReady(player)||B(chefs[player]["useSuppressed"])||!Pending().Any(p=>p.Index==lease.Index)||!CanAllocatePlate(lease.Index)||
            !mealFoods.TryGetValue(lease.Index,out int food)||plating.Contains(lease.Index)||
            !CarnivalRecipes.MatchRecipe(Entity(food),296560,false).ReadyToDeliver||IsPlate(Entity(food))||HeldByAnyone(food)||
            !PureOnion(lease.Pan)||Attached(lease.Home)!=lease.Pan||Attached(lease.Counter)!=0)return false;
        var recipe=recipes[lease.Index];
        var sauces=recipe.RequiredInputs.Where(i=>i==CarnivalRecipes.Mustard||i==CarnivalRecipes.Ketchup).ToArray();
        if(!recipe.RequiredInputs.Contains(CarnivalRecipes.Onion)||sauces.Length>1)return false;
        int source=AttachmentParent(food),plate=AvailablePlate(player);
        if(source==0||plate==0||!NativeDirectOnionSource(source)||!Free(source,food)||
            EarlierOrdinaryMealNeedsPlate(player,lease.Index,plate))return false;
        int plateSource=AttachmentParent(plate),output=MealOutput(plate,lease.Index);
        if(plateSource==0||!NativeDirectOnionSource(plateSource)||!Free(plateSource)||
            output==0||Station(output)?.Regions.Contains("lower-right")!=true||!EmptyAttachment(output))return false;
        int sauce=sauces.FirstOrDefault()?.Id??0,dispenser=sauce==0?0:Single("condiment"),button=sauce==0?0:Single("condiment-switch");
        if(dispenser!=0&&(Entity(dispenser)?["switchIndex"] is null||I(Entity(dispenser)?["switchIndex"]) is not (0 or 1)))return false;
        int[] ids=new[]{lease.Pan,lease.Home,lease.Counter,food,source,plate,plateSource,output,dispenser,button}.Where(id=>id!=0).Distinct().ToArray();
        if(ids.Any(id=>Entity(id)?["observedOrdinal"] is null||I(Entity(id)?["observedOrdinal"])<0||!B(Entity(id)?["active"]))||
            new[]{food,source,plate,plateSource,output,dispenser,button}.Where(id=>id!=0).Any(lease.Resources.Contains)||
            !Free(food,source,plate,output,dispenser,button))return false;
        var registry=LastPlatedSourceRegistry(food);
        if(!B(state["entityRegistration"]?["installed"])||N(state["entityRegistration"]?["errorCount"])!=0||
            registry?["kind"]?.ToString()!="register"||registry["entity"]?["observedRegistrationSequence"] is null)return false;
        RequireEarlyOnionIdentity(lease);
        double progress=N(Entity(lease.Pan)?["cookingProgress"]),speed=N(chefs[player]["runSpeed"])*N(chefs[player]["surfaceSpeedMultiplier"]);
        if(Entity(lease.Pan)?["cookingProgress"] is null||!double.IsFinite(progress)||progress is <9 or >=18||
            N(Entity(lease.Pan)?["cookingTime"])!=12||!double.IsFinite(speed)||speed<=0)return false;
        bool switchNeeded=sauce!=0&&I(Entity(dispenser)?["switchIndex"])!=(sauce==CarnivalRecipes.Mustard.Id?0:1);
        var destinations=new List<int>();if(switchNeeded)destinations.Add(button);
        destinations.Add(plate);destinations.Add(source);if(sauce!=0)destinations.Add(dispenser);destinations.Add(lease.Pan);destinations.Add(output);
        var obstacles=TrafficObstacles(player);var position=Position(player);var paths=new JsonArray();double length=0,throughPan=0;
        foreach(int target in destinations)
        {
            if(Station(target) is not {} station)return false;
            var path=Navigation.ToStation(model,position,station,obstacles);if(!path.Success)return false;
            position=path.Points[^1];length+=path.Length;if(target==lease.Pan)throughPan=length;
            paths.Add(new JsonObject{["target"]=target,["path"]=JsonSerializer.SerializeToNode(path)});
        }
        // Preserve full native cook wait in the estimate, even when walking is
        // likely to overlap it. Budget never assumes optional dash acceleration.
        double heat=throughPan/speed+Math.Max(0,12-progress)+3.5,total=heat+(length-throughPan)/speed+1;
        if(!double.IsFinite(total)||heat>=21-progress||total>=14.5)return false;
        var plan=new PlatedOnion(lease,player,food,source,plate,plateSource,output,sauce,dispenser,button,recipe.Id,Frame,
            Frame+(int)Math.Ceiling(total*60)+30,new JsonObject{["paths"]=paths,["pathLength"]=length,["throughPanLength"]=throughPan,
                ["switchNeeded"]=switchNeeded,["nativeProgress"]=progress,["nativeRemainingCookSeconds"]=Math.Max(0,12-progress),
                ["estimatedHeatSeconds"]=heat,["estimatedWholeSeconds"]=total,["nativeGuardRemainingSeconds"]=21-progress});
        foreach(int id in ids)plan.Identities.Add(id,I(Entity(id)?["observedOrdinal"]));
        plan.SourceRegistration=PlatedSequence(registry["entity"]!["observedRegistrationSequence"]);
        plan.SourceUnityInstance=I(registry["entity"]?["unityInstanceId"]);
        if(plan.SourceRegistration<=0)return false;
        var actions=new List<JsonObject>();if(sauce!=0)actions.Add(SauceSwitch(sauces[0]));
        actions.Add(A("take",plate));actions.Add(A("assemble",source));
        if(sauce!=0){var apply=A("apply",dispenser);apply["expectedIngredientId"]=sauce;actions.Add(apply);}
        actions.Add(OnionAction("navigate",lease.Pan));actions.Add(OnionAction("cook",lease.Pan));
        actions.Add(OnionAction("combine",lease.Pan));actions.Add(A("place",output));
        if(!Start(player,"plated-onion-finish-"+(lease.Index+1),actions,plan.Resources,()=>CompletePlatedOnion(plan)))return false;
        plan.Work=workers[player]!;platedOnions.Add(lease,plan);assembling.Add(lease.Index);plating.Add(lease.Index);
        SetEarlyOnionPhase(lease,EarlyOnionPhase.PlatedFinish,player);
        Log("nativePlatedOnionAdmitted",PlatedOnionStatus(plan));return true;
    }

    private void ObservePlatedOnionFinish(EarlyOnionLease lease,bool requireWork=true)
    {
        if(!platedOnions.TryGetValue(lease,out var p)||lease.Phase!=EarlyOnionPhase.PlatedFinish||lease.Owner!=p.Player||
            !EarlyOnionLeases().Any(l=>ReferenceEquals(l,lease))||lease.Index<delivered||recipes[lease.Index].Id!=p.Recipe||
            mealFoods.GetValueOrDefault(lease.Index)!=p.Food||!assembling.Contains(lease.Index)||!plating.Contains(lease.Index)||
            p.Identities.Where(e=>e.Key!=p.Food).Any(e=>!SameObservedEntity(e.Key,e.Value)||!B(Entity(e.Key)?["active"]))||
            !NativeDirectOnionSource(p.Source)||!NativeDirectOnionSource(p.PlateSource)||!IsPlate(Entity(p.Plate)))
            throw new InvalidOperationException("Plated onion finish lost its exact recipe, plate, source, original lease or native identities.");
        RequireEarlyOnionIdentity(lease);
        if(requireWork&&(!ReferenceEquals(workers[p.Player],p.Work)||!p.Work.OwnedResources.SetEquals(p.Resources)||
            p.Resources.Any(id=>!reserved.Contains(id))||workers.OfType<Work>().Any(w=>w!=p.Work&&w.OwnedResources.Overlaps(p.Resources.Concat(lease.Resources)))))
            throw new InvalidOperationException("Plated onion finish lost its exclusive original Work and persistent pan ownership.");
        double progress=N(Entity(lease.Pan)?["cookingProgress"]);
        if(Entity(lease.Pan)?["cookingProgress"] is null||!double.IsFinite(progress)||progress<0||N(Entity(lease.Pan)?["cookingTime"])!=12)
            throw new InvalidOperationException("Plated onion finish lacks finite native cooking observations.");
        if(Frame>p.Deadline||Frame-p.Started>900||progress>=21)
            throw new TimeoutException("Plated onion finish exceeded its fixed transaction or native heat deadline.");
        if(Attached(lease.Home)!=lease.Pan||AttachmentParent(lease.Pan)!=lease.Home||Attached(lease.Counter)!=0||HeldByAnyone(lease.Pan)||
            !B(chefs[p.Player]["controlsEnabled"])||Region(p.Player)!="center"||NativeCannonFlight(p.Player)||
            Food(lease.Pan).IsRuined||Food(p.Plate).IsRuined||new[]{0,1,2,3}.Any(i=>i!=p.Player&&Held(i)==p.Plate)||
            Attached(p.Output)!=0&&Attached(p.Output)!=p.Plate)
            throw new InvalidOperationException("Plated onion finish changed pan attachment, chef, reserved output or native food safety.");
        var rawSource=(state["entities"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(e=>Id(e)==p.Food);
        var registry=LastPlatedSourceRegistry(p.Food);
        if(registry is not null&&(PlatedSequence(registry["entity"]?["observedRegistrationSequence"])!=p.SourceRegistration||
            I(registry["entity"]?["unityInstanceId"])!=p.SourceUnityInstance)||
            rawSource is not null&&I(rawSource["observedOrdinal"])!=p.Identities[p.Food])
            throw new InvalidOperationException("Plated onion finish observed native source ID reuse instead of its pinned food incarnation.");
        bool remove=registry?["kind"]?.ToString()=="remove";
        if(!p.PickedUp)
        {
            if(Held(p.Player)==p.Plate&&EmptyFood(p.Plate)&&Attached(p.PlateSource)!=p.Plate){p.PickedUp=true;Log("nativePlatedOnionPlatePickedUp",PlatedOnionStatus(p));}
            else if(Attached(p.PlateSource)!=p.Plate||HeldByAnyone(p.Plate)||!EmptyFood(p.Plate)||Held(p.Player)!=0)
                throw new InvalidOperationException("Plated onion finish lacks its original empty plate pickup.");
        }
        bool plain=CarnivalRecipes.MatchRecipe(Entity(p.Plate),296560).ReadyToDeliver;
        bool dressed=CarnivalRecipes.MatchRecipe(Entity(p.Plate),p.BeforeOnionRecipe).ReadyToDeliver;
        bool final=CarnivalRecipes.MatchRecipe(Entity(p.Plate),p.Recipe).ReadyToDeliver;
        if(!p.BaseAcquired&&plain)
        {
            if(!p.PickedUp||HeldByAnyone(p.Food)||AttachmentParent(p.Food)!=0||
                !(Held(p.Player)==p.Plate&&Attached(p.Source)==0||Held(p.Player)==0&&Attached(p.Source)==p.Plate)||
                !remove&&(rawSource is null||B(rawSource["active"])))
                throw new InvalidOperationException("Plated onion finish lacks exact native base consumption and same-plate recovery evidence.");
            p.BaseAcquired=true;p.BaseFrame=Frame;Log("nativePlatedOnionBaseAcquired",PlatedOnionStatus(p));
        }
        if(!p.BaseAcquired)
        {
            if(remove||!SameObservedEntity(p.Food,p.Identities[p.Food])||!B(Entity(p.Food)?["active"])||
                !CarnivalRecipes.MatchRecipe(Entity(p.Food),296560,false).ReadyToDeliver||Attached(p.Source)!=p.Food||
                HeldByAnyone(p.Food)||!EmptyFood(p.Plate)||p.PickedUp&&Held(p.Player)!=p.Plate)
                throw new InvalidOperationException("Plated onion finish lost its exact plain base before native plate assembly.");
        }
        else
        {
            // Native B: plate-under composition at GF471, matching source
            // remove receipt at472, and the same plate recovered at474.
            if(remove)p.SourceRemovalObserved=true;
            if(!p.SourceRemovalObserved&&Frame-p.BaseFrame>3||rawSource is not null&&B(rawSource["active"])||
                AttachmentParent(p.Food)!=0||HeldByAnyone(p.Food)||!plain&&!dressed&&!final||
                !(Held(p.Player)==p.Plate&&Attached(p.Source)==0||Held(p.Player)==0&&(Attached(p.Source)==p.Plate||p.Harvested&&Attached(p.Output)==p.Plate)))
                throw new InvalidOperationException("Plated onion finish lost consumed-source receipt or exact plate while recovering/finishing.");
            if(dressed&&!p.SauceObserved)
            {
                if(p.Sauce!=0&&(Held(p.Player)!=p.Plate||I(Entity(p.Dispenser)?["switchIndex"])!=(p.Sauce==CarnivalRecipes.Mustard.Id?0:1)))
                    throw new InvalidOperationException("Plated onion condiment lacks exact held-plate and native dispenser selection.");
                p.SauceObserved=true;Log("nativePlatedOnionSauceObserved",PlatedOnionStatus(p));
            }
            if(p.SauceObserved&&!dressed&&!final)throw new InvalidOperationException("Plated onion lost its already observed exact condiment contents.");
        }
        if(!EmptyFood(lease.Pan))
        {
            if(p.Harvested||!PureOnion(lease.Pan)||final)throw new InvalidOperationException("Plated onion changed its original onion batch before native consumption.");
            if(NativeOnionCooked(lease.Pan)&&!p.CookedObserved){p.CookedObserved=true;Log("nativePlatedOnionCooked",PlatedOnionStatus(p));}
        }
        else if(!p.Harvested)
        {
            if(!p.BaseAcquired||!p.SourceRemovalObserved||!p.SauceObserved||!p.CookedObserved||Held(p.Player)!=p.Plate||!final||progress!=0)
                throw new InvalidOperationException("Plated onion disappearance lacks prior native cooking and exact dressed-plate transfer.");
            p.Harvested=true;Log("nativePlatedOnionConsumed",PlatedOnionStatus(p));
        }
        else if(!final||progress!=0)throw new InvalidOperationException("Plated onion final plate or original emptied pan changed before output.");
    }
    private void CompletePlatedOnion(PlatedOnion p)
    {
        ObservePlatedOnionFinish(p.Lease,false);
        if(!p.Harvested||!p.SourceRemovalObserved||Held(p.Player)!=0||Attached(p.Output)!=p.Plate||Attached(p.Source)!=0||
            !EmptyFood(p.Lease.Pan)||N(Entity(p.Lease.Pan)?["cookingProgress"])!=0||Attached(p.Lease.Home)!=p.Lease.Pan||
            !CarnivalRecipes.MatchRecipe(Entity(p.Plate),p.Recipe).ReadyToDeliver)
            throw new InvalidOperationException("Plated onion completion lacks its same native meal at the selected output and empty original home pan.");
        mealPlates[p.Lease.Index]=p.Plate;mealFoods.Remove(p.Lease.Index);assembling.Remove(p.Lease.Index);plating.Remove(p.Lease.Index);
        platedOnions.Remove(p.Lease);foreach(int id in p.Lease.Resources)reserved.Remove(id);
        if(ReferenceEquals(earlyOnion,p.Lease))earlyOnion=null;
        else if(ReferenceEquals(secondEarlyOnion,p.Lease))secondEarlyOnion=null;
        else throw new InvalidOperationException("Plated onion completed an unregistered persistent lease.");
        Log("nativePlatedOnionComplete",PlatedOnionStatus(p));
    }
    private JsonObject PlatedOnionStatus(PlatedOnion p)=>new(){["frame"]=Frame,["player"]=p.Player,["mealIndex"]=p.Lease.Index,["recipeId"]=p.Recipe,
        ["pan"]=p.Lease.Pan,["home"]=p.Lease.Home,["unusedParkingCounter"]=p.Lease.Counter,["food"]=p.Food,["source"]=p.Source,
        ["plate"]=p.Plate,["plateSource"]=p.PlateSource,["output"]=p.Output,["sourceRegistration"]=p.SourceRegistration,
        ["sourceUnityInstance"]=p.SourceUnityInstance,["identities"]=JsonSerializer.SerializeToNode(p.Identities),["pickedUp"]=p.PickedUp,
        ["baseAcquired"]=p.BaseAcquired,["sourceRemovalObserved"]=p.SourceRemovalObserved,["sauceObserved"]=p.SauceObserved,
        ["cookedObserved"]=p.CookedObserved,["harvested"]=p.Harvested,["startedFrame"]=p.Started,["deadlineFrame"]=p.Deadline,
        ["admissionEvidence"]=p.Evidence.DeepClone()};
}
