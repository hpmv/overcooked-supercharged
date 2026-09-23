#define OC2_LEVEL_GRAPH_NO_MAIN
#include "../level_graph/LevelGraph.cpp"

#include "JoinedContactImage.h"

namespace {

physx333_offline::JoinedContactImage capture(World& world)
{
    physx333_offline::JoinedContactImage image;
    std::string error;
    if (!physx333_offline::CaptureJoinedContactImage(
            *world.scene, image, error))
        fail("joined contact capture: " + error);
    physx333_offline::JoinedContactImage repeat;
    if (!physx333_offline::CaptureJoinedContactImage(
            *world.scene, repeat, error))
        fail("joined contact repeat capture: " + error);
    if (!image.exact(repeat, error))
        fail("joined contact repeat image differs: " + error);
    return image;
}

void verifyContactImage(const physx333_offline::JoinedContactImage& image,
                        bool successor)
{
    const PxU32 contacts = successor ? 8u : 12u;
    const PxU32 reports = successor ? 8u : 10u;
    if (image.rows.size() != contacts ||
        image.actorPairs.sips.size() != contacts ||
        image.actorPairs.actorPairs.size() != contacts ||
        image.actorPairs.reportDataPool.usedCount != reports ||
        image.persistentEventOrder.size() != reports ||
        image.nextPersistentPair != reports ||
        !image.forceThresholdEventOrder.empty())
        fail("joined contact/report/event inventory differs");
    std::set<physx333_offline::JoinedContactKey> keys;
    std::set<PxU32> sipSlots, actorPairSlots, managerSlots, reportSlots;
    for (PxU32 i = 0; i < contacts; ++i)
    {
        const auto& row = image.rows[i];
        if (row.sceneIndex != i || row.key.shape0.actor ==
                row.key.shape1.actor ||
            row.manifoldKind != 1 ||
            row.workUnitBytes.empty() || row.managerWords.size() != 14 ||
            !keys.insert(row.key).second ||
            !sipSlots.insert(row.sipSlot).second ||
            !actorPairSlots.insert(row.actorPairSlot).second ||
            !managerSlots.insert(row.managerSlot).second)
            fail("joined contact row identity, PCM, or pool ownership differs at " +
                 std::to_string(i) + " actors=" +
                 std::to_string(row.key.shape0.actor) + ":" +
                 std::to_string(row.key.shape0.shape) + "," +
                 std::to_string(row.key.shape1.actor) + ":" +
                 std::to_string(row.key.shape1.shape) + " manifold=" +
                 std::to_string(row.manifoldKind) + "/" +
                 std::to_string(row.manifoldContactCount));
        if (row.reportPoolSlot != 0xffffffffu &&
            !reportSlots.insert(row.reportPoolSlot).second)
            fail("joined report object is shared unexpectedly");
        const PxU32 fixed = row.key.shape0.actor < kMover ?
            row.key.shape0.actor : row.key.shape1.actor;
        const bool noTouch = !successor &&
            (fixed == 3 || fixed == 5);
        if ((row.actorPairTouchCount == 0) != noTouch ||
            (row.reportPoolSlot == 0xffffffffu) != noTouch ||
            (noTouch && row.manifoldContactCount != 0))
            fail("joined contact touch/report ownership differs at row " +
                 std::to_string(i));
    }
    if (reportSlots.size() != reports)
        fail("joined contact report-data count differs");
    if (!successor)
    {
        PxU32 missingNoReport = 0;
        for (const auto& row : image.rows)
            if (row.key.shape0.actor == kMover ||
                row.key.shape1.actor == kMover)
            {
                const PxU32 fixed = row.key.shape0.actor == kMover ?
                    row.key.shape1.actor : row.key.shape0.actor;
                if (fixed >= kExtraFirst && fixed <= kExtraLast &&
                    row.reportPoolSlot == 0xffffffffu)
                    ++missingNoReport;
            }
        if (missingNoReport != 2)
            fail("two disappearing no-touch contacts lost their null reports");
    }
}

} // namespace

int main()
{
    Runtime runtime;
    World first(runtime);
    first.step(0.0f);
    const Snapshot a = first.step(0.0f);
    verify(a, false);
    const auto contactA = capture(first);
    verifyContactImage(contactA, false);
    const auto& fourth = contactA.rows[3];
    std::cout << "JOINED_CM_ORACLE_WORD_48 pair="
              << fourth.key.shape0.actor << ':' << fourth.key.shape0.shape
              << ',' << fourth.key.shape1.actor << ':'
              << fourth.key.shape1.shape << " manager="
              << fourth.managerSlot << " friction_patches="
              << fourth.managerWords[6] << '\n';
    const Snapshot b = first.step(-0.2f);
    verify(b, true);
    const auto contactB = capture(first);
    verifyContactImage(contactB, true);
    for (const auto& row : contactB.rows)
        if (row.key == fourth.key)
            std::cout << "JOINED_CM_SURVIVOR_B manager=" << row.managerSlot
                      << " friction_patches=" << row.managerWords[6] << '\n';

    World fresh(runtime);
    fresh.step(0.0f);
    const Snapshot freshA = fresh.step(0.0f);
    verify(freshA, false);
    const auto freshContactA = capture(fresh);
    const Snapshot freshB = fresh.step(-0.2f);
    verify(freshB, true);
    const auto freshContactB = capture(fresh);
    std::string difference;
    if (!contactA.portable(freshContactA, difference) ||
        !contactB.portable(freshContactB, difference))
        fail("fresh-scene joined contact semantics differ: " + difference);
    if (runtime.errors.count) fail("PhysX reported an error");
    std::cout << "PASS joined read-only contact image: 12/4/2 -> 8/2/2, "
                 "ten/eight report owners, native-oriented keys, full "
                 "same-scene image, and fresh-scene portable semantics\n";
}
