#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// A source-built, same-scene component image for a settled fetchResults
// boundary. This does not recreate actors or restore the low-level contact,
// broadphase, body, query, or island state. Do not simulate after applying it
// until the other component images have been restored and joined.
struct SceneClockImage
{
    struct Field
    {
        std::string name;
        std::uintptr_t address = 0;
        std::vector<unsigned char> bytes;
        bool invariant = false;
    };

    struct PointerArray
    {
        std::string name;
        std::uintptr_t object = 0;
        std::uintptr_t data = 0;
        std::uint32_t capacity = 0;
        std::vector<std::uintptr_t> values;
        bool invariant = false;
    };

    struct IDTracker
    {
        std::string name;
        std::uintptr_t object = 0;
        std::uint32_t currentId = 0;
        std::uintptr_t freeData = 0;
        std::uint32_t freeCapacity = 0;
        std::vector<std::uint32_t> freeIds;
        std::uintptr_t pendingData = 0;
        std::uint32_t pendingCapacity = 0;
        std::uintptr_t bitmapData = 0;
        std::uint32_t bitmapWords = 0;
    };

    std::uintptr_t npScene = 0;
    std::uintptr_t scScene = 0;
    std::vector<Field> fields;
    std::vector<PointerArray> arrays;
    IDTracker shapeIds;
    IDTracker rigidIds;

    bool equals(const SceneClockImage& other,
                std::string& firstDifference) const;
};

// Capture refuses an active collide/solve/simulate, pending removals, event
// buffers, unsupported features, or pending ObjectIDTracker releases. Restore
// preflights every field, array allocation, actor pointer order, and ID pool before
// the first write. A failed post-write comparison rolls back to the live image.
bool CaptureSceneClock(physx::PxScene& scene, SceneClockImage& image,
                       std::string& error);
bool RestoreSceneClock(physx::PxScene& scene,
                       const SceneClockImage& image, std::string& error);

}} // namespace oc2::offline
