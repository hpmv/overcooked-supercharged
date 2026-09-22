#pragma once

#include <cstdint>

namespace physx333_offline {

enum InteractionReportBridgeResultV1 {
    InteractionReportBridgeSuccess = 0,
    InteractionReportBridgeInvalidInput = 1,
    InteractionReportBridgeUnsupportedScene = 2,
    InteractionReportBridgeUnknownPair = 3,
    InteractionReportBridgeAlreadyAllocated = 4,
    InteractionReportBridgeAllocationFailure = 5
};

// The addresses are six ActorPair objects already constructed inside the
// supplied NPhaseCore. They are never persisted in an image.
typedef std::uint32_t (__cdecl* InteractionReportCreateFnV1)(
    void* nphaseCore, void* const* orderedActorPairs, std::uint32_t count);

// Export: oc2_physx333_report_create_subset_v2. All existing report objects
// are preserved; only the supplied absent ActorPairs use the SDK lazy path.
// The bridge requires every live overlap to have report data afterward.
typedef std::uint32_t (__cdecl* InteractionReportCreateSubsetFnV2)(
    void* nphaseCore, void* const* orderedMissingActorPairs,
    std::uint32_t missingCount, std::uint32_t expectedOverlapCount,
    std::uint32_t expectedAlreadyReported);

} // namespace physx333_offline
