// Stress joined rewind under source-built PhysX motion and friction history.
#pragma warning(push)
#pragma warning(disable:4716)
#define main joined_topology_embedded_main
#include "../joined_topology/JoinedTopology.cpp"
#undef main
#pragma warning(pop)

#include "../joined_workunit_restore/JoinedWorkUnitRestore.h"
#include "../memblock_restore/MemBlockRestore.h"

namespace {

void requireMotionStage(bool okay, const char* stage,
                        const std::string& error, bool verbose)
{
    if (!okay)
        fail(std::string("joined motion restore stopped at ") + stage +
             ": " + error);
    if (verbose)
        std::cout << "JOINED_MOTION_STAGE " << stage << " passed\n";
}

void equalMotion(const Snapshot& expected, const Snapshot& actual,
                 const char* stage)
{
    std::string difference;
    if (!expected.oracle.equals(actual.oracle, difference))
        fail(std::string(stage) + " Oracle: " + difference);
    if (!expected.aux.equals(actual.aux, difference))
        fail(std::string(stage) + " auxiliary: " + difference);
    if (!(expected.actorPair == actual.actorPair) ||
        !(expected.graph == actual.graph))
        fail(std::string(stage) + " ActorPair or interaction graph");
    if (!expected.sap.equals(actual.sap, difference))
        fail(std::string(stage) + " SAP: " + difference);
    if (!expected.cache.equalsWithRebasedFreeIds(actual.cache, difference))
        fail(std::string(stage) + " transform cache: " + difference);
    if (!expected.island.equals(actual.island, difference))
        fail(std::string(stage) + " island: " + difference);
    if (!expected.memBlocks.equals(actual.memBlocks, difference))
        fail(std::string(stage) + " memory blocks: " + difference);
    if (!expected.shapeCache.equals(actual.shapeCache, difference))
        fail(std::string(stage) + " shape-cache bindings: " + difference);
    if (!expected.bodies.equals(actual.bodies, difference))
        fail(std::string(stage) + " bodies: " + difference);
    if (!expected.clock.equals(actual.clock, difference))
        fail(std::string(stage) + " scene clock: " + difference);
    if (!expected.context.equals(actual.context, difference))
        fail(std::string(stage) + " context: " + difference);
    if (!expected.query.equalsWithRebasedStack(actual.query, difference))
        fail(std::string(stage) + " query: " + difference);
    if (!(expected.facts == actual.facts) ||
        expected.deletedOverlaps != actual.deletedOverlaps ||
        expected.events != actual.events)
        fail(std::string(stage) + " facts, deletions, or ordered callbacks");
}

void restoreMotion(
    World& world, const Snapshot& a, const Snapshot& b,
    const physx333_offline::JoinedContactImage& contactA,
    const physx333_offline::JoinedContactImage& contactB,
    const physx333_offline::MemBlockRestoreImage& blockA,
    bool verbose)
{
    std::string error;
    const physx333_offline::JoinedTopologyPlanV1 plan =
        makePlan(world, a, b, contactA);
    const physx333_offline::JoinedReportPlanV1 reportPlan =
        makeReportPlan(a, contactA, contactB, plan);
    NpScene& np = static_cast<NpScene&>(*world.scene);
    Sc::NPhaseCore& nphase = *np.getScene().getScScene().getNPhaseCore();
    const PxU32 topologyResult =
        physx333_offline::oc2_physx333_joined_topology_recreate_v1(
            &nphase, &plan);
    requireMotionStage(
        topologyResult == physx333_offline::JoinedTopologySuccess,
        "topology", std::to_string(topologyResult), verbose);
    verifyTopologyReadback(a, b, captureTopology(world));
    const PxU32 reportResult =
        physx333_offline::oc2_physx333_joined_report_restore_v1(
            &nphase, &reportPlan);
    requireMotionStage(reportResult == physx333_offline::JoinedReportSuccess,
                       "report/touch", std::to_string(reportResult), verbose);
    verifyReportReadback(contactA, captureContact(world));
    requireMotionStage(physx333_offline::InstallJoinedWorkUnitBindings(
        *world.scene, contactA, error), "WorkUnit bindings", error, verbose);
    requireMotionStage(physx333_offline::RestoreMemBlockPoolForJoin(
        *world.scene, world.memBlockRegistry, blockA, error),
        "memory blocks", error, verbose);
    requireMotionStage(physx333_offline::RestoreJoinedWorkUnitPayload(
        *world.scene, contactA, error), "PCM/contact payload", error, verbose);
    requireMotionStage(oc2::offline::RestoreIslandForJoin(
        *world.scene, a.island, error), "island", error, verbose);
    requireMotionStage(oc2::offline::RestoreSap(
        *world.scene, a.sap, error), "SAP", error, verbose);
    requireMotionStage(oc2::offline::RestoreTransformCache(
        *world.scene, a.cache, error), "transform cache", error, verbose);
    requireMotionStage(oc2::offline::RestoreShapeCacheBindings(
        *world.scene, a.shapeCache, error), "shape-cache IDs", error, verbose);
    requireMotionStage(oc2::offline::RestoreBodies(
        *world.scene, a.bodies, error), "bodies", error, verbose);
    requireMotionStage(oc2::offline::RestoreSceneClock(
        *world.scene, a.clock, error), "scene clock", error, verbose);
    requireMotionStage(oc2::offline::RestoreContextImage(
        *world.scene, a.context, error), "context", error, verbose);
    requireMotionStage(oc2::offline::RestoreQueryImage(
        *world.scene, a.query, error), "query", error, verbose);

    world.callback.rows = a.events;
    equalMotion(a, world.capture(), "restored motion A");
    std::string difference;
    if (!contactA.exact(captureContact(world), difference))
        fail("restored motion A contact image: " + difference);
    if (verbose) std::cout << "JOINED_MOTION_A_READBACK passed\n";
}

void requireGraph(const Snapshot& image, bool deleted)
{
    const PxU32 contacts = deleted ? 8u : 12u;
    const PxU32 triggers = deleted ? 2u : 4u;
    if (image.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != contacts ||
        image.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != triggers ||
        image.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2 ||
        std::set<PairKey>(image.graph.scenePairs.begin(),
                          image.graph.scenePairs.end()) !=
            expectedPairs(deleted) ||
        image.facts.touchPairs != (deleted ? 8u : 10u) ||
        image.facts.reportPairs != (deleted ? 8u : 10u))
        fail("motion successor graph, touch, or report shape differs");
    verifyAux(image, deleted);
    verifyActorPairs(image, deleted);
    if (deleted)
    {
        std::set<PairKey> expectedDeleted;
        for (PxU32 fixed = kExtraFirst; fixed <= kExtraLast; ++fixed)
            expectedDeleted.insert(pair(Sc::PX_INTERACTION_TYPE_OVERLAP,
                                        {kMover, 0}, {fixed, 0}));
        for (PxU32 fixed = kTriggerA; fixed <= kTriggerB; ++fixed)
            expectedDeleted.insert(pair(Sc::PX_INTERACTION_TYPE_TRIGGER,
                                        {kMover, 0}, {fixed, 0}));
        if (image.facts.sapDeletes != 6 ||
            image.deletedOverlaps.size() != 6 ||
            std::set<PairKey>(image.deletedOverlaps.begin(),
                              image.deletedOverlaps.end()) != expectedDeleted)
            fail("motion deletion endpoint has wrong SAP overlap losses");
    }
}

Snapshot dashCheckpoint(World& world)
{
    world.callback.rows.clear();
    const PxReal x[4] = {0.0f, -2.2f, -4.4f, -6.6f};
    for (PxU32 i = 0; i < 4; ++i)
    {
        world.chefs[i]->setGlobalPose(PxTransform(PxVec3(x[i], 0.8f, 0.0f)));
        world.chefs[i]->setLinearVelocity(
            PxVec3(0.0f, 0.0f, i == 0 ? -8.0f : 0.0f));
        world.chefs[i]->setAngularVelocity(PxVec3(0.0f));
    }
    world.idle->setWakeCounter(100.0f);
    world.scene->simulate(kStep);
    if (!world.scene->fetchResults(true))
        fail("fetchResults dash checkpoint");
    return world.capture();
}

void runScenario(bool noInput, bool kinetic)
{
    Runtime runtime;
    World world(runtime);
    if (kinetic)
    {
        PxShape* shapes[2] = {NULL, NULL};
        if (world.chefs[0]->getShapes(shapes, 2) != 2 || !shapes[1])
            fail("kinetic fixture moving auxiliary shape is absent");
        shapes[1]->setGeometry(PxBoxGeometry(1.4f, 1.5f, 1.0f));
    }
    world.step(0.0f);
    const Snapshot initial = world.step(0.0f);
    verify(initial, false);
    world.scene->setGravity(PxVec3(0.0f, -9.81f, 0.0f));
    for (PxU32 i = 1; i < 4; ++i)
        world.chefs[i]->setLinearVelocity(PxVec3(0.6f, 0.0f, 0.0f));
    world.advanceWithoutInputs();
    const Snapshot a = kinetic ? dashCheckpoint(world) : world.step(0.0f);
    std::cout << "MOTION_PROBE A graph=" << a.graph.scenePairs.size()
              << " contacts=" << a.facts.touchPairs
              << " callbacks=" << a.events.size() << '\n';
    for (PxU32 i = 0; i < 4; ++i)
    {
        const PxTransform pose = world.chefs[i]->getGlobalPose();
        const PxVec3 velocity = world.chefs[i]->getLinearVelocity();
        std::cout << "MOTION_BODY " << i << " y=" << pose.p.y
                  << " x=" << pose.p.x << " vx=" << velocity.x
                  << " vy=" << velocity.y
                  << " vz=" << velocity.z << '\n';
        if (kinetic && i == 0 && velocity.z >= -4.0f)
            fail("kinetic checkpoint lost dash velocity");
    }
    requireGraph(a, false);
    const physx333_offline::JoinedContactImage contactA = captureContact(world);
    PxU32 activeFriction = 0;
    for (const auto& row : contactA.rows)
    {
        PxcNpWorkUnit work = {};
        if (row.workUnitBytes.size() != sizeof(work))
            fail("motion WorkUnit size changed");
        std::memcpy(&work, row.workUnitBytes.data(), sizeof(work));
        if (work.frictionPatchCount && work.frictionDataPtr)
            ++activeFriction;
    }
    std::cout << "MOTION_FRICTION active=" << activeFriction << '\n';
    if (activeFriction == 0) fail("motion checkpoint lacks friction data");

    physx333_offline::MemBlockIdentityRegistry checkpointRegistry =
        world.memBlockRegistry;
    physx333_offline::MemBlockRestoreImage blockA;
    std::string error;
    requireMotionStage(physx333_offline::CaptureMemBlockRestore(
        *world.scene, checkpointRegistry, blockA, error),
        "A memory-block capture", error, true);
    std::vector<Snapshot> intermediate;
    Snapshot b;
    if (kinetic)
    {
        // The public-API dash gives a fixed three-frame trace: two moving
        // intermediate boundaries, then the 8/2/2 deletion boundary.
        for (unsigned step = 0; step != 3; ++step)
        {
            Snapshot next = world.advanceWithoutInputs();
            std::cout << "MOTION_KINETIC_STEP " << step
                      << " graph=" << next.graph.scenePairs.size()
                      << " contacts=" << next.facts.touchPairs
                      << " mover_z=" << world.chefs[0]->getGlobalPose().p.z
                      << '\n';
            if (step == 2)
            {
                b = next;
            }
            else
            {
                if (next.graph.counts[
                        Sc::PX_INTERACTION_TYPE_OVERLAP] != 12 ||
                    next.graph.counts[
                        Sc::PX_INTERACTION_TYPE_TRIGGER] != 4)
                    fail("kinetic intermediate topology changed early");
                intermediate.push_back(next);
            }
        }
    }
    else
    {
        if (noInput)
        {
            intermediate.push_back(world.advanceWithoutInputs());
            std::cout << "MOTION_NATURAL graph="
                      << intermediate.back().graph.scenePairs.size()
                      << " contacts=" << intermediate.back().facts.touchPairs
                      << " callbacks=" << intermediate.back().events.size()
                      << '\n';
        }
        b = world.step(-0.2f);
    }
    std::cout << "MOTION_PROBE B graph=" << b.graph.scenePairs.size()
              << " contacts=" << b.facts.touchPairs
              << " callbacks=" << b.events.size()
              << " mover_z=" << world.chefs[0]->getGlobalPose().p.z
              << '\n';
    if (kinetic)
        for (const auto& trigger : b.aux.triggers)
            std::cout << "MOTION_TRIGGER actor="
                      << trigger.pair.shape1.actorId << ':'
                      << trigger.pair.shape1.shapeIndex
                      << " last_touch=" << trigger.lastFrameHadContacts
                      << " flags=" << trigger.triggerFlags
                      << " cache=" << trigger.triggerCacheState << '\n';
    requireGraph(b, true);
    verifyAuxTransition(a, b);
    verifyActorPairTransition(a, b);
    const physx333_offline::JoinedContactImage contactB = captureContact(world);
    for (unsigned cycle = 0; cycle != 100; ++cycle)
    {
        restoreMotion(world, a, b, contactA, contactB, blockA, cycle == 0);
        for (const Snapshot& expected : intermediate)
            equalMotion(expected, world.advanceWithoutInputs(),
                        "replayed no-input motion step");
        equalMotion(b, kinetic ? world.advanceWithoutInputs() :
            world.step(-0.2f), "replayed motion B");
        if (runtime.errors.count)
            fail("PhysX reported a joined motion replay error");
    }
    std::cout << "PASS joined motion "
              << (kinetic ? "kinetic deletion" :
                  noInput ? "no-input suffix" : "direct A/B")
              << " x100\n";
}

} // namespace

int main(int argc, char** argv)
{
    if (argc == 1) runScenario(false, false);
    else if (argc == 2 && std::strcmp(argv[1], "--no-input") == 0)
        runScenario(true, false);
    else if (argc == 2 && std::strcmp(argv[1], "--kinetic") == 0)
        runScenario(false, true);
    else fail("unknown joined motion option");
    return 0;
}
