// Diagnostic differential only: run the joined component transaction with
// the source-built fixed-address allocator, then compare its full ledger and
// bytes at restored A and next B. No raw arena write is used for the restore.
#pragma warning(push)
#pragma warning(disable:4716)
#define main joined_topology_embedded_main
#include "../joined_topology/JoinedTopology.cpp"
#undef main
#pragma warning(pop)

#include "../joined_workunit_restore/JoinedWorkUnitRestore.h"
#include "../memblock_restore/MemBlockRestore.h"
#include "../arena_snapshot/ArenaSnapshot.h"
#include "../arena_snapshot/ArenaAabbPadding.h"

#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <Windows.h>
#define private public
#include "ScSimStats.h"
#undef private

#include <algorithm>
#include <map>

namespace {

using ArenaImage = oc2::offline::ArenaSnapshotAllocator::Image;
using ArenaBlock = oc2::offline::ArenaSnapshotAllocator::Block;

std::string basename(const std::string& path)
{
    const std::size_t slash = path.find_last_of("/\\");
    return slash == std::string::npos ? path : path.substr(slash + 1);
}

std::size_t blockAt(const ArenaImage& image, std::size_t offset)
{
    auto found = std::upper_bound(image.blocks.begin(), image.blocks.end(),
        offset, [](std::size_t value, const ArenaBlock& block) {
            return value < block.offset;
        });
    if (found == image.blocks.begin()) return image.blocks.size();
    --found;
    if (offset >= found->offset && offset - found->offset < found->size)
        return static_cast<std::size_t>(found - image.blocks.begin());
    return image.blocks.size();
}

void traceNativeOwner(const ArenaImage& image,
                      std::uintptr_t arenaBase,
                      const char* name, const void* pointer)
{
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(pointer);
    if (address < arenaBase) return;
    const std::size_t offset = address - arenaBase;
    const std::size_t block = blockAt(image, offset);
    if (block == image.blocks.size()) return;
    std::cout << "JOINED_ARENA_NATIVE_OWNER " << name
              << " block=" << block
              << " in_block=" << offset - image.blocks[block].offset
              << " size=" << image.blocks[block].size << '\n';
}

std::uintptr_t traceNativeOwners(World& world, const ArenaImage& image)
{
    MEMORY_BASIC_INFORMATION memory = {};
    if (!VirtualQuery(world.scene, &memory, sizeof(memory)) ||
        !memory.AllocationBase)
        fail("joined arena owner VirtualQuery failed");
    const std::uintptr_t base = reinterpret_cast<std::uintptr_t>(
        memory.AllocationBase);
    NpScene& np = static_cast<NpScene&>(*world.scene);
    Sc::Scene& sc = np.getScene().getScScene();
    traceNativeOwner(image, base, "ScScene", &sc);
    traceNativeOwner(image, base, "InteractionScene",
                     &sc.getInteractionScene());
    traceNativeOwner(image, base, "NPhaseCore", sc.getNPhaseCore());
    traceNativeOwner(image, base, "SimStats", &sc.getStatsInternal());
    traceNativeOwner(image, base, "ShapeIDTracker",
                     &sc.getShapeIDTracker());
    traceNativeOwner(image, base, "RigidIDTracker",
                     &sc.getRigidIDTracker());
    traceNativeOwner(image, base, "TriggerBufferExtraData",
                     &sc.getTriggerBufferExtraData());
    traceNativeOwner(image, base, "PxsContext",
                     sc.getInteractionScene().getLowLevelContext());
    PxsContext* const context =
        sc.getInteractionScene().getLowLevelContext();
    traceNativeOwner(image, base, "PxsContext.scratch_allocator",
                     &context->getScratchAllocator());
    traceNativeOwner(image, base, "PxsContext.mem_block_pool",
                     &context->getNpMemBlockPool());
    std::cout << "JOINED_ARENA_TRIGGER_SIZES api="
              << sc.getTriggerBufferAPI().size()
              << " extra=" << sc.getTriggerBufferExtraData().size()
              << '\n';
    std::cout << "JOINED_ARENA_SIM_STATS_LAYOUT size="
              << sizeof(Sc::SimStats)
              << " trigger_pairs=" <<
                 offsetof(Sc::SimStats, numTriggerPairs)
              << " broadphase_adds_pending=" <<
                 offsetof(Sc::SimStats, numBroadPhaseAddsPending)
              << '\n';
    std::cout << "JOINED_ARENA_SOURCE_LAYOUT interaction_active="
              << offsetof(Sc::InteractionScene, mActiveBodies)
              << " interaction_arrays=" <<
                 offsetof(Sc::InteractionScene, mInteractions)
              << " nphase_report_set=" <<
                 offsetof(Sc::NPhaseCore, mContactReportActorPairSet)
              << " nphase_report_buffer=" <<
                 offsetof(Sc::NPhaseCore, mContactReportBuffer)
              << " scene_trigger_api=" <<
                 offsetof(Sc::Scene, mTriggerBufferAPI)
              << " scene_trigger_extra=" <<
                 offsetof(Sc::Scene, mTriggerBufferExtraData)
              << " scene_sleep=" << offsetof(Sc::Scene, mSleepBodies)
              << " scene_woke=" << offsetof(Sc::Scene, mWokeBodies)
              << '\n';
    return base;
}

void traceObserverBindings(const Snapshot& snapshot,
                           const ArenaImage& arena,
                           std::uintptr_t base)
{
    const std::size_t indices[] = {69, 72, 164, 165, 171, 281, 288, 289};
    for (std::size_t index : indices)
    {
        if (index >= arena.blocks.size()) continue;
        const std::uintptr_t begin = base + arena.blocks[index].offset;
        const std::uintptr_t end = begin + arena.blocks[index].size;
        std::size_t matches = 0;
        const auto emit = [&](const std::string& name,
                              std::uintptr_t pointer) {
            if (pointer >= begin && pointer < end && matches++ < 12)
                std::cout << "JOINED_ARENA_OBSERVER block=" << index
                          << " name=" << name
                          << " in_block=" << pointer - begin << '\n';
        };
        for (const auto& field : snapshot.query.fields)
            emit("query." + field.name, field.address);
        for (const auto& field : snapshot.context.fields)
            emit("context." + field.name, field.address);
        for (const auto& row : snapshot.context.arrays)
            emit("context." + row.name + ".data", row.data);
        for (const auto& row : snapshot.context.bitmaps)
            emit("context." + row.name + ".data", row.data);
        for (const auto& field : snapshot.bodies.fields)
            emit("body." + field.name, field.address);
        for (const auto& field : snapshot.clock.fields)
            emit("clock." + field.name, field.address);
        for (const auto& row : snapshot.clock.arrays)
            emit("clock." + row.name + ".data", row.data);
        for (const auto& field : snapshot.sap.scalars)
            emit("sap." + field.name, field.address);
        for (const auto& row : snapshot.sap.buffers)
            emit("sap." + row.name, row.address);
        for (const auto& field : snapshot.island.scalars)
            emit("island." + field.name, field.address);
        for (const auto& row : snapshot.island.buffers)
            emit("island." + row.name, row.address);
        emit("cache.transforms", snapshot.cache.transforms.address);
        emit("cache.references", snapshot.cache.referenceCounts.address);
        emit("cache.free_ids", snapshot.cache.freeIds.address);
        if (!matches)
            std::cout << "JOINED_ARENA_OBSERVER block=" << index
                      << " name=<no matched component field>\n";
    }
}

void tracePointerOwners(const ArenaImage& arena, std::uintptr_t base)
{
    const std::size_t targets[] = {69, 164, 165, 171, 281, 288, 289};
    for (std::size_t target : targets)
    {
        if (target >= arena.blocks.size()) continue;
        const std::uint32_t address = static_cast<std::uint32_t>(
            base + arena.blocks[target].offset);
        std::size_t matches = 0;
        for (std::size_t parent = 0; parent < arena.blocks.size(); ++parent)
        {
            const ArenaBlock& block = arena.blocks[parent];
            if (!block.live || block.offset + block.size > arena.bytes.size())
                continue;
            for (std::size_t byte = 0; byte + sizeof(address) <= block.size;
                 byte += sizeof(address))
            {
                std::uint32_t pointer = 0;
                std::memcpy(&pointer,
                    arena.bytes.data() + block.offset + byte,
                    sizeof(pointer));
                if (pointer != address || matches++ >= 8) continue;
                std::cout << "JOINED_ARENA_POINTER_OWNER target_block="
                          << target << " parent_block=" << parent
                          << " parent_byte=" << byte
                          << " parent_size=" << block.size
                          << " parent_source=" << basename(block.file)
                          << ':' << block.line << '\n';
            }
        }
    }
}

void traceMemBlockArrayOwners(const ArenaImage& arena, std::uintptr_t base,
                              const physx333_offline::MemBlockImage& image)
{
    for (const auto& row : image.arrays)
    {
        const std::size_t block = row.address >= base ?
            blockAt(arena, row.address - base) : arena.blocks.size();
        std::cout << "JOINED_ARENA_MEMBLOCK_ARRAY " << row.name
                  << " block=" << block
                  << " size=" << row.size
                  << " capacity=" << row.capacity << '\n';
    }
}

bool reportArenaDifference(const char* stage,
                           const ArenaImage& expected,
                           const ArenaImage& actual)
{
    ArenaImage normalizedExpected = expected;
    ArenaImage normalizedActual = actual;
    std::string error;
    if (!oc2::offline::NormalizeAabbTaskPadding(normalizedExpected, error) ||
        !oc2::offline::NormalizeAabbTaskPadding(normalizedActual, error))
        fail(std::string(stage) + " AABB padding inventory: " + error);

    const std::size_t commonBytes = std::min(expected.bytes.size(),
                                             actual.bytes.size());
    std::size_t rawChanged = 0, maskedChanged = 0, paddingOnly = 0;
    std::size_t liveChanged = 0, deadChanged = 0, gapChanged = 0;
    std::map<std::size_t, std::size_t> blockChanges;
    std::map<std::size_t, std::size_t> firstByte;
    for (std::size_t offset = 0; offset < commonBytes; ++offset)
    {
        const bool raw = expected.bytes[offset] != actual.bytes[offset];
        const bool masked = normalizedExpected.bytes[offset] !=
                            normalizedActual.bytes[offset];
        rawChanged += raw ? 1 : 0;
        paddingOnly += raw && !masked ? 1 : 0;
        if (!masked) continue;
        ++maskedChanged;
        const std::size_t block = blockAt(expected, offset);
        if (block == expected.blocks.size()) ++gapChanged;
        else
        {
            ++blockChanges[block];
            if (!firstByte.count(block)) firstByte[block] = offset;
            if (expected.blocks[block].live) ++liveChanged;
            else ++deadChanged;
        }
    }
    const std::size_t extraBytes = expected.bytes.size() > commonBytes ?
        expected.bytes.size() - commonBytes :
        actual.bytes.size() - commonBytes;
    std::size_t firstLedger = std::min(expected.blocks.size(),
                                       actual.blocks.size());
    for (std::size_t i = 0; i < firstLedger; ++i)
        if (!(expected.blocks[i] == actual.blocks[i]))
        { firstLedger = i; break; }
    const bool sameLedger = expected.owner == actual.owner &&
        expected.cursor == actual.cursor && expected.peak == actual.peak &&
        expected.blocks == actual.blocks;
    std::cout << "JOINED_ARENA_DIFF " << stage
              << " checkpoint_cursor=" << expected.cursor
              << " restored_cursor=" << actual.cursor
              << " checkpoint_peak=" << expected.peak
              << " restored_peak=" << actual.peak
              << " checkpoint_blocks=" << expected.blocks.size()
              << " restored_blocks=" << actual.blocks.size()
              << " ledger_equal=" << sameLedger
              << " first_ledger_difference=" << firstLedger
              << " raw_changed=" << rawChanged
              << " known_padding_only=" << paddingOnly
              << " unclassified_changed=" << maskedChanged
              << " live=" << liveChanged
              << " freed=" << deadChanged
              << " alignment_gap=" << gapChanged
              << " unequal_extent_bytes=" << extraBytes << '\n';
    std::vector<std::pair<std::size_t, std::size_t> > sorted(
        blockChanges.begin(), blockChanges.end());
    std::sort(sorted.begin(), sorted.end(),
        [](const std::pair<std::size_t, std::size_t>& a,
           const std::pair<std::size_t, std::size_t>& b) {
            return a.second > b.second;
        });
    const std::size_t detailCount = std::min<std::size_t>(sorted.size(), 12);
    for (std::size_t i = 0; i < detailCount; ++i)
    {
        const std::size_t index = sorted[i].first;
        const ArenaBlock& block = expected.blocks[index];
        std::cout << "JOINED_ARENA_OWNER " << stage
                  << " block=" << index
                  << " offset=" << block.offset
                  << " size=" << block.size
                  << " bytes_changed=" << sorted[i].second
                  << " first_block_byte=" <<
                     firstByte[index] - block.offset
                  << " first_expected=" <<
                     static_cast<unsigned>(expected.bytes[firstByte[index]])
                  << " first_replayed=" <<
                     static_cast<unsigned>(actual.bytes[firstByte[index]])
                  << " checkpoint_live=" << block.live
                  << " type=" << block.type
                  << " source=" << basename(block.file)
                  << ':' << block.line << '\n';
    }
    if (expected.blocks.size() != actual.blocks.size())
    {
        const std::size_t first = std::min(expected.blocks.size(),
                                           actual.blocks.size());
        const bool grew = actual.blocks.size() > expected.blocks.size();
        const ArenaImage& longer = grew ? actual : expected;
        for (std::size_t i = first;
             i < std::min(first + 12, longer.blocks.size()); ++i)
        {
            const ArenaBlock& block = longer.blocks[i];
            std::cout << "JOINED_ARENA_EXTRA_ALLOC " << stage
                      << " side=" << (grew ? "replay" : "checkpoint")
                      << " block=" << i
                      << " offset=" << block.offset
                      << " size=" << block.size
                      << " live=" << block.live
                      << " type=" << block.type
                      << " source=" << basename(block.file)
                      << ':' << block.line << '\n';
        }
    }
    return sameLedger && !maskedChanged && !extraBytes;
}

void traceAllocatorStage(const char* stage,
                         oc2::offline::ArenaSnapshotAllocator& arena,
                         ArenaImage& previous)
{
    ArenaImage current = arena.capture();
    if (current.blocks.size() != previous.blocks.size() ||
        current.cursor != previous.cursor)
    {
        std::cout << "JOINED_ARENA_STAGE " << stage
                  << " blocks=" << previous.blocks.size() << "->"
                  << current.blocks.size()
                  << " cursor=" << previous.cursor << "->"
                  << current.cursor << '\n';
        for (std::size_t i = previous.blocks.size();
             i < current.blocks.size(); ++i)
        {
            const ArenaBlock& block = current.blocks[i];
            std::cout << "JOINED_ARENA_STAGE_ALLOC " << stage
                      << " block=" << i
                      << " offset=" << block.offset
                      << " size=" << block.size
                      << " live=" << block.live
                      << " source=" << basename(block.file)
                      << ':' << block.line << '\n';
        }
    }
    previous = std::move(current);
}

void requireStage(bool ok, const char* stage, const std::string& error)
{
    if (!ok) fail(std::string("joined arena component stage ") + stage +
                  " failed; discard scene: " + error);
}

void equalComponents(const Snapshot& expected, const Snapshot& actual,
                     const char* stage, bool coldQueryTree)
{
    std::string difference;
    if (!expected.oracle.equals(actual.oracle, difference) ||
        !expected.aux.equals(actual.aux, difference) ||
        !(expected.actorPair == actual.actorPair) ||
        !(expected.graph == actual.graph) ||
        !expected.sap.equals(actual.sap, difference) ||
        !expected.cache.equalsWithRebasedFreeIds(actual.cache, difference) ||
        !expected.island.equals(actual.island, difference) ||
        !expected.memBlocks.equals(actual.memBlocks, difference) ||
        !expected.shapeCache.equals(actual.shapeCache, difference) ||
        !expected.bodies.equals(actual.bodies, difference) ||
        !expected.clock.equals(actual.clock, difference) ||
        !expected.context.equals(actual.context, difference))
        fail(std::string(stage) + " component mismatch: " + difference);
    const bool queryEqual = coldQueryTree ?
        expected.query.equalsWithRebuiltColdTree(actual.query, difference) :
        expected.query.equalsWithRebasedStack(actual.query, difference);
    if (!queryEqual || !(expected.facts == actual.facts) ||
        expected.deletedOverlaps != actual.deletedOverlaps ||
        expected.events != actual.events)
        fail(std::string(stage) + " query/facts/callback mismatch: " +
             difference);
}

void runArenaScenario(const char* name, bool warm, bool shippedSapOrder)
{
    std::cout << "JOINED_ARENA_SCENARIO " << name << '\n';
    oc2::offline::ArenaSnapshotAllocator arena(256u * 1024u * 1024u);
    if (!arena.valid()) fail("reserve joined source arena");
    Runtime runtime(&arena);
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
            fail("joined arena warmup did not restore 12/4/2 graph");
    }
    const Snapshot a = world.step(0.0f);
    verify(a, false);
    const auto contactA = captureContact(world);
    physx333_offline::MemBlockIdentityRegistry checkpointRegistry =
        world.memBlockRegistry;
    physx333_offline::MemBlockRestoreImage blockA;
    std::string error;
    requireStage(physx333_offline::CaptureMemBlockRestore(
        *world.scene, checkpointRegistry, blockA, error),
        "checkpoint blocks", error);
    const ArenaImage arenaA = arena.capture();
    const std::uintptr_t arenaBase = traceNativeOwners(world, arenaA);
    traceMemBlockArrayOwners(arenaA, arenaBase, blockA.pool);
    std::cout << "JOINED_ARENA_REPORT_USED_BYTES "
              << contactA.reportBufferIndex
              << " actor_pairs=" << static_cast<NpScene&>(*world.scene)
                 .getScene().getScScene().getNPhaseCore()
                 ->getNbContactReportActorPairs() << '\n';
    traceObserverBindings(a, arenaA, arenaBase);
    tracePointerOwners(arenaA, arenaBase);
    const physx333_offline::MemBlockIdentityRegistry registryA =
        world.memBlockRegistry;
    const Snapshot naturalB = world.advanceWithoutInputs();
    const ArenaImage arenaNaturalB = arena.capture();
    if (!arena.restore(arenaA, error))
        fail("raw-arena reference return to A: " + error);
    world.callback.rows = a.events;
    world.memBlockRegistry = registryA;
    std::string rawReturnDifference;
    if (!arenaA.equals(arena.capture(), rawReturnDifference))
        fail("raw-arena reference return changed checkpoint: " +
             rawReturnDifference);
    equalComponents(a, world.capture(), "raw-arena reference A", false);

    const Snapshot b = world.step(-0.2f);
    verify(b, true);
    verifyAuxTransition(a, b);
    const auto contactB = captureContact(world);
    const ArenaImage arenaB = arena.capture();
    ArenaImage sourceStage = arenaA;
    traceAllocatorStage("source A to B", arena, sourceStage);
    const auto plan = makePlan(world, a, b, contactA);
    const auto reportPlan = makeReportPlan(a, contactA, contactB, plan);
    NpScene& np = static_cast<NpScene&>(*world.scene);
    Sc::NPhaseCore& nphase = *np.getScene().getScScene().getNPhaseCore();
    const PxU32 topology =
        physx333_offline::oc2_physx333_joined_topology_recreate_v1(
            &nphase, &plan);
    requireStage(topology == physx333_offline::JoinedTopologySuccess,
                 "topology", std::to_string(topology));
    verifyTopologyReadback(a, b, captureTopology(world));
    const PxU32 report =
        physx333_offline::oc2_physx333_joined_report_restore_v1(
            &nphase, &reportPlan);
    requireStage(report == physx333_offline::JoinedReportSuccess,
                 "report", std::to_string(report));
    verifyReportReadback(contactA, captureContact(world));
    requireStage(physx333_offline::InstallJoinedWorkUnitBindings(
        *world.scene, contactA, error), "WorkUnit bindings", error);
    requireStage(physx333_offline::RestoreMemBlockPoolForJoin(
        *world.scene, world.memBlockRegistry, blockA, error),
        "memory blocks", error);
    requireStage(physx333_offline::RestoreJoinedWorkUnitPayload(
        *world.scene, contactA, error), "contact/PCM payload", error);
    requireStage(oc2::offline::RestoreIslandForJoin(
        *world.scene, a.island, error), "island", error);
    requireStage(oc2::offline::RestoreSap(
        *world.scene, a.sap, error), "SAP", error);
    requireStage(oc2::offline::RestoreTransformCache(
        *world.scene, a.cache, error), "transform cache", error);
    requireStage(oc2::offline::RestoreShapeCacheBindings(
        *world.scene, a.shapeCache, error), "shape-cache IDs", error);
    requireStage(oc2::offline::RestoreBodies(
        *world.scene, a.bodies, error), "bodies", error);
    requireStage(oc2::offline::RestoreSceneClock(
        *world.scene, a.clock, error), "scene clock", error);
    requireStage(oc2::offline::RestoreContextImage(
        *world.scene, a.context, error), "context", error);
    requireStage(oc2::offline::RestoreQueryImage(
        *world.scene, a.query, error), "query", error);

    world.callback.rows = a.events;
    const Snapshot restoredA = world.capture();
    const ArenaImage arenaRestoredA = arena.capture();
    const physx333_offline::MemBlockIdentityRegistry restoredRegistry =
        world.memBlockRegistry;
    const bool rawA = reportArenaDifference("restored_A", arenaA,
                                             arenaRestoredA);
    equalComponents(a, restoredA, "restored A", false);
    std::string difference;
    if (!contactA.exact(captureContact(world), difference))
        fail("joined arena restored A contact image: " + difference);

    const Snapshot replayB = world.step(-0.2f);
    const ArenaImage arenaReplayB = arena.capture();
    const bool rawB = reportArenaDifference("replayed_B", arenaB,
                                             arenaReplayB);
    equalComponents(b, replayB, "replayed B", !warm);

    if (!arena.restore(arenaRestoredA, error))
        fail("raw-arena return to component-restored A: " + error);
    world.callback.rows = a.events;
    world.memBlockRegistry = restoredRegistry;
    equalComponents(a, world.capture(), "component-restored A branch", false);
    const Snapshot naturalReplayB = world.advanceWithoutInputs();
    const ArenaImage arenaNaturalReplayB = arena.capture();
    const bool rawNaturalB = reportArenaDifference("no_input_B",
        arenaNaturalB, arenaNaturalReplayB);
    equalComponents(naturalB, naturalReplayB, "no-input B", !warm);
    if (warm && (!rawB || !rawNaturalB))
        fail("warm joined replay did not converge to the checkpoint allocator image");
    if (runtime.errors.count) fail("PhysX reported a joined arena error");
    std::cout << "PASS joined arena differential " << name
              << " complete component A/B parity; raw_A=" << rawA
              << " raw_B=" << rawB
              << " raw_no_input_B=" << rawNaturalB << '\n';
}

} // namespace

int main()
{
    runArenaScenario("cold", false, false);
    runArenaScenario("warm", true, false);
    runArenaScenario("shipped-order cold", false, true);
    runArenaScenario("shipped-order warm", true, true);
}
