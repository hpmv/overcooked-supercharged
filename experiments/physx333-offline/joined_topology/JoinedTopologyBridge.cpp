// Compiled only into an isolated, disposable PhysX 3.3.3 source DLL.
// This is a topology experiment, not an ABI-compatible Unity replacement.
#define PHYSX333_JOINED_TOPOLOGY_BRIDGE_BUILD
#include "JoinedTopologyBridge.h"

#include <cstdint>
#include <cstring>
#include <cstdlib>
#include <vector>

#define private public
#define protected public
#include "ScNPhaseCore.h"
#include "ScScene.h"
#include "ScShapeSim.h"
#include "ScShapeCore.h"
#include "ScRigidCore.h"
#include "ScRigidSim.h"
#include "ScActorSim.h"
#include "ScInteractionScene.h"
#include "ScInteraction.h"
#include "ScElement.h"
#include "ScElementSimInteraction.h"
#include "ScShapeInstancePairLL.h"
#include "ScTriggerInteraction.h"
#include "ScElementInteractionMarker.h"
#include "ScActorPair.h"
#include "ScCoreInteraction.h"
#include "PxsContext.h"
#include "PxsContactManager.h"
#include "PxsIslandManager.h"
#undef protected
#undef private

static_assert(sizeof(void*) == 4, "Joined topology bridge requires Win32");
static_assert(sizeof(physx333_offline::JoinedTopologyRoleV1) == 64,
              "Unexpected joined role ABI");
static_assert(sizeof(physx333_offline::JoinedTopologyActorOrderV1) == 80,
              "Unexpected joined actor ABI");
static_assert(sizeof(physx333_offline::JoinedTopologyPlanV1) == 2288,
              "Unexpected joined plan ABI");

namespace {
using namespace physx;
using namespace physx333_offline;

const PxU32 kContacts = 12, kTriggers = 4, kMarkers = 2;
const PxU32 kRoles = 18, kActors = 13, kPoolSize = 32;
const PxU32 kNoSlot = 0xffffffffu;

PxU32 edgeHookId(const PxsIslandManagerEdgeHook& hook)
{
    // This source-defined hook consists of one private EdgeType index; the
    // header is transitively included before the access-label shim opens it.
    static_assert(sizeof(hook) == sizeof(EdgeType),
                  "Unexpected PhysX edge-hook layout");
    EdgeType id;
    std::memcpy(&id, &hook, sizeof(id));
    return id;
}

struct ResolvedRole {
    Sc::RigidSim* actor0;
    Sc::RigidSim* actor1;
    Sc::ShapeSim* shape0;
    Sc::ShapeSim* shape1;
    Sc::Interaction* interaction;
    bool missing;
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

bool matchesFilter(Sc::Scene& scene, Sc::ShapeSim& a,
                   Sc::ShapeSim& b, PxU32 type, PxU32 expected)
{
    const PxSimulationFilterShader shader = scene.getFilterShaderFast();
    if (!shader || scene.getFilterCallbackFast()) return false;
    PxFilterObjectAttributes aa, ab;
    PxFilterData da, db;
    a.getFilterInfo(aa, da);
    b.getFilterInfo(ab, db);
    PxPairFlags flags;
    const PxFilterFlags result = shader(aa, da, ab, db, flags,
        scene.getFilterShaderDataFast(), scene.getFilterShaderDataSizeFast());
    return static_cast<PxU32>(result) ==
               (type == Sc::PX_INTERACTION_TYPE_MARKER ?
                static_cast<PxU32>(PxFilterFlag::eSUPPRESS) :
                static_cast<PxU32>(PxFilterFlag::eDEFAULT)) &&
           static_cast<PxU32>(flags) == expected;
}

bool samePair(const ResolvedRole& a, const ResolvedRole& b)
{
    return (a.shape0 == b.shape0 && a.shape1 == b.shape1) ||
           (a.shape0 == b.shape1 && a.shape1 == b.shape0);
}

bool endpoints(const Sc::ElementSimInteraction& interaction,
               const ResolvedRole& role)
{
    const Sc::ElementSim* a = &interaction.getElementSim0();
    const Sc::ElementSim* b = &interaction.getElementSim1();
    return (a == role.shape0 && b == role.shape1) ||
           (a == role.shape1 && b == role.shape0);
}

template<class T, class Pool>
PxU32 physicalSlot(const Pool& pool, const void* object)
{
    if (pool.mSlabs.size() != 1 || !object) return kNoSlot;
    const std::uintptr_t first =
        reinterpret_cast<std::uintptr_t>(pool.mSlabs[0]);
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(object);
    const std::uintptr_t end = first + pool.mElementsPerSlab * sizeof(T);
    if (address < first || address >= end ||
        (address - first) % sizeof(T)) return kNoSlot;
    return static_cast<PxU32>((address - first) / sizeof(T));
}

template<class T, class Pool>
bool validPool(const Pool& pool, PxU32 used)
{
    return pool.mSlabs.size() == 1 && pool.mElementsPerSlab == kPoolSize &&
           pool.mUsed == used && pool.mUnReleasedFree ==
               static_cast<PxI32>(kPoolSize - used) &&
           pool.mFreeElement != NULL;
}

template<class T, class Pool>
bool freePrefix(const Pool& pool, const PxU32* desired, PxU32 count)
{
    if (count > kPoolSize) return false;
    const typename Pool::FreeList* node = pool.mFreeElement;
    bool seen[kPoolSize] = {};
    PxU32 first[kPoolSize] = {};
    const PxU32 freeCount = kPoolSize - pool.mUsed;
    for (PxU32 i = 0; i < freeCount; ++i)
    {
        if (!node) return false;
        const PxU32 slot = physicalSlot<T>(pool, node);
        if (slot >= kPoolSize || seen[slot]) return false;
        seen[slot] = true;
        first[i] = slot;
        node = node->mNext;
    }
    if (node) return false;
    for (PxU32 i = 0; i < count; ++i)
    {
        bool found = false;
        for (PxU32 j = 0; j < count; ++j)
            if (first[j] == desired[i]) found = true;
        if (!found) return false;
        for (PxU32 j = 0; j < i; ++j)
            if (desired[j] == desired[i]) return false;
    }
    return true;
}

template<class T, class Pool>
void orderFreePrefix(Pool& pool, const PxU32* desired, PxU32 count)
{
    // freePrefix already proved these slots comprise exactly the first count
    // free nodes. Preserve the rest of the LIFO chain byte-for-byte.
    typename Pool::FreeList* nodes[kPoolSize] = {};
    typename Pool::FreeList* node = pool.mFreeElement;
    for (PxU32 i = 0; i <= count; ++i)
    {
        nodes[i] = node;
        if (node) node = node->mNext;
    }
    typename Pool::FreeList* ordered[kPoolSize] = {};
    for (PxU32 i = 0; i < count; ++i)
        for (PxU32 j = 0; j < count; ++j)
            if (physicalSlot<T>(pool, nodes[j]) == desired[i])
                ordered[i] = nodes[j];
    pool.mFreeElement = ordered[0];
    for (PxU32 i = 0; i + 1 < count; ++i)
        ordered[i]->mNext = ordered[i + 1];
    ordered[count - 1]->mNext = nodes[count];
}

typedef PxcPoolList<PxsContactManager, PxsContext> ContactManagerPool;

bool contactManagerFreeSuffix(const ContactManagerPool& pool,
                              const PxU32* desired, PxU32 count)
{
    // PxcPoolList::get pops mFreeList[--mFreeCount]. No growth may occur
    // during this fixed-size join, and the four target IDs must already be
    // the four available objects at that end of the stack.
    if (!pool.mSlabCount || !pool.mSlabs || !pool.mFreeList ||
        pool.mFreeCount < count ||
        pool.mFreeCount > pool.mSlabCount * pool.mEltsPerSlab)
        return false;
    const PxU32 capacity = pool.mSlabCount * pool.mEltsPerSlab;
    std::vector<bool> seen(capacity, false);
    PxU32 active = 0;
    for (PxU32 i = 0; i < capacity; ++i)
        active += pool.mUseBitmap.boundedTest(i) ? 1u : 0u;
    if (active != 8 || active + pool.mFreeCount != capacity)
        return false;
    for (PxU32 i = 0; i < pool.mFreeCount; ++i)
    {
        const PxsContactManager* manager = pool.mFreeList[i];
        if (!manager) return false;
        const PxU32 id = manager->getIndex();
        if (id >= capacity || seen[id] ||
            pool.mUseBitmap.boundedTest(id) ||
            manager != pool.mSlabs[id / pool.mEltsPerSlab] +
                       id % pool.mEltsPerSlab)
            return false;
        seen[id] = true;
    }
    for (PxU32 i = 0; i < count; ++i)
    {
        if (desired[i] >= capacity) return false;
        bool found = false;
        for (PxU32 j = 0; j < count; ++j)
            found |= pool.mFreeList[pool.mFreeCount - 1 - j]->getIndex() ==
                     desired[i];
        if (!found) return false;
        for (PxU32 j = 0; j < i; ++j)
            if (desired[j] == desired[i]) return false;
    }
    return true;
}

void orderContactManagerFreeSuffix(ContactManagerPool& pool,
                                   const PxU32* desired, PxU32 count)
{
    // Only the allocation end changes; the remainder of the free stack and
    // every used manager stay untouched.
    PxsContactManager* ordered[4] = {};
    for (PxU32 i = 0; i < count; ++i)
        for (PxU32 j = 0; j < count; ++j)
        {
            PxsContactManager* item =
                pool.mFreeList[pool.mFreeCount - 1 - j];
            if (item->getIndex() == desired[i]) ordered[i] = item;
        }
    for (PxU32 i = 0; i < count; ++i)
        pool.mFreeList[pool.mFreeCount - 1 - i] = ordered[i];
}

bool islandEdgeFreePrefix(const EdgeManager& edges,
                          const PxU32* desired, PxU32 count)
{
    // ElemManager::getAvailableElem consumes mNextFreeElem and follows
    // mFreeElems[id]. Check the complete chain before changing its prefix.
    if (!edges.mFreeElems || edges.mCapacity < 12 ||
        edges.mNumFreeElems < count ||
        edges.mNumFreeElems > edges.mCapacity ||
        edges.mCapacity - edges.mNumFreeElems != 8)
        return false;
    std::vector<bool> seen(edges.mCapacity, false);
    PxU32 prefix[4] = {};
    PxU32 id = edges.mNextFreeElem;
    for (PxU32 i = 0; i < edges.mNumFreeElems; ++i)
    {
        if (id >= edges.mCapacity || seen[id]) return false;
        seen[id] = true;
        if (i < count) prefix[i] = id;
        id = edges.mFreeElems[id];
    }
    if (id != static_cast<PxU32>(Edge::INVALID)) return false;
    for (PxU32 i = 0; i < count; ++i)
    {
        if (desired[i] >= edges.mCapacity) return false;
        bool found = false;
        for (PxU32 j = 0; j < count; ++j)
            found |= prefix[j] == desired[i];
        if (!found) return false;
        for (PxU32 j = 0; j < i; ++j)
            if (desired[j] == desired[i]) return false;
    }
    return true;
}

void orderIslandEdgeFreePrefix(EdgeManager& edges,
                               const PxU32* desired, PxU32 count)
{
    PxU32 tail = edges.mNextFreeElem;
    for (PxU32 i = 0; i < count; ++i)
        tail = edges.mFreeElems[tail];
    edges.mNextFreeElem = desired[0];
    for (PxU32 i = 0; i + 1 < count; ++i)
        edges.mFreeElems[desired[i]] = static_cast<EdgeType>(desired[i + 1]);
    edges.mFreeElems[desired[count - 1]] = static_cast<EdgeType>(tail);
}

PxU32 interactionSlot(const Sc::NPhaseCore& nphase,
                      const Sc::Interaction& interaction)
{
    switch (interaction.getType())
    {
    case Sc::PX_INTERACTION_TYPE_OVERLAP:
        return physicalSlot<Sc::ShapeInstancePairLL>(nphase.mLLSipPool,
            static_cast<const Sc::ShapeInstancePairLL*>(&interaction));
    case Sc::PX_INTERACTION_TYPE_TRIGGER:
        return physicalSlot<Sc::TriggerInteraction>(nphase.mTriggerPool,
            static_cast<const Sc::TriggerInteraction*>(&interaction));
    case Sc::PX_INTERACTION_TYPE_MARKER:
        return physicalSlot<Sc::ElementInteractionMarker>(
            nphase.mInteractionMarkerPool,
            static_cast<const Sc::ElementInteractionMarker*>(&interaction));
    default: return kNoSlot;
    }
}

Sc::Interaction* findExisting(Sc::InteractionScene& scene,
                             PxU32 type, const ResolvedRole& role)
{
    const Sc::InteractionType kind = static_cast<Sc::InteractionType>(type);
    const PxU32 count = scene.getInteractionCount(kind);
    for (PxU32 i = 0; i < count; ++i)
    {
        Sc::Interaction* item = scene.mInteractions[type][i];
        if (item && item->getType() == kind &&
            endpoints(*static_cast<Sc::ElementSimInteraction*>(item), role))
            return item;
    }
    return NULL;
}

bool validPermutation(const PxU32* values, PxU32 count,
                      PxU32 first, PxU32 last)
{
    if (count != last - first) return false;
    bool seen[kRoles] = {};
    for (PxU32 i = 0; i < count; ++i)
    {
        if (values[i] < first || values[i] >= last || seen[values[i]])
            return false;
        seen[values[i]] = true;
    }
    return true;
}

bool sameActorPair(const ResolvedRole& a, const ResolvedRole& b)
{
    return (a.actor0 == b.actor0 && a.actor1 == b.actor1) ||
           (a.actor0 == b.actor1 && a.actor1 == b.actor0);
}

void failStop() { std::abort(); }

} // namespace

extern "C" OC2_JOINED_TOPOLOGY_API std::uint32_t __cdecl
oc2_physx333_joined_topology_recreate_v1(void* nphaseCore,
    const JoinedTopologyPlanV1* plan)
{
    if (!nphaseCore || !plan ||
        !validPermutation(plan->contactSceneOrder, kContacts, 0, 12) ||
        !validPermutation(plan->triggerSceneOrder, kTriggers, 12, 16) ||
        !validPermutation(plan->markerSceneOrder, kMarkers, 16, 18))
        return JoinedTopologyInvalidInput;

    Sc::NPhaseCore& nphase = *static_cast<Sc::NPhaseCore*>(nphaseCore);
    Sc::Scene& scene = nphase.getScene();
    Sc::InteractionScene& interactions = scene.getInteractionScene();
    if (!interactions.getLowLevelContext())
        return JoinedTopologyUnsupportedScene;
    PxsContext& context = *interactions.getLowLevelContext();
    ContactManagerPool& managerPool = context.mContactManagerPool;
    EdgeManager& edgePool = context.getIslandManager().mEdgeManager;
    const PxU32 overlapType = Sc::PX_INTERACTION_TYPE_OVERLAP;
    const PxU32 triggerType = Sc::PX_INTERACTION_TYPE_TRIGGER;
    const PxU32 markerType = Sc::PX_INTERACTION_TYPE_MARKER;
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) != 8 ||
        interactions.getActiveInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) != 8 ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) != 2 ||
        interactions.getActiveInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) != 2 ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER) != 2 ||
        scene.getFilterCallbackFast() ||
        interactions.mInteractions[overlapType].capacity() < kContacts ||
        interactions.mInteractions[triggerType].capacity() < kTriggers ||
        interactions.mInteractions[markerType].capacity() < kMarkers ||
        !validPool<Sc::ShapeInstancePairLL>(nphase.mLLSipPool, 8) ||
        !validPool<Sc::ActorPair>(nphase.mActorPairPool, 8) ||
        !validPool<Sc::TriggerInteraction>(nphase.mTriggerPool, 2) ||
        !validPool<Sc::ElementInteractionMarker>(
            nphase.mInteractionMarkerPool, 2))
        return JoinedTopologyUnsupportedScene;

    ResolvedRole roles[kRoles] = {};
    Sc::RigidSim* actorSims[kActors] = {};
    for (PxU32 i = 0; i < kActors; ++i)
    {
        if (!plan->actors[i].actorCore || plan->actors[i].count > kRoles)
            return JoinedTopologyInvalidInput;
        Sc::RigidSim* actor = static_cast<Sc::RigidCore*>(
            plan->actors[i].actorCore)->getSim();
        if (!actor || &actor->getScene() != &scene ||
            actor->mInteractions.mCapacity < plan->actors[i].count)
            return JoinedTopologyUnsupportedScene;
        for (PxU32 j = 0; j < i; ++j)
            if (actorSims[j] == actor) return JoinedTopologyInvalidInput;
        actorSims[i] = actor;
    }

    for (PxU32 i = 0; i < kRoles; ++i)
    {
        const JoinedTopologyRoleV1& p = plan->roles[i];
        const PxU32 type = i < 12 ? overlapType :
                           i < 16 ? triggerType : markerType;
        if (p.type != type || !p.actorCore0 || !p.shapeCore0 ||
            !p.actorCore1 || !p.shapeCore1 ||
            p.actorCore0 == p.actorCore1 ||
            p.targetInteractionPoolSlot >= kPoolSize ||
            (type == overlapType ? p.targetActorPairPoolSlot >= kPoolSize :
                                   p.targetActorPairPoolSlot != kNoSlot) ||
            (type != overlapType &&
             (p.targetContactManagerIndex != kNoSlot ||
              p.targetIslandEdgeId != kNoSlot)) ||
            (type == markerType && p.expectedPairFlags != 0))
            return JoinedTopologyInvalidInput;
        Sc::RigidSim* a = static_cast<Sc::RigidCore*>(p.actorCore0)->getSim();
        Sc::RigidSim* b = static_cast<Sc::RigidCore*>(p.actorCore1)->getSim();
        if (!a || !b || &a->getScene() != &scene ||
            &b->getScene() != &scene)
            return JoinedTopologyUnresolvedShape;
        bool foundA = false, foundB = false;
        for (PxU32 j = 0; j < kActors; ++j)
        {
            foundA |= actorSims[j] == a;
            foundB |= actorSims[j] == b;
        }
        if (!foundA || !foundB) return JoinedTopologyInvalidInput;
        Sc::ShapeSim* sa = findShape(*a,
            *static_cast<Sc::ShapeCore*>(p.shapeCore0));
        Sc::ShapeSim* sb = findShape(*b,
            *static_cast<Sc::ShapeCore*>(p.shapeCore1));
        if (!sa || !sb || !sa->hasAABBMgrHandle() ||
            !sb->hasAABBMgrHandle())
            return JoinedTopologyUnresolvedShape;
        const bool aDynamic = a->getActorType() == PxActorType::eRIGID_DYNAMIC;
        const bool bDynamic = b->getActorType() == PxActorType::eRIGID_DYNAMIC;
        if (aDynamic == bDynamic ||
            (aDynamic ? b->getActorType() : a->getActorType()) !=
                PxActorType::eRIGID_STATIC)
            return JoinedTopologyUnsupportedScene;
        Sc::ShapeSim* dynamicShape = aDynamic ? sa : sb;
        Sc::ShapeSim* staticShape = aDynamic ? sb : sa;
        if (staticShape->getGeometryType() != PxGeometryType::eBOX ||
            (dynamicShape->getGeometryType() != PxGeometryType::eCAPSULE &&
             dynamicShape->getGeometryType() != PxGeometryType::eBOX) ||
            !(dynamicShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
            (dynamicShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
            (type == triggerType ?
                (!(staticShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
                 (staticShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE)) :
                (!(staticShape->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
                 (staticShape->getFlags() & PxShapeFlag::eTRIGGER_SHAPE))))
            return JoinedTopologyUnsupportedScene;
        if (!matchesFilter(scene, *sa, *sb, type, p.expectedPairFlags))
            return JoinedTopologyUnexpectedFilter;
        if (type == triggerType &&
            (p.expectedPairFlags !=
                static_cast<PxU32>(PxPairFlag::eTRIGGER_DEFAULT) ||
             p.targetLastTouch != 1 ||
             p.targetCacheState != Gu::TRIGGER_DISJOINT ||
             p.targetTriggerFlags !=
                 static_cast<PxU32>(PxPairFlag::eNOTIFY_TOUCH_FOUND |
                                    PxPairFlag::eNOTIFY_TOUCH_LOST) ||
             p.targetCoreFlags !=
                 Sc::CoreInteraction::IS_ELEMENT_INTERACTION ||
             p.targetDirtyFlags != Sc::CoreInteraction::CIF_DIRTY_ALL ||
             p.targetInteractionFlags !=
                 (Sc::PX_INTERACTION_FLAG_RB_ELEMENT |
                  Sc::PX_INTERACTION_FLAG_FILTERABLE)))
            return JoinedTopologyInvalidInput;
        roles[i].actor0 = a; roles[i].actor1 = b;
        roles[i].shape0 = sa; roles[i].shape1 = sb;
        roles[i].interaction = findExisting(interactions, type, roles[i]);
        roles[i].missing = roles[i].interaction == NULL;
        for (PxU32 j = 0; j < i; ++j)
            if (samePair(roles[i], roles[j]))
                return JoinedTopologyInvalidInput;
        if (roles[i].interaction)
        {
            Sc::Interaction* interaction = roles[i].interaction;
            if (interactionSlot(nphase, *interaction) !=
                    p.targetInteractionPoolSlot)
                return JoinedTopologyPoolMismatch;
            if (type == overlapType)
            {
                Sc::ShapeInstancePairLL* sip =
                    static_cast<Sc::ShapeInstancePairLL*>(interaction);
                if (&sip->getShape0() != sa || &sip->getShape1() != sb ||
                    physicalSlot<Sc::ActorPair>(nphase.mActorPairPool,
                        sip->getActorPair()) != p.targetActorPairPoolSlot ||
                    !sip->mManager ||
                    sip->mManager->getIndex() !=
                        p.targetContactManagerIndex ||
                    edgeHookId(sip->mLLIslandHook) != p.targetIslandEdgeId)
                    return JoinedTopologyUnsupportedScene;
            }
            else if (type == triggerType)
            {
                Sc::TriggerInteraction* trigger =
                    static_cast<Sc::TriggerInteraction*>(interaction);
                if (&trigger->getShape0() != sa ||
                    &trigger->getShape1() != sb ||
                    trigger->getInteractionFlags() !=
                        p.targetInteractionFlags ||
                    trigger->Sc::CoreInteraction::mFlags !=
                        p.targetCoreFlags ||
                    trigger->Sc::CoreInteraction::mDirtyFlags !=
                        p.targetDirtyFlags ||
                    trigger->Sc::TriggerInteraction::mFlags !=
                        p.targetTriggerFlags ||
                    trigger->mLastFrameHadContacts != p.targetLastTouch ||
                    trigger->mTriggerCache.state != p.targetCacheState)
                    return JoinedTopologyUnsupportedScene;
            }
        }
    }

    // The supplied role inventory must cover every live interaction exactly
    // once. This also forbids an unexpected native pair hidden in the scene.
    for (PxU32 t = 0; t < 3; ++t)
    {
        const PxU32 type = t == 0 ? overlapType :
                           t == 1 ? triggerType : markerType;
        const PxU32 expected = t == 0 ? 8 : 2;
        for (PxU32 j = 0; j < expected; ++j)
        {
            Sc::Interaction* item = interactions.mInteractions[type][j];
            PxU32 owners = 0;
            for (PxU32 i = 0; i < kRoles; ++i)
                owners += roles[i].interaction == item;
            if (owners != 1) return JoinedTopologyExistingInteraction;
        }
    }
    PxU32 missingContactSlots[4], missingActorPairSlots[4];
    PxU32 missingManagerIndices[4], missingEdgeIds[4];
    PxU32 missingTriggerSlots[2];
    bool markedMissing[kRoles] = {};
    for (PxU32 i = 0; i < 4; ++i)
    {
        const PxU32 id = plan->missingContactRoles[i];
        if (id >= 12 || markedMissing[id] || !roles[id].missing)
            return JoinedTopologyInvalidInput;
        markedMissing[id] = true;
        missingContactSlots[i] = plan->roles[id].targetInteractionPoolSlot;
        missingActorPairSlots[i] =
            plan->roles[id].targetActorPairPoolSlot;
        missingManagerIndices[i] =
            plan->roles[id].targetContactManagerIndex;
        missingEdgeIds[i] = plan->roles[id].targetIslandEdgeId;
        for (PxU32 j = 0; j < kContacts; ++j)
            if (j != id && sameActorPair(roles[id], roles[j]))
                return JoinedTopologyUnsupportedScene;
    }
    for (PxU32 i = 0; i < 2; ++i)
    {
        const PxU32 id = plan->missingTriggerRoles[i];
        if (id < 12 || id >= 16 || markedMissing[id] ||
            !roles[id].missing)
            return JoinedTopologyInvalidInput;
        markedMissing[id] = true;
        missingTriggerSlots[i] = plan->roles[id].targetInteractionPoolSlot;
    }
    for (PxU32 i = 0; i < kRoles; ++i)
        if (roles[i].missing != markedMissing[i])
            return JoinedTopologyInvalidInput;
    if (!freePrefix<Sc::ShapeInstancePairLL>(nphase.mLLSipPool,
            missingContactSlots, 4) ||
        !freePrefix<Sc::ActorPair>(nphase.mActorPairPool,
            missingActorPairSlots, 4) ||
        !freePrefix<Sc::TriggerInteraction>(nphase.mTriggerPool,
            missingTriggerSlots, 2) ||
        !contactManagerFreeSuffix(managerPool, missingManagerIndices, 4) ||
        !islandEdgeFreePrefix(edgePool, missingEdgeIds, 4))
        return JoinedTopologyPoolMismatch;

    // Validate each actor's proposed mixed array as a bijection with its
    // endpoints, and ensure no source array has to grow during recreation.
    PxU32 roleActorUses[kRoles] = {};
    PxU32 actorCapacities[kActors] = {};
    for (PxU32 i = 0; i < kActors; ++i)
    {
        const JoinedTopologyActorOrderV1& order = plan->actors[i];
        bool seen[kRoles] = {};
        Sc::RigidSim* actor = actorSims[i];
        actorCapacities[i] = actor->mInteractions.mCapacity;
        for (PxU32 j = 0; j < order.count; ++j)
        {
            const PxU32 id = order.roleIds[j];
            if (id >= kRoles || seen[id] ||
                (roles[id].actor0 != actor && roles[id].actor1 != actor))
                return JoinedTopologyInvalidInput;
            seen[id] = true;
            ++roleActorUses[id];
        }
        PxU32 live = 0;
        for (PxU32 id = 0; id < kRoles; ++id)
            if (seen[id] && !roles[id].missing) ++live;
        if (live != actor->mInteractions.size())
            return JoinedTopologyUnsupportedScene;
    }
    for (PxU32 i = 0; i < kRoles; ++i)
        if (roleActorUses[i] != 2) return JoinedTopologyInvalidInput;

    const PxU32 sceneCapacity[3] = {
        interactions.mInteractions[overlapType].capacity(),
        interactions.mInteractions[triggerType].capacity(),
        interactions.mInteractions[markerType].capacity()
    };

    // First write: rearrange only free-list heads that were independently
    // proved to contain precisely the target slots. The original constructors
    // then rebuild the missing contact and trigger graph in native source.
    orderFreePrefix<Sc::ShapeInstancePairLL>(nphase.mLLSipPool,
        missingContactSlots, 4);
    orderFreePrefix<Sc::ActorPair>(nphase.mActorPairPool,
        missingActorPairSlots, 4);
    orderFreePrefix<Sc::TriggerInteraction>(nphase.mTriggerPool,
        missingTriggerSlots, 2);
    orderContactManagerFreeSuffix(managerPool, missingManagerIndices, 4);
    orderIslandEdgeFreePrefix(edgePool, missingEdgeIds, 4);
    for (PxU32 i = 0; i < 4; ++i)
    {
        const PxU32 id = plan->missingContactRoles[i];
        ResolvedRole& r = roles[id];
        nphase.onOverlapCreated(r.shape0, r.shape1, 0);
        r.interaction = findExisting(interactions, overlapType, r);
        if (!r.interaction ||
            interactionSlot(nphase, *r.interaction) !=
                plan->roles[id].targetInteractionPoolSlot)
            failStop();
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(r.interaction);
        if (&sip->getShape0() != r.shape0 ||
            &sip->getShape1() != r.shape1 ||
            physicalSlot<Sc::ActorPair>(nphase.mActorPairPool,
                sip->getActorPair()) !=
                plan->roles[id].targetActorPairPoolSlot ||
            !sip->mManager ||
            sip->mManager->getIndex() !=
                plan->roles[id].targetContactManagerIndex ||
            edgeHookId(sip->mLLIslandHook) !=
                plan->roles[id].targetIslandEdgeId)
            failStop();
    }
    for (PxU32 i = 0; i < 2; ++i)
    {
        const PxU32 id = plan->missingTriggerRoles[i];
        ResolvedRole& r = roles[id];
        nphase.onOverlapCreated(r.shape0, r.shape1, 0);
        r.interaction = findExisting(interactions, triggerType, r);
        if (!r.interaction ||
            interactionSlot(nphase, *r.interaction) !=
                plan->roles[id].targetInteractionPoolSlot)
            failStop();
        Sc::TriggerInteraction* trigger =
            static_cast<Sc::TriggerInteraction*>(r.interaction);
        const JoinedTopologyRoleV1& target = plan->roles[id];
        if (&trigger->getShape0() != r.shape0 ||
            &trigger->getShape1() != r.shape1 ||
            trigger->getInteractionFlags() != target.targetInteractionFlags)
            failStop();
        trigger->Sc::TriggerInteraction::mFlags =
            static_cast<PxU16>(target.targetTriggerFlags);
        trigger->mLastFrameHadContacts = target.targetLastTouch != 0;
        trigger->mTriggerCache.state =
            static_cast<PxU16>(target.targetCacheState);
        trigger->Sc::CoreInteraction::mFlags =
            static_cast<PxU16>(target.targetCoreFlags);
        trigger->Sc::CoreInteraction::mDirtyFlags =
            static_cast<PxU16>(target.targetDirtyFlags);
    }

    const PxU32* sceneOrders[3] = {
        plan->contactSceneOrder, plan->triggerSceneOrder,
        plan->markerSceneOrder
    };
    const PxU32 sceneCounts[3] = {kContacts, kTriggers, kMarkers};
    const PxU32 sceneTypes[3] = {overlapType, triggerType, markerType};
    for (PxU32 t = 0; t < 3; ++t)
        for (PxU32 i = 0; i < sceneCounts[t]; ++i)
        {
            Sc::Interaction* item = roles[sceneOrders[t][i]].interaction;
            if (!item) failStop();
            interactions.mInteractions[sceneTypes[t]][i] = item;
            item->mSceneId = i;
        }
    for (PxU32 a = 0; a < kActors; ++a)
    {
        Sc::RigidSim* actor = actorSims[a];
        const JoinedTopologyActorOrderV1& order = plan->actors[a];
        for (PxU32 i = 0; i < order.count; ++i)
        {
            Sc::Interaction* item = roles[order.roleIds[i]].interaction;
            if (!item) failStop();
            actor->mInteractions[i] = item;
            item->setActorId(actor, i);
        }
    }

    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) !=
            kContacts ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_OVERLAP) != kContacts ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) !=
            kTriggers ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_TRIGGER) != kTriggers ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER) !=
            kMarkers ||
        nphase.mLLSipPool.mUsed != kContacts ||
        nphase.mActorPairPool.mUsed != kContacts ||
        nphase.mTriggerPool.mUsed != kTriggers ||
        nphase.mInteractionMarkerPool.mUsed != kMarkers)
        failStop();
    for (PxU32 t = 0; t < 3; ++t)
    {
        if (interactions.mInteractions[sceneTypes[t]].capacity() !=
                sceneCapacity[t]) failStop();
        for (PxU32 i = 0; i < sceneCounts[t]; ++i)
            if (interactions.mInteractions[sceneTypes[t]][i]->mSceneId != i)
                failStop();
    }
    for (PxU32 a = 0; a < kActors; ++a)
    {
        Sc::RigidSim* actor = actorSims[a];
        const JoinedTopologyActorOrderV1& order = plan->actors[a];
        if (actor->mInteractions.size() != order.count ||
            actor->mInteractions.mCapacity != actorCapacities[a])
            failStop();
        for (PxU32 i = 0; i < order.count; ++i)
            if (actor->mInteractions[i] !=
                    roles[order.roleIds[i]].interaction ||
                actor->mInteractions[i]->getActorId(actor) != i)
                failStop();
    }
    return JoinedTopologySuccess;
}
