#include <windows.h>
#include <stdint.h>
#include <string.h>

#if !defined(_M_IX86)
#error This observer requires the x86 compiler and ABI.
#endif

namespace {

static void CopyBytes(void* destination,const void* source,uint32_t count) {
    uint8_t* out=static_cast<uint8_t*>(destination);
    const uint8_t* in=static_cast<const uint8_t*>(source);
    for(uint32_t i=0;i<count;++i)out[i]=in[i];
}

static bool EqualBytes(const void* left,const void* right,uint32_t count) {
    const uint8_t* a=static_cast<const uint8_t*>(left);
    const uint8_t* b=static_cast<const uint8_t*>(right);
    for(uint32_t i=0;i<count;++i)if(a[i]!=b[i])return false;
    return true;
}

enum EventKind : uint32_t {
    EventMarker = 1,
    EventRigidbodyAwakeFromLoad = 10,
    EventRigidbodyCreate = 11,
    EventRigidbodyMovePosition = 12,
    EventRigidbodySetIsKinematic = 13,
    EventSceneAddRigidBody = 20,
    EventSceneRemoveRigidBody = 21,
    EventScbBodySetBody2World = 30,
    EventNpRigidDynamicSetGlobalPose = 31,
    EventNpRigidDynamicSetKinematicTarget = 32,
    EventScBodyCoreSetBody2World = 33,
    EventNpRigidBodySetCMassLocalPoseInternal = 34,
    EventNpRigidBodySetMassSpaceInertiaTensor = 35,
    EventRigidbodyUpdateMassDistribution = 36,
    EventRigidbodyUpdateMassDistributionPost = 37,
    EventRigidbodyMassDistributionShape = 38,
    EventBoxColliderPoseChanged = 39,
    EventPhysicsManagerSimulate = 40,
    EventPhysicsManagerSyncTransforms = 41,
    EventRigidbodyApplyConstraintsPostGetPose = 42,
    EventRigidbodyGetPositionPostGetPose = 43,
    EventPxDiagonalizeEntry = 44,
    EventPhysicsManagerSyncTransformsPost = 45,
    EventTransformQueueChanges = 46,
    EventTransformDispatchQueued = 47,
    EventPxcDiscreteNarrowPhasePcm = 50,
    EventNPhaseCoreOverlapCreated = 51,
    EventPxsContactManagerCreated = 52,
    EventAnimatorUpdateAvatars = 53,
    EventAnimatorWriteProperties = 54,
    EventDirectorPrepareStage = 55,
    EventDirectorProcessStage = 56,
    EventAnimatorControllerPrepareFrame = 57,
    EventAnimatorControllerClearFirstEvaluationFlag = 58,
    EventAnimatorControllerUpdateGraph = 59,
    EventAnimatorEvaluateStateMachineEntry = 60,
    EventAnimatorEvaluateStateMachineEarlyExit = 61,
    EventAnimatorEvaluateStateMachineExit = 62,
    EventAnimatorStateMixerSnapshot = 63,
    EventAnimatorOuterMixerSnapshot = 64,
    EventAnimatorStateMachineMemoryRaw = 65,
    EventAnimatorStateMachineOutputRaw = 66,
    EventAnimatorConditionFloat = 67,
    EventAnimatorEndTransitionEntry = 68,
    EventAnimatorEndTransitionExit = 69,
    EventAnimatorStartInterruptedTransitionEntry = 70,
    EventAnimatorStartInterruptedTransitionExit = 71,
    EventNPhaseCoreUpdateDirtyInteractions = 72,
    EventNPhaseCoreDirtyInteraction = 73,
    EventShapeInstancePairCreateManager = 74
};

enum HookMask : uint32_t {
    MaskUnityLifecycle = 1u << 0,
    MaskSceneLifecycle = 1u << 1,
    MaskPhysxPose = 1u << 2,
    MaskPhysicsPhases = 1u << 3,
    MaskNarrowPhase = 1u << 4,
    MaskMassFrame = 1u << 5,
    MaskTransformDispatch = 1u << 6,
    MaskAnimatorScheduling = 1u << 7,
    MaskAnimatorStateMachine = 1u << 8,
    MaskAnimatorTransitionLifecycle = 1u << 9,
    MaskAll = MaskUnityLifecycle | MaskSceneLifecycle | MaskPhysxPose | MaskPhysicsPhases |
        MaskNarrowPhase | MaskMassFrame | MaskTransformDispatch | MaskAnimatorScheduling |
        MaskAnimatorStateMachine | MaskAnimatorTransitionLifecycle
};

#pragma pack(push, 8)
struct TraceEvent {
    volatile LONG sequence;
    uint32_t kind;
    uint32_t threadId;
    int64_t qpc;
    uintptr_t self;
    uintptr_t returnAddress;
    uintptr_t stack[8];
    uintptr_t payload[8];
    uintptr_t frames[8];
    uintptr_t extra[5];
};

struct TraceStatus {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t eventSize;
    uint32_t installedMask;
    uint32_t installedHookCount;
    uint32_t capacity;
    int32_t latestSequence;
    int32_t droppedEstimate;
    uint32_t lastError;
};
#pragma pack(pop)

struct HookSpec {
    uint32_t kind;
    uint32_t mask;
    uint32_t rva;
    uint32_t stolen;
    uint8_t expected[12];
    void* hook;
    void** trampolineSlot;
    uint8_t original[12];
    void* trampoline;
    bool installed;
};

static const uint32_t kApiVersion = 1;
static const uint32_t kCapacity = 32768;
static TraceEvent g_events[kCapacity] = {};
static volatile LONG g_latestSequence = 0;
static volatile LONG g_droppedEstimate = 0;
static uint32_t g_installedMask = 0;
static uint32_t g_installedHookCount = 0;
static uint32_t g_lastError = 0;
static uintptr_t g_unityBase = 0;
static volatile LONG g_animatorEvaluateCapture = 0;

struct NativeVec3 {
    float x, y, z;
};

struct NativeTransform {
    float rotation[4];
    float position[3];
};

static const uint32_t kRigidActorGetShapesRva = 0xA10740;
static const uint32_t kShapeGetGeometryTypeRva = 0x842360;
static const uint32_t kShapeGetBoxGeometryRva = 0xA0D0B0;
static const uint32_t kShapeGetCapsuleGeometryRva = 0xA0D0F0;
static const uint32_t kShapeGetFlagsRva = 0xA0D1D0;
static const uint32_t kShapeGetLocalPoseRva = 0xA0D2A0;
static const uint8_t kRigidActorGetShapesBytes[] = {0x55,0x8B,0xEC,0x83,0xC1,0x14,0x5D,0xE9};
static const uint8_t kShapeGetGeometryTypeBytes[] = {0x8B,0x41,0x74,0xC3};
static const uint8_t kShapeGetBoxGeometryBytes[] = {0x55,0x8B,0xEC,0x83,0x79,0x74,0x03};
static const uint8_t kShapeGetCapsuleGeometryBytes[] = {0x55,0x8B,0xEC,0x83,0x79,0x74,0x02};
static const uint8_t kShapeGetFlagsBytes[] = {0x55,0x8B,0xEC,0xF6,0x41,0x24,0x40};
static const uint8_t kShapeGetLocalPoseBytes[] = {0x55,0x8B,0xEC,0xF6,0x41,0x24,0x04,0x56};

typedef uint32_t (__thiscall *RigidActorGetShapes)(void* self, void** shapes,
    uint32_t capacity, uint32_t startIndex);
typedef uint32_t (__thiscall *ShapeGetGeometryType)(void* self);
typedef uint8_t (__thiscall *ShapeGetGeometry)(void* self, void* geometry);
typedef void (__thiscall *ShapeGetFlags)(void* self, uint8_t* flags);
typedef void (__thiscall *ShapeGetLocalPose)(void* self, NativeTransform* pose);

static bool ValidateMassShapeReaders() {
    struct ReaderBytes { uint32_t rva; const uint8_t* bytes; uint32_t count; };
    const ReaderBytes readers[] = {
        {kRigidActorGetShapesRva,kRigidActorGetShapesBytes,sizeof(kRigidActorGetShapesBytes)},
        {kShapeGetGeometryTypeRva,kShapeGetGeometryTypeBytes,sizeof(kShapeGetGeometryTypeBytes)},
        {kShapeGetBoxGeometryRva,kShapeGetBoxGeometryBytes,sizeof(kShapeGetBoxGeometryBytes)},
        {kShapeGetCapsuleGeometryRva,kShapeGetCapsuleGeometryBytes,sizeof(kShapeGetCapsuleGeometryBytes)},
        {kShapeGetFlagsRva,kShapeGetFlagsBytes,sizeof(kShapeGetFlagsBytes)},
        {kShapeGetLocalPoseRva,kShapeGetLocalPoseBytes,sizeof(kShapeGetLocalPoseBytes)}
    };
    for (uint32_t i=0;i<sizeof(readers)/sizeof(readers[0]);++i)
        if (!EqualBytes(reinterpret_cast<const void*>(g_unityBase+readers[i].rva),
            readers[i].bytes,readers[i].count)) return false;
    return true;
}

static void RecordMassDistributionShapes(uintptr_t rigidbody, uintptr_t returnAddress,
    LONG parentSequence) {
    const uintptr_t actor=*reinterpret_cast<const uintptr_t*>(rigidbody+0x34);
    if (!actor) return;
    uintptr_t shapes[16]={};
    RigidActorGetShapes getShapes=reinterpret_cast<RigidActorGetShapes>(
        g_unityBase+kRigidActorGetShapesRva);
    uint32_t count=getShapes(reinterpret_cast<void*>(actor),
        reinterpret_cast<void**>(shapes),16,0);
    if (count>16) count=16;
    ShapeGetGeometryType getType=reinterpret_cast<ShapeGetGeometryType>(
        g_unityBase+kShapeGetGeometryTypeRva);
    ShapeGetGeometry getBox=reinterpret_cast<ShapeGetGeometry>(
        g_unityBase+kShapeGetBoxGeometryRva);
    ShapeGetGeometry getCapsule=reinterpret_cast<ShapeGetGeometry>(
        g_unityBase+kShapeGetCapsuleGeometryRva);
    ShapeGetFlags getFlags=reinterpret_cast<ShapeGetFlags>(g_unityBase+kShapeGetFlagsRva);
    ShapeGetLocalPose getPose=reinterpret_cast<ShapeGetLocalPose>(g_unityBase+kShapeGetLocalPoseRva);
    for (uint32_t i=0;i<count;++i) {
        LONG sequence=InterlockedIncrement(&g_latestSequence);
        TraceEvent* event=&g_events[static_cast<uint32_t>(sequence-1)%kCapacity];
        event->sequence=0;
        event->kind=EventRigidbodyMassDistributionShape;
        event->threadId=GetCurrentThreadId();
        LARGE_INTEGER qpc;QueryPerformanceCounter(&qpc);event->qpc=qpc.QuadPart;
        event->self=rigidbody;event->returnAddress=returnAddress;
        for(uint32_t j=0;j<8;++j)event->stack[j]=0;
        for(uint32_t j=0;j<8;++j)event->payload[j]=0;
        for(uint32_t j=0;j<8;++j)event->frames[j]=0;
        for(uint32_t j=0;j<5;++j)event->extra[j]=0;
        const uintptr_t shape=shapes[i];
        uint32_t type=getType(reinterpret_cast<void*>(shape));
        uint8_t flags=0;getFlags(reinterpret_cast<void*>(shape),&flags);
        NativeTransform pose={};getPose(reinterpret_cast<void*>(shape),&pose);
        event->stack[0]=actor;event->stack[1]=static_cast<uintptr_t>(parentSequence);
        event->stack[2]=shape;event->stack[3]=i;event->stack[4]=count;
        event->stack[5]=type;event->stack[6]=flags;
        CopyBytes(event->payload,&pose,sizeof(pose));
        uint8_t geometryOk=0;
        if(type==2)geometryOk=getCapsule(reinterpret_cast<void*>(shape),event->extra);
        else if(type==3)geometryOk=getBox(reinterpret_cast<void*>(shape),event->extra);
        event->stack[7]=geometryOk;
        MemoryBarrier();event->sequence=sequence;
        if(sequence>static_cast<LONG>(kCapacity))g_droppedEstimate=sequence-static_cast<LONG>(kCapacity);
    }
}

static void* g_trampolineAwakeFromLoad = 0;
static void* g_trampolineCreate = 0;
static void* g_trampolineMovePosition = 0;
static void* g_trampolineSetIsKinematic = 0;
static void* g_trampolineAddRigidBody = 0;
static void* g_trampolineRemoveRigidBody = 0;
static void* g_trampolineScbSetBody2World = 0;
static void* g_trampolineNpSetGlobalPose = 0;
static void* g_trampolineNpSetKinematicTarget = 0;
static void* g_trampolineScSetBody2World = 0;
static void* g_trampolineNpSetCMassLocalPoseInternal = 0;
static void* g_trampolineNpSetMassSpaceInertiaTensor = 0;
static void* g_trampolineRigidbodyUpdateMassDistribution = 0;
static void* g_trampolineBoxColliderPoseChanged = 0;
static void* g_trampolinePhysicsManagerSimulate = 0;
static void* g_trampolinePhysicsManagerSyncTransforms = 0;
static void* g_trampolineTransformQueueChanges = 0;
static void* g_trampolineApplyConstraintsPostGetPose = 0;
static void* g_trampolineGetPositionPostGetPose = 0;
static void* g_trampolinePxDiagonalize = 0;
static void* g_trampolinePxcDiscreteNarrowPhasePcm = 0;
static void* g_trampolineNPhaseCoreOverlapCreated = 0;
static void* g_trampolinePxsContextCreateContactManager = 0;
static void* g_trampolineNPhaseCoreUpdateDirtyInteractions = 0;
static void* g_trampolineShapeInstancePairCreateManager = 0;
static void* g_trampolineAnimatorUpdateAvatars = 0;
static void* g_trampolineAnimatorWriteProperties = 0;
static void* g_trampolineDirectorPrepareStage = 0;
static void* g_trampolineDirectorProcessStage = 0;
static void* g_trampolineAnimatorControllerPrepareFrame = 0;
static void* g_trampolineAnimatorControllerClearFirstEvaluationFlag = 0;
static void* g_trampolineAnimatorControllerUpdateGraph = 0;
static void* g_trampolineAnimatorEvaluateStateMachine = 0;
static void* g_trampolineAnimatorEvaluateStateMachineEarlyExit = 0;
static void* g_trampolineAnimatorEvaluateStateMachineExit = 0;
static void* g_trampolineAnimatorConditionFloat = 0;
static void* g_trampolineAnimatorEndTransition = 0;
static void* g_trampolineAnimatorStartInterruptedTransition = 0;
static __declspec(thread) bool g_recordSyncTransformsPost = false;

struct AnimatorEvaluateThreadState {
    uintptr_t controller;
    LONG updateGraphSequence;
    uint32_t ordinal;
    uint32_t entryOrdinal;
    LONG entrySequence;
    uintptr_t arguments[5];
};
static __declspec(thread) AnimatorEvaluateThreadState g_animatorEvaluate = {};

struct AnimatorTransitionCall {
    uint32_t entryKind;
    uintptr_t self;
    LONG entrySequence;
};
static __declspec(thread) AnimatorTransitionCall g_animatorTransitionCalls[16] = {};
static __declspec(thread) uint32_t g_animatorTransitionCallDepth = 0;
static __declspec(thread) uint32_t g_animatorTransitionCallOverflow = 0;

static const uint32_t kTransformChangeDispatchGlobalRva = 0xFF0D28;
static const uint32_t kColliderChangeHandleScaleRva = 0xFA2E34;
static const uint32_t kColliderChangeHandleTransformRva = 0xFA2E38;
static const uint32_t kBodyChangeHandleRva = 0xFA2E3C;
static const uint32_t kBodyPhysicsAnimationHandleRva = 0xFA2E40;

static void AddHandleMask(uint32_t handle, uint32_t& low, uint32_t& high) {
    if (handle < 32u) low |= 1u << handle;
    else if (handle < 64u) high |= 1u << (handle - 32u);
}

static void PhysicsTransformMasks(uint32_t& low, uint32_t& high) {
    low = high = 0;
    AddHandleMask(*reinterpret_cast<const uint32_t*>(
        g_unityBase + kColliderChangeHandleScaleRva), low, high);
    AddHandleMask(*reinterpret_cast<const uint32_t*>(
        g_unityBase + kColliderChangeHandleTransformRva), low, high);
    AddHandleMask(*reinterpret_cast<const uint32_t*>(
        g_unityBase + kBodyChangeHandleRva), low, high);
    AddHandleMask(*reinterpret_cast<const uint32_t*>(
        g_unityBase + kBodyPhysicsAnimationHandleRva), low, high);
}

static bool TransformDispatchHasPhysicsPending() {
    const uintptr_t dispatch = *reinterpret_cast<const uintptr_t*>(
        g_unityBase + kTransformChangeDispatchGlobalRva);
    if (!dispatch || *reinterpret_cast<const uint32_t*>(dispatch + 0x10) == 0) return false;
    uint32_t low, high; PhysicsTransformMasks(low, high);
    return ((*reinterpret_cast<const uint32_t*>(dispatch + 0x00) & low) |
        (*reinterpret_cast<const uint32_t*>(dispatch + 0x04) & high)) != 0;
}

static bool TransformHasPhysicsInterest(uintptr_t transform) {
    if (!transform) return false;
    const uintptr_t hierarchy = *reinterpret_cast<const uintptr_t*>(transform + 0x20);
    if (!hierarchy) return false;
    uint32_t low, high; PhysicsTransformMasks(low, high);
    return ((*reinterpret_cast<const uint32_t*>(hierarchy + 0x20) & low) |
        (*reinterpret_cast<const uint32_t*>(hierarchy + 0x24) & high)) != 0;
}

static void CaptureTransformDispatchSummary(TraceEvent* event) {
    const uintptr_t dispatch = *reinterpret_cast<const uintptr_t*>(
        g_unityBase + kTransformChangeDispatchGlobalRva);
    event->payload[0] = dispatch;
    if (!dispatch) return;
    event->payload[1] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x00);
    event->payload[2] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x04);
    event->payload[3] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x08);
    event->payload[4] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x0C);
    event->payload[5] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x10);
    event->payload[6] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x778);
    event->payload[7] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x77C);
}

// Snapshot the ordered global TransformHierarchy queue at SyncTransforms entry.
// QueueChanges stores the hierarchy pointer in dynamic_array<void*,4> at
// TransformChangeDispatch+0x08 and the current queue index/masks at hierarchy
// offsets 0x1C/0x20/0x24. This observer is bounded and read-only.
static void RecordTransformDispatchQueued(LONG parentSequence) {
    const uintptr_t dispatch = *reinterpret_cast<const uintptr_t*>(
        g_unityBase + kTransformChangeDispatchGlobalRva);
    if (!dispatch) return;
    const uintptr_t array = *reinterpret_cast<const uintptr_t*>(dispatch + 0x08);
    uint32_t count = *reinterpret_cast<const uint32_t*>(dispatch + 0x10);
    if (!array) return;
    const uint32_t recordedCount = count > 512u ? 512u : count;
    for (uint32_t index = 0; index < recordedCount; ++index) {
        const uintptr_t hierarchy = reinterpret_cast<const uintptr_t*>(array)[index];
        LONG sequence = InterlockedIncrement(&g_latestSequence);
        TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
        event->sequence = 0;
        event->kind = EventTransformDispatchQueued;
        event->threadId = GetCurrentThreadId();
        LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
        event->self = hierarchy;
        event->returnAddress = 0;
        for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
        for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
        for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
        for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
        event->stack[0] = static_cast<uintptr_t>(parentSequence);
        event->stack[1] = index;
        event->stack[2] = count;
        event->stack[3] = recordedCount;
        if (hierarchy) {
            event->payload[0] = *reinterpret_cast<const uintptr_t*>(hierarchy + 0x1C);
            event->payload[1] = *reinterpret_cast<const uintptr_t*>(hierarchy + 0x20);
            event->payload[2] = *reinterpret_cast<const uintptr_t*>(hierarchy + 0x24);
        }
        MemoryBarrier(); event->sequence = sequence;
        if (sequence > static_cast<LONG>(kCapacity))
            g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
    }
}

static uint32_t HashBytes32(const void* data, uint32_t count) {
    const uint8_t* bytes = static_cast<const uint8_t*>(data);
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= bytes[i];
        hash *= 16777619u;
    }
    return hash;
}

static bool ReadableRange(uintptr_t address, uint32_t count) {
    if (!address || !count || address + count < address) return false;
    uintptr_t current = address;
    const uintptr_t end = address + count;
    while (current < end) {
        MEMORY_BASIC_INFORMATION info = {};
        if (!VirtualQuery(reinterpret_cast<const void*>(current), &info, sizeof(info))) return false;
        const DWORD blocked = PAGE_NOACCESS | PAGE_GUARD;
        if (info.State != MEM_COMMIT || (info.Protect & blocked) != 0) return false;
        const uintptr_t regionEnd = reinterpret_cast<uintptr_t>(info.BaseAddress) + info.RegionSize;
        if (regionEnd <= current) return false;
        current = regionEnd;
    }
    return true;
}

static uintptr_t ReadPointerOrZero(uintptr_t address) {
    return ReadableRange(address, sizeof(uintptr_t))
        ? *reinterpret_cast<const uintptr_t*>(address) : 0;
}

static void FinishEvent(TraceEvent* event, LONG sequence) {
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity))
        g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

static void InitializeAnimatorEvaluateEvent(TraceEvent* event,
    uint32_t kind, uintptr_t returnAddress, const uintptr_t* arguments, LONG pairedEntry) {
    event->sequence = 0;
    event->kind = kind;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
    event->self = g_animatorEvaluate.controller;
    event->returnAddress = returnAddress;
    for (uint32_t i = 0; i < 5; ++i) event->stack[i] = arguments[i];
    event->stack[5] = static_cast<uintptr_t>(g_animatorEvaluate.updateGraphSequence);
    event->stack[6] = g_animatorEvaluate.entryOrdinal;
    event->stack[7] = static_cast<uintptr_t>(pairedEntry);
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;

    // Five arguments are StateMachineConstant, StateMachineInput, Output,
    // Memory and Workspace. Preserve the complete 0x24-byte input plus hashes
    // of every bounded structure actually supplied to the evaluator.
    if (ReadableRange(arguments[1], 0x24)) {
        const uintptr_t* input = reinterpret_cast<const uintptr_t*>(arguments[1]);
        for (uint32_t i = 0; i < 8; ++i) event->payload[i] = input[i];
        event->extra[0] = input[8];
    }
    event->frames[0] = ReadableRange(arguments[0], 0x20) ? HashBytes32(reinterpret_cast<const void*>(arguments[0]), 0x20) : 0;
    event->frames[1] = ReadableRange(arguments[1], 0x24) ? HashBytes32(reinterpret_cast<const void*>(arguments[1]), 0x24) : 0;
    event->frames[2] = ReadableRange(arguments[2], 0x14) ? HashBytes32(reinterpret_cast<const void*>(arguments[2]), 0x14) : 0;
    event->frames[3] = ReadableRange(arguments[3], 0x70) ? HashBytes32(reinterpret_cast<const void*>(arguments[3]), 0x70) : 0;
    event->frames[4] = ReadableRange(arguments[4], 0x08) ? HashBytes32(reinterpret_cast<const void*>(arguments[4]), 0x08) : 0;
    if (ReadableRange(arguments[2], 0x10)) event->frames[5] = *reinterpret_cast<const uintptr_t*>(arguments[2] + 0x0C);
    if (ReadableRange(arguments[3], 0x70)) {
        event->frames[6] = *reinterpret_cast<const uintptr_t*>(arguments[3] + 0x08);
        event->frames[7] = *reinterpret_cast<const uintptr_t*>(arguments[3] + 0x0C);
        event->extra[3] = *reinterpret_cast<const uintptr_t*>(arguments[3] + 0x10);
        event->extra[4] = *reinterpret_cast<const uint8_t*>(arguments[3] + 0x68) |
            (static_cast<uintptr_t>(*reinterpret_cast<const uint8_t*>(arguments[3] + 0x6E)) << 8);
    }
}

static void InitializeAnimatorRawEvent(TraceEvent* event, uint32_t kind,
    LONG parentSequence, uintptr_t source, uint32_t requiredBytes) {
    event->sequence = 0;
    event->kind = kind;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
    event->self = g_animatorEvaluate.controller;
    event->returnAddress = ReadableRange(source, requiredBytes) ? source : 0;
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    event->stack[0] = static_cast<uintptr_t>(parentSequence);
}

static void RecordAnimatorStateMachineMemoryRaw(LONG parentSequence, uintptr_t memory) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    InitializeAnimatorRawEvent(event, EventAnimatorStateMachineMemoryRaw,
        parentSequence, memory, 0x70);
    if (event->returnAddress) {
        const uintptr_t* words = reinterpret_cast<const uintptr_t*>(memory);
        for (uint32_t i = 0; i < 7; ++i) event->stack[1 + i] = words[i];
        for (uint32_t i = 0; i < 8; ++i) event->payload[i] = words[7 + i];
        for (uint32_t i = 0; i < 8; ++i) event->frames[i] = words[15 + i];
        for (uint32_t i = 0; i < 5; ++i) event->extra[i] = words[23 + i];
    }
    FinishEvent(event, sequence);
}

static void RecordAnimatorStateMachineOutputRaw(LONG parentSequence, uintptr_t output) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    InitializeAnimatorRawEvent(event, EventAnimatorStateMachineOutputRaw,
        parentSequence, output, 0x14);
    if (event->returnAddress) {
        const uintptr_t* words = reinterpret_cast<const uintptr_t*>(output);
        for (uint32_t i = 0; i < 5; ++i) event->stack[1 + i] = words[i];
    }
    FinishEvent(event, sequence);
}

static void RecordAnimatorStateMachineRaw(LONG parentSequence, const uintptr_t* arguments) {
    RecordAnimatorStateMachineMemoryRaw(parentSequence, arguments[3]);
    RecordAnimatorStateMachineOutputRaw(parentSequence, arguments[2]);
}

// Observe the exact float already loaded by EvaluateCondition immediately
// before its mode-3 greater-than comparison.  The caller obtains the runtime
// ValueArray through StateMachineInput+0x0C; the ValueArray stores typed arrays
// as self-relative pointers.  Capturing the resolved element closes the gap
// left by hashing only the StateMachineInput prefix.
static void __cdecl RecordAnimatorConditionFloat(const uintptr_t* saved,
    uint32_t actualBits) {
    if (InterlockedCompareExchange(&g_animatorEvaluateCapture, 0, 0) == 0 ||
        !g_animatorEvaluate.controller || !g_animatorEvaluate.entrySequence) return;

    // At RVA 0x663524: ESI=ConditionConstant, EBX=ValueConstant descriptor,
    // EDX=runtime ValueArray, ECX=typed float index and XMM1=loaded value.
    const uintptr_t condition = saved[1];
    const uintptr_t descriptor = saved[4];
    const uintptr_t runtime = saved[5];
    const uint32_t index = static_cast<uint32_t>(saved[6]);
    if (!ReadableRange(condition, 12) || !ReadableRange(descriptor, 12) ||
        !ReadableRange(runtime, 0x20)) return;

    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventAnimatorConditionFloat;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
    event->self = g_animatorEvaluate.controller;
    event->returnAddress = g_unityBase + 0x663524;
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;

    event->stack[0] = static_cast<uintptr_t>(g_animatorEvaluate.entrySequence);
    event->stack[1] = static_cast<uintptr_t>(g_animatorEvaluate.updateGraphSequence);
    event->stack[2] = g_animatorEvaluate.entryOrdinal;
    event->stack[3] = condition;
    event->stack[4] = descriptor;
    event->stack[5] = runtime;
    event->stack[6] = index;

    event->payload[0] = *reinterpret_cast<const uint32_t*>(condition + 0x00);
    event->payload[1] = *reinterpret_cast<const uint32_t*>(condition + 0x04);
    event->payload[2] = *reinterpret_cast<const uint32_t*>(condition + 0x08);
    event->payload[3] = *reinterpret_cast<const uint32_t*>(descriptor + 0x00);
    event->payload[4] = *reinterpret_cast<const uint32_t*>(descriptor + 0x04);
    event->payload[5] = *reinterpret_cast<const uint32_t*>(descriptor + 0x08);
    event->payload[6] = *reinterpret_cast<const uint32_t*>(runtime + 0x18);
    const int32_t relative = *reinterpret_cast<const int32_t*>(runtime + 0x1C);
    event->payload[7] = static_cast<uint32_t>(relative);

    const int64_t target64 = static_cast<int64_t>(runtime + 0x1C) + relative +
        static_cast<int64_t>(index) * sizeof(uint32_t);
    if (target64 >= 0 && target64 <= 0xFFFFFFFFll) {
        const uintptr_t target = static_cast<uintptr_t>(target64);
        if (ReadableRange(target, sizeof(uint32_t))) {
            event->stack[7] = target;
            event->frames[1] = *reinterpret_cast<const uint32_t*>(target);
        }
    }
    event->frames[0] = actualBits;

    const uintptr_t input = g_animatorEvaluate.arguments[1];
    const uintptr_t memory = g_animatorEvaluate.arguments[3];
    event->frames[2] = input;
    if (ReadableRange(input, 0x10))
        event->frames[3] = *reinterpret_cast<const uintptr_t*>(input + 0x0C);
    if (ReadableRange(g_animatorEvaluate.controller + 0xB0, 8)) {
        const uintptr_t controllerInput = *reinterpret_cast<const uintptr_t*>(
            g_animatorEvaluate.controller + 0xB0);
        event->frames[4] = controllerInput;
        if (ReadableRange(controllerInput, sizeof(uint32_t)))
            event->frames[5] = *reinterpret_cast<const uint32_t*>(controllerInput);
    }
    if (ReadableRange(memory, 0x38)) {
        event->extra[0] = *reinterpret_cast<const uint32_t*>(memory + 0x08);
        event->extra[1] = *reinterpret_cast<const uint32_t*>(memory + 0x0C);
        event->extra[2] = *reinterpret_cast<const uint32_t*>(memory + 0x34);
    }
    if (ReadableRange(input, 0x0C))
        event->extra[3] = *reinterpret_cast<const uint32_t*>(input + 0x08);
    FinishEvent(event, sequence);
}

// UpdateGraph resolves StateMachineMemory through two self-relative offsets.
// Reuse the parent event's call-stack words for a compact layer-0 summary;
// emitting one additional 152-byte event for every controller overflowed the
// framework RPC frame during a ten-frame probe.
static void CaptureAnimatorUpdateGraphStateMachineSummary(TraceEvent* event,
    uintptr_t controller) {
    if (!event || !controller ||
        !ReadableRange(controller + 0xB4, sizeof(uintptr_t))) return;
    const uintptr_t root = *reinterpret_cast<const uintptr_t*>(controller + 0xB4);
    if (!root || !ReadableRange(root + 0x04, sizeof(int32_t))) return;
    const uintptr_t slotsBase = root + 0x04;
    const int32_t slotsOffset = *reinterpret_cast<const int32_t*>(slotsBase);
    const uintptr_t layer0Slot = slotsBase + slotsOffset;
    if (!ReadableRange(layer0Slot, sizeof(int32_t))) return;
    const int32_t stateMachineOffset = *reinterpret_cast<const int32_t*>(layer0Slot);
    if (!stateMachineOffset) return;
    const uintptr_t stateMachine = layer0Slot + stateMachineOffset;
    if (!ReadableRange(stateMachine, 0x6C)) return;
    event->frames[0] = stateMachine;
    event->frames[1] = *reinterpret_cast<const uintptr_t*>(stateMachine + 0x08);
    event->frames[2] = *reinterpret_cast<const uintptr_t*>(stateMachine + 0x0C);
    event->frames[3] = *reinterpret_cast<const uintptr_t*>(stateMachine + 0x24);
    event->frames[4] = *reinterpret_cast<const uintptr_t*>(stateMachine + 0x34);
    event->frames[5] = *reinterpret_cast<const uintptr_t*>(stateMachine + 0x38);
    event->frames[6] = *reinterpret_cast<const uintptr_t*>(stateMachine + 0x48);
    event->frames[7] = *reinterpret_cast<const uintptr_t*>(stateMachine + 0x68);
}

static void RecordAnimatorMixerSnapshot(LONG parentSequence, uint32_t liveIndex,
    uint32_t branch, uintptr_t live, uintptr_t mixer) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventAnimatorStateMixerSnapshot;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
    event->self = g_animatorEvaluate.controller;
    event->returnAddress = ReadableRange(mixer + 0xA5, 1)
        ? *reinterpret_cast<const uint8_t*>(mixer + 0xA5) : 0xFFFFFFFFu;
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    event->stack[0] = static_cast<uintptr_t>(parentSequence);
    event->stack[1] = static_cast<uintptr_t>(g_animatorEvaluate.updateGraphSequence);
    event->stack[2] = g_animatorEvaluate.entryOrdinal;
    event->stack[3] = liveIndex;
    event->stack[4] = branch;
    event->stack[5] = live;
    event->stack[6] = mixer;
    event->payload[0] = ReadPointerOrZero(live);
    event->payload[1] = ReadPointerOrZero(mixer);

    const uintptr_t mixerInternal = ReadPointerOrZero(mixer + 0x10);
    event->stack[7] = mixerInternal;
    const uint32_t inputCount = static_cast<uint32_t>(ReadPointerOrZero(mixerInternal + 0x18));
    const uintptr_t entries = ReadPointerOrZero(mixerInternal + 0x10);
    event->payload[2] = inputCount;
    event->payload[3] = entries;
    if (inputCount <= 64u && (!inputCount || ReadableRange(entries, inputCount * 12u))) {
        event->payload[5] = inputCount ? HashBytes32(reinterpret_cast<const void*>(entries), inputCount * 12u) : 0;
        uint32_t weightHash = 2166136261u;
        uint32_t activeCount = 0;
        const uint32_t retained = inputCount < 13u ? inputCount : 13u;
        event->payload[7] = retained;
        for (uint32_t i = 0; i < inputCount; ++i) {
            const uint32_t bits = *reinterpret_cast<const uint32_t*>(entries + i * 12u);
            for (uint32_t byte = 0; byte < 4; ++byte) {
                weightHash ^= static_cast<uint8_t>(bits >> (byte * 8));
                weightHash *= 16777619u;
            }
            if ((bits & 0x7FFFFFFFu) != 0) ++activeCount;
            if (i < 8u) event->frames[i] = bits;
            else if (i < 13u) event->extra[i - 8u] = bits;
        }
        event->payload[4] = activeCount;
        event->payload[6] = weightHash;
    } else {
        event->payload[4] = 0xFFFFFFFFu;
        event->payload[7] = 0xFFFFFFFFu;
    }
    FinishEvent(event, sequence);
}

static void RecordAnimatorOuterMixerSnapshot(LONG parentSequence, uint32_t liveIndex,
    uintptr_t live, uintptr_t playable, uintptr_t slots) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventAnimatorOuterMixerSnapshot;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
    event->self = g_animatorEvaluate.controller;
    event->returnAddress = 0;
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    const uint32_t slotCount = static_cast<uint32_t>(ReadPointerOrZero(playable + 0x18));
    event->stack[0] = static_cast<uintptr_t>(parentSequence);
    event->stack[1] = static_cast<uintptr_t>(g_animatorEvaluate.updateGraphSequence);
    event->stack[2] = g_animatorEvaluate.entryOrdinal;
    event->stack[3] = liveIndex;
    event->stack[4] = live;
    event->stack[5] = playable;
    event->stack[6] = slots;
    event->stack[7] = slotCount;
    event->payload[0] = ReadPointerOrZero(live);
    if (slotCount <= 64u && (!slotCount || ReadableRange(slots, slotCount * 12u))) {
        event->payload[1] = slotCount ? HashBytes32(reinterpret_cast<const void*>(slots), slotCount * 12u) : 0;
        const uint32_t wordCount = slotCount * 3u;
        const uint32_t retainedWords = wordCount < 16u ? wordCount : 16u;
        event->extra[2] = retainedWords;
        const uintptr_t* words = reinterpret_cast<const uintptr_t*>(slots);
        for (uint32_t i = 0; i < retainedWords; ++i) {
            if (i < 6u) event->payload[2 + i] = words[i];
            else if (i < 14u) event->frames[i - 6u] = words[i];
            else event->extra[i - 14u] = words[i];
        }
    } else {
        event->payload[1] = 0xFFFFFFFFu;
        event->extra[2] = 0xFFFFFFFFu;
    }
    if (ReadableRange(live + 0xA0, 6)) {
        event->extra[3] = *reinterpret_cast<const uint8_t*>(live + 0xA0) |
            (static_cast<uintptr_t>(*reinterpret_cast<const uint8_t*>(live + 0xA4)) << 8) |
            (static_cast<uintptr_t>(*reinterpret_cast<const uint8_t*>(live + 0xA5)) << 16);
    }
    FinishEvent(event, sequence);
}

static void RecordAnimatorMixerSnapshots(LONG parentSequence, const uintptr_t* arguments) {
    // EvaluateStateMachine's Output+0x0c points at the E8 live-state-machine
    // descriptor. Inline the exact read-only traversal used by
    // GetStateMixerPlayable/GetInputWeight; do not call into graph code.
    const uintptr_t output = arguments[2];
    const uintptr_t descriptor = ReadPointerOrZero(output + 0x0C);
    const uintptr_t liveArray = ReadPointerOrZero(descriptor + 0x00);
    const uint32_t liveCount = static_cast<uint32_t>(ReadPointerOrZero(descriptor + 0x04));
    if (liveCount > 64u || (liveCount && !ReadableRange(liveArray, liveCount * sizeof(uintptr_t)))) return;
    for (uint32_t index = 0; index < liveCount; ++index) {
        const uintptr_t live = ReadPointerOrZero(liveArray + index * sizeof(uintptr_t));
        const uintptr_t playable = ReadPointerOrZero(live + 0x10);
        const uintptr_t slots = ReadPointerOrZero(playable + 0x10);
        const uintptr_t mixerTrue = ReadPointerOrZero(slots + 0x04);
        const uintptr_t mixerFalse = ReadPointerOrZero(slots + 0x10);
        RecordAnimatorOuterMixerSnapshot(parentSequence, index, live, playable, slots);
        RecordAnimatorMixerSnapshot(parentSequence, index, 1u, live, mixerTrue);
        RecordAnimatorMixerSnapshot(parentSequence, index, 0u, live, mixerFalse);
    }
}

static void __cdecl RecordAnimatorEvaluateEntry(const uintptr_t* saved) {
    const uintptr_t expectedReturn = g_unityBase + 0x649F32;
    if (InterlockedCompareExchange(&g_animatorEvaluateCapture, 0, 0) == 0 ||
        saved[9] != expectedReturn || !g_animatorEvaluate.controller) return;
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    uintptr_t arguments[5] = {saved[10], saved[11], saved[12], saved[13], saved[14]};
    g_animatorEvaluate.entryOrdinal = g_animatorEvaluate.ordinal++;
    g_animatorEvaluate.entrySequence = sequence;
    for (uint32_t i = 0; i < 5; ++i) g_animatorEvaluate.arguments[i] = arguments[i];
    InitializeAnimatorEvaluateEvent(event, EventAnimatorEvaluateStateMachineEntry,
        saved[9], arguments, 0);
    FinishEvent(event, sequence);
    RecordAnimatorStateMachineRaw(sequence, arguments);
    RecordAnimatorMixerSnapshots(sequence, arguments);
}

static void __cdecl RecordAnimatorEvaluateExit(uint32_t kind, const uintptr_t* saved) {
    const uintptr_t frame = saved[2];
    if (!frame || !g_animatorEvaluate.entrySequence) return;
    uintptr_t arguments[5] = {};
    for (uint32_t i = 0; i < 5; ++i)
        arguments[i] = *reinterpret_cast<const uintptr_t*>(frame + 0x08 + i * sizeof(uintptr_t));
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    InitializeAnimatorEvaluateEvent(event, kind,
        *reinterpret_cast<const uintptr_t*>(frame + 0x04), arguments,
        g_animatorEvaluate.entrySequence);
    FinishEvent(event, sequence);
    RecordAnimatorStateMachineRaw(sequence, arguments);
    g_animatorEvaluate.entrySequence = 0;
}

static void HashWord32(uint32_t& hash, uintptr_t value) {
    for (uint32_t byte = 0; byte < sizeof(uintptr_t); ++byte) {
        hash ^= static_cast<uint8_t>(value >> (byte * 8));
        hash *= 16777619u;
    }
}

// Hash the bounded, documented mixer/child fields that can distinguish a
// graph-side mutation without changing or invoking the graph. A return value
// of 0xFFFFFFFF means that the pointer or expected vtable was not readable.
static uint32_t HashAnimatorMixerState(uintptr_t mixer) {
    const uintptr_t expectedMixerVtable = g_unityBase + 0xE83744;
    if (!ReadableRange(mixer, 0xA6) || ReadPointerOrZero(mixer) != expectedMixerVtable)
        return 0xFFFFFFFFu;
    const uintptr_t internal = ReadPointerOrZero(mixer + 0x10);
    if (!ReadableRange(internal + 0x10, 12)) return 0xFFFFFFFFu;
    const uintptr_t entries = ReadPointerOrZero(internal + 0x10);
    const uint32_t count = static_cast<uint32_t>(ReadPointerOrZero(internal + 0x18));
    if (count > 64u || (count && !ReadableRange(entries, count * 12u)))
        return 0xFFFFFFFFu;

    uint32_t hash = 2166136261u;
    HashWord32(hash, mixer);
    HashWord32(hash, expectedMixerVtable);
    HashWord32(hash, internal);
    HashWord32(hash, entries);
    HashWord32(hash, count);
    for (uint32_t offset = 0x90; offset <= 0x93; ++offset)
        HashWord32(hash, *reinterpret_cast<const uint8_t*>(mixer + offset));
    HashWord32(hash, *reinterpret_cast<const uint8_t*>(mixer + 0xA0));
    HashWord32(hash, *reinterpret_cast<const uint8_t*>(mixer + 0xA4));
    HashWord32(hash, *reinterpret_cast<const uint8_t*>(mixer + 0xA5));
    if (count) HashWord32(hash, HashBytes32(reinterpret_cast<const void*>(entries), count * 12u));

    const uintptr_t expectedClipVtable = g_unityBase + 0xE83610;
    for (uint32_t index = 0; index < count; ++index) {
        const uintptr_t child = ReadPointerOrZero(entries + index * 12u + 4u);
        HashWord32(hash, child);
        if (!ReadableRange(child, sizeof(uintptr_t))) {
            HashWord32(hash, 0xFFFFFFFFu);
            continue;
        }
        const uintptr_t vtable = ReadPointerOrZero(child);
        HashWord32(hash, vtable);
        if (vtable == expectedClipVtable && ReadableRange(child, 0x10C))
            HashWord32(hash, ReadPointerOrZero(child + 0x108));
        if (ReadableRange(child + 0x90, 6)) {
            for (uint32_t offset = 0x90; offset <= 0x93; ++offset)
                HashWord32(hash, *reinterpret_cast<const uint8_t*>(child + offset));
            HashWord32(hash, *reinterpret_cast<const uint8_t*>(child + 0xA4));
            HashWord32(hash, *reinterpret_cast<const uint8_t*>(child + 0xA5));
        }
    }
    return hash;
}

static void CaptureAnimatorTransitionGraph(TraceEvent* event, uintptr_t outer) {
    const uintptr_t expectedOuterVtable = g_unityBase + 0xE839C8;
    uint32_t status = 0;
    if (!ReadableRange(outer, 0xA6)) {
        event->extra[4] = status;
        return;
    }
    const uintptr_t vtable = ReadPointerOrZero(outer);
    event->extra[3] = vtable;
    if (vtable == expectedOuterVtable) status |= 2u;
    event->stack[7] = *reinterpret_cast<const uint8_t*>(outer + 0xA0) |
        (static_cast<uintptr_t>(*reinterpret_cast<const uint8_t*>(outer + 0xA4)) << 8) |
        (static_cast<uintptr_t>(*reinterpret_cast<const uint8_t*>(outer + 0xA5)) << 16);
    if ((status & 2u) == 0) {
        event->extra[4] = status;
        return;
    }

    const uintptr_t internal = ReadPointerOrZero(outer + 0x10);
    event->payload[0] = internal;
    if (!ReadableRange(internal + 0x10, 12)) {
        event->extra[4] = status;
        return;
    }
    const uintptr_t entries = ReadPointerOrZero(internal + 0x10);
    const uint32_t count = static_cast<uint32_t>(ReadPointerOrZero(internal + 0x18));
    event->payload[1] = entries;
    event->payload[2] = count;
    if (count > 64u || (count && !ReadableRange(entries, count * 12u))) {
        event->extra[4] = status;
        return;
    }
    status |= 1u;
    event->payload[3] = count
        ? HashBytes32(reinterpret_cast<const void*>(entries), count * 12u) : 0;
    const uint32_t retainedWords = count * 3u < 9u ? count * 3u : 9u;
    const uintptr_t* words = reinterpret_cast<const uintptr_t*>(entries);
    for (uint32_t index = 0; index < retainedWords; ++index) {
        if (index < 4u) event->payload[4 + index] = words[index];
        else event->frames[index - 4u] = words[index];
    }
    status |= retainedWords << 8;

    // The outer transition mixer has three state-mixer branches in all
    // observed Unity 2017.4.8f1 graphs. Retain independent entry-array and
    // bounded playable-state hashes for each present branch.
    for (uint32_t slot = 0; slot < count && slot < 3u; ++slot) {
        const uintptr_t branch = ReadPointerOrZero(entries + slot * 12u + 4u);
        uint32_t entryHash = 0xFFFFFFFFu;
        const uintptr_t branchInternal = ReadPointerOrZero(branch + 0x10);
        if (ReadableRange(branchInternal + 0x10, 12)) {
            const uintptr_t branchEntries = ReadPointerOrZero(branchInternal + 0x10);
            const uint32_t branchCount = static_cast<uint32_t>(ReadPointerOrZero(branchInternal + 0x18));
            if (branchCount <= 64u && (!branchCount || ReadableRange(branchEntries, branchCount * 12u))) {
                entryHash = branchCount
                    ? HashBytes32(reinterpret_cast<const void*>(branchEntries), branchCount * 12u) : 0;
                status |= 1u << (16u + slot);
            }
        }
        const uint32_t stateHash = HashAnimatorMixerState(branch);
        if (slot == 0u) { event->frames[5] = entryHash; event->frames[6] = stateHash; }
        else if (slot == 1u) { event->frames[7] = entryHash; event->extra[0] = stateHash; }
        else { event->extra[1] = entryHash; event->extra[2] = stateHash; }
    }
    event->extra[4] = status;
}

static LONG RecordAnimatorTransitionLifecycle(uint32_t kind, uintptr_t self,
    uintptr_t returnAddress, uintptr_t argument0, uintptr_t argument1,
    LONG pairedEntry) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = kind;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
    event->self = self;
    event->returnAddress = returnAddress;
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    event->stack[0] = g_animatorEvaluate.controller;
    event->stack[1] = static_cast<uintptr_t>(g_animatorEvaluate.updateGraphSequence);
    event->stack[2] = g_animatorEvaluate.entryOrdinal;
    event->stack[3] = static_cast<uintptr_t>(pairedEntry);
    event->stack[4] = static_cast<uintptr_t>(g_animatorEvaluate.entrySequence);
    event->stack[5] = argument0;
    event->stack[6] = argument1;
    CaptureAnimatorTransitionGraph(event, self);
    FinishEvent(event, sequence);
    return sequence;
}

static void PushAnimatorTransitionCall(uint32_t entryKind, uintptr_t self, LONG sequence) {
    if (g_animatorTransitionCallDepth >= 16u) {
        ++g_animatorTransitionCallOverflow;
        return;
    }
    AnimatorTransitionCall& call = g_animatorTransitionCalls[g_animatorTransitionCallDepth++];
    call.entryKind = entryKind;
    call.self = self;
    call.entrySequence = sequence;
}

static LONG PopAnimatorTransitionCall(uint32_t entryKind, uintptr_t self) {
    if (g_animatorTransitionCallOverflow) {
        --g_animatorTransitionCallOverflow;
        return 0;
    }
    if (!g_animatorTransitionCallDepth) return 0;
    AnimatorTransitionCall& call = g_animatorTransitionCalls[g_animatorTransitionCallDepth - 1u];
    if (call.entryKind != entryKind || call.self != self) return 0;
    --g_animatorTransitionCallDepth;
    return call.entrySequence;
}

static void __cdecl RecordAnimatorTransitionEntry(uint32_t kind, const uintptr_t* saved) {
    const uintptr_t argument0 = kind == EventAnimatorStartInterruptedTransitionEntry ? saved[10] : 0;
    const uintptr_t argument1 = kind == EventAnimatorStartInterruptedTransitionEntry ? saved[11] : 0;
    const LONG sequence = RecordAnimatorTransitionLifecycle(kind, saved[6], saved[9],
        argument0, argument1, 0);
    PushAnimatorTransitionCall(kind, saved[6], sequence);
}

// The wrapper preserves the original self below pushfd/pushad. Thus saved[9]
// is self, saved[10] is the original caller, and saved[11..12] are the two
// original StartInterruptedTransition arguments.
static void __cdecl RecordAnimatorTransitionExit(uint32_t kind, const uintptr_t* saved) {
    const uint32_t entryKind = kind == EventAnimatorEndTransitionExit
        ? EventAnimatorEndTransitionEntry : EventAnimatorStartInterruptedTransitionEntry;
    const uintptr_t argument0 = kind == EventAnimatorStartInterruptedTransitionExit ? saved[11] : 0;
    const uintptr_t argument1 = kind == EventAnimatorStartInterruptedTransitionExit ? saved[12] : 0;
    const LONG pairedEntry = PopAnimatorTransitionCall(entryKind, saved[9]);
    RecordAnimatorTransitionLifecycle(kind, saved[9], saved[10],
        argument0, argument1, pairedEntry);
}

// pushfd + pushad leaves the original entry state in this layout. In
// particular, saved[6] is ECX and saved[9] is the original return address.
static void __cdecl RecordEntry(uint32_t kind, const uintptr_t* saved) {
    if (kind == EventAnimatorControllerUpdateGraph &&
        (g_installedMask & MaskAnimatorStateMachine) != 0 &&
        (g_installedMask & MaskAnimatorScheduling) == 0 &&
        InterlockedCompareExchange(&g_animatorEvaluateCapture, 0, 0) == 0) return;
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = kind;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    event->self = (kind == EventAnimatorUpdateAvatars) ? saved[10] : saved[6];
    event->returnAddress = kind == EventTransformQueueChanges
        ? *reinterpret_cast<const uintptr_t*>(saved[2] + 4) : saved[9];
    if (kind == EventAnimatorControllerUpdateGraph) {
        g_animatorEvaluate.controller = saved[6];
        g_animatorEvaluate.updateGraphSequence = sequence;
        g_animatorEvaluate.ordinal = 0;
        g_animatorEvaluate.entryOrdinal = 0;
        g_animatorEvaluate.entrySequence = 0;
        for (uint32_t i = 0; i < 5; ++i) g_animatorEvaluate.arguments[i] = 0;
    }
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = saved[9 + i];
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    if (kind == EventRigidbodyUpdateMassDistribution) {
        uint16_t x87Control = 0;
        uint32_t mxcsr = 0;
        __asm fnstcw x87Control
        __asm stmxcsr mxcsr
        event->extra[0] = x87Control;
        event->extra[1] = mxcsr;
    }
    if (kind == EventRigidbodyMovePosition || kind == EventScbBodySetBody2World ||
        kind == EventNpRigidDynamicSetGlobalPose || kind == EventNpRigidDynamicSetKinematicTarget ||
        kind == EventScBodyCoreSetBody2World || kind == EventNpRigidBodySetCMassLocalPoseInternal) {
        const uintptr_t* payload = reinterpret_cast<const uintptr_t*>(saved[10]);
        const uint32_t count = kind == EventNpRigidBodySetCMassLocalPoseInternal ? 7 : 8;
        for (uint32_t i = 0; i < count; ++i) event->payload[i] = payload[i];
    } else if (kind == EventNpRigidBodySetMassSpaceInertiaTensor) {
        const uintptr_t* payload = reinterpret_cast<const uintptr_t*>(saved[10]);
        for (uint32_t i = 0; i < 3; ++i) event->payload[i] = payload[i];
    } else if (kind == EventRigidbodyUpdateMassDistribution) {
        const uintptr_t rigidbody = saved[6];
        event->payload[0] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
        event->payload[1] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x44);
        event->payload[2] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x50);
        event->payload[3] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x54);
        event->payload[4] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x58);
    } else if (kind == EventPhysicsManagerSimulate) {
        event->payload[0] = saved[10];
    } else if (kind == EventPhysicsManagerSyncTransforms) {
        CaptureTransformDispatchSummary(event);
    } else if (kind == EventTransformQueueChanges) {
        const uintptr_t transform = saved[6];
        const uintptr_t hierarchy = transform
            ? *reinterpret_cast<const uintptr_t*>(transform + 0x20) : 0;
        event->payload[0] = hierarchy;
        if (hierarchy) {
            event->payload[1] = *reinterpret_cast<const uintptr_t*>(hierarchy + 0x1C);
            event->payload[2] = *reinterpret_cast<const uintptr_t*>(hierarchy + 0x20);
            event->payload[3] = *reinterpret_cast<const uintptr_t*>(hierarchy + 0x24);
        }
        const uintptr_t dispatch = *reinterpret_cast<const uintptr_t*>(
            g_unityBase + kTransformChangeDispatchGlobalRva);
        event->payload[4] = dispatch;
        if (dispatch) {
            event->payload[5] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x10);
            event->payload[6] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x00);
            event->payload[7] = *reinterpret_cast<const uintptr_t*>(dispatch + 0x04);
        }
    } else if (kind == EventAnimatorUpdateAvatars) {
        // The first argument is a const dynamic_array<PlayableOutput*,4>&.
        // Unity's own entry code reads this exact layout before doing any work:
        // data at +0x00, count at +0x08, first output at data[0], and the
        // owning PlayableGraph at firstOutput+0x14. Preserve the graph's native
        // frame id and prepared delta before UpdateAvatars mutates anything.
        const uintptr_t list = saved[10];
        const uintptr_t data = list ? *reinterpret_cast<const uintptr_t*>(list + 0x00) : 0;
        const uint32_t count = list ? *reinterpret_cast<const uint32_t*>(list + 0x08) : 0;
        const uintptr_t firstOutput = data && count
            ? *reinterpret_cast<const uintptr_t*>(data) : 0;
        const uintptr_t graph = firstOutput
            ? *reinterpret_cast<const uintptr_t*>(firstOutput + 0x14) : 0;
        event->payload[0] = data;
        event->payload[1] = count;
        event->payload[2] = firstOutput;
        event->payload[3] = graph;
        if (graph) {
            event->payload[4] = *reinterpret_cast<const uintptr_t*>(graph + 0x10);
            event->payload[5] = *reinterpret_cast<const uintptr_t*>(graph + 0x14);
            event->payload[6] = *reinterpret_cast<const uintptr_t*>(graph + 0x20);
            event->payload[7] = *reinterpret_cast<const uintptr_t*>(graph + 0x24);
        }
    } else if (kind == EventAnimatorWriteProperties) {
        // Verified Unity 2017.4.8f1 x86 Animator offsets used by the shipped
        // WriteProperties/UpdateAvatars code. These are raw observations: the
        // tracer assigns no semantic meaning beyond fields proven in the PDB
        // disassembly. Animator+0x28C is the graph handle node; node+0x14 is
        // the PlayableGraph. Its +0x10/+0x14 frame id and +0x20 double are the
        // values copied into FrameData by PlayableGraph::PrepareFrame.
        const uintptr_t animator = saved[6];
        if (animator) {
            event->payload[0] = *reinterpret_cast<const uintptr_t*>(animator + 0x78);
            event->payload[1] = *reinterpret_cast<const uintptr_t*>(animator + 0x7C);
            event->payload[2] = *reinterpret_cast<const uintptr_t*>(animator + 0x80);
            event->payload[3] = *reinterpret_cast<const uintptr_t*>(animator + 0x84);
            event->payload[4] = *reinterpret_cast<const uintptr_t*>(animator + 0x268);
            event->payload[5] = *reinterpret_cast<const uintptr_t*>(animator + 0x288);
            const uintptr_t graphNode = *reinterpret_cast<const uintptr_t*>(animator + 0x28C);
            event->payload[6] = graphNode;
            event->payload[7] = *reinterpret_cast<const uintptr_t*>(animator + 0x290);
            event->extra[0] = graphNode
                ? *reinterpret_cast<const uintptr_t*>(graphNode + 0x14) : 0;
            if (event->extra[0]) {
                const uintptr_t graph = event->extra[0];
                event->extra[1] = *reinterpret_cast<const uintptr_t*>(graph + 0x10);
                event->extra[2] = *reinterpret_cast<const uintptr_t*>(graph + 0x14);
                event->extra[3] = *reinterpret_cast<const uintptr_t*>(graph + 0x20);
                event->extra[4] = *reinterpret_cast<const uintptr_t*>(graph + 0x24);
            }
        }
    } else if (kind == EventAnimatorControllerPrepareFrame ||
        kind == EventAnimatorControllerClearFirstEvaluationFlag ||
        kind == EventAnimatorControllerUpdateGraph) {
        // AnimatorControllerPlayable owns state that is not represented by the
        // recorder blob alone.  These offsets are read directly by the exact
        // Unity 2017.4.8f1 implementations at RVAs 0x649090, 0x646A60, and
        // 0x649CE0.  Capture the scheduler/evaluation gate words at entry so a
        // rewind run can be compared without mutating the controller or graph.
        const uintptr_t controller = saved[6];
        if (controller) {
            event->payload[0] = *reinterpret_cast<const uintptr_t*>(controller + 0xA0);
            event->payload[1] = *reinterpret_cast<const uintptr_t*>(controller + 0xB0);
            const uintptr_t memory = *reinterpret_cast<const uintptr_t*>(controller + 0xB4);
            event->payload[2] = memory;
            event->payload[3] = *reinterpret_cast<const uintptr_t*>(controller + 0xB8);
            event->payload[4] = *reinterpret_cast<const uintptr_t*>(controller + 0xAC);
            event->payload[5] = *reinterpret_cast<const uintptr_t*>(controller + 0x10);
            if (memory) {
                event->payload[6] = *reinterpret_cast<const uintptr_t*>(memory + 0x10);
                event->payload[7] = *reinterpret_cast<const uintptr_t*>(memory + 0x14);
                event->extra[0] = *reinterpret_cast<const uintptr_t*>(memory + 0x18);
                event->extra[1] = *reinterpret_cast<const uintptr_t*>(memory + 0x00);
                event->extra[2] = *reinterpret_cast<const uintptr_t*>(memory + 0x04);
                event->extra[3] = *reinterpret_cast<const uintptr_t*>(memory + 0x08);
                event->extra[4] = *reinterpret_cast<const uintptr_t*>(memory + 0x0C);
            }
        }
    } else if (kind == EventDirectorPrepareStage || kind == EventDirectorProcessStage) {
        event->payload[0] = saved[10];
    }
    USHORT captured = CaptureStackBackTrace(1, 8, reinterpret_cast<PVOID*>(event->frames), 0);
    for (USHORT i = captured; i < 8; ++i) event->frames[i] = 0;
    if (kind == EventAnimatorControllerUpdateGraph)
        CaptureAnimatorUpdateGraphStateMachineSummary(event, event->self);
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity)) g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
    if (kind == EventRigidbodyUpdateMassDistribution)
        RecordMassDistributionShapes(event->self,event->returnAddress,sequence);
    else if (kind == EventPhysicsManagerSyncTransforms)
        RecordTransformDispatchQueued(sequence);
}

static void __cdecl RecordSyncTransformsEntry(const uintptr_t* saved) {
    g_recordSyncTransformsPost = TransformDispatchHasPhysicsPending();
    if (g_recordSyncTransformsPost)
        RecordEntry(EventPhysicsManagerSyncTransforms, saved);
}

// Delimit the synchronous callback stream produced by a filtered
// PhysicsManager::SyncTransforms invocation. At this wrapper boundary the
// original ECX value is retained at saved[9] and the original caller return
// address is at saved[10]. Empty SyncTransforms calls never emit either edge.
static void __cdecl RecordSyncTransformsPost(const uintptr_t* saved) {
    if (!g_recordSyncTransformsPost) return;
    g_recordSyncTransformsPost = false;
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventPhysicsManagerSyncTransformsPost;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc; QueryPerformanceCounter(&qpc); event->qpc = qpc.QuadPart;
    event->self = saved[9];
    event->returnAddress = saved[10];
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    MemoryBarrier(); event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity))
        g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

static void __cdecl RecordTransformQueueChangesEntry(const uintptr_t* saved) {
    if (TransformHasPhysicsInterest(saved[6]))
        RecordEntry(EventTransformQueueChanges, saved);
}

// Record the exact native mass frame after Rigidbody::UpdateMassDistribution
// returns. The wrapper retains the original Rigidbody and caller return address
// below the saved register block. PhysX getters are read-only and dispatched
// through the same actor vtable used by the shipped implementation.
static void __cdecl RecordMassDistributionPost(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventRigidbodyUpdateMassDistributionPost;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    const uintptr_t rigidbody = saved[9];
    const uintptr_t actor = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    event->self = rigidbody;
    event->returnAddress = saved[10];
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    event->stack[0] = actor;
    event->stack[1] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x50);
    event->stack[2] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x54);
    event->stack[3] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x58);
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    if (actor != 0) {
        void** vtable = *reinterpret_cast<void***>(actor);
        typedef NativeTransform (__thiscall *GetCenter)(void* self);
        typedef NativeVec3 (__thiscall *GetInertia)(void* self);
        NativeTransform center = reinterpret_cast<GetCenter>(vtable[0x78 / 4])(
            reinterpret_cast<void*>(actor));
        NativeVec3 inertia = reinterpret_cast<GetInertia>(vtable[0x8C / 4])(
            reinterpret_cast<void*>(actor));
        CopyBytes(event->payload, &center, sizeof(center));
        CopyBytes(event->extra, &inertia, sizeof(inertia));
    }
    USHORT captured = CaptureStackBackTrace(1, 8, reinterpret_cast<PVOID*>(event->frames), 0);
    for (USHORT i = captured; i < 8; ++i) event->frames[i] = 0;
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity)) g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

// This probe runs at UnityPlayer RVA 0x480F1F, immediately after
// Rigidbody::ApplyConstraints calls PxRigidDynamic::getGlobalPose. At that
// instruction EDI is the Rigidbody, EBP is its active frame pointer, and the
// returned PxTransform occupies [EBP-0x94, EBP-0x78). Recording at this call
// boundary is safe for caller-volatile registers and does not alter the pose.
static void __cdecl RecordApplyConstraintsPostGetPose(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventRigidbodyApplyConstraintsPostGetPose;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    event->self = saved[0]; // EDI
    event->returnAddress = g_unityBase + 0x480F1F;
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    const uintptr_t rigidbody = saved[0];
    event->stack[0] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34); // PxRigidDynamic
    event->stack[1] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x58); // Rigidbody constraints
    event->stack[2] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x1C); // Transform
    const uintptr_t* pose = reinterpret_cast<const uintptr_t*>(saved[2] - 0x94); // EBP
    for (uint32_t i = 0; i < 7; ++i) event->payload[i] = pose[i];
    event->payload[7] = 0;
    USHORT captured = CaptureStackBackTrace(1, 8, reinterpret_cast<PVOID*>(event->frames), 0);
    for (USHORT i = captured; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity)) g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

// This probe runs at UnityPlayer RVA 0x482F81, immediately after
// Rigidbody::GetPosition calls PxRigidDynamic::getGlobalPose. At that point
// ESI is the Rigidbody and EAX points at the returned PxTransform. The hook is
// observation-only and preserves every general-purpose and SIMD register.
static void __cdecl RecordGetPositionPostGetPose(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventRigidbodyGetPositionPostGetPose;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    event->self = saved[1]; // ESI
    event->returnAddress = g_unityBase + 0x482F81;
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    const uintptr_t rigidbody = saved[1];
    if (rigidbody != 0) {
        event->stack[0] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34); // PxRigidDynamic
        event->stack[1] = *reinterpret_cast<const uintptr_t*>(saved[2] + 0x08); // Vector3f result
        event->stack[2] = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x1C); // Transform
    }
    const uintptr_t poseAddress = saved[7]; // EAX
    if (poseAddress != 0) {
        const uintptr_t* pose = reinterpret_cast<const uintptr_t*>(poseAddress);
        for (uint32_t i = 0; i < 7; ++i) event->payload[i] = pose[i];
        event->payload[7] = 0;
    } else {
        for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    }
    USHORT captured = CaptureStackBackTrace(1, 8, reinterpret_cast<PVOID*>(event->frames), 0);
    for (USHORT i = captured; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity)) g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

// PhysX 3.3.3 PxDiagonalize receives a hidden PxVec3 return pointer followed
// by the dense 3x3 inertia matrix and output quaternion. Capture the complete
// matrix and the read-only x87/SSE control words before any Jacobi iteration.
// UnityPlayer 2017.4.8f1 contains the source-identical 24-iteration routine at
// RVA 0xB2E3A0. No floating-point registers or control state are changed.
static void __cdecl RecordPxDiagonalizeEntry(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventPxDiagonalizeEntry;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    const uintptr_t result = saved[10];
    const uintptr_t matrix = saved[11];
    const uintptr_t axes = saved[12];
    event->self = matrix;
    event->returnAddress = saved[9];
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    event->stack[0] = result;
    event->stack[1] = matrix;
    event->stack[2] = axes;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    if (matrix != 0) {
        const uintptr_t* words = reinterpret_cast<const uintptr_t*>(matrix);
        for (uint32_t i = 0; i < 8; ++i) event->payload[i] = words[i];
        event->extra[0] = words[8];
    }
    uint16_t x87Control = 0;
    uint32_t mxcsr = 0;
    __asm fnstcw x87Control
    __asm stmxcsr mxcsr
    event->extra[1] = x87Control;
    event->extra[2] = mxcsr;
    USHORT captured = CaptureStackBackTrace(1, 8,
        reinterpret_cast<PVOID*>(event->frames), 0);
    for (USHORT i = captured; i < 8; ++i) event->frames[i] = 0;
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity))
        g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

// The shipped Unity 2017.4.8f1 function at RVA 0xA9E900 is the x86 cdecl
// PxcDiscreteNarrowPhasePCM entry. Its second argument is a PxcNpWorkUnit.
// The exact layout predates the public PhysX 3.4 layout, so retain raw words
// rather than assigning speculative field names. Shipped code reads through
// byte offset 0x61; this probe copies words 0x00..0x64. Stack slots 0..2
// retain the context, cache and output arguments. No stack walk is performed
// because this is a high-frequency, potentially worker-thread entry point.
static void __cdecl RecordPxcDiscreteNarrowPhasePcm(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventPxcDiscreteNarrowPhasePcm;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    const uintptr_t workUnitAddress = saved[11]; // cdecl argument 2
    event->self = workUnitAddress;
    event->returnAddress = saved[9];
    event->stack[0] = saved[10]; // argument 1 (thread context in the shipped code)
    event->stack[1] = saved[12]; // raw argument 3
    event->stack[2] = saved[13]; // raw argument 4
    const uintptr_t* workUnit = reinterpret_cast<const uintptr_t*>(workUnitAddress);
    for (uint32_t i = 0; i < 5; ++i) event->stack[3 + i] = workUnit[i];
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = workUnit[5 + i];
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = workUnit[13 + i];
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = workUnit[21 + i];
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity)) g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

// Sc::NPhaseCore::onOverlapCreated receives the two Sc::Element pointers
// selected by broadphase. Preserve their entry order and the first 32 bytes of
// each element without interpreting the stripped, version-specific layout.
static void __cdecl RecordNPhaseCoreOverlapCreated(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventNPhaseCoreOverlapCreated;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    event->self = saved[6]; // Sc::NPhaseCore this
    event->returnAddress = saved[9];
    event->stack[0] = saved[10]; // Element 0
    event->stack[1] = saved[11]; // Element 1
    event->stack[2] = saved[12]; // CCD pass
    for (uint32_t i = 3; i < 8; ++i) event->stack[i] = 0;
    const uintptr_t* element0 = reinterpret_cast<const uintptr_t*>(saved[10]);
    const uintptr_t* element1 = reinterpret_cast<const uintptr_t*>(saved[11]);
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = element0[i];
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = element1[i];
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity)) g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

// Sc::NPhaseCore::updateDirtyInteractions iterates the compacting hash set at
// NPhaseCore+0x44 directly through its dense mEntries array. Record the exact
// header and one event per live entry before the untouched shipped function
// consumes and clears it. The offsets and x86 layout are source-matched to
// PhysX 3.3.3 and independently visible in the shipped function at A540F0.
static void __cdecl RecordNPhaseCoreUpdateDirtyInteractions(const uintptr_t* saved) {
    const uintptr_t nphase = saved[6];
    const uintptr_t set = nphase + 0x44;
    const uintptr_t buffer = *reinterpret_cast<const uintptr_t*>(set + 0x00);
    const uintptr_t entries = *reinterpret_cast<const uintptr_t*>(set + 0x04);
    const uintptr_t entriesNext = *reinterpret_cast<const uintptr_t*>(set + 0x08);
    const uintptr_t hash = *reinterpret_cast<const uintptr_t*>(set + 0x0C);
    const uint32_t entriesCapacity = *reinterpret_cast<const uint32_t*>(set + 0x10);
    const uint32_t hashSize = *reinterpret_cast<const uint32_t*>(set + 0x14);
    const uint32_t loadFactorBits = *reinterpret_cast<const uint32_t*>(set + 0x18);
    const uint32_t freeList = *reinterpret_cast<const uint32_t*>(set + 0x1C);
    const uint32_t timestamp = *reinterpret_cast<const uint32_t*>(set + 0x20);
    const uint32_t count = *reinterpret_cast<const uint32_t*>(set + 0x24);

    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventNPhaseCoreUpdateDirtyInteractions;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    event->self = nphase;
    event->returnAddress = saved[9];
    event->stack[0] = set;
    event->stack[1] = buffer;
    event->stack[2] = entries;
    event->stack[3] = entriesNext;
    event->stack[4] = hash;
    event->stack[5] = entriesCapacity;
    event->stack[6] = hashSize;
    event->stack[7] = loadFactorBits;
    event->payload[0] = freeList;
    event->payload[1] = timestamp;
    event->payload[2] = count;
    const uintptr_t ownerScene = *reinterpret_cast<const uintptr_t*>(nphase);
    event->payload[3] = ownerScene;
    event->payload[4] = ownerScene != 0
        ? *reinterpret_cast<const uint8_t*>(ownerScene + 0x4A4) : 0;
    for (uint32_t i = 5; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity))
        g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);

    // Fail closed on a header that is inconsistent with a compacting set. A
    // valid empty set may have null entries; a non-empty one may not.
    if (count > entriesCapacity || count > 256u || (count != 0 && entries == 0)) return;
    const uintptr_t* dense = reinterpret_cast<const uintptr_t*>(entries);
    for (uint32_t index = 0; index < count; ++index) {
        sequence = InterlockedIncrement(&g_latestSequence);
        event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
        event->sequence = 0;
        event->kind = EventNPhaseCoreDirtyInteraction;
        event->threadId = GetCurrentThreadId();
        QueryPerformanceCounter(&qpc);
        event->qpc = qpc.QuadPart;
        const uintptr_t interaction = dense[index];
        event->self = interaction;
        event->returnAddress = saved[9];
        event->stack[0] = nphase;
        event->stack[1] = set;
        event->stack[2] = entries;
        event->stack[3] = index;
        event->stack[4] = count;
        event->stack[5] = entriesCapacity;
        event->stack[6] = hashSize;
        event->stack[7] = hash;
        for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
        for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
        for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
        if (interaction != 0) {
            const uintptr_t* raw = reinterpret_cast<const uintptr_t*>(interaction);
            for (uint32_t i = 0; i < 8; ++i) event->payload[i] = raw[i];
        }
        MemoryBarrier();
        event->sequence = sequence;
        if (sequence > static_cast<LONG>(kCapacity))
            g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
    }
}

// Record the ShapeInstancePairLL object at the exact createManager entry. This
// correlates each dense dirty-list pointer with the pair that subsequently
// receives a transform-cache id and PxsContactManager pool slot.
static void __cdecl RecordShapeInstancePairCreateManager(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventShapeInstancePairCreateManager;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    event->self = saved[6];
    event->returnAddress = saved[9];
    for (uint32_t i = 0; i < 8; ++i) event->stack[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    if (event->self != 0) {
        const uintptr_t* raw = reinterpret_cast<const uintptr_t*>(event->self);
        for (uint32_t i = 0; i < 8; ++i) event->stack[i] = raw[i];
        for (uint32_t i = 0; i < 8; ++i) event->payload[i] = raw[8 + i];
        // The shipped ShapeInstancePairLL allocation is 0x44 bytes. Keep the
        // read strictly within that object instead of sampling pool-adjacent
        // storage.
        event->frames[0] = raw[16];
    }
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity))
        g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

// This probe wraps PxsContext::createContactManager and records its return.
// The shipped implementation pops a PxsContactManager from a LIFO free stack
// at PxsContext+0x2B8. At this post-call boundary saved[9] is the preserved
// PxsContext pointer, saved[10] is the original caller return address and
// saved[11..12] are the original descriptor/material arguments. The selected
// manager's pool index and pair identity are read from offsets established by
// the shipped create/init disassembly. No allocator or manager state is changed.
static void __cdecl RecordPxsContactManagerCreated(const uintptr_t* saved) {
    LONG sequence = InterlockedIncrement(&g_latestSequence);
    TraceEvent* event = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
    event->sequence = 0;
    event->kind = EventPxsContactManagerCreated;
    event->threadId = GetCurrentThreadId();
    LARGE_INTEGER qpc;
    QueryPerformanceCounter(&qpc);
    event->qpc = qpc.QuadPart;
    const uintptr_t manager = saved[7]; // EAX return value
    const uintptr_t context = saved[9]; // explicitly preserved by the wrapper
    event->self = manager;
    event->returnAddress = saved[10];
    event->stack[0] = context;
    event->stack[1] = saved[11]; // PxvManagerDescRigidRigid
    event->stack[2] = saved[12]; // PxsMaterialManager
    for (uint32_t i = 3; i < 8; ++i) event->stack[i] = 0;
    if (saved[11] != 0)
        event->stack[6] = *reinterpret_cast<const uintptr_t*>(saved[11]); // descriptor userData / SIP
    for (uint32_t i = 0; i < 8; ++i) event->payload[i] = 0;
    for (uint32_t i = 0; i < 8; ++i) event->frames[i] = 0;
    for (uint32_t i = 0; i < 5; ++i) event->extra[i] = 0;
    if (context != 0) {
        const uint32_t postFreeCount = *reinterpret_cast<const uint32_t*>(context + 0x2CC);
        const uintptr_t freeArray = *reinterpret_cast<const uintptr_t*>(context + 0x2C8);
        event->stack[3] = postFreeCount;
        event->stack[4] = freeArray;
        if (freeArray != 0)
            event->stack[5] = reinterpret_cast<const uintptr_t*>(freeArray)[postFreeCount];
    }
    if (manager != 0) {
        event->payload[0] = *reinterpret_cast<const uintptr_t*>(manager + 0x4C); // pool index
        event->payload[1] = *reinterpret_cast<const uintptr_t*>(manager + 0x50); // rigid core 0
        event->payload[2] = *reinterpret_cast<const uintptr_t*>(manager + 0x54); // rigid core 1
        event->payload[3] = *reinterpret_cast<const uintptr_t*>(manager + 0x58); // shape core 0
        event->payload[4] = *reinterpret_cast<const uintptr_t*>(manager + 0x5C); // shape core 1
        event->payload[5] = *reinterpret_cast<const uintptr_t*>(manager + 0x60);
        event->payload[6] = *reinterpret_cast<const uintptr_t*>(manager + 0x74); // transform cache 0
        event->payload[7] = *reinterpret_cast<const uintptr_t*>(manager + 0x78); // transform cache 1
        const uintptr_t* raw = reinterpret_cast<const uintptr_t*>(manager);
        for (uint32_t i = 0; i < 8; ++i) event->frames[i] = raw[i];
        for (uint32_t i = 0; i < 5; ++i) event->extra[i] = raw[8 + i];
    }
    MemoryBarrier();
    event->sequence = sequence;
    if (sequence > static_cast<LONG>(kCapacity)) g_droppedEstimate = sequence - static_cast<LONG>(kCapacity);
}

#define TRACE_HOOK(name, kind, trampoline) \
    __declspec(naked) static void name() { \
        __asm pushfd \
        __asm pushad \
        __asm mov eax, esp \
        __asm push eax \
        __asm push kind \
        __asm call RecordEntry \
        __asm add esp, 8 \
        __asm popad \
        __asm popfd \
        __asm jmp dword ptr [trampoline] \
    }

TRACE_HOOK(HookAwakeFromLoad, EventRigidbodyAwakeFromLoad, g_trampolineAwakeFromLoad)
TRACE_HOOK(HookCreate, EventRigidbodyCreate, g_trampolineCreate)
TRACE_HOOK(HookMovePosition, EventRigidbodyMovePosition, g_trampolineMovePosition)
TRACE_HOOK(HookSetIsKinematic, EventRigidbodySetIsKinematic, g_trampolineSetIsKinematic)
TRACE_HOOK(HookAddRigidBody, EventSceneAddRigidBody, g_trampolineAddRigidBody)
TRACE_HOOK(HookRemoveRigidBody, EventSceneRemoveRigidBody, g_trampolineRemoveRigidBody)
TRACE_HOOK(HookScbSetBody2World, EventScbBodySetBody2World, g_trampolineScbSetBody2World)
TRACE_HOOK(HookNpSetGlobalPose, EventNpRigidDynamicSetGlobalPose, g_trampolineNpSetGlobalPose)
TRACE_HOOK(HookNpSetKinematicTarget, EventNpRigidDynamicSetKinematicTarget, g_trampolineNpSetKinematicTarget)
TRACE_HOOK(HookScSetBody2World, EventScBodyCoreSetBody2World, g_trampolineScSetBody2World)
TRACE_HOOK(HookNpSetCMassLocalPoseInternal, EventNpRigidBodySetCMassLocalPoseInternal, g_trampolineNpSetCMassLocalPoseInternal)
TRACE_HOOK(HookNpSetMassSpaceInertiaTensor, EventNpRigidBodySetMassSpaceInertiaTensor, g_trampolineNpSetMassSpaceInertiaTensor)
TRACE_HOOK(HookPhysicsManagerSimulate, EventPhysicsManagerSimulate, g_trampolinePhysicsManagerSimulate)
TRACE_HOOK(HookBoxColliderPoseChanged, EventBoxColliderPoseChanged, g_trampolineBoxColliderPoseChanged)
TRACE_HOOK(HookPxDiagonalize, EventPxDiagonalizeEntry, g_trampolinePxDiagonalize)
TRACE_HOOK(HookAnimatorUpdateAvatars, EventAnimatorUpdateAvatars, g_trampolineAnimatorUpdateAvatars)
TRACE_HOOK(HookAnimatorWriteProperties, EventAnimatorWriteProperties, g_trampolineAnimatorWriteProperties)
TRACE_HOOK(HookDirectorPrepareStage, EventDirectorPrepareStage, g_trampolineDirectorPrepareStage)
TRACE_HOOK(HookDirectorProcessStage, EventDirectorProcessStage, g_trampolineDirectorProcessStage)
TRACE_HOOK(HookAnimatorControllerPrepareFrame, EventAnimatorControllerPrepareFrame, g_trampolineAnimatorControllerPrepareFrame)
TRACE_HOOK(HookAnimatorControllerClearFirstEvaluationFlag, EventAnimatorControllerClearFirstEvaluationFlag, g_trampolineAnimatorControllerClearFirstEvaluationFlag)
TRACE_HOOK(HookAnimatorControllerUpdateGraph, EventAnimatorControllerUpdateGraph, g_trampolineAnimatorControllerUpdateGraph)

__declspec(naked) static void HookAnimatorEvaluateStateMachine() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordAnimatorEvaluateEntry
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_trampolineAnimatorEvaluateStateMachine]
}

#define ANIMATOR_EVALUATE_EXIT_HOOK(name, kind, trampoline) \
    __declspec(naked) static void name() { \
        __asm pushfd \
        __asm pushad \
        __asm mov eax, esp \
        __asm push eax \
        __asm push kind \
        __asm call RecordAnimatorEvaluateExit \
        __asm add esp, 8 \
        __asm popad \
        __asm popfd \
        __asm jmp dword ptr [trampoline] \
    }

ANIMATOR_EVALUATE_EXIT_HOOK(HookAnimatorEvaluateStateMachineEarlyExit,
    EventAnimatorEvaluateStateMachineEarlyExit, g_trampolineAnimatorEvaluateStateMachineEarlyExit)
ANIMATOR_EVALUATE_EXIT_HOOK(HookAnimatorEvaluateStateMachineExit,
    EventAnimatorEvaluateStateMachineExit, g_trampolineAnimatorEvaluateStateMachineExit)
#undef ANIMATOR_EVALUATE_EXIT_HOOK

// EvaluateCondition uses XMM1 for the live float and immediately consumes it
// in the stolen COMISS instruction. Preserve every SIMD register around the
// observer so the trampoline receives the exact original comparison state.
__declspec(naked) static void HookAnimatorConditionFloat() {
    __asm sub esp, 128
    __asm movdqu [esp], xmm0
    __asm movdqu [esp + 16], xmm1
    __asm movdqu [esp + 32], xmm2
    __asm movdqu [esp + 48], xmm3
    __asm movdqu [esp + 64], xmm4
    __asm movdqu [esp + 80], xmm5
    __asm movdqu [esp + 96], xmm6
    __asm movdqu [esp + 112], xmm7
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm mov edx, dword ptr [esp + 52]
    __asm push edx
    __asm push eax
    __asm call RecordAnimatorConditionFloat
    __asm add esp, 8
    __asm popad
    __asm popfd
    __asm movdqu xmm0, [esp]
    __asm movdqu xmm1, [esp + 16]
    __asm movdqu xmm2, [esp + 32]
    __asm movdqu xmm3, [esp + 48]
    __asm movdqu xmm4, [esp + 64]
    __asm movdqu xmm5, [esp + 80]
    __asm movdqu xmm6, [esp + 96]
    __asm movdqu xmm7, [esp + 112]
    __asm add esp, 128
    __asm jmp dword ptr [g_trampolineAnimatorConditionFloat]
}

__declspec(naked) static void HookAnimatorEndTransition() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm push EventAnimatorEndTransitionEntry
    __asm call RecordAnimatorTransitionEntry
    __asm add esp, 8
    __asm popad
    __asm popfd
    __asm push ecx
    __asm call dword ptr [g_trampolineAnimatorEndTransition]
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm push EventAnimatorEndTransitionExit
    __asm call RecordAnimatorTransitionExit
    __asm add esp, 8
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret
}

__declspec(naked) static void HookAnimatorStartInterruptedTransition() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm push EventAnimatorStartInterruptedTransitionEntry
    __asm call RecordAnimatorTransitionEntry
    __asm add esp, 8
    __asm popad
    __asm popfd
    __asm push ecx
    // Duplicate the original two stack arguments for the real thiscall. Its
    // RET 8 consumes only these duplicates; the caller's originals remain for
    // our exit snapshot and final RET 8.
    __asm push dword ptr [esp + 12]
    __asm push dword ptr [esp + 12]
    __asm call dword ptr [g_trampolineAnimatorStartInterruptedTransition]
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm push EventAnimatorStartInterruptedTransitionExit
    __asm call RecordAnimatorTransitionExit
    __asm add esp, 8
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret 8
}

// The paused player can call SyncTransforms thousands of times per second.
// Record only invocations that will drain one of PhysicsManager's four change
// handles; empty calls are irrelevant and would evict the advancing phase.
// Wrap the original call so kind 45 exactly bounds its synchronous callbacks.
__declspec(naked) static void HookPhysicsManagerSyncTransforms() {
    __asm push ecx
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordSyncTransformsEntry
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm call dword ptr [g_trampolinePhysicsManagerSyncTransforms]
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordSyncTransformsPost
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret
}

// This patchpoint is after QueueChanges' relocatable global load and before it
// mutates the hierarchy or global queue. Record only transforms carrying one
// of the physics interest bits.
__declspec(naked) static void HookTransformQueueChanges() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordTransformQueueChangesEntry
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_trampolineTransformQueueChanges]
}

__declspec(naked) static void HookApplyConstraintsPostGetPose() {
    __asm sub esp, 128
    __asm movdqu [esp], xmm0
    __asm movdqu [esp + 16], xmm1
    __asm movdqu [esp + 32], xmm2
    __asm movdqu [esp + 48], xmm3
    __asm movdqu [esp + 64], xmm4
    __asm movdqu [esp + 80], xmm5
    __asm movdqu [esp + 96], xmm6
    __asm movdqu [esp + 112], xmm7
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordApplyConstraintsPostGetPose
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm movdqu xmm0, [esp]
    __asm movdqu xmm1, [esp + 16]
    __asm movdqu xmm2, [esp + 32]
    __asm movdqu xmm3, [esp + 48]
    __asm movdqu xmm4, [esp + 64]
    __asm movdqu xmm5, [esp + 80]
    __asm movdqu xmm6, [esp + 96]
    __asm movdqu xmm7, [esp + 112]
    __asm add esp, 128
    __asm jmp dword ptr [g_trampolineApplyConstraintsPostGetPose]
}

__declspec(naked) static void HookGetPositionPostGetPose() {
    __asm sub esp, 128
    __asm movdqu [esp], xmm0
    __asm movdqu [esp + 16], xmm1
    __asm movdqu [esp + 32], xmm2
    __asm movdqu [esp + 48], xmm3
    __asm movdqu [esp + 64], xmm4
    __asm movdqu [esp + 80], xmm5
    __asm movdqu [esp + 96], xmm6
    __asm movdqu [esp + 112], xmm7
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordGetPositionPostGetPose
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm movdqu xmm0, [esp]
    __asm movdqu xmm1, [esp + 16]
    __asm movdqu xmm2, [esp + 32]
    __asm movdqu xmm3, [esp + 48]
    __asm movdqu xmm4, [esp + 64]
    __asm movdqu xmm5, [esp + 80]
    __asm movdqu xmm6, [esp + 96]
    __asm movdqu xmm7, [esp + 112]
    __asm add esp, 128
    __asm jmp dword ptr [g_trampolineGetPositionPostGetPose]
}

__declspec(naked) static void HookPxcDiscreteNarrowPhasePcm() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordPxcDiscreteNarrowPhasePcm
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_trampolinePxcDiscreteNarrowPhasePcm]
}

__declspec(naked) static void HookNPhaseCoreOverlapCreated() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordNPhaseCoreOverlapCreated
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_trampolineNPhaseCoreOverlapCreated]
}

__declspec(naked) static void HookNPhaseCoreUpdateDirtyInteractions() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordNPhaseCoreUpdateDirtyInteractions
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_trampolineNPhaseCoreUpdateDirtyInteractions]
}

__declspec(naked) static void HookShapeInstancePairCreateManager() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordShapeInstancePairCreateManager
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_trampolineShapeInstancePairCreateManager]
}

// Preserve the original thiscall stack, invoke the trampoline with copied
// arguments, record the returned manager, then perform the original ret 8.
// The saved context word stays below pushfd/pushad and is never exposed to the
// original implementation.
__declspec(naked) static void HookPxsContextCreateContactManager() {
    __asm push ecx
    __asm push dword ptr [esp + 0x0C]
    __asm push dword ptr [esp + 0x0C]
    __asm call dword ptr [g_trampolinePxsContextCreateContactManager]
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordPxsContactManagerCreated
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret 8
}

// UpdateMassDistribution is void thiscall with no stack arguments. Keep the
// Rigidbody pointer below the trampoline call, observe its result, then return
// with the trampoline's complete general-register and flag state restored.
__declspec(naked) static void HookRigidbodyUpdateMassDistribution() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm push EventRigidbodyUpdateMassDistribution
    __asm call RecordEntry
    __asm add esp, 8
    __asm popad
    __asm popfd
    __asm push ecx
    __asm call dword ptr [g_trampolineRigidbodyUpdateMassDistribution]
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call RecordMassDistributionPost
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret
}

#undef TRACE_HOOK

static HookSpec g_hooks[] = {
    {EventRigidbodyAwakeFromLoad, MaskUnityLifecycle, 0x481B50, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x0C}, HookAwakeFromLoad, &g_trampolineAwakeFromLoad},
    {EventRigidbodyCreate, MaskUnityLifecycle, 0x482510, 9, {0x55,0x8B,0xEC,0x81,0xEC,0x90,0x00,0x00,0x00}, HookCreate, &g_trampolineCreate},
    {EventRigidbodyMovePosition, MaskUnityLifecycle, 0x483550, 6, {0x55,0x8B,0xEC,0x8B,0x45,0x08}, HookMovePosition, &g_trampolineMovePosition},
    {EventRigidbodySetIsKinematic, MaskUnityLifecycle, 0x4840D0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x2C}, HookSetIsKinematic, &g_trampolineSetIsKinematic},
    {EventSceneAddRigidBody, MaskSceneLifecycle, 0xA1F0C0, 9, {0x55,0x8B,0xEC,0x81,0xEC,0x24,0x02,0x00,0x00}, HookAddRigidBody, &g_trampolineAddRigidBody},
    {EventSceneRemoveRigidBody, MaskSceneLifecycle, 0xA20290, 9, {0x55,0x8B,0xEC,0x81,0xEC,0x20,0x02,0x00,0x00}, HookRemoveRigidBody, &g_trampolineRemoveRigidBody},
    {EventScbBodySetBody2World, MaskPhysxPose, 0xA136D0, 6, {0x55,0x8B,0xEC,0x56,0x8B,0xF1}, HookScbSetBody2World, &g_trampolineScbSetBody2World},
    {EventNpRigidDynamicSetGlobalPose, MaskPhysxPose, 0xA143D0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x44}, HookNpSetGlobalPose, &g_trampolineNpSetGlobalPose},
    {EventNpRigidDynamicSetKinematicTarget, MaskPhysxPose, 0xA149E0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x38}, HookNpSetKinematicTarget, &g_trampolineNpSetKinematicTarget},
    {EventScBodyCoreSetBody2World, MaskPhysxPose, 0xA3A9B0, 6, {0x55,0x8B,0xEC,0x8B,0x55,0x08}, HookScSetBody2World, &g_trampolineScSetBody2World},
    {EventNpRigidBodySetCMassLocalPoseInternal, MaskPhysxPose, 0xA13E90, 9, {0x55,0x8B,0xEC,0x83,0xEC,0x48,0x53,0x8B,0xD9}, HookNpSetCMassLocalPoseInternal, &g_trampolineNpSetCMassLocalPoseInternal},
    {EventNpRigidBodySetMassSpaceInertiaTensor, MaskPhysxPose, 0xA14E80, 9, {0x55,0x8B,0xEC,0x8B,0x55,0x08,0x83,0xEC,0x0C}, HookNpSetMassSpaceInertiaTensor, &g_trampolineNpSetMassSpaceInertiaTensor},
    {EventRigidbodyUpdateMassDistribution, MaskMassFrame, 0x484D80, 9, {0x55,0x8B,0xEC,0x83,0xEC,0x4C,0x53,0x56,0x57}, HookRigidbodyUpdateMassDistribution, &g_trampolineRigidbodyUpdateMassDistribution},
    {EventBoxColliderPoseChanged, MaskMassFrame, 0x465240, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x40}, HookBoxColliderPoseChanged, &g_trampolineBoxColliderPoseChanged},
    {EventPxDiagonalizeEntry, MaskMassFrame, 0xB2E3A0, 9, {0x55,0x8B,0xEC,0x81,0xEC,0xA0,0x00,0x00,0x00}, HookPxDiagonalize, &g_trampolinePxDiagonalize},
    {EventPhysicsManagerSimulate, MaskPhysicsPhases, 0x47BA70, 9, {0x55,0x8B,0xEC,0x81,0xEC,0xA8,0x04,0x00,0x00}, HookPhysicsManagerSimulate, &g_trampolinePhysicsManagerSimulate},
    {EventPhysicsManagerSyncTransforms, MaskTransformDispatch, 0x47C8B0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x3C}, HookPhysicsManagerSyncTransforms, &g_trampolinePhysicsManagerSyncTransforms},
    {EventTransformQueueChanges, MaskTransformDispatch, 0x43F35A, 7, {0x56,0x8B,0x71,0x20,0x8B,0x46,0x20}, HookTransformQueueChanges, &g_trampolineTransformQueueChanges},
    {EventRigidbodyApplyConstraintsPostGetPose, MaskPhysicsPhases, 0x480F1F, 5, {0xF3,0x0F,0x10,0x75,0xC4}, HookApplyConstraintsPostGetPose, &g_trampolineApplyConstraintsPostGetPose},
    {EventRigidbodyGetPositionPostGetPose, MaskPhysicsPhases, 0x482F81, 5, {0x8B,0xD0,0x8B,0x45,0x08}, HookGetPositionPostGetPose, &g_trampolineGetPositionPostGetPose},
    {EventPxcDiscreteNarrowPhasePcm, MaskNarrowPhase, 0xA9E900, 9, {0x53,0x8B,0xDC,0x83,0xEC,0x08,0x83,0xE4,0xF0}, HookPxcDiscreteNarrowPhasePcm, &g_trampolinePxcDiscreteNarrowPhasePcm},
    {EventNPhaseCoreOverlapCreated, MaskNarrowPhase, 0xA51210, 9, {0x55,0x8B,0xEC,0x81,0xEC,0x94,0x00,0x00,0x00}, HookNPhaseCoreOverlapCreated, &g_trampolineNPhaseCoreOverlapCreated},
    {EventPxsContactManagerCreated, MaskNarrowPhase, 0xA69E80, 6, {0x55,0x8B,0xEC,0x53,0x8B,0xD9}, HookPxsContextCreateContactManager, &g_trampolinePxsContextCreateContactManager},
    {EventNPhaseCoreUpdateDirtyInteractions, MaskNarrowPhase, 0xA540F0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x34}, HookNPhaseCoreUpdateDirtyInteractions, &g_trampolineNPhaseCoreUpdateDirtyInteractions},
    {EventShapeInstancePairCreateManager, MaskNarrowPhase, 0xA54430, 9, {0x55,0x8B,0xEC,0x81,0xEC,0x94,0x00,0x00,0x00}, HookShapeInstancePairCreateManager, &g_trampolineShapeInstancePairCreateManager},
    {EventAnimatorUpdateAvatars, MaskAnimatorScheduling, 0x62B5D0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x68}, HookAnimatorUpdateAvatars, &g_trampolineAnimatorUpdateAvatars},
    {EventAnimatorWriteProperties, MaskAnimatorScheduling, 0x62C750, 6, {0x55,0x8B,0xEC,0x56,0x8B,0xF1}, HookAnimatorWriteProperties, &g_trampolineAnimatorWriteProperties},
    {EventDirectorPrepareStage, MaskAnimatorScheduling, 0x2F2030, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x18}, HookDirectorPrepareStage, &g_trampolineDirectorPrepareStage},
    {EventDirectorProcessStage, MaskAnimatorScheduling, 0x2F2230, 6, {0x55,0x8B,0xEC,0x8B,0x45,0x08}, HookDirectorProcessStage, &g_trampolineDirectorProcessStage},
    {EventAnimatorControllerPrepareFrame, MaskAnimatorScheduling, 0x649090, 6, {0x55,0x8B,0xEC,0x56,0x8B,0xF1}, HookAnimatorControllerPrepareFrame, &g_trampolineAnimatorControllerPrepareFrame},
    {EventAnimatorControllerClearFirstEvaluationFlag, MaskAnimatorScheduling, 0x646A60, 9, {0x56,0x8B,0xF1,0x8B,0x86,0xA0,0x00,0x00,0x00}, HookAnimatorControllerClearFirstEvaluationFlag, &g_trampolineAnimatorControllerClearFirstEvaluationFlag},
    {EventAnimatorControllerUpdateGraph, MaskAnimatorScheduling | MaskAnimatorStateMachine, 0x649CE0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x5C}, HookAnimatorControllerUpdateGraph, &g_trampolineAnimatorControllerUpdateGraph},
    {EventAnimatorEvaluateStateMachineEntry, MaskAnimatorStateMachine, 0x663C50, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x40}, HookAnimatorEvaluateStateMachine, &g_trampolineAnimatorEvaluateStateMachine},
    {EventAnimatorEvaluateStateMachineEarlyExit, MaskAnimatorStateMachine, 0x663C8A, 5, {0x5B,0x8B,0xE5,0x5D,0xC3}, HookAnimatorEvaluateStateMachineEarlyExit, &g_trampolineAnimatorEvaluateStateMachineEarlyExit},
    {EventAnimatorEvaluateStateMachineExit, MaskAnimatorStateMachine, 0x664650, 5, {0x5B,0x8B,0xE5,0x5D,0xC3}, HookAnimatorEvaluateStateMachineExit, &g_trampolineAnimatorEvaluateStateMachineExit},
    {EventAnimatorConditionFloat, MaskAnimatorStateMachine, 0x663524, 5, {0x0F,0x2F,0x4E,0x08,0x5F}, HookAnimatorConditionFloat, &g_trampolineAnimatorConditionFloat},
    {EventAnimatorEndTransitionEntry, MaskAnimatorTransitionLifecycle, 0x646CD0, 6, {0x55,0x8B,0xEC,0x83,0xEC,0x08}, HookAnimatorEndTransition, &g_trampolineAnimatorEndTransition},
    {EventAnimatorStartInterruptedTransitionEntry, MaskAnimatorTransitionLifecycle, 0x649C60, 6, {0x55,0x8B,0xEC,0x53,0x8B,0xD9}, HookAnimatorStartInterruptedTransition, &g_trampolineAnimatorStartInterruptedTransition}
};

static bool WriteJump(void* source, void* destination, uint32_t size) {
    if (size < 5) return false;
    DWORD oldProtect = 0;
    if (!VirtualProtect(source, size, PAGE_EXECUTE_READWRITE, &oldProtect)) return false;
    uint8_t* bytes = static_cast<uint8_t*>(source);
    bytes[0] = 0xE9;
    *reinterpret_cast<int32_t*>(bytes + 1) = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(destination) - reinterpret_cast<uintptr_t>(source) - 5);
    for (uint32_t i = 5; i < size; ++i) bytes[i] = 0x90;
    FlushInstructionCache(GetCurrentProcess(), source, size);
    DWORD ignored = 0;
    return VirtualProtect(source, size, oldProtect, &ignored) != FALSE;
}

static bool InstallOne(HookSpec& spec) {
    uint8_t* source = reinterpret_cast<uint8_t*>(g_unityBase + spec.rva);
    if (!EqualBytes(source, spec.expected, spec.stolen)) {
        g_lastError = ERROR_REVISION_MISMATCH;
        return false;
    }
    CopyBytes(spec.original, source, spec.stolen);
    uint8_t* trampoline = static_cast<uint8_t*>(VirtualAlloc(0, spec.stolen + 5,
        MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!trampoline) { g_lastError = GetLastError(); return false; }
    CopyBytes(trampoline, source, spec.stolen);
    trampoline[spec.stolen] = 0xE9;
    *reinterpret_cast<int32_t*>(trampoline + spec.stolen + 1) = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(source + spec.stolen) -
        reinterpret_cast<uintptr_t>(trampoline + spec.stolen) - 5);
    FlushInstructionCache(GetCurrentProcess(), trampoline, spec.stolen + 5);
    spec.trampoline = trampoline;
    *spec.trampolineSlot = trampoline;
    if (!WriteJump(source, spec.hook, spec.stolen)) {
        g_lastError = GetLastError();
        *spec.trampolineSlot = 0;
        VirtualFree(trampoline, 0, MEM_RELEASE);
        spec.trampoline = 0;
        return false;
    }
    spec.installed = true;
    ++g_installedHookCount;
    return true;
}

static bool UninstallOne(HookSpec& spec) {
    if (!spec.installed) return true;
    uint8_t* source = reinterpret_cast<uint8_t*>(g_unityBase + spec.rva);
    DWORD oldProtect = 0;
    if (!VirtualProtect(source, spec.stolen, PAGE_EXECUTE_READWRITE, &oldProtect)) {
        g_lastError = GetLastError();
        return false;
    }
    CopyBytes(source, spec.original, spec.stolen);
    FlushInstructionCache(GetCurrentProcess(), source, spec.stolen);
    DWORD ignored = 0;
    VirtualProtect(source, spec.stolen, oldProtect, &ignored);
    spec.installed = false;
    --g_installedHookCount;
    *spec.trampolineSlot = 0;
    if (spec.trampoline) VirtualFree(spec.trampoline, 0, MEM_RELEASE);
    spec.trampoline = 0;
    return true;
}

static bool UninstallAll() {
    bool ok = true;
    for (int i = static_cast<int>(sizeof(g_hooks) / sizeof(g_hooks[0])) - 1; i >= 0; --i)
        if (!UninstallOne(g_hooks[i])) ok = false;
    g_installedMask = 0;
    return ok;
}

} // namespace

extern "C" __declspec(dllexport) uint32_t __cdecl oc2_trace_api_version() {
    return kApiVersion;
}

extern "C" __declspec(dllexport) int __cdecl oc2_trace_install(uintptr_t unityBase, uint32_t mask) {
    if (!unityBase) { g_lastError = 0xE001u; return 0; }
    if (!mask) { g_lastError = 0xE002u; return 0; }
    if ((mask & ~static_cast<uint32_t>(MaskAll)) != 0) {
        g_lastError = 0xE100u | (mask & 0xFFu);
        return 0;
    }
    if (g_installedHookCount != 0) {
        g_lastError = ERROR_ALREADY_EXISTS;
        return 0;
    }
    g_unityBase = unityBase;
    g_lastError = 0;
    if ((mask & MaskMassFrame) != 0 && !ValidateMassShapeReaders()) {
        g_lastError = ERROR_REVISION_MISMATCH;
        return 0;
    }
    for (uint32_t i = 0; i < sizeof(g_hooks) / sizeof(g_hooks[0]); ++i) {
        HookSpec& hook = g_hooks[i];
        if ((hook.mask & mask) != 0 && !InstallOne(hook)) {
            UninstallAll();
            return 0;
        }
    }
    g_installedMask = mask;
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl oc2_trace_uninstall() {
    InterlockedExchange(&g_animatorEvaluateCapture, 0);
    g_animatorTransitionCallDepth = 0;
    g_animatorTransitionCallOverflow = 0;
    return UninstallAll() ? 1 : 0;
}

extern "C" __declspec(dllexport) void __cdecl oc2_trace_clear() {
    InterlockedExchange(&g_animatorEvaluateCapture, 0);
    g_animatorTransitionCallDepth = 0;
    g_animatorTransitionCallOverflow = 0;
    for (uint32_t i = 0; i < kCapacity; ++i) g_events[i].sequence = 0;
    g_latestSequence = 0;
    g_droppedEstimate = 0;
}

extern "C" __declspec(dllexport) void __cdecl oc2_trace_mark(uint32_t code, uintptr_t value) {
    if (code == 230u || code == 270u) InterlockedExchange(&g_animatorEvaluateCapture, 0);
    uintptr_t synthetic[17] = {};
    synthetic[6] = value;
    synthetic[9] = code;
    RecordEntry(EventMarker, synthetic);
    if (code == 220u || code == 260u) InterlockedExchange(&g_animatorEvaluateCapture, 1);
}

extern "C" __declspec(dllexport) int __cdecl oc2_trace_status(TraceStatus* status) {
    if (!status) return 0;
    status->apiVersion = kApiVersion;
    status->structSize = sizeof(TraceStatus);
    status->eventSize = sizeof(TraceEvent);
    status->installedMask = g_installedMask;
    status->installedHookCount = g_installedHookCount;
    status->capacity = kCapacity;
    status->latestSequence = g_latestSequence;
    status->droppedEstimate = g_droppedEstimate;
    status->lastError = g_lastError;
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl oc2_trace_read(
    int32_t afterSequence, TraceEvent* output, int32_t capacity) {
    if (!output || capacity <= 0) return 0;
    LONG latest = g_latestSequence;
    LONG first = latest > static_cast<LONG>(kCapacity) ? latest - static_cast<LONG>(kCapacity) + 1 : 1;
    if (afterSequence >= first) first = afterSequence + 1;
    if (first > latest) return 0;
    LONG available = latest - first + 1;
    if (available > capacity) first = latest - capacity + 1;
    int32_t written = 0;
    for (LONG sequence = first; sequence <= latest && written < capacity; ++sequence) {
        TraceEvent* source = &g_events[static_cast<uint32_t>(sequence - 1) % kCapacity];
        if (source->sequence != sequence) continue;
        output[written++] = *source;
    }
    return written;
}

BOOL WINAPI DllMain(HINSTANCE, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_DETACH) UninstallAll();
    return TRUE;
}
