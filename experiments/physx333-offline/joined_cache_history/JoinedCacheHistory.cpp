// Isolate the game's observed transform-cache LIFO history from the ordinary
// joined fixture. The extra shapes are created and detached through public
// PhysX calls before checkpoint A; no private state is fabricated.
#pragma warning(push)
#pragma warning(disable:4716)
#define main joined_topology_embedded_main
#include "../joined_topology/JoinedTopology.cpp"
#undef main
#pragma warning(pop)

#include "../joined_workunit_restore/JoinedWorkUnitRestore.h"
#include "../memblock_restore/MemBlockRestore.h"

namespace {

void checkCacheHistory(const Snapshot& image)
{
    const std::vector<PxU32> desired = {12, 11, 10};
    if (image.facts.cacheCurrent != 13 ||
        image.facts.cacheLive != 10 ||
        image.facts.cacheRefs != 24 ||
        image.facts.cacheFreeIds != desired ||
        image.facts.contactManagers != 12 ||
        image.facts.largeManifolds != 12 ||
        image.facts.sphereManifolds != 0 ||
        image.facts.islandEdges != 12 ||
        image.facts.touchPairs != 10 ||
        image.facts.reportPairs != 10 ||
        image.graph.scenePairs.size() != 18 ||
        std::set<PairKey>(image.graph.scenePairs.begin(),
                          image.graph.scenePairs.end()) !=
            expectedPairs(false) ||
        image.graph.activeBodies.size() != 5 ||
        image.actorPair.sips.size() != 12 ||
        image.actorPair.actorPairs.size() != 12 ||
        image.aux.triggers.size() != 4 ||
        image.aux.markers.size() != 2 ||
        image.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != 12 ||
        image.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != 4 ||
        image.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2)
        fail("cache-history warmup did not reach the target ledger/graph");
}

void warmCacheHistory(World& world, Runtime& runtime)
{
    const Snapshot initial = world.step(0.0f);
    if (initial.facts.cacheCurrent != 10 ||
        initial.facts.cacheLive != 10 ||
        initial.facts.cacheRefs != 24 ||
        !initial.facts.cacheFreeIds.empty())
        fail("cache-history setup requires the known cold graph ledger");
    PxRigidStatic* fixed = static_cast<PxRigidStatic*>(world.actors[2]);
    if (id(fixed) != 3)
        fail("cache-history setup static actor identity changed");
    PxShape* temporary[4] = {NULL, NULL, NULL, NULL};
    for (unsigned i = 0; i < 4; ++i)
    {
        temporary[i] = runtime.physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *runtime.material);
        if (!temporary[i]) fail("create temporary cache-history shape");
        World::setRole(*temporary[i], ExtraPlainNoTouch);
        fixed->attachShape(*temporary[i]);
        const Snapshot added = world.step(0.0f);
        if (added.facts.cacheCurrent != 11 + i ||
            added.facts.cacheLive != 11 + i)
            fail("temporary shape did not allocate its cache ID");
    }
    const unsigned releaseOrder[4] = {2, 1, 0, 3};
    for (unsigned index : releaseOrder)
    {
        fixed->detachShape(*temporary[index]);
        temporary[index]->release();
        temporary[index] = NULL;
        world.step(0.0f);
    }
    const Snapshot settled = world.step(0.0f);
    checkCacheHistory(settled);
    if (runtime.errors.count)
        fail("PhysX reported an error during cache-history warmup");
    std::cout << "JOINED_CACHE_HISTORY_LEDGER current=13 live=10 refs=24 "
                 "free=[12,11,10]\n";
}

void requireStage(bool okay, const char* stage, const std::string& error,
                  bool verbose)
{
    if (!okay)
        fail(std::string("cache-history joined restore stopped at ") +
             stage + ": " + error);
    if (verbose)
        std::cout << "JOINED_CACHE_HISTORY_STAGE " << stage << " passed\n";
}

void equalStopped(const Snapshot& expected, const Snapshot& actual,
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

void restoreCheckpoint(
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
    requireStage(topologyResult == physx333_offline::JoinedTopologySuccess,
                 "topology", std::to_string(topologyResult), verbose);
    verifyTopologyReadback(a, b, captureTopology(world));
    const PxU32 reportResult =
        physx333_offline::oc2_physx333_joined_report_restore_v1(
            &nphase, &reportPlan);
    requireStage(reportResult == physx333_offline::JoinedReportSuccess,
                 "report/touch", std::to_string(reportResult), verbose);
    verifyReportReadback(contactA, captureContact(world));
    requireStage(physx333_offline::InstallJoinedWorkUnitBindings(
        *world.scene, contactA, error), "WorkUnit bindings", error, verbose);
    requireStage(physx333_offline::RestoreMemBlockPoolForJoin(
        *world.scene, world.memBlockRegistry, blockA, error),
        "memory blocks", error, verbose);
    requireStage(physx333_offline::RestoreJoinedWorkUnitPayload(
        *world.scene, contactA, error), "PCM/contact payload", error, verbose);
    requireStage(oc2::offline::RestoreIslandForJoin(
        *world.scene, a.island, error), "island", error, verbose);
    requireStage(oc2::offline::RestoreSap(
        *world.scene, a.sap, error), "SAP", error, verbose);
    requireStage(oc2::offline::RestoreTransformCache(
        *world.scene, a.cache, error), "transform cache", error, verbose);
    requireStage(oc2::offline::RestoreShapeCacheBindings(
        *world.scene, a.shapeCache, error), "shape-cache IDs", error, verbose);
    requireStage(oc2::offline::RestoreBodies(
        *world.scene, a.bodies, error), "bodies", error, verbose);
    requireStage(oc2::offline::RestoreSceneClock(
        *world.scene, a.clock, error), "scene clock", error, verbose);
    requireStage(oc2::offline::RestoreContextImage(
        *world.scene, a.context, error), "context", error, verbose);
    requireStage(oc2::offline::RestoreQueryImage(
        *world.scene, a.query, error), "query", error, verbose);

    world.callback.rows = a.events; // Fixture output, not PhysX state.
    equalStopped(a, world.capture(), "restored cache-history A");
    std::string difference;
    if (!contactA.exact(captureContact(world), difference))
        fail("restored cache-history A contact image: " + difference);
    checkCacheHistory(a);
    if (verbose)
        std::cout << "JOINED_CACHE_HISTORY_A_READBACK passed\n";
}

void run(bool noInput)
{
    Runtime runtime;
    World world(runtime);
    world.step(0.0f);
    warmCacheHistory(world, runtime);
    const Snapshot a = world.step(0.0f);
    checkCacheHistory(a);
    const physx333_offline::JoinedContactImage contactA = captureContact(world);
    physx333_offline::MemBlockIdentityRegistry checkpointRegistry =
        world.memBlockRegistry;
    physx333_offline::MemBlockRestoreImage blockA;
    std::string error;
    requireStage(physx333_offline::CaptureMemBlockRestore(
        *world.scene, checkpointRegistry, blockA, error),
        "A memory-block capture", error, true);

    // The no-input branch is a separate fresh-process trace. It cannot be
    // taken after 100 direct A/B cycles: that later public-API history puts
    // the dynamic query pruner in a phase this component image rejects.
    Snapshot natural;
    if (noInput) natural = world.advanceWithoutInputs();
    const Snapshot b = world.step(-0.2f);
    const physx333_offline::JoinedContactImage contactB = captureContact(world);
    if (b.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != 8 ||
        b.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != 2 ||
        b.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2)
        fail("cache-history successor interaction topology changed");

    for (unsigned cycle = 0; cycle != 100; ++cycle)
    {
        restoreCheckpoint(world, a, b, contactA, contactB, blockA,
                          cycle == 0);
        if (noInput)
            equalStopped(natural, world.advanceWithoutInputs(),
                         "cache-history no-input successor");
        equalStopped(b, world.step(-0.2f),
                     "cache-history deletion successor");
        if (runtime.errors.count)
            fail("PhysX reported a cache-history replay error");
    }
    std::cout << "PASS joined cache-history "
              << (noInput ? "A->no-input->B" : "direct A/B")
              << " x100\n";
}

} // namespace

int main(int argc, char** argv)
{
    if (argc == 1) run(false);
    else if (argc == 2 && std::strcmp(argv[1], "--no-input") == 0)
        run(true);
    else fail("unknown joined cache-history option");
    return 0;
}
