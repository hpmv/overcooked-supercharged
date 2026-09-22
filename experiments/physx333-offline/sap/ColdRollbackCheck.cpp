// Source-built PhysX 3.3.3 regression for the first AABB deleted-overlap
// result allocation. No game binary or scene is used.
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <string>

#include "PxPhysicsAPI.h"
#include "SapImage.h"

using namespace physx;

namespace {

void require(bool condition, const std::string& message)
{
    if (!condition)
    {
        std::cerr << "FAIL: " << message << '\n';
        std::exit(1);
    }
}

struct Errors : PxErrorCallback
{
    unsigned count = 0;
    void reportError(PxErrorCode::Enum, const char* message,
                     const char*, int) override
    {
        ++count;
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

PxFilterFlags contactFilter(PxFilterObjectAttributes, PxFilterData,
                            PxFilterObjectAttributes, PxFilterData,
                            PxPairFlags& pairFlags, const void*, PxU32)
{
    pairFlags = PxPairFlag::eCONTACT_DEFAULT;
    return PxFilterFlag::eDEFAULT;
}

PxU32 scalar(const oc2::offline::SapImage& image, const char* name)
{
    for (const auto& item : image.scalars)
        if (item.name == name && item.bytes.size() == sizeof(PxU32))
        {
            PxU32 value = 0;
            std::memcpy(&value, item.bytes.data(), sizeof(value));
            return value;
        }
    require(false, std::string("missing scalar ") + name);
    return 0;
}

const oc2::offline::SapImage::Buffer& buffer(
    const oc2::offline::SapImage& image, const char* name)
{
    for (const auto& item : image.buffers)
        if (item.name == name) return item;
    require(false, std::string("missing buffer ") + name);
    return image.buffers.front();
}

void step(PxScene& scene)
{
    scene.simulate(1.0f / 60.0f);
    require(scene.fetchResults(true), "fetchResults failed");
}

} // namespace

int main()
{
    PxDefaultAllocator allocator;
    Errors errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = PxCreateFoundation(
        PX_PHYSICS_VERSION, allocator, errors);
    require(foundation != nullptr, "foundation creation failed");
    PxPhysics* physics = PxCreatePhysics(
        PX_PHYSICS_VERSION, *foundation, PxTolerancesScale());
    require(physics != nullptr, "physics creation failed");
    PxMaterial* material = physics->createMaterial(0.5f, 0.5f, 0.0f);
    require(material != nullptr, "material creation failed");

    PxSceneDesc desc(physics->getTolerancesScale());
    desc.gravity = PxVec3(0.0f);
    desc.cpuDispatcher = &dispatcher;
    desc.filterShader = contactFilter;
    desc.broadPhaseType = PxBroadPhaseType::eSAP;
    PxScene* scene = physics->createScene(desc);
    require(scene != nullptr, "scene creation failed");

    PxRigidStatic* floor = physics->createRigidStatic(PxTransform(PxIdentity));
    PxRigidDynamic* mover = physics->createRigidDynamic(
        PxTransform(PxVec3(0.0f, 0.9f, 0.0f)));
    require(floor != nullptr && mover != nullptr, "actor creation failed");
    PxShape* staticShape = physics->createShape(
        PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
    PxShape* dynamicShape = physics->createShape(
        PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
    require(staticShape != nullptr && dynamicShape != nullptr,
            "shape creation failed");
    floor->attachShape(*staticShape);
    mover->attachShape(*dynamicShape);
    staticShape->release();
    dynamicShape->release();
    mover->setMass(1.0f);
    mover->setMassSpaceInertiaTensor(PxVec3(1.0f));
    scene->addActor(*floor);
    scene->addActor(*mover);

    step(*scene);
    oc2::offline::SapImage cold;
    std::string error;
    require(oc2::offline::CaptureSap(*scene, cold, error), error);
    require(scalar(cold, "aabb.deletedOverlapCapacity") == 0 &&
            scalar(cold, "aabb.deletedOverlapSize") == 0 &&
            buffer(cold, "aabb.deletedOverlaps").address == 0 &&
            buffer(cold, "aabb.deletedOverlaps").bytes.empty(),
            "checkpoint did not have a cold deleted-overlap array");

    mover->setGlobalPose(PxTransform(PxVec3(10.0f, 0.9f, 0.0f)));
    step(*scene);
    oc2::offline::SapImage deleted;
    require(oc2::offline::CaptureSap(*scene, deleted, error), error);
    require(scalar(deleted, "aabb.deletedOverlapCapacity") == 32 &&
            scalar(deleted, "aabb.deletedOverlapSize") == 1 &&
            buffer(deleted, "aabb.deletedOverlaps").address != 0,
            "deletion did not grow the result array from zero to 32");

    oc2::offline::SapImage malformed = cold;
    for (auto& item : malformed.buffers)
        if (item.name == "aabb.deletedOverlaps") item.address = 1;
    require(!oc2::offline::RestoreSap(*scene, malformed, error),
            "malformed cold image was accepted");
    oc2::offline::SapImage afterReject;
    require(oc2::offline::CaptureSap(*scene, afterReject, error), error);
    std::string difference;
    require(deleted.equals(afterReject, difference),
            "malformed image changed live state: " + difference);

    oc2::offline::SapImage otherCapacity = cold;
    bool changedOtherCapacity = false;
    for (auto& item : otherCapacity.scalars)
        if (item.name == "sap.createdPairsCapacity")
        {
            PxU32 value = 0;
            std::memcpy(&value, item.bytes.data(), sizeof(value));
            ++value;
            std::memcpy(item.bytes.data(), &value, sizeof(value));
            changedOtherCapacity = true;
        }
    require(changedOtherCapacity, "created-pair capacity is absent");
    require(!oc2::offline::RestoreSap(*scene, otherCapacity, error),
            "unrelated capacity change was accepted");
    require(oc2::offline::CaptureSap(*scene, afterReject, error), error);
    require(deleted.equals(afterReject, difference),
            "rejected capacity change modified live state: " + difference);

    require(oc2::offline::RestoreSap(*scene, cold, error), error);
    oc2::offline::SapImage restored;
    require(oc2::offline::CaptureSap(*scene, restored, error), error);
    require(cold.equals(restored, difference),
            "cold SAP restore differs at " + difference);
    require(errors.count == 0, "PhysX reported an error");
    std::cout << "PASS cold AABB deleted-overlap 0->32 rollback\n";
    std::cout << "PASS malformed cold image rejected atomically\n";
    std::cout << "PASS unrelated capacity change rejected atomically\n";

    // Only SAP was rewound; contacts and islands still describe the deleted
    // state. End the process without simulating or destroying this scene.
    std::cout.flush();
    std::_Exit(0);
}
