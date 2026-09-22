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

struct NPhasePoolSnapshotBuffersV1 {
    uintptr_t* slabBases; uint32_t slabBaseCapacity;
    uint32_t* freeSlots; uint32_t freeSlotCapacity;
    uint32_t* allocationWords; uint32_t allocationWordCapacity;
    uint8_t* slabBytes; uint32_t slabByteCapacity;
};

struct NPhasePoolSnapshotReceiptV1 {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, pool, slabsData, freeHead;
    uint32_t poolKind, poolOffset, elementSize, elementsPerSlab, slabSize,
        slabCount, slabCapacityRaw, totalSlots, used, unreleased,
        freeHeadSlot;
    uint32_t slabBasesRequired, slabBasesWritten, freeSlotsRequired,
        freeSlotsWritten, allocationWordsRequired, allocationWordsWritten,
        slabBytesRequired, slabBytesWritten;
    uint32_t metadataHash, slabBaseHash, freeSlotOrderHash,
        allocationBitmapHash, slabByteHash, snapshotHash, validationFlags,
        invalidKind, invalidIndex, detail;
};

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

struct NPhaseReportSnapshotBuffersV1 {
    uintptr_t* actorPairBacking;
    uint32_t actorPairCapacity;
    uintptr_t* persistentBacking;
    uint32_t persistentCapacity;
    uintptr_t* forceThresholdBacking;
    uint32_t forceThresholdCapacity;
    uint8_t* reportBufferBytes;
    uint32_t reportBufferByteCapacity;
};

struct NPhaseReportArrayReceiptV1 {
    uintptr_t data;
    uint32_t count, capacityRaw, backingRequired, backingWritten;
    uint32_t logicalOrderHash, backingHash;
};

struct NPhaseReportSnapshotReceiptV1 {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, ownerScene;
    uint32_t sceneTimeStamp, sceneReportShapePairTimeStamp;
    NPhaseReportArrayReceiptV1 actorPairs, persistent;
    uint32_t nextFramePersistentIndex;
    NPhaseReportArrayReceiptV1 forceThreshold;
    uintptr_t reportBuffer;
    uint32_t reportBufferCurrentIndex, reportBufferCurrentSize;
    uint32_t reportBufferDefaultSize, reportBufferLastIndex;
    uint32_t reportBufferAllocationLocked;
    uint32_t reportBufferRequired, reportBufferWritten;
    uint32_t reportBufferActiveHash, reportBufferAllocationHash;
    uint32_t metadataHash, snapshotHash, validationFlags;
    uint32_t invalidKind, invalidIndex, detail;
};

struct InteractionGraphActorRecord {
    uintptr_t actor, vtable, inlineSlots[4], interactionsData, firstElement,
        interactionScene;
    uint32_t sceneArrayIndex, interactionOutputStart, interactionCount,
        interactionCapacity, activeBodyIndex, interactionOrderHash;
    uint16_t transferringCount, uniqueCount, countedCount;
    uint8_t actorType, islandNodeInfo;
    uint32_t validationFlags;
};

struct InteractionGraphInteractionRecord {
    uintptr_t interaction, vtable, actor0, actor1, element0, element1,
        shapeCore0, shapeCore1, pxsShapeCore0, pxsShapeCore1,
        semanticLow, semanticHigh;
    uint32_t sceneId, globalIndex, active;
    uint16_t actorId0, actorId1;
    uint8_t interactionType, interactionFlags;
    uint16_t reserved;
    uint32_t validationFlags;
};

struct InteractionGraphPoolReceipt {
    uintptr_t pool, slabData, freeHead;
    uint32_t blockCapacity, blockBytes, inlineBufferUsed, slabCount,
        slabCapacityRaw, elementsPerSlab, used, unreleasedFree, slabSize,
        totalElements, freeCount, slabOutputStart, freeOutputStart,
        slabOrderHash, freeOrderHash, usedOwnerHash, validationFlags;
};

struct InteractionGraphReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, ownerScene, interactionScene, llContext;
    uint32_t timestamp;
    uintptr_t activeBodiesData;
    uint32_t activeBodiesCount, activeBodiesCapacityRaw, activeTwoWayStart;
    uintptr_t globalData[6];
    uint32_t globalCount[6], globalCapacityRaw[6], globalActiveCount[6],
        globalOrderHash[6];
    uint32_t activeBodiesRequired, activeBodiesWritten, actorsRequired,
        actorsWritten, interactionsRequired, interactionsWritten,
        actorSlotsRequired, actorSlotsWritten, poolSlabsRequired,
        poolSlabsWritten, poolFreeRequired, poolFreeWritten, actorHash,
        interactionHash, actorSlotHash, poolHash, graphHash, validationFlags,
        invalidKind, invalidIndex, detail;
    InteractionGraphPoolReceipt pools[3];
};

struct TransformCacheEntryRecord {
    uint32_t id, refCount;
    float rotation[4], position[3];
    uint32_t poseHash, bindingCount, stateFlags;
};

struct TransformCacheBindingRecord {
    uintptr_t shapeSim, shapeCore, pxsShapeCore, interaction;
    uint32_t interactionIndex, endpointIndex, cacheId, refCount, poseHash,
        validationFlags;
};

struct TransformCacheReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, nphaseCore, ownerScene, interactionScene, context,
        transformCache;
    uint32_t currentId;
    uintptr_t freeData;
    uint32_t freeCount, freeCapacityRaw;
    uintptr_t transformsData;
    uint32_t transformsCount, transformsCapacityRaw;
    uintptr_t refCountsData;
    uint32_t refCountsCount, refCountsCapacityRaw;
    uint32_t entriesRequired, entriesWritten, freeRequired, freeWritten,
        bindingsRequired, bindingsWritten, liveCount, totalRefCount,
        entryHash, freeOrderHash, bindingHash, snapshotHash, validationFlags,
        invalidKind, invalidIndex, detail;
};

struct BroadPhaseOverlapRecord {
    uintptr_t userData0, userData1, shapeCore0, shapeCore1, pxsShapeCore0,
        pxsShapeCore1;
    uint32_t cacheId0, cacheId1, pairHash, validationFlags;
};

struct FinishBroadPhaseObserverReceipt {
    uint32_t apiVersion, structSize, result, lastError;
    uintptr_t unityBase, expectedScene, expectedContext, expectedNPhaseCore,
        observedScene, observedContext, observedNPhaseCore, aabbManager,
        interactionScene, transformCache;
    uint32_t installed, state, expectedPass, armedThreadId, armedOrdinal,
        observationOrdinal, slotIndex, pass, threadId, createdRequired,
        createdWritten, deletedRequired, deletedWritten, createdHash,
        deletedHash, preCacheHash, postCacheHash, preGraphHash, postGraphHash,
        validationFlags, invalidKind, invalidIndex, detail,
        droppedObservations;
};

#pragma pack(push, 4)
struct IslandNodeSlotRecord {
    uint32_t id, ownerOrArticulationRaw, islandId, rawFlagsWord, freeNext,
        nextNode, slotFlags, validationFlags;
};
struct IslandEdgeSlotRecord {
    uint32_t id, node0, node1, taggedObjectRaw, freeNext, nextEdge, slotFlags,
        semanticBindingIndex, validationFlags;
};
struct IslandSlotRecord {
    uint32_t id, startNode, startEdge, endNode, endEdge, freeNext, slotFlags,
        validationFlags;
};
struct IslandArticulationRootSlotRecord {
    uint32_t id, articulationLinkHandle, articulationOwner, freeNext,
        slotFlags, validationFlags;
};
struct IslandSipEdgeBinding {
    uint32_t edgeId, edgeType, sip, hookAddress, shapeSim0, shapeSim1,
        pxsShapeCoreLow, pxsShapeCoreHigh, contactManager, taggedObjectRaw,
        validationFlags;
};
struct IslandEdgeJournalRecord {
    uint32_t ordinal, eventKind, observerPhase, threadId, edgeType, node0,
        node1, preEdgeId, postEdgeId, hookAddress, ownerObject,
        pxsShapeCoreLow, pxsShapeCoreHigh, validationFlags;
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
    uint32_t vtable, elements, freeNext, nextList, capacity, freeHead,
        freeCount, required, written, elementHash, freeChainHash, nextHash;
};
struct IslandQueueReceipt {
    uint32_t data, count, capacity, defaultCapacity, required, written, hash;
};
struct IslandBitmapReceipt {
    uint32_t data, wordCount, required, written, hash;
};
struct IslandSnapshotReceiptV1 {
    uint32_t apiVersion, structSize, result, lastError, unityBase, nphaseCore,
        ownerScene, interactionScene, context, islandManager, phase,
        observerSequence, observationOrdinal, captureThreadId, epoch;
    IslandElementManagerReceipt node, edge, island, root;
    IslandQueueReceipt nodeCreated, nodeDeleted, edgeCreated, edgeDeleted,
        edgeBroken, edgeJoined;
    IslandBitmapReceipt kinematic, kinematicChange, notReady, notReadyChange,
        islandBitmap;
    uint32_t numAddedRBodies, numAddedArtics, numAddedKinematics,
        numAddedEdgesContact, numAddedEdgesConstraint,
        numAddedEdgesArticulation, numEdgeReferencesToKinematic,
        numRequiredKinematicDuplicates, everythingAsleep, hasAnythingChanged,
        performIslandUpdate, liveContactEdges, liveConstraintEdges,
        liveArticulationEdges, bindingsRequired, bindingsWritten, bindingHash,
        journalBeginOrdinal, journalEndOrdinal, journalOverflowCount,
        snapshotHash, validationFlags, invalidKind, invalidIndex, detail;
};
struct IslandUpdateObserverReceipt {
    uint32_t apiVersion, structSize, result, lastError, unityBase,
        expectedManager, expectedContext, expectedNphase, observedManager,
        observedContext, observedNphase, installed, state, expectedPass,
        armedThreadId, observerSequence, armedOrdinal, observationOrdinal,
        slotIndex, pass, threadId, preResult, postResult, preSnapshotHash,
        postSnapshotHash, journalBeginOrdinal, journalEndOrdinal,
        validationFlags, invalidKind, invalidIndex, detail, inFlight;
};
struct IslandEdgeJournalReceipt {
    uint32_t apiVersion, structSize, result, lastError, unityBase,
        expectedManager, installed, state, firstOrdinal, nextOrdinal,
        requestedBegin, recordsRequired, recordsWritten, overflowCount,
        addCount, removeCount, recordHash, validationFlags, invalidKind,
        invalidIndex, detail;
};
struct IslandNodeRebindV1 {
    uint32_t targetNodeId, targetOwnerRaw, targetBodyCore,
        liveBodySim, liveBodyCore,
        liveHookAddress, currentNodeId, semanticKey, validationFlags;
};
struct IslandContactEdgeRebindV1 {
    uint32_t targetBindingIndex, targetEdgeId, targetPxsShapeCoreLow,
        targetPxsShapeCoreHigh, liveSip, liveHookAddress, liveShapeSim0,
        liveShapeSim1, livePxsShapeCoreLow, livePxsShapeCoreHigh,
        liveContactManager, currentEdgeId, semanticKey, validationFlags;
};
struct IslandRestoreRequestV1 {
    uint32_t apiVersion, structSize, flags, expectedThreadId,
        expectedManager, expectedContext;
    const IslandSnapshotBuffersV1* targetBuffers;
    const IslandSnapshotReceiptV1* targetReceipt;
    const IslandNodeRebindV1* nodeRebinds;
    uint32_t nodeRebindCount;
    const IslandContactEdgeRebindV1* edgeRebinds;
    uint32_t edgeRebindCount;
    const IslandSnapshotBuffersV1* rollbackBuffers;
    IslandSnapshotReceiptV1* rollbackReceipt;
    const IslandSnapshotBuffersV1* verifyBuffers;
    IslandSnapshotReceiptV1* verifyReceipt;
};
struct IslandRestoreReceiptV1 {
    uint32_t apiVersion, structSize, result, lastError, unityBase, nphaseCore,
        ownerScene, interactionScene, context, islandManager, threadId, stage,
        requestFlags, observerStateBefore, observerStateAfter, epochBefore,
        epochAfter, inFlightBefore, inFlightAfter, sceneFlagsBefore,
        sceneFlagsAfter, journalOrdinalBefore, journalOrdinalAfter,
        targetRawHash, targetRebasedHash, beforeHash, afterHash, rollbackHash,
        nodeBindingsRequired, nodeBindingsValidated, nodeHooksWritten,
        edgeBindingsRequired, edgeBindingsValidated, edgeHooksWritten,
        bytesWritten, mutationStarted, mutationCommitted, rollbackAttempted,
        rollbackSucceeded, failStopped, validationFlags, invalidKind,
        invalidIndex, detail;
};
#pragma pack(pop)
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
static_assert(sizeof(NPhasePoolSnapshotBuffersV1) == 32,
    "Unexpected Win32 NPhase-pool snapshot buffer ABI");
static_assert(sizeof(NPhasePoolSnapshotReceiptV1) == 152,
    "Unexpected Win32 NPhase-pool snapshot receipt ABI");
static_assert(sizeof(NPhaseReportStateReceipt) == 116,
    "Unexpected Win32 NPhase report-state receipt ABI");
static_assert(sizeof(NPhaseReportSnapshotBuffersV1) == 32,
    "Unexpected Win32 complete NPhase report buffer ABI");
static_assert(sizeof(NPhaseReportArrayReceiptV1) == 28,
    "Unexpected Win32 complete NPhase report array ABI");
static_assert(sizeof(NPhaseReportSnapshotReceiptV1) == 188,
    "Unexpected Win32 complete NPhase report receipt ABI");
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
    "Unexpected Win32 finishBroadPhase receipt ABI");
static_assert(sizeof(IslandNodeSlotRecord) == 32, "island node ABI");
static_assert(sizeof(IslandEdgeSlotRecord) == 36, "island edge ABI");
static_assert(sizeof(IslandSlotRecord) == 32, "island ABI");
static_assert(sizeof(IslandArticulationRootSlotRecord) == 24,
    "island root ABI");
static_assert(sizeof(IslandSipEdgeBinding) == 44, "island binding ABI");
static_assert(sizeof(IslandEdgeJournalRecord) == 56, "island journal ABI");
static_assert(sizeof(IslandSnapshotBuffersV1) == 128,
    "island buffer ABI");
static_assert(sizeof(IslandSnapshotReceiptV1) == 620,
    "island snapshot ABI");
static_assert(sizeof(IslandUpdateObserverReceipt) == 128,
    "island observer ABI");
static_assert(sizeof(IslandEdgeJournalReceipt) == 84,
    "island journal receipt ABI");
static_assert(sizeof(IslandNodeRebindV1) == 36,
    "island node-rebind ABI");
static_assert(sizeof(IslandContactEdgeRebindV1) == 56,
    "island contact-edge-rebind ABI");
static_assert(sizeof(IslandRestoreRequestV1) == 64,
    "island restore-request ABI");
static_assert(sizeof(IslandRestoreReceiptV1) == 176,
    "island restore-receipt ABI");

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
typedef int (__cdecl *DirtyCaptureSnapshot)(uintptr_t, uintptr_t,
    DirtyInteractionKey*, uint32_t, DirtyInteractionOrderReceipt*);
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
typedef int (__cdecl *CaptureNPhasePoolSnapshotV1)(uintptr_t, uintptr_t,
    uint32_t, const NPhasePoolSnapshotBuffersV1*,
    NPhasePoolSnapshotReceiptV1*);
typedef int (__cdecl *CaptureNPhaseReportState)(uintptr_t, uintptr_t,
    uintptr_t*, uint32_t, uintptr_t*, uint32_t, uintptr_t*, uint32_t,
    uint8_t*, uint32_t, NPhaseReportStateReceipt*);
typedef int (__cdecl *CaptureNPhaseReportSnapshotV1)(uintptr_t, uintptr_t,
    const NPhaseReportSnapshotBuffersV1*,
    NPhaseReportSnapshotReceiptV1*);
typedef int (__cdecl *CaptureInteractionGraph)(uintptr_t, uintptr_t,
    uintptr_t*, uint32_t, InteractionGraphActorRecord*, uint32_t,
    InteractionGraphInteractionRecord*, uint32_t, uintptr_t*, uint32_t,
    uintptr_t*, uint32_t, uintptr_t*, uint32_t, InteractionGraphReceipt*);
typedef int (__cdecl *CaptureTransformCache)(uintptr_t, uintptr_t,
    TransformCacheEntryRecord*, uint32_t, uint32_t*, uint32_t,
    TransformCacheBindingRecord*, uint32_t, TransformCacheReceipt*);
typedef int (__cdecl *FinishBroadPhaseAction)(uintptr_t,
    FinishBroadPhaseObserverReceipt*);
typedef int (__cdecl *FinishBroadPhaseArm)(uintptr_t, uintptr_t, uintptr_t,
    uintptr_t, uint32_t, FinishBroadPhaseObserverReceipt*);
typedef int (__cdecl *FinishBroadPhaseCopy)(uintptr_t, uint32_t,
    BroadPhaseOverlapRecord*, uint32_t, BroadPhaseOverlapRecord*, uint32_t,
    FinishBroadPhaseObserverReceipt*);
typedef void (__thiscall *FakeFinishBroadPhase)(void*, uint32_t);
typedef int (__cdecl *CaptureIslandSnapshot)(uintptr_t, uintptr_t, uint32_t,
    const IslandSnapshotBuffersV1*, IslandSnapshotReceiptV1*);
typedef int (__cdecl *RestoreIslandSnapshot)(uintptr_t, uintptr_t,
    const IslandRestoreRequestV1*, IslandRestoreReceiptV1*);
typedef int (__cdecl *IslandInstall)(uintptr_t, uintptr_t,
    IslandUpdateObserverReceipt*);
typedef int (__cdecl *IslandStatus)(uintptr_t,
    IslandUpdateObserverReceipt*);
typedef int (__cdecl *IslandArm)(uintptr_t, uintptr_t, uintptr_t, uint32_t,
    const IslandSnapshotBuffersV1*, IslandSnapshotReceiptV1*,
    const IslandSnapshotBuffersV1*, IslandSnapshotReceiptV1*,
    IslandUpdateObserverReceipt*);
typedef int (__cdecl *IslandCopy)(uintptr_t, uint32_t,
    const IslandSnapshotBuffersV1*, IslandSnapshotReceiptV1*,
    const IslandSnapshotBuffersV1*, IslandSnapshotReceiptV1*,
    IslandUpdateObserverReceipt*);
typedef int (__cdecl *IslandAction)(uintptr_t,
    IslandUpdateObserverReceipt*);
typedef int (__cdecl *IslandJournalCopy)(uintptr_t, uint32_t,
    IslandEdgeJournalRecord*, uint32_t, IslandEdgeJournalReceipt*);
typedef void (__thiscall *FakeIslandAddEdge)(void*, uint32_t, uint32_t,
    uint32_t, uint32_t*);
typedef void (__thiscall *FakeIslandRemoveEdge)(void*, uint32_t, uint32_t*);
typedef void (__thiscall *FakeIslandUpdate)(void*, void*, uint32_t);
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

struct FinishBroadPhaseWorkerCall {
    FakeFinishBroadPhase finish;
    void* scene;
    uint32_t pass;
    DWORD threadId;
};

static DWORD WINAPI RunFinishBroadPhaseWorker(void* value) {
    FinishBroadPhaseWorkerCall* call =
        static_cast<FinishBroadPhaseWorkerCall*>(value);
    call->threadId = GetCurrentThreadId();
    call->finish(call->scene, call->pass);
    return 0;
}

struct IslandUpdateWorkerCall {
    FakeIslandUpdate update;
    void* manager;
    void* task;
    uint32_t pass;
    DWORD threadId;
};

static DWORD WINAPI RunIslandUpdateWorker(void* value) {
    IslandUpdateWorkerCall* call =
        static_cast<IslandUpdateWorkerCall*>(value);
    call->threadId = GetCurrentThreadId();
    call->update(call->manager, call->task, call->pass);
    return 0;
}

struct IslandAddWorkerCall {
    FakeIslandAddEdge add;
    void* manager;
    uint32_t edgeType;
    uint32_t node0;
    uint32_t node1;
    uint32_t* hook;
};

static DWORD WINAPI RunIslandAddWorker(void* value) {
    IslandAddWorkerCall* call = static_cast<IslandAddWorkerCall*>(value);
    call->add(call->manager, call->edgeType, call->node0, call->node1,
        call->hook);
    return 0;
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

static uint32_t TestAppendByteHash(uint32_t hash, const void* value,
    uint32_t count) {
    const uint8_t* bytes = static_cast<const uint8_t*>(value);
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= bytes[i];
        hash *= 16777619u;
    }
    return hash;
}

static uint32_t TestByteHash(const void* value, uint32_t count) {
    return TestAppendByteHash(2166136261u, value, count);
}

static uint32_t TestPointerOrderHash(const uintptr_t* values,
    uint32_t count) {
    uint32_t hash = 2166136261u;
    for (uint32_t i = 0; i < count; ++i) {
        hash ^= static_cast<uint32_t>(values[i]);
        hash *= 16777619u;
    }
    return hash;
}

static const uint32_t kLargePoolAllocatorRva = 0xA69A90;
static const uint32_t kCurrentApiVersion = 21u;
static const uint32_t kSpherePoolAllocatorRva = 0xA69AC0;
static const uint32_t kLargePoolSlabRva = 0xA69BEA;
static const uint32_t kSpherePoolSlabRva = 0xA69CCA;
static const uint32_t kLargePoolCallsiteRva = 0xA69F15;
static const uint32_t kSpherePoolCallsiteRva = 0xA69F32;
static const uint32_t kDirtyUpdateRva = 0xA540F0;
static const uint32_t kCreateManagerRva = 0xA69E80;
static const uint32_t kCreateShapeInstancePairRva = 0xA4E560;
static const uint32_t kCreateMarkerPoolLayoutRva = 0xA4E525;
static const uint32_t kCreateTriggerPoolLayoutRva = 0xA4E66A;
static const uint32_t kFindActorPairRva = 0xA4F7B0;
static const uint32_t kActorPairSlabRva = 0xA4D9BA;
static const uint32_t kCreateActorPairReportDataRva = 0xA4E370;
static const uint32_t kActorPairReportSlabStrideRva = 0xA4DAC4;
static const uint32_t kReleaseActorPairReportDataRva = 0xA522A0;
static const uint32_t kAddPersistentContactEventPairRva = 0xA4CD90;
static const uint32_t kRemovePersistentContactEventPairRva = 0xA53840;
static const uint32_t kContactReportBufferAllocateRva = 0xA53950;
static const uint32_t kReportSceneTimestampLayoutRva = 0xA551DC;
static const uint32_t kInitManagerRva = 0xA7E5F0;
static const uint32_t kCreateSipRva = 0xA54430;
static const uint32_t kGetShapeTypeRva = 0x842360;
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
static const uint32_t kFinishBroadPhaseRva = 0xA31CA0;
static const uint32_t kIslandAddEdgeRva = 0xA63940;
static const uint32_t kIslandRemoveEdgeRva = 0xA64490;
static const uint32_t kIslandPrivateUpdateRva = 0xA65430;
static const uint32_t kIslandUpdateRva = 0xA65530;
static const uint32_t kIslandSecondUpdateRva = 0xA655B0;
static const uint32_t kContextUpdateIslandsRva = 0xA6C910;
static const uint32_t kCreateManagerTransformCacheLayoutRva = 0xA54530;
static const uint32_t kShapeSimCreateTransformCacheRva = 0xA473B0;
static const uint8_t kCreateManagerBytes[] = {0x55,0x8B,0xEC,0x53,0x8B,0xD9};
static const uint8_t kCreateShapeInstancePairBytes[] = {
    0x55,0x8B,0xEC,0x51,0x53,0x8B,0x5D,0x08
};
static const uint8_t kCreateShapeInstancePairPoolBytes[] = {
    0x81,0xC6,0xE0,0x02,0x00,0x00,0x8B,0xD8,
    0x83,0xBE,0x24,0x01,0x00,0x00,0x00,0x75,0x07,0x8B,0xCE
};
static const uint8_t kCreateMarkerPoolLayoutBytes[] = {
    0x56,0x57,0x8D,0x8B,0x58,0x06,0x00,0x00,
    0xE8,0x2E,0xC8,0xFF,0xFF
};
static const uint8_t kCreateTriggerPoolLayoutBytes[] = {
    0x52,0x50,0x81,0xC1,0x08,0x04,0x00,0x00,
    0xE8,0x79,0xC7,0xFF,0xFF
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
static const uint8_t kReportSceneTimestampLayoutBytes[] = {
    0x8B,0x4D,0xBC,0x8B,0x7F,0x30,0x8B,0x41,0x4C,0x83,0x7F,
    0x14,0x00,0x89,0x45,0xE0,0x8B,0x41,0x50,0x89,0x45,0x8C
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
static const uint8_t kContextUpdateIslandsBytes[] = {
    0x55,0x8B,0xEC,0x6A,0x00,0xFF,0x75,0x0C,
    0x81,0xC1,0x1C,0x18,0x00,0x00,0xE8
};
static const uint8_t kCreateManagerTransformCacheLayoutBytes[] = {
    0x8B,0x86,0xB4,0x04,0x00,0x00,0x8B,0x4D,0xF8,
    0x8B,0xB0,0xE8,0x03,0x00,0x00,0x81,0xC6,0xBC,0x1D,0x00,0x00,
    0x56
};
static const uint8_t kShapeSimCreateTransformCacheBytes[] = {
    0x55,0x8B,0xEC,0x83,0xEC,0x20,0x8B,0xC1,0x53,0x8B,0x5D,0x08,
    0x89,0x45,0xFC,0x83,0x78,0x18,0xFF
};
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

static HANDLE g_finishPauseEntered = 0;
static HANDLE g_finishPauseRelease = 0;
static volatile LONG g_finishPauseClaimed = 0;
static HANDLE g_islandAddPauseEntered = 0;
static HANDLE g_islandAddPauseRelease = 0;
static volatile LONG g_islandAddPauseClaimed = 0;
typedef BOOL (WINAPI *VirtualProtectFunction)(LPVOID, SIZE_T, DWORD, PDWORD);
static VirtualProtectFunction g_realVirtualProtect = 0;
static volatile LONG g_virtualProtectCallCount = 0;

static BOOL WINAPI FailSecondVirtualProtect(LPVOID address, SIZE_T size,
    DWORD protection, PDWORD oldProtection) {
    const LONG call = InterlockedIncrement(&g_virtualProtectCallCount);
    const BOOL changed = g_realVirtualProtect(address, size, protection,
        oldProtection);
    if (call == 2 && changed) {
        SetLastError(ERROR_ACCESS_DENIED);
        return FALSE;
    }
    return changed;
}

static DWORD* FindVirtualProtectImport(HMODULE module) {
    uint8_t* base = reinterpret_cast<uint8_t*>(module);
    const IMAGE_DOS_HEADER* dos = reinterpret_cast<const IMAGE_DOS_HEADER*>(
        base);
    if (!base || dos->e_magic != IMAGE_DOS_SIGNATURE) return 0;
    const IMAGE_NT_HEADERS32* nt =
        reinterpret_cast<const IMAGE_NT_HEADERS32*>(base + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return 0;
    const IMAGE_DATA_DIRECTORY& imports = nt->OptionalHeader.DataDirectory[
        IMAGE_DIRECTORY_ENTRY_IMPORT];
    if (!imports.VirtualAddress) return 0;
    IMAGE_IMPORT_DESCRIPTOR* descriptor =
        reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base +
            imports.VirtualAddress);
    for (; descriptor->Name; ++descriptor) {
        if (!descriptor->OriginalFirstThunk) continue;
        IMAGE_THUNK_DATA32* names = reinterpret_cast<IMAGE_THUNK_DATA32*>(
            base + descriptor->OriginalFirstThunk);
        IMAGE_THUNK_DATA32* functions = reinterpret_cast<IMAGE_THUNK_DATA32*>(
            base + descriptor->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++functions) {
            if (IMAGE_SNAP_BY_ORDINAL32(names->u1.Ordinal)) continue;
            const IMAGE_IMPORT_BY_NAME* importName =
                reinterpret_cast<const IMAGE_IMPORT_BY_NAME*>(base +
                    names->u1.AddressOfData);
            if (strcmp(reinterpret_cast<const char*>(importName->Name),
                    "VirtualProtect") == 0)
                return &functions->u1.Function;
        }
    }
    return 0;
}

static bool WriteImportFunction(DWORD* slot, uintptr_t value) {
    if (!slot) return false;
    DWORD oldProtection = 0;
    if (!VirtualProtect(slot, sizeof(*slot), PAGE_READWRITE,
            &oldProtection)) return false;
    *slot = static_cast<uint32_t>(value);
    DWORD ignored = 0;
    return VirtualProtect(slot, sizeof(*slot), oldProtection, &ignored) !=
        FALSE;
}

static void __cdecl PauseFirstSyntheticFinishBroadPhase() {
    if (!g_finishPauseEntered || !g_finishPauseRelease ||
        InterlockedCompareExchange(&g_finishPauseClaimed, 1, 0) != 0)
        return;
    SetEvent(g_finishPauseEntered);
    WaitForSingleObject(g_finishPauseRelease, 10000);
}

static void __cdecl PauseFirstSyntheticIslandAdd() {
    if (!g_islandAddPauseEntered || !g_islandAddPauseRelease ||
        InterlockedCompareExchange(&g_islandAddPauseClaimed, 1, 0) != 0)
        return;
    SetEvent(g_islandAddPauseEntered);
    WaitForSingleObject(g_islandAddPauseRelease, 10000);
}

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
    CopyBytes(image + kCreateMarkerPoolLayoutRva,
        kCreateMarkerPoolLayoutBytes,
        sizeof(kCreateMarkerPoolLayoutBytes));
    CopyBytes(image + kCreateTriggerPoolLayoutRva,
        kCreateTriggerPoolLayoutBytes,
        sizeof(kCreateTriggerPoolLayoutBytes));
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
    CopyBytes(image + kReportSceneTimestampLayoutRva,
        kReportSceneTimestampLayoutBytes,
        sizeof(kReportSceneTimestampLayoutBytes));
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
    CopyBytes(image + kInteractionSceneCtorRva,
        kInteractionSceneCtorBytes, sizeof(kInteractionSceneCtorBytes));
    CopyBytes(image + kInteractionSceneCtorPool16Rva,
        kInteractionSceneCtorPool16Bytes,
        sizeof(kInteractionSceneCtorPool16Bytes));
    CopyBytes(image + kInteractionSceneCtorPool32Rva,
        kInteractionSceneCtorPool32Bytes,
        sizeof(kInteractionSceneCtorPool32Bytes));
    CopyBytes(image + kInteractionSceneCtorTailRva,
        kInteractionSceneCtorTailBytes,
        sizeof(kInteractionSceneCtorTailBytes));
    CopyBytes(image + kInteractionActorCtorLayoutRva,
        kInteractionActorCtorLayoutBytes,
        sizeof(kInteractionActorCtorLayoutBytes));
    CopyBytes(image + kInteractionActorReallocRva,
        kInteractionActorReallocBytes,
        sizeof(kInteractionActorReallocBytes));
    CopyBytes(image + kInteractionActorRegisterRva,
        kInteractionActorRegisterBytes,
        sizeof(kInteractionActorRegisterBytes));
    CopyBytes(image + kInteractionActorUnregisterRva,
        kInteractionActorUnregisterBytes,
        sizeof(kInteractionActorUnregisterBytes));
    CopyBytes(image + kInteractionPointerAllocateRva,
        kInteractionPointerAllocateBytes,
        sizeof(kInteractionPointerAllocateBytes));
    CopyBytes(image + kInteractionPointerFreeRva,
        kInteractionPointerFreeBytes,
        sizeof(kInteractionPointerFreeBytes));
    CopyBytes(image + kInteractionActiveTestRva,
        kInteractionActiveTestBytes,
        sizeof(kInteractionActiveTestBytes));
    CopyBytes(image + kInteractionActivateRva,
        kInteractionActivateBytes, sizeof(kInteractionActivateBytes));
    CopyBytes(image + kInteractionDeactivateRva,
        kInteractionDeactivateBytes, sizeof(kInteractionDeactivateBytes));
    CopyBytes(image + kInteractionRegisterRva,
        kInteractionRegisterBytes, sizeof(kInteractionRegisterBytes));
    CopyBytes(image + kInteractionUnregisterRva,
        kInteractionUnregisterBytes, sizeof(kInteractionUnregisterBytes));
    CopyBytes(image + kCreateManagerTransformCacheLayoutRva,
        kCreateManagerTransformCacheLayoutBytes,
        sizeof(kCreateManagerTransformCacheLayoutBytes));
    CopyBytes(image + kShapeSimCreateTransformCacheRva,
        kShapeSimCreateTransformCacheBytes,
        sizeof(kShapeSimCreateTransformCacheBytes));

    // Executable synthetic Scene::finishBroadPhase.  Its entry/layout bytes
    // match the shipped revision exactly.  After the guarded prefix it makes
    // one deterministic cache-byte and graph-timestamp change, then returns
    // with the real thiscall RET 4 contract.  Observer tests compare this
    // unhooked baseline with the hooked pass-through result byte-for-byte.
    CopyBytes(image + kFinishBroadPhaseRva, kFinishBroadPhaseBytes,
        sizeof(kFinishBroadPhaseBytes));
    const uint8_t finishArgument[] = {0xFF,0x75,0x08};
    CopyBytes(image + kFinishBroadPhaseRva + 0x09, finishArgument,
        sizeof(finishArgument));
    CopyBytes(image + kFinishBroadPhaseRva + 0x0C,
        kFinishBroadPhaseLayoutBytes, sizeof(kFinishBroadPhaseLayoutBytes));
    uint8_t finishTail[] = {
        0x83,0xC4,0x04,                         // add esp,4
        0x8B,0x83,0xB4,0x04,0x00,0x00,          // mov eax,[ebx+4b4h]
        0xFF,0x80,0xEC,0x03,0x00,0x00,          // inc [eax+3ech]
        0x8B,0x80,0xE8,0x03,0x00,0x00,          // mov eax,[eax+3e8h]
        0x8B,0x80,0xCC,0x1D,0x00,0x00,          // mov eax,[eax+1dcch]
        0xFF,0x00,                               // inc dword ptr [eax]
        0xB8,0x00,0x00,0x00,0x00,               // mov eax,pause helper
        0xFF,0xD0,                               // call eax
        0x5F,0x5E,0x5B,                          // pop edi; pop esi; pop ebx
        0x8B,0xE5,0x5D,0xC2,0x04,0x00           // leave-ish; ret 4
    };
    const uint32_t pauseAddress = static_cast<uint32_t>(
        reinterpret_cast<uintptr_t>(PauseFirstSyntheticFinishBroadPhase));
    CopyBytes(finishTail + 30, reinterpret_cast<const uint8_t*>(
        &pauseAddress), sizeof(pauseAddress));
    CopyBytes(image + kFinishBroadPhaseRva + 0x21, finishTail,
        sizeof(finishTail));

    CopyBytes(image + kIslandAddEdgeRva, kIslandAddEdgeBytes,
        sizeof(kIslandAddEdgeBytes));
    uint8_t addTail[] = {
        0x00,0x00,0x00,0x00,                   // lea esi,[ebx]
        0x8B,0x45,0x14,                         // mov eax,[ebp+14h]
        0xC7,0x00,0x01,0x00,0x00,0x00,          // mov [eax],1
        0xB8,0x00,0x00,0x00,0x00,               // mov eax,pause helper
        0xFF,0xD0,                               // call eax
        0x5E,0x5B,0x8B,0xE5,0x5D,0xC2,0x10,0x00
    };
    const uint32_t islandAddPauseAddress = static_cast<uint32_t>(
        reinterpret_cast<uintptr_t>(PauseFirstSyntheticIslandAdd));
    CopyBytes(addTail + 14, reinterpret_cast<const uint8_t*>(
        &islandAddPauseAddress), sizeof(islandAddPauseAddress));
    CopyBytes(image + kIslandAddEdgeRva + sizeof(kIslandAddEdgeBytes),
        addTail, sizeof(addTail));
    CopyBytes(image + kIslandRemoveEdgeRva, kIslandRemoveEdgeBytes,
        sizeof(kIslandRemoveEdgeBytes));
    const uint8_t removeTail[] = {
        0x00,0x00,0x00,0x00,                   // lea esi,[edi]
        0xC7,0x03,0xFF,0xFF,0xFF,0xFF,          // mov [ebx],ffffffffh
        0x5F,0x5E,0x5B,0x8B,0xE5,0x5D,0xC2,0x08,0x00
    };
    CopyBytes(image + kIslandRemoveEdgeRva + sizeof(kIslandRemoveEdgeBytes),
        removeTail, sizeof(removeTail));
    CopyBytes(image + kIslandPrivateUpdateRva, kIslandPrivateUpdateBytes,
        sizeof(kIslandPrivateUpdateBytes));
    CopyBytes(image + kIslandUpdateRva, kIslandUpdateBytes,
        sizeof(kIslandUpdateBytes));
    image[0xA63A20] = 0xC3;
    image[0xA64BE0] = 0xC3;
    const uint8_t updateTail[] = {
        0xFF,0x86,0xBC,0x01,0x00,0x00,          // inc [esi+1bch]
        0x5F,0x5E,0x5B,0xC2,0x08,0x00
    };
    CopyBytes(image + kIslandUpdateRva + sizeof(kIslandUpdateBytes),
        updateTail, sizeof(updateTail));
    CopyBytes(image + kIslandSecondUpdateRva, kIslandSecondUpdateBytes,
        sizeof(kIslandSecondUpdateBytes));
    CopyBytes(image + kContextUpdateIslandsRva,
        kContextUpdateIslandsBytes, sizeof(kContextUpdateIslandsBytes));
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
    DirtyCaptureCopy copyCapture, DirtyCaptureSnapshot captureSnapshot,
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

    // Change only the live dense order.  The stateless reader must return the
    // current C/A/B image while the completed hook capture remains A/B/C.
    entries[0] = reinterpret_cast<uintptr_t>(interactions[2]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[0]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[1]);
    BuildDirtyHash(entries, 3, next, capacity, hash, hashSize);
    DirtyInteractionOrderReceipt hookBeforeSnapshot = {};
    Check(status(imagePointer, &hookBeforeSnapshot) == 1 &&
        hookBeforeSnapshot.result == 1,
        "dirty hook receipt is readable before stateless capture");
    DirtyInteractionKey snapshotKeys[3] = {};
    DirtyInteractionOrderReceipt snapshotReceipt = {};
    Check(captureSnapshot(imagePointer,
            reinterpret_cast<uintptr_t>(nphase), snapshotKeys, 3,
            &snapshotReceipt) == 1 && snapshotReceipt.result == 1 &&
        snapshotReceipt.nphaseCore == reinterpret_cast<uintptr_t>(nphase) &&
        snapshotReceipt.count == 3 &&
        snapshotReceipt.orderHashBefore == snapshotReceipt.orderHashAfter &&
        SameDirtyKey(snapshotKeys[0], captured[2]) &&
        SameDirtyKey(snapshotKeys[1], captured[0]) &&
        SameDirtyKey(snapshotKeys[2], captured[1]),
        "dirty stateless snapshot captures the exact current dense order");
    DirtyInteractionOrderReceipt hookAfterSnapshot = {};
    Check(status(imagePointer, &hookAfterSnapshot) == 1 &&
        memcmp(&hookBeforeSnapshot, &hookAfterSnapshot,
            sizeof(hookBeforeSnapshot)) == 0,
        "dirty stateless snapshot preserves the pending hook receipt byte-for-byte");
    DirtyInteractionOrderReceipt shortSnapshot = {};
    Check(captureSnapshot(imagePointer,
            reinterpret_cast<uintptr_t>(nphase), snapshotKeys, 2,
            &shortSnapshot) == 0 && shortSnapshot.result == 11 &&
        shortSnapshot.count == 3,
        "dirty stateless snapshot reports required caller capacity");
    Check(status(imagePointer, &hookAfterSnapshot) == 1 &&
        memcmp(&hookBeforeSnapshot, &hookAfterSnapshot,
            sizeof(hookBeforeSnapshot)) == 0,
        "dirty stateless capacity failure leaves the hook receipt unchanged");
    DirtyInteractionKey legacyAfterSnapshot[3] = {};
    Check(copyCapture(imagePointer, legacyAfterSnapshot, 3, &receipt) == 1 &&
        SameDirtyKey(legacyAfterSnapshot[0], captured[0]) &&
        SameDirtyKey(legacyAfterSnapshot[1], captured[1]) &&
        SameDirtyKey(legacyAfterSnapshot[2], captured[2]),
        "dirty stateless snapshot preserves the completed legacy A/B/C capture");
    entries[0] = reinterpret_cast<uintptr_t>(interactions[0]);
    entries[1] = reinterpret_cast<uintptr_t>(interactions[1]);
    entries[2] = reinterpret_cast<uintptr_t>(interactions[2]);
    BuildDirtyHash(entries, 3, next, capacity, hash, hashSize);

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
    DirtyInteractionOrderReceipt armedBeforeSnapshot = {};
    Check(status(imagePointer, &armedBeforeSnapshot) == 1 &&
        armedBeforeSnapshot.result == 18 && armedBeforeSnapshot.armed == 1,
        "dirty armed hook receipt is readable before rejected snapshot");
    snapshotReceipt = {};
    Check(captureSnapshot(imagePointer,
            reinterpret_cast<uintptr_t>(nphase), snapshotKeys, 3,
            &snapshotReceipt) == 0 && snapshotReceipt.result == 18 &&
        snapshotReceipt.lastError == ERROR_IO_PENDING,
        "dirty stateless snapshot rejects an in-flight hook transaction");
    DirtyInteractionOrderReceipt armedAfterSnapshot = {};
    Check(status(imagePointer, &armedAfterSnapshot) == 1 &&
        memcmp(&armedBeforeSnapshot, &armedAfterSnapshot,
            sizeof(armedBeforeSnapshot)) == 0,
        "dirty rejected stateless snapshot preserves the armed hook receipt");
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
    Check(receipt.result == 1 && receipt.apiVersion == kCurrentApiVersion &&
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
    Check(receipt.result == 1 && receipt.apiVersion == kCurrentApiVersion &&
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

struct NPhasePoolTestLayout {
    uint32_t offset;
    uint32_t elementSize;
    uint32_t slabSize;
};

static NPhasePoolTestLayout GetNPhasePoolTestLayout(uint32_t kind) {
    NPhasePoolTestLayout layout = {};
    if (kind == 0u) {
        layout.offset = 0x90u;
        layout.elementSize = 0x18u;
        layout.slabSize = 0x300u;
    } else if (kind == 1u) {
        layout.offset = 0x2E0u;
        layout.elementSize = 0x44u;
        layout.slabSize = 0x880u;
    } else if (kind == 2u) {
        layout.offset = 0x408u;
        layout.elementSize = 0x3Cu;
        layout.slabSize = 0x780u;
    } else if (kind == 3u) {
        layout.offset = 0x530u;
        layout.elementSize = 0x24u;
        layout.slabSize = 0x480u;
    } else if (kind == 4u) {
        layout.offset = 0x658u;
        layout.elementSize = 0x28u;
        layout.slabSize = 0x500u;
    }
    return layout;
}

static void InitializeNPhasePoolTest(uint8_t* nphase, uint8_t* slab,
    uintptr_t* slabs, uint32_t kind, uint32_t* expectedFree) {
    const NPhasePoolTestLayout layout = GetNPhasePoolTestLayout(kind);
    memset(nphase, 0, 0x800u);
    for (uint32_t i = 0; i < 0x880u; ++i)
        slab[i] = static_cast<uint8_t>((i * 13u + kind * 37u + 11u) & 0xFFu);
    slabs[0] = reinterpret_cast<uintptr_t>(slab);
    uint8_t* pool = nphase + layout.offset;
    *reinterpret_cast<uintptr_t*>(pool + 0x108u) =
        reinterpret_cast<uintptr_t>(slabs);
    *reinterpret_cast<uint32_t*>(pool + 0x10Cu) = 1u;
    *reinterpret_cast<uint32_t*>(pool + 0x110u) = 0x80000080u;
    *reinterpret_cast<uint32_t*>(pool + 0x114u) = 32u;
    *reinterpret_cast<uint32_t*>(pool + 0x118u) = 3u;
    *reinterpret_cast<uint32_t*>(pool + 0x11Cu) = 29u;
    *reinterpret_cast<uint32_t*>(pool + 0x120u) = layout.slabSize;
    uint32_t freeCount = 0;
    for (uint32_t ordinal = 32u; ordinal-- > 0u;) {
        if (ordinal == 1u || ordinal == 7u || ordinal == 30u) continue;
        expectedFree[freeCount++] = ordinal;
    }
    for (uint32_t i = 0; i < freeCount; ++i) {
        uint8_t* element = slab + expectedFree[i] * layout.elementSize;
        *reinterpret_cast<uintptr_t*>(element) = i + 1u < freeCount ?
            reinterpret_cast<uintptr_t>(slab +
                expectedFree[i + 1u] * layout.elementSize) : 0u;
    }
    *reinterpret_cast<uintptr_t*>(pool + 0x124u) =
        reinterpret_cast<uintptr_t>(slab +
            expectedFree[0] * layout.elementSize);
}

static void RunNPhasePoolSnapshotTests(uint8_t* image,
    CaptureNPhasePoolSnapshotV1 capture) {
    bool allKindsExact = true;
    bool allKindsRepeatable = true;
    for (uint32_t kind = 0; kind < 5u; ++kind) {
        const NPhasePoolTestLayout layout = GetNPhasePoolTestLayout(kind);
        __declspec(align(16)) uint8_t nphase[0x800] = {};
        __declspec(align(16)) uint8_t slab[0x880] = {};
        uintptr_t slabs[128] = {};
        uint32_t expectedFree[29] = {};
        InitializeNPhasePoolTest(nphase, slab, slabs, kind, expectedFree);
        uint8_t nphaseBefore[sizeof(nphase)] = {};
        uint8_t slabBefore[sizeof(slab)] = {};
        memcpy(nphaseBefore, nphase, sizeof(nphase));
        memcpy(slabBefore, slab, sizeof(slab));

        uintptr_t slabBases[1] = {};
        uint32_t freeSlots[29] = {};
        uint32_t allocationWords[1] = {};
        uint8_t slabBytes[0x880] = {};
        NPhasePoolSnapshotBuffersV1 buffers = {
            slabBases, 1u, freeSlots, 29u, allocationWords, 1u,
            slabBytes, layout.slabSize
        };
        NPhasePoolSnapshotReceiptV1 receipt = {};
        const int captured = capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), kind, &buffers, &receipt);
        const bool exact = captured == 1 && receipt.result == 1u &&
            receipt.apiVersion == kCurrentApiVersion &&
            receipt.structSize == sizeof(receipt) &&
            receipt.pool == reinterpret_cast<uintptr_t>(
                nphase + layout.offset) &&
            receipt.slabsData == reinterpret_cast<uintptr_t>(slabs) &&
            receipt.freeHead == reinterpret_cast<uintptr_t>(slab +
                expectedFree[0] * layout.elementSize) &&
            receipt.poolKind == kind && receipt.poolOffset == layout.offset &&
            receipt.elementSize == layout.elementSize &&
            receipt.elementsPerSlab == 32u &&
            receipt.slabSize == layout.slabSize &&
            receipt.slabCount == 1u &&
            receipt.slabCapacityRaw == 0x80000080u &&
            receipt.totalSlots == 32u && receipt.used == 3u &&
            receipt.unreleased == 29u &&
            receipt.freeHeadSlot == expectedFree[0] &&
            receipt.slabBasesRequired == 1u &&
            receipt.slabBasesWritten == 1u &&
            receipt.freeSlotsRequired == 29u &&
            receipt.freeSlotsWritten == 29u &&
            receipt.allocationWordsRequired == 1u &&
            receipt.allocationWordsWritten == 1u &&
            receipt.slabBytesRequired == layout.slabSize &&
            receipt.slabBytesWritten == layout.slabSize &&
            receipt.validationFlags == 0xFFu &&
            receipt.metadataHash != 0u && receipt.slabBaseHash != 0u &&
            receipt.freeSlotOrderHash != 0u &&
            receipt.allocationBitmapHash != 0u &&
            receipt.slabByteHash != 0u && receipt.snapshotHash != 0u &&
            slabBases[0] == reinterpret_cast<uintptr_t>(slab) &&
            memcmp(freeSlots, expectedFree, sizeof(expectedFree)) == 0 &&
            allocationWords[0] ==
                ((1u << 1u) | (1u << 7u) | (1u << 30u)) &&
            memcmp(slabBytes, slab, layout.slabSize) == 0 &&
            memcmp(nphaseBefore, nphase, sizeof(nphase)) == 0 &&
            memcmp(slabBefore, slab, sizeof(slab)) == 0;
        allKindsExact = allKindsExact && exact;

        uintptr_t repeatedBases[1] = {};
        uint32_t repeatedFree[29] = {};
        uint32_t repeatedWords[1] = {};
        uint8_t repeatedBytes[0x880] = {};
        NPhasePoolSnapshotBuffersV1 repeatedBuffers = {
            repeatedBases, 1u, repeatedFree, 29u, repeatedWords, 1u,
            repeatedBytes, layout.slabSize
        };
        NPhasePoolSnapshotReceiptV1 repeatedReceipt = {};
        const int repeated = capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), kind, &repeatedBuffers,
            &repeatedReceipt);
        allKindsRepeatable = allKindsRepeatable && repeated == 1 &&
            memcmp(&receipt, &repeatedReceipt, sizeof(receipt)) == 0 &&
            memcmp(slabBases, repeatedBases, sizeof(slabBases)) == 0 &&
            memcmp(freeSlots, repeatedFree, sizeof(freeSlots)) == 0 &&
            memcmp(allocationWords, repeatedWords,
                sizeof(allocationWords)) == 0 &&
            memcmp(slabBytes, repeatedBytes, layout.slabSize) == 0;
    }
    Check(allKindsExact,
        "NPhase pool capture preserves exact metadata and bytes for all five pools");
    Check(allKindsRepeatable,
        "NPhase pool capture is byte-repeatable for all five pools");

    {
        const NPhasePoolTestLayout inlineLayout = GetNPhasePoolTestLayout(0u);
        __declspec(align(16)) uint8_t inlineNphase[0x800] = {};
        __declspec(align(16)) uint8_t inlineSlab[0x880] = {};
        uintptr_t externalSlabs[128] = {};
        uint32_t inlineExpectedFree[29] = {};
        InitializeNPhasePoolTest(inlineNphase, inlineSlab, externalSlabs, 0u,
            inlineExpectedFree);
        uint8_t* inlinePool = inlineNphase + inlineLayout.offset;
        uintptr_t* inlineSlabs = reinterpret_cast<uintptr_t*>(
            inlinePool + 0x04u);
        inlineSlabs[0] = reinterpret_cast<uintptr_t>(inlineSlab);
        *reinterpret_cast<uintptr_t*>(inlinePool + 0x108u) =
            reinterpret_cast<uintptr_t>(inlineSlabs);
        *reinterpret_cast<uint32_t*>(inlinePool + 0x110u) = 0x80000040u;
        inlinePool[0x104u] = 1u;
        uintptr_t inlineBases[1] = {};
        uint32_t inlineFree[29] = {};
        uint32_t inlineWords[1] = {};
        uint8_t inlineBytes[0x880] = {};
        NPhasePoolSnapshotBuffersV1 inlineBuffers = {
            inlineBases, 1u, inlineFree, 29u, inlineWords, 1u,
            inlineBytes, inlineLayout.slabSize
        };
        NPhasePoolSnapshotReceiptV1 inlineReceipt = {};
        Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(inlineNphase), 0u, &inlineBuffers,
            &inlineReceipt) == 1 && inlineReceipt.result == 1u &&
            inlineReceipt.slabsData == reinterpret_cast<uintptr_t>(
                inlinePool + 0x04u) &&
            inlineBases[0] == reinterpret_cast<uintptr_t>(inlineSlab) &&
            memcmp(inlineFree, inlineExpectedFree,
                sizeof(inlineExpectedFree)) == 0 &&
            memcmp(inlineBytes, inlineSlab, inlineLayout.slabSize) == 0,
            "NPhase pool capture accepts the canonical inline slab table");

        *reinterpret_cast<uint32_t*>(inlinePool + 0x110u) = 128u;
        inlineReceipt = {};
        Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(inlineNphase), 0u, &inlineBuffers,
            &inlineReceipt) == 0 && inlineReceipt.result == 6u,
            "NPhase pool capture rejects corrupt inline slab capacity");

        *reinterpret_cast<uint32_t*>(inlinePool + 0x110u) = 0x80000040u;
        inlinePool[0x104u] = 0u;
        inlineReceipt = {};
        Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(inlineNphase), 0u, &inlineBuffers,
            &inlineReceipt) == 0 && inlineReceipt.result == 6u,
            "NPhase pool capture rejects corrupt inline allocator ownership");
    }

    const NPhasePoolTestLayout layout = GetNPhasePoolTestLayout(0u);
    __declspec(align(16)) uint8_t nphase[0x800] = {};
    __declspec(align(16)) uint8_t slab[0x880] = {};
    uintptr_t slabs[128] = {};
    uint32_t expectedFree[29] = {};
    uintptr_t slabBases[1] = {};
    uint32_t freeSlots[29] = {};
    uint32_t allocationWords[1] = {};
    uint8_t slabBytes[0x880] = {};
    NPhasePoolSnapshotBuffersV1 buffers = {
        slabBases, 1u, freeSlots, 29u, allocationWords, 1u,
        slabBytes, layout.slabSize
    };
    NPhasePoolSnapshotReceiptV1 receipt = {};

    InitializeNPhasePoolTest(nphase, slab, slabs, 0u, expectedFree);
    uint8_t* pool = nphase + layout.offset;
    *reinterpret_cast<uint32_t*>(pool + 0x114u) = 31u;
    memset(slabBytes, 0xCD, sizeof(slabBytes));
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &buffers, &receipt) == 0 &&
        receipt.result == 6u && slabBytes[0] == 0xCDu,
        "NPhase pool capture rejects corrupt layout metadata before output");

    InitializeNPhasePoolTest(nphase, slab, slabs, 0u, expectedFree);
    uint8_t* freeHead = slab + expectedFree[0] * layout.elementSize;
    *reinterpret_cast<uintptr_t*>(freeHead) =
        reinterpret_cast<uintptr_t>(freeHead);
    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &buffers, &receipt) == 0 &&
        receipt.result == 9u,
        "NPhase pool capture rejects a cyclic duplicate free chain");

    InitializeNPhasePoolTest(nphase, slab, slabs, 0u, expectedFree);
    bool allShortBuffersRejected = true;
    for (uint32_t bufferKind = 0; bufferKind < 4u; ++bufferKind) {
        NPhasePoolSnapshotBuffersV1 shortBuffers = buffers;
        if (bufferKind == 0u) shortBuffers.slabBaseCapacity = 0u;
        else if (bufferKind == 1u) shortBuffers.freeSlotCapacity = 28u;
        else if (bufferKind == 2u)
            shortBuffers.allocationWordCapacity = 0u;
        else shortBuffers.slabByteCapacity = layout.slabSize - 1u;
        receipt = {};
        allShortBuffersRejected = allShortBuffersRejected &&
            capture(reinterpret_cast<uintptr_t>(image),
                reinterpret_cast<uintptr_t>(nphase), 0u, &shortBuffers,
                &receipt) == 0 && receipt.result == 10u &&
            receipt.slabBasesRequired == 1u &&
            receipt.freeSlotsRequired == 29u &&
            receipt.allocationWordsRequired == 1u &&
            receipt.slabBytesRequired == layout.slabSize;
    }
    Check(allShortBuffersRejected,
        "NPhase pool capture reports every undersized caller buffer");

    InitializeNPhasePoolTest(nphase, slab, slabs, 0u, expectedFree);
    uint8_t overlapStorage[128] = {};
    NPhasePoolSnapshotBuffersV1 overlapBuffers = buffers;
    overlapBuffers.slabBases =
        reinterpret_cast<uintptr_t*>(overlapStorage);
    overlapBuffers.freeSlots = reinterpret_cast<uint32_t*>(overlapStorage);
    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &overlapBuffers,
        &receipt) == 0 && receipt.result == 12u,
        "NPhase pool capture rejects overlapping caller outputs");

    NPhasePoolSnapshotBuffersV1 sourceOverlapBuffers = buffers;
    sourceOverlapBuffers.slabBytes = slab;
    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &sourceOverlapBuffers,
        &receipt) == 0 && receipt.result == 12u,
        "NPhase pool capture rejects output that aliases live pool storage");

    NPhasePoolSnapshotBuffersV1 inactiveTableTailBuffers = buffers;
    inactiveTableTailBuffers.slabBases = slabs + 64u;
    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &inactiveTableTailBuffers,
        &receipt) == 0 && receipt.result == 12u,
        "NPhase pool capture protects the external slab-table capacity tail");

    InitializeNPhasePoolTest(nphase, slab, slabs, 0u, expectedFree);
    uint8_t* overlappingSlabTable = slab + layout.elementSize;
    *reinterpret_cast<uintptr_t*>(overlappingSlabTable) =
        reinterpret_cast<uintptr_t>(slab);
    *reinterpret_cast<uintptr_t*>(pool + 0x108u) =
        reinterpret_cast<uintptr_t>(overlappingSlabTable);
    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &buffers, &receipt) == 0 &&
        receipt.result == 6u,
        "NPhase pool capture rejects overlapping source partitions");

    InitializeNPhasePoolTest(nphase, slab, slabs, 0u, expectedFree);
    slabs[0] = reinterpret_cast<uintptr_t>(slab + 1u);
    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &buffers, &receipt) == 0 &&
        receipt.result == 7u,
        "NPhase pool capture rejects an unaligned slab base");

    InitializeNPhasePoolTest(nphase, slab, slabs, 0u, expectedFree);
    image[kCreateMarkerPoolLayoutRva] ^= 1u;
    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 0u, &buffers, &receipt) == 0 &&
        receipt.result == 3u,
        "NPhase pool capture rejects a shipped-code revision mismatch");
    image[kCreateMarkerPoolLayoutRva] ^= 1u;

    receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
        reinterpret_cast<uintptr_t>(nphase), 5u, &buffers, &receipt) == 0 &&
        receipt.result == 4u,
        "NPhase pool capture rejects an unknown pool kind");
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
    Check(receipt.result == 1 && receipt.apiVersion == kCurrentApiVersion &&
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

static void RunNPhaseReportSnapshotV1Tests(uint8_t* image,
    CaptureNPhaseReportSnapshotV1 capture) {
    __declspec(align(16)) uint8_t nphase[0x80] = {};
    __declspec(align(16)) uint8_t scene[0x80] = {};
    __declspec(align(16)) uint8_t actorPairObjects[2][0x18] = {};
    __declspec(align(16)) uint8_t sipObjects[3][0x44] = {};
    __declspec(align(16)) uint8_t reportBytes[32] = {};
    __declspec(align(16)) uintptr_t actorPairBacking[4] = {
        reinterpret_cast<uintptr_t>(actorPairObjects[1]),
        reinterpret_cast<uintptr_t>(actorPairObjects[0]),
        0xA1A2A3A4u, 0xA5A6A7A8u
    };
    __declspec(align(16)) uintptr_t persistentBacking[4] = {
        reinterpret_cast<uintptr_t>(sipObjects[2]),
        reinterpret_cast<uintptr_t>(sipObjects[0]),
        0xB1B2B3B4u, 0xB5B6B7B8u
    };
    __declspec(align(16)) uintptr_t forceBacking[2] = {
        reinterpret_cast<uintptr_t>(sipObjects[1]), 0xC1C2C3C4u
    };
    *reinterpret_cast<uintptr_t*>(nphase + 0x00) =
        reinterpret_cast<uintptr_t>(scene);
    *reinterpret_cast<uintptr_t*>(nphase + 0x04) =
        reinterpret_cast<uintptr_t>(actorPairBacking);
    *reinterpret_cast<uint32_t*>(nphase + 0x08) = 2u;
    *reinterpret_cast<uint32_t*>(nphase + 0x0C) = 0x80000004u;
    *reinterpret_cast<uintptr_t*>(nphase + 0x10) =
        reinterpret_cast<uintptr_t>(persistentBacking);
    *reinterpret_cast<uint32_t*>(nphase + 0x14) = 2u;
    *reinterpret_cast<uint32_t*>(nphase + 0x18) = 4u;
    *reinterpret_cast<uint32_t*>(nphase + 0x1C) = 1u;
    *reinterpret_cast<uintptr_t*>(nphase + 0x20) =
        reinterpret_cast<uintptr_t>(forceBacking);
    *reinterpret_cast<uint32_t*>(nphase + 0x24) = 1u;
    *reinterpret_cast<uint32_t*>(nphase + 0x28) = 2u;
    *reinterpret_cast<uintptr_t*>(nphase + 0x2C) =
        reinterpret_cast<uintptr_t>(reportBytes);
    *reinterpret_cast<uint32_t*>(nphase + 0x30) = 19u;
    *reinterpret_cast<uint32_t*>(nphase + 0x34) = 32u;
    *reinterpret_cast<uint32_t*>(nphase + 0x38) = 16u;
    *reinterpret_cast<uint32_t*>(nphase + 0x3C) = 16u;
    nphase[0x40] = 0u;
    *reinterpret_cast<uint32_t*>(scene + 0x4C) = 0x11223344u;
    *reinterpret_cast<uint32_t*>(scene + 0x50) = 0x55667788u;
    for (uint32_t i = 0; i < 2u; ++i)
        *reinterpret_cast<uint16_t*>(actorPairObjects[i] + 0x0C) = 1u;
    *reinterpret_cast<uint32_t*>(sipObjects[2] + 0x2C) = 0x00200000u;
    *reinterpret_cast<uint32_t*>(sipObjects[2] + 0x34) = 0u;
    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x2C) = 0x00200000u;
    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x34) = 1u;
    *reinterpret_cast<uint32_t*>(sipObjects[1] + 0x2C) = 0x00800000u;
    *reinterpret_cast<uint32_t*>(sipObjects[1] + 0x34) = 0u;
    for (uint32_t i = 0; i < sizeof(reportBytes); ++i)
        reportBytes[i] = static_cast<uint8_t>(0x40u + i);

    __declspec(align(16)) uintptr_t actorOutput[4] = {};
    __declspec(align(16)) uintptr_t persistentOutput[4] = {};
    __declspec(align(16)) uintptr_t forceOutput[2] = {};
    __declspec(align(16)) uint8_t reportOutput[32] = {};
    NPhaseReportSnapshotBuffersV1 buffers = {
        actorOutput, 4u, persistentOutput, 4u, forceOutput, 2u,
        reportOutput, 32u
    };
    uint8_t nphaseBefore[sizeof(nphase)] = {};
    uint8_t sceneBefore[sizeof(scene)] = {};
    uint8_t actorObjectBefore[sizeof(actorPairObjects)] = {};
    uint8_t sipBefore[sizeof(sipObjects)] = {};
    uintptr_t actorBackingBefore[4] = {};
    uintptr_t persistentBackingBefore[4] = {};
    uintptr_t forceBackingBefore[2] = {};
    uint8_t reportBefore[sizeof(reportBytes)] = {};
    memcpy(nphaseBefore, nphase, sizeof(nphase));
    memcpy(sceneBefore, scene, sizeof(scene));
    memcpy(actorObjectBefore, actorPairObjects, sizeof(actorPairObjects));
    memcpy(sipBefore, sipObjects, sizeof(sipObjects));
    memcpy(actorBackingBefore, actorPairBacking, sizeof(actorPairBacking));
    memcpy(persistentBackingBefore, persistentBacking,
        sizeof(persistentBacking));
    memcpy(forceBackingBefore, forceBacking, sizeof(forceBacking));
    memcpy(reportBefore, reportBytes, sizeof(reportBytes));

    NPhaseReportSnapshotReceiptV1 receipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers, &receipt) == 1,
        "complete NPhase report-history capture succeeds");
    Check(receipt.result == 1u &&
        receipt.apiVersion == kCurrentApiVersion &&
        receipt.structSize == sizeof(receipt) &&
        receipt.ownerScene == reinterpret_cast<uintptr_t>(scene) &&
        receipt.sceneTimeStamp == 0x11223344u &&
        receipt.sceneReportShapePairTimeStamp == 0x55667788u &&
        receipt.actorPairs.count == 2u &&
        receipt.actorPairs.capacityRaw == 0x80000004u &&
        receipt.actorPairs.backingRequired == 4u &&
        receipt.actorPairs.backingWritten == 4u &&
        receipt.persistent.count == 2u &&
        receipt.persistent.backingWritten == 4u &&
        receipt.nextFramePersistentIndex == 1u &&
        receipt.forceThreshold.count == 1u &&
        receipt.forceThreshold.backingWritten == 2u &&
        receipt.reportBufferRequired == 32u &&
        receipt.reportBufferWritten == 32u &&
        receipt.validationFlags == 0xFFu &&
        receipt.invalidKind == 0xFFFFFFFFu &&
        receipt.invalidIndex == 0xFFFFFFFFu,
        "complete NPhase report receipt proves timestamps and full extents");
    Check(Same(actorOutput, actorPairBacking, 4u) &&
        Same(persistentOutput, persistentBacking, 4u) &&
        Same(forceOutput, forceBacking, 2u) &&
        memcmp(reportOutput, reportBytes, sizeof(reportBytes)) == 0,
        "complete NPhase report capture preserves opaque capacity tails");
    Check(receipt.actorPairs.logicalOrderHash ==
            TestPointerOrderHash(actorPairBacking, 2u) &&
        receipt.actorPairs.backingHash ==
            TestByteHash(actorPairBacking, sizeof(actorPairBacking)) &&
        receipt.persistent.logicalOrderHash ==
            TestPointerOrderHash(persistentBacking, 2u) &&
        receipt.persistent.backingHash ==
            TestByteHash(persistentBacking, sizeof(persistentBacking)) &&
        receipt.forceThreshold.logicalOrderHash ==
            TestPointerOrderHash(forceBacking, 1u) &&
        receipt.forceThreshold.backingHash ==
            TestByteHash(forceBacking, sizeof(forceBacking)) &&
        receipt.reportBufferActiveHash == TestByteHash(reportBytes, 19u) &&
        receipt.reportBufferAllocationHash ==
            TestByteHash(reportBytes, sizeof(reportBytes)) &&
        receipt.metadataHash != 0u && receipt.snapshotHash != 0u,
        "complete NPhase report capture hashes logical and raw images separately");
    Check(memcmp(nphaseBefore, nphase, sizeof(nphase)) == 0 &&
        memcmp(sceneBefore, scene, sizeof(scene)) == 0 &&
        memcmp(actorObjectBefore, actorPairObjects,
            sizeof(actorPairObjects)) == 0 &&
        memcmp(sipBefore, sipObjects, sizeof(sipObjects)) == 0 &&
        memcmp(actorBackingBefore, actorPairBacking,
            sizeof(actorPairBacking)) == 0 &&
        memcmp(persistentBackingBefore, persistentBacking,
            sizeof(persistentBacking)) == 0 &&
        memcmp(forceBackingBefore, forceBacking,
            sizeof(forceBacking)) == 0 &&
        memcmp(reportBefore, reportBytes, sizeof(reportBytes)) == 0,
        "complete NPhase report capture leaves every source byte unchanged");

    uintptr_t actorRepeated[4] = {};
    uintptr_t persistentRepeated[4] = {};
    uintptr_t forceRepeated[2] = {};
    uint8_t reportRepeated[32] = {};
    NPhaseReportSnapshotBuffersV1 repeatedBuffers = {
        actorRepeated, 4u, persistentRepeated, 4u, forceRepeated, 2u,
        reportRepeated, 32u
    };
    NPhaseReportSnapshotReceiptV1 repeated = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &repeatedBuffers,
            &repeated) == 1 &&
        memcmp(&receipt, &repeated, sizeof(receipt)) == 0 &&
        Same(actorOutput, actorRepeated, 4u) &&
        Same(persistentOutput, persistentRepeated, 4u) &&
        Same(forceOutput, forceRepeated, 2u) &&
        memcmp(reportOutput, reportRepeated, sizeof(reportOutput)) == 0,
        "complete NPhase report capture is byte-repeatable");

    actorPairBacking[2] ^= 1u;
    NPhaseReportSnapshotReceiptV1 tailReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &repeatedBuffers,
            &tailReceipt) == 1 &&
        tailReceipt.actorPairs.logicalOrderHash ==
            receipt.actorPairs.logicalOrderHash &&
        tailReceipt.actorPairs.backingHash != receipt.actorPairs.backingHash &&
        tailReceipt.snapshotHash != receipt.snapshotHash &&
        tailReceipt.reportBufferAllocationHash ==
            receipt.reportBufferAllocationHash,
        "inactive array-tail changes affect raw but not logical history");
    actorPairBacking[2] ^= 1u;
    reportBytes[25] ^= 1u;
    NPhaseReportSnapshotReceiptV1 reportTailReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &repeatedBuffers,
            &reportTailReceipt) == 1 &&
        reportTailReceipt.reportBufferActiveHash ==
            receipt.reportBufferActiveHash &&
        reportTailReceipt.reportBufferAllocationHash !=
            receipt.reportBufferAllocationHash &&
        reportTailReceipt.snapshotHash != receipt.snapshotHash,
        "inactive report-tail changes affect allocation but not active history");
    reportBytes[25] ^= 1u;

    NPhaseReportSnapshotBuffersV1 shortBuffers = buffers;
    NPhaseReportSnapshotReceiptV1 shortReceipt = {};
    shortBuffers.actorPairCapacity = 3u;
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &shortBuffers,
            &shortReceipt) == 0 && shortReceipt.result == 8u &&
        shortReceipt.actorPairs.backingRequired == 4u &&
        shortReceipt.actorPairs.backingWritten == 0u &&
        shortReceipt.reportBufferWritten == 0u,
        "complete NPhase report capture reports a short ActorPair backing");
    shortBuffers = buffers;
    shortBuffers.persistentCapacity = 3u;
    shortReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &shortBuffers,
            &shortReceipt) == 0 && shortReceipt.result == 8u,
        "complete NPhase report capture reports a short persistent backing");
    shortBuffers = buffers;
    shortBuffers.forceThresholdCapacity = 1u;
    shortReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &shortBuffers,
            &shortReceipt) == 0 && shortReceipt.result == 8u,
        "complete NPhase report capture reports a short force backing");
    shortBuffers = buffers;
    shortBuffers.reportBufferByteCapacity = 31u;
    shortReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &shortBuffers,
            &shortReceipt) == 0 && shortReceipt.result == 8u,
        "complete NPhase report capture reports a short report allocation");

    NPhaseReportSnapshotBuffersV1 overlapBuffers = buffers;
    overlapBuffers.persistentBacking = actorOutput;
    NPhaseReportSnapshotReceiptV1 overlapReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &overlapBuffers,
            &overlapReceipt) == 0 && overlapReceipt.result == 10u,
        "complete NPhase report capture rejects overlapping outputs");
    overlapBuffers = buffers;
    overlapBuffers.actorPairBacking = actorPairBacking;
    overlapReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &overlapBuffers,
            &overlapReceipt) == 0 && overlapReceipt.result == 10u &&
        memcmp(actorBackingBefore, actorPairBacking,
            sizeof(actorPairBacking)) == 0,
        "complete NPhase report capture rejects a backing-source alias");
    overlapBuffers = buffers;
    overlapBuffers.actorPairBacking = actorPairBacking + 2;
    overlapReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &overlapBuffers,
            &overlapReceipt) == 0 && overlapReceipt.result == 10u &&
        memcmp(actorBackingBefore, actorPairBacking,
            sizeof(actorPairBacking)) == 0,
        "complete NPhase report capture rejects an inactive-tail alias");
    overlapBuffers = buffers;
    overlapBuffers.actorPairBacking =
        reinterpret_cast<uintptr_t*>(actorPairObjects[0]);
    overlapReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &overlapBuffers,
            &overlapReceipt) == 0 && overlapReceipt.result == 10u &&
        memcmp(actorObjectBefore, actorPairObjects,
            sizeof(actorPairObjects)) == 0,
        "complete NPhase report capture rejects an active-object alias");

    NPhaseReportSnapshotReceiptV1 noWriteReceipt = {};
    memset(&noWriteReceipt, 0xA5, sizeof(noWriteReceipt));
    NPhaseReportSnapshotReceiptV1 noWriteBefore = noWriteReceipt;
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase),
            reinterpret_cast<const NPhaseReportSnapshotBuffersV1*>(nphase),
            &noWriteReceipt) == 0 &&
        memcmp(&noWriteReceipt, &noWriteBefore, sizeof(noWriteReceipt)) == 0 &&
        memcmp(nphaseBefore, nphase, sizeof(nphase)) == 0,
        "complete NPhase report capture rejects a source-aliased descriptor without writing");
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            reinterpret_cast<NPhaseReportSnapshotReceiptV1*>(nphase)) == 0 &&
        memcmp(nphaseBefore, nphase, sizeof(nphase)) == 0,
        "complete NPhase report capture rejects a source-aliased receipt without writing");

    *reinterpret_cast<uint32_t*>(nphase + 0x30) = 0u;
    *reinterpret_cast<uint32_t*>(nphase + 0x3C) = 0u;
    NPhaseReportSnapshotReceiptV1 pristineReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            &pristineReceipt) == 1 && pristineReceipt.result == 1u,
        "complete NPhase report capture accepts pristine last-index zero");
    *reinterpret_cast<uint32_t*>(nphase + 0x3C) = 0xFFFFFFFFu;
    NPhaseReportSnapshotReceiptV1 resetReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            &resetReceipt) == 1 && resetReceipt.result == 1u,
        "complete NPhase report capture accepts reset last-index sentinel");
    *reinterpret_cast<uint32_t*>(nphase + 0x3C) = 1u;
    NPhaseReportSnapshotReceiptV1 badLastReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            &badLastReceipt) == 0 && badLastReceipt.result == 5u,
        "complete NPhase report capture rejects an invalid empty last index");
    *reinterpret_cast<uint32_t*>(nphase + 0x30) = 19u;
    *reinterpret_cast<uint32_t*>(nphase + 0x3C) = 16u;

    actorPairBacking[1] = actorPairBacking[0];
    NPhaseReportSnapshotReceiptV1 duplicateReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            &duplicateReceipt) == 0 && duplicateReceipt.result == 7u,
        "complete NPhase report capture rejects duplicate live members");
    actorPairBacking[1] = actorBackingBefore[1];
    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x34) = 0u;
    NPhaseReportSnapshotReceiptV1 indexReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            &indexReceipt) == 0 && indexReceipt.result == 6u,
        "complete NPhase report capture rejects an incoherent SIP index");
    *reinterpret_cast<uint32_t*>(sipObjects[0] + 0x34) = 1u;

    const uintptr_t savedReportBuffer =
        *reinterpret_cast<uintptr_t*>(nphase + 0x2C);
    *reinterpret_cast<uintptr_t*>(nphase + 0x2C) =
        reinterpret_cast<uintptr_t>(actorPairBacking);
    NPhaseReportSnapshotReceiptV1 sourceLayoutReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            &sourceLayoutReceipt) == 0 &&
        sourceLayoutReceipt.result == 11u,
        "complete NPhase report capture rejects overlapping source ownership");
    *reinterpret_cast<uintptr_t*>(nphase + 0x2C) = savedReportBuffer;

    image[kReportSceneTimestampLayoutRva] ^= 1u;
    NPhaseReportSnapshotReceiptV1 revisionReceipt = {};
    Check(capture(reinterpret_cast<uintptr_t>(image),
            reinterpret_cast<uintptr_t>(nphase), &buffers,
            &revisionReceipt) == 0 && revisionReceipt.result == 3u,
        "complete NPhase report capture guards both Scene timestamp offsets");
    image[kReportSceneTimestampLayoutRva] ^= 1u;
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
    Check(receipt.result == 1 && receipt.apiVersion == kCurrentApiVersion &&
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

struct InteractionGraphFixture {
    uint8_t nphase[4];
    uint8_t ownerScene[0x4B8];
    uint8_t interactionScene[0x3F4];
    uint8_t actors[4][0x34];
    uint8_t interactions[6][0x20];
    uint8_t elements[12][0x20];
    uint8_t shapeCores[6][0x24];
    uintptr_t activeBodies[4];
    uintptr_t globalInteractions[6];
    uint8_t slab8[0x400];
    uint8_t slab16[0x800];
    uint8_t slab32[0x1000];
};

static void InitializeInteractionPointerPool(uint8_t* pool, uint8_t* slab,
    uint32_t blockBytes, uint32_t usedBlocks) {
    *reinterpret_cast<uintptr_t*>(pool + 0x04) =
        reinterpret_cast<uintptr_t>(slab);
    *reinterpret_cast<uint8_t*>(pool + 0x104) = 1;
    *reinterpret_cast<uintptr_t*>(pool + 0x108) =
        reinterpret_cast<uintptr_t>(pool + 0x04);
    *reinterpret_cast<uint32_t*>(pool + 0x10C) = 1;
    *reinterpret_cast<uint32_t*>(pool + 0x110) = 0x80000040u;
    *reinterpret_cast<uint32_t*>(pool + 0x114) = 32;
    *reinterpret_cast<uint32_t*>(pool + 0x118) = usedBlocks;
    // Intentionally differs from freeCount. It is deferred slab-reclamation
    // accounting, not a second expression of the free-chain length.
    *reinterpret_cast<uint32_t*>(pool + 0x11C) = 0xFFFFFFFDu;
    *reinterpret_cast<uint32_t*>(pool + 0x120) = blockBytes * 32u;
    *reinterpret_cast<uintptr_t*>(pool + 0x124) =
        reinterpret_cast<uintptr_t>(slab + usedBlocks * blockBytes);
    for (uint32_t i = usedBlocks; i < 32; ++i) {
        *reinterpret_cast<uintptr_t*>(slab + i * blockBytes) = i == 31 ?
            0 : reinterpret_cast<uintptr_t>(slab + (i + 1) * blockBytes);
    }
}

static void InitializeInteractionGraphFixture(InteractionGraphFixture& f) {
    ZeroMemory(&f, sizeof(f));
    const uintptr_t owner = reinterpret_cast<uintptr_t>(f.ownerScene);
    const uintptr_t scene = reinterpret_cast<uintptr_t>(f.interactionScene);
    *reinterpret_cast<uintptr_t*>(f.nphase) = owner;
    *reinterpret_cast<uintptr_t*>(f.ownerScene + 0x4B4) = scene;
    *reinterpret_cast<uintptr_t*>(f.interactionScene + 0x3E8) =
        reinterpret_cast<uintptr_t>(&f);
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x3EC) = 123;
    *reinterpret_cast<uintptr_t*>(f.interactionScene + 0x3F0) = owner;

    for (uint32_t i = 0; i < 4; ++i)
        f.activeBodies[i] = reinterpret_cast<uintptr_t>(f.actors[i]);
    *reinterpret_cast<uintptr_t*>(f.interactionScene) =
        reinterpret_cast<uintptr_t>(f.activeBodies);
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x04) = 4;
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x08) = 4;
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x0C) = 1;

    for (uint32_t i = 0; i < 6; ++i)
        f.globalInteractions[i] =
            reinterpret_cast<uintptr_t>(f.interactions[i]);
    for (uint32_t i = 0; i < 6; ++i) {
        *reinterpret_cast<uintptr_t*>(f.interactionScene + 0x10 + i * 12) =
            reinterpret_cast<uintptr_t>(f.globalInteractions + i);
        *reinterpret_cast<uint32_t*>(f.interactionScene + 0x14 + i * 12) = 1;
        *reinterpret_cast<uint32_t*>(f.interactionScene + 0x18 + i * 12) = 1;
        *reinterpret_cast<uint32_t*>(f.interactionScene + 0x58 + i * 4) = 1;
    }

    const uint32_t poolOffsets[3] = {0x70u, 0x198u, 0x2C0u};
    uint8_t* slabs[3] = {f.slab8, f.slab16, f.slab32};
    const uint32_t blockBytes[3] = {0x20u, 0x40u, 0x80u};
    const uint32_t usedBlocks[3] = {2u, 1u, 1u};
    for (uint32_t i = 0; i < 3; ++i)
        InitializeInteractionPointerPool(f.interactionScene + poolOffsets[i],
            slabs[i], blockBytes[i], usedBlocks[i]);

    for (uint32_t i = 0; i < 4; ++i) {
        uint8_t* actor = f.actors[i];
        *reinterpret_cast<uintptr_t*>(actor) =
            reinterpret_cast<uintptr_t>(actor + 0x30);
        *reinterpret_cast<uintptr_t*>(actor + 0x20) =
            reinterpret_cast<uintptr_t>(f.elements[i * 2]);
        *reinterpret_cast<uintptr_t*>(actor + 0x24) = scene;
        *reinterpret_cast<uint32_t*>(actor + 0x28) = i;
        *reinterpret_cast<uint16_t*>(actor + 0x2E) = 1;
        *reinterpret_cast<uint8_t*>(actor + 0x32) = 1;
        *reinterpret_cast<uint8_t*>(actor + 0x33) = i == 0 ? 3 : 5;
    }
    *reinterpret_cast<uint32_t*>(f.actors[0] + 0x1C) = 6;
    *reinterpret_cast<uint16_t*>(f.actors[0] + 0x2C) = 4;
    *reinterpret_cast<uint16_t*>(f.actors[0] + 0x30) = 2;
    *reinterpret_cast<uintptr_t*>(f.actors[1] + 0x14) =
        reinterpret_cast<uintptr_t>(f.slab8 + 0x20);
    *reinterpret_cast<uint32_t*>(f.actors[1] + 0x1C) = 2;
    *reinterpret_cast<uint32_t*>(f.actors[1] + 0x18) = 8;
    *reinterpret_cast<uint16_t*>(f.actors[1] + 0x2C) = 1;
    *reinterpret_cast<uint16_t*>(f.actors[1] + 0x30) = 1;
    *reinterpret_cast<uintptr_t*>(f.actors[2] + 0x14) =
        reinterpret_cast<uintptr_t>(f.slab16);
    *reinterpret_cast<uint32_t*>(f.actors[2] + 0x1C) = 2;
    *reinterpret_cast<uint32_t*>(f.actors[2] + 0x18) = 16;
    *reinterpret_cast<uint16_t*>(f.actors[2] + 0x2C) = 2;
    *reinterpret_cast<uint16_t*>(f.actors[2] + 0x30) = 1;
    *reinterpret_cast<uintptr_t*>(f.actors[3] + 0x14) =
        reinterpret_cast<uintptr_t>(f.slab32);
    *reinterpret_cast<uint32_t*>(f.actors[3] + 0x1C) = 2;
    *reinterpret_cast<uint32_t*>(f.actors[3] + 0x18) = 32;
    *reinterpret_cast<uint16_t*>(f.actors[3] + 0x2C) = 1;
    *reinterpret_cast<uint16_t*>(f.actors[3] + 0x30) = 0;

    *reinterpret_cast<uintptr_t*>(f.actors[0] + 0x14) =
        reinterpret_cast<uintptr_t>(f.slab8);
    *reinterpret_cast<uint32_t*>(f.actors[0] + 0x18) = 8;
    const uint32_t actor0Order[6] = {0u, 1u, 4u, 5u, 2u, 3u};
    for (uint32_t i = 0; i < 6; ++i)
        *reinterpret_cast<uintptr_t*>(f.slab8 + i * 4) =
            reinterpret_cast<uintptr_t>(f.interactions[actor0Order[i]]);
    *reinterpret_cast<uintptr_t*>(f.slab8 + 0x20) =
        reinterpret_cast<uintptr_t>(f.interactions[0]);
    *reinterpret_cast<uintptr_t*>(f.slab8 + 0x24) =
        reinterpret_cast<uintptr_t>(f.interactions[3]);
    *reinterpret_cast<uintptr_t*>(f.slab16) =
        reinterpret_cast<uintptr_t>(f.interactions[1]);
    *reinterpret_cast<uintptr_t*>(f.slab16 + 0x04) =
        reinterpret_cast<uintptr_t>(f.interactions[4]);
    *reinterpret_cast<uintptr_t*>(f.slab32) =
        reinterpret_cast<uintptr_t>(f.interactions[5]);
    *reinterpret_cast<uintptr_t*>(f.slab32 + 0x04) =
        reinterpret_cast<uintptr_t>(f.interactions[2]);

    const uint32_t peerActors[6] = {1u, 2u, 3u, 1u, 2u, 3u};
    const uint16_t actor0Ids[6] = {0u, 1u, 4u, 5u, 2u, 3u};
    const uint16_t peerIds[6] = {0u, 0u, 1u, 1u, 1u, 0u};
    for (uint32_t i = 0; i < 6; ++i) {
        *reinterpret_cast<uintptr_t*>(f.elements[i * 2] + 0x08) =
            reinterpret_cast<uintptr_t>(f.actors[0]);
        *reinterpret_cast<uintptr_t*>(f.elements[i * 2 + 1] + 0x08) =
            reinterpret_cast<uintptr_t>(f.actors[peerActors[i]]);
    }
    const uint32_t shapeElementIndices[6] = {0u,1u,4u,5u,6u,7u};
    for (uint32_t i = 0; i < 6u; ++i)
        *reinterpret_cast<uintptr_t*>(
            f.elements[shapeElementIndices[i]] + 0x1C) =
                reinterpret_cast<uintptr_t>(f.shapeCores[i]);
    for (uint32_t i = 0; i < 6u; ++i)
        *reinterpret_cast<uintptr_t*>(f.shapeCores[i] + 0x20) =
            reinterpret_cast<uintptr_t>(f.shapeCores[i] + 0x20);
    for (uint32_t i = 0; i < 6; ++i) {
        uint8_t* interaction = f.interactions[i];
        *reinterpret_cast<uintptr_t*>(interaction) =
            reinterpret_cast<uintptr_t>(interaction + 0x1C);
        *reinterpret_cast<uintptr_t*>(interaction + 0x04) =
            reinterpret_cast<uintptr_t>(f.actors[0]);
        *reinterpret_cast<uintptr_t*>(interaction + 0x08) =
            reinterpret_cast<uintptr_t>(f.actors[peerActors[i]]);
        *reinterpret_cast<uint32_t*>(interaction + 0x0C) = 0;
        *reinterpret_cast<uint16_t*>(interaction + 0x10) = actor0Ids[i];
        *reinterpret_cast<uint16_t*>(interaction + 0x12) = peerIds[i];
        *reinterpret_cast<uint8_t*>(interaction + 0x14) =
            static_cast<uint8_t>(i);
        *reinterpret_cast<uint8_t*>(interaction + 0x15) = i == 0 ? 0x11 : 1;
        *reinterpret_cast<uintptr_t*>(interaction + 0x18) =
            reinterpret_cast<uintptr_t>(f.elements[i * 2]);
        *reinterpret_cast<uintptr_t*>(interaction + 0x1C) =
            reinterpret_cast<uintptr_t>(f.elements[i * 2 + 1]);
    }
}

struct InteractionGraphMutation {
    volatile LONG run;
    volatile LONG* timestamp;
};

static DWORD WINAPI MutateInteractionGraphTimestamp(void* value) {
    InteractionGraphMutation* mutation =
        static_cast<InteractionGraphMutation*>(value);
    while (InterlockedCompareExchange(&mutation->run, 1, 1) == 1)
        InterlockedIncrement(mutation->timestamp);
    return 0;
}

static void RunInteractionGraphTests(uint8_t* image,
    CaptureInteractionGraph capture) {
    InteractionGraphFixture fixture = {};
    InitializeInteractionGraphFixture(fixture);
    InteractionGraphFixture before = {};
    CopyMemory(&before, &fixture, sizeof(fixture));
    uintptr_t active[4] = {};
    InteractionGraphActorRecord actors[4] = {};
    InteractionGraphInteractionRecord interactions[6] = {};
    uintptr_t slots[12] = {};
    uintptr_t slabs[3] = {};
    uintptr_t freeBlocks[92] = {};
    InteractionGraphReceipt receipt = {};
    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(fixture.nphase);
    const auto invoke = [&](InteractionGraphReceipt* output,
        uint32_t activeCapacity, uint32_t actorCapacity,
        uint32_t interactionCapacity, uint32_t slotCapacity,
        uint32_t slabCapacity, uint32_t freeCapacity) {
        return capture(imagePointer, nphase, active, activeCapacity,
            actors, actorCapacity, interactions, interactionCapacity,
            slots, slotCapacity, slabs, slabCapacity, freeBlocks,
            freeCapacity, output);
    };

    const int firstCapture = invoke(&receipt, 4, 4, 6, 12, 3, 92);
    if (!firstCapture)
        printf("interaction graph diagnostic: result=%u kind=%u index=%u detail=%u error=%u\n",
            receipt.result, receipt.invalidKind, receipt.invalidIndex,
            receipt.detail, receipt.lastError);
    Check(firstCapture == 1,
        "interaction graph capture succeeds");
    Check(receipt.apiVersion == kCurrentApiVersion &&
        receipt.structSize == sizeof(receipt) &&
        receipt.result == 1 && receipt.validationFlags == 0xFF &&
        receipt.activeBodiesWritten == 4 && receipt.actorsWritten == 4 &&
        receipt.interactionsWritten == 6 && receipt.actorSlotsWritten == 12 &&
        receipt.poolSlabsWritten == 3 && receipt.poolFreeWritten == 92,
        "interaction graph receipt proves complete caller-owned projection");
    Check(Same(active, fixture.activeBodies, 4) &&
        interactions[0].semanticLow ==
            reinterpret_cast<uintptr_t>(fixture.shapeCores[0] + 0x20) &&
        interactions[0].semanticHigh ==
            reinterpret_cast<uintptr_t>(fixture.shapeCores[1] + 0x20) &&
        interactions[2].semanticLow ==
            reinterpret_cast<uintptr_t>(fixture.shapeCores[2] + 0x20) &&
        interactions[2].semanticHigh ==
            reinterpret_cast<uintptr_t>(fixture.shapeCores[3] + 0x20) &&
        interactions[3].semanticLow ==
            reinterpret_cast<uintptr_t>(fixture.shapeCores[4] + 0x20) &&
        interactions[3].semanticHigh ==
            reinterpret_cast<uintptr_t>(fixture.shapeCores[5] + 0x20),
        "interaction graph retains contact, trigger, and marker semantic keys");
    bool flattened = true;
    for (uint32_t i = 0; i < 6; ++i)
        flattened = flattened && interactions[i].interactionType == i &&
            interactions[i].interaction == fixture.globalInteractions[i];
    Check(flattened,
        "interaction graph flattens all six global type arrays in order");
    Check(actors[0].validationFlags == 0x7F &&
        interactions[0].validationFlags == 0x1F &&
        receipt.pools[0].validationFlags == 0x7F &&
        receipt.pools[1].validationFlags == 0x7F &&
        receipt.pools[2].validationFlags == 0x7F &&
        receipt.pools[0].freeCount == 30 &&
        receipt.pools[0].unreleasedFree == 0xFFFFFFFDu,
        "interaction graph validates indices, active prefixes, and pool ownership");
    Check(memcmp(&before, &fixture, sizeof(fixture)) == 0,
        "interaction graph capture is non-mutating");

    uintptr_t repeatedActive[4] = {};
    InteractionGraphActorRecord repeatedActors[4] = {};
    InteractionGraphInteractionRecord repeatedInteractions[6] = {};
    uintptr_t repeatedSlots[12] = {};
    uintptr_t repeatedSlabs[3] = {};
    uintptr_t repeatedFree[92] = {};
    InteractionGraphReceipt repeated = {};
    Check(capture(imagePointer, nphase, repeatedActive, 4, repeatedActors, 4,
        repeatedInteractions, 6, repeatedSlots, 12, repeatedSlabs, 3,
        repeatedFree, 92, &repeated) == 1 &&
        memcmp(&receipt, &repeated, sizeof(receipt)) == 0 &&
        memcmp(active, repeatedActive, sizeof(active)) == 0 &&
        memcmp(actors, repeatedActors, sizeof(actors)) == 0 &&
        memcmp(interactions, repeatedInteractions, sizeof(interactions)) == 0 &&
        memcmp(slots, repeatedSlots, sizeof(slots)) == 0 &&
        memcmp(slabs, repeatedSlabs, sizeof(slabs)) == 0 &&
        memcmp(freeBlocks, repeatedFree, sizeof(freeBlocks)) == 0,
        "interaction graph capture is byte-repeatable");

    InteractionGraphReceipt shortReceipt = {};
    Check(invoke(&shortReceipt, 3, 4, 6, 12, 3, 92) == 0 &&
        shortReceipt.result == 6 && shortReceipt.activeBodiesRequired == 4 &&
        shortReceipt.poolFreeRequired == 92,
        "interaction graph capture reports all required capacities");

    *reinterpret_cast<uint16_t*>(fixture.interactions[0] + 0x10) = 1;
    InteractionGraphReceipt cachedIndexReceipt = {};
    Check(invoke(&cachedIndexReceipt, 4, 4, 6, 12, 3, 92) == 0 &&
        cachedIndexReceipt.result == 9,
        "interaction graph rejects a corrupt bilateral cached actor slot");
    *reinterpret_cast<uint16_t*>(fixture.interactions[0] + 0x10) = 0;

    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x70 + 0x124) =
        reinterpret_cast<uintptr_t>(fixture.slab8);
    InteractionGraphReceipt poolReceipt = {};
    Check(invoke(&poolReceipt, 4, 4, 6, 12, 3, 92) == 0 &&
        poolReceipt.result == 10,
        "interaction graph rejects a corrupt pointer-pool free chain");
    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x70 + 0x124) =
        reinterpret_cast<uintptr_t>(fixture.slab8 + 0x40);

    InteractionGraphMutation mutation = {1,
        reinterpret_cast<volatile LONG*>(fixture.interactionScene + 0x3EC)};
    HANDLE thread = CreateThread(0, 0, MutateInteractionGraphTimestamp,
        &mutation, 0, 0);
    Check(thread != 0, "interaction graph mutation thread starts");
    if (thread) {
        Sleep(10);
        InteractionGraphReceipt unstableReceipt = {};
        const int unstable = invoke(&unstableReceipt, 4, 4, 6, 12, 3, 92);
        InterlockedExchange(&mutation.run, 0);
        WaitForSingleObject(thread, 10000);
        CloseHandle(thread);
        Check(unstable == 0 && unstableReceipt.result == 11 &&
            unstableReceipt.lastError == ERROR_RETRY,
            "interaction graph repeat-read rejects a torn sample with ERROR_RETRY");
    }
    *reinterpret_cast<uint32_t*>(fixture.interactionScene + 0x3EC) = 123;

    image[kInteractionActorRegisterRva] ^= 1;
    InteractionGraphReceipt revisionReceipt = {};
    Check(invoke(&revisionReceipt, 4, 4, 6, 12, 3, 92) == 0 &&
        revisionReceipt.result == 3,
        "interaction graph capture fails closed on a revision mismatch");
    image[kInteractionActorRegisterRva] ^= 1;
}

struct TransformBroadPhaseFixture {
    uint8_t nphase[4];
    uint8_t ownerScene[0x4B8];
    uint8_t interactionScene[0x3F4];
    uint8_t context[0x1DE4];
    uint8_t aabbManager[0xC2BC];
    uint8_t actors[6][0x34];
    uint8_t sips[3][0x3C];
    uint8_t managers[2][0x80];
    uint8_t shapeSims[6][0x20];
    uint8_t shapeCores[6][0x24];
    uintptr_t globalInteractions[3];
    float transforms[4][7];
    uint32_t refCounts[4];
    uint32_t freeIds[4];
    uintptr_t createdOverlaps[2];
    uintptr_t deletedOverlaps[2];
};

static void InitializeTransformBroadPhaseFixture(
    TransformBroadPhaseFixture& f) {
    ZeroMemory(&f, sizeof(f));
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(f.nphase);
    const uintptr_t owner = reinterpret_cast<uintptr_t>(f.ownerScene);
    const uintptr_t interactionScene =
        reinterpret_cast<uintptr_t>(f.interactionScene);
    const uintptr_t context = reinterpret_cast<uintptr_t>(f.context);
    const uintptr_t aabb = reinterpret_cast<uintptr_t>(f.aabbManager);
    *reinterpret_cast<uintptr_t*>(f.nphase) = owner;
    *reinterpret_cast<uintptr_t*>(f.ownerScene + 0x450) = nphase;
    *reinterpret_cast<uintptr_t*>(f.ownerScene + 0x4B4) = interactionScene;
    *reinterpret_cast<uintptr_t*>(f.interactionScene + 0x3E8) = context;
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x3EC) = 700;
    *reinterpret_cast<uintptr_t*>(f.interactionScene + 0x3F0) = owner;
    *reinterpret_cast<uintptr_t*>(f.context + 0x08) = aabb;

    uint8_t* cache = f.context + 0x1DBC;
    *reinterpret_cast<uint32_t*>(cache) = 3;
    *reinterpret_cast<uintptr_t*>(cache + 0x04) =
        reinterpret_cast<uintptr_t>(f.freeIds);
    *reinterpret_cast<uint32_t*>(cache + 0x08) = 1;
    *reinterpret_cast<uint32_t*>(cache + 0x0C) = 4;
    *reinterpret_cast<uintptr_t*>(cache + 0x10) =
        reinterpret_cast<uintptr_t>(f.transforms);
    *reinterpret_cast<uint32_t*>(cache + 0x14) = 4;
    *reinterpret_cast<uint32_t*>(cache + 0x18) = 4;
    *reinterpret_cast<uintptr_t*>(cache + 0x1C) =
        reinterpret_cast<uintptr_t>(f.refCounts);
    *reinterpret_cast<uint32_t*>(cache + 0x20) = 4;
    *reinterpret_cast<uint32_t*>(cache + 0x24) = 4;
    f.freeIds[0] = 1;
    f.refCounts[0] = 2;
    f.refCounts[2] = 2;
    for (uint32_t id = 0; id < 4; ++id) {
        f.transforms[id][3] = 1.0f;
        f.transforms[id][4] = static_cast<float>(id + 1u);
        f.transforms[id][5] = static_cast<float>(id + 2u);
        f.transforms[id][6] = static_cast<float>(id + 3u);
    }

    *reinterpret_cast<uintptr_t*>(f.interactionScene + 0x10) =
        reinterpret_cast<uintptr_t>(f.globalInteractions);
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x14) = 3;
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x18) = 3;
    *reinterpret_cast<uint32_t*>(f.interactionScene + 0x58) = 3;
    for (uint32_t i = 0; i < 3; ++i) {
        uint8_t* interaction = f.sips[i] + 8;
        f.globalInteractions[i] =
            reinterpret_cast<uintptr_t>(interaction);
        const uint32_t endpoint0 = i * 2u;
        const uint32_t endpoint1 = endpoint0 + 1u;
        *reinterpret_cast<uintptr_t*>(interaction) =
            reinterpret_cast<uintptr_t>(interaction + 0x1C);
        *reinterpret_cast<uintptr_t*>(interaction + 0x04) =
            reinterpret_cast<uintptr_t>(f.actors[endpoint0]);
        *reinterpret_cast<uintptr_t*>(interaction + 0x08) =
            reinterpret_cast<uintptr_t>(f.actors[endpoint1]);
        *reinterpret_cast<uint32_t*>(interaction + 0x0C) = i;
        *reinterpret_cast<uint16_t*>(interaction + 0x10) = 0;
        *reinterpret_cast<uint16_t*>(interaction + 0x12) = 0;
        *reinterpret_cast<uint8_t*>(interaction + 0x14) = 0;
        *reinterpret_cast<uint8_t*>(interaction + 0x15) = 0x10;
        *reinterpret_cast<uintptr_t*>(interaction + 0x18) =
            reinterpret_cast<uintptr_t>(f.shapeSims[endpoint0]);
        *reinterpret_cast<uintptr_t*>(interaction + 0x1C) =
            reinterpret_cast<uintptr_t>(f.shapeSims[endpoint1]);
        if (i < 2) {
            const uintptr_t manager = reinterpret_cast<uintptr_t>(
                f.managers[i]);
            *reinterpret_cast<uintptr_t*>(interaction + 0x30) = manager;
            *reinterpret_cast<uintptr_t*>(f.managers[i] + 0x0C) =
                reinterpret_cast<uintptr_t>(f.sips[i]);
            *reinterpret_cast<uintptr_t*>(f.managers[i] + 0x58) =
                reinterpret_cast<uintptr_t>(f.shapeCores[endpoint0] + 0x20);
            *reinterpret_cast<uintptr_t*>(f.managers[i] + 0x5C) =
                reinterpret_cast<uintptr_t>(f.shapeCores[endpoint1] + 0x20);
        }
    }
    const uint32_t cacheIds[6] = {
        0u, 2u, 0u, 2u, 0xFFFFFFFFu, 0xFFFFFFFFu
    };
    for (uint32_t i = 0; i < 6; ++i) {
        *reinterpret_cast<uintptr_t*>(f.shapeSims[i] + 0x08) =
            reinterpret_cast<uintptr_t>(f.actors[i]);
        *reinterpret_cast<uint32_t*>(f.shapeSims[i] + 0x18) = cacheIds[i];
        *reinterpret_cast<uintptr_t*>(f.shapeSims[i] + 0x1C) =
            reinterpret_cast<uintptr_t>(f.shapeCores[i]);
        *reinterpret_cast<uintptr_t*>(f.shapeCores[i] + 0x20) =
            reinterpret_cast<uintptr_t>(f.shapeCores[i] + 0x20);
    }
    for (uint32_t i = 0; i < 2; ++i) {
        *reinterpret_cast<uint32_t*>(f.managers[i] + 0x74) =
            cacheIds[i * 2u];
        *reinterpret_cast<uint32_t*>(f.managers[i] + 0x78) =
            cacheIds[i * 2u + 1u];
    }

    f.createdOverlaps[0] = reinterpret_cast<uintptr_t>(f.shapeSims[0]);
    f.createdOverlaps[1] = reinterpret_cast<uintptr_t>(f.shapeSims[1]);
    f.deletedOverlaps[0] = reinterpret_cast<uintptr_t>(f.shapeSims[2]);
    f.deletedOverlaps[1] = reinterpret_cast<uintptr_t>(f.shapeSims[3]);
    *reinterpret_cast<uintptr_t*>(f.aabbManager + 0xC2A8) =
        reinterpret_cast<uintptr_t>(f.createdOverlaps);
    *reinterpret_cast<uint32_t*>(f.aabbManager + 0xC2AC) = 1;
    *reinterpret_cast<uintptr_t*>(f.aabbManager + 0xC2B4) =
        reinterpret_cast<uintptr_t>(f.deletedOverlaps);
    *reinterpret_cast<uint32_t*>(f.aabbManager + 0xC2B8) = 1;
}

static void RunTransformCacheTests(uint8_t* image,
    CaptureTransformCache capture) {
    TransformBroadPhaseFixture fixture = {};
    InitializeTransformBroadPhaseFixture(fixture);
    TransformBroadPhaseFixture before = {};
    CopyMemory(&before, &fixture, sizeof(fixture));
    TransformCacheEntryRecord entries[4] = {};
    uint32_t freeIds[4] = {};
    TransformCacheBindingRecord bindings[4] = {};
    TransformCacheReceipt receipt = {};
    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(fixture.nphase);
    const auto invoke = [&](TransformCacheReceipt* output,
        uint32_t entryCapacity, uint32_t freeCapacity,
        uint32_t bindingCapacity) {
        return capture(imagePointer, nphase, entries, entryCapacity,
            freeIds, freeCapacity, bindings, bindingCapacity, output);
    };

    const int captured = invoke(&receipt, 4, 4, 4);
    if (!captured)
        printf("transform cache diagnostic: result=%u kind=%u index=%u detail=%u error=%u\n",
            receipt.result, receipt.invalidKind, receipt.invalidIndex,
            receipt.detail, receipt.lastError);
    Check(captured == 1 && receipt.apiVersion == kCurrentApiVersion &&
        receipt.structSize == sizeof(receipt) && receipt.result == 1 &&
        receipt.validationFlags == 0xFF && receipt.currentId == 3 &&
        receipt.entriesWritten == 3 && receipt.freeWritten == 1 &&
        receipt.bindingsRequired == 4 && receipt.bindingsWritten == 4 &&
        receipt.liveCount == 2 &&
        receipt.totalRefCount == 4,
        "transform cache captures only manager-backed settled state");
    Check(freeIds[0] == 1 && entries[0].bindingCount == 2 &&
        entries[0].stateFlags == 5 && entries[1].stateFlags == 3 &&
        entries[2].bindingCount == 2 &&
        bindings[0].shapeSim ==
            reinterpret_cast<uintptr_t>(fixture.shapeSims[0]) &&
        bindings[0].cacheId == 0 && bindings[1].cacheId == 2 &&
        bindings[2].cacheId == 0 && bindings[3].cacheId == 2,
        "transform cache preserves LIFO free order and oriented bindings");
    Check(memcmp(&before, &fixture, sizeof(fixture)) == 0,
        "transform cache capture is non-mutating");

    TransformCacheEntryRecord repeatedEntries[4] = {};
    uint32_t repeatedFree[4] = {};
    TransformCacheBindingRecord repeatedBindings[4] = {};
    TransformCacheReceipt repeated = {};
    Check(capture(imagePointer, nphase, repeatedEntries, 4, repeatedFree, 4,
        repeatedBindings, 4, &repeated) == 1 &&
        memcmp(&receipt, &repeated, sizeof(receipt)) == 0 &&
        memcmp(entries, repeatedEntries, sizeof(entries)) == 0 &&
        memcmp(freeIds, repeatedFree, sizeof(freeIds)) == 0 &&
        memcmp(bindings, repeatedBindings, sizeof(bindings)) == 0,
        "transform cache capture is byte-repeatable");

    TransformCacheReceipt shortReceipt = {};
    Check(invoke(&shortReceipt, 2, 0, 3) == 0 &&
        shortReceipt.result == 6 && shortReceipt.entriesRequired == 3 &&
        shortReceipt.freeRequired == 1 && shortReceipt.bindingsRequired == 4,
        "transform cache reports all required caller capacities");

    fixture.freeIds[0] = 0;
    TransformCacheReceipt freeReceipt = {};
    Check(invoke(&freeReceipt, 4, 4, 4) == 0 &&
        freeReceipt.result == 7,
        "transform cache rejects a referenced ID in the free stack");
    fixture.freeIds[0] = 1;

    fixture.refCounts[0] = 1;
    TransformCacheReceipt referenceReceipt = {};
    Check(invoke(&referenceReceipt, 4, 4, 4) == 0 &&
        referenceReceipt.result == 9,
        "transform cache rejects binding/reference-count mismatch");
    fixture.refCounts[0] = 2;

    const uintptr_t managerBacklink = *reinterpret_cast<uintptr_t*>(
        fixture.managers[0] + 0x0C);
    *reinterpret_cast<uintptr_t*>(fixture.managers[0] + 0x0C) =
        managerBacklink + 4u;
    TransformCacheReceipt backlinkReceipt = {};
    Check(invoke(&backlinkReceipt, 4, 4, 4) == 0 &&
        backlinkReceipt.result == 8,
        "transform cache rejects a manager with the wrong SIP backlink");
    *reinterpret_cast<uintptr_t*>(fixture.managers[0] + 0x0C) =
        managerBacklink;

    const uintptr_t managerShape = *reinterpret_cast<uintptr_t*>(
        fixture.managers[0] + 0x58);
    *reinterpret_cast<uintptr_t*>(fixture.managers[0] + 0x58) =
        managerShape + 4u;
    TransformCacheReceipt managerShapeReceipt = {};
    Check(invoke(&managerShapeReceipt, 4, 4, 4) == 0 &&
        managerShapeReceipt.result == 8,
        "transform cache rejects a manager/shape identity mismatch");
    *reinterpret_cast<uintptr_t*>(fixture.managers[0] + 0x58) = managerShape;

    image[kCreateManagerTransformCacheLayoutRva] ^= 1;
    TransformCacheReceipt revisionReceipt = {};
    Check(invoke(&revisionReceipt, 4, 4, 4) == 0 &&
        revisionReceipt.result == 3,
        "transform cache fails closed on layout revision mismatch");
    image[kCreateManagerTransformCacheLayoutRva] ^= 1;
}

static void RunFinishBroadPhaseObserverTests(uint8_t* image, HMODULE library,
    FinishBroadPhaseAction installObserver,
    FinishBroadPhaseAction statusObserver,
    FinishBroadPhaseArm armObserver,
    FinishBroadPhaseCopy copyObserver,
    FinishBroadPhaseAction cancelObserver,
    FinishBroadPhaseAction uninstallObserver) {
    TransformBroadPhaseFixture fixture = {};
    InitializeTransformBroadPhaseFixture(fixture);
    const uintptr_t imagePointer = reinterpret_cast<uintptr_t>(image);
    const uintptr_t scene = reinterpret_cast<uintptr_t>(fixture.ownerScene);
    const uintptr_t context = reinterpret_cast<uintptr_t>(fixture.context);
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(fixture.nphase);
    FakeFinishBroadPhase finish = reinterpret_cast<FakeFinishBroadPhase>(
        imagePointer + kFinishBroadPhaseRva);

    DWORD* virtualProtectImport = FindVirtualProtectImport(library);
    const DWORD originalVirtualProtect = virtualProtectImport ?
        *virtualProtectImport : 0;
    g_realVirtualProtect = reinterpret_cast<VirtualProtectFunction>(
        static_cast<uintptr_t>(originalVirtualProtect));
    InterlockedExchange(&g_virtualProtectCallCount, 0);
    const bool injected = originalVirtualProtect && WriteImportFunction(
        virtualProtectImport, reinterpret_cast<uintptr_t>(
            FailSecondVirtualProtect));
    FinishBroadPhaseObserverReceipt failureReceipt = {};
    const int failedInstall = injected ? installObserver(imagePointer,
        &failureReceipt) : 1;
    const bool importRestored = injected && WriteImportFunction(
        virtualProtectImport, originalVirtualProtect);
    Check(injected && importRestored && failedInstall == 0 &&
        failureReceipt.result == 7 && failureReceipt.installed == 1 &&
        failureReceipt.state == 0 && image[kFinishBroadPhaseRva] == 0xE9,
        "landed detour survives post-write protection-restore failure");

    FinishBroadPhaseObserverReceipt receipt = {};
    Check(installObserver(imagePointer, &receipt) == 1 &&
        receipt.apiVersion == kCurrentApiVersion &&
        receipt.structSize == sizeof(receipt) &&
        receipt.result == 1 && receipt.installed == 1 && receipt.state == 1,
        "finishBroadPhase observer reactivates landed dormant detour");

    receipt = {};
    Check(armObserver(imagePointer, scene, context, nphase, 0, &receipt) == 1 &&
        receipt.result == 1 && receipt.state == 2 &&
        receipt.armedOrdinal == 1,
        "finishBroadPhase observer arms exact pass-zero identity");

    // Model the two writes performed by the synthetic original and compare
    // the entire fixture after the hooked call.  Any additional hook write
    // would make this byte comparison fail.
    TransformBroadPhaseFixture expected = {};
    CopyMemory(&expected, &fixture, sizeof(expected));
    ++*reinterpret_cast<uint32_t*>(expected.interactionScene + 0x3EC);
    ++*reinterpret_cast<uint32_t*>(expected.transforms[0]);
    finish(fixture.ownerScene, 0);
    Check(memcmp(&fixture, &expected, sizeof(fixture)) == 0,
        "finishBroadPhase hook is a data-pass-through around the original");

    FinishBroadPhaseObserverReceipt status = {};
    Check(statusObserver(imagePointer, &status) == 1 &&
        status.result == 1 && status.state == 4 &&
        status.armedOrdinal == 1 && status.observationOrdinal == 1 &&
        status.createdRequired == 1 && status.deletedRequired == 1 &&
        status.expectedScene == scene && status.observedScene == scene &&
        status.expectedContext == context && status.observedContext == context &&
        status.expectedNPhaseCore == nphase &&
        status.observedNPhaseCore == nphase && status.pass == 0 &&
        status.preCacheHash != status.postCacheHash &&
        status.preGraphHash != status.postGraphHash &&
        status.validationFlags == 0x3FF,
        "finishBroadPhase status returns the committed exact observation");

    FinishBroadPhaseObserverReceipt shortReceipt = {};
    Check(copyObserver(imagePointer, 1, 0, 0, 0, 0,
        &shortReceipt) == 0 && shortReceipt.result == 12 &&
        shortReceipt.createdRequired == 1 && shortReceipt.deletedRequired == 1,
        "finishBroadPhase copy reports exact required capacities");
    BroadPhaseOverlapRecord created[1] = {};
    BroadPhaseOverlapRecord deleted[1] = {};
    FinishBroadPhaseObserverReceipt copyReceipt = {};
    Check(copyObserver(imagePointer, 1, created, 1, deleted, 1,
        &copyReceipt) == 1 && copyReceipt.result == 1 &&
        copyReceipt.createdWritten == 1 && copyReceipt.deletedWritten == 1 &&
        created[0].userData0 ==
            reinterpret_cast<uintptr_t>(fixture.shapeSims[0]) &&
        created[0].userData1 ==
            reinterpret_cast<uintptr_t>(fixture.shapeSims[1]) &&
        deleted[0].userData0 ==
            reinterpret_cast<uintptr_t>(fixture.shapeSims[2]) &&
        deleted[0].userData1 ==
            reinterpret_cast<uintptr_t>(fixture.shapeSims[3]) &&
        created[0].validationFlags == 0x0F &&
        deleted[0].validationFlags == 0x0F,
        "finishBroadPhase copy preserves ordered oriented overlap rows");

    // Four additional observations overwrite ring slot zero.  Exact ordinal
    // copy must reject the retained-looking first buffers as stale.
    uint32_t latestOrdinal = 1;
    for (uint32_t i = 0; i < 4; ++i) {
        receipt = {};
        Check(armObserver(imagePointer, scene, context, nphase, 0,
            &receipt) == 1, "finishBroadPhase observer rearms");
        latestOrdinal = receipt.armedOrdinal;
        finish(fixture.ownerScene, 0);
    }
    FinishBroadPhaseObserverReceipt staleReceipt = {};
    Check(latestOrdinal == 5 &&
        copyObserver(imagePointer, 1, created, 1, deleted, 1,
            &staleReceipt) == 0 && staleReceipt.result == 11,
        "finishBroadPhase ring rejects overwritten observation ordinal");

    *reinterpret_cast<uint32_t*>(fixture.aabbManager + 0xC2AC) = 4097;
    receipt = {};
    Check(armObserver(imagePointer, scene, context, nphase, 0,
        &receipt) == 1 && receipt.armedOrdinal == 6,
        "finishBroadPhase observer arms capacity-error observation");
    finish(fixture.ownerScene, 0);
    status = {};
    Check(statusObserver(imagePointer, &status) == 0 &&
        status.result == 12 && status.state == 5 &&
        status.observationOrdinal == 6 && status.createdRequired == 4097,
        "finishBroadPhase status returns committed capacity failure");
    *reinterpret_cast<uint32_t*>(fixture.aabbManager + 0xC2AC) = 1;

    receipt = {};
    Check(armObserver(imagePointer, scene, context, nphase, 0,
        &receipt) == 1 && cancelObserver(imagePointer, &receipt) == 1 &&
        receipt.state == 1,
        "finishBroadPhase pending observation can be cancelled");
    finish(fixture.ownerScene, 0);
    status = {};
    Check(statusObserver(imagePointer, &status) == 1 &&
        status.state == 1 && status.observationOrdinal == 6,
        "cancelled observer remains dormant while original still runs");

    receipt = {};
    Check(uninstallObserver(imagePointer, &receipt) == 1 &&
        receipt.result == 1 && receipt.installed == 1 && receipt.state == 0 &&
        image[kFinishBroadPhaseRva] == 0xE9,
        "finishBroadPhase observer uninstalls to a resident dormant hook");

    receipt = {};
    Check(installObserver(imagePointer, &receipt) == 1 &&
        receipt.installed == 1 && receipt.state == 1,
        "finishBroadPhase observer reinstalls with retained trampoline");
    g_finishPauseEntered = CreateEventA(0, TRUE, FALSE, 0);
    g_finishPauseRelease = CreateEventA(0, TRUE, FALSE, 0);
    InterlockedExchange(&g_finishPauseClaimed, 0);
    receipt = {};
    Check(g_finishPauseEntered && g_finishPauseRelease &&
        armObserver(imagePointer, scene, context, nphase, 0,
            &receipt) == 1,
        "finishBroadPhase concurrent-owner observation arms");
    FinishBroadPhaseWorkerCall firstCall = {
        finish, fixture.ownerScene, 0, 0
    };
    HANDLE firstThread = CreateThread(0, 0, RunFinishBroadPhaseWorker,
        &firstCall, 0, 0);
    const DWORD entered = g_finishPauseEntered ? WaitForSingleObject(
        g_finishPauseEntered, 10000) : WAIT_FAILED;
    FinishBroadPhaseWorkerCall secondCall = {
        finish, fixture.ownerScene, 0, 0
    };
    HANDLE secondThread = entered == WAIT_OBJECT_0 ? CreateThread(0, 0,
        RunFinishBroadPhaseWorker, &secondCall, 0, 0) : 0;
    const DWORD secondDone = secondThread ? WaitForSingleObject(
        secondThread, 10000) : WAIT_FAILED;
    status = {};
    Check(firstThread && entered == WAIT_OBJECT_0 && secondThread &&
        secondDone == WAIT_OBJECT_0 &&
        statusObserver(imagePointer, &status) == 1 && status.state == 3,
        "token-zero concurrent exit cannot finalize the owner observation");
    if (g_finishPauseRelease) SetEvent(g_finishPauseRelease);
    const DWORD firstDone = firstThread ? WaitForSingleObject(firstThread,
        10000) : WAIT_FAILED;
    status = {};
    Check(firstDone == WAIT_OBJECT_0 &&
        statusObserver(imagePointer, &status) == 1 && status.state == 4 &&
        status.observationOrdinal == 1 && status.armedThreadId != 0u &&
        status.threadId != 0u &&
        status.armedThreadId != status.threadId &&
        status.armedThreadId == GetCurrentThreadId() &&
        status.threadId == firstCall.threadId,
        "owner exit commits the concurrent observation exactly once");
    if (firstThread) CloseHandle(firstThread);
    if (secondThread) CloseHandle(secondThread);
    if (g_finishPauseEntered) CloseHandle(g_finishPauseEntered);
    if (g_finishPauseRelease) CloseHandle(g_finishPauseRelease);
    g_finishPauseEntered = 0;
    g_finishPauseRelease = 0;
    receipt = {};
    Check(uninstallObserver(imagePointer, &receipt) == 1 &&
        receipt.installed == 1 && receipt.state == 0 &&
        image[kFinishBroadPhaseRva] == 0xE9,
        "reinstalled observer returns to its resident dormant hook");

    image[kFinishBroadPhaseRva + 0x0C] ^= 1;
    receipt = {};
    Check(installObserver(imagePointer, &receipt) == 0 &&
        receipt.result == 3,
        "finishBroadPhase observer fails closed on layout revision mismatch");
    image[kFinishBroadPhaseRva + 0x0C] ^= 1;
}

struct SyntheticIslandFixture {
    uint8_t nphase[8];
    uint8_t ownerScene[0x4C0];
    uint8_t interactionScene[0x400];
    uint8_t context[0x1B00];
    uint8_t bodySim[0xC0];
    uint32_t nodeElements[6];
    uint32_t nodeFree[2];
    uint32_t nodeNext[2];
    uint32_t edgeElements[6];
    uint32_t edgeFree[2];
    uint32_t edgeNext[2];
    uint32_t islandElements[4];
    uint32_t islandFree[1];
    uint32_t rootElements[2];
    uint32_t rootFree[1];
    uint32_t nodeBitmaps[4];
    uint32_t islandBitmap[1];
    uint32_t nodeCreated[2];
    uint32_t nodeDeleted[2];
    uint32_t edgeCreated[2];
    uint32_t edgeDeleted[2];
    uint32_t edgeBroken[2];
    uint32_t edgeJoined[2];
    uint8_t sip[0x40];
    uint8_t shapeSim0[0x20];
    uint8_t shapeSim1[0x20];
    uint8_t shapeCore0[0x24];
    uint8_t shapeCore1[0x24];
    uint8_t reusedContactManagerStorage[0x2F];
    uintptr_t contactInteractions[1];
};

struct IslandOutputStorage {
    IslandNodeSlotRecord nodes[4];
    IslandEdgeSlotRecord edges[4];
    IslandSlotRecord islands[4];
    IslandArticulationRootSlotRecord roots[4];
    uint32_t kinematic[4], kinematicChange[4], notReady[4],
        notReadyChange[4], islandWords[4];
    uint32_t nodeCreated[8], nodeDeleted[8], edgeCreated[8], edgeDeleted[8],
        edgeBroken[8], edgeJoined[8];
    IslandSipEdgeBinding bindings[8];
};

struct IslandRestoreOutputStorage {
    IslandNodeSlotRecord nodes[256];
    IslandEdgeSlotRecord edges[256];
    IslandSlotRecord islands[256];
    IslandArticulationRootSlotRecord roots[32];
    uint32_t kinematic[8], kinematicChange[8], notReady[8],
        notReadyChange[8], islandWords[8];
    uint32_t nodeCreated[256], nodeDeleted[256], edgeCreated[256],
        edgeDeleted[256], edgeBroken[256], edgeJoined[256];
    IslandSipEdgeBinding bindings[12];
};

#pragma warning(push)
#pragma warning(disable:4324)
struct SyntheticIslandRestoreFixture {
    uint8_t nphase[8];
    uint8_t ownerScene[0x4C0];
    uint8_t interactionScene[0x400];
    uint8_t context[0x1B00];
    uint8_t bodySims[9][0xC0];
    uint8_t bodyCores[9][8];
    uint8_t liveBodySims[9][0xC0];
    uint8_t liveBodyCores[9][8];
    uint32_t nodeElements[256 * 3];
    uint32_t nodeFree[256];
    uint32_t nodeNext[256];
    uint32_t edgeElements[256 * 3];
    uint32_t edgeFree[256];
    uint32_t edgeNext[256];
    uint32_t islandElements[256 * 4];
    uint32_t islandFree[256];
    uint32_t rootElements[32 * 2];
    uint32_t rootFree[32];
    uint32_t nodeBitmaps[4][8];
    uint32_t islandBitmap[8];
    uint32_t nodeCreated[256], nodeDeleted[256], edgeCreated[256],
        edgeDeleted[256], edgeBroken[256], edgeJoined[256];
    uint8_t shapeSims[9][0x20];
    uint8_t shapeCores[9][0x24];
    uint8_t oldSips[12][0x40];
    __declspec(align(16)) uint8_t oldManagers[12][0x10];
    uint8_t liveSips[12][0x40];
    __declspec(align(16)) uint8_t liveManagers[12][0x10];
    uintptr_t contactInteractions[12];
};
#pragma warning(pop)

static IslandSnapshotBuffersV1 IslandBuffers(IslandOutputStorage& storage,
    bool full = true) {
    IslandSnapshotBuffersV1 value = {};
    if (!full) return value;
    value.nodes = storage.nodes; value.nodeCapacity = 4;
    value.edges = storage.edges; value.edgeCapacity = 4;
    value.islands = storage.islands; value.islandCapacity = 4;
    value.roots = storage.roots; value.rootCapacity = 4;
    value.kinematicWords = storage.kinematic;
    value.kinematicWordCapacity = 4;
    value.kinematicChangeWords = storage.kinematicChange;
    value.kinematicChangeWordCapacity = 4;
    value.notReadyWords = storage.notReady;
    value.notReadyWordCapacity = 4;
    value.notReadyChangeWords = storage.notReadyChange;
    value.notReadyChangeWordCapacity = 4;
    value.islandWords = storage.islandWords;
    value.islandWordCapacity = 4;
    value.nodeCreated = storage.nodeCreated; value.nodeCreatedCapacity = 8;
    value.nodeDeleted = storage.nodeDeleted; value.nodeDeletedCapacity = 8;
    value.edgeCreated = storage.edgeCreated; value.edgeCreatedCapacity = 8;
    value.edgeDeleted = storage.edgeDeleted; value.edgeDeletedCapacity = 8;
    value.edgeBroken = storage.edgeBroken; value.edgeBrokenCapacity = 8;
    value.edgeJoined = storage.edgeJoined; value.edgeJoinedCapacity = 8;
    value.bindings = storage.bindings; value.bindingCapacity = 8;
    return value;
}

static IslandSnapshotBuffersV1 IslandRestoreBuffers(
    IslandRestoreOutputStorage& storage) {
    IslandSnapshotBuffersV1 value = {};
    value.nodes = storage.nodes; value.nodeCapacity = 256;
    value.edges = storage.edges; value.edgeCapacity = 256;
    value.islands = storage.islands; value.islandCapacity = 256;
    value.roots = storage.roots; value.rootCapacity = 32;
    value.kinematicWords = storage.kinematic;
    value.kinematicWordCapacity = 8;
    value.kinematicChangeWords = storage.kinematicChange;
    value.kinematicChangeWordCapacity = 8;
    value.notReadyWords = storage.notReady;
    value.notReadyWordCapacity = 8;
    value.notReadyChangeWords = storage.notReadyChange;
    value.notReadyChangeWordCapacity = 8;
    value.islandWords = storage.islandWords;
    value.islandWordCapacity = 8;
    value.nodeCreated = storage.nodeCreated;
    value.nodeCreatedCapacity = 256;
    value.nodeDeleted = storage.nodeDeleted;
    value.nodeDeletedCapacity = 256;
    value.edgeCreated = storage.edgeCreated;
    value.edgeCreatedCapacity = 256;
    value.edgeDeleted = storage.edgeDeleted;
    value.edgeDeletedCapacity = 256;
    value.edgeBroken = storage.edgeBroken;
    value.edgeBrokenCapacity = 256;
    value.edgeJoined = storage.edgeJoined;
    value.edgeJoinedCapacity = 256;
    value.bindings = storage.bindings; value.bindingCapacity = 12;
    return value;
}

static uintptr_t InitializeIslandRestoreFixture(
    SyntheticIslandRestoreFixture& fixture, uintptr_t unityBase) {
    ZeroMemory(&fixture, sizeof(fixture));
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(fixture.nphase);
    const uintptr_t owner = reinterpret_cast<uintptr_t>(fixture.ownerScene);
    const uintptr_t interaction = reinterpret_cast<uintptr_t>(
        fixture.interactionScene);
    const uintptr_t context = reinterpret_cast<uintptr_t>(fixture.context);
    const uintptr_t manager = context + 0x181Cu;
    *reinterpret_cast<uintptr_t*>(fixture.nphase) = owner;
    *reinterpret_cast<uintptr_t*>(fixture.ownerScene + 0x450) = nphase;
    *reinterpret_cast<uintptr_t*>(fixture.ownerScene + 0x4B4) = interaction;
    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x3E8) = context;
    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x3F0) = owner;
    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x10) =
        reinterpret_cast<uintptr_t>(fixture.contactInteractions);
    *reinterpret_cast<uint32_t*>(fixture.interactionScene + 0x14) = 12u;
    *reinterpret_cast<uint32_t*>(fixture.interactionScene + 0x18) = 12u;

    for (uint32_t i = 0; i < 256u; ++i) {
        fixture.nodeFree[i] = i + 1u < 256u ? i + 1u : 0xFFFFFFFFu;
        fixture.nodeNext[i] = 0xFFFFFFFFu;
        fixture.edgeFree[i] = i + 1u < 256u ? i + 1u : 0xFFFFFFFFu;
        fixture.edgeNext[i] = 0xFFFFFFFFu;
        fixture.islandFree[i] = i + 1u < 256u ? i + 1u : 0xFFFFFFFFu;
    }
    for (uint32_t i = 0; i < 32u; ++i)
        fixture.rootFree[i] = i + 1u < 32u ? i + 1u : 0xFFFFFFFFu;
    for (uint32_t i = 0; i < 9u; ++i) {
        const uintptr_t bodySim = reinterpret_cast<uintptr_t>(
            fixture.bodySims[i]);
        const uintptr_t bodyCore = reinterpret_cast<uintptr_t>(
            fixture.bodyCores[i]);
        *reinterpret_cast<uintptr_t*>(fixture.bodySims[i] + 0x24) =
            interaction;
        *reinterpret_cast<uintptr_t*>(fixture.bodySims[i] + 0x34) = bodyCore;
        *reinterpret_cast<uint32_t*>(fixture.bodySims[i] + 0xBC) = i;
        *reinterpret_cast<uintptr_t*>(fixture.bodyCores[i] + 4) = bodySim;
        const uintptr_t liveBodySim = reinterpret_cast<uintptr_t>(
            fixture.liveBodySims[i]);
        const uintptr_t liveBodyCore = reinterpret_cast<uintptr_t>(
            fixture.liveBodyCores[i]);
        *reinterpret_cast<uintptr_t*>(fixture.liveBodySims[i] + 0x24) =
            interaction;
        *reinterpret_cast<uintptr_t*>(fixture.liveBodySims[i] + 0x34) =
            liveBodyCore;
        *reinterpret_cast<uint32_t*>(fixture.liveBodySims[i] + 0xBC) = i;
        *reinterpret_cast<uintptr_t*>(fixture.liveBodyCores[i] + 4) =
            liveBodySim;
        fixture.nodeElements[i * 3] = static_cast<uint32_t>(bodySim);
        fixture.nodeElements[i * 3 + 1] = i;
        fixture.nodeElements[i * 3 + 2] = 0u;
        fixture.islandElements[i * 4] = i;
        fixture.islandElements[i * 4 + 1] = i == 0u ? 0u : 0xFFFFFFFFu;
        fixture.islandElements[i * 4 + 2] = i;
        fixture.islandElements[i * 4 + 3] = i == 0u ? 11u : 0xFFFFFFFFu;
        fixture.islandBitmap[i >> 5] |= 1u << (i & 31u);
        *reinterpret_cast<uintptr_t*>(fixture.shapeSims[i] + 0x1C) =
            reinterpret_cast<uintptr_t>(fixture.shapeCores[i]);
    }
    static const uint8_t edgeNodes[12][2] = {
        {0,1},{1,2},{2,3},{3,4},{4,5},{5,6},
        {6,7},{7,8},{0,2},{2,4},{4,6},{6,8}
    };
    for (uint32_t i = 0; i < 12u; ++i) {
        const uintptr_t sip = reinterpret_cast<uintptr_t>(fixture.oldSips[i]);
        const uintptr_t contactManager = reinterpret_cast<uintptr_t>(
            fixture.oldManagers[i]);
        *reinterpret_cast<uintptr_t*>(fixture.oldSips[i] + 0x20) =
            reinterpret_cast<uintptr_t>(
                fixture.shapeSims[edgeNodes[i][0]]);
        *reinterpret_cast<uintptr_t*>(fixture.oldSips[i] + 0x24) =
            reinterpret_cast<uintptr_t>(
                fixture.shapeSims[edgeNodes[i][1]]);
        *reinterpret_cast<uintptr_t*>(fixture.oldSips[i] + 0x38) =
            contactManager;
        *reinterpret_cast<uint32_t*>(fixture.oldSips[i] + 0x3C) = i;
        *reinterpret_cast<uintptr_t*>(fixture.oldManagers[i] + 0x0C) = sip;
        fixture.contactInteractions[i] = sip + 8u;
        fixture.edgeElements[i * 3] = edgeNodes[i][0];
        fixture.edgeElements[i * 3 + 1] = edgeNodes[i][1];
        fixture.edgeElements[i * 3 + 2] = static_cast<uint32_t>(
            contactManager);
        fixture.edgeNext[i] = i + 1u < 12u ? i + 1u : 0xFFFFFFFFu;
    }

    *reinterpret_cast<uintptr_t*>(manager + 0x0C) = unityBase + 0xEFF2C0u;
    *reinterpret_cast<uintptr_t*>(manager + 0x10) =
        reinterpret_cast<uintptr_t>(fixture.nodeElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x14) =
        reinterpret_cast<uintptr_t>(fixture.nodeFree);
    *reinterpret_cast<uint32_t*>(manager + 0x18) = 256u;
    *reinterpret_cast<uint32_t*>(manager + 0x1C) = 9u;
    *reinterpret_cast<uint32_t*>(manager + 0x20) = 247u;
    *reinterpret_cast<uintptr_t*>(manager + 0x24) =
        reinterpret_cast<uintptr_t>(fixture.nodeNext);
    for (uint32_t i = 0; i < 4u; ++i) {
        *reinterpret_cast<uintptr_t*>(manager + 0x28 + i * 4u) =
            reinterpret_cast<uintptr_t>(fixture.nodeBitmaps[i]);
        *reinterpret_cast<uint32_t*>(manager + 0x38 + i * 4u) = 8u;
    }
    *reinterpret_cast<uintptr_t*>(manager + 0x118) = unityBase + 0xEFF2C8u;
    *reinterpret_cast<uintptr_t*>(manager + 0x11C) =
        reinterpret_cast<uintptr_t>(fixture.edgeElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x120) =
        reinterpret_cast<uintptr_t>(fixture.edgeFree);
    *reinterpret_cast<uint32_t*>(manager + 0x124) = 256u;
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 12u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 244u;
    *reinterpret_cast<uintptr_t*>(manager + 0x130) =
        reinterpret_cast<uintptr_t>(fixture.edgeNext);
    *reinterpret_cast<uintptr_t*>(manager + 0x174) = unityBase + 0xEFF2D8u;
    *reinterpret_cast<uintptr_t*>(manager + 0x178) =
        reinterpret_cast<uintptr_t>(fixture.islandElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x17C) =
        reinterpret_cast<uintptr_t>(fixture.islandFree);
    *reinterpret_cast<uint32_t*>(manager + 0x180) = 256u;
    *reinterpret_cast<uint32_t*>(manager + 0x184) = 9u;
    *reinterpret_cast<uint32_t*>(manager + 0x188) = 247u;
    *reinterpret_cast<uintptr_t*>(manager + 0x19C) =
        reinterpret_cast<uintptr_t>(fixture.islandBitmap);
    *reinterpret_cast<uint32_t*>(manager + 0x1A0) = 8u;
    *reinterpret_cast<uintptr_t*>(manager + 0x1A4) = unityBase + 0xEFF2E0u;
    *reinterpret_cast<uintptr_t*>(manager + 0x1A8) =
        reinterpret_cast<uintptr_t>(fixture.rootElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x1AC) =
        reinterpret_cast<uintptr_t>(fixture.rootFree);
    *reinterpret_cast<uint32_t*>(manager + 0x1B0) = 32u;
    *reinterpret_cast<uint32_t*>(manager + 0x1B4) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x1B8) = 32u;

    *reinterpret_cast<uintptr_t*>(manager + 0x134) =
        reinterpret_cast<uintptr_t>(fixture.nodeCreated);
    *reinterpret_cast<uintptr_t*>(manager + 0x13C) =
        reinterpret_cast<uintptr_t>(fixture.nodeDeleted);
    *reinterpret_cast<uint32_t*>(manager + 0x144) = 256u;
    *reinterpret_cast<uint32_t*>(manager + 0x148) = 256u;
    *reinterpret_cast<uintptr_t*>(manager + 0x14C) =
        reinterpret_cast<uintptr_t>(fixture.edgeCreated);
    *reinterpret_cast<uintptr_t*>(manager + 0x154) =
        reinterpret_cast<uintptr_t>(fixture.edgeDeleted);
    *reinterpret_cast<uintptr_t*>(manager + 0x15C) =
        reinterpret_cast<uintptr_t>(fixture.edgeBroken);
    *reinterpret_cast<uintptr_t*>(manager + 0x164) =
        reinterpret_cast<uintptr_t>(fixture.edgeJoined);
    *reinterpret_cast<uint32_t*>(manager + 0x16C) = 256u;
    *reinterpret_cast<uint32_t*>(manager + 0x170) = 256u;
    *reinterpret_cast<uint32_t*>(manager + 0x1BC) = 9u;
    *reinterpret_cast<uint32_t*>(manager + 0x1C8) = 12u;
    return manager;
}

static void MaterializeIslandRestoreContacts(
    SyntheticIslandRestoreFixture& fixture) {
    const uintptr_t context = reinterpret_cast<uintptr_t>(fixture.context);
    const uintptr_t manager = context + 0x181Cu;
    static const uint8_t edgeNodes[12][2] = {
        {0,1},{1,2},{2,3},{3,4},{4,5},{5,6},
        {6,7},{7,8},{0,2},{2,4},{4,6},{6,8}
    };
    fixture.islandElements[1] = 0xFFFFFFFFu;
    fixture.islandElements[3] = 0xFFFFFFFFu;
    for (uint32_t i = 0; i < 12u; ++i) {
        const uintptr_t sip = reinterpret_cast<uintptr_t>(fixture.liveSips[i]);
        const uintptr_t contactManager = reinterpret_cast<uintptr_t>(
            fixture.liveManagers[i]);
        *reinterpret_cast<uintptr_t*>(fixture.liveSips[i] + 0x20) =
            reinterpret_cast<uintptr_t>(
                fixture.shapeSims[edgeNodes[i][0]]);
        *reinterpret_cast<uintptr_t*>(fixture.liveSips[i] + 0x24) =
            reinterpret_cast<uintptr_t>(
                fixture.shapeSims[edgeNodes[i][1]]);
        *reinterpret_cast<uintptr_t*>(fixture.liveSips[i] + 0x38) =
            contactManager;
        *reinterpret_cast<uint32_t*>(fixture.liveSips[i] + 0x3C) = i;
        *reinterpret_cast<uintptr_t*>(fixture.liveManagers[i] + 0x0C) = sip;
        fixture.contactInteractions[i] = sip + 8u;
        fixture.edgeElements[i * 3 + 2] =
            static_cast<uint32_t>(contactManager) | 4u;
        fixture.edgeNext[i] = 0xFFFFFFFFu;
        fixture.edgeCreated[i] = i;
        fixture.edgeJoined[i] = i;
    }
    *reinterpret_cast<uint32_t*>(manager + 0x150) = 12u;
    *reinterpret_cast<uint32_t*>(manager + 0x168) = 12u;
    *reinterpret_cast<uint8_t*>(manager + 0x1DD) = 1u;
    *reinterpret_cast<uint8_t*>(manager + 0x1DE) = 1u;
}

static uint32_t PermutedIslandNodeId(uint32_t targetId) {
    return (targetId + 4u) % 9u;
}

static uint32_t PermutedIslandEdgeId(uint32_t targetId) {
    return (targetId * 5u + 3u) % 12u;
}

static void PermuteIslandRestoreLiveIds(
    SyntheticIslandRestoreFixture& fixture) {
    static const uint8_t edgeNodes[12][2] = {
        {0,1},{1,2},{2,3},{3,4},{4,5},{5,6},
        {6,7},{7,8},{0,2},{2,4},{4,6},{6,8}
    };
    for (uint32_t target = 0; target < 9u; ++target) {
        const uint32_t current = PermutedIslandNodeId(target);
        fixture.nodeElements[current * 3u] = static_cast<uint32_t>(
            reinterpret_cast<uintptr_t>(fixture.liveBodySims[target]));
        *reinterpret_cast<uint32_t*>(fixture.liveBodySims[target] + 0xBCu) =
            current;
    }
    for (uint32_t target = 0; target < 12u; ++target) {
        const uint32_t current = PermutedIslandEdgeId(target);
        fixture.edgeElements[current * 3u] = PermutedIslandNodeId(
            edgeNodes[target][0]);
        fixture.edgeElements[current * 3u + 1u] = PermutedIslandNodeId(
            edgeNodes[target][1]);
        fixture.edgeElements[current * 3u + 2u] = static_cast<uint32_t>(
            reinterpret_cast<uintptr_t>(fixture.liveManagers[target])) | 4u;
        fixture.edgeNext[current] = 0xFFFFFFFFu;
        fixture.edgeCreated[target] = current;
        fixture.edgeJoined[target] = current;
        *reinterpret_cast<uint32_t*>(fixture.liveSips[target] + 0x3Cu) =
            current;
    }
}

static uintptr_t InitializeIslandFixture(SyntheticIslandFixture& fixture,
    uintptr_t unityBase) {
    ZeroMemory(&fixture, sizeof(fixture));
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(fixture.nphase);
    const uintptr_t owner = reinterpret_cast<uintptr_t>(fixture.ownerScene);
    const uintptr_t interaction = reinterpret_cast<uintptr_t>(
        fixture.interactionScene);
    const uintptr_t context = reinterpret_cast<uintptr_t>(fixture.context);
    const uintptr_t manager = context + 0x181Cu;
    *reinterpret_cast<uintptr_t*>(fixture.nphase) = owner;
    *reinterpret_cast<uintptr_t*>(fixture.ownerScene + 0x450) = nphase;
    *reinterpret_cast<uintptr_t*>(fixture.ownerScene + 0x4B4) = interaction;
    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x3E8) = context;
    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x3F0) = owner;
    *reinterpret_cast<uintptr_t*>(fixture.interactionScene + 0x10) =
        reinterpret_cast<uintptr_t>(fixture.contactInteractions);
    *reinterpret_cast<uint32_t*>(fixture.interactionScene + 0x14) = 0u;
    *reinterpret_cast<uint32_t*>(fixture.interactionScene + 0x18) = 1u;

    fixture.nodeElements[0] = static_cast<uint32_t>(
        reinterpret_cast<uintptr_t>(fixture.bodySim));
    fixture.nodeElements[1] = 0u;
    fixture.nodeElements[2] = 0u;
    fixture.nodeElements[3] = 0u;
    fixture.nodeElements[4] = 0xFFFFFFFFu;
    fixture.nodeElements[5] = 0u;
    fixture.nodeFree[0] = 0xFFFFFFFFu;
    fixture.nodeFree[1] = 0xFFFFFFFFu;
    fixture.nodeNext[0] = 0xFFFFFFFFu;
    fixture.nodeNext[1] = 0xFFFFFFFFu;
    *reinterpret_cast<uint32_t*>(fixture.bodySim + 0xBC) = 0u;
    for (uint32_t i = 0; i < 2; ++i) {
        fixture.edgeElements[i * 3] = 0xFFFFFFFFu;
        fixture.edgeElements[i * 3 + 1] = 0xFFFFFFFFu;
        fixture.edgeElements[i * 3 + 2] = 0u;
        fixture.edgeNext[i] = 0xFFFFFFFFu;
    }
    fixture.edgeFree[0] = 0xFFFFFFFFu;
    fixture.edgeFree[1] = 0u;
    fixture.islandElements[0] = 0u;
    fixture.islandElements[1] = 0xFFFFFFFFu;
    fixture.islandElements[2] = 0u;
    fixture.islandElements[3] = 0xFFFFFFFFu;
    fixture.islandFree[0] = 0xFFFFFFFFu;
    fixture.rootFree[0] = 0xFFFFFFFFu;
    fixture.islandBitmap[0] = 1u;

    *reinterpret_cast<uintptr_t*>(manager + 0x0C) = unityBase + 0xEFF2C0u;
    *reinterpret_cast<uintptr_t*>(manager + 0x10) =
        reinterpret_cast<uintptr_t>(fixture.nodeElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x14) =
        reinterpret_cast<uintptr_t>(fixture.nodeFree);
    *reinterpret_cast<uint32_t*>(manager + 0x18) = 2u;
    *reinterpret_cast<uint32_t*>(manager + 0x1C) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x20) = 1u;
    *reinterpret_cast<uintptr_t*>(manager + 0x24) =
        reinterpret_cast<uintptr_t>(fixture.nodeNext);
    for (uint32_t i = 0; i < 4; ++i) {
        *reinterpret_cast<uintptr_t*>(manager + 0x28 + i * 4u) =
            reinterpret_cast<uintptr_t>(&fixture.nodeBitmaps[i]);
        *reinterpret_cast<uint32_t*>(manager + 0x38 + i * 4u) = 1u;
    }
    *reinterpret_cast<uintptr_t*>(manager + 0x118) = unityBase + 0xEFF2C8u;
    *reinterpret_cast<uintptr_t*>(manager + 0x11C) =
        reinterpret_cast<uintptr_t>(fixture.edgeElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x120) =
        reinterpret_cast<uintptr_t>(fixture.edgeFree);
    *reinterpret_cast<uint32_t*>(manager + 0x124) = 2u;
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 2u;
    *reinterpret_cast<uintptr_t*>(manager + 0x130) =
        reinterpret_cast<uintptr_t>(fixture.edgeNext);
    *reinterpret_cast<uintptr_t*>(manager + 0x174) = unityBase + 0xEFF2D8u;
    *reinterpret_cast<uintptr_t*>(manager + 0x178) =
        reinterpret_cast<uintptr_t>(fixture.islandElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x17C) =
        reinterpret_cast<uintptr_t>(fixture.islandFree);
    *reinterpret_cast<uint32_t*>(manager + 0x180) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x184) = 0xFFFFFFFFu;
    *reinterpret_cast<uint32_t*>(manager + 0x188) = 0u;
    *reinterpret_cast<uintptr_t*>(manager + 0x19C) =
        reinterpret_cast<uintptr_t>(fixture.islandBitmap);
    *reinterpret_cast<uint32_t*>(manager + 0x1A0) = 1u;
    *reinterpret_cast<uintptr_t*>(manager + 0x1A4) = unityBase + 0xEFF2E0u;
    *reinterpret_cast<uintptr_t*>(manager + 0x1A8) =
        reinterpret_cast<uintptr_t>(fixture.rootElements);
    *reinterpret_cast<uintptr_t*>(manager + 0x1AC) =
        reinterpret_cast<uintptr_t>(fixture.rootFree);
    *reinterpret_cast<uint32_t*>(manager + 0x1B0) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x1B4) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x1B8) = 1u;
    *reinterpret_cast<uintptr_t*>(manager + 0x134) =
        reinterpret_cast<uintptr_t>(fixture.nodeCreated);
    *reinterpret_cast<uintptr_t*>(manager + 0x13C) =
        reinterpret_cast<uintptr_t>(fixture.nodeDeleted);
    *reinterpret_cast<uint32_t*>(manager + 0x144) = 2u;
    *reinterpret_cast<uint32_t*>(manager + 0x148) = 2u;
    *reinterpret_cast<uintptr_t*>(manager + 0x14C) =
        reinterpret_cast<uintptr_t>(fixture.edgeCreated);
    *reinterpret_cast<uintptr_t*>(manager + 0x154) =
        reinterpret_cast<uintptr_t>(fixture.edgeDeleted);
    *reinterpret_cast<uintptr_t*>(manager + 0x15C) =
        reinterpret_cast<uintptr_t>(fixture.edgeBroken);
    *reinterpret_cast<uintptr_t*>(manager + 0x164) =
        reinterpret_cast<uintptr_t>(fixture.edgeJoined);
    *reinterpret_cast<uint32_t*>(manager + 0x16C) = 2u;
    *reinterpret_cast<uint32_t*>(manager + 0x170) = 2u;
    *reinterpret_cast<uintptr_t*>(fixture.sip + 0x20) =
        reinterpret_cast<uintptr_t>(fixture.shapeSim0);
    *reinterpret_cast<uintptr_t*>(fixture.sip + 0x24) =
        reinterpret_cast<uintptr_t>(fixture.shapeSim1);
    *reinterpret_cast<uintptr_t*>(fixture.shapeSim0 + 0x1C) =
        reinterpret_cast<uintptr_t>(fixture.shapeCore0);
    *reinterpret_cast<uintptr_t*>(fixture.shapeSim1 + 0x1C) =
        reinterpret_cast<uintptr_t>(fixture.shapeCore1);
    *reinterpret_cast<uint32_t*>(fixture.sip + 0x3C) = 0xFFFFFFFFu;
    return manager;
}

static void RunIslandTests(uint8_t* image, CaptureIslandSnapshot capture,
    IslandInstall install, IslandStatus status, IslandArm arm,
    IslandCopy copy, IslandAction cancel, IslandAction uninstall,
    IslandJournalCopy copyJournal) {
    const uintptr_t unity = reinterpret_cast<uintptr_t>(image);
    SyntheticIslandFixture fixture = {};
    const uintptr_t manager = InitializeIslandFixture(fixture, unity);
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(fixture.nphase);
    IslandOutputStorage storage = {};
    IslandSnapshotBuffersV1 buffers = IslandBuffers(storage);
    IslandSnapshotReceiptV1 receipt = {};
    IslandOutputStorage emptyStorage = {};
    IslandSnapshotBuffersV1 empty = IslandBuffers(emptyStorage, false);
    Check(capture(unity, nphase, 1u, &empty, &receipt) == 0 &&
        receipt.result == 6u && receipt.node.required == 2u &&
        receipt.edge.required == 2u && receipt.island.required == 1u &&
        receipt.root.required == 1u,
        "island sizing call reports every exact required capacity");
    receipt = {};
    const int firstCapture = capture(unity, nphase, 1u, &buffers, &receipt);
    Check(firstCapture == 1 &&
        receipt.result == 1u && receipt.validationFlags == 0x3FFu &&
        receipt.structSize == 620u && receipt.bindingsWritten == 0u &&
        storage.nodes[0].slotFlags == 5u &&
        (storage.nodes[1].slotFlags & 2u) != 0u &&
        (storage.edges[0].slotFlags & 2u) != 0u &&
        (storage.edges[1].slotFlags & 2u) != 0u,
        "island capture preserves physical slots, allocator order and topology");
    const uint32_t stableHash = receipt.snapshotHash;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 1 &&
        receipt.snapshotHash == stableHash,
        "island capture is deterministic at a settled boundary");

    // A single element may legally be both created and deleted before the
    // next update.  Preserve C/D order and opposition without inventing a
    // disjointness rule, while still requiring the source tag bits.
    fixture.nodeCreated[0] = 1u;
    fixture.nodeDeleted[0] = 1u;
    fixture.edgeCreated[0] = 1u;
    fixture.edgeDeleted[0] = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x138) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x140) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x150) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x158) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x1C) = 0xFFFFFFFFu;
    *reinterpret_cast<uint32_t*>(manager + 0x20) = 0u;
    fixture.nodeElements[3] = 0u;
    fixture.nodeElements[4] = 0xFFFFFFFFu;
    fixture.nodeElements[5] = 0x60u;
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 1u;
    fixture.edgeElements[3] = 0xFFFFFFFFu;
    fixture.edgeElements[4] = 0xFFFFFFFFu;
    fixture.edgeElements[5] = 0x0Du;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 1 &&
        receipt.nodeCreated.written == 1u &&
        receipt.nodeDeleted.written == 1u &&
        receipt.edgeCreated.written == 1u &&
        receipt.edgeDeleted.written == 1u &&
        storage.nodeCreated[0] == 1u && storage.nodeDeleted[0] == 1u &&
        storage.edgeCreated[0] == 1u && storage.edgeDeleted[0] == 1u,
        "island capture accepts exact opposing C/D rows with both tags");
    fixture.nodeElements[5] &= ~0x40u;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 0 &&
        receipt.result == 9u,
        "island capture rejects C membership without the created tag");
    *reinterpret_cast<uint32_t*>(manager + 0x138) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x140) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x150) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x158) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x1C) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x20) = 1u;
    fixture.nodeElements[3] = 0u;
    fixture.nodeElements[4] = 0xFFFFFFFFu;
    fixture.nodeElements[5] = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 2u;
    fixture.edgeElements[3] = 0xFFFFFFFFu;
    fixture.edgeElements[4] = 0xFFFFFFFFu;
    fixture.edgeElements[5] = 0u;

    // A SIP edge can exist before/after its contact manager.  Its shape-core
    // key still provides exact semantic identity when the manager is null.
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 1u;
    fixture.edgeElements[3] = 0u;
    fixture.edgeElements[4] = 0u;
    fixture.edgeElements[5] = 0u;
    *reinterpret_cast<uint32_t*>(fixture.sip + 0x3C) = 1u;
    *reinterpret_cast<uintptr_t*>(fixture.sip + 0x38) = 0u;
    fixture.contactInteractions[0] = reinterpret_cast<uintptr_t>(
        fixture.sip + 8u);
    *reinterpret_cast<uint32_t*>(fixture.interactionScene + 0x14) = 1u;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 1 &&
        receipt.liveContactEdges == 1u && receipt.bindingsWritten == 1u &&
        storage.bindings[0].sip == reinterpret_cast<uintptr_t>(fixture.sip) &&
        storage.bindings[0].contactManager == 0u,
        "island capture binds a managerless active SIP edge");
    *reinterpret_cast<uint32_t*>(fixture.interactionScene + 0x14) = 0u;
    *reinterpret_cast<uint32_t*>(fixture.sip + 0x3C) = 0xFFFFFFFFu;
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 2u;
    fixture.edgeElements[3] = 0xFFFFFFFFu;
    fixture.edgeElements[4] = 0xFFFFFFFFu;
    fixture.edgeElements[5] = 0u;

    fixture.nodeFree[1] = 1u;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 0 &&
        receipt.result == 7u,
        "island capture rejects a cyclic LIFO free chain");
    fixture.nodeFree[1] = 0xFFFFFFFFu;
    fixture.islandBitmap[0] |= 0x80000000u;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 0 &&
        receipt.result == 8u,
        "island capture rejects bitmap tail bits above capacity");
    fixture.islandBitmap[0] = 1u;
    *reinterpret_cast<uint32_t*>(fixture.bodySim + 0xBC) = 1u;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 0 &&
        receipt.result == 10u,
        "island capture cross-validates BodySim ownership hooks");
    *reinterpret_cast<uint32_t*>(fixture.bodySim + 0xBC) = 0u;
    image[kIslandPrivateUpdateRva] ^= 1u;
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 0 &&
        receipt.result == 3u,
        "island capture fails closed on shipped revision mismatch");
    image[kIslandPrivateUpdateRva] ^= 1u;

    IslandUpdateObserverReceipt observer = {};
    Check(install(unity, manager, &observer) == 1 && observer.installed == 1u &&
        observer.state == 1u && observer.validationFlags == 1u,
        "island observer installs all three resident hooks");
    FakeIslandAddEdge add = reinterpret_cast<FakeIslandAddEdge>(
        image + kIslandAddEdgeRva);
    FakeIslandRemoveEdge remove = reinterpret_cast<FakeIslandRemoveEdge>(
        image + kIslandRemoveEdgeRva);
    uint32_t edgeHook = 0xFFFFFFFFu;
    add(reinterpret_cast<void*>(manager), 1u, 0u, 0u, &edgeHook);
    remove(reinterpret_cast<void*>(manager), 1u, &edgeHook);
    IslandEdgeJournalRecord journal[4] = {};
    IslandEdgeJournalReceipt journalReceipt = {};
    Check(edgeHook == 0xFFFFFFFFu &&
        copyJournal(unity, 1u, journal, 4u, &journalReceipt) == 1 &&
        journalReceipt.recordsWritten == 2u &&
        journalReceipt.addCount == 1u && journalReceipt.removeCount == 1u &&
        journal[0].eventKind == 1u && journal[0].postEdgeId == 1u &&
        journal[1].eventKind == 2u && journal[1].preEdgeId == 1u &&
        journal[1].postEdgeId == 0xFFFFFFFFu,
        "island edge journal preserves exact oriented add/remove order");

    // Reservation advances the next ordinal before the wrapper commits its
    // row.  A snapshot must fail, not skip that newest lifetime and consult
    // older ring history.
    g_islandAddPauseEntered = CreateEventA(0, TRUE, FALSE, 0);
    g_islandAddPauseRelease = CreateEventA(0, TRUE, FALSE, 0);
    InterlockedExchange(&g_islandAddPauseClaimed, 0);
    uint32_t pausedHook = 0xFFFFFFFFu;
    IslandAddWorkerCall addCall = {add, reinterpret_cast<void*>(manager),
        1u, 0u, 0u, &pausedHook};
    HANDLE addThread = g_islandAddPauseEntered && g_islandAddPauseRelease ?
        CreateThread(0, 0, RunIslandAddWorker, &addCall, 0, 0) : 0;
    const DWORD addEntered = addThread ? WaitForSingleObject(
        g_islandAddPauseEntered, 10000) : WAIT_FAILED;
    receipt = {};
    const int uncommittedCapture = addEntered == WAIT_OBJECT_0 ?
        capture(unity, nphase, 1u, &buffers, &receipt) : 1;
    Check(addThread && addEntered == WAIT_OBJECT_0 &&
        uncommittedCapture == 0 && receipt.result == 13u,
        "island capture rejects a retained uncommitted newest journal row");
    if (g_islandAddPauseRelease) SetEvent(g_islandAddPauseRelease);
    const DWORD addDone = addThread ? WaitForSingleObject(addThread, 10000) :
        WAIT_FAILED;
    Check(addDone == WAIT_OBJECT_0 && pausedHook == 1u,
        "island add wrapper commits after the uncommitted snapshot probe");
    if (addThread) CloseHandle(addThread);
    if (g_islandAddPauseEntered) CloseHandle(g_islandAddPauseEntered);
    if (g_islandAddPauseRelease) CloseHandle(g_islandAddPauseRelease);
    g_islandAddPauseEntered = 0;
    g_islandAddPauseRelease = 0;

    // A deferred contact edge is keyed by the REMOVE journal record.  Its raw
    // manager payload is diagnostic only: destroyManager precedes removeEdge,
    // so the pool slot may already backlink to an unrelated SIP.
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 1u;
    fixture.edgeElements[3] = 0u;
    fixture.edgeElements[4] = 0u;
    const uintptr_t reusedContactManager =
        (reinterpret_cast<uintptr_t>(fixture.reusedContactManagerStorage) +
            15u) & ~static_cast<uintptr_t>(15u);
    fixture.edgeElements[5] = static_cast<uint32_t>(
        reusedContactManager) | 8u;
    fixture.edgeDeleted[0] = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x158) = 1u;
    *reinterpret_cast<uintptr_t*>(reusedContactManager + 0x0C) =
        reinterpret_cast<uintptr_t>(fixture.sip + 4u);
    *reinterpret_cast<uint32_t*>(fixture.sip + 0x3C) = 1u;
    remove(reinterpret_cast<void*>(manager), 0u,
        reinterpret_cast<uint32_t*>(fixture.sip + 0x3C));
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 1 &&
        receipt.bindingsWritten == 1u &&
        storage.bindings[0].sip == reinterpret_cast<uintptr_t>(fixture.sip) &&
        storage.bindings[0].contactManager ==
            reusedContactManager,
        "island deferred-D binding tolerates a reused manager pool slot");

    // The latest reliable event for an edge ID owns that lifetime.  An
    // incomplete newest REMOVE must fail closed instead of falling through to
    // the older complete REMOVE above.
    uint32_t invalidContactHook = 1u;
    remove(reinterpret_cast<void*>(manager), 0u, &invalidContactHook);
    receipt = {};
    Check(capture(unity, nphase, 1u, &buffers, &receipt) == 0 &&
        receipt.result == 11u,
        "island capture does not reuse an older binding after invalid newest remove");
    *reinterpret_cast<uint32_t*>(manager + 0x158) = 0u;
    *reinterpret_cast<uint32_t*>(manager + 0x128) = 1u;
    *reinterpret_cast<uint32_t*>(manager + 0x12C) = 2u;
    fixture.edgeElements[3] = 0xFFFFFFFFu;
    fixture.edgeElements[4] = 0xFFFFFFFFu;
    fixture.edgeElements[5] = 0u;

    IslandOutputStorage preSource = {}, postSource = {};
    IslandSnapshotBuffersV1 preSourceBuffers = IslandBuffers(preSource);
    IslandSnapshotBuffersV1 postSourceBuffers = IslandBuffers(postSource);
    IslandSnapshotReceiptV1 preSourceReceipt = {}, postSourceReceipt = {};
    observer = {};
    const DWORD islandArmThreadId = GetCurrentThreadId();
    Check(arm(unity, manager, nphase, 0u, &preSourceBuffers,
            &preSourceReceipt, &postSourceBuffers, &postSourceReceipt,
            &observer) == 1 && observer.state == 2u &&
        observer.armedOrdinal == 1u && observer.armedThreadId != 0u &&
        observer.armedThreadId == islandArmThreadId,
        "island observer arms caller-owned pre/post buffers");
    FakeIslandUpdate update = reinterpret_cast<FakeIslandUpdate>(
        image + kIslandUpdateRva);
    IslandUpdateWorkerCall updateCall = {
        update, reinterpret_cast<void*>(manager), 0, 0u, 0
    };
    HANDLE updateThread = CreateThread(0, 0, RunIslandUpdateWorker,
        &updateCall, 0, 0);
    const DWORD updateDone = updateThread ? WaitForSingleObject(updateThread,
        10000) : WAIT_FAILED;
    DWORD updateExitCode = 1;
    const BOOL updateExitRead = updateThread ?
        GetExitCodeThread(updateThread, &updateExitCode) : FALSE;
    if (updateThread) CloseHandle(updateThread);
    observer = {};
    const int observerStatus = status(unity, &observer);
    Check(updateThread && updateDone == WAIT_OBJECT_0 && updateExitRead &&
        updateExitCode == 0 && updateCall.threadId != 0u &&
        observerStatus == 1 && observer.state == 4u &&
        observer.observationOrdinal == 1u && observer.inFlight == 0u &&
        observer.validationFlags == 0x7Fu &&
        observer.expectedManager == manager &&
        observer.observedManager == manager && observer.pass == 0u &&
        observer.armedThreadId == islandArmThreadId &&
        observer.threadId == updateCall.threadId &&
        observer.armedThreadId != observer.threadId,
        "island observer commits an exact cross-thread first-pass transition");
    IslandOutputStorage preCopy = {}, postCopy = {};
    IslandSnapshotBuffersV1 preCopyBuffers = IslandBuffers(preCopy);
    IslandSnapshotBuffersV1 postCopyBuffers = IslandBuffers(postCopy);
    IslandSnapshotReceiptV1 preCopyReceipt = {}, postCopyReceipt = {};
    observer = {};
    Check(copy(unity, 1u, &preCopyBuffers, &preCopyReceipt,
            &postCopyBuffers, &postCopyReceipt, &observer) == 1 &&
        preCopyReceipt.snapshotHash == observer.preSnapshotHash &&
        postCopyReceipt.snapshotHash == observer.postSnapshotHash &&
        preCopyReceipt.numAddedRBodies + 1u ==
            postCopyReceipt.numAddedRBodies,
        "island observer copies stable pre/post snapshots by ordinal");
    observer = {};
    Check(cancel(unity, &observer) == 1 && observer.state == 1u &&
        observer.inFlight == 0u,
        "island cancel supplies an explicit caller-buffer quiescence fence");
    observer = {};
    Check(uninstall(unity, &observer) == 1 && observer.installed == 1u &&
        observer.state == 0u && image[kIslandAddEdgeRva] == 0xE9 &&
        image[kIslandRemoveEdgeRva] == 0xE9 &&
        image[kIslandUpdateRva] == 0xE9,
        "island logical uninstall leaves all pinned detours dormant");
    observer = {};
    Check(install(unity, manager, &observer) == 1 && observer.state == 1u,
        "island observer safely reuses resident detours");
    observer = {};
    Check(uninstall(unity, &observer) == 1 && observer.state == 0u,
        "island observer returns to dormant after reactivation");
}

static void RunIslandRestoreTests(uint8_t* image,
    CaptureIslandSnapshot capture, RestoreIslandSnapshot restore,
    IslandInstall install, IslandAction uninstall) {
    const uintptr_t unity = reinterpret_cast<uintptr_t>(image);
    __declspec(align(16)) SyntheticIslandRestoreFixture fixture = {};
    const uintptr_t manager = InitializeIslandRestoreFixture(fixture, unity);
    const uintptr_t nphase = reinterpret_cast<uintptr_t>(fixture.nphase);
    const uintptr_t context = reinterpret_cast<uintptr_t>(fixture.context);
    IslandRestoreOutputStorage targetStorage = {};
    IslandRestoreOutputStorage rollbackStorage = {};
    IslandRestoreOutputStorage verifyStorage = {};
    IslandSnapshotBuffersV1 targetBuffers = IslandRestoreBuffers(
        targetStorage);
    IslandSnapshotBuffersV1 rollbackBuffers = IslandRestoreBuffers(
        rollbackStorage);
    IslandSnapshotBuffersV1 verifyBuffers = IslandRestoreBuffers(
        verifyStorage);
    IslandSnapshotReceiptV1 targetReceipt = {};
    Check(capture(unity, nphase, 1u, &targetBuffers, &targetReceipt) == 1 &&
        targetReceipt.apiVersion == kCurrentApiVersion &&
        targetReceipt.node.freeCount == 247u &&
        targetReceipt.edge.freeCount == 244u &&
        targetReceipt.island.freeCount == 247u &&
        targetReceipt.root.freeCount == 32u &&
        targetReceipt.liveContactEdges == 12u &&
        targetReceipt.bindingsWritten == 12u,
        "island restore fixture captures the exact settled f444 scope");

    IslandUpdateObserverReceipt observer = {};
    Check(install(unity, manager, &observer) == 1 &&
        observer.state == 1u,
        "island restore acquires the resident hook quiescence boundary");
    MaterializeIslandRestoreContacts(fixture);
    PermuteIslandRestoreLiveIds(fixture);

    IslandNodeRebindV1 nodeBindings[9] = {};
    for (uint32_t i = 0; i < 9u; ++i) {
        const uintptr_t targetBodyCore = reinterpret_cast<uintptr_t>(
            fixture.bodyCores[i]);
        const uintptr_t bodySim = reinterpret_cast<uintptr_t>(
            fixture.liveBodySims[i]);
        const uintptr_t bodyCore = reinterpret_cast<uintptr_t>(
            fixture.liveBodyCores[i]);
        nodeBindings[i].targetNodeId = i;
        nodeBindings[i].targetOwnerRaw =
            targetStorage.nodes[i].ownerOrArticulationRaw;
        nodeBindings[i].targetBodyCore = static_cast<uint32_t>(
            targetBodyCore);
        nodeBindings[i].liveBodySim = static_cast<uint32_t>(bodySim);
        nodeBindings[i].liveBodyCore = static_cast<uint32_t>(bodyCore);
        nodeBindings[i].liveHookAddress = static_cast<uint32_t>(
            bodySim + 0xBCu);
        nodeBindings[i].currentNodeId = PermutedIslandNodeId(i);
        nodeBindings[i].semanticKey = 0xB1000000u + i;
        nodeBindings[i].validationFlags = 0x3Fu;
    }
    static const uint8_t edgeNodes[12][2] = {
        {0,1},{1,2},{2,3},{3,4},{4,5},{5,6},
        {6,7},{7,8},{0,2},{2,4},{4,6},{6,8}
    };
    IslandContactEdgeRebindV1 edgeBindings[12] = {};
    for (uint32_t i = 0; i < 12u; ++i) {
        const uintptr_t sip = reinterpret_cast<uintptr_t>(fixture.liveSips[i]);
        const uintptr_t shape0 = reinterpret_cast<uintptr_t>(
            fixture.shapeSims[edgeNodes[i][0]]);
        const uintptr_t shape1 = reinterpret_cast<uintptr_t>(
            fixture.shapeSims[edgeNodes[i][1]]);
        const uint32_t pxs0 = static_cast<uint32_t>(
            reinterpret_cast<uintptr_t>(
                fixture.shapeCores[edgeNodes[i][0]]) + 0x20u);
        const uint32_t pxs1 = static_cast<uint32_t>(
            reinterpret_cast<uintptr_t>(
                fixture.shapeCores[edgeNodes[i][1]]) + 0x20u);
        edgeBindings[i].targetBindingIndex = i;
        edgeBindings[i].targetEdgeId = targetStorage.bindings[i].edgeId;
        edgeBindings[i].targetPxsShapeCoreLow =
            targetStorage.bindings[i].pxsShapeCoreLow;
        edgeBindings[i].targetPxsShapeCoreHigh =
            targetStorage.bindings[i].pxsShapeCoreHigh;
        edgeBindings[i].liveSip = static_cast<uint32_t>(sip);
        edgeBindings[i].liveHookAddress = static_cast<uint32_t>(sip + 0x3Cu);
        edgeBindings[i].liveShapeSim0 = static_cast<uint32_t>(shape0);
        edgeBindings[i].liveShapeSim1 = static_cast<uint32_t>(shape1);
        edgeBindings[i].livePxsShapeCoreLow = pxs0 < pxs1 ? pxs0 : pxs1;
        edgeBindings[i].livePxsShapeCoreHigh = pxs0 < pxs1 ? pxs1 : pxs0;
        edgeBindings[i].liveContactManager = static_cast<uint32_t>(
            reinterpret_cast<uintptr_t>(fixture.liveManagers[i]));
        edgeBindings[i].currentEdgeId = PermutedIslandEdgeId(i);
        edgeBindings[i].semanticKey = i + 1u;
        edgeBindings[i].validationFlags = 0x3Fu;
    }
    IslandSnapshotReceiptV1 rollbackReceipt = {}, verifyReceipt = {};
    IslandRestoreRequestV1 request = {};
    request.apiVersion = kCurrentApiVersion;
    request.structSize = sizeof(request);
    request.flags = 1u;
    request.expectedThreadId = GetCurrentThreadId();
    request.expectedManager = static_cast<uint32_t>(manager);
    request.expectedContext = static_cast<uint32_t>(context);
    request.targetBuffers = &targetBuffers;
    request.targetReceipt = &targetReceipt;
    request.nodeRebinds = nodeBindings;
    request.nodeRebindCount = 9u;
    request.edgeRebinds = edgeBindings;
    request.edgeRebindCount = 12u;
    request.rollbackBuffers = &rollbackBuffers;
    request.rollbackReceipt = &rollbackReceipt;
    request.verifyBuffers = &verifyBuffers;
    request.verifyReceipt = &verifyReceipt;
    IslandRestoreReceiptV1 receipt = {};

    request.verifyReceipt = &rollbackReceipt;
    Check(restore(unity, nphase, &request, &receipt) == 0 &&
        receipt.result == 14u && receipt.mutationStarted == 0u,
        "island restore rejects aliased rollback and verification receipts");
    request.verifyReceipt = &verifyReceipt;
    IslandSnapshotBuffersV1 overlappingVerify = verifyBuffers;
    overlappingVerify.nodes = reinterpret_cast<IslandNodeSlotRecord*>(
        reinterpret_cast<uint8_t*>(rollbackBuffers.nodes) + 4u);
    request.verifyBuffers = &overlappingVerify;
    receipt = {};
    Check(restore(unity, nphase, &request, &receipt) == 0 &&
        receipt.result == 14u && receipt.mutationStarted == 0u,
        "island restore rejects partially overlapping image ranges");
    request.verifyBuffers = &verifyBuffers;
    // Later read-only capture APIs do not change the settled island image;
    // retain compatibility with historical API-19 and API-20 checkpoints.
    targetReceipt.apiVersion = 19u;
    receipt = {};
    const int restored = restore(unity, nphase, &request, &receipt);
    if (!restored)
        printf("island restore diagnostic: result=%u stage=%u kind=%u index=%u detail=%u error=%u node=%u edge=%u rollback=%u/%u\n",
            receipt.result, receipt.stage, receipt.invalidKind,
            receipt.invalidIndex, receipt.detail, receipt.lastError,
            receipt.nodeBindingsValidated, receipt.edgeBindingsValidated,
            receipt.rollbackAttempted, receipt.rollbackSucceeded);
    bool distinctBodyIncarnations = true;
    for (uint32_t i = 0; i < 9u; ++i)
        distinctBodyIncarnations = distinctBodyIncarnations &&
            nodeBindings[i].targetBodyCore != nodeBindings[i].liveBodyCore &&
            nodeBindings[i].currentNodeId != nodeBindings[i].targetNodeId;
    Check(restored == 1 && distinctBodyIncarnations &&
        receipt.apiVersion == kCurrentApiVersion &&
        receipt.result == 1u && receipt.stage == 8u &&
        receipt.validationFlags == 0xFFu &&
        receipt.nodeBindingsValidated == 9u &&
        receipt.edgeBindingsValidated == 12u &&
        receipt.nodeHooksWritten == 9u && receipt.edgeHooksWritten == 12u &&
        receipt.mutationStarted == 1u && receipt.mutationCommitted == 1u &&
        receipt.rollbackAttempted == 0u && receipt.failStopped == 0u &&
        receipt.targetRawHash != receipt.targetRebasedHash &&
        rollbackReceipt.edgeCreated.required == 12u &&
        rollbackReceipt.edgeJoined.required == 12u,
        "island restore accepts an API-19 target and atomically projects it");
    targetReceipt.apiVersion = 20u;
    bool hooksRebound = true;
    for (uint32_t i = 0; i < 12u; ++i)
        hooksRebound = hooksRebound &&
            *reinterpret_cast<uint32_t*>(fixture.liveSips[i] + 0x3C) == i;
    Check(hooksRebound &&
        *reinterpret_cast<uint32_t*>(manager + 0x150) == 0u &&
        *reinterpret_cast<uint32_t*>(manager + 0x168) == 0u &&
        *reinterpret_cast<uint8_t*>(manager + 0x1DE) == 0u,
        "island restore publishes hooks and clears transient C/J work last");

    IslandSnapshotReceiptV1 afterReceipt = {};
    Check(capture(unity, nphase, 1u, &rollbackBuffers, &afterReceipt) == 1 &&
        afterReceipt.snapshotHash == receipt.targetRebasedHash &&
        afterReceipt.bindingHash == verifyReceipt.bindingHash &&
        rollbackStorage.bindings[0].sip ==
            reinterpret_cast<uintptr_t>(fixture.liveSips[0]),
        "island restore physical readback equals the fully rebased target image");

    IslandNodeRebindV1 invalidNodes[9] = {};
    CopyMemory(invalidNodes, nodeBindings, sizeof(invalidNodes));
    for (uint32_t i = 0; i < 9u; ++i)
        invalidNodes[i].currentNodeId = i;
    IslandContactEdgeRebindV1 settledEdges[12] = {};
    CopyMemory(settledEdges, edgeBindings, sizeof(settledEdges));
    for (uint32_t i = 0; i < 12u; ++i)
        settledEdges[i].currentEdgeId = settledEdges[i].targetEdgeId;
    const uint32_t swapFields[][2] = {
        {invalidNodes[0].liveBodySim, invalidNodes[1].liveBodySim},
        {invalidNodes[0].liveBodyCore, invalidNodes[1].liveBodyCore},
        {invalidNodes[0].liveHookAddress, invalidNodes[1].liveHookAddress},
        {invalidNodes[0].currentNodeId, invalidNodes[1].currentNodeId}
    };
    invalidNodes[0].liveBodySim = swapFields[0][1];
    invalidNodes[1].liveBodySim = swapFields[0][0];
    invalidNodes[0].liveBodyCore = swapFields[1][1];
    invalidNodes[1].liveBodyCore = swapFields[1][0];
    invalidNodes[0].liveHookAddress = swapFields[2][1];
    invalidNodes[1].liveHookAddress = swapFields[2][0];
    invalidNodes[0].currentNodeId = swapFields[3][1];
    invalidNodes[1].currentNodeId = swapFields[3][0];
    request.nodeRebinds = invalidNodes;
    request.edgeRebinds = settledEdges;
    receipt = {};
    const int invalidRestored = restore(unity, nphase, &request, &receipt);
    if (invalidRestored || receipt.result != 12u)
        printf("island identity diagnostic: restored=%d result=%u node=%u edge=%u stage=%u detail=%u\n",
            invalidRestored, receipt.result, receipt.nodeBindingsValidated,
            receipt.edgeBindingsValidated, receipt.stage, receipt.detail);
    Check(invalidRestored == 0 &&
        receipt.result == 12u && receipt.mutationStarted == 0u &&
        receipt.rollbackAttempted == 0u && receipt.failStopped == 0u &&
        receipt.nodeBindingsValidated == 9u,
        "island restore rejects a stable-identity permutation whose edge topology disagrees");
    afterReceipt = {};
    Check(capture(unity, nphase, 1u, &rollbackBuffers, &afterReceipt) == 1 &&
        afterReceipt.snapshotHash == verifyReceipt.snapshotHash,
        "island restore semantic rejection leaves the settled image unchanged");
    observer = {};
    Check(uninstall(unity, &observer) == 1 && observer.state == 0u,
        "island restore releases the observer boundary after all tests");
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
    CaptureNPhasePoolSnapshotV1 captureNPhasePool =
        reinterpret_cast<CaptureNPhasePoolSnapshotV1>(GetProcAddress(
            library, "oc2_nphase_pool_capture_snapshot_v1"));
    CaptureNPhaseReportState captureNPhaseReport =
        reinterpret_cast<CaptureNPhaseReportState>(GetProcAddress(
            library, "oc2_nphase_report_state_capture_snapshot"));
    CaptureNPhaseReportSnapshotV1 captureNPhaseReportV1 =
        reinterpret_cast<CaptureNPhaseReportSnapshotV1>(GetProcAddress(
            library, "oc2_nphase_report_state_capture_snapshot_v1"));
    CaptureInteractionGraph captureInteractionGraph =
        reinterpret_cast<CaptureInteractionGraph>(GetProcAddress(
            library, "oc2_interaction_graph_capture_snapshot"));
    CaptureTransformCache captureTransformCache =
        reinterpret_cast<CaptureTransformCache>(GetProcAddress(
            library, "oc2_transform_cache_capture_snapshot"));
    FinishBroadPhaseAction installFinishBroadPhaseObserver =
        reinterpret_cast<FinishBroadPhaseAction>(GetProcAddress(library,
            "oc2_finish_broad_phase_observer_install"));
    FinishBroadPhaseAction statusFinishBroadPhaseObserver =
        reinterpret_cast<FinishBroadPhaseAction>(GetProcAddress(library,
            "oc2_finish_broad_phase_observer_status"));
    FinishBroadPhaseArm armFinishBroadPhaseObserver =
        reinterpret_cast<FinishBroadPhaseArm>(GetProcAddress(library,
            "oc2_finish_broad_phase_observer_arm"));
    FinishBroadPhaseCopy copyFinishBroadPhaseObserver =
        reinterpret_cast<FinishBroadPhaseCopy>(GetProcAddress(library,
            "oc2_finish_broad_phase_observer_copy"));
    FinishBroadPhaseAction cancelFinishBroadPhaseObserver =
        reinterpret_cast<FinishBroadPhaseAction>(GetProcAddress(library,
            "oc2_finish_broad_phase_observer_cancel"));
    FinishBroadPhaseAction uninstallFinishBroadPhaseObserver =
        reinterpret_cast<FinishBroadPhaseAction>(GetProcAddress(library,
            "oc2_finish_broad_phase_observer_uninstall"));
    CaptureIslandSnapshot captureIsland =
        reinterpret_cast<CaptureIslandSnapshot>(GetProcAddress(library,
            "oc2_island_capture_snapshot_v1"));
    RestoreIslandSnapshot restoreIsland =
        reinterpret_cast<RestoreIslandSnapshot>(GetProcAddress(library,
            "oc2_island_restore_snapshot_v1"));
    IslandInstall installIsland = reinterpret_cast<IslandInstall>(
        GetProcAddress(library, "oc2_island_update_observer_install"));
    IslandStatus statusIsland = reinterpret_cast<IslandStatus>(
        GetProcAddress(library, "oc2_island_update_observer_status"));
    IslandArm armIsland = reinterpret_cast<IslandArm>(GetProcAddress(library,
        "oc2_island_update_observer_arm"));
    IslandCopy copyIsland = reinterpret_cast<IslandCopy>(GetProcAddress(
        library, "oc2_island_update_observer_copy"));
    IslandAction cancelIsland = reinterpret_cast<IslandAction>(GetProcAddress(
        library, "oc2_island_update_observer_cancel"));
    IslandAction uninstallIsland = reinterpret_cast<IslandAction>(
        GetProcAddress(library, "oc2_island_update_observer_uninstall"));
    IslandJournalCopy copyIslandJournal =
        reinterpret_cast<IslandJournalCopy>(GetProcAddress(library,
            "oc2_island_edge_journal_copy"));
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
    DirtyCaptureSnapshot captureDirtySnapshot =
        reinterpret_cast<DirtyCaptureSnapshot>(GetProcAddress(library,
            "oc2_dirty_interaction_order_capture_snapshot"));
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
    Check(version && version() == kCurrentApiVersion, "API version");
    Check(capture != 0, "capture export");
    Check(restore != 0, "restore export");
    Check(captureManifold != 0, "manifold capture export");
    Check(restoreManifold != 0, "manifold restore export");
    Check(captureSip != 0, "shape-pair pool capture export");
    Check(captureActorPair != 0, "ActorPair pool capture export");
    Check(captureActorPairReport != 0,
        "ActorPair report pool capture export");
    Check(captureNPhasePool != 0, "NPhase pool snapshot export");
    Check(captureNPhaseReport != 0,
        "NPhase report-state capture export");
    Check(captureNPhaseReportV1 != 0,
        "complete NPhase report-history capture export");
    Check(captureInteractionGraph != 0,
        "interaction graph capture export");
    Check(captureTransformCache != 0,
        "transform-cache capture export");
    Check(installFinishBroadPhaseObserver && statusFinishBroadPhaseObserver &&
        armFinishBroadPhaseObserver && copyFinishBroadPhaseObserver &&
        cancelFinishBroadPhaseObserver && uninstallFinishBroadPhaseObserver,
        "finishBroadPhase observer exports");
    Check(captureIsland && restoreIsland && installIsland && statusIsland && armIsland &&
        copyIsland && cancelIsland && uninstallIsland && copyIslandJournal,
        "island snapshot/restore/observer/journal exports");
    Check(installDirty && statusDirty && lastDirtyNPhase && armDirtyCapture &&
        copyDirtyCapture && captureDirtySnapshot &&
        armDirtyRestore && cancelDirty && uninstallDirty,
        "dirty interaction exports");
    Check(installObserver && uninstallObserver && auditRecreate && armRecreate &&
        statusRecreate && cancelRecreate, "contact recreation exports");
    if (!version || !capture || !restore || !captureManifold || !captureSip ||
        !captureActorPair || !captureActorPairReport || !captureNPhasePool ||
        !captureNPhaseReport || !captureNPhaseReportV1 ||
        !captureInteractionGraph ||
        !captureTransformCache || !installFinishBroadPhaseObserver ||
        !statusFinishBroadPhaseObserver || !armFinishBroadPhaseObserver ||
        !copyFinishBroadPhaseObserver || !cancelFinishBroadPhaseObserver ||
        !uninstallFinishBroadPhaseObserver ||
        !captureIsland || !restoreIsland || !installIsland || !statusIsland || !armIsland ||
        !copyIsland || !cancelIsland || !uninstallIsland ||
        !copyIslandJournal ||
        !restoreManifold || !installDirty || !statusDirty || !lastDirtyNPhase || !armDirtyCapture ||
        !copyDirtyCapture || !captureDirtySnapshot || !armDirtyRestore ||
        !cancelDirty ||
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
    Check(receipt.result == 1 && receipt.apiVersion == kCurrentApiVersion &&
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
        RunNPhasePoolSnapshotTests(revisionImage, captureNPhasePool);
        RunNPhaseReportStateTests(revisionImage, captureNPhaseReport);
        RunNPhaseReportSnapshotV1Tests(revisionImage,
            captureNPhaseReportV1);
        RunInteractionGraphTests(revisionImage, captureInteractionGraph);
        RunTransformCacheTests(revisionImage, captureTransformCache);
        RunFinishBroadPhaseObserverTests(revisionImage, library,
            installFinishBroadPhaseObserver, statusFinishBroadPhaseObserver,
            armFinishBroadPhaseObserver, copyFinishBroadPhaseObserver,
            cancelFinishBroadPhaseObserver,
            uninstallFinishBroadPhaseObserver);
        RunIslandTests(revisionImage, captureIsland, installIsland,
            statusIsland, armIsland, copyIsland, cancelIsland,
            uninstallIsland, copyIslandJournal);
        RunIslandRestoreTests(revisionImage, captureIsland, restoreIsland,
            installIsland, uninstallIsland);
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
        RunDirtyInteractionTests(revisionImage, installDirty, statusDirty,
            lastDirtyNPhase, armDirtyCapture, copyDirtyCapture,
            captureDirtySnapshot, armDirtyRestore, cancelDirty,
            uninstallDirty);
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
