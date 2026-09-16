using SuperchargedPatch.Authoring.Modules;
using Team17.Online.Multiplayer.Messaging;
using UnityEngine;

int checks=0;
void Check(bool value,string name){if(!value)throw new Exception(name);checks++;}
void Reject(Action run,string name){try{run();}catch(ArgumentException){checks++;return;}catch(InvalidOperationException){checks++;return;}catch(OverflowException){checks++;return;}throw new Exception("Missing rejection: "+name);}
Dictionary<string,object> Map(params object[] pairs){var d=new Dictionary<string,object>();for(int i=0;i<pairs.Length;i+=2)d.Add((string)pairs[i],pairs[i+1]);return d;}
var obj=new GameObject();var body=new TestBody();obj.components.Add(body);
EntitySerialisationRegistry.m_EntitiesList._items=new[]{new Entry {m_Header=new Header {m_uEntityID=103},m_GameObject=obj}};
var module=new InspectionModule();
Dictionary<string,object> Args()=>Map("entityId",103L,"component",typeof(TestBody).FullName,"expectedObjectId",(long)obj.GetInstanceID(),"expectedComponentId",(long)body.GetInstanceID());
Dictionary<string,object> Field(string name){var a=Args();a["kind"]="field";a["name"]=name;return a;}
Dictionary<string,object> Set(string name,object value){var a=Field(name);a["value"]=value;return a;}
Dictionary<string,object> Invoke(string name){var a=Args();a["method"]=name;a["members"]=new object[]{Map("kind","field","name","mass")};return a;}
var catalog=(Dictionary<string,object>)module.Invoke("inspect",Map("entityId",103L));
Check((bool)catalog["ok"]&&!(bool)catalog["mutationAttempted"],"component catalog is read-only");
var inspect=Args();inspect["members"]=new object[]{Map("kind","field","name","inherited","declaringType",typeof(BaseBody).FullName)};
Check((bool)((Dictionary<string,object>)module.Invoke("inspect",inspect))["ok"],"private ancestor field readable");
var change=(Dictionary<string,object>)module.Invoke("set",Set("mass",2.5));
Check((bool)change["ok"]&&body.mass==2.5f,"typed scalar write");
Check(change.ContainsKey("beforeValue")&&change.ContainsKey("afterValue"),"set exact before and after receipts");
var vector=Map("x",1.0,"y",2.0,"z",3.0);
Check((bool)((Dictionary<string,object>)module.Invoke("set",Set("center",vector)))["ok"]&&body.center.z==3,"typed Vector3");
Check((bool)((Dictionary<string,object>)module.Invoke("set",Set("rotation",Map("x",0.0,"y",0.0,"z",0.0,"w",1.0))))["ok"],"typed Quaternion");
Check((bool)((Dictionary<string,object>)module.Invoke("set",Set("flag",true)))["ok"]&&body.flag,"typed boolean");
var prop=Args();prop["kind"]="property";prop["name"]="PrivateSetting";prop["value"]=7L;
Check((bool)((Dictionary<string,object>)module.Invoke("set",prop))["ok"]&&body.Setting==7,"private property setter");
var reset=(Dictionary<string,object>)module.Invoke("invoke",Invoke("ResetCenterOfMass"));
Check((bool)reset["ok"]&&body.mass==1&&body.resets==1,"explicit native-style no-arg method");
Check(!(bool)reset["rollbackPerformed"],"no invented rollback");
var stale=Set("mass",3.0);stale["expectedObjectId"]=-999L;Reject(()=>module.Invoke("set",stale),"wrong GameObject incarnation");
stale=Set("mass",3.0);stale["expectedComponentId"]=-999L;Reject(()=>module.Invoke("set",stale),"wrong component incarnation");
stale=Set("mass",3.0);stale.Remove("expectedObjectId");Reject(()=>module.Invoke("set",stale),"missing write object identity");
stale=Invoke("ResetCenterOfMass");stale.Remove("expectedComponentId");Reject(()=>module.Invoke("invoke",stale),"missing invoke component identity");
Reject(()=>module.Invoke("set",Set("mass",Double.NaN)),"nonfinite scalar");
Reject(()=>module.Invoke("set",Set("count",1.5)),"fractional integer");
Reject(()=>module.Invoke("set",Set("count",long.MaxValue)),"integer overflow");
Reject(()=>module.Invoke("set",Set("readOnly",1L)),"readonly field");
Reject(()=>module.Invoke("set",Set("reference",null)),"arbitrary reference write");
Reject(()=>module.Invoke("set",Set("center",Map("x",1.0,"y",2.0))),"incomplete vector");
Reject(()=>module.Invoke("invoke",Invoke("NeedsArgument")),"method arguments unsupported");
var noObserve=Invoke("ResetCenterOfMass");noObserve.Remove("members");Reject(()=>module.Invoke("invoke",noObserve),"invoke requires observed members");
var throwing=(Dictionary<string,object>)module.Invoke("invoke",Invoke("ThrowAfterChange"));
Check(!(bool)throwing["ok"]&&(bool)throwing["mutationAttempted"]&&body.mass==9,"failed invocation retains actual mutation");
Check(throwing.ContainsKey("after")&&throwing.ContainsKey("error"),"failure has after receipt and error");
var bad=Set("count",.5);float before=body.mass;
Reject(()=>module.Invoke("batch",Map("operations",new object[]{Map("operation","set","args",Set("mass",4.0)),Map("operation","set","args",bad)})),"whole batch type preflight");
Check(body.mass==before,"preflight failure applies no first mutation");
var batch=(Dictionary<string,object>)module.Invoke("batch",Map("operations",new object[]{Map("operation","set","args",Set("mass",4.0)),Map("operation","invoke","args",Invoke("ResetCenterOfMass"))}));
Check((bool)batch["ok"]&&body.mass==1,"ordered explicit batch");
var second=new TestBody();obj.components.Add(second);
Reject(()=>module.Invoke("inspect",Map("entityId",103L,"component",typeof(TestBody).FullName)),"ambiguous same-type component");
Check((bool)((Dictionary<string,object>)module.Invoke("inspect",Args()))["ok"],"instance ID disambiguates exact component");
obj.components.Remove(second);
body.callback=()=>EntitySerialisationRegistry.m_EntitiesList._items[0].m_GameObject=new GameObject();
var changed=(Dictionary<string,object>)module.Invoke("invoke",Invoke("ChangeIdentity"));
Check(!(bool)changed["ok"]&&changed.ContainsKey("afterError"),"native method replacing registered object fails after identity check");
module.Dispose();Reject(()=>module.Invoke("inspect",Map("entityId",103L)),"retired module cannot run");
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{passed=true,checks,gameCalls=0,scope="Controlled registry/component reflection adapter; real Unity method effects need native receipts"}));

class BaseBody:Component {private int inherited=5;public int Proof=>inherited;}
class TestBody:BaseBody
{
    public float mass=1;public Vector3 center;public Quaternion rotation;public bool flag;public int count;
    public readonly int readOnly=4;public object reference;public int resets;public Action callback;
    private int PrivateSetting {get;set;}public int Setting=>PrivateSetting;
    public void ResetCenterOfMass(){mass=1;resets++;}
    public void NeedsArgument(int value){mass=value;}
    public void ThrowAfterChange(){mass=9;throw new InvalidOperationException("intentional method failure");}
    public void ChangeIdentity(){callback();}
}
