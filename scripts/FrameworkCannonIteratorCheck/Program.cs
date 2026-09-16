using System.Collections;
using SuperchargedPatch;
using System.Reflection;

var checks=new List<string>();
void Check(bool ok,string text){if(!ok)throw new Exception(text);checks.Add(text);}
void Reject(Action action,string text){try{action();}catch(InvalidOperationException){checks.Add(text);return;}throw new Exception("Expected rejection: "+text);}
var owner=new object();var target=new object();
string[] Schema(Type type)=>type.GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly).Select(f=>f.Name).ToArray();
NativeIteratorCopy Copier()=>new(new(){{typeof(Flight),Schema(typeof(Flight))},{typeof(Animation),Schema(typeof(Animation))}},value=>ReferenceEquals(value,owner)||ReferenceEquals(value,target));
var animation=new Animation{Time=.4f,End=1f,Owner=owner,Target=target};
var flight=new Flight{Child=animation,Owner=owner};var copier=Copier();var saved=copier.Capture(flight);
Check(Flight.MoveCalls==0&&Animation.MoveCalls==0&&Flight.DisposeCalls==0,"capture invokes no native MoveNext/Dispose");
Check(saved.Matches(flight),"captured running iterator values match");
animation.Time=.7f;flight.State=2;
Check(!saved.Matches(flight),"future original iterator mutation is detected");
var restored=(Flight)saved.RestoreCopy();var restoredAnimation=(Animation)restored.Child;
Check(restored.State==1&&restoredAnimation.Time==.4f,"saved phase and native progress restored");
Check(!ReferenceEquals(restored,flight)&&!ReferenceEquals(restoredAnimation,animation),"nested iterator objects are copied independently");
Check(ReferenceEquals(restored.Owner,owner)&&ReferenceEquals(restoredAnimation.Target,target),"native owner and target references remain exact");
restoredAnimation.Time=.9f;restored.State=3;
var again=(Flight)saved.RestoreCopy();
Check(again.State==1&&((Animation)again.Child).Time==.4f,"repeated restore never mutates saved checkpoint");
Check(saved.Matches(again),"fresh restore matches immutable checkpoint");
again.MoveNext();
Check(Flight.MoveCalls==1&&Animation.MoveCalls==1&&((Animation)again.Child).Time==.5f,"only ordinary continuation advances copied iterator");
Check(((Animation)((Flight)saved.RestoreCopy()).Child).Time==.4f,"advancing one continuation cannot consume future replays");
var wrongTarget=(Flight)saved.RestoreCopy();((Animation)wrongTarget.Child).Target=new object();
Check(!saved.Matches(wrongTarget),"replacement native target is not equal");
Reject(()=>copier.Capture(wrongTarget),"unapproved mutable reference is rejected at capture");
Reject(()=>new NativeIteratorCopy(new(){{typeof(Flight),Schema(typeof(Flight)).Skip(1).ToArray()}},_=>true),"removed schema field fails preflight");
Reject(()=>new NativeIteratorCopy(new(){{typeof(Flight),Schema(typeof(Flight)).Concat(new[]{"unknown"}).ToArray()}},_=>true),"added schema field fails preflight");
Reject(()=>new NativeIteratorCopy(new(){{typeof(object),Array.Empty<string>()}},_=>true),"noniterator type cannot enter clone whitelist");
Reject(()=>copier.Capture(new Foreign()),"unknown native iterator implementation rejected");
var unknownYield=new Flight{Owner=owner,Child=new Animation{Owner=owner,Target=target},Yield=new List<int>()};
Reject(()=>copier.Capture(unknownYield),"external scheduler/yield object is not shallow-copied");
var managedValue=new Flight{Owner=owner,Child=new Animation{Owner=owner,Target=target},Yield=new ManagedValue{Value=new List<int>()}};
Reject(()=>copier.Capture(managedValue),"value-type wrapper cannot hide mutable managed state");
var alias=new Alias{First=new Animation{Owner=owner,Target=target}};alias.Second=alias.First;
var aliasCopier=new NativeIteratorCopy(new(){{typeof(Alias),Schema(typeof(Alias))},{typeof(Animation),Schema(typeof(Animation))}},value=>ReferenceEquals(value,owner)||ReferenceEquals(value,target));
var aliasRestore=(Alias)aliasCopier.Capture(alias).RestoreCopy();
Check(ReferenceEquals(aliasRestore.First,aliasRestore.Second)&&!ReferenceEquals(aliasRestore.First,alias.First),"nested alias identity is preserved without sharing original iterator");
IEnumerator deep=new Animation{Owner=owner,Target=target};for(int i=0;i<12;i++)deep=new Flight{Owner=owner,Child=deep};
Reject(()=>copier.Capture(deep),"excessive iterator graph rejected before restore");
Check(Flight.DisposeCalls==0,"failed capture does not invoke native disposal side effects");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks=checks.Count,names=checks,scope="Actual iterator-copy helper with controlled managed iterators; no Unity/native flight execution."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));

class Flight:IEnumerator,IDisposable{
 public static int MoveCalls,DisposeCalls;public int State=1;public IEnumerator Child;public object Owner;public object Yield;
 public object Current=>Yield;public bool MoveNext(){MoveCalls++;Child?.MoveNext();return true;}public void Reset()=>throw new NotSupportedException();public void Dispose(){DisposeCalls++;}
}
class Animation:IEnumerator{
 public static int MoveCalls;public float Time,End;public object Owner,Target;
 public object Current=>null;public bool MoveNext(){MoveCalls++;Time+=.1f;return Time<End;}public void Reset()=>throw new NotSupportedException();
}
class Foreign:IEnumerator{public object Current=>null;public bool MoveNext()=>true;public void Reset(){}}
class Alias:IEnumerator{public IEnumerator First,Second;public object Current=>null;public bool MoveNext()=>true;public void Reset(){}}
struct ManagedValue{public List<int> Value;}
