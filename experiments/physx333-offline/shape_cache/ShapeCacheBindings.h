#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// The transform-cache array and ID pool do not own the ShapeSim ID fields.
// This joined image covers every attached rigid-static/dynamic ShapeSim in the
// stopped source-built scene, including shapes with no current interaction.
// It is tied to their exact ShapeSim allocations.
struct ShapeCacheBindings
{
    struct Binding
    {
        std::uintptr_t shapeSim = 0;
        std::uint32_t shapeId = 0;
        std::uint32_t transformCacheId = 0;
    };

    std::uintptr_t scene = 0;
    std::vector<Binding> bindings;

    bool equals(const ShapeCacheBindings& other,
                std::string& firstDifference) const;
};

// Capture does not mutate PhysX. Restore requires the same ordered set of
// attached rigid ShapeSim objects and must run after the cache array/ID
// pool has been restored, before the next simulation step. It does not create
// interactions, IDs, or cache entries.
bool CaptureShapeCacheBindings(physx::PxScene& scene,
                               ShapeCacheBindings& image,
                               std::string& error);
bool RestoreShapeCacheBindings(physx::PxScene& scene,
                               const ShapeCacheBindings& image,
                               std::string& error);

}} // namespace oc2::offline
