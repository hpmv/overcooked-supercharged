using SuperchargedPatch;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;
using System.Security.Cryptography;
using System.Text.Json;

var checks=new List<string>();
void Check(bool pass,string name){if(!pass)throw new Exception(name);checks.Add(name);}
void Reject(Action action,string name){bool failed=false;try{action();}catch(InvalidOperationException){failed=true;}Check(failed,name);}
Dictionary<string,object> Metrics()=>(Dictionary<string,object>)NativeCannonCheckpoint.CaptureDiagnostics();
long Count(string name)=>(long)Metrics()[name];
bool Active()=>(bool)Metrics()["batchActive"];
ServerCannon Make(uint id) {
 var obj=new GameObject();var server=obj.Add<ServerCannon>();obj.Add<ClientCannon>();obj.Add<Cannon>().m_attachPoint=obj.transform;
 obj.Add<ServerCannonSessionInteractable>();obj.Add<ClientCannonSessionInteractable>();obj.Add<ClientCannonPlayerHandler>();obj.Add<ClientCannonCosmeticDecisions>();
 obj.Add<CannonCosmeticDecisions>().m_cannonAnimator=obj.Add<Animator>();
 var entry=new EntitySerialisationEntry {m_GameObject=obj};entry.m_Header.m_uEntityID=id;
 EntitySerialisationRegistry.Entries[obj]=entry;EntitySerialisationRegistry.m_EntitiesList._items.Add(entry);return server;
}
void Clean(){NativeCannonCheckpoint.EndObservationBatch();UnityEngine.Object.All.Clear();EntitySerialisationRegistry.Entries.Clear();EntitySerialisationRegistry.m_EntitiesList._items.Clear();Extensions.OnSend=null;}
var cannon=Make(84);Make(85);
var first=NativeCannonCheckpoint.Observe(cannon);var second=NativeCannonCheckpoint.Observe(cannon);
Check(!ReferenceEquals(first,second),"outside-batch repeated logical frame captures fresh");
long deep=Count("deepCaptures"),reuses=Count("captureReuses");
NativeCannonCheckpoint.BeginObservationBatch(60);
try {
 NativeCannonCheckpoint.ObserveFrame(cannon);
 var a=NativeCannonCheckpoint.CaptureAll();var b=NativeCannonCheckpoint.CaptureAll();
 Check(ReferenceEquals(a[0],b[0])&&ReferenceEquals(a[1],b[1]),"aux and kitchen share exact snapshot only inside batch");
 Check(Count("deepCaptures")-deep==2&&Count("captureReuses")-reuses==3,"two cannon batch captures twice and reuses three reads");
}finally{NativeCannonCheckpoint.EndObservationBatch();}
Check(!Active(),"normal collector finally leaves no active batch");
cannon.m_readyToLaunch=false;
NativeCannonCheckpoint.BeginObservationBatch(60);var next=NativeCannonCheckpoint.Observe(cannon);NativeCannonCheckpoint.EndObservationBatch();
Check(!next.Ready,"same paused frame next callback sees changed native state");
NativeCannonCheckpoint.BeginObservationBatch(61);NativeCannonCheckpoint.Observe(cannon);Time.frameCount++;
Reject(()=>NativeCannonCheckpoint.Observe(cannon),"Unity callback change rejects escaped batch");Check(!Active(),"escaped callback clears cache");
NativeCannonCheckpoint.BeginObservationBatch(61);NativeCannonCheckpoint.Observe(cannon);
Reject(()=>NativeCannonCheckpoint.BeginObservationBatch(61),"nested collector rejected");Check(!Active(),"nested collector failure clears outer batch");
try {NativeCannonCheckpoint.BeginObservationBatch(62);NativeCannonCheckpoint.Observe(cannon);throw new ApplicationException("collector failed");}
catch(ApplicationException){}finally{NativeCannonCheckpoint.EndObservationBatch();}
Check(!Active(),"collector exception finally clears batch");
NativeCannonCheckpoint.BeginObservationBatch(63);NativeCannonCheckpoint.Observe(cannon);Extensions.OnSend=(s,m)=>throw new Exception("output failed");
NativeCannonCheckpoint.ObserveFrame(cannon);Check(!Active()&&NativeCannonCheckpoint.LastObservationError.Contains("output failed"),"aux exception records failure and cancels batch");Extensions.OnSend=null;

// A warp can occur at the same logical and Unity frame: neither frame value may
// authorize reuse during its preflight, postcondition or subsequent observation.
cannon.m_readyToLaunch=true;var saved=NativeCannonCheckpoint.CaptureAll();
NativeCannonCheckpoint.BeginObservationBatch(64);NativeCannonCheckpoint.Observe(cannon);cannon.m_readyToLaunch=false;
Reject(()=>NativeCannonCheckpoint.VerifyRestored(saved),"warp postcondition detects real native mutation behind cached observation");
Check(!Active(),"warp postcondition unconditionally clears batch");
NativeCannonCheckpoint.BeginObservationBatch(64);NativeCannonCheckpoint.Observe(cannon);cannon.gameObject.GetComponent<ServerCannonSessionInteractable>().m_session=new object();
Reject(()=>NativeCannonCheckpoint.Validate(saved),"warp preflight sees new native session despite cached settled state");
Check(!Active(),"warp preflight clears batch on refusal");cannon.gameObject.GetComponent<ServerCannonSessionInteractable>().m_session=null;
NativeCannonCheckpoint.BeginObservationBatch(64);var future=NativeCannonCheckpoint.Observe(cannon);NativeCannonCheckpoint.Restore(saved);
Check(!Active()&&NativeCannonCheckpoint.Observe(cannon).Ready&&!future.Ready,"restore clears cached future and next read sees restored native value");
NativeCannonCheckpoint.VerifyRestored(saved);Check(true,"fresh postcondition accepts restored state");

void Mutation(Action<ServerCannon> mutate,string name) {
 Clean();var s=Make(84);NativeCannonCheckpoint.BeginObservationBatch(0);NativeCannonCheckpoint.Observe(s);mutate(s);
 Reject(()=>NativeCannonCheckpoint.Observe(s),name);Check(!Active(),name+" clears batch");
}
Mutation(s=>{var e=new EntitySerialisationEntry{m_GameObject=s.gameObject};e.m_Header.m_uEntityID=84;EntitySerialisationRegistry.Entries[s.gameObject]=e;},"same ID registry replacement rejected");
Mutation(s=>EntitySerialisationRegistry.Entries.Remove(s.gameObject),"registry removal rejected");
Mutation(s=>EntitySerialisationRegistry.GetEntry(s.gameObject).m_Header.m_uEntityID=99,"registered ID mutation rejected");
Mutation(s=>s.gameObject.Add<ClientCannon>(),"native component replacement rejected");
Mutation(s=>s.gameObject.GetComponent<ClientCannon>().Destroyed=true,"destroyed native component rejected");
Mutation(s=>s.Destroyed=true,"destroyed cannon rejected");
Mutation(s=>s.transform.parent=new GameObject().transform,"native hierarchy parent replacement rejected");
Clean();cannon=Make(84);var passenger=new GameObject();var passengerEntry=new EntitySerialisationEntry{m_GameObject=passenger};passengerEntry.m_Header.m_uEntityID=103;EntitySerialisationRegistry.Entries[passenger]=passengerEntry;
cannon.m_loadedObject=passenger;NativeCannonCheckpoint.BeginObservationBatch(0);NativeCannonCheckpoint.Observe(cannon);EntitySerialisationRegistry.Entries.Remove(passenger);
Reject(()=>NativeCannonCheckpoint.Observe(cannon),"saved passenger registry removal rejected");Check(!Active(),"passenger identity exception clears batch");
Clean();cannon=Make(84);NativeCannonCheckpoint.BeginObservationBatch(0);NativeCannonCheckpoint.Observe(cannon);Make(85);
Check(NativeCannonCheckpoint.CaptureAll().Length==2,"CaptureAll enumerates new actual membership during batch");NativeCannonCheckpoint.EndObservationBatch();
Clean();cannon=Make(84);cannon.gameObject.GetComponent<ClientCannon>().Destroyed=true;NativeCannonCheckpoint.BeginObservationBatch(0);
long attempts=Count("deepCaptures");Reject(()=>NativeCannonCheckpoint.Observe(cannon),"failed deep capture is not cached");Check(!Active()&&Count("deepCaptures")==attempts+1,"failed deep attempt counted and scope cleared");
cannon.gameObject.Add<ClientCannon>();Check(NativeCannonCheckpoint.Observe(cannon)!=null,"subsequent repaired state captures fresh");
Check(Count("deepCaptureTicks")>0&&Count("reuseValidationTicks")>0&&(long)Metrics()["tickFrequency"]>0,"capture and reuse timings have explicit stopwatch units");
var source=Path.GetFullPath("framework/patch/NativeCannonCheckpoint.cs");
var report=new {passed=true,checks=checks.Count,names=checks,sourceSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))),metrics=Metrics(),scope="Linked production cannon capture with controlled native/Unity doubles; native speed and gameplay parity not measured."};
var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true});
if(args.Length>0)File.WriteAllText(args[0],json);Console.WriteLine(json);
