// This file is compiled only into the isolated, disposable PhysX source DLL.
// The original pinned checkout and the shared source mirror are untouched.
#define PHYSX333_TRIGGER_BRIDGE_BUILD
#include "TriggerRewindBridge.h"

#include <cstdint>
#include <cstdlib>

#define private public
#define protected public
#include "ScNPhaseCore.h"
#include "ScScene.h"
#include "ScShapeSim.h"
#include "ScShapeCore.h"
#include "ScRigidCore.h"
#include "ScRigidSim.h"
#include "ScInteractionScene.h"
#include "ScElement.h"
#include "ScTriggerInteraction.h"
#include "ScCoreInteraction.h"
#undef protected
#undef private

static_assert(sizeof(void*) == 4, "Trigger rewind bridge requires Win32");
static_assert(sizeof(physx333_offline::TriggerRewindPairV1) == 48,
              "Unexpected trigger rewind pair ABI");

namespace {
using namespace physx;
using namespace physx333_offline;

struct Resolved {
    Sc::ShapeSim* dynamicShape;
    Sc::ShapeSim* staticShape;
};

Sc::ShapeSim* findShape(Sc::RigidSim& actor, const Sc::ShapeCore& core)
{
    for (Sc::Element* element = actor.getElements_(); element;
         element = element->mNextInActor)
        if (element->getElementType() == Sc::PX_ELEMENT_TYPE_SHAPE)
        {
            Sc::ShapeSim* shape = static_cast<Sc::ShapeSim*>(element);
            if (&shape->getCore() == &core) return shape;
        }
    return NULL;
}

bool matchesFilter(Sc::Scene& scene, Sc::ShapeSim& dynamicShape,
                   Sc::ShapeSim& staticShape, PxU32 expected)
{
    const PxSimulationFilterShader shader = scene.getFilterShaderFast();
    if (!shader || scene.getFilterCallbackFast()) return false;
    PxFilterObjectAttributes attr0, attr1;
    PxFilterData data0, data1;
    dynamicShape.getFilterInfo(attr0, data0);
    staticShape.getFilterInfo(attr1, data1);
    PxPairFlags flags;
    const PxFilterFlags result = shader(attr0, data0, attr1, data1, flags,
        scene.getFilterShaderDataFast(), scene.getFilterShaderDataSizeFast());
    return static_cast<PxU32>(result) == PxFilterFlag::eDEFAULT &&
           static_cast<PxU32>(flags) == expected;
}

PxU32 physicalSlot(const Sc::NPhaseCore& nphase, const void* object)
{
    const auto& pool = nphase.mTriggerPool;
    if (pool.mSlabs.size() != 1) return 0xffffffffu;
    const std::uintptr_t first =
        reinterpret_cast<std::uintptr_t>(pool.mSlabs[0]);
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(object);
    const std::uintptr_t end = first +
        pool.mElementsPerSlab * sizeof(Sc::TriggerInteraction);
    if (address < first || address >= end ||
        (address - first) % sizeof(Sc::TriggerInteraction))
        return 0xffffffffu;
    return static_cast<PxU32>((address - first) /
                              sizeof(Sc::TriggerInteraction));
}

} // namespace

extern "C" OC2_TRIGGER_BRIDGE_API std::uint32_t __cdecl
oc2_physx333_trigger_recreate_v1(void* nphaseCore,
    const TriggerRewindPairV1* pairs, std::uint32_t pairCount)
{
    if (!nphaseCore || !pairs || pairCount != 2)
        return TriggerRewindInvalidInput;
    Sc::NPhaseCore& nphase = *static_cast<Sc::NPhaseCore*>(nphaseCore);
    Sc::Scene& scene = nphase.getScene();
    Sc::InteractionScene& interactions = scene.getInteractionScene();
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER) ||
        scene.getFilterCallbackFast())
        return TriggerRewindUnsupportedScene;

    auto& pool = nphase.mTriggerPool;
    if (pool.mSlabs.size() != 1 || pool.mElementsPerSlab != 32 ||
        pool.mUsed != 0 || pool.mUnReleasedFree != 32 ||
        !pool.mFreeElement)
        return TriggerRewindPoolMismatch;
    // Validate the entire chain before rearranging its two head nodes. A
    // different pool topology could make a guessed next pointer destructive.
    auto* node = pool.mFreeElement;
    decltype(pool.mFreeElement) nodes[32];
    bool seen[32] = {};
    for (PxU32 i = 0; i < 32; ++i)
    {
        if (!node) return TriggerRewindPoolMismatch;
        const PxU32 slot = physicalSlot(nphase, node);
        if (slot >= 32 || seen[slot]) return TriggerRewindPoolMismatch;
        seen[slot] = true;
        nodes[i] = node;
        node = node->mNext;
    }
    if (node) return TriggerRewindPoolMismatch;

    Resolved resolved[2];
    for (PxU32 i = 0; i < 2; ++i)
    {
        const TriggerRewindPairV1& pair = pairs[i];
        if (!pair.dynamicActorCore || !pair.dynamicShapeCore ||
            !pair.staticActorCore || !pair.staticShapeCore ||
            pair.dynamicActorCore == pair.staticActorCore ||
            pair.targetPoolSlot >= 32 || pair.targetLastTouch != 1 ||
            pair.targetCacheState != Gu::TRIGGER_DISJOINT ||
            pair.targetCoreFlags !=
                Sc::CoreInteraction::IS_ELEMENT_INTERACTION ||
            pair.targetDirtyFlags != Sc::CoreInteraction::CIF_DIRTY_ALL ||
            pair.targetInteractionFlags !=
                (Sc::PX_INTERACTION_FLAG_RB_ELEMENT |
                 Sc::PX_INTERACTION_FLAG_FILTERABLE) ||
            pair.targetTriggerFlags !=
                static_cast<PxU32>(PxPairFlag::eNOTIFY_TOUCH_FOUND |
                                   PxPairFlag::eNOTIFY_TOUCH_LOST) ||
            pair.expectedPairFlags !=
                static_cast<PxU32>(PxPairFlag::eTRIGGER_DEFAULT))
            return TriggerRewindInvalidInput;
        Sc::RigidSim* dynamicActor =
            static_cast<Sc::RigidCore*>(pair.dynamicActorCore)->getSim();
        Sc::RigidSim* staticActor =
            static_cast<Sc::RigidCore*>(pair.staticActorCore)->getSim();
        if (!dynamicActor || !staticActor ||
            &dynamicActor->getScene() != &scene ||
            &staticActor->getScene() != &scene ||
            dynamicActor->getActorType() != PxActorType::eRIGID_DYNAMIC ||
            staticActor->getActorType() != PxActorType::eRIGID_STATIC)
            return TriggerRewindUnresolvedShape;
        Sc::ShapeSim* dynamicShape = findShape(*dynamicActor,
            *static_cast<Sc::ShapeCore*>(pair.dynamicShapeCore));
        Sc::ShapeSim* staticShape = findShape(*staticActor,
            *static_cast<Sc::ShapeCore*>(pair.staticShapeCore));
        if (!dynamicShape || !staticShape ||
            !dynamicShape->hasAABBMgrHandle() ||
            !staticShape->hasAABBMgrHandle() ||
            dynamicShape->getGeometryType() != PxGeometryType::eCAPSULE ||
            staticShape->getGeometryType() != PxGeometryType::eBOX ||
            !(dynamicShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
            (dynamicShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
            !(staticShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
            (staticShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE))
            return TriggerRewindUnsupportedScene;
        if (!matchesFilter(scene, *dynamicShape, *staticShape,
                           pair.expectedPairFlags))
            return TriggerRewindUnexpectedFilter;
        resolved[i].dynamicShape = dynamicShape;
        resolved[i].staticShape = staticShape;
    }
    const PxU32 freeHeadSlot0 = physicalSlot(nphase, nodes[0]);
    const PxU32 freeHeadSlot1 = physicalSlot(nphase, nodes[1]);
    if (resolved[0].dynamicShape != resolved[1].dynamicShape ||
        resolved[0].staticShape == resolved[1].staticShape ||
        (freeHeadSlot0 != pairs[0].targetPoolSlot &&
         freeHeadSlot1 != pairs[0].targetPoolSlot) ||
        (freeHeadSlot0 != pairs[1].targetPoolSlot &&
         freeHeadSlot1 != pairs[1].targetPoolSlot) ||
        pairs[0].targetPoolSlot == pairs[1].targetPoolSlot)
        return TriggerRewindPoolMismatch;

    // Beginning of the write phase. Only the two just-freed head nodes are
    // permuted; the untouched free tail remains in the same order.
    auto* first = physicalSlot(nphase, nodes[0]) ==
        pairs[0].targetPoolSlot ? nodes[0] : nodes[1];
    auto* second = first == nodes[0] ? nodes[1] : nodes[0];
    first->mNext = second;
    second->mNext = nodes[2];
    pool.mFreeElement = first;

    for (PxU32 i = 0; i < 2; ++i)
    {
        nphase.onOverlapCreated(resolved[i].dynamicShape,
                                resolved[i].staticShape, 0);
        if (interactions.getInteractionCount(
                Sc::PX_INTERACTION_TYPE_TRIGGER) != i + 1)
            std::abort();
        Sc::Interaction* created = interactions.mInteractions[
            Sc::PX_INTERACTION_TYPE_TRIGGER][i];
        if (!created || created->getType() !=
                Sc::PX_INTERACTION_TYPE_TRIGGER)
            std::abort();
        Sc::TriggerInteraction* trigger =
            static_cast<Sc::TriggerInteraction*>(created);
        if (physicalSlot(nphase, trigger) != pairs[i].targetPoolSlot ||
            &trigger->getShape0() != resolved[i].staticShape ||
            &trigger->getShape1() != resolved[i].dynamicShape ||
            trigger->getInteractionFlags() !=
                pairs[i].targetInteractionFlags)
            std::abort();
        trigger->Sc::TriggerInteraction::mFlags =
            static_cast<PxU16>(pairs[i].targetTriggerFlags);
        trigger->mLastFrameHadContacts = true;
        trigger->mTriggerCache.state = Gu::TRIGGER_DISJOINT;
        trigger->Sc::CoreInteraction::mFlags =
            static_cast<PxU16>(pairs[i].targetCoreFlags);
        trigger->Sc::CoreInteraction::mDirtyFlags =
            static_cast<PxU16>(pairs[i].targetDirtyFlags);
    }
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) != 2 ||
        pool.mUsed != 2)
        std::abort();
    return TriggerRewindSuccess;
}
