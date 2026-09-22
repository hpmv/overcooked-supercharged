#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace physx333_offline {

struct ActorGraphKey {
    std::uint32_t first = 0, second = 0;
    bool operator==(const ActorGraphKey& b) const {
        return first == b.first && second == b.second;
    }
    bool operator<(const ActorGraphKey& b) const {
        return first < b.first || (first == b.first && second < b.second);
    }
};

struct ActorGraphShapeKey {
    std::uint32_t actor = 0, index = 0;
    bool operator==(const ActorGraphShapeKey& b) const {
        return actor == b.actor && index == b.index;
    }
    bool operator<(const ActorGraphShapeKey& b) const {
        return actor < b.actor || (actor == b.actor && index < b.index);
    }
};

struct ActorGraphPoolImage {
    std::uint32_t slabCount = 0, elementsPerSlab = 0;
    std::uint32_t usedCount = 0, unreleasedFree = 0, slabSize = 0;
    std::vector<std::uint32_t> usedSlots;
    std::vector<std::uint32_t> freeOrder;
    bool operator==(const ActorGraphPoolImage& b) const {
        return slabCount == b.slabCount && elementsPerSlab == b.elementsPerSlab &&
            usedCount == b.usedCount && unreleasedFree == b.unreleasedFree &&
            slabSize == b.slabSize && usedSlots == b.usedSlots &&
            freeOrder == b.freeOrder;
    }
};

struct ActorGraphSipRow {
    ActorGraphShapeKey shape0, shape1; // Native endpoint orientation.
    ActorGraphKey actorKey;             // Canonical actor identity.
    std::uint32_t sipSlot = 0, actorPairSlot = 0;
    bool hasTouch = false, isReportPair = false;
    bool operator==(const ActorGraphSipRow& b) const {
        return shape0 == b.shape0 && shape1 == b.shape1 &&
            actorKey == b.actorKey && sipSlot == b.sipSlot &&
            actorPairSlot == b.actorPairSlot && hasTouch == b.hasTouch &&
            isReportPair == b.isReportPair;
    }
};

struct ActorGraphPairRow {
    ActorGraphKey key;
    std::uint32_t actorA = 0, actorB = 0; // Native/report orientation.
    std::uint32_t poolSlot = 0, refCount = 0, touchCount = 0;
    std::uint32_t internalFlags = 0, sipOwners = 0, touchingOwners = 0;
    bool inReportSet = false, hasReportData = false;
    std::uint32_t reportPoolSlot = 0xffffffffu;
    std::uint32_t reportResetStamp = 0;
    std::uint32_t reportActorA = 0, reportActorB = 0;
    std::uint32_t clientA = 0, clientB = 0;
    std::uint32_t behaviorA = 0, behaviorB = 0;
    bool streamValid = false;
    std::uint32_t streamBufferIndex = 0, streamMaxPairs = 0;
    std::uint32_t streamCurrentPairs = 0, streamExtraSize = 0;
    std::uint32_t streamFlagsAndMaxExtra = 0;
    bool operator==(const ActorGraphPairRow& b) const;
};

struct ActorPairGraphImage {
    std::vector<ActorGraphSipRow> sips; // InteractionScene order.
    std::vector<ActorGraphPairRow> actorPairs; // Sorted by canonical actor key.
    std::vector<ActorGraphKey> reportSetOrder;
    ActorGraphPoolImage actorPairPool, reportDataPool;
    bool operator==(const ActorPairGraphImage& b) const {
        return sips == b.sips && actorPairs == b.actorPairs &&
            reportSetOrder == b.reportSetOrder &&
            actorPairPool == b.actorPairPool && reportDataPool == b.reportDataPool;
    }
};

// Read only; call at a completed fetchResults boundary. Rigid actors must have
// unique, nonzero 32-bit userData IDs. The image normalizes actor/shape pointers
// into those IDs and shape indices, but pool slots remain same-scene identities.
bool CaptureActorPairGraph(physx::PxScene& scene, ActorPairGraphImage& image,
                           std::string& error);

} // namespace physx333_offline
