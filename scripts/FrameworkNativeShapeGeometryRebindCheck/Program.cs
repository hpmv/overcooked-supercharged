using SuperchargedPatch.Authoring.Modules;

var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
void Reject(Action action,string name)
{
    try{action();}catch(InvalidOperationException){checks.Add(name);return;}
    throw new Exception("Expected rejection: "+name);
}
UIntPtr[] P(params uint[] values)=>values.Select(value=>new UIntPtr(value)).ToArray();
NativeShapeGeometryRebindPlan.RecreatedPair Pair(object historical,object current)=>new(){Historical=historical,Current=current};

var oldTrigger=new HostileEquality("old-trigger");
var oldSolid=new HostileEquality("old-solid");
var survivor=new HostileEquality("survivor");
var newTrigger=new HostileEquality("new-trigger");
var newSolid=new HostileEquality("new-solid");

// Managed order deliberately differs from actor order.  The proof must join
// through each Collider's exact PxShape binding, not array position or Equals.
object[] historical={oldTrigger,survivor,oldSolid};
UIntPtr[] historicalBindings=P(0x30,0x10,0x20);
UIntPtr[] historicalActor=P(0x10,0x20,0x30);
object[] current={newSolid,newTrigger,survivor};
UIntPtr[] currentBindings=P(0x90,0x80,0x10);
UIntPtr[] currentActor=P(0x10,0x90,0x80);
var pairs=new[]{Pair(oldSolid,newSolid),Pair(oldTrigger,newTrigger)};

var plan=NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,currentBindings,currentActor,pairs,2);
Check(plan.RecreatedByActorIndex.SequenceEqual(new[]{false,true,true}),
    "exact semantic actor order identifies the two recreated rows");
Check(plan.HistoricalManagedIndexByActorIndex.SequenceEqual(new[]{1,2,0})
    &&plan.CurrentManagedIndexByActorIndex.SequenceEqual(new[]{2,0,1}),
    "actor roles join through exact historical and current Collider bindings");
Check(HostileEquality.EqualityCalls==0,
    "destroyed-wrapper-safe role proof uses reference identity and never Equals/operator equality");

var oldSingle=new HostileEquality("old-single");
var newSingle=new HostileEquality("new-single");
var singleSurvivor=new HostileEquality("single-survivor");
var singlePlan=NativeShapeGeometryRebindPlan.Build(
    new object[]{oldSingle,singleSurvivor},P(0x40,0x50),P(0x50,0x40),
    new object[]{singleSurvivor,newSingle},P(0x50,0x60),P(0x50,0x60),
    new[]{Pair(oldSingle,newSingle)},1);
Check(singlePlan.RecreatedByActorIndex.SequenceEqual(new[]{false,true})
    &&singlePlan.HistoricalManagedIndexByActorIndex.SequenceEqual(new[]{1,0})
    &&singlePlan.CurrentManagedIndexByActorIndex.SequenceEqual(new[]{0,1}),
    "one recreated row is proven exactly while the other actor row survives by reference identity");

Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,currentBindings,currentActor,new[]{pairs[0]},2),"missing recreated pair fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,currentBindings,currentActor,new[]{pairs[0],Pair(oldSolid,newTrigger)},2),
    "duplicated historical role fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,currentBindings,currentActor,new[]{pairs[0],Pair(oldTrigger,newSolid)},2),
    "duplicated current role fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,currentBindings,P(0x10,0x80,0x90),pairs,2),
    "different semantic actor order fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,P(0x90,0x81,0x10),currentActor,pairs,2),
    "current Collider-to-PxShape set mismatch fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,P(0x30,0x10,0x10),historicalActor,
    current,currentBindings,currentActor,pairs,2),"duplicate historical binding fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,currentBindings,P(0x10,0x90,0x90),pairs,2),"duplicate actor shape fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    new object[]{newSolid,newTrigger,new HostileEquality("replacement-survivor")},currentBindings,currentActor,pairs,2),
    "unmapped surviving Collider reincarnation fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(historical,historicalBindings,historicalActor,
    current,currentBindings,currentActor,new[]{Pair(oldSolid,oldSolid),pairs[1]},2),
    "pair that is not a true Collider reincarnation fails closed");
Reject(()=>NativeShapeGeometryRebindPlan.Build(Array.Empty<object>(),Array.Empty<UIntPtr>(),Array.Empty<UIntPtr>(),
    Array.Empty<object>(),Array.Empty<UIntPtr>(),Array.Empty<UIntPtr>(),Array.Empty<NativeShapeGeometryRebindPlan.RecreatedPair>(),0),
    "empty topology fails closed");

Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {passed=true,checkCount=checks.Count,checks},
    new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));

sealed class HostileEquality
{
    internal static int EqualityCalls;
    readonly string name;
    internal HostileEquality(string name){this.name=name;}
    public override bool Equals(object value){EqualityCalls++;return true;}
    public override int GetHashCode(){EqualityCalls++;return 0;}
    public override string ToString()=>name;
}
