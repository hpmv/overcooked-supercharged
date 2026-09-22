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

} // namespace

int main()
{
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
