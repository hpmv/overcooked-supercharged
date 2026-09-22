#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// A source-layout image of the settled SceneQueryManager and its AABB pruners.
// Same-scene only; guarded allocations must remain live except the explicit
// progressive FIFO rebase and cold BUILD_INIT rewind documented below.
struct QueryImage
{
    struct Field
    {
        std::string name;
        std::uintptr_t address = 0;
        std::vector<std::uint8_t> bytes;
        bool invariant = false;
    };

    std::uintptr_t scene = 0;
    std::vector<Field> fields;
    std::uint64_t seal = 0;

    bool equals(const QueryImage& other, std::string& error) const;
    // Only the progressive dynamic new-tree FIFO's backing array may have
    // moved. All other fields, including its logical size/capacity/content,
    // must still match. This does not imply identical allocator history.
    bool equalsWithRebasedStack(const QueryImage& other,
                                std::string& error) const;
    // After a cold BUILD_INIT rewind, source PhysX rebuilds indices, nodes,
    // FIFO object and FIFO backing at new addresses. Compare the first
    // BUILD_IN_PROGRESS step's initialized node fields and translate FIFO
    // node pointers to node-array offsets. Uninitialized node AABB bytes are
    // deliberately excluded; later build steps are outside this comparator.
    bool equalsWithRebuiltColdTree(const QueryImage& other,
                                   std::string& error) const;
};

bool CaptureQueryImage(physx::PxScene& scene, QueryImage& out,
                       std::string& error);
bool RestoreQueryImage(physx::PxScene& scene, const QueryImage& target,
                       std::string& error);

}} // namespace oc2::offline
