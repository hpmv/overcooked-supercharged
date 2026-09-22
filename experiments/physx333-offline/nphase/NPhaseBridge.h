#pragma once

#include <cstdint>

namespace physx333_offline {

// POD ABI shared with a test-only export built into the disposable PhysX
// 3.3.3 mirror. The addresses are Sc::RigidCore and Sc::ShapeCore objects
// belonging to the same live scene. They are never persisted in an image.
struct NPhaseBridgePairV1 {
    void* actorCore0;
    void* shapeCore0;
    void* actorCore1;
    void* shapeCore1;
    std::uint32_t expectedPairFlags;
};

enum NPhaseBridgeResultV1 {
    NPhaseBridgeSuccess = 0,
    NPhaseBridgeInvalidInput = 1,
    NPhaseBridgeUnsupportedScene = 2,
    NPhaseBridgeUnresolvedShape = 3,
    NPhaseBridgeUnexpectedFilter = 4,
    NPhaseBridgeExistingInteraction = 5
};

// Export name: oc2_physx333_nphase_recreate_v1 (cdecl, undecorated DLL name).
// It runs a complete preflight before calling the original lifecycle method.
// Supported scene: quiescent, six distinct static/dynamic box contacts, no
// other shape interactions, no filter callback, eDEFAULT filter result.
typedef std::uint32_t (__cdecl* NPhaseRecreateFnV1)(
    void* nphaseCore, const NPhaseBridgePairV1* pairs,
    std::uint32_t pairCount);

} // namespace physx333_offline
