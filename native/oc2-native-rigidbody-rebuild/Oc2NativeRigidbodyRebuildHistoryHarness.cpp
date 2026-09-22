#include <windows.h>
#include <stdint.h>
#include <stdio.h>
#include <string.h>

#if !defined(_M_IX86)
#error This harness requires Win32/x86.
#endif

#pragma pack(push, 8)
struct ContactPoolReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t context, freeArray;
    uint32_t freeCount, orderHashBefore, orderHashAfter;
    uintptr_t top[16];
};

struct ManifoldPoolReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, context, pool;
    uint32_t poolKind;
    uintptr_t freeHeadBefore, freeHeadAfter;
    uint32_t elementSize, elementsPerSlab, used, unreleased, slabSize;
    uint32_t traversedCount, orderHashBefore, orderHashAfter;
    uintptr_t topBefore[16], topAfter[16];
};

struct DirtyInteractionKey {
    uintptr_t elementLow, elementHigh, primaryVtable;
    uint32_t interactionType;
};

struct DirtyInteractionOrderReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, set, entries, entriesNext, hash;
    uint32_t entriesCapacity, hashSize, count, action, captures, restores;
    uint32_t orderHashBefore, orderHashAfter, installed, armed;
    uint32_t restoreMode, matchedCount, capturedOnlyCount, liveOnlyCount;
};

struct ContactContextObserverReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, observedContext;
    uint32_t observations, installed;
};

struct ContactRecreatePlanRow {
    uintptr_t pxsShapeCoreLow, pxsShapeCoreHigh;
    uintptr_t targetManager, targetManifold, targetSip;
    uint32_t targetSlot, manifoldBytes, targetManagerFlags, flags;
};

struct ContactRecreateReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, context, freeArray, largePool;
    uint32_t state, rowCount, matchedCount, remainingCount;
    uint32_t contactCountBefore, targetContactCount, contactCountCurrent;
    uint32_t contactHashBefore, targetContactHash, contactHashCurrent;
    uint32_t largeCountBefore, targetLargeCount, largeCountCurrent;
    uint32_t largeHashBefore, targetLargeHash, largeHashCurrent;
    uint32_t largeUsedBefore, targetLargeUsed, largeUsedCurrent;
    uint32_t largeUnreleasedBefore, targetLargeUnreleased, largeUnreleasedCurrent;
    uint32_t matchedMask, threadId;
    uintptr_t lastSip, lastShapeLow, lastShapeHigh, lastManager, lastManifold;
    uint32_t invalidRow, detail, installed, armed;
    uint32_t observerEntries, attemptOrdinal;
    uintptr_t attemptSip, attemptShapeLow, attemptShapeHigh;
    uint32_t attemptRow, attemptMatchedMask, attemptFreeCount, attemptManagerIndex;
    uintptr_t attemptLargeHead;
    uint32_t attemptManifoldIndex;
    uintptr_t nphaseCore, sipPool;
    uint32_t sipMatchedCount, sipRemainingCount;
    uint32_t targetSipCount, targetSipHash, targetSipUsed,
        targetSipUnreleased;
    uint32_t sipCountCurrent, sipHashCurrent, sipUsedCurrent,
        sipUnreleasedCurrent;
    uint32_t sipMatchedMask, sipObserverEntries;
    uintptr_t lastAllocatedSip;
};

struct ContactRecreateAuditReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, context, nphaseCore, sipPool, freeArray, largePool;
    uint32_t evaluatedMask, issueMask, recreateState, observerInstalled;
    uint32_t rowCount, invalidRowCount, duplicateRowCount;
    uint32_t targetFreeSipRowCount, activeSipRowCount;
    uint32_t targetSipCount, targetSipHash, targetSipUsed,
        targetSipUnreleased;
    uint32_t liveSipCount, liveSipHash, liveSipUsed, liveSipUnreleased;
    uint32_t expectedSemanticSipCount, expectedLegacySipCount;
    uint32_t sipMissingCount, sipExtraCount;
    uint32_t targetContactCount, targetContactHash;
    uint32_t liveContactCount, liveContactHash, expectedContactCount;
    uint32_t contactMissingCount, contactExtraCount;
    uint32_t useBitmapCount, activeBitmapCount, touchBitmapCount,
        modifiableBitmapCount;
    uint32_t targetLargeCount, targetLargeHash, targetLargeUsed,
        targetLargeUnreleased;
    uint32_t liveLargeCount, liveLargeHash, liveLargeUsed,
        liveLargeUnreleased;
    uint32_t expectedLargeCount, largeMissingCount, largeExtraCount;
    uint32_t unwritableCount, firstResult, firstError, firstRow, firstDetail;
};

struct SipPoolReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, pool, freeHead;
    uint32_t elementSize, elementsPerSlab, used, unreleased, slabSize;
    uint32_t traversedCount, orderHash, validationFlags;
    uintptr_t top[16];
};

struct ActorPairPoolReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, pool, freeHead, slabs;
    uint32_t elementSize, elementsPerSlab, used, unreleased, slabSize;
    uint32_t slabCount, totalElements, freeCount, freeOrderHash;
    uint32_t allocatedCount, allocatedOrderHash, validationFlags;
    uintptr_t topFree[16], topAllocated[16];
};

typedef ActorPairPoolReceipt ActorPairReportPoolReceipt;

struct NPhaseReportStateReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, ownerScene, actorPairData;
    uint32_t actorPairCount, actorPairCapacityRaw;
    uintptr_t persistentData;
    uint32_t persistentCount, persistentCapacityRaw;
    uint32_t nextFramePersistentIndex;
    uintptr_t forceThresholdData;
    uint32_t forceThresholdCount, forceThresholdCapacityRaw;
    uintptr_t reportBuffer;
    uint32_t reportBufferCurrentIndex, reportBufferCurrentSize;
    uint32_t reportBufferDefaultSize, reportBufferLastIndex;
    uint32_t reportBufferAllocationLocked;
    uint32_t actorPairOrderHash, persistentOrderHash;
    uint32_t forceThresholdOrderHash, reportBufferActiveHash;
    uint32_t reportBufferAllocationHash;
    uint32_t validationFlags;
};
#pragma pack(pop)

static_assert(sizeof(ManifoldPoolReceipt) == 200,
    "Unexpected Win32 manifold-pool receipt ABI");
static_assert(sizeof(DirtyInteractionKey) == 16,
    "Unexpected Win32 dirty-interaction key ABI");
static_assert(sizeof(DirtyInteractionOrderReceipt) == 96,
    "Unexpected Win32 dirty-interaction receipt ABI");
static_assert(sizeof(ContactRecreatePlanRow) == 36,
    "Unexpected Win32 contact-recreate plan-row ABI");
static_assert(sizeof(ContactRecreateReceipt) == 268,
    "Unexpected Win32 contact-recreate receipt ABI");
static_assert(sizeof(ContactRecreateAuditReceipt) == 232,
    "Unexpected Win32 contact-recreate audit receipt ABI");
static_assert(sizeof(SipPoolReceipt) == 128,
    "Unexpected Win32 shape-pair-pool receipt ABI");
static_assert(sizeof(ActorPairPoolReceipt) == 212,
    "Unexpected Win32 ActorPair-pool receipt ABI");
static_assert(sizeof(NPhaseReportStateReceipt) == 116,
    "Unexpected Win32 NPhase report-state receipt ABI");

typedef uint32_t (__cdecl *ApiVersion)();
typedef int (__cdecl *CaptureSnapshot)(uintptr_t, uintptr_t*, uint32_t,
    ContactPoolReceipt*);
typedef int (__cdecl *RestoreSnapshot)(uintptr_t, uintptr_t,
    const uintptr_t*, uint32_t, ContactPoolReceipt*);
typedef int (__cdecl *CaptureManifoldSnapshot)(uintptr_t, uintptr_t, uint32_t,
    uintptr_t*, uint32_t, ManifoldPoolReceipt*);
typedef int (__cdecl *RestoreManifoldSnapshot)(uintptr_t, uintptr_t, uint32_t,
    uintptr_t, const uintptr_t*, uint32_t, ManifoldPoolReceipt*);
typedef int (__cdecl *DirtyAction)(uintptr_t, DirtyInteractionOrderReceipt*);
typedef int (__cdecl *DirtyLastNPhase)(uintptr_t, uintptr_t*, uint32_t*);
typedef int (__cdecl *DirtyCaptureCopy)(uintptr_t, DirtyInteractionKey*,
    uint32_t, DirtyInteractionOrderReceipt*);
typedef int (__cdecl *DirtyRestoreArm)(uintptr_t, uintptr_t, uintptr_t,
    uintptr_t, uintptr_t, uint32_t, uint32_t,
    const DirtyInteractionKey*, uint32_t, uint32_t,
    DirtyInteractionOrderReceipt*);
typedef void (__thiscall *DirtyUpdate)(void*);
typedef int (__cdecl *ContextObserverAction)(uintptr_t,
    ContactContextObserverReceipt*);
typedef int (__cdecl *ContactRecreateArm)(uintptr_t, uintptr_t, uintptr_t,
    uintptr_t, const uintptr_t*, uint32_t, uint32_t, uint32_t, uintptr_t,
    const uintptr_t*, uint32_t, uintptr_t, const uintptr_t*, uint32_t,
    uint32_t, uint32_t, const ContactRecreatePlanRow*, uint32_t,
    ContactRecreateReceipt*);
typedef int (__cdecl *ContactRecreateAudit)(uintptr_t, uintptr_t, uintptr_t,
    uintptr_t, const uintptr_t*, uint32_t, uint32_t, uint32_t, uintptr_t,
    const uintptr_t*, uint32_t, uintptr_t, const uintptr_t*, uint32_t,
    uint32_t, uint32_t, const ContactRecreatePlanRow*, uint32_t,
    ContactRecreateAuditReceipt*);
typedef int (__cdecl *CaptureSipSnapshot)(uintptr_t, uintptr_t, uintptr_t*,
    uint32_t, SipPoolReceipt*);
typedef int (__cdecl *CaptureActorPairSnapshot)(uintptr_t, uintptr_t,
    uintptr_t*, uint32_t, uintptr_t*, uint32_t, ActorPairPoolReceipt*);
typedef int (__cdecl *CaptureActorPairReportSnapshot)(uintptr_t, uintptr_t,
    uintptr_t*, uint32_t, uintptr_t*, uint32_t,
    ActorPairReportPoolReceipt*);
typedef int (__cdecl *CaptureNPhaseReportState)(uintptr_t, uintptr_t,
    uintptr_t*, uint32_t, uintptr_t*, uint32_t, uintptr_t*, uint32_t,
    uint8_t*, uint32_t, NPhaseReportStateReceipt*);
typedef int (__cdecl *ContactRecreateStatus)(uintptr_t, uintptr_t,
    ContactRecreateReceipt*);
typedef int (__cdecl *ContactRecreateCancel)(uintptr_t,
    ContactRecreateReceipt*);
typedef uintptr_t (__thiscall *FakeCreateManager)(void*, void*, void*);
typedef uintptr_t (__thiscall *FakeCreateSip)(void*, void*, void*, uint32_t);

struct FakeCreateWorkerCall {
    FakeCreateManager create;
    void* context;
    void* descriptor;
    DWORD threadId;
    uintptr_t result;
};

static DWORD WINAPI RunFakeCreateWorker(void* value) {
    FakeCreateWorkerCall* call = static_cast<FakeCreateWorkerCall*>(value);
    call->threadId = GetCurrentThreadId();
    call->result = call->create(call->context, call->descriptor, 0);
    return 0;
}

static bool InvokeFakeCreateOnWorker(FakeCreateManager create, void* context,
    void* descriptor, DWORD& threadId) {
    FakeCreateWorkerCall call = {create, context, descriptor, 0, 0};
    HANDLE thread = CreateThread(0, 0, RunFakeCreateWorker, &call, 0, 0);
    if (!thread) return false;
    const DWORD wait = WaitForSingleObject(thread, 10000);
    DWORD exitCode = 1;
    const BOOL readExit = GetExitCodeThread(thread, &exitCode);
    CloseHandle(thread);
    threadId = call.threadId;
    return wait == WAIT_OBJECT_0 && readExit && exitCode == 0;
}

static int failures = 0;

static void Check(bool value, const char* message) {
    if (value) return;
    ++failures;
    printf("FAIL: %s\n", message);
}

static bool Same(const uintptr_t* left, const uintptr_t* right,
    uint32_t count) {
    for (uint32_t i = 0; i < count; ++i)
        if (left[i] != right[i]) return false;
    return true;
}

static void CopyBytes(uint8_t* destination, const uint8_t* source,
    uint32_t count) {
    for (uint32_t i = 0; i < count; ++i) destination[i] = source[i];
}

static const uint32_t kLargePoolAllocatorRva = 0xA69A90;
static const uint32_t kSpherePoolAllocatorRva = 0xA69AC0;
static const uint32_t kLargePoolSlabRva = 0xA69BEA;
static const uint32_t kSpherePoolSlabRva = 0xA69CCA;
static const uint32_t kLargePoolCallsiteRva = 0xA69F15;
static const uint32_t kSpherePoolCallsiteRva = 0xA69F32;
static const uint32_t kDirtyUpdateRva = 0xA540F0;
static const uint32_t kCreateManagerRva = 0xA69E80;
static const uint32_t kCreateShapeInstancePairRva = 0xA4E560;
static const uint32_t kFindActorPairRva = 0xA4F7B0;
static const uint32_t kActorPairSlabRva = 0xA4D9BA;
static const uint32_t kCreateActorPairReportDataRva = 0xA4E370;
static const uint32_t kActorPairReportSlabStrideRva = 0xA4DAC4;
static const uint32_t kReleaseActorPairReportDataRva = 0xA522A0;
static const uint32_t kAddPersistentContactEventPairRva = 0xA4CD90;
static const uint32_t kRemovePersistentContactEventPairRva = 0xA53840;
static const uint32_t kContactReportBufferAllocateRva = 0xA53950;
static const uint32_t kInitManagerRva = 0xA7E5F0;
static const uint32_t kCreateSipRva = 0xA54430;
static const uint32_t kGetShapeTypeRva = 0x842360;
static const uint8_t kCreateManagerBytes[] = {0x55,0x8B,0xEC,0x53,0x8B,0xD9};
static const uint8_t kCreateShapeInstancePairBytes[] = {
    0x55,0x8B,0xEC,0x51,0x53,0x8B,0x5D,0x08
};
static const uint8_t kCreateShapeInstancePairPoolBytes[] = {
    0x81,0xC6,0xE0,0x02,0x00,0x00,0x8B,0xD8,
    0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x07,0x8B,0xCE
};
static const uint8_t kFindActorPairBytes[] = {
    0x55,0x8B,0xEC,0x51,0x53,0x8B,0x5D,0x08
};
static const uint8_t kFindActorPairPoolBytes[] = {
    0x81,0xC7,0x90,0x00,0x00,0x00,0x83,0xBF,
    0x24,0x01,0x00,0x00,0x00,0x75,0x07
};
static const uint8_t kActorPairSlabStrideBytes[] = {
    0x8B,0x8E,0x14,0x01,0x00,0x00,0x49,0x8D,
    0x0C,0x49,0x8D,0x0C,0xCF
};
static const uint8_t kCreateActorPairReportDataBytes[] = {
    0x81,0xC1,0x30,0x05,0x00,0x00,0xE9,0x65,0xFD,0xFF,0xFF
};
static const uint8_t kActorPairReportSlabStrideBytes[] = {
    0x83,0xE9,0x24,0x3B,0xCF,0x73,0xE5
};
static const uint8_t kReleaseActorPairReportDataBytes[] = {
    0x55,0x8B,0xEC,0x56,0x8D,0xB1,0x30,0x05,0x00,0x00
};
static const uint8_t kAddPersistentContactEventPairBytes[] = {
    0x55,0x8B,0xEC,0x53,0x8B,0x5D,0x08,0x56,0x8B,0xF1,
    0x81,0x4B,0x2C,0x00,0x00,0x20,0x00,0x8B,0x4E,0x14,
    0x8B,0x56,0x1C,0x3B,0xCA
};
static const uint8_t kRemovePersistentContactEventPairBytes[] = {
    0x55,0x8B,0xEC,0x53,0x8B,0x5D,0x08,0x8B,0xD1,0x56,
    0x8B,0x73,0x34,0x8B,0x42,0x1C,0x3B,0xF0
};
static const uint8_t kContactReportBufferAllocateBytes[] = {
    0x55,0x8B,0xEC,0x51,0x8B,0x55,0x0C,0x53,0x57,0x8B,
    0xF9,0xF6,0xC2,0x0F,0x74,0x06
};
static const uint8_t kContactReportBufferLayoutBytes[] = {
    0x8B,0x47,0x30,0x8B,0x55,0x10,0xC1,0xE3,0x04,
    0x8D,0x48,0x0F,0x83,0xE1,0xF0
};
static const uint8_t kCreateManagerPoolBytes[] = {
    0x83,0xBB,0xCC,0x02,0x00,0x00,0x00,0x56,0x8D,0xB3,0xB8,0x02,0x00,0x00
};
static const uint8_t kCreateManagerPopBytes[] = {
    0xFF,0x4E,0x14,0x8B,0x4E,0x14,0x8B,0x46,0x10,0x57,0xFF,0x75,
    0x0C,0x8B,0x3C,0x88,0x8B,0x46,0x20,0xFF,0x75,0x08,0x8B,0x57,
    0x4C,0x8B,0xCA,0xC1,0xE9,0x05,0x83,0xE2,0x1F,0x8D,0x0C,0x88,
    0x8B,0x01,0x0F,0xAB,0xD0,0x89,0x01,0x8B
};
static const uint8_t kInitManagerBytes[] = {
    0x55,0x8B,0xEC,0x56,0x8B,0x75,0x08,0x8B,0x46,0x0C,0x89,0x01
};
static const uint8_t kInitManagerUserDataBytes[] = {
    0x8B,0x06,0x89,0x41,0x0C,0x33,0xC0,0x66,0x89,0x41,0x72,
    0x66,0x89,0x41,0x24
};
static const uint8_t kCreateSipBytes[] = {
    0x55,0x8B,0xEC,0x81,0xEC,0x94,0x00,0x00,0x00,0x53,0x8B,0xD9,
    0x56,0x89,0x5D,0xC8,0x8B,0x4B,0x20,0xE8,0xA8,0x3A,0xFF,0xFF,
    0x8B,0x73,0x20,0x89,0x45,0xEC,0x8B,0x43,0x24,0x89,0x45,0xFC,
    0x8B
};
static const uint8_t kCreateSipShapeBytes[] = {
    0x8B,0x46,0x1C,0x83,0xC0,0x20,0x89,0x45,0x80,0x8B,0x43,0x1C,
    0x83,0xC0,0x20,0x89,0x7D,0xB4,0x83
};
static const uint8_t kGetShapeTypeBytes[] = {0x8B,0x41,0x74,0xC3};
static const uint8_t kDirtyUpdateFunctionBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x34,0x8B,0xE5,0x5D,0xC3
};
static const uint8_t kLargePoolAllocatorBytes[] = {
    0x56,0x8B,0xF1,0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x05,
    0xE8,0xAF,0x00,0x00,0x00,0x8B,0x86,0x24,0x01,0x00,0x00,0x8B,
    0x08,0xFF,0x86,0x18,0x01,0x00,0x00,0xFF,0x8E,0x1C,0x01,0x00,
    0x00,0x89,0x8E,0x24,0x01,0x00,0x00,0x5E,0xC3
};
static const uint8_t kSpherePoolAllocatorBytes[] = {
    0x56,0x8B,0xF1,0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x05,
    0xE8,0x5F,0x01,0x00,0x00,0x8B,0x86,0x24,0x01,0x00,0x00,0x8B,
    0x08,0xFF,0x86,0x18,0x01,0x00,0x00,0xFF,0x8E,0x1C,0x01,0x00,
    0x00,0x89,0x8E,0x24,0x01,0x00,0x00,0x5E,0xC3
};
static const uint8_t kLargePoolSlabBytes[] = {
    0x69,0x8E,0x14,0x01,0x00,0x00,0xF0,0x00,0x00,0x00,0x81,0xC1,
    0x10,0xFF,0xFF,0xFF,0x03,0xCF,0x3B,0xCF,0x72,0x1E,0x8B,0x86,
    0x24,0x01,0x00,0x00,0x89,0x01,0xFF,0x86,0x1C,0x01,0x00,0x00,
    0x89,0x8E,0x24,0x01,0x00,0x00,0x81,0xE9,0xF0,0x00,0x00,0x00,
    0x3B,0xCF,0x73,0xE2
};
static const uint8_t kSpherePoolSlabBytes[] = {
    0x8B,0x86,0x14,0x01,0x00,0x00,0x8D,0x0C,0x40,0xC1,0xE1,0x05,
    0x83,0xC1,0xA0,0x03,0xCF,0x3B,0xCF,0x72,0x1C,0x90,0x8B,0x86,
    0x24,0x01,0x00,0x00,0x89,0x01,0xFF,0x86,0x1C,0x01,0x00,0x00,
    0x89,0x8E,0x24,0x01,0x00,0x00,0x83,0xE9,0x60,0x3B,0xCF,0x73,
    0xE5
};
static const uint8_t kLargePoolCallsiteBytes[] = {
    0x8D,0x8B,0xE4,0x02,0x00,0x00,0xE8,0x70,0xFB,0xFF,0xFF
};
static const uint8_t kSpherePoolCallsiteBytes[] = {
    0x8D,0x8B,0x0C,0x04,0x00,0x00,0xE8,0x83,0xFB,0xFF,0xFF
};

static uint8_t* CreateRevisionImage() {
    const uint32_t size = 0xA80000;
    uint8_t* image = static_cast<uint8_t*>(VirtualAlloc(0, size,
        MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!image) return 0;
    CopyBytes(image + kLargePoolAllocatorRva, kLargePoolAllocatorBytes,
        sizeof(kLargePoolAllocatorBytes));
    CopyBytes(image + kSpherePoolAllocatorRva, kSpherePoolAllocatorBytes,
        sizeof(kSpherePoolAllocatorBytes));
    CopyBytes(image + kLargePoolSlabRva, kLargePoolSlabBytes,
        sizeof(kLargePoolSlabBytes));
    CopyBytes(image + kSpherePoolSlabRva, kSpherePoolSlabBytes,
        sizeof(kSpherePoolSlabBytes));
    CopyBytes(image + kLargePoolCallsiteRva, kLargePoolCallsiteBytes,
        sizeof(kLargePoolCallsiteBytes));
    CopyBytes(image + kSpherePoolCallsiteRva, kSpherePoolCallsiteBytes,
        sizeof(kSpherePoolCallsiteBytes));
    CopyBytes(image + kDirtyUpdateRva, kDirtyUpdateFunctionBytes,
        sizeof(kDirtyUpdateFunctionBytes));
    CopyBytes(image + kCreateManagerRva, kCreateManagerBytes,
        sizeof(kCreateManagerBytes));
    CopyBytes(image + kCreateManagerRva + 0x06, kCreateManagerPoolBytes,
        sizeof(kCreateManagerPoolBytes));
    CopyBytes(image + kCreateManagerRva + 0x29, kCreateManagerPopBytes,
        sizeof(kCreateManagerPopBytes));
    // The harness executes only the patched entry/preparation hook.  Its
    // trampoline returns through this small valid epilogue instead of running
    // the copied disassembly-signature bytes used solely by revision guards.
    image[kCreateManagerRva + 0x14] = 0xE9;
    *reinterpret_cast<int32_t*>(image + kCreateManagerRva + 0x15) =
        static_cast<int32_t>(0x80 - 0x19);
    const uint8_t epilogue[] = {0x5E,0x5B,0x5D,0xC2,0x08,0x00};
    CopyBytes(image + kCreateManagerRva + 0x80, epilogue,
        sizeof(epilogue));
    CopyBytes(image + kCreateShapeInstancePairRva,
        kCreateShapeInstancePairBytes,
        sizeof(kCreateShapeInstancePairBytes));
    image[kCreateShapeInstancePairRva + 0x08] = 0xE9;
    *reinterpret_cast<int32_t*>(image + kCreateShapeInstancePairRva + 0x09) =
        static_cast<int32_t>(0x90 - 0x0D);
    CopyBytes(image + kCreateShapeInstancePairRva + 0x65,
        kCreateShapeInstancePairPoolBytes,
        sizeof(kCreateShapeInstancePairPoolBytes));
    CopyBytes(image + kFindActorPairRva, kFindActorPairBytes,
        sizeof(kFindActorPairBytes));
    CopyBytes(image + kFindActorPairRva + 0x91,
        kFindActorPairPoolBytes, sizeof(kFindActorPairPoolBytes));
    CopyBytes(image + kActorPairSlabRva,
        kActorPairSlabStrideBytes, sizeof(kActorPairSlabStrideBytes));
    CopyBytes(image + kCreateActorPairReportDataRva,
        kCreateActorPairReportDataBytes,
        sizeof(kCreateActorPairReportDataBytes));
    CopyBytes(image + kActorPairReportSlabStrideRva,
        kActorPairReportSlabStrideBytes,
        sizeof(kActorPairReportSlabStrideBytes));
    CopyBytes(image + kReleaseActorPairReportDataRva,
        kReleaseActorPairReportDataBytes,
        sizeof(kReleaseActorPairReportDataBytes));
    CopyBytes(image + kAddPersistentContactEventPairRva,
        kAddPersistentContactEventPairBytes,
        sizeof(kAddPersistentContactEventPairBytes));
    CopyBytes(image + kRemovePersistentContactEventPairRva,
        kRemovePersistentContactEventPairBytes,
        sizeof(kRemovePersistentContactEventPairBytes));
    CopyBytes(image + kContactReportBufferAllocateRva,
        kContactReportBufferAllocateBytes,
        sizeof(kContactReportBufferAllocateBytes));
    CopyBytes(image + kContactReportBufferAllocateRva + 0x1F,
        kContactReportBufferLayoutBytes,
        sizeof(kContactReportBufferLayoutBytes));
    // ECX was saved by the copied prologue at [EBP-4]. Pop one 0x44-byte SIP
    // from NPhaseCore::mLLSipPool, update PxPool counters, and preserve the
    // real thiscall/RET 0x0c contract used by the hook trampoline.
    const uint8_t sipEpilogue[] = {
        0x8B,0x4D,0xFC,                         // mov ecx,[ebp-4]
        0x81,0xC1,0xE0,0x02,0x00,0x00,          // add ecx,2e0h
        0x8B,0x81,0x24,0x01,0x00,0x00,          // mov eax,[ecx+124h]
        0x8B,0x10,                               // mov edx,[eax]
        0xFF,0x81,0x18,0x01,0x00,0x00,          // inc [ecx+118h]
        0xFF,0x89,0x1C,0x01,0x00,0x00,          // dec [ecx+11ch]
        0x89,0x91,0x24,0x01,0x00,0x00,          // mov [ecx+124h],edx
        0x5B,                                    // pop ebx
        0x8B,0xE5,                               // mov esp,ebp
        0x5D,                                    // pop ebp
        0xC2,0x0C,0x00                           // ret 0ch
    };
    CopyBytes(image + kCreateShapeInstancePairRva + 0x90, sipEpilogue,
        sizeof(sipEpilogue));
    CopyBytes(image + kInitManagerRva, kInitManagerBytes,
        sizeof(kInitManagerBytes));
    CopyBytes(image + kInitManagerRva + 0x172,
        kInitManagerUserDataBytes, sizeof(kInitManagerUserDataBytes));
    CopyBytes(image + kCreateSipRva, kCreateSipBytes,
        sizeof(kCreateSipBytes));
    CopyBytes(image + kCreateSipRva + 0x1E6, kCreateSipShapeBytes,
        sizeof(kCreateSipShapeBytes));
    CopyBytes(image + kGetShapeTypeRva, kGetShapeTypeBytes,
        sizeof(kGetShapeTypeBytes));
    return image;
}

static void InitializeManifoldPool(uint8_t* context, uint32_t poolKind,
    uint8_t nodes[4][0xF0]) {
    const uint32_t poolOffset = poolKind == 0 ? 0x2E4 : 0x40C;
    const uint32_t elementSize = poolKind == 0 ? 0xF0 : 0x60;
    uint8_t* pool = context + poolOffset;
    *reinterpret_cast<uint32_t*>(pool + 0x114) = 4;
    *reinterpret_cast<uint32_t*>(pool + 0x118) = 1;
    // releaseEmptySlabs can rebuild this chain and then reset the telemetry
    // counter. Deliberately keep it unequal to the traversed count.
    *reinterpret_cast<uint32_t*>(pool + 0x11C) = 0;
    *reinterpret_cast<uint32_t*>(pool + 0x120) = 4 * elementSize;
    *reinterpret_cast<uintptr_t*>(nodes[0]) = reinterpret_cast<uintptr_t>(nodes[1]);
    *reinterpret_cast<uintptr_t*>(nodes[1]) = reinterpret_cast<uintptr_t>(nodes[2]);
    *reinterpret_cast<uintptr_t*>(nodes[2]) = 0;
    *reinterpret_cast<uintptr_t*>(pool + 0x124) =
        reinterpret_cast<uintptr_t>(nodes[0]);
}

static bool ManifoldOrderMatches(uint8_t* context, uint32_t poolKind,
    const uintptr_t* expected, uint32_t count) {
    const uint32_t poolOffset = poolKind == 0 ? 0x2E4 : 0x40C;
    uintptr_t current = *reinterpret_cast<uintptr_t*>(
        context + poolOffset + 0x124);
    for (uint32_t i = 0; i < count; ++i) {
        if (current != expected[i]) return false;
        current = *reinterpret_cast<uintptr_t*>(current);
    }
    return current == 0;
}

static uint32_t DirtyPointerHash(uintptr_t pointer) {
    uint32_t value = static_cast<uint32_t>(pointer);
    value += ~(value << 15);
    value ^= value >> 10;
    value += value << 3;
    value ^= value >> 6;
    value += ~(value << 11);
    value ^= value >> 16;
    return value;
}

static void BuildDirtyHash(const uintptr_t* entries, uint32_t count,
    uint32_t* next, uint32_t capacity, uint32_t* hash, uint32_t hashSize) {
    for (uint32_t i = 0; i < hashSize; ++i) hash[i] = 0xFFFFFFFFu;
    for (uint32_t i = 0; i < capacity; ++i) next[i] = 0xCCCCCCCCu;
    for (uint32_t i = 0; i < count; ++i) {
        const uint32_t bucket = DirtyPointerHash(entries[i]) & (hashSize - 1);
        next[i] = hash[bucket];
        hash[bucket] = i;
    }
}

static void SetDirtyInteraction(uint8_t* interaction, uintptr_t vtable,
    uint32_t type, uintptr_t element0, uintptr_t element1) {
    for (uint32_t i = 0; i < 0x28; ++i) interaction[i] = 0;
    *reinterpret_cast<uintptr_t*>(interaction + 0x00) = vtable;
    *reinterpret_cast<uint16_t*>(interaction + 0x04) = 0xFFFFu;
    *reinterpret_cast<uint16_t*>(interaction + 0x06) = 3u;
    *reinterpret_cast<uintptr_t*>(interaction + 0x08) = vtable + 0x10;
    *reinterpret_cast<uint8_t*>(interaction + 0x1C) =
        static_cast<uint8_t>(type);
    *reinterpret_cast<uintptr_t*>(interaction + 0x20) = element0;
    *reinterpret_cast<uintptr_t*>(interaction + 0x24) = element1;
}

static DirtyInteractionKey DirtyKey(const uint8_t* interaction) {
    const uintptr_t element0 =
        *reinterpret_cast<const uintptr_t*>(interaction + 0x20);
    const uintptr_t element1 =
        *reinterpret_cast<const uintptr_t*>(interaction + 0x24);
    DirtyInteractionKey key = {
        element0 < element1 ? element0 : element1,
        element0 < element1 ? element1 : element0,
        *reinterpret_cast<const uintptr_t*>(interaction),
        *reinterpret_cast<const uint8_t*>(interaction + 0x1C)
    };
    return key;
}

static bool SameDirtyKey(const DirtyInteractionKey& left,
    const DirtyInteractionKey& right) {
    return left.elementLow == right.elementLow &&
        left.elementHigh == right.elementHigh &&
        left.primaryVtable == right.primaryVtable &&
        left.interactionType == right.interactionType;
}

static bool DirtyHashValid(const uintptr_t* entries, uint32_t count,
    const uint32_t* next, const uint32_t* hash, uint32_t hashSize) {
    bool seen[8] = {};
    uint32_t visited = 0;
    for (uint32_t bucket = 0; bucket < hashSize; ++bucket) {
        uint32_t index = hash[bucket];
        while (index != 0xFFFFFFFFu) {
            if (index >= count || seen[index] ||
                (DirtyPointerHash(entries[index]) & (hashSize - 1)) != bucket)
                return false;
            seen[index] = true;
            ++visited;
            index = next[index];
        }
    }
    return visited == count;
}

static void RunDirtyInteractionTests(uint8_t* image, DirtyAction install,
    DirtyAction status, DirtyLastNPhase lastNPhase, DirtyAction armCapture,
    DirtyCaptureCopy copyCapture,
    DirtyRestoreArm armRestore, DirtyAction cancel, DirtyAction uninstall) {
    const uint32_t capacity = 8;
    const uint32_t hashSize = 16;
    uint8_t nphase[0x80] = {};
    uint8_t scene[0x4A5] = {};
    __declspec(align(16)) uint8_t storage[256] = {};
    __declspec(align(16)) uint8_t replacementStorage[512] = {};
    uint8_t interactions[4][0x28] = {};
    uintptr_t elements[8] = {};
    *reinterpret_cast<uintptr_t*>(nphase) =
        reinterpret_cast<uintptr_t>(scene);
    uint8_t* set = nphase + 0x44;
    uint32_t* hash = reinterpret_cast<uint32_t*>(storage);
    uint32_t* next = hash + hashSize;
    uintptr_t entriesAddress =
        (reinterpret_cast<uintptr_t>(next + capacity) + 15u) &
        ~static_cast<uintptr_t>(15u);
    uintptr_t* entries = reinterpret_cast<uintptr_t*>(entriesAddress);
    *reinterpret_cast<uintptr_t*>(set + 0x00) =
        reinterpret_cast<uintptr_t>(storage);
    *reinterpret_cast<uintptr_t*>(set + 0x04) = entriesAddress;
    *reinterpret_cast<uintptr_t*>(set + 0x08) =
        reinterpret_cast<uintptr_t>(next);
    *reinterpret_cast<uintptr_t*>(set + 0x0C) =
        reinterpret_cast<uintptr_t>(hash);
    *reinterpret_cast<uint32_t*>(set + 0x10) = capacity;
    *reinterpret_cast<uint32_t*>(set + 0x14) = hashSize;
    *reinterpret_cast<uint32_t*>(set + 0x18) = 0x3F400000u;
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 3;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 3;

    const uintptr_t vtable = 0x12345000u;
    SetDirtyInteraction(interactions[0], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[0]),
        reinterpret_cast<uintptr_t>(&elements[1]));
    SetDirtyInteraction(interactions[1], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[2]),
        reinterpret_cast<uintptr_t>(&elements[3]));
    SetDirtyInteraction(interactions[2], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[4]),
        reinterpret_cast<uintptr_t>(&elements[5]));
    entries[0] = reinterpret_cast<uintptr_t>(interactions[0]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[1]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[2]);
    BuildDirtyHash(entries, 3, next, capacity, hash, hashSize);

    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    DirtyInteractionOrderReceipt receipt = {};
    Check(install(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.installed == 1, "dirty hook installs");
    uintptr_t observedNPhase = 1;
    uint32_t observations = 1;
    Check(lastNPhase(imagePointer, &observedNPhase, &observations) == 0 &&
        observedNPhase == 0 && observations == 0,
        "passive nphase is unavailable before the first hook entry");
    Check(armCapture(imagePointer, &receipt) == 1 && receipt.result == 18 &&
        receipt.armed == 1, "dirty capture arms");
    DirtyUpdate update = reinterpret_cast<DirtyUpdate>(
        image + kDirtyUpdateRva);
    update(nphase);
    Check(lastNPhase(imagePointer, &observedNPhase, &observations) == 1 &&
        observedNPhase == reinterpret_cast<uintptr_t>(nphase) &&
        observations == 1,
        "passive nphase observes the first hook entry");
    uintptr_t repeatedNPhase = 0;
    uint32_t repeatedObservations = 0;
    Check(lastNPhase(imagePointer, &repeatedNPhase,
        &repeatedObservations) == 1 &&
        repeatedNPhase == observedNPhase &&
        repeatedObservations == observations,
        "passive nphase getter is repeatable and read-only");
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.count == 3 && receipt.captures == 1 && receipt.armed == 0,
        "dirty capture completes one-shot");
    update(nphase);
    Check(lastNPhase(imagePointer, &observedNPhase, &observations) == 1 &&
        observedNPhase == reinterpret_cast<uintptr_t>(nphase) &&
        observations == 2,
        "passive nphase advances while the transaction hook is idle");
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.captures == 1 && receipt.armed == 0 &&
        receipt.nphaseCore == reinterpret_cast<uintptr_t>(nphase),
        "dirty capture is dormant after one call");
    DirtyInteractionOrderReceipt capturedReceipt = receipt;
    DirtyInteractionKey captured[3] = {};
    Check(copyCapture(imagePointer, captured, 3, &receipt) == 1 &&
        receipt.result == 1, "dirty capture copies semantic order");
    Check(SameDirtyKey(captured[0], DirtyKey(interactions[0])) &&
        SameDirtyKey(captured[1], DirtyKey(interactions[1])) &&
        SameDirtyKey(captured[2], DirtyKey(interactions[2])),
        "dirty capture uses element identity");

    // Cancel while idle is a true no-op: reset cleanup must not erase the
    // receipt or semantic snapshot from an already completed one-shot capture.
    const uint32_t capturesBeforeIdleCancel = receipt.captures;
    Check(cancel(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.armed == 0 && receipt.captures == capturesBeforeIdleCancel,
        "dirty idle cancel preserves completed capture");
    Check(copyCapture(imagePointer, captured, 3, &receipt) == 1 &&
        receipt.result == 1, "dirty capture remains copyable after idle cancel");

    // A pending action can still be cancelled.  The hook must remain dormant
    // on the following update and must not increment the one-shot counter.
    Check(armCapture(imagePointer, &receipt) == 1 && receipt.result == 18 &&
        receipt.armed == 1, "dirty second capture arms before cancel");
    Check(cancel(imagePointer, &receipt) == 1 && receipt.result == 10 &&
        receipt.lastError == ERROR_CANCELLED && receipt.armed == 0,
        "dirty pending capture cancels");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 10 &&
        receipt.captures == capturesBeforeIdleCancel && receipt.armed == 0,
        "dirty cancelled capture stays dormant");

    // Reassign pooled objects to different element pairs and scramble dense
    // order. Restore must select current pointers by semantic key, never copy
    // the stale captured CoreInteraction addresses.  Replace the entire
    // CoalescedHashSet backing allocation and grow both capacities as well;
    // those implementation details are allowed to change while NPhaseCore and
    // semantic membership remain exact.
    SetDirtyInteraction(interactions[0], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[2]),
        reinterpret_cast<uintptr_t>(&elements[3]));
    SetDirtyInteraction(interactions[1], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[4]),
        reinterpret_cast<uintptr_t>(&elements[5]));
    SetDirtyInteraction(interactions[2], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[0]),
        reinterpret_cast<uintptr_t>(&elements[1]));
    const uint32_t replacementCapacity = 12;
    const uint32_t replacementHashSize = 32;
    hash = reinterpret_cast<uint32_t*>(replacementStorage);
    next = hash + replacementHashSize;
    entriesAddress = (reinterpret_cast<uintptr_t>(
        next + replacementCapacity) + 15u) & ~static_cast<uintptr_t>(15u);
    entries = reinterpret_cast<uintptr_t*>(entriesAddress);
    *reinterpret_cast<uintptr_t*>(set + 0x00) =
        reinterpret_cast<uintptr_t>(replacementStorage);
    *reinterpret_cast<uintptr_t*>(set + 0x04) = entriesAddress;
    *reinterpret_cast<uintptr_t*>(set + 0x08) =
        reinterpret_cast<uintptr_t>(next);
    *reinterpret_cast<uintptr_t*>(set + 0x0C) =
        reinterpret_cast<uintptr_t>(hash);
    *reinterpret_cast<uint32_t*>(set + 0x10) = replacementCapacity;
    *reinterpret_cast<uint32_t*>(set + 0x14) = replacementHashSize;
    entries[0] = reinterpret_cast<uintptr_t>(interactions[1]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[2]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[0]);
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);

    // Exact semantic membership is not sufficient if the set belongs to a
    // different NPhaseCore.  Even an otherwise coherent alias must fail
    // closed without touching its container.
    uint8_t foreignNphase[0x80] = {};
    *reinterpret_cast<uintptr_t*>(foreignNphase) =
        reinterpret_cast<uintptr_t>(scene);
    CopyBytes(foreignNphase + 0x44, set, 0x28);
    uintptr_t foreignBefore[3] = {entries[0], entries[1], entries[2]};
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1,
        "dirty foreign-nphase restore arms");
    update(foreignNphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 15 &&
        receipt.armed == 0 &&
        receipt.nphaseCore == reinterpret_cast<uintptr_t>(foreignNphase) &&
        receipt.entries == entriesAddress &&
        Same(entries, foreignBefore, 3),
        "dirty restore rejects a different nphase core");

    // The same NPhaseCore with a coherent but shorter current set is also not
    // the captured semantic set.  Count mismatch is rejected before writes.
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 2;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 2;
    BuildDirtyHash(entries, 2, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1,
        "dirty count-mismatch restore arms");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 15 &&
        receipt.armed == 0 && receipt.count == 2 &&
        receipt.entriesCapacity == replacementCapacity &&
        receipt.hashSize == replacementHashSize &&
        entries[0] == foreignBefore[0] &&
        entries[1] == foreignBefore[1],
        "dirty restore rejects semantic count mismatch");
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 3;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 3;
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);

    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1 &&
        receipt.result == 18, "dirty restore arms");
    update(nphase);
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 1 && receipt.orderHashBefore !=
        receipt.orderHashAfter && receipt.restoreMode == 1 &&
        receipt.matchedCount == 3 && receipt.capturedOnlyCount == 0 &&
        receipt.liveOnlyCount == 0, "dirty restore completes one-shot");
    Check(receipt.nphaseCore == capturedReceipt.nphaseCore &&
        receipt.entries == entriesAddress &&
        receipt.entriesNext == reinterpret_cast<uintptr_t>(next) &&
        receipt.hash == reinterpret_cast<uintptr_t>(hash) &&
        receipt.entriesCapacity == replacementCapacity &&
        receipt.hashSize == replacementHashSize,
        "dirty restore receipt reports replacement storage");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[0]) &&
        entries[2] == reinterpret_cast<uintptr_t>(interactions[1]),
        "dirty restore maps semantics to current pooled pointers");
    Check(DirtyHashValid(entries, 3, next, hash, replacementHashSize),
        "dirty restore rebuilds valid pointer hash chains");
    uintptr_t oneShot[3] = {entries[0], entries[1], entries[2]};
    update(nphase);
    Check(Same(entries, oneShot, 3), "dirty restore is dormant after one call");

    // Missing semantic membership must leave every container array untouched.
    entries[0] = reinterpret_cast<uintptr_t>(interactions[1]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[2]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[0]);
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 1, &receipt) == 1,
        "dirty mismatch restore arms");
    SetDirtyInteraction(interactions[1], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[1]),
        reinterpret_cast<uintptr_t>(&elements[4]));
    uintptr_t entriesBefore[3] = {entries[0], entries[1], entries[2]};
    uint32_t nextBefore[3] = {next[0], next[1], next[2]};
    uint32_t hashBefore[32] = {};
    CopyBytes(reinterpret_cast<uint8_t*>(hashBefore),
        reinterpret_cast<const uint8_t*>(hash), sizeof(hashBefore));
    update(nphase);
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 14 &&
        receipt.armed == 0, "dirty membership mismatch fails closed");
    Check(Same(entries, entriesBefore, 3) &&
        next[0] == nextBefore[0] && next[1] == nextBefore[1] &&
        next[2] == nextBefore[2] &&
        memcmp(hash, hashBefore, sizeof(hashBefore)) == 0,
        "dirty membership rejection is non-mutating");

    // Projection mode permits a branch-local replacement without inventing
    // a position for it. The new D entry remains at dense slot 1 while the A
    // and B survivor slots are refilled in checkpoint-relative order.
    entries[0] = reinterpret_cast<uintptr_t>(interactions[0]); // B
    entries[1] = reinterpret_cast<uintptr_t>(interactions[1]); // D
    entries[2] = reinterpret_cast<uintptr_t>(interactions[2]); // A
    BuildDirtyHash(entries, 3, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 2, &receipt) == 1 &&
        receipt.result == 18 && receipt.restoreMode == 2,
        "dirty projection restore arms explicitly");
    update(nphase);
    receipt = {};
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 2 && receipt.restoreMode == 2 &&
        receipt.matchedCount == 2 && receipt.capturedOnlyCount == 1 &&
        receipt.liveOnlyCount == 1,
        "dirty projection reports survivor accounting");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[1]) &&
        entries[2] == reinterpret_cast<uintptr_t>(interactions[0]),
        "dirty projection preserves current-only slot and orders survivors");
    Check(DirtyHashValid(entries, 3, next, hash, replacementHashSize),
        "dirty projection rebuilds valid pointer hash chains");
    uintptr_t projectedOneShot[3] = {entries[0], entries[1], entries[2]};
    update(nphase);
    Check(Same(entries, projectedOneShot, 3),
        "dirty projection is dormant after one call");

    // A larger live set keeps its one current-only slot while all three
    // checkpoint interactions survive. This exercises count growth and a
    // replacement backing allocation in projection mode.
    SetDirtyInteraction(interactions[3], vtable, 3,
        reinterpret_cast<uintptr_t>(&elements[4]),
        reinterpret_cast<uintptr_t>(&elements[5])); // C
    entries[0] = reinterpret_cast<uintptr_t>(interactions[3]); // C
    entries[1] = reinterpret_cast<uintptr_t>(interactions[1]); // D
    entries[2] = reinterpret_cast<uintptr_t>(interactions[0]); // B
    entries[3] = reinterpret_cast<uintptr_t>(interactions[2]); // A
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 4;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 4;
    BuildDirtyHash(entries, 4, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 2, &receipt) == 1,
        "dirty projection with live addition arms");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 3 && receipt.count == 4 &&
        receipt.matchedCount == 3 && receipt.capturedOnlyCount == 0 &&
        receipt.liveOnlyCount == 1,
        "dirty projection accepts a larger live set");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[1]) &&
        entries[2] == reinterpret_cast<uintptr_t>(interactions[0]) &&
        entries[3] == reinterpret_cast<uintptr_t>(interactions[3]) &&
        DirtyHashValid(entries, 4, next, hash, replacementHashSize),
        "dirty projection orders survivors around a live-only slot");

    // A smaller live set contains A and B only. Missing checkpoint C is
    // reported, while the survivor slots still recover A-before-B order.
    entries[0] = reinterpret_cast<uintptr_t>(interactions[0]); // B
    entries[1] = reinterpret_cast<uintptr_t>(interactions[2]); // A
    *reinterpret_cast<uint32_t*>(set + 0x1C) = 2;
    *reinterpret_cast<uint32_t*>(set + 0x24) = 2;
    BuildDirtyHash(entries, 2, next, replacementCapacity, hash,
        replacementHashSize);
    receipt = {};
    Check(armRestore(imagePointer, capturedReceipt.nphaseCore,
        capturedReceipt.entries, capturedReceipt.entriesNext,
        capturedReceipt.hash, capturedReceipt.entriesCapacity,
        capturedReceipt.hashSize, captured, 3, 2, &receipt) == 1,
        "dirty projection with checkpoint deletion arms");
    update(nphase);
    Check(status(imagePointer, &receipt) == 1 && receipt.result == 1 &&
        receipt.restores == 4 && receipt.count == 2 &&
        receipt.matchedCount == 2 && receipt.capturedOnlyCount == 1 &&
        receipt.liveOnlyCount == 0,
        "dirty projection accepts a smaller live set");
    Check(entries[0] == reinterpret_cast<uintptr_t>(interactions[2]) &&
        entries[1] == reinterpret_cast<uintptr_t>(interactions[0]) &&
        DirtyHashValid(entries, 2, next, hash, replacementHashSize),
        "dirty projection orders a checkpoint subset");

    receipt = {};
    Check(uninstall(imagePointer, &receipt) == 1 && receipt.result == 1,
        "dirty hook uninstalls");
    observedNPhase = 1;
    observations = 1;
    Check(lastNPhase(imagePointer, &observedNPhase, &observations) == 0 &&
        observedNPhase == 0 && observations == 0,
        "passive nphase resets on uninstall");
    Check(memcmp(image + kDirtyUpdateRva, kDirtyUpdateFunctionBytes,
        6) == 0, "dirty hook restores exact function bytes");
}

static void RunActorPairPoolTests(uint8_t* image,
    CaptureActorPairSnapshot capture) {
    __declspec(align(16)) uint8_t nphase[0x400] = {};
    __declspec(align(16)) uint8_t actorPairs[32][0x18] = {};
    uintptr_t slabs[1] = {reinterpret_cast<uintptr_t>(actorPairs)};
    uint8_t* pool = nphase + 0x90;
    *reinterpret_cast<uintptr_t*>(pool + 0x108) =
        reinterpret_cast<uintptr_t>(slabs);
    *reinterpret_cast<uint32_t*>(pool + 0x10C) = 1;
    *reinterpret_cast<uint32_t*>(pool + 0x110) = 64;
    *reinterpret_cast<uint32_t*>(pool + 0x114) = 32;
    *reinterpret_cast<uint32_t*>(pool + 0x118) = 2;
    *reinterpret_cast<uint32_t*>(pool + 0x11C) = 30;
    *reinterpret_cast<uint32_t*>(pool + 0x120) = 0x300;
    for (uint32_t i = 2; i < 32; ++i)
        *reinterpret_cast<uintptr_t*>(actorPairs[i]) = i + 1 < 32 ?
            reinterpret_cast<uintptr_t>(actorPairs[i + 1]) : 0;
    *reinterpret_cast<uintptr_t*>(pool + 0x124) =
        reinterpret_cast<uintptr_t>(actorPairs[2]);

    uintptr_t freeOrder[32] = {};
    uintptr_t allocatedOrder[32] = {};
    ActorPairPoolReceipt receipt = {};
    uint8_t nphaseBefore[sizeof(nphase)] = {};
    uint8_t pairsBefore[sizeof(actorPairs)] = {};
    memcpy(nphaseBefore, nphase, sizeof(nphase));
    memcpy(pairsBefore, actorPairs, sizeof(actorPairs));
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 32,
        allocatedOrder, 32, &receipt) == 1,
        "ActorPair pool capture succeeds");
    Check(receipt.result == 1 && receipt.apiVersion == 14 &&
        receipt.structSize == sizeof(receipt) &&
        receipt.pool == reinterpret_cast<uintptr_t>(pool) &&
        receipt.elementSize == 0x18 && receipt.elementsPerSlab == 32 &&
        receipt.slabSize == 0x300 && receipt.slabCount == 1 &&
        receipt.totalElements == 32 && receipt.freeCount == 30 &&
        receipt.allocatedCount == 2 && receipt.validationFlags == 0xFF,
        "ActorPair pool receipt proves the complete partition");
    Check(freeOrder[0] == reinterpret_cast<uintptr_t>(actorPairs[2]) &&
        freeOrder[29] == reinterpret_cast<uintptr_t>(actorPairs[31]) &&
        allocatedOrder[0] == reinterpret_cast<uintptr_t>(actorPairs[0]) &&
        allocatedOrder[1] == reinterpret_cast<uintptr_t>(actorPairs[1]),
        "ActorPair free order and canonical allocated order are exact");
    Check(memcmp(nphaseBefore, nphase, sizeof(nphase)) == 0 &&
        memcmp(pairsBefore, actorPairs, sizeof(actorPairs)) == 0,
        "ActorPair capture does not mutate native state");

    ActorPairPoolReceipt repeated = {};
    uintptr_t repeatedFree[32] = {};
    uintptr_t repeatedAllocated[32] = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), repeatedFree, 32,
        repeatedAllocated, 32, &repeated) == 1 &&
        memcmp(&receipt, &repeated, sizeof(receipt)) == 0 &&
        Same(freeOrder, repeatedFree, 30) &&
        Same(allocatedOrder, repeatedAllocated, 2),
        "ActorPair capture is byte-repeatable");

    ActorPairPoolReceipt shortReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 29,
        allocatedOrder, 32, &shortReceipt) == 0 &&
        shortReceipt.result == 8,
        "ActorPair capture rejects a short caller-owned buffer");

    const uintptr_t savedNext = *reinterpret_cast<uintptr_t*>(actorPairs[5]);
    *reinterpret_cast<uintptr_t*>(actorPairs[5]) =
        reinterpret_cast<uintptr_t>(actorPairs[4]);
    ActorPairPoolReceipt duplicateReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 32,
        allocatedOrder, 32, &duplicateReceipt) == 0 &&
        duplicateReceipt.result == 7,
        "ActorPair capture rejects a duplicate free-list node");
    *reinterpret_cast<uintptr_t*>(actorPairs[5]) = savedNext;

    image[kFindActorPairRva + 0x91] ^= 1;
    ActorPairPoolReceipt revisionReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 32,
        allocatedOrder, 32, &revisionReceipt) == 0 &&
        revisionReceipt.result == 3,
        "ActorPair capture rejects a shipped-code revision mismatch");
    image[kFindActorPairRva + 0x91] ^= 1;
}

static void RunActorPairReportPoolTests(uint8_t* image,
    CaptureActorPairReportSnapshot capture) {
    __declspec(align(16)) uint8_t nphase[0x800] = {};
    __declspec(align(16)) uint8_t reportData[32][0x24] = {};
    uintptr_t slabs[1] = {reinterpret_cast<uintptr_t>(reportData)};
    uint8_t* pool = nphase + 0x530;
    *reinterpret_cast<uintptr_t*>(pool + 0x108) =
        reinterpret_cast<uintptr_t>(slabs);
    *reinterpret_cast<uint32_t*>(pool + 0x10C) = 1;
    *reinterpret_cast<uint32_t*>(pool + 0x110) = 64;
    *reinterpret_cast<uint32_t*>(pool + 0x114) = 32;
    *reinterpret_cast<uint32_t*>(pool + 0x118) = 10;
    *reinterpret_cast<uint32_t*>(pool + 0x11C) = 22;
    *reinterpret_cast<uint32_t*>(pool + 0x120) = 0x480;
    for (uint32_t i = 10; i < 32; ++i)
        *reinterpret_cast<uintptr_t*>(reportData[i]) = i + 1 < 32 ?
            reinterpret_cast<uintptr_t>(reportData[i + 1]) : 0;
    *reinterpret_cast<uintptr_t*>(pool + 0x124) =
        reinterpret_cast<uintptr_t>(reportData[10]);

    uintptr_t freeOrder[32] = {};
    uintptr_t allocatedOrder[32] = {};
    ActorPairReportPoolReceipt receipt = {};
    uint8_t nphaseBefore[sizeof(nphase)] = {};
    uint8_t reportsBefore[sizeof(reportData)] = {};
    memcpy(nphaseBefore, nphase, sizeof(nphase));
    memcpy(reportsBefore, reportData, sizeof(reportData));
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 32,
        allocatedOrder, 32, &receipt) == 1,
        "ActorPair report pool capture succeeds");
    Check(receipt.result == 1 && receipt.apiVersion == 14 &&
        receipt.structSize == sizeof(receipt) &&
        receipt.pool == reinterpret_cast<uintptr_t>(pool) &&
        receipt.elementSize == 0x24 && receipt.elementsPerSlab == 32 &&
        receipt.slabSize == 0x480 && receipt.slabCount == 1 &&
        receipt.totalElements == 32 && receipt.freeCount == 22 &&
        receipt.allocatedCount == 10 && receipt.validationFlags == 0xFF,
        "ActorPair report pool receipt proves the complete partition");
    Check(freeOrder[0] == reinterpret_cast<uintptr_t>(reportData[10]) &&
        freeOrder[21] == reinterpret_cast<uintptr_t>(reportData[31]) &&
        allocatedOrder[0] == reinterpret_cast<uintptr_t>(reportData[0]) &&
        allocatedOrder[9] == reinterpret_cast<uintptr_t>(reportData[9]),
        "ActorPair report free and allocated orders are exact");
    Check(memcmp(nphaseBefore, nphase, sizeof(nphase)) == 0 &&
        memcmp(reportsBefore, reportData, sizeof(reportData)) == 0,
        "ActorPair report capture does not mutate native state");

    ActorPairReportPoolReceipt repeated = {};
    uintptr_t repeatedFree[32] = {};
    uintptr_t repeatedAllocated[32] = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), repeatedFree, 32,
        repeatedAllocated, 32, &repeated) == 1 &&
        memcmp(&receipt, &repeated, sizeof(receipt)) == 0 &&
        Same(freeOrder, repeatedFree, 22) &&
        Same(allocatedOrder, repeatedAllocated, 10),
        "ActorPair report capture is byte-repeatable");

    ActorPairReportPoolReceipt shortReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 21,
        allocatedOrder, 32, &shortReceipt) == 0 &&
        shortReceipt.result == 8,
        "ActorPair report capture rejects a short caller-owned buffer");

    const uintptr_t savedNext = *reinterpret_cast<uintptr_t*>(reportData[13]);
    *reinterpret_cast<uintptr_t*>(reportData[13]) =
        reinterpret_cast<uintptr_t>(reportData[12]);
    ActorPairReportPoolReceipt duplicateReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 32,
        allocatedOrder, 32, &duplicateReceipt) == 0 &&
        duplicateReceipt.result == 7,
        "ActorPair report capture rejects a duplicate free-list node");
    *reinterpret_cast<uintptr_t*>(reportData[13]) = savedNext;

    image[kCreateActorPairReportDataRva] ^= 1;
    ActorPairReportPoolReceipt revisionReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), freeOrder, 32,
        allocatedOrder, 32, &revisionReceipt) == 0 &&
        revisionReceipt.result == 3,
        "ActorPair report capture rejects a shipped-code revision mismatch");
    image[kCreateActorPairReportDataRva] ^= 1;
}

static void RunNPhaseReportStateTests(uint8_t* image,
    CaptureNPhaseReportState capture) {
    __declspec(align(16)) uint8_t nphase[0x80] = {};
    __declspec(align(16)) uint8_t actorPairObjects[2][0x18] = {};
    __declspec(align(16)) uint8_t sipObjects[3][0x44] = {};
    __declspec(align(16)) uint8_t reportBytes[32] = {};
    uintptr_t actorPairList[2] = {
        reinterpret_cast<uintptr_t>(actorPairObjects[1]),
        reinterpret_cast<uintptr_t>(actorPairObjects[0])
    };
    uintptr_t persistentList[2] = {
        reinterpret_cast<uintptr_t>(sipObjects[2]),
        reinterpret_cast<uintptr_t>(sipObjects[0])
    };
    uintptr_t forceList[1] = {
        reinterpret_cast<uintptr_t>(sipObjects[1])
    };
    *reinterpret_cast<uintptr_t*>(nphase + 0x00) =
        reinterpret_cast<uintptr_t>(nphase + 0x60);
    *reinterpret_cast<uintptr_t*>(nphase + 0x04) =
        reinterpret_cast<uintptr_t>(actorPairList);
    *reinterpret_cast<uint32_t*>(nphase + 0x08) = 2;
    *reinterpret_cast<uint32_t*>(nphase + 0x0C) = 0x80000004u;
    *reinterpret_cast<uintptr_t*>(nphase + 0x10) =
        reinterpret_cast<uintptr_t>(persistentList);
    *reinterpret_cast<uint32_t*>(nphase + 0x14) = 2;
    *reinterpret_cast<uint32_t*>(nphase + 0x18) = 4;
    *reinterpret_cast<uint32_t*>(nphase + 0x1C) = 1;
    *reinterpret_cast<uintptr_t*>(nphase + 0x20) =
        reinterpret_cast<uintptr_t>(forceList);
    *reinterpret_cast<uint32_t*>(nphase + 0x24) = 1;
    *reinterpret_cast<uint32_t*>(nphase + 0x28) = 2;
    *reinterpret_cast<uintptr_t*>(nphase + 0x2C) =
        reinterpret_cast<uintptr_t>(reportBytes);
    *reinterpret_cast<uint32_t*>(nphase + 0x30) = 19;
    *reinterpret_cast<uint32_t*>(nphase + 0x34) = 32;
    *reinterpret_cast<uint32_t*>(nphase + 0x38) = 16;
    *reinterpret_cast<uint32_t*>(nphase + 0x3C) = 16;
    nphase[0x40] = 0;
    for (uint32_t i = 0; i < 2; ++i)
        *reinterpret_cast<uint16_t*>(actorPairObjects[i] + 0x0C) = 1;
    *reinterpret_cast<uint32_t*>(sipObjects[2] + 0x2C) = 0x00200000u;
    *reinterpret_cast<uint32_t*>(sipObjects[2] + 0x34) = 0;
    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x2C) = 0x00200000u;
    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x34) = 1;
    *reinterpret_cast<uint32_t*>(sipObjects[1] + 0x2C) = 0x00800000u;
    *reinterpret_cast<uint32_t*>(sipObjects[1] + 0x34) = 0;
    for (uint32_t i = 0; i < sizeof(reportBytes); ++i)
        reportBytes[i] = static_cast<uint8_t>(0x80u + i);

    uintptr_t capturedActorPairs[4] = {};
    uintptr_t capturedPersistent[4] = {};
    uintptr_t capturedForce[4] = {};
    uint8_t capturedBytes[32] = {};
    uint8_t nphaseBefore[sizeof(nphase)] = {};
    uint8_t pairBefore[sizeof(actorPairObjects)] = {};
    uint8_t sipBefore[sizeof(sipObjects)] = {};
    uint8_t reportBefore[sizeof(reportBytes)] = {};
    memcpy(nphaseBefore, nphase, sizeof(nphase));
    memcpy(pairBefore, actorPairObjects, sizeof(actorPairObjects));
    memcpy(sipBefore, sipObjects, sizeof(sipObjects));
    memcpy(reportBefore, reportBytes, sizeof(reportBytes));
    NPhaseReportStateReceipt receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), capturedActorPairs, 4,
        capturedPersistent, 4, capturedForce, 4, capturedBytes, 32,
        &receipt) == 1, "NPhase report-state capture succeeds");
    Check(receipt.result == 1 && receipt.apiVersion == 14 &&
        receipt.structSize == sizeof(receipt) &&
        receipt.ownerScene == reinterpret_cast<uintptr_t>(nphase + 0x60) &&
        receipt.actorPairCount == 2 && receipt.persistentCount == 2 &&
        receipt.nextFramePersistentIndex == 1 &&
        receipt.forceThresholdCount == 1 &&
        receipt.reportBufferCurrentIndex == 19 &&
        receipt.reportBufferCurrentSize == 32 &&
        receipt.reportBufferDefaultSize == 16 &&
        receipt.reportBufferLastIndex == 16 &&
        receipt.validationFlags == 0x7F,
        "NPhase report receipt proves lists, split, and buffer metadata");
    Check(Same(capturedActorPairs, actorPairList, 2) &&
        Same(capturedPersistent, persistentList, 2) &&
        Same(capturedForce, forceList, 1) &&
        memcmp(capturedBytes, reportBytes, sizeof(reportBytes)) == 0,
        "NPhase report capture preserves exact list and allocation bytes");
    Check(memcmp(nphaseBefore, nphase, sizeof(nphase)) == 0 &&
        memcmp(pairBefore, actorPairObjects, sizeof(actorPairObjects)) == 0 &&
        memcmp(sipBefore, sipObjects, sizeof(sipObjects)) == 0 &&
        memcmp(reportBefore, reportBytes, sizeof(reportBytes)) == 0,
        "NPhase report capture is non-mutating");

    NPhaseReportStateReceipt repeated = {};
    uintptr_t repeatedPairs[4] = {};
    uintptr_t repeatedPersistent[4] = {};
    uintptr_t repeatedForce[4] = {};
    uint8_t repeatedBytes[32] = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), repeatedPairs, 4,
        repeatedPersistent, 4, repeatedForce, 4, repeatedBytes, 32,
        &repeated) == 1 && memcmp(&receipt, &repeated, sizeof(receipt)) == 0 &&
        Same(capturedPersistent, repeatedPersistent, 2) &&
        memcmp(capturedBytes, repeatedBytes, sizeof(capturedBytes)) == 0,
        "NPhase report capture is byte-repeatable");

    NPhaseReportStateReceipt shortReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), capturedActorPairs, 4,
        capturedPersistent, 4, capturedForce, 4, capturedBytes, 31,
        &shortReceipt) == 0 && shortReceipt.result == 8,
        "NPhase report capture rejects a short full-allocation buffer");

    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x34) = 0;
    NPhaseReportStateReceipt indexReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), capturedActorPairs, 4,
        capturedPersistent, 4, capturedForce, 4, capturedBytes, 32,
        &indexReceipt) == 0 && indexReceipt.result == 6,
        "NPhase report capture rejects an incoherent SIP list index");
    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x34) = 1;

    image[kRemovePersistentContactEventPairRva] ^= 1;
    NPhaseReportStateReceipt revisionReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), capturedActorPairs, 4,
        capturedPersistent, 4, capturedForce, 4, capturedBytes, 32,
        &revisionReceipt) == 0 && revisionReceipt.result == 3,
        "NPhase report capture rejects a shipped-code revision mismatch");
    image[kRemovePersistentContactEventPairRva] ^= 1;
}

static void RunManifoldPoolTests(uint8_t* image, uint32_t poolKind,
    CaptureManifoldSnapshot capture, RestoreManifoldSnapshot restore) {
    const uint32_t poolOffset = poolKind == 0 ? 0x2E4 : 0x40C;
    const uint32_t elementSize = poolKind == 0 ? 0xF0 : 0x60;
    uint8_t context[0x538] = {};
    uint8_t secondContext[0x538] = {};
    uint8_t nodes[4][0xF0] = {};
    InitializeManifoldPool(context, poolKind, nodes);
    InitializeManifoldPool(secondContext, poolKind, nodes);
    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    const uintptr_t contextPointer = reinterpret_cast<uintptr_t>(context);
    const uintptr_t poolPointer = reinterpret_cast<uintptr_t>(
        context + poolOffset);
    uintptr_t saved[3] = {};
    ManifoldPoolReceipt receipt = {};

    Check(capture(imagePointer, contextPointer, poolKind, saved, 3,
        &receipt) == 1, "manifold capture succeeds");
    Check(receipt.result == 1 && receipt.apiVersion == 14 &&
        receipt.structSize == sizeof(receipt), "manifold capture receipt");
    Check(receipt.pool == poolPointer && receipt.poolKind == poolKind &&
        receipt.elementSize == elementSize && receipt.traversedCount == 3,
        "manifold capture telemetry");
    uintptr_t expected[3] = {
        reinterpret_cast<uintptr_t>(nodes[0]),
        reinterpret_cast<uintptr_t>(nodes[1]),
        reinterpret_cast<uintptr_t>(nodes[2])
    };
    Check(Same(saved, expected, 3), "manifold capture preserves head order");

    *reinterpret_cast<uintptr_t*>(nodes[2]) = expected[0];
    *reinterpret_cast<uintptr_t*>(nodes[0]) = expected[1];
    *reinterpret_cast<uintptr_t*>(nodes[1]) = 0;
    *reinterpret_cast<uintptr_t*>(context + poolOffset + 0x124) = expected[2];
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer, saved,
        3, &receipt) == 1, "manifold restore succeeds");
    Check(receipt.result == 1 && receipt.freeHeadAfter == expected[0] &&
        *reinterpret_cast<uintptr_t*>(nodes[0]) == expected[1] &&
        *reinterpret_cast<uintptr_t*>(nodes[1]) == expected[2] &&
        *reinterpret_cast<uintptr_t*>(nodes[2]) == 0,
        "manifold restore rewires exact head order");
    Check(*reinterpret_cast<uint32_t*>(context + poolOffset + 0x118) == 1 &&
        *reinterpret_cast<uint32_t*>(context + poolOffset + 0x11C) == 0,
        "manifold restore preserves accounting");

    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind,
        poolPointer + sizeof(uintptr_t), saved, 3, &receipt) == 0 &&
        receipt.result == 14, "wrong manifold pool identity is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold identity rejection is non-mutating");
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer, saved,
        2, &receipt) == 0 && receipt.result == 15,
        "wrong manifold count is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold count rejection is non-mutating");
    uintptr_t duplicate[3] = { saved[0], saved[0], saved[2] };
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer,
        duplicate, 3, &receipt) == 0 && receipt.result == 10,
        "duplicate manifold snapshot is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold duplicate rejection is non-mutating");
    uintptr_t changed[3] = {
        saved[0], saved[1], reinterpret_cast<uintptr_t>(nodes[3])
    };
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer,
        changed, 3, &receipt) == 0 && receipt.result == 16,
        "changed manifold membership is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold membership rejection is non-mutating");
    uintptr_t tooSmall[2] = {};
    receipt = {};
    Check(capture(imagePointer, contextPointer, poolKind, tooSmall, 2,
        &receipt) == 0 && receipt.result == 11,
        "short manifold capture buffer is rejected");
    Check(ManifoldOrderMatches(context, poolKind, expected, 3),
        "manifold capture rejection is non-mutating");

    receipt = {};
    Check(restore(imagePointer, reinterpret_cast<uintptr_t>(secondContext),
        poolKind, poolPointer, saved, 3, &receipt) == 0 &&
        receipt.result == 14, "changed manifold context is rejected");

    *reinterpret_cast<uint32_t*>(context + poolOffset + 0x118) = 0;
    *reinterpret_cast<uint32_t*>(context + poolOffset + 0x11C) = 0;
    *reinterpret_cast<uintptr_t*>(context + poolOffset + 0x124) = 0;
    receipt = {};
    Check(capture(imagePointer, contextPointer, poolKind, 0, 0,
        &receipt) == 1 && receipt.result == 1 &&
        receipt.traversedCount == 0, "zero manifold list capture succeeds");
    receipt = {};
    Check(restore(imagePointer, contextPointer, poolKind, poolPointer, 0, 0,
        &receipt) == 1 && receipt.result == 1 &&
        receipt.freeHeadAfter == 0, "zero manifold list restore succeeds");
}

static void InitializeContactRecreateFixture(uint8_t* context,
    uint8_t managers[256][0x80], uintptr_t* freeArray, uintptr_t* slabs,
    uint32_t* useMap, uint32_t* activeMap, uint32_t* touchMap,
    uint32_t* modifiableMap, uint8_t manifolds[32][0xF0]) {
    memset(context, 0, 0x1800);
    memset(managers, 0, 256 * 0x80);
    memset(useMap, 0, 8 * sizeof(uint32_t));
    memset(activeMap, 0, 8 * sizeof(uint32_t));
    memset(touchMap, 0, 8 * sizeof(uint32_t));
    memset(modifiableMap, 0, 8 * sizeof(uint32_t));
    slabs[0] = reinterpret_cast<uintptr_t>(managers);
    for (uint32_t i = 0; i < 256; ++i) {
        freeArray[i] = reinterpret_cast<uintptr_t>(managers[i]);
        *reinterpret_cast<uint32_t*>(managers[i] + 0x4C) = i;
    }
    uint8_t* contactPool = context + 0x2B8;
    *reinterpret_cast<uint32_t*>(contactPool + 0x00) = 256;
    *reinterpret_cast<uint32_t*>(contactPool + 0x04) = 4096;
    *reinterpret_cast<uint32_t*>(contactPool + 0x08) = 1;
    *reinterpret_cast<uint32_t*>(contactPool + 0x0C) = 8;
    *reinterpret_cast<uintptr_t*>(contactPool + 0x10) =
        reinterpret_cast<uintptr_t>(freeArray);
    *reinterpret_cast<uint32_t*>(contactPool + 0x14) = 256;
    *reinterpret_cast<uintptr_t*>(contactPool + 0x18) =
        reinterpret_cast<uintptr_t>(slabs);
    *reinterpret_cast<uintptr_t*>(contactPool + 0x1C) =
        reinterpret_cast<uintptr_t>(context);
    *reinterpret_cast<uintptr_t*>(contactPool + 0x20) =
        reinterpret_cast<uintptr_t>(useMap);
    *reinterpret_cast<uint32_t*>(contactPool + 0x24) = 8;
    *reinterpret_cast<uintptr_t*>(context + 0x534) =
        reinterpret_cast<uintptr_t>(activeMap);
    *reinterpret_cast<uint32_t*>(context + 0x538) = 8;
    *reinterpret_cast<uintptr_t*>(context + 0x540) =
        reinterpret_cast<uintptr_t>(touchMap);
    *reinterpret_cast<uint32_t*>(context + 0x544) = 8;
    *reinterpret_cast<uintptr_t*>(context + 0x16D0) =
        reinterpret_cast<uintptr_t>(modifiableMap);
    *reinterpret_cast<uint32_t*>(context + 0x16D4) = 8;

    uint8_t* largePool = context + 0x2E4;
    *reinterpret_cast<uint32_t*>(largePool + 0x114) = 32;
    *reinterpret_cast<uint32_t*>(largePool + 0x118) = 0;
    *reinterpret_cast<uint32_t*>(largePool + 0x11C) = 32;
    *reinterpret_cast<uint32_t*>(largePool + 0x120) = 32 * 0xF0;
    for (uint32_t i = 0; i < 32; ++i)
        *reinterpret_cast<uintptr_t*>(manifolds[i]) = i + 1 < 32 ?
            reinterpret_cast<uintptr_t>(manifolds[i + 1]) : 0;
    *reinterpret_cast<uintptr_t*>(largePool + 0x124) =
        reinterpret_cast<uintptr_t>(manifolds[0]);
}

static void ConsumePreparedAllocation(uint8_t* context,
    uintptr_t expectedManager, uintptr_t expectedManifold) {
    uintptr_t* freeArray = reinterpret_cast<uintptr_t*>(
        *reinterpret_cast<uintptr_t*>(context + 0x2C8));
    uint32_t& freeCount = *reinterpret_cast<uint32_t*>(context + 0x2CC);
    uint8_t* largePool = context + 0x2E4;
    const uintptr_t head = *reinterpret_cast<uintptr_t*>(largePool + 0x124);
    Check(freeArray[freeCount - 1] == expectedManager,
        "endpoint hook selects the expected manager");
    Check(head == expectedManifold,
        "endpoint hook selects the expected manifold");
    --freeCount;
    *reinterpret_cast<uintptr_t*>(largePool + 0x124) =
        *reinterpret_cast<uintptr_t*>(head);
    ++*reinterpret_cast<uint32_t*>(largePool + 0x118);
    --*reinterpret_cast<uint32_t*>(largePool + 0x11C);
}

static void CommitPreparedAllocation(uint8_t* context,
    uintptr_t expectedManager, uintptr_t expectedManifold, uintptr_t sip,
    uintptr_t shape0, uintptr_t shape1) {
    ConsumePreparedAllocation(context, expectedManager, expectedManifold);
    const uint32_t slot = *reinterpret_cast<uint32_t*>(expectedManager + 0x4C);
    *reinterpret_cast<uintptr_t*>(expectedManager + 0x0C) = sip;
    *reinterpret_cast<uintptr_t*>(expectedManager + 0x3C) = expectedManifold;
    *reinterpret_cast<uintptr_t*>(expectedManager + 0x58) = shape0;
    *reinterpret_cast<uintptr_t*>(expectedManager + 0x5C) = shape1;
    *reinterpret_cast<uintptr_t*>(sip + 0x38) = expectedManager;
    const uintptr_t allocatedBitmaps[2] = {
        reinterpret_cast<uintptr_t>(context + 0x2D8),
        reinterpret_cast<uintptr_t>(context + 0x534)};
    for (uint32_t i = 0; i < 2; ++i) {
        const uintptr_t bitmap = allocatedBitmaps[i];
        const uintptr_t map = *reinterpret_cast<uintptr_t*>(bitmap);
        reinterpret_cast<uint32_t*>(map)[slot >> 5] |=
            1u << (slot & 31u);
    }
}

static void ReturnPreparedAllocation(uint8_t* context, uintptr_t manager,
    uintptr_t manifold, uintptr_t sip) {
    uintptr_t* freeArray = reinterpret_cast<uintptr_t*>(
        *reinterpret_cast<uintptr_t*>(context + 0x2C8));
    uint32_t& freeCount = *reinterpret_cast<uint32_t*>(context + 0x2CC);
    freeArray[freeCount++] = manager;
    uint8_t* largePool = context + 0x2E4;
    *reinterpret_cast<uintptr_t*>(manifold) =
        *reinterpret_cast<uintptr_t*>(largePool + 0x124);
    *reinterpret_cast<uintptr_t*>(largePool + 0x124) = manifold;
    --*reinterpret_cast<uint32_t*>(largePool + 0x118);
    ++*reinterpret_cast<uint32_t*>(largePool + 0x11C);
    const uint32_t slot = *reinterpret_cast<uint32_t*>(manager + 0x4C);
    const uintptr_t returnedBitmaps[4] = {
        reinterpret_cast<uintptr_t>(context + 0x2D8),
        reinterpret_cast<uintptr_t>(context + 0x534),
        reinterpret_cast<uintptr_t>(context + 0x540),
        reinterpret_cast<uintptr_t>(context + 0x16D0)};
    for (uint32_t i = 0; i < 4; ++i) {
        const uintptr_t bitmap = returnedBitmaps[i];
        const uintptr_t map = *reinterpret_cast<uintptr_t*>(bitmap);
        reinterpret_cast<uint32_t*>(map)[slot >> 5] &=
            ~(1u << (slot & 31u));
    }
    *reinterpret_cast<uintptr_t*>(sip + 0x38) = 0;
}

#if 0
static void RunContactRecreateTests(uint8_t* image,
    ContextObserverAction installObserver,
    ContextObserverAction uninstallObserver, ContactRecreateArm arm,
    ContactRecreateStatus status, ContactRecreateCancel cancel) {
    uint8_t context[0x1800] = {};
    uint8_t managers[256][0x80] = {};
    uintptr_t freeArray[256] = {};
    uintptr_t slabs[1] = {};
    uint32_t useMap[8] = {}, activeMap[8] = {}, touchMap[8] = {},
        modifiableMap[8] = {};
    uint8_t manifolds[32][0xF0] = {};
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    const uintptr_t contextPointer = reinterpret_cast<uintptr_t>(context);
    const uintptr_t freeArrayPointer = reinterpret_cast<uintptr_t>(freeArray);
    const uintptr_t largePoolPointer = reinterpret_cast<uintptr_t>(
        context + 0x2E4);
    uintptr_t targetContact[254] = {};
    for (uint32_t i = 0; i < 254; ++i)
        targetContact[i] = reinterpret_cast<uintptr_t>(managers[i + 2]);
    uintptr_t targetLarge[30] = {};
    for (uint32_t i = 0; i < 30; ++i)
        targetLarge[i] = reinterpret_cast<uintptr_t>(manifolds[i + 2]);
    uintptr_t originalContact[256] = {};
    for (uint32_t i = 0; i < 256; ++i)
        originalContact[i] = reinterpret_cast<uintptr_t>(managers[i]);
    uintptr_t originalLarge[32] = {};
    for (uint32_t i = 0; i < 32; ++i)
        originalLarge[i] = reinterpret_cast<uintptr_t>(manifolds[i]);
    ContactRecreatePlanRow rows[2] = {
        {0x1000, 0x2000, reinterpret_cast<uintptr_t>(managers[0]),
            reinterpret_cast<uintptr_t>(manifolds[0]), 0, 0xF0, 0, 0},
        {0x3000, 0x4000, reinterpret_cast<uintptr_t>(managers[1]),
            reinterpret_cast<uintptr_t>(manifolds[1]), 1, 0xF0, 0, 0}
    };
    ContactContextObserverReceipt observer = {};
    Check(installObserver(imagePointer, &observer) == 1 &&
        observer.result == 1 && observer.installed == 1,
        "contact observer installs for recreation tests");

    ContactRecreateReceipt receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 1 && receipt.result == 1 && receipt.state == 1 &&
        receipt.armed == 1, "contact recreation union plan arms");
    Check(Same(freeArray, targetContact, 254),
        "contact recreation primes exact target-free prefix");
    Check(ManifoldOrderMatches(context, 0,
        reinterpret_cast<const uintptr_t*>(0), 0) == false,
        "contact recreation has a nonempty primed manifold chain");

    FakeCreateManager create = reinterpret_cast<FakeCreateManager>(
        image + kCreateManagerRva);
    uint8_t sip0[0x44] = {}, sip1[0x44] = {};
    uintptr_t descriptor0[7] = {reinterpret_cast<uintptr_t>(sip0),
        0,0,0,0,0x1000,0x2000};
    uintptr_t descriptor1[7] = {reinterpret_cast<uintptr_t>(sip1),
        0,0,0,0,0x3000,0x4000};
    create(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    DWORD workerThreadId = 0;
    Check(InvokeFakeCreateOnWorker(create, context, descriptor1,
        workerThreadId),
        "contact recreation accepts a serialized worker-thread handoff");
    CommitPreparedAllocation(context, rows[1].targetManager,
        rows[1].targetManifold, descriptor1[0], descriptor1[5],
        descriptor1[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.result == 1 && receipt.state == 2 && receipt.armed == 0 &&
        receipt.matchedCount == 2 && receipt.remainingCount == 0 &&
        receipt.contactCountCurrent == 254 &&
        receipt.contactHashCurrent == receipt.targetContactHash &&
        receipt.largeCountCurrent == 30 &&
        receipt.largeHashCurrent == receipt.targetLargeHash &&
        receipt.largeUsedCurrent == 2 && receipt.largeUnreleasedCurrent == 30 &&
        receipt.threadId == workerThreadId,
        "contact recreation converges after exact pair order");
    Check(cancel(imagePointer, &receipt) == 1 && receipt.state == 0,
        "completed contact recreation clears to dormant state");

    // Reinitialize and consume in reverse semantic order.  The same target
    // free structures must remain after both allocations.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    memset(sip0, 0, sizeof(sip0));
    memset(sip1, 0, sizeof(sip1));
    receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 1, "contact recreation reverse-order plan arms");
    create(context, descriptor1, 0);
    CommitPreparedAllocation(context, rows[1].targetManager,
        rows[1].targetManifold, descriptor1[0], descriptor1[5],
        descriptor1[6]);
    create(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.state == 2 && receipt.contactHashCurrent ==
            receipt.targetContactHash && receipt.largeHashCurrent ==
            receipt.targetLargeHash,
        "contact recreation is independent of pair creation order");
    cancel(imagePointer, &receipt);

    // An armed plan that has not observed a shipped allocation is entirely
    // reversible, including the primed contact array and linked manifold
    // chain.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    memset(sip0, 0, sizeof(sip0));
    memset(sip1, 0, sizeof(sip1));
    receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 1, "unmatched contact recreation plan arms");
    Check(cancel(imagePointer, &receipt) == 1 && receipt.state == 0 &&
        *reinterpret_cast<uint32_t*>(context + 0x2CC) == 256 &&
        Same(freeArray, originalContact, 256) &&
        ManifoldOrderMatches(context, 0, originalLarge, 32) &&
        *reinterpret_cast<uint32_t*>(context + 0x2E4 + 0x118) == 0 &&
        *reinterpret_cast<uint32_t*>(context + 0x2E4 + 0x11C) == 32,
        "unmatched cancellation restores both allocator preimages");

    // A short-lived pair absent from the checkpoint remains pass-through.  It
    // may temporarily consume the primed tops, but once the shipped lifecycle
    // returns both LIFO entries the tracked checkpoint pairs can still select
    // their exact manager/manifold identities.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    memset(sip0, 0, sizeof(sip0));
    memset(sip1, 0, sizeof(sip1));
    receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 1, "unexpected-pair contact recreation plan arms");
    uint8_t unexpectedSip[0x44] = {};
    uintptr_t unexpectedDescriptor[7] = {
        reinterpret_cast<uintptr_t>(unexpectedSip),0,0,0,0,0x5000,0x6000
    };
    const uintptr_t transientManager = freeArray[255];
    const uintptr_t transientManifold = *reinterpret_cast<uintptr_t*>(
        context + 0x2E4 + 0x124);
    create(context, unexpectedDescriptor, 0);
    ConsumePreparedAllocation(context, transientManager, transientManifold);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.result == 1 && receipt.state == 1 &&
        receipt.matchedCount == 0 && receipt.remainingCount == 2 &&
        receipt.contactCountCurrent == 255 &&
        receipt.largeCountCurrent == 31,
        "transient foreign pair remains a guarded pass-through");
    ReturnPreparedAllocation(context, transientManager, transientManifold,
        unexpectedDescriptor[0]);
    create(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    create(context, descriptor1, 0);
    CommitPreparedAllocation(context, rows[1].targetManager,
        rows[1].targetManifold, descriptor1[0], descriptor1[5],
        descriptor1[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.state == 2 && receipt.matchedCount == 2 &&
        receipt.contactHashCurrent == receipt.targetContactHash &&
        receipt.largeHashCurrent == receipt.targetLargeHash,
        "tracked recreation converges after a returned foreign pair");
    cancel(imagePointer, &receipt);

    // Status remains an honest partial receipt after one natural allocation.
    // Cancellation may clear the hook state but must not put that now-live
    // manager or manifold back into a free pool.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    memset(sip0, 0, sizeof(sip0));
    memset(sip1, 0, sizeof(sip1));
    receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 1, "partial contact recreation plan arms");
    create(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.state == 1 && receipt.armed == 1 &&
        receipt.matchedCount == 1 && receipt.remainingCount == 1 &&
        receipt.contactCountCurrent == 255 &&
        receipt.largeCountCurrent == 31 &&
        receipt.largeUsedCurrent == 1 && receipt.largeUnreleasedCurrent == 31,
        "partial contact recreation status reports one consumption");
    Check(cancel(imagePointer, &receipt) == 1 &&
        *reinterpret_cast<uint32_t*>(context + 0x2CC) == 255 &&
        !Same(freeArray, originalContact, 256),
        "partial cancellation does not free an already-consumed manager");

    // Repeating an already consumed semantic pair is a distinct poisoned
    // state, not an accidental second match.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    memset(sip0, 0, sizeof(sip0));
    memset(sip1, 0, sizeof(sip1));
    receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 1, "duplicate-pair contact recreation plan arms");
    create(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    create(context, descriptor0, 0);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 0 &&
        receipt.result == 13 && receipt.state == 3 &&
        receipt.matchedCount == 1 && receipt.remainingCount == 1,
        "duplicate semantic pair poisons a partially consumed plan");
    cancel(imagePointer, &receipt);

    // A pair may destroy its manager and recreate it during the same
    // maintenance pass.  Admit that lifecycle only after the exact selected
    // manager and manifold are both back in their free structures, all slot
    // membership bits are clear, and the SIP backlink is null again.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    memset(sip0, 0, sizeof(sip0));
    memset(sip1, 0, sizeof(sip1));
    receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 1, "returned-pair contact recreation plan arms");
    create(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    ReturnPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0]);
    create(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    create(context, descriptor1, 0);
    CommitPreparedAllocation(context, rows[1].targetManager,
        rows[1].targetManifold, descriptor1[0], descriptor1[5],
        descriptor1[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.result == 1 && receipt.state == 2 &&
        receipt.matchedCount == 2 && receipt.remainingCount == 0 &&
        receipt.observerEntries == 3,
        "proved destroy/recreate lifecycle converges exactly once live");
    cancel(imagePointer, &receipt);

    // Membership admission rejects a readable foreign manager even when all
    // counts and bitmap metadata otherwise look valid.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    uint8_t foreignManager[0x80] = {};
    *reinterpret_cast<uint32_t*>(foreignManager + 0x4C) = 255;
    freeArray[255] = reinterpret_cast<uintptr_t>(foreignManager);
    receipt = {};
    Check(arm(imagePointer, contextPointer, freeArrayPointer, targetContact,
        254, largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &receipt) == 0 && receipt.result == 7 && receipt.state == 3 &&
        receipt.detail == 14,
        "foreign contact-manager membership is rejected before writes");
    cancel(imagePointer, &receipt);

    // An unarmed observer must not alter either allocator.
    InitializeContactRecreateFixture(context, managers, freeArray, slabs,
        useMap, activeMap, touchMap, modifiableMap, manifolds);
    const uintptr_t topBefore = freeArray[255];
    const uintptr_t headBefore = *reinterpret_cast<uintptr_t*>(
        context + 0x2E4 + 0x124);
    create(context, descriptor0, 0);
    Check(freeArray[255] == topBefore &&
        *reinterpret_cast<uintptr_t*>(context + 0x2E4 + 0x124) == headBefore,
        "dormant contact observer performs no allocator writes");

    observer = {};
    Check(uninstallObserver(imagePointer, &observer) == 1 &&
        observer.result == 1 && observer.installed == 0,
        "contact observer uninstalls after recreation tests");
}
#endif

static void RunContactRecreateSipTests(uint8_t* image,
    ContextObserverAction installObserver,
    ContextObserverAction uninstallObserver, ContactRecreateArm arm,
    ContactRecreateStatus status, ContactRecreateCancel cancel,
    CaptureSipSnapshot captureSip, ContactRecreateAudit audit) {
    uint8_t context[0x1800] = {};
    uint8_t managers[256][0x80] = {};
    uintptr_t freeArray[256] = {};
    uintptr_t slabs[1] = {};
    uint32_t useMap[8] = {}, activeMap[8] = {}, touchMap[8] = {},
        modifiableMap[8] = {};
    uint8_t manifolds[32][0xF0] = {};
    uint8_t nphase[0x420] = {};
    uint8_t sipNodes[32][0x44] = {};
    uint8_t shapeSims[6][0x20] = {};
    uint8_t shapeCores[6][0x24] = {};
    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    const uintptr_t contextPointer = reinterpret_cast<uintptr_t>(context);
    const uintptr_t nphasePointer = reinterpret_cast<uintptr_t>(nphase);
    const uintptr_t sipPoolPointer = nphasePointer + 0x2E0;
    const uintptr_t freeArrayPointer = reinterpret_cast<uintptr_t>(freeArray);
    const uintptr_t largePoolPointer = reinterpret_cast<uintptr_t>(
        context + 0x2E4);
    uintptr_t targetContact[254] = {};
    uintptr_t targetLarge[30] = {};
    uintptr_t targetSip[30] = {};
    for (uint32_t i = 0; i < 254; ++i)
        targetContact[i] = reinterpret_cast<uintptr_t>(managers[i + 2]);
    for (uint32_t i = 0; i < 30; ++i) {
        targetLarge[i] = reinterpret_cast<uintptr_t>(manifolds[i + 2]);
        targetSip[i] = reinterpret_cast<uintptr_t>(sipNodes[i + 2]);
    }
    for (uint32_t i = 0; i < 6; ++i)
        *reinterpret_cast<uintptr_t*>(shapeSims[i] + 0x1C) =
            reinterpret_cast<uintptr_t>(shapeCores[i]);
    const uintptr_t pxs[6] = {
        reinterpret_cast<uintptr_t>(shapeCores[0] + 0x20),
        reinterpret_cast<uintptr_t>(shapeCores[1] + 0x20),
        reinterpret_cast<uintptr_t>(shapeCores[2] + 0x20),
        reinterpret_cast<uintptr_t>(shapeCores[3] + 0x20),
        reinterpret_cast<uintptr_t>(shapeCores[4] + 0x20),
        reinterpret_cast<uintptr_t>(shapeCores[5] + 0x20)
    };
    ContactRecreatePlanRow rows[2] = {
        {pxs[0] < pxs[1] ? pxs[0] : pxs[1],
            pxs[0] < pxs[1] ? pxs[1] : pxs[0],
            reinterpret_cast<uintptr_t>(managers[0]),
            reinterpret_cast<uintptr_t>(manifolds[0]),
            reinterpret_cast<uintptr_t>(sipNodes[0]), 0, 0xF0, 0, 0},
        {pxs[2] < pxs[3] ? pxs[2] : pxs[3],
            pxs[2] < pxs[3] ? pxs[3] : pxs[2],
            reinterpret_cast<uintptr_t>(managers[1]),
            reinterpret_cast<uintptr_t>(manifolds[1]),
            reinterpret_cast<uintptr_t>(sipNodes[1]), 1, 0xF0, 0, 0}
    };
    const auto initialize = [&]() {
        InitializeContactRecreateFixture(context, managers, freeArray, slabs,
            useMap, activeMap, touchMap, modifiableMap, manifolds);
        memset(nphase + 0x2E0, 0, 0x140);
        memset(sipNodes, 0, sizeof(sipNodes));
        uint8_t* pool = nphase + 0x2E0;
        *reinterpret_cast<uint32_t*>(pool + 0x114) = 32;
        *reinterpret_cast<uint32_t*>(pool + 0x118) = 0;
        *reinterpret_cast<uint32_t*>(pool + 0x11C) = 32;
        *reinterpret_cast<uint32_t*>(pool + 0x120) = 0x880;
        for (uint32_t i = 0; i < 32; ++i)
            *reinterpret_cast<uintptr_t*>(sipNodes[i]) = i + 1 < 32 ?
                reinterpret_cast<uintptr_t>(sipNodes[i + 1]) : 0;
        *reinterpret_cast<uintptr_t*>(pool + 0x124) =
            reinterpret_cast<uintptr_t>(sipNodes[0]);
    };
    const auto returnSip = [&](uintptr_t sip) {
        uint8_t* pool = nphase + 0x2E0;
        *reinterpret_cast<uintptr_t*>(sip) =
            *reinterpret_cast<uintptr_t*>(pool + 0x124);
        *reinterpret_cast<uintptr_t*>(pool + 0x124) = sip;
        --*reinterpret_cast<uint32_t*>(pool + 0x118);
        ++*reinterpret_cast<uint32_t*>(pool + 0x11C);
    };
    const auto armPlan = [&](ContactRecreateReceipt& receipt) {
        return arm(imagePointer, contextPointer, nphasePointer, sipPoolPointer,
            targetSip, 30, 2, 30, freeArrayPointer, targetContact, 254,
            largePoolPointer, targetLarge, 30, 2, 30, rows, 2, &receipt);
    };

    initialize();
    uintptr_t sipCaptured[32] = {};
    SipPoolReceipt sipReceipt = {};
    Check(captureSip(imagePointer, nphasePointer, sipCaptured, 32,
        &sipReceipt) == 1 && sipReceipt.result == 1 &&
        sipReceipt.traversedCount == 32 && sipReceipt.used == 0 &&
        sipReceipt.unreleased == 32,
        "shape-pair pool capture validates exact LIFO state");

    ContactContextObserverReceipt observer = {};
    Check(installObserver(imagePointer, &observer) == 1 &&
        observer.result == 1 && observer.installed == 1,
        "dual shape-pair/contact observer installs");

    ContactRecreateAuditReceipt auditReceipt = {};
    Check(audit(imagePointer, contextPointer, nphasePointer, sipPoolPointer,
        targetSip, 30, 2, 30, freeArrayPointer, targetContact, 254,
        largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &auditReceipt) == 1 && auditReceipt.result == 1 &&
        auditReceipt.apiVersion == 1 &&
        auditReceipt.structSize == sizeof(auditReceipt) &&
        auditReceipt.issueMask == 0 &&
        auditReceipt.evaluatedMask == 0x001FFFFFu &&
        auditReceipt.liveSipCount == 32 &&
        auditReceipt.expectedSemanticSipCount == 32 &&
        auditReceipt.liveContactCount == 256 &&
        auditReceipt.expectedContactCount == 256 &&
        auditReceipt.liveLargeCount == 32 &&
        auditReceipt.expectedLargeCount == 32,
        "read-only recreation audit accepts a clean complete plan");

    uintptr_t mixedTargetSip[30] = {};
    memcpy(mixedTargetSip, targetSip, sizeof(mixedTargetSip));
    mixedTargetSip[29] = rows[0].targetSip;
    auditReceipt = {};
    Check(audit(imagePointer, contextPointer, nphasePointer, sipPoolPointer,
        mixedTargetSip, 30, 2, 30, freeArrayPointer, targetContact, 254,
        largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &auditReceipt) == 1 && auditReceipt.result == 2 &&
        auditReceipt.recreateState == 0 &&
        auditReceipt.targetFreeSipRowCount == 1 &&
        auditReceipt.activeSipRowCount == 1 &&
        (auditReceipt.issueMask & (1u << 10)) != 0 &&
        (auditReceipt.issueMask & (1u << 11)) != 0 &&
        (auditReceipt.issueMask & (1u << 12)) != 0 &&
        (auditReceipt.issueMask & (1u << 16)) == 0 &&
        (auditReceipt.issueMask & (1u << 19)) == 0,
        "read-only audit accumulates mixed-phase SIP issues and continues");
    auditReceipt = {};
    Check(audit(imagePointer, contextPointer, nphasePointer, sipPoolPointer,
        targetSip, 30, 2, 30, freeArrayPointer, targetContact, 254,
        largePoolPointer, targetLarge, 30, 2, 30, rows, 2,
        &auditReceipt) == 1 && auditReceipt.result == 1 &&
        auditReceipt.recreateState == 0,
        "read-only audit leaves native recreation state reusable");
    FakeCreateSip createSip = reinterpret_cast<FakeCreateSip>(
        image + kCreateShapeInstancePairRva);
    FakeCreateManager createManager = reinterpret_cast<FakeCreateManager>(
        image + kCreateManagerRva);

    ContactRecreateReceipt receipt = {};
    Check(armPlan(receipt) == 1 && receipt.result == 1 &&
        receipt.apiVersion == 2 && receipt.state == 1 &&
        receipt.sipMatchedCount == 0,
        "combined shape-pair/contact recreation plan arms");
    uintptr_t descriptor0[7] = {0,0,0,0,0,pxs[0],pxs[1]};
    uintptr_t descriptor1[7] = {0,0,0,0,0,pxs[2],pxs[3]};
    descriptor0[0] = createSip(nphase, shapeSims[0], shapeSims[1], 0);
    Check(descriptor0[0] == rows[0].targetSip,
        "shape-pair hook selects row-zero historical SIP");
    createManager(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    descriptor1[0] = createSip(nphase, shapeSims[2], shapeSims[3], 0);
    Check(descriptor1[0] == rows[1].targetSip,
        "shape-pair hook selects row-one historical SIP");
    DWORD workerThreadId = 0;
    Check(InvokeFakeCreateOnWorker(createManager, context, descriptor1,
        workerThreadId), "manager hook accepts worker-thread handoff");
    CommitPreparedAllocation(context, rows[1].targetManager,
        rows[1].targetManifold, descriptor1[0], descriptor1[5],
        descriptor1[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.state == 2 && receipt.matchedCount == 2 &&
        receipt.sipMatchedCount == 2 && receipt.sipRemainingCount == 0 &&
        receipt.sipCountCurrent == 30 &&
        receipt.sipHashCurrent == receipt.targetSipHash &&
        receipt.sipUsedCurrent == 2 && receipt.sipUnreleasedCurrent == 30 &&
        receipt.contactHashCurrent == receipt.targetContactHash &&
        receipt.largeHashCurrent == receipt.targetLargeHash,
        "combined recreation converges with exact SIP/manager/manifold pools");
    Check(cancel(imagePointer, &receipt) == 1 && receipt.state == 0,
        "completed combined recreation clears to dormant state");

    // A foreign pair may borrow an ordinary checkpoint-free SIP and return it
    // before either tracked pair. The exact LIFO state and historical row SIPs
    // remain available afterward.
    initialize();
    receipt = {};
    Check(armPlan(receipt) == 1,
        "combined recreation arms before transient foreign pair");
    uintptr_t foreignDescriptor[7] = {0,0,0,0,0,pxs[4],pxs[5]};
    foreignDescriptor[0] = createSip(nphase, shapeSims[4], shapeSims[5], 0);
    const uintptr_t foreignManager = freeArray[255];
    const uintptr_t foreignManifold = *reinterpret_cast<uintptr_t*>(
        context + 0x2E4 + 0x124);
    createManager(context, foreignDescriptor, 0);
    ConsumePreparedAllocation(context, foreignManager, foreignManifold);
    ReturnPreparedAllocation(context, foreignManager, foreignManifold,
        foreignDescriptor[0]);
    returnSip(foreignDescriptor[0]);
    descriptor0[0] = createSip(nphase, shapeSims[0], shapeSims[1], 0);
    createManager(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    descriptor1[0] = createSip(nphase, shapeSims[2], shapeSims[3], 0);
    createManager(context, descriptor1, 0);
    CommitPreparedAllocation(context, rows[1].targetManager,
        rows[1].targetManifold, descriptor1[0], descriptor1[5],
        descriptor1[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.state == 2 && receipt.sipMatchedCount == 2 &&
        receipt.sipHashCurrent == receipt.targetSipHash,
        "tracked SIP recreation survives a returned foreign allocation");
    cancel(imagePointer, &receipt);

    // The manager and SIP may both be destroyed and recreated within one
    // maintenance pass. Reconciliation must clear both match bits only after
    // their exact pool entries have returned.
    initialize();
    receipt = {};
    Check(armPlan(receipt) == 1,
        "combined recreation arms for destroy/recreate lifecycle");
    descriptor0[0] = createSip(nphase, shapeSims[0], shapeSims[1], 0);
    createManager(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    ReturnPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0]);
    returnSip(descriptor0[0]);
    descriptor0[0] = createSip(nphase, shapeSims[0], shapeSims[1], 0);
    createManager(context, descriptor0, 0);
    CommitPreparedAllocation(context, rows[0].targetManager,
        rows[0].targetManifold, descriptor0[0], descriptor0[5],
        descriptor0[6]);
    descriptor1[0] = createSip(nphase, shapeSims[2], shapeSims[3], 0);
    createManager(context, descriptor1, 0);
    CommitPreparedAllocation(context, rows[1].targetManager,
        rows[1].targetManifold, descriptor1[0], descriptor1[5],
        descriptor1[6]);
    receipt = {};
    Check(status(imagePointer, contextPointer, &receipt) == 1 &&
        receipt.state == 2 && receipt.matchedCount == 2 &&
        receipt.sipMatchedCount == 2 && receipt.sipObserverEntries == 3,
        "combined destroy/recreate lifecycle converges exactly once live");
    cancel(imagePointer, &receipt);

    // An unobserved arm is fully reversible, including the newly primed SIP
    // chain as well as the manager and manifold allocators.
    initialize();
    receipt = {};
    Check(armPlan(receipt) == 1,
        "unobserved combined recreation plan arms");
    Check(cancel(imagePointer, &receipt) == 1 && receipt.state == 0 &&
        *reinterpret_cast<uintptr_t*>(nphase + 0x2E0 + 0x124) ==
            reinterpret_cast<uintptr_t>(sipNodes[0]) &&
        *reinterpret_cast<uint32_t*>(nphase + 0x2E0 + 0x118) == 0 &&
        *reinterpret_cast<uint32_t*>(nphase + 0x2E0 + 0x11C) == 32,
        "unobserved cancellation restores the SIP-pool preimage");

    observer = {};
    Check(uninstallObserver(imagePointer, &observer) == 1 &&
        observer.result == 1 && observer.installed == 0,
        "dual shape-pair/contact observer uninstalls");
}

int main(int argc, char** argv) {
    if (argc != 2) {
        printf("usage: Oc2NativeRigidbodyRebuildHistoryHarness <dll>\n");
        return 2;
    }
    HMODULE library = LoadLibraryA(argv[1]);
    if (!library) {
        printf("LoadLibrary failed: %lu\n", GetLastError());
        return 2;
    }
    ApiVersion version = reinterpret_cast<ApiVersion>(
        GetProcAddress(library, "oc2_rigidbody_rebuild_api_version"));
    CaptureSnapshot capture = reinterpret_cast<CaptureSnapshot>(
        GetProcAddress(library, "oc2_contact_manager_pool_capture_snapshot"));
    RestoreSnapshot restore = reinterpret_cast<RestoreSnapshot>(
        GetProcAddress(library, "oc2_contact_manager_pool_restore_snapshot"));
    CaptureManifoldSnapshot captureManifold =
        reinterpret_cast<CaptureManifoldSnapshot>(GetProcAddress(library,
            "oc2_manifold_pool_capture_snapshot"));
    RestoreManifoldSnapshot restoreManifold =
        reinterpret_cast<RestoreManifoldSnapshot>(GetProcAddress(library,
            "oc2_manifold_pool_restore_snapshot"));
    CaptureSipSnapshot captureSip = reinterpret_cast<CaptureSipSnapshot>(
        GetProcAddress(library,
            "oc2_shape_instance_pair_pool_capture_snapshot"));
    CaptureActorPairSnapshot captureActorPair =
        reinterpret_cast<CaptureActorPairSnapshot>(GetProcAddress(library,
            "oc2_actor_pair_pool_capture_snapshot"));
    CaptureActorPairReportSnapshot captureActorPairReport =
        reinterpret_cast<CaptureActorPairReportSnapshot>(GetProcAddress(
            library, "oc2_actor_pair_report_pool_capture_snapshot"));
    CaptureNPhaseReportState captureNPhaseReport =
        reinterpret_cast<CaptureNPhaseReportState>(GetProcAddress(
            library, "oc2_nphase_report_state_capture_snapshot"));
    DirtyAction installDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_install"));
    DirtyAction statusDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_status"));
    DirtyLastNPhase lastDirtyNPhase = reinterpret_cast<DirtyLastNPhase>(
        GetProcAddress(library, "oc2_dirty_interaction_last_nphase"));
    DirtyAction armDirtyCapture = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_capture_arm"));
    DirtyCaptureCopy copyDirtyCapture =
        reinterpret_cast<DirtyCaptureCopy>(GetProcAddress(library,
            "oc2_dirty_interaction_order_capture_copy"));
    DirtyRestoreArm armDirtyRestore =
        reinterpret_cast<DirtyRestoreArm>(GetProcAddress(library,
            "oc2_dirty_interaction_order_restore_arm"));
    DirtyAction uninstallDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_uninstall"));
    DirtyAction cancelDirty = reinterpret_cast<DirtyAction>(GetProcAddress(
        library, "oc2_dirty_interaction_order_cancel"));
    ContextObserverAction installObserver =
        reinterpret_cast<ContextObserverAction>(GetProcAddress(library,
            "oc2_contact_manager_context_observer_install"));
    ContextObserverAction uninstallObserver =
        reinterpret_cast<ContextObserverAction>(GetProcAddress(library,
            "oc2_contact_manager_context_observer_uninstall"));
    ContactRecreateArm armRecreate = reinterpret_cast<ContactRecreateArm>(
        GetProcAddress(library, "oc2_contact_recreate_arm"));
    ContactRecreateAudit auditRecreate =
        reinterpret_cast<ContactRecreateAudit>(GetProcAddress(library,
            "oc2_contact_recreate_audit"));
    ContactRecreateStatus statusRecreate =
        reinterpret_cast<ContactRecreateStatus>(GetProcAddress(library,
            "oc2_contact_recreate_status"));
    ContactRecreateCancel cancelRecreate =
        reinterpret_cast<ContactRecreateCancel>(GetProcAddress(library,
            "oc2_contact_recreate_cancel"));
    Check(version && version() == 14, "API version");
    Check(capture != 0, "capture export");
    Check(restore != 0, "restore export");
    Check(captureManifold != 0, "manifold capture export");
    Check(restoreManifold != 0, "manifold restore export");
    Check(captureSip != 0, "shape-pair pool capture export");
    Check(captureActorPair != 0, "ActorPair pool capture export");
    Check(captureActorPairReport != 0,
        "ActorPair report pool capture export");
    Check(captureNPhaseReport != 0,
        "NPhase report-state capture export");
    Check(installDirty && statusDirty && lastDirtyNPhase && armDirtyCapture && copyDirtyCapture &&
        armDirtyRestore && cancelDirty && uninstallDirty,
        "dirty interaction exports");
    Check(installObserver && uninstallObserver && auditRecreate && armRecreate &&
        statusRecreate && cancelRecreate, "contact recreation exports");
    if (!version || !capture || !restore || !captureManifold || !captureSip ||
        !captureActorPair || !captureActorPairReport ||
        !captureNPhaseReport ||
        !restoreManifold || !installDirty || !statusDirty || !lastDirtyNPhase || !armDirtyCapture ||
        !copyDirtyCapture || !armDirtyRestore || !cancelDirty ||
        !uninstallDirty || !installObserver || !uninstallObserver ||
        !auditRecreate || !armRecreate || !statusRecreate ||
        !cancelRecreate) {
        FreeLibrary(library);
        return 1;
    }

    uint8_t context[0x2D0] = {};
    uint8_t managers[4][0x50] = {};
    uintptr_t values[3] = {
        reinterpret_cast<uintptr_t>(managers[0]),
        reinterpret_cast<uintptr_t>(managers[1]),
        reinterpret_cast<uintptr_t>(managers[2])
    };
    *reinterpret_cast<uintptr_t*>(context + 0x2C8) =
        reinterpret_cast<uintptr_t>(values);
    *reinterpret_cast<uint32_t*>(context + 0x2CC) = 3;
    const uintptr_t contextPointer = reinterpret_cast<uintptr_t>(context);
    const uintptr_t arrayPointer = reinterpret_cast<uintptr_t>(values);

    uintptr_t saved[3] = {};
    ContactPoolReceipt receipt = {};
    Check(capture(contextPointer, saved, 3, &receipt) == 1,
        "capture succeeds");
    Check(receipt.result == 1 && receipt.apiVersion == 14 &&
        receipt.structSize == sizeof(receipt), "capture receipt");
    Check(Same(saved, values, 3), "capture copies exact order");

    uintptr_t permuted[3] = { values[2], values[0], values[1] };
    for (uint32_t i = 0; i < 3; ++i) values[i] = permuted[i];
    receipt = {};
    Check(restore(contextPointer, arrayPointer, saved, 3, &receipt) == 1,
        "restore succeeds for identical membership");
    Check(receipt.result == 1 && Same(values, saved, 3),
        "restore writes exact order");

    uintptr_t before[3] = { values[0], values[1], values[2] };
    receipt = {};
    Check(restore(contextPointer, arrayPointer + sizeof(uintptr_t), saved, 3,
        &receipt) == 0 && receipt.result == 9,
        "wrong free-array address is rejected");
    Check(Same(values, before, 3), "array-address rejection is non-mutating");

    receipt = {};
    Check(restore(contextPointer, arrayPointer, saved, 2, &receipt) == 0 &&
        receipt.result == 10, "wrong count is rejected");
    Check(Same(values, before, 3), "count rejection is non-mutating");

    uintptr_t duplicate[3] = { saved[0], saved[0], saved[2] };
    receipt = {};
    Check(restore(contextPointer, arrayPointer, duplicate, 3, &receipt) == 0 &&
        receipt.result == 6, "duplicate snapshot is rejected");
    Check(Same(values, before, 3), "duplicate rejection is non-mutating");

    values[2] = reinterpret_cast<uintptr_t>(managers[3]);
    uintptr_t changed[3] = { values[0], values[1], values[2] };
    receipt = {};
    Check(restore(contextPointer, arrayPointer, saved, 3, &receipt) == 0 &&
        receipt.result == 11, "changed membership is rejected");
    Check(Same(values, changed, 3), "membership rejection is non-mutating");

    receipt = {};
    uintptr_t tooSmall[2] = {};
    Check(capture(contextPointer, tooSmall, 2, &receipt) == 0 &&
        receipt.result == 2, "short capture buffer is rejected");
    Check(Same(values, changed, 3), "capture rejection is non-mutating");

    uint8_t* revisionImage = CreateRevisionImage();
    Check(revisionImage != 0, "revision image allocation");
    if (revisionImage) {
        RunActorPairPoolTests(revisionImage, captureActorPair);
        RunActorPairReportPoolTests(revisionImage, captureActorPairReport);
        RunNPhaseReportStateTests(revisionImage, captureNPhaseReport);
        RunManifoldPoolTests(revisionImage, 0, captureManifold,
            restoreManifold);
        RunManifoldPoolTests(revisionImage, 1, captureManifold,
            restoreManifold);
        RunContactRecreateSipTests(revisionImage, installObserver,
            uninstallObserver, armRecreate, statusRecreate, cancelRecreate,
            captureSip, auditRecreate);

        uint8_t manifoldContext[0x538] = {};
        uint8_t manifoldNodes[4][0xF0] = {};
        InitializeManifoldPool(manifoldContext, 0, manifoldNodes);
        uintptr_t manifoldSaved[3] = {};
        ManifoldPoolReceipt manifoldReceipt = {};
        revisionImage[kLargePoolCallsiteRva] ^= 1;
        Check(captureManifold(reinterpret_cast<uintptr_t>(revisionImage),
            reinterpret_cast<uintptr_t>(manifoldContext), 0, manifoldSaved,
            3, &manifoldReceipt) == 0 && manifoldReceipt.result == 3,
            "manifold revision mismatch is rejected");
        revisionImage[kLargePoolCallsiteRva] ^= 1;
        manifoldReceipt = {};
        Check(captureManifold(reinterpret_cast<uintptr_t>(revisionImage),
            reinterpret_cast<uintptr_t>(manifoldContext), 2, manifoldSaved,
            3, &manifoldReceipt) == 0 && manifoldReceipt.result == 4,
            "invalid manifold pool kind is rejected");
        RunDirtyInteractionTests(revisionImage, installDirty, statusDirty, lastDirtyNPhase,
            armDirtyCapture, copyDirtyCapture, armDirtyRestore,
            cancelDirty, uninstallDirty);
        VirtualFree(revisionImage, 0, MEM_RELEASE);
    }

    FreeLibrary(library);
    if (failures) {
        printf("FAILED %d\n", failures);
        return 1;
    }
    printf("PASS caller-owned SIP/contact/manifold-pool history\n");
    return 0;
}
