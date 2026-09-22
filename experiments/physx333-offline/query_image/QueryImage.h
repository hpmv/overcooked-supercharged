#pragma once

#include <cstdint>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace oc2 { namespace offline {

// A source-layout image of the settled SceneQueryManager and its AABB pruners.
// Only valid in the same scene while every guarded allocation is still live.
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
};

bool CaptureQueryImage(physx::PxScene& scene, QueryImage& out,
                       std::string& error);
bool RestoreQueryImage(physx::PxScene& scene, const QueryImage& target,
                       std::string& error);

}} // namespace oc2::offline
