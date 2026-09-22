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
const PxU32 kMixedDynamicId = 5;
const PxU32 kMarkerFilterTag = 1;

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

PxFilterFlags filter(PxFilterObjectAttributes a0, PxFilterData d0,
                     PxFilterObjectAttributes a1, PxFilterData d1,
                     PxPairFlags& flags, const void*, PxU32)
{
    if (d0.word0 == kMarkerFilterTag || d1.word0 == kMarkerFilterTag)
    {
        flags = PxPairFlags();
        return PxFilterFlag::eSUPPRESS;
    }
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

// Four statics share one dynamic actor. Two trigger pairs disappear at B;
// a third trigger and one marker survive, so the dynamic actor's interaction
// array contains mixed types and cannot be restored by merely appending.
struct MixedWorld
{
    Runtime& runtime;
    Callbacks callbacks;
    PxScene* scene = nullptr;
    PxRigidDynamic* dynamic = nullptr;
    PxRigidStatic* statics[4] = {};

    explicit MixedWorld(Runtime& rt) : runtime(rt)
    {
        PxSceneDesc desc(rt.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &rt.dispatcher;
        desc.filterShader = filter;
        desc.simulationEventCallback = &callbacks;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        scene = rt.physics->createScene(desc);
        require(scene != nullptr, "mixed scene creation");
        for (PxU32 i = 0; i < 4; ++i)
        {
            const PxVec3 position = i == 0 ? PxVec3(-0.65f, 0.0f, 0.0f) :
                i == 1 ? PxVec3(0.65f, 0.0f, 0.0f) :
                i == 2 ? PxVec3(0.0f, 0.0f, 1.5f) :
                         PxVec3(3.0f, 0.0f, 1.5f);
            const PxVec3 extents = i < 2 ? PxVec3(0.25f) :
                i == 2 ? PxVec3(0.25f, 0.25f, 2.0f) :
                         PxVec3(0.30f, 0.30f, 2.0f);
            statics[i] = rt.physics->createRigidStatic(PxTransform(position));
            PxShape* shape = rt.physics->createShape(
                PxBoxGeometry(extents), *rt.material);
            require(statics[i] != nullptr && shape != nullptr,
                    "mixed static creation");
            if (i < 3)
            {
                shape->setFlag(PxShapeFlag::eSIMULATION_SHAPE, false);
                shape->setFlag(PxShapeFlag::eTRIGGER_SHAPE, true);
            }
            else
            {
                PxFilterData data;
                data.word0 = kMarkerFilterTag;
                shape->setSimulationFilterData(data);
            }
            statics[i]->attachShape(*shape);
            shape->release();
            statics[i]->userData = reinterpret_cast<void*>(
                static_cast<std::uintptr_t>(i + 1));
            scene->addActor(*statics[i]);
        }
        dynamic = rt.physics->createRigidDynamic(PxTransform(PxVec3(0.0f)));
        require(dynamic != nullptr, "mixed dynamic creation");
        PxShape* capsule = rt.physics->createShape(
            PxCapsuleGeometry(0.25f, 0.5f), *rt.material);
        require(capsule != nullptr, "mixed capsule creation");
        dynamic->attachShape(*capsule);
        capsule->release();
        PxShape* auxiliary = rt.physics->createShape(
            PxBoxGeometry(0.2f, 0.2f, 0.2f), *rt.material);
        require(auxiliary != nullptr, "mixed auxiliary box creation");
        auxiliary->setLocalPose(PxTransform(PxVec3(3.0f, 0.0f, 0.0f)));
        dynamic->attachShape(*auxiliary);
        auxiliary->release();
        dynamic->setMass(1.0f);
        dynamic->setMassSpaceInertiaTensor(PxVec3(1.0f));
        dynamic->setWakeCounter(100.0f);
        dynamic->userData = reinterpret_cast<void*>(
            static_cast<std::uintptr_t>(kMixedDynamicId));
        scene->addActor(*dynamic);
    }

    ~MixedWorld()
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
        require(scene->fetchResults(true), "mixed fetchResults");
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
                std::string(stage) + " clock: " + error);
        return image;
    }

    Sc::NPhaseCore* nphase()
    {
        NpScene& np = static_cast<NpScene&>(*scene);
        return np.getScene().getScScene().getNPhaseCore();
    }

    void* staticActorCore(PxU32 staticId)
    {
        require(staticId >= 1 && staticId <= 4, "mixed static actor ID");
        return &static_cast<NpRigidStatic*>(statics[staticId - 1])
            ->getScbRigidStaticFast().getScStatic();
    }

    void* staticShapeCore(PxU32 staticId)
    {
        PxShape* shape = nullptr;
        statics[staticId - 1]->getShapes(&shape, 1);
        return &static_cast<NpShape*>(shape)->getScbShape().getScShape();
    }

    void* dynamicActorCore()
    {
        return &static_cast<NpRigidDynamic*>(dynamic)
            ->getScbBodyFast().getScBody();
    }

    void* dynamicShapeCore(PxU32 index)
    {
        require(index < 2, "mixed dynamic shape index");
        PxShape* shapes[2] = {};
        require(dynamic->getShapes(shapes, 2) == 2,
                "mixed dynamic shape inventory");
        return &static_cast<NpShape*>(shapes[index])
            ->getScbShape().getScShape();
    }

    TriggerRewindPairV1 pairFor(const AuxInteractionRow& row)
    {
        const PxU32 staticId = row.pair.shape0.actorId;
        require(staticId >= 1 && staticId <= 2 &&
                row.pair.shape1.actorId == kMixedDynamicId &&
                row.pair.shape0.shapeIndex == 0 &&
                row.pair.shape1.shapeIndex == 0,
                "mixed missing trigger orientation/identity");
        TriggerRewindPairV1 pair = {};
        pair.dynamicActorCore = dynamicActorCore();
        pair.dynamicShapeCore = dynamicShapeCore(0);
        pair.staticActorCore = staticActorCore(staticId);
        pair.staticShapeCore = staticShapeCore(staticId);
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

    TriggerRewindMixedPlanV2 makePlan(const AuxInteractionImage& a)
    {
        require(a.triggers.size() == 3 && a.markers.size() == 1 &&
                a.sceneOrders.size() == 3,
                "mixed A interaction inventory");
        TriggerRewindMixedPlanV2 plan = {};
        const AuxInteractionRow* missing[2] = {};
        const AuxInteractionRow* survivor = nullptr;
        for (const auto& row : a.triggers)
        {
            const PxU32 staticId = row.pair.shape0.actorId;
            if (staticId == 1 || staticId == 2)
                missing[staticId - 1] = &row;
            else if (staticId == 3)
                survivor = &row;
        }
        require(missing[0] && missing[1] && survivor,
                "mixed A missing/survivor trigger identity");
        plan.missing[0] = pairFor(*missing[0]);
        plan.missing[1] = pairFor(*missing[1]);
        plan.survivorTriggerStaticActorCore = staticActorCore(3);
        plan.survivorTriggerStaticShapeCore = staticShapeCore(3);
        plan.survivorMarkerStaticActorCore = staticActorCore(4);
        plan.survivorMarkerStaticShapeCore = staticShapeCore(4);
        plan.survivorMarkerDynamicShapeCore = dynamicShapeCore(1);
        plan.targetSurvivorTriggerPoolSlot = survivor->poolSlot;
        plan.targetSurvivorMarkerPoolSlot = a.markers[0].poolSlot;
        const auto role = [&](const AuxPairKey& key) -> PxU32 {
            if (key == missing[0]->pair) return 0;
            if (key == missing[1]->pair) return 1;
            if (key == survivor->pair) return 2;
            if (key == a.markers[0].pair) return 3;
            require(false, "unknown mixed interaction role");
            return 0xffffffffu;
        };
        require(a.sceneOrders[1].pairs.size() == 3,
                "mixed A scene trigger order size");
        for (PxU32 i = 0; i < 3; ++i)
            plan.targetSceneTriggerOrder[i] =
                role(a.sceneOrders[1].pairs[i]);
        bool foundDynamicOrder = false;
        for (const auto& actor : a.actorOrders)
            if (actor.actorId == kMixedDynamicId)
            {
                require(actor.pairs.size() == 4,
                        "mixed A dynamic interaction count");
                for (PxU32 i = 0; i < 4; ++i)
                    plan.targetDynamicActorOrder[i] = role(actor.pairs[i]);
                foundDynamicOrder = true;
            }
        require(foundDynamicOrder, "mixed dynamic actor order missing");
        return plan;
    }

    void restore(const State& checkpoint,
                 const TriggerRewindMixedPlanV2& plan)
    {
        std::string error;
        require(oc2::offline::RestoreBodies(*scene, checkpoint.body, error),
                "mixed restore body: " + error);
        require(oc2::offline::RestoreSap(*scene, checkpoint.sap, error),
                "mixed restore SAP: " + error);
        require(oc2::offline::RestoreSceneClock(*scene, checkpoint.clock,
                                                error),
                "mixed restore clock: " + error);
        const std::uint32_t result =
            oc2_physx333_trigger_recreate_mixed_v2(nphase(), &plan);
        require(result == TriggerRewindSuccess,
                "mixed native trigger reconstruction rejected: " +
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

void checkMixedEvents(const std::vector<Event>& events, PxU32 flags,
                      bool includeSurvivor)
{
    require(events.size() == (includeSurvivor ? 3u : 2u),
            "mixed trigger callback count");
    bool seen[3] = {};
    for (const auto& event : events)
    {
        require(event.staticId >= 1 &&
                event.staticId <= (includeSurvivor ? 3u : 2u) &&
                event.otherId == kMixedDynamicId && event.flags == flags &&
                !seen[event.staticId - 1],
                "mixed trigger callback identity/payload");
        seen[event.staticId - 1] = true;
    }
    require(seen[0] && seen[1] && (!includeSurvivor || seen[2]),
            "mixed trigger callback set");
}

void runMixedCase(Runtime& rt, bool warm)
{
    MixedWorld world(rt);
    checkMixedEvents(world.step(0.0f),
                     PxPairFlag::eNOTIFY_TOUCH_FOUND, true);
    require(world.step(0.0f).empty(), "mixed settled A callbacks");
    if (warm)
    {
        checkMixedEvents(world.step(3.0f),
                         PxPairFlag::eNOTIFY_TOUCH_LOST, false);
        checkMixedEvents(world.step(0.0f),
                         PxPairFlag::eNOTIFY_TOUCH_FOUND, false);
        require(world.step(0.0f).empty(), "mixed warm A callbacks");
    }
    const State a = world.capture(warm ? "mixed warm A" : "mixed cold A");
    require(a.aux.triggers.size() == 3 &&
            a.aux.markers.size() == 1 &&
            a.aux.triggerPool.usedCount == 3 &&
            a.aux.markerPool.usedCount == 1 &&
            a.aux.sceneOrders.size() == 3 &&
            a.aux.sceneOrders[1].activeCount == 3 &&
            a.aux.sceneOrders[2].pairs.size() == 1,
            "mixed A is not 3 triggers plus 1 marker");
    const TriggerRewindMixedPlanV2 plan = world.makePlan(a.aux);
    State firstB;
    std::vector<Event> firstBEvents;
    for (unsigned cycle = 0; cycle < 100; ++cycle)
    {
        const auto bEvents = world.step(3.0f);
        checkMixedEvents(bEvents, PxPairFlag::eNOTIFY_TOUCH_LOST, false);
        const State b = world.capture("mixed B");
        require(b.aux.triggers.size() == 1 &&
                b.aux.markers.size() == 1 &&
                b.aux.triggerPool.usedCount == 1 &&
                b.aux.markerPool.usedCount == 1 &&
                b.aux.triggers[0].pair.shape0.actorId == 3 &&
                b.aux.triggerPool.freeOrder.size() == 31 &&
                b.aux.markers[0].pair == a.aux.markers[0].pair &&
                b.aux.markers[0].poolSlot == a.aux.markers[0].poolSlot,
                "mixed B did not preserve exactly the trigger and marker");
        if (cycle == 0)
        {
            // PhysX appends the two newly created trigger interactions to
            // the survivor scene array and the dynamic actor's mixed array.
            // Require this fixture to need a real order repair at both sites.
            require(plan.targetSceneTriggerOrder[0] != 2 ||
                    plan.targetSceneTriggerOrder[1] != 0 ||
                    plan.targetSceneTriggerOrder[2] != 1,
                    "mixed scene order repair was not exercised");
            bool foundDynamicOrder = false;
            for (const auto& actor : b.aux.actorOrders)
                if (actor.actorId == kMixedDynamicId)
                {
                    require(actor.pairs.size() == 2,
                            "mixed B dynamic actor survivor count");
                    const PxU32 firstRole =
                        actor.pairs[0] == b.aux.triggers[0].pair ? 2u :
                        actor.pairs[0] == b.aux.markers[0].pair ? 3u : 99u;
                    const PxU32 secondRole =
                        actor.pairs[1] == b.aux.triggers[0].pair ? 2u :
                        actor.pairs[1] == b.aux.markers[0].pair ? 3u : 99u;
                    require(firstRole < 4 && secondRole < 4 &&
                            firstRole != secondRole,
                            "mixed B actor order has unknown survivor");
                    require(plan.targetDynamicActorOrder[0] != firstRole ||
                            plan.targetDynamicActorOrder[1] != secondRole ||
                            plan.targetDynamicActorOrder[2] != 0 ||
                            plan.targetDynamicActorOrder[3] != 1,
                            "mixed actor order repair was not exercised");
                    foundDynamicOrder = true;
                }
            require(foundDynamicOrder, "mixed B dynamic actor order absent");
            firstB = b;
            firstBEvents = bEvents;
            TriggerRewindMixedPlanV2 bad = plan;
            bad.targetSceneTriggerOrder[0] =
                bad.targetSceneTriggerOrder[1];
            require(oc2_physx333_trigger_recreate_mixed_v2(
                        world.nphase(), &bad) == TriggerRewindInvalidInput,
                    "mixed invalid scene order was accepted");
            bad = plan;
            bad.missing[0].targetPoolSlot = 31;
            require(oc2_physx333_trigger_recreate_mixed_v2(
                        world.nphase(), &bad) == TriggerRewindPoolMismatch,
                    "mixed wrong free-head slot was accepted");
            bad = plan;
            bad.survivorMarkerStaticShapeCore =
                plan.survivorTriggerStaticShapeCore;
            require(oc2_physx333_trigger_recreate_mixed_v2(
                        world.nphase(), &bad) != TriggerRewindSuccess,
                    "mixed wrong marker shape identity was accepted");
            checkEqual(b, world.capture("mixed after reject"),
                       "mixed atomic rejection");
        }
        else
        {
            checkEqual(firstB, b, "mixed replayed B");
            require(firstBEvents == bEvents,
                    "mixed replayed B ordered callbacks");
        }
        world.restore(a, plan);
        checkEqual(a, world.capture("mixed restored A"),
                   "mixed restored A");
    }
    std::cout << "PASS " << (warm ? "warm" : "cold")
              << " mixed survivor trigger/marker plus two recreated "
                 "triggers, 100 exact A and replay B cycles\n";
}

} // namespace

int main()
{
    Runtime runtime;
    runCase(runtime, false);
    runCase(runtime, true);
    runMixedCase(runtime, false);
    runMixedCase(runtime, true);
    require(runtime.errors.count == 0, "PhysX reported errors");
    return 0;
}
