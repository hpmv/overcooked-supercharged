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
    // Physical same-scene allocation bindings. These are used only for the
    // source-built subset resurrection preflight, never as portable IDs.
    std::uint32_t sipPoolSlot;
    std::uint32_t managerSlot;
    std::uint32_t islandEdge;

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

// Source-built test-only subset lifecycle stage. The current 6- or 12-box
// fixture must retain at least one pair; the function creates only target
// pairs absent from that successor. It preflights the next SIP/CM pool slots,
// preserves survivor objects, and restores ordered scene/mover pair arrays.
// Touch, report, contact payload, island, SAP, and other checkpoint state are
// NOT restored. On any postwrite failure the disposable scene is fail-stop:
// do not simulate or destroy/reuse it as a valid restored scene.
bool RestoreNPhaseSubset(physx::PxScene& scene,
                          const NPhaseTopologyImage& target,
                          std::string& error);

} // namespace physx333_offline
