using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed partial class CarnivalPlanner
{
    private enum BakeryPhase { Supplying, Parking, Verifying, Parked, Transferring }
    private sealed class BakeryLease(int bowl,int index,int home,int counter,int ordinal,int homeOrdinal,int counterOrdinal,int frame)
    {
        public readonly int Bowl=bowl,Index=index,Home=home,Counter=counter,Ordinal=ordinal,StartFrame=frame;
        public readonly int HomeOrdinal=homeOrdinal,CounterOrdinal=counterOrdinal;
        public int Owner=-1,PhaseFrame=frame,ProofFrame,LastProofFrame,StableSamples;
        public double ParkedProgress,ProofTimer;
        public string ParkedFood="";
        public BakeryPhase Phase;
        public int[] Resources=>[Home,Counter];
    }
    private readonly Dictionary<int,BakeryLease> bakeryLeases=[];
    private int EffectiveBakeryLookahead=>Math.Max(options.Lookahead,options.BakeryLookahead);
    private bool IsBakeryParticipant(int player)=>bakeryLeases.Values.Any(l=>l.Owner==player);
    private bool BakeryCookAvailable(int player)=>workers[player] is null&&!IsSauceParticipant(player)&&!IsEarlyOnionParticipant(player)&&
        !IsBakeryParticipant(player)&&!IsFryerParticipant(player)&&!IsPotParticipant(player)&&Held(player)==0&&Region(player)=="center"&&B(chefs[player]["controlsEnabled"]);
    private static void ValidateBakeryOption(CarnivalPlannerOptions value)
    {
        if(value.BakeryLookahead is <0 or 1 or >32)throw new ArgumentException("Bakery lookahead must be zero or between two and 32 orders.");
    }
    private IEnumerable<(int Index,NativeRecipe Recipe)> BakeryPending()
    {
        if(options.BakeryLookahead==0)return Pending();
        return Enumerable.Range(delivered,Math.Min(EffectiveBakeryLookahead,recipes.Length-delivered))
            .Where(i=>!mealPlates.ContainsKey(i)&&!assembling.Contains(i)).Select(i=>(i,recipes[i]));
    }
    private bool WithinOrdinaryWindow(int index)=>index>=delivered&&index<Math.Min(recipes.Length,delivered+options.Lookahead)&&
        !mealPlates.ContainsKey(index)&&!assembling.Contains(index);
    private bool AdmitBakeryAssignment(int bowl,int index)
    {
        if(options.BakeryLookahead==0||WithinOrdinaryWindow(index))return true;
        if(bakeryLeases.ContainsKey(bowl)||bakeryLeases.Count>=2||!bowlHomes.TryGetValue(bowl,out int home)||
            !EmptyFood(bowl)||!Free(bowl,home)||HeldByAnyone(bowl)||Attached(home)!=bowl||
            N(Entity(bowl)?["mixingTime"])!=12||N(Entity(bowl)?["mixingProgress"])!=0)return false;
        int counter=EmptyCenterCounter(Station(bowl)!.Position);
        if(counter==0||KitchenModel.Components(Entity(counter)!).Any(c=>c is "MixingStation" or "CookingStation"))return false;
        var lease=new BakeryLease(bowl,index,home,counter,I(Entity(bowl)?["observedOrdinal"]),I(Entity(home)?["observedOrdinal"]),I(Entity(counter)?["observedOrdinal"]),Frame);
        foreach(int resource in lease.Resources)reserved.Add(resource);
        bakeryLeases.Add(bowl,lease);Log("bakeryLeaseAcquired",BakeryStatus(lease));return true;
    }
    private bool BakeryFryingAdmitted(int bowl,int index)
    {
        if(bakeryLeases.TryGetValue(bowl,out var active)&&active.Phase!=BakeryPhase.Parked)return false;
        if(options.BakeryLookahead==0)return true;
        if(!WithinOrdinaryWindow(index))return false;
        return !bakeryLeases.TryGetValue(bowl,out var lease)||lease.Phase==BakeryPhase.Parked;
    }
    private int[] BakeryTransferResources(int bowl,int basket,int home)=>bakeryLeases.ContainsKey(bowl)?[bowl,basket]:[bowl,basket,home];
    private void BeginBakeryTransfer(int bowl,int player)
    {
        if(!bakeryLeases.TryGetValue(bowl,out var lease))return;
        RequireBakeryIdentity(lease);RequireParkedBakery(lease);
        lease.Owner=player;SetBakeryPhase(lease,BakeryPhase.Transferring);
    }
    private void CompleteBakeryTransfer(int bowl)
    {
        if(!bakeryLeases.TryGetValue(bowl,out var lease))return;
        RequireBakeryIdentity(lease);
        if(Attached(lease.Home)!=bowl||Attached(lease.Counter)!=0||!EmptyFood(bowl)||Held(lease.Owner)!=0)
            throw new InvalidOperationException("Future bakery transfer did not restore its exact empty bowl and release its parking counter.");
        ReleaseBakeryLease(lease,"native-dough-transferred-and-original-empty-bowl-restored");
    }
    private void SetBakeryPhase(BakeryLease lease,BakeryPhase phase)
    {
        lease.Phase=phase;lease.PhaseFrame=Frame;Log("bakeryPhase",BakeryStatus(lease));
    }
    private void ReleaseBakeryLease(BakeryLease lease,string reason)
    {
        var observation=BakeryStatus(lease);observation["reason"]=reason;Log("bakeryLeaseReleased",observation);
        foreach(int resource in lease.Resources)reserved.Remove(resource);
        bakeryLeases.Remove(lease.Bowl);
    }
    private void RequireBakeryIdentity(BakeryLease lease)
    {
        if(Entity(lease.Bowl) is not {} bowl||Entity(lease.Home) is not {} home||Entity(lease.Counter) is not {} counter||
            I(bowl["observedOrdinal"])!=lease.Ordinal||I(home["observedOrdinal"])!=lease.HomeOrdinal||I(counter["observedOrdinal"])!=lease.CounterOrdinal||
            bowlAssignments.GetValueOrDefault(lease.Bowl,-1)!=lease.Index||
            !KitchenModel.Components(bowl).Contains("MixableContainer")||
            !KitchenModel.Components(home).Contains("MixingStation")||!KitchenModel.Components(counter).Contains("AttachStation")||
            lease.Resources.Any(r=>!reserved.Contains(r))||N(bowl["mixingTime"])!=12)
            throw new InvalidOperationException("Future bakery lease lost its exact native bowl, mixer or exclusive storage identity.");
        if(KitchenModel.Components(Entity(lease.Counter)??new()).Any(c=>c is "MixingStation" or "CookingStation"))
            throw new InvalidOperationException("Future bakery storage is an active processing station.");
    }
    private void RequireParkedBakery(BakeryLease lease)
    {
        if(Attached(lease.Counter)!=lease.Bowl||Attached(lease.Home)!=0||HeldByAnyone(lease.Bowl)||
            !AssignedDoughReady(Food(lease.Bowl),recipes[lease.Index])||
            N(Entity(lease.Bowl)?["mixingProgress"])!=lease.ParkedProgress||JsonSerializer.Serialize(Food(lease.Bowl))!=lease.ParkedFood)
            throw new InvalidOperationException("Future bakery dough did not remain unchanged in its exact bowl off the mixer.");
    }
    private JsonObject BakeryAction(string type,int station,int timeout=300) => VesselAction(type, station, timeout);
    private void AdvanceBakeryLookahead() => AdvanceBakeryLookahead(true);
    private void AdvanceBakeryLookahead(bool admitNewOwners)
    {
        foreach(var lease in bakeryLeases.Values.OrderByDescending(l=>Attached(l.Home)==l.Bowl?N(Entity(l.Bowl)?["mixingProgress"]):-1).ThenBy(l=>l.Index).ToArray())
        {
            RequireBakeryIdentity(lease);
            double progress=N(Entity(lease.Bowl)?["mixingProgress"]);
            if(Food(lease.Bowl).IsRuined||Attached(lease.Home)==lease.Bowl&&progress>=21)
                throw new TimeoutException("Future bakery dough could not leave its native mixer before the observed overmix deadline.");
            if(lease.Owner>=0&&(!B(chefs[lease.Owner]["controlsEnabled"])||Region(lease.Owner)!="center"||IsSauceParticipant(lease.Owner)||IsEarlyOnionParticipant(lease.Owner)))
                throw new InvalidOperationException("Future bakery worker lost native control or exclusive ownership.");
            if((lease.Phase is BakeryPhase.Parking or BakeryPhase.Transferring)&&Frame-lease.PhaseFrame>900)
                throw new TimeoutException("Future bakery transfer exceeded its native action budget.");
            if(lease.Phase==BakeryPhase.Supplying)
            {
                if(Attached(lease.Home)!=lease.Bowl||Attached(lease.Counter)!=0)
                    throw new InvalidOperationException("Future bakery mixing lost its original bowl or reserved empty counter.");
                if(WithinOrdinaryWindow(lease.Index))
                {
                    ReleaseBakeryLease(lease,"order-entered-ordinary-window-before-offmix-transfer");continue;
                }
                if(progress<9||!Food(lease.Bowl).IngredientIds.Order().SequenceEqual(recipes[lease.Index].RequiredInputs.Select(i=>i.Id).Order())||!Free(lease.Bowl))continue;
                if(!admitNewOwners)continue;
                int player=new[]{0,3}.Where(BakeryCookAvailable).OrderBy(p=>Position(p).Distance(Station(lease.Bowl)!.Position)).DefaultIfEmpty(-1).First();
                if(player<0)continue;
                TryAdmitBakeryParking(lease,player);continue;
            }
            if(lease.Phase==BakeryPhase.Verifying)
            {
                RequireParkedBakery(lease);
                if(Frame-lease.PhaseFrame>30)throw new TimeoutException("Future bakery offmix proof did not advance.");
                if(Frame>lease.LastProofFrame){lease.StableSamples++;lease.LastProofFrame=Frame;}
                if(lease.StableSamples>=2&&Frame-lease.ProofFrame>=2&&N(state["timer"])<lease.ProofTimer)
                {
                    Log("bakeryOffmixProved",BakeryStatus(lease));lease.Owner=-1;SetBakeryPhase(lease,BakeryPhase.Parked);
                }
            }
            else if(lease.Phase==BakeryPhase.Parked)RequireParkedBakery(lease);
        }
    }
    private bool TryAdmitBakeryParking(BakeryLease lease,int player)
    {
        if(!BakeryCookAvailable(player)||lease.Owner>=0||lease.Phase!=BakeryPhase.Supplying||!Free(lease.Bowl)||
            Attached(lease.Home)!=lease.Bowl||!AssignedDoughIngredients(lease.Bowl,lease.Index)||N(Entity(lease.Bowl)?["mixingProgress"])<9)return false;
        RequireBakeryIdentity(lease);
        if(!Start(player,"park-future-dough-"+(lease.Index+1),
                    [BakeryAction("navigate",lease.Home),BakeryAction("mix",lease.Bowl,300),BakeryAction("take",lease.Home),BakeryAction("place",lease.Counter)],
                    [lease.Bowl],()=>
                    {
                        RequireBakeryIdentity(lease);
                        if(Attached(lease.Counter)!=lease.Bowl||Attached(lease.Home)!=0||Held(player)!=0||!AssignedDoughReady(Food(lease.Bowl),recipes[lease.Index]))
                            throw new InvalidOperationException("Future bakery parking did not preserve its exact native Mixed recipe.");
                        lease.ParkedProgress=N(Entity(lease.Bowl)?["mixingProgress"]);lease.ParkedFood=JsonSerializer.Serialize(Food(lease.Bowl));
                        lease.ProofFrame=lease.LastProofFrame=Frame;lease.ProofTimer=N(state["timer"]);lease.StableSamples=0;
                        SetBakeryPhase(lease,BakeryPhase.Verifying);
                    }))return false;
        lease.Owner=player;SetBakeryPhase(lease,BakeryPhase.Parking);return true;
    }
    private bool AssignedDoughIngredients(int bowl,int index)=>index>=0&&index<recipes.Length&&IsDonut(recipes[index])&&
        !Food(bowl).IsRuined&&Food(bowl).IngredientIds.Order().SequenceEqual(recipes[index].RequiredInputs.Select(i=>i.Id).Order());
    private JsonObject BakeryStatus(BakeryLease lease)=>new(){["bowl"]=lease.Bowl,["orderIndex"]=lease.Index,["originalMixer"]=lease.Home,
        ["counter"]=lease.Counter,["observedOrdinal"]=lease.Ordinal,["owner"]=lease.Owner,["phase"]=lease.Phase.ToString(),
        ["mixerObservedOrdinal"]=lease.HomeOrdinal,["counterObservedOrdinal"]=lease.CounterOrdinal,
        ["frame"]=Frame,["startFrame"]=lease.StartFrame,["phaseFrame"]=lease.PhaseFrame,["mixingProgress"]=Entity(lease.Bowl)?["mixingProgress"]?.DeepClone(),
        ["nativeMixingTime"]=Entity(lease.Bowl)?["mixingTime"]?.DeepClone(),["nativeAbortProgress"]=21,
        ["parkedProgress"]=lease.ParkedProgress,["stableOffmixSamples"]=lease.StableSamples,["proofStartFrame"]=lease.ProofFrame,["proofStartTimer"]=lease.ProofTimer};
    private JsonArray BakeryStatus()=>new(bakeryLeases.Values.OrderBy(l=>l.Index).Select(l=>(JsonNode?)BakeryStatus(l)).ToArray());
}
