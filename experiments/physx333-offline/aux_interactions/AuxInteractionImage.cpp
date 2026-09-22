#include "AuxInteractionImage.h"

#include <algorithm>
#include <cstdint>
#include <limits>
#include <map>
#include <set>
#include <string>
#include <utility>
#include <vector>

// This access-label shim is confined to a source-built observer. It does not
// alter PhysX objects; all captured identities are normalized before export.
#define private public
#define protected public
#include "NpScene.h"
#include "NpShape.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScNPhaseCore.h"
#include "ScShapeInstancePairLL.h"
#include "ScTriggerInteraction.h"
#include "ScElementInteractionMarker.h"
#include "ScActorCore.h"
#include "ScActorSim.h"
#include "ScInteractionScene.h"
#include "ScScene.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "The PhysX 3.3.3 observer requires Win32");

typedef std::map<const Sc::ShapeCore*, AuxShapeKey> ShapeKeys;
typedef std::map<const Sc::Actor*, PxU32> ActorIds;
typedef std::map<const Sc::Interaction*, AuxPairKey> PairKeys;

template <class T, class Alloc>
bool poolSlot(const Ps::Pool<T, Alloc>& pool, const void* pointer,
              PxU32& slot)
{
    const std::uintptr_t address = reinterpret_cast<std::uintptr_t>(pointer);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const std::uintptr_t first =
            reinterpret_cast<std::uintptr_t>(pool.mSlabs[slab]);
        const std::uintptr_t end =
            first + pool.mElementsPerSlab * sizeof(T);
        if (address >= first && address < end &&
            (address - first) % sizeof(T) == 0)
        {
            slot = slab * pool.mElementsPerSlab +
                static_cast<PxU32>((address - first) / sizeof(T));
            return true;
        }
    }
    return false;
}

template <class T, class Alloc>
bool capturePool(const Ps::Pool<T, Alloc>& pool, AuxPoolImage& out,
                 const char* name, std::string& error)
{
    out.slabCount = pool.mSlabs.size();
    out.elementsPerSlab = pool.mElementsPerSlab;
    out.usedCount = pool.mUsed;
    out.unReleasedFree = pool.mUnReleasedFree;
    out.slabSize = pool.mSlabSize;
    if (out.slabCount > 1024 || !out.elementsPerSlab ||
        out.slabCount > 65536 / out.elementsPerSlab)
    {
        error = std::string(name) + " pool capacity is unsupported";
        return false;
    }
    const PxU32 capacity = out.slabCount * out.elementsPerSlab;
    std::vector<bool> seen(capacity, false);
    const typename Ps::PoolBase<T, Alloc>::FreeList* node =
        pool.mFreeElement;
    while (node)
    {
        PxU32 slot = 0;
        if (!poolSlot(pool, node, slot) || seen[slot] ||
            out.freeOrder.size() >= capacity)
        {
            error = std::string(name) + " pool free chain is invalid";
            return false;
        }
        seen[slot] = true;
        out.freeOrder.push_back(slot);
        node = node->mNext;
    }
    if (out.usedCount + out.freeOrder.size() != capacity)
    {
        error = std::string(name) + " pool used/free partition differs";
        return false;
    }
    return true;
}

bool actorShapes(PxScene& scene, ShapeKeys& shapes, ActorIds& actorIds,
                 std::map<PxU32, Sc::Actor*>& actorById,
                 std::string& error)
{
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    if (!count || count > 256)
    {
        error = "aux image has no supported rigid actor inventory";
        return false;
    }
    std::vector<PxActor*> publicActors(count);
    if (scene.getActors(flags, publicActors.data(), count) != count)
    {
        error = "actor enumeration changed during aux capture";
        return false;
    }
    for (PxActor* actor : publicActors)
    {
        const std::uintptr_t raw =
            reinterpret_cast<std::uintptr_t>(actor->userData);
        if (!raw || raw > std::numeric_limits<PxU32>::max())
        {
            error = "rigid actor lacks a 32-bit userData identity";
            return false;
        }
        const PxU32 actorId = static_cast<PxU32>(raw);
        Sc::Actor* sim = NULL;
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
            sim = static_cast<Sc::ActorCore&>(
                static_cast<NpRigidDynamic*>(actor)->getScbBodyFast()
                    .getScBody()).getSim();
        else if (actor->getType() == PxActorType::eRIGID_STATIC)
            sim = static_cast<Sc::ActorCore&>(
                static_cast<NpRigidStatic*>(actor)->getScbRigidStaticFast()
                    .getScStatic()).getSim();
        if (!sim || !actorById.insert(std::make_pair(actorId, sim)).second ||
            !actorIds.insert(std::make_pair(sim, actorId)).second)
        {
            error = "rigid actor simulation identity is absent or duplicated";
            return false;
        }
        PxRigidActor* rigid = static_cast<PxRigidActor*>(actor);
        const PxU32 shapeCount = rigid->getNbShapes();
        std::vector<PxShape*> publicShapes(shapeCount);
        if (shapeCount && rigid->getShapes(publicShapes.data(), shapeCount) !=
                              shapeCount)
        {
            error = "shape enumeration changed during aux capture";
            return false;
        }
        for (PxU32 i = 0; i < shapeCount; ++i)
        {
            if (!publicShapes[i])
            {
                error = "null rigid shape in aux fixture";
                return false;
            }
            NpShape* shape = static_cast<NpShape*>(publicShapes[i]);
            const Sc::ShapeCore* core =
                &shape->getScbShape().getScShape();
            AuxShapeKey key;
            key.actorId = actorId;
            key.shapeIndex = i;
            if (!shapes.insert(std::make_pair(core, key)).second)
            {
                error = "rigid shape core is duplicated";
                return false;
            }
        }
    }
    return true;
}

bool interactionKey(Sc::Interaction& interaction,
                    const ShapeKeys& shapes, AuxPairKey& out,
                    std::string& error)
{
    const Sc::InteractionType type = interaction.getType();
    if (type != Sc::PX_INTERACTION_TYPE_OVERLAP &&
        type != Sc::PX_INTERACTION_TYPE_TRIGGER &&
        type != Sc::PX_INTERACTION_TYPE_MARKER)
    {
        error = "non-shape interaction is outside aux fixture";
        return false;
    }
    Sc::ElementSimInteraction& element =
        static_cast<Sc::ElementSimInteraction&>(interaction);
    if (element.getElementSim0().getElementType() !=
            Sc::PX_ELEMENT_TYPE_SHAPE ||
        element.getElementSim1().getElementType() !=
            Sc::PX_ELEMENT_TYPE_SHAPE)
    {
        error = "aux interaction does not bind two rigid shapes";
        return false;
    }
    const Sc::ShapeCore* core0 =
        &static_cast<Sc::ShapeSim&>(element.getElementSim0()).getCore();
    const Sc::ShapeCore* core1 =
        &static_cast<Sc::ShapeSim&>(element.getElementSim1()).getCore();
    const ShapeKeys::const_iterator a = shapes.find(core0);
    const ShapeKeys::const_iterator b = shapes.find(core1);
    if (a == shapes.end() || b == shapes.end())
    {
        error = "aux interaction references an unknown shape";
        return false;
    }
    out.type = type;
    out.shape0 = a->second;
    out.shape1 = b->second;
    return true;
}

template <class Pool>
bool slotIsFree(const Pool& pool, const AuxPoolImage& image, PxU32 slot)
{
    return slot >= pool.mSlabs.size() * pool.mElementsPerSlab ||
        std::find(image.freeOrder.begin(), image.freeOrder.end(), slot) !=
            image.freeOrder.end();
}

bool captureAuxRow(Sc::Interaction& interaction, const AuxPairKey& key,
                   PxU32 sceneIndex, PxU32 activeCount,
                   Sc::NPhaseCore& nphase,
                   const AuxPoolImage& triggerPool,
                   const AuxPoolImage& markerPool,
                   AuxInteractionRow& row, std::string& error)
{
    row.pair = key;
    row.sceneIndex = sceneIndex;
    row.active = sceneIndex < activeCount ? 1u : 0u;
    row.interactionFlags = interaction.getInteractionFlags();
    row.actorIndex0 = interaction.getActorId(&interaction.getActor0());
    row.actorIndex1 = interaction.getActorId(&interaction.getActor1());
    Sc::CoreInteraction& core =
        static_cast<Sc::ElementSimInteraction&>(interaction);
    row.coreFlags = core.mFlags;
    row.dirtyFlags = core.mDirtyFlags;
    if (key.type == Sc::PX_INTERACTION_TYPE_TRIGGER)
    {
        Sc::TriggerInteraction& trigger =
            static_cast<Sc::TriggerInteraction&>(interaction);
        if (trigger.getShape0().getGeometryType() != PxGeometryType::eBOX ||
            trigger.getShape1().getGeometryType() != PxGeometryType::eBOX ||
            !poolSlot(nphase.mTriggerPool, &trigger, row.poolSlot) ||
            slotIsFree(nphase.mTriggerPool, triggerPool, row.poolSlot))
        {
            error = "trigger geometry or physical pool slot is unsupported";
            return false;
        }
        row.triggerFlags = trigger.mFlags;
        row.lastFrameHadContacts = trigger.mLastFrameHadContacts ? 1u : 0u;
        row.triggerCacheState = trigger.mTriggerCache.state;
    }
    else
    {
        Sc::ElementInteractionMarker& marker =
            static_cast<Sc::ElementInteractionMarker&>(interaction);
        if (!poolSlot(nphase.mInteractionMarkerPool, &marker,
                      row.poolSlot) ||
            slotIsFree(nphase.mInteractionMarkerPool,
                       markerPool, row.poolSlot))
        {
            error = "marker physical pool slot is invalid";
            return false;
        }
    }
    return true;
}

} // namespace

bool AuxInteractionImage::equals(const AuxInteractionImage& b,
                                 std::string& firstDifference) const
{
    firstDifference.clear();
    if (!(triggerPool == b.triggerPool))
        firstDifference = "trigger pool/free order";
    else if (!(markerPool == b.markerPool))
        firstDifference = "marker pool/free order";
    else if (triggers != b.triggers)
        firstDifference = "ordered trigger rows";
    else if (markers != b.markers)
        firstDifference = "ordered marker rows";
    else if (sceneOrders != b.sceneOrders)
        firstDifference = "per-type scene interaction order";
    else if (actorOrders != b.actorOrders)
        firstDifference = "per-actor interaction order";
    return firstDifference.empty();
}

bool CaptureAuxInteractionImage(PxScene& scene,
                                AuxInteractionImage& image,
                                std::string& error)
{
    error.clear();
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "aux capture requires a completed fetchResults boundary";
        return false;
    }
    AuxInteractionImage next;
    ShapeKeys shapes;
    ActorIds actorIds;
    std::map<PxU32, Sc::Actor*> actorById;
    if (!actorShapes(scene, shapes, actorIds, actorById, error)) return false;
    Sc::Scene& sc = np.getScene().getScScene();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    Sc::NPhaseCore& nphase = *sc.getNPhaseCore();
    if (!capturePool(nphase.mTriggerPool, next.triggerPool,
                     "trigger", error) ||
        !capturePool(nphase.mInteractionMarkerPool, next.markerPool,
                     "marker", error))
        return false;

    PairKeys pairKeys;
    const Sc::InteractionType types[3] = {
        Sc::PX_INTERACTION_TYPE_OVERLAP,
        Sc::PX_INTERACTION_TYPE_TRIGGER,
        Sc::PX_INTERACTION_TYPE_MARKER
    };
    for (PxU32 typeIndex = 0; typeIndex < 3; ++typeIndex)
    {
        const Sc::InteractionType type = types[typeIndex];
        AuxSceneOrder order;
        order.type = type;
        order.activeCount = interactions.getActiveInteractionCount(type);
        order.capacity = interactions.mInteractions[type].capacity();
        const PxU32 size = interactions.getInteractionCount(type);
        if (order.activeCount > size || size > order.capacity || size > 256)
        {
            error = "aux scene interaction count/capacity is invalid";
            return false;
        }
        for (PxU32 i = 0; i < size; ++i)
        {
            Sc::Interaction* interaction = interactions.mInteractions[type][i];
            if (!interaction || interaction->getType() != type ||
                interaction->mSceneId != i)
            {
                error = "aux scene interaction reverse index is invalid";
                return false;
            }
            AuxPairKey key;
            if (!interactionKey(*interaction, shapes, key, error) ||
                !pairKeys.insert(std::make_pair(interaction, key)).second)
            {
                if (error.empty()) error = "duplicate aux interaction pointer";
                return false;
            }
            order.pairs.push_back(key);
            if (type == Sc::PX_INTERACTION_TYPE_TRIGGER ||
                type == Sc::PX_INTERACTION_TYPE_MARKER)
            {
                AuxInteractionRow row;
                if (!captureAuxRow(*interaction, key, i, order.activeCount,
                                   nphase, next.triggerPool, next.markerPool,
                                   row, error))
                    return false;
                if (type == Sc::PX_INTERACTION_TYPE_TRIGGER)
                    next.triggers.push_back(row);
                else
                    next.markers.push_back(row);
            }
        }
        next.sceneOrders.push_back(order);
    }
    for (PxU32 type = 0; type < Sc::PX_INTERACTION_TYPE_COUNT; ++type)
        if (type != Sc::PX_INTERACTION_TYPE_OVERLAP &&
            type != Sc::PX_INTERACTION_TYPE_TRIGGER &&
            type != Sc::PX_INTERACTION_TYPE_MARKER &&
            interactions.getInteractionCount(
                static_cast<Sc::InteractionType>(type)))
        {
            error = "aux fixture has an unsupported interaction type";
            return false;
        }
    if (next.triggers.size() != next.triggerPool.usedCount ||
        next.markers.size() != next.markerPool.usedCount)
    {
        error = "aux live row count differs from physical pool usage";
        return false;
    }

    for (const auto& actor : actorById)
    {
        AuxActorOrder order;
        order.actorId = actor.first;
        const Sc::Actor& sim = *actor.second;
        order.capacity = sim.mInteractions.mCapacity;
        order.transferringCount = sim.mNumTransferringInteractions;
        order.uniqueCount = sim.mNumUniqueInteractions;
        order.countedCount = sim.mNumCountedInteractions;
        if (sim.mInteractions.size() > order.capacity ||
            order.transferringCount > sim.mInteractions.size() ||
            order.countedCount > sim.mInteractions.size())
        {
            error = "aux actor interaction count/capacity is invalid";
            return false;
        }
        for (PxU32 i = 0; i < sim.mInteractions.size(); ++i)
        {
            Sc::Interaction* interaction = sim.mInteractions[i];
            const PairKeys::const_iterator found = pairKeys.find(interaction);
            if (found == pairKeys.end() ||
                interaction->getActorId(&sim) != i ||
                (&interaction->getActor0() != &sim &&
                 &interaction->getActor1() != &sim))
            {
                error = "aux actor interaction binding/reverse index differs";
                return false;
            }
            order.pairs.push_back(found->second);
        }
        next.actorOrders.push_back(order);
    }
    image = next;
    error.clear();
    return true;
}

} // namespace physx333_offline
