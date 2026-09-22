#include "NPhaseTopology.h"
#include "NPhaseBridge.h"

#include <algorithm>
#include <cstring>
#include <limits>
#include <map>
#include <set>
#include <sstream>
#include <utility>
#include <windows.h>

#include "PxPhysicsAPI.h"

// Private inspection is confined to this source-only offline experiment.
#define private public
#define protected public
#include "NpScene.h"
#include "NpShape.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScNPhaseCore.h"
#include "ScShapeInstancePairLL.h"
#include "ScInteractionScene.h"
#include "ScScene.h"
#include "ScActor.h"
#include "PxsContext.h"
#include "PxsContactManager.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "NPhase topology requires Win32 PhysX");

struct ShapeKeyLess {
    bool operator()(const NPhaseShapeKey& a, const NPhaseShapeKey& b) const
    {
        return a.actorId < b.actorId ||
               (a.actorId == b.actorId && a.shapeIndex < b.shapeIndex);
    }
};

struct ShapeBinding {
    PxShape* shape;
    void* actorCore;
    void* shapeCore;
};

typedef std::map<NPhaseShapeKey, ShapeBinding, ShapeKeyLess> ShapeByKey;
typedef std::map<const Sc::ShapeCore*, NPhaseShapeKey> KeyByShape;

struct FixtureSize {
    PxU32 contacts = 0;
    PxU32 moverId = 0;
};

bool fixtureShapes(PxScene& scene, ShapeByKey& byKey,
                   KeyByShape& byShape, FixtureSize& size,
                   std::string& error)
{
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    if (count != 7 && count != 13)
    {
        error = "NPhase topology requires the seven- or thirteen-actor fixture";
        return false;
    }
    size.contacts = count - 1;
    size.moverId = count;
    std::vector<PxActor*> actors(count);
    if (scene.getActors(flags, &actors[0], count) != count)
    {
        error = "Actor enumeration changed during NPhase capture";
        return false;
    }
    std::set<std::uint32_t> seenActors;
    for (PxU32 i = 0; i < count; ++i)
    {
        PxActor* actor = actors[i];
        const uintptr_t rawId = reinterpret_cast<uintptr_t>(actor->userData);
        if (!rawId || rawId > size.moverId ||
            !seenActors.insert(static_cast<std::uint32_t>(rawId)).second)
        {
            error = "Fixture actor IDs must be unique values 1 through mover ID";
            return false;
        }
        const PxU32 id = static_cast<PxU32>(rawId);
        if ((id == size.moverId &&
             actor->getType() != PxActorType::eRIGID_DYNAMIC) ||
            (id != size.moverId &&
             actor->getType() != PxActorType::eRIGID_STATIC))
        {
            error = "Fixture actor type does not match its ID";
            return false;
        }
        PxRigidActor& rigid = *static_cast<PxRigidActor*>(actor);
        const PxU32 shapeCount = rigid.getNbShapes();
        if (shapeCount != (id == size.moverId ? size.contacts : 1u))
        {
            error = "Fixture actor has an unexpected number of shapes";
            return false;
        }
        std::vector<PxShape*> shapes(shapeCount);
        if (rigid.getShapes(&shapes[0], shapeCount) != shapeCount)
        {
            error = "Shape enumeration changed during NPhase capture";
            return false;
        }
        void* actorCore = NULL;
        if (id == size.moverId)
            actorCore = &static_cast<NpRigidDynamic&>(rigid)
                             .getScbBodyFast().getScBody();
        else
            actorCore = &static_cast<NpRigidStatic&>(rigid)
                             .getScbRigidStaticFast().getScStatic();
        for (PxU32 shapeIndex = 0; shapeIndex < shapeCount; ++shapeIndex)
        {
            PxShape* shape = shapes[shapeIndex];
            // PxShape::getActor() is null for a shareable shape even when
            // PxRigidActor::getShapes() lists its live scene attachment.
            if (!shape || shape->getGeometryType() != PxGeometryType::eBOX)
            {
                error = "Fixture shape binding changed";
                return false;
            }
            NPhaseShapeKey key = { id, shapeIndex };
            ShapeBinding binding = {
                shape,
                actorCore,
                &static_cast<NpShape&>(*shape).getScbShape().getScShape()
            };
            byKey[key] = binding;
            byShape[static_cast<Sc::ShapeCore*>(binding.shapeCore)] = key;
        }
    }
    if (seenActors.size() != count ||
        byKey.size() != 2u * size.contacts ||
        byShape.size() != 2u * size.contacts)
    {
        error = "Fixture actor or shape IDs are incomplete";
        return false;
    }
    return true;
}

bool pairKeyValid(const NPhasePairTopology& pair, const FixtureSize& size)
{
    if (pair.shape0.actorId != size.moverId ||
        pair.shape0.shapeIndex >= size.contacts ||
        pair.shape1.actorId < 1 ||
        pair.shape1.actorId > size.contacts ||
        pair.shape1.shapeIndex != 0)
        return false;
    return pair.shape0.shapeIndex + 1 == pair.shape1.actorId;
}

template <class T, class Alloc>
bool poolSlot(const Ps::Pool<T, Alloc>& pool, const void* pointer,
              std::uint32_t& slot)
{
    const uintptr_t address = reinterpret_cast<uintptr_t>(pointer);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const uintptr_t first =
            reinterpret_cast<uintptr_t>(pool.mSlabs[slab]);
        const uintptr_t end = first + pool.mElementsPerSlab * sizeof(T);
        if (address >= first && address < end &&
            (address - first) % sizeof(T) == 0)
        {
            slot = slab * pool.mElementsPerSlab +
                static_cast<std::uint32_t>((address - first) / sizeof(T));
            return true;
        }
    }
    return false;
}

std::string bridgeError(std::uint32_t result)
{
    switch (result)
    {
    case NPhaseBridgeInvalidInput: return "bridge rejected invalid input";
    case NPhaseBridgeUnsupportedScene: return "bridge rejected unsupported scene";
    case NPhaseBridgeUnresolvedShape: return "bridge could not resolve a live shape";
    case NPhaseBridgeUnexpectedFilter: return "bridge filter result differed from checkpoint";
    case NPhaseBridgeExistingInteraction: return "bridge found an existing or duplicate interaction";
    default:
    {
        std::ostringstream out;
        out << "bridge returned unknown result " << result;
        return out.str();
    }
    }
}

} // namespace

bool NPhaseShapeKey::operator==(const NPhaseShapeKey& other) const
{
    return actorId == other.actorId && shapeIndex == other.shapeIndex;
}

bool NPhasePairTopology::operator==(const NPhasePairTopology& other) const
{
    return shape0 == other.shape0 && shape1 == other.shape1 &&
           pairFlags == other.pairFlags && hasTouch == other.hasTouch &&
           hasKnownTouch == other.hasKnownTouch &&
           hasManager == other.hasManager &&
           actorPairRefCount == other.actorPairRefCount &&
           actorPairTouchCount == other.actorPairTouchCount &&
           sipPoolSlot == other.sipPoolSlot &&
           managerSlot == other.managerSlot &&
           islandEdge == other.islandEdge;
}

bool NPhaseTopologyImage::equals(const NPhaseTopologyImage& other,
                                 std::string& firstDifference) const
{
    if (activePairCount != other.activePairCount)
    {
        firstDifference = "active shape-pair count differs";
        return false;
    }
    if (pairs.size() != other.pairs.size())
    {
        firstDifference = "shape-pair count differs";
        return false;
    }
    for (size_t i = 0; i < pairs.size(); ++i)
        if (!(pairs[i] == other.pairs[i]))
        {
            std::ostringstream out;
            out << "shape-pair row " << i << " differs";
            firstDifference = out.str();
            return false;
        }
    firstDifference.clear();
    return true;
}

bool NPhaseTopologyImage::sameShapePairs(
    const NPhaseTopologyImage& other,
    std::string& firstDifference) const
{
    if (activePairCount != other.activePairCount ||
        pairs.size() != other.pairs.size())
    {
        firstDifference = "shape-pair count or active count differs";
        return false;
    }
    for (size_t i = 0; i < pairs.size(); ++i)
    {
        const NPhasePairTopology& a = pairs[i];
        const NPhasePairTopology& b = other.pairs[i];
        if (!(a.shape0 == b.shape0) || !(a.shape1 == b.shape1) ||
            a.pairFlags != b.pairFlags)
        {
            std::ostringstream out;
            out << "ordered shape-pair row " << i << " differs";
            firstDifference = out.str();
            return false;
        }
    }
    firstDifference.clear();
    return true;
}

bool CaptureNPhaseTopology(PxScene& scene, NPhaseTopologyImage& image,
                           std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.mIsBuffering)
    {
        error = "NPhase capture requires a completed fetchResults boundary";
        return false;
    }
    ShapeByKey byKey;
    KeyByShape byShape;
    FixtureSize fixture;
    if (!fixtureShapes(scene, byKey, byShape, fixture, error)) return false;

    Sc::Scene& sc = np.getScene().getScScene();
    Sc::NPhaseCore& nphase = *sc.getNPhaseCore();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER))
    {
        error = "Trigger or marker interactions are unsupported";
        return false;
    }
    NPhaseTopologyImage next;
    next.activePairCount = interactions.getActiveInteractionCount(
        Sc::PX_INTERACTION_TYPE_OVERLAP);
    Cm::Range<Sc::Interaction*const> range = interactions.getInteractions(
        Sc::PX_INTERACTION_TYPE_OVERLAP);
    while (!range.empty())
    {
        Sc::ShapeInstancePairLL& sip =
            *static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        const KeyByShape::const_iterator shape0 =
            byShape.find(&sip.getShape0().getCore());
        const KeyByShape::const_iterator shape1 =
            byShape.find(&sip.getShape1().getCore());
        if (shape0 == byShape.end() || shape1 == byShape.end())
        {
            error = "NPhase interaction references an unbound fixture shape";
            return false;
        }
        NPhasePairTopology row;
        row.shape0 = shape0->second;
        row.shape1 = shape1->second;
        row.pairFlags = sip.getPairFlags();
        row.hasTouch = sip.hasTouch() ? 1u : 0u;
        row.hasKnownTouch = sip.hasKnownTouchState() ? 1u : 0u;
        row.hasManager = sip.mManager ? 1u : 0u;
        row.actorPairRefCount = sip.getActorPair()->getRefCount();
        row.actorPairTouchCount = sip.getActorPair()->getTouchCount();
        if (!poolSlot(nphase.mLLSipPool, &sip, row.sipPoolSlot))
        {
            error = "SIP is outside its allocation pool";
            return false;
        }
        row.managerSlot = sip.mManager
            ? sip.mManager->getIndex() : 0xffffffffu;
        std::memcpy(&row.islandEdge, &sip.mLLIslandHook,
                    sizeof(row.islandEdge));
        if (!pairKeyValid(row, fixture))
        {
            error = "NPhase interaction does not match the box fixture";
            return false;
        }
        next.pairs.push_back(row);
    }
    if (next.pairs.size() > fixture.contacts ||
        next.activePairCount > next.pairs.size())
    {
        error = "NPhase interaction counts exceed fixture bounds";
        return false;
    }
    image = next;
    error.clear();
    return true;
}

bool RestoreNPhaseTopology(PxScene& scene,
                           const NPhaseTopologyImage& target,
                           std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.mIsBuffering)
    {
        error = "NPhase restore requires a completed fetchResults boundary";
        return false;
    }
    if (target.pairs.size() != 6 || target.activePairCount != 6)
    {
        error = "Only the six-active-pair predecessor is supported";
        return false;
    }
    NPhaseTopologyImage current;
    if (!CaptureNPhaseTopology(scene, current, error)) return false;
    if (!current.pairs.empty() || current.activePairCount)
    {
        error = "NPhase restore requires the empty deletion successor";
        return false;
    }
    ShapeByKey byKey;
    KeyByShape byShape;
    FixtureSize fixture;
    if (!fixtureShapes(scene, byKey, byShape, fixture, error)) return false;
    if (fixture.contacts != 6)
    {
        error = "Legacy six-pair restore requires the six-box fixture";
        return false;
    }
    std::set<std::uint32_t> staticIds;
    NPhaseBridgePairV1 bridgePairs[6];
    for (size_t i = 0; i < target.pairs.size(); ++i)
    {
        const NPhasePairTopology& pair = target.pairs[i];
        if (!pairKeyValid(pair, fixture) ||
            !staticIds.insert(pair.shape1.actorId).second ||
            !pair.hasTouch || !pair.hasKnownTouch || !pair.hasManager)
        {
            error = "Target is not the supported six-touch predecessor";
            return false;
        }
        const ShapeByKey::const_iterator shape0 = byKey.find(pair.shape0);
        const ShapeByKey::const_iterator shape1 = byKey.find(pair.shape1);
        if (shape0 == byKey.end() || shape1 == byKey.end())
        {
            error = "Target shape identity is absent from the live scene";
            return false;
        }
        bridgePairs[i].actorCore0 = shape0->second.actorCore;
        bridgePairs[i].shapeCore0 = shape0->second.shapeCore;
        bridgePairs[i].actorCore1 = shape1->second.actorCore;
        bridgePairs[i].shapeCore1 = shape1->second.shapeCore;
        bridgePairs[i].expectedPairFlags = pair.pairFlags;
    }
    if (staticIds.size() != 6)
    {
        error = "Target omits at least one of the six static contacts";
        return false;
    }

    HMODULE physxDll = GetModuleHandleA("PhysX3_x86.dll");
    if (!physxDll)
    {
        error = "Pinned PhysX3_x86.dll is not loaded";
        return false;
    }
    FARPROC exported = GetProcAddress(
        physxDll, "oc2_physx333_nphase_recreate_v1");
    if (!exported)
        exported = GetProcAddress(
            physxDll, "_oc2_physx333_nphase_recreate_v1");
    if (!exported)
    {
        error = "Test-only NPhase lifecycle bridge is not installed";
        return false;
    }
    const NPhaseRecreateFnV1 recreate =
        reinterpret_cast<NPhaseRecreateFnV1>(exported);
    Sc::NPhaseCore* nphase = np.getScene().getScScene().getNPhaseCore();
    const std::uint32_t result = recreate(nphase, bridgePairs, 6);
    if (result != NPhaseBridgeSuccess)
    {
        error = bridgeError(result);
        return false;
    }
    NPhaseTopologyImage recreated;
    if (!CaptureNPhaseTopology(scene, recreated, error))
    {
        error = "Lifecycle creation changed scene; dispose it: " + error;
        return false;
    }
    if (recreated.pairs.size() != 6 || recreated.activePairCount != 6)
    {
        error = "Lifecycle creation changed scene but did not create six active pairs; dispose it";
        return false;
    }
    for (size_t i = 0; i < 6; ++i)
    {
        if (!(recreated.pairs[i].shape0 == target.pairs[i].shape0) ||
            !(recreated.pairs[i].shape1 == target.pairs[i].shape1) ||
            recreated.pairs[i].pairFlags != target.pairs[i].pairFlags ||
            recreated.pairs[i].hasManager != target.pairs[i].hasManager ||
            recreated.pairs[i].actorPairRefCount !=
                target.pairs[i].actorPairRefCount)
        {
            error = "Lifecycle creation changed scene but pair/manager topology differs; dispose it";
            return false;
        }
    }
    error.clear();
    return true;
}

bool RestoreNPhaseSubset(PxScene& scene,
                          const NPhaseTopologyImage& target,
                          std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.mIsBuffering)
    {
        error = "NPhase subset restore requires a completed fetchResults";
        return false;
    }
    ShapeByKey byKey;
    KeyByShape byShape;
    FixtureSize fixture;
    if (!fixtureShapes(scene, byKey, byShape, fixture, error)) return false;
    NPhaseTopologyImage current;
    if (!CaptureNPhaseTopology(scene, current, error)) return false;
    if (target.pairs.size() != fixture.contacts ||
        target.activePairCount != target.pairs.size() ||
        current.pairs.size() >= target.pairs.size() ||
        current.activePairCount != current.pairs.size())
    {
        error = "Target/current pair counts are outside the active subset fixture";
        return false;
    }

    Sc::Scene& sc = np.getScene().getScScene();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    Sc::NPhaseCore& nphase = *sc.getNPhaseCore();
    PxsContext* context = interactions.getLowLevelContext();
    if (!context || interactions.getInteractionCount(
            Sc::PX_INTERACTION_TYPE_OVERLAP) != current.pairs.size() ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER))
    {
        error = "Subset scene interaction state is unsupported";
        return false;
    }

    std::vector<int> targetRow(fixture.contacts, -1);
    for (size_t i = 0; i < target.pairs.size(); ++i)
    {
        const NPhasePairTopology& row = target.pairs[i];
        if (!pairKeyValid(row, fixture) || !row.hasKnownTouch ||
            !row.hasManager ||
            row.sipPoolSlot == 0xffffffffu ||
            row.managerSlot == 0xffffffffu ||
            targetRow[row.shape0.shapeIndex] >= 0)
        {
            error = "Target contains an invalid or duplicate box pair";
            return false;
        }
        targetRow[row.shape0.shapeIndex] = static_cast<int>(i);
    }

    // Save exact survivor object identities before invoking the source
    // lifecycle. A pool slot alone cannot prove an object was preserved.
    std::vector<Sc::ShapeInstancePairLL*> survivors(fixture.contacts, NULL);
    Cm::Range<Sc::Interaction*const> live = interactions.getInteractions(
        Sc::PX_INTERACTION_TYPE_OVERLAP);
    while (!live.empty())
    {
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(live.front());
        live.popFront();
        const KeyByShape::const_iterator key =
            byShape.find(&sip->getShape0().getCore());
        if (key == byShape.end() || key->second.actorId != fixture.moverId ||
            key->second.shapeIndex >= fixture.contacts ||
            survivors[key->second.shapeIndex])
        {
            error = "Existing interactions are outside the subset fixture";
            return false;
        }
        const PxU32 shape = key->second.shapeIndex;
        survivors[shape] = sip;
        const NPhasePairTopology& expected =
            target.pairs[targetRow[shape]];
        std::uint32_t slot = 0;
        if (!poolSlot(nphase.mLLSipPool, sip, slot) ||
            slot != expected.sipPoolSlot || !sip->mManager ||
            sip->mManager->getIndex() != expected.managerSlot ||
            sip->getPairFlags() != expected.pairFlags)
        {
            error = "Survivor SIP/manager binding differs from checkpoint";
            return false;
        }
    }
    PxU32 survivorCount = 0;
    for (Sc::ShapeInstancePairLL* sip : survivors)
        if (sip) ++survivorCount;
    if (survivorCount != current.pairs.size())
    {
        error = "Survivor interaction count is inconsistent";
        return false;
    }

    Sc::Actor* mover = NULL;
    for (Sc::ShapeInstancePairLL* sip : survivors)
    {
        if (!sip) continue;
        Sc::Actor& a = sip->getActor0();
        Sc::Actor& b = sip->getActor1();
        Sc::Actor* candidate = a.getActorType() ==
            PxActorType::eRIGID_DYNAMIC ? &a : &b;
        if (candidate->getActorType() != PxActorType::eRIGID_DYNAMIC ||
            (mover && mover != candidate))
        {
            error = "Survivors do not share one dynamic actor";
            return false;
        }
        mover = candidate;
    }
    if (!mover || mover->mInteractions.size() != current.pairs.size() ||
        mover->mNumTransferringInteractions != 0)
    {
        error = "Mover interaction array is outside the subset fixture";
        return false;
    }

    const PxU32 missing = static_cast<PxU32>(
        target.pairs.size() - current.pairs.size());
    auto* nextSip = nphase.mLLSipPool.mFreeElement;
    auto& managerPool = context->mContactManagerPool;
    if (managerPool.mFreeCount < missing)
    {
        error = "Contact manager pool would grow during subset creation";
        return false;
    }
    std::vector<PxU32> createShapes;
    createShapes.reserve(missing);
    for (PxU32 step = 0; step < missing; ++step)
    {
        std::uint32_t nextSipSlot = 0;
        if (!nextSip || !poolSlot(nphase.mLLSipPool,
                                  nextSip, nextSipSlot))
        {
            error = "SIP pool would grow or its free chain is invalid";
            return false;
        }
        const PxU32 nextManagerSlot = managerPool.mFreeList[
            managerPool.mFreeCount - 1 - step]->getIndex();
        PxU32 match = fixture.contacts;
        for (PxU32 shape = 0; shape < fixture.contacts; ++shape)
        {
            if (survivors[shape] ||
                std::find(createShapes.begin(), createShapes.end(), shape) !=
                    createShapes.end()) continue;
            const NPhasePairTopology& row =
                target.pairs[targetRow[shape]];
            if (row.sipPoolSlot == nextSipSlot &&
                row.managerSlot == nextManagerSlot)
            {
                match = shape;
                break;
            }
        }
        if (match == fixture.contacts)
        {
            error = "Next SIP and manager slots cannot realize target subset";
            return false;
        }
        createShapes.push_back(match);
        nextSip = nextSip->mNext;
    }

    std::vector<NPhaseBridgePairV1> requests(missing);
    for (PxU32 i = 0; i < missing; ++i)
    {
        const NPhasePairTopology& row =
            target.pairs[targetRow[createShapes[i]]];
        const ShapeBinding& a = byKey.find(row.shape0)->second;
        const ShapeBinding& b = byKey.find(row.shape1)->second;
        requests[i].actorCore0 = a.actorCore;
        requests[i].shapeCore0 = a.shapeCore;
        requests[i].actorCore1 = b.actorCore;
        requests[i].shapeCore1 = b.shapeCore;
        requests[i].expectedPairFlags = row.pairFlags;
    }
    HMODULE physxDll = GetModuleHandleA("PhysX3_x86.dll");
    FARPROC exported = physxDll ? GetProcAddress(
        physxDll, "oc2_physx333_nphase_recreate_subset_v2") : NULL;
    if (!exported && physxDll)
        exported = GetProcAddress(
            physxDll, "_oc2_physx333_nphase_recreate_subset_v2");
    if (!exported)
    {
        error = "Test-only NPhase subset lifecycle bridge is not installed";
        return false;
    }
    const NPhaseRecreateSubsetFnV2 recreate =
        reinterpret_cast<NPhaseRecreateSubsetFnV2>(exported);
    const std::uint32_t result = recreate(
        &nphase, requests.data(), missing,
        static_cast<PxU32>(current.pairs.size()));
    if (result != NPhaseBridgeSuccess)
    {
        error = bridgeError(result);
        return false;
    }

    // From here a failure invalidates the scene. No simulation may follow.
    std::vector<Sc::ShapeInstancePairLL*> byShapePointer(
        fixture.contacts, NULL);
    Cm::Range<Sc::Interaction*const> created = interactions.getInteractions(
        Sc::PX_INTERACTION_TYPE_OVERLAP);
    while (!created.empty())
    {
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(created.front());
        created.popFront();
        const KeyByShape::const_iterator key =
            byShape.find(&sip->getShape0().getCore());
        if (key == byShape.end() || key->second.actorId != fixture.moverId ||
            key->second.shapeIndex >= fixture.contacts ||
            byShapePointer[key->second.shapeIndex])
        {
            error = "Lifecycle changed scene but pair binding is invalid; dispose it";
            return false;
        }
        byShapePointer[key->second.shapeIndex] = sip;
    }
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) !=
            target.pairs.size() ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_OVERLAP) != target.pairs.size() ||
        mover->mInteractions.size() != target.pairs.size())
    {
        error = "Lifecycle changed scene but active pair count differs; dispose it";
        return false;
    }
    for (PxU32 shape = 0; shape < fixture.contacts; ++shape)
    {
        Sc::ShapeInstancePairLL* sip = byShapePointer[shape];
        const NPhasePairTopology& row = target.pairs[targetRow[shape]];
        std::uint32_t slot = 0;
        if (!sip || (survivors[shape] && survivors[shape] != sip) ||
            !poolSlot(nphase.mLLSipPool, sip, slot) ||
            slot != row.sipPoolSlot || !sip->mManager ||
            sip->mManager->getIndex() != row.managerSlot)
        {
            error = "Lifecycle changed scene but physical pair slots differ; dispose it";
            return false;
        }
    }

    // Only pointer-array permutations follow; physical SIP/CM objects and
    // survivor identities remain untouched. Each static actor has one pair.
    for (size_t i = 0; i < target.pairs.size(); ++i)
    {
        Sc::ShapeInstancePairLL* sip =
            byShapePointer[target.pairs[i].shape0.shapeIndex];
        interactions.mInteractions[Sc::PX_INTERACTION_TYPE_OVERLAP][i] = sip;
        sip->mSceneId = static_cast<PxU32>(i);
    }
    for (size_t i = 0; i < target.pairs.size(); ++i)
    {
        Sc::ShapeInstancePairLL* sip =
            byShapePointer[target.pairs[i].shape0.shapeIndex];
        mover->mInteractions[i] = sip;
        sip->setActorId(mover, static_cast<PxU32>(i));
    }
    NPhaseTopologyImage ordered;
    if (!CaptureNPhaseTopology(scene, ordered, error) ||
        !target.sameShapePairs(ordered, error))
    {
        error = "Lifecycle changed scene but ordered topology differs; dispose it: " +
            error;
        return false;
    }
    for (size_t i = 0; i < target.pairs.size(); ++i)
        if (ordered.pairs[i].sipPoolSlot != target.pairs[i].sipPoolSlot ||
            ordered.pairs[i].managerSlot != target.pairs[i].managerSlot)
        {
            error = "Lifecycle changed scene but final SIP/CM slots differ; dispose it";
            return false;
        }
    error.clear();
    return true;
}

} // namespace physx333_offline
