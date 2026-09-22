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
#include "ScElementInteractionMarker.h"
#include "ScCoreInteraction.h"
#undef protected
#undef private

static_assert(sizeof(void*) == 4, "Trigger rewind bridge requires Win32");
static_assert(sizeof(physx333_offline::TriggerRewindPairV1) == 48,
              "Unexpected trigger rewind pair ABI");
static_assert(sizeof(physx333_offline::TriggerRewindMixedPlanV2) == 152,
              "Unexpected mixed trigger rewind plan ABI");

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

PxU32 markerSlot(const Sc::NPhaseCore& nphase, const void* object)
{
    const auto& pool = nphase.mInteractionMarkerPool;
    if (pool.mSlabs.size() != 1) return 0xffffffffu;
    const std::uintptr_t first =
        reinterpret_cast<std::uintptr_t>(pool.mSlabs[0]);
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(object);
    const std::uintptr_t end = first +
        pool.mElementsPerSlab * sizeof(Sc::ElementInteractionMarker);
    if (address < first || address >= end ||
        (address - first) % sizeof(Sc::ElementInteractionMarker))
        return 0xffffffffu;
    return static_cast<PxU32>((address - first) /
        sizeof(Sc::ElementInteractionMarker));
}

bool matchesMarkerFilter(Sc::Scene& scene, Sc::ShapeSim& dynamicShape,
                         Sc::ShapeSim& staticShape)
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
    return static_cast<PxU32>(result) == PxFilterFlag::eSUPPRESS &&
           static_cast<PxU32>(flags) == 0;
}

bool validRoles(const PxU32* roles, PxU32 size)
{
    bool seen[4] = {};
    if (size > 4) return false;
    for (PxU32 i = 0; i < size; ++i)
    {
        if (roles[i] >= size || seen[roles[i]]) return false;
        seen[roles[i]] = true;
    }
    return true;
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

extern "C" OC2_TRIGGER_BRIDGE_API std::uint32_t __cdecl
oc2_physx333_trigger_recreate_mixed_v2(void* nphaseCore,
    const TriggerRewindMixedPlanV2* plan)
{
    if (!nphaseCore || !plan ||
        !validRoles(plan->targetSceneTriggerOrder, 3) ||
        !validRoles(plan->targetDynamicActorOrder, 4))
        return TriggerRewindInvalidInput;
    Sc::NPhaseCore& nphase = *static_cast<Sc::NPhaseCore*>(nphaseCore);
    Sc::Scene& scene = nphase.getScene();
    Sc::InteractionScene& interactions = scene.getInteractionScene();
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) != 1 ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_TRIGGER) != 1 ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER) != 1 ||
        scene.getFilterCallbackFast() ||
        interactions.mInteractions[Sc::PX_INTERACTION_TYPE_TRIGGER].capacity() < 3)
        return TriggerRewindUnsupportedScene;

    auto& pool = nphase.mTriggerPool;
    if (pool.mSlabs.size() != 1 || pool.mElementsPerSlab != 32 ||
        pool.mUsed != 1 || pool.mUnReleasedFree != 31 ||
        !pool.mFreeElement || nphase.mInteractionMarkerPool.mUsed != 1)
        return TriggerRewindPoolMismatch;
    auto* node = pool.mFreeElement;
    decltype(pool.mFreeElement) nodes[31];
    bool seen[32] = {};
    for (PxU32 i = 0; i < 31; ++i)
    {
        if (!node) return TriggerRewindPoolMismatch;
        const PxU32 slot = physicalSlot(nphase, node);
        if (slot >= 32 || seen[slot]) return TriggerRewindPoolMismatch;
        seen[slot] = true;
        nodes[i] = node;
        node = node->mNext;
    }
    if (node) return TriggerRewindPoolMismatch;

    Sc::TriggerInteraction* survivor =
        static_cast<Sc::TriggerInteraction*>(
            interactions.mInteractions[Sc::PX_INTERACTION_TYPE_TRIGGER][0]);
    Sc::ElementInteractionMarker* marker =
        static_cast<Sc::ElementInteractionMarker*>(
            interactions.mInteractions[Sc::PX_INTERACTION_TYPE_MARKER][0]);
    if (!survivor || !marker ||
        survivor->getType() != Sc::PX_INTERACTION_TYPE_TRIGGER ||
        marker->getType() != Sc::PX_INTERACTION_TYPE_MARKER ||
        physicalSlot(nphase, survivor) !=
            plan->targetSurvivorTriggerPoolSlot ||
        markerSlot(nphase, marker) != plan->targetSurvivorMarkerPoolSlot ||
        !survivor->mLastFrameHadContacts ||
        survivor->mTriggerCache.state != Gu::TRIGGER_DISJOINT)
        return TriggerRewindUnsupportedScene;

    const TriggerRewindPairV1& firstPair = plan->missing[0];
    if (!firstPair.dynamicActorCore ||
        !plan->survivorTriggerStaticActorCore ||
        !plan->survivorTriggerStaticShapeCore ||
        !plan->survivorMarkerStaticActorCore ||
        !plan->survivorMarkerStaticShapeCore ||
        !plan->survivorMarkerDynamicShapeCore)
        return TriggerRewindInvalidInput;
    Sc::RigidSim* dynamicActor =
        static_cast<Sc::RigidCore*>(firstPair.dynamicActorCore)->getSim();
    Sc::RigidSim* survivorStatic =
        static_cast<Sc::RigidCore*>(
            plan->survivorTriggerStaticActorCore)->getSim();
    Sc::RigidSim* markerStatic =
        static_cast<Sc::RigidCore*>(
            plan->survivorMarkerStaticActorCore)->getSim();
    if (!dynamicActor || !survivorStatic || !markerStatic ||
        &dynamicActor->getScene() != &scene ||
        &survivorStatic->getScene() != &scene ||
        &markerStatic->getScene() != &scene ||
        dynamicActor->getActorType() != PxActorType::eRIGID_DYNAMIC ||
        survivorStatic->getActorType() != PxActorType::eRIGID_STATIC ||
        markerStatic->getActorType() != PxActorType::eRIGID_STATIC ||
        dynamicActor->mInteractions.size() != 2 ||
        dynamicActor->mInteractions.mCapacity < 4)
        return TriggerRewindUnsupportedScene;
    Sc::ShapeSim* dynamicMain = findShape(*dynamicActor,
        *static_cast<Sc::ShapeCore*>(firstPair.dynamicShapeCore));
    Sc::ShapeSim* dynamicAux = findShape(*dynamicActor,
        *static_cast<Sc::ShapeCore*>(
            plan->survivorMarkerDynamicShapeCore));
    Sc::ShapeSim* survivorStaticShape = findShape(*survivorStatic,
        *static_cast<Sc::ShapeCore*>(
            plan->survivorTriggerStaticShapeCore));
    Sc::ShapeSim* markerStaticShape = findShape(*markerStatic,
        *static_cast<Sc::ShapeCore*>(
            plan->survivorMarkerStaticShapeCore));
    if (!dynamicMain || !dynamicAux || !survivorStaticShape ||
        !markerStaticShape || dynamicAux == dynamicMain ||
        !dynamicMain->hasAABBMgrHandle() ||
        !dynamicAux->hasAABBMgrHandle() ||
        !survivorStaticShape->hasAABBMgrHandle() ||
        !markerStaticShape->hasAABBMgrHandle() ||
        dynamicMain->getGeometryType() != PxGeometryType::eCAPSULE ||
        dynamicAux->getGeometryType() != PxGeometryType::eBOX ||
        survivorStaticShape->getGeometryType() != PxGeometryType::eBOX ||
        markerStaticShape->getGeometryType() != PxGeometryType::eBOX ||
        !(dynamicMain->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
        (dynamicMain->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
        !(dynamicAux->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
        (dynamicAux->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
        !(survivorStaticShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
        (survivorStaticShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
        !(markerStaticShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
        (markerStaticShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
        &survivor->getShape0() != survivorStaticShape ||
        &survivor->getShape1() != dynamicMain ||
        !((&marker->getElementSim0() == markerStaticShape &&
           &marker->getElementSim1() == dynamicAux) ||
          (&marker->getElementSim0() == dynamicAux &&
           &marker->getElementSim1() == markerStaticShape)))
        return TriggerRewindUnsupportedScene;
    if (!matchesFilter(scene, *dynamicMain, *survivorStaticShape,
                       static_cast<PxU32>(PxPairFlag::eTRIGGER_DEFAULT)) ||
        !matchesMarkerFilter(scene, *dynamicAux, *markerStaticShape))
        return TriggerRewindUnexpectedFilter;

    Resolved resolved[2];
    Sc::RigidSim* missingStatics[2] = {NULL, NULL};
    for (PxU32 i = 0; i < 2; ++i)
    {
        const TriggerRewindPairV1& pair = plan->missing[i];
        if (!pair.dynamicActorCore || !pair.dynamicShapeCore ||
            !pair.staticActorCore || !pair.staticShapeCore ||
            pair.dynamicActorCore != firstPair.dynamicActorCore ||
            pair.dynamicShapeCore != firstPair.dynamicShapeCore ||
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
        Sc::RigidSim* staticActor =
            static_cast<Sc::RigidCore*>(pair.staticActorCore)->getSim();
        if (!staticActor || &staticActor->getScene() != &scene ||
            staticActor->getActorType() != PxActorType::eRIGID_STATIC ||
            staticActor == survivorStatic || staticActor == markerStatic ||
            staticActor->mInteractions.size() != 0)
            return TriggerRewindUnsupportedScene;
        Sc::ShapeSim* staticShape = findShape(*staticActor,
            *static_cast<Sc::ShapeCore*>(pair.staticShapeCore));
        if (!staticShape || !staticShape->hasAABBMgrHandle() ||
            staticShape->getGeometryType() != PxGeometryType::eBOX ||
            !(staticShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
            (staticShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE))
            return TriggerRewindUnsupportedScene;
        if (!matchesFilter(scene, *dynamicMain, *staticShape,
                           pair.expectedPairFlags))
            return TriggerRewindUnexpectedFilter;
        resolved[i].dynamicShape = dynamicMain;
        resolved[i].staticShape = staticShape;
        missingStatics[i] = staticActor;
    }
    const PxU32 freeHeadSlot0 = physicalSlot(nphase, nodes[0]);
    const PxU32 freeHeadSlot1 = physicalSlot(nphase, nodes[1]);
    if (resolved[0].staticShape == resolved[1].staticShape ||
        plan->missing[0].targetPoolSlot ==
            plan->missing[1].targetPoolSlot ||
        plan->missing[0].targetPoolSlot ==
            plan->targetSurvivorTriggerPoolSlot ||
        plan->missing[1].targetPoolSlot ==
            plan->targetSurvivorTriggerPoolSlot ||
        (freeHeadSlot0 != plan->missing[0].targetPoolSlot &&
         freeHeadSlot1 != plan->missing[0].targetPoolSlot) ||
        (freeHeadSlot0 != plan->missing[1].targetPoolSlot &&
         freeHeadSlot1 != plan->missing[1].targetPoolSlot) ||
        (dynamicActor->mInteractions[0] != survivor &&
         dynamicActor->mInteractions[1] != survivor) ||
        (dynamicActor->mInteractions[0] != marker &&
         dynamicActor->mInteractions[1] != marker))
        return TriggerRewindPoolMismatch;

    // The original lifecycle appends missing pairs. First restore the LIFO
    // free-head order so each new object receives its checkpoint pool slot.
    const PxU32 triggerSceneCapacity =
        interactions.mInteractions[Sc::PX_INTERACTION_TYPE_TRIGGER].capacity();
    const PxU32 dynamicActorCapacity = dynamicActor->mInteractions.mCapacity;
    auto* firstNode = freeHeadSlot0 == plan->missing[0].targetPoolSlot ?
        nodes[0] : nodes[1];
    auto* secondNode = firstNode == nodes[0] ? nodes[1] : nodes[0];
    firstNode->mNext = secondNode;
    secondNode->mNext = nodes[2];
    pool.mFreeElement = firstNode;

    Sc::Interaction* roles[4] = {NULL, NULL, survivor, marker};
    for (PxU32 i = 0; i < 2; ++i)
    {
        nphase.onOverlapCreated(resolved[i].dynamicShape,
                                resolved[i].staticShape, 0);
        if (interactions.getInteractionCount(
                Sc::PX_INTERACTION_TYPE_TRIGGER) != i + 2)
            std::abort();
        Sc::TriggerInteraction* trigger =
            static_cast<Sc::TriggerInteraction*>(
                interactions.mInteractions[
                    Sc::PX_INTERACTION_TYPE_TRIGGER][i + 1]);
        if (!trigger || trigger->getType() !=
                Sc::PX_INTERACTION_TYPE_TRIGGER ||
            physicalSlot(nphase, trigger) !=
                plan->missing[i].targetPoolSlot ||
            &trigger->getShape0() != resolved[i].staticShape ||
            &trigger->getShape1() != resolved[i].dynamicShape ||
            trigger->getInteractionFlags() !=
                plan->missing[i].targetInteractionFlags)
            std::abort();
        trigger->Sc::TriggerInteraction::mFlags =
            static_cast<PxU16>(plan->missing[i].targetTriggerFlags);
        trigger->mLastFrameHadContacts = true;
        trigger->mTriggerCache.state = Gu::TRIGGER_DISJOINT;
        trigger->Sc::CoreInteraction::mFlags =
            static_cast<PxU16>(plan->missing[i].targetCoreFlags);
        trigger->Sc::CoreInteraction::mDirtyFlags =
            static_cast<PxU16>(plan->missing[i].targetDirtyFlags);
        roles[i] = trigger;
    }

    // Restore the exact interaction-array projections and their reverse IDs.
    // No actor or scene buffer is resized by this fixed graph.
    for (PxU32 i = 0; i < 3; ++i)
    {
        Sc::Interaction* interaction =
            roles[plan->targetSceneTriggerOrder[i]];
        interactions.mInteractions[Sc::PX_INTERACTION_TYPE_TRIGGER][i] =
            interaction;
        interaction->mSceneId = i;
    }
    for (PxU32 i = 0; i < 4; ++i)
    {
        Sc::Interaction* interaction =
            roles[plan->targetDynamicActorOrder[i]];
        dynamicActor->mInteractions[i] = interaction;
        interaction->setActorId(dynamicActor, i);
    }
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) != 3 ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_TRIGGER) != 3 ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER) != 1 ||
        dynamicActor->mInteractions.size() != 4 || pool.mUsed != 3 ||
        interactions.mInteractions[
            Sc::PX_INTERACTION_TYPE_TRIGGER].capacity() !=
            triggerSceneCapacity ||
        dynamicActor->mInteractions.mCapacity != dynamicActorCapacity ||
        nphase.mInteractionMarkerPool.mUsed != 1)
        std::abort();
    for (PxU32 i = 0; i < 3; ++i)
        if (interactions.mInteractions[
                Sc::PX_INTERACTION_TYPE_TRIGGER][i]->mSceneId != i)
            std::abort();
    for (PxU32 i = 0; i < 4; ++i)
        if (dynamicActor->mInteractions[i]->getActorId(dynamicActor) != i)
            std::abort();
    for (PxU32 i = 0; i < 2; ++i)
        if (missingStatics[i]->mInteractions.size() != 1 ||
            missingStatics[i]->mInteractions[0] != roles[i] ||
            roles[i]->getActorId(missingStatics[i]) != 0)
            std::abort();
    if (survivorStatic->mInteractions.size() != 1 ||
        survivorStatic->mInteractions[0] != survivor ||
        markerStatic->mInteractions.size() != 1 ||
        markerStatic->mInteractions[0] != marker)
        std::abort();
    return TriggerRewindSuccess;
}
