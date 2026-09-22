#include "QueryImage.h"

#include <cstddef>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <utility>

#include <windows.h>
#include "PxPhysicsAPI.h"

// Private access is restricted to this source-built offline test component.
#define private public
#define protected public
#include "NpScene.h"
#include "SqAABBPruner.h"
#include "SqAABBTree.h"
#undef protected
#undef private

namespace oc2 { namespace offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "Query image requires Win32 PhysX 3.3.3");

// SqAABBTree.cpp defines FIFOStack2 locally rather than in a header.
// This is its pinned Win32 layout: Ps::Array<NodeAndParent>, then mCurIndex.
struct StackEntryMirror
{
    Sq::AABBTreeNode* node;
    Sq::AABBTreeNode* parent;
};
struct FIFOStackMirror
{
    Ps::Array<StackEntryMirror> entries;
    PxU32 currentIndex;
};
static_assert(sizeof(FIFOStackMirror) == 16,
              "Unexpected PhysX 3.3.3 FIFOStack2 layout");

constexpr std::size_t kMaxImageBytes = 16u * 1024u * 1024u;
const char* const kRebasedStackData = "dynamic.newTreeStorage.stackData";
const char* const kRebasedStackCapacity = "dynamic.newTreeStorage.stackCapacity";
const char* const kRebasedStackSize = "dynamic.newTreeStorage.stackSize";
const char* const kRebasedStackStorage = "dynamic.newTreeStorage.stackStorage";
const char* const kColdIndicesPointer = "dynamic.newTreeStorage.indicesPointer";
const char* const kColdNodesPointer = "dynamic.newTreeStorage.nodesPointer";
const char* const kColdStackPointer = "dynamic.newTreeStorage.stackPointer";
const char* const kColdIndices = "dynamic.newTreeStorage.indices";
const char* const kColdNodes = "dynamic.newTreeStorage.nodes";
const char* const kColdProgress = "dynamic.progress";
const char* const kColdBuilderNodePointer = "dynamic.builderNodePointer";
const char* const kColdStackCursor = "dynamic.newTreeStorage.stackCursor";

bool coldTopologyScalar(const std::string& name)
{
    return name == kColdProgress || name == kColdBuilderNodePointer ||
           name == kColdIndicesPointer || name == kColdNodesPointer ||
           name == kColdStackPointer;
}

const QueryImage::Field* findField(const QueryImage& image, const char* name)
{
    for (const QueryImage::Field& field : image.fields)
        if (field.name == name) return &field;
    return nullptr;
}

bool readWord(const QueryImage::Field* field, PxU32& value)
{
    if (!field || field->bytes.size() != sizeof(value)) return false;
    std::memcpy(&value, field->bytes.data(), sizeof(value));
    return true;
}

bool validStackBuffer(const QueryImage& image, PxU32& capacity,
                      PxU32& size)
{
    const QueryImage::Field* data = findField(image, kRebasedStackData);
    const QueryImage::Field* storage = findField(image, kRebasedStackStorage);
    const QueryImage::Field* cap = findField(image, kRebasedStackCapacity);
    if (!storage)
        return false;
    PxU32 pointer = 0;
    if (!readWord(data, pointer) || !readWord(cap, capacity) ||
        !readWord(findField(image, kRebasedStackSize), size))
        return false;
    if (size > capacity ||
        storage->bytes.size() != std::size_t(capacity) * sizeof(StackEntryMirror) ||
        (capacity ? pointer != storage->address : pointer != 0))
        return false;
    return true;
}

bool coldBuildRequested(const QueryImage& target, const QueryImage& before)
{
    PxU32 targetPhase = 0, beforePhase = 0;
    return readWord(findField(target, kColdProgress), targetPhase) &&
           readWord(findField(before, kColdProgress), beforePhase) &&
           targetPhase == Sq::BUILD_INIT &&
           beforePhase == Sq::BUILD_IN_PROGRESS;
}

bool preflightColdBuild(const QueryImage& target, const QueryImage& before,
                        std::string& error)
{
    static const char* const extra[] = {
        kRebasedStackData, kRebasedStackCapacity, kRebasedStackSize,
        kColdStackCursor, kRebasedStackStorage
    };
    if (target.scene != before.scene ||
        target.fields.size() + sizeof(extra) / sizeof(extra[0]) !=
            before.fields.size() ||
        findField(target, kRebasedStackData) ||
        !findField(before, kRebasedStackData))
    {
        error = "cold query build has an unexpected field layout";
        return false;
    }
    for (std::size_t i = 0; i < sizeof(extra) / sizeof(extra[0]); ++i)
        if (before.fields[target.fields.size() + i].name != extra[i])
        {
            error = "cold query build has unexpected FIFO fields";
            return false;
        }
    PxU32 targetIndices = 1, targetNodes = 1, targetStack = 1;
    PxU32 beforeIndices = 0, beforeNodes = 0, beforeStack = 0;
    PxU32 targetBuilderNode = 1, beforeBuilderNode = 0;
    const QueryImage::Field* targetIndexBytes = findField(target, kColdIndices);
    const QueryImage::Field* targetNodeBytes = findField(target, kColdNodes);
    const QueryImage::Field* beforeIndexBytes = findField(before, kColdIndices);
    const QueryImage::Field* beforeNodeBytes = findField(before, kColdNodes);
    PxU32 stackCapacity = 0, stackSize = 0;
    if (!readWord(findField(target, kColdIndicesPointer), targetIndices) ||
        !readWord(findField(target, kColdNodesPointer), targetNodes) ||
        !readWord(findField(target, kColdStackPointer), targetStack) ||
        !readWord(findField(target, kColdBuilderNodePointer), targetBuilderNode) ||
        !readWord(findField(before, kColdIndicesPointer), beforeIndices) ||
        !readWord(findField(before, kColdNodesPointer), beforeNodes) ||
        !readWord(findField(before, kColdStackPointer), beforeStack) ||
        !readWord(findField(before, kColdBuilderNodePointer), beforeBuilderNode) ||
        !targetIndexBytes || !targetNodeBytes || !beforeIndexBytes ||
        !beforeNodeBytes || targetIndices || targetNodes || targetStack ||
        targetBuilderNode || !beforeIndices || !beforeNodes || !beforeStack ||
        beforeBuilderNode != beforeNodes ||
        targetIndexBytes->address || !targetIndexBytes->bytes.empty() ||
        targetNodeBytes->address || !targetNodeBytes->bytes.empty() ||
        beforeIndexBytes->address != beforeIndices ||
        beforeIndexBytes->bytes.empty() ||
        beforeNodeBytes->address != beforeNodes ||
        beforeNodeBytes->bytes.empty() ||
        !validStackBuffer(before, stackCapacity, stackSize) || !stackCapacity)
    {
        error = "cold query build has inconsistent tree allocations";
        return false;
    }
    for (std::size_t i = 0; i < target.fields.size(); ++i)
    {
        const QueryImage::Field& to = target.fields[i];
        const QueryImage::Field& from = before.fields[i];
        const bool releasedArray = to.name == kColdIndices ||
                                   to.name == kColdNodes;
        if (to.name != from.name || to.invariant != from.invariant ||
            (!releasedArray &&
             (to.address != from.address ||
              to.bytes.size() != from.bytes.size())) ||
            (to.invariant && to.bytes != from.bytes &&
             !coldTopologyScalar(to.name)))
        {
            error = "cold query build changed unrelated storage at " + to.name;
            return false;
        }
    }
    return true;
}

bool addBytes(QueryImage& image, const std::string& name,
              const void* address, std::size_t bytes, bool invariant,
              std::string& error)
{
    if (bytes > kMaxImageBytes || (bytes && !address))
    {
        error = name + " has an invalid allocation";
        return false;
    }
    QueryImage::Field row;
    row.name = name;
    row.address = reinterpret_cast<std::uintptr_t>(address);
    row.invariant = invariant;
    row.bytes.resize(bytes);
    if (bytes) std::memcpy(row.bytes.data(), address, bytes);
    image.fields.push_back(std::move(row));
    return true;
}

template <typename T>
bool addField(QueryImage& image, const std::string& name,
              const T& value, bool invariant, std::string& error)
{
    return addBytes(image, name, &value, sizeof(value), invariant, error);
}

template <typename T>
bool addArray(QueryImage& image, const std::string& name,
              T* data, std::size_t count, bool invariant,
              std::string& error)
{
    if (count > kMaxImageBytes / sizeof(T))
    {
        error = name + " exceeds image limit";
        return false;
    }
    return addBytes(image, name, data, count * sizeof(T), invariant, error);
}

void hashU64(std::uint64_t& hash, std::uint64_t value)
{
    for (unsigned i = 0; i < 8; ++i)
    {
        hash ^= (value >> (i * 8)) & 0xff;
        hash *= UINT64_C(1099511628211);
    }
}

std::uint64_t seal(const QueryImage& image)
{
    std::uint64_t hash = UINT64_C(14695981039346656037);
    hashU64(hash, image.scene);
    hashU64(hash, image.fields.size());
    for (const QueryImage::Field& field : image.fields)
    {
        hashU64(hash, field.address);
        hashU64(hash, field.invariant);
        hashU64(hash, field.name.size());
        for (char byte : field.name)
        {
            hash ^= static_cast<std::uint8_t>(byte);
            hash *= UINT64_C(1099511628211);
        }
        hashU64(hash, field.bytes.size());
        for (std::uint8_t byte : field.bytes)
        {
            hash ^= byte;
            hash *= UINT64_C(1099511628211);
        }
    }
    return hash;
}

bool addPool(QueryImage& image, const char* prefix,
             Sq::PruningPool& pool, std::string& error)
{
    const std::string p(prefix);
    if (pool.mNbObjects > pool.mMaxNbObjects ||
        pool.mFirstFreshHandle > pool.mMaxNbObjects ||
        pool.mMaxNbObjects > 65536)
    {
        error = p + " has invalid pool counts";
        return false;
    }
    if (!addField(image, p + ".objects", pool.mNbObjects, true, error) ||
        !addField(image, p + ".capacity", pool.mMaxNbObjects, true, error) ||
        !addField(image, p + ".firstFresh", pool.mFirstFreshHandle, true, error) ||
        !addField(image, p + ".freeHead", pool.mHandleFreeList, true, error) ||
        !addField(image, p + ".boxPointer", pool.mWorldBoxes, true, error) ||
        !addField(image, p + ".payloadPointer", pool.mObjects, true, error) ||
        !addField(image, p + ".handleToIndexPointer", pool.mHandleToIndex, true, error) ||
        !addField(image, p + ".indexToHandlePointer", pool.mIndexToHandle, true, error) ||
        !addArray(image, p + ".boxes", pool.mWorldBoxes, pool.mNbObjects, false, error) ||
        !addArray(image, p + ".payloads", pool.mObjects, pool.mNbObjects, true, error) ||
        !addArray(image, p + ".handleToIndex", pool.mHandleToIndex,
                  pool.mFirstFreshHandle, true, error) ||
        !addArray(image, p + ".indexToHandle", pool.mIndexToHandle,
                  pool.mNbObjects, true, error))
        return false;
    return true;
}

bool addTree(QueryImage& image, const char* prefix,
             Sq::AABBTree* tree, PxU32 allocatedPrimitives,
             std::string& error)
{
    const std::string p(prefix);
    if (!tree) return true;
    if (tree->mTotalNbNodes > 131071 ||
        tree->mTotalPrims > 65536 ||
        tree->mRefitBitmask.mSize > 8192 ||
        tree->mNbRefitNodes > SUPPORT_UPDATE_ARRAY)
    {
        error = p + " has unsupported tree storage";
        return false;
    }
    if (tree->mPool && !allocatedPrimitives)
    {
        error = p + " has a tree with no pool primitives";
        return false;
    }
    const PxU32 indexCount = tree->mIndices ? allocatedPrimitives : 0;
    const PxU32 storageNodes = tree->mPool ?
        allocatedPrimitives * 2 - 1 : 0;
    if (allocatedPrimitives > 65536 ||
        tree->mTotalNbNodes > storageNodes)
    {
        error = p + " has excessive build primitives";
        return false;
    }
    if ((tree->mPool && !tree->mIndices) ||
        (tree->mIndices && !tree->mPool))
    {
        error = p + " has incomplete tree allocations";
        return false;
    }
    if (!addField(image, p + ".indicesPointer", tree->mIndices, true, error) ||
        !addField(image, p + ".nodesPointer", tree->mPool, true, error) ||
        !addField(image, p + ".stackPointer", tree->mStack, true, error) ||
        !addField(image, p + ".nodeCount", tree->mTotalNbNodes, true, error) ||
        !addField(image, p + ".primitiveCount", tree->mTotalPrims, true, error) ||
        !addField(image, p + ".refitBitsPointer", tree->mRefitBitmask.mBits, true, error) ||
        !addField(image, p + ".refitWordCount", tree->mRefitBitmask.mSize, true, error) ||
        !addField(image, p + ".refitHighestWord", tree->mRefitHighestSetWord, false, error) ||
        !addField(image, p + ".refitCount", tree->mNbRefitNodes, false, error) ||
        !addArray(image, p + ".indices", tree->mIndices,
                  indexCount, false, error) ||
        !addArray(image, p + ".nodes", tree->mPool,
                  storageNodes, false, error) ||
        !addArray(image, p + ".refitBits", tree->mRefitBitmask.mBits,
                  tree->mRefitBitmask.mSize, false, error) ||
        !addArray(image, p + ".refitArray", tree->mRefitArray,
                  SUPPORT_UPDATE_ARRAY, false, error))
        return false;
    if (tree->mStack)
    {
        FIFOStackMirror& stack = *reinterpret_cast<FIFOStackMirror*>(tree->mStack);
        if (stack.entries.size() > stack.entries.capacity() ||
            stack.entries.capacity() > 131071 ||
            stack.currentIndex > stack.entries.size())
        {
            error = p + " has an invalid FIFO build stack";
            return false;
        }
        if (!addField(image, p + ".stackData", stack.entries.mData, true, error) ||
            !addField(image, p + ".stackCapacity", stack.entries.mCapacity, true, error) ||
            !addField(image, p + ".stackSize", stack.entries.mSize, false, error) ||
            !addField(image, p + ".stackCursor", stack.currentIndex, false, error) ||
            !addArray(image, p + ".stackStorage", stack.entries.begin(),
                      stack.entries.capacity(), false, error))
            return false;
    }
    return true;
}

bool addPruner(QueryImage& image, const char* prefix,
               Sq::AABBPruner& pruner, bool incremental,
               std::string& error)
{
    const std::string p(prefix);
    const Sq::BucketPrunerCore& bucket = pruner.mBucketPruner;
    const bool building = pruner.mProgress == Sq::BUILD_INIT ||
                          pruner.mProgress == Sq::BUILD_IN_PROGRESS;
    if (pruner.mIncrementalRebuild != incremental ||
        (!building && pruner.mProgress != Sq::BUILD_NOT_STARTED) ||
        (building && (!incremental || !pruner.mNewTree ||
                      !pruner.mCachedBoxes || !pruner.mNbCachedBoxes ||
                      pruner.mBuilder.mAABBArray != pruner.mCachedBoxes ||
                      !pruner.mDoSaveFixups)) ||
        (!building && (pruner.mNewTree || pruner.mCachedBoxes ||
                       pruner.mNbCachedBoxes || pruner.mBuilder.mNodeBase ||
                       pruner.mBuilder.mAABBArray || pruner.mDoSaveFixups)) ||
        pruner.mUncommittedChanges ||
        pruner.mBuf0.size() || pruner.mBuf1.size() ||
        pruner.mNewTreeFixups.size() ||
        bucket.mCoreNbObjects || bucket.mCoreCapacity || bucket.mNbFree ||
        bucket.mSortedNb || bucket.mSortedCapacity ||
        bucket.mCoreBoxes || bucket.mCoreObjects || bucket.mCoreRemap ||
        bucket.mSortedWorldBoxes || bucket.mSortedObjects ||
        bucket.mMap.size())
    {
        error = p + " has unsupported rebuild, bucket, or commit state";
        return false;
    }
    if (building && (pruner.mBuilder.mNbPrimitives != pruner.mNbCachedBoxes ||
        (pruner.mProgress == Sq::BUILD_INIT &&
         (pruner.mBuilder.mNodeBase || pruner.mNewTree->mStack ||
          pruner.mNewTree->mPool || pruner.mNewTree->mIndices)) ||
        (pruner.mProgress == Sq::BUILD_IN_PROGRESS &&
         (pruner.mBuilder.mNodeBase != pruner.mNewTree->mPool ||
          !pruner.mNewTree->mStack || !pruner.mNewTree->mPool ||
          !pruner.mNewTree->mIndices))))
    {
        error = p + " builder and second tree disagree";
        return false;
    }
    if (!addField(image, p + ".currentTree", pruner.mAABBTree, true, error) ||
        !addField(image, p + ".newTree", pruner.mNewTree, true, error) ||
        !addField(image, p + ".cachedBoxes", pruner.mCachedBoxes, true, error) ||
        !addField(image, p + ".cachedBoxCount", pruner.mNbCachedBoxes, true, error) ||
        !addArray(image, p + ".cachedBoxStorage", pruner.mCachedBoxes,
                  pruner.mNbCachedBoxes, false, error) ||
        !addField(image, p + ".progress", pruner.mProgress, true, error) ||
        !addField(image, p + ".incremental", pruner.mIncrementalRebuild, true, error) ||
        !addField(image, p + ".builderSettings", pruner.mBuilder.mSettings, true, error) ||
        !addField(image, p + ".builderInputCount", pruner.mBuilder.mNbPrimitives, true, error) ||
        !addField(image, p + ".builderInputPointer", pruner.mBuilder.mAABBArray, true, error) ||
        !addField(image, p + ".builderNodePointer", pruner.mBuilder.mNodeBase, true, error) ||
        !addField(image, p + ".builderTotalPrims", pruner.mBuilder.mTotalPrims, false, error) ||
        !addField(image, p + ".builderNodeCount", pruner.mBuilder.mCount, false, error) ||
        !addField(image, p + ".builderInvalidSplits", pruner.mBuilder.mNbInvalidSplits, false, error) ||
        !addField(image, p + ".addedBuffer", pruner.mAddedObjects, true, error) ||
        !addField(image, p + ".removalBuffer", pruner.mToRemoveFromBucket, true, error) ||
        !addField(image, p + ".bucketDirty", bucket.mDirty, false, error) ||
        !addField(image, p + ".bucketMap", bucket.mMap, true, error) ||
        !addField(image, p + ".bucketSortAxis", bucket.mSortAxis, true, error) ||
        !addField(image, p + ".bucketGlobalBox", bucket.mGlobalBox, true, error) ||
        !addField(image, p + ".bucketLevel1", bucket.mLevel1, true, error) ||
        !addField(image, p + ".bucketLevel2", bucket.mLevel2, true, error) ||
        !addField(image, p + ".bucketLevel3", bucket.mLevel3, true, error) ||
        !addField(image, p + ".bucketOwnMemory", bucket.mOwnMemory, true, error) ||
        !addField(image, p + ".buffer0Object", pruner.mBuf0, true, error) ||
        !addField(image, p + ".buffer1Object", pruner.mBuf1, true, error) ||
        !addField(image, p + ".fixupPointer", pruner.mNewTreeFixups.mData, true, error) ||
        !addField(image, p + ".fixupSize", pruner.mNewTreeFixups.mSize, true, error) ||
        !addField(image, p + ".fixupCapacity", pruner.mNewTreeFixups.mCapacity, true, error) ||
        !addField(image, p + ".rebuildCalls", pruner.mNbCalls, false, error) ||
        !addField(image, p + ".rebuildHint", pruner.mRebuildRateHint, true, error) ||
        !addField(image, p + ".totalWork", pruner.mTotalWorkUnits, false, error) ||
        !addField(image, p + ".adaptiveTerm", pruner.mAdaptiveRebuildTerm, false, error) ||
        !addField(image, p + ".needsNewTree", pruner.mNeedsNewTree, false, error) ||
        !addField(image, p + ".committed", pruner.mUncommittedChanges, true, error) ||
        !addField(image, p + ".saveFixups", pruner.mDoSaveFixups, true, error) ||
        !addField(image, p + ".mapPointer", pruner.mTreeMap.mMapping.mData, true, error) ||
        !addField(image, p + ".mapSize", pruner.mTreeMap.mMapping.mSize, true, error) ||
        !addField(image, p + ".mapCapacity", pruner.mTreeMap.mMapping.mCapacity, true, error) ||
        !addArray(image, p + ".map", pruner.mTreeMap.mMapping.begin(),
                  pruner.mTreeMap.mMapping.size(), true, error) ||
        !addPool(image, (p + ".pool").c_str(), pruner.mPool, error) ||
        !addTree(image, (p + ".tree").c_str(), pruner.mAABBTree,
                 pruner.mPool.mNbObjects, error) ||
        !addTree(image, (p + ".newTreeStorage").c_str(), pruner.mNewTree,
                 pruner.mNbCachedBoxes, error))
        return false;
    return true;
}

bool capture(PxScene& scene, QueryImage& out, std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.mPhysicsRunning || np.mCollisionRunning || np.mIsBuffering)
    {
        error = "scene is not settled after fetchResults";
        return false;
    }
    Sq::SceneQueryManager& manager = np.getSceneQueryManagerFast();
    if (manager.mPrunerType[0] != PxPruningStructure::eSTATIC_AABB_TREE ||
        manager.mPrunerType[1] != PxPruningStructure::eDYNAMIC_AABB_TREE ||
        !manager.mPruners[0] || !manager.mPruners[1])
    {
        error = "only static AABB tree + dynamic AABB tree are supported";
        return false;
    }
    if (manager.mDirtyList.size() > manager.mDirtyList.capacity() ||
        manager.mDirtyList.capacity() > 65536)
    {
        error = "query dirty list exceeds supported capacity";
        return false;
    }
    QueryImage result;
    result.scene = reinterpret_cast<std::uintptr_t>(&scene);
    if (!addField(result, "manager.staticPruner", manager.mPruners[0], true, error) ||
        !addField(result, "manager.dynamicPruner", manager.mPruners[1], true, error) ||
        !addField(result, "manager.staticType", manager.mPrunerType[0], true, error) ||
        !addField(result, "manager.dynamicType", manager.mPrunerType[1], true, error) ||
        !addField(result, "manager.rebuildHint", manager.mRebuildRateHint, true, error) ||
        !addField(result, "manager.staticTimestamp", manager.mTimestamp[0], false, error) ||
        !addField(result, "manager.dynamicTimestamp", manager.mTimestamp[1], false, error) ||
        !addField(result, "manager.dirtyListPointer", manager.mDirtyList.mData, true, error) ||
        !addField(result, "manager.dirtyListCapacity", manager.mDirtyList.mCapacity, true, error) ||
        !addField(result, "manager.dirtyListSize", manager.mDirtyList.mSize, false, error) ||
        !addArray(result, "manager.dirtyListStorage", manager.mDirtyList.begin(),
                  manager.mDirtyList.capacity(), false, error))
        return false;

    for (PxU32 i = 0; i < 2; ++i)
    {
        const std::string p = i ? "manager.dynamicDirty" : "manager.staticDirty";
        Cm::BitMap& dirty = manager.mDirtyMap[i];
        if (dirty.getWordCount() > 8192 ||
            !addField(result, p + ".pointer", dirty.mMap, true, error) ||
            !addField(result, p + ".wordCount", dirty.mWordCount, true, error) ||
            !addArray(result, p + ".words", dirty.getWords(),
                      dirty.getWordCount(), false, error))
            return false;
    }
    if (!addPruner(result, "static", *static_cast<Sq::AABBPruner*>(manager.mPruners[0]),
                   false, error) ||
        !addPruner(result, "dynamic", *static_cast<Sq::AABBPruner*>(manager.mPruners[1]),
                   true, error))
        return false;

    for (PxU32 i = 0; i < manager.mDirtyList.size(); ++i)
    {
        Sq::ActorShape* ref = manager.mDirtyList[i];
        const PxU32 index = Sq::SceneQueryManager::getPrunerIndex(ref);
        const PxU32 handle = Sq::SceneQueryManager::getPrunerHandle(ref);
        if (index > 1 || handle >= manager.mDirtyMap[index].size() ||
            !manager.mDirtyMap[index].test(handle))
        {
            error = "query dirty list and bitmap disagree";
            return false;
        }
    }
    result.seal = seal(result);
    out = std::move(result);
    return true;
}

} // namespace

bool QueryImage::equals(const QueryImage& other, std::string& error) const
{
    if (scene != other.scene || fields.size() != other.fields.size())
    {
        error = "query image scene or field count differs";
        return false;
    }
    for (std::size_t i = 0; i < fields.size(); ++i)
    {
        const Field& a = fields[i];
        const Field& b = other.fields[i];
        if (a.name != b.name || a.address != b.address ||
            a.invariant != b.invariant || a.bytes != b.bytes)
        {
            error = "query image differs at " + a.name;
            if (a.bytes.size() == sizeof(PxU32) && b.bytes.size() == sizeof(PxU32))
            {
                PxU32 lhs, rhs;
                std::memcpy(&lhs, a.bytes.data(), sizeof(lhs));
                std::memcpy(&rhs, b.bytes.data(), sizeof(rhs));
                error += " (" + std::to_string(lhs) + " versus " +
                         std::to_string(rhs) + ")";
            }
            return false;
        }
    }
    return true;
}

bool QueryImage::equalsWithRebasedStack(const QueryImage& other,
                                        std::string& error) const
{
    if (scene != other.scene || fields.size() != other.fields.size())
    {
        error = "query image scene or field count differs";
        return false;
    }
    for (std::size_t i = 0; i < fields.size(); ++i)
    {
        const Field& a = fields[i];
        const Field& b = other.fields[i];
        if (a.name != b.name || a.invariant != b.invariant ||
            a.bytes.size() != b.bytes.size() ||
            (a.address != b.address && a.name != kRebasedStackStorage) ||
            (a.bytes != b.bytes && a.name != kRebasedStackData))
        {
            error = "query image differs at " + a.name;
            return false;
        }
    }
    const bool haveStack = findField(*this, kRebasedStackData) != nullptr;
    if (haveStack != (findField(other, kRebasedStackData) != nullptr))
    {
        error = "query FIFO stack layout differs";
        return false;
    }
    if (haveStack)
    {
        PxU32 lhsCap = 0, lhsSize = 0, rhsCap = 0, rhsSize = 0;
        if (!validStackBuffer(*this, lhsCap, lhsSize) ||
            !validStackBuffer(other, rhsCap, rhsSize) ||
            lhsCap != rhsCap || lhsSize != rhsSize)
        {
            error = "query FIFO stack backing is inconsistent";
            return false;
        }
    }
    return true;
}

bool QueryImage::equalsWithRebuiltColdTree(const QueryImage& other,
                                           std::string& error) const
{
    PxU32 lhsPhase = 0, rhsPhase = 0;
    if (scene != other.scene || fields.size() != other.fields.size() ||
        !readWord(findField(*this, kColdProgress), lhsPhase) ||
        !readWord(findField(other, kColdProgress), rhsPhase) ||
        lhsPhase != Sq::BUILD_IN_PROGRESS || rhsPhase != lhsPhase)
    {
        error = "query images are not matching in-progress cold builds";
        return false;
    }
    const auto validTree = [](const QueryImage& image,
                              PxU32& nodeBase, PxU32& nodeCount) {
        PxU32 indices = 0, stack = 0, builderBase = 0;
        PxU32 stackCapacity = 0, stackSize = 0;
        const Field* indexBytes = findField(image, kColdIndices);
        const Field* nodeBytes = findField(image, kColdNodes);
        if (!readWord(findField(image, kColdIndicesPointer), indices) ||
            !readWord(findField(image, kColdNodesPointer), nodeBase) ||
            !readWord(findField(image, kColdStackPointer), stack) ||
            !readWord(findField(image, kColdBuilderNodePointer), builderBase) ||
            !indexBytes || !nodeBytes || !indices || !nodeBase || !stack ||
            indexBytes->address != indices || indexBytes->bytes.empty() ||
            nodeBytes->address != nodeBase || nodeBytes->bytes.empty() ||
            nodeBytes->bytes.size() % sizeof(Sq::AABBTreeNode) ||
            builderBase != nodeBase ||
            !validStackBuffer(image, stackCapacity, stackSize))
            return false;
        nodeCount = static_cast<PxU32>(nodeBytes->bytes.size() /
                                       sizeof(Sq::AABBTreeNode));
        return nodeCount != 0;
    };
    PxU32 lhsNodes = 0, rhsNodes = 0, lhsCount = 0, rhsCount = 0;
    if (!validTree(*this, lhsNodes, lhsCount) ||
        !validTree(other, rhsNodes, rhsCount) || lhsCount != rhsCount)
    {
        error = "rebuilt query tree has inconsistent pointer ownership";
        return false;
    }
    const auto firstBuildStep = [](const QueryImage& image) {
        PxU32 builderCount = 0, treeCount = 1, treePrims = 1;
        PxU32 stackSize = 0, stackCursor = 1;
        return readWord(findField(image, "dynamic.builderNodeCount"),
                        builderCount) && builderCount == 1 &&
               readWord(findField(image,
                        "dynamic.newTreeStorage.nodeCount"), treeCount) &&
               !treeCount &&
               readWord(findField(image,
                        "dynamic.newTreeStorage.primitiveCount"),
                        treePrims) && !treePrims &&
               readWord(findField(image, kRebasedStackSize), stackSize) &&
               stackSize == 1 &&
               readWord(findField(image, kColdStackCursor), stackCursor) &&
               !stackCursor;
    };
    if (!firstBuildStep(*this) || !firstBuildStep(other))
    {
        error = "rebuilt query comparator supports only the first build step";
        return false;
    }
    const auto movedAddress = [](const std::string& name) {
        return name == kColdIndices || name == kColdNodes ||
               name == kRebasedStackData || name == kRebasedStackCapacity ||
               name == kRebasedStackSize || name == kColdStackCursor ||
               name == kRebasedStackStorage;
    };
    const auto movedValue = [](const std::string& name) {
        return name == kColdIndicesPointer || name == kColdNodesPointer ||
               name == kColdStackPointer || name == kColdBuilderNodePointer ||
               name == kRebasedStackData;
    };
    for (std::size_t i = 0; i < fields.size(); ++i)
    {
        const Field& a = fields[i];
        const Field& b = other.fields[i];
        if (a.name != b.name || a.invariant != b.invariant ||
            a.bytes.size() != b.bytes.size() ||
            (a.address != b.address && !movedAddress(a.name)) ||
            (a.bytes != b.bytes && !movedValue(a.name) &&
             a.name != kRebasedStackStorage && a.name != kColdNodes))
        {
            error = "rebuilt query image differs at " + a.name;
            return false;
        }
    }
    const Field* lhsNodeBytes = findField(*this, kColdNodes);
    const Field* rhsNodeBytes = findField(other, kColdNodes);
    if (!lhsNodeBytes || !rhsNodeBytes)
    {
        error = "rebuilt query node storage is absent";
        return false;
    }
    for (std::size_t i = 0; i < lhsCount; ++i)
    {
        PxU64 lhsBits = 0, rhsBits = 0;
        const std::size_t offset = i * sizeof(Sq::AABBTreeNode) +
                                   offsetof(Sq::AABBTreeNode, mBitfield);
        std::memcpy(&lhsBits, lhsNodeBytes->bytes.data() + offset,
                    sizeof(lhsBits));
        std::memcpy(&rhsBits, rhsNodeBytes->bytes.data() + offset,
                    sizeof(rhsBits));
        if (lhsBits != rhsBits)
        {
            error = "rebuilt query initialized node bits differ";
            return false;
        }
    }
    const Field* lhsStack = findField(*this, kRebasedStackStorage);
    const Field* rhsStack = findField(other, kRebasedStackStorage);
    if (!lhsStack || !rhsStack ||
        lhsStack->bytes.size() != rhsStack->bytes.size() ||
        lhsStack->bytes.size() % sizeof(StackEntryMirror))
    {
        error = "rebuilt query FIFO storage is invalid";
        return false;
    }
    const auto nodeOffset = [](PxU32 pointer, PxU32 base,
                               PxU32 count, PxU32& offset) {
        if (pointer < base) return false;
        const PxU32 delta = pointer - base;
        if (delta % sizeof(Sq::AABBTreeNode)) return false;
        offset = delta / sizeof(Sq::AABBTreeNode);
        return offset < count;
    };
    for (std::size_t i = 0; i < lhsStack->bytes.size();
         i += sizeof(StackEntryMirror))
    {
        StackEntryMirror lhsEntry, rhsEntry;
        std::memcpy(&lhsEntry, lhsStack->bytes.data() + i, sizeof(lhsEntry));
        std::memcpy(&rhsEntry, rhsStack->bytes.data() + i, sizeof(rhsEntry));
        PxU32 lhsNode = 0, rhsNode = 0, lhsParent = 0, rhsParent = 0;
        if (!nodeOffset(reinterpret_cast<PxU32>(lhsEntry.node), lhsNodes,
                        lhsCount, lhsNode) ||
            !nodeOffset(reinterpret_cast<PxU32>(rhsEntry.node), rhsNodes,
                        rhsCount, rhsNode) ||
            !nodeOffset(reinterpret_cast<PxU32>(lhsEntry.parent), lhsNodes,
                        lhsCount, lhsParent) ||
            !nodeOffset(reinterpret_cast<PxU32>(rhsEntry.parent), rhsNodes,
                        rhsCount, rhsParent) ||
            lhsNode != rhsNode || lhsParent != rhsParent)
        {
            error = "rebuilt query FIFO node references differ";
            return false;
        }
    }
    return true;
}

bool CaptureQueryImage(PxScene& scene, QueryImage& out, std::string& error)
{
    error.clear();
    return capture(scene, out, error);
}

bool RestoreQueryImage(PxScene& scene, const QueryImage& target,
                       std::string& error)
{
    error.clear();
    if (target.seal != seal(target))
    {
        error = "query image was modified after capture";
        return false;
    }
    QueryImage before;
    if (!capture(scene, before, error)) return false;
    if (coldBuildRequested(target, before))
    {
        if (!preflightColdBuild(target, before, error)) return false;
        typedef unsigned (__cdecl* ReleaseColdTreeFn)(void*);
        HMODULE physxDll = GetModuleHandleA("PhysX3_x86.dll");
        FARPROC exported = physxDll ? GetProcAddress(
            physxDll, "oc2_physx333_query_release_cold_v1") : NULL;
        if (!exported && physxDll)
            exported = GetProcAddress(
                physxDll, "_oc2_physx333_query_release_cold_v1");
        if (!exported)
        {
            error = "source query lifecycle bridge is unavailable";
            return false;
        }
        // The exact reverse of progressiveBuild(progress=0): release only the
        // second tree's indices, nodes and FIFO, then restore its owning
        // pruner's checkpoint fields. This may free storage, so a failed
        // postcondition cannot be rolled back to B at the old addresses.
        NpScene& np = static_cast<NpScene&>(scene);
        Sq::SceneQueryManager& manager = np.getSceneQueryManagerFast();
        Sq::AABBPruner& dynamic =
            *static_cast<Sq::AABBPruner*>(manager.mPruners[1]);
        ReleaseColdTreeFn releaseColdTree =
            reinterpret_cast<ReleaseColdTreeFn>(exported);
        if (releaseColdTree(dynamic.mNewTree))
        {
            std::fputs("Fatal cold query lifecycle bridge failure\n", stderr);
            std::abort();
        }
        for (const QueryImage::Field& field : target.fields)
            if ((!field.invariant || coldTopologyScalar(field.name)) &&
                !field.bytes.empty())
                std::memcpy(reinterpret_cast<void*>(field.address),
                            field.bytes.data(), field.bytes.size());
        QueryImage observed;
        std::string verifyError;
        if (capture(scene, observed, verifyError) &&
            target.equals(observed, verifyError)) return true;
        std::fprintf(stderr,
                     "Fatal cold query rewind verification failure: %s\n",
                     verifyError.c_str());
        std::abort();
    }
    if (target.scene != before.scene ||
        target.fields.size() != before.fields.size())
    {
        error = "query image belongs to another scene or layout";
        return false;
    }
    // The source FIFO is a Ps::Array. A later progressive build may double
    // its allocation, freeing the checkpoint buffer. Never write to that
    // stale address. Reuse the currently owned (at least as large) buffer,
    // lower its logical capacity, and let Ps::Array grow it on replay.
    bool rebaseStack = false;
    const QueryImage::Field* targetStack = findField(target, kRebasedStackStorage);
    const QueryImage::Field* beforeStack = findField(before, kRebasedStackStorage);
    if (targetStack || beforeStack)
    {
        PxU32 targetCap = 0, targetSize = 0;
        PxU32 beforeCap = 0, beforeSize = 0;
        if (!validStackBuffer(target, targetCap, targetSize) ||
            !validStackBuffer(before, beforeCap, beforeSize))
        {
            error = "query FIFO stack image is inconsistent";
            return false;
        }
        rebaseStack = targetStack->address != beforeStack->address ||
            targetCap != beforeCap;
        if (rebaseStack && (!targetCap || targetCap > beforeCap ||
                            targetStack->address == beforeStack->address))
        {
            error = "query FIFO stack cannot be safely rebased";
            return false;
        }
    }
    for (std::size_t i = 0; i < target.fields.size(); ++i)
    {
        const QueryImage::Field& to = target.fields[i];
        const QueryImage::Field& from = before.fields[i];
        const bool movedStorage = rebaseStack && to.name == kRebasedStackStorage;
        const bool movedData = rebaseStack && to.name == kRebasedStackData;
        const bool lowerCapacity = rebaseStack && to.name == kRebasedStackCapacity;
        if (to.name != from.name ||
            (!movedStorage && to.address != from.address) ||
            to.invariant != from.invariant ||
            (!movedStorage && to.bytes.size() != from.bytes.size()))
        {
            error = "query storage changed at " + to.name;
            return false;
        }
        if (to.invariant && to.bytes != from.bytes &&
            !movedData && !lowerCapacity)
        {
            error = "query topology changed at " + to.name;
            return false;
        }
    }
    for (const QueryImage::Field& field : target.fields)
        if ((!field.invariant ||
             (rebaseStack && field.name == kRebasedStackCapacity)) &&
            !field.bytes.empty())
        {
            const std::uintptr_t address =
                rebaseStack && field.name == kRebasedStackStorage ?
                beforeStack->address : field.address;
            std::memcpy(reinterpret_cast<void*>(address),
                        field.bytes.data(), field.bytes.size());
        }

    QueryImage observed;
    std::string verifyError;
    if (capture(scene, observed, verifyError) &&
        (rebaseStack ? target.equalsWithRebasedStack(observed, verifyError) :
                       target.equals(observed, verifyError))) return true;

    // All writes above have fixed addresses, so this rollback cannot allocate.
    for (const QueryImage::Field& field : before.fields)
        if ((!field.invariant ||
             (rebaseStack && field.name == kRebasedStackCapacity)) &&
            !field.bytes.empty())
            std::memcpy(reinterpret_cast<void*>(field.address),
                        field.bytes.data(), field.bytes.size());
    error = "query restore verification failed: " + verifyError;
    return false;
}

}} // namespace oc2::offline
