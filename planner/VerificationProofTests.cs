using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace OvercookedTAS.Controller;

public static class VerificationProofTests
{
    public static async Task<int> RunAsync()
    {
        int checks=0;
        void Check(bool condition,string label){if(!condition)throw new Exception("FAIL: "+label);checks++;}
        void Reject(Action action,string label){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("FAIL: "+label);}
        var initial=VerificationTests.Initial();Verification.ValidateInitial(initial);checks++;
        foreach(string field in new[]{"captureFramerate","fixedDeltaTime","unityDeltaTime","clientTime","clientDeltaTime","logicalTime","levelClientTimeZero","unityMaximumDeltaTime","frame","fixedFrame","levelFrameZero","levelFixedFrameZero","introFrameZero","startAlignmentReleaseFrame","levelStartPhysicsPhase","alignStartPhysics"})
        {
            var bad=initial.DeepClone().AsObject();bad["state"]!.AsObject().Remove(field);
            Reject(()=>Verification.ValidateInitial(bad),"missing actual clock field "+field);
        }
        foreach(var (field,value) in new[]{("captureFramerate",(JsonNode)JsonValue.Create(50)!), ("fixedDeltaTime",JsonValue.Create(1.0/60)!), ("levelStartPhysicsPhase",JsonValue.Create(4)!), ("alignStartPhysics",JsonValue.Create(false)!)})
        {
            var bad=initial.DeepClone().AsObject();bad["state"]![field]=value.DeepClone();
            Reject(()=>Verification.ValidateInitial(bad),"wrong actual clock setting "+field);
        }
        foreach(string field in new[]{"inputActive","logicalClockActive","alignStartPhysics","nativePhysicsAutoSimulation"})
        {
            var bad=initial.DeepClone().AsObject();bad["state"]!["instrumentation"]![field]=false;
            Reject(()=>Verification.ValidateInitial(bad),"inactive installed instrumentation "+field);
        }
        Reject(()=>Verification.ValidateInstrumentation(initial["state"]!.AsObject(),123,true),"native seed must equal requested seed");
        Reject(()=>Verification.ValidateInstrumentation(initial["state"]!.AsObject(),-10,false),"native isolation must equal requested setting");
        var extraPlugin=initial.DeepClone().AsObject();extraPlugin["state"]!["instrumentation"]!["manifest"]!["loadedPlugins"]!.AsArray().Add(new JsonObject{["identifier"]="unrelated.mod",["version"]="1",["assemblySha256"]=new string('b',64)});
        Reject(()=>Verification.ValidateInitial(extraPlugin),"another loaded BepInEx plugin invalidates isolated runtime proof");
        extraPlugin=initial.DeepClone().AsObject();extraPlugin["state"]!["instrumentation"]!["manifest"]!["loadedPlugins"]![0]!["assemblySha256"]=new string('b',64);
        Reject(()=>Verification.ValidateInitial(extraPlugin),"loaded plugin hash must match hook manifest");
        foreach(string field in new[]{"seconds","loadedRoundDataSeconds","timerLimitSeconds"})
        {
            var bad=initial.DeepClone().AsObject();bad["state"]!["roundDuration"]![field]=300.0;
            Reject(()=>Verification.ValidateInitial(bad),"loaded duration cannot be inferred from remaining timer: "+field);
        }
        foreach(string field in new[]{"installed","addHookInstalled","removeHookInstalled","clearHookInstalled"})
        {
            var bad=initial.DeepClone().AsObject();bad["state"]!["entityRegistration"]![field]=false;
            Reject(()=>Verification.ValidateInitial(bad),"missing native registration hook: "+field);
        }
        var registered=initial.DeepClone().AsObject();var registry=registered["state"]!["entityRegistration"]!.AsObject();
        registry["events"]!.AsArray().Add(new JsonObject{["kind"]="register",["sequence"]=1L,["roundEpoch"]=1,["method"]="actual native AddEntry fixture",["registryCountAfter"]=1,["entity"]=new JsonObject{["entityId"]=99,["observedRegistrationSequence"]=1L}});
        registry["lastSequence"]=1L;registry["retainedCount"]=1;registered["state"]!["entities"]!.AsArray().Add(new JsonObject{["id"]=99});
        Verification.ValidateRegistration(registered["state"]!.AsObject(),true);checks++;
        registry["events"]![0]!["sequence"]=2L;
        Reject(()=>Verification.ValidateRegistration(registered["state"]!.AsObject(),true),"missing native registration sequence rejected");
        registry["events"]![0]!["sequence"]=1L;registry["returnedHistoryLimited"]=true;
        Reject(()=>Verification.ValidateRegistration(registered["state"]!.AsObject(),true),"truncated initial native registration window rejected");
        registry["returnedHistoryLimited"]=false;registered["state"]!["entities"]!.AsArray().Add(new JsonObject{["id"]=100});
        Reject(()=>Verification.ValidateRegistration(registered["state"]!.AsObject(),true),"unexplained initial native entity rejected");
        var proof=Verification.InitialProof(initial);
        var reordered=new JsonObject(initial["state"]!.AsObject().Reverse().Select(p=>KeyValuePair.Create(p.Key,p.Value?.DeepClone())));
        Check(Verification.NodeHash(reordered)==proof["canonicalInitialStateSha256"]!.GetValue<string>(),"canonical full-state hash ignores object-key ordering");
        reordered["clientTime"]=10.000001;
        Check(Verification.NodeHash(reordered)!=proof["canonicalInitialStateSha256"]!.GetValue<string>(),"full-state hash preserves native clock differences");
        var directory=Path.Combine(Path.GetTempPath(),"oc2-final-evidence-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var final=VerificationTests.Finished();var state=final["state"]!.AsObject();state["screenWidth"]=1;state["screenHeight"]=1;
            byte[] png=Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
            int calls=0;
            Task<JsonObject> Capture(JsonObject request)
            {
                Check(request["command"]?.ToString()=="screenshot"&&request.Count==3,"post-movie capture emits only screenshot/path");calls++;
                File.WriteAllBytes(request["path"]!.GetValue<string>(),png);
                var response=final.DeepClone().AsObject();response["message"]=request["path"]!.DeepClone();return Task.FromResult(response);
            }
            var screenshot=await Verification.CaptureFinalScreenshotAsync(Path.Combine(directory,"final.png"),final,"immutable-input-hash",Capture);
            Check(calls==1&&screenshot["outsideMovieHash"]!.GetValue<bool>()&&screenshot["score"]!.GetValue<int>()==state["score"]!.GetValue<int>(),"screenshot evidence binds native final score outside movie digest");
            var manifest=Json.Object(File.ReadAllText(screenshot["manifest"]!.GetValue<string>()));
            Check(manifest["imageSha256"]!.GetValue<string>()==Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(),"manifest binds exact PNG bytes");
            foreach(string mutation in new[]{"frame","score","message","png"})
            {
                async Task<JsonObject> BadCapture(JsonObject request)
                {
                    var response=await Capture(request);
                    if(mutation=="message")response["message"]=Path.Combine(directory,"wrong.png");
                    else if(mutation=="png")File.WriteAllBytes(request["path"]!.GetValue<string>(),png[..33]);
                    else response["state"]![mutation]=state[mutation]!.GetValue<long>()+1;
                    return response;
                }
                try{await Verification.CaptureFinalScreenshotAsync(Path.Combine(directory,mutation+".png"),final,"immutable",BadCapture);throw new Exception("Invalid final evidence accepted: "+mutation);}
                catch(InvalidDataException){checks++;}
            }
        }
        finally{foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
        Console.WriteLine($"PASS: {checks} initial-clock/instrumentation/screenshot evidence assertions.");return checks;
    }
}
