#pragma once

#include "../memblock/MemBlockImage.h"

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace physx333_offline {

// These are the persistent stream references held by each live low-level
// contact manager. A pool-only restore cannot repair changed references, so
// the restore preflight requires the same managers and reference values.
struct MemBlockContactBinding {
    std::uintptr_t managerAddress = 0;
    std::uint32_t managerIndex = 0;
    std::uint32_t workFlags = 0;
    std::uint32_t statusFlags = 0;
    std::uint32_t contactCount = 0;
    std::uint32_t pairCachePairData = 0;
    std::uintptr_t solverConstraint = 0;
    std::uint32_t solverConstraintSize = 0;
    std::uintptr_t compressedContacts = 0;
    std::uint32_t compressedContactSize = 0;
    std::uintptr_t frictionData = 0;
    std::uint32_t frictionPatchCount = 0;
    std::uintptr_t npCache = 0;
    std::uint32_t npCacheSize = 0;
    std::uintptr_t ccdContacts = 0;
    std::uintptr_t manifold = 0;

    bool operator==(const MemBlockContactBinding& other) const;
};

struct MemBlockRestoreImage {
    MemBlockImage pool;
    MemBlockIdentityRegistry registry;
    std::vector<MemBlockContactBinding> contactBindings;
};

// Capture only at a stopped fetchResults boundary. Both outputs are unchanged
// on failure. The registry is part of the checkpoint and must be retained.
bool CaptureMemBlockRestore(physx::PxScene& scene,
                            MemBlockIdentityRegistry& registry,
                            MemBlockRestoreImage& image,
                            std::string& error);

// Pool-only same-scene restore. The current registry must still describe
// exactly the checkpoint's live allocations. Every array's backing storage,
// capacity, size, and block membership must match. Active contact-manager
// stream pointers must match as well. These restrictions deliberately reject
// checkpoint-to-deletion resurrection until topology/allocator ownership is
// restored by a larger transaction.
//
// A rejected preflight makes no writes. A failed post-write verification
// restores the previous pool image and verifies the rollback. If rollback
// cannot be proved, the process terminates instead of simulating corrupt
// state. Never call while PhysX is simulating or concurrently accessed.
bool RestoreMemBlockPool(physx::PxScene& scene,
                         const MemBlockIdentityRegistry& currentRegistry,
                         const MemBlockRestoreImage& target,
                         std::string& error);

} // namespace physx333_offline
