#include "QueryImage.h"

#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"

using namespace physx;
using oc2::offline::CaptureQueryImage;
using oc2::offline::QueryImage;
using oc2::offline::RestoreQueryImage;

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
                     PxPairFlags& flags, const void*, PxU32)
{
    flags = PxPairFlag::eCONTACT_DEFAULT;
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
    PxRigidStatic* fixed = nullptr;
    PxRigidDynamic* mover = nullptr;
    PxShape* moverShape = nullptr;

    explicit Fixture(PxU32 dynamicShapes = 1)
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

        fixed = physics->createRigidStatic(PxTransform(PxVec3(-10.0f, 0.0f, 0.0f)));
        PxShape* staticShape = physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
        require(fixed && staticShape, "static actor/shape");
        fixed->attachShape(*staticShape);
        staticShape->release();
        scene->addActor(*fixed);

        mover = physics->createRigidDynamic(PxTransform(PxVec3(3.0f, 0.0f, 0.0f)));
        moverShape = physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
        require(mover && moverShape, "dynamic actor/shape");
        mover->attachShape(*moverShape);
        moverShape->release();
        for (PxU32 i = 1; i < dynamicShapes; ++i)
        {
            PxShape* extra = physics->createShape(
                PxBoxGeometry(0.2f, 0.2f, 0.2f), *material);
            require(extra != nullptr, "extra query shape");
            extra->setLocalPose(PxTransform(PxVec3(0.0f, 0.0f,
                                                   10.0f + PxReal(i))));
            mover->attachShape(*extra);
            extra->release();
        }
        mover->setMass(1.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(1.0f));
        scene->addActor(*mover);
        mover->putToSleep();

        scene->simulate(1.0f / 60.0f);
        require(scene->fetchResults(true), "settled fetchResults");
        // The public purge path leaves a finished current tree and no
        // progressive build. It is part of fixture setup, not rewind.
        scene->forceDynamicTreeRebuild(false, true);
        PxRaycastBuffer warm;
        scene->raycast(PxVec3(1.0f, 0.0f, 0.0f), PxVec3(1.0f, 0.0f, 0.0f),
                       3.0f, warm);
        // First refit lazily allocates the tree's refit bitmap. Prime that
        // allocation before taking either image in this fixed-storage test.
        mover->setGlobalPose(PxTransform(PxVec3(3.5f, 0.0f, 0.0f)));
        scene->raycast(PxVec3(1.0f, 0.0f, 0.0f), PxVec3(1.0f, 0.0f, 0.0f),
                       4.0f, warm);
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

    bool rayAt(float x)
    {
        PxRaycastBuffer hit;
        scene->raycast(PxVec3(x - 2.0f, 0.0f, 0.0f),
                       PxVec3(1.0f, 0.0f, 0.0f), 4.0f, hit);
        return hit.hasBlock && hit.block.shape == moverShape;
    }

    bool overlapAt(float x)
    {
        PxOverlapBuffer hit;
        scene->overlap(PxSphereGeometry(0.4f),
                       PxTransform(PxVec3(x, 0.0f, 0.0f)), hit);
        for (PxU32 i = 0; i < hit.nbTouches; ++i)
            if (hit.touches[i].shape == moverShape) return true;
        return hit.hasBlock && hit.block.shape == moverShape;
    }
};

bool sameStorage(const QueryImage& a, const QueryImage& b)
{
    if (a.scene != b.scene || a.fields.size() != b.fields.size()) return false;
    for (std::size_t i = 0; i < a.fields.size(); ++i)
    {
        const QueryImage::Field& x = a.fields[i];
        const QueryImage::Field& y = b.fields[i];
        if (x.name != y.name || x.address != y.address ||
            x.bytes.size() != y.bytes.size() ||
            x.invariant != y.invariant ||
            (x.invariant && x.bytes != y.bytes)) return false;
    }
    return true;
}

void check()
{
    Fixture fixture;
    std::string error;
    QueryImage settled;
    if (!CaptureQueryImage(*fixture.scene, settled, error))
        require(false, "settled capture: " + error);
    QueryImage duplicate;
    if (!CaptureQueryImage(*fixture.scene, duplicate, error) ||
        !settled.equals(duplicate, error))
        require(false, "duplicate settled capture: " + error);
    require(fixture.rayAt(3.0f) && fixture.overlapAt(3.0f),
            "initial raycast/overlap");
    QueryImage unchanged;
    if (!CaptureQueryImage(*fixture.scene, unchanged, error) ||
        !settled.equals(unchanged, error))
        require(false, "query without pending updates changed the image: " + error);

    fixture.mover->setGlobalPose(PxTransform(PxVec3(5.0f, 0.0f, 0.0f)));
    QueryImage pending;
    if (!CaptureQueryImage(*fixture.scene, pending, error))
        require(false, "pending capture: " + error);
    require(!settled.equals(pending, error), "pending update did not alter query state");
    require(fixture.rayAt(5.0f) && fixture.overlapAt(5.0f),
            "updated raycast/overlap");
    QueryImage flushed;
    if (!CaptureQueryImage(*fixture.scene, flushed, error))
        require(false, "flushed capture: " + error);
    require(!pending.equals(flushed, error), "query did not flush pending state");

    for (int i = 0; i < 100; ++i)
    {
        if (!RestoreQueryImage(*fixture.scene, pending, error))
            require(false, "restore pending: " + error);
        QueryImage observed;
        if (!CaptureQueryImage(*fixture.scene, observed, error) ||
            !pending.equals(observed, error))
            require(false, "pending parity: " + error);
        require(fixture.rayAt(5.0f) && fixture.overlapAt(5.0f),
                "raycast/overlap after pending restore");
        if (!CaptureQueryImage(*fixture.scene, observed, error) ||
            !flushed.equals(observed, error))
            require(false, "query flush parity: " + error);
        if (!RestoreQueryImage(*fixture.scene, flushed, error))
            require(false, "restore flushed: " + error);
    }

    QueryImage corrupt = pending;
    corrupt.fields[0].bytes[0] ^= 1;
    require(!RestoreQueryImage(*fixture.scene, corrupt, error),
            "corrupt image was accepted");
    QueryImage after;
    if (!CaptureQueryImage(*fixture.scene, after, error) ||
        !flushed.equals(after, error))
        require(false, "corrupt rejection changed live image: " + error);

    fixture.scene->removeActor(*fixture.mover);
    require(!RestoreQueryImage(*fixture.scene, pending, error),
            "changed query topology was accepted");

    std::cout << "PASS source query image: settled, pending flush, 100 A/B "
                 "round trips, raycast/overlap replay, atomic "
                 "rejection\n";
}

void checkRebuildGate()
{
    Fixture rebuilding(32);
    rebuilding.mover->setGlobalPose(PxTransform(PxVec3(7.0f, 0.0f, 0.0f)));
    rebuilding.scene->simulate(1.0f / 60.0f);
    require(rebuilding.scene->fetchResults(true), "rebuild fixture fetchResults");
    std::string error;
    QueryImage initializing;
    if (!CaptureQueryImage(*rebuilding.scene, initializing, error))
        require(false, "BUILD_INIT capture: " + error);

    rebuilding.mover->setGlobalPose(PxTransform(PxVec3(8.0f, 0.0f, 0.0f)));
    QueryImage pending;
    if (!CaptureQueryImage(*rebuilding.scene, pending, error))
        require(false, "BUILD_INIT pending capture: " + error);
    require(rebuilding.rayAt(8.0f) && rebuilding.overlapAt(8.0f),
            "BUILD_INIT query results");
    QueryImage flushed;
    if (!CaptureQueryImage(*rebuilding.scene, flushed, error))
        require(false, "BUILD_INIT flushed capture: " + error);
    for (int i = 0; i < 100; ++i)
    {
        if (!RestoreQueryImage(*rebuilding.scene, pending, error))
            require(false, "BUILD_INIT pending restore: " + error);
        require(rebuilding.rayAt(8.0f) && rebuilding.overlapAt(8.0f),
                "BUILD_INIT replay hits");
        QueryImage observed;
        if (!CaptureQueryImage(*rebuilding.scene, observed, error) ||
            !flushed.equals(observed, error))
            require(false, "BUILD_INIT replay parity: " + error);
    }

    rebuilding.scene->simulate(1.0f / 60.0f);
    require(rebuilding.scene->fetchResults(true), "BUILD_IN_PROGRESS fetchResults");
    QueryImage inProgress;
    if (!CaptureQueryImage(*rebuilding.scene, inProgress, error))
        require(false, "BUILD_IN_PROGRESS capture: " + error);
    require(!RestoreQueryImage(*rebuilding.scene, initializing, error),
            "cross-phase image was accepted");
    rebuilding.mover->setGlobalPose(PxTransform(PxVec3(9.0f, 0.0f, 0.0f)));
    QueryImage inProgressPending;
    if (!CaptureQueryImage(*rebuilding.scene, inProgressPending, error))
        require(false, "BUILD_IN_PROGRESS pending capture: " + error);
    require(rebuilding.rayAt(9.0f) && rebuilding.overlapAt(9.0f),
            "BUILD_IN_PROGRESS query results");
    QueryImage inProgressFlushed;
    if (!CaptureQueryImage(*rebuilding.scene, inProgressFlushed, error))
        require(false, "BUILD_IN_PROGRESS flushed capture: " + error);
    for (int i = 0; i < 100; ++i)
    {
        if (!RestoreQueryImage(*rebuilding.scene, inProgressPending, error))
            require(false, "BUILD_IN_PROGRESS pending restore: " + error);
        require(rebuilding.rayAt(9.0f) && rebuilding.overlapAt(9.0f),
                "BUILD_IN_PROGRESS replay hits");
        QueryImage observed;
        if (!CaptureQueryImage(*rebuilding.scene, observed, error) ||
            !inProgressFlushed.equals(observed, error))
            require(false, "BUILD_IN_PROGRESS replay parity: " + error);
    }

    rebuilding.mover->putToSleep();
    QueryImage stepA, stepB;
    bool stableStep = false;
    for (int i = 0; i < 50; ++i)
    {
        if (!CaptureQueryImage(*rebuilding.scene, stepA, error))
            require(false, "progressive step A capture: " + error);
        rebuilding.scene->simulate(1.0f / 60.0f);
        require(rebuilding.scene->fetchResults(true),
                "progressive fixture fetchResults");
        if (!CaptureQueryImage(*rebuilding.scene, stepB, error))
            require(false, "progressive step B capture: " + error);
        if (sameStorage(stepA, stepB) && !stepA.equals(stepB, error))
        {
            stableStep = true;
            break;
        }
    }
    require(stableStep, "no stable-allocation progressive step was observed");
    for (int i = 0; i < 100; ++i)
    {
        if (!RestoreQueryImage(*rebuilding.scene, stepA, error))
            require(false, "progressive step restore: " + error);
        rebuilding.scene->simulate(1.0f / 60.0f);
        require(rebuilding.scene->fetchResults(true),
                "progressive replay fetchResults");
        QueryImage observed;
        if (!CaptureQueryImage(*rebuilding.scene, observed, error) ||
            !stepB.equals(observed, error))
            require(false, "progressive step replay parity: " + error);
    }
    std::cout << "PASS progressive query-tree image: BUILD_INIT replay, "
                 "BUILD_IN_PROGRESS query and builder replay, cross-phase "
                 "rejection\n";
}

struct SixShapeQueryFixture
{
    PxDefaultAllocator allocator;
    Errors errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;
    PxScene* scene = nullptr;
    PxRigidDynamic* mover = nullptr;
    PxShape* firstShape = nullptr;
    std::vector<PxRigidActor*> actors;

    SixShapeQueryFixture()
    {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        require(foundation != nullptr, "six-shape foundation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        require(physics != nullptr, "six-shape physics");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        require(material != nullptr, "six-shape material");
        PxSceneDesc desc(physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &dispatcher;
        desc.filterShader = filter;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        desc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
        desc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
        scene = physics->createScene(desc);
        require(scene != nullptr, "six-shape scene");
        for (PxU32 i = 0; i < 6; ++i)
        {
            PxRigidStatic* fixed = physics->createRigidStatic(
                PxTransform(PxVec3(PxReal(i) * 3.0f, -10.0f, 0.0f)));
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(fixed && shape, "six-shape fixed actor/shape");
            fixed->attachShape(*shape);
            shape->release();
            scene->addActor(*fixed);
            actors.push_back(fixed);
        }
        mover = physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f, 0.95f, 0.0f)));
        require(mover != nullptr, "six-shape mover");
        for (PxU32 i = 0; i < 6; ++i)
        {
            PxShape* shape = physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
            require(shape != nullptr, "six-shape dynamic shape");
            mover->attachShape(*shape);
            shape->setLocalPose(PxTransform(PxVec3(PxReal(i) * 3.0f,
                                                  0.0f, 0.0f)));
            if (!i) firstShape = shape;
            shape->release();
        }
        mover->setMass(6.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(18.0f, 18.0f, 18.0f));
        mover->setLinearDamping(0.0f);
        mover->setAngularDamping(0.0f);
        scene->addActor(*mover);
        actors.push_back(mover);
    }

    ~SixShapeQueryFixture()
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
        mover->setLinearVelocity(PxVec3(0.0f));
        mover->setAngularVelocity(PxVec3(0.0f));
        scene->simulate(1.0f / 60.0f);
        require(scene->fetchResults(true), "six-shape fetchResults");
    }

    bool overlapMoverAt(float z)
    {
        PxOverlapBuffer hit;
        scene->overlap(PxSphereGeometry(0.25f),
                       PxTransform(PxVec3(0.0f, 0.95f, z)), hit);
        if (hit.hasBlock && hit.block.shape == firstShape) return true;
        for (PxU32 i = 0; i < hit.nbTouches; ++i)
            if (hit.touches[i].shape == firstShape) return true;
        return false;
    }
};

const QueryImage::Field* queryField(const QueryImage& image,
                                    const std::string& name)
{
    for (const QueryImage::Field& field : image.fields)
        if (field.name == name) return &field;
    return nullptr;
}

void checkStackRebase()
{
    SixShapeQueryFixture fixture;
    fixture.step(false);
    fixture.step(true);
    fixture.step(false);
    std::string error;
    QueryImage checkpoint;
    if (!CaptureQueryImage(*fixture.scene, checkpoint, error))
        require(false, "six-shape checkpoint capture: " + error);
    fixture.step(true);
    QueryImage successor;
    if (!CaptureQueryImage(*fixture.scene, successor, error))
        require(false, "six-shape successor capture: " + error);
    const QueryImage::Field* aStack = queryField(
        checkpoint, "dynamic.newTreeStorage.stackStorage");
    const QueryImage::Field* bStack = queryField(
        successor, "dynamic.newTreeStorage.stackStorage");
    require(aStack && bStack && aStack->bytes.size() == 8 &&
            bStack->bytes.size() == 16 &&
            aStack->address != bStack->address,
            "six-shape fixture missed FIFO capacity 1 to 2 growth");
    QueryImage altered = checkpoint;
    for (QueryImage::Field& field : altered.fields)
        if (field.name == "dynamic.newTreeStorage.stackStorage")
            field.bytes[0] ^= 1;
    require(!checkpoint.equalsWithRebasedStack(altered, error),
            "rebased equality ignored changed FIFO entry content");
    altered = checkpoint;
    for (QueryImage::Field& field : altered.fields)
        if (field.name == "dynamic.newTreeStorage.nodes")
            field.bytes[0] ^= 1;
    require(!checkpoint.equalsWithRebasedStack(altered, error),
            "rebased equality ignored changed tree-node content");
    require(fixture.overlapMoverAt(5.0f), "six-shape successor overlap");

    for (int i = 0; i < 100; ++i)
    {
        if (!RestoreQueryImage(*fixture.scene, checkpoint, error))
            require(false, "six-shape rebased restore: " + error);
        QueryImage observed;
        if (!CaptureQueryImage(*fixture.scene, observed, error) ||
            !checkpoint.equalsWithRebasedStack(observed, error))
            require(false, "six-shape rebased checkpoint parity: " + error);
        require(!checkpoint.equals(observed, error),
                "six-shape restore unexpectedly reused freed FIFO address");
        if (i == 0)
        {
            require(!RestoreQueryImage(*fixture.scene, successor, error),
                    "unsafe reverse FIFO capacity growth was accepted");
            QueryImage afterReject;
            if (!CaptureQueryImage(*fixture.scene, afterReject, error) ||
                !checkpoint.equalsWithRebasedStack(afterReject, error))
                require(false, "reverse-growth rejection mutated query: " + error);
        }
        fixture.step(true);
        if (!CaptureQueryImage(*fixture.scene, observed, error) ||
            !successor.equalsWithRebasedStack(observed, error))
            require(false, "six-shape replayed build step: " + error);
        require(fixture.overlapMoverAt(5.0f),
                "six-shape replayed successor overlap");
    }
    std::cout << "PASS six-shape progressive FIFO growth: guarded rebase, "
                 "100 query-image and overlap replays, unsafe reverse "
                 "growth rejected atomically\n";
}

} // namespace

int main()
{
    check();
    checkRebuildGate();
    checkStackRebase();
    return 0;
}
