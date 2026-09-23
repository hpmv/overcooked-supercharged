#define OC2_LEVEL_GRAPH_NO_MAIN
#include "../level_graph/LevelGraph.cpp"
#include "JoinedTopologyBridge.h"

#include <map>
#include <set>
#include <stdexcept>

namespace {

struct StoppedImage
{
    physx333_offline::OracleImage oracle;
    physx333_offline::AuxInteractionImage aux;
    physx333_offline::ActorPairGraphImage actorPair;
    GraphImage graph;
    oc2::offline::SapImage sap;
    oc2::offline::TransformCacheImage cache;
    oc2::offline::IslandImage island;
    physx333_offline::MemBlockImage memBlocks;
    oc2::offline::ShapeCacheBindings shapeCache;
    oc2::offline::BodyImage bodies;
    oc2::offline::SceneClockImage clock;
    oc2::offline::ContextImage context;
    oc2::offline::QueryImage query;
};

StoppedImage captureStopped(World& world)
{
    StoppedImage image;
    std::string error;
    if (!physx333_offline::CaptureOracle(*world.scene, image.oracle, error) ||
        !captureGraph(*world.scene, image.graph, error) ||
        !physx333_offline::CaptureAuxInteractionImage(
            *world.scene, image.aux, error) ||
        !physx333_offline::CaptureActorPairGraph(
            *world.scene, image.actorPair, error) ||
        !oc2::offline::CaptureSap(*world.scene, image.sap, error) ||
        !oc2::offline::CaptureTransformCache(
            *world.scene, image.cache, error) ||
        !oc2::offline::CaptureIsland(*world.scene, image.island, error) ||
        !physx333_offline::CaptureMemBlockPool(
            *world.scene, world.memBlockRegistry, image.memBlocks, error) ||
        !oc2::offline::CaptureShapeCacheBindings(
            *world.scene, image.shapeCache, error) ||
        !oc2::offline::CaptureBodies(*world.scene, image.bodies, error) ||
        !oc2::offline::CaptureSceneClock(*world.scene, image.clock, error) ||
        !oc2::offline::CaptureContextImage(
            *world.scene, image.context, error) ||
        !oc2::offline::CaptureQueryImage(*world.scene, image.query, error))
        fail("joined stopped capture: " + error);
    return image;
}

StoppedImage captureTopology(World& world)
{
    StoppedImage image;
    std::string error;
    if (!physx333_offline::CaptureOracle(*world.scene, image.oracle, error) ||
        !captureGraph(*world.scene, image.graph, error) ||
        !physx333_offline::CaptureAuxInteractionImage(
            *world.scene, image.aux, error) ||
        !physx333_offline::CaptureActorPairGraph(
            *world.scene, image.actorPair, error))
        fail("joined topology readback: " + error);
    return image;
}

void requireUnchanged(const Snapshot& expected, const StoppedImage& actual,
                      const char* stage)
{
    std::string difference;
    if (!expected.oracle.equals(actual.oracle, difference))
        fail(std::string(stage) + " changed Oracle: " + difference);
    if (!expected.aux.equals(actual.aux, difference))
        fail(std::string(stage) + " changed auxiliary image: " + difference);
    if (!(expected.actorPair == actual.actorPair))
        fail(std::string(stage) + " changed ActorPair graph");
    if (!(expected.graph == actual.graph))
        fail(std::string(stage) + " changed interaction graph");
    if (!expected.sap.equals(actual.sap, difference) ||
        !expected.cache.equals(actual.cache, difference) ||
        !expected.island.equals(actual.island, difference) ||
        !expected.memBlocks.equals(actual.memBlocks, difference) ||
        !expected.shapeCache.equals(actual.shapeCache, difference) ||
        !expected.bodies.equals(actual.bodies, difference) ||
        !expected.clock.equals(actual.clock, difference) ||
        !expected.context.equals(actual.context, difference) ||
        !expected.query.equals(actual.query, difference))
        fail(std::string(stage) + " changed component image: " + difference);
}

struct LiveCores
{
    std::map<PxU32, void*> actors;
    std::map<ShapeKey, void*> shapes;
};

LiveCores captureLiveCores(World& world)
{
    LiveCores live;
    const PxActorTypeFlags types = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    std::vector<PxActor*> actors(world.scene->getNbActors(types));
    if (actors.size() != 13 || world.scene->getActors(types, actors.data(),
            static_cast<PxU32>(actors.size())) != actors.size())
        fail("joined fixture actor inventory differs");
    for (PxActor* actor : actors)
    {
        const PxU32 actorId = id(actor);
        if (!actorId || !live.actors.insert(std::make_pair(actorId,
                static_cast<void*>(&static_cast<Sc::ActorSim*>(
                    scActor(*actor))->getActorCore()))).second)
            fail("joined fixture actor IDs are missing or duplicate");
        PxRigidActor& rigid = *static_cast<PxRigidActor*>(actor);
        std::vector<PxShape*> shapes(rigid.getNbShapes());
        if (rigid.getShapes(shapes.data(),
                static_cast<PxU32>(shapes.size())) != shapes.size())
            fail("joined fixture shape enumeration changed");
        for (PxU32 i = 0; i < shapes.size(); ++i)
        {
            ShapeKey key; key.actor = actorId; key.shape = i;
            void* core = &static_cast<NpShape&>(*shapes[i]).getScbShape()
                .getScShape();
            if (!live.shapes.insert(std::make_pair(key, core)).second)
                fail("joined fixture shape IDs are duplicate");
        }
    }
    return live;
}

void fillEndpoints(physx333_offline::JoinedTopologyRoleV1& role,
                   const LiveCores& live, const ShapeKey& s0,
                   const ShapeKey& s1)
{
    role.actorCore0 = live.actors.at(s0.actor);
    role.shapeCore0 = live.shapes.at(s0);
    role.actorCore1 = live.actors.at(s1.actor);
    role.shapeCore1 = live.shapes.at(s1);
}

ShapeKey shapeKey(const physx333_offline::ActorGraphShapeKey& shape)
{
    ShapeKey key; key.actor = shape.actor; key.shape = shape.index;
    return key;
}

ShapeKey shapeKey(const physx333_offline::AuxShapeKey& shape)
{
    ShapeKey key; key.actor = shape.actorId; key.shape = shape.shapeIndex;
    return key;
}

physx333_offline::JoinedTopologyPlanV1 makePlan(
    World& world, const Snapshot& a, const Snapshot& b)
{
    using namespace physx333_offline;
    if (a.graph.scenePairs.size() != 18 ||
        a.actorPair.sips.size() != 12 || a.aux.triggers.size() != 4 ||
        a.aux.markers.size() != 2 || a.graph.actors.size() != 13)
        fail("joined checkpoint images have unexpected counts");
    const LiveCores live = captureLiveCores(world);
    JoinedTopologyPlanV1 plan = {};
    const PxU32 contactFlags = static_cast<PxU32>(
        PxPairFlag::eCONTACT_DEFAULT | PxPairFlag::eNOTIFY_TOUCH_FOUND |
        PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
        PxPairFlag::eNOTIFY_TOUCH_LOST);
    std::map<PairKey, PxU32> roleByKey;
    for (PxU32 i = 0; i < 18; ++i)
    {
        const PairKey& key = a.graph.scenePairs[i];
        if (!roleByKey.insert(std::make_pair(key, i)).second)
            fail("joined checkpoint has duplicate pair keys");
        JoinedTopologyRoleV1& role = plan.roles[i];
        role.type = key.type;
        role.targetActorPairPoolSlot = 0xffffffffu;
        if (i < 12)
        {
            const ActorGraphSipRow& sip = a.actorPair.sips[i];
            const ShapeKey s0 = shapeKey(sip.shape0);
            const ShapeKey s1 = shapeKey(sip.shape1);
            if (!(pair(key.type, s0, s1) == key) ||
                key.type != Sc::PX_INTERACTION_TYPE_OVERLAP)
                fail("joined contact scene/image order differs");
            fillEndpoints(role, live, s0, s1);
            role.expectedPairFlags = contactFlags;
            role.targetInteractionPoolSlot = sip.sipSlot;
            role.targetActorPairPoolSlot = sip.actorPairSlot;
            plan.contactSceneOrder[i] = i;
        }
        else if (i < 16)
        {
            const AuxInteractionRow& trigger = a.aux.triggers[i - 12];
            const ShapeKey s0 = shapeKey(trigger.pair.shape0);
            const ShapeKey s1 = shapeKey(trigger.pair.shape1);
            if (!(pair(key.type, s0, s1) == key) ||
                key.type != Sc::PX_INTERACTION_TYPE_TRIGGER)
                fail("joined trigger scene/image order differs");
            fillEndpoints(role, live, s0, s1);
            role.expectedPairFlags = PxPairFlag::eTRIGGER_DEFAULT;
            role.targetInteractionPoolSlot = trigger.poolSlot;
            role.targetInteractionFlags = trigger.interactionFlags;
            role.targetCoreFlags = trigger.coreFlags;
            role.targetDirtyFlags = trigger.dirtyFlags;
            role.targetTriggerFlags = trigger.triggerFlags;
            role.targetLastTouch = trigger.lastFrameHadContacts;
            role.targetCacheState = trigger.triggerCacheState;
            plan.triggerSceneOrder[i - 12] = i;
        }
        else
        {
            const AuxInteractionRow& marker = a.aux.markers[i - 16];
            const ShapeKey s0 = shapeKey(marker.pair.shape0);
            const ShapeKey s1 = shapeKey(marker.pair.shape1);
            if (!(pair(key.type, s0, s1) == key) ||
                key.type != Sc::PX_INTERACTION_TYPE_MARKER)
                fail("joined marker scene/image order differs");
            fillEndpoints(role, live, s0, s1);
            role.targetInteractionPoolSlot = marker.poolSlot;
            plan.markerSceneOrder[i - 16] = i;
        }
    }

    for (PxU32 i = 0; i < 13; ++i)
    {
        const ActorOrder& source = a.graph.actors[i];
        JoinedTopologyActorOrderV1& order = plan.actors[i];
        if (source.pairs.size() > 18)
            fail("joined actor interaction count exceeds fixed plan");
        order.actorCore = live.actors.at(source.id);
        order.count = static_cast<PxU32>(source.pairs.size());
        for (PxU32 j = 0; j < order.count; ++j)
            order.roleIds[j] = roleByKey.at(source.pairs[j]);
    }

    const std::set<PairKey> bKeys(
        b.graph.scenePairs.begin(), b.graph.scenePairs.end());
    std::set<PxU32> missingContacts, missingTriggers;
    for (PxU32 i = 0; i < 18; ++i)
        if (!bKeys.count(a.graph.scenePairs[i]))
        {
            if (i < 12) missingContacts.insert(i);
            else if (i < 16) missingTriggers.insert(i);
            else fail("joined marker disappeared");
        }
    if (missingContacts.size() != 4 || missingTriggers.size() != 2)
        fail("joined missing interaction counts differ");
    const auto& sipFree = part(b.oracle,
        "nphase.pool.shape_pair.free_order");
    if (sipFree.size() < 4 || b.aux.triggerPool.freeOrder.size() < 2)
        fail("joined successor free chains are incomplete");
    for (PxU32 step = 0; step < 4; ++step)
    {
        const PxU32 slot = sipFree[step];
        auto found = std::find_if(missingContacts.begin(),
            missingContacts.end(), [&](PxU32 role) {
                return plan.roles[role].targetInteractionPoolSlot == slot;
            });
        if (found == missingContacts.end())
            fail("joined SIP free head cannot realize target slot");
        plan.missingContactRoles[step] = *found;
        missingContacts.erase(found);
    }
    for (PxU32 step = 0; step < 2; ++step)
    {
        const PxU32 slot = b.aux.triggerPool.freeOrder[step];
        auto found = std::find_if(missingTriggers.begin(),
            missingTriggers.end(), [&](PxU32 role) {
                return plan.roles[role].targetInteractionPoolSlot == slot;
            });
        if (found == missingTriggers.end())
            fail("joined trigger free head cannot realize target slot");
        plan.missingTriggerRoles[step] = *found;
        missingTriggers.erase(found);
    }
    return plan;
}

void verifyTopologyReadback(const Snapshot& a, const Snapshot& b,
                            const StoppedImage& restored)
{
    std::string difference;
    if (!(a.graph == restored.graph))
        fail("joined checkpoint interaction graph differs after lifecycle");
    if (!a.aux.equals(restored.aux, difference))
        fail("joined checkpoint auxiliary/mixed ordering differs: " +
             difference);
    const auto& wanted = a.actorPair;
    const auto& live = restored.actorPair;
    if (wanted.sips.size() != 12 || live.sips.size() != 12 ||
        wanted.actorPairs.size() != 12 || live.actorPairs.size() != 12 ||
        !(wanted.actorPairPool == live.actorPairPool) ||
        !(b.actorPair.reportDataPool == live.reportDataPool))
        fail("joined checkpoint ActorPair pool topology differs");
    for (PxU32 i = 0; i < 12; ++i)
    {
        const auto& expectedSip = wanted.sips[i];
        const auto& actualSip = live.sips[i];
        if (!(expectedSip.shape0 == actualSip.shape0) ||
            !(expectedSip.shape1 == actualSip.shape1) ||
            !(expectedSip.actorKey == actualSip.actorKey) ||
            expectedSip.sipSlot != actualSip.sipSlot ||
            expectedSip.actorPairSlot != actualSip.actorPairSlot)
            fail("joined checkpoint SIP/AP identity or slot differs");
        const auto& expectedPair = wanted.actorPairs[i];
        const auto& actualPair = live.actorPairs[i];
        if (!(expectedPair.key == actualPair.key) ||
            expectedPair.actorA != actualPair.actorA ||
            expectedPair.actorB != actualPair.actorB ||
            expectedPair.poolSlot != actualPair.poolSlot ||
            expectedPair.refCount != actualPair.refCount ||
            expectedPair.sipOwners != actualPair.sipOwners)
            fail("joined checkpoint ActorPair ownership differs");
    }
    const char* exactSections[] = {
        "nphase.interaction_counts",
        "nphase.pool.shape_pair.header",
        "nphase.pool.shape_pair.free_order",
        "nphase.pool.actor_pair.header",
        "nphase.pool.actor_pair.free_order",
        "nphase.pool.trigger.header",
        "nphase.pool.trigger.free_order",
        "nphase.pool.marker.header",
        "nphase.pool.marker.free_order",
        "contact.pool.header",
        "contact.pool.free_order",
        "contact.pool.used_indices"
    };
    for (const char* section : exactSections)
        if (part(a.oracle, section) != part(restored.oracle, section))
            fail(std::string("joined checkpoint Oracle topology differs: ") +
                 section);
    const auto& targetSipRows = part(a.oracle, "nphase.shape_pairs");
    const auto& currentSipRows = part(restored.oracle,
                                      "nphase.shape_pairs");
    // Oracle stores 12 words per SIP: ordinal, physical SIP slot, two
    // two-word shape IDs, five metadata words, and physical CM index.
    if (targetSipRows.size() != 12u * 12u ||
        currentSipRows.size() != targetSipRows.size())
        fail("joined checkpoint Oracle SIP row layout changed");
    for (PxU32 row = 0; row < 12; ++row)
    {
        const PxU32 start = row * 12;
        for (PxU32 word = 0; word < 6; ++word)
            if (targetSipRows[start + word] !=
                currentSipRows[start + word])
                fail("joined checkpoint Oracle SIP identity/order differs at " +
                     std::to_string(row));
        if (targetSipRows[start + 11] != currentSipRows[start + 11])
            fail("joined checkpoint contact-manager binding differs at " +
                 std::to_string(row) + ": target=" +
                 std::to_string(targetSipRows[start + 11]) +
                 " restored=" +
                 std::to_string(currentSipRows[start + 11]));
    }

    // The full source Oracle is deliberately not A-equal: newly created
    // contacts do not yet have A's touch/report, work-unit, or manifold state.
    if (a.oracle.equals(restored.oracle, difference))
        fail("topology-only bridge unexpectedly restored the full Oracle");
    std::cout << "JOINED_TOPOLOGY_ORACLE_FIRST_REMAINING "
              << difference << '\n';
}

void requireReject(World& world, const Snapshot& b,
                   Sc::NPhaseCore& nphase,
                   const physx333_offline::JoinedTopologyPlanV1& invalid,
                   const char* stage, PxU32 expectedResult)
{
    const PxU32 result =
        physx333_offline::oc2_physx333_joined_topology_recreate_v1(
            &nphase, &invalid);
    if (result != expectedResult)
        fail(std::string(stage) + " unexpected rejection code " +
             std::to_string(result) + " (expected " +
             std::to_string(expectedResult) + ")");
    requireUnchanged(b, captureStopped(world), stage);
    std::cout << "JOINED_REJECT " << stage << " code=" << result << '\n';
}

} // namespace

void runScenario(const char* name, bool warm)
{
    std::cout << "JOINED_SCENARIO " << name << '\n';
    Runtime runtime;
    World world(runtime);
    world.step(0.0f);
    if (warm)
    {
        const Snapshot settled = world.step(0.0f);
        verify(settled, false);
        const Snapshot departed = world.step(-0.2f);
        verify(departed, true);
        const Snapshot returned = world.step(0.0f);
        if (returned.graph.scenePairs.size() != 18 ||
            std::set<PairKey>(returned.graph.scenePairs.begin(),
                              returned.graph.scenePairs.end()) !=
                expectedPairs(false))
            fail("warmup did not reconstruct the 12/4/2 pair graph");
        std::cout << "JOINED_WARMUP 12/4/2 -> 8/2/2 -> 12/4/2\n";
    }
    const Snapshot a = world.step(0.0f);
    verify(a, false);
    const Snapshot b = world.step(-0.2f);
    verify(b, true);
    const auto plan = makePlan(world, a, b);
    for (PxU32 i = 0; i < 4; ++i)
    {
        const PxU32 role = plan.missingContactRoles[i];
        std::cout << "JOINED_CONTACT_FREE step=" << i
                  << " role=" << role
                  << " sip=" << plan.roles[role].targetInteractionPoolSlot
                  << " ap=" << plan.roles[role].targetActorPairPoolSlot
                  << " b_sip_free=" <<
                    part(b.oracle, "nphase.pool.shape_pair.free_order")[i]
                  << " b_ap_free=" <<
                    part(b.oracle, "nphase.pool.actor_pair.free_order")[i]
                  << '\n';
    }
    for (PxU32 i = 0; i < 2; ++i)
    {
        const PxU32 role = plan.missingTriggerRoles[i];
        std::cout << "JOINED_TRIGGER_FREE step=" << i
                  << " role=" << role
                  << " slot=" << plan.roles[role].targetInteractionPoolSlot
                  << " b_free=" << b.aux.triggerPool.freeOrder[i] << '\n';
    }
    requireUnchanged(b, captureStopped(world), "initial B repeat");
    NpScene& np = static_cast<NpScene&>(*world.scene);
    Sc::NPhaseCore& nphase = *np.getScene().getScScene().getNPhaseCore();

    auto invalid = plan;
    invalid.roles[plan.missingContactRoles[0]].shapeCore0 = NULL;
    requireReject(world, b, nphase, invalid, "null contact endpoint",
                  physx333_offline::JoinedTopologyInvalidInput);
    invalid = plan;
    invalid.roles[plan.missingContactRoles[0]].expectedPairFlags ^= 1u;
    requireReject(world, b, nphase, invalid, "contact filter mismatch",
                  physx333_offline::JoinedTopologyUnexpectedFilter);
    invalid = plan;
    invalid.roles[plan.missingTriggerRoles[0]].targetInteractionPoolSlot =
        0xffffffffu;
    requireReject(world, b, nphase, invalid, "trigger slot mismatch",
                  physx333_offline::JoinedTopologyInvalidInput);
    invalid = plan;
    invalid.roles[plan.missingContactRoles[0]].targetActorPairPoolSlot = 31;
    requireReject(world, b, nphase, invalid, "missing ActorPair slot mismatch",
                  physx333_offline::JoinedTopologyPoolMismatch);
    invalid = plan;
    PxU32 survivorTrigger = 0xffffffffu;
    const std::set<PairKey> bKeys(
        b.graph.scenePairs.begin(), b.graph.scenePairs.end());
    for (PxU32 role = 12; role < 16; ++role)
        if (bKeys.count(a.graph.scenePairs[role]))
        { survivorTrigger = role; break; }
    if (survivorTrigger == 0xffffffffu)
        fail("joined fixture has no surviving trigger");
    invalid.roles[survivorTrigger].targetInteractionPoolSlot = 31;
    requireReject(world, b, nphase, invalid, "survivor trigger slot mismatch",
                  physx333_offline::JoinedTopologyPoolMismatch);
    invalid = plan;
    invalid.contactSceneOrder[0] = invalid.contactSceneOrder[1];
    requireReject(world, b, nphase, invalid, "duplicate contact scene role",
                  physx333_offline::JoinedTopologyInvalidInput);
    invalid = plan;
    invalid.actors[8].roleIds[0] = invalid.actors[8].roleIds[1];
    requireReject(world, b, nphase, invalid, "duplicate actor role",
                  physx333_offline::JoinedTopologyInvalidInput);

    const PxU32 result =
        physx333_offline::oc2_physx333_joined_topology_recreate_v1(
            &nphase, &plan);
    if (result != physx333_offline::JoinedTopologySuccess)
        fail("joined source lifecycle rejected valid checkpoint plan, code " +
             std::to_string(result));
    // onOverlapCreated leaves island change queues dirty until a later
    // simulation; the full IslandImage explicitly requires a post-fetch
    // boundary. No simulation is authorized in this topology-only gate.
    const StoppedImage restored = captureTopology(world);
    verifyTopologyReadback(a, b, restored);
    if (runtime.errors.count) fail("PhysX reported an error");
    std::cout << "PASS joined " << name
              << " 12/4/2 topology reconstruction from 8/2/2 "
                 "with seven atomic prewrite rejection controls; "
                 "contact reports/payload and full rewind remain unrestored\n";
}

int main()
{
    runScenario("cold", false);
    runScenario("warm", true);
}
