#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace physx333_offline {

// An allocation's position in a pool array is not its identity: the same
// block can move between active streams and the LIFO unused array. Keep one
// registry per scene across captures, starting at the same logical point when
// comparing independent scenes. A heap address absent from one capture is
// retired, so later reuse of that address receives a new ordinal. Reuse at
// exactly the same address entirely between two captures is invisible here;
// an allocator hook is needed before this identity scheme can cover arbitrary
// allocation histories.
struct MemBlockIdentityRegistry {
    std::uintptr_t sceneAddress = 0;
    std::map<std::uintptr_t, std::uint32_t> liveHeapOrdinals;
    std::map<std::uintptr_t, std::uint32_t> liveExceptionalOrdinals;
    std::uint32_t nextHeapOrdinal = 0;
    std::uint32_t nextExceptionalOrdinal = 0;
};

// Read-only image of a stopped PhysX 3.3.3 Win32 PxcNpMemBlockPool. Addresses
// are retained solely to help a future same-scene restore preflight; equals()
// compares ordinals, array order, counters and full contents, never addresses.
struct MemBlockImage {
    struct BlockRef {
        enum Kind : std::uint32_t { Heap = 0, Scratch = 1 };
        Kind kind = Heap;
        std::uint32_t ordinal = 0;

        bool operator==(const BlockRef& other) const {
            return kind == other.kind && ordinal == other.ordinal;
        }
    };

    struct Array {
        std::string name;
        std::uintptr_t address = 0;
        std::uint32_t size = 0;
        std::uint32_t capacity = 0;
        std::vector<BlockRef> entries;
    };

    struct Block {
        BlockRef identity;
        std::uintptr_t address = 0;
        // All bytes are retained, including stale bytes in a free block and
        // unused space after a stream's current write position.
        std::vector<unsigned char> bytes;
    };

    std::uintptr_t sceneAddress = 0;
    std::uintptr_t poolAddress = 0;
    std::uintptr_t scratchAllocatorAddress = 0;

    std::uint32_t npCacheActiveStream = 0;
    std::uint32_t frictionActiveStream = 0;
    std::uint32_t ccdCacheActiveStream = 0;
    std::uint32_t contactIndex = 0;
    std::uint32_t allocatedBlocks = 0;
    std::uint32_t maxBlocks = 0;
    std::uint32_t initialBlocks = 0;
    std::uint32_t usedBlocks = 0;
    std::uint32_t maxUsedBlocks = 0;
    std::uint32_t peakConstraintAllocations = 0;
    std::uint32_t constraintAllocations = 0;

    std::uintptr_t scratchBlockAddress = 0;
    std::uint32_t scratchBlockCount = 0;
    std::uintptr_t scratchArenaAddress = 0;
    std::uint32_t scratchArenaSize = 0;
    std::uint32_t scratchStackCapacity = 0;
    // Offset from scratchArenaAddress; UINT32_MAX represents the null stack
    // entry used when no scratch arena has been supplied.
    std::vector<std::uint32_t> scratchStackOffsets;
    std::vector<unsigned char> scratchArenaBytes;

    // Fixed order: constraints, contacts 0/1, friction 0/1, cache 0/1,
    // scratch, unused. The exceptional-allocation list follows separately.
    std::vector<Array> arrays;
    std::uintptr_t exceptionalArrayAddress = 0;
    std::uint32_t exceptionalArrayCapacity = 0;
    std::vector<std::uint32_t> exceptionalOrdinals;
    std::vector<Block> blocks;

    // Nonempty means that the image does not cover all state needed for a
    // parity claim. Both content and order are compared by equals().
    std::vector<std::string> unsupported;

    bool equals(const MemBlockImage& other, std::string& firstDifference) const;
};

// Call only after fetchResults and before the next scene write. This never
// changes PhysX state. Registry and image are left unchanged on failure.
bool CaptureMemBlockPool(physx::PxScene& scene,
                         MemBlockIdentityRegistry& registry,
                         MemBlockImage& image,
                         std::string& error);

} // namespace physx333_offline
