#include "Oracle.h"

#include <algorithm>
#include <cstring>
#include <iomanip>
#include <limits>
#include <set>
#include <sstream>
#include <utility>

// This translation unit is a test-only observer. The access-label shim lets
// it inspect the unmodified, pinned PhysX 3.3.3 source layout. It never calls
// private mutation methods or writes into an SDK object.
#define private public
#define protected public
#include "NpScene.h"
#include "NpShape.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScNPhaseCore.h"
#include "ScActorPair.h"
#include "ScActorElementPair.h"
#include "ScShapeInstancePairLL.h"
#include "ScTriggerInteraction.h"
#include "ScElementInteractionMarker.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"
#include "PxsIslandManager.h"
#include "PxsContactManager.h"
#include "PxcContactCache.h"
#undef protected
#undef private

namespace physx333_offline {
namespace {

using namespace physx;

static_assert(sizeof(void*) == 4, "The private oracle requires Win32 PhysX");

typedef std::vector<std::uint32_t> Words;
typedef std::map<const Sc::ShapeCore*, std::pair<PxU32, PxU32> > ShapeIds;
typedef std::map<const Sc::RigidCore*, PxU32> ActorIds;

std::uint32_t floatBits(PxReal value)
{
    std::uint32_t result;
    std::memcpy(&result, &value, sizeof(result));
    return result;
}

PxU32 hookId(const PxsIslandManagerEdgeHook& hook)
{
    // NpScene transitively includes this private header before the access
    // shim can open it. The source defines the hook as one index field.
    static_assert(sizeof(hook) == sizeof(EdgeType),
                  "Unexpected PhysX island-hook layout");
    EdgeType id;
    std::memcpy(&id, &hook, sizeof(id));
    return id;
}

std::uint64_t hashBytes(const void* bytes, size_t count)
{
    const unsigned char* data = static_cast<const unsigned char*>(bytes);
    std::uint64_t hash = UINT64_C(14695981039346656037);
    for (size_t i = 0; i < count; ++i)
    {
        hash ^= data[i];
        hash *= UINT64_C(1099511628211);
    }
    return hash;
}

void appendHash(Words& out, const void* bytes, size_t count)
{
    const std::uint64_t value = hashBytes(bytes, count);
    out.push_back(static_cast<std::uint32_t>(count));
    out.push_back(static_cast<std::uint32_t>(value));
    out.push_back(static_cast<std::uint32_t>(value >> 32));
}

bool appendLocalContactCache(Words& out, const PxcNpCache& cache,
                             std::string& error)
{
    out.push_back(cache.size);
    if (!cache.size)
    {
        if (cache.ptr) { error = "Empty local-contact cache has nonnull data"; return false; }
        return true;
    }
    if (!cache.ptr)
    {
        error = "Nonempty local-contact cache has null data";
        return false;
    }
    const size_t payloadSize = (sizeof(PxcLocalContactsCache) + 3u) & ~size_t(3u);
    if (cache.size < payloadSize + sizeof(PxU32))
    {
        error = "Local-contact cache is shorter than its header";
        return false;
    }
    const PxcLocalContactsCache& payload =
        *reinterpret_cast<const PxcLocalContactsCache*>(cache.ptr);
    PxU32 contactBytes = 0;
    std::memcpy(&contactBytes, cache.ptr + payloadSize, sizeof(contactBytes));
    if (contactBytes > cache.size - payloadSize - sizeof(contactBytes) ||
        cache.size != ((payloadSize + sizeof(contactBytes) + contactBytes + 15u) & ~size_t(15u)))
    {
        error = "Local-contact cache payload length is inconsistent";
        return false;
    }
    // PxcNpCacheWrite rounds its allocation to 16 bytes, but does not write
    // the tail. PxcNpCacheRead2 reads only these typed fields and contactBytes.
    // Compare those semantics, not uninitialized allocator padding.
    const PxTransform transforms[2] = { payload.mTransform0, payload.mTransform1 };
    for (size_t i = 0; i < 2; ++i)
    {
        out.push_back(floatBits(transforms[i].p.x));
        out.push_back(floatBits(transforms[i].p.y));
        out.push_back(floatBits(transforms[i].p.z));
        out.push_back(floatBits(transforms[i].q.x));
        out.push_back(floatBits(transforms[i].q.y));
        out.push_back(floatBits(transforms[i].q.z));
        out.push_back(floatBits(transforms[i].q.w));
    }
    out.push_back(payload.mNbCachedContacts);
    out.push_back(payload.mUseFaceIndices ? 1u : 0u);
    out.push_back(payload.mSameNormal ? 1u : 0u);
    appendHash(out, cache.ptr + payloadSize + sizeof(contactBytes), contactBytes);
    return true;
}

void appendShape(Words& out, const ShapeIds& ids,
                 const Sc::ShapeCore& shape)
{
    const ShapeIds::const_iterator found = ids.find(&shape);
    if (found == ids.end())
    {
        out.push_back(0xffffffffu);
        out.push_back(0xffffffffu);
    }
    else
    {
        out.push_back(found->second.first);
        out.push_back(found->second.second);
    }
}

bool fixtureIds(PxScene& scene, ShapeIds& ids, ActorIds& actorCores,
                std::string& error)
{
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    std::vector<PxActor*> actors(count);
    if (count && scene.getActors(flags, &actors[0], count) != count)
    {
        error = "PxScene actor enumeration changed during capture";
        return false;
    }
    std::set<PxU32> actorIds;
    for (PxU32 i = 0; i < count; ++i)
    {
        PxActor* actor = actors[i];
        const uintptr_t userId = reinterpret_cast<uintptr_t>(actor->userData);
        if (!userId || userId > std::numeric_limits<PxU32>::max() ||
            !actorIds.insert(static_cast<PxU32>(userId)).second)
        {
            error = "Fixture actor userData must contain a unique nonzero 32-bit ID";
            return false;
        }
        PxRigidActor* rigid = static_cast<PxRigidActor*>(actor);
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
        {
            NpRigidDynamic* npActor = static_cast<NpRigidDynamic*>(actor);
            actorCores[&npActor->getScbBodyFast().getScBody()] =
                static_cast<PxU32>(userId);
        }
        else
        {
            NpRigidStatic* npActor = static_cast<NpRigidStatic*>(actor);
            actorCores[&npActor->getScbRigidStaticFast().getScStatic()] =
                static_cast<PxU32>(userId);
        }
        const PxU32 shapeCount = rigid->getNbShapes();
        std::vector<PxShape*> shapes(shapeCount);
        if (shapeCount && rigid->getShapes(&shapes[0], shapeCount) != shapeCount)
        {
            error = "Fixture shape enumeration changed during capture";
            return false;
        }
        for (PxU32 j = 0; j < shapeCount; ++j)
        {
            NpShape* npShape = static_cast<NpShape*>(shapes[j]);
            ids[&npShape->getScbShape().getScShape()] =
                std::make_pair(static_cast<PxU32>(userId), j);
        }
    }
    return true;
}

PxU32 actorId(const Sc::RigidSim& sim, const ActorIds& actorCores)
{
    const ActorIds::const_iterator found =
        actorCores.find(&sim.getRigidCore());
    return found == actorCores.end() ? 0xffffffffu : found->second;
}

// Pool free links are addresses. Convert each to a stable (slab, slot) ID.
template<class T, class Alloc>
bool poolSlot(const Ps::Pool<T, Alloc>& pool, const void* pointer,
              PxU32& slot)
{
    const uintptr_t address = reinterpret_cast<uintptr_t>(pointer);
    for (PxU32 slab = 0; slab < pool.mSlabs.size(); ++slab)
    {
        const uintptr_t first = reinterpret_cast<uintptr_t>(pool.mSlabs[slab]);
        const uintptr_t last = first + pool.mElementsPerSlab * sizeof(T);
        if (address >= first && address < last &&
            (address - first) % sizeof(T) == 0)
        {
            slot = slab * pool.mElementsPerSlab +
                   static_cast<PxU32>((address - first) / sizeof(T));
            return true;
        }
    }
    return false;
}

template<class T, class Alloc>
bool capturePool(const char* name, const Ps::Pool<T, Alloc>& pool,
                 OracleImage& image, std::string& error)
{
    Words& header = image.parts[std::string("nphase.pool.") + name + ".header"];
    header.push_back(pool.mSlabs.size());
    header.push_back(pool.mElementsPerSlab);
    header.push_back(pool.mUsed);
    header.push_back(static_cast<PxU32>(pool.mUnReleasedFree));
    header.push_back(pool.mSlabSize);
    Words& free = image.parts[std::string("nphase.pool.") + name + ".free_order"];
    const PxU32 capacity = pool.mSlabs.size() * pool.mElementsPerSlab;
    std::vector<bool> seen(capacity, false);
    const typename Ps::PoolBase<T, Alloc>::FreeList* cursor = pool.mFreeElement;
    while (cursor)
    {
        PxU32 slot = 0;
        if (!poolSlot(pool, cursor, slot) || seen[slot])
        {
            error = std::string("Invalid or cyclic NPhase pool free list: ") + name;
            return false;
        }
        seen[slot] = true;
        free.push_back(slot);
        cursor = cursor->mNext;
    }
    if (capacity - free.size() != pool.mUsed)
    {
        error = std::string("NPhase pool active/free partition differs: ") + name;
        return false;
    }
    return true;
}

template<class Manager>
bool captureElemManager(const char* name, const Manager& manager,
                        PxU32 invalid, OracleImage& image,
                        std::vector<bool>& freeMask,
                        std::string& error)
{
    const std::string prefix = std::string("island.") + name;
    Words& head = image.parts[prefix + ".header"];
    head.push_back(manager.mCapacity);
    head.push_back(manager.mNextFreeElem);
    head.push_back(manager.mNumFreeElems);
    freeMask.assign(manager.mCapacity, false);
    Words& order = image.parts[prefix + ".free_order"];
    PxU32 cursor = manager.mNextFreeElem;
    while (cursor != invalid)
    {
        if (cursor >= manager.mCapacity || freeMask[cursor])
        {
            error = prefix + " free list is out of range or cyclic";
            return false;
        }
        freeMask[cursor] = true;
        order.push_back(cursor);
        cursor = manager.mFreeElems[cursor];
    }
    if (order.size() != manager.mNumFreeElems)
    {
        error = prefix + " free count differs from chain length";
        return false;
    }
    return true;
}

template<class Id>
void appendActiveIslandIds(Words& out, const Id* ids, PxU32 count)
{
    out.push_back(count);
    for (PxU32 i = 0; i < count; ++i) out.push_back(ids[i]);
}

} // namespace

bool OracleImage::equals(const OracleImage& other,
                         std::string& firstDifference) const
{
    if (unsupported != other.unsupported)
    {
        firstDifference = "unsupported coverage differs";
        return false;
    }
    std::map<std::string, Words>::const_iterator a = parts.begin();
    std::map<std::string, Words>::const_iterator b = other.parts.begin();
    for (; a != parts.end() && b != other.parts.end(); ++a, ++b)
    {
        if (a->first != b->first)
        {
            firstDifference = "section differs: " + a->first + " vs " + b->first;
            return false;
        }
        if (a->second.size() != b->second.size())
        {
            firstDifference = "word count differs: " + a->first;
            return false;
        }
        for (size_t i = 0; i < a->second.size(); ++i)
        {
            if (a->second[i] != b->second[i])
            {
                std::ostringstream out;
                out << a->first << '[' << i << "] 0x" << std::hex
                    << a->second[i] << " vs 0x" << b->second[i];
                firstDifference = out.str();
                return false;
            }
        }
    }
    if (a != parts.end() || b != other.parts.end())
    {
        firstDifference = "section count differs";
        return false;
    }
    firstDifference.clear();
    return true;
}

std::string OracleImage::summary() const
{
    std::ostringstream out;
    out << parts.size() << " sections";
    for (std::map<std::string, Words>::const_iterator it = parts.begin();
         it != parts.end(); ++it)
        out << ", " << it->first << '=' << it->second.size();
    if (!unsupported.empty()) out << ", unsupported=" << unsupported.size();
    return out.str();
}

bool CaptureOracle(PxScene& scene, OracleImage& image, std::string& error)
{
    NpScene& npScene = static_cast<NpScene&>(scene);
    if (npScene.isPhysicsRunning() || npScene.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    OracleImage next;
    ShapeIds ids;
    ActorIds actorCores;
    if (!fixtureIds(scene, ids, actorCores, error)) return false;
    Sc::Scene& sc = npScene.getScene().getScScene();
    Sc::NPhaseCore& np = *sc.getNPhaseCore();
    Sc::InteractionScene& interactions = sc.getInteractionScene();
    PxsContext& context = *interactions.getLowLevelContext();
    PxsIslandManager& islands = context.getIslandManager();

    // The six NPhase pools that own rigid pair/interaction/report objects.
    if (!capturePool("actor_pair", np.mActorPairPool, next, error) ||
        !capturePool("actor_element_pair", np.mActorElementPairPool, next, error) ||
        !capturePool("shape_pair", np.mLLSipPool, next, error) ||
        !capturePool("trigger", np.mTriggerPool, next, error) ||
        !capturePool("actor_pair_report", np.mActorPairContactReportDataPool, next, error) ||
        !capturePool("marker", np.mInteractionMarkerPool, next, error))
        return false;

    // InteractionScene keeps a stable ordered vector per type, with the active
    // prefix separated by mActiveInteractionCount. Project shape pairs and
    // their ActorPair and low-level contact-manager state into fixture IDs.
    Words& counts = next.parts["nphase.interaction_counts"];
    for (PxU32 type = 0; type < Sc::PX_INTERACTION_TYPE_COUNT; ++type)
    {
        counts.push_back(interactions.getInteractionCount(static_cast<Sc::InteractionType>(type)));
        counts.push_back(interactions.getActiveInteractionCount(static_cast<Sc::InteractionType>(type)));
    }
    Cm::Range<Sc::Interaction*const> range =
        interactions.getInteractions(Sc::PX_INTERACTION_TYPE_OVERLAP);
    Words& sipRows = next.parts["nphase.shape_pairs"];
    Words& actorRows = next.parts["nphase.actor_pairs"];
    Words& cmRows = next.parts["contact.managers"];
    Words& streamRows = next.parts["contact.streams"];
    Words& manifoldRows = next.parts["contact.manifolds"];
    std::set<const Sc::ActorPair*> seenActorPairs;
    std::set<PxU32> seenManagerIds;
    PxU32 sipOrdinal = 0;
    while (!range.empty())
    {
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        const Sc::ShapeSim& s0 = sip->getShape0();
        const Sc::ShapeSim& s1 = sip->getShape1();
        if (ids.find(&s0.getCore()) == ids.end() ||
            ids.find(&s1.getCore()) == ids.end())
        {
            error = "SIP refers to a shape outside the fixture identity map";
            return false;
        }
        sipRows.push_back(sipOrdinal++);
        PxU32 sipPoolSlot = 0;
        if (!poolSlot(np.mLLSipPool, sip, sipPoolSlot))
        {
            error = "Registered SIP is outside its NPhase pool";
            return false;
        }
        sipRows.push_back(sipPoolSlot);
        appendShape(sipRows, ids, s0.getCore());
        appendShape(sipRows, ids, s1.getCore());
        sipRows.push_back(sip->mFlags);
        sipRows.push_back(sip->mContactReportStamp);
        sipRows.push_back(sip->mReportPairIndex);
        sipRows.push_back(sip->mReportStreamIndex);
        sipRows.push_back(hookId(sip->mLLIslandHook));
        sipRows.push_back(sip->mManager ? sip->mManager->getIndex() : 0xffffffffu);
        Sc::ActorPair& pair = *sip->getActorPair();
        if (actorId(pair.getActorA(), actorCores) == 0xffffffffu ||
            actorId(pair.getActorB(), actorCores) == 0xffffffffu)
        {
            error = "ActorPair refers to an actor outside the fixture identity map";
            return false;
        }
        if (seenActorPairs.insert(&pair).second)
        {
            PxU32 actorPairSlot = 0;
            if (!poolSlot(np.mActorPairPool, &pair, actorPairSlot))
            {
                error = "ActorPair is outside its NPhase pool";
                return false;
            }
            actorRows.push_back(actorPairSlot);
            actorRows.push_back(actorId(pair.getActorA(), actorCores));
            actorRows.push_back(actorId(pair.getActorB(), actorCores));
            actorRows.push_back(pair.mInternalFlags);
            actorRows.push_back(pair.mTouchCount);
            actorRows.push_back(pair.mRefCount);
            actorRows.push_back(pair.mReportData ? 1u : 0u);
            if (pair.mReportData)
            {
                PxU32 reportSlot = 0;
                if (!poolSlot(np.mActorPairContactReportDataPool,
                              pair.mReportData, reportSlot))
                {
                    error = "ActorPair report data is outside its NPhase pool";
                    return false;
                }
                actorRows.push_back(reportSlot);
                actorRows.push_back(pair.mReportData->mStrmResetStamp);
                actorRows.push_back(pair.mReportData->mActorAID);
                actorRows.push_back(pair.mReportData->mActorBID);
            }
        }
        if (!sip->mManager) continue;
        PxsContactManager& cm = *sip->mManager;
        if (!seenManagerIds.insert(cm.getIndex()).second)
        {
            error = "Duplicate low-level contact manager index";
            return false;
        }
        const PxcNpWorkUnit& work = cm.getWorkUnit();
        cmRows.push_back(cm.getIndex());
        cmRows.push_back(cm.mFlags);
        cmRows.push_back(work.statusFlags);
        cmRows.push_back(work.flags);
        cmRows.push_back(work.contactCount);
        cmRows.push_back(work.compressedContactSize);
        cmRows.push_back(work.frictionPatchCount);
        cmRows.push_back(work.axisConstraintCount);
        cmRows.push_back(work.solverConstraintSize);
        // prevSolverConstraintSize is declared in 3.3.3 but has no reads or
        // writes in the pinned source. It is uninitialized allocator residue,
        // not a replay state variable, so do not compare it across scenes.
        cmRows.push_back(work.pairCache.pairData);
        cmRows.push_back(work.pairCache.size);
        cmRows.push_back(floatBits(work.restDistance));
        cmRows.push_back(work.mTransformCache0);
        cmRows.push_back(work.mTransformCache1);
        if (work.compressedContactSize && !work.compressedContacts)
        {
            error = "Contact manager has a nonempty stream with a null pointer";
            return false;
        }
        streamRows.push_back(cm.getIndex());
        appendHash(streamRows, work.compressedContacts, work.compressedContactSize);
        streamRows.push_back(cm.getIndex());
        if (!appendLocalContactCache(streamRows, work.pairCache, error))
            return false;
        const uintptr_t manifold = work.pairCache.manifold;
        manifoldRows.push_back(cm.getIndex());
        manifoldRows.push_back(manifold ? ((manifold & 1) ? 2u : 1u) : 0u);
        if (manifold && !(manifold & 1))
        {
            Gu::PersistentContactManifold& data =
                *reinterpret_cast<Gu::PersistentContactManifold*>(manifold);
            const PxU32 contactCount = data.getNumContacts();
            if (contactCount > GU_MANIFOLD_CACHE_SIZE)
            {
                error = "Contact manifold count exceeds the 3.3.3 cache limit";
                return false;
            }
            manifoldRows.push_back(contactCount);
            for (PxU32 c = 0; c < contactCount; ++c)
                appendHash(manifoldRows, &data.getContactPoint(c),
                           sizeof(Gu::PersistentContact));
        }
        else
        {
            manifoldRows.push_back(0);
            if (manifold)
                next.unsupported.push_back("multi-manifold contents");
        }
    }

    Words& nphaseLists = next.parts["nphase.event_lists"];
    nphaseLists.push_back(np.mPersistentContactEventPairList.size());
    nphaseLists.push_back(np.mNextFramePersistentContactEventPairIndex);
    nphaseLists.push_back(np.mForceThresholdContactEventPairList.size());
    nphaseLists.push_back(np.mContactReportActorPairSet.size());
    // Ordered list identities are recorded through their corresponding SIP
    // indices, avoiding allocator addresses in the image.
    std::map<const Sc::ShapeInstancePairLL*, PxU32> sipIndex;
    Cm::Range<Sc::Interaction*const> range2 =
        interactions.getInteractions(Sc::PX_INTERACTION_TYPE_OVERLAP);
    for (PxU32 i = 0; !range2.empty(); ++i)
    {
        sipIndex[static_cast<Sc::ShapeInstancePairLL*>(range2.front())] = i;
        range2.popFront();
    }
    for (PxU32 i = 0; i < np.mPersistentContactEventPairList.size(); ++i)
    {
        const std::map<const Sc::ShapeInstancePairLL*, PxU32>::const_iterator it =
            sipIndex.find(np.mPersistentContactEventPairList[i]);
        if (it == sipIndex.end()) { error = "Unregistered persistent SIP"; return false; }
        nphaseLists.push_back(it->second);
    }
    for (PxU32 i = 0; i < np.mForceThresholdContactEventPairList.size(); ++i)
    {
        const std::map<const Sc::ShapeInstancePairLL*, PxU32>::const_iterator it =
            sipIndex.find(np.mForceThresholdContactEventPairList[i]);
        if (it == sipIndex.end()) { error = "Unregistered threshold SIP"; return false; }
        nphaseLists.push_back(it->second);
    }
    for (PxU32 i = 0; i < np.mContactReportActorPairSet.size(); ++i)
    {
        const Sc::ActorPair* pair = np.mContactReportActorPairSet[i];
        nphaseLists.push_back(actorId(pair->getActorA(), actorCores));
        nphaseLists.push_back(actorId(pair->getActorB(), actorCores));
    }
    Words& reportBuffer = next.parts["nphase.report_buffer"];
    reportBuffer.push_back(np.mContactReportBuffer.mCurrentBufferIndex);
    reportBuffer.push_back(np.mContactReportBuffer.mCurrentBufferSize);
    reportBuffer.push_back(np.mContactReportBuffer.mDefaultBufferSize);
    reportBuffer.push_back(np.mContactReportBuffer.mLastBufferIndex);
    reportBuffer.push_back(np.mContactReportBuffer.mAllocationLocked ? 1u : 0u);
    if (np.mContactReportBuffer.mCurrentBufferIndex >
        np.mContactReportBuffer.mCurrentBufferSize)
    {
        error = "Contact report buffer cursor exceeds capacity";
        return false;
    }
    appendHash(reportBuffer, np.mContactReportBuffer.mBuffer,
               np.mContactReportBuffer.mCurrentBufferIndex);

    // Low-level CM pool allocation order and occupancy. The free stack is
    // decisive for the next manager ID; active indices validate the SIP map.
    const PxcPoolList<PxsContactManager, PxsContext>& cmPool =
        context.mContactManagerPool;
    Words& cmPoolHead = next.parts["contact.pool.header"];
    cmPoolHead.push_back(cmPool.mSlabCount);
    cmPoolHead.push_back(cmPool.mEltsPerSlab);
    cmPoolHead.push_back(cmPool.mFreeCount);
    cmPoolHead.push_back(cmPool.mMaxSlabs);
    Words& cmPoolFree = next.parts["contact.pool.free_order"];
    std::set<PxU32> freeManagerIds;
    for (PxU32 i = 0; i < cmPool.mFreeCount; ++i)
    {
        const PxU32 id = cmPool.mFreeList[i]->getIndex();
        if (id >= cmPool.mSlabCount * cmPool.mEltsPerSlab ||
            !freeManagerIds.insert(id).second ||
            cmPool.mUseBitmap.boundedTest(id))
        {
            error = "Contact-manager pool free stack is invalid";
            return false;
        }
        cmPoolFree.push_back(id);
    }
    Words& cmPoolUsed = next.parts["contact.pool.used_indices"];
    for (PxU32 i = 0; i < cmPool.mSlabCount * cmPool.mEltsPerSlab; ++i)
        if (cmPool.mUseBitmap.boundedTest(i)) cmPoolUsed.push_back(i);
    if (cmPoolFree.size() + cmPoolUsed.size() !=
        cmPool.mSlabCount * cmPool.mEltsPerSlab)
    {
        error = "Contact-manager pool active/free partition differs";
        return false;
    }

    // The three indexed island pools retain their exact ordered free links.
    // Active semantic rows omit free-slot payload, which is not constructed.
    const NodeManager& nodes = islands.mNodeManager;
    const EdgeManager& edges = islands.mEdgeManager;
    const IslandManager& islandPool = islands.mIslands;
    std::vector<bool> freeNodes, freeEdges, freeIslands;
    if (!captureElemManager("nodes", nodes, static_cast<PxU32>(Node::INVALID),
                            next, freeNodes, error) ||
        !captureElemManager("edges", edges, static_cast<PxU32>(Edge::INVALID),
                            next, freeEdges, error) ||
        !captureElemManager("islands", islandPool, static_cast<PxU32>(Island::INVALID),
                            next, freeIslands, error))
        return false;
    Words& nodeRows = next.parts["island.nodes.active"];
    Words& nodeLinks = next.parts["island.nodes.all_links"];
    for (PxU32 i = 0; i < nodes.mCapacity; ++i)
    {
        nodeLinks.push_back(nodes.mFreeElems[i]);
        nodeLinks.push_back(nodes.mNextNodeIds[i]);
    }
    for (PxU32 b = 0; b < NodeManager::eMAX_NB_BITMAPS; ++b)
    {
        Words& bitmap = next.parts["island.nodes.bitmaps"];
        bitmap.push_back(b);
        bitmap.push_back(nodes.mBitmapWordCounts[b]);
        for (PxU32 w = 0; w < nodes.mBitmapWordCounts[b]; ++w)
            bitmap.push_back(nodes.mBitmapWords[b][w]);
    }
    for (PxU32 i = 0; i < nodes.mCapacity; ++i)
    {
        if (freeNodes[i]) continue;
        const Node& node = nodes.mElems[i];
        nodeRows.push_back(i);
        nodeRows.push_back(node.getFlags());
        nodeRows.push_back(node.getIslandId());
        nodeRows.push_back(nodes.mNextNodeIds[i]);
        if (node.getIsArticulated())
        {
            nodeRows.push_back(0xffffffffu);
            next.unsupported.push_back("articulation owner normalization");
        }
        else
        {
            const Sc::BodySim* body =
                reinterpret_cast<const Sc::BodySim*>(node.mRigidBodyOwner);
            nodeRows.push_back(body ? actorId(*body, actorCores) : 0xffffffffu);
        }
    }
    Words& edgeRows = next.parts["island.edges.active"];
    Words& edgeLinks = next.parts["island.edges.all_links"];
    for (PxU32 i = 0; i < edges.mCapacity; ++i)
    {
        edgeLinks.push_back(edges.mFreeElems[i]);
        edgeLinks.push_back(edges.mNextEdgeIds[i]);
    }
    for (PxU32 i = 0; i < edges.mCapacity; ++i)
    {
        if (freeEdges[i]) continue;
        const Edge& edge = edges.mElems[i];
        const uintptr_t tagged = reinterpret_cast<uintptr_t>(edge.mContactManager);
        edgeRows.push_back(i);
        edgeRows.push_back(edge.getNode1());
        edgeRows.push_back(edge.getNode2());
        edgeRows.push_back(edges.mNextEdgeIds[i]);
        edgeRows.push_back(static_cast<PxU32>(tagged & Edge::EDGE_ALL_FLAGS));
        edgeRows.push_back(edge.getIsTypeCM() && (tagged & ~uintptr_t(15))
            ? reinterpret_cast<PxsContactManager*>(tagged & ~uintptr_t(15))->getIndex()
            : 0xffffffffu);
        if (!edge.getIsTypeCM() && (tagged & ~uintptr_t(15)))
            next.unsupported.push_back("constraint pointer normalization");
    }
    Words& islandRows = next.parts["island.islands.active"];
    Words& islandLinks = next.parts["island.islands.all_links"];
    for (PxU32 i = 0; i < islandPool.mCapacity; ++i)
        islandLinks.push_back(islandPool.mFreeElems[i]);
    Words& islandBitmap = next.parts["island.islands.bitmap"];
    islandBitmap.push_back(islandPool.mBitmapWordCount);
    for (PxU32 w = 0; w < islandPool.mBitmapWordCount; ++w)
        islandBitmap.push_back(islandPool.mBitmapWords[w]);
    for (PxU32 i = 0; i < islandPool.mCapacity; ++i)
    {
        if (freeIslands[i]) continue;
        const Island& item = islandPool.mElems[i];
        islandRows.push_back(i);
        islandRows.push_back(item.mStartNodeId);
        islandRows.push_back(item.mEndNodeId);
        islandRows.push_back(item.mStartEdgeId);
        islandRows.push_back(item.mEndEdgeId);
    }
    Words& changes = next.parts["island.change_queues"];
    appendActiveIslandIds(changes, islands.mNodeChangeManager.mCreatedNodes,
                          islands.mNodeChangeManager.mCreatedNodesSize);
    appendActiveIslandIds(changes, islands.mNodeChangeManager.mDeletedNodes,
                          islands.mNodeChangeManager.mDeletedNodesSize);
    appendActiveIslandIds(changes, islands.mEdgeChangeManager.mCreatedEdges,
                          islands.mEdgeChangeManager.mCreatedEdgesSize);
    appendActiveIslandIds(changes, islands.mEdgeChangeManager.mDeletedEdges,
                          islands.mEdgeChangeManager.mDeletedEdgesSize);
    appendActiveIslandIds(changes, islands.mEdgeChangeManager.mJoinedEdges,
                          islands.mEdgeChangeManager.mJoinedEdgesSize);
    appendActiveIslandIds(changes, islands.mEdgeChangeManager.mBrokenEdges,
                          islands.mEdgeChangeManager.mBrokenEdgesSize);
    Words& flags = next.parts["island.flags"];
    flags.push_back(islands.mEverythingAsleep ? 1u : 0u);
    flags.push_back(islands.mHasAnythingChanged ? 1u : 0u);
    flags.push_back(islands.mPerformIslandUpdate ? 1u : 0u);
    flags.push_back(islands.mNumAddedRBodies);
    flags.push_back(islands.mNumAddedArtics);
    flags.push_back(islands.mNumAddedKinematics);
    for (PxU32 i = 0; i < PxsIslandManager::MAX_NUM_EDGE_TYPES; ++i)
        flags.push_back(islands.mNumAddedEdges[i]);
    flags.push_back(islands.mNumEdgeReferencesToKinematic);
    flags.push_back(islands.mNumRequiredKinematicDuplicates);

    next.unsupported.push_back("free-slot payload and allocator block tails");
    next.unsupported.push_back("NPhase filter-pair pool and dirty hash set");
    next.unsupported.push_back("constraint and articulation graph payload");
    next.unsupported.push_back("contact solver and friction backing blocks");
    image = next;
    error.clear();
    return true;
}

} // namespace physx333_offline
