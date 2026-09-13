using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private sealed class PlatedBase(int player,int index,int recipe,int board,int bun,int plate,int plateSource,int pot,int home,
        int sauce,int dispenser,int button,int output,bool buffer,int frame,int deadline,JsonObject evidence)
    {
        public readonly int Player=player,Index=index,Recipe=recipe,Board=board,Bun=bun,Plate=plate,PlateSource=plateSource,
            Pot=pot,Home=home,Sauce=sauce,Dispenser=dispenser,Button=button,Output=output,Started=frame,Deadline=deadline;
        public readonly bool UsesBuffer=buffer;
        public readonly JsonObject Evidence=evidence;
        public readonly Dictionary<int,int> Identities=[];
        public long BunRegistration;
        public int BunUnityInstance,BunFrame=-1;
        public bool PickedUp,BunAcquired,RemovalObserved,CookedObserved,Harvested,SauceObserved;
        public Work Work=null!;
        public int[] Resources=>new[]{Board,Bun,Plate,Pot,Home,Dispenser,Button,Output}.Where(id=>id!=0).Distinct().ToArray();
    }
    private readonly Dictionary<Work,PlatedBase> platedBases=[];
    private bool NativeBunOnlyPlate(int plate)=>IsPlate(Entity(plate))&&CarnivalRecipes.ClassifyEntity(Entity(plate)).EvidenceGaps.Length==0&&IsLooseChoppedBun(plate);
    private bool OriginalPlatedBasePot(int pot,int home)=>potHomes.TryGetValue(pot,out var original)&&original.Home==home&&
        SameObservedEntity(pot,original.PotOrdinal)&&SameObservedEntity(home,original.HomeOrdinal)&&
        B(Entity(pot)?["active"])&&B(Entity(home)?["active"])&&NativeCookingStation(Entity(home))&&
        Attached(home)==pot&&AttachmentParent(pot)==home&&!HeldByAnyone(pot)&&!potRescues.ContainsKey(pot)&&
        N(Entity(pot)?["cookingTime"])==12&&I(Entity(pot)?["cookingTypeId"])==CarnivalRecipes.PotCookingStepId;

    private bool TryPlatedHotdogBase(int player,int index,int board,int bun,int pot,bool usesBuffer,bool exactNearReady)
    {
        // Called only within the existing selected BuildUnplatedHotdog tuple;
        // this helper never searches a later recipe or substitutes another pot.
        if(!options.PlatedHotdogBase||!HeatCookAvailable(player)||!TrafficControlsReady(player)||B(chefs[player]["useSuppressed"])||
            !Pending().Any(p=>p.Index==index)||mealFoods.ContainsKey(index)||plating.Contains(index)||!CanAllocatePlate(index)||
            IsDonut(recipes[index])||recipes[index].RequiredInputs.Contains(CarnivalRecipes.Onion)||
            !NearReadyPotSource(board,bun)||!potHomes.TryGetValue(pot,out var native)||!OriginalPlatedBasePot(pot,native.Home)||
            !(NativeSausagePot(pot,true)||exactNearReady&&options.NearReadyPotHarvest&&NativeNearReadyPot(pot)))return false;
        var recipe=recipes[index];var sauces=recipe.RequiredInputs.Where(i=>i==CarnivalRecipes.Mustard||i==CarnivalRecipes.Ketchup).ToArray();
        if(sauces.Length>1||usesBuffer&&(bufferedBun!=bun||bunBufferCounter!=board))return false;
        int plate=AvailablePlate(player),home=native.Home;
        if(plate==0||EarlierOrdinaryMealNeedsPlate(player,index,plate))return false;
        int plateSource=AttachmentParent(plate),output=MealOutput(plate,index),sauce=sauces.FirstOrDefault()?.Id??0;
        if(plateSource==0||!NativeDirectOnionSource(plateSource)||!Free(plateSource)||output==0||
            Station(output)?.Regions.Contains("lower-right")!=true||!EmptyAttachment(output))return false;
        int dispenser=sauce==0?0:Single("condiment"),button=sauce==0?0:Single("condiment-switch");
        if(dispenser!=0&&(Entity(dispenser)?["switchIndex"] is null||I(Entity(dispenser)?["switchIndex"]) is not (0 or 1)))return false;
        int[] ids=new[]{board,bun,plate,plateSource,pot,home,dispenser,button,output}.Where(id=>id!=0).Distinct().ToArray();
        if(!Free(ids)||ids.Any(id=>Entity(id)?["observedOrdinal"] is null||I(Entity(id)?["observedOrdinal"])<0||!B(Entity(id)?["active"])))return false;
        var registry=LastPlatedSourceRegistry(bun);
        if(!B(state["entityRegistration"]?["installed"])||N(state["entityRegistration"]?["errorCount"])!=0||registry?["kind"]?.ToString()!="register"||
            PlatedSequence(registry["entity"]?["observedRegistrationSequence"])<=0)return false;
        double progress=N(Entity(pot)?["cookingProgress"]),speed=N(chefs[player]["runSpeed"])*N(chefs[player]["surfaceSpeedMultiplier"]);
        if(Entity(pot)?["cookingProgress"] is null||!double.IsFinite(progress)||progress<0||progress>=23||!double.IsFinite(speed)||speed<=0)return false;
        bool switchNeeded=sauce!=0&&I(Entity(dispenser)?["switchIndex"])!=(sauce==CarnivalRecipes.Mustard.Id?0:1);
        var destinations=new List<int>();if(switchNeeded)destinations.Add(button);destinations.Add(plate);destinations.Add(board);destinations.Add(pot);
        if(sauce!=0)destinations.Add(dispenser);destinations.Add(output);
        var obstacles=TrafficObstacles(player);var position=Position(player);var paths=new JsonArray();double length=0,throughPot=0;
        foreach(int target in destinations)
        {
            if(Station(target) is not {} station)return false;var path=Navigation.ToStation(model,position,station,obstacles);if(!path.Success)return false;
            position=path.Points[^1];length+=path.Length;if(target==pot)throughPot=length;
            paths.Add(new JsonObject{["target"]=target,["path"]=JsonSerializer.SerializeToNode(path)});
        }
        double heat=throughPot/speed+Math.Max(0,12-progress)+3.5,total=heat+(length-throughPot)/speed+(sauce==0?1:2);
        if(!double.IsFinite(total)||heat>=23-progress||total>=14.5)return false;
        var plan=new PlatedBase(player,index,recipe.Id,board,bun,plate,plateSource,pot,home,sauce,dispenser,button,output,usesBuffer,Frame,
            Frame+(int)Math.Ceiling(total*60)+30,new JsonObject{["paths"]=paths,["pathLength"]=length,["throughPotLength"]=throughPot,
                ["switchNeeded"]=switchNeeded,["nativeProgress"]=progress,["exactNearReadyRequested"]=exactNearReady,["remainingNativeCookSeconds"]=Math.Max(0,12-progress),
                ["estimatedHeatSeconds"]=heat,["estimatedWholeSeconds"]=total,["guardRemainingSeconds"]=23-progress});
        foreach(int id in ids)plan.Identities.Add(id,I(Entity(id)?["observedOrdinal"]));
        plan.BunRegistration=PlatedSequence(registry["entity"]?["observedRegistrationSequence"]);plan.BunUnityInstance=I(registry["entity"]?["unityInstanceId"]);
        var actions=new List<JsonObject>();if(sauce!=0)actions.Add(SauceSwitch(sauces[0]));
        actions.Add(A("take",plate));actions.Add(A("assemble",board));actions.Add(A("navigate",pot));actions.Add(PotAction("cook",pot));actions.Add(A("combine",pot));
        if(sauce!=0){var apply=A("apply",dispenser);apply["expectedIngredientId"]=sauce;actions.Add(apply);}actions.Add(A("place",output));
        if(!Start(player,"plated-hotdog-base-"+(index+1),actions,plan.Resources,()=>CompletePlatedBase(plan)))return false;
        plan.Work=workers[player]!;platedBases.Add(plan.Work,plan);assembling.Add(index);plating.Add(index);
        Log("nativePlatedBaseAdmitted",PlatedBaseStatus(plan));return true;
    }

    private void ObservePlatedBase(PlatedBase p,bool requireWork=true)
    {
        if(p.Index<delivered||recipes[p.Index].Id!=p.Recipe||mealFoods.ContainsKey(p.Index)||mealPlates.ContainsKey(p.Index)||
            !assembling.Contains(p.Index)||!plating.Contains(p.Index)||!platedBases.TryGetValue(p.Work,out var same)||!ReferenceEquals(same,p)||
            p.Identities.Where(e=>e.Key!=p.Bun).Any(e=>!SameObservedEntity(e.Key,e.Value)||!B(Entity(e.Key)?["active"]))||
            !NativeDirectOnionSource(p.Board)||!NativeDirectOnionSource(p.PlateSource)||!IsPlate(Entity(p.Plate))||
            !OriginalPlatedBasePot(p.Pot,p.Home)||!B(chefs[p.Player]["controlsEnabled"])||Region(p.Player)!="center"||NativeCannonFlight(p.Player))
            throw new InvalidOperationException("Plate-first hotdog changed its exact native source, pot, plate, recipe or chef.");
        if(requireWork&&(!ReferenceEquals(workers[p.Player],p.Work)||!p.Work.OwnedResources.SetEquals(p.Resources)||
            p.Resources.Any(id=>!reserved.Contains(id))||workers.OfType<Work>().Any(w=>w!=p.Work&&w.OwnedResources.Overlaps(p.Resources))))
            throw new InvalidOperationException("Plate-first hotdog lost its exclusive original Work and exact resources.");
        double progress=N(Entity(p.Pot)?["cookingProgress"]);
        if(Entity(p.Pot)?["cookingProgress"] is null||!double.IsFinite(progress)||progress<0||Food(p.Pot).IsRuined||Food(p.Plate).IsRuined)
            throw new InvalidOperationException("Plate-first hotdog lost valid native cooking/food observations.");
        if(Frame>p.Deadline||Frame-p.Started>900||progress>=23)throw new TimeoutException("Plate-first hotdog exceeded its fixed native heat/transaction deadline.");
        if(Attached(p.Output)!=0&&Attached(p.Output)!=p.Plate||new[]{0,1,2,3}.Any(i=>i!=p.Player&&Held(i)==p.Plate)||
            p.UsesBuffer&&(bufferedBun!=p.Bun||bunBufferCounter!=p.Board))
            throw new InvalidOperationException("Plate-first hotdog lost its selected output, plate owner or original bun buffer.");
        var raw=(state["entities"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault(e=>Id(e)==p.Bun);var registry=LastPlatedSourceRegistry(p.Bun);
        if(registry is not null&&(PlatedSequence(registry["entity"]?["observedRegistrationSequence"])!=p.BunRegistration||I(registry["entity"]?["unityInstanceId"])!=p.BunUnityInstance)||
            raw is not null&&I(raw["observedOrdinal"])!=p.Identities[p.Bun])throw new InvalidOperationException("Plate-first hotdog observed bun ID reuse instead of its original ingredient.");
        bool removed=registry?["kind"]?.ToString()=="remove",bunOnly=NativeBunOnlyPlate(p.Plate),plain=CarnivalRecipes.MatchRecipe(Entity(p.Plate),296560).ReadyToDeliver,
            final=CarnivalRecipes.MatchRecipe(Entity(p.Plate),p.Recipe).ReadyToDeliver;
        if(!p.PickedUp)
        {
            if(Held(p.Player)==p.Plate&&EmptyFood(p.Plate)&&Attached(p.PlateSource)!=p.Plate){p.PickedUp=true;Log("nativePlatedBasePlatePickedUp",PlatedBaseStatus(p));}
            else if(Held(p.Player)!=0||Attached(p.PlateSource)!=p.Plate||HeldByAnyone(p.Plate)||!EmptyFood(p.Plate))
                throw new InvalidOperationException("Plate-first hotdog lacks its exact clean plate pickup.");
        }
        if(!p.BunAcquired&&bunOnly)
        {
            if(!p.PickedUp||HeldByAnyone(p.Bun)||AttachmentParent(p.Bun)!=0||
                !(Held(p.Player)==p.Plate&&Attached(p.Board)==0||Held(p.Player)==0&&Attached(p.Board)==p.Plate)||
                !removed&&(raw is null||B(raw["active"])))throw new InvalidOperationException("Plate-first hotdog lacks native chopped-bun consumption and same-plate recovery.");
            p.BunAcquired=true;p.BunFrame=Frame;Log("nativePlatedBaseBunAcquired",PlatedBaseStatus(p));
        }
        if(!p.BunAcquired)
        {
            if(removed||!SameObservedEntity(p.Bun,p.Identities[p.Bun])||!B(Entity(p.Bun)?["active"])||!IsLooseChoppedBun(p.Bun)||
                Attached(p.Board)!=p.Bun||HeldByAnyone(p.Bun)||!EmptyFood(p.Plate)||p.PickedUp&&Held(p.Player)!=p.Plate)
                throw new InvalidOperationException("Plate-first hotdog changed its prepared bun before native assembly.");
        }
        else
        {
            if(removed)p.RemovalObserved=true;
            if(!p.RemovalObserved&&Frame-p.BunFrame>3||raw is not null&&B(raw["active"])||AttachmentParent(p.Bun)!=0||HeldByAnyone(p.Bun)||
                !(Held(p.Player)==p.Plate&&Attached(p.Board)==0||Held(p.Player)==0&&(Attached(p.Board)==p.Plate||p.Harvested&&Attached(p.Output)==p.Plate)))
                throw new InvalidOperationException("Plate-first hotdog lost exact consumed-bun receipt or plate-under recovery.");
        }
        if(!EmptyFood(p.Pot))
        {
            if(p.Harvested||!NativeSausagePot(p.Pot)||p.BunAcquired&&!bunOnly)
                throw new InvalidOperationException("Plate-first hotdog changed the exact sausage or bun-only plate before native transfer.");
            if(NativeSausagePot(p.Pot,true)&&!p.CookedObserved){p.CookedObserved=true;Log("nativePlatedBaseCooked",PlatedBaseStatus(p));}
        }
        else if(!p.Harvested)
        {
            if(!p.BunAcquired||!p.RemovalObserved||!p.CookedObserved||Held(p.Player)!=p.Plate||!plain||!NativeEmptyPot(p.Pot))
                throw new InvalidOperationException("Plate-first hotdog lacks prior Cooked sausage entering its exact held bun-only plate.");
            p.Harvested=true;Log("nativePlatedBaseSausageConsumed",PlatedBaseStatus(p));
        }
        else if(!NativeEmptyPot(p.Pot)||!plain&&!final)
            throw new InvalidOperationException("Plate-first hotdog refilled its reserved pot or lost its finished native base.");
        if(p.Harvested&&final&&!p.SauceObserved)
        {
            if(p.Sauce!=0&&(Held(p.Player)!=p.Plate||I(Entity(p.Dispenser)?["switchIndex"])!=(p.Sauce==CarnivalRecipes.Mustard.Id?0:1)))
                throw new InvalidOperationException("Plate-first hotdog condiment lacks exact held-plate and dispenser evidence.");
            p.SauceObserved=true;Log("nativePlatedBaseSauceObserved",PlatedBaseStatus(p));
        }
        if(p.SauceObserved&&!final)throw new InvalidOperationException("Plate-first hotdog lost its exact final condiment recipe.");
    }
    private void ObservePlatedHotdogBases(){foreach(var p in platedBases.Values)ObservePlatedBase(p);}
    private void CompletePlatedBase(PlatedBase p)
    {
        ObservePlatedBase(p,false);
        if(!p.BunAcquired||!p.RemovalObserved||!p.CookedObserved||!p.Harvested||!p.SauceObserved||Held(p.Player)!=0||
            Attached(p.Board)!=0||Attached(p.Output)!=p.Plate||!NativeEmptyPot(p.Pot)||!CarnivalRecipes.MatchRecipe(Entity(p.Plate),p.Recipe).ReadyToDeliver)
            throw new InvalidOperationException("Plate-first hotdog lacks exact native final output, original consumed bun and emptied home pot.");
        if(p.UsesBuffer){bufferedBun=bunBufferCounter=0;Log("plannerBunBufferHarvested",new JsonObject{["food"]=p.Bun,["counter"]=p.Board,["mealIndex"]=p.Index,["plate"]=p.Plate,["frame"]=Frame});}
        mealPlates[p.Index]=p.Plate;assembling.Remove(p.Index);plating.Remove(p.Index);platedBases.Remove(p.Work);
        Log("nativePlatedBaseComplete",PlatedBaseStatus(p));
    }
    private JsonObject PlatedBaseStatus(PlatedBase p)=>new(){["frame"]=Frame,["player"]=p.Player,["mealIndex"]=p.Index,["recipeId"]=p.Recipe,
        ["board"]=p.Board,["bun"]=p.Bun,["plate"]=p.Plate,["plateSource"]=p.PlateSource,["pot"]=p.Pot,["home"]=p.Home,["output"]=p.Output,
        ["bunRegistration"]=p.BunRegistration,["bunUnityInstance"]=p.BunUnityInstance,["identities"]=JsonSerializer.SerializeToNode(p.Identities),
        ["usesBuffer"]=p.UsesBuffer,["pickedUp"]=p.PickedUp,["bunAcquired"]=p.BunAcquired,["removalObserved"]=p.RemovalObserved,
        ["cookedObserved"]=p.CookedObserved,["harvested"]=p.Harvested,["sauceObserved"]=p.SauceObserved,["startedFrame"]=p.Started,
        ["deadlineFrame"]=p.Deadline,["admissionEvidence"]=p.Evidence.DeepClone()};
}
