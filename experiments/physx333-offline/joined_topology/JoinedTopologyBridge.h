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
    void* targetManifoldAddress;        // contacts only; same-scene PCM owner
    std::uint32_t targetManifoldPoolSlot; // contacts only; UINT32_MAX otherwise
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

// Same-process, same-scene report/touch stage after the topology bridge.
// Roles are the twelve checkpoint contact scene-order rows. The endpoint
// addresses and pool slots are guards, not a persisted Unity ABI.
struct JoinedReportRoleV1 {
    void* shapeCore0;
    void* shapeCore1;
    std::uint32_t sipSlot, actorPairSlot, managerSlot, reportPoolSlot;
    std::uint32_t sipFlags, reportStamp, reportPairIndex, reportStreamIndex;
    std::uint32_t actorPairFlags, touchCount, refCount;
    std::uint32_t reportResetStamp, reportActorAId, reportActorBId;
    std::uint32_t reportStreamSize;
    unsigned char reportStreamBytes[16];
    std::uint32_t managerFlags, managerStatusFlags;
};

struct JoinedReportBitmapV1 {
    void* expectedStorage;
    std::uint32_t count;
    std::uint32_t words[64];
};

struct JoinedReportPlanV1 {
    JoinedReportRoleV1 roles[12];
    std::uint32_t createRoles[2];
    std::uint32_t persistentRoles[10];
    std::uint32_t currentReportFreeCount, currentReportFreeOrder[32];
    std::uint32_t targetReportFreeCount, targetReportFreeOrder[32];
    std::uint32_t targetNextPersistent;
    std::uint32_t reportBufferIndex, reportBufferSize;
    std::uint32_t reportBufferDefaultSize, reportBufferLastIndex;
    std::uint32_t reportBufferAllocationLocked;
    JoinedReportBitmapV1 managerUse, activeManagers;
    JoinedReportBitmapV1 modifiableManagers, touchEventManagers;
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

enum JoinedReportResultV1 {
    JoinedReportSuccess = 0,
    JoinedReportInvalidInput = 1,
    JoinedReportUnsupportedScene = 2,
    JoinedReportPoolMismatch = 3,
    JoinedReportPairMismatch = 4
};

#ifdef PHYSX333_JOINED_TOPOLOGY_BRIDGE_BUILD
#define OC2_JOINED_TOPOLOGY_API __declspec(dllexport)
#else
#define OC2_JOINED_TOPOLOGY_API __declspec(dllimport)
#endif

// Recreates only the four missing contact and two missing trigger interactions
// in the synthetic 12/4/2 graph. Existing marker objects survive. It repairs
// interaction identity, manager/edge IDs, pool slots, PCM manifold identity,
// scene order, all
// actors' mixed order and reverse indices, and initialized trigger history.
// Contact report data, contact-manager payload, manifold contents, broadphase,
// and the complete island graph are not restored by this function. All
// rejection is prewrite; any postwrite
// mismatch aborts rather than returning a partially mutated scene.
extern "C" OC2_JOINED_TOPOLOGY_API std::uint32_t __cdecl
oc2_physx333_joined_topology_recreate_v1(void* nphaseCore,
    const JoinedTopologyPlanV1* plan);

// Uses the original ActorPair lazy report constructor for exactly two new
// report objects, then restores graph-keyed SIP/AP touch scalars, the ordered
// persistent-event list, report data, and contact-manager event bitmaps.
// All validation rejects before writing. A postwrite invariant failure is
// fail-stop; this stage alone is not safe to simulate or a full rewind.
extern "C" OC2_JOINED_TOPOLOGY_API std::uint32_t __cdecl
oc2_physx333_joined_report_restore_v1(void* nphaseCore,
    const JoinedReportPlanV1* plan);

} // namespace physx333_offline
