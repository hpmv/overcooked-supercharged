#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// Test-only image of the SAP and broadphase element allocations in one stopped
// PhysX 3.3.3 scene. Addresses deliberately remain process-local: this first
// restore requires the same scene and the same live allocation topology.
struct SapImage
{
    struct Scalar
    {
        std::string name;
        std::uintptr_t address = 0;
        bool topology = false;
        std::vector<unsigned char> bytes;
    };

    struct Buffer
    {
        std::string name;
        std::uintptr_t address = 0;
        std::vector<unsigned char> bytes;
    };

    struct Binding
    {
        std::uint32_t id = 0;
        std::uintptr_t userData = 0;
        std::uint32_t group = 0;
        std::uint32_t ownerId = 0;
        std::uint32_t aabbDataHandle = 0;
        std::uintptr_t shapeCore = 0;
        std::uintptr_t rigidCore = 0;
        std::uintptr_t bodyAtom = 0;
        std::uintptr_t localSpaceAabb = 0;
    };

    std::uintptr_t scene = 0;
    std::uintptr_t manager = 0;
    std::uintptr_t sap = 0;
    std::uint32_t activeElements = 0;
    std::vector<Scalar> scalars;
    std::vector<Buffer> buffers;
    std::vector<Binding> bindings;

    bool equals(const SapImage& other, std::string& firstDifference) const;
};

// Calls are valid only after fetchResults and before the next simulate. This
// component does not restore contacts, NPhase, or islands. A caller must not
// simulate after RestoreSap until all those companion components are restored.
bool CaptureSap(physx::PxScene& scene, SapImage& image, std::string& error);
bool RestoreSap(physx::PxScene& scene, const SapImage& image, std::string& error);

}} // namespace oc2::offline
