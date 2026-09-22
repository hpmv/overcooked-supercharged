#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// Test-only image of one stopped PhysX 3.3.3 scene's low-level transform
// cache. The addresses are process-local and are intentionally part of the
// image: restore requires the same scene and unchanged allocation topology.
struct TransformCacheImage
{
    struct Array
    {
        std::uintptr_t address = 0;
        std::uint32_t size = 0;
        std::uint32_t capacity = 0;
        // Includes the allocated tail beyond size. The ID pool pops its free
        // IDs from the end of the used prefix; their order is significant.
        std::vector<unsigned char> bytes;
    };

    std::uintptr_t scene = 0;
    std::uintptr_t cache = 0;
    std::uintptr_t idPool = 0;
    std::uint32_t currentId = 0;
    Array transforms;
    Array referenceCounts;
    Array freeIds;

    bool equals(const TransformCacheImage& other,
                std::string& firstDifference) const;
};

// Call only after fetchResults and before the next simulation call. This
// restores the cache and its ID pool only; simulation requires companion
// restoration of the owners, contacts, broadphase and island state.
bool CaptureTransformCache(physx::PxScene& scene, TransformCacheImage& image,
                           std::string& error);
bool RestoreTransformCache(physx::PxScene& scene,
                           const TransformCacheImage& image,
                           std::string& error);

}} // namespace oc2::offline
