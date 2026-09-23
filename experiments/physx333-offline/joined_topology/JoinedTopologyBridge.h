#pragma once

#include <cstdint>

namespace physx333_offline {

// Test-only, same-scene addresses. This is deliberately not a persisted
// checkpoint format and not a Unity ABI. Roles 0..11 are contacts, 12..15
// triggers, and 16..17 markers; all four endpoint pointers use Sc cores.
struct JoinedTopologyRoleV1 {
    std::uint32_t type;
    void* actorCore0;
    void* shapeCore0;
    void* actorCore1;
    void* shapeCore1;
    std::uint32_t expectedPairFlags;
    std::uint32_t targetInteractionPoolSlot;
    std::uint32_t targetActorPairPoolSlot; // contacts only; UINT32_MAX otherwise
    std::uint32_t targetContactManagerIndex; // contacts only; UINT32_MAX otherwise
    std::uint32_t targetIslandEdgeId; // contacts only; UINT32_MAX otherwise
    std::uint32_t targetInteractionFlags;
    std::uint32_t targetCoreFlags;
    std::uint32_t targetDirtyFlags;
    std::uint32_t targetTriggerFlags;       // triggers only; zero otherwise
    std::uint32_t targetLastTouch;          // triggers only; zero otherwise
    std::uint32_t targetCacheState;         // triggers only; zero otherwise
};

struct JoinedTopologyActorOrderV1 {
    void* actorCore;
    std::uint32_t count;
    std::uint32_t roleIds[18];
};

struct JoinedTopologyPlanV1 {
    JoinedTopologyRoleV1 roles[18];
    // Exact source lifecycle creation order. Each list must contain precisely
    // the roles absent from the successor and no surviving role.
    std::uint32_t missingContactRoles[4];
    std::uint32_t missingTriggerRoles[2];
    std::uint32_t contactSceneOrder[12];
    std::uint32_t triggerSceneOrder[4];
    std::uint32_t markerSceneOrder[2];
    JoinedTopologyActorOrderV1 actors[13];
};

enum JoinedTopologyResultV1 {
    JoinedTopologySuccess = 0,
    JoinedTopologyInvalidInput = 1,
    JoinedTopologyUnsupportedScene = 2,
    JoinedTopologyUnresolvedShape = 3,
    JoinedTopologyUnexpectedFilter = 4,
    JoinedTopologyPoolMismatch = 5,
    JoinedTopologyExistingInteraction = 6
};

#ifdef PHYSX333_JOINED_TOPOLOGY_BRIDGE_BUILD
#define OC2_JOINED_TOPOLOGY_API __declspec(dllexport)
#else
#define OC2_JOINED_TOPOLOGY_API __declspec(dllimport)
#endif

// Recreates only the four missing contact and two missing trigger interactions
// in the synthetic 12/4/2 graph. Existing marker objects survive. It repairs
// interaction identity, manager/edge IDs, pool slots, scene order, all
// actors' mixed order and reverse indices, and initialized trigger history.
// Contact report data, contact-manager payload, manifold bytes, broadphase,
// and the complete island graph are not restored by this function. All
// rejection is prewrite; any postwrite
// mismatch aborts rather than returning a partially mutated scene.
extern "C" OC2_JOINED_TOPOLOGY_API std::uint32_t __cdecl
oc2_physx333_joined_topology_recreate_v1(void* nphaseCore,
    const JoinedTopologyPlanV1* plan);

} // namespace physx333_offline
