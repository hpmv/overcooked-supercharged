#include "ArenaSnapshot.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <utility>

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>

namespace oc2 { namespace offline {
namespace {

constexpr std::size_t kAlignment = 16;

bool alignedCursor(std::size_t cursor, std::size_t& aligned)
{
    if (cursor > std::numeric_limits<std::size_t>::max() -
                     (kAlignment - 1)) return false;
    aligned = (cursor + kAlignment - 1) & ~(kAlignment - 1);
    return true;
}

} // namespace

ArenaSnapshotAllocator::ArenaSnapshotAllocator(std::size_t reserveBytes)
    : mReserve(reserveBytes)
{
    if (reserveBytes)
        mBase = static_cast<std::uint8_t*>(VirtualAlloc(
            nullptr, reserveBytes, MEM_RESERVE | MEM_COMMIT, PAGE_READWRITE));
}

ArenaSnapshotAllocator::~ArenaSnapshotAllocator()
{
    if (mBase) VirtualFree(mBase, 0, MEM_RELEASE);
}

void* ArenaSnapshotAllocator::allocate(std::size_t size, const char* typeName,
                                      const char* filename, int line)
{
    if (!size) return nullptr;
    std::lock_guard<std::mutex> lock(mMutex);
    if (!mBase) return nullptr;
    std::size_t start = 0;
    if (!alignedCursor(mCursor, start) || start > mReserve ||
        size > mReserve - start)
        return nullptr;
    const std::size_t end = start + size;
    void* result = mBase + start;
    mByAddress.emplace(reinterpret_cast<std::uintptr_t>(result),
                       mBlocks.size());
    Block block;
    block.offset = start;
    block.size = size;
    block.live = true;
    block.type = typeName ? typeName : "";
    block.file = filename ? filename : "";
    block.line = line;
    mBlocks.push_back(std::move(block));
    mCursor = end;
    mPeak = std::max(mPeak, end);
    return result;
}

void ArenaSnapshotAllocator::deallocate(void* pointer)
{
    if (!pointer) return;
    std::lock_guard<std::mutex> lock(mMutex);
    const auto found = mByAddress.find(reinterpret_cast<std::uintptr_t>(pointer));
    if (found == mByAddress.end() || !mBlocks[found->second].live)
        std::abort();
    mBlocks[found->second].live = false;
    mByAddress.erase(found);
}

ArenaSnapshotAllocator::Image ArenaSnapshotAllocator::capture() const
{
    std::lock_guard<std::mutex> lock(mMutex);
    Image image;
    image.owner = reinterpret_cast<std::uintptr_t>(this);
    image.cursor = mCursor;
    image.peak = mPeak;
    image.blocks = mBlocks;
    if (mBase && mPeak)
        image.bytes.assign(mBase, mBase + mPeak);
    return image;
}

bool ArenaSnapshotAllocator::restore(const Image& image, std::string& error)
{
    error.clear();
    if (!mBase || image.owner != reinterpret_cast<std::uintptr_t>(this) ||
        image.cursor > image.peak || image.peak > mReserve ||
        image.bytes.size() != image.peak)
    {
        error = "arena image owner or extent differs";
        return false;
    }
    std::unordered_map<std::uintptr_t, std::size_t> live;
    live.reserve(image.blocks.size());
    std::size_t lastEnd = 0;
    for (std::size_t i = 0; i < image.blocks.size(); ++i)
    {
        const Block& block = image.blocks[i];
        if (block.offset < lastEnd || block.offset % kAlignment ||
            block.size == 0 || block.offset > image.cursor ||
            block.size > image.cursor - block.offset)
        {
            error = "arena image block inventory is invalid";
            return false;
        }
        lastEnd = block.offset + block.size;
        if (block.live)
            live.emplace(reinterpret_cast<std::uintptr_t>(mBase + block.offset), i);
    }
    std::vector<Block> blocks = image.blocks;
    std::lock_guard<std::mutex> lock(mMutex);
    if (image.peak < mPeak)
        std::memset(mBase + image.peak, 0, mPeak - image.peak);
    if (!image.bytes.empty())
        std::memcpy(mBase, image.bytes.data(), image.bytes.size());
    mCursor = image.cursor;
    mPeak = image.peak;
    mBlocks.swap(blocks);
    mByAddress.swap(live);
    return true;
}

bool ArenaSnapshotAllocator::Image::equals(const Image& other,
                                           std::string& difference) const
{
    difference.clear();
    if (owner != other.owner || cursor != other.cursor || peak != other.peak ||
        blocks != other.blocks || bytes.size() != other.bytes.size())
    {
        difference = "owner, extent, or allocation ledger";
        return false;
    }
    std::size_t mismatches = 0;
    for (std::size_t i = 0; i < bytes.size(); ++i)
        if (bytes[i] != other.bytes[i])
        {
            if (!mismatches)
            {
                difference = "arena byte at offset " + std::to_string(i);
                for (const Block& block : blocks)
                    if (i >= block.offset && i - block.offset < block.size)
                    {
                        difference += " block+" +
                                      std::to_string(i - block.offset) + "/" +
                                      std::to_string(block.size) + " expected=" +
                                      std::to_string(bytes[i]) + " replay=" +
                                      std::to_string(other.bytes[i]) + " in " +
                                      block.type + " (" + block.file +
                                      ":" + std::to_string(block.line) + ")";
                        break;
                }
            }
            if (mismatches < 100)
            {
                difference += " [" + std::to_string(i);
                for (const Block& block : blocks)
                    if (i >= block.offset && i - block.offset < block.size)
                    {
                        difference += "+" +
                                      std::to_string(i - block.offset);
                        break;
                    }
                difference += "]";
            }
            ++mismatches;
        }
    if (mismatches)
    {
        difference += "; total differing bytes=" + std::to_string(mismatches);
        return false;
    }
    return true;
}

}} // namespace oc2::offline
