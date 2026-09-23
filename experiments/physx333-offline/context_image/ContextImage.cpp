#include "ContextImage.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <set>
#include <type_traits>
#include <utility>

#include <windows.h>
#include "PxPhysicsAPI.h"

// These access shims are confined to this source-built Win32 experiment.
#define private public
#define protected public
#include "NpScene.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"
#include "PxsDynamics.h"
#include "PxsThreadContext.h"
#include "PxsThresholdTable.h"
#undef protected
#undef private

namespace oc2 { namespace offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "ContextImage requires Win32 PhysX 3.3.3");

template<class T>
void addField(ContextImage& image, const char* name, T& value,
              bool invariant = false)
{
    ContextImage::Field row;
    row.name = name;
    row.address = reinterpret_cast<std::uintptr_t>(&value);
    row.bytes.resize(sizeof(value));
    std::memcpy(row.bytes.data(), &value, sizeof(value));
    row.invariant = invariant;
    image.fields.push_back(std::move(row));
}

template<class A>
void addArray(ContextImage& image, const char* name, A& array,
              bool invariant = false, bool fullCapacityBytes = false)
{
    typedef typename std::remove_reference<decltype(array[0])>::type Element;
    // These enumerated source arrays hold values/pointers, not owning
    // elements. Cm::SpatialVector has a user-declared empty destructor, so a
    // trivial-destructor trait would reject it despite bytewise safe storage.
    ContextImage::Array row;
    row.name = name;
    row.object = reinterpret_cast<std::uintptr_t>(&array);
    row.data = reinterpret_cast<std::uintptr_t>(array.begin());
    row.sizeAddress = reinterpret_cast<std::uintptr_t>(&array.mSize);
    row.size = array.size();
    row.capacity = array.capacity();
    row.elementBytes = sizeof(Element);
    row.objectBytes = sizeof(array);
    row.fullCapacityBytes = fullCapacityBytes;
    row.bytes.resize(size_t(fullCapacityBytes ? row.capacity : row.size) *
                     row.elementBytes);
    if (!row.bytes.empty())
        std::memcpy(row.bytes.data(), array.begin(), row.bytes.size());
    row.invariant = invariant;
    image.arrays.push_back(std::move(row));
}

template<class A>
void addThreadArray(ContextImage& image, size_t index, const char* name,
                    A& array)
{
    const std::string key = "thread[" + std::to_string(index) + "]." + name;
    // resizeArrays() uses forceSize_Unsafe. Preserve capacity tails as well
    // as the currently published prefix for the next solver update.
    addArray(image, key.c_str(), array, false, true);
}

void addBitmap(ContextImage& image, const char* name, Cm::BitMap& bitmap,
               bool resetBeforeNextUse = false);

void addThreadBitmap(ContextImage& image, size_t index, const char* name,
                     Cm::BitMap& bitmap)
{
    const std::string key = "thread[" + std::to_string(index) + "]." + name;
    addBitmap(image, key.c_str(), bitmap, true);
}

void addBitmap(ContextImage& image, const char* name, Cm::BitMap& bitmap,
               bool resetBeforeNextUse)
{
    ContextImage::Bitmap row;
    row.name = name;
    row.object = reinterpret_cast<std::uintptr_t>(&bitmap);
    row.data = reinterpret_cast<std::uintptr_t>(bitmap.getWords());
    row.wordCount = bitmap.getWordCount();
    row.userMemory = bitmap.isInUserMemory();
    row.objectBytes = sizeof(bitmap);
    row.resetBeforeNextUse = resetBeforeNextUse;
    if (row.wordCount)
        row.words.assign(bitmap.getWords(), bitmap.getWords() + row.wordCount);
    image.bitmaps.push_back(std::move(row));
}

void addCachedThread(ContextImage& image, size_t index,
                     PxsThreadContext& thread)
{
    const size_t firstArray = image.arrays.size();
    const size_t firstBitmap = image.bitmaps.size();
    ContextImage::ThreadObject object;
    object.address = reinterpret_cast<std::uintptr_t>(&thread);
    object.compressedCacheSize = thread.mCompressedCacheSize;
    object.constraintSize = thread.mConstraintSize;
    object.bytes.resize(sizeof(thread));
    object.ignoredHeaderBytes.resize(sizeof(thread), 0);
    std::memcpy(object.bytes.data(), &thread, sizeof(thread));
    image.cachedThreads.push_back(std::move(object));

    addThreadArray(image, index, "constraintBlockTracking",
                   thread.mConstraintBlockManager.mTrackingArray);
    addThreadArray(image, index, "constraintsPerPartition",
                   thread.mConstraintsPerPartition);
    addThreadArray(image, index, "frictionConstraintsPerPartition",
                   thread.mFrictionConstraintsPerPartition);
    addThreadArray(image, index, "partitionNormalizationBitmap",
                   thread.mPartitionNormalizationBitmap);
    addThreadArray(image, index, "bodyCoreArray", thread.bodyCoreArray);
    addThreadArray(image, index, "accelerationArray", thread.accelerationArray);
    addThreadArray(image, index, "motionVelocityArray", thread.motionVelocityArray);
    addThreadArray(image, index, "contactConstraintDescArray",
                   thread.contactConstraintDescArray);
    addThreadArray(image, index, "tempConstraintDescArray",
                   thread.tempConstraintDescArray);
    addThreadArray(image, index, "frictionConstraintDescArray",
                   thread.frictionConstraintDescArray);
    addThreadArray(image, index, "orderedContactConstraints",
                   thread.orderedContactConstraints);
    addThreadArray(image, index, "contactConstraintBatchHeaders",
                   thread.contactConstraintBatchHeaders);
    addThreadArray(image, index, "frictionConstraintBatchHeaders",
                   thread.frictionConstraintBatchHeaders);
    addThreadArray(image, index, "compoundConstraints",
                   thread.compoundConstraints);
    addThreadArray(image, index, "orderedContactList",
                   thread.orderedContactList);
    addThreadArray(image, index, "tempContactList", thread.tempContactList);
    addThreadArray(image, index, "sortIndexArray", thread.sortIndexArray);
    addThreadArray(image, index, "thresholdStream", thread.mThresholdStream);
    addThreadArray(image, index, "articulations", thread.mArticulations);
    addThreadBitmap(image, index, "localChangeTouch", thread.mLocalChangeTouch);
    addThreadBitmap(image, index, "localChangedActors",
                    thread.mLocalChangedActors);
    ContextImage::ThreadObject& saved = image.cachedThreads.back();
    for (size_t i = firstArray; i < image.arrays.size(); ++i)
    {
        const ContextImage::Array& row = image.arrays[i];
        const size_t offset = row.object - saved.address;
        for (size_t j = 0; j < row.objectBytes; ++j)
        {
            saved.ignoredHeaderBytes[offset + j] = 1;
            saved.bytes[offset + j] = 0;
        }
    }
    for (size_t i = firstBitmap; i < image.bitmaps.size(); ++i)
    {
        const ContextImage::Bitmap& row = image.bitmaps[i];
        const size_t offset = row.object - saved.address;
        for (size_t j = 0; j < row.objectBytes; ++j)
        {
            saved.ignoredHeaderBytes[offset + j] = 1;
            saved.bytes[offset + j] = 0;
        }
    }
}

bool zeroBitmap(const Cm::BitMap& bitmap)
{
    for (PxU32 i = 0; i < bitmap.getWordCount(); ++i)
        if (bitmap.getWords()[i]) return false;
    return true;
}

bool addTable(ContextImage& image, PxsThresholdTable& table,
              std::string& error)
{
    ContextImage::ThresholdTable& row = image.thresholdTable;
    row.object = reinterpret_cast<std::uintptr_t>(&table);
    row.buffer = reinterpret_cast<std::uintptr_t>(table.mBuffer);
    row.hash = reinterpret_cast<std::uintptr_t>(table.mHash);
    row.pairs = reinterpret_cast<std::uintptr_t>(table.mPairs);
    row.nexts = reinterpret_cast<std::uintptr_t>(table.mNexts);
    row.hashSize = table.mHashSize;
    row.hashCapacity = table.mHashCapactiy;
    row.pairsSize = table.mPairsSize;
    row.pairsCapacity = table.mPairsCapacity;
    if (!row.buffer)
    {
        if (row.hash || row.pairs || row.nexts || row.hashSize ||
            row.hashCapacity || row.pairsSize || row.pairsCapacity)
        {
            error = "threshold table has null buffer with live metadata";
            return false;
        }
        return true;
    }
    if (row.hashCapacity != 2 * row.pairsCapacity + 1 ||
        row.hashSize > row.hashCapacity || row.pairsSize > row.pairsCapacity ||
        row.pairsCapacity > 1000000)
    {
        error = "threshold table sizes are inconsistent";
        return false;
    }
    const size_t pairBytes = size_t(row.pairsCapacity) * sizeof(PxsThresholdTable::Pair);
    const size_t nextBytes = size_t(row.pairsCapacity) * sizeof(PxU32);
    const size_t hashBytes = size_t(row.hashCapacity) * sizeof(PxU32);
    if (row.pairs != row.buffer || row.nexts != row.buffer + pairBytes ||
        row.hash != row.buffer + pairBytes + nextBytes)
    {
        error = "threshold table backing layout differs from source";
        return false;
    }
    row.bytes.resize(pairBytes + nextBytes + hashBytes);
    std::memcpy(row.bytes.data(), table.mBuffer, row.bytes.size());
    return true;
}

bool sameTableLayout(const ContextImage::ThresholdTable& target,
                     const ContextImage::ThresholdTable& live,
                     std::string& error)
{
    if (target.object != live.object || target.buffer != live.buffer ||
        target.hash != live.hash || target.pairs != live.pairs ||
        target.nexts != live.nexts ||
        target.hashCapacity != live.hashCapacity ||
        target.pairsCapacity != live.pairsCapacity ||
        target.bytes.size() != live.bytes.size() ||
        target.hashSize > target.hashCapacity ||
        target.pairsSize > target.pairsCapacity ||
        (!target.buffer && (target.hashSize || target.pairsSize)))
    {
        error = "threshold table identity, allocation, or sizes changed";
        return false;
    }
    return true;
}

bool preflight(const ContextImage& target, const ContextImage& live,
               std::string& error)
{
    if (target.scene != live.scene || target.context != live.context ||
        target.dynamics != live.dynamics || target.actors != live.actors ||
        target.threadCacheHeader != live.threadCacheHeader ||
        target.cachedThreadOrder != live.cachedThreadOrder ||
        target.cachedThreads.size() != live.cachedThreads.size() ||
        target.fields.size() != live.fields.size() ||
        target.arrays.size() != live.arrays.size() ||
        target.bitmaps.size() != live.bitmaps.size())
    {
        error = "context scene, actor, or image schema changed";
        return false;
    }
    for (size_t i = 0; i < target.cachedThreads.size(); ++i)
    {
        const ContextImage::ThreadObject& a = target.cachedThreads[i];
        const ContextImage::ThreadObject& b = live.cachedThreads[i];
        std::uintptr_t nextEntry = 0;
        if (a.address != b.address || a.address != target.cachedThreadOrder[i] ||
            a.bytes.size() != sizeof(PxsThreadContext) ||
            b.bytes.size() != sizeof(PxsThreadContext) ||
            a.ignoredHeaderBytes != b.ignoredHeaderBytes ||
            a.ignoredHeaderBytes.size() != a.bytes.size())
        {
            error = "cached thread-context identity or image size changed";
            return false;
        }
        std::memcpy(&nextEntry, a.bytes.data(), sizeof(void*));
        const std::uintptr_t expectedNext =
            i + 1 < target.cachedThreadOrder.size() ?
            target.cachedThreadOrder[i + 1] : 0;
        if (nextEntry != expectedNext)
        {
            error = "cached thread-context image corrupts its LIFO link";
            return false;
        }
        const PxsThreadContext* thread =
            reinterpret_cast<const PxsThreadContext*>(a.address);
        const size_t compressedOffset =
            reinterpret_cast<std::uintptr_t>(&thread->mCompressedCacheSize) -
            a.address;
        const size_t constraintOffset =
            reinterpret_cast<std::uintptr_t>(&thread->mConstraintSize) -
            a.address;
        PxU32 compressed = 0, constraint = 0;
        std::memcpy(&compressed, a.bytes.data() + compressedOffset,
                    sizeof(compressed));
        std::memcpy(&constraint, a.bytes.data() + constraintOffset,
                    sizeof(constraint));
        if (compressed != a.compressedCacheSize ||
            constraint != a.constraintSize)
        {
            error = "cached thread size counters differ from object payload";
            return false;
        }
        for (size_t j = 0; j < a.bytes.size(); ++j)
            if (a.ignoredHeaderBytes[j] && a.bytes[j])
            {
                error = "cached thread image has data in an ignored header";
                return false;
            }
    }
    for (size_t i = 0; i < target.fields.size(); ++i)
    {
        const ContextImage::Field& a = target.fields[i];
        const ContextImage::Field& b = live.fields[i];
        if (a.name != b.name || a.address != b.address ||
            a.bytes.size() != b.bytes.size() || a.invariant != b.invariant ||
            (a.invariant && a.bytes != b.bytes))
        {
            error = a.name + " field identity or invariant changed";
            return false;
        }
    }
    // The static-world datum must remain bound to this dynamics context's
    // embedded solver body. Copying a stale or corrupt pointer would make the
    // next constraint setup dereference unrelated memory.
    std::uintptr_t worldSolverBody = 0;
    PxcSolverBodyData worldData;
    bool foundBody = false, foundData = false;
    for (const ContextImage::Field& row : target.fields)
    {
        if (row.name == "dynamics.worldSolverBody")
        {
            worldSolverBody = row.address;
            foundBody = true;
        }
        else if (row.name == "dynamics.worldSolverBodyData")
        {
            if (row.bytes.size() != sizeof(worldData))
            {
                error = "world solver-body data has invalid size";
                return false;
            }
            std::memcpy(&worldData, row.bytes.data(), sizeof(worldData));
            foundData = true;
        }
    }
    if (!foundBody || !foundData ||
        reinterpret_cast<std::uintptr_t>(worldData.solverBody) !=
            worldSolverBody || worldData.originalBody)
    {
        error = "world solver-body bindings are invalid";
        return false;
    }
    for (size_t i = 0; i < target.arrays.size(); ++i)
    {
        const ContextImage::Array& a = target.arrays[i];
        const ContextImage::Array& b = live.arrays[i];
        if (a.name != b.name || a.object != b.object ||
            a.data != b.data || a.sizeAddress != b.sizeAddress ||
            a.capacity != b.capacity ||
            a.elementBytes != b.elementBytes ||
            a.objectBytes != b.objectBytes || a.size > a.capacity ||
            a.fullCapacityBytes != b.fullCapacityBytes ||
            a.bytes.size() !=
                size_t(a.fullCapacityBytes ? a.capacity : a.size) *
                    a.elementBytes ||
            a.invariant != b.invariant ||
            (a.invariant && (a.size != b.size || a.bytes != b.bytes)))
        {
            error = a.name + " array identity, allocation, or payload changed";
            return false;
        }
    }
    for (size_t i = 0; i < target.bitmaps.size(); ++i)
    {
        const ContextImage::Bitmap& a = target.bitmaps[i];
        const ContextImage::Bitmap& b = live.bitmaps[i];
        if (a.name != b.name || a.object != b.object ||
            a.objectBytes != b.objectBytes ||
            a.resetBeforeNextUse != b.resetBeforeNextUse ||
            (!a.resetBeforeNextUse &&
             (a.data != b.data || a.wordCount != b.wordCount ||
              a.userMemory != b.userMemory ||
              a.words.size() != a.wordCount)))
        {
            error = a.name + " bitmap allocation or payload changed";
            return false;
        }
    }
    return sameTableLayout(target.thresholdTable, live.thresholdTable, error);
}

void apply(const ContextImage& image)
{
    for (const ContextImage::Field& row : image.fields)
        if (!row.invariant)
            std::memcpy(reinterpret_cast<void*>(row.address),
                        row.bytes.data(), row.bytes.size());
    for (const ContextImage::ThreadObject& thread : image.cachedThreads)
    {
        unsigned char* destination =
            reinterpret_cast<unsigned char*>(thread.address);
        for (size_t i = 0; i < thread.bytes.size(); ++i)
            if (!thread.ignoredHeaderBytes[i])
                destination[i] = thread.bytes[i];
    }
    for (const ContextImage::Array& row : image.arrays)
        if (!row.invariant)
        {
            if (!row.bytes.empty())
                std::memcpy(reinterpret_cast<void*>(row.data),
                            row.bytes.data(), row.bytes.size());
            *reinterpret_cast<PxU32*>(row.sizeAddress) = row.size;
        }
    for (const ContextImage::Bitmap& row : image.bitmaps)
        if (!row.resetBeforeNextUse && !row.words.empty())
            std::memcpy(reinterpret_cast<void*>(row.data), row.words.data(),
                        row.words.size() * sizeof(PxU32));
    const ContextImage::ThresholdTable& table = image.thresholdTable;
    if (!table.bytes.empty())
        std::memcpy(reinterpret_cast<void*>(table.buffer), table.bytes.data(),
                    table.bytes.size());
    PxsThresholdTable& actual =
        *reinterpret_cast<PxsThresholdTable*>(table.object);
    actual.mHashSize = table.hashSize;
    actual.mPairsSize = table.pairsSize;
}

} // namespace

bool ContextImage::equals(const ContextImage& other,
                          std::string& firstDifference) const
{
    firstDifference.clear();
    if (scene != other.scene || context != other.context ||
        dynamics != other.dynamics || actors != other.actors ||
        threadCacheHeader != other.threadCacheHeader ||
        cachedThreadOrder != other.cachedThreadOrder ||
        cachedThreads.size() != other.cachedThreads.size() ||
        fields.size() != other.fields.size() ||
        arrays.size() != other.arrays.size() ||
        bitmaps.size() != other.bitmaps.size())
    {
        firstDifference = "context identity or schema";
        return false;
    }
    for (size_t i = 0; i < cachedThreads.size(); ++i)
        if (cachedThreads[i].address != other.cachedThreads[i].address ||
            cachedThreads[i].compressedCacheSize !=
                other.cachedThreads[i].compressedCacheSize ||
            cachedThreads[i].constraintSize !=
                other.cachedThreads[i].constraintSize ||
            cachedThreads[i].ignoredHeaderBytes !=
                other.cachedThreads[i].ignoredHeaderBytes ||
            cachedThreads[i].bytes != other.cachedThreads[i].bytes)
        {
            size_t first = 0;
            const size_t length =
                (std::min)(cachedThreads[i].bytes.size(),
                           other.cachedThreads[i].bytes.size());
            while (first < length && cachedThreads[i].bytes[first] ==
                                     other.cachedThreads[i].bytes[first])
                ++first;
            firstDifference = "cached thread context " + std::to_string(i) +
                              " byte " + std::to_string(first) + " " +
                              std::to_string(first < cachedThreads[i].bytes.size() ?
                                  cachedThreads[i].bytes[first] : 999) + " vs " +
                              std::to_string(first < other.cachedThreads[i].bytes.size() ?
                                  other.cachedThreads[i].bytes[first] : 999);
            return false;
        }
    for (size_t i = 0; i < fields.size(); ++i)
        if (fields[i].name != other.fields[i].name ||
            fields[i].address != other.fields[i].address ||
            fields[i].bytes != other.fields[i].bytes ||
            fields[i].invariant != other.fields[i].invariant)
        {
            firstDifference = fields[i].name;
            return false;
        }
    for (size_t i = 0; i < arrays.size(); ++i)
    {
        const Array& a = arrays[i];
        const Array& b = other.arrays[i];
        if (a.name != b.name || a.object != b.object || a.data != b.data ||
            a.sizeAddress != b.sizeAddress || a.size != b.size ||
            a.capacity != b.capacity || a.elementBytes != b.elementBytes ||
            a.objectBytes != b.objectBytes ||
            a.fullCapacityBytes != b.fullCapacityBytes ||
            a.bytes != b.bytes || a.invariant != b.invariant)
        {
            firstDifference = a.name;
            return false;
        }
    }
    for (size_t i = 0; i < bitmaps.size(); ++i)
    {
        const Bitmap& a = bitmaps[i];
        const Bitmap& b = other.bitmaps[i];
        if (a.name != b.name || a.object != b.object ||
            a.objectBytes != b.objectBytes ||
            a.resetBeforeNextUse != b.resetBeforeNextUse ||
            (!a.resetBeforeNextUse &&
             (a.data != b.data || a.wordCount != b.wordCount ||
              a.userMemory != b.userMemory || a.words != b.words)))
        {
            firstDifference = a.name;
            return false;
        }
    }
    const ThresholdTable& a = thresholdTable;
    const ThresholdTable& b = other.thresholdTable;
    if (a.object != b.object || a.buffer != b.buffer || a.hash != b.hash ||
        a.pairs != b.pairs || a.nexts != b.nexts ||
        a.hashSize != b.hashSize || a.hashCapacity != b.hashCapacity ||
        a.pairsSize != b.pairsSize || a.pairsCapacity != b.pairsCapacity ||
        a.bytes != b.bytes)
    {
        firstDifference = "threshold table";
        return false;
    }
    return true;
}

bool CaptureContextImage(PxScene& scene, ContextImage& image,
                         std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    if (scene.getFlags() & PxSceneFlag::eENABLE_CCD)
    {
        error = "CCD scene is outside this context image";
        return false;
    }
    if (scene.getNbConstraints() || scene.getNbArticulations() ||
        scene.getNbAggregates())
    {
        error = "constraint, articulation, and aggregate context unsupported";
        return false;
    }
    Sc::InteractionScene& interactions =
        np.getScene().getScScene().getInteractionScene();
    PxsContext& context = *interactions.getLowLevelContext();
    if (context.mContactModifyCallback ||
        !zeroBitmap(context.mModifiableContactManager) ||
        context.mModifiablePairArray.size())
    {
        error = "contact modification context unsupported";
        return false;
    }
    if (!context.mDynamicsContext || !context.mCCDContext)
    {
        error = "low-level dynamics or CCD context missing";
        return false;
    }
    PxsDynamicsContext& dynamics = *context.mDynamicsContext;
    if (dynamics.mWorldSolverBodyData.solverBody !=
        &dynamics.mWorldSolverBody)
    {
        error = "world solver-body data has unexpected binding";
        return false;
    }
    ContextImage next;
    next.scene = reinterpret_cast<std::uintptr_t>(&scene);
    next.context = reinterpret_cast<std::uintptr_t>(&context);
    next.dynamics = reinterpret_cast<std::uintptr_t>(&dynamics);
    PSLIST_HEADER threadHeader = reinterpret_cast<PSLIST_HEADER>(
        context.mThreadContextPool.root.mImpl);
    if (!threadHeader)
    {
        error = "thread-context cache has no SList header";
        return false;
    }
    next.threadCacheHeader = reinterpret_cast<std::uintptr_t>(threadHeader);
    std::set<std::uintptr_t> seenThreads;
    for (PSLIST_ENTRY entry = threadHeader->Next.Next;
         entry; entry = entry->Next)
    {
        const std::uintptr_t address =
            reinterpret_cast<std::uintptr_t>(entry);
        if (!seenThreads.insert(address).second ||
            next.cachedThreadOrder.size() >= 64)
        {
            error = "thread-context cache is cyclic or unexpectedly large";
            return false;
        }
        next.cachedThreadOrder.push_back(address);
        PxsThreadContext* thread = static_cast<PxsThreadContext*>(
            reinterpret_cast<
                PxcThreadCoherantCache<PxsThreadContext>::EntryBase*>(entry));
        addCachedThread(next, next.cachedThreadOrder.size() - 1, *thread);
    }
    if (next.cachedThreadOrder.size() != threadHeader->Depth)
    {
        error = "thread-context cache depth differs from its chain";
        return false;
    }
    const PxActorTypeFlags actorTypes =
        PxActorTypeFlag::eRIGID_STATIC | PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 actorCount = scene.getNbActors(actorTypes);
    std::vector<PxActor*> actors(actorCount);
    if (actorCount && scene.getActors(actorTypes, actors.data(), actorCount) !=
        actorCount)
    {
        error = "rigid actor enumeration changed during capture";
        return false;
    }
    for (PxActor* actor : actors)
        next.actors.push_back(reinterpret_cast<std::uintptr_t>(actor));

    addField(next, "context.touchLost", context.mTouchesLost);
    addField(next, "context.touchFound", context.mTouchesFound);
    addField(next, "context.touchEventCounts", context.mCMTouchEventCount);
    addField(next, "context.fastMovingShapes", context.mNumFastMovingShapes);
    addField(next, "context.simStats", context.mSimStats);
    addField(next, "context.batchedContext", context.mBatchedContext, true);
    addField(next, "context.index", context.mIndex, true);
    addField(next, "context.ccdContext", context.mCCDContext, true);
    addField(next, "context.frictionType", context.mFrictionType, true);
    addField(next, "context.pcm", context.mPCM, true);
    addField(next, "context.contactCache", context.mContactCache, true);
    addField(next, "context.averagePoint", context.mCreateAveragePoint, true);
    addField(next, "context.createContactStream", context.mCreateContactStream, true);
    addField(next, "context.meshContactMargin", context.mMeshContactMargin, true);
    addField(next, "context.correlationDistance", context.mCorrelationDistance, true);
    addField(next, "context.toleranceLength", context.mToleranceLength, true);

    addField(next, "dynamics.dt", dynamics.mDt);
    addField(next, "dynamics.invDt", dynamics.mInvDt);
    addField(next, "dynamics.maxSolverConstraintSize",
             dynamics.mMaxSolverConstraintSize);
    addField(next, "dynamics.kinematicCount", dynamics.mKinematicCount);
    addField(next, "dynamics.worldSolverBody", dynamics.mWorldSolverBody);
    addField(next, "dynamics.worldSolverBodyData",
             dynamics.mWorldSolverBodyData);
    addField(next, "dynamics.context", dynamics.mContext, true);
    addField(next, "dynamics.solverCore", dynamics.mSolverCore, true);
    addField(next, "dynamics.bounceThreshold",
             dynamics.mBounceThreshold, true);
    addField(next, "dynamics.frictionOffsetThreshold",
             dynamics.mFrictionOffsetThreshold, true);
    addField(next, "dynamics.solverBatchSize",
             dynamics.mSolverBatchSize, true);

    const size_t firstContextArray = next.arrays.size();
    addArray(next, "threshold.stream", context.mThresholdStream);
    addArray(next, "solver.bodies", dynamics.mSolverBodyPool);
    addArray(next, "solver.bodyData", dynamics.mSolverBodyDataPool);
    addArray(next, "scratch.batchPrim", context.mBatchWorkUnitArrayPrim, true);
    addArray(next, "scratch.batchCnvx", context.mBatchWorkUnitArrayCnvx, true);
    addArray(next, "scratch.batchHF", context.mBatchWorkUnitArrayHF, true);
    addArray(next, "scratch.batchMesh", context.mBatchWorkUnitArrayMesh, true);
    addArray(next, "scratch.batchCnvxMesh",
             context.mBatchWorkUnitArrayCnvxMesh, true);
    addArray(next, "scratch.batchOther", context.mBatchWorkUnitArrayOther, true);
    for (size_t i = firstContextArray + 3; i < next.arrays.size(); ++i)
        if (next.arrays[i].size)
        {
            error = next.arrays[i].name + " is nonempty at settled boundary";
            return false;
        }
    addBitmap(next, "changedAABBHandles", context.mChangedAABBMgrHandles);
    if (!addTable(next, context.mThresholdTable, error)) return false;
    image = std::move(next);
    error.clear();
    return true;
}

bool RestoreContextImage(PxScene& scene, const ContextImage& image,
                         std::string& error)
{
    ContextImage live;
    if (!CaptureContextImage(scene, live, error) ||
        !preflight(image, live, error))
        return false;
    apply(image);
    ContextImage observed;
    std::string difference;
    if (!CaptureContextImage(scene, observed, error) ||
        !image.equals(observed, difference))
    {
        apply(live);
        error = "context restore verification failed: " +
                (difference.empty() ? error : difference);
        return false;
    }
    error.clear();
    return true;
}

}} // namespace oc2::offline
