#pragma once

#include <cstdint>
#include <string>
#include <vector>

#include "../actor_pair_graph/ActorPairGraphImage.h"
#include "../oracle/Oracle.h"

namespace physx { class PxScene; }

namespace physx333_offline {

struct JoinedContactShapeKey {
    std::uint32_t actor = 0, shape = 0;
    bool operator==(const JoinedContactShapeKey& b) const {
        return actor == b.actor && shape == b.shape;
    }
    bool operator<(const JoinedContactShapeKey& b) const {
        return actor < b.actor || (actor == b.actor && shape < b.shape);
    }
};

// Endpoint order is PhysX's native ShapeInstancePairLL order, not a sorted key.
struct JoinedContactKey {
    JoinedContactShapeKey shape0, shape1;
    bool operator==(const JoinedContactKey& b) const {
        return shape0 == b.shape0 && shape1 == b.shape1;
    }
    bool operator<(const JoinedContactKey& b) const {
        return shape0 < b.shape0 ||
            (!(b.shape0 < shape0) && shape1 < b.shape1);
    }
};

struct JoinedContactBitmap {
    std::uintptr_t address = 0; // Same-scene storage identity only.
    std::vector<std::uint32_t> words;
    bool exact(const JoinedContactBitmap& b) const {
        return address == b.address && words == b.words;
    }
};

struct JoinedContactRow {
    JoinedContactKey key;
    ActorGraphKey actorPairKey;
    std::uint32_t sceneIndex = 0, sipSlot = 0, actorPairSlot = 0;
    std::uint32_t managerSlot = 0, islandEdge = 0;
    std::uint32_t sipFlags = 0, reportStamp = 0;
    std::uint32_t reportPairIndex = 0, reportStreamIndex = 0;
    std::uint32_t actorPairFlags = 0, actorPairTouchCount = 0;
    std::uint32_t actorPairRefCount = 0;
    std::uint32_t reportPoolSlot = 0xffffffffu, reportResetStamp = 0;
    std::uint32_t reportActorA = 0, reportActorB = 0;
    std::vector<unsigned char> reportStreamManager;
    std::uint32_t managerFlags = 0, managerStatusFlags = 0;
    // All 14 portable Oracle contact-manager words for this scene-order row.
    std::vector<std::uint32_t> managerWords;
    // Exact same-scene Win32 WorkUnit bytes, including source-owned pointers.
    // This is an observation, never an independently portable serialization.
    std::vector<unsigned char> workUnitBytes;
    std::vector<unsigned char> compressedContactBytes, pairCacheBytes;
    std::uint32_t manifoldKind = 0, manifoldContactCount = 0;
    std::uint32_t manifoldWarmStartCount = 0;
    std::vector<unsigned char> manifoldTransformBytes;
    std::vector<unsigned char> manifoldIndexBytes; // Four A and four B indices.
    std::vector<unsigned char> manifoldContactBytes; // Used contacts only.

    bool exact(const JoinedContactRow& b) const;
    bool portable(const JoinedContactRow& b) const;
};

struct JoinedContactImage {
    std::uintptr_t scene = 0;
    std::vector<JoinedContactRow> rows; // InteractionScene overlap order.
    ActorPairGraphImage actorPairs;
    OracleImage oracle;
    std::vector<JoinedContactKey> persistentEventOrder;
    std::vector<JoinedContactKey> forceThresholdEventOrder;
    std::uint32_t nextPersistentPair = 0;
    std::vector<std::uint32_t> managerFreeOrder;
    JoinedContactBitmap managerUse, activeManagers;
    JoinedContactBitmap modifiableManagers, touchEventManagers;
    std::uint32_t reportBufferIndex = 0, reportBufferSize = 0;
    std::uint32_t reportBufferDefaultSize = 0, reportBufferLastIndex = 0;
    std::uint32_t reportBufferAllocationLocked = 0;
    std::vector<unsigned char> reportBufferBytes;

    bool exact(const JoinedContactImage& b, std::string& difference) const;
    bool portable(const JoinedContactImage& b, std::string& difference) const;
};

// Read only, at a completed fetchResults boundary. This fixture accepts the
// level-shaped 12/4/2 checkpoint or 8/2/2 successor; it does not restore.
bool CaptureJoinedContactImage(physx::PxScene& scene,
                               JoinedContactImage& image,
                               std::string& error);

} // namespace physx333_offline
