#include <windows.h>
#include <stddef.h>
#include <stdint.h>

#if !defined(_M_IX86)
#error This helper requires the Unity 2017 Win32/x86 ABI.
#endif

namespace {

static const uint32_t kApiVersion = 17;
static const uint32_t kCopyControllerMemoryRva = 0x62D350;
static const uint32_t kNormalizeControllerMemoryRva = 0x649040;
static const uint32_t kBumpAllocatorVtableRva = 0xE82790;
static const uint32_t kBumpAllocatorAllocateRva = 0x61F020;
static const uint32_t kBumpAllocatorFreeRva = 0x0E2E20;
static const uint32_t kMaximumBlobSize = 1024u * 1024u;
static const uint32_t kControllerInputPrefixSize = 12u;
static const uint32_t kControllerInputRecordSize = 24u;
static const uint32_t kMaximumControllerInputRecords =
    (kMaximumBlobSize-kControllerInputPrefixSize)/kControllerInputRecordSize;
static const uint32_t kMaximumTopologyLayers = 64u;
static const uint32_t kMixerPlayableVtableRva = 0xE839C8;
static const uint32_t kAnimationMixerPlayableVtableRva = 0xE83744;
static const uint32_t kAnimationClipPlayableVtableRva = 0xE83610;
static const uint32_t kAnimationPosePlayableVtableRva = 0xE83934;
static const uint32_t kAnimationLayerMixerPlayableVtableRva = 0xE836A4;
static const uint32_t kAnimatorControllerPlayableVtableRva = 0xE83A60;
static const uint32_t kAnimationMixerPlayableDeletingDestructorRva = 0x641260;
// Relocation-free tail of ConstructPlayable<AnimationMixerPlayable> covering
// the exact 0xA0 allocation-size argument and its allocation call.
static const uint32_t kAnimationMixerPlayableAllocationSizeSpanRva = 0x64A6F7;
static const uint32_t kSetInputWeightRva = 0x2F5000;
static const uint32_t kPlayableSetTimeRva = 0x2F54C0;
static const uint32_t kPlayableAdvanceTimeRva = 0x2F37A0;
static const uint32_t kAnimationClipSetTimeRva = 0x645B70;
static const uint32_t kAnimationClipAdvanceTimeRva = 0x643200;
static const uint32_t kAnimationClipSetInternalWeightRva = 0x6458A0;
static const uint32_t kEndTransitionRva = 0x646CD0;
static const uint32_t kRootByTypeRva = 0x6456E0;
static const uint32_t kSetClipRva = 0x645760;
static const uint32_t kOverrideClipPlayablesRva = 0x648AB0;
static const uint32_t kConnectNoTopologyChangeRva = 0x641B90;
static const uint32_t kDisconnectNoTopologyChangeRva = 0x642050;
static const uint32_t kLowerConnectRva = 0x2F1540;
static const uint32_t kSetInputConnectionWrapperRva = 0x6457E0;
static const uint32_t kClipPoseSetInputConnectionWrapperRva = 0x645810;
static const uint32_t kSetOutputConnectionRva = 0x2F50A0;
static const uint32_t kClearInputRva = 0x2F1270;
static const uint32_t kClearOutputRva = 0x2F1320;
static const uint32_t kMaximumStateMachinesPerLayer = 256u;
static const uint32_t kMaximumMixerInputs = 256u;
static const uint32_t kMaximumMixerGraphRecords = 16384u;
static const uint32_t kMaximumOwnerGraphRecords = 65536u;
static const uint32_t kMaximumResolverDepth = 64u;
static const uint32_t kMaximumClipCacheBytes = 16u * 1024u * 1024u;
static const uint32_t kFirstEvaluationFlagOffset = 0x18;
static const uint8_t kCopyControllerMemoryBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x3C};
static const uint8_t kNormalizeControllerMemoryBytes[] = {0x55,0x8B,0xEC,0x53,0x57,0x8B,0xF9};
static const uint8_t kBumpAllocatorAllocateBytes[] = {0x55,0x8B,0xEC,0x8B,0x45,0x0C,0x56,0x57};
static const uint8_t kSetInputWeightBytes[] = {
    0x55,0x8B,0xEC,0x8B,0x51,0x10,0x8B,0x45,0x08,0x3B,0x42,0x18,0x73,0x18,
    0xF3,0x0F,0x10,0x4D,0x0C,0x0F,0x57,0xC0,0x0F,0x2F,0xC1,0x77,0x0B,
    0x8D,0x0C,0x40,0x8B,0x42,0x10,0xF3,0x0F,0x11,0x0C,0x88,0x5D,0xC2,0x08,0x00};
static const uint8_t kPlayableSetTimeBytes[] = {
    0x55,0x8B,0xEC,0x57,0x8B,0xF9,0x8B,0x47,0x7C,0xA8,0x02,0x75,0x0A,
    0xF2,0x0F,0x10,0x47,0x28,0xF2,0x0F,0x11,0x47,0x30};
static const uint8_t kPlayableAdvanceTimeBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x18,0x56,0x8B,0xF1,0x57,0x81,0x66,0x7C,
    0xFF,0xFC,0xFF,0xFF,0x8B,0x7E,0x7C};
static const uint8_t kAnimationClipSetTimeBytes[] = {
    0x55,0x8B,0xEC,0xF2,0x0F,0x10,0x41,0x28,0x83,0xEC,0x08,0x66,0x0F,
    0x5A,0xC0,0xF3,0x0F,0x11,0x81,0xC0,0x00,0x00,0x00};
static const uint8_t kAnimationClipAdvanceTimeBytes[] = {
    0x55,0x8B,0xEC,0x8B,0x41,0x7C,0xD1,0xE8,0xA8,0x01,0x75,0x11,0xF2,
    0x0F,0x10,0x41,0x28,0x66,0x0F,0x5A,0xC0};
static const uint8_t kAnimationClipSetInternalWeightBytes[] = {
    0x55,0x8B,0xEC,0xF3,0x0F,0x10,0x45,0x08,0xF3,0x0F,0x11,0x81,
    0xC4,0x00,0x00,0x00,0x5D,0xC2,0x04,0x00};

typedef void* (__cdecl *CopyControllerMemory)(const void*,void*,uint32_t*);
typedef void (__thiscall *NormalizeControllerMemory)(void*,void*);
typedef void (__thiscall *AllocatorFree)(void*,void*);
typedef void (__thiscall *SetInputWeight)(void*,uint32_t,float);
typedef void (__thiscall *PlayableTimeCall)(void*,double);
typedef void (__thiscall *PlayableInternalWeightCall)(void*,float);
typedef void (__thiscall *EndTransition)(void*);
typedef void (__thiscall *OverrideClipPlayables)(void*);
typedef void (__thiscall *SetClip)(void*,void*);
typedef void* (__cdecl *RootByType)(void*,uint32_t);

struct BumpAllocator {
    void* vtable;
    void* current;
    void* base;
    uint32_t capacity;
};

#pragma pack(push,8)
struct AnimatorControllerReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t memoryBefore;
    uintptr_t memoryAfter;
    uintptr_t allocator;
    uint32_t blobSizeBefore;
    uint32_t blobSizeAfter;
    uint32_t hashBefore;
    uint32_t hashAfter;
    uint32_t firstDifference;
    uint32_t beforeByte;
    uint32_t afterByte;
    uint32_t differenceCount;
    uint32_t firstDifferenceExceptFirstEvaluationFlag;
    uint32_t beforeByteExceptFirstEvaluationFlag;
    uint32_t afterByteExceptFirstEvaluationFlag;
};

struct AnimatorControllerInputReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerConstant;
    uintptr_t controllerInput;
    uintptr_t records;
    uint32_t recordCount;
    uint32_t outerCount;
    uint32_t byteSize;
    uint32_t hash;
};

struct AnimatorTransitionTopologyReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerMemory;
    uintptr_t controllerGraphMemory;
    uintptr_t liveLayers;
    uint32_t blobCapacity;
    uint32_t blobLayerCount;
    uint32_t graphLayerCount;
    uint32_t liveLayerCount;
    uint32_t byteSize;
    uint32_t hash;
    uint32_t topologyMismatchCount;
};

struct AnimatorTransitionTopologyLayer {
    uint32_t index;
    uintptr_t live;
    uintptr_t node;
    uint32_t blobInterruptedRaw;
    uint32_t nodeStartArgumentRaw;
    uint32_t liveModeRaw;
    uint32_t liveSecondaryArgumentRaw;
};

struct AnimatorMixerGraphReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerConstant;
    uintptr_t descriptors;
    uint32_t layerCount;
    uint32_t recordCount;
    uint32_t byteSize;
    uint32_t hash;
    uint32_t mismatchCount;
    uint32_t restoredWeightCount;
    uint32_t mismatchRecordIndex;
    uint32_t mismatchByteOffset;
    uint32_t expectedWord;
    uint32_t actualWord;
};

struct AnimatorMixerWeightRecord {
    uint32_t recordKind;
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t trueBranch;
    uint32_t inputIndex;
    uintptr_t outer;
    uintptr_t outerInternal;
    uintptr_t outerEntries;
    uint32_t outerInputCount;
    uint32_t outerEntriesHash;
    uint32_t outerModeA0;
    uint32_t outerArgumentA4;
    uint32_t outerWeightBits;
    uintptr_t outerChild;
    uint32_t outerWord8;
    uintptr_t mixer;
    uintptr_t mixerInternal;
    uintptr_t mixerEntries;
    uint32_t mixerInputCount;
    uint32_t mixerFlagA5;
    uint32_t mixerWeightBits;
    uintptr_t mixerChild;
    uint32_t mixerWord8;
};

struct AnimatorOwnerGraphReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerConstant;
    uintptr_t descriptors;
    uintptr_t graph;
    uint32_t layerCount;
    uint32_t recordCount;
    uint32_t byteSize;
    uint32_t hash;
    uint32_t graphDirty58;
    uint32_t failureRecord;
};

// A flat, pointer-preserving observation of the native Playable ownership
// graph. Node rows are followed by complete input and output entry rows. The
// resolver fields separately retain the exact path SetClip(null) follows when
// it decides which upstream playable receives dirty byte +0x93.
struct AnimatorOwnerGraphRecord {
    uint32_t recordKind;
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t branchIndex;
    uint32_t inputIndex;
    uintptr_t self;
    uintptr_t vtable;
    uintptr_t internal;
    uintptr_t graph;
    uintptr_t inputEntries;
    uint32_t inputCount;
    uint32_t inputCapacityRaw;
    uintptr_t outputEntries;
    uint32_t outputCount;
    uint32_t outputCapacityRaw;
    uint32_t flags7C;
    uint32_t dirty9093;
    uint32_t rawA0A3;
    uint32_t rawA4A7;
    uint32_t word50;
    uintptr_t clip108;
    uintptr_t entryAddress;
    uint32_t entryWeightBits;
    uintptr_t entryPlayable;
    uint32_t entryPortRaw;
    uintptr_t resolverOrigin;
    uintptr_t resolverResult;
    uint32_t resolverDepth;
    uint32_t resolverStatus;
    uint32_t currentTime28Low;
    uint32_t currentTime28High;
    uint32_t previousTime30Low;
    uint32_t previousTime30High;
    uint32_t duration38Low;
    uint32_t duration38High;
    uint32_t mode80;
    uint32_t raw9497;
    uintptr_t bindingA8;
    uintptr_t bindingAC;
    uintptr_t bindingB0;
    uintptr_t bindingB4;
    uint32_t clipCacheBC;
    uint32_t clipCacheC0;
    uint32_t clipInternalWeightBits;
    uint32_t clipFlags10C;
    uint32_t bindingCachePresence;
    uintptr_t bindingACInner;
    uint32_t bindingACCount;
    uint32_t bindingACHash;
    uintptr_t bindingB0Inner;
    uint32_t bindingB0Count;
    uint32_t bindingB0Hash;
    uint32_t bindingB4Hash;
};

struct AnimatorEndTransitionReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerConstant;
    uintptr_t descriptors;
    uintptr_t graph;
    uint32_t stage;
    uint32_t targetTopologyCount;
    uint32_t currentTopologyCount;
    uint32_t targetOwnerCount;
    uint32_t currentOwnerCount;
    uint32_t projectedOwnerCount;
    uint32_t afterOwnerCount;
    uint32_t plannedTransitionCount;
    uint32_t completedTransitionCount;
    uint32_t targetTopologyHash;
    uint32_t currentTopologyHash;
    uint32_t targetOwnerHash;
    uint32_t currentOwnerHash;
    uint32_t projectedOwnerHash;
    uint32_t afterOwnerHash;
    uint32_t graphDirtyBefore;
    uint32_t graphDirtyProjected;
    uint32_t graphDirtyAfter;
    uint32_t failureLayer;
    uint32_t failureStateMachine;
    uint32_t failureRecord;
    uint32_t failureByteOffset;
    uint32_t expectedWord;
    uint32_t actualWord;
    uint32_t mutationStarted;
    uint32_t plannedReboundClipCount;
    uint32_t completedReboundClipCount;
    uint32_t plannedWeightPlanCount;
    uint32_t completedWeightPlanCount;
    uint32_t primedWeightWriteCount;
    uint32_t rolledBackWeightWriteCount;
    uint32_t rollbackFailure;
    uint32_t controllerDirtyBefore;
    uint32_t controllerDirtyAfterEnd;
    uint32_t controllerDirtyProjected;
    uint32_t controllerDirtyAfter;
};

// Receipt for a deliberately sacrificial diagnostic.  The called Unity
// routine is the engine-owned clip-binding pass used by
// Animator::UpdateOverrideControllerBindings; it does not advance time, but it
// does mutate the Playable graph and therefore must never be treated as an
// observational probe.
struct AnimatorOverrideClipReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerMemory;
    uintptr_t graph;
    uint32_t stage;
    uint32_t memorySizeBefore;
    uint32_t memorySizeAfter;
    uint32_t memoryHashBefore;
    uint32_t memoryHashAfter;
    uint32_t ownerCountBefore;
    uint32_t ownerCountAfter;
    uint32_t ownerHashBefore;
    uint32_t ownerHashAfter;
    uint32_t graphDirtyBefore;
    uint32_t graphDirtyAfter;
    uint32_t controllerDirty9093Before;
    uint32_t controllerDirty9093After;
};

// Exact receipt for the narrow native Playable clock reconstruction used
// before transition normalization.  It deliberately restores each physical
// node through that node's virtual SetTime/OnAdvanceTime implementation; it
// does not evaluate the graph or dispatch Animator callbacks.
struct AnimatorPlayableTimeReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerConstant;
    uintptr_t descriptors;
    uintptr_t graph;
    uint32_t stage;
    uint32_t targetOwnerCount;
    uint32_t currentOwnerCount;
    uint32_t projectedOwnerCount;
    uint32_t afterOwnerCount;
    uint32_t targetOwnerHash;
    uint32_t currentOwnerHash;
    uint32_t projectedOwnerHash;
    uint32_t afterOwnerHash;
    uint32_t graphDirtyBefore;
    uint32_t graphDirtyAfter;
    uint32_t uniqueTargetNodes;
    uint32_t uniqueCurrentNodes;
    uint32_t plannedNodeCount;
    uint32_t completedNodeCount;
    uint32_t ordinaryAdvanceRecipes;
    uint32_t seekOnlyRecipes;
    uint32_t failureRecord;
    uint32_t failureTargetRecord;
    uint32_t failureByteOffset;
    uint32_t expectedWord;
    uint32_t actualWord;
    uintptr_t failureNode;
    uint32_t mutationStarted;
};

// Receipt for the narrow Stage-A cleanup that follows OverrideClipPlayables.
// This operation is deliberately separate from the override pass: it only
// clears a live clip when the retained target maps the same physical branch,
// that branch is exactly weight zero, and the target contains Unity's coherent
// post-maintenance null-clip state. A finite retained descendant weight is
// first restored to the target's zero through Unity's verified setter while
// the zero-weight ancestor proves that the descendant cannot contribute.
struct AnimatorTargetNullClipReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t animator;
    uintptr_t controller;
    uintptr_t controllerConstant;
    uintptr_t descriptors;
    uintptr_t graph;
    uint32_t stage;
    uint32_t targetOwnerCount;
    uint32_t currentOwnerCount;
    uint32_t projectedOwnerCount;
    uint32_t stableOwnerCount;
    uint32_t afterOwnerCount;
    uint32_t targetOwnerHash;
    uint32_t currentOwnerHash;
    uint32_t projectedOwnerHash;
    uint32_t stableOwnerHash;
    uint32_t afterOwnerHash;
    uint32_t graphDirtyBefore;
    uint32_t graphDirtyProjected;
    uint32_t graphDirtyAfter;
    uint32_t plannedClipCount;
    uint32_t completedClipCount;
    uint32_t failureLayer;
    uint32_t failureStateMachine;
    uint32_t failureRecord;
    uint32_t failureByteOffset;
    uint32_t expectedWord;
    uint32_t actualWord;
    uint32_t mutationStarted;
    uint32_t exactStateMachineCount;
    uint32_t rotatedStateMachineCount;
    uint32_t controllerDirtyBefore;
    uint32_t controllerDirtyProjected;
    uint32_t controllerDirtyAfter;
    uint32_t plannedAlreadyNullClipCount;
    uint32_t plannedEmptyOutputCount;
    uint32_t plannedScalarWriteCount;
    uint32_t completedScalarWriteCount;
    uint32_t rolledBackScalarWriteCount;
    uint32_t scalarRollbackFailure;
};
#pragma pack(pop)

static_assert(sizeof(AnimatorControllerReceipt)==84,"Unexpected x86 controller receipt layout.");
static_assert(sizeof(AnimatorMixerGraphReceipt)==76,"Unexpected x86 mixer receipt layout.");
static_assert(sizeof(AnimatorMixerWeightRecord)==92,"Unexpected x86 mixer record layout.");
static_assert(sizeof(AnimatorOwnerGraphReceipt)==64,"Unexpected x86 owner receipt layout.");
static_assert(sizeof(AnimatorOwnerGraphRecord)==212,"Unexpected x86 owner record layout.");
static_assert(sizeof(AnimatorEndTransitionReceipt)==184,"Unexpected x86 EndTransition receipt layout.");
static_assert(sizeof(AnimatorOverrideClipReceipt)==88,"Unexpected x86 OverrideClip receipt layout.");
static_assert(sizeof(AnimatorPlayableTimeReceipt)==136,"Unexpected x86 Playable-time receipt layout.");
static_assert(sizeof(AnimatorTargetNullClipReceipt)==176,"Unexpected x86 target-null clip receipt layout.");

enum Result : uint32_t {
    ResultOk = 1,
    ResultBadArgument = 2,
    ResultUnreadable = 3,
    ResultRevisionMismatch = 4,
    ResultInvalidBlob = 5,
    ResultCapacity = 6,
    ResultControllerChanged = 7,
    ResultBlobMismatch = 8,
    ResultFault = 9,
    ResultTopologyMismatch = 10,
    ResultWriteMismatch = 11
};

enum EndTransitionStage : uint32_t {
    EndTransitionStageNone=0,
    EndTransitionStageArguments=1,
    EndTransitionStageRevision=2,
    EndTransitionStageResolve=3,
    EndTransitionStageLifecycleCapture=4,
    EndTransitionStageLifecyclePreflight=5,
    EndTransitionStageOwnerCapture=6,
    EndTransitionStageRotationPlan=7,
    EndTransitionStageProjectedCapture=8,
    EndTransitionStageProjectedCompare=9,
    EndTransitionStageWeightPrime=10,
    EndTransitionStageMutation=11,
    EndTransitionStageRebind=12,
    EndTransitionStageWeights=13,
    EndTransitionStageAfterCapture=14,
    EndTransitionStageAfterCompare=15,
    EndTransitionStageComplete=16
};

enum OverrideClipStage : uint32_t {
    OverrideClipStageNone=0,
    OverrideClipStageArguments=1,
    OverrideClipStageRevision=2,
    OverrideClipStageResolve=3,
    OverrideClipStageBeforeCapture=4,
    OverrideClipStageMutation=5,
    OverrideClipStageAfterCapture=6,
    OverrideClipStageComplete=7
};

enum PlayableTimeStage : uint32_t {
    PlayableTimeStageNone=0,
    PlayableTimeStageArguments=1,
    PlayableTimeStageRevision=2,
    PlayableTimeStageResolve=3,
    PlayableTimeStageCapture=4,
    PlayableTimeStagePreflight=5,
    PlayableTimeStageProjected=6,
    PlayableTimeStageMutation=7,
    PlayableTimeStageAfterCapture=8,
    PlayableTimeStageAfterCompare=9,
    PlayableTimeStageComplete=10
};

enum TargetNullClipStage : uint32_t {
    TargetNullClipStageNone=0,
    TargetNullClipStageArguments=1,
    TargetNullClipStageRevision=2,
    TargetNullClipStageResolve=3,
    TargetNullClipStageCapture=4,
    TargetNullClipStagePlan=5,
    TargetNullClipStageProjected=6,
    TargetNullClipStageStable=7,
    TargetNullClipStageMutation=8,
    TargetNullClipStageAfterCapture=9,
    TargetNullClipStageAfterCompare=10,
    TargetNullClipStageComplete=11
};

static bool Readable(const void* pointer,uint32_t bytes) {
    MEMORY_BASIC_INFORMATION memory = {};
    if (!pointer || VirtualQuery(pointer,&memory,sizeof(memory)) != sizeof(memory)) return false;
    if (memory.State != MEM_COMMIT || (memory.Protect & (PAGE_GUARD|PAGE_NOACCESS)) != 0) return false;
    const uintptr_t begin = reinterpret_cast<uintptr_t>(pointer);
    const uintptr_t end = begin + bytes;
    const uintptr_t regionEnd = reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize;
    return end >= begin && end <= regionEnd;
}

static bool Writable(const void* pointer,uint32_t bytes) {
    MEMORY_BASIC_INFORMATION memory={};
    if(!pointer||VirtualQuery(pointer,&memory,sizeof(memory))!=sizeof(memory))return false;
    if(memory.State!=MEM_COMMIT||(memory.Protect&(PAGE_GUARD|PAGE_NOACCESS))!=0)return false;
    const DWORD protection=memory.Protect&0xFFu;
    if(protection!=PAGE_READWRITE&&protection!=PAGE_WRITECOPY&&
       protection!=PAGE_EXECUTE_READWRITE&&protection!=PAGE_EXECUTE_WRITECOPY)return false;
    const uintptr_t begin=reinterpret_cast<uintptr_t>(pointer),end=begin+bytes;
    const uintptr_t regionEnd=reinterpret_cast<uintptr_t>(memory.BaseAddress)+memory.RegionSize;
    return end>=begin&&end<=regionEnd;
}

static bool EqualBytes(const void* left,const void* right,uint32_t count) {
    const uint8_t* a = static_cast<const uint8_t*>(left);
    const uint8_t* b = static_cast<const uint8_t*>(right);
    for (uint32_t i=0;i<count;++i) if (a[i] != b[i]) return false;
    return true;
}

static void CopyBytes(void* destination,const void* source,uint32_t count) {
    uint8_t* out = static_cast<uint8_t*>(destination);
    const uint8_t* in = static_cast<const uint8_t*>(source);
    for (uint32_t i=0;i<count;++i) out[i] = in[i];
}

static uint32_t Hash(const void* source,uint32_t count) {
    const uint8_t* bytes = static_cast<const uint8_t*>(source);
    uint32_t hash = 2166136261u;
    for (uint32_t i=0;i<count;++i) { hash ^= bytes[i]; hash *= 16777619u; }
    return hash;
}

static void InitializeReceipt(AnimatorControllerReceipt* receipt,uintptr_t unityBase,uintptr_t animator) {
    AnimatorControllerReceipt zero = {};
    *receipt = zero;
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(AnimatorControllerReceipt);
    receipt->unityBase = unityBase;
    receipt->animator = animator;
    receipt->firstDifference = 0xFFFFFFFFu;
    receipt->beforeByte = 0xFFFFFFFFu;
    receipt->afterByte = 0xFFFFFFFFu;
    receipt->firstDifferenceExceptFirstEvaluationFlag = 0xFFFFFFFFu;
    receipt->beforeByteExceptFirstEvaluationFlag = 0xFFFFFFFFu;
    receipt->afterByteExceptFirstEvaluationFlag = 0xFFFFFFFFu;
}

static void InitializeInputReceipt(AnimatorControllerInputReceipt* receipt,uintptr_t unityBase,uintptr_t animator) {
    AnimatorControllerInputReceipt zero = {};
    *receipt = zero;
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(AnimatorControllerInputReceipt);
    receipt->unityBase = unityBase;
    receipt->animator = animator;
}

static void InitializeTopologyReceipt(AnimatorTransitionTopologyReceipt* receipt,uintptr_t unityBase,uintptr_t animator) {
    AnimatorTransitionTopologyReceipt zero = {};
    *receipt = zero;
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(AnimatorTransitionTopologyReceipt);
    receipt->unityBase = unityBase;
    receipt->animator = animator;
}

static void InitializeMixerGraphReceipt(AnimatorMixerGraphReceipt* receipt,uintptr_t unityBase,uintptr_t animator) {
    AnimatorMixerGraphReceipt zero = {};
    *receipt = zero;
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(AnimatorMixerGraphReceipt);
    receipt->unityBase = unityBase;
    receipt->animator = animator;
    receipt->mismatchRecordIndex=0xFFFFFFFFu;
    receipt->mismatchByteOffset=0xFFFFFFFFu;
}

static void InitializeOwnerGraphReceipt(AnimatorOwnerGraphReceipt* receipt,uintptr_t unityBase,uintptr_t animator) {
    AnimatorOwnerGraphReceipt zero={};
    *receipt=zero;
    receipt->apiVersion=kApiVersion;
    receipt->structSize=sizeof(AnimatorOwnerGraphReceipt);
    receipt->unityBase=unityBase;
    receipt->animator=animator;
    receipt->failureRecord=0xFFFFFFFFu;
}

static void InitializeEndTransitionReceipt(AnimatorEndTransitionReceipt* receipt,
                                           uintptr_t unityBase,uintptr_t animator) {
    AnimatorEndTransitionReceipt zero={};
    *receipt=zero;
    receipt->apiVersion=kApiVersion;
    receipt->structSize=sizeof(AnimatorEndTransitionReceipt);
    receipt->unityBase=unityBase;
    receipt->animator=animator;
    receipt->failureLayer=0xFFFFFFFFu;
    receipt->failureStateMachine=0xFFFFFFFFu;
    receipt->failureRecord=0xFFFFFFFFu;
    receipt->failureByteOffset=0xFFFFFFFFu;
}

static void InitializeOverrideClipReceipt(AnimatorOverrideClipReceipt* receipt,
                                          uintptr_t unityBase,uintptr_t animator) {
    AnimatorOverrideClipReceipt zero={};
    *receipt=zero;
    receipt->apiVersion=kApiVersion;
    receipt->structSize=sizeof(AnimatorOverrideClipReceipt);
    receipt->unityBase=unityBase;
    receipt->animator=animator;
}

static void InitializePlayableTimeReceipt(AnimatorPlayableTimeReceipt* receipt,
                                          uintptr_t unityBase,uintptr_t animator) {
    AnimatorPlayableTimeReceipt zero={};
    *receipt=zero;
    receipt->apiVersion=kApiVersion;
    receipt->structSize=sizeof(AnimatorPlayableTimeReceipt);
    receipt->unityBase=unityBase;
    receipt->animator=animator;
    receipt->failureRecord=0xFFFFFFFFu;
    receipt->failureTargetRecord=0xFFFFFFFFu;
    receipt->failureByteOffset=0xFFFFFFFFu;
}

static void InitializeTargetNullClipReceipt(AnimatorTargetNullClipReceipt* receipt,
                                            uintptr_t unityBase,uintptr_t animator) {
    AnimatorTargetNullClipReceipt zero={};
    *receipt=zero;
    receipt->apiVersion=kApiVersion;
    receipt->structSize=sizeof(AnimatorTargetNullClipReceipt);
    receipt->unityBase=unityBase;
    receipt->animator=animator;
    receipt->failureLayer=0xFFFFFFFFu;
    receipt->failureStateMachine=0xFFFFFFFFu;
    receipt->failureRecord=0xFFFFFFFFu;
    receipt->failureByteOffset=0xFFFFFFFFu;
}

static bool Within(const void* base,uint32_t size,const void* pointer,uint32_t bytes) {
    const uintptr_t begin=reinterpret_cast<uintptr_t>(base);
    const uintptr_t end=begin+size;
    const uintptr_t item=reinterpret_cast<uintptr_t>(pointer);
    const uintptr_t itemEnd=item+bytes;
    return end>=begin&&itemEnd>=item&&item>=begin&&itemEnd<=end;
}

static bool RelativeWithin(const void* base,uint32_t size,const void* anchor,int32_t relative,
                           uint32_t bytes,const uint8_t*& result) {
    const int64_t target=static_cast<int64_t>(reinterpret_cast<uintptr_t>(anchor))+relative;
    if(target<0||target>0xFFFFFFFFll)return false;
    result=reinterpret_cast<const uint8_t*>(static_cast<uintptr_t>(target));
    return Within(base,size,result,bytes);
}

static bool Resolve(uintptr_t unityBase,uintptr_t animator,AnimatorControllerReceipt* receipt,
                    void*& controller,void*& memory,void*& allocator) {
    if (!Readable(reinterpret_cast<void*>(unityBase+kCopyControllerMemoryRva),sizeof(kCopyControllerMemoryBytes)) ||
        !Readable(reinterpret_cast<void*>(unityBase+kNormalizeControllerMemoryRva),sizeof(kNormalizeControllerMemoryBytes)) ||
        !Readable(reinterpret_cast<void*>(unityBase+kBumpAllocatorAllocateRva),sizeof(kBumpAllocatorAllocateBytes)) ||
        !Readable(reinterpret_cast<void*>(unityBase+kBumpAllocatorVtableRva),8)) {
        receipt->result = ResultUnreadable; return false;
    }
    if (!EqualBytes(reinterpret_cast<void*>(unityBase+kCopyControllerMemoryRva),kCopyControllerMemoryBytes,sizeof(kCopyControllerMemoryBytes)) ||
        !EqualBytes(reinterpret_cast<void*>(unityBase+kNormalizeControllerMemoryRva),kNormalizeControllerMemoryBytes,sizeof(kNormalizeControllerMemoryBytes)) ||
        !EqualBytes(reinterpret_cast<void*>(unityBase+kBumpAllocatorAllocateRva),kBumpAllocatorAllocateBytes,sizeof(kBumpAllocatorAllocateBytes)) ||
        *reinterpret_cast<uintptr_t*>(unityBase+kBumpAllocatorVtableRva)!=unityBase+kBumpAllocatorAllocateRva ||
        *reinterpret_cast<uintptr_t*>(unityBase+kBumpAllocatorVtableRva+4)!=unityBase+kBumpAllocatorFreeRva) {
        receipt->result = ResultRevisionMismatch; return false;
    }
    if (!Readable(reinterpret_cast<void*>(animator+0x288),4)) { receipt->result=ResultUnreadable; return false; }
    controller = *reinterpret_cast<void**>(animator+0x288);
    if (!Readable(controller,0xC0)) { receipt->result=ResultUnreadable; return false; }
    memory = *reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xB4);
    allocator = reinterpret_cast<void*>(reinterpret_cast<uintptr_t>(controller)+0x88);
    if (!Readable(memory,0x20) || !Readable(allocator,4)) { receipt->result=ResultUnreadable; return false; }
    receipt->controller = reinterpret_cast<uintptr_t>(controller);
    receipt->memoryBefore = reinterpret_cast<uintptr_t>(memory);
    receipt->allocator = reinterpret_cast<uintptr_t>(allocator);
    return true;
}

static void Release(void* allocator,void* allocation) {
    if (!allocation) return;
    void** table = *reinterpret_cast<void***>(allocator);
    AllocatorFree freeAllocation = reinterpret_cast<AllocatorFree>(table[1]);
    freeAllocation(allocator,allocation);
}

static void FirstDifference(const void* before,uint32_t beforeSize,const void* after,uint32_t afterSize,
                            AnimatorControllerReceipt* receipt) {
    const uint8_t* a=static_cast<const uint8_t*>(before);
    const uint8_t* b=static_cast<const uint8_t*>(after);
    const uint32_t count=beforeSize<afterSize?beforeSize:afterSize;
    for(uint32_t i=0;i<count;++i) if(a[i]!=b[i]) {
        ++receipt->differenceCount;
        if(receipt->firstDifference==0xFFFFFFFFu) {
            receipt->firstDifference=i;receipt->beforeByte=a[i];receipt->afterByte=b[i];
        }
        if(i!=kFirstEvaluationFlagOffset&&receipt->firstDifferenceExceptFirstEvaluationFlag==0xFFFFFFFFu) {
            receipt->firstDifferenceExceptFirstEvaluationFlag=i;
            receipt->beforeByteExceptFirstEvaluationFlag=a[i];
            receipt->afterByteExceptFirstEvaluationFlag=b[i];
        }
    }
    if(beforeSize!=afterSize) {
        const uint32_t maximum=beforeSize>afterSize?beforeSize:afterSize;
        receipt->differenceCount+=maximum-count;
        if(receipt->firstDifference==0xFFFFFFFFu)receipt->firstDifference=count;
        if(receipt->firstDifferenceExceptFirstEvaluationFlag==0xFFFFFFFFu)
            receipt->firstDifferenceExceptFirstEvaluationFlag=count;
    }
}

static uint32_t HashControllerInput(const void* prefix,const void* records,uint32_t recordBytes) {
    uint32_t hash = 2166136261u;
    const uint8_t* prefixBytes = static_cast<const uint8_t*>(prefix);
    for (uint32_t i=0;i<kControllerInputPrefixSize;++i) {
        hash ^= prefixBytes[i]; hash *= 16777619u;
    }
    const uint8_t* recordData = static_cast<const uint8_t*>(records);
    for (uint32_t i=0;i<recordBytes;++i) {
        hash ^= recordData[i]; hash *= 16777619u;
    }
    return hash;
}

static void HashAppend(uint32_t& hash,const void* source,uint32_t count) {
    const uint8_t* bytes=static_cast<const uint8_t*>(source);
    for(uint32_t i=0;i<count;++i){hash^=bytes[i];hash*=16777619u;}
}

enum ClipBindingCachePresence : uint32_t {
    ClipBindingACPresent=1u<<0,
    ClipBindingACInnerPresent=1u<<1,
    ClipBindingB0Present=1u<<2,
    ClipBindingB0InnerPresent=1u<<3,
    ClipBindingB4Present=1u<<4
};

static bool ResolveClipBindingSource(uintptr_t clipRuntimeData,uintptr_t& source) {
    source=0;
    if(!clipRuntimeData||clipRuntimeData>UINTPTR_MAX-0x5F0u)return false;
    const uintptr_t anchor=clipRuntimeData+0x5F0u;
    if(!Readable(reinterpret_cast<const void*>(anchor),4))return false;
    const int32_t relative=*reinterpret_cast<const int32_t*>(anchor);
    const int64_t resolved=static_cast<int64_t>(anchor)+relative;
    if(resolved<=0||resolved>static_cast<int64_t>(UINTPTR_MAX))return false;
    source=static_cast<uintptr_t>(resolved);
    return Readable(reinterpret_cast<const void*>(source),0x28);
}

static bool CaptureClipBindingCaches(AnimatorOwnerGraphRecord& node,uint32_t failureRecord,
                                     AnimatorOwnerGraphReceipt* receipt) {
    const uintptr_t clipRuntimeData=node.rawA4A7;
    const uintptr_t cacheAC=node.bindingAC;
    const uintptr_t cacheB0=node.bindingB0;
    const uintptr_t cacheB4=node.bindingB4;
    uintptr_t bindingSource=0;
    const bool needsBindingSource=cacheAC!=0||cacheB0!=0;
    if(needsBindingSource&&!ResolveClipBindingSource(clipRuntimeData,bindingSource)){
        receipt->result=ResultUnreadable;receipt->failureRecord=failureRecord;return false;
    }

    if(cacheAC){
        node.bindingCachePresence|=ClipBindingACPresent;
        if(!Readable(reinterpret_cast<const void*>(cacheAC),0x14)){
            receipt->result=ResultUnreadable;receipt->failureRecord=failureRecord;return false;
        }
        node.bindingACInner=*reinterpret_cast<const uintptr_t*>(cacheAC);
        node.bindingACCount=*reinterpret_cast<const uint32_t*>(cacheAC+4);
        const uint32_t sourceCount=*reinterpret_cast<const uint32_t*>(bindingSource+8);
        if(node.bindingACCount!=sourceCount||
           node.bindingACCount>kMaximumClipCacheBytes/0x14u){
            receipt->result=ResultInvalidBlob;receipt->failureRecord=failureRecord;return false;
        }
        const uint32_t innerBytes=node.bindingACCount*0x14u;
        if((innerBytes==0)!=(node.bindingACInner==0)){
            receipt->result=ResultInvalidBlob;receipt->failureRecord=failureRecord;return false;
        }
        if(innerBytes&&!Readable(reinterpret_cast<const void*>(node.bindingACInner),innerBytes)){
            receipt->result=ResultUnreadable;receipt->failureRecord=failureRecord;return false;
        }
        if(node.bindingACInner)node.bindingCachePresence|=ClipBindingACInnerPresent;
        // Canonicalize the allocator-owned inner pointer by hashing only the
        // four scalar header words and the complete, bounded inner array.
        uint32_t cacheHash=2166136261u;
        HashAppend(cacheHash,reinterpret_cast<const void*>(cacheAC+4),0x10);
        if(innerBytes)HashAppend(cacheHash,reinterpret_cast<const void*>(node.bindingACInner),innerBytes);
        node.bindingACHash=cacheHash;
    }

    if(cacheB0){
        node.bindingCachePresence|=ClipBindingB0Present;
        if(!Readable(reinterpret_cast<const void*>(cacheB0),4)){
            receipt->result=ResultUnreadable;receipt->failureRecord=failureRecord;return false;
        }
        const uint64_t count64=static_cast<uint64_t>(*reinterpret_cast<const uint32_t*>(bindingSource+8))+
                               *reinterpret_cast<const uint32_t*>(bindingSource+0x10)+
                               *reinterpret_cast<const uint32_t*>(bindingSource+0x24);
        if(count64>kMaximumClipCacheBytes/4u){
            receipt->result=ResultInvalidBlob;receipt->failureRecord=failureRecord;return false;
        }
        node.bindingB0Count=static_cast<uint32_t>(count64);
        node.bindingB0Inner=*reinterpret_cast<const uintptr_t*>(cacheB0);
        const uint32_t innerBytes=node.bindingB0Count*4u;
        if((innerBytes==0)!=(node.bindingB0Inner==0)){
            receipt->result=ResultInvalidBlob;receipt->failureRecord=failureRecord;return false;
        }
        if(innerBytes&&!Readable(reinterpret_cast<const void*>(node.bindingB0Inner),innerBytes)){
            receipt->result=ResultUnreadable;receipt->failureRecord=failureRecord;return false;
        }
        if(node.bindingB0Inner)node.bindingCachePresence|=ClipBindingB0InnerPresent;
        // The outer object contains only the allocator-owned inner pointer;
        // count plus inner bytes is its pointer-independent representation.
        uint32_t cacheHash=2166136261u;
        HashAppend(cacheHash,&node.bindingB0Count,sizeof(node.bindingB0Count));
        if(innerBytes)HashAppend(cacheHash,reinterpret_cast<const void*>(node.bindingB0Inner),innerBytes);
        node.bindingB0Hash=cacheHash;
    }

    if(cacheB4){
        node.bindingCachePresence|=ClipBindingB4Present;
        if(!Readable(reinterpret_cast<const void*>(cacheB4),0x200)){
            receipt->result=ResultUnreadable;receipt->failureRecord=failureRecord;return false;
        }
        uint32_t cacheHash=2166136261u;
        // Constructor RVA 0x641060 deliberately leaves [04,0F] and
        // [1C8,1CF] untouched. Exclude those allocator-padding bytes.
        HashAppend(cacheHash,reinterpret_cast<const void*>(cacheB4),0x04);
        HashAppend(cacheHash,reinterpret_cast<const void*>(cacheB4+0x10),0x1B8);
        HashAppend(cacheHash,reinterpret_cast<const void*>(cacheB4+0x1D0),0x30);
        node.bindingB4Hash=cacheHash;
    }
    return true;
}

static bool ValidWeight(uint32_t bits) {
    const uint32_t magnitude=bits&0x7FFFFFFFu;
    if((magnitude&0x7F800000u)==0x7F800000u)return false;
    return (bits&0x80000000u)==0||magnitude==0;
}

static bool SameMixerTopology(const AnimatorMixerWeightRecord& current,
                              const AnimatorMixerWeightRecord& saved) {
    if(current.recordKind==0){
        AnimatorMixerWeightRecord comparable=saved;
        comparable.outerWeightBits=current.outerWeightBits;
        return EqualBytes(&current,&comparable,sizeof(current));
    }
    if(current.recordKind!=1||current.inputIndex==0xFFFFFFFFu)
        return EqualBytes(&current,&saved,sizeof(current));
    AnimatorMixerWeightRecord comparable=saved;
    comparable.mixerWeightBits=current.mixerWeightBits;
    return EqualBytes(&current,&comparable,sizeof(current));
}

static void DescribeMixerMismatch(const AnimatorMixerWeightRecord& current,
                                  const AnimatorMixerWeightRecord& saved,uint32_t recordIndex,
                                  bool exact,AnimatorMixerGraphReceipt* receipt) {
    AnimatorMixerWeightRecord comparable=saved;
    if(!exact&&current.recordKind==0)comparable.outerWeightBits=current.outerWeightBits;
    else if(!exact&&current.recordKind==1&&current.inputIndex!=0xFFFFFFFFu)
        comparable.mixerWeightBits=current.mixerWeightBits;
    const uint8_t* a=reinterpret_cast<const uint8_t*>(&current);
    const uint8_t* b=reinterpret_cast<const uint8_t*>(&comparable);
    for(uint32_t i=0;i<sizeof(current);++i)if(a[i]!=b[i]){
        receipt->mismatchRecordIndex=recordIndex;receipt->mismatchByteOffset=i;
        const uint32_t word=i&~3u;
        receipt->expectedWord=*reinterpret_cast<const uint32_t*>(b+word);
        receipt->actualWord=*reinterpret_cast<const uint32_t*>(a+word);
        return;
    }
    receipt->mismatchRecordIndex=recordIndex;
}

static bool ResolveMixerGraph(uintptr_t unityBase,uintptr_t animator,AnimatorMixerGraphReceipt* receipt,
                              void*& controller,void*& constant,void*& descriptors) {
    AnimatorControllerReceipt controllerReceipt={};
    InitializeReceipt(&controllerReceipt,unityBase,animator);
    void* memory=0;void* allocator=0;
    if(!Resolve(unityBase,animator,&controllerReceipt,controller,memory,allocator)){
        receipt->result=controllerReceipt.result;receipt->lastError=controllerReceipt.lastError;return false;
    }
    receipt->controller=reinterpret_cast<uintptr_t>(controller);
    if(!Readable(controller,0xF0)){receipt->result=ResultUnreadable;return false;}
    constant=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xAC);
    descriptors=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xE8);
    receipt->controllerConstant=reinterpret_cast<uintptr_t>(constant);
    receipt->descriptors=reinterpret_cast<uintptr_t>(descriptors);
    if(!Readable(constant,12)){receipt->result=ResultUnreadable;return false;}
    receipt->layerCount=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(constant)+8);
    const uint32_t descriptorCount=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0xEC);
    if(receipt->layerCount>kMaximumTopologyLayers||descriptorCount!=receipt->layerCount){
        receipt->result=ResultTopologyMismatch;receipt->mismatchCount=1;return false;
    }
    if(receipt->layerCount!=0&&!Readable(descriptors,receipt->layerCount*8)){
        receipt->result=ResultUnreadable;return false;
    }
    return true;
}

static bool VerifyMixerRevision(uintptr_t unityBase,AnimatorMixerGraphReceipt* receipt) {
    const void* outerVtable=reinterpret_cast<void*>(unityBase+kMixerPlayableVtableRva);
    const void* innerVtable=reinterpret_cast<void*>(unityBase+kAnimationMixerPlayableVtableRva);
    const void* setter=reinterpret_cast<void*>(unityBase+kSetInputWeightRva);
    if(!Readable(outerVtable,0x14)||!Readable(innerVtable,0x14)||!Readable(setter,sizeof(kSetInputWeightBytes))){
        receipt->result=ResultUnreadable;return false;
    }
    if(*reinterpret_cast<const uintptr_t*>(reinterpret_cast<uintptr_t>(outerVtable)+0x10)!=unityBase+kSetInputWeightRva||
       *reinterpret_cast<const uintptr_t*>(reinterpret_cast<uintptr_t>(innerVtable)+0x10)!=unityBase+kSetInputWeightRva||
       !EqualBytes(setter,kSetInputWeightBytes,sizeof(kSetInputWeightBytes))){
        receipt->result=ResultRevisionMismatch;return false;
    }
    return true;
}

struct OwnerRevisionBlock { uint32_t rva,size,hash; };

static bool VerifyOwnerVtable(uintptr_t unityBase,uint32_t vtableRva,uint32_t inputWrapperRva) {
    const uintptr_t table=unityBase+vtableRva;
    if(!Readable(reinterpret_cast<const void*>(table),0x58))return false;
    return *reinterpret_cast<const uintptr_t*>(table+0x10)==unityBase+kSetInputWeightRva&&
           *reinterpret_cast<const uintptr_t*>(table+0x48)==unityBase+inputWrapperRva&&
           *reinterpret_cast<const uintptr_t*>(table+0x4C)==unityBase+kSetOutputConnectionRva&&
           *reinterpret_cast<const uintptr_t*>(table+0x50)==unityBase+kClearInputRva&&
           *reinterpret_cast<const uintptr_t*>(table+0x54)==unityBase+kClearOutputRva;
}

static bool VerifyOwnerRevision(uintptr_t unityBase,AnimatorOwnerGraphReceipt* receipt) {
    static const OwnerRevisionBlock blocks[]={
        {kEndTransitionRva,0x117,0x47CA3AEAu},{kRootByTypeRva,0x49,0xBF7FDD56u},
        {kSetClipRva,0x3D,0x9954C652u},{kConnectNoTopologyChangeRva,0x2D,0x89EEF470u},
        {kAnimationMixerPlayableDeletingDestructorRva,0x33,0x2CCF6B99u},
        {kAnimationMixerPlayableAllocationSizeSpanRva,0x0F,0x69ED1300u},
        {kDisconnectNoTopologyChangeRva,0x1D,0xDF464C29u},{kLowerConnectRva,0xBA,0xB48BA0DBu},
        {kSetInputWeightRva,0x28,0x8E5F778Fu},{kSetInputConnectionWrapperRva,0x23,0xFD5ACF44u},
        {kClipPoseSetInputConnectionWrapperRva,0x1E,0xF33BE2BBu},{0x2F65F0,0x05,0xB2CE3DE3u},
        {0x2F1D50,0x1D,0x1665C9ECu},{0x2F1DC3,0x2E,0xB187B2FEu},
        {0x2F12DC,0x35,0x763804C9u},{0x2F138C,0x2D,0xE0970A7Eu},
        {0x2F4E00,0x2D,0x127935CBu},{0x2F4EA4,0x42,0xA3AF2BB4u},
        {0x2F50A0,0x21,0x6AD009CEu},{0x2F50EB,0x18,0xE6E7F57Cu},
        {0x2F515D,0x20,0x341B3E08u}
    };
    for(uint32_t i=0;i<sizeof(blocks)/sizeof(blocks[0]);++i){
        const uintptr_t address=unityBase+blocks[i].rva;
        if(address<unityBase||!Readable(reinterpret_cast<const void*>(address),blocks[i].size)){
            receipt->result=ResultUnreadable;return false;
        }
        if(Hash(reinterpret_cast<const void*>(address),blocks[i].size)!=blocks[i].hash){
            receipt->result=ResultRevisionMismatch;return false;
        }
    }
    if(!VerifyOwnerVtable(unityBase,kMixerPlayableVtableRva,kSetInputConnectionWrapperRva)||
       !VerifyOwnerVtable(unityBase,kAnimationMixerPlayableVtableRva,kSetInputConnectionWrapperRva)||
       !VerifyOwnerVtable(unityBase,kAnimationPosePlayableVtableRva,kClipPoseSetInputConnectionWrapperRva)||
       !VerifyOwnerVtable(unityBase,kAnimationClipPlayableVtableRva,kClipPoseSetInputConnectionWrapperRva)){
        receipt->result=ResultRevisionMismatch;return false;
    }
    return true;
}

static bool VisitMixerRecord(const AnimatorMixerWeightRecord& current,
                             const AnimatorMixerWeightRecord* saved,uint32_t savedCount,
                             AnimatorMixerWeightRecord* output,uint32_t outputCount,
                             bool exact,bool write,SetInputWeight setter,
                             uint32_t& visited,uint32_t& hash,AnimatorMixerGraphReceipt* receipt) {
    if(visited>=kMaximumMixerGraphRecords){receipt->result=ResultInvalidBlob;return false;}
    if(output){
        if(visited>=outputCount){receipt->result=ResultCapacity;return false;}
        output[visited]=current;
    }
    if(saved){
        if(visited>=savedCount){receipt->result=ResultTopologyMismatch;++receipt->mismatchCount;return false;}
        const bool matches=exact?EqualBytes(&current,&saved[visited],sizeof(current)):
                                 SameMixerTopology(current,saved[visited]);
        if(!matches){DescribeMixerMismatch(current,saved[visited],visited,exact,receipt);
            receipt->result=exact?ResultWriteMismatch:ResultTopologyMismatch;
            ++receipt->mismatchCount;return false;}
        const uintptr_t targetEntries=current.recordKind==0?current.outerEntries:current.mixerEntries;
        if(current.recordKind<=1&&current.inputIndex!=0xFFFFFFFFu&&
           !Writable(reinterpret_cast<void*>(targetEntries+current.inputIndex*12),4)){
            receipt->result=ResultUnreadable;return false;
        }
        const uint32_t currentWeight=current.recordKind==0?current.outerWeightBits:current.mixerWeightBits;
        const uint32_t savedWeight=current.recordKind==0?saved[visited].outerWeightBits:saved[visited].mixerWeightBits;
        if(write&&current.recordKind<=1&&current.inputIndex!=0xFFFFFFFFu&&currentWeight!=savedWeight){
            if(!ValidWeight(savedWeight)){
                receipt->result=ResultInvalidBlob;return false;
            }
            union { uint32_t bits;float value; } weight={savedWeight};
            setter(reinterpret_cast<void*>(current.recordKind==0?current.outer:current.mixer),current.inputIndex,weight.value);
            ++receipt->restoredWeightCount;
        }
    }
    HashAppend(hash,&current,sizeof(current));++visited;return true;
}

static bool WalkMixerGraph(uintptr_t unityBase,void* controller,void* constant,void* descriptors,uint32_t layerCount,
                           const AnimatorMixerWeightRecord* saved,uint32_t savedCount,
                           AnimatorMixerWeightRecord* output,uint32_t outputCount,
                           bool exact,bool write,SetInputWeight setter,
                           uint32_t& visited,uint32_t& hash,AnimatorMixerGraphReceipt* receipt) {
    visited=0;hash=2166136261u;
    const uint8_t* descriptorBytes=static_cast<const uint8_t*>(descriptors);
    for(uint32_t layer=0;layer<layerCount;++layer){
        const uint8_t* descriptor=descriptorBytes+layer*8;
        void** stateMachines=*reinterpret_cast<void***>(const_cast<uint8_t*>(descriptor));
        const uint32_t stateMachineCount=*reinterpret_cast<const uint32_t*>(descriptor+4);
        if(stateMachineCount>kMaximumStateMachinesPerLayer){receipt->result=ResultInvalidBlob;return false;}
        if(stateMachineCount!=0&&!Readable(stateMachines,stateMachineCount*4)){
            receipt->result=ResultUnreadable;return false;
        }
        AnimatorMixerWeightRecord layerRecord={};
        layerRecord.recordKind=2;layerRecord.layerIndex=layer;layerRecord.stateMachineIndex=stateMachineCount;
        layerRecord.trueBranch=0xFFFFFFFFu;layerRecord.inputIndex=0xFFFFFFFFu;
        layerRecord.outer=reinterpret_cast<uintptr_t>(controller);
        layerRecord.outerInternal=reinterpret_cast<uintptr_t>(constant);
        layerRecord.outerEntries=reinterpret_cast<uintptr_t>(descriptors);
        layerRecord.outerInputCount=layerCount;
        layerRecord.outerChild=reinterpret_cast<uintptr_t>(descriptor);
        layerRecord.mixer=reinterpret_cast<uintptr_t>(stateMachines);
        if(!VisitMixerRecord(layerRecord,saved,savedCount,output,outputCount,exact,write,setter,
                             visited,hash,receipt))return false;
        for(uint32_t stateMachine=0;stateMachine<stateMachineCount;++stateMachine){
            void* outer=stateMachines[stateMachine];
            if(!Readable(outer,0xA8)||*reinterpret_cast<uintptr_t*>(outer)!=unityBase+kMixerPlayableVtableRva){
                receipt->result=ResultTopologyMismatch;++receipt->mismatchCount;return false;
            }
            void* outerInternal=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(outer)+0x10);
            if(!Readable(outerInternal,0x1C)){receipt->result=ResultUnreadable;return false;}
            void* outerEntries=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(outerInternal)+0x10);
            const uint32_t outerCount=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(outerInternal)+0x18);
            if(outerCount<2||outerCount>kMaximumMixerInputs||!Readable(outerEntries,outerCount*12)){
                receipt->result=ResultTopologyMismatch;++receipt->mismatchCount;return false;
            }
            const uint32_t outerMode=*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(outer)+0xA0);
            if(outerMode>2){receipt->result=ResultTopologyMismatch;++receipt->mismatchCount;return false;}
            for(uint32_t outerInput=0;outerInput<outerCount;++outerInput){
                const uint8_t* entry=static_cast<const uint8_t*>(outerEntries)+outerInput*12;
                AnimatorMixerWeightRecord record={};
                record.recordKind=0;record.layerIndex=layer;record.stateMachineIndex=stateMachine;
                record.trueBranch=2;record.inputIndex=outerInput;
                record.outer=reinterpret_cast<uintptr_t>(outer);
                record.outerInternal=reinterpret_cast<uintptr_t>(outerInternal);
                record.outerEntries=reinterpret_cast<uintptr_t>(outerEntries);
                record.outerInputCount=outerCount;record.outerEntriesHash=0;
                record.outerModeA0=outerMode;
                record.outerArgumentA4=*reinterpret_cast<const uint8_t*>(reinterpret_cast<uintptr_t>(outer)+0xA4);
                record.outerWeightBits=*reinterpret_cast<const uint32_t*>(entry);
                record.outerChild=*reinterpret_cast<const uintptr_t*>(entry+4);
                record.outerWord8=*reinterpret_cast<const uint32_t*>(entry+8);
                if(!VisitMixerRecord(record,saved,savedCount,output,outputCount,exact,write,setter,
                                     visited,hash,receipt))return false;
            }
            for(uint32_t branch=0;branch<2;++branch){
                const uint8_t* outerEntry=static_cast<const uint8_t*>(outerEntries)+branch*12;
                void* mixer=*reinterpret_cast<void* const*>(outerEntry+4);
                if(!Readable(mixer,0xA0)||*reinterpret_cast<uintptr_t*>(mixer)!=unityBase+kAnimationMixerPlayableVtableRva){
                    receipt->result=ResultTopologyMismatch;++receipt->mismatchCount;return false;
                }
                void* mixerInternal=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(mixer)+0x10);
                if(!Readable(mixerInternal,0x1C)){receipt->result=ResultUnreadable;return false;}
                void* mixerEntries=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(mixerInternal)+0x10);
                const uint32_t mixerCount=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(mixerInternal)+0x18);
                if(mixerCount>kMaximumMixerInputs||
                   (mixerCount!=0&&!Readable(mixerEntries,mixerCount*12))){
                    receipt->result=ResultTopologyMismatch;++receipt->mismatchCount;return false;
                }
                const uint32_t emitted=mixerCount==0?1:mixerCount;
                for(uint32_t input=0;input<emitted;++input){
                    AnimatorMixerWeightRecord record={};
                    record.recordKind=1;record.layerIndex=layer;record.stateMachineIndex=stateMachine;
                    record.trueBranch=branch==0?1u:0u;record.inputIndex=mixerCount==0?0xFFFFFFFFu:input;
                    record.outer=reinterpret_cast<uintptr_t>(outer);
                    record.outerInternal=reinterpret_cast<uintptr_t>(outerInternal);
                    record.outerEntries=reinterpret_cast<uintptr_t>(outerEntries);
                    record.outerInputCount=outerCount;record.outerEntriesHash=0;
                    record.outerModeA0=outerMode;
                    record.outerArgumentA4=*reinterpret_cast<const uint8_t*>(reinterpret_cast<uintptr_t>(outer)+0xA4);
                    record.outerWeightBits=*reinterpret_cast<const uint32_t*>(outerEntry);
                    record.outerChild=reinterpret_cast<uintptr_t>(mixer);
                    record.outerWord8=*reinterpret_cast<const uint32_t*>(outerEntry+8);
                    record.mixer=reinterpret_cast<uintptr_t>(mixer);
                    record.mixerInternal=reinterpret_cast<uintptr_t>(mixerInternal);
                    record.mixerEntries=reinterpret_cast<uintptr_t>(mixerEntries);
                    record.mixerInputCount=mixerCount;
                    // AnimationMixerPlayable ends at +0xA0 in this revision;
                    // the legacy observation at +0xA5 sampled an adjacent
                    // allocation. Keep the serialized field canonical so old
                    // ABI consumers retain their fixed record layout.
                    record.mixerFlagA5=0u;
                    if(mixerCount!=0){
                        const uint8_t* entry=static_cast<const uint8_t*>(mixerEntries)+input*12;
                        record.mixerWeightBits=*reinterpret_cast<const uint32_t*>(entry);
                        record.mixerChild=*reinterpret_cast<const uintptr_t*>(entry+4);
                        record.mixerWord8=*reinterpret_cast<const uint32_t*>(entry+8);
                    }
                    if(!VisitMixerRecord(record,saved,savedCount,output,outputCount,exact,write,setter,
                                         visited,hash,receipt))return false;
                }
            }
        }
    }
    if(saved&&visited!=savedCount){receipt->result=exact?ResultWriteMismatch:ResultTopologyMismatch;
        ++receipt->mismatchCount;return false;}
    return true;
}

static bool ApplyMixerWeightValues(const AnimatorMixerWeightRecord* saved,uint32_t savedCount,
                                   const uint32_t* preimage,bool useSaved,SetInputWeight setter,
                                   bool countWrites,AnimatorMixerGraphReceipt* receipt) {
    for(uint32_t i=0;i<savedCount;++i){
        if(saved[i].recordKind>1||saved[i].inputIndex==0xFFFFFFFFu)continue;
        const uintptr_t entries=saved[i].recordKind==0?saved[i].outerEntries:saved[i].mixerEntries;
        const uintptr_t playable=saved[i].recordKind==0?saved[i].outer:saved[i].mixer;
        uint32_t* target=reinterpret_cast<uint32_t*>(entries+saved[i].inputIndex*12);
        const uint32_t captured=saved[i].recordKind==0?saved[i].outerWeightBits:saved[i].mixerWeightBits;
        const uint32_t desired=useSaved?captured:preimage[i];
        if(*target==desired)continue;
        union { uint32_t bits;float value; } weight={desired};
        setter(reinterpret_cast<void*>(playable),saved[i].inputIndex,weight.value);
        if(*target!=desired)return false;
        if(countWrites)++receipt->restoredWeightCount;
    }
    return true;
}

enum OwnerRecordKind : uint32_t {
    OwnerOuterNode=1,OwnerOuterInput=2,OwnerOuterOutput=3,
    OwnerBranchNode=10,OwnerBranchInput=11,OwnerBranchOutput=12,
    OwnerChildNode=20,OwnerChildInput=21,OwnerChildOutput=22,
    OwnerResolverNode=30,OwnerResolverInput=31,OwnerResolverOutput=32,
    OwnerResolverDecision=33,OwnerResolverTerminal=34
};

struct OwnerRotationPlan {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uintptr_t outer;
    uintptr_t outerEntries;
    uintptr_t oldBranch0;
    uintptr_t oldBranch1;
    uintptr_t oldBranch2;
    uintptr_t oldBranch0InputEntries;
    uint32_t oldBranch0InputCount;
    uintptr_t oldBranch1InputEntries;
    uint32_t oldBranch1InputCount;
    uintptr_t oldBranch0Output0;
    uintptr_t oldBranch1Output0;
    uint32_t targetOuterWeight0;
    uint32_t targetOuterWeight1;
    uint32_t targetOuterWeight2;
};

struct OwnerClearedClip {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t targetBranchIndex;
    uint32_t inputIndex;
    uint32_t currentRecord;
    uint32_t requiresPreClear;
    uintptr_t playable;
    uintptr_t dirtyRoot;
    uint32_t rootDirtyBefore;
    uint32_t expectedRawA4A7;
    AnimatorOwnerGraphRecord preimage;
};

struct OwnerStagedClip {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uintptr_t playable;
    AnimatorOwnerGraphRecord preimage;
};

// A clip playable whose physical object will occupy the correct logical branch
// after EndTransition, but whose non-null clip still belongs to the live
// branch role.  SetClip is applied only after the branch rotation, so the same
// plan also covers an old-branch-zero clip that EndTransition clears first.
struct OwnerReboundClip {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t targetBranchIndex;
    uint32_t inputIndex;
    uint32_t currentRecord;
    uintptr_t playable;
    uintptr_t sourceClip;
    uintptr_t targetClip;
    uintptr_t dirtyRoot;
    uint32_t playableDirtyBefore;
    uint32_t rootDirtyBefore;
    AnimatorOwnerGraphRecord preimage;
};

enum OwnerWeightPlanRole : uint32_t {
    OwnerWeightPrimeOldBranch1=1,
    OwnerWeightFinalOldBranch0=2,
    OwnerWeightFinalOuter=3
};

struct OwnerWeightPlan {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t role;
    uint32_t inputIndex;
    uint32_t currentRecord;
    uintptr_t mixer;
    uintptr_t mixerVtable;
    uintptr_t mixerInternal;
    uintptr_t inputEntries;
    uint32_t inputCount;
    uint32_t inputCapacityRaw;
    uintptr_t entryAddress;
    uintptr_t entryPlayableBefore;
    uintptr_t entryPlayableAfter;
    uint32_t entryPortRaw;
    uint32_t weightBefore;
    uint32_t weightAfterEnd;
    uint32_t weightTarget;
    uint32_t primeWriteStarted;
    uint32_t primeWriteCompleted;
};

struct DirectClipClearPlan {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t branchIndex;
    uint32_t inputIndex;
    uint32_t currentRecord;
    uint32_t targetRecord;
    uintptr_t playable;
    uintptr_t expectedClip;
    uintptr_t dirtyRoot;
    uintptr_t outerWeightAddress;
    uintptr_t branchMixer;
    uintptr_t branchInternal;
    uintptr_t branchInputEntries;
    uint32_t branchInputCount;
    uint32_t branchInputCapacityRaw;
    uintptr_t inputWeightAddress;
    uintptr_t inputPlayable;
    uint32_t inputPortRaw;
    uint32_t currentInputRecord;
    uint32_t inputWeightBefore;
    uint32_t playableDirtyBefore;
    uint32_t rootDirtyBefore;
};

// OverrideClipPlayables can leave an already-null, zero-input clip in a
// transient lifecycle preimage which Unity's own null-clip maintenance path
// cannot finish: +0x92 remains dirty and the constructor-owned +0x10C option
// remains cleared.  This paired plan is admitted only for that exact preimage
// and restores the checkpoint values transactionally without evaluating the
// graph.
struct AlreadyNullClipFinalizePlan {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t branchIndex;
    uint32_t inputIndex;
    uint32_t currentRecord;
    uintptr_t playable;
    uint32_t dirtyBefore;
    uint32_t dirtyTarget;
    uint32_t clipFlagsBefore;
    uint32_t clipFlagsTarget;
};

// A Playable connection stores the authoritative mixing weight on the
// destination input.  Its reciprocal source-output entry is lifecycle residue:
// construction initializes it to zero, while ClearOutput writes one.  Restore
// the captured zero only for an exact branch whose every destination input is
// zero and whose children are coherent null clips.
struct EmptyBranchOutputFinalizePlan {
    uint32_t layerIndex;
    uint32_t stateMachineIndex;
    uint32_t branchIndex;
    uint32_t currentRecord;
    uintptr_t branchMixer;
    uintptr_t branchInternal;
    uintptr_t outputEntries;
    uint32_t outputCount;
    uint32_t outputCapacityRaw;
    uintptr_t outputAddress;
    uintptr_t outputPlayable;
    uint32_t outputPortRaw;
    uint32_t weightBefore;
    uint32_t weightTarget;
};

struct OwnerProjection {
    const OwnerRotationPlan* plans;
    uint32_t planCount;
    const OwnerClearedClip* clearedClips;
    uint32_t clearedClipCount;
    const OwnerStagedClip* stagedClips;
    uint32_t stagedClipCount;
    const OwnerReboundClip* reboundClips;
    uint32_t reboundClipCount;
    const AnimatorOwnerGraphRecord* target;
    uint32_t targetCount;
    uint32_t admitStagedRotationTransient;
    const DirectClipClearPlan* directClipPlans;
    uint32_t directClipPlanCount;
    const AlreadyNullClipFinalizePlan* alreadyNullClipPlans;
    uint32_t alreadyNullClipPlanCount;
    const EmptyBranchOutputFinalizePlan* emptyOutputPlans;
    uint32_t emptyOutputPlanCount;
};

static const OwnerRotationPlan* FindOwnerRotation(const OwnerProjection* projection,
                                                  uint32_t layer,uint32_t stateMachine) {
    if(!projection)return 0;
    for(uint32_t i=0;i<projection->planCount;++i)
        if(projection->plans[i].layerIndex==layer&&
           projection->plans[i].stateMachineIndex==stateMachine)return projection->plans+i;
    return 0;
}

static const OwnerClearedClip* FindOwnerClearedClip(const OwnerProjection* projection,
                                                    uintptr_t playable) {
    if(!projection)return 0;
    for(uint32_t i=0;i<projection->clearedClipCount;++i)
        if(projection->clearedClips[i].playable==playable)return projection->clearedClips+i;
    return 0;
}

static const OwnerStagedClip* FindOwnerStagedClip(const OwnerProjection* projection,
                                                  uint32_t layer,uint32_t stateMachine,
                                                  uintptr_t playable) {
    if(!projection)return 0;
    const OwnerStagedClip* found=0;
    for(uint32_t i=0;i<projection->stagedClipCount;++i){
        const OwnerStagedClip& staged=projection->stagedClips[i];
        if(staged.layerIndex!=layer||staged.stateMachineIndex!=stateMachine||
           staged.playable!=playable)continue;
        if(found)return 0;
        found=projection->stagedClips+i;
    }
    return found;
}

static const OwnerReboundClip* FindOwnerReboundClip(const OwnerProjection* projection,
                                                     uintptr_t playable) {
    if(!projection||!playable)return 0;
    const OwnerReboundClip* found=0;
    for(uint32_t i=0;i<projection->reboundClipCount;++i){
        const OwnerReboundClip& rebound=projection->reboundClips[i];
        if(rebound.playable!=playable)continue;
        if(found)return 0;
        found=projection->reboundClips+i;
    }
    return found;
}

static const DirectClipClearPlan* FindDirectClipInputPlan(
    const OwnerProjection* projection,const AnimatorOwnerGraphRecord& record) {
    if(!projection||record.recordKind!=OwnerBranchInput)return 0;
    const DirectClipClearPlan* found=0;
    for(uint32_t i=0;i<projection->directClipPlanCount;++i){
        const DirectClipClearPlan& plan=projection->directClipPlans[i];
        if(record.layerIndex!=plan.layerIndex||record.stateMachineIndex!=plan.stateMachineIndex||
           record.branchIndex!=plan.branchIndex||record.inputIndex!=plan.inputIndex||
           record.self!=plan.branchMixer||record.entryAddress!=plan.inputWeightAddress)continue;
        if(found)return 0;
        found=projection->directClipPlans+i;
    }
    return found;
}

static const AlreadyNullClipFinalizePlan* FindAlreadyNullClipFinalizePlan(
    const OwnerProjection* projection,const AnimatorOwnerGraphRecord& record) {
    if(!projection||record.vtable==0)return 0;
    const AlreadyNullClipFinalizePlan* found=0;
    for(uint32_t i=0;i<projection->alreadyNullClipPlanCount;++i){
        const AlreadyNullClipFinalizePlan& plan=projection->alreadyNullClipPlans[i];
        if(record.self!=plan.playable)continue;
        if(found)return 0;
        found=projection->alreadyNullClipPlans+i;
    }
    return found;
}

static const EmptyBranchOutputFinalizePlan* FindEmptyBranchOutputFinalizePlan(
    const OwnerProjection* projection,const AnimatorOwnerGraphRecord& record) {
    if(!projection||record.recordKind!=OwnerBranchOutput)return 0;
    const EmptyBranchOutputFinalizePlan* found=0;
    for(uint32_t i=0;i<projection->emptyOutputPlanCount;++i){
        const EmptyBranchOutputFinalizePlan& plan=projection->emptyOutputPlans[i];
        if(record.layerIndex!=plan.layerIndex||record.stateMachineIndex!=plan.stateMachineIndex||
           record.branchIndex!=plan.branchIndex||record.entryAddress!=plan.outputAddress)continue;
        if(found)return 0;
        found=projection->emptyOutputPlans+i;
    }
    return found;
}

static bool IsOwnerDirtyRoot(const OwnerProjection* projection,uintptr_t playable) {
    if(!projection||!playable)return false;
    for(uint32_t i=0;i<projection->clearedClipCount;++i)
        if(projection->clearedClips[i].dirtyRoot==playable)return true;
    for(uint32_t i=0;i<projection->reboundClipCount;++i)
        if(projection->reboundClips[i].dirtyRoot==playable)return true;
    return false;
}

static const AnimatorOwnerGraphRecord* FindProjectionTargetBranchInput(
    const OwnerProjection* projection,uint32_t layer,uint32_t stateMachine,uint32_t branch,uint32_t input) {
    if(!projection||!projection->target)return 0;
    const AnimatorOwnerGraphRecord* found=0;
    for(uint32_t i=0;i<projection->targetCount;++i){
        const AnimatorOwnerGraphRecord& record=projection->target[i];
        if(record.recordKind==OwnerBranchInput&&record.layerIndex==layer&&
           record.stateMachineIndex==stateMachine&&record.branchIndex==branch&&record.inputIndex==input){
            if(found)return 0;
            found=projection->target+i;
        }
    }
    return found;
}

static void ProjectOwnerRecord(AnimatorOwnerGraphRecord& record,const OwnerProjection* projection) {
    if(!projection)return;
    const bool carriesNodeState=record.recordKind!=OwnerResolverDecision&&
                                record.recordKind!=OwnerResolverTerminal;
    const OwnerRotationPlan* plan=FindOwnerRotation(projection,record.layerIndex,record.stateMachineIndex);
    if(plan){
        if(carriesNodeState&&
           (record.self==plan->outer||record.self==plan->oldBranch0||record.self==plan->oldBranch1)){
            record.flags7C|=0x80u;
            record.dirty9093&=0xFFFFFF00u;
        }
        if(record.entryAddress==plan->outerEntries){
            record.entryWeightBits=plan->targetOuterWeight0;
            record.entryPlayable=plan->oldBranch1;
            record.entryPortRaw=0;
        } else if(record.entryAddress==plan->outerEntries+12u){
            record.entryWeightBits=plan->targetOuterWeight1;
            record.entryPlayable=plan->oldBranch0;
            record.entryPortRaw=0;
        } else if(record.entryAddress==plan->outerEntries+24u){
            record.entryWeightBits=plan->targetOuterWeight2;
        }
        if(record.entryAddress==plan->oldBranch0Output0||record.entryAddress==plan->oldBranch1Output0){
            record.entryWeightBits=0x3F800000u;
            record.entryPlayable=plan->outer;
        }
        if(record.entryAddress>=plan->oldBranch0InputEntries){
            const uintptr_t relative=record.entryAddress-plan->oldBranch0InputEntries;
            if(relative<plan->oldBranch0InputCount*12u&&relative%12u==0){
                const uint32_t input=static_cast<uint32_t>(relative/12u);
                const AnimatorOwnerGraphRecord* targetInput=FindProjectionTargetBranchInput(
                    projection,record.layerIndex,record.stateMachineIndex,1u,input);
                if(targetInput)record.entryWeightBits=targetInput->entryWeightBits;
            }
        }
        if(record.entryAddress>=plan->oldBranch1InputEntries){
            const uintptr_t relative=record.entryAddress-plan->oldBranch1InputEntries;
            if(relative<plan->oldBranch1InputCount*12u&&relative%12u==0){
                const uint32_t input=static_cast<uint32_t>(relative/12u);
                const AnimatorOwnerGraphRecord* targetInput=FindProjectionTargetBranchInput(
                    projection,record.layerIndex,record.stateMachineIndex,0u,input);
                if(targetInput)record.entryWeightBits=targetInput->entryWeightBits;
            }
        }
    }
    const OwnerClearedClip* cleared=carriesNodeState?FindOwnerClearedClip(projection,record.self):0;
    if(cleared){
        record.clip108=0;
        record.dirty9093=(record.dirty9093&0xFF00FFFFu)|0x00010000u;
    }
    const OwnerReboundClip* rebound=carriesNodeState?FindOwnerReboundClip(projection,record.self):0;
    if(rebound){
        if(record.recordKind==OwnerChildNode||record.recordKind==OwnerChildOutput)
            record.clip108=rebound->targetClip;
        record.dirty9093=(record.dirty9093&0xFF00FFFFu)|0x00010000u;
    }
    if(carriesNodeState&&IsOwnerDirtyRoot(projection,record.self))
        record.dirty9093=(record.dirty9093&0x00FFFFFFu)|0x01000000u;
    if(FindDirectClipInputPlan(projection,record))record.entryWeightBits=0u;
    const AlreadyNullClipFinalizePlan* nullFinalize=
        carriesNodeState?FindAlreadyNullClipFinalizePlan(projection,record):0;
    if(nullFinalize){
        record.dirty9093=nullFinalize->dirtyTarget;
        record.clipFlags10C=nullFinalize->clipFlagsTarget;
    }
    const EmptyBranchOutputFinalizePlan* outputFinalize=
        FindEmptyBranchOutputFinalizePlan(projection,record);
    if(outputFinalize)record.entryWeightBits=outputFinalize->weightTarget;
}

static bool EmitOwnerRecord(const AnimatorOwnerGraphRecord& record,
                            AnimatorOwnerGraphRecord* output,uint32_t outputCount,
                            uint32_t& visited,uint32_t& hash,AnimatorOwnerGraphReceipt* receipt) {
    if(visited>=kMaximumOwnerGraphRecords){receipt->result=ResultInvalidBlob;receipt->failureRecord=visited;return false;}
    if(output){
        if(visited>=outputCount){receipt->result=ResultCapacity;receipt->failureRecord=visited;return false;}
        output[visited]=record;
    }
    HashAppend(hash,&record,sizeof(record));
    ++visited;
    return true;
}

static bool SnapshotOwnerPlayable(uint32_t nodeKind,uint32_t layer,uint32_t stateMachine,
                                  uint32_t branch,void* playable,uintptr_t expectedVtable,
                                  bool captureClip,uintptr_t resolverOrigin,uint32_t resolverDepth,
                                  const OwnerProjection* projection,
                                  AnimatorOwnerGraphRecord* output,uint32_t outputCount,
                                  uint32_t& visited,uint32_t& hash,AnimatorOwnerGraphReceipt* receipt) {
    if(!Readable(playable,sizeof(uintptr_t))){receipt->result=ResultUnreadable;receipt->failureRecord=visited;return false;}
    const uintptr_t self=reinterpret_cast<uintptr_t>(playable);
    const uintptr_t vtable=*reinterpret_cast<const uintptr_t*>(self);
    if(expectedVtable&&vtable!=expectedVtable){receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;}
    // This Unity revision allocates an ordinary AnimationMixerPlayable as
    // exactly 0xA0 bytes. Its deleting destructor passes 0xA0 to operator
    // delete, and ConstructPlayable<AnimationMixerPlayable> passes 0xA0 to
    // operator new. Consequently +0xA0..+0xA7 are neighboring allocator data,
    // not playable state. Other observed playable classes are at least 0xA8
    // bytes; notably the outer state-machine MixerPlayable owns meaningful
    // lifecycle fields at +0xA0/+0xA4 and must remain strict.
    const uint32_t readablePlayableBytes=
        vtable==receipt->unityBase+kAnimationMixerPlayableVtableRva?0xA0u:0xA8u;
    if(!Readable(playable,readablePlayableBytes)){
        receipt->result=ResultUnreadable;receipt->failureRecord=visited;return false;
    }
    void* internal=*reinterpret_cast<void**>(self+0x10);
    if(!Readable(internal,0x30)){receipt->result=ResultUnreadable;receipt->failureRecord=visited;return false;}
    const uintptr_t internalAddress=reinterpret_cast<uintptr_t>(internal);
    void* inputEntries=*reinterpret_cast<void**>(internalAddress+0x10);
    const uint32_t inputCount=*reinterpret_cast<const uint32_t*>(internalAddress+0x18);
    const uint32_t inputCapacity=*reinterpret_cast<const uint32_t*>(internalAddress+0x1C);
    void* outputEntries=*reinterpret_cast<void**>(internalAddress+0x20);
    const uint32_t nativeOutputCount=*reinterpret_cast<const uint32_t*>(internalAddress+0x28);
    const uint32_t outputCapacity=*reinterpret_cast<const uint32_t*>(internalAddress+0x2C);
    if(inputCount>kMaximumMixerInputs||nativeOutputCount>kMaximumMixerInputs||
       inputCount>(inputCapacity&0x7FFFFFFFu)||nativeOutputCount>(outputCapacity&0x7FFFFFFFu)||
       (inputCount&&!Readable(inputEntries,inputCount*12))||
       (nativeOutputCount&&!Readable(outputEntries,nativeOutputCount*12))){
        receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
    }
    const uintptr_t graph=*reinterpret_cast<const uintptr_t*>(self+0x78);
    if(!graph||graph>UINTPTR_MAX-0x58||!Readable(reinterpret_cast<const void*>(graph+0x58),4)){
        receipt->result=ResultUnreadable;receipt->failureRecord=visited;return false;
    }
    if(!receipt->graph){
        receipt->graph=graph;
        receipt->graphDirty58=*reinterpret_cast<const uint32_t*>(graph+0x58);
    } else if(receipt->graph!=graph||receipt->graphDirty58!=*reinterpret_cast<const uint32_t*>(graph+0x58)){
        receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
    }
    AnimatorOwnerGraphRecord node={};
    node.recordKind=nodeKind;node.layerIndex=layer;node.stateMachineIndex=stateMachine;
    node.branchIndex=branch;node.inputIndex=0xFFFFFFFFu;
    node.self=self;node.vtable=vtable;node.internal=internalAddress;node.graph=graph;
    node.inputEntries=reinterpret_cast<uintptr_t>(inputEntries);node.inputCount=inputCount;
    node.inputCapacityRaw=inputCapacity;node.outputEntries=reinterpret_cast<uintptr_t>(outputEntries);
    node.outputCount=nativeOutputCount;node.outputCapacityRaw=outputCapacity;
    node.flags7C=*reinterpret_cast<const uint32_t*>(self+0x7C);
    node.dirty9093=*reinterpret_cast<const uint32_t*>(self+0x90);
    if(vtable!=receipt->unityBase+kAnimationMixerPlayableVtableRva){
        node.rawA0A3=*reinterpret_cast<const uint32_t*>(self+0xA0);
        node.rawA4A7=*reinterpret_cast<const uint32_t*>(self+0xA4);
    }
    node.word50=*reinterpret_cast<const uint32_t*>(self+0x50);
    node.currentTime28Low=*reinterpret_cast<const uint32_t*>(self+0x28);
    node.currentTime28High=*reinterpret_cast<const uint32_t*>(self+0x2C);
    node.previousTime30Low=*reinterpret_cast<const uint32_t*>(self+0x30);
    node.previousTime30High=*reinterpret_cast<const uint32_t*>(self+0x34);
    node.duration38Low=*reinterpret_cast<const uint32_t*>(self+0x38);
    node.duration38High=*reinterpret_cast<const uint32_t*>(self+0x3C);
    node.mode80=*reinterpret_cast<const uint32_t*>(self+0x80);
    node.raw9497=*reinterpret_cast<const uint32_t*>(self+0x94);
    if(captureClip){
        if(vtable!=receipt->unityBase+kAnimationClipPlayableVtableRva||!Readable(playable,0x110)){
            receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
        }
        node.clip108=*reinterpret_cast<const uintptr_t*>(self+0x108);
        node.bindingA8=*reinterpret_cast<const uintptr_t*>(self+0xA8);
        node.bindingAC=*reinterpret_cast<const uintptr_t*>(self+0xAC);
        node.bindingB0=*reinterpret_cast<const uintptr_t*>(self+0xB0);
        node.bindingB4=*reinterpret_cast<const uintptr_t*>(self+0xB4);
        node.clipCacheBC=*reinterpret_cast<const uint8_t*>(self+0xBC);
        node.clipCacheC0=*reinterpret_cast<const uint32_t*>(self+0xC0);
        node.clipInternalWeightBits=*reinterpret_cast<const uint32_t*>(self+0xC4);
        node.clipFlags10C=*reinterpret_cast<const uint32_t*>(self+0x10C);
        if(!CaptureClipBindingCaches(node,visited,receipt))return false;
    }
    node.resolverOrigin=resolverOrigin;node.resolverDepth=resolverDepth;
    ProjectOwnerRecord(node,projection);
    if(!EmitOwnerRecord(node,output,outputCount,visited,hash,receipt))return false;
    const uint8_t* inputs=static_cast<const uint8_t*>(inputEntries);
    for(uint32_t index=0;index<inputCount;++index){
        AnimatorOwnerGraphRecord entry=node;
        entry.recordKind=nodeKind+1;entry.inputIndex=index;
        entry.entryAddress=reinterpret_cast<uintptr_t>(inputs+index*12);
        entry.entryWeightBits=*reinterpret_cast<const uint32_t*>(inputs+index*12);
        entry.entryPlayable=*reinterpret_cast<const uintptr_t*>(inputs+index*12+4);
        entry.entryPortRaw=*reinterpret_cast<const uint32_t*>(inputs+index*12+8);
        ProjectOwnerRecord(entry,projection);
        if(!EmitOwnerRecord(entry,output,outputCount,visited,hash,receipt))return false;
    }
    const uint8_t* outputs=static_cast<const uint8_t*>(outputEntries);
    for(uint32_t index=0;index<nativeOutputCount;++index){
        AnimatorOwnerGraphRecord entry=node;
        entry.recordKind=nodeKind+2;entry.inputIndex=index;
        entry.entryAddress=reinterpret_cast<uintptr_t>(outputs+index*12);
        entry.entryWeightBits=*reinterpret_cast<const uint32_t*>(outputs+index*12);
        entry.entryPlayable=*reinterpret_cast<const uintptr_t*>(outputs+index*12+4);
        entry.entryPortRaw=*reinterpret_cast<const uint32_t*>(outputs+index*12+8);
        ProjectOwnerRecord(entry,projection);
        if(!EmitOwnerRecord(entry,output,outputCount,visited,hash,receipt))return false;
    }
    return true;
}

static bool SnapshotSetClipResolver(uint32_t layer,uint32_t stateMachine,uint32_t branch,
                                    void* origin,AnimatorOwnerGraphRecord* output,uint32_t outputCount,
                                    const OwnerProjection* projection,
                                    uint32_t& visited,uint32_t& hash,AnimatorOwnerGraphReceipt* receipt) {
    uintptr_t seen[kMaximumResolverDepth]={};
    uintptr_t current=reinterpret_cast<uintptr_t>(origin);
    const uintptr_t originAddress=current;
    for(uint32_t depth=0;depth<kMaximumResolverDepth;++depth){
        if(!current){receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;}
        for(uint32_t i=0;i<depth;++i)if(seen[i]==current){
            receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
        }
        seen[depth]=current;
        static const uint32_t expectedVtables[]={
            kAnimationClipPlayableVtableRva,kAnimationMixerPlayableVtableRva,kMixerPlayableVtableRva,
            kAnimationLayerMixerPlayableVtableRva,kAnimatorControllerPlayableVtableRva
        };
        if(depth>=sizeof(expectedVtables)/sizeof(expectedVtables[0])){
            receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
        }
        if(!SnapshotOwnerPlayable(OwnerResolverNode,layer,stateMachine,branch,
                                  reinterpret_cast<void*>(current),receipt->unityBase+expectedVtables[depth],false,originAddress,depth,
                                  projection,
                                  output,outputCount,visited,hash,receipt))return false;
        void* internal=*reinterpret_cast<void**>(current+0x10);
        const uintptr_t internalAddress=reinterpret_cast<uintptr_t>(internal);
        const uint32_t nativeOutputCount=*reinterpret_cast<const uint32_t*>(internalAddress+0x28);
        const uintptr_t outputEntries=*reinterpret_cast<const uintptr_t*>(internalAddress+0x20);
        // Unity's resolver reads output[0].playable even when outputCount is
        // zero. Require that exact sentinel record to be readable as part of
        // the preflight rather than speculating about absent storage.
        AnimatorOwnerGraphRecord decision={};
        decision.recordKind=OwnerResolverDecision;decision.layerIndex=layer;
        decision.stateMachineIndex=stateMachine;decision.branchIndex=branch;
        decision.inputIndex=depth;decision.self=current;
        decision.graph=receipt->graph;decision.outputEntries=outputEntries;
        decision.outputCount=nativeOutputCount;decision.word50=*reinterpret_cast<const uint32_t*>(current+0x50);
        decision.resolverOrigin=originAddress;decision.resolverDepth=depth;
        if(nativeOutputCount>1){
            decision.resolverStatus=5u;decision.resolverResult=0;
            ProjectOwnerRecord(decision,projection);
            if(!EmitOwnerRecord(decision,output,outputCount,visited,hash,receipt))return false;
            return true;
        }
        if(!Readable(reinterpret_cast<const void*>(outputEntries),12)){
            receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
        }
        const uintptr_t next=*reinterpret_cast<const uintptr_t*>(outputEntries+4);
        decision.entryAddress=outputEntries;decision.entryWeightBits=*reinterpret_cast<const uint32_t*>(outputEntries);
        decision.entryPlayable=next;decision.entryPortRaw=*reinterpret_cast<const uint32_t*>(outputEntries+8);
        if(!next){
            decision.resolverStatus=decision.word50==0?1u:2u;
            decision.resolverResult=decision.word50==0?current:0;
        } else if(next==2){
            decision.resolverStatus=3u;decision.resolverResult=0;
        } else {
            if(next>UINTPTR_MAX-0x50||!Readable(reinterpret_cast<const void*>(next+0x50),4)){
                receipt->result=ResultUnreadable;receipt->failureRecord=visited;return false;
            }
            const uint32_t nextWord50=*reinterpret_cast<const uint32_t*>(next+0x50);
            if(nextWord50!=0){
                decision.resolverStatus=4u;decision.resolverResult=current;
                AnimatorOwnerGraphRecord terminal={};
                terminal.recordKind=OwnerResolverTerminal;terminal.layerIndex=layer;
                terminal.stateMachineIndex=stateMachine;terminal.branchIndex=branch;
                terminal.inputIndex=depth+1;terminal.self=next;terminal.word50=nextWord50;
                terminal.resolverOrigin=originAddress;terminal.resolverDepth=depth+1;
                terminal.resolverStatus=4u;terminal.resolverResult=current;
                ProjectOwnerRecord(terminal,projection);
                if(!EmitOwnerRecord(terminal,output,outputCount,visited,hash,receipt))return false;
            }
        }
        ProjectOwnerRecord(decision,projection);
        if(!EmitOwnerRecord(decision,output,outputCount,visited,hash,receipt))return false;
        if(decision.resolverStatus)return true;
        current=next;
    }
    receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
}

static bool WalkOwnerGraphCore(uintptr_t unityBase,void* descriptors,uint32_t layerCount,
                               const OwnerProjection* projection,
                               AnimatorOwnerGraphRecord* output,uint32_t outputCount,
                               uint32_t& visited,uint32_t& hash,AnimatorOwnerGraphReceipt* receipt) {
    visited=0;hash=2166136261u;
    const uint8_t* descriptorBytes=static_cast<const uint8_t*>(descriptors);
    for(uint32_t layer=0;layer<layerCount;++layer){
        const uint8_t* descriptor=descriptorBytes+layer*8;
        void** stateMachines=*reinterpret_cast<void***>(const_cast<uint8_t*>(descriptor));
        const uint32_t stateMachineCount=*reinterpret_cast<const uint32_t*>(descriptor+4);
        if(stateMachineCount>kMaximumStateMachinesPerLayer||
           (stateMachineCount&&!Readable(stateMachines,stateMachineCount*4))){
            receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
        }
        for(uint32_t stateMachine=0;stateMachine<stateMachineCount;++stateMachine){
            void* outer=stateMachines[stateMachine];
            if(!SnapshotOwnerPlayable(OwnerOuterNode,layer,stateMachine,0xFFFFFFFFu,outer,
                                      unityBase+kMixerPlayableVtableRva,false,0,0,
                                      projection,
                                      output,outputCount,visited,hash,receipt))return false;
            void* outerInternal=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(outer)+0x10);
            const uintptr_t outerEntries=*reinterpret_cast<const uintptr_t*>(reinterpret_cast<uintptr_t>(outerInternal)+0x10);
            const uint32_t outerCount=*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(outerInternal)+0x18);
            if(outerCount!=3){receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;}
            const OwnerRotationPlan* rotation=FindOwnerRotation(projection,layer,stateMachine);
            for(uint32_t branch=0;branch<3;++branch){
                uint32_t liveBranch=branch;
                if(rotation&&branch<2)liveBranch=1u-branch;
                void* mixer=*reinterpret_cast<void* const*>(outerEntries+liveBranch*12+4);
                if(!mixer){receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;}
                const uintptr_t branchVtable=Readable(mixer,4)?*reinterpret_cast<const uintptr_t*>(mixer):0;
                if((branch<2&&branchVtable!=unityBase+kAnimationMixerPlayableVtableRva)||
                   (branch==2&&branchVtable!=unityBase+kAnimationMixerPlayableVtableRva&&
                    branchVtable!=unityBase+kAnimationPosePlayableVtableRva)){
                    receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
                }
                if(!SnapshotOwnerPlayable(OwnerBranchNode,layer,stateMachine,branch,mixer,
                                          branchVtable,false,0,0,
                                          projection,
                                          output,outputCount,visited,hash,receipt))return false;
                if(branchVtable==unityBase+kAnimationPosePlayableVtableRva)continue;
                void* mixerInternal=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(mixer)+0x10);
                const uintptr_t mixerEntries=*reinterpret_cast<const uintptr_t*>(reinterpret_cast<uintptr_t>(mixerInternal)+0x10);
                const uint32_t mixerCount=*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(mixerInternal)+0x18);
                for(uint32_t input=0;input<mixerCount;++input){
                    void* child=*reinterpret_cast<void* const*>(mixerEntries+input*12+4);
                    if(!child){receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;}
                    const uintptr_t vtable=Readable(child,4)?*reinterpret_cast<const uintptr_t*>(child):0;
                    const bool isClip=vtable==unityBase+kAnimationClipPlayableVtableRva;
                    const bool endTransitionTouched=branch==0&&input+1<mixerCount;
                    if(endTransitionTouched&&!isClip){
                        receipt->result=ResultTopologyMismatch;receipt->failureRecord=visited;return false;
                    }
                    if(!SnapshotOwnerPlayable(OwnerChildNode,layer,stateMachine,branch,child,
                                              0,isClip,0,0,projection,
                                              output,outputCount,visited,hash,receipt))return false;
                    const OwnerClearedClip* cleared=FindOwnerClearedClip(projection,reinterpret_cast<uintptr_t>(child));
                    const uintptr_t clip=isClip&&!cleared?
                        *reinterpret_cast<const uintptr_t*>(reinterpret_cast<uintptr_t>(child)+0x108):0;
                    if(endTransitionTouched&&clip!=0&&
                       !SnapshotSetClipResolver(layer,stateMachine,branch,child,
                                                output,outputCount,projection,visited,hash,receipt))return false;
                }
            }
        }
    }
    return true;
}

static bool WalkOwnerGraph(uintptr_t unityBase,void* descriptors,uint32_t layerCount,
                           AnimatorOwnerGraphRecord* output,uint32_t outputCount,
                           uint32_t& visited,uint32_t& hash,AnimatorOwnerGraphReceipt* receipt) {
    return WalkOwnerGraphCore(unityBase,descriptors,layerCount,0,output,outputCount,visited,hash,receipt);
}

static bool ResolveOwnerGraph(uintptr_t unityBase,uintptr_t animator,AnimatorOwnerGraphReceipt* receipt,
                              void*& controller,void*& constant,void*& descriptors) {
    AnimatorMixerGraphReceipt mixer={};
    InitializeMixerGraphReceipt(&mixer,unityBase,animator);
    if(!VerifyMixerRevision(unityBase,&mixer)){
        receipt->result=mixer.result;receipt->lastError=mixer.lastError;return false;
    }
    if(!VerifyOwnerRevision(unityBase,receipt))return false;
    if(!ResolveMixerGraph(unityBase,animator,&mixer,controller,constant,descriptors)){
        receipt->result=mixer.result;receipt->lastError=mixer.lastError;return false;
    }
    receipt->controller=reinterpret_cast<uintptr_t>(controller);
    receipt->controllerConstant=reinterpret_cast<uintptr_t>(constant);
    receipt->descriptors=reinterpret_cast<uintptr_t>(descriptors);
    receipt->layerCount=mixer.layerCount;
    return true;
}

static bool CaptureNormalizationTopology(void* controller,void* memory,
                                         AnimatorTransitionTopologyLayer* rows,uint32_t capacity,
                                         uint32_t& count,uint32_t& hash,
                                         AnimatorEndTransitionReceipt* receipt) {
    count=0;hash=0;
    if(!Readable(controller,0xEC)){receipt->result=ResultUnreadable;return false;}
    const uint32_t blobCapacity=*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0xBC);
    if(blobCapacity<0x69||blobCapacity>kMaximumBlobSize||!Readable(memory,blobCapacity)){
        receipt->result=ResultInvalidBlob;return false;
    }
    const uint32_t blobLayerCount=*reinterpret_cast<const uint32_t*>(memory);
    void* graphMemory=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xB8);
    void* liveLayers=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xE8);
    if(!Readable(graphMemory,0x10)||!Readable(liveLayers,8)){
        receipt->result=ResultUnreadable;return false;
    }
    const uint32_t graphLayerCount=*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(graphMemory)+0x0C);
    const int32_t signedCount=*reinterpret_cast<const int32_t*>(reinterpret_cast<uintptr_t>(liveLayers)+4);
    if(signedCount<0){receipt->result=ResultInvalidBlob;return false;}
    count=static_cast<uint32_t>(signedCount);
    if(count>capacity||count>kMaximumTopologyLayers||blobLayerCount!=graphLayerCount||count>blobLayerCount){
        receipt->result=ResultInvalidBlob;return false;
    }
    void** nodes=*reinterpret_cast<void***>(reinterpret_cast<uintptr_t>(graphMemory)+4);
    void** lives=*reinterpret_cast<void***>(liveLayers);
    if(count&&(!Readable(nodes,count*4u)||!Readable(lives,count*4u))){
        receipt->result=ResultUnreadable;return false;
    }
    const uint8_t* rootAnchor=static_cast<const uint8_t*>(memory)+4;
    const uint8_t* entries=0;
    if(!Within(memory,blobCapacity,rootAnchor,4)||
       !RelativeWithin(memory,blobCapacity,rootAnchor,*reinterpret_cast<const int32_t*>(rootAnchor),count*4u,entries)){
        receipt->result=ResultInvalidBlob;return false;
    }
    for(uint32_t i=0;i<count;++i){
        const uint8_t* entry=entries+i*4u;
        const uint8_t* layer=0;
        if(!RelativeWithin(memory,blobCapacity,entry,*reinterpret_cast<const int32_t*>(entry),0x69,layer)){
            receipt->result=ResultInvalidBlob;receipt->failureRecord=i;return false;
        }
        void* node=nodes[i];void* live=lives[i];
        if(!Readable(node,0x14)||!Readable(live,0xA8)||
           *reinterpret_cast<const uintptr_t*>(live)!=receipt->unityBase+kMixerPlayableVtableRva){
            receipt->result=ResultTopologyMismatch;receipt->failureRecord=i;return false;
        }
        const uint32_t mode=*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(live)+0xA0);
        if(mode>2){receipt->result=ResultInvalidBlob;receipt->failureRecord=i;return false;}
        AnimatorTransitionTopologyLayer row={};
        row.index=i;row.live=reinterpret_cast<uintptr_t>(live);row.node=reinterpret_cast<uintptr_t>(node);
        row.blobInterruptedRaw=layer[0x68];
        row.nodeStartArgumentRaw=*reinterpret_cast<const uint8_t*>(reinterpret_cast<uintptr_t>(node)+0x10);
        row.liveModeRaw=mode;
        row.liveSecondaryArgumentRaw=*reinterpret_cast<const uint8_t*>(reinterpret_cast<uintptr_t>(live)+0xA4);
        rows[i]=row;
    }
    hash=Hash(rows,count*sizeof(AnimatorTransitionTopologyLayer));
    return true;
}

static bool CaptureOwnerAllocated(uintptr_t unityBase,void* descriptors,uint32_t layerCount,
                                  const OwnerProjection* projection,AnimatorOwnerGraphRecord*& records,
                                  uint32_t& count,uint32_t& hash,AnimatorOwnerGraphReceipt* ownerReceipt) {
    records=0;count=0;hash=0;
    uint32_t firstCount=0,firstHash=0;
    if(!WalkOwnerGraphCore(unityBase,descriptors,layerCount,projection,0,0,
                           firstCount,firstHash,ownerReceipt))return false;
    if(firstCount==0||firstCount>kMaximumOwnerGraphRecords){
        ownerReceipt->result=ResultInvalidBlob;return false;
    }
    const uint32_t bytes=firstCount*sizeof(AnimatorOwnerGraphRecord);
    records=static_cast<AnimatorOwnerGraphRecord*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,bytes));
    if(!records){ownerReceipt->lastError=GetLastError();ownerReceipt->result=ResultFault;return false;}
    uint32_t secondCount=0,secondHash=0;
    if(!WalkOwnerGraphCore(unityBase,descriptors,layerCount,projection,records,firstCount,
                           secondCount,secondHash,ownerReceipt)||
       secondCount!=firstCount||secondHash!=firstHash){
        if(ownerReceipt->result==0||ownerReceipt->result==ResultOk)ownerReceipt->result=ResultTopologyMismatch;
        HeapFree(GetProcessHeap(),0,records);records=0;return false;
    }
    count=secondCount;hash=secondHash;return true;
}

static bool IsOwnerNodeRecord(uint32_t kind) {
    return kind==OwnerOuterNode||kind==OwnerBranchNode||kind==OwnerChildNode||kind==OwnerResolverNode;
}

static bool CarriesOwnerNodeState(uint32_t kind) {
    return kind!=OwnerResolverDecision&&kind!=OwnerResolverTerminal;
}

static bool IsCapturedClipRecord(const AnimatorOwnerGraphRecord& record,uintptr_t unityBase) {
    return record.vtable==unityBase+kAnimationClipPlayableVtableRva&&
           (record.recordKind==OwnerChildNode||record.recordKind==OwnerChildInput||
            record.recordKind==OwnerChildOutput);
}

static uint64_t OwnerDoubleBits(uint32_t low,uint32_t high) {
    return static_cast<uint64_t>(low)|(static_cast<uint64_t>(high)<<32);
}

static double OwnerDouble(uint32_t low,uint32_t high) {
    union { uint64_t bits; double value; } decoded={OwnerDoubleBits(low,high)};
    return decoded.value;
}

static uint32_t OwnerFloatBits(double value) {
    union { float value; uint32_t bits; } converted={static_cast<float>(value)};
    return converted.bits;
}

static bool FiniteOwnerDouble(uint32_t low,uint32_t high) {
    const uint64_t bits=OwnerDoubleBits(low,high);
    return (bits&0x7FF0000000000000ull)!=0x7FF0000000000000ull;
}

static bool SameTemporalDefinition(const AnimatorOwnerGraphRecord& left,
                                   const AnimatorOwnerGraphRecord& right,
                                   uintptr_t unityBase) {
    if(left.self!=right.self||left.vtable!=right.vtable||left.internal!=right.internal||left.graph!=right.graph||
       left.flags7C!=right.flags7C||left.currentTime28Low!=right.currentTime28Low||
       left.currentTime28High!=right.currentTime28High||left.previousTime30Low!=right.previousTime30Low||
       left.previousTime30High!=right.previousTime30High||left.duration38Low!=right.duration38Low||
       left.duration38High!=right.duration38High||left.mode80!=right.mode80)return false;
    if(IsCapturedClipRecord(left,unityBase)&&IsCapturedClipRecord(right,unityBase)&&
       (left.clipCacheC0!=right.clipCacheC0||
        left.clipInternalWeightBits!=right.clipInternalWeightBits))return false;
    return true;
}

static const AnimatorOwnerGraphRecord* FindCanonicalTimeNode(
    const AnimatorOwnerGraphRecord* records,uint32_t count,uintptr_t self,uint32_t& recordIndex) {
    const AnimatorOwnerGraphRecord* found=0;recordIndex=0xFFFFFFFFu;
    for(uint32_t i=0;i<count;++i){
        if(!IsOwnerNodeRecord(records[i].recordKind)||records[i].self!=self)continue;
        if(!found||records[i].recordKind==OwnerChildNode){found=records+i;recordIndex=i;}
    }
    return found;
}

static uint32_t CountUniqueTimeNodes(const AnimatorOwnerGraphRecord* records,uint32_t count) {
    uint32_t unique=0;
    for(uint32_t i=0;i<count;++i){
        if(!IsOwnerNodeRecord(records[i].recordKind))continue;
        bool seen=false;
        for(uint32_t j=0;j<i;++j)
            if(IsOwnerNodeRecord(records[j].recordKind)&&records[j].self==records[i].self){seen=true;break;}
        if(!seen)++unique;
    }
    return unique;
}

static void SetPlayableTimeFailure(AnimatorPlayableTimeReceipt* receipt,uint32_t record,
                                   uint32_t targetRecord,uint32_t byteOffset,
                                   uint32_t expected,uint32_t actual,uintptr_t node) {
    receipt->failureRecord=record;receipt->failureTargetRecord=targetRecord;
    receipt->failureByteOffset=byteOffset;receipt->expectedWord=expected;
    receipt->actualWord=actual;receipt->failureNode=node;
}

static bool VerifyPlayableTimeRevision(uintptr_t unityBase,AnimatorPlayableTimeReceipt* receipt) {
    struct HashedSpan { uint32_t rva;uint32_t size;uint32_t hash; };
    // These spans cover the complete four functions used by the recipes.
    // OnAdvanceTime contains four loader-relocated DWORDs, so hash every
    // relocation-free span and verify each relocated target explicitly.
    static const HashedSpan spans[]={
        {kPlayableSetTimeRva,0x75u,0xFDDBD2BBu},
        {kPlayableAdvanceTimeRva+0x00u,0x50u,0x49DFEF3Du},
        {kPlayableAdvanceTimeRva+0x54u,0x1Au,0xB943AC7Eu},
        {kPlayableAdvanceTimeRva+0x72u,0x5Au,0x5A394116u},
        {kPlayableAdvanceTimeRva+0xD0u,0x07u,0x96256DC6u},
        {kPlayableAdvanceTimeRva+0xDBu,0xA4u,0xB07F0A26u},
        {kAnimationClipSetTimeRva,0x2Au,0x3E553D36u},
        {kAnimationClipAdvanceTimeRva,0x33u,0x3A40FC16u},
        {kAnimationClipSetInternalWeightRva,0x14u,0xD7262A81u}
    };
    for(uint32_t i=0;i<sizeof(spans)/sizeof(spans[0]);++i){
        const uintptr_t address=unityBase+spans[i].rva;
        if(address<unityBase||!Readable(reinterpret_cast<const void*>(address),spans[i].size)){
            receipt->result=ResultUnreadable;return false;
        }
        if(Hash(reinterpret_cast<const void*>(address),spans[i].size)!=spans[i].hash){
            receipt->result=ResultRevisionMismatch;return false;
        }
    }
    struct Relocation { uint32_t offset;uint32_t targetRva; };
    static const Relocation relocations[]={
        {0x50u,0xE29DA8u},{0x6Eu,0xDF4270u},{0xCCu,0xE11130u},{0xD7u,0xDF4270u}
    };
    for(uint32_t i=0;i<sizeof(relocations)/sizeof(relocations[0]);++i){
        const uintptr_t address=unityBase+kPlayableAdvanceTimeRva+relocations[i].offset;
        if(address<unityBase||!Readable(reinterpret_cast<const void*>(address),sizeof(uintptr_t))){
            receipt->result=ResultUnreadable;return false;
        }
        if(*reinterpret_cast<const uintptr_t*>(address)!=unityBase+relocations[i].targetRva){
            receipt->result=ResultRevisionMismatch;return false;
        }
    }
    static const uint32_t baseVtables[]={
        kMixerPlayableVtableRva,kAnimationMixerPlayableVtableRva,kAnimationPosePlayableVtableRva,
        kAnimationLayerMixerPlayableVtableRva,kAnimatorControllerPlayableVtableRva
    };
    for(uint32_t i=0;i<sizeof(baseVtables)/sizeof(baseVtables[0]);++i){
        const uintptr_t table=unityBase+baseVtables[i];
        if(!Readable(reinterpret_cast<const void*>(table),0x48)){
            receipt->result=ResultUnreadable;return false;
        }
        if(*reinterpret_cast<const uintptr_t*>(table+0x20)!=unityBase+kPlayableSetTimeRva||
           *reinterpret_cast<const uintptr_t*>(table+0x44)!=unityBase+kPlayableAdvanceTimeRva){
            receipt->result=ResultRevisionMismatch;return false;
        }
    }
    const uintptr_t clip=unityBase+kAnimationClipPlayableVtableRva;
    if(!Readable(reinterpret_cast<const void*>(clip),0x94)){
        receipt->result=ResultUnreadable;return false;
    }
    if(*reinterpret_cast<const uintptr_t*>(clip+0x20)!=unityBase+kAnimationClipSetTimeRva||
       *reinterpret_cast<const uintptr_t*>(clip+0x44)!=unityBase+kAnimationClipAdvanceTimeRva||
       *reinterpret_cast<const uintptr_t*>(clip+0x90)!=unityBase+kAnimationClipSetInternalWeightRva||
       !EqualBytes(reinterpret_cast<const void*>(unityBase+kAnimationClipSetInternalWeightRva),
                   kAnimationClipSetInternalWeightBytes,
                   sizeof(kAnimationClipSetInternalWeightBytes))){
        receipt->result=ResultRevisionMismatch;return false;
    }
    return true;
}

static bool ValidateTimeNode(const AnimatorOwnerGraphRecord& current,uint32_t currentIndex,
                             const AnimatorOwnerGraphRecord& target,uint32_t targetIndex,
                             uintptr_t unityBase,AnimatorPlayableTimeReceipt* receipt) {
    static const uint64_t maximumDuration=0x7FEFFFFFFFFFFFFFull;
    if(!current.self||current.self!=target.self){
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,20,
                               static_cast<uint32_t>(target.self),static_cast<uint32_t>(current.self),current.self);
        receipt->result=ResultTopologyMismatch;return false;
    }
    if(current.vtable!=target.vtable){
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,24,
                               static_cast<uint32_t>(target.vtable),static_cast<uint32_t>(current.vtable),current.self);
        receipt->result=ResultTopologyMismatch;return false;
    }
    if(current.internal!=target.internal){
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,28,
                               static_cast<uint32_t>(target.internal),static_cast<uint32_t>(current.internal),current.self);
        receipt->result=ResultTopologyMismatch;return false;
    }
    if(current.graph!=target.graph){
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,32,
                               static_cast<uint32_t>(target.graph),static_cast<uint32_t>(current.graph),current.self);
        receipt->result=ResultTopologyMismatch;return false;
    }
    const uintptr_t vtable=current.vtable;
    const bool knownBase=vtable==unityBase+kMixerPlayableVtableRva||
        vtable==unityBase+kAnimationMixerPlayableVtableRva||
        vtable==unityBase+kAnimationPosePlayableVtableRva||
        vtable==unityBase+kAnimationLayerMixerPlayableVtableRva||
        vtable==unityBase+kAnimatorControllerPlayableVtableRva;
    const bool clip=vtable==unityBase+kAnimationClipPlayableVtableRva;
    if(!knownBase&&!clip){
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,24,0,static_cast<uint32_t>(vtable),current.self);
        receipt->result=ResultTopologyMismatch;return false;
    }
    if(!Readable(reinterpret_cast<const void*>(current.self),clip?0x110u:0x84u)||
       !Writable(reinterpret_cast<const void*>(current.self+0x28),0x10u)||
       !Writable(reinterpret_cast<const void*>(current.self+0x7C),4u)||
       (clip&&!Writable(reinterpret_cast<const void*>(current.self+0xC0),8u))){
        receipt->result=ResultUnreadable;SetPlayableTimeFailure(receipt,currentIndex,targetIndex,116,0,0,current.self);return false;
    }
    if(current.duration38Low!=target.duration38Low){
        receipt->result=ResultTopologyMismatch;SetPlayableTimeFailure(receipt,currentIndex,targetIndex,132,
            target.duration38Low,current.duration38Low,current.self);return false;
    }
    if(current.duration38High!=target.duration38High){
        receipt->result=ResultTopologyMismatch;SetPlayableTimeFailure(receipt,currentIndex,targetIndex,136,
            target.duration38High,current.duration38High,current.self);return false;
    }
    if(current.mode80!=target.mode80||target.mode80!=2u){
        receipt->result=ResultTopologyMismatch;SetPlayableTimeFailure(receipt,currentIndex,targetIndex,140,
            target.mode80==2u?target.mode80:2u,current.mode80,current.self);return false;
    }
    if(OwnerDoubleBits(target.duration38Low,target.duration38High)!=maximumDuration){
        receipt->result=ResultInvalidBlob;SetPlayableTimeFailure(receipt,currentIndex,targetIndex,132,
            static_cast<uint32_t>(maximumDuration),target.duration38Low,current.self);return false;
    }
    // No child propagation and no loop/end marker reconstruction in this
    // first supported native lifecycle.  The four seek/evaluated-marker
    // combinations themselves are handled below.
    const uint32_t unsupported=0x324u;
    // Current 0x100/0x200 are cleared by the mandatory first Advance(0).
    // Current 0x20 is not cleared by every recipe, so reject it before any
    // mutation instead of discovering that unsupported state afterward.
    if((target.flags7C&unsupported)!=0||
       (current.flags7C&~0x342u)!=(target.flags7C&~0x342u)){
        receipt->result=ResultTopologyMismatch;
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,60,target.flags7C,current.flags7C,current.self);return false;
    }
    if(!FiniteOwnerDouble(target.currentTime28Low,target.currentTime28High)||
       !FiniteOwnerDouble(target.previousTime30Low,target.previousTime30High)){
        receipt->result=ResultInvalidBlob;SetPlayableTimeFailure(receipt,currentIndex,targetIndex,116,0,0,current.self);return false;
    }
    const double previous=OwnerDouble(target.previousTime30Low,target.previousTime30High);
    const double desired=OwnerDouble(target.currentTime28Low,target.currentTime28High);
    const uint32_t markers=target.flags7C&0x42u;
    if(markers==0x40u){
        volatile double delta=desired-previous;
        volatile double replayed=previous+delta;
        union { double value; uint64_t bits; } result={replayed};
        if(delta<0.0||result.bits!=OwnerDoubleBits(target.currentTime28Low,target.currentTime28High)){
            receipt->result=ResultInvalidBlob;
            SetPlayableTimeFailure(receipt,currentIndex,targetIndex,116,target.currentTime28Low,
                                   static_cast<uint32_t>(result.bits),current.self);return false;
        }
    }
    if(clip&&target.clipCacheC0!=OwnerFloatBits(previous)){
        receipt->result=ResultTopologyMismatch;
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,180,target.clipCacheC0,
                               OwnerFloatBits(previous),current.self);return false;
    }
    if(clip&&(!ValidWeight(current.clipInternalWeightBits)||
             !ValidWeight(target.clipInternalWeightBits))){
        receipt->result=ResultTopologyMismatch;
        SetPlayableTimeFailure(receipt,currentIndex,targetIndex,172,
                               target.clipInternalWeightBits,
                               current.clipInternalWeightBits,current.self);return false;
    }
    return true;
}

static bool PlayableClockExact(const AnimatorOwnerGraphRecord& current,
                               const AnimatorOwnerGraphRecord& target,uintptr_t unityBase) {
    return current.flags7C==target.flags7C&&
           current.currentTime28Low==target.currentTime28Low&&current.currentTime28High==target.currentTime28High&&
           current.previousTime30Low==target.previousTime30Low&&current.previousTime30High==target.previousTime30High&&
           (!IsCapturedClipRecord(target,unityBase)||current.clipCacheC0==target.clipCacheC0);
}

static bool TemporalNodeExact(const AnimatorOwnerGraphRecord& current,
                              const AnimatorOwnerGraphRecord& target,uintptr_t unityBase) {
    return PlayableClockExact(current,target,unityBase)&&
           (!IsCapturedClipRecord(target,unityBase)||
            current.clipInternalWeightBits==target.clipInternalWeightBits);
}

static void ProjectTemporalNode(AnimatorOwnerGraphRecord& record,
                                const AnimatorOwnerGraphRecord& target,uintptr_t unityBase) {
    record.flags7C=target.flags7C;
    record.currentTime28Low=target.currentTime28Low;record.currentTime28High=target.currentTime28High;
    record.previousTime30Low=target.previousTime30Low;record.previousTime30High=target.previousTime30High;
    if(IsCapturedClipRecord(record,unityBase)){
        record.clipCacheC0=target.clipCacheC0;
        record.clipInternalWeightBits=target.clipInternalWeightBits;
    }
}

static bool ComparePlayableTimeBytes(const AnimatorOwnerGraphRecord* expected,
                                     const AnimatorOwnerGraphRecord* actual,uint32_t count,
                                     AnimatorPlayableTimeReceipt* receipt) {
    const uint8_t* left=reinterpret_cast<const uint8_t*>(expected);
    const uint8_t* right=reinterpret_cast<const uint8_t*>(actual);
    const uint32_t bytes=count*sizeof(AnimatorOwnerGraphRecord);
    for(uint32_t i=0;i<bytes;++i)if(left[i]!=right[i]){
        const uint32_t record=i/sizeof(AnimatorOwnerGraphRecord);
        const uint32_t offset=i%sizeof(AnimatorOwnerGraphRecord);
        const uint32_t word=offset&~3u;
        SetPlayableTimeFailure(receipt,record,0xFFFFFFFFu,offset,
            *reinterpret_cast<const uint32_t*>(left+record*sizeof(AnimatorOwnerGraphRecord)+word),
            *reinterpret_cast<const uint32_t*>(right+record*sizeof(AnimatorOwnerGraphRecord)+word),
            actual[record].self);
        return false;
    }
    return true;
}

static bool CaptureControllerMemoryDigest(uintptr_t unityBase,void* memory,void* allocator,
                                          uint32_t& size,uint32_t& hash,
                                          AnimatorOverrideClipReceipt* receipt) {
    size=0;hash=0;
    CopyControllerMemory copy=reinterpret_cast<CopyControllerMemory>(unityBase+kCopyControllerMemoryRva);
    void* blob=copy(memory,allocator,&size);
    if(!blob||size<0x20||size>kMaximumBlobSize){
        receipt->result=ResultInvalidBlob;Release(allocator,blob);return false;
    }
    hash=Hash(blob,size);Release(allocator,blob);return true;
}

static bool CaptureOwnerDigest(uintptr_t unityBase,void* controller,void* constant,
                               void* descriptors,uint32_t layerCount,
                               uint32_t& count,uint32_t& hash,
                               uintptr_t& graph,uint32_t& graphDirty,
                               AnimatorOverrideClipReceipt* receipt) {
    AnimatorOwnerGraphReceipt owner={};
    InitializeOwnerGraphReceipt(&owner,unityBase,receipt->animator);
    owner.controller=reinterpret_cast<uintptr_t>(controller);
    owner.controllerConstant=reinterpret_cast<uintptr_t>(constant);
    owner.descriptors=reinterpret_cast<uintptr_t>(descriptors);
    owner.layerCount=layerCount;
    AnimatorOwnerGraphRecord* records=0;
    if(!CaptureOwnerAllocated(unityBase,descriptors,layerCount,0,records,count,hash,&owner)){
        receipt->result=owner.result;receipt->lastError=owner.lastError;return false;
    }
    graph=owner.graph;graphDirty=owner.graphDirty58;
    HeapFree(GetProcessHeap(),0,records);return true;
}

static const AnimatorOwnerGraphRecord* FindOwnerRecord(const AnimatorOwnerGraphRecord* records,uint32_t count,
                                                       uint32_t kind,uint32_t layer,uint32_t stateMachine,
                                                       uint32_t branch,uint32_t input,bool& duplicate) {
    const AnimatorOwnerGraphRecord* found=0;duplicate=false;
    for(uint32_t i=0;i<count;++i){
        const AnimatorOwnerGraphRecord& record=records[i];
        if(record.recordKind==kind&&record.layerIndex==layer&&record.stateMachineIndex==stateMachine&&
           record.branchIndex==branch&&record.inputIndex==input){
            if(found){duplicate=true;return 0;}found=records+i;
        }
    }
    return found;
}

static const AnimatorOwnerGraphRecord* FindUniqueOwnerRecord(
    const AnimatorOwnerGraphRecord* records,uint32_t count,uint32_t kind,
    uint32_t layer,uint32_t stateMachine,uint32_t branch,uint32_t input) {
    bool duplicate=false;
    const AnimatorOwnerGraphRecord* found=FindOwnerRecord(records,count,kind,layer,stateMachine,
                                                           branch,input,duplicate);
    return duplicate?0:found;
}

static const AnimatorOwnerGraphRecord* FindOwnerChild(const AnimatorOwnerGraphRecord* records,uint32_t count,
                                                      uint32_t layer,uint32_t stateMachine,uint32_t branch,
                                                      uintptr_t self,bool& duplicate) {
    const AnimatorOwnerGraphRecord* found=0;duplicate=false;
    for(uint32_t i=0;i<count;++i){
        const AnimatorOwnerGraphRecord& record=records[i];
        if(record.recordKind==OwnerChildNode&&record.layerIndex==layer&&
           record.stateMachineIndex==stateMachine&&record.branchIndex==branch&&record.self==self){
            if(found){duplicate=true;return 0;}found=records+i;
        }
    }
    return found;
}

static const AnimatorOwnerGraphRecord* FindOwnerResolverResult(const AnimatorOwnerGraphRecord* records,uint32_t count,
                                                               uint32_t layer,uint32_t stateMachine,
                                                               uintptr_t origin,bool& duplicate) {
    const AnimatorOwnerGraphRecord* found=0;duplicate=false;
    for(uint32_t i=0;i<count;++i){
        const AnimatorOwnerGraphRecord& record=records[i];
        if(record.recordKind==OwnerResolverDecision&&record.layerIndex==layer&&
           record.stateMachineIndex==stateMachine&&record.branchIndex==0&&
           record.resolverOrigin==origin&&record.resolverStatus!=0){
            if(found){duplicate=true;return 0;}found=records+i;
        }
    }
    return found;
}

static void SetOwnerFailure(AnimatorEndTransitionReceipt* receipt,
                            const AnimatorOwnerGraphRecord* records,uint32_t count,
                            uint32_t index,uint32_t byteOffset,uint32_t expected,uint32_t actual) {
    receipt->failureRecord=index;receipt->failureByteOffset=byteOffset;
    receipt->expectedWord=expected;receipt->actualWord=actual;
    if(records&&index<count){
        receipt->failureLayer=records[index].layerIndex;
        receipt->failureStateMachine=records[index].stateMachineIndex;
    }
}

static bool CompareNormalizationBytes(const void* expected,const void* actual,uint32_t count,
                                      uint32_t recordSize,bool owner,
                                      AnimatorEndTransitionReceipt* receipt) {
    const uint8_t* a=static_cast<const uint8_t*>(expected);
    const uint8_t* b=static_cast<const uint8_t*>(actual);
    for(uint32_t i=0;i<count;++i)if(a[i]!=b[i]){
        const uint32_t record=i/recordSize;
        const uint32_t offset=i%recordSize;
        const uint32_t wordOffset=offset&~3u;
        const uint32_t base=record*recordSize+wordOffset;
        const uint32_t expectedWord=*reinterpret_cast<const uint32_t*>(a+base);
        const uint32_t actualWord=*reinterpret_cast<const uint32_t*>(b+base);
        if(owner)SetOwnerFailure(receipt,static_cast<const AnimatorOwnerGraphRecord*>(expected),
                                 count/recordSize,record,offset,expectedWord,actualWord);
        else {
            receipt->failureRecord=record;receipt->failureByteOffset=offset;
            receipt->expectedWord=expectedWord;receipt->actualWord=actualWord;
            const AnimatorTransitionTopologyLayer* rows=static_cast<const AnimatorTransitionTopologyLayer*>(expected);
            if(record<count/recordSize)receipt->failureLayer=rows[record].index;
        }
        return false;
    }
    return true;
}

static bool EquivalentClipBindingCacheAllocations(const AnimatorOwnerGraphRecord& expected,
                                                   const AnimatorOwnerGraphRecord& actual) {
    // OverrideClipPlayables rebuilds these allocator-owned cache objects.  Their
    // addresses are process history, not Animator state.  Admit rebasing only
    // for clip rows whose complete pointer-independent observations match.
    if(expected.recordKind<OwnerChildNode||expected.recordKind>OwnerChildOutput||
       actual.recordKind!=expected.recordKind||
       expected.bindingCachePresence!=actual.bindingCachePresence||
       expected.bindingACCount!=actual.bindingACCount||
       expected.bindingACHash!=actual.bindingACHash||
       expected.bindingB0Count!=actual.bindingB0Count||
       expected.bindingB0Hash!=actual.bindingB0Hash||
       expected.bindingB4Hash!=actual.bindingB4Hash)return false;

    const uintptr_t expectedPointers[5]={
        expected.bindingAC,expected.bindingACInner,expected.bindingB0,
        expected.bindingB0Inner,expected.bindingB4
    };
    const uintptr_t actualPointers[5]={
        actual.bindingAC,actual.bindingACInner,actual.bindingB0,
        actual.bindingB0Inner,actual.bindingB4
    };
    const uint32_t presenceBits[5]={
        ClipBindingACPresent,ClipBindingACInnerPresent,ClipBindingB0Present,
        ClipBindingB0InnerPresent,ClipBindingB4Present
    };
    for(uint32_t i=0;i<5;++i){
        const bool expectedPresent=(expected.bindingCachePresence&presenceBits[i])!=0u;
        const bool actualPresent=(actual.bindingCachePresence&presenceBits[i])!=0u;
        if((expectedPointers[i]!=0)!=expectedPresent||
           (actualPointers[i]!=0)!=actualPresent)return false;
        for(uint32_t j=0;j<i;++j){
            // Preserve every alias relationship inside each observation while
            // deliberately ignoring aliases across two points in time.
            if((expectedPointers[i]==expectedPointers[j])!=
               (actualPointers[i]==actualPointers[j]))return false;
        }
    }
    return true;
}

static bool CoherentDeallocatedNullClip(const AnimatorOwnerGraphRecord& record);

static bool IsClipBindingAllocatorPointerByte(uint32_t recordOffset) {
    const uint32_t offsets[5]={
        static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,bindingAC)),
        static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,bindingACInner)),
        static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,bindingB0)),
        static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,bindingB0Inner)),
        static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,bindingB4))
    };
    for(uint32_t i=0;i<5;++i)
        if(recordOffset>=offsets[i]&&recordOffset<offsets[i]+sizeof(uintptr_t))return true;
    return false;
}

static bool IsClipBindingObservationByte(uint32_t recordOffset) {
    const uint32_t bindingBegin=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,bindingA8));
    const uint32_t clockBegin=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,clipCacheC0));
    const uint32_t cacheBegin=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,bindingCachePresence));
    return (recordOffset>=bindingBegin&&recordOffset<clockBegin)||
           (recordOffset>=cacheBegin&&recordOffset<sizeof(AnimatorOwnerGraphRecord));
}

static bool ExactClipBindingObservation(const AnimatorOwnerGraphRecord& expected,
                                        const AnimatorOwnerGraphRecord& actual) {
    const uint8_t* expectedBytes=reinterpret_cast<const uint8_t*>(&expected);
    const uint8_t* actualBytes=reinterpret_cast<const uint8_t*>(&actual);
    for(uint32_t offset=0;offset<sizeof(AnimatorOwnerGraphRecord);++offset)
        if(IsClipBindingObservationByte(offset)&&expectedBytes[offset]!=actualBytes[offset])return false;
    return true;
}

static bool IsClipRebindUntouchedByte(uint32_t recordOffset) {
    const uint32_t rawA4=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,rawA4A7));
    const uint32_t clipFlags=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,clipFlags10C));
    return (recordOffset>=rawA4&&recordOffset<rawA4+sizeof(uint32_t))||
           IsClipBindingObservationByte(recordOffset)||
           (recordOffset>=clipFlags&&recordOffset<clipFlags+sizeof(uint32_t));
}

static bool ExactClipRebindUntouchedObservation(const AnimatorOwnerGraphRecord& expected,
                                                const AnimatorOwnerGraphRecord& actual) {
    const uint8_t* expectedBytes=reinterpret_cast<const uint8_t*>(&expected);
    const uint8_t* actualBytes=reinterpret_cast<const uint8_t*>(&actual);
    for(uint32_t offset=0;offset<sizeof(AnimatorOwnerGraphRecord);++offset)
        if(IsClipRebindUntouchedByte(offset)&&expectedBytes[offset]!=actualBytes[offset])return false;
    return true;
}

static bool CompareOwnerAllowPendingVisit(const AnimatorOwnerGraphRecord* expected,
                                          const AnimatorOwnerGraphRecord* actual,
                                          uint32_t byteCount,const OwnerProjection* projection,
                                          AnimatorEndTransitionReceipt* receipt) {
    if(byteCount%sizeof(AnimatorOwnerGraphRecord)!=0u)
        return CompareNormalizationBytes(expected,actual,byteCount,
                                         sizeof(AnimatorOwnerGraphRecord),true,receipt);

    const uint8_t* expectedBytes=reinterpret_cast<const uint8_t*>(expected);
    const uint8_t* actualBytes=reinterpret_cast<const uint8_t*>(actual);
    const uint32_t recordCount=byteCount/sizeof(AnimatorOwnerGraphRecord);
    const uint32_t flagsOffset=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,flags7C));
    for(uint32_t byteIndex=0;byteIndex<byteCount;++byteIndex){
        if(expectedBytes[byteIndex]==actualBytes[byteIndex])continue;
        const uint32_t recordIndex=byteIndex/sizeof(AnimatorOwnerGraphRecord);
        const uint32_t recordOffset=byteIndex%sizeof(AnimatorOwnerGraphRecord);
        const AnimatorOwnerGraphRecord& expectedRecord=expected[recordIndex];
        const AnimatorOwnerGraphRecord& actualRecord=actual[recordIndex];
        const uint32_t expectedFlags=expected[recordIndex].flags7C;
        const uint32_t actualFlags=actual[recordIndex].flags7C;
        const OwnerRotationPlan* plan=FindOwnerRotation(
            projection,expectedRecord.layerIndex,expectedRecord.stateMachineIndex);
        const bool transitionContainer=plan&&
            (expectedRecord.self==plan->outer||expectedRecord.self==plan->oldBranch0||
             expectedRecord.self==plan->oldBranch1);
        // The flat observation repeats a playable's node state in its node,
        // input/output, and resolver rows. Admit bit 0x80 only when the row's
        // underlying object is one of this exact rotation plan's containers.
        if(recordOffset>=flagsOffset&&recordOffset<flagsOffset+sizeof(uint32_t)&&
           transitionContainer&&
           (expectedFlags&0x80u)==0u&&actualFlags==(expectedFlags|0x80u))continue;

        if(IsClipBindingAllocatorPointerByte(recordOffset)&&
           EquivalentClipBindingCacheAllocations(expectedRecord,actualRecord))continue;

        // SetClip changes only clip108, dirty byte +0x92, and the resolved
        // root's +0x93 byte.  Until the following ordinary maintenance visit,
        // clip-derived A4/cache/10C observations remain the exact preimage of
        // the physical playable.  Admit only that complete, captured preimage
        // for a specifically planned non-null rebind.  C0 and C4 remain strict:
        // the preceding Playable-time transaction must already have restored
        // them by physical playable identity.
        const OwnerReboundClip* rebound=projection&&projection->admitStagedRotationTransient&&plan?
            FindOwnerReboundClip(projection,expectedRecord.self):0;
        const bool reboundChildRow=expectedRecord.recordKind==OwnerChildNode||
                                   expectedRecord.recordKind==OwnerChildOutput;
        const bool reboundResolverRow=expectedRecord.recordKind>=OwnerResolverNode&&
                                      expectedRecord.recordKind<=OwnerResolverOutput;
        const uint32_t reboundRawA4Offset=
            static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,rawA4A7));
        // Child rows include the complete clip-binding/cache observation, so
        // require that whole retained preimage before admitting any transient
        // byte. Resolver rows deliberately use captureClip=false and therefore
        // carry only the playable's raw +0xA4 value from that clip-specific
        // range; their zero binding/cache fields cannot be compared with the
        // richer child-row preimage. Admit only the exact retained +0xA4 word
        // there, while every other resolver byte stays under the ordinary
        // projected target comparison.
        const bool exactReboundTransient=rebound&&
            ((reboundChildRow&&
             ExactClipRebindUntouchedObservation(rebound->preimage,actualRecord)&&
             IsClipRebindUntouchedByte(recordOffset))||
            (reboundResolverRow&&actualRecord.rawA4A7==rebound->preimage.rawA4A7&&
             recordOffset>=reboundRawA4Offset&&
             recordOffset<reboundRawA4Offset+sizeof(uint32_t)));
        if(rebound&&expectedRecord.branchIndex==rebound->targetBranchIndex&&
           actualRecord.branchIndex==rebound->targetBranchIndex&&
           expectedRecord.self==actualRecord.self&&expectedRecord.self==rebound->playable&&
           expectedRecord.vtable==actualRecord.vtable&&
           expectedRecord.vtable==receipt->unityBase+kAnimationClipPlayableVtableRva&&
           expectedRecord.recordKind==actualRecord.recordKind&&
           CarriesOwnerNodeState(expectedRecord.recordKind)&&
           (reboundChildRow?
                actualRecord.clip108==rebound->targetClip:
                actualRecord.clip108==expectedRecord.clip108)&&
           exactReboundTransient){
            const uint8_t* preimageBytes=reinterpret_cast<const uint8_t*>(&rebound->preimage);
            if(actualBytes[byteIndex]==preimageBytes[recordOffset])continue;
        }

        // A physical clip retains the binding set for its old branch role in
        // the EndTransition callback. Unity refreshes that set on the next
        // ordinary graph-maintenance visit. Admit this bounded transient only
        // for an explicitly planned rotation. A later no-op normalization has
        // no plan and therefore requires target counts/hashes/presence exactly.
        const OwnerStagedClip* staged=projection&&projection->admitStagedRotationTransient&&plan?
            FindOwnerStagedClip(projection,expectedRecord.layerIndex,expectedRecord.stateMachineIndex,
                                expectedRecord.self):0;
        if(staged&&expectedRecord.branchIndex==0u&&actualRecord.branchIndex==0u&&
           !FindOwnerClearedClip(projection,expectedRecord.self)&&
           expectedRecord.self==actualRecord.self&&expectedRecord.self==staged->playable&&
           expectedRecord.vtable==actualRecord.vtable&&
           expectedRecord.vtable==receipt->unityBase+kAnimationClipPlayableVtableRva&&
           expectedRecord.recordKind==actualRecord.recordKind&&
           (expectedRecord.recordKind==OwnerChildNode||expectedRecord.recordKind==OwnerChildOutput)&&
           IsClipBindingObservationByte(recordOffset)&&
           ExactClipBindingObservation(staged->preimage,actualRecord))continue;
        // EndTransition calls SetClip(null) on every non-terminal child of the
        // old branch 0, but SetClip does not release the clip's binding cache.
        // That cache remains byte-for-byte at its captured preimage until the
        // following ordinary graph-maintenance visit.  Admit only that exact
        // pending observation for the specific planned clear; the later no-plan
        // verification has no projection and therefore still requires the
        // coherent target-null cache exactly.
        const OwnerClearedClip* clearedClip=
            projection&&projection->admitStagedRotationTransient&&plan?
                FindOwnerClearedClip(projection,expectedRecord.self):0;
        if(clearedClip&&expectedRecord.branchIndex==clearedClip->targetBranchIndex&&
           actualRecord.branchIndex==clearedClip->targetBranchIndex&&
           expectedRecord.self==actualRecord.self&&expectedRecord.self==clearedClip->playable&&
           expectedRecord.vtable==actualRecord.vtable&&
           expectedRecord.vtable==receipt->unityBase+kAnimationClipPlayableVtableRva&&
           expectedRecord.recordKind==actualRecord.recordKind&&
           (expectedRecord.recordKind==OwnerChildNode||expectedRecord.recordKind==OwnerChildOutput)&&
           CoherentDeallocatedNullClip(expectedRecord)&&actualRecord.clip108==0u&&
           IsClipBindingObservationByte(recordOffset)&&
           ExactClipBindingObservation(clearedClip->preimage,actualRecord))continue;
        const uint32_t raw9497Offset=
            static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,raw9497));
        if(projection&&projection->admitStagedRotationTransient&&
           FindOwnerClearedClip(projection,expectedRecord.self)&&
           expectedRecord.clip108==0u&&(expectedRecord.raw9497&0xFFu)==0u&&
           actualRecord.raw9497==(expectedRecord.raw9497|1u)&&
           recordOffset>=raw9497Offset&&recordOffset<raw9497Offset+sizeof(uint32_t))continue;

        const uint32_t kind=expectedRecord.recordKind;
        const bool childOrResolverRow=kind>=OwnerChildNode&&kind<=OwnerResolverOutput;
        // Active/inactive propagation consumes bit 0x40 on the selected child
        // and sets it on the cleared child during the next graph-maintenance
        // visit. The high pointer/tag bits and every other flag stay exact.
        if(recordOffset>=flagsOffset&&recordOffset<flagsOffset+sizeof(uint32_t)&&
           projection&&projection->admitStagedRotationTransient&&plan&&
           childOrResolverRow&&(expectedFlags^actualFlags)==0x40u)continue;

        const uint32_t dirtyOffset=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,dirty9093));
        if(recordOffset>=dirtyOffset&&recordOffset<dirtyOffset+sizeof(uint32_t)&&
           rebound&&expectedRecord.branchIndex==rebound->targetBranchIndex&&
           actualRecord.branchIndex==rebound->targetBranchIndex&&
           expectedRecord.self==actualRecord.self&&expectedRecord.self==rebound->playable&&
           actualRecord.dirty9093==((rebound->playableDirtyBefore&0xFF00FFFFu)|0x00010000u))continue;
        if(recordOffset>=dirtyOffset&&recordOffset<dirtyOffset+sizeof(uint32_t)&&
           projection&&projection->admitStagedRotationTransient&&plan&&
           IsOwnerDirtyRoot(projection,expectedRecord.self)&&
           (expectedRecord.dirty9093&0xFF000000u)==0u&&
           actualRecord.dirty9093==(expectedRecord.dirty9093|0x01000000u))continue;

        const uint32_t rawA4Offset=static_cast<uint32_t>(offsetof(AnimatorOwnerGraphRecord,rawA4A7));
        if(recordOffset>=rawA4Offset&&recordOffset<rawA4Offset+sizeof(uint32_t)&&
           projection&&projection->admitStagedRotationTransient&&plan&&
           FindOwnerClearedClip(projection,expectedRecord.self)&&
           expectedRecord.rawA4A7==0u&&
           actualRecord.rawA4A7==FindOwnerClearedClip(projection,expectedRecord.self)->expectedRawA4A7)continue;

        const uint32_t wordOffset=recordOffset&~3u;
        const uint32_t base=recordIndex*sizeof(AnimatorOwnerGraphRecord)+wordOffset;
        SetOwnerFailure(receipt,expected,recordCount,recordIndex,recordOffset,
                        *reinterpret_cast<const uint32_t*>(expectedBytes+base),
                        *reinterpret_cast<const uint32_t*>(actualBytes+base));
        return false;
    }
    return true;
}

static bool ResolverSupersetRecordRootedInTarget(
    const AnimatorOwnerGraphRecord& record,
    const AnimatorOwnerGraphRecord* target,uint32_t targetCount) {
    if(record.recordKind<OwnerResolverNode||record.recordKind>OwnerResolverTerminal||
       record.branchIndex!=0u||!record.resolverOrigin)return false;
    bool duplicate=false;
    const AnimatorOwnerGraphRecord* origin=FindOwnerChild(
        target,targetCount,record.layerIndex,record.stateMachineIndex,
        record.branchIndex,record.resolverOrigin,duplicate);
    return origin&&!duplicate;
}

// A paused Stage-B capture can retain a longer SetClip resolver walk than the
// checkpoint even though every checkpoint-owned playable and logical graph
// record is still present. This is a read-only, no-EndTransition holding
// state: admit only an exact checkpoint row sequence interleaved with
// additional resolver rows rooted at an exact checkpoint child. The caller separately
// requires the complete live projection to remain byte-exact through the
// no-op transaction, and the later requireNoPlan verification stays exact.
static bool CompareOwnerResolverSupersetAllowPendingVisit(
    const AnimatorOwnerGraphRecord* expected,uint32_t expectedCount,
    const AnimatorOwnerGraphRecord* actual,uint32_t actualCount,
    const OwnerProjection* projection,AnimatorEndTransitionReceipt* receipt) {
    if(actualCount<=expectedCount){
        if(actualCount!=expectedCount){
            receipt->result=ResultTopologyMismatch;receipt->expectedWord=expectedCount;
            receipt->actualWord=actualCount;return false;
        }
        return CompareOwnerAllowPendingVisit(
            expected,actual,expectedCount*sizeof(AnimatorOwnerGraphRecord),projection,receipt);
    }

    AnimatorOwnerGraphRecord* filtered=static_cast<AnimatorOwnerGraphRecord*>(
        HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                  expectedCount*sizeof(AnimatorOwnerGraphRecord)));
    if(!filtered){
        receipt->lastError=GetLastError();receipt->result=ResultFault;return false;
    }

    bool success=false;
    do {
        for(uint32_t i=0;i<expectedCount;++i){
            if(expected[i].recordKind>=OwnerResolverNode&&
               expected[i].recordKind<=OwnerResolverTerminal){
                receipt->result=ResultInvalidBlob;
                SetOwnerFailure(receipt,expected,expectedCount,i,0,0u,expected[i].recordKind);break;
            }
        }
        if(receipt->result)break;
        uint32_t filteredCount=0;
        for(uint32_t i=0;i<actualCount;++i){
            const bool resolver=actual[i].recordKind>=OwnerResolverNode&&
                                actual[i].recordKind<=OwnerResolverTerminal;
            if(resolver){
                if(!ResolverSupersetRecordRootedInTarget(actual[i],expected,expectedCount)){
                    receipt->result=ResultTopologyMismatch;
                    SetOwnerFailure(receipt,actual,actualCount,i,0,OwnerResolverNode,actual[i].recordKind);break;
                }
                continue;
            }
            if(filteredCount>=expectedCount){
                receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,actual,actualCount,i,0,expectedCount,filteredCount+1u);break;
            }
            filtered[filteredCount++]=actual[i];
        }
        if(receipt->result)break;
        if(filteredCount!=expectedCount){
            receipt->result=ResultTopologyMismatch;receipt->expectedWord=expectedCount;
            receipt->actualWord=filteredCount;break;
        }
        success=CompareOwnerAllowPendingVisit(
            expected,filtered,expectedCount*sizeof(AnimatorOwnerGraphRecord),projection,receipt);
    } while(false);

    HeapFree(GetProcessHeap(),0,filtered);
    return success;
}

static bool CleanSettledOuter(const AnimatorOwnerGraphRecord* outer,
                              const AnimatorOwnerGraphRecord* branch0,
                              const AnimatorOwnerGraphRecord* branch1) {
    // Unity's lifecycle topology exposes +0xA4 as a single argument byte.
    // The adjacent opaque +0xA5..+0xA7 bytes are not part of that field and
    // legitimately carry nonzero data for some chef controller incarnations.
    return outer&&branch0&&branch1&&outer->rawA0A3==2u&&(outer->rawA4A7&0xFFu)==0u&&
           outer->dirty9093==0u&&branch0->dirty9093==0u&&branch1->dirty9093==0u;
}

static bool SameDirectPlayableTopology(const AnimatorOwnerGraphRecord& left,
                                       const AnimatorOwnerGraphRecord& right);
static bool SameDirectEntryTopology(const AnimatorOwnerGraphRecord& left,
                                    const AnimatorOwnerGraphRecord& right);
static bool CoherentDeallocatedNullClip(const AnimatorOwnerGraphRecord& record);

static bool OwnerGraphContainsClip(const AnimatorOwnerGraphRecord* records,uint32_t count,
                                   uintptr_t unityBase,uintptr_t clip) {
    if(!records||!clip)return false;
    for(uint32_t i=0;i<count;++i)
        if(records[i].recordKind==OwnerChildNode&&
           records[i].vtable==unityBase+kAnimationClipPlayableVtableRva&&
           records[i].clip108==clip)return true;
    return false;
}

static bool AppendOwnerReboundClip(uintptr_t unityBase,uintptr_t controller,
                                   const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
                                   const AnimatorOwnerGraphRecord& child,
                                   uint32_t targetBranch,uint32_t input,uintptr_t targetClip,
                                   OwnerReboundClip* rebound,uint32_t reboundCapacity,
                                   uint32_t& reboundCount,AnimatorEndTransitionReceipt* receipt) {
    const uint32_t childRecord=static_cast<uint32_t>(&child-current);
    if(child.vtable!=unityBase+kAnimationClipPlayableVtableRva||
       !child.clip108||!targetClip||!OwnerGraphContainsClip(current,currentCount,unityBase,targetClip)){
        receipt->result=ResultTopologyMismatch;
        SetOwnerFailure(receipt,current,currentCount,childRecord,80,targetClip,child.clip108);return false;
    }
    if(reboundCount>=reboundCapacity){receipt->result=ResultCapacity;return false;}
    for(uint32_t prior=0;prior<reboundCount;++prior)if(rebound[prior].playable==child.self){
        receipt->result=ResultTopologyMismatch;
        SetOwnerFailure(receipt,current,currentCount,childRecord,20,0,child.self);return false;
    }
    RootByType rootByType=reinterpret_cast<RootByType>(unityBase+kRootByTypeRva);
    const uintptr_t dirtyRoot=reinterpret_cast<uintptr_t>(
        rootByType(reinterpret_cast<void*>(child.self),0u));
    if(dirtyRoot!=controller||!Readable(reinterpret_cast<const void*>(dirtyRoot+0x90u),4u)){
        receipt->result=ResultTopologyMismatch;
        SetOwnerFailure(receipt,current,currentCount,childRecord,112,controller,dirtyRoot);return false;
    }
    OwnerReboundClip& item=rebound[reboundCount++];
    item.layerIndex=child.layerIndex;item.stateMachineIndex=child.stateMachineIndex;
    item.targetBranchIndex=targetBranch;item.inputIndex=input;item.currentRecord=childRecord;
    item.playable=child.self;
    item.sourceClip=child.clip108;item.targetClip=targetClip;item.dirtyRoot=dirtyRoot;
    item.playableDirtyBefore=child.dirty9093;
    item.rootDirtyBefore=*reinterpret_cast<const uint32_t*>(dirtyRoot+0x90u);
    item.preimage=child;
    return true;
}

static bool BuildOwnerProjection(uintptr_t unityBase,
                                 uintptr_t controller,
                                 const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
                                 const AnimatorOwnerGraphRecord* target,uint32_t targetCount,
                                 OwnerRotationPlan* plans,uint32_t planCapacity,uint32_t& planCount,
                                 OwnerClearedClip* cleared,uint32_t clearedCapacity,uint32_t& clearedCount,
                                 OwnerStagedClip* staged,uint32_t stagedCapacity,uint32_t& stagedCount,
                                 OwnerReboundClip* rebound,uint32_t reboundCapacity,uint32_t& reboundCount,
                                 AnimatorEndTransitionReceipt* receipt) {
    planCount=0;clearedCount=0;stagedCount=0;reboundCount=0;
    uint32_t currentOuterCount=0,targetOuterCount=0;
    for(uint32_t i=0;i<currentCount;++i)if(current[i].recordKind==OwnerOuterNode)++currentOuterCount;
    for(uint32_t i=0;i<targetCount;++i)if(target[i].recordKind==OwnerOuterNode)++targetOuterCount;
    if(currentOuterCount==0||currentOuterCount!=targetOuterCount||currentOuterCount>planCapacity){
        receipt->result=ResultTopologyMismatch;receipt->expectedWord=targetOuterCount;
        receipt->actualWord=currentOuterCount;return false;
    }
    for(uint32_t i=0;i<currentCount;++i){
        const AnimatorOwnerGraphRecord& outer=current[i];
        if(outer.recordKind!=OwnerOuterNode)continue;
        const AnimatorOwnerGraphRecord* targetOuter=FindUniqueOwnerRecord(target,targetCount,OwnerOuterNode,
            outer.layerIndex,outer.stateMachineIndex,0xFFFFFFFFu,0xFFFFFFFFu);
        if(!targetOuter){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,0,outer.self,targetOuter?targetOuter->self:0);return false;
        }
        const AnimatorOwnerGraphRecord* currentInputs[3]={};
        const AnimatorOwnerGraphRecord* targetInputs[3]={};
        for(uint32_t input=0;input<3;++input){
            currentInputs[input]=FindUniqueOwnerRecord(current,currentCount,OwnerOuterInput,outer.layerIndex,
                outer.stateMachineIndex,0xFFFFFFFFu,input);
            if(!currentInputs[input]){receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,current,currentCount,i,0,input,0xFFFFFFFFu);return false;}
            targetInputs[input]=FindUniqueOwnerRecord(target,targetCount,OwnerOuterInput,outer.layerIndex,
                outer.stateMachineIndex,0xFFFFFFFFu,input);
            if(!targetInputs[input]){receipt->result=ResultInvalidBlob;
                SetOwnerFailure(receipt,target,targetCount,static_cast<uint32_t>(targetOuter-target),0,input,0xFFFFFFFFu);return false;}
        }
        const AnimatorOwnerGraphRecord* currentBranch0=FindUniqueOwnerRecord(current,currentCount,OwnerBranchNode,
            outer.layerIndex,outer.stateMachineIndex,0,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* currentBranch1=FindUniqueOwnerRecord(current,currentCount,OwnerBranchNode,
            outer.layerIndex,outer.stateMachineIndex,1,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* targetBranch0=FindUniqueOwnerRecord(target,targetCount,OwnerBranchNode,
            outer.layerIndex,outer.stateMachineIndex,0,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* targetBranch1=FindUniqueOwnerRecord(target,targetCount,OwnerBranchNode,
            outer.layerIndex,outer.stateMachineIndex,1,0xFFFFFFFFu);
        if(!currentBranch0||!currentBranch1||!targetBranch0||!targetBranch1||
           !CleanSettledOuter(&outer,currentBranch0,currentBranch1)||
           !CleanSettledOuter(targetOuter,targetBranch0,targetBranch1)){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,64,0,outer.dirty9093);return false;
        }
        const uintptr_t current0=currentInputs[0]->entryPlayable;
        const uintptr_t current1=currentInputs[1]->entryPlayable;
        const uintptr_t current2=currentInputs[2]->entryPlayable;
        const uintptr_t target0=targetInputs[0]->entryPlayable;
        const uintptr_t target1=targetInputs[1]->entryPlayable;
        const uintptr_t target2=targetInputs[2]->entryPlayable;
        if(!current0||!current1||!current2||current0==current1||current0==current2||current1==current2||
           currentInputs[0]->entryPortRaw||currentInputs[1]->entryPortRaw||
           targetInputs[0]->entryPortRaw||targetInputs[1]->entryPortRaw||targetInputs[2]->entryPortRaw||
           !ValidWeight(targetInputs[0]->entryWeightBits)||!ValidWeight(targetInputs[1]->entryWeightBits)||
           !ValidWeight(targetInputs[2]->entryWeightBits)||current2!=target2){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,92,target2,current2);return false;
        }
        const bool exact=current0==target0&&current1==target1;
        const bool rotated=current0==target1&&current1==target0;
        if(!exact&&!rotated){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,92,target0,current0);return false;
        }
        if(exact)continue;
        // Keep every rotation-plan rejection distinguishable in the receipt.
        // This is still a pre-mutation guard: the extra branches only identify
        // which observed topology invariant failed.
        if(planCount>=planCapacity){
            receipt->result=ResultCapacity;
            SetOwnerFailure(receipt,current,currentCount,i,0,planCapacity,planCount);return false;
        }
        if(currentBranch0->self!=current0){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,20,current0,currentBranch0->self);return false;
        }
        if(currentBranch1->self!=current1){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,20,current1,currentBranch1->self);return false;
        }
        if(targetBranch0->self!=target0){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,target,targetCount,static_cast<uint32_t>(targetOuter-target),20,
                            target0,targetBranch0->self);return false;
        }
        if(targetBranch1->self!=target1){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,target,targetCount,static_cast<uint32_t>(targetOuter-target),20,
                            target1,targetBranch1->self);return false;
        }
        const uint32_t animationMixerVtable=static_cast<uint32_t>(unityBase+kAnimationMixerPlayableVtableRva);
        if(currentBranch0->vtable!=animationMixerVtable){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,24,animationMixerVtable,currentBranch0->vtable);return false;
        }
        if(currentBranch1->vtable!=animationMixerVtable){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,24,animationMixerVtable,currentBranch1->vtable);return false;
        }
        if(targetBranch0->vtable!=animationMixerVtable){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,target,targetCount,static_cast<uint32_t>(targetOuter-target),24,
                            animationMixerVtable,targetBranch0->vtable);return false;
        }
        if(targetBranch1->vtable!=animationMixerVtable){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,target,targetCount,static_cast<uint32_t>(targetOuter-target),24,
                            animationMixerVtable,targetBranch1->vtable);return false;
        }
        if(currentBranch0->inputCount==0){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,40,1,currentBranch0->inputCount);return false;
        }
        if(currentBranch1->inputCount!=targetBranch0->inputCount){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,40,targetBranch0->inputCount,
                            currentBranch1->inputCount);return false;
        }
        if(currentBranch0->inputCount!=targetBranch1->inputCount){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,40,targetBranch1->inputCount,
                            currentBranch0->inputCount);return false;
        }
        const AnimatorOwnerGraphRecord* output0=FindUniqueOwnerRecord(current,currentCount,OwnerBranchOutput,
            outer.layerIndex,outer.stateMachineIndex,0,0);
        const AnimatorOwnerGraphRecord* output1=FindUniqueOwnerRecord(current,currentCount,OwnerBranchOutput,
            outer.layerIndex,outer.stateMachineIndex,1,0);
        if(!output0||!output1||currentBranch0->outputCount==0||currentBranch1->outputCount==0||
           output0->entryAddress!=currentBranch0->outputEntries||output1->entryAddress!=currentBranch1->outputEntries||
           output0->entryPlayable!=outer.self||output1->entryPlayable!=outer.self){
            receipt->result=ResultTopologyMismatch;
            SetOwnerFailure(receipt,current,currentCount,i,48,outer.self,output0?output0->entryPlayable:0);return false;
        }
        OwnerRotationPlan& plan=plans[planCount++];
        plan.layerIndex=outer.layerIndex;plan.stateMachineIndex=outer.stateMachineIndex;
        plan.outer=outer.self;plan.outerEntries=outer.inputEntries;
        plan.oldBranch0=current0;plan.oldBranch1=current1;plan.oldBranch2=current2;
        plan.oldBranch0InputEntries=currentBranch0->inputEntries;
        plan.oldBranch0InputCount=currentBranch0->inputCount;
        plan.oldBranch1InputEntries=currentBranch1->inputEntries;
        plan.oldBranch1InputCount=currentBranch1->inputCount;
        plan.oldBranch0Output0=output0->entryAddress;plan.oldBranch1Output0=output1->entryAddress;
        plan.targetOuterWeight0=targetInputs[0]->entryWeightBits;
        plan.targetOuterWeight1=targetInputs[1]->entryWeightBits;
        plan.targetOuterWeight2=targetInputs[2]->entryWeightBits;
        for(uint32_t input=0;input<currentBranch1->inputCount;++input){
            const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,OwnerBranchInput,
                outer.layerIndex,outer.stateMachineIndex,0,input);
            const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,OwnerBranchInput,
                outer.layerIndex,outer.stateMachineIndex,1,input);
            if(!targetInput||!currentInput||!ValidWeight(targetInput->entryWeightBits)||
               !SameDirectEntryTopology(*currentInput,*targetInput)){
                receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,current,currentCount,i,88,input,0xFFFFFFFFu);return false;
            }
            bool currentDuplicate=false,targetDuplicate=false;
            const AnimatorOwnerGraphRecord* currentChild=FindOwnerChild(current,currentCount,
                outer.layerIndex,outer.stateMachineIndex,1,currentInput->entryPlayable,currentDuplicate);
            const AnimatorOwnerGraphRecord* targetChild=FindOwnerChild(target,targetCount,
                outer.layerIndex,outer.stateMachineIndex,0,targetInput->entryPlayable,targetDuplicate);
            if(currentDuplicate){
                receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,current,currentCount,i,12,0,1);return false;
            }
            if(targetDuplicate){
                receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,target,targetCount,static_cast<uint32_t>(targetOuter-target),12,0,1);return false;
            }
            if((currentChild==0)!=(targetChild==0)){
                receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,current,currentCount,i,20,
                    targetChild?targetChild->self:0,currentChild?currentChild->self:0);return false;
            }
            if(currentChild){
                const uint32_t currentChildRecord=static_cast<uint32_t>(currentChild-current);
#define OC2_REQUIRE_CHILD_TOPOLOGY(field,offset) \
                if(currentChild->field!=targetChild->field){ \
                    receipt->result=ResultTopologyMismatch; \
                    SetOwnerFailure(receipt,current,currentCount,currentChildRecord,offset, \
                                    targetChild->field,currentChild->field);return false; \
                }
                OC2_REQUIRE_CHILD_TOPOLOGY(self,20)
                OC2_REQUIRE_CHILD_TOPOLOGY(vtable,24)
                OC2_REQUIRE_CHILD_TOPOLOGY(internal,28)
                OC2_REQUIRE_CHILD_TOPOLOGY(graph,32)
                OC2_REQUIRE_CHILD_TOPOLOGY(inputEntries,36)
                OC2_REQUIRE_CHILD_TOPOLOGY(inputCount,40)
                OC2_REQUIRE_CHILD_TOPOLOGY(inputCapacityRaw,44)
                OC2_REQUIRE_CHILD_TOPOLOGY(outputEntries,48)
                OC2_REQUIRE_CHILD_TOPOLOGY(outputCount,52)
                OC2_REQUIRE_CHILD_TOPOLOGY(outputCapacityRaw,56)
#undef OC2_REQUIRE_CHILD_TOPOLOGY
            }
            if(currentChild&&currentChild->vtable==unityBase+kAnimationClipPlayableVtableRva){
                const uint32_t currentChildRecord=static_cast<uint32_t>(currentChild-current);
                if(currentChild->clip108!=targetChild->clip108){
                    if(!targetChild->clip108&&currentChild->clip108){
                        if(targetInput->entryWeightBits!=0u||
                           !CoherentDeallocatedNullClip(*targetChild)||
                           clearedCount>=clearedCapacity){
                            receipt->result=clearedCount>=clearedCapacity?ResultCapacity:ResultTopologyMismatch;
                            SetOwnerFailure(receipt,current,currentCount,currentChildRecord,80,
                                            targetChild->clip108,currentChild->clip108);return false;
                        }
                        for(uint32_t prior=0;prior<clearedCount;++prior)
                            if(cleared[prior].playable==currentChild->self){
                                receipt->result=ResultTopologyMismatch;
                                SetOwnerFailure(receipt,current,currentCount,currentChildRecord,20,
                                                0,currentChild->self);return false;
                            }
                        RootByType rootByType=reinterpret_cast<RootByType>(unityBase+kRootByTypeRva);
                        const uintptr_t dirtyRoot=reinterpret_cast<uintptr_t>(
                            rootByType(reinterpret_cast<void*>(currentChild->self),0u));
                        if(dirtyRoot!=controller||
                           !Readable(reinterpret_cast<const void*>(dirtyRoot+0x90u),4u)){
                            receipt->result=ResultTopologyMismatch;
                            SetOwnerFailure(receipt,current,currentCount,currentChildRecord,112,
                                            controller,dirtyRoot);return false;
                        }
                        OwnerClearedClip& item=cleared[clearedCount++];
                        item.layerIndex=outer.layerIndex;
                        item.stateMachineIndex=outer.stateMachineIndex;
                        item.targetBranchIndex=0u;item.inputIndex=input;
                        item.currentRecord=currentChildRecord;item.requiresPreClear=1u;
                        item.playable=currentChild->self;item.dirtyRoot=dirtyRoot;
                        item.rootDirtyBefore=*reinterpret_cast<const uint32_t*>(dirtyRoot+0x90u);
                        item.expectedRawA4A7=currentChild->rawA4A7;item.preimage=*currentChild;
                        continue;
                    }
                    if(!targetChild->clip108||!currentChild->clip108){
                        receipt->result=ResultTopologyMismatch;
                        SetOwnerFailure(receipt,current,currentCount,currentChildRecord,80,
                                        targetChild->clip108,currentChild->clip108);return false;
                    }
                    if(!AppendOwnerReboundClip(unityBase,controller,current,currentCount,*currentChild,
                        0u,input,targetChild->clip108,rebound,reboundCapacity,reboundCount,receipt))return false;
                } else {
                    if(stagedCount>=stagedCapacity){receipt->result=ResultCapacity;return false;}
                    for(uint32_t prior=0;prior<stagedCount;++prior)if(staged[prior].playable==currentChild->self){
                        receipt->result=ResultTopologyMismatch;
                        SetOwnerFailure(receipt,current,currentCount,i,20,0,currentChild->self);return false;
                    }
                    OwnerStagedClip& item=staged[stagedCount++];
                    item.layerIndex=outer.layerIndex;item.stateMachineIndex=outer.stateMachineIndex;
                    item.playable=currentChild->self;item.preimage=*currentChild;
                }
            }
        }
        for(uint32_t input=0;input+1u<currentBranch0->inputCount;++input){
            const AnimatorOwnerGraphRecord* branchInput=FindUniqueOwnerRecord(current,currentCount,OwnerBranchInput,
                outer.layerIndex,outer.stateMachineIndex,0,input);
            const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,OwnerBranchInput,
                outer.layerIndex,outer.stateMachineIndex,1,input);
            if(!branchInput||!targetInput||!branchInput->entryPlayable||
               !SameDirectEntryTopology(*branchInput,*targetInput)){receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,current,currentCount,i,92,input,0xFFFFFFFFu);return false;}
            bool duplicate=false;
            const AnimatorOwnerGraphRecord* child=FindOwnerChild(current,currentCount,outer.layerIndex,
                outer.stateMachineIndex,0,branchInput->entryPlayable,duplicate);
            bool targetDuplicate=false;
            const AnimatorOwnerGraphRecord* targetChild=FindOwnerChild(target,targetCount,outer.layerIndex,
                outer.stateMachineIndex,1,targetInput->entryPlayable,targetDuplicate);
            if(!child||!targetChild||duplicate||targetDuplicate||
               child->vtable!=unityBase+kAnimationClipPlayableVtableRva||
               targetChild->vtable!=child->vtable||!SameDirectPlayableTopology(*child,*targetChild)){
                receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,current,currentCount,i,24,unityBase+kAnimationClipPlayableVtableRva,
                                child?child->vtable:0);return false;
            }
            if(targetChild->clip108){
                if(!AppendOwnerReboundClip(unityBase,controller,current,currentCount,*child,
                    1u,input,targetChild->clip108,rebound,reboundCapacity,reboundCount,receipt))return false;
                continue;
            }
            if(!child->clip108)continue;
            if(clearedCount>=clearedCapacity){receipt->result=ResultCapacity;return false;}
            for(uint32_t prior=0;prior<clearedCount;++prior)if(cleared[prior].playable==child->self){
                receipt->result=ResultTopologyMismatch;SetOwnerFailure(receipt,current,currentCount,i,20,0,child->self);return false;
            }
            const AnimatorOwnerGraphRecord* decision=FindOwnerResolverResult(current,currentCount,
                outer.layerIndex,outer.stateMachineIndex,child->self,duplicate);
            if(!decision||duplicate||
               ((decision->resolverStatus==1u||decision->resolverStatus==4u)!=(decision->resolverResult!=0))||
               ((decision->resolverStatus==2u||decision->resolverStatus==3u||decision->resolverStatus==5u)&&
                 decision->resolverResult!=0)){
                receipt->result=ResultTopologyMismatch;
                SetOwnerFailure(receipt,current,currentCount,i,112,1,decision?decision->resolverStatus:0);return false;
            }
            cleared[clearedCount].layerIndex=outer.layerIndex;
            cleared[clearedCount].stateMachineIndex=outer.stateMachineIndex;
            cleared[clearedCount].targetBranchIndex=1u;
            cleared[clearedCount].inputIndex=input;
            cleared[clearedCount].currentRecord=static_cast<uint32_t>(child-current);
            cleared[clearedCount].requiresPreClear=0u;
            cleared[clearedCount].playable=child->self;
            cleared[clearedCount].dirtyRoot=decision->resolverResult!=child->self?decision->resolverResult:0;
            cleared[clearedCount].rootDirtyBefore=cleared[clearedCount].dirtyRoot&&
                Readable(reinterpret_cast<const void*>(cleared[clearedCount].dirtyRoot+0x90u),4u)?
                *reinterpret_cast<const uint32_t*>(cleared[clearedCount].dirtyRoot+0x90u):0u;
            cleared[clearedCount].expectedRawA4A7=child->rawA4A7;
            cleared[clearedCount].preimage=*child;
            ++clearedCount;
        }
    }
    if(planCount==0){
        // A no-op exact target is permitted, but only after the full projected
        // owner comparison proves that no unclassified difference exists.
        return true;
    }
    return true;
}

static void SetTargetNullClipFailure(AnimatorTargetNullClipReceipt* receipt,
                                     const AnimatorOwnerGraphRecord* records,uint32_t count,
                                     uint32_t index,uint32_t byteOffset,uint32_t expected,uint32_t actual) {
    receipt->failureRecord=index;receipt->failureByteOffset=byteOffset;
    receipt->expectedWord=expected;receipt->actualWord=actual;
    if(records&&index<count){
        receipt->failureLayer=records[index].layerIndex;
        receipt->failureStateMachine=records[index].stateMachineIndex;
    }
}

static bool CompareTargetNullClipBytes(const AnimatorOwnerGraphRecord* expected,
                                       const AnimatorOwnerGraphRecord* actual,uint32_t count,
                                       AnimatorTargetNullClipReceipt* receipt) {
    const uint8_t* left=reinterpret_cast<const uint8_t*>(expected);
    const uint8_t* right=reinterpret_cast<const uint8_t*>(actual);
    const uint32_t bytes=count*sizeof(AnimatorOwnerGraphRecord);
    for(uint32_t i=0;i<bytes;++i)if(left[i]!=right[i]){
        const uint32_t record=i/sizeof(AnimatorOwnerGraphRecord);
        const uint32_t offset=i%sizeof(AnimatorOwnerGraphRecord);
        const uint32_t word=offset&~3u;
        SetTargetNullClipFailure(receipt,expected,count,record,offset,
            *reinterpret_cast<const uint32_t*>(left+record*sizeof(AnimatorOwnerGraphRecord)+word),
            *reinterpret_cast<const uint32_t*>(right+record*sizeof(AnimatorOwnerGraphRecord)+word));
        return false;
    }
    return true;
}

static bool FilterTargetNullResolverSuperset(
    const AnimatorOwnerGraphRecord* target,uint32_t targetCount,
    const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
    AnimatorOwnerGraphRecord* filtered,AnimatorTargetNullClipReceipt* receipt) {
    if(!target||!current||!filtered||currentCount<=targetCount){
        receipt->result=ResultBadArgument;return false;
    }
    for(uint32_t i=0;i<targetCount;++i){
        if(target[i].recordKind>=OwnerResolverNode&&target[i].recordKind<=OwnerResolverTerminal){
            receipt->result=ResultInvalidBlob;
            SetTargetNullClipFailure(receipt,target,targetCount,i,0,0u,target[i].recordKind);
            return false;
        }
    }
    uint32_t filteredCount=0;
    for(uint32_t i=0;i<currentCount;++i){
        const bool resolver=current[i].recordKind>=OwnerResolverNode&&
                            current[i].recordKind<=OwnerResolverTerminal;
        if(resolver){
            if(!ResolverSupersetRecordRootedInTarget(current[i],target,targetCount)){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,0,
                                         OwnerResolverNode,current[i].recordKind);
                return false;
            }
            continue;
        }
        if(filteredCount>=targetCount){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,i,0,targetCount,filteredCount+1u);
            return false;
        }
        filtered[filteredCount++]=current[i];
    }
    if(filteredCount!=targetCount){
        receipt->result=ResultTopologyMismatch;receipt->expectedWord=targetCount;
        receipt->actualWord=filteredCount;return false;
    }
    for(uint32_t i=0;i<targetCount;++i){
        const AnimatorOwnerGraphRecord& expected=target[i];
        const AnimatorOwnerGraphRecord& actual=filtered[i];
        if(actual.recordKind!=expected.recordKind||actual.layerIndex!=expected.layerIndex||
           actual.stateMachineIndex!=expected.stateMachineIndex||actual.branchIndex!=expected.branchIndex||
           actual.inputIndex!=expected.inputIndex||actual.self!=expected.self||actual.vtable!=expected.vtable||
           actual.internal!=expected.internal||actual.graph!=expected.graph||
           actual.inputEntries!=expected.inputEntries||actual.inputCount!=expected.inputCount||
           actual.inputCapacityRaw!=expected.inputCapacityRaw||actual.outputEntries!=expected.outputEntries||
           actual.outputCount!=expected.outputCount||actual.outputCapacityRaw!=expected.outputCapacityRaw||
           actual.entryAddress!=expected.entryAddress||actual.entryPlayable!=expected.entryPlayable||
           actual.entryPortRaw!=expected.entryPortRaw){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,target,targetCount,i,0,expected.recordKind,actual.recordKind);
            return false;
        }
    }
    return true;
}

static bool SameDirectPlayableTopology(const AnimatorOwnerGraphRecord& left,
                                       const AnimatorOwnerGraphRecord& right) {
    return left.self==right.self&&left.vtable==right.vtable&&left.internal==right.internal&&
           left.graph==right.graph&&left.inputEntries==right.inputEntries&&
           left.inputCount==right.inputCount&&left.inputCapacityRaw==right.inputCapacityRaw&&
           left.outputEntries==right.outputEntries&&left.outputCount==right.outputCount&&
           left.outputCapacityRaw==right.outputCapacityRaw;
}

static bool SameDirectEntryTopology(const AnimatorOwnerGraphRecord& left,
                                    const AnimatorOwnerGraphRecord& right) {
    return SameDirectPlayableTopology(left,right)&&left.entryAddress==right.entryAddress&&
           left.entryPlayable==right.entryPlayable&&left.entryPortRaw==right.entryPortRaw;
}

static bool CoherentDeallocatedNullClip(const AnimatorOwnerGraphRecord& record) {
    return record.clip108==0&&(record.raw9497&0xFFu)==0u&&record.rawA4A7==0u&&
           record.bindingA8==0&&record.bindingAC==0&&record.bindingB0==0&&record.bindingB4==0&&
           record.bindingCachePresence==0&&record.bindingACInner==0&&record.bindingACCount==0&&
           record.bindingACHash==0&&record.bindingB0Inner==0&&record.bindingB0Count==0&&
           record.bindingB0Hash==0&&record.bindingB4Hash==0;
}

static bool SameAlreadyNullClipStaticState(const AnimatorOwnerGraphRecord& current,
                                           const AnimatorOwnerGraphRecord& target) {
    // Playable-time restoration owns flags7C, the two clocks, clipCacheC0, and
    // clipInternalWeightBits.  The paired finalizer owns dirty9093 and
    // clipFlags10C.  Everything else must already be byte-for-byte equivalent.
    return current.recordKind==target.recordKind&&current.layerIndex==target.layerIndex&&
        current.stateMachineIndex==target.stateMachineIndex&&current.branchIndex==target.branchIndex&&
        current.inputIndex==target.inputIndex&&SameDirectPlayableTopology(current,target)&&
        current.rawA0A3==target.rawA0A3&&current.rawA4A7==target.rawA4A7&&
        current.word50==target.word50&&current.clip108==target.clip108&&
        current.entryAddress==target.entryAddress&&current.entryWeightBits==target.entryWeightBits&&
        current.entryPlayable==target.entryPlayable&&current.entryPortRaw==target.entryPortRaw&&
        current.resolverOrigin==target.resolverOrigin&&current.resolverResult==target.resolverResult&&
        current.resolverDepth==target.resolverDepth&&current.resolverStatus==target.resolverStatus&&
        current.duration38Low==target.duration38Low&&current.duration38High==target.duration38High&&
        current.mode80==target.mode80&&current.raw9497==target.raw9497&&
        current.bindingA8==target.bindingA8&&current.bindingAC==target.bindingAC&&
        current.bindingB0==target.bindingB0&&current.bindingB4==target.bindingB4&&
        current.clipCacheBC==target.clipCacheBC&&
        current.bindingCachePresence==target.bindingCachePresence&&
        current.bindingACInner==target.bindingACInner&&current.bindingACCount==target.bindingACCount&&
        current.bindingACHash==target.bindingACHash&&current.bindingB0Inner==target.bindingB0Inner&&
        current.bindingB0Count==target.bindingB0Count&&current.bindingB0Hash==target.bindingB0Hash&&
        current.bindingB4Hash==target.bindingB4Hash;
}

static uint32_t CountOwnerEntryAddress(const AnimatorOwnerGraphRecord* records,uint32_t count,
                                       uintptr_t address) {
    uint32_t matches=0;
    for(uint32_t i=0;i<count;++i)if(records[i].entryAddress==address)++matches;
    return matches;
}

static uint32_t CountOwnerChildNode(const AnimatorOwnerGraphRecord* records,uint32_t count,
                                    uintptr_t playable) {
    uint32_t matches=0;
    for(uint32_t i=0;i<count;++i)
        if(records[i].recordKind==OwnerChildNode&&records[i].self==playable)++matches;
    return matches;
}

// Mirrors AnimationPlayable::RootByType(this,0) only far enough to prove the
// exact ownership chain used by the direct branch-1 SetClip(null) operation.
// The accepted path is clip -> branch mixer -> transition mixer -> layer mixer
// -> controller and must resolve to that same controller object.
static bool ResolveDirectSetClipRoot(uintptr_t unityBase,uintptr_t origin,uintptr_t controller,
                                     uintptr_t& root,AnimatorTargetNullClipReceipt* receipt,
                                     const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
                                     uint32_t failureRecord) {
    root=0;
    static const uint32_t expectedVtables[]={
        kAnimationClipPlayableVtableRva,kAnimationMixerPlayableVtableRva,kMixerPlayableVtableRva,
        kAnimationLayerMixerPlayableVtableRva,kAnimatorControllerPlayableVtableRva
    };
    uintptr_t seen[sizeof(expectedVtables)/sizeof(expectedVtables[0])]={};
    uintptr_t node=origin;
    for(uint32_t depth=0;depth<sizeof(expectedVtables)/sizeof(expectedVtables[0]);++depth){
        const uint32_t readablePlayableBytes=
            expectedVtables[depth]==kAnimationMixerPlayableVtableRva?0xA0u:0xA8u;
        if(!node||node>UINTPTR_MAX-0x50u||
           !Readable(reinterpret_cast<const void*>(node),readablePlayableBytes)||
           *reinterpret_cast<const uintptr_t*>(node)!=unityBase+expectedVtables[depth]){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,20,
                                     unityBase+expectedVtables[depth],
                                     node&&Readable(reinterpret_cast<const void*>(node),4)?
                                         *reinterpret_cast<const uint32_t*>(node):0);return false;
        }
        for(uint32_t i=0;i<depth;++i)if(seen[i]==node){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,20,0,node);return false;
        }
        seen[depth]=node;
        const uintptr_t internal=*reinterpret_cast<const uintptr_t*>(node+0x10);
        if(!Readable(reinterpret_cast<const void*>(internal),0x30)){
            receipt->result=ResultUnreadable;
            SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,24,1,0);return false;
        }
        const uintptr_t outputs=*reinterpret_cast<const uintptr_t*>(internal+0x20);
        const uint32_t outputCount=*reinterpret_cast<const uint32_t*>(internal+0x28);
        if(outputCount>1u||!Readable(reinterpret_cast<const void*>(outputs),12)){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,48,1u,outputCount);return false;
        }
        const uintptr_t next=*reinterpret_cast<const uintptr_t*>(outputs+4);
        const uint32_t word50=*reinterpret_cast<const uint32_t*>(node+0x50);
        if(!next){
            root=word50==0u?node:0;
        } else if(next==2u){
            root=0;
        } else {
            if(next>UINTPTR_MAX-0x50u||!Readable(reinterpret_cast<const void*>(next+0x50),4)){
                receipt->result=ResultUnreadable;
                SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,112,1,0);return false;
            }
            if(*reinterpret_cast<const uint32_t*>(next+0x50)!=0u)root=node;
            else {node=next;continue;}
        }
        if(root!=controller){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,112,controller,root);return false;
        }
        RootByType nativeResolver=reinterpret_cast<RootByType>(unityBase+kRootByTypeRva);
        const uintptr_t nativeRoot=reinterpret_cast<uintptr_t>(
            nativeResolver(reinterpret_cast<void*>(origin),0u));
        if(nativeRoot!=root){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,112,root,nativeRoot);return false;
        }
        return true;
    }
    receipt->result=ResultTopologyMismatch;
    SetTargetNullClipFailure(receipt,current,currentCount,failureRecord,112,controller,0);return false;
}

static bool BuildDirectClipClearPlan(uintptr_t unityBase,uintptr_t controller,
                                     const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
                                     const AnimatorOwnerGraphRecord* target,uint32_t targetCount,
                                     DirectClipClearPlan* plans,OwnerClearedClip* cleared,uint32_t capacity,
                                     uint32_t& planCount,AnimatorTargetNullClipReceipt* receipt) {
    planCount=0;
    if(currentCount!=targetCount){
        receipt->result=ResultTopologyMismatch;receipt->expectedWord=targetCount;
        receipt->actualWord=currentCount;return false;
    }
    uint32_t currentOuterCount=0,targetOuterCount=0;
    for(uint32_t i=0;i<currentCount;++i)if(current[i].recordKind==OwnerOuterNode)++currentOuterCount;
    for(uint32_t i=0;i<targetCount;++i)if(target[i].recordKind==OwnerOuterNode)++targetOuterCount;
    if(currentOuterCount==0||currentOuterCount!=targetOuterCount){
        receipt->result=ResultTopologyMismatch;receipt->expectedWord=targetOuterCount;
        receipt->actualWord=currentOuterCount;return false;
    }
    for(uint32_t i=0;i<currentCount;++i){
        const AnimatorOwnerGraphRecord& outer=current[i];
        if(outer.recordKind!=OwnerOuterNode)continue;
        const AnimatorOwnerGraphRecord* targetOuter=FindUniqueOwnerRecord(target,targetCount,OwnerOuterNode,
            outer.layerIndex,outer.stateMachineIndex,0xFFFFFFFFu,0xFFFFFFFFu);
        if(!targetOuter||targetOuter->vtable!=unityBase+kMixerPlayableVtableRva||
           !SameDirectPlayableTopology(outer,*targetOuter)){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,i,20,outer.self,targetOuter?targetOuter->self:0);return false;
        }
        const AnimatorOwnerGraphRecord* currentOuterInputs[3]={};
        const AnimatorOwnerGraphRecord* targetOuterInputs[3]={};
        for(uint32_t input=0;input<3;++input){
            currentOuterInputs[input]=FindUniqueOwnerRecord(current,currentCount,OwnerOuterInput,
                outer.layerIndex,outer.stateMachineIndex,0xFFFFFFFFu,input);
            targetOuterInputs[input]=FindUniqueOwnerRecord(target,targetCount,OwnerOuterInput,
                outer.layerIndex,outer.stateMachineIndex,0xFFFFFFFFu,input);
            if(!currentOuterInputs[input]||!targetOuterInputs[input]||
               !SameDirectPlayableTopology(*currentOuterInputs[input],*targetOuterInputs[input])||
               currentOuterInputs[input]->entryAddress!=targetOuterInputs[input]->entryAddress||
               currentOuterInputs[input]->entryPortRaw!=targetOuterInputs[input]->entryPortRaw){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,88,input,0xFFFFFFFFu);return false;
            }
        }
        const uintptr_t current0=currentOuterInputs[0]->entryPlayable;
        const uintptr_t current1=currentOuterInputs[1]->entryPlayable;
        const uintptr_t current2=currentOuterInputs[2]->entryPlayable;
        const uintptr_t target0=targetOuterInputs[0]->entryPlayable;
        const uintptr_t target1=targetOuterInputs[1]->entryPlayable;
        const uintptr_t target2=targetOuterInputs[2]->entryPlayable;
        const bool exact=current0==target0&&current1==target1&&current2==target2;
        const bool rotated=current0==target1&&current1==target0&&current2==target2;
        if(rotated){++receipt->rotatedStateMachineCount;continue;}
        if(!exact){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,i,92,target0,current0);return false;
        }
        ++receipt->exactStateMachineCount;

        const uintptr_t currentBranches[2]={current0,current1};
        const uintptr_t targetBranches[2]={target0,target1};
        for(uint32_t branchIndex=0;branchIndex<2u;++branchIndex){
            const AnimatorOwnerGraphRecord* currentBranch=FindUniqueOwnerRecord(current,currentCount,OwnerBranchNode,
                outer.layerIndex,outer.stateMachineIndex,branchIndex,0xFFFFFFFFu);
            const AnimatorOwnerGraphRecord* targetBranch=FindUniqueOwnerRecord(target,targetCount,OwnerBranchNode,
                outer.layerIndex,outer.stateMachineIndex,branchIndex,0xFFFFFFFFu);
            if(!currentBranch||!targetBranch||currentBranch->self!=currentBranches[branchIndex]||
               targetBranch->self!=targetBranches[branchIndex]||
               currentBranch->vtable!=unityBase+kAnimationMixerPlayableVtableRva||
               targetBranch->vtable!=unityBase+kAnimationMixerPlayableVtableRva||
               !SameDirectPlayableTopology(*currentBranch,*targetBranch)){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,20,targetBranches[branchIndex],
                                         currentBranch?currentBranch->self:0);return false;
            }
            for(uint32_t input=0;input<currentBranch->inputCount;++input){
                const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,OwnerBranchInput,
                    outer.layerIndex,outer.stateMachineIndex,branchIndex,input);
                const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,OwnerBranchInput,
                    outer.layerIndex,outer.stateMachineIndex,branchIndex,input);
            if(!currentInput||!targetInput||!SameDirectEntryTopology(*currentInput,*targetInput)){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,88,input,0xFFFFFFFFu);return false;
            }
            bool duplicate=false;
            const AnimatorOwnerGraphRecord* currentChild=FindOwnerChild(current,currentCount,outer.layerIndex,
                outer.stateMachineIndex,branchIndex,currentInput->entryPlayable,duplicate);
            if(duplicate){receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,20,0,currentInput->entryPlayable);return false;}
            bool targetDuplicate=false;
            const AnimatorOwnerGraphRecord* targetChild=FindOwnerChild(target,targetCount,outer.layerIndex,
                outer.stateMachineIndex,branchIndex,targetInput->entryPlayable,targetDuplicate);
            if(targetDuplicate||!currentChild||!targetChild||
               !SameDirectPlayableTopology(*currentChild,*targetChild)){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,20,
                    targetChild?targetChild->self:0,currentChild?currentChild->self:0);return false;
            }
            if(targetChild->vtable!=unityBase+kAnimationClipPlayableVtableRva){
                if(currentChild->vtable!=targetChild->vtable){receipt->result=ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,
                        static_cast<uint32_t>(currentChild-current),24,targetChild->vtable,currentChild->vtable);return false;}
                continue;
            }
            const uint32_t childRecord=static_cast<uint32_t>(currentChild-current);
            const uint32_t targetRecord=static_cast<uint32_t>(targetChild-target);
            if(currentChild->vtable!=targetChild->vtable){receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,childRecord,24,targetChild->vtable,currentChild->vtable);return false;}
            if(targetChild->clip108!=0){
                if(currentChild->clip108!=targetChild->clip108){receipt->result=ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,childRecord,80,targetChild->clip108,currentChild->clip108);return false;}
                continue;
            }
            if(!CoherentDeallocatedNullClip(*targetChild)){
                receipt->result=ResultInvalidBlob;
                SetTargetNullClipFailure(receipt,target,targetCount,targetRecord,80,0,targetChild->clip108);return false;
            }
            if(currentChild->clip108==0){
                // The separate already-null finalizer validates every static
                // field and either accepts an exact dirty/option pair or plans
                // the one proved OverrideClipPlayables transient.  Do not fold
                // that reversible scalar repair into a SetClip plan.
                if(!CoherentDeallocatedNullClip(*currentChild)){
                    receipt->result=ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,childRecord,64,
                                             targetChild->dirty9093,currentChild->dirty9093);return false;
                }
                continue;
            }
            uint32_t currentOccurrences=0,targetOccurrences=0;
            for(uint32_t record=0;record<currentCount;++record)
                if(current[record].recordKind==OwnerChildNode&&current[record].self==currentChild->self)
                    ++currentOccurrences;
            for(uint32_t record=0;record<targetCount;++record)
                if(target[record].recordKind==OwnerChildNode&&target[record].self==targetChild->self)
                    ++targetOccurrences;
            if(currentOccurrences!=1u||targetOccurrences!=1u){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,childRecord,20,1u,currentOccurrences);return false;
            }
            const uint32_t projectedDirty=(currentChild->dirty9093&0xFF00FFFFu)|0x00010000u;
            // Keep each rejection separately attributable.  These checks are
            // deliberately equivalent to the former compound predicate; the
            // split changes no accepted topology and prevents a true final
            // dirty-word comparison from masking an earlier failed guard.
            if(currentOuterInputs[branchIndex]->entryWeightBits!=0u){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,
                    static_cast<uint32_t>(currentOuterInputs[branchIndex]-current),88,0,
                    currentOuterInputs[branchIndex]->entryWeightBits);return false;
            }
            if(targetOuterInputs[branchIndex]->entryWeightBits!=0u){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,target,targetCount,
                    static_cast<uint32_t>(targetOuterInputs[branchIndex]-target),88,0,
                    targetOuterInputs[branchIndex]->entryWeightBits);return false;
            }
            if(targetInput->entryWeightBits!=0u){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,target,targetCount,
                    static_cast<uint32_t>(targetInput-target),88,0,
                    targetInput->entryWeightBits);return false;
            }
            // The outer branch is already zero in both observations, so a
            // finite retained descendant weight cannot affect the root pose.
            // Restore that stale physical selection to the exact target zero
            // transactionally before SetClip(null).
            if(!ValidWeight(currentInput->entryWeightBits)){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,
                    static_cast<uint32_t>(currentInput-current),88,0,
                    currentInput->entryWeightBits);return false;
            }
            if(input+1u>=currentBranch->inputCount){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,
                    static_cast<uint32_t>(currentBranch-current),40,input+2u,
                    currentBranch->inputCount);return false;
            }
            // UnityPlayer's revision-locked SetClip implementation neither
            // reads nor writes +0x94.  A live clip can therefore legitimately
            // arrive here with either observed lifecycle value; it is not a
            // precondition of the engine-owned SetClip(null) mutation.  The
            // target still has to pass CoherentDeallocatedNullClip above, and
            // the projected/actual captures below retain +0x94 exactly.
            // +0xA4 is likewise not consumed by SetClip.  Preserve its exact
            // current value in OwnerClearedClip so the staged comparison can
            // distinguish a real maintenance change, but do not require that
            // opaque lifecycle field to be nonzero before the call.
            // SetClip writes byte +0x92 to one when the clip changes.  Stage A
            // runs immediately after OverrideClipPlayables, so both zero (the
            // override was already identical) and one (the override changed
            // this clip) are valid, idempotent preimages.  Reject every other
            // byte value, then require the complete projected target word.
            if((currentChild->dirty9093&0x00FE0000u)!=0u){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,childRecord,64,0x00010000u,
                    currentChild->dirty9093&0x00FF0000u);return false;
            }
            if(projectedDirty!=targetChild->dirty9093){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,childRecord,64,
                                         targetChild->dirty9093,projectedDirty);return false;
            }
            if(planCount>=capacity){receipt->result=ResultCapacity;return false;}
            for(uint32_t prior=0;prior<planCount;++prior)if(plans[prior].playable==currentChild->self){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,childRecord,20,0,currentChild->self);return false;
            }
            uintptr_t root=0;
            if(!ResolveDirectSetClipRoot(unityBase,currentChild->self,controller,root,receipt,
                                         current,currentCount,childRecord))return false;
            const uint32_t rootDirty=*reinterpret_cast<const uint32_t*>(root+0x90);
            // Production Stage A invokes this helper immediately after the
            // verified OverrideClipPlayables call. That Unity call leaves the
            // controller root dirty word at exactly 0x01000000. SetClip(null)
            // sets the same bit idempotently; reject every other preimage so a
            // stale or unrelated pending lifecycle state is never admitted.
            if(rootDirty!=0x01000000u){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,childRecord,64,0x01000000u,rootDirty);return false;
            }
            DirectClipClearPlan& plan=plans[planCount];
            plan.layerIndex=outer.layerIndex;plan.stateMachineIndex=outer.stateMachineIndex;
            plan.branchIndex=branchIndex;plan.inputIndex=input;plan.currentRecord=childRecord;
            plan.targetRecord=targetRecord;plan.playable=currentChild->self;
            plan.expectedClip=currentChild->clip108;plan.dirtyRoot=root;
            plan.outerWeightAddress=currentOuterInputs[branchIndex]->entryAddress;
            plan.branchMixer=currentBranch->self;plan.branchInternal=currentBranch->internal;
            plan.branchInputEntries=currentBranch->inputEntries;
            plan.branchInputCount=currentBranch->inputCount;
            plan.branchInputCapacityRaw=currentBranch->inputCapacityRaw;
            plan.inputWeightAddress=currentInput->entryAddress;
            plan.inputPlayable=currentInput->entryPlayable;plan.inputPortRaw=currentInput->entryPortRaw;
            plan.currentInputRecord=static_cast<uint32_t>(currentInput-current);
            plan.inputWeightBefore=currentInput->entryWeightBits;
            plan.playableDirtyBefore=currentChild->dirty9093;
            plan.rootDirtyBefore=rootDirty;
            cleared[planCount].layerIndex=outer.layerIndex;
            cleared[planCount].stateMachineIndex=outer.stateMachineIndex;
            cleared[planCount].targetBranchIndex=branchIndex;
            cleared[planCount].inputIndex=input;
            cleared[planCount].currentRecord=childRecord;
            cleared[planCount].requiresPreClear=0u;
            cleared[planCount].playable=plan.playable;cleared[planCount].dirtyRoot=root;
            cleared[planCount].rootDirtyBefore=rootDirty;
            cleared[planCount].expectedRawA4A7=currentChild->rawA4A7;
            cleared[planCount].preimage=*currentChild;
            ++planCount;
            }
        }
    }
    return true;
}

static bool BuildAlreadyNullFinalizePlans(
    uintptr_t unityBase,const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
    const AnimatorOwnerGraphRecord* target,uint32_t targetCount,
    AlreadyNullClipFinalizePlan* nullPlans,uint32_t nullCapacity,uint32_t& nullCount,
    EmptyBranchOutputFinalizePlan* outputPlans,uint32_t outputCapacity,uint32_t& outputCount,
    AnimatorTargetNullClipReceipt* receipt) {
    nullCount=0;outputCount=0;
    if(currentCount!=targetCount){receipt->result=ResultTopologyMismatch;return false;}
    for(uint32_t i=0;i<currentCount;++i){
        const AnimatorOwnerGraphRecord& outer=current[i];
        if(outer.recordKind!=OwnerOuterNode)continue;
        const AnimatorOwnerGraphRecord* targetOuter=FindUniqueOwnerRecord(target,targetCount,OwnerOuterNode,
            outer.layerIndex,outer.stateMachineIndex,0xFFFFFFFFu,0xFFFFFFFFu);
        if(!targetOuter||!SameDirectPlayableTopology(outer,*targetOuter)){
            receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,i,20,
                targetOuter?static_cast<uint32_t>(targetOuter->self):0u,static_cast<uint32_t>(outer.self));
            return false;
        }
        const AnimatorOwnerGraphRecord* currentOuterInputs[3]={};
        const AnimatorOwnerGraphRecord* targetOuterInputs[3]={};
        for(uint32_t input=0;input<3;++input){
            currentOuterInputs[input]=FindUniqueOwnerRecord(current,currentCount,OwnerOuterInput,
                outer.layerIndex,outer.stateMachineIndex,0xFFFFFFFFu,input);
            targetOuterInputs[input]=FindUniqueOwnerRecord(target,targetCount,OwnerOuterInput,
                outer.layerIndex,outer.stateMachineIndex,0xFFFFFFFFu,input);
            // A settled-transition rotation swaps outer input 0/1 playables
            // while retaining the same entry storage and port topology.  Do
            // not require entryPlayable equality until the exact/rotated
            // classification below; doing so made the rotated skip
            // unreachable and rejected an otherwise supported state machine.
            if(!currentOuterInputs[input]||!targetOuterInputs[input]||
               !SameDirectPlayableTopology(*currentOuterInputs[input],*targetOuterInputs[input])||
               currentOuterInputs[input]->entryAddress!=targetOuterInputs[input]->entryAddress||
               currentOuterInputs[input]->entryPortRaw!=targetOuterInputs[input]->entryPortRaw){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,88,input,0xFFFFFFFFu);return false;
            }
        }
        const bool exact=currentOuterInputs[0]->entryPlayable==targetOuterInputs[0]->entryPlayable&&
            currentOuterInputs[1]->entryPlayable==targetOuterInputs[1]->entryPlayable&&
            currentOuterInputs[2]->entryPlayable==targetOuterInputs[2]->entryPlayable;
        const bool rotated=currentOuterInputs[0]->entryPlayable==targetOuterInputs[1]->entryPlayable&&
            currentOuterInputs[1]->entryPlayable==targetOuterInputs[0]->entryPlayable&&
            currentOuterInputs[2]->entryPlayable==targetOuterInputs[2]->entryPlayable;
        if(rotated)continue;
        if(!exact){receipt->result=ResultTopologyMismatch;
            SetTargetNullClipFailure(receipt,current,currentCount,i,92,
                static_cast<uint32_t>(targetOuterInputs[0]->entryPlayable),
                static_cast<uint32_t>(currentOuterInputs[0]->entryPlayable));return false;}

        for(uint32_t branchIndex=0;branchIndex<2u;++branchIndex){
            const AnimatorOwnerGraphRecord* currentBranch=FindUniqueOwnerRecord(current,currentCount,OwnerBranchNode,
                outer.layerIndex,outer.stateMachineIndex,branchIndex,0xFFFFFFFFu);
            const AnimatorOwnerGraphRecord* targetBranch=FindUniqueOwnerRecord(target,targetCount,OwnerBranchNode,
                outer.layerIndex,outer.stateMachineIndex,branchIndex,0xFFFFFFFFu);
            if(!currentBranch||!targetBranch||
               currentBranch->self!=currentOuterInputs[branchIndex]->entryPlayable||
               targetBranch->self!=targetOuterInputs[branchIndex]->entryPlayable||
               currentBranch->vtable!=unityBase+kAnimationMixerPlayableVtableRva||
               targetBranch->vtable!=unityBase+kAnimationMixerPlayableVtableRva||
               !SameDirectPlayableTopology(*currentBranch,*targetBranch)){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,i,20,
                    targetBranch?static_cast<uint32_t>(targetBranch->self):0u,
                    currentBranch?static_cast<uint32_t>(currentBranch->self):0u);return false;
            }
            const AnimatorOwnerGraphRecord* currentOutput=FindUniqueOwnerRecord(current,currentCount,OwnerBranchOutput,
                outer.layerIndex,outer.stateMachineIndex,branchIndex,0u);
            const AnimatorOwnerGraphRecord* targetOutput=FindUniqueOwnerRecord(target,targetCount,OwnerBranchOutput,
                outer.layerIndex,outer.stateMachineIndex,branchIndex,0u);
            // Captured branch-output port storage is opaque lifecycle state and
            // is not universally zero (a live valid branch observed 0x4233D244).
            // Require it to match the checkpoint through
            // SameDirectEntryTopology and preserve it in the live preflight;
            // only the independently addressed weight word is ever written.
            if(!currentOutput||!targetOutput||currentBranch->inputCount==0u||
               currentBranch->outputCount!=1u||targetBranch->outputCount!=1u||
               currentOutput->entryAddress!=currentBranch->outputEntries||
               targetOutput->entryAddress!=targetBranch->outputEntries||
               currentOutput->entryPlayable!=outer.self||targetOutput->entryPlayable!=targetOuter->self||
               !SameDirectEntryTopology(*currentOutput,*targetOutput)){
                receipt->result=ResultTopologyMismatch;
                SetTargetNullClipFailure(receipt,current,currentCount,
                    currentOutput?static_cast<uint32_t>(currentOutput-current):i,88,0u,
                    currentOutput?currentOutput->entryWeightBits:0xFFFFFFFFu);return false;
            }

            // A branch is inactive when every authoritative destination-input
            // weight is zero.  Its children are not required to all be clip
            // playables: Unity also keeps zero-weight placeholder playables in
            // this topology.  Those placeholders do not make the reciprocal
            // branch-output weight authoritative.
            bool safeEmpty=true;
            for(uint32_t input=0;input<currentBranch->inputCount;++input){
                const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,OwnerBranchInput,
                    outer.layerIndex,outer.stateMachineIndex,branchIndex,input);
                const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,OwnerBranchInput,
                    outer.layerIndex,outer.stateMachineIndex,branchIndex,input);
                if(!currentInput||!targetInput||!SameDirectEntryTopology(*currentInput,*targetInput)){
                    receipt->result=ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,
                        static_cast<uint32_t>(currentBranch-current),88,input,0xFFFFFFFFu);return false;
                }
                bool currentDuplicate=false,targetDuplicate=false;
                const AnimatorOwnerGraphRecord* currentChild=FindOwnerChild(current,currentCount,outer.layerIndex,
                    outer.stateMachineIndex,branchIndex,currentInput->entryPlayable,currentDuplicate);
                const AnimatorOwnerGraphRecord* targetChild=FindOwnerChild(target,targetCount,outer.layerIndex,
                    outer.stateMachineIndex,branchIndex,targetInput->entryPlayable,targetDuplicate);
                if(currentDuplicate||targetDuplicate||!currentChild||!targetChild||
                   !SameDirectPlayableTopology(*currentChild,*targetChild)){
                    receipt->result=ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,
                        currentChild?static_cast<uint32_t>(currentChild-current):static_cast<uint32_t>(currentBranch-current),
                        20,targetChild?static_cast<uint32_t>(targetChild->self):0u,
                        currentChild?static_cast<uint32_t>(currentChild->self):0u);return false;
                }
                const bool bothNull=currentChild->vtable==unityBase+kAnimationClipPlayableVtableRva&&
                    targetChild->vtable==unityBase+kAnimationClipPlayableVtableRva&&
                    currentChild->clip108==0&&targetChild->clip108==0&&
                    CoherentDeallocatedNullClip(*currentChild)&&CoherentDeallocatedNullClip(*targetChild);
                if(currentInput->entryWeightBits!=0u||targetInput->entryWeightBits!=0u)
                    safeEmpty=false;
                if(bothNull&&!SameAlreadyNullClipStaticState(*currentChild,*targetChild)){
                    receipt->result=ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,
                        static_cast<uint32_t>(currentChild-current),76,targetChild->word50,currentChild->word50);
                    return false;
                }
            }

            if(currentOutput->entryWeightBits!=targetOutput->entryWeightBits){
                const uint32_t outputRecord=static_cast<uint32_t>(currentOutput-current);
                if(!safeEmpty||currentOutput->entryWeightBits!=0x3F800000u||
                   targetOutput->entryWeightBits!=0u||
                   CountOwnerEntryAddress(current,currentCount,currentOutput->entryAddress)!=1u||
                   CountOwnerEntryAddress(target,targetCount,targetOutput->entryAddress)!=1u||
                   outputCount>=outputCapacity){
                    receipt->result=outputCount>=outputCapacity?ResultCapacity:ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,outputRecord,88,
                        targetOutput->entryWeightBits,currentOutput->entryWeightBits);return false;
                }
                EmptyBranchOutputFinalizePlan& plan=outputPlans[outputCount++];
                plan.layerIndex=outer.layerIndex;plan.stateMachineIndex=outer.stateMachineIndex;
                plan.branchIndex=branchIndex;plan.currentRecord=outputRecord;
                plan.branchMixer=currentBranch->self;plan.branchInternal=currentBranch->internal;
                plan.outputEntries=currentBranch->outputEntries;plan.outputCount=currentBranch->outputCount;
                plan.outputCapacityRaw=currentBranch->outputCapacityRaw;
                plan.outputAddress=currentOutput->entryAddress;plan.outputPlayable=currentOutput->entryPlayable;
                plan.outputPortRaw=currentOutput->entryPortRaw;plan.weightBefore=currentOutput->entryWeightBits;
                plan.weightTarget=targetOutput->entryWeightBits;
            }

            for(uint32_t input=0;input<currentBranch->inputCount;++input){
                const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,OwnerBranchInput,
                    outer.layerIndex,outer.stateMachineIndex,branchIndex,input);
                const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,OwnerBranchInput,
                    outer.layerIndex,outer.stateMachineIndex,branchIndex,input);
                bool currentDuplicate=false,targetDuplicate=false;
                const AnimatorOwnerGraphRecord* currentChild=FindOwnerChild(current,currentCount,outer.layerIndex,
                    outer.stateMachineIndex,branchIndex,currentInput->entryPlayable,currentDuplicate);
                const AnimatorOwnerGraphRecord* targetChild=FindOwnerChild(target,targetCount,outer.layerIndex,
                    outer.stateMachineIndex,branchIndex,targetInput->entryPlayable,targetDuplicate);
                if(currentChild->vtable!=unityBase+kAnimationClipPlayableVtableRva||
                   targetChild->vtable!=unityBase+kAnimationClipPlayableVtableRva||
                   currentChild->clip108!=0||targetChild->clip108!=0||
                   !CoherentDeallocatedNullClip(*currentChild)||
                   !CoherentDeallocatedNullClip(*targetChild))continue;
                if(currentChild->dirty9093==targetChild->dirty9093&&
                   currentChild->clipFlags10C==targetChild->clipFlags10C)continue;
                const uint32_t childRecord=static_cast<uint32_t>(currentChild-current);
                if(!safeEmpty||currentChild->dirty9093!=0x00010000u||targetChild->dirty9093!=0u||
                   currentChild->clipFlags10C!=0u||targetChild->clipFlags10C!=1u||
                   CountOwnerChildNode(current,currentCount,currentChild->self)!=1u||
                   CountOwnerChildNode(target,targetCount,targetChild->self)!=1u||nullCount>=nullCapacity){
                    receipt->result=nullCount>=nullCapacity?ResultCapacity:ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,childRecord,64,
                        targetChild->dirty9093,currentChild->dirty9093);return false;
                }
                for(uint32_t prior=0;prior<nullCount;++prior)if(nullPlans[prior].playable==currentChild->self){
                    receipt->result=ResultTopologyMismatch;
                    SetTargetNullClipFailure(receipt,current,currentCount,childRecord,20,0u,
                        static_cast<uint32_t>(currentChild->self));return false;
                }
                AlreadyNullClipFinalizePlan& plan=nullPlans[nullCount++];
                plan.layerIndex=outer.layerIndex;plan.stateMachineIndex=outer.stateMachineIndex;
                plan.branchIndex=branchIndex;plan.inputIndex=input;plan.currentRecord=childRecord;
                plan.playable=currentChild->self;plan.dirtyBefore=currentChild->dirty9093;
                plan.dirtyTarget=targetChild->dirty9093;plan.clipFlagsBefore=currentChild->clipFlags10C;
                plan.clipFlagsTarget=targetChild->clipFlags10C;
            }
        }
    }
    return true;
}

static bool DirectClipInputTopologyExact(uintptr_t unityBase,const DirectClipClearPlan& plan) {
    if(!plan.branchMixer||!plan.branchInternal||!plan.branchInputEntries||
       plan.inputIndex>=plan.branchInputCount||plan.inputIndex>UINT32_MAX/12u||
       plan.branchInputEntries>UINTPTR_MAX-plan.inputIndex*12u||
       !Readable(reinterpret_cast<const void*>(plan.branchMixer),0x14)||
       *reinterpret_cast<const uintptr_t*>(plan.branchMixer)!=unityBase+kAnimationMixerPlayableVtableRva||
       *reinterpret_cast<const uintptr_t*>(plan.branchMixer+0x10)!=plan.branchInternal||
       !Readable(reinterpret_cast<const void*>(plan.branchInternal),0x20)||
       *reinterpret_cast<const uintptr_t*>(plan.branchInternal+0x10)!=plan.branchInputEntries||
       *reinterpret_cast<const uint32_t*>(plan.branchInternal+0x18)!=plan.branchInputCount||
       *reinterpret_cast<const uint32_t*>(plan.branchInternal+0x1C)!=plan.branchInputCapacityRaw||
       plan.branchInputEntries+plan.inputIndex*12u!=plan.inputWeightAddress||
       !Readable(reinterpret_cast<const void*>(plan.inputWeightAddress),12)||
       !Writable(reinterpret_cast<const void*>(plan.inputWeightAddress),4)||
       *reinterpret_cast<const uintptr_t*>(plan.inputWeightAddress+4)!=plan.inputPlayable||
       *reinterpret_cast<const uint32_t*>(plan.inputWeightAddress+8)!=plan.inputPortRaw)return false;
    return true;
}

static bool ApplyDirectClipInputWeights(uintptr_t unityBase,const DirectClipClearPlan* plans,
                                        uint32_t count,bool restorePreimage,SetInputWeight setter,
                                        AnimatorTargetNullClipReceipt* receipt) {
    for(uint32_t i=0;i<count;++i){
        const DirectClipClearPlan& plan=plans[i];
        if(restorePreimage&&Readable(reinterpret_cast<const void*>(plan.inputWeightAddress),4)&&
           *reinterpret_cast<const uint32_t*>(plan.inputWeightAddress)==plan.inputWeightBefore)continue;
        if(!DirectClipInputTopologyExact(unityBase,plan)){
            receipt->result=ResultTopologyMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;
            receipt->failureRecord=plan.currentInputRecord;receipt->failureByteOffset=88;
            receipt->expectedWord=plan.inputWeightBefore;receipt->actualWord=0;return false;
        }
        const uint32_t live=*reinterpret_cast<const uint32_t*>(plan.inputWeightAddress);
        const uint32_t desired=restorePreimage?plan.inputWeightBefore:0u;
        if(live==desired)continue;
        // Before clip clearing, rollback may see either an already-zeroed entry
        // or an untouched exact preimage. Refuse every third state.
        const uint32_t admitted=restorePreimage?0u:plan.inputWeightBefore;
        if(live!=admitted){
            receipt->result=ResultWriteMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;
            receipt->failureRecord=plan.currentInputRecord;receipt->failureByteOffset=88;
            receipt->expectedWord=admitted;receipt->actualWord=live;return false;
        }
        union { uint32_t bits;float value; } weight={desired};
        setter(reinterpret_cast<void*>(plan.branchMixer),plan.inputIndex,weight.value);
        const uint32_t after=*reinterpret_cast<const uint32_t*>(plan.inputWeightAddress);
        if(after!=desired){
            receipt->result=ResultWriteMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;
            receipt->failureRecord=plan.currentInputRecord;receipt->failureByteOffset=88;
            receipt->expectedWord=desired;receipt->actualWord=after;return false;
        }
    }
    return true;
}

static bool AlreadyNullFinalizeTopologyExact(uintptr_t unityBase,
                                             const AlreadyNullClipFinalizePlan& plan,
                                             bool targetState) {
    if(!plan.playable||!Readable(reinterpret_cast<const void*>(plan.playable),0x110u)||
       !Writable(reinterpret_cast<void*>(plan.playable+0x90u),4u)||
       !Writable(reinterpret_cast<void*>(plan.playable+0x10Cu),4u)||
       *reinterpret_cast<const uintptr_t*>(plan.playable)!=unityBase+kAnimationClipPlayableVtableRva||
       *reinterpret_cast<const uintptr_t*>(plan.playable+0x108u)!=0u)return false;
    const uint32_t expectedDirty=targetState?plan.dirtyTarget:plan.dirtyBefore;
    const uint32_t expectedFlags=targetState?plan.clipFlagsTarget:plan.clipFlagsBefore;
    return *reinterpret_cast<const uint32_t*>(plan.playable+0x90u)==expectedDirty&&
        *reinterpret_cast<const uint32_t*>(plan.playable+0x10Cu)==expectedFlags;
}

static bool EmptyOutputFinalizeTopologyExact(uintptr_t unityBase,
                                             const EmptyBranchOutputFinalizePlan& plan,
                                             bool targetState) {
    if(!plan.branchMixer||!plan.branchInternal||!plan.outputEntries||plan.outputCount!=1u||
       plan.outputAddress!=plan.outputEntries||
       !Readable(reinterpret_cast<const void*>(plan.branchMixer),0x14u)||
       *reinterpret_cast<const uintptr_t*>(plan.branchMixer)!=unityBase+kAnimationMixerPlayableVtableRva||
       *reinterpret_cast<const uintptr_t*>(plan.branchMixer+0x10u)!=plan.branchInternal||
       !Readable(reinterpret_cast<const void*>(plan.branchInternal),0x30u)||
       *reinterpret_cast<const uintptr_t*>(plan.branchInternal+0x20u)!=plan.outputEntries||
       *reinterpret_cast<const uint32_t*>(plan.branchInternal+0x28u)!=plan.outputCount||
       *reinterpret_cast<const uint32_t*>(plan.branchInternal+0x2Cu)!=plan.outputCapacityRaw||
       !Readable(reinterpret_cast<const void*>(plan.outputAddress),12u)||
       !Writable(reinterpret_cast<void*>(plan.outputAddress),4u)||
       *reinterpret_cast<const uintptr_t*>(plan.outputAddress+4u)!=plan.outputPlayable||
       *reinterpret_cast<const uint32_t*>(plan.outputAddress+8u)!=plan.outputPortRaw)return false;
    const uint32_t expected=targetState?plan.weightTarget:plan.weightBefore;
    return *reinterpret_cast<const uint32_t*>(plan.outputAddress)==expected;
}

static bool ApplyFinalizeScalars(uintptr_t unityBase,
                                 const AlreadyNullClipFinalizePlan* nullPlans,uint32_t nullCount,
                                 const EmptyBranchOutputFinalizePlan* outputPlans,uint32_t outputCount,
                                 AnimatorTargetNullClipReceipt* receipt) {
    for(uint32_t i=0;i<outputCount;++i){
        const EmptyBranchOutputFinalizePlan& plan=outputPlans[i];
        if(!EmptyOutputFinalizeTopologyExact(unityBase,plan,false)){
            receipt->result=ResultTopologyMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
            receipt->failureByteOffset=88;receipt->expectedWord=plan.weightBefore;
            receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.outputAddress),4u)?
                *reinterpret_cast<const uint32_t*>(plan.outputAddress):0u;return false;
        }
        *reinterpret_cast<uint32_t*>(plan.outputAddress)=plan.weightTarget;
        if(!EmptyOutputFinalizeTopologyExact(unityBase,plan,true)){
            receipt->result=ResultWriteMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
            receipt->failureByteOffset=88;receipt->expectedWord=plan.weightTarget;
            receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.outputAddress),4u)?
                *reinterpret_cast<const uint32_t*>(plan.outputAddress):0u;return false;
        }
        ++receipt->completedScalarWriteCount;
    }
    for(uint32_t i=0;i<nullCount;++i){
        const AlreadyNullClipFinalizePlan& plan=nullPlans[i];
        if(!AlreadyNullFinalizeTopologyExact(unityBase,plan,false)){
            receipt->result=ResultTopologyMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
            receipt->failureByteOffset=64;receipt->expectedWord=plan.dirtyBefore;
            receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.playable+0x90u),4u)?
                *reinterpret_cast<const uint32_t*>(plan.playable+0x90u):0u;return false;
        }
        *reinterpret_cast<uint32_t*>(plan.playable+0x90u)=plan.dirtyTarget;
        if(*reinterpret_cast<const uint32_t*>(plan.playable+0x90u)!=plan.dirtyTarget){
            receipt->result=ResultWriteMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
            receipt->failureByteOffset=64;receipt->expectedWord=plan.dirtyTarget;
            receipt->actualWord=*reinterpret_cast<const uint32_t*>(plan.playable+0x90u);return false;
        }
        ++receipt->completedScalarWriteCount;
        *reinterpret_cast<uint32_t*>(plan.playable+0x10Cu)=plan.clipFlagsTarget;
        if(!AlreadyNullFinalizeTopologyExact(unityBase,plan,true)){
            receipt->result=ResultWriteMismatch;receipt->failureLayer=plan.layerIndex;
            receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
            receipt->failureByteOffset=176;receipt->expectedWord=plan.clipFlagsTarget;
            receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.playable+0x10Cu),4u)?
                *reinterpret_cast<const uint32_t*>(plan.playable+0x10Cu):0u;return false;
        }
        ++receipt->completedScalarWriteCount;
    }
    return true;
}

static bool RollbackFinalizeScalars(
    const AlreadyNullClipFinalizePlan* nullPlans,uint32_t nullCount,
    const EmptyBranchOutputFinalizePlan* outputPlans,uint32_t outputCount,
    AnimatorTargetNullClipReceipt* receipt) {
    bool exact=true;
    for(uint32_t i=nullCount;i>0;--i){
        const AlreadyNullClipFinalizePlan& plan=nullPlans[i-1u];
        if(!Writable(reinterpret_cast<void*>(plan.playable+0x90u),4u)||
           !Writable(reinterpret_cast<void*>(plan.playable+0x10Cu),4u)){exact=false;continue;}
        uint32_t liveFlags=*reinterpret_cast<const uint32_t*>(plan.playable+0x10Cu);
        if(liveFlags==plan.clipFlagsTarget){
            *reinterpret_cast<uint32_t*>(plan.playable+0x10Cu)=plan.clipFlagsBefore;
            if(*reinterpret_cast<const uint32_t*>(plan.playable+0x10Cu)==plan.clipFlagsBefore)
                ++receipt->rolledBackScalarWriteCount;
            else exact=false;
        } else if(liveFlags!=plan.clipFlagsBefore)exact=false;
        uint32_t liveDirty=*reinterpret_cast<const uint32_t*>(plan.playable+0x90u);
        if(liveDirty==plan.dirtyTarget){
            *reinterpret_cast<uint32_t*>(plan.playable+0x90u)=plan.dirtyBefore;
            if(*reinterpret_cast<const uint32_t*>(plan.playable+0x90u)==plan.dirtyBefore)
                ++receipt->rolledBackScalarWriteCount;
            else exact=false;
        } else if(liveDirty!=plan.dirtyBefore)exact=false;
    }
    for(uint32_t i=outputCount;i>0;--i){
        const EmptyBranchOutputFinalizePlan& plan=outputPlans[i-1u];
        if(!Writable(reinterpret_cast<void*>(plan.outputAddress),4u)){exact=false;continue;}
        const uint32_t live=*reinterpret_cast<const uint32_t*>(plan.outputAddress);
        if(live==plan.weightTarget){
            *reinterpret_cast<uint32_t*>(plan.outputAddress)=plan.weightBefore;
            if(*reinterpret_cast<const uint32_t*>(plan.outputAddress)==plan.weightBefore)
                ++receipt->rolledBackScalarWriteCount;
            else exact=false;
        } else if(live!=plan.weightBefore)exact=false;
    }
    if(!exact)receipt->scalarRollbackFailure=1u;
    return exact;
}

static void CopyOwnerFailure(const AnimatorOwnerGraphReceipt& owner,
                             AnimatorEndTransitionReceipt* receipt) {
    receipt->result=owner.result;
    receipt->lastError=owner.lastError;
    receipt->failureRecord=owner.failureRecord;
}

static bool ValidateSettledTopology(const AnimatorTransitionTopologyLayer* rows,uint32_t count,
                                    AnimatorEndTransitionReceipt* receipt) {
    for(uint32_t i=0;i<count;++i){
        const AnimatorTransitionTopologyLayer& row=rows[i];
        uint32_t offset=0,expected=0,actual=row.index;
        if(row.index!=i){expected=i;}
        else if(!row.live){offset=4;actual=0;expected=1;}
        else if(!row.node){offset=8;actual=0;expected=1;}
        else if(row.blobInterruptedRaw){offset=12;actual=row.blobInterruptedRaw;}
        else if(row.nodeStartArgumentRaw){offset=16;actual=row.nodeStartArgumentRaw;}
        else if(row.liveModeRaw!=2u){offset=20;expected=2u;actual=row.liveModeRaw;}
        else if(row.liveSecondaryArgumentRaw){offset=24;actual=row.liveSecondaryArgumentRaw;}
        else continue;
        receipt->result=ResultTopologyMismatch;
        receipt->failureLayer=row.index;receipt->failureRecord=i;receipt->failureByteOffset=offset;
        receipt->expectedWord=expected;receipt->actualWord=actual;
        return false;
    }
    return true;
}

static bool VerifyRotationPreimages(uintptr_t unityBase,const OwnerRotationPlan* plans,uint32_t count,
                                    uintptr_t graph,AnimatorEndTransitionReceipt* receipt) {
    for(uint32_t i=0;i<count;++i){
        const OwnerRotationPlan& plan=plans[i];
        receipt->failureLayer=plan.layerIndex;receipt->failureStateMachine=plan.stateMachineIndex;
        if(!Readable(reinterpret_cast<const void*>(plan.outer),0xA8)||
           *reinterpret_cast<const uintptr_t*>(plan.outer)!=unityBase+kMixerPlayableVtableRva||
           *reinterpret_cast<const uintptr_t*>(plan.outer+0x78)!=graph||
           *reinterpret_cast<const uint32_t*>(plan.outer+0xA0)!=2u||
           *reinterpret_cast<const uint8_t*>(plan.outer+0xA4)!=0u||
           *reinterpret_cast<const uint32_t*>(plan.outer+0x90)!=0u){
            receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=0;
            receipt->expectedWord=2u;receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.outer),4)?
                *reinterpret_cast<const uint32_t*>(plan.outer):0;return false;
        }
        const uintptr_t outerInternal=*reinterpret_cast<const uintptr_t*>(plan.outer+0x10);
        if(!Readable(reinterpret_cast<const void*>(outerInternal),0x30)||
           *reinterpret_cast<const uintptr_t*>(outerInternal+0x10)!=plan.outerEntries||
           *reinterpret_cast<const uint32_t*>(outerInternal+0x18)!=3u||
           !Readable(reinterpret_cast<const void*>(plan.outerEntries),36)||
           *reinterpret_cast<const uintptr_t*>(plan.outerEntries+4)!=plan.oldBranch0||
           *reinterpret_cast<const uintptr_t*>(plan.outerEntries+16)!=plan.oldBranch1||
           *reinterpret_cast<const uintptr_t*>(plan.outerEntries+28)!=plan.oldBranch2||
           *reinterpret_cast<const uint32_t*>(plan.outerEntries+8)!=0u||
           *reinterpret_cast<const uint32_t*>(plan.outerEntries+20)!=0u){
            receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=36;
            receipt->expectedWord=plan.oldBranch0;receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.outerEntries+4),4)?
                *reinterpret_cast<const uint32_t*>(plan.outerEntries+4):0;return false;
        }
        const uintptr_t branches[2]={plan.oldBranch0,plan.oldBranch1};
        const uintptr_t outputs[2]={plan.oldBranch0Output0,plan.oldBranch1Output0};
        for(uint32_t branch=0;branch<2;++branch){
            if(!Readable(reinterpret_cast<const void*>(branches[branch]),0xA0)||
               *reinterpret_cast<const uintptr_t*>(branches[branch])!=unityBase+kAnimationMixerPlayableVtableRva||
               *reinterpret_cast<const uintptr_t*>(branches[branch]+0x78)!=graph||
               *reinterpret_cast<const uint32_t*>(branches[branch]+0x90)!=0u){
                receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=20;
                receipt->expectedWord=unityBase+kAnimationMixerPlayableVtableRva;
                receipt->actualWord=Readable(reinterpret_cast<const void*>(branches[branch]),4)?
                    *reinterpret_cast<const uint32_t*>(branches[branch]):0;return false;
            }
            const uintptr_t internal=*reinterpret_cast<const uintptr_t*>(branches[branch]+0x10);
            if(!Readable(reinterpret_cast<const void*>(internal),0x30)||
               *reinterpret_cast<const uintptr_t*>(internal+0x20)!=outputs[branch]||
               *reinterpret_cast<const uint32_t*>(internal+0x28)==0u||
               !Readable(reinterpret_cast<const void*>(outputs[branch]),12)||
               *reinterpret_cast<const uintptr_t*>(outputs[branch]+4)!=plan.outer){
                receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=48;
                receipt->expectedWord=plan.outer;receipt->actualWord=Readable(reinterpret_cast<const void*>(outputs[branch]+4),4)?
                    *reinterpret_cast<const uint32_t*>(outputs[branch]+4):0;return false;
            }
            if(branch==0&&(*reinterpret_cast<const uintptr_t*>(internal+0x10)!=plan.oldBranch0InputEntries||
                           *reinterpret_cast<const uint32_t*>(internal+0x18)!=plan.oldBranch0InputCount)){
                receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=36;
                receipt->expectedWord=plan.oldBranch0InputEntries;
                receipt->actualWord=*reinterpret_cast<const uint32_t*>(internal+0x10);return false;
            }
            if(branch==1&&(*reinterpret_cast<const uintptr_t*>(internal+0x10)!=plan.oldBranch1InputEntries||
                           *reinterpret_cast<const uint32_t*>(internal+0x18)!=plan.oldBranch1InputCount)){
                receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=36;
                receipt->expectedWord=plan.oldBranch1InputEntries;
                receipt->actualWord=*reinterpret_cast<const uint32_t*>(internal+0x10);return false;
            }
        }
    }
    receipt->failureLayer=0xFFFFFFFFu;receipt->failureStateMachine=0xFFFFFFFFu;
    return true;
}

static bool AppendOwnerWeightPlan(uintptr_t expectedVtable,uint32_t role,
                                  const AnimatorOwnerGraphRecord* currentBase,uint32_t currentCount,
                                  const AnimatorOwnerGraphRecord& current,
                                  const AnimatorOwnerGraphRecord& target,
                                  uintptr_t expectedPlayableBefore,uintptr_t expectedPlayableAfter,
                                  uint32_t weightAfterEnd,
                                  OwnerWeightPlan* output,uint32_t capacity,uint32_t& count,
                                  AnimatorEndTransitionReceipt* receipt) {
    const uint32_t currentRecord=static_cast<uint32_t>(&current-currentBase);
    if(currentRecord>=currentCount||count>=capacity||current.vtable!=expectedVtable||
       target.vtable!=expectedVtable||current.recordKind!=target.recordKind||
       current.self!=target.self||current.internal!=target.internal||
       current.inputEntries!=target.inputEntries||current.inputCount!=target.inputCount||
       current.inputCapacityRaw!=target.inputCapacityRaw||current.inputIndex!=target.inputIndex||
       current.entryAddress!=target.entryAddress||current.entryPortRaw!=target.entryPortRaw||
       current.entryPlayable!=expectedPlayableBefore||target.entryPlayable!=expectedPlayableAfter||
       current.inputIndex>=current.inputCount||current.inputIndex>UINT32_MAX/12u||
       current.inputEntries>UINTPTR_MAX-current.inputIndex*12u||
       current.entryAddress!=current.inputEntries+current.inputIndex*12u||
       !ValidWeight(current.entryWeightBits)||!ValidWeight(weightAfterEnd)||
       !ValidWeight(target.entryWeightBits)){
        receipt->result=count>=capacity?ResultCapacity:ResultTopologyMismatch;
        SetOwnerFailure(receipt,currentBase,currentCount,currentRecord,88,
                        target.entryWeightBits,current.entryWeightBits);return false;
    }
    for(uint32_t i=0;i<count;++i)if(output[i].entryAddress==current.entryAddress){
        receipt->result=ResultTopologyMismatch;
        SetOwnerFailure(receipt,currentBase,currentCount,currentRecord,84,0,current.entryAddress);return false;
    }
    OwnerWeightPlan& plan=output[count++];
    plan.layerIndex=current.layerIndex;plan.stateMachineIndex=current.stateMachineIndex;
    plan.role=role;plan.inputIndex=current.inputIndex;plan.currentRecord=currentRecord;
    plan.mixer=current.self;plan.mixerVtable=current.vtable;plan.mixerInternal=current.internal;
    plan.inputEntries=current.inputEntries;plan.inputCount=current.inputCount;
    plan.inputCapacityRaw=current.inputCapacityRaw;plan.entryAddress=current.entryAddress;
    plan.entryPlayableBefore=expectedPlayableBefore;plan.entryPlayableAfter=expectedPlayableAfter;
    plan.entryPortRaw=current.entryPortRaw;plan.weightBefore=current.entryWeightBits;
    plan.weightAfterEnd=weightAfterEnd;plan.weightTarget=target.entryWeightBits;
    return true;
}

static bool BuildOwnerWeightPlans(uintptr_t unityBase,const OwnerRotationPlan* rotations,
                                  uint32_t rotationCount,
                                  const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
                                  const AnimatorOwnerGraphRecord* target,uint32_t targetCount,
                                  OwnerWeightPlan* output,uint32_t capacity,uint32_t& count,
                                  AnimatorEndTransitionReceipt* receipt) {
    count=0;
    for(uint32_t rotationIndex=0;rotationIndex<rotationCount;++rotationIndex){
        const OwnerRotationPlan& rotation=rotations[rotationIndex];
        const AnimatorOwnerGraphRecord* currentBranch1=FindUniqueOwnerRecord(current,currentCount,
            OwnerBranchNode,rotation.layerIndex,rotation.stateMachineIndex,1u,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* targetBranch0=FindUniqueOwnerRecord(target,targetCount,
            OwnerBranchNode,rotation.layerIndex,rotation.stateMachineIndex,0u,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* currentBranch0=FindUniqueOwnerRecord(current,currentCount,
            OwnerBranchNode,rotation.layerIndex,rotation.stateMachineIndex,0u,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* targetBranch1=FindUniqueOwnerRecord(target,targetCount,
            OwnerBranchNode,rotation.layerIndex,rotation.stateMachineIndex,1u,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* currentOuter=FindUniqueOwnerRecord(current,currentCount,
            OwnerOuterNode,rotation.layerIndex,rotation.stateMachineIndex,0xFFFFFFFFu,0xFFFFFFFFu);
        const AnimatorOwnerGraphRecord* targetOuter=FindUniqueOwnerRecord(target,targetCount,
            OwnerOuterNode,rotation.layerIndex,rotation.stateMachineIndex,0xFFFFFFFFu,0xFFFFFFFFu);
        if(!currentBranch1||!targetBranch0||!currentBranch0||!targetBranch1||!currentOuter||!targetOuter||
           currentBranch1->self!=rotation.oldBranch1||targetBranch0->self!=rotation.oldBranch1||
           currentBranch0->self!=rotation.oldBranch0||targetBranch1->self!=rotation.oldBranch0||
           currentOuter->self!=rotation.outer||targetOuter->self!=rotation.outer){
            receipt->result=ResultTopologyMismatch;receipt->failureLayer=rotation.layerIndex;
            receipt->failureStateMachine=rotation.stateMachineIndex;receipt->failureByteOffset=20;
            receipt->expectedWord=rotation.oldBranch1;receipt->actualWord=currentBranch1?currentBranch1->self:0;
            return false;
        }
        for(uint32_t input=0;input<rotation.oldBranch1InputCount;++input){
            const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,
                OwnerBranchInput,rotation.layerIndex,rotation.stateMachineIndex,1u,input);
            const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,
                OwnerBranchInput,rotation.layerIndex,rotation.stateMachineIndex,0u,input);
            if(!currentInput||!targetInput){receipt->result=ResultTopologyMismatch;
                receipt->failureLayer=rotation.layerIndex;receipt->failureStateMachine=rotation.stateMachineIndex;
                receipt->failureByteOffset=88;receipt->expectedWord=input;receipt->actualWord=0xFFFFFFFFu;return false;}
            if(!AppendOwnerWeightPlan(unityBase+kAnimationMixerPlayableVtableRva,
                OwnerWeightPrimeOldBranch1,current,currentCount,*currentInput,*targetInput,
                currentInput->entryPlayable,targetInput->entryPlayable,targetInput->entryWeightBits,
                output,capacity,count,receipt))return false;
        }
        for(uint32_t input=0;input<rotation.oldBranch0InputCount;++input){
            const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,
                OwnerBranchInput,rotation.layerIndex,rotation.stateMachineIndex,0u,input);
            const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,
                OwnerBranchInput,rotation.layerIndex,rotation.stateMachineIndex,1u,input);
            if(!currentInput||!targetInput){receipt->result=ResultTopologyMismatch;
                receipt->failureLayer=rotation.layerIndex;receipt->failureStateMachine=rotation.stateMachineIndex;
                receipt->failureByteOffset=88;receipt->expectedWord=input;receipt->actualWord=0xFFFFFFFFu;return false;}
            if(!AppendOwnerWeightPlan(unityBase+kAnimationMixerPlayableVtableRva,
                OwnerWeightFinalOldBranch0,current,currentCount,*currentInput,*targetInput,
                currentInput->entryPlayable,targetInput->entryPlayable,
                input+1u<rotation.oldBranch0InputCount?0u:currentInput->entryWeightBits,
                output,capacity,count,receipt))return false;
        }
        const uintptr_t beforeOuter[3]={rotation.oldBranch0,rotation.oldBranch1,rotation.oldBranch2};
        const uintptr_t afterOuter[3]={rotation.oldBranch1,rotation.oldBranch0,rotation.oldBranch2};
        for(uint32_t input=0;input<3u;++input){
            const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,
                OwnerOuterInput,rotation.layerIndex,rotation.stateMachineIndex,0xFFFFFFFFu,input);
            const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,
                OwnerOuterInput,rotation.layerIndex,rotation.stateMachineIndex,0xFFFFFFFFu,input);
            if(!currentInput||!targetInput){receipt->result=ResultTopologyMismatch;
                receipt->failureLayer=rotation.layerIndex;receipt->failureStateMachine=rotation.stateMachineIndex;
                receipt->failureByteOffset=88;receipt->expectedWord=input;receipt->actualWord=0xFFFFFFFFu;return false;}
            const uint32_t afterEnd=input==0u?0x3F800000u:0u;
            if(!AppendOwnerWeightPlan(unityBase+kMixerPlayableVtableRva,OwnerWeightFinalOuter,
                current,currentCount,*currentInput,*targetInput,beforeOuter[input],afterOuter[input],afterEnd,
                output,capacity,count,receipt))return false;
        }
    }
    return true;
}

static bool OwnerWeightTopologyExact(const OwnerWeightPlan& plan,bool afterEnd) {
    const uintptr_t expectedPlayable=afterEnd?plan.entryPlayableAfter:plan.entryPlayableBefore;
    return plan.mixer&&plan.mixerInternal&&plan.inputEntries&&plan.entryAddress&&
           plan.inputIndex<plan.inputCount&&plan.inputIndex<=UINT32_MAX/12u&&
           plan.inputEntries<=UINTPTR_MAX-plan.inputIndex*12u&&
           Readable(reinterpret_cast<const void*>(plan.mixer),0x14u)&&
           *reinterpret_cast<const uintptr_t*>(plan.mixer)==plan.mixerVtable&&
           *reinterpret_cast<const uintptr_t*>(plan.mixer+0x10u)==plan.mixerInternal&&
           Readable(reinterpret_cast<const void*>(plan.mixerInternal),0x20u)&&
           *reinterpret_cast<const uintptr_t*>(plan.mixerInternal+0x10u)==plan.inputEntries&&
           *reinterpret_cast<const uint32_t*>(plan.mixerInternal+0x18u)==plan.inputCount&&
           *reinterpret_cast<const uint32_t*>(plan.mixerInternal+0x1Cu)==plan.inputCapacityRaw&&
           plan.entryAddress==plan.inputEntries+plan.inputIndex*12u&&
           Readable(reinterpret_cast<const void*>(plan.entryAddress),12u)&&
           Writable(reinterpret_cast<const void*>(plan.entryAddress),4u)&&
           *reinterpret_cast<const uintptr_t*>(plan.entryAddress+4u)==expectedPlayable&&
           *reinterpret_cast<const uint32_t*>(plan.entryAddress+8u)==plan.entryPortRaw;
}

static bool VerifyOwnerWeightPreimages(const OwnerWeightPlan* plans,uint32_t count,
                                       AnimatorEndTransitionReceipt* receipt) {
    for(uint32_t i=0;i<count;++i){
        const OwnerWeightPlan& plan=plans[i];receipt->failureLayer=plan.layerIndex;
        receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
        if(!OwnerWeightTopologyExact(plan,false)){
            receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=84;
            receipt->expectedWord=plan.entryPlayableBefore;
            receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.entryAddress+4u),4u)?
                *reinterpret_cast<const uint32_t*>(plan.entryAddress+4u):0;return false;
        }
        const uint32_t live=*reinterpret_cast<const uint32_t*>(plan.entryAddress);
        if(live!=plan.weightBefore){receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=88;
            receipt->expectedWord=plan.weightBefore;receipt->actualWord=live;return false;}
    }
    return true;
}

static bool ApplyPrimeOwnerWeights(OwnerWeightPlan* plans,uint32_t count,SetInputWeight setter,
                                   bool& changed,AnimatorEndTransitionReceipt* receipt) {
    changed=false;
    for(uint32_t i=0;i<count;++i){
        OwnerWeightPlan& plan=plans[i];
        if(plan.role!=OwnerWeightPrimeOldBranch1)continue;
        receipt->failureLayer=plan.layerIndex;receipt->failureStateMachine=plan.stateMachineIndex;
        receipt->failureRecord=plan.currentRecord;receipt->failureByteOffset=88;
        if(!OwnerWeightTopologyExact(plan,false)){
            receipt->result=ResultTopologyMismatch;receipt->expectedWord=plan.entryPlayableBefore;
            receipt->actualWord=0;return false;
        }
        uint32_t* live=reinterpret_cast<uint32_t*>(plan.entryAddress);
        if(*live==plan.weightAfterEnd)continue;
        if(*live!=plan.weightBefore){receipt->result=ResultTopologyMismatch;
            receipt->expectedWord=plan.weightBefore;receipt->actualWord=*live;return false;}
        union { uint32_t bits;float value; } weight={plan.weightAfterEnd};
        changed=true;
        plan.primeWriteStarted=1u;
        setter(reinterpret_cast<void*>(plan.mixer),plan.inputIndex,weight.value);
        if(*live!=plan.weightAfterEnd){receipt->result=ResultWriteMismatch;
            receipt->expectedWord=plan.weightAfterEnd;receipt->actualWord=*live;return false;}
        plan.primeWriteCompleted=1u;
        ++receipt->primedWeightWriteCount;
    }
    return true;
}

static bool RollbackPrimeOwnerWeights(OwnerWeightPlan* plans,uint32_t count,SetInputWeight setter,
                                      AnimatorEndTransitionReceipt* receipt) {
    bool exact=true;
    for(uint32_t reverse=count;reverse!=0u;--reverse){
        OwnerWeightPlan& plan=plans[reverse-1u];
        if(plan.role!=OwnerWeightPrimeOldBranch1||!plan.primeWriteStarted)continue;
        if(!OwnerWeightTopologyExact(plan,false)){exact=false;continue;}
        uint32_t* live=reinterpret_cast<uint32_t*>(plan.entryAddress);
        if(*live==plan.weightBefore){plan.primeWriteStarted=0u;plan.primeWriteCompleted=0u;continue;}
        if(*live!=plan.weightAfterEnd){exact=false;continue;}
        union { uint32_t bits;float value; } weight={plan.weightBefore};
        setter(reinterpret_cast<void*>(plan.mixer),plan.inputIndex,weight.value);
        if(*live!=plan.weightBefore)exact=false;
        else {++receipt->rolledBackWeightWriteCount;plan.primeWriteStarted=0u;plan.primeWriteCompleted=0u;}
    }
    if(!exact)receipt->rollbackFailure=1u;
    return exact;
}

static bool VerifyOwnerWeightsAfterEnd(const OwnerWeightPlan* plans,uint32_t count,
                                       AnimatorEndTransitionReceipt* receipt) {
    for(uint32_t i=0;i<count;++i){
        const OwnerWeightPlan& plan=plans[i];receipt->failureLayer=plan.layerIndex;
        receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
        if(!OwnerWeightTopologyExact(plan,true)){
            receipt->result=ResultWriteMismatch;receipt->failureByteOffset=84;
            receipt->expectedWord=plan.entryPlayableAfter;receipt->actualWord=0;return false;
        }
        const uint32_t live=*reinterpret_cast<const uint32_t*>(plan.entryAddress);
        if(live!=plan.weightAfterEnd){receipt->result=ResultWriteMismatch;receipt->failureByteOffset=88;
            receipt->expectedWord=plan.weightAfterEnd;receipt->actualWord=live;return false;}
    }
    return true;
}

static bool ApplyFinalOwnerWeights(const OwnerWeightPlan* plans,uint32_t count,SetInputWeight setter,
                                   AnimatorEndTransitionReceipt* receipt) {
    for(uint32_t i=0;i<count;++i){
        const OwnerWeightPlan& plan=plans[i];receipt->failureLayer=plan.layerIndex;
        receipt->failureStateMachine=plan.stateMachineIndex;receipt->failureRecord=plan.currentRecord;
        receipt->failureByteOffset=88;
        if(!OwnerWeightTopologyExact(plan,true)){
            receipt->result=ResultWriteMismatch;receipt->expectedWord=plan.entryPlayableAfter;
            receipt->actualWord=0;return false;
        }
        uint32_t* live=reinterpret_cast<uint32_t*>(plan.entryAddress);
        if(*live!=plan.weightAfterEnd){
            receipt->result=ResultWriteMismatch;receipt->expectedWord=plan.weightAfterEnd;
            receipt->actualWord=*live;return false;
        }
        if(*live!=plan.weightTarget){
            union { uint32_t bits;float value; } weight={plan.weightTarget};
            setter(reinterpret_cast<void*>(plan.mixer),plan.inputIndex,weight.value);
            if(*live!=plan.weightTarget){receipt->result=ResultWriteMismatch;
                receipt->expectedWord=plan.weightTarget;receipt->actualWord=*live;return false;}
        }
        ++receipt->completedWeightPlanCount;
    }
    return true;
}

static bool CaptureReboundClipObservation(uintptr_t unityBase,const OwnerReboundClip& rebound,
                                          uint32_t branchIndex,uint32_t expectedGraphDirty,
                                          AnimatorOwnerGraphRecord& observation,
                                          AnimatorEndTransitionReceipt* receipt) {
    AnimatorOwnerGraphRecord records[1u+kMaximumMixerInputs*2u]={};
    AnimatorOwnerGraphReceipt owner={};
    InitializeOwnerGraphReceipt(&owner,unityBase,receipt->animator);
    uint32_t visited=0,hash=2166136261u;
    if(!SnapshotOwnerPlayable(OwnerChildNode,rebound.layerIndex,rebound.stateMachineIndex,
                              branchIndex,reinterpret_cast<void*>(rebound.playable),
                              unityBase+kAnimationClipPlayableVtableRva,true,0,0,0,
                              records,static_cast<uint32_t>(sizeof(records)/sizeof(records[0])),
                              visited,hash,&owner)){
        CopyOwnerFailure(owner,receipt);receipt->failureRecord=rebound.currentRecord;return false;
    }
    if(visited==0u||records[0].self!=rebound.playable||owner.graph!=receipt->graph||
       owner.graphDirty58!=expectedGraphDirty){
        receipt->result=ResultTopologyMismatch;receipt->failureLayer=rebound.layerIndex;
        receipt->failureStateMachine=rebound.stateMachineIndex;receipt->failureRecord=rebound.currentRecord;
        receipt->failureByteOffset=20;receipt->expectedWord=rebound.playable;
        receipt->actualWord=visited?records[0].self:0;return false;
    }
    observation=records[0];
    return true;
}

static bool VerifyReboundClipPreimages(uintptr_t unityBase,uintptr_t controller,
                                       const OwnerReboundClip* rebounds,uint32_t reboundCount,
                                       const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
                                       const AnimatorOwnerGraphRecord* target,uint32_t targetCount,
                                       AnimatorEndTransitionReceipt* receipt) {
    RootByType rootByType=reinterpret_cast<RootByType>(unityBase+kRootByTypeRva);
    for(uint32_t i=0;i<reboundCount;++i){
        const OwnerReboundClip& rebound=rebounds[i];
        receipt->failureLayer=rebound.layerIndex;receipt->failureStateMachine=rebound.stateMachineIndex;
        receipt->failureRecord=rebound.currentRecord;
        const uint32_t currentBranch=1u-rebound.targetBranchIndex;
        const AnimatorOwnerGraphRecord* currentInput=FindUniqueOwnerRecord(current,currentCount,OwnerBranchInput,
            rebound.layerIndex,rebound.stateMachineIndex,currentBranch,rebound.inputIndex);
        const AnimatorOwnerGraphRecord* targetInput=FindUniqueOwnerRecord(target,targetCount,OwnerBranchInput,
            rebound.layerIndex,rebound.stateMachineIndex,rebound.targetBranchIndex,rebound.inputIndex);
        bool targetDuplicate=false;
        const AnimatorOwnerGraphRecord* targetChild=targetInput?FindOwnerChild(target,targetCount,
            rebound.layerIndex,rebound.stateMachineIndex,rebound.targetBranchIndex,
            targetInput->entryPlayable,targetDuplicate):0;
        if(!currentInput||!targetInput||targetDuplicate||!targetChild||
           !SameDirectEntryTopology(*currentInput,*targetInput)||
           currentInput->entryPlayable!=rebound.playable||targetInput->entryPlayable!=rebound.playable||
           targetChild->clip108!=rebound.targetClip||targetChild->self!=rebound.playable||
           rebound.targetBranchIndex>1u||!Readable(reinterpret_cast<const void*>(rebound.targetClip),4u)||
           !Writable(reinterpret_cast<const void*>(rebound.playable+0x90u),4u)||
           !Writable(reinterpret_cast<const void*>(rebound.playable+0x108u),4u)){
            receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=80;
            receipt->expectedWord=rebound.targetClip;receipt->actualWord=targetChild?targetChild->clip108:0;return false;
        }
        AnimatorOwnerGraphRecord live={};
        if(!CaptureReboundClipObservation(unityBase,rebound,currentBranch,
                                          receipt->graphDirtyBefore,live,receipt))return false;
        if(memcmp(&live,&rebound.preimage,sizeof(live))!=0){
            receipt->result=ResultTopologyMismatch;
            const uint8_t* expected=reinterpret_cast<const uint8_t*>(&rebound.preimage);
            const uint8_t* actual=reinterpret_cast<const uint8_t*>(&live);
            for(uint32_t offset=0;offset<sizeof(live);++offset)if(expected[offset]!=actual[offset]){
                const uint32_t word=offset&~3u;receipt->failureByteOffset=offset;
                receipt->expectedWord=*reinterpret_cast<const uint32_t*>(expected+word);
                receipt->actualWord=*reinterpret_cast<const uint32_t*>(actual+word);break;
            }
            return false;
        }
        const uintptr_t root=reinterpret_cast<uintptr_t>(
            rootByType(reinterpret_cast<void*>(rebound.playable),0u));
        if(root!=rebound.dirtyRoot||root!=controller||
           !Readable(reinterpret_cast<const void*>(root+0x90u),4u)||
           !Writable(reinterpret_cast<const void*>(root+0x90u),4u)||
           *reinterpret_cast<const uint32_t*>(root+0x90u)!=rebound.rootDirtyBefore){
            receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=112;
            receipt->expectedWord=rebound.rootDirtyBefore;
            receipt->actualWord=root&&Readable(reinterpret_cast<const void*>(root+0x90u),4u)?
                *reinterpret_cast<const uint32_t*>(root+0x90u):0;return false;
        }
    }
    return true;
}

static const OwnerWeightPlan* FindPreClearWeightPlan(const OwnerClearedClip& cleared,
                                                     const OwnerWeightPlan* weights,
                                                     uint32_t weightCount) {
    const OwnerWeightPlan* found=0;
    for(uint32_t i=0;i<weightCount;++i){
        const OwnerWeightPlan& weight=weights[i];
        if(weight.role!=OwnerWeightPrimeOldBranch1||
           weight.layerIndex!=cleared.layerIndex||
           weight.stateMachineIndex!=cleared.stateMachineIndex||
           weight.inputIndex!=cleared.inputIndex)continue;
        if(found)return 0;
        found=weights+i;
    }
    return found;
}

static bool VerifyPreClearClipPreimages(uintptr_t unityBase,uintptr_t controller,
                                        const OwnerClearedClip* cleared,uint32_t clearedCount,
                                        const OwnerWeightPlan* weights,uint32_t weightCount,
                                        const AnimatorOwnerGraphRecord* current,uint32_t currentCount,
                                        AnimatorEndTransitionReceipt* receipt) {
    RootByType rootByType=reinterpret_cast<RootByType>(unityBase+kRootByTypeRva);
    for(uint32_t i=0;i<clearedCount;++i){
        const OwnerClearedClip& item=cleared[i];
        if(!item.requiresPreClear)continue;
        receipt->failureLayer=item.layerIndex;receipt->failureStateMachine=item.stateMachineIndex;
        receipt->failureRecord=item.currentRecord;
        bool duplicate=false;
        const AnimatorOwnerGraphRecord* live=FindOwnerChild(current,currentCount,item.layerIndex,
            item.stateMachineIndex,1u-item.targetBranchIndex,item.playable,duplicate);
        const OwnerWeightPlan* weight=FindPreClearWeightPlan(item,weights,weightCount);
        const uintptr_t root=reinterpret_cast<uintptr_t>(
            rootByType(reinterpret_cast<void*>(item.playable),0u));
        if(duplicate||!live||memcmp(live,&item.preimage,sizeof(*live))!=0||
           !weight||weight->entryPlayableBefore!=item.playable||weight->weightAfterEnd!=0u||
           !OwnerWeightTopologyExact(*weight,false)||root!=item.dirtyRoot||root!=controller||
           !Readable(reinterpret_cast<const void*>(root+0x90u),4u)||
           *reinterpret_cast<const uint32_t*>(root+0x90u)!=item.rootDirtyBefore||
           !Writable(reinterpret_cast<const void*>(item.playable+0x90u),4u)||
           !Writable(reinterpret_cast<const void*>(item.playable+0x108u),4u)){
            receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=80;
            receipt->expectedWord=item.preimage.clip108;
            receipt->actualWord=live?live->clip108:0u;return false;
        }
    }
    return true;
}

static bool ApplyPreClearClips(uintptr_t unityBase,uintptr_t controller,
                               const OwnerClearedClip* cleared,uint32_t clearedCount,
                               const OwnerWeightPlan* weights,uint32_t weightCount,
                               SetClip setter,AnimatorEndTransitionReceipt* receipt) {
    RootByType rootByType=reinterpret_cast<RootByType>(unityBase+kRootByTypeRva);
    uint32_t completed=0;
    for(uint32_t i=0;i<clearedCount;++i){
        const OwnerClearedClip& item=cleared[i];
        if(!item.requiresPreClear)continue;
        receipt->failureLayer=item.layerIndex;receipt->failureStateMachine=item.stateMachineIndex;
        receipt->failureRecord=item.currentRecord;receipt->failureByteOffset=80;
        const OwnerWeightPlan* weight=FindPreClearWeightPlan(item,weights,weightCount);
        const uintptr_t root=reinterpret_cast<uintptr_t>(
            rootByType(reinterpret_cast<void*>(item.playable),0u));
        const uint32_t dirtyProjected=(item.preimage.dirty9093&0xFF00FFFFu)|0x00010000u;
        const uint32_t rootProjected=(item.rootDirtyBefore&0x00FFFFFFu)|0x01000000u;
        const uint32_t rootBefore=completed?rootProjected:item.rootDirtyBefore;
        if(!weight||!OwnerWeightTopologyExact(*weight,false)||
           *reinterpret_cast<const uint32_t*>(weight->entryAddress)!=0u||
           weight->weightAfterEnd!=0u||root!=item.dirtyRoot||root!=controller||
           !Readable(reinterpret_cast<const void*>(item.playable+0x108u),4u)||
           !Readable(reinterpret_cast<const void*>(item.playable+0x90u),4u)||
           !Readable(reinterpret_cast<const void*>(root+0x90u),4u)||
           *reinterpret_cast<const uintptr_t*>(item.playable+0x108u)!=item.preimage.clip108||
           *reinterpret_cast<const uint32_t*>(item.playable+0x90u)!=item.preimage.dirty9093||
           *reinterpret_cast<const uint32_t*>(root+0x90u)!=rootBefore){
            receipt->result=ResultWriteMismatch;receipt->expectedWord=0u;
            receipt->actualWord=weight&&Readable(reinterpret_cast<const void*>(weight->entryAddress),4u)?
                *reinterpret_cast<const uint32_t*>(weight->entryAddress):0xFFFFFFFFu;return false;
        }
        setter(reinterpret_cast<void*>(item.playable),0);
        const uintptr_t afterRoot=reinterpret_cast<uintptr_t>(
            rootByType(reinterpret_cast<void*>(item.playable),0u));
        if(*reinterpret_cast<const uintptr_t*>(item.playable+0x108u)!=0u||
           *reinterpret_cast<const uint32_t*>(item.playable+0x90u)!=dirtyProjected||
           afterRoot!=root||*reinterpret_cast<const uint32_t*>(afterRoot+0x90u)!=rootProjected){
            receipt->result=ResultWriteMismatch;receipt->expectedWord=0u;
            receipt->actualWord=*reinterpret_cast<const uintptr_t*>(item.playable+0x108u);return false;
        }
        ++completed;
    }
    return true;
}

static bool ApplyOwnerReboundClips(uintptr_t unityBase,uintptr_t controller,
                                   const OwnerReboundClip* rebounds,uint32_t reboundCount,
                                   SetClip setter,AnimatorEndTransitionReceipt* receipt) {
    RootByType rootByType=reinterpret_cast<RootByType>(unityBase+kRootByTypeRva);
    for(uint32_t i=0;i<reboundCount;++i){
        const OwnerReboundClip& rebound=rebounds[i];
        receipt->failureLayer=rebound.layerIndex;receipt->failureStateMachine=rebound.stateMachineIndex;
        receipt->failureRecord=rebound.currentRecord;
        AnimatorOwnerGraphRecord before={};
        if(!CaptureReboundClipObservation(unityBase,rebound,rebound.targetBranchIndex,
                                          receipt->graphDirtyProjected,before,receipt))return false;
        const uintptr_t expectedBeforeClip=rebound.targetBranchIndex==0u?rebound.sourceClip:0u;
        const uint32_t expectedDirty=(rebound.playableDirtyBefore&0xFF00FFFFu)|0x00010000u;
        AnimatorOwnerGraphRecord normalized=before;
        normalized.branchIndex=rebound.preimage.branchIndex;
        normalized.clip108=rebound.preimage.clip108;
        normalized.dirty9093=rebound.preimage.dirty9093;
        if((normalized.flags7C^rebound.preimage.flags7C)==0x40u)
            normalized.flags7C=rebound.preimage.flags7C;
        if(!SameDirectPlayableTopology(before,rebound.preimage)||before.clip108!=expectedBeforeClip||
           before.dirty9093!=(rebound.targetBranchIndex==0u?rebound.playableDirtyBefore:expectedDirty)||
           memcmp(&normalized,&rebound.preimage,sizeof(normalized))!=0||
           !Readable(reinterpret_cast<const void*>(rebound.targetClip),4u)){
            receipt->result=ResultWriteMismatch;receipt->failureByteOffset=80;
            receipt->expectedWord=expectedBeforeClip;receipt->actualWord=before.clip108;return false;
        }
        const uintptr_t root=reinterpret_cast<uintptr_t>(
            rootByType(reinterpret_cast<void*>(rebound.playable),0u));
        const uint32_t expectedRootBefore=i==0u?receipt->controllerDirtyAfterEnd:
                                                    receipt->controllerDirtyProjected;
        if(root!=rebound.dirtyRoot||root!=controller||
           !Readable(reinterpret_cast<const void*>(root+0x90u),4u)||
           *reinterpret_cast<const uint32_t*>(root+0x90u)!=expectedRootBefore){
            receipt->result=ResultWriteMismatch;receipt->failureByteOffset=112;
            receipt->expectedWord=expectedRootBefore;
            receipt->actualWord=root&&Readable(reinterpret_cast<const void*>(root+0x90u),4u)?
                *reinterpret_cast<const uint32_t*>(root+0x90u):0;return false;
        }
        setter(reinterpret_cast<void*>(rebound.playable),reinterpret_cast<void*>(rebound.targetClip));
        AnimatorOwnerGraphRecord after={};
        if(!CaptureReboundClipObservation(unityBase,rebound,rebound.targetBranchIndex,
                                          receipt->graphDirtyProjected,after,receipt))return false;
        AnimatorOwnerGraphRecord afterNormalized=after;
        afterNormalized.clip108=before.clip108;afterNormalized.dirty9093=before.dirty9093;
        const uintptr_t afterRoot=reinterpret_cast<uintptr_t>(
            rootByType(reinterpret_cast<void*>(rebound.playable),0u));
        if(after.clip108!=rebound.targetClip||after.dirty9093!=expectedDirty||
           memcmp(&before,&afterNormalized,sizeof(before))!=0||afterRoot!=root||
           !Readable(reinterpret_cast<const void*>(afterRoot+0x90u),4u)||
           *reinterpret_cast<const uint32_t*>(afterRoot+0x90u)!=receipt->controllerDirtyProjected){
            receipt->result=ResultWriteMismatch;receipt->failureByteOffset=80;
            receipt->expectedWord=rebound.targetClip;receipt->actualWord=after.clip108;return false;
        }
        ++receipt->completedReboundClipCount;
    }
    return true;
}

} // namespace

extern "C" __declspec(dllexport) uint32_t __cdecl oc2_animator_checkpoint_api_version() {
    return kApiVersion;
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_controller_normalize(
    uintptr_t unityBase,uintptr_t animator,AnimatorControllerReceipt* receipt);

extern "C" __declspec(dllexport) int __cdecl oc2_animator_controller_roundtrip(
    uintptr_t unityBase,uintptr_t animator,AnimatorControllerReceipt* receipt) {
    // Version 4 deliberately makes the legacy probe a normalization check.
    // The old implementation called playback-only SetRecorderData and was not
    // observational on an ordinary live Animator controller.
    return oc2_animator_controller_normalize(unityBase,animator,receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_controller_capture(
    uintptr_t unityBase,uintptr_t animator,void* output,uint32_t capacity,AnimatorControllerReceipt* receipt) {
    if (!receipt) return 0;
    InitializeReceipt(receipt,unityBase,animator);
    if (!unityBase || !animator || (!output&&capacity)) { receipt->result=ResultBadArgument; return 0; }
    void* controller=0;void* memory=0;void* allocator=0;
    if (!Resolve(unityBase,animator,receipt,controller,memory,allocator)) return 0;
    __try {
        CopyControllerMemory copy=reinterpret_cast<CopyControllerMemory>(unityBase+kCopyControllerMemoryRva);
        void* blob=copy(memory,allocator,&receipt->blobSizeBefore);
        if(!blob||receipt->blobSizeBefore<0x20||receipt->blobSizeBefore>kMaximumBlobSize) {
            receipt->result=ResultInvalidBlob;Release(allocator,blob);return 0;
        }
        receipt->hashBefore=Hash(blob,receipt->blobSizeBefore);
        if(!output||capacity<receipt->blobSizeBefore) {
            receipt->result=ResultCapacity;Release(allocator,blob);return 0;
        }
        CopyBytes(output,blob,receipt->blobSizeBefore);Release(allocator,blob);
        receipt->result=ResultOk;return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;return 0;
    }
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_controller_input_capture(
    uintptr_t unityBase,uintptr_t animator,void* output,uint32_t capacity,AnimatorControllerInputReceipt* receipt) {
    if (!receipt) return 0;
    InitializeInputReceipt(receipt,unityBase,animator);
    if (!unityBase || !animator || (!output&&capacity)) { receipt->result=ResultBadArgument; return 0; }
    AnimatorControllerReceipt controllerReceipt = {};
    InitializeReceipt(&controllerReceipt,unityBase,animator);
    void* controller=0;void* memory=0;void* allocator=0;
    if (!Resolve(unityBase,animator,&controllerReceipt,controller,memory,allocator)) {
        receipt->result=controllerReceipt.result;receipt->lastError=controllerReceipt.lastError;return 0;
    }
    receipt->controller=reinterpret_cast<uintptr_t>(controller);
    __try {
        void* constant=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xAC);
        void* input=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xB0);
        receipt->controllerConstant=reinterpret_cast<uintptr_t>(constant);
        receipt->controllerInput=reinterpret_cast<uintptr_t>(input);
        if(!Readable(constant,12)||!Readable(input,16)) { receipt->result=ResultUnreadable;return 0; }
        receipt->recordCount=*reinterpret_cast<uint32_t*>(constant);
        receipt->outerCount=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(constant)+8);
        void* records=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(input)+12);
        receipt->records=reinterpret_cast<uintptr_t>(records);
        if(receipt->recordCount>kMaximumControllerInputRecords) { receipt->result=ResultInvalidBlob;return 0; }
        const uint32_t recordBytes=receipt->recordCount*kControllerInputRecordSize;
        receipt->byteSize=kControllerInputPrefixSize+recordBytes;
        if(recordBytes!=0&&!Readable(records,recordBytes)) { receipt->result=ResultUnreadable;return 0; }
        receipt->hash=HashControllerInput(input,records,recordBytes);
        if(!output||capacity<receipt->byteSize) { receipt->result=ResultCapacity;return 0; }
        CopyBytes(output,input,kControllerInputPrefixSize);
        if(recordBytes!=0)CopyBytes(static_cast<uint8_t*>(output)+kControllerInputPrefixSize,records,recordBytes);
        receipt->result=ResultOk;return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;return 0;
    }
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_transition_topology_capture(
    uintptr_t unityBase,uintptr_t animator,void* output,uint32_t capacity,AnimatorTransitionTopologyReceipt* receipt) {
    if(!receipt)return 0;
    InitializeTopologyReceipt(receipt,unityBase,animator);
    if(!unityBase||!animator||(!output&&capacity)){receipt->result=ResultBadArgument;return 0;}
    AnimatorControllerReceipt controllerReceipt={};InitializeReceipt(&controllerReceipt,unityBase,animator);
    void* controller=0;void* memory=0;void* allocator=0;
    if(!Resolve(unityBase,animator,&controllerReceipt,controller,memory,allocator)){
        receipt->result=controllerReceipt.result;receipt->lastError=controllerReceipt.lastError;return 0;
    }
    receipt->controller=reinterpret_cast<uintptr_t>(controller);
    receipt->controllerMemory=reinterpret_cast<uintptr_t>(memory);
    __try {
        if(!Readable(controller,0xEC)){receipt->result=ResultUnreadable;return 0;}
        receipt->blobCapacity=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0xBC);
        if(receipt->blobCapacity<0x69||receipt->blobCapacity>kMaximumBlobSize||
            !Readable(memory,receipt->blobCapacity)){receipt->result=ResultInvalidBlob;return 0;}
        receipt->blobLayerCount=*reinterpret_cast<uint32_t*>(memory);
        void* graphMemory=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xB8);
        void* liveLayers=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xE8);
        receipt->controllerGraphMemory=reinterpret_cast<uintptr_t>(graphMemory);
        receipt->liveLayers=reinterpret_cast<uintptr_t>(liveLayers);
        if(!Readable(graphMemory,0x10)||!Readable(liveLayers,8)){receipt->result=ResultUnreadable;return 0;}
        receipt->graphLayerCount=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(graphMemory)+0x0C);
        int32_t signedCount=*reinterpret_cast<int32_t*>(reinterpret_cast<uintptr_t>(liveLayers)+4);
        if(signedCount<0){receipt->result=ResultInvalidBlob;return 0;}
        receipt->liveLayerCount=static_cast<uint32_t>(signedCount);
        if(receipt->liveLayerCount>kMaximumTopologyLayers||receipt->blobLayerCount!=receipt->graphLayerCount||
            receipt->liveLayerCount>receipt->blobLayerCount){receipt->result=ResultInvalidBlob;return 0;}
        const uint32_t count=receipt->liveLayerCount;
        receipt->byteSize=count*sizeof(AnimatorTransitionTopologyLayer);
        void** nodes=*reinterpret_cast<void***>(reinterpret_cast<uintptr_t>(graphMemory)+4);
        void** lives=*reinterpret_cast<void***>(liveLayers);
        if(count!=0&&(!Readable(nodes,count*4)||!Readable(lives,count*4))){receipt->result=ResultUnreadable;return 0;}
        const uint8_t* rootAnchor=static_cast<const uint8_t*>(memory)+4;
        if(!Within(memory,receipt->blobCapacity,rootAnchor,4)){receipt->result=ResultInvalidBlob;return 0;}
        const uint8_t* entries=0;
        if(!RelativeWithin(memory,receipt->blobCapacity,rootAnchor,*reinterpret_cast<const int32_t*>(rootAnchor),count*4,entries)){
            receipt->result=ResultInvalidBlob;return 0;
        }
        AnimatorTransitionTopologyLayer layers[kMaximumTopologyLayers]={};
        for(uint32_t i=0;i<count;++i){
            const uint8_t* entry=entries+i*4;const uint8_t* layer=0;
            if(!RelativeWithin(memory,receipt->blobCapacity,entry,*reinterpret_cast<const int32_t*>(entry),0x69,layer)){
                receipt->result=ResultInvalidBlob;return 0;
            }
            void* node=nodes[i];void* live=lives[i];
            if(!Readable(node,0x14)||!Readable(live,0xA8)||
                *reinterpret_cast<uintptr_t*>(live)!=unityBase+kMixerPlayableVtableRva){receipt->result=ResultUnreadable;return 0;}
            uint32_t liveMode=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(live)+0xA0);
            if(liveMode>2){receipt->result=ResultInvalidBlob;return 0;}
            layers[i].index=i;layers[i].live=reinterpret_cast<uintptr_t>(live);layers[i].node=reinterpret_cast<uintptr_t>(node);
            layers[i].blobInterruptedRaw=layer[0x68];
            layers[i].nodeStartArgumentRaw=*reinterpret_cast<uint8_t*>(reinterpret_cast<uintptr_t>(node)+0x10);
            layers[i].liveModeRaw=liveMode;
            layers[i].liveSecondaryArgumentRaw=*reinterpret_cast<uint8_t*>(reinterpret_cast<uintptr_t>(live)+0xA4);
            if((liveMode==0)!=(layers[i].blobInterruptedRaw!=0))++receipt->topologyMismatchCount;
        }
        receipt->hash=Hash(layers,receipt->byteSize);
        if(!output||capacity<receipt->byteSize){receipt->result=ResultCapacity;return 0;}
        if(receipt->byteSize!=0)CopyBytes(output,layers,receipt->byteSize);
        receipt->result=ResultOk;return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER){receipt->lastError=GetExceptionCode();receipt->result=ResultFault;return 0;}
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_mixer_graph_capture(
    uintptr_t unityBase,uintptr_t animator,void* output,uint32_t capacity,AnimatorMixerGraphReceipt* receipt) {
    if(!receipt)return 0;
    InitializeMixerGraphReceipt(receipt,unityBase,animator);
    if(!unityBase||!animator||(!output&&capacity)){receipt->result=ResultBadArgument;return 0;}
    __try {
        void* controller=0;void* constant=0;void* descriptors=0;
        if(!ResolveMixerGraph(unityBase,animator,receipt,controller,constant,descriptors))return 0;
        uint32_t count=0,hash=0;
        if(!WalkMixerGraph(unityBase,controller,constant,descriptors,receipt->layerCount,0,0,0,0,false,false,0,
                           count,hash,receipt))return 0;
        receipt->recordCount=count;
        receipt->byteSize=count*sizeof(AnimatorMixerWeightRecord);
        if(!output||capacity<receipt->byteSize){receipt->result=ResultCapacity;return 0;}
        uint32_t capturedCount=0,capturedHash=0;
        if(!WalkMixerGraph(unityBase,controller,constant,descriptors,receipt->layerCount,0,0,
                           static_cast<AnimatorMixerWeightRecord*>(output),capacity/sizeof(AnimatorMixerWeightRecord),
                           false,false,0,capturedCount,capturedHash,receipt))return 0;
        if(capturedCount!=count){receipt->result=ResultTopologyMismatch;++receipt->mismatchCount;return 0;}
        receipt->recordCount=capturedCount;receipt->hash=capturedHash;
        receipt->result=ResultOk;return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER){receipt->lastError=GetExceptionCode();receipt->result=ResultFault;return 0;}
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_mixer_graph_restore(
    uintptr_t unityBase,uintptr_t animator,const void* blob,uint32_t size,AnimatorMixerGraphReceipt* receipt) {
    if(!receipt)return 0;
    InitializeMixerGraphReceipt(receipt,unityBase,animator);
    if(!unityBase||!animator||!blob||size==0||size%sizeof(AnimatorMixerWeightRecord)!=0||
       size/sizeof(AnimatorMixerWeightRecord)>kMaximumMixerGraphRecords||!Readable(blob,size)){
        receipt->result=ResultBadArgument;return 0;
    }
    const AnimatorMixerWeightRecord* saved=static_cast<const AnimatorMixerWeightRecord*>(blob);
    const uint32_t savedCount=size/sizeof(AnimatorMixerWeightRecord);
    uint32_t* preimage=0;
    SetInputWeight setter=0;
    bool mutationStarted=false;
    __try {
        for(uint32_t i=0;i<savedCount;++i){
            if(saved[i].recordKind>2||
               (saved[i].recordKind==0&&!ValidWeight(saved[i].outerWeightBits))||
               (saved[i].recordKind==1&&saved[i].inputIndex!=0xFFFFFFFFu&&!ValidWeight(saved[i].mixerWeightBits))){
                receipt->result=ResultInvalidBlob;return 0;
            }
            if(saved[i].recordKind<=1&&saved[i].inputIndex!=0xFFFFFFFFu){
                const uintptr_t entries=saved[i].recordKind==0?saved[i].outerEntries:saved[i].mixerEntries;
                const uintptr_t target=entries+saved[i].inputIndex*12;
                for(uint32_t j=0;j<i;++j)
                    if(saved[j].recordKind<=1&&saved[j].inputIndex!=0xFFFFFFFFu&&
                       (saved[j].recordKind==0?saved[j].outerEntries:saved[j].mixerEntries)+saved[j].inputIndex*12==target){
                        receipt->result=ResultInvalidBlob;return 0;
                    }
            }
        }
        if(!VerifyMixerRevision(unityBase,receipt))return 0;
        void* controller=0;void* constant=0;void* descriptors=0;
        if(!ResolveMixerGraph(unityBase,animator,receipt,controller,constant,descriptors))return 0;
        receipt->recordCount=savedCount;receipt->byteSize=size;
        const uint32_t expectedHash=Hash(saved,size);

        uint32_t validatedCount=0,validatedHash=0;
        if(!WalkMixerGraph(unityBase,controller,constant,descriptors,receipt->layerCount,saved,savedCount,0,0,
                           false,false,0,validatedCount,validatedHash,receipt))return 0;

        preimage=static_cast<uint32_t*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,savedCount*sizeof(uint32_t)));
        if(!preimage){receipt->lastError=GetLastError();receipt->result=ResultFault;return 0;}
        for(uint32_t i=0;i<savedCount;++i)
            if(saved[i].recordKind<=1&&saved[i].inputIndex!=0xFFFFFFFFu){
                const uintptr_t entries=saved[i].recordKind==0?saved[i].outerEntries:saved[i].mixerEntries;
                preimage[i]=*reinterpret_cast<const uint32_t*>(entries+saved[i].inputIndex*12);
                if(!ValidWeight(preimage[i])){
                    HeapFree(GetProcessHeap(),0,preimage);preimage=0;receipt->result=ResultInvalidBlob;return 0;
                }
            }
        setter=reinterpret_cast<SetInputWeight>(unityBase+kSetInputWeightRva);
        mutationStarted=true;
        if(!ApplyMixerWeightValues(saved,savedCount,preimage,true,setter,true,receipt)){
            ApplyMixerWeightValues(saved,savedCount,preimage,false,setter,false,receipt);
            mutationStarted=false;HeapFree(GetProcessHeap(),0,preimage);preimage=0;
            receipt->result=ResultWriteMismatch;return 0;
        }

        uint32_t verifiedCount=0,verifiedHash=0;
        if(!WalkMixerGraph(unityBase,controller,constant,descriptors,receipt->layerCount,saved,savedCount,0,0,
                           true,false,0,verifiedCount,verifiedHash,receipt)){
            ApplyMixerWeightValues(saved,savedCount,preimage,false,setter,false,receipt);
            mutationStarted=false;HeapFree(GetProcessHeap(),0,preimage);preimage=0;return 0;
        }
        receipt->recordCount=verifiedCount;receipt->hash=verifiedHash;
        if(verifiedCount!=savedCount||verifiedHash!=expectedHash){
            ApplyMixerWeightValues(saved,savedCount,preimage,false,setter,false,receipt);
            mutationStarted=false;HeapFree(GetProcessHeap(),0,preimage);preimage=0;
            receipt->result=ResultWriteMismatch;++receipt->mismatchCount;return 0;
        }
        mutationStarted=false;HeapFree(GetProcessHeap(),0,preimage);preimage=0;
        receipt->result=ResultOk;return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER){
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;
        if(mutationStarted&&preimage&&setter){
            __try{ApplyMixerWeightValues(saved,savedCount,preimage,false,setter,false,receipt);}
            __except(EXCEPTION_EXECUTE_HANDLER){receipt->lastError=GetExceptionCode();}
        }
        if(preimage)HeapFree(GetProcessHeap(),0,preimage);
        return 0;
    }
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_owner_graph_capture(
    uintptr_t unityBase,uintptr_t animator,void* output,uint32_t capacity,
    AnimatorOwnerGraphReceipt* receipt) {
    if(!receipt)return 0;
    InitializeOwnerGraphReceipt(receipt,unityBase,animator);
    if(!unityBase||!animator){receipt->result=ResultBadArgument;return 0;}
    __try {
        void* controller=0;void* constant=0;void* descriptors=0;
        if(!ResolveOwnerGraph(unityBase,animator,receipt,controller,constant,descriptors))return 0;
        uint32_t count=0,hash=0;
        if(!WalkOwnerGraph(unityBase,descriptors,receipt->layerCount,0,0,
                           count,hash,receipt))return 0;
        receipt->recordCount=count;
        receipt->byteSize=count*sizeof(AnimatorOwnerGraphRecord);
        if(!output||capacity<receipt->byteSize){receipt->result=ResultCapacity;return 0;}
        if(!Writable(output,receipt->byteSize)){receipt->result=ResultBadArgument;return 0;}
        uint32_t capturedCount=0,capturedHash=0;
        if(!WalkOwnerGraph(unityBase,descriptors,receipt->layerCount,
                           static_cast<AnimatorOwnerGraphRecord*>(output),
                           capacity/sizeof(AnimatorOwnerGraphRecord),capturedCount,capturedHash,receipt))return 0;
        if(capturedCount!=count||capturedHash!=hash){receipt->result=ResultTopologyMismatch;receipt->failureRecord=capturedCount;return 0;}
        receipt->recordCount=capturedCount;receipt->hash=capturedHash;
        receipt->result=ResultOk;return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER){
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;return 0;
    }
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_playable_time_restore(
    uintptr_t unityBase,uintptr_t animator,const void* targetOwnerBlob,uint32_t targetOwnerSize,
    AnimatorPlayableTimeReceipt* receipt) {
    if(!receipt)return 0;
    InitializePlayableTimeReceipt(receipt,unityBase,animator);
    receipt->stage=PlayableTimeStageArguments;
    if(!unityBase||!animator||!targetOwnerBlob||targetOwnerSize==0||
       targetOwnerSize%sizeof(AnimatorOwnerGraphRecord)!=0||
       targetOwnerSize/sizeof(AnimatorOwnerGraphRecord)>kMaximumOwnerGraphRecords||
       !Readable(targetOwnerBlob,targetOwnerSize)){
        receipt->result=ResultBadArgument;return 0;
    }
    const AnimatorOwnerGraphRecord* target=
        static_cast<const AnimatorOwnerGraphRecord*>(targetOwnerBlob);
    receipt->targetOwnerCount=targetOwnerSize/sizeof(AnimatorOwnerGraphRecord);
    receipt->targetOwnerHash=Hash(targetOwnerBlob,targetOwnerSize);

    AnimatorOwnerGraphRecord* current=0;
    AnimatorOwnerGraphRecord* projected=0;
    AnimatorOwnerGraphRecord* after=0;
    bool success=false;
    __try {
        do {
            receipt->stage=PlayableTimeStageRevision;
            if(!VerifyPlayableTimeRevision(unityBase,receipt))break;
            AnimatorOwnerGraphReceipt ownerRevision={};
            InitializeOwnerGraphReceipt(&ownerRevision,unityBase,animator);
            if(!VerifyOwnerRevision(unityBase,&ownerRevision)){
                receipt->result=ownerRevision.result;receipt->lastError=ownerRevision.lastError;break;
            }

            receipt->stage=PlayableTimeStageResolve;
            AnimatorOwnerGraphReceipt currentReceipt={};
            InitializeOwnerGraphReceipt(&currentReceipt,unityBase,animator);
            void* controller=0;void* constant=0;void* descriptors=0;
            if(!ResolveOwnerGraph(unityBase,animator,&currentReceipt,controller,constant,descriptors)){
                receipt->result=currentReceipt.result;receipt->lastError=currentReceipt.lastError;break;
            }
            receipt->controller=reinterpret_cast<uintptr_t>(controller);
            receipt->controllerConstant=reinterpret_cast<uintptr_t>(constant);
            receipt->descriptors=reinterpret_cast<uintptr_t>(descriptors);

            receipt->stage=PlayableTimeStageCapture;
            uint32_t currentCount=0,currentHash=0;
            if(!CaptureOwnerAllocated(unityBase,descriptors,currentReceipt.layerCount,0,current,
                                      currentCount,currentHash,&currentReceipt)){
                receipt->result=currentReceipt.result;receipt->lastError=currentReceipt.lastError;
                receipt->failureRecord=currentReceipt.failureRecord;break;
            }
            receipt->currentOwnerCount=currentCount;receipt->currentOwnerHash=currentHash;
            receipt->graph=currentReceipt.graph;receipt->graphDirtyBefore=currentReceipt.graphDirty58;

            receipt->stage=PlayableTimeStagePreflight;
            receipt->uniqueTargetNodes=CountUniqueTimeNodes(target,receipt->targetOwnerCount);
            receipt->uniqueCurrentNodes=CountUniqueTimeNodes(current,currentCount);
            // Before EndTransition normalization the live graph may still
            // expose resolver-only nodes from the future branch that are not
            // reachable in the saved target graph.  Every target node must be
            // present below, but extra live nodes are intentionally left
            // untouched until the guarded lifecycle operation removes them.
            if(receipt->uniqueTargetNodes==0||receipt->uniqueTargetNodes>receipt->uniqueCurrentNodes){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->uniqueTargetNodes;
                receipt->actualWord=receipt->uniqueCurrentNodes;break;
            }
            bool preflight=true;
            for(uint32_t i=0;i<receipt->targetOwnerCount&&preflight;++i){
                // Every flat row carrying this physical node's state must
                // agree with its canonical node row.  This rejects torn or
                // corrupt snapshots before the first virtual time call.
                if(!CarriesOwnerNodeState(target[i].recordKind))continue;
                uint32_t canonicalIndex=0;
                const AnimatorOwnerGraphRecord* canonical=
                    FindCanonicalTimeNode(target,receipt->targetOwnerCount,target[i].self,canonicalIndex);
                if(!canonical||!SameTemporalDefinition(target[i],*canonical,unityBase)){
                    receipt->result=ResultInvalidBlob;
                    SetPlayableTimeFailure(receipt,0xFFFFFFFFu,i,116,
                                           canonical?canonical->currentTime28Low:0,target[i].currentTime28Low,target[i].self);
                    preflight=false;break;
                }
                uint32_t currentIndex=0;
                const AnimatorOwnerGraphRecord* live=
                    FindCanonicalTimeNode(current,currentCount,target[i].self,currentIndex);
                if(!live){
                    receipt->result=ResultTopologyMismatch;
                    SetPlayableTimeFailure(receipt,0xFFFFFFFFu,i,20,static_cast<uint32_t>(target[i].self),0,target[i].self);
                    preflight=false;break;
                }
            }
            for(uint32_t i=0;i<currentCount&&preflight;++i){
                if(!CarriesOwnerNodeState(current[i].recordKind))continue;
                uint32_t currentCanonicalIndex=0;
                const AnimatorOwnerGraphRecord* currentCanonical=
                    FindCanonicalTimeNode(current,currentCount,current[i].self,currentCanonicalIndex);
                if(!currentCanonical||!SameTemporalDefinition(current[i],*currentCanonical,unityBase)){
                    receipt->result=ResultTopologyMismatch;
                    SetPlayableTimeFailure(receipt,i,0xFFFFFFFFu,116,
                                           currentCanonical?currentCanonical->currentTime28Low:0,current[i].currentTime28Low,current[i].self);
                    preflight=false;break;
                }
                // Only validate and plan each physical node once.
                if(!IsOwnerNodeRecord(current[i].recordKind))continue;
                bool seen=false;
                for(uint32_t j=0;j<i;++j)
                    if(IsOwnerNodeRecord(current[j].recordKind)&&current[j].self==current[i].self){seen=true;break;}
                if(seen)continue;
                uint32_t targetIndex=0;
                const AnimatorOwnerGraphRecord* desired=
                    FindCanonicalTimeNode(target,receipt->targetOwnerCount,current[i].self,targetIndex);
                if(!desired)continue;
                if(!desired||!ValidateTimeNode(*currentCanonical,currentCanonicalIndex,*desired,targetIndex,
                                               unityBase,receipt)){
                    if(receipt->result==0)receipt->result=ResultTopologyMismatch;
                    preflight=false;break;
                }
                if(!TemporalNodeExact(*currentCanonical,*desired,unityBase))++receipt->plannedNodeCount;
            }
            if(!preflight)break;

            receipt->stage=PlayableTimeStageProjected;
            const uint32_t currentOwnerSize=currentCount*sizeof(AnimatorOwnerGraphRecord);
            projected=static_cast<AnimatorOwnerGraphRecord*>(HeapAlloc(GetProcessHeap(),0,currentOwnerSize));
            if(!projected){receipt->lastError=GetLastError();receipt->result=ResultFault;break;}
            CopyBytes(projected,current,currentOwnerSize);
            for(uint32_t i=0;i<currentCount;++i){
                if(!CarriesOwnerNodeState(projected[i].recordKind))continue;
                uint32_t targetIndex=0;
                const AnimatorOwnerGraphRecord* desired=
                    FindCanonicalTimeNode(target,receipt->targetOwnerCount,projected[i].self,targetIndex);
                if(!desired)continue;
                ProjectTemporalNode(projected[i],*desired,unityBase);
            }
            if(!preflight)break;
            receipt->projectedOwnerCount=currentCount;
            receipt->projectedOwnerHash=Hash(projected,currentOwnerSize);

            receipt->stage=PlayableTimeStageMutation;
            for(uint32_t i=0;i<currentCount;++i){
                if(!IsOwnerNodeRecord(current[i].recordKind))continue;
                bool seen=false;
                for(uint32_t j=0;j<i;++j)
                    if(IsOwnerNodeRecord(current[j].recordKind)&&current[j].self==current[i].self){seen=true;break;}
                if(seen)continue;
                uint32_t targetIndex=0;
                const AnimatorOwnerGraphRecord* desired=
                    FindCanonicalTimeNode(target,receipt->targetOwnerCount,current[i].self,targetIndex);
                uint32_t currentCanonicalIndex=0;
                const AnimatorOwnerGraphRecord* live=
                    FindCanonicalTimeNode(current,currentCount,current[i].self,currentCanonicalIndex);
                if(!desired)continue;
                if(!live){receipt->result=ResultTopologyMismatch;break;}
                if(TemporalNodeExact(*live,*desired,unityBase))continue;
                receipt->failureRecord=currentCanonicalIndex;receipt->failureTargetRecord=targetIndex;
                receipt->failureNode=live->self;receipt->mutationStarted=1;
                void** table=*reinterpret_cast<void***>(live->self);
                if(!PlayableClockExact(*live,*desired,unityBase)){
                    PlayableTimeCall setTime=reinterpret_cast<PlayableTimeCall>(table[8]);
                    PlayableTimeCall advanceTime=reinterpret_cast<PlayableTimeCall>(table[17]);
                    const double previous=OwnerDouble(desired->previousTime30Low,desired->previousTime30High);
                    const double desiredCurrent=OwnerDouble(desired->currentTime28Low,desired->currentTime28High);
                    setTime(reinterpret_cast<void*>(live->self),previous);
                    advanceTime(reinterpret_cast<void*>(live->self),0.0);
                    switch(desired->flags7C&0x42u){
                        case 0x40u: {
                            volatile double delta=desiredCurrent-previous;
                            advanceTime(reinterpret_cast<void*>(live->self),delta);
                            ++receipt->ordinaryAdvanceRecipes;
                            break;
                        }
                        case 0x00u:
                            setTime(reinterpret_cast<void*>(live->self),desiredCurrent);
                            advanceTime(reinterpret_cast<void*>(live->self),0.0);
                            ++receipt->seekOnlyRecipes;
                            break;
                        case 0x02u:
                            setTime(reinterpret_cast<void*>(live->self),desiredCurrent);
                            ++receipt->seekOnlyRecipes;
                            break;
                        case 0x42u:
                            advanceTime(reinterpret_cast<void*>(live->self),0.0);
                            setTime(reinterpret_cast<void*>(live->self),desiredCurrent);
                            ++receipt->seekOnlyRecipes;
                            break;
                        default:
                            receipt->result=ResultInvalidBlob;break;
                    }
                }
                if(receipt->result)break;
                if(IsCapturedClipRecord(*live,unityBase)&&
                   live->clipInternalWeightBits!=desired->clipInternalWeightBits){
                    PlayableInternalWeightCall setInternalWeight=
                        reinterpret_cast<PlayableInternalWeightCall>(table[36]);
                    union { uint32_t bits;float value; } weight={desired->clipInternalWeightBits};
                    setInternalWeight(reinterpret_cast<void*>(live->self),weight.value);
                }
                ++receipt->completedNodeCount;
            }
            if(receipt->result)break;

            receipt->stage=PlayableTimeStageAfterCapture;
            AnimatorOwnerGraphReceipt afterResolve={};
            InitializeOwnerGraphReceipt(&afterResolve,unityBase,animator);
            void* afterController=0;void* afterConstant=0;void* afterDescriptors=0;
            if(!ResolveOwnerGraph(unityBase,animator,&afterResolve,afterController,afterConstant,afterDescriptors)){
                receipt->result=afterResolve.result;receipt->lastError=afterResolve.lastError;break;
            }
            if(afterController!=controller||afterConstant!=constant||afterDescriptors!=descriptors||
               afterResolve.layerCount!=currentReceipt.layerCount){
                receipt->result=ResultControllerChanged;break;
            }
            uint32_t afterCount=0,afterHash=0;
            if(!CaptureOwnerAllocated(unityBase,afterDescriptors,afterResolve.layerCount,0,after,
                                      afterCount,afterHash,&afterResolve)){
                receipt->result=afterResolve.result;receipt->lastError=afterResolve.lastError;
                receipt->failureRecord=afterResolve.failureRecord;break;
            }
            receipt->afterOwnerCount=afterCount;receipt->afterOwnerHash=afterHash;
            receipt->graphDirtyAfter=afterResolve.graphDirty58;

            receipt->stage=PlayableTimeStageAfterCompare;
            if(afterCount!=receipt->projectedOwnerCount){
                receipt->result=ResultWriteMismatch;receipt->expectedWord=receipt->projectedOwnerCount;
                receipt->actualWord=afterCount;break;
            }
            if(afterResolve.graph!=receipt->graph){
                receipt->result=ResultWriteMismatch;receipt->expectedWord=static_cast<uint32_t>(receipt->graph);
                receipt->actualWord=static_cast<uint32_t>(afterResolve.graph);break;
            }
            if(receipt->graphDirtyAfter!=receipt->graphDirtyBefore){
                receipt->result=ResultWriteMismatch;receipt->expectedWord=receipt->graphDirtyBefore;
                receipt->actualWord=receipt->graphDirtyAfter;break;
            }
            if(!ComparePlayableTimeBytes(projected,after,afterCount,receipt)){
                receipt->result=ResultWriteMismatch;break;
            }
            if(afterHash!=receipt->projectedOwnerHash){
                receipt->result=ResultWriteMismatch;receipt->expectedWord=receipt->projectedOwnerHash;
                receipt->actualWord=afterHash;break;
            }
            receipt->failureRecord=0xFFFFFFFFu;receipt->failureTargetRecord=0xFFFFFFFFu;
            receipt->failureByteOffset=0xFFFFFFFFu;receipt->failureNode=0;
            receipt->expectedWord=0;receipt->actualWord=0;
            receipt->stage=PlayableTimeStageComplete;receipt->result=ResultOk;success=true;
        } while(false);
    }
    __except(EXCEPTION_EXECUTE_HANDLER){
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;
    }
    if(after)HeapFree(GetProcessHeap(),0,after);
    if(projected)HeapFree(GetProcessHeap(),0,projected);
    if(current)HeapFree(GetProcessHeap(),0,current);
    return success?1:0;
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_target_null_clip_restore(
    uintptr_t unityBase,uintptr_t animator,const void* targetOwnerBlob,uint32_t targetOwnerSize,
    AnimatorTargetNullClipReceipt* receipt) {
    if(!receipt)return 0;
    InitializeTargetNullClipReceipt(receipt,unityBase,animator);
    receipt->stage=TargetNullClipStageArguments;
    if(!unityBase||!animator||!targetOwnerBlob||targetOwnerSize==0||
       targetOwnerSize%sizeof(AnimatorOwnerGraphRecord)!=0||
       targetOwnerSize/sizeof(AnimatorOwnerGraphRecord)>kMaximumOwnerGraphRecords||
       !Readable(targetOwnerBlob,targetOwnerSize)){
        receipt->result=ResultBadArgument;return 0;
    }
    const AnimatorOwnerGraphRecord* target=
        static_cast<const AnimatorOwnerGraphRecord*>(targetOwnerBlob);
    receipt->targetOwnerCount=targetOwnerSize/sizeof(AnimatorOwnerGraphRecord);
    receipt->targetOwnerHash=Hash(targetOwnerBlob,targetOwnerSize);

    AnimatorOwnerGraphRecord* current=0;
    AnimatorOwnerGraphRecord* currentWithoutResolvers=0;
    AnimatorOwnerGraphRecord* projected=0;
    AnimatorOwnerGraphRecord* stable=0;
    AnimatorOwnerGraphRecord* after=0;
    DirectClipClearPlan* plans=0;
    OwnerClearedClip* cleared=0;
    AlreadyNullClipFinalizePlan* nullFinalizePlans=0;
    EmptyBranchOutputFinalizePlan* outputFinalizePlans=0;
    SetInputWeight inputWeightSetter=0;
    uint32_t planCount=0;
    uint32_t nullFinalizeCount=0;
    uint32_t outputFinalizeCount=0;
    bool clipClearingStarted=false;
    bool success=false;
    __try {
        do {
            receipt->stage=TargetNullClipStageRevision;
            AnimatorOwnerGraphReceipt revision={};
            InitializeOwnerGraphReceipt(&revision,unityBase,animator);
            if(!VerifyOwnerRevision(unityBase,&revision)){
                receipt->result=revision.result;receipt->lastError=revision.lastError;break;
            }

            receipt->stage=TargetNullClipStageResolve;
            AnimatorOwnerGraphReceipt ownerResolve={};
            InitializeOwnerGraphReceipt(&ownerResolve,unityBase,animator);
            void* controller=0;void* constant=0;void* descriptors=0;
            if(!ResolveOwnerGraph(unityBase,animator,&ownerResolve,controller,constant,descriptors)){
                receipt->result=ownerResolve.result;receipt->lastError=ownerResolve.lastError;
                receipt->failureRecord=ownerResolve.failureRecord;break;
            }
            receipt->controller=reinterpret_cast<uintptr_t>(controller);
            receipt->controllerConstant=reinterpret_cast<uintptr_t>(constant);
            receipt->descriptors=reinterpret_cast<uintptr_t>(descriptors);
            if(!Readable(controller,0x94)){
                receipt->result=ResultUnreadable;break;
            }

            receipt->stage=TargetNullClipStageCapture;
            AnimatorOwnerGraphReceipt currentReceipt={};
            InitializeOwnerGraphReceipt(&currentReceipt,unityBase,animator);
            currentReceipt.layerCount=ownerResolve.layerCount;
            if(!CaptureOwnerAllocated(unityBase,descriptors,ownerResolve.layerCount,0,current,
                                      receipt->currentOwnerCount,receipt->currentOwnerHash,&currentReceipt)){
                receipt->result=currentReceipt.result;receipt->lastError=currentReceipt.lastError;
                receipt->failureRecord=currentReceipt.failureRecord;break;
            }
            receipt->graph=currentReceipt.graph;
            receipt->graphDirtyBefore=currentReceipt.graphDirty58;
            receipt->controllerDirtyBefore=
                *reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0x90);

            const AnimatorOwnerGraphRecord* planningCurrent=current;
            uint32_t planningCurrentCount=receipt->currentOwnerCount;
            if(receipt->currentOwnerCount<receipt->targetOwnerCount){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->targetOwnerCount;
                receipt->actualWord=receipt->currentOwnerCount;break;
            }
            if(receipt->currentOwnerCount>receipt->targetOwnerCount){
                currentWithoutResolvers=static_cast<AnimatorOwnerGraphRecord*>(
                    HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,targetOwnerSize));
                if(!currentWithoutResolvers){
                    receipt->lastError=GetLastError();receipt->result=ResultFault;break;
                }
                if(!FilterTargetNullResolverSuperset(
                       target,receipt->targetOwnerCount,current,receipt->currentOwnerCount,
                       currentWithoutResolvers,receipt))break;
                planningCurrent=currentWithoutResolvers;
                planningCurrentCount=receipt->targetOwnerCount;
            }

            plans=static_cast<DirectClipClearPlan*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                planningCurrentCount*sizeof(DirectClipClearPlan)));
            cleared=static_cast<OwnerClearedClip*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                planningCurrentCount*sizeof(OwnerClearedClip)));
            nullFinalizePlans=static_cast<AlreadyNullClipFinalizePlan*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                planningCurrentCount*sizeof(AlreadyNullClipFinalizePlan)));
            outputFinalizePlans=static_cast<EmptyBranchOutputFinalizePlan*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                planningCurrentCount*sizeof(EmptyBranchOutputFinalizePlan)));
            if(!plans||!cleared||!nullFinalizePlans||!outputFinalizePlans){
                receipt->lastError=GetLastError();receipt->result=ResultFault;break;
            }

            receipt->stage=TargetNullClipStagePlan;
            if(!BuildDirectClipClearPlan(unityBase,reinterpret_cast<uintptr_t>(controller),
                                         planningCurrent,planningCurrentCount,target,receipt->targetOwnerCount,
                                         plans,cleared,planningCurrentCount,planCount,receipt))break;
            receipt->plannedClipCount=planCount;
            if(!BuildAlreadyNullFinalizePlans(unityBase,planningCurrent,planningCurrentCount,
                                              target,receipt->targetOwnerCount,
                                              nullFinalizePlans,planningCurrentCount,nullFinalizeCount,
                                              outputFinalizePlans,planningCurrentCount,outputFinalizeCount,
                                              receipt))break;
            receipt->plannedAlreadyNullClipCount=nullFinalizeCount;
            receipt->plannedEmptyOutputCount=outputFinalizeCount;
            receipt->plannedScalarWriteCount=nullFinalizeCount*2u+outputFinalizeCount;
            OwnerProjection projection={0,0,cleared,planCount,0,0,0,0,target,receipt->targetOwnerCount,0,
                                        plans,planCount,nullFinalizePlans,nullFinalizeCount,
                                        outputFinalizePlans,outputFinalizeCount};

            receipt->stage=TargetNullClipStageProjected;
            AnimatorOwnerGraphReceipt projectedReceipt={};
            InitializeOwnerGraphReceipt(&projectedReceipt,unityBase,animator);
            projectedReceipt.layerCount=ownerResolve.layerCount;
            if(!CaptureOwnerAllocated(unityBase,descriptors,ownerResolve.layerCount,&projection,projected,
                                      receipt->projectedOwnerCount,receipt->projectedOwnerHash,&projectedReceipt)){
                receipt->result=projectedReceipt.result;receipt->lastError=projectedReceipt.lastError;
                receipt->failureRecord=projectedReceipt.failureRecord;break;
            }
            receipt->graphDirtyProjected=receipt->graphDirtyBefore;
            receipt->controllerDirtyProjected=receipt->controllerDirtyBefore|(planCount?0x01000000u:0u);
            if(receipt->projectedOwnerCount!=receipt->targetOwnerCount){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->targetOwnerCount;
                receipt->actualWord=receipt->projectedOwnerCount;break;
            }
            if(projectedReceipt.graph!=receipt->graph||
               projectedReceipt.graphDirty58!=receipt->graphDirtyProjected){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->graph;
                receipt->actualWord=projectedReceipt.graph;break;
            }

            receipt->stage=TargetNullClipStageStable;
            AnimatorOwnerGraphReceipt stableReceipt={};
            InitializeOwnerGraphReceipt(&stableReceipt,unityBase,animator);
            stableReceipt.layerCount=ownerResolve.layerCount;
            if(!CaptureOwnerAllocated(unityBase,descriptors,ownerResolve.layerCount,0,stable,
                                      receipt->stableOwnerCount,receipt->stableOwnerHash,&stableReceipt)){
                receipt->result=stableReceipt.result;receipt->lastError=stableReceipt.lastError;
                receipt->failureRecord=stableReceipt.failureRecord;break;
            }
            if(receipt->stableOwnerCount!=receipt->currentOwnerCount||stableReceipt.graph!=receipt->graph||
               stableReceipt.graphDirty58!=receipt->graphDirtyBefore||
               receipt->stableOwnerHash!=receipt->currentOwnerHash||
               !CompareTargetNullClipBytes(current,stable,receipt->currentOwnerCount,receipt)){
                if(receipt->result==0)receipt->result=ResultTopologyMismatch;break;
            }
            if(*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0x90)!=
               receipt->controllerDirtyBefore){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->controllerDirtyBefore;
                receipt->actualWord=*reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0x90);break;
            }
            AnimatorOwnerGraphReceipt finalRevision={};
            InitializeOwnerGraphReceipt(&finalRevision,unityBase,animator);
            if(!VerifyOwnerRevision(unityBase,&finalRevision)){
                receipt->result=finalRevision.result;receipt->lastError=finalRevision.lastError;break;
            }
            if(Hash(targetOwnerBlob,targetOwnerSize)!=receipt->targetOwnerHash){
                receipt->result=ResultInvalidBlob;receipt->expectedWord=receipt->targetOwnerHash;
                receipt->actualWord=Hash(targetOwnerBlob,targetOwnerSize);break;
            }
            for(uint32_t i=0;i<planCount;++i){
                const DirectClipClearPlan& plan=plans[i];
                receipt->failureLayer=plan.layerIndex;receipt->failureStateMachine=plan.stateMachineIndex;
                receipt->failureRecord=plan.currentInputRecord;
                if(!DirectClipInputTopologyExact(unityBase,plan)||
                   *reinterpret_cast<const uint32_t*>(plan.inputWeightAddress)!=plan.inputWeightBefore){
                    receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=88;
                    receipt->expectedWord=plan.inputWeightBefore;
                    receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.inputWeightAddress),4)?
                        *reinterpret_cast<const uint32_t*>(plan.inputWeightAddress):0;break;
                }
                receipt->failureRecord=plan.currentRecord;
                if(!Writable(reinterpret_cast<void*>(plan.playable+0x90),4)||
                   !Writable(reinterpret_cast<void*>(plan.playable+0x108),4)||
                   !Writable(reinterpret_cast<void*>(plan.dirtyRoot+0x90),4)||
                   !Readable(reinterpret_cast<const void*>(plan.outerWeightAddress),4)||
                   *reinterpret_cast<const uintptr_t*>(plan.playable)!=unityBase+kAnimationClipPlayableVtableRva||
                   *reinterpret_cast<const uintptr_t*>(plan.playable+0x108)!=plan.expectedClip||
                   *reinterpret_cast<const uint32_t*>(plan.playable+0x90)!=plan.playableDirtyBefore||
                   *reinterpret_cast<const uint32_t*>(plan.dirtyRoot+0x90)!=plan.rootDirtyBefore||
                   *reinterpret_cast<const uint32_t*>(plan.outerWeightAddress)!=0u){
                    receipt->result=ResultTopologyMismatch;receipt->failureByteOffset=64;
                    receipt->expectedWord=plan.playableDirtyBefore;
                    receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.playable+0x90),4)?
                        *reinterpret_cast<const uint32_t*>(plan.playable+0x90):0;break;
                }
                uintptr_t stableRoot=0;
                if(!ResolveDirectSetClipRoot(unityBase,plan.playable,
                    reinterpret_cast<uintptr_t>(controller),stableRoot,receipt,
                    planningCurrent,planningCurrentCount,plan.currentRecord)||stableRoot!=plan.dirtyRoot){
                    if(receipt->result==0){receipt->result=ResultTopologyMismatch;
                        receipt->expectedWord=plan.dirtyRoot;receipt->actualWord=stableRoot;}
                    break;
                }
            }
            for(uint32_t i=0;receipt->result==0&&i<outputFinalizeCount;++i){
                const EmptyBranchOutputFinalizePlan& plan=outputFinalizePlans[i];
                receipt->failureLayer=plan.layerIndex;receipt->failureStateMachine=plan.stateMachineIndex;
                receipt->failureRecord=plan.currentRecord;receipt->failureByteOffset=88;
                if(!EmptyOutputFinalizeTopologyExact(unityBase,plan,false)){
                    receipt->result=ResultTopologyMismatch;receipt->expectedWord=plan.weightBefore;
                    receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.outputAddress),4u)?
                        *reinterpret_cast<const uint32_t*>(plan.outputAddress):0u;
                }
            }
            for(uint32_t i=0;receipt->result==0&&i<nullFinalizeCount;++i){
                const AlreadyNullClipFinalizePlan& plan=nullFinalizePlans[i];
                receipt->failureLayer=plan.layerIndex;receipt->failureStateMachine=plan.stateMachineIndex;
                receipt->failureRecord=plan.currentRecord;receipt->failureByteOffset=64;
                if(!AlreadyNullFinalizeTopologyExact(unityBase,plan,false)){
                    receipt->result=ResultTopologyMismatch;receipt->expectedWord=plan.dirtyBefore;
                    receipt->actualWord=Readable(reinterpret_cast<const void*>(plan.playable+0x90u),4u)?
                        *reinterpret_cast<const uint32_t*>(plan.playable+0x90u):0u;
                }
            }
            if(receipt->result!=0)break;

            receipt->stage=TargetNullClipStageMutation;
            inputWeightSetter=reinterpret_cast<SetInputWeight>(unityBase+kSetInputWeightRva);
            SetClip setClip=reinterpret_cast<SetClip>(unityBase+kSetClipRva);
            if(planCount||receipt->plannedScalarWriteCount)receipt->mutationStarted=1u;
            if(!ApplyFinalizeScalars(unityBase,nullFinalizePlans,nullFinalizeCount,
                                     outputFinalizePlans,outputFinalizeCount,receipt)){
                if(RollbackFinalizeScalars(nullFinalizePlans,nullFinalizeCount,
                                           outputFinalizePlans,outputFinalizeCount,receipt))
                    receipt->mutationStarted=0u;
                break;
            }
            // Deactivate every admitted descendant first. Until the first
            // SetClip call these writes are exactly reversible, and every
            // affected mixer remains behind a verified zero-weight ancestor.
            if(!ApplyDirectClipInputWeights(unityBase,plans,planCount,false,inputWeightSetter,receipt)){
                const bool inputRollback=ApplyDirectClipInputWeights(
                    unityBase,plans,planCount,true,inputWeightSetter,receipt);
                const bool scalarRollback=RollbackFinalizeScalars(
                    nullFinalizePlans,nullFinalizeCount,outputFinalizePlans,outputFinalizeCount,receipt);
                if(inputRollback&&scalarRollback)
                    receipt->mutationStarted=0u;
                break;
            }
            if(planCount)clipClearingStarted=true;
            for(uint32_t i=0;i<planCount;++i){
                receipt->failureLayer=plans[i].layerIndex;
                receipt->failureStateMachine=plans[i].stateMachineIndex;
                receipt->failureRecord=plans[i].currentRecord;
                setClip(reinterpret_cast<void*>(plans[i].playable),0);
                ++receipt->completedClipCount;
            }

            receipt->stage=TargetNullClipStageAfterCapture;
            AnimatorOwnerGraphReceipt afterResolve={};
            InitializeOwnerGraphReceipt(&afterResolve,unityBase,animator);
            void* afterController=0;void* afterConstant=0;void* afterDescriptors=0;
            if(!ResolveOwnerGraph(unityBase,animator,&afterResolve,afterController,afterConstant,afterDescriptors)){
                receipt->result=afterResolve.result;receipt->lastError=afterResolve.lastError;break;
            }
            if(afterController!=controller||afterConstant!=constant||afterDescriptors!=descriptors||
               afterResolve.layerCount!=ownerResolve.layerCount){
                receipt->result=ResultControllerChanged;break;
            }
            AnimatorOwnerGraphReceipt afterReceipt={};
            InitializeOwnerGraphReceipt(&afterReceipt,unityBase,animator);
            afterReceipt.layerCount=afterResolve.layerCount;
            if(!CaptureOwnerAllocated(unityBase,afterDescriptors,afterResolve.layerCount,0,after,
                                      receipt->afterOwnerCount,receipt->afterOwnerHash,&afterReceipt)){
                receipt->result=afterReceipt.result;receipt->lastError=afterReceipt.lastError;
                receipt->failureRecord=afterReceipt.failureRecord;break;
            }
            receipt->graphDirtyAfter=afterReceipt.graphDirty58;
            receipt->controllerDirtyAfter=
                *reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(afterController)+0x90);

            receipt->stage=TargetNullClipStageAfterCompare;
            if(receipt->completedClipCount!=receipt->plannedClipCount||
               receipt->completedScalarWriteCount!=receipt->plannedScalarWriteCount||
               receipt->afterOwnerCount!=receipt->projectedOwnerCount||
               afterReceipt.graph!=receipt->graph||
               receipt->graphDirtyAfter!=receipt->graphDirtyProjected||
               receipt->controllerDirtyAfter!=receipt->controllerDirtyProjected){
                receipt->result=ResultWriteMismatch;receipt->expectedWord=receipt->controllerDirtyProjected;
                receipt->actualWord=receipt->controllerDirtyAfter;break;
            }
            if(!CompareTargetNullClipBytes(projected,after,receipt->projectedOwnerCount,receipt)||
               receipt->afterOwnerHash!=receipt->projectedOwnerHash){
                receipt->result=ResultWriteMismatch;break;
            }
            receipt->failureLayer=0xFFFFFFFFu;receipt->failureStateMachine=0xFFFFFFFFu;
            receipt->failureRecord=0xFFFFFFFFu;receipt->failureByteOffset=0xFFFFFFFFu;
            receipt->expectedWord=0;receipt->actualWord=0;
            receipt->stage=TargetNullClipStageComplete;receipt->result=ResultOk;success=true;
        } while(false);
    }
    __except(EXCEPTION_EXECUTE_HANDLER){
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;
        if(receipt->mutationStarted&&!clipClearingStarted){
            __try {
                const bool inputRollback=!plans||!inputWeightSetter||
                    ApplyDirectClipInputWeights(unityBase,plans,planCount,true,inputWeightSetter,receipt);
                const bool scalarRollback=!nullFinalizePlans||!outputFinalizePlans||
                    RollbackFinalizeScalars(nullFinalizePlans,nullFinalizeCount,
                                            outputFinalizePlans,outputFinalizeCount,receipt);
                if(inputRollback&&scalarRollback)
                    receipt->mutationStarted=0u;
            }
            __except(EXCEPTION_EXECUTE_HANDLER){receipt->lastError=GetExceptionCode();}
        }
    }
    if(!success&&receipt->mutationStarted&&!clipClearingStarted){
        __try {
            const bool inputRollback=!plans||!inputWeightSetter||
                ApplyDirectClipInputWeights(unityBase,plans,planCount,true,inputWeightSetter,receipt);
            const bool scalarRollback=!nullFinalizePlans||!outputFinalizePlans||
                RollbackFinalizeScalars(nullFinalizePlans,nullFinalizeCount,
                                        outputFinalizePlans,outputFinalizeCount,receipt);
            if(inputRollback&&scalarRollback)receipt->mutationStarted=0u;
        }
        __except(EXCEPTION_EXECUTE_HANDLER){receipt->lastError=GetExceptionCode();}
    }
    if(after)HeapFree(GetProcessHeap(),0,after);
    if(stable)HeapFree(GetProcessHeap(),0,stable);
    if(projected)HeapFree(GetProcessHeap(),0,projected);
    if(cleared)HeapFree(GetProcessHeap(),0,cleared);
    if(outputFinalizePlans)HeapFree(GetProcessHeap(),0,outputFinalizePlans);
    if(nullFinalizePlans)HeapFree(GetProcessHeap(),0,nullFinalizePlans);
    if(plans)HeapFree(GetProcessHeap(),0,plans);
    if(currentWithoutResolvers)HeapFree(GetProcessHeap(),0,currentWithoutResolvers);
    if(current)HeapFree(GetProcessHeap(),0,current);
    return success?1:0;
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_override_clip_playables_probe(
    uintptr_t unityBase,uintptr_t animator,AnimatorOverrideClipReceipt* receipt) {
    if(!receipt)return 0;
    InitializeOverrideClipReceipt(receipt,unityBase,animator);
    receipt->stage=OverrideClipStageArguments;
    if(!unityBase||!animator){receipt->result=ResultBadArgument;return 0;}

    bool mutationStarted=false;
    __try {
        receipt->stage=OverrideClipStageRevision;
        const void* method=reinterpret_cast<const void*>(unityBase+kOverrideClipPlayablesRva);
        if(!Readable(method,0x1C9)){receipt->result=ResultUnreadable;return 0;}
        if(Hash(method,0x1C9)!=0x3E05AE4Au){receipt->result=ResultRevisionMismatch;return 0;}
        AnimatorOwnerGraphReceipt ownerRevision={};
        InitializeOwnerGraphReceipt(&ownerRevision,unityBase,animator);
        if(!VerifyOwnerRevision(unityBase,&ownerRevision)){
            receipt->result=ownerRevision.result;receipt->lastError=ownerRevision.lastError;return 0;
        }

        receipt->stage=OverrideClipStageResolve;
        AnimatorControllerReceipt controllerReceipt={};
        InitializeReceipt(&controllerReceipt,unityBase,animator);
        void* controller=0;void* memory=0;void* allocator=0;
        if(!Resolve(unityBase,animator,&controllerReceipt,controller,memory,allocator)){
            receipt->result=controllerReceipt.result;receipt->lastError=controllerReceipt.lastError;return 0;
        }
        AnimatorOwnerGraphReceipt ownerResolve={};
        InitializeOwnerGraphReceipt(&ownerResolve,unityBase,animator);
        void* ownerController=0;void* constant=0;void* descriptors=0;
        if(!ResolveOwnerGraph(unityBase,animator,&ownerResolve,ownerController,constant,descriptors)){
            receipt->result=ownerResolve.result;receipt->lastError=ownerResolve.lastError;return 0;
        }
        if(ownerController!=controller||!Readable(controller,0x94)){
            receipt->result=ResultControllerChanged;return 0;
        }
        receipt->controller=reinterpret_cast<uintptr_t>(controller);
        receipt->controllerMemory=reinterpret_cast<uintptr_t>(memory);

        receipt->stage=OverrideClipStageBeforeCapture;
        if(!CaptureControllerMemoryDigest(unityBase,memory,allocator,
                                          receipt->memorySizeBefore,receipt->memoryHashBefore,receipt))return 0;
        uintptr_t graphBefore=0;
        if(!CaptureOwnerDigest(unityBase,controller,constant,descriptors,ownerResolve.layerCount,
                               receipt->ownerCountBefore,receipt->ownerHashBefore,
                               graphBefore,receipt->graphDirtyBefore,receipt))return 0;
        receipt->graph=graphBefore;
        receipt->controllerDirty9093Before=
            *reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0x90);
        if(receipt->controllerDirty9093Before!=0u){
            receipt->result=ResultTopologyMismatch;return 0;
        }

        receipt->stage=OverrideClipStageMutation;
        mutationStarted=true;
        OverrideClipPlayables overrideClips=
            reinterpret_cast<OverrideClipPlayables>(unityBase+kOverrideClipPlayablesRva);
        overrideClips(controller);

        receipt->stage=OverrideClipStageAfterCapture;
        AnimatorControllerReceipt afterControllerReceipt={};
        InitializeReceipt(&afterControllerReceipt,unityBase,animator);
        void* afterController=0;void* afterMemory=0;void* afterAllocator=0;
        if(!Resolve(unityBase,animator,&afterControllerReceipt,afterController,afterMemory,afterAllocator)){
            receipt->result=afterControllerReceipt.result;receipt->lastError=afterControllerReceipt.lastError;return 0;
        }
        AnimatorOwnerGraphReceipt afterOwnerResolve={};
        InitializeOwnerGraphReceipt(&afterOwnerResolve,unityBase,animator);
        void* afterOwnerController=0;void* afterConstant=0;void* afterDescriptors=0;
        if(!ResolveOwnerGraph(unityBase,animator,&afterOwnerResolve,
                              afterOwnerController,afterConstant,afterDescriptors)){
            receipt->result=afterOwnerResolve.result;receipt->lastError=afterOwnerResolve.lastError;return 0;
        }
        if(afterController!=controller||afterOwnerController!=controller||afterMemory!=memory||
           afterConstant!=constant||afterDescriptors!=descriptors||!Readable(afterController,0x94)){
            receipt->result=ResultControllerChanged;return 0;
        }
        if(!CaptureControllerMemoryDigest(unityBase,afterMemory,afterAllocator,
                                          receipt->memorySizeAfter,receipt->memoryHashAfter,receipt))return 0;
        uintptr_t graphAfter=0;
        if(!CaptureOwnerDigest(unityBase,afterController,afterConstant,afterDescriptors,
                               afterOwnerResolve.layerCount,receipt->ownerCountAfter,receipt->ownerHashAfter,
                               graphAfter,receipt->graphDirtyAfter,receipt))return 0;
        if(graphAfter!=graphBefore){receipt->result=ResultControllerChanged;return 0;}
        receipt->controllerDirty9093After=
            *reinterpret_cast<const uint32_t*>(reinterpret_cast<uintptr_t>(afterController)+0x90);
        if(receipt->controllerDirty9093After!=0x01000000u){
            receipt->result=ResultWriteMismatch;return 0;
        }
        receipt->stage=OverrideClipStageComplete;
        receipt->result=ResultOk;return 1;
    }
    __except(EXCEPTION_EXECUTE_HANDLER){
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;
        if(mutationStarted&&receipt->stage<OverrideClipStageMutation)receipt->stage=OverrideClipStageMutation;
        return 0;
    }
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_settled_end_transition_normalize(
    uintptr_t unityBase,uintptr_t animator,
    const void* targetTopologyBlob,uint32_t targetTopologySize,
    const void* targetOwnerBlob,uint32_t targetOwnerSize,
    uint32_t requireNoPlan,
    AnimatorEndTransitionReceipt* receipt) {
    if(!receipt)return 0;
    InitializeEndTransitionReceipt(receipt,unityBase,animator);
    receipt->stage=EndTransitionStageArguments;
    if(!unityBase||!animator||!targetTopologyBlob||!targetOwnerBlob||
       targetTopologySize==0||targetTopologySize%sizeof(AnimatorTransitionTopologyLayer)!=0||
       targetOwnerSize==0||targetOwnerSize%sizeof(AnimatorOwnerGraphRecord)!=0||
       targetTopologySize/sizeof(AnimatorTransitionTopologyLayer)>kMaximumTopologyLayers||
       targetOwnerSize/sizeof(AnimatorOwnerGraphRecord)>kMaximumOwnerGraphRecords||requireNoPlan>1u||
       !Readable(targetTopologyBlob,targetTopologySize)||!Readable(targetOwnerBlob,targetOwnerSize)){
        receipt->result=ResultBadArgument;return 0;
    }

    const AnimatorTransitionTopologyLayer* targetTopology=
        static_cast<const AnimatorTransitionTopologyLayer*>(targetTopologyBlob);
    const AnimatorOwnerGraphRecord* targetOwner=
        static_cast<const AnimatorOwnerGraphRecord*>(targetOwnerBlob);
    receipt->targetTopologyCount=targetTopologySize/sizeof(AnimatorTransitionTopologyLayer);
    receipt->targetOwnerCount=targetOwnerSize/sizeof(AnimatorOwnerGraphRecord);
    receipt->targetTopologyHash=Hash(targetTopologyBlob,targetTopologySize);
    receipt->targetOwnerHash=Hash(targetOwnerBlob,targetOwnerSize);

    AnimatorOwnerGraphRecord* currentOwner=0;
    AnimatorOwnerGraphRecord* projectedOwner=0;
    AnimatorOwnerGraphRecord* stableOwner=0;
    AnimatorOwnerGraphRecord* afterOwner=0;
    OwnerRotationPlan* plans=0;
    OwnerClearedClip* cleared=0;
    OwnerStagedClip* staged=0;
    OwnerReboundClip* rebound=0;
    OwnerWeightPlan* weightPlans=0;
    uint32_t weightPlanCount=0;
    SetInputWeight mutationWeightSetter=0;
    bool reversiblePrimeChanged=false;
    bool admittedResolverSuperset=false;
    bool success=false;
    __try {
        do {
            receipt->stage=EndTransitionStageRevision;
            AnimatorMixerGraphReceipt mixerRevision={};
            InitializeMixerGraphReceipt(&mixerRevision,unityBase,animator);
            if(!VerifyMixerRevision(unityBase,&mixerRevision)){
                receipt->result=mixerRevision.result;receipt->lastError=mixerRevision.lastError;break;
            }
            AnimatorOwnerGraphReceipt ownerRevision={};
            InitializeOwnerGraphReceipt(&ownerRevision,unityBase,animator);
            if(!VerifyOwnerRevision(unityBase,&ownerRevision)){CopyOwnerFailure(ownerRevision,receipt);break;}

            receipt->stage=EndTransitionStageResolve;
            AnimatorMixerGraphReceipt mixer={};
            InitializeMixerGraphReceipt(&mixer,unityBase,animator);
            void* controller=0;void* constant=0;void* descriptors=0;
            if(!ResolveMixerGraph(unityBase,animator,&mixer,controller,constant,descriptors)){
                receipt->result=mixer.result;receipt->lastError=mixer.lastError;break;
            }
            receipt->controller=reinterpret_cast<uintptr_t>(controller);
            receipt->controllerConstant=reinterpret_cast<uintptr_t>(constant);
            receipt->descriptors=reinterpret_cast<uintptr_t>(descriptors);
            if(!Readable(controller,0xC0)){receipt->result=ResultUnreadable;break;}
            void* memory=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xB4);
            if(!Readable(memory,0x20)){receipt->result=ResultUnreadable;break;}

            receipt->stage=EndTransitionStageLifecycleCapture;
            AnimatorTransitionTopologyLayer currentTopology[kMaximumTopologyLayers]={};
            uint32_t currentTopologyCount=0,currentTopologyHash=0;
            if(!CaptureNormalizationTopology(controller,memory,currentTopology,kMaximumTopologyLayers,
                                              currentTopologyCount,currentTopologyHash,receipt))break;
            receipt->currentTopologyCount=currentTopologyCount;
            receipt->currentTopologyHash=currentTopologyHash;

            receipt->stage=EndTransitionStageLifecyclePreflight;
            if(currentTopologyCount!=receipt->targetTopologyCount){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->targetTopologyCount;
                receipt->actualWord=currentTopologyCount;break;
            }
            if(!CompareNormalizationBytes(targetTopology,currentTopology,targetTopologySize,
                                           sizeof(AnimatorTransitionTopologyLayer),false,receipt)){
                receipt->result=ResultTopologyMismatch;break;
            }
            if(!ValidateSettledTopology(currentTopology,currentTopologyCount,receipt))break;

            receipt->stage=EndTransitionStageOwnerCapture;
            AnimatorOwnerGraphReceipt currentReceipt={};
            InitializeOwnerGraphReceipt(&currentReceipt,unityBase,animator);
            currentReceipt.controller=receipt->controller;
            currentReceipt.controllerConstant=receipt->controllerConstant;
            currentReceipt.descriptors=receipt->descriptors;
            currentReceipt.layerCount=mixer.layerCount;
            uint32_t currentOwnerCount=0,currentOwnerHash=0;
            if(!CaptureOwnerAllocated(unityBase,descriptors,mixer.layerCount,0,currentOwner,
                                      currentOwnerCount,currentOwnerHash,&currentReceipt)){
                CopyOwnerFailure(currentReceipt,receipt);break;
            }
            receipt->currentOwnerCount=currentOwnerCount;receipt->currentOwnerHash=currentOwnerHash;
            receipt->graph=currentReceipt.graph;receipt->graphDirtyBefore=currentReceipt.graphDirty58;

            plans=static_cast<OwnerRotationPlan*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                                                           currentOwnerCount*sizeof(OwnerRotationPlan)));
            cleared=static_cast<OwnerClearedClip*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                                                              currentOwnerCount*sizeof(OwnerClearedClip)));
            staged=static_cast<OwnerStagedClip*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                                                           currentOwnerCount*sizeof(OwnerStagedClip)));
            rebound=static_cast<OwnerReboundClip*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                                                             currentOwnerCount*sizeof(OwnerReboundClip)));
            weightPlans=static_cast<OwnerWeightPlan*>(HeapAlloc(GetProcessHeap(),HEAP_ZERO_MEMORY,
                                                                currentOwnerCount*sizeof(OwnerWeightPlan)));
            if(!plans||!cleared||!staged||!rebound||!weightPlans){
                receipt->lastError=GetLastError();receipt->result=ResultFault;break;
            }

            receipt->stage=EndTransitionStageRotationPlan;
            uint32_t planCount=0,clearedCount=0,stagedCount=0,reboundCount=0;
            if(!BuildOwnerProjection(unityBase,receipt->controller,currentOwner,currentOwnerCount,
                                     targetOwner,receipt->targetOwnerCount,
                                     plans,currentOwnerCount,planCount,cleared,currentOwnerCount,clearedCount,
                                     staged,currentOwnerCount,stagedCount,
                                     rebound,currentOwnerCount,reboundCount,receipt))break;
            receipt->plannedTransitionCount=planCount;
            receipt->plannedReboundClipCount=reboundCount;
            if(!BuildOwnerWeightPlans(unityBase,plans,planCount,currentOwner,currentOwnerCount,
                                      targetOwner,receipt->targetOwnerCount,
                                      weightPlans,currentOwnerCount,weightPlanCount,receipt))break;
            receipt->plannedWeightPlanCount=weightPlanCount;
            if(requireNoPlan&&planCount!=0u){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=0u;
                receipt->actualWord=planCount;break;
            }
            OwnerProjection projection={plans,planCount,cleared,clearedCount,staged,stagedCount,
                                         rebound,reboundCount,
                                         targetOwner,receipt->targetOwnerCount,planCount?1u:0u,0,0,
                                         0,0,0,0};
            if(!Readable(reinterpret_cast<const void*>(receipt->controller+0x90u),4u)){
                receipt->result=ResultUnreadable;break;
            }
            receipt->controllerDirtyBefore=*reinterpret_cast<const uint32_t*>(receipt->controller+0x90u);
            bool endTransitionDirtiesController=false;
            for(uint32_t i=0;i<clearedCount;++i)
                if(cleared[i].dirtyRoot==receipt->controller)endTransitionDirtiesController=true;
            for(uint32_t i=0;i<reboundCount;++i)
                if(rebound[i].targetBranchIndex==1u&&rebound[i].dirtyRoot==receipt->controller)
                    endTransitionDirtiesController=true;
            receipt->controllerDirtyAfterEnd=endTransitionDirtiesController?
                ((receipt->controllerDirtyBefore&0x00FFFFFFu)|0x01000000u):
                receipt->controllerDirtyBefore;
            receipt->controllerDirtyProjected=IsOwnerDirtyRoot(&projection,receipt->controller)?
                ((receipt->controllerDirtyBefore&0x00FFFFFFu)|0x01000000u):
                receipt->controllerDirtyBefore;

            receipt->stage=EndTransitionStageProjectedCapture;
            AnimatorOwnerGraphReceipt projectedReceipt={};
            InitializeOwnerGraphReceipt(&projectedReceipt,unityBase,animator);
            projectedReceipt.layerCount=mixer.layerCount;
            uint32_t projectedOwnerCount=0,projectedOwnerHash=0;
            if(!CaptureOwnerAllocated(unityBase,descriptors,mixer.layerCount,&projection,projectedOwner,
                                      projectedOwnerCount,projectedOwnerHash,&projectedReceipt)){
                CopyOwnerFailure(projectedReceipt,receipt);break;
            }
            receipt->projectedOwnerCount=projectedOwnerCount;
            receipt->projectedOwnerHash=projectedOwnerHash;
            receipt->graphDirtyProjected=receipt->graphDirtyBefore|(planCount?8u:0u);
            if(projectedReceipt.graph!=receipt->graph){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->graph;
                receipt->actualWord=projectedReceipt.graph;break;
            }
            if(projectedReceipt.graphDirty58!=receipt->graphDirtyBefore){
                receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->graphDirtyBefore;
                receipt->actualWord=projectedReceipt.graphDirty58;break;
            }

            receipt->stage=EndTransitionStageProjectedCompare;
            admittedResolverSuperset=requireNoPlan==0u&&planCount==0u&&clearedCount==0u&&
                stagedCount==0u&&reboundCount==0u&&weightPlanCount==0u&&
                projectedOwnerCount>receipt->targetOwnerCount&&
                projectedOwnerCount==currentOwnerCount;
            if(admittedResolverSuperset&&
               (!CompareNormalizationBytes(currentOwner,projectedOwner,
                    currentOwnerCount*sizeof(AnimatorOwnerGraphRecord),
                    sizeof(AnimatorOwnerGraphRecord),true,receipt)||
                projectedOwnerHash!=currentOwnerHash)){
                receipt->result=ResultTopologyMismatch;
                if(receipt->failureRecord==0xFFFFFFFFu){
                    receipt->expectedWord=currentOwnerHash;receipt->actualWord=projectedOwnerHash;
                }
                break;
            }
            if(projectedOwnerCount==receipt->targetOwnerCount){
                if(!CompareOwnerAllowPendingVisit(
                    targetOwner,projectedOwner,targetOwnerSize,&projection,receipt)){
                    receipt->result=ResultTopologyMismatch;break;
                }
            } else if(!admittedResolverSuperset||
                      !CompareOwnerResolverSupersetAllowPendingVisit(
                          targetOwner,receipt->targetOwnerCount,
                          projectedOwner,projectedOwnerCount,&projection,receipt)){
                if(receipt->result==0u){
                    receipt->result=ResultTopologyMismatch;receipt->expectedWord=receipt->targetOwnerCount;
                    receipt->actualWord=projectedOwnerCount;
                }
                break;
            }

            // Close the observation-to-mutation gap with a second complete
            // unprojected capture. A mismatch is rejected before EndTransition.
            AnimatorOwnerGraphReceipt stableReceipt={};
            InitializeOwnerGraphReceipt(&stableReceipt,unityBase,animator);
            stableReceipt.layerCount=mixer.layerCount;
            uint32_t stableCount=0,stableHash=0;
            if(!CaptureOwnerAllocated(unityBase,descriptors,mixer.layerCount,0,stableOwner,
                                      stableCount,stableHash,&stableReceipt)){
                CopyOwnerFailure(stableReceipt,receipt);break;
            }
            if(stableCount!=currentOwnerCount){receipt->result=ResultTopologyMismatch;
                receipt->expectedWord=currentOwnerCount;receipt->actualWord=stableCount;break;}
            if(stableReceipt.graph!=receipt->graph){receipt->result=ResultTopologyMismatch;
                receipt->expectedWord=receipt->graph;receipt->actualWord=stableReceipt.graph;break;}
            if(stableReceipt.graphDirty58!=receipt->graphDirtyBefore){receipt->result=ResultTopologyMismatch;
                receipt->expectedWord=receipt->graphDirtyBefore;receipt->actualWord=stableReceipt.graphDirty58;break;}
            if(!CompareNormalizationBytes(currentOwner,stableOwner,currentOwnerCount*sizeof(AnimatorOwnerGraphRecord),
                                           sizeof(AnimatorOwnerGraphRecord),true,receipt)){
                receipt->result=ResultTopologyMismatch;break;
            }
            if(stableHash!=currentOwnerHash){receipt->result=ResultTopologyMismatch;
                receipt->expectedWord=currentOwnerHash;receipt->actualWord=stableHash;break;}
            AnimatorTransitionTopologyLayer stableTopology[kMaximumTopologyLayers]={};
            uint32_t stableTopologyCount=0,stableTopologyHash=0;
            if(!CaptureNormalizationTopology(controller,memory,stableTopology,kMaximumTopologyLayers,
                                              stableTopologyCount,stableTopologyHash,receipt))break;
            if(stableTopologyCount!=currentTopologyCount){receipt->result=ResultTopologyMismatch;
                receipt->expectedWord=currentTopologyCount;receipt->actualWord=stableTopologyCount;break;}
            if(!CompareNormalizationBytes(currentTopology,stableTopology,targetTopologySize,
                                           sizeof(AnimatorTransitionTopologyLayer),false,receipt)){
                receipt->result=ResultTopologyMismatch;break;
            }
            if(stableTopologyHash!=currentTopologyHash){receipt->result=ResultTopologyMismatch;
                receipt->expectedWord=currentTopologyHash;receipt->actualWord=stableTopologyHash;break;}
            if(!VerifyRotationPreimages(unityBase,plans,planCount,receipt->graph,receipt))break;
            if(!VerifyOwnerWeightPreimages(weightPlans,weightPlanCount,receipt))break;
            if(!VerifyReboundClipPreimages(unityBase,receipt->controller,rebound,reboundCount,
                                           currentOwner,currentOwnerCount,targetOwner,
                                           receipt->targetOwnerCount,receipt))break;
            if(!VerifyPreClearClipPreimages(unityBase,receipt->controller,cleared,clearedCount,
                                            weightPlans,weightPlanCount,currentOwner,currentOwnerCount,
                                            receipt))break;
            if(Hash(targetTopologyBlob,targetTopologySize)!=receipt->targetTopologyHash||
               Hash(targetOwnerBlob,targetOwnerSize)!=receipt->targetOwnerHash){
                receipt->result=ResultBlobMismatch;receipt->failureByteOffset=0;
                receipt->expectedWord=receipt->targetOwnerHash;
                receipt->actualWord=Hash(targetOwnerBlob,targetOwnerSize);break;
            }
            AnimatorOwnerGraphReceipt finalRevision={};
            InitializeOwnerGraphReceipt(&finalRevision,unityBase,animator);
            if(!VerifyOwnerRevision(unityBase,&finalRevision)){CopyOwnerFailure(finalRevision,receipt);break;}

            receipt->stage=EndTransitionStageWeightPrime;
            EndTransition endTransition=reinterpret_cast<EndTransition>(unityBase+kEndTransitionRva);
            SetClip setClip=reinterpret_cast<SetClip>(unityBase+kSetClipRva);
            mutationWeightSetter=reinterpret_cast<SetInputWeight>(unityBase+kSetInputWeightRva);
            if(!ApplyPrimeOwnerWeights(weightPlans,weightPlanCount,mutationWeightSetter,
                                       reversiblePrimeChanged,receipt)){
                if(reversiblePrimeChanged)
                    RollbackPrimeOwnerWeights(weightPlans,weightPlanCount,mutationWeightSetter,receipt);
                reversiblePrimeChanged=false;
                break;
            }
            receipt->stage=EndTransitionStageMutation;
            if(planCount)receipt->mutationStarted=1u;
            if(!ApplyPreClearClips(unityBase,receipt->controller,cleared,clearedCount,
                                   weightPlans,weightPlanCount,setClip,receipt))break;
            for(uint32_t i=0;i<planCount;++i){
                receipt->failureLayer=plans[i].layerIndex;
                receipt->failureStateMachine=plans[i].stateMachineIndex;
                endTransition(reinterpret_cast<void*>(plans[i].outer));
                ++receipt->completedTransitionCount;
            }

            if(receipt->graph&&Readable(reinterpret_cast<const void*>(receipt->graph+0x58u),4u)){
                const uint32_t graphDirty=*reinterpret_cast<const uint32_t*>(receipt->graph+0x58u);
                if(graphDirty!=receipt->graphDirtyProjected){receipt->result=ResultWriteMismatch;
                    receipt->failureByteOffset=88;receipt->expectedWord=receipt->graphDirtyProjected;
                    receipt->actualWord=graphDirty;break;}
            } else {receipt->result=ResultUnreadable;break;}
            if(!VerifyOwnerWeightsAfterEnd(weightPlans,weightPlanCount,receipt))break;
            if(!Readable(reinterpret_cast<const void*>(receipt->controller+0x90u),4u)||
               *reinterpret_cast<const uint32_t*>(receipt->controller+0x90u)!=receipt->controllerDirtyAfterEnd){
                receipt->result=ResultWriteMismatch;receipt->failureByteOffset=64;
                receipt->expectedWord=receipt->controllerDirtyAfterEnd;
                receipt->actualWord=Readable(reinterpret_cast<const void*>(receipt->controller+0x90u),4u)?
                    *reinterpret_cast<const uint32_t*>(receipt->controller+0x90u):0;break;
            }
            AnimatorTransitionTopologyLayer postEndTopology[kMaximumTopologyLayers]={};
            uint32_t postEndTopologyCount=0,postEndTopologyHash=0;
            if(!CaptureNormalizationTopology(controller,memory,postEndTopology,kMaximumTopologyLayers,
                                              postEndTopologyCount,postEndTopologyHash,receipt))break;
            if(postEndTopologyCount!=receipt->targetTopologyCount||
               !CompareNormalizationBytes(targetTopology,postEndTopology,targetTopologySize,
                                           sizeof(AnimatorTransitionTopologyLayer),false,receipt)){
                receipt->result=ResultWriteMismatch;break;
            }

            receipt->stage=EndTransitionStageRebind;
            if(!ApplyOwnerReboundClips(unityBase,receipt->controller,rebound,reboundCount,
                                       setClip,receipt))break;

            receipt->stage=EndTransitionStageWeights;
            if(!ApplyFinalOwnerWeights(weightPlans,weightPlanCount,mutationWeightSetter,receipt))break;

            receipt->stage=EndTransitionStageAfterCapture;
            AnimatorOwnerGraphReceipt afterResolve={};
            InitializeOwnerGraphReceipt(&afterResolve,unityBase,animator);
            void* afterController=0;void* afterConstant=0;void* afterDescriptors=0;
            if(!ResolveOwnerGraph(unityBase,animator,&afterResolve,afterController,afterConstant,afterDescriptors)){
                CopyOwnerFailure(afterResolve,receipt);break;
            }
            if(reinterpret_cast<uintptr_t>(afterController)!=receipt->controller||
               reinterpret_cast<uintptr_t>(afterConstant)!=receipt->controllerConstant||
               reinterpret_cast<uintptr_t>(afterDescriptors)!=receipt->descriptors||
               afterResolve.layerCount!=mixer.layerCount){
                receipt->result=ResultControllerChanged;receipt->expectedWord=receipt->controller;
                receipt->actualWord=reinterpret_cast<uintptr_t>(afterController);break;
            }
            void* afterMemory=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(afterController)+0xB4);
            if(afterMemory!=memory){receipt->result=ResultControllerChanged;
                receipt->expectedWord=reinterpret_cast<uintptr_t>(memory);
                receipt->actualWord=reinterpret_cast<uintptr_t>(afterMemory);break;}

            AnimatorOwnerGraphReceipt afterReceipt={};
            InitializeOwnerGraphReceipt(&afterReceipt,unityBase,animator);
            afterReceipt.layerCount=mixer.layerCount;
            uint32_t afterOwnerCount=0,afterOwnerHash=0;
            if(!CaptureOwnerAllocated(unityBase,afterDescriptors,mixer.layerCount,0,afterOwner,
                                      afterOwnerCount,afterOwnerHash,&afterReceipt)){
                CopyOwnerFailure(afterReceipt,receipt);break;
            }
            receipt->afterOwnerCount=afterOwnerCount;receipt->afterOwnerHash=afterOwnerHash;
            receipt->graphDirtyAfter=afterReceipt.graphDirty58;
            receipt->controllerDirtyAfter=Readable(reinterpret_cast<const void*>(receipt->controller+0x90u),4u)?
                *reinterpret_cast<const uint32_t*>(receipt->controller+0x90u):0;
            AnimatorTransitionTopologyLayer afterTopology[kMaximumTopologyLayers]={};
            uint32_t afterTopologyCount=0,afterTopologyHash=0;
            if(!CaptureNormalizationTopology(afterController,afterMemory,afterTopology,kMaximumTopologyLayers,
                                              afterTopologyCount,afterTopologyHash,receipt))break;

            receipt->stage=EndTransitionStageAfterCompare;
            if(admittedResolverSuperset){
                if(afterOwnerCount!=projectedOwnerCount){receipt->result=ResultWriteMismatch;
                    receipt->expectedWord=projectedOwnerCount;receipt->actualWord=afterOwnerCount;break;}
            } else if(afterOwnerCount!=receipt->targetOwnerCount){receipt->result=ResultWriteMismatch;
                receipt->expectedWord=receipt->targetOwnerCount;receipt->actualWord=afterOwnerCount;break;}
            if(afterTopologyCount!=receipt->targetTopologyCount){receipt->result=ResultWriteMismatch;
                receipt->expectedWord=receipt->targetTopologyCount;receipt->actualWord=afterTopologyCount;break;}
            if(afterReceipt.graph!=receipt->graph){receipt->result=ResultWriteMismatch;
                receipt->expectedWord=receipt->graph;receipt->actualWord=afterReceipt.graph;break;}
            if(receipt->graphDirtyAfter!=receipt->graphDirtyProjected){receipt->result=ResultWriteMismatch;
                receipt->expectedWord=receipt->graphDirtyProjected;receipt->actualWord=receipt->graphDirtyAfter;break;}
            if(receipt->controllerDirtyAfter!=receipt->controllerDirtyProjected){receipt->result=ResultWriteMismatch;
                receipt->expectedWord=receipt->controllerDirtyProjected;
                receipt->actualWord=receipt->controllerDirtyAfter;break;}
            const bool ownerPostimageExact=admittedResolverSuperset?
                (afterOwnerHash==projectedOwnerHash&&
                 CompareNormalizationBytes(projectedOwner,afterOwner,
                    projectedOwnerCount*sizeof(AnimatorOwnerGraphRecord),
                    sizeof(AnimatorOwnerGraphRecord),true,receipt)&&
                 CompareOwnerResolverSupersetAllowPendingVisit(
                    targetOwner,receipt->targetOwnerCount,afterOwner,afterOwnerCount,&projection,receipt)):
                CompareOwnerAllowPendingVisit(targetOwner,afterOwner,targetOwnerSize,&projection,receipt);
            if(!ownerPostimageExact||
               !CompareNormalizationBytes(targetTopology,afterTopology,targetTopologySize,
                                           sizeof(AnimatorTransitionTopologyLayer),false,receipt)){
                receipt->result=ResultWriteMismatch;break;
            }
            receipt->failureLayer=0xFFFFFFFFu;receipt->failureStateMachine=0xFFFFFFFFu;
            receipt->failureRecord=0xFFFFFFFFu;receipt->failureByteOffset=0xFFFFFFFFu;
            receipt->expectedWord=0;receipt->actualWord=0;
            receipt->stage=EndTransitionStageComplete;receipt->result=ResultOk;success=true;
        } while(false);
    }
    __except(EXCEPTION_EXECUTE_HANDLER){
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;
    }
    if(!receipt->mutationStarted&&reversiblePrimeChanged&&weightPlans&&mutationWeightSetter){
        __try { RollbackPrimeOwnerWeights(weightPlans,weightPlanCount,mutationWeightSetter,receipt); }
        __except(EXCEPTION_EXECUTE_HANDLER){ receipt->rollbackFailure=1u; }
        reversiblePrimeChanged=false;
    }
    if(afterOwner)HeapFree(GetProcessHeap(),0,afterOwner);
    if(stableOwner)HeapFree(GetProcessHeap(),0,stableOwner);
    if(projectedOwner)HeapFree(GetProcessHeap(),0,projectedOwner);
    if(rebound)HeapFree(GetProcessHeap(),0,rebound);
    if(weightPlans)HeapFree(GetProcessHeap(),0,weightPlans);
    if(cleared)HeapFree(GetProcessHeap(),0,cleared);
    if(staged)HeapFree(GetProcessHeap(),0,staged);
    if(plans)HeapFree(GetProcessHeap(),0,plans);
    if(currentOwner)HeapFree(GetProcessHeap(),0,currentOwner);
    return success?1:0;
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_controller_normalize(
    uintptr_t unityBase,uintptr_t animator,AnimatorControllerReceipt* receipt) {
    if (!receipt) return 0;
    InitializeReceipt(receipt,unityBase,animator);
    if (!unityBase || !animator) { receipt->result=ResultBadArgument; return 0; }
    void* controller=0;void* memory=0;void* allocator=0;
    if (!Resolve(unityBase,animator,receipt,controller,memory,allocator)) return 0;
    void* before=0;void* after=0;
    __try {
        CopyControllerMemory copy=reinterpret_cast<CopyControllerMemory>(unityBase+kCopyControllerMemoryRva);
        before=copy(memory,allocator,&receipt->blobSizeBefore);
        if(!before||receipt->blobSizeBefore<0x20||receipt->blobSizeBefore>kMaximumBlobSize) {
            receipt->result=ResultInvalidBlob;Release(allocator,before);return 0;
        }
        receipt->hashBefore=Hash(before,receipt->blobSizeBefore);
        void* animatorAllocator=reinterpret_cast<void*>(animator+0x90);
        if(!Readable(animatorAllocator,4)) {
            receipt->result=ResultUnreadable;Release(allocator,before);return 0;
        }
        NormalizeControllerMemory normalize=
            reinterpret_cast<NormalizeControllerMemory>(unityBase+kNormalizeControllerMemoryRva);
        normalize(controller,animatorAllocator);
        void* memoryAfter=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xB4);
        uint32_t capacity=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0xBC);
        receipt->memoryAfter=reinterpret_cast<uintptr_t>(memoryAfter);
        if(!Readable(memoryAfter,0x20)||capacity<receipt->blobSizeBefore||
            (reinterpret_cast<uintptr_t>(memoryAfter)&15u)!=0) {
            receipt->result=ResultControllerChanged;Release(allocator,before);return 0;
        }
        after=copy(memoryAfter,allocator,&receipt->blobSizeAfter);
        if(!after||receipt->blobSizeAfter<0x20||receipt->blobSizeAfter>kMaximumBlobSize) {
            receipt->result=ResultInvalidBlob;Release(allocator,after);Release(allocator,before);return 0;
        }
        receipt->hashAfter=Hash(after,receipt->blobSizeAfter);
        FirstDifference(before,receipt->blobSizeBefore,after,receipt->blobSizeAfter,receipt);
        receipt->result=(receipt->firstDifference==0xFFFFFFFFu)?ResultOk:ResultBlobMismatch;
        Release(allocator,after);Release(allocator,before);
        return receipt->result==ResultOk?1:0;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;
        return 0;
    }
}

extern "C" __declspec(dllexport) int __cdecl oc2_animator_controller_restore(
    uintptr_t unityBase,uintptr_t animator,const void* blob,uint32_t size,AnimatorControllerReceipt* receipt) {
    if (!receipt) return 0;
    InitializeReceipt(receipt,unityBase,animator);
    if (!unityBase || !animator || !blob || size<0x20 || size>kMaximumBlobSize || !Readable(blob,size)) {
        receipt->result=ResultBadArgument;return 0;
    }
    void* controller=0;void* memory=0;void* allocator=0;
    if (!Resolve(unityBase,animator,receipt,controller,memory,allocator)) return 0;
    __try {
        receipt->blobSizeBefore=size;receipt->hashBefore=Hash(blob,size);
        uint32_t capacity=*reinterpret_cast<uint32_t*>(reinterpret_cast<uintptr_t>(controller)+0xBC);
        if(capacity<size||(reinterpret_cast<uintptr_t>(memory)&15u)!=0) {
            receipt->result=ResultCapacity;return 0;
        }
        BumpAllocator bump={reinterpret_cast<void*>(unityBase+kBumpAllocatorVtableRva),memory,memory,capacity};
        CopyControllerMemory copy=reinterpret_cast<CopyControllerMemory>(unityBase+kCopyControllerMemoryRva);
        uint32_t restoredSize=0;
        void* restored=copy(blob,&bump,&restoredSize);
        void* memoryAfter=*reinterpret_cast<void**>(reinterpret_cast<uintptr_t>(controller)+0xB4);
        receipt->memoryAfter=reinterpret_cast<uintptr_t>(memoryAfter);
        if(restored!=memory||memoryAfter!=memory||restoredSize!=size||!Readable(memoryAfter,0x20)) {
            receipt->result=ResultControllerChanged;return 0;
        }
        void* after=copy(memoryAfter,allocator,&receipt->blobSizeAfter);
        if(!after||receipt->blobSizeAfter<0x20||receipt->blobSizeAfter>kMaximumBlobSize) {
            receipt->result=ResultInvalidBlob;Release(allocator,after);return 0;
        }
        receipt->hashAfter=Hash(after,receipt->blobSizeAfter);
        FirstDifference(blob,size,after,receipt->blobSizeAfter,receipt);
        receipt->result=(receipt->firstDifference==0xFFFFFFFFu)?ResultOk:ResultBlobMismatch;
        Release(allocator,after);return receipt->result==ResultOk?1:0;
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        receipt->lastError=GetExceptionCode();receipt->result=ResultFault;return 0;
    }
}
