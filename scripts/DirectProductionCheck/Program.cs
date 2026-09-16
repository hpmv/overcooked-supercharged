using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using OvercookedTAS.Controller;

int I(JsonNode? n)=>n is null?0:int.Parse(n.ToString());
double N(JsonNode? n)=>n is null?double.NaN:double.Parse(n.ToString(),System.Globalization.CultureInfo.InvariantCulture);
bool B(JsonNode? n)=>n?.GetValue<bool>()==true;
void Need(bool good,string message){if(!good)throw new InvalidOperationException(message);}
JsonObject E(JsonObject s,int id)=>s["entities"]!.AsArray().OfType<JsonObject>().Single(e=>I(e["id"])==id);
JsonObject C(JsonObject s,int p)=>s["chefs"]!.AsArray().OfType<JsonObject>().Single(c=>I(c["playerId"])==p);
JsonObject Pad(JsonObject r,int p)=>r["inputs"]!.AsArray().OfType<JsonObject>().Single(x=>I(x["player"])==p);
int Held(JsonObject s,int player)=>I(C(s,player)["heldEntityId"]);
int Parent(JsonObject s,int item)=>s["entities"]!.AsArray().OfType<JsonObject>().Where(e=>I(e["attachedEntityId"])==item).Select(e=>I(e["id"])).SingleOrDefault();
FoodObservation Food(JsonObject s,int id)=>CarnivalRecipes.ClassifyEntity(E(s,id)).Food;
bool Empty(JsonObject s,int id)=>Food(s,id).IngredientIds.Length==0;
bool Dough(JsonObject s,int id,int recipe)=>!Food(s,id).IsRuined&&Food(s,id).IngredientIds.Order().SequenceEqual(CarnivalRecipes.GetRecipe(recipe).RequiredInputs.Select(x=>x.Id).Order())&&Food(s,id).DescendantsAndSelf().Any(f=>f.Kind==FoodNodeKind.Mixed&&f.Preparation==FoodPreparation.Mixed);
string Hash(string path){using var stream=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();}
string PrefixHash(string path,long length){using var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite);using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);byte[] block=new byte[1024*1024];while(length>0){int read=f.Read(block,0,(int)Math.Min(block.Length,length));Need(read>0,"Captured compressed prefix was truncated.");hash.AppendData(block,0,read);length-=read;}return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();}
JsonObject report=new();var records=new List<Proof>();var recipes=new Dictionary<int,int>();int calls=0,lastFrame=-1;long bytes=0;string? tailError=null;
JsonObject? lastResponse=null;string? lastCall=null;
try
{
    Need(args.Length is 2 or 3,"Usage: TRACE NEW_REPORT [MAX_GAMEPLAY_FRAME]");Need(!File.Exists(args[1]),"Report already exists.");
    int limit=args.Length==3?int.Parse(args[2]):int.MaxValue;
    Need(Hash(typeof(CarnivalRecipes).Assembly.Location)=="0afc09032339fb9a69c006bd303bc3bf45862c433afb3bed07fc62253d086404","Checker must use frozen58-test V14 classifier.");
    using var file=new FileStream(args[0],FileMode.Open,FileAccess.Read,FileShare.ReadWrite);bytes=file.Length;
    using var bounded=new PrefixStream(file,bytes);using var gzip=new GZipStream(bounded,CompressionMode.Decompress);using var reader=new StreamReader(gzip);
    while(true)
    {
        string? line;try{line=reader.ReadLine();}catch(InvalidDataException ex){tailError=ex.Message;break;}if(line is null)break;
        JsonDocument document;try{document=JsonDocument.Parse(line);}catch(JsonException ex){tailError=ex.Message;break;}
        using(document)
        {
            var root=document.RootElement;string kind=root.GetProperty("kind").GetString()!;
            if(kind=="header")continue;
            if(kind=="call")
            {
                var native=root.GetProperty("response").GetProperty("state");int frame=native.GetProperty("gameplayFrame").GetInt32();if(frame>limit)break;
                lastFrame=frame;calls++;lastCall=line;
                if(root.GetProperty("request").GetProperty("command").GetString()=="preview")
                {
                    var response=JsonNode.Parse(root.GetProperty("response").GetRawText())!.AsObject();var request=JsonNode.Parse(root.GetProperty("request").GetRawText())!.AsObject();Verification.ValidatePreview(request,response);
                    foreach(var r in response["preview"]!["recipes"]!.AsArray().OfType<JsonObject>())recipes[I(r["index"])]=I(r["recipeId"]);
                }
                if(!records.Any(p=>!p.Completed))continue;
                var res=JsonNode.Parse(root.GetProperty("response").GetRawText())!.AsObject();
                Need(B(res["ok"])&&B(res["paused"]),"Native call failed or escaped pause gate during direct Work.");
                foreach(var proof in records.Where(p=>!p.Completed))Observe(proof,res,lastResponse);
                lastResponse=res;continue;
            }
            if(kind!="event")continue;
            string name=root.GetProperty("name").GetString()!;
            if(name is "nativeDirectEarlyOnionAdmitted" or "nativeNearReadyDoughAdmitted")
            {
                Need(lastCall is not null,"Admission lacks a preceding native snapshot.");
                var v=JsonNode.Parse(root.GetProperty("value").GetRawText())!.AsObject();var row=JsonNode.Parse(lastCall!)!.AsObject();var res=row["response"]!.AsObject();var s=res["state"]!.AsObject();
                var p=new Proof{Kind=name=="nativeDirectEarlyOnionAdmitted"?"onion":"dough",Player=I(v["player"]),Start=I(v["frame"]),Index=I(v[name=="nativeDirectEarlyOnionAdmitted"?"mealIndex":"recipeIndex"])};
                Need(p.Start==I(s["gameplayFrame"]),"Admission is not aligned to an actual native sample.");
                if(p.Kind=="onion")
                {
                    p.Vessel=I(v["pan"]);p.Home=I(v["home"]);p.Food=I(v["food"]);p.Source=I(v["source"]);p.Counter=I(v["unusedParkingCounter"]);p.Recipe=472326;
                    Need(Held(s,p.Player)==0&&Parent(s,p.Vessel)==p.Home&&Parent(s,p.Food)==p.Source&&CarnivalRecipes.MatchRecipe(E(s,p.Food),296560,false).ReadyToDeliver&&I(E(s,p.Counter)["attachedEntityId"])==0,"Direct-onion admission lacks the exact plain source or loaded home pan.");
                    p.Ids=[p.Vessel,p.Home,p.Food,p.Source,p.Counter];
                }
                else
                {
                    p.Vessel=I(v["bowl"]);p.Home=I(v["mixer"]);p.Basket=I(v["basket"]);p.Fryer=I(v["fryer"]);p.Recipe=recipes[p.Index];p.Ids=[p.Vessel,p.Home,p.Basket,p.Fryer];
                    Need(Held(s,p.Player)==0&&Parent(s,p.Vessel)==p.Home&&Parent(s,p.Basket)==p.Fryer&&Empty(s,p.Basket)&&N(E(s,p.Vessel)["mixingProgress"]) is >=9 and <12&&!Dough(s,p.Vessel,p.Recipe),"Near-ready admission lacks an actual incomplete mixing bowl and empty original fryer.");
                    Need(Food(s,p.Vessel).IngredientIds.Order().SequenceEqual(CarnivalRecipes.GetRecipe(p.Recipe).RequiredInputs.Select(x=>x.Id).Order()),"Near-ready native ingredients differ from the assigned preview recipe.");
                }
                p.Ordinals=p.Ids.ToDictionary(id=>id,id=>I(E(s,id)["observedOrdinal"]));p.InitialProgress=N(E(s,p.Vessel)[p.Kind=="onion"?"cookingProgress":"mixingProgress"]);
                records.Add(p);Observe(p,res,null);lastResponse=res;continue;
            }
            if(name is "nativeDirectEarlyOnionComplete" or "nativeNearReadyDoughComplete")
            {
                var v=JsonNode.Parse(root.GetProperty("value").GetRawText())!.AsObject();string type=name=="nativeDirectEarlyOnionComplete"?"onion":"dough";int vessel=I(v[type=="onion"?"pan":"bowl"]);
                var p=records.Single(p=>!p.Completed&&p.Kind==type&&p.Vessel==vessel);var res=JsonNode.Parse(lastCall!)!["response"]!.AsObject();var s=res["state"]!.AsObject();
                Observe(p,res,lastResponse);Need(p.ReadyFrame>=p.Start&&p.TransferFrame>p.ReadyFrame&&Held(s,p.Player)==0&&Empty(s,p.Vessel)&&Parent(s,p.Vessel)==p.Home,"Completion lacks independently observed native preparation, consumption and original empty home vessel.");
                if(type=="onion")Need(Parent(s,p.Food)==p.Source&&CarnivalRecipes.MatchRecipe(E(s,p.Food),472326,false).ReadyToDeliver&&I(E(s,p.Counter)["attachedEntityId"])==0,"Final direct-onion exact source return/unused parking proof absent.");
                else Need(p.DetachFrame>p.ReadyFrame&&Dough(s,p.Basket,p.Recipe)&&Parent(s,p.Basket)==p.Fryer,"Final near-ready bowl restore and assigned native dough-in-fryer proof absent.");
                p.Completed=true;p.End=I(s["gameplayFrame"]);p.EndTime=N(s["clientTime"]);p.EndTimer=N(s["timer"]);
            }
        }
    }
    report["ok"]=true;
}
catch(Exception error){report["ok"]=false;report["error"]=error.Message;report["detail"]=error.ToString();Environment.ExitCode=1;}
report["classification"]="Bounded read-only production trace audit. Completed direct Work intervals are individually checked; pending intervals and unobserved tail are not successes. No full-round, score or replay qualification.";
report["trace"]=args.Length>0?Path.GetFullPath(args[0]):null;report["prefixBytesAtStart"]=bytes;report["callsRead"]=calls;report["lastGameplayFrame"]=lastFrame;report["tailDecoderMessage"]=tailError;
if(args.Length>0&&bytes>0)report["sourceCompressedPrefixSha256"]=PrefixHash(args[0],bytes);
report["completed"]=records.Count(p=>p.Completed);report["pending"]=records.Count(p=>!p.Completed);report["records"]=JsonSerializer.SerializeToNode(records,new JsonSerializerOptions{IncludeFields=true});
report["checkerSha256"]=Hash("scripts/DirectProductionCheck/Program.cs");report["classifierSha256"]=Hash(typeof(CarnivalRecipes).Assembly.Location);
string output=report.ToJsonString(new(){WriteIndented=true});if(args.Length>=2&&!File.Exists(args[1]))File.WriteAllText(args[1],output+Environment.NewLine);Console.WriteLine(output);

void Observe(Proof p,JsonObject res,JsonObject? previous)
{
    var s=res["state"]!.AsObject();int frame=I(s["gameplayFrame"]);if(frame==p.LastObserved)return;
    Need(I(s["captureFramerate"])==60&&N(s["fixedDeltaTime"])==.02&&!B(s["timerSuppressed"])&&B(s["instrumentation"]?["nativePhysicsAutoSimulation"]),"Native processing/physics instrumentation differs during direct interval.");
    Need(s["instrumentation"]?["manifest"]?["pluginSha256"]?.ToString()=="7d5fa9fb67a17cfc974d80e2416620bfa4f6ab2cc213ac8a33b890aa7b6fafbd","Unexpected native plugin hash.");
    Need(p.LastObserved<0||frame==p.LastObserved+1,"Missing native advancing sample during a direct interval.");p.LastObserved=frame;p.Samples++;
    if(p.Samples==1){p.StartTime=N(s["clientTime"]);p.StartTimer=N(s["timer"]);}
    Need(p.Ordinals.All(x=>I(E(s,x.Key)["observedOrdinal"])==x.Value)&&B(C(s,p.Player)["controlsEnabled"]),"Direct native identities or owner controls changed.");
    Need(p.Ids.All(id=>!Food(s,id).IsRuined),"Direct interval contains ruined native food.");
    double progress=N(E(s,p.Vessel)[p.Kind=="onion"?"cookingProgress":"mixingProgress"]);p.MaximumProgress=Math.Max(p.MaximumProgress,progress);
    Need(progress<21,"Direct vessel reached its native safety guard.");
    if(p.Kind=="onion")
    {
        Need(Parent(s,p.Vessel)==p.Home&&I(E(s,p.Counter)["attachedEntityId"])==0&&new[]{0,p.Food}.Contains(Held(s,p.Player)),"Direct onion carried/parked the pan or changed owner food.");
        if(!Empty(s,p.Vessel))
        {
            Need(CarnivalRecipes.MatchRecipe(E(s,p.Food),296560,false).ReadyToDeliver&&Food(s,p.Vessel).IngredientIds.SequenceEqual(new[]{CarnivalRecipes.Onion.Id}),"Pan/plain ingredients changed before native onion transfer.");
            if(Food(s,p.Vessel).Preparation==FoodPreparation.Cooked&&Food(s,p.Vessel).CookingStepId==CarnivalRecipes.PanCookingStepId&&progress>12&&p.ReadyFrame<0)p.ReadyFrame=frame;
        }
        else
        {
            Need(p.ReadyFrame>=0&&CarnivalRecipes.MatchRecipe(E(s,p.Food),472326,false).ReadyToDeliver&&progress==0,"Empty onion pan lacks prior native Cooked evidence and exact new meal.");
            if(p.TransferFrame<0){Need(Held(s,p.Player)==p.Food,"Native onion transfer did not retain its exact held base.");PickupEdge(p,res,previous);p.TransferFrame=frame;}
        }
    }
    else
    {
        if(Dough(s,p.Vessel,p.Recipe)&&p.ReadyFrame<0)p.ReadyFrame=frame;
        if(Held(s,p.Player)==p.Vessel&&p.DetachFrame<0){Need(p.ReadyFrame>=0&&Parent(s,p.Vessel)==0,"Bowl detached before native Mixed or while still attached.");p.DetachFrame=frame;p.OffmixProgress=progress;}
        if(p.DetachFrame>=0&&!Empty(s,p.Vessel))Need(progress==p.OffmixProgress&&Held(s,p.Player)==p.Vessel,"Held native dough changed mix progress or owner.");
        if(Empty(s,p.Vessel))
        {
            Need(p.DetachFrame>=0&&Dough(s,p.Basket,p.Recipe)&&Food(s,p.Basket).CookingStepId==CarnivalRecipes.DeepFryerCookingStepId,"Empty bowl lacks exact completed assigned dough in native fryer.");
            if(p.TransferFrame<0){Need(Held(s,p.Player)==p.Vessel,"Native dough transfer did not retain its emptied bowl.");PickupEdge(p,res,previous);p.TransferFrame=frame;}
        }
        Need(Parent(s,p.Basket)==p.Fryer&&N(E(s,p.Basket)["cookingProgress"])<19,"Target fryer moved or exceeded its native heat guard during restoration.");
    }
}
void PickupEdge(Proof p,JsonObject res,JsonObject? previous)
{
    Need(previous is not null&&B(Pad(res,p.Player)["pickup"])&&!B(Pad(previous!,p.Player)["pickup"]),"Native contents transfer lacks an observed pickup edge.");
    int target=I(C(previous!["state"]!.AsObject(),p.Player)["placementTargetId"]);
    Need(p.Kind=="onion"?new[]{p.Vessel,p.Home}.Contains(target):new[]{p.Basket,p.Fryer}.Contains(target),"Native placement target differs from exact vessel/home.");
}
sealed class Proof
{
    public string Kind="";public int Player,Start,End,Index,Vessel,Home,Food,Source,Counter,Basket,Fryer,Recipe,Samples,LastObserved=-1,ReadyFrame=-1,DetachFrame=-1,TransferFrame=-1;
    public double InitialProgress,MaximumProgress,OffmixProgress,StartTime,EndTime,StartTimer,EndTimer;public bool Completed;public int[] Ids=[];public Dictionary<int,int> Ordinals=[];
}
sealed class PrefixStream(Stream inner,long length):Stream
{
    readonly long total=length;long left=length;public override int Read(byte[] b,int o,int n){int read=inner.Read(b,o,(int)Math.Min(n,left));left-=read;return read;}
    public override int Read(Span<byte> b){int read=inner.Read(b[..(int)Math.Min(b.Length,left)]);left-=read;return read;}
    public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>total;public override long Position{get=>total-left;set=>throw new NotSupportedException();}public override void Flush(){}public override long Seek(long x,SeekOrigin y)=>throw new NotSupportedException();public override void SetLength(long x)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
}
