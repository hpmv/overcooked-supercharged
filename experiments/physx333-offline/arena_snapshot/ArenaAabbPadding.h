#pragma once

#include <string>
#include "ArenaSnapshot.h"

namespace oc2 { namespace offline {

// For diagnostic byte equality only. The image itself retains raw bytes.
// Exactly 75 source-proven, unwritten task-parameter padding bytes are masked.
bool NormalizeAabbTaskPadding(ArenaSnapshotAllocator::Image& image,
                              std::string& error);

}} // namespace oc2::offline
