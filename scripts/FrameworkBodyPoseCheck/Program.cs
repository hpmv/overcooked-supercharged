using SuperchargedPatch;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Reject(Action action,string name){try{action();}catch(InvalidOperationException){checks.Add(name);return;}throw new Exception("Expected rejection: "+name);}
EntitySerialisationEntry Add(int id,bool body=true) {
 var obj=new GameObject();if(body)obj.AddBody();var row=new EntitySerialisationEntry{m_GameObject=obj};row.m_Header.m_uEntityID=(uint)id;EntitySerialisationRegistry.m_EntitiesList._items.Add(row);return row;
}
var proxy=Add(121);var chef=Add(103);var item=Add(10,false);var obj=chef.m_GameObject;
HarmonyLib.Harmony.Installed=false;
Reject(()=>NativeBodyPoseCheckpoint.Capture(),"automatic mode requires actual installed setter observation contract");
HarmonyLib.Harmony.Installed=true;
obj.transform.localPosition=new(9,1,3);obj.transform.localRotation=new(.1f,.2f,.3f,.4f);
obj.body.position=new(10,2,4);obj.body.rotation=new(.2f,.3f,.4f,.5f);obj.body.velocity=new(1,2,3);
var initial=NativeBodyPoseCheckpoint.Capture();var ids=initial.Select(s=>s.EntityId).ToHashSet();
Check(ids.SetEquals(new[]{103,121}),"initial set captures actual registered chef and body proxy only");
Check(initial.Select(s=>s.EntityId).SequenceEqual(new[]{103,121}),"snapshot ordering deterministic by native entity ID");
Check(initial[0].BodyPosition.x==10&&initial[0].LocalPosition.x==9,"distinct rigidbody and local-transform poses retained");
Check(initial[0].WorldPosition.x==9,"actual world transform captured separately from body pose");
obj.transform.localPosition=new(21,0,0);obj.transform.localRotation=new(0,1,0,0);obj.body.position=new(20,0,0);obj.body.rotation=new(0,0,1,0);
Events.Rows.Clear();NativeBodyPoseCheckpoint.Restore(initial);NativeBodyPoseCheckpoint.VerifyRestored(initial);
Check(Events.Rows.SequenceEqual(new[]{"transform-position","transform-rotation","body-position","body-rotation"}),"all transform pose writes precede that body's rigidbody pose writes");
Check(obj.body.position.x==10&&obj.transform.position.x==9,"restore preserves observed unequal physics and transform positions");
Check(obj.body.velocity.x==1&&!obj.body.isKinematic,"pose helper leaves velocity and kinematic state untouched");
Events.Rows.Clear();NativeBodyPoseCheckpoint.Restore(initial);Check(Events.Rows.Count==0,"matching poses receive no redundant setter calls");
var dynamic=Add(200);dynamic.m_GameObject.body.position=new(50,60,70);
var later=NativeBodyPoseCheckpoint.Capture(ids);Check(later.Length==2&&ids.SetEquals(new[]{103,121}),"additional dynamic bodies ignored without mutating selected initial IDs");
obj.body.position=new(100,0,0);NativeBodyPoseCheckpoint.Restore(initial);
Check(dynamic.m_GameObject.body.position.x==50,"fixed restore never rewrites extra dynamic body");
Reject(()=>NativeBodyPoseCheckpoint.Capture(new(){103,121,999}),"missing selected fixed rigidbody rejects capture");
Reject(()=>NativeBodyPoseCheckpoint.Capture(new(){10}),"selected registered nonbody is not silently accepted");
obj.transform.parent=new GameObject().transform;Events.Rows.Clear();
Reject(()=>NativeBodyPoseCheckpoint.Restore(initial),"changed fixed-body parent rejects before mutations");
Check(Events.Rows.Count==0,"parent preflight failure causes no pose setters");obj.transform.parent=null;
var parent=new GameObject().transform;obj.transform.parent=parent;var withParent=NativeBodyPoseCheckpoint.Capture(ids);parent.Destroyed=true;
Reject(()=>NativeBodyPoseCheckpoint.Validate(withParent),"destroyed saved native parent is rejected");parent.Destroyed=false;obj.transform.parent=null;
var oldBody=obj.body;obj.AddBody();Reject(()=>NativeBodyPoseCheckpoint.Validate(initial),"replaced native rigidbody component rejected");obj.body=oldBody;
EntitySerialisationRegistry.m_EntitiesList._items.Remove(chef);var replacement=new EntitySerialisationEntry{m_GameObject=obj};replacement.m_Header.m_uEntityID=103;EntitySerialisationRegistry.m_EntitiesList._items.Add(replacement);
Reject(()=>NativeBodyPoseCheckpoint.Validate(initial),"replaced registration with identical ID and object rejected");EntitySerialisationRegistry.m_EntitiesList._items.Remove(replacement);EntitySerialisationRegistry.m_EntitiesList._items.Add(chef);
obj.body.position=new(float.NaN,0,0);Reject(()=>NativeBodyPoseCheckpoint.Capture(ids),"nonfinite native physics pose rejected");obj.body.position=initial[0].BodyPosition;
obj.transform.position=new(777,0,0);Reject(()=>NativeBodyPoseCheckpoint.VerifyRestored(initial),"world transform difference remains visible even if local and body poses match");obj.transform.position=initial[0].WorldPosition;
obj.transform.localRotation=default;obj.body.rotation=default;var zeros=NativeBodyPoseCheckpoint.Capture(ids);NativeBodyPoseCheckpoint.VerifyRestored(zeros);
Check(true,"zero stored quaternion compared fieldwise rather than Unity orientation equality");
EntitySerialisationRegistry.m_EntitiesList._items.Add(chef);Reject(()=>NativeBodyPoseCheckpoint.Capture(ids),"duplicate native registered body is rejected");EntitySerialisationRegistry.m_EntitiesList._items.RemoveAt(EntitySerialisationRegistry.m_EntitiesList.Count-1);
proxy.m_GameObject.body.Destroyed=true;obj.body.position=new(99,0,0);Events.Rows.Clear();
Reject(()=>NativeBodyPoseCheckpoint.Restore(initial),"later invalid body rejects entire selected set before first restore");
Check(Events.Rows.Count==0&&obj.body.position.x==99,"whole-set preflight prevents partial pose restoration");proxy.m_GameObject.body.Destroyed=false;
var diagnostics=(Dictionary<string,object>)NativeBodyPoseCheckpoint.Diagnostics(initial);var rows=(object[])diagnostics["bodies"];var first=(Dictionary<string,object>)rows[0];
Check(first.ContainsKey("rigidbodyPosition")&&first.ContainsKey("transformPosition")&&first.ContainsKey("transformLocalPosition"),"diagnostics distinguish body/local/world pose sources");

(GameObject obj,NativeBodyPoseCheckpoint.Snapshot[] snapshot) RotationFixture() {
 EntitySerialisationRegistry.m_EntitiesList._items.Clear();var target=Add(103).m_GameObject;
 target.transform.localPosition=new(1,2,3);target.transform.localRotation=new(0,-.7071068f,0,.7071068f);
 target.body.position=target.transform.position;target.body.rotation=target.transform.rotation;
 var captured=NativeBodyPoseCheckpoint.Capture();target.body.rotation=new(0,1,0,0);Events.Rows.Clear();return(target,captured);
}
int Setters()=>Events.Rows.Count(e=>e=="body-rotation");
var fixture=RotationFixture();var q=fixture.snapshot[0].BodyRotation;float epsilon=1f/(1<<28);
fixture.obj.body.RotationRoundtrip=v=>new(v.x+epsilon,v.y,v.z-epsilon,v.w);
NativeBodyPoseCheckpoint.Restore(fixture.snapshot);NativeBodyPoseCheckpoint.VerifyRestored(fixture.snapshot);
Check(Setters()==2,"small deterministic mass-frame-like readback residual solved in two assignments");
Check(fixture.obj.body.rotation.x==0&&fixture.obj.body.rotation.z==0&&fixture.obj.body.rotation.y==q.y&&fixture.obj.body.rotation.w==q.w,"all four stored quaternion components restored exactly");
Check(fixture.snapshot[0].BodyRotation.x==0&&fixture.obj.transform.rotation.x==0,"preimage does not modify captured target or transform rotation");
var attempts=(object[])((Dictionary<string,object>)NativeBodyPoseCheckpoint.LastRotationRestores)["attempts"];
var final=(Dictionary<string,object>)attempts.Last();var candidate=(Dictionary<string,object>)final["input"];
Check((bool)final["exact"]&&(float)candidate["x"]==-epsilon&&(float)candidate["z"]==epsilon,"diagnostic pins actual compensating setter input and exact readback");
Check(final.ContainsKey("residual")&&final.ContainsKey("restoreCall")&&final.ContainsKey("entityId"),"every setter receipt identifies call body and residual");
Events.Rows.Clear();NativeBodyPoseCheckpoint.Restore(fixture.snapshot);Check(Setters()==0,"exact restored body skips further preimage assignments");

fixture=RotationFixture();q=fixture.snapshot[0].BodyRotation;fixture.obj.body.RotationRoundtrip=v=>new(epsilon,q.y,0,q.w);
Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"tiny irreducible residual is not accepted with tolerance");
Check(Setters()==2,"unchanged readback residual stops at second assignment");
Reject(()=>NativeBodyPoseCheckpoint.VerifyRestored(fixture.snapshot),"failed preimage does not weaken ordinary exact postcondition");
fixture=RotationFixture();q=fixture.snapshot[0].BodyRotation;int n=0;fixture.obj.body.RotationRoundtrip=v=>new(epsilon*++n,q.y,0,q.w);
Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"worsening readback residual is rejected");Check(Setters()==2,"worsening search is bounded before third assignment");
fixture=RotationFixture();q=fixture.snapshot[0].BodyRotation;n=0;fixture.obj.body.RotationRoundtrip=v=>new(epsilon/(1<<++n),q.y,0,q.w);
Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"monotonic improvement without exact readback still fails");Check(Setters()==4,"strict maximum is four total rotation assignments");
fixture=RotationFixture();q=fixture.snapshot[0].BodyRotation;fixture.obj.body.RotationRoundtrip=v=>new(.01f,q.y,0,q.w);
Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"large readback residual is outside authoring envelope");Check(Setters()==1,"excessive residual prevents another setter");
fixture=RotationFixture();fixture.obj.body.RotationRoundtrip=v=>new(float.NaN,0,0,1);
Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"nonfinite native setter result is rejected");Check(Setters()==1,"nonfinite result ends assignment loop");
var nonfiniteLog=System.Text.Json.JsonSerializer.Serialize(NativeBodyPoseCheckpoint.LastRotationRestores);
Check(nonfiniteLog.Contains("NaN")&&nonfiniteLog.Contains("nonfinite"),"nonfinite failure diagnostics remain valid JSON and preserve symbolic readback");

var mutations=new Dictionary<string,Action<Rigidbody>> {
 {"mass",b=>b.mass+=1},{"centerOfMass",b=>b.centerOfMass=new(1,0,0)},
 {"inertiaTensor",b=>b.inertiaTensor=new(2,1,1)},{"inertiaTensorRotation",b=>b.inertiaTensorRotation=new(0,.1f,0,.99f)},
 {"drag",b=>b.drag+=1},{"angularDrag",b=>b.angularDrag+=1},{"constraints",b=>b.constraints=RigidbodyConstraints.FreezeRotation},
 {"interpolation",b=>b.interpolation=RigidbodyInterpolation.Interpolate},{"collisionDetection",b=>b.collisionDetectionMode=CollisionDetectionMode.Continuous},
 {"detectCollisions",b=>b.detectCollisions=false},{"sleepThreshold",b=>b.sleepThreshold+=1},{"maxAngularVelocity",b=>b.maxAngularVelocity+=1}
};
foreach(var change in mutations) {
 fixture=RotationFixture();change.Value(fixture.obj.body);
 if(new[]{"mass","centerOfMass","inertiaTensor","inertiaTensorRotation"}.Contains(change.Key)){
  var b=fixture.obj.body;var a=fixture.snapshot[0].Invariants;
  b.ResetCenter=()=>b.centerOfMass=a.CenterOfMass;b.ResetTensor=()=>{b.inertiaTensor=a.InertiaTensor;b.inertiaTensorRotation=a.InertiaTensorRotation;};
  NativeBodyPoseCheckpoint.Validate(fixture.snapshot);Check(Events.Rows.Count==0,"future mass-frame preflight is read-only: "+change.Key);
  NativeBodyPoseCheckpoint.Restore(fixture.snapshot);NativeBodyPoseCheckpoint.VerifyRestored(fixture.snapshot);
  Check(Events.Rows.Count(e=>e=="reset-center")==1&&Events.Rows.Count(e=>e=="reset-inertia")==1,"future mass-frame uses exactly one pair of native resets: "+change.Key);
  continue;
 }
 Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"changed captured invariant rejected: "+change.Key);
 Check(Events.Rows.Count==0,"invariant preflight has no pose writes: "+change.Key);
}
fixture=RotationFixture();fixture.obj.body.inertiaTensor=new(float.NaN,1,1);
Reject(()=>NativeBodyPoseCheckpoint.Capture(),"nonfinite mass-frame invariant cannot enter a checkpoint");
fixture=RotationFixture();var capturedBody=fixture.obj.body;
capturedBody.mass=2.5f;capturedBody.centerOfMass=new(.1f,.2f,.3f);
capturedBody.inertiaTensor=new(2,3,4);capturedBody.inertiaTensorRotation=new(.1f,.2f,.3f,.9f);
var capturedInvariants=NativeBodyPoseCheckpoint.Capture();
var capturedJson=System.Text.Json.JsonSerializer.Serialize(NativeBodyPoseCheckpoint.Diagnostics(capturedInvariants));
foreach(var change in mutations)change.Value(capturedBody);
Events.Rows.Clear();
var afterLiveChanges=NativeBodyPoseCheckpoint.Diagnostics(capturedInvariants);
Check(capturedJson==System.Text.Json.JsonSerializer.Serialize(afterLiveChanges),"saved diagnostics report captured invariants unchanged by later live getter changes");
var savedRow=(Dictionary<string,object>)((object[])((Dictionary<string,object>)afterLiveChanges)["bodies"])[0];
Check((float)savedRow["mass"]==2.5f&&(float)((Dictionary<string,object>)savedRow["inertiaTensor"])["y"]==3f,
 "diagnostics expose nondefault target mass and principal inertia rather than current paused values");
Check(new[]{"mass","centerOfMass","inertiaTensor","inertiaTensorRotation","drag","angularDrag","constraints","interpolation","collisionDetectionMode","detectCollisions","sleepThreshold","maxAngularVelocity"}.All(savedRow.ContainsKey),
 "snapshot diagnostics expose all twelve captured invariant fields");
Check(Events.Rows.Count==0,"reading saved body diagnostics performs no pose setters or restoration");
foreach(var field in new[]{"mass","velocity","angularVelocity","kinematic","gravity","transform","identity"}) {
 fixture=RotationFixture();var f=fixture;
 f.obj.body.OnRotationSet=()=>{switch(field){
  case "mass":f.obj.body.mass+=1;break;case "velocity":f.obj.body.velocity=new(1,0,0);break;
  case "angularVelocity":f.obj.body.angularVelocity=new(0,1,0);break;case "kinematic":f.obj.body.isKinematic=true;break;
  case "gravity":f.obj.body.useGravity=true;break;case "transform":f.obj.transform.localRotation=new(0,1,0,0);break;
  case "identity":f.obj.AddBody();break;
 }};
 Reject(()=>NativeBodyPoseCheckpoint.Restore(f.snapshot),"rotation setter side effect rejected: "+field);
 Check(Setters()==1,"side effect prevents retry: "+field);
}
fixture=RotationFixture();Events.Rows.Clear();
NativeBodyPoseCheckpoint.BeginStageObservations(31);
NativeBodyPoseCheckpoint.ObserveStage(fixture.snapshot,"before-resume");
fixture.obj.body.inertiaTensorRotation=new(0,0,-1.78814062e-7f,1);fixture.obj.body.isKinematic=true;
NativeBodyPoseCheckpoint.ObserveStage(fixture.snapshot,"after-resume");
var stages=(object[])((Dictionary<string,object>)NativeBodyPoseCheckpoint.StageDiagnostics)["stages"];
var observed=(Dictionary<string,object>)((object[])((Dictionary<string,object>)((Dictionary<string,object>)stages[1])["current"])["bodies"])[0];
Check(stages.Length==2&&(float)((Dictionary<string,object>)observed["inertiaTensorRotation"])["z"]==-1.78814062e-7f,
 "read-only stage diagnostics preserve exact witnessed U inertia drift");
Check((bool)observed["rawIsKinematic"]&&fixture.snapshot[0].Invariants.InertiaTensorRotation.z==0,
 "stage diagnostics distinguish current kinematic/mass-frame state from unchanged target capture");
Check(Events.Rows.Count==0,"stage observations call no pose setters");
fixture.obj.body.Destroyed=true;
NativeBodyPoseCheckpoint.ObserveStage(fixture.snapshot,"missing-body");
stages=(object[])((Dictionary<string,object>)NativeBodyPoseCheckpoint.StageDiagnostics)["stages"];
Check(!(bool)((Dictionary<string,object>)stages[2])["captured"]&&((Dictionary<string,object>)stages[2]).ContainsKey("error"),
 "diagnostic capture failure is recorded without replacing original warp error");
fixture.obj.body.Destroyed=false;
for(int i=0;i<40;i++)NativeBodyPoseCheckpoint.ObserveStage(fixture.snapshot,"bounded-"+i);
var stageReport=(Dictionary<string,object>)NativeBodyPoseCheckpoint.StageDiagnostics;
Check(((object[])stageReport["stages"]).Length==32&&(int)stageReport["discardedOlderStages"]==11,
 "stage diagnostic memory has a strict current-warp bound");
NativeBodyPoseCheckpoint.BeginStageObservations(99);stageReport=(Dictionary<string,object>)NativeBodyPoseCheckpoint.StageDiagnostics;
Check((int)stageReport["frame"]==99&&((object[])stageReport["stages"]).Length==0&&(int)stageReport["discardedOlderStages"]==0,
 "new warp clears prior-stage observations and pins requested frame");
// Real V values are applied to stand-ins; reset callbacks remain deliberately
// synthetic and cannot establish native PhysX recomputation equivalence.
var vPath=Path.GetFullPath("artifacts/framework-migration/native-v/pickup-failure-native.json");
using var vDoc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(vPath));var vBridge=vDoc.RootElement.GetProperty("bridge");
var vSaved=vBridge.GetProperty("nativeCheckpoints").GetProperty("requestedBodyCheckpoint").GetProperty("bodies").EnumerateArray().Single(e=>e.GetProperty("entityId").GetInt32()==103);
var vLive=vBridge.GetProperty("nativePhysics").GetProperty("bodies").EnumerateArray().Single(e=>e.GetProperty("entityId").GetInt32()==103);
Vector3 V(System.Text.Json.JsonElement e)=>new(e.GetProperty("x").GetSingle(),e.GetProperty("y").GetSingle(),e.GetProperty("z").GetSingle());
Quaternion Q(System.Text.Json.JsonElement e)=>new(e.GetProperty("x").GetSingle(),e.GetProperty("y").GetSingle(),e.GetProperty("z").GetSingle(),e.GetProperty("w").GetSingle());
(GameObject body,GameObject ancestor,CapsuleCollider shape,NativeBodyPoseCheckpoint.Snapshot[] saved) ShapeFixture(){
 var f=RotationFixture();var b=f.obj;var middle=new GameObject();middle.transform.parent=b.transform;
 var shapeObj=new GameObject();shapeObj.transform.parent=middle.transform;var shape=shapeObj.AddCollider<CapsuleCollider>();shape.center=new(0,1,0);
 b.body.rotation=f.snapshot[0].BodyRotation;
 var saved=NativeBodyPoseCheckpoint.Capture();Events.Rows.Clear();return(b,middle,shape,saved);
}
void ReadyReset(GameObject b,NativeBodyPoseCheckpoint.Snapshot[] saved){
 var a=saved[0].Invariants;
 b.body.ResetCenter=()=>b.body.centerOfMass=a.CenterOfMass;
 b.body.ResetTensor=()=>{b.body.inertiaTensor=a.InertiaTensor;b.body.inertiaTensorRotation=a.InertiaTensorRotation;};
}
var sf=ShapeFixture();var rb=sf.body.body;
rb.centerOfMass=V(vSaved.GetProperty("centerOfMass"));rb.inertiaTensor=V(vSaved.GetProperty("inertiaTensor"));rb.inertiaTensorRotation=Q(vSaved.GetProperty("inertiaTensorRotation"));
var nativeSaved=NativeBodyPoseCheckpoint.Capture();
rb.centerOfMass=V(vLive.GetProperty("centerOfMass"));rb.inertiaTensor=V(vLive.GetProperty("inertiaTensor"));rb.inertiaTensorRotation=Q(vLive.GetProperty("inertiaTensorRotation"));
var carried=new GameObject();carried.transform.parent=sf.body.transform;carried.AddCollider<BoxCollider>();
Events.Rows.Clear();NativeBodyPoseCheckpoint.Validate(nativeSaved);Check(Events.Rows.Count==0,"actual V mass-frame delta with extra carried shape passes read-only preflight");
Reject(()=>NativeBodyPoseCheckpoint.Restore(nativeSaved),"carried shape must be detached before any natural mass reset");
Check(Events.Rows.Count==0,"wrong target compound topology causes no pose writes or resets");
carried.transform.parent=null;ReadyReset(sf.body,nativeSaved);
NativeBodyPoseCheckpoint.Restore(nativeSaved);NativeBodyPoseCheckpoint.VerifyRestored(nativeSaved);
Check(rb.centerOfMass.y==1&&rb.centerOfMass.z==0&&rb.inertiaTensorRotation.z==0,"actual V future COM and inertia rotation restored by modeled native auto recomputation");
Check(Events.Rows.Count(e=>e=="reset-inertia")==1,"actual V-shaped change resets native inertia once");
var massLog=(Dictionary<string,object>)((object[])((Dictionary<string,object>)NativeBodyPoseCheckpoint.LastMassFrameRestores)["attempts"]).Last();
Check((bool)massLog["exact"]&&massLog.ContainsKey("before")&&massLog.ContainsKey("after")&&massLog.ContainsKey("target"),"mass reset receipt retains before after target and exact outcome");
foreach(var mutation in new[]{"shape","ancestor-pose","ancestor-scale","ancestor-active","parent","active","trigger","layer","material","replacement","mesh"}){
 sf=ShapeFixture();sf.body.body.centerOfMass=new(1,0,0);ReadyReset(sf.body,sf.saved);
 switch(mutation){
  case "shape":sf.shape.radius+=.01f;break;
  case "ancestor-pose":sf.ancestor.transform.localPosition=new(.1f,0,0);break;
  case "ancestor-scale":sf.ancestor.transform.localScale=new(1,2,1);break;
  case "ancestor-active":sf.ancestor.activeSelf=false;break;
  case "parent":sf.shape.transform.parent=sf.body.transform;break;
  case "active":sf.shape.gameObject.activeSelf=false;break;
  case "trigger":sf.shape.isTrigger=true;break;
  case "layer":sf.shape.gameObject.layer=7;break;
  case "material":sf.shape.sharedMaterial=new();break;
  case "replacement":sf.shape.gameObject.colliders.Clear();sf.shape.gameObject.AddCollider<CapsuleCollider>();break;
  case "mesh":sf.shape.gameObject.AddCollider<MeshCollider>();break;
 }
 Events.Rows.Clear();Reject(()=>NativeBodyPoseCheckpoint.Restore(sf.saved),"wrong restored collider prerequisite rejected: "+mutation);
 Check(Events.Rows.Count==0,"wrong collider prerequisite rejected before native reset: "+mutation);
}
sf=ShapeFixture();sf.ancestor.transform.localPosition=new(.2f,0,0);Events.Rows.Clear();
NativeBodyPoseCheckpoint.Restore(sf.saved);Check(Events.Rows.Count==0,"unchanged mass permits early body pose stage before cannon child restoration");
Reject(()=>NativeBodyPoseCheckpoint.VerifyRestored(sf.saved),"final verification still rejects an unrestored cannon-like collider ancestor");
sf.ancestor.transform.localPosition=default;NativeBodyPoseCheckpoint.VerifyRestored(sf.saved);
Check(true,"final topology succeeds only after the existing child sidecar restores actual pose");
sf=ShapeFixture();sf.body.body.centerOfMass=new(1,0,0);ReadyReset(sf.body,sf.saved);
sf.body.body.ResetTensor=()=>sf.body.body.inertiaTensorRotation=new(0,0,epsilon,1);
Reject(()=>NativeBodyPoseCheckpoint.Restore(sf.saved),"tiny natural recomputation mismatch is not accepted or manually corrected");
Check(sf.body.body.inertiaTensorRotation.z==epsilon&&Events.Rows.Count(e=>e=="reset-inertia")==1,"failed reset preserves exact unmatched native result and has no retry");
sf=ShapeFixture();sf.shape.gameObject.colliders.Clear();sf.shape.gameObject.AddCollider<MeshCollider>().sharedMesh=new();
var meshSaved=NativeBodyPoseCheckpoint.Capture();sf.body.body.centerOfMass=new(1,0,0);Events.Rows.Clear();
Reject(()=>NativeBodyPoseCheckpoint.Restore(meshSaved),"captured mesh identity alone does not authorize automatic recomputation");
Check(!Events.Rows.Any(e=>e.StartsWith("reset-")),"uncheckpointed mesh vertex contents cause no mass reset");
sf=ShapeFixture();sf.body.body.centerOfMass=new(1,0,0);ReadyReset(sf.body,sf.saved);sf.body.body.ResetTensor=()=>sf.body.body.inertiaTensor=new(float.NaN,0,0);
Reject(()=>NativeBodyPoseCheckpoint.Restore(sf.saved),"nonfinite reset fails without replacing original failure");
Check(System.Text.Json.JsonSerializer.Serialize(NativeBodyPoseCheckpoint.LastMassFrameRestores).Contains("afterError"),"nonfinite reset diagnostics remain serializable");
foreach(var field in new[]{"velocity","kinematic","drag","topology"}){
 sf=ShapeFixture();sf.body.body.centerOfMass=new(1,0,0);ReadyReset(sf.body,sf.saved);var current=sf;
 var reset=current.body.body.ResetTensor;current.body.body.ResetTensor=()=>{reset();switch(field){
  case "velocity":current.body.body.velocity=new(1,0,0);break;case "kinematic":current.body.body.isKinematic=true;break;
  case "drag":current.body.body.drag=3;break;case "topology":current.shape.radius=.8f;break;
 }};
 Reject(()=>NativeBodyPoseCheckpoint.Restore(current.saved),"automatic reset unexpected state change rejected: "+field);
}
sf=ShapeFixture();sf.body.body.centerOfMass=new(1,0,0);sf.body.transform.localRotation=new(1,0,0,0);sf.body.body.rotation=new(1,0,0,0);
var savedShape=sf;var target=sf.saved[0];ReadyReset(sf.body,sf.saved);var resetCenter=sf.body.body.ResetCenter;
sf.body.body.ResetCenter=()=>{Check(savedShape.body.transform.localRotation.y==target.LocalRotation.y&&savedShape.body.body.rotation.y==target.BodyRotation.y,"captured transform and body pose restored before automatic recalculation");resetCenter();};
NativeBodyPoseCheckpoint.Restore(sf.saved);NativeBodyPoseCheckpoint.VerifyRestored(sf.saved);
Check(Events.Rows.IndexOf("body-rotation")<Events.Rows.IndexOf("reset-center"),"native automatic resets follow captured body orientation assignment");
sf=ShapeFixture();sf.body.body.centerOfMass=new(1,0,0);sf.body.body.rotation=new(1,0,0,0);ReadyReset(sf.body,sf.saved);
q=sf.saved[0].BodyRotation;n=0;sf.body.body.RotationRoundtrip=v=>new(epsilon/(1<<++n),q.y,q.z,q.w);
Events.Rows.Clear();Reject(()=>NativeBodyPoseCheckpoint.Restore(sf.saved),"post-reset preimage still requires exact quaternion");
Check(Setters()==4,"provisional pre-reset assignment counts toward four total assignments");
sf=ShapeFixture();NativeBodyMassMode.ObserveManualAssignment(sf.body.body,"centerOfMass");Events.Rows.Clear();
Reject(()=>NativeBodyPoseCheckpoint.Restore(sf.saved),"observed manual COM setter forbids resetting automatic mode");Check(Events.Rows.Count==0,"manual mode rejection makes no native setters");
foreach(var property in new[]{"inertiaTensor","inertiaTensorRotation"}){sf=ShapeFixture();NativeBodyMassMode.ObserveManualAssignment(sf.body.body,property);Reject(()=>NativeBodyPoseCheckpoint.Capture(),"observed manual mode cannot enter checkpoint: "+property);}
fixture=RotationFixture();Helpers.Paused=false;
Reject(()=>NativeBodyPoseCheckpoint.InstallRestoreStrategy(new BodyStrategyFixture()),"module strategy installation requires native paused boundary");Helpers.Paused=true;
Reject(()=>NativeBodyPoseCheckpoint.InstallRestoreStrategy(new BodyStrategyFixture{ApiVersion=2}),"unknown body strategy API rejected");
Reject(()=>NativeBodyPoseCheckpoint.InstallRestoreStrategy(new BodyStrategyFixture{Name=""}),"empty strategy identity rejected");
var offThread=System.Threading.Tasks.Task.Run(()=>{try{NativeBodyPoseCheckpoint.InstallRestoreStrategy(new BodyStrategyFixture());return false;}catch(InvalidOperationException){return true;}}).Result;
Check(offThread,"strategy registration rejects a different managed thread");
var moduleA=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
Check((string)((Dictionary<string,object>)NativeBodyPoseCheckpoint.RestoreStrategyDiagnostics)["name"]=="builtin","constructing module does not activate it");
moduleA.Invoke("activate",new());
fixture.obj.body.RotationRoundtrip=v=>new(v.x+epsilon,v.y,v.z-epsilon,v.w);Events.Rows.Clear();
NativeBodyPoseCheckpoint.Restore(fixture.snapshot);NativeBodyPoseCheckpoint.VerifyRestored(fixture.snapshot);
var moduleStatus=(Dictionary<string,object>)moduleA.Invoke("status",new());
Check(Setters()==2&&(long)moduleStatus["restoreCalls"]==1,"external module owns substantive two-assignment pose algorithm");
Check(((object[])moduleStatus["rotationRestores"]).Length==2,"external algorithm owns actual setter receipts separately from builtin history");
var moduleB=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();moduleB.Invoke("activate",new());moduleA.Dispose();
fixture=RotationFixture();NativeBodyPoseCheckpoint.Restore(fixture.snapshot);
Check((long)((Dictionary<string,object>)moduleB.Invoke("status",new()))["restoreCalls"]==1,"disposing replaced module preserves successor strategy");
moduleB.Invoke("deactivate",new());
Check((string)((Dictionary<string,object>)NativeBodyPoseCheckpoint.RestoreStrategyDiagnostics)["name"]=="builtin","explicit deactivate restores builtin fallback");moduleB.Dispose();
var setters=typeof(NativeBodyPoseCheckpoint.Snapshot).GetProperties().Where(p=>p.Name!="HasAnalyticColliders").Select(p=>p.GetSetMethod()).ToArray();
Check(setters.All(m=>m==null),"external assemblies cannot assign target snapshot properties");
Check(typeof(NativeBodyPoseCheckpoint.BodyInvariants).GetProperties().All(p=>p.GetSetMethod()==null),"external assemblies cannot rewrite captured target mass values");
sf=ShapeFixture();var copiedShapes=sf.saved[0].Colliders;copiedShapes[0]=null;
Check(sf.saved[0].Colliders[0]!=null,"public collider collection cannot overwrite captured shape array");
foreach(var failure in new[]{"noop","array-replacement","velocity","reentry","reinstall","throw-after-write"}){
 fixture=RotationFixture();var current=fixture;
 var bad=new BodyStrategyFixture{Name=failure,Action=rows=>{
  if(failure=="array-replacement")Array.Clear(rows,0,rows.Length);
  if(failure=="velocity"){current.obj.body.rotation=current.snapshot[0].BodyRotation;current.obj.body.velocity=new(9,0,0);}
  if(failure=="reentry")NativeBodyPoseCheckpoint.Restore(rows);
  if(failure=="reinstall")NativeBodyPoseCheckpoint.InstallRestoreStrategy(new BodyStrategyFixture());
  if(failure=="throw-after-write"){current.obj.body.rotation=new(0,0,1,0);throw new InvalidOperationException("fixture post-mutation failure");}
 }};
 var lease=NativeBodyPoseCheckpoint.InstallRestoreStrategy(bad);Events.Rows.Clear();
 Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"permanent core rejects module failure: "+failure);
 Check(bad.Calls==1&&!(bool)((Dictionary<string,object>)NativeBodyPoseCheckpoint.RestoreStrategyDiagnostics)["active"],"module failure clears dispatch fence without builtin retry: "+failure);
 if(failure=="throw-after-write")Check(fixture.obj.body.rotation.z==1&&Setters()==1,"post-mutation module exception does not run builtin rollback");
 lease.Dispose();
}
fixture=RotationFixture();var oneShot=new BodyStrategyFixture{Name="single-readback-v1",Action=rows=>{foreach(var row in rows)row.Body.rotation=row.BodyRotation;}};
var firstLease=NativeBodyPoseCheckpoint.InstallRestoreStrategy(oneShot);fixture.obj.body.RotationRoundtrip=v=>new(v.x+epsilon,v.y,v.z,v.w);Events.Rows.Clear();
Reject(()=>NativeBodyPoseCheckpoint.Restore(fixture.snapshot),"first substantive strategy cannot accept inexact one-shot native setter");Check(Setters()==1,"first strategy executes one actual modeled pose assignment");
var revised=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();revised.Invoke("activate",new());firstLease.Dispose();Events.Rows.Clear();
NativeBodyPoseCheckpoint.Restore(fixture.snapshot);NativeBodyPoseCheckpoint.VerifyRestored(fixture.snapshot);
Check(Setters()==2,"replacement algorithm solves same modeled residual in same process without recapture");
Check((long)((Dictionary<string,object>)revised.Invoke("status",new()))["restoreCalls"]==1,"replacement actual algorithm invocation is separately recorded");revised.Dispose();
fixture=RotationFixture();var plateauModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
var plateauRow=fixture.snapshot[0];var plateauPosition=plateauRow.BodyPosition;var plateauRotation=plateauRow.BodyRotation;
var poseEpsilon=1f/(1<<20);
plateauModule.ConfigureNativePoseFixture(fixture.obj.body,(attempt,inputPosition,inputRotation)=>attempt switch {
 1=>new(new(plateauPosition.x,plateauPosition.y-poseEpsilon,plateauPosition.z),new(plateauRotation.x+poseEpsilon/2,plateauRotation.y,plateauRotation.z,plateauRotation.w)),
 2=>new(new(plateauPosition.x,plateauPosition.y+poseEpsilon,plateauPosition.z),new(plateauRotation.x+poseEpsilon/2,plateauRotation.y,plateauRotation.z,plateauRotation.w)),
 _=>new(plateauPosition,plateauRotation)});
plateauModule.RestoreExistingActorPoseFixture(plateauRow);
var plateauStatus=(Dictionary<string,object>)plateauModule.Invoke("status",new());
var plateauRecords=(object[])plateauStatus["nativePoseRestores"];
Check(plateauModule.NativePoseFixtureAttempts==3&&plateauRecords.Length==1&&
 (bool)((Dictionary<string,object>)plateauRecords[0])["exact"],
 "external native joint-pose preimage permits one equal-size sign-changing plateau before exact third readback");
plateauModule.Dispose();
fixture=RotationFixture();var latticeModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
plateauRow=fixture.snapshot[0];plateauPosition=plateauRow.BodyPosition;plateauRotation=plateauRow.BodyRotation;
latticeModule.ConfigureNativePoseFixture(fixture.obj.body,(attempt,inputPosition,inputRotation)=>attempt switch {
 1=>new(new(plateauPosition.x,plateauPosition.y-poseEpsilon,plateauPosition.z),plateauRotation),
 2=>new(new(plateauPosition.x,plateauPosition.y+poseEpsilon,plateauPosition.z),plateauRotation),
 3=>new(new(plateauPosition.x,plateauPosition.y-2*poseEpsilon,plateauPosition.z),plateauRotation),
 4=>new(new(plateauPosition.x,plateauPosition.y+poseEpsilon,plateauPosition.z),plateauRotation),
 _=>new(plateauPosition,plateauRotation)});
latticeModule.RestoreExistingActorPoseFixture(plateauRow);
Check(latticeModule.NativePoseFixtureAttempts==5,
 "external native joint-pose preimage permits bounded nonmonotonic float-lattice steps before exact fifth readback");
latticeModule.Dispose();
fixture=RotationFixture();var boundedPlateauModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
plateauRow=fixture.snapshot[0];plateauPosition=plateauRow.BodyPosition;plateauRotation=plateauRow.BodyRotation;
boundedPlateauModule.ConfigureNativePoseFixture(fixture.obj.body,(attempt,inputPosition,inputRotation)=>
 new(new(plateauPosition.x,plateauPosition.y+(attempt%2==0?poseEpsilon:-poseEpsilon),plateauPosition.z),plateauRotation));
Reject(()=>boundedPlateauModule.RestoreExistingActorPoseFixture(plateauRow),"equal-size native joint-pose cycle remains bounded and requires an exact readback");
Check(boundedPlateauModule.NativePoseFixtureAttempts==5,"equal-size native joint-pose cycle stops at the dedicated five-assignment cap");
boundedPlateauModule.Dispose();
fixture=RotationFixture();var body2WorldModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
var body2WorldRow=fixture.snapshot[0];body2WorldModule.ConfigureNativeBody2WorldFixture(fixture.obj.body,"exact");
body2WorldModule.RestoreCheckpointBody2WorldFixture(body2WorldRow);
NativeBodyPoseCheckpoint.VerifyRestored(fixture.snapshot);
var body2WorldStatus=(Dictionary<string,object>)body2WorldModule.Invoke("status",new());
var body2WorldRecords=(object[])body2WorldStatus["nativeBody2WorldRestores"];
var expectedBody2WorldCaptureSize=IntPtr.Size==4?404:528;
var expectedBody2WorldRestoreSize=IntPtr.Size==4?404:440;
Check(body2WorldModule.NativeBody2WorldCaptureReceiptSize==expectedBody2WorldCaptureSize&&
 body2WorldModule.NativeBody2WorldRestoreReceiptSize==expectedBody2WorldRestoreSize,
 "native body2World managed receipt sizes match the field layout for the active pointer ABI");
Check(body2WorldModule.NativeBody2WorldFixtureCalls==1&&body2WorldModule.NativePoseFixtureAttempts==0&&
 body2WorldRecords.Length==1&&(bool)((Dictionary<string,object>)body2WorldRecords[0])["exact"],
 "exact native body2World restore bypasses the lossy public-pose inverse and records one verified receipt");
body2WorldModule.Dispose();
foreach(var failure in new[]{"control","mass","simulation","readback","sleep","motion"}) {
 fixture=RotationFixture();body2WorldModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
 body2WorldRow=fixture.snapshot[0];body2WorldModule.ConfigureNativeBody2WorldFixture(fixture.obj.body,failure);
 Reject(()=>body2WorldModule.RestoreCheckpointBody2WorldFixture(body2WorldRow),
  "native body2World restore rejects "+failure+" side effects");
 Check(body2WorldModule.NativeBody2WorldFixtureCalls==1,
  "failed native body2World "+failure+" restore remains bounded to one call");
 body2WorldModule.Dispose();
}
fixture=RotationFixture();NativeBodyPoseCheckpoint.Restore(fixture.snapshot);NativeBodyPoseCheckpoint.VerifyRestored(fixture.snapshot);
var wakeModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();var wakeRow=fixture.snapshot[0];
const uint halfWakeCounterBits=0x3F000000;fixture.obj.body.sleeping=true;
wakeModule.ConfigureNativeWakeStateFixture(fixture.obj.body,"exact",halfWakeCounterBits);
wakeModule.RestoreCheckpointWakeStateFixture(wakeRow,halfWakeCounterBits);
var wakeStatus=(Dictionary<string,object>)wakeModule.Invoke("status",new());
var wakeRecords=(object[])wakeStatus["nativeWakeStateRestores"];
Check(wakeModule.NativeWakeStateRestoreReceiptSize==(IntPtr.Size==4?76:96),
 "native wake-state managed receipt size matches the active pointer ABI");
Check(wakeModule.NativeKinematicTargetReceiptSize==(IntPtr.Size==4?84:104),
 "native kinematic-target managed receipt size matches the active pointer ABI");
Check(!fixture.obj.body.IsSleeping()&&fixture.obj.body.WakeCalls==0&&wakeModule.NativeWakeStateFixtureCalls==1&&
 (uint)((Dictionary<string,object>)wakeRecords[0])["callMask"]==1&&
 (bool)((Dictionary<string,object>)wakeRecords[0])["exact"],
 "positive checkpoint wake counter restores a sleeping body through one exact native setter receipt");
wakeModule.Dispose();
fixture=RotationFixture();NativeBodyPoseCheckpoint.Restore(fixture.snapshot);wakeRow=fixture.snapshot[0];
wakeModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();fixture.obj.body.sleeping=true;
wakeModule.ConfigureNativeWakeStateFixture(fixture.obj.body,"exact",0);
wakeModule.RestoreCheckpointWakeStateFixture(wakeRow,0);
wakeRecords=(object[])((Dictionary<string,object>)wakeModule.Invoke("status",new()))["nativeWakeStateRestores"];
Check((uint)((Dictionary<string,object>)wakeRecords[0])["callMask"]==3,
 "zero checkpoint wake counter uses the fenced wake-then-zero-counter sequence");
wakeModule.Dispose();
foreach(var failure in new[]{"counter","sleep","body-sim","mask","motion"}) {
 fixture=RotationFixture();NativeBodyPoseCheckpoint.Restore(fixture.snapshot);wakeRow=fixture.snapshot[0];
 wakeModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();fixture.obj.body.sleeping=true;
 wakeModule.ConfigureNativeWakeStateFixture(fixture.obj.body,failure,halfWakeCounterBits);
 Reject(()=>wakeModule.RestoreCheckpointWakeStateFixture(wakeRow,halfWakeCounterBits),
  "native wake-state restore rejects "+failure+" side effects");
 Check(wakeModule.NativeWakeStateFixtureCalls==1,"failed native wake-state "+failure+" restore remains bounded");
 wakeModule.Dispose();
}
EntitySerialisationRegistry.m_EntitiesList._items.Clear();var proxyProbe=Add(121).m_GameObject;
proxyProbe.objectContainer=new ObjectContainer{gameObject=proxyProbe};proxyProbe.body.isKinematic=true;
proxyProbe.body.centerOfMass=new(0,.05000001f,0);proxyProbe.body.inertiaTensor=new(0,.08166667f,0);
var probeModule=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
var probeArgs=new Dictionary<string,object>{{"entityId",121},{"expectedObjectId",proxyProbe.GetInstanceID()},{"expectedBodyId",proxyProbe.body.GetInstanceID()}};
proxyProbe.body.ResetCenter=()=>Check(!proxyProbe.body.isKinematic,"r2 probe invokes native COM reset only after clearing kinematic");
proxyProbe.body.ResetTensor=()=>Check(!proxyProbe.body.isKinematic,"r2 probe invokes native inertia reset only after clearing kinematic");
var probeReport=(Dictionary<string,object>)probeModule.Invoke("probe-empty-reset",probeArgs);
Check((bool)probeReport["ok"]&&!(bool)probeReport["massFieldsChanged"],"r2 unchanged mass outcome remains a diagnostic no-op rather than claimed fix");
Check(proxyProbe.body.isKinematic&&(bool)probeReport["originalKinematicRestored"],"r2 probe restores original kinematic state after successful calls");
Check(((List<object>)probeReport["stages"]).Count==5,"r2 records before clear COM inertia and cleanup phases");
proxyProbe.body.ResetTensor=()=>throw new InvalidOperationException("modeled reset failure");
probeReport=(Dictionary<string,object>)probeModule.Invoke("probe-empty-reset",probeArgs);
Check(!(bool)probeReport["ok"]&&probeReport.ContainsKey("error")&&proxyProbe.body.isKinematic,"r2 finally restores kinematic after a reset exception");
var wrongProbeArgs=new Dictionary<string,object>(probeArgs);wrongProbeArgs["expectedBodyId"]=-999;Events.Rows.Clear();
Reject(()=>probeModule.Invoke("probe-empty-reset",wrongProbeArgs),"r2 wrong live body identity rejects before mutation");
Check(Events.Rows.Count==0,"r2 identity rejection issues no reset");
proxyProbe.AddCollider<BoxCollider>();Reject(()=>probeModule.Invoke("probe-empty-reset",probeArgs),"r2 occupied proxy rejected before diagnostic toggle");
proxyProbe.colliders.Clear();Helpers.Paused=false;
Reject(()=>probeModule.Invoke("probe-empty-reset",probeArgs),"r2 primitive rejects unpaused native state");Helpers.Paused=true;probeModule.Dispose();
var r2Path=Path.GetFullPath("artifacts/framework-migration/native-x/body-module-r2-empty-reset.json");
using var r2Doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(r2Path));var r2Result=r2Doc.RootElement.GetProperty("result").GetProperty("result");
var r2States=r2Result.GetProperty("stages").EnumerateArray().ToDictionary(e=>e.GetProperty("phase").GetString(),e=>e.GetProperty("state"));
Check(r2Result.GetProperty("ok").GetBoolean()&&!r2Result.GetProperty("simulationInvoked").GetBoolean(),"actual X r2 primitive pins successful no-simulation diagnostic");
void SetNativeMass(Rigidbody b,System.Text.Json.JsonElement state){var m=state.GetProperty("mass");b.mass=m.GetProperty("mass").GetSingle();b.centerOfMass=V(m.GetProperty("centerOfMass"));b.inertiaTensor=V(m.GetProperty("inertiaTensor"));b.inertiaTensorRotation=Q(m.GetProperty("inertiaTensorRotation"));}
(GameObject obj,NativeBodyPoseCheckpoint.Snapshot[] saved) R3Fixture(){
 EntitySerialisationRegistry.m_EntitiesList._items.Clear();var p=Add(121).m_GameObject;p.objectContainer=new ObjectContainer{gameObject=p};
 p.body.isKinematic=true;SetNativeMass(p.body,r2States["after-clear-kinematic"]);p.body.position=V(r2States["before"].GetProperty("position"));p.body.rotation=Q(r2States["before"].GetProperty("rotation"));
 p.transform.localPosition=p.body.position;p.transform.localRotation=p.body.rotation;var s=NativeBodyPoseCheckpoint.Capture();
 SetNativeMass(p.body,r2States["before"]);p.body.OnKinematicSet=value=>{if(!value)SetNativeMass(p.body,r2States["after-clear-kinematic"]);};
 Events.Rows.Clear();return(p,s);
}
var r3=R3Fixture();var r3Module=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();r3Module.Invoke("activate",new());
NativeBodyPoseCheckpoint.Restore(r3.saved);NativeBodyPoseCheckpoint.VerifyRestored(r3.saved);
Check(r3.obj.body.centerOfMass.y==0&&r3.obj.body.inertiaTensor.y==1&&r3.obj.body.isKinematic,"r3 restores actual r2 target mass values and original flag through modeled native transition");
var r3Status=(Dictionary<string,object>)r3Module.Invoke("status",new());var r3Log=(Dictionary<string,object>)((object[])r3Status["massRestores"]).Last();
Check((bool)r3Log["emptyProxyRebuild"]&&(bool)r3Log["originalKinematicRestored"]&&(bool)r3Log["exact"],"r3 keeps transition and exact native target outcome in algorithm receipt");
r3Module.Dispose();
foreach(var rejection in new[]{"reset-throws","unchanged-after-clear","mass-change","not-container","moving","not-kinematic"}){
 r3=R3Fixture();r3Module=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();r3Module.Invoke("activate",new());
 switch(rejection){
  case "reset-throws":r3.obj.body.ResetTensor=()=>throw new InvalidOperationException("r3 modeled native reset failure");break;
  case "unchanged-after-clear":r3.obj.body.OnKinematicSet=null;break;
  case "mass-change":r3.obj.body.mass=2;break;
  case "not-container":r3.obj.objectContainer=null;break;
  case "moving":r3.obj.body.velocity=new(1,0,0);break;
  case "not-kinematic":r3.obj.body.OnKinematicSet=null;r3.obj.body.isKinematic=false;break;
 }
 bool originalFlag=r3.obj.body.isKinematic;Reject(()=>NativeBodyPoseCheckpoint.Restore(r3.saved),"r3 rejects unsupported/mismatching native transition: "+rejection);
 Check(r3.obj.body.isKinematic==originalFlag,"r3 preserves/restores original kinematic flag after rejection: "+rejection);r3Module.Dispose();
}
var r4Path=Path.GetFullPath("artifacts/framework-migration/native-x-v11/body-r3-after-spawn-owner-failure.json");
using var r4Doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(r4Path));
var r4Bridge=r4Doc.RootElement.GetProperty("records").EnumerateArray().Last().GetProperty("response").GetProperty("bridge");
var r4Saved=r4Bridge.GetProperty("nativeCheckpoints").GetProperty("requestedBodyCheckpoint").GetProperty("bodies").EnumerateArray().Single(r=>r.GetProperty("entityId").GetInt32()==104);
var r4Actual=r4Bridge.GetProperty("nativePhysics").GetProperty("bodies").EnumerateArray().Single(r=>r.GetProperty("entityId").GetInt32()==104);
Check(r4Saved.GetProperty("rigidbodyPosition").GetProperty("y").GetSingle()!=r4Actual.GetProperty("position").GetProperty("y").GetSingle(),"actual X spawn failure pins a distinct body y after provisional rotation");
Check(V(r4Saved.GetProperty("transformPosition")).Equals(V(r4Actual.GetProperty("transformPosition"))),"actual X spawn failure has the exact saved world transform position");
foreach(var failure in new[]{"success","position-noop","large-drift","transform-drift","motion-drift"}) {
 EntitySerialisationRegistry.m_EntitiesList._items.Clear();var p=Add(104).m_GameObject;p.AddCollider<CapsuleCollider>().center=new(0,1,0);
 var savedPosition=V(r4Saved.GetProperty("rigidbodyPosition"));var savedRotation=Q(r4Saved.GetProperty("rigidbodyRotation"));
 p.transform.localPosition=savedPosition;p.transform.localRotation=savedRotation;p.body.position=savedPosition;p.body.rotation=savedRotation;
 p.body.centerOfMass=V(r4Saved.GetProperty("centerOfMass"));p.body.inertiaTensor=V(r4Saved.GetProperty("inertiaTensor"));p.body.inertiaTensorRotation=Q(r4Saved.GetProperty("inertiaTensorRotation"));
 var saved=NativeBodyPoseCheckpoint.Capture();p.body.centerOfMass=V(r4Actual.GetProperty("centerOfMass"));p.body.inertiaTensorRotation=Q(r4Actual.GetProperty("inertiaTensorRotation"));p.body.rotation=new(0,0,0,1);
 ReadyReset(p,saved);int rotationCalls=0;var observedPosition=V(r4Actual.GetProperty("position"));
 p.body.RotationRoundtrip=v=>++rotationCalls==1?Q(r4Actual.GetProperty("rotation")):v;
 p.body.OnRotationSet=()=>{if(rotationCalls!=1)return;
  p.body.position=failure=="large-drift"?new(savedPosition.x,savedPosition.y-.001f,savedPosition.z):observedPosition;
  if(failure=="position-noop")p.body.PositionRoundtrip=v=>observedPosition;
  if(failure=="transform-drift")p.transform.position=new(savedPosition.x,savedPosition.y+.000001f,savedPosition.z);
  if(failure=="motion-drift")p.body.velocity=new(1,0,0);
 };
 var r4Module=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();r4Module.Invoke("activate",new());Events.Rows.Clear();
 if(failure=="success") {
  NativeBodyPoseCheckpoint.Restore(saved);NativeBodyPoseCheckpoint.VerifyRestored(saved);
  Check(p.body.position.y==savedPosition.y&&p.body.rotation.y==savedRotation.y,"r4 reapplication and post-reset rotation remain exact under captured failure model");
  var report=(Dictionary<string,object>)((object[])((Dictionary<string,object>)r4Module.Invoke("status",new()))["massRestores"]).Last();
  Check((bool)report["positionReappliedAfterProvisionalRotation"]&&(bool)report["exact"]&&report.ContainsKey("afterPositionReapplicationPose"),"r4 preserves exact target and all provisional/reapplication readbacks");
  Check(Events.Rows.IndexOf("body-position")<Events.Rows.IndexOf("reset-center"),"r4 verifies repaired body position before native reset");
 } else {
  Reject(()=>NativeBodyPoseCheckpoint.Restore(saved),"r4 fails closed for "+failure);
  Check(!Events.Rows.Any(e=>e.StartsWith("reset-")),"r4 never resets mass after a failed pose/motion prerequisite: "+failure);
 }
 r4Module.Dispose();
}
var r5Path=Path.GetFullPath("artifacts/framework-migration/native-x-v11b/body-r4-after-spawn-failure.json");
using var r5Doc=System.Text.Json.JsonDocument.Parse(File.ReadAllText(r5Path));
var r5Mass=r5Doc.RootElement.GetProperty("result").GetProperty("result").GetProperty("massRestores").EnumerateArray().Last();
var r5Target=V(r5Mass.GetProperty("targetPose").GetProperty("bodyPosition"));
var r5Readback=V(r5Mass.GetProperty("afterPositionReapplicationPose").GetProperty("bodyPosition"));
Check(r5Mass.GetProperty("positionReappliedAfterProvisionalRotation").GetBoolean()&&!r5Mass.GetProperty("resetCenterOfMass").GetBoolean()&&r5Target.y!=r5Readback.y,"actual R4 exact-position assignment failed before any mass reset");
foreach(var mode in new[]{"captured-linear-residual","nonfinite","shrinking-no-exact"}) {
 EntitySerialisationRegistry.m_EntitiesList._items.Clear();var p=Add(104).m_GameObject;p.AddCollider<CapsuleCollider>().center=new(0,1,0);
 var targetQ=Q(r5Mass.GetProperty("targetPose").GetProperty("bodyRotation"));p.transform.localPosition=r5Target;p.transform.localRotation=targetQ;
 p.body.position=r5Target;p.body.rotation=targetQ;p.body.centerOfMass=new(0,1,0);p.body.inertiaTensor=new(0,0,0);p.body.inertiaTensorRotation=new(0,0,0,1);
 var saved=NativeBodyPoseCheckpoint.Capture();p.body.centerOfMass=new(0,.985052f,.136409953f);p.body.rotation=new(0,0,0,1);ReadyReset(p,saved);
 int rotations=0,positions=0;var offset=r5Readback.y-r5Target.y;
 p.body.OnRotationSet=()=>{if(++rotations!=1)return;p.body.position=V(r5Mass.GetProperty("afterProvisionalRotationPose").GetProperty("bodyPosition"));
  p.body.PositionRoundtrip=v=>{positions++;return mode=="nonfinite"?new(v.x,float.NaN,v.z):mode=="shrinking-no-exact"?new(v.x,r5Target.y-offset/(1<<positions),v.z):new(v.x,v.y+offset,v.z);};};
 var module=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();module.Invoke("activate",new());Events.Rows.Clear();
 if(mode=="captured-linear-residual") {
  NativeBodyPoseCheckpoint.Restore(saved);NativeBodyPoseCheckpoint.VerifyRestored(saved);
  Check(positions==2&&p.body.position.y==r5Target.y,"R5 modeled captured native position residual resolves exactly in two bounded assignments");
  var log=(Dictionary<string,object>)((object[])((Dictionary<string,object>)module.Invoke("status",new()))["massRestores"]).Last();
  var positionAttempts=(List<object>)log["positionPreimageAttempts"];
  Check(positionAttempts.Count==2&&!(bool)((Dictionary<string,object>)positionAttempts[0])["exact"]&&(bool)((Dictionary<string,object>)positionAttempts[1])["exact"],"R5 retains failed and exact readbacks without redefining the target");
 } else {
  Reject(()=>NativeBodyPoseCheckpoint.Restore(saved),"R5 rejects "+mode);
  Check(positions<=4&&!Events.Rows.Any(e=>e.StartsWith("reset-")),"R5 bounds all position attempts and never resets with wrong pose: "+mode);
 }
 module.Dispose();
}
{
 var module=new SuperchargedPatch.Authoring.Modules.BodyRestoreModule();
 Check(module.PendingSleepNotificationLifecycleFixture("success"),
  "Deferred mass restore admits the exact PhysX sleep-notification intermediate");
 foreach(var rejection in new[]{"flags","sleep-index","not-ready-change","identity","wake-index"})
  Check(!module.PendingSleepNotificationLifecycleFixture(rejection),
   "Deferred mass restore rejects altered sleep-notification lifecycle: "+rejection);
 module.Dispose();
}
var productionPaths=new[]{"framework/patch/NativeBodyPoseCheckpoint.cs","framework/patch/NativeBodyColliderCheckpoint.cs","framework/patch/NativeBodyMassMode.cs","framework/patch/NativeKitchenCheckpoint.cs", "framework/patch/IBodyRestoreStrategy.cs","framework/patch/NativeBodyRestoreDispatch.cs","framework/modules/body-restore/BodyRestoreModule.cs","framework/modules/body-restore/NativeShapePoseRestore.cs","framework/modules/body-restore/EmptyProxyResetProbe.cs","framework/modules/body-restore/EmptyProxyRestore.cs"};
var sourcePath=Path.GetFullPath("framework/patch/NativeBodyPoseCheckpoint.cs");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks=checks.Count,names=checks,
 sourceSha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(sourcePath))),
 sources=productionPaths.ToDictionary(p=>p,p=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(p)))),
 fixture=new{path=vPath,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(vPath)))},
 r2Fixture=new{path=r2Path,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(r2Path)))},
 r4Fixture=new{path=r4Path,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(r4Path)))},
 r5Fixture=new{path=r5Path,sha256=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(r5Path)))},
 scope="Production body/collider/mode helper with controlled registry and independent Rigidbody/Transform stand-ins. Actual V mass values plus modeled native reset callbacks test policy; no installed PhysX reset equivalence, automatic mode or native continuation proof."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
