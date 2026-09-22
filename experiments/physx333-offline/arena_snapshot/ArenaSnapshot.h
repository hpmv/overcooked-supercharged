#pragma once

#include <cstddef>
#include <cstdint>
#include <mutex>
#include <string>
#include <unordered_map>
#include <vector>

#include "foundation/PxAllocatorCallback.h"

namespace oc2 { namespace offline {

// A diagnostic allocator for a source-built, single-process PhysX scene.
// Freed blocks stay mapped so a settled scene can be restored at identical
// addresses. This is not the allocator used by the shipped Unity binary.
class ArenaSnapshotAllocator final : public physx::PxAllocatorCallback
{
public:
    struct Block
    {
        std::size_t offset = 0;
        std::size_t size = 0;
        bool live = false;
        std::string type;
        std::string file;
        int line = 0;

        bool operator==(const Block& other) const
        {
            return offset == other.offset && size == other.size &&
                   live == other.live && type == other.type &&
                   file == other.file && line == other.line;
        }
    };

    struct Image
    {
        std::uintptr_t owner = 0;
        std::size_t cursor = 0;
        std::size_t peak = 0;
        std::vector<Block> blocks;
        std::vector<std::uint8_t> bytes;

        bool equals(const Image& other, std::string& difference) const;
    };

    explicit ArenaSnapshotAllocator(std::size_t reserveBytes);
    ~ArenaSnapshotAllocator() override;
    ArenaSnapshotAllocator(const ArenaSnapshotAllocator&) = delete;
    ArenaSnapshotAllocator& operator=(const ArenaSnapshotAllocator&) = delete;

    void* allocate(std::size_t size, const char* typeName,
                   const char* filename, int line) override;
    void deallocate(void* pointer) override;

    bool valid() const { return mBase != nullptr; }
    Image capture() const;
    bool restore(const Image& image, std::string& error);

private:
    std::uint8_t* mBase = nullptr;
    std::size_t mReserve = 0;
    std::size_t mCursor = 0;
    std::size_t mPeak = 0;
    std::vector<Block> mBlocks;
    std::unordered_map<std::uintptr_t, std::size_t> mByAddress;
    mutable std::mutex mMutex;
};

}} // namespace oc2::offline
