#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace physx333_offline {

// Normalized, read-only image of the trigger and marker interaction branch.
// Pool slots, actor userData IDs, and shape indices replace process pointers.
struct AuxShapeKey {
    std::uint32_t actorId = 0;
    std::uint32_t shapeIndex = 0;
    bool operator==(const AuxShapeKey& b) const {
        return actorId == b.actorId && shapeIndex == b.shapeIndex;
    }
};

struct AuxPairKey {
    std::uint32_t type = 0;
    AuxShapeKey shape0;
    AuxShapeKey shape1;
    bool operator==(const AuxPairKey& b) const {
        return type == b.type && shape0 == b.shape0 && shape1 == b.shape1;
    }
};

struct AuxPoolImage {
    std::uint32_t slabCount = 0;
    std::uint32_t elementsPerSlab = 0;
    std::uint32_t usedCount = 0;
    std::int32_t unReleasedFree = 0;
    std::uint32_t slabSize = 0;
    std::vector<std::uint32_t> freeOrder;
    bool operator==(const AuxPoolImage& b) const {
        return slabCount == b.slabCount &&
            elementsPerSlab == b.elementsPerSlab &&
            usedCount == b.usedCount &&
            unReleasedFree == b.unReleasedFree &&
            slabSize == b.slabSize && freeOrder == b.freeOrder;
    }
};

struct AuxInteractionRow {
    AuxPairKey pair;
    std::uint32_t poolSlot = 0;
    std::uint32_t sceneIndex = 0;
    std::uint32_t actorIndex0 = 0;
    std::uint32_t actorIndex1 = 0;
    std::uint32_t active = 0;
    std::uint32_t interactionFlags = 0;
    std::uint32_t coreFlags = 0;
    std::uint32_t dirtyFlags = 0;
    // Trigger-only fields. Marker rows keep these zero.
    std::uint32_t triggerFlags = 0;
    std::uint32_t lastFrameHadContacts = 0;
    std::uint32_t triggerCacheState = 0;
    // Box/box trigger overlap ignores cache.dir and cache.gjkState. They are
    // never initialized by this fixture and must not enter the image.
    bool operator==(const AuxInteractionRow& b) const {
        return pair == b.pair && poolSlot == b.poolSlot &&
            sceneIndex == b.sceneIndex && actorIndex0 == b.actorIndex0 &&
            actorIndex1 == b.actorIndex1 && active == b.active &&
            interactionFlags == b.interactionFlags &&
            coreFlags == b.coreFlags && dirtyFlags == b.dirtyFlags &&
            triggerFlags == b.triggerFlags &&
            lastFrameHadContacts == b.lastFrameHadContacts &&
            triggerCacheState == b.triggerCacheState;
    }
};

struct AuxSceneOrder {
    std::uint32_t type = 0;
    std::uint32_t activeCount = 0;
    std::uint32_t capacity = 0;
    std::vector<AuxPairKey> pairs;
    bool operator==(const AuxSceneOrder& b) const {
        return type == b.type && activeCount == b.activeCount &&
            capacity == b.capacity && pairs == b.pairs;
    }
};

struct AuxActorOrder {
    std::uint32_t actorId = 0;
    std::uint32_t capacity = 0;
    std::uint32_t transferringCount = 0;
    std::uint32_t uniqueCount = 0;
    std::uint32_t countedCount = 0;
    std::vector<AuxPairKey> pairs;
    bool operator==(const AuxActorOrder& b) const {
        return actorId == b.actorId && capacity == b.capacity &&
            transferringCount == b.transferringCount &&
            uniqueCount == b.uniqueCount && countedCount == b.countedCount &&
            pairs == b.pairs;
    }
};

struct AuxInteractionImage {
    AuxPoolImage triggerPool;
    AuxPoolImage markerPool;
    std::vector<AuxInteractionRow> triggers;
    std::vector<AuxInteractionRow> markers;
    // Overlap entries are included here only to preserve mixed scene/actor
    // ordering; their contact payload remains owned by InteractionImage.
    std::vector<AuxSceneOrder> sceneOrders;
    std::vector<AuxActorOrder> actorOrders;

    // Ignores scene and allocation addresses, so separately built scenes can
    // be compared. It does not claim that either scene can be restored yet.
    bool equals(const AuxInteractionImage& b,
                std::string& firstDifference) const;
};

// Quiescent, read-only capture. Actor userData must hold unique nonzero IDs.
// Current cache policy intentionally supports box/box triggers only.
bool CaptureAuxInteractionImage(physx::PxScene& scene,
                                AuxInteractionImage& image,
                                std::string& error);

} // namespace physx333_offline
