#include "MemBlockRestore.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <map>
#include <set>
#include <sstream>
#include <utility>

#include "PxPhysicsAPI.h"

// Private access is limited to this source-only, pinned-ABI experiment.
#define private public
#define protected public
#include "NpScene.h"
#include "ScScene.h"
#include "ScInteractionScene.h"
#include "ScShapeInstancePairLL.h"
#include "PxsContext.h"
#include "PxcNpMemBlockPool.h"
#include "PxcScratchAllocator.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "memory-block restore requires Win32 PhysX");
static_assert(PxcNpMemBlock::SIZE == 16384, "unexpected PhysX block size");

const char* const kArrayNames[] = {
    "constraints", "contacts[0]", "contacts[1]", "friction[0]",
    "friction[1]", "npCache[0]", "npCache[1]", "scratchBlocks",
    "unused"
};
const std::size_t kArrayCount = sizeof(kArrayNames) / sizeof(kArrayNames[0]);

struct MutableArrays {
    PxcNpMemBlockArray* array[kArrayCount];
};

MutableArrays arraysOf(PxcNpMemBlockPool& pool)
{
    MutableArrays arrays = {{
        &pool.mConstraints, &pool.mContacts[0], &pool.mContacts[1],
        &pool.mFriction[0], &pool.mFriction[1], &pool.mNpCache[0],
        &pool.mNpCache[1], &pool.mScratchBlocks, &pool.mUnused
    }};
    return arrays;
}

PxcNpMemBlockPool* poolOf(PxScene& scene)
{
    NpScene& np = static_cast<NpScene&>(scene);
    PxsContext* context = np.getScene().getScScene()
                              .getInteractionScene().getLowLevelContext();
    return context ? &context->getNpMemBlockPool() : NULL;
}

bool captureBindings(PxScene& scene,
                     std::vector<MemBlockContactBinding>& bindings,
                     std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    Sc::InteractionScene& interactions =
        np.getScene().getScScene().getInteractionScene();
    Cm::Range<Sc::Interaction*const> range =
        interactions.getInteractions(Sc::PX_INTERACTION_TYPE_OVERLAP);
    std::set<std::uintptr_t> seen;
    while (!range.empty())
    {
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        if (!sip->mManager) continue;
        PxsContactManager& cm = *sip->mManager;
        MemBlockContactBinding binding;
        binding.managerAddress = reinterpret_cast<std::uintptr_t>(&cm);
        if (!seen.insert(binding.managerAddress).second)
        {
            error = "contact manager occurs in more than one live interaction";
            return false;
        }
        const PxcNpWorkUnit& work = cm.getWorkUnit();
        binding.managerIndex = cm.getIndex();
        binding.workFlags = work.flags;
        binding.statusFlags = work.statusFlags;
        binding.contactCount = work.contactCount;
        binding.pairCachePairData = work.pairCache.pairData;
        binding.solverConstraint =
            reinterpret_cast<std::uintptr_t>(work.solverConstraintPointer);
        binding.solverConstraintSize = work.solverConstraintSize;
        binding.compressedContacts =
            reinterpret_cast<std::uintptr_t>(work.compressedContacts);
        binding.compressedContactSize = work.compressedContactSize;
        binding.frictionData =
            reinterpret_cast<std::uintptr_t>(work.frictionDataPtr);
        binding.frictionPatchCount = work.frictionPatchCount;
        binding.npCache = reinterpret_cast<std::uintptr_t>(work.pairCache.ptr);
        binding.npCacheSize = work.pairCache.size;
        binding.ccdContacts = reinterpret_cast<std::uintptr_t>(work.ccdContacts);
        binding.manifold = work.pairCache.manifold;
        bindings.push_back(binding);
    }
    std::sort(bindings.begin(), bindings.end(),
              [](const MemBlockContactBinding& a,
                 const MemBlockContactBinding& b) {
                  return a.managerAddress < b.managerAddress;
              });
    return true;
}

bool registriesEqual(const MemBlockIdentityRegistry& a,
                     const MemBlockIdentityRegistry& b)
{
    return a.sceneAddress == b.sceneAddress &&
           a.liveHeapOrdinals == b.liveHeapOrdinals &&
           a.liveExceptionalOrdinals == b.liveExceptionalOrdinals &&
           a.nextHeapOrdinal == b.nextHeapOrdinal &&
           a.nextExceptionalOrdinal == b.nextExceptionalOrdinal;
}

bool sameStorage(const MemBlockImage& a, const MemBlockImage& b,
                 std::string& error, bool allowOwnerTransfer = false)
{
    if (a.sceneAddress != b.sceneAddress ||
        a.poolAddress != b.poolAddress ||
        a.scratchAllocatorAddress != b.scratchAllocatorAddress ||
        a.scratchArenaAddress != b.scratchArenaAddress ||
        a.scratchBlockAddress != b.scratchBlockAddress ||
        a.exceptionalArrayAddress != b.exceptionalArrayAddress ||
        a.arrays.size() != b.arrays.size())
    {
        error = "pool, allocator, or array topology belongs to another scene";
        return false;
    }
    for (std::size_t i = 0; i < a.arrays.size(); ++i)
    {
        const MemBlockImage::Array& x = a.arrays[i];
        const MemBlockImage::Array& y = b.arrays[i];
        if (x.address != y.address || x.capacity != y.capacity ||
            (!allowOwnerTransfer && x.size != y.size) || x.name != y.name ||
            x.size > x.capacity || y.size > y.capacity)
        {
            error = std::string("array storage or size changed: ") + x.name;
            return false;
        }
    }
    return true;
}

bool validateImage(const MemBlockRestoreImage& target,
                   const MemBlockRestoreImage& current,
                   std::string& error, bool allowOwnerTransfer)
{
    const MemBlockImage& saved = target.pool;
    const MemBlockImage& live = current.pool;
    if (!registriesEqual(target.registry, current.registry))
    {
        error = "live allocation identity registry differs from checkpoint";
        return false;
    }
    if (!saved.unsupported.empty() || !live.unsupported.empty())
    {
        error = "memory-block capture contains unsupported ownership state";
        return false;
    }
    if (!sameStorage(saved, live, error, allowOwnerTransfer)) return false;
    if (saved.arrays.size() != kArrayCount ||
        saved.maxBlocks != live.maxBlocks ||
        saved.initialBlocks != live.initialBlocks ||
        saved.scratchArenaSize != live.scratchArenaSize ||
        saved.scratchStackCapacity != live.scratchStackCapacity ||
        saved.scratchStackOffsets != live.scratchStackOffsets ||
        saved.scratchStackOffsets.size() != 1 ||
        saved.scratchBlockCount || live.scratchBlockCount ||
        saved.exceptionalArrayCapacity != live.exceptionalArrayCapacity ||
        !saved.exceptionalOrdinals.empty() ||
        !live.exceptionalOrdinals.empty() ||
        !target.registry.liveExceptionalOrdinals.empty() ||
        saved.scratchArenaBytes.size() != saved.scratchArenaSize ||
        live.scratchArenaBytes.size() != live.scratchArenaSize ||
        saved.npCacheActiveStream > 1 || saved.frictionActiveStream > 1 ||
        saved.ccdCacheActiveStream > 1 || saved.contactIndex > 1)
    {
        error = "pool configuration, scratch, or exceptional-allocation guard failed";
        return false;
    }
    // A stopped scratch allocator has only its initial stack entry. PhysX
    // returns the arena end here; a null arena has the sentinel offset.
    const std::uint32_t expectedStack = saved.scratchArenaAddress
        ? saved.scratchArenaSize : UINT32_MAX;
    if (saved.scratchStackOffsets[0] != expectedStack)
    {
        error = "scratch allocator retains an outstanding suballocation";
        return false;
    }

    std::size_t targetEntryCount = 0;
    std::size_t liveEntryCount = 0;
    for (std::size_t i = 0; i < kArrayCount; ++i)
    {
        targetEntryCount += saved.arrays[i].size;
        liveEntryCount += live.arrays[i].size;
    }
    if (targetEntryCount != saved.blocks.size() ||
        liveEntryCount != live.blocks.size())
    {
        error = "block payload count does not match array entries";
        return false;
    }

    typedef std::map<std::uintptr_t, MemBlockImage::BlockRef> BlockMap;
    BlockMap targetBlocks;
    BlockMap liveBlocks;
    for (std::size_t i = 0; i < saved.blocks.size(); ++i)
    {
        const MemBlockImage::Block& block = saved.blocks[i];
        if (!block.address || block.bytes.size() != PxcNpMemBlock::SIZE ||
            block.identity.kind != MemBlockImage::BlockRef::Heap ||
            !targetBlocks.insert(std::make_pair(block.address,
                                                block.identity)).second)
        {
            error = "checkpoint block identity, address, or payload is invalid";
            return false;
        }
        const std::map<std::uintptr_t, std::uint32_t>::const_iterator id =
            target.registry.liveHeapOrdinals.find(block.address);
        if (id == target.registry.liveHeapOrdinals.end() ||
            id->second != block.identity.ordinal)
        {
            error = "checkpoint block disagrees with ownership registry";
            return false;
        }
    }
    for (std::size_t i = 0; i < live.blocks.size(); ++i)
        liveBlocks.insert(std::make_pair(live.blocks[i].address,
                                         live.blocks[i].identity));
    if (targetBlocks.size() != saved.blocks.size() ||
        targetBlocks.size() != target.registry.liveHeapOrdinals.size() ||
        targetBlocks.size() != liveBlocks.size() ||
        targetBlocks.size() != saved.allocatedBlocks ||
        targetBlocks.size() != live.allocatedBlocks)
    {
        error = "heap allocation inventory changed or is incomplete";
        return false;
    }
    for (BlockMap::const_iterator it = targetBlocks.begin();
         it != targetBlocks.end(); ++it)
    {
        const BlockMap::const_iterator found = liveBlocks.find(it->first);
        if (found == liveBlocks.end() || !(found->second == it->second))
        {
            error = "checkpoint block allocation is no longer live";
            return false;
        }
    }
    std::set<std::uintptr_t> targetSeen;
    std::set<std::uintptr_t> liveSeen;
    std::size_t targetActiveBlockCount = 0;
    std::size_t liveActiveBlockCount = 0;
    for (std::size_t arrayIndex = 0; arrayIndex < kArrayCount; ++arrayIndex)
    {
        const MemBlockImage::Array& a = saved.arrays[arrayIndex];
        const MemBlockImage::Array& b = live.arrays[arrayIndex];
        if (a.name != kArrayNames[arrayIndex] || b.name != a.name ||
            a.entries.size() != a.size || b.entries.size() != b.size ||
            (arrayIndex == 7 && a.size != 0))
        {
            error = "checkpoint array layout or scratch-block guard failed";
            return false;
        }
        std::set<std::uintptr_t> targetMembership;
        std::set<std::uintptr_t> liveMembership;
        for (std::size_t i = 0; i < a.size; ++i)
        {
            // Block order in the image follows array order exactly.
            std::size_t offset = 0;
            for (std::size_t previous = 0; previous < arrayIndex; ++previous)
                offset += saved.arrays[previous].size;
            const MemBlockImage::Block& block = saved.blocks[offset + i];
            if (!(block.identity == a.entries[i]) ||
                !targetSeen.insert(block.address).second)
            {
                error = std::string("duplicate or mismatched block in ") + a.name;
                return false;
            }
            targetMembership.insert(block.address);
        }
        for (std::size_t i = 0; i < b.size; ++i)
        {
            std::size_t offset = 0;
            for (std::size_t previous = 0; previous < arrayIndex; ++previous)
                offset += live.arrays[previous].size;
            const MemBlockImage::Block& block = live.blocks[offset + i];
            if (!(block.identity == b.entries[i]) ||
                !liveSeen.insert(block.address).second)
            {
                error = std::string("invalid live block in ") + b.name;
                return false;
            }
            liveMembership.insert(block.address);
        }
        if (!allowOwnerTransfer && targetMembership != liveMembership)
        {
            error = std::string("block ownership changed in ") + a.name;
            return false;
        }
        if (arrayIndex < 8)
        {
            targetActiveBlockCount += a.size;
            liveActiveBlockCount += b.size;
        }
    }
    if (targetSeen.size() != targetBlocks.size() ||
        liveSeen.size() != liveBlocks.size() ||
        targetSeen != liveSeen ||
        targetActiveBlockCount != saved.usedBlocks ||
        liveActiveBlockCount != live.usedBlocks ||
        saved.maxUsedBlocks < saved.usedBlocks ||
        live.maxUsedBlocks < live.usedBlocks ||
        saved.constraintAllocations != 0 || live.constraintAllocations != 0)
    {
        error = "block partition or used-block counter is inconsistent";
        return false;
    }
    if (target.contactBindings != current.contactBindings)
    {
        error = "active contact-manager stream pointers or owners changed";
        return false;
    }
    return true;
}

bool containsRange(const MemBlockImage& image, std::uintptr_t address,
                   std::uint32_t size)
{
    if (!address || !size) return false;
    for (std::size_t i = 0; i < image.blocks.size(); ++i)
    {
        const std::uint64_t begin = image.blocks[i].address;
        const std::uint64_t end = begin + PxcNpMemBlock::SIZE;
        if (address >= begin && std::uint64_t(address) + size <= end)
            return true;
    }
    return false;
}

bool validateContactRanges(const MemBlockRestoreImage& image,
                           std::string& error)
{
    for (std::size_t i = 0; i < image.contactBindings.size(); ++i)
    {
        const MemBlockContactBinding& cm = image.contactBindings[i];
        if (cm.ccdContacts)
        {
            error = "CCD contact stream ownership is unsupported";
            return false;
        }
        if (cm.solverConstraintSize &&
            !containsRange(image.pool, cm.solverConstraint,
                           cm.solverConstraintSize))
        {
            error = "solver constraint pointer is outside tracked blocks";
            return false;
        }
        if (cm.compressedContactSize &&
            !containsRange(image.pool, cm.compressedContacts,
                           cm.compressedContactSize))
        {
            error = "contact stream pointer is outside tracked blocks";
            return false;
        }
        if (cm.frictionData && cm.frictionPatchCount &&
            !containsRange(image.pool, cm.frictionData, 1))
        {
            error = "friction stream pointer is outside tracked blocks";
            return false;
        }
        if (cm.npCacheSize &&
            !containsRange(image.pool, cm.npCache, cm.npCacheSize))
        {
            error = "narrow-phase cache pointer is outside tracked blocks";
            return false;
        }
    }
    return true;
}

// Preflight establishes that all arrays already have the same size, capacity,
// backing address, and block membership. Consequently this commit path only
// writes existing entries and existing allocation bytes; it never allocates.
void writeImage(PxcNpMemBlockPool& pool, const MemBlockImage& image)
{
    const MutableArrays arrays = arraysOf(pool);
    std::size_t offset = 0;
    for (std::size_t a = 0; a < kArrayCount; ++a)
    {
        arrays.array[a]->forceSize_Unsafe(image.arrays[a].size);
        for (std::size_t i = 0; i < image.arrays[a].size; ++i)
            (*arrays.array[a])[static_cast<PxU32>(i)] =
                reinterpret_cast<PxcNpMemBlock*>(
                    image.blocks[offset + i].address);
        offset += image.arrays[a].size;
    }
    for (std::size_t i = 0; i < image.blocks.size(); ++i)
    {
        const MemBlockImage::Block& block = image.blocks[i];
        std::memcpy(reinterpret_cast<void*>(block.address),
                    &block.bytes[0], PxcNpMemBlock::SIZE);
    }
    if (image.scratchArenaSize)
        std::memcpy(reinterpret_cast<void*>(image.scratchArenaAddress),
                    &image.scratchArenaBytes[0], image.scratchArenaSize);
    pool.mNpCacheActiveStream = image.npCacheActiveStream;
    pool.mFrictionActiveStream = image.frictionActiveStream;
    pool.mCCDCacheActiveStream = image.ccdCacheActiveStream;
    pool.mContactIndex = image.contactIndex;
    pool.mAllocatedBlocks = image.allocatedBlocks;
    pool.mMaxBlocks = image.maxBlocks;
    pool.mInitialBlocks = image.initialBlocks;
    pool.mUsedBlocks = image.usedBlocks;
    pool.mMaxUsedBlocks = image.maxUsedBlocks;
    pool.mPeakConstraintAllocations = image.peakConstraintAllocations;
    pool.mConstraintAllocations = image.constraintAllocations;
}

bool verifyImage(PxScene& scene,
                 const MemBlockIdentityRegistry& registry,
                 const MemBlockRestoreImage& expected,
                 std::string& error)
{
    MemBlockIdentityRegistry ids = registry;
    MemBlockRestoreImage actual;
    if (!CaptureMemBlockRestore(scene, ids, actual, error)) return false;
    std::string difference;
    if (!actual.pool.equals(expected.pool, difference) ||
        !sameStorage(actual.pool, expected.pool, error) ||
        actual.contactBindings != expected.contactBindings ||
        !registriesEqual(actual.registry, expected.registry))
    {
        if (error.empty()) error = difference.empty()
            ? "memory-block bindings or registry differ after restore"
            : "memory-block image differs after restore: " + difference;
        return false;
    }
    return true;
}

} // namespace

bool MemBlockContactBinding::operator==(
    const MemBlockContactBinding& other) const
{
    return managerAddress == other.managerAddress &&
           managerIndex == other.managerIndex &&
           workFlags == other.workFlags &&
           statusFlags == other.statusFlags &&
           contactCount == other.contactCount &&
           pairCachePairData == other.pairCachePairData &&
           solverConstraint == other.solverConstraint &&
           solverConstraintSize == other.solverConstraintSize &&
           compressedContacts == other.compressedContacts &&
           compressedContactSize == other.compressedContactSize &&
           frictionData == other.frictionData &&
           frictionPatchCount == other.frictionPatchCount &&
           npCache == other.npCache &&
           npCacheSize == other.npCacheSize &&
           ccdContacts == other.ccdContacts &&
           manifold == other.manifold;
}

bool CaptureMemBlockRestore(PxScene& scene,
                            MemBlockIdentityRegistry& registry,
                            MemBlockRestoreImage& image,
                            std::string& error)
{
    error.clear();
    MemBlockIdentityRegistry nextRegistry = registry;
    MemBlockRestoreImage next;
    if (!CaptureMemBlockPool(scene, nextRegistry, next.pool, error) ||
        !captureBindings(scene, next.contactBindings, error))
        return false;
    next.registry = nextRegistry;
    registry = std::move(nextRegistry);
    image = std::move(next);
    return true;
}

bool restorePoolImpl(PxScene& scene,
                     const MemBlockIdentityRegistry& currentRegistry,
                     const MemBlockRestoreImage& target,
                     bool allowOwnerTransfer,
                     std::string& error)
{
    error.clear();
    MemBlockIdentityRegistry ids = currentRegistry;
    MemBlockRestoreImage before;
    if (!CaptureMemBlockRestore(scene, ids, before, error)) return false;
    if (!validateImage(target, before, error, allowOwnerTransfer) ||
        !validateContactRanges(target, error) ||
        !validateContactRanges(before, error))
        return false;
    PxcNpMemBlockPool* pool = poolOf(scene);
    if (!pool || reinterpret_cast<std::uintptr_t>(pool) !=
                     target.pool.poolAddress)
    {
        error = "pool identity changed after preflight";
        return false;
    }

    writeImage(*pool, target.pool);
    std::string verificationError;
    bool verified = false;
    try
    {
        verified = verifyImage(scene, currentRegistry,
                               target, verificationError);
    }
    catch (...)
    {
        verificationError = "exception during post-write verification";
    }
    if (verified) return true;

    writeImage(*pool, before.pool);
    bool rolledBack = false;
    try
    {
        std::string rollbackError;
        rolledBack = verifyImage(scene, currentRegistry,
                                 before, rollbackError);
    }
    catch (...) {}
    if (!rolledBack) std::abort();
    error = "memory-block verification failed; previous state restored: " +
            verificationError;
    return false;
}

bool RestoreMemBlockPool(PxScene& scene,
                         const MemBlockIdentityRegistry& currentRegistry,
                         const MemBlockRestoreImage& target,
                         std::string& error)
{
    return restorePoolImpl(scene, currentRegistry, target, false, error);
}

bool RestoreMemBlockPoolForJoin(PxScene& scene,
                                const MemBlockIdentityRegistry& currentRegistry,
                                const MemBlockRestoreImage& target,
                                std::string& error)
{
    return restorePoolImpl(scene, currentRegistry, target, true, error);
}

} // namespace physx333_offline
