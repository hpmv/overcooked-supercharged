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

static uint32_t WordHash(const uint32_t* values, uint32_t count) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= values[i];
        hash *= 16777619u;
    }
    return hash;
}

static uint32_t ByteHash(const void* value, uint32_t count) {
    const uint8_t* bytes = static_cast<const uint8_t*>(value);
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= bytes[i];
        hash *= 16777619u;
    }
    return hash;
}

static uint32_t AppendByteHash(uint32_t hash, const void* value,
    uint32_t count) {
    const uint8_t* bytes = static_cast<const uint8_t*>(value);
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= bytes[i];
        hash *= 16777619u;
    }
    return hash;
}

static uint32_t BitCount32(uint32_t value) {
    value = value - ((value >> 1) & 0x55555555u);
    value = (value & 0x33333333u) + ((value >> 2) & 0x33333333u);
    return (((value + (value >> 4)) & 0x0F0F0F0Fu) * 0x01010101u) >> 24;
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

// A read-only semantic view of one pool-used PxsContactManager.  The fields
// are intentionally scalar: this ABI can be consumed by the managed
// authoring module without compiling any PhysX headers into the shipped game.
struct ContactManagerOwnerRecord {
    uint32_t slot;
    uint32_t membership;
    uintptr_t manager;
    uintptr_t sip;
    uintptr_t primaryVtable;
    uintptr_t secondaryVtable;
    uintptr_t shapeSim0;
    uintptr_t shapeSim1;
    uintptr_t pxsShapeCore0;
    uintptr_t pxsShapeCore1;
    uintptr_t pxShape0;
    uintptr_t pxShape1;
    uintptr_t rigidBody0;
    uintptr_t rigidBody1;
    uintptr_t rigidCore0;
    uintptr_t rigidCore1;
    uintptr_t manifold;
    uintptr_t cachePointer;
    uint32_t pairData;
    uint32_t transformCache0;
    uint32_t transformCache1;
    uint32_t managerFlags;
    uint32_t sipFlags;
    uint32_t contactReportStamp;
    uint32_t reportPairIndex;
    uint32_t reportStreamIndex;
    uintptr_t actorPair;
    uint32_t managerHash;
    uint32_t sipHash;
    uint32_t manifoldHash;
    uint32_t manifoldBytes;
    uint32_t cacheHash;
    uint16_t cacheSize;
    uint16_t contactCount;
    uint16_t workUnitFlags;
    uint16_t statusFlags;
    uint8_t interactionType;
    uint8_t interactionFlags;
    uint8_t geomType0;
    uint8_t geomType1;
    uint8_t disableResponse;
    uint8_t disableCcd;
    uint16_t validationFlags;
    // Exact Sc::ActorPair state while this SIP owns the pair.  This is
    // captured at the same output boundary as the manager and SIP so the
    // rewind planner never has to infer an allocated ActorPair from bytes
    // that have subsequently become an intrusive free-list node.
    uintptr_t actorPairActor0;
    uintptr_t actorPairActor1;
    uintptr_t actorPairScene;
    uint16_t actorPairInternalFlags;
    uint16_t actorPairTouchCount;
    uint16_t actorPairRefCount;
    uint16_t actorPairReserved;
    uintptr_t actorPairReportData;
    uint32_t actorPairHash;
};

struct ContactManagerOwnerReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t context;
    uintptr_t pool;
    uintptr_t freeArray;
    uintptr_t slabs;
    uintptr_t useBitmap;
    uintptr_t activeBitmap;
    uintptr_t touchBitmap;
    uintptr_t modifiableBitmap;
    uint32_t elementsPerSlab;
    uint32_t maximumSlabs;
    uint32_t slabCount;
    uint32_t log2ElementsPerSlab;
    uint32_t freeCount;
    uint32_t useWordCount;
    uint32_t activeWordCount;
    uint32_t touchWordCount;
    uint32_t modifiableWordCount;
    uint32_t totalSlots;
    uint32_t usedCount;
    uint32_t activeCount;
    uint32_t touchCount;
    uint32_t modifiableCount;
    uint32_t recordsRequired;
    uint32_t recordsWritten;
    uint32_t freeOrderHash;
    uint32_t freeIndexOrderHash;
    uint32_t useBitmapHash;
    uint32_t activeBitmapHash;
    uint32_t touchBitmapHash;
    uint32_t modifiableBitmapHash;
    uint32_t ownerHash;
    uint32_t consistencyFlags;
    uint32_t invalidSlot;
    uint32_t detail;
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

// One rewind-only allocation row.  Endpoint identity is the canonical
// unordered PxsShapeCore pair from PxvManagerDescRigidRigid.  The shipped
// allocator still performs every initialization; the hook only selects which
// already-free manager and manifold it will pop for this pair.
struct ContactRecreatePlanRow {
    uintptr_t pxsShapeCoreLow;
    uintptr_t pxsShapeCoreHigh;
    uintptr_t targetManager;
    uintptr_t targetManifold;
    uintptr_t targetSip;
    uint32_t targetSlot;
    uint32_t manifoldBytes;
    uint32_t targetManagerFlags;
    uint32_t flags;
};

struct ContactRecreateReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t context;
    uintptr_t freeArray;
    uintptr_t largePool;
    uint32_t state;
    uint32_t rowCount;
    uint32_t matchedCount;
    uint32_t remainingCount;
    uint32_t contactCountBefore;
    uint32_t targetContactCount;
    uint32_t contactCountCurrent;
    uint32_t contactHashBefore;
    uint32_t targetContactHash;
    uint32_t contactHashCurrent;
    uint32_t largeCountBefore;
    uint32_t targetLargeCount;
    uint32_t largeCountCurrent;
    uint32_t largeHashBefore;
    uint32_t targetLargeHash;
    uint32_t largeHashCurrent;
    uint32_t largeUsedBefore;
    uint32_t targetLargeUsed;
    uint32_t largeUsedCurrent;
    uint32_t largeUnreleasedBefore;
    uint32_t targetLargeUnreleased;
    uint32_t largeUnreleasedCurrent;
    uint32_t matchedMask;
    uint32_t threadId;
    uintptr_t lastSip;
    uintptr_t lastShapeLow;
    uintptr_t lastShapeHigh;
    uintptr_t lastManager;
    uintptr_t lastManifold;
    uint32_t invalidRow;
    uint32_t detail;
    uint32_t installed;
    uint32_t armed;
    uint32_t observerEntries;
    uint32_t attemptOrdinal;
    uintptr_t attemptSip;
    uintptr_t attemptShapeLow;
    uintptr_t attemptShapeHigh;
    uint32_t attemptRow;
    uint32_t attemptMatchedMask;
    uint32_t attemptFreeCount;
    uint32_t attemptManagerIndex;
    uintptr_t attemptLargeHead;
    uint32_t attemptManifoldIndex;
    uintptr_t nphaseCore;
    uintptr_t sipPool;
    uint32_t sipMatchedCount;
    uint32_t sipRemainingCount;
    uint32_t targetSipCount;
    uint32_t targetSipHash;
    uint32_t targetSipUsed;
    uint32_t targetSipUnreleased;
    uint32_t sipCountCurrent;
    uint32_t sipHashCurrent;
    uint32_t sipUsedCurrent;
    uint32_t sipUnreleasedCurrent;
    uint32_t sipMatchedMask;
    uint32_t sipObserverEntries;
    uintptr_t lastAllocatedSip;
};

// Pure planning receipt for the contact/SIP/large-manifold portion of a
// rewind.  Unlike ContactRecreateReceipt this is never backed by process-wide
// hook state: the audit writes only this caller-owned receipt and performs no
// allocator, bitmap, hook, or PhysX writes.  evaluatedMask distinguishes a
// passed check from one whose prerequisites made it unsafe to evaluate.
struct ContactRecreateAuditReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t context;
    uintptr_t nphaseCore;
    uintptr_t sipPool;
    uintptr_t freeArray;
    uintptr_t largePool;
    uint32_t evaluatedMask;
    uint32_t issueMask;
    uint32_t recreateState;
    uint32_t observerInstalled;
    uint32_t rowCount;
    uint32_t invalidRowCount;
    uint32_t duplicateRowCount;
    uint32_t targetFreeSipRowCount;
    uint32_t activeSipRowCount;
    uint32_t targetSipCount;
    uint32_t targetSipHash;
    uint32_t targetSipUsed;
    uint32_t targetSipUnreleased;
    uint32_t liveSipCount;
    uint32_t liveSipHash;
    uint32_t liveSipUsed;
    uint32_t liveSipUnreleased;
    uint32_t expectedSemanticSipCount;
    uint32_t expectedLegacySipCount;
    uint32_t sipMissingCount;
    uint32_t sipExtraCount;
    uint32_t targetContactCount;
    uint32_t targetContactHash;
    uint32_t liveContactCount;
    uint32_t liveContactHash;
    uint32_t expectedContactCount;
    uint32_t contactMissingCount;
    uint32_t contactExtraCount;
    uint32_t useBitmapCount;
    uint32_t activeBitmapCount;
    uint32_t touchBitmapCount;
    uint32_t modifiableBitmapCount;
    uint32_t targetLargeCount;
    uint32_t targetLargeHash;
    uint32_t targetLargeUsed;
    uint32_t targetLargeUnreleased;
    uint32_t liveLargeCount;
    uint32_t liveLargeHash;
    uint32_t liveLargeUsed;
    uint32_t liveLargeUnreleased;
    uint32_t expectedLargeCount;
    uint32_t largeMissingCount;
    uint32_t largeExtraCount;
    uint32_t unwritableCount;
    uint32_t firstResult;
    uint32_t firstError;
    uint32_t firstRow;
    uint32_t firstDetail;
};

struct SipPoolReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t nphaseCore;
    uintptr_t pool;
    uintptr_t freeHead;
    uint32_t elementSize;
    uint32_t elementsPerSlab;
    uint32_t used;
    uint32_t unreleased;
    uint32_t slabSize;
    uint32_t traversedCount;
    uint32_t orderHash;
    uint32_t validationFlags;
    uintptr_t top[16];
};

// Read-only complete partition of Sc::NPhaseCore::mActorPairPool.  Free order
// is allocator-significant; allocated order is canonical slab/index order so
// target/live set comparisons do not depend on hash containers or addresses
// supplied by managed code.
struct ActorPairPoolReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t nphaseCore;
    uintptr_t pool;
    uintptr_t freeHead;
    uintptr_t slabs;
    uint32_t elementSize;
    uint32_t elementsPerSlab;
    uint32_t used;
    uint32_t unreleased;
    uint32_t slabSize;
    uint32_t slabCount;
    uint32_t totalElements;
    uint32_t freeCount;
    uint32_t freeOrderHash;
    uint32_t allocatedCount;
    uint32_t allocatedOrderHash;
    uint32_t validationFlags;
    uintptr_t topFree[16];
    uintptr_t topAllocated[16];
};

// ActorPairContactReportData uses the same Ps::Pool implementation as
// ActorPair, but its objects are 0x24 bytes and live in NPhaseCore's pool at
// +0x530.  Capturing both sides of this partition is required before an
// ActorPair's persistent report-history pointer can be reconstructed without
// synthesizing notification callbacks.
struct ActorPairReportPoolReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t nphaseCore;
    uintptr_t pool;
    uintptr_t freeHead;
    uintptr_t slabs;
    uint32_t elementSize;
    uint32_t elementsPerSlab;
    uint32_t used;
    uint32_t unreleased;
    uint32_t slabSize;
    uint32_t slabCount;
    uint32_t totalElements;
    uint32_t freeCount;
    uint32_t freeOrderHash;
    uint32_t allocatedCount;
    uint32_t allocatedOrderHash;
    uint32_t validationFlags;
    uintptr_t topFree[16];
    uintptr_t topAllocated[16];
};

// Read-only checkpoint view of the report-history structures that precede
// mDirtyInteractions in Sc::NPhaseCore.  Array capacities retain PhysX's raw
// ownership bit; the caller receives the exact logical orders and the bytes
// currently committed in ContactReportBuffer without changing any header.
struct NPhaseReportStateReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t nphaseCore;
    uintptr_t ownerScene;
    uintptr_t actorPairData;
    uint32_t actorPairCount;
    uint32_t actorPairCapacityRaw;
    uintptr_t persistentData;
    uint32_t persistentCount;
    uint32_t persistentCapacityRaw;
    uint32_t nextFramePersistentIndex;
    uintptr_t forceThresholdData;
    uint32_t forceThresholdCount;
    uint32_t forceThresholdCapacityRaw;
    uintptr_t reportBuffer;
    uint32_t reportBufferCurrentIndex;
    uint32_t reportBufferCurrentSize;
    uint32_t reportBufferDefaultSize;
    uint32_t reportBufferLastIndex;
    uint32_t reportBufferAllocationLocked;
    uint32_t actorPairOrderHash;
    uint32_t persistentOrderHash;
    uint32_t forceThresholdOrderHash;
    uint32_t reportBufferActiveHash;
    uint32_t reportBufferAllocationHash;
    uint32_t validationFlags;
};

// Caller-owned, read-only projection of Sc::InteractionScene and every
// registration/index relation that can affect the next simulation step.
struct InteractionGraphActorRecord {
    uintptr_t actor;
    uintptr_t vtable;
    uintptr_t inlineSlots[4];
    uintptr_t interactionsData;
    uintptr_t firstElement;
    uintptr_t interactionScene;
    uint32_t sceneArrayIndex;
    uint32_t interactionOutputStart;
    uint32_t interactionCount;
    uint32_t interactionCapacity;
    uint32_t activeBodyIndex;
    uint32_t interactionOrderHash;
    uint16_t transferringCount;
    uint16_t uniqueCount;
    uint16_t countedCount;
    uint8_t actorType;
    uint8_t islandNodeInfo;
    uint32_t validationFlags;
};

struct InteractionGraphInteractionRecord {
    uintptr_t interaction;
    uintptr_t vtable;
    uintptr_t actor0;
    uintptr_t actor1;
    uintptr_t element0;
    uintptr_t element1;
    uintptr_t shapeCore0;
    uintptr_t shapeCore1;
    uintptr_t pxsShapeCore0;
    uintptr_t pxsShapeCore1;
    uintptr_t semanticLow;
    uintptr_t semanticHigh;
    uint32_t sceneId;
    uint32_t globalIndex;
    uint32_t active;
    uint16_t actorId0;
    uint16_t actorId1;
    uint8_t interactionType;
    uint8_t interactionFlags;
    uint16_t reserved;
    uint32_t validationFlags;
};

struct InteractionGraphPoolReceipt {
    uintptr_t pool;
    uintptr_t slabData;
    uintptr_t freeHead;
    uint32_t blockCapacity;
    uint32_t blockBytes;
    uint32_t inlineBufferUsed;
    uint32_t slabCount;
    uint32_t slabCapacityRaw;
    uint32_t elementsPerSlab;
    uint32_t used;
    uint32_t unreleasedFree;
    uint32_t slabSize;
    uint32_t totalElements;
    uint32_t freeCount;
    uint32_t slabOutputStart;
    uint32_t freeOutputStart;
    uint32_t slabOrderHash;
    uint32_t freeOrderHash;
    uint32_t usedOwnerHash;
    uint32_t validationFlags;
};

struct InteractionGraphReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t nphaseCore;
    uintptr_t ownerScene;
    uintptr_t interactionScene;
    uintptr_t llContext;
    uint32_t timestamp;
    uintptr_t activeBodiesData;
    uint32_t activeBodiesCount;
    uint32_t activeBodiesCapacityRaw;
    uint32_t activeTwoWayStart;
    uintptr_t globalData[6];
    uint32_t globalCount[6];
    uint32_t globalCapacityRaw[6];
    uint32_t globalActiveCount[6];
    uint32_t globalOrderHash[6];
    uint32_t activeBodiesRequired;
    uint32_t activeBodiesWritten;
    uint32_t actorsRequired;
    uint32_t actorsWritten;
    uint32_t interactionsRequired;
    uint32_t interactionsWritten;
    uint32_t actorSlotsRequired;
    uint32_t actorSlotsWritten;
    uint32_t poolSlabsRequired;
    uint32_t poolSlabsWritten;
    uint32_t poolFreeRequired;
    uint32_t poolFreeWritten;
    uint32_t actorHash;
    uint32_t interactionHash;
    uint32_t actorSlotHash;
    uint32_t poolHash;
    uint32_t graphHash;
    uint32_t validationFlags;
    uint32_t invalidKind;
    uint32_t invalidIndex;
    uint32_t detail;
    InteractionGraphPoolReceipt pools[3];
};

// Exact, caller-owned projection of PxsTransformCache.  Entries cover every
// ID below mCurrentID, including IDs in the LIFO free array.  Binding rows
// follow the type-zero InteractionScene array and preserve endpoint
// orientation (endpoint zero, then endpoint one for each interaction).
struct TransformCacheEntryRecord {
    uint32_t id;
    uint32_t refCount;
    float rotation[4];
    float position[3];
    uint32_t poseHash;
    uint32_t bindingCount;
    uint32_t stateFlags;
};

struct TransformCacheBindingRecord {
    uintptr_t shapeSim;
    uintptr_t shapeCore;
    uintptr_t pxsShapeCore;
    uintptr_t interaction;
    uint32_t interactionIndex;
    uint32_t endpointIndex;
    uint32_t cacheId;
    uint32_t refCount;
    uint32_t poseHash;
    uint32_t validationFlags;
};

struct TransformCacheReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t nphaseCore;
    uintptr_t ownerScene;
    uintptr_t interactionScene;
    uintptr_t context;
    uintptr_t transformCache;
    uint32_t currentId;
    uintptr_t freeData;
    uint32_t freeCount;
    uint32_t freeCapacityRaw;
    uintptr_t transformsData;
    uint32_t transformsCount;
    uint32_t transformsCapacityRaw;
    uintptr_t refCountsData;
    uint32_t refCountsCount;
    uint32_t refCountsCapacityRaw;
    uint32_t entriesRequired;
    uint32_t entriesWritten;
    uint32_t freeRequired;
    uint32_t freeWritten;
    uint32_t bindingsRequired;
    uint32_t bindingsWritten;
    uint32_t liveCount;
    uint32_t totalRefCount;
    uint32_t entryHash;
    uint32_t freeOrderHash;
    uint32_t bindingHash;
    uint32_t snapshotHash;
    uint32_t validationFlags;
    uint32_t invalidKind;
    uint32_t invalidIndex;
    uint32_t detail;
};

// One oriented AABB overlap as it existed at finishBroadPhase entry.  The
// hook writes these records only into a fixed DLL-owned ring; callers copy
// them later using the exact observation ordinal returned by arm/status.
struct BroadPhaseOverlapRecord {
    uintptr_t userData0;
    uintptr_t userData1;
    uintptr_t shapeCore0;
    uintptr_t shapeCore1;
    uintptr_t pxsShapeCore0;
    uintptr_t pxsShapeCore1;
    uint32_t cacheId0;
    uint32_t cacheId1;
    uint32_t pairHash;
    uint32_t validationFlags;
};

struct FinishBroadPhaseObserverReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uintptr_t unityBase;
    uintptr_t expectedScene;
    uintptr_t expectedContext;
    uintptr_t expectedNPhaseCore;
    uintptr_t observedScene;
    uintptr_t observedContext;
    uintptr_t observedNPhaseCore;
    uintptr_t aabbManager;
    uintptr_t interactionScene;
    uintptr_t transformCache;
    uint32_t installed;
    uint32_t state;
    uint32_t expectedPass;
    uint32_t armedThreadId;
    uint32_t armedOrdinal;
    uint32_t observationOrdinal;
    uint32_t slotIndex;
    uint32_t pass;
    uint32_t threadId;
    uint32_t createdRequired;
    uint32_t createdWritten;
    uint32_t deletedRequired;
    uint32_t deletedWritten;
    uint32_t createdHash;
    uint32_t deletedHash;
    uint32_t preCacheHash;
    uint32_t postCacheHash;
    uint32_t preGraphHash;
    uint32_t postGraphHash;
    uint32_t validationFlags;
    uint32_t invalidKind;
    uint32_t invalidIndex;
    uint32_t detail;
    uint32_t droppedObservations;
};

// PxsIslandManager is a shipped-build-only ABI.  Keep these records at
// explicit four-byte packing even though the older exported records above
// live under the translation unit's historical pack(8) block.
#pragma pack(push, 4)
struct IslandNodeSlotRecord {
    uint32_t id;
    uint32_t ownerOrArticulationRaw;
    uint32_t islandId;
    uint32_t rawFlagsWord;
    uint32_t freeNext;
    uint32_t nextNode;
    uint32_t slotFlags;
    uint32_t validationFlags;
};

struct IslandEdgeSlotRecord {
    uint32_t id;
    uint32_t node0;
    uint32_t node1;
    uint32_t taggedObjectRaw;
    uint32_t freeNext;
    uint32_t nextEdge;
    uint32_t slotFlags;
    uint32_t semanticBindingIndex;
    uint32_t validationFlags;
};

struct IslandSlotRecord {
    uint32_t id;
    uint32_t startNode;
    uint32_t startEdge;
    uint32_t endNode;
    uint32_t endEdge;
    uint32_t freeNext;
    uint32_t slotFlags;
    uint32_t validationFlags;
};

struct IslandArticulationRootSlotRecord {
    uint32_t id;
    uint32_t articulationLinkHandle;
    uint32_t articulationOwner;
    uint32_t freeNext;
    uint32_t slotFlags;
    uint32_t validationFlags;
};

struct IslandSipEdgeBinding {
    uint32_t edgeId;
    uint32_t edgeType;
    uint32_t sip;
    uint32_t hookAddress;
    uint32_t shapeSim0;
    uint32_t shapeSim1;
    uint32_t pxsShapeCoreLow;
    uint32_t pxsShapeCoreHigh;
    uint32_t contactManager;
    uint32_t taggedObjectRaw;
    uint32_t validationFlags;
};

struct IslandEdgeJournalRecord {
    uint32_t ordinal;
    uint32_t eventKind;
    uint32_t observerPhase;
    uint32_t threadId;
    uint32_t edgeType;
    uint32_t node0;
    uint32_t node1;
    uint32_t preEdgeId;
    uint32_t postEdgeId;
    uint32_t hookAddress;
    uint32_t ownerObject;
    uint32_t pxsShapeCoreLow;
    uint32_t pxsShapeCoreHigh;
    uint32_t validationFlags;
};

struct IslandSnapshotBuffersV1 {
    IslandNodeSlotRecord* nodes; uint32_t nodeCapacity;
    IslandEdgeSlotRecord* edges; uint32_t edgeCapacity;
    IslandSlotRecord* islands; uint32_t islandCapacity;
    IslandArticulationRootSlotRecord* roots; uint32_t rootCapacity;
    uint32_t* kinematicWords; uint32_t kinematicWordCapacity;
    uint32_t* kinematicChangeWords; uint32_t kinematicChangeWordCapacity;
    uint32_t* notReadyWords; uint32_t notReadyWordCapacity;
    uint32_t* notReadyChangeWords; uint32_t notReadyChangeWordCapacity;
    uint32_t* islandWords; uint32_t islandWordCapacity;
    uint32_t* nodeCreated; uint32_t nodeCreatedCapacity;
    uint32_t* nodeDeleted; uint32_t nodeDeletedCapacity;
    uint32_t* edgeCreated; uint32_t edgeCreatedCapacity;
    uint32_t* edgeDeleted; uint32_t edgeDeletedCapacity;
    uint32_t* edgeBroken; uint32_t edgeBrokenCapacity;
    uint32_t* edgeJoined; uint32_t edgeJoinedCapacity;
    IslandSipEdgeBinding* bindings; uint32_t bindingCapacity;
};

struct IslandElementManagerReceipt {
    uint32_t vtable;
    uint32_t elements;
    uint32_t freeNext;
    uint32_t nextList;
    uint32_t capacity;
    uint32_t freeHead;
    uint32_t freeCount;
    uint32_t required;
    uint32_t written;
    uint32_t elementHash;
    uint32_t freeChainHash;
    uint32_t nextHash;
};

struct IslandQueueReceipt {
    uint32_t data;
    uint32_t count;
    uint32_t capacity;
    uint32_t defaultCapacity;
    uint32_t required;
    uint32_t written;
    uint32_t hash;
};

struct IslandBitmapReceipt {
    uint32_t data;
    uint32_t wordCount;
    uint32_t required;
    uint32_t written;
    uint32_t hash;
};

struct IslandSnapshotReceiptV1 {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uint32_t unityBase;
    uint32_t nphaseCore;
    uint32_t ownerScene;
    uint32_t interactionScene;
    uint32_t context;
    uint32_t islandManager;
    uint32_t phase;
    uint32_t observerSequence;
    uint32_t observationOrdinal;
    uint32_t captureThreadId;
    uint32_t epoch;
    IslandElementManagerReceipt node;
    IslandElementManagerReceipt edge;
    IslandElementManagerReceipt island;
    IslandElementManagerReceipt root;
    IslandQueueReceipt nodeCreated;
    IslandQueueReceipt nodeDeleted;
    IslandQueueReceipt edgeCreated;
    IslandQueueReceipt edgeDeleted;
    IslandQueueReceipt edgeBroken;
    IslandQueueReceipt edgeJoined;
    IslandBitmapReceipt kinematic;
    IslandBitmapReceipt kinematicChange;
    IslandBitmapReceipt notReady;
    IslandBitmapReceipt notReadyChange;
    IslandBitmapReceipt islandBitmap;
    uint32_t numAddedRBodies;
    uint32_t numAddedArtics;
    uint32_t numAddedKinematics;
    uint32_t numAddedEdgesContact;
    uint32_t numAddedEdgesConstraint;
    uint32_t numAddedEdgesArticulation;
    uint32_t numEdgeReferencesToKinematic;
    uint32_t numRequiredKinematicDuplicates;
    uint32_t everythingAsleep;
    uint32_t hasAnythingChanged;
    uint32_t performIslandUpdate;
    uint32_t liveContactEdges;
    uint32_t liveConstraintEdges;
    uint32_t liveArticulationEdges;
    uint32_t bindingsRequired;
    uint32_t bindingsWritten;
    uint32_t bindingHash;
    uint32_t journalBeginOrdinal;
    uint32_t journalEndOrdinal;
    uint32_t journalOverflowCount;
    uint32_t snapshotHash;
    uint32_t validationFlags;
    uint32_t invalidKind;
    uint32_t invalidIndex;
    uint32_t detail;
};

struct IslandUpdateObserverReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uint32_t unityBase;
    uint32_t expectedManager;
    uint32_t expectedContext;
    uint32_t expectedNphase;
    uint32_t observedManager;
    uint32_t observedContext;
    uint32_t observedNphase;
    uint32_t installed;
    uint32_t state;
    uint32_t expectedPass;
    uint32_t armedThreadId;
    uint32_t observerSequence;
    uint32_t armedOrdinal;
    uint32_t observationOrdinal;
    uint32_t slotIndex;
    uint32_t pass;
    uint32_t threadId;
    uint32_t preResult;
    uint32_t postResult;
    uint32_t preSnapshotHash;
    uint32_t postSnapshotHash;
    uint32_t journalBeginOrdinal;
    uint32_t journalEndOrdinal;
    uint32_t validationFlags;
    uint32_t invalidKind;
    uint32_t invalidIndex;
    uint32_t detail;
    uint32_t inFlight;
};

struct IslandEdgeJournalReceipt {
    uint32_t apiVersion;
    uint32_t structSize;
    uint32_t result;
    uint32_t lastError;
    uint32_t unityBase;
    uint32_t expectedManager;
    uint32_t installed;
    uint32_t state;
    uint32_t firstOrdinal;
    uint32_t nextOrdinal;
    uint32_t requestedBegin;
    uint32_t recordsRequired;
    uint32_t recordsWritten;
    uint32_t overflowCount;
    uint32_t addCount;
    uint32_t removeCount;
    uint32_t recordHash;
    uint32_t validationFlags;
    uint32_t invalidKind;
    uint32_t invalidIndex;
    uint32_t detail;
};
#pragma pack(pop)

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
static_assert(sizeof(ContactManagerOwnerRecord) == 172,
    "Unexpected Win32 contact-manager owner record ABI");
static_assert(sizeof(ContactManagerOwnerReceipt) == 156,
    "Unexpected Win32 contact-manager owner receipt ABI");
static_assert(sizeof(ContactRecreatePlanRow) == 36,
    "Unexpected Win32 contact-recreate plan-row ABI");
static_assert(sizeof(ContactRecreateReceipt) == 268,
    "Unexpected Win32 contact-recreate receipt ABI");
static_assert(sizeof(ContactRecreateAuditReceipt) == 232,
    "Unexpected Win32 contact-recreate audit receipt ABI");
static_assert(sizeof(SipPoolReceipt) == 128,
    "Unexpected Win32 SIP-pool receipt ABI");
static_assert(sizeof(ActorPairPoolReceipt) == 212,
    "Unexpected Win32 ActorPair-pool receipt ABI");
static_assert(sizeof(ActorPairReportPoolReceipt) == 212,
    "Unexpected Win32 ActorPair-report-pool receipt ABI");
static_assert(sizeof(NPhaseReportStateReceipt) == 116,
    "Unexpected Win32 NPhase report-state receipt ABI");
static_assert(sizeof(InteractionGraphActorRecord) == 72,
    "Unexpected Win32 interaction-graph actor ABI");
static_assert(sizeof(InteractionGraphInteractionRecord) == 72,
    "Unexpected Win32 interaction-graph interaction ABI");
static_assert(sizeof(InteractionGraphPoolReceipt) == 80,
    "Unexpected Win32 interaction-graph pool ABI");
static_assert(sizeof(InteractionGraphReceipt) == 500,
    "Unexpected Win32 interaction-graph receipt ABI");
static_assert(sizeof(TransformCacheEntryRecord) == 48,
    "Unexpected Win32 transform-cache entry ABI");
static_assert(sizeof(TransformCacheBindingRecord) == 40,
    "Unexpected Win32 transform-cache binding ABI");
static_assert(sizeof(TransformCacheReceipt) == 144,
    "Unexpected Win32 transform-cache receipt ABI");
static_assert(sizeof(BroadPhaseOverlapRecord) == 40,
    "Unexpected Win32 broadphase overlap ABI");
static_assert(sizeof(FinishBroadPhaseObserverReceipt) == 152,
    "Unexpected Win32 finishBroadPhase observer ABI");
static_assert(sizeof(IslandNodeSlotRecord) == 32,
    "Unexpected Win32 island node record ABI");
static_assert(sizeof(IslandEdgeSlotRecord) == 36,
    "Unexpected Win32 island edge record ABI");
static_assert(sizeof(IslandSlotRecord) == 32,
    "Unexpected Win32 island record ABI");
static_assert(sizeof(IslandArticulationRootSlotRecord) == 24,
    "Unexpected Win32 island articulation-root record ABI");
static_assert(sizeof(IslandSipEdgeBinding) == 44,
    "Unexpected Win32 island SIP binding ABI");
static_assert(sizeof(IslandEdgeJournalRecord) == 56,
    "Unexpected Win32 island edge journal ABI");
static_assert(sizeof(IslandSnapshotBuffersV1) == 128,
    "Unexpected Win32 island snapshot buffer ABI");
static_assert(sizeof(IslandElementManagerReceipt) == 48,
    "Unexpected Win32 island element-manager receipt ABI");
static_assert(sizeof(IslandQueueReceipt) == 28,
    "Unexpected Win32 island queue receipt ABI");
static_assert(sizeof(IslandBitmapReceipt) == 20,
    "Unexpected Win32 island bitmap receipt ABI");
static_assert(sizeof(IslandSnapshotReceiptV1) == 620,
    "Unexpected Win32 island snapshot receipt ABI");
static_assert(sizeof(IslandUpdateObserverReceipt) == 128,
    "Unexpected Win32 island update-observer receipt ABI");
static_assert(sizeof(IslandEdgeJournalReceipt) == 84,
    "Unexpected Win32 island edge-journal receipt ABI");
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

enum ContactManagerOwnerResult : uint32_t {
    ContactManagerOwnerOk = 1,
    ContactManagerOwnerBadArgument = 2,
    ContactManagerOwnerRevisionMismatch = 3,
    ContactManagerOwnerUnreadablePool = 4,
    ContactManagerOwnerInvalidPool = 5,
    ContactManagerOwnerUnreadableBitmap = 6,
    ContactManagerOwnerInvalidFreeEntry = 7,
    ContactManagerOwnerMembershipMismatch = 8,
    ContactManagerOwnerCapacityTooSmall = 9,
    ContactManagerOwnerUnreadableManager = 10,
    ContactManagerOwnerInvalidManager = 11,
    ContactManagerOwnerInvalidSip = 12,
    ContactManagerOwnerInvalidShape = 13,
    ContactManagerOwnerUnstable = 14
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

enum ContactRecreateResult : uint32_t {
    ContactRecreateOk = 1,
    ContactRecreateBadArgument = 2,
    ContactRecreateRevisionMismatch = 3,
    ContactRecreateObserverMissing = 4,
    ContactRecreateAlreadyArmed = 5,
    ContactRecreateInvalidPlan = 6,
    ContactRecreateContactMembership = 7,
    ContactRecreateManifoldMembership = 8,
    ContactRecreateMetadataMismatch = 9,
    ContactRecreateNotWritable = 10,
    ContactRecreateWriteVerificationFailed = 11,
    ContactRecreateUnexpectedPair = 12,
    ContactRecreateDuplicatePair = 13,
    ContactRecreateManagerUnavailable = 14,
    ContactRecreateManifoldUnavailable = 15,
    ContactRecreateThreadChanged = 16,
    ContactRecreateContextChanged = 17,
    ContactRecreateSipMembership = 18,
    ContactRecreateSipUnavailable = 19,
    ContactRecreateSipResultMismatch = 20
};

enum SipPoolResult : uint32_t {
    SipPoolOk = 1,
    SipPoolBadArgument = 2,
    SipPoolRevisionMismatch = 3,
    SipPoolUnreadable = 4,
    SipPoolInvalidMetadata = 5,
    SipPoolInvalidNode = 6,
    SipPoolDuplicateNode = 7,
    SipPoolCapacityTooSmall = 8
};

enum ContactRecreateState : uint32_t {
    ContactRecreateIdle = 0,
    ContactRecreateArmed = 1,
    ContactRecreateComplete = 2,
    ContactRecreatePoisoned = 3
};

enum ContactRecreateAuditResult : uint32_t {
    ContactRecreateAuditClean = 1,
    ContactRecreateAuditHasIssues = 2
};

enum ContactRecreateAuditCheck : uint32_t {
    ContactAuditArguments = 1u << 0,
    ContactAuditObserver = 1u << 1,
    ContactAuditRecreateState = 1u << 2,
    ContactAuditRevisions = 1u << 3,
    ContactAuditTargetBuffers = 1u << 4,
    ContactAuditTargetUniqueness = 1u << 5,
    ContactAuditRows = 1u << 6,
    ContactAuditRowUniqueness = 1u << 7,
    ContactAuditSipCapture = 1u << 8,
    ContactAuditSipIdentity = 1u << 9,
    ContactAuditSipSemanticCounts = 1u << 10,
    ContactAuditSipMembership = 1u << 11,
    ContactAuditSipLegacyArm = 1u << 12,
    ContactAuditContactCapture = 1u << 13,
    ContactAuditContactIdentity = 1u << 14,
    ContactAuditContactBitmaps = 1u << 15,
    ContactAuditContactMembership = 1u << 16,
    ContactAuditLargeCapture = 1u << 17,
    ContactAuditLargeIdentity = 1u << 18,
    ContactAuditLargeMembership = 1u << 19,
    ContactAuditWritability = 1u << 20
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
    DirtyInteractionOrderPending = 18,
    DirtyInteractionOrderUnstable = 19
};

enum DirtyInteractionOrderAction : uint32_t {
    DirtyInteractionOrderIdle = 0,
    DirtyInteractionOrderCapture = 1,
    DirtyInteractionOrderRestore = 2
};

enum InteractionGraphResult : uint32_t {
    InteractionGraphOk = 1,
    InteractionGraphBadArgument = 2,
    InteractionGraphRevisionMismatch = 3,
    InteractionGraphUnreadable = 4,
    InteractionGraphInvalidMetadata = 5,
    InteractionGraphCapacityTooSmall = 6,
    InteractionGraphInvalidActiveBody = 7,
    InteractionGraphInvalidInteraction = 8,
    InteractionGraphInvalidActor = 9,
    InteractionGraphInvalidPool = 10,
    InteractionGraphUnstable = 11
};

enum TransformCacheResult : uint32_t {
    TransformCacheOk = 1,
    TransformCacheBadArgument = 2,
    TransformCacheRevisionMismatch = 3,
    TransformCacheUnreadable = 4,
    TransformCacheInvalidMetadata = 5,
    TransformCacheCapacityTooSmall = 6,
    TransformCacheInvalidFreeId = 7,
    TransformCacheInvalidBinding = 8,
    TransformCacheReferenceMismatch = 9,
    TransformCacheUnstable = 10
};

enum FinishBroadPhaseObserverResult : uint32_t {
    FinishBroadPhaseObserverOk = 1,
    FinishBroadPhaseObserverBadArgument = 2,
    FinishBroadPhaseObserverRevisionMismatch = 3,
    FinishBroadPhaseObserverAlreadyInstalled = 4,
    FinishBroadPhaseObserverNotInstalled = 5,
    FinishBroadPhaseObserverAllocationFailed = 6,
    FinishBroadPhaseObserverProtectFailed = 7,
    FinishBroadPhaseObserverPatchChanged = 8,
    FinishBroadPhaseObserverBusy = 9,
    FinishBroadPhaseObserverNotReady = 10,
    FinishBroadPhaseObserverStale = 11,
    FinishBroadPhaseObserverCapacityTooSmall = 12,
    FinishBroadPhaseObserverInvalidIdentity = 13,
    FinishBroadPhaseObserverInvalidMetadata = 14,
    FinishBroadPhaseObserverUnstable = 15,
    FinishBroadPhaseObserverCaptureFailed = 16
};

enum FinishBroadPhaseObserverState : uint32_t {
    FinishBroadPhaseObserverUninstalled = 0,
    FinishBroadPhaseObserverIdle = 1,
    FinishBroadPhaseObserverArmed = 2,
    FinishBroadPhaseObserverCapturing = 3,
    FinishBroadPhaseObserverCaptured = 4,
    FinishBroadPhaseObserverFailed = 5
};

enum IslandSnapshotResult : uint32_t {
    IslandSnapshotOk = 1,
    IslandSnapshotBadArgument = 2,
    IslandSnapshotRevisionMismatch = 3,
    IslandSnapshotInvalidIdentity = 4,
    IslandSnapshotInvalidMetadata = 5,
    IslandSnapshotCapacityTooSmall = 6,
    IslandSnapshotInvalidFreeChain = 7,
    IslandSnapshotInvalidBitmap = 8,
    IslandSnapshotInvalidQueue = 9,
    IslandSnapshotInvalidTopology = 10,
    IslandSnapshotInvalidBinding = 11,
    IslandSnapshotInvalidPhase = 12,
    IslandSnapshotUnstable = 13,
    IslandSnapshotJournalOverflow = 14
};

enum IslandObserverResult : uint32_t {
    IslandObserverOk = 1,
    IslandObserverBadArgument = 2,
    IslandObserverRevisionMismatch = 3,
    IslandObserverAlreadyInstalled = 4,
    IslandObserverNotInstalled = 5,
    IslandObserverAllocationFailed = 6,
    IslandObserverProtectFailed = 7,
    IslandObserverPatchChanged = 8,
    IslandObserverBusy = 9,
    IslandObserverNotReady = 10,
    IslandObserverStale = 11,
    IslandObserverCapacityTooSmall = 12,
    IslandObserverInvalidIdentity = 13,
    IslandObserverInvalidMetadata = 14,
    IslandObserverUnstable = 15,
    IslandObserverCaptureFailed = 16
};

enum IslandObserverState : uint32_t {
    IslandObserverDormant = 0,
    IslandObserverIdle = 1,
    IslandObserverArmed = 2,
    IslandObserverCapturing = 3,
    IslandObserverCaptured = 4,
    IslandObserverFailed = 5
};

enum IslandJournalResult : uint32_t {
    IslandJournalOk = 1,
    IslandJournalBadArgument = 2,
    IslandJournalNotInstalled = 3,
    IslandJournalNotReady = 4,
    IslandJournalStale = 5,
    IslandJournalCapacityTooSmall = 6,
    IslandJournalUnstable = 7,
    IslandJournalOverflow = 8
};

enum IslandSnapshotPhase : uint32_t {
    IslandSnapshotSettled = 1,
    IslandSnapshotPreUpdate = 2,
    IslandSnapshotPostUpdate = 3
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

static const uint32_t kApiVersion = 18;
static const uint32_t kMaximumShapePoses = 64;
static const uint32_t kMaximumContactManagers = 4096;
static const uint32_t kMaximumManifolds = 4096;
static const uint32_t kMaximumShapeInstancePairs = 4096;
static const uint32_t kMaximumContactRecreateRows = 32;
static const uint32_t kMaximumDirtyInteractions = 4096;
static const uint32_t kMaximumDirtyHashSize = 8192;
static const uint32_t kMaximumInteractionGraphActors = 4096;
static const uint32_t kMaximumInteractionGraphInteractions = 16384;
static const uint32_t kMaximumInteractionGraphActorSlots = 32768;
static const uint32_t kMaximumInteractionGraphPoolEntries = 65536;
static const uint32_t kMaximumTransformCacheIds = 16384;
static const uint32_t kMaximumTransformCacheBindings =
    kMaximumInteractionGraphInteractions * 2u;
static const uint32_t kMaximumBroadPhaseOverlaps = 4096;
static const uint32_t kFinishBroadPhaseRingCapacity = 4;
static const uint32_t kMaximumIslandNodes = 16384;
static const uint32_t kMaximumIslandEdges = 65536;
static const uint32_t kMaximumIslands = 16384;
static const uint32_t kMaximumIslandRoots = 16384;
static const uint32_t kMaximumIslandQueueEntries = 65536;
static const uint32_t kMaximumIslandBindings = 16384;
static const uint32_t kIslandJournalCapacity = 65536;
static const uint32_t kCleanupRva = 0x481ED0;
static const uint32_t kCreateRva = 0x482510;
static const uint32_t kGetShapesRva = 0xA10740;
static const uint32_t kCreateContactManagerRva = 0xA69E80;
static const uint32_t kInitContactManagerRva = 0xA7E5F0;
static const uint32_t kShapeInstancePairCreateManagerRva = 0xA54430;
static const uint32_t kCreateShapeInstancePairRva = 0xA4E560;
static const uint32_t kFindActorPairRva = 0xA4F7B0;
static const uint32_t kCreateActorPairReportDataRva = 0xA4E370;
static const uint32_t kActorPairReportSlabStrideRva = 0xA4DAC4;
static const uint32_t kReleaseActorPairReportDataRva = 0xA522A0;
static const uint32_t kAddPersistentContactEventPairRva = 0xA4CD90;
static const uint32_t kRemovePersistentContactEventPairRva = 0xA53840;
static const uint32_t kContactReportBufferAllocateRva = 0xA53950;
static const uint32_t kInteractionSceneCtorRva = 0xA40570;
static const uint32_t kInteractionSceneCtorPool16Rva = 0xA40623;
static const uint32_t kInteractionSceneCtorPool32Rva = 0xA40639;
static const uint32_t kInteractionSceneCtorTailRva = 0xA4064F;
static const uint32_t kInteractionActorCtorLayoutRva = 0xA3F74C;
static const uint32_t kInteractionActorReallocRva = 0xA3FA90;
static const uint32_t kInteractionActorRegisterRva = 0xA3FB40;
static const uint32_t kInteractionActorUnregisterRva = 0xA3FD50;
static const uint32_t kInteractionPointerAllocateRva = 0xA40E60;
static const uint32_t kInteractionPointerFreeRva = 0xA411D0;
static const uint32_t kInteractionActiveTestRva = 0xA41EC0;
static const uint32_t kInteractionActivateRva = 0xA41EE0;
static const uint32_t kInteractionDeactivateRva = 0xA41F40;
static const uint32_t kInteractionRegisterRva = 0xA420B0;
static const uint32_t kInteractionUnregisterRva = 0xA42950;
static const uint32_t kUpdateDirtyInteractionsRva = 0xA540F0;
static const uint32_t kFinishBroadPhaseRva = 0xA31CA0;
static const uint32_t kIslandAddEdgeRva = 0xA63940;
static const uint32_t kIslandRemoveEdgeRva = 0xA64490;
static const uint32_t kIslandPrivateUpdateRva = 0xA65430;
static const uint32_t kIslandUpdateRva = 0xA65530;
static const uint32_t kIslandSecondUpdateRva = 0xA655B0;
static const uint32_t kPxsContextUpdateIslandsRva = 0xA6C910;
static const uint32_t kIslandNodeVtableRva = 0xEFF2C0;
static const uint32_t kIslandEdgeVtableRva = 0xEFF2C8;
static const uint32_t kIslandManagerVtableRva = 0xEFF2D8;
static const uint32_t kIslandRootVtableRva = 0xEFF2E0;
static const uint32_t kCreateManagerTransformCacheLayoutRva = 0xA54530;
static const uint32_t kShapeSimCreateTransformCacheRva = 0xA473B0;
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
static const uint32_t kContactManagerSize = 0x80;
static const uint8_t kCleanupBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x74,0x53,0x8B,0xD9,0x56,0x57};
static const uint8_t kCreateBytes[] = {0x55,0x8B,0xEC,0x81,0xEC,0x90,0x00,0x00,0x00,0x53,0x8B,0xD9};
static const uint8_t kGetShapesBytes[] = {0x55,0x8B,0xEC,0x83,0xC1,0x14,0x5D,0xE9};
static const uint8_t kCreateContactManagerBytes[] = {0x55,0x8B,0xEC,0x53,0x8B,0xD9};
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
static const uint8_t kInteractionSceneCtorBytes[] = {
    0x55,0x8B,0xEC,0x51,0x56,0x8B,0xF1,0x8D,0x45,0xFF,
    0x68,0x00,0x04,0x00,0x00,0x6A,0x20,0x50,0xC7,0x06,
    0x00,0x00,0x00,0x00,0x8D,0x4E,0x70
};
static const uint8_t kInteractionSceneCtorPool16Bytes[] = {
    0x68,0x00,0x08,0x00,0x00,0x6A,0x20,0x8D,0x45,0xFF,0x50,
    0x8D,0x8E,0x98,0x01,0x00,0x00
};
static const uint8_t kInteractionSceneCtorPool32Bytes[] = {
    0x68,0x00,0x10,0x00,0x00,0x6A,0x20,0x8D,0x45,0xFF,0x50,
    0x8D,0x8E,0xC0,0x02,0x00,0x00
};
static const uint8_t kInteractionSceneCtorTailBytes[] = {
    0x8B,0x45,0x08,0x89,0x86,0xF0,0x03,0x00,0x00,0x8B,0xC6,
    0xC7,0x86,0xE8,0x03,0x00,0x00,0x00,0x00,0x00,0x00,
    0xC7,0x86,0xEC,0x03,0x00,0x00,0x00,0x00,0x00,0x00
};
static const uint8_t kInteractionActorCtorLayoutBytes[] = {
    0xC7,0x41,0x14,0x00,0x00,0x00,0x00,
    0xC7,0x41,0x18,0x00,0x00,0x00,0x00,
    0xC7,0x41,0x1C,0x00,0x00,0x00,0x00,0x89,0x41,0x24
};
static const uint8_t kInteractionActorReallocBytes[] = {
    0x55,0x8B,0xEC,0x51,0x53,0x8B,0xD1,0x8B,0x4D,0x14,
    0x89,0x55,0xFC,0x56,0x57,0x85,0xC9
};
static const uint8_t kInteractionActorRegisterBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x08,0x53,0x56,0x57,0x8B,0x7D,0x08,
    0x8B,0xF1,0x8B,0x47,0x04,0x0F,0xB6,0x4F,0x14
};
static const uint8_t kInteractionActorUnregisterBytes[] = {
    0x55,0x8B,0xEC,0x56,0x57,0x8B,0x7D,0x08,0x8B,0xF1,
    0x39,0x77,0x04,0x75,0x06,0x0F,0xB7,0x57,0x10
};
static const uint8_t kInteractionPointerAllocateBytes[] = {
    0x55,0x8B,0xEC,0x8B,0x45,0x08,0x56,0x83,0xF8,0x08,0x75,0x32,
    0x83,0xB9,0x94,0x01,0x00,0x00,0x00,0x8D,0x71,0x70
};
static const uint8_t kInteractionPointerFreeBytes[] = {
    0x55,0x8B,0xEC,0x8B,0x45,0x0C,0x83,0xF8,0x08,0x75,0x4A,
    0x56,0x8D,0x71,0x70,0x8B,0x4D,0x08
};
static const uint8_t kInteractionActiveTestBytes[] = {
    0x55,0x8B,0xEC,0x8B,0x45,0x08,0x0F,0xB6,0x50,0x14,
    0x8B,0x40,0x0C,0x3B,0x44,0x91,0x58
};
static const uint8_t kInteractionActivateBytes[] = {
    0x55,0x8B,0xEC,0x51,0x53,0x56,0x8B,0x75,0x08,0x57,
    0x8D,0x79,0x58,0x0F,0xB6,0x46,0x14
};
static const uint8_t kInteractionDeactivateBytes[] = {
    0x55,0x8B,0xEC,0x51,0x53,0x57,0x8B,0x7D,0x08,0x89,0x4D,0xFC,
    0x0F,0xB6,0x47,0x14
};
static const uint8_t kInteractionRegisterBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x08,0x56,0x57,0x8B,0x7D,0x08,
    0x8B,0xD1,0x89,0x55,0xF8,0x0F,0xB6,0x4F,0x14
};
static const uint8_t kInteractionUnregisterBytes[] = {
    0x55,0x8B,0xEC,0x51,0x8B,0x55,0x08,0x53,0x56,0x57,
    0x0F,0xB6,0x7A,0x14,0x8B,0x5A,0x0C
};
static const uint8_t kCreateContactManagerPoolBytes[] = {
    0x83,0xBB,0xCC,0x02,0x00,0x00,0x00,0x56,0x8D,0xB3,0xB8,0x02,0x00,0x00
};
static const uint8_t kCreateContactManagerPopBytes[] = {
    0xFF,0x4E,0x14,0x8B,0x4E,0x14,0x8B,0x46,0x10,0x57,0xFF,0x75,
    0x0C,0x8B,0x3C,0x88,0x8B,0x46,0x20,0xFF,0x75,0x08,0x8B,0x57,
    0x4C,0x8B,0xCA,0xC1,0xE9,0x05,0x83,0xE2,0x1F,0x8D,0x0C,0x88,
    0x8B,0x01,0x0F,0xAB,0xD0,0x89,0x01,0x8B
};
static const uint8_t kInitContactManagerBytes[] = {
    0x55,0x8B,0xEC,0x56,0x8B,0x75,0x08,0x8B,0x46,0x0C,0x89,0x01
};
static const uint8_t kInitContactManagerUserDataBytes[] = {
    0x8B,0x06,0x89,0x41,0x0C,0x33,0xC0,0x66,0x89,0x41,0x72,
    0x66,0x89,0x41,0x24
};
static const uint8_t kCreateSipBytes[] = {
    0x55,0x8B,0xEC,0x81,0xEC,0x94,0x00,0x00,0x00,0x53,0x8B,0xD9,
    0x56,0x89,0x5D,0xC8,0x8B,0x4B,0x20,0xE8,0xA8,0x3A,0xFF,0xFF,
    0x8B,0x73,0x20,0x89,0x45,0xEC,0x8B,0x43,0x24,0x89,0x45,0xFC,
    0x8B
};
static const uint8_t kCreateSipShapeCoreBytes[] = {
    0x8B,0x46,0x1C,0x83,0xC0,0x20,0x89,0x45,0x80,0x8B,0x43,0x1C,
    0x83,0xC0,0x20,0x89,0x7D,0xB4,0x83
};
static const uint8_t kNpShapeGetTypeBytes[] = {0x8B,0x41,0x74,0xC3};
static const uint8_t kUpdateDirtyInteractionsBytes[] = {0x55,0x8B,0xEC,0x83,0xEC,0x34};
static const uint8_t kFinishBroadPhaseBytes[] = {
    0x55,0x8B,0xEC,0x51,0x53,0x8B,0xD9,0x56,0x57
};
static const uint8_t kFinishBroadPhaseLayoutBytes[] = {
    0x8B,0x83,0xB4,0x04,0x00,0x00,0x8B,0x8B,0x50,0x04,0x00,0x00,
    0x8B,0x80,0xE8,0x03,0x00,0x00,0x8B,0x70,0x08
};
static const uint8_t kIslandAddEdgeBytes[] = {
    0x55,0x8B,0xEC,0x53,0x8B,0xD9,0x83,0xBB,
    0x28,0x01,0x00,0x00,0xFF,0x56,0x8D,0xB3
};
static const uint8_t kIslandRemoveEdgeBytes[] = {
    0x55,0x8B,0xEC,0x53,0x8B,0x5D,0x0C,0x56,
    0x57,0x8B,0xF9,0x8B,0x03,0x8D,0xB7
};
static const uint8_t kIslandPrivateUpdateBytes[] = {
    0x53,0x8B,0xD9,0x56,0x57,0xFF,0x73,0x08,
    0x8D,0x83,0x78,0x02,0x00,0x00,0x50
};
static const uint8_t kIslandUpdateBytes[] = {
    0x53,0x56,0x57,0x8B,0xF1,0xE8,0xE6,0xE4,
    0xFF,0xFF,0x8B,0xCE,0xE8,0x9F,0xF6,0xFF,0xFF
};
static const uint8_t kIslandSecondUpdateBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x08,0x53,0x56,
    0x8B,0xF1,0x57,0x89,0x75,0xF8
};
static const uint8_t kPxsContextUpdateIslandsBytes[] = {
    0x55,0x8B,0xEC,0x6A,0x00,0xFF,0x75,0x0C,
    0x81,0xC1,0x1C,0x18,0x00,0x00,0xE8
};
static const uint8_t kCreateManagerTransformCacheLayoutBytes[] = {
    0x8B,0x86,0xB4,0x04,0x00,0x00,0x8B,0x4D,0xF8,
    0x8B,0xB0,0xE8,0x03,0x00,0x00,0x81,0xC6,0xBC,0x1D,0x00,0x00,
    0x56
};
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
static void* g_shapeInstancePairTrampoline = 0;
static uint8_t g_shapeInstancePairOriginal[sizeof(kCreateShapeInstancePairBytes)] = {};
static bool g_contactManagerContextObserverInstalled = false;
static volatile LONG g_contactRecreateState = ContactRecreateIdle;
static volatile LONG g_contactRecreateResult = ContactRecreateOk;
static volatile LONG g_contactRecreateError = ERROR_SUCCESS;
static uintptr_t g_contactRecreateUnityBase = 0;
static uintptr_t g_contactRecreateContext = 0;
static uintptr_t g_contactRecreateFreeArray = 0;
static uintptr_t g_contactRecreateLargePool = 0;
static uint32_t g_contactRecreateRowCount = 0;
static uint32_t g_contactRecreateMatchedCount = 0;
static uint32_t g_contactRecreateMatchedMask = 0;
static uint32_t g_contactRecreateThreadId = 0;
static volatile LONG g_contactRecreateHookLock = 0;
static volatile LONG g_sipRecreateHookLock = 0;
static volatile LONG g_contactRecreateObserverEntries = 0;
static volatile LONG g_sipRecreateObserverEntries = 0;
static uint32_t g_contactRecreateContactCountBefore = 0;
static uint32_t g_contactRecreateTargetContactCount = 0;
static uint32_t g_contactRecreateContactHashBefore = 0;
static uint32_t g_contactRecreateTargetContactHash = 0;
static uint32_t g_contactRecreateLargeCountBefore = 0;
static uint32_t g_contactRecreateTargetLargeCount = 0;
static uint32_t g_contactRecreateLargeHashBefore = 0;
static uint32_t g_contactRecreateTargetLargeHash = 0;
static uint32_t g_contactRecreateLargeUsedBefore = 0;
static uint32_t g_contactRecreateTargetLargeUsed = 0;
static uint32_t g_contactRecreateLargeUnreleasedBefore = 0;
static uint32_t g_contactRecreateTargetLargeUnreleased = 0;
static uintptr_t g_contactRecreateLastSip = 0;
static uintptr_t g_contactRecreateLastShapeLow = 0;
static uintptr_t g_contactRecreateLastShapeHigh = 0;
static uintptr_t g_contactRecreateLastManager = 0;
static uintptr_t g_contactRecreateLastManifold = 0;
static uint32_t g_contactRecreateInvalidRow = 0xFFFFFFFFu;
static uint32_t g_contactRecreateDetail = 0;
static uint32_t g_contactRecreateAttemptOrdinal = 0;
static uintptr_t g_contactRecreateAttemptSip = 0;
static uintptr_t g_contactRecreateAttemptShapeLow = 0;
static uintptr_t g_contactRecreateAttemptShapeHigh = 0;
static uint32_t g_contactRecreateAttemptRow = 0xFFFFFFFFu;
static uint32_t g_contactRecreateAttemptMatchedMask = 0;
static uint32_t g_contactRecreateAttemptFreeCount = 0xFFFFFFFFu;
static uint32_t g_contactRecreateAttemptManagerIndex = 0xFFFFFFFFu;
static uintptr_t g_contactRecreateAttemptLargeHead = 0;
static uint32_t g_contactRecreateAttemptManifoldIndex = 0xFFFFFFFFu;
static uintptr_t g_contactRecreateNPhaseCore = 0;
static uintptr_t g_contactRecreateSipPool = 0;
static uint32_t g_contactRecreateSipMatchedCount = 0;
static uint32_t g_contactRecreateSipMatchedMask = 0;
static uint32_t g_contactRecreateTargetSipCount = 0;
static uint32_t g_contactRecreateTargetSipHash = 0;
static uint32_t g_contactRecreateTargetSipUsed = 0;
static uint32_t g_contactRecreateTargetSipUnreleased = 0;
static uintptr_t g_contactRecreateLastAllocatedSip = 0;
static uint32_t g_sipRecreateAttemptRow = 0xFFFFFFFFu;
static ContactRecreatePlanRow g_contactRecreateRows[kMaximumContactRecreateRows] = {};
static uintptr_t g_contactRecreateTargetContact[kMaximumContactManagers] = {};
static uintptr_t g_contactRecreateTargetLarge[kMaximumManifolds] = {};
static uintptr_t g_contactRecreateScratchContact[kMaximumContactManagers] = {};
static uintptr_t g_contactRecreateScratchLarge[kMaximumManifolds] = {};
static uintptr_t g_contactRecreatePrimedLarge[kMaximumManifolds] = {};
static uintptr_t g_contactRecreateTargetSip[kMaximumShapeInstancePairs] = {};
static uintptr_t g_contactRecreateScratchSip[kMaximumShapeInstancePairs] = {};
static uintptr_t g_contactRecreatePrimedSip[kMaximumShapeInstancePairs] = {};
static uintptr_t g_dirtyInteractionUnityBase = 0;
static void* g_dirtyInteractionTrampoline = 0;
static uint8_t g_dirtyInteractionOriginal[sizeof(kUpdateDirtyInteractionsBytes)] = {};
static volatile LONG g_dirtyInteractionAction = DirtyInteractionOrderIdle;
static volatile LONG g_dirtyInteractionResult = DirtyInteractionOrderNotCaptured;
static volatile LONG g_dirtyInteractionError = ERROR_INVALID_STATE;
static volatile LONG g_dirtyInteractionCaptures = 0;
static volatile LONG g_dirtyInteractionRestores = 0;
static bool g_dirtyInteractionInstalled = false;
static uintptr_t g_lastObservedNPhaseCore = 0;
static volatile LONG g_dirtyNPhaseObservations = 0;
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

struct FinishBroadPhaseCaptureSlot {
    volatile LONG committedOrdinal;
    FinishBroadPhaseObserverReceipt receipt;
    BroadPhaseOverlapRecord created[kMaximumBroadPhaseOverlaps];
    BroadPhaseOverlapRecord deleted[kMaximumBroadPhaseOverlaps];
};

static uintptr_t g_finishBroadPhaseUnityBase = 0;
static void* g_finishBroadPhaseTrampoline = 0;
static uint8_t g_finishBroadPhaseOriginal[sizeof(kFinishBroadPhaseBytes)] = {};
static const uint8_t g_finishBroadPhaseModuleMarker = 0;
static volatile LONG g_finishBroadPhaseModulePinned = 0;
// 0 = uninstalled, -1 = lifecycle transition, 1 = installed.
static volatile LONG g_finishBroadPhaseInstalled = 0;
static volatile LONG g_finishBroadPhaseInFlight = 0;
static volatile LONG g_finishBroadPhaseState =
    FinishBroadPhaseObserverUninstalled;
static volatile LONG g_finishBroadPhaseOrdinal = 0;
static volatile LONG g_finishBroadPhaseDropped = 0;
static uintptr_t g_finishBroadPhaseExpectedScene = 0;
static uintptr_t g_finishBroadPhaseExpectedContext = 0;
static uintptr_t g_finishBroadPhaseExpectedNPhaseCore = 0;
static uint32_t g_finishBroadPhaseExpectedPass = 0;
static uint32_t g_finishBroadPhaseArmedThreadId = 0;
static uint32_t g_finishBroadPhaseArmedOrdinal = 0;
static FinishBroadPhaseCaptureSlot
    g_finishBroadPhaseSlots[kFinishBroadPhaseRingCapacity] = {};

struct IslandJournalSlot {
    volatile LONG committedOrdinal;
    IslandEdgeJournalRecord record;
};

static uintptr_t g_islandUnityBase = 0;
static uintptr_t g_islandExpectedManager = 0;
static uintptr_t g_islandExpectedContext = 0;
static uintptr_t g_islandExpectedNphase = 0;
static void* g_islandAddEdgeTrampoline = 0;
static void* g_islandRemoveEdgeTrampoline = 0;
static void* g_islandUpdateTrampoline = 0;
static uint8_t g_islandAddEdgeOriginal[6] = {};
static uint8_t g_islandRemoveEdgeOriginal[7] = {};
static uint8_t g_islandUpdateOriginal[5] = {};
static const uint8_t g_islandModuleMarker = 0;
static volatile LONG g_islandModulePinned = 0;
// 0 means no resident detour, -1 lifecycle transition, 1 resident detours.
static volatile LONG g_islandInstalled = 0;
static volatile LONG g_islandState = IslandObserverDormant;
static volatile LONG g_islandInFlight = 0;
static volatile LONG g_islandEpoch = 0;
static volatile LONG g_islandCapturePhase = IslandSnapshotSettled;
static volatile LONG g_islandObserverSequence = 0;
static volatile LONG g_islandNextObservationOrdinal = 0;
static volatile LONG g_islandCommittedObservationOrdinal = 0;
static volatile LONG g_islandDeferredDormant = 0;
static volatile LONG g_islandJournalNextOrdinal = 0;
static volatile LONG g_islandJournalOverflow = 0;
static uint32_t g_islandExpectedPass = 0;
static uint32_t g_islandArmedThreadId = 0;
static uint32_t g_islandArmedOrdinal = 0;
static uint32_t g_islandArmJournalBegin = 1;
static IslandSnapshotBuffersV1 g_islandArmedPreBuffers = {};
static IslandSnapshotBuffersV1 g_islandArmedPostBuffers = {};
static IslandSnapshotReceiptV1* g_islandArmedPreReceipt = 0;
static IslandSnapshotReceiptV1* g_islandArmedPostReceipt = 0;
static IslandUpdateObserverReceipt g_islandCommittedReceipt = {};
static IslandJournalSlot g_islandJournal[kIslandJournalCapacity] = {};

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

static int FailContactManagerOwner(ContactManagerOwnerReceipt* receipt,
    ContactManagerOwnerResult result, uint32_t error, uint32_t slot,
    uint32_t detail) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
        receipt->invalidSlot = slot;
        receipt->detail = detail;
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

static int FailContactRecreate(ContactRecreateReceipt* receipt,
    ContactRecreateResult result, uint32_t error, uint32_t invalidRow,
    uint32_t detail) {
    InterlockedExchange(&g_contactRecreateResult, result);
    InterlockedExchange(&g_contactRecreateError, static_cast<LONG>(error));
    InterlockedExchange(&g_contactRecreateState, ContactRecreatePoisoned);
    g_contactRecreateInvalidRow = invalidRow;
    g_contactRecreateDetail = detail;
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
        receipt->state = ContactRecreatePoisoned;
        receipt->invalidRow = invalidRow;
        receipt->detail = detail;
        receipt->armed = 0;
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

static int FailSipPool(SipPoolReceipt* receipt, SipPoolResult result,
    uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailActorPairPool(ActorPairPoolReceipt* receipt,
    SipPoolResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailActorPairReportPool(ActorPairReportPoolReceipt* receipt,
    SipPoolResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailNPhaseReportState(NPhaseReportStateReceipt* receipt,
    SipPoolResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

static int FailTransformCache(TransformCacheReceipt* receipt,
    TransformCacheResult result, uint32_t error, uint32_t kind,
    uint32_t index, uint32_t detail) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
        receipt->invalidKind = kind;
        receipt->invalidIndex = index;
        receipt->detail = detail;
    }
    return 0;
}

static int FailFinishBroadPhaseObserver(
    FinishBroadPhaseObserverReceipt* receipt,
    FinishBroadPhaseObserverResult result, uint32_t error,
    uint32_t kind, uint32_t index, uint32_t detail) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
        receipt->invalidKind = kind;
        receipt->invalidIndex = index;
        receipt->detail = detail;
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

static void InitializeContactManagerOwnerReceipt(
    ContactManagerOwnerReceipt* receipt, uintptr_t unityBase,
    uintptr_t context) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(ContactManagerOwnerReceipt);
    receipt->unityBase = unityBase;
    receipt->context = context;
    receipt->invalidSlot = 0xFFFFFFFFu;
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

static void InitializeContactRecreateReceipt(ContactRecreateReceipt* receipt,
    uintptr_t unityBase, uintptr_t context) {
    *receipt = {};
    receipt->apiVersion = 2;
    receipt->structSize = sizeof(ContactRecreateReceipt);
    receipt->result = static_cast<uint32_t>(g_contactRecreateResult);
    receipt->lastError = static_cast<uint32_t>(g_contactRecreateError);
    receipt->unityBase = unityBase;
    receipt->context = context;
    receipt->freeArray = g_contactRecreateFreeArray;
    receipt->largePool = g_contactRecreateLargePool;
    receipt->state = static_cast<uint32_t>(g_contactRecreateState);
    receipt->rowCount = g_contactRecreateRowCount;
    receipt->matchedCount = g_contactRecreateMatchedCount;
    receipt->remainingCount = g_contactRecreateRowCount -
        g_contactRecreateMatchedCount;
    receipt->contactCountBefore = g_contactRecreateContactCountBefore;
    receipt->targetContactCount = g_contactRecreateTargetContactCount;
    receipt->contactHashBefore = g_contactRecreateContactHashBefore;
    receipt->targetContactHash = g_contactRecreateTargetContactHash;
    receipt->largeCountBefore = g_contactRecreateLargeCountBefore;
    receipt->targetLargeCount = g_contactRecreateTargetLargeCount;
    receipt->largeHashBefore = g_contactRecreateLargeHashBefore;
    receipt->targetLargeHash = g_contactRecreateTargetLargeHash;
    receipt->largeUsedBefore = g_contactRecreateLargeUsedBefore;
    receipt->targetLargeUsed = g_contactRecreateTargetLargeUsed;
    receipt->largeUnreleasedBefore =
        g_contactRecreateLargeUnreleasedBefore;
    receipt->targetLargeUnreleased =
        g_contactRecreateTargetLargeUnreleased;
    receipt->matchedMask = g_contactRecreateMatchedMask;
    receipt->threadId = g_contactRecreateThreadId;
    receipt->lastSip = g_contactRecreateLastSip;
    receipt->lastShapeLow = g_contactRecreateLastShapeLow;
    receipt->lastShapeHigh = g_contactRecreateLastShapeHigh;
    receipt->lastManager = g_contactRecreateLastManager;
    receipt->lastManifold = g_contactRecreateLastManifold;
    receipt->invalidRow = g_contactRecreateInvalidRow;
    receipt->detail = g_contactRecreateDetail;
    receipt->installed = g_contactManagerContextObserverInstalled ? 1u : 0u;
    receipt->armed = g_contactRecreateState == ContactRecreateArmed ? 1u : 0u;
    receipt->observerEntries = static_cast<uint32_t>(
        g_contactRecreateObserverEntries);
    receipt->attemptOrdinal = g_contactRecreateAttemptOrdinal;
    receipt->attemptSip = g_contactRecreateAttemptSip;
    receipt->attemptShapeLow = g_contactRecreateAttemptShapeLow;
    receipt->attemptShapeHigh = g_contactRecreateAttemptShapeHigh;
    receipt->attemptRow = g_contactRecreateAttemptRow;
    receipt->attemptMatchedMask = g_contactRecreateAttemptMatchedMask;
    receipt->attemptFreeCount = g_contactRecreateAttemptFreeCount;
    receipt->attemptManagerIndex = g_contactRecreateAttemptManagerIndex;
    receipt->attemptLargeHead = g_contactRecreateAttemptLargeHead;
    receipt->attemptManifoldIndex = g_contactRecreateAttemptManifoldIndex;
    receipt->nphaseCore = g_contactRecreateNPhaseCore;
    receipt->sipPool = g_contactRecreateSipPool;
    receipt->sipMatchedCount = g_contactRecreateSipMatchedCount;
    receipt->sipRemainingCount = g_contactRecreateRowCount -
        g_contactRecreateSipMatchedCount;
    receipt->targetSipCount = g_contactRecreateTargetSipCount;
    receipt->targetSipHash = g_contactRecreateTargetSipHash;
    receipt->targetSipUsed = g_contactRecreateTargetSipUsed;
    receipt->targetSipUnreleased = g_contactRecreateTargetSipUnreleased;
    receipt->sipMatchedMask = g_contactRecreateSipMatchedMask;
    receipt->sipObserverEntries = static_cast<uint32_t>(
        g_sipRecreateObserverEntries);
    receipt->lastAllocatedSip = g_contactRecreateLastAllocatedSip;
    const uintptr_t sipPool = g_contactRecreateSipPool;
    if (sipPool && Readable(reinterpret_cast<const void*>(sipPool + 0x118),
            0x10)) {
        receipt->sipUsedCurrent = *reinterpret_cast<const uint32_t*>(
            sipPool + 0x118);
        receipt->sipUnreleasedCurrent = *reinterpret_cast<const uint32_t*>(
            sipPool + 0x11C);
        uintptr_t current = *reinterpret_cast<const uintptr_t*>(
            sipPool + 0x124);
        uint32_t hash = 2166136261u;
        while (current && receipt->sipCountCurrent <
                kMaximumShapeInstancePairs) {
            hash ^= static_cast<uint32_t>(current);
            hash *= 16777619u;
            ++receipt->sipCountCurrent;
            if (!Readable(reinterpret_cast<const void*>(current),
                    sizeof(uintptr_t))) {
                receipt->sipCountCurrent = 0;
                hash = 0;
                break;
            }
            current = *reinterpret_cast<const uintptr_t*>(current);
        }
        if (current) {
            receipt->sipCountCurrent = 0;
            hash = 0;
        }
        receipt->sipHashCurrent = hash;
    }
}

static void InitializeSipPoolReceipt(SipPoolReceipt* receipt,
    uintptr_t unityBase, uintptr_t nphaseCore) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(SipPoolReceipt);
    receipt->unityBase = unityBase;
    receipt->nphaseCore = nphaseCore;
    receipt->pool = nphaseCore ? nphaseCore + 0x2E0 : 0;
    receipt->elementSize = 0x44;
}

static void InitializeActorPairPoolReceipt(ActorPairPoolReceipt* receipt,
    uintptr_t unityBase, uintptr_t nphaseCore) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(ActorPairPoolReceipt);
    receipt->unityBase = unityBase;
    receipt->nphaseCore = nphaseCore;
    receipt->pool = nphaseCore ? nphaseCore + 0x90 : 0;
    receipt->elementSize = 0x18;
}

static void InitializeActorPairReportPoolReceipt(
    ActorPairReportPoolReceipt* receipt, uintptr_t unityBase,
    uintptr_t nphaseCore) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(ActorPairReportPoolReceipt);
    receipt->unityBase = unityBase;
    receipt->nphaseCore = nphaseCore;
    receipt->pool = nphaseCore ? nphaseCore + 0x530 : 0;
    receipt->elementSize = 0x24;
}

static void InitializeNPhaseReportStateReceipt(
    NPhaseReportStateReceipt* receipt, uintptr_t unityBase,
    uintptr_t nphaseCore) {
    *receipt = {};
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(NPhaseReportStateReceipt);
    receipt->unityBase = unityBase;
    receipt->nphaseCore = nphaseCore;
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

static bool ReadContactRecreateBitmapBit(uintptr_t bitmap, uint32_t slot,
    bool& value) {
    value = false;
    if (!Readable(reinterpret_cast<const void*>(bitmap), 8)) return false;
    const uintptr_t map = *reinterpret_cast<const uintptr_t*>(bitmap);
    const uint32_t wordCount = *reinterpret_cast<const uint32_t*>(bitmap + 4) &
        0x7FFFFFFFu;
    const uint32_t word = slot >> 5;
    if (!map || word >= wordCount || wordCount > 0x20000u ||
        !Readable(reinterpret_cast<const void*>(map + word * 4), 4))
        return false;
    value = (*reinterpret_cast<const uint32_t*>(map + word * 4) &
        (1u << (slot & 31u))) != 0;
    return true;
}

static bool ContactRecreateManifoldFreeIndex(uintptr_t pool,
    uintptr_t target, uint32_t& index) {
    index = 0xFFFFFFFFu;
    if (!Readable(reinterpret_cast<const void*>(pool + 0x124),
            sizeof(uintptr_t))) return false;
    uintptr_t current = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    for (uint32_t i = 0; current && i < kMaximumManifolds; ++i) {
        if (current == target) {
            index = i;
            return true;
        }
        if (!Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t))) return false;
        current = *reinterpret_cast<const uintptr_t*>(current);
    }
    return current == 0;
}

static bool ContactRecreateSipFreeIndex(uintptr_t target, uint32_t& index) {
    index = 0xFFFFFFFFu;
    const uintptr_t pool = g_contactRecreateSipPool;
    if (!pool || !Readable(reinterpret_cast<const void*>(pool + 0x124),
            sizeof(uintptr_t))) return false;
    uintptr_t current = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    for (uint32_t i = 0; current && i < kMaximumShapeInstancePairs; ++i) {
        if (current == target) {
            index = i;
            return true;
        }
        if (!Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t))) return false;
        current = *reinterpret_cast<const uintptr_t*>(current);
    }
    return current == 0;
}

static void RefreshContactRecreateCompletion() {
    if (g_contactRecreateMatchedCount == g_contactRecreateRowCount &&
        g_contactRecreateSipMatchedCount == g_contactRecreateRowCount)
        InterlockedExchange(&g_contactRecreateState,
            ContactRecreateComplete);
    else if (g_contactRecreateState == ContactRecreateComplete)
        InterlockedExchange(&g_contactRecreateState, ContactRecreateArmed);
}

static bool ReconcileReturnedSipRecreateRows() {
    const uintptr_t pool = g_contactRecreateSipPool;
    if (!pool || pool != g_contactRecreateNPhaseCore + 0x2E0 ||
        !Readable(reinterpret_cast<const void*>(pool + 0x118), 0x10))
        return false;
    for (uint32_t rowIndex = 0; rowIndex < g_contactRecreateRowCount;
        ++rowIndex) {
        const uint32_t bit = 1u << rowIndex;
        if ((g_contactRecreateSipMatchedMask & bit) == 0) continue;
        uint32_t index = 0xFFFFFFFFu;
        if (!ContactRecreateSipFreeIndex(
                g_contactRecreateRows[rowIndex].targetSip, index))
            return false;
        if (index == 0xFFFFFFFFu) continue;
        g_contactRecreateSipMatchedMask &= ~bit;
        --g_contactRecreateSipMatchedCount;
    }
    RefreshContactRecreateCompletion();
    return true;
}

static bool ReconcileReturnedContactRecreateRows(uintptr_t context) {
    if (!Readable(reinterpret_cast<const void*>(context + 0x2C8), 8) ||
        *reinterpret_cast<const uintptr_t*>(context + 0x2C8) !=
            g_contactRecreateFreeArray)
        return false;
    const uint32_t freeCount = *reinterpret_cast<const uint32_t*>(
        context + 0x2CC);
    if (freeCount > kMaximumContactManagers ||
        !Readable(reinterpret_cast<const void*>(g_contactRecreateFreeArray),
            freeCount * sizeof(uintptr_t))) return false;
    const uintptr_t* freeArray = reinterpret_cast<const uintptr_t*>(
        g_contactRecreateFreeArray);
    for (uint32_t rowIndex = 0; rowIndex < g_contactRecreateRowCount;
        ++rowIndex) {
        const uint32_t bit = 1u << rowIndex;
        if ((g_contactRecreateMatchedMask & bit) == 0) continue;
        const ContactRecreatePlanRow& row = g_contactRecreateRows[rowIndex];
        bool managerFree = false;
        for (uint32_t i = 0; i < freeCount; ++i)
            if (freeArray[i] == row.targetManager) {
                managerFree = true;
                break;
            }
        uint32_t manifoldIndex = 0xFFFFFFFFu;
        if (!ContactRecreateManifoldFreeIndex(g_contactRecreateLargePool,
                row.targetManifold, manifoldIndex)) return false;
        const bool manifoldFree = manifoldIndex != 0xFFFFFFFFu;
        if (managerFree != manifoldFree) return false;
        if (!managerFree) continue;
        bool use = false, active = false, touch = false, modifiable = false;
        if (!ReadContactRecreateBitmapBit(context + 0x2B8 + 0x20,
                row.targetSlot, use) ||
            !ReadContactRecreateBitmapBit(context + 0x534,
                row.targetSlot, active) ||
            !ReadContactRecreateBitmapBit(context + 0x540,
                row.targetSlot, touch) ||
            !ReadContactRecreateBitmapBit(context + 0x16D0,
                row.targetSlot, modifiable) || use || active || touch ||
            modifiable) return false;
        g_contactRecreateMatchedMask &= ~bit;
        --g_contactRecreateMatchedCount;
    }
    RefreshContactRecreateCompletion();
    return true;
}

static bool CountUsedContactManagersForPair(uintptr_t context,
    uintptr_t shapeLow, uintptr_t shapeHigh, uint32_t& count) {
    count = 0;
    const uintptr_t pool = context + 0x2B8;
    if (!Readable(reinterpret_cast<const void*>(pool), 0x28)) return false;
    const uint32_t elementsPerSlab = *reinterpret_cast<const uint32_t*>(
        pool + 0x00);
    const uint32_t slabCount = *reinterpret_cast<const uint32_t*>(pool + 0x08);
    const uintptr_t slabs = *reinterpret_cast<const uintptr_t*>(pool + 0x18);
    const uintptr_t useMap = *reinterpret_cast<const uintptr_t*>(pool + 0x20);
    const uint32_t useWords = *reinterpret_cast<const uint32_t*>(pool + 0x24) &
        0x7FFFFFFFu;
    if (elementsPerSlab != 256u || !slabCount ||
        slabCount > kMaximumContactManagers / elementsPerSlab || !slabs ||
        !useMap || useWords < ((slabCount * elementsPerSlab + 31u) >> 5) ||
        !Readable(reinterpret_cast<const void*>(slabs),
            slabCount * sizeof(uintptr_t)) ||
        !Readable(reinterpret_cast<const void*>(useMap),
            ((slabCount * elementsPerSlab + 31u) >> 5) * 4)) return false;
    const uint32_t totalSlots = slabCount * elementsPerSlab;
    for (uint32_t slot = 0; slot < totalSlots; ++slot) {
        if ((*reinterpret_cast<const uint32_t*>(useMap +
                (slot >> 5) * 4) & (1u << (slot & 31u))) == 0) continue;
        const uintptr_t slab = reinterpret_cast<const uintptr_t*>(slabs)[
            slot >> 8];
        const uintptr_t manager = slab + (slot & 0xFFu) * kContactManagerSize;
        if (!slab || !Readable(reinterpret_cast<const void*>(manager + 0x5C),
                sizeof(uintptr_t))) return false;
        const uintptr_t shape0 = *reinterpret_cast<const uintptr_t*>(
            manager + 0x58);
        const uintptr_t shape1 = *reinterpret_cast<const uintptr_t*>(
            manager + 0x5C);
        const uintptr_t low = shape0 < shape1 ? shape0 : shape1;
        const uintptr_t high = shape0 < shape1 ? shape1 : shape0;
        if (low == shapeLow && high == shapeHigh) ++count;
    }
    return true;
}

static void ReleaseContactRecreateHookLock() {
    MemoryBarrier();
    InterlockedExchange(&g_contactRecreateHookLock, 0);
}

static void ReleaseSipRecreateHookLock() {
    MemoryBarrier();
    InterlockedExchange(&g_sipRecreateHookLock, 0);
}

static bool ReadShapeSimPxsShapeCore(uintptr_t shapeSim,
    uintptr_t& pxsShapeCore) {
    pxsShapeCore = 0;
    if (!shapeSim || !Readable(reinterpret_cast<const void*>(shapeSim + 0x1C),
            sizeof(uintptr_t))) return false;
    const uintptr_t shapeCore = *reinterpret_cast<const uintptr_t*>(
        shapeSim + 0x1C);
    if (!shapeCore || !Readable(reinterpret_cast<const void*>(shapeCore + 0x20),
            sizeof(uintptr_t))) return false;
    pxsShapeCore = shapeCore + 0x20;
    return true;
}

static int __cdecl ObserveAndPrepareShapeInstancePair(uintptr_t nphaseCore,
    uintptr_t shapeSim0, uintptr_t shapeSim1) {
    LONG state = g_contactRecreateState;
    if (state != ContactRecreateArmed && state != ContactRecreateComplete &&
        InterlockedCompareExchange(&g_sipRecreateHookLock, 0, 0) == 0)
        return 0;
    while (InterlockedCompareExchange(&g_sipRecreateHookLock, 1, 0) != 0)
        SwitchToThread();
    state = g_contactRecreateState;
    if (state != ContactRecreateArmed && state != ContactRecreateComplete) {
        ReleaseSipRecreateHookLock();
        return 0;
    }
    InterlockedIncrement(&g_sipRecreateObserverEntries);
    g_sipRecreateAttemptRow = 0xFFFFFFFFu;
    if (nphaseCore != g_contactRecreateNPhaseCore ||
        nphaseCore + 0x2E0 != g_contactRecreateSipPool) {
        FailContactRecreate(0, ContactRecreateContextChanged,
            ERROR_INVALID_STATE, 0xFFFFFFFFu,
            static_cast<uint32_t>(nphaseCore));
        return 1;
    }
    uintptr_t shape0 = 0, shape1 = 0;
    if (!ReadShapeSimPxsShapeCore(shapeSim0, shape0) ||
        !ReadShapeSimPxsShapeCore(shapeSim1, shape1) || shape0 == shape1) {
        FailContactRecreate(0, ContactRecreateBadArgument, ERROR_NOACCESS,
            0xFFFFFFFFu, 25);
        return 1;
    }
    const uintptr_t shapeLow = shape0 < shape1 ? shape0 : shape1;
    const uintptr_t shapeHigh = shape0 < shape1 ? shape1 : shape0;
    if (!ReconcileReturnedSipRecreateRows()) {
        FailContactRecreate(0, ContactRecreateMetadataMismatch,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 26);
        return 1;
    }
    uint32_t rowIndex = 0xFFFFFFFFu;
    for (uint32_t i = 0; i < g_contactRecreateRowCount; ++i) {
        if (g_contactRecreateRows[i].pxsShapeCoreLow == shapeLow &&
            g_contactRecreateRows[i].pxsShapeCoreHigh == shapeHigh) {
            rowIndex = i;
            break;
        }
    }
    g_sipRecreateAttemptRow = rowIndex;
    if (rowIndex == 0xFFFFFFFFu) return 1;
    const uint32_t bit = 1u << rowIndex;
    if ((g_contactRecreateSipMatchedMask & bit) != 0) {
        FailContactRecreate(0, ContactRecreateDuplicatePair,
            ERROR_ALREADY_EXISTS, rowIndex, 27);
        return 1;
    }
    const ContactRecreatePlanRow& row = g_contactRecreateRows[rowIndex];
    const uintptr_t pool = g_contactRecreateSipPool;
    const uint32_t remaining = g_contactRecreateRowCount -
        g_contactRecreateSipMatchedCount;
    const uint32_t used = *reinterpret_cast<const uint32_t*>(pool + 0x118);
    const uint32_t unreleased = *reinterpret_cast<const uint32_t*>(
        pool + 0x11C);
    if (unreleased != g_contactRecreateTargetSipCount + remaining ||
        used + remaining != g_contactRecreateTargetSipUsed ||
        unreleased != g_contactRecreateTargetSipUnreleased + remaining) {
        FailContactRecreate(0, ContactRecreateSipUnavailable,
            ERROR_INVALID_STATE, rowIndex, 28);
        return 1;
    }
    uintptr_t head = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    uintptr_t previous = 0;
    uintptr_t current = head;
    uint32_t traversed = 0;
    while (current && current != row.targetSip && traversed < unreleased) {
        if (!Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t))) break;
        previous = current;
        current = *reinterpret_cast<const uintptr_t*>(current);
        ++traversed;
    }
    if (current != row.targetSip || traversed >= unreleased ||
        !Readable(reinterpret_cast<const void*>(current), sizeof(uintptr_t)) ||
        !Writable(reinterpret_cast<void*>(pool + 0x124), sizeof(uintptr_t)) ||
        !Writable(reinterpret_cast<void*>(current), sizeof(uintptr_t)) ||
        (previous && !Writable(reinterpret_cast<void*>(previous),
            sizeof(uintptr_t)))) {
        FailContactRecreate(0, ContactRecreateSipUnavailable,
            ERROR_NOACCESS, rowIndex, 29);
        return 1;
    }
    if (previous) {
        const uintptr_t next = *reinterpret_cast<const uintptr_t*>(current);
        *reinterpret_cast<uintptr_t*>(previous) = next;
        *reinterpret_cast<uintptr_t*>(current) = head;
        *reinterpret_cast<uintptr_t*>(pool + 0x124) = current;
    }
    MemoryBarrier();
    if (*reinterpret_cast<const uintptr_t*>(pool + 0x124) != row.targetSip) {
        FailContactRecreate(0, ContactRecreateWriteVerificationFailed,
            ERROR_WRITE_FAULT, rowIndex, 30);
        return 1;
    }
    return 1;
}

static void __cdecl FinishShapeInstancePair(uintptr_t allocatedSip) {
    const uint32_t rowIndex = g_sipRecreateAttemptRow;
    g_contactRecreateLastAllocatedSip = allocatedSip;
    if (rowIndex != 0xFFFFFFFFu && rowIndex < g_contactRecreateRowCount) {
        const ContactRecreatePlanRow& row = g_contactRecreateRows[rowIndex];
        if (allocatedSip != row.targetSip) {
            FailContactRecreate(0, ContactRecreateSipResultMismatch,
                ERROR_INVALID_STATE, rowIndex,
                static_cast<uint32_t>(allocatedSip));
        } else {
            const uint32_t bit = 1u << rowIndex;
            g_contactRecreateSipMatchedMask |= bit;
            ++g_contactRecreateSipMatchedCount;
            RefreshContactRecreateCompletion();
        }
    }
    g_sipRecreateAttemptRow = 0xFFFFFFFFu;
    ReleaseSipRecreateHookLock();
}

static int __cdecl ObserveAndPrepareContactManager(uintptr_t context,
    uintptr_t descriptor) {
    ObserveContactManagerContext(context);
    LONG state = g_contactRecreateState;
    if (state != ContactRecreateArmed && state != ContactRecreateComplete &&
        InterlockedCompareExchange(&g_contactRecreateHookLock, 0, 0) == 0)
        return 0;
    while (InterlockedCompareExchange(&g_contactRecreateHookLock, 1, 0) != 0)
        SwitchToThread();
    state = g_contactRecreateState;
    if (state != ContactRecreateArmed && state != ContactRecreateComplete) {
        ReleaseContactRecreateHookLock();
        return 0;
    }
    // This is a pre-hook and always passes through to the shipped function.
    // While a rewind plan is active, the hook lock remains held through the
    // untouched shipped function so a worker-thread handoff cannot observe or
    // consume a different prepared allocator top.  Inactive calls retain the
    // original direct trampoline path without taking this lock.
    InterlockedIncrement(&g_contactRecreateObserverEntries);
    const uint32_t threadId = GetCurrentThreadId();
    g_contactRecreateThreadId = threadId;
    if (context != g_contactRecreateContext) {
        FailContactRecreate(0, ContactRecreateContextChanged,
            ERROR_INVALID_STATE, 0xFFFFFFFFu,
            static_cast<uint32_t>(context));
        return 1;
    }
    if (!Readable(reinterpret_cast<const void*>(descriptor), 28)) {
        FailContactRecreate(0, ContactRecreateBadArgument, ERROR_NOACCESS,
            0xFFFFFFFFu, 1);
        return 1;
    }
    const uintptr_t sip = *reinterpret_cast<const uintptr_t*>(descriptor);
    uintptr_t shape0 = *reinterpret_cast<const uintptr_t*>(descriptor + 20);
    uintptr_t shape1 = *reinterpret_cast<const uintptr_t*>(descriptor + 24);
    if (!sip || !shape0 || !shape1 || shape0 == shape1) {
        FailContactRecreate(0, ContactRecreateBadArgument,
            ERROR_INVALID_DATA, 0xFFFFFFFFu, 2);
        return 1;
    }
    const uintptr_t shapeLow = shape0 < shape1 ? shape0 : shape1;
    const uintptr_t shapeHigh = shape0 < shape1 ? shape1 : shape0;
    if (!ReconcileReturnedContactRecreateRows(context)) {
        FailContactRecreate(0, ContactRecreateMetadataMismatch,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 21);
        return 1;
    }
    uint32_t rowIndex = 0xFFFFFFFFu;
    for (uint32_t i = 0; i < g_contactRecreateRowCount; ++i) {
        if (g_contactRecreateRows[i].pxsShapeCoreLow == shapeLow &&
            g_contactRecreateRows[i].pxsShapeCoreHigh == shapeHigh) {
            rowIndex = i;
            break;
        }
    }
    g_contactRecreateAttemptOrdinal = static_cast<uint32_t>(
        g_contactRecreateObserverEntries);
    g_contactRecreateAttemptSip = sip;
    g_contactRecreateAttemptShapeLow = shapeLow;
    g_contactRecreateAttemptShapeHigh = shapeHigh;
    g_contactRecreateAttemptRow = rowIndex;
    g_contactRecreateAttemptMatchedMask = g_contactRecreateMatchedMask;
    g_contactRecreateAttemptFreeCount = 0xFFFFFFFFu;
    g_contactRecreateAttemptManagerIndex = 0xFFFFFFFFu;
    g_contactRecreateAttemptLargeHead = 0;
    g_contactRecreateAttemptManifoldIndex = 0xFFFFFFFFu;
    if (Readable(reinterpret_cast<const void*>(context + 0x2C8), 8) &&
        *reinterpret_cast<const uintptr_t*>(context + 0x2C8) ==
            g_contactRecreateFreeArray) {
        const uint32_t diagnosticFreeCount =
            *reinterpret_cast<const uint32_t*>(context + 0x2CC);
        g_contactRecreateAttemptFreeCount = diagnosticFreeCount;
    }
    const uintptr_t diagnosticPool = context + 0x2E4;
    if (diagnosticPool == g_contactRecreateLargePool &&
        Readable(reinterpret_cast<const void*>(diagnosticPool + 0x124),
            sizeof(uintptr_t)))
        g_contactRecreateAttemptLargeHead =
            *reinterpret_cast<const uintptr_t*>(diagnosticPool + 0x124);

    // The maintenance pass may create short-lived pairs that were absent at
    // the checkpoint.  They receive the untouched allocator behavior.  If
    // one survives or consumes a checkpoint-owned entry, the exact count,
    // order, membership, and owner checks reject the final boundary or the
    // next tracked allocation; a transient that is destroyed first restores
    // the LIFO tops and is behaviorally invisible.
    if (rowIndex == 0xFFFFFFFFu) return 1;

    const ContactRecreatePlanRow& row = g_contactRecreateRows[rowIndex];
    if (sip != row.targetSip) {
        FailContactRecreate(0, ContactRecreateSipResultMismatch,
            ERROR_INVALID_STATE, rowIndex, static_cast<uint32_t>(sip));
        return 1;
    }
    if (!Readable(reinterpret_cast<const void*>(sip + 0x38),
            sizeof(uintptr_t)) ||
        *reinterpret_cast<const uintptr_t*>(sip + 0x38) != 0) {
        FailContactRecreate(0, ContactRecreateDuplicatePair,
            ERROR_ALREADY_EXISTS, rowIndex, 22);
        return 1;
    }
    uint32_t usedPairCount = 0;
    if (!CountUsedContactManagersForPair(context, shapeLow, shapeHigh,
            usedPairCount) || usedPairCount != 0) {
        FailContactRecreate(0, ContactRecreateDuplicatePair,
            ERROR_ALREADY_EXISTS, rowIndex, 23);
        return 1;
    }
    if (g_contactRecreateAttemptFreeCount <= kMaximumContactManagers &&
        Readable(reinterpret_cast<const void*>(g_contactRecreateFreeArray),
            g_contactRecreateAttemptFreeCount * sizeof(uintptr_t))) {
        const uintptr_t* diagnosticFree =
            reinterpret_cast<const uintptr_t*>(g_contactRecreateFreeArray);
        for (uint32_t i = 0; i < g_contactRecreateAttemptFreeCount; ++i) {
            if (diagnosticFree[i] == row.targetManager) {
                g_contactRecreateAttemptManagerIndex = i;
                break;
            }
        }
    }
    if (g_contactRecreateAttemptLargeHead) {
        uintptr_t diagnosticCurrent = g_contactRecreateAttemptLargeHead;
        for (uint32_t i = 0; diagnosticCurrent &&
            i < kMaximumManifolds; ++i) {
            if (diagnosticCurrent == row.targetManifold) {
                g_contactRecreateAttemptManifoldIndex = i;
                break;
            }
            if (!Readable(reinterpret_cast<const void*>(diagnosticCurrent),
                    sizeof(uintptr_t))) break;
            diagnosticCurrent = *reinterpret_cast<const uintptr_t*>(
                diagnosticCurrent);
        }
    }
    const uint32_t bit = 1u << rowIndex;
    if ((g_contactRecreateMatchedMask & bit) != 0) {
        FailContactRecreate(0, ContactRecreateDuplicatePair,
            ERROR_ALREADY_EXISTS, rowIndex, 4);
        return 1;
    }

    if (!Readable(reinterpret_cast<const void*>(context + 0x2C8), 8) ||
        *reinterpret_cast<const uintptr_t*>(context + 0x2C8) !=
            g_contactRecreateFreeArray) {
        FailContactRecreate(0, ContactRecreateContextChanged,
            ERROR_INVALID_STATE, rowIndex, 5);
        return 1;
    }
    uintptr_t* freeArray = reinterpret_cast<uintptr_t*>(
        g_contactRecreateFreeArray);
    const uint32_t remaining = g_contactRecreateRowCount -
        g_contactRecreateMatchedCount;
    const uint32_t freeCount = *reinterpret_cast<const uint32_t*>(
        context + 0x2CC);
    if (freeCount != g_contactRecreateTargetContactCount + remaining ||
        !Readable(freeArray, freeCount * sizeof(uintptr_t))) {
        FailContactRecreate(0, ContactRecreateManagerUnavailable,
            ERROR_INVALID_STATE, rowIndex, 6);
        return 1;
    }
    uint32_t managerIndex = 0xFFFFFFFFu;
    for (uint32_t i = g_contactRecreateTargetContactCount;
        i < freeCount; ++i) {
        if (freeArray[i] == row.targetManager) {
            managerIndex = i;
            break;
        }
    }
    if (managerIndex == 0xFFFFFFFFu ||
        !Writable(freeArray + managerIndex, sizeof(uintptr_t)) ||
        !Writable(freeArray + freeCount - 1, sizeof(uintptr_t))) {
        FailContactRecreate(0, ContactRecreateManagerUnavailable,
            ERROR_NOACCESS, rowIndex, 7);
        return 1;
    }

    const uintptr_t pool = context + 0x2E4;
    if (pool != g_contactRecreateLargePool ||
        !Readable(reinterpret_cast<const void*>(pool + 0x118), 0x10)) {
        FailContactRecreate(0, ContactRecreateContextChanged,
            ERROR_INVALID_STATE, rowIndex, 8);
        return 1;
    }
    uintptr_t head = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    uintptr_t previous = 0;
    uintptr_t current = head;
    uint32_t manifoldIndex = 0;
    const uint32_t expectedLargeCount =
        g_contactRecreateTargetLargeCount + remaining;
    while (current && current != row.targetManifold &&
        manifoldIndex < expectedLargeCount) {
        if (!Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t))) break;
        previous = current;
        current = *reinterpret_cast<const uintptr_t*>(current);
        ++manifoldIndex;
    }
    if (current != row.targetManifold || manifoldIndex >= remaining ||
        !Readable(reinterpret_cast<const void*>(current), sizeof(uintptr_t)) ||
        !Writable(reinterpret_cast<void*>(pool + 0x124), sizeof(uintptr_t)) ||
        !Writable(reinterpret_cast<void*>(current), sizeof(uintptr_t)) ||
        (previous && !Writable(reinterpret_cast<void*>(previous),
            sizeof(uintptr_t)))) {
        FailContactRecreate(0, ContactRecreateManifoldUnavailable,
            ERROR_NOACCESS, rowIndex, 9);
        return 1;
    }

    const uintptr_t displacedManager = freeArray[freeCount - 1];
    freeArray[managerIndex] = displacedManager;
    freeArray[freeCount - 1] = row.targetManager;
    if (previous) {
        const uintptr_t next = *reinterpret_cast<const uintptr_t*>(current);
        *reinterpret_cast<uintptr_t*>(previous) = next;
        *reinterpret_cast<uintptr_t*>(current) = head;
        *reinterpret_cast<uintptr_t*>(pool + 0x124) = current;
    }
    MemoryBarrier();
    if (freeArray[freeCount - 1] != row.targetManager ||
        *reinterpret_cast<const uintptr_t*>(pool + 0x124) !=
            row.targetManifold) {
        FailContactRecreate(0, ContactRecreateWriteVerificationFailed,
            ERROR_WRITE_FAULT, rowIndex, 10);
        return 1;
    }

    g_contactRecreateMatchedMask |= bit;
    ++g_contactRecreateMatchedCount;
    g_contactRecreateLastSip = sip;
    g_contactRecreateLastShapeLow = shapeLow;
    g_contactRecreateLastShapeHigh = shapeHigh;
    g_contactRecreateLastManager = row.targetManager;
    g_contactRecreateLastManifold = row.targetManifold;
    RefreshContactRecreateCompletion();
    return 1;
}

// The shape-pair hook selects only the identity popped by PhysX's own pool.
// Construction, overlap filtering, manager creation, and every side effect
// remain inside the untouched shipped implementation.
__declspec(naked) static void HookCreateShapeInstancePair() {
    __asm pushfd
    __asm pushad
    __asm mov eax, dword ptr [esp + 0x28]
    __asm mov edx, dword ptr [esp + 0x2C]
    __asm push edx
    __asm push eax
    __asm push ecx
    __asm call ObserveAndPrepareShapeInstancePair
    __asm add esp, 0x0C
    __asm mov dword ptr [esp + 0x0C], eax
    __asm popad
    __asm popfd
    __asm cmp dword ptr [esp - 0x18], 0
    __asm jne locked_sip_call
    __asm jmp dword ptr [g_shapeInstancePairTrampoline]
locked_sip_call:
    __asm push dword ptr [esp + 0x0C]
    __asm push dword ptr [esp + 0x0C]
    __asm push dword ptr [esp + 0x0C]
    __asm call dword ptr [g_shapeInstancePairTrampoline]
    __asm pushfd
    __asm pushad
    __asm mov ecx, dword ptr [esp + 0x1C]
    __asm push ecx
    __asm call FinishShapeInstancePair
    __asm add esp, 4
    __asm popad
    __asm popfd
    __asm ret 0x0C
}

// This observer is a pass-through during ordinary play.  Only an explicitly
// armed rewind plan may reorder still-free allocator entries before the
// untouched shipped createContactManager body consumes them.
__declspec(naked) static void HookCreateContactManagerContext() {
    __asm pushfd
    __asm pushad
    __asm mov eax, dword ptr [esp + 0x28]
    __asm push eax
    __asm push ecx
    __asm call ObserveAndPrepareContactManager
    __asm add esp, 8
    // POPAD deliberately ignores its saved-ESP slot.  Reuse that slot to
    // carry the helper's lock-ownership result while restoring every register
    // and flag exactly as the shipped function received them.
    __asm mov dword ptr [esp + 0x0C], eax
    __asm popad
    __asm popfd
    __asm cmp dword ptr [esp - 0x18], 0
    __asm jne locked_recreate_call
    __asm jmp dword ptr [g_contactManagerContextTrampoline]
locked_recreate_call:
    // Rebuild the original thiscall argument list beneath a private return
    // address.  The trampoline executes the untouched shipped function and
    // its RET 8 returns here with the original caller stack restored.
    __asm push dword ptr [esp + 8]
    __asm push dword ptr [esp + 8]
    __asm call dword ptr [g_contactManagerContextTrampoline]
    __asm pushfd
    __asm pushad
    __asm call ReleaseContactRecreateHookLock
    __asm popad
    __asm popfd
    __asm ret 8
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

static int FailDirtyInteractionSnapshot(DirtyInteractionOrderReceipt* receipt,
    DirtyInteractionOrderResult result, uint32_t error) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
    }
    return 0;
}

// Reads the current CoalescedHashSet directly at a quiescent managed output
// boundary.  This deliberately uses only caller-owned scratch/output storage:
// the one-shot hook receipt may still be awaiting managed validation, so a
// diagnostic snapshot must not alter any g_dirtyInteraction* field or counter.
static int CaptureDirtyInteractionOrderSnapshot(uintptr_t unityBase,
    uintptr_t nphase, DirtyInteractionKey* keys, uint32_t capacity,
    DirtyInteractionOrderReceipt* receipt) {
    if (!receipt) return 0;
    InitializeDirtyInteractionOrderReceipt(receipt, unityBase);
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase)
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderNotInstalled, ERROR_INVALID_STATE);
    if (!nphase || nphase != g_lastObservedNPhaseCore)
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderIdentityChanged, ERROR_INVALID_STATE);
    if (InterlockedCompareExchange(&g_dirtyInteractionAction, 0, 0) !=
        DirtyInteractionOrderIdle)
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderPending, ERROR_IO_PENDING);

    const uintptr_t set = nphase + 0x44;
    if (!Readable(reinterpret_cast<const void*>(nphase), sizeof(uintptr_t)) ||
        !Readable(reinterpret_cast<const void*>(set), 0x28))
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderInvalidHeader, ERROR_NOACCESS);
    const uintptr_t buffer =
        *reinterpret_cast<const uintptr_t*>(set + 0x00);
    const uintptr_t entries =
        *reinterpret_cast<const uintptr_t*>(set + 0x04);
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
    receipt->nphaseCore = nphase;
    receipt->set = set;
    receipt->entries = entries;
    receipt->entriesNext = entriesNext;
    receipt->hash = hash;
    receipt->entriesCapacity = entriesCapacity;
    receipt->hashSize = hashSize;
    receipt->count = count;
    const uintptr_t expectedEntriesNext =
        buffer + hashSize * sizeof(uint32_t);
    const uintptr_t expectedEntries = (expectedEntriesNext +
        entriesCapacity * sizeof(uint32_t) + 15u) &
        ~static_cast<uintptr_t>(15u);
    const uintptr_t ownerScene =
        *reinterpret_cast<const uintptr_t*>(nphase);
    if (!buffer || !entries || !entriesNext || !hash || hash != buffer ||
        entriesNext != expectedEntriesNext || entries != expectedEntries ||
        !ownerScene ||
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
            hashSize * sizeof(uint32_t)))
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderInvalidHeader, ERROR_INVALID_DATA);
    if (capacity < count)
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER);
    if (count != 0 && (!keys ||
        !Writable(keys, count * sizeof(DirtyInteractionKey))))
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderBadArgument, ERROR_INVALID_PARAMETER);

    DirtyInteractionKey localKeys[kMaximumDirtyInteractions] = {};
    uint8_t used[kMaximumDirtyInteractions] = {};
    const uintptr_t* dense = reinterpret_cast<const uintptr_t*>(entries);
    for (uint32_t i = 0; i < count; ++i) {
        if (!dense[i] || !Readable(reinterpret_cast<const void*>(dense[i]),
                10 * sizeof(uintptr_t)))
            return FailDirtyInteractionSnapshot(receipt,
                DirtyInteractionOrderInvalidEntry, ERROR_NOACCESS);
        const DirtyInteractionKey key = ReadDirtyInteractionKey(dense[i]);
        const uint16_t coreFlags =
            *reinterpret_cast<const uint16_t*>(dense[i] + 0x06);
        if (!key.primaryVtable || !key.elementLow || !key.elementHigh ||
            key.elementLow == key.elementHigh || key.interactionType > 5 ||
            (coreFlags & 3u) != 3u)
            return FailDirtyInteractionSnapshot(receipt,
                DirtyInteractionOrderInvalidEntry, ERROR_INVALID_DATA);
        localKeys[i] = key;
        for (uint32_t prior = 0; prior < i; ++prior)
            if (dense[prior] == dense[i] ||
                SameDirtyInteractionKey(localKeys[prior], key))
                return FailDirtyInteractionSnapshot(receipt,
                    DirtyInteractionOrderInvalidEntry, ERROR_DUP_NAME);
    }
    const uint32_t* currentNext =
        reinterpret_cast<const uint32_t*>(entriesNext);
    const uint32_t* currentHash = reinterpret_cast<const uint32_t*>(hash);
    uint32_t visited = 0;
    for (uint32_t bucket = 0; bucket < hashSize; ++bucket) {
        uint32_t index = currentHash[bucket];
        while (index != 0xFFFFFFFFu) {
            if (index >= count || used[index] ||
                (PhysxPointerHash(dense[index]) & (hashSize - 1)) != bucket ||
                ++visited > count)
                return FailDirtyInteractionSnapshot(receipt,
                    DirtyInteractionOrderInvalidHeader, ERROR_INVALID_DATA);
            used[index] = 1;
            index = currentNext[index];
        }
    }
    if (visited != count)
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderInvalidHeader, ERROR_INVALID_DATA);
    const uint32_t orderHash = DirtyInteractionKeyOrderHash(localKeys, count);

    // There is no public lock for this container.  The managed caller invokes
    // us only after the physics step has joined, but repeat-read every owned
    // field and chain before publishing so an accidental live call fails
    // closed instead of returning a torn state.
    if (!Readable(reinterpret_cast<const void*>(set), 0x28) ||
        *reinterpret_cast<const uintptr_t*>(set + 0x00) != buffer ||
        *reinterpret_cast<const uintptr_t*>(set + 0x04) != entries ||
        *reinterpret_cast<const uintptr_t*>(set + 0x08) != entriesNext ||
        *reinterpret_cast<const uintptr_t*>(set + 0x0C) != hash ||
        *reinterpret_cast<const uint32_t*>(set + 0x10) != entriesCapacity ||
        *reinterpret_cast<const uint32_t*>(set + 0x14) != hashSize ||
        *reinterpret_cast<const uint32_t*>(set + 0x18) != loadFactorBits ||
        *reinterpret_cast<const uint32_t*>(set + 0x1C) != freeList ||
        *reinterpret_cast<const uint32_t*>(set + 0x24) != count ||
        *reinterpret_cast<const uintptr_t*>(nphase) != ownerScene ||
        (*reinterpret_cast<const uint8_t*>(ownerScene + 0x4A4) & 6u) != 0)
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderUnstable, ERROR_RETRY);
    if ((count != 0 && !Readable(dense, count * sizeof(uintptr_t))) ||
        !Readable(currentNext, entriesCapacity * sizeof(uint32_t)) ||
        !Readable(currentHash, hashSize * sizeof(uint32_t)))
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderUnstable, ERROR_RETRY);
    for (uint32_t i = 0; i < count; ++i) {
        const uintptr_t interaction = dense[i];
        if (interaction == 0 ||
            !Readable(reinterpret_cast<const void*>(interaction),
                10 * sizeof(uintptr_t)) ||
            !SameDirtyInteractionKey(ReadDirtyInteractionKey(interaction),
                localKeys[i]))
            return FailDirtyInteractionSnapshot(receipt,
                DirtyInteractionOrderUnstable, ERROR_RETRY);
    }
    for (uint32_t i = 0; i < count; ++i) used[i] = 0;
    visited = 0;
    for (uint32_t bucket = 0; bucket < hashSize; ++bucket) {
        uint32_t index = currentHash[bucket];
        while (index != 0xFFFFFFFFu) {
            if (index >= count || used[index] ||
                (PhysxPointerHash(dense[index]) & (hashSize - 1)) != bucket ||
                ++visited > count)
                return FailDirtyInteractionSnapshot(receipt,
                    DirtyInteractionOrderUnstable, ERROR_RETRY);
            used[index] = 1;
            index = currentNext[index];
        }
    }
    if (visited != count ||
        DirtyInteractionKeyOrderHash(localKeys, count) != orderHash)
        return FailDirtyInteractionSnapshot(receipt,
            DirtyInteractionOrderUnstable, ERROR_RETRY);
    if (count != 0)
        CopyBytes(keys, localKeys, count * sizeof(DirtyInteractionKey));
    receipt->orderHashBefore = orderHash;
    receipt->orderHashAfter = orderHash;
    receipt->result = DirtyInteractionOrderOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

// Runs at the exact entry to Sc::NPhaseCore::updateDirtyInteractions.  The
// hook is inert unless explicitly armed while the authoring pause fence is
// held.  All validation completes before a restore writes any PhysX state.
static void __cdecl ProcessDirtyInteractionOrder(const uintptr_t* saved) {
    const uintptr_t observedNPhase = saved ? saved[6] : 0;
    if (observedNPhase) {
        g_lastObservedNPhaseCore = observedNPhase;
        MemoryBarrier();
        InterlockedIncrement(&g_dirtyNPhaseObservations);
    }
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
    g_lastObservedNPhaseCore = 0;
    InterlockedExchange(&g_dirtyNPhaseObservations, 0);
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

static int ReadLastObservedNPhaseCore(uintptr_t unityBase,
    uintptr_t* nphaseCore, uint32_t* observations) {
    if (!nphaseCore || !observations ||
        !Writable(nphaseCore, sizeof(*nphaseCore)) ||
        !Writable(observations, sizeof(*observations))) return 0;
    *nphaseCore = 0;
    *observations = 0;
    if (!g_dirtyInteractionInstalled ||
        unityBase != g_dirtyInteractionUnityBase) return 0;
    LONG before = 0;
    LONG after = 0;
    uintptr_t observed = 0;
    do {
        before = InterlockedCompareExchange(&g_dirtyNPhaseObservations, 0, 0);
        MemoryBarrier();
        observed = g_lastObservedNPhaseCore;
        MemoryBarrier();
        after = InterlockedCompareExchange(&g_dirtyNPhaseObservations, 0, 0);
    } while (before != after);
    if (before <= 0 || !observed) return 0;
    *nphaseCore = observed;
    *observations = static_cast<uint32_t>(before);
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
    g_lastObservedNPhaseCore = 0;
    InterlockedExchange(&g_dirtyNPhaseObservations, 0);
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

static bool HasShapeInstancePairJump(const void* source) {
    const uint8_t* bytes = static_cast<const uint8_t*>(source);
    if (bytes[0] != 0xE9 || bytes[5] != 0x90 || bytes[6] != 0x90 ||
        bytes[7] != 0x90) return false;
    const int32_t displacement = *reinterpret_cast<const int32_t*>(bytes + 1);
    const uintptr_t destination = reinterpret_cast<uintptr_t>(source) + 5 +
        displacement;
    return destination == reinterpret_cast<uintptr_t>(
        HookCreateShapeInstancePair);
}

static bool ShapeInstancePairPoolRevisionMatches(uintptr_t unityBase);

static bool RestoreHookBytes(void* source, const uint8_t* original,
    uint32_t size) {
    DWORD oldProtect = 0;
    if (!VirtualProtect(source, size, PAGE_EXECUTE_READWRITE, &oldProtect))
        return false;
    CopyBytes(source, original, size);
    FlushInstructionCache(GetCurrentProcess(), source, size);
    DWORD ignored = 0;
    return VirtualProtect(source, size, oldProtect, &ignored) != FALSE;
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
    uint8_t* source = reinterpret_cast<uint8_t*>(unityBase +
        kCreateContactManagerRva);
    uint8_t* sipSource = reinterpret_cast<uint8_t*>(unityBase +
        kCreateShapeInstancePairRva);
    if (!Readable(source, sizeof(kCreateContactManagerBytes)) ||
        !EqualBytes(source, kCreateContactManagerBytes,
            sizeof(kCreateContactManagerBytes)) ||
        !ShapeInstancePairPoolRevisionMatches(unityBase))
        return FailContactContextObserver(receipt, ContactContextObserverRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    uint8_t* trampoline = static_cast<uint8_t*>(VirtualAlloc(0,
        sizeof(kCreateContactManagerBytes) + 5, MEM_COMMIT | MEM_RESERVE,
        PAGE_EXECUTE_READWRITE));
    if (!trampoline)
        return FailContactContextObserver(receipt, ContactContextObserverAllocationFailed,
            GetLastError());
    uint8_t* sipTrampoline = static_cast<uint8_t*>(VirtualAlloc(0,
        sizeof(kCreateShapeInstancePairBytes) + 5,
        MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!sipTrampoline) {
        const DWORD error = GetLastError();
        VirtualFree(trampoline, 0, MEM_RELEASE);
        return FailContactContextObserver(receipt,
            ContactContextObserverAllocationFailed, error);
    }
    CopyBytes(g_contactManagerContextOriginal, source, sizeof(kCreateContactManagerBytes));
    CopyBytes(trampoline, source, sizeof(kCreateContactManagerBytes));
    trampoline[sizeof(kCreateContactManagerBytes)] = 0xE9;
    *reinterpret_cast<int32_t*>(trampoline + sizeof(kCreateContactManagerBytes) + 1) =
        static_cast<int32_t>(reinterpret_cast<uintptr_t>(source + sizeof(kCreateContactManagerBytes)) -
        reinterpret_cast<uintptr_t>(trampoline + sizeof(kCreateContactManagerBytes)) - 5);
    FlushInstructionCache(GetCurrentProcess(), trampoline,
        sizeof(kCreateContactManagerBytes) + 5);
    CopyBytes(g_shapeInstancePairOriginal, sipSource,
        sizeof(kCreateShapeInstancePairBytes));
    CopyBytes(sipTrampoline, sipSource,
        sizeof(kCreateShapeInstancePairBytes));
    sipTrampoline[sizeof(kCreateShapeInstancePairBytes)] = 0xE9;
    *reinterpret_cast<int32_t*>(sipTrampoline +
        sizeof(kCreateShapeInstancePairBytes) + 1) = static_cast<int32_t>(
        reinterpret_cast<uintptr_t>(sipSource +
            sizeof(kCreateShapeInstancePairBytes)) -
        reinterpret_cast<uintptr_t>(sipTrampoline +
            sizeof(kCreateShapeInstancePairBytes)) - 5);
    FlushInstructionCache(GetCurrentProcess(), sipTrampoline,
        sizeof(kCreateShapeInstancePairBytes) + 5);
    g_contactManagerContextTrampoline = trampoline;
    g_shapeInstancePairTrampoline = sipTrampoline;
    if (!WriteJump(sipSource, HookCreateShapeInstancePair,
            sizeof(kCreateShapeInstancePairBytes))) {
        const DWORD error = GetLastError();
        g_contactManagerContextTrampoline = 0;
        g_shapeInstancePairTrampoline = 0;
        VirtualFree(trampoline, 0, MEM_RELEASE);
        VirtualFree(sipTrampoline, 0, MEM_RELEASE);
        return FailContactContextObserver(receipt, ContactContextObserverProtectFailed, error);
    }
    if (!WriteJump(source, HookCreateContactManagerContext,
            sizeof(kCreateContactManagerBytes))) {
        const DWORD error = GetLastError();
        RestoreHookBytes(sipSource, g_shapeInstancePairOriginal,
            sizeof(kCreateShapeInstancePairBytes));
        g_contactManagerContextTrampoline = 0;
        g_shapeInstancePairTrampoline = 0;
        VirtualFree(trampoline, 0, MEM_RELEASE);
        VirtualFree(sipTrampoline, 0, MEM_RELEASE);
        return FailContactContextObserver(receipt,
            ContactContextObserverProtectFailed, error);
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
    uint8_t* source = reinterpret_cast<uint8_t*>(unityBase +
        kCreateContactManagerRva);
    uint8_t* sipSource = reinterpret_cast<uint8_t*>(unityBase +
        kCreateShapeInstancePairRva);
    if (!Readable(source, sizeof(kCreateContactManagerBytes)) ||
        !HasObserverJump(source) ||
        !Readable(sipSource, sizeof(kCreateShapeInstancePairBytes)) ||
        !HasShapeInstancePairJump(sipSource))
        return FailContactContextObserver(receipt, ContactContextObserverPatchChanged,
            ERROR_INVALID_STATE);
    if (!RestoreHookBytes(source, g_contactManagerContextOriginal,
            sizeof(kCreateContactManagerBytes)) ||
        !RestoreHookBytes(sipSource, g_shapeInstancePairOriginal,
            sizeof(kCreateShapeInstancePairBytes)))
        return FailContactContextObserver(receipt, ContactContextObserverProtectFailed,
            GetLastError());
    void* trampoline = g_contactManagerContextTrampoline;
    void* sipTrampoline = g_shapeInstancePairTrampoline;
    g_contactManagerContextTrampoline = 0;
    g_shapeInstancePairTrampoline = 0;
    g_contactManagerContextObserverInstalled = false;
    g_observerUnityBase = 0;
    if (trampoline) VirtualFree(trampoline, 0, MEM_RELEASE);
    if (sipTrampoline) VirtualFree(sipTrampoline, 0, MEM_RELEASE);
    InitializeContactContextObserverReceipt(receipt, unityBase);
    receipt->result = ContactContextObserverOk;
    return 1;
}

static bool ContactManagerOwnerRevisionMatches(uintptr_t unityBase) {
    if (!unityBase) return false;
    const void* createPool = reinterpret_cast<const void*>(
        unityBase + kCreateContactManagerRva + 0x06);
    const void* createPop = reinterpret_cast<const void*>(
        unityBase + kCreateContactManagerRva + 0x29);
    const void* initialize = reinterpret_cast<const void*>(
        unityBase + kInitContactManagerRva);
    const void* initializeUserData = reinterpret_cast<const void*>(
        unityBase + kInitContactManagerRva + 0x172);
    const void* createSip = reinterpret_cast<const void*>(
        unityBase + kShapeInstancePairCreateManagerRva);
    const void* createSipShapeCore = reinterpret_cast<const void*>(
        unityBase + kShapeInstancePairCreateManagerRva + 0x1E6);
    const void* getType = reinterpret_cast<const void*>(
        unityBase + kNpShapeGetGeometryTypeRva);
    return Readable(createPool, sizeof(kCreateContactManagerPoolBytes)) &&
        EqualBytes(createPool, kCreateContactManagerPoolBytes,
            sizeof(kCreateContactManagerPoolBytes)) &&
        Readable(createPop, sizeof(kCreateContactManagerPopBytes)) &&
        EqualBytes(createPop, kCreateContactManagerPopBytes,
            sizeof(kCreateContactManagerPopBytes)) &&
        Readable(initialize, sizeof(kInitContactManagerBytes)) &&
        EqualBytes(initialize, kInitContactManagerBytes,
            sizeof(kInitContactManagerBytes)) &&
        Readable(initializeUserData,
            sizeof(kInitContactManagerUserDataBytes)) &&
        EqualBytes(initializeUserData, kInitContactManagerUserDataBytes,
            sizeof(kInitContactManagerUserDataBytes)) &&
        Readable(createSip, sizeof(kCreateSipBytes)) &&
        EqualBytes(createSip, kCreateSipBytes, sizeof(kCreateSipBytes)) &&
        Readable(createSipShapeCore, sizeof(kCreateSipShapeCoreBytes)) &&
        EqualBytes(createSipShapeCore, kCreateSipShapeCoreBytes,
            sizeof(kCreateSipShapeCoreBytes)) &&
        Readable(getType, sizeof(kNpShapeGetTypeBytes)) &&
        EqualBytes(getType, kNpShapeGetTypeBytes,
            sizeof(kNpShapeGetTypeBytes));
}

static bool ReadContactBitmap(uintptr_t bitmap, uint32_t totalSlots,
    uintptr_t& map, uint32_t& wordCount) {
    if (!Readable(reinterpret_cast<const void*>(bitmap), 8)) return false;
    map = *reinterpret_cast<const uintptr_t*>(bitmap);
    wordCount = *reinterpret_cast<const uint32_t*>(bitmap + 4) & 0x7FFFFFFFu;
    const uint32_t requiredWords = (totalSlots + 31u) >> 5;
    return map && wordCount >= requiredWords && wordCount <= 0x20000u &&
        Readable(reinterpret_cast<const void*>(map), requiredWords * 4);
}

static uint32_t ContactBitmapCount(uintptr_t map, uint32_t totalSlots) {
    const uint32_t words = (totalSlots + 31u) >> 5;
    uint32_t count = 0;
    for (uint32_t i = 0; i < words; ++i) {
        uint32_t value = *reinterpret_cast<const uint32_t*>(map + i * 4);
        if (i + 1 == words && (totalSlots & 31u) != 0)
            value &= (1u << (totalSlots & 31u)) - 1u;
        count += BitCount32(value);
    }
    return count;
}

static bool ContactBitmapTest(uintptr_t map, uint32_t slot) {
    return (*reinterpret_cast<const uint32_t*>(
        map + (slot >> 5) * 4) & (1u << (slot & 31u))) != 0;
}

static uint32_t ContactBitmapHash(uintptr_t map, uint32_t totalSlots) {
    return WordHash(reinterpret_cast<const uint32_t*>(map),
        (totalSlots + 31u) >> 5);
}

static bool ReadContactManagerOwner(uintptr_t unityBase, uintptr_t manager,
    uint32_t slot, uint32_t membership, ContactManagerOwnerRecord& record,
    ContactManagerOwnerReceipt* receipt) {
    record = {};
    record.slot = slot;
    record.membership = membership;
    record.manager = manager;
    if (!Readable(reinterpret_cast<const void*>(manager),
            kContactManagerSize)) {
        FailContactManagerOwner(receipt, ContactManagerOwnerUnreadableManager,
            ERROR_NOACCESS, slot, 1);
        return false;
    }
    if (*reinterpret_cast<const uint32_t*>(manager + 0x4C) != slot) {
        FailContactManagerOwner(receipt, ContactManagerOwnerInvalidManager,
            ERROR_INVALID_DATA, slot, 2);
        return false;
    }

    record.rigidBody0 = *reinterpret_cast<const uintptr_t*>(manager + 0x00);
    record.rigidBody1 = *reinterpret_cast<const uintptr_t*>(manager + 0x04);
    record.managerFlags = *reinterpret_cast<const uint32_t*>(manager + 0x08);
    record.sip = *reinterpret_cast<const uintptr_t*>(manager + 0x0C);
    record.contactCount = *reinterpret_cast<const uint16_t*>(manager + 0x24);
    record.workUnitFlags = *reinterpret_cast<const uint16_t*>(manager + 0x26);
    record.manifold = *reinterpret_cast<const uintptr_t*>(manager + 0x3C);
    record.pairData = *reinterpret_cast<const uint32_t*>(manager + 0x40);
    record.cachePointer = *reinterpret_cast<const uintptr_t*>(manager + 0x44);
    record.cacheSize = *reinterpret_cast<const uint16_t*>(manager + 0x48);
    record.rigidCore0 = *reinterpret_cast<const uintptr_t*>(manager + 0x50);
    record.rigidCore1 = *reinterpret_cast<const uintptr_t*>(manager + 0x54);
    record.pxsShapeCore0 = *reinterpret_cast<const uintptr_t*>(manager + 0x58);
    record.pxsShapeCore1 = *reinterpret_cast<const uintptr_t*>(manager + 0x5C);
    record.geomType0 = *reinterpret_cast<const uint8_t*>(manager + 0x70);
    record.geomType1 = *reinterpret_cast<const uint8_t*>(manager + 0x71);
    record.statusFlags = *reinterpret_cast<const uint16_t*>(manager + 0x72);
    record.transformCache0 = *reinterpret_cast<const uint32_t*>(manager + 0x74);
    record.transformCache1 = *reinterpret_cast<const uint32_t*>(manager + 0x78);
    record.disableResponse = *reinterpret_cast<const uint8_t*>(manager + 0x22);
    record.disableCcd = *reinterpret_cast<const uint8_t*>(manager + 0x23);

    if (!record.sip || !Readable(reinterpret_cast<const void*>(record.sip),
            0x44)) {
        FailContactManagerOwner(receipt, ContactManagerOwnerInvalidSip,
            ERROR_NOACCESS, slot, 3);
        return false;
    }
    record.primaryVtable = *reinterpret_cast<const uintptr_t*>(record.sip);
    record.secondaryVtable = *reinterpret_cast<const uintptr_t*>(record.sip + 8);
    record.interactionType = *reinterpret_cast<const uint8_t*>(record.sip + 0x1C);
    record.interactionFlags = *reinterpret_cast<const uint8_t*>(record.sip + 0x1D);
    record.shapeSim0 = *reinterpret_cast<const uintptr_t*>(record.sip + 0x20);
    record.shapeSim1 = *reinterpret_cast<const uintptr_t*>(record.sip + 0x24);
    record.contactReportStamp = *reinterpret_cast<const uint32_t*>(
        record.sip + 0x28);
    record.sipFlags = *reinterpret_cast<const uint32_t*>(record.sip + 0x2C);
    record.actorPair = *reinterpret_cast<const uintptr_t*>(record.sip + 0x30);
    record.reportPairIndex = *reinterpret_cast<const uint32_t*>(
        record.sip + 0x34);
    record.reportStreamIndex = *reinterpret_cast<const uint16_t*>(
        record.sip + 0x40);
    const uintptr_t backlink = *reinterpret_cast<const uintptr_t*>(record.sip + 0x38);
    if (!record.primaryVtable || !record.secondaryVtable || !record.actorPair ||
        backlink != manager || record.interactionType != 0 ||
        (record.interactionFlags & 0x11u) != 0x11u || !record.shapeSim0 ||
        !record.shapeSim1 || record.shapeSim0 == record.shapeSim1 ||
        !Readable(reinterpret_cast<const void*>(record.shapeSim0), 0x20) ||
        !Readable(reinterpret_cast<const void*>(record.shapeSim1), 0x20)) {
        FailContactManagerOwner(receipt, ContactManagerOwnerInvalidSip,
            ERROR_INVALID_DATA, slot, 4);
        return false;
    }

    const uintptr_t shapeActor0 = *reinterpret_cast<const uintptr_t*>(
        record.shapeSim0 + 0x08);
    const uintptr_t shapeActor1 = *reinterpret_cast<const uintptr_t*>(
        record.shapeSim1 + 0x08);
    if (!shapeActor0 || !shapeActor1 || shapeActor0 == shapeActor1 ||
        !Readable(reinterpret_cast<const void*>(record.actorPair), 0x18)) {
        FailContactManagerOwner(receipt, ContactManagerOwnerInvalidSip,
            ERROR_NOACCESS, slot, 9);
        return false;
    }
    record.actorPairActor0 = *reinterpret_cast<const uintptr_t*>(
        record.actorPair + 0x00);
    record.actorPairActor1 = *reinterpret_cast<const uintptr_t*>(
        record.actorPair + 0x04);
    record.actorPairScene = *reinterpret_cast<const uintptr_t*>(
        record.actorPair + 0x08);
    record.actorPairInternalFlags = *reinterpret_cast<const uint16_t*>(
        record.actorPair + 0x0C);
    record.actorPairTouchCount = *reinterpret_cast<const uint16_t*>(
        record.actorPair + 0x0E);
    record.actorPairRefCount = *reinterpret_cast<const uint16_t*>(
        record.actorPair + 0x10);
    record.actorPairReportData = *reinterpret_cast<const uintptr_t*>(
        record.actorPair + 0x14);
    const bool actorEndpointsMatch =
        (record.actorPairActor0 == shapeActor0 &&
            record.actorPairActor1 == shapeActor1) ||
        (record.actorPairActor0 == shapeActor1 &&
            record.actorPairActor1 == shapeActor0);
    if (!actorEndpointsMatch || !record.actorPairScene ||
        (record.actorPairInternalFlags & ~0x7u) != 0 ||
        record.actorPairRefCount == 0 ||
        (record.actorPairReportData &&
            !Readable(reinterpret_cast<const void*>(
                record.actorPairReportData), sizeof(uintptr_t)))) {
        FailContactManagerOwner(receipt, ContactManagerOwnerInvalidSip,
            ERROR_INVALID_DATA, slot, 10);
        return false;
    }
    record.actorPairHash = ByteHash(
        reinterpret_cast<const void*>(record.actorPair), 0x18);

    const uintptr_t scShapeCore0 = *reinterpret_cast<const uintptr_t*>(
        record.shapeSim0 + 0x1C);
    const uintptr_t scShapeCore1 = *reinterpret_cast<const uintptr_t*>(
        record.shapeSim1 + 0x1C);
    if (!scShapeCore0 || !scShapeCore1 || scShapeCore0 > UINTPTR_MAX - 0x20 ||
        scShapeCore1 > UINTPTR_MAX - 0x20 ||
        scShapeCore0 + 0x20 != record.pxsShapeCore0 ||
        scShapeCore1 + 0x20 != record.pxsShapeCore1 ||
        record.transformCache0 != *reinterpret_cast<const uint32_t*>(
            record.shapeSim0 + 0x18) ||
        record.transformCache1 != *reinterpret_cast<const uint32_t*>(
            record.shapeSim1 + 0x18) || record.pxsShapeCore0 < 0x50 ||
        record.pxsShapeCore1 < 0x50) {
        FailContactManagerOwner(receipt, ContactManagerOwnerInvalidShape,
            ERROR_INVALID_DATA, slot, 5);
        return false;
    }
    record.pxShape0 = record.pxsShapeCore0 - 0x50;
    record.pxShape1 = record.pxsShapeCore1 - 0x50;
    if (!Readable(reinterpret_cast<const void*>(record.pxShape0), 0x78) ||
        !Readable(reinterpret_cast<const void*>(record.pxShape1), 0x78) ||
        *reinterpret_cast<const uintptr_t*>(record.pxShape0) !=
            unityBase + kNpShapeVtableRva ||
        *reinterpret_cast<const uintptr_t*>(record.pxShape1) !=
            unityBase + kNpShapeVtableRva ||
        static_cast<uint8_t>(*reinterpret_cast<const uint32_t*>(
            record.pxShape0 + 0x74)) != record.geomType0 ||
        static_cast<uint8_t>(*reinterpret_cast<const uint32_t*>(
            record.pxShape1 + 0x74)) != record.geomType1) {
        FailContactManagerOwner(receipt, ContactManagerOwnerInvalidShape,
            ERROR_INVALID_DATA, slot, 6);
        return false;
    }

    record.managerHash = ByteHash(reinterpret_cast<const void*>(manager),
        kContactManagerSize);
    record.sipHash = ByteHash(reinterpret_cast<const void*>(record.sip), 0x44);
    if (record.manifold > 1 && (record.manifold & 1u) == 0) {
        record.manifoldBytes = record.geomType0 && record.geomType1 &&
            record.geomType0 <= 4 && record.geomType1 <= 4 ? 0xF0u : 0x60u;
        if (!Readable(reinterpret_cast<const void*>(record.manifold),
                record.manifoldBytes)) {
            FailContactManagerOwner(receipt, ContactManagerOwnerInvalidManager,
                ERROR_NOACCESS, slot, 7);
            return false;
        }
        record.manifoldHash = ByteHash(
            reinterpret_cast<const void*>(record.manifold),
            record.manifoldBytes);
    }
    if (record.cacheSize) {
        if (!record.cachePointer || record.cacheSize > 16384u ||
            !Readable(reinterpret_cast<const void*>(record.cachePointer),
                record.cacheSize)) {
            FailContactManagerOwner(receipt, ContactManagerOwnerInvalidManager,
                ERROR_NOACCESS, slot, 8);
            return false;
        }
        record.cacheHash = ByteHash(
            reinterpret_cast<const void*>(record.cachePointer),
            record.cacheSize);
    }
    record.validationFlags = 0x00FFu;
    return true;
}

static int CaptureContactManagerActiveOwners(uintptr_t unityBase,
    uintptr_t context, ContactManagerOwnerRecord* records, uint32_t capacity,
    ContactManagerOwnerReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactManagerOwnerReceipt(receipt, unityBase, context);
    if (!unityBase || !context || (!records && capacity))
        return FailContactManagerOwner(receipt, ContactManagerOwnerBadArgument,
            ERROR_INVALID_PARAMETER, 0xFFFFFFFFu, 1);
    if (!ContactManagerOwnerRevisionMatches(unityBase))
        return FailContactManagerOwner(receipt,
            ContactManagerOwnerRevisionMismatch, ERROR_REVISION_MISMATCH,
            0xFFFFFFFFu, 2);

    const uintptr_t pool = context + 0x2B8;
    receipt->pool = pool;
    if (!Readable(reinterpret_cast<const void*>(pool), 0x2C))
        return FailContactManagerOwner(receipt,
            ContactManagerOwnerUnreadablePool, ERROR_NOACCESS,
            0xFFFFFFFFu, 3);
    receipt->elementsPerSlab = *reinterpret_cast<const uint32_t*>(pool + 0x00);
    receipt->maximumSlabs = *reinterpret_cast<const uint32_t*>(pool + 0x04);
    receipt->slabCount = *reinterpret_cast<const uint32_t*>(pool + 0x08);
    receipt->log2ElementsPerSlab = *reinterpret_cast<const uint32_t*>(pool + 0x0C);
    receipt->freeArray = *reinterpret_cast<const uintptr_t*>(pool + 0x10);
    receipt->freeCount = *reinterpret_cast<const uint32_t*>(pool + 0x14);
    receipt->slabs = *reinterpret_cast<const uintptr_t*>(pool + 0x18);
    const uintptr_t argument = *reinterpret_cast<const uintptr_t*>(pool + 0x1C);
    if (receipt->elementsPerSlab != 256u || receipt->maximumSlabs != 4096u ||
        !receipt->slabCount || receipt->log2ElementsPerSlab != 8u ||
        argument != context || receipt->slabCount >
            kMaximumContactManagers / receipt->elementsPerSlab) {
        return FailContactManagerOwner(receipt, ContactManagerOwnerInvalidPool,
            ERROR_INVALID_DATA, 0xFFFFFFFFu, 4);
    }
    receipt->totalSlots = receipt->slabCount * receipt->elementsPerSlab;
    if (receipt->freeCount > receipt->totalSlots || !receipt->freeArray ||
        !receipt->slabs ||
        !Readable(reinterpret_cast<const void*>(receipt->slabs),
            receipt->slabCount * sizeof(uintptr_t)) ||
        !Readable(reinterpret_cast<const void*>(receipt->freeArray),
            receipt->freeCount * sizeof(uintptr_t))) {
        return FailContactManagerOwner(receipt, ContactManagerOwnerInvalidPool,
            ERROR_NOACCESS, 0xFFFFFFFFu, 5);
    }

    if (!ReadContactBitmap(pool + 0x20, receipt->totalSlots,
            receipt->useBitmap, receipt->useWordCount) ||
        !ReadContactBitmap(context + 0x534, receipt->totalSlots,
            receipt->activeBitmap, receipt->activeWordCount) ||
        !ReadContactBitmap(context + 0x540, receipt->totalSlots,
            receipt->touchBitmap, receipt->touchWordCount) ||
        !ReadContactBitmap(context + 0x16D0, receipt->totalSlots,
            receipt->modifiableBitmap, receipt->modifiableWordCount)) {
        return FailContactManagerOwner(receipt,
            ContactManagerOwnerUnreadableBitmap, ERROR_NOACCESS,
            0xFFFFFFFFu, 6);
    }

    uint8_t freeSlots[kMaximumContactManagers] = {};
    uint32_t freeIndexHash = 2166136261u;
    for (uint32_t i = 0; i < receipt->slabCount; ++i) {
        const uintptr_t slab = reinterpret_cast<const uintptr_t*>(
            receipt->slabs)[i];
        if (!slab || !Readable(reinterpret_cast<const void*>(slab),
                receipt->elementsPerSlab * kContactManagerSize))
            return FailContactManagerOwner(receipt,
                ContactManagerOwnerInvalidPool, ERROR_NOACCESS,
                i << 8, 7);
    }
    for (uint32_t i = 0; i < receipt->freeCount; ++i) {
        const uintptr_t manager = reinterpret_cast<const uintptr_t*>(
            receipt->freeArray)[i];
        if (!manager || !Readable(reinterpret_cast<const void*>(manager + 0x4C), 4))
            return FailContactManagerOwner(receipt,
                ContactManagerOwnerInvalidFreeEntry, ERROR_NOACCESS,
                0xFFFFFFFFu, 8);
        const uint32_t slot = *reinterpret_cast<const uint32_t*>(manager + 0x4C);
        if (slot >= receipt->totalSlots || freeSlots[slot])
            return FailContactManagerOwner(receipt,
                ContactManagerOwnerInvalidFreeEntry, ERROR_INVALID_DATA,
                slot, 9);
        const uintptr_t slab = reinterpret_cast<const uintptr_t*>(
            receipt->slabs)[slot >> 8];
        if (manager != slab + (slot & 0xFFu) * kContactManagerSize)
            return FailContactManagerOwner(receipt,
                ContactManagerOwnerInvalidFreeEntry, ERROR_INVALID_DATA,
                slot, 10);
        freeSlots[slot] = 1;
        freeIndexHash ^= slot;
        freeIndexHash *= 16777619u;
    }

    receipt->freeOrderHash = OrderHash(
        reinterpret_cast<const uintptr_t*>(receipt->freeArray),
        receipt->freeCount);
    receipt->freeIndexOrderHash = freeIndexHash;
    receipt->useBitmapHash = ContactBitmapHash(receipt->useBitmap,
        receipt->totalSlots);
    receipt->activeBitmapHash = ContactBitmapHash(receipt->activeBitmap,
        receipt->totalSlots);
    receipt->touchBitmapHash = ContactBitmapHash(receipt->touchBitmap,
        receipt->totalSlots);
    receipt->modifiableBitmapHash = ContactBitmapHash(
        receipt->modifiableBitmap, receipt->totalSlots);
    receipt->usedCount = ContactBitmapCount(receipt->useBitmap,
        receipt->totalSlots);
    receipt->activeCount = ContactBitmapCount(receipt->activeBitmap,
        receipt->totalSlots);
    receipt->touchCount = ContactBitmapCount(receipt->touchBitmap,
        receipt->totalSlots);
    receipt->modifiableCount = ContactBitmapCount(receipt->modifiableBitmap,
        receipt->totalSlots);
    receipt->recordsRequired = receipt->usedCount;
    if (capacity < receipt->recordsRequired ||
        (receipt->recordsRequired && !records) ||
        (records && !Writable(records,
            receipt->recordsRequired * sizeof(ContactManagerOwnerRecord))))
        return FailContactManagerOwner(receipt,
            ContactManagerOwnerCapacityTooSmall, ERROR_INSUFFICIENT_BUFFER,
            0xFFFFFFFFu, 11);
    if (receipt->freeCount + receipt->usedCount != receipt->totalSlots ||
        receipt->activeCount != receipt->usedCount)
        return FailContactManagerOwner(receipt,
            ContactManagerOwnerMembershipMismatch, ERROR_INVALID_STATE,
            0xFFFFFFFFu, 12);

    uint32_t ownerHash = 2166136261u;
    for (uint32_t slot = 0; slot < receipt->totalSlots; ++slot) {
        const bool used = ContactBitmapTest(receipt->useBitmap, slot);
        const bool active = ContactBitmapTest(receipt->activeBitmap, slot);
        const bool touch = ContactBitmapTest(receipt->touchBitmap, slot);
        const bool modifiable = ContactBitmapTest(
            receipt->modifiableBitmap, slot);
        if (used == (freeSlots[slot] != 0) || active != used ||
            (touch && !active) || (modifiable && !active))
            return FailContactManagerOwner(receipt,
                ContactManagerOwnerMembershipMismatch, ERROR_INVALID_STATE,
                slot, 13);
        const uintptr_t slab = reinterpret_cast<const uintptr_t*>(
            receipt->slabs)[slot >> 8];
        const uintptr_t manager = slab +
            (slot & 0xFFu) * kContactManagerSize;
        if (!Readable(reinterpret_cast<const void*>(manager + 0x4C), 4) ||
            *reinterpret_cast<const uint32_t*>(manager + 0x4C) != slot)
            return FailContactManagerOwner(receipt,
                ContactManagerOwnerInvalidManager, ERROR_INVALID_DATA,
                slot, 14);
        if (!used) continue;
        const bool managerModifiable =
            (*reinterpret_cast<const uint32_t*>(manager + 0x08) & 1u) != 0;
        if (managerModifiable != modifiable)
            return FailContactManagerOwner(receipt,
                ContactManagerOwnerMembershipMismatch, ERROR_INVALID_STATE,
                slot, 15);
        const uint32_t membership = 1u | 2u | (touch ? 4u : 0u) |
            (modifiable ? 8u : 0u);
        ContactManagerOwnerRecord& record = records[receipt->recordsWritten];
        if (!ReadContactManagerOwner(unityBase, manager, slot, membership,
                record, receipt))
            return 0;
        ownerHash = AppendByteHash(ownerHash, &record, sizeof(record));
        ++receipt->recordsWritten;
    }
    receipt->ownerHash = ownerHash;

    // Repeat every externally mutable header/hash and every active owner read.
    // A diagnostic sample is rejected rather than mixing two physics phases.
    if (*reinterpret_cast<const uint32_t*>(pool + 0x00) !=
            receipt->elementsPerSlab ||
        *reinterpret_cast<const uint32_t*>(pool + 0x04) !=
            receipt->maximumSlabs ||
        *reinterpret_cast<const uint32_t*>(pool + 0x08) != receipt->slabCount ||
        *reinterpret_cast<const uint32_t*>(pool + 0x0C) !=
            receipt->log2ElementsPerSlab ||
        *reinterpret_cast<const uintptr_t*>(pool + 0x10) != receipt->freeArray ||
        *reinterpret_cast<const uint32_t*>(pool + 0x14) != receipt->freeCount ||
        *reinterpret_cast<const uintptr_t*>(pool + 0x18) != receipt->slabs ||
        OrderHash(reinterpret_cast<const uintptr_t*>(receipt->freeArray),
            receipt->freeCount) != receipt->freeOrderHash ||
        ContactBitmapHash(receipt->useBitmap, receipt->totalSlots) !=
            receipt->useBitmapHash ||
        ContactBitmapHash(receipt->activeBitmap, receipt->totalSlots) !=
            receipt->activeBitmapHash ||
        ContactBitmapHash(receipt->touchBitmap, receipt->totalSlots) !=
            receipt->touchBitmapHash ||
        ContactBitmapHash(receipt->modifiableBitmap, receipt->totalSlots) !=
            receipt->modifiableBitmapHash)
        return FailContactManagerOwner(receipt, ContactManagerOwnerUnstable,
            ERROR_RETRY, 0xFFFFFFFFu, 16);

    uint32_t secondHash = 2166136261u;
    uint32_t secondCount = 0;
    for (uint32_t slot = 0; slot < receipt->totalSlots; ++slot) {
        if (!ContactBitmapTest(receipt->useBitmap, slot)) continue;
        const uintptr_t slab = reinterpret_cast<const uintptr_t*>(
            receipt->slabs)[slot >> 8];
        const uintptr_t manager = slab +
            (slot & 0xFFu) * kContactManagerSize;
        const bool touch = ContactBitmapTest(receipt->touchBitmap, slot);
        const bool modifiable = ContactBitmapTest(
            receipt->modifiableBitmap, slot);
        ContactManagerOwnerRecord check = {};
        const uint32_t membership = 1u | 2u | (touch ? 4u : 0u) |
            (modifiable ? 8u : 0u);
        if (!ReadContactManagerOwner(unityBase, manager, slot, membership,
                check, receipt))
            return 0;
        secondHash = AppendByteHash(secondHash, &check, sizeof(check));
        ++secondCount;
    }
    if (secondCount != receipt->recordsWritten ||
        secondHash != receipt->ownerHash)
        return FailContactManagerOwner(receipt, ContactManagerOwnerUnstable,
            ERROR_RETRY, 0xFFFFFFFFu, 17);

    receipt->consistencyFlags = 0x00FFu;
    receipt->result = ContactManagerOwnerOk;
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

static bool ShapeInstancePairPoolRevisionMatches(uintptr_t unityBase) {
    if (!unityBase) return false;
    const void* entry = reinterpret_cast<const void*>(
        unityBase + kCreateShapeInstancePairRva);
    const void* allocation = reinterpret_cast<const void*>(
        unityBase + kCreateShapeInstancePairRva + 0x65);
    const bool entryMatches =
        Readable(entry, sizeof(kCreateShapeInstancePairBytes)) &&
        (EqualBytes(entry, kCreateShapeInstancePairBytes,
            sizeof(kCreateShapeInstancePairBytes)) ||
         (g_contactManagerContextObserverInstalled &&
            unityBase == g_observerUnityBase &&
            HasShapeInstancePairJump(entry)));
    return entryMatches &&
        Readable(allocation, sizeof(kCreateShapeInstancePairPoolBytes)) &&
        EqualBytes(allocation, kCreateShapeInstancePairPoolBytes,
            sizeof(kCreateShapeInstancePairPoolBytes));
}

static int CaptureShapeInstancePairPool(uintptr_t unityBase,
    uintptr_t nphaseCore, uintptr_t* snapshot, uint32_t capacity,
    SipPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeSipPoolReceipt(receipt, unityBase, nphaseCore);
    if (!unityBase || !nphaseCore)
        return FailSipPool(receipt, SipPoolBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!ShapeInstancePairPoolRevisionMatches(unityBase))
        return FailSipPool(receipt, SipPoolRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    const uintptr_t pool = nphaseCore + 0x2E0;
    if (!Readable(reinterpret_cast<const void*>(pool + 0x114), 0x14))
        return FailSipPool(receipt, SipPoolUnreadable, ERROR_NOACCESS);
    receipt->elementsPerSlab = *reinterpret_cast<const uint32_t*>(
        pool + 0x114);
    receipt->used = *reinterpret_cast<const uint32_t*>(pool + 0x118);
    receipt->unreleased = *reinterpret_cast<const uint32_t*>(pool + 0x11C);
    receipt->slabSize = *reinterpret_cast<const uint32_t*>(pool + 0x120);
    receipt->freeHead = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    if (receipt->elementsPerSlab != 32u || receipt->slabSize != 0x880u ||
        receipt->used > kMaximumShapeInstancePairs ||
        receipt->unreleased > kMaximumShapeInstancePairs ||
        receipt->used + receipt->unreleased >
            kMaximumShapeInstancePairs ||
        ((receipt->used + receipt->unreleased) & 31u) != 0)
        return FailSipPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_DATA);

    uintptr_t order[kMaximumShapeInstancePairs] = {};
    receipt->orderHash = 2166136261u;
    uintptr_t current = receipt->freeHead;
    while (current) {
        const uint32_t index = receipt->traversedCount;
        if (index >= kMaximumShapeInstancePairs)
            return FailSipPool(receipt, SipPoolInvalidMetadata,
                ERROR_INSUFFICIENT_BUFFER);
        if (!Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t)))
            return FailSipPool(receipt, SipPoolInvalidNode,
                ERROR_NOACCESS);
        for (uint32_t i = 0; i < index; ++i)
            if (order[i] == current)
                return FailSipPool(receipt, SipPoolDuplicateNode,
                    ERROR_DUP_NAME);
        order[index] = current;
        if (index < 16) receipt->top[index] = current;
        receipt->orderHash ^= static_cast<uint32_t>(current);
        receipt->orderHash *= 16777619u;
        ++receipt->traversedCount;
        current = *reinterpret_cast<const uintptr_t*>(current);
    }
    if (receipt->traversedCount != receipt->unreleased)
        return FailSipPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_STATE);
    if (capacity < receipt->traversedCount)
        return FailSipPool(receipt, SipPoolCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER);
    if (receipt->traversedCount && (!snapshot ||
        !Writable(snapshot, receipt->traversedCount * sizeof(uintptr_t))))
        return FailSipPool(receipt, SipPoolBadArgument, ERROR_NOACCESS);
    if (receipt->traversedCount)
        CopyWords(snapshot, order, receipt->traversedCount);
    receipt->validationFlags = 0x1Fu;
    receipt->result = SipPoolOk;
    return 1;
}

static bool ContainsPointer(const uintptr_t* values, uint32_t count,
    uintptr_t value) {
    for (uint32_t i = 0; i < count; ++i)
        if (values[i] == value) return true;
    return false;
}

static bool UniqueNonzeroPointers(const uintptr_t* values, uint32_t count) {
    for (uint32_t i = 0; i < count; ++i) {
        if (!values[i]) return false;
        for (uint32_t j = 0; j < i; ++j)
            if (values[i] == values[j]) return false;
    }
    return true;
}

static bool ActorPairPoolRevisionMatches(uintptr_t unityBase) {
    if (!unityBase) return false;
    const void* entry = reinterpret_cast<const void*>(
        unityBase + kFindActorPairRva);
    const void* pool = reinterpret_cast<const void*>(
        unityBase + kFindActorPairRva + 0x91);
    const void* stride = reinterpret_cast<const void*>(
        unityBase + 0xA4D9BA);
    return Readable(entry, sizeof(kFindActorPairBytes)) &&
        EqualBytes(entry, kFindActorPairBytes,
            sizeof(kFindActorPairBytes)) &&
        Readable(pool, sizeof(kFindActorPairPoolBytes)) &&
        EqualBytes(pool, kFindActorPairPoolBytes,
            sizeof(kFindActorPairPoolBytes)) &&
        Readable(stride, sizeof(kActorPairSlabStrideBytes)) &&
        EqualBytes(stride, kActorPairSlabStrideBytes,
            sizeof(kActorPairSlabStrideBytes));
}

static bool ActorPairPoolElement(const uintptr_t* slabs,
    uint32_t slabCount, uintptr_t value, uint32_t& slabIndex,
    uint32_t& elementIndex) {
    slabIndex = 0xFFFFFFFFu;
    elementIndex = 0xFFFFFFFFu;
    for (uint32_t slab = 0; slab < slabCount; ++slab) {
        const uintptr_t begin = slabs[slab];
        const uintptr_t end = begin + 0x300u;
        if (value < begin || value >= end) continue;
        const uintptr_t delta = value - begin;
        if ((delta % 0x18u) != 0) return false;
        slabIndex = slab;
        elementIndex = static_cast<uint32_t>(delta / 0x18u);
        return elementIndex < 32u;
    }
    return false;
}

static int CaptureActorPairPool(uintptr_t unityBase,
    uintptr_t nphaseCore, uintptr_t* freeSnapshot, uint32_t freeCapacity,
    uintptr_t* allocatedSnapshot, uint32_t allocatedCapacity,
    ActorPairPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeActorPairPoolReceipt(receipt, unityBase, nphaseCore);
    if (!unityBase || !nphaseCore)
        return FailActorPairPool(receipt, SipPoolBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!ActorPairPoolRevisionMatches(unityBase))
        return FailActorPairPool(receipt, SipPoolRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    const uintptr_t pool = nphaseCore + 0x90;
    if (!Readable(reinterpret_cast<const void*>(pool + 0x108), 0x20))
        return FailActorPairPool(receipt, SipPoolUnreadable,
            ERROR_NOACCESS);
    receipt->slabs = *reinterpret_cast<const uintptr_t*>(pool + 0x108);
    receipt->slabCount = *reinterpret_cast<const uint32_t*>(pool + 0x10C);
    const uint32_t rawSlabCapacity =
        *reinterpret_cast<const uint32_t*>(pool + 0x110);
    receipt->elementsPerSlab = *reinterpret_cast<const uint32_t*>(
        pool + 0x114);
    receipt->used = *reinterpret_cast<const uint32_t*>(pool + 0x118);
    receipt->unreleased = *reinterpret_cast<const uint32_t*>(pool + 0x11C);
    receipt->slabSize = *reinterpret_cast<const uint32_t*>(pool + 0x120);
    receipt->freeHead = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    const uint32_t slabCapacity = rawSlabCapacity & 0x7FFFFFFFu;
    if (receipt->elementsPerSlab != 32u ||
        receipt->slabSize != 0x300u || receipt->slabCount > 128u ||
        receipt->slabCount > slabCapacity ||
        (receipt->slabCount && (!receipt->slabs ||
            !Readable(reinterpret_cast<const void*>(receipt->slabs),
                receipt->slabCount * sizeof(uintptr_t)))))
        return FailActorPairPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_DATA);
    receipt->totalElements = receipt->slabCount * 32u;
    if (receipt->totalElements > kMaximumShapeInstancePairs ||
        receipt->used > receipt->totalElements ||
        receipt->unreleased > receipt->totalElements ||
        receipt->used + receipt->unreleased != receipt->totalElements)
        return FailActorPairPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_DATA);

    uintptr_t freeOrder[kMaximumShapeInstancePairs] = {};
    const uintptr_t* slabs = reinterpret_cast<const uintptr_t*>(
        receipt->slabs);
    receipt->freeOrderHash = 2166136261u;
    uintptr_t current = receipt->freeHead;
    while (current) {
        const uint32_t index = receipt->freeCount;
        if (index >= receipt->totalElements)
            return FailActorPairPool(receipt, SipPoolInvalidMetadata,
                ERROR_INSUFFICIENT_BUFFER);
        if (!Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t)))
            return FailActorPairPool(receipt, SipPoolInvalidNode,
                ERROR_NOACCESS);
        uint32_t slabIndex = 0, elementIndex = 0;
        if (!ActorPairPoolElement(slabs, receipt->slabCount, current,
                slabIndex, elementIndex))
            return FailActorPairPool(receipt, SipPoolInvalidNode,
                ERROR_INVALID_ADDRESS);
        for (uint32_t i = 0; i < index; ++i)
            if (freeOrder[i] == current)
                return FailActorPairPool(receipt, SipPoolDuplicateNode,
                    ERROR_DUP_NAME);
        freeOrder[index] = current;
        if (index < 16u) receipt->topFree[index] = current;
        receipt->freeOrderHash ^= static_cast<uint32_t>(current);
        receipt->freeOrderHash *= 16777619u;
        ++receipt->freeCount;
        current = *reinterpret_cast<const uintptr_t*>(current);
    }
    if (receipt->freeCount != receipt->unreleased ||
        freeCapacity < receipt->freeCount ||
        allocatedCapacity < receipt->used)
        return FailActorPairPool(receipt,
            freeCapacity < receipt->freeCount ||
                allocatedCapacity < receipt->used ?
                    SipPoolCapacityTooSmall : SipPoolInvalidMetadata,
            freeCapacity < receipt->freeCount ||
                allocatedCapacity < receipt->used ?
                    ERROR_INSUFFICIENT_BUFFER : ERROR_INVALID_STATE);
    if ((receipt->freeCount && (!freeSnapshot ||
            !Writable(freeSnapshot,
                receipt->freeCount * sizeof(uintptr_t)))) ||
        (receipt->used && (!allocatedSnapshot ||
            !Writable(allocatedSnapshot,
                receipt->used * sizeof(uintptr_t)))))
        return FailActorPairPool(receipt, SipPoolBadArgument,
            ERROR_NOACCESS);

    receipt->allocatedOrderHash = 2166136261u;
    for (uint32_t slab = 0; slab < receipt->slabCount; ++slab) {
        const uintptr_t begin = slabs[slab];
        if (!begin || !Readable(reinterpret_cast<const void*>(begin),
                receipt->slabSize))
            return FailActorPairPool(receipt, SipPoolInvalidNode,
                ERROR_NOACCESS);
        for (uint32_t element = 0; element < 32u; ++element) {
            const uintptr_t value = begin + element * 0x18u;
            if (ContainsPointer(freeOrder, receipt->freeCount, value))
                continue;
            if (receipt->allocatedCount >= receipt->used)
                return FailActorPairPool(receipt,
                    SipPoolInvalidMetadata, ERROR_INVALID_STATE);
            allocatedSnapshot[receipt->allocatedCount] = value;
            if (receipt->allocatedCount < 16u)
                receipt->topAllocated[receipt->allocatedCount] = value;
            receipt->allocatedOrderHash ^=
                static_cast<uint32_t>(value);
            receipt->allocatedOrderHash *= 16777619u;
            ++receipt->allocatedCount;
        }
    }
    if (receipt->allocatedCount != receipt->used)
        return FailActorPairPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_STATE);
    if (receipt->freeCount)
        CopyWords(freeSnapshot, freeOrder, receipt->freeCount);
    receipt->validationFlags = 0xFFu;
    receipt->result = SipPoolOk;
    return 1;
}

static bool ActorPairReportPoolRevisionMatches(uintptr_t unityBase) {
    if (!unityBase) return false;
    const void* create = reinterpret_cast<const void*>(
        unityBase + kCreateActorPairReportDataRva);
    const void* stride = reinterpret_cast<const void*>(
        unityBase + kActorPairReportSlabStrideRva);
    const void* release = reinterpret_cast<const void*>(
        unityBase + kReleaseActorPairReportDataRva);
    return Readable(create, sizeof(kCreateActorPairReportDataBytes)) &&
        EqualBytes(create, kCreateActorPairReportDataBytes,
            sizeof(kCreateActorPairReportDataBytes)) &&
        Readable(stride, sizeof(kActorPairReportSlabStrideBytes)) &&
        EqualBytes(stride, kActorPairReportSlabStrideBytes,
            sizeof(kActorPairReportSlabStrideBytes)) &&
        Readable(release, sizeof(kReleaseActorPairReportDataBytes)) &&
        EqualBytes(release, kReleaseActorPairReportDataBytes,
            sizeof(kReleaseActorPairReportDataBytes));
}

static bool ActorPairReportPoolElement(const uintptr_t* slabs,
    uint32_t slabCount, uintptr_t value, uint32_t& slabIndex,
    uint32_t& elementIndex) {
    slabIndex = 0xFFFFFFFFu;
    elementIndex = 0xFFFFFFFFu;
    for (uint32_t slab = 0; slab < slabCount; ++slab) {
        const uintptr_t begin = slabs[slab];
        const uintptr_t end = begin + 0x480u;
        if (value < begin || value >= end) continue;
        const uintptr_t delta = value - begin;
        if ((delta % 0x24u) != 0) return false;
        slabIndex = slab;
        elementIndex = static_cast<uint32_t>(delta / 0x24u);
        return elementIndex < 32u;
    }
    return false;
}

static int CaptureActorPairReportPool(uintptr_t unityBase,
    uintptr_t nphaseCore, uintptr_t* freeSnapshot, uint32_t freeCapacity,
    uintptr_t* allocatedSnapshot, uint32_t allocatedCapacity,
    ActorPairReportPoolReceipt* receipt) {
    if (!receipt) return 0;
    InitializeActorPairReportPoolReceipt(receipt, unityBase, nphaseCore);
    if (!unityBase || !nphaseCore)
        return FailActorPairReportPool(receipt, SipPoolBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!ActorPairReportPoolRevisionMatches(unityBase))
        return FailActorPairReportPool(receipt, SipPoolRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    const uintptr_t pool = nphaseCore + 0x530;
    if (!Readable(reinterpret_cast<const void*>(pool + 0x108), 0x20))
        return FailActorPairReportPool(receipt, SipPoolUnreadable,
            ERROR_NOACCESS);
    receipt->slabs = *reinterpret_cast<const uintptr_t*>(pool + 0x108);
    receipt->slabCount = *reinterpret_cast<const uint32_t*>(pool + 0x10C);
    const uint32_t rawSlabCapacity =
        *reinterpret_cast<const uint32_t*>(pool + 0x110);
    receipt->elementsPerSlab = *reinterpret_cast<const uint32_t*>(
        pool + 0x114);
    receipt->used = *reinterpret_cast<const uint32_t*>(pool + 0x118);
    receipt->unreleased = *reinterpret_cast<const uint32_t*>(pool + 0x11C);
    receipt->slabSize = *reinterpret_cast<const uint32_t*>(pool + 0x120);
    receipt->freeHead = *reinterpret_cast<const uintptr_t*>(pool + 0x124);
    const uint32_t slabCapacity = rawSlabCapacity & 0x7FFFFFFFu;
    if (receipt->elementsPerSlab != 32u ||
        receipt->slabSize != 0x480u || receipt->slabCount > 128u ||
        receipt->slabCount > slabCapacity ||
        (receipt->slabCount && (!receipt->slabs ||
            !Readable(reinterpret_cast<const void*>(receipt->slabs),
                receipt->slabCount * sizeof(uintptr_t)))))
        return FailActorPairReportPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_DATA);
    receipt->totalElements = receipt->slabCount * 32u;
    if (receipt->totalElements > kMaximumShapeInstancePairs ||
        receipt->used > receipt->totalElements ||
        receipt->unreleased > receipt->totalElements ||
        receipt->used + receipt->unreleased != receipt->totalElements)
        return FailActorPairReportPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_DATA);

    uintptr_t freeOrder[kMaximumShapeInstancePairs] = {};
    const uintptr_t* slabs = reinterpret_cast<const uintptr_t*>(
        receipt->slabs);
    receipt->freeOrderHash = 2166136261u;
    uintptr_t current = receipt->freeHead;
    while (current) {
        const uint32_t index = receipt->freeCount;
        if (index >= receipt->totalElements)
            return FailActorPairReportPool(receipt,
                SipPoolInvalidMetadata, ERROR_INSUFFICIENT_BUFFER);
        if (!Readable(reinterpret_cast<const void*>(current),
                sizeof(uintptr_t)))
            return FailActorPairReportPool(receipt, SipPoolInvalidNode,
                ERROR_NOACCESS);
        uint32_t slabIndex = 0, elementIndex = 0;
        if (!ActorPairReportPoolElement(slabs, receipt->slabCount,
                current, slabIndex, elementIndex))
            return FailActorPairReportPool(receipt, SipPoolInvalidNode,
                ERROR_INVALID_ADDRESS);
        for (uint32_t i = 0; i < index; ++i)
            if (freeOrder[i] == current)
                return FailActorPairReportPool(receipt,
                    SipPoolDuplicateNode, ERROR_DUP_NAME);
        freeOrder[index] = current;
        if (index < 16u) receipt->topFree[index] = current;
        receipt->freeOrderHash ^= static_cast<uint32_t>(current);
        receipt->freeOrderHash *= 16777619u;
        ++receipt->freeCount;
        current = *reinterpret_cast<const uintptr_t*>(current);
    }
    if (receipt->freeCount != receipt->unreleased ||
        freeCapacity < receipt->freeCount ||
        allocatedCapacity < receipt->used)
        return FailActorPairReportPool(receipt,
            freeCapacity < receipt->freeCount ||
                allocatedCapacity < receipt->used ?
                    SipPoolCapacityTooSmall : SipPoolInvalidMetadata,
            freeCapacity < receipt->freeCount ||
                allocatedCapacity < receipt->used ?
                    ERROR_INSUFFICIENT_BUFFER : ERROR_INVALID_STATE);
    if ((receipt->freeCount && (!freeSnapshot ||
            !Writable(freeSnapshot,
                receipt->freeCount * sizeof(uintptr_t)))) ||
        (receipt->used && (!allocatedSnapshot ||
            !Writable(allocatedSnapshot,
                receipt->used * sizeof(uintptr_t)))))
        return FailActorPairReportPool(receipt, SipPoolBadArgument,
            ERROR_NOACCESS);

    receipt->allocatedOrderHash = 2166136261u;
    for (uint32_t slab = 0; slab < receipt->slabCount; ++slab) {
        const uintptr_t begin = slabs[slab];
        if (!begin || !Readable(reinterpret_cast<const void*>(begin),
                receipt->slabSize))
            return FailActorPairReportPool(receipt, SipPoolInvalidNode,
                ERROR_NOACCESS);
        for (uint32_t element = 0; element < 32u; ++element) {
            const uintptr_t value = begin + element * 0x24u;
            if (ContainsPointer(freeOrder, receipt->freeCount, value))
                continue;
            if (receipt->allocatedCount >= receipt->used)
                return FailActorPairReportPool(receipt,
                    SipPoolInvalidMetadata, ERROR_INVALID_STATE);
            allocatedSnapshot[receipt->allocatedCount] = value;
            if (receipt->allocatedCount < 16u)
                receipt->topAllocated[receipt->allocatedCount] = value;
            receipt->allocatedOrderHash ^=
                static_cast<uint32_t>(value);
            receipt->allocatedOrderHash *= 16777619u;
            ++receipt->allocatedCount;
        }
    }
    if (receipt->allocatedCount != receipt->used)
        return FailActorPairReportPool(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_STATE);
    if (receipt->freeCount)
        CopyWords(freeSnapshot, freeOrder, receipt->freeCount);
    receipt->validationFlags = 0xFFu;
    receipt->result = SipPoolOk;
    return 1;
}

static bool NPhaseReportStateRevisionMatches(uintptr_t unityBase) {
    if (!unityBase) return false;
    const void* addPersistent = reinterpret_cast<const void*>(
        unityBase + kAddPersistentContactEventPairRva);
    const void* removePersistent = reinterpret_cast<const void*>(
        unityBase + kRemovePersistentContactEventPairRva);
    const void* allocateBuffer = reinterpret_cast<const void*>(
        unityBase + kContactReportBufferAllocateRva);
    const void* bufferLayout = reinterpret_cast<const void*>(
        unityBase + kContactReportBufferAllocateRva + 0x1F);
    return Readable(addPersistent,
            sizeof(kAddPersistentContactEventPairBytes)) &&
        EqualBytes(addPersistent, kAddPersistentContactEventPairBytes,
            sizeof(kAddPersistentContactEventPairBytes)) &&
        Readable(removePersistent,
            sizeof(kRemovePersistentContactEventPairBytes)) &&
        EqualBytes(removePersistent,
            kRemovePersistentContactEventPairBytes,
            sizeof(kRemovePersistentContactEventPairBytes)) &&
        Readable(allocateBuffer,
            sizeof(kContactReportBufferAllocateBytes)) &&
        EqualBytes(allocateBuffer, kContactReportBufferAllocateBytes,
            sizeof(kContactReportBufferAllocateBytes)) &&
        Readable(bufferLayout,
            sizeof(kContactReportBufferLayoutBytes)) &&
        EqualBytes(bufferLayout, kContactReportBufferLayoutBytes,
            sizeof(kContactReportBufferLayoutBytes));
}

static bool UniquePointers(const uintptr_t* values, uint32_t count) {
    for (uint32_t i = 0; i < count; ++i) {
        if (!values[i]) return false;
        for (uint32_t j = 0; j < i; ++j)
            if (values[i] == values[j]) return false;
    }
    return true;
}

static int CaptureNPhaseReportState(uintptr_t unityBase,
    uintptr_t nphaseCore, uintptr_t* actorPairs,
    uint32_t actorPairCapacity, uintptr_t* persistentSips,
    uint32_t persistentCapacity, uintptr_t* forceThresholdSips,
    uint32_t forceThresholdCapacity, uint8_t* reportBufferBytes,
    uint32_t reportBufferCapacity, NPhaseReportStateReceipt* receipt) {
    if (!receipt) return 0;
    InitializeNPhaseReportStateReceipt(receipt, unityBase, nphaseCore);
    if (!unityBase || !nphaseCore)
        return FailNPhaseReportState(receipt, SipPoolBadArgument,
            ERROR_INVALID_PARAMETER);
    if (!NPhaseReportStateRevisionMatches(unityBase))
        return FailNPhaseReportState(receipt, SipPoolRevisionMismatch,
            ERROR_REVISION_MISMATCH);
    if (!Readable(reinterpret_cast<const void*>(nphaseCore), 0x44))
        return FailNPhaseReportState(receipt, SipPoolUnreadable,
            ERROR_NOACCESS);

    receipt->ownerScene = *reinterpret_cast<const uintptr_t*>(nphaseCore);
    receipt->actorPairData = *reinterpret_cast<const uintptr_t*>(
        nphaseCore + 0x04);
    receipt->actorPairCount = *reinterpret_cast<const uint32_t*>(
        nphaseCore + 0x08);
    receipt->actorPairCapacityRaw = *reinterpret_cast<const uint32_t*>(
        nphaseCore + 0x0C);
    receipt->persistentData = *reinterpret_cast<const uintptr_t*>(
        nphaseCore + 0x10);
    receipt->persistentCount = *reinterpret_cast<const uint32_t*>(
        nphaseCore + 0x14);
    receipt->persistentCapacityRaw = *reinterpret_cast<const uint32_t*>(
        nphaseCore + 0x18);
    receipt->nextFramePersistentIndex =
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x1C);
    receipt->forceThresholdData = *reinterpret_cast<const uintptr_t*>(
        nphaseCore + 0x20);
    receipt->forceThresholdCount = *reinterpret_cast<const uint32_t*>(
        nphaseCore + 0x24);
    receipt->forceThresholdCapacityRaw =
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x28);
    receipt->reportBuffer = *reinterpret_cast<const uintptr_t*>(
        nphaseCore + 0x2C);
    receipt->reportBufferCurrentIndex =
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x30);
    receipt->reportBufferCurrentSize =
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x34);
    receipt->reportBufferDefaultSize =
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x38);
    receipt->reportBufferLastIndex =
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x3C);
    receipt->reportBufferAllocationLocked =
        *reinterpret_cast<const uint8_t*>(nphaseCore + 0x40);

    uint32_t error = 0;
    if (!receipt->ownerScene ||
        !ReadPointerArray(receipt->actorPairData,
            receipt->actorPairCount, receipt->actorPairCapacityRaw, &error) ||
        !ReadPointerArray(receipt->persistentData,
            receipt->persistentCount, receipt->persistentCapacityRaw,
            &error) ||
        !ReadPointerArray(receipt->forceThresholdData,
            receipt->forceThresholdCount,
            receipt->forceThresholdCapacityRaw, &error) ||
        receipt->nextFramePersistentIndex > receipt->persistentCount)
        return FailNPhaseReportState(receipt, SipPoolInvalidMetadata,
            error ? error : ERROR_INVALID_DATA);
    if (receipt->actorPairCount > actorPairCapacity ||
        receipt->persistentCount > persistentCapacity ||
        receipt->forceThresholdCount > forceThresholdCapacity ||
        receipt->reportBufferCurrentSize > reportBufferCapacity)
        return FailNPhaseReportState(receipt, SipPoolCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER);
    if ((receipt->actorPairCount && (!actorPairs ||
            !Writable(actorPairs, receipt->actorPairCount *
                sizeof(uintptr_t)))) ||
        (receipt->persistentCount && (!persistentSips ||
            !Writable(persistentSips, receipt->persistentCount *
                sizeof(uintptr_t)))) ||
        (receipt->forceThresholdCount && (!forceThresholdSips ||
            !Writable(forceThresholdSips, receipt->forceThresholdCount *
                sizeof(uintptr_t)))) ||
        (receipt->reportBufferCurrentSize && (!reportBufferBytes ||
            !Writable(reportBufferBytes,
                receipt->reportBufferCurrentSize))))
        return FailNPhaseReportState(receipt, SipPoolBadArgument,
            ERROR_NOACCESS);

    const uintptr_t* sourceActorPairs =
        reinterpret_cast<const uintptr_t*>(receipt->actorPairData);
    const uintptr_t* sourcePersistent =
        reinterpret_cast<const uintptr_t*>(receipt->persistentData);
    const uintptr_t* sourceForce =
        reinterpret_cast<const uintptr_t*>(receipt->forceThresholdData);
    if (!UniquePointers(sourceActorPairs, receipt->actorPairCount) ||
        !UniquePointers(sourcePersistent, receipt->persistentCount) ||
        !UniquePointers(sourceForce, receipt->forceThresholdCount))
        return FailNPhaseReportState(receipt, SipPoolDuplicateNode,
            ERROR_DUP_NAME);
    for (uint32_t i = 0; i < receipt->actorPairCount; ++i) {
        const uintptr_t pair = sourceActorPairs[i];
        if (!Readable(reinterpret_cast<const void*>(pair), 0x18) ||
            ((*reinterpret_cast<const uint16_t*>(pair + 0x0C)) & 1u) == 0)
            return FailNPhaseReportState(receipt, SipPoolInvalidNode,
                ERROR_INVALID_DATA);
    }
    for (uint32_t i = 0; i < receipt->persistentCount; ++i) {
        const uintptr_t sip = sourcePersistent[i];
        if (!Readable(reinterpret_cast<const void*>(sip), 0x44) ||
            ((*reinterpret_cast<const uint32_t*>(sip + 0x2C)) &
                0x00200000u) == 0 ||
            ((*reinterpret_cast<const uint32_t*>(sip + 0x2C)) &
                0x00800000u) != 0 ||
            *reinterpret_cast<const uint32_t*>(sip + 0x34) != i)
            return FailNPhaseReportState(receipt, SipPoolInvalidNode,
                ERROR_INVALID_DATA);
    }
    for (uint32_t i = 0; i < receipt->forceThresholdCount; ++i) {
        const uintptr_t sip = sourceForce[i];
        if (!Readable(reinterpret_cast<const void*>(sip), 0x44) ||
            ((*reinterpret_cast<const uint32_t*>(sip + 0x2C)) &
                0x00800000u) == 0 ||
            ((*reinterpret_cast<const uint32_t*>(sip + 0x2C)) &
                0x00200000u) != 0 ||
            *reinterpret_cast<const uint32_t*>(sip + 0x34) != i)
            return FailNPhaseReportState(receipt, SipPoolInvalidNode,
                ERROR_INVALID_DATA);
        if (ContainsPointer(sourcePersistent, receipt->persistentCount, sip))
            return FailNPhaseReportState(receipt, SipPoolDuplicateNode,
                ERROR_DUP_NAME);
    }

    if (!receipt->reportBuffer || !receipt->reportBufferCurrentSize ||
        !receipt->reportBufferDefaultSize ||
        receipt->reportBufferCurrentSize > 0x04000000u ||
        receipt->reportBufferDefaultSize >
            receipt->reportBufferCurrentSize ||
        receipt->reportBufferCurrentIndex >
            receipt->reportBufferCurrentSize ||
        receipt->reportBufferAllocationLocked > 1u ||
        (receipt->reportBufferLastIndex != 0xFFFFFFFFu &&
            receipt->reportBufferLastIndex >=
                receipt->reportBufferCurrentIndex) ||
        (receipt->reportBufferCurrentSize &&
            !Readable(reinterpret_cast<const void*>(receipt->reportBuffer),
                receipt->reportBufferCurrentSize)))
        return FailNPhaseReportState(receipt, SipPoolInvalidMetadata,
            ERROR_INVALID_DATA);

    receipt->actorPairOrderHash = OrderHash(sourceActorPairs,
        receipt->actorPairCount);
    receipt->persistentOrderHash = OrderHash(sourcePersistent,
        receipt->persistentCount);
    receipt->forceThresholdOrderHash = OrderHash(sourceForce,
        receipt->forceThresholdCount);
    receipt->reportBufferActiveHash = receipt->reportBufferCurrentIndex ?
        ByteHash(reinterpret_cast<const void*>(receipt->reportBuffer),
            receipt->reportBufferCurrentIndex) : 2166136261u;
    receipt->reportBufferAllocationHash = ByteHash(
        reinterpret_cast<const void*>(receipt->reportBuffer),
        receipt->reportBufferCurrentSize);
    if (receipt->actorPairCount)
        CopyWords(actorPairs, sourceActorPairs, receipt->actorPairCount);
    if (receipt->persistentCount)
        CopyWords(persistentSips, sourcePersistent,
            receipt->persistentCount);
    if (receipt->forceThresholdCount)
        CopyWords(forceThresholdSips, sourceForce,
            receipt->forceThresholdCount);
    if (receipt->reportBufferCurrentSize)
        CopyBytes(reportBufferBytes,
            reinterpret_cast<const void*>(receipt->reportBuffer),
            receipt->reportBufferCurrentSize);

    if (*reinterpret_cast<const uintptr_t*>(nphaseCore) !=
            receipt->ownerScene ||
        *reinterpret_cast<const uintptr_t*>(nphaseCore + 0x04) !=
            receipt->actorPairData ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x08) !=
            receipt->actorPairCount ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x0C) !=
            receipt->actorPairCapacityRaw ||
        *reinterpret_cast<const uintptr_t*>(nphaseCore + 0x10) !=
            receipt->persistentData ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x14) !=
            receipt->persistentCount ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x18) !=
            receipt->persistentCapacityRaw ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x1C) !=
            receipt->nextFramePersistentIndex ||
        *reinterpret_cast<const uintptr_t*>(nphaseCore + 0x20) !=
            receipt->forceThresholdData ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x24) !=
            receipt->forceThresholdCount ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x28) !=
            receipt->forceThresholdCapacityRaw ||
        *reinterpret_cast<const uintptr_t*>(nphaseCore + 0x2C) !=
            receipt->reportBuffer ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x30) !=
            receipt->reportBufferCurrentIndex ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x34) !=
            receipt->reportBufferCurrentSize ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x38) !=
            receipt->reportBufferDefaultSize ||
        *reinterpret_cast<const uint32_t*>(nphaseCore + 0x3C) !=
            receipt->reportBufferLastIndex ||
        *reinterpret_cast<const uint8_t*>(nphaseCore + 0x40) !=
            receipt->reportBufferAllocationLocked ||
        OrderHash(sourceActorPairs, receipt->actorPairCount) !=
            receipt->actorPairOrderHash ||
        OrderHash(sourcePersistent, receipt->persistentCount) !=
            receipt->persistentOrderHash ||
        OrderHash(sourceForce, receipt->forceThresholdCount) !=
            receipt->forceThresholdOrderHash ||
        (receipt->reportBufferCurrentIndex ?
            ByteHash(reinterpret_cast<const void*>(receipt->reportBuffer),
                receipt->reportBufferCurrentIndex) : 2166136261u) !=
            receipt->reportBufferActiveHash ||
        ByteHash(reinterpret_cast<const void*>(receipt->reportBuffer),
            receipt->reportBufferCurrentSize) !=
            receipt->reportBufferAllocationHash)
        return FailNPhaseReportState(receipt, SipPoolInvalidMetadata,
            ERROR_RETRY);

    receipt->validationFlags = 0x7Fu;
    receipt->result = SipPoolOk;
    return 1;
}

struct InteractionGraphArrayHeader {
    uintptr_t data;
    uint32_t count;
    uint32_t capacityRaw;
};

static int FailInteractionGraph(InteractionGraphReceipt* receipt,
    InteractionGraphResult result, uint32_t error, uint32_t kind,
    uint32_t index, uint32_t detail) {
    receipt->result = result;
    receipt->lastError = error;
    receipt->invalidKind = kind;
    receipt->invalidIndex = index;
    receipt->detail = detail;
    SetLastError(error);
    return 0;
}

static bool InteractionGraphCodeMatches(uintptr_t unityBase) {
    struct CodeGuard {
        uint32_t rva;
        const uint8_t* bytes;
        uint32_t count;
    };
    const CodeGuard guards[] = {
        {kInteractionSceneCtorRva, kInteractionSceneCtorBytes,
            sizeof(kInteractionSceneCtorBytes)},
        {kInteractionSceneCtorPool16Rva, kInteractionSceneCtorPool16Bytes,
            sizeof(kInteractionSceneCtorPool16Bytes)},
        {kInteractionSceneCtorPool32Rva, kInteractionSceneCtorPool32Bytes,
            sizeof(kInteractionSceneCtorPool32Bytes)},
        {kInteractionSceneCtorTailRva, kInteractionSceneCtorTailBytes,
            sizeof(kInteractionSceneCtorTailBytes)},
        {kInteractionActorCtorLayoutRva, kInteractionActorCtorLayoutBytes,
            sizeof(kInteractionActorCtorLayoutBytes)},
        {kInteractionActorReallocRva, kInteractionActorReallocBytes,
            sizeof(kInteractionActorReallocBytes)},
        {kInteractionActorRegisterRva, kInteractionActorRegisterBytes,
            sizeof(kInteractionActorRegisterBytes)},
        {kInteractionActorUnregisterRva, kInteractionActorUnregisterBytes,
            sizeof(kInteractionActorUnregisterBytes)},
        {kInteractionPointerAllocateRva, kInteractionPointerAllocateBytes,
            sizeof(kInteractionPointerAllocateBytes)},
        {kInteractionPointerFreeRva, kInteractionPointerFreeBytes,
            sizeof(kInteractionPointerFreeBytes)},
        {kInteractionActiveTestRva, kInteractionActiveTestBytes,
            sizeof(kInteractionActiveTestBytes)},
        {kInteractionActivateRva, kInteractionActivateBytes,
            sizeof(kInteractionActivateBytes)},
        {kInteractionDeactivateRva, kInteractionDeactivateBytes,
            sizeof(kInteractionDeactivateBytes)},
        {kInteractionRegisterRva, kInteractionRegisterBytes,
            sizeof(kInteractionRegisterBytes)},
        {kInteractionUnregisterRva, kInteractionUnregisterBytes,
            sizeof(kInteractionUnregisterBytes)}
    };
    if (!unityBase) return false;
    for (uint32_t i = 0; i < sizeof(guards) / sizeof(guards[0]); ++i) {
        const void* address = reinterpret_cast<const void*>(
            unityBase + guards[i].rva);
        if (!Readable(address, guards[i].count) ||
            !EqualBytes(address, guards[i].bytes, guards[i].count))
            return false;
    }
    return true;
}

static bool ReadInteractionGraphArray(uintptr_t address,
    InteractionGraphArrayHeader& header, uint32_t maximum) {
    if (!Readable(reinterpret_cast<const void*>(address), 12)) return false;
    header.data = *reinterpret_cast<const uintptr_t*>(address);
    header.count = *reinterpret_cast<const uint32_t*>(address + 4);
    header.capacityRaw = *reinterpret_cast<const uint32_t*>(address + 8);
    const uint32_t capacity = header.capacityRaw & 0x7FFFFFFFu;
    return header.count <= capacity && header.count <= maximum &&
        (!header.count || (header.data && Readable(
            reinterpret_cast<const void*>(header.data),
            header.count * sizeof(uintptr_t))));
}

static uint32_t FindInteractionGraphActor(const uintptr_t* actors,
    uint32_t count, uintptr_t actor) {
    return PointerIndex(actors, count, actor);
}

static bool AddInteractionGraphActor(uintptr_t* actors, uint32_t& count,
    uintptr_t actor) {
    if (!actor) return false;
    if (FindInteractionGraphActor(actors, count, actor) != 0xFFFFFFFFu)
        return true;
    if (count >= kMaximumInteractionGraphActors) return false;
    actors[count++] = actor;
    return true;
}

static uint32_t FindInteractionGraphRecord(
    const InteractionGraphInteractionRecord* records, uint32_t count,
    uintptr_t interaction) {
    for (uint32_t i = 0; i < count; ++i)
        if (records[i].interaction == interaction) return i;
    return 0xFFFFFFFFu;
}

static bool FillInteractionGraphInteraction(uintptr_t interaction,
    uint32_t expectedType, uint32_t globalIndex, uint32_t activeCount,
    InteractionGraphInteractionRecord& record) {
    ZeroMemory(&record, sizeof(record));
    if (!interaction ||
        !Readable(reinterpret_cast<const void*>(interaction), 0x18))
        return false;
    record.interaction = interaction;
    record.vtable = *reinterpret_cast<const uintptr_t*>(interaction);
    record.actor0 = *reinterpret_cast<const uintptr_t*>(interaction + 0x04);
    record.actor1 = *reinterpret_cast<const uintptr_t*>(interaction + 0x08);
    record.sceneId = *reinterpret_cast<const uint32_t*>(interaction + 0x0C);
    record.actorId0 = *reinterpret_cast<const uint16_t*>(interaction + 0x10);
    record.actorId1 = *reinterpret_cast<const uint16_t*>(interaction + 0x12);
    record.interactionType = *reinterpret_cast<const uint8_t*>(
        interaction + 0x14);
    record.interactionFlags = *reinterpret_cast<const uint8_t*>(
        interaction + 0x15);
    record.globalIndex = globalIndex;
    record.active = globalIndex < activeCount ? 1u : 0u;
    if (!record.vtable || !record.actor0 || !record.actor1 ||
        record.actor0 == record.actor1 || record.sceneId != globalIndex ||
        record.interactionType != expectedType ||
        record.actorId0 == 0xFFFFu || record.actorId1 == 0xFFFFu)
        return false;

    if (expectedType == 0u || expectedType == 2u ||
        expectedType == 3u || expectedType == 4u) {
        if (!Readable(reinterpret_cast<const void*>(interaction), 0x20))
            return false;
        record.element0 = *reinterpret_cast<const uintptr_t*>(
            interaction + 0x18);
        record.element1 = *reinterpret_cast<const uintptr_t*>(
            interaction + 0x1C);
        if (!record.element0 || !record.element1 ||
            !Readable(reinterpret_cast<const void*>(record.element0), 0x0C) ||
            !Readable(reinterpret_cast<const void*>(record.element1), 0x0C) ||
            *reinterpret_cast<const uintptr_t*>(record.element0 + 0x08) !=
                record.actor0 ||
            *reinterpret_cast<const uintptr_t*>(record.element1 + 0x08) !=
                record.actor1)
            return false;
    }
    if (expectedType == 0u) {
        if ((record.interactionFlags & 0x10u) == 0 ||
            !Readable(reinterpret_cast<const void*>(record.element0), 0x20) ||
            !Readable(reinterpret_cast<const void*>(record.element1), 0x20))
            return false;
        record.shapeCore0 = *reinterpret_cast<const uintptr_t*>(
            record.element0 + 0x1C);
        record.shapeCore1 = *reinterpret_cast<const uintptr_t*>(
            record.element1 + 0x1C);
        if (!record.shapeCore0 || !record.shapeCore1 ||
            !Readable(reinterpret_cast<const void*>(record.shapeCore0 + 0x20),
                sizeof(uintptr_t)) ||
            !Readable(reinterpret_cast<const void*>(record.shapeCore1 + 0x20),
                sizeof(uintptr_t)))
            return false;
        record.pxsShapeCore0 = record.shapeCore0 + 0x20;
        record.pxsShapeCore1 = record.shapeCore1 + 0x20;
        record.semanticLow = record.pxsShapeCore0 < record.pxsShapeCore1 ?
            record.pxsShapeCore0 : record.pxsShapeCore1;
        record.semanticHigh = record.pxsShapeCore0 < record.pxsShapeCore1 ?
            record.pxsShapeCore1 : record.pxsShapeCore0;
        if (!record.semanticLow || record.semanticLow == record.semanticHigh)
            return false;
    }
    record.validationFlags = 0x1Fu;
    return true;
}

static bool PointerPoolContains(uintptr_t slabData, uint32_t slabCount,
    uint32_t slabSize, uint32_t blockBytes, uintptr_t pointer) {
    const uintptr_t* slabs = reinterpret_cast<const uintptr_t*>(slabData);
    for (uint32_t i = 0; i < slabCount; ++i) {
        const uintptr_t slab = slabs[i];
        if (pointer >= slab && pointer < slab + slabSize &&
            ((pointer - slab) % blockBytes) == 0) return true;
    }
    return false;
}

static bool PointerPoolFreeContains(uintptr_t head, uint32_t maximum,
    uintptr_t pointer) {
    uintptr_t node = head;
    for (uint32_t i = 0; node && i < maximum; ++i) {
        if (node == pointer) return true;
        if (!Readable(reinterpret_cast<const void*>(node), sizeof(uintptr_t)))
            return false;
        node = *reinterpret_cast<const uintptr_t*>(node);
    }
    return false;
}

static bool ReadInteractionGraphPool(uintptr_t interactionScene,
    uint32_t poolIndex, uint32_t slabOutputStart, uint32_t freeOutputStart,
    InteractionGraphPoolReceipt& pool) {
    ZeroMemory(&pool, sizeof(pool));
    static const uint32_t offsets[3] = {0x70u, 0x198u, 0x2C0u};
    static const uint32_t capacities[3] = {8u, 16u, 32u};
    static const uint32_t blockBytes[3] = {0x20u, 0x40u, 0x80u};
    pool.pool = interactionScene + offsets[poolIndex];
    pool.blockCapacity = capacities[poolIndex];
    pool.blockBytes = blockBytes[poolIndex];
    pool.slabOutputStart = slabOutputStart;
    pool.freeOutputStart = freeOutputStart;
    if (!Readable(reinterpret_cast<const void*>(pool.pool), 0x128))
        return false;
    pool.inlineBufferUsed = *reinterpret_cast<const uint8_t*>(
        pool.pool + 0x104);
    pool.slabData = *reinterpret_cast<const uintptr_t*>(pool.pool + 0x108);
    pool.slabCount = *reinterpret_cast<const uint32_t*>(pool.pool + 0x10C);
    pool.slabCapacityRaw = *reinterpret_cast<const uint32_t*>(
        pool.pool + 0x110);
    pool.elementsPerSlab = *reinterpret_cast<const uint32_t*>(
        pool.pool + 0x114);
    pool.used = *reinterpret_cast<const uint32_t*>(pool.pool + 0x118);
    pool.unreleasedFree = *reinterpret_cast<const uint32_t*>(
        pool.pool + 0x11C);
    pool.slabSize = *reinterpret_cast<const uint32_t*>(pool.pool + 0x120);
    pool.freeHead = *reinterpret_cast<const uintptr_t*>(pool.pool + 0x124);
    const uint32_t slabCapacity = pool.slabCapacityRaw & 0x7FFFFFFFu;
    if (pool.inlineBufferUsed > 1u || pool.elementsPerSlab != 32u ||
        pool.slabSize != pool.blockBytes * 32u ||
        pool.slabCount > slabCapacity || pool.slabCount > 2048u ||
        (pool.inlineBufferUsed && (pool.slabData != pool.pool + 4u ||
            slabCapacity != 64u)) ||
        (pool.slabCount && (!pool.slabData || !Readable(
            reinterpret_cast<const void*>(pool.slabData),
            pool.slabCount * sizeof(uintptr_t))))) return false;
    if (pool.slabCount > kMaximumInteractionGraphPoolEntries / 32u)
        return false;
    pool.totalElements = pool.slabCount * 32u;
    if (pool.used > pool.totalElements) return false;
    const uintptr_t* slabs = reinterpret_cast<const uintptr_t*>(pool.slabData);
    for (uint32_t i = 0; i < pool.slabCount; ++i) {
        if (!slabs[i] ||
            !Readable(reinterpret_cast<const void*>(slabs[i]), pool.slabSize))
            return false;
        for (uint32_t j = 0; j < i; ++j)
            if (slabs[i] == slabs[j]) return false;
    }
    pool.slabOrderHash = OrderHash(slabs, pool.slabCount);
    uint32_t freeHash = 2166136261u;
    uintptr_t node = pool.freeHead;
    while (node) {
        if (pool.freeCount >= pool.totalElements ||
            !PointerPoolContains(pool.slabData, pool.slabCount,
                pool.slabSize, pool.blockBytes, node) ||
            !Readable(reinterpret_cast<const void*>(node), sizeof(uintptr_t)))
            return false;
        freeHash ^= static_cast<uint32_t>(node);
        freeHash *= 16777619u;
        ++pool.freeCount;
        node = *reinterpret_cast<const uintptr_t*>(node);
    }
    // mUnReleasedFree is signed deferred slab-reclamation accounting. It is
    // deliberately independent of the exact free-chain length and can become
    // negative after releaseEmptySlabs resets it while free nodes remain.
    if (pool.freeCount + pool.used != pool.totalElements) return false;
    pool.freeOrderHash = freeHash;
    pool.validationFlags = 0x3Fu;
    return true;
}

static int CaptureInteractionGraph(uintptr_t unityBase, uintptr_t nphaseCore,
    uintptr_t* activeBodies, uint32_t activeBodyCapacity,
    InteractionGraphActorRecord* actors, uint32_t actorCapacity,
    InteractionGraphInteractionRecord* interactions,
    uint32_t interactionCapacity, uintptr_t* actorSlots,
    uint32_t actorSlotCapacity, uintptr_t* poolSlabs,
    uint32_t poolSlabCapacity, uintptr_t* poolFree,
    uint32_t poolFreeCapacity, InteractionGraphReceipt* receipt) {
    if (!receipt) return 0;
    ZeroMemory(receipt, sizeof(*receipt));
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(*receipt);
    receipt->unityBase = unityBase;
    receipt->nphaseCore = nphaseCore;
    receipt->invalidIndex = 0xFFFFFFFFu;
    if (!unityBase || !nphaseCore)
        return FailInteractionGraph(receipt, InteractionGraphBadArgument,
            ERROR_INVALID_PARAMETER, 0, 0xFFFFFFFFu, 1);
    if (!InteractionGraphCodeMatches(unityBase))
        return FailInteractionGraph(receipt,
            InteractionGraphRevisionMismatch, ERROR_REVISION_MISMATCH,
            0, 0xFFFFFFFFu, 2);
    if (!Readable(reinterpret_cast<const void*>(nphaseCore), 4))
        return FailInteractionGraph(receipt, InteractionGraphUnreadable,
            ERROR_NOACCESS, 0, 0xFFFFFFFFu, 3);
    receipt->ownerScene = *reinterpret_cast<const uintptr_t*>(nphaseCore);
    if (!receipt->ownerScene || !Readable(reinterpret_cast<const void*>(
            receipt->ownerScene + 0x4B4), sizeof(uintptr_t)))
        return FailInteractionGraph(receipt, InteractionGraphUnreadable,
            ERROR_NOACCESS, 0, 0xFFFFFFFFu, 4);
    receipt->interactionScene = *reinterpret_cast<const uintptr_t*>(
        receipt->ownerScene + 0x4B4);
    if (!receipt->interactionScene || !Readable(reinterpret_cast<const void*>(
            receipt->interactionScene), 0x3F4) ||
        *reinterpret_cast<const uintptr_t*>(receipt->interactionScene + 0x3F0) !=
            receipt->ownerScene)
        return FailInteractionGraph(receipt,
            InteractionGraphInvalidMetadata, ERROR_INVALID_DATA,
            0, 0xFFFFFFFFu, 5);
    receipt->llContext = *reinterpret_cast<const uintptr_t*>(
        receipt->interactionScene + 0x3E8);
    receipt->timestamp = *reinterpret_cast<const uint32_t*>(
        receipt->interactionScene + 0x3EC);
    if (!receipt->llContext)
        return FailInteractionGraph(receipt,
            InteractionGraphInvalidMetadata, ERROR_INVALID_DATA,
            0, 0xFFFFFFFFu, 6);

    InteractionGraphArrayHeader activeHeader = {};
    InteractionGraphArrayHeader globalHeaders[6] = {};
    if (!ReadInteractionGraphArray(receipt->interactionScene, activeHeader,
            kMaximumInteractionGraphActors))
        return FailInteractionGraph(receipt,
            InteractionGraphInvalidActiveBody, ERROR_INVALID_DATA,
            1, 0xFFFFFFFFu, 7);
    receipt->activeBodiesData = activeHeader.data;
    receipt->activeBodiesCount = activeHeader.count;
    receipt->activeBodiesCapacityRaw = activeHeader.capacityRaw;
    receipt->activeBodiesRequired = activeHeader.count;
    receipt->activeTwoWayStart = *reinterpret_cast<const uint32_t*>(
        receipt->interactionScene + 0x0C);
    if (receipt->activeTwoWayStart > activeHeader.count)
        return FailInteractionGraph(receipt,
            InteractionGraphInvalidActiveBody, ERROR_INVALID_DATA,
            1, 0xFFFFFFFFu, 8);

    uint32_t totalInteractions = 0;
    for (uint32_t type = 0; type < 6; ++type) {
        const uintptr_t headerAddress = receipt->interactionScene +
            0x10 + type * 12u;
        if (!ReadInteractionGraphArray(headerAddress, globalHeaders[type],
                kMaximumInteractionGraphInteractions - totalInteractions))
            return FailInteractionGraph(receipt,
                InteractionGraphInvalidInteraction, ERROR_INVALID_DATA,
                2, type, 9);
        receipt->globalData[type] = globalHeaders[type].data;
        receipt->globalCount[type] = globalHeaders[type].count;
        receipt->globalCapacityRaw[type] = globalHeaders[type].capacityRaw;
        receipt->globalActiveCount[type] =
            *reinterpret_cast<const uint32_t*>(
                receipt->interactionScene + 0x58 + type * 4u);
        if (receipt->globalActiveCount[type] > globalHeaders[type].count)
            return FailInteractionGraph(receipt,
                InteractionGraphInvalidInteraction, ERROR_INVALID_DATA,
                2, type, 10);
        receipt->globalOrderHash[type] = OrderHash(
            reinterpret_cast<const uintptr_t*>(globalHeaders[type].data),
            globalHeaders[type].count);
        totalInteractions += globalHeaders[type].count;
    }
    receipt->interactionsRequired = totalInteractions;

    uintptr_t actorPointers[kMaximumInteractionGraphActors] = {};
    uint32_t actorCount = 0;
    const uintptr_t* activeSource = reinterpret_cast<const uintptr_t*>(
        activeHeader.data);
    for (uint32_t i = 0; i < activeHeader.count; ++i) {
        if (!AddInteractionGraphActor(actorPointers, actorCount,
                activeSource[i]))
            return FailInteractionGraph(receipt,
                InteractionGraphInvalidActiveBody, ERROR_INVALID_DATA,
                1, i, 11);
        for (uint32_t j = 0; j < i; ++j)
            if (activeSource[i] == activeSource[j])
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidActiveBody, ERROR_DUP_NAME,
                    1, i, 12);
    }
    for (uint32_t type = 0; type < 6; ++type) {
        const uintptr_t* values = reinterpret_cast<const uintptr_t*>(
            globalHeaders[type].data);
        for (uint32_t i = 0; i < globalHeaders[type].count; ++i) {
            const uintptr_t interaction = values[i];
            if (!interaction || !Readable(reinterpret_cast<const void*>(
                    interaction), 0x0C) ||
                !AddInteractionGraphActor(actorPointers, actorCount,
                    *reinterpret_cast<const uintptr_t*>(interaction + 0x04)) ||
                !AddInteractionGraphActor(actorPointers, actorCount,
                    *reinterpret_cast<const uintptr_t*>(interaction + 0x08)))
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidInteraction, ERROR_INVALID_DATA,
                    2, i, 13u + type);
        }
    }
    receipt->actorsRequired = actorCount;

    uint32_t actorSlotCount = 0;
    for (uint32_t i = 0; i < actorCount; ++i) {
        const uintptr_t actor = actorPointers[i];
        if (!Readable(reinterpret_cast<const void*>(actor), 0x34))
            return FailInteractionGraph(receipt, InteractionGraphInvalidActor,
                ERROR_NOACCESS, 3, i, 20);
        const uint32_t count = *reinterpret_cast<const uint32_t*>(actor + 0x1C);
        const uint32_t capacity = *reinterpret_cast<const uint32_t*>(
            actor + 0x18);
        const uintptr_t data = *reinterpret_cast<const uintptr_t*>(actor + 0x14);
        if (count > capacity || count > kMaximumInteractionGraphActorSlots -
                actorSlotCount || (count && (!data || !Readable(
                    reinterpret_cast<const void*>(data),
                    count * sizeof(uintptr_t)))))
            return FailInteractionGraph(receipt, InteractionGraphInvalidActor,
                ERROR_INVALID_DATA, 3, i, 21);
        actorSlotCount += count;
    }
    receipt->actorSlotsRequired = actorSlotCount;

    uint32_t slabRequired = 0;
    uint32_t freeRequired = 0;
    for (uint32_t i = 0; i < 3; ++i) {
        if (!ReadInteractionGraphPool(receipt->interactionScene, i,
                slabRequired, freeRequired, receipt->pools[i]))
            return FailInteractionGraph(receipt, InteractionGraphInvalidPool,
                ERROR_INVALID_DATA, 4, i, 22);
        slabRequired += receipt->pools[i].slabCount;
        freeRequired += receipt->pools[i].freeCount;
        if (slabRequired > kMaximumInteractionGraphPoolEntries ||
            freeRequired > kMaximumInteractionGraphPoolEntries)
            return FailInteractionGraph(receipt, InteractionGraphInvalidPool,
                ERROR_INVALID_DATA, 4, i, 23);
    }
    receipt->poolSlabsRequired = slabRequired;
    receipt->poolFreeRequired = freeRequired;

    if (activeBodyCapacity < activeHeader.count || actorCapacity < actorCount ||
        interactionCapacity < totalInteractions ||
        actorSlotCapacity < actorSlotCount ||
        poolSlabCapacity < slabRequired || poolFreeCapacity < freeRequired)
        return FailInteractionGraph(receipt,
            InteractionGraphCapacityTooSmall, ERROR_INSUFFICIENT_BUFFER,
            0, 0xFFFFFFFFu, 24);
    if ((activeHeader.count && (!activeBodies || !Writable(activeBodies,
            activeHeader.count * sizeof(uintptr_t)))) ||
        (actorCount && (!actors || !Writable(actors,
            actorCount * sizeof(InteractionGraphActorRecord)))) ||
        (totalInteractions && (!interactions || !Writable(interactions,
            totalInteractions * sizeof(InteractionGraphInteractionRecord)))) ||
        (actorSlotCount && (!actorSlots || !Writable(actorSlots,
            actorSlotCount * sizeof(uintptr_t)))) ||
        (slabRequired && (!poolSlabs || !Writable(poolSlabs,
            slabRequired * sizeof(uintptr_t)))) ||
        (freeRequired && (!poolFree || !Writable(poolFree,
            freeRequired * sizeof(uintptr_t)))))
        return FailInteractionGraph(receipt, InteractionGraphBadArgument,
            ERROR_NOACCESS, 0, 0xFFFFFFFFu, 25);

    if (activeHeader.count)
        CopyWords(activeBodies, activeSource, activeHeader.count);
    receipt->activeBodiesWritten = activeHeader.count;

    uint32_t interactionOutput = 0;
    for (uint32_t type = 0; type < 6; ++type) {
        const uintptr_t* values = reinterpret_cast<const uintptr_t*>(
            globalHeaders[type].data);
        for (uint32_t i = 0; i < globalHeaders[type].count; ++i) {
            if (!FillInteractionGraphInteraction(values[i], type, i,
                    receipt->globalActiveCount[type],
                    interactions[interactionOutput]))
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidInteraction, ERROR_INVALID_DATA,
                    2, interactionOutput, 26);
            ++interactionOutput;
        }
    }
    receipt->interactionsWritten = interactionOutput;

    uint32_t slotOutput = 0;
    for (uint32_t i = 0; i < actorCount; ++i) {
        const uintptr_t actor = actorPointers[i];
        InteractionGraphActorRecord& record = actors[i];
        ZeroMemory(&record, sizeof(record));
        record.actor = actor;
        record.vtable = *reinterpret_cast<const uintptr_t*>(actor);
        for (uint32_t j = 0; j < 4; ++j)
            record.inlineSlots[j] = *reinterpret_cast<const uintptr_t*>(
                actor + 4 + j * 4u);
        record.interactionsData = *reinterpret_cast<const uintptr_t*>(
            actor + 0x14);
        record.interactionCapacity = *reinterpret_cast<const uint32_t*>(
            actor + 0x18);
        record.interactionCount = *reinterpret_cast<const uint32_t*>(
            actor + 0x1C);
        record.firstElement = *reinterpret_cast<const uintptr_t*>(actor + 0x20);
        record.interactionScene = *reinterpret_cast<const uintptr_t*>(
            actor + 0x24);
        record.sceneArrayIndex = *reinterpret_cast<const uint32_t*>(
            actor + 0x28);
        record.transferringCount = *reinterpret_cast<const uint16_t*>(
            actor + 0x2C);
        record.uniqueCount = *reinterpret_cast<const uint16_t*>(actor + 0x2E);
        record.countedCount = *reinterpret_cast<const uint16_t*>(actor + 0x30);
        record.actorType = *reinterpret_cast<const uint8_t*>(actor + 0x32);
        record.islandNodeInfo = *reinterpret_cast<const uint8_t*>(actor + 0x33);
        record.interactionOutputStart = slotOutput;
        record.activeBodyIndex = PointerIndex(activeSource,
            activeHeader.count, actor);
        if (!record.vtable || record.interactionScene !=
                receipt->interactionScene ||
            record.transferringCount > record.interactionCount ||
            (record.interactionCapacity == 0 &&
                (record.interactionsData || record.interactionCount)) ||
            (record.interactionCapacity == 4 &&
                record.interactionsData != actor + 4) ||
            (record.interactionCapacity > 4 &&
                record.interactionsData == actor + 4))
            return FailInteractionGraph(receipt, InteractionGraphInvalidActor,
                ERROR_INVALID_DATA, 3, i, 27);
        const bool isActive = (record.islandNodeInfo & 1u) != 0;
        if ((isActive && (record.activeBodyIndex == 0xFFFFFFFFu ||
                record.sceneArrayIndex != record.activeBodyIndex)) ||
            (!isActive && (record.activeBodyIndex != 0xFFFFFFFFu ||
                record.sceneArrayIndex != 0xFFFFFFFEu)))
            return FailInteractionGraph(receipt, InteractionGraphInvalidActor,
                ERROR_INVALID_DATA, 3, i, 28);
        const uintptr_t* sourceSlots = reinterpret_cast<const uintptr_t*>(
            record.interactionsData);
        uint32_t counted = 0;
        for (uint32_t j = 0; j < record.interactionCount; ++j) {
            const uintptr_t interaction = sourceSlots[j];
            const uint32_t recordIndex = FindInteractionGraphRecord(
                interactions, totalInteractions, interaction);
            if (recordIndex == 0xFFFFFFFFu)
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidActor, ERROR_INVALID_DATA,
                    3, i, 29);
            const InteractionGraphInteractionRecord& interactionRecord =
                interactions[recordIndex];
            const bool actor0 = interactionRecord.actor0 == actor;
            const bool actor1 = interactionRecord.actor1 == actor;
            if (actor0 == actor1 ||
                (actor0 ? interactionRecord.actorId0 :
                    interactionRecord.actorId1) != j)
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidActor, ERROR_INVALID_DATA,
                    3, i, 30);
            const uint32_t otherIndex = FindInteractionGraphActor(
                actorPointers, actorCount, actor0 ?
                    interactionRecord.actor1 : interactionRecord.actor0);
            if (otherIndex == 0xFFFFFFFFu)
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidActor, ERROR_INVALID_DATA,
                    3, i, 31);
            const uint8_t otherType = *reinterpret_cast<const uint8_t*>(
                actorPointers[otherIndex] + 0x32);
            const bool dynamic0 = record.actorType == 1u ||
                record.actorType == 4u;
            const bool dynamic1 = otherType == 1u || otherType == 4u;
            const bool permanentlyNonTransferring = !dynamic0 || !dynamic1 ||
                interactionRecord.interactionType == 2u ||
                interactionRecord.interactionType == 3u;
            if ((j < record.transferringCount) ==
                    permanentlyNonTransferring)
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidActor, ERROR_INVALID_DATA,
                    3, i, 32);
            if (interactionRecord.interactionType < 2u) ++counted;
            actorSlots[slotOutput++] = interaction;
        }
        if (counted != record.countedCount)
            return FailInteractionGraph(receipt, InteractionGraphInvalidActor,
                ERROR_INVALID_DATA, 3, i, 33);
        record.interactionOrderHash = OrderHash(sourceSlots,
            record.interactionCount);
        record.validationFlags = 0x7Fu;
    }
    receipt->actorsWritten = actorCount;
    receipt->actorSlotsWritten = slotOutput;
    if (slotOutput != totalInteractions * 2u)
        return FailInteractionGraph(receipt, InteractionGraphInvalidActor,
            ERROR_INVALID_DATA, 3, 0xFFFFFFFFu, 34);

    for (uint32_t i = 0; i < activeHeader.count; ++i) {
        const uint32_t actorIndex = FindInteractionGraphActor(actorPointers,
            actorCount, activeSource[i]);
        if (actorIndex == 0xFFFFFFFFu)
            return FailInteractionGraph(receipt,
                InteractionGraphInvalidActiveBody, ERROR_INVALID_DATA,
                1, i, 35);
        const uint8_t node = actors[actorIndex].islandNodeInfo;
        const uint8_t expectedType = i < receipt->activeTwoWayStart ? 2u : 4u;
        if ((node & 1u) == 0 ||
            (node & 0x0Eu) != expectedType)
            return FailInteractionGraph(receipt,
                InteractionGraphInvalidActiveBody, ERROR_INVALID_DATA,
                1, i, 35);
    }
    for (uint32_t i = 0; i < totalInteractions; ++i) {
        const InteractionGraphInteractionRecord& record = interactions[i];
        const uint32_t actor0 = FindInteractionGraphActor(actorPointers,
            actorCount, record.actor0);
        const uint32_t actor1 = FindInteractionGraphActor(actorPointers,
            actorCount, record.actor1);
        if (actor0 == 0xFFFFFFFFu || actor1 == 0xFFFFFFFFu ||
            (record.active &&
                actors[actor0].activeBodyIndex == 0xFFFFFFFFu &&
                actors[actor1].activeBodyIndex == 0xFFFFFFFFu))
            return FailInteractionGraph(receipt,
                InteractionGraphInvalidInteraction, ERROR_INVALID_DATA,
                2, i, 36);
    }

    uint32_t slabOutput = 0;
    uint32_t freeOutput = 0;
    for (uint32_t poolIndex = 0; poolIndex < 3; ++poolIndex) {
        InteractionGraphPoolReceipt& pool = receipt->pools[poolIndex];
        const uintptr_t* slabs = reinterpret_cast<const uintptr_t*>(
            pool.slabData);
        if (pool.slabCount)
            CopyWords(poolSlabs + slabOutput, slabs, pool.slabCount);
        slabOutput += pool.slabCount;
        uintptr_t node = pool.freeHead;
        while (node) {
            poolFree[freeOutput++] = node;
            node = *reinterpret_cast<const uintptr_t*>(node);
        }
        uint32_t owners = 0;
        uint32_t ownerHash = 2166136261u;
        for (uint32_t i = 0; i < actorCount; ++i) {
            if (actors[i].interactionCapacity != pool.blockCapacity) continue;
            const uintptr_t block = actors[i].interactionsData;
            if (!block || !PointerPoolContains(pool.slabData, pool.slabCount,
                    pool.slabSize, pool.blockBytes, block) ||
                PointerPoolFreeContains(pool.freeHead, pool.freeCount, block))
                return FailInteractionGraph(receipt,
                    InteractionGraphInvalidPool, ERROR_INVALID_DATA,
                    4, poolIndex, 37);
            for (uint32_t j = 0; j < i; ++j)
                if (actors[j].interactionCapacity == pool.blockCapacity &&
                    actors[j].interactionsData == block)
                    return FailInteractionGraph(receipt,
                        InteractionGraphInvalidPool, ERROR_DUP_NAME,
                        4, poolIndex, 38);
            ownerHash ^= static_cast<uint32_t>(block);
            ownerHash *= 16777619u;
            ++owners;
        }
        if (owners != pool.used)
            return FailInteractionGraph(receipt,
                InteractionGraphInvalidPool, ERROR_INVALID_DATA,
                4, poolIndex, 39);
        pool.usedOwnerHash = ownerHash;
        pool.validationFlags |= 0x40u;
    }
    receipt->poolSlabsWritten = slabOutput;
    receipt->poolFreeWritten = freeOutput;

    // Reread all independently mutable headers, orders, records and pool
    // chains. A checkpoint must describe one phase rather than a torn sample.
    InteractionGraphArrayHeader checkHeader = {};
    if (!ReadInteractionGraphArray(receipt->interactionScene, checkHeader,
            kMaximumInteractionGraphActors) ||
        checkHeader.data != activeHeader.data ||
        checkHeader.count != activeHeader.count ||
        checkHeader.capacityRaw != activeHeader.capacityRaw ||
        *reinterpret_cast<const uint32_t*>(receipt->interactionScene + 0x0C) !=
            receipt->activeTwoWayStart ||
        OrderHash(reinterpret_cast<const uintptr_t*>(checkHeader.data),
            checkHeader.count) != OrderHash(activeBodies,
                receipt->activeBodiesWritten))
        return FailInteractionGraph(receipt, InteractionGraphUnstable,
            ERROR_RETRY, 0, 0xFFFFFFFFu, 40);
    for (uint32_t type = 0; type < 6; ++type) {
        if (!ReadInteractionGraphArray(receipt->interactionScene + 0x10 +
                type * 12u, checkHeader,
                kMaximumInteractionGraphInteractions) ||
            checkHeader.data != globalHeaders[type].data ||
            checkHeader.count != globalHeaders[type].count ||
            checkHeader.capacityRaw != globalHeaders[type].capacityRaw ||
            *reinterpret_cast<const uint32_t*>(receipt->interactionScene +
                0x58 + type * 4u) != receipt->globalActiveCount[type] ||
            OrderHash(reinterpret_cast<const uintptr_t*>(checkHeader.data),
                checkHeader.count) != receipt->globalOrderHash[type])
            return FailInteractionGraph(receipt, InteractionGraphUnstable,
                ERROR_RETRY, 2, type, 41);
    }
    for (uint32_t i = 0; i < actorCount; ++i) {
        const InteractionGraphActorRecord& record = actors[i];
        if (!Readable(reinterpret_cast<const void*>(record.actor), 0x34) ||
            *reinterpret_cast<const uintptr_t*>(record.actor) != record.vtable ||
            *reinterpret_cast<const uintptr_t*>(record.actor + 0x04) !=
                record.inlineSlots[0] ||
            *reinterpret_cast<const uintptr_t*>(record.actor + 0x08) !=
                record.inlineSlots[1] ||
            *reinterpret_cast<const uintptr_t*>(record.actor + 0x0C) !=
                record.inlineSlots[2] ||
            *reinterpret_cast<const uintptr_t*>(record.actor + 0x10) !=
                record.inlineSlots[3] ||
            *reinterpret_cast<const uintptr_t*>(record.actor + 0x14) !=
                record.interactionsData ||
            *reinterpret_cast<const uint32_t*>(record.actor + 0x18) !=
                record.interactionCapacity ||
            *reinterpret_cast<const uint32_t*>(record.actor + 0x1C) !=
                record.interactionCount ||
            *reinterpret_cast<const uintptr_t*>(record.actor + 0x20) !=
                record.firstElement ||
            *reinterpret_cast<const uintptr_t*>(record.actor + 0x24) !=
                record.interactionScene ||
            *reinterpret_cast<const uint32_t*>(record.actor + 0x28) !=
                record.sceneArrayIndex ||
            *reinterpret_cast<const uint16_t*>(record.actor + 0x2C) !=
                record.transferringCount ||
            *reinterpret_cast<const uint16_t*>(record.actor + 0x2E) !=
                record.uniqueCount ||
            *reinterpret_cast<const uint16_t*>(record.actor + 0x30) !=
                record.countedCount ||
            *reinterpret_cast<const uint8_t*>(record.actor + 0x32) !=
                record.actorType ||
            *reinterpret_cast<const uint8_t*>(record.actor + 0x33) !=
                record.islandNodeInfo ||
            OrderHash(reinterpret_cast<const uintptr_t*>(
                record.interactionsData), record.interactionCount) !=
                record.interactionOrderHash)
            return FailInteractionGraph(receipt, InteractionGraphUnstable,
                ERROR_RETRY, 3, i, 42);
    }
    for (uint32_t i = 0; i < totalInteractions; ++i) {
        InteractionGraphInteractionRecord check = {};
        const InteractionGraphInteractionRecord& record = interactions[i];
        if (!FillInteractionGraphInteraction(record.interaction,
                record.interactionType, record.globalIndex,
                receipt->globalActiveCount[record.interactionType],
                check) || check.vtable != record.vtable ||
            check.actor0 != record.actor0 || check.actor1 != record.actor1 ||
            check.sceneId != record.sceneId ||
            check.actorId0 != record.actorId0 ||
            check.actorId1 != record.actorId1 ||
            check.interactionType != record.interactionType ||
            check.interactionFlags != record.interactionFlags ||
            check.active != record.active ||
            check.element0 != record.element0 || check.element1 != record.element1 ||
            check.shapeCore0 != record.shapeCore0 ||
            check.shapeCore1 != record.shapeCore1 ||
            check.pxsShapeCore0 != record.pxsShapeCore0 ||
            check.pxsShapeCore1 != record.pxsShapeCore1 ||
            check.semanticLow != record.semanticLow ||
            check.semanticHigh != record.semanticHigh)
            return FailInteractionGraph(receipt, InteractionGraphUnstable,
                ERROR_RETRY, 2, i, 43);
    }
    for (uint32_t poolIndex = 0; poolIndex < 3; ++poolIndex) {
        InteractionGraphPoolReceipt check = {};
        const InteractionGraphPoolReceipt& pool = receipt->pools[poolIndex];
        if (!ReadInteractionGraphPool(receipt->interactionScene, poolIndex,
                pool.slabOutputStart, pool.freeOutputStart, check) ||
            check.pool != pool.pool || check.slabData != pool.slabData ||
            check.freeHead != pool.freeHead || check.slabCount != pool.slabCount ||
            check.slabCapacityRaw != pool.slabCapacityRaw ||
            check.used != pool.used ||
            check.unreleasedFree != pool.unreleasedFree ||
            check.freeCount != pool.freeCount ||
            check.slabOrderHash != pool.slabOrderHash ||
            check.freeOrderHash != pool.freeOrderHash)
            return FailInteractionGraph(receipt, InteractionGraphUnstable,
                ERROR_RETRY, 4, poolIndex, 44);
    }
    if (*reinterpret_cast<const uintptr_t*>(nphaseCore) != receipt->ownerScene ||
        *reinterpret_cast<const uintptr_t*>(receipt->ownerScene + 0x4B4) !=
            receipt->interactionScene ||
        *reinterpret_cast<const uintptr_t*>(receipt->interactionScene + 0x3E8) !=
            receipt->llContext ||
        *reinterpret_cast<const uint32_t*>(receipt->interactionScene + 0x3EC) !=
            receipt->timestamp)
        return FailInteractionGraph(receipt, InteractionGraphUnstable,
            ERROR_RETRY, 0, 0xFFFFFFFFu, 45);

    receipt->actorHash = ByteHash(actors,
        actorCount * sizeof(InteractionGraphActorRecord));
    receipt->interactionHash = ByteHash(interactions,
        totalInteractions * sizeof(InteractionGraphInteractionRecord));
    receipt->actorSlotHash = OrderHash(actorSlots, actorSlotCount);
    receipt->poolHash = ByteHash(receipt->pools,
        sizeof(receipt->pools));
    uint32_t graphHash = 2166136261u;
    graphHash = AppendByteHash(graphHash, activeBodies,
        activeHeader.count * sizeof(uintptr_t));
    graphHash = AppendByteHash(graphHash, &receipt->actorHash,
        sizeof(receipt->actorHash));
    graphHash = AppendByteHash(graphHash, &receipt->interactionHash,
        sizeof(receipt->interactionHash));
    graphHash = AppendByteHash(graphHash, &receipt->actorSlotHash,
        sizeof(receipt->actorSlotHash));
    graphHash = AppendByteHash(graphHash, &receipt->poolHash,
        sizeof(receipt->poolHash));
    receipt->graphHash = graphHash;
    receipt->validationFlags = 0xFFu;
    receipt->result = InteractionGraphOk;
    return 1;
}

struct TransformCacheHeaderState {
    uint32_t currentId;
    InteractionGraphArrayHeader freeIds;
    InteractionGraphArrayHeader transforms;
    InteractionGraphArrayHeader refCounts;
};

static bool TransformCacheRevisionMatches(uintptr_t unityBase) {
    if (!InteractionGraphCodeMatches(unityBase)) return false;
    const void* layout = reinterpret_cast<const void*>(unityBase +
        kCreateManagerTransformCacheLayoutRva);
    const void* shapeCreate = reinterpret_cast<const void*>(unityBase +
        kShapeSimCreateTransformCacheRva);
    static const uint8_t shapeCreateBytes[] = {
        0x55,0x8B,0xEC,0x83,0xEC,0x20,0x8B,0xC1,0x53,0x8B,0x5D,0x08,
        0x89,0x45,0xFC,0x83,0x78,0x18,0xFF
    };
    return Readable(layout, sizeof(kCreateManagerTransformCacheLayoutBytes)) &&
        EqualBytes(layout, kCreateManagerTransformCacheLayoutBytes,
            sizeof(kCreateManagerTransformCacheLayoutBytes)) &&
        Readable(shapeCreate, sizeof(shapeCreateBytes)) &&
        EqualBytes(shapeCreate, shapeCreateBytes, sizeof(shapeCreateBytes));
}

static bool ReadTransformCacheHeader(uintptr_t transformCache,
    TransformCacheHeaderState& header) {
    ZeroMemory(&header, sizeof(header));
    if (!transformCache || !Readable(reinterpret_cast<const void*>(
            transformCache), 0x28)) return false;
    header.currentId = *reinterpret_cast<const uint32_t*>(transformCache);
    header.freeIds.data = *reinterpret_cast<const uintptr_t*>(
        transformCache + 0x04);
    header.freeIds.count = *reinterpret_cast<const uint32_t*>(
        transformCache + 0x08);
    header.freeIds.capacityRaw = *reinterpret_cast<const uint32_t*>(
        transformCache + 0x0C);
    header.transforms.data = *reinterpret_cast<const uintptr_t*>(
        transformCache + 0x10);
    header.transforms.count = *reinterpret_cast<const uint32_t*>(
        transformCache + 0x14);
    header.transforms.capacityRaw = *reinterpret_cast<const uint32_t*>(
        transformCache + 0x18);
    header.refCounts.data = *reinterpret_cast<const uintptr_t*>(
        transformCache + 0x1C);
    header.refCounts.count = *reinterpret_cast<const uint32_t*>(
        transformCache + 0x20);
    header.refCounts.capacityRaw = *reinterpret_cast<const uint32_t*>(
        transformCache + 0x24);
    const uint32_t freeCapacity =
        header.freeIds.capacityRaw & 0x7FFFFFFFu;
    const uint32_t transformCapacity =
        header.transforms.capacityRaw & 0x7FFFFFFFu;
    const uint32_t refCapacity =
        header.refCounts.capacityRaw & 0x7FFFFFFFu;
    if (header.currentId > kMaximumTransformCacheIds ||
        header.freeIds.count > freeCapacity ||
        header.freeIds.count > header.currentId ||
        header.transforms.count > transformCapacity ||
        header.refCounts.count > refCapacity ||
        transformCapacity > kMaximumTransformCacheIds ||
        refCapacity > kMaximumTransformCacheIds ||
        header.transforms.count != transformCapacity ||
        header.refCounts.count != refCapacity ||
        transformCapacity != refCapacity ||
        header.currentId > header.transforms.count ||
        (header.freeIds.count && (!header.freeIds.data || !Readable(
            reinterpret_cast<const void*>(header.freeIds.data),
            header.freeIds.count * sizeof(uint32_t)))) ||
        (header.currentId && (!header.transforms.data || !Readable(
            reinterpret_cast<const void*>(header.transforms.data),
            header.currentId * sizeof(PhysxTransform)))) ||
        (header.currentId && (!header.refCounts.data || !Readable(
            reinterpret_cast<const void*>(header.refCounts.data),
            header.currentId * sizeof(uint32_t))))) return false;
    return true;
}

static bool ResolveTransformCache(uintptr_t nphaseCore,
    uintptr_t& ownerScene, uintptr_t& interactionScene, uintptr_t& context,
    uintptr_t& transformCache) {
    ownerScene = 0;
    interactionScene = 0;
    context = 0;
    transformCache = 0;
    if (!nphaseCore || !Readable(reinterpret_cast<const void*>(nphaseCore),
            sizeof(uintptr_t))) return false;
    ownerScene = *reinterpret_cast<const uintptr_t*>(nphaseCore);
    if (!ownerScene || !Readable(reinterpret_cast<const void*>(ownerScene +
            0x450), 0x68)) return false;
    interactionScene = *reinterpret_cast<const uintptr_t*>(ownerScene +
        0x4B4);
    if (!interactionScene ||
        *reinterpret_cast<const uintptr_t*>(ownerScene + 0x450) != nphaseCore ||
        !Readable(reinterpret_cast<const void*>(interactionScene), 0x3F4) ||
        *reinterpret_cast<const uintptr_t*>(interactionScene + 0x3F0) !=
            ownerScene) return false;
    context = *reinterpret_cast<const uintptr_t*>(interactionScene + 0x3E8);
    if (!context || !Readable(reinterpret_cast<const void*>(context),
            0x1DE4)) return false;
    transformCache = context + 0x1DBCu;
    return true;
}

static bool SameTransformCacheHeader(const TransformCacheHeaderState& left,
    const TransformCacheHeaderState& right) {
    return left.currentId == right.currentId &&
        left.freeIds.data == right.freeIds.data &&
        left.freeIds.count == right.freeIds.count &&
        left.freeIds.capacityRaw == right.freeIds.capacityRaw &&
        left.transforms.data == right.transforms.data &&
        left.transforms.count == right.transforms.count &&
        left.transforms.capacityRaw == right.transforms.capacityRaw &&
        left.refCounts.data == right.refCounts.data &&
        left.refCounts.count == right.refCounts.count &&
        left.refCounts.capacityRaw == right.refCounts.capacityRaw;
}

static bool HashTransformCacheNoAllocation(uintptr_t context,
    uint32_t& hash) {
    hash = 0;
    if (!context || !Readable(reinterpret_cast<const void*>(context),
            0x1DE4)) return false;
    const uintptr_t cache = context + 0x1DBCu;
    TransformCacheHeaderState header = {};
    if (!ReadTransformCacheHeader(cache, header)) return false;
    const uint32_t* freeIds = reinterpret_cast<const uint32_t*>(
        header.freeIds.data);
    const uint32_t* refs = reinterpret_cast<const uint32_t*>(
        header.refCounts.data);
    uint32_t freeSeen[(kMaximumTransformCacheIds + 31u) / 32u] = {};
    for (uint32_t i = 0; i < header.freeIds.count; ++i) {
        const uint32_t id = freeIds[i];
        if (id >= header.currentId || refs[id] != 0)
            return false;
        const uint32_t mask = 1u << (id & 31u);
        if (freeSeen[id >> 5] & mask) return false;
        freeSeen[id >> 5] |= mask;
    }
    uint32_t value = 2166136261u;
    value = AppendByteHash(value, &header.currentId,
        sizeof(header.currentId));
    value = AppendByteHash(value, freeIds,
        header.freeIds.count * sizeof(uint32_t));
    value = AppendByteHash(value, reinterpret_cast<const void*>(
        header.transforms.data),
        header.currentId * sizeof(PhysxTransform));
    value = AppendByteHash(value, refs,
        header.currentId * sizeof(uint32_t));
    TransformCacheHeaderState check = {};
    if (!ReadTransformCacheHeader(cache, check) ||
        !SameTransformCacheHeader(header, check)) return false;
    hash = value;
    return true;
}

static bool HashInteractionGraphNoAllocation(uintptr_t ownerScene,
    uintptr_t nphaseCore, uint32_t& hash) {
    hash = 0;
    if (!ownerScene || !nphaseCore ||
        !Readable(reinterpret_cast<const void*>(ownerScene + 0x450), 0x68) ||
        *reinterpret_cast<const uintptr_t*>(ownerScene + 0x450) != nphaseCore ||
        *reinterpret_cast<const uintptr_t*>(nphaseCore) != ownerScene)
        return false;
    const uintptr_t interactionScene = *reinterpret_cast<const uintptr_t*>(
        ownerScene + 0x4B4);
    if (!interactionScene || !Readable(reinterpret_cast<const void*>(
            interactionScene), 0x3F4) ||
        *reinterpret_cast<const uintptr_t*>(interactionScene + 0x3F0) !=
            ownerScene) return false;
    uint32_t value = 2166136261u;
    const uint32_t timestamp = *reinterpret_cast<const uint32_t*>(
        interactionScene + 0x3EC);
    value = AppendByteHash(value, &timestamp, sizeof(timestamp));
    for (uint32_t array = 0; array < 7; ++array) {
        const uintptr_t headerAddress = interactionScene +
            (array == 0 ? 0u : 0x10u + (array - 1u) * 12u);
        InteractionGraphArrayHeader header = {};
        const uint32_t maximum = array == 0 ?
            kMaximumInteractionGraphActors :
            kMaximumInteractionGraphInteractions;
        if (!ReadInteractionGraphArray(headerAddress, header, maximum))
            return false;
        value = AppendByteHash(value, &header, sizeof(header));
        const uintptr_t* entries = reinterpret_cast<const uintptr_t*>(
            header.data);
        value = AppendByteHash(value, entries,
            header.count * sizeof(uintptr_t));
        if (array == 0) continue;
        const uint32_t type = array - 1u;
        const uint32_t active = *reinterpret_cast<const uint32_t*>(
            interactionScene + 0x58 + type * 4u);
        if (active > header.count) return false;
        value = AppendByteHash(value, &active, sizeof(active));
        for (uint32_t i = 0; i < header.count; ++i) {
            const uint32_t bytes = (type == 0u || type == 2u ||
                type == 3u || type == 4u) ? 0x20u : 0x18u;
            if (!entries[i] || !Readable(reinterpret_cast<const void*>(
                    entries[i]), bytes)) return false;
            value = AppendByteHash(value, reinterpret_cast<const void*>(
                entries[i]), bytes);
        }
    }
    hash = value;
    return true;
}

static bool ReadTransformCacheInteractionManager(
    const InteractionGraphInteractionRecord& interaction,
    uintptr_t& manager, uint32_t (&cacheIds)[2]) {
    manager = 0;
    cacheIds[0] = 0xFFFFFFFFu;
    cacheIds[1] = 0xFFFFFFFFu;
    if (interaction.interaction < 8u ||
        !Readable(reinterpret_cast<const void*>(interaction.interaction),
            0x34)) return false;
    manager = *reinterpret_cast<const uintptr_t*>(
        interaction.interaction + 0x30);
    if (!manager) return true;
    if (!Readable(reinterpret_cast<const void*>(manager),
            kContactManagerSize) ||
        *reinterpret_cast<const uintptr_t*>(manager + 0x0C) !=
            interaction.interaction - 8u ||
        *reinterpret_cast<const uintptr_t*>(manager + 0x58) !=
            interaction.pxsShapeCore0 ||
        *reinterpret_cast<const uintptr_t*>(manager + 0x5C) !=
            interaction.pxsShapeCore1)
        return false;
    cacheIds[0] = *reinterpret_cast<const uint32_t*>(manager + 0x74);
    cacheIds[1] = *reinterpret_cast<const uint32_t*>(manager + 0x78);
    return cacheIds[0] == *reinterpret_cast<const uint32_t*>(
            interaction.element0 + 0x18) &&
        cacheIds[1] == *reinterpret_cast<const uint32_t*>(
            interaction.element1 + 0x18);
}

static int CaptureTransformCache(uintptr_t unityBase, uintptr_t nphaseCore,
    TransformCacheEntryRecord* entries, uint32_t entryCapacity,
    uint32_t* freeIds, uint32_t freeCapacity,
    TransformCacheBindingRecord* bindings, uint32_t bindingCapacity,
    TransformCacheReceipt* receipt) {
    if (!receipt) return 0;
    ZeroMemory(receipt, sizeof(*receipt));
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(*receipt);
    receipt->unityBase = unityBase;
    receipt->nphaseCore = nphaseCore;
    receipt->invalidIndex = 0xFFFFFFFFu;
    if (!unityBase || !nphaseCore)
        return FailTransformCache(receipt, TransformCacheBadArgument,
            ERROR_INVALID_PARAMETER, 0, 0xFFFFFFFFu, 1);
    if (!TransformCacheRevisionMatches(unityBase))
        return FailTransformCache(receipt, TransformCacheRevisionMismatch,
            ERROR_REVISION_MISMATCH, 0, 0xFFFFFFFFu, 2);
    if (!ResolveTransformCache(nphaseCore, receipt->ownerScene,
            receipt->interactionScene, receipt->context,
            receipt->transformCache))
        return FailTransformCache(receipt, TransformCacheUnreadable,
            ERROR_NOACCESS, 0, 0xFFFFFFFFu, 3);
    receipt->validationFlags |= 0x03u;

    TransformCacheHeaderState header = {};
    if (!ReadTransformCacheHeader(receipt->transformCache, header))
        return FailTransformCache(receipt, TransformCacheInvalidMetadata,
            ERROR_INVALID_DATA, 1, 0xFFFFFFFFu, 4);
    receipt->currentId = header.currentId;
    receipt->freeData = header.freeIds.data;
    receipt->freeCount = header.freeIds.count;
    receipt->freeCapacityRaw = header.freeIds.capacityRaw;
    receipt->transformsData = header.transforms.data;
    receipt->transformsCount = header.transforms.count;
    receipt->transformsCapacityRaw = header.transforms.capacityRaw;
    receipt->refCountsData = header.refCounts.data;
    receipt->refCountsCount = header.refCounts.count;
    receipt->refCountsCapacityRaw = header.refCounts.capacityRaw;
    receipt->entriesRequired = header.currentId;
    receipt->freeRequired = header.freeIds.count;
    receipt->validationFlags |= 0x04u;

    InteractionGraphArrayHeader interactionHeader = {};
    if (!ReadInteractionGraphArray(receipt->interactionScene + 0x10,
            interactionHeader, kMaximumInteractionGraphInteractions) ||
        interactionHeader.count > kMaximumTransformCacheBindings / 2u)
        return FailTransformCache(receipt, TransformCacheInvalidMetadata,
            ERROR_INVALID_DATA, 2, 0xFFFFFFFFu, 5);
    const uint32_t activeCount = *reinterpret_cast<const uint32_t*>(
        receipt->interactionScene + 0x58);
    if (activeCount > interactionHeader.count)
        return FailTransformCache(receipt, TransformCacheInvalidMetadata,
            ERROR_INVALID_DATA, 2, 0xFFFFFFFFu, 18);
    const uintptr_t* interactions = reinterpret_cast<const uintptr_t*>(
        interactionHeader.data);
    for (uint32_t i = 0; i < interactionHeader.count; ++i) {
        InteractionGraphInteractionRecord interaction = {};
        uintptr_t manager = 0;
        uint32_t cacheIds[2] = {};
        if (!FillInteractionGraphInteraction(interactions[i], 0u, i,
                activeCount, interaction) ||
            !ReadTransformCacheInteractionManager(interaction, manager,
                cacheIds))
            return FailTransformCache(receipt, TransformCacheInvalidBinding,
                ERROR_INVALID_DATA, 5, i, 11);
        if (manager) receipt->bindingsRequired += 2u;
    }
    if (entryCapacity < receipt->entriesRequired ||
        freeCapacity < receipt->freeRequired ||
        bindingCapacity < receipt->bindingsRequired)
        return FailTransformCache(receipt, TransformCacheCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER, 0, 0xFFFFFFFFu, 6);
    if ((receipt->entriesRequired && (!entries || !Writable(entries,
            receipt->entriesRequired * sizeof(*entries)))) ||
        (receipt->freeRequired && (!freeIds || !Writable(freeIds,
            receipt->freeRequired * sizeof(*freeIds)))) ||
        (receipt->bindingsRequired && (!bindings || !Writable(bindings,
            receipt->bindingsRequired * sizeof(*bindings)))))
        return FailTransformCache(receipt, TransformCacheBadArgument,
            ERROR_NOACCESS, 0, 0xFFFFFFFFu, 7);

    const PhysxTransform* sourceTransforms =
        reinterpret_cast<const PhysxTransform*>(header.transforms.data);
    const uint32_t* sourceRefs = reinterpret_cast<const uint32_t*>(
        header.refCounts.data);
    for (uint32_t id = 0; id < header.currentId; ++id) {
        TransformCacheEntryRecord& record = entries[id];
        ZeroMemory(&record, sizeof(record));
        record.id = id;
        record.refCount = sourceRefs[id];
        CopyBytes(record.rotation, sourceTransforms[id].rotation,
            sizeof(record.rotation));
        CopyBytes(record.position, sourceTransforms[id].position,
            sizeof(record.position));
        record.poseHash = ByteHash(&sourceTransforms[id],
            sizeof(PhysxTransform));
        record.stateFlags = 0x01u;
        if (record.refCount) {
            RigidPose pose = ExportPose(sourceTransforms[id]);
            if (!Finite(pose))
                return FailTransformCache(receipt,
                    TransformCacheInvalidMetadata, ERROR_INVALID_DATA,
                    3, id, 8);
            record.stateFlags |= 0x04u;
            ++receipt->liveCount;
            receipt->totalRefCount += record.refCount;
        }
    }
    receipt->entriesWritten = header.currentId;

    const uint32_t* sourceFree = reinterpret_cast<const uint32_t*>(
        header.freeIds.data);
    for (uint32_t i = 0; i < header.freeIds.count; ++i) {
        const uint32_t id = sourceFree[i];
        if (id >= header.currentId || entries[id].refCount != 0 ||
            (entries[id].stateFlags & 0x02u) != 0)
            return FailTransformCache(receipt, TransformCacheInvalidFreeId,
                ERROR_INVALID_DATA, 4, i, 9);
        entries[id].stateFlags |= 0x02u;
        freeIds[i] = id;
    }
    receipt->freeWritten = header.freeIds.count;
    for (uint32_t id = 0; id < header.currentId; ++id)
        if ((entries[id].refCount == 0) !=
                ((entries[id].stateFlags & 0x02u) != 0))
            return FailTransformCache(receipt, TransformCacheInvalidFreeId,
                ERROR_INVALID_DATA, 4, id, 10);
    receipt->validationFlags |= 0x08u;

    uint32_t bindingOutput = 0;
    for (uint32_t i = 0; i < interactionHeader.count; ++i) {
        InteractionGraphInteractionRecord interaction = {};
        uintptr_t manager = 0;
        uint32_t cacheIds[2] = {};
        if (!FillInteractionGraphInteraction(interactions[i], 0u, i,
                activeCount, interaction) ||
            !ReadTransformCacheInteractionManager(interaction, manager,
                cacheIds))
            return FailTransformCache(receipt, TransformCacheUnstable,
                ERROR_RETRY, 5, i, 19);
        if (!manager) continue;
        const uintptr_t shapeSims[2] = {
            interaction.element0, interaction.element1
        };
        const uintptr_t shapeCores[2] = {
            interaction.shapeCore0, interaction.shapeCore1
        };
        for (uint32_t endpoint = 0; endpoint < 2; ++endpoint) {
            const uint32_t cacheId = cacheIds[endpoint];
            if (cacheId >= header.currentId ||
                (entries[cacheId].stateFlags & 0x02u) != 0 ||
                entries[cacheId].refCount == 0)
                return FailTransformCache(receipt,
                    TransformCacheInvalidBinding, ERROR_INVALID_DATA,
                    5, bindingOutput, 12);
            TransformCacheBindingRecord& binding = bindings[bindingOutput++];
            ZeroMemory(&binding, sizeof(binding));
            binding.shapeSim = shapeSims[endpoint];
            binding.shapeCore = shapeCores[endpoint];
            binding.pxsShapeCore = shapeCores[endpoint] + 0x20;
            binding.interaction = interactions[i];
            binding.interactionIndex = i;
            binding.endpointIndex = endpoint;
            binding.cacheId = cacheId;
            binding.refCount = entries[cacheId].refCount;
            binding.poseHash = entries[cacheId].poseHash;
            binding.validationFlags = 0x0Fu;
            ++entries[cacheId].bindingCount;
        }
    }
    receipt->bindingsWritten = bindingOutput;
    receipt->validationFlags |= 0x30u;
    if (bindingOutput != receipt->bindingsRequired)
        return FailTransformCache(receipt,
            TransformCacheUnstable, ERROR_RETRY,
            6, 0xFFFFFFFFu, 20);
    if (bindingOutput != receipt->totalRefCount)
        return FailTransformCache(receipt,
            TransformCacheReferenceMismatch, ERROR_INVALID_DATA,
            6, 0xFFFFFFFFu, 13);
    for (uint32_t id = 0; id < header.currentId; ++id)
        if (entries[id].bindingCount != entries[id].refCount)
            return FailTransformCache(receipt,
                TransformCacheReferenceMismatch, ERROR_INVALID_DATA,
                6, id, 14);
    receipt->validationFlags |= 0x40u;

    receipt->entryHash = ByteHash(entries,
        receipt->entriesWritten * sizeof(*entries));
    receipt->freeOrderHash = WordHash(freeIds, receipt->freeWritten);
    receipt->bindingHash = ByteHash(bindings,
        receipt->bindingsWritten * sizeof(*bindings));
    uint32_t snapshotHash = 2166136261u;
    snapshotHash = AppendByteHash(snapshotHash, &receipt->currentId,
        sizeof(receipt->currentId));
    snapshotHash = AppendByteHash(snapshotHash, &receipt->entryHash,
        sizeof(receipt->entryHash));
    snapshotHash = AppendByteHash(snapshotHash, &receipt->freeOrderHash,
        sizeof(receipt->freeOrderHash));
    snapshotHash = AppendByteHash(snapshotHash, &receipt->bindingHash,
        sizeof(receipt->bindingHash));
    receipt->snapshotHash = snapshotHash;

    TransformCacheHeaderState check = {};
    uintptr_t checkScene = 0;
    uintptr_t checkInteractionScene = 0;
    uintptr_t checkContext = 0;
    uintptr_t checkCache = 0;
    if (!ResolveTransformCache(nphaseCore, checkScene, checkInteractionScene,
            checkContext, checkCache) || checkScene != receipt->ownerScene ||
        checkInteractionScene != receipt->interactionScene ||
        checkContext != receipt->context || checkCache != receipt->transformCache ||
        !ReadTransformCacheHeader(checkCache, check) ||
        !SameTransformCacheHeader(header, check) ||
        WordHash(reinterpret_cast<const uint32_t*>(check.freeIds.data),
            check.freeIds.count) != receipt->freeOrderHash)
        return FailTransformCache(receipt, TransformCacheUnstable,
            ERROR_RETRY, 0, 0xFFFFFFFFu, 15);
    for (uint32_t id = 0; id < header.currentId; ++id)
        if (sourceRefs[id] != entries[id].refCount ||
            ByteHash(&sourceTransforms[id], sizeof(PhysxTransform)) !=
                entries[id].poseHash)
            return FailTransformCache(receipt, TransformCacheUnstable,
                ERROR_RETRY, 3, id, 16);
    for (uint32_t i = 0; i < bindingOutput; ++i)
        if (!Readable(reinterpret_cast<const void*>(bindings[i].interaction),
                0x34) ||
            !Readable(reinterpret_cast<const void*>(bindings[i].shapeSim),
                0x20) ||
            *reinterpret_cast<const uintptr_t*>(bindings[i].shapeSim + 0x1C) !=
                bindings[i].shapeCore ||
            *reinterpret_cast<const uint32_t*>(bindings[i].shapeSim + 0x18) !=
                bindings[i].cacheId ||
            !*reinterpret_cast<const uintptr_t*>(bindings[i].interaction +
                0x30) ||
            !Readable(reinterpret_cast<const void*>(
                *reinterpret_cast<const uintptr_t*>(bindings[i].interaction +
                    0x30)), kContactManagerSize) ||
            *reinterpret_cast<const uintptr_t*>(
                *reinterpret_cast<const uintptr_t*>(bindings[i].interaction +
                    0x30) + 0x0C) != bindings[i].interaction - 8u ||
            *reinterpret_cast<const uintptr_t*>(
                *reinterpret_cast<const uintptr_t*>(bindings[i].interaction +
                    0x30) + 0x58 + bindings[i].endpointIndex * 4u) !=
                bindings[i].pxsShapeCore ||
            *reinterpret_cast<const uint32_t*>(
                *reinterpret_cast<const uintptr_t*>(bindings[i].interaction +
                    0x30) + 0x74 + bindings[i].endpointIndex * 4u) !=
                bindings[i].cacheId)
            return FailTransformCache(receipt, TransformCacheUnstable,
                ERROR_RETRY, 5, i, 17);
    receipt->validationFlags |= 0x80u;
    receipt->result = TransformCacheOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

struct IslandElementHeader {
    uintptr_t vtable;
    uintptr_t elements;
    uintptr_t freeNext;
    uintptr_t nextList;
    uint32_t capacity;
    uint32_t freeHead;
    uint32_t freeCount;
};

struct IslandQueueHeader {
    uintptr_t data;
    uint32_t count;
    uint32_t capacity;
    uint32_t defaultCapacity;
};

struct IslandBitmapHeader {
    uintptr_t data;
    uint32_t wordCount;
};

struct IslandHeaderState {
    uintptr_t ownerScene;
    uintptr_t interactionScene;
    uintptr_t context;
    uintptr_t manager;
    IslandElementHeader node;
    IslandElementHeader edge;
    IslandElementHeader island;
    IslandElementHeader root;
    IslandQueueHeader nodeCreated;
    IslandQueueHeader nodeDeleted;
    IslandQueueHeader edgeCreated;
    IslandQueueHeader edgeDeleted;
    IslandQueueHeader edgeBroken;
    IslandQueueHeader edgeJoined;
    IslandBitmapHeader bitmaps[5];
    uint32_t scalars[11];
};

static void HookIslandAddEdge();
static void HookIslandRemoveEdge();
static void HookIslandUpdate();

static bool HasIslandJump(const void* source, const void* destination,
    uint32_t patchSize) {
    if (!source || patchSize < 5u) return false;
    const uint8_t* bytes = static_cast<const uint8_t*>(source);
    if (!Readable(source, patchSize) || bytes[0] != 0xE9) return false;
    for (uint32_t i = 5u; i < patchSize; ++i)
        if (bytes[i] != 0x90) return false;
    const int32_t displacement = *reinterpret_cast<const int32_t*>(bytes + 1u);
    return reinterpret_cast<uintptr_t>(source) + 5u + displacement ==
        reinterpret_cast<uintptr_t>(destination);
}

static bool IslandRevisionMatches(uintptr_t unityBase) {
    const struct Guard { uint32_t rva; const uint8_t* bytes; uint32_t count; }
        guards[] = {
            {kIslandAddEdgeRva, kIslandAddEdgeBytes,
                sizeof(kIslandAddEdgeBytes)},
            {kIslandRemoveEdgeRva, kIslandRemoveEdgeBytes,
                sizeof(kIslandRemoveEdgeBytes)},
            {kIslandPrivateUpdateRva, kIslandPrivateUpdateBytes,
                sizeof(kIslandPrivateUpdateBytes)},
            {kIslandUpdateRva, kIslandUpdateBytes,
                sizeof(kIslandUpdateBytes)},
            {kIslandSecondUpdateRva, kIslandSecondUpdateBytes,
                sizeof(kIslandSecondUpdateBytes)},
            {kPxsContextUpdateIslandsRva, kPxsContextUpdateIslandsBytes,
                sizeof(kPxsContextUpdateIslandsBytes)}
        };
    if (!unityBase) return false;
    for (uint32_t i = 0; i < sizeof(guards) / sizeof(guards[0]); ++i) {
        const void* address = reinterpret_cast<const void*>(unityBase +
            guards[i].rva);
        if (!Readable(address, guards[i].count)) return false;
        if (EqualBytes(address, guards[i].bytes, guards[i].count)) continue;
        if (i == 0u && HasIslandJump(address, HookIslandAddEdge, 6u)) continue;
        if (i == 1u && HasIslandJump(address, HookIslandRemoveEdge, 7u))
            continue;
        if (i == 3u && HasIslandJump(address, HookIslandUpdate, 5u)) continue;
        return false;
    }
    return true;
}

static bool ReadIslandElementHeader(uintptr_t address, uint32_t stride,
    uint32_t hardLimit, bool hasNext, IslandElementHeader& header) {
    ZeroMemory(&header, sizeof(header));
    if (!Readable(reinterpret_cast<const void*>(address),
            hasNext ? 0x1Cu : 0x18u)) return false;
    header.vtable = *reinterpret_cast<const uintptr_t*>(address);
    header.elements = *reinterpret_cast<const uintptr_t*>(address + 4u);
    header.freeNext = *reinterpret_cast<const uintptr_t*>(address + 8u);
    header.capacity = *reinterpret_cast<const uint32_t*>(address + 0x0Cu);
    header.freeHead = *reinterpret_cast<const uint32_t*>(address + 0x10u);
    header.freeCount = *reinterpret_cast<const uint32_t*>(address + 0x14u);
    header.nextList = hasNext ?
        *reinterpret_cast<const uintptr_t*>(address + 0x18u) : 0u;
    if (header.capacity > hardLimit || header.freeCount > header.capacity ||
        (header.freeHead != 0xFFFFFFFFu &&
            header.freeHead >= header.capacity) ||
        (header.capacity && (!header.elements || !header.freeNext ||
            !Readable(reinterpret_cast<const void*>(header.elements),
                header.capacity * stride) ||
            !Readable(reinterpret_cast<const void*>(header.freeNext),
                header.capacity * sizeof(uint32_t)) ||
            (hasNext && (!header.nextList || !Readable(
                reinterpret_cast<const void*>(header.nextList),
                header.capacity * sizeof(uint32_t))))))) return false;
    return true;
}

static bool ReadIslandQueueHeader(uintptr_t pointerAddress,
    uintptr_t countAddress, uint32_t capacity, uint32_t defaultCapacity,
    IslandQueueHeader& header) {
    ZeroMemory(&header, sizeof(header));
    if (!Readable(reinterpret_cast<const void*>(pointerAddress), 4u) ||
        !Readable(reinterpret_cast<const void*>(countAddress), 4u))
        return false;
    header.data = *reinterpret_cast<const uintptr_t*>(pointerAddress);
    header.count = *reinterpret_cast<const uint32_t*>(countAddress);
    header.capacity = capacity;
    header.defaultCapacity = defaultCapacity;
    if (header.capacity > kMaximumIslandQueueEntries ||
        header.count > header.capacity ||
        (header.count && (!header.data || !Readable(
            reinterpret_cast<const void*>(header.data),
            header.count * sizeof(uint32_t))))) return false;
    return true;
}

static bool ReadIslandHeader(uintptr_t unityBase, uintptr_t nphaseCore,
    uintptr_t expectedManager, IslandHeaderState& header) {
    ZeroMemory(&header, sizeof(header));
    if (!nphaseCore || !Readable(reinterpret_cast<const void*>(nphaseCore), 4u))
        return false;
    header.ownerScene = *reinterpret_cast<const uintptr_t*>(nphaseCore);
    if (!header.ownerScene || !Readable(reinterpret_cast<const void*>(
            header.ownerScene + 0x450u), 0x68u) ||
        *reinterpret_cast<const uintptr_t*>(header.ownerScene + 0x450u) !=
            nphaseCore) return false;
    header.interactionScene = *reinterpret_cast<const uintptr_t*>(
        header.ownerScene + 0x4B4u);
    if (!header.interactionScene || !Readable(reinterpret_cast<const void*>(
            header.interactionScene), 0x3F4u) ||
        *reinterpret_cast<const uintptr_t*>(header.interactionScene + 0x3F0u) !=
            header.ownerScene) return false;
    header.context = *reinterpret_cast<const uintptr_t*>(
        header.interactionScene + 0x3E8u);
    header.manager = header.context + 0x181Cu;
    if (!header.context || !Readable(reinterpret_cast<const void*>(
            header.manager), 0x2D8u) ||
        (expectedManager && header.manager != expectedManager)) return false;
    if (!ReadIslandElementHeader(header.manager + 0x0Cu, 12u,
            kMaximumIslandNodes, true, header.node) ||
        !ReadIslandElementHeader(header.manager + 0x118u, 12u,
            kMaximumIslandEdges, true, header.edge) ||
        !ReadIslandElementHeader(header.manager + 0x174u, 16u,
            kMaximumIslands, false, header.island) ||
        !ReadIslandElementHeader(header.manager + 0x1A4u, 8u,
            kMaximumIslandRoots, false, header.root)) return false;
    if (header.node.vtable != unityBase + kIslandNodeVtableRva ||
        header.edge.vtable != unityBase + kIslandEdgeVtableRva ||
        header.island.vtable != unityBase + kIslandManagerVtableRva ||
        header.root.vtable != unityBase + kIslandRootVtableRva) return false;
    const uint32_t nodeQueueCapacity = *reinterpret_cast<const uint32_t*>(
        header.manager + 0x144u);
    const uint32_t nodeQueueDefault = *reinterpret_cast<const uint32_t*>(
        header.manager + 0x148u);
    const uint32_t edgeQueueCapacity = *reinterpret_cast<const uint32_t*>(
        header.manager + 0x16Cu);
    const uint32_t edgeQueueDefault = *reinterpret_cast<const uint32_t*>(
        header.manager + 0x170u);
    if (!ReadIslandQueueHeader(header.manager + 0x134u,
            header.manager + 0x138u, nodeQueueCapacity, nodeQueueDefault,
            header.nodeCreated) ||
        !ReadIslandQueueHeader(header.manager + 0x13Cu,
            header.manager + 0x140u, nodeQueueCapacity, nodeQueueDefault,
            header.nodeDeleted) ||
        !ReadIslandQueueHeader(header.manager + 0x14Cu,
            header.manager + 0x150u, edgeQueueCapacity, edgeQueueDefault,
            header.edgeCreated) ||
        !ReadIslandQueueHeader(header.manager + 0x154u,
            header.manager + 0x158u, edgeQueueCapacity, edgeQueueDefault,
            header.edgeDeleted) ||
        !ReadIslandQueueHeader(header.manager + 0x15Cu,
            header.manager + 0x160u, edgeQueueCapacity, edgeQueueDefault,
            header.edgeBroken) ||
        !ReadIslandQueueHeader(header.manager + 0x164u,
            header.manager + 0x168u, edgeQueueCapacity, edgeQueueDefault,
            header.edgeJoined)) return false;
    for (uint32_t i = 0; i < 4u; ++i) {
        header.bitmaps[i].data = *reinterpret_cast<const uintptr_t*>(
            header.manager + 0x28u + i * 4u);
        header.bitmaps[i].wordCount = *reinterpret_cast<const uint32_t*>(
            header.manager + 0x38u + i * 4u);
        if (header.bitmaps[i].wordCount >
                (kMaximumIslandNodes + 31u) / 32u ||
            (header.bitmaps[i].wordCount && (!header.bitmaps[i].data ||
                !Readable(reinterpret_cast<const void*>(
                    header.bitmaps[i].data),
                    header.bitmaps[i].wordCount * sizeof(uint32_t)))))
            return false;
    }
    header.bitmaps[4].data = *reinterpret_cast<const uintptr_t*>(
        header.manager + 0x19Cu);
    header.bitmaps[4].wordCount = *reinterpret_cast<const uint32_t*>(
        header.manager + 0x1A0u);
    if (header.bitmaps[4].wordCount >
            (kMaximumIslands + 31u) / 32u ||
        (header.bitmaps[4].wordCount && (!header.bitmaps[4].data ||
            !Readable(reinterpret_cast<const void*>(header.bitmaps[4].data),
                header.bitmaps[4].wordCount * sizeof(uint32_t)))))
        return false;
    for (uint32_t i = 0; i < 8u; ++i)
        header.scalars[i] = *reinterpret_cast<const uint32_t*>(
            header.manager + 0x1BCu + i * 4u);
    header.scalars[8] = *reinterpret_cast<const uint8_t*>(
        header.manager + 0x1DCu);
    header.scalars[9] = *reinterpret_cast<const uint8_t*>(
        header.manager + 0x1DDu);
    header.scalars[10] = *reinterpret_cast<const uint8_t*>(
        header.manager + 0x1DEu);
    return true;
}

static void FillIslandElementReceipt(const IslandElementHeader& header,
    IslandElementManagerReceipt& receipt, uint32_t required) {
    receipt.vtable = static_cast<uint32_t>(header.vtable);
    receipt.elements = static_cast<uint32_t>(header.elements);
    receipt.freeNext = static_cast<uint32_t>(header.freeNext);
    receipt.nextList = static_cast<uint32_t>(header.nextList);
    receipt.capacity = header.capacity;
    receipt.freeHead = header.freeHead;
    receipt.freeCount = header.freeCount;
    receipt.required = required;
}

static void FillIslandQueueReceipt(const IslandQueueHeader& header,
    IslandQueueReceipt& receipt) {
    receipt.data = static_cast<uint32_t>(header.data);
    receipt.count = header.count;
    receipt.capacity = header.capacity;
    receipt.defaultCapacity = header.defaultCapacity;
    receipt.required = header.count;
}

static void FillIslandBitmapReceipt(const IslandBitmapHeader& header,
    IslandBitmapReceipt& receipt) {
    receipt.data = static_cast<uint32_t>(header.data);
    receipt.wordCount = header.wordCount;
    receipt.required = header.wordCount;
}

static int FailIslandSnapshot(IslandSnapshotReceiptV1* receipt,
    IslandSnapshotResult result, uint32_t error, uint32_t kind,
    uint32_t index, uint32_t detail) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
        receipt->invalidKind = kind;
        receipt->invalidIndex = index;
        receipt->detail = detail;
    }
    return 0;
}

static bool SameIslandHeader(const IslandHeaderState& left,
    const IslandHeaderState& right) {
    return EqualBytes(&left, reinterpret_cast<const uint8_t*>(&right),
        sizeof(left));
}

static bool IslandBufferWritable(const void* pointer, uint32_t capacity,
    uint32_t required, uint32_t stride) {
    if (capacity < required) return false;
    return required == 0u || (pointer && Writable(const_cast<void*>(pointer),
        required * stride));
}

static uint32_t HashIslandNodeRaw(const IslandNodeSlotRecord* records,
    uint32_t count) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash = AppendByteHash(hash, &records[i].ownerOrArticulationRaw, 4u);
        hash = AppendByteHash(hash, &records[i].islandId, 4u);
        hash = AppendByteHash(hash, &records[i].rawFlagsWord, 4u);
    }
    return hash;
}

static uint32_t HashIslandEdgeRaw(const IslandEdgeSlotRecord* records,
    uint32_t count) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash = AppendByteHash(hash, &records[i].node0, 4u);
        hash = AppendByteHash(hash, &records[i].node1, 4u);
        hash = AppendByteHash(hash, &records[i].taggedObjectRaw, 4u);
    }
    return hash;
}

static uint32_t HashIslandRaw(const IslandSlotRecord* records,
    uint32_t count) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash = AppendByteHash(hash, &records[i].startNode, 4u);
        hash = AppendByteHash(hash, &records[i].startEdge, 4u);
        hash = AppendByteHash(hash, &records[i].endNode, 4u);
        hash = AppendByteHash(hash, &records[i].endEdge, 4u);
    }
    return hash;
}

static uint32_t HashIslandRootRaw(
    const IslandArticulationRootSlotRecord* records, uint32_t count) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash = AppendByteHash(hash, &records[i].articulationLinkHandle, 4u);
        hash = AppendByteHash(hash, &records[i].articulationOwner, 4u);
    }
    return hash;
}

static uint32_t HashIslandRecordWord(const void* records, uint32_t count,
    uint32_t stride, uint32_t offset) {
    uint32_t hash = 2166136261u;
    const uint8_t* bytes = static_cast<const uint8_t*>(records);
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= *reinterpret_cast<const uint32_t*>(
            bytes + i * stride + offset);
        hash *= 16777619u;
    }
    return hash;
}

static uint32_t IslandJournalCurrentNext() {
    return static_cast<uint32_t>(InterlockedCompareExchange(
        &g_islandJournalNextOrdinal, 0, 0)) + 1u;
}

static uint32_t IslandJournalCurrentFirst(uint32_t next) {
    return next > kIslandJournalCapacity ?
        next - kIslandJournalCapacity : 1u;
}

static int CaptureIslandSnapshot(uintptr_t unityBase, uintptr_t nphaseCore,
    uint32_t expectedPhase, const IslandSnapshotBuffersV1* buffers,
    IslandSnapshotReceiptV1* receipt) {
    if (!receipt || !Writable(receipt, sizeof(*receipt))) return 0;
    ZeroMemory(receipt, sizeof(*receipt));
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(*receipt);
    receipt->unityBase = static_cast<uint32_t>(unityBase);
    receipt->nphaseCore = static_cast<uint32_t>(nphaseCore);
    receipt->phase = expectedPhase;
    receipt->captureThreadId = GetCurrentThreadId();
    receipt->invalidIndex = 0xFFFFFFFFu;
    if (!unityBase || !nphaseCore || !buffers ||
        !Readable(buffers, sizeof(*buffers)))
        return FailIslandSnapshot(receipt, IslandSnapshotBadArgument,
            ERROR_INVALID_PARAMETER, 0u, 0xFFFFFFFFu, 1u);
    if (!IslandRevisionMatches(unityBase))
        return FailIslandSnapshot(receipt, IslandSnapshotRevisionMismatch,
            ERROR_REVISION_MISMATCH, 1u, 0xFFFFFFFFu, 2u);
    const LONG epoch = InterlockedCompareExchange(&g_islandEpoch, 0, 0);
    const LONG capturePhase = InterlockedCompareExchange(
        &g_islandCapturePhase, 0, 0);
    if ((expectedPhase == IslandSnapshotSettled && ((epoch & 1) != 0 ||
            capturePhase != IslandSnapshotSettled)) ||
        ((expectedPhase == IslandSnapshotPreUpdate ||
            expectedPhase == IslandSnapshotPostUpdate) &&
            (InterlockedCompareExchange(&g_islandState, 0, 0) !=
                IslandObserverCapturing || capturePhase !=
                    static_cast<LONG>(expectedPhase) || (epoch & 1) == 0)) ||
        (expectedPhase < IslandSnapshotSettled ||
            expectedPhase > IslandSnapshotPostUpdate))
        return FailIslandSnapshot(receipt, IslandSnapshotInvalidPhase,
            ERROR_INVALID_STATE, 9u, 0xFFFFFFFFu, 3u);

    IslandHeaderState header = {};
    if (!ReadIslandHeader(unityBase, nphaseCore,
            expectedPhase == IslandSnapshotSettled ? 0u :
                g_islandExpectedManager, header))
        return FailIslandSnapshot(receipt, IslandSnapshotInvalidIdentity,
            ERROR_INVALID_DATA, 1u, 0xFFFFFFFFu, 4u);
    receipt->ownerScene = static_cast<uint32_t>(header.ownerScene);
    receipt->interactionScene = static_cast<uint32_t>(header.interactionScene);
    receipt->context = static_cast<uint32_t>(header.context);
    receipt->islandManager = static_cast<uint32_t>(header.manager);
    receipt->observerSequence = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_islandObserverSequence, 0, 0));
    receipt->observationOrdinal = expectedPhase == IslandSnapshotSettled ? 0u :
        g_islandArmedOrdinal;
    receipt->epoch = static_cast<uint32_t>(epoch);
    FillIslandElementReceipt(header.node, receipt->node, header.node.capacity);
    FillIslandElementReceipt(header.edge, receipt->edge, header.edge.capacity);
    FillIslandElementReceipt(header.island, receipt->island,
        header.island.capacity);
    FillIslandElementReceipt(header.root, receipt->root, header.root.capacity);
    FillIslandQueueReceipt(header.nodeCreated, receipt->nodeCreated);
    FillIslandQueueReceipt(header.nodeDeleted, receipt->nodeDeleted);
    FillIslandQueueReceipt(header.edgeCreated, receipt->edgeCreated);
    FillIslandQueueReceipt(header.edgeDeleted, receipt->edgeDeleted);
    FillIslandQueueReceipt(header.edgeBroken, receipt->edgeBroken);
    FillIslandQueueReceipt(header.edgeJoined, receipt->edgeJoined);
    for (uint32_t i = 0; i < 5u; ++i) {
        IslandBitmapReceipt* bitmap = i == 0u ? &receipt->kinematic :
            i == 1u ? &receipt->kinematicChange :
            i == 2u ? &receipt->notReady :
            i == 3u ? &receipt->notReadyChange : &receipt->islandBitmap;
        FillIslandBitmapReceipt(header.bitmaps[i], *bitmap);
    }
    receipt->numAddedRBodies = header.scalars[0];
    receipt->numAddedArtics = header.scalars[1];
    receipt->numAddedKinematics = header.scalars[2];
    receipt->numAddedEdgesContact = header.scalars[3];
    receipt->numAddedEdgesConstraint = header.scalars[4];
    receipt->numAddedEdgesArticulation = header.scalars[5];
    receipt->numEdgeReferencesToKinematic = header.scalars[6];
    receipt->numRequiredKinematicDuplicates = header.scalars[7];
    receipt->everythingAsleep = header.scalars[8];
    receipt->hasAnythingChanged = header.scalars[9];
    receipt->performIslandUpdate = header.scalars[10];
    receipt->journalEndOrdinal = IslandJournalCurrentNext();
    receipt->journalBeginOrdinal = IslandJournalCurrentFirst(
        receipt->journalEndOrdinal);
    receipt->journalOverflowCount = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_islandJournalOverflow, 0, 0));

    InteractionGraphArrayHeader contactInteractions = {};
    if (!ReadInteractionGraphArray(header.interactionScene + 0x10u,
            contactInteractions, kMaximumIslandBindings))
        return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
            ERROR_INVALID_DATA, 8u, 0xFFFFFFFFu, 5u);
    uint32_t bindingRequired = 0;
    uint32_t bindingEdgeSeen[(kMaximumIslandEdges + 31u) / 32u] = {};
    uint32_t journalEdgeDecided[(kMaximumIslandEdges + 31u) / 32u] = {};
    uint32_t edgeFreeSeen[(kMaximumIslandEdges + 31u) / 32u] = {};
    uint32_t edgeDeletedSeen[(kMaximumIslandEdges + 31u) / 32u] = {};
    uint32_t freeEdge = header.edge.freeHead;
    for (uint32_t i = 0; i < header.edge.freeCount; ++i) {
        if (freeEdge >= header.edge.capacity ||
            (edgeFreeSeen[freeEdge >> 5] &
                (1u << (freeEdge & 31u))) != 0u)
            return FailIslandSnapshot(receipt,
                IslandSnapshotInvalidFreeChain, ERROR_INVALID_DATA,
                3u, freeEdge, 4u);
        edgeFreeSeen[freeEdge >> 5] |= 1u << (freeEdge & 31u);
        freeEdge = *reinterpret_cast<const uint32_t*>(
            header.edge.freeNext + freeEdge * 4u);
    }
    if (freeEdge != 0xFFFFFFFFu)
        return FailIslandSnapshot(receipt, IslandSnapshotInvalidFreeChain,
            ERROR_INVALID_DATA, 3u, freeEdge, 5u);
    const uint32_t* deletedEdges = reinterpret_cast<const uint32_t*>(
        header.edgeDeleted.data);
    for (uint32_t i = 0; i < header.edgeDeleted.count; ++i) {
        const uint32_t edgeId = deletedEdges[i];
        if (edgeId >= header.edge.capacity)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidQueue,
                ERROR_INVALID_DATA, 5u, i, 3u);
        edgeDeletedSeen[edgeId >> 5] |= 1u << (edgeId & 31u);
    }
    const uintptr_t* interactionValues = reinterpret_cast<const uintptr_t*>(
        contactInteractions.data);
    const uint32_t contactOrderHash = OrderHash(interactionValues,
        contactInteractions.count);
    for (uint32_t i = 0; i < contactInteractions.count; ++i) {
        const uintptr_t secondary = interactionValues[i];
        if (!secondary || secondary < 8u || !Readable(
                reinterpret_cast<const void*>(secondary - 8u), 0x40u))
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_NOACCESS, 8u, i, 6u);
        uint32_t edgeId = *reinterpret_cast<const uint32_t*>(
            secondary + 0x34u);
        if (edgeId == 0xFFFFFFFFu) continue;
        if (edgeId >= header.edge.capacity ||
            (edgeFreeSeen[edgeId >> 5] & (1u << (edgeId & 31u))) != 0u ||
            (bindingEdgeSeen[edgeId >> 5] &
                (1u << (edgeId & 31u))) != 0u)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_INVALID_DATA, 8u, i, 7u);
        bindingEdgeSeen[edgeId >> 5] |= 1u << (edgeId & 31u);
        ++bindingRequired;
    }
    // Binding recovery uses the complete retained journal.  The observer's
    // exported transition interval still begins at arm time, but a D edge can
    // already be pending at that settled boundary.
    const uint32_t journalScanBegin = receipt->journalBeginOrdinal;
    for (uint32_t ordinal = receipt->journalEndOrdinal;
            ordinal-- > journalScanBegin;) {
        IslandJournalSlot& slot = g_islandJournal[
            (ordinal - 1u) % kIslandJournalCapacity];
        if (static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal)
            return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
                ERROR_IO_PENDING, 8u, ordinal, 45u);
        const IslandEdgeJournalRecord record = slot.record;
        if (record.ordinal != ordinal ||
            (record.eventKind != 1u && record.eventKind != 2u) ||
            static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal)
            return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
                ERROR_RETRY, 8u, ordinal, 46u);
        const uint32_t edgeId = record.eventKind == 1u ?
            record.postEdgeId : record.preEdgeId;
        if (edgeId == 0xFFFFFFFFu) {
            if (record.eventKind == 2u)
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidBinding, ERROR_INVALID_DATA,
                    8u, ordinal, 47u);
            continue;
        }
        if (edgeId >= header.edge.capacity)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_INVALID_DATA, 8u, ordinal, 48u);
        if ((edgeFreeSeen[edgeId >> 5] & (1u << (edgeId & 31u))) != 0u ||
            (bindingEdgeSeen[edgeId >> 5] &
                (1u << (edgeId & 31u))) != 0u ||
            (journalEdgeDecided[edgeId >> 5] &
                (1u << (edgeId & 31u))) != 0u)
            continue;
        journalEdgeDecided[edgeId >> 5] |= 1u << (edgeId & 31u);
        if (record.eventKind == 1u || record.edgeType != 0u) continue;
        const uint32_t tagged = *reinterpret_cast<const uint32_t*>(
            header.edge.elements + edgeId * 12u + 8u);
        if ((tagged & 8u) == 0u ||
            (edgeDeletedSeen[edgeId >> 5] &
                (1u << (edgeId & 31u))) == 0u) continue;
        if (record.validationFlags != 0x1Fu || !record.ownerObject ||
            record.hookAddress != record.ownerObject + 0x3Cu ||
            !record.pxsShapeCoreLow ||
            record.pxsShapeCoreLow >= record.pxsShapeCoreHigh)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_INVALID_DATA, 8u, ordinal, 49u);
        bindingEdgeSeen[edgeId >> 5] |= 1u << (edgeId & 31u);
        ++bindingRequired;
    }
    receipt->bindingsRequired = bindingRequired;

    const bool capacityOk =
        IslandBufferWritable(buffers->nodes, buffers->nodeCapacity,
            header.node.capacity, sizeof(IslandNodeSlotRecord)) &&
        IslandBufferWritable(buffers->edges, buffers->edgeCapacity,
            header.edge.capacity, sizeof(IslandEdgeSlotRecord)) &&
        IslandBufferWritable(buffers->islands, buffers->islandCapacity,
            header.island.capacity, sizeof(IslandSlotRecord)) &&
        IslandBufferWritable(buffers->roots, buffers->rootCapacity,
            header.root.capacity, sizeof(IslandArticulationRootSlotRecord)) &&
        IslandBufferWritable(buffers->kinematicWords,
            buffers->kinematicWordCapacity, header.bitmaps[0].wordCount, 4u) &&
        IslandBufferWritable(buffers->kinematicChangeWords,
            buffers->kinematicChangeWordCapacity,
            header.bitmaps[1].wordCount, 4u) &&
        IslandBufferWritable(buffers->notReadyWords,
            buffers->notReadyWordCapacity, header.bitmaps[2].wordCount, 4u) &&
        IslandBufferWritable(buffers->notReadyChangeWords,
            buffers->notReadyChangeWordCapacity,
            header.bitmaps[3].wordCount, 4u) &&
        IslandBufferWritable(buffers->islandWords,
            buffers->islandWordCapacity, header.bitmaps[4].wordCount, 4u) &&
        IslandBufferWritable(buffers->nodeCreated,
            buffers->nodeCreatedCapacity, header.nodeCreated.count, 4u) &&
        IslandBufferWritable(buffers->nodeDeleted,
            buffers->nodeDeletedCapacity, header.nodeDeleted.count, 4u) &&
        IslandBufferWritable(buffers->edgeCreated,
            buffers->edgeCreatedCapacity, header.edgeCreated.count, 4u) &&
        IslandBufferWritable(buffers->edgeDeleted,
            buffers->edgeDeletedCapacity, header.edgeDeleted.count, 4u) &&
        IslandBufferWritable(buffers->edgeBroken,
            buffers->edgeBrokenCapacity, header.edgeBroken.count, 4u) &&
        IslandBufferWritable(buffers->edgeJoined,
            buffers->edgeJoinedCapacity, header.edgeJoined.count, 4u) &&
        IslandBufferWritable(buffers->bindings, buffers->bindingCapacity,
            bindingRequired, sizeof(IslandSipEdgeBinding));
    if (!capacityOk)
        return FailIslandSnapshot(receipt, IslandSnapshotCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER, 2u, 0xFFFFFFFFu, 7u);

    for (uint32_t i = 0; i < header.node.capacity; ++i) {
        IslandNodeSlotRecord& record = buffers->nodes[i];
        ZeroMemory(&record, sizeof(record));
        const uintptr_t source = header.node.elements + i * 12u;
        record.id = i;
        record.ownerOrArticulationRaw = *reinterpret_cast<const uint32_t*>(source);
        record.islandId = *reinterpret_cast<const uint32_t*>(source + 4u);
        record.rawFlagsWord = *reinterpret_cast<const uint32_t*>(source + 8u);
        record.freeNext = *reinterpret_cast<const uint32_t*>(
            header.node.freeNext + i * 4u);
        record.nextNode = *reinterpret_cast<const uint32_t*>(
            header.node.nextList + i * 4u);
    }
    receipt->node.written = header.node.capacity;
    for (uint32_t i = 0; i < header.edge.capacity; ++i) {
        IslandEdgeSlotRecord& record = buffers->edges[i];
        ZeroMemory(&record, sizeof(record));
        const uintptr_t source = header.edge.elements + i * 12u;
        record.id = i;
        record.node0 = *reinterpret_cast<const uint32_t*>(source);
        record.node1 = *reinterpret_cast<const uint32_t*>(source + 4u);
        record.taggedObjectRaw = *reinterpret_cast<const uint32_t*>(source + 8u);
        record.freeNext = *reinterpret_cast<const uint32_t*>(
            header.edge.freeNext + i * 4u);
        record.nextEdge = *reinterpret_cast<const uint32_t*>(
            header.edge.nextList + i * 4u);
        record.semanticBindingIndex = 0xFFFFFFFFu;
    }
    receipt->edge.written = header.edge.capacity;
    for (uint32_t i = 0; i < header.island.capacity; ++i) {
        IslandSlotRecord& record = buffers->islands[i];
        ZeroMemory(&record, sizeof(record));
        const uintptr_t source = header.island.elements + i * 16u;
        record.id = i;
        record.startNode = *reinterpret_cast<const uint32_t*>(source);
        record.startEdge = *reinterpret_cast<const uint32_t*>(source + 4u);
        record.endNode = *reinterpret_cast<const uint32_t*>(source + 8u);
        record.endEdge = *reinterpret_cast<const uint32_t*>(source + 12u);
        record.freeNext = *reinterpret_cast<const uint32_t*>(
            header.island.freeNext + i * 4u);
    }
    receipt->island.written = header.island.capacity;
    for (uint32_t i = 0; i < header.root.capacity; ++i) {
        IslandArticulationRootSlotRecord& record = buffers->roots[i];
        ZeroMemory(&record, sizeof(record));
        const uintptr_t source = header.root.elements + i * 8u;
        record.id = i;
        record.articulationLinkHandle = *reinterpret_cast<const uint32_t*>(source);
        record.articulationOwner = *reinterpret_cast<const uint32_t*>(source + 4u);
        record.freeNext = *reinterpret_cast<const uint32_t*>(
            header.root.freeNext + i * 4u);
    }
    receipt->root.written = header.root.capacity;

    struct FreeWalk { IslandElementHeader* source; uint32_t count;
        uint32_t kind; } freeWalks[] = {
        {&header.node, header.node.capacity, 0u},
        {&header.edge, header.edge.capacity, 1u},
        {&header.island, header.island.capacity, 2u},
        {&header.root, header.root.capacity, 3u}
    };
    for (uint32_t kind = 0; kind < 4u; ++kind) {
        IslandElementHeader& source = *freeWalks[kind].source;
        uint32_t id = source.freeHead;
        uint32_t hash = 2166136261u;
        for (uint32_t visited = 0; visited < source.freeCount; ++visited) {
            if (id >= source.capacity)
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidFreeChain, ERROR_INVALID_DATA,
                    3u, id, kind);
            uint32_t* flags = kind == 0u ? &buffers->nodes[id].slotFlags :
                kind == 1u ? &buffers->edges[id].slotFlags :
                kind == 2u ? &buffers->islands[id].slotFlags :
                    &buffers->roots[id].slotFlags;
            if ((*flags & 2u) != 0u)
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidFreeChain, ERROR_DUP_NAME,
                    3u, id, kind);
            *flags |= 2u;
            hash = AppendByteHash(hash, &id, sizeof(id));
            id = *reinterpret_cast<const uint32_t*>(source.freeNext + id * 4u);
        }
        if (id != 0xFFFFFFFFu)
            return FailIslandSnapshot(receipt,
                IslandSnapshotInvalidFreeChain, ERROR_INVALID_DATA,
                3u, id, 10u + kind);
        IslandElementManagerReceipt* output = kind == 0u ? &receipt->node :
            kind == 1u ? &receipt->edge : kind == 2u ? &receipt->island :
                &receipt->root;
        output->freeChainHash = hash;
    }
    for (uint32_t i = 0; i < header.node.capacity; ++i)
        if ((buffers->nodes[i].slotFlags & 2u) == 0u)
            buffers->nodes[i].slotFlags |= 1u;
    for (uint32_t i = 0; i < header.edge.capacity; ++i)
        if ((buffers->edges[i].slotFlags & 2u) == 0u) {
            buffers->edges[i].slotFlags |= 1u;
            if ((buffers->edges[i].taggedObjectRaw & 1u) == 0u)
                ++receipt->liveContactEdges;
            else if ((buffers->edges[i].taggedObjectRaw & ~0xFu) != 0u)
                ++receipt->liveConstraintEdges;
            else
                ++receipt->liveArticulationEdges;
        }
    for (uint32_t i = 0; i < header.island.capacity; ++i)
        if ((buffers->islands[i].slotFlags & 2u) == 0u)
            buffers->islands[i].slotFlags |= 1u;
    for (uint32_t i = 0; i < header.root.capacity; ++i)
        if ((buffers->roots[i].slotFlags & 2u) == 0u)
            buffers->roots[i].slotFlags |= 1u;

    uint32_t* bitmapOutputs[] = {buffers->kinematicWords,
        buffers->kinematicChangeWords, buffers->notReadyWords,
        buffers->notReadyChangeWords, buffers->islandWords};
    IslandBitmapReceipt* bitmapReceipts[] = {&receipt->kinematic,
        &receipt->kinematicChange, &receipt->notReady,
        &receipt->notReadyChange, &receipt->islandBitmap};
    for (uint32_t b = 0; b < 5u; ++b) {
        const uint32_t words = header.bitmaps[b].wordCount;
        if (words) CopyBytes(bitmapOutputs[b],
            reinterpret_cast<const void*>(header.bitmaps[b].data), words * 4u);
        bitmapReceipts[b]->written = words;
        bitmapReceipts[b]->hash = WordHash(bitmapOutputs[b], words);
        const uint32_t capacity = b < 4u ? header.node.capacity :
            header.island.capacity;
        const uint32_t requiredWords = (capacity + 31u) / 32u;
        if (words != requiredWords || (words && (capacity & 31u) != 0u &&
            (bitmapOutputs[b][requiredWords - 1u] &
                ~((1u << (capacity & 31u)) - 1u)) != 0u))
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBitmap,
                ERROR_INVALID_DATA, 4u, b, 20u);
        for (uint32_t id = 0; id < capacity; ++id) {
            if ((bitmapOutputs[b][id >> 5] & (1u << (id & 31u))) == 0u)
                continue;
            if (b < 4u) buffers->nodes[id].slotFlags |= 1u << (4u + b);
            else buffers->islands[id].slotFlags |= 4u;
        }
    }

    struct QueueCopy { const IslandQueueHeader* source; uint32_t* output;
        IslandQueueReceipt* receipt; uint32_t bit; bool node; } queues[] = {
        {&header.nodeCreated,buffers->nodeCreated,&receipt->nodeCreated,8u,true},
        {&header.nodeDeleted,buffers->nodeDeleted,&receipt->nodeDeleted,9u,true},
        {&header.edgeCreated,buffers->edgeCreated,&receipt->edgeCreated,4u,false},
        {&header.edgeDeleted,buffers->edgeDeleted,&receipt->edgeDeleted,5u,false},
        {&header.edgeBroken,buffers->edgeBroken,&receipt->edgeBroken,6u,false},
        {&header.edgeJoined,buffers->edgeJoined,&receipt->edgeJoined,7u,false}
    };
    for (uint32_t q = 0; q < 6u; ++q) {
        const IslandQueueHeader& source = *queues[q].source;
        if (source.count) CopyBytes(queues[q].output,
            reinterpret_cast<const void*>(source.data), source.count * 4u);
        queues[q].receipt->written = source.count;
        queues[q].receipt->hash = WordHash(queues[q].output, source.count);
        const uint32_t limit = queues[q].node ? header.node.capacity :
            header.edge.capacity;
        for (uint32_t j = 0; j < source.count; ++j) {
            const uint32_t id = queues[q].output[j];
            if (id >= limit || (queues[q].node ?
                    (buffers->nodes[id].slotFlags & 2u) :
                    (buffers->edges[id].slotFlags & 2u)))
                return FailIslandSnapshot(receipt, IslandSnapshotInvalidQueue,
                    ERROR_INVALID_DATA, 5u, j, q);
            // C and D retain their elements until updateIslands consumes the
            // queue.  Require the matching source tag while preserving the
            // exact queue order and multiplicity.  B/J are event streams and
            // deliberately have no uniqueness or tag-bit invariant.
            if ((q == 0u &&
                    (buffers->nodes[id].rawFlagsWord & 0x40u) == 0u) ||
                (q == 1u &&
                    (buffers->nodes[id].rawFlagsWord & 0x20u) == 0u) ||
                (q == 2u &&
                    (buffers->edges[id].taggedObjectRaw & 4u) == 0u) ||
                (q == 3u &&
                    (buffers->edges[id].taggedObjectRaw & 8u) == 0u))
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidQueue, ERROR_INVALID_DATA,
                    5u, j, 10u + q);
            if (queues[q].node)
                buffers->nodes[id].slotFlags |= 1u << queues[q].bit;
            else buffers->edges[id].slotFlags |= 1u << queues[q].bit;
        }
    }

    for (uint32_t islandId = 0; islandId < header.island.capacity; ++islandId) {
        IslandSlotRecord& island = buffers->islands[islandId];
        if ((island.slotFlags & 4u) == 0u) continue;
        if ((island.slotFlags & 2u) != 0u)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidTopology,
                ERROR_INVALID_DATA, 6u, islandId, 30u);
        uint32_t id = island.startNode;
        uint32_t last = 0xFFFFFFFFu;
        uint32_t walked = 0;
        while (id != 0xFFFFFFFFu && walked++ < header.node.capacity) {
            if (id >= header.node.capacity ||
                (buffers->nodes[id].slotFlags & (2u | 4u)) != 0u ||
                buffers->nodes[id].islandId != islandId)
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidTopology, ERROR_INVALID_DATA,
                    6u, islandId, 31u);
            buffers->nodes[id].slotFlags |= 4u;
            last = id;
            id = buffers->nodes[id].nextNode;
        }
        if (id != 0xFFFFFFFFu || last != island.endNode)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidTopology,
                ERROR_INVALID_DATA, 6u, islandId, 32u);
        id = island.startEdge;
        last = 0xFFFFFFFFu;
        walked = 0;
        while (id != 0xFFFFFFFFu && walked++ < header.edge.capacity) {
            if (id >= header.edge.capacity ||
                (buffers->edges[id].slotFlags & (2u | 4u)) != 0u ||
                (buffers->edges[id].node0 != 0xFFFFFFFFu &&
                    buffers->edges[id].node0 >= header.node.capacity) ||
                (buffers->edges[id].node1 != 0xFFFFFFFFu &&
                    buffers->edges[id].node1 >= header.node.capacity))
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidTopology, ERROR_INVALID_DATA,
                    6u, islandId, 33u);
            buffers->edges[id].slotFlags |= 4u;
            last = id;
            id = buffers->edges[id].nextEdge;
        }
        if (id != 0xFFFFFFFFu || last != island.endEdge)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidTopology,
                ERROR_INVALID_DATA, 6u, islandId, 34u);
    }

    ZeroMemory(bindingEdgeSeen, sizeof(bindingEdgeSeen));
    ZeroMemory(journalEdgeDecided, sizeof(journalEdgeDecided));
    uint32_t bindingOutput = 0;
    for (uint32_t i = 0; i < contactInteractions.count; ++i) {
        const uintptr_t secondary = interactionValues[i];
        const uintptr_t primary = secondary - 8u;
        const uintptr_t shape0 = *reinterpret_cast<const uintptr_t*>(
            primary + 0x20u);
        const uintptr_t shape1 = *reinterpret_cast<const uintptr_t*>(
            primary + 0x24u);
        const uintptr_t manager = *reinterpret_cast<const uintptr_t*>(
            primary + 0x38u);
        const uint32_t edgeId = *reinterpret_cast<const uint32_t*>(
            primary + 0x3Cu);
        if (edgeId == 0xFFFFFFFFu) continue;
        uintptr_t pxs0Value = 0, pxs1Value = 0;
        if (!ReadShapeSimPxsShapeCore(shape0, pxs0Value) ||
            !ReadShapeSimPxsShapeCore(shape1, pxs1Value))
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_INVALID_DATA, 8u, i, 40u);
        const uint32_t pxs0 = static_cast<uint32_t>(pxs0Value);
        const uint32_t pxs1 = static_cast<uint32_t>(pxs1Value);
        uint32_t low = pxs0 < pxs1 ? pxs0 : pxs1;
        uint32_t high = pxs0 < pxs1 ? pxs1 : pxs0;
        if (edgeId >= header.edge.capacity ||
            (buffers->edges[edgeId].slotFlags & 2u) != 0u ||
            buffers->edges[edgeId].semanticBindingIndex != 0xFFFFFFFFu ||
            (buffers->edges[edgeId].taggedObjectRaw & ~0xFu) != manager ||
            (manager && (!Readable(reinterpret_cast<const void*>(manager),
                0x10u) || *reinterpret_cast<const uintptr_t*>(
                    manager + 0x0Cu) != primary)))
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_INVALID_DATA, 8u, i, 41u);
        IslandSipEdgeBinding& binding = buffers->bindings[bindingOutput];
        ZeroMemory(&binding, sizeof(binding));
        binding.edgeId = edgeId;
        binding.edgeType = 0u;
        binding.sip = static_cast<uint32_t>(primary);
        binding.hookAddress = static_cast<uint32_t>(primary + 0x3Cu);
        binding.shapeSim0 = static_cast<uint32_t>(shape0);
        binding.shapeSim1 = static_cast<uint32_t>(shape1);
        binding.pxsShapeCoreLow = low;
        binding.pxsShapeCoreHigh = high;
        binding.contactManager = static_cast<uint32_t>(manager);
        binding.taggedObjectRaw = buffers->edges[edgeId].taggedObjectRaw;
        binding.validationFlags = 0x1Fu;
        buffers->edges[edgeId].semanticBindingIndex = bindingOutput;
        bindingEdgeSeen[edgeId >> 5] |= 1u << (edgeId & 31u);
        ++bindingOutput;
    }
    for (uint32_t ordinal = receipt->journalEndOrdinal;
            ordinal-- > journalScanBegin;) {
        IslandJournalSlot& slot = g_islandJournal[
            (ordinal - 1u) % kIslandJournalCapacity];
        if (static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal)
            return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
                ERROR_IO_PENDING, 8u, ordinal, 45u);
        const IslandEdgeJournalRecord record = slot.record;
        if (record.ordinal != ordinal ||
            (record.eventKind != 1u && record.eventKind != 2u) ||
            static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal)
            return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
                ERROR_RETRY, 8u, ordinal, 46u);
        const uint32_t edgeId = record.eventKind == 1u ?
            record.postEdgeId : record.preEdgeId;
        if (edgeId == 0xFFFFFFFFu) {
            if (record.eventKind == 2u)
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidBinding, ERROR_INVALID_DATA,
                    8u, ordinal, 47u);
            continue;
        }
        if (edgeId >= header.edge.capacity)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_INVALID_DATA, 8u, ordinal, 48u);
        if ((buffers->edges[edgeId].slotFlags & 2u) != 0u ||
            (bindingEdgeSeen[edgeId >> 5] &
                (1u << (edgeId & 31u))) != 0u ||
            (journalEdgeDecided[edgeId >> 5] &
                (1u << (edgeId & 31u))) != 0u)
            continue;
        journalEdgeDecided[edgeId >> 5] |= 1u << (edgeId & 31u);
        if (record.eventKind == 1u || record.edgeType != 0u) continue;
        if ((buffers->edges[edgeId].taggedObjectRaw & 8u) == 0u ||
            (edgeDeletedSeen[edgeId >> 5] &
                (1u << (edgeId & 31u))) == 0u) continue;
        if (record.validationFlags != 0x1Fu || !record.ownerObject ||
            record.hookAddress != record.ownerObject + 0x3Cu ||
            !record.pxsShapeCoreLow ||
            record.pxsShapeCoreLow >= record.pxsShapeCoreHigh)
            return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
                ERROR_INVALID_DATA, 8u, ordinal, 49u);
        const uintptr_t currentManager =
            buffers->edges[edgeId].taggedObjectRaw & ~0xFu;
        IslandSipEdgeBinding& binding = buffers->bindings[bindingOutput];
        ZeroMemory(&binding, sizeof(binding));
        binding.edgeId = edgeId;
        binding.edgeType = 0u;
        binding.sip = record.ownerObject;
        binding.hookAddress = record.hookAddress;
        // The SIP can already have returned to its pool.  Its captured owner,
        // hook and shape-core key remain diagnostic identity, but never
        // dereference that stale address here.
        binding.shapeSim0 = 0u;
        binding.shapeSim1 = 0u;
        binding.pxsShapeCoreLow = record.pxsShapeCoreLow;
        binding.pxsShapeCoreHigh = record.pxsShapeCoreHigh;
        // destroyManager() precedes removeEdge().  The raw payload can
        // therefore name a freed or already-reused manager pool slot while D
        // is still pending.  Preserve it diagnostically, but never dereference
        // it or require a backlink to the journal's retired SIP owner.
        binding.contactManager = static_cast<uint32_t>(currentManager);
        binding.taggedObjectRaw = buffers->edges[edgeId].taggedObjectRaw;
        binding.validationFlags = 0x1Fu;
        buffers->edges[edgeId].semanticBindingIndex = bindingOutput;
        bindingEdgeSeen[edgeId >> 5] |= 1u << (edgeId & 31u);
        ++bindingOutput;
    }
    receipt->bindingsWritten = bindingOutput;
    if (bindingOutput != bindingRequired ||
        bindingOutput != receipt->liveContactEdges)
        return FailIslandSnapshot(receipt, IslandSnapshotInvalidBinding,
            ERROR_INVALID_DATA, 8u, bindingOutput, 42u);

    for (uint32_t i = 0; i < header.node.capacity; ++i) {
        IslandNodeSlotRecord& node = buffers->nodes[i];
        if ((node.slotFlags & 1u) != 0u &&
            (node.rawFlagsWord & (2u | 0x20u)) == 0u) {
            const uintptr_t owner = node.ownerOrArticulationRaw;
            if (!owner || !Readable(reinterpret_cast<const void*>(owner),
                    0xC0u) || *reinterpret_cast<const uint32_t*>(
                        owner + 0xBCu) != i)
                return FailIslandSnapshot(receipt,
                    IslandSnapshotInvalidTopology, ERROR_INVALID_DATA,
                    6u, i, 43u);
        }
        node.validationFlags = 0x1Fu;
    }
    for (uint32_t i = 0; i < header.edge.capacity; ++i)
        buffers->edges[i].validationFlags = 0x1Fu;
    for (uint32_t i = 0; i < header.island.capacity; ++i)
        buffers->islands[i].validationFlags = 0x0Fu;
    for (uint32_t i = 0; i < header.root.capacity; ++i)
        buffers->roots[i].validationFlags = 0x07u;

    receipt->node.elementHash = HashIslandNodeRaw(buffers->nodes,
        header.node.capacity);
    receipt->node.nextHash = HashIslandRecordWord(buffers->nodes,
        header.node.capacity, sizeof(IslandNodeSlotRecord), 20u);
    receipt->edge.elementHash = HashIslandEdgeRaw(buffers->edges,
        header.edge.capacity);
    receipt->edge.nextHash = HashIslandRecordWord(buffers->edges,
        header.edge.capacity, sizeof(IslandEdgeSlotRecord), 20u);
    receipt->island.elementHash = HashIslandRaw(buffers->islands,
        header.island.capacity);
    receipt->root.elementHash = HashIslandRootRaw(buffers->roots,
        header.root.capacity);
    const uint32_t nodeFreePhysicalHash = HashIslandRecordWord(
        buffers->nodes, header.node.capacity,
        sizeof(IslandNodeSlotRecord), 16u);
    const uint32_t edgeFreePhysicalHash = HashIslandRecordWord(
        buffers->edges, header.edge.capacity,
        sizeof(IslandEdgeSlotRecord), 16u);
    const uint32_t islandFreePhysicalHash = HashIslandRecordWord(
        buffers->islands, header.island.capacity,
        sizeof(IslandSlotRecord), 20u);
    const uint32_t rootFreePhysicalHash = HashIslandRecordWord(
        buffers->roots, header.root.capacity,
        sizeof(IslandArticulationRootSlotRecord), 12u);
    receipt->bindingHash = ByteHash(buffers->bindings,
        bindingOutput * sizeof(IslandSipEdgeBinding));
    uint32_t hash = 2166136261u;
    hash = AppendByteHash(hash, buffers->nodes,
        header.node.capacity * sizeof(IslandNodeSlotRecord));
    hash = AppendByteHash(hash, buffers->edges,
        header.edge.capacity * sizeof(IslandEdgeSlotRecord));
    hash = AppendByteHash(hash, buffers->islands,
        header.island.capacity * sizeof(IslandSlotRecord));
    hash = AppendByteHash(hash, buffers->roots,
        header.root.capacity * sizeof(IslandArticulationRootSlotRecord));
    for (uint32_t i = 0; i < 5u; ++i)
        hash = AppendByteHash(hash, bitmapOutputs[i],
            header.bitmaps[i].wordCount * 4u);
    for (uint32_t i = 0; i < 6u; ++i)
        hash = AppendByteHash(hash, queues[i].output,
            queues[i].source->count * 4u);
    hash = AppendByteHash(hash, buffers->bindings,
        bindingOutput * sizeof(IslandSipEdgeBinding));
    hash = AppendByteHash(hash, header.scalars, sizeof(header.scalars));
    receipt->snapshotHash = hash;

    IslandHeaderState check = {};
    InteractionGraphArrayHeader contactCheck = {};
    const bool contactStable = ReadInteractionGraphArray(
        header.interactionScene + 0x10u, contactCheck,
        kMaximumIslandBindings) &&
        contactCheck.data == contactInteractions.data &&
        contactCheck.count == contactInteractions.count &&
        contactCheck.capacityRaw == contactInteractions.capacityRaw &&
        OrderHash(reinterpret_cast<const uintptr_t*>(contactCheck.data),
            contactCheck.count) == contactOrderHash;
    bool bindingsStable = true;
    for (uint32_t i = 0; i < bindingOutput && bindingsStable; ++i) {
        const IslandSipEdgeBinding& binding = buffers->bindings[i];
        if (!binding.shapeSim0 && !binding.shapeSim1) continue;
        if (!binding.sip || binding.hookAddress != binding.sip + 0x3Cu ||
            !Readable(reinterpret_cast<const void*>(binding.sip), 0x40u) ||
            *reinterpret_cast<const uint32_t*>(binding.hookAddress) !=
                binding.edgeId ||
            *reinterpret_cast<const uintptr_t*>(binding.sip + 0x20u) !=
                binding.shapeSim0 ||
            *reinterpret_cast<const uintptr_t*>(binding.sip + 0x24u) !=
                binding.shapeSim1 ||
            *reinterpret_cast<const uintptr_t*>(binding.sip + 0x38u) !=
                binding.contactManager ||
            (buffers->edges[binding.edgeId].taggedObjectRaw & ~0xFu) !=
                binding.contactManager ||
            (binding.contactManager &&
                (!Readable(reinterpret_cast<const void*>(
                    binding.contactManager), 0x10u) ||
                 *reinterpret_cast<const uintptr_t*>(
                    binding.contactManager + 0x0Cu) != binding.sip))) {
            bindingsStable = false;
            break;
        }
        uintptr_t pxs0 = 0, pxs1 = 0;
        if (!ReadShapeSimPxsShapeCore(binding.shapeSim0, pxs0) ||
            !ReadShapeSimPxsShapeCore(binding.shapeSim1, pxs1)) {
            bindingsStable = false;
            break;
        }
        const uint32_t low = static_cast<uint32_t>(pxs0 < pxs1 ? pxs0 : pxs1);
        const uint32_t high = static_cast<uint32_t>(pxs0 < pxs1 ? pxs1 : pxs0);
        bindingsStable = low == binding.pxsShapeCoreLow &&
            high == binding.pxsShapeCoreHigh;
    }
    if (!ReadIslandHeader(unityBase, nphaseCore, header.manager, check))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 51u);
    if (!SameIslandHeader(header, check))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 52u);
    if (!contactStable || !bindingsStable)
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 53u);
    if (receipt->node.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.node.elements), check.node.capacity * 12u) ||
        receipt->edge.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.edge.elements), check.edge.capacity * 12u) ||
        receipt->island.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.island.elements), check.island.capacity * 16u) ||
        receipt->root.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.root.elements), check.root.capacity * 8u))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 54u);
    if (nodeFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.node.freeNext), check.node.capacity))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 55u);
    if (edgeFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edge.freeNext), check.edge.capacity))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 551u);
    if (islandFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.island.freeNext), check.island.capacity))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 552u);
    if (rootFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.root.freeNext), check.root.capacity))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 553u);
    if (receipt->node.nextHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.node.nextList), check.node.capacity) ||
        receipt->edge.nextHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edge.nextList), check.edge.capacity))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 56u);
    if (receipt->kinematic.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.bitmaps[0].data), check.bitmaps[0].wordCount) ||
        receipt->kinematicChange.hash != WordHash(
            reinterpret_cast<const uint32_t*>(check.bitmaps[1].data),
            check.bitmaps[1].wordCount) ||
        receipt->notReady.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.bitmaps[2].data), check.bitmaps[2].wordCount) ||
        receipt->notReadyChange.hash != WordHash(
            reinterpret_cast<const uint32_t*>(check.bitmaps[3].data),
            check.bitmaps[3].wordCount) ||
        receipt->islandBitmap.hash != WordHash(
            reinterpret_cast<const uint32_t*>(check.bitmaps[4].data),
            check.bitmaps[4].wordCount))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 57u);
    if (receipt->nodeCreated.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.nodeCreated.data), check.nodeCreated.count) ||
        receipt->nodeDeleted.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.nodeDeleted.data), check.nodeDeleted.count) ||
        receipt->edgeCreated.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeCreated.data), check.edgeCreated.count) ||
        receipt->edgeDeleted.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeDeleted.data), check.edgeDeleted.count) ||
        receipt->edgeBroken.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeBroken.data), check.edgeBroken.count) ||
        receipt->edgeJoined.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeJoined.data), check.edgeJoined.count))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 58u);
    if (receipt->journalEndOrdinal != IslandJournalCurrentNext() ||
        epoch != InterlockedCompareExchange(&g_islandEpoch, 0, 0) ||
        capturePhase != InterlockedCompareExchange(
            &g_islandCapturePhase, 0, 0))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 59u);
    if (
        receipt->node.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.node.elements), check.node.capacity * 12u) ||
        receipt->edge.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.edge.elements), check.edge.capacity * 12u) ||
        receipt->island.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.island.elements), check.island.capacity * 16u) ||
        receipt->root.elementHash != ByteHash(reinterpret_cast<const void*>(
            check.root.elements), check.root.capacity * 8u) ||
        nodeFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.node.freeNext), check.node.capacity) ||
        edgeFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edge.freeNext), check.edge.capacity) ||
        islandFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.island.freeNext), check.island.capacity) ||
        rootFreePhysicalHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.root.freeNext), check.root.capacity) ||
        receipt->node.nextHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.node.nextList), check.node.capacity) ||
        receipt->edge.nextHash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edge.nextList), check.edge.capacity) ||
        receipt->kinematic.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.bitmaps[0].data), check.bitmaps[0].wordCount) ||
        receipt->kinematicChange.hash != WordHash(
            reinterpret_cast<const uint32_t*>(check.bitmaps[1].data),
            check.bitmaps[1].wordCount) ||
        receipt->notReady.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.bitmaps[2].data), check.bitmaps[2].wordCount) ||
        receipt->notReadyChange.hash != WordHash(
            reinterpret_cast<const uint32_t*>(check.bitmaps[3].data),
            check.bitmaps[3].wordCount) ||
        receipt->islandBitmap.hash != WordHash(
            reinterpret_cast<const uint32_t*>(check.bitmaps[4].data),
            check.bitmaps[4].wordCount) ||
        receipt->nodeCreated.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.nodeCreated.data), check.nodeCreated.count) ||
        receipt->nodeDeleted.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.nodeDeleted.data), check.nodeDeleted.count) ||
        receipt->edgeCreated.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeCreated.data), check.edgeCreated.count) ||
        receipt->edgeDeleted.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeDeleted.data), check.edgeDeleted.count) ||
        receipt->edgeBroken.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeBroken.data), check.edgeBroken.count) ||
        receipt->edgeJoined.hash != WordHash(reinterpret_cast<const uint32_t*>(
            check.edgeJoined.data), check.edgeJoined.count) ||
        receipt->journalEndOrdinal != IslandJournalCurrentNext() ||
        epoch != InterlockedCompareExchange(&g_islandEpoch, 0, 0) ||
        capturePhase != InterlockedCompareExchange(
            &g_islandCapturePhase, 0, 0) ||
        (expectedPhase == IslandSnapshotSettled ?
            InterlockedCompareExchange(&g_islandState, 0, 0) ==
                IslandObserverCapturing :
            InterlockedCompareExchange(&g_islandState, 0, 0) !=
                IslandObserverCapturing) ||
        (expectedPhase != IslandSnapshotSettled &&
            (receipt->observerSequence != static_cast<uint32_t>(
                InterlockedCompareExchange(&g_islandObserverSequence, 0, 0)) ||
             receipt->observationOrdinal != g_islandArmedOrdinal)))
        return FailIslandSnapshot(receipt, IslandSnapshotUnstable,
            ERROR_RETRY, 10u, 0xFFFFFFFFu, 50u);
    receipt->validationFlags = 0x3FFu;
    receipt->result = IslandSnapshotOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

static uint32_t ReserveIslandJournal(uintptr_t self, uint32_t eventKind,
    uint32_t edgeType, uint32_t node0, uint32_t node1,
    uintptr_t hookAddress) {
    if (InterlockedCompareExchange(&g_islandInstalled, 0, 0) != 1 ||
        InterlockedCompareExchange(&g_islandState, 0, 0) ==
            IslandObserverDormant || self != g_islandExpectedManager ||
        !hookAddress || !Readable(reinterpret_cast<const void*>(hookAddress),
            sizeof(uint32_t))) return 0u;
    const uint32_t ordinal = static_cast<uint32_t>(InterlockedIncrement(
        &g_islandJournalNextOrdinal));
    if (ordinal > kIslandJournalCapacity)
        InterlockedIncrement(&g_islandJournalOverflow);
    IslandJournalSlot& slot = g_islandJournal[
        (ordinal - 1u) % kIslandJournalCapacity];
    InterlockedExchange(&slot.committedOrdinal, 0);
    ZeroMemory(&slot.record, sizeof(slot.record));
    slot.record.ordinal = ordinal;
    slot.record.eventKind = eventKind;
    slot.record.observerPhase = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_islandCapturePhase, 0, 0));
    slot.record.threadId = GetCurrentThreadId();
    slot.record.edgeType = edgeType;
    slot.record.node0 = node0;
    slot.record.node1 = node1;
    slot.record.preEdgeId = 0xFFFFFFFFu;
    slot.record.postEdgeId = 0xFFFFFFFFu;
    slot.record.hookAddress = static_cast<uint32_t>(hookAddress);
    slot.record.validationFlags = 0x03u;
    if (eventKind == 2u) {
        const uint32_t edgeId = *reinterpret_cast<const uint32_t*>(hookAddress);
        slot.record.preEdgeId = edgeId;
        if (edgeId != 0xFFFFFFFFu &&
            Readable(reinterpret_cast<const void*>(self + 0x118u), 0x1Cu)) {
            const uintptr_t elements = *reinterpret_cast<const uintptr_t*>(
                self + 0x11Cu);
            const uint32_t capacity = *reinterpret_cast<const uint32_t*>(
                self + 0x124u);
            if (edgeId < capacity && elements && Readable(
                    reinterpret_cast<const void*>(elements + edgeId * 12u),
                    12u)) {
                slot.record.node0 = *reinterpret_cast<const uint32_t*>(
                    elements + edgeId * 12u);
                slot.record.node1 = *reinterpret_cast<const uint32_t*>(
                    elements + edgeId * 12u + 4u);
                slot.record.validationFlags |= 0x04u;
            }
        }
    }
    if (edgeType == 0u && hookAddress >= 0x3Cu) {
        const uintptr_t owner = hookAddress - 0x3Cu;
        if (Readable(reinterpret_cast<const void*>(owner), 0x28u)) {
            const uintptr_t shape0 = *reinterpret_cast<const uintptr_t*>(
                owner + 0x20u);
            const uintptr_t shape1 = *reinterpret_cast<const uintptr_t*>(
                owner + 0x24u);
            uintptr_t pxs0 = 0, pxs1 = 0;
            if (ReadShapeSimPxsShapeCore(shape0, pxs0) &&
                ReadShapeSimPxsShapeCore(shape1, pxs1) && pxs0 != pxs1) {
                slot.record.ownerObject = static_cast<uint32_t>(owner);
                slot.record.pxsShapeCoreLow = static_cast<uint32_t>(
                    pxs0 < pxs1 ? pxs0 : pxs1);
                slot.record.pxsShapeCoreHigh = static_cast<uint32_t>(
                    pxs0 < pxs1 ? pxs1 : pxs0);
                slot.record.validationFlags |= 0x18u;
            }
        }
    } else {
        // Constraint/articulation ownership remains deliberately unresolved;
        // the exact raw hook event is still complete journal evidence.
        slot.record.validationFlags |= 0x18u;
    }
    return ordinal;
}

static uint32_t __cdecl ObserveIslandAddEntry(uintptr_t self,
    uint32_t edgeType, uint32_t node0, uint32_t node1,
    uintptr_t hookAddress) {
    return ReserveIslandJournal(self, 1u, edgeType, node0, node1,
        hookAddress);
}

static void __cdecl ObserveIslandAddExit(uint32_t ordinal,
    uintptr_t hookAddress) {
    if (!ordinal) return;
    IslandJournalSlot& slot = g_islandJournal[
        (ordinal - 1u) % kIslandJournalCapacity];
    if (slot.record.ordinal != ordinal || !Readable(
            reinterpret_cast<const void*>(hookAddress), 4u)) return;
    slot.record.postEdgeId = *reinterpret_cast<const uint32_t*>(hookAddress);
    if (slot.record.postEdgeId != 0xFFFFFFFFu)
        slot.record.validationFlags |= 0x04u;
    MemoryBarrier();
    InterlockedExchange(&slot.committedOrdinal, static_cast<LONG>(ordinal));
}

static uint32_t __cdecl ObserveIslandRemoveEntry(uintptr_t self,
    uint32_t edgeType, uintptr_t hookAddress) {
    return ReserveIslandJournal(self, 2u, edgeType, 0xFFFFFFFFu,
        0xFFFFFFFFu, hookAddress);
}

static void __cdecl ObserveIslandRemoveExit(uint32_t ordinal,
    uintptr_t hookAddress) {
    if (!ordinal) return;
    IslandJournalSlot& slot = g_islandJournal[
        (ordinal - 1u) % kIslandJournalCapacity];
    if (slot.record.ordinal != ordinal || !Readable(
            reinterpret_cast<const void*>(hookAddress), 4u)) return;
    slot.record.postEdgeId = *reinterpret_cast<const uint32_t*>(hookAddress);
    if (slot.record.postEdgeId == 0xFFFFFFFFu)
        slot.record.validationFlags |= 0x04u;
    MemoryBarrier();
    InterlockedExchange(&slot.committedOrdinal, static_cast<LONG>(ordinal));
}

static void InitializeIslandObserverReceipt(uintptr_t unityBase,
    IslandUpdateObserverReceipt* receipt) {
    ZeroMemory(receipt, sizeof(*receipt));
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(*receipt);
    receipt->result = IslandObserverOk;
    receipt->unityBase = static_cast<uint32_t>(unityBase);
    receipt->expectedManager = static_cast<uint32_t>(g_islandExpectedManager);
    receipt->expectedContext = static_cast<uint32_t>(g_islandExpectedContext);
    receipt->expectedNphase = static_cast<uint32_t>(g_islandExpectedNphase);
    receipt->installed = InterlockedCompareExchange(&g_islandInstalled,
        0, 0) == 1 ? 1u : 0u;
    receipt->state = static_cast<uint32_t>(InterlockedCompareExchange(
        &g_islandState, 0, 0));
    receipt->expectedPass = g_islandExpectedPass;
    receipt->armedThreadId = g_islandArmedThreadId;
    receipt->observerSequence = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_islandObserverSequence, 0, 0));
    receipt->armedOrdinal = g_islandArmedOrdinal;
    receipt->observationOrdinal = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_islandCommittedObservationOrdinal,
            0, 0));
    receipt->slotIndex = 0u;
    receipt->journalBeginOrdinal = g_islandArmJournalBegin;
    receipt->journalEndOrdinal = IslandJournalCurrentNext();
    receipt->invalidIndex = 0xFFFFFFFFu;
    receipt->inFlight = static_cast<uint32_t>(InterlockedCompareExchange(
        &g_islandInFlight, 0, 0));
}

static int FailIslandObserver(IslandUpdateObserverReceipt* receipt,
    IslandObserverResult result, uint32_t error, uint32_t kind,
    uint32_t index, uint32_t detail) {
    if (receipt) {
        receipt->result = result;
        receipt->lastError = error;
        receipt->invalidKind = kind;
        receipt->invalidIndex = index;
        receipt->detail = detail;
        receipt->inFlight = static_cast<uint32_t>(InterlockedCompareExchange(
            &g_islandInFlight, 0, 0));
    }
    return 0;
}

static uint32_t __cdecl ObserveIslandUpdateEntry(uintptr_t self,
    uintptr_t /*task*/, uint32_t pass) {
    const uintptr_t expectedManager = g_islandExpectedManager;
    const uintptr_t expectedNphase = g_islandExpectedNphase;
    const uint32_t expectedPass = g_islandExpectedPass;
    const uint32_t armedThreadId = g_islandArmedThreadId;
    const uint32_t armedOrdinal = g_islandArmedOrdinal;
    const LONG observerSequence = InterlockedCompareExchange(
        &g_islandObserverSequence, 0, 0);
    if (self != expectedManager || !expectedNphase || pass != expectedPass)
        return 0u;
    if (InterlockedCompareExchange(&g_islandState,
            IslandObserverCapturing, IslandObserverArmed) !=
        IslandObserverArmed) return 0u;
    MemoryBarrier();
    if (expectedManager != g_islandExpectedManager ||
        expectedNphase != g_islandExpectedNphase ||
        expectedPass != g_islandExpectedPass ||
        armedThreadId != g_islandArmedThreadId ||
        armedOrdinal != g_islandArmedOrdinal ||
        observerSequence != InterlockedCompareExchange(
            &g_islandObserverSequence, 0, 0)) {
        const LONG target = InterlockedExchange(&g_islandDeferredDormant, 0) ?
            IslandObserverDormant : IslandObserverArmed;
        InterlockedCompareExchange(&g_islandState, target,
            IslandObserverCapturing);
        return 0u;
    }
    const uint32_t ordinal = static_cast<uint32_t>(InterlockedIncrement(
        &g_islandNextObservationOrdinal));
    ZeroMemory(&g_islandCommittedReceipt, sizeof(g_islandCommittedReceipt));
    InitializeIslandObserverReceipt(g_islandUnityBase,
        &g_islandCommittedReceipt);
    g_islandCommittedReceipt.state = IslandObserverCapturing;
    g_islandCommittedReceipt.observationOrdinal = ordinal;
    g_islandCommittedReceipt.pass = pass;
    g_islandCommittedReceipt.threadId = GetCurrentThreadId();
    g_islandCommittedReceipt.observedManager = static_cast<uint32_t>(self);
    g_islandCommittedReceipt.observedContext = static_cast<uint32_t>(
        self >= 0x181Cu ? self - 0x181Cu : 0u);
    g_islandCommittedReceipt.observedNphase = static_cast<uint32_t>(
        g_islandExpectedNphase);
    g_islandCommittedReceipt.journalBeginOrdinal = g_islandArmJournalBegin;
    if (ordinal != g_islandArmedOrdinal) {
        g_islandCommittedReceipt.result = IslandObserverInvalidIdentity;
        g_islandCommittedReceipt.lastError = ERROR_INVALID_DATA;
        g_islandCommittedReceipt.invalidKind = 1u;
        g_islandCommittedReceipt.detail = 1u;
    } else {
        g_islandCommittedReceipt.validationFlags |= 0x03u;
    }
    const LONG priorEpoch = InterlockedIncrement(&g_islandEpoch) - 1;
    InterlockedExchange(&g_islandCapturePhase, IslandSnapshotPreUpdate);
    if ((priorEpoch & 1) != 0) {
        g_islandCommittedReceipt.result = IslandObserverUnstable;
        g_islandCommittedReceipt.lastError = ERROR_INVALID_STATE;
        g_islandCommittedReceipt.detail = 2u;
    }
    const int preOk = CaptureIslandSnapshot(g_islandUnityBase,
        g_islandExpectedNphase, IslandSnapshotPreUpdate,
        &g_islandArmedPreBuffers, g_islandArmedPreReceipt);
    g_islandCommittedReceipt.preResult = g_islandArmedPreReceipt ?
        g_islandArmedPreReceipt->result : IslandSnapshotBadArgument;
    g_islandCommittedReceipt.preSnapshotHash = preOk &&
        g_islandArmedPreReceipt ? g_islandArmedPreReceipt->snapshotHash : 0u;
    if (preOk) g_islandCommittedReceipt.validationFlags |= 0x04u;
    else if (g_islandCommittedReceipt.result == IslandObserverOk) {
        g_islandCommittedReceipt.result = IslandObserverCaptureFailed;
        g_islandCommittedReceipt.lastError = g_islandArmedPreReceipt ?
            g_islandArmedPreReceipt->lastError : ERROR_NOACCESS;
        g_islandCommittedReceipt.invalidKind = 2u;
    }
    return ordinal;
}

static void __cdecl ObserveIslandUpdateExit(uintptr_t self, uint32_t pass,
    uint32_t ordinal) {
    if (!ordinal ||
        InterlockedCompareExchange(&g_islandState, 0, 0) !=
            IslandObserverCapturing) return;
    if (ordinal != g_islandArmedOrdinal &&
        g_islandCommittedReceipt.result == IslandObserverOk) {
        g_islandCommittedReceipt.result = IslandObserverUnstable;
        g_islandCommittedReceipt.lastError = ERROR_INVALID_DATA;
        g_islandCommittedReceipt.detail = 4u;
    }
    g_islandCommittedReceipt.validationFlags |= 0x08u;
    InterlockedExchange(&g_islandCapturePhase, IslandSnapshotPostUpdate);
    const int postOk = CaptureIslandSnapshot(g_islandUnityBase,
        g_islandExpectedNphase, IslandSnapshotPostUpdate,
        &g_islandArmedPostBuffers, g_islandArmedPostReceipt);
    g_islandCommittedReceipt.postResult = g_islandArmedPostReceipt ?
        g_islandArmedPostReceipt->result : IslandSnapshotBadArgument;
    g_islandCommittedReceipt.postSnapshotHash = postOk &&
        g_islandArmedPostReceipt ? g_islandArmedPostReceipt->snapshotHash : 0u;
    g_islandCommittedReceipt.observedManager = static_cast<uint32_t>(self);
    g_islandCommittedReceipt.pass = pass;
    g_islandCommittedReceipt.journalEndOrdinal = IslandJournalCurrentNext();
    if (postOk) g_islandCommittedReceipt.validationFlags |= 0x10u;
    else if (g_islandCommittedReceipt.result == IslandObserverOk) {
        g_islandCommittedReceipt.result = IslandObserverCaptureFailed;
        g_islandCommittedReceipt.lastError = g_islandArmedPostReceipt ?
            g_islandArmedPostReceipt->lastError : ERROR_NOACCESS;
        g_islandCommittedReceipt.invalidKind = 3u;
    }
    if (g_islandCommittedReceipt.journalEndOrdinal >=
            g_islandCommittedReceipt.journalBeginOrdinal)
        g_islandCommittedReceipt.validationFlags |= 0x20u;
    const LONG afterEpoch = InterlockedIncrement(&g_islandEpoch);
    InterlockedExchange(&g_islandCapturePhase, IslandSnapshotSettled);
    if ((afterEpoch & 1) != 0 &&
        g_islandCommittedReceipt.result == IslandObserverOk) {
        g_islandCommittedReceipt.result = IslandObserverUnstable;
        g_islandCommittedReceipt.lastError = ERROR_INVALID_STATE;
        g_islandCommittedReceipt.detail = 3u;
    }
    const LONG finalState = g_islandCommittedReceipt.result ==
        IslandObserverOk ? IslandObserverCaptured : IslandObserverFailed;
    if (finalState == IslandObserverCaptured)
        g_islandCommittedReceipt.validationFlags |= 0x40u;
    g_islandCommittedReceipt.state = static_cast<uint32_t>(finalState);
    g_islandCommittedReceipt.inFlight = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_islandInFlight, 0, 0));
    MemoryBarrier();
    InterlockedExchange(&g_islandCommittedObservationOrdinal,
        static_cast<LONG>(ordinal));
    InterlockedExchange(&g_islandState,
        InterlockedExchange(&g_islandDeferredDormant, 0) != 0 ?
            IslandObserverDormant : finalState);
}

__declspec(naked) static void HookIslandAddEdge() {
    __asm push 0
    __asm pushfd
    __asm pushad
    __asm lock inc dword ptr [g_islandInFlight]
    __asm mov eax, dword ptr [esp + 0x18]
    __asm push dword ptr [esp + 0x38]
    __asm push dword ptr [esp + 0x38]
    __asm push dword ptr [esp + 0x38]
    __asm push dword ptr [esp + 0x38]
    __asm push eax
    __asm call ObserveIslandAddEntry
    __asm add esp, 20
    __asm mov dword ptr [esp + 0x24], eax
    __asm popad
    __asm popfd
    __asm push ecx
    __asm push dword ptr [esp + 0x18]
    __asm push dword ptr [esp + 0x18]
    __asm push dword ptr [esp + 0x18]
    __asm push dword ptr [esp + 0x18]
    __asm call dword ptr [g_islandAddEdgeTrampoline]
    __asm pop ecx
    __asm pushfd
    __asm pushad
    __asm push dword ptr [esp + 0x38]
    __asm push dword ptr [esp + 0x28]
    __asm call ObserveIslandAddExit
    __asm add esp, 8
    __asm lock dec dword ptr [g_islandInFlight]
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret 16
}

__declspec(naked) static void HookIslandRemoveEdge() {
    __asm push 0
    __asm pushfd
    __asm pushad
    __asm lock inc dword ptr [g_islandInFlight]
    __asm mov eax, dword ptr [esp + 0x18]
    __asm push dword ptr [esp + 0x30]
    __asm push dword ptr [esp + 0x30]
    __asm push eax
    __asm call ObserveIslandRemoveEntry
    __asm add esp, 12
    __asm mov dword ptr [esp + 0x24], eax
    __asm popad
    __asm popfd
    __asm push ecx
    __asm push dword ptr [esp + 0x10]
    __asm push dword ptr [esp + 0x10]
    __asm call dword ptr [g_islandRemoveEdgeTrampoline]
    __asm pop ecx
    __asm pushfd
    __asm pushad
    __asm push dword ptr [esp + 0x30]
    __asm push dword ptr [esp + 0x28]
    __asm call ObserveIslandRemoveExit
    __asm add esp, 8
    __asm lock dec dword ptr [g_islandInFlight]
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret 8
}

__declspec(naked) static void HookIslandUpdate() {
    __asm push 0
    __asm pushfd
    __asm pushad
    __asm lock inc dword ptr [g_islandInFlight]
    __asm mov eax, dword ptr [esp + 0x18]
    __asm push dword ptr [esp + 0x30]
    __asm push dword ptr [esp + 0x30]
    __asm push eax
    __asm call ObserveIslandUpdateEntry
    __asm add esp, 12
    __asm mov dword ptr [esp + 0x24], eax
    __asm popad
    __asm popfd
    __asm push ecx
    __asm push dword ptr [esp + 0x10]
    __asm push dword ptr [esp + 0x10]
    __asm call dword ptr [g_islandUpdateTrampoline]
    __asm pop ecx
    __asm pushfd
    __asm pushad
    __asm mov eax, dword ptr [esp + 0x18]
    __asm push dword ptr [esp + 0x24]
    __asm push dword ptr [esp + 0x34]
    __asm push eax
    __asm call ObserveIslandUpdateExit
    __asm add esp, 12
    __asm lock dec dword ptr [g_islandInFlight]
    __asm popad
    __asm popfd
    __asm add esp, 4
    __asm ret 8
}

static bool PrepareIslandTrampoline(uint8_t* source, uint32_t patchSize,
    void*& trampolineValue, uint8_t* original) {
    if (trampolineValue) return true;
    if (!Readable(source, patchSize)) return false;
    uint8_t* trampoline = static_cast<uint8_t*>(VirtualAlloc(0,
        patchSize + 5u, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!trampoline) return false;
    CopyBytes(original, source, patchSize);
    CopyBytes(trampoline, source, patchSize);
    trampoline[patchSize] = 0xE9;
    *reinterpret_cast<int32_t*>(trampoline + patchSize + 1u) =
        static_cast<int32_t>(reinterpret_cast<uintptr_t>(source + patchSize) -
            reinterpret_cast<uintptr_t>(trampoline + patchSize) - 5);
    FlushInstructionCache(GetCurrentProcess(), trampoline, patchSize + 5u);
    trampolineValue = trampoline;
    return true;
}

static bool ValidateIslandManagerIdentity(uintptr_t unityBase,
    uintptr_t manager) {
    return manager >= 0x181Cu && Readable(reinterpret_cast<const void*>(
        manager), 0x1BCu) &&
        *reinterpret_cast<const uintptr_t*>(manager + 0x0Cu) ==
            unityBase + kIslandNodeVtableRva &&
        *reinterpret_cast<const uintptr_t*>(manager + 0x118u) ==
            unityBase + kIslandEdgeVtableRva &&
        *reinterpret_cast<const uintptr_t*>(manager + 0x174u) ==
            unityBase + kIslandManagerVtableRva &&
        *reinterpret_cast<const uintptr_t*>(manager + 0x1A4u) ==
            unityBase + kIslandRootVtableRva;
}

static bool AllIslandHooksLanded(uintptr_t unityBase) {
    return HasIslandJump(reinterpret_cast<const void*>(unityBase +
            kIslandAddEdgeRva), HookIslandAddEdge, 6u) &&
        HasIslandJump(reinterpret_cast<const void*>(unityBase +
            kIslandRemoveEdgeRva), HookIslandRemoveEdge, 7u) &&
        HasIslandJump(reinterpret_cast<const void*>(unityBase +
            kIslandUpdateRva), HookIslandUpdate, 5u);
}

static void ResetIslandObserver(uintptr_t manager) {
    g_islandExpectedManager = manager;
    g_islandExpectedContext = manager - 0x181Cu;
    g_islandExpectedNphase = 0u;
    g_islandExpectedPass = 0u;
    g_islandArmedThreadId = 0u;
    g_islandArmedOrdinal = 0u;
    g_islandArmJournalBegin = 1u;
    ZeroMemory(&g_islandArmedPreBuffers, sizeof(g_islandArmedPreBuffers));
    ZeroMemory(&g_islandArmedPostBuffers, sizeof(g_islandArmedPostBuffers));
    g_islandArmedPreReceipt = 0;
    g_islandArmedPostReceipt = 0;
    ZeroMemory(&g_islandCommittedReceipt, sizeof(g_islandCommittedReceipt));
    InterlockedExchange(&g_islandEpoch, 0);
    InterlockedExchange(&g_islandCapturePhase, IslandSnapshotSettled);
    InterlockedExchange(&g_islandObserverSequence, 0);
    InterlockedExchange(&g_islandNextObservationOrdinal, 0);
    InterlockedExchange(&g_islandCommittedObservationOrdinal, 0);
    InterlockedExchange(&g_islandJournalNextOrdinal, 0);
    InterlockedExchange(&g_islandJournalOverflow, 0);
    InterlockedExchange(&g_islandDeferredDormant, 0);
    for (uint32_t i = 0; i < kIslandJournalCapacity; ++i)
        InterlockedExchange(&g_islandJournal[i].committedOrdinal, 0);
}

static int InstallIslandObserver(uintptr_t unityBase,
    uintptr_t expectedManager, IslandUpdateObserverReceipt* receipt) {
    if (!receipt || !Writable(receipt, sizeof(*receipt))) return 0;
    InitializeIslandObserverReceipt(unityBase, receipt);
    if (!unityBase || !expectedManager)
        return FailIslandObserver(receipt, IslandObserverBadArgument,
            ERROR_INVALID_PARAMETER, 0u, 0xFFFFFFFFu, 1u);
    const LONG lifecycle = InterlockedCompareExchange(&g_islandInstalled,
        -1, 0);
    if (lifecycle == 1) {
        if (InterlockedCompareExchange(&g_islandState, 0, 0) !=
                IslandObserverDormant ||
            InterlockedCompareExchange(&g_islandInstalled, -1, 1) != 1)
            return FailIslandObserver(receipt,
                InterlockedCompareExchange(&g_islandState, 0, 0) ==
                    IslandObserverDormant ? IslandObserverBusy :
                    IslandObserverAlreadyInstalled,
                ERROR_BUSY, 0u, 0xFFFFFFFFu, 2u);
    } else if (lifecycle != 0) {
        return FailIslandObserver(receipt, IslandObserverBusy, ERROR_BUSY,
            0u, 0xFFFFFFFFu, 2u);
    }
    if (InterlockedCompareExchange(&g_islandInFlight, 0, 0) != 0) {
        InterlockedExchange(&g_islandInstalled, lifecycle == 1 ? 1 : 0);
        return FailIslandObserver(receipt, IslandObserverBusy, ERROR_BUSY,
            0u, 0xFFFFFFFFu, 3u);
    }
    if ((g_islandUnityBase && g_islandUnityBase != unityBase) ||
        !ValidateIslandManagerIdentity(unityBase, expectedManager) ||
        !IslandRevisionMatches(unityBase)) {
        InterlockedExchange(&g_islandInstalled, lifecycle == 1 ? 1 : 0);
        return FailIslandObserver(receipt,
            !ValidateIslandManagerIdentity(unityBase, expectedManager) ?
                IslandObserverInvalidIdentity :
                IslandObserverRevisionMismatch,
            !ValidateIslandManagerIdentity(unityBase, expectedManager) ?
                ERROR_INVALID_DATA : ERROR_REVISION_MISMATCH,
            1u, 0xFFFFFFFFu, 4u);
    }
    uint8_t* addSource = reinterpret_cast<uint8_t*>(unityBase +
        kIslandAddEdgeRva);
    uint8_t* removeSource = reinterpret_cast<uint8_t*>(unityBase +
        kIslandRemoveEdgeRva);
    uint8_t* updateSource = reinterpret_cast<uint8_t*>(unityBase +
        kIslandUpdateRva);
    if ((!HasIslandJump(addSource, HookIslandAddEdge, 6u) &&
            !EqualBytes(addSource, kIslandAddEdgeBytes,
                sizeof(kIslandAddEdgeBytes))) ||
        (!HasIslandJump(removeSource, HookIslandRemoveEdge, 7u) &&
            !EqualBytes(removeSource, kIslandRemoveEdgeBytes,
                sizeof(kIslandRemoveEdgeBytes))) ||
        (!HasIslandJump(updateSource, HookIslandUpdate, 5u) &&
            !EqualBytes(updateSource, kIslandUpdateBytes,
                sizeof(kIslandUpdateBytes)))) {
        InterlockedExchange(&g_islandInstalled, lifecycle == 1 ? 1 : 0);
        return FailIslandObserver(receipt, IslandObserverPatchChanged,
            ERROR_INVALID_STATE, 1u, 0xFFFFFFFFu, 5u);
    }
    g_islandUnityBase = unityBase;
    if ((!g_islandAddEdgeTrampoline && !PrepareIslandTrampoline(addSource,
            6u, g_islandAddEdgeTrampoline, g_islandAddEdgeOriginal)) ||
        (!g_islandRemoveEdgeTrampoline && !PrepareIslandTrampoline(removeSource,
            7u, g_islandRemoveEdgeTrampoline, g_islandRemoveEdgeOriginal)) ||
        (!g_islandUpdateTrampoline && !PrepareIslandTrampoline(updateSource,
            5u, g_islandUpdateTrampoline, g_islandUpdateOriginal))) {
        InterlockedExchange(&g_islandInstalled, 0);
        return FailIslandObserver(receipt, IslandObserverAllocationFailed,
            GetLastError(), 0u, 0xFFFFFFFFu, 6u);
    }
    if (InterlockedCompareExchange(&g_islandModulePinned, 0, 0) == 0) {
        HMODULE module = 0;
        if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCSTR>(&g_islandModuleMarker), &module)) {
            InterlockedExchange(&g_islandInstalled, 0);
            return FailIslandObserver(receipt, IslandObserverAllocationFailed,
                GetLastError(), 0u, 0xFFFFFFFFu, 7u);
        }
        InterlockedExchange(&g_islandModulePinned, 1);
    }
    ResetIslandObserver(expectedManager);
    InterlockedExchange(&g_islandState, IslandObserverDormant);
    bool patchOk = true;
    if (!HasIslandJump(addSource, HookIslandAddEdge, 6u))
        patchOk = WriteJump(addSource, HookIslandAddEdge, 6u) && patchOk;
    if (patchOk && !HasIslandJump(removeSource, HookIslandRemoveEdge, 7u))
        patchOk = WriteJump(removeSource, HookIslandRemoveEdge, 7u) && patchOk;
    if (patchOk && !HasIslandJump(updateSource, HookIslandUpdate, 5u))
        patchOk = WriteJump(updateSource, HookIslandUpdate, 5u) && patchOk;
    const bool allLanded = AllIslandHooksLanded(unityBase);
    if (!patchOk || !allLanded) {
        const uint32_t error = GetLastError();
        const bool anyLanded = HasIslandJump(addSource, HookIslandAddEdge, 6u) ||
            HasIslandJump(removeSource, HookIslandRemoveEdge, 7u) ||
            HasIslandJump(updateSource, HookIslandUpdate, 5u);
        InterlockedExchange(&g_islandInstalled, anyLanded ? 1 : 0);
        InitializeIslandObserverReceipt(unityBase, receipt);
        return FailIslandObserver(receipt,
            patchOk ? IslandObserverPatchChanged :
                IslandObserverProtectFailed,
            error ? error : ERROR_INVALID_STATE, 0u, 0xFFFFFFFFu, 8u);
    }
    InterlockedExchange(&g_islandState, IslandObserverIdle);
    InterlockedExchange(&g_islandInstalled, 1);
    InitializeIslandObserverReceipt(unityBase, receipt);
    receipt->validationFlags = 0x01u;
    return 1;
}

static int StatusIslandObserver(uintptr_t unityBase,
    IslandUpdateObserverReceipt* receipt) {
    if (!receipt || !Writable(receipt, sizeof(*receipt))) return 0;
    InitializeIslandObserverReceipt(unityBase, receipt);
    if (InterlockedCompareExchange(&g_islandInstalled, 0, 0) != 1 ||
        unityBase != g_islandUnityBase || !AllIslandHooksLanded(unityBase))
        return FailIslandObserver(receipt, IslandObserverNotInstalled,
            ERROR_INVALID_STATE, 0u, 0xFFFFFFFFu, 1u);
    const uint32_t ordinal = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_islandCommittedObservationOrdinal,
            0, 0));
    if (ordinal) {
        CopyBytes(receipt, &g_islandCommittedReceipt, sizeof(*receipt));
        MemoryBarrier();
        if (ordinal != static_cast<uint32_t>(InterlockedCompareExchange(
                &g_islandCommittedObservationOrdinal, 0, 0)))
            return FailIslandObserver(receipt, IslandObserverStale,
                ERROR_RETRY, 0u, ordinal, 2u);
        receipt->state = static_cast<uint32_t>(InterlockedCompareExchange(
            &g_islandState, 0, 0));
        receipt->installed = 1u;
        receipt->inFlight = static_cast<uint32_t>(InterlockedCompareExchange(
            &g_islandInFlight, 0, 0));
        return receipt->result == IslandObserverOk ? 1 : 0;
    }
    return 1;
}

static int ArmIslandObserver(uintptr_t unityBase, uintptr_t expectedManager,
    uintptr_t expectedNphase, uint32_t expectedPass,
    const IslandSnapshotBuffersV1* preBuffers,
    IslandSnapshotReceiptV1* preReceipt,
    const IslandSnapshotBuffersV1* postBuffers,
    IslandSnapshotReceiptV1* postReceipt,
    IslandUpdateObserverReceipt* receipt) {
    if (!receipt || !Writable(receipt, sizeof(*receipt))) return 0;
    InitializeIslandObserverReceipt(unityBase, receipt);
    if (InterlockedCompareExchange(&g_islandInstalled, 0, 0) != 1 ||
        unityBase != g_islandUnityBase || !AllIslandHooksLanded(unityBase))
        return FailIslandObserver(receipt, IslandObserverNotInstalled,
            ERROR_INVALID_STATE, 0u, 0xFFFFFFFFu, 1u);
    if (expectedManager != g_islandExpectedManager || !expectedNphase ||
        expectedPass != 0u || !preBuffers || !postBuffers ||
        !Readable(preBuffers, sizeof(*preBuffers)) ||
        !Readable(postBuffers, sizeof(*postBuffers)) ||
        !preReceipt || !postReceipt ||
        !Writable(preReceipt, sizeof(*preReceipt)) ||
        !Writable(postReceipt, sizeof(*postReceipt)))
        return FailIslandObserver(receipt, IslandObserverBadArgument,
            ERROR_INVALID_PARAMETER, 0u, 0xFFFFFFFFu, 2u);
    IslandHeaderState identity = {};
    if (!ReadIslandHeader(unityBase, expectedNphase, expectedManager,
            identity) || identity.context != g_islandExpectedContext)
        return FailIslandObserver(receipt, IslandObserverInvalidIdentity,
            ERROR_INVALID_DATA, 1u, 0xFFFFFFFFu, 3u);
    if (InterlockedCompareExchange(&g_islandInFlight, 0, 0) != 0)
        return FailIslandObserver(receipt, IslandObserverBusy, ERROR_BUSY,
            0u, 0xFFFFFFFFu, 4u);
    LONG state = InterlockedCompareExchange(&g_islandState, 0, 0);
    if (state == IslandObserverArmed || state == IslandObserverCapturing ||
        state == IslandObserverDormant ||
        InterlockedCompareExchange(&g_islandState,
            IslandObserverCapturing, state) != state)
        return FailIslandObserver(receipt, IslandObserverBusy, ERROR_BUSY,
            0u, 0xFFFFFFFFu, 5u);
    g_islandExpectedNphase = expectedNphase;
    g_islandExpectedPass = expectedPass;
    g_islandArmedThreadId = GetCurrentThreadId();
    g_islandArmedPreBuffers = *preBuffers;
    g_islandArmedPostBuffers = *postBuffers;
    g_islandArmedPreReceipt = preReceipt;
    g_islandArmedPostReceipt = postReceipt;
    g_islandArmJournalBegin = IslandJournalCurrentNext();
    const uint32_t sequence = static_cast<uint32_t>(InterlockedIncrement(
        &g_islandObserverSequence));
    (void)sequence;
    g_islandArmedOrdinal = static_cast<uint32_t>(InterlockedCompareExchange(
        &g_islandNextObservationOrdinal, 0, 0)) + 1u;
    MemoryBarrier();
    InterlockedExchange(&g_islandState, IslandObserverArmed);
    InitializeIslandObserverReceipt(unityBase, receipt);
    receipt->validationFlags = 0x03u;
    return 1;
}

static bool CopyIslandSnapshotBuffers(const IslandSnapshotBuffersV1& source,
    const IslandSnapshotReceiptV1& sourceReceipt,
    const IslandSnapshotBuffersV1* destination,
    IslandSnapshotReceiptV1* destinationReceipt) {
    if (!destination || !Readable(destination, sizeof(*destination)) ||
        !destinationReceipt || !Writable(destinationReceipt,
            sizeof(*destinationReceipt))) return false;
#define OC2_COPY_ISLAND(field, cap, required, type) \
    if (!IslandBufferWritable(destination->field, destination->cap, \
            required, sizeof(type))) return false; \
    if (required) CopyBytes(destination->field, source.field, \
        required * sizeof(type))
    OC2_COPY_ISLAND(nodes, nodeCapacity, sourceReceipt.node.required,
        IslandNodeSlotRecord);
    OC2_COPY_ISLAND(edges, edgeCapacity, sourceReceipt.edge.required,
        IslandEdgeSlotRecord);
    OC2_COPY_ISLAND(islands, islandCapacity, sourceReceipt.island.required,
        IslandSlotRecord);
    OC2_COPY_ISLAND(roots, rootCapacity, sourceReceipt.root.required,
        IslandArticulationRootSlotRecord);
    OC2_COPY_ISLAND(kinematicWords, kinematicWordCapacity,
        sourceReceipt.kinematic.required, uint32_t);
    OC2_COPY_ISLAND(kinematicChangeWords, kinematicChangeWordCapacity,
        sourceReceipt.kinematicChange.required, uint32_t);
    OC2_COPY_ISLAND(notReadyWords, notReadyWordCapacity,
        sourceReceipt.notReady.required, uint32_t);
    OC2_COPY_ISLAND(notReadyChangeWords, notReadyChangeWordCapacity,
        sourceReceipt.notReadyChange.required, uint32_t);
    OC2_COPY_ISLAND(islandWords, islandWordCapacity,
        sourceReceipt.islandBitmap.required, uint32_t);
    OC2_COPY_ISLAND(nodeCreated, nodeCreatedCapacity,
        sourceReceipt.nodeCreated.required, uint32_t);
    OC2_COPY_ISLAND(nodeDeleted, nodeDeletedCapacity,
        sourceReceipt.nodeDeleted.required, uint32_t);
    OC2_COPY_ISLAND(edgeCreated, edgeCreatedCapacity,
        sourceReceipt.edgeCreated.required, uint32_t);
    OC2_COPY_ISLAND(edgeDeleted, edgeDeletedCapacity,
        sourceReceipt.edgeDeleted.required, uint32_t);
    OC2_COPY_ISLAND(edgeBroken, edgeBrokenCapacity,
        sourceReceipt.edgeBroken.required, uint32_t);
    OC2_COPY_ISLAND(edgeJoined, edgeJoinedCapacity,
        sourceReceipt.edgeJoined.required, uint32_t);
    OC2_COPY_ISLAND(bindings, bindingCapacity,
        sourceReceipt.bindingsRequired, IslandSipEdgeBinding);
#undef OC2_COPY_ISLAND
    CopyBytes(destinationReceipt, &sourceReceipt, sizeof(*destinationReceipt));
    return true;
}

static int CopyIslandObservation(uintptr_t unityBase,
    uint32_t observationOrdinal, const IslandSnapshotBuffersV1* preBuffers,
    IslandSnapshotReceiptV1* preReceipt,
    const IslandSnapshotBuffersV1* postBuffers,
    IslandSnapshotReceiptV1* postReceipt,
    IslandUpdateObserverReceipt* receipt) {
    if (!receipt || !Writable(receipt, sizeof(*receipt))) return 0;
    InitializeIslandObserverReceipt(unityBase, receipt);
    if (InterlockedCompareExchange(&g_islandInstalled, 0, 0) != 1 ||
        unityBase != g_islandUnityBase)
        return FailIslandObserver(receipt, IslandObserverNotInstalled,
            ERROR_INVALID_STATE, 0u, 0xFFFFFFFFu, 1u);
    if (!observationOrdinal || observationOrdinal != static_cast<uint32_t>(
            InterlockedCompareExchange(&g_islandCommittedObservationOrdinal,
                0, 0)))
        return FailIslandObserver(receipt, IslandObserverNotReady,
            ERROR_IO_PENDING, 0u, observationOrdinal, 2u);
    if (InterlockedCompareExchange(&g_islandInFlight, 0, 0) != 0)
        return FailIslandObserver(receipt, IslandObserverBusy, ERROR_BUSY,
            0u, observationOrdinal, 3u);
    const IslandUpdateObserverReceipt committed = g_islandCommittedReceipt;
    if (committed.observationOrdinal != observationOrdinal)
        return FailIslandObserver(receipt, IslandObserverStale, ERROR_RETRY,
            0u, observationOrdinal, 4u);
    if (committed.result != IslandObserverOk) {
        CopyBytes(receipt, &committed, sizeof(*receipt));
        return 0;
    }
    if (!g_islandArmedPreReceipt || !g_islandArmedPostReceipt ||
        !CopyIslandSnapshotBuffers(g_islandArmedPreBuffers,
            *g_islandArmedPreReceipt, preBuffers, preReceipt) ||
        !CopyIslandSnapshotBuffers(g_islandArmedPostBuffers,
            *g_islandArmedPostReceipt, postBuffers, postReceipt))
        return FailIslandObserver(receipt, IslandObserverCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER, 2u, observationOrdinal, 5u);
    MemoryBarrier();
    if (observationOrdinal != static_cast<uint32_t>(
            InterlockedCompareExchange(&g_islandCommittedObservationOrdinal,
                0, 0)))
        return FailIslandObserver(receipt, IslandObserverStale, ERROR_RETRY,
            0u, observationOrdinal, 6u);
    CopyBytes(receipt, &committed, sizeof(*receipt));
    receipt->inFlight = 0u;
    return 1;
}

static int CancelIslandObserver(uintptr_t unityBase,
    bool dormant, IslandUpdateObserverReceipt* receipt) {
    if (!receipt || !Writable(receipt, sizeof(*receipt))) return 0;
    InitializeIslandObserverReceipt(unityBase, receipt);
    if (InterlockedCompareExchange(&g_islandInstalled, 0, 0) != 1 ||
        unityBase != g_islandUnityBase)
        return FailIslandObserver(receipt, IslandObserverNotInstalled,
            ERROR_INVALID_STATE, 0u, 0xFFFFFFFFu, 1u);
    for (;;) {
        const LONG state = InterlockedCompareExchange(&g_islandState, 0, 0);
        if (state == IslandObserverCapturing) {
            if (dormant) InterlockedExchange(&g_islandDeferredDormant, 1);
            return FailIslandObserver(receipt, IslandObserverBusy, ERROR_BUSY,
                0u, 0xFFFFFFFFu, 2u);
        }
        const LONG target = dormant ? IslandObserverDormant :
            IslandObserverIdle;
        if (InterlockedCompareExchange(&g_islandState, target, state) == state)
            break;
    }
    if (InterlockedCompareExchange(&g_islandInFlight, 0, 0) != 0)
        return FailIslandObserver(receipt, IslandObserverBusy, ERROR_BUSY,
            0u, 0xFFFFFFFFu, 3u);
    g_islandArmedPreReceipt = 0;
    g_islandArmedPostReceipt = 0;
    g_islandArmedOrdinal = 0u;
    g_islandArmedThreadId = 0u;
    if (dormant) {
        g_islandExpectedNphase = 0u;
        g_islandExpectedPass = 0u;
    }
    InitializeIslandObserverReceipt(unityBase, receipt);
    return 1;
}

static int CopyIslandJournal(uintptr_t unityBase, uint32_t beginOrdinal,
    IslandEdgeJournalRecord* records, uint32_t capacity,
    IslandEdgeJournalReceipt* receipt) {
    if (!receipt || !Writable(receipt, sizeof(*receipt))) return 0;
    ZeroMemory(receipt, sizeof(*receipt));
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(*receipt);
    receipt->unityBase = static_cast<uint32_t>(unityBase);
    receipt->expectedManager = static_cast<uint32_t>(g_islandExpectedManager);
    receipt->installed = InterlockedCompareExchange(&g_islandInstalled,
        0, 0) == 1 ? 1u : 0u;
    receipt->state = static_cast<uint32_t>(InterlockedCompareExchange(
        &g_islandState, 0, 0));
    receipt->requestedBegin = beginOrdinal;
    receipt->invalidIndex = 0xFFFFFFFFu;
    if (!receipt->installed || unityBase != g_islandUnityBase) {
        receipt->result = IslandJournalNotInstalled;
        receipt->lastError = ERROR_INVALID_STATE;
        return 0;
    }
    const uint32_t next = IslandJournalCurrentNext();
    const uint32_t first = IslandJournalCurrentFirst(next);
    receipt->firstOrdinal = first;
    receipt->nextOrdinal = next;
    receipt->overflowCount = static_cast<uint32_t>(InterlockedCompareExchange(
        &g_islandJournalOverflow, 0, 0));
    if (!beginOrdinal || beginOrdinal < first || beginOrdinal > next) {
        receipt->result = beginOrdinal < first ? IslandJournalStale :
            IslandJournalBadArgument;
        receipt->lastError = ERROR_INVALID_DATA;
        return 0;
    }
    receipt->recordsRequired = next - beginOrdinal;
    if (capacity < receipt->recordsRequired) {
        receipt->result = IslandJournalCapacityTooSmall;
        receipt->lastError = ERROR_INSUFFICIENT_BUFFER;
        return 0;
    }
    if (receipt->recordsRequired && (!records || !Writable(records,
            receipt->recordsRequired * sizeof(*records)))) {
        receipt->result = IslandJournalBadArgument;
        receipt->lastError = ERROR_NOACCESS;
        return 0;
    }
    for (uint32_t i = 0; i < receipt->recordsRequired; ++i) {
        const uint32_t ordinal = beginOrdinal + i;
        IslandJournalSlot& slot = g_islandJournal[
            (ordinal - 1u) % kIslandJournalCapacity];
        if (static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal) {
            receipt->result = IslandJournalNotReady;
            receipt->lastError = ERROR_IO_PENDING;
            receipt->invalidIndex = i;
            return 0;
        }
        records[i] = slot.record;
        MemoryBarrier();
        if (static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal ||
            records[i].ordinal != ordinal) {
            receipt->result = IslandJournalUnstable;
            receipt->lastError = ERROR_RETRY;
            receipt->invalidIndex = i;
            return 0;
        }
        if (records[i].eventKind == 1u) ++receipt->addCount;
        else if (records[i].eventKind == 2u) ++receipt->removeCount;
        else {
            receipt->result = IslandJournalUnstable;
            receipt->lastError = ERROR_INVALID_DATA;
            receipt->invalidIndex = i;
            return 0;
        }
    }
    if (next != IslandJournalCurrentNext()) {
        receipt->result = IslandJournalUnstable;
        receipt->lastError = ERROR_RETRY;
        return 0;
    }
    receipt->recordsWritten = receipt->recordsRequired;
    receipt->recordHash = ByteHash(records,
        receipt->recordsWritten * sizeof(*records));
    receipt->validationFlags = 0x1Fu;
    receipt->result = IslandJournalOk;
    return 1;
}

static bool FinishBroadPhaseLayoutRevisionMatches(uintptr_t unityBase) {
    if (!unityBase || !TransformCacheRevisionMatches(unityBase)) return false;
    const void* layout = reinterpret_cast<const void*>(unityBase +
        kFinishBroadPhaseRva + 0x0Cu);
    return Readable(layout, sizeof(kFinishBroadPhaseLayoutBytes)) &&
        EqualBytes(layout, kFinishBroadPhaseLayoutBytes,
            sizeof(kFinishBroadPhaseLayoutBytes));
}

static bool FinishBroadPhaseRevisionMatches(uintptr_t unityBase) {
    const void* entry = reinterpret_cast<const void*>(unityBase +
        kFinishBroadPhaseRva);
    return FinishBroadPhaseLayoutRevisionMatches(unityBase) &&
        Readable(entry, sizeof(kFinishBroadPhaseBytes)) &&
        EqualBytes(entry, kFinishBroadPhaseBytes,
            sizeof(kFinishBroadPhaseBytes));
}

static bool FillBroadPhaseOverlapRecord(uintptr_t userData0,
    uintptr_t userData1, BroadPhaseOverlapRecord& record) {
    ZeroMemory(&record, sizeof(record));
    if (!userData0 || !userData1 || userData0 == userData1 ||
        !Readable(reinterpret_cast<const void*>(userData0), 0x20) ||
        !Readable(reinterpret_cast<const void*>(userData1), 0x20))
        return false;
    record.userData0 = userData0;
    record.userData1 = userData1;
    record.cacheId0 = *reinterpret_cast<const uint32_t*>(userData0 + 0x18);
    record.cacheId1 = *reinterpret_cast<const uint32_t*>(userData1 + 0x18);
    record.shapeCore0 = *reinterpret_cast<const uintptr_t*>(userData0 + 0x1C);
    record.shapeCore1 = *reinterpret_cast<const uintptr_t*>(userData1 + 0x1C);
    if (!record.shapeCore0 || !record.shapeCore1 ||
        record.shapeCore0 == record.shapeCore1 ||
        !Readable(reinterpret_cast<const void*>(record.shapeCore0 + 0x20),
            sizeof(uintptr_t)) ||
        !Readable(reinterpret_cast<const void*>(record.shapeCore1 + 0x20),
            sizeof(uintptr_t))) return false;
    record.pxsShapeCore0 = record.shapeCore0 + 0x20;
    record.pxsShapeCore1 = record.shapeCore1 + 0x20;
    record.pairHash = ByteHash(&record, 8u * sizeof(uint32_t));
    record.validationFlags = 0x0Fu;
    return true;
}

static void FailFinishBroadPhaseSlot(FinishBroadPhaseCaptureSlot& slot,
    FinishBroadPhaseObserverResult result, uint32_t error, uint32_t kind,
    uint32_t index, uint32_t detail) {
    slot.receipt.result = result;
    slot.receipt.lastError = error;
    slot.receipt.invalidKind = kind;
    slot.receipt.invalidIndex = index;
    slot.receipt.detail = detail;
}

static uint32_t __cdecl ObserveFinishBroadPhaseEntry(uintptr_t scene,
    uint32_t pass) {
    if (InterlockedCompareExchange(&g_finishBroadPhaseState,
            FinishBroadPhaseObserverCapturing,
            FinishBroadPhaseObserverArmed) !=
        FinishBroadPhaseObserverArmed) return 0;
    if (scene != g_finishBroadPhaseExpectedScene ||
        pass != g_finishBroadPhaseExpectedPass) {
        InterlockedExchange(&g_finishBroadPhaseState,
            FinishBroadPhaseObserverArmed);
        return 0;
    }

    const LONG ordinal = InterlockedIncrement(&g_finishBroadPhaseOrdinal);
    const uint32_t slotIndex = static_cast<uint32_t>(ordinal - 1) %
        kFinishBroadPhaseRingCapacity;
    FinishBroadPhaseCaptureSlot& slot = g_finishBroadPhaseSlots[slotIndex];
    InterlockedExchange(&slot.committedOrdinal, 0);
    ZeroMemory(&slot.receipt, sizeof(slot.receipt));
    slot.receipt.apiVersion = kApiVersion;
    slot.receipt.structSize = sizeof(slot.receipt);
    slot.receipt.result = FinishBroadPhaseObserverNotReady;
    slot.receipt.lastError = ERROR_IO_PENDING;
    slot.receipt.unityBase = g_finishBroadPhaseUnityBase;
    slot.receipt.expectedScene = g_finishBroadPhaseExpectedScene;
    slot.receipt.expectedContext = g_finishBroadPhaseExpectedContext;
    slot.receipt.expectedNPhaseCore = g_finishBroadPhaseExpectedNPhaseCore;
    slot.receipt.observedScene = scene;
    slot.receipt.expectedPass = g_finishBroadPhaseExpectedPass;
    slot.receipt.armedThreadId = g_finishBroadPhaseArmedThreadId;
    slot.receipt.armedOrdinal = g_finishBroadPhaseArmedOrdinal;
    slot.receipt.observationOrdinal = static_cast<uint32_t>(ordinal);
    slot.receipt.slotIndex = slotIndex;
    slot.receipt.pass = pass;
    slot.receipt.threadId = GetCurrentThreadId();
    slot.receipt.state = FinishBroadPhaseObserverCapturing;
    slot.receipt.installed = 1;
    slot.receipt.invalidIndex = 0xFFFFFFFFu;
    slot.receipt.droppedObservations = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_finishBroadPhaseDropped, 0, 0));
    g_finishBroadPhaseArmedOrdinal = static_cast<uint32_t>(ordinal);

    if (!Readable(reinterpret_cast<const void*>(scene + 0x450), 0x68)) {
        FailFinishBroadPhaseSlot(slot,
            FinishBroadPhaseObserverInvalidIdentity, ERROR_NOACCESS,
            1, 0xFFFFFFFFu, 1);
        return static_cast<uint32_t>(ordinal);
    }
    slot.receipt.observedNPhaseCore =
        *reinterpret_cast<const uintptr_t*>(scene + 0x450);
    slot.receipt.interactionScene =
        *reinterpret_cast<const uintptr_t*>(scene + 0x4B4);
    if (slot.receipt.observedNPhaseCore !=
            slot.receipt.expectedNPhaseCore ||
        !slot.receipt.interactionScene ||
        !Readable(reinterpret_cast<const void*>(
            slot.receipt.interactionScene), 0x3F4) ||
        *reinterpret_cast<const uintptr_t*>(
            slot.receipt.interactionScene + 0x3F0) != scene) {
        FailFinishBroadPhaseSlot(slot,
            FinishBroadPhaseObserverInvalidIdentity, ERROR_INVALID_DATA,
            1, 0xFFFFFFFFu, 2);
        return static_cast<uint32_t>(ordinal);
    }
    slot.receipt.observedContext = *reinterpret_cast<const uintptr_t*>(
        slot.receipt.interactionScene + 0x3E8);
    slot.receipt.transformCache = slot.receipt.observedContext + 0x1DBCu;
    if (slot.receipt.observedContext != slot.receipt.expectedContext ||
        *reinterpret_cast<const uintptr_t*>(
            slot.receipt.observedNPhaseCore) != scene ||
        !Readable(reinterpret_cast<const void*>(
            slot.receipt.observedContext), 0x1DE4)) {
        FailFinishBroadPhaseSlot(slot,
            FinishBroadPhaseObserverInvalidIdentity, ERROR_INVALID_DATA,
            1, 0xFFFFFFFFu, 3);
        return static_cast<uint32_t>(ordinal);
    }
    slot.receipt.aabbManager = *reinterpret_cast<const uintptr_t*>(
        slot.receipt.observedContext + 0x08);
    if (!slot.receipt.aabbManager ||
        !Readable(reinterpret_cast<const void*>(slot.receipt.aabbManager),
            0xC2BC)) {
        FailFinishBroadPhaseSlot(slot,
            FinishBroadPhaseObserverInvalidMetadata, ERROR_NOACCESS,
            2, 0xFFFFFFFFu, 4);
        return static_cast<uint32_t>(ordinal);
    }
    slot.receipt.validationFlags |= 0x103u;

    const uintptr_t createdData = *reinterpret_cast<const uintptr_t*>(
        slot.receipt.aabbManager + 0xC2A8);
    slot.receipt.createdRequired = *reinterpret_cast<const uint32_t*>(
        slot.receipt.aabbManager + 0xC2AC);
    const uintptr_t deletedData = *reinterpret_cast<const uintptr_t*>(
        slot.receipt.aabbManager + 0xC2B4);
    slot.receipt.deletedRequired = *reinterpret_cast<const uint32_t*>(
        slot.receipt.aabbManager + 0xC2B8);
    if (slot.receipt.createdRequired > kMaximumBroadPhaseOverlaps ||
        slot.receipt.deletedRequired > kMaximumBroadPhaseOverlaps ||
        (slot.receipt.createdRequired && (!createdData || !Readable(
            reinterpret_cast<const void*>(createdData),
            slot.receipt.createdRequired * 2u * sizeof(uintptr_t)))) ||
        (slot.receipt.deletedRequired && (!deletedData || !Readable(
            reinterpret_cast<const void*>(deletedData),
            slot.receipt.deletedRequired * 2u * sizeof(uintptr_t))))) {
        InterlockedIncrement(&g_finishBroadPhaseDropped);
        FailFinishBroadPhaseSlot(slot,
            FinishBroadPhaseObserverCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER, 2, 0xFFFFFFFFu, 5);
        return static_cast<uint32_t>(ordinal);
    }
    const uintptr_t* created = reinterpret_cast<const uintptr_t*>(createdData);
    for (uint32_t i = 0; i < slot.receipt.createdRequired; ++i) {
        if (!FillBroadPhaseOverlapRecord(created[i * 2u],
                created[i * 2u + 1u], slot.created[i])) {
            FailFinishBroadPhaseSlot(slot,
                FinishBroadPhaseObserverInvalidMetadata, ERROR_INVALID_DATA,
                3, i, 6);
            return static_cast<uint32_t>(ordinal);
        }
    }
    slot.receipt.createdWritten = slot.receipt.createdRequired;
    const uintptr_t* deleted = reinterpret_cast<const uintptr_t*>(deletedData);
    for (uint32_t i = 0; i < slot.receipt.deletedRequired; ++i) {
        if (!FillBroadPhaseOverlapRecord(deleted[i * 2u],
                deleted[i * 2u + 1u], slot.deleted[i])) {
            FailFinishBroadPhaseSlot(slot,
                FinishBroadPhaseObserverInvalidMetadata, ERROR_INVALID_DATA,
                4, i, 7);
            return static_cast<uint32_t>(ordinal);
        }
    }
    slot.receipt.deletedWritten = slot.receipt.deletedRequired;
    slot.receipt.createdHash = ByteHash(slot.created,
        slot.receipt.createdWritten * sizeof(BroadPhaseOverlapRecord));
    slot.receipt.deletedHash = ByteHash(slot.deleted,
        slot.receipt.deletedWritten * sizeof(BroadPhaseOverlapRecord));
    slot.receipt.validationFlags |= 0x0Cu;
    if (!HashTransformCacheNoAllocation(slot.receipt.observedContext,
            slot.receipt.preCacheHash)) {
        FailFinishBroadPhaseSlot(slot,
            FinishBroadPhaseObserverInvalidMetadata, ERROR_INVALID_DATA,
            5, 0xFFFFFFFFu, 8);
        return static_cast<uint32_t>(ordinal);
    }
    slot.receipt.validationFlags |= 0x10u;
    if (!HashInteractionGraphNoAllocation(scene,
            slot.receipt.observedNPhaseCore,
            slot.receipt.preGraphHash)) {
        FailFinishBroadPhaseSlot(slot,
            FinishBroadPhaseObserverInvalidMetadata, ERROR_INVALID_DATA,
            6, 0xFFFFFFFFu, 9);
        return static_cast<uint32_t>(ordinal);
    }
    slot.receipt.validationFlags |= 0x20u;
    return static_cast<uint32_t>(ordinal);
}

static void __cdecl ObserveFinishBroadPhaseExit(uintptr_t scene,
    uint32_t pass, uint32_t ordinal) {
    if (!ordinal ||
        InterlockedCompareExchange(&g_finishBroadPhaseState, 0, 0) !=
        FinishBroadPhaseObserverCapturing ||
        scene != g_finishBroadPhaseExpectedScene ||
        pass != g_finishBroadPhaseExpectedPass ||
        ordinal != g_finishBroadPhaseArmedOrdinal) return;
    const uint32_t slotIndex = (ordinal - 1u) %
        kFinishBroadPhaseRingCapacity;
    FinishBroadPhaseCaptureSlot& slot = g_finishBroadPhaseSlots[slotIndex];
    if (slot.receipt.observationOrdinal != ordinal ||
        slot.receipt.threadId != GetCurrentThreadId()) {
        FailFinishBroadPhaseSlot(slot, FinishBroadPhaseObserverUnstable,
            ERROR_RETRY, 7, 0xFFFFFFFFu, 10);
    }
    if (slot.receipt.result == FinishBroadPhaseObserverNotReady) {
        if (!HashTransformCacheNoAllocation(slot.receipt.observedContext,
                slot.receipt.postCacheHash))
            FailFinishBroadPhaseSlot(slot,
                FinishBroadPhaseObserverUnstable, ERROR_RETRY,
                5, 0xFFFFFFFFu, 11);
        else
            slot.receipt.validationFlags |= 0x40u;
    }
    if (slot.receipt.result == FinishBroadPhaseObserverNotReady) {
        if (!HashInteractionGraphNoAllocation(scene,
                slot.receipt.observedNPhaseCore,
                slot.receipt.postGraphHash))
            FailFinishBroadPhaseSlot(slot,
                FinishBroadPhaseObserverUnstable, ERROR_RETRY,
                6, 0xFFFFFFFFu, 12);
        else
            slot.receipt.validationFlags |= 0x80u;
    }
    slot.receipt.validationFlags |= 0x200u;
    if (slot.receipt.result == FinishBroadPhaseObserverNotReady) {
        slot.receipt.result = FinishBroadPhaseObserverOk;
        slot.receipt.lastError = ERROR_SUCCESS;
        slot.receipt.state = FinishBroadPhaseObserverCaptured;
    } else {
        slot.receipt.state = FinishBroadPhaseObserverFailed;
    }
    slot.receipt.droppedObservations = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_finishBroadPhaseDropped, 0, 0));
    MemoryBarrier();
    InterlockedExchange(&slot.committedOrdinal,
        static_cast<LONG>(ordinal));
    InterlockedExchange(&g_finishBroadPhaseState,
        slot.receipt.state == FinishBroadPhaseObserverCaptured ?
            FinishBroadPhaseObserverCaptured :
            FinishBroadPhaseObserverFailed);
}

__declspec(naked) static void HookFinishBroadPhase() {
    // The private token slot follows this wrapper invocation through the
    // untouched original.  Nested/concurrent wrappers receive token zero and
    // therefore cannot finalize the observation owned by another invocation.
    __asm push 0
    __asm pushfd
    __asm pushad
    __asm lock inc dword ptr [g_finishBroadPhaseInFlight]
    __asm mov eax, dword ptr [esp + 0x18]
    __asm mov edx, dword ptr [esp + 0x2C]
    __asm push edx
    __asm push eax
    __asm call ObserveFinishBroadPhaseEntry
    __asm add esp, 8
    __asm mov dword ptr [esp + 0x24], eax
    __asm popad
    __asm popfd
    // Preserve the Scene pointer across the untouched RET 4 function.  The
    // private argument is consumed by the trampoline; the caller's original
    // argument remains for this wrapper's final RET 4.
    __asm push ecx
    __asm push dword ptr [esp + 12]
    __asm call dword ptr [g_finishBroadPhaseTrampoline]
    __asm pop ecx
    __asm pushfd
    __asm pushad
    __asm mov eax, dword ptr [esp + 0x18]
    __asm mov edx, dword ptr [esp + 0x2C]
    __asm mov ecx, dword ptr [esp + 0x24]
    __asm push ecx
    __asm push edx
    __asm push eax
    __asm call ObserveFinishBroadPhaseExit
    __asm add esp, 12
    __asm lock dec dword ptr [g_finishBroadPhaseInFlight]
    __asm popad
    __asm popfd
    __asm lea esp, [esp + 4]
    __asm ret 4
}

static bool HasFinishBroadPhaseJump(const void* source) {
    const uint8_t* bytes = static_cast<const uint8_t*>(source);
    if (bytes[0] != 0xE9) return false;
    for (uint32_t i = 5; i < sizeof(kFinishBroadPhaseBytes); ++i)
        if (bytes[i] != 0x90) return false;
    const int32_t displacement = *reinterpret_cast<const int32_t*>(bytes + 1);
    return reinterpret_cast<uintptr_t>(source) + 5 + displacement ==
        reinterpret_cast<uintptr_t>(HookFinishBroadPhase);
}

static void InitializeFinishBroadPhaseObserverReceipt(uintptr_t unityBase,
    FinishBroadPhaseObserverReceipt* receipt) {
    ZeroMemory(receipt, sizeof(*receipt));
    receipt->apiVersion = kApiVersion;
    receipt->structSize = sizeof(*receipt);
    receipt->result = FinishBroadPhaseObserverOk;
    receipt->lastError = ERROR_SUCCESS;
    receipt->unityBase = unityBase;
    receipt->expectedScene = g_finishBroadPhaseExpectedScene;
    receipt->expectedContext = g_finishBroadPhaseExpectedContext;
    receipt->expectedNPhaseCore = g_finishBroadPhaseExpectedNPhaseCore;
    receipt->expectedPass = g_finishBroadPhaseExpectedPass;
    receipt->armedThreadId = g_finishBroadPhaseArmedThreadId;
    receipt->armedOrdinal = g_finishBroadPhaseArmedOrdinal;
    receipt->observationOrdinal = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_finishBroadPhaseOrdinal, 0, 0));
    receipt->installed = InterlockedCompareExchange(
            &g_finishBroadPhaseInstalled, 0, 0) == 1 &&
        g_finishBroadPhaseTrampoline &&
        unityBase == g_finishBroadPhaseUnityBase ? 1u : 0u;
    receipt->state = static_cast<uint32_t>(InterlockedCompareExchange(
        &g_finishBroadPhaseState, 0, 0));
    receipt->invalidIndex = 0xFFFFFFFFu;
    receipt->droppedObservations = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_finishBroadPhaseDropped, 0, 0));
}

static int InstallFinishBroadPhaseObserver(uintptr_t unityBase,
    FinishBroadPhaseObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    if (!unityBase)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverBadArgument, ERROR_INVALID_PARAMETER,
            0, 0xFFFFFFFFu, 1);
    const LONG lifecycle = InterlockedCompareExchange(
        &g_finishBroadPhaseInstalled, -1, 0);
    bool reuseResidentHook = false;
    if (lifecycle == 1 && InterlockedCompareExchange(
            &g_finishBroadPhaseState, 0, 0) ==
            FinishBroadPhaseObserverUninstalled) {
        reuseResidentHook = InterlockedCompareExchange(
            &g_finishBroadPhaseInstalled, -1, 1) == 1;
        if (!reuseResidentHook)
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverBusy, ERROR_BUSY,
                0, 0xFFFFFFFFu, 2);
    } else if (lifecycle != 0) {
        return FailFinishBroadPhaseObserver(receipt,
            lifecycle == 1 ? FinishBroadPhaseObserverAlreadyInstalled :
                FinishBroadPhaseObserverBusy,
            lifecycle == 1 ? ERROR_ALREADY_EXISTS : ERROR_BUSY,
            0, 0xFFFFFFFFu, 2);
    }
    uint8_t* source = reinterpret_cast<uint8_t*>(unityBase +
        kFinishBroadPhaseRva);
    if (reuseResidentHook) {
        if (!g_finishBroadPhaseTrampoline ||
            unityBase != g_finishBroadPhaseUnityBase ||
            !FinishBroadPhaseLayoutRevisionMatches(unityBase) ||
            !Readable(source, sizeof(kFinishBroadPhaseBytes)) ||
            !HasFinishBroadPhaseJump(source)) {
            InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
            return FailFinishBroadPhaseObserver(receipt,
                !FinishBroadPhaseLayoutRevisionMatches(unityBase) ?
                    FinishBroadPhaseObserverRevisionMismatch :
                    FinishBroadPhaseObserverPatchChanged,
                !FinishBroadPhaseLayoutRevisionMatches(unityBase) ?
                    ERROR_REVISION_MISMATCH : ERROR_INVALID_STATE,
                0, 0xFFFFFFFFu, 8);
        }
        g_finishBroadPhaseExpectedScene = 0;
        g_finishBroadPhaseExpectedContext = 0;
        g_finishBroadPhaseExpectedNPhaseCore = 0;
        g_finishBroadPhaseExpectedPass = 0;
        g_finishBroadPhaseArmedThreadId = 0;
        g_finishBroadPhaseArmedOrdinal = 0;
        InterlockedExchange(&g_finishBroadPhaseOrdinal, 0);
        InterlockedExchange(&g_finishBroadPhaseDropped, 0);
        for (uint32_t i = 0; i < kFinishBroadPhaseRingCapacity; ++i)
            InterlockedExchange(
                &g_finishBroadPhaseSlots[i].committedOrdinal, 0);
        InterlockedExchange(&g_finishBroadPhaseState,
            FinishBroadPhaseObserverIdle);
        InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
        InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
        return 1;
    }
    if (g_finishBroadPhaseTrampoline &&
        unityBase != g_finishBroadPhaseUnityBase) {
        InterlockedExchange(&g_finishBroadPhaseInstalled, 0);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverPatchChanged, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 6);
    }
    if (!FinishBroadPhaseRevisionMatches(unityBase)) {
        InterlockedExchange(&g_finishBroadPhaseInstalled, 0);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverRevisionMismatch,
            ERROR_REVISION_MISMATCH, 0, 0xFFFFFFFFu, 3);
    }
    uint8_t* trampoline = static_cast<uint8_t*>(
        g_finishBroadPhaseTrampoline);
    const bool allocated = trampoline == 0;
    if (allocated) {
        trampoline = static_cast<uint8_t*>(VirtualAlloc(0,
            sizeof(kFinishBroadPhaseBytes) + 5u, MEM_COMMIT | MEM_RESERVE,
            PAGE_EXECUTE_READWRITE));
        if (!trampoline) {
            const DWORD error = GetLastError();
            InterlockedExchange(&g_finishBroadPhaseInstalled, 0);
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverAllocationFailed, error,
                0, 0xFFFFFFFFu, 4);
        }
        CopyBytes(g_finishBroadPhaseOriginal, source,
            sizeof(kFinishBroadPhaseBytes));
        CopyBytes(trampoline, source, sizeof(kFinishBroadPhaseBytes));
        trampoline[sizeof(kFinishBroadPhaseBytes)] = 0xE9;
        *reinterpret_cast<int32_t*>(trampoline +
            sizeof(kFinishBroadPhaseBytes) + 1u) = static_cast<int32_t>(
            reinterpret_cast<uintptr_t>(source +
                sizeof(kFinishBroadPhaseBytes)) -
            reinterpret_cast<uintptr_t>(trampoline +
                sizeof(kFinishBroadPhaseBytes)) - 5);
        FlushInstructionCache(GetCurrentProcess(), trampoline,
            sizeof(kFinishBroadPhaseBytes) + 5u);
        g_finishBroadPhaseTrampoline = trampoline;
        g_finishBroadPhaseUnityBase = unityBase;
    }
    if (InterlockedCompareExchange(&g_finishBroadPhaseModulePinned,
            0, 0) == 0) {
        HMODULE pinnedModule = 0;
        if (!GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS |
                GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCSTR>(&g_finishBroadPhaseModuleMarker),
                &pinnedModule)) {
            const DWORD error = GetLastError();
            if (allocated) {
                g_finishBroadPhaseTrampoline = 0;
                g_finishBroadPhaseUnityBase = 0;
                VirtualFree(trampoline, 0, MEM_RELEASE);
            }
            InterlockedExchange(&g_finishBroadPhaseInstalled, 0);
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverAllocationFailed, error,
                0, 0xFFFFFFFFu, 7);
        }
        InterlockedExchange(&g_finishBroadPhaseModulePinned, 1);
    }
    g_finishBroadPhaseExpectedScene = 0;
    g_finishBroadPhaseExpectedContext = 0;
    g_finishBroadPhaseExpectedNPhaseCore = 0;
    g_finishBroadPhaseExpectedPass = 0;
    g_finishBroadPhaseArmedThreadId = 0;
    g_finishBroadPhaseArmedOrdinal = 0;
    InterlockedExchange(&g_finishBroadPhaseOrdinal, 0);
    InterlockedExchange(&g_finishBroadPhaseDropped, 0);
    for (uint32_t i = 0; i < kFinishBroadPhaseRingCapacity; ++i)
        InterlockedExchange(&g_finishBroadPhaseSlots[i].committedOrdinal, 0);
    InterlockedExchange(&g_finishBroadPhaseState,
        FinishBroadPhaseObserverIdle);
    if (!WriteJump(source, HookFinishBroadPhase,
            sizeof(kFinishBroadPhaseBytes))) {
        const DWORD error = GetLastError();
        InterlockedExchange(&g_finishBroadPhaseState,
            FinishBroadPhaseObserverUninstalled);
        const bool jumpLanded = Readable(source,
            sizeof(kFinishBroadPhaseBytes)) &&
            HasFinishBroadPhaseJump(source);
        if (!jumpLanded && allocated) {
            g_finishBroadPhaseTrampoline = 0;
            g_finishBroadPhaseUnityBase = 0;
            VirtualFree(trampoline, 0, MEM_RELEASE);
        }
        // WriteJump can report failure only after it has already written and
        // flushed the complete detour (for example, when restoring the old
        // page protection fails).  A landed jump must keep its trampoline,
        // pinned module, and logical resident state alive; freeing any of
        // them would turn a diagnostic protection error into a crash.
        InterlockedExchange(&g_finishBroadPhaseInstalled,
            jumpLanded ? 1 : 0);
        InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverProtectFailed, error,
            0, 0xFFFFFFFFu, 5);
    }
    InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    return 1;
}

static int ReadFinishBroadPhaseObserverStatus(uintptr_t unityBase,
    FinishBroadPhaseObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    if (InterlockedCompareExchange(&g_finishBroadPhaseInstalled, 0, 0) != 1 ||
        !g_finishBroadPhaseTrampoline ||
        unityBase != g_finishBroadPhaseUnityBase)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotInstalled, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 1);
    const LONG state = InterlockedCompareExchange(&g_finishBroadPhaseState,
        0, 0);
    if (state == FinishBroadPhaseObserverCaptured ||
        state == FinishBroadPhaseObserverFailed) {
        const uint32_t ordinal = static_cast<uint32_t>(
            InterlockedCompareExchange(&g_finishBroadPhaseOrdinal, 0, 0));
        if (!ordinal)
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverNotReady, ERROR_IO_PENDING,
                0, 0xFFFFFFFFu, 2);
        FinishBroadPhaseCaptureSlot& slot = g_finishBroadPhaseSlots[
            (ordinal - 1u) % kFinishBroadPhaseRingCapacity];
        if (static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal)
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverNotReady, ERROR_IO_PENDING,
                0, ordinal, 3);
        CopyBytes(receipt, &slot.receipt, sizeof(*receipt));
        MemoryBarrier();
        if (static_cast<uint32_t>(InterlockedCompareExchange(
                &slot.committedOrdinal, 0, 0)) != ordinal)
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverStale, ERROR_RETRY,
                0, ordinal, 4);
        receipt->installed = 1;
        receipt->state = static_cast<uint32_t>(state);
        receipt->droppedObservations = static_cast<uint32_t>(
            InterlockedCompareExchange(&g_finishBroadPhaseDropped, 0, 0));
        return receipt->result == FinishBroadPhaseObserverOk ? 1 : 0;
    }
    return 1;
}

static int ArmFinishBroadPhaseObserver(uintptr_t unityBase,
    uintptr_t expectedScene, uintptr_t expectedContext,
    uintptr_t expectedNPhaseCore, uint32_t expectedPass,
    FinishBroadPhaseObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    if (InterlockedCompareExchange(&g_finishBroadPhaseInstalled, 0, 0) != 1 ||
        !g_finishBroadPhaseTrampoline ||
        unityBase != g_finishBroadPhaseUnityBase)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotInstalled, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 1);
    const LONG initialState = InterlockedCompareExchange(
        &g_finishBroadPhaseState,
        0, 0);
    if (initialState == FinishBroadPhaseObserverUninstalled)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotInstalled, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 2);
    if (initialState == FinishBroadPhaseObserverArmed ||
        initialState == FinishBroadPhaseObserverCapturing)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverBusy, ERROR_BUSY,
            0, 0xFFFFFFFFu, 2);
    if (!expectedScene || !expectedContext || !expectedNPhaseCore ||
        expectedPass != 0u ||
        !Readable(reinterpret_cast<const void*>(expectedScene + 0x450),
            0x68) ||
        *reinterpret_cast<const uintptr_t*>(expectedScene + 0x450) !=
            expectedNPhaseCore ||
        *reinterpret_cast<const uintptr_t*>(expectedNPhaseCore) !=
            expectedScene) {
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverInvalidIdentity, ERROR_INVALID_DATA,
            1, 0xFFFFFFFFu, 3);
    }
    const uintptr_t interactionScene = *reinterpret_cast<const uintptr_t*>(
        expectedScene + 0x4B4);
    if (!interactionScene || !Readable(reinterpret_cast<const void*>(
            interactionScene), 0x3F4) ||
        *reinterpret_cast<const uintptr_t*>(interactionScene + 0x3E8) !=
            expectedContext ||
        *reinterpret_cast<const uintptr_t*>(interactionScene + 0x3F0) !=
            expectedScene ||
        !Readable(reinterpret_cast<const void*>(expectedContext), 0x1DE4) ||
        !*reinterpret_cast<const uintptr_t*>(expectedContext + 0x08))
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverInvalidIdentity, ERROR_INVALID_DATA,
            1, 0xFFFFFFFFu, 4);
    uint8_t* source = reinterpret_cast<uint8_t*>(unityBase +
        kFinishBroadPhaseRva);
    if (!Readable(source, sizeof(kFinishBroadPhaseBytes)) ||
        !HasFinishBroadPhaseJump(source))
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverPatchChanged, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 5);
    const LONG lifecycle = InterlockedCompareExchange(
        &g_finishBroadPhaseInstalled, -1, 1);
    if (lifecycle != 1)
        return FailFinishBroadPhaseObserver(receipt,
            lifecycle == -1 ? FinishBroadPhaseObserverBusy :
                FinishBroadPhaseObserverNotInstalled,
            lifecycle == -1 ? ERROR_BUSY : ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 6);
    if (!g_finishBroadPhaseTrampoline ||
        unityBase != g_finishBroadPhaseUnityBase ||
        !Readable(source, sizeof(kFinishBroadPhaseBytes)) ||
        !HasFinishBroadPhaseJump(source)) {
        InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverPatchChanged, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 7);
    }
    const LONG state = InterlockedCompareExchange(&g_finishBroadPhaseState,
        0, 0);
    if (state == FinishBroadPhaseObserverArmed ||
        state == FinishBroadPhaseObserverCapturing ||
        InterlockedCompareExchange(&g_finishBroadPhaseState,
            FinishBroadPhaseObserverCapturing, state) != state) {
        InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverBusy, ERROR_BUSY,
            0, 0xFFFFFFFFu, 8);
    }
    g_finishBroadPhaseExpectedScene = expectedScene;
    g_finishBroadPhaseExpectedContext = expectedContext;
    g_finishBroadPhaseExpectedNPhaseCore = expectedNPhaseCore;
    g_finishBroadPhaseExpectedPass = expectedPass;
    g_finishBroadPhaseArmedThreadId = GetCurrentThreadId();
    g_finishBroadPhaseArmedOrdinal = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_finishBroadPhaseOrdinal, 0, 0) + 1);
    MemoryBarrier();
    InterlockedExchange(&g_finishBroadPhaseState,
        FinishBroadPhaseObserverArmed);
    InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    return 1;
}

static int CopyFinishBroadPhaseObservation(uintptr_t unityBase,
    uint32_t observationOrdinal, BroadPhaseOverlapRecord* created,
    uint32_t createdCapacity, BroadPhaseOverlapRecord* deleted,
    uint32_t deletedCapacity, FinishBroadPhaseObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    if (InterlockedCompareExchange(&g_finishBroadPhaseInstalled, 0, 0) != 1 ||
        !g_finishBroadPhaseTrampoline ||
        unityBase != g_finishBroadPhaseUnityBase)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotInstalled, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 1);
    if (!observationOrdinal)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverBadArgument, ERROR_INVALID_PARAMETER,
            0, 0xFFFFFFFFu, 2);
    const uint32_t latest = static_cast<uint32_t>(
        InterlockedCompareExchange(&g_finishBroadPhaseOrdinal, 0, 0));
    if (observationOrdinal > latest)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotReady, ERROR_IO_PENDING,
            0, observationOrdinal, 3);
    if (latest - observationOrdinal >= kFinishBroadPhaseRingCapacity)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverStale, ERROR_INVALID_DATA,
            0, observationOrdinal, 4);
    const uint32_t slotIndex = (observationOrdinal - 1u) %
        kFinishBroadPhaseRingCapacity;
    FinishBroadPhaseCaptureSlot& slot = g_finishBroadPhaseSlots[slotIndex];
    if (static_cast<uint32_t>(InterlockedCompareExchange(
            &slot.committedOrdinal, 0, 0)) != observationOrdinal)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotReady, ERROR_IO_PENDING,
            0, observationOrdinal, 5);
    CopyBytes(receipt, &slot.receipt, sizeof(*receipt));
    if (receipt->result != FinishBroadPhaseObserverOk) return 0;
    if (createdCapacity < receipt->createdRequired ||
        deletedCapacity < receipt->deletedRequired)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverCapacityTooSmall,
            ERROR_INSUFFICIENT_BUFFER, 0, observationOrdinal, 6);
    if ((receipt->createdRequired && (!created || !Writable(created,
            receipt->createdRequired * sizeof(*created)))) ||
        (receipt->deletedRequired && (!deleted || !Writable(deleted,
            receipt->deletedRequired * sizeof(*deleted)))))
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverBadArgument, ERROR_NOACCESS,
            0, observationOrdinal, 7);
    CopyBytes(created, slot.created,
        receipt->createdRequired * sizeof(*created));
    CopyBytes(deleted, slot.deleted,
        receipt->deletedRequired * sizeof(*deleted));
    MemoryBarrier();
    if (static_cast<uint32_t>(InterlockedCompareExchange(
            &slot.committedOrdinal, 0, 0)) != observationOrdinal)
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverStale, ERROR_RETRY,
            0, observationOrdinal, 8);
    receipt->createdWritten = receipt->createdRequired;
    receipt->deletedWritten = receipt->deletedRequired;
    return 1;
}

static int CancelFinishBroadPhaseObserver(uintptr_t unityBase,
    FinishBroadPhaseObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    const LONG lifecycle = InterlockedCompareExchange(
        &g_finishBroadPhaseInstalled, -1, 1);
    if (lifecycle != 1)
        return FailFinishBroadPhaseObserver(receipt,
            lifecycle == -1 ? FinishBroadPhaseObserverBusy :
                FinishBroadPhaseObserverNotInstalled,
            lifecycle == -1 ? ERROR_BUSY : ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 1);
    if (!g_finishBroadPhaseTrampoline ||
        unityBase != g_finishBroadPhaseUnityBase) {
        InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotInstalled, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 1);
    }
    for (;;) {
        const LONG state = InterlockedCompareExchange(
            &g_finishBroadPhaseState, 0, 0);
        if (state == FinishBroadPhaseObserverUninstalled) break;
        if (state == FinishBroadPhaseObserverCapturing) {
            InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverBusy, ERROR_BUSY,
                0, 0xFFFFFFFFu, 2);
        }
        if (InterlockedCompareExchange(&g_finishBroadPhaseState,
                FinishBroadPhaseObserverIdle, state) == state)
            break;
    }
    g_finishBroadPhaseExpectedScene = 0;
    g_finishBroadPhaseExpectedContext = 0;
    g_finishBroadPhaseExpectedNPhaseCore = 0;
    g_finishBroadPhaseExpectedPass = 0;
    g_finishBroadPhaseArmedThreadId = 0;
    g_finishBroadPhaseArmedOrdinal = 0;
    InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    return 1;
}

static int UninstallFinishBroadPhaseObserver(uintptr_t unityBase,
    FinishBroadPhaseObserverReceipt* receipt) {
    if (!receipt) return 0;
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    const LONG lifecycle = InterlockedCompareExchange(
        &g_finishBroadPhaseInstalled, -1, 1);
    if (lifecycle != 1)
        return FailFinishBroadPhaseObserver(receipt,
            lifecycle == -1 ? FinishBroadPhaseObserverBusy :
                FinishBroadPhaseObserverNotInstalled,
            lifecycle == -1 ? ERROR_BUSY : ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 1);
    if (!g_finishBroadPhaseTrampoline ||
        unityBase != g_finishBroadPhaseUnityBase) {
        InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverNotInstalled, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 1);
    }
    LONG priorState = FinishBroadPhaseObserverUninstalled;
    for (;;) {
        priorState = InterlockedCompareExchange(&g_finishBroadPhaseState,
            0, 0);
        if (priorState == FinishBroadPhaseObserverCapturing) {
            InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
            return FailFinishBroadPhaseObserver(receipt,
                FinishBroadPhaseObserverBusy, ERROR_BUSY,
                0, 0xFFFFFFFFu, 2);
        }
        if (InterlockedCompareExchange(&g_finishBroadPhaseState,
                FinishBroadPhaseObserverUninstalled, priorState) ==
            priorState) break;
    }
    uint8_t* source = reinterpret_cast<uint8_t*>(unityBase +
        kFinishBroadPhaseRva);
    if (!Readable(source, sizeof(kFinishBroadPhaseBytes)) ||
        !HasFinishBroadPhaseJump(source)) {
        InterlockedCompareExchange(&g_finishBroadPhaseState, priorState,
            FinishBroadPhaseObserverUninstalled);
        InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
        return FailFinishBroadPhaseObserver(receipt,
            FinishBroadPhaseObserverPatchChanged, ERROR_INVALID_STATE,
            0, 0xFFFFFFFFu, 3);
    }
    g_finishBroadPhaseExpectedScene = 0;
    g_finishBroadPhaseExpectedContext = 0;
    g_finishBroadPhaseExpectedNPhaseCore = 0;
    g_finishBroadPhaseExpectedPass = 0;
    g_finishBroadPhaseArmedThreadId = 0;
    g_finishBroadPhaseArmedOrdinal = 0;
    // Logical uninstall only.  Rewriting a multi-byte x86 entry detour is not
    // safe without suspending every possible caller, so keep the pinned hook
    // resident and dormant until process exit.  A later install validates and
    // reuses it without touching executable code.
    InterlockedExchange(&g_finishBroadPhaseInstalled, 1);
    InitializeFinishBroadPhaseObserverReceipt(unityBase, receipt);
    receipt->result = FinishBroadPhaseObserverOk;
    receipt->lastError = ERROR_SUCCESS;
    return 1;
}

static int AuditContactRecreate(uintptr_t unityBase, uintptr_t context,
    uintptr_t nphaseCore, uintptr_t expectedSipPool,
    const uintptr_t* targetSip, uint32_t targetSipCount,
    uint32_t targetSipUsed, uint32_t targetSipUnreleased,
    uintptr_t expectedFreeArray, const uintptr_t* targetContact,
    uint32_t targetContactCount, uintptr_t expectedLargePool,
    const uintptr_t* targetLarge, uint32_t targetLargeCount,
    uint32_t targetLargeUsed, uint32_t targetLargeUnreleased,
    const ContactRecreatePlanRow* rows, uint32_t rowCount,
    ContactRecreateAuditReceipt* receipt) {
    if (!receipt) return 0;
    ZeroMemory(receipt, sizeof(*receipt));
    receipt->apiVersion = 1;
    receipt->structSize = sizeof(*receipt);
    receipt->unityBase = unityBase;
    receipt->context = context;
    receipt->nphaseCore = nphaseCore;
    receipt->sipPool = expectedSipPool;
    receipt->freeArray = expectedFreeArray;
    receipt->largePool = expectedLargePool;
    receipt->recreateState = static_cast<uint32_t>(g_contactRecreateState);
    receipt->observerInstalled = g_contactManagerContextObserverInstalled &&
        unityBase == g_observerUnityBase ? 1u : 0u;
    receipt->rowCount = rowCount;
    receipt->targetSipCount = targetSipCount;
    receipt->targetSipUsed = targetSipUsed;
    receipt->targetSipUnreleased = targetSipUnreleased;
    receipt->targetContactCount = targetContactCount;
    receipt->targetLargeCount = targetLargeCount;
    receipt->targetLargeUsed = targetLargeUsed;
    receipt->targetLargeUnreleased = targetLargeUnreleased;
    receipt->firstRow = 0xFFFFFFFFu;

    const auto issue = [&](uint32_t check, ContactRecreateResult result,
        uint32_t error, uint32_t row, uint32_t detail) {
        receipt->issueMask |= check;
        if (!receipt->firstResult) {
            receipt->firstResult = result;
            receipt->firstError = error;
            receipt->firstRow = row;
            receipt->firstDetail = detail;
        }
    };

    receipt->evaluatedMask |= ContactAuditArguments;
    const bool basicArguments = unityBase && context && nphaseCore &&
        expectedSipPool && expectedFreeArray && expectedLargePool &&
        targetContact && targetContactCount && rows && rowCount &&
        rowCount <= kMaximumContactRecreateRows &&
        targetSipCount <= kMaximumShapeInstancePairs &&
        targetContactCount <= kMaximumContactManagers &&
        targetLargeCount <= kMaximumManifolds &&
        targetSipCount + rowCount <= kMaximumShapeInstancePairs &&
        targetContactCount + rowCount <= kMaximumContactManagers &&
        targetLargeCount + rowCount <= kMaximumManifolds &&
        (!targetSipCount || targetSip) &&
        (!targetLargeCount || targetLarge);
    if (!basicArguments)
        issue(ContactAuditArguments, ContactRecreateBadArgument,
            ERROR_INVALID_PARAMETER, 0xFFFFFFFFu, 1);

    receipt->evaluatedMask |= ContactAuditObserver;
    if (!receipt->observerInstalled)
        issue(ContactAuditObserver, ContactRecreateObserverMissing,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 2);

    receipt->evaluatedMask |= ContactAuditRecreateState;
    if (g_contactRecreateState != ContactRecreateIdle)
        issue(ContactAuditRecreateState, ContactRecreateAlreadyArmed,
            ERROR_BUSY, 0xFFFFFFFFu, 3);

    receipt->evaluatedMask |= ContactAuditRevisions;
    if (!unityBase || !ContactManagerOwnerRevisionMatches(unityBase) ||
        !ShapeInstancePairPoolRevisionMatches(unityBase) ||
        !ManifoldPoolRevisionMatches(unityBase))
        issue(ContactAuditRevisions, ContactRecreateRevisionMismatch,
            ERROR_REVISION_MISMATCH, 0xFFFFFFFFu, 4);

    bool readableTargets = false;
    if (basicArguments) {
        receipt->evaluatedMask |= ContactAuditTargetBuffers;
        readableTargets = (!targetSipCount || Readable(targetSip,
                targetSipCount * sizeof(uintptr_t))) &&
            Readable(targetContact,
                targetContactCount * sizeof(uintptr_t)) &&
            (!targetLargeCount || Readable(targetLarge,
                targetLargeCount * sizeof(uintptr_t))) &&
            Readable(rows, rowCount * sizeof(ContactRecreatePlanRow));
        if (!readableTargets)
            issue(ContactAuditTargetBuffers, ContactRecreateBadArgument,
                ERROR_NOACCESS, 0xFFFFFFFFu, 5);
    }

    if (readableTargets) {
        receipt->targetSipHash = OrderHash(targetSip, targetSipCount);
        receipt->targetContactHash = OrderHash(targetContact,
            targetContactCount);
        receipt->targetLargeHash = OrderHash(targetLarge, targetLargeCount);

        receipt->evaluatedMask |= ContactAuditTargetUniqueness;
        if ((targetSipCount && !UniqueNonzeroPointers(targetSip,
                targetSipCount)) ||
            !UniqueNonzeroPointers(targetContact, targetContactCount) ||
            (targetLargeCount && !UniqueNonzeroPointers(targetLarge,
                targetLargeCount)))
            issue(ContactAuditTargetUniqueness, ContactRecreateInvalidPlan,
                ERROR_DUP_NAME, 0xFFFFFFFFu, 6);

        receipt->evaluatedMask |= ContactAuditRows;
        for (uint32_t i = 0; i < rowCount; ++i) {
            const ContactRecreatePlanRow& row = rows[i];
            if (!row.pxsShapeCoreLow ||
                row.pxsShapeCoreLow >= row.pxsShapeCoreHigh ||
                !row.targetManager || !row.targetManifold ||
                !row.targetSip || row.manifoldBytes != 0xF0u ||
                ContainsPointer(targetContact, targetContactCount,
                    row.targetManager) ||
                ContainsPointer(targetLarge, targetLargeCount,
                    row.targetManifold)) {
                ++receipt->invalidRowCount;
                issue(ContactAuditRows, ContactRecreateInvalidPlan,
                    ERROR_INVALID_DATA, i, 11);
            }
            if (ContainsPointer(targetSip, targetSipCount, row.targetSip))
                ++receipt->targetFreeSipRowCount;
            else
                ++receipt->activeSipRowCount;
        }

        receipt->evaluatedMask |= ContactAuditRowUniqueness;
        for (uint32_t i = 0; i < rowCount; ++i) {
            for (uint32_t j = 0; j < i; ++j) {
                if ((rows[j].pxsShapeCoreLow == rows[i].pxsShapeCoreLow &&
                        rows[j].pxsShapeCoreHigh ==
                            rows[i].pxsShapeCoreHigh) ||
                    rows[j].targetManager == rows[i].targetManager ||
                    rows[j].targetManifold == rows[i].targetManifold ||
                    rows[j].targetSip == rows[i].targetSip ||
                    rows[j].targetSlot == rows[i].targetSlot) {
                    ++receipt->duplicateRowCount;
                    issue(ContactAuditRowUniqueness,
                        ContactRecreateInvalidPlan, ERROR_DUP_NAME, i, 12);
                    break;
                }
            }
        }
    }

    uintptr_t liveSip[kMaximumShapeInstancePairs] = {};
    SipPoolReceipt sipReceipt = {};
    bool sipCaptured = false;
    if (nphaseCore && unityBase) {
        receipt->evaluatedMask |= ContactAuditSipCapture;
        sipCaptured = CaptureShapeInstancePairPool(unityBase, nphaseCore,
            liveSip, kMaximumShapeInstancePairs, &sipReceipt) != 0;
        if (!sipCaptured)
            issue(ContactAuditSipCapture, ContactRecreateSipMembership,
                sipReceipt.lastError, 0xFFFFFFFFu, 31);
        else {
            receipt->liveSipCount = sipReceipt.traversedCount;
            receipt->liveSipHash = sipReceipt.orderHash;
            receipt->liveSipUsed = sipReceipt.used;
            receipt->liveSipUnreleased = sipReceipt.unreleased;
        }
    }
    if (sipCaptured) {
        receipt->evaluatedMask |= ContactAuditSipIdentity;
        if (sipReceipt.pool != expectedSipPool ||
            sipReceipt.freeHead != (sipReceipt.traversedCount
                ? liveSip[0] : 0))
            issue(ContactAuditSipIdentity, ContactRecreateSipMembership,
                ERROR_INVALID_STATE, 0xFFFFFFFFu, 32);
    }
    if (sipCaptured && readableTargets) {
        receipt->expectedSemanticSipCount = targetSipCount +
            receipt->activeSipRowCount;
        receipt->expectedLegacySipCount = targetSipCount + rowCount;
        receipt->evaluatedMask |= ContactAuditSipSemanticCounts;
        if (sipReceipt.traversedCount !=
                receipt->expectedSemanticSipCount ||
            sipReceipt.used + receipt->activeSipRowCount != targetSipUsed ||
            sipReceipt.unreleased != targetSipUnreleased +
                receipt->activeSipRowCount)
            issue(ContactAuditSipSemanticCounts,
                ContactRecreateSipMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 35);

        receipt->evaluatedMask |= ContactAuditSipLegacyArm;
        if (receipt->targetFreeSipRowCount != 0 ||
            sipReceipt.traversedCount != receipt->expectedLegacySipCount ||
            sipReceipt.used + rowCount != targetSipUsed ||
            sipReceipt.unreleased != targetSipUnreleased + rowCount)
            issue(ContactAuditSipLegacyArm, ContactRecreateSipMembership,
                ERROR_INVALID_STATE, 0xFFFFFFFFu, 32);

        receipt->evaluatedMask |= ContactAuditSipMembership;
        for (uint32_t i = 0; i < targetSipCount; ++i)
            if (!ContainsPointer(liveSip, sipReceipt.traversedCount,
                    targetSip[i]))
                ++receipt->sipMissingCount;
        for (uint32_t i = 0; i < rowCount; ++i)
            if (!ContainsPointer(targetSip, targetSipCount,
                    rows[i].targetSip) &&
                !ContainsPointer(liveSip, sipReceipt.traversedCount,
                    rows[i].targetSip))
                ++receipt->sipMissingCount;
        for (uint32_t i = 0; i < sipReceipt.traversedCount; ++i) {
            if (ContainsPointer(targetSip, targetSipCount, liveSip[i]))
                continue;
            bool rowSip = false;
            for (uint32_t j = 0; j < rowCount; ++j)
                if (!ContainsPointer(targetSip, targetSipCount,
                        rows[j].targetSip) &&
                    rows[j].targetSip == liveSip[i]) {
                    rowSip = true;
                    break;
                }
            if (!rowSip) ++receipt->sipExtraCount;
        }
        if (receipt->sipMissingCount || receipt->sipExtraCount)
            issue(ContactAuditSipMembership,
                ContactRecreateSipMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 33);
    }

    uintptr_t* liveContact = 0;
    uint32_t liveContactCount = 0;
    ContactPoolReceipt contactReceipt = {};
    InitializeContactPoolReceipt(&contactReceipt, context);
    bool contactCaptured = false;
    if (context) {
        receipt->evaluatedMask |= ContactAuditContactCapture;
        contactCaptured = ReadContactPool(context, liveContact,
            liveContactCount, &contactReceipt);
        if (!contactCaptured)
            issue(ContactAuditContactCapture,
                ContactRecreateContactMembership, contactReceipt.lastError,
                0xFFFFFFFFu, 7);
        else {
            receipt->liveContactCount = liveContactCount;
            receipt->liveContactHash = OrderHash(liveContact,
                liveContactCount);
        }
    }

    uintptr_t contactPool = 0;
    uintptr_t slabs = 0;
    uint32_t totalSlots = 0;
    if (contactCaptured) {
        receipt->expectedContactCount = targetContactCount + rowCount;
        receipt->evaluatedMask |= ContactAuditContactIdentity;
        contactPool = context + 0x2B8;
        if (!Readable(reinterpret_cast<const void*>(contactPool), 0x2C)) {
            issue(ContactAuditContactIdentity,
                ContactRecreateContactMembership, ERROR_NOACCESS,
                0xFFFFFFFFu, 9);
        } else {
            const uint32_t elementsPerSlab =
                *reinterpret_cast<const uint32_t*>(contactPool + 0x00);
            const uint32_t slabCount =
                *reinterpret_cast<const uint32_t*>(contactPool + 0x08);
            slabs = *reinterpret_cast<const uintptr_t*>(contactPool + 0x18);
            totalSlots = elementsPerSlab * slabCount;
            if (reinterpret_cast<uintptr_t>(liveContact) !=
                    expectedFreeArray ||
                liveContactCount != receipt->expectedContactCount ||
                elementsPerSlab != 256u || !slabCount ||
                totalSlots != liveContactCount || !slabs ||
                !Readable(reinterpret_cast<const void*>(slabs),
                    slabCount * sizeof(uintptr_t)))
                issue(ContactAuditContactIdentity,
                    ContactRecreateContactMembership, ERROR_INVALID_STATE,
                    0xFFFFFFFFu, 8);
        }
    }

    if (contactCaptured && totalSlots) {
        receipt->evaluatedMask |= ContactAuditContactBitmaps;
        uintptr_t useMap = 0, activeMap = 0, touchMap = 0,
            modifiableMap = 0;
        uint32_t useWords = 0, activeWords = 0, touchWords = 0,
            modifiableWords = 0;
        const bool bitmapsReadable =
            ReadContactBitmap(contactPool + 0x20, totalSlots, useMap,
                useWords) &&
            ReadContactBitmap(context + 0x534, totalSlots, activeMap,
                activeWords) &&
            ReadContactBitmap(context + 0x540, totalSlots, touchMap,
                touchWords) &&
            ReadContactBitmap(context + 0x16D0, totalSlots,
                modifiableMap, modifiableWords);
        if (bitmapsReadable) {
            receipt->useBitmapCount = ContactBitmapCount(useMap, totalSlots);
            receipt->activeBitmapCount = ContactBitmapCount(activeMap,
                totalSlots);
            receipt->touchBitmapCount = ContactBitmapCount(touchMap,
                totalSlots);
            receipt->modifiableBitmapCount = ContactBitmapCount(
                modifiableMap, totalSlots);
        }
        if (!bitmapsReadable || receipt->useBitmapCount ||
            receipt->activeBitmapCount || receipt->touchBitmapCount ||
            receipt->modifiableBitmapCount)
            issue(ContactAuditContactBitmaps,
                ContactRecreateContactMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 10);
    }

    if (contactCaptured && readableTargets) {
        receipt->evaluatedMask |= ContactAuditContactMembership;
        for (uint32_t i = 0; i < targetContactCount; ++i)
            if (!ContainsPointer(liveContact, liveContactCount,
                    targetContact[i]))
                ++receipt->contactMissingCount;
        for (uint32_t i = 0; i < rowCount; ++i)
            if (!ContainsPointer(liveContact, liveContactCount,
                    rows[i].targetManager))
                ++receipt->contactMissingCount;
        for (uint32_t i = 0; i < liveContactCount; ++i) {
            if (ContainsPointer(targetContact, targetContactCount,
                    liveContact[i])) continue;
            bool rowManager = false;
            for (uint32_t j = 0; j < rowCount; ++j)
                if (rows[j].targetManager == liveContact[i]) {
                    rowManager = true;
                    break;
                }
            if (!rowManager) ++receipt->contactExtraCount;
        }
        if (receipt->contactMissingCount || receipt->contactExtraCount)
            issue(ContactAuditContactMembership,
                ContactRecreateContactMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 14);

        if (slabs && totalSlots) {
            for (uint32_t i = 0; i < rowCount; ++i) {
                const ContactRecreatePlanRow& row = rows[i];
                if (row.targetSlot >= totalSlots) {
                    ++receipt->invalidRowCount;
                    issue(ContactAuditRows, ContactRecreateInvalidPlan,
                        ERROR_INVALID_DATA, i, 13);
                    continue;
                }
                const uintptr_t slab =
                    reinterpret_cast<const uintptr_t*>(slabs)[
                        row.targetSlot >> 8];
                if (!slab || row.targetManager != slab +
                        (row.targetSlot & 0xFFu) * kContactManagerSize ||
                    !Readable(reinterpret_cast<const void*>(
                        row.targetManager + 0x4C), 4) ||
                    *reinterpret_cast<const uint32_t*>(
                        row.targetManager + 0x4C) != row.targetSlot) {
                    ++receipt->invalidRowCount;
                    issue(ContactAuditRows, ContactRecreateInvalidPlan,
                        ERROR_INVALID_DATA, i, 13);
                }
            }
        }
    }

    uintptr_t liveLarge[kMaximumManifolds] = {};
    uint32_t liveLargeCount = 0;
    ManifoldPoolReceipt manifoldReceipt = {};
    InitializeManifoldPoolReceipt(&manifoldReceipt, unityBase, context, 0);
    bool largeCaptured = false;
    if (unityBase && context) {
        receipt->evaluatedMask |= ContactAuditLargeCapture;
        largeCaptured = ReadManifoldPool(context, 0, liveLarge,
            liveLargeCount, &manifoldReceipt);
        if (!largeCaptured)
            issue(ContactAuditLargeCapture,
                ContactRecreateManifoldMembership,
                manifoldReceipt.lastError, 0xFFFFFFFFu, 15);
        else {
            receipt->liveLargeCount = liveLargeCount;
            receipt->liveLargeHash = OrderHash(liveLarge, liveLargeCount);
            receipt->liveLargeUsed = manifoldReceipt.used;
            receipt->liveLargeUnreleased = manifoldReceipt.unreleased;
        }
    }
    if (largeCaptured) {
        receipt->expectedLargeCount = targetLargeCount + rowCount;
        receipt->evaluatedMask |= ContactAuditLargeIdentity;
        if (manifoldReceipt.pool != expectedLargePool ||
            liveLargeCount != receipt->expectedLargeCount ||
            manifoldReceipt.used + rowCount != targetLargeUsed ||
            manifoldReceipt.unreleased != targetLargeUnreleased + rowCount)
            issue(ContactAuditLargeIdentity,
                ContactRecreateMetadataMismatch, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 16);
    }
    if (largeCaptured && readableTargets) {
        receipt->evaluatedMask |= ContactAuditLargeMembership;
        for (uint32_t i = 0; i < targetLargeCount; ++i)
            if (!ContainsPointer(liveLarge, liveLargeCount, targetLarge[i]))
                ++receipt->largeMissingCount;
        for (uint32_t i = 0; i < rowCount; ++i)
            if (!ContainsPointer(liveLarge, liveLargeCount,
                    rows[i].targetManifold))
                ++receipt->largeMissingCount;
        for (uint32_t i = 0; i < liveLargeCount; ++i) {
            if (ContainsPointer(targetLarge, targetLargeCount,
                    liveLarge[i])) continue;
            bool rowManifold = false;
            for (uint32_t j = 0; j < rowCount; ++j)
                if (rows[j].targetManifold == liveLarge[i]) {
                    rowManifold = true;
                    break;
                }
            if (!rowManifold) ++receipt->largeExtraCount;
        }
        if (receipt->largeMissingCount || receipt->largeExtraCount)
            issue(ContactAuditLargeMembership,
                ContactRecreateManifoldMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 17);
    }

    if (sipCaptured && contactCaptured && largeCaptured) {
        receipt->evaluatedMask |= ContactAuditWritability;
        if (!Writable(liveContact,
                liveContactCount * sizeof(uintptr_t)))
            ++receipt->unwritableCount;
        if (!Writable(reinterpret_cast<void*>(expectedSipPool + 0x124),
                sizeof(uintptr_t)))
            ++receipt->unwritableCount;
        if (!Writable(reinterpret_cast<void*>(expectedLargePool + 0x124),
                sizeof(uintptr_t)))
            ++receipt->unwritableCount;
        for (uint32_t i = 0; i < sipReceipt.traversedCount; ++i)
            if (!Writable(reinterpret_cast<void*>(liveSip[i]),
                    sizeof(uintptr_t)))
                ++receipt->unwritableCount;
        for (uint32_t i = 0; i < liveLargeCount; ++i)
            if (!Writable(reinterpret_cast<void*>(liveLarge[i]),
                    sizeof(uintptr_t)))
                ++receipt->unwritableCount;
        if (receipt->unwritableCount)
            issue(ContactAuditWritability, ContactRecreateNotWritable,
                ERROR_NOACCESS, 0xFFFFFFFFu, 18);
    }

    receipt->result = receipt->issueMask
        ? ContactRecreateAuditHasIssues : ContactRecreateAuditClean;
    receipt->lastError = receipt->firstError;
    return 1;
}

static void WriteManifoldOrder(uintptr_t pool, const uintptr_t* order,
    uint32_t count) {
    for (uint32_t i = 0; i < count; ++i)
        *reinterpret_cast<uintptr_t*>(order[i]) =
            i + 1 < count ? order[i + 1] : 0;
    *reinterpret_cast<uintptr_t*>(pool + 0x124) = count ? order[0] : 0;
}

static void ClearContactRecreateState() {
    InterlockedExchange(&g_contactRecreateState, ContactRecreateIdle);
    InterlockedExchange(&g_contactRecreateResult, ContactRecreateOk);
    InterlockedExchange(&g_contactRecreateError, ERROR_SUCCESS);
    g_contactRecreateUnityBase = 0;
    g_contactRecreateContext = 0;
    g_contactRecreateFreeArray = 0;
    g_contactRecreateLargePool = 0;
    g_contactRecreateRowCount = 0;
    g_contactRecreateMatchedCount = 0;
    g_contactRecreateMatchedMask = 0;
    g_contactRecreateThreadId = 0;
    InterlockedExchange(&g_contactRecreateObserverEntries, 0);
    g_contactRecreateContactCountBefore = 0;
    g_contactRecreateTargetContactCount = 0;
    g_contactRecreateContactHashBefore = 0;
    g_contactRecreateTargetContactHash = 0;
    g_contactRecreateLargeCountBefore = 0;
    g_contactRecreateTargetLargeCount = 0;
    g_contactRecreateLargeHashBefore = 0;
    g_contactRecreateTargetLargeHash = 0;
    g_contactRecreateLargeUsedBefore = 0;
    g_contactRecreateTargetLargeUsed = 0;
    g_contactRecreateLargeUnreleasedBefore = 0;
    g_contactRecreateTargetLargeUnreleased = 0;
    g_contactRecreateLastSip = 0;
    g_contactRecreateLastShapeLow = 0;
    g_contactRecreateLastShapeHigh = 0;
    g_contactRecreateLastManager = 0;
    g_contactRecreateLastManifold = 0;
    g_contactRecreateInvalidRow = 0xFFFFFFFFu;
    g_contactRecreateDetail = 0;
    g_contactRecreateAttemptOrdinal = 0;
    g_contactRecreateAttemptSip = 0;
    g_contactRecreateAttemptShapeLow = 0;
    g_contactRecreateAttemptShapeHigh = 0;
    g_contactRecreateAttemptRow = 0xFFFFFFFFu;
    g_contactRecreateAttemptMatchedMask = 0;
    g_contactRecreateAttemptFreeCount = 0xFFFFFFFFu;
    g_contactRecreateAttemptManagerIndex = 0xFFFFFFFFu;
    g_contactRecreateAttemptLargeHead = 0;
    g_contactRecreateAttemptManifoldIndex = 0xFFFFFFFFu;
    g_contactRecreateNPhaseCore = 0;
    g_contactRecreateSipPool = 0;
    g_contactRecreateSipMatchedCount = 0;
    g_contactRecreateSipMatchedMask = 0;
    g_contactRecreateTargetSipCount = 0;
    g_contactRecreateTargetSipHash = 0;
    g_contactRecreateTargetSipUsed = 0;
    g_contactRecreateTargetSipUnreleased = 0;
    g_contactRecreateLastAllocatedSip = 0;
    g_sipRecreateAttemptRow = 0xFFFFFFFFu;
    InterlockedExchange(&g_sipRecreateObserverEntries, 0);
}

static int ArmContactRecreate(uintptr_t unityBase, uintptr_t context,
    uintptr_t nphaseCore, uintptr_t expectedSipPool,
    const uintptr_t* targetSip, uint32_t targetSipCount,
    uint32_t targetSipUsed, uint32_t targetSipUnreleased,
    uintptr_t expectedFreeArray, const uintptr_t* targetContact,
    uint32_t targetContactCount, uintptr_t expectedLargePool,
    const uintptr_t* targetLarge, uint32_t targetLargeCount,
    uint32_t targetLargeUsed, uint32_t targetLargeUnreleased,
    const ContactRecreatePlanRow* rows, uint32_t rowCount,
    ContactRecreateReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactRecreateReceipt(receipt, unityBase, context);
    if (!unityBase || !context || !nphaseCore || !expectedSipPool ||
        !expectedFreeArray || !expectedLargePool ||
        !targetContact || !targetContactCount || !rows || !rowCount ||
        rowCount > kMaximumContactRecreateRows ||
        targetSipCount > kMaximumShapeInstancePairs - rowCount ||
        targetContactCount > kMaximumContactManagers - rowCount ||
        targetLargeCount > kMaximumManifolds - rowCount ||
        (targetSipCount && !targetSip) ||
        (targetLargeCount && !targetLarge))
        return FailContactRecreate(receipt, ContactRecreateBadArgument,
            ERROR_INVALID_PARAMETER, 0xFFFFFFFFu, 1);
    if (!g_contactManagerContextObserverInstalled ||
        unityBase != g_observerUnityBase)
        return FailContactRecreate(receipt, ContactRecreateObserverMissing,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 2);
    if (g_contactRecreateState == ContactRecreateArmed)
        return FailContactRecreate(receipt, ContactRecreateAlreadyArmed,
            ERROR_ALREADY_EXISTS, 0xFFFFFFFFu, 3);
    if (!ContactManagerOwnerRevisionMatches(unityBase) ||
        !ShapeInstancePairPoolRevisionMatches(unityBase) ||
        !ManifoldPoolRevisionMatches(unityBase))
        return FailContactRecreate(receipt, ContactRecreateRevisionMismatch,
            ERROR_REVISION_MISMATCH, 0xFFFFFFFFu, 4);
    if ((targetSipCount && !Readable(targetSip,
            targetSipCount * sizeof(uintptr_t))) ||
        !Readable(targetContact, targetContactCount * sizeof(uintptr_t)) ||
        (targetLargeCount && !Readable(targetLarge,
            targetLargeCount * sizeof(uintptr_t))) ||
        !Readable(rows, rowCount * sizeof(ContactRecreatePlanRow)))
        return FailContactRecreate(receipt, ContactRecreateBadArgument,
            ERROR_NOACCESS, 0xFFFFFFFFu, 5);
    if ((targetSipCount && !UniqueNonzeroPointers(targetSip,
            targetSipCount)) ||
        !UniqueNonzeroPointers(targetContact, targetContactCount) ||
        (targetLargeCount && !UniqueNonzeroPointers(targetLarge,
            targetLargeCount)))
        return FailContactRecreate(receipt, ContactRecreateInvalidPlan,
            ERROR_DUP_NAME, 0xFFFFFFFFu, 6);

    uintptr_t liveSip[kMaximumShapeInstancePairs] = {};
    SipPoolReceipt sipReceipt = {};
    if (!CaptureShapeInstancePairPool(unityBase, nphaseCore, liveSip,
            kMaximumShapeInstancePairs, &sipReceipt))
        return FailContactRecreate(receipt, ContactRecreateSipMembership,
            sipReceipt.lastError, 0xFFFFFFFFu, 31);
    if (sipReceipt.pool != expectedSipPool ||
        liveSip[0] != sipReceipt.freeHead ||
        sipReceipt.traversedCount != targetSipCount + rowCount ||
        sipReceipt.used + rowCount != targetSipUsed ||
        sipReceipt.unreleased != targetSipUnreleased + rowCount)
        return FailContactRecreate(receipt, ContactRecreateSipMembership,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 32);

    uintptr_t* liveContact = 0;
    uint32_t liveContactCount = 0;
    ContactPoolReceipt contactReceipt = {};
    InitializeContactPoolReceipt(&contactReceipt, context);
    if (!ReadContactPool(context, liveContact, liveContactCount,
            &contactReceipt))
        return FailContactRecreate(receipt, ContactRecreateContactMembership,
            contactReceipt.lastError, 0xFFFFFFFFu, 7);
    if (reinterpret_cast<uintptr_t>(liveContact) != expectedFreeArray ||
        liveContactCount != targetContactCount + rowCount)
        return FailContactRecreate(receipt, ContactRecreateContactMembership,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 8);

    const uintptr_t contactPool = context + 0x2B8;
    if (!Readable(reinterpret_cast<const void*>(contactPool), 0x2C))
        return FailContactRecreate(receipt, ContactRecreateContactMembership,
            ERROR_NOACCESS, 0xFFFFFFFFu, 9);
    const uint32_t elementsPerSlab = *reinterpret_cast<const uint32_t*>(
        contactPool + 0x00);
    const uint32_t slabCount = *reinterpret_cast<const uint32_t*>(
        contactPool + 0x08);
    const uintptr_t slabs = *reinterpret_cast<const uintptr_t*>(
        contactPool + 0x18);
    const uint32_t totalSlots = elementsPerSlab * slabCount;
    uintptr_t useMap = 0, activeMap = 0, touchMap = 0, modifiableMap = 0;
    uint32_t useWords = 0, activeWords = 0, touchWords = 0,
        modifiableWords = 0;
    if (elementsPerSlab != 256u || !slabCount ||
        totalSlots != liveContactCount || !slabs ||
        !Readable(reinterpret_cast<const void*>(slabs),
            slabCount * sizeof(uintptr_t)) ||
        !ReadContactBitmap(contactPool + 0x20, totalSlots, useMap, useWords) ||
        !ReadContactBitmap(context + 0x534, totalSlots, activeMap,
            activeWords) ||
        !ReadContactBitmap(context + 0x540, totalSlots, touchMap,
            touchWords) ||
        !ReadContactBitmap(context + 0x16D0, totalSlots, modifiableMap,
            modifiableWords) || ContactBitmapCount(useMap, totalSlots) != 0 ||
        ContactBitmapCount(activeMap, totalSlots) != 0 ||
        ContactBitmapCount(touchMap, totalSlots) != 0 ||
        ContactBitmapCount(modifiableMap, totalSlots) != 0)
        return FailContactRecreate(receipt, ContactRecreateContactMembership,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 10);

    for (uint32_t i = 0; i < rowCount; ++i) {
        const ContactRecreatePlanRow& row = rows[i];
        if (!row.pxsShapeCoreLow ||
            row.pxsShapeCoreLow >= row.pxsShapeCoreHigh ||
            !row.targetManager || !row.targetManifold || !row.targetSip ||
            row.targetSlot >= totalSlots || row.manifoldBytes != 0xF0u ||
            ContainsPointer(targetSip, targetSipCount, row.targetSip) ||
            ContainsPointer(targetContact, targetContactCount,
                row.targetManager) ||
            ContainsPointer(targetLarge, targetLargeCount,
                row.targetManifold))
            return FailContactRecreate(receipt, ContactRecreateInvalidPlan,
                ERROR_INVALID_DATA, i, 11);
        for (uint32_t j = 0; j < i; ++j)
            if ((rows[j].pxsShapeCoreLow == row.pxsShapeCoreLow &&
                    rows[j].pxsShapeCoreHigh == row.pxsShapeCoreHigh) ||
                rows[j].targetManager == row.targetManager ||
                rows[j].targetManifold == row.targetManifold ||
                rows[j].targetSip == row.targetSip ||
                rows[j].targetSlot == row.targetSlot)
                return FailContactRecreate(receipt,
                    ContactRecreateInvalidPlan, ERROR_DUP_NAME, i, 12);
        const uintptr_t slab = reinterpret_cast<const uintptr_t*>(slabs)[
            row.targetSlot >> 8];
        if (!slab || row.targetManager != slab +
                (row.targetSlot & 0xFFu) * kContactManagerSize ||
            !Readable(reinterpret_cast<const void*>(row.targetManager +
                0x4C), 4) || *reinterpret_cast<const uint32_t*>(
                row.targetManager + 0x4C) != row.targetSlot)
            return FailContactRecreate(receipt,
                ContactRecreateInvalidPlan, ERROR_INVALID_DATA, i, 13);
    }
    for (uint32_t i = 0; i < sipReceipt.traversedCount; ++i) {
        if (ContainsPointer(targetSip, targetSipCount, liveSip[i])) continue;
        bool rowSip = false;
        for (uint32_t j = 0; j < rowCount; ++j)
            if (rows[j].targetSip == liveSip[i]) {
                rowSip = true;
                break;
            }
        if (!rowSip)
            return FailContactRecreate(receipt,
                ContactRecreateSipMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 33);
    }
    for (uint32_t i = 0; i < liveContactCount; ++i) {
        if (ContainsPointer(targetContact, targetContactCount,
                liveContact[i])) continue;
        bool rowManager = false;
        for (uint32_t j = 0; j < rowCount; ++j)
            if (rows[j].targetManager == liveContact[i]) {
                rowManager = true;
                break;
            }
        if (!rowManager)
            return FailContactRecreate(receipt,
                ContactRecreateContactMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 14);
    }

    uintptr_t liveLarge[kMaximumManifolds] = {};
    uint32_t liveLargeCount = 0;
    ManifoldPoolReceipt manifoldReceipt = {};
    InitializeManifoldPoolReceipt(&manifoldReceipt, unityBase, context, 0);
    if (!ReadManifoldPool(context, 0, liveLarge, liveLargeCount,
            &manifoldReceipt))
        return FailContactRecreate(receipt,
            ContactRecreateManifoldMembership, manifoldReceipt.lastError,
            0xFFFFFFFFu, 15);
    if (manifoldReceipt.pool != expectedLargePool ||
        liveLargeCount != targetLargeCount + rowCount ||
        manifoldReceipt.used + rowCount != targetLargeUsed ||
        manifoldReceipt.unreleased != targetLargeUnreleased + rowCount)
        return FailContactRecreate(receipt,
            ContactRecreateMetadataMismatch, ERROR_INVALID_STATE,
            0xFFFFFFFFu, 16);
    for (uint32_t i = 0; i < liveLargeCount; ++i) {
        if (ContainsPointer(targetLarge, targetLargeCount,
                liveLarge[i])) continue;
        bool rowManifold = false;
        for (uint32_t j = 0; j < rowCount; ++j)
            if (rows[j].targetManifold == liveLarge[i]) {
                rowManifold = true;
                break;
            }
        if (!rowManifold)
            return FailContactRecreate(receipt,
                ContactRecreateManifoldMembership, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 17);
    }

    if (!Writable(liveContact, liveContactCount * sizeof(uintptr_t)) ||
        !Writable(reinterpret_cast<void*>(expectedSipPool + 0x124),
            sizeof(uintptr_t)) ||
        !Writable(reinterpret_cast<void*>(expectedLargePool + 0x124),
            sizeof(uintptr_t)))
        return FailContactRecreate(receipt, ContactRecreateNotWritable,
            ERROR_NOACCESS, 0xFFFFFFFFu, 18);
    for (uint32_t i = 0; i < sipReceipt.traversedCount; ++i)
        if (!Writable(reinterpret_cast<void*>(liveSip[i]),
                sizeof(uintptr_t)))
            return FailContactRecreate(receipt, ContactRecreateNotWritable,
                ERROR_NOACCESS, 0xFFFFFFFFu, 34);
    for (uint32_t i = 0; i < liveLargeCount; ++i)
        if (!Writable(reinterpret_cast<void*>(liveLarge[i]),
                sizeof(uintptr_t)))
            return FailContactRecreate(receipt, ContactRecreateNotWritable,
                ERROR_NOACCESS, 0xFFFFFFFFu, 19);

    CopyWords(g_contactRecreateScratchSip, liveSip,
        sipReceipt.traversedCount);
    if (targetSipCount) CopyWords(g_contactRecreateTargetSip, targetSip,
        targetSipCount);
    CopyWords(g_contactRecreateScratchContact, liveContact,
        liveContactCount);
    CopyWords(g_contactRecreateScratchLarge, liveLarge, liveLargeCount);
    CopyWords(g_contactRecreateTargetContact, targetContact,
        targetContactCount);
    if (targetLargeCount) CopyWords(g_contactRecreateTargetLarge,
        targetLarge, targetLargeCount);
    for (uint32_t i = 0; i < rowCount; ++i)
        g_contactRecreateRows[i] = rows[i];

    for (uint32_t i = 0; i < targetSipCount; ++i)
        g_contactRecreatePrimedSip[i] = targetSip[i];
    for (uint32_t i = 0; i < rowCount; ++i)
        g_contactRecreatePrimedSip[targetSipCount + i] = rows[i].targetSip;
    WriteManifoldOrder(expectedSipPool, g_contactRecreatePrimedSip,
        sipReceipt.traversedCount);
    for (uint32_t i = 0; i < targetContactCount; ++i)
        liveContact[i] = targetContact[i];
    for (uint32_t i = 0; i < rowCount; ++i)
        liveContact[targetContactCount + i] = rows[i].targetManager;
    for (uint32_t i = 0; i < rowCount; ++i)
        g_contactRecreatePrimedLarge[i] = rows[i].targetManifold;
    for (uint32_t i = 0; i < targetLargeCount; ++i)
        g_contactRecreatePrimedLarge[rowCount + i] = targetLarge[i];
    WriteManifoldOrder(expectedLargePool, g_contactRecreatePrimedLarge,
        liveLargeCount);
    MemoryBarrier();
    bool contactVerified = true;
    for (uint32_t i = 0; i < targetContactCount; ++i)
        if (liveContact[i] != targetContact[i]) contactVerified = false;
    for (uint32_t i = 0; i < rowCount; ++i)
        if (liveContact[targetContactCount + i] != rows[i].targetManager)
            contactVerified = false;
    bool sipVerified =
        *reinterpret_cast<const uintptr_t*>(expectedSipPool + 0x124) ==
            (targetSipCount ? targetSip[0] : rows[0].targetSip);
    if (!contactVerified || !sipVerified ||
        *reinterpret_cast<const uintptr_t*>(expectedLargePool + 0x124) !=
            rows[0].targetManifold) {
        WriteManifoldOrder(expectedSipPool, liveSip,
            sipReceipt.traversedCount);
        CopyWords(liveContact, g_contactRecreateScratchContact,
            liveContactCount);
        WriteManifoldOrder(expectedLargePool, liveLarge, liveLargeCount);
        return FailContactRecreate(receipt,
            ContactRecreateWriteVerificationFailed, ERROR_WRITE_FAULT,
            0xFFFFFFFFu, 20);
    }

    g_contactRecreateUnityBase = unityBase;
    g_contactRecreateContext = context;
    g_contactRecreateNPhaseCore = nphaseCore;
    g_contactRecreateSipPool = expectedSipPool;
    g_contactRecreateFreeArray = expectedFreeArray;
    g_contactRecreateLargePool = expectedLargePool;
    g_contactRecreateRowCount = rowCount;
    g_contactRecreateMatchedCount = 0;
    g_contactRecreateMatchedMask = 0;
    g_contactRecreateSipMatchedCount = 0;
    g_contactRecreateSipMatchedMask = 0;
    g_contactRecreateThreadId = 0;
    InterlockedExchange(&g_contactRecreateObserverEntries, 0);
    InterlockedExchange(&g_sipRecreateObserverEntries, 0);
    g_contactRecreateTargetSipCount = targetSipCount;
    g_contactRecreateTargetSipHash = OrderHash(targetSip, targetSipCount);
    g_contactRecreateTargetSipUsed = targetSipUsed;
    g_contactRecreateTargetSipUnreleased = targetSipUnreleased;
    g_contactRecreateContactCountBefore = liveContactCount;
    g_contactRecreateTargetContactCount = targetContactCount;
    g_contactRecreateContactHashBefore = OrderHash(
        g_contactRecreateScratchContact, liveContactCount);
    g_contactRecreateTargetContactHash = OrderHash(targetContact,
        targetContactCount);
    g_contactRecreateLargeCountBefore = liveLargeCount;
    g_contactRecreateTargetLargeCount = targetLargeCount;
    g_contactRecreateLargeHashBefore = OrderHash(liveLarge, liveLargeCount);
    g_contactRecreateTargetLargeHash = OrderHash(targetLarge,
        targetLargeCount);
    g_contactRecreateLargeUsedBefore = manifoldReceipt.used;
    g_contactRecreateTargetLargeUsed = targetLargeUsed;
    g_contactRecreateLargeUnreleasedBefore = manifoldReceipt.unreleased;
    g_contactRecreateTargetLargeUnreleased = targetLargeUnreleased;
    g_contactRecreateLastSip = 0;
    g_contactRecreateLastShapeLow = 0;
    g_contactRecreateLastShapeHigh = 0;
    g_contactRecreateLastManager = 0;
    g_contactRecreateLastManifold = 0;
    g_contactRecreateInvalidRow = 0xFFFFFFFFu;
    g_contactRecreateDetail = 0;
    g_contactRecreateAttemptOrdinal = 0;
    g_contactRecreateAttemptSip = 0;
    g_contactRecreateAttemptShapeLow = 0;
    g_contactRecreateAttemptShapeHigh = 0;
    g_contactRecreateAttemptRow = 0xFFFFFFFFu;
    g_contactRecreateAttemptMatchedMask = 0;
    g_contactRecreateAttemptFreeCount = 0xFFFFFFFFu;
    g_contactRecreateAttemptManagerIndex = 0xFFFFFFFFu;
    g_contactRecreateAttemptLargeHead = 0;
    g_contactRecreateAttemptManifoldIndex = 0xFFFFFFFFu;
    g_contactRecreateLastAllocatedSip = 0;
    g_sipRecreateAttemptRow = 0xFFFFFFFFu;
    InterlockedExchange(&g_contactRecreateResult, ContactRecreateOk);
    InterlockedExchange(&g_contactRecreateError, ERROR_SUCCESS);
    InterlockedExchange(&g_contactRecreateState, ContactRecreateArmed);
    InitializeContactRecreateReceipt(receipt, unityBase, context);
    receipt->result = ContactRecreateOk;
    return 1;
}

static int ReadContactRecreateStatus(uintptr_t unityBase, uintptr_t context,
    ContactRecreateReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactRecreateReceipt(receipt, unityBase, context);
    if (g_contactRecreateState == ContactRecreateIdle ||
        unityBase != g_contactRecreateUnityBase ||
        context != g_contactRecreateContext)
        return FailContactRecreate(receipt, ContactRecreateBadArgument,
            ERROR_INVALID_STATE, 0xFFFFFFFFu, 21);
    if (g_contactRecreateState == ContactRecreateArmed ||
        g_contactRecreateState == ContactRecreateComplete) {
        while (InterlockedCompareExchange(&g_sipRecreateHookLock, 1, 0) != 0)
            SwitchToThread();
        while (InterlockedCompareExchange(&g_contactRecreateHookLock, 1, 0) != 0)
            SwitchToThread();
        const bool sipReconciled = ReconcileReturnedSipRecreateRows();
        const bool contactReconciled =
            ReconcileReturnedContactRecreateRows(context);
        ReleaseContactRecreateHookLock();
        ReleaseSipRecreateHookLock();
        if (!sipReconciled || !contactReconciled)
            return FailContactRecreate(receipt,
                ContactRecreateMetadataMismatch, ERROR_INVALID_STATE,
                0xFFFFFFFFu, 24);
        InitializeContactRecreateReceipt(receipt, unityBase, context);
    }
    if (Readable(reinterpret_cast<const void*>(context + 0x2C8), 8) &&
        *reinterpret_cast<const uintptr_t*>(context + 0x2C8) ==
            g_contactRecreateFreeArray) {
        receipt->contactCountCurrent = *reinterpret_cast<const uint32_t*>(
            context + 0x2CC);
        if (receipt->contactCountCurrent <= kMaximumContactManagers &&
            Readable(reinterpret_cast<const void*>(
                g_contactRecreateFreeArray),
                receipt->contactCountCurrent * sizeof(uintptr_t)))
            receipt->contactHashCurrent = OrderHash(
                reinterpret_cast<const uintptr_t*>(
                    g_contactRecreateFreeArray),
                receipt->contactCountCurrent);
    }
    ManifoldPoolReceipt manifoldReceipt = {};
    uintptr_t order[kMaximumManifolds] = {};
    uint32_t count = 0;
    InitializeManifoldPoolReceipt(&manifoldReceipt, unityBase, context, 0);
    if (ReadManifoldPool(context, 0, order, count, &manifoldReceipt)) {
        receipt->largeCountCurrent = count;
        receipt->largeHashCurrent = OrderHash(order, count);
        receipt->largeUsedCurrent = manifoldReceipt.used;
        receipt->largeUnreleasedCurrent = manifoldReceipt.unreleased;
    }
    return receipt->result == ContactRecreateOk ? 1 : 0;
}

static int CancelContactRecreate(uintptr_t unityBase,
    ContactRecreateReceipt* receipt) {
    if (!receipt) return 0;
    InitializeContactRecreateReceipt(receipt, unityBase,
        g_contactRecreateContext);
    // Only a plan that never entered the pass-through hook is reversible.
    // matchedCount is insufficient because a rejected callback still returns
    // into shipped createContactManager and can consume the primed tops.
    if ((g_contactRecreateState == ContactRecreateArmed ||
            g_contactRecreateState == ContactRecreatePoisoned) &&
        g_contactRecreateObserverEntries == 0 &&
        g_sipRecreateObserverEntries == 0 &&
        g_contactRecreateFreeArray && g_contactRecreateLargePool &&
        g_contactRecreateSipPool) {
        uintptr_t* freeArray = reinterpret_cast<uintptr_t*>(
            g_contactRecreateFreeArray);
        if (Writable(freeArray, g_contactRecreateContactCountBefore *
                sizeof(uintptr_t)))
            CopyWords(freeArray, g_contactRecreateScratchContact,
                g_contactRecreateContactCountBefore);
        WriteManifoldOrder(g_contactRecreateLargePool,
            g_contactRecreateScratchLarge,
            g_contactRecreateLargeCountBefore);
        WriteManifoldOrder(g_contactRecreateSipPool,
            g_contactRecreateScratchSip,
            g_contactRecreateTargetSipCount + g_contactRecreateRowCount);
    }
    ClearContactRecreateState();
    InitializeContactRecreateReceipt(receipt, unityBase, 0);
    receipt->result = ContactRecreateOk;
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

extern "C" __declspec(dllexport) int __cdecl oc2_island_capture_snapshot_v1(
    uintptr_t unityBase, uintptr_t nphaseCore, uint32_t expectedPhase,
    const IslandSnapshotBuffersV1* buffers,
    IslandSnapshotReceiptV1* receipt) {
    return CaptureIslandSnapshot(unityBase, nphaseCore, expectedPhase,
        buffers, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_island_update_observer_install(uintptr_t unityBase,
    uintptr_t expectedManager, IslandUpdateObserverReceipt* receipt) {
    return InstallIslandObserver(unityBase, expectedManager, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_island_update_observer_status(uintptr_t unityBase,
    IslandUpdateObserverReceipt* receipt) {
    return StatusIslandObserver(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_island_update_observer_arm(uintptr_t unityBase,
    uintptr_t expectedManager, uintptr_t expectedNphase,
    uint32_t expectedPass, const IslandSnapshotBuffersV1* preBuffers,
    IslandSnapshotReceiptV1* preReceipt,
    const IslandSnapshotBuffersV1* postBuffers,
    IslandSnapshotReceiptV1* postReceipt,
    IslandUpdateObserverReceipt* receipt) {
    return ArmIslandObserver(unityBase, expectedManager, expectedNphase,
        expectedPass, preBuffers, preReceipt, postBuffers, postReceipt,
        receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_island_update_observer_copy(uintptr_t unityBase,
    uint32_t observationOrdinal,
    const IslandSnapshotBuffersV1* preBuffers,
    IslandSnapshotReceiptV1* preReceipt,
    const IslandSnapshotBuffersV1* postBuffers,
    IslandSnapshotReceiptV1* postReceipt,
    IslandUpdateObserverReceipt* receipt) {
    return CopyIslandObservation(unityBase, observationOrdinal, preBuffers,
        preReceipt, postBuffers, postReceipt, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_island_update_observer_cancel(uintptr_t unityBase,
    IslandUpdateObserverReceipt* receipt) {
    return CancelIslandObserver(unityBase, false, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_island_update_observer_uninstall(uintptr_t unityBase,
    IslandUpdateObserverReceipt* receipt) {
    return CancelIslandObserver(unityBase, true, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_island_edge_journal_copy(
    uintptr_t unityBase, uint32_t beginOrdinal,
    IslandEdgeJournalRecord* records, uint32_t capacity,
    IslandEdgeJournalReceipt* receipt) {
    return CopyIslandJournal(unityBase, beginOrdinal, records, capacity,
        receipt);
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

extern "C" __declspec(dllexport) int __cdecl oc2_contact_manager_active_owners(
    uintptr_t unityBase, uintptr_t context,
    ContactManagerOwnerRecord* records, uint32_t capacity,
    ContactManagerOwnerReceipt* receipt) {
    return CaptureContactManagerActiveOwners(unityBase, context, records,
        capacity, receipt);
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

extern "C" __declspec(dllexport) int __cdecl
oc2_shape_instance_pair_pool_capture_snapshot(
    uintptr_t unityBase, uintptr_t nphaseCore, uintptr_t* snapshot,
    uint32_t capacity, SipPoolReceipt* receipt) {
    return CaptureShapeInstancePairPool(unityBase, nphaseCore, snapshot,
        capacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_actor_pair_pool_capture_snapshot(
    uintptr_t unityBase, uintptr_t nphaseCore, uintptr_t* freeSnapshot,
    uint32_t freeCapacity, uintptr_t* allocatedSnapshot,
    uint32_t allocatedCapacity, ActorPairPoolReceipt* receipt) {
    return CaptureActorPairPool(unityBase, nphaseCore, freeSnapshot,
        freeCapacity, allocatedSnapshot, allocatedCapacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_actor_pair_report_pool_capture_snapshot(
    uintptr_t unityBase, uintptr_t nphaseCore, uintptr_t* freeSnapshot,
    uint32_t freeCapacity, uintptr_t* allocatedSnapshot,
    uint32_t allocatedCapacity, ActorPairReportPoolReceipt* receipt) {
    return CaptureActorPairReportPool(unityBase, nphaseCore, freeSnapshot,
        freeCapacity, allocatedSnapshot, allocatedCapacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_nphase_report_state_capture_snapshot(
    uintptr_t unityBase, uintptr_t nphaseCore, uintptr_t* actorPairs,
    uint32_t actorPairCapacity, uintptr_t* persistentSips,
    uint32_t persistentCapacity, uintptr_t* forceThresholdSips,
    uint32_t forceThresholdCapacity, uint8_t* reportBufferBytes,
    uint32_t reportBufferCapacity, NPhaseReportStateReceipt* receipt) {
    return CaptureNPhaseReportState(unityBase, nphaseCore, actorPairs,
        actorPairCapacity, persistentSips, persistentCapacity,
        forceThresholdSips, forceThresholdCapacity, reportBufferBytes,
        reportBufferCapacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_interaction_graph_capture_snapshot(
    uintptr_t unityBase, uintptr_t nphaseCore,
    uintptr_t* activeBodies, uint32_t activeBodyCapacity,
    InteractionGraphActorRecord* actors, uint32_t actorCapacity,
    InteractionGraphInteractionRecord* interactions,
    uint32_t interactionCapacity, uintptr_t* actorSlots,
    uint32_t actorSlotCapacity, uintptr_t* poolSlabs,
    uint32_t poolSlabCapacity, uintptr_t* poolFree,
    uint32_t poolFreeCapacity, InteractionGraphReceipt* receipt) {
    return CaptureInteractionGraph(unityBase, nphaseCore,
        activeBodies, activeBodyCapacity, actors, actorCapacity,
        interactions, interactionCapacity, actorSlots, actorSlotCapacity,
        poolSlabs, poolSlabCapacity, poolFree, poolFreeCapacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_transform_cache_capture_snapshot(
    uintptr_t unityBase, uintptr_t nphaseCore,
    TransformCacheEntryRecord* entries, uint32_t entryCapacity,
    uint32_t* freeIds, uint32_t freeCapacity,
    TransformCacheBindingRecord* bindings, uint32_t bindingCapacity,
    TransformCacheReceipt* receipt) {
    return CaptureTransformCache(unityBase, nphaseCore, entries,
        entryCapacity, freeIds, freeCapacity, bindings, bindingCapacity,
        receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_finish_broad_phase_observer_install(
    uintptr_t unityBase, FinishBroadPhaseObserverReceipt* receipt) {
    return InstallFinishBroadPhaseObserver(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_finish_broad_phase_observer_status(
    uintptr_t unityBase, FinishBroadPhaseObserverReceipt* receipt) {
    return ReadFinishBroadPhaseObserverStatus(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_finish_broad_phase_observer_arm(
    uintptr_t unityBase, uintptr_t expectedScene,
    uintptr_t expectedContext, uintptr_t expectedNPhaseCore,
    uint32_t expectedPass, FinishBroadPhaseObserverReceipt* receipt) {
    return ArmFinishBroadPhaseObserver(unityBase, expectedScene,
        expectedContext, expectedNPhaseCore, expectedPass, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_finish_broad_phase_observer_copy(
    uintptr_t unityBase, uint32_t observationOrdinal,
    BroadPhaseOverlapRecord* created, uint32_t createdCapacity,
    BroadPhaseOverlapRecord* deleted, uint32_t deletedCapacity,
    FinishBroadPhaseObserverReceipt* receipt) {
    return CopyFinishBroadPhaseObservation(unityBase, observationOrdinal,
        created, createdCapacity, deleted, deletedCapacity, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_finish_broad_phase_observer_cancel(
    uintptr_t unityBase, FinishBroadPhaseObserverReceipt* receipt) {
    return CancelFinishBroadPhaseObserver(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl
oc2_finish_broad_phase_observer_uninstall(
    uintptr_t unityBase, FinishBroadPhaseObserverReceipt* receipt) {
    return UninstallFinishBroadPhaseObserver(unityBase, receipt);
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

extern "C" __declspec(dllexport) uint32_t __cdecl oc2_contact_recreate_api_version() {
    return 2;
}

extern "C" __declspec(dllexport) uint32_t __cdecl
oc2_contact_recreate_audit_api_version() {
    return 1;
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_recreate_audit(
    uintptr_t unityBase, uintptr_t context, uintptr_t nphaseCore,
    uintptr_t expectedSipPool, const uintptr_t* targetSip,
    uint32_t targetSipCount, uint32_t targetSipUsed,
    uint32_t targetSipUnreleased, uintptr_t expectedFreeArray,
    const uintptr_t* targetContact, uint32_t targetContactCount,
    uintptr_t expectedLargePool, const uintptr_t* targetLarge,
    uint32_t targetLargeCount, uint32_t targetLargeUsed,
    uint32_t targetLargeUnreleased, const ContactRecreatePlanRow* rows,
    uint32_t rowCount, ContactRecreateAuditReceipt* receipt) {
    return AuditContactRecreate(unityBase, context, nphaseCore,
        expectedSipPool, targetSip, targetSipCount, targetSipUsed,
        targetSipUnreleased, expectedFreeArray, targetContact,
        targetContactCount, expectedLargePool, targetLarge,
        targetLargeCount, targetLargeUsed, targetLargeUnreleased, rows,
        rowCount, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_recreate_arm(
    uintptr_t unityBase, uintptr_t context, uintptr_t nphaseCore,
    uintptr_t expectedSipPool, const uintptr_t* targetSip,
    uint32_t targetSipCount, uint32_t targetSipUsed,
    uint32_t targetSipUnreleased, uintptr_t expectedFreeArray,
    const uintptr_t* targetContact, uint32_t targetContactCount,
    uintptr_t expectedLargePool, const uintptr_t* targetLarge,
    uint32_t targetLargeCount, uint32_t targetLargeUsed,
    uint32_t targetLargeUnreleased, const ContactRecreatePlanRow* rows,
    uint32_t rowCount, ContactRecreateReceipt* receipt) {
    return ArmContactRecreate(unityBase, context, nphaseCore,
        expectedSipPool, targetSip, targetSipCount, targetSipUsed,
        targetSipUnreleased, expectedFreeArray,
        targetContact, targetContactCount, expectedLargePool, targetLarge,
        targetLargeCount, targetLargeUsed, targetLargeUnreleased, rows,
        rowCount, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_recreate_status(
    uintptr_t unityBase, uintptr_t context,
    ContactRecreateReceipt* receipt) {
    return ReadContactRecreateStatus(unityBase, context, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_contact_recreate_cancel(
    uintptr_t unityBase, ContactRecreateReceipt* receipt) {
    return CancelContactRecreate(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_install(
    uintptr_t unityBase, DirtyInteractionOrderReceipt* receipt) {
    return InstallDirtyInteractionOrderHook(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_status(
    uintptr_t unityBase, DirtyInteractionOrderReceipt* receipt) {
    return ReadDirtyInteractionOrderStatus(unityBase, receipt);
}

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_last_nphase(
    uintptr_t unityBase, uintptr_t* nphaseCore, uint32_t* observations) {
    return ReadLastObservedNPhaseCore(unityBase, nphaseCore, observations);
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

extern "C" __declspec(dllexport) int __cdecl oc2_dirty_interaction_order_capture_snapshot(
    uintptr_t unityBase, uintptr_t nphaseCore, DirtyInteractionKey* keys,
    uint32_t capacity, DirtyInteractionOrderReceipt* receipt) {
    return CaptureDirtyInteractionOrderSnapshot(unityBase, nphaseCore, keys,
        capacity, receipt);
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
