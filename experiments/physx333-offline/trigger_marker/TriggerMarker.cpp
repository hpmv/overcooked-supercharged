// Source-built PhysX 3.3.3 contact/trigger/marker interaction baseline.
// This test does not launch Unity or Overcooked and never mutates SDK internals.
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <set>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "../oracle/Oracle.h"
#include "../aux_interactions/AuxInteractionImage.h"

using namespace physx;

namespace {

const PxU32 kContactCount = 12;
const PxU32 kMoverId = 13;
const PxU32 kMarkerFilterTag = 1;
const PxReal kStep = 1.0f / 60.0f;

void fail(const std::string& reason)
{
    std::cerr << "FAIL " << reason << '\n';
    std::exit(1);
}

PxU32 id(const PxActor* actor)
{
    return static_cast<PxU32>(
        reinterpret_cast<std::uintptr_t>(actor->userData));
}

struct ErrorCallback : PxErrorCallback
{
    virtual void reportError(PxErrorCode::Enum code, const char* message,
                             const char* file, int line)
    {
        std::cerr << "PhysX " << static_cast<int>(code) << ": " << message
                  << " (" << file << ':' << line << ")\n";
        if (code == PxErrorCode::eINVALID_PARAMETER ||
            code == PxErrorCode::eINVALID_OPERATION ||
            code == PxErrorCode::eOUT_OF_MEMORY)
            fail("PhysX rejected the fixture");
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

PxFilterFlags fixtureFilter(PxFilterObjectAttributes a0, PxFilterData d0,
                            PxFilterObjectAttributes a1, PxFilterData d1,
                            PxPairFlags& pairFlags, const void*, PxU32)
{
    // PhysX 3.3.3 ScNPhaseCore::getRbElementInteractionType maps SUPPRESS,
    // unlike KILL, to a live PX_INTERACTION_TYPE_MARKER object.
    if (d0.word0 == kMarkerFilterTag || d1.word0 == kMarkerFilterTag)
    {
        pairFlags = PxPairFlags();
        return PxFilterFlag::eSUPPRESS;
    }
    if (PxFilterObjectIsTrigger(a0) || PxFilterObjectIsTrigger(a1))
    {
        pairFlags = PxPairFlag::eTRIGGER_DEFAULT;
        return PxFilterFlag::eDEFAULT;
    }
    pairFlags = PxPairFlag::eCONTACT_DEFAULT |
                PxPairFlag::eNOTIFY_TOUCH_FOUND |
                PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
                PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct Event
{
    char kind = '?';
    PxU32 staticId = 0;
    PxU32 otherId = 0;
    PxU32 flags = 0;
    PxU32 contacts = 0;

    bool operator==(const Event& b) const
    {
        return kind == b.kind && staticId == b.staticId &&
               otherId == b.otherId && flags == b.flags &&
               contacts == b.contacts;
    }
};

struct Events : PxSimulationEventCallback
{
    std::vector<Event> rows;
    virtual void onConstraintBreak(PxConstraintInfo*, PxU32) {}
    virtual void onWake(PxActor**, PxU32) {}
    virtual void onSleep(PxActor**, PxU32) {}
    virtual void onContact(const PxContactPairHeader& header,
                           const PxContactPair* pairs, PxU32 count)
    {
        for (PxU32 i = 0; i < count; ++i)
        {
            Event e;
            e.kind = 'C';
            e.staticId = PxMin(id(header.actors[0]), id(header.actors[1]));
            e.otherId = PxMax(id(header.actors[0]), id(header.actors[1]));
            e.flags = static_cast<PxU16>(pairs[i].events);
            e.contacts = pairs[i].contactCount;
            rows.push_back(e);
        }
    }
    virtual void onTrigger(PxTriggerPair* pairs, PxU32 count)
    {
        for (PxU32 i = 0; i < count; ++i)
        {
            Event e;
            e.kind = 'T';
            e.staticId = id(pairs[i].triggerActor);
            e.otherId = id(pairs[i].otherActor);
            e.flags = static_cast<PxU32>(pairs[i].status);
            rows.push_back(e);
        }
    }
};

struct Runtime
{
    PxDefaultAllocator allocator;
    ErrorCallback errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = NULL;
    PxPhysics* physics = NULL;
    PxMaterial* material = NULL;

    Runtime()
    {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        if (!foundation) fail("PxCreateFoundation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        if (!physics) fail("PxCreatePhysics");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        if (!material) fail("createMaterial");
    }
    ~Runtime()
    {
        material->release();
        physics->release();
        foundation->release();
    }
};

struct Snapshot
{
    PxU32 contacts = 0;
    PxU32 triggers = 0;
    PxU32 markers = 0;
    PxU32 contactPool = 0;
    PxU32 triggerPool = 0;
    PxU32 markerPool = 0;
    std::vector<Event> events;
};

const std::vector<std::uint32_t>& section(
    const physx333_offline::OracleImage& image, const char* name)
{
    const auto found = image.parts.find(name);
    if (found == image.parts.end()) fail(std::string("missing Oracle ") + name);
    return found->second;
}

struct World
{
    Runtime& runtime;
    Events callback;
    PxScene* scene = NULL;
    PxRigidDynamic* mover = NULL;
    std::vector<PxRigidActor*> actors;

    explicit World(Runtime& rt) : runtime(rt)
    {
        PxSceneDesc desc(rt.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &rt.dispatcher;
        desc.filterShader = fixtureFilter;
        desc.simulationEventCallback = &callback;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        scene = rt.physics->createScene(desc);
        if (!scene) fail("createScene");

        for (PxU32 i = 0; i < kContactCount; ++i)
            addStatic(i + 1, i, i < 8 ? 0.0f : 0.9f, false, false);
        const PxU32 triggerAt[4] = {0, 1, 8, 9};
        for (PxU32 i = 0; i < 4; ++i)
            addStatic(14 + i, triggerAt[i], i < 2 ? 0.0f : 0.9f,
                      true, false);
        for (PxU32 i = 0; i < 2; ++i)
            addStatic(18 + i, 2 + i, 0.0f, false, true);

        mover = rt.physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f, 0.95f, 0.0f)));
        if (!mover) fail("createRigidDynamic");
        for (PxU32 i = 0; i < kContactCount; ++i)
        {
            PxShape* shape = rt.physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *rt.material);
            if (!shape) fail("create mover shape");
            mover->attachShape(*shape);
            shape->setLocalPose(PxTransform(PxVec3(
                static_cast<PxReal>(i) * 3.0f, 0.0f, 0.0f)));
            shape->release();
        }
        mover->setMass(12.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(36.0f, 36.0f, 36.0f));
        mover->setLinearDamping(0.0f);
        mover->setAngularDamping(0.0f);
        mover->userData = reinterpret_cast<void*>(
            static_cast<std::uintptr_t>(kMoverId));
        scene->addActor(*mover);
        actors.push_back(mover);
    }

    void addStatic(PxU32 actorId, PxU32 shapeIndex, PxReal z,
                   bool trigger, bool marker)
    {
        PxRigidStatic* fixed = runtime.physics->createRigidStatic(
            PxTransform(PxVec3(static_cast<PxReal>(shapeIndex) * 3.0f,
                               0.0f, z)));
        if (!fixed) fail("createRigidStatic");
        PxShape* shape = runtime.physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *runtime.material);
        if (!shape) fail("create static shape");
        if (trigger)
        {
            shape->setFlag(PxShapeFlag::eSIMULATION_SHAPE, false);
            shape->setFlag(PxShapeFlag::eTRIGGER_SHAPE, true);
        }
        if (marker)
        {
            PxFilterData data;
            data.word0 = kMarkerFilterTag;
            shape->setSimulationFilterData(data);
        }
        fixed->attachShape(*shape);
        shape->release();
        fixed->userData = reinterpret_cast<void*>(
            static_cast<std::uintptr_t>(actorId));
        scene->addActor(*fixed);
        actors.push_back(fixed);
    }

    ~World()
    {
        for (PxRigidActor* actor : actors) actor->release();
        scene->release();
    }

    Snapshot step(PxReal z)
    {
        callback.rows.clear();
        mover->setGlobalPose(PxTransform(PxVec3(0.0f, 0.95f, z)));
        mover->setLinearVelocity(PxVec3(0.0f));
        mover->setAngularVelocity(PxVec3(0.0f));
        scene->simulate(kStep);
        if (!scene->fetchResults(true)) fail("fetchResults");
        physx333_offline::OracleImage image;
        std::string error;
        if (!physx333_offline::CaptureOracle(*scene, image, error))
            fail("CaptureOracle: " + error);
        const std::vector<std::uint32_t>& counts =
            section(image, "nphase.interaction_counts");
        if (counts.size() < 8) fail("short interaction counts");
        const std::vector<std::uint32_t>& contactPool =
            section(image, "nphase.pool.shape_pair.header");
        const std::vector<std::uint32_t>& triggerPool =
            section(image, "nphase.pool.trigger.header");
        const std::vector<std::uint32_t>& markerPool =
            section(image, "nphase.pool.marker.header");
        if (contactPool.size() < 3 || triggerPool.size() < 3 ||
            markerPool.size() < 3)
            fail("short interaction pool headers");
        Snapshot out;
        out.contacts = counts[0];
        out.triggers = counts[4];
        out.markers = counts[6];
        out.contactPool = contactPool[2];
        out.triggerPool = triggerPool[2];
        out.markerPool = markerPool[2];
        out.events = callback.rows;
        return out;
    }
};

void print(const char* name, const Snapshot& s)
{
    std::cout << name << " interactions=" << s.contacts << '/' <<
        s.triggers << '/' << s.markers << " pools=" << s.contactPool <<
        '/' << s.triggerPool << '/' << s.markerPool << " events=" <<
        s.events.size() << '\n';
    for (std::size_t i = 0; i < s.events.size(); ++i)
    {
        const Event& e = s.events[i];
        std::cout << name << " event[" << i << "]=" << e.kind << ':' <<
            e.staticId << "/" << e.otherId << ":flags=" << e.flags <<
            ":contacts=" << e.contacts << '\n';
    }
}

void checkCounts(const Snapshot& s, PxU32 contacts, PxU32 triggers,
                 PxU32 markers, const char* stage)
{
    if (s.contacts != contacts || s.triggers != triggers ||
        s.markers != markers || s.contactPool != contacts ||
        s.triggerPool != triggers || s.markerPool != markers)
        fail(std::string(stage) + " interaction/pool counts differ");
}

void checkSuccessorEvents(const Snapshot& s)
{
    if (s.events.size() != 14)
        fail("successor callback count differs");
    for (PxU32 i = 0; i < 14; ++i)
    {
        const Event& e = s.events[i];
        const bool isTrigger = i < 2;
        const bool isLost = i < 6;
        const PxU32 wantedId = isTrigger ? 16 + i :
            (isLost ? 9 + i - 2 : 1 + i - 6);
        const PxU32 wantedFlag = isLost ?
            PxPairFlag::eNOTIFY_TOUCH_LOST :
            PxPairFlag::eNOTIFY_TOUCH_PERSISTS;
        if (e.kind != (isTrigger ? 'T' : 'C') ||
            e.staticId != wantedId || e.otherId != kMoverId ||
            !(e.flags & wantedFlag))
            fail("successor callback order differs at row " +
                 std::to_string(i));
    }
    PxU32 lostContacts = 0, persistentContacts = 0, lostTriggers = 0;
    std::set<PxU32> lostContactIds, persistentContactIds, lostTriggerIds;
    for (const Event& e : s.events)
    {
        if (e.otherId != kMoverId) fail("callback refers to wrong mover");
        if (e.kind == 'C')
        {
            if (e.flags & PxPairFlag::eNOTIFY_TOUCH_LOST)
            {
                ++lostContacts;
                lostContactIds.insert(e.staticId);
            }
            else if (e.flags & PxPairFlag::eNOTIFY_TOUCH_PERSISTS)
            {
                ++persistentContacts;
                persistentContactIds.insert(e.staticId);
            }
            else fail("unexpected successor contact event");
        }
        else if (e.kind == 'T' &&
                 (e.flags & PxPairFlag::eNOTIFY_TOUCH_LOST))
        {
            ++lostTriggers;
            lostTriggerIds.insert(e.staticId);
        }
        else fail("unexpected successor trigger event");
    }
    if (lostContacts != 4 || persistentContacts != 8 || lostTriggers != 2 ||
        lostContactIds != std::set<PxU32>({9, 10, 11, 12}) ||
        persistentContactIds != std::set<PxU32>({1, 2, 3, 4, 5, 6, 7, 8}) ||
        lostTriggerIds != std::set<PxU32>({16, 17}))
        fail("successor callback ownership/event pattern differs");
}

physx333_offline::AuxInteractionImage captureAux(World& world,
                                                  const char* stage)
{
    physx333_offline::AuxInteractionImage image;
    std::string error;
    if (!physx333_offline::CaptureAuxInteractionImage(
            *world.scene, image, error))
        fail(std::string(stage) + " auxiliary capture: " + error);
    return image;
}

PxU32 staticId(const physx333_offline::AuxPairKey& pair)
{
    if (pair.shape0.actorId == kMoverId)
        return pair.shape1.actorId;
    if (pair.shape1.actorId == kMoverId)
        return pair.shape0.actorId;
    fail("auxiliary pair has no fixture mover");
    return 0;
}

void checkAuxDelta(const physx333_offline::AuxInteractionImage& a,
                   const physx333_offline::AuxInteractionImage& b)
{
    if (a.sceneOrders.size() != 3 || b.sceneOrders.size() != 3 ||
        a.sceneOrders[0].pairs.size() != 12 ||
        b.sceneOrders[0].pairs.size() != 8 ||
        a.sceneOrders[1].pairs.size() != 4 ||
        b.sceneOrders[1].pairs.size() != 2 ||
        a.sceneOrders[2].pairs.size() != 2 ||
        b.sceneOrders[2].pairs.size() != 2 ||
        a.triggers.size() != 4 || b.triggers.size() != 2 ||
        a.markers.size() != 2 || b.markers.size() != 2 ||
        a.triggerPool.usedCount != 4 || b.triggerPool.usedCount != 2 ||
        a.markerPool.usedCount != 2 || b.markerPool.usedCount != 2)
        fail("auxiliary 12/4/2 to 8/2/2 inventory differs");
    if (a.sceneOrders[2].pairs != b.sceneOrders[2].pairs ||
        !(a.markerPool == b.markerPool))
        fail("surviving marker order or pool changed");
    for (PxU32 i = 0; i < 4; ++i)
        if (staticId(a.triggers[i].pair) != 14 + i ||
            !a.triggers[i].lastFrameHadContacts)
            fail("checkpoint trigger identity/touch state differs");
    for (PxU32 i = 0; i < 2; ++i)
        if (staticId(b.triggers[i].pair) != 14 + i ||
            !(b.triggers[i].pair == a.triggers[i].pair) ||
            b.triggers[i].poolSlot != a.triggers[i].poolSlot ||
            !b.triggers[i].lastFrameHadContacts ||
            !(b.markers[i].pair == a.markers[i].pair) ||
            b.markers[i].poolSlot != a.markers[i].poolSlot)
            fail("auxiliary survivor identity/slot differs");
    if (b.triggerPool.freeOrder.size() !=
            a.triggerPool.freeOrder.size() + 2 ||
        !std::equal(a.triggerPool.freeOrder.begin(),
                    a.triggerPool.freeOrder.end(),
                    b.triggerPool.freeOrder.begin() + 2))
        fail("trigger free-chain successor tail differs");
    std::set<PxU32> releasedSlots = {
        a.triggers[2].poolSlot, a.triggers[3].poolSlot
    };
    if (releasedSlots != std::set<PxU32>({
            b.triggerPool.freeOrder[0], b.triggerPool.freeOrder[1]}))
        fail("trigger free-chain head is not the two deleted pairs");
    bool foundMoverA = false, foundMoverB = false;
    for (const auto& actor : a.actorOrders)
        if (actor.actorId == kMoverId)
        {
            foundMoverA = true;
            if (actor.pairs.size() != 18)
                fail("checkpoint mover interaction order is not 18 entries");
        }
    for (const auto& actor : b.actorOrders)
        if (actor.actorId == kMoverId)
        {
            foundMoverB = true;
            if (actor.pairs.size() != 12)
                fail("successor mover interaction order is not 12 entries");
        }
    if (!foundMoverA || !foundMoverB)
        fail("auxiliary mover actor order is absent");
}

void printAux(const char* name,
              const physx333_offline::AuxInteractionImage& image)
{
    std::cout << name << " aux trigger_pool=" <<
        image.triggerPool.usedCount << '/' <<
        image.triggerPool.freeOrder.size() <<
        " marker_pool=" << image.markerPool.usedCount << '/' <<
        image.markerPool.freeOrder.size() << '\n';
    for (std::size_t i = 0; i < image.triggers.size(); ++i)
    {
        const auto& row = image.triggers[i];
        std::cout << name << " trigger[" << i << "] static=" <<
            staticId(row.pair) << " pool_slot=" << row.poolSlot <<
            " actor_indices=" << row.actorIndex0 << '/' <<
            row.actorIndex1 << " flags=" << row.triggerFlags <<
            " touch=" << row.lastFrameHadContacts <<
            " cache_state=" << row.triggerCacheState << '\n';
    }
    for (std::size_t i = 0; i < image.markers.size(); ++i)
        std::cout << name << " marker[" << i << "] static=" <<
            staticId(image.markers[i].pair) << " pool_slot=" <<
            image.markers[i].poolSlot << '\n';
}

} // namespace

int main()
{
    Runtime runtime;
    World first(runtime);
    const Snapshot entry = first.step(0.0f);
    const Snapshot checkpoint = first.step(0.0f);
    const physx333_offline::AuxInteractionImage checkpointAux =
        captureAux(first, "A");
    const Snapshot successor = first.step(-0.2f);
    const physx333_offline::AuxInteractionImage successorAux =
        captureAux(first, "B");
    print("ENTRY", entry);
    print("A", checkpoint);
    print("B", successor);
    printAux("A", checkpointAux);
    printAux("B", successorAux);
    checkCounts(checkpoint, 12, 4, 2, "A");
    checkCounts(successor, 8, 2, 2, "B");
    checkSuccessorEvents(successor);
    checkAuxDelta(checkpointAux, successorAux);

    // A second fresh scene is an ordered callback and interaction baseline.
    World fresh(runtime);
    const Snapshot freshEntry = fresh.step(0.0f);
    const Snapshot freshA = fresh.step(0.0f);
    const physx333_offline::AuxInteractionImage freshAuxA =
        captureAux(fresh, "fresh A");
    const Snapshot freshB = fresh.step(-0.2f);
    const physx333_offline::AuxInteractionImage freshAuxB =
        captureAux(fresh, "fresh B");
    std::string auxDifference;
    if (!checkpointAux.equals(freshAuxA, auxDifference))
        fail("fresh-scene A auxiliary image: " + auxDifference);
    if (!successorAux.equals(freshAuxB, auxDifference))
        fail("fresh-scene B auxiliary image: " + auxDifference);
    if (freshEntry.events != entry.events ||
        freshA.events != checkpoint.events ||
        freshB.events != successor.events ||
        freshA.contacts != checkpoint.contacts ||
        freshA.triggers != checkpoint.triggers ||
        freshA.markers != checkpoint.markers ||
        freshB.contacts != successor.contacts ||
        freshB.triggers != successor.triggers ||
        freshB.markers != successor.markers)
        fail("fresh-scene ordered events or interaction counts differ");
    std::cout << "PASS 12/4/2 -> 8/2/2, fresh-scene ordered events, "
                 "and full auxiliary images\n";
    return 0;
}
