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
TypeDefinition Nested(string name)=>type.NestedTypes.Single(value=>value.Name==name);
Instruction[] Instructions(string name)=>Method(name).Body.Instructions.ToArray();
string[] Calls(string name)=>Instructions(name).Where(value=>value.Operand is MethodReference)
    .Select(value=>((MethodReference)value.Operand).Name).ToArray();
string[] Strings(string name)=>Instructions(name).Where(value=>value.OpCode.Code==Code.Ldstr)
    .Select(value=>(string)value.Operand).ToArray();
IEnumerable<TypeDefinition> Descendants(TypeDefinition root)
{
    yield return root;
    foreach(var nested in root.NestedTypes)
        foreach(var descendant in Descendants(nested))yield return descendant;
}
int FamilyCallCount(string method,string called)=>Descendants(type)
    .SelectMany(value=>value.Methods)
    .Where(value=>value.Name==method||value.Name.Contains("<"+method+">"))
    .Where(value=>value.HasBody)
    .SelectMany(value=>value.Body.Instructions)
    .Count(value=>value.Operand is MethodReference target&&target.Name==called);
int CallIndex(string name,string called)=>Array.FindIndex(Instructions(name),value=>
    value.Operand is MethodReference method&&method.Name==called);
int StoreIndex(string name,string field)=>Array.FindIndex(Instructions(name),value=>
    value.OpCode.Code==Code.Stfld&&value.Operand is FieldReference target&&target.Name==field);
bool StoresNull(string name,string field)
{
    var body=Instructions(name);
    for(int index=1;index<body.Length;index++)
        if(body[index].OpCode.Code==Code.Stfld&&body[index].Operand is FieldReference target&&
            target.Name==field&&body[index-1].OpCode.Code==Code.Ldnull)return true;
    return false;
}
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
    "CaptureActorPairPoolState","CaptureActorPairReportPoolState","CaptureNPhasePoolImages",
    "CaptureNPhaseReportState",
    "CaptureInteractionGraphState","CaptureTransformCacheState","CaptureIslandSnapshotState",
    "CaptureManifoldPoolStateReadOnly","RunTransformDispatchAction",
    "CaptureDirtyInteractionStateReadOnly"})
    Check(phaseCalls.Contains(family),
        "post-transition image captures "+family+" at the same output boundary");
var ownerState=type.NestedTypes.Single(value=>value.Name=="ContactManagerOwnerState");
foreach(string image in new[]{"ManagerBytes","SipBytes","ActorPairBytes","ManifoldBytes","CacheBytes"})
    Check(ownerState.Fields.Any(value=>value.Name==image&&value.FieldType.FullName=="System.Byte[][]"),
        "contact-owner checkpoint retains bounded "+image+" images");
Check(FamilyCallCount("CaptureContactManagerOwners","ReadBytes")==5,
    "contact-owner capture reads every opaque native image exactly once per row");
Check(Calls("SameContactManagerOwnerSnapshot").Count(value=>value=="SameByteMatrix")==5,
    "contact-owner equality includes every opaque native image");
var graphState=type.NestedTypes.Single(value=>value.Name=="InteractionGraphState");
Check(graphState.Fields.Any(value=>value.Name=="PrimaryBytes"&&
        value.FieldType.FullName=="System.Byte[][]"),
    "interaction-graph checkpoint retains bounded primary-object images");
Check(Calls("CaptureInteractionGraphState").Contains("CaptureInteractionPrimaryBytes")&&
    Calls("CaptureInteractionPrimaryBytes").Contains("ReadBytes"),
    "interaction-graph capture reads rigid primary objects at the checkpoint boundary");
Check(Calls("SameInteractionGraphState").Contains("SameByteMatrix"),
    "interaction-graph equality includes rigid primary-object history");
var nativeAbi=type.Fields.Single(value=>value.Name=="NativeAbiVersion");
Check(nativeAbi.HasConstant&&Convert.ToUInt32(nativeAbi.Constant)==21u,
    "managed activation is pinned to native ABI version 21");
var poolBuffers=Nested("NativeNPhasePoolSnapshotBuffers");
Check(poolBuffers.IsSequentialLayout&&poolBuffers.PackingSize==8&&
    poolBuffers.Fields.Select(value=>value.Name).SequenceEqual(new[]{
        "SlabBases","SlabBaseCapacity","FreeSlots","FreeSlotCapacity",
        "AllocationWords","AllocationWordCapacity","SlabBytes","SlabByteCapacity"})&&
    poolBuffers.Fields.Where((value,index)=>(index&1)==0).All(value=>
        value.FieldType.MetadataType==MetadataType.IntPtr)&&
    poolBuffers.Fields.Where((value,index)=>(index&1)!=0).All(value=>
        value.FieldType.MetadataType==MetadataType.UInt32),
    "complete NPhase-pool output buffers preserve the API21 32-byte ABI");
var poolReceipt=Nested("NativeNPhasePoolSnapshotReceipt");
var poolReceiptFields=new[]{"ApiVersion","StructSize","Result","LastError",
    "UnityBase","NPhaseCore","Pool","SlabsData","FreeHead","PoolKind","PoolOffset",
    "ElementSize","ElementsPerSlab","SlabSize","SlabCount","SlabCapacityRaw",
    "TotalSlots","Used","Unreleased","FreeHeadSlot","SlabBasesRequired",
    "SlabBasesWritten","FreeSlotsRequired","FreeSlotsWritten","AllocationWordsRequired",
    "AllocationWordsWritten","SlabBytesRequired","SlabBytesWritten","MetadataHash",
    "SlabBaseHash","FreeSlotOrderHash","AllocationBitmapHash","SlabByteHash",
    "SnapshotHash","ValidationFlags","InvalidKind","InvalidIndex","Detail"};
Check(poolReceipt.IsSequentialLayout&&poolReceipt.PackingSize==8&&
    poolReceipt.Fields.Select(value=>value.Name).SequenceEqual(poolReceiptFields)&&
    poolReceipt.Fields.Where(value=>new[]{"UnityBase","NPhaseCore","Pool","SlabsData","FreeHead"}
        .Contains(value.Name)).All(value=>value.FieldType.MetadataType==MetadataType.UIntPtr)&&
    poolReceipt.Fields.Where(value=>!new[]{"UnityBase","NPhaseCore","Pool","SlabsData","FreeHead"}
        .Contains(value.Name)).All(value=>value.FieldType.MetadataType==MetadataType.UInt32),
    "complete NPhase-pool receipt preserves the API21 152-byte field contract");
var poolCaptureInvoke=Nested("NativeNPhasePoolCaptureSnapshot").Methods.Single(value=>
    value.Name=="Invoke");
Check(poolCaptureInvoke.ReturnType.MetadataType==MetadataType.Int32&&
    poolCaptureInvoke.Parameters.Select(value=>value.ParameterType.MetadataType).SequenceEqual(
        new[]{MetadataType.UIntPtr,MetadataType.UIntPtr,MetadataType.UInt32,
            MetadataType.IntPtr,MetadataType.IntPtr}),
    "complete NPhase-pool capture delegate matches the five-argument native export");
var poolImage=Nested("NPhasePoolImageState");
Check(poolImage.Fields.Select(value=>value.Name).SequenceEqual(
        new[]{"Receipt","SlabBases","FreeSlots","AllocationWords","SlabBytes"})&&
    poolImage.Fields.Single(value=>value.Name=="Receipt").FieldType.Name==
        "NativeNPhasePoolSnapshotReceipt"&&
    new[]{"SlabBases","FreeSlots","AllocationWords"}.All(name=>
        poolImage.Fields.Single(value=>value.Name==name).FieldType.FullName=="System.UInt32[]")&&
    poolImage.Fields.Single(value=>value.Name=="SlabBytes").FieldType.FullName=="System.Byte[]",
    "complete NPhase-pool state owns metadata, topology, partition, and full slab bytes");
foreach(string owner in new[]{"PhysicsPhaseSnapshot","CheckpointSidecar"})
    Check(Nested(owner).Fields.Any(value=>value.Name=="NPhasePoolImages"&&
        value.FieldType.FullName.EndsWith("/NPhasePoolImageState[]")),
        owner+" retains all complete NPhase-pool images");
var reportBuffers=Nested("NativeNPhaseReportSnapshotBuffersV1");
Check(reportBuffers.IsSequentialLayout&&reportBuffers.PackingSize==8&&
    reportBuffers.Fields.Select(value=>value.Name).SequenceEqual(new[]{
        "ActorPairBacking","ActorPairCapacity","PersistentBacking","PersistentCapacity",
        "ForceThresholdBacking","ForceThresholdCapacity","ReportBufferBytes",
        "ReportBufferByteCapacity"})&&
    reportBuffers.Fields.Where((value,index)=>(index&1)==0).All(value=>
        value.FieldType.MetadataType==MetadataType.IntPtr)&&
    reportBuffers.Fields.Where((value,index)=>(index&1)!=0).All(value=>
        value.FieldType.MetadataType==MetadataType.UInt32),
    "complete report-history output buffers preserve the API21 32-byte ABI");
var reportArrayReceipt=Nested("NativeNPhaseReportArrayReceiptV1");
Check(reportArrayReceipt.IsSequentialLayout&&reportArrayReceipt.PackingSize==8&&
    reportArrayReceipt.Fields.Select(value=>value.Name).SequenceEqual(new[]{
        "Data","Count","CapacityRaw","BackingRequired","BackingWritten",
        "LogicalOrderHash","BackingHash"})&&
    reportArrayReceipt.Fields.Single(value=>value.Name=="Data").FieldType.MetadataType==
        MetadataType.UIntPtr&&reportArrayReceipt.Fields.Where(value=>value.Name!="Data")
        .All(value=>value.FieldType.MetadataType==MetadataType.UInt32),
    "complete report-history array receipt preserves the API21 28-byte field contract");
var reportReceipt=Nested("NativeNPhaseReportSnapshotReceiptV1");
var reportReceiptFields=new[]{"ApiVersion","StructSize","Result","LastError",
    "UnityBase","NPhaseCore","OwnerScene","SceneTimeStamp",
    "SceneReportShapePairTimeStamp","ActorPairs","Persistent",
    "NextFramePersistentIndex","ForceThreshold","ReportBuffer",
    "ReportBufferCurrentIndex","ReportBufferCurrentSize","ReportBufferDefaultSize",
    "ReportBufferLastIndex","ReportBufferAllocationLocked","ReportBufferRequired",
    "ReportBufferWritten","ReportBufferActiveHash","ReportBufferAllocationHash",
    "MetadataHash","SnapshotHash","ValidationFlags","InvalidKind","InvalidIndex","Detail"};
Check(reportReceipt.IsSequentialLayout&&reportReceipt.PackingSize==8&&
    reportReceipt.Fields.Select(value=>value.Name).SequenceEqual(reportReceiptFields)&&
    new[]{"UnityBase","NPhaseCore","OwnerScene","ReportBuffer"}.All(name=>
        reportReceipt.Fields.Single(value=>value.Name==name).FieldType.MetadataType==
            MetadataType.UIntPtr)&&
    new[]{"ActorPairs","Persistent","ForceThreshold"}.All(name=>
        reportReceipt.Fields.Single(value=>value.Name==name).FieldType.Name==
            "NativeNPhaseReportArrayReceiptV1"),
    "complete report-history receipt preserves the API21 188-byte field contract");
var reportCaptureInvoke=Nested("NativeNPhaseReportStateCaptureSnapshotV1").Methods.Single(
    value=>value.Name=="Invoke");
Check(reportCaptureInvoke.ReturnType.MetadataType==MetadataType.Int32&&
    reportCaptureInvoke.Parameters.Select(value=>value.ParameterType.MetadataType)
        .SequenceEqual(new[]{MetadataType.UIntPtr,MetadataType.UIntPtr,
            MetadataType.IntPtr,MetadataType.IntPtr}),
    "complete report-history capture delegate matches the four-argument V1 export");
var reportState=Nested("NPhaseReportState");
foreach(string logical in new[]{"ActorPairs","PersistentSips","ForceThresholdSips"})
    Check(reportState.Fields.Any(value=>value.Name==logical&&
        value.FieldType.FullName=="System.UInt32[]"),
        "complete report history retains legacy logical list "+logical);
foreach(string backing in new[]{"ActorPairBacking","PersistentBacking","ForceThresholdBacking"})
    Check(reportState.Fields.Any(value=>value.Name==backing&&
        value.FieldType.FullName=="System.UInt32[]"),
        "complete report history retains full-capacity backing "+backing);
Check(reportState.Fields.Any(value=>value.Name=="ReportBufferBytes"&&
        value.FieldType.FullName=="System.Byte[]"),
    "complete report history retains the full report-buffer allocation");
Check(Strings("Activate").Contains("oc2_dirty_interaction_order_capture_snapshot")&&
    Strings("Activate").Contains("oc2_island_restore_snapshot_v1")&&
    Strings("Activate").Contains("oc2_nphase_pool_capture_snapshot_v1")&&
    Strings("Activate").Contains("oc2_nphase_report_state_capture_snapshot_v1")&&
    !Strings("Activate").Contains("oc2_nphase_report_state_capture_snapshot"),
    "activation requires the API21 complete-pool/report, stateless-capture, and island-restore exports");
Check(Calls("CaptureNPhasePoolImages").Contains("CaptureNPhasePoolImage")&&
    Calls("CaptureNPhasePoolImages").Contains("ValidateNPhasePoolImages")&&
    Calls("CaptureNPhasePoolImage").Contains("ReadPointerBuffer")&&
    Calls("CaptureNPhasePoolImage").Count(value=>value=="ReadUInt32Buffer")==2&&
    Calls("CaptureNPhasePoolImage").Contains("ValidateNPhasePoolImage"),
    "complete NPhase-pool capture copies and validates every native output family");
Check(Calls("RunContactPoolAction").Contains("CaptureNPhasePoolImages")&&
    Calls("RunContactPoolAction").Contains("ValidateCheckpointPoolCoherence")&&
    Calls("ValidatePhysicsPhaseSnapshot").Contains("ValidateNPhasePoolImages")&&
    Calls("ValidatePhysicsPhaseSnapshot").Contains("ValidateCheckpointPoolCoherence"),
    "entry and post-transition captures validate complete pool images in cross-family context");
Check(Calls("ValidateCheckpointPoolCoherence").Contains("ValidateNPhasePoolImages")&&
    Calls("ValidateCheckpointPoolCoherence").Contains("NPhasePoolImagesCoherentWithLegacy")&&
    Calls("NPhasePoolImagesCoherentWithLegacy").Contains("NPhasePoolFreeAddresses")&&
    Calls("NPhasePoolImagesCoherentWithLegacy").Contains("NPhasePoolAllocatedAddresses"),
    "checkpoint coherence projects complete-pool free and allocated partitions into legacy families");
Check(Calls("StoreCheckpointSidecar").Contains("ValidateNPhasePoolImages")&&
    Calls("StoreCheckpointSidecar").Contains("SameRawNPhasePoolImages")&&
    Calls("StoreCheckpointSidecar").Contains("ValidateCheckpointPoolCoherence")&&
    Calls("SamePhysicsPhaseSnapshot").Contains("SameRawNPhasePoolImages"),
    "publication and phase equality include complete NPhase-pool identity");
Check(Calls("DescribePhysicsPhaseSnapshot").Contains("DescribeNPhasePoolImages")&&
    Strings("DescribePhysicsPhaseSnapshot").Contains("nphasePoolImages")&&
    Calls("DescribePhysicsPhaseComparison").Contains("SameRawNPhasePoolImages")&&
    Strings("DescribePhysicsPhaseComparison").Contains("nphasePoolImagesRawEqual")&&
    Strings("DescribePhysicsPhaseComparison").Contains("allRawFamiliesEqual")&&
    !Strings("DescribePhysicsPhaseComparison").Contains("allFamiliesEqual"),
    "phase diagnostics explicitly distinguish raw complete NPhase-pool equality");
Check(Calls("CaptureNPhaseReportState").Count(value=>value=="ReadPointerBuffer")==3&&
    Calls("CaptureNPhaseReportState").Count(value=>value=="NPhaseReportLogicalPrefix")==3&&
    Calls("CaptureNPhaseReportState").Contains("ValidateNPhaseReportState"),
    "complete report capture retains every full backing and derives each legacy logical prefix");
Check(Calls("RunContactPoolAction").Contains("CaptureNPhaseReportState")&&
    Calls("CapturePhysicsPhaseSnapshot").Contains("CaptureNPhaseReportState")&&
    Calls("ValidateCheckpointPoolCoherence").Contains("ValidateNPhaseReportState")&&
    Instructions("RunContactPoolAction").Any(value=>value.Operand is FieldReference field&&
        field.Name=="captureNPhaseReportStateSnapshotV1"),
    "entry and exact post-output boundaries capture and cross-validate complete report history");
Check(Calls("ValidateNPhaseReportState").Count(value=>
        value=="ValidateNPhaseReportArray")==3&&
    Calls("ValidateNPhaseReportState").Contains("NPhaseReportMetadataHash")&&
    Calls("ValidateNPhaseReportState").Contains("ValidNPhaseReportLastIndex")&&
    Calls("ValidateNPhaseReportState").Contains("AppendUInt32ArrayByteHash")&&
    Calls("ValidateNPhaseReportState").Contains("AppendByteArrayHash")&&
    Calls("ValidateNPhaseReportArray").Contains("NPhaseReportBackingHash")&&
    Calls("ValidateNPhaseReportArray").Contains("ContactPoolOrderHash"),
    "managed report validation proves complete backings, logical prefixes, and native hashes");
Check(Calls("AppendNPhaseReportReadiness").Contains("SameRawNPhaseReportState")&&
    Strings("AppendNPhaseReportReadiness").Contains("NPHASE_SCENE_TIMESTAMPS_CAPTURED"),
    "report readiness treats complete capture equality as raw and admits both Scene timestamps");
Check(Calls("StoreCheckpointSidecar").Contains("SameRawNPhaseReportState")&&
    Calls("SamePhysicsPhaseSnapshot").Contains("SameRawNPhaseReportState")&&
    Calls("DescribePhysicsPhaseComparison").Contains("SameRawNPhaseReportState")&&
    Strings("DescribePhysicsPhaseComparison").Contains("nphaseReportsRawEqual")&&
    !Strings("DescribePhysicsPhaseComparison").Contains("nphaseReportsEqual"),
    "publication and diagnostics label address-sensitive report history as raw equality");
var reportDescriptionStrings=Strings("DescribeNPhaseReportState");
foreach(string field in new[]{"sceneTimeStamp","sceneReportShapePairTimeStamp",
    "sceneTimestampsEqual","actorPairBackingHash","actorPairBackingSha256",
    "persistentBackingHash","persistentBackingSha256","forceThresholdBackingHash",
    "forceThresholdBackingSha256","reportBufferAllocationSha256","metadataHash",
    "snapshotHash"})
    Check(reportDescriptionStrings.Contains(field),
        "complete report diagnostics expose "+field);
Check(StoresNull("Deactivate","captureNPhasePoolSnapshot"),
    "deactivation clears the API21 complete NPhase-pool capture delegate");
Check(StoresNull("Deactivate","captureNPhaseReportStateSnapshotV1"),
    "deactivation clears the API21 complete report-history capture delegate");
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
