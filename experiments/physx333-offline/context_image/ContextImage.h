#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// Test-only image of the settled low-level context outside the interaction,
// memory-block, island, body, SAP, transform-cache, and scene-clock images.
// Every address is a same-scene allocation/identity guard, not a portable ID.
struct ContextImage
{
    struct Field
    {
        std::string name;
        std::uintptr_t address = 0;
        std::vector<unsigned char> bytes;
        bool invariant = false;
    };

    struct Array
    {
        std::string name;
        std::uintptr_t object = 0;
        std::uintptr_t data = 0;
        std::uintptr_t sizeAddress = 0;
        std::uint32_t size = 0;
        std::uint32_t capacity = 0;
        std::uint32_t elementBytes = 0;
        std::uint32_t objectBytes = 0;
        bool fullCapacityBytes = false;
        std::vector<unsigned char> bytes;
        bool invariant = false;
    };

    struct Bitmap
    {
        std::string name;
        std::uintptr_t object = 0;
        std::uintptr_t data = 0;
        std::uint32_t wordCount = 0;
        std::uint32_t userMemory = 0;
        std::uint32_t objectBytes = 0;
        bool resetBeforeNextUse = false;
        std::vector<std::uint32_t> words;
    };

    struct ThresholdTable
    {
        std::uintptr_t object = 0;
        std::uintptr_t buffer = 0;
        std::uintptr_t hash = 0;
        std::uintptr_t pairs = 0;
        std::uintptr_t nexts = 0;
        std::uint32_t hashSize = 0;
        std::uint32_t hashCapacity = 0;
        std::uint32_t pairsSize = 0;
        std::uint32_t pairsCapacity = 0;
        std::vector<unsigned char> bytes;
    };

    struct ThreadObject
    {
        std::uintptr_t address = 0;
        std::uint32_t compressedCacheSize = 0;
        std::uint32_t constraintSize = 0;
        std::vector<unsigned char> bytes;
        std::vector<unsigned char> ignoredHeaderBytes;
    };

    std::uintptr_t scene = 0;
    std::uintptr_t context = 0;
    std::uintptr_t dynamics = 0;
    std::uintptr_t threadCacheHeader = 0;
    std::vector<std::uintptr_t> cachedThreadOrder;
    std::vector<ThreadObject> cachedThreads;
    std::vector<std::uintptr_t> actors;
    std::vector<Field> fields;
    std::vector<Array> arrays;
    std::vector<Bitmap> bitmaps;
    ThresholdTable thresholdTable;

    bool equals(const ContextImage& other,
                std::string& firstDifference) const;
};

// Requires a completed PxScene::fetchResults boundary. Rejects CCD,
// constraints, articulations, aggregates, contact modification, and nonempty
// solver scratch arrays. Cached thread contexts are covered only while their
// LIFO order and all owned allocations stay identical. Callers must not step
// after restoring this component alone.
bool CaptureContextImage(physx::PxScene& scene, ContextImage& image,
                         std::string& error);

// Preflights image schema, identity, storage, and table layout before writing.
// Restores only same-allocation POD fields and buffers. Other components must
// finish the joined restore before a PhysX update is legal.
bool RestoreContextImage(physx::PxScene& scene,
                         const ContextImage& image, std::string& error);

}} // namespace oc2::offline
