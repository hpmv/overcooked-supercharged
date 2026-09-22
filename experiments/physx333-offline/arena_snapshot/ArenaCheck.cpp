// Diagnostic raw-allocation rewind oracle for the pinned, source-built PhysX.
// The arena is deliberately not a proposed allocator for Unity's binary.
#include <cstdlib>
#include <cstddef>
#include <iostream>
#include <string>
#include <utility>
#include <vector>

#include "PxPhysicsAPI.h"
#include "ArenaSnapshot.h"
#include "ArenaAabbPadding.h"

#define private public
#define protected public
#include "NpPhysics.h"
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

bool sameInitializedArena(const oc2::offline::ArenaSnapshotAllocator::Image& a,
                          const oc2::offline::ArenaSnapshotAllocator::Image& b,
                          std::string& error)
{
    auto lhs = a;
    auto rhs = b;
    const bool leftValid = oc2::offline::NormalizeAabbTaskPadding(lhs, error);
    require(leftValid, error);
    const bool rightValid = oc2::offline::NormalizeAabbTaskPadding(rhs, error);
    require(rightValid, error);
    return lhs.equals(rhs, error);
}

PxShape* onlyShape(PxRigidStatic& actor)
{
    PxShape* shape = nullptr;
    require(actor.getNbShapes() == 1 && actor.getShapes(&shape, 1) == 1 &&
            shape != nullptr, "static actor must have one shape");
    return shape;
}

PxShape* staticRayHit(PxScene& scene, PxReal x, PxReal z)
{
    PxRaycastBuffer hit;
    const PxQueryFilterData filter{PxQueryFlags(PxQueryFlag::eSTATIC)};
    const bool found = scene.raycast(PxVec3(x, 5.0f, z),
                                     PxVec3(0.0f, -1.0f, 0.0f), 10.0f,
                                     hit, PxHitFlag::eDEFAULT, filter);
    return found && hit.hasBlock ? hit.block.shape : nullptr;
}

int runLifetimeDiagnostic()
{
    // PxDeletionListener callbacks and the fixture's C++ vectors live outside
    // the arena. Keep the former absent and explicitly repair the latter.
    Fixture f(false);
    require(!static_cast<NpPhysics*>(f.physics)->mDeletionListenersExist,
            "lifetime diagnostic requires no deletion listeners");
    f.step(0.0f);
    f.step(0.0f);
    require(f.contactPairs() == 12, "lifetime checkpoint must have twelve pairs");
    require(f.physics->getNbShapes() == 24,
            "lifetime checkpoint must have twenty-four shapes");

    const PxU32 victimIndex = 11;
    const PxReal victimX = PxReal(victimIndex) * 3.0f;
    const PxReal victimZ = 0.9f;
    PxRigidStatic* const checkpointActor = f.statics[victimIndex];
    PxShape* const checkpointShape = onlyShape(*checkpointActor);
    require(staticRayHit(*f.scene, victimX, victimZ) == checkpointShape,
            "checkpoint static query must hit victim shape");
    require(staticRayHit(*f.scene, victimX, 6.0f) == nullptr,
            "checkpoint static query must miss replacement position");
    const auto checkpoint = f.allocator.capture();

    const PxActorTypeFlags staticFlags = PxActorTypeFlag::eRIGID_STATIC;
    require(f.scene->getNbActors(staticFlags) == 12,
            "checkpoint static actor inventory");
    std::vector<PxActor*> checkpointActors(12);
    require(f.scene->getActors(staticFlags, checkpointActors.data(), 12) == 12,
            "capture checkpoint static actor order");

    struct Observation
    {
        oc2::offline::ArenaSnapshotAllocator::Image arena;
        std::vector<PxU32> events;
        PxU32 pairs = 0;
    };
    const PxReal suffixPoses[] = {-0.2f, 0.0f, -0.2f, 0.0f, -0.2f};
    std::vector<Observation> expected;
    for (unsigned step = 0; step < 5; ++step)
    {
        f.step(suffixPoses[step]);
        require(staticRayHit(*f.scene, victimX, victimZ) == checkpointShape,
                "reference suffix static query");
        Observation observed;
        observed.arena = f.allocator.capture();
        observed.events = f.events.words;
        observed.pairs = f.contactPairs();
        require(observed.pairs == (step % 2 ? 12u : 8u),
                "reference suffix pair count");
        expected.push_back(std::move(observed));
    }

    for (unsigned iteration = 0; iteration < 100; ++iteration)
    {
        std::string error;
        if (!f.allocator.restore(checkpoint, error))
            require(false, "pre-perturbation arena restore: " + error);
        f.statics[victimIndex] = checkpointActor;
        f.events.words.clear();
        if (!checkpoint.equals(f.allocator.capture(), error))
            require(false, "pre-perturbation checkpoint differs: " + error);

        // Releasing this actor also destroys its attached shape: the fixture
        // relinquished the user shape reference during construction.
        checkpointActor->release();
        f.statics[victimIndex] = nullptr;
        require(f.scene->getNbActors(staticFlags) == 11 &&
                f.physics->getNbShapes() == 23,
                "victim actor and attached shape were not both released");
        PxRigidStatic* replacement = f.physics->createRigidStatic(
            PxTransform(PxVec3(victimX, 0.0f, 6.0f)));
        require(replacement != nullptr, "replacement static actor creation");
        PxShape* replacementShape = f.physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *f.material);
        require(replacementShape != nullptr, "replacement static shape creation");
        replacement->attachShape(*replacementShape);
        replacementShape->release();
        replacement->userData = reinterpret_cast<void*>(
            std::uintptr_t(victimIndex + 1));
        f.scene->addActor(*replacement);
        f.statics[victimIndex] = replacement;
        require(f.scene->getNbActors(staticFlags) == 12 &&
                f.physics->getNbShapes() == 24,
                "replacement actor and shape inventory differs");
        f.step(0.0f);
        require(f.contactPairs() == 11,
                "replacement successor must have eleven contacts");
        require(staticRayHit(*f.scene, victimX, victimZ) == nullptr &&
                staticRayHit(*f.scene, victimX, 6.0f) == replacementShape,
                "replacement static query differs");
        require(f.errors.count == 0, "replacement successor PhysX error");

        // The replacement no longer exists in the restored graph. Never
        // release it after this point; its address may alias checkpointActor.
        if (!f.allocator.restore(checkpoint, error))
            require(false, "lifetime arena restore: " + error);
        f.statics[victimIndex] = checkpointActor;
        f.events.words.clear();
        if (!checkpoint.equals(f.allocator.capture(), error))
            require(false, "lifetime checkpoint bytes or ledger differ: " +
                    error);
        require(f.scene->getNbActors(staticFlags) == 12 &&
                f.physics->getNbShapes() == 24,
                "restored actor or shape count differs");
        std::vector<PxActor*> restoredActors(12);
        require(f.scene->getActors(staticFlags, restoredActors.data(), 12) == 12 &&
                restoredActors == checkpointActors,
                "restored static actor order differs");
        require(onlyShape(*checkpointActor) == checkpointShape,
                "restored static shape identity differs");
        require(staticRayHit(*f.scene, victimX, victimZ) == checkpointShape &&
                staticRayHit(*f.scene, victimX, 6.0f) == nullptr,
                "restored static query differs");
        if (!checkpoint.equals(f.allocator.capture(), error))
            require(false, "restored query changed checkpoint arena: " + error);

        for (unsigned step = 0; step < 5; ++step)
        {
            f.step(suffixPoses[step]);
            require(staticRayHit(*f.scene, victimX, victimZ) == checkpointShape,
                    "replayed suffix static query differs");
            require(f.contactPairs() == expected[step].pairs,
                    "replayed suffix contact count differs");
            require(f.events.words == expected[step].events,
                    "replayed suffix callbacks differ");
            const auto replayed = f.allocator.capture();
            if (!sameInitializedArena(expected[step].arena, replayed, error))
                require(false, "replayed lifetime suffix arena differs at step " +
                        std::to_string(step) + ": " + error);
        }
    }
    std::cout << "PASS source-built static actor/shape lifetime arena replay x100 "
                 "(single fixture, inline dispatcher only)\n";
    return 0;
}

} // namespace

int main(int argc, char** argv)
{
    if (argc == 2 && std::string(argv[1]) == "--lifetime")
        return runLifetimeDiagnostic();
    const bool mixed = argc == 2 && std::string(argv[1]) == "--mixed";
    require(argc == 1 || mixed,
            "usage: physx333_arena_snapshot [--mixed|--lifetime]");
    Fixture f(mixed);
    f.step(0.0f);
    f.step(0.0f);
    require(f.contactPairs() == 12, "checkpoint must have twelve pairs");
    const auto checkpoint = f.allocator.capture();
    const PxReal suffixPoses[] = {-0.2f, 0.0f, -0.2f, 0.0f, -0.2f};
    struct Observation
    {
        oc2::offline::ArenaSnapshotAllocator::Image arena;
        std::vector<PxU32> events;
        PxU32 pairs;
    };
    std::vector<Observation> expected;
    for (unsigned step = 0; step < 5; ++step)
    {
        f.step(suffixPoses[step]);
        Observation observed;
        observed.arena = f.allocator.capture();
        observed.events = f.events.words;
        observed.pairs = f.contactPairs();
        require(observed.pairs == (step % 2 ? 12u : 8u),
                "reference suffix pair count");
        expected.push_back(std::move(observed));
    }
    {
        auto invalid = checkpoint;
        ++invalid.owner;
        std::string error;
        require(!f.allocator.restore(invalid, error),
                "foreign arena image must reject");
        require(expected.back().arena.equals(f.allocator.capture(), error),
                "foreign image rejection changed arena: " + error);
        invalid = checkpoint;
        invalid.blocks.front().offset = 1;
        require(!f.allocator.restore(invalid, error),
                "misaligned arena block must reject");
        require(expected.back().arena.equals(f.allocator.capture(), error),
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
        for (unsigned step = 0; step < 5; ++step)
        {
            f.step(suffixPoses[step]);
            require(f.contactPairs() == expected[step].pairs,
                    "replay suffix pair count");
            require(f.events.words == expected[step].events,
                    "replay suffix callbacks differ");
            const auto replayed = f.allocator.capture();
            if (!sameInitializedArena(expected[step].arena, replayed, error))
                require(false, "replayed suffix arena differs at step " +
                        std::to_string(step) + ": " + error);
        }
    }
    std::cout << "PASS source-built 12-to-8 " <<
        (mixed ? "mixed " : "all-touch ") <<
        "raw-allocation five-step replay x100\n";
}
