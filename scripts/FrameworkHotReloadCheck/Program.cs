using SuperchargedPatch.Authoring;
using System.Security.Cryptography;
using System.Text.Json;

string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);
string one=Path.GetFullPath(args[1]),two=Path.GetFullPath(args[2]);
File.Copy(one,Path.Combine(root,"one.dll"),true);File.Copy(two,Path.Combine(root,"two.dll"),true);
string Hash(string p)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))).ToLowerInvariant();
bool paused=true;int count=0;
var host=new AuthoringModuleHost(root,()=>{if(!paused)throw new InvalidOperationException("running");});
void Check(bool passed,string label){if(!passed)throw new Exception(label);count++;}
void Reject(Action operation,string label){try{operation();}catch{count++;return;}throw new Exception("Accepted: "+label);}
string Result(object value)=>((Dictionary<string,object>)value)["result"].ToString();
host.Load("restore","one.dll","Module",Hash(one));
Check(Result(host.Invoke("restore","read",null))=="revision-one","first DLL executed");
string receipt=Path.Combine(root,"retired.txt");
if(File.Exists(receipt))File.Delete(receipt);
host.Invoke("restore","configure",new Dictionary<string,object>{{"receipt",receipt}});
Reject(()=>host.Load("restore","two.dll","Module",Hash(two),new string('0',64)),"different permanent core");
Reject(()=>host.Load("restore","two.dll","Module",new string('0',64)),"wrong hash");
Check(Result(host.Invoke("restore","read",null))=="revision-one","hash failure retains old code");
Reject(()=>host.Load("restore","one.dll","Module",Hash(one)),"same CLR identity");
Check(!File.Exists(receipt),"rejected load did not retire old code");
paused=false;
Reject(()=>host.Load("restore","two.dll","Module",Hash(two)),"load while running");
Reject(()=>host.Invoke("restore","read",null),"invoke while running");
paused=true;
Exception foreign=null;var thread=new Thread(()=>{try{host.Invoke("restore","read",null);}catch(Exception e){foreign=e;}});thread.Start();thread.Join();
Check(foreign!=null,"foreign thread rejected");
host.Load("restore","two.dll","Module",Hash(two));
Check(File.ReadAllText(receipt)=="revision-one","replacement disposes previous module");
Check(Result(host.Invoke("restore","read",null))=="revision-two","second DLL executed in same host");
Reject(()=>host.Invoke("restore","fail",null),"exception reported");
Check(Result(host.Invoke("restore","read",null))=="revision-two","exception releases busy gate");
host.Unload("restore");
Reject(()=>host.Invoke("restore","read",null),"retired module unavailable");
Reject(()=>host.Load("restore","../one.dll","Module",Hash(one)),"outside module directory");
Check(((List<object>)((Dictionary<string,object>)host.Describe())["active"]).Count==0,"no active callbacks after retire");
Console.WriteLine(JsonSerializer.Serialize(new{passed=true,checks=count,scope="Production host dynamic assembly loading, replacement, disposal and rejection; native game tested separately",state=host.Describe()}));
