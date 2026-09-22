#include "ArenaAabbPadding.h"

#include <cstddef>
#include <vector>
#include "PxPhysicsAPI.h"

#define private public
#define protected public
#include "PxsAABBManager.h"
#undef protected
#undef private

namespace oc2 { namespace offline {

bool NormalizeAabbTaskPadding(ArenaSnapshotAllocator::Image& image,
                              std::string& error)
{
    using namespace physx;
    error.clear();
    // The bool at byte 12 is followed by three bytes that the pinned source
    // copies into 25 embedded AABB task parameters but never initializes.
    static_assert(offsetof(PxsComputeAABBParams, secondBroadPhase) == 12,
                  "AABB task parameter layout changed");
    static_assert(offsetof(PxsComputeAABBParams, numFastMovingShapes) == 16,
                  "AABB task parameter padding changed");
    std::vector<std::size_t> pads;
    const auto add = [&pads](std::size_t paramsOffset) {
        for (std::size_t byte = 1; byte <= 3; ++byte)
            pads.push_back(paramsOffset +
                           offsetof(PxsComputeAABBParams, secondBroadPhase) +
                           byte);
    };
    const auto single = [&add](std::size_t taskOffset) {
        add(taskOffset + offsetof(SingleAABBTask, mParams));
        for (std::size_t i = 0; i < 6; ++i)
            add(taskOffset + offsetof(SingleAABBTask, mAABBUpdateTask) +
                i * sizeof(SingleAABBUpdateTask) +
                offsetof(SingleAABBUpdateTask, mParams));
    };
    single(offsetof(PxsAABBManager, mSingleShapeAABBTask));
    add(offsetof(PxsAABBManager, mActorAABBTask) +
        offsetof(ActorAABBTask, mParams));
    add(offsetof(PxsAABBManager, mAggregateAABBTask) +
        offsetof(AggregateAABBTask, mParams));
    for (std::size_t i = 0; i < 6; ++i)
        add(offsetof(PxsAABBManager, mAggregateAABBTask) +
            offsetof(AggregateAABBTask, mAABBUpdateTask) +
            i * sizeof(AggregateAABBUpdateTask) +
            offsetof(AggregateAABBUpdateTask, mParams));
    add(offsetof(PxsAABBManager, mBPWorkTask) +
        offsetof(BPWorkTask, mParams));
    add(offsetof(PxsAABBManager, mProcessBPResultsTask) +
        offsetof(ProcessBPResultsTask, mParams));
    single(offsetof(PxsAABBManager, mAggregateShapeAABBTask));
    add(offsetof(PxsAABBManager, mAggregateOverlapTask) +
        offsetof(AggregateOverlapTask, mParams));
    if (pads.size() != 75) {
        error = "AABB padding inventory changed";
        return false;
    }

    std::size_t managerOffset = 0;
    unsigned managers = 0;
    for (const auto& block : image.blocks)
        if (block.size == sizeof(PxsAABBManager) &&
            block.file.find("PxsContext.cpp") != std::string::npos)
        {
            managerOffset = block.offset;
            ++managers;
        }
    if (managers != 1 || managerOffset > image.bytes.size() ||
        sizeof(PxsAABBManager) > image.bytes.size() - managerOffset)
    {
        error = "AABB manager arena block is not unique or in bounds";
        return false;
    }
    for (std::size_t pad : pads)
    {
        if (pad >= sizeof(PxsAABBManager)) {
            error = "AABB padding offset is out of bounds";
            return false;
        }
        image.bytes[managerOffset + pad] = 0;
    }
    return true;
}

}} // namespace oc2::offline
