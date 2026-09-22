// Whole-allocation rewind diagnostic for the level-shaped source-built scene.
// This intentionally shares the fixture implementation, not the game runtime.
#define OC2_LEVEL_GRAPH_NO_MAIN
#include "../level_graph/LevelGraph.cpp"
#include "../arena_snapshot/ArenaSnapshot.h"
#include "../arena_snapshot/ArenaAabbPadding.h"

namespace {

void equalSnapshot(const Snapshot& expected, const Snapshot& actual)
{
    std::string difference;
    if (!expected.oracle.equals(actual.oracle, difference) ||
        !expected.aux.equals(actual.aux, difference) ||
        !(expected.actorPair == actual.actorPair) ||
        !expected.sap.equals(actual.sap, difference) ||
        !expected.cache.equals(actual.cache, difference) ||
        !expected.island.equals(actual.island, difference) ||
        !expected.memBlocks.equals(actual.memBlocks, difference) ||
        !expected.shapeCache.equals(actual.shapeCache, difference) ||
        !expected.bodies.equals(actual.bodies, difference) ||
        !expected.clock.equals(actual.clock, difference) ||
        !expected.context.equals(actual.context, difference) ||
        !expected.query.equals(actual.query, difference) ||
        !(expected.graph == actual.graph) ||
        !(expected.facts == actual.facts) ||
        expected.deletedOverlaps != actual.deletedOverlaps ||
        expected.events != actual.events)
        fail("level arena replay image differs: " + difference);
}

void equalInitializedArena(
    const oc2::offline::ArenaSnapshotAllocator::Image& expected,
    const oc2::offline::ArenaSnapshotAllocator::Image& actual)
{
    auto lhs = expected;
    auto rhs = actual;
    std::string error;
    if (!oc2::offline::NormalizeAabbTaskPadding(lhs, error) ||
        !oc2::offline::NormalizeAabbTaskPadding(rhs, error) ||
        !lhs.equals(rhs, error))
        fail("initialized source-built allocation replay: " + error);
}

void checkCacheHistoryWarmup()
{
    Runtime runtime;
    World world(runtime);
    world.step(0.0f);
    const Snapshot initial = world.step(0.0f);
    if (initial.facts.cacheCurrent != 10 ||
        initial.facts.cacheLive != 10 ||
        initial.facts.cacheRefs != 24 ||
        !initial.facts.cacheFreeIds.empty())
        fail("cache warmup requires the known synthetic A allocation history");

    PxRigidStatic* fixed = static_cast<PxRigidStatic*>(world.actors[2]);
    if (id(fixed) != 3) fail("cache warmup static actor identity changed");
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
            fail("temporary contact shape did not allocate the next cache ID");
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
    const std::vector<PxU32> desired = {12, 11, 10};
    const std::set<PairKey> semanticPairs(
        settled.graph.scenePairs.begin(), settled.graph.scenePairs.end());
    if (settled.facts.cacheCurrent != 13 ||
        settled.facts.cacheLive != 10 ||
        settled.facts.cacheRefs != 24 ||
        settled.facts.cacheFreeIds != desired ||
        settled.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != 12 ||
        settled.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != 4 ||
        settled.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2 ||
        settled.graph.activeBodies.size() != 5 ||
        semanticPairs != expectedPairs(false) ||
        semanticPairs.size() != settled.graph.scenePairs.size() ||
        runtime.errors.count)
    {
        std::cerr << "CACHE_HISTORY current=" << settled.facts.cacheCurrent
                  << " live=" << settled.facts.cacheLive
                  << " refs=" << settled.facts.cacheRefs << " free=";
        for (PxU32 value : settled.facts.cacheFreeIds)
            std::cerr << value << ',';
        std::cerr << '\n';
        fail("temporary contact history did not reproduce target cache ledger");
    }
    std::cout << "PASS source-built four-shape public warmup reaches "
                 "cache currentId=13 live=10 refs=24 free=[12,11,10]\n";
}

} // namespace

int main(int argc, char** argv)
{
    if (argc == 2 && std::strcmp(argv[1], "--cache-history") == 0)
    {
        checkCacheHistoryWarmup();
        return 0;
    }
    if (argc != 1) fail("unknown level-arena diagnostic option");
    oc2::offline::ArenaSnapshotAllocator arena(256u * 1024u * 1024u);
    if (!arena.valid()) fail("reserve PhysX diagnostic arena");
    Runtime runtime(&arena);
    World world(runtime);
    world.step(0.0f);
    const Snapshot a = world.step(0.0f);
    verify(a, false);
    const auto arenaA = arena.capture();
    const PxReal trajectory[5] = {-0.2f, -0.2f, 0.0f, -0.2f, 0.0f};
    std::vector<Snapshot> reference;
    std::vector<oc2::offline::ArenaSnapshotAllocator::Image> referenceArena;
    for (PxReal pose : trajectory)
    {
        reference.push_back(world.step(pose));
        referenceArena.push_back(arena.capture());
    }
    verify(reference[0], true);
    verifyAuxTransition(a, reference[0]);

    for (unsigned cycle = 0; cycle != 100; ++cycle)
    {
        std::string error;
        if (!arena.restore(arenaA, error))
            fail("restore source-built arena: " + error);
        for (unsigned step = 0; step != 5; ++step)
        {
            const Snapshot replay = world.step(trajectory[step]);
            equalSnapshot(reference[step], replay);
            const auto arenaReplay = arena.capture();
            equalInitializedArena(referenceArena[step], arenaReplay);
        }
        // The third reference step is a second, non-adjacent checkpoint C
        // after the six overlaps have reappeared. Rewind there from the end
        // of the suffix and verify its separate leave/return continuation.
        if (!arena.restore(referenceArena[2], error))
            fail("restore non-adjacent source-built checkpoint: " + error);
        for (unsigned step = 3; step != 5; ++step)
        {
            const Snapshot replay = world.step(trajectory[step]);
            equalSnapshot(reference[step], replay);
            const auto arenaReplay = arena.capture();
            equalInitializedArena(referenceArena[step], arenaReplay);
        }
        if (runtime.errors.count) fail("PhysX reported an arena replay error");
    }
    std::cout << "PASS level-like shared-endpoint full raw-allocation "
                 "12/4/2 -> 8/2/2 five-step and non-adjacent suffixes x100\n";
}
