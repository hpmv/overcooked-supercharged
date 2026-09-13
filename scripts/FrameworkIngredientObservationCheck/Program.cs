using Mono.Cecil;
using System.Security.Cryptography;
using System.Text.Json;
using SuperchargedPatch;

int checks=0;
void Check(bool value,string reason) { if(!value) throw new Exception(reason);checks++; }
void Reject(Action action,string reason) { try { action(); } catch(ArgumentNullException) { checks++;return; } catch(InvalidOperationException) { checks++;return; } throw new Exception(reason); }
var source=new ServerIngredientContainer();
var empty=NativeIngredientContentsObservation.Capture(source);
Check(source.Reads==1 && empty.Contents is {Length:0},"Initial empty must be read, not inferred from absent messages.");
var ingredient=new AssembledDefinitionNode {Identity=284626};
source.Contents.Add(ingredient);
Check(empty.Contents.Length==0,"Later source changes cannot overwrite the captured array.");
var occupied=NativeIngredientContentsObservation.Capture(source);
Check(occupied.Contents.Length==1 && ReferenceEquals(occupied.Contents[0],ingredient),"Actual native node identity survives observation; no flatten/rebuild.");
source.Contents.Add(ingredient);
var duplicate=NativeIngredientContentsObservation.Capture(source);
Check(duplicate.Contents.Length==2 && ReferenceEquals(duplicate.Contents[0],duplicate.Contents[1]),"Duplicate ingredients are preserved, not deduplicated.");
Check(!ReferenceEquals(occupied,duplicate) && !ReferenceEquals(occupied.Contents,duplicate.Contents),"Fresh messages and arrays isolate observations from future reads.");
occupied.Contents[0]=null;
Check(source.Contents.Count==2 && source.Contents.All(x=>ReferenceEquals(x,ingredient)),"Observation array cannot change the native collection.");
source.Unavailable=true;
Reject(()=>NativeIngredientContentsObservation.Capture(source),"Unknown contents must not become a fabricated empty container.");
Reject(()=>NativeIngredientContentsObservation.Capture(null),"Missing component must not become a fabricated empty container.");

string nativePath="runtime/Overcooked2_Data/Managed/Assembly-CSharp.dll";
using var native=AssemblyDefinition.ReadAssembly(nativePath);
MethodDefinition Method(AssemblyDefinition assembly,string type,string method)=>assembly.MainModule.Types.Single(t=>t.Name==type).Methods.Single(m=>m.Name==method);
string[] Calls(MethodDefinition method)=>method.Body.Instructions.Where(i=>i.Operand is MethodReference).Select(i=>((MethodReference)i.Operand).FullName).ToArray();
var getter=Method(native,"ServerIngredientContainer","GetContents");
Check(Calls(getter).Length==1 && Calls(getter)[0].Contains("::ToArray()") && !getter.Body.Instructions.Any(i=>i.OpCode.Name.StartsWith("stfld")),"Installed GetContents only reads a copied native array.");
var update=Calls(Method(native,"ServerIngredientContainer","GetServerUpdate"));
Check(update.Any(x=>x.Contains("Initialise(System.Boolean)")) && !update.Any(x=>x.Contains("GetContents")),"Installed update reports active-state, not composition.");
Check(Calls(Method(native,"ServerIngredientContainer","OnContentsChanged")).Any(x=>x.Contains("SendServerEvent")),"Native contents callback is a game event and must not be used for observation.");
string builtPath="artifacts/framework-build/SuperchargedPatch.dll";
using var built=AssemblyDefinition.ReadAssembly(builtPath);
var capture=Calls(Method(built,"NativeIngredientContentsObservation","Capture"));
Check(capture.Count(x=>x.Contains("ServerIngredientContainer::GetContents()"))==1,"Built producer reads actual native source exactly once.");
Check(capture.Count(x=>x.Contains("IngredientContainerMessage::Initialise(AssembledDefinitionNode[])"))==1,"Built producer uses the native array overload.");
Check(!capture.Any(x=>x.Contains("GetServerUpdate") || x.Contains("SendServerEvent") || x.Contains("OnContentsChanged") || x.Contains("SetContents") || x.Contains("Empty()")),"Built producer has no native mutation/callback path.");
Check(Calls(Method(built,"ActiveStateCollector","CollectDataForFrame")).Any(x=>x.Contains("NativeIngredientContentsObservation::Capture")),"Collector is wired to the built observation producer.");
using var differences=JsonDocument.Parse(File.ReadAllText("artifacts/framework-migration/native-m/idle-probe/entity-differences-0.json"));
Check(Enumerable.Range(2,12).All(id=> {
    var d=differences.RootElement.GetProperty(id.ToString());
    return !d.GetProperty("before").GetProperty("data").TryGetProperty("rawGameEntityData",out _)
        && d.GetProperty("after").GetProperty("data").GetProperty("rawGameEntityData").GetString()=="EAA=";
}),"Captured native M has exactly the twelve witnessed missing-initial empty payloads; no fixture synthesis.");
Console.WriteLine(JsonSerializer.Serialize(new {checks,nativeSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativePath))),builtSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(builtPath))),scope="Controlled reader fixtures and installed/built IL contract checks; native N observation parity remains to be tested."}));
