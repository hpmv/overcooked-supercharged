using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
using System.Text.Json;

if(args.Length!=1||!File.Exists(args[0]))
    throw new ArgumentException("Pass one compiled RigidbodyActorRebuild module DLL.");

var checks=new List<string>();
void Check(bool value,string name){if(!value)throw new Exception(name);checks.Add(name);}
using var assembly=AssemblyDefinition.ReadAssembly(Path.GetFullPath(args[0]));
var type=assembly.MainModule.Types.Single(value=>value.FullName==
    "SuperchargedPatch.Authoring.Modules.RigidbodyActorRebuildModule");
MethodDefinition Method(string name)=>type.Methods.Single(value=>value.Name==name);
Instruction[] Instructions(string name)=>Method(name).Body.Instructions.ToArray();
string[] Calls(string name)=>Instructions(name).Where(value=>value.Operand is MethodReference)
    .Select(value=>((MethodReference)value.Operand).Name).ToArray();
string[] Strings(string name)=>Instructions(name).Where(value=>value.OpCode.Code==Code.Ldstr)
    .Select(value=>(string)value.Operand).ToArray();
int CallIndex(string name,string called)=>Array.FindIndex(Instructions(name),value=>
    value.Operand is MethodReference method&&method.Name==called);
int StoreIndex(string name,string field)=>Array.FindIndex(Instructions(name),value=>
    value.OpCode.Code==Code.Stfld&&value.Operand is FieldReference target&&target.Name==field);
bool StoresMinusOne(string name,string field)
{
    var body=Instructions(name);
    for(int index=1;index<body.Length;index++)
        if(body[index].OpCode.Code==Code.Stfld&&body[index].Operand is FieldReference target&&
            target.Name==field&&body[index-1].OpCode.Code==Code.Ldc_I4_M1)return true;
    return false;
}

var hook=Method("AfterCaptureFrame");
Check(hook.IsPublic&&hook.IsStatic&&hook.ReturnType.MetadataType==MetadataType.Void&&
    hook.Parameters.Count==1&&hook.Parameters[0].ParameterType.MetadataType==MetadataType.Int32,
    "CaptureFrame postfix has exact public static void(int) Harmony signature");
Check(Calls("AfterCaptureFrame").Contains("ObserveSceneGeneration")&&
    Calls("AfterCaptureFrame").Contains("ObserveCoreRoundIdentity")&&
    Calls("AfterCaptureFrame").Contains("CaptureContactPoolAtScheduledFrame"),
    "postfix observes scene and round ownership before scheduled transaction");

Check(Strings("Invoke").Contains("capture-contact-pool-at-frame"),
    "hot operation is present in compiled dispatch");
Check(Strings("ArmContactPoolCaptureAtFrame").Contains(
    "capture-contact-pool-at-frame requires exactly one whole-number frame."),
    "admission accepts the JSON host's Int64 whole-number frame representation");
Check(Calls("ArmContactPoolCaptureAtFrame").Contains("ToInt64"),
    "admission normalizes the JSON host's whole-number representation before range validation");
Check(Calls("ArmContactPoolCaptureAtFrame").Contains("IsPaused")&&
    Calls("ArmContactPoolCaptureAtFrame").Contains("CurrentCheckpointFrame"),
    "admission checks pause fence and current checkpoint watermark");

var captureCalls=Calls("CaptureContactPoolAtScheduledFrame");
Check(captureCalls.Contains("RefreshObservedContactManagerContext")&&
    captureCalls.Contains("CurrentCheckpointFrame")&&captureCalls.Contains("CoreCheckpointSnapshot"),
    "target transaction refreshes context and binds exact core snapshot");
Check(Strings("CaptureContactPoolAtScheduledFrame").Any(value=>value.Contains("skipped exact output frame")),
    "late observation fails closed instead of capturing a neighboring frame");
Check(captureCalls.Contains("RunContactPoolAction")&&captureCalls.Contains("CancelDirtyInteractionWork"),
    "target transaction uses existing capture path and has native-arm rollback");
int run=CallIndex("CaptureContactPoolAtScheduledFrame","RunContactPoolAction");
int clear=StoreIndex("CaptureContactPoolAtScheduledFrame","scheduledContactPoolCaptureFrame");
var captureBody=Instructions("CaptureContactPoolAtScheduledFrame");
Check(run>=0&&clear>run&&StoresMinusOne("CaptureContactPoolAtScheduledFrame",
    "scheduledContactPoolCaptureFrame"),
    "schedule clears only after synchronous capture path returns and validates");
Check(run>0&&captureBody[run-1].OpCode.Code==Code.Ldc_I4_1,
    "scheduled transaction forces Transform-dispatch capture even when optional automatic restore is disabled");
Check(captureBody.Count(value=>value.OpCode.Code==Code.Stfld&&value.Operand is FieldReference field&&
    field.Name=="scheduledContactPoolCaptureFrame")==1,
    "target transaction has one success-only schedule clear");
Check(captureBody.Any(value=>value.OpCode.Code==Code.Ldfld&&value.Operand is FieldReference field&&
        field.Name=="CoreSnapshot")&&captureBody.Any(value=>value.OpCode.Code==Code.Ceq||
        value.OpCode.Code==Code.Bne_Un||value.OpCode.Code==Code.Bne_Un_S||
        value.OpCode.Code==Code.Beq||value.OpCode.Code==Code.Beq_S),
    "staged sidecar retains the identical exact core snapshot object");
Check(!captureCalls.Contains("StoreCheckpointSidecar")&&
    Calls("FinalizePendingDirtyInteractionCapture").Contains("StoreCheckpointSidecar"),
    "sidecar publication remains deferred until dirty-interaction sample finalization");
Check(Calls("RunContactPoolAction").Contains("ArmDirtyInteractionCapture")&&
    Calls("RunContactPoolAction").Contains("RunTransformDispatchAction"),
    "shared capture path stages dirty-interaction and Transform-dispatch state");

Check(Strings("InstallAutomaticHook").Contains("CaptureFrame")&&
    Calls("InstallAutomaticHook").Contains("Patch"),
    "automatic hook patches NativeKitchenCheckpoint.CaptureFrame");
var installBody=Instructions("InstallAutomaticHook");
int priorityStore=Array.FindIndex(installBody,value=>value.OpCode.Code==Code.Stfld&&
    value.Operand is FieldReference field&&field.DeclaringType.FullName=="HarmonyLib.HarmonyMethod"&&
    field.Name=="priority");
Check(priorityStore>0&&installBody[priorityStore-1].OpCode.Code==Code.Ldc_I4_0,
    "CaptureFrame postfix receives explicit Harmony priority");

foreach(string method in new[]{"CancelContactPoolAction","ResetSceneOwnedCheckpointState","Deactivate"})
{
    Check(StoresMinusOne(method,"scheduledContactPoolCaptureFrame"),
        method+" clears scheduled target");
    Check(StoresMinusOne(method,"scheduledContactPoolLastObservedFrame"),
        method+" clears scheduled observation watermark");
}
var statusStrings=Strings("Status");
Check(statusStrings.Contains("scheduledContactPoolCapturePending")&&
    statusStrings.Contains("scheduledContactPoolCaptureFrame")&&
    statusStrings.Contains("scheduledContactPoolLastObservedFrame")&&
    statusStrings.Contains("scheduledContactPoolCaptureTriggers"),
    "status exposes scheduled target, observation, and trigger state");

string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])));
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,checks=checks.Count,names=checks,
    dll=Path.GetFullPath(args[0]),sha256=hash}));
