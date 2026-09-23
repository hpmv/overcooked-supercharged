#pragma once

#include "../query_image/QueryImage.h"

namespace oc2 { namespace offline {

// Offline, source-mirror-only rewind of one BUILD_INIT -> committed tree swap.
// The original QueryImage restore gate remains unchanged.
// Caller must externally serialize against all queries and scene mutations.
// forceVerificationFailure is a fixture-only fault injection that exercises
// the escrow rollback path after all staged allocations have been installed.
bool RestoreAcrossOneQuerySwap(physx::PxScene& scene,
                               const QueryImage& checkpoint,
                               std::string& error,
                               bool forceVerificationFailure = false);

// Compare images after source-owned allocations move, while preserving all
// captured logical content and the known pointer-alias relationships.
bool EqualsCrossSwapRebased(const QueryImage& expected,
                           const QueryImage& observed,
                           std::string& error);

}} // namespace oc2::offline
