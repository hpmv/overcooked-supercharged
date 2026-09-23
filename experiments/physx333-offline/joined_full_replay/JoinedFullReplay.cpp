// Join the existing source-native topology and contact-payload stages with
// the independent stopped-scene component restorers. A failing stage exits
// immediately; no partially restored scene is ever simulated or torn down.
#pragma warning(push)
#pragma warning(disable:4716)
#define main joined_topology_embedded_main
#include "../joined_topology/JoinedTopology.cpp"
#undef main
#pragma warning(pop)

#include "../joined_workunit_restore/JoinedWorkUnitRestore.h"
#include "../memblock_restore/MemBlockRestore.h"

namespace {

void equalStopped(const Snapshot& expected, const Snapshot& actual,
                  const char* stage, bool rebuiltColdQueryTree)
{
    std::string difference;
    if (!expected.oracle.equals(actual.oracle, difference))
        fail(std::string(stage) + " Oracle: " + difference);
    if (!expected.aux.equals(actual.aux, difference))
        fail(std::string(stage) + " auxiliary interactions: " + difference);
    if (!(expected.actorPair == actual.actorPair))
        fail(std::string(stage) + " ActorPair/report graph");
    if (!(expected.graph == actual.graph))
        fail(std::string(stage) + " interaction graph");
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
    bool queryEqual = rebuiltColdQueryTree ?
        expected.query.equalsWithRebuiltColdTree(actual.query, difference) :
        expected.query.equalsWithRebasedStack(actual.query, difference);
    if (!queryEqual)
    {
        const auto field = [](const oc2::offline::QueryImage& image,
                              const char* name)
            -> const oc2::offline::QueryImage::Field* {
            for (const auto& row : image.fields)
                if (row.name == name) return &row;
            return nullptr;
        };
        const auto* lhs = field(expected.query,
            "dynamic.newTreeStorage.stackStorage");
        const auto* rhs = field(actual.query,
            "dynamic.newTreeStorage.stackStorage");
        if (lhs && rhs)
        {
            const auto word = [&](const oc2::offline::QueryImage& image,
                                  const char* name) -> PxU32 {
                const auto* row = field(image, name);
                PxU32 value = 0xffffffffu;
                if (row && row->bytes.size() == sizeof(value))
                    std::memcpy(&value, row->bytes.data(), sizeof(value));
                return value;
            };
            const std::size_t count = (std::min)(lhs->bytes.size(),
                                                 rhs->bytes.size());
            std::size_t first = 0;
            while (first < count && lhs->bytes[first] == rhs->bytes[first])
                ++first;
            std::cerr << "QUERY_STACK_DIAGNOSTIC expected_address="
                      << lhs->address << " actual_address=" << rhs->address
                      << " expected_bytes=" << lhs->bytes.size()
                      << " actual_bytes=" << rhs->bytes.size()
                      << " first_byte=" << first
                      << " expected_size=" << word(expected.query,
                            "dynamic.newTreeStorage.stackSize")
                      << " actual_size=" << word(actual.query,
                            "dynamic.newTreeStorage.stackSize")
                      << " expected_nodes=" << word(expected.query,
                            "dynamic.newTreeStorage.nodesPointer")
                      << " actual_nodes=" << word(actual.query,
                            "dynamic.newTreeStorage.nodesPointer");
            if (first < count)
                std::cerr << " expected_value=" << unsigned(lhs->bytes[first])
                          << " actual_value=" << unsigned(rhs->bytes[first]);
            std::cerr << '\n';

            // This one fixture may compare the source FIFO's live entries
            // rather than its unused Ps::Array capacity tail. FIFOStack2::pop
            // reads only entries below mStack.size(), and a later pushBack
            // placement-constructs at that size before it can be read. Do
            // not change the general QueryImage comparator or any restore
            // guard, and do not normalize a differing live entry or field.
            if (!rebuiltColdQueryTree &&
                difference == "query image differs at " + lhs->name)
            {
                const PxU32 lhsSize = word(expected.query,
                    "dynamic.newTreeStorage.stackSize");
                const PxU32 rhsSize = word(actual.query,
                    "dynamic.newTreeStorage.stackSize");
                const PxU32 lhsCapacity = word(expected.query,
                    "dynamic.newTreeStorage.stackCapacity");
                const PxU32 rhsCapacity = word(actual.query,
                    "dynamic.newTreeStorage.stackCapacity");
                const std::size_t entryBytes = 2u * sizeof(void*);
                const std::size_t liveBytes = std::size_t(lhsSize) * entryBytes;
                if (entryBytes == 8 && lhsSize == rhsSize &&
                    lhsCapacity == rhsCapacity && lhsSize <= lhsCapacity &&
                    lhs->bytes.size() == std::size_t(lhsCapacity) * entryBytes &&
                    rhs->bytes.size() == lhs->bytes.size() &&
                    first >= liveBytes &&
                    std::equal(lhs->bytes.begin(),
                               lhs->bytes.begin() + liveBytes,
                               rhs->bytes.begin()))
                {
                    auto normalizedExpected = expected.query;
                    auto normalizedActual = actual.query;
                    const auto normalize = [](oc2::offline::QueryImage& image,
                                              std::size_t used) {
                        for (auto& row : image.fields)
                            if (row.name ==
                                "dynamic.newTreeStorage.stackStorage")
                                std::fill(row.bytes.begin() + used,
                                          row.bytes.end(), 0);
                    };
                    normalize(normalizedExpected, liveBytes);
                    normalize(normalizedActual, liveBytes);
                    std::string normalizedDifference;
                    queryEqual = normalizedExpected.equalsWithRebasedStack(
                        normalizedActual, normalizedDifference);
                    if (queryEqual)
                        std::cout << "QUERY_STACK_UNUSED_TAIL_ONLY size="
                                  << lhsSize << " capacity=" << lhsCapacity
                                  << " first_differing_byte=" << first << '\n';
                    else
                        difference = normalizedDifference;
                }
            }
        }
        if (!queryEqual)
            fail(std::string(stage) + " query: " + difference);
    }
    if (!(expected.facts == actual.facts) ||
        expected.deletedOverlaps != actual.deletedOverlaps ||
        expected.events != actual.events)
        fail(std::string(stage) + " facts, ordered deletions, or callbacks");
}

void requireStage(bool okay, const char* stage, const std::string& error,
                  bool verbose = true)
{
    if (!okay)
        fail(std::string("joined full replay stopped at ") + stage +
             ": " + error);
    if (verbose)
        std::cout << "JOINED_FULL_STAGE " << stage << " passed\n";
}

void restoreCheckpoint(
    World& world, const Snapshot& target, const Snapshot& successor,
    const physx333_offline::JoinedContactImage& targetContact,
    const physx333_offline::JoinedContactImage& successorContact,
    const physx333_offline::MemBlockRestoreImage& targetBlocks,
    bool verbose)
{
    std::string error;
    const physx333_offline::JoinedTopologyPlanV1 plan =
        makePlan(world, target, successor, targetContact);
    const physx333_offline::JoinedReportPlanV1 reportPlan =
        makeReportPlan(target, targetContact, successorContact, plan);
    NpScene& np = static_cast<NpScene&>(*world.scene);
    Sc::NPhaseCore& nphase = *np.getScene().getScScene().getNPhaseCore();

    const PxU32 topologyResult =
        physx333_offline::oc2_physx333_joined_topology_recreate_v1(
            &nphase, &plan);
    requireStage(topologyResult == physx333_offline::JoinedTopologySuccess,
                 "topology", std::to_string(topologyResult), verbose);
    verifyTopologyReadback(target, successor, captureTopology(world));
    const PxU32 reportResult =
        physx333_offline::oc2_physx333_joined_report_restore_v1(
            &nphase, &reportPlan);
    requireStage(reportResult == physx333_offline::JoinedReportSuccess,
                 "report/touch", std::to_string(reportResult), verbose);
    verifyReportReadback(targetContact, captureContact(world));

    requireStage(physx333_offline::InstallJoinedWorkUnitBindings(
        *world.scene, targetContact, error),
        "all contact bindings", error, verbose);
    requireStage(physx333_offline::RestoreMemBlockPoolForJoin(
        *world.scene, world.memBlockRegistry, targetBlocks, error),
        "memory blocks", error, verbose);
    requireStage(physx333_offline::RestoreJoinedWorkUnitPayload(
        *world.scene, targetContact, error),
        "contact/PCM payload", error, verbose);
    requireStage(oc2::offline::RestoreIslandForJoin(
        *world.scene, target.island, error), "island", error, verbose);
    requireStage(oc2::offline::RestoreSap(
        *world.scene, target.sap, error), "SAP", error, verbose);
    requireStage(oc2::offline::RestoreTransformCache(
        *world.scene, target.cache, error),
        "transform cache", error, verbose);
    requireStage(oc2::offline::RestoreShapeCacheBindings(
        *world.scene, target.shapeCache, error),
        "shape-cache IDs", error, verbose);
    requireStage(oc2::offline::RestoreBodies(
        *world.scene, target.bodies, error), "bodies", error, verbose);
    requireStage(oc2::offline::RestoreSceneClock(
        *world.scene, target.clock, error), "scene clock", error, verbose);
    requireStage(oc2::offline::RestoreContextImage(
        *world.scene, target.context, error), "context", error, verbose);
    requireStage(oc2::offline::RestoreQueryImage(
        *world.scene, target.query, error), "query", error, verbose);

    // The callback vector is fixture-owned output outside the PhysX scene.
    world.callback.rows = target.events;
    const Snapshot restored = world.capture();
    equalStopped(target, restored, "restored checkpoint", false);
    const physx333_offline::JoinedContactImage restoredContact =
        captureContact(world);
    std::string difference;
    if (!targetContact.exact(restoredContact, difference))
        fail("restored checkpoint exact joined contact image: " + difference);
    if (verbose) std::cout << "JOINED_FULL_A_READBACK passed\n";
}

void runScenario(const char* name, bool warm, bool shippedSapOrder)
{
    std::cout << "JOINED_FULL_SCENARIO " << name << '\n';
    Runtime runtime;
    World world(runtime, shippedSapOrder);
    world.step(0.0f);
    if (warm)
    {
        verify(world.step(0.0f), false);
        verify(world.step(-0.2f), true);
        const Snapshot returned = world.step(0.0f);
        if (returned.graph.scenePairs.size() != 18 ||
            std::set<PairKey>(returned.graph.scenePairs.begin(),
                              returned.graph.scenePairs.end()) !=
                expectedPairs(false))
            fail("joined full-replay warmup did not restore 12/4/2 graph");
    }
    const Snapshot a = world.step(0.0f);
    verify(a, false);
    const physx333_offline::JoinedContactImage contactA = captureContact(world);
    physx333_offline::MemBlockIdentityRegistry checkpointRegistry =
        world.memBlockRegistry;
    physx333_offline::MemBlockRestoreImage blockA;
    std::string error;
    requireStage(physx333_offline::CaptureMemBlockRestore(
        *world.scene, checkpointRegistry, blockA, error),
        "A memory-block capture", error);

    const Snapshot b = world.step(-0.2f);
    verify(b, true);
    verifyAuxTransition(a, b);
    const physx333_offline::JoinedContactImage contactB = captureContact(world);
    for (unsigned cycle = 0; cycle != 100; ++cycle)
    {
        restoreCheckpoint(world, a, b, contactA, contactB, blockA,
                          cycle == 0);
        // The transaction's full A readback is the only authorization to
        // resume simulation. Do not apply fixture inputs before that check.
        const Snapshot replayB = world.step(-0.2f);
        equalStopped(b, replayB, "replayed B", !warm);
        if (runtime.errors.count)
            fail("PhysX reported an A/B joined full-replay error");
    }
    std::cout << "JOINED_FULL_AB_100 passed " << name << '\n';

    // A later checkpoint is separated from its restore site by three steps.
    // The first replay step applies no fixture setters, so it cannot mask
    // an incorrect restored body or pending native state.
    world.step(0.0f); // Return; this frame can contain found-touch reports.
    const Snapshot c = world.step(0.0f); // Fully settled contact checkpoint.
    verify(c, false);
    const physx333_offline::JoinedContactImage contactC = captureContact(world);
    physx333_offline::MemBlockIdentityRegistry cRegistry =
        world.memBlockRegistry;
    physx333_offline::MemBlockRestoreImage blockC;
    requireStage(physx333_offline::CaptureMemBlockRestore(
        *world.scene, cRegistry, blockC, error),
        "C memory-block capture", error);
    const Snapshot natural = world.advanceWithoutInputs();
    const Snapshot d = world.step(-0.2f);
    const Snapshot e = world.step(-0.2f);
    const physx333_offline::JoinedContactImage contactE = captureContact(world);
    if (d.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != 8 ||
        d.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != 2 ||
        d.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2 ||
        e.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != 8 ||
        e.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != 2 ||
        e.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2)
        fail("nonadjacent deleted endpoint changed interaction topology");
    for (unsigned cycle = 0; cycle != 100; ++cycle)
    {
        restoreCheckpoint(world, c, e, contactC, contactE, blockC,
                          cycle == 0);
        const Snapshot replayNatural = world.advanceWithoutInputs();
        equalStopped(natural, replayNatural, "no-input successor", false);
        equalStopped(d, world.step(-0.2f), "nonadjacent deleted step", false);
        equalStopped(e, world.step(-0.2f), "nonadjacent final step", false);
        if (runtime.errors.count)
            fail("PhysX reported a nonadjacent joined full-replay error");
    }
    std::cout << "PASS joined full replay " << name
              << " A/B and nonadjacent no-input suffix x100\n";
}

} // namespace

int main(int argc, char** argv)
{
    if (argc == 1)
    {
        runScenario("cold", false, false);
        return 0;
    }
    if (argc == 2 && std::strcmp(argv[1], "--warm") == 0)
    {
        runScenario("warm", true, false);
        return 0;
    }
    if (argc == 2 && std::strcmp(argv[1], "--shipped-order") == 0)
    {
        runScenario("shipped-order cold", false, true);
        return 0;
    }
    if (argc == 2 && std::strcmp(argv[1], "--shipped-order-warm") == 0)
    {
        runScenario("shipped-order warm", true, true);
        return 0;
    }
    fail("unknown joined full-replay option");
    return 1;
}
