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

} // namespace physx333_offline
