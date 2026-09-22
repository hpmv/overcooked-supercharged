#include "MemBlockImage.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <set>
#include <sstream>
#include <utility>

#include "PxPhysicsAPI.h"

// Test-only access to the pinned source layout. No vendor source or DLL is
// patched, and this translation unit never writes to an SDK object.
#define private public
#define protected public
#include "NpScene.h"
#include "ScScene.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"
#include "PxcNpMemBlockPool.h"
#include "PxcScratchAllocator.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "memory-block observer requires Win32 PhysX");
static_assert(PxcNpMemBlock::SIZE == 16384, "unexpected PhysX block size");

const std::size_t kMaxTrackedBlocks = 4096;
const std::size_t kMaxScratchArenaBytes = 64u * 1024u * 1024u;
const std::size_t kMaxArrayCapacity = 65536;

struct NamedArray {
    const char* name;
    const PxcNpMemBlockArray* source;
};

std::string atIndex(const char* name, std::size_t index)
{
    std::ostringstream stream;
    stream << name << '[' << index << ']';
    return stream.str();
}

bool captureScratch(const PxcNpMemBlockPool& pool,
                    MemBlockImage& image, std::string& error)
{
    const PxcScratchAllocator& scratch = pool.mScratchAllocator;
    image.scratchArenaAddress = reinterpret_cast<std::uintptr_t>(scratch.mStart);
    image.scratchArenaSize = scratch.mSize;
    image.scratchStackCapacity = scratch.mStack.capacity();
    if (scratch.mSize > kMaxScratchArenaBytes ||
        scratch.mStack.size() > kMaxArrayCapacity ||
        (scratch.mSize && !scratch.mStart) || !scratch.mStack.size())
    {
        error = "invalid or excessive scratch allocator storage";
        return false;
    }
    if (!scratch.mStart && scratch.mSize)
    {
        error = "scratch allocator has size without a base";
        return false;
    }
    const std::uintptr_t base = image.scratchArenaAddress;
    const std::uint64_t end = std::uint64_t(base) + scratch.mSize;
    if (end > std::uint64_t(std::numeric_limits<std::uintptr_t>::max()))
    {
        error = "scratch allocator range overflows the address space";
        return false;
    }
    for (PxU32 i = 0; i < scratch.mStack.size(); ++i)
    {
        const std::uintptr_t pointer =
            reinterpret_cast<std::uintptr_t>(scratch.mStack[i]);
        if (!base && !scratch.mSize && !pointer)
            image.scratchStackOffsets.push_back(UINT32_MAX);
        else if (pointer >= base && std::uint64_t(pointer) <= end)
            image.scratchStackOffsets.push_back(
                static_cast<std::uint32_t>(pointer - base));
        else
        {
            error = atIndex("scratch stack", i) + " points outside its arena";
            return false;
        }
    }
    image.scratchArenaBytes.resize(scratch.mSize);
    if (scratch.mSize)
        std::memcpy(&image.scratchArenaBytes[0], scratch.mStart,
                    scratch.mSize);
    if (scratch.mStack.size() != 1)
        image.unsupported.push_back(
            "scratch allocator still has outstanding suballocations");
    return true;
}

bool captureArray(const NamedArray& named,
                  const MemBlockIdentityRegistry& oldRegistry,
                  MemBlockIdentityRegistry& nextRegistry,
                  std::map<std::uintptr_t, std::uint32_t>& nextHeapLive,
                  std::set<std::uintptr_t>& seen,
                  std::set<std::uint32_t>& scratchSeen,
                  MemBlockImage& image, std::string& error)
{
    const PxcNpMemBlockArray& source = *named.source;
    if (source.size() > source.capacity() ||
        source.capacity() > kMaxArrayCapacity ||
        (source.capacity() && !source.begin()))
    {
        error = std::string(named.name) + " has invalid size/capacity";
        return false;
    }
    MemBlockImage::Array target;
    target.name = named.name;
    target.address = reinterpret_cast<std::uintptr_t>(source.begin());
    target.size = source.size();
    target.capacity = source.capacity();
    target.entries.reserve(source.size());

    const std::uintptr_t scratchBase = image.scratchBlockAddress;
    const std::uint64_t scratchEnd =
        std::uint64_t(scratchBase) +
        std::uint64_t(image.scratchBlockCount) * PxcNpMemBlock::SIZE;
    for (PxU32 i = 0; i < source.size(); ++i)
    {
        const PxcNpMemBlock* const block = source[i];
        const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(block);
        if (!address || !seen.insert(address).second)
        {
            error = atIndex(named.name, i) + " is null or duplicated";
            return false;
        }
        MemBlockImage::BlockRef id;
        if (scratchBase && address >= scratchBase &&
            std::uint64_t(address) < scratchEnd)
        {
            const std::uintptr_t offset = address - scratchBase;
            if (offset % PxcNpMemBlock::SIZE)
            {
                error = atIndex(named.name, i) +
                        " is not aligned to a scratch block";
                return false;
            }
            id.kind = MemBlockImage::BlockRef::Scratch;
            id.ordinal = static_cast<std::uint32_t>(
                offset / PxcNpMemBlock::SIZE);
            scratchSeen.insert(id.ordinal);
        }
        else
        {
            // The pool may retain ordinary heap blocks in any tracking
            // array. An old ordinal survives movement between those arrays.
            id.kind = MemBlockImage::BlockRef::Heap;
            const std::map<std::uintptr_t, std::uint32_t>::const_iterator old =
                oldRegistry.liveHeapOrdinals.find(address);
            if (old != oldRegistry.liveHeapOrdinals.end())
                id.ordinal = old->second;
            else
            {
                if (nextRegistry.nextHeapOrdinal == UINT32_MAX)
                {
                    error = "heap block ordinal space exhausted";
                    return false;
                }
                id.ordinal = nextRegistry.nextHeapOrdinal++;
            }
            nextHeapLive.insert(std::make_pair(address, id.ordinal));
        }
        target.entries.push_back(id);
        MemBlockImage::Block saved;
        saved.identity = id;
        saved.address = address;
        saved.bytes.resize(PxcNpMemBlock::SIZE);
        std::memcpy(&saved.bytes[0], block->data, PxcNpMemBlock::SIZE);
        image.blocks.push_back(std::move(saved));
        if (image.blocks.size() > kMaxTrackedBlocks)
        {
            error = "memory block image exceeds configured capture limit";
            return false;
        }
    }
    image.arrays.push_back(std::move(target));
    return true;
}

bool captureExceptional(const PxcNpMemBlockPool& pool,
                        const MemBlockIdentityRegistry& oldRegistry,
                        MemBlockIdentityRegistry& nextRegistry,
                        MemBlockImage& image, std::string& error)
{
    const Ps::Array<PxU8*>& source = pool.mExceptionalConstraints;
    if (source.size() > source.capacity() ||
        source.capacity() > kMaxArrayCapacity ||
        (source.capacity() && !source.begin()))
    {
        error = "exceptional allocation array has invalid size/capacity";
        return false;
    }
    image.exceptionalArrayAddress =
        reinterpret_cast<std::uintptr_t>(source.begin());
    image.exceptionalArrayCapacity = source.capacity();
    std::map<std::uintptr_t, std::uint32_t> newLive;
    for (PxU32 i = 0; i < source.size(); ++i)
    {
        const std::uintptr_t address =
            reinterpret_cast<std::uintptr_t>(source[i]);
        if (!address || newLive.find(address) != newLive.end())
        {
            error = atIndex("exceptional allocation", i) +
                    " is null or duplicated";
            return false;
        }
        const std::map<std::uintptr_t, std::uint32_t>::const_iterator old =
            oldRegistry.liveExceptionalOrdinals.find(address);
        std::uint32_t ordinal;
        if (old != oldRegistry.liveExceptionalOrdinals.end())
            ordinal = old->second;
        else
        {
            if (nextRegistry.nextExceptionalOrdinal == UINT32_MAX)
            {
                error = "exceptional allocation ordinal space exhausted";
                return false;
            }
            ordinal = nextRegistry.nextExceptionalOrdinal++;
        }
        newLive.insert(std::make_pair(address, ordinal));
        image.exceptionalOrdinals.push_back(ordinal);
    }
    nextRegistry.liveExceptionalOrdinals.swap(newLive);
    if (source.size())
        image.unsupported.push_back(
            "exceptional constraint allocation sizes and payloads unavailable");
    return true;
}

bool compareBytes(const std::vector<unsigned char>& a,
                  const std::vector<unsigned char>& b,
                  const std::string& name, std::string& difference)
{
    if (a.size() != b.size())
    {
        difference = name + ".size";
        return false;
    }
    for (std::size_t i = 0; i < a.size(); ++i)
    {
        if (a[i] != b[i])
        {
            difference = atIndex(name.c_str(), i);
            return false;
        }
    }
    return true;
}

} // namespace

bool CaptureMemBlockPool(PxScene& scene,
                         MemBlockIdentityRegistry& registry,
                         MemBlockImage& image,
                         std::string& error)
{
    error.clear();
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    PxsContext* context = np.getScene().getScScene()
                              .getInteractionScene().getLowLevelContext();
    if (!context)
    {
        error = "scene has no low-level context";
        return false;
    }
    const std::uintptr_t sceneAddress =
        reinterpret_cast<std::uintptr_t>(&scene);
    if (registry.sceneAddress && registry.sceneAddress != sceneAddress)
    {
        error = "memory block identity registry belongs to another scene";
        return false;
    }
    const PxcNpMemBlockPool& pool = context->getNpMemBlockPool();
    MemBlockImage next;
    MemBlockIdentityRegistry nextRegistry = registry;
    nextRegistry.sceneAddress = sceneAddress;
    next.sceneAddress = sceneAddress;
    next.poolAddress = reinterpret_cast<std::uintptr_t>(&pool);
    next.scratchAllocatorAddress =
        reinterpret_cast<std::uintptr_t>(&pool.mScratchAllocator);
    next.npCacheActiveStream = pool.mNpCacheActiveStream;
    next.frictionActiveStream = pool.mFrictionActiveStream;
    next.ccdCacheActiveStream = pool.mCCDCacheActiveStream;
    next.contactIndex = pool.mContactIndex;
    next.allocatedBlocks = pool.mAllocatedBlocks;
    next.maxBlocks = pool.mMaxBlocks;
    next.initialBlocks = pool.mInitialBlocks;
    next.usedBlocks = pool.mUsedBlocks;
    next.maxUsedBlocks = pool.mMaxUsedBlocks;
    next.peakConstraintAllocations = pool.mPeakConstraintAllocations;
    next.constraintAllocations = pool.mConstraintAllocations;
    next.scratchBlockAddress =
        reinterpret_cast<std::uintptr_t>(pool.mScratchBlockAddr);
    next.scratchBlockCount = pool.mNbScratchBlocks;
    if (next.npCacheActiveStream > 1 || next.frictionActiveStream > 1 ||
        next.ccdCacheActiveStream > 1 || next.contactIndex > 1 ||
        next.usedBlocks > next.allocatedBlocks ||
        next.maxUsedBlocks < next.usedBlocks ||
        (next.scratchBlockCount && !next.scratchBlockAddress) ||
        (!next.scratchBlockCount && next.scratchBlockAddress) ||
        next.scratchBlockCount > kMaxTrackedBlocks)
    {
        error = "memory block pool selector or counter invariant failed";
        return false;
    }
    if (!captureScratch(pool, next, error)) return false;
    if (next.scratchBlockCount)
    {
        const std::uint64_t arenaBegin = next.scratchArenaAddress;
        const std::uint64_t arenaEnd = arenaBegin + next.scratchArenaSize;
        const std::uint64_t scratchBegin = next.scratchBlockAddress;
        const std::uint64_t scratchEnd = scratchBegin +
            std::uint64_t(next.scratchBlockCount) * PxcNpMemBlock::SIZE;
        if (scratchBegin < arenaBegin || scratchEnd > arenaEnd)
        {
            error = "scratch blocks extend outside scratch allocator arena";
            return false;
        }
        next.unsupported.push_back(
            "scratch constraint memory remains active after fetchResults");
    }

    const NamedArray arrays[] = {
        { "constraints", &pool.mConstraints },
        { "contacts[0]", &pool.mContacts[0] },
        { "contacts[1]", &pool.mContacts[1] },
        { "friction[0]", &pool.mFriction[0] },
        { "friction[1]", &pool.mFriction[1] },
        { "npCache[0]", &pool.mNpCache[0] },
        { "npCache[1]", &pool.mNpCache[1] },
        { "scratchBlocks", &pool.mScratchBlocks },
        { "unused", &pool.mUnused }
    };
    std::map<std::uintptr_t, std::uint32_t> nextHeapLive;
    std::set<std::uintptr_t> seen;
    std::set<std::uint32_t> scratchSeen;
    for (std::size_t i = 0; i < sizeof(arrays) / sizeof(arrays[0]); ++i)
    {
        if (!captureArray(arrays[i], registry, nextRegistry,
                          nextHeapLive, seen, scratchSeen, next, error))
            return false;
    }
    nextRegistry.liveHeapOrdinals.swap(nextHeapLive);
    if (!captureExceptional(pool, registry, nextRegistry, next, error))
        return false;

    const std::size_t heapCount = nextRegistry.liveHeapOrdinals.size();
    std::size_t unusedHeapCount = 0;
    const MemBlockImage::Array& unused = next.arrays.back();
    for (std::size_t i = 0; i < unused.entries.size(); ++i)
    {
        if (unused.entries[i].kind == MemBlockImage::BlockRef::Heap)
            ++unusedHeapCount;
        else
            next.unsupported.push_back(
                "scratch block appeared in heap unused array");
    }
    const std::size_t activeHeapCount = heapCount - unusedHeapCount;
    if (heapCount > next.allocatedBlocks ||
        activeHeapCount > next.usedBlocks ||
        scratchSeen.size() > next.scratchBlockCount)
    {
        error = "tracked block count exceeds pool allocation counters";
        return false;
    }
    if (heapCount != next.allocatedBlocks ||
        activeHeapCount != next.usedBlocks ||
        scratchSeen.size() != next.scratchBlockCount ||
        next.constraintAllocations)
        next.unsupported.push_back(
            "blocks owned by external constraint arrays are not enumerated");

    registry = std::move(nextRegistry);
    image = std::move(next);
    return true;
}

bool MemBlockImage::equals(const MemBlockImage& other,
                           std::string& firstDifference) const
{
    firstDifference.clear();
#define CHECK_FIELD(field) do { if (field != other.field) { \
    firstDifference = #field; return false; } } while (0)
    CHECK_FIELD(npCacheActiveStream);
    CHECK_FIELD(frictionActiveStream);
    CHECK_FIELD(ccdCacheActiveStream);
    CHECK_FIELD(contactIndex);
    CHECK_FIELD(allocatedBlocks);
    CHECK_FIELD(maxBlocks);
    CHECK_FIELD(initialBlocks);
    CHECK_FIELD(usedBlocks);
    CHECK_FIELD(maxUsedBlocks);
    CHECK_FIELD(peakConstraintAllocations);
    CHECK_FIELD(constraintAllocations);
    CHECK_FIELD(scratchBlockCount);
    CHECK_FIELD(scratchArenaSize);
    CHECK_FIELD(scratchStackCapacity);
    CHECK_FIELD(scratchStackOffsets);
    CHECK_FIELD(exceptionalArrayCapacity);
    CHECK_FIELD(exceptionalOrdinals);
    CHECK_FIELD(unsupported);
#undef CHECK_FIELD
    if (!compareBytes(scratchArenaBytes, other.scratchArenaBytes,
                      "scratchArenaBytes", firstDifference)) return false;
    if (arrays.size() != other.arrays.size())
    {
        firstDifference = "arrays.size";
        return false;
    }
    for (std::size_t i = 0; i < arrays.size(); ++i)
    {
        const Array& a = arrays[i];
        const Array& b = other.arrays[i];
        if (a.name != b.name || a.size != b.size ||
            a.capacity != b.capacity || a.entries != b.entries)
        {
            firstDifference = atIndex("arrays", i) + "." + a.name;
            return false;
        }
    }
    if (blocks.size() != other.blocks.size())
    {
        firstDifference = "blocks.size";
        return false;
    }
    for (std::size_t i = 0; i < blocks.size(); ++i)
    {
        const Block& a = blocks[i];
        const Block& b = other.blocks[i];
        if (!(a.identity == b.identity))
        {
            firstDifference = atIndex("blocks", i) + ".identity";
            return false;
        }
        if (!compareBytes(a.bytes, b.bytes, atIndex("blocks", i) + ".bytes",
                          firstDifference)) return false;
    }
    return true;
}

} // namespace physx333_offline
