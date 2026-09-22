#include "TransformCacheImage.h"

#include <cstdlib>
#include <cstring>
#include <utility>
#include <vector>

#include "PxPhysicsAPI.h"

// These two headers are opened only for this source-backed offline test. No
// accessor or layout patch is applied to the vendor checkout or PhysX DLL.
#define private public
#define protected public
#include "CmIDPool.h"
#include "PxsTransformCache.h"
#undef protected
#undef private

#include "NpScene.h"
#include "ScScene.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"

namespace oc2 { namespace offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "transform cache image requires Win32 PhysX");
static_assert(sizeof(PxU32) == 4, "unexpected PhysX ID width");

const std::size_t kMaximumArrayBytes = 256u * 1024u * 1024u;

struct CacheAccess
{
    PxsTransformCache* cache = nullptr;
    Cm::IDPool* ids = nullptr;
};

bool getCache(PxScene& scene, CacheAccess& access, std::string& error)
{
    // PxPhysics::createScene creates this concrete scene in the offline
    // harness. Both phase flags must be clear after fetchResults.
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    PxsContext* context = np.getScene().getScScene()
                              .getInteractionScene().getLowLevelContext();
    if (!context)
    {
        error = "scene has no low-level context";
        return false;
    }
    access.cache = &context->getTransformCache();
    access.ids = &access.cache->mIDPool;
    return true;
}

template <typename T>
bool captureArray(const Ps::Array<T>& source, TransformCacheImage::Array& out,
                  const char* name, std::string& error)
{
    const std::size_t capacity = source.capacity();
    if (source.size() > capacity || capacity > kMaximumArrayBytes / sizeof(T))
    {
        error = std::string(name) + ": invalid or excessive size/capacity";
        return false;
    }
    if (capacity && !source.begin())
    {
        error = std::string(name) + ": null allocation with nonzero capacity";
        return false;
    }
    out.address = reinterpret_cast<std::uintptr_t>(source.begin());
    out.size = source.size();
    out.capacity = source.capacity();
    out.bytes.resize(capacity * sizeof(T));
    if (!out.bytes.empty())
        std::memcpy(out.bytes.data(), source.begin(), out.bytes.size());
    return true;
}

template <typename T>
bool sameArrayTopology(const Ps::Array<T>& live,
                       const TransformCacheImage::Array& saved,
                       const char* name, bool fixedSize, std::string& error)
{
    if (saved.capacity > kMaximumArrayBytes / sizeof(T) ||
        saved.size > saved.capacity ||
        saved.bytes.size() != std::size_t(saved.capacity) * sizeof(T))
    {
        error = std::string(name) + ": invalid saved size/capacity";
        return false;
    }
    if (saved.address != reinterpret_cast<std::uintptr_t>(live.begin()) ||
        saved.capacity != live.capacity() ||
        (fixedSize && saved.size != live.size()))
    {
        error = std::string(name) + ": allocation identity/capacity changed";
        return false;
    }
    return true;
}

bool readU32(const TransformCacheImage::Array& array, PxU32 index, PxU32& out)
{
    if (index >= array.capacity || sizeof(PxU32) *
            (std::size_t(index) + 1u) > array.bytes.size())
        return false;
    std::memcpy(&out, array.bytes.data() + sizeof(PxU32) * index,
                sizeof(PxU32));
    return true;
}

bool validateSaved(const TransformCacheImage& image, std::string& error)
{
    const TransformCacheImage::Array& poses = image.transforms;
    const TransformCacheImage::Array& refs = image.referenceCounts;
    const TransformCacheImage::Array& free = image.freeIds;
    if (poses.size != poses.capacity || refs.size != refs.capacity ||
        poses.capacity != refs.capacity || image.currentId > poses.size ||
        free.size > image.currentId)
    {
        error = "transform cache and ID pool size relation is invalid";
        return false;
    }
    std::vector<unsigned char> isFree(image.currentId, 0);
    for (PxU32 i = 0; i < free.size; ++i)
    {
        PxU32 id = 0;
        if (!readU32(free, i, id) || id >= image.currentId || isFree[id])
        {
            error = "ID pool free list has duplicate or out-of-range ID";
            return false;
        }
        isFree[id] = 1;
    }
    for (PxU32 i = 0; i < refs.size; ++i)
    {
        if (i < image.currentId && !isFree[i]) continue;
        PxU32 count = 0;
        if (!readU32(refs, i, count) || count != 0)
        {
            error = "free or unallocated transform cache ID has references";
            return false;
        }
    }
    return true;
}

template <typename T>
void writeArray(Ps::Array<T>& target, const TransformCacheImage::Array& source)
{
    if (!source.bytes.empty())
        std::memcpy(target.begin(), source.bytes.data(), source.bytes.size());
}

void writeImage(CacheAccess& access, const TransformCacheImage& image)
{
    writeArray(access.cache->mTransformCache, image.transforms);
    writeArray(access.cache->mRefCounts, image.referenceCounts);
    writeArray(access.ids->mFreeIDs, image.freeIds);
    access.ids->mFreeIDs.forceSize_Unsafe(image.freeIds.size);
    access.ids->mCurrentID = image.currentId;
}

bool sameArray(const TransformCacheImage::Array& a,
               const TransformCacheImage::Array& b)
{
    return a.address == b.address && a.size == b.size &&
           a.capacity == b.capacity && a.bytes == b.bytes;
}

} // namespace

bool TransformCacheImage::equals(const TransformCacheImage& other,
                                 std::string& firstDifference) const
{
    firstDifference.clear();
    if (scene != other.scene || cache != other.cache || idPool != other.idPool)
    {
        firstDifference = "scene/transform cache identity";
        return false;
    }
    if (currentId != other.currentId)
    {
        firstDifference = "ID pool current ID";
        return false;
    }
    if (!sameArray(transforms, other.transforms))
    {
        firstDifference = "transform array";
        return false;
    }
    if (!sameArray(referenceCounts, other.referenceCounts))
    {
        firstDifference = "reference count array";
        return false;
    }
    if (!sameArray(freeIds, other.freeIds))
    {
        firstDifference = "ID pool free IDs and allocated tail";
        return false;
    }
    return true;
}

bool CaptureTransformCache(PxScene& scene, TransformCacheImage& image,
                           std::string& error)
{
    error.clear();
    CacheAccess access;
    if (!getCache(scene, access, error)) return false;
    TransformCacheImage fresh;
    fresh.scene = reinterpret_cast<std::uintptr_t>(&scene);
    fresh.cache = reinterpret_cast<std::uintptr_t>(access.cache);
    fresh.idPool = reinterpret_cast<std::uintptr_t>(access.ids);
    fresh.currentId = access.ids->mCurrentID;
    if (!captureArray(access.cache->mTransformCache, fresh.transforms,
                      "transform array", error) ||
        !captureArray(access.cache->mRefCounts, fresh.referenceCounts,
                      "reference counts", error) ||
        !captureArray(access.ids->mFreeIDs, fresh.freeIds,
                      "ID pool free IDs", error) ||
        !validateSaved(fresh, error))
        return false;
    image = std::move(fresh);
    return true;
}

bool RestoreTransformCache(PxScene& scene, const TransformCacheImage& image,
                           std::string& error)
{
    error.clear();
    CacheAccess access;
    if (!getCache(scene, access, error)) return false;
    if (image.scene != reinterpret_cast<std::uintptr_t>(&scene) ||
        image.cache != reinterpret_cast<std::uintptr_t>(access.cache) ||
        image.idPool != reinterpret_cast<std::uintptr_t>(access.ids))
    {
        error = "transform cache image belongs to another scene";
        return false;
    }
    if (!sameArrayTopology(access.cache->mTransformCache, image.transforms,
                           "transform array", true, error) ||
        !sameArrayTopology(access.cache->mRefCounts, image.referenceCounts,
                           "reference counts", true, error) ||
        !sameArrayTopology(access.ids->mFreeIDs, image.freeIds,
                           "ID pool free IDs", false, error) ||
        !validateSaved(image, error))
        return false;

    // All structural checks precede the first write. The following copies
    // reuse existing allocations and cannot invoke a PhysX allocator.
    TransformCacheImage rollback;
    if (!CaptureTransformCache(scene, rollback, error)) return false;
    writeImage(access, image);
    TransformCacheImage observed;
    std::string verifyError;
    bool verified = false;
    try
    {
        verified = CaptureTransformCache(scene, observed, verifyError) &&
                   image.equals(observed, verifyError);
    }
    catch (...)
    {
        verifyError = "postwrite capture threw an exception";
    }
    if (!verified)
    {
        writeImage(access, rollback);
        TransformCacheImage reverted;
        std::string rollbackError;
        try
        {
            if (!CaptureTransformCache(scene, reverted, rollbackError) ||
                !rollback.equals(reverted, rollbackError))
                std::abort();
        }
        catch (...)
        {
            std::abort();
        }
        error = "transform cache restore failed and was rolled back: " + verifyError;
        return false;
    }
    return true;
}

}} // namespace oc2::offline
