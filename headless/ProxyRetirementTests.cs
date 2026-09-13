using Google.Protobuf;
using Hpmv;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Team17.Online.Multiplayer.Messaging;

namespace Supercharged.Headless;
public static class ProxyRetirementTests
{
    public static JsonObject Run(string path,string evidenceRoot)
    {
        int checks=0,exchanges=0,controls=0,inputDifferences=0;
        void Check(bool ok,string why){if(!ok)throw new InvalidOperationException("Captured proxy retirement: "+why);checks++;}
        HeadlessSession session=null;GameSetup setup=null;OutputData last=null;
        foreach(string line in TraceStore.Expand(TraceStore.ReadLines(path)))
        {
            var row=JsonNode.Parse(line)!.AsObject();
            if(row["kind"]?.ToString()=="session") {
                byte[] bytes=Convert.FromBase64String(row["initialSetupProtobuf"]!.ToString());
                Check(Convert.ToHexStringLower(SHA256.HashData(bytes))==row["initialSetupSha256"]!.ToString(),"Recorded initial setup hash matches");
                setup=Hpmv.Save.GameSetup.Parser.ParseFrom(bytes).FromProto();
                session=new HeadlessSession(setup,row["setupSource"]!.ToString(),evidenceRoot,row["requestedSeed"]?.GetValue<int>(),levelProfile:row["level"]?.ToString());
            }
            else if(row["kind"]?.ToString()=="control") {session!.Command(row["request"]!.AsObject());controls++;}
            else if(row["kind"]?.ToString()=="exchange") {
                last=row["output"]!.Deserialize<OutputData>(RuntimeHost.Json)!;
                var input=session!.getNext(last).GetAwaiter().GetResult();exchanges++;
                if(row["input"] is JsonObject expected && !input.Equals(expected.Deserialize<InputData>(RuntimeHost.Json)))inputDifferences++;
            }
        }
        var before=session!.Inspect(true);int frame=before["frame"]!.GetValue<int>();
        Check(frame==305 && before["errors"]!.AsArray().Count==0,"Actual native trace reconstructed to305 without errors");
        var entities=before["entities"]!.AsArray();
        Check(!entities.Any(e=>e!["id"]!.GetValue<int>()==51) && entities.Single(e=>e!["id"]!.GetValue<int>()==53)!["path"]!.ToJsonString()=="[30,0,0]","Actual raw51 destruction and prepared53 nested spawn reconstructed");
        Check(before["graphMappingValidation"]!["ok"]!.GetValue<bool>()==false && before["graphMappingValidation"]!["error"]!.ToString().Contains("52"),"Exact observed unretired proxy52 reproduces mapping blocker");
        Check(inputDifferences==0,"All original recorded emitted inputs match on full-prefix reconstruction");
        var suffix=last!.DeepCopy();suffix.ServerMessages=new();suffix.EntityRegistry=new();suffix.Items=new();suffix.Chefs=new();suffix.LastFramePaused=suffix.NextFramePaused=true;
        var retirement=new ServerMessage {Type=(int)MessageType.EntityRetirementMessage,Message=new EntityRetirementMessage {m_entityHeader=new(){m_uEntityID=52}}.ToBytes()};
        suffix.ServerMessages.Add(retirement);
        string exactSuffix=JsonSerializer.Serialize(suffix,RuntimeHost.Json);OutputData emitted=null;
        session.Exchange+=(output,input)=>emitted=output;
        session.getNext(suffix).GetAwaiter().GetResult();
        var after=session.Inspect(true);
        Check(ReferenceEquals(emitted,suffix) && exactSuffix==JsonSerializer.Serialize(emitted,RuntimeHost.Json),"Full original retirement packet retained unchanged for trace/output observers");
        Check(after["frame"]!.GetValue<int>()==frame && after["entities"]!.ToJsonString()==entities.ToJsonString(),"Proxy-only retirement changes no logical entity or physics state");
        Check(after["graphMappingValidation"]!["ok"]!.GetValue<bool>() && after["graphMappingValidation"]!["removedNativeIds"]!.AsArray().Any(n=>n!.GetValue<int>()==52),"Proxy52 receipt repairs exact current mapping validation");
        Check(after["observedProxyRetirements"]!.AsArray().Count==1 && after["observedProxyRetirements"]![0]!["logicalOwnerPath"]!.ToJsonString()=="[30,0]","Explicit skipped-logical-lookup receipt pins retired original owner");
        session.getNext(suffix).GetAwaiter().GetResult();var repeated=session.Inspect(true);
        Check(repeated["observedProxyRetirements"]!.AsArray().Count==1 && repeated["errors"]!.AsArray().Count==0,
            "A later retirement for the same already-proven absent proxy is idempotent without inventing another logical entity or proof");
        var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
        var audit=(CarnivalRegistryAudit)typeof(HeadlessSession).GetField("registryAudit",flags)!.GetValue(session)!;
        var connector=(RealGameConnector)typeof(HeadlessSession).GetField("core",flags)!.GetValue(session)!;
        Check(audit.InspectRetiredOwnerProxy(54,connector.simulator.entityIdToRecord,frame) is null,"Actual prepared53 still live: its proxy54 cannot bypass logical lookup yet");
        // Synthetic continuation atop the actual captured prepared record:
        // owner destruction must precede its proxy retirement within the packet.
        var preparedSuffix=suffix.DeepCopy();preparedSuffix.ServerMessages=new(){
            new ServerMessage{Type=(int)MessageType.DestroyEntity,Message=new DestroyEntityMessage{m_Header=new(){m_uEntityID=53}}.ToBytes()},
            new ServerMessage{Type=(int)MessageType.EntityRetirementMessage,Message=new EntityRetirementMessage{m_entityHeader=new(){m_uEntityID=54}}.ToBytes()}};
        session.getNext(preparedSuffix).GetAwaiter().GetResult();var consumed=session.Inspect(true);
        Check(consumed["observedProxyRetirements"]!.AsArray().Count==2 && consumed["observedProxyRetirements"]![1]!["logicalOwnerPath"]!.ToJsonString()=="[30,0,0]","Prepared53 consumption followed by54 retirement in the same callback preserves native ordering");
        Check(consumed["graphMappingValidation"]!["ok"]!.GetValue<bool>() && consumed["graphMappingValidation"]!["removedNativeIds"]!.AsArray().Any(n=>n!.GetValue<int>()==54),"Same-packet retired prepared proxy gets only an explicit observer receipt");
        // Unknown unassociated proxy remains fatal; a fresh native load must
        // recover this local error without replaying old authored graph nodes.
        var unknown=suffix.DeepCopy();unknown.ServerMessages[0].Message=new EntityRetirementMessage {m_entityHeader=new(){m_uEntityID=999}}.ToBytes();
        try {session.getNext(unknown).GetAwaiter().GetResult();throw new Exception("Expected unknown retirement rejection");}
        catch(KeyNotFoundException){checks++;}
        int oldNodes=setup!.sequences.NodeById.Count;Check(oldNodes>0,"Actual prepared-food candidate graph definitions still present before reload");
        var fresh=new OutputData{LastFramePaused=true,NextFramePaused=true,ServerMessages=new(){new ServerMessage{Type=(int)MessageType.LevelLoadByName,Message=new LevelLoadByNameMessage{m_Scene="s_sushi_1_1",m_StartLoadGameState=GameState.InLevel,m_HideLoadingScreenGameState=GameState.InLevel}.ToBytes()}},InvalidStateReason=""};
        var freshInput=session.getNext(fresh).GetAwaiter().GetResult();var freshState=session.Inspect(true);
        Check(setup.sequences.NodeById.Count==0 && freshState["nativeReloadDiscard"]!["discardedActionIds"]!.AsArray().Count==oldNodes,"Observed new native level discards all old CLI-owned batches before original graph reset");
        Check(freshState["errors"]!.AsArray().Count==0 && freshState["nativeReloadDiscard"]!["previousErrors"]!.AsArray().Count>0,"Previous failure preserved explicitly without poisoning fresh level");
        Check(freshInput.Input==null || freshInput.Input.Count==0 || freshInput.Input.Values.All(p=>p.Pad.X==0&&p.Pad.Y==0&&!p.Pickup.Down&&!p.Interact.Down&&!p.Dash.Down),"No old action input emitted during fresh native reset");
        Check(setup.entityRecords.GenAllEntities().Where(e=>e.path.ids.Length>1).All(e=>!e.existed[0]),"Original native reset retires prior scene's dynamic claims at zero");
        Directory.CreateDirectory(evidenceRoot);File.WriteAllText(Path.Combine(evidenceRoot,"before.json"),before.ToJsonString());File.WriteAllText(Path.Combine(evidenceRoot,"after.json"),after.ToJsonString());File.WriteAllText(Path.Combine(evidenceRoot,"test-suffix.json"),exactSuffix);
        return new(){["ok"]=true,["checks"]=checks,["exchanges"]=exchanges,["controls"]=controls,["recordedInputDifferences"]=inputDifferences,["sourceSha256"]=Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path))),
            ["gameCalls"]=0,["scope"]="Unmodified actual native Story11 trace replay through raw51→prepared53/frame305; explicitly generated test suffix uses the existing framework retirement52 codec because the original failing callback was not persisted. No historical native removal event or native game call is claimed."};
    }
}
