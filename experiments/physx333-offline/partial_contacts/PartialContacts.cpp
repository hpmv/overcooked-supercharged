// Source-built PhysX 3.3.3 partial-contact predecessor fixture.
// This executable never loads Unity or Overcooked.
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "../oracle/Oracle.h"
#include "../sap/SapImage.h"
#include "../island/IslandImage.h"
#include "../nphase/NPhaseTopology.h"
#include "../interaction/InteractionImage.h"
#include "../memblock_restore/MemBlockRestore.h"
#include "../cache/TransformCacheImage.h"
#include "../shape_cache/ShapeCacheBindings.h"
#include "../body/BodyImage.h"
#include "../scene_clock/SceneClockImage.h"
#include "../context_image/ContextImage.h"
#include "../query_image/QueryImage.h"

using namespace physx;

namespace {

const PxU32 kStatics = 12;
const PxU32 kMoverId = 13;
const PxReal kStep = 1.0f / 60.0f;

void fail(const std::string& reason)
{
    std::cerr << "FAIL: " << reason << '\n';
    std::exit(1);
}

std::string sequence(const std::vector<std::uint32_t>& values,
                     std::size_t limit = 20)
{
    std::ostringstream out;
    out << '[';
    for (std::size_t i = 0; i < values.size() && i < limit; ++i)
    {
        if (i) out << ',';
        out << values[i];
    }
    if (values.size() > limit) out << ",... total=" << values.size();
    out << ']';
    return out.str();
}

std::string tail(const std::vector<std::uint32_t>& values,
                 std::size_t limit = 8)
{
    std::ostringstream out;
    out << '[';
    const std::size_t first = values.size() > limit ? values.size() - limit : 0;
    for (std::size_t i = first; i < values.size(); ++i)
    {
        if (i != first) out << ',';
        out << values[i];
    }
    out << ']';
    return out.str();
}

const std::vector<std::uint32_t>& part(
    const physx333_offline::OracleImage& image, const std::string& name)
{
    const auto found = image.parts.find(name);
    if (found == image.parts.end()) fail("Oracle section absent: " + name);
    return found->second;
}

std::uint32_t sapScalar(const oc2::offline::SapImage& image,
                        const char* name)
{
    for (const auto& scalar : image.scalars)
    {
        if (scalar.name == name)
        {
            if (scalar.bytes.size() != sizeof(std::uint32_t))
                fail(std::string("SAP scalar size changed: ") + name);
            std::uint32_t value = 0;
            std::memcpy(&value, scalar.bytes.data(), sizeof(value));
            return value;
        }
    }
    fail(std::string("SAP scalar absent: ") + name);
    return 0;
}

std::uintptr_t sapBufferAddress(const oc2::offline::SapImage& image,
                                const char* name)
{
    for (const auto& buffer : image.buffers)
        if (buffer.name == name) return buffer.address;
    fail(std::string("SAP buffer absent: ") + name);
    return 0;
}

struct ErrorCallback : PxErrorCallback
{
    PxU32 count = 0;
    virtual void reportError(PxErrorCode::Enum code, const char* message,
                             const char* file, int line)
    {
        ++count;
        std::cerr << "PhysX " << static_cast<int>(code) << ": " << message
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

PxFilterFlags reportFilter(PxFilterObjectAttributes, PxFilterData,
                           PxFilterObjectAttributes, PxFilterData,
                           PxPairFlags& pairFlags, const void*, PxU32)
{
    pairFlags = PxPairFlag::eCONTACT_DEFAULT |
                PxPairFlag::eNOTIFY_TOUCH_FOUND |
                PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
                PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct Event
{
    PxU32 actor0 = 0;
    PxU32 actor1 = 0;
    PxU32 flags = 0;
    PxU32 contacts = 0;

    bool operator==(const Event& other) const
    {
        return actor0 == other.actor0 && actor1 == other.actor1 &&
               flags == other.flags && contacts == other.contacts;
    }
};

struct ContactEvents : PxSimulationEventCallback
{
    std::vector<Event> rows;
    virtual void onConstraintBreak(PxConstraintInfo*, PxU32) {}
    virtual void onWake(PxActor**, PxU32) {}
    virtual void onSleep(PxActor**, PxU32) {}
    virtual void onTrigger(PxTriggerPair*, PxU32) {}

    virtual void onContact(const PxContactPairHeader& header,
                           const PxContactPair* pairs, PxU32 count)
    {
        const PxU32 id0 = static_cast<PxU32>(
            reinterpret_cast<std::uintptr_t>(header.actors[0]->userData));
        const PxU32 id1 = static_cast<PxU32>(
            reinterpret_cast<std::uintptr_t>(header.actors[1]->userData));
        for (PxU32 i = 0; i < count; ++i)
        {
            Event row;
            row.actor0 = PxMin(id0, id1);
            row.actor1 = PxMax(id0, id1);
            row.flags = static_cast<PxU16>(pairs[i].events);
            row.contacts = pairs[i].contactCount;
            rows.push_back(row);
        }
    }
};

struct Runtime
{
    PxDefaultAllocator allocator;
    ErrorCallback errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation = nullptr;
    PxPhysics* physics = nullptr;
    PxMaterial* material = nullptr;

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

struct PairRow
{
    PxU32 staticId = 0;
    PxU32 moverShape = 0;
    PxU32 sipOrdinal = 0;
    PxU32 sipSlot = 0;
    PxU32 managerSlot = 0;
    PxU32 islandEdge = 0;
    PxU32 touchFlags = 0;
};

// The Oracle emits these documented field groups in InteractionScene order.
// Decode identities, physical SIP/manager slots, and island edges without
// assuming a particular subset survives or a particular free-list order.
std::vector<PairRow> pairs(const physx333_offline::OracleImage& image)
{
    const auto& sip = part(image, "nphase.shape_pairs");
    const auto& manager = part(image, "contact.managers");
    const std::size_t sipStride = 12;
    const std::size_t managerStride = 14;
    if (sip.size() % sipStride || manager.size() % managerStride ||
        sip.size() / sipStride != manager.size() / managerStride)
        fail("Oracle SIP/manager row layout changed");
    std::vector<PairRow> result;
    for (std::size_t i = 0; i < sip.size() / sipStride; ++i)
    {
        const PxU32* row = sip.data() + i * sipStride;
        const PxU32* cm = manager.data() + i * managerStride;
        PairRow item;
        item.sipOrdinal = row[0];
        item.sipSlot = row[1];
        if (row[2] == kMoverId && row[5] == 0)
        {
            item.moverShape = row[3];
            item.staticId = row[4];
        }
        else if (row[4] == kMoverId && row[3] == 0)
        {
            item.moverShape = row[5];
            item.staticId = row[2];
        }
        else fail("Unexpected SIP actor/shape identities");
        if (item.staticId != item.moverShape + 1 ||
            item.staticId > kStatics || row[11] != cm[0])
            fail("SIP/manager binding is outside the 12-box fixture");
        item.managerSlot = cm[0];
        item.islandEdge = row[10];
        item.touchFlags = row[6];
        result.push_back(item);
    }
    return result;
}

struct Capture
{
    physx333_offline::OracleImage oracle;
    oc2::offline::SapImage sap;
    oc2::offline::IslandImage island;
    std::vector<PairRow> pairRows;
    std::vector<Event> events;
};

struct World
{
    Runtime& runtime;
    ContactEvents callback;
    PxScene* scene = nullptr;
    PxRigidDynamic* mover = nullptr;
    std::vector<PxRigidActor*> actors;

    explicit World(Runtime& rt) : runtime(rt)
    {
        PxSceneDesc desc(rt.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &rt.dispatcher;
        desc.filterShader = reportFilter;
        desc.simulationEventCallback = &callback;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        desc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
        desc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
        scene = rt.physics->createScene(desc);
        if (!scene) fail("createScene");

        for (PxU32 i = 0; i < kStatics; ++i)
        {
            const PxReal x = static_cast<PxReal>(i) * 3.0f;
            const PxReal z = i < 8 ? 0.0f : 0.9f;
            PxRigidStatic* fixed = rt.physics->createRigidStatic(
                PxTransform(PxVec3(x, 0.0f, z)));
            if (!fixed) fail("createRigidStatic");
            PxShape* shape = rt.physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *rt.material);
            if (!shape) fail("create static shape");
            fixed->attachShape(*shape);
            shape->release();
            fixed->userData = reinterpret_cast<void*>(
                static_cast<std::uintptr_t>(i + 1));
            scene->addActor(*fixed);
            actors.push_back(fixed);
        }

        mover = rt.physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f, 0.95f, 0.0f)));
        if (!mover) fail("createRigidDynamic");
        for (PxU32 i = 0; i < kStatics; ++i)
        {
            PxShape* shape = rt.physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *rt.material);
            if (!shape) fail("create dynamic shape");
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

    ~World()
    {
        for (PxRigidActor* actor : actors) actor->release();
        scene->release();
    }

    void step(PxReal z)
    {
        callback.rows.clear();
        mover->setGlobalPose(PxTransform(PxVec3(0.0f, 0.95f, z)));
        mover->setLinearVelocity(PxVec3(0.0f));
        mover->setAngularVelocity(PxVec3(0.0f));
        scene->simulate(kStep);
        if (!scene->fetchResults(true)) fail("fetchResults");
    }

    Capture capture() const
    {
        Capture image;
        std::string error;
        if (!physx333_offline::CaptureOracle(*scene, image.oracle, error))
            fail("CaptureOracle: " + error);
        if (!oc2::offline::CaptureSap(*scene, image.sap, error))
            fail("CaptureSap: " + error);
        if (!oc2::offline::CaptureIsland(*scene, image.island, error))
            fail("CaptureIsland: " + error);
        image.pairRows = pairs(image.oracle);
        image.events = callback.rows;
        return image;
    }
};

struct JoinedCapture
{
    oc2::offline::TransformCacheImage cache;
    oc2::offline::ShapeCacheBindings shapeCache;
    oc2::offline::BodyImage body;
    oc2::offline::SceneClockImage clock;
    oc2::offline::ContextImage context;
    oc2::offline::QueryImage query;
    physx333_offline::MemBlockRestoreImage blocks;
};

void captureJoined(World& world,
                   physx333_offline::MemBlockIdentityRegistry& memoryIds,
                   JoinedCapture& image, const char* stage)
{
    std::string error;
    if (!oc2::offline::CaptureTransformCache(*world.scene,
                                              image.cache, error))
        fail(std::string(stage) + " transform-cache capture: " + error);
    if (!oc2::offline::CaptureShapeCacheBindings(*world.scene,
                                                 image.shapeCache, error))
        fail(std::string(stage) + " shape-cache capture: " + error);
    if (!oc2::offline::CaptureBodies(*world.scene, image.body, error))
        fail(std::string(stage) + " body capture: " + error);
    if (!oc2::offline::CaptureSceneClock(*world.scene, image.clock, error))
        fail(std::string(stage) + " scene-clock capture: " + error);
    if (!oc2::offline::CaptureContextImage(*world.scene,
                                           image.context, error))
        fail(std::string(stage) + " context capture: " + error);
    if (!oc2::offline::CaptureQueryImage(*world.scene, image.query, error))
        fail(std::string(stage) + " query capture: " + error);
    if (!physx333_offline::CaptureMemBlockRestore(
            *world.scene, memoryIds, image.blocks, error))
        fail(std::string(stage) + " memory-block capture: " + error);
}

void verifyJoined(const JoinedCapture& expected,
                  const JoinedCapture& actual, const char* stage)
{
    std::string error;
    if (!expected.cache.equals(actual.cache, error))
        fail(std::string(stage) + " transform-cache image: " + error);
    if (!expected.shapeCache.equals(actual.shapeCache, error))
        fail(std::string(stage) + " shape-cache image: " + error);
    if (!expected.body.equals(actual.body, error))
        fail(std::string(stage) + " body image: " + error);
    if (!expected.clock.equals(actual.clock, error))
        fail(std::string(stage) + " scene-clock image: " + error);
    if (!expected.context.equals(actual.context, error))
        fail(std::string(stage) + " context image: " + error);
    if (!expected.query.equalsWithRebasedStack(actual.query, error))
        fail(std::string(stage) + " query image: " + error);
    if (!expected.blocks.pool.equals(actual.blocks.pool, error) ||
        expected.blocks.contactBindings != actual.blocks.contactBindings)
        fail(std::string(stage) + " memory-block image: " + error);
}

void restoreJoinedSubset(World& world,
                         const physx333_offline::NPhaseTopologyImage& topology,
                         const physx333_offline::InteractionImage& interaction,
                         physx333_offline::MemBlockIdentityRegistry& memoryIds,
                         const Capture& checkpoint,
                         const JoinedCapture& checkpointJoined,
                         const char* stage)
{
    std::string error;
    if (!physx333_offline::RestoreNPhaseSubset(
            *world.scene, topology, error))
        fail(std::string(stage) + " NPhase subset: " + error);
    if (!physx333_offline::RestoreInteractionMetadataSubset12(
            *world.scene, interaction, error))
        fail(std::string(stage) + " interaction metadata: " + error);
    if (!physx333_offline::InstallInteractionContactBindingsSubset12(
            *world.scene, interaction, error))
        fail(std::string(stage) + " contact bindings: " + error);
    if (!physx333_offline::RestoreMemBlockPoolForJoin(
            *world.scene, memoryIds, checkpointJoined.blocks, error))
        fail(std::string(stage) + " memory blocks: " + error);
    if (!physx333_offline::RestoreInteractionContactPayloadSubset12(
            *world.scene, interaction, error))
        fail(std::string(stage) + " contact payload: " + error);
    if (!oc2::offline::RestoreIslandForJoin(
            *world.scene, checkpoint.island, error))
        fail(std::string(stage) + " island: " + error);
    if (!oc2::offline::RestoreSap(
            *world.scene, checkpoint.sap, error))
        fail(std::string(stage) + " SAP: " + error);
    if (!oc2::offline::RestoreTransformCache(
            *world.scene, checkpointJoined.cache, error))
        fail(std::string(stage) + " transform cache: " + error);
    if (!oc2::offline::RestoreShapeCacheBindings(
            *world.scene, checkpointJoined.shapeCache, error))
        fail(std::string(stage) + " shape cache: " + error);
    if (!oc2::offline::RestoreBodies(
            *world.scene, checkpointJoined.body, error))
        fail(std::string(stage) + " body: " + error);
    if (!oc2::offline::RestoreSceneClock(
            *world.scene, checkpointJoined.clock, error))
        fail(std::string(stage) + " scene clock: " + error);
    if (!oc2::offline::RestoreContextImage(
            *world.scene, checkpointJoined.context, error))
        fail(std::string(stage) + " context: " + error);
    if (!oc2::offline::RestoreQueryImage(
            *world.scene, checkpointJoined.query, error))
        fail(std::string(stage) + " query: " + error);
    const Capture restored = world.capture();
    if (!checkpoint.oracle.equals(restored.oracle, error) ||
        !checkpoint.sap.equals(restored.sap, error) ||
        !checkpoint.island.equals(restored.island, error))
        fail(std::string(stage) + " checkpoint Oracle/SAP/island: " + error);
    JoinedCapture restoredJoined;
    captureJoined(world, memoryIds, restoredJoined, stage);
    verifyJoined(checkpointJoined, restoredJoined, stage);
}

void verifyEventSet(const std::vector<Event>& rows, PxU32 mask,
                    PxU32 first, PxU32 last, const char* stage)
{
    if (rows.size() != last - first + 1)
        fail(std::string(stage) + " event count " + std::to_string(rows.size()));
    std::set<PxU32> seen;
    for (const auto& row : rows)
    {
        if (row.actor0 < first || row.actor0 > last ||
            row.actor1 != kMoverId || !(row.flags & mask) ||
            !seen.insert(row.actor0).second)
            fail(std::string(stage) + " callback identities/flags differ");
    }
}

void verifyPartialEvents(const std::vector<Event>& rows)
{
    if (rows.size() != 12) fail("Partial step expected 12 callback rows");
    std::set<PxU32> seen;
    PxU32 persist = 0, lost = 0;
    for (const auto& row : rows)
    {
        if (row.actor0 < 1 || row.actor0 > kStatics ||
            row.actor1 != kMoverId || !seen.insert(row.actor0).second)
            fail("Partial step callback identity or duplicate differs");
        if (row.actor0 <= 8)
        {
            if (!(row.flags & PxPairFlag::eNOTIFY_TOUCH_PERSISTS))
                fail("Surviving pair failed to persist");
            ++persist;
        }
        else
        {
            if (!(row.flags & PxPairFlag::eNOTIFY_TOUCH_LOST))
                fail("Deleted pair failed to report touch lost");
            ++lost;
        }
    }
    if (persist != 8 || lost != 4) fail("Unexpected partial event split");
}

void verifyStable(World& world, const Capture& expected, const char* stage)
{
    const Capture again = world.capture();
    std::string difference;
    if (!expected.oracle.equals(again.oracle, difference))
        fail(std::string(stage) + " Oracle double capture: " + difference);
    if (!expected.sap.equals(again.sap, difference))
        fail(std::string(stage) + " SAP double capture: " + difference);
    if (!expected.island.equals(again.island, difference))
        fail(std::string(stage) + " island double capture: " + difference);
}

void printPairFacts(const char* label, const Capture& image)
{
    std::cout << label << " pair_count=" << image.pairRows.size()
              << " sap_boxes=" << image.sap.activeElements
              << " sap_pairs=" << sapScalar(image.sap, "pair.activeCount")
              << " sap_created=" << sapScalar(image.sap, "sap.createdPairsSize")
              << " sap_deleted=" << sapScalar(image.sap, "sap.deletedPairsSize")
              << " island_bindings=" << image.island.bindings.size() << '\n';
    for (const auto& row : image.pairRows)
        std::cout << "PAIR " << label << " static=" << row.staticId
                  << " mover_shape=" << row.moverShape
                  << " sip_order=" << row.sipOrdinal
                  << " sip_slot=" << row.sipSlot
                  << " manager_slot=" << row.managerSlot
                  << " island_edge=" << row.islandEdge
                  << " flags=" << row.touchFlags << '\n';
    const char* freeSections[] = {
        "nphase.pool.shape_pair.free_order",
        "contact.pool.free_order",
        "island.edges.free_order"
    };
    for (const char* name : freeSections)
        std::cout << "FREE " << label << ' ' << name
                  << " head=" << sequence(part(image.oracle, name))
                  << " tail=" << tail(part(image.oracle, name)) << '\n';
    for (const auto& row : image.events)
        std::cout << "EVENT " << label << " static=" << row.actor0
                  << " mover=" << row.actor1
                  << " flags=" << row.flags
                  << " contacts=" << row.contacts << '\n';
}

void verifyFreshEqual(const Capture& a, const Capture& b,
                      const char* stage)
{
    std::string difference;
    if (!a.oracle.equals(b.oracle, difference))
        fail(std::string(stage) + " Oracle fresh-scene equality: " + difference);
    if (a.events != b.events)
        fail(std::string(stage) + " ordered callback fresh-scene equality");
    if (a.pairRows.size() != b.pairRows.size())
        fail(std::string(stage) + " pair row count differs");
    for (std::size_t i = 0; i < a.pairRows.size(); ++i)
    {
        const PairRow& x = a.pairRows[i];
        const PairRow& y = b.pairRows[i];
        if (x.staticId != y.staticId || x.moverShape != y.moverShape ||
            x.sipOrdinal != y.sipOrdinal || x.sipSlot != y.sipSlot ||
            x.managerSlot != y.managerSlot || x.islandEdge != y.islandEdge ||
            x.touchFlags != y.touchFlags)
            fail(std::string(stage) + " pair row differs");
    }
}

void subsetProbe(Runtime& runtime, bool warm)
{
    World world(runtime);
    if (warm)
    {
        // A separate control: preallocate the broadphase deletion-output
        // buffer, then re-establish all twelve pairs before checkpoint A.
        // This does not replace the hard cold A->B predecessor test.
        world.step(0.0f);
        world.step(-0.2f);
    }
    world.step(0.0f);
    world.step(0.0f);
    const Capture checkpoint = world.capture();
    physx333_offline::NPhaseTopologyImage target;
    std::string error;
    if (!physx333_offline::CaptureNPhaseTopology(*world.scene,
                                                 target, error))
        fail("NPhase checkpoint capture: " + error);
    physx333_offline::InteractionImage checkpointInteraction;
    if (!physx333_offline::CaptureInteractionImage(
            *world.scene, checkpointInteraction, error))
        fail("12-pair interaction checkpoint capture: " + error);
    physx333_offline::MemBlockIdentityRegistry memoryIds;
    JoinedCapture checkpointJoined;
    captureJoined(world, memoryIds, checkpointJoined, "checkpoint");
    const physx333_offline::MemBlockRestoreImage& checkpointMemory =
        checkpointJoined.blocks;
    world.step(-0.2f);
    const Capture successor = world.capture();
    JoinedCapture successorJoined;
    captureJoined(world, memoryIds, successorJoined, "successor");
    std::cout << "REPORT_FACTS A_report_objects="
              << part(checkpoint.oracle,
                      "nphase.pool.actor_pair_report.header")[2]
              << " B_report_objects="
              << part(successor.oracle,
                      "nphase.pool.actor_pair_report.header")[2]
              << " A_persistent_pairs="
              << part(checkpoint.oracle, "nphase.event_lists")[0]
              << " B_persistent_pairs="
              << part(successor.oracle, "nphase.event_lists")[0]
              << '\n';
    physx333_offline::NPhaseTopologyImage current;
    if (!physx333_offline::CaptureNPhaseTopology(*world.scene,
                                                 current, error))
        fail("NPhase successor capture: " + error);
    if (target.pairs.size() != 12 || current.pairs.size() != 8)
        fail("NPhase subset probe did not produce 12-to-8");

    physx333_offline::NPhaseTopologyImage invalid = target;
    invalid.pairs.back().shape0.shapeIndex = 99;
    if (physx333_offline::RestoreNPhaseSubset(*world.scene, invalid, error))
        fail("Invalid NPhase subset target was accepted");
    const Capture afterReject = world.capture();
    if (!successor.oracle.equals(afterReject.oracle, error))
        fail("Rejected NPhase subset target changed the scene: " + error);

    invalid = target;
    invalid.pairs.back().managerSlot = 999;
    if (physx333_offline::RestoreNPhaseSubset(*world.scene, invalid, error))
        fail("Impossible NPhase manager slot was accepted");
    const Capture afterSlotReject = world.capture();
    if (!successor.oracle.equals(afterSlotReject.oracle, error))
        fail("Rejected NPhase manager slot changed the scene: " + error);

    if (!physx333_offline::RestoreNPhaseSubset(*world.scene, target, error))
        fail("NPhase subset lifecycle failed; scene is fail-stop: " + error);
    physx333_offline::NPhaseTopologyImage restored;
    if (!physx333_offline::CaptureNPhaseTopology(*world.scene,
                                                 restored, error) ||
        !target.sameShapePairs(restored, error))
        fail("NPhase subset ordered topology differs: " + error);
    physx333_offline::InteractionImage recreatedInteraction;
    if (!physx333_offline::CaptureInteractionImage(
            *world.scene, recreatedInteraction, error))
        fail("12-pair interaction post-lifecycle capture: " + error);
    if (checkpointInteraction.pairs.size() != 12 ||
        recreatedInteraction.pairs.size() != 12)
        fail("12-pair interaction images are incomplete");
    for (std::size_t i = 0; i < target.pairs.size(); ++i)
    {
        if (target.pairs[i].sipPoolSlot != restored.pairs[i].sipPoolSlot ||
            target.pairs[i].managerSlot != restored.pairs[i].managerSlot)
            fail("NPhase subset SIP/manager slot differs at pair " +
                 std::to_string(i));
    }
    physx333_offline::OracleImage projected;
    if (!physx333_offline::CaptureOracle(*world.scene, projected, error))
        fail("NPhase subset projected Oracle capture: " + error);
    if (checkpoint.oracle.equals(projected, error))
        fail("Lifecycle-only subset unexpectedly matched the full checkpoint");
    std::cout << "SUBSET_ORACLE_FIRST_DIFFERENCE " << error << '\n';
    if (!physx333_offline::RestoreInteractionMetadataSubset12(
            *world.scene, checkpointInteraction, error))
        fail("12-pair report/touch restore failed; scene is fail-stop: " + error);
    physx333_offline::OracleImage metadataProjected;
    if (!physx333_offline::CaptureOracle(
            *world.scene, metadataProjected, error))
        fail("12-pair metadata Oracle capture: " + error);
    if (checkpoint.oracle.equals(metadataProjected, error))
        fail("Metadata-only subset unexpectedly matched full checkpoint");
    std::cout << "METADATA_ORACLE_FIRST_DIFFERENCE " << error << '\n';
    if (!physx333_offline::InstallInteractionContactBindingsSubset12(
            *world.scene, checkpointInteraction, error))
        fail("12-pair contact binding install failed; scene is fail-stop: " +
             error);
    if (!physx333_offline::RestoreMemBlockPoolForJoin(
            *world.scene, memoryIds, checkpointMemory, error))
        fail("12-pair contact block restore failed; scene is fail-stop: " +
             error);
    if (!physx333_offline::RestoreInteractionContactPayloadSubset12(
            *world.scene, checkpointInteraction, error))
        fail("12-pair contact payload restore failed; scene is fail-stop: " +
             error);
    physx333_offline::OracleImage contactProjected;
    if (!physx333_offline::CaptureOracle(
            *world.scene, contactProjected, error))
        fail("12-pair contact Oracle capture: " + error);
    for (const auto& section : checkpoint.oracle.parts)
    {
        if (section.first.find("contact.") != 0 &&
            section.first.find("nphase.") != 0) continue;
        if (part(contactProjected, section.first) != section.second)
            fail("12-pair interaction Oracle section differs: " +
                 section.first);
    }
    std::cout << "PASS all contact.* and nphase.* Oracle sections match A\n";
    if (checkpoint.oracle.equals(contactProjected, error))
        fail("Interaction-only subset unexpectedly matched full checkpoint");
    std::cout << "CONTACT_ORACLE_FIRST_DIFFERENCE " << error << '\n';
    if (!oc2::offline::RestoreIslandForJoin(
            *world.scene, checkpoint.island, error))
        fail("12-pair joined island restore failed; scene is fail-stop: " +
             error);
    physx333_offline::OracleImage islandProjected;
    if (!physx333_offline::CaptureOracle(
            *world.scene, islandProjected, error))
        fail("12-pair island Oracle capture: " + error);
    if (!checkpoint.oracle.equals(islandProjected, error))
        fail("12-pair island restore did not match full Oracle: " + error);
    oc2::offline::IslandImage restoredIsland;
    if (!oc2::offline::CaptureIsland(*world.scene, restoredIsland, error) ||
        !checkpoint.island.equals(restoredIsland, error))
        fail("12-pair island image differs: " + error);
    std::cout << "PASS twelve-pair full Oracle and island checkpoint image\n";

    oc2::offline::SapImage preRestoreSap;
    if (!oc2::offline::CaptureSap(*world.scene, preRestoreSap, error))
        fail("12-pair pre-restore SAP capture: " + error);
    std::cout << "SAP_DELETED_OVERLAP_CAPACITY checkpoint="
              << sapScalar(checkpoint.sap, "aabb.deletedOverlapCapacity")
              << " current="
              << sapScalar(preRestoreSap, "aabb.deletedOverlapCapacity")
              << " checkpoint_size="
              << sapScalar(checkpoint.sap, "aabb.deletedOverlapSize")
              << " current_size="
              << sapScalar(preRestoreSap, "aabb.deletedOverlapSize")
              << " checkpoint_buffer=0x" << std::hex
              << sapBufferAddress(checkpoint.sap, "aabb.deletedOverlaps")
              << " current_buffer=0x"
              << sapBufferAddress(preRestoreSap, "aabb.deletedOverlaps")
              << std::dec
              << '\n';

    if (!oc2::offline::RestoreSap(*world.scene, checkpoint.sap, error))
        fail("12-pair SAP restore failed; scene is fail-stop: " + error);
    if (!oc2::offline::RestoreTransformCache(
            *world.scene, checkpointJoined.cache, error))
        fail("12-pair transform-cache restore failed; scene is fail-stop: " +
             error);
    if (!oc2::offline::RestoreShapeCacheBindings(
            *world.scene, checkpointJoined.shapeCache, error))
        fail("12-pair shape-cache restore failed; scene is fail-stop: " +
             error);
    if (!oc2::offline::RestoreBodies(
            *world.scene, checkpointJoined.body, error))
        fail("12-pair body restore failed; scene is fail-stop: " + error);
    if (!oc2::offline::RestoreSceneClock(
            *world.scene, checkpointJoined.clock, error))
        fail("12-pair clock restore failed; scene is fail-stop: " + error);
    if (!oc2::offline::RestoreContextImage(
            *world.scene, checkpointJoined.context, error))
        fail("12-pair context restore failed; scene is fail-stop: " + error);
    if (!oc2::offline::RestoreQueryImage(
            *world.scene, checkpointJoined.query, error))
        fail("12-pair query restore failed; scene is fail-stop: " + error);
    const Capture restoredA = world.capture();
    if (!checkpoint.oracle.equals(restoredA.oracle, error) ||
        !checkpoint.sap.equals(restoredA.sap, error) ||
        !checkpoint.island.equals(restoredA.island, error))
        fail("12-pair complete checkpoint image differs: " + error);
    JoinedCapture restoredJoined;
    captureJoined(world, memoryIds, restoredJoined, "restored checkpoint");
    verifyJoined(checkpointJoined, restoredJoined, "restored checkpoint");
    std::cout << "PASS twelve-pair complete checkpoint component images\n";

    world.step(-0.2f);
    const Capture replayedB = world.capture();
    verifyFreshEqual(successor, replayedB, "joined next-step");
    if (!successor.sap.equals(replayedB.sap, error) ||
        !successor.island.equals(replayedB.island, error))
        fail("12-pair joined next-step SAP/island image: " + error);
    JoinedCapture replayedJoined;
    captureJoined(world, memoryIds, replayedJoined, "replayed successor");
    verifyJoined(successorJoined, replayedJoined, "joined next-step");
    std::cout << "PASS 12-to-8 joined next-step ordered callbacks, Oracle, "
                 "and all component images\n";
    for (unsigned iteration = 1; iteration < 100; ++iteration)
    {
        const std::string stage = "joined repeat " +
            std::to_string(iteration);
        restoreJoinedSubset(world, target, checkpointInteraction, memoryIds,
                            checkpoint, checkpointJoined, stage.c_str());
        world.step(-0.2f);
        const Capture repeatedB = world.capture();
        verifyFreshEqual(successor, repeatedB, stage.c_str());
        if (!successor.sap.equals(repeatedB.sap, error) ||
            !successor.island.equals(repeatedB.island, error))
            fail(stage + " successor SAP/island: " + error);
        JoinedCapture repeatedJoined;
        captureJoined(world, memoryIds, repeatedJoined, stage.c_str());
        verifyJoined(successorJoined, repeatedJoined, stage.c_str());
    }
    std::cout << "PASS 12-to-8 joined replay and all component images x100\n";
    std::cout.flush();
    std::_Exit(0);
}

} // namespace

int main(int argc, char** argv)
{
    static_assert(sizeof(void*) == 4, "Requires Win32 PhysX");
    const bool subset = argc == 2 && std::string(argv[1]) == "--subset-probe";
    const bool warmSubset = argc == 2 &&
        std::string(argv[1]) == "--subset-warm-probe";
    if (argc != 1 && !subset && !warmSubset)
        fail("Unknown partial-contact fixture argument");
    Runtime runtime;
    if (subset || warmSubset) subsetProbe(runtime, warmSubset);
    Capture checkpoint, successor;
    std::vector<Event> publicRepeat;
    {
        World world(runtime);
        world.step(0.0f);
        verifyEventSet(world.callback.rows, PxPairFlag::eNOTIFY_TOUCH_FOUND,
                       1, 12, "first contact");
        world.step(0.0f);
        checkpoint = world.capture();
        verifyStable(world, checkpoint, "checkpoint");
        verifyEventSet(checkpoint.events, PxPairFlag::eNOTIFY_TOUCH_PERSISTS,
                       1, 12, "established checkpoint");
        if (checkpoint.pairRows.size() != 12)
            fail("Checkpoint did not retain 12 interactions");

        const PxTransform originalPose = world.mover->getGlobalPose();
        const PxVec3 originalLinear = world.mover->getLinearVelocity();
        const PxVec3 originalAngular = world.mover->getAngularVelocity();
        const PxReal originalWake = world.mover->getWakeCounter();
        world.step(-0.2f);
        successor = world.capture();
        verifyStable(world, successor, "successor");
        verifyPartialEvents(successor.events);
        if (successor.pairRows.size() != 8)
            fail("Partial successor did not retain 8 interactions");
        std::set<PxU32> live;
        for (const auto& row : successor.pairRows) live.insert(row.staticId);
        for (PxU32 id = 1; id <= 12; ++id)
            if (live.count(id) != (id <= 8 ? 1u : 0u))
                fail("Unexpected surviving NPhase pair set");

        printPairFacts("A", checkpoint);
        printPairFacts("B", successor);

        world.mover->setGlobalPose(originalPose);
        world.mover->setLinearVelocity(originalLinear);
        world.mover->setAngularVelocity(originalAngular);
        world.mover->setWakeCounter(originalWake);
        world.step(-0.2f);
        publicRepeat = world.callback.rows;
        if (publicRepeat == successor.events)
            fail("Public-body control unexpectedly replayed the four lost events");
        verifyEventSet(publicRepeat, PxPairFlag::eNOTIFY_TOUCH_PERSISTS,
                       1, 8, "public-only repeat");
        std::cout << "PUBLIC_REPEAT callback_rows=" << publicRepeat.size()
                  << " original=" << successor.events.size() << '\n';
    }

    {
        World fresh(runtime);
        fresh.step(0.0f);
        fresh.step(0.0f);
        const Capture freshA = fresh.capture();
        verifyFreshEqual(checkpoint, freshA, "checkpoint");
        fresh.step(-0.2f);
        const Capture freshB = fresh.capture();
        verifyFreshEqual(successor, freshB, "successor");
    }
    if (runtime.errors.count) fail("PhysX issued an error");
    std::cout << "PASS deterministic fresh-scene A(12) -> B(8), ordered "
                 "callbacks, source Oracle, SAP/island stable captures, "
                 "and public-only predecessor control\n";
}
