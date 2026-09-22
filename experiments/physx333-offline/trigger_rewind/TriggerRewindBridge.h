#pragma once

#include <cstdint>

namespace physx333_offline {

// Addresses are live Sc cores from the same scene, never checkpoint data.
struct TriggerRewindPairV1 {
    void* dynamicActorCore;
    void* dynamicShapeCore;
    void* staticActorCore;
    void* staticShapeCore;
    std::uint32_t targetPoolSlot;
    std::uint32_t expectedPairFlags;
    std::uint32_t targetInteractionFlags;
    std::uint32_t targetCoreFlags;
    std::uint32_t targetDirtyFlags;
    std::uint32_t targetTriggerFlags;
    std::uint32_t targetLastTouch;
    std::uint32_t targetCacheState;
};

enum TriggerRewindResultV1 {
    TriggerRewindSuccess = 0,
    TriggerRewindInvalidInput = 1,
    TriggerRewindUnsupportedScene = 2,
    TriggerRewindUnresolvedShape = 3,
    TriggerRewindUnexpectedFilter = 4,
    TriggerRewindPoolMismatch = 5
};

// Export built only into the isolated source mirror for this experiment.
// All failures before the first write leave the scene untouched. Any failure
// after that point aborts, because a partial NPhase restoration is unsafe.
#ifdef PHYSX333_TRIGGER_BRIDGE_BUILD
#define OC2_TRIGGER_BRIDGE_API __declspec(dllexport)
#else
#define OC2_TRIGGER_BRIDGE_API __declspec(dllimport)
#endif
extern "C" OC2_TRIGGER_BRIDGE_API std::uint32_t __cdecl
oc2_physx333_trigger_recreate_v1(void* nphaseCore,
    const TriggerRewindPairV1* pairs, std::uint32_t pairCount);

} // namespace physx333_offline
