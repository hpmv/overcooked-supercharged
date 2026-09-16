using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class SeedOptimizerTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool value,string label){if(!value)throw new Exception("FAIL: "+label);checks++;}
        void Reject(Action action,string label){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("FAIL: "+label);}
        var plain=Enumerable.Range(0,80).Select(i=>new NativeSeedOrder(i,296560,40)).ToArray();
        var preview=new NativeSeedPreview(7,"native-module-fixture","sequence-fixture",plain);
        var estimate=SeedOptimizer.Estimate(preview,new(),new());
        Check(estimate.FreshFifoTargetDeliveries==71,"fresh FIFO tip ceiling retains both initial eight-point tips and native startup deficit");
        Check(estimate.Deliveries.Select(d=>d.Index).SequenceEqual(Enumerable.Range(0,estimate.EstimatedDeliveries)),"estimated delivery sequence stays FIFO");
        Check(estimate.Deliveries.All(d=>d.ReadySeconds>=CarnivalRecipes.BoilSeconds&&d.DeliverySeconds>=d.ReadySeconds),"native cooking duration and preparation dependency bound delivery");
        Check(estimate.Deliveries.All(d=>d.PlateToken is >=0 and <4),"only four native plate tokens are allocated");
        Check(estimate.Deliveries.GroupBy(d=>d.PlateToken).All(g=>g.Zip(g.Skip(1),(a,b)=>b.DeliverySeconds>=a.DeliverySeconds+CarnivalRecipes.PlateReturnSeconds+CarnivalRecipes.WashSeconds).All(x=>x)),"plate reuse includes native return and washing time");
        Check(estimate.EstimatedFreshScoreCeiling==estimate.Deliveries.Sum(d=>d.BaseValue+d.FreshTipCeiling),"score ceiling reconciles with estimated native-valued FIFO deliveries");
        var single=SeedOptimizer.Estimate(preview,new(ServiceBatch:1),new());
        Check(!single.Deliveries.Select(d=>d.DeliverySeconds).SequenceEqual(estimate.Deliveries.Select(d=>d.DeliverySeconds)),"service batch changes the resource timeline");
        var shortPreview=preview with{Recipes=plain.Take(12).ToArray()};
        var first=SeedOptimizer.Optimize([shortPreview],new(),5000,2,1,3);
        var second=SeedOptimizer.Optimize([shortPreview],new(),5000,2,1,3);
        Check(JsonSerializer.Serialize(first)==JsonSerializer.Serialize(second),"beam search and local refinement produce deterministic ordered output");
        Check(first.Classification=="offline_resource_surrogate_not_native_score_validation"&&first.Candidates.All(c=>!c.Parameters.UseDash&&!c.Parameters.DirectVesselThrows),"uncalibrated optional transfers and dash are never silently enabled");
        JsonObject Row(int seed)=>new()
        {
            ["seed"]=seed,["scene"]="s_Day_3_4",["generator"]="RoundData",["nativeModuleVersionId"]="native-fixture",
            ["freshInstance"]=true,["ambientRngRestored"]=true,["isolatedRngRestored"]=true,["observationsRestored"]=true,["liveInstanceUnchanged"]=true,["frameUnchanged"]=true,
            ["recipes"]=new JsonArray(plain.Take(4).Select(r=>(JsonNode)new JsonObject{["index"]=r.Index,["recipeId"]=r.RecipeId,["baseValue"]=r.BaseValue}).ToArray())
        };
        string directory=Path.Combine(Path.GetTempPath(),"oc2-seed-optimizer-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"previews.jsonl.gz");
            using(var stream=new GZipStream(File.Create(path),CompressionLevel.Fastest))using(var writer=new StreamWriter(stream))
            {writer.WriteLine(Row(1).ToJsonString());writer.WriteLine(new JsonObject{["kind"]="call",["response"]=new JsonObject{["preview"]=Row(2)}}.ToJsonString());}
            var read=SeedOptimizer.ReadPreviews(path);
            Check(read.Length==2&&read[0].Seed==1&&read[1].Seed==2&&read[0].SequenceHash==read[1].SequenceHash,"raw and traced gzip native previews resolve with stable sequence hashes");
            string badPath=Path.Combine(directory,"bad.json");var bad=Row(3);bad["ambientRngRestored"]=false;File.WriteAllText(badPath,bad.ToJsonString());
            Reject(()=>SeedOptimizer.ReadPreviews(badPath),"failed native restoration must reject preview");
            bad=Row(3);bad["recipes"]![1]!["index"]=8;File.WriteAllText(badPath,bad.ToJsonString());
            Reject(()=>SeedOptimizer.ReadPreviews(badPath),"noncontiguous native sequence rejected");
        }
        finally
        {
            foreach(string file in Directory.EnumerateFiles(directory))File.Delete(file);
            Directory.Delete(directory);
        }
        Console.WriteLine($"PASS: {checks} offline seed/batch optimization assertions.");return checks;
    }
}
