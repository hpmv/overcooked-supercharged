#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace physx333_offline {

// Shape identity is stable only while the same actors and their shape order
// remain in the scene. No PhysX allocation address is stored in this image.
struct NPhaseShapeKey {
    std::uint32_t actorId;
    std::uint32_t shapeIndex;

    bool operator==(const NPhaseShapeKey& other) const;
};

struct NPhasePairTopology {
    NPhaseShapeKey shape0;
    NPhaseShapeKey shape1;
    std::uint32_t pairFlags;
    std::uint32_t hasTouch;
    std::uint32_t hasKnownTouch;
    std::uint32_t hasManager;
    std::uint32_t actorPairRefCount;
    std::uint32_t actorPairTouchCount;

    bool operator==(const NPhasePairTopology& other) const;
};

struct NPhaseTopologyImage {
    std::vector<NPhasePairTopology> pairs;
    std::uint32_t activePairCount;

    NPhaseTopologyImage() : activePairCount(0) {}
    // Compare just the ordered shape identities and filter pair flags.
    // Lifecycle reconstruction is expected to pass this while full state
    // equality remains false until touch/contact/island history is restored.
    bool sameShapePairs(const NPhaseTopologyImage& other,
                        std::string& firstDifference) const;
    bool equals(const NPhaseTopologyImage& other,
                std::string& firstDifference) const;
};

// Quiescent, read-only capture. Actor userData must be a unique, nonzero
// 32-bit fixture ID. Shapes are keyed by their enumeration index per actor.
bool CaptureNPhaseTopology(physx::PxScene& scene,
                           NPhaseTopologyImage& image,
                           std::string& error);

// A narrow experiment: create the missing six rigid shape interactions in
// an empty successor using the original NPhaseCore::onOverlapCreated path.
// The bridge lives only in a disposable copy of the pinned PhysX DLL.
// This does not restore touch flags, contact streams/manifolds, pool history,
// island graph, or other predecessor state. Do not simulate after this call.
// All inputs are preflighted before the first interaction is created.
bool RestoreNPhaseTopology(physx::PxScene& scene,
                           const NPhaseTopologyImage& target,
                           std::string& error);

} // namespace physx333_offline
