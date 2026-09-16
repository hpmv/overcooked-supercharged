using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OvercookedTAS.Controller;

// Independent, file-only experiment checker. No TasClient or network calls.
return SauceCheck.Run(args);

static class SauceCheck
{
    const string FrozenHash = "60db3656d074464ec35d2ea5126ee0abff9073a148d188a7509cce107bb7a025";
    static readonly string[] ChefDiscrete = ["playerId","entityId","heldEntityId","controlsEnabled","canAcceptInput","directlyControlled","respawning","aimingThrow","useSuppressed","inputSuppressed","pickupTargetId","placementTargetId","useTargetId","serverInteractionId","clientPredictedInteractionId"];
    static readonly string[] EntityDiscrete = ["id","observedOrdinal","name","active","attachedEntityId","ingredientIds","contents","composition","workStage","workSubStage","cookingTypeId","mixingTypeId","switchIndex","plateCount","plateStackEntityId","plateStackKind","cannonFlying","cannonLoadedEntityId","portalTeleporting","portalReceiving","throwFlying"];
    static readonly string[] WorldDiscrete = ["scene","gameState","score","baseScore","tips","multiplier","combo","delivered","deductions","serverRoundActive","clientRoundActive","timerSuppressed"];
    static readonly string[] ClockFields = ["gameplayFrame","gameplayFixedFrame","physicsStepsThisFrame","framesSinceNoPhysics","levelStartPhysicsPhase","timer","clientTime","clientDeltaTime","fixedDeltaTime","logicalTime"];
    static readonly string[] PhysicalFields = ["position","rotation","velocity","angularVelocity","forward","cachedVelocity","dashTimer","dashRemaining","kinematic","sleeping"];
    static readonly int[] FixedIds = [2,3,6,7,13,14,18,19,20,33,37,40,43,49,72,79];
    static int I(JsonNode? n) => n is null ? 0 : int.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
    static double N(JsonNode? n) => n is null ? double.NaN : double.Parse(n.ToString(), System.Globalization.CultureInfo.InvariantCulture);
    static bool B(JsonNode? n) => n?.GetValue<bool>() == true;
    static void Need(bool b, string why) { if (!b) throw new InvalidDataException(why); }
    static JsonObject Read(string p) => JsonNode.Parse(File.ReadAllText(p))!.AsObject();
    static string FileHash(string p) { using var f = File.OpenRead(p); return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant(); }
    static JsonNode? Sort(JsonNode? n) => n switch { JsonObject o => new JsonObject(o.OrderBy(p=>p.Key,StringComparer.Ordinal).Select(p=>KeyValuePair.Create(p.Key,Sort(p.Value)))), JsonArray a=>new JsonArray(a.Select(Sort).ToArray()), _=>n?.DeepClone() };
    static string Hash(JsonNode? n) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sort(n)?.ToJsonString() ?? "null"))).ToLowerInvariant();
    static JsonObject Pick(JsonObject o, IEnumerable<string> fields) => new(fields.Where(o.ContainsKey).Select(k=>KeyValuePair.Create(k,o[k]?.DeepClone())));
    static JsonObject E(JsonObject s, int id) => s["entities"]!.AsArray().OfType<JsonObject>().Single(e=>I(e["id"])==id);
    static JsonObject C(JsonObject s, int id) => s["chefs"]!.AsArray().OfType<JsonObject>().Single(e=>I(e["playerId"])==id);
    static JsonObject Pad(JsonObject response,int p) => response["inputs"]!.AsArray().OfType<JsonObject>().Single(x=>I(x["player"])==p);
    static bool Neutral(JsonObject p) => N(p["x"])==0 && N(p["y"])==0 && !B(p["pickup"]) && !B(p["use"]) && !B(p["dash"]);
    static bool AllNeutral(JsonObject r) => r["inputs"]!.AsArray().Count==4 && r["inputs"]!.AsArray().OfType<JsonObject>().All(Neutral);
    static JsonObject FileProof(string path) => new(){["path"]=Path.GetFullPath(path),["bytes"]=new FileInfo(path).Length,["sha256"]=FileHash(path)};
    static IEnumerable<JsonObject> Rows(string path)
    {
        using var f=File.OpenRead(path); long size=f.Length; var mtime=File.GetLastWriteTimeUtc(path);
        using Stream input=path.EndsWith(".gz",StringComparison.OrdinalIgnoreCase)?new GZipStream(f,CompressionMode.Decompress):f;
        using var reader=new StreamReader(input); string? line;
        while((line=reader.ReadLine()) is not null) if(!string.IsNullOrWhiteSpace(line)) yield return JsonNode.Parse(line)!.AsObject();
        Need(f.Length==size && File.GetLastWriteTimeUtc(path)==mtime,"Trace changed while checking: "+path);
    }
    static JsonObject[] Requests(string path) => Rows(path).Where(r=>r["kind"]?.ToString() is not ("header" or "event")).Select(r=>(r["request"]??r).DeepClone().AsObject()).ToArray();
    static void SameRequests(IEnumerable<JsonObject> a,IEnumerable<JsonObject> b,string where)
    {
        var aa=a.ToArray();var bb=b.ToArray();Need(aa.Length==bb.Length,where+" request count differs.");
        for(int i=0;i<aa.Length;i++)Need(JsonNode.DeepEquals(aa[i],bb[i]),$"{where} request {i} differs (no axis normalization or boundary changes permitted).");
    }
    static JsonObject Projection(JsonObject s,bool physical=false)
    {
        var result=physical?new JsonObject():Pick(s,WorldDiscrete);
        result["chefs"]=new JsonArray(s["chefs"]!.AsArray().OfType<JsonObject>().OrderBy(x=>I(x["playerId"])).Select(x=>(JsonNode)Pick(x,physical?PhysicalFields.Concat(["playerId","entityId"]):ChefDiscrete)).ToArray());
        result["entities"]=new JsonArray(s["entities"]!.AsArray().OfType<JsonObject>().OrderBy(x=>I(x["id"])).Select(x=>(JsonNode)Pick(x,physical?PhysicalFields.Concat(["id","observedOrdinal"]):EntityDiscrete)).ToArray());
        if(!physical) result["orders"]=new JsonArray(s["orders"]!.AsArray().OfType<JsonObject>().Select(x=>(JsonNode)new JsonObject(x.Where(p=>!p.Key.Contains("time",StringComparison.OrdinalIgnoreCase)).Select(p=>KeyValuePair.Create(p.Key,p.Value?.DeepClone())))).ToArray());
        return result;
    }
    sealed record Stamp(int Frame,string Discrete,string Physical,string Clock);
    sealed class Trace
    {
        public string Path="", Plugin="", Instrumentation="";
        public JsonObject First=null!, Last=null!;
        public List<JsonObject> Requests=[];
        public List<Stamp> Stamps=[];
        public List<JsonObject> Events=[];
        public JsonArray SauceTransitions=[];
        public Dictionary<int,(double Progress,string Food,int Frame,int Samples)> OffMix=[];
        public Dictionary<int,double> MaximumPotProgress=[];
        public HashSet<int> Parked=[];
        public int BaseFrame=-1, MustardFrame=-1, KetchupFrame=-1, SwitchFrame=-1;
        public double Travel;
    }
    static void ValidatePads(JsonObject req,JsonObject res,int? onlyPlayer)
    {
        var pads=req["inputs"]!.AsArray().OfType<JsonObject>().ToArray();
        Need(pads.Length==4 && pads.Select(p=>I(p["player"])).Order().SequenceEqual(new[]{0,1,2,3}),"Step lacks four distinct logical pads.");
        foreach(var pad in pads)
        {
            Need(pad.All(p=>new[]{"player","x","y","pickup","use","dash"}.Contains(p.Key)),"Unknown gameplay input field.");
            foreach(string axis in new[]{"x","y"}) Need(double.IsFinite(N(pad[axis])) && Math.Abs(N(pad[axis]))<=1,"Invalid ordinary axis.");
            foreach(string button in new[]{"pickup","use","dash"}) Need(pad[button] is JsonValue v && v.TryGetValue<bool>(out _),"Button is not boolean.");
            int player=I(pad["player"]); var observed=Pad(res,player);
            foreach(string button in new[]{"pickup","use","dash"}) Need(B(observed[button])==B(pad[button]),"Observed native button differs from requested input.");
            float x=(float)N(pad["x"]),y=(float)N(pad["y"]); float mag=(float)Math.Sqrt(x*x+y*y);
            Need((float)N(observed["x"])==(mag>1?x/mag:x) && (float)N(observed["y"])==(mag>1?y/mag:y),"Observed native axes differ from native float input normalization.");
            if(onlyPlayer is not null && player!=onlyPlayer) Need(Neutral(pad),"A non-participating chef received branch/setup input.");
        }
    }
    static Trace AuditTrace(string path,string phase,int temp=0)
    {
        var t=new Trace{Path=path}; JsonObject? previous=null; Dictionary<int,int>? ordinals=null;
        foreach(var row in Rows(path))
        {
            if(row["kind"]?.ToString()=="event") { t.Events.Add(row.DeepClone().AsObject()); continue; }
            if(row["kind"]?.ToString()=="header")continue;
            Need(row["kind"]?.ToString()=="call","Unknown trace row.");
            var req=row["request"]!.AsObject(); var res=row["response"]!.AsObject();var s=res["state"]!.AsObject();string command=req["command"]!.ToString();
            Need(I(req["version"])==1 && B(res["ok"]) && B(res["paused"]),"Protocol failed or response not paused.");
            Need(command is "step" or "inspect" or "preview" or "restart","Unexpected command; this checker permits no restoration, resume, render, or game-state writes.");
            if(command=="restart")Need(phase=="prefix" && t.Requests.Count==0 && I(req["seed"])==0 && B(req["isolateRecipeRandom"]),"Restart is only allowed as the seed-zero isolated prefix start.");
            if(command=="preview") { Need(phase=="prefix","Preview is not part of matched setup/branch."); Verification.ValidatePreview(req,res); }
            if(command=="step") { Need(I(req["steps"])==1,"Each captured input boundary must be one native frame.");ValidatePads(req,res,phase=="branch"?0:phase=="setup"?3:null); }
            Need(s["scene"]?.ToString()=="s_Day_3_4" && I(s["score"])==0 && I(s["delivered"])==0 && I(s["deductions"])==0,"Scene, score, delivery or deduction changed.");
            Need(B(s["gameEventsInstalled"]) && I(s["gameEventsDropped"])==0,"Missing native event observer or dropped evidence.");
            foreach(var ev in s["gameEvents"]!.AsArray().OfType<JsonObject>())
                Need(ev["kind"]?.ToString()=="input_release" && B(ev["inputsNeutral"]) && new[]{"scoreDelta","baseScoreDelta","tipDelta","deductionDelta","deliveryDelta"}.All(k=>I(ev[k])==0),"Unexpected native scoring/delivery event; only observed neutral connection-release metadata is permitted.");
            var ins=s["instrumentation"]!.AsObject();string plugin=ins["manifest"]!["pluginSha256"]!.ToString(),manifest=ins["manifestSha256"]!.ToString();
            if(t.Requests.Count==0)
            {
                t.First=res.DeepClone().AsObject();t.Plugin=plugin;t.Instrumentation=manifest;
                var session=JsonNode.Parse(res["session"]!.ToString())!.AsObject();
                Need(I(session["dlc"])==8 && I(session["variantPlayers"])==4 && I(session["serverUsers"])==4 && I(session["clientUsers"])==4 && I(session["virtualPads"])==4,"Missing native four-local-player Carnival session proof.");
                Verification.ValidateInstrumentation(s,0,true);
                ordinals=FixedIds.ToDictionary(id=>id,id=>I(E(s,id)["observedOrdinal"]));
            }
            Need(plugin==t.Plugin && manifest==t.Instrumentation && B(ins["nativePhysicsAutoSimulation"]) && B(ins["inputActive"]),"Instrumentation/input/native physics configuration changed.");
            Need(N(s["fixedDeltaTime"])==.02 && I(s["captureFramerate"])==60 && !B(s["timerSuppressed"]),"Native fixed-step/capture/timer configuration changed.");
            Need(s["chefs"]!.AsArray().OfType<JsonObject>().Select(c=>I(c["playerId"])).Order().SequenceEqual(new[]{0,1,2,3}),"Native four-chef identity set changed.");
            foreach(int id in FixedIds)Need(I(E(s,id)["observedOrdinal"])==ordinals![id],"Reserved/station/vessel native identity changed: "+id);
            foreach(var entity in s["entities"]!.AsArray().OfType<JsonObject>().Where(e=>e["composition"] is JsonObject))
                Need(!CarnivalRecipes.ClassifyEntity(entity).Food.IsRuined,"Native ruined food observed on entity "+entity["id"]);
            if(previous is not null)
            {
                var p=previous["state"]!.AsObject();int advance=command=="step"?1:0;
                Need(I(s["gameplayFrame"])==I(p["gameplayFrame"])+advance,"Native sample alignment gap or unrecorded advancing call.");
                if(advance==1)
                {
                    Need(N(s["clientTime"])>N(p["clientTime"]) && N(s["clientTime"])-N(p["clientTime"])<.05 && N(s["timer"])<N(p["timer"]),"Native processing/round clock failed to advance normally.");
                    Need(I(s["gameplayFixedFrame"])==I(p["gameplayFixedFrame"])+I(s["physicsStepsThisFrame"]) && I(s["physicsStepsThisFrame"]) is 0 or 1,"Native physics phase/count discontinuity.");
                    if(phase=="branch") t.Travel+=Distance(C(p,0)["position"]!,C(s,0)["position"]!);
                }
            }
            if(phase is "setup" or "branch") ObserveExperiment(t,res,previous,temp,phase);
            t.Requests.Add(req.DeepClone().AsObject());
            t.Stamps.Add(new(I(s["gameplayFrame"]),Hash(Projection(s)),Hash(Projection(s,true)),Hash(Pick(s,ClockFields))));
            previous=res;t.Last=res;
        }
        Need(t.Requests.Count>0 && AllNeutral(t.Last),"Empty trace or final inputs not neutral.");
        if(phase=="prefix")Need(t.Requests[0]["command"]?.ToString()=="restart" && I(t.Last["state"]!["gameplayFrame"])==1347,"Prefix did not finish at explicit neutral GF1347.");
        return t;
    }
    static double Distance(JsonNode a,JsonNode b) => Math.Sqrt(Math.Pow(N(a["x"])-N(b["x"]),2)+Math.Pow(N(a["z"])-N(b["z"]),2));
    static void ObserveExperiment(Trace t,JsonObject res,JsonObject? previous,int temp,string phase)
    {
        var s=res["state"]!.AsObject();int frame=I(s["gameplayFrame"]);var p=previous?["state"]?.AsObject();
        foreach(int pot in new[]{2,7})
        {
            double progress=N(E(s,pot)["cookingProgress"]);Need(progress>=0 && progress<24 && N(E(s,pot)["cookingTime"])==12,"Pot reached native burn deadline or configuration differs.");
            t.MaximumPotProgress[pot]=Math.Max(t.MaximumPotProgress.GetValueOrDefault(pot),progress);
        }
        foreach(int bowl in new[]{3,6})
        {
            bool off=I(E(s,14)["attachedEntityId"])!=bowl && I(E(s,18)["attachedEntityId"])!=bowl;
            if(!off) { Need(!t.OffMix.ContainsKey(bowl),"An observed offmixer bowl returned to an active mixer.");continue; }
            var e=E(s,bowl);double progress=N(e["mixingProgress"]);string food=Hash(e["composition"]);
            Need(progress>=0 && progress<24,"Offmixer bowl has invalid or ruined native progress.");
            if(t.OffMix.TryGetValue(bowl,out var first)) { Need(progress==first.Progress && food==first.Food,"Offmixer progress/composition changed: bowl "+bowl);t.OffMix[bowl]=(progress,food,first.Frame,first.Samples+1); }
            else t.OffMix[bowl]=(progress,food,frame,1);
        }
        if(phase!="branch")return;
        Need(t.OffMix.Count==2 && I(E(s,33)["attachedEntityId"])==6 && I(C(s,3)["heldEntityId"])==3,"Common setup offmixer placement/held identity was not preserved throughout branch.");
        Need(new[]{1,2,3}.All(player=>Neutral(Pad(res,player))) && I(C(s,1)["heldEntityId"])==0 && I(C(s,2)["heldEntityId"])==0,"A non-participant changed held state or input.");
        var plate=E(s,13);Need(I(plate["observedOrdinal"])==12 && B(plate["active"]),"Original plate identity vanished.");
        var observed=CarnivalRecipes.ClassifyEntity(plate);Need(observed.EvidenceGaps.Length==0 && observed.IsPlate,"Native food-tree plate evidence is incomplete.");
        if(CarnivalRecipes.MatchRecipe(plate,296560).ReadyToDeliver && t.BaseFrame<0)t.BaseFrame=frame;
        if(p is not null)
        {
            var before=CarnivalRecipes.ClassifyEntity(E(p,13));var old=before.Food.IngredientIds;var now=observed.Food.IngredientIds;
            var added=now.Except(old).ToArray();
            foreach(int sauce in added.Where(x=>x==17094||x==158482))
            {
                int index=sauce==17094?0:1;
                Need(added.Length==1 && I(C(p,0)["heldEntityId"])==13 && I(C(s,0)["heldEntityId"])==13 && I(C(p,0)["placementTargetId"])==72 && B(Pad(res,0)["pickup"]) && !B(Pad(previous!,0)["pickup"]),"Sauce food delta lacks exact held plate, native target and pickup edge.");
                Need(I(E(p,72)["switchIndex"])==index && I(E(s,72)["switchIndex"])==index && CarnivalRecipes.ClassifyEntity(E(s,72)).Food.IngredientIds.Contains(sauce),"Sauce delta differs from native dispenser selection.");
                int recipe=sauce==17094?158500:125780;Need(CarnivalRecipes.MatchRecipe(plate,recipe).ReadyToDeliver,"Added sauce is not the exact complete native recipe.");
                if(sauce==17094) {Need(t.BaseFrame>=0 && t.MustardFrame<0 && t.KetchupFrame<0,"Unexpected/repeated mustard transition.");t.MustardFrame=frame;}
                else {Need(t.MustardFrame>=0 && t.KetchupFrame<0 && t.SwitchFrame>=0 && t.Parked.Contains(temp),"Ketchup applied without prior mustard, native switch and selected parking.");t.KetchupFrame=frame;}
                t.SauceTransitions.Add(new JsonObject{["frame"]=frame,["plateId"]=13,["plateObservedOrdinal"]=12,["ingredientId"]=sauce,["dispenserId"]=72,["switchIndex"]=index,["recipeId"]=recipe,["nativePickupEdge"]=true,["compositionSha256"]=Hash(plate["composition"])});
            }
            if(I(E(p,72)["switchIndex"])==0 && I(E(s,72)["switchIndex"])==1)
            {
                Need(I(C(p,0)["heldEntityId"])==0 && I(C(p,0)["useTargetId"])==79 && B(Pad(res,0)["use"]) && !B(Pad(previous!,0)["use"]),"Condiment switch change lacks empty hands, native button target and use edge.");t.SwitchFrame=frame;
            }
        }
        if(I(E(s,temp)["attachedEntityId"])==13 && CarnivalRecipes.MatchRecipe(plate,158500).ReadyToDeliver)t.Parked.Add(temp);
    }
    static void SetupFinished(Trace t)
    {
        var s=t.Last["state"]!.AsObject();Need(I(E(s,33)["attachedEntityId"])==6 && I(C(s,3)["heldEntityId"])==3 && I(E(s,14)["attachedEntityId"])==0 && I(E(s,18)["attachedEntityId"])==0,"Setup did not park bowl6/take bowl3/empty native mixers.");
        Need(t.OffMix.Count==2 && t.OffMix.Values.All(v=>v.Samples>=3),"Setup lacks two advancing offmixer stability observations.");
        Need(s["chefs"]!.AsArray().OfType<JsonObject>().All(c=>B(c["controlsEnabled"])) && new[]{0,1,2}.All(i=>I(C(s,i)["heldEntityId"])==0),"Setup chefs are not controlled with the expected held items.");
        Need(I(E(s,37)["attachedEntityId"])==151 && I(E(s,40)["attachedEntityId"])==0 && I(E(s,49)["attachedEntityId"])==0 && I(E(s,43)["attachedEntityId"])==13,"Setup changed source/plate/parking/output prerequisites.");
        Need(CarnivalRecipes.MatchRecipe(E(s,151),296560,false).ReadyToDeliver && CarnivalRecipes.ClassifyEntity(E(s,13)).Food.IngredientIds.Length==0,"Setup base meal or empty plate differs.");
        Need(new[]{2,7}.All(pot=>24-N(E(s,pot)["cookingProgress"])>15),"Setup leaves insufficient actual pot deadline budget for the 900-frame branch.");
    }
    static void SetupAuthorship(Trace t,JsonObject plan)
    {
        var authored=plan["jobs"]![0]!["actions"]!.AsArray();
        var made=t.Events.Where(e=>e["name"]?.ToString()=="actionCreated").Select(e=>e["value"]!.DeepClone().AsObject()).ToArray();
        Need(made.Length==authored.Count,"Captured setup does not contain every authored action.");
        for(int i=0;i<made.Length;i++){Need(I(made[i]["player"])==3,"Setup action belongs to another player.");made[i].Remove("player");Need(JsonNode.DeepEquals(made[i],authored[i]),"Captured setup action differs from corrected authored plan.");}
        var waits=t.Events.Where(e=>e["name"]?.ToString()=="actionComplete"&&e["value"]?["action"]?["type"]?.ToString()=="wait").ToArray();
        Need(waits.Select(e=>I(e["value"]!["frames"])).SequenceEqual(new[]{18,2}),"Captured native setup did not complete the effective 18/2-frame waits.");
    }
    static void Boundary(Trace a,Trace b)
    {
        var x=a.Last["state"]!.AsObject();var y=b.First["state"]!.AsObject();
        Need(AllNeutral(a.Last)&&AllNeutral(b.First),"Trace boundary is not explicitly neutral.");
        Need(JsonNode.DeepEquals(Pick(x,ClockFields),Pick(y,ClockFields))&&JsonNode.DeepEquals(Projection(x),Projection(y))&&JsonNode.DeepEquals(Projection(x,true),Projection(y,true)),"Unrecorded state/clock/physics change at adjacent trace boundary.");
    }
    static JsonObject Summary(Trace t) => new(){["file"]=FileProof(t.Path),["requests"]=t.Requests.Count,["steps"]=t.Requests.Count(r=>r["command"]?.ToString()=="step"),["firstFrame"]=I(t.First["state"]!["gameplayFrame"]),["lastFrame"]=I(t.Last["state"]!["gameplayFrame"]),["nativeElapsedSeconds"]=N(t.Last["state"]!["clientTime"])-N(t.First["state"]!["clientTime"]),["timerElapsedSeconds"]=N(t.First["state"]!["timer"])-N(t.Last["state"]!["timer"]),["pluginSha256"]=t.Plugin,["instrumentationManifestSha256"]=t.Instrumentation,["offmix"]=new JsonArray(t.OffMix.OrderBy(p=>p.Key).Select(p=>(JsonNode)new JsonObject{["id"]=p.Key,["mixingProgress"]=p.Value.Progress,["compositionSha256"]=p.Value.Food,["firstFrame"]=p.Value.Frame,["samples"]=p.Value.Samples}).ToArray()),["maximumPotProgress"]=JsonSerializer.SerializeToNode(t.MaximumPotProgress),["sauceTransitions"]=t.SauceTransitions.DeepClone(),["chef0PlanarTravelMeters"]=t.Travel};
    sealed record Arm(Trace Prefix,Trace Setup,Trace Branch,JsonObject Result);
    static Arm CheckArm(JsonObject config,int temp,JsonObject[] prefixMovie,JsonObject[] setupMovie)
    {
        Console.Error.WriteLine("Checking source/temporary counter "+temp);
        var prefix=AuditTrace(config["prefixTrace"]!.ToString(),"prefix");SameRequests(prefixMovie,prefix.Requests,"Immutable prefix");
        var setup=AuditTrace(config["setupTrace"]!.ToString(),"setup");SameRequests(setupMovie,setup.Requests,"Captured common setup");Boundary(prefix,setup);SetupFinished(setup);
        var branch=AuditTrace(config["trace"]!.ToString(),"branch",temp);Boundary(setup,branch);
        Need(new[]{prefix,setup,branch}.All(t=>t.Plugin==prefix.Plugin&&t.Instrumentation==prefix.Instrumentation),"Plugin changed between experiment stages.");
        var result=Read(config["result"]!.ToString());Need(B(result["ok"])&&B(result["paused"])&&JsonNode.DeepEquals(result["state"],branch.Last["state"]),"Closed branch result differs from final trace.");
        var final=branch.Last["state"]!.AsObject();Need(branch.BaseFrame>=0 && branch.MustardFrame>branch.BaseFrame && branch.KetchupFrame>branch.MustardFrame && branch.SwitchFrame>branch.MustardFrame,"Native base→mustard→switch→ketchup timeline incomplete.");
        Need(I(E(final,49)["attachedEntityId"])==13 && I(E(final,temp)["attachedEntityId"])==0 && CarnivalRecipes.MatchRecipe(E(final,13),125780).ReadyToDeliver && !final["entities"]!.AsArray().OfType<JsonObject>().Any(e=>I(e["id"])==151&&B(e["active"])) && I(C(final,0)["heldEntityId"])==0,"Final output is not the exact dual-sauce original plate, consumed source and empty temporary station.");
        Need(branch.Requests.Count(r=>r["command"]?.ToString()=="step")<=900,"Branch exceeded authored native frame budget.");
        var expected=Read(config["plan"]!.ToString())["jobs"]![0]!["actions"]!.AsArray();
        var created=branch.Events.Where(e=>e["name"]?.ToString()=="actionCreated").Select(e=>e["value"]!.DeepClone().AsObject()).ToArray();
        Need(created.Length==expected.Count,"Branch action creation count differs from authored plan.");
        for(int i=0;i<created.Length;i++){Need(I(created[i]["player"])==0,"Branch action belongs to another chef.");created[i].Remove("player");Need(JsonNode.DeepEquals(created[i],expected[i]),"Branch created an unauthored action.");}
        Need(branch.Events.Count(e=>e["name"]?.ToString()=="actionComplete")==expected.Count && branch.Events.Count(e=>e["name"]?.ToString()=="jobComplete")==1,"Missing completed native branch actions/job.");
        return new(prefix,setup,branch,result);
    }
    static JsonObject CompareStamps(Trace a,Trace b)
    {
        Need(a.Stamps.Count==b.Stamps.Count,"Matched trace sample count differs.");
        int First(Func<Stamp,string> f)=>Enumerable.Range(0,a.Stamps.Count).FirstOrDefault(i=>f(a.Stamps[i])!=f(b.Stamps[i]),-1);
        JsonNode? At(int i)=>i<0?null:new JsonObject{["requestIndex"]=i,["aFrame"]=a.Stamps[i].Frame,["bFrame"]=b.Stamps[i].Frame};
        return new(){["samples"]=a.Stamps.Count,["firstFoodControlStateDifference"]=At(First(x=>x.Discrete)),["firstPhysicalDifference"]=At(First(x=>x.Physical)),["firstClockDifference"]=At(First(x=>x.Clock)),["comparisonUsesNoNumericTolerance"]=true};
    }
    static JsonArray Differences(JsonNode? a,JsonNode? b,string path="",int limit=40)
    {
        var list=new JsonArray();void Walk(JsonNode? x,JsonNode? y,string p)
        {
            if(list.Count>=limit||JsonNode.DeepEquals(x,y))return;
            if(x is JsonObject xo&&y is JsonObject yo){foreach(string key in xo.Select(v=>v.Key).Union(yo.Select(v=>v.Key)).Order(StringComparer.Ordinal)){if(!xo.ContainsKey(key)||!yo.ContainsKey(key)){list.Add(new JsonObject{["path"]=p+"."+key,["aPresent"]=xo.ContainsKey(key),["bPresent"]=yo.ContainsKey(key)});if(list.Count>=limit)return;}else Walk(xo[key],yo[key],p+"."+key);}return;}
            if(x is JsonArray xa&&y is JsonArray ya&&xa.Count==ya.Count){for(int i=0;i<xa.Count;i++)Walk(xa[i],ya[i],p+"["+i+"]");return;}
            list.Add(new JsonObject{["path"]=p,["a"]=x?.DeepClone(),["b"]=y?.DeepClone()});
        }Walk(a,b,path);return list;
    }
    static JsonObject CompareStarts(Trace a,Trace b)
    {
        var x=a.First["state"]!.AsObject();var y=b.First["state"]!.AsObject();var dx=Projection(x);var dy=Projection(y);var px=Projection(x,true);var py=Projection(y,true);
        return new(){["foodControlsExactlyEqual"]=JsonNode.DeepEquals(dx,dy),["physicalExactlyEqual"]=JsonNode.DeepEquals(px,py),["clocksExactlyEqual"]=JsonNode.DeepEquals(Pick(x,ClockFields),Pick(y,ClockFields)),["entireRawStateExactlyEqual"]=JsonNode.DeepEquals(x,y),["aRawStateSha256"]=Hash(x),["bRawStateSha256"]=Hash(y),["foodControlDifferencesFirst40"]=Differences(dx,dy),["physicalDifferencesFirst40"]=Differences(px,py),["clockDifferences"]=Differences(Pick(x,ClockFields),Pick(y,ClockFields)),["rawStateDifferencesFirst40"]=Differences(x,y),["chefPositionDistances"]=new JsonArray(Enumerable.Range(0,4).Select(i=>(JsonNode)new JsonObject{["player"]=i,["xzMeters"]=Distance(C(x,i)["position"]!,C(y,i)["position"]!)}).ToArray()),["comparisonUsesNoNumericTolerance"]=true};
    }
    static JsonObject SelfTest()
    {
        var checks=new List<string>();void Check(bool value,string name){Need(value,"Checker regression: "+name);checks.Add(name);}
        void Reject(Action action,string name){bool rejected=false;try{action();}catch(InvalidDataException){rejected=true;}Check(rejected,name);}
        var setup=Read("artifacts/sauce-staging-a-setup-result.json");
        Trace Setup(JsonObject response){var t=new Trace{Last=response};foreach(int id in new[]{3,6})t.OffMix[id]=(N(E(response["state"]!.AsObject(),id)["mixingProgress"]),Hash(E(response["state"]!.AsObject(),id)["composition"]),1428,6);return t;}
        SetupFinished(Setup(setup));Check(true,"actual native setup passes exact bowl/base/plate/deadline prerequisites");
        foreach(string defect in new[]{"wrong-park","wrong-held-bowl","mixer-reattached","plate-occupied","base-missing","near-occupied","insufficient-pot-budget","disabled-chef","missing-stability"})
        {
            var bad=setup.DeepClone().AsObject();var s=bad["state"]!.AsObject();var t=Setup(bad);
            switch(defect){case "wrong-park":E(s,33)["attachedEntityId"]=3;break;case "wrong-held-bowl":C(s,3)["heldEntityId"]=6;break;case "mixer-reattached":E(s,14)["attachedEntityId"]=3;break;case "plate-occupied":E(s,13)["composition"]=E(s,151)["composition"]!.DeepClone();break;case "base-missing":E(s,37)["attachedEntityId"]=0;break;case "near-occupied":E(s,40)["attachedEntityId"]=13;break;case "insufficient-pot-budget":E(s,2)["cookingProgress"]=9;break;case "disabled-chef":C(s,3)["controlsEnabled"]=false;break;case "missing-stability":t.OffMix[3]=(10,"x",1433,1);break;}
            Reject(()=>SetupFinished(t),defect);
        }
        Boundary(new Trace{Last=setup},new Trace{First=setup});Check(true,"equal same-run boundary accepted");
        foreach(string field in new[]{"timer","gameplayFrame"}){var bad=setup.DeepClone().AsObject();bad["state"]![field]=N(bad["state"]![field])+1;Reject(()=>Boundary(new Trace{Last=setup},new Trace{First=bad}),"unrecorded boundary "+field);}
        var physical=setup.DeepClone().AsObject();C(physical["state"]!.AsObject(),0)["position"]!["x"]=N(C(physical["state"]!.AsObject(),0)["position"]!["x"])+.000001;Reject(()=>Boundary(new Trace{Last=setup},new Trace{First=physical}),"tiny physical change is not tolerance-hidden");
        Check(!JsonNode.DeepEquals(JsonNode.Parse("true"),JsonNode.Parse("1")),"boolean differs from numeric input");
        Check(Differences(JsonNode.Parse("{\"x\":null}"),new JsonObject()).Count==1,"missing property differs from null");
        Check(JsonNode.DeepEquals(JsonNode.Parse("1"),JsonNode.Parse("1.0")),"numerically equal JSON spelling preserves numeric semantics");
        var req=new JsonObject{["version"]=1,["command"]="step",["steps"]=1};SameRequests([req],[req],"fixture");Check(true,"equal original requests accepted");
        var changed=req.DeepClone().AsObject();changed["steps"]=2;Reject(()=>SameRequests([req],[changed],"fixture"),"changed step boundary rejected");Reject(()=>SameRequests([req],[],"fixture"),"missing captured request rejected");
        var transition=new Dictionary<int,(JsonObject Before,JsonObject After)>();JsonObject? previous=null;
        foreach(var row in Rows("artifacts/sauce-staging-a-branch.jsonl.gz"))
        {
            if(row["kind"]?.ToString()!="call")continue;var response=row["response"]!.AsObject();int frame=I(response["state"]!["gameplayFrame"]);
            if(frame is 1609 or 2017)transition[frame]=(previous!.DeepClone().AsObject(),response.DeepClone().AsObject());previous=response;
        }
        foreach(var pair in transition)
        {
            Trace Prior(){var t=new Trace{BaseFrame=1511,MustardFrame=pair.Key==2017?1609:-1,SwitchFrame=pair.Key==2017?1900:-1};t.Parked.Add(37);return t;}
            ObserveExperiment(Prior(),pair.Value.After,pair.Value.Before,37,"branch");Check(true,"actual native sauce delta/edge passes GF"+pair.Key);
            foreach(string defect in new[]{"no-pickup","held-mismatch","target-mismatch","switch-mismatch","reused-plate","not-an-edge","wrong-food-prep"})
            {
                var after=pair.Value.After.DeepClone().AsObject();var before=pair.Value.Before.DeepClone().AsObject();var s=after["state"]!.AsObject();
                switch(defect){case "no-pickup":Pad(after,0)["pickup"]=false;break;case "held-mismatch":C(s,0)["heldEntityId"]=0;break;case "target-mismatch":C(before["state"]!.AsObject(),0)["placementTargetId"]=79;break;case "switch-mismatch":E(s,72)["switchIndex"]=2;break;case "reused-plate":E(s,13)["observedOrdinal"]=999;break;case "not-an-edge":Pad(before,0)["pickup"]=true;break;case "wrong-food-prep":foreach(var node in E(s,13)["composition"]!["children"]!.AsArray().OfType<JsonObject>())if(node["type"]?.ToString()=="CookedCompositeAssembledNode")node["state"]="Raw";break;}
                Reject(()=>ObserveExperiment(Prior(),after,before,37,"branch"),defect+" GF"+pair.Key);
            }
        }
        var stable=PriorOffmix(setup);var changedMix=setup.DeepClone().AsObject();E(changedMix["state"]!.AsObject(),3)["mixingProgress"]=10.95;Reject(()=>ObserveExperiment(stable,changedMix,null,37,"setup"),"partial Unmixed offmixer progress drift rejected");
        return new JsonObject{["ok"]=true,["count"]=checks.Count,["checks"]=JsonSerializer.SerializeToNode(checks),["classification"]="Offline checker regressions based on captured native setup and sauce transitions; mutated cases are synthetic and not native runs.",["nativeFixtureTrace"]=FileProof("artifacts/sauce-staging-a-branch.jsonl.gz"),["checkerSource"]=FileProof("scripts/SauceABProbeCheck/Program.cs")};
    }
    static Trace PriorOffmix(JsonObject response){var t=new Trace();foreach(int id in new[]{3,6})t.OffMix[id]=(N(E(response["state"]!.AsObject(),id)["mixingProgress"]),Hash(E(response["state"]!.AsObject(),id)["composition"]),1433,1);return t;}
    public static int Run(string[] args)
    {
        JsonObject report=new();int exit=0;
        try
        {
            Need(args.Length==2,"Usage: SauceABProbeCheck CONFIG.json NEW_REPORT.json");Need(!File.Exists(args[1]),"Output exists; choose a new report path.");
            if(args[0]=="selftest"){report=SelfTest();string testText=report.ToJsonString(new(){WriteIndented=true});File.WriteAllText(args[1],testText+Environment.NewLine);Console.WriteLine(testText);return 0;}
            var config=Read(args[0]);string bundle=config["controllerBundle"]!.ToString();string dll=Path.Combine(bundle,"OvercookedTAS.Controller.dll");var manifest=Read(Path.Combine(bundle,"manifest.json"));
            Need(FileHash(dll)==FrozenHash && FileHash(typeof(CarnivalRecipes).Assembly.Location)==FrozenHash && manifest["controllerSha256"]?.ToString()==FrozenHash,"Checker must use the frozen V13 classifier/build.");
            var sourceFiles=manifest["sourceFiles"]!.AsArray().OfType<JsonObject>().ToArray();foreach(var f in sourceFiles)Need(FileHash(Path.Combine(bundle,"source",f["path"]!.ToString()))==f["sha256"]?.ToString(),"Frozen source manifest differs.");
            var aPlan=Read(config["baseline"]!["plan"]!.ToString());var bPlan=Read(config["near"]!["plan"]!.ToString());var aa=aPlan["jobs"]![0]!["actions"]!.AsArray();var bb=bPlan["jobs"]![0]!["actions"]!.AsArray();
            Need(aa.Count==9&&bb.Count==9,"Expected nine identical serial native actions.");for(int i=0;i<9;i++){var actionB=bb[i]!.DeepClone().AsObject();if(i is 4 or 6){Need(aa[i]!["station"]?.ToString()=="37"&&actionB["station"]?.ToString()=="40","Unexpected temporary counter change.");actionB["station"]="37";}Need(JsonNode.DeepEquals(aa[i],actionB),"Branches differ beyond permitted temporary counter place/take.");}
            var setupPlan=Read(config["setupPlan"]!.ToString());var waits=setupPlan["jobs"]![0]!["actions"]!.AsArray().OfType<JsonObject>().Where(a=>a["type"]?.ToString()=="wait").ToArray();Need(waits.Select(a=>I(a["durationFrames"])).SequenceEqual(new[]{18,2})&&waits.All(a=>a["frames"]is null),"Setup effective wait durations are not 18 and 2.");
            var prep=Read("artifacts/sauce-staging-probe-preparation.json");foreach(var f in prep["files"]!.AsObject())Need(FileHash(f.Key)==f.Value!["sha256"]?.ToString(),"Prepared probe hash changed.");
            Need(FileHash(config["prefixMovie"]!.ToString())=="353bdadcb69881c5de31ac6ed33c290160f2f02b7e11729a94f147709b2f76df","Prefix differs from independently prepared preserved movie.");
            var prefixMovie=Requests(config["prefixMovie"]!.ToString());var setupMovie=Requests(config["setupMovie"]!.ToString());
            var a=CheckArm(config["baseline"]!.AsObject(),37,prefixMovie,setupMovie);SetupAuthorship(a.Setup,setupPlan);report["baseline"]=new JsonObject{["prefix"]=Summary(a.Prefix),["setup"]=Summary(a.Setup),["branch"]=Summary(a.Branch)};
            var b=CheckArm(config["near"]!.AsObject(),40,prefixMovie,setupMovie);report["near"]=new JsonObject{["prefix"]=Summary(b.Prefix),["setup"]=Summary(b.Setup),["branch"]=Summary(b.Branch)};
            Need(a.Prefix.Plugin==b.Prefix.Plugin&&a.Prefix.Instrumentation==b.Prefix.Instrumentation,"Branches ran different native instrumentation.");
            var starts=CompareStarts(a.Branch,b.Branch);report["matchedBranchStart"]=starts;
            report["prefixComparison"]=CompareStamps(a.Prefix,b.Prefix);report["setupComparison"]=CompareStamps(a.Setup,b.Setup);
            int delta=I(a.Branch.Last["state"]!["gameplayFrame"])-I(a.Branch.First["state"]!["gameplayFrame"])-I(b.Branch.Last["state"]!["gameplayFrame"])+I(b.Branch.First["state"]!["gameplayFrame"]);
            bool exact=B(starts["foodControlsExactlyEqual"])&&B(starts["physicalExactlyEqual"])&&B(starts["clocksExactlyEqual"]);
            report["ok"]=true;report["nativeBranchesValid"]=true;report["exactRelevantStartingStateMatch"]=exact;
            report["classification"]=exact?"Matched native serial sauce experiment; independent component inputs identical, exact compared starting state equal.":"Both native serial branches valid with identical prefix/setup requests; starting-state drift is explicitly reported. Timing delta is observed, not proof of identical raw conditions.";
            report["observedBaselineMinusNearFrames"]=delta;report["observedBaselineMinusNearSecondsAt60Fps"]=delta/60.0;
            report["nativeSavingsGeneralization"]= "A two-branch diagnostic does not establish full-planner throughput, repeated-run confidence or a high score.";
            report["provenance"]=new JsonObject{["config"]=FileProof(args[0]),["prefixMovie"]=FileProof(config["prefixMovie"]!.ToString()),["setupMovie"]=FileProof(config["setupMovie"]!.ToString()),["setupPlan"]=FileProof(config["setupPlan"]!.ToString()),["baselinePlan"]=FileProof(config["baseline"]!["plan"]!.ToString()),["nearPlan"]=FileProof(config["near"]!["plan"]!.ToString()),["baselineResult"]=FileProof(config["baseline"]!["result"]!.ToString()),["nearResult"]=FileProof(config["near"]!["result"]!.ToString()),["controller"]=FileProof(dll),["controllerManifest"]=FileProof(Path.Combine(bundle,"manifest.json")),["verifiedSourceFiles"]=sourceFiles.Length,["checkerSource"]=FileProof("scripts/SauceABProbeCheck/Program.cs"),["checkerProject"]=FileProof("scripts/SauceABProbeCheck/SauceABProbeCheck.csproj"),["controllerExecutionAttribution"]="Operator identified frozen V13; trace headers attest controller version, not a process DLL measurement. The checker independently hashes the frozen classifier and its source manifest."};
        }
        catch(Exception error){report["ok"]=false;report["error"]=error.Message;report["detail"]=error.ToString();exit=1;}
        string text=report.ToJsonString(new(){WriteIndented=true});if(args.Length==2&&!File.Exists(args[1]))File.WriteAllText(args[1],text+Environment.NewLine);Console.WriteLine(text);return exit;
    }
}
