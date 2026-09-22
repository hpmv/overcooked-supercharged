// Offline PhysX 3.3.3 motion/sleep checkpoint matrix. This runs the pinned,
// source-built Win32 DLL and never loads Unity or the game.
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <map>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "ScBodySim.h"
#include "../body/BodyImage.h"
#include "../island/IslandImage.h"
#include "../scene_clock/SceneClockImage.h"

using namespace physx;
using oc2::offline::BodyImage;
using oc2::offline::IslandImage;
using oc2::offline::SceneClockImage;

namespace {

const PxReal kStep = 1.0f / 60.0f;

void require(bool ok, const std::string& message)
{
    if (!ok)
    {
        std::cerr << "FAIL " << message << '\n';
        std::exit(1);
    }
}

PxU32 word(PxReal f)
{
    PxU32 value = 0;
    static_assert(sizeof(value) == sizeof(f), "PhysX floats must be 32-bit");
    std::memcpy(&value, &f, sizeof(value));
    return value;
}

struct Errors : PxErrorCallback
{
    PxU32 count = 0;
    virtual void reportError(PxErrorCode::Enum code, const char* message,
                             const char* file, int line)
    {
        ++count;
        std::cerr << "PhysX " << int(code) << ": " << message
                  << " (" << file << ':' << line << ")\n";
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
                PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
                PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct Events : PxSimulationEventCallback
{
    std::vector<PxU32> rows;
    virtual void onConstraintBreak(PxConstraintInfo*, PxU32) {}
    virtual void onWake(PxActor** actors, PxU32 count)
    {
        rows.push_back(1);
        rows.push_back(count);
        for (PxU32 i = 0; i < count; ++i)
            rows.push_back(PxU32(reinterpret_cast<uintptr_t>(actors[i]->userData)));
    }
    virtual void onSleep(PxActor** actors, PxU32 count)
    {
        rows.push_back(2);
        rows.push_back(count);
        for (PxU32 i = 0; i < count; ++i)
            rows.push_back(PxU32(reinterpret_cast<uintptr_t>(actors[i]->userData)));
    }
    virtual void onTrigger(PxTriggerPair*, PxU32) {}
    virtual void onContact(const PxContactPairHeader& header,
                           const PxContactPair* pairs, PxU32 count)
    {
        for (PxU32 i = 0; i < count; ++i)
        {
            rows.push_back(3);
            rows.push_back(PxU32(reinterpret_cast<uintptr_t>(header.actors[0]->userData)));
            rows.push_back(PxU32(reinterpret_cast<uintptr_t>(header.actors[1]->userData)));
            rows.push_back(static_cast<PxU16>(pairs[i].events));
            rows.push_back(pairs[i].contactCount);
        }
    }
};

struct PublicBody
{
    PxTransform pose;
    PxVec3 linear;
    PxVec3 angular;
    PxReal wakeCounter;
    bool sleeping;
};

struct Digest
{
    std::map<std::string, std::vector<PxU32> > sections;

    void add(const char* name, PxU32 value) { sections[name].push_back(value); }
    void add(const char* name, PxReal value) { add(name, word(value)); }

    bool equals(const Digest& other, std::string& firstDifference) const
    {
        firstDifference.clear();
        if (sections.size() != other.sections.size())
        {
            firstDifference = "section count";
            return false;
        }
        std::map<std::string, std::vector<PxU32> >::const_iterator a = sections.begin();
        std::map<std::string, std::vector<PxU32> >::const_iterator b = other.sections.begin();
        for (; a != sections.end(); ++a, ++b)
        {
            if (a->first != b->first || a->second.size() != b->second.size())
            {
                firstDifference = a->first + " size/schema";
                return false;
            }
            for (size_t i = 0; i < a->second.size(); ++i)
                if (a->second[i] != b->second[i])
                {
                    firstDifference = a->first + '[' + std::to_string(i) +
                                      "] expected=" + std::to_string(a->second[i]) +
                                      " replay=" + std::to_string(b->second[i]);
                    return false;
                }
        }
        return true;
    }
};

struct Fixture
{
    PxDefaultAllocator allocator;
    Errors errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;
    PxScene* scene = nullptr;
    PxRigidStatic* floor = nullptr;
    PxRigidDynamic* mover = nullptr;
    Events events;
    bool floorContact;

    explicit Fixture(bool withFloor) : floorContact(withFloor)
    {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        require(foundation != nullptr, "foundation creation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        require(physics != nullptr, "physics creation");
        material = physics->createMaterial(0.9f, 0.9f, 0.0f);
        require(material != nullptr, "material creation");
        PxSceneDesc desc(physics->getTolerancesScale());
        desc.gravity = withFloor ? PxVec3(0.0f, -9.81f, 0.0f) : PxVec3(0.0f);
        desc.cpuDispatcher = &dispatcher;
        desc.filterShader = contactFilter;
        desc.simulationEventCallback = &events;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        scene = physics->createScene(desc);
        require(scene != nullptr, "scene creation");

        if (withFloor)
        {
            floor = physics->createRigidStatic(PxTransform(PxVec3(0.0f, -0.5f, 0.0f)));
            require(floor != nullptr, "floor creation");
            PxShape* shape = physics->createShape(
                PxBoxGeometry(15.0f, 0.5f, 15.0f), *material);
            require(shape != nullptr, "floor shape creation");
            floor->attachShape(*shape);
            shape->release();
            floor->userData = reinterpret_cast<void*>(uintptr_t(1));
            scene->addActor(*floor);
        }
        mover = physics->createRigidDynamic(PxTransform(
            PxVec3(0.0f, withFloor ? 0.5f : 1.0f, 0.0f)));
        require(mover != nullptr, "mover creation");
        PxShape* shape = physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *material);
        require(shape != nullptr, "mover shape creation");
        mover->attachShape(*shape);
        shape->release();
        mover->setMass(1.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(1.0f));
        mover->setLinearDamping(0.0f);
        mover->setAngularDamping(0.0f);
        mover->userData = reinterpret_cast<void*>(uintptr_t(2));
        scene->addActor(*mover);
    }

    ~Fixture()
    {
        mover->release();
        if (floor) floor->release();
        scene->release();
        material->release();
        physics->release();
        foundation->release();
    }

    void step()
    {
        events.rows.clear();
        scene->simulate(kStep);
        require(scene->fetchResults(true), "fetchResults");
        require(errors.count == 0, "PhysX reported an error");
    }

    PublicBody body() const
    {
        PublicBody result = { mover->getGlobalPose(), mover->getLinearVelocity(),
                              mover->getAngularVelocity(),
                              mover->getWakeCounter(), mover->isSleeping() };
        return result;
    }

    void restorePublic(const PublicBody& saved)
    {
        // Deliberately limited to documented PxRigidDynamic calls, including
        // the public sleep/wake operations. This is a negative control.
        if (!saved.sleeping) mover->wakeUp();
        mover->setGlobalPose(saved.pose);
        mover->setLinearVelocity(saved.linear);
        mover->setAngularVelocity(saved.angular);
        mover->setWakeCounter(saved.wakeCounter);
        if (saved.sleeping) mover->putToSleep();
        require(errors.count == 0, "public restore reported a PhysX error");
    }

    Digest digest() const
    {
        Digest d;
        const PublicBody b = body();
        d.add("body", b.pose.p.x); d.add("body", b.pose.p.y); d.add("body", b.pose.p.z);
        d.add("body", b.pose.q.x); d.add("body", b.pose.q.y);
        d.add("body", b.pose.q.z); d.add("body", b.pose.q.w);
        d.add("body", b.linear.x); d.add("body", b.linear.y); d.add("body", b.linear.z);
        d.add("body", b.angular.x); d.add("body", b.angular.y); d.add("body", b.angular.z);
        d.add("body", b.wakeCounter); d.add("body", PxU32(b.sleeping));
        d.add("events.length", PxU32(events.rows.size()));
        for (PxU32 value : events.rows) d.add("events.rows", value);
        PxSimulationStatistics stats;
        scene->getSimulationStatistics(stats);
        d.add("stats", stats.nbActiveDynamicBodies);
        d.add("stats", stats.nbDynamicBodies);
        for (PxU32 i = 0; i < PxGeometryType::eGEOMETRY_COUNT; ++i)
            for (PxU32 j = 0; j < PxGeometryType::eGEOMETRY_COUNT; ++j)
                d.add("contact_pairs", stats.nbDiscreteContactPairs[i][j]);
        return d;
    }

    PxU32 boxContactPairs() const
    {
        PxSimulationStatistics stats;
        scene->getSimulationStatistics(stats);
        return stats.nbDiscreteContactPairs[PxGeometryType::eBOX]
                                           [PxGeometryType::eBOX];
    }
};

struct Checkpoint
{
    PublicBody publicBody;
    BodyImage bodies;
    IslandImage island;
    SceneClockImage clock;
};

Checkpoint capture(Fixture& f)
{
    Checkpoint saved;
    saved.publicBody = f.body();
    std::string error;
    const bool bodyOk = oc2::offline::CaptureBodies(*f.scene, saved.bodies, error);
    require(bodyOk, "body capture: " + error);
    const bool islandOk = oc2::offline::CaptureIsland(*f.scene, saved.island, error);
    require(islandOk, "island capture: " + error);
    const bool clockOk = oc2::offline::CaptureSceneClock(*f.scene, saved.clock, error);
    require(clockOk, "scene clock capture: " + error);
    return saved;
}

const BodyImage::Field* bodyField(const BodyImage& image,
                                  const std::string& name)
{
    for (const BodyImage::Field& field : image.fields)
        if (field.name == name) return &field;
    return nullptr;
}

bool containsCore(const SceneClockImage::PointerArray& row,
                  std::uintptr_t core)
{
    return std::find(row.values.begin(), row.values.end(), core) !=
           row.values.end();
}

bool restoreSleepMarkersThenBodies(Fixture& f,
                                   const Checkpoint& saved,
                                   std::string& error)
{
    // SceneClockImage has already restored the scene's sleep/wake lists.
    // BodyImage intentionally refuses to invent the corresponding BodySim
    // marker flags. This fixture-only bridge validates the saved relation
    // before patching those four bits, then delegates all other body data to
    // BodyImage. It never changes allocation or actor topology.
    if (saved.clock.arrays.size() < 2 ||
        saved.clock.arrays[0].name != "Sc.sleepBodies" ||
        saved.clock.arrays[1].name != "Sc.wokeBodies" ||
        saved.bodies.actors.size() != 1)
    {
        error = "unsupported sleep-list schema or actor count";
        return false;
    }
    BodyImage live;
    if (!oc2::offline::CaptureBodies(*f.scene, live, error)) return false;
    const BodyImage::Actor& a = saved.bodies.actors[0];
    const BodyImage::Actor& b = live.actors[0];
    if (a.userId != b.userId || a.pxActor != b.pxActor ||
        a.core != b.core || a.sim != b.sim || a.active != b.active ||
        a.shapes != b.shapes)
    {
        error = "actor binding or active state differs after public wake";
        return false;
    }
    const std::string name = "actor[" + std::to_string(a.userId) +
                             "].BodySim.internalFlags";
    const BodyImage::Field* target = bodyField(saved.bodies, name);
    const BodyImage::Field* current = bodyField(live, name);
    if (!target || !current || target->address != current->address ||
        target->bytes.size() != sizeof(PxU16) ||
        current->bytes.size() != sizeof(PxU16))
    {
        error = "BodySim flag location/size changed";
        return false;
    }
    PxU16 savedFlags = 0, liveFlags = 0;
    std::memcpy(&savedFlags, target->bytes.data(), sizeof(savedFlags));
    std::memcpy(&liveFlags, current->bytes.data(), sizeof(liveFlags));
    const PxU16 mask = Sc::BodySim::BF_IS_IN_SLEEP_LIST |
                       Sc::BodySim::BF_IS_IN_WAKEUP_LIST |
                       Sc::BodySim::BF_SLEEP_NOTIFY |
                       Sc::BodySim::BF_WAKEUP_NOTIFY;
    const bool inSleep = containsCore(saved.clock.arrays[0], a.core);
    const bool inWake = containsCore(saved.clock.arrays[1], a.core);
    if (bool(savedFlags & Sc::BodySim::BF_IS_IN_SLEEP_LIST) != inSleep ||
        bool(savedFlags & Sc::BodySim::BF_IS_IN_WAKEUP_LIST) != inWake)
    {
        error = "saved BodySim flags disagree with saved Sc::Scene lists";
        return false;
    }
    const PxU16 patched = PxU16((liveFlags & ~mask) | (savedFlags & mask));
    std::memcpy(reinterpret_cast<void*>(target->address), &patched,
                sizeof(patched));
    if (!oc2::offline::RestoreBodies(*f.scene, saved.bodies, error))
    {
        std::memcpy(reinterpret_cast<void*>(target->address), &liveFlags,
                    sizeof(liveFlags));
        return false;
    }
    return true;
}

void diagnoseRestoreGate(Fixture& f, const Checkpoint& earlier,
                         const std::string& label)
{
    std::string error;
    BodyImage before;
    require(oc2::offline::CaptureBodies(*f.scene, before, error),
            label + " body preflight capture: " + error);
    const bool restoredBody = oc2::offline::RestoreBodies(
        *f.scene, earlier.bodies, error);
    std::cout << label << " BodyImage B->A: "
              << (restoredBody ? "accepted" : "rejected: " + error) << '\n';
    BodyImage after;
    require(oc2::offline::CaptureBodies(*f.scene, after, error),
            label + " body postflight capture: " + error);
    if (!restoredBody)
        require(before.equals(after, error),
                label + " rejected body restore mutated state: " + error);

    IslandImage islandBefore;
    require(oc2::offline::CaptureIsland(*f.scene, islandBefore, error),
            label + " island preflight capture: " + error);
    const bool restoredIsland = oc2::offline::RestoreIsland(
        *f.scene, earlier.island, error);
    std::cout << label << " IslandImage B->A: "
              << (restoredIsland ? "accepted" : "rejected: " + error) << '\n';
    IslandImage islandAfter;
    require(oc2::offline::CaptureIsland(*f.scene, islandAfter, error),
            label + " island postflight capture: " + error);
    if (restoredIsland)
    {
        const bool rolledBack = oc2::offline::RestoreIsland(
            *f.scene, islandBefore, error);
        require(rolledBack, label + " accepted island probe did not roll back: " + error);
        IslandImage reverted;
        const bool recaptured = oc2::offline::CaptureIsland(*f.scene, reverted, error);
        require(recaptured && islandBefore.equals(reverted, error),
                label + " accepted island probe did not restore original: " + error);
    }
    else
        require(islandBefore.equals(islandAfter, error),
                label + " rejected island restore mutated state: " + error);
}

void compareReplay(Fixture& f, const Checkpoint& earlier,
                   const Digest& originalB, const BodyImage& originalBodyB,
                   const std::string& label, bool dash)
{
    f.restorePublic(earlier.publicBody);
    BodyImage publicA;
    std::string error;
    require(oc2::offline::CaptureBodies(*f.scene, publicA, error),
            label + " public A body capture: " + error);
    std::cout << label << " public restore hidden A: "
              << (earlier.bodies.equals(publicA, error) ? "equal" : error) << '\n';
    if (dash) f.mover->setLinearVelocity(PxVec3(8.0f, 0.0f, 0.0f));
    f.step();
    const Digest replayB = f.digest();
    std::cout << label << " public-only next step: "
              << (originalB.equals(replayB, error) ? "equal" : error) << '\n';
    BodyImage replayBodyB;
    require(oc2::offline::CaptureBodies(*f.scene, replayBodyB, error),
            label + " replay B body capture: " + error);
    std::cout << label << " next-step BodyImage: "
              << (originalBodyB.equals(replayBodyB, error) ? "equal" : error) << '\n';
}

void quietSleepMatrix()
{
    Fixture f(false);
    f.mover->setWakeCounter(0.05f);
    // The island manager's creation journals are not settled until the first
    // post-fetch boundary, which is the earliest valid image checkpoint.
    f.step();
    require(!f.mover->isSleeping(), "quiet fixture unexpectedly asleep before stepping");
    Checkpoint beforeSleep;
    bool found = false;
    for (int i = 0; i < 120; ++i)
    {
        beforeSleep = capture(f);
        f.step();
        if (f.mover->isSleeping())
        {
            found = true;
            std::cout << "quiet wake->sleep frame=" << i + 1
                      << " A.wakeCounter=" << beforeSleep.publicBody.wakeCounter
                      << " B.wakeCounter=" << f.mover->getWakeCounter() << '\n';
            break;
        }
    }
    require(found, "no natural wake->sleep transition within 120 frames");
    const Digest sleepingB = f.digest();
    const Checkpoint sleeping = capture(f);
    require(!beforeSleep.publicBody.sleeping && sleeping.publicBody.sleeping,
            "wake->sleep endpoints have wrong active flags");
    diagnoseRestoreGate(f, beforeSleep, "quiet wake->sleep");
    compareReplay(f, beforeSleep, sleepingB, sleeping.bodies,
                  "quiet wake->sleep", false);

    // A distinct sleeping->awake checkpoint, using the same scene but with
    // a fresh stopped sleeping endpoint.
    f.mover->putToSleep();
    f.step();
    const Checkpoint asleepA = capture(f);
    require(asleepA.publicBody.sleeping, "sleep->wake A did not sleep");
    f.mover->setLinearVelocity(PxVec3(8.0f, 0.0f, 0.0f));
    f.step();
    require(!f.mover->isSleeping(), "sleep->wake B did not wake");
    const Digest awakeB = f.digest();
    const Checkpoint awake = capture(f);
    diagnoseRestoreGate(f, asleepA, "quiet sleep->wake");
    compareReplay(f, asleepA, awakeB, awake.bodies,
                  "quiet sleep->wake", true);
}

void frictionDashMatrix()
{
    Fixture f(true);
    // Let the floor manifold settle before the sleeping checkpoint. The
    // same-scene negative control then rewinds the box after one dash step.
    bool asleep = false;
    for (int i = 0; i < 240; ++i)
    {
        f.step();
        if (f.mover->isSleeping())
        {
            asleep = true;
            std::cout << "friction floor settled naturally at frame=" << i + 1 << '\n';
            break;
        }
    }
    require(asleep, "floor box did not settle naturally within 240 frames");
    require(f.boxContactPairs() > 0, "floor checkpoint has no box contact pair");
    const Checkpoint asleepA = capture(f);
    f.mover->setLinearVelocity(PxVec3(8.0f, 0.0f, 0.0f));
    f.step();
    require(!f.mover->isSleeping(), "friction dash did not wake body");
    require(f.boxContactPairs() > 0, "floor dash successor lost box contact pair");
    const Digest dashB = f.digest();
    const Checkpoint awakeB = capture(f);
    diagnoseRestoreGate(f, asleepA, "friction sleep->dash");
    compareReplay(f, asleepA, dashB, awakeB.bodies,
                  "friction sleep->dash", true);
}

void quietJoinedRewindProbe()
{
    Fixture f(false);
    f.mover->setWakeCounter(0.05f);
    // Prime the sleep and wake notification arrays before the measured A/B
    // pair; SceneClockImage intentionally rejects allocator rebasing.
    for (int i = 0; i < 120 && !f.mover->isSleeping(); ++i) f.step();
    require(f.mover->isSleeping(), "joined quiet warmup did not sleep");
    f.mover->wakeUp();
    f.mover->setWakeCounter(0.05f);
    f.step();
    require(!f.mover->isSleeping(), "joined quiet warmup did not wake");
    Checkpoint awakeA;
    bool found = false;
    for (int i = 0; i < 120; ++i)
    {
        awakeA = capture(f);
        f.step();
        if (f.mover->isSleeping())
        {
            found = true;
            break;
        }
    }
    require(found, "joined quiet fixture did not sleep naturally");
    const Digest expectedB = f.digest();
    const Checkpoint sleepingB = capture(f);
    for (int cycle = 0; cycle < 100; ++cycle)
    {
        // Re-activate only to make the BodyImage active identity match. The
        // source-backed joined island/body images must provide the saved data.
        f.mover->wakeUp();
        std::string error;
        if (!oc2::offline::RestoreIslandForJoin(*f.scene, awakeA.island, error))
        {
            std::cout << "quiet joined wake->sleep gate: island: " << error << '\n';
            std::exit(1);
        }
        if (!oc2::offline::RestoreSceneClock(*f.scene, awakeA.clock, error))
        {
            std::cout << "quiet joined wake->sleep gate: scene clock: "
                      << error << '\n';
            std::exit(1);
        }
        if (!restoreSleepMarkersThenBodies(f, awakeA, error))
        {
            std::cout << "quiet joined wake->sleep gate: body: " << error << '\n';
            std::exit(1);
        }
        BodyImage observedBody;
        if (!oc2::offline::CaptureBodies(*f.scene, observedBody, error) ||
            !awakeA.bodies.equals(observedBody, error))
        {
            std::cout << "quiet joined wake->sleep gate: checkpoint body: "
                      << error << '\n';
            std::exit(1);
        }
        IslandImage observedIsland;
        if (!oc2::offline::CaptureIsland(*f.scene, observedIsland, error) ||
            !awakeA.island.equals(observedIsland, error))
        {
            std::cout << "quiet joined wake->sleep gate: checkpoint island: "
                      << error << '\n';
            std::exit(1);
        }
        SceneClockImage observedClock;
        if (!oc2::offline::CaptureSceneClock(*f.scene, observedClock, error) ||
            !awakeA.clock.equals(observedClock, error))
        {
            std::cout << "quiet joined wake->sleep gate: checkpoint scene clock: "
                      << error << '\n';
            std::exit(1);
        }
        f.step();
        const Digest replayB = f.digest();
        if (!expectedB.equals(replayB, error))
        {
            std::cout << "quiet joined wake->sleep gate: next step: "
                      << error << '\n';
            std::exit(1);
        }
        BodyImage replayBody;
        if (!oc2::offline::CaptureBodies(*f.scene, replayBody, error) ||
            !sleepingB.bodies.equals(replayBody, error))
        {
            std::cout << "quiet joined wake->sleep gate: next-step body: "
                      << error << '\n';
            std::exit(1);
        }
        IslandImage replayIsland;
        SceneClockImage replayClock;
        if (!oc2::offline::CaptureIsland(*f.scene, replayIsland, error) ||
            !sleepingB.island.equals(replayIsland, error) ||
            !oc2::offline::CaptureSceneClock(*f.scene, replayClock, error) ||
            !sleepingB.clock.equals(replayClock, error))
        {
            std::cout << "quiet joined wake->sleep gate: next-step island/clock: "
                      << error << '\n';
            std::exit(1);
        }
    }
    std::cout << "PASS quiet joined wake->sleep replay x100 (body/island/clock/public)\n";
}

void quietJoinedWakeProbe()
{
    Fixture f(false);
    f.mover->setWakeCounter(0.05f);
    for (int i = 0; i < 120 && !f.mover->isSleeping(); ++i) f.step();
    require(f.mover->isSleeping(), "joined wake warmup did not sleep");
    // Prime the wake list as well as the sleep list before checkpoint A.
    f.mover->wakeUp();
    f.step();
    f.mover->putToSleep();
    f.step();
    require(f.mover->isSleeping(), "joined wake A did not sleep");
    const Checkpoint asleepA = capture(f);
    f.mover->setLinearVelocity(PxVec3(8.0f, 0.0f, 0.0f));
    f.step();
    require(!f.mover->isSleeping(), "joined wake B did not wake");
    const Digest expectedB = f.digest();
    const Checkpoint awakeB = capture(f);
    for (int cycle = 0; cycle < 100; ++cycle)
    {
        f.mover->putToSleep();
        std::string error;
        if (!oc2::offline::RestoreIslandForJoin(*f.scene, asleepA.island, error))
        {
            std::cout << "quiet joined sleep->wake gate: island: " << error << '\n';
            std::exit(1);
        }
        if (!oc2::offline::RestoreSceneClock(*f.scene, asleepA.clock, error))
        {
            std::cout << "quiet joined sleep->wake gate: scene clock: " << error << '\n';
            std::exit(1);
        }
        if (!restoreSleepMarkersThenBodies(f, asleepA, error))
        {
            std::cout << "quiet joined sleep->wake gate: body: " << error << '\n';
            std::exit(1);
        }
        BodyImage checkpointBody;
        IslandImage checkpointIsland;
        SceneClockImage checkpointClock;
        if (!oc2::offline::CaptureBodies(*f.scene, checkpointBody, error) ||
            !asleepA.bodies.equals(checkpointBody, error) ||
            !oc2::offline::CaptureIsland(*f.scene, checkpointIsland, error) ||
            !asleepA.island.equals(checkpointIsland, error) ||
            !oc2::offline::CaptureSceneClock(*f.scene, checkpointClock, error) ||
            !asleepA.clock.equals(checkpointClock, error))
        {
            std::cout << "quiet joined sleep->wake gate: checkpoint: "
                      << error << '\n';
            std::exit(1);
        }
        f.mover->setLinearVelocity(PxVec3(8.0f, 0.0f, 0.0f));
        f.step();
        const Digest replayB = f.digest();
        if (!expectedB.equals(replayB, error))
        {
            std::cout << "quiet joined sleep->wake gate: next step: "
                      << error << '\n';
            std::exit(1);
        }
        BodyImage replayBody;
        if (!oc2::offline::CaptureBodies(*f.scene, replayBody, error) ||
            !awakeB.bodies.equals(replayBody, error))
        {
            std::cout << "quiet joined sleep->wake gate: next-step body: "
                      << error << '\n';
            std::exit(1);
        }
        IslandImage replayIsland;
        SceneClockImage replayClock;
        if (!oc2::offline::CaptureIsland(*f.scene, replayIsland, error) ||
            !awakeB.island.equals(replayIsland, error) ||
            !oc2::offline::CaptureSceneClock(*f.scene, replayClock, error) ||
            !awakeB.clock.equals(replayClock, error))
        {
            std::cout << "quiet joined sleep->wake gate: next-step island/clock: "
                      << error << '\n';
            std::exit(1);
        }
    }
    std::cout << "PASS quiet joined sleep->wake replay x100 (body/island/clock/public)\n";
}

} // namespace

int main()
{
    quietSleepMatrix();
    frictionDashMatrix();
    quietJoinedRewindProbe();
    quietJoinedWakeProbe();
    std::cout << "PASS offline sleep/motion transition matrix completed\n";
    return 0;
}
