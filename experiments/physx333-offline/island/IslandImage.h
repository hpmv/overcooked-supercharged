#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// A process-local image of the stopped PhysX 3.3.3 island manager. It is
// intentionally tied to one scene and to the existing allocation addresses.
struct IslandImage
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
        enum Kind { Node, Edge, ArticulationRoot } kind = Node;
        std::uint32_t id = 0;
        std::uintptr_t owner = 0;
        std::uint32_t endpoint0 = 0;
        std::uint32_t endpoint1 = 0;
        std::uint32_t type = 0;
    };

    std::uintptr_t scene = 0;
    std::uintptr_t context = 0;
    std::uintptr_t manager = 0;
    std::uintptr_t scratchAllocator = 0;
    std::vector<Scalar> scalars;
    std::vector<Buffer> buffers;
    std::vector<Binding> bindings;

    bool equals(const IslandImage& other, std::string& firstDifference) const;
};

// Valid after fetchResults and before the next simulate, with no intervening
// actor/edge mutations. Restore requires the same allocation addresses and
// the same active node, edge and articulation bindings. In particular, an
// edge whose contact manager has been destroyed cannot be resurrected here.
// The caller must restore NPhase and all other PhysX components before it can
// simulate; successful island restore alone does not imply scene parity.
bool CaptureIsland(physx::PxScene& scene, IslandImage& image, std::string& error);
bool RestoreIsland(physx::PxScene& scene, const IslandImage& image, std::string& error);

}} // namespace oc2::offline
