#include "ShapeCacheBindings.h"

#include <cstdlib>
#include <map>
#include <set>
#include <utility>

#include "PxPhysicsAPI.h"

// Source-only access to the retained ShapeSim field. The vendor checkout and
// the built PhysX DLL are not patched for this component.
#define private public
#define protected public
#include "ScShapeSim.h"
#include "PxsTransformCache.h"
#include "CmIDPool.h"
#undef protected
#undef private

#include "NpScene.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScBodyCore.h"
#include "ScStaticCore.h"
#include "ScActorCore.h"
#include "ScBodySim.h"
#include "ScStaticSim.h"
#include "ScShapeSim.h"
#include "framework/ScElement.h"
#include "ScScene.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"

namespace oc2 { namespace offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "shape-cache binding requires Win32 PhysX");

bool bindingKeysEqual(const ShapeCacheBindings::Binding& a,
                      const ShapeCacheBindings::Binding& b)
{
    return a.shapeSim == b.shapeSim && a.shapeId == b.shapeId;
}

bool collect(PxScene& scene, ShapeCacheBindings& image,
             PxsTransformCache*& cache, std::string& error)
{
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    Sc::InteractionScene& interactions =
        np.getScene().getScScene().getInteractionScene();
    PxsContext* context = interactions.getLowLevelContext();
    if (!context)
    {
        error = "scene has no low-level context";
        return false;
    }
    cache = &context->getTransformCache();
    image.scene = reinterpret_cast<std::uintptr_t>(&scene);
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 actorCount = scene.getNbActors(flags);
    std::vector<PxActor*> actors(actorCount);
    if (actorCount && scene.getActors(flags, actors.data(), actorCount) != actorCount)
    {
        error = "rigid actor enumeration changed during shape-cache capture";
        return false;
    }
    std::map<std::uintptr_t, ShapeCacheBindings::Binding> seen;
    for (PxActor* actor : actors)
    {
        Sc::RigidSim* rigid = nullptr;
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
        {
            Sc::BodyCore& core = static_cast<NpRigidDynamic&>(*actor)
                .getScbBodyFast().getScBody();
            rigid = static_cast<Sc::RigidSim*>(
                static_cast<Sc::ActorCore&>(core).getSim());
        }
        else if (actor->getType() == PxActorType::eRIGID_STATIC)
        {
            Sc::StaticCore& core = static_cast<NpRigidStatic&>(*actor)
                .getScbRigidStaticFast().getScStatic();
            rigid = static_cast<Sc::RigidSim*>(
                static_cast<Sc::ActorCore&>(core).getSim());
        }
        if (!rigid)
        {
            error = "rigid actor has no same-scene simulation object";
            return false;
        }
        for (Sc::Element* element = rigid->getElements_(); element;
             element = element->mNextInActor)
        {
            if (element->getElementType() != Sc::PX_ELEMENT_TYPE_SHAPE)
            {
                error = "non-rigid shape element is unsupported";
                return false;
            }
            Sc::ShapeSim* shape = static_cast<Sc::ShapeSim*>(element);
            ShapeCacheBindings::Binding binding;
            binding.shapeSim = reinterpret_cast<std::uintptr_t>(shape);
            binding.shapeId = shape->getID();
            binding.transformCacheId = shape->getTransformCacheID();
            if (!seen.insert(std::make_pair(binding.shapeSim, binding)).second)
            {
                error = "ShapeSim occurs twice in rigid actor element lists";
                return false;
            }
        }
    }
    for (const auto& entry : seen)
        image.bindings.push_back(entry.second);
    return true;
}

void write(const ShapeCacheBindings& image)
{
    for (const auto& binding : image.bindings)
        reinterpret_cast<Sc::ShapeSim*>(binding.shapeSim)->mTransformCacheId =
            binding.transformCacheId;
}

} // namespace

bool ShapeCacheBindings::equals(const ShapeCacheBindings& other,
                                std::string& firstDifference) const
{
    firstDifference.clear();
    if (scene != other.scene || bindings.size() != other.bindings.size())
    {
        firstDifference = "scene or active shape count";
        return false;
    }
    for (std::size_t i = 0; i < bindings.size(); ++i)
        if (!bindingKeysEqual(bindings[i], other.bindings[i]) ||
            bindings[i].transformCacheId != other.bindings[i].transformCacheId)
        {
            firstDifference = "transform-cache ID on active shape " +
                std::to_string(i);
            return false;
        }
    return true;
}

bool CaptureShapeCacheBindings(PxScene& scene, ShapeCacheBindings& image,
                               std::string& error)
{
    error.clear();
    ShapeCacheBindings fresh;
    PxsTransformCache* cache = nullptr;
    if (!collect(scene, fresh, cache, error)) return false;
    image = std::move(fresh);
    return true;
}

bool RestoreShapeCacheBindings(PxScene& scene,
                               const ShapeCacheBindings& image,
                               std::string& error)
{
    error.clear();
    ShapeCacheBindings before;
    PxsTransformCache* cache = nullptr;
    if (!collect(scene, before, cache, error)) return false;
    if (image.scene != before.scene ||
        image.bindings.size() != before.bindings.size())
    {
        error = "shape-cache binding scene or active shape count changed";
        return false;
    }
    std::set<std::uint32_t> savedIds;
    for (std::size_t i = 0; i < image.bindings.size(); ++i)
    {
        const auto& target = image.bindings[i];
        const bool validCacheId = target.transformCacheId == PX_INVALID_U32 ||
            (target.transformCacheId < cache->mIDPool.mCurrentID &&
             cache->getReferenceCount(target.transformCacheId) != 0 &&
             savedIds.insert(target.transformCacheId).second);
        if (!bindingKeysEqual(target, before.bindings[i]) || !validCacheId)
        {
            error = "shape-cache identity, target ID, or reference count is invalid";
            return false;
        }
    }
    write(image);
    ShapeCacheBindings observed;
    std::string verificationError;
    bool verified = false;
    try
    {
        verified = CaptureShapeCacheBindings(scene, observed,
                                             verificationError) &&
                   image.equals(observed, verificationError);
    }
    catch (...) { verificationError = "postwrite capture threw"; }
    if (verified) return true;
    write(before);
    ShapeCacheBindings rolledBack;
    std::string rollbackError;
    if (!CaptureShapeCacheBindings(scene, rolledBack, rollbackError) ||
        !before.equals(rolledBack, rollbackError))
        std::abort();
    error = "shape-cache binding verification failed and was rolled back: " +
        verificationError;
    return false;
}

}} // namespace oc2::offline
