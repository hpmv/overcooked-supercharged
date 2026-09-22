#include "SceneClockImage.h"

#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "NpScene.h"
#include "ScObjectIDTracker.h"

using namespace physx;
using oc2::offline::SceneClockImage;
using oc2::offline::CaptureSceneClock;
using oc2::offline::RestoreSceneClock;

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

PxFilterFlags filter(PxFilterObjectAttributes, PxFilterData,
                     PxFilterObjectAttributes, PxFilterData,
                     PxPairFlags& pairFlags, const void*, PxU32)
{
    pairFlags = PxPairFlag::eCONTACT_DEFAULT |
                PxPairFlag::eNOTIFY_TOUCH_FOUND |
                PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
                PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct ContactEvents : PxSimulationEventCallback
{
    virtual void onConstraintBreak(PxConstraintInfo*, PxU32) {}
    virtual void onWake(PxActor**, PxU32) {}
    virtual void onSleep(PxActor**, PxU32) {}
    virtual void onTrigger(PxTriggerPair*, PxU32) {}
    virtual void onContact(const PxContactPairHeader&,
                           const PxContactPair*, PxU32) {}
};

struct Fixture
{
    PxDefaultAllocator allocator;
    Errors errors;
    InlineDispatcher dispatcher;
    ContactEvents events;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;
    PxScene* scene = nullptr;
    PxRigidDynamic* mover = nullptr;
    std::vector<PxRigidActor*> actors;

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
        desc.simulationEventCallback = &events;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        scene = physics->createScene(desc);
        require(scene != nullptr, "scene");
    }

    ~Fixture()
    {
        for (PxRigidActor* actor : actors) actor->release();
        scene->release();
        material->release();
        physics->release();
        foundation->release();
    }

    void step(PxReal dt)
    {
        scene->simulate(dt);
        require(scene->fetchResults(true), "fetchResults");
    }

    void makeSixContacts()
    {
        for (PxU32 i = 0; i < 6; ++i)
        {
            PxRigidStatic* fixed = physics->createRigidStatic(
                PxTransform(PxVec3(PxReal(i) * 3.0f, 0.0f, 0.0f)));
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(fixed && shape, "static actor/shape");
            fixed->attachShape(*shape);
            shape->release();
            scene->addActor(*fixed);
            actors.push_back(fixed);
        }
        mover = physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f, 0.95f, 0.0f)));
        require(mover != nullptr, "mover");
        for (PxU32 i = 0; i < 6; ++i)
        {
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(shape != nullptr, "mover shape");
            mover->attachShape(*shape);
            shape->setLocalPose(PxTransform(
                PxVec3(PxReal(i) * 3.0f, 0.0f, 0.0f)));
            shape->release();
        }
        mover->setMass(6.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(18.0f));
        scene->addActor(*mover);
        actors.push_back(mover);
    }

    void stepPose(bool away)
    {
        mover->setGlobalPose(PxTransform(PxVec3(0.0f, 0.95f,
                                                away ? 5.0f : 0.0f)));
        step(1.0f / 60.0f);
    }
};

PxU32 primeFreeStack(Sc::ObjectIDTracker& tracker)
{
    const PxU32 zero = tracker.createID();
    const PxU32 one = tracker.createID();
    const PxU32 two = tracker.createID();
    require(one == zero + 1 && two == zero + 2,
            "fresh tracker IDs are sequential");
    tracker.releaseID(zero);
    tracker.processPendingReleases();
    tracker.clearDeletedIDMap();
    return zero;
}

void check()
{
    Fixture fixture;
    fixture.step(1.0f / 60.0f);
    Sc::Scene& sc = static_cast<NpScene&>(*fixture.scene)
                        .getScene().getScScene();
    const PxU32 recycledShape = primeFreeStack(sc.getShapeIDTracker());
    const PxU32 recycledRigid = primeFreeStack(sc.getRigidIDTracker());
    fixture.scene->setGravity(PxVec3(0.0f, -9.81f, 0.0f));

    SceneClockImage a;
    std::string error;
    require(CaptureSceneClock(*fixture.scene, a, error),
            "A capture: " + error);
    SceneClockImage duplicate;
    require(CaptureSceneClock(*fixture.scene, duplicate, error) &&
            a.equals(duplicate, error), "duplicate A: " + error);
    require(a.shapeIds.freeIds.size() == 1 &&
            a.rigidIds.freeIds.size() == 1,
            "primed ID free stacks");

    fixture.step(1.0f / 30.0f);
    require(sc.getShapeIDTracker().createID() == recycledShape,
            "shape ID free-stack pop");
    require(sc.getRigidIDTracker().createID() == recycledRigid,
            "rigid ID free-stack pop");
    SceneClockImage b;
    require(CaptureSceneClock(*fixture.scene, b, error),
            "B capture: " + error);
    require(!a.equals(b, error), "clock fixture did not change");

    for (int i = 0; i < 100; ++i)
    {
        require(RestoreSceneClock(*fixture.scene, a, error),
                "restore A: " + error);
        SceneClockImage observed;
        require(CaptureSceneClock(*fixture.scene, observed, error) &&
                a.equals(observed, error), "A image parity: " + error);
        require(RestoreSceneClock(*fixture.scene, b, error),
                "restore B: " + error);
        require(CaptureSceneClock(*fixture.scene, observed, error) &&
                b.equals(observed, error), "B image parity: " + error);
    }

    SceneClockImage corrupt = a;
    corrupt.shapeIds.freeIds[0] = corrupt.shapeIds.currentId + 1;
    SceneClockImage before;
    require(CaptureSceneClock(*fixture.scene, before, error),
            "before corrupt image");
    require(!RestoreSceneClock(*fixture.scene, corrupt, error),
            "invalid free-ID image accepted");
    SceneClockImage after;
    require(CaptureSceneClock(*fixture.scene, after, error) &&
            before.equals(after, error),
            "rejected free-ID image mutated scene");

    corrupt = a;
    corrupt.fields[0].address += 4;
    require(!RestoreSceneClock(*fixture.scene, corrupt, error),
            "invalid field address accepted");
    require(CaptureSceneClock(*fixture.scene, after, error) &&
            before.equals(after, error),
            "rejected field address mutated scene");

    require(RestoreSceneClock(*fixture.scene, a, error),
            "final A restore: " + error);
    require(sc.getShapeIDTracker().createID() == recycledShape &&
            sc.getRigidIDTracker().createID() == recycledRigid,
            "restored LIFO ID allocation");
    std::cout << "PASS scene-clock source image: duplicate, 100 A/B round trips, "
                 "ID LIFO, atomic corruption rejection\n";
}

void checkSixContactBoundary()
{
    Fixture fixture;
    fixture.makeSixContacts();
    fixture.stepPose(false);
    fixture.stepPose(true);
    fixture.stepPose(false);
    SceneClockImage a;
    std::string error;
    require(CaptureSceneClock(*fixture.scene, a, error),
            "six-contact A capture: " + error);
    fixture.stepPose(true);
    SceneClockImage b;
    require(CaptureSceneClock(*fixture.scene, b, error),
            "six-contact B capture: " + error);
    for (int i = 0; i < 100; ++i)
    {
        require(RestoreSceneClock(*fixture.scene, a, error),
                "six-contact restore A: " + error);
        SceneClockImage observed;
        require(CaptureSceneClock(*fixture.scene, observed, error) &&
                a.equals(observed, error),
                "six-contact A parity: " + error);
        require(RestoreSceneClock(*fixture.scene, b, error),
                "six-contact restore B: " + error);
        require(CaptureSceneClock(*fixture.scene, observed, error) &&
                b.equals(observed, error),
                "six-contact B parity: " + error);
    }
    std::cout << "PASS six-contact scene-clock A/B component image x100\n";
}

} // namespace

int main()
{
    check();
    checkSixContactBoundary();
    return 0;
}
