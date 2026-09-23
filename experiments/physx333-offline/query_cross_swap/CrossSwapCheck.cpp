#include "CrossSwapImage.h"

#include <cstdlib>
#include <cstring>
#include <iostream>
#include <mutex>
#include <string>
#include <unordered_map>

#include "PxPhysicsAPI.h"

using namespace physx;
using oc2::offline::CaptureQueryImage;
using oc2::offline::QueryImage;
using oc2::offline::RestoreQueryImage;
using oc2::offline::EqualsCrossSwapRebased;
using oc2::offline::RestoreAcrossOneQuerySwap;

namespace {

void require(bool condition, const std::string& message)
{
    if (!condition)
    {
        std::cerr << "FAIL " << message << '\n';
        std::exit(1);
    }
}

struct CountingAllocator : PxAllocatorCallback
{
    PxDefaultAllocator backing;
    mutable std::mutex lock;
    std::unordered_map<void*, std::size_t> live;
    std::size_t liveBytes = 0;

    void* allocate(std::size_t size, const char* typeName,
                   const char* filename, int line) override
    {
        void* result = backing.allocate(size, typeName, filename, line);
        if (result)
        {
            std::lock_guard<std::mutex> guard(lock);
            live[result] = size;
            liveBytes += size;
        }
        return result;
    }

    void deallocate(void* pointer) override
    {
        if (pointer)
        {
            std::lock_guard<std::mutex> guard(lock);
            const auto found = live.find(pointer);
            require(found != live.end(), "allocator freed an unowned block");
            liveBytes -= found->second;
            live.erase(found);
        }
        backing.deallocate(pointer);
    }

    std::size_t count() const
    {
        std::lock_guard<std::mutex> guard(lock);
        return live.size();
    }

    std::size_t bytes() const
    {
        std::lock_guard<std::mutex> guard(lock);
        return liveBytes;
    }
};

struct Errors : PxErrorCallback
{
    void reportError(PxErrorCode::Enum, const char* message,
                     const char*, int) override
    {
        std::cerr << "PhysX: " << message << '\n';
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

PxFilterFlags filter(PxFilterObjectAttributes, PxFilterData,
                     PxFilterObjectAttributes, PxFilterData,
                     PxPairFlags& flags, const void*, PxU32)
{
    flags = PxPairFlag::eCONTACT_DEFAULT;
    return PxFilterFlag::eDEFAULT;
}

const QueryImage::Field* field(const QueryImage& image, const char* name)
{
    for (const QueryImage::Field& item : image.fields)
        if (item.name == name) return &item;
    return nullptr;
}

PxU32 word(const QueryImage& image, const char* name)
{
    const QueryImage::Field* item = field(image, name);
    require(item && item->bytes.size() == sizeof(PxU32),
            std::string("missing image field ") + name);
    PxU32 result = 0;
    std::memcpy(&result, item->bytes.data(), sizeof(result));
    return result;
}

bool postSwap(const QueryImage& image)
{
    return word(image, "dynamic.progress") == 0 &&
           word(image, "dynamic.cachedBoxCount") == 32 &&
           word(image, "dynamic.cachedBoxes") == 0 &&
           word(image, "dynamic.newTree") == 0;
}

struct Fixture
{
    CountingAllocator allocator;
    Errors errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;
    PxScene* scene = nullptr;
    PxRigidStatic* fixed = nullptr;
    PxRigidDynamic* mover = nullptr;
    PxShape* firstShape = nullptr;

    Fixture()
    {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        require(foundation != nullptr, "foundation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        require(physics != nullptr, "physics");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        require(material != nullptr, "material");
        PxSceneDesc desc(physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &dispatcher;
        desc.filterShader = filter;
        desc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
        desc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
        scene = physics->createScene(desc);
        require(scene != nullptr, "scene");

        fixed = physics->createRigidStatic(
            PxTransform(PxVec3(-10.0f, 0.0f, 0.0f)));
        PxShape* staticShape = physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
        require(fixed && staticShape, "static actor/shape");
        fixed->attachShape(*staticShape);
        staticShape->release();
        scene->addActor(*fixed);

        mover = physics->createRigidDynamic(
            PxTransform(PxVec3(3.0f, 0.0f, 0.0f)));
        require(mover != nullptr, "dynamic actor");
        for (PxU32 i = 0; i < 32; ++i)
        {
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.2f, 0.2f, 0.2f), *material);
            require(shape != nullptr, "dynamic shape");
            if (i) shape->setLocalPose(PxTransform(
                PxVec3(0.0f, 0.0f, 10.0f + PxReal(i))));
            mover->attachShape(*shape);
            if (!i) firstShape = shape;
            shape->release();
        }
        mover->setMass(1.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(1.0f));
        scene->addActor(*mover);
        mover->putToSleep();

        step();
        scene->forceDynamicTreeRebuild(false, true);
        PxRaycastBuffer warm;
        scene->raycast(PxVec3(1.0f, 0.0f, 0.0f),
                       PxVec3(1.0f, 0.0f, 0.0f), 3.0f, warm);
        mover->setGlobalPose(PxTransform(PxVec3(3.5f, 0.0f, 0.0f)));
        scene->raycast(PxVec3(1.0f, 0.0f, 0.0f),
                       PxVec3(1.0f, 0.0f, 0.0f), 4.0f, warm);
        mover->setGlobalPose(PxTransform(PxVec3(7.0f, 0.0f, 0.0f)));
        mover->putToSleep();
    }

    ~Fixture()
    {
        mover->release();
        fixed->release();
        scene->release();
        material->release();
        physics->release();
        foundation->release();
    }

    void step()
    {
        scene->simulate(1.0f / 60.0f);
        require(scene->fetchResults(true), "fetchResults");
    }

    void queryAtSeven()
    {
        PxRaycastBuffer ray;
        scene->raycast(PxVec3(5.0f, 0.0f, 0.0f),
                       PxVec3(1.0f, 0.0f, 0.0f), 4.0f, ray);
        require(ray.hasBlock && ray.block.shape == firstShape,
                "raycast at rebuilt shape");
        PxOverlapBuffer overlap;
        scene->overlap(PxSphereGeometry(0.2f),
                       PxTransform(PxVec3(7.0f, 0.0f, 0.0f)), overlap);
        bool found = overlap.hasBlock && overlap.block.shape == firstShape;
        for (PxU32 i = 0; i < overlap.nbTouches; ++i)
            found = found || overlap.touches[i].shape == firstShape;
        require(found, "overlap at rebuilt shape");
    }
};

void capture(Fixture& fixture, QueryImage& image, const char* phase)
{
    std::string error;
    if (!CaptureQueryImage(*fixture.scene, image, error))
        require(false, std::string(phase) + " capture: " + error);
}

} // namespace

int main()
{
    Fixture fixture;
    fixture.step();
    QueryImage referenceA;
    capture(fixture, referenceA, "BUILD_INIT");
    require(word(referenceA, "dynamic.progress") == 1 &&
            word(referenceA, "dynamic.newTree") != 0 &&
            word(referenceA, "dynamic.cachedBoxes") != 0 &&
            word(referenceA, "dynamic.newTreeStorage.nodesPointer") == 0,
            "fixture did not reach cold BUILD_INIT");
    fixture.queryAtSeven();
    QueryImage afterAQuery;
    capture(fixture, afterAQuery, "post-query BUILD_INIT");
    std::string queryError;
    require(referenceA.equals(afterAQuery, queryError),
            "BUILD_INIT query changed checkpoint state: " + queryError);
    const std::size_t liveA = fixture.allocator.count();
    const std::size_t bytesA = fixture.allocator.bytes();

    QueryImage referenceB;
    bool swapped = false;
    for (int i = 0; i < 512; ++i)
    {
        fixture.step();
        capture(fixture, referenceB, "first swap");
        if (postSwap(referenceB))
        {
            swapped = true;
            break;
        }
    }
    require(swapped, "first tree did not swap");
    fixture.queryAtSeven();
    QueryImage afterBQuery;
    capture(fixture, afterBQuery, "post-query swap");
    require(referenceB.equals(afterBQuery, queryError),
            "post-swap query changed checkpoint state: " + queryError);
    const std::size_t liveB = fixture.allocator.count();
    const std::size_t bytesB = fixture.allocator.bytes();

    std::string error;
    require(!RestoreQueryImage(*fixture.scene, referenceA, error),
            "old fixed-address restore crossed the swap");
    QueryImage afterReject;
    capture(fixture, afterReject, "negative control");
    require(referenceB.equals(afterReject, error),
            "negative control changed live query state: " + error);
    require(fixture.allocator.count() == liveB &&
            fixture.allocator.bytes() == bytesB,
            "negative control changed allocator ownership");

    require(!RestoreAcrossOneQuerySwap(*fixture.scene, referenceB, error),
            "cross-swap restore accepted a post-swap checkpoint");
    QueryImage afterWrongPhase;
    capture(fixture, afterWrongPhase, "wrong-phase negative control");
    require(referenceB.equals(afterWrongPhase, error),
            "wrong-phase negative control changed live state: " + error);

    require(!RestoreAcrossOneQuerySwap(*fixture.scene, referenceA,
                                       error, true) &&
            error.find("test-injected verification failure") !=
                std::string::npos,
            "forced cross-swap rollback did not execute");
    QueryImage afterRollback;
    capture(fixture, afterRollback, "forced rollback");
    require(referenceB.equals(afterRollback, error),
            "forced rollback changed live query state: " + error);
    require(fixture.allocator.count() == liveB &&
            fixture.allocator.bytes() == bytesB,
            "forced rollback leaked staged allocations");

    QueryImage currentA = referenceA;
    for (int cycle = 0; cycle < 20; ++cycle)
    {
        if (!RestoreAcrossOneQuerySwap(*fixture.scene, currentA, error))
            require(false, "cross-swap restore: " + error);
        QueryImage restoredA;
        capture(fixture, restoredA, "restored BUILD_INIT");
        if (!EqualsCrossSwapRebased(referenceA, restoredA, error))
            require(false, "rebased BUILD_INIT parity: " + error);
        require(fixture.allocator.count() == liveA &&
                fixture.allocator.bytes() == bytesA,
                "restored BUILD_INIT allocator leak");
        fixture.queryAtSeven();
        currentA = restoredA;

        QueryImage replayB;
        swapped = false;
        for (int step = 0; step < 512; ++step)
        {
            fixture.step();
            capture(fixture, replayB, "replayed swap");
            if (postSwap(replayB))
            {
                swapped = true;
                break;
            }
        }
        require(swapped, "replayed tree did not swap");
        if (!EqualsCrossSwapRebased(referenceB, replayB, error))
            require(false, "rebased post-swap parity: " + error);
        fixture.queryAtSeven();
        require(fixture.allocator.count() == liveB &&
                fixture.allocator.bytes() == bytesB,
                "replayed post-swap allocator leak");
    }
    PxSceneDesc foreignDesc(fixture.physics->getTolerancesScale());
    foreignDesc.gravity = PxVec3(0.0f);
    foreignDesc.cpuDispatcher = &fixture.dispatcher;
    foreignDesc.filterShader = filter;
    foreignDesc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
    foreignDesc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
    PxScene* foreignScene = fixture.physics->createScene(foreignDesc);
    require(foreignScene != nullptr, "foreign scene");
    const std::size_t foreignLive = fixture.allocator.count();
    const std::size_t foreignBytes = fixture.allocator.bytes();
    require(!RestoreAcrossOneQuerySwap(*foreignScene, referenceA, error),
            "cross-swap restore accepted a different scene");
    require(fixture.allocator.count() == foreignLive &&
            fixture.allocator.bytes() == foreignBytes,
            "foreign-scene rejection changed allocator ownership");
    foreignScene->release();
    std::cout << "PASS offline BUILD_INIT cross-swap query rewind: "
                 "20 rehydrations, query replays and allocator checks\n";
    return 0;
}
