// Diagnostic raw-allocation rewind oracle for the pinned, source-built PhysX.
// The arena is deliberately not a proposed allocator for Unity's binary.
#include <cstdlib>
#include <cstddef>
#include <iostream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "ArenaSnapshot.h"

#define private public
#define protected public
#include "PxsAABBManager.h"
#undef protected
#undef private

using namespace physx;

namespace {

void require(bool ok, const std::string& message)
{
    if (!ok)
    {
        std::cerr << "FAIL " << message << '\n';
        std::exit(1);
    }
}

struct Errors : PxErrorCallback
{
    PxU32 count = 0;
    void reportError(PxErrorCode::Enum, const char* message,
                     const char*, int) override
    {
        ++count;
        std::cerr << "PhysX " << message << '\n';
    }
};

struct InlineDispatcher : PxCpuDispatcher
{
    void submitTask(PxBaseTask& task) override
    {
        task.run();
        task.release();
    }
    PxU32 getWorkerCount() const override { return 0; }
};

PxFilterFlags contactFilter(PxFilterObjectAttributes, PxFilterData,
                            PxFilterObjectAttributes, PxFilterData,
                            PxPairFlags& flags, const void*, PxU32)
{
    flags = PxPairFlag::eCONTACT_DEFAULT |
            PxPairFlag::eNOTIFY_TOUCH_FOUND |
            PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
            PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct Events : PxSimulationEventCallback
{
    std::vector<PxU32> words;
    void onConstraintBreak(PxConstraintInfo*, PxU32) override {}
    void onWake(PxActor**, PxU32) override {}
    void onSleep(PxActor**, PxU32) override {}
    void onTrigger(PxTriggerPair*, PxU32) override {}
    void onContact(const PxContactPairHeader& header,
                   const PxContactPair* pairs, PxU32 count) override
    {
        for (PxU32 i = 0; i < count; ++i)
        {
            words.push_back(static_cast<PxU32>(
                reinterpret_cast<std::uintptr_t>(header.actors[0]->userData)));
            words.push_back(static_cast<PxU32>(
                reinterpret_cast<std::uintptr_t>(header.actors[1]->userData)));
            words.push_back(static_cast<PxU16>(pairs[i].events));
            words.push_back(pairs[i].contactCount);
        }
    }
};

struct Fixture
{
    oc2::offline::ArenaSnapshotAllocator allocator{256u * 1024u * 1024u};
    Errors errors;
    InlineDispatcher dispatcher;
    Events events;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;
    PxScene* scene = nullptr;
    PxRigidDynamic* mover = nullptr;
    std::vector<PxRigidStatic*> statics;

    explicit Fixture(bool mixed)
    {
        require(allocator.valid(), "reserve fixed-address arena");
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        require(foundation != nullptr, "foundation creation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        require(physics != nullptr, "physics creation");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        require(material != nullptr, "material creation");
        PxSceneDesc desc(physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &dispatcher;
        desc.simulationEventCallback = &events;
        desc.filterShader = contactFilter;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        desc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
        desc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
        scene = physics->createScene(desc);
        require(scene != nullptr, "scene creation");

        for (PxU32 i = 0; i < 12; ++i)
        {
            const bool cornerGap = mixed && (i == 8 || i == 10);
            PxRigidStatic* fixed = physics->createRigidStatic(
                PxTransform(PxVec3(PxReal(i) * 3.0f +
                                    (cornerGap ? 1.1f : 0.0f),
                                    0.0f,
                                    i < 8 ? 0.0f :
                                    (cornerGap ? 1.1f : 0.9f))));
            require(fixed != nullptr, "static actor creation");
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(shape != nullptr, "static shape creation");
            fixed->attachShape(*shape);
            shape->release();
            fixed->userData = reinterpret_cast<void*>(
                std::uintptr_t(i + 1));
            scene->addActor(*fixed);
            statics.push_back(fixed);
        }
        mover = physics->createRigidDynamic(PxTransform(
            PxVec3(0.0f, mixed ? 0.95f : 0.0f, 0.0f)));
        require(mover != nullptr, "dynamic actor creation");
        for (PxU32 i = 0; i < 12; ++i)
        {
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(shape != nullptr, "dynamic shape creation");
            mover->attachShape(*shape);
            const PxVec3 local(PxReal(i) * 3.0f, 0.0f, 0.0f);
            if (mixed && (i == 8 || i == 10))
                shape->setLocalPose(PxTransform(
                    local,
                    PxQuat(0.78539816339f, PxVec3(0.0f, 1.0f, 0.0f))));
            else
                shape->setLocalPose(PxTransform(local));
            shape->release();
        }
        mover->setMass(12.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(12.0f));
        mover->userData = reinterpret_cast<void*>(std::uintptr_t(13));
        scene->addActor(*mover);
    }

    ~Fixture()
    {
        // Actor and scene destruction is deliberately outside the measured
        // rewind interval. Their native OS resources are not snapshotted.
        mover->release();
        for (PxRigidStatic* actor : statics) actor->release();
        scene->release();
        material->release();
        physics->release();
        foundation->release();
    }

    void step(PxReal z)
    {
        events.words.clear();
        mover->setGlobalPose(PxTransform(PxVec3(0.0f, 0.0f, z)));
        scene->simulate(1.0f / 60.0f);
        require(scene->fetchResults(true), "fetchResults");
        require(errors.count == 0, "PhysX error");
    }

    PxU32 contactPairs() const
    {
        PxSimulationStatistics stats;
        scene->getSimulationStatistics(stats);
        return stats.nbDiscreteContactPairs[PxGeometryType::eBOX]
                                           [PxGeometryType::eBOX];
    }
};

void normalizeAabbTaskPadding(oc2::offline::ArenaSnapshotAllocator::Image& image)
{
    // PxsComputeAABBParams has three unwritten bytes between its bool and
    // pointer on Win32. PxsAABBManager copies this struct into 25 embedded
    // tasks each step. Those bytes are not read by the source and can differ
    // even with identical initialized physics state.
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
    require(pads.size() == 75, "unexpected AABB padding inventory");

    std::size_t managerOffset = 0;
    unsigned managers = 0;
    for (const auto& block : image.blocks)
        if (block.size == sizeof(PxsAABBManager) &&
            block.file.find("PxsContext.cpp") != std::string::npos)
        {
            managerOffset = block.offset;
            ++managers;
        }
    require(managers == 1, "AABB manager arena block is not unique");
    for (std::size_t pad : pads)
    {
        require(pad < sizeof(PxsAABBManager), "AABB pad out of bounds");
        image.bytes[managerOffset + pad] = 0;
    }
}

bool sameInitializedArena(const oc2::offline::ArenaSnapshotAllocator::Image& a,
                          const oc2::offline::ArenaSnapshotAllocator::Image& b,
                          std::string& error)
{
    auto lhs = a;
    auto rhs = b;
    normalizeAabbTaskPadding(lhs);
    normalizeAabbTaskPadding(rhs);
    return lhs.equals(rhs, error);
}

} // namespace

int main(int argc, char** argv)
{
    const bool mixed = argc == 2 && std::string(argv[1]) == "--mixed";
    require(argc == 1 || mixed, "usage: physx333_arena_snapshot [--mixed]");
    Fixture f(mixed);
    f.step(0.0f);
    f.step(0.0f);
    require(f.contactPairs() == 12, "checkpoint must have twelve pairs");
    const auto checkpoint = f.allocator.capture();
    f.step(-0.2f);
    require(f.contactPairs() == 8, "successor must have eight pairs");
    const auto expected = f.allocator.capture();
    const std::vector<PxU32> expectedEvents = f.events.words;
    {
        auto invalid = checkpoint;
        ++invalid.owner;
        std::string error;
        require(!f.allocator.restore(invalid, error),
                "foreign arena image must reject");
        require(expected.equals(f.allocator.capture(), error),
                "foreign image rejection changed arena: " + error);
        invalid = checkpoint;
        invalid.blocks.front().offset = 1;
        require(!f.allocator.restore(invalid, error),
                "misaligned arena block must reject");
        require(expected.equals(f.allocator.capture(), error),
                "malformed block rejection changed arena: " + error);
    }
    for (unsigned iteration = 0; iteration < 100; ++iteration)
    {
        std::string error;
        require(f.allocator.restore(checkpoint, error),
                "arena restore: " + error);
        const auto restored = f.allocator.capture();
        if (!checkpoint.equals(restored, error))
            require(false, "checkpoint arena differs: " + error);
        f.step(-0.2f);
        require(f.contactPairs() == 8, "replay pair count");
        require(f.events.words == expectedEvents, "replay callbacks differ");
        const auto replayed = f.allocator.capture();
        if (!sameInitializedArena(expected, replayed, error))
            require(false, "replayed arena differs: " + error);
    }
    std::cout << "PASS source-built 12-to-8 " <<
        (mixed ? "mixed " : "all-touch ") <<
        "raw-allocation replay x100\n";
}
