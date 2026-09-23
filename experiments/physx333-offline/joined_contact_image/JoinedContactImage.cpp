#include "JoinedContactImage.h"

#include <algorithm>
#include <cstring>
#include <map>
#include <set>

#include "PxPhysicsAPI.h"

// Source-private access is limited to this read-only, pinned Win32 fixture.
#define private public
#define protected public
#include "NpScene.h"
#include "NpShape.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScActor.h"
#include "ScActorCore.h"
#include "ScActorSim.h"
#include "ScInteractionScene.h"
#include "ScNPhaseCore.h"
#include "ScScene.h"
#include "ScShapeSim.h"
#include "ScShapeInstancePairLL.h"
#include "PxsContext.h"
#include "PxsContactManager.h"
#include "PxcNpMemBlockPool.h"
#include "GuPersistentContactManifold.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "Joined contact image requires Win32 PhysX");
static_assert(sizeof(PxsIslandManagerEdgeHook) == sizeof(PxU32),
              "Unexpected island hook layout");

template<class T, class Alloc>
bool poolSlot(const Ps::Pool<T, Alloc>& pool, const void* object,
              PxU32& result)
{
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(object);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const std::uintptr_t first =
            reinterpret_cast<std::uintptr_t>(pool.mSlabs[slab]);
        const std::uintptr_t end = first + pool.mElementsPerSlab * sizeof(T);
        if (address >= first && address < end &&
            (address - first) % sizeof(T) == 0)
        {
            result = slab * pool.mElementsPerSlab +
                static_cast<PxU32>((address - first) / sizeof(T));
            return true;
        }
    }
    return false;
}

JoinedContactBitmap captureBitmap(const Cm::BitMap& bitmap)
{
    JoinedContactBitmap result;
    result.address = reinterpret_cast<std::uintptr_t>(bitmap.getWords());
    if (bitmap.getWordCount())
        result.words.assign(bitmap.getWords(),
                            bitmap.getWords() + bitmap.getWordCount());
    return result;
}

Sc::Actor* scActor(PxActor& actor)
{
    if (actor.getType() == PxActorType::eRIGID_DYNAMIC)
        return static_cast<Sc::ActorCore&>(
            static_cast<NpRigidDynamic&>(actor).getScbBodyFast()
                .getScBody()).getSim();
    return static_cast<Sc::ActorCore&>(
        static_cast<NpRigidStatic&>(actor).getScbRigidStaticFast()
            .getScStatic()).getSim();
}

bool fixtureIds(PxScene& scene,
                std::map<const Sc::ShapeCore*, JoinedContactShapeKey>& shapes,
                std::string& error)
{
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    if (count != 13)
    { error = "Joined contact fixture requires thirteen rigid actors"; return false; }
    std::vector<PxActor*> actors(count);
    if (scene.getActors(flags, actors.data(), count) != count)
    { error = "Rigid actor inventory changed during capture"; return false; }
    std::set<PxU32> actorIds;
    for (PxActor* actor : actors)
    {
        const PxU32 actorId = static_cast<PxU32>(
            reinterpret_cast<std::uintptr_t>(actor->userData));
        if (!actorId || !actorIds.insert(actorId).second || !scActor(*actor))
        { error = "Rigid actor ID is missing or duplicate"; return false; }
        PxRigidActor& rigid = *static_cast<PxRigidActor*>(actor);
        std::vector<PxShape*> publicShapes(rigid.getNbShapes());
        if (rigid.getShapes(publicShapes.data(),
                static_cast<PxU32>(publicShapes.size())) != publicShapes.size())
        { error = "Rigid shape inventory changed during capture"; return false; }
        for (PxU32 i = 0; i < publicShapes.size(); ++i)
        {
            const Sc::ShapeCore* core =
                &static_cast<NpShape&>(*publicShapes[i]).getScbShape()
                    .getScShape();
            JoinedContactShapeKey key; key.actor = actorId; key.shape = i;
            if (!shapes.insert(std::make_pair(core, key)).second)
            { error = "Rigid shape core is duplicated"; return false; }
        }
    }
    return true;
}

bool geometrySupported(const PxcNpWorkUnit& work)
{
    const PxU8 a = work.geomType0, b = work.geomType1;
    return ((a == PxGeometryType::eCAPSULE && b == PxGeometryType::eBOX) ||
            (a == PxGeometryType::eBOX && b == PxGeometryType::eCAPSULE)) &&
           work.shapeCore0 && work.shapeCore1 &&
           a == work.shapeCore0->geometry.getType() &&
           b == work.shapeCore1->geometry.getType();
}

bool usedWarmIndicesEqual(const JoinedContactRow& a,
                          const JoinedContactRow& b)
{
    const std::size_t count = a.manifoldWarmStartCount;
    if (count != b.manifoldWarmStartCount || count > 4 ||
        a.manifoldIndexBytes.size() != 8 || b.manifoldIndexBytes.size() != 8)
        return false;
    return std::equal(a.manifoldIndexBytes.begin(),
                      a.manifoldIndexBytes.begin() + count,
                      b.manifoldIndexBytes.begin()) &&
           std::equal(a.manifoldIndexBytes.begin() + 4,
                      a.manifoldIndexBytes.begin() + 4 + count,
                      b.manifoldIndexBytes.begin() + 4);
}

} // namespace

bool JoinedContactRow::portable(const JoinedContactRow& b) const
{
    return key == b.key && actorPairKey == b.actorPairKey &&
        sceneIndex == b.sceneIndex && sipSlot == b.sipSlot &&
        actorPairSlot == b.actorPairSlot && managerSlot == b.managerSlot &&
        islandEdge == b.islandEdge && sipFlags == b.sipFlags &&
        reportStamp == b.reportStamp &&
        reportPairIndex == b.reportPairIndex &&
        reportStreamIndex == b.reportStreamIndex &&
        actorPairFlags == b.actorPairFlags &&
        actorPairTouchCount == b.actorPairTouchCount &&
        actorPairRefCount == b.actorPairRefCount &&
        reportPoolSlot == b.reportPoolSlot &&
        reportResetStamp == b.reportResetStamp &&
        reportActorA == b.reportActorA && reportActorB == b.reportActorB &&
        reportStreamManager == b.reportStreamManager &&
        managerFlags == b.managerFlags &&
        managerStatusFlags == b.managerStatusFlags &&
        managerWords == b.managerWords &&
        compressedContactBytes == b.compressedContactBytes &&
        pairCacheBytes == b.pairCacheBytes &&
        manifoldKind == b.manifoldKind &&
        manifoldContactCount == b.manifoldContactCount &&
        manifoldWarmStartCount == b.manifoldWarmStartCount &&
        manifoldTransformBytes == b.manifoldTransformBytes &&
        usedWarmIndicesEqual(*this, b) &&
        manifoldContactBytes == b.manifoldContactBytes;
}

bool JoinedContactRow::exact(const JoinedContactRow& b) const
{
    return portable(b) && workUnitBytes == b.workUnitBytes &&
        manifoldIndexBytes == b.manifoldIndexBytes;
}

bool JoinedContactImage::portable(const JoinedContactImage& b,
                                  std::string& difference) const
{
    if (!oracle.equals(b.oracle, difference)) return false;
    if (!(actorPairs == b.actorPairs))
    { difference = "ActorPair ownership/report graph"; return false; }
    if (rows.size() != b.rows.size())
    { difference = "contact row count"; return false; }
    for (std::size_t i = 0; i < rows.size(); ++i)
        if (!rows[i].portable(b.rows[i]))
        { difference = "contact row " + std::to_string(i); return false; }
    if (persistentEventOrder != b.persistentEventOrder ||
        forceThresholdEventOrder != b.forceThresholdEventOrder ||
        nextPersistentPair != b.nextPersistentPair ||
        managerFreeOrder != b.managerFreeOrder ||
        managerUse.words != b.managerUse.words ||
        activeManagers.words != b.activeManagers.words ||
        modifiableManagers.words != b.modifiableManagers.words ||
        touchEventManagers.words != b.touchEventManagers.words ||
        reportBufferIndex != b.reportBufferIndex ||
        reportBufferSize != b.reportBufferSize ||
        reportBufferDefaultSize != b.reportBufferDefaultSize ||
        reportBufferLastIndex != b.reportBufferLastIndex ||
        reportBufferAllocationLocked != b.reportBufferAllocationLocked ||
        reportBufferBytes != b.reportBufferBytes)
    { difference = "contact event, pool, bitmap, or report-buffer state"; return false; }
    difference.clear();
    return true;
}

bool JoinedContactImage::exact(const JoinedContactImage& b,
                               std::string& difference) const
{
    if (!portable(b, difference)) return false;
    if (scene != b.scene || !managerUse.exact(b.managerUse) ||
        !activeManagers.exact(b.activeManagers) ||
        !modifiableManagers.exact(b.modifiableManagers) ||
        !touchEventManagers.exact(b.touchEventManagers))
    { difference = "same-scene pointer/bitmap identity"; return false; }
    for (std::size_t i = 0; i < rows.size(); ++i)
        if (!rows[i].exact(b.rows[i]))
        { difference = "same-scene raw contact row " + std::to_string(i); return false; }
    difference.clear();
    return true;
}

bool CaptureJoinedContactImage(PxScene& scene, JoinedContactImage& image,
                               std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    { error = "Joined contact capture requires completed fetchResults"; return false; }
    JoinedContactImage next;
    if (!CaptureOracle(scene, next.oracle, error) ||
        !CaptureActorPairGraph(scene, next.actorPairs, error))
        return false;
    Sc::Scene& sc = np.getScene().getScScene();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    Sc::NPhaseCore& nphase = *sc.getNPhaseCore();
    const PxU32 count = interactions.getInteractionCount(
        Sc::PX_INTERACTION_TYPE_OVERLAP);
    const PxU32 triggers = interactions.getInteractionCount(
        Sc::PX_INTERACTION_TYPE_TRIGGER);
    const PxU32 markers = interactions.getInteractionCount(
        Sc::PX_INTERACTION_TYPE_MARKER);
    if (!((count == 12 && triggers == 4) ||
          (count == 8 && triggers == 2)) || markers != 2 ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_OVERLAP) != count ||
        next.actorPairs.sips.size() != count)
    { error = "Joined scene is outside 12/4/2 or 8/2/2 fixture"; return false; }
    const auto cmWords = next.oracle.parts.find("contact.managers");
    if (cmWords == next.oracle.parts.end() ||
        cmWords->second.size() != count * 14)
    { error = "Oracle contact-manager row layout differs"; return false; }
    std::map<const Sc::ShapeCore*, JoinedContactShapeKey> shapeIds;
    if (!fixtureIds(scene, shapeIds, error)) return false;
    next.scene = reinterpret_cast<std::uintptr_t>(&scene);
    std::map<const Sc::ShapeInstancePairLL*, JoinedContactKey> livePairs;
    std::set<JoinedContactKey> distinctKeys;
    std::set<PxU32> distinctManagerSlots;
    Cm::Range<Sc::Interaction*const> range = interactions.getInteractions(
        Sc::PX_INTERACTION_TYPE_OVERLAP);
    for (PxU32 i = 0; i < count; ++i)
    {
        if (range.empty())
        { error = "Contact interaction array shortened"; return false; }
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        if (!sip || !sip->mManager || sip->mSceneId != i)
        { error = "Contact SIP registration or manager is invalid"; return false; }
        const auto s0 = shapeIds.find(&sip->getShape0().getCore());
        const auto s1 = shapeIds.find(&sip->getShape1().getCore());
        if (s0 == shapeIds.end() || s1 == shapeIds.end())
        { error = "Contact SIP endpoint is outside fixture"; return false; }
        JoinedContactRow row;
        row.key.shape0 = s0->second; row.key.shape1 = s1->second;
        row.actorPairKey.first = std::min(s0->second.actor, s1->second.actor);
        row.actorPairKey.second = std::max(s0->second.actor, s1->second.actor);
        row.sceneIndex = i;
        if (!distinctKeys.insert(row.key).second ||
            !livePairs.insert(std::make_pair(sip, row.key)).second ||
            !poolSlot(nphase.mLLSipPool, sip, row.sipSlot) ||
            !poolSlot(nphase.mActorPairPool, sip->getActorPair(),
                      row.actorPairSlot))
        { error = "Contact SIP identity or physical pool slot is invalid"; return false; }
        const ActorGraphSipRow& graphRow = next.actorPairs.sips[i];
        if (graphRow.shape0.actor != row.key.shape0.actor ||
            graphRow.shape0.index != row.key.shape0.shape ||
            graphRow.shape1.actor != row.key.shape1.actor ||
            graphRow.shape1.index != row.key.shape1.shape ||
            !(graphRow.actorKey == row.actorPairKey) ||
            graphRow.sipSlot != row.sipSlot ||
            graphRow.actorPairSlot != row.actorPairSlot)
        { error = "Contact SIP and ActorPair graph disagree"; return false; }
        row.managerSlot = sip->mManager->getIndex();
        if (!distinctManagerSlots.insert(row.managerSlot).second)
        { error = "Two SIPs share a contact-manager slot"; return false; }
        std::memcpy(&row.islandEdge, &sip->mLLIslandHook,
                    sizeof(row.islandEdge));
        row.sipFlags = sip->mFlags;
        row.reportStamp = sip->mContactReportStamp;
        row.reportPairIndex = sip->mReportPairIndex;
        row.reportStreamIndex = sip->mReportStreamIndex;
        Sc::ActorPair& actorPair = *sip->getActorPair();
        row.actorPairFlags = actorPair.mInternalFlags;
        row.actorPairTouchCount = actorPair.mTouchCount;
        row.actorPairRefCount = actorPair.mRefCount;
        if (actorPair.mReportData)
        {
            if (!poolSlot(nphase.mActorPairContactReportDataPool,
                          actorPair.mReportData, row.reportPoolSlot))
            { error = "Report data is outside its physical pool"; return false; }
            row.reportResetStamp = actorPair.mReportData->mStrmResetStamp;
            row.reportActorA = actorPair.mReportData->mActorAID;
            row.reportActorB = actorPair.mReportData->mActorBID;
            const unsigned char* report = reinterpret_cast<const unsigned char*>(
                &actorPair.mReportData->mContactStreamManager);
            row.reportStreamManager.assign(
                report, report + sizeof(Sc::ContactStreamManager));
        }
        PxsContactManager& manager = *sip->mManager;
        row.managerFlags = manager.mFlags;
        const PxcNpWorkUnit& work = manager.getWorkUnit();
        if (!geometrySupported(work) || work.index != row.managerSlot ||
            work.compressedContactSize > 65536 ||
            (work.compressedContactSize && !work.compressedContacts) ||
            (work.pairCache.size && !work.pairCache.ptr) ||
            (!work.pairCache.size && work.pairCache.ptr))
        { error = "Contact work-unit geometry or stream is unsupported"; return false; }
        row.managerStatusFlags = work.statusFlags;
        const unsigned char* raw =
            reinterpret_cast<const unsigned char*>(&work);
        row.workUnitBytes.assign(raw, raw + sizeof(PxcNpWorkUnit));
        row.managerWords.assign(cmWords->second.begin() + i * 14,
                                cmWords->second.begin() + (i + 1) * 14);
        if (row.managerWords[0] != row.managerSlot ||
            row.managerWords[1] != row.managerFlags ||
            row.managerWords[2] != row.managerStatusFlags)
        { error = "Contact manager and Oracle row disagree"; return false; }
        if (work.compressedContactSize)
            row.compressedContactBytes.assign(work.compressedContacts,
                work.compressedContacts + work.compressedContactSize);
        if (work.pairCache.size)
            row.pairCacheBytes.assign(work.pairCache.ptr,
                work.pairCache.ptr + work.pairCache.size);
        if (work.pairCache.manifold)
        {
            if ((work.pairCache.manifold & 15u) != 0)
            { error = "Only aligned single PCM manifolds are supported"; return false; }
            const Gu::PersistentContactManifold& manifold =
                *reinterpret_cast<const Gu::PersistentContactManifold*>(
                    work.pairCache.manifold);
            if (manifold.mNumContacts > GU_MANIFOLD_CACHE_SIZE ||
                manifold.mNumWarmStartPoints > 4)
            { error = "PCM contact or warm-start count is invalid"; return false; }
            row.manifoldKind = 1;
            row.manifoldContactCount = manifold.mNumContacts;
            row.manifoldWarmStartCount = manifold.mNumWarmStartPoints;
            const unsigned char* transform =
                reinterpret_cast<const unsigned char*>(&manifold.mRelativeTransform);
            row.manifoldTransformBytes.assign(transform,
                transform + sizeof(manifold.mRelativeTransform));
            row.manifoldIndexBytes.assign(manifold.mAIndice,
                                          manifold.mAIndice + 4);
            row.manifoldIndexBytes.insert(row.manifoldIndexBytes.end(),
                                          manifold.mBIndice,
                                          manifold.mBIndice + 4);
            const unsigned char* contacts =
                reinterpret_cast<const unsigned char*>(manifold.mContactPoints);
            row.manifoldContactBytes.assign(contacts, contacts +
                row.manifoldContactCount * sizeof(Gu::PersistentContact));
        }
        next.rows.push_back(row);
    }
    if (!range.empty())
    { error = "Contact interaction array grew during capture"; return false; }
    next.nextPersistentPair =
        nphase.mNextFramePersistentContactEventPairIndex;
    if (next.nextPersistentPair >
        nphase.mPersistentContactEventPairList.size())
    { error = "Persistent contact split exceeds list size"; return false; }
    for (PxU32 i = 0; i < nphase.mPersistentContactEventPairList.size(); ++i)
    {
        const auto found = livePairs.find(
            nphase.mPersistentContactEventPairList[i]);
        if (found == livePairs.end())
        { error = "Persistent event references unknown SIP"; return false; }
        next.persistentEventOrder.push_back(found->second);
    }
    for (PxU32 i = 0; i < nphase.mForceThresholdContactEventPairList.size(); ++i)
    {
        const auto found = livePairs.find(
            nphase.mForceThresholdContactEventPairList[i]);
        if (found == livePairs.end())
        { error = "Threshold event references unknown SIP"; return false; }
        next.forceThresholdEventOrder.push_back(found->second);
    }
    PxsContext& context = *interactions.getLowLevelContext();
    const auto& pool = context.mContactManagerPool;
    next.managerUse = captureBitmap(pool.mUseBitmap);
    next.activeManagers = captureBitmap(context.mActiveContactManager);
    next.modifiableManagers = captureBitmap(context.mModifiableContactManager);
    next.touchEventManagers = captureBitmap(context.mContactManagerTouchEvent);
    const PxU32 capacity = pool.mSlabCount * pool.mEltsPerSlab;
    std::set<PxU32> freeSlots;
    for (PxU32 i = 0; i < pool.mFreeCount; ++i)
    {
        const PxU32 slot = pool.mFreeList[i]->getIndex();
        if (slot >= capacity || !freeSlots.insert(slot).second ||
            pool.mUseBitmap.boundedTest(slot))
        { error = "Contact-manager free stack is invalid"; return false; }
        next.managerFreeOrder.push_back(slot);
    }
    if (freeSlots.size() + distinctManagerSlots.size() != capacity)
    { error = "Contact-manager used/free partition differs"; return false; }
    Sc::ContactReportBuffer& reports = nphase.mContactReportBuffer;
    next.reportBufferIndex = reports.mCurrentBufferIndex;
    next.reportBufferSize = reports.mCurrentBufferSize;
    next.reportBufferDefaultSize = reports.mDefaultBufferSize;
    next.reportBufferLastIndex = reports.mLastBufferIndex;
    next.reportBufferAllocationLocked = reports.mAllocationLocked ? 1u : 0u;
    if (next.reportBufferIndex > next.reportBufferSize ||
        (next.reportBufferIndex && !reports.mBuffer))
    { error = "Contact report buffer cursor is invalid"; return false; }
    if (next.reportBufferIndex)
        next.reportBufferBytes.assign(reports.mBuffer,
            reports.mBuffer + next.reportBufferIndex);
    image = next;
    error.clear();
    return true;
}

} // namespace physx333_offline
