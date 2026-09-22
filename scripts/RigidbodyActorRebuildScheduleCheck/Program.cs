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
Check(captureCalls.Contains("RunContactPoolAction")&&captureCalls.Contains("CancelCheckpointObservationWork"),
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
    Calls("FinalizePendingDirtyInteractionCaptureCore").Contains("StoreCheckpointSidecar"),
    "sidecar publication remains deferred until dirty-interaction sample finalization");
Check(Calls("RunContactPoolAction").Contains("ArmCheckpointObservationCapture")&&
    Calls("RunContactPoolAction").Contains("RunTransformDispatchAction"),
    "shared capture path stages the complete observation transaction and Transform-dispatch state");

var outputCalls=Calls("AfterCaptureFrame");
Check(outputCalls.Contains("CapturePendingPostTransitionSnapshotAtOutput")&&
    outputCalls.Contains("FinalizePendingDirtyInteractionCapture")&&
    outputCalls.IndexOf("CapturePendingPostTransitionSnapshotAtOutput")<
        outputCalls.IndexOf("FinalizePendingDirtyInteractionCapture"),
    "output boundary seals the full post-transition image before publishing the entry transaction");
Check(outputCalls.Contains("CaptureFirstReplayTransitionAuditAtOutput")&&
    outputCalls.IndexOf("CaptureFirstReplayTransitionAuditAtOutput")<
        outputCalls.IndexOf("ValidatePendingContactRecreate"),
    "first-replay native transition is persisted at the output boundary before contact validation can cancel it");
var phaseCalls=Calls("CapturePhysicsPhaseSnapshot");
foreach(string family in new[]{"CurrentCheckpointFrame","CoreCheckpointSnapshot",
    "CaptureLiveContactPoolState","CaptureContactManagerOwners","CaptureSipPoolState",
    "CaptureActorPairPoolState","CaptureActorPairReportPoolState","CaptureNPhaseReportState",
    "CaptureInteractionGraphState","CaptureTransformCacheState","CaptureIslandSnapshotState",
    "CaptureManifoldPoolStateReadOnly","RunTransformDispatchAction",
    "CaptureDirtyInteractionStateReadOnly"})
    Check(phaseCalls.Contains(family),
        "post-transition image captures "+family+" at the same output boundary");
Check(Strings("Activate").Contains("oc2_dirty_interaction_order_capture_snapshot"),
    "activation requires the API18 stateless dirty-interaction snapshot export");
Check(Calls("CaptureDirtyInteractionStateReadOnly").Contains("Equals")&&
    Strings("CaptureDirtyInteractionStateReadOnly").Any(value=>
        value.Contains("changed the pending hook transaction receipt")),
    "stateless dirty capture proves the native hook receipt remained unchanged");
Check(Calls("DescribePhysicsPhaseComparison").Contains("SameIslandPhysicalSnapshotState")&&
    Calls("DescribePhysicsPhaseComparison").Contains("SameIslandSnapshotState")&&
    Strings("DescribePhysicsPhaseComparison").Contains("islandSnapshotRawEqual"),
    "post comparison separates physical island state from advancing observer provenance");
Check(Strings("ClearIslandCaptureProvenance").Contains("ObserverSequence")&&
    Strings("ClearIslandCaptureProvenance").Contains("Epoch")&&
    Strings("ClearIslandCaptureProvenance").Contains("JournalEndOrdinal"),
    "physical island comparison removes only capture and journal provenance");
Check(Calls("StoreCheckpointSidecar").Contains("ValidatePhysicsPhaseSnapshot")&&
    Calls("StoreCheckpointSidecar").Contains("CoreCheckpointSnapshot")&&
    Strings("StoreCheckpointSidecar").Any(value=>
        value.Contains("not phase-linked to its entry and transition")),
    "publication validates the entry-transition-post link and exact N+1 core identity");
var transitionCaptureCalls=Calls("CaptureFirstReplayTransitionAuditAtOutput");
Check(transitionCaptureCalls.Contains("CopyFinishBroadPhaseCapture")&&
    transitionCaptureCalls.Contains("CopyIslandTransitionAudit")&&
    transitionCaptureCalls.Contains("CapturePhysicsPhaseSnapshot")&&
    transitionCaptureCalls.Contains("CancelFirstReplayTransitionObservationWork"),
    "first-replay audit captures broadphase, island, and the complete restored post image");
var auditReceiptStrings=Strings("DescribeFirstReplayTransitionAudit");
Check(auditReceiptStrings.Contains("checkpointFrame")&&
    auditReceiptStrings.Contains("transitionFrame")&&
    auditReceiptStrings.Contains("capturedAtOutputFrame")&&
    auditReceiptStrings.Contains("readAtFrame")&&
    auditReceiptStrings.Contains("broadPhase")&&
    auditReceiptStrings.Contains("transition")&&
    auditReceiptStrings.Contains("targetPostSnapshot")&&
    auditReceiptStrings.Contains("restoredPostSnapshot")&&
    auditReceiptStrings.Contains("postSnapshotComparison"),
    "first-replay receipt separates boundaries and compares every target/restored post family");

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
Check(statusStrings.Contains("firstReplayIslandAuditCheckpointFrame")&&
    statusStrings.Contains("firstReplayIslandAuditTransitionFrame")&&
    statusStrings.Contains("firstReplayIslandAuditCapturedAtOutputFrame")&&
    statusStrings.Contains("firstReplayBroadPhaseAudit")&&
    !statusStrings.Contains("firstReplayIslandAuditFrame"),
    "status exposes distinct first-replay checkpoint, transition, capture, and broadphase evidence");
Check(Strings("RecordContactRecreateReceipt").Contains("matchedMask")&&
    Strings("RecordContactRecreateReceipt").Contains("threadId"),
    "contact recreation receipts expose the actual current manager mask and callback thread");

string hash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])));
Console.WriteLine(JsonSerializer.Serialize(new{ok=true,checks=checks.Count,names=checks,
    dll=Path.GetFullPath(args[0]),sha256=hash}));
