using System.Collections;
using System.Reflection;
using SuperchargedPatch;
using UnityEngine;
using Team17.Online.Multiplayer.Messaging;

var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Reject(Action action,string name){try{action();}catch(InvalidOperationException){checks.Add(name);return;}throw new Exception("Expected rejection: "+name);}
void CaptureFields(NativeCannonFlightCheckpoint.Snapshot s,State state) {
 try {typeof(NativeCannonFlightCheckpoint).GetMethod("CaptureFields",BindingFlags.NonPublic|BindingFlags.Static).Invoke(null,new object[]{s,typeof(State),state,null});}
 catch(TargetInvocationException e){throw e.InnerException;}
}
NativeCannonFlightCheckpoint.Snapshot Fixture() {
 var obj=new GameObject();var cannon=new GameObject();var entry=new EntitySerialisationEntry();entry.m_Header.m_uEntityID=103;EntitySerialisationRegistry.Entries[obj]=entry;
 var iterator=new Flight{Progress=.25f,Owner=obj};
 var copy=new NativeIteratorCopy(new(){{typeof(Flight),new[]{"Progress","Owner"}}},v=>ReferenceEquals(v,obj));
 return new(){Server=cannon.Add<ServerCannon>(),Client=cannon.Add<ClientCannon>(),Passenger=obj,PassengerEntry=entry,Controls=obj.Add<PlayerControls>(),
  Body=obj.Add<Rigidbody>(),Collider=obj.Add<Collider>(),Parenting=obj.Add<DynamicLandscapeParenting>(),ControlsEnabled=false,ColliderEnabled=false,
  Kinematic=true,ParentingEnabled=false,Position=new(10,2,-19),Rotation=new(.1f,.2f,.3f,.4f),Velocity=new(.1f,0,.2f),AngularVelocity=new(0,.3f,0),
  Iterator=copy.Capture(iterator),Time=.25f,Duration=1};
}
var saved=Fixture();var state=new State{Clock=3,Reference=new GameObject(),Payload=new(){NetworkTime=7,ClientTimeStamp=9,Velocity=new(1,2,3),WorldObject=new(){LocalPosition=new(4,5,6),HasPositions=true,ParentEntityID=19}}};
CaptureFields(saved,state);
var captured=(ChefPositionMessage)saved.States[0].Values[Array.FindIndex(saved.States[0].Members,f=>f.Name=="Payload")];
Check(!ReferenceEquals(state.Payload,captured)&&!ReferenceEquals(state.Payload.WorldObject,captured.WorldObject),"native chef/world payloads captured independently");
state.Payload.NetworkTime=99;state.Payload.WorldObject.ParentEntityID=25;state.Clock=99;
saved.Client.m_launches.Add(new Flight{Progress=.9f});
NativeCannonFlightCheckpoint.Restore(saved);
Check(state.Clock==3&&state.Payload.NetworkTime==7&&state.Payload.WorldObject.ParentEntityID==19,"restore uses immutable clocks and nested message values");
Check(!ReferenceEquals(state.Payload,captured)&&!ReferenceEquals(state.Payload.WorldObject,captured.WorldObject),"first restore does not expose snapshot mutable payload");
Check(saved.Client.m_launches.Count==1&&((Flight)saved.Client.m_launches[0]).Progress==.25f,"one fresh saved iterator replaces future launch list");
Check(saved.Body.position.Equals(saved.Position)&&saved.Body.rotation.Equals(saved.Rotation)&&saved.Body.velocity.Equals(saved.Velocity)&&saved.Body.angularVelocity.Equals(saved.AngularVelocity),"native body pose and velocity fields restored exactly");
Check(saved.Body.isKinematic&&!saved.Controls.enabled&&!saved.Collider.enabled&&!saved.Parenting.enabled,"saved flight control and body flags restored");
Check(saved.Passenger.transform.parent==null,"saved unparented passenger restored");
var first=state.Payload;var firstIterator=(Flight)saved.Client.m_launches[0];
state.Payload.WorldObject.ParentEntityID=100;firstIterator.Progress=.8f;NativeCannonFlightCheckpoint.Restore(saved);
Check(!ReferenceEquals(first,state.Payload)&&state.Payload.WorldObject.ParentEntityID==19,"second restore replaces independently mutated message payload");
Check(!ReferenceEquals(firstIterator,saved.Client.m_launches[0])&&((Flight)saved.Client.m_launches[0]).Progress==.25f,"second restore creates independent immutable iterator");
Check(Flight.Moves==0&&Flight.Disposals==0,"restore invokes no iterator continuation or disposal");
Check(NativeCannonFlightCheckpoint.Same(saved,saved),"identical stored flight snapshot passes exact comparator");
Check(!NativeCannonFlightCheckpoint.Same(saved,null)&&NativeCannonFlightCheckpoint.Same(null,null),"absent and present flight snapshots remain distinct");
var same=Fixture();same.Passenger=saved.Passenger;same.PassengerEntry=saved.PassengerEntry;same.Server=saved.Server;same.Client=saved.Client;
same.Iterator=saved.Iterator;CaptureFields(same,state);
Check(NativeCannonFlightCheckpoint.Same(saved,same),"fresh field capture compares nested payload values");
state.Payload.ClientTimeStamp=10;var changed=Fixture();changed.Passenger=saved.Passenger;changed.PassengerEntry=saved.PassengerEntry;changed.Server=saved.Server;changed.Iterator=saved.Iterator;CaptureFields(changed,state);
Check(!NativeCannonFlightCheckpoint.Same(saved,changed),"native message timestamp drift remains visible");
var originalEntry=saved.PassengerEntry;EntitySerialisationRegistry.Entries[saved.Passenger]=new EntitySerialisationEntry();
Reject(()=>NativeCannonFlightCheckpoint.Restore(saved),"passenger registration incarnation replacement rejects before restore");
EntitySerialisationRegistry.Entries[saved.Passenger]=originalEntry;
state.Reference.Destroyed=true;
Reject(()=>NativeCannonFlightCheckpoint.Validate(saved),"destroyed pinned sync owner reference rejected");
state.Reference.Destroyed=false;
saved.Body.Destroyed=true;Reject(()=>NativeCannonFlightCheckpoint.Validate(saved),"destroyed exact passenger body rejected");saved.Body.Destroyed=false;
state.Clock=float.NaN;Reject(()=>CaptureFields(Fixture(),state),"nonfinite native scalar fails snapshot capture");state.Clock=3;
state.Reference.Destroyed=true;Reject(()=>CaptureFields(Fixture(),state),"destroyed native reference fails snapshot capture");
var unrecognised=new GameObject().Add<ServerCannon>();unrecognised.flying=true;
Check(NativeCannonFlightCheckpoint.TryCapture(unrecognised)==null&&NativeCannonFlightCheckpoint.LastScopeReason.Contains("native module"),"unknown installed module rejects before native iterator access");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks=checks.Count,names=checks,scope="Production flight state copy/restore/comparison with controlled native component/message/registry stand-ins. Native admission and Unity continuation are not executed."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));

class State {public float Clock;public GameObject Reference;public ChefPositionMessage Payload;}
class Flight:IEnumerator,IDisposable {
 public static int Moves,Disposals;public float Progress;public object Owner;public object Current=>null;
 public bool MoveNext(){Moves++;return true;}public void Reset()=>throw new NotSupportedException();public void Dispose(){Disposals++;}
}
