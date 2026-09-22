// Shared ActorPair ownership in a source-built PhysX 3.3.3 scene.
// The observer is read only; this fixture changes only public SDK objects.
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "ActorPairGraphImage.h"

using namespace physx;

namespace {

const PxReal kStep = 1.0f / 60.0f;

void fail(const std::string& reason)
{
    std::cerr << "FAIL: " << reason << '\n';
    std::exit(1);
}

struct Errors : PxErrorCallback {
    virtual void reportError(PxErrorCode::Enum code, const char* message,
                             const char* file, int line) {
        std::cerr << "PhysX " << static_cast<int>(code) << ": " << message
                  << " (" << file << ':' << line << ")\n";
        if (code == PxErrorCode::eINVALID_PARAMETER ||
            code == PxErrorCode::eINVALID_OPERATION ||
            code == PxErrorCode::eOUT_OF_MEMORY)
            fail("PhysX rejected the fixture");
    }
};

struct Dispatcher : PxCpuDispatcher {
    virtual void submitTask(PxBaseTask& task) {
        task.run(); task.release();
    }
    virtual PxU32 getWorkerCount() const { return 0; }
};

struct Events : PxSimulationEventCallback {
    PxU32 reportedShapePairs = 0;
    virtual void onConstraintBreak(PxConstraintInfo*, PxU32) {}
    virtual void onWake(PxActor**, PxU32) {}
    virtual void onSleep(PxActor**, PxU32) {}
    virtual void onTrigger(PxTriggerPair*, PxU32) {}
    virtual void onContact(const PxContactPairHeader&,
                           const PxContactPair*, PxU32 count) {
        reportedShapePairs += count;
    }
};

PxFilterFlags filter(PxFilterObjectAttributes, PxFilterData,
                     PxFilterObjectAttributes, PxFilterData,
                     PxPairFlags& flags, const void*, PxU32)
{
    flags = PxPairFlag::eCONTACT_DEFAULT |
            PxPairFlag::eNOTIFY_TOUCH_FOUND |
            PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
            PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct Runtime {
    PxDefaultAllocator allocator;
    Errors errors;
    Dispatcher dispatcher;
    PxFoundation* foundation = NULL;
    PxPhysics* physics = NULL;
    PxMaterial* material = NULL;
    Runtime() {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        if (!foundation) fail("PxCreateFoundation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        if (!physics) fail("PxCreatePhysics");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        if (!material) fail("createMaterial");
    }
    ~Runtime() {
        material->release(); physics->release(); foundation->release();
    }
};

struct World {
    Runtime& rt;
    Events events;
    PxScene* scene = NULL;
    PxRigidStatic* fixed = NULL;
    PxRigidDynamic* moving = NULL;
    PxShape* movingShape[2] = {NULL, NULL};
    bool attached[2] = {true, true};

    explicit World(Runtime& runtime) : rt(runtime) {
        PxSceneDesc desc(rt.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &rt.dispatcher;
        desc.filterShader = filter;
        desc.simulationEventCallback = &events;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        desc.flags |= PxSceneFlag::eENABLE_PCM;
        scene = rt.physics->createScene(desc);
        if (!scene) fail("createScene");

        fixed = rt.physics->createRigidStatic(
            PxTransform(PxVec3(0.0f, 0.0f, 0.0f)));
        if (!fixed) fail("createRigidStatic");
        PxShape* floor = rt.physics->createShape(
            PxBoxGeometry(2.0f, 0.4f, 1.0f), *rt.material);
        if (!floor) fail("create fixed shape");
        fixed->attachShape(*floor);
        floor->release();
        fixed->userData = reinterpret_cast<void*>(std::uintptr_t(1));
        scene->addActor(*fixed);

        moving = rt.physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f, 0.5f, 0.0f)));
        if (!moving) fail("createRigidDynamic");
        for (PxU32 i = 0; i < 2; ++i) {
            movingShape[i] = rt.physics->createShape(
                PxBoxGeometry(0.3f, 0.3f, 0.3f), *rt.material);
            if (!movingShape[i]) fail("create moving shape");
            movingShape[i]->setLocalPose(PxTransform(
                PxVec3(i ? 0.5f : -0.5f, 0.0f, 0.0f)));
            moving->attachShape(*movingShape[i]);
        }
        moving->setMass(1.0f);
        moving->setMassSpaceInertiaTensor(PxVec3(1.0f));
        moving->setLinearDamping(0.0f);
        moving->setAngularDamping(0.0f);
        moving->userData = reinterpret_cast<void*>(std::uintptr_t(2));
        scene->addActor(*moving);
    }
    ~World() {
        moving->release();
        fixed->release();
        for (PxShape* shape : movingShape) shape->release();
        scene->release();
    }
    void detach(PxU32 index) {
        if (index >= 2 || !attached[index]) fail("invalid detach request");
        moving->detachShape(*movingShape[index]);
        attached[index] = false;
    }
    physx333_offline::ActorPairGraphImage step() {
        events.reportedShapePairs = 0;
        moving->setGlobalPose(PxTransform(PxVec3(0.0f, 0.5f, 0.0f)));
        moving->setLinearVelocity(PxVec3(0.0f));
        moving->setAngularVelocity(PxVec3(0.0f));
        scene->simulate(kStep);
        if (!scene->fetchResults(true)) fail("fetchResults");
        physx333_offline::ActorPairGraphImage image;
        std::string error;
        if (!physx333_offline::CaptureActorPairGraph(*scene, image, error))
            fail("CaptureActorPairGraph: " + error);
        return image;
    }
};

void check(const physx333_offline::ActorPairGraphImage& image,
           std::size_t expectedOwners)
{
    if (image.sips.size() != expectedOwners ||
        image.actorPairs.size() != (expectedOwners ? 1u : 0u) ||
        image.actorPairPool.usedCount != (expectedOwners ? 1u : 0u) ||
        image.reportDataPool.usedCount != (expectedOwners ? 1u : 0u))
        fail("ActorPair/SIP/report pool cardinality differs");
    if (expectedOwners) {
        const physx333_offline::ActorGraphPairRow& pair = image.actorPairs[0];
        if (pair.key.first != 1 || pair.key.second != 2 ||
            pair.sipOwners != expectedOwners ||
            pair.touchingOwners != expectedOwners ||
            pair.refCount != expectedOwners ||
            pair.touchCount != expectedOwners || !pair.hasReportData ||
            pair.reportActorA != pair.actorA ||
            pair.reportActorB != pair.actorB)
            fail("shared ActorPair ownership or report identity differs");
        for (const auto& sip : image.sips)
            if (!(sip.actorKey == pair.key) ||
                sip.actorPairSlot != pair.poolSlot || !sip.hasTouch)
                fail("SIP does not share the expected touching ActorPair");
    }
}

std::vector<physx333_offline::ActorPairGraphImage> run(Runtime& rt)
{
    World world(rt);
    std::vector<physx333_offline::ActorPairGraphImage> images;
    images.push_back(world.step());
    check(images.back(), 2);
    if (world.events.reportedShapePairs != 2)
        fail("initial contact report did not include two shape pairs");

    world.detach(1);
    images.push_back(world.step());
    check(images.back(), 1);

    world.detach(0);
    images.push_back(world.step());
    check(images.back(), 0);
    return images;
}

} // namespace

int main()
{
    Runtime rt;
    const auto first = run(rt);
    const auto fresh = run(rt);
    if (first != fresh)
        fail("independent fresh-scene ActorPair graph images differ");
    std::cout << "PASS: shared ActorPair 2 -> 1 -> 0 SIP owners, "
                 "reports, pool order, fresh-scene equality\n";
    return 0;
}
