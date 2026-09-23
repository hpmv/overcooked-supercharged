#include "ContextImage.h"

#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"

using namespace physx;
using oc2::offline::CaptureContextImage;
using oc2::offline::ContextImage;
using oc2::offline::RestoreContextImage;

namespace {

void require(bool condition, const std::string& message)
{
    if (!condition)
    {
        std::cerr << "FAIL " << message << '\n';
        std::exit(1);
    }
}

struct Errors : PxErrorCallback
{
    virtual void reportError(PxErrorCode::Enum, const char* message,
                             const char*, int)
    {
        std::cerr << "PhysX: " << message << '\n';
    }
};

struct InlineDispatcher : PxCpuDispatcher
{
    virtual void submitTask(PxBaseTask& task)
    {
        task.run();
        task.release();
    }
    virtual PxU32 getWorkerCount() const { return 0; }
};

PxFilterFlags contactFilter(PxFilterObjectAttributes, PxFilterData,
                            PxFilterObjectAttributes, PxFilterData,
                            PxPairFlags& pairFlags, const void*, PxU32)
{
    pairFlags = PxPairFlag::eCONTACT_DEFAULT |
                PxPairFlag::eNOTIFY_TOUCH_FOUND |
                PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct Fixture
{
    PxDefaultAllocator allocator;
    Errors errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;
    PxScene* scene = nullptr;
    PxRigidDynamic* mover = nullptr;
    std::vector<PxRigidActor*> actors;

    explicit Fixture(bool ccd = false)
    {
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
        desc.filterShader = contactFilter;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        if (ccd) desc.flags |= PxSceneFlag::eENABLE_CCD;
        scene = physics->createScene(desc);
        require(scene != nullptr, "scene creation");
        for (PxU32 i = 0; i < 6; ++i)
        {
            PxRigidStatic* fixed = physics->createRigidStatic(
                PxTransform(PxVec3(PxReal(i) * 3.0f, 0.0f, 0.0f)));
            require(fixed != nullptr, "static actor creation");
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(shape != nullptr, "static shape creation");
            fixed->attachShape(*shape);
            shape->release();
            fixed->userData = reinterpret_cast<void*>(static_cast<uintptr_t>(i + 1));
            scene->addActor(*fixed);
            actors.push_back(fixed);
        }
        mover = physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f, 0.95f, 0.0f)));
        require(mover != nullptr, "dynamic actor creation");
        for (PxU32 i = 0; i < 6; ++i)
        {
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(shape != nullptr, "dynamic shape creation");
            mover->attachShape(*shape);
            shape->setLocalPose(PxTransform(PxVec3(PxReal(i) * 3.0f)));
            shape->release();
        }
        mover->setMass(6.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(18.0f));
        mover->userData = reinterpret_cast<void*>(static_cast<uintptr_t>(7));
        scene->addActor(*mover);
        actors.push_back(mover);
    }

    ~Fixture()
    {
        for (PxRigidActor* actor : actors) actor->release();
        scene->release();
        material->release();
        physics->release();
        foundation->release();
    }

    void step(bool away)
    {
        mover->setGlobalPose(PxTransform(PxVec3(0.0f, 0.95f,
                                                away ? 5.0f : 0.0f)));
        scene->simulate(1.0f / 60.0f);
        require(scene->fetchResults(true), "fetchResults");
    }
};

} // namespace

int main()
{
    {
    Fixture fixture;
    fixture.step(false);
    std::string error;
    ContextImage warm;
    const bool warmCaptured = CaptureContextImage(*fixture.scene, warm, error);
    require(warmCaptured, "warm capture: " + error);
    require(warm.cachedThreadOrder.size() == 1,
            "inline six-box fixture should retain one cached thread context");
    require(warm.cachedThreads.size() == 1,
            "cached thread-context image missing");
    ContextImage duplicate;
    require(CaptureContextImage(*fixture.scene, duplicate, error),
            "duplicate capture: " + error);
    require(warm.equals(duplicate, error),
            "duplicate capture changed context: " + error);
    fixture.step(true);
    ContextImage away;
    require(CaptureContextImage(*fixture.scene, away, error),
            "away capture: " + error);
    require(warm.bitmaps[0].name == "thread[0].localChangeTouch" &&
            away.bitmaps[0].name == warm.bitmaps[0].name &&
            warm.bitmaps[0].wordCount == 1 &&
            away.bitmaps[0].wordCount == 8 &&
            warm.bitmaps[0].data != away.bitmaps[0].data,
            "fixture did not exercise cached bitmap rebasing");
    std::cout << "cached thread counters A="
              << warm.cachedThreads[0].compressedCacheSize << '/'
              << warm.cachedThreads[0].constraintSize << " B="
              << away.cachedThreads[0].compressedCacheSize << '/'
              << away.cachedThreads[0].constraintSize << '\n';
    require(!warm.equals(away, error),
            "six-box context did not change at all");

    for (int i = 0; i < 100; ++i)
    {
        const bool restoredWarm = RestoreContextImage(*fixture.scene, warm, error);
        require(restoredWarm, "restore warm: " + error);
        ContextImage observed;
        require(CaptureContextImage(*fixture.scene, observed, error) &&
                warm.equals(observed, error),
                "warm verification: " + error);
        const bool restoredAway = RestoreContextImage(*fixture.scene, away, error);
        require(restoredAway, "restore away: " + error);
        require(CaptureContextImage(*fixture.scene, observed, error) &&
                away.equals(observed, error),
                "away verification: " + error);
    }

    ContextImage corrupt = warm;
    corrupt.arrays[0].capacity++;
    ContextImage before, after;
    require(CaptureContextImage(*fixture.scene, before, error),
            "before corrupt capture: " + error);
    require(!RestoreContextImage(*fixture.scene, corrupt, error),
            "corrupt array capacity accepted");
    require(CaptureContextImage(*fixture.scene, after, error) &&
            before.equals(after, error),
            "corrupt image changed the context");
    corrupt = warm;
    require(!corrupt.arrays.empty(), "context fixture has no arrays");
    corrupt.arrays[0].data = UINTPTR_MAX;
    require(!RestoreContextImage(*fixture.scene, corrupt, error),
            "stale context array backing accepted");
    require(CaptureContextImage(*fixture.scene, after, error) &&
            before.equals(after, error),
            "stale array image changed the context");
    corrupt = warm;
    corrupt.thresholdTable.hashSize = corrupt.thresholdTable.hashCapacity + 1;
    require(!RestoreContextImage(*fixture.scene, corrupt, error),
            "corrupt threshold table accepted");
    require(CaptureContextImage(*fixture.scene, after, error) &&
            before.equals(after, error),
            "corrupt threshold table changed the context");
    corrupt = warm;
    bool changedBinding = false;
    for (ContextImage::Field& row : corrupt.fields)
        if (row.name == "dynamics.worldSolverBodyData")
        {
            require(row.bytes.size() >= 48, "world solver data too small");
            row.bytes[44] ^= 1;
            changedBinding = true;
        }
    require(changedBinding, "world solver data field missing");
    require(!RestoreContextImage(*fixture.scene, corrupt, error),
            "corrupt world solver binding accepted");
    require(CaptureContextImage(*fixture.scene, after, error) &&
            before.equals(after, error),
            "corrupt world solver binding changed the context");
    corrupt = warm;
    corrupt.cachedThreads[0].compressedCacheSize++;
    require(!RestoreContextImage(*fixture.scene, corrupt, error),
            "corrupt cached thread counter accepted");
    require(CaptureContextImage(*fixture.scene, after, error) &&
            before.equals(after, error),
            "corrupt cached thread counter changed the context");
    corrupt = warm;
    corrupt.cachedThreads[0].ignoredHeaderBytes[0] ^= 1;
    require(!RestoreContextImage(*fixture.scene, corrupt, error),
            "corrupt cached thread header mask accepted");
    require(CaptureContextImage(*fixture.scene, after, error) &&
            before.equals(after, error),
            "corrupt cached thread mask changed the context");
    corrupt = warm;
    corrupt.cachedThreadOrder[0] += 4;
    require(!RestoreContextImage(*fixture.scene, corrupt, error),
            "corrupt cached thread LIFO identity accepted");
    require(CaptureContextImage(*fixture.scene, after, error) &&
            before.equals(after, error),
            "corrupt cached thread LIFO identity changed the context");
    std::cout << "PASS context duplicate, 100 A-B-A component round trips, "
                 "atomic structural rejection\n";
    }
    {
        Fixture ccdFixture(true);
        ccdFixture.step(false);
        ContextImage unsupported;
        std::string error;
        require(!CaptureContextImage(*ccdFixture.scene, unsupported, error) &&
                error.find("CCD") != std::string::npos,
                "CCD feature gate did not reject capture");
    }
    std::cout << "PASS explicit CCD feature gate\n";
    return 0;
}
