#include "BodyImage.h"

#include <algorithm>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"

using namespace physx;
using oc2::offline::BodyImage;
using oc2::offline::CaptureBodies;
using oc2::offline::RestoreBodies;

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
    pairFlags = PxPairFlag::eCONTACT_DEFAULT;
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

    Fixture()
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
            shape->setLocalPose(PxTransform(PxVec3(PxReal(i) * 3.0f, 0.0f, 0.0f)));
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

void checkRoundTrip(Fixture& fixture, const char* title, bool expectForcePayload)
{
    std::string error;
    fixture.step(false);
    BodyImage warm;
    const bool capturedWarm = CaptureBodies(*fixture.scene, warm, error);
    require(capturedWarm, std::string(title) + " warm capture: " + error);
    require(warm.actors.size() == 7 &&
            (warm.actors.back().simStateData != 0) == expectForcePayload,
            std::string(title) + " unexpected SimStateData binding");
    BodyImage duplicate;
    require(CaptureBodies(*fixture.scene, duplicate, error),
            std::string(title) + " duplicate capture: " + error);
    require(warm.equals(duplicate, error),
            std::string(title) + " duplicate equality: " + error);

    fixture.step(true);
    BodyImage away;
    require(CaptureBodies(*fixture.scene, away, error),
            std::string(title) + " away capture: " + error);
    require(!warm.equals(away, error),
            std::string(title) + " fixture body state did not change");

    for (int i = 0; i < 100; ++i)
    {
        require(RestoreBodies(*fixture.scene, warm, error),
                std::string(title) + " restore warm: " + error);
        BodyImage observed;
        require(CaptureBodies(*fixture.scene, observed, error) &&
                warm.equals(observed, error),
                std::string(title) + " warm verification: " + error);
        require(RestoreBodies(*fixture.scene, away, error),
                std::string(title) + " restore away: " + error);
        require(CaptureBodies(*fixture.scene, observed, error) &&
                away.equals(observed, error),
                std::string(title) + " away verification: " + error);
    }

    BodyImage corrupt = warm;
    require(!corrupt.fields.empty(), "body fields absent");
    corrupt.fields[0].address += 4;
    BodyImage before;
    require(CaptureBodies(*fixture.scene, before, error), "before corrupt capture");
    require(!RestoreBodies(*fixture.scene, corrupt, error),
            "corrupt body image was accepted");
    BodyImage after;
    require(CaptureBodies(*fixture.scene, after, error) &&
            before.equals(after, error),
            "corrupt body image changed the scene");

    BodyImage corruptPayload = warm;
    bool changedPayload = false;
    for (BodyImage::Field& field : corruptPayload.fields)
        if (field.name == "actor[7].BodyCore.lowLevelCore")
        {
            std::fill(field.bytes.begin(), field.bytes.end(),
                      static_cast<unsigned char>(0xff));
            changedPayload = true;
        }
    require(changedPayload, "dynamic core field absent");
    require(!RestoreBodies(*fixture.scene, corruptPayload, error),
            "invalid dynamic core payload was accepted");
    require(CaptureBodies(*fixture.scene, after, error) &&
            before.equals(after, error),
            "invalid dynamic core payload changed the scene");
    std::cout << "PASS " << title << " duplicate, 100 A-B-A round trips, atomic rejection\n";
}

} // namespace

int main()
{
    {
        Fixture fixture;
        checkRoundTrip(fixture, "six-box body", false);
    }
    {
        Fixture fixture;
        // Force allocation of persistent SimStateData before the checkpoint.
        fixture.mover->addForce(PxVec3(3.0f, 0.0f, 0.0f), PxForceMode::eFORCE);
        checkRoundTrip(fixture, "six-box force payload", true);
    }
    return 0;
}
