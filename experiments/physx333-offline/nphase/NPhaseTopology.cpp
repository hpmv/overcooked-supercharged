#include "NPhaseTopology.h"
#include "NPhaseBridge.h"

#include <algorithm>
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

bool fixtureShapes(PxScene& scene, ShapeByKey& byKey,
                   KeyByShape& byShape, std::string& error)
{
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    if (count != 7)
    {
        error = "NPhase topology currently supports the seven-actor fixture";
        return false;
    }
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
        if (!rawId || rawId > 7 ||
            !seenActors.insert(static_cast<std::uint32_t>(rawId)).second)
        {
            error = "Fixture actor IDs must be unique values 1 through 7";
            return false;
        }
        const PxU32 id = static_cast<PxU32>(rawId);
        if ((id == 7 && actor->getType() != PxActorType::eRIGID_DYNAMIC) ||
            (id != 7 && actor->getType() != PxActorType::eRIGID_STATIC))
        {
            error = "Fixture actor type does not match its ID";
            return false;
        }
        PxRigidActor& rigid = *static_cast<PxRigidActor*>(actor);
        const PxU32 shapeCount = rigid.getNbShapes();
        if (shapeCount != (id == 7 ? 6u : 1u))
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
        if (id == 7)
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
    if (seenActors.size() != 7 || byKey.size() != 12 || byShape.size() != 12)
    {
        error = "Fixture actor or shape IDs are incomplete";
        return false;
    }
    return true;
}

bool pairKeyValid(const NPhasePairTopology& pair)
{
    if (pair.shape0.actorId != 7 || pair.shape0.shapeIndex >= 6 ||
        pair.shape1.actorId < 1 || pair.shape1.actorId > 6 ||
        pair.shape1.shapeIndex != 0)
        return false;
    return pair.shape0.shapeIndex + 1 == pair.shape1.actorId;
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
           actorPairTouchCount == other.actorPairTouchCount;
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
    if (!fixtureShapes(scene, byKey, byShape, error)) return false;

    Sc::Scene& sc = np.getScene().getScScene();
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
        if (!pairKeyValid(row))
        {
            error = "NPhase interaction does not match the six-contact fixture";
            return false;
        }
        next.pairs.push_back(row);
    }
    if (next.pairs.size() > 6 ||
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
    if (!fixtureShapes(scene, byKey, byShape, error)) return false;
    std::set<std::uint32_t> staticIds;
    NPhaseBridgePairV1 bridgePairs[6];
    for (size_t i = 0; i < target.pairs.size(); ++i)
    {
        const NPhasePairTopology& pair = target.pairs[i];
        if (!pairKeyValid(pair) || !staticIds.insert(pair.shape1.actorId).second ||
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

} // namespace physx333_offline
