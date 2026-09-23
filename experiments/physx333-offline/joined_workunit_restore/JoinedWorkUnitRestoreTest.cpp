// Reuse the exact joined-topology fixture construction and source bridge,
// without running that fixture's main. This translation unit adds only the
// next isolated WorkUnit/PCM and NpMemBlockPool stage.
#pragma warning(push)
#pragma warning(disable:4716)
#define main joined_topology_embedded_main
#include "../joined_topology/JoinedTopology.cpp"
#undef main
#pragma warning(pop)

#include "JoinedWorkUnitRestore.h"
#include "../memblock_restore/MemBlockRestore.h"

namespace {

void checkRejected(World& world,
                   const physx333_offline::JoinedContactImage& invalid,
                   const physx333_offline::JoinedContactImage& baseline,
                   const char* name)
{
    std::string error, difference;
    if (physx333_offline::InstallJoinedWorkUnitBindings(
            *world.scene, invalid, error) || error.empty())
        fail(std::string("joined WorkUnit preflight accepted ") + name);
    const auto current = captureContact(world);
    if (!baseline.exact(current, difference))
        fail(std::string("joined WorkUnit rejected ") + name +
             " after writing: " + difference);
    std::cout << "JOINED_WORKUNIT_REJECT " << name << " reason=" << error
              << '\n';
}

void runWorkUnitScenario(const char* name, bool warm,
                         bool shippedSapOrder)
{
    std::cout << "JOINED_WORKUNIT_SCENARIO " << name << '\n';
    Runtime runtime;
    World world(runtime, shippedSapOrder);
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
            fail("joined WorkUnit warmup did not restore 12/4/2 graph");
    }
    const Snapshot a = world.step(0.0f);
    verify(a, false);
    const auto contactA = captureContact(world);
    physx333_offline::MemBlockIdentityRegistry checkpointRegistry =
        world.memBlockRegistry;
    physx333_offline::MemBlockRestoreImage blockA;
    std::string error;
    if (!physx333_offline::CaptureMemBlockRestore(
            *world.scene, checkpointRegistry, blockA, error))
        fail("joined A memory-block checkpoint: " + error);
    const Snapshot b = world.step(-0.2f);
    verify(b, true);
    const auto contactB = captureContact(world);
    if (contactA.rows.size() != 12 || contactB.rows.size() != 8)
        fail("joined WorkUnit fixture contact count differs");
    const auto plan = makePlan(world, a, b, contactA);
    const auto reportPlan = makeReportPlan(a, contactA, contactB, plan);
    NpScene& np = static_cast<NpScene&>(*world.scene);
    Sc::NPhaseCore& nphase = *np.getScene().getScScene().getNPhaseCore();
    const PxU32 topologyResult =
        physx333_offline::oc2_physx333_joined_topology_recreate_v1(
            &nphase, &plan);
    if (topologyResult != physx333_offline::JoinedTopologySuccess)
        fail("joined WorkUnit topology stage rejected checkpoint: " +
             std::to_string(topologyResult));
    verifyTopologyReadback(a, b, captureTopology(world));
    const PxU32 reportResult =
        physx333_offline::oc2_physx333_joined_report_restore_v1(
            &nphase, &reportPlan);
    if (reportResult != physx333_offline::JoinedReportSuccess)
        fail("joined WorkUnit report stage rejected checkpoint: " +
             std::to_string(reportResult));
    const auto baseline = captureContact(world);
    verifyReportReadback(contactA, baseline);

    auto invalid = contactA;
    invalid.rows[0].workUnitBytes.clear();
    checkRejected(world, invalid, baseline, "missing WorkUnit bytes");
    invalid = contactA;
    ++invalid.rows[0].managerSlot;
    checkRejected(world, invalid, baseline, "wrong manager owner");
    invalid = contactA;
    ++invalid.rows[0].reportStamp;
    checkRejected(world, invalid, baseline, "wrong report prerequisite");
    invalid = contactA;
    PxcNpWorkUnit bad;
    std::memcpy(&bad, invalid.rows[0].workUnitBytes.data(), sizeof(bad));
    bad.pairCache.manifold = 0;
    std::memcpy(invalid.rows[0].workUnitBytes.data(), &bad, sizeof(bad));
    checkRejected(world, invalid, baseline, "wrong PCM manifold binding");
    invalid = contactA;
    std::memcpy(&bad, invalid.rows[0].workUnitBytes.data(), sizeof(bad));
    bad.shapeCore0 = reinterpret_cast<decltype(bad.shapeCore0)>(
        static_cast<std::uintptr_t>(1));
    std::memcpy(invalid.rows[0].workUnitBytes.data(), &bad, sizeof(bad));
    checkRejected(world, invalid, baseline, "wrong shape-core pointer");
    invalid = contactA;
    std::memcpy(&bad, invalid.rows[0].workUnitBytes.data(), sizeof(bad));
    bad.compressedContacts = reinterpret_cast<decltype(bad.compressedContacts)>(
        static_cast<std::uintptr_t>(1));
    std::memcpy(invalid.rows[0].workUnitBytes.data(), &bad, sizeof(bad));
    checkRejected(world, invalid, baseline, "wrong stream pointer");

    if (!physx333_offline::InstallJoinedWorkUnitBindings(
            *world.scene, contactA, error))
        fail("joined WorkUnit binding stage rejected checkpoint: " + error);
    if (!physx333_offline::RestoreMemBlockPoolForJoin(
            *world.scene, world.memBlockRegistry, blockA, error))
        fail("joined WorkUnit memory-block stage rejected checkpoint; "
             "dispose scene: " + error);
    if (!physx333_offline::RestoreJoinedWorkUnitPayload(
            *world.scene, contactA, error))
        fail("joined WorkUnit backed payload stage rejected checkpoint; "
             "dispose scene: " + error);
    const auto restored = captureContact(world);
    if (restored.rows.size() != contactA.rows.size())
        fail("joined WorkUnit restored contact count differs");
    for (size_t i = 0; i < contactA.rows.size(); ++i)
        if (!contactA.rows[i].exact(restored.rows[i]))
            fail("joined WorkUnit exact row differs at " +
                 std::to_string(i));
    std::string oracleDifference;
    if (!contactA.oracle.equals(restored.oracle, oracleDifference))
        std::cout << "JOINED_WORKUNIT_ORACLE_FIRST_REMAINING "
                  << oracleDifference << '\n';
    if (!physx333_offline::RestoreJoinedWorkUnitPayload(
            *world.scene, contactA, error))
        fail("joined WorkUnit repeat payload rejected checkpoint; "
             "dispose scene: " + error);
    std::string repeatDifference;
    if (!restored.exact(captureContact(world), repeatDifference))
        fail("joined WorkUnit repeat changed stopped image: " +
             repeatDifference);
    if (runtime.errors.count) fail("PhysX reported a joined WorkUnit error");
    std::cout << "PASS joined " << name
              << " all 12 exact contact WorkUnits, PCM used payload, and "
                 "backing memory blocks; scene/island/SAP parity not claimed\n";
}

} // namespace

int main()
{
    runWorkUnitScenario("cold", false, false);
    runWorkUnitScenario("warm", true, false);
    runWorkUnitScenario("shipped-order cold", false, true);
    runWorkUnitScenario("shipped-order warm", true, true);
}
