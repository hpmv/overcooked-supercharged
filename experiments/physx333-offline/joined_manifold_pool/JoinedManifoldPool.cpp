#define OC2_LEVEL_GRAPH_NO_MAIN
#include "../level_graph/LevelGraph.cpp"

#include "../joined_contact_image/JoinedContactImage.h"

namespace {

using physx333_offline::JoinedContactImage;
using physx333_offline::JoinedContactKey;

struct ManifoldRow {
    JoinedContactKey key;
    PxU32 managerSlot = 0, manifoldSlot = 0;
    std::uintptr_t manifoldAddress = 0;

    bool portable(const ManifoldRow& b) const {
        return key == b.key && managerSlot == b.managerSlot &&
            manifoldSlot == b.manifoldSlot;
    }
    bool exact(const ManifoldRow& b) const {
        return portable(b) && manifoldAddress == b.manifoldAddress;
    }
};

struct PoolImage {
    std::uintptr_t sceneAddress = 0, poolAddress = 0;
    std::vector<std::uintptr_t> slabs;
    PxU32 elementsPerSlab = 0, used = 0;
    PxI32 unReleasedFree = 0;
    std::vector<PxU32> usedSlots, freeOrder;
    std::vector<ManifoldRow> rows; // Contact scene order.

    bool portable(const PoolImage& b) const {
        if (slabs.size() != b.slabs.size() ||
            elementsPerSlab != b.elementsPerSlab || used != b.used ||
            unReleasedFree != b.unReleasedFree ||
            usedSlots != b.usedSlots || freeOrder != b.freeOrder ||
            rows.size() != b.rows.size())
            return false;
        for (size_t i = 0; i < rows.size(); ++i)
            if (!rows[i].portable(b.rows[i])) return false;
        return true;
    }
    bool exact(const PoolImage& b) const {
        if (!portable(b) || sceneAddress != b.sceneAddress ||
            poolAddress != b.poolAddress || slabs != b.slabs)
            return false;
        for (size_t i = 0; i < rows.size(); ++i)
            if (!rows[i].exact(b.rows[i])) return false;
        return true;
    }
};

typedef Ps::Pool<Gu::LargePersistentContactManifold> LargePool;

bool slotOf(const LargePool& pool, const void* object, PxU32& slot)
{
    const std::uintptr_t address =
        reinterpret_cast<std::uintptr_t>(object);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const std::uintptr_t begin =
            reinterpret_cast<std::uintptr_t>(pool.mSlabs[slab]);
        const std::uintptr_t bytes = pool.mElementsPerSlab *
            sizeof(Gu::LargePersistentContactManifold);
        if (address >= begin && address < begin + bytes &&
            (address - begin) % sizeof(Gu::LargePersistentContactManifold) == 0)
        {
            slot = slab * pool.mElementsPerSlab + static_cast<PxU32>(
                (address - begin) / sizeof(Gu::LargePersistentContactManifold));
            return true;
        }
    }
    return false;
}

struct Capture {
    JoinedContactImage contacts;
    PoolImage pool;
};

Capture captureOne(World& world)
{
    Capture result;
    std::string error;
    if (!physx333_offline::CaptureJoinedContactImage(
            *world.scene, result.contacts, error))
        fail("joined contact capture: " + error);
    NpScene& np = static_cast<NpScene&>(*world.scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
        fail("manifold pool capture requires completed fetchResults");
    PxsContext* context = np.getScene().getScScene()
        .getInteractionScene().getLowLevelContext();
    if (!context) fail("missing low-level context");
    const LargePool& pool = context->mManifoldPool;
    PoolImage& image = result.pool;
    image.sceneAddress = reinterpret_cast<std::uintptr_t>(world.scene);
    image.poolAddress = reinterpret_cast<std::uintptr_t>(&pool);
    image.elementsPerSlab = pool.mElementsPerSlab;
    image.used = pool.mUsed;
    image.unReleasedFree = pool.mUnReleasedFree;
    if (!image.elementsPerSlab || pool.mSlabs.empty() ||
        pool.mSlabs.size() > UINT32_MAX / image.elementsPerSlab)
        fail("large-manifold pool capacity is invalid");
    for (PxU32 i = 0; i < pool.mSlabs.size(); ++i)
        image.slabs.push_back(reinterpret_cast<std::uintptr_t>(
            pool.mSlabs[i]));
    const PxU32 capacity = static_cast<PxU32>(pool.mSlabs.size()) *
        image.elementsPerSlab;
    std::set<PxU32> freeSlots;
    auto* node = pool.mFreeElement;
    while (node)
    {
        PxU32 slot = 0;
        if (!slotOf(pool, node, slot) || !freeSlots.insert(slot).second ||
            image.freeOrder.size() >= capacity)
            fail("large-manifold free chain is invalid");
        image.freeOrder.push_back(slot);
        node = node->mNext;
    }
    for (PxU32 slot = 0; slot < capacity; ++slot)
        if (!freeSlots.count(slot)) image.usedSlots.push_back(slot);
    if (image.usedSlots.size() != image.used ||
        image.used != result.contacts.rows.size())
        fail("large-manifold used/free partition differs from contacts");

    std::set<PxU32> ownedSlots;
    for (const auto& contact : result.contacts.rows)
    {
        if (contact.workUnitBytes.size() != sizeof(PxcNpWorkUnit) ||
            contact.manifoldKind != 1)
            fail("contact WorkUnit or PCM kind is invalid");
        PxcNpWorkUnit work;
        std::memcpy(&work, contact.workUnitBytes.data(), sizeof(work));
        if (!work.pairCache.manifold ||
            (work.pairCache.manifold & 15u) != 0 ||
            work.index != contact.managerSlot)
            fail("contact WorkUnit manifold binding is invalid");
        PxU32 slot = 0;
        auto* manifold = reinterpret_cast<Gu::LargePersistentContactManifold*>(
            work.pairCache.manifold);
        if (!slotOf(pool, manifold, slot) || freeSlots.count(slot) ||
            !ownedSlots.insert(slot).second ||
            manifold->mContactPoints != manifold->mContactPointsBuff ||
            manifold->mNumContacts != contact.manifoldContactCount ||
            manifold->mNumWarmStartPoints != contact.manifoldWarmStartCount)
            fail("WorkUnit does not own its unique live large-manifold slot");
        ManifoldRow row;
        row.key = contact.key;
        row.managerSlot = contact.managerSlot;
        row.manifoldSlot = slot;
        row.manifoldAddress = work.pairCache.manifold;
        image.rows.push_back(row);
    }
    if (ownedSlots.size() != image.usedSlots.size())
        fail("some live large-manifold slots lack contact owners");
    return result;
}

Capture capture(World& world)
{
    Capture first = captureOne(world);
    Capture repeated = captureOne(world);
    std::string difference;
    if (!first.contacts.exact(repeated.contacts, difference) ||
        !first.pool.exact(repeated.pool))
        fail("same-scene repeated manifold image differs: " + difference);
    return first;
}

std::map<JoinedContactKey, ManifoldRow> keyed(const PoolImage& image)
{
    std::map<JoinedContactKey, ManifoldRow> rows;
    for (const auto& row : image.rows)
        if (!rows.insert(std::make_pair(row.key, row)).second)
            fail("duplicate native-oriented manifold key");
    return rows;
}

bool disappearingContact(const JoinedContactKey& key)
{
    const bool mover0 = key.shape0.actor == kMover && key.shape0.shape == 0;
    const bool mover1 = key.shape1.actor == kMover && key.shape1.shape == 0;
    if (mover0 == mover1) return false;
    const PxU32 fixed = mover0 ? key.shape1.actor : key.shape0.actor;
    const PxU32 fixedShape = mover0 ? key.shape1.shape : key.shape0.shape;
    return fixed >= kExtraFirst && fixed <= kExtraLast && fixedShape == 0;
}

void checkDeparture(const PoolImage& a, const PoolImage& b)
{
    if (a.rows.size() != 12 || b.rows.size() != 8 ||
        a.used != 12 || b.used != 8 || a.slabs != b.slabs ||
        a.poolAddress != b.poolAddress ||
        b.freeOrder.size() != a.freeOrder.size() + 4)
        fail("A/B manifold pool shape differs");
    const auto aRows = keyed(a), bRows = keyed(b);
    std::set<PxU32> released;
    for (const auto& entry : aRows)
    {
        const auto found = bRows.find(entry.first);
        if (found != bRows.end())
        {
            if (!entry.second.exact(found->second))
                fail("surviving contact changed manifold physical identity");
        }
        else
        {
            if (!disappearingContact(entry.first))
                fail("unexpected contact manifold disappeared");
            released.insert(entry.second.manifoldSlot);
        }
    }
    if (released.size() != 4)
        fail("expected four released capsule/box manifold slots");
    const std::set<PxU32> freePrefix(b.freeOrder.begin(),
                                     b.freeOrder.begin() + 4);
    if (freePrefix != released ||
        !std::equal(a.freeOrder.begin(), a.freeOrder.end(),
                    b.freeOrder.begin() + 4))
        fail("released manifolds are not exactly the free-chain head");
}

PxU32 recreatedTargetCount(const PoolImage& a, const PoolImage& returned)
{
    const auto aRows = keyed(a), returnedRows = keyed(returned);
    if (aRows.size() != 12 || returnedRows.size() != 12)
        fail("returned A has wrong manifold row count");
    PxU32 matched = 0;
    for (const auto& entry : aRows)
    {
        const auto found = returnedRows.find(entry.first);
        if (found == returnedRows.end())
            fail("returned A lost a manifold key");
        if (disappearingContact(entry.first))
            matched += entry.second.exact(found->second) ? 1u : 0u;
        else if (!entry.second.exact(found->second))
            fail("surviving manifold changed during return to A");
    }
    return matched;
}

void compareFresh(const Capture& a, const Capture& fresh,
                  const char* stage)
{
    std::string difference;
    if (!a.contacts.portable(fresh.contacts, difference) ||
        !a.pool.portable(fresh.pool))
        fail(std::string("fresh-scene manifold projection differs at ") +
             stage + ": " + difference);
}

} // namespace

int main()
{
    Runtime runtime;
    World first(runtime);
    first.step(0.0f);
    verify(first.step(0.0f), false);
    const Capture a = capture(first);
    verify(first.step(-0.2f), true);
    const Capture b = capture(first);
    checkDeparture(a.pool, b.pool);
    first.step(0.0f);
    verify(first.step(0.0f), false);
    const Capture returned = capture(first);
    const PxU32 recreated = recreatedTargetCount(a.pool, returned.pool);

    World fresh(runtime);
    fresh.step(0.0f);
    verify(fresh.step(0.0f), false);
    const Capture freshA = capture(fresh);
    verify(fresh.step(-0.2f), true);
    const Capture freshB = capture(fresh);
    checkDeparture(freshA.pool, freshB.pool);
    fresh.step(0.0f);
    verify(fresh.step(0.0f), false);
    const Capture freshReturned = capture(fresh);
    const PxU32 freshRecreated =
        recreatedTargetCount(freshA.pool, freshReturned.pool);
    compareFresh(a, freshA, "A");
    compareFresh(b, freshB, "B");
    compareFresh(returned, freshReturned, "returned A");
    if (recreated != 4 || freshRecreated != 4 ||
        !a.pool.exact(returned.pool) ||
        !freshA.pool.exact(freshReturned.pool) || runtime.errors.count)
        fail("public return did not recreate the exact A manifold pool");
    std::cout << "PCM_RECREATED_TARGET_IDENTITIES " << recreated
              << "/4 pool_exact=" << a.pool.exact(returned.pool) << '\n';
    std::cout << "PASS read-only joined manifold-pool A/B/returned-A "
                 "ownership and fresh-scene projection\n";
}
