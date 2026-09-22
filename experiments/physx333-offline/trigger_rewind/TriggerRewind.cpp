// Source-built PhysX 3.3.3 trigger deletion and same-scene reconstruction.
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "NpScene.h"
#include "NpShape.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScScene.h"
#include "ScNPhaseCore.h"
#include "ScRigidCore.h"
#include "../oracle/Oracle.h"
#include "../aux_interactions/AuxInteractionImage.h"
#include "../sap/SapImage.h"
#include "../body/BodyImage.h"
#include "../scene_clock/SceneClockImage.h"
#include "TriggerRewindBridge.h"

using namespace physx;
using namespace physx333_offline;

namespace {

const PxReal kStep = 1.0f / 60.0f;
const PxU32 kDynamicId = 3;

void require(bool condition, const std::string& reason)
{
    if (!condition)
    {
        std::cerr << "FAIL " << reason << '\n';
        std::exit(1);
    }
}

PxU32 actorId(const PxActor* actor)
{
    return static_cast<PxU32>(
        reinterpret_cast<std::uintptr_t>(actor->userData));
}

struct Errors : PxErrorCallback
{
    unsigned count = 0;
    void reportError(PxErrorCode::Enum code, const char* message,
                     const char* file, int line) override
    {
        ++count;
        std::cerr << "PhysX " << static_cast<int>(code) << ": " << message
                  << " (" << file << ':' << line << ")\n";
        if (code == PxErrorCode::eINVALID_PARAMETER ||
            code == PxErrorCode::eINVALID_OPERATION ||
            code == PxErrorCode::eOUT_OF_MEMORY)
            std::abort();
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

PxFilterFlags filter(PxFilterObjectAttributes a0, PxFilterData,
                     PxFilterObjectAttributes a1, PxFilterData,
                     PxPairFlags& flags, const void*, PxU32)
{
    if (PxFilterObjectIsTrigger(a0) || PxFilterObjectIsTrigger(a1))
    {
        flags = PxPairFlag::eTRIGGER_DEFAULT;
        return PxFilterFlag::eDEFAULT;
    }
    flags = PxPairFlag::eCONTACT_DEFAULT;
    return PxFilterFlag::eDEFAULT;
}

struct Event
{
    PxU32 staticId = 0;
    PxU32 otherId = 0;
    PxU32 flags = 0;
    bool operator==(const Event& b) const
    {
        return staticId == b.staticId && otherId == b.otherId &&
               flags == b.flags;
    }
};

struct Callbacks : PxSimulationEventCallback
{
    std::vector<Event> rows;
    void onConstraintBreak(PxConstraintInfo*, PxU32) override {}
    void onWake(PxActor**, PxU32) override {}
    void onSleep(PxActor**, PxU32) override {}
    void onContact(const PxContactPairHeader&, const PxContactPair*,
                   PxU32) override
    {
        require(false, "unexpected contact callback");
    }
    void onTrigger(PxTriggerPair* pairs, PxU32 count) override
    {
        for (PxU32 i = 0; i < count; ++i)
        {
            Event event;
            event.staticId = actorId(pairs[i].triggerActor);
            event.otherId = actorId(pairs[i].otherActor);
            event.flags = static_cast<PxU32>(pairs[i].status);
            rows.push_back(event);
        }
    }
};

struct Runtime
{
    PxDefaultAllocator allocator;
    Errors errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;

    Runtime()
    {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        require(foundation != nullptr, "foundation creation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        require(physics != nullptr, "physics creation");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        require(material != nullptr, "material creation");
    }
    ~Runtime()
    {
        material->release();
        physics->release();
        foundation->release();
    }
};

struct State
{
    OracleImage oracle;
    AuxInteractionImage aux;
    oc2::offline::SapImage sap;
    oc2::offline::BodyImage body;
    oc2::offline::SceneClockImage clock;
};

struct World
{
    Runtime& runtime;
    Callbacks callbacks;
    PxScene* scene = nullptr;
    PxRigidDynamic* dynamic = nullptr;
    PxRigidStatic* statics[2] = {};

    explicit World(Runtime& rt) : runtime(rt)
    {
        PxSceneDesc desc(rt.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &rt.dispatcher;
        desc.filterShader = filter;
        desc.simulationEventCallback = &callbacks;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        scene = rt.physics->createScene(desc);
        require(scene != nullptr, "scene creation");
        for (PxU32 i = 0; i < 2; ++i)
        {
            const PxReal x = i == 0 ? -0.65f : 0.65f;
            statics[i] = rt.physics->createRigidStatic(
                PxTransform(PxVec3(x, 0.0f, 0.0f)));
            PxShape* shape = rt.physics->createShape(
                PxBoxGeometry(0.25f, 0.25f, 0.25f), *rt.material);
            require(statics[i] != nullptr && shape != nullptr,
                    "static trigger creation");
            shape->setFlag(PxShapeFlag::eSIMULATION_SHAPE, false);
            shape->setFlag(PxShapeFlag::eTRIGGER_SHAPE, true);
            statics[i]->attachShape(*shape);
            shape->release();
            statics[i]->userData = reinterpret_cast<void*>(
                static_cast<std::uintptr_t>(i + 1));
            scene->addActor(*statics[i]);
        }
        dynamic = rt.physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f)));
        PxShape* capsule = rt.physics->createShape(
            PxCapsuleGeometry(0.25f, 0.5f), *rt.material);
        require(dynamic != nullptr && capsule != nullptr,
                "dynamic capsule creation");
        dynamic->attachShape(*capsule);
        capsule->release();
        dynamic->setMass(1.0f);
        dynamic->setMassSpaceInertiaTensor(PxVec3(1.0f));
        dynamic->setWakeCounter(100.0f);
        dynamic->userData = reinterpret_cast<void*>(
            static_cast<std::uintptr_t>(kDynamicId));
        scene->addActor(*dynamic);
    }

    ~World()
    {
        dynamic->release();
        for (PxRigidStatic* actor : statics) actor->release();
        scene->release();
    }

    std::vector<Event> step(PxReal z)
    {
        callbacks.rows.clear();
        dynamic->setGlobalPose(PxTransform(PxVec3(0.0f, 0.0f, z)));
        dynamic->setLinearVelocity(PxVec3(0.0f));
        dynamic->setAngularVelocity(PxVec3(0.0f));
        scene->simulate(kStep);
        require(scene->fetchResults(true), "fetchResults");
        return callbacks.rows;
    }

    State capture(const char* stage)
    {
        State image;
        std::string error;
        require(CaptureOracle(*scene, image.oracle, error),
                std::string(stage) + " Oracle: " + error);
        require(CaptureAuxInteractionImage(*scene, image.aux, error),
                std::string(stage) + " aux: " + error);
        require(oc2::offline::CaptureSap(*scene, image.sap, error),
                std::string(stage) + " SAP: " + error);
        require(oc2::offline::CaptureBodies(*scene, image.body, error),
                std::string(stage) + " body: " + error);
        require(oc2::offline::CaptureSceneClock(*scene, image.clock, error),
                std::string(stage) + " scene clock: " + error);
        return image;
    }

    Sc::NPhaseCore* nphase()
    {
        NpScene& np = static_cast<NpScene&>(*scene);
        return np.getScene().getScScene().getNPhaseCore();
    }

    TriggerRewindPairV1 pairFor(const AuxInteractionRow& row)
    {
        require(row.pair.shape0.actorId >= 1 &&
                row.pair.shape0.actorId <= 2 &&
                row.pair.shape1.actorId == kDynamicId &&
                row.pair.shape0.shapeIndex == 0 &&
                row.pair.shape1.shapeIndex == 0,
                "checkpoint trigger orientation/identity");
        PxShape* dynamicShape = nullptr;
        PxShape* staticShape = nullptr;
        dynamic->getShapes(&dynamicShape, 1);
        PxRigidStatic* staticActor = statics[row.pair.shape0.actorId - 1];
        staticActor->getShapes(&staticShape, 1);
        TriggerRewindPairV1 pair = {};
        pair.dynamicActorCore = &static_cast<NpRigidDynamic*>(dynamic)
            ->getScbBodyFast().getScBody();
        pair.dynamicShapeCore = &static_cast<NpShape*>(dynamicShape)
            ->getScbShape().getScShape();
        pair.staticActorCore = &static_cast<NpRigidStatic*>(staticActor)
            ->getScbRigidStaticFast().getScStatic();
        pair.staticShapeCore = &static_cast<NpShape*>(staticShape)
            ->getScbShape().getScShape();
        pair.targetPoolSlot = row.poolSlot;
        pair.expectedPairFlags = PxPairFlag::eTRIGGER_DEFAULT;
        pair.targetInteractionFlags = row.interactionFlags;
        pair.targetCoreFlags = row.coreFlags;
        pair.targetDirtyFlags = row.dirtyFlags;
        pair.targetTriggerFlags = row.triggerFlags;
        pair.targetLastTouch = row.lastFrameHadContacts;
        pair.targetCacheState = row.triggerCacheState;
        return pair;
    }

    void restore(const State& checkpoint)
    {
        std::string error;
        TriggerRewindPairV1 pairs[2] = {
            pairFor(checkpoint.aux.triggers[0]),
            pairFor(checkpoint.aux.triggers[1])
        };
        require(oc2::offline::RestoreBodies(*scene, checkpoint.body, error),
                "restore body: " + error);
        require(oc2::offline::RestoreSap(*scene, checkpoint.sap, error),
                "restore SAP: " + error);
        require(oc2::offline::RestoreSceneClock(*scene, checkpoint.clock,
                                                error),
                "restore scene clock: " + error);
        const std::uint32_t result = oc2_physx333_trigger_recreate_v1(
            nphase(), pairs, 2);
        require(result == TriggerRewindSuccess,
                "native trigger reconstruction was rejected: " +
                std::to_string(result));
    }
};

void checkEqual(const State& a, const State& b, const char* stage)
{
    std::string difference;
    require(a.oracle.equals(b.oracle, difference),
            std::string(stage) + " Oracle: " + difference);
    require(a.aux.equals(b.aux, difference),
            std::string(stage) + " aux: " + difference);
    require(a.sap.equals(b.sap, difference),
            std::string(stage) + " SAP: " + difference);
    require(a.body.equals(b.body, difference),
            std::string(stage) + " body: " + difference);
    require(a.clock.equals(b.clock, difference),
            std::string(stage) + " scene clock: " + difference);
}

void checkEvents(const std::vector<Event>& events, PxU32 flags)
{
    require(events.size() == 2, "expected two trigger callbacks");
    bool seen[2] = {};
    for (const auto& event : events)
    {
        require(event.staticId >= 1 && event.staticId <= 2 &&
                event.otherId == kDynamicId && event.flags == flags &&
                !seen[event.staticId - 1],
                "trigger callback identity or payload differs");
        seen[event.staticId - 1] = true;
    }
    require(seen[0] && seen[1], "trigger callback pair missing");
}

void runCase(Runtime& rt, bool warm)
{
    World world(rt);
    checkEvents(world.step(0.0f), PxPairFlag::eNOTIFY_TOUCH_FOUND);
    require(world.step(0.0f).empty(), "settled A emitted callbacks");
    if (warm)
    {
        checkEvents(world.step(3.0f), PxPairFlag::eNOTIFY_TOUCH_LOST);
        checkEvents(world.step(0.0f), PxPairFlag::eNOTIFY_TOUCH_FOUND);
        require(world.step(0.0f).empty(), "warm A emitted callbacks");
    }
    const State a = world.capture(warm ? "warm A" : "cold A");
    require(a.aux.triggers.size() == 2 &&
            a.aux.triggerPool.usedCount == 2 &&
            a.aux.markers.empty() &&
            a.aux.sceneOrders.size() == 3 &&
            a.aux.sceneOrders[1].activeCount == 2,
            "settled A trigger inventory");
    std::vector<Event> firstBEvents;
    State firstB;
    for (unsigned cycle = 0; cycle < 100; ++cycle)
    {
        const auto bEvents = world.step(3.0f);
        checkEvents(bEvents, PxPairFlag::eNOTIFY_TOUCH_LOST);
        const State b = world.capture("B");
        require(b.aux.triggers.empty() &&
                b.aux.triggerPool.usedCount == 0 &&
                b.aux.triggerPool.freeOrder.size() == 32,
                "B did not delete both trigger objects");
        if (cycle == 0)
        {
            firstBEvents = bEvents;
            firstB = b;
            TriggerRewindPairV1 rejected[2] = {
                world.pairFor(a.aux.triggers[0]),
                world.pairFor(a.aux.triggers[1])
            };
            rejected[0].targetPoolSlot = 99;
            require(oc2_physx333_trigger_recreate_v1(
                        world.nphase(), rejected, 2) ==
                    TriggerRewindInvalidInput,
                    "out-of-range slot was accepted");
            checkEqual(b, world.capture("after range reject"),
                       "range rejection");
            rejected[0] = world.pairFor(a.aux.triggers[0]);
            rejected[0].targetPoolSlot = 31;
            require(oc2_physx333_trigger_recreate_v1(
                        world.nphase(), rejected, 2) ==
                    TriggerRewindPoolMismatch,
                    "wrong free-head slot was accepted");
            checkEqual(b, world.capture("after pool reject"),
                       "pool rejection");
            rejected[0] = world.pairFor(a.aux.triggers[0]);
            rejected[0].staticShapeCore = rejected[1].staticShapeCore;
            const std::uint32_t identityResult =
                oc2_physx333_trigger_recreate_v1(
                    world.nphase(), rejected, 2);
            require(identityResult != TriggerRewindSuccess,
                    "wrong static shape identity was accepted");
            checkEqual(b, world.capture("after identity reject"),
                       "identity rejection");
            rejected[0] = world.pairFor(a.aux.triggers[0]);
            rejected[0].expectedPairFlags = 0;
            require(oc2_physx333_trigger_recreate_v1(
                        world.nphase(), rejected, 2) ==
                    TriggerRewindInvalidInput,
                    "wrong pair flags were accepted");
            const State afterReject = world.capture("after reject");
            checkEqual(b, afterReject, "atomic rejection");
        }
        else
        {
            checkEqual(firstB, b, "replayed B");
            require(firstBEvents == bEvents,
                    "replayed B ordered callback stream");
        }
        world.restore(a);
        const State restored = world.capture("restored A");
        checkEqual(a, restored, "restored A");
    }
    std::cout << "PASS " << (warm ? "warm" : "cold")
              << " two capsule/box trigger deletions, native B->A restore, "
                 "100 exact A and replay B cycles\n";
}

} // namespace

int main()
{
    Runtime runtime;
    runCase(runtime, false);
    runCase(runtime, true);
    require(runtime.errors.count == 0, "PhysX reported errors");
    return 0;
}
