using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public sealed record NativeSeedOrder(int Index,int RecipeId,int BaseValue);
public sealed record NativeSeedPreview(int Seed,string NativeModuleVersionId,string SequenceHash,NativeSeedOrder[] Recipes);
public sealed record CarnivalBatchParameters(int Lookahead=8,int ServiceBatch=3,int IngredientBatch=2,int WashBatch=2,int PlateReserve=1,bool GroupCondiments=true,bool UseDash=false,bool DirectVesselThrows=false);
public sealed record SeedCostModel
{
    public string Provenance {get;init;}="Uncalibrated route-cost assumptions; replace with measured action timings before interpreting absolute predictions.";
    public double PantryGrabSeconds {get;init;}=.55;
    public double PantryTripSeconds {get;init;}=2.0;
    public double ChopSetupSeconds {get;init;}=.35;
    public double VesselLoadSeconds {get;init;}=.7;
    public double VesselHarvestSeconds {get;init;}=.7;
    public double PlateAssemblySeconds {get;init;}=1;
    public double CondimentSeconds {get;init;}=.65;
    public double CondimentSwitchSeconds {get;init;}=.7;
    public double ServiceTripSeconds {get;init;}=5;
    public double ServeEachSeconds {get;init;}=.65;
    public double WashTripSeconds {get;init;}=5;
    public double CleanHandoffSeconds {get;init;}=.65;
    public double BatchCongestionSeconds {get;init;}=.25;
    public double DashTravelFactor {get;init;}=1;
    public double VesselThrowFactor {get;init;}=1;
    public bool AllowDashCandidates {get;init;}
    public bool AllowVesselThrowCandidates {get;init;}
}
public sealed record EstimatedDelivery(int Index,int RecipeId,int BaseValue,int FreshTipCeiling,double ReadySeconds,double DeliverySeconds,int PlateToken);
public sealed record SeedCandidate(int Seed,string SequenceHash,CarnivalBatchParameters Parameters,int FreshFifoTargetDeliveries,int EstimatedDeliveries,int EstimatedFreshScoreCeiling,double? EstimatedTargetSeconds,double LastEstimatedDeliverySeconds,int LateHarvests,int PreviewCount,EstimatedDelivery[] Deliveries)
{
    public Dictionary<string,double> ChefBusySeconds {get;init;}=[];
    public Dictionary<string,double> VesselBusySeconds {get;init;}=[];
}
public sealed record SeedOptimizationResult(string Classification,string ModelVersion,int TargetScore,int InputSeeds,int EvaluatedCandidates,SeedCostModel Costs,string[] Assumptions,SeedCandidate[] Candidates);

/// <summary>Offline candidate ranking only. No game connection, native RNG calls, inputs, or score modification.</summary>
public static class SeedOptimizer
{
    private static readonly JsonSerializerOptions Format=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    public static int Command(Options options)
    {
        string source=Path.GetFullPath(options.Required("file"));
        var previews=ReadPreviews(source);
        var costs=options.Has("costs")?JsonSerializer.Deserialize<SeedCostModel>(File.ReadAllText(options.Required("costs")),Format)??throw new InvalidDataException("Cost model is null."):new SeedCostModel();
        var result=Optimize(previews,costs,options.Int("target-score",5000),options.Int("beam",8),options.Int("rounds",2),options.Int("top",10));
        var output=JsonSerializer.SerializeToNode(result,Format)!.AsObject();
        output["inputFile"]=source;output["inputSha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source)));
        output["nativeModuleVersionId"]=previews[0].NativeModuleVersionId;
        if(options.Has("costs")){output["costFile"]=Path.GetFullPath(options.Required("costs"));output["costSha256"]=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(options.Required("costs"))));}
        string json=output.ToJsonString(Format);
        if(options.Has("out"))
        {
            string destination=Path.GetFullPath(options.Required("out"));
            if(destination.Equals(source,StringComparison.OrdinalIgnoreCase)||options.Has("costs")&&destination.Equals(Path.GetFullPath(options.Required("costs")),StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Optimizer output must not overwrite an input.");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.WriteAllText(destination,json);
        }
        Console.WriteLine(json);return 0;
    }
    public static NativeSeedPreview[] ReadPreviews(string path)
    {
        using var source=File.OpenRead(path);
        using var decompressed=path.EndsWith(".gz",StringComparison.OrdinalIgnoreCase)?new GZipStream(source,CompressionMode.Decompress):null;
        using var reader=new StreamReader(decompressed is null?source:decompressed);
        var rows=new List<JsonObject>();
        if(path.EndsWith(".json",StringComparison.OrdinalIgnoreCase)||path.EndsWith(".json.gz",StringComparison.OrdinalIgnoreCase))
        {
            var parsed=JsonNode.Parse(reader.ReadToEnd())??throw new InvalidDataException("Preview file is empty.");
            if(parsed is JsonArray array)rows.AddRange(array.Select(n=>n?.AsObject()??throw new InvalidDataException("Null preview row.")));
            else rows.Add(parsed.AsObject());
        }
        else
        {
            int lineNumber=0;
            while(reader.ReadLine() is { } line)
            {
                lineNumber++;if(string.IsNullOrWhiteSpace(line))continue;
                try{rows.Add(Json.Object(line));}catch(Exception error){throw new InvalidDataException("Invalid native preview JSON at line "+lineNumber,error);}
            }
        }
        var result=new Dictionary<int,NativeSeedPreview>();string? module=null;
        foreach(var row in rows)
        {
            if(row["kind"]?.ToString() is "header" or "event")continue;
            var preview=(row["response"]?["preview"]??row["preview"]??row).AsObject();
            if(preview["recipes"] is not JsonArray recipes)continue;
            if(preview["scene"]?.ToString()!="s_Day_3_4"||preview["generator"]?.ToString()!="RoundData")throw new InvalidDataException("Preview must be native Carnival RoundData.");
            foreach(string flag in new[]{"freshInstance","ambientRngRestored","isolatedRngRestored","observationsRestored","liveInstanceUnchanged","frameUnchanged"})
                if(preview[flag]?.GetValue<bool>()!=true)throw new InvalidDataException("Native preview restoration invariant is not true: "+flag);
            string currentModule=preview["nativeModuleVersionId"]?.ToString()??throw new InvalidDataException("Native preview module identity is missing.");
            if(module is not null&&module!=currentModule)throw new InvalidDataException("Cannot mix previews from different native assemblies.");module=currentModule;
            var orders=recipes.Select((node,index)=>
            {
                if(node?["index"]?.GetValue<int>()!=index)throw new InvalidDataException("Native preview order indices must be contiguous from zero.");
                int recipeId=node["recipeId"]!.GetValue<int>(),baseValue=node["baseValue"]!.GetValue<int>();
                var known=CarnivalRecipes.GetRecipe(recipeId);
                if(baseValue!=known.BaseScore)throw new InvalidDataException("Native recipe value differs from the audited installed recipe model: "+recipeId);
                return new NativeSeedOrder(index,recipeId,baseValue);
            }).ToArray();
            if(orders.Length<4)throw new InvalidDataException("Each preview needs at least four native recipe draws.");
            string sequence=string.Join(";",orders.Select(o=>$"{o.Index}:{o.RecipeId}:{o.BaseValue}"));
            var item=new NativeSeedPreview(preview["seed"]!.GetValue<int>(),currentModule,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sequence))),orders);
            if(result.TryGetValue(item.Seed,out var previous)&&previous.SequenceHash!=item.SequenceHash)throw new InvalidDataException("The same seed has conflicting native preview sequences.");
            result[item.Seed]=item;
        }
        if(result.Count==0)throw new InvalidDataException("File contains no verified native seed previews.");
        return result.Values.OrderBy(p=>p.Seed).ToArray();
    }
    public static SeedOptimizationResult Optimize(IReadOnlyList<NativeSeedPreview> previews,SeedCostModel costs,int target=5000,int beamWidth=8,int rounds=2,int top=10)
    {
        ValidateCosts(costs);
        if(previews.Count==0||target<=0||beamWidth is <1 or >128||rounds is <0 or >10||top is <1 or >128)throw new ArgumentOutOfRangeException(nameof(beamWidth));
        var cache=new Dictionary<(int,CarnivalBatchParameters),SeedCandidate>();
        SeedCandidate Evaluate(NativeSeedPreview preview,CarnivalBatchParameters parameters)
        {
            var key=(preview.Seed,parameters);
            if(!cache.TryGetValue(key,out var candidate)){candidate=Estimate(preview,parameters,costs,target);cache.Add(key,candidate);}
            return candidate;
        }
        SeedCandidate[] Rank(IEnumerable<SeedCandidate> candidates,int count)=>candidates.DistinctBy(c=>(c.Seed,c.Parameters))
            .OrderBy(c=>c.LateHarvests).ThenByDescending(c=>c.EstimatedFreshScoreCeiling).ThenBy(c=>c.EstimatedTargetSeconds??double.PositiveInfinity)
            .ThenBy(c=>c.LastEstimatedDeliverySeconds).ThenBy(c=>c.FreshFifoTargetDeliveries).ThenBy(c=>c.Seed).ThenBy(c=>JsonSerializer.Serialize(c.Parameters),StringComparer.Ordinal).Take(count).ToArray();
        var bySeed=previews.ToDictionary(p=>p.Seed);
        var beam=Rank(previews.Select(p=>Evaluate(p,new())),beamWidth);
        for(int dimension=0;dimension<8;dimension++)
            beam=Rank(beam.SelectMany(c=>Variants(c.Parameters,dimension,costs).Select(p=>Evaluate(bySeed[c.Seed],p))),beamWidth);
        for(int round=0;round<rounds;round++)
        {
            var refined=Rank(beam.Concat(beam.SelectMany(c=>Enumerable.Range(0,8).SelectMany(d=>Variants(c.Parameters,d,costs)).Select(p=>Evaluate(bySeed[c.Seed],p)))),beamWidth);
            bool changed=!refined.Select(c=>(c.Seed,c.Parameters)).SequenceEqual(beam.Select(c=>(c.Seed,c.Parameters)));beam=refined;if(!changed)break;
        }
        return new("offline_resource_surrogate_not_native_score_validation","carnival-seed-batches-v1",target,previews.Count,cache.Count,costs,
        ["Native weighted order sequences and recipe values come from validated read-only previews; the RNG is never regenerated here.",
         "Ranking assumes fresh FIFO tips and order availability. Native order arrival timing, expiry, missed-order deductions, collisions and legal input completion are not simulated.",
         "Two central chef calendars, alternating pantry/service and bakery/washing chef calendars, two vessels per cooking kind, native processing durations, four reusable plates and seven-second dirty returns constrain the estimate.",
         "Cooked food can wait unplated; plates constrain assembly/service/washing rather than pot harvesting. Passive cooking never occupies a chef calendar.",
         "Movement, handoff and role-change durations come from the explicit cost model; defaults are uncalibrated assumptions. All promising candidates require fresh-start execution in the real game.",
         "Beam pruning and local refinement are deterministic but do not guarantee a globally optimal seed or batch configuration. The report retains the best evaluated parameters for each seed. Candidate parameters beyond Lookahead/ServiceBatch/DirectVesselThrows require planner support."],Rank(cache.Values.GroupBy(c=>c.Seed).Select(g=>Rank(g,1)[0]),top));
    }
    private static IEnumerable<CarnivalBatchParameters> Variants(CarnivalBatchParameters p,int dimension,SeedCostModel costs)
    {
        yield return p;
        foreach(int value in dimension==0?new[]{4,6,8,10,12}:new[]{1,2,3,4})
        {
            if(dimension==0)yield return p with{Lookahead=value};
            if(dimension==1)yield return p with{ServiceBatch=value};
            if(dimension==2)yield return p with{IngredientBatch=value};
            if(dimension==3)yield return p with{WashBatch=value};
        }
        if(dimension==4)foreach(int value in new[]{0,1,2})yield return p with{PlateReserve=value};
        if(dimension==5)yield return p with{GroupCondiments=!p.GroupCondiments};
        if(dimension==6&&costs.AllowDashCandidates)yield return p with{UseDash=!p.UseDash};
        if(dimension==7&&costs.AllowVesselThrowCandidates)yield return p with{DirectVesselThrows=!p.DirectVesselThrows};
    }
    private static void ValidateCosts(SeedCostModel costs)
    {
        foreach(var property in typeof(SeedCostModel).GetProperties().Where(p=>p.PropertyType==typeof(double)))
        {
            double value=(double)property.GetValue(costs)!;
            if(!double.IsFinite(value)||value<0||value>60)throw new ArgumentException("Cost must be finite seconds in0..60: "+property.Name);
        }
        if(costs.DashTravelFactor<=0||costs.DashTravelFactor>1||costs.VesselThrowFactor<=0||costs.VesselThrowFactor>1)throw new ArgumentException("Optional travel/throw factors must be in (0,1].");
    }
    public static SeedCandidate Estimate(NativeSeedPreview preview,CarnivalBatchParameters p,SeedCostModel c,int target=5000)
    {
        if(p.Lookahead is <1 or >32||p.ServiceBatch is <1 or >4||p.IngredientBatch is <1 or >4||p.WashBatch is <1 or >4||p.PlateReserve is <0 or >3)throw new ArgumentException("Invalid Carnival batching parameters.");
        var calendar=new ReservationTable();int serial=0,late=0,sauce=CarnivalRecipes.MustardSwitchIndex;
        int F(double seconds)=>(int)Math.Ceiling(seconds*60-1e-8);
        int Schedule(int chef,int ready,double seconds,string label,params string[] resources)
        {
            int duration=Math.Max(1,F(seconds));var keys=resources.Append("chef:"+chef).ToArray();int start=calendar.Earliest(keys,ready,duration);
            calendar.Add(keys,start,duration,label+":"+serial++);return start+duration;
        }
        int Center(int ready,double seconds,string label,params string[] resources)
        {
            int duration=Math.Max(1,F(seconds));int chef=new[]{0,3}.OrderBy(id=>calendar.Earliest(resources.Append("chef:"+id),ready,duration)).ThenBy(id=>id).First();
            return Schedule(chef,ready,seconds,label,resources);
        }
        var vessels=new Dictionary<string,int[]>{{"pot",new int[2]},{"pan",new int[2]},{"mixer",new int[2]},{"fryer",new int[2]}};
        var vesselBusy=new Dictionary<string,double>{{"pot",0},{"pan",0},{"mixer",0},{"fryer",0}};
        int PrepareVessel(string kind,int ready,double duration)
        {
            int index=vessels[kind][0]<=vessels[kind][1]?0:1;double factor=p.DirectVesselThrows?c.VesselThrowFactor:1;
            int loaded=Center(Math.Max(ready,vessels[kind][index]),c.VesselLoadSeconds*factor,kind+"-load");
            int nativeReady=loaded+F(duration),harvest=Center(nativeReady,c.VesselHarvestSeconds,kind+"-harvest");
            if(harvest-F(c.VesselHarvestSeconds)>loaded+F(duration*2))late++;
            int started=loaded-Math.Max(1,F(c.VesselLoadSeconds*factor));
            vesselBusy[kind]+=Math.Max(0,Math.Min(harvest,F(CarnivalRecipes.RoundSeconds))-Math.Min(started,F(CarnivalRecipes.RoundSeconds)))/60.0;
            vessels[kind][index]=harvest;return harvest;
        }
        double travel=p.UseDash?c.DashTravelFactor:1;
        var readyMeals=new int[preview.Recipes.Length];int prepared=0,served=0,score=0,freshTarget=0,freshSum=0,preparationRelease=0;
        foreach(var order in preview.Recipes){freshSum+=order.BaseValue+Tip(order.Index);if(freshTarget==0&&freshSum>=target)freshTarget=order.Index+1;}
        if(freshTarget==0)freshTarget=int.MaxValue;
        int PrepareMeal(int index)
        {
            var recipe=CarnivalRecipes.GetRecipe(preview.Recipes[index].RecipeId);bool donut=recipe.Preparation.Any(s=>s.Operation=="mix");
            var inputs=recipe.RequiredInputs.Where(i=>i!=CarnivalRecipes.Ketchup&&i!=CarnivalRecipes.Mustard).ToArray();
            double pantry=inputs.Length*c.PantryGrabSeconds+inputs.Sum(i=>i.ChopSeconds+(i.ChopImpacts>0?c.ChopSetupSeconds:0))+c.PantryTripSeconds*travel/p.IngredientBatch+c.BatchCongestionSeconds*Math.Pow(Math.Max(0,p.IngredientBatch-2),2);
            int supplied=Schedule(donut?1:2,preparationRelease,pantry,"pantry");
            if(donut)return PrepareVessel("fryer",PrepareVessel("mixer",supplied,CarnivalRecipes.MixSeconds),CarnivalRecipes.DeepFrySeconds);
            int sausage=PrepareVessel("pot",supplied,CarnivalRecipes.BoilSeconds);
            int onion=inputs.Contains(CarnivalRecipes.Onion)?PrepareVessel("pan",supplied,CarnivalRecipes.PanSeconds):0;
            return Math.Max(sausage,onion);
        }
        var plateReady=new int[4];var dirty=new List<(int Plate,int Ready)>();var deliveries=new List<EstimatedDelivery>();double? targetSeconds=null;
        void WashPending()
        {
            if(dirty.Count==0)return;
            var batch=dirty.Take(Math.Min(p.WashBatch,dirty.Count)).ToArray();dirty.RemoveRange(0,batch.Length);
            int after=Schedule(1,batch.Max(d=>d.Ready),c.WashTripSeconds*travel+batch.Length*CarnivalRecipes.WashSeconds+batch.Length*c.CleanHandoffSeconds,"wash-wave","sink");
            foreach(var item in batch)plateReady[item.Plate]=Center(after,c.CleanHandoffSeconds,"clean-handoff");
        }
        while(served<preview.Recipes.Length)
        {
            while(prepared<Math.Min(preview.Recipes.Length,served+p.Lookahead)){readyMeals[prepared]=PrepareMeal(prepared);prepared++;}
            int batchCount=Math.Min(Math.Min(p.ServiceBatch,4-p.PlateReserve),preview.Recipes.Length-served);batchCount=Math.Min(batchCount,prepared-served);
            var batch=new List<(int Index,int Ready,int Plate)>();
            // Sauce grouping can change the preparation order; service remains FIFO.
            var preparationOrder=Enumerable.Range(served,batchCount);
            if(p.GroupCondiments)preparationOrder=preparationOrder.OrderBy(i=>CarnivalRecipes.GetRecipe(preview.Recipes[i].RecipeId).RequiredInputs.Contains(sauce==0?CarnivalRecipes.Mustard:CarnivalRecipes.Ketchup)?0:1).ThenBy(i=>i);
            foreach(int index in preparationOrder)
            {
                if(plateReady.All(t=>t==int.MaxValue))WashPending();
                int plate=Enumerable.Range(0,4).OrderBy(i=>plateReady[i]).ThenBy(i=>i).First();
                if(plateReady[plate]==int.MaxValue)throw new InvalidOperationException("Plate-token surrogate deadlocked.");
                var recipe=CarnivalRecipes.GetRecipe(preview.Recipes[index].RecipeId);var sauces=recipe.RequiredInputs.Where(i=>i==CarnivalRecipes.Mustard||i==CarnivalRecipes.Ketchup).ToArray();
                double plating=c.PlateAssemblySeconds+sauces.Length*c.CondimentSeconds;
                foreach(var ingredient in sauces.OrderBy(i=>p.GroupCondiments&&(i==CarnivalRecipes.Mustard?0:1)==sauce?0:1))
                {
                    int desired=ingredient==CarnivalRecipes.Mustard?0:1;if(desired!=sauce){plating+=c.CondimentSwitchSeconds;sauce=desired;}
                }
                int assembled=Center(Math.Max(readyMeals[index],plateReady[plate]),plating,"plate",sauces.Length>0?["condiment"]:[]);
                plateReady[plate]=int.MaxValue;batch.Add((index,assembled,plate));
            }
            int serviceEnd=Schedule(2,batch.Max(b=>b.Ready),c.ServiceTripSeconds*travel+batch.Count*c.ServeEachSeconds,"service-wave","service-pass");
            int firstService=serviceEnd-F(c.ServiceTripSeconds*travel*.5)-F(batch.Count*c.ServeEachSeconds);
            foreach(var item in batch.OrderBy(b=>b.Index))
            {
                int deliveredAt=firstService+F(c.ServeEachSeconds*(item.Index-served+1));var order=preview.Recipes[item.Index];
                if(deliveredAt<=F(CarnivalRecipes.RoundSeconds))
                {
                    score+=order.BaseValue+Tip(order.Index);deliveries.Add(new(item.Index,order.RecipeId,order.BaseValue,Tip(order.Index),readyMeals[item.Index]/60.0,deliveredAt/60.0,item.Plate));
                    if(targetSeconds is null&&score>=target)targetSeconds=deliveredAt/60.0;
                }
                dirty.Add((item.Plate,deliveredAt+F(CarnivalRecipes.PlateReturnSeconds)));
            }
            served+=batchCount;
            preparationRelease=Math.Max(preparationRelease,serviceEnd-F(c.ServiceTripSeconds*travel*.5));
            while(dirty.Count>=p.WashBatch)WashPending();
            if(firstService>F(CarnivalRecipes.RoundSeconds))break;
        }
        return new(preview.Seed,preview.SequenceHash,p,freshTarget,deliveries.Count,score,targetSeconds,deliveries.LastOrDefault()?.DeliverySeconds??0,late,preview.Recipes.Length,deliveries.ToArray())
        {
            ChefBusySeconds=Enumerable.Range(0,4).ToDictionary(id=>"chef:"+id,id=>calendar.Entries.Where(r=>r.Resource=="chef:"+id).Sum(r=>Math.Max(0,Math.Min(r.End,F(CarnivalRecipes.RoundSeconds))-Math.Min(r.Start,F(CarnivalRecipes.RoundSeconds)))/60.0)),
            VesselBusySeconds=vesselBusy
        };
    }
    private static int Tip(int index)=>8*Math.Max(1,Math.Min(4,index));
}
