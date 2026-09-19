#include <windows.h>
#include <stdint.h>

#if !defined(_M_IX86)
#error This helper requires the Unity 2017 Win32/x86 ABI.
#endif

namespace {

static bool EqualBytes(const void* left, const uint8_t* right, uint32_t count) {
    const uint8_t* value = static_cast<const uint8_t*>(left);
    for (uint32_t i = 0; i < count; ++i) if (value[i] != right[i]) return false;
    return true;
}

static bool Readable(const void* pointer, uint32_t bytes) {
    MEMORY_BASIC_INFORMATION memory = {};
    if (!pointer || VirtualQuery(pointer, &memory, sizeof(memory)) != sizeof(memory)) return false;
    if (memory.State != MEM_COMMIT || (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0) return false;
    uintptr_t begin = reinterpret_cast<uintptr_t>(pointer);
    uintptr_t end = begin + bytes;
    uintptr_t regionEnd = reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize;
    return end >= begin && end <= regionEnd;
}

static bool Writable(void* pointer, uint32_t bytes) {
    MEMORY_BASIC_INFORMATION memory = {};
    if (!pointer || VirtualQuery(pointer, &memory, sizeof(memory)) != sizeof(memory)) return false;
    if (memory.State != MEM_COMMIT || (memory.Protect & (PAGE_GUARD | PAGE_NOACCESS)) != 0) return false;
    const DWORD protection = memory.Protect & 0xFF;
    if (protection != PAGE_READWRITE && protection != PAGE_WRITECOPY &&
        protection != PAGE_EXECUTE_READWRITE && protection != PAGE_EXECUTE_WRITECOPY) return false;
    uintptr_t begin = reinterpret_cast<uintptr_t>(pointer);
    uintptr_t end = begin + bytes;
    uintptr_t regionEnd = reinterpret_cast<uintptr_t>(memory.BaseAddress) + memory.RegionSize;
    return end >= begin && end <= regionEnd;
}

static void CopyWords(uintptr_t* destination, const uintptr_t* source, uint32_t count) {
    for (uint32_t i = 0; i < count; ++i) destination[i] = source[i];
}

static void CopyBytes(void* destination, const void* source, uint32_t count) {
    uint8_t* output = static_cast<uint8_t*>(destination);
    const uint8_t* input = static_cast<const uint8_t*>(source);
    for (uint32_t i = 0; i < count; ++i) output[i] = input[i];
}

static uint32_t OrderHash(const uintptr_t* values, uint32_t count) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= static_cast<uint32_t>(values[i]);
        hash *= 16777619u;
    }
    return hash;
}

static uint32_t PointerIndex(const uintptr_t* values, uint32_t count,
    uintptr_t target) {
    for (uint32_t i = 0; i < count; ++i)
        if (values[i] == target) return i;
    return 0xFFFFFFFFu;
}

#pragma pack(push, 8)
struct RebuildReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actorBefore;
    uintptr_t actorAfterInactiveCreate;
    uintptr_t actorAfterActiveCreate;
};

struct ContactPoolReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t context;
    uintptr_t freeArray;
    uint32_t freeCount;
    uint32_t orderHashBefore;
    uint32_t orderHashAfter;
    uintptr_t top[16];
};

struct ContactContextObserverReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t observedContext;
    uint32_t observations;
    uint32_t installed;
};

struct ManifoldPoolReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t context;
    uintptr_t pool;
    uint32_t poolKind;
    uintptr_t freeHeadBefore;
    uintptr_t freeHeadAfter;
    uint32_t elementSize;
    uint32_t elementsPerSlab;
    uint32_t used;
    uint32_t unreleased;
    uint32_t slabSize;
    uint32_t traversedCount;
    uint32_t orderHashBefore;
    uint32_t orderHashAfter;
    uintptr_t topBefore[16];
    uintptr_t topAfter[16];
};

// CoreInteraction objects are pooled and therefore cannot be identified by
// their own address across rewind.  The concrete type and the two ElementSim
// endpoints remain stable for the lifetime of the scene and form the narrow
// semantic identity needed to restore mDirtyInteractions order.
struct DirtyInteractionKey {
    uintptr_t elementLow;
    uintptr_t elementHigh;
    uintptr_t primaryVtable;
    uint32_t interactionType;
};

struct DirtyInteractionOrderReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t nphaseCore;
    uintptr_t set;
    uintptr_t entries;
    uintptr_t entriesNext;
    uintptr_t hash;
    uint32_t entriesCapacity;
    uint32_t hashSize;
    uint32_t count;
    uint32_t action;
    uint32_t captures;
    uint32_t restores;
    uint32_t orderHashBefore;
    uint32_t orderHashAfter;
    uint32_t installed;
    uint32_t armed;
    uint32_t restoreMode;
    uint32_t matchedCount;
    uint32_t capturedOnlyCount;
    uint32_t liveOnlyCount;
};

struct RigidPose {
    float position[3];
    float rotation[4];
};

// PhysX PxTransform stores PxQuat q before PxVec3 p. RigidPose deliberately
// retains the managed/export ABI above, so convert explicitly at the callsite.
struct PhysxTransform {
    float rotation[4];
    float position[3];
};
static_assert(sizeof(PhysxTransform) == sizeof(RigidPose),
    "Unexpected PhysX transform size");

struct PhysxVec3 {
    float x;
    float y;
    float z;
};
static_assert(sizeof(PhysxVec3) == 12, "Unexpected PhysX vector size");

// PxGeometry stores its PxGeometryType::Enum first. Sphere, capsule and box
// append one, two and three floats respectively. Retain a fixed-size copy so
// the managed checkpoint ABI is independent of the concrete analytic type.
struct ShapeGeometry {
    uint32_t type;
    float values[3];
};
static_assert(sizeof(ShapeGeometry) == 16, "Unexpected shape geometry size");

struct SetGlobalPoseReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    RigidPose pose;
};

// The public PxRigidDynamic pose is a rounded composition of Scb::Body's
// body2World and body2Actor transforms.  Preserve both operands so rewind can
// reconstruct a checkpoint that is not necessarily in setGlobalPose's
// float32 image.
struct Body2WorldCaptureReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    uintptr_t scene;
    uint32_t controlState;
    uint32_t bodyBufferFlags;
    uint32_t simulationRunning;
    uint32_t physicsBuffering;
    RigidPose actorPose;
    RigidPose body2Actor;
    RigidPose bufferedBody2World;
    RigidPose coreBody2World;
    uintptr_t bodySim;
    uint32_t wakeCounterBufferedBits;
    uint32_t wakeCounterCoreBits;
    uint32_t bufferedIsSleeping;
    uint32_t bodySimActive;
    uintptr_t bodyCore;
    uintptr_t bodyCoreBodySim;
    uint32_t bodyCoreFlags;
    uintptr_t simStateData;
    uint32_t simStateTargetValid;
    uintptr_t interactionScene;
    uintptr_t scScene;
    uint32_t sceneArrayIndex;
    uint32_t bodySimInternalFlags;
    uint32_t velocityModState;
    uint32_t islandHook;
    uintptr_t activeBodiesData;
    uint32_t activeBodiesCount;
    uint32_t activeBodiesCapacity;
    uint32_t activeTwoWayStart;
    uintptr_t activeBodyAtSceneIndex;
    uint32_t activeBodiesHash;
    uintptr_t islandManager;
    uintptr_t islandNodeData;
    uintptr_t islandNodeOwner;
    uint32_t islandNodeIslandId;
    uint32_t islandNodeFlags;
    uintptr_t kinematicBitmap;
    uintptr_t kinematicChangeBitmap;
    uintptr_t notReadyBitmap;
    uintptr_t notReadyChangeBitmap;
    uintptr_t kinematicBitmapMap;
    uintptr_t kinematicChangeBitmapMap;
    uintptr_t notReadyBitmapMap;
    uintptr_t notReadyChangeBitmapMap;
    uint32_t kinematicBitmapWordCount;
    uint32_t kinematicChangeBitmapWordCount;
    uint32_t notReadyBitmapWordCount;
    uint32_t notReadyChangeBitmapWordCount;
    uint32_t kinematicBitmapWord;
    uint32_t kinematicChangeBitmapWord;
    uint32_t notReadyBitmapWord;
    uint32_t notReadyChangeBitmapWord;
    uint32_t kinematicBitmapBit;
    uint32_t kinematicChangeBitmapBit;
    uint32_t notReadyBitmapBit;
    uint32_t notReadyChangeBitmapBit;
    uint32_t islandManagerFlags;
    uintptr_t sleepBodiesData;
    uint32_t sleepBodiesCount;
    uint32_t sleepBodiesCapacity;
    uint32_t sleepBodiesHash;
    uint32_t sleepBodiesIndex;
    uintptr_t wokeBodiesData;
    uint32_t wokeBodiesCount;
    uint32_t wokeBodiesCapacity;
    uint32_t wokeBodiesHash;
    uint32_t wokeBodiesIndex;
    uint32_t wokeBodyListValid;
    uint32_t sleepBodyListValid;
    uint32_t lifecycleStable;
};

struct Body2WorldRestoreReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    uintptr_t sceneBefore;
    uintptr_t sceneAfter;
    uintptr_t apiScene;
    uint32_t dynamicTimestampBefore;
    uint32_t dynamicTimestampAfter;
    uint32_t controlStateBefore;
    uint32_t controlStateAfter;
    uint32_t bodyBufferFlagsBefore;
    uint32_t bodyBufferFlagsAfter;
    uint32_t simulationRunningBefore;
    uint32_t simulationRunningAfter;
    uint32_t physicsBufferingBefore;
    uint32_t physicsBufferingAfter;
    uint32_t changed;
    RigidPose actorPoseBefore;
    RigidPose actorPoseTarget;
    RigidPose actorPoseAfter;
    RigidPose body2ActorBefore;
    RigidPose body2ActorAfter;
    RigidPose bufferedBody2WorldBefore;
    RigidPose coreBody2WorldBefore;
    RigidPose body2WorldTarget;
    RigidPose bufferedBody2WorldAfter;
    RigidPose coreBody2WorldAfter;
    uintptr_t bodySimBefore;
    uintptr_t bodySimAfter;
    uint32_t wakeCounterBufferedBitsBefore;
    uint32_t wakeCounterBufferedBitsAfter;
    uint32_t wakeCounterCoreBitsBefore;
    uint32_t wakeCounterCoreBitsAfter;
    uint32_t bufferedIsSleepingBefore;
    uint32_t bufferedIsSleepingAfter;
    uint32_t bodySimActiveBefore;
    uint32_t bodySimActiveAfter;
};

struct WakeStateRestoreReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    uintptr_t bodySim;
    uint32_t targetWakeCounterBits;
    uint32_t targetSleeping;
    uint32_t wakeCounterBufferedBitsBefore;
    uint32_t wakeCounterCoreBitsBefore;
    uint32_t bufferedIsSleepingBefore;
    uint32_t bodySimActiveBefore;
    uint32_t wakeCounterBufferedBitsAfter;
    uint32_t wakeCounterCoreBitsAfter;
    uint32_t bufferedIsSleepingAfter;
    uint32_t bodySimActiveAfter;
    uint32_t callMask;
};
static_assert(sizeof(uintptr_t) == 4, "Native rewind helper requires the x86 Unity ABI");
static_assert(sizeof(Body2WorldCaptureReceipt) == 404,
    "Unexpected body2World capture receipt ABI");
static_assert(sizeof(Body2WorldRestoreReceipt) == 404,
    "Unexpected body2World restore receipt ABI");
static_assert(sizeof(WakeStateRestoreReceipt) == 76,
    "Unexpected wake-state restore receipt ABI");

struct RigidMassFrame {
    float centerOfMass[3];
    float inertiaTensorRotation[4];
    float inertiaTensor[3];
};

struct SetMassFrameReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    uint32_t automaticInertiaBefore;
    uint32_t automaticCenterBefore;
    uint32_t automaticInertiaAfter;
    uint32_t automaticCenterAfter;
    RigidMassFrame frame;
};

struct ShapePoseRestoreReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    uint32_t count;
    uint32_t poseChanged;
    uint32_t geometryChanged;
    uint32_t expectedOrderHash;
    uint32_t observedOrderHash;
};

struct KinematicTargetReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    uint32_t unityIsKinematic;
    uint32_t publicTargetValid;
    uint32_t scbBodyBufferFlags;
    uint32_t bufferedTargetValid;
    uintptr_t simStateData;
    uint32_t simStateIsKinematic;
    uint32_t coreTargetValid;
    RigidPose target;
};

struct InvalidateKinematicTargetReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t rigidbody;
    uintptr_t actor;
    uintptr_t bodyCore;
    uintptr_t simStateData;
    uint32_t unityIsKinematic;
    uint32_t simStateIsKinematic;
    uint32_t targetValidBefore;
    uint32_t targetValidAfter;
};
#pragma pack(pop)

static_assert(sizeof(ManifoldPoolReceipt) == 200,
    "Unexpected Win32 manifold-pool receipt ABI");
static_assert(sizeof(DirtyInteractionKey) == 16,
    "Unexpected Win32 dirty-interaction key ABI");
static_assert(sizeof(DirtyInteractionOrderReceipt) == 96,
    "Unexpected Win32 dirty-interaction receipt ABI");
static_assert(sizeof(InvalidateKinematicTargetReceipt) == 52,
    "Unexpected Win32 kinematic-target invalidation receipt ABI");

enum RebuildResult : uint32_t {
    RebuildOk = 1,
    RebuildBadArgument = 2,
    RebuildUnreadableRigidbody = 3,
    RebuildCleanupRevisionMismatch = 4,
    RebuildCreateRevisionMismatch = 5,
    RebuildMissingActor = 6,
    RebuildInactiveCreateMissingActor = 7,
    RebuildInactiveFlagMismatch = 8,
    RebuildActiveCreateMissingActor = 9,
    RebuildActiveFlagMismatch = 10
};

enum ContactPoolResult : uint32_t {
    ContactPoolOk = 1,
    ContactPoolBadArgument = 2,
    ContactPoolUnreadableContext = 3,
    ContactPoolInvalidCount = 4,
    ContactPoolUnreadableArray = 5,
    ContactPoolInvalidEntry = 6,
    ContactPoolNoSnapshot = 7,
    ContactPoolContextChanged = 8,
    ContactPoolArrayChanged = 9,
    ContactPoolCountChanged = 10,
    ContactPoolMembershipChanged = 11,
    ContactPoolArrayNotWritable = 12,
    ContactPoolWriteVerificationFailed = 13
};

enum ContactContextObserverResult : uint32_t {
    ContactContextObserverOk = 1,
    ContactContextObserverBadArgument = 2,
    ContactContextObserverRevisionMismatch = 3,
    ContactContextObserverAlreadyInstalled = 4,
    ContactContextObserverNotInstalled = 5,
    ContactContextObserverAllocationFailed = 6,
    ContactContextObserverProtectFailed = 7,
    ContactContextObserverPatchChanged = 8
};

enum ManifoldPoolResult : uint32_t {
    ManifoldPoolOk = 1,
    ManifoldPoolBadArgument = 2,
    ManifoldPoolRevisionMismatch = 3,
    ManifoldPoolInvalidKind = 4,
    ManifoldPoolUnreadableContext = 5,
    ManifoldPoolUnreadablePool = 6,
    ManifoldPoolInvalidMetadata = 7,
    ManifoldPoolInvalidCount = 8,
    ManifoldPoolInvalidNode = 9,
    ManifoldPoolDuplicateNode = 10,
    ManifoldPoolCapacityTooSmall = 11,
    ManifoldPoolBufferNotWritable = 12,
    ManifoldPoolBufferUnreadable = 13,
    ManifoldPoolIdentityChanged = 14,
    ManifoldPoolCountChanged = 15,
    ManifoldPoolMembershipChanged = 16,
    ManifoldPoolHeadNotWritable = 17,
    ManifoldPoolNodeNotWritable = 18,
    ManifoldPoolWriteVerificationFailed = 19
};

enum DirtyInteractionOrderResult : uint32_t {
    DirtyInteractionOrderOk = 1,
    DirtyInteractionOrderBadArgument = 2,
    DirtyInteractionOrderRevisionMismatch = 3,
    DirtyInteractionOrderAlreadyInstalled = 4,
    DirtyInteractionOrderNotInstalled = 5,
    DirtyInteractionOrderAllocationFailed = 6,
    DirtyInteractionOrderProtectFailed = 7,
    DirtyInteractionOrderPatchChanged = 8,
    DirtyInteractionOrderAlreadyArmed = 9,
    DirtyInteractionOrderNotCaptured = 10,
    DirtyInteractionOrderCapacityTooSmall = 11,
    DirtyInteractionOrderInvalidHeader = 12,
    DirtyInteractionOrderInvalidEntry = 13,
    DirtyInteractionOrderMembershipChanged = 14,
    DirtyInteractionOrderIdentityChanged = 15,
    DirtyInteractionOrderNotWritable = 16,
    DirtyInteractionOrderWriteVerificationFailed = 17,
    DirtyInteractionOrderPending = 18
};

enum DirtyInteractionOrderAction : uint32_t {
    DirtyInteractionOrderIdle = 0,
    DirtyInteractionOrderCapture = 1,
    DirtyInteractionOrderRestore = 2
};

enum DirtyInteractionRestoreMode : uint32_t {
    DirtyInteractionRestoreNone = 0,
    DirtyInteractionRestoreExact = 1,
    DirtyInteractionRestoreProjection = 2
};

enum SetGlobalPoseResult : uint32_t {
    SetGlobalPoseOk = 1,
    SetGlobalPoseBadArgument = 2,
    SetGlobalPoseUnreadableRigidbody = 3,
    SetGlobalPoseMissingActor = 4,
    SetGlobalPoseRevisionMismatch = 5,
    SetGlobalPoseNonfinite = 6
};

enum Body2WorldResult : uint32_t {
    Body2WorldOk = 1,
    Body2WorldBadArgument = 2,
    Body2WorldUnreadableRigidbody = 3,
    Body2WorldMissingActor = 4,
    Body2WorldRevisionMismatch = 5,
    Body2WorldBuffered = 6,
    Body2WorldNonfinite = 7,
    Body2WorldMassFrameChanged = 8,
    Body2WorldReadbackChanged = 9,
    Body2WorldCoreMismatch = 10,
    Body2WorldNotInScene = 11,
    Body2WorldMissingApiScene = 12,
    Body2WorldPreimageMismatch = 13,
    Body2WorldSleepStateMismatch = 14,
    Body2WorldLifecycleUnreadable = 15,
    Body2WorldLifecycleUnstable = 16
};

enum WakeStateResult : uint32_t {
    WakeStateOk = 1,
    WakeStateBadArgument = 2,
    WakeStateBodyStateInvalid = 3,
    WakeStateRevisionMismatch = 4,
    WakeStateInvalidTarget = 5,
    WakeStateSleepingTransitionUnsupported = 6,
    WakeStateReadbackChanged = 7
};

enum SetMassFrameResult : uint32_t {
    SetMassFrameOk = 1,
    SetMassFrameBadArgument = 2,
    SetMassFrameUnreadableRigidbody = 3,
    SetMassFrameMissingActor = 4,
    SetMassFrameCenterRevisionMismatch = 5,
    SetMassFrameInertiaRevisionMismatch = 6,
    SetMassFrameNonfinite = 7,
    SetMassFrameInvalidInertia = 8,
    SetMassFrameInvalidRotation = 9,
    SetMassFrameNotAutomaticBefore = 10,
    SetMassFrameNotAutomaticAfter = 11
};

enum ShapePoseRestoreResult : uint32_t {
    ShapePoseRestoreOk = 1,
    ShapePoseRestoreBadArgument = 2,
    ShapePoseRestoreUnreadableRigidbody = 3,
    ShapePoseRestoreMissingActor = 4,
    ShapePoseRestoreGetShapesRevisionMismatch = 5,
    ShapePoseRestoreGetPoseRevisionMismatch = 6,
    ShapePoseRestoreSetPoseRevisionMismatch = 7,
    ShapePoseRestoreInvalidCount = 8,
    ShapePoseRestoreMembershipChanged = 9,
    ShapePoseRestoreNonfinite = 10,
    ShapePoseRestoreReadbackChanged = 11,
    ShapePoseRestoreGetGeometryRevisionMismatch = 12,
    ShapePoseRestoreSetGeometryRevisionMismatch = 13,
    ShapePoseRestoreGeometryTypeChanged = 14,
    ShapePoseRestoreGeometryReadbackChanged = 15
};

enum KinematicTargetResult : uint32_t {
    KinematicTargetOk = 1,
    KinematicTargetBadArgument = 2,
    KinematicTargetUnreadableRigidbody = 3,
    KinematicTargetMissingActor = 4,
    KinematicTargetRevisionMismatch = 5,
    KinematicTargetUnreadableState = 6,
    KinematicTargetNonfinite = 7,
    KinematicTargetNotKinematic = 8,
    KinematicTargetAlreadyValid = 9,
    KinematicTargetBodyStateInvalid = 10,
    KinematicTargetReadbackChanged = 11
};

enum InvalidateKinematicTargetResult : uint32_t {
    InvalidateKinematicTargetOk = 1,
    InvalidateKinematicTargetBadArgument = 2,
    InvalidateKinematicTargetUnreadableRigidbody = 3,
    InvalidateKinematicTargetMissingActor = 4,
    InvalidateKinematicTargetRevisionMismatch = 5,
    InvalidateKinematicTargetNotKinematic = 6,
    InvalidateKinematicTargetUnreadableState = 7,
    InvalidateKinematicTargetNotValid = 8,
    InvalidateKinematicTargetReadbackChanged = 9
};

static const uint32_t kApiVersion = 11;
static const uint32_t kMaximumShapePoses = 64;
static const uint32_t kMaximumContactManagers = 4096;
static const uint32_t kMaximumManifolds = 4096;
static const uint32_t kMaximumDirtyInteractions = 4096;
static const uint32_t kMaximumDirtyHashSize = 8192;
static const uint32_t kCleanupRva = 0x481ED0;
static const uint32_t kCreateRva = 0x482510;
static const uint32_t kGetShapesRva = 0xA10740;
static const uint32_t kCreateContactManagerRva = 0xA69E80;
static const uint32_t kUpdateDirtyInteractionsRva = 0xA540F0;
static const uint32_t kLargeManifoldPoolRva = 0xA69A90;
static const uint32_t kSphereManifoldPoolRva = 0xA69AC0;
static const uint32_t kLargeManifoldPoolSlabRva = 0xA69BEA;
static const uint32_t kSphereManifoldPoolSlabRva = 0xA69CCA;
static const uint32_t kLargeManifoldCallsiteRva = 0xA69F15;
static const uint32_t kSphereManifoldCallsiteRva = 0xA69F32;
static const uint32_t kNpSetGlobalPoseRva = 0xA143D0;
static const uint32_t kNpGetGlobalPoseRva = 0xA12090;
static const uint32_t kScbBodySetBody2WorldRva = 0xA136D0;
static const uint32_t kScBodyCoreSetBody2WorldRva = 0xA3A9B0;
static const uint32_t kNpActorGetApiSceneRva = 0xA257E0;
static const uint32_t kNpShapeManagerMarkSceneQueryRva = 0xA266E0;
static const uint32_t kNpRigidDynamicVtableRva = 0xEFA6EC;
static const uint32_t kNpSetWakeCounterRva = 0xA15760;
static const uint32_t kNpWakeUpRva = 0xA15E80;
static const uint32_t kNpPutToSleepRva = 0xA12E80;
static const uint32_t kNpGetKinematicTargetRva = 0xA12530;
static const uint32_t kNpSetKinematicTargetRva = 0xA149E0;
static const uint32_t kScBodyCoreInvalidateKinematicTargetRva = 0xA3A5A0;
static const uint32_t kNpSetCMassLocalPoseInternalRva = 0xA13E90;
static const uint32_t kNpSetMassSpaceInertiaTensorRva = 0xA14E80;
static const uint32_t kNpShapeGetLocalPoseRva = 0xA0D2A0;
static const uint32_t kNpShapeSetLocalPoseRva = 0xA0E4C0;
static const uint32_t kNpShapeGetGeometryTypeRva = 0x842360;
static const uint32_t kNpShapeGetBoxGeometryRva = 0xA0D0B0;
static const uint32_t kNpShapeGetCapsuleGeometryRva = 0xA0D0F0;
static const uint32_t kNpShapeGetSphereGeometryRva = 0xA0DC50;
static const uint32_t kNpShapeSetGeometryRva = 0xA0E340;
static const uint32_t kNpShapeVtableRva = 0xEF9A04;
static const uint8_t kCleanupBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x74,0x53,0x8B,0xD9,0x56,0x57};
static const uint8_t kCreateBytes[] = {0x55,0x8B,0xEC,0x81,0xEC,0x90,0x00,0x00,0x00,0x53,0x8B,0xD9};
static const uint8_t kGetShapesBytes[] = {0x55,0x8B,0xEC,0x83,0xC1,0x14,0x5D,0xE9};
static const uint8_t kCreateContactManagerBytes[] = {0x55,0x8B,0xEC,0x53,0x8B,0xD9};
static const uint8_t kUpdateDirtyInteractionsBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x34};
// These exact UnityPlayer 2017.4.8f1 Win32 instructions prove both the
// PxsContext member offsets and the intrusive Ps::Pool bookkeeping layout.
// In particular, allocate() pops mFreeElement at +0x124 while updating used
// and unreleased at +0x118/+0x11c. The slow paths prove elementsPerSlab at
// +0x114 and the concrete 0xf0/0x60 element strides.
static const uint8_t kLargeManifoldPoolBytes[] = {
    0x56,0x8B,0xF1,0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x05,
    0xE8,0xAF,0x00,0x00,0x00,0x8B,0x86,0x24,0x01,0x00,0x00,0x8B,
    0x08,0xFF,0x86,0x18,0x01,0x00,0x00,0xFF,0x8E,0x1C,0x01,0x00,
    0x00,0x89,0x8E,0x24,0x01,0x00,0x00,0x5E,0xC3
};
static const uint8_t kSphereManifoldPoolBytes[] = {
    0x56,0x8B,0xF1,0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x05,
    0xE8,0x5F,0x01,0x00,0x00,0x8B,0x86,0x24,0x01,0x00,0x00,0x8B,
    0x08,0xFF,0x86,0x18,0x01,0x00,0x00,0xFF,0x8E,0x1C,0x01,0x00,
    0x00,0x89,0x8E,0x24,0x01,0x00,0x00,0x5E,0xC3
};
static const uint8_t kLargeManifoldPoolSlabBytes[] = {
    0x69,0x8E,0x14,0x01,0x00,0x00,0xF0,0x00,0x00,0x00,0x81,0xC1,
    0x10,0xFF,0xFF,0xFF,0x03,0xCF,0x3B,0xCF,0x72,0x1E,0x8B,0x86,
    0x24,0x01,0x00,0x00,0x89,0x01,0xFF,0x86,0x1C,0x01,0x00,0x00,
    0x89,0x8E,0x24,0x01,0x00,0x00,0x81,0xE9,0xF0,0x00,0x00,0x00,
    0x3B,0xCF,0x73,0xE2
};
static const uint8_t kSphereManifoldPoolSlabBytes[] = {
    0x8B,0x86,0x14,0x01,0x00,0x00,0x8D,0x0C,0x40,0xC1,0xE1,0x05,
    0x83,0xC1,0xA0,0x03,0xCF,0x3B,0xCF,0x72,0x1C,0x90,0x8B,0x86,
    0x24,0x01,0x00,0x00,0x89,0x01,0xFF,0x86,0x1C,0x01,0x00,0x00,
    0x89,0x8E,0x24,0x01,0x00,0x00,0x83,0xE9,0x60,0x3B,0xCF,0x73,
    0xE5
};
static const uint8_t kLargeManifoldCallsiteBytes[] = {
    0x8D,0x8B,0xE4,0x02,0x00,0x00,0xE8,0x70,0xFB,0xFF,0xFF
};
static const uint8_t kSphereManifoldCallsiteBytes[] = {
    0x8D,0x8B,0x0C,0x04,0x00,0x00,0xE8,0x83,0xFB,0xFF,0xFF
};
static const uint8_t kNpSetGlobalPoseBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x44};
static const uint8_t kNpGetGlobalPoseBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x24,0xF7,0x81,
    0x1C,0x01,0x00,0x00,0x00,0x02,0x00,0x00
};
static const uint8_t kScbBodySetBody2WorldBytes[] = {
    0x55,0x8B,0xEC,0x56,0x8B,0xF1,0x8B,0x4D,0x08,0x8B,0x01,0x89,
    0x86,0xB0,0x00,0x00,0x00
};
// NpRigidDynamic::setGlobalPose must continue to pass actor+0x30 to the
// Scb::Body routine with asPartOfBody2ActorChange=false.
static const uint8_t kNpSetGlobalPoseScbThisBytes[] = {
    0x8D,0x4F,0x30,0x6A,0x00
};
static const uint8_t kNpSetGlobalPoseScbCallBytes[] = {
    0xE8,0x8D,0xEF,0xFF,0xFF
};
static const uint8_t kNpSetGlobalPoseBody2ActorSelectionBytes[] = {
    0xF7,0x87,0x1C,0x01,0x00,0x00,0x00,0x02,0x00,0x00,0x74,0x0A,
    0x8B,0x47,0x38,0x05,0x90,0x00,0x00,0x00,0xEB,0x03,0x8D,0x47,0x70
};
static const uint8_t kNpSetGlobalPoseScenePreludeBytes[] = {
    0x57,0xE8,0xFF,0x13,0x01,0x00,0x8B,0x55,0x08,0x8B,0xD8
};
static const uint8_t kNpSetGlobalPoseSceneUpdateBytes[] = {
    0x85,0xDB,0x74,0x2E,0x8D,0xB3,0x40,0x0D,0x00,0x00,0x56,
    0x8D,0x4F,0x14,0xE8,0x51,0x22,0x01,0x00,0xFF,0x46,0x18
};
static const uint8_t kNpActorGetApiSceneBytes[] = {
    0x55,0x8B,0xEC,0x8B,0x4D,0x08,0x0F,0xB7,0x41,0x04
};
static const uint8_t kNpShapeManagerMarkSceneQueryBytes[] = {
    0x55,0x8B,0xEC,0x66,0x83,0x79,0x0C,0x01,0x53,0x0F,0xB7,0x59,0x04
};
static const uint8_t kScbBodySetBody2WorldLastCopyBytes[] = {
    0x8B,0x41,0x18,0x89,0x86,0xC8,0x00,0x00,0x00
};
static const uint8_t kScbBodySetBody2WorldCorePathBytes[] = {
    0x8B,0x46,0x04,0xC1,0xE8,0x1E,0x83,0xF8,0x03,0x74,0x1E,0x83,
    0xF8,0x02,0x75,0x0B,0x8B,0x06,0x80,0xB8,0x81,0x09,0x00,0x00,
    0x00,0x75,0x0E,0x51,0x8D,0x4E,0x10,0xE8,0x75,0x72,0x02,0x00,
    0x5E,0x5D,0xC2,0x08,0x00
};
static const uint8_t kScBodyCoreSetBody2WorldBytes[] = {
    0x55,0x8B,0xEC,0x8B,0x55,0x08,0x8B,0x02,0x89,0x41,0x10,0x8B,
    0x42,0x04,0x89,0x41,0x14,0x8B,0x42,0x08,0x89,0x41,0x18,0x8B,
    0x42,0x0C,0x89,0x41,0x1C,0x8B,0x42,0x10,0x89,0x41,0x20,0x8B,
    0x42,0x14,0x89,0x41,0x24,0x8B,0x42,0x18,0x89,0x41,0x28,0x8B,
    0x49,0x04,0x85,0xC9,0x74,0x05,0xE8,0xC5,0xEC,0x00,0x00,0x5D,
    0xC2,0x04,0x00
};
static const uint8_t kNpSetWakeCounterBytes[] = {
    0x55,0x8B,0xEC,0xF3,0x0F,0x10,0x45,0x08,0x51,0x83,0xC1,0x30,
    0xF3,0x0F,0x11,0x04,0x24,0xE8,0x5A,0xFF,0xFF,0xFF,0x5D,0xC2,0x04,0x00
};
static const uint8_t kNpWakeUpBytes[] = {
    0x8B,0x41,0x30,0x83,0xC1,0x30,0x51,0xF3,0x0F,0x10,0x80,0x2C,
    0x0B,0x00,0x00,0xF3,0x0F,0x11,0x04,0x24,0xE8,0x07,0x00,0x00,0x00,0xC3
};
static const uint8_t kNpPutToSleepBytes[] = {
    0x83,0xC1,0x30,0xE9,0x08,0x00,0x00,0x00
};
static const uint8_t kNpGetKinematicTargetBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x44,0xF7,0x81,0x1C,0x01,0x00,0x00,0x00,0x10,0x00,0x00};
static const uint8_t kNpSetKinematicTargetBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x38};
static const uint8_t kScBodyCoreInvalidateKinematicTargetBytes[] = {
    0x8B,0x81,0x9C,0x00,0x00,0x00,0xC6,0x40,0x1C,0x00,0xC3
};
static const uint8_t kNpSetCMassLocalPoseInternalBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x48,0x53,0x8B,0xD9};
static const uint8_t kNpSetMassSpaceInertiaTensorBytes[] = {0x55,0x8B,0xEC,0x8B,0x55,0x08,0x83,0xEC,0x0C};
static const uint8_t kNpShapeGetLocalPoseBytes[] = {0x55,0x8B,0xEC,0xF6,0x41,0x24,0x04,0x56};
static const uint8_t kNpShapeSetLocalPoseBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x1C,0x8B,0x45,0x08};
static const uint8_t kNpShapeGetGeometryTypeBytes[] = {0x8B,0x41,0x74,0xC3};
static const uint8_t kNpShapeGetBoxGeometryBytes[] = {0x55,0x8B,0xEC,0x83,0x79,0x74,0x03,0x74,0x06,0x32,0xC0,0x5D,0xC2,0x04,0x00,0xF6};
static const uint8_t kNpShapeGetCapsuleGeometryBytes[] = {0x55,0x8B,0xEC,0x83,0x79,0x74,0x02};
static const uint8_t kNpShapeGetSphereGeometryBytes[] = {0x55,0x8B,0xEC,0x83,0x79,0x74,0x00};
static const uint8_t kNpShapeSetGeometryBytes[] = {0x55,0x8B,0xEC,0x53,0x8B,0x5D,0x08,0x56,0x8B,0xF1,0x8B,0x03,0x3B,0x46,0x74,0x74,0x25};

typedef void (__thiscall *RigidbodyInternal)(void* self, bool active);
typedef uint32_t (__thiscall *RigidActorGetShapes)(void* self, void** shapes,
    uint32_t capacity, uint32_t startIndex);
typedef void (__thiscall *NpRigidDynamicSetGlobalPose)(void* self,
    const PhysxTransform& pose, bool autowake);
typedef PhysxTransform* (__thiscall *NpRigidDynamicGetGlobalPose)(void* self,
    PhysxTransform* pose);
typedef void (__thiscall *ScbBodySetBody2World)(void* self,
    const PhysxTransform& pose, bool asPartOfBody2ActorChange);
typedef void (__thiscall *NpRigidDynamicSetWakeCounter)(void* self,
    float wakeCounter);
typedef void (__thiscall *NpRigidDynamicWakeUp)(void* self);
typedef void* (__cdecl *NpActorGetApiScene)(void* actor);
typedef void (__thiscall *NpShapeManagerMarkSceneQuery)(void* shapeManager,
    void* sceneQueryManager);
typedef bool (__thiscall *NpRigidDynamicGetKinematicTarget)(void* self,
    PhysxTransform& pose);
typedef void (__thiscall *NpRigidDynamicSetKinematicTarget)(void* self,
    const PhysxTransform& pose);
typedef void (__thiscall *ScBodyCoreInvalidateKinematicTarget)(void* self);
typedef void (__thiscall *NpRigidBodySetCMassLocalPoseInternal)(void* self,
    const PhysxTransform& pose);
typedef void (__thiscall *NpRigidBodySetMassSpaceInertiaTensor)(void* self,
    const PhysxVec3& inertia);
typedef void (__thiscall *NpShapeGetLocalPose)(void* self, PhysxTransform* pose);
typedef void (__thiscall *NpShapeSetLocalPose)(void* self, const PhysxTransform& pose);
typedef uint32_t (__thiscall *NpShapeGetGeometryType)(void* self);
typedef uint8_t (__thiscall *NpShapeGetGeometry)(void* self, ShapeGeometry* geometry);
typedef void (__thiscall *NpShapeSetGeometry)(void* self, const ShapeGeometry& geometry);

static uintptr_t g_contactPoolSnapshot[kMaximumContactManagers] = {};
static uintptr_t g_contactPoolContext = 0;
static uintptr_t g_contactPoolFreeArray = 0;
static uint32_t g_contactPoolFreeCount = 0;
static uint32_t g_contactPoolOrderHash = 0;
static bool g_contactPoolCaptured = false;
static uintptr_t g_observerUnityBase = 0;
static volatile LONG g_observedContactManagerContext = 0;
static volatile LONG g_contactManagerContextObservations = 0;
static void* g_contactManagerContextTrampoline = 0;
static uint8_t g_contactManagerContextOriginal[sizeof(kCreateContactManagerBytes)] = {};
static bool g_contactManagerContextObserverInstalled = false;
static uintptr_t g_dirtyInteractionUnityBase = 0;
static void* g_dirtyInteractionTrampoline = 0;
static uint8_t g_dirtyInteractionOriginal[sizeof(kUpdateDirtyInteractionsBytes)] = {};
static volatile LONG g_dirtyInteractionAction = DirtyInteractionOrderIdle;
static volatile LONG g_dirtyInteractionResult = DirtyInteractionOrderNotCaptured;
static volatile LONG g_dirtyInteractionError = ERROR_INVALID_STATE;
static volatile LONG g_dirtyInteractionCaptures = 0;
static volatile LONG g_dirtyInteractionRestores = 0;
static bool g_dirtyInteractionInstalled = false;
static uintptr_t g_dirtyInteractionNPhaseCore = 0;
static uintptr_t g_dirtyInteractionSet = 0;
static uintptr_t g_dirtyInteractionEntries = 0;
static uintptr_t g_dirtyInteractionEntriesNext = 0;
static uintptr_t g_dirtyInteractionHash = 0;
static uint32_t g_dirtyInteractionEntriesCapacity = 0;
static uint32_t g_dirtyInteractionHashSize = 0;
static uint32_t g_dirtyInteractionCount = 0;
static uint32_t g_dirtyInteractionOrderHashBefore = 0;
static uint32_t g_dirtyInteractionOrderHashAfter = 0;
static uint32_t g_dirtyInteractionRestoreMode = DirtyInteractionRestoreNone;
static uint32_t g_dirtyInteractionMatchedCount = 0;
static uint32_t g_dirtyInteractionCapturedOnlyCount = 0;
static uint32_t g_dirtyInteractionLiveOnlyCount = 0;
static DirtyInteractionKey g_dirtyInteractionKeys[kMaximumDirtyInteractions] = {};
static DirtyInteractionKey g_dirtyInteractionReorderedKeys[kMaximumDirtyInteractions] = {};
static uintptr_t g_dirtyInteractionLive[kMaximumDirtyInteractions] = {};
static uintptr_t g_dirtyInteractionMatched[kMaximumDirtyInteractions] = {};
static uintptr_t g_dirtyInteractionReordered[kMaximumDirtyInteractions] = {};
static uint8_t g_dirtyInteractionUsed[kMaximumDirtyInteractions] = {};
static uint32_t g_dirtyInteractionOldNext[kMaximumDirtyInteractions] = {};
static uint32_t g_dirtyInteractionNewNext[kMaximumDirtyInteractions] = {};
static uint32_t g_dirtyInteractionOldHash[kMaximumDirtyHashSize] = {};
static uint32_t g_dirtyInteractionNewHash[kMaximumDirtyHashSize] = {};

static int Fail(RebuildReceipt* receipt, RebuildResult result, uint32_t error) {
    receipt->result = result;
    receipt->lastError = error;
    return 0;
}

static int FailContactPool(ContactPoolReceipt* receipt, ContactPoolResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailContactContextObserver(ContactContextObserverReceipt* receipt,
    ContactContextObserverResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailManifoldPool(ManifoldPoolReceipt* receipt,
    ManifoldPoolResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailDirtyInteractionOrder(DirtyInteractionOrderReceipt* receipt,
    DirtyInteractionOrderResult result, uint32_t error) {
    InterlockedExchange(&g_dirtyInteractionResult, result);
    InterlockedExchange(&g_dirtyInteractionError, static_cast<LONG>(error));
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailSetGlobalPose(SetGlobalPoseReceipt* receipt,
    SetGlobalPoseResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailBody2WorldCapture(Body2WorldCaptureReceipt* receipt,
    Body2WorldResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailBody2WorldRestore(Body2WorldRestoreReceipt* receipt,
    Body2WorldResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailWakeStateRestore(WakeStateRestoreReceipt* receipt,
    WakeStateResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailSetMassFrame(SetMassFrameReceipt* receipt,
    SetMassFrameResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailShapePoseRestore(ShapePoseRestoreReceipt* receipt,
    ShapePoseRestoreResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailKinematicTarget(KinematicTargetReceipt* receipt,
    KinematicTargetResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailInvalidateKinematicTarget(InvalidateKinematicTargetReceipt* receipt,
    InvalidateKinematicTargetResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    SetLastError(error);
    return 0;
}

static bool Finite(float value) {
    const uint32_t bits = *reinterpret_cast<const uint32_t*>(&value) & 0x7FFFFFFFu;
    return bits < 0x7F800000u;
}

static bool Finite(const RigidPose& pose) {
    for (uint32_t i = 0; i < 3; ++i) if (!Finite(pose.position[i])) return false;
    for (uint32_t i = 0; i < 4; ++i) if (!Finite(pose.rotation[i])) return false;
    return true;
}

static RigidPose ExportPose(const PhysxTransform& pose) {
    RigidPose result = {};
    for (uint32_t i = 0; i < 3; ++i) result.position[i] = pose.position[i];
    for (uint32_t i = 0; i < 4; ++i) result.rotation[i] = pose.rotation[i];
    return result;
}

static PhysxTransform ImportPose(const RigidPose& pose) {
    PhysxTransform result = {};
    for (uint32_t i = 0; i < 3; ++i) result.position[i] = pose.position[i];
    for (uint32_t i = 0; i < 4; ++i) result.rotation[i] = pose.rotation[i];
    return result;
}

struct BodyPoseState {
    uintptr_t actor;
    uintptr_t scene;
    uint32_t controlState;
    uint32_t bodyBufferFlags;
    uint32_t simulationRunning;
    uint32_t physicsBuffering;
    RigidPose actorPose;
    RigidPose body2Actor;
    RigidPose bufferedBody2World;
    RigidPose coreBody2World;
    uintptr_t bodySim;
    uint32_t wakeCounterBufferedBits;
    uint32_t wakeCounterCoreBits;
    uint32_t bufferedIsSleeping;
    uint32_t bodySimActive;
    uintptr_t bodyCore;
    uintptr_t bodyCoreBodySim;
    uint32_t bodyCoreFlags;
    uintptr_t simStateData;
    uint32_t simStateTargetValid;
    uintptr_t interactionScene;
    uintptr_t scScene;
    uint32_t sceneArrayIndex;
    uint32_t bodySimInternalFlags;
    uint32_t velocityModState;
    uint32_t islandHook;
    uintptr_t activeBodiesData;
    uint32_t activeBodiesCount;
    uint32_t activeBodiesCapacity;
    uint32_t activeTwoWayStart;
    uintptr_t activeBodyAtSceneIndex;
    uint32_t activeBodiesHash;
    uintptr_t islandManager;
    uintptr_t islandNodeData;
    uintptr_t islandNodeOwner;
    uint32_t islandNodeIslandId;
    uint32_t islandNodeFlags;
    uintptr_t kinematicBitmap;
    uintptr_t kinematicChangeBitmap;
    uintptr_t notReadyBitmap;
    uintptr_t notReadyChangeBitmap;
    uintptr_t kinematicBitmapMap;
    uintptr_t kinematicChangeBitmapMap;
    uintptr_t notReadyBitmapMap;
    uintptr_t notReadyChangeBitmapMap;
    uint32_t kinematicBitmapWordCount;
    uint32_t kinematicChangeBitmapWordCount;
    uint32_t notReadyBitmapWordCount;
    uint32_t notReadyChangeBitmapWordCount;
    uint32_t kinematicBitmapWord;
    uint32_t kinematicChangeBitmapWord;
    uint32_t notReadyBitmapWord;
    uint32_t notReadyChangeBitmapWord;
    uint32_t kinematicBitmapBit;
    uint32_t kinematicChangeBitmapBit;
    uint32_t notReadyBitmapBit;
    uint32_t notReadyChangeBitmapBit;
    uint32_t islandManagerFlags;
    uintptr_t sleepBodiesData;
    uint32_t sleepBodiesCount;
    uint32_t sleepBodiesCapacity;
    uint32_t sleepBodiesHash;
    uint32_t sleepBodiesIndex;
    uintptr_t wokeBodiesData;
    uint32_t wokeBodiesCount;
    uint32_t wokeBodiesCapacity;
    uint32_t wokeBodiesHash;
    uint32_t wokeBodiesIndex;
    uint32_t wokeBodyListValid;
    uint32_t sleepBodyListValid;
    uint32_t lifecycleStable;
};

static bool ReadPointerArray(uintptr_t data, uint32_t count,
    uint32_t rawCapacity, uint32_t* error) {
    const uint32_t capacity = rawCapacity & 0x7FFFFFFFu;
    if (count > capacity || count > 65536u) {
        *error = ERROR_INVALID_DATA;
        return false;
    }
    if (count && (!data || !Readable(reinterpret_cast<const void*>(data),
            count * sizeof(uintptr_t)))) {
        *error = ERROR_NOACCESS;
        return false;
    }
    return true;
}

static bool ReadBitmapWord(uintptr_t bitmap, uint32_t bit,
    uintptr_t* map, uint32_t* wordCount, uint32_t* word,
    uint32_t* bitValue, uint32_t* error) {
    *map = 0;
    *wordCount = 0;
    *word = 0;
    *bitValue = 0;
    if (!bitmap || !Readable(reinterpret_cast<const void*>(bitmap),
            sizeof(uintptr_t) + sizeof(uint32_t))) {
        *error = ERROR_NOACCESS;
        return false;
    }
    *map = *reinterpret_cast<const uintptr_t*>(bitmap);
    *wordCount =
        *reinterpret_cast<const uint32_t*>(bitmap + sizeof(uintptr_t)) &
        0x7FFFFFFFu;
    const uint32_t wordIndex = bit >> 5;
    if (!*map || wordIndex >= *wordCount || *wordCount > 65536u) {
        *error = ERROR_INVALID_DATA;
        return false;
    }
    const uintptr_t address = *map + wordIndex * sizeof(uint32_t);
    if (!Readable(reinterpret_cast<const void*>(address), sizeof(uint32_t))) {
        *error = ERROR_NOACCESS;
        return false;
    }
    *word = *reinterpret_cast<const uint32_t*>(address);
    *bitValue = (*word >> (bit & 31u)) & 1u;
    return true;
}

static Body2WorldResult CaptureBodyLifecycleState(BodyPoseState* state,
    uint32_t* error) {
    const uintptr_t bodySim = state->bodySim;
    if (!Readable(reinterpret_cast<const void*>(bodySim), 0xC0)) {
        *error = ERROR_NOACCESS;
        return Body2WorldLifecycleUnreadable;
    }
    state->interactionScene =
        *reinterpret_cast<const uintptr_t*>(bodySim + 0x24);
    state->sceneArrayIndex =
        *reinterpret_cast<const uint32_t*>(bodySim + 0x28);
    state->bodyCore =
        *reinterpret_cast<const uintptr_t*>(bodySim + 0x34);
    state->bodySimInternalFlags =
        *reinterpret_cast<const uint16_t*>(bodySim + 0x90);
    state->velocityModState =
        *reinterpret_cast<const uint8_t*>(bodySim + 0x92);
    state->islandHook =
        *reinterpret_cast<const uint32_t*>(bodySim + 0xBC);
    if (!state->bodyCore ||
        !Readable(reinterpret_cast<const void*>(state->bodyCore), 0xA0) ||
        !state->interactionScene ||
        !Readable(reinterpret_cast<const void*>(state->interactionScene), 0x3F4)) {
        *error = ERROR_NOACCESS;
        return Body2WorldLifecycleUnreadable;
    }
    state->bodyCoreBodySim =
        *reinterpret_cast<const uintptr_t*>(state->bodyCore + 0x04);
    state->bodyCoreFlags =
        *reinterpret_cast<const uint32_t*>(state->bodyCore + 0x2C);
    state->simStateData =
        *reinterpret_cast<const uintptr_t*>(state->bodyCore + 0x9C);
    if (state->bodyCoreBodySim != bodySim) {
        *error = ERROR_INVALID_STATE;
        return Body2WorldLifecycleUnreadable;
    }
    if (state->simStateData) {
        if (!Readable(reinterpret_cast<const void*>(state->simStateData), 0x20)) {
            *error = ERROR_NOACCESS;
            return Body2WorldLifecycleUnreadable;
        }
        state->simStateTargetValid =
            *reinterpret_cast<const uint8_t*>(state->simStateData + 0x1C) != 0 ? 1u : 0u;
    }

    state->activeBodiesData =
        *reinterpret_cast<const uintptr_t*>(state->interactionScene + 0x00);
    state->activeBodiesCount =
        *reinterpret_cast<const uint32_t*>(state->interactionScene + 0x04);
    state->activeBodiesCapacity =
        *reinterpret_cast<const uint32_t*>(state->interactionScene + 0x08);
    state->activeTwoWayStart =
        *reinterpret_cast<const uint32_t*>(state->interactionScene + 0x0C);
    state->scScene =
        *reinterpret_cast<const uintptr_t*>(state->interactionScene + 0x3F0);
    if (!ReadPointerArray(state->activeBodiesData, state->activeBodiesCount,
            state->activeBodiesCapacity, error) ||
        state->activeTwoWayStart > state->activeBodiesCount || !state->scScene ||
        !Readable(reinterpret_cast<const void*>(state->scScene + 0x464), 0x1A))
        return Body2WorldLifecycleUnreadable;
    const uintptr_t* activeBodies =
        reinterpret_cast<const uintptr_t*>(state->activeBodiesData);
    state->activeBodiesHash = OrderHash(activeBodies, state->activeBodiesCount);
    state->activeBodyAtSceneIndex =
        state->sceneArrayIndex < state->activeBodiesCount ?
        activeBodies[state->sceneArrayIndex] : 0;

    const uintptr_t context =
        *reinterpret_cast<const uintptr_t*>(state->interactionScene + 0x3E8);
    if (!context || !Readable(reinterpret_cast<const void*>(context + 0x181C), 0x1DF)) {
        *error = ERROR_NOACCESS;
        return Body2WorldLifecycleUnreadable;
    }
    state->islandManager = context + 0x181C;
    state->islandNodeData =
        *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x10);
    if (!state->islandNodeData || state->islandHook >= 0x100000u ||
        !Readable(reinterpret_cast<const void*>(state->islandNodeData +
            state->islandHook * 12u), 12)) {
        *error = ERROR_NOACCESS;
        return Body2WorldLifecycleUnreadable;
    }
    const uintptr_t islandNode = state->islandNodeData + state->islandHook * 12u;
    state->islandNodeOwner =
        *reinterpret_cast<const uintptr_t*>(islandNode + 0x00);
    state->islandNodeIslandId =
        *reinterpret_cast<const uint32_t*>(islandNode + 0x04);
    state->islandNodeFlags =
        *reinterpret_cast<const uint8_t*>(islandNode + 0x08);
    state->kinematicBitmap =
        *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x108);
    state->kinematicChangeBitmap =
        *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x10C);
    state->notReadyBitmap =
        *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x110);
    state->notReadyChangeBitmap =
        *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x114);
    if (!ReadBitmapWord(state->kinematicBitmap, state->islandHook,
            &state->kinematicBitmapMap, &state->kinematicBitmapWordCount,
            &state->kinematicBitmapWord, &state->kinematicBitmapBit, error) ||
        !ReadBitmapWord(state->kinematicChangeBitmap, state->islandHook,
            &state->kinematicChangeBitmapMap,
            &state->kinematicChangeBitmapWordCount,
            &state->kinematicChangeBitmapWord,
            &state->kinematicChangeBitmapBit, error) ||
        !ReadBitmapWord(state->notReadyBitmap, state->islandHook,
            &state->notReadyBitmapMap, &state->notReadyBitmapWordCount,
            &state->notReadyBitmapWord, &state->notReadyBitmapBit, error) ||
        !ReadBitmapWord(state->notReadyChangeBitmap, state->islandHook,
            &state->notReadyChangeBitmapMap,
            &state->notReadyChangeBitmapWordCount,
            &state->notReadyChangeBitmapWord,
            &state->notReadyChangeBitmapBit, error))
        return Body2WorldLifecycleUnreadable;
    if (state->islandNodeOwner != bodySim ||
        (((state->islandNodeFlags & 0x01u) != 0 ? 1u : 0u) !=
            state->kinematicBitmapBit) ||
        (((state->islandNodeFlags & 0x08u) != 0 ? 1u : 0u) !=
            state->notReadyBitmapBit)) {
        *error = ERROR_INVALID_DATA;
        return Body2WorldLifecycleUnreadable;
    }
    const uint32_t everythingAsleep =
        *reinterpret_cast<const uint8_t*>(state->islandManager + 0x1DC);
    const uint32_t hasAnythingChanged =
        *reinterpret_cast<const uint8_t*>(state->islandManager + 0x1DD);
    const uint32_t performIslandUpdate =
        *reinterpret_cast<const uint8_t*>(state->islandManager + 0x1DE);
    if (everythingAsleep > 1u || hasAnythingChanged > 1u ||
        performIslandUpdate > 1u) {
        *error = ERROR_INVALID_DATA;
        return Body2WorldLifecycleUnreadable;
    }
    state->islandManagerFlags = everythingAsleep |
        (hasAnythingChanged << 8) | (performIslandUpdate << 16);

    state->sleepBodiesData =
        *reinterpret_cast<const uintptr_t*>(state->scScene + 0x464);
    state->sleepBodiesCount =
        *reinterpret_cast<const uint32_t*>(state->scScene + 0x468);
    state->sleepBodiesCapacity =
        *reinterpret_cast<const uint32_t*>(state->scScene + 0x46C);
    state->wokeBodiesData =
        *reinterpret_cast<const uintptr_t*>(state->scScene + 0x470);
    state->wokeBodiesCount =
        *reinterpret_cast<const uint32_t*>(state->scScene + 0x474);
    state->wokeBodiesCapacity =
        *reinterpret_cast<const uint32_t*>(state->scScene + 0x478);
    state->wokeBodyListValid =
        *reinterpret_cast<const uint8_t*>(state->scScene + 0x47C);
    state->sleepBodyListValid =
        *reinterpret_cast<const uint8_t*>(state->scScene + 0x47D);
    if (state->wokeBodyListValid > 1u || state->sleepBodyListValid > 1u) {
        *error = ERROR_INVALID_DATA;
        return Body2WorldLifecycleUnreadable;
    }
    if (!ReadPointerArray(state->sleepBodiesData, state->sleepBodiesCount,
            state->sleepBodiesCapacity, error) ||
        !ReadPointerArray(state->wokeBodiesData, state->wokeBodiesCount,
            state->wokeBodiesCapacity, error))
        return Body2WorldLifecycleUnreadable;
    const uintptr_t* sleepBodies =
        reinterpret_cast<const uintptr_t*>(state->sleepBodiesData);
    const uintptr_t* wokeBodies =
        reinterpret_cast<const uintptr_t*>(state->wokeBodiesData);
    state->sleepBodiesHash = OrderHash(sleepBodies, state->sleepBodiesCount);
    state->sleepBodiesIndex = PointerIndex(sleepBodies,
        state->sleepBodiesCount, state->bodyCore);
    state->wokeBodiesHash = OrderHash(wokeBodies, state->wokeBodiesCount);
    state->wokeBodiesIndex = PointerIndex(wokeBodies,
        state->wokeBodiesCount, state->bodyCore);

    uintptr_t kinematicBitmapMapAfter = 0;
    uintptr_t kinematicChangeBitmapMapAfter = 0;
    uintptr_t notReadyBitmapMapAfter = 0;
    uintptr_t notReadyChangeBitmapMapAfter = 0;
    uint32_t kinematicBitmapWordCountAfter = 0;
    uint32_t kinematicChangeBitmapWordCountAfter = 0;
    uint32_t notReadyBitmapWordCountAfter = 0;
    uint32_t notReadyChangeBitmapWordCountAfter = 0;
    uint32_t kinematicBitmapWordAfter = 0;
    uint32_t kinematicChangeBitmapWordAfter = 0;
    uint32_t notReadyBitmapWordAfter = 0;
    uint32_t notReadyChangeBitmapWordAfter = 0;
    uint32_t kinematicBitmapBitAfter = 0;
    uint32_t kinematicChangeBitmapBitAfter = 0;
    uint32_t notReadyBitmapBitAfter = 0;
    uint32_t notReadyChangeBitmapBitAfter = 0;
    if (!ReadBitmapWord(state->kinematicBitmap, state->islandHook,
            &kinematicBitmapMapAfter, &kinematicBitmapWordCountAfter,
            &kinematicBitmapWordAfter, &kinematicBitmapBitAfter, error) ||
        !ReadBitmapWord(state->kinematicChangeBitmap, state->islandHook,
            &kinematicChangeBitmapMapAfter,
            &kinematicChangeBitmapWordCountAfter,
            &kinematicChangeBitmapWordAfter,
            &kinematicChangeBitmapBitAfter, error) ||
        !ReadBitmapWord(state->notReadyBitmap, state->islandHook,
            &notReadyBitmapMapAfter, &notReadyBitmapWordCountAfter,
            &notReadyBitmapWordAfter, &notReadyBitmapBitAfter, error) ||
        !ReadBitmapWord(state->notReadyChangeBitmap, state->islandHook,
            &notReadyChangeBitmapMapAfter,
            &notReadyChangeBitmapWordCountAfter,
            &notReadyChangeBitmapWordAfter,
            &notReadyChangeBitmapBitAfter, error))
        return Body2WorldLifecycleUnreadable;

    const bool stable =
        state->sceneArrayIndex == *reinterpret_cast<const uint32_t*>(bodySim + 0x28) &&
        state->bodySimInternalFlags == *reinterpret_cast<const uint16_t*>(bodySim + 0x90) &&
        state->bodySimActive == ((*reinterpret_cast<const uint8_t*>(bodySim + 0x33) & 1u) ? 1u : 0u) &&
        state->wakeCounterCoreBits == *reinterpret_cast<const uint32_t*>(state->bodyCore + 0x98) &&
        state->islandNodeOwner == *reinterpret_cast<const uintptr_t*>(islandNode + 0x00) &&
        state->islandNodeIslandId == *reinterpret_cast<const uint32_t*>(islandNode + 0x04) &&
        state->islandNodeFlags == *reinterpret_cast<const uint8_t*>(islandNode + 0x08) &&
        state->kinematicBitmap == *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x108) &&
        state->kinematicChangeBitmap == *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x10C) &&
        state->notReadyBitmap == *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x110) &&
        state->notReadyChangeBitmap == *reinterpret_cast<const uintptr_t*>(state->islandManager + 0x114) &&
        state->kinematicBitmapMap == kinematicBitmapMapAfter &&
        state->kinematicChangeBitmapMap == kinematicChangeBitmapMapAfter &&
        state->notReadyBitmapMap == notReadyBitmapMapAfter &&
        state->notReadyChangeBitmapMap == notReadyChangeBitmapMapAfter &&
        state->kinematicBitmapWordCount == kinematicBitmapWordCountAfter &&
        state->kinematicChangeBitmapWordCount == kinematicChangeBitmapWordCountAfter &&
        state->notReadyBitmapWordCount == notReadyBitmapWordCountAfter &&
        state->notReadyChangeBitmapWordCount == notReadyChangeBitmapWordCountAfter &&
        state->kinematicBitmapWord == kinematicBitmapWordAfter &&
        state->kinematicChangeBitmapWord == kinematicChangeBitmapWordAfter &&
        state->notReadyBitmapWord == notReadyBitmapWordAfter &&
        state->notReadyChangeBitmapWord == notReadyChangeBitmapWordAfter &&
        state->kinematicBitmapBit == kinematicBitmapBitAfter &&
        state->kinematicChangeBitmapBit == kinematicChangeBitmapBitAfter &&
        state->notReadyBitmapBit == notReadyBitmapBitAfter &&
        state->notReadyChangeBitmapBit == notReadyChangeBitmapBitAfter &&
        state->islandManagerFlags ==
            (static_cast<uint32_t>(*reinterpret_cast<const uint8_t*>(state->islandManager + 0x1DC)) |
            (static_cast<uint32_t>(*reinterpret_cast<const uint8_t*>(state->islandManager + 0x1DD)) << 8) |
            (static_cast<uint32_t>(*reinterpret_cast<const uint8_t*>(state->islandManager + 0x1DE)) << 16)) &&
        state->activeBodiesData == *reinterpret_cast<const uintptr_t*>(state->interactionScene + 0x00) &&
        state->activeBodiesCount == *reinterpret_cast<const uint32_t*>(state->interactionScene + 0x04) &&
        state->activeBodiesCapacity == *reinterpret_cast<const uint32_t*>(state->interactionScene + 0x08) &&
        state->activeTwoWayStart == *reinterpret_cast<const uint32_t*>(state->interactionScene + 0x0C) &&
        state->activeBodiesHash == OrderHash(activeBodies, state->activeBodiesCount) &&
        state->sleepBodiesData == *reinterpret_cast<const uintptr_t*>(state->scScene + 0x464) &&
        state->sleepBodiesCount == *reinterpret_cast<const uint32_t*>(state->scScene + 0x468) &&
        state->sleepBodiesCapacity == *reinterpret_cast<const uint32_t*>(state->scScene + 0x46C) &&
        state->sleepBodiesHash == OrderHash(sleepBodies, state->sleepBodiesCount) &&
        state->wokeBodiesData == *reinterpret_cast<const uintptr_t*>(state->scScene + 0x470) &&
        state->wokeBodiesCount == *reinterpret_cast<const uint32_t*>(state->scScene + 0x474) &&
        state->wokeBodiesCapacity == *reinterpret_cast<const uint32_t*>(state->scScene + 0x478) &&
        state->wokeBodiesHash == OrderHash(wokeBodies, state->wokeBodiesCount) &&
        state->wokeBodyListValid ==
            *reinterpret_cast<const uint8_t*>(state->scScene + 0x47C) &&
        state->sleepBodyListValid ==
            *reinterpret_cast<const uint8_t*>(state->scScene + 0x47D);
    state->lifecycleStable = stable ? 1u : 0u;
    if (!stable) {
        *error = ERROR_RETRY;
        return Body2WorldLifecycleUnstable;
    }
    return Body2WorldOk;
}

static Body2WorldResult CaptureBodyPoseState(uintptr_t unityBase,
    uintptr_t rigidbody, BodyPoseState* state, uint32_t* error) {
    if (error) *error = ERROR_SUCCESS;
    if (!unityBase || !rigidbody || !state || !error) {
        if (error) *error = ERROR_INVALID_PARAMETER;
        return Body2WorldBadArgument;
    }
    *state = {};
    if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x38)) {
        *error = ERROR_NOACCESS;
        return Body2WorldUnreadableRigidbody;
    }
    const uintptr_t actor =
        *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    state->actor = actor;
    if (!actor || !Readable(reinterpret_cast<const void*>(actor), 0x120)) {
        *error = ERROR_INVALID_STATE;
        return Body2WorldMissingActor;
    }
    const uintptr_t vtable = *reinterpret_cast<const uintptr_t*>(actor);
    const void* getAddress = reinterpret_cast<const void*>(
        unityBase + kNpGetGlobalPoseRva);
    const void* setAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetGlobalPoseRva);
    const void* scbAddress = reinterpret_cast<const void*>(
        unityBase + kScbBodySetBody2WorldRva);
    const void* scbThisAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetGlobalPoseRva + 0x2C1);
    const void* scbCallAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetGlobalPoseRva + 0x36E);
    const void* scenePreludeAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetGlobalPoseRva + 0x0B);
    const void* sceneUpdateAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetGlobalPoseRva + 0xAC);
    const void* getApiSceneAddress = reinterpret_cast<const void*>(
        unityBase + kNpActorGetApiSceneRva);
    const void* markSceneQueryAddress = reinterpret_cast<const void*>(
        unityBase + kNpShapeManagerMarkSceneQueryRva);
    const void* body2ActorSelectionAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetGlobalPoseRva + 0x142);
    const void* scbLastCopyAddress = reinterpret_cast<const void*>(
        unityBase + kScbBodySetBody2WorldRva + 0x3E);
    const void* scbCorePathAddress = reinterpret_cast<const void*>(
        unityBase + kScbBodySetBody2WorldRva + 0x47);
    const void* coreSetAddress = reinterpret_cast<const void*>(
        unityBase + kScBodyCoreSetBody2WorldRva);
    if (vtable != unityBase + kNpRigidDynamicVtableRva ||
        !Readable(reinterpret_cast<const void*>(vtable), 0x58) ||
        *reinterpret_cast<const uintptr_t*>(vtable + 0x50) !=
            unityBase + kNpGetGlobalPoseRva ||
        *reinterpret_cast<const uintptr_t*>(vtable + 0x54) !=
            unityBase + kNpSetGlobalPoseRva ||
        !Readable(getAddress, sizeof(kNpGetGlobalPoseBytes)) ||
        !EqualBytes(getAddress, kNpGetGlobalPoseBytes,
            sizeof(kNpGetGlobalPoseBytes)) ||
        !Readable(setAddress, sizeof(kNpSetGlobalPoseBytes)) ||
        !EqualBytes(setAddress, kNpSetGlobalPoseBytes,
            sizeof(kNpSetGlobalPoseBytes)) ||
        !Readable(scbAddress, sizeof(kScbBodySetBody2WorldBytes)) ||
        !EqualBytes(scbAddress, kScbBodySetBody2WorldBytes,
            sizeof(kScbBodySetBody2WorldBytes)) ||
        !Readable(scbThisAddress, sizeof(kNpSetGlobalPoseScbThisBytes)) ||
        !EqualBytes(scbThisAddress, kNpSetGlobalPoseScbThisBytes,
            sizeof(kNpSetGlobalPoseScbThisBytes)) ||
        !Readable(scbCallAddress, sizeof(kNpSetGlobalPoseScbCallBytes)) ||
        !EqualBytes(scbCallAddress, kNpSetGlobalPoseScbCallBytes,
            sizeof(kNpSetGlobalPoseScbCallBytes)) ||
        !Readable(scenePreludeAddress, sizeof(kNpSetGlobalPoseScenePreludeBytes)) ||
        !EqualBytes(scenePreludeAddress, kNpSetGlobalPoseScenePreludeBytes,
            sizeof(kNpSetGlobalPoseScenePreludeBytes)) ||
        !Readable(sceneUpdateAddress, sizeof(kNpSetGlobalPoseSceneUpdateBytes)) ||
        !EqualBytes(sceneUpdateAddress, kNpSetGlobalPoseSceneUpdateBytes,
            sizeof(kNpSetGlobalPoseSceneUpdateBytes)) ||
        !Readable(getApiSceneAddress, sizeof(kNpActorGetApiSceneBytes)) ||
        !EqualBytes(getApiSceneAddress, kNpActorGetApiSceneBytes,
            sizeof(kNpActorGetApiSceneBytes)) ||
        !Readable(markSceneQueryAddress, sizeof(kNpShapeManagerMarkSceneQueryBytes)) ||
        !EqualBytes(markSceneQueryAddress, kNpShapeManagerMarkSceneQueryBytes,
            sizeof(kNpShapeManagerMarkSceneQueryBytes)) ||
        !Readable(body2ActorSelectionAddress,
            sizeof(kNpSetGlobalPoseBody2ActorSelectionBytes)) ||
        !EqualBytes(body2ActorSelectionAddress,
            kNpSetGlobalPoseBody2ActorSelectionBytes,
            sizeof(kNpSetGlobalPoseBody2ActorSelectionBytes)) ||
        !Readable(scbLastCopyAddress,
            sizeof(kScbBodySetBody2WorldLastCopyBytes)) ||
        !EqualBytes(scbLastCopyAddress,
            kScbBodySetBody2WorldLastCopyBytes,
            sizeof(kScbBodySetBody2WorldLastCopyBytes)) ||
        !Readable(scbCorePathAddress,
            sizeof(kScbBodySetBody2WorldCorePathBytes)) ||
        !EqualBytes(scbCorePathAddress,
            kScbBodySetBody2WorldCorePathBytes,
            sizeof(kScbBodySetBody2WorldCorePathBytes)) ||
        !Readable(coreSetAddress, sizeof(kScBodyCoreSetBody2WorldBytes)) ||
        !EqualBytes(coreSetAddress, kScBodyCoreSetBody2WorldBytes,
            sizeof(kScBodyCoreSetBody2WorldBytes))) {
        *error = ERROR_REVISION_MISMATCH;
        return Body2WorldRevisionMismatch;
    }

    const uint32_t controlWord =
        *reinterpret_cast<const uint32_t*>(actor + 0x34);
    state->controlState = controlWord >> 30;
    state->scene = *reinterpret_cast<const uintptr_t*>(actor + 0x30);
    if (state->controlState != 2) {
        *error = ERROR_INVALID_STATE;
        return Body2WorldNotInScene;
    }
    if (!state->scene ||
        !Readable(reinterpret_cast<const void*>(state->scene + 0x980), 2)) {
        *error = ERROR_NOACCESS;
        return Body2WorldBuffered;
    }
    state->simulationRunning =
        *reinterpret_cast<const uint8_t*>(state->scene + 0x980) != 0 ? 1u : 0u;
    state->physicsBuffering =
        *reinterpret_cast<const uint8_t*>(state->scene + 0x981) != 0 ? 1u : 0u;
    if (state->simulationRunning != 0 ||
        state->physicsBuffering != 0) {
        *error = ERROR_BUSY;
        return Body2WorldBuffered;
    }

    state->bodyBufferFlags =
        *reinterpret_cast<const uint32_t*>(actor + 0x11C);
    if (state->bodyBufferFlags != 0) {
        *error = ERROR_BUSY;
        return Body2WorldBuffered;
    }
    state->bodySim = *reinterpret_cast<const uintptr_t*>(actor + 0x44);
    if (!state->bodySim ||
        !Readable(reinterpret_cast<const void*>(state->bodySim + 0x33), 1)) {
        *error = ERROR_NOACCESS;
        return Body2WorldMissingActor;
    }
    state->wakeCounterBufferedBits =
        *reinterpret_cast<const uint32_t*>(actor + 0x114);
    state->wakeCounterCoreBits =
        *reinterpret_cast<const uint32_t*>(actor + 0xD8);
    state->bufferedIsSleeping =
        *reinterpret_cast<const uint32_t*>(actor + 0x118);
    state->bodySimActive =
        (*reinterpret_cast<const uint8_t*>(state->bodySim + 0x33) & 1u) != 0 ? 1u : 0u;
    if (state->wakeCounterBufferedBits != state->wakeCounterCoreBits ||
        state->bufferedIsSleeping > 1u ||
        state->bufferedIsSleeping == state->bodySimActive) {
        *error = ERROR_INVALID_STATE;
        return Body2WorldSleepStateMismatch;
    }
    const Body2WorldResult lifecycleResult =
        CaptureBodyLifecycleState(state, error);
    if (lifecycleResult != Body2WorldOk) return lifecycleResult;
    uintptr_t body2Actor = actor + 0x70;
    if ((state->bodyBufferFlags & 0x200u) != 0) {
        const uintptr_t bodyBuffer =
            *reinterpret_cast<const uintptr_t*>(actor + 0x38);
        if (!bodyBuffer ||
            !Readable(reinterpret_cast<const void*>(bodyBuffer + 0x90),
                sizeof(PhysxTransform))) {
            *error = ERROR_NOACCESS;
            return Body2WorldBuffered;
        }
        body2Actor = bodyBuffer + 0x90;
    }
    if (!Readable(reinterpret_cast<const void*>(body2Actor),
            sizeof(PhysxTransform)) ||
        !Readable(reinterpret_cast<const void*>(actor + 0xE0),
            sizeof(PhysxTransform)) ||
        !Readable(reinterpret_cast<const void*>(actor + 0x50),
            sizeof(PhysxTransform))) {
        *error = ERROR_NOACCESS;
        return Body2WorldMissingActor;
    }
    state->body2Actor = ExportPose(
        *reinterpret_cast<const PhysxTransform*>(body2Actor));
    state->bufferedBody2World = ExportPose(
        *reinterpret_cast<const PhysxTransform*>(actor + 0xE0));
    state->coreBody2World = ExportPose(
        *reinterpret_cast<const PhysxTransform*>(actor + 0x50));
    if (!EqualBytes(&state->bufferedBody2World,
            reinterpret_cast<const uint8_t*>(&state->coreBody2World),
            sizeof(RigidPose))) {
        *error = ERROR_INVALID_STATE;
        return Body2WorldCoreMismatch;
    }
    NpRigidDynamicGetGlobalPose getGlobalPose =
        reinterpret_cast<NpRigidDynamicGetGlobalPose>(
            unityBase + kNpGetGlobalPoseRva);
    PhysxTransform actorPose = {};
    getGlobalPose(reinterpret_cast<void*>(actor), &actorPose);
    state->actorPose = ExportPose(actorPose);
    if (!Finite(state->actorPose) || !Finite(state->body2Actor) ||
        !Finite(state->bufferedBody2World) ||
        !Finite(state->coreBody2World)) {
        *error = ERROR_INVALID_DATA;
        return Body2WorldNonfinite;
    }
    return Body2WorldOk;
}

static bool Finite(const RigidMassFrame& frame) {
    for (uint32_t i = 0; i < 3; ++i)
        if (!Finite(frame.centerOfMass[i]) || !Finite(frame.inertiaTensor[i]))
            return false;
    for (uint32_t i = 0; i < 4; ++i)
        if (!Finite(frame.inertiaTensorRotation[i])) return false;
    return true;
}

static bool SupportedAnalyticGeometry(uint32_t type) {
    return type == 0 || type == 2 || type == 3;
}

static uint32_t GeometrySize(uint32_t type) {
    if (type == 0) return 8;
    if (type == 2) return 12;
    if (type == 3) return 16;
    return sizeof(uint32_t);
}

static bool Finite(const ShapeGeometry& geometry) {
    if (!SupportedAnalyticGeometry(geometry.type)) return true;
    const uint32_t values = geometry.type == 0 ? 1 : (geometry.type == 2 ? 2 : 3);
    for (uint32_t i = 0; i < values; ++i)
        if (!Finite(geometry.values[i])) return false;
    if (geometry.values[0] <= 0.0f) return false;
    if (geometry.type == 2) return geometry.values[1] >= 0.0f;
    for (uint32_t i = 1; i < values; ++i)
        if (geometry.values[i] <= 0.0f) return false;
    return true;
}

static void InitializeContactPoolReceipt(ContactPoolReceipt* receipt, uintptr_t context) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(ContactPoolReceipt);
    receipt->context = context;
}

static void InitializeContactContextObserverReceipt(ContactContextObserverReceipt* receipt,
    uintptr_t unityBase) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(ContactContextObserverReceipt);
    receipt->unityBase = unityBase;
    receipt->observedContext = static_cast<uintptr_t>(g_observedContactManagerContext);
    receipt->observations = static_cast<uint32_t>(g_contactManagerContextObservations);
    receipt->installed = g_contactManagerContextObserverInstalled ? 1u : 0u;
}

static void InitializeManifoldPoolReceipt(ManifoldPoolReceipt* receipt,
    uintptr_t unityBase, uintptr_t context, uint32_t poolKind) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(ManifoldPoolReceipt);
    receipt->unityBase = unityBase;
    receipt->context = context;
    receipt->poolKind = poolKind;
}

static void InitializeDirtyInteractionOrderReceipt(
    DirtyInteractionOrderReceipt* receipt, uintptr_t unityBase) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(DirtyInteractionOrderReceipt);
    receipt->result = static_cast<uint32_t>(g_dirtyInteractionResult);
    receipt->lastError = static_cast<uint32_t>(g_dirtyInteractionError);
    receipt->unityBase = unityBase;
    receipt->nphaseCore = g_dirtyInteractionNPhaseCore;
    receipt->set = g_dirtyInteractionSet;
    receipt->entries = g_dirtyInteractionEntries;
    receipt->entriesNext = g_dirtyInteractionEntriesNext;
    receipt->hash = g_dirtyInteractionHash;
    receipt->entriesCapacity = g_dirtyInteractionEntriesCapacity;
    receipt->hashSize = g_dirtyInteractionHashSize;
    receipt->count = g_dirtyInteractionCount;
    receipt->action = static_cast<uint32_t>(g_dirtyInteractionAction);
    receipt->captures = static_cast<uint32_t>(g_dirtyInteractionCaptures);
    receipt->restores = static_cast<uint32_t>(g_dirtyInteractionRestores);
    receipt->orderHashBefore = g_dirtyInteractionOrderHashBefore;
    receipt->orderHashAfter = g_dirtyInteractionOrderHashAfter;
    receipt->installed = g_dirtyInteractionInstalled ? 1u : 0u;
    receipt->armed = g_dirtyInteractionAction != DirtyInteractionOrderIdle ? 1u : 0u;
    receipt->restoreMode = g_dirtyInteractionRestoreMode;
    receipt->matchedCount = g_dirtyInteractionMatchedCount;
    receipt->capturedOnlyCount = g_dirtyInteractionCapturedOnlyCount;
    receipt->liveOnlyCount = g_dirtyInteractionLiveOnlyCount;
}

static void __cdecl ObserveContactManagerContext(uintptr_t context) {
    InterlockedExchange(&g_observedContactManagerContext, static_cast<LONG>(context));
    InterlockedIncrement(&g_contactManagerContextObservations);
}

// This pass-through observer records only PxsContext's this pointer. It runs
// before the untouched shipped createContactManager body and changes no
// allocator, descriptor, manager or return value.
__declspec(naked) static void HookCreateContactManagerContext() {
    __asm pushfd
    __asm pushad
    __asm push ecx
    __asm call ObserveContactManagerContext
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_contactManagerContextTrampoline]
}

static DirtyInteractionKey ReadDirtyInteractionKey(uintptr_t interaction) {
    const uintptr_t* words = reinterpret_cast<const uintptr_t*>(interaction);
    const uintptr_t element0 = words[8];
    const uintptr_t element1 = words[9];
    DirtyInteractionKey key = {
        element0 < element1 ? element0 : element1,
        element0 < element1 ? element1 : element0,
        words[0],
        *reinterpret_cast<const uint8_t*>(interaction + 0x1C)
    };
    return key;
}

static bool SameDirtyInteractionKey(const DirtyInteractionKey& left,
    const DirtyInteractionKey& right) {
    return left.elementLow == right.elementLow &&
        left.elementHigh == right.elementHigh &&
        left.primaryVtable == right.primaryVtable &&
        left.interactionType == right.interactionType;
}

static uint32_t DirtyInteractionKeyOrderHash(const DirtyInteractionKey* keys,
    uint32_t count) {
    uint32_t hash = 2166136261u;
    const uint32_t* words = reinterpret_cast<const uint32_t*>(keys);
    for (uint32_t i = 0; i < count * 4; ++i) {
        hash ^= words[i];
        hash *= 16777619u;
    }
    return hash;
}

static uint32_t PhysxPointerHash(uintptr_t pointer) {
    uint32_t value = static_cast<uint32_t>(pointer);
    value += ~(value << 15);
    value ^= value >> 10;
    value += value << 3;
    value ^= value >> 6;
    value += ~(value << 11);
    value ^= value >> 16;
    return value;
}

static void CompleteDirtyInteractionOrder(DirtyInteractionOrderResult result,
    uint32_t error) {
    InterlockedExchange(&g_dirtyInteractionResult, result);
    InterlockedExchange(&g_dirtyInteractionError, static_cast<LONG>(error));
    MemoryBarrier();
    InterlockedExchange(&g_dirtyInteractionAction, DirtyInteractionOrderIdle);
}

// Runs at the exact entry to Sc::NPhaseCore::updateDirtyInteractions.  The
// hook is inert unless explicitly armed while the authoring pause fence is
// held.  All validation completes before a restore writes any PhysX state.
static void __cdecl ProcessDirtyInteractionOrder(const uintptr_t* saved) {
    const LONG action = g_dirtyInteractionAction;
    if (action != DirtyInteractionOrderCapture &&
        action != DirtyInteractionOrderRestore) return;

    const uintptr_t nphase = saved[6];
    const uintptr_t set = nphase + 0x44;
    if (!nphase || !Readable(reinterpret_cast<const void*>(set), 0x28)) {
        CompleteDirtyInteractionOrder(DirtyInteractionOrderInvalidHeader,
            ERROR_NOACCESS);
        return;
    }
    const uintptr_t entries =
        *reinterpret_cast<const uintptr_t*>(set + 0x04);
    const uintptr_t buffer =
        *reinterpret_cast<const uintptr_t*>(set + 0x00);
    const uintptr_t entriesNext =
        *reinterpret_cast<const uintptr_t*>(set + 0x08);
    const uintptr_t hash =
        *reinterpret_cast<const uintptr_t*>(set + 0x0C);
    const uint32_t entriesCapacity =
        *reinterpret_cast<const uint32_t*>(set + 0x10);
    const uint32_t hashSize =
        *reinterpret_cast<const uint32_t*>(set + 0x14);
    const uint32_t loadFactorBits =
        *reinterpret_cast<const uint32_t*>(set + 0x18);
    const uint32_t freeList =
        *reinterpret_cast<const uint32_t*>(set + 0x1C);
    const uint32_t count =
        *reinterpret_cast<const uint32_t*>(set + 0x24);
    const uintptr_t expectedEntriesNext =
        buffer + hashSize * sizeof(uint32_t);
    const uintptr_t expectedEntries = (expectedEntriesNext +
        entriesCapacity * sizeof(uint32_t) + 15u) & ~static_cast<uintptr_t>(15u);
    const uintptr_t ownerScene =
        *reinterpret_cast<const uintptr_t*>(nphase);
    if (!buffer || !entries || !entriesNext || !hash ||
        hash != buffer || entriesNext != expectedEntriesNext ||
        entries != expectedEntries || !ownerScene ||
        !Readable(reinterpret_cast<const void*>(ownerScene + 0x4A4), 1) ||
        (*reinterpret_cast<const uint8_t*>(ownerScene + 0x4A4) & 6u) != 0 ||
        entriesCapacity == 0 ||
        entriesCapacity > kMaximumDirtyInteractions ||
        hashSize == 0 || hashSize > kMaximumDirtyHashSize ||
        (hashSize & (hashSize - 1)) != 0 || count > entriesCapacity ||
        freeList != count || loadFactorBits != 0x3F400000u ||
        !Readable(reinterpret_cast<const void*>(entries),
            count * sizeof(uintptr_t)) ||
        !Readable(reinterpret_cast<const void*>(entriesNext),
            entriesCapacity * sizeof(uint32_t)) ||
        !Readable(reinterpret_cast<const void*>(hash),
            hashSize * sizeof(uint32_t))) {
        CompleteDirtyInteractionOrder(DirtyInteractionOrderInvalidHeader,
            ERROR_INVALID_DATA);
        return;
    }

    const uintptr_t* dense = reinterpret_cast<const uintptr_t*>(entries);
    for (uint32_t i = 0; i < count; ++i) {
        if (!dense[i] || !Readable(reinterpret_cast<const void*>(dense[i]),
            10 * sizeof(uintptr_t))) {
            CompleteDirtyInteractionOrder(DirtyInteractionOrderInvalidEntry,
                ERROR_NOACCESS);
            return;
        }
        const DirtyInteractionKey key = ReadDirtyInteractionKey(dense[i]);
        const uint16_t coreFlags =
            *reinterpret_cast<const uint16_t*>(dense[i] + 0x06);
        if (!key.primaryVtable || !key.elementLow || !key.elementHigh ||
            key.elementLow == key.elementHigh || key.interactionType > 5 ||
            (coreFlags & 3u) != 3u) {
            CompleteDirtyInteractionOrder(DirtyInteractionOrderInvalidEntry,
                ERROR_INVALID_DATA);
            return;
        }
        g_dirtyInteractionLive[i] = dense[i];
        g_dirtyInteractionReorderedKeys[i] = key;
        for (uint32_t prior = 0; prior < i; ++prior) {
            if (g_dirtyInteractionLive[prior] == dense[i] ||
                SameDirtyInteractionKey(g_dirtyInteractionReorderedKeys[prior],
                    key)) {
                CompleteDirtyInteractionOrder(
                    DirtyInteractionOrderInvalidEntry, ERROR_DUP_NAME);
                return;
            }
        }
    }

    // Validate the current set's hash image before relying on or replacing it.
    for (uint32_t i = 0; i < count; ++i) g_dirtyInteractionUsed[i] = 0;
    const uint32_t* currentNext =
        reinterpret_cast<const uint32_t*>(entriesNext);
    const uint32_t* currentHash = reinterpret_cast<const uint32_t*>(hash);
    uint32_t visited = 0;
    for (uint32_t bucket = 0; bucket < hashSize; ++bucket) {
        uint32_t index = currentHash[bucket];
        while (index != 0xFFFFFFFFu) {
            if (index >= count || g_dirtyInteractionUsed[index] ||
                (PhysxPointerHash(dense[index]) & (hashSize - 1)) != bucket ||
                ++visited > count) {
                CompleteDirtyInteractionOrder(
                    DirtyInteractionOrderInvalidHeader, ERROR_INVALID_DATA);
                return;
            }
            g_dirtyInteractionUsed[index] = 1;
            index = currentNext[index];
        }
    }
    if (visited != count) {
        CompleteDirtyInteractionOrder(DirtyInteractionOrderInvalidHeader,
            ERROR_INVALID_DATA);
        return;
    }
    const uint32_t liveOrderHash = DirtyInteractionKeyOrderHash(
        g_dirtyInteractionReorderedKeys, count);

    // Preserve the armed identity before publishing the coherent set observed
    // by this invocation.  Every post-hook receipt (including an identity or
    // membership rejection) then reports the actual current container rather
    // than stale capture-time storage.
    const uintptr_t armedNPhaseCore = g_dirtyInteractionNPhaseCore;
    const uint32_t armedCount = g_dirtyInteractionCount;
    g_dirtyInteractionNPhaseCore = nphase;
    g_dirtyInteractionSet = set;
    g_dirtyInteractionEntries = entries;
    g_dirtyInteractionEntriesNext = entriesNext;
    g_dirtyInteractionHash = hash;
    g_dirtyInteractionEntriesCapacity = entriesCapacity;
    g_dirtyInteractionHashSize = hashSize;
    g_dirtyInteractionCount = count;

    if (action == DirtyInteractionOrderCapture) {
        for (uint32_t i = 0; i < count; ++i)
            g_dirtyInteractionKeys[i] =
                ReadDirtyInteractionKey(g_dirtyInteractionLive[i]);
        g_dirtyInteractionRestoreMode = DirtyInteractionRestoreNone;
        g_dirtyInteractionMatchedCount = 0;
        g_dirtyInteractionCapturedOnlyCount = 0;
        g_dirtyInteractionLiveOnlyCount = 0;
        g_dirtyInteractionOrderHashBefore = liveOrderHash;
        g_dirtyInteractionOrderHashAfter = g_dirtyInteractionOrderHashBefore;
        InterlockedIncrement(&g_dirtyInteractionCaptures);
        CompleteDirtyInteractionOrder(DirtyInteractionOrderOk, ERROR_SUCCESS);
        return;
    }

    // The CoalescedHashSet is allowed to grow and replace its backing buffer
    // between checkpoint capture and restore.  Its storage addresses and
    // capacities are therefore telemetry, not scene identity. NPhaseCore is
    // stable scene-owned identity. Exact mode additionally requires identical
    // membership; projection mode permits legitimate branch-local additions
    // and removals while ordering only the checkpoint identities that survive.
    if (nphase != armedNPhaseCore ||
        (g_dirtyInteractionRestoreMode == DirtyInteractionRestoreExact &&
            count != armedCount)) {
        CompleteDirtyInteractionOrder(DirtyInteractionOrderIdentityChanged,
            ERROR_INVALID_STATE);
        return;
    }

    for (uint32_t i = 0; i < count; ++i) g_dirtyInteractionUsed[i] = 0;
    uint32_t matched = 0;
    for (uint32_t target = 0; target < armedCount; ++target) {
        uint32_t found = 0xFFFFFFFFu;
        for (uint32_t live = 0; live < count; ++live) {
            if (g_dirtyInteractionUsed[live]) continue;
            const DirtyInteractionKey key =
                ReadDirtyInteractionKey(g_dirtyInteractionLive[live]);
            if (SameDirtyInteractionKey(key, g_dirtyInteractionKeys[target])) {
                found = live;
                break;
            }
        }
        if (found == 0xFFFFFFFFu) {
            if (g_dirtyInteractionRestoreMode == DirtyInteractionRestoreExact) {
                CompleteDirtyInteractionOrder(
                    DirtyInteractionOrderMembershipChanged, ERROR_NOT_FOUND);
                return;
            }
            continue;
        }
        g_dirtyInteractionUsed[found] = 1;
        g_dirtyInteractionMatched[matched++] = g_dirtyInteractionLive[found];
    }
    g_dirtyInteractionMatchedCount = matched;
    g_dirtyInteractionCapturedOnlyCount = armedCount - matched;
    g_dirtyInteractionLiveOnlyCount = count - matched;
    if (g_dirtyInteractionRestoreMode == DirtyInteractionRestoreExact &&
        (g_dirtyInteractionCapturedOnlyCount != 0 ||
            g_dirtyInteractionLiveOnlyCount != 0)) {
        CompleteDirtyInteractionOrder(
            DirtyInteractionOrderMembershipChanged, ERROR_NOT_FOUND);
        return;
    }

    // Preserve every current-only interaction at its current dense slot.
    // Refill only the slots occupied by surviving checkpoint interactions in
    // captured relative order. This is the smallest permutation that restores
    // historical order without inventing placement for branch-local contacts.
    uint32_t nextMatched = 0;
    for (uint32_t live = 0; live < count; ++live) {
        g_dirtyInteractionReordered[live] = g_dirtyInteractionUsed[live]
            ? g_dirtyInteractionMatched[nextMatched++]
            : g_dirtyInteractionLive[live];
    }
    if (nextMatched != matched) {
        CompleteDirtyInteractionOrder(
            DirtyInteractionOrderWriteVerificationFailed, ERROR_INVALID_DATA);
        return;
    }
    bool changed = false;
    for (uint32_t i = 0; i < count; ++i)
        if (g_dirtyInteractionReordered[i] != g_dirtyInteractionLive[i]) {
            changed = true;
            break;
        }
    if (!changed) {
        g_dirtyInteractionOrderHashBefore = liveOrderHash;
        g_dirtyInteractionOrderHashAfter = liveOrderHash;
        InterlockedIncrement(&g_dirtyInteractionRestores);
        CompleteDirtyInteractionOrder(DirtyInteractionOrderOk, ERROR_SUCCESS);
        return;
    }
    if (!Writable(reinterpret_cast<void*>(entries),
            count * sizeof(uintptr_t)) ||
        !Writable(reinterpret_cast<void*>(entriesNext),
            count * sizeof(uint32_t)) ||
        !Writable(reinterpret_cast<void*>(hash),
            hashSize * sizeof(uint32_t))) {
        CompleteDirtyInteractionOrder(DirtyInteractionOrderNotWritable,
            ERROR_WRITE_FAULT);
        return;
    }

    const uint32_t* liveNext =
        reinterpret_cast<const uint32_t*>(entriesNext);
    const uint32_t* liveHash = reinterpret_cast<const uint32_t*>(hash);
    for (uint32_t i = 0; i < count; ++i) {
        g_dirtyInteractionOldNext[i] = liveNext[i];
        g_dirtyInteractionNewNext[i] = 0xFFFFFFFFu;
    }
    for (uint32_t i = 0; i < hashSize; ++i) {
        g_dirtyInteractionOldHash[i] = liveHash[i];
        g_dirtyInteractionNewHash[i] = 0xFFFFFFFFu;
    }
    for (uint32_t i = 0; i < count; ++i) {
        const uint32_t bucket =
            PhysxPointerHash(g_dirtyInteractionReordered[i]) & (hashSize - 1);
        g_dirtyInteractionNewNext[i] = g_dirtyInteractionNewHash[bucket];
        g_dirtyInteractionNewHash[bucket] = i;
    }

    CopyWords(reinterpret_cast<uintptr_t*>(entries),
        g_dirtyInteractionReordered, count);
    CopyBytes(reinterpret_cast<void*>(entriesNext),
        g_dirtyInteractionNewNext, count * sizeof(uint32_t));
    CopyBytes(reinterpret_cast<void*>(hash),
        g_dirtyInteractionNewHash, hashSize * sizeof(uint32_t));
    if (!EqualBytes(reinterpret_cast<const void*>(entries),
            reinterpret_cast<const uint8_t*>(g_dirtyInteractionReordered),
            count * sizeof(uintptr_t)) ||
        !EqualBytes(reinterpret_cast<const void*>(entriesNext),
            reinterpret_cast<const uint8_t*>(g_dirtyInteractionNewNext),
            count * sizeof(uint32_t)) ||
        !EqualBytes(reinterpret_cast<const void*>(hash),
            reinterpret_cast<const uint8_t*>(g_dirtyInteractionNewHash),
            hashSize * sizeof(uint32_t))) {
        CopyWords(reinterpret_cast<uintptr_t*>(entries),
            g_dirtyInteractionLive, count);
        CopyBytes(reinterpret_cast<void*>(entriesNext),
            g_dirtyInteractionOldNext, count * sizeof(uint32_t));
        CopyBytes(reinterpret_cast<void*>(hash),
            g_dirtyInteractionOldHash, hashSize * sizeof(uint32_t));
        CompleteDirtyInteractionOrder(
            DirtyInteractionOrderWriteVerificationFailed, ERROR_WRITE_FAULT);
        return;
    }

    g_dirtyInteractionOrderHashBefore = liveOrderHash;
    for (uint32_t i = 0; i < count; ++i)
        g_dirtyInteractionReorderedKeys[i] =
            ReadDirtyInteractionKey(g_dirtyInteractionReordered[i]);
    g_dirtyInteractionOrderHashAfter =
        DirtyInteractionKeyOrderHash(g_dirtyInteractionReorderedKeys, count);
    InterlockedIncrement(&g_dirtyInteractionRestores);
    CompleteDirtyInteractionOrder(DirtyInteractionOrderOk, ERROR_SUCCESS);
}

__declspec(naked) static void HookUpdateDirtyInteractions() {
    __asm pushfd
    __asm pushad
    __asm mov eax, esp
    __asm push eax
    __asm call ProcessDirtyInteractionOrder
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm jmp dword ptr [g_dirtyInteractionTrampoline]
}

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

static bool HasDirtyInteractionJump(const void* source) {
    const uint8_t* bytes = static_cast<const uint8_t*>(source);
    if (bytes[0] != 0xE9 || bytes[5] != 0x90) return false;
    const int32_t displacement = *reinterpret_cast<const int32_t*>(bytes + 1);
    const uintptr_t destination = reinterpret_cast<uintptr_t>(source) + 5 +
        displacement;
    return destination == reinterpret_cast<uintptr_t>(HookUpdateDirtyInteractions);
}

static int InstallDirtyInteractionOrderHook(uintptr_t unityBase,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!unityBase)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderBadArgument, ERROR_INVALID_PARAMETER);
    if (g_dirtyInteractionInstalled)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderAlreadyInstalled, ERROR_ALREADY_EXISTS);
    uint8_t* source =
        reinterpret_cast<uint8_t*>(unityBase + kUpdateDirtyInteractionsRva);
    if (!Readable(source, sizeof(kUpdateDirtyInteractionsBytes)) ||
        !EqualBytes(source, kUpdateDirtyInteractionsBytes,
            sizeof(kUpdateDirtyInteractionsBytes)))
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderRevisionMismatch, ERROR_REVISION_MISMATCH);
    uint8_t* trampoline = static_cast<uint8_t*>(VirtualAlloc(0,
        sizeof(kUpdateDirtyInteractionsBytes) + 5,
        MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!trampoline)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderAllocationFailed, GetLastError());
    CopyBytes(g_dirtyInteractionOriginal, source,
        sizeof(kUpdateDirtyInteractionsBytes));
    CopyBytes(trampoline, source, sizeof(kUpdateDirtyInteractionsBytes));
    trampoline[sizeof(kUpdateDirtyInteractionsBytes)] = 0xE9;
    *reinterpret_cast<int32_t*>(trampoline +
        sizeof(kUpdateDirtyInteractionsBytes) + 1) = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(source + sizeof(kUpdateDirtyInteractionsBytes)) -
        reinterpret_cast<uintptr_t>(trampoline +
            sizeof(kUpdateDirtyInteractionsBytes)) - 5);
    FlushInstructionCache(GetCurrentProcess(), trampoline,
        sizeof(kUpdateDirtyInteractionsBytes) + 5);
    g_dirtyInteractionTrampoline = trampoline;
    if (!WriteJump(source, HookUpdateDirtyInteractions,
        sizeof(kUpdateDirtyInteractionsBytes))) {
        const DWORD error = GetLastError();
        g_dirtyInteractionTrampoline = 0;
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderProtectFailed, error);
    }
    g_dirtyInteractionUnityBase = unityBase;
    g_dirtyInteractionInstalled = true;
    InterlockedExchange(&g_dirtyInteractionAction,
        DirtyInteractionOrderIdle);
    InterlockedExchange(&g_dirtyInteractionResult,
        DirtyInteractionOrderNotCaptured);
    InterlockedExchange(&g_dirtyInteractionError, ERROR_INVALID_STATE);
    g_dirtyInteractionRestoreMode = DirtyInteractionRestoreNone;
    g_dirtyInteractionMatchedCount = 0;
    g_dirtyInteractionCapturedOnlyCount = 0;
    g_dirtyInteractionLiveOnlyCount = 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    receipt->result = DirtyInteractionOrderOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

static int ReadDirtyInteractionOrderStatus(uintptr_t unityBase,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderNotInstalled, ERROR_INVALID_STATE);
    return 1;
}

static int ArmDirtyInteractionOrderCapture(uintptr_t unityBase,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderNotInstalled, ERROR_INVALID_STATE);
    if (g_dirtyInteractionAction != DirtyInteractionOrderIdle)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderAlreadyArmed, ERROR_BUSY);
    g_dirtyInteractionNPhaseCore = 0;
    g_dirtyInteractionSet = 0;
    g_dirtyInteractionEntries = 0;
    g_dirtyInteractionEntriesNext = 0;
    g_dirtyInteractionHash = 0;
    g_dirtyInteractionEntriesCapacity = 0;
    g_dirtyInteractionHashSize = 0;
    g_dirtyInteractionCount = 0;
    g_dirtyInteractionOrderHashBefore = 0;
    g_dirtyInteractionOrderHashAfter = 0;
    g_dirtyInteractionRestoreMode = DirtyInteractionRestoreNone;
    g_dirtyInteractionMatchedCount = 0;
    g_dirtyInteractionCapturedOnlyCount = 0;
    g_dirtyInteractionLiveOnlyCount = 0;
    InterlockedExchange(&g_dirtyInteractionResult,
        DirtyInteractionOrderPending);
    InterlockedExchange(&g_dirtyInteractionError, ERROR_IO_PENDING);
    MemoryBarrier();
    InterlockedExchange(&g_dirtyInteractionAction,
        DirtyInteractionOrderCapture);
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    return 1;
}

static int CopyDirtyInteractionOrderCapture(uintptr_t unityBase,
    DirtyInteractionKey* keys, uint32_t capacity,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderNotInstalled, ERROR_INVALID_STATE);
    if (g_dirtyInteractionAction != DirtyInteractionOrderIdle ||
        g_dirtyInteractionResult != DirtyInteractionOrderOk ||
        g_dirtyInteractionCaptures == 0)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderNotCaptured, ERROR_INVALID_STATE);
    if (capacity < g_dirtyInteractionCount)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER);
    if (g_dirtyInteractionCount != 0 && (!keys ||
        !Writable(keys, g_dirtyInteractionCount *
            sizeof(DirtyInteractionKey))))
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderBadArgument, ERROR_INVALID_PARAMETER);
    CopyBytes(keys, g_dirtyInteractionKeys,
        g_dirtyInteractionCount * sizeof(DirtyInteractionKey));
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    receipt->result = DirtyInteractionOrderOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

static int ArmDirtyInteractionOrderRestore(uintptr_t unityBase,
    uintptr_t expectedNPhaseCore, uintptr_t expectedEntries,
    uintptr_t expectedEntriesNext, uintptr_t expectedHash,
    uint32_t expectedEntriesCapacity, uint32_t expectedHashSize,
    const DirtyInteractionKey* keys, uint32_t count, uint32_t restoreMode,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderNotInstalled, ERROR_INVALID_STATE);
    if (g_dirtyInteractionAction != DirtyInteractionOrderIdle)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderAlreadyArmed, ERROR_BUSY);
    if (!expectedNPhaseCore || !expectedEntries || !expectedEntriesNext ||
        !expectedHash || expectedEntriesCapacity == 0 ||
        expectedEntriesCapacity > kMaximumDirtyInteractions ||
        expectedHashSize == 0 ||
        expectedHashSize > kMaximumDirtyHashSize ||
        (expectedHashSize & (expectedHashSize - 1)) != 0 ||
        (restoreMode != DirtyInteractionRestoreExact &&
            restoreMode != DirtyInteractionRestoreProjection) ||
        count > expectedEntriesCapacity ||
        (count != 0 && (!keys || !Readable(keys,
            count * sizeof(DirtyInteractionKey)))))
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderBadArgument, ERROR_INVALID_PARAMETER);
    for (uint32_t i = 0; i < count; ++i) {
        if (!keys[i].elementLow || !keys[i].elementHigh ||
            keys[i].elementLow >= keys[i].elementHigh ||
            !keys[i].primaryVtable || keys[i].interactionType > 5)
            return FailDirtyInteractionOrder(receipt,
                DirtyInteractionOrderInvalidEntry, ERROR_INVALID_DATA);
        for (uint32_t prior = 0; prior < i; ++prior)
            if (SameDirtyInteractionKey(keys[prior], keys[i]))
                return FailDirtyInteractionOrder(receipt,
                    DirtyInteractionOrderInvalidEntry, ERROR_DUP_NAME);
    }
    CopyBytes(g_dirtyInteractionKeys, keys,
        count * sizeof(DirtyInteractionKey));
    g_dirtyInteractionNPhaseCore = expectedNPhaseCore;
    g_dirtyInteractionSet = expectedNPhaseCore + 0x44;
    g_dirtyInteractionEntries = expectedEntries;
    g_dirtyInteractionEntriesNext = expectedEntriesNext;
    g_dirtyInteractionHash = expectedHash;
    g_dirtyInteractionEntriesCapacity = expectedEntriesCapacity;
    g_dirtyInteractionHashSize = expectedHashSize;
    g_dirtyInteractionCount = count;
    g_dirtyInteractionOrderHashBefore = 0;
    g_dirtyInteractionOrderHashAfter =
        DirtyInteractionKeyOrderHash(g_dirtyInteractionKeys, count);
    g_dirtyInteractionRestoreMode = restoreMode;
    g_dirtyInteractionMatchedCount = 0;
    g_dirtyInteractionCapturedOnlyCount = 0;
    g_dirtyInteractionLiveOnlyCount = 0;
    InterlockedExchange(&g_dirtyInteractionResult,
        DirtyInteractionOrderPending);
    InterlockedExchange(&g_dirtyInteractionError, ERROR_IO_PENDING);
    MemoryBarrier();
    InterlockedExchange(&g_dirtyInteractionAction,
        DirtyInteractionOrderRestore);
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    return 1;
}

static int CancelDirtyInteractionOrder(uintptr_t unityBase,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderNotInstalled, ERROR_INVALID_STATE);
    const LONG action = InterlockedExchange(&g_dirtyInteractionAction,
        DirtyInteractionOrderIdle);
    // Cancel only invalidates work that was actually pending.  In particular,
    // the managed reset path may call cancel after a one-shot operation has
    // already completed; that idle no-op must not erase the completed receipt
    // or make a valid capture appear unavailable.
    if (action != DirtyInteractionOrderIdle) {
        InterlockedExchange(&g_dirtyInteractionResult,
            DirtyInteractionOrderNotCaptured);
        InterlockedExchange(&g_dirtyInteractionError, ERROR_CANCELLED);
        g_dirtyInteractionRestoreMode = DirtyInteractionRestoreNone;
        g_dirtyInteractionMatchedCount = 0;
        g_dirtyInteractionCapturedOnlyCount = 0;
        g_dirtyInteractionLiveOnlyCount = 0;
    }
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    return 1;
}

static int UninstallDirtyInteractionOrderHook(uintptr_t unityBase,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase)
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderNotInstalled, ERROR_INVALID_STATE);
    uint8_t* source =
        reinterpret_cast<uint8_t*>(unityBase + kUpdateDirtyInteractionsRva);
    if (!Readable(source, sizeof(kUpdateDirtyInteractionsBytes)) ||
        !HasDirtyInteractionJump(source))
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderPatchChanged, ERROR_INVALID_STATE);
    DWORD oldProtect = 0;
    if (!VirtualProtect(source, sizeof(kUpdateDirtyInteractionsBytes),
        PAGE_EXECUTE_READWRITE, &oldProtect))
        return FailDirtyInteractionOrder(receipt,
            DirtyInteractionOrderProtectFailed, GetLastError());
    CopyBytes(source, g_dirtyInteractionOriginal,
        sizeof(kUpdateDirtyInteractionsBytes));
    FlushInstructionCache(GetCurrentProcess(), source,
        sizeof(kUpdateDirtyInteractionsBytes));
    DWORD ignored = 0;
    VirtualProtect(source, sizeof(kUpdateDirtyInteractionsBytes),
        oldProtect, &ignored);
    void* trampoline = g_dirtyInteractionTrampoline;
    g_dirtyInteractionTrampoline = 0;
    g_dirtyInteractionInstalled = false;
    g_dirtyInteractionUnityBase = 0;
    g_dirtyInteractionRestoreMode = DirtyInteractionRestoreNone;
    g_dirtyInteractionMatchedCount = 0;
    g_dirtyInteractionCapturedOnlyCount = 0;
    g_dirtyInteractionLiveOnlyCount = 0;
    InterlockedExchange(&g_dirtyInteractionAction,
        DirtyInteractionOrderIdle);
    if (trampoline) VirtualFree(trampoline, 0, MEM_RELEASE);
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    receipt->result = DirtyInteractionOrderOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

static bool HasObserverJump(const void* source) {
    const uint8_t* bytes = static_cast<const uint8_t*>(source);
    if (bytes[0] != 0xE9 || bytes[5] != 0x90) return false;
    const int32_t displacement = *reinterpret_cast<const int32_t*>(bytes + 1);
    const uintptr_t destination = reinterpret_cast<uintptr_t>(source) + 5 + displacement;
    return destination == reinterpret_cast<uintptr_t>(HookCreateContactManagerContext);
}

static int InstallContactManagerContextObserver(uintptr_t unityBase,
    ContactContextObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactContextObserverReceipt(receipt, unityBase);
    if (!unityBase)
        return FailContactContextObserver(receipt, ContactContextObserverBadArgument,
            ERROR_INVALID_PARAMETER);
    if (g_contactManagerContextObserverInstalled)
        return FailContactContextObserver(receipt, ContactContextObserverAlreadyInstalled,
            ERROR_ALREADY_EXISTS);
    uint8_t* source = reinterpret_cast<uint8_t*>(unityBase + kCreateContactManagerRva);
    if (!Readable(source, sizeof(kCreateContactManagerBytes)) ||
        !EqualBytes(source, kCreateContactManagerBytes, sizeof(kCreateContactManagerBytes)))
        return FailContactContextObserver(receipt, ContactContextObserverRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    uint8_t* trampoline = static_cast<uint8_t*>(VirtualAlloc(0,
        sizeof(kCreateContactManagerBytes) + 5, MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE));
    if (!trampoline)
        return FailContactContextObserver(receipt, ContactContextObserverAllocationFailed,
            GetLastError());
    CopyBytes(g_contactManagerContextOriginal, source, sizeof(kCreateContactManagerBytes));
    CopyBytes(trampoline, source, sizeof(kCreateContactManagerBytes));
    trampoline[sizeof(kCreateContactManagerBytes)] = 0xE9;
    *reinterpret_cast<int32_t*>(trampoline + sizeof(kCreateContactManagerBytes) + 1) =
        static_cast<int32_t>(reinterpret_cast<uintptr_t>(source + sizeof(kCreateContactManagerBytes)) -
        reinterpret_cast<uintptr_t>(trampoline + sizeof(kCreateContactManagerBytes)) - 5);
    FlushInstructionCache(GetCurrentProcess(), trampoline,
        sizeof(kCreateContactManagerBytes) + 5);
    g_contactManagerContextTrampoline = trampoline;
    if (!WriteJump(source, HookCreateContactManagerContext,
        sizeof(kCreateContactManagerBytes))) {
        const DWORD error = GetLastError();
        g_contactManagerContextTrampoline = 0;
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return FailContactContextObserver(receipt, ContactContextObserverProtectFailed, error);
    }
    g_observerUnityBase = unityBase;
    g_contactManagerContextObserverInstalled = true;
    InitializeContactContextObserverReceipt(receipt, unityBase);
    receipt->result = ContactContextObserverOk;
    return 1;
}

static int ReadContactManagerContextObserver(uintptr_t unityBase,
    ContactContextObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactContextObserverReceipt(receipt, unityBase);
    if (!g_contactManagerContextObserverInstalled || unityBase != g_observerUnityBase)
        return FailContactContextObserver(receipt, ContactContextObserverNotInstalled,
            ERROR_INVALID_STATE);
    receipt->result = ContactContextObserverOk;
    return 1;
}

static int UninstallContactManagerContextObserver(uintptr_t unityBase,
    ContactContextObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactContextObserverReceipt(receipt, unityBase);
    if (!g_contactManagerContextObserverInstalled || unityBase != g_observerUnityBase)
        return FailContactContextObserver(receipt, ContactContextObserverNotInstalled,
            ERROR_INVALID_STATE);
    uint8_t* source = reinterpret_cast<uint8_t*>(unityBase + kCreateContactManagerRva);
    if (!Readable(source, sizeof(kCreateContactManagerBytes)) || !HasObserverJump(source))
        return FailContactContextObserver(receipt, ContactContextObserverPatchChanged,
            ERROR_INVALID_STATE);
    DWORD oldProtect = 0;
    if (!VirtualProtect(source, sizeof(kCreateContactManagerBytes),
        PAGE_EXECUTE_READWRITE, &oldProtect))
        return FailContactContextObserver(receipt, ContactContextObserverProtectFailed,
            GetLastError());
    CopyBytes(source, g_contactManagerContextOriginal,
        sizeof(kCreateContactManagerBytes));
    FlushInstructionCache(GetCurrentProcess(), source,
        sizeof(kCreateContactManagerBytes));
    DWORD ignored = 0;
    VirtualProtect(source, sizeof(kCreateContactManagerBytes), oldProtect, &ignored);
    void* trampoline = g_contactManagerContextTrampoline;
    g_contactManagerContextTrampoline = 0;
    g_contactManagerContextObserverInstalled = false;
    g_observerUnityBase = 0;
    if (trampoline) VirtualFree(trampoline, 0, MEM_RELEASE);
    InitializeContactContextObserverReceipt(receipt, unityBase);
    receipt->result = ContactContextObserverOk;
    return 1;
}

static bool ReadContactPool(uintptr_t context, uintptr_t*& freeArray, uint32_t& freeCount,
    ContactPoolReceipt* receipt) {
    // Unity's shipped PxsContext embeds the contact-manager pool at +0x2B8.
    // createContactManager reads its free-array pointer at +0x2C8 and its
    // LIFO count at +0x2CC. These offsets are guarded by the same exact player
    // revision used by the actor rebuild helper.
    if (!context) return FailContactPool(receipt, ContactPoolBadArgument, ERROR_INVALID_PARAMETER) != 0;
    if (!Readable(reinterpret_cast<const void*>(context + 0x2C8), 8))
        return FailContactPool(receipt, ContactPoolUnreadableContext, ERROR_NOACCESS) != 0;
    freeArray = reinterpret_cast<uintptr_t*>(
        *reinterpret_cast<const uintptr_t*>(context + 0x2C8));
    freeCount = *reinterpret_cast<const uint32_t*>(context + 0x2CC);
    receipt->freeArray = reinterpret_cast<uintptr_t>(freeArray);
    receipt->freeCount = freeCount;
    if (!freeCount || freeCount > kMaximumContactManagers)
        return FailContactPool(receipt, ContactPoolInvalidCount, ERROR_INVALID_DATA) != 0;
    if (!Readable(freeArray, freeCount * sizeof(uintptr_t)))
        return FailContactPool(receipt, ContactPoolUnreadableArray, ERROR_NOACCESS) != 0;
    for (uint32_t i = 0; i < freeCount; ++i) {
        if (!freeArray[i] || !Readable(reinterpret_cast<const void*>(freeArray[i] + 0x4C), 4))
            return FailContactPool(receipt, ContactPoolInvalidEntry, ERROR_INVALID_DATA) != 0;
        for (uint32_t j = 0; j < i; ++j)
            if (freeArray[j] == freeArray[i])
                return FailContactPool(receipt, ContactPoolInvalidEntry, ERROR_DUP_NAME) != 0;
    }
    return true;
}

static void RecordContactPoolOrder(ContactPoolReceipt* receipt, const uintptr_t* freeArray,
    uint32_t freeCount, bool after) {
    const uint32_t hash = OrderHash(freeArray, freeCount);
    if (after) receipt->orderHashAfter = hash;
    else receipt->orderHashBefore = hash;
    for (uint32_t i = 0; i < 16; ++i)
        receipt->top[i] = i < freeCount ? freeArray[freeCount - 1 - i] : 0;
}

static int CaptureContactPool(uintptr_t context, ContactPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactPoolReceipt(receipt, context);
    uintptr_t* freeArray = 0;
    uint32_t freeCount = 0;
    if (!ReadContactPool(context, freeArray, freeCount, receipt)) return 0;
    CopyWords(g_contactPoolSnapshot, freeArray, freeCount);
    g_contactPoolContext = context;
    g_contactPoolFreeArray = reinterpret_cast<uintptr_t>(freeArray);
    g_contactPoolFreeCount = freeCount;
    g_contactPoolOrderHash = OrderHash(freeArray, freeCount);
    g_contactPoolCaptured = true;
    RecordContactPoolOrder(receipt, freeArray, freeCount, false);
    receipt->orderHashAfter = receipt->orderHashBefore;
    receipt->result = ContactPoolOk;
    return 1;
}

static int RestoreContactPool(uintptr_t context, ContactPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactPoolReceipt(receipt, context);
    if (!g_contactPoolCaptured)
        return FailContactPool(receipt, ContactPoolNoSnapshot, ERROR_INVALID_STATE);
    uintptr_t* freeArray = 0;
    uint32_t freeCount = 0;
    if (!ReadContactPool(context, freeArray, freeCount, receipt)) return 0;
    RecordContactPoolOrder(receipt, freeArray, freeCount, false);
    if (context != g_contactPoolContext)
        return FailContactPool(receipt, ContactPoolContextChanged, ERROR_INVALID_STATE);
    if (reinterpret_cast<uintptr_t>(freeArray) != g_contactPoolFreeArray)
        return FailContactPool(receipt, ContactPoolArrayChanged, ERROR_INVALID_STATE);
    if (freeCount != g_contactPoolFreeCount)
        return FailContactPool(receipt, ContactPoolCountChanged, ERROR_INVALID_STATE);
    // Restoring order is safe only when the live free list contains exactly
    // the same unique manager pointers as the snapshot. This proves we change
    // no active/free membership and no allocation count.
    for (uint32_t i = 0; i < freeCount; ++i) {
        bool found = false;
        for (uint32_t j = 0; j < freeCount; ++j)
            if (freeArray[i] == g_contactPoolSnapshot[j]) { found = true; break; }
        if (!found)
            return FailContactPool(receipt, ContactPoolMembershipChanged, ERROR_INVALID_STATE);
    }
    if (!Writable(freeArray, freeCount * sizeof(uintptr_t)))
        return FailContactPool(receipt, ContactPoolArrayNotWritable, ERROR_NOACCESS);
    CopyWords(freeArray, g_contactPoolSnapshot, freeCount);
    MemoryBarrier();
    RecordContactPoolOrder(receipt, freeArray, freeCount, true);
    if (receipt->orderHashAfter != g_contactPoolOrderHash)
        return FailContactPool(receipt, ContactPoolWriteVerificationFailed, ERROR_WRITE_FAULT);
    receipt->result = ContactPoolOk;
    return 1;
}

// Copies the complete ordered free list into storage owned by the caller.
// Unlike CaptureContactPool, this does not retain process-global snapshot
// state in the helper, so the authoring layer can keep one sidecar per
// checkpoint frame without changing any live allocator field.
static int CaptureContactPoolSnapshot(uintptr_t context, uintptr_t* snapshot,
    uint32_t capacity, ContactPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactPoolReceipt(receipt, context);
    uintptr_t* freeArray = 0;
    uint32_t freeCount = 0;
    if (!ReadContactPool(context, freeArray, freeCount, receipt)) return 0;
    if (!snapshot || capacity < freeCount)
        return FailContactPool(receipt, ContactPoolBadArgument,
            ERROR_INSUFFICIENT_BUFFER);
    if (!Writable(snapshot, freeCount * sizeof(uintptr_t)))
        return FailContactPool(receipt, ContactPoolArrayNotWritable,
            ERROR_NOACCESS);
    CopyWords(snapshot, freeArray, freeCount);
    RecordContactPoolOrder(receipt, freeArray, freeCount, false);
    receipt->orderHashAfter = receipt->orderHashBefore;
    receipt->result = ContactPoolOk;
    return 1;
}

// Restores an explicitly supplied ordered free list. The expected array
// address, count, unique membership, writability and final order are all
// checked before success. Thus this changes only LIFO order, never which
// contact managers are active/free or how many exist.
static int RestoreContactPoolSnapshot(uintptr_t context,
    uintptr_t expectedFreeArray, const uintptr_t* snapshot,
    uint32_t snapshotCount, ContactPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactPoolReceipt(receipt, context);
    if (!snapshot || !snapshotCount || snapshotCount > kMaximumContactManagers ||
        !Readable(snapshot, snapshotCount * sizeof(uintptr_t)))
        return FailContactPool(receipt, ContactPoolBadArgument,
            ERROR_INVALID_PARAMETER);
    uintptr_t* freeArray = 0;
    uint32_t freeCount = 0;
    if (!ReadContactPool(context, freeArray, freeCount, receipt)) return 0;
    RecordContactPoolOrder(receipt, freeArray, freeCount, false);
    if (reinterpret_cast<uintptr_t>(freeArray) != expectedFreeArray)
        return FailContactPool(receipt, ContactPoolArrayChanged,
            ERROR_INVALID_STATE);
    if (freeCount != snapshotCount)
        return FailContactPool(receipt, ContactPoolCountChanged,
            ERROR_INVALID_STATE);
    for (uint32_t i = 0; i < snapshotCount; ++i) {
        if (!snapshot[i] ||
            !Readable(reinterpret_cast<const void*>(snapshot[i] + 0x4C), 4))
            return FailContactPool(receipt, ContactPoolInvalidEntry,
                ERROR_INVALID_DATA);
        for (uint32_t j = 0; j < i; ++j)
            if (snapshot[j] == snapshot[i])
                return FailContactPool(receipt, ContactPoolInvalidEntry,
                    ERROR_DUP_NAME);
    }
    for (uint32_t i = 0; i < freeCount; ++i) {
        bool found = false;
        for (uint32_t j = 0; j < snapshotCount; ++j)
            if (freeArray[i] == snapshot[j]) { found = true; break; }
        if (!found)
            return FailContactPool(receipt, ContactPoolMembershipChanged,
                ERROR_INVALID_STATE);
    }
    if (!Writable(freeArray, freeCount * sizeof(uintptr_t)))
        return FailContactPool(receipt, ContactPoolArrayNotWritable,
            ERROR_NOACCESS);
    CopyWords(freeArray, snapshot, freeCount);
    MemoryBarrier();
    RecordContactPoolOrder(receipt, freeArray, freeCount, true);
    for (uint32_t i = 0; i < freeCount; ++i)
        if (freeArray[i] != snapshot[i])
            return FailContactPool(receipt,
                ContactPoolWriteVerificationFailed, ERROR_WRITE_FAULT);
    if (receipt->orderHashAfter != OrderHash(snapshot, snapshotCount))
        return FailContactPool(receipt, ContactPoolWriteVerificationFailed,
            ERROR_WRITE_FAULT);
    receipt->result = ContactPoolOk;
    return 1;
}

struct ManifoldPoolLayout {
    uint32_t contextOffset;
    uint32_t elementSize;
};

static bool ReadManifoldPoolLayout(uint32_t poolKind,
    ManifoldPoolLayout& layout) {
    if (poolKind == 0) {
        layout.contextOffset = 0x2E4;
        layout.elementSize = 0xF0;
        return true;
    }
    if (poolKind == 1) {
        layout.contextOffset = 0x40C;
        layout.elementSize = 0x60;
        return true;
    }
    return false;
}

static bool ManifoldPoolRevisionMatches(uintptr_t unityBase) {
    if (!unityBase) return false;
    const void* largeAllocator = reinterpret_cast<const void*>(
        unityBase + kLargeManifoldPoolRva);
    const void* sphereAllocator = reinterpret_cast<const void*>(
        unityBase + kSphereManifoldPoolRva);
    const void* largeSlab = reinterpret_cast<const void*>(
        unityBase + kLargeManifoldPoolSlabRva);
    const void* sphereSlab = reinterpret_cast<const void*>(
        unityBase + kSphereManifoldPoolSlabRva);
    const void* largeCallsite = reinterpret_cast<const void*>(
        unityBase + kLargeManifoldCallsiteRva);
    const void* sphereCallsite = reinterpret_cast<const void*>(
        unityBase + kSphereManifoldCallsiteRva);
    return Readable(largeAllocator, sizeof(kLargeManifoldPoolBytes)) &&
        EqualBytes(largeAllocator, kLargeManifoldPoolBytes,
            sizeof(kLargeManifoldPoolBytes)) &&
        Readable(sphereAllocator, sizeof(kSphereManifoldPoolBytes)) &&
        EqualBytes(sphereAllocator, kSphereManifoldPoolBytes,
            sizeof(kSphereManifoldPoolBytes)) &&
        Readable(largeSlab, sizeof(kLargeManifoldPoolSlabBytes)) &&
        EqualBytes(largeSlab, kLargeManifoldPoolSlabBytes,
            sizeof(kLargeManifoldPoolSlabBytes)) &&
        Readable(sphereSlab, sizeof(kSphereManifoldPoolSlabBytes)) &&
        EqualBytes(sphereSlab, kSphereManifoldPoolSlabBytes,
            sizeof(kSphereManifoldPoolSlabBytes)) &&
        Readable(largeCallsite, sizeof(kLargeManifoldCallsiteBytes)) &&
        EqualBytes(largeCallsite, kLargeManifoldCallsiteBytes,
            sizeof(kLargeManifoldCallsiteBytes)) &&
        Readable(sphereCallsite, sizeof(kSphereManifoldCallsiteBytes)) &&
        EqualBytes(sphereCallsite, kSphereManifoldCallsiteBytes,
            sizeof(kSphereManifoldCallsiteBytes));
}

static void RecordManifoldPoolOrder(ManifoldPoolReceipt* receipt,
    const uintptr_t* order, uint32_t count, bool after) {
    const uint32_t hash = OrderHash(order, count);
    if (after) {
        receipt->orderHashAfter = hash;
        for (uint32_t i = 0; i < 16; ++i)
            receipt->topAfter[i] = i < count ? order[i] : 0;
    } else {
        receipt->orderHashBefore = hash;
        for (uint32_t i = 0; i < 16; ++i)
            receipt->topBefore[i] = i < count ? order[i] : 0;
    }
}

static bool ReadManifoldPool(uintptr_t context, uint32_t poolKind,
    uintptr_t* order, uint32_t& count, ManifoldPoolReceipt* receipt) {
    ManifoldPoolLayout layout = {};
    if (!ReadManifoldPoolLayout(poolKind, layout))
        return FailManifoldPool(receipt, ManifoldPoolInvalidKind,
            ERROR_INVALID_PARAMETER) != 0;
    if (!context)
        return FailManifoldPool(receipt, ManifoldPoolBadArgument,
            ERROR_INVALID_PARAMETER) != 0;
    if (!Readable(reinterpret_cast<const void*>(context), sizeof(uintptr_t)))
        return FailManifoldPool(receipt, ManifoldPoolUnreadableContext,
            ERROR_NOACCESS) != 0;

    const uintptr_t pool = context + layout.contextOffset;
    receipt->pool = pool;
    receipt->elementSize = layout.elementSize;
    if (!Readable(reinterpret_cast<const void*>(pool + 0x114), 0x14))
        return FailManifoldPool(receipt, ManifoldPoolUnreadablePool,
            ERROR_NOACCESS) != 0;

    receipt->elementsPerSlab = *reinterpret_cast<const uint32_t*>(pool + 0x114);
    receipt->used = *reinterpret_cast<const uint32_t*>(pool + 0x118);
    receipt->unreleased = *reinterpret_cast<const uint32_t*>(pool + 0x11C);
    receipt->slabSize = *reinterpret_cast<const uint32_t*>(pool + 0x120);
    receipt->freeHeadBefore = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    receipt->freeHeadAfter = receipt->freeHeadBefore;

    const uint64_t expectedSlabSize = static_cast<uint64_t>(
        receipt->elementsPerSlab) * layout.elementSize;
    if (!receipt->elementsPerSlab || expectedSlabSize > UINT32_MAX ||
        receipt->slabSize != static_cast<uint32_t>(expectedSlabSize))
        return FailManifoldPool(receipt, ManifoldPoolInvalidMetadata,
            ERROR_INVALID_DATA) != 0;

    count = 0;
    uintptr_t current = receipt->freeHeadBefore;
    while (current) {
        if (count >= kMaximumManifolds)
            return FailManifoldPool(receipt, ManifoldPoolInvalidCount,
                ERROR_INSUFFICIENT_BUFFER) != 0;
        if (!Readable(reinterpret_cast<const void*>(current), sizeof(uintptr_t)))
            return FailManifoldPool(receipt, ManifoldPoolInvalidNode,
                ERROR_NOACCESS) != 0;
        for (uint32_t i = 0; i < count; ++i)
            if (order[i] == current)
                return FailManifoldPool(receipt, ManifoldPoolDuplicateNode,
                    ERROR_DUP_NAME) != 0;
        order[count++] = current;
        current = *reinterpret_cast<const uintptr_t*>(current);
    }
    receipt->traversedCount = count;
    return true;
}

static int CaptureManifoldPoolSnapshot(uintptr_t unityBase,
    uintptr_t context, uint32_t poolKind, uintptr_t* snapshot,
    uint32_t capacity, ManifoldPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeManifoldPoolReceipt(receipt, unityBase, context, poolKind);
    if (!ManifoldPoolRevisionMatches(unityBase))
        return FailManifoldPool(receipt, ManifoldPoolRevisionMismatch,
            ERROR_REVISION_MISMATCH);

    uintptr_t order[kMaximumManifolds] = {};
    uint32_t count = 0;
    if (!ReadManifoldPool(context, poolKind, order, count, receipt)) return 0;
    if (capacity < count)
        return FailManifoldPool(receipt, ManifoldPoolCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER);
    if (count && !snapshot)
        return FailManifoldPool(receipt, ManifoldPoolBadArgument,
            ERROR_INVALID_PARAMETER);
    if (count && !Writable(snapshot, count * sizeof(uintptr_t)))
        return FailManifoldPool(receipt, ManifoldPoolBufferNotWritable,
            ERROR_NOACCESS);

    if (count) CopyWords(snapshot, order, count);
    RecordManifoldPoolOrder(receipt, order, count, false);
    RecordManifoldPoolOrder(receipt, order, count, true);
    receipt->result = ManifoldPoolOk;
    return 1;
}

static int RestoreManifoldPoolSnapshot(uintptr_t unityBase,
    uintptr_t context, uint32_t poolKind, uintptr_t expectedPool,
    const uintptr_t* snapshot, uint32_t snapshotCount,
    ManifoldPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeManifoldPoolReceipt(receipt, unityBase, context, poolKind);
    if (snapshotCount > kMaximumManifolds || (snapshotCount && !snapshot))
        return FailManifoldPool(receipt, ManifoldPoolBadArgument,
            ERROR_INVALID_PARAMETER);
    if (snapshotCount &&
        !Readable(snapshot, snapshotCount * sizeof(uintptr_t)))
        return FailManifoldPool(receipt, ManifoldPoolBufferUnreadable,
            ERROR_NOACCESS);
    if (!ManifoldPoolRevisionMatches(unityBase))
        return FailManifoldPool(receipt, ManifoldPoolRevisionMismatch,
            ERROR_REVISION_MISMATCH);

    uintptr_t liveOrder[kMaximumManifolds] = {};
    uint32_t liveCount = 0;
    if (!ReadManifoldPool(context, poolKind, liveOrder, liveCount, receipt))
        return 0;
    RecordManifoldPoolOrder(receipt, liveOrder, liveCount, false);
    if (!expectedPool || receipt->pool != expectedPool)
        return FailManifoldPool(receipt, ManifoldPoolIdentityChanged,
            ERROR_INVALID_STATE);
    if (liveCount != snapshotCount)
        return FailManifoldPool(receipt, ManifoldPoolCountChanged,
            ERROR_INVALID_STATE);

    for (uint32_t i = 0; i < snapshotCount; ++i) {
        if (!snapshot[i] ||
            !Readable(reinterpret_cast<const void*>(snapshot[i]),
                sizeof(uintptr_t)))
            return FailManifoldPool(receipt, ManifoldPoolInvalidNode,
                ERROR_NOACCESS);
        for (uint32_t j = 0; j < i; ++j)
            if (snapshot[j] == snapshot[i])
                return FailManifoldPool(receipt, ManifoldPoolDuplicateNode,
                    ERROR_DUP_NAME);
    }
    for (uint32_t i = 0; i < liveCount; ++i) {
        bool found = false;
        for (uint32_t j = 0; j < snapshotCount; ++j)
            if (liveOrder[i] == snapshot[j]) { found = true; break; }
        if (!found)
            return FailManifoldPool(receipt, ManifoldPoolMembershipChanged,
                ERROR_INVALID_STATE);
    }

    if (!Writable(reinterpret_cast<void*>(receipt->pool + 0x124),
        sizeof(uintptr_t)))
        return FailManifoldPool(receipt, ManifoldPoolHeadNotWritable,
            ERROR_NOACCESS);
    for (uint32_t i = 0; i < snapshotCount; ++i)
        if (!Writable(reinterpret_cast<void*>(snapshot[i]), sizeof(uintptr_t)))
            return FailManifoldPool(receipt, ManifoldPoolNodeNotWritable,
                ERROR_NOACCESS);

    for (uint32_t i = 0; i < snapshotCount; ++i)
        *reinterpret_cast<uintptr_t*>(snapshot[i]) =
            i + 1 < snapshotCount ? snapshot[i + 1] : 0;
    *reinterpret_cast<uintptr_t*>(receipt->pool + 0x124) =
        snapshotCount ? snapshot[0] : 0;
    MemoryBarrier();

    receipt->freeHeadAfter = *reinterpret_cast<const uintptr_t*>(
        receipt->pool + 0x124);
    uintptr_t current = receipt->freeHeadAfter;
    for (uint32_t i = 0; i < snapshotCount; ++i) {
        if (current != snapshot[i] ||
            !Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t)))
            return FailManifoldPool(receipt,
                ManifoldPoolWriteVerificationFailed, ERROR_WRITE_FAULT);
        current = *reinterpret_cast<const uintptr_t*>(current);
    }
    if (current)
        return FailManifoldPool(receipt, ManifoldPoolWriteVerificationFailed,
            ERROR_WRITE_FAULT);
    if (!Readable(reinterpret_cast<const void*>(receipt->pool + 0x114), 0x10) ||
        *reinterpret_cast<const uint32_t*>(receipt->pool + 0x114) != receipt->elementsPerSlab ||
        *reinterpret_cast<const uint32_t*>(receipt->pool + 0x118) != receipt->used ||
        *reinterpret_cast<const uint32_t*>(receipt->pool + 0x11C) != receipt->unreleased ||
        *reinterpret_cast<const uint32_t*>(receipt->pool + 0x120) != receipt->slabSize)
        return FailManifoldPool(receipt, ManifoldPoolWriteVerificationFailed,
            ERROR_WRITE_FAULT);
    RecordManifoldPoolOrder(receipt, snapshot, snapshotCount, true);
    receipt->result = ManifoldPoolOk;
    return 1;
}

static int RebuildBatch(uintptr_t unityBase, const uintptr_t* rigidbodies,
    uint32_t count, RebuildReceipt* receipts) {
    if (!rigidbodies || !receipts || count == 0 || count > 4) return 0;
    for (uint32_t i = 0; i < count; ++i) {
        receipts[i] = {};
        receipts[i].apiVersion = kApiVersion;
        receipts[i].structSize = sizeof(RebuildReceipt);
        receipts[i].unityBase = unityBase;
        receipts[i].rigidbody = rigidbodies[i];
    }
    if (!unityBase) return Fail(&receipts[0], RebuildBadArgument, ERROR_INVALID_PARAMETER);

    const void* cleanupAddress = reinterpret_cast<const void*>(unityBase + kCleanupRva);
    const void* createAddress = reinterpret_cast<const void*>(unityBase + kCreateRva);
    if (!Readable(cleanupAddress, sizeof(kCleanupBytes)) ||
        !EqualBytes(cleanupAddress, kCleanupBytes, sizeof(kCleanupBytes)))
        return Fail(&receipts[0], RebuildCleanupRevisionMismatch, ERROR_REVISION_MISMATCH);
    if (!Readable(createAddress, sizeof(kCreateBytes)) ||
        !EqualBytes(createAddress, kCreateBytes, sizeof(kCreateBytes)))
        return Fail(&receipts[0], RebuildCreateRevisionMismatch, ERROR_REVISION_MISMATCH);

    for (uint32_t i = 0; i < count; ++i) {
        uintptr_t rigidbody = rigidbodies[i];
        if (!rigidbody) return Fail(&receipts[i], RebuildBadArgument, ERROR_INVALID_PARAMETER);
        if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x51))
            return Fail(&receipts[i], RebuildUnreadableRigidbody, ERROR_NOACCESS);
        receipts[i].actorBefore = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
        if (!receipts[i].actorBefore)
            return Fail(&receipts[i], RebuildMissingActor, ERROR_INVALID_STATE);
        for (uint32_t j = 0; j < i; ++j)
            if (rigidbodies[j] == rigidbody)
                return Fail(&receipts[i], RebuildBadArgument, ERROR_DUP_NAME);
    }

    RigidbodyInternal create = reinterpret_cast<RigidbodyInternal>(unityBase + kCreateRva);
    // First remove every target from the active PhysX scene. This tears down
    // the complete selected contact island before any target is reinserted.
    for (uint32_t i = 0; i < count; ++i) {
        uintptr_t rigidbody = rigidbodies[i];
        create(reinterpret_cast<void*>(rigidbody), false);
        receipts[i].actorAfterInactiveCreate = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
        if (!receipts[i].actorAfterInactiveCreate)
            return Fail(&receipts[i], RebuildInactiveCreateMissingActor, ERROR_INVALID_STATE);
        if (*reinterpret_cast<const uint8_t*>(rigidbody + 0x50) != 0)
            return Fail(&receipts[i], RebuildInactiveFlagMismatch, ERROR_INVALID_STATE);
    }
    // Then reinsert the full target set in one deterministic caller-supplied
    // order. Managed code re-registers capsules only after this loop completes.
    for (uint32_t i = 0; i < count; ++i) {
        uintptr_t rigidbody = rigidbodies[i];
        create(reinterpret_cast<void*>(rigidbody), true);
        receipts[i].actorAfterActiveCreate = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
        if (!receipts[i].actorAfterActiveCreate)
            return Fail(&receipts[i], RebuildActiveCreateMissingActor, ERROR_INVALID_STATE);
        if (*reinterpret_cast<const uint8_t*>(rigidbody + 0x50) == 0)
            return Fail(&receipts[i], RebuildActiveFlagMismatch, ERROR_INVALID_STATE);
        receipts[i].result = RebuildOk;
    }
    return 1;
}

static int SetExistingActorGlobalPose(uintptr_t unityBase, uintptr_t rigidbody,
    const RigidPose* pose, SetGlobalPoseReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(SetGlobalPoseReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    if (!unityBase || !rigidbody || !pose)
        return FailSetGlobalPose(receipt, SetGlobalPoseBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!Finite(*pose))
        return FailSetGlobalPose(receipt, SetGlobalPoseNonfinite,
            ERROR_INVALID_DATA);
    if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x38))
        return FailSetGlobalPose(receipt, SetGlobalPoseUnreadableRigidbody,
            ERROR_NOACCESS);
    const uintptr_t actor =
        *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    receipt->actor = actor;
    if (!actor || !Readable(reinterpret_cast<const void*>(actor), 0x18))
        return FailSetGlobalPose(receipt, SetGlobalPoseMissingActor,
            ERROR_INVALID_STATE);
    const void* address =
        reinterpret_cast<const void*>(unityBase + kNpSetGlobalPoseRva);
    if (!Readable(address, sizeof(kNpSetGlobalPoseBytes)) ||
        !EqualBytes(address, kNpSetGlobalPoseBytes,
            sizeof(kNpSetGlobalPoseBytes)))
        return FailSetGlobalPose(receipt, SetGlobalPoseRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    receipt->pose = *pose;
    NpRigidDynamicSetGlobalPose setGlobalPose =
        reinterpret_cast<NpRigidDynamicSetGlobalPose>(
            unityBase + kNpSetGlobalPoseRva);
    PhysxTransform nativePose = {};
    for (uint32_t i = 0; i < 4; ++i)
        nativePose.rotation[i] = pose->rotation[i];
    for (uint32_t i = 0; i < 3; ++i)
        nativePose.position[i] = pose->position[i];
    setGlobalPose(reinterpret_cast<void*>(actor), nativePose, false);
    receipt->result = SetGlobalPoseOk;
    return 1;
}

static int CaptureExistingBody2World(uintptr_t unityBase, uintptr_t rigidbody,
    Body2WorldCaptureReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(Body2WorldCaptureReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    BodyPoseState state = {};
    uint32_t error = ERROR_SUCCESS;
    const Body2WorldResult result = CaptureBodyPoseState(
        unityBase, rigidbody, &state, &error);
    receipt->actor = state.actor;
    receipt->scene = state.scene;
    receipt->controlState = state.controlState;
    receipt->bodyBufferFlags = state.bodyBufferFlags;
    receipt->simulationRunning = state.simulationRunning;
    receipt->physicsBuffering = state.physicsBuffering;
    receipt->actorPose = state.actorPose;
    receipt->body2Actor = state.body2Actor;
    receipt->bufferedBody2World = state.bufferedBody2World;
    receipt->coreBody2World = state.coreBody2World;
    receipt->bodySim = state.bodySim;
    receipt->wakeCounterBufferedBits = state.wakeCounterBufferedBits;
    receipt->wakeCounterCoreBits = state.wakeCounterCoreBits;
    receipt->bufferedIsSleeping = state.bufferedIsSleeping;
    receipt->bodySimActive = state.bodySimActive;
    receipt->bodyCore = state.bodyCore;
    receipt->bodyCoreBodySim = state.bodyCoreBodySim;
    receipt->bodyCoreFlags = state.bodyCoreFlags;
    receipt->simStateData = state.simStateData;
    receipt->simStateTargetValid = state.simStateTargetValid;
    receipt->interactionScene = state.interactionScene;
    receipt->scScene = state.scScene;
    receipt->sceneArrayIndex = state.sceneArrayIndex;
    receipt->bodySimInternalFlags = state.bodySimInternalFlags;
    receipt->velocityModState = state.velocityModState;
    receipt->islandHook = state.islandHook;
    receipt->activeBodiesData = state.activeBodiesData;
    receipt->activeBodiesCount = state.activeBodiesCount;
    receipt->activeBodiesCapacity = state.activeBodiesCapacity;
    receipt->activeTwoWayStart = state.activeTwoWayStart;
    receipt->activeBodyAtSceneIndex = state.activeBodyAtSceneIndex;
    receipt->activeBodiesHash = state.activeBodiesHash;
    receipt->islandManager = state.islandManager;
    receipt->islandNodeData = state.islandNodeData;
    receipt->islandNodeOwner = state.islandNodeOwner;
    receipt->islandNodeIslandId = state.islandNodeIslandId;
    receipt->islandNodeFlags = state.islandNodeFlags;
    receipt->kinematicBitmap = state.kinematicBitmap;
    receipt->kinematicChangeBitmap = state.kinematicChangeBitmap;
    receipt->notReadyBitmap = state.notReadyBitmap;
    receipt->notReadyChangeBitmap = state.notReadyChangeBitmap;
    receipt->kinematicBitmapMap = state.kinematicBitmapMap;
    receipt->kinematicChangeBitmapMap = state.kinematicChangeBitmapMap;
    receipt->notReadyBitmapMap = state.notReadyBitmapMap;
    receipt->notReadyChangeBitmapMap = state.notReadyChangeBitmapMap;
    receipt->kinematicBitmapWordCount = state.kinematicBitmapWordCount;
    receipt->kinematicChangeBitmapWordCount = state.kinematicChangeBitmapWordCount;
    receipt->notReadyBitmapWordCount = state.notReadyBitmapWordCount;
    receipt->notReadyChangeBitmapWordCount = state.notReadyChangeBitmapWordCount;
    receipt->kinematicBitmapWord = state.kinematicBitmapWord;
    receipt->kinematicChangeBitmapWord = state.kinematicChangeBitmapWord;
    receipt->notReadyBitmapWord = state.notReadyBitmapWord;
    receipt->notReadyChangeBitmapWord = state.notReadyChangeBitmapWord;
    receipt->kinematicBitmapBit = state.kinematicBitmapBit;
    receipt->kinematicChangeBitmapBit = state.kinematicChangeBitmapBit;
    receipt->notReadyBitmapBit = state.notReadyBitmapBit;
    receipt->notReadyChangeBitmapBit = state.notReadyChangeBitmapBit;
    receipt->islandManagerFlags = state.islandManagerFlags;
    receipt->sleepBodiesData = state.sleepBodiesData;
    receipt->sleepBodiesCount = state.sleepBodiesCount;
    receipt->sleepBodiesCapacity = state.sleepBodiesCapacity;
    receipt->sleepBodiesHash = state.sleepBodiesHash;
    receipt->sleepBodiesIndex = state.sleepBodiesIndex;
    receipt->wokeBodiesData = state.wokeBodiesData;
    receipt->wokeBodiesCount = state.wokeBodiesCount;
    receipt->wokeBodiesCapacity = state.wokeBodiesCapacity;
    receipt->wokeBodiesHash = state.wokeBodiesHash;
    receipt->wokeBodiesIndex = state.wokeBodiesIndex;
    receipt->wokeBodyListValid = state.wokeBodyListValid;
    receipt->sleepBodyListValid = state.sleepBodyListValid;
    receipt->lifecycleStable = state.lifecycleStable;
    if (result != Body2WorldOk)
        return FailBody2WorldCapture(receipt, result, error);
    receipt->result = Body2WorldOk;
    return 1;
}

static void PopulateBody2WorldRestoreBefore(Body2WorldRestoreReceipt* receipt,
    const BodyPoseState& state) {
    receipt->actor = state.actor;
    receipt->sceneBefore = state.scene;
    receipt->controlStateBefore = state.controlState;
    receipt->bodyBufferFlagsBefore = state.bodyBufferFlags;
    receipt->simulationRunningBefore = state.simulationRunning;
    receipt->physicsBufferingBefore = state.physicsBuffering;
    receipt->actorPoseBefore = state.actorPose;
    receipt->body2ActorBefore = state.body2Actor;
    receipt->bufferedBody2WorldBefore = state.bufferedBody2World;
    receipt->coreBody2WorldBefore = state.coreBody2World;
    receipt->bodySimBefore = state.bodySim;
    receipt->wakeCounterBufferedBitsBefore = state.wakeCounterBufferedBits;
    receipt->wakeCounterCoreBitsBefore = state.wakeCounterCoreBits;
    receipt->bufferedIsSleepingBefore = state.bufferedIsSleeping;
    receipt->bodySimActiveBefore = state.bodySimActive;
}

static void PopulateBody2WorldRestoreAfter(Body2WorldRestoreReceipt* receipt,
    const BodyPoseState& state) {
    receipt->sceneAfter = state.scene;
    receipt->controlStateAfter = state.controlState;
    receipt->bodyBufferFlagsAfter = state.bodyBufferFlags;
    receipt->simulationRunningAfter = state.simulationRunning;
    receipt->physicsBufferingAfter = state.physicsBuffering;
    receipt->actorPoseAfter = state.actorPose;
    receipt->body2ActorAfter = state.body2Actor;
    receipt->bufferedBody2WorldAfter = state.bufferedBody2World;
    receipt->coreBody2WorldAfter = state.coreBody2World;
    receipt->bodySimAfter = state.bodySim;
    receipt->wakeCounterBufferedBitsAfter = state.wakeCounterBufferedBits;
    receipt->wakeCounterCoreBitsAfter = state.wakeCounterCoreBits;
    receipt->bufferedIsSleepingAfter = state.bufferedIsSleeping;
    receipt->bodySimActiveAfter = state.bodySimActive;
}

static int RestoreExistingBody2World(uintptr_t unityBase, uintptr_t rigidbody,
    const RigidPose* actorPose, const RigidPose* body2Actor,
    const RigidPose* body2World, Body2WorldRestoreReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(Body2WorldRestoreReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    if (actorPose) receipt->actorPoseTarget = *actorPose;
    if (body2World) receipt->body2WorldTarget = *body2World;
    if (!unityBase || !rigidbody || !actorPose || !body2Actor || !body2World)
        return FailBody2WorldRestore(receipt, Body2WorldBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!Finite(*actorPose) || !Finite(*body2Actor) || !Finite(*body2World))
        return FailBody2WorldRestore(receipt, Body2WorldNonfinite,
            ERROR_INVALID_DATA);

    BodyPoseState before = {};
    uint32_t error = ERROR_SUCCESS;
    Body2WorldResult result = CaptureBodyPoseState(
        unityBase, rigidbody, &before, &error);
    PopulateBody2WorldRestoreBefore(receipt, before);
    if (result != Body2WorldOk)
        return FailBody2WorldRestore(receipt, result, error);
    if (!EqualBytes(&before.body2Actor,
            reinterpret_cast<const uint8_t*>(body2Actor), sizeof(RigidPose)))
        return FailBody2WorldRestore(receipt, Body2WorldMassFrameChanged,
            ERROR_INVALID_STATE);

    // Prove the captured internal transforms still compose to the requested
    // public actor pose before touching scene queries, timestamps or BodySim.
    // The shipped Np getter is pure on this zeroed shadow actor: with
    // BF_Body2Actor clear it reads body2Actor at +0x70 and body2World at +0xE0.
    __declspec(align(16)) uint8_t shadowActor[0x120] = {};
    *reinterpret_cast<PhysxTransform*>(shadowActor + 0x70) =
        ImportPose(*body2Actor);
    *reinterpret_cast<PhysxTransform*>(shadowActor + 0xE0) =
        ImportPose(*body2World);
    NpRigidDynamicGetGlobalPose getGlobalPose =
        reinterpret_cast<NpRigidDynamicGetGlobalPose>(
            unityBase + kNpGetGlobalPoseRva);
    PhysxTransform composedActorPose = {};
    getGlobalPose(shadowActor, &composedActorPose);
    const RigidPose composedPose = ExportPose(composedActorPose);
    if (!EqualBytes(&composedPose,
            reinterpret_cast<const uint8_t*>(actorPose), sizeof(RigidPose)))
        return FailBody2WorldRestore(receipt, Body2WorldPreimageMismatch,
            ERROR_INVALID_DATA);

    const bool actorExact = EqualBytes(&before.actorPose,
        reinterpret_cast<const uint8_t*>(actorPose), sizeof(RigidPose));
    const bool bodyExact = EqualBytes(&before.bufferedBody2World,
        reinterpret_cast<const uint8_t*>(body2World), sizeof(RigidPose));
    if (actorExact && bodyExact) {
        PopulateBody2WorldRestoreAfter(receipt, before);
        receipt->result = Body2WorldOk;
        return 1;
    }

    // Reproduce setGlobalPose's scene-query prelude exactly once, then call
    // the official Scb setter once with the captured COM pose.  Calling the
    // public setter first would invoke BodySim::postBody2WorldChange twice and
    // perturb contact/trigger history unnecessarily.
    NpActorGetApiScene getApiScene = reinterpret_cast<NpActorGetApiScene>(
        unityBase + kNpActorGetApiSceneRva);
    void* apiScene = getApiScene(reinterpret_cast<void*>(before.actor));
    receipt->apiScene = reinterpret_cast<uintptr_t>(apiScene);
    if (!apiScene)
        return FailBody2WorldRestore(receipt,
            Body2WorldMissingApiScene, ERROR_INVALID_STATE);
    uint8_t* sceneBytes = static_cast<uint8_t*>(apiScene);
    if (!Readable(sceneBytes + 0xD40, 0x1C) ||
        !Writable(sceneBytes + 0xD58, sizeof(uint32_t)))
        return FailBody2WorldRestore(receipt,
            Body2WorldReadbackChanged, ERROR_NOACCESS);
    uint32_t* timestamp = reinterpret_cast<uint32_t*>(sceneBytes + 0xD58);
    receipt->dynamicTimestampBefore = *timestamp;
    NpShapeManagerMarkSceneQuery markSceneQuery =
        reinterpret_cast<NpShapeManagerMarkSceneQuery>(
            unityBase + kNpShapeManagerMarkSceneQueryRva);
    markSceneQuery(reinterpret_cast<void*>(before.actor + 0x14),
        sceneBytes + 0xD40);
    receipt->changed |= 1u;
    ++(*timestamp);
    receipt->changed |= 2u;
    receipt->dynamicTimestampAfter = *timestamp;
    if (receipt->dynamicTimestampAfter !=
        receipt->dynamicTimestampBefore + 1u)
        return FailBody2WorldRestore(receipt,
            Body2WorldReadbackChanged, ERROR_WRITE_FAULT);

    const PhysxTransform exactBody2World = ImportPose(*body2World);
    ScbBodySetBody2World setBody2World =
        reinterpret_cast<ScbBodySetBody2World>(
            unityBase + kScbBodySetBody2WorldRva);
    setBody2World(reinterpret_cast<void*>(before.actor + 0x30),
        exactBody2World, false);
    receipt->changed |= 4u;

    BodyPoseState after = {};
    error = ERROR_SUCCESS;
    result = CaptureBodyPoseState(unityBase, rigidbody, &after, &error);
    PopulateBody2WorldRestoreAfter(receipt, after);
    if (result != Body2WorldOk)
        return FailBody2WorldRestore(receipt, result, error);
    if (after.actor != before.actor || after.scene != before.scene ||
        after.controlState != before.controlState ||
        after.bodyBufferFlags != before.bodyBufferFlags ||
        after.simulationRunning != before.simulationRunning ||
        after.physicsBuffering != before.physicsBuffering ||
        after.bodySim != before.bodySim ||
        after.wakeCounterBufferedBits != before.wakeCounterBufferedBits ||
        after.wakeCounterCoreBits != before.wakeCounterCoreBits ||
        after.bufferedIsSleeping != before.bufferedIsSleeping ||
        after.bodySimActive != before.bodySimActive ||
        !EqualBytes(&after.body2Actor,
            reinterpret_cast<const uint8_t*>(body2Actor), sizeof(RigidPose)))
        return FailBody2WorldRestore(receipt, Body2WorldMassFrameChanged,
            ERROR_INVALID_STATE);
    if (!EqualBytes(&after.bufferedBody2World,
            reinterpret_cast<const uint8_t*>(body2World), sizeof(RigidPose)) ||
        !EqualBytes(&after.coreBody2World,
            reinterpret_cast<const uint8_t*>(body2World), sizeof(RigidPose)) ||
        !EqualBytes(&after.actorPose,
            reinterpret_cast<const uint8_t*>(actorPose), sizeof(RigidPose)))
        return FailBody2WorldRestore(receipt, Body2WorldReadbackChanged,
            ERROR_WRITE_FAULT);
    receipt->result = Body2WorldOk;
    return 1;
}

static void PopulateWakeStateBefore(WakeStateRestoreReceipt* receipt,
    const BodyPoseState& state) {
    receipt->actor = state.actor;
    receipt->bodySim = state.bodySim;
    receipt->wakeCounterBufferedBitsBefore = state.wakeCounterBufferedBits;
    receipt->wakeCounterCoreBitsBefore = state.wakeCounterCoreBits;
    receipt->bufferedIsSleepingBefore = state.bufferedIsSleeping;
    receipt->bodySimActiveBefore = state.bodySimActive;
}

static void PopulateWakeStateAfter(WakeStateRestoreReceipt* receipt,
    const BodyPoseState& state) {
    receipt->wakeCounterBufferedBitsAfter = state.wakeCounterBufferedBits;
    receipt->wakeCounterCoreBitsAfter = state.wakeCounterCoreBits;
    receipt->bufferedIsSleepingAfter = state.bufferedIsSleeping;
    receipt->bodySimActiveAfter = state.bodySimActive;
}

static bool WakeFunctionsMatch(uintptr_t unityBase, uintptr_t actor) {
    if (!unityBase || !actor ||
        !Readable(reinterpret_cast<const void*>(actor), 0x70)) return false;
    const uintptr_t vtable = *reinterpret_cast<const uintptr_t*>(actor);
    const void* setAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetWakeCounterRva);
    const void* wakeAddress = reinterpret_cast<const void*>(
        unityBase + kNpWakeUpRva);
    const void* sleepAddress = reinterpret_cast<const void*>(
        unityBase + kNpPutToSleepRva);
    return vtable == unityBase + kNpRigidDynamicVtableRva &&
        Readable(reinterpret_cast<const void*>(vtable), 0x120) &&
        *reinterpret_cast<const uintptr_t*>(vtable + 0x110) ==
            unityBase + kNpSetWakeCounterRva &&
        *reinterpret_cast<const uintptr_t*>(vtable + 0x118) ==
            unityBase + kNpWakeUpRva &&
        *reinterpret_cast<const uintptr_t*>(vtable + 0x11C) ==
            unityBase + kNpPutToSleepRva &&
        Readable(setAddress, sizeof(kNpSetWakeCounterBytes)) &&
        EqualBytes(setAddress, kNpSetWakeCounterBytes,
            sizeof(kNpSetWakeCounterBytes)) &&
        Readable(wakeAddress, sizeof(kNpWakeUpBytes)) &&
        EqualBytes(wakeAddress, kNpWakeUpBytes, sizeof(kNpWakeUpBytes)) &&
        Readable(sleepAddress, sizeof(kNpPutToSleepBytes)) &&
        EqualBytes(sleepAddress, kNpPutToSleepBytes,
            sizeof(kNpPutToSleepBytes));
}

static int RestoreExistingWakeState(uintptr_t unityBase, uintptr_t rigidbody,
    uint32_t targetWakeCounterBits, uint32_t targetSleeping,
    WakeStateRestoreReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(WakeStateRestoreReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    receipt->targetWakeCounterBits = targetWakeCounterBits;
    receipt->targetSleeping = targetSleeping;
    union WakeCounterValue { uint32_t bits; float value; } target = {};
    target.bits = targetWakeCounterBits;
    if (!unityBase || !rigidbody || targetSleeping > 1u)
        return FailWakeStateRestore(receipt, WakeStateBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!Finite(target.value) || target.value < 0.0f ||
        (targetSleeping != 0 && (targetWakeCounterBits & 0x7FFFFFFFu) != 0))
        return FailWakeStateRestore(receipt, WakeStateInvalidTarget,
            ERROR_INVALID_DATA);

    BodyPoseState before = {};
    uint32_t error = ERROR_SUCCESS;
    Body2WorldResult bodyResult = CaptureBodyPoseState(
        unityBase, rigidbody, &before, &error);
    PopulateWakeStateBefore(receipt, before);
    if (bodyResult != Body2WorldOk)
        return FailWakeStateRestore(receipt, WakeStateBodyStateInvalid, error);
    if (!WakeFunctionsMatch(unityBase, before.actor))
        return FailWakeStateRestore(receipt, WakeStateRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    if ((*reinterpret_cast<const uint8_t*>(before.actor + 0x6C) & 1u) != 0)
        return FailWakeStateRestore(receipt, WakeStateBodyStateInvalid,
            ERROR_INVALID_STATE);

    const bool exact = before.wakeCounterBufferedBits == targetWakeCounterBits &&
        before.wakeCounterCoreBits == targetWakeCounterBits &&
        before.bufferedIsSleeping == targetSleeping &&
        before.bodySimActive == (targetSleeping == 0 ? 1u : 0u);
    if (exact) {
        PopulateWakeStateAfter(receipt, before);
        receipt->result = WakeStateOk;
        return 1;
    }
    if (targetSleeping != 0)
        return FailWakeStateRestore(receipt,
            WakeStateSleepingTransitionUnsupported, ERROR_NOT_SUPPORTED);

    NpRigidDynamicSetWakeCounter setWakeCounter =
        reinterpret_cast<NpRigidDynamicSetWakeCounter>(
            unityBase + kNpSetWakeCounterRva);
    NpRigidDynamicWakeUp wakeUp = reinterpret_cast<NpRigidDynamicWakeUp>(
        unityBase + kNpWakeUpRva);
    if (before.bufferedIsSleeping != 0 &&
        (targetWakeCounterBits & 0x7FFFFFFFu) == 0) {
        wakeUp(reinterpret_cast<void*>(before.actor));
        receipt->callMask |= 2u;
    }
    setWakeCounter(reinterpret_cast<void*>(before.actor), target.value);
    receipt->callMask |= 1u;

    BodyPoseState after = {};
    error = ERROR_SUCCESS;
    bodyResult = CaptureBodyPoseState(unityBase, rigidbody, &after, &error);
    PopulateWakeStateAfter(receipt, after);
    if (bodyResult != Body2WorldOk)
        return FailWakeStateRestore(receipt, WakeStateBodyStateInvalid, error);
    if (after.actor != before.actor || after.scene != before.scene ||
        after.controlState != before.controlState ||
        after.bodyBufferFlags != before.bodyBufferFlags ||
        after.simulationRunning != before.simulationRunning ||
        after.physicsBuffering != before.physicsBuffering ||
        after.bodySim != before.bodySim ||
        !EqualBytes(&after.actorPose,
            reinterpret_cast<const uint8_t*>(&before.actorPose), sizeof(RigidPose)) ||
        !EqualBytes(&after.body2Actor,
            reinterpret_cast<const uint8_t*>(&before.body2Actor), sizeof(RigidPose)) ||
        !EqualBytes(&after.bufferedBody2World,
            reinterpret_cast<const uint8_t*>(&before.bufferedBody2World), sizeof(RigidPose)) ||
        !EqualBytes(&after.coreBody2World,
            reinterpret_cast<const uint8_t*>(&before.coreBody2World), sizeof(RigidPose)) ||
        after.wakeCounterBufferedBits != targetWakeCounterBits ||
        after.wakeCounterCoreBits != targetWakeCounterBits ||
        after.bufferedIsSleeping != 0 || after.bodySimActive != 1)
        return FailWakeStateRestore(receipt, WakeStateReadbackChanged,
            ERROR_WRITE_FAULT);
    receipt->result = WakeStateOk;
    return 1;
}

static int GetExistingActorKinematicTarget(uintptr_t unityBase,
    uintptr_t rigidbody, KinematicTargetReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(KinematicTargetReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    if (!unityBase || !rigidbody)
        return FailKinematicTarget(receipt, KinematicTargetBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x55))
        return FailKinematicTarget(receipt, KinematicTargetUnreadableRigidbody,
            ERROR_NOACCESS);
    const uintptr_t actor =
        *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    receipt->actor = actor;
    receipt->unityIsKinematic =
        *reinterpret_cast<const uint8_t*>(rigidbody + 0x54);
    if (!actor || !Readable(reinterpret_cast<const void*>(actor), 0x120))
        return FailKinematicTarget(receipt, KinematicTargetMissingActor,
            ERROR_INVALID_STATE);
    const void* address = reinterpret_cast<const void*>(
        unityBase + kNpGetKinematicTargetRva);
    if (!Readable(address, sizeof(kNpGetKinematicTargetBytes)) ||
        !EqualBytes(address, kNpGetKinematicTargetBytes,
            sizeof(kNpGetKinematicTargetBytes)))
        return FailKinematicTarget(receipt, KinematicTargetRevisionMismatch,
            ERROR_REVISION_MISMATCH);

    // NpRigidDynamic embeds Scb::Body at +0x30. The matching PhysX 3.3.3
    // source and shipped PDB/disassembly put mBodyBufferFlags at +0xEC,
    // BodyCore at +0x10, and BodyCore::mSimStateData at +0x9C. These fields
    // are diagnostic only; publicTargetValid/target come from PhysX's public
    // NpRigidDynamic implementation below.
    receipt->scbBodyBufferFlags =
        *reinterpret_cast<const uint32_t*>(actor + 0x11C);
    receipt->bufferedTargetValid =
        (receipt->scbBodyBufferFlags & 0x2000u) != 0 ? 1u : 0u;
    receipt->simStateData =
        *reinterpret_cast<const uintptr_t*>(actor + 0xDC);
    if (receipt->simStateData) {
        if (!Readable(reinterpret_cast<const void*>(receipt->simStateData), 0x20))
            return FailKinematicTarget(receipt, KinematicTargetUnreadableState,
                ERROR_NOACCESS);
        receipt->simStateIsKinematic =
            *reinterpret_cast<const uint8_t*>(receipt->simStateData + 0x1F) == 1 ? 1u : 0u;
        if (receipt->simStateIsKinematic)
            receipt->coreTargetValid =
                *reinterpret_cast<const uint8_t*>(receipt->simStateData + 0x1C) != 0 ? 1u : 0u;
    }

    NpRigidDynamicGetKinematicTarget getTarget =
        reinterpret_cast<NpRigidDynamicGetKinematicTarget>(
            unityBase + kNpGetKinematicTargetRva);
    PhysxTransform nativePose = {};
    receipt->publicTargetValid =
        getTarget(reinterpret_cast<void*>(actor), nativePose) ? 1u : 0u;
    if (receipt->publicTargetValid) {
        for (uint32_t i = 0; i < 3; ++i)
            receipt->target.position[i] = nativePose.position[i];
        for (uint32_t i = 0; i < 4; ++i)
            receipt->target.rotation[i] = nativePose.rotation[i];
        if (!Finite(receipt->target))
            return FailKinematicTarget(receipt, KinematicTargetNonfinite,
                ERROR_INVALID_DATA);
    }
    receipt->result = KinematicTargetOk;
    return 1;
}

// Reconstruct one source-equivalent pending kinematic target through PhysX's
// public NpRigidDynamic entry point.  This is intentionally narrower than a
// general wake-state writer: the caller must present a stable, targetless,
// sleeping kinematic actor while simulation and API buffering are stopped.
// PhysX owns the resulting wake counter, island activation, active-list order,
// and BF_KINEMATIC_MOVED transition.
static int SetExistingActorKinematicTarget(uintptr_t unityBase,
    uintptr_t rigidbody, const RigidPose* pose,
    KinematicTargetReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(KinematicTargetReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    if (!unityBase || !rigidbody || !pose)
        return FailKinematicTarget(receipt, KinematicTargetBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!Finite(*pose))
        return FailKinematicTarget(receipt, KinematicTargetNonfinite,
            ERROR_INVALID_DATA);

    KinematicTargetReceipt beforeTarget = {};
    if (GetExistingActorKinematicTarget(
            unityBase, rigidbody, &beforeTarget) != 1) {
        *receipt = beforeTarget;
        return 0;
    }
    *receipt = beforeTarget;
    if (beforeTarget.unityIsKinematic != 1 ||
        beforeTarget.simStateIsKinematic != 1)
        return FailKinematicTarget(receipt, KinematicTargetNotKinematic,
            ERROR_INVALID_STATE);
    if (beforeTarget.publicTargetValid != 0 ||
        beforeTarget.bufferedTargetValid != 0 ||
        beforeTarget.coreTargetValid != 0)
        return FailKinematicTarget(receipt, KinematicTargetAlreadyValid,
            ERROR_ALREADY_EXISTS);

    BodyPoseState beforeBody = {};
    uint32_t error = ERROR_SUCCESS;
    if (CaptureBodyPoseState(unityBase, rigidbody, &beforeBody, &error) !=
            Body2WorldOk ||
        beforeBody.actor != beforeTarget.actor ||
        beforeBody.controlState != 2 || beforeBody.bodyBufferFlags != 0 ||
        beforeBody.simulationRunning != 0 || beforeBody.physicsBuffering != 0 ||
        beforeBody.bufferedIsSleeping != 1 || beforeBody.bodySimActive != 0 ||
        beforeBody.wakeCounterBufferedBits != 0 ||
        beforeBody.wakeCounterCoreBits != 0)
        return FailKinematicTarget(receipt, KinematicTargetBodyStateInvalid,
            error == ERROR_SUCCESS ? ERROR_INVALID_STATE : error);
    if (!Readable(reinterpret_cast<const void*>(beforeBody.actor),
            sizeof(uintptr_t)) ||
        *reinterpret_cast<const uintptr_t*>(beforeBody.actor) !=
            unityBase + kNpRigidDynamicVtableRva)
        return FailKinematicTarget(receipt, KinematicTargetMissingActor,
            ERROR_INVALID_STATE);
    const void* address = reinterpret_cast<const void*>(
        unityBase + kNpSetKinematicTargetRva);
    if (!Readable(address, sizeof(kNpSetKinematicTargetBytes)) ||
        !EqualBytes(address, kNpSetKinematicTargetBytes,
            sizeof(kNpSetKinematicTargetBytes)))
        return FailKinematicTarget(receipt, KinematicTargetRevisionMismatch,
            ERROR_REVISION_MISMATCH);

    NpRigidDynamicSetKinematicTarget setTarget =
        reinterpret_cast<NpRigidDynamicSetKinematicTarget>(
            unityBase + kNpSetKinematicTargetRva);
    const PhysxTransform nativePose = ImportPose(*pose);
    setTarget(reinterpret_cast<void*>(beforeBody.actor), nativePose);

    KinematicTargetReceipt afterTarget = {};
    if (GetExistingActorKinematicTarget(
            unityBase, rigidbody, &afterTarget) != 1) {
        *receipt = afterTarget;
        return 0;
    }
    BodyPoseState afterBody = {};
    error = ERROR_SUCCESS;
    const Body2WorldResult bodyResult = CaptureBodyPoseState(
        unityBase, rigidbody, &afterBody, &error);
    *receipt = afterTarget;
    if (bodyResult != Body2WorldOk ||
        afterTarget.actor != beforeTarget.actor ||
        afterTarget.unityIsKinematic != 1 ||
        afterTarget.publicTargetValid != 1 ||
        afterTarget.scbBodyBufferFlags != 0 ||
        afterTarget.bufferedTargetValid != 0 ||
        afterTarget.simStateData != beforeTarget.simStateData ||
        afterTarget.simStateIsKinematic != 1 ||
        afterTarget.coreTargetValid != 1 ||
        !EqualBytes(&afterTarget.target,
            reinterpret_cast<const uint8_t*>(pose), sizeof(RigidPose)) ||
        afterBody.actor != beforeBody.actor ||
        afterBody.scene != beforeBody.scene ||
        afterBody.controlState != beforeBody.controlState ||
        afterBody.bodyBufferFlags != beforeBody.bodyBufferFlags ||
        afterBody.simulationRunning != 0 || afterBody.physicsBuffering != 0 ||
        afterBody.bodySim != beforeBody.bodySim ||
        afterBody.bufferedIsSleeping != 0 || afterBody.bodySimActive != 1 ||
        afterBody.wakeCounterBufferedBits == 0 ||
        afterBody.wakeCounterBufferedBits != afterBody.wakeCounterCoreBits ||
        !EqualBytes(&afterBody.actorPose,
            reinterpret_cast<const uint8_t*>(&beforeBody.actorPose),
            sizeof(RigidPose)) ||
        !EqualBytes(&afterBody.body2Actor,
            reinterpret_cast<const uint8_t*>(&beforeBody.body2Actor),
            sizeof(RigidPose)) ||
        !EqualBytes(&afterBody.bufferedBody2World,
            reinterpret_cast<const uint8_t*>(&beforeBody.bufferedBody2World),
            sizeof(RigidPose)) ||
        !EqualBytes(&afterBody.coreBody2World,
            reinterpret_cast<const uint8_t*>(&beforeBody.coreBody2World),
            sizeof(RigidPose)))
        return FailKinematicTarget(receipt, KinematicTargetReadbackChanged,
            error == ERROR_SUCCESS ? ERROR_WRITE_FAULT : error);
    receipt->result = KinematicTargetOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

// Unity's Transform pose dispatch can install a kinematic target even when a
// sleeping actor was already moved natively to that exact pose. PhysX 3.3.3
// exposes no public clear-target API. Sc::BodyCore::invalidateKinematicTarget
// is the source operation used after target consumption and writes only the
// KinematicTransform::targetValid byte. This helper deliberately does not call
// deactivateKinematic, putToSleep, or any wake/island/list operation.
static int InvalidateExistingActorKinematicTarget(uintptr_t unityBase,
    uintptr_t rigidbody, InvalidateKinematicTargetReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(InvalidateKinematicTargetReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    if (!unityBase || !rigidbody)
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetBadArgument, ERROR_INVALID_PARAMETER);
    if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x55))
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetUnreadableRigidbody, ERROR_NOACCESS);
    receipt->unityIsKinematic =
        *reinterpret_cast<const uint8_t*>(rigidbody + 0x54);
    if (receipt->unityIsKinematic != 1)
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetNotKinematic, ERROR_INVALID_STATE);
    receipt->actor = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    if (!receipt->actor ||
        !Readable(reinterpret_cast<const void*>(receipt->actor), 0xE0) ||
        *reinterpret_cast<const uintptr_t*>(receipt->actor) !=
            unityBase + kNpRigidDynamicVtableRva)
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetMissingActor, ERROR_INVALID_STATE);
    receipt->bodyCore = receipt->actor + 0x40;
    receipt->simStateData =
        *reinterpret_cast<const uintptr_t*>(receipt->bodyCore + 0x9C);
    if (!receipt->simStateData ||
        !Readable(reinterpret_cast<const void*>(receipt->simStateData), 0x20))
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetUnreadableState, ERROR_NOACCESS);
    receipt->simStateIsKinematic =
        *reinterpret_cast<const uint8_t*>(receipt->simStateData + 0x1F) == 1 ? 1u : 0u;
    receipt->targetValidBefore =
        *reinterpret_cast<const uint8_t*>(receipt->simStateData + 0x1C) != 0 ? 1u : 0u;
    if (receipt->simStateIsKinematic != 1 || receipt->targetValidBefore != 1)
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetNotValid, ERROR_INVALID_STATE);
    const void* address = reinterpret_cast<const void*>(
        unityBase + kScBodyCoreInvalidateKinematicTargetRva);
    if (!Readable(address, sizeof(kScBodyCoreInvalidateKinematicTargetBytes)) ||
        !EqualBytes(address, kScBodyCoreInvalidateKinematicTargetBytes,
            sizeof(kScBodyCoreInvalidateKinematicTargetBytes)))
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetRevisionMismatch, ERROR_REVISION_MISMATCH);

    ScBodyCoreInvalidateKinematicTarget invalidateTarget =
        reinterpret_cast<ScBodyCoreInvalidateKinematicTarget>(
            unityBase + kScBodyCoreInvalidateKinematicTargetRva);
    invalidateTarget(reinterpret_cast<void*>(receipt->bodyCore));

    if (*reinterpret_cast<const uintptr_t*>(rigidbody + 0x34) != receipt->actor ||
        *reinterpret_cast<const uintptr_t*>(receipt->bodyCore + 0x9C) !=
            receipt->simStateData ||
        *reinterpret_cast<const uint8_t*>(receipt->simStateData + 0x1F) != 1 ||
        *reinterpret_cast<const uint8_t*>(rigidbody + 0x54) != 1)
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetReadbackChanged, ERROR_WRITE_FAULT);
    receipt->targetValidAfter =
        *reinterpret_cast<const uint8_t*>(receipt->simStateData + 0x1C) != 0 ? 1u : 0u;
    if (receipt->targetValidAfter != 0)
        return FailInvalidateKinematicTarget(receipt,
            InvalidateKinematicTargetReadbackChanged, ERROR_WRITE_FAULT);
    receipt->result = InvalidateKinematicTargetOk;
    return 1;
}

static int SetExistingActorMassFrame(uintptr_t unityBase, uintptr_t rigidbody,
    const RigidMassFrame* frame, SetMassFrameReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(SetMassFrameReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    if (!unityBase || !rigidbody || !frame)
        return FailSetMassFrame(receipt, SetMassFrameBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!Finite(*frame))
        return FailSetMassFrame(receipt, SetMassFrameNonfinite,
            ERROR_INVALID_DATA);
    for (uint32_t i = 0; i < 3; ++i)
        if (frame->inertiaTensor[i] < 0.0f)
            return FailSetMassFrame(receipt, SetMassFrameInvalidInertia,
                ERROR_INVALID_DATA);
    const float* q = frame->inertiaTensorRotation;
    const float normSquared =
        q[0] * q[0] + q[1] * q[1] + q[2] * q[2] + q[3] * q[3];
    if (normSquared < 0.998f || normSquared > 1.002f)
        return FailSetMassFrame(receipt, SetMassFrameInvalidRotation,
            ERROR_INVALID_DATA);
    if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x53))
        return FailSetMassFrame(receipt, SetMassFrameUnreadableRigidbody,
            ERROR_NOACCESS);
    const uintptr_t actor =
        *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    receipt->actor = actor;
    if (!actor || !Readable(reinterpret_cast<const void*>(actor), 0x18))
        return FailSetMassFrame(receipt, SetMassFrameMissingActor,
            ERROR_INVALID_STATE);
    receipt->automaticInertiaBefore =
        *reinterpret_cast<const uint8_t*>(rigidbody + 0x51);
    receipt->automaticCenterBefore =
        *reinterpret_cast<const uint8_t*>(rigidbody + 0x52);
    if (receipt->automaticInertiaBefore != 1 ||
        receipt->automaticCenterBefore != 1)
        return FailSetMassFrame(receipt, SetMassFrameNotAutomaticBefore,
            ERROR_INVALID_STATE);
    const void* centerAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetCMassLocalPoseInternalRva);
    if (!Readable(centerAddress, sizeof(kNpSetCMassLocalPoseInternalBytes)) ||
        !EqualBytes(centerAddress, kNpSetCMassLocalPoseInternalBytes,
            sizeof(kNpSetCMassLocalPoseInternalBytes)))
        return FailSetMassFrame(receipt, SetMassFrameCenterRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    const void* inertiaAddress = reinterpret_cast<const void*>(
        unityBase + kNpSetMassSpaceInertiaTensorRva);
    if (!Readable(inertiaAddress, sizeof(kNpSetMassSpaceInertiaTensorBytes)) ||
        !EqualBytes(inertiaAddress, kNpSetMassSpaceInertiaTensorBytes,
            sizeof(kNpSetMassSpaceInertiaTensorBytes)))
        return FailSetMassFrame(receipt, SetMassFrameInertiaRevisionMismatch,
            ERROR_REVISION_MISMATCH);

    receipt->frame = *frame;
    PhysxTransform nativeCenter = {};
    for (uint32_t i = 0; i < 4; ++i)
        nativeCenter.rotation[i] = frame->inertiaTensorRotation[i];
    for (uint32_t i = 0; i < 3; ++i)
        nativeCenter.position[i] = frame->centerOfMass[i];
    PhysxVec3 nativeInertia = {
        frame->inertiaTensor[0],
        frame->inertiaTensor[1],
        frame->inertiaTensor[2]
    };
    NpRigidBodySetCMassLocalPoseInternal setCenter =
        reinterpret_cast<NpRigidBodySetCMassLocalPoseInternal>(
            unityBase + kNpSetCMassLocalPoseInternalRva);
    NpRigidBodySetMassSpaceInertiaTensor setInertia =
        reinterpret_cast<NpRigidBodySetMassSpaceInertiaTensor>(
            unityBase + kNpSetMassSpaceInertiaTensorRva);
    setCenter(reinterpret_cast<void*>(actor), nativeCenter);
    setInertia(reinterpret_cast<void*>(actor), nativeInertia);
    receipt->automaticInertiaAfter =
        *reinterpret_cast<const uint8_t*>(rigidbody + 0x51);
    receipt->automaticCenterAfter =
        *reinterpret_cast<const uint8_t*>(rigidbody + 0x52);
    if (receipt->automaticInertiaAfter != 1 ||
        receipt->automaticCenterAfter != 1)
        return FailSetMassFrame(receipt, SetMassFrameNotAutomaticAfter,
            ERROR_INVALID_STATE);
    receipt->result = SetMassFrameOk;
    return 1;
}

static bool ShapeReaderRevisionMatches(uintptr_t unityBase) {
    const void* getShapesAddress = reinterpret_cast<const void*>(unityBase + kGetShapesRva);
    const void* getPoseAddress = reinterpret_cast<const void*>(unityBase + kNpShapeGetLocalPoseRva);
    const void* getTypeAddress = reinterpret_cast<const void*>(unityBase + kNpShapeGetGeometryTypeRva);
    const void* getBoxAddress = reinterpret_cast<const void*>(unityBase + kNpShapeGetBoxGeometryRva);
    const void* getCapsuleAddress = reinterpret_cast<const void*>(unityBase + kNpShapeGetCapsuleGeometryRva);
    const void* getSphereAddress = reinterpret_cast<const void*>(unityBase + kNpShapeGetSphereGeometryRva);
    return Readable(getShapesAddress, sizeof(kGetShapesBytes)) &&
        EqualBytes(getShapesAddress, kGetShapesBytes, sizeof(kGetShapesBytes)) &&
        Readable(getPoseAddress, sizeof(kNpShapeGetLocalPoseBytes)) &&
        EqualBytes(getPoseAddress, kNpShapeGetLocalPoseBytes, sizeof(kNpShapeGetLocalPoseBytes)) &&
        Readable(getTypeAddress, sizeof(kNpShapeGetGeometryTypeBytes)) &&
        EqualBytes(getTypeAddress, kNpShapeGetGeometryTypeBytes, sizeof(kNpShapeGetGeometryTypeBytes)) &&
        Readable(getBoxAddress, sizeof(kNpShapeGetBoxGeometryBytes)) &&
        EqualBytes(getBoxAddress, kNpShapeGetBoxGeometryBytes, sizeof(kNpShapeGetBoxGeometryBytes)) &&
        Readable(getCapsuleAddress, sizeof(kNpShapeGetCapsuleGeometryBytes)) &&
        EqualBytes(getCapsuleAddress, kNpShapeGetCapsuleGeometryBytes, sizeof(kNpShapeGetCapsuleGeometryBytes)) &&
        Readable(getSphereAddress, sizeof(kNpShapeGetSphereGeometryBytes)) &&
        EqualBytes(getSphereAddress, kNpShapeGetSphereGeometryBytes, sizeof(kNpShapeGetSphereGeometryBytes));
}

static int CaptureExistingActorShapePoses(uintptr_t unityBase, uintptr_t rigidbody,
    uintptr_t* shapes, RigidPose* poses, ShapeGeometry* geometries,
    uint32_t capacity, uint32_t* count, uint32_t* error) {
    if (count) *count = 0;
    if (error) *error = ERROR_SUCCESS;
    if (!unityBase || !rigidbody || !shapes || !poses || !geometries || !capacity ||
        capacity > kMaximumShapePoses || !count || !error) {
        if (error) *error = ERROR_INVALID_PARAMETER;
        return 0;
    }
    if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x38) ||
        !Writable(shapes, capacity * sizeof(uintptr_t)) ||
        !Writable(poses, capacity * sizeof(RigidPose)) ||
        !Writable(geometries, capacity * sizeof(ShapeGeometry))) {
        *error = ERROR_NOACCESS;
        return 0;
    }
    const uintptr_t actor = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    if (!actor || !Readable(reinterpret_cast<const void*>(actor), 0x18)) {
        *error = ERROR_INVALID_STATE;
        return 0;
    }
    if (!ShapeReaderRevisionMatches(unityBase)) {
        *error = ERROR_REVISION_MISMATCH;
        return 0;
    }
    for (uint32_t i = 0; i < capacity; ++i) {
        shapes[i] = 0;
        poses[i] = {};
        geometries[i] = {};
    }
    RigidActorGetShapes getShapes = reinterpret_cast<RigidActorGetShapes>(
        unityBase + kGetShapesRva);
    NpShapeGetLocalPose getPose = reinterpret_cast<NpShapeGetLocalPose>(
        unityBase + kNpShapeGetLocalPoseRva);
    NpShapeGetGeometryType getType = reinterpret_cast<NpShapeGetGeometryType>(
        unityBase + kNpShapeGetGeometryTypeRva);
    NpShapeGetGeometry getBox = reinterpret_cast<NpShapeGetGeometry>(
        unityBase + kNpShapeGetBoxGeometryRva);
    NpShapeGetGeometry getCapsule = reinterpret_cast<NpShapeGetGeometry>(
        unityBase + kNpShapeGetCapsuleGeometryRva);
    NpShapeGetGeometry getSphere = reinterpret_cast<NpShapeGetGeometry>(
        unityBase + kNpShapeGetSphereGeometryRva);
    *count = getShapes(reinterpret_cast<void*>(actor),
        reinterpret_cast<void**>(shapes), capacity, 0);
    if (*count > capacity) {
        *error = ERROR_INSUFFICIENT_BUFFER;
        return 0;
    }
    for (uint32_t i = 0; i < *count; ++i) {
        if (!shapes[i] || !Readable(reinterpret_cast<const void*>(shapes[i]), 0x18) ||
            *reinterpret_cast<const uintptr_t*>(shapes[i]) != unityBase + kNpShapeVtableRva) {
            *error = ERROR_NOACCESS;
            return 0;
        }
        PhysxTransform nativePose = {};
        getPose(reinterpret_cast<void*>(shapes[i]), &nativePose);
        for (uint32_t j = 0; j < 3; ++j) poses[i].position[j] = nativePose.position[j];
        for (uint32_t j = 0; j < 4; ++j) poses[i].rotation[j] = nativePose.rotation[j];
        if (!Finite(poses[i])) {
            *error = ERROR_INVALID_DATA;
            return 0;
        }
        const uint32_t type = getType(reinterpret_cast<void*>(shapes[i]));
        geometries[i].type = type;
        uint8_t geometryOk = 1;
        if (type == 0) geometryOk = getSphere(reinterpret_cast<void*>(shapes[i]), &geometries[i]);
        else if (type == 2) geometryOk = getCapsule(reinterpret_cast<void*>(shapes[i]), &geometries[i]);
        else if (type == 3) geometryOk = getBox(reinterpret_cast<void*>(shapes[i]), &geometries[i]);
        if (!geometryOk || geometries[i].type != type || !Finite(geometries[i])) {
            *error = ERROR_INVALID_DATA;
            return 0;
        }
    }
    return 1;
}

static int RestoreExistingActorShapePoses(uintptr_t unityBase, uintptr_t rigidbody,
    const uintptr_t* expectedShapes, const RigidPose* poses,
    const ShapeGeometry* geometries, uint32_t count, ShapePoseRestoreReceipt* receipt) {
    if (!receipt) return 0;
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(ShapePoseRestoreReceipt);
    receipt->unityBase = unityBase;
    receipt->rigidbody = rigidbody;
    receipt->count = count;
    if (!unityBase || !rigidbody || (!expectedShapes && count) || (!poses && count) ||
        (!geometries && count))
        return FailShapePoseRestore(receipt, ShapePoseRestoreBadArgument,
            ERROR_INVALID_PARAMETER);
    if (count > kMaximumShapePoses)
        return FailShapePoseRestore(receipt, ShapePoseRestoreInvalidCount,
            ERROR_INSUFFICIENT_BUFFER);
    if (!Readable(reinterpret_cast<const void*>(rigidbody), 0x38) ||
        (count && (!Readable(expectedShapes, count * sizeof(uintptr_t)) ||
            !Readable(poses, count * sizeof(RigidPose)) ||
            !Readable(geometries, count * sizeof(ShapeGeometry)))))
        return FailShapePoseRestore(receipt, ShapePoseRestoreUnreadableRigidbody,
            ERROR_NOACCESS);
    const uintptr_t actor = *reinterpret_cast<const uintptr_t*>(rigidbody + 0x34);
    receipt->actor = actor;
    if (!actor || !Readable(reinterpret_cast<const void*>(actor), 0x18))
        return FailShapePoseRestore(receipt, ShapePoseRestoreMissingActor,
            ERROR_INVALID_STATE);
    const void* getShapesAddress = reinterpret_cast<const void*>(unityBase + kGetShapesRva);
    if (!Readable(getShapesAddress, sizeof(kGetShapesBytes)) ||
        !EqualBytes(getShapesAddress, kGetShapesBytes, sizeof(kGetShapesBytes)))
        return FailShapePoseRestore(receipt, ShapePoseRestoreGetShapesRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    const void* getPoseAddress = reinterpret_cast<const void*>(unityBase + kNpShapeGetLocalPoseRva);
    if (!Readable(getPoseAddress, sizeof(kNpShapeGetLocalPoseBytes)) ||
        !EqualBytes(getPoseAddress, kNpShapeGetLocalPoseBytes, sizeof(kNpShapeGetLocalPoseBytes)))
        return FailShapePoseRestore(receipt, ShapePoseRestoreGetPoseRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    const void* setPoseAddress = reinterpret_cast<const void*>(unityBase + kNpShapeSetLocalPoseRva);
    if (!Readable(setPoseAddress, sizeof(kNpShapeSetLocalPoseBytes)) ||
        !EqualBytes(setPoseAddress, kNpShapeSetLocalPoseBytes, sizeof(kNpShapeSetLocalPoseBytes)))
        return FailShapePoseRestore(receipt, ShapePoseRestoreSetPoseRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    if (!ShapeReaderRevisionMatches(unityBase))
        return FailShapePoseRestore(receipt, ShapePoseRestoreGetGeometryRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    const void* setGeometryAddress = reinterpret_cast<const void*>(unityBase + kNpShapeSetGeometryRva);
    if (!Readable(setGeometryAddress, sizeof(kNpShapeSetGeometryBytes)) ||
        !EqualBytes(setGeometryAddress, kNpShapeSetGeometryBytes, sizeof(kNpShapeSetGeometryBytes)))
        return FailShapePoseRestore(receipt, ShapePoseRestoreSetGeometryRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    for (uint32_t i = 0; i < count; ++i)
        if (!expectedShapes[i] || !Readable(reinterpret_cast<const void*>(expectedShapes[i]), 0x18) ||
            !Finite(poses[i]) || !Finite(geometries[i]))
            return FailShapePoseRestore(receipt, ShapePoseRestoreNonfinite,
                ERROR_INVALID_DATA);

    uintptr_t observedShapes[kMaximumShapePoses] = {};
    RigidActorGetShapes getShapes = reinterpret_cast<RigidActorGetShapes>(
        unityBase + kGetShapesRva);
    uint32_t observedCount = getShapes(reinterpret_cast<void*>(actor),
        reinterpret_cast<void**>(observedShapes), kMaximumShapePoses, 0);
    receipt->expectedOrderHash = OrderHash(expectedShapes, count);
    receipt->observedOrderHash = OrderHash(observedShapes, observedCount);
    if (observedCount != count || !EqualBytes(observedShapes,
        reinterpret_cast<const uint8_t*>(expectedShapes), count * sizeof(uintptr_t)))
        return FailShapePoseRestore(receipt, ShapePoseRestoreMembershipChanged,
            ERROR_INVALID_STATE);

    NpShapeGetLocalPose getPose = reinterpret_cast<NpShapeGetLocalPose>(
        unityBase + kNpShapeGetLocalPoseRva);
    NpShapeSetLocalPose setPose = reinterpret_cast<NpShapeSetLocalPose>(
        unityBase + kNpShapeSetLocalPoseRva);
    NpShapeGetGeometryType getType = reinterpret_cast<NpShapeGetGeometryType>(
        unityBase + kNpShapeGetGeometryTypeRva);
    NpShapeGetGeometry getBox = reinterpret_cast<NpShapeGetGeometry>(
        unityBase + kNpShapeGetBoxGeometryRva);
    NpShapeGetGeometry getCapsule = reinterpret_cast<NpShapeGetGeometry>(
        unityBase + kNpShapeGetCapsuleGeometryRva);
    NpShapeGetGeometry getSphere = reinterpret_cast<NpShapeGetGeometry>(
        unityBase + kNpShapeGetSphereGeometryRva);
    NpShapeSetGeometry setGeometry = reinterpret_cast<NpShapeSetGeometry>(
        unityBase + kNpShapeSetGeometryRva);
    ShapeGeometry observedGeometries[kMaximumShapePoses] = {};
    for (uint32_t i = 0; i < count; ++i) {
        void* shape = reinterpret_cast<void*>(observedShapes[i]);
        if (!Readable(shape, sizeof(uintptr_t)) ||
            *reinterpret_cast<const uintptr_t*>(shape) != unityBase + kNpShapeVtableRva)
            return FailShapePoseRestore(receipt, ShapePoseRestoreGeometryTypeChanged,
                ERROR_REVISION_MISMATCH);
        const uint32_t observedType = getType(shape);
        if (observedType != geometries[i].type)
            return FailShapePoseRestore(receipt, ShapePoseRestoreGeometryTypeChanged,
                ERROR_INVALID_STATE);
        observedGeometries[i].type = observedType;
        uint8_t geometryOk = 1;
        if (observedType == 0) geometryOk = getSphere(shape, &observedGeometries[i]);
        else if (observedType == 2) geometryOk = getCapsule(shape, &observedGeometries[i]);
        else if (observedType == 3) geometryOk = getBox(shape, &observedGeometries[i]);
        if (!geometryOk || observedGeometries[i].type != observedType ||
            !Finite(observedGeometries[i]))
            return FailShapePoseRestore(receipt, ShapePoseRestoreGeometryReadbackChanged,
                ERROR_INVALID_DATA);
    }
    for (uint32_t i = 0; i < count; ++i) {
        void* shape = reinterpret_cast<void*>(observedShapes[i]);
        const uint32_t observedType = observedGeometries[i].type;
        if (SupportedAnalyticGeometry(observedType)) {
            const uint32_t geometrySize = GeometrySize(observedType);
            if (!EqualBytes(&observedGeometries[i],
                reinterpret_cast<const uint8_t*>(&geometries[i]), geometrySize)) {
                setGeometry(shape, geometries[i]);
                ++receipt->geometryChanged;
                ShapeGeometry afterGeometry = {};
                uint8_t geometryOk = observedType == 0 ? getSphere(shape, &afterGeometry) :
                    (observedType == 2 ? getCapsule(shape, &afterGeometry) : getBox(shape, &afterGeometry));
                if (!geometryOk || !EqualBytes(&afterGeometry,
                    reinterpret_cast<const uint8_t*>(&geometries[i]), geometrySize))
                    return FailShapePoseRestore(receipt, ShapePoseRestoreGeometryReadbackChanged,
                        ERROR_WRITE_FAULT);
            }
        }
        PhysxTransform target = {};
        for (uint32_t j = 0; j < 3; ++j) target.position[j] = poses[i].position[j];
        for (uint32_t j = 0; j < 4; ++j) target.rotation[j] = poses[i].rotation[j];
        PhysxTransform before = {};
        getPose(shape, &before);
        if (EqualBytes(&before, reinterpret_cast<const uint8_t*>(&target), sizeof(target))) continue;
        setPose(shape, target);
        ++receipt->poseChanged;
        PhysxTransform after = {};
        getPose(shape, &after);
        if (!EqualBytes(&after, reinterpret_cast<const uint8_t*>(&target), sizeof(target)))
            return FailShapePoseRestore(receipt, ShapePoseRestoreReadbackChanged,
                ERROR_WRITE_FAULT);
    }
    receipt->result = ShapePoseRestoreOk;
    return 1;
}

} // namespace

extern "C" __declspec(dllexport) uint32_t __cdecl oc2_rigidbody_rebuild_api_version() {
    return kApiVersion;
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_rebuild(
    uintptr_t unityBase, uintptr_t rigidbody, RebuildReceipt* receipt) {
    return RebuildBatch(unityBase, &rigidbody, 1, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_rebuild_batch(
    uintptr_t unityBase, const uintptr_t* rigidbodies, uint32_t count, RebuildReceipt* receipts) {
    return RebuildBatch(unityBase, rigidbodies, count, receipts);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_actor_shapes(
    uintptr_t unityBase, uintptr_t actor, uintptr_t* shapes, uint32_t capacity,
    uint32_t* count, uint32_t* error) {
    if (count) *count = 0;
    if (error) *error = ERROR_SUCCESS;
    if (!unityBase || !actor || !shapes || !capacity || !count || !error) {
        if (error) *error = ERROR_INVALID_PARAMETER;
        return 0;
    }
    const void* address = reinterpret_cast<const void*>(unityBase + kGetShapesRva);
    if (!Readable(address, sizeof(kGetShapesBytes)) ||
        !EqualBytes(address, kGetShapesBytes, sizeof(kGetShapesBytes))) {
        *error = ERROR_REVISION_MISMATCH;
        return 0;
    }
    if (!Readable(reinterpret_cast<const void*>(actor), 0x18)) {
        *error = ERROR_NOACCESS;
        return 0;
    }
    for (uint32_t i = 0; i < capacity; ++i) shapes[i] = 0;
    RigidActorGetShapes getShapes = reinterpret_cast<RigidActorGetShapes>(unityBase + kGetShapesRva);
    *count = getShapes(reinterpret_cast<void*>(actor), reinterpret_cast<void**>(shapes), capacity, 0);
    if (*count > capacity) {
        *error = ERROR_INSUFFICIENT_BUFFER;
        return 0;
    }
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_set_global_pose(
    uintptr_t unityBase, uintptr_t rigidbody, const RigidPose* pose,
    SetGlobalPoseReceipt* receipt) {
    return SetExistingActorGlobalPose(unityBase, rigidbody, pose, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_capture_body2world(
    uintptr_t unityBase, uintptr_t rigidbody,
    Body2WorldCaptureReceipt* receipt) {
    return CaptureExistingBody2World(unityBase, rigidbody, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_restore_body2world(
    uintptr_t unityBase, uintptr_t rigidbody, const RigidPose* actorPose,
    const RigidPose* body2Actor, const RigidPose* body2World,
    Body2WorldRestoreReceipt* receipt) {
    return RestoreExistingBody2World(unityBase, rigidbody, actorPose,
        body2Actor, body2World, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_restore_wake_state(
    uintptr_t unityBase, uintptr_t rigidbody, uint32_t targetWakeCounterBits,
    uint32_t targetSleeping, WakeStateRestoreReceipt* receipt) {
    return RestoreExistingWakeState(unityBase, rigidbody,
        targetWakeCounterBits, targetSleeping, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_get_kinematic_target(
    uintptr_t unityBase, uintptr_t rigidbody, KinematicTargetReceipt* receipt) {
    return GetExistingActorKinematicTarget(unityBase, rigidbody, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_set_kinematic_target(
    uintptr_t unityBase, uintptr_t rigidbody, const RigidPose* pose,
    KinematicTargetReceipt* receipt) {
    return SetExistingActorKinematicTarget(
        unityBase, rigidbody, pose, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_invalidate_kinematic_target(
    uintptr_t unityBase, uintptr_t rigidbody,
    InvalidateKinematicTargetReceipt* receipt) {
    return InvalidateExistingActorKinematicTarget(unityBase, rigidbody, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_set_mass_frame(
    uintptr_t unityBase, uintptr_t rigidbody, const RigidMassFrame* frame,
    SetMassFrameReceipt* receipt) {
    return SetExistingActorMassFrame(unityBase, rigidbody, frame, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_capture_shape_poses(
    uintptr_t unityBase, uintptr_t rigidbody, uintptr_t* shapes, RigidPose* poses,
    ShapeGeometry* geometries, uint32_t capacity, uint32_t* count, uint32_t* error) {
    return CaptureExistingActorShapePoses(unityBase, rigidbody, shapes, poses, geometries,
        capacity, count, error);
}

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_restore_shape_poses(
    uintptr_t unityBase, uintptr_t rigidbody, const uintptr_t* shapes,
    const RigidPose* poses, const ShapeGeometry* geometries, uint32_t count,
    ShapePoseRestoreReceipt* receipt) {
    return RestoreExistingActorShapePoses(unityBase, rigidbody, shapes, poses, geometries,
        count, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_pool_capture(
    uintptr_t context, ContactPoolReceipt* receipt) {
    return CaptureContactPool(context, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_pool_restore(
    uintptr_t context, ContactPoolReceipt* receipt) {
    return RestoreContactPool(context, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_pool_capture_snapshot(
    uintptr_t context, uintptr_t* snapshot, uint32_t capacity,
    ContactPoolReceipt* receipt) {
    return CaptureContactPoolSnapshot(context, snapshot, capacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_pool_restore_snapshot(
    uintptr_t context, uintptr_t expectedFreeArray, const uintptr_t* snapshot,
    uint32_t count, ContactPoolReceipt* receipt) {
    return RestoreContactPoolSnapshot(context, expectedFreeArray, snapshot,
        count, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_manifold_pool_capture_snapshot(
    uintptr_t unityBase, uintptr_t context, uint32_t poolKind,
    uintptr_t* snapshot, uint32_t capacity, ManifoldPoolReceipt* receipt) {
    return CaptureManifoldPoolSnapshot(unityBase, context, poolKind, snapshot,
        capacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_manifold_pool_restore_snapshot(
    uintptr_t unityBase, uintptr_t context, uint32_t poolKind,
    uintptr_t expectedPool, const uintptr_t* snapshot, uint32_t count,
    ManifoldPoolReceipt* receipt) {
    return RestoreManifoldPoolSnapshot(unityBase, context, poolKind,
        expectedPool, snapshot, count, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_context_observer_install(
    uintptr_t unityBase, ContactContextObserverReceipt* receipt) {
    return InstallContactManagerContextObserver(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_context_observer_status(
    uintptr_t unityBase, ContactContextObserverReceipt* receipt) {
    return ReadContactManagerContextObserver(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_context_observer_uninstall(
    uintptr_t unityBase, ContactContextObserverReceipt* receipt) {
    return UninstallContactManagerContextObserver(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_install(
    uintptr_t unityBase, DirtyInteractionOrderReceipt* receipt) {
    return InstallDirtyInteractionOrderHook(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_status(
    uintptr_t unityBase, DirtyInteractionOrderReceipt* receipt) {
    return ReadDirtyInteractionOrderStatus(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_capture_arm(
    uintptr_t unityBase, DirtyInteractionOrderReceipt* receipt) {
    return ArmDirtyInteractionOrderCapture(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_capture_copy(
    uintptr_t unityBase, DirtyInteractionKey* keys, uint32_t capacity,
    DirtyInteractionOrderReceipt* receipt) {
    return CopyDirtyInteractionOrderCapture(unityBase, keys, capacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_restore_arm(
    uintptr_t unityBase, uintptr_t expectedNPhaseCore,
    uintptr_t expectedEntries, uintptr_t expectedEntriesNext,
    uintptr_t expectedHash, uint32_t expectedEntriesCapacity,
    uint32_t expectedHashSize, const DirtyInteractionKey* keys,
    uint32_t count, uint32_t restoreMode,
    DirtyInteractionOrderReceipt* receipt) {
    return ArmDirtyInteractionOrderRestore(unityBase, expectedNPhaseCore,
        expectedEntries, expectedEntriesNext, expectedHash,
        expectedEntriesCapacity, expectedHashSize, keys, count, restoreMode,
        receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_cancel(
    uintptr_t unityBase, DirtyInteractionOrderReceipt* receipt) {
    return CancelDirtyInteractionOrder(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_uninstall(
    uintptr_t unityBase, DirtyInteractionOrderReceipt* receipt) {
    return UninstallDirtyInteractionOrderHook(unityBase, receipt);
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) {
    return TRUE;
}
