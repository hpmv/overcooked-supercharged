using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// Bounded, read-only trace projection. Never creates a game connection.
if(args.Length!=2)throw new ArgumentException("TRACE OUTPUT");
int[] counters=[32,33,37,38,40,41,43,61];
int I(JsonNode? n)=>n is null?0:int.Parse(n.ToString());
double N(JsonNode? n)=>n is null?0:double.Parse(n.ToString(),System.Globalization.CultureInfo.InvariantCulture);
var jobs=new List<(int Player,string Name,HashSet<int> Owned)>();
var onions=new Dictionary<int,JsonObject>();var bakery=new Dictionary<int,JsonObject>();var sausage=new Dictionary<int,JsonObject>();var fryer=new Dictionary<int,JsonObject>();JsonObject? sauce=null;
var countersHistogram=new int[9];var occupiedHistogram=new int[9];var anomalies=new List<string>();var transitions=new JsonArray();
JsonObject? snapshot=null;int samples=0,firstRuin=-1,minFree=8,maxPhys=0,maxLease=0,statusChecks=0,lastSaved=-1000;string prior="";
int[] recipes=[];JsonObject? config=null;JsonObject? worst=null;var critical=new JsonArray();
void Note(string text){if(anomalies.Count<30)anomalies.Add(text);}
void Flush()
{
 if(snapshot is null)return;
 int gf=I(snapshot["frame"]);var attached=snapshot["attached"]!.AsObject();
 var physical=counters.Where(c=>I(attached[c.ToString()])!=0).ToHashSet();
 var work=jobs.SelectMany(j=>j.Owned).Where(counters.Contains).ToHashSet();
 var lease=onions.Values.Select(l=>I(l["parkingCounter"])).Concat(bakery.Values.Select(l=>I(l["counter"])))
  .Concat(sausage.Values.Select(l=>I(l["counter"]))).Concat(fryer.Values.Select(l=>I(l["counter"]))).Where(counters.Contains).ToHashSet();
 if(sauce?["resources"] is JsonArray sr)lease.UnionWith(sr.Select(I).Where(counters.Contains));
 var used=physical.Union(work).Union(lease).ToHashSet();int free=8-used.Count;
 bool preRuin=firstRuin<0||gf<firstRuin;
 if(preRuin){countersHistogram[free]++;occupiedHistogram[physical.Count]++;minFree=Math.Min(minFree,free);maxPhys=Math.Max(maxPhys,physical.Count);maxLease=Math.Max(maxLease,lease.Count);}
 var observation=snapshot.DeepClone().AsObject();observation["physicallyOccupied"]=JsonSerializer.SerializeToNode(physical.Order());
 observation["jobReservedCounters"]=JsonSerializer.SerializeToNode(work.Order());observation["longLeasedCounters"]=JsonSerializer.SerializeToNode(lease.Order());
 observation["freeCounters"]=JsonSerializer.SerializeToNode(counters.Except(used));observation["freeCount"]=free;
 observation["onions"]=new JsonArray(onions.Values.Select(l=>(JsonNode?)l.DeepClone()).ToArray());
 observation["bakery"]=new JsonArray(bakery.Values.Select(l=>(JsonNode?)l.DeepClone()).ToArray());
 observation["sausage"]=new JsonArray(sausage.Values.Select(l=>(JsonNode?)l.DeepClone()).ToArray());observation["fryer"]=new JsonArray(fryer.Values.Select(l=>(JsonNode?)l.DeepClone()).ToArray());
 observation["jobs"]=JsonSerializer.SerializeToNode(jobs.Select(j=>new{player=j.Player,name=j.Name,counters=j.Owned.Where(counters.Contains).Order().ToArray()}));
 string signature=free+":"+string.Join(',',physical.Order())+":"+string.Join(',',lease.Order());
 if(preRuin&&(worst is null||free<I(worst["freeCount"])))worst=observation.DeepClone().AsObject();
 if(preRuin&&signature!=prior&&(free<=2||I(snapshot["criticalFryers"])>0)&&(gf-lastSaved>=30||gf==2336))
 {transitions.Add(observation.DeepClone());lastSaved=gf;}
 if(gf is 1991 or 2121 or 2208 or 2336 or 2521 or 2604 or 2627 or 2711 or 7454 or 8019 or 1824 or 1907 or 2934)critical.Add(observation.DeepClone());
 prior=signature;samples++;snapshot=null;
}
using var file=new FileStream(args[0],FileMode.Open,FileAccess.Read,FileShare.Read);
long bytes=file.Length;if(bytes>1_000_000_000)throw new InvalidOperationException("Trace size bound exceeded.");
string hash=Convert.ToHexString(SHA256.HashData(file)).ToLowerInvariant();file.Position=0;
using var gzip=new GZipStream(file,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
string? line;int lines=0;
while((line=reader.ReadLine())is not null)
{
 if(++lines>100000||line.Length>8_000_000)throw new InvalidOperationException("Line bound exceeded.");
 using var doc=JsonDocument.Parse(line);var row=doc.RootElement;string? kind=row.GetProperty("kind").GetString();
 if(kind=="event")
 {
  string name=row.GetProperty("name").GetString()!;var v=JsonNode.Parse(row.GetProperty("value").GetRawText())!.AsObject();
  if(name=="plannerInitialized"){config=v["configuration"]!.DeepClone().AsObject();recipes=v["preview"]!["recipes"]!.AsArray().Select(r=>I(r!["recipeId"])).ToArray();}
  if(name=="plannerJobStart")jobs.Add((I(v["player"]),v["name"]!.ToString(),v["resources"]!.AsArray().Select(I).ToHashSet()));
  if(name=="plannerJobComplete")
  {int k=jobs.FindIndex(j=>j.Player==I(v["player"])&&j.Name==v["name"]!.ToString());if(k<0)Note("Unmatched completion "+v);else jobs.RemoveAt(k);}
  if(name=="pantryChopDelegated")
  {int k=jobs.FindIndex(j=>j.Player==I(v["supplier"])&&j.Name==v["originalJob"]!.ToString());if(k<0)Note("Unmatched pantry delegation "+v);else jobs.RemoveAt(k);}
  if(name=="plannerResourceReleased")
  {int k=jobs.FindIndex(j=>j.Player==I(v["player"])&&j.Name==v["name"]!.ToString());if(k<0)Note("Unmatched resource release "+v);else jobs[k].Owned.Remove(I(v["resource"]));}
  if(name is "earlyOnionLeaseAcquired" or "parallelOnionLeaseAcquired" or "parallelOnionOrdinaryPanAdopted" or "earlyOnionPhase" or "earlyOnionOffheatProved")onions[I(v["pan"])]=v;
  if(name=="earlyOnionComplete")onions.Remove(I(v["pan"]));
  if(name is "bakeryLeaseAcquired" or "bakeryPhase" or "bakeryOffmixProved")bakery[I(v["bowl"])]=v;
  if(name=="bakeryLeaseReleased")bakery.Remove(I(v["bowl"]));
  if(name is "sausageBufferAdmitted" or "sausageBufferIngredientObserved" or "sausageBufferPhase")sausage[I(v["counter"])]=v;
  if(name=="sausageBufferConsumed")sausage.Remove(I(v["counter"]));
  if(name is "fryerRescueAcquired" or "fryerRescuePhase" or "fryerOffheatProved")fryer[I(v["basket"])]=v;
  if(name=="fryerRescueReleased")fryer.Remove(I(v["basket"]));
  if(name is "cooperativeSauceLeaseAcquired" or "cooperativeSauceBarrier")sauce=v;
  if(name is "cooperativeSauceComplete" or "cooperativeSauceAborted")sauce=null;
  if(name=="plannerStatus")
  {
   var actualOnions=(v["earlyOnions"] as JsonArray)?.OfType<JsonObject>().Select(l=>I(l["parkingCounter"])).Where(c=>c!=0).ToHashSet()??[];
   var actualBakery=(v["bakeryLeases"] as JsonArray)?.OfType<JsonObject>().Select(l=>I(l["counter"])).ToHashSet()??[];
   if(!actualOnions.SetEquals(onions.Values.Select(l=>I(l["parkingCounter"])))||!actualBakery.SetEquals(bakery.Values.Select(l=>I(l["counter"]))))Note("Long lease mismatch at "+v["gameplayFrame"]);
   statusChecks++;
  }
  continue;
 }
 if(kind!="call")continue;
 var response=row.GetProperty("response");if(!response.TryGetProperty("state",out var native)||native.ValueKind!=JsonValueKind.Object)continue;
 int frame=native.GetProperty("gameplayFrame").GetInt32();if(snapshot is not null&&I(snapshot["frame"])==frame)continue;Flush();
 var selected=new Dictionary<int,JsonElement>();var all=native.GetProperty("entities").EnumerateArray().ToArray();
 foreach(var e in all){int id=e.GetProperty("id").GetInt32();if(counters.Contains(id)||new[]{2,7,5,8,4,9,3,6,23,56,42,45}.Contains(id))selected[id]=e;}
 var attached=new JsonObject();var descriptions=new JsonObject();
 foreach(int c in counters.Append(42).Append(45).Append(23).Append(56))
 {
  int child=selected[c].GetProperty("attachedEntityId").GetInt32();attached[c.ToString()]=child;
  if(child!=0)
  {
   var e=all.FirstOrDefault(e=>e.GetProperty("id").GetInt32()==child);
   if(e.ValueKind==JsonValueKind.Object){var entity=JsonNode.Parse(e.GetRawText())!.AsObject();var food=CarnivalRecipes.ClassifyEntity(entity);
    descriptions[c.ToString()]=new JsonObject{["id"]=child,["name"]=entity["name"]?.DeepClone(),["ingredientIds"]=JsonSerializer.SerializeToNode(food.Food.IngredientIds),["preparation"]=food.Food.Preparation.ToString()};}
  }
 }
 int criticalFryers=0;var vessels=new JsonArray();
 foreach(int id in new[]{2,7,5,8,4,9,3,6})if(selected.TryGetValue(id,out var e))
 {
  var entity=JsonNode.Parse(e.GetRawText())!.AsObject();var f=CarnivalRecipes.ClassifyEntity(entity).Food;
  if(f.IsRuined&&firstRuin<0)firstRuin=frame;
  double progress=N(entity["cookingProgress"]);
  if(id is 5 or 8&&progress>=9&&f.IngredientIds.Length>0&&!f.IsRuined)criticalFryers++;
  vessels.Add(new JsonObject{["id"]=id,["ingredients"]=JsonSerializer.SerializeToNode(f.IngredientIds),["state"]=f.Preparation.ToString(),["progress"]=progress});
 }
 snapshot=new JsonObject{["frame"]=frame,["delivered"]=native.GetProperty("delivered").GetInt32(),["timer"]=native.GetProperty("timer").GetDouble(),
  ["attached"]=attached,["counterContents"]=descriptions,["vessels"]=vessels,["criticalFryers"]=criticalFryers,
  ["chefs"]=JsonSerializer.SerializeToNode(native.GetProperty("chefs").EnumerateArray().Select(c=>new{player=c.GetProperty("playerId").GetInt32(),held=c.GetProperty("heldEntityId").GetInt32(),position=JsonNode.Parse(c.GetProperty("position").GetRawText())}))};
 if(args[0].Contains("native-round-v11")&&frame is 1991 or 2121 or 2208 or 2521 or 2604 or 2627 or 2711)
  File.WriteAllText("artifacts/v11-pot-evidence-gf"+frame+".json",response.GetRawText()+Environment.NewLine);
}
Flush();
var windows=new JsonArray();var donut=new HashSet<int>{228996,130976};var onion=new HashSet<int>{47642,472326,257844};
for(int d=0;d+12<=recipes.Length;d++)
{
 int pan=Math.Min(2,recipes.Skip(d).Take(3).Count(onion.Contains));int fry=Math.Min(2,recipes.Skip(d).Take(8).Count(donut.Contains));int total=recipes.Skip(d).Take(12).Count(donut.Contains);
 int bakeryAndFryer=Math.Min(4,Math.Min(total,fry+2));
 windows.Add(new JsonObject{["delivered"]=d,["onionUpperBound"]=pan,["fryerUpperBound"]=fry,["distinctDonutsWithin12"]=total,["combinedFryerOffmixUpperBound"]=bakeryAndFryer,
 ["withSize1AndBun"]=pan+bakeryAndFryer+2,["withSize2AndBun"]=pan+bakeryAndFryer+3});
}
var report=new JsonObject{["source"]=Path.GetFullPath(args[0]),["sourceBytes"]=bytes,["sourceSha256"]=hash,["configuration"]=config,["samples"]=samples,["firstRuinedFrame"]=firstRuin,
 ["eligibleOrdinaryCounters"]=JsonSerializer.SerializeToNode(counters),["protectedWorkspace"]=42,["minimumFreeBeforeRuin"]=minFree,["maximumPhysicalOccupancyBeforeRuin"]=maxPhys,
 ["maximumLongLeasedCountersBeforeRuin"]=maxLease,["freeCountSampleHistogramBeforeRuin"]=JsonSerializer.SerializeToNode(countersHistogram),["physicalOccupancyHistogramBeforeRuin"]=JsonSerializer.SerializeToNode(occupiedHistogram),
 ["longLeaseStatusCrossChecks"]=statusChecks,["anomalies"]=JsonSerializer.SerializeToNode(anomalies),["worstBeforeRuin"]=worst,["selectedNativeFrames"]=critical,["pressureTransitionsBeforeRuin"]=transitions,["recipes"]=JsonSerializer.SerializeToNode(recipes),["recipeStorageUpperBounds"]=windows,
 ["policy"]="Post-event ownership union with native attachments at each completed frame; existing V9/V10 events only. Upper bounds include offmix leases retained after entering ordinary window; maxima are admissible capacity bounds, not a claimed simultaneous native state. No projected scores or native mutations."};
File.WriteAllText(args[1],report.ToJsonString(new(){WriteIndented=true})+Environment.NewLine);
Console.WriteLine(JsonSerializer.Serialize(new{source=args[0],samples,firstRuin,minFree,maxPhys,maxLease,statusChecks,anomalies,output=args[1]}));
