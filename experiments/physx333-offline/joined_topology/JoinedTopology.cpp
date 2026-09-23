#define OC2_LEVEL_GRAPH_NO_MAIN
#include "../level_graph/LevelGraph.cpp"
#include "../joined_contact_image/JoinedContactImage.h"
#include "JoinedTopologyBridge.h"

#include <map>
#include <set>
#include <stdexcept>
#include <cstring>

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

physx333_offline::JoinedContactImage captureContact(World& world)
{
    physx333_offline::JoinedContactImage image;
    std::string error;
    if (!physx333_offline::CaptureJoinedContactImage(
            *world.scene, image, error))
        fail("joined contact image: " + error);
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
    World& world, const Snapshot& a, const Snapshot& b,
    const physx333_offline::JoinedContactImage& contactA)
{
    using namespace physx333_offline;
    if (a.graph.scenePairs.size() != 18 ||
        a.actorPair.sips.size() != 12 || a.aux.triggers.size() != 4 ||
        a.aux.markers.size() != 2 || a.graph.actors.size() != 13 ||
        contactA.rows.size() != 12)
        fail("joined checkpoint images have unexpected counts");
    NpScene& np = static_cast<NpScene&>(*world.scene);
    PxsContext* context = np.getScene().getScScene()
        .getInteractionScene().getLowLevelContext();
    if (!context) fail("joined successor has no low-level context");
    const auto& manifoldPool = context->mManifoldPool;
    if (manifoldPool.mSlabs.size() != 1 ||
        manifoldPool.mElementsPerSlab != 32)
        fail("joined successor large-manifold pool shape changed");
    const std::uintptr_t manifoldBase =
        reinterpret_cast<std::uintptr_t>(manifoldPool.mSlabs[0]);
    const std::uintptr_t manifoldBytes = 32u *
        sizeof(Gu::LargePersistentContactManifold);
    const LiveCores live = captureLiveCores(world);
    JoinedTopologyPlanV1 plan = {};
    const PxU32 contactFlags = static_cast<PxU32>(
        PxPairFlag::eCONTACT_DEFAULT | PxPairFlag::eNOTIFY_TOUCH_FOUND |
        PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
        PxPairFlag::eNOTIFY_TOUCH_LOST);
    const auto& sipRows = part(a.oracle, "nphase.shape_pairs");
    const auto& edgeRows = part(a.oracle, "island.edges.active");
    if (sipRows.size() != 12u * 12u || edgeRows.size() % 6u)
        fail("joined source contact/edge row layout changed");
    std::map<PxU32, PxU32> edgeByManager;
    for (size_t offset = 0; offset < edgeRows.size(); offset += 6)
        if (edgeRows[offset + 5] != 0xffffffffu &&
            !edgeByManager.insert(std::make_pair(
                edgeRows[offset + 5], edgeRows[offset])).second)
            fail("joined source has duplicate island edge manager owner");
    std::map<PairKey, PxU32> roleByKey;
    for (PxU32 i = 0; i < 18; ++i)
    {
        const PairKey& key = a.graph.scenePairs[i];
        if (!roleByKey.insert(std::make_pair(key, i)).second)
            fail("joined checkpoint has duplicate pair keys");
        JoinedTopologyRoleV1& role = plan.roles[i];
        role.type = key.type;
        role.targetActorPairPoolSlot = 0xffffffffu;
        role.targetContactManagerIndex = 0xffffffffu;
        role.targetIslandEdgeId = 0xffffffffu;
        role.targetManifoldPoolSlot = 0xffffffffu;
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
            if (sipRows[i * 12u + 1] != sip.sipSlot ||
                sipRows[i * 12u + 11] == 0xffffffffu)
                fail("joined source SIP/manager identity differs");
            role.targetContactManagerIndex = sipRows[i * 12u + 11];
            const JoinedContactRow& contact = contactA.rows[i];
            if (contact.sceneIndex != i ||
                contact.key.shape0.actor != s0.actor ||
                contact.key.shape0.shape != s0.shape ||
                contact.key.shape1.actor != s1.actor ||
                contact.key.shape1.shape != s1.shape ||
                contact.managerSlot != role.targetContactManagerIndex ||
                contact.manifoldKind != 1 ||
                contact.workUnitBytes.size() != sizeof(PxcNpWorkUnit))
                fail("joined contact/manifold checkpoint row differs");
            PxcNpWorkUnit work;
            std::memcpy(&work, contact.workUnitBytes.data(), sizeof(work));
            const std::uintptr_t manifold = work.pairCache.manifold;
            if (!manifold || (manifold & 15u) ||
                manifold < manifoldBase ||
                manifold >= manifoldBase + manifoldBytes ||
                (manifold - manifoldBase) %
                    sizeof(Gu::LargePersistentContactManifold))
                fail("joined target manifold is outside retained pool");
            role.targetManifoldAddress = reinterpret_cast<void*>(manifold);
            role.targetManifoldPoolSlot = static_cast<PxU32>(
                (manifold - manifoldBase) /
                sizeof(Gu::LargePersistentContactManifold));
            auto edge = edgeByManager.find(role.targetContactManagerIndex);
            if (edge == edgeByManager.end())
                fail("joined source manager has no island edge");
            role.targetIslandEdgeId = edge->second;
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

ShapeKey shapeKey(const physx333_offline::JoinedContactShapeKey& shape)
{
    ShapeKey key; key.actor = shape.actor; key.shape = shape.shape;
    return key;
}

void fillReportBitmap(physx333_offline::JoinedReportBitmapV1& destination,
                      const physx333_offline::JoinedContactBitmap& source)
{
    if (source.words.size() > 64)
        fail("joined report bitmap exceeds fixed plan capacity");
    destination.expectedStorage = reinterpret_cast<void*>(source.address);
    destination.count = static_cast<PxU32>(source.words.size());
    std::copy(source.words.begin(), source.words.end(), destination.words);
}

physx333_offline::JoinedReportPlanV1 makeReportPlan(
    const Snapshot& a, const physx333_offline::JoinedContactImage& contactA,
    const physx333_offline::JoinedContactImage& contactB,
    const physx333_offline::JoinedTopologyPlanV1& topology)
{
    using namespace physx333_offline;
    if (contactA.rows.size() != 12 || contactB.rows.size() != 8 ||
        contactA.actorPairs.reportDataPool.usedCount != 10 ||
        contactB.actorPairs.reportDataPool.usedCount != 8 ||
        contactA.persistentEventOrder.size() != 10 ||
        contactB.persistentEventOrder.size() != 8 ||
        !contactA.forceThresholdEventOrder.empty() ||
        !contactB.forceThresholdEventOrder.empty() ||
        !contactA.actorPairs.reportSetOrder.empty() ||
        !contactB.actorPairs.reportSetOrder.empty() ||
        contactA.reportBufferIndex || contactB.reportBufferIndex ||
        !contactA.reportBufferBytes.empty() ||
        !contactB.reportBufferBytes.empty())
        fail("joined report checkpoint topology is unsupported");
    JoinedReportPlanV1 plan = {};
    std::map<JoinedContactKey, PxU32> roleByKey;
    std::map<PxU32, PxU32> missingByReportSlot;
    for (PxU32 i = 0; i < 12; ++i)
    {
        const JoinedContactRow& source = contactA.rows[i];
        const PairKey key = pair(Sc::PX_INTERACTION_TYPE_OVERLAP,
            shapeKey(source.key.shape0), shapeKey(source.key.shape1));
        if (!(a.graph.scenePairs[i] == key) ||
            !roleByKey.insert(std::make_pair(source.key, i)).second ||
            topology.roles[i].targetInteractionPoolSlot != source.sipSlot ||
            topology.roles[i].targetActorPairPoolSlot != source.actorPairSlot ||
            topology.roles[i].targetContactManagerIndex != source.managerSlot)
            fail("joined contact row differs from topology role");
        JoinedReportRoleV1& target = plan.roles[i];
        target.shapeCore0 = topology.roles[i].shapeCore0;
        target.shapeCore1 = topology.roles[i].shapeCore1;
        target.sipSlot = source.sipSlot;
        target.actorPairSlot = source.actorPairSlot;
        target.managerSlot = source.managerSlot;
        target.reportPoolSlot = source.reportPoolSlot;
        target.sipFlags = source.sipFlags;
        target.reportStamp = source.reportStamp;
        target.reportPairIndex = source.reportPairIndex;
        target.reportStreamIndex = source.reportStreamIndex;
        target.actorPairFlags = source.actorPairFlags;
        target.touchCount = source.actorPairTouchCount;
        target.refCount = source.actorPairRefCount;
        target.reportResetStamp = source.reportResetStamp;
        target.reportActorAId = source.reportActorA;
        target.reportActorBId = source.reportActorB;
        target.reportStreamSize =
            static_cast<PxU32>(source.reportStreamManager.size());
        if (target.reportStreamSize > sizeof(target.reportStreamBytes))
            fail("joined contact report stream manager exceeds plan");
        std::copy(source.reportStreamManager.begin(),
                  source.reportStreamManager.end(), target.reportStreamBytes);
        target.managerFlags = source.managerFlags;
        target.managerStatusFlags = source.managerStatusFlags;
        const auto bRow = std::find_if(contactB.rows.begin(),
            contactB.rows.end(), [&](const JoinedContactRow& row) {
                return row.key == source.key;
            });
        if (bRow == contactB.rows.end() &&
            source.reportPoolSlot != 0xffffffffu)
            if (!missingByReportSlot.insert(std::make_pair(
                    source.reportPoolSlot, i)).second)
                fail("joined missing report pool slot is duplicated");
    }
    if (missingByReportSlot.size() != 2)
        fail("joined report stage expected exactly two new owners");
    const auto& bFree = contactB.actorPairs.reportDataPool.freeOrder;
    const auto& aFree = contactA.actorPairs.reportDataPool.freeOrder;
    if (bFree.size() != 24 || aFree.size() != 22 ||
        !std::equal(aFree.begin(), aFree.end(), bFree.begin() + 2))
        fail("joined report pool untouched free tail differs");
    plan.currentReportFreeCount = static_cast<PxU32>(bFree.size());
    plan.targetReportFreeCount = static_cast<PxU32>(aFree.size());
    std::copy(bFree.begin(), bFree.end(), plan.currentReportFreeOrder);
    std::copy(aFree.begin(), aFree.end(), plan.targetReportFreeOrder);
    for (PxU32 i = 0; i < 2; ++i)
    {
        const auto found = missingByReportSlot.find(bFree[i]);
        if (found == missingByReportSlot.end())
            fail("joined source report free head cannot realize checkpoint");
        plan.createRoles[i] = found->second;
    }
    for (PxU32 i = 0; i < 10; ++i)
        plan.persistentRoles[i] = roleByKey.at(
            contactA.persistentEventOrder[i]);
    plan.targetNextPersistent = contactA.nextPersistentPair;
    plan.reportBufferIndex = contactA.reportBufferIndex;
    plan.reportBufferSize = contactA.reportBufferSize;
    plan.reportBufferDefaultSize = contactA.reportBufferDefaultSize;
    plan.reportBufferLastIndex = contactA.reportBufferLastIndex;
    plan.reportBufferAllocationLocked =
        contactA.reportBufferAllocationLocked;
    fillReportBitmap(plan.managerUse, contactA.managerUse);
    fillReportBitmap(plan.activeManagers, contactA.activeManagers);
    fillReportBitmap(plan.modifiableManagers, contactA.modifiableManagers);
    fillReportBitmap(plan.touchEventManagers, contactA.touchEventManagers);
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
    const auto& targetEdges = part(a.oracle, "island.edges.active");
    const auto& currentEdges = part(restored.oracle,
                                    "island.edges.active");
    if (targetEdges.size() % 6u || currentEdges.size() % 6u)
        fail("joined island edge row layout changed");
    std::map<PxU32, PxU32> targetEdgeByManager, liveEdgeByManager;
    for (size_t offset = 0; offset < targetEdges.size(); offset += 6)
        if (targetEdges[offset + 5] != 0xffffffffu &&
            !targetEdgeByManager.insert(std::make_pair(
                targetEdges[offset + 5], targetEdges[offset])).second)
            fail("joined checkpoint has duplicate manager/edge owner");
    for (size_t offset = 0; offset < currentEdges.size(); offset += 6)
        if (currentEdges[offset + 5] != 0xffffffffu &&
            !liveEdgeByManager.insert(std::make_pair(
                currentEdges[offset + 5], currentEdges[offset])).second)
            fail("joined restored state has duplicate manager/edge owner");
    if (targetEdgeByManager.size() != 12 ||
        liveEdgeByManager != targetEdgeByManager)
        fail("joined checkpoint manager/island-edge binding differs");
    const auto& bManagerFree = part(b.oracle, "contact.pool.free_order");
    const auto& liveManagerFree = part(restored.oracle,
                                       "contact.pool.free_order");
    const auto& bEdgeFree = part(b.oracle, "island.edges.free_order");
    const auto& liveEdgeFree = part(restored.oracle,
                                    "island.edges.free_order");
    if (bManagerFree.size() < 4 || bEdgeFree.size() < 4 ||
        liveManagerFree.size() + 4 != bManagerFree.size() ||
        liveEdgeFree.size() + 4 != bEdgeFree.size() ||
        !std::equal(liveManagerFree.begin(), liveManagerFree.end(),
                    bManagerFree.begin()) ||
        !std::equal(liveEdgeFree.begin(), liveEdgeFree.end(),
                    bEdgeFree.begin() + 4))
        fail("joined manager/edge untouched free tails changed");

    // The full source Oracle is deliberately not A-equal: newly created
    // contacts do not yet have A's touch/report, work-unit, or manifold state.
    if (a.oracle.equals(restored.oracle, difference))
        fail("topology-only bridge unexpectedly restored the full Oracle");
    std::cout << "JOINED_TOPOLOGY_ORACLE_FIRST_REMAINING "
              << difference << '\n';
}

void verifyReportReadback(
    const physx333_offline::JoinedContactImage& target,
    const physx333_offline::JoinedContactImage& restored)
{
    if (!(target.actorPairs == restored.actorPairs))
        fail("joined checkpoint full ActorPair/report graph differs");
    if (target.rows.size() != 12 || restored.rows.size() != 12)
        fail("joined report row inventory differs");
    for (PxU32 i = 0; i < 12; ++i)
    {
        const auto& a = target.rows[i];
        const auto& b = restored.rows[i];
        if (!(a.key == b.key) ||
            a.sipSlot != b.sipSlot ||
            a.actorPairSlot != b.actorPairSlot ||
            a.managerSlot != b.managerSlot ||
            a.islandEdge != b.islandEdge ||
            a.sipFlags != b.sipFlags ||
            a.reportStamp != b.reportStamp ||
            a.reportPairIndex != b.reportPairIndex ||
            a.reportStreamIndex != b.reportStreamIndex ||
            a.actorPairFlags != b.actorPairFlags ||
            a.actorPairTouchCount != b.actorPairTouchCount ||
            a.actorPairRefCount != b.actorPairRefCount ||
            a.reportPoolSlot != b.reportPoolSlot ||
            a.reportResetStamp != b.reportResetStamp ||
            a.reportActorA != b.reportActorA ||
            a.reportActorB != b.reportActorB ||
            a.reportStreamManager != b.reportStreamManager ||
            a.managerFlags != b.managerFlags ||
            a.managerStatusFlags != b.managerStatusFlags)
            fail("joined report/touch row differs at role " +
                 std::to_string(i));
    }
    if (target.persistentEventOrder != restored.persistentEventOrder ||
        target.nextPersistentPair != restored.nextPersistentPair ||
        target.forceThresholdEventOrder !=
            restored.forceThresholdEventOrder ||
        target.managerUse.words != restored.managerUse.words ||
        target.activeManagers.words != restored.activeManagers.words ||
        target.modifiableManagers.words !=
            restored.modifiableManagers.words ||
        target.touchEventManagers.words !=
            restored.touchEventManagers.words ||
        target.reportBufferIndex != restored.reportBufferIndex ||
        target.reportBufferSize != restored.reportBufferSize ||
        target.reportBufferDefaultSize !=
            restored.reportBufferDefaultSize ||
        target.reportBufferLastIndex != restored.reportBufferLastIndex ||
        target.reportBufferAllocationLocked !=
            restored.reportBufferAllocationLocked ||
        target.reportBufferBytes != restored.reportBufferBytes)
        fail("joined ordered event, bitmap, or report buffer differs");
    const char* exactSections[] = {
        "nphase.shape_pairs", "nphase.actor_pairs",
        "nphase.event_lists", "nphase.report_buffer",
        "nphase.pool.actor_pair_report.header",
        "nphase.pool.actor_pair_report.free_order"
    };
    for (const char* section : exactSections)
        if (part(target.oracle, section) !=
            part(restored.oracle, section))
            fail(std::string("joined report Oracle section differs: ") +
                 section);
    const auto& targetManagers = part(target.oracle, "contact.managers");
    const auto& currentManagers = part(restored.oracle, "contact.managers");
    if (targetManagers.size() != 12u * 14u ||
        currentManagers.size() != targetManagers.size())
        fail("joined contact manager Oracle row layout differs");
    for (PxU32 i = 0; i < 12; ++i)
        for (PxU32 word = 0; word < 3; ++word)
            if (targetManagers[i * 14u + word] !=
                currentManagers[i * 14u + word])
                fail("joined contact manager report/status words differ at " +
                     std::to_string(i));
    std::string difference;
    if (target.oracle.equals(restored.oracle, difference))
        fail("report-only stage unexpectedly restored the full Oracle");
    std::cout << "JOINED_REPORT_ORACLE_FIRST_REMAINING "
              << difference << '\n';
}

void requireReportReject(
    World& world, Sc::NPhaseCore& nphase,
    const StoppedImage& baseline,
    const physx333_offline::JoinedReportPlanV1& invalid,
    const char* name, PxU32 expectedResult)
{
    const PxU32 result =
        physx333_offline::oc2_physx333_joined_report_restore_v1(
            &nphase, &invalid);
    if (result != expectedResult)
        fail(std::string(name) + " report rejection code " +
             std::to_string(result) + " expected " +
             std::to_string(expectedResult));
    const StoppedImage current = captureTopology(world);
    std::string difference;
    if (!baseline.oracle.equals(current.oracle, difference) ||
        !baseline.aux.equals(current.aux, difference) ||
        !(baseline.actorPair == current.actorPair) ||
        !(baseline.graph == current.graph))
        fail(std::string(name) + " mutated joined topology: " + difference);
    std::cout << "JOINED_REPORT_REJECT " << name << " code=" << result
              << '\n';
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
    const physx333_offline::JoinedContactImage contactA =
        captureContact(world);
    const Snapshot b = world.step(-0.2f);
    verify(b, true);
    const physx333_offline::JoinedContactImage contactB =
        captureContact(world);
    const auto plan = makePlan(world, a, b, contactA);
    const auto reportPlan = makeReportPlan(a, contactA, contactB, plan);
    const std::set<PairKey> bKeys(
        b.graph.scenePairs.begin(), b.graph.scenePairs.end());
    const auto& bManagerFree = part(b.oracle, "contact.pool.free_order");
    const auto& bEdgeFree = part(b.oracle, "island.edges.free_order");
    if (bManagerFree.size() < 4 || bEdgeFree.size() < 4)
        fail("joined successor manager/edge free chains are too short");
    bool managerReorder = false, edgeReorder = false;
    for (PxU32 i = 0; i < 4; ++i)
    {
        const PxU32 role = plan.missingContactRoles[i];
        managerReorder |= bManagerFree[bManagerFree.size() - 1 - i] !=
                          plan.roles[role].targetContactManagerIndex;
        edgeReorder |= bEdgeFree[i] !=
                       plan.roles[role].targetIslandEdgeId;
        std::cout << "JOINED_CONTACT_FREE step=" << i
                  << " role=" << role
                  << " sip=" << plan.roles[role].targetInteractionPoolSlot
                  << " ap=" << plan.roles[role].targetActorPairPoolSlot
                  << " b_sip_free=" <<
                    part(b.oracle, "nphase.pool.shape_pair.free_order")[i]
                  << " b_ap_free=" <<
                    part(b.oracle, "nphase.pool.actor_pair.free_order")[i]
                  << " cm=" << plan.roles[role].targetContactManagerIndex
                  << " b_cm_next=" <<
                    bManagerFree[bManagerFree.size() - 1 - i]
                  << " edge=" << plan.roles[role].targetIslandEdgeId
                  << " b_edge_next=" << bEdgeFree[i]
                  << " manifold=" <<
                    plan.roles[role].targetManifoldPoolSlot
                  << '\n';
    }
    std::cout << "JOINED_POOL_REORDER cm=" << managerReorder
              << " edge=" << edgeReorder << '\n';
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
    invalid.roles[plan.missingContactRoles[0]].targetContactManagerIndex =
        0xffffffffu;
    requireReject(world, b, nphase, invalid, "missing manager index mismatch",
                  physx333_offline::JoinedTopologyPoolMismatch);
    invalid = plan;
    invalid.roles[plan.missingContactRoles[0]].targetIslandEdgeId =
        0xffffffffu;
    requireReject(world, b, nphase, invalid, "missing island edge mismatch",
                  physx333_offline::JoinedTopologyPoolMismatch);
    invalid = plan;
    invalid.roles[plan.missingContactRoles[0]].targetManifoldPoolSlot = 31;
    requireReject(world, b, nphase, invalid, "missing manifold slot mismatch",
                  physx333_offline::JoinedTopologyPoolMismatch);
    PxU32 survivorContact = 0xffffffffu;
    for (PxU32 role = 0; role < 12; ++role)
        if (bKeys.count(a.graph.scenePairs[role]))
        { survivorContact = role; break; }
    if (survivorContact == 0xffffffffu)
        fail("joined fixture has no surviving contact");
    invalid = plan;
    invalid.roles[survivorContact].targetManifoldAddress =
        plan.roles[plan.missingContactRoles[0]].targetManifoldAddress;
    requireReject(world, b, nphase, invalid,
                  "survivor manifold address mismatch",
                  physx333_offline::JoinedTopologyPoolMismatch);
    invalid = plan;
    PxU32 survivorTrigger = 0xffffffffu;
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
    const physx333_offline::JoinedContactImage topologyContact =
        captureContact(world);
    if (topologyContact.rows.size() != 12)
        fail("joined topology did not restore twelve manifold owners");
    for (PxU32 i = 0; i < 12; ++i)
    {
        const auto& row = topologyContact.rows[i];
        if (row.workUnitBytes.size() != sizeof(PxcNpWorkUnit))
            fail("joined topology WorkUnit size changed");
        PxcNpWorkUnit work;
        std::memcpy(&work, row.workUnitBytes.data(), sizeof(work));
        if (work.pairCache.manifold != reinterpret_cast<std::uintptr_t>(
                plan.roles[i].targetManifoldAddress))
            fail("joined topology manifold address readback differs");
    }
    std::cout << "JOINED_MANIFOLD_BINDINGS 12/12\n";
    const StoppedImage reportBaseline = captureTopology(world);
    auto invalidReport = reportPlan;
    invalidReport.roles[0].shapeCore0 = NULL;
    requireReportReject(world, nphase, reportBaseline, invalidReport,
        "null report endpoint", physx333_offline::JoinedReportPairMismatch);
    invalidReport = reportPlan;
    invalidReport.roles[reportPlan.createRoles[0]].reportPoolSlot = 31;
    requireReportReject(world, nphase, reportBaseline, invalidReport,
        "wrong report pool slot", physx333_offline::JoinedReportPoolMismatch);
    invalidReport = reportPlan;
    invalidReport.persistentRoles[0] = invalidReport.persistentRoles[1];
    requireReportReject(world, nphase, reportBaseline, invalidReport,
        "duplicate persistent event", physx333_offline::JoinedReportInvalidInput);
    invalidReport = reportPlan;
    invalidReport.managerUse.expectedStorage = NULL;
    requireReportReject(world, nphase, reportBaseline, invalidReport,
        "wrong bitmap storage", physx333_offline::JoinedReportUnsupportedScene);
    const PxU32 reportResult =
        physx333_offline::oc2_physx333_joined_report_restore_v1(
            &nphase, &reportPlan);
    if (reportResult != physx333_offline::JoinedReportSuccess)
        fail("joined source report stage rejected valid plan, code " +
             std::to_string(reportResult));
    verifyReportReadback(contactA, captureContact(world));
    if (runtime.errors.count) fail("PhysX reported an error");
    std::cout << "PASS joined " << name
              << " 12/4/2 topology, twelve PCM pool identities, and ten/eight "
                 "report ownership reconstruction with fifteen prewrite "
                 "rejection controls; "
                 "contact payload and full rewind remain unrestored\n";
}

int main()
{
    runScenario("cold", false);
    runScenario("warm", true);
}
