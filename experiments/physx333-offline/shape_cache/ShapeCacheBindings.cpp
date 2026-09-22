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
#include "ScScene.h"
#include "ScInteractionScene.h"
#include "ScShapeInstancePairLL.h"
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
    std::map<std::uintptr_t, ShapeCacheBindings::Binding> seen;
    Cm::Range<Sc::Interaction*const> range =
        interactions.getInteractions(Sc::PX_INTERACTION_TYPE_OVERLAP);
    while (!range.empty())
    {
        Sc::ShapeInstancePairLL* pair =
            static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        Sc::ShapeSim* shapes[] = {&pair->getShape0(), &pair->getShape1()};
        for (unsigned i = 0; i < 2; ++i)
        {
            ShapeCacheBindings::Binding binding;
            binding.shapeSim = reinterpret_cast<std::uintptr_t>(shapes[i]);
            binding.shapeId = shapes[i]->getID();
            binding.transformCacheId = shapes[i]->getTransformCacheID();
            const auto found = seen.find(binding.shapeSim);
            if (found != seen.end())
            {
                if (!bindingKeysEqual(found->second, binding) ||
                    found->second.transformCacheId != binding.transformCacheId)
                {
                    error = "ShapeSim appears with conflicting transform ID";
                    return false;
                }
            }
            else
                seen.insert(std::make_pair(binding.shapeSim, binding));
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
        if (!bindingKeysEqual(target, before.bindings[i]) ||
            target.transformCacheId == PX_INVALID_U32 ||
            target.transformCacheId >= cache->mIDPool.mCurrentID ||
            cache->getReferenceCount(target.transformCacheId) == 0 ||
            !savedIds.insert(target.transformCacheId).second)
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
