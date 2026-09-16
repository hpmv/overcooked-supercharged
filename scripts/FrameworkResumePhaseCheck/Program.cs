using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Hpmv;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SuperchargedPatch;
using SuperchargedPatch.Authoring.Modules;
using SuperchargedPatch.Bridge;

var checks=new List<string>();
void Check(bool b,string n){if(!b)throw new Exception(n);checks.Add(n);}
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
object Field(object o,string n)=>o.GetType().GetField(n,flags).GetValue(o);
object Call(object o,string n,params object[] a)=>o.GetType().GetMethod(n,flags).Invoke(o,a);
object Pull(object q)=>q.GetType().GetMethod("Dequeue").Invoke(q,new object[]{TimeSpan.Zero});
void Frame(int phase){UnityEngine.Time.frameCount++;Injector.Server.CurrentFrameData.PhysicsFramesElapsed=phase==0?0:1;Injector.Server.CurrentFrameData.FramesSinceLastNoPhysicsFrame=phase;}
void Late(){if(ResumePhaseModule.BeforeLateUpdate())ControllerHandler.LateUpdate();}
Dictionary<string,object> Status(ResumePhaseModule m)=>(Dictionary<string,object>)m.Invoke("status",new());
(ResumePhaseModule module,object observation,InputData input) Setup(int phase=0,bool publish=true){
 NativeSessionBridge.Blocked=false;NativeSessionBridge.Clone=false;Helpers.Paused=true;StateInvalidityManager.InvalidReason="";
 UnityEngine.Time.captureFramerate=60;UnityEngine.Time.fixedDeltaTime=.02f;
 Injector.Server=new();ControllerHandler.MultiplayerController=new();
 var module=new ResumePhaseModule();module.Invoke("activate",new());
 Frame(phase);ControllerHandler.LateUpdate();var o=Pull(Field(Injector.Server,"output"));
 var reply=new InputData{RequestResume=true,NextFrame=31,GameSpeed=1001,__isset=new(){nextFrame=true,gameSpeed=true}};
 if(publish){ResumePhaseModule.ObserveReply(Injector.Server,o,reply,true);Call(Injector.Server,"PublishReply",o,reply,true);
  Injector.Server.TryGetCurrentInput(out var accepted);ResumePhaseModule.ObserveAccept(Injector.Server,reply,true);}
 return(module,o,reply);
}
// Publication and acceptance are two distinct ownership boundaries. Prove that
// the module retains exact provenance if the existing filter substitutes the
// object stored as currentInput.
{
 var(m,o,i)=Setup(publish:false);NativeSessionBridge.Clone=true;
 ResumePhaseModule.ObserveReply(Injector.Server,o,i,true);Call(Injector.Server,"PublishReply",o,i,true);
 Check(Injector.Server.TryGetCurrentInput(out var accepted)&&!ReferenceEquals(i,accepted),"Fixture filter substitutes accepted resume object");
 ResumePhaseModule.ObserveAccept(Injector.Server,i,true);
 Frame(1);Late();
 Check(!Helpers.Paused&&(long)Status(m)["acceptedResumeMappings"]==1,"Exact publish-to-accept binding survives input substitution");
 Helpers.Paused=true;m.Dispose();NativeSessionBridge.Clone=false;
}
// Test the module's exact prefix sequence around the real handler/queue source;
// Harmony/native/network surfaces are stubbed, and no socket is opened.
foreach(int delay in new[]{1,2,3,6}){
 var (m,o,i)=Setup();int capture=ActiveStateCollector.CapturedPhysics.Count,applied=TASLogicalButton.Applied.Count;
 var events=Injector.Server.CurrentFrameData;events.ServerMessages.Add("native");events.EntityRegistry.Add("registration");
 Frame(delay%6);Late();
 if(delay%6!=1){
  Check(Helpers.Paused&&ReferenceEquals(Field(Injector.Server,"currentInput"),i),"Delayed reply retains same accepted resume delay"+delay);
  Check(ActiveStateCollector.CapturedPhysics.Count==capture&&Injector.Server.CurrentFrameData.PhysicsFramesElapsed==0,"Delayed reply skips capture and resets only current physics delay"+delay);
 }
 int phase=delay%6,waits=0;while(Helpers.Paused&&waits++<12){phase=(phase+1)%6;Frame(phase);Late();}
 Check(!Helpers.Paused,"Native target reached within one six-phase cycle delay"+delay);
 Check(phase==1&&ActiveStateCollector.CapturedPhysics.Count==capture+1,"Execution uses actual target phase1 and one capture delay"+delay);
 Check(TASLogicalButton.Applied.Count==applied&&TASLogicalButton.Applied.Count(value=>ReferenceEquals(value,i))==1,
  "Resume input remains accepted exactly once across held callbacks delay"+delay);
 var output=Pull(Field(Injector.Server,"output"));var data=(OutputData)Field(output,"Value");
 Check(data.LastFramePaused&&!data.NextFramePaused&&data.FramesSinceLastNoPhysicsFrame==1&&data.ServerMessages.SequenceEqual(new[]{"native"})&&data.EntityRegistry.Count==1,"One original acknowledgement preserves events and true phase delay"+delay);
 Helpers.Paused=true;m.Dispose();
}
// Exact failing native X receipt: origin phase0, late native execution phase2.
{
 var(m,o,i)=Setup(publish:false);int captures=ActiveStateCollector.CapturedPhysics.Count,applies=TASLogicalButton.Applied.Count;
 ControllerHandler.MultiplayerController.DuringFlush=()=>{
  ControllerHandler.MultiplayerController.DuringFlush=null;
  ResumePhaseModule.ObserveReply(Injector.Server,o,i,true);Call(Injector.Server,"PublishReply",o,i,true);
 };
 Frame(2);Late();
 Check(Helpers.Paused&&ActiveStateCollector.CapturedPhysics.Count==captures&&TASLogicalButton.Applied.Count==applies,"Reply arriving in native flush cannot reach an unguarded second poll");
 Check(Field(Injector.Server,"currentInput")==null&&Injector.Server.CurrentFrameData.PhysicsFramesElapsed==0,"Between-poll arrival stays queued with no accepted frame/capture");
 Injector.Server.TryGetCurrentInput(out var lateAccepted);ResumePhaseModule.ObserveAccept(Injector.Server,i,true);
 Frame(3);Late();Check(Helpers.Paused&&ReferenceEquals(Field(Injector.Server,"currentInput"),i),"Next callback accepts that exact late reply under phase guard");
 Frame(1);Late();Check(!Helpers.Paused&&TASLogicalButton.Applied.Count==applies+1,"Between-poll arrival resumes exactly once only at target phase");
 Helpers.Paused=true;m.Dispose();
}
var raw=File.ReadLines("artifacts/framework-migration/native-x/offline-exact-frames/exchange-through-movement-replay-131.jsonl").ToArray();
var source=JsonDocument.Parse(raw[1708-1]).RootElement;var consumed=JsonDocument.Parse(raw[1709-1]).RootElement;
Check(source.GetProperty("output").GetProperty("FramesSinceLastNoPhysicsFrame").GetInt32()==0&&source.GetProperty("input").GetProperty("RequestResume").GetBoolean()&&consumed.GetProperty("output").GetProperty("FramesSinceLastNoPhysicsFrame").GetInt32()==2,"Pinned X failure is exact phase0 request consumed at phase2");
{
 var (m,o,i)=Setup();Frame(2);Late();Check(Helpers.Paused,"Captured incorrect native phase2 is held, never relabeled");
 for(int p=3;p<=6;p++){Frame(p%6);Late();Check(Helpers.Paused,"Wait through actual callback phase"+(p%6));}
 Frame(1);Late();Check(!Helpers.Paused,"Captured failure route resumes only when actual target1 returns");Helpers.Paused=true;m.Dispose();
}
foreach(string bad in new[]{"unknown","epoch","combined","schedule","timeout","retire"}){
 var (m,o,i)=Setup(publish:bad!="unknown");
 if(bad=="unknown")Call(Injector.Server,"PublishReply",o,i,true);
 if(bad=="combined")i.Input=new(){{103,new object()}};
 if(bad=="schedule")UnityEngine.Time.fixedDeltaTime=.01f;
 Frame(2);
 if(bad=="epoch"){
  Injector.Server.TryGetCurrentInput(out _);typeof(InjectorServer).GetField("connectionEpoch",flags).SetValue(Injector.Server,1L);
 }
 Late();
 if(bad=="timeout")for(int n=0;n<12;n++){Frame(2);Late();}
 if(bad=="retire")m.Invoke("deactivate",new());
 Check(Helpers.Paused&&NativeSessionBridge.Blocked&&StateInvalidityManager.InvalidReason.StartsWith("AUTHORING_RESUME_PHASE_FAILED:"),"Invalid resume fails closed with explicit pause/error: "+bad);
 m.Dispose();
}
{
 var (m,o,i)=Setup(publish:false);var warp=new InputData{Warp=new object(),NextFrame=31,__isset=new(){nextFrame=true}};
 ResumePhaseModule.ObserveReply(Injector.Server,o,warp,true);Call(Injector.Server,"PublishReply",o,warp,true);
 Frame(2);int calls=WarpHandler.Calls;Late();Check(WarpHandler.Calls==calls+1&&(long)Status(m)["deferredCallbacks"]==0,"Warp path passes through unchanged with no phase hold");m.Dispose();
}
{
 var(m,o,i)=Setup(publish:false);Helpers.Paused=false;int applies=TASLogicalButton.Applied.Count;
 Check(ResumePhaseModule.BeforeLateUpdate()&&TASLogicalButton.Applied.Count==applies,"Running prefix reads no input and leaves original scheduling alone");Helpers.Paused=true;m.Dispose();
 Check(ResumePhaseModule.BeforeLateUpdate(),"Disposed module has no active prefix effect");
}
string dll=args.ElementAtOrDefault(0)??"framework-run/modules/ResumePhase-r1c/ResumePhase.r1c.dll";
using var a=AssemblyDefinition.ReadAssembly(dll);using var core=AssemblyDefinition.ReadAssembly("artifacts/framework-plugin-native-x/SuperchargedPatch.dll");
var t=a.MainModule.Types.Single(x=>x.Name=="ResumePhaseModule");var callsAll=t.Methods.Where(x=>x.HasBody).SelectMany(x=>x.Body.Instructions).Where(x=>x.Operand is MethodReference).Select(x=>(MethodReference)x.Operand).ToArray();
Check(!callsAll.Any(x=>x.Name=="Resume"||x.Name=="Sleep"||x.Name=="Wait"||x.Name=="Dequeue"||x.Name=="set_fixedDeltaTime"||x.Name=="set_captureFramerate"),"Compiled module has no resume/time setter/blocking-wait call");
Check(t.Methods.Single(x=>x.Name=="ObserveReply").Parameters.Select(x=>x.Name).SequenceEqual(new[]{"__instance","__0","__1","__2"}),"Compiled Harmony prefix uses exact positional managed argument bindings");
Check(t.Methods.Single(x=>x.Name=="ObserveAccept").Parameters.Select(x=>x.Name).SequenceEqual(new[]{"__instance","__0","__1"}),"Compiled acceptance postfix uses exact positional managed argument bindings");
Check(core.MainModule.Types.Single(x=>x.Name=="InjectorServer").Methods.Single(x=>x.Name=="PublishReply").Parameters.Count==3,"Frozen X publication hook signature matches three arguments");
Check(core.MainModule.Types.Single(x=>x.Name=="InjectorServer").Methods.Single(x=>x.Name=="Accept").Parameters.Count==2,"Frozen X acceptance hook signature matches two arguments");
Check(callsAll.Count(x=>x.Name=="Patch")==4&&callsAll.Any(x=>x.Name=="UnpatchSelf"),"Exactly four owned Harmony hooks with explicit retirement");
string Hash(string p)=>Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(p)));
var report=new{ok=true,checks=checks.Count,names=checks,moduleSha256=Hash(dll),coreSha256=Hash("artifacts/framework-plugin-native-x/SuperchargedPatch.dll"),traceSha256=Hash("artifacts/framework-migration/native-x/offline-exact-frames/exchange-through-movement-replay-131.jsonl"),scope="Actual module/server/handler source plus compiled CLR2/IL checks and captured X phase tuple; Harmony/native surfaces stubbed, no socket/game, runtime hot-hook and native continuation proof still required."};
var json=JsonSerializer.Serialize(report);File.WriteAllText("artifacts/framework-resume-phase-tests.json",json);Console.WriteLine(json);
