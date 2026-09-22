#include "SapImage.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <sstream>
#include <utility>

#include "PxPhysicsAPI.h"
#include "NpScene.h"
#include "ScScene.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"
#include "PsAllocator.h"

// The private access is confined to this offline test translation unit. The
// vendor checkout and the linked PhysX DLLs are unmodified.
#define private public
#define protected public
#include "PxsAABBManager.h"
#include "PxsBroadPhaseSap.h"
#undef protected
#undef private

namespace oc2 { namespace offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "SAP image requires Win32 PhysX");
static_assert(sizeof(IntegerAABB) == 24, "Unexpected BPElem bounds layout");
static_assert(sizeof(SapBox1D) == 8, "Unexpected SAP box layout");
static_assert(sizeof(PxcBroadPhasePair) == 8, "Unexpected SAP pair layout");
static_assert(sizeof(PxcAABBDataStatic) == 8, "Unexpected static AABB data layout");
static_assert(sizeof(PxcAABBDataDynamic) == 16, "Unexpected dynamic AABB data layout");

struct ScalarRef
{
    std::string name;
    void* address;
    std::size_t size;
    bool topology;
};

struct BufferRef
{
    std::string name;
    const void* address;
    std::size_t size;
};

struct Plan
{
    std::uintptr_t scene = 0;
    std::uintptr_t manager = 0;
    std::uintptr_t sap = 0;
    std::uint32_t activeElements = 0;
    std::vector<ScalarRef> scalars;
    std::vector<BufferRef> buffers;
    std::vector<SapImage::Binding> bindings;
};

template <typename T>
void addScalar(Plan& plan, const char* name, T& value, bool topology = false)
{
    plan.scalars.push_back(ScalarRef{name, &value, sizeof(value), topology});
}

bool addBuffer(Plan& plan, const std::string& name, const void* address,
               std::size_t count, std::size_t stride, std::string& error)
{
    const std::size_t limit = 256u * 1024u * 1024u;
    if (count > limit / stride)
    {
        error = name + ": unreasonable allocation capacity";
        return false;
    }
    const std::size_t bytes = count * stride;
    if (bytes != 0 && address == nullptr)
    {
        error = name + ": null allocation with nonzero capacity";
        return false;
    }
    plan.buffers.push_back(BufferRef{name, address, bytes});
    return true;
}

std::size_t align16(std::size_t size)
{
    return (size + 15u) & ~std::size_t(15u);
}

bool addAlignedBuffer(Plan& plan, const std::string& name, const void* address,
                      std::size_t count, std::size_t stride, std::string& error)
{
    if (count > (256u * 1024u * 1024u - 15u) / stride)
    {
        error = name + ": unreasonable aligned allocation capacity";
        return false;
    }
    return addBuffer(plan, name, address, align16(count * stride), 1, error);
}

bool validateFreeList(const char* name, const PxcBpHandle* next,
                      PxU32 capacity, PxU32 first, std::vector<bool>& free,
                      std::string& error)
{
    free.assign(capacity, false);
    PxU32 id = first;
    while (id != PX_INVALID_BP_HANDLE)
    {
        if (id >= capacity || free[id])
        {
            error = std::string(name) + ": free list is out of range or cyclic";
            return false;
        }
        free[id] = true;
        id = next[id];
    }
    return true;
}

template <typename T>
bool validateAabbDataFreeList(const char* name, const T* data,
                              PxU32 capacity, PxU32 first,
                              std::vector<bool>& free, std::string& error)
{
    if (capacity && !data)
    {
        error = std::string(name) + ": null data allocation";
        return false;
    }
    free.assign(capacity, false);
    PxU32 id = first;
    while (id != PX_INVALID_BP_HANDLE)
    {
        if (id >= capacity || free[id])
        {
            error = std::string(name) + ": free list is out of range or cyclic";
            return false;
        }
        free[id] = true;
        id = *reinterpret_cast<const PxcBpHandle*>(&data[id]);
    }
    return true;
}

bool appendChangeList(Plan& plan, const char* name, ChangeList& list,
                      std::string& error)
{
    const std::string prefix(name);
    addScalar(plan, (prefix + ".size").c_str(), list.mElemsSize);
    addScalar(plan, (prefix + ".capacity").c_str(), list.mElemsCapacity, true);
    addScalar(plan, (prefix + ".defaultCapacity").c_str(), list.mDefaultElemsCapacity, true);
    if (list.mElemsSize > list.mElemsCapacity)
    {
        error = prefix + ": size exceeds capacity";
        return false;
    }
    if (!addBuffer(plan, prefix + ".elements", list.mElems,
                   list.mElemsCapacity, sizeof(PxcBpHandle), error))
        return false;
    // Bitmap capacity is in words, including words beyond the current ids.
    if (!addBuffer(plan, prefix + ".bitmap", list.mBitMap.getWords(),
                   list.mBitMap.getWordCount(), sizeof(PxU32), error))
        return false;
    return true;
}

bool buildPlan(PxScene& scene, Plan& plan, std::string& error)
{
    // PhysX 3.3.3 creates NpScene for PxPhysics::createScene. The harness uses
    // only that concrete implementation.
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    PxsContext* context = np.getScene().getScScene()
                              .getInteractionScene().getLowLevelContext();
    if (!context || !context->getAABBManager())
    {
        error = "scene has no low-level AABB manager";
        return false;
    }
    PxsAABBManager& mgr = *context->getAABBManager();
    PxvBroadPhase* broadPhase = mgr.getBroadPhase();
    if (!broadPhase || broadPhase->getType() != PxBroadPhaseType::eSAP)
    {
        error = "scene broadphase is not SAP";
        return false;
    }
    PxsBroadPhaseContextSap& sap = *static_cast<PxsBroadPhaseContextSap*>(broadPhase);
    BPElems& elems = mgr.mBPElems;
    plan.scene = reinterpret_cast<std::uintptr_t>(&scene);
    plan.manager = reinterpret_cast<std::uintptr_t>(&mgr);
    plan.sap = reinterpret_cast<std::uintptr_t>(&sap);

    if (sap.mBoxesSize > sap.mBoxesCapacity ||
        sap.mBoxesSizePrev > sap.mBoxesCapacity ||
        sap.mEndPointsCapacity < 2u * sap.mBoxesSize + NUM_SENTINELS ||
        sap.mDataSize > sap.mDataCapacity ||
        sap.mCreatedPairsSize > sap.mCreatedPairsCapacity ||
        sap.mDeletedPairsSize > sap.mDeletedPairsCapacity)
    {
        error = "SAP size/capacity relation is invalid";
        return false;
    }

    addScalar(plan, "sap.createdInput", sap.mCreated);
    addScalar(plan, "sap.createdInputSize", sap.mCreatedSize);
    addScalar(plan, "sap.removedInput", sap.mRemoved);
    addScalar(plan, "sap.removedInputSize", sap.mRemovedSize);
    addScalar(plan, "sap.updatedInput", sap.mUpdated);
    addScalar(plan, "sap.updatedInputSize", sap.mUpdatedSize);
    addScalar(plan, "sap.boundsInput", sap.mBoxBoundsMinMax);
    addScalar(plan, "sap.groupsInput", sap.mBoxGroups);
    addScalar(plan, "sap.boxesCapacity", sap.mBoxesCapacity, true);
    addScalar(plan, "sap.boxesSize", sap.mBoxesSize);
    addScalar(plan, "sap.boxesSizePrev", sap.mBoxesSizePrev);
    addScalar(plan, "sap.endPointsCapacity", sap.mEndPointsCapacity, true);
    addScalar(plan, "sap.dataSize", sap.mDataSize);
    addScalar(plan, "sap.dataCapacity", sap.mDataCapacity, true);
    addScalar(plan, "sap.createdPairsSize", sap.mCreatedPairsSize);
    addScalar(plan, "sap.createdPairsCapacity", sap.mCreatedPairsCapacity, true);
    addScalar(plan, "sap.deletedPairsSize", sap.mDeletedPairsSize);
    addScalar(plan, "sap.deletedPairsCapacity", sap.mDeletedPairsCapacity, true);

    for (PxU32 axis = 0; axis < 3; ++axis)
    {
        const std::string prefix = "sap.axis" + std::to_string(axis);
        if (!addAlignedBuffer(plan, prefix + ".boxes", sap.mBoxEndPts[axis],
                              sap.mBoxesCapacity, sizeof(SapBox1D), error) ||
            !addAlignedBuffer(plan, prefix + ".endpointValues", sap.mEndPointValues[axis],
                              sap.mEndPointsCapacity, sizeof(PxcBPValType), error) ||
            !addAlignedBuffer(plan, prefix + ".endpointData", sap.mEndPointDatas[axis],
                              sap.mEndPointsCapacity, sizeof(PxcBpHandle), error))
            return false;
    }
#if BP_UPDATE_BEFORE_SWAP
    if (!addAlignedBuffer(plan, "sap.boxesUpdated", sap.mBoxesUpdated,
                          sap.mBoxesCapacity, sizeof(PxU8), error) ||
        !addAlignedBuffer(plan, "sap.sortedUpdates", sap.mSortedUpdateElements,
                          sap.mEndPointsCapacity, sizeof(PxcBpHandle), error) ||
        !addAlignedBuffer(plan, "sap.activityPockets", sap.mActivityPockets,
                          sap.mEndPointsCapacity, sizeof(PxsBroadPhaseActivityPocket), error) ||
        !addAlignedBuffer(plan, "sap.listNext", sap.mListNext,
                          sap.mEndPointsCapacity, sizeof(PxcBpHandle), error) ||
        !addAlignedBuffer(plan, "sap.listPrev", sap.mListPrev,
                          sap.mEndPointsCapacity, sizeof(PxcBpHandle), error))
        return false;
#endif
    if (!addBuffer(plan, "sap.data", sap.mData, sap.mDataCapacity,
                   sizeof(PxcBpHandle), error) ||
        !addBuffer(plan, "sap.createdPairs", sap.mCreatedPairsArray,
                   sap.mCreatedPairsCapacity, sizeof(PxcBroadPhasePair), error) ||
        !addBuffer(plan, "sap.deletedPairs", sap.mDeletedPairsArray,
                   sap.mDeletedPairsCapacity, sizeof(PxcBroadPhasePair), error))
        return false;

    SapPairManager& pairs = sap.mPairs;
    if (pairs.mHashSize > pairs.mHashCapacity ||
        pairs.mNbActivePairs > pairs.mActivePairsCapacity ||
        pairs.mNbActivePairs > pairs.mHashSize ||
        (pairs.mHashSize && pairs.mMask != pairs.mHashSize - 1u))
    {
        error = "SAP pair manager size/capacity relation is invalid";
        return false;
    }
    addScalar(plan, "pair.hashSize", pairs.mHashSize);
    addScalar(plan, "pair.hashCapacity", pairs.mHashCapacity, true);
    addScalar(plan, "pair.minHashCapacity", pairs.mMinAllowedHashCapacity, true);
    addScalar(plan, "pair.activeCount", pairs.mNbActivePairs);
    addScalar(plan, "pair.activeCapacity", pairs.mActivePairsCapacity, true);
    addScalar(plan, "pair.mask", pairs.mMask);
    if (!addBuffer(plan, "pair.hashTable", pairs.mHashTable,
                   pairs.mHashCapacity, sizeof(PxcBpHandle), error) ||
        !addBuffer(plan, "pair.next", pairs.mNext,
                   pairs.mActivePairsCapacity, sizeof(PxcBpHandle), error) ||
        !addBuffer(plan, "pair.active", pairs.mActivePairs,
                   pairs.mActivePairsCapacity, sizeof(PxcBroadPhasePair), error) ||
        !addBuffer(plan, "pair.states", pairs.mActivePairStates,
                   pairs.mActivePairsCapacity, sizeof(PxU8), error))
        return false;

    for (PxU32 axis = 0; axis < 3; ++axis)
    {
        BroadPhaseBatchUpdateWorkTask& task = sap.mBatchUpdateTasks[axis];
        const std::string prefix = "sap.batch" + std::to_string(axis);
        if (task.mPairsSize > task.mPairsCapacity)
        {
            error = prefix + ": size exceeds capacity";
            return false;
        }
        addScalar(plan, (prefix + ".size").c_str(), task.mPairsSize);
        addScalar(plan, (prefix + ".capacity").c_str(), task.mPairsCapacity, true);
        if (!addBuffer(plan, prefix + ".pairs", task.mPairs,
                       task.mPairsCapacity, sizeof(PxcBroadPhasePair), error))
            return false;
    }

    addScalar(plan, "bpelem.capacity", elems.mCapacity, true);
    addScalar(plan, "bpelem.firstFree", elems.mFirstFreeElem);
    if ((elems.mCapacity & 31u) != 0)
    {
        error = "BPElem capacity is not 32-aligned";
        return false;
    }
    const std::size_t cap = elems.mCapacity;
    const std::size_t bpelemBytes = align16(cap * sizeof(IntegerAABB)) +
        align16(cap * sizeof(void*)) + 4u * align16(cap * sizeof(PxcBpHandle));
    if (bpelemBytes && elems.mBuffer != reinterpret_cast<PxU8*>(elems.mBounds))
    {
        error = "BPElem backing allocation does not begin at bounds";
        return false;
    }
    if (!addBuffer(plan, "bpelem.backing", elems.mBuffer,
                   bpelemBytes, 1, error))
        return false;

    auto& statics = elems.mStaticAABBDataManager;
    auto& dynamics = elems.mDynamicAABBDataManager;
    addScalar(plan, "bpelem.staticCapacity", statics.mCapacity, true);
    addScalar(plan, "bpelem.staticFirstFree", statics.mFirstFreeElem);
    addScalar(plan, "bpelem.dynamicCapacity", dynamics.mCapacity, true);
    addScalar(plan, "bpelem.dynamicFirstFree", dynamics.mFirstFreeElem);
    if (!addBuffer(plan, "bpelem.staticData", statics.mData,
                   statics.mCapacity, sizeof(PxcAABBDataStatic), error) ||
        !addBuffer(plan, "bpelem.dynamicData", dynamics.mData,
                   dynamics.mCapacity, sizeof(PxcAABBDataDynamic), error))
        return false;

    if (cap && (!elems.mGroups || !elems.mUserDatas || !elems.mOwnerIds ||
                !elems.mAABBDataHandles))
    {
        error = "BPElem columns are null";
        return false;
    }
    std::vector<bool> free;
    std::vector<bool> staticFree, dynamicFree;
    if (!validateFreeList("BPElem", elems.mGroups, elems.mCapacity,
                          elems.mFirstFreeElem, free, error) ||
        !validateAabbDataFreeList("static AABB data", statics.mData,
                                  statics.mCapacity, statics.mFirstFreeElem,
                                  staticFree, error) ||
        !validateAabbDataFreeList("dynamic AABB data", dynamics.mData,
                                  dynamics.mCapacity, dynamics.mFirstFreeElem,
                                  dynamicFree, error))
        return false;
    for (PxU32 id = 0; id < elems.mCapacity; ++id)
    {
        if (free[id]) continue;
        SapImage::Binding b;
        b.id = id;
        b.userData = reinterpret_cast<std::uintptr_t>(elems.mUserDatas[id]);
        b.group = elems.mGroups[id];
        b.ownerId = elems.mOwnerIds[id];
        b.aabbDataHandle = elems.mAABBDataHandles[id];
        if (b.aabbDataHandle != PX_INVALID_BP_HANDLE)
        {
            if (b.group == 0)
            {
                if (b.aabbDataHandle >= statics.mCapacity ||
                    staticFree[b.aabbDataHandle])
                {
                    error = "BPElem static AABB data handle is out of range";
                    return false;
                }
                const PxcAABBDataStatic& d = statics.mData[b.aabbDataHandle];
                b.shapeCore = reinterpret_cast<std::uintptr_t>(d.mShapeCore);
                b.rigidCore = reinterpret_cast<std::uintptr_t>(d.mRigidCore);
            }
            else
            {
                if (b.aabbDataHandle >= dynamics.mCapacity ||
                    dynamicFree[b.aabbDataHandle])
                {
                    error = "BPElem dynamic AABB data handle is out of range";
                    return false;
                }
                const PxcAABBDataDynamic& d = dynamics.mData[b.aabbDataHandle];
                b.shapeCore = reinterpret_cast<std::uintptr_t>(d.mShapeCore);
                b.rigidCore = reinterpret_cast<std::uintptr_t>(d.mRigidCore);
                b.bodyAtom = reinterpret_cast<std::uintptr_t>(d.mBodyAtom);
                b.localSpaceAabb = reinterpret_cast<std::uintptr_t>(d.mLocalSpaceAABB);
            }
        }
        plan.bindings.push_back(b);
    }
    plan.activeElements = static_cast<std::uint32_t>(plan.bindings.size());
    if (sap.mBoxesSize != plan.activeElements)
    {
        error = "SAP box count differs from active BPElem count";
        return false;
    }

    if (pairs.mHashSize)
    {
        std::vector<bool> seen(pairs.mNbActivePairs, false);
        for (PxU32 bucket = 0; bucket < pairs.mHashSize; ++bucket)
        {
            PxU32 index = pairs.mHashTable[bucket];
            while (index != PX_INVALID_BP_HANDLE)
            {
                if (index >= pairs.mNbActivePairs || seen[index])
                {
                    error = "SAP pair hash chain is out of range or cyclic";
                    return false;
                }
                seen[index] = true;
                const PxcBroadPhasePair& pair = pairs.mActivePairs[index];
                if (pair.mVolA > pair.mVolB ||
                    pair.mVolB >= elems.mCapacity ||
                    free[pair.mVolA] || free[pair.mVolB])
                {
                    error = "SAP pair refers to an invalid BPElem";
                    return false;
                }
                index = pairs.mNext[index];
            }
        }
        if (std::find(seen.begin(), seen.end(), false) != seen.end())
        {
            error = "SAP pair is absent from hash chains";
            return false;
        }
    }
    else if (pairs.mNbActivePairs)
    {
        error = "SAP has active pairs without a hash table";
        return false;
    }

    if (!appendChangeList(plan, "aabb.updated", mgr.mBPUpdatedElems, error) ||
        !appendChangeList(plan, "aabb.created", mgr.mBPCreatedElems, error) ||
        !appendChangeList(plan, "aabb.removed", mgr.mBPRemovedElems, error))
        return false;

    addScalar(plan, "aabb.createdOverlapSize", mgr.mCreatedPairsSize);
    addScalar(plan, "aabb.createdOverlapCapacity", mgr.mCreatedPairsCapacity, true);
    addScalar(plan, "aabb.deletedOverlapSize", mgr.mDeletedPairsSize);
    addScalar(plan, "aabb.deletedOverlapCapacity", mgr.mDeletedPairsCapacity, true);
    if (mgr.mCreatedPairsSize > mgr.mCreatedPairsCapacity ||
        mgr.mDeletedPairsSize > mgr.mDeletedPairsCapacity)
    {
        error = "AABB overlap result size exceeds capacity";
        return false;
    }
    if (!addBuffer(plan, "aabb.createdOverlaps", mgr.mCreatedPairs,
                   mgr.mCreatedPairsCapacity, sizeof(PxvBroadPhaseOverlap), error) ||
        !addBuffer(plan, "aabb.deletedOverlaps", mgr.mDeletedPairs,
                   mgr.mDeletedPairsCapacity, sizeof(PxvBroadPhaseOverlap), error))
        return false;
    return true;
}

bool sameBinding(const SapImage::Binding& a, const SapImage::Binding& b)
{
    return a.id == b.id && a.userData == b.userData && a.group == b.group &&
        a.ownerId == b.ownerId && a.aabbDataHandle == b.aabbDataHandle &&
        a.shapeCore == b.shapeCore && a.rigidCore == b.rigidCore &&
        a.bodyAtom == b.bodyAtom && a.localSpaceAabb == b.localSpaceAabb;
}

const SapImage::Scalar* findScalar(const SapImage& image, const char* name)
{
    for (const auto& value : image.scalars)
        if (value.name == name) return &value;
    return nullptr;
}

const SapImage::Buffer* findBuffer(const SapImage& image, const char* name)
{
    for (const auto& value : image.buffers)
        if (value.name == name) return &value;
    return nullptr;
}

bool imageU32(const SapImage& image, const char* name, PxU32& value,
              std::string& error)
{
    const auto* scalar = findScalar(image, name);
    if (!scalar || scalar->bytes.size() != sizeof(value))
    {
        error = std::string("saved scalar is absent: ") + name;
        return false;
    }
    std::memcpy(&value, scalar->bytes.data(), sizeof(value));
    return true;
}

template <typename T>
bool imageArrayItem(const SapImage::Buffer& buffer, std::size_t index,
                    T& value)
{
    if (index > buffer.bytes.size() / sizeof(T) ||
        index == buffer.bytes.size() / sizeof(T)) return false;
    std::memcpy(&value, buffer.bytes.data() + index * sizeof(T), sizeof(T));
    return true;
}

bool validateSavedImage(const SapImage& image, std::string& error)
{
    PxU32 elemCapacity = 0, firstFree = 0, boxesSize = 0;
    PxU32 staticCapacity = 0, dynamicCapacity = 0;
    PxU32 staticFirstFree = 0, dynamicFirstFree = 0;
    PxU32 hashSize = 0, hashCapacity = 0, activePairs = 0;
    PxU32 activeCapacity = 0, mask = 0;
    if (!imageU32(image, "bpelem.capacity", elemCapacity, error) ||
        !imageU32(image, "bpelem.firstFree", firstFree, error) ||
        !imageU32(image, "bpelem.staticCapacity", staticCapacity, error) ||
        !imageU32(image, "bpelem.staticFirstFree", staticFirstFree, error) ||
        !imageU32(image, "bpelem.dynamicCapacity", dynamicCapacity, error) ||
        !imageU32(image, "bpelem.dynamicFirstFree", dynamicFirstFree, error) ||
        !imageU32(image, "sap.boxesSize", boxesSize, error) ||
        !imageU32(image, "pair.hashSize", hashSize, error) ||
        !imageU32(image, "pair.hashCapacity", hashCapacity, error) ||
        !imageU32(image, "pair.activeCount", activePairs, error) ||
        !imageU32(image, "pair.activeCapacity", activeCapacity, error) ||
        !imageU32(image, "pair.mask", mask, error)) return false;
    const auto* backing = findBuffer(image, "bpelem.backing");
    const auto* staticData = findBuffer(image, "bpelem.staticData");
    const auto* dynamicData = findBuffer(image, "bpelem.dynamicData");
    const auto* hashTable = findBuffer(image, "pair.hashTable");
    const auto* next = findBuffer(image, "pair.next");
    const auto* pairData = findBuffer(image, "pair.active");
    if (!backing || !staticData || !dynamicData || !hashTable || !next || !pairData)
    {
        error = "saved SAP/BPElem allocation inventory is incomplete";
        return false;
    }
    const std::size_t byteLimit = 256u * 1024u * 1024u;
    if (elemCapacity > byteLimit / 44u ||
        staticCapacity > byteLimit / sizeof(PxcAABBDataStatic) ||
        dynamicCapacity > byteLimit / sizeof(PxcAABBDataDynamic) ||
        hashCapacity > byteLimit / sizeof(PxcBpHandle) ||
        activeCapacity > byteLimit / sizeof(PxcBroadPhasePair))
    {
        error = "saved SAP/BPElem capacity is unreasonable";
        return false;
    }
    const std::size_t c = elemCapacity;
    const std::size_t boundsLength = align16(c * sizeof(IntegerAABB));
    const std::size_t pointerLength = align16(c * sizeof(void*));
    const std::size_t handlesLength = align16(c * sizeof(PxcBpHandle));
    if (backing->bytes.size() != boundsLength + pointerLength + 4u * handlesLength ||
        staticData->bytes.size() != staticCapacity * sizeof(PxcAABBDataStatic) ||
        dynamicData->bytes.size() != dynamicCapacity * sizeof(PxcAABBDataDynamic) ||
        hashTable->bytes.size() != hashCapacity * sizeof(PxcBpHandle) ||
        next->bytes.size() != activeCapacity * sizeof(PxcBpHandle) ||
        pairData->bytes.size() != activeCapacity * sizeof(PxcBroadPhasePair))
    {
        error = "saved SAP/BPElem declared capacity does not match its buffers";
        return false;
    }
    const std::size_t userOffset = boundsLength;
    const std::size_t groupOffset = userOffset + pointerLength;
    const std::size_t ownerOffset = groupOffset + handlesLength;
    const std::size_t dataHandleOffset = ownerOffset + handlesLength;
    auto wordAt = [&backing](std::size_t offset) -> PxU32 {
        PxU32 value = 0;
        std::memcpy(&value, backing->bytes.data() + offset, sizeof(value));
        return value;
    };
    std::vector<bool> free(c, false);
    for (PxU32 id = firstFree; id != PX_INVALID_BP_HANDLE;)
    {
        if (id >= c || free[id])
        {
            error = "saved BPElem free list is out of range or cyclic";
            return false;
        }
        free[id] = true;
        id = wordAt(groupOffset + id * sizeof(PxcBpHandle));
    }
    auto dataFreeList = [&error](const char* name, const SapImage::Buffer& buffer,
                                 PxU32 capacity, PxU32 first, std::size_t stride,
                                 std::vector<bool>& result) -> bool {
        result.assign(capacity, false);
        for (PxU32 id = first; id != PX_INVALID_BP_HANDLE;)
        {
            if (id >= capacity || result[id])
            {
                error = std::string("saved ") + name + " free list is out of range or cyclic";
                return false;
            }
            result[id] = true;
            PxU32 nextId = PX_INVALID_BP_HANDLE;
            std::memcpy(&nextId, buffer.bytes.data() + std::size_t(id) * stride,
                        sizeof(nextId));
            id = nextId;
        }
        return true;
    };
    std::vector<bool> staticFree, dynamicFree;
    if (!dataFreeList("static AABB data", *staticData, staticCapacity,
                      staticFirstFree, sizeof(PxcAABBDataStatic), staticFree) ||
        !dataFreeList("dynamic AABB data", *dynamicData, dynamicCapacity,
                      dynamicFirstFree, sizeof(PxcAABBDataDynamic), dynamicFree))
        return false;
    std::size_t bindingIndex = 0;
    for (PxU32 id = 0; id < c; ++id)
    {
        if (free[id]) continue;
        if (bindingIndex >= image.bindings.size())
        {
            error = "saved BPElem active set is incomplete";
            return false;
        }
        SapImage::Binding actual;
        actual.id = id;
        actual.userData = wordAt(userOffset + id * sizeof(void*));
        actual.group = wordAt(groupOffset + id * sizeof(PxcBpHandle));
        actual.ownerId = wordAt(ownerOffset + id * sizeof(PxcBpHandle));
        actual.aabbDataHandle = wordAt(dataHandleOffset + id * sizeof(PxcBpHandle));
        if (actual.aabbDataHandle != PX_INVALID_BP_HANDLE)
        {
            if (actual.group == 0)
            {
                PxcAABBDataStatic data;
                if (actual.aabbDataHandle >= staticCapacity ||
                    staticFree[actual.aabbDataHandle] ||
                    !imageArrayItem(*staticData, actual.aabbDataHandle, data))
                {
                    error = "saved static AABB data handle is out of range";
                    return false;
                }
                actual.shapeCore = reinterpret_cast<std::uintptr_t>(data.mShapeCore);
                actual.rigidCore = reinterpret_cast<std::uintptr_t>(data.mRigidCore);
            }
            else
            {
                PxcAABBDataDynamic data;
                if (actual.aabbDataHandle >= dynamicCapacity ||
                    dynamicFree[actual.aabbDataHandle] ||
                    !imageArrayItem(*dynamicData, actual.aabbDataHandle, data))
                {
                    error = "saved dynamic AABB data handle is out of range";
                    return false;
                }
                actual.shapeCore = reinterpret_cast<std::uintptr_t>(data.mShapeCore);
                actual.rigidCore = reinterpret_cast<std::uintptr_t>(data.mRigidCore);
                actual.bodyAtom = reinterpret_cast<std::uintptr_t>(data.mBodyAtom);
                actual.localSpaceAabb = reinterpret_cast<std::uintptr_t>(data.mLocalSpaceAABB);
            }
        }
        if (!sameBinding(actual, image.bindings[bindingIndex++]))
        {
            error = "saved BPElem binding does not match its backing bytes";
            return false;
        }
    }
    if (bindingIndex != image.bindings.size() || bindingIndex != boxesSize ||
        bindingIndex != image.activeElements)
    {
        error = "saved SAP box count differs from active BPElem set";
        return false;
    }
    PxU32 endpointCapacity = 0;
    if (!imageU32(image, "sap.endPointsCapacity", endpointCapacity, error))
        return false;
    if (boxesSize > (endpointCapacity - std::min(endpointCapacity, PxU32(NUM_SENTINELS))) / 2u ||
        endpointCapacity < NUM_SENTINELS)
    {
        error = "saved SAP endpoint capacity is too small";
        return false;
    }
    const PxU32 endpointCount = 2u * boxesSize + NUM_SENTINELS;
    for (PxU32 axis = 0; axis < 3; ++axis)
    {
        const std::string prefix = "sap.axis" + std::to_string(axis);
        const auto* boxMap = findBuffer(image, (prefix + ".boxes").c_str());
        const auto* values = findBuffer(image, (prefix + ".endpointValues").c_str());
        const auto* data = findBuffer(image, (prefix + ".endpointData").c_str());
        if (!boxMap || !values || !data ||
            boxMap->bytes.size() < c * sizeof(SapBox1D) ||
            values->bytes.size() < endpointCapacity * sizeof(PxcBPValType) ||
            data->bytes.size() < endpointCapacity * sizeof(PxcBpHandle))
        {
            error = prefix + ": saved endpoint inventory is incomplete";
            return false;
        }
        PxcBPValType minValue = 0, maxValue = 0;
        PxcBpHandle minData = 0, maxData = 0;
        imageArrayItem(*values, 0, minValue);
        imageArrayItem(*values, endpointCount - 1u, maxValue);
        imageArrayItem(*data, 0, minData);
        imageArrayItem(*data, endpointCount - 1u, maxData);
        if (minValue != 0 || maxValue != 0xffffffffu ||
            minData != (PX_INVALID_BP_HANDLE & ~PxcBpHandle(1)) ||
            maxData != PX_INVALID_BP_HANDLE)
        {
            error = prefix + ": sentinel mismatch";
            return false;
        }
        std::vector<unsigned char> endpointSeen(c, 0);
        for (PxU32 i = 1; i + 1 < endpointCount; ++i)
        {
            PxcBPValType previous = 0, current = 0;
            PxcBpHandle handle = 0;
            imageArrayItem(*values, i - 1u, previous);
            imageArrayItem(*values, i, current);
            imageArrayItem(*data, i, handle);
            const PxU32 owner = handle >> 1u;
            const PxU32 isMaximum = handle & 1u;
            if (previous > current || owner >= c || free[owner] ||
                (endpointSeen[owner] & (1u << isMaximum)))
            {
                error = prefix + ": endpoint order/owner is invalid";
                return false;
            }
            endpointSeen[owner] |= static_cast<unsigned char>(1u << isMaximum);
            SapBox1D box;
            imageArrayItem(*boxMap, owner, box);
            if (box.mMinMax[isMaximum] != i)
            {
                error = prefix + ": endpoint inverse map mismatch";
                return false;
            }
        }
        for (PxU32 id = 0; id < c; ++id)
            if (!free[id] && endpointSeen[id] != 3u)
            {
                error = prefix + ": active BPElem lacks two endpoints";
                return false;
            }
    }
    if (hashSize > hashCapacity || activePairs > activeCapacity ||
        activePairs > hashSize || (hashSize && mask != hashSize - 1u))
    {
        error = "saved pair manager size/capacity relation is invalid";
        return false;
    }
    std::vector<bool> seen(activePairs, false);
    for (PxU32 bucket = 0; bucket < hashSize; ++bucket)
    {
        PxcBpHandle index = PX_INVALID_BP_HANDLE;
        if (!imageArrayItem(*hashTable, bucket, index))
        {
            error = "saved pair hash bucket is absent";
            return false;
        }
        while (index != PX_INVALID_BP_HANDLE)
        {
            if (index >= activePairs || seen[index])
            {
                error = "saved pair hash chain is out of range or cyclic";
                return false;
            }
            seen[index] = true;
            PxcBroadPhasePair pair;
            if (!imageArrayItem(*pairData, index, pair) ||
                pair.mVolA > pair.mVolB || pair.mVolB >= c ||
                free[pair.mVolA] || free[pair.mVolB])
            {
                error = "saved pair refers to an invalid BPElem";
                return false;
            }
            if (!imageArrayItem(*next, index, index))
            {
                error = "saved pair next entry is absent";
                return false;
            }
        }
    }
    if (std::find(seen.begin(), seen.end(), false) != seen.end())
    {
        error = "saved active pair is absent from hash chains";
        return false;
    }
    return true;
}

void writeImage(const Plan& target, const SapImage& image,
                bool skipDeletedOverlapBuffer = false)
{
    for (std::size_t i = 0; i < target.buffers.size(); ++i)
    {
        if (skipDeletedOverlapBuffer &&
            target.buffers[i].name == "aabb.deletedOverlaps") continue;
        if (target.buffers[i].size)
            std::memcpy(const_cast<void*>(target.buffers[i].address),
                        image.buffers[i].bytes.data(), target.buffers[i].size);
    }
    for (std::size_t i = 0; i < target.scalars.size(); ++i)
        if (!target.scalars[i].topology)
            std::memcpy(target.scalars[i].address, image.scalars[i].bytes.data(),
                        target.scalars[i].size);
}

} // namespace

bool SapImage::equals(const SapImage& other, std::string& firstDifference) const
{
    firstDifference.clear();
    if (scene != other.scene || manager != other.manager || sap != other.sap)
    {
        firstDifference = "scene/SAP identity";
        return false;
    }
    if (activeElements != other.activeElements || bindings.size() != other.bindings.size())
    {
        firstDifference = "active BPElem count";
        return false;
    }
    for (std::size_t i = 0; i < bindings.size(); ++i)
        if (!sameBinding(bindings[i], other.bindings[i]))
        {
            firstDifference = "BPElem binding " + std::to_string(i);
            return false;
        }
    if (scalars.size() != other.scalars.size() || buffers.size() != other.buffers.size())
    {
        firstDifference = "image inventory";
        return false;
    }
    for (std::size_t i = 0; i < scalars.size(); ++i)
    {
        const Scalar& a = scalars[i];
        const Scalar& b = other.scalars[i];
        if (a.name != b.name || a.address != b.address ||
            a.topology != b.topology || a.bytes != b.bytes)
        {
            firstDifference = a.name;
            return false;
        }
    }
    for (std::size_t i = 0; i < buffers.size(); ++i)
    {
        const Buffer& a = buffers[i];
        const Buffer& b = other.buffers[i];
        // This is an owned, settled-frame result array. Replaying a cold
        // checkpoint allocates a new backing buffer at an arbitrary address.
        // The capacity scalar and every backing byte are still compared.
        const bool rebasedDeletedOverlaps =
            a.name == "aabb.deletedOverlaps" &&
            a.bytes.size() == 32u * sizeof(PxvBroadPhaseOverlap) &&
            b.bytes.size() == a.bytes.size() &&
            a.address != 0 && b.address != 0;
        if (a.name != b.name ||
            (!rebasedDeletedOverlaps && a.address != b.address) ||
            a.bytes != b.bytes)
        {
            firstDifference = a.name;
            return false;
        }
    }
    return true;
}

bool CaptureSap(PxScene& scene, SapImage& image, std::string& error)
{
    error.clear();
    Plan plan;
    if (!buildPlan(scene, plan, error)) return false;
    SapImage fresh;
    fresh.scene = plan.scene;
    fresh.manager = plan.manager;
    fresh.sap = plan.sap;
    fresh.activeElements = plan.activeElements;
    fresh.bindings = std::move(plan.bindings);
    for (const ScalarRef& ref : plan.scalars)
    {
        SapImage::Scalar s;
        s.name = ref.name;
        s.address = reinterpret_cast<std::uintptr_t>(ref.address);
        s.topology = ref.topology;
        const unsigned char* begin = static_cast<const unsigned char*>(ref.address);
        s.bytes.assign(begin, begin + ref.size);
        fresh.scalars.push_back(std::move(s));
    }
    for (const BufferRef& ref : plan.buffers)
    {
        SapImage::Buffer b;
        b.name = ref.name;
        b.address = reinterpret_cast<std::uintptr_t>(ref.address);
        if (ref.size)
        {
            const unsigned char* begin = static_cast<const unsigned char*>(ref.address);
            b.bytes.assign(begin, begin + ref.size);
        }
        fresh.buffers.push_back(std::move(b));
    }
    image = std::move(fresh);
    return true;
}

bool RestoreSap(PxScene& scene, const SapImage& image, std::string& error)
{
    error.clear();
    Plan live;
    if (!buildPlan(scene, live, error)) return false;
    if (image.scene != live.scene || image.manager != live.manager ||
        image.sap != live.sap)
    {
        error = "SAP image belongs to a different scene or manager";
        return false;
    }
    if (image.activeElements != live.activeElements ||
        image.bindings.size() != live.bindings.size())
    {
        error = "active BPElem set changed";
        return false;
    }
    for (std::size_t i = 0; i < live.bindings.size(); ++i)
        if (!sameBinding(image.bindings[i], live.bindings[i]))
        {
            error = "BPElem actor/shape identity changed at slot " +
                    std::to_string(live.bindings[i].id);
            return false;
        }
    if (image.scalars.size() != live.scalars.size() ||
        image.buffers.size() != live.buffers.size())
    {
        error = "SAP image inventory is incompatible with live scene";
        return false;
    }
    // The AABB manager starts with no deleted-overlap result array and
    // allocates 32 slots on its first deletion. At a settled frame this
    // buffer has already been consumed by Sc::Scene::finishBroadPhase, but
    // the manager still owns it. This is the only allocation change accepted
    // here: all other topology and allocation identities remain strict.
    PxsAABBManager& mgr = *reinterpret_cast<PxsAABBManager*>(live.manager);
    PxU32 savedDeletedCapacity = 0, savedDeletedSize = 0;
    if (!imageU32(image, "aabb.deletedOverlapCapacity", savedDeletedCapacity, error) ||
        !imageU32(image, "aabb.deletedOverlapSize", savedDeletedSize, error))
        return false;
    const SapImage::Buffer* savedDeleted =
        findBuffer(image, "aabb.deletedOverlaps");
    const bool coldDeletedRollback =
        savedDeletedCapacity == 0 && savedDeletedSize == 0 &&
        mgr.mDeletedPairsCapacity == 32 && mgr.mDeletedPairs != nullptr;
    if (coldDeletedRollback &&
        (!savedDeleted || savedDeleted->address != 0 ||
         !savedDeleted->bytes.empty()))
    {
        error = "cold deleted-overlap image must have a null, empty buffer";
        return false;
    }
    // Validate every remaining field before modifying the scene. The cold
    // result-array path detaches and frees one owned allocation after verify.
    for (std::size_t i = 0; i < live.scalars.size(); ++i)
    {
        const ScalarRef& target = live.scalars[i];
        const SapImage::Scalar& source = image.scalars[i];
        if (source.name != target.name ||
            source.address != reinterpret_cast<std::uintptr_t>(target.address) ||
            source.bytes.size() != target.size ||
            source.topology != target.topology)
        {
            error = "scalar layout mismatch at " + target.name;
            return false;
        }
        if (target.topology &&
            !(coldDeletedRollback &&
              target.name == "aabb.deletedOverlapCapacity") &&
            std::memcmp(target.address, source.bytes.data(), target.size) != 0)
        {
            error = "allocation capacity changed at " + target.name;
            return false;
        }
    }
    for (std::size_t i = 0; i < live.buffers.size(); ++i)
    {
        const BufferRef& target = live.buffers[i];
        const SapImage::Buffer& source = image.buffers[i];
        if (source.name != target.name ||
            (!(coldDeletedRollback && target.name == "aabb.deletedOverlaps") &&
             (source.address != reinterpret_cast<std::uintptr_t>(target.address) ||
              source.bytes.size() != target.size)))
        {
            error = "allocation identity/capacity changed at " + target.name;
            return false;
        }
    }
    if (!validateSavedImage(image, error)) return false;

    SapImage rollback;
    if (!CaptureSap(scene, rollback, error)) return false;
    PxvBroadPhaseOverlap* detachedDeletedPairs = nullptr;
    if (coldDeletedRollback)
    {
        // Retain ownership until the complete postwrite image verifies. If
        // verification fails, the original B allocation can be reattached.
        detachedDeletedPairs = mgr.mDeletedPairs;
    }
    writeImage(live, image, coldDeletedRollback);
    if (coldDeletedRollback)
    {
        mgr.mDeletedPairs = nullptr;
        mgr.mDeletedPairsCapacity = 0;
    }
    SapImage observed;
    std::string verifyError;
    bool verified = false;
    try
    {
        verified = CaptureSap(scene, observed, verifyError) &&
                   image.equals(observed, verifyError);
    }
    catch (...)
    {
        verifyError = "postwrite capture threw an exception";
    }
    if (!verified)
    {
        if (coldDeletedRollback)
        {
            mgr.mDeletedPairs = detachedDeletedPairs;
            mgr.mDeletedPairsCapacity = 32;
        }
        writeImage(live, rollback);
        SapImage reverted;
        std::string rollbackError;
        if (!CaptureSap(scene, reverted, rollbackError) ||
            !rollback.equals(reverted, rollbackError))
            std::abort(); // A failed rollback must never permit another simulation.
        error = "SAP restore verification failed and was rolled back: " + verifyError;
        return false;
    }
    if (coldDeletedRollback) PX_FREE(detachedDeletedPairs);
    return true;
}

}} // namespace oc2::offline
