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
#pragma pack(pop)

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

enum SetGlobalPoseResult : uint32_t {
    SetGlobalPoseOk = 1,
    SetGlobalPoseBadArgument = 2,
    SetGlobalPoseUnreadableRigidbody = 3,
    SetGlobalPoseMissingActor = 4,
    SetGlobalPoseRevisionMismatch = 5,
    SetGlobalPoseNonfinite = 6
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
    KinematicTargetNonfinite = 7
};

static const uint32_t kApiVersion = 6;
static const uint32_t kMaximumShapePoses = 64;
static const uint32_t kMaximumContactManagers = 4096;
static const uint32_t kCleanupRva = 0x481ED0;
static const uint32_t kCreateRva = 0x482510;
static const uint32_t kGetShapesRva = 0xA10740;
static const uint32_t kCreateContactManagerRva = 0xA69E80;
static const uint32_t kNpSetGlobalPoseRva = 0xA143D0;
static const uint32_t kNpGetKinematicTargetRva = 0xA12530;
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
static const uint8_t kNpSetGlobalPoseBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x44};
static const uint8_t kNpGetKinematicTargetBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x44,0xF7,0x81,0x1C,0x01,0x00,0x00,0x00,0x10,0x00,0x00};
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
typedef bool (__thiscall *NpRigidDynamicGetKinematicTarget)(void* self,
    PhysxTransform& pose);
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

static int FailSetGlobalPose(SetGlobalPoseReceipt* receipt,
    SetGlobalPoseResult result, uint32_t error) {
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

static bool Finite(float value) {
    const uint32_t bits = *reinterpret_cast<const uint32_t*>(&value) & 0x7FFFFFFFu;
    return bits < 0x7F800000u;
}

static bool Finite(const RigidPose& pose) {
    for (uint32_t i = 0; i < 3; ++i) if (!Finite(pose.position[i])) return false;
    for (uint32_t i = 0; i < 4; ++i) if (!Finite(pose.rotation[i])) return false;
    return true;
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

extern "C" __declspec(dllexport) int __cdecl oc2_rigidbody_get_kinematic_target(
    uintptr_t unityBase, uintptr_t rigidbody, KinematicTargetReceipt* receipt) {
    return GetExistingActorKinematicTarget(unityBase, rigidbody, receipt);
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

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) {
    return TRUE;
}
