#include <windows.h>
#include <stdint.h>
#include <stdio.h>

#pragma pack(push,8)
struct AnimatorControllerReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,memoryBefore,memoryAfter,allocator;
    uint32_t blobSizeBefore,blobSizeAfter,hashBefore,hashAfter,firstDifference,beforeByte,afterByte;
    uint32_t differenceCount,firstDifferenceExceptFirstEvaluationFlag;
    uint32_t beforeByteExceptFirstEvaluationFlag,afterByteExceptFirstEvaluationFlag;
};

struct AnimatorControllerInputReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,controllerConstant,controllerInput,records;
    uint32_t recordCount,outerCount,byteSize,hash;
};
struct AnimatorTransitionTopologyReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,controllerMemory,controllerGraphMemory,liveLayers;
    uint32_t blobCapacity,blobLayerCount,graphLayerCount,liveLayerCount,byteSize,hash,topologyMismatchCount;
};
struct AnimatorMixerGraphReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,controllerConstant,descriptors;
    uint32_t layerCount,recordCount,byteSize,hash,mismatchCount,restoredWeightCount;
    uint32_t mismatchRecordIndex,mismatchByteOffset,expectedWord,actualWord;
};
struct AnimatorOwnerGraphReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,controllerConstant,descriptors,graph;
    uint32_t layerCount,recordCount,byteSize,hash,graphDirty58,failureRecord;
};
struct AnimatorEndTransitionReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,controllerConstant,descriptors,graph;
    uint32_t stage,targetTopologyCount,currentTopologyCount,targetOwnerCount,currentOwnerCount;
    uint32_t projectedOwnerCount,afterOwnerCount,plannedTransitionCount,completedTransitionCount;
    uint32_t targetTopologyHash,currentTopologyHash,targetOwnerHash,currentOwnerHash,projectedOwnerHash,afterOwnerHash;
    uint32_t graphDirtyBefore,graphDirtyProjected,graphDirtyAfter;
    uint32_t failureLayer,failureStateMachine,failureRecord,failureByteOffset,expectedWord,actualWord;
    uint32_t mutationStarted,plannedReboundClipCount,completedReboundClipCount;
    uint32_t plannedWeightPlanCount,completedWeightPlanCount,primedWeightWriteCount;
    uint32_t rolledBackWeightWriteCount,rollbackFailure;
    uint32_t controllerDirtyBefore,controllerDirtyAfterEnd,controllerDirtyProjected,controllerDirtyAfter;
};
struct AnimatorPlayableTimeReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,controllerConstant,descriptors,graph;
    uint32_t stage,targetOwnerCount,currentOwnerCount,projectedOwnerCount,afterOwnerCount;
    uint32_t targetOwnerHash,currentOwnerHash,projectedOwnerHash,afterOwnerHash;
    uint32_t graphDirtyBefore,graphDirtyAfter,uniqueTargetNodes,uniqueCurrentNodes;
    uint32_t plannedNodeCount,completedNodeCount,ordinaryAdvanceRecipes,seekOnlyRecipes;
    uint32_t failureRecord,failureTargetRecord,failureByteOffset,expectedWord,actualWord;
    uintptr_t failureNode;
    uint32_t mutationStarted;
};
struct AnimatorTargetNullClipReceipt {
    uint32_t apiVersion,structSize,result,lastError;
    uintptr_t unityBase,animator,controller,controllerConstant,descriptors,graph;
    uint32_t stage,targetOwnerCount,currentOwnerCount,projectedOwnerCount,stableOwnerCount,afterOwnerCount;
    uint32_t targetOwnerHash,currentOwnerHash,projectedOwnerHash,stableOwnerHash,afterOwnerHash;
    uint32_t graphDirtyBefore,graphDirtyProjected,graphDirtyAfter,plannedClipCount,completedClipCount;
    uint32_t failureLayer,failureStateMachine,failureRecord,failureByteOffset,expectedWord,actualWord;
    uint32_t mutationStarted,exactStateMachineCount,rotatedStateMachineCount;
    uint32_t controllerDirtyBefore,controllerDirtyProjected,controllerDirtyAfter;
    uint32_t plannedAlreadyNullClipCount,plannedEmptyOutputCount,plannedScalarWriteCount;
    uint32_t completedScalarWriteCount,rolledBackScalarWriteCount,scalarRollbackFailure;
};
#pragma pack(pop)

typedef uint32_t (__cdecl *ApiVersion)();
typedef int (__cdecl *Roundtrip)(uintptr_t,uintptr_t,AnimatorControllerReceipt*);
typedef int (__cdecl *CaptureInput)(uintptr_t,uintptr_t,void*,uint32_t,AnimatorControllerInputReceipt*);
typedef int (__cdecl *CaptureTopology)(uintptr_t,uintptr_t,void*,uint32_t,AnimatorTransitionTopologyReceipt*);
typedef int (__cdecl *CaptureMixerGraph)(uintptr_t,uintptr_t,void*,uint32_t,AnimatorMixerGraphReceipt*);
typedef int (__cdecl *RestoreMixerGraph)(uintptr_t,uintptr_t,const void*,uint32_t,AnimatorMixerGraphReceipt*);
typedef int (__cdecl *CaptureOwnerGraph)(uintptr_t,uintptr_t,void*,uint32_t,AnimatorOwnerGraphReceipt*);
typedef int (__cdecl *NormalizeEndTransition)(uintptr_t,uintptr_t,const void*,uint32_t,
                                              const void*,uint32_t,uint32_t,
                                              AnimatorEndTransitionReceipt*);
typedef int (__cdecl *RestorePlayableTime)(uintptr_t,uintptr_t,const void*,uint32_t,AnimatorPlayableTimeReceipt*);
typedef int (__cdecl *RestoreTargetNullClip)(uintptr_t,uintptr_t,const void*,uint32_t,AnimatorTargetNullClipReceipt*);

int main(int argc,char** argv) {
    if(argc!=2) return 2;
    HMODULE module=LoadLibraryA(argv[1]);if(!module) return 3;
    ApiVersion version=reinterpret_cast<ApiVersion>(GetProcAddress(module,"oc2_animator_checkpoint_api_version"));
    Roundtrip roundtrip=reinterpret_cast<Roundtrip>(GetProcAddress(module,"oc2_animator_controller_roundtrip"));
    CaptureInput captureInput=reinterpret_cast<CaptureInput>(GetProcAddress(module,"oc2_animator_controller_input_capture"));
    CaptureTopology captureTopology=reinterpret_cast<CaptureTopology>(GetProcAddress(module,"oc2_animator_transition_topology_capture"));
    CaptureMixerGraph captureMixerGraph=reinterpret_cast<CaptureMixerGraph>(GetProcAddress(module,"oc2_animator_mixer_graph_capture"));
    RestoreMixerGraph restoreMixerGraph=reinterpret_cast<RestoreMixerGraph>(GetProcAddress(module,"oc2_animator_mixer_graph_restore"));
    CaptureOwnerGraph captureOwnerGraph=reinterpret_cast<CaptureOwnerGraph>(GetProcAddress(module,"oc2_animator_owner_graph_capture"));
    NormalizeEndTransition normalizeEndTransition=reinterpret_cast<NormalizeEndTransition>(
        GetProcAddress(module,"oc2_animator_settled_end_transition_normalize"));
    RestorePlayableTime restorePlayableTime=reinterpret_cast<RestorePlayableTime>(
        GetProcAddress(module,"oc2_animator_playable_time_restore"));
    RestoreTargetNullClip restoreTargetNullClip=reinterpret_cast<RestoreTargetNullClip>(
        GetProcAddress(module,"oc2_animator_target_null_clip_restore"));
    if(!version||!roundtrip||!captureInput||!captureTopology||!captureMixerGraph||
       !restoreMixerGraph||!captureOwnerGraph||!normalizeEndTransition||
       !restorePlayableTime||!restoreTargetNullClip||version()!=17)return 4;
    AnimatorControllerReceipt receipt={};
    int ok=roundtrip(0,0,&receipt);
    AnimatorControllerInputReceipt inputReceipt={};
    int inputOk=captureInput(0,0,0,0,&inputReceipt);
    AnimatorTransitionTopologyReceipt topologyReceipt={};
    int topologyOk=captureTopology(0,0,0,0,&topologyReceipt);
    AnimatorMixerGraphReceipt mixerCaptureReceipt={};
    int mixerCaptureOk=captureMixerGraph(0,0,0,0,&mixerCaptureReceipt);
    AnimatorMixerGraphReceipt mixerRestoreReceipt={};
    int mixerRestoreOk=restoreMixerGraph(0,0,0,0,&mixerRestoreReceipt);
    AnimatorOwnerGraphReceipt ownerReceipt={};
    int ownerOk=captureOwnerGraph(0,0,0,0,&ownerReceipt);
    AnimatorEndTransitionReceipt endReceipt={};
    int endOk=normalizeEndTransition(0,0,0,0,0,0,0,&endReceipt);
    AnimatorPlayableTimeReceipt timeReceipt={};
    int timeOk=restorePlayableTime(0,0,0,0,&timeReceipt);
    AnimatorTargetNullClipReceipt targetNullReceipt={};
    int targetNullOk=restoreTargetNullClip(0,0,0,0,&targetNullReceipt);
    printf("api=%u size=%u result=%u ok=%d inputApi=%u inputSize=%u inputResult=%u inputOk=%d topologyApi=%u topologySize=%u topologyResult=%u topologyOk=%d mixerCaptureApi=%u mixerCaptureSize=%u mixerCaptureResult=%u mixerCaptureOk=%d mixerRestoreApi=%u mixerRestoreSize=%u mixerRestoreResult=%u mixerRestoreOk=%d ownerApi=%u ownerSize=%u ownerResult=%u ownerOk=%d endApi=%u endSize=%u endResult=%u endOk=%d timeApi=%u timeSize=%u timeResult=%u timeOk=%d targetNullApi=%u targetNullSize=%u targetNullResult=%u targetNullOk=%d\n",
        receipt.apiVersion,receipt.structSize,receipt.result,ok,inputReceipt.apiVersion,inputReceipt.structSize,
        inputReceipt.result,inputOk,topologyReceipt.apiVersion,topologyReceipt.structSize,topologyReceipt.result,topologyOk,
        mixerCaptureReceipt.apiVersion,mixerCaptureReceipt.structSize,mixerCaptureReceipt.result,mixerCaptureOk,
        mixerRestoreReceipt.apiVersion,mixerRestoreReceipt.structSize,mixerRestoreReceipt.result,mixerRestoreOk,
        ownerReceipt.apiVersion,ownerReceipt.structSize,ownerReceipt.result,ownerOk,
        endReceipt.apiVersion,endReceipt.structSize,endReceipt.result,endOk,
        timeReceipt.apiVersion,timeReceipt.structSize,timeReceipt.result,timeOk,
        targetNullReceipt.apiVersion,targetNullReceipt.structSize,targetNullReceipt.result,targetNullOk);
    FreeLibrary(module);
    return !ok&&receipt.apiVersion==17&&receipt.structSize==sizeof(receipt)&&receipt.result==2&&
        !inputOk&&inputReceipt.apiVersion==17&&inputReceipt.structSize==sizeof(inputReceipt)&&inputReceipt.result==2&&
        !topologyOk&&topologyReceipt.apiVersion==17&&topologyReceipt.structSize==sizeof(topologyReceipt)&&topologyReceipt.result==2&&
        !mixerCaptureOk&&mixerCaptureReceipt.apiVersion==17&&mixerCaptureReceipt.structSize==sizeof(mixerCaptureReceipt)&&mixerCaptureReceipt.result==2&&
        !mixerRestoreOk&&mixerRestoreReceipt.apiVersion==17&&mixerRestoreReceipt.structSize==sizeof(mixerRestoreReceipt)&&mixerRestoreReceipt.result==2&&
        !ownerOk&&ownerReceipt.apiVersion==17&&ownerReceipt.structSize==sizeof(ownerReceipt)&&ownerReceipt.result==2&&
        !endOk&&endReceipt.apiVersion==17&&endReceipt.structSize==sizeof(endReceipt)&&endReceipt.result==2&&
        endReceipt.mutationStarted==0&&endReceipt.plannedReboundClipCount==0&&
        endReceipt.completedReboundClipCount==0&&endReceipt.plannedWeightPlanCount==0&&
        endReceipt.completedWeightPlanCount==0&&endReceipt.rollbackFailure==0&&
        !timeOk&&timeReceipt.apiVersion==17&&timeReceipt.structSize==sizeof(timeReceipt)&&timeReceipt.result==2&&
        !targetNullOk&&targetNullReceipt.apiVersion==17&&targetNullReceipt.structSize==sizeof(targetNullReceipt)&&targetNullReceipt.result==2&&
        targetNullReceipt.plannedScalarWriteCount==0&&targetNullReceipt.completedScalarWriteCount==0&&
        targetNullReceipt.rolledBackScalarWriteCount==0&&targetNullReceipt.scalarRollbackFailure==0?0:5;
}
