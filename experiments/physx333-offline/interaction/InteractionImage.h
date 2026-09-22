#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include "../nphase/NPhaseTopology.h"

namespace physx { class PxScene; }

namespace physx333_offline {

// Six box contacts only. The pool slots are physical slots in the stopped,
// same-scene PhysX 3.3.3 pools; they are not portable serialization IDs.
struct InteractionPairImage {
    std::uint32_t moverShape = 0;
    std::uint32_t sipPoolSlot = 0;
    std::uint32_t actorPairPoolSlot = 0;
    std::uint32_t managerSlot = 0;
    std::uint32_t islandEdge = 0;
    std::uint32_t sipFlags = 0;
    std::uint32_t contactReportStamp = 0;
    std::uint32_t reportPairIndex = 0;
    std::uint32_t reportStreamIndex = 0;
    std::uint32_t actorPairFlags = 0;
    std::uint32_t actorPairTouchCount = 0;
    std::uint32_t actorPairRefCount = 0;
    std::uint32_t reportDataPresent = 0;
    std::uint32_t reportPoolSlot = 0xffffffffu;
    std::uint32_t reportResetStamp = 0;
    std::uint32_t reportActorAId = 0;
    std::uint32_t reportActorBId = 0;
    // Exact ContactStreamManager object bytes. It contains only counters,
    // flags, and an index into the scene-owned report buffer.
    std::vector<unsigned char> reportStreamManager;
    std::uint32_t managerFlags = 0;
    std::uint32_t managerStatusFlags = 0;
    // Exact Win32 work-unit bytes for same-scene restoration. Internal
    // pointer fields are validated against live backing before being applied.
    std::vector<unsigned char> workUnitBytes;
    std::vector<unsigned char> compressedContactBytes;
    std::vector<unsigned char> pairCacheBytes;
    std::vector<unsigned char> manifoldTransformBytes;
    std::vector<unsigned char> manifoldContactBytes;
    std::uint32_t manifoldKind = 0; // 0 absent, 1 box/box single manifold
    std::uint32_t manifoldContactCount = 0;
    std::uint32_t manifoldWarmStartCount = 0;
    std::vector<unsigned char> manifoldIndexBytes;
};

struct InteractionImage {
    std::uintptr_t scene = 0;
    NPhaseTopologyImage topology;
    // A row is keyed by moverShape; these vectors preserve each container's
    // order separately from the physical allocation order.
    std::vector<InteractionPairImage> pairs;
    std::vector<std::uint32_t> sceneOrder;
    std::vector<std::uint32_t> moverActorOrder;
    std::vector<std::uint32_t> managerFreeOrder;
    std::vector<std::uint32_t> reportPoolFreeOrder;
    std::vector<std::uint32_t> persistentEventOrder;
    std::vector<std::uint32_t> forceThresholdEventOrder;
    std::vector<std::uint32_t> reportActorPairOrder;
    std::uint32_t managerSlabCount = 0;
    std::uint32_t managerFreeCount = 0;
    std::uint32_t reportPoolSlabCount = 0;
    std::uint32_t reportPoolUsedCount = 0;
    std::uint32_t sceneActiveCount = 0;
    std::uint32_t moverTransferringCount = 0;
    std::uint32_t nextPersistentPair = 0;
    std::uint32_t reportBufferIndex = 0;
    std::uint32_t reportBufferSize = 0;
    std::uint32_t reportBufferDefaultSize = 0;
    std::uint32_t reportBufferLastIndex = 0;
    std::uint32_t reportBufferAllocationLocked = 0;
    std::vector<unsigned char> reportBufferBytes;

    bool sameSlotsAndOrder(const InteractionImage& other,
                           std::string& firstDifference) const;
};

// Both calls require a completed fetchResults boundary. The image is tied to
// this exact scene and shape/actor topology. Capture does not mutate PhysX.
bool CaptureInteractionImage(physx::PxScene& scene, InteractionImage& image,
                             std::string& error);

// Narrow lifecycle and ordering stage. Requires an empty deletion successor.
// Creates the six NPhase pairs through the pinned source bridge in reverse
// order, then restores InteractionScene and mover Actor interaction order.
// Touch state, contact streams, manifold contents, reports, and island graph
// are NOT restored here; do not simulate until their companion restorers run.
// On a failure after lifecycle creation the scene must be disposed.
bool RestoreInteractionOrder(physx::PxScene& scene,
                             const InteractionImage& target,
                             std::string& error);

// Requires RestoreInteractionOrder first. Allocates the six lazy ActorPair
// report objects through the source bridge, then restores ordered event lists
// and the scalar SIP/ActorPair/CM/report metadata. Contact stream and manifold
// payload are still separate work; do not simulate after this stage alone.
bool RestoreInteractionMetadata(physx::PxScene& scene,
                                const InteractionImage& target,
                                std::string& error);

// Requires interaction metadata and the source-equivalent NpMemBlockPool
// backing to be restored first. Restores the six contact work units and
// owned single PCM manifolds, preserving each newly allocated manifold's
// self-pointer. Rejects stream pointers outside live pool blocks.
bool RestoreInteractionContactPayload(physx::PxScene& scene,
                                      const InteractionImage& target,
                                      std::string& error);

} // namespace physx333_offline
