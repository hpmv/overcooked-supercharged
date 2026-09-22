#include "InteractionImage.h"
#include "InteractionReportBridge.h"

#include <algorithm>
#include <array>
#include <cstring>
#include <map>
#include <set>
#include <sstream>
#include <vector>
#include <windows.h>

// This access shim is confined to the disposable, pinned source experiment.
#define private public
#define protected public
#include "NpScene.h"
#include "ScNPhaseCore.h"
#include "ScActorPair.h"
#include "ScShapeInstancePairLL.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"
#include "PxsContactManager.h"
#include "PxcNpMemBlockPool.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "Interaction image requires Win32 PhysX");
static_assert(sizeof(PxsIslandManagerEdgeHook) == sizeof(PxU32),
              "Unexpected island edge hook layout");

typedef std::array<Sc::ShapeInstancePairLL*, 6> PairPointers;

InteractionImage::Bitmap captureBitmap(const Cm::BitMap& bitmap)
{
    InteractionImage::Bitmap result;
    result.address = reinterpret_cast<uintptr_t>(bitmap.getWords());
    if (bitmap.getWordCount())
        result.words.assign(bitmap.getWords(),
                            bitmap.getWords() + bitmap.getWordCount());
    return result;
}

bool bitmapStorageSame(const Cm::BitMap& bitmap,
                       const InteractionImage::Bitmap& image)
{
    return reinterpret_cast<uintptr_t>(bitmap.getWords()) == image.address &&
           bitmap.getWordCount() == image.words.size();
}

void writeBitmap(Cm::BitMap& bitmap,
                 const InteractionImage::Bitmap& image)
{
    if (!image.words.empty())
        std::memcpy(bitmap.getWords(), image.words.data(),
                    image.words.size() * sizeof(PxU32));
}

template<class T, class Alloc>
bool poolSlot(const Ps::Pool<T, Alloc>& pool, const void* pointer,
              PxU32& result)
{
    const uintptr_t address = reinterpret_cast<uintptr_t>(pointer);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const uintptr_t first =
            reinterpret_cast<uintptr_t>(pool.mSlabs[slab]);
        const uintptr_t last = first + pool.mElementsPerSlab * sizeof(T);
        if (address >= first && address < last &&
            (address - first) % sizeof(T) == 0)
        {
            result = slab * pool.mElementsPerSlab +
                     static_cast<PxU32>((address - first) / sizeof(T));
            return true;
        }
    }
    return false;
}

bool findMover(Sc::ShapeInstancePairLL& pair, Sc::Actor*& mover)
{
    Sc::Actor& a = pair.getActor0();
    Sc::Actor& b = pair.getActor1();
    if (a.getActorType() == PxActorType::eRIGID_DYNAMIC &&
        b.getActorType() == PxActorType::eRIGID_STATIC)
    {
        mover = &a;
        return true;
    }
    if (b.getActorType() == PxActorType::eRIGID_DYNAMIC &&
        a.getActorType() == PxActorType::eRIGID_STATIC)
    {
        mover = &b;
        return true;
    }
    return false;
}

bool fixtureRows(PxScene& scene, InteractionImage& next,
                 PairPointers& byShape, Sc::Actor*& mover,
                 std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "Interaction capture requires a completed fetchResults boundary";
        return false;
    }
    if (!CaptureNPhaseTopology(scene, next.topology, error)) return false;
    if (next.topology.pairs.size() != 6 ||
        next.topology.activePairCount != 6)
    {
        error = "Interaction image requires six active fixture pairs";
        return false;
    }

    Sc::Scene& sc = np.getScene().getScScene();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    Sc::NPhaseCore& nphase = *sc.getNPhaseCore();
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) != 6 ||
        interactions.getActiveInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) != 6 ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER))
    {
        error = "Interaction scene is outside the six-active-pair fixture";
        return false;
    }

    byShape.fill(NULL);
    next.scene = reinterpret_cast<uintptr_t>(&scene);
    next.sceneActiveCount = 6;
    next.pairs.resize(6);
    Cm::Range<Sc::Interaction*const> range =
        interactions.getInteractions(Sc::PX_INTERACTION_TYPE_OVERLAP);
    for (size_t sceneIndex = 0; sceneIndex < 6; ++sceneIndex)
    {
        if (range.empty())
        {
            error = "Interaction array changed during capture";
            return false;
        }
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        const NPhasePairTopology& topology = next.topology.pairs[sceneIndex];
        const PxU32 shape = topology.shape0.shapeIndex;
        if (topology.shape0.actorId != 7 || shape >= 6 ||
            topology.shape1.actorId != shape + 1 ||
            topology.shape1.shapeIndex != 0 || byShape[shape] ||
            sip->mSceneId != sceneIndex || !sip->mManager)
        {
            error = "Shape, registration, or manager identity is inconsistent";
            return false;
        }
        Sc::Actor* thisMover = NULL;
        if (!findMover(*sip, thisMover) || (mover && mover != thisMover))
        {
            error = "Pairs do not share exactly one dynamic fixture actor";
            return false;
        }
        mover = thisMover;
        byShape[shape] = sip;
        next.sceneOrder.push_back(shape);

        InteractionPairImage& row = next.pairs[shape];
        row.moverShape = shape;
        if (!poolSlot(nphase.mLLSipPool, sip, row.sipPoolSlot) ||
            !poolSlot(nphase.mActorPairPool, sip->getActorPair(),
                      row.actorPairPoolSlot))
        {
            error = "Pair address is outside the NPhase allocation pools";
            return false;
        }
        row.managerSlot = sip->mManager->getIndex();
        std::memcpy(&row.islandEdge, &sip->mLLIslandHook,
                    sizeof(row.islandEdge));
        row.sipFlags = sip->mFlags;
        row.contactReportStamp = sip->mContactReportStamp;
        row.reportPairIndex = sip->mReportPairIndex;
        row.reportStreamIndex = sip->mReportStreamIndex;
        Sc::ActorPair& actorPair = *sip->getActorPair();
        row.actorPairFlags = actorPair.mInternalFlags;
        row.actorPairTouchCount = actorPair.mTouchCount;
        row.actorPairRefCount = actorPair.mRefCount;
        row.managerFlags = sip->mManager->mFlags;
        const PxcNpWorkUnit& work = sip->mManager->getWorkUnit();
        row.managerStatusFlags = work.statusFlags;
        const unsigned char* workBegin =
            reinterpret_cast<const unsigned char*>(&work);
        row.workUnitBytes.assign(workBegin,
                                 workBegin + sizeof(PxcNpWorkUnit));
        if (work.compressedContactSize > 65536 ||
            (work.compressedContactSize && !work.compressedContacts) ||
            (work.pairCache.size && !work.pairCache.ptr) ||
            (!work.pairCache.size && work.pairCache.ptr))
        {
            error = "Contact stream or local cache layout is inconsistent";
            return false;
        }
        if (work.compressedContactSize)
            row.compressedContactBytes.assign(
                work.compressedContacts,
                work.compressedContacts + work.compressedContactSize);
        if (work.pairCache.size)
            row.pairCacheBytes.assign(
                work.pairCache.ptr,
                work.pairCache.ptr + work.pairCache.size);
        if (work.pairCache.manifold)
        {
            if ((work.pairCache.manifold & 1u) != 0 ||
                work.geomType0 != PxGeometryType::eBOX ||
                work.geomType1 != PxGeometryType::eBOX)
            {
                error = "Only a single box/box PCM manifold is supported";
                return false;
            }
            const Gu::PersistentContactManifold& manifold =
                *reinterpret_cast<const Gu::PersistentContactManifold*>(
                    work.pairCache.manifold);
            if (manifold.mNumContacts > GU_MANIFOLD_CACHE_SIZE ||
                manifold.mNumWarmStartPoints > GU_MANIFOLD_CACHE_SIZE)
            {
                error = "PCM manifold contact/warm-start count is invalid";
                return false;
            }
            row.manifoldKind = 1;
            row.manifoldContactCount = manifold.mNumContacts;
            row.manifoldWarmStartCount = manifold.mNumWarmStartPoints;
            const unsigned char* transform =
                reinterpret_cast<const unsigned char*>(
                    &manifold.mRelativeTransform);
            row.manifoldTransformBytes.assign(
                transform,
                transform + sizeof(manifold.mRelativeTransform));
            const unsigned char* a = manifold.mAIndice;
            const unsigned char* b = manifold.mBIndice;
            row.manifoldIndexBytes.assign(a, a + 4);
            row.manifoldIndexBytes.insert(
                row.manifoldIndexBytes.end(), b, b + 4);
            const unsigned char* contacts =
                reinterpret_cast<const unsigned char*>(
                    manifold.mContactPoints);
            row.manifoldContactBytes.assign(
                contacts,
                contacts + manifold.mNumContacts *
                    sizeof(Gu::PersistentContact));
        }
        if (actorPair.mReportData)
        {
            row.reportDataPresent = 1;
            if (!poolSlot(nphase.mActorPairContactReportDataPool,
                          actorPair.mReportData, row.reportPoolSlot))
            {
                error = "ActorPair report data is outside its allocation pool";
                return false;
            }
            row.reportResetStamp = actorPair.mReportData->mStrmResetStamp;
            row.reportActorAId = actorPair.mReportData->mActorAID;
            row.reportActorBId = actorPair.mReportData->mActorBID;
            const unsigned char* begin = reinterpret_cast<const unsigned char*>(
                &actorPair.mReportData->mContactStreamManager);
            row.reportStreamManager.assign(
                begin, begin + sizeof(Sc::ContactStreamManager));
        }
    }
    if (!range.empty() || !mover || mover->mInteractions.size() != 6)
    {
        error = "Fixture mover interaction count is not six";
        return false;
    }
    next.moverTransferringCount = mover->mNumTransferringInteractions;
    if (next.moverTransferringCount != 0)
    {
        error = "Fixture mover has unsupported transferring interactions";
        return false;
    }
    for (PxU32 i = 0; i < 6; ++i)
    {
        Sc::Interaction* interaction = mover->mInteractions[i];
        bool found = false;
        for (PxU32 shape = 0; shape < 6; ++shape)
        {
            if (byShape[shape] == interaction)
            {
                if (interaction->getActorId(mover) != i)
                {
                    error = "Mover interaction reverse index is inconsistent";
                    return false;
                }
                next.moverActorOrder.push_back(shape);
                found = true;
                break;
            }
        }
        if (!found)
        {
            error = "Mover interaction references an unknown pair";
            return false;
        }
    }

    std::map<const Sc::ShapeInstancePairLL*, PxU32> shapeBySip;
    std::map<const Sc::ActorPair*, PxU32> shapeByActorPair;
    for (PxU32 i = 0; i < 6; ++i)
    {
        shapeBySip[byShape[i]] = i;
        shapeByActorPair[byShape[i]->getActorPair()] = i;
    }
    next.nextPersistentPair =
        nphase.mNextFramePersistentContactEventPairIndex;
    if (next.nextPersistentPair >
        nphase.mPersistentContactEventPairList.size())
    {
        error = "Persistent contact event split exceeds list length";
        return false;
    }
    for (PxU32 i = 0; i < nphase.mPersistentContactEventPairList.size(); ++i)
    {
        const auto found = shapeBySip.find(
            nphase.mPersistentContactEventPairList[i]);
        if (found == shapeBySip.end())
        {
            error = "Persistent event list has an unregistered pair";
            return false;
        }
        next.persistentEventOrder.push_back(found->second);
    }
    for (PxU32 i = 0; i < nphase.mForceThresholdContactEventPairList.size(); ++i)
    {
        const auto found = shapeBySip.find(
            nphase.mForceThresholdContactEventPairList[i]);
        if (found == shapeBySip.end())
        {
            error = "Force event list has an unregistered pair";
            return false;
        }
        next.forceThresholdEventOrder.push_back(found->second);
    }
    for (PxU32 i = 0; i < nphase.mContactReportActorPairSet.size(); ++i)
    {
        const auto found = shapeByActorPair.find(
            nphase.mContactReportActorPairSet[i]);
        if (found == shapeByActorPair.end())
        {
            error = "Report ActorPair set has an unregistered pair";
            return false;
        }
        next.reportActorPairOrder.push_back(found->second);
    }

    const auto& reportPool = nphase.mActorPairContactReportDataPool;
    next.reportPoolSlabCount = reportPool.mSlabs.size();
    next.reportPoolUsedCount = reportPool.mUsed;
    const PxU32 reportCapacity =
        reportPool.mSlabs.size() * reportPool.mElementsPerSlab;
    std::vector<bool> seenReportSlots(reportCapacity, false);
    auto* reportFree = reportPool.mFreeElement;
    while (reportFree)
    {
        PxU32 slot = 0;
        if (!poolSlot(reportPool, reportFree, slot) ||
            seenReportSlots[slot] ||
            next.reportPoolFreeOrder.size() >= reportCapacity)
        {
            error = "ActorPair report pool free chain is inconsistent";
            return false;
        }
        seenReportSlots[slot] = true;
        next.reportPoolFreeOrder.push_back(slot);
        reportFree = reportFree->mNext;
    }
    if (next.reportPoolUsedCount + next.reportPoolFreeOrder.size() !=
        reportCapacity)
    {
        error = "ActorPair report pool used/free partition is inconsistent";
        return false;
    }

    const auto& pool = interactions.getLowLevelContext()->mContactManagerPool;
    const PxsContext& context = *interactions.getLowLevelContext();
    next.managerPoolUseBitmap = captureBitmap(pool.mUseBitmap);
    next.activeManagerBitmap =
        captureBitmap(context.mActiveContactManager);
    next.modifiableManagerBitmap =
        captureBitmap(context.mModifiableContactManager);
    next.touchEventBitmap =
        captureBitmap(context.mContactManagerTouchEvent);
    next.managerSlabCount = pool.mSlabCount;
    next.managerFreeCount = pool.mFreeCount;
    const PxU32 capacity = pool.mSlabCount * pool.mEltsPerSlab;
    std::vector<bool> seen(capacity, false);
    for (PxU32 i = 0; i < pool.mFreeCount; ++i)
    {
        const PxU32 id = pool.mFreeList[i]->getIndex();
        if (id >= capacity || seen[id] || pool.mUseBitmap.boundedTest(id))
        {
            error = "Contact manager free stack is inconsistent";
            return false;
        }
        seen[id] = true;
        next.managerFreeOrder.push_back(id);
    }
    for (PxU32 id = 0; id < capacity; ++id)
        if (seen[id] == static_cast<bool>(pool.mUseBitmap.boundedTest(id)))
        {
            error = "Contact manager active/free partition is inconsistent";
            return false;
        }

    const Sc::ContactReportBuffer& reports = nphase.mContactReportBuffer;
    next.reportBufferIndex = reports.mCurrentBufferIndex;
    next.reportBufferSize = reports.mCurrentBufferSize;
    next.reportBufferDefaultSize = reports.mDefaultBufferSize;
    next.reportBufferLastIndex = reports.mLastBufferIndex;
    next.reportBufferAllocationLocked = reports.mAllocationLocked ? 1u : 0u;
    if (next.reportBufferIndex > next.reportBufferSize ||
        (next.reportBufferIndex && !reports.mBuffer))
    {
        error = "Contact report buffer cursor or backing is invalid";
        return false;
    }
    if (next.reportBufferIndex)
        next.reportBufferBytes.assign(
            reports.mBuffer, reports.mBuffer + next.reportBufferIndex);
    return true;
}

bool sixDistinct(const std::vector<std::uint32_t>& order)
{
    if (order.size() != 6) return false;
    std::set<std::uint32_t> values(order.begin(), order.end());
    return values.size() == 6 && *values.begin() == 0 &&
           *values.rbegin() == 5;
}

void appendBlocks(const PxcNpMemBlockArray& array,
                  std::set<uintptr_t>& blocks)
{
    for (PxU32 i = 0; i < array.size(); ++i)
        if (array[i])
            blocks.insert(reinterpret_cast<uintptr_t>(array[i]));
}

bool liveBlockSpan(const std::set<uintptr_t>& blocks,
                   const void* pointer, size_t size)
{
    if (!pointer) return size == 0;
    const uintptr_t address = reinterpret_cast<uintptr_t>(pointer);
    for (const uintptr_t block : blocks)
    {
        if (address >= block &&
            address < block + PxcNpMemBlock::SIZE &&
            size <= block + PxcNpMemBlock::SIZE - address)
            return true;
    }
    return false;
}

bool allocatedMemBlocks(PxsContext& context,
                        std::set<uintptr_t>& blocks,
                        std::string& error)
{
    PxcNpMemBlockPool& pool = context.mNpMemBlockPool;
    appendBlocks(pool.mConstraints, blocks);
    for (PxU32 i = 0; i < 2; ++i)
    {
        appendBlocks(pool.mContacts[i], blocks);
        appendBlocks(pool.mFriction[i], blocks);
        appendBlocks(pool.mNpCache[i], blocks);
    }
    appendBlocks(pool.mScratchBlocks, blocks);
    // Post-fetch contact streams in this fixture still point into blocks
    // retained on the allocated-but-unused LIFO list.
    appendBlocks(pool.mUnused, blocks);
    if (blocks.size() != pool.mAllocatedBlocks)
    {
        error = "NpMemBlockPool allocated-block partition is inconsistent";
        return false;
    }
    return true;
}

} // namespace

bool CaptureInteractionImage(PxScene& scene, InteractionImage& image,
                             std::string& error)
{
    InteractionImage next;
    PairPointers pairs;
    Sc::Actor* mover = NULL;
    if (!fixtureRows(scene, next, pairs, mover, error)) return false;
    image = next;
    error.clear();
    return true;
}

bool InteractionImage::sameSlotsAndOrder(
    const InteractionImage& other, std::string& firstDifference) const
{
    if (scene != other.scene || pairs.size() != other.pairs.size() ||
        pairs.size() != 6 || sceneOrder != other.sceneOrder ||
        moverActorOrder != other.moverActorOrder ||
        sceneActiveCount != other.sceneActiveCount ||
        moverTransferringCount != other.moverTransferringCount)
    {
        firstDifference = "scene or ordered interaction arrays differ";
        return false;
    }
    for (size_t i = 0; i < 6; ++i)
        if (pairs[i].moverShape != other.pairs[i].moverShape ||
            pairs[i].sipPoolSlot != other.pairs[i].sipPoolSlot ||
            pairs[i].actorPairPoolSlot != other.pairs[i].actorPairPoolSlot ||
            pairs[i].managerSlot != other.pairs[i].managerSlot)
        {
            std::ostringstream out;
            out << "pair " << i << " physical allocation differs";
            firstDifference = out.str();
            return false;
        }
    if (managerSlabCount != other.managerSlabCount ||
        managerFreeCount != other.managerFreeCount ||
        managerFreeOrder != other.managerFreeOrder)
    {
        firstDifference = "contact manager pool free stack differs";
        return false;
    }
    firstDifference.clear();
    return true;
}

bool RestoreInteractionOrder(PxScene& scene, const InteractionImage& target,
                             std::string& error)
{
    if (target.scene != reinterpret_cast<uintptr_t>(&scene) ||
        target.pairs.size() != 6 ||
        !sixDistinct(target.sceneOrder) ||
        !sixDistinct(target.moverActorOrder) ||
        target.topology.pairs.size() != 6 ||
        target.topology.activePairCount != 6)
    {
        error = "Target is not a same-scene six-pair interaction image";
        return false;
    }
    NPhaseTopologyImage empty;
    if (!CaptureNPhaseTopology(scene, empty, error)) return false;
    if (!empty.pairs.empty() || empty.activePairCount)
    {
        error = "Interaction ordering restore requires an empty NPhase successor";
        return false;
    }
    NPhaseTopologyImage reverse = target.topology;
    std::reverse(reverse.pairs.begin(), reverse.pairs.end());
    if (!RestoreNPhaseTopology(scene, reverse, error)) return false;

    InteractionImage recreated;
    if (!CaptureInteractionImage(scene, recreated, error))
    {
        error = "Lifecycle creation changed scene; dispose it: " + error;
        return false;
    }
    for (PxU32 i = 0; i < 6; ++i)
        if (recreated.pairs[i].sipPoolSlot != target.pairs[i].sipPoolSlot ||
            recreated.pairs[i].actorPairPoolSlot !=
                target.pairs[i].actorPairPoolSlot ||
            recreated.pairs[i].managerSlot != target.pairs[i].managerSlot)
        {
            error = "Lifecycle creation changed scene but physical pair slots differ; dispose it";
            return false;
        }
    if (recreated.managerSlabCount != target.managerSlabCount ||
        recreated.managerFreeCount != target.managerFreeCount ||
        recreated.managerFreeOrder != target.managerFreeOrder)
    {
        error = "Lifecycle creation changed scene but manager free stack differs; dispose it";
        return false;
    }

    NpScene& np = static_cast<NpScene&>(scene);
    Sc::InteractionScene& interactions =
        np.getScene().getScScene().getInteractionScene();
    PairPointers byShape;
    byShape.fill(NULL);
    for (size_t i = 0; i < 6; ++i)
    {
        const PxU32 shape = recreated.sceneOrder[i];
        byShape[shape] = static_cast<Sc::ShapeInstancePairLL*>(
            interactions.mInteractions[Sc::PX_INTERACTION_TYPE_OVERLAP][i]);
    }
    Sc::Actor* mover = NULL;
    if (!findMover(*byShape[0], mover) ||
        mover->mInteractions.size() != 6 ||
        mover->mNumTransferringInteractions != 0)
    {
        error = "Lifecycle creation changed mover interaction topology; dispose it";
        return false;
    }
    // Complete preflight above. These are only pointer permutations; no
    // object is constructed, destroyed, or moved to another physical slot.
    for (PxU32 i = 0; i < 6; ++i)
    {
        Sc::ShapeInstancePairLL* sip = byShape[target.sceneOrder[i]];
        interactions.mInteractions[Sc::PX_INTERACTION_TYPE_OVERLAP][i] = sip;
        sip->mSceneId = i;
    }
    for (PxU32 i = 0; i < 6; ++i)
    {
        Sc::ShapeInstancePairLL* sip = byShape[target.moverActorOrder[i]];
        mover->mInteractions[i] = sip;
        sip->setActorId(mover, i);
    }
    InteractionImage ordered;
    if (!CaptureInteractionImage(scene, ordered, error) ||
        !target.sameSlotsAndOrder(ordered, error))
    {
        error = "Interaction order changed scene but verification failed; dispose it: " + error;
        return false;
    }
    error.clear();
    return true;
}

bool RestoreInteractionMetadata(PxScene& scene,
                                const InteractionImage& target,
                                std::string& error)
{
    InteractionImage current;
    if (!CaptureInteractionImage(scene, current, error)) return false;
    if (!target.sameSlotsAndOrder(current, error))
    {
        error = "Interaction order stage is not complete: " + error;
        return false;
    }
    if (target.reportBufferIndex || current.reportBufferIndex ||
        !target.reportBufferBytes.empty() ||
        !current.reportBufferBytes.empty() ||
        target.forceThresholdEventOrder.size() ||
        target.reportActorPairOrder.size() ||
        target.persistentEventOrder.size() != 6 ||
        target.nextPersistentPair != 6 ||
        !current.persistentEventOrder.empty() ||
        !current.forceThresholdEventOrder.empty() ||
        !current.reportActorPairOrder.empty())
    {
        error = "Report/event topology is outside the zero-cursor six-persistent-pair fixture";
        return false;
    }
    if (target.reportPoolSlabCount != current.reportPoolSlabCount ||
        current.reportPoolFreeOrder.size() < 6 ||
        target.reportPoolUsedCount != current.reportPoolUsedCount + 6)
    {
        error = "ActorPair report pool allocation topology changed";
        return false;
    }
    PxsContext& lowLevel = *static_cast<NpScene&>(scene).getScene()
        .getScScene().getInteractionScene().getLowLevelContext();
    if (!bitmapStorageSame(lowLevel.mContactManagerPool.mUseBitmap,
                           target.managerPoolUseBitmap) ||
        !bitmapStorageSame(lowLevel.mActiveContactManager,
                           target.activeManagerBitmap) ||
        !bitmapStorageSame(lowLevel.mModifiableContactManager,
                           target.modifiableManagerBitmap) ||
        !bitmapStorageSame(lowLevel.mContactManagerTouchEvent,
                           target.touchEventBitmap) ||
        current.managerPoolUseBitmap.words !=
            target.managerPoolUseBitmap.words)
    {
        error = "Contact-manager bitmap allocation or pool use changed";
        return false;
    }
    std::map<PxU32, PxU32> shapeByReportSlot;
    for (PxU32 shape = 0; shape < 6; ++shape)
    {
        if (!target.pairs[shape].reportDataPresent ||
            current.pairs[shape].reportDataPresent ||
            target.pairs[shape].reportStreamManager.size() !=
                sizeof(Sc::ContactStreamManager) ||
            !shapeByReportSlot.insert(std::make_pair(
                target.pairs[shape].reportPoolSlot, shape)).second)
        {
            error = "Target or live report object ownership is unsupported";
            return false;
        }
    }
    if (shapeByReportSlot.size() != 6)
    {
        error = "Target report slots are not distinct";
        return false;
    }
    std::set<PxU32> eventShapes(target.persistentEventOrder.begin(),
                                 target.persistentEventOrder.end());
    if (eventShapes.size() != 6 || *eventShapes.begin() != 0 ||
        *eventShapes.rbegin() != 5)
    {
        error = "Target persistent event list is not a six-pair permutation";
        return false;
    }

    NpScene& np = static_cast<NpScene&>(scene);
    Sc::Scene& sc = np.getScene().getScScene();
    Sc::NPhaseCore& nphase = *sc.getNPhaseCore();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    PairPointers byShape;
    byShape.fill(NULL);
    for (PxU32 i = 0; i < 6; ++i)
        byShape[current.sceneOrder[i]] =
            static_cast<Sc::ShapeInstancePairLL*>(
                interactions.mInteractions[Sc::PX_INTERACTION_TYPE_OVERLAP][i]);

    void* creationOrder[6];
    for (PxU32 i = 0; i < 6; ++i)
    {
        const auto found = shapeByReportSlot.find(
            current.reportPoolFreeOrder[i]);
        if (found == shapeByReportSlot.end())
        {
            error = "Next report pool slots cannot be assigned to target pairs";
            return false;
        }
        creationOrder[i] = byShape[found->second]->getActorPair();
    }
    HMODULE physxDll = GetModuleHandleA("PhysX3_x86.dll");
    FARPROC exported = physxDll ? GetProcAddress(
        physxDll, "oc2_physx333_report_create_v1") : NULL;
    if (!exported && physxDll)
        exported = GetProcAddress(
            physxDll, "_oc2_physx333_report_create_v1");
    if (!exported)
    {
        error = "Test-only report lifecycle bridge is not installed";
        return false;
    }
    const InteractionReportCreateFnV1 create =
        reinterpret_cast<InteractionReportCreateFnV1>(exported);
    const PxU32 result = create(&nphase, creationOrder, 6);
    if (result != InteractionReportBridgeSuccess)
    {
        std::ostringstream out;
        out << "Report bridge changed scene or rejected it (code "
            << result << "); dispose it";
        error = out.str();
        return false;
    }
    if (!CaptureInteractionImage(scene, current, error))
    {
        error = "Report lifecycle changed scene; dispose it: " + error;
        return false;
    }
    if (target.reportPoolSlabCount != current.reportPoolSlabCount ||
        target.reportPoolUsedCount != current.reportPoolUsedCount ||
        target.reportPoolFreeOrder != current.reportPoolFreeOrder)
    {
        error = "Report lifecycle changed scene but pool free order differs; dispose it";
        return false;
    }
    for (PxU32 shape = 0; shape < 6; ++shape)
        if (!current.pairs[shape].reportDataPresent ||
            current.pairs[shape].reportPoolSlot !=
                target.pairs[shape].reportPoolSlot)
        {
            error = "Report lifecycle changed scene but pair binding differs; dispose it";
            return false;
        }

    // The fixture's report buffer cursor is zero. Preserve its allocation,
    // and reconstruct the ordered persistent list and payload of its six
    // already-live report objects. All writes below have fixed capacities.
    nphase.mPersistentContactEventPairList.clear();
    for (PxU32 i = 0; i < 6; ++i)
        nphase.mPersistentContactEventPairList.pushBack(
            byShape[target.persistentEventOrder[i]]);
    nphase.mNextFramePersistentContactEventPairIndex =
        target.nextPersistentPair;
    for (PxU32 shape = 0; shape < 6; ++shape)
    {
        const InteractionPairImage& source = target.pairs[shape];
        Sc::ShapeInstancePairLL& sip = *byShape[shape];
        Sc::ActorPair& actorPair = *sip.getActorPair();
        Sc::ActorPairContactReportData& report = *actorPair.mReportData;
        if (actorPair.mRefCount != source.actorPairRefCount ||
            report.mActorAID != source.reportActorAId ||
            report.mActorBID != source.reportActorBId)
        {
            error = "Report lifecycle changed scene but actor identity differs; dispose it";
            return false;
        }
        sip.mFlags = source.sipFlags;
        sip.mContactReportStamp = source.contactReportStamp;
        sip.mReportPairIndex = source.reportPairIndex;
        sip.mReportStreamIndex =
            static_cast<PxU16>(source.reportStreamIndex);
        actorPair.mInternalFlags =
            static_cast<PxU16>(source.actorPairFlags);
        actorPair.mTouchCount =
            static_cast<PxU16>(source.actorPairTouchCount);
        report.mStrmResetStamp = source.reportResetStamp;
        std::memcpy(&report.mContactStreamManager,
                    source.reportStreamManager.data(),
                    source.reportStreamManager.size());
        sip.mManager->mFlags = source.managerFlags;
        sip.mManager->getWorkUnit().statusFlags =
            static_cast<PxU16>(source.managerStatusFlags);
    }
    Sc::ContactReportBuffer& reports = nphase.mContactReportBuffer;
    if (reports.mCurrentBufferSize != target.reportBufferSize ||
        reports.mDefaultBufferSize != target.reportBufferDefaultSize ||
        static_cast<PxU32>(reports.mAllocationLocked) !=
            target.reportBufferAllocationLocked)
    {
        error = "Report buffer allocation topology differs; dispose it";
        return false;
    }
    reports.mLastBufferIndex = target.reportBufferLastIndex;
    writeBitmap(lowLevel.mActiveContactManager,
                target.activeManagerBitmap);
    writeBitmap(lowLevel.mModifiableContactManager,
                target.modifiableManagerBitmap);
    writeBitmap(lowLevel.mContactManagerTouchEvent,
                target.touchEventBitmap);

    InteractionImage restored;
    if (!CaptureInteractionImage(scene, restored, error))
    {
        error = "Interaction metadata changed scene; dispose it: " + error;
        return false;
    }
    if (restored.persistentEventOrder != target.persistentEventOrder ||
        restored.nextPersistentPair != target.nextPersistentPair ||
        restored.reportBufferLastIndex != target.reportBufferLastIndex)
    {
        error = "Interaction metadata readback differs; dispose it";
        return false;
    }
    if (restored.managerPoolUseBitmap.words !=
            target.managerPoolUseBitmap.words ||
        restored.activeManagerBitmap.words !=
            target.activeManagerBitmap.words ||
        restored.modifiableManagerBitmap.words !=
            target.modifiableManagerBitmap.words ||
        restored.touchEventBitmap.words !=
            target.touchEventBitmap.words)
    {
        error = "Contact-manager bitmap readback differs; dispose it";
        return false;
    }
    for (PxU32 shape = 0; shape < 6; ++shape)
    {
        const InteractionPairImage& a = restored.pairs[shape];
        const InteractionPairImage& b = target.pairs[shape];
        if (a.sipFlags != b.sipFlags ||
            a.contactReportStamp != b.contactReportStamp ||
            a.reportPairIndex != b.reportPairIndex ||
            a.reportStreamIndex != b.reportStreamIndex ||
            a.actorPairFlags != b.actorPairFlags ||
            a.actorPairTouchCount != b.actorPairTouchCount ||
            a.actorPairRefCount != b.actorPairRefCount ||
            a.reportResetStamp != b.reportResetStamp ||
            a.reportStreamManager != b.reportStreamManager ||
            a.managerFlags != b.managerFlags ||
            a.managerStatusFlags != b.managerStatusFlags)
        {
            error = "Interaction pair metadata readback differs; dispose it";
            return false;
        }
    }
    error.clear();
    return true;
}

static bool restoreInteractionContactPayload(PxScene& scene,
                                             const InteractionImage& target,
                                             std::string& error,
                                             bool requireBackingBytes)
{
    InteractionImage current;
    if (!CaptureInteractionImage(scene, current, error)) return false;
    if (!target.sameSlotsAndOrder(current, error) ||
        target.persistentEventOrder != current.persistentEventOrder ||
        target.nextPersistentPair != current.nextPersistentPair ||
        target.reportPoolFreeOrder != current.reportPoolFreeOrder)
    {
        error = "Interaction metadata stage is not complete: " + error;
        return false;
    }

    NpScene& np = static_cast<NpScene&>(scene);
    Sc::InteractionScene& interactions =
        np.getScene().getScScene().getInteractionScene();
    PxsContext& context = *interactions.getLowLevelContext();
    std::set<uintptr_t> blocks;
    if (!allocatedMemBlocks(context, blocks, error)) return false;

    PairPointers byShape;
    byShape.fill(NULL);
    for (PxU32 i = 0; i < 6; ++i)
        byShape[current.sceneOrder[i]] =
            static_cast<Sc::ShapeInstancePairLL*>(
                interactions.mInteractions[Sc::PX_INTERACTION_TYPE_OVERLAP][i]);

    struct RestorePlan {
        PxsContactManager* manager;
        PxcNpWorkUnit work;
        Gu::PersistentContactManifold* manifold;
    };
    RestorePlan plan[6];
    for (PxU32 shape = 0; shape < 6; ++shape)
    {
        const InteractionPairImage& source = target.pairs[shape];
        const InteractionPairImage& liveRow = current.pairs[shape];
        PxsContactManager& cm = *byShape[shape]->mManager;
        const PxcNpWorkUnit& live = cm.getWorkUnit();
        if (source.workUnitBytes.size() != sizeof(PxcNpWorkUnit) ||
            source.managerStatusFlags != liveRow.managerStatusFlags ||
            source.sipFlags != liveRow.sipFlags ||
            source.actorPairTouchCount != liveRow.actorPairTouchCount ||
            source.reportPoolSlot != liveRow.reportPoolSlot)
        {
            error = "Contact payload prerequisites or work-unit size differ";
            return false;
        }
        PxcNpWorkUnit saved;
        std::memcpy(&saved, source.workUnitBytes.data(), sizeof(saved));
        if (saved.index != source.managerSlot ||
            saved.geomType0 != PxGeometryType::eBOX ||
            saved.geomType1 != PxGeometryType::eBOX ||
            saved.rigidCore0 != live.rigidCore0 ||
            saved.rigidCore1 != live.rigidCore1 ||
            saved.shapeCore0 != live.shapeCore0 ||
            saved.shapeCore1 != live.shapeCore1 ||
            saved.materialManager != live.materialManager ||
            saved.compressedContactSize !=
                source.compressedContactBytes.size() ||
            saved.pairCache.size != source.pairCacheBytes.size() ||
            saved.statusFlags != source.managerStatusFlags ||
            saved.ccdContacts)
        {
            error = "Saved contact work unit is unsupported or owner bindings changed";
            return false;
        }
        if (!liveBlockSpan(blocks, saved.compressedContacts,
                           saved.compressedContactSize) ||
            !liveBlockSpan(blocks, saved.pairCache.ptr,
                           saved.pairCache.size) ||
            !liveBlockSpan(blocks, saved.solverConstraintPointer,
                           saved.solverConstraintPointer ?
                               std::max<PxU32>(saved.solverConstraintSize, 1u) :
                               0u) ||
            !liveBlockSpan(blocks, saved.frictionDataPtr,
                           saved.frictionDataPtr ? 1u : 0u))
        {
            error = "Saved stream, solver, or friction pointer is outside live NpMemBlockPool blocks";
            return false;
        }
        if (requireBackingBytes &&
            ((!source.compressedContactBytes.empty() &&
              std::memcmp(saved.compressedContacts,
                          source.compressedContactBytes.data(),
                          source.compressedContactBytes.size()) != 0) ||
             (!source.pairCacheBytes.empty() &&
              std::memcmp(saved.pairCache.ptr,
                          source.pairCacheBytes.data(),
                          source.pairCacheBytes.size()) != 0)))
        {
            error = "Saved contact/cache stream backing differs; restore NpMemBlockPool first";
            return false;
        }
        const uintptr_t freshManifold = live.pairCache.manifold;
        if ((source.manifoldKind == 0 &&
             (saved.pairCache.manifold || freshManifold)) ||
            (source.manifoldKind == 1 &&
             (!saved.pairCache.manifold ||
              (saved.pairCache.manifold & 15u) ||
              !freshManifold || (freshManifold & 15u) ||
              source.manifoldContactCount > GU_MANIFOLD_CACHE_SIZE ||
              source.manifoldWarmStartCount > GU_MANIFOLD_CACHE_SIZE ||
              source.manifoldTransformBytes.size() !=
                  sizeof(Gu::PersistentContactManifold::mRelativeTransform) ||
              source.manifoldIndexBytes.size() != 8 ||
              source.manifoldContactBytes.size() !=
                  source.manifoldContactCount *
                      sizeof(Gu::PersistentContact))) ||
            source.manifoldKind > 1)
        {
            error = "Single box/box PCM manifold allocation or image is invalid";
            return false;
        }
        plan[shape].manager = &cm;
        plan[shape].manifold = source.manifoldKind ?
            reinterpret_cast<Gu::PersistentContactManifold*>(freshManifold) :
            NULL;
        saved.pairCache.manifold = freshManifold;
        plan[shape].work = saved;
    }

    // Every pointer is validated before the first write. Stage 2 also
    // verifies that the memory-block restore installed the saved bytes.
    // The manifold object was allocated by PhysX's lifecycle path. Its
    // mContactPoints self-pointer must stay bound to that new object.
    for (PxU32 shape = 0; shape < 6; ++shape)
    {
        const InteractionPairImage& source = target.pairs[shape];
        RestorePlan& item = plan[shape];
        std::memcpy(&item.manager->getWorkUnit(), &item.work,
                    sizeof(PxcNpWorkUnit));
        if (!item.manifold) continue;
        Gu::PersistentContactManifold& manifold = *item.manifold;
        std::memcpy(&manifold.mRelativeTransform,
                    source.manifoldTransformBytes.data(),
                    source.manifoldTransformBytes.size());
        manifold.mNumContacts =
            static_cast<PxU8>(source.manifoldContactCount);
        manifold.mNumWarmStartPoints =
            static_cast<PxU8>(source.manifoldWarmStartCount);
        std::memcpy(manifold.mAIndice,
                    source.manifoldIndexBytes.data(), 4);
        std::memcpy(manifold.mBIndice,
                    source.manifoldIndexBytes.data() + 4, 4);
        if (!source.manifoldContactBytes.empty())
            std::memcpy(manifold.mContactPoints,
                        source.manifoldContactBytes.data(),
                        source.manifoldContactBytes.size());
    }
    InteractionImage restored;
    if (!CaptureInteractionImage(scene, restored, error))
    {
        error = "Contact payload changed scene; dispose it: " + error;
        return false;
    }
    for (PxU32 shape = 0; shape < 6; ++shape)
    {
        const InteractionPairImage& a = target.pairs[shape];
        const InteractionPairImage& b = restored.pairs[shape];
        if (a.manifoldKind != b.manifoldKind ||
            a.manifoldContactCount != b.manifoldContactCount ||
            a.manifoldWarmStartCount != b.manifoldWarmStartCount ||
            a.manifoldTransformBytes != b.manifoldTransformBytes ||
            a.manifoldIndexBytes != b.manifoldIndexBytes ||
            a.manifoldContactBytes != b.manifoldContactBytes ||
            (requireBackingBytes &&
             (a.compressedContactBytes != b.compressedContactBytes ||
              a.pairCacheBytes != b.pairCacheBytes)) ||
            std::memcmp(&plan[shape].manager->getWorkUnit(),
                        &plan[shape].work,
                        sizeof(PxcNpWorkUnit)) != 0)
        {
            error = "Contact payload readback differs; dispose it";
            return false;
        }
    }
    error.clear();
    return true;
}

bool InstallInteractionContactBindings(PxScene& scene,
                                       const InteractionImage& target,
                                       std::string& error)
{
    return restoreInteractionContactPayload(scene, target, error, false);
}

bool RestoreInteractionContactPayload(PxScene& scene,
                                      const InteractionImage& target,
                                      std::string& error)
{
    return restoreInteractionContactPayload(scene, target, error, true);
}

} // namespace physx333_offline
