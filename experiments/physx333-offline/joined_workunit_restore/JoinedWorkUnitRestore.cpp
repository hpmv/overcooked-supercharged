#include "JoinedWorkUnitRestore.h"

#include <algorithm>
#include <cstring>
#include <map>
#include <set>
#include <vector>

// These source-private headers are used only in the disposable Win32 offline
// fixture. They are not a declaration of Unity's compiled ABI.
#define private public
#define protected public
#include "NpScene.h"
#include "ScScene.h"
#include "ScNPhaseCore.h"
#include "ScInteractionScene.h"
#include "ScShapeInstancePairLL.h"
#include "PxsContext.h"
#include "PxsContactManager.h"
#include "PxcNpMemBlockPool.h"
#include "PxcNpWorkUnit.h"
#include "GuPersistentContactManifold.h"
#include "PxvGeometry.h"
#undef protected
#undef private

#include "../memblock/MemBlockImage.h"

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "Joined WorkUnits require Win32 PhysX");
static_assert(sizeof(Gu::LargePersistentContactManifold) == 240,
              "Pinned large PCM manifold layout changed");

struct InstallRow {
    PxsContactManager* manager = NULL;
    PxcNpWorkUnit saved;
    Gu::LargePersistentContactManifold* manifold = NULL;
    const JoinedContactRow* source = NULL;
};

bool samePrerequisites(const JoinedContactRow& a,
                       const JoinedContactRow& b)
{
    return a.key == b.key && a.actorPairKey == b.actorPairKey &&
        a.sceneIndex == b.sceneIndex && a.sipSlot == b.sipSlot &&
        a.actorPairSlot == b.actorPairSlot &&
        a.managerSlot == b.managerSlot && a.islandEdge == b.islandEdge &&
        a.sipFlags == b.sipFlags && a.reportStamp == b.reportStamp &&
        a.reportPairIndex == b.reportPairIndex &&
        a.reportStreamIndex == b.reportStreamIndex &&
        a.actorPairFlags == b.actorPairFlags &&
        a.actorPairTouchCount == b.actorPairTouchCount &&
        a.actorPairRefCount == b.actorPairRefCount &&
        a.reportPoolSlot == b.reportPoolSlot &&
        a.reportResetStamp == b.reportResetStamp &&
        a.reportActorA == b.reportActorA &&
        a.reportActorB == b.reportActorB &&
        a.reportStreamManager == b.reportStreamManager &&
        a.managerFlags == b.managerFlags &&
        a.managerStatusFlags == b.managerStatusFlags &&
        a.managerWords.size() == 14 && b.managerWords.size() == 14 &&
        std::equal(a.managerWords.begin(), a.managerWords.begin() + 3,
                   b.managerWords.begin());
}

bool sameReportStage(const JoinedContactImage& a,
                     const JoinedContactImage& b)
{
    return a.scene == b.scene && a.actorPairs == b.actorPairs &&
        a.persistentEventOrder == b.persistentEventOrder &&
        a.forceThresholdEventOrder == b.forceThresholdEventOrder &&
        a.nextPersistentPair == b.nextPersistentPair &&
        a.managerFreeOrder == b.managerFreeOrder &&
        a.managerUse.exact(b.managerUse) &&
        a.activeManagers.exact(b.activeManagers) &&
        a.modifiableManagers.exact(b.modifiableManagers) &&
        a.touchEventManagers.exact(b.touchEventManagers) &&
        a.reportBufferIndex == b.reportBufferIndex &&
        a.reportBufferSize == b.reportBufferSize &&
        a.reportBufferDefaultSize == b.reportBufferDefaultSize &&
        a.reportBufferLastIndex == b.reportBufferLastIndex &&
        a.reportBufferAllocationLocked == b.reportBufferAllocationLocked &&
        a.reportBufferBytes == b.reportBufferBytes;
}

bool supportedGeometry(const PxcNpWorkUnit& work)
{
    const PxU8 a = work.geomType0, b = work.geomType1;
    return ((a == PxGeometryType::eCAPSULE && b == PxGeometryType::eBOX) ||
            (a == PxGeometryType::eBOX && b == PxGeometryType::eCAPSULE)) &&
        work.shapeCore0 && work.shapeCore1 &&
        a == work.shapeCore0->geometry.getType() &&
        b == work.shapeCore1->geometry.getType();
}

bool blockSpan(const std::set<std::uintptr_t>& blocks,
               const void* pointer, std::size_t size)
{
    if (!pointer) return size == 0;
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(pointer);
    for (const std::uintptr_t block : blocks)
        if (address >= block &&
            address < block + PxcNpMemBlock::SIZE &&
            size <= block + PxcNpMemBlock::SIZE - address)
            return true;
    return false;
}

bool allocatedBlocks(PxScene& scene, std::set<std::uintptr_t>& blocks,
                     std::string& error)
{
    MemBlockIdentityRegistry registry;
    MemBlockImage image;
    if (!CaptureMemBlockPool(scene, registry, image, error)) return false;
    if (image.blocks.size() != image.allocatedBlocks ||
        !image.unsupported.empty())
    {
        error = "NpMemBlockPool allocation inventory is unsupported";
        return false;
    }
    for (const MemBlockImage::Block& block : image.blocks)
        if (!block.address || block.bytes.size() != PxcNpMemBlock::SIZE ||
            !blocks.insert(block.address).second)
        {
            error = "NpMemBlockPool allocated-block partition is invalid";
            return false;
        }
    return true;
}

bool manifoldSlot(const Ps::Pool<Gu::LargePersistentContactManifold>& pool,
                  const void* object, PxU32& slot)
{
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(object);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const std::uintptr_t begin = reinterpret_cast<std::uintptr_t>(
            pool.mSlabs[slab]);
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

bool manifoldPartition(const Ps::Pool<Gu::LargePersistentContactManifold>& pool,
                       std::set<PxU32>& freeSlots, std::string& error)
{
    if (!pool.mElementsPerSlab || pool.mSlabs.empty() ||
        pool.mSlabs.size() > UINT32_MAX / pool.mElementsPerSlab)
    {
        error = "PCM manifold pool capacity is invalid";
        return false;
    }
    const PxU32 capacity = static_cast<PxU32>(pool.mSlabs.size()) *
        pool.mElementsPerSlab;
    auto* node = pool.mFreeElement;
    while (node)
    {
        PxU32 slot = 0;
        if (!manifoldSlot(pool, node, slot) ||
            !freeSlots.insert(slot).second || freeSlots.size() > capacity)
        {
            error = "PCM manifold free chain is invalid";
            return false;
        }
        node = node->mNext;
    }
    if (capacity - freeSlots.size() != pool.mUsed)
    {
        error = "PCM manifold used/free partition differs";
        return false;
    }
    return true;
}

bool preflight(PxScene& scene, const JoinedContactImage& target,
               bool requireBacking, std::vector<InstallRow>& plan,
               std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    { error = "Joined WorkUnit restore requires completed fetchResults"; return false; }
    JoinedContactImage current;
    if (!CaptureJoinedContactImage(scene, current, error)) return false;
    if (target.rows.size() != 12 || current.rows.size() != 12 ||
        !sameReportStage(target, current))
    { error = "Joined contact topology/report stage is incomplete"; return false; }

    std::set<std::uintptr_t> blocks;
    if (!allocatedBlocks(scene, blocks, error)) return false;
    Sc::InteractionScene& interactions = np.getScene().getScScene()
        .getInteractionScene();
    PxsContext* context = interactions.getLowLevelContext();
    if (!context)
    { error = "Joined contact low-level context is missing"; return false; }
    const auto& manifoldPool = context->mManifoldPool;
    std::set<PxU32> freeManifolds, ownedManifolds;
    if (!manifoldPartition(manifoldPool, freeManifolds, error)) return false;

    std::map<JoinedContactKey, const JoinedContactRow*> byKey;
    for (const JoinedContactRow& row : current.rows)
        if (!byKey.insert(std::make_pair(row.key, &row)).second)
        { error = "Duplicate native-oriented contact key"; return false; }
    std::set<JoinedContactKey> targetKeys;
    for (const JoinedContactRow& source : target.rows)
    {
        if (!targetKeys.insert(source.key).second)
        { error = "Duplicate saved native-oriented contact key"; return false; }
        const auto found = byKey.find(source.key);
        if (found == byKey.end() ||
            !samePrerequisites(source, *found->second))
        { error = "Joined contact owner, slot, or report prerequisite differs"; return false; }
        const JoinedContactRow& liveRow = *found->second;
        if (source.workUnitBytes.size() != sizeof(PxcNpWorkUnit) ||
            liveRow.workUnitBytes.size() != sizeof(PxcNpWorkUnit) ||
            source.sceneIndex >= interactions.mInteractions[
                Sc::PX_INTERACTION_TYPE_OVERLAP].size())
        { error = "Joined contact WorkUnit image or scene index is invalid"; return false; }
        auto* sip = static_cast<Sc::ShapeInstancePairLL*>(
            interactions.mInteractions[Sc::PX_INTERACTION_TYPE_OVERLAP]
                [source.sceneIndex]);
        if (!sip || !sip->mManager ||
            sip->mManager->getIndex() != source.managerSlot)
        { error = "Joined contact manager owner changed"; return false; }
        PxsContactManager& manager = *sip->mManager;
        const PxcNpWorkUnit& live = manager.getWorkUnit();
        PxcNpWorkUnit saved;
        std::memcpy(&saved, source.workUnitBytes.data(), sizeof(saved));
        // Compare untrusted saved pointers with already-observed live owners
        // before supportedGeometry dereferences either ShapeCore.
        if (saved.index != source.managerSlot ||
            saved.geomType0 != live.geomType0 ||
            saved.geomType1 != live.geomType1 ||
            saved.rigidCore0 != live.rigidCore0 ||
            saved.rigidCore1 != live.rigidCore1 ||
            saved.shapeCore0 != live.shapeCore0 ||
            saved.shapeCore1 != live.shapeCore1 ||
            saved.materialManager != live.materialManager ||
            !supportedGeometry(live) || !supportedGeometry(saved) ||
            saved.statusFlags != source.managerStatusFlags ||
            saved.compressedContactSize !=
                source.compressedContactBytes.size() ||
            saved.pairCache.size != source.pairCacheBytes.size() ||
            saved.ccdContacts)
        { error = "Saved WorkUnit geometry, binding, or stream size is unsupported"; return false; }
        if (!blockSpan(blocks, saved.compressedContacts,
                       saved.compressedContactSize) ||
            !blockSpan(blocks, saved.pairCache.ptr, saved.pairCache.size) ||
            !blockSpan(blocks, saved.solverConstraintPointer,
                       saved.solverConstraintPointer ?
                           std::max<PxU32>(saved.solverConstraintSize, 1u) :
                           0u) ||
            !blockSpan(blocks, saved.frictionDataPtr,
                       saved.frictionDataPtr ? 1u : 0u))
        { error = "Saved WorkUnit stream pointer is outside allocated blocks"; return false; }
        if (requireBacking &&
            ((!source.compressedContactBytes.empty() &&
              std::memcmp(saved.compressedContacts,
                          source.compressedContactBytes.data(),
                          source.compressedContactBytes.size()) != 0) ||
             (!source.pairCacheBytes.empty() &&
              std::memcmp(saved.pairCache.ptr,
                          source.pairCacheBytes.data(),
                          source.pairCacheBytes.size()) != 0)))
        { error = "Saved contact/cache backing differs; restore memory blocks first"; return false; }
        if (source.manifoldKind != 1 ||
            !saved.pairCache.manifold ||
            saved.pairCache.manifold != live.pairCache.manifold ||
            (saved.pairCache.manifold & 15u) ||
            source.manifoldContactCount > GU_MANIFOLD_CACHE_SIZE ||
            source.manifoldWarmStartCount > 4 ||
            source.manifoldIndexBytes.size() != 8 ||
            source.manifoldContactBytes.size() !=
                source.manifoldContactCount * sizeof(Gu::PersistentContact))
        { error = "Saved PCM manifold identity or payload is unsupported"; return false; }
        auto* manifold = reinterpret_cast<Gu::LargePersistentContactManifold*>(
            saved.pairCache.manifold);
        PxU32 manifoldIndex = 0;
        if (!manifoldSlot(manifoldPool, manifold, manifoldIndex) ||
            freeManifolds.count(manifoldIndex) ||
            !ownedManifolds.insert(manifoldIndex).second ||
            manifold->mContactPoints != manifold->mContactPointsBuff ||
            source.manifoldTransformBytes.size() !=
                sizeof(manifold->mRelativeTransform))
        { error = "Saved PCM manifold does not own a unique live pool slot"; return false; }
        InstallRow item;
        item.manager = &manager;
        item.saved = saved;
        item.manifold = manifold;
        item.source = &source;
        plan.push_back(item);
    }
    if (plan.size() != 12 || ownedManifolds.size() != manifoldPool.mUsed)
    { error = "Joined contact or PCM pool owner count differs"; return false; }
    return true;
}

bool install(PxScene& scene, const JoinedContactImage& target,
             bool requireBacking, std::string& error)
{
    error.clear();
    std::vector<InstallRow> plan;
    if (!preflight(scene, target, requireBacking, plan, error)) return false;
    for (const InstallRow& item : plan)
    {
        const JoinedContactRow& source = *item.source;
        std::memcpy(&item.manager->getWorkUnit(), &item.saved,
                    sizeof(PxcNpWorkUnit));
        Gu::LargePersistentContactManifold& manifold = *item.manifold;
        std::memcpy(&manifold.mRelativeTransform,
                    source.manifoldTransformBytes.data(),
                    source.manifoldTransformBytes.size());
        manifold.mNumContacts = static_cast<PxU8>(
            source.manifoldContactCount);
        manifold.mNumWarmStartPoints = static_cast<PxU8>(
            source.manifoldWarmStartCount);
        std::memcpy(manifold.mAIndice,
                    source.manifoldIndexBytes.data(), 4);
        std::memcpy(manifold.mBIndice,
                    source.manifoldIndexBytes.data() + 4, 4);
        if (!source.manifoldContactBytes.empty())
            std::memcpy(manifold.mContactPoints,
                        source.manifoldContactBytes.data(),
                        source.manifoldContactBytes.size());
    }
    JoinedContactImage restored;
    if (!CaptureJoinedContactImage(scene, restored, error))
    { error = "Post-write joined contact capture failed; dispose scene: " + error; return false; }
    std::map<JoinedContactKey, const JoinedContactRow*> byKey;
    for (const JoinedContactRow& row : restored.rows)
        if (!byKey.insert(std::make_pair(row.key, &row)).second)
        { error = "Post-write duplicate contact key; dispose scene"; return false; }
    for (const JoinedContactRow& source : target.rows)
    {
        const auto found = byKey.find(source.key);
        if (found == byKey.end())
        { error = "Post-write contact key missing; dispose scene"; return false; }
        const JoinedContactRow& actual = *found->second;
        if (source.workUnitBytes != actual.workUnitBytes ||
            source.managerWords != actual.managerWords ||
            source.manifoldKind != actual.manifoldKind ||
            source.manifoldContactCount != actual.manifoldContactCount ||
            source.manifoldWarmStartCount != actual.manifoldWarmStartCount ||
            source.manifoldTransformBytes != actual.manifoldTransformBytes ||
            source.manifoldIndexBytes != actual.manifoldIndexBytes ||
            source.manifoldContactBytes != actual.manifoldContactBytes ||
            (requireBacking && !source.exact(actual)))
        { error = "Post-write contact payload differs; dispose scene"; return false; }
    }
    return true;
}

} // namespace

bool InstallJoinedWorkUnitBindings(PxScene& scene,
                                   const JoinedContactImage& checkpoint,
                                   std::string& error)
{
    return install(scene, checkpoint, false, error);
}

bool RestoreJoinedWorkUnitPayload(PxScene& scene,
                                  const JoinedContactImage& checkpoint,
                                  std::string& error)
{
    return install(scene, checkpoint, true, error);
}

} // namespace physx333_offline
