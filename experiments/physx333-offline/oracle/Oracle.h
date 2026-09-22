#pragma once

#include <cstdint>
#include <map>
#include <string>
#include <vector>

namespace physx { class PxScene; }

namespace physx333_offline {

// A quiescent, read-only projection of the original PhysX 3.3.3 internals.
// All object references in parts are fixture IDs, pool slot IDs, or ordered
// indices. No process addresses are intentionally present in this image.
struct OracleImage {
    std::map<std::string, std::vector<std::uint32_t> > parts;
    std::vector<std::string> unsupported;

    bool equals(const OracleImage& other, std::string& firstDifference) const;
    std::string summary() const;
};

// Must be called after PxScene::fetchResults(), before the next scene write.
// Fails if actor IDs (PxActor::userData) are absent or duplicated, or if a
// private graph/list invariant fails. This function never mutates PhysX.
bool CaptureOracle(physx::PxScene& scene, OracleImage& image,
                   std::string& error);

} // namespace physx333_offline
