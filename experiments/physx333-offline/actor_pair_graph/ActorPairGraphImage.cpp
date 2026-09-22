#include "ActorPairGraphImage.h"

#include <algorithm>
#include <cstdint>
#include <limits>
#include <map>
#include <set>
#include <utility>
#include <vector>

#include "PxPhysicsAPI.h"

// Restricted to this source-built diagnostic; the observer never writes the
// fields exposed by this access-label shim.
#define private public
#define protected public
#include "NpScene.h"
#include "NpShape.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScActorPair.h"
#include "ScActorCore.h"
#include "ScActorSim.h"
#include "ScInteractionScene.h"
#include "ScNPhaseCore.h"
#include "ScScene.h"
#include "ScShapeSim.h"
#include "ScShapeInstancePairLL.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "ActorPair graph observer requires Win32");

typedef std::map<const Sc::RigidSim*, std::uint32_t> ActorIds;
typedef std::map<const Sc::ShapeCore*, ActorGraphShapeKey> ShapeKeys;

ActorGraphKey key(std::uint32_t a, std::uint32_t b)
{
    ActorGraphKey result;
    result.first = std::min(a, b);
    result.second = std::max(a, b);
    return result;
}

template <class T, class Alloc>
bool poolSlot(const Ps::Pool<T, Alloc>& pool, const void* pointer,
              std::uint32_t& slot)
{
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(pointer);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const std::uintptr_t start =
            reinterpret_cast<std::uintptr_t>(pool.mSlabs[slab]);
        const std::uintptr_t end = start + pool.mElementsPerSlab * sizeof(T);
        if (address >= start && address < end &&
            (address - start) % sizeof(T) == 0)
        {
            slot = slab * pool.mElementsPerSlab +
                static_cast<PxU32>((address - start) / sizeof(T));
            return true;
        }
    }
    return false;
}

template <class T, class Alloc>
bool capturePool(const Ps::Pool<T, Alloc>& pool, ActorGraphPoolImage& image,
                 const char* name, std::string& error)
{
    image.slabCount = pool.mSlabs.size();
    image.elementsPerSlab = pool.mElementsPerSlab;
    image.usedCount = pool.mUsed;
    image.unreleasedFree = pool.mUnReleasedFree;
    image.slabSize = pool.mSlabSize;
    if (!image.elementsPerSlab || image.slabCount > 1024 ||
        image.slabCount > 65536 / image.elementsPerSlab ||
        pool.mUnReleasedFree < 0)
    {
        error = std::string(name) + " pool geometry exceeds observer bounds";
        return false;
    }
    const std::uint32_t capacity = image.slabCount * image.elementsPerSlab;
    std::vector<bool> freeSlots(capacity, false);
    const typename Ps::PoolBase<T, Alloc>::FreeList* node =
        pool.mFreeElement;
    while (node)
    {
        std::uint32_t slot = 0;
        if (!poolSlot(pool, node, slot) || freeSlots[slot] ||
            image.freeOrder.size() >= capacity)
        {
            error = std::string(name) + " pool free list is invalid";
            return false;
        }
        freeSlots[slot] = true;
        image.freeOrder.push_back(slot);
        node = node->mNext;
    }
    for (std::uint32_t i = 0; i < capacity; ++i)
        if (!freeSlots[i]) image.usedSlots.push_back(i);
    if (image.usedSlots.size() != image.usedCount)
    {
        error = std::string(name) + " pool used/free partition differs";
        return false;
    }
    return true;
}

bool inventory(PxScene& scene, ActorIds& actors, ShapeKeys& shapes,
               std::map<std::uint32_t, PxActor*>& publicById,
               std::string& error)
{
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    if (count > 4096) { error = "rigid actor inventory exceeds bound"; return false; }
    std::vector<PxActor*> publicActors(count);
    if (count && scene.getActors(flags, publicActors.data(), count) != count)
    { error = "rigid actor inventory changed during capture"; return false; }
    std::set<std::uint32_t> ids;
    for (PxActor* actor : publicActors)
    {
        const std::uintptr_t raw =
            reinterpret_cast<std::uintptr_t>(actor->userData);
        if (!raw || raw > std::numeric_limits<std::uint32_t>::max() ||
            !ids.insert(static_cast<std::uint32_t>(raw)).second)
        { error = "rigid actor userData IDs are absent or duplicate"; return false; }
        publicById.insert(std::make_pair(static_cast<std::uint32_t>(raw), actor));
        Sc::Actor* sim = NULL;
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
            sim = static_cast<Sc::ActorCore&>(
                static_cast<NpRigidDynamic*>(actor)->getScbBodyFast()
                    .getScBody()).getSim();
        else
            sim = static_cast<Sc::ActorCore&>(
                static_cast<NpRigidStatic*>(actor)->getScbRigidStaticFast()
                    .getScStatic()).getSim();
        if (!sim || !actors.insert(std::make_pair(
                static_cast<Sc::RigidSim*>(sim),
                static_cast<std::uint32_t>(raw))).second)
        { error = "rigid simulation actor identity is invalid"; return false; }
        PxRigidActor* rigid = static_cast<PxRigidActor*>(actor);
        const PxU32 shapeCount = rigid->getNbShapes();
        std::vector<PxShape*> publicShapes(shapeCount);
        if (shapeCount && rigid->getShapes(publicShapes.data(), shapeCount) !=
                              shapeCount)
        { error = "shape inventory changed during capture"; return false; }
        for (PxU32 i = 0; i < shapeCount; ++i)
        {
            const Sc::ShapeCore* core = &static_cast<NpShape*>(publicShapes[i])
                ->getScbShape().getScShape();
            ActorGraphShapeKey shape;
            shape.actor = static_cast<std::uint32_t>(raw);
            shape.index = i;
            if (!shapes.insert(std::make_pair(core, shape)).second)
            { error = "shape core identity is duplicated"; return false; }
        }
    }
    return true;
}

std::uint32_t actorId(const ActorIds& ids, const Sc::RigidSim& actor)
{
    const ActorIds::const_iterator found = ids.find(&actor);
    return found == ids.end() ? 0 : found->second;
}

bool fillPair(const ActorIds& ids,
              const std::map<std::uint32_t, PxActor*>& publicById,
              const Sc::ActorPair& pair,
              Sc::NPhaseCore& nphase, ActorGraphPairRow& row,
              std::string& error)
{
    row.actorA = actorId(ids, pair.getActorA());
    row.actorB = actorId(ids, pair.getActorB());
    if (!row.actorA || !row.actorB || row.actorA == row.actorB)
    { error = "ActorPair has unknown or equal actor endpoints"; return false; }
    row.key = key(row.actorA, row.actorB);
    row.refCount = pair.mRefCount;
    row.touchCount = pair.mTouchCount;
    row.internalFlags = pair.mInternalFlags;
    row.inReportSet = pair.isInContactReportActorPairSet() != 0;
    row.hasReportData = pair.mReportData != NULL;
    if (!pair.mReportData) return true;

    const Sc::ActorPairContactReportData& report = *pair.mReportData;
    if (!poolSlot(nphase.mActorPairContactReportDataPool,
                  &report, row.reportPoolSlot) ||
        report.mActorAID != pair.getActorA().getID() ||
        report.mActorBID != pair.getActorB().getID() ||
        report.mPxActorA != publicById.at(row.actorA) ||
        report.mPxActorB != publicById.at(row.actorB))
    { error = "ActorPair report identity or pool slot is invalid"; return false; }
    row.reportActorA = row.actorA;
    row.reportActorB = row.actorB;
    row.reportResetStamp = report.mStrmResetStamp;
    row.clientA = report.mActorAClientID;
    row.clientB = report.mActorBClientID;
    row.behaviorA = report.mActorAClientBehavior;
    row.behaviorB = report.mActorBClientBehavior;

    const Sc::ContactStreamManager& stream = report.mContactStreamManager;
    row.streamMaxPairs = stream.maxPairCount;
    row.streamFlagsAndMaxExtra = stream.flags_and_maxExtraDataBlocks;
    // Constructor initializes only these two members. The other three are
    // read only after a report stream has actually been allocated.
    row.streamValid = report.mStrmResetStamp != 0xffffffffu &&
                      stream.maxPairCount != 0;
    if (row.streamValid)
    {
        row.streamBufferIndex = stream.bufferIndex;
        row.streamCurrentPairs = stream.currentPairCount;
        row.streamExtraSize = stream.extraDataSize;
    }
    return true;
}

} // namespace

bool ActorGraphPairRow::operator==(const ActorGraphPairRow& b) const
{
    return key == b.key && actorA == b.actorA && actorB == b.actorB &&
        poolSlot == b.poolSlot && refCount == b.refCount &&
        touchCount == b.touchCount && internalFlags == b.internalFlags &&
        sipOwners == b.sipOwners && touchingOwners == b.touchingOwners &&
        inReportSet == b.inReportSet && hasReportData == b.hasReportData &&
        reportPoolSlot == b.reportPoolSlot &&
        reportResetStamp == b.reportResetStamp &&
        reportActorA == b.reportActorA && reportActorB == b.reportActorB &&
        clientA == b.clientA && clientB == b.clientB &&
        behaviorA == b.behaviorA && behaviorB == b.behaviorB &&
        streamValid == b.streamValid &&
        streamBufferIndex == b.streamBufferIndex &&
        streamMaxPairs == b.streamMaxPairs &&
        streamCurrentPairs == b.streamCurrentPairs &&
        streamExtraSize == b.streamExtraSize &&
        streamFlagsAndMaxExtra == b.streamFlagsAndMaxExtra;
}

bool CaptureActorPairGraph(PxScene& scene, ActorPairGraphImage& image,
                           std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    { error = "capture requires a completed fetchResults boundary"; return false; }
    ActorIds actorIds;
    ShapeKeys shapeKeys;
    std::map<std::uint32_t, PxActor*> publicById;
    if (!inventory(scene, actorIds, shapeKeys, publicById, error)) return false;

    Sc::Scene& sc = np.getScene().getScScene();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    Sc::NPhaseCore& nphase = *sc.getNPhaseCore();
    ActorPairGraphImage next;
    if (!capturePool(nphase.mActorPairPool, next.actorPairPool,
                     "ActorPair", error) ||
        !capturePool(nphase.mActorPairContactReportDataPool,
                     next.reportDataPool, "ActorPair report", error))
        return false;

    typedef std::pair<ActorGraphShapeKey, ActorGraphShapeKey> SipKey;
    std::set<SipKey> seenSips;
    std::map<const Sc::ActorPair*, std::pair<std::uint32_t, std::uint32_t> >
        ownerCounts;
    Cm::Range<Sc::Interaction*const> contacts = interactions.getInteractions(
        Sc::PX_INTERACTION_TYPE_OVERLAP);
    while (!contacts.empty())
    {
        const Sc::ShapeInstancePairLL* sip =
            static_cast<const Sc::ShapeInstancePairLL*>(contacts.front());
        contacts.popFront();
        const ShapeKeys::const_iterator s0 = shapeKeys.find(
            &sip->getShape0().getCore());
        const ShapeKeys::const_iterator s1 = shapeKeys.find(
            &sip->getShape1().getCore());
        if (s0 == shapeKeys.end() || s1 == shapeKeys.end())
        { error = "contact SIP references an unknown shape"; return false; }
        SipKey semantic(s0->second, s1->second);
        if (semantic.second < semantic.first)
            std::swap(semantic.first, semantic.second);
        if (!seenSips.insert(semantic).second)
        { error = "duplicate semantic contact SIP key"; return false; }
        ActorGraphSipRow row;
        row.shape0 = s0->second;
        row.shape1 = s1->second;
        row.actorKey = key(row.shape0.actor, row.shape1.actor);
        row.hasTouch = sip->hasTouch() != 0;
        row.isReportPair = sip->isReportPair() != 0;
        if (!poolSlot(nphase.mLLSipPool, sip, row.sipSlot) ||
            !poolSlot(nphase.mActorPairPool, sip->getActorPair(),
                      row.actorPairSlot))
        { error = "contact SIP or ActorPair is outside its pool"; return false; }
        const Sc::ActorPair* pair = sip->getActorPair();
        if (!(key(actorId(actorIds, pair->getActorA()),
                   actorId(actorIds, pair->getActorB())) == row.actorKey))
        { error = "contact SIP ActorPair endpoint ownership differs"; return false; }
        std::pair<std::uint32_t, std::uint32_t>& counts = ownerCounts[pair];
        ++counts.first;
        if (row.hasTouch) ++counts.second;
        next.sips.push_back(row);
    }

    std::map<const Sc::ActorPair*, ActorGraphKey> keysByPointer;
    std::set<ActorGraphKey> seenActorKeys;
    const auto& pool = nphase.mActorPairPool;
    for (std::uint32_t slot : next.actorPairPool.usedSlots)
    {
        const std::uint32_t slab = slot / pool.mElementsPerSlab;
        const std::uint32_t offset = slot % pool.mElementsPerSlab;
        const Sc::ActorPair* pair =
            reinterpret_cast<const Sc::ActorPair*>(pool.mSlabs[slab]) + offset;
        ActorGraphPairRow row;
        row.poolSlot = slot;
        if (!fillPair(actorIds, publicById, *pair, nphase, row, error))
            return false;
        if (!seenActorKeys.insert(row.key).second)
        { error = "duplicate ActorPair for one canonical actor key"; return false; }
        const auto owners = ownerCounts.find(pair);
        if (owners != ownerCounts.end())
        {
            row.sipOwners = owners->second.first;
            row.touchingOwners = owners->second.second;
        }
        keysByPointer.insert(std::make_pair(pair, row.key));
        next.actorPairs.push_back(row);
    }
    if (keysByPointer.size() < ownerCounts.size())
    { error = "contact SIP owns an unallocated ActorPair"; return false; }

    std::set<const Sc::ActorPair*> seenReportSet;
    for (PxU32 i = 0; i < nphase.mContactReportActorPairSet.size(); ++i)
    {
        const Sc::ActorPair* pair = nphase.mContactReportActorPairSet[i];
        const auto found = keysByPointer.find(pair);
        if (found == keysByPointer.end() || !seenReportSet.insert(pair).second)
        { error = "contact report set has an unknown or repeated ActorPair"; return false; }
        next.reportSetOrder.push_back(found->second);
    }
    std::set<std::uint32_t> reportSlots;
    for (ActorGraphPairRow& row : next.actorPairs)
    {
        const Sc::ActorPair* pair = NULL;
        for (const auto& item : keysByPointer)
            if (item.second == row.key) { pair = item.first; break; }
        const bool inSet = seenReportSet.count(pair) != 0;
        if (row.inReportSet != inSet ||
            row.refCount != row.sipOwners + (inSet ? 1u : 0u) ||
            row.touchCount != row.touchingOwners ||
            (!row.sipOwners && !inSet))
        { error = "ActorPair flags/refcount/touchcount do not match live owners"; return false; }
        if (row.hasReportData)
        {
            if (!reportSlots.insert(row.reportPoolSlot).second ||
                !std::binary_search(next.reportDataPool.usedSlots.begin(),
                                    next.reportDataPool.usedSlots.end(),
                                    row.reportPoolSlot))
            { error = "ActorPair report pool ownership differs"; return false; }
        }
    }
    if (reportSlots.size() != next.reportDataPool.usedCount)
    { error = "unowned ActorPair report data remains in pool"; return false; }
    std::sort(next.actorPairs.begin(), next.actorPairs.end(),
              [](const ActorGraphPairRow& a, const ActorGraphPairRow& b) {
                  return a.key < b.key;
              });
    image = next;
    error.clear();
    return true;
}

} // namespace physx333_offline
