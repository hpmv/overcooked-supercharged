#include "IslandImage.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <utility>

#include "PxPhysicsAPI.h"
#include "NpScene.h"
#include "ScScene.h"
#include "ScInteractionScene.h"

// Restrict private access to this offline test translation unit. No vendor
// source or game binary is changed by the image implementation.
#define private public
#define protected public
#include "PxsContext.h"
#undef protected
#undef private

namespace oc2 { namespace offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "Island image requires Win32 PhysX");

struct ScalarRef
{
    std::string name;
    void* address;
    std::size_t size;
    bool topology;
};

struct BufferRef
{
    std::string name;
    void* address;
    std::size_t size;
};

struct Plan
{
    std::uintptr_t scene = 0;
    std::uintptr_t context = 0;
    std::uintptr_t manager = 0;
    std::uintptr_t scratchAllocator = 0;
    std::vector<ScalarRef> scalars;
    std::vector<BufferRef> buffers;
    std::vector<IslandImage::Binding> bindings;
};

template <typename T>
void scalar(Plan& plan, const std::string& name, T& value, bool topology = false)
{
    plan.scalars.push_back(ScalarRef{name, &value, sizeof(value), topology});
}

bool buffer(Plan& plan, const std::string& name, void* address,
            std::size_t count, std::size_t stride, std::string& error)
{
    const std::size_t limit = 256u * 1024u * 1024u;
    if (stride == 0 || count > limit / stride)
    {
        error = name + ": unreasonable capacity";
        return false;
    }
    const std::size_t bytes = count * stride;
    if (bytes != 0 && address == nullptr)
    {
        error = name + ": null storage";
        return false;
    }
    plan.buffers.push_back(BufferRef{name, address, bytes});
    return true;
}

template <typename T, typename Id>
bool appendPool(Plan& plan, const char* prefix, ElemManager<T, Id>& pool,
                std::string& error)
{
    const std::string base(prefix);
    scalar(plan, base + ".capacity", pool.mCapacity, true);
    scalar(plan, base + ".firstFree", pool.mNextFreeElem);
    scalar(plan, base + ".freeCount", pool.mNumFreeElems);
    if (pool.mCapacity > static_cast<PxU32>(T::INVALID) +
            (sizeof(Id) == 2 ? 1u : 0u) ||
        pool.mNumFreeElems > pool.mCapacity)
    {
        error = base + ": invalid capacity or free count";
        return false;
    }
    return buffer(plan, base + ".elements", pool.mElems,
                  pool.mCapacity, sizeof(T), error) &&
           buffer(plan, base + ".free", pool.mFreeElems,
                  pool.mCapacity, sizeof(Id), error);
}

bool appendNodeChanges(Plan& plan, NodeChangeManager& changes,
                       std::string& error)
{
    scalar(plan, "nodeChanges.capacity", changes.mCapacity, true);
    scalar(plan, "nodeChanges.defaultCapacity", changes.mDefaultCapacity, true);
    scalar(plan, "nodeChanges.createdSize", changes.mCreatedNodesSize);
    scalar(plan, "nodeChanges.deletedSize", changes.mDeletedNodesSize);
    if (changes.mCreatedNodesSize > changes.mCapacity ||
        changes.mDeletedNodesSize > changes.mCapacity)
    {
        error = "node change queue exceeds its capacity";
        return false;
    }
    return buffer(plan, "nodeChanges.created", changes.mCreatedNodes,
                  changes.mCapacity, sizeof(NodeType), error) &&
           buffer(plan, "nodeChanges.deleted", changes.mDeletedNodes,
                  changes.mCapacity, sizeof(NodeType), error);
}

bool appendEdgeChanges(Plan& plan, EdgeChangeManager& changes,
                       std::string& error)
{
    scalar(plan, "edgeChanges.capacity", changes.mCapacity, true);
    scalar(plan, "edgeChanges.defaultCapacity", changes.mDefaultCapacity, true);
    scalar(plan, "edgeChanges.createdSize", changes.mCreatedEdgesSize);
    scalar(plan, "edgeChanges.deletedSize", changes.mDeletedEdgesSize);
    scalar(plan, "edgeChanges.brokenSize", changes.mBrokenEdgesSize);
    scalar(plan, "edgeChanges.joinedSize", changes.mJoinedEdgesSize);
    if (changes.mCreatedEdgesSize > changes.mCapacity ||
        changes.mDeletedEdgesSize > changes.mCapacity ||
        changes.mBrokenEdgesSize > changes.mCapacity ||
        changes.mJoinedEdgesSize > changes.mCapacity)
    {
        error = "edge change queue exceeds its capacity";
        return false;
    }
    return buffer(plan, "edgeChanges.created", changes.mCreatedEdges,
                  changes.mCapacity, sizeof(EdgeType), error) &&
           buffer(plan, "edgeChanges.deleted", changes.mDeletedEdges,
                  changes.mCapacity, sizeof(EdgeType), error) &&
           buffer(plan, "edgeChanges.broken", changes.mBrokenEdges,
                  changes.mCapacity, sizeof(EdgeType), error) &&
           buffer(plan, "edgeChanges.joined", changes.mJoinedEdges,
                  changes.mCapacity, sizeof(EdgeType), error);
}

template <typename Id>
bool freeChain(const char* name, const Id* links, PxU32 capacity,
               PxU32 head, PxU32 expected, std::vector<bool>& free,
               std::string& error)
{
    free.assign(capacity, false);
    PxU32 id = head;
    PxU32 count = 0;
    while (id != static_cast<PxU32>(std::numeric_limits<Id>::max()))
    {
        if (id >= capacity || free[id])
        {
            error = std::string(name) + ": free chain is cyclic or out of range";
            return false;
        }
        free[id] = true;
        ++count;
        id = links[id];
    }
    if (count != expected)
    {
        error = std::string(name) + ": free chain length differs from count";
        return false;
    }
    return true;
}

bool sameBinding(const IslandImage::Binding& a, const IslandImage::Binding& b)
{
    return a.kind == b.kind && a.id == b.id && a.owner == b.owner &&
           a.endpoint0 == b.endpoint0 && a.endpoint1 == b.endpoint1 &&
           a.type == b.type;
}

bool appendBindings(PxsIslandManager& mgr, Plan& plan, std::string& error)
{
    NodeManager& nodes = mgr.mNodeManager;
    EdgeManager& edges = mgr.mEdgeManager;
    IslandManager& islands = mgr.mIslands;
    ArticulationRootManager& roots = mgr.mRootArticulationManager;
    std::vector<bool> freeNodes, freeEdges, freeIslands, freeRoots;
    if (!freeChain("nodes", nodes.mFreeElems, nodes.mCapacity,
                   nodes.mNextFreeElem, nodes.mNumFreeElems, freeNodes, error) ||
        !freeChain("edges", edges.mFreeElems, edges.mCapacity,
                   edges.mNextFreeElem, edges.mNumFreeElems, freeEdges, error) ||
        !freeChain("islands", islands.mFreeElems, islands.mCapacity,
                   islands.mNextFreeElem, islands.mNumFreeElems, freeIslands, error) ||
        !freeChain("articulation roots", roots.mFreeElems, roots.mCapacity,
                   roots.mNextFreeElem, roots.mNumFreeElems, freeRoots, error))
        return false;

    for (PxU32 i = 0; i < nodes.mCapacity; ++i)
    {
        if (i == static_cast<PxU32>(INVALID_NODE) || freeNodes[i]) continue;
        const Node& node = nodes.mElems[i];
        if (node.mIslandId != INVALID_ISLAND &&
            (node.mIslandId >= islands.mCapacity || freeIslands[node.mIslandId]))
        {
            error = "active node refers to a free or invalid island";
            return false;
        }
        IslandImage::Binding binding;
        binding.kind = IslandImage::Binding::Node;
        binding.id = i;
        binding.owner = reinterpret_cast<std::uintptr_t>(node.mRigidBodyOwner);
        binding.type = node.mFlags &
            (Node::eKINEMATIC | Node::eARTICULATED | Node::eARTICULATEDROOT);
        if (!binding.owner)
        {
            error = "active node has no owner or articulation handle";
            return false;
        }
        plan.bindings.push_back(binding);
    }
    for (PxU32 i = 0; i < edges.mCapacity; ++i)
    {
        if (i == static_cast<PxU32>(INVALID_EDGE) || freeEdges[i]) continue;
        const Edge& edge = edges.mElems[i];
        const NodeType a = edge.mNode1, b = edge.mNode2;
        if ((a == INVALID_NODE && b == INVALID_NODE) || a == b ||
            (a != INVALID_NODE && (a >= nodes.mCapacity || freeNodes[a])) ||
            (b != INVALID_NODE && (b >= nodes.mCapacity || freeNodes[b])))
        {
            error = "active edge has invalid node endpoints";
            return false;
        }
        IslandImage::Binding binding;
        binding.kind = IslandImage::Binding::Edge;
        binding.id = i;
        binding.owner = reinterpret_cast<std::uintptr_t>(edge.mContactManager) &
                        ~static_cast<std::uintptr_t>(Edge::EDGE_ALL_FLAGS);
        binding.endpoint0 = a;
        binding.endpoint1 = b;
        binding.type = reinterpret_cast<std::uintptr_t>(edge.mContactManager) &
                       Edge::EDGE_TYPE_CONSTRAINT_OR_ARTICULATION;
        plan.bindings.push_back(binding);
    }
    for (PxU32 i = 0; i < roots.mCapacity; ++i)
    {
        if (i == static_cast<PxU32>(INVALID_NODE) || freeRoots[i]) continue;
        const ArticulationRoot& root = roots.mElems[i];
        IslandImage::Binding binding;
        binding.kind = IslandImage::Binding::ArticulationRoot;
        binding.id = i;
        binding.owner = reinterpret_cast<std::uintptr_t>(root.mArticulationOwner);
        binding.endpoint0 = static_cast<std::uint32_t>(root.mArticulationLinkHandle);
        plan.bindings.push_back(binding);
    }
    for (PxU32 i = 0; i < islands.mCapacity; ++i)
    {
        if (i == static_cast<PxU32>(INVALID_ISLAND)) continue;
        const bool marked =
            (islands.mBitmapWords[i >> 5] & (1u << (i & 31u))) != 0;
        if (marked && freeIslands[i])
        {
            error = "island bitmap marks a free island";
            return false;
        }
        if (freeIslands[i]) continue;
        const Island& island = islands.mElems[i];
        if ((island.mStartNodeId == INVALID_NODE) !=
                (island.mEndNodeId == INVALID_NODE) ||
            (island.mStartEdgeId == INVALID_EDGE) !=
                (island.mEndEdgeId == INVALID_EDGE) ||
            (island.mStartNodeId != INVALID_NODE &&
                (island.mStartNodeId >= nodes.mCapacity ||
                 freeNodes[island.mStartNodeId] ||
                 island.mEndNodeId >= nodes.mCapacity ||
                 freeNodes[island.mEndNodeId])) ||
            (island.mStartEdgeId != INVALID_EDGE &&
                (island.mStartEdgeId >= edges.mCapacity ||
                 freeEdges[island.mStartEdgeId] ||
                 island.mEndEdgeId >= edges.mCapacity ||
                 freeEdges[island.mEndEdgeId])))
        {
            error = "island endpoints refer to free or invalid elements";
            return false;
        }
    }
    return true;
}

std::size_t align16(std::size_t value)
{
    return (value + 15u) & ~std::size_t(15u);
}

bool validateBackingLayout(const PxsIslandManager& mgr, std::string& error)
{
    const NodeManager& nodes = mgr.mNodeManager;
    const EdgeManager& edges = mgr.mEdgeManager;
    const IslandManager& islands = mgr.mIslands;
    const ArticulationRootManager& roots = mgr.mRootArticulationManager;
    const auto* nodeBase = reinterpret_cast<const unsigned char*>(nodes.mElems);
    const auto* edgeBase = reinterpret_cast<const unsigned char*>(edges.mElems);
    const auto* islandBase = reinterpret_cast<const unsigned char*>(islands.mElems);
    const auto* rootBase = reinterpret_cast<const unsigned char*>(roots.mElems);
    if ((nodes.mCapacity && !nodeBase) || (edges.mCapacity && !edgeBase) ||
        (islands.mCapacity && !islandBase) || (roots.mCapacity && !rootBase))
    {
        error = "island pool has a missing backing allocation";
        return false;
    }
    if (nodeBase)
    {
        std::size_t offset = align16(sizeof(Node) * nodes.mCapacity);
        if (reinterpret_cast<const unsigned char*>(nodes.mFreeElems) != nodeBase + offset)
        {
            error = "node free-list column is outside expected backing layout";
            return false;
        }
        offset += align16(sizeof(NodeType) * nodes.mCapacity);
        if (reinterpret_cast<const unsigned char*>(nodes.mNextNodeIds) != nodeBase + offset)
        {
            error = "node next-ID column is outside expected backing layout";
            return false;
        }
        offset += align16(sizeof(NodeType) * nodes.mCapacity);
        const std::size_t words = (nodes.mCapacity + 31u) >> 5;
        for (PxU32 i = 0; i < NodeManager::eMAX_NB_BITMAPS; ++i)
        {
            if (reinterpret_cast<const unsigned char*>(nodes.mBitmapWords[i]) !=
                nodeBase + offset + i * align16(sizeof(PxU32) * words))
            {
                error = "node bitmap is outside expected backing layout";
                return false;
            }
        }
    }
    if (edgeBase)
    {
        std::size_t offset = align16(sizeof(Edge) * edges.mCapacity);
        if (reinterpret_cast<const unsigned char*>(edges.mFreeElems) != edgeBase + offset)
        {
            error = "edge free-list column is outside expected backing layout";
            return false;
        }
        offset += align16(sizeof(EdgeType) * edges.mCapacity);
        if (reinterpret_cast<const unsigned char*>(edges.mNextEdgeIds) != edgeBase + offset)
        {
            error = "edge next-ID column is outside expected backing layout";
            return false;
        }
    }
    if (islandBase)
    {
        std::size_t offset = align16(sizeof(Island) * islands.mCapacity);
        if (reinterpret_cast<const unsigned char*>(islands.mFreeElems) != islandBase + offset)
        {
            error = "island free-list column is outside expected backing layout";
            return false;
        }
        offset += align16(sizeof(IslandType) * islands.mCapacity);
        if (reinterpret_cast<const unsigned char*>(islands.mBitmapWords) !=
            islandBase + offset)
        {
            error = "island bitmap is outside expected backing layout";
            return false;
        }
    }
    if (rootBase && reinterpret_cast<const unsigned char*>(roots.mFreeElems) !=
        rootBase + align16(sizeof(ArticulationRoot) * roots.mCapacity))
    {
        error = "articulation-root free list is outside expected backing layout";
        return false;
    }
    const NodeChangeManager& nodeChanges = mgr.mNodeChangeManager;
    if (nodeChanges.mCapacity &&
        (!nodeChanges.mCreatedNodes || !nodeChanges.mDeletedNodes ||
         nodeChanges.mDeletedNodes !=
             nodeChanges.mCreatedNodes + nodeChanges.mCapacity))
    {
        error = "node change queues do not share the expected backing";
        return false;
    }
    const EdgeChangeManager& edgeChanges = mgr.mEdgeChangeManager;
    if (edgeChanges.mCapacity)
    {
        const EdgeType* const base = edgeChanges.mCreatedEdges;
        const EdgeType* const columns[] = {
            edgeChanges.mDeletedEdges, edgeChanges.mBrokenEdges,
            edgeChanges.mJoinedEdges
        };
        bool occupied[4] = {true, false, false, false};
        for (const EdgeType* column : columns)
        {
            if (!base || !column)
            {
                error = "edge change queue backing is missing";
                return false;
            }
            const std::uintptr_t delta =
                reinterpret_cast<std::uintptr_t>(column) -
                reinterpret_cast<std::uintptr_t>(base);
            const std::uintptr_t stride =
                static_cast<std::uintptr_t>(edgeChanges.mCapacity) * sizeof(EdgeType);
            if (!stride || delta % stride || delta / stride > 3u ||
                occupied[delta / stride])
            {
                error = "edge change queues overlap or escape their backing";
                return false;
            }
            occupied[delta / stride] = true;
        }
    }
    return true;
}

bool validateDisjointRanges(const Plan& plan, std::string& error)
{
    struct Range { std::string name; std::uintptr_t begin, end; };
    std::vector<Range> ranges;
    for (const auto& ref : plan.scalars)
        ranges.push_back(Range{ref.name,
            reinterpret_cast<std::uintptr_t>(ref.address),
            reinterpret_cast<std::uintptr_t>(ref.address) + ref.size});
    for (const auto& ref : plan.buffers)
    {
        if (!ref.size) continue;
        const std::uintptr_t begin = reinterpret_cast<std::uintptr_t>(ref.address);
        if (!begin || begin + ref.size < begin)
        {
            error = ref.name + ": invalid backing address range";
            return false;
        }
        ranges.push_back(Range{ref.name, begin, begin + ref.size});
    }
    std::sort(ranges.begin(), ranges.end(),
              [](const Range& a, const Range& b) { return a.begin < b.begin; });
    for (std::size_t i = 1; i < ranges.size(); ++i)
        if (ranges[i].begin < ranges[i - 1].end)
        {
            error = "island image write ranges overlap: " +
                    ranges[i - 1].name + " and " + ranges[i].name;
            return false;
        }
    return true;
}

bool buildPlan(PxScene& scene, Plan& plan, std::string& error,
               bool allowPendingChanges = false)
{
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
    PxsIslandManager& mgr = context->getIslandManager();
    plan.scene = reinterpret_cast<std::uintptr_t>(&scene);
    plan.context = reinterpret_cast<std::uintptr_t>(context);
    plan.manager = reinterpret_cast<std::uintptr_t>(&mgr);
    plan.scratchAllocator = reinterpret_cast<std::uintptr_t>(&mgr.mScratchAllocator);
    if (!validateBackingLayout(mgr, error)) return false;

    ProcessSleepingIslandsComputeData& compute = mgr.mProcessSleepingIslandsComputeData;
    IslandManagerUpdateWorkBuffers& work = mgr.mIslandManagerUpdateWorkBuffers;
    if ((!allowPendingChanges &&
         (mgr.mPerformIslandUpdate || mgr.mHasAnythingChanged ||
          mgr.mNodeChangeManager.mCreatedNodesSize ||
          mgr.mNodeChangeManager.mDeletedNodesSize ||
          mgr.mEdgeChangeManager.mCreatedEdgesSize ||
          mgr.mEdgeChangeManager.mDeletedEdgesSize ||
          mgr.mEdgeChangeManager.mJoinedEdgesSize ||
          mgr.mEdgeChangeManager.mBrokenEdgesSize)) ||
        compute.mDataBlock || compute.mBodiesToWakeOrSleep ||
        compute.mNarrowPhaseContactManagers || compute.mSolverBodyMap ||
        compute.mSolverKinematics || compute.mSolverBodies ||
        compute.mSolverArticulations || compute.mSolverArticulationOwners ||
        compute.mSolverContactManagers || compute.mSolverConstraints ||
        compute.mIslandIndices || work.mKinematicProxySourceNodeIds ||
        work.mKinematicProxyNextNodeIds || work.mKinematicProxyLastNodeIds ||
        work.mGraphNextNodes || work.mGraphStartIslands ||
        work.mGraphNextIslands)
    {
        error = "island manager is not at a post-fetch quiescent point";
        return false;
    }
    for (PxU32 i = 0; i < IslandManagerUpdateWorkBuffers::eMAX_NB_BITMAPS; ++i)
        if (work.mBitmapWords[i])
        {
            error = "island work bitmap remains active";
            return false;
        }

    scalar(plan, "rigidBodyOffset", mgr.mRigidBodyOffset, true);
    scalar(plan, "eventProfiler", mgr.mEventProfiler, true);
    scalar(plan, "numRigidBodies", mgr.mNumAddedRBodies);
    scalar(plan, "numArticulations", mgr.mNumAddedArtics);
    scalar(plan, "numKinematics", mgr.mNumAddedKinematics);
    for (PxU32 i = 0; i < PxsIslandManager::MAX_NUM_EDGE_TYPES; ++i)
        scalar(plan, "numEdges" + std::to_string(i), mgr.mNumAddedEdges[i]);
    scalar(plan, "numKinematicEdgeReferences", mgr.mNumEdgeReferencesToKinematic);
    scalar(plan, "numRequiredKinematicDuplicates", mgr.mNumRequiredKinematicDuplicates);
    scalar(plan, "everythingAsleep", mgr.mEverythingAsleep);
    scalar(plan, "hasAnythingChanged", mgr.mHasAnythingChanged);
    scalar(plan, "performIslandUpdate", mgr.mPerformIslandUpdate);
    scalar(plan, "workBufferSize", mgr.mBufferSize, true);

    if (!appendPool(plan, "nodes", mgr.mNodeManager, error) ||
        !appendPool(plan, "edges", mgr.mEdgeManager, error) ||
        !appendPool(plan, "islands", mgr.mIslands, error) ||
        !appendPool(plan, "articulationRoots", mgr.mRootArticulationManager, error))
        return false;
    if (!buffer(plan, "nodes.next", mgr.mNodeManager.mNextNodeIds,
                mgr.mNodeManager.mCapacity, sizeof(NodeType), error) ||
        !buffer(plan, "edges.next", mgr.mEdgeManager.mNextEdgeIds,
                mgr.mEdgeManager.mCapacity, sizeof(EdgeType), error))
        return false;
    const PxU32 nodeWords = (mgr.mNodeManager.mCapacity + 31u) >> 5;
    for (PxU32 i = 0; i < NodeManager::eMAX_NB_BITMAPS; ++i)
    {
        if (mgr.mNodeManager.mBitmapWordCounts[i] != nodeWords ||
            mgr.mNodeManager.mBitmaps[i]->getWords() !=
                mgr.mNodeManager.mBitmapWords[i] ||
            mgr.mNodeManager.mBitmaps[i]->getWordCount() != nodeWords)
        {
            error = "node bitmap metadata is inconsistent";
            return false;
        }
        scalar(plan, "nodeBitmapWords" + std::to_string(i),
               mgr.mNodeManager.mBitmapWordCounts[i], true);
        if (!buffer(plan, "nodes.bitmap" + std::to_string(i),
                    mgr.mNodeManager.mBitmapWords[i], nodeWords,
                    sizeof(PxU32), error))
            return false;
    }
    const PxU32 islandWords = mgr.mIslands.mCapacity >> 5;
    if ((mgr.mIslands.mCapacity & 31u) ||
        mgr.mIslands.mBitmapWordCount != islandWords ||
        mgr.mIslands.mBitMap->getWords() != mgr.mIslands.mBitmapWords ||
        mgr.mIslands.mBitMap->getWordCount() != islandWords)
    {
        error = "island bitmap metadata is inconsistent";
        return false;
    }
    scalar(plan, "islandBitmapWords", mgr.mIslands.mBitmapWordCount, true);
    if (!buffer(plan, "islands.bitmap", mgr.mIslands.mBitmapWords,
                islandWords, sizeof(PxU32), error) ||
        !appendNodeChanges(plan, mgr.mNodeChangeManager, error) ||
        !appendEdgeChanges(plan, mgr.mEdgeChangeManager, error) ||
        !buffer(plan, "compute", &compute, 1, sizeof(compute), error) ||
        !buffer(plan, "islandObjects", &mgr.mIslandObjects,
                1, sizeof(mgr.mIslandObjects), error) ||
        !buffer(plan, "work", &work, 1, sizeof(work), error) ||
        !buffer(plan, "workBuffer", mgr.mBuffer, mgr.mBufferSize, 1, error))
        return false;

    return validateDisjointRanges(plan, error) &&
           appendBindings(mgr, plan, error);
}

const IslandImage::Scalar* findScalar(const IslandImage& image, const char* name)
{
    for (const auto& s : image.scalars) if (s.name == name) return &s;
    return nullptr;
}

const IslandImage::Buffer* findBuffer(const IslandImage& image, const char* name)
{
    for (const auto& b : image.buffers) if (b.name == name) return &b;
    return nullptr;
}

template <typename T>
bool readScalar(const IslandImage& image, const char* name, T& value)
{
    const IslandImage::Scalar* s = findScalar(image, name);
    if (!s || s->bytes.size() != sizeof(T)) return false;
    std::memcpy(&value, s->bytes.data(), sizeof(T));
    return true;
}

template <typename T>
bool readElement(const IslandImage::Buffer& bufferImage, std::size_t i, T& value)
{
    if (i >= bufferImage.bytes.size() / sizeof(T)) return false;
    std::memcpy(&value, bufferImage.bytes.data() + i * sizeof(T), sizeof(T));
    return true;
}

template <typename Id>
bool validateImagePool(const IslandImage& image, const char* prefix,
                       std::vector<bool>& free, std::string& error)
{
    const std::string base(prefix);
    PxU32 capacity = 0, first = 0, count = 0;
    const auto* elems = findBuffer(image, (base + ".elements").c_str());
    const auto* links = findBuffer(image, (base + ".free").c_str());
    if (!readScalar(image, (base + ".capacity").c_str(), capacity) ||
        !readScalar(image, (base + ".firstFree").c_str(), first) ||
        !readScalar(image, (base + ".freeCount").c_str(), count) ||
        !elems || !links || links->bytes.size() != capacity * sizeof(Id) ||
        count > capacity)
    {
        error = base + ": malformed saved pool";
        return false;
    }
    free.assign(capacity, false);
    PxU32 id = first;
    PxU32 seen = 0;
    while (id != static_cast<PxU32>(std::numeric_limits<Id>::max()))
    {
        if (id >= capacity || free[id])
        {
            error = base + ": malformed saved free chain";
            return false;
        }
        free[id] = true;
        ++seen;
        Id next;
        if (!readElement(*links, id, next)) return false;
        id = next;
    }
    if (seen != count)
    {
        error = base + ": saved free count differs from chain";
        return false;
    }
    return true;
}

bool validateSavedImage(const IslandImage& image, std::string& error)
{
    std::vector<bool> freeNodes, freeEdges, freeIslands, freeRoots;
    if (!validateImagePool<NodeType>(image, "nodes", freeNodes, error) ||
        !validateImagePool<EdgeType>(image, "edges", freeEdges, error) ||
        !validateImagePool<IslandType>(image, "islands", freeIslands, error) ||
        !validateImagePool<NodeType>(image, "articulationRoots", freeRoots, error))
        return false;
    const auto* nodes = findBuffer(image, "nodes.elements");
    const auto* edges = findBuffer(image, "edges.elements");
    const auto* roots = findBuffer(image, "articulationRoots.elements");
    const auto* islands = findBuffer(image, "islands.elements");
    const auto* bits = findBuffer(image, "islands.bitmap");
    if (!nodes || !edges || !roots || !islands || !bits ||
        nodes->bytes.size() != freeNodes.size() * sizeof(Node) ||
        edges->bytes.size() != freeEdges.size() * sizeof(Edge) ||
        roots->bytes.size() != freeRoots.size() * sizeof(ArticulationRoot) ||
        islands->bytes.size() != freeIslands.size() * sizeof(Island) ||
        bits->bytes.size() != (freeIslands.size() >> 5) * sizeof(PxU32))
    {
        error = "saved island pool array layout is malformed";
        return false;
    }
    std::vector<IslandImage::Binding> actual;
    for (PxU32 i = 0; i < freeNodes.size(); ++i)
    {
        if (i == static_cast<PxU32>(INVALID_NODE) || freeNodes[i]) continue;
        Node node;
        readElement(*nodes, i, node);
        if (node.mIslandId != INVALID_ISLAND &&
            (node.mIslandId >= freeIslands.size() || freeIslands[node.mIslandId]))
        {
            error = "saved node refers to an invalid island";
            return false;
        }
        IslandImage::Binding b;
        b.kind = IslandImage::Binding::Node;
        b.id = i;
        b.owner = reinterpret_cast<std::uintptr_t>(node.mRigidBodyOwner);
        b.type = node.mFlags &
            (Node::eKINEMATIC | Node::eARTICULATED | Node::eARTICULATEDROOT);
        actual.push_back(b);
    }
    for (PxU32 i = 0; i < freeEdges.size(); ++i)
    {
        if (i == static_cast<PxU32>(INVALID_EDGE) || freeEdges[i]) continue;
        Edge edge;
        readElement(*edges, i, edge);
        if ((edge.mNode1 == INVALID_NODE && edge.mNode2 == INVALID_NODE) ||
            edge.mNode1 == edge.mNode2 ||
            (edge.mNode1 != INVALID_NODE &&
             (edge.mNode1 >= freeNodes.size() || freeNodes[edge.mNode1])) ||
            (edge.mNode2 != INVALID_NODE &&
             (edge.mNode2 >= freeNodes.size() || freeNodes[edge.mNode2])))
        {
            error = "saved edge has invalid node endpoints";
            return false;
        }
        IslandImage::Binding b;
        b.kind = IslandImage::Binding::Edge;
        b.id = i;
        b.owner = reinterpret_cast<std::uintptr_t>(edge.mContactManager) &
                  ~static_cast<std::uintptr_t>(Edge::EDGE_ALL_FLAGS);
        b.endpoint0 = edge.mNode1;
        b.endpoint1 = edge.mNode2;
        b.type = reinterpret_cast<std::uintptr_t>(edge.mContactManager) &
                 Edge::EDGE_TYPE_CONSTRAINT_OR_ARTICULATION;
        actual.push_back(b);
    }
    for (PxU32 i = 0; i < freeRoots.size(); ++i)
    {
        if (i == static_cast<PxU32>(INVALID_NODE) || freeRoots[i]) continue;
        ArticulationRoot root;
        readElement(*roots, i, root);
        IslandImage::Binding b;
        b.kind = IslandImage::Binding::ArticulationRoot;
        b.id = i;
        b.owner = reinterpret_cast<std::uintptr_t>(root.mArticulationOwner);
        b.endpoint0 = static_cast<std::uint32_t>(root.mArticulationLinkHandle);
        actual.push_back(b);
    }
    for (PxU32 i = 0; i < freeIslands.size(); ++i)
    {
        if (i == static_cast<PxU32>(INVALID_ISLAND)) continue;
        PxU32 word = 0;
        readElement(*bits, i >> 5, word);
        const bool marked = (word & (1u << (i & 31u))) != 0;
        if (freeIslands[i] && marked)
        {
            error = "saved island bitmap marks a free island";
            return false;
        }
        if (freeIslands[i]) continue;
        Island island;
        readElement(*islands, i, island);
        if ((island.mStartNodeId == INVALID_NODE) !=
                (island.mEndNodeId == INVALID_NODE) ||
            (island.mStartEdgeId == INVALID_EDGE) !=
                (island.mEndEdgeId == INVALID_EDGE) ||
            (island.mStartNodeId != INVALID_NODE &&
                (island.mStartNodeId >= freeNodes.size() ||
                 freeNodes[island.mStartNodeId] ||
                 island.mEndNodeId >= freeNodes.size() ||
                 freeNodes[island.mEndNodeId])) ||
            (island.mStartEdgeId != INVALID_EDGE &&
                (island.mStartEdgeId >= freeEdges.size() ||
                 freeEdges[island.mStartEdgeId] ||
                 island.mEndEdgeId >= freeEdges.size() ||
                 freeEdges[island.mEndEdgeId])))
        {
            error = "saved island endpoint refers to a free element";
            return false;
        }
    }
    if (actual.size() != image.bindings.size())
    {
        error = "saved active binding count is malformed";
        return false;
    }
    for (std::size_t i = 0; i < actual.size(); ++i)
        if (!sameBinding(actual[i], image.bindings[i]))
        {
            error = "saved binding does not match saved pool bytes";
            return false;
        }
    const auto* compute = findBuffer(image, "compute");
    const auto* work = findBuffer(image, "work");
    if (!compute || compute->bytes.size() != sizeof(ProcessSleepingIslandsComputeData) ||
        !work || work->bytes.size() != sizeof(IslandManagerUpdateWorkBuffers))
    {
        error = "saved transient metadata is malformed";
        return false;
    }
    ProcessSleepingIslandsComputeData c;
    IslandManagerUpdateWorkBuffers w;
    std::memcpy(&c, compute->bytes.data(), sizeof(c));
    std::memcpy(&w, work->bytes.data(), sizeof(w));
    if (c.mDataBlock || c.mBodiesToWakeOrSleep ||
        c.mNarrowPhaseContactManagers || c.mSolverBodyMap ||
        c.mSolverKinematics || c.mSolverBodies ||
        c.mSolverArticulations || c.mSolverArticulationOwners ||
        c.mSolverContactManagers || c.mSolverConstraints || c.mIslandIndices ||
        w.mKinematicProxySourceNodeIds || w.mKinematicProxyNextNodeIds ||
        w.mKinematicProxyLastNodeIds || w.mGraphNextNodes ||
        w.mGraphStartIslands || w.mGraphNextIslands)
    {
        error = "saved transient pointers are not quiescent";
        return false;
    }
    for (PxU32 i = 0; i < IslandManagerUpdateWorkBuffers::eMAX_NB_BITMAPS; ++i)
        if (w.mBitmapWords[i])
        {
            error = "saved work bitmap remains active";
            return false;
        }
    return true;
}

bool validateSavedPointers(const IslandImage& image,
                           const PxsIslandManager& live, std::string& error)
{
    const auto* workBytes = findBuffer(image, "work");
    const auto* objectBytes = findBuffer(image, "islandObjects");
    if (!workBytes || workBytes->bytes.size() != sizeof(IslandManagerUpdateWorkBuffers) ||
        !objectBytes || objectBytes->bytes.size() != sizeof(PxsIslandObjects))
    {
        error = "saved island pointer metadata is malformed";
        return false;
    }
    IslandManagerUpdateWorkBuffers work;
    PxsIslandObjects objects;
    std::memcpy(&work, workBytes->bytes.data(), sizeof(work));
    std::memcpy(&objects, objectBytes->bytes.data(), sizeof(objects));
    for (PxU32 i = 0; i < IslandManagerUpdateWorkBuffers::eMAX_NB_BITMAPS; ++i)
    {
        if (work.mBitmap[i] != live.mIslandManagerUpdateWorkBuffers.mBitmap[i] ||
            work.mBitmap[i] != reinterpret_cast<const Cm::BitMap*>(
                live.mIslandManagerUpdateWorkBuffers.mBitmapBuffer[i]))
        {
            error = "saved island work bitmap object identity changed";
            return false;
        }
    }
    const std::uintptr_t begin = reinterpret_cast<std::uintptr_t>(live.mBuffer);
    const std::uintptr_t end = begin + live.mBufferSize;
    if (end < begin)
    {
        error = "island work allocation address wraps";
        return false;
    }
    const void* pointers[] = {
        objects.bodies, objects.articulations, objects.articulationOwners,
        objects.contactManagers, objects.constraints
    };
    for (const void* pointer : pointers)
    {
        const std::uintptr_t value = reinterpret_cast<std::uintptr_t>(pointer);
        if (value && (!begin || value < begin || value > end))
        {
            error = "saved solver island pointer is outside the same work allocation";
            return false;
        }
    }
    return true;
}

void writeImage(const Plan& target, const IslandImage& image)
{
    for (std::size_t i = 0; i < target.buffers.size(); ++i)
        if (target.buffers[i].size)
            std::memcpy(target.buffers[i].address,
                        image.buffers[i].bytes.data(), target.buffers[i].size);
    for (std::size_t i = 0; i < target.scalars.size(); ++i)
        if (!target.scalars[i].topology)
            std::memcpy(target.scalars[i].address,
                        image.scalars[i].bytes.data(), target.scalars[i].size);
}

} // namespace

bool IslandImage::equals(const IslandImage& other, std::string& firstDifference) const
{
    if (scene != other.scene || context != other.context ||
        manager != other.manager || scratchAllocator != other.scratchAllocator)
    {
        firstDifference = "island manager identity";
        return false;
    }
    if (bindings.size() != other.bindings.size() ||
        scalars.size() != other.scalars.size() ||
        buffers.size() != other.buffers.size())
    {
        firstDifference = "island image inventory";
        return false;
    }
    for (std::size_t i = 0; i < bindings.size(); ++i)
        if (!sameBinding(bindings[i], other.bindings[i]))
        {
            firstDifference = "island binding " + std::to_string(i);
            return false;
        }
    for (std::size_t i = 0; i < scalars.size(); ++i)
        if (scalars[i].name != other.scalars[i].name ||
            scalars[i].address != other.scalars[i].address ||
            scalars[i].topology != other.scalars[i].topology ||
            scalars[i].bytes != other.scalars[i].bytes)
        {
            firstDifference = scalars[i].name;
            return false;
        }
    for (std::size_t i = 0; i < buffers.size(); ++i)
        if (buffers[i].name != other.buffers[i].name ||
            buffers[i].address != other.buffers[i].address ||
            buffers[i].bytes != other.buffers[i].bytes)
        {
            firstDifference = buffers[i].name;
            return false;
        }
    return true;
}

bool captureIslandImpl(PxScene& scene, IslandImage& image,
                       std::string& error, bool allowPendingChanges)
{
    error.clear();
    Plan plan;
    if (!buildPlan(scene, plan, error, allowPendingChanges)) return false;
    IslandImage fresh;
    fresh.scene = plan.scene;
    fresh.context = plan.context;
    fresh.manager = plan.manager;
    fresh.scratchAllocator = plan.scratchAllocator;
    fresh.bindings = std::move(plan.bindings);
    for (const ScalarRef& ref : plan.scalars)
    {
        IslandImage::Scalar s;
        s.name = ref.name;
        s.address = reinterpret_cast<std::uintptr_t>(ref.address);
        s.topology = ref.topology;
        const unsigned char* p = static_cast<const unsigned char*>(ref.address);
        s.bytes.assign(p, p + ref.size);
        fresh.scalars.push_back(std::move(s));
    }
    for (const BufferRef& ref : plan.buffers)
    {
        IslandImage::Buffer b;
        b.name = ref.name;
        b.address = reinterpret_cast<std::uintptr_t>(ref.address);
        if (ref.size)
        {
            const unsigned char* p = static_cast<const unsigned char*>(ref.address);
            b.bytes.assign(p, p + ref.size);
        }
        fresh.buffers.push_back(std::move(b));
    }
    if (!validateSavedImage(fresh, error)) return false;
    image = std::move(fresh);
    return true;
}

bool CaptureIsland(PxScene& scene, IslandImage& image, std::string& error)
{
    return captureIslandImpl(scene, image, error, false);
}

bool restoreIslandImpl(PxScene& scene, const IslandImage& image,
                       std::string& error, bool allowPendingChanges)
{
    error.clear();
    Plan live;
    if (!buildPlan(scene, live, error, allowPendingChanges)) return false;
    if (image.scene != live.scene || image.context != live.context ||
        image.manager != live.manager ||
        image.scratchAllocator != live.scratchAllocator)
    {
        error = "island image belongs to another scene or manager";
        return false;
    }
    if (image.bindings.size() != live.bindings.size())
    {
        error = "island active node/edge/root topology changed";
        return false;
    }
    for (std::size_t i = 0; i < live.bindings.size(); ++i)
        if (!sameBinding(image.bindings[i], live.bindings[i]))
        {
            error = "island actor or contact-manager binding changed at slot " +
                    std::to_string(live.bindings[i].id);
            return false;
        }
    if (image.scalars.size() != live.scalars.size() ||
        image.buffers.size() != live.buffers.size())
    {
        error = "island image inventory differs from live scene";
        return false;
    }
    for (std::size_t i = 0; i < live.scalars.size(); ++i)
    {
        const ScalarRef& target = live.scalars[i];
        const IslandImage::Scalar& source = image.scalars[i];
        if (source.name != target.name ||
            source.address != reinterpret_cast<std::uintptr_t>(target.address) ||
            source.topology != target.topology ||
            source.bytes.size() != target.size)
        {
            error = "island scalar layout changed at " + target.name;
            return false;
        }
        if (target.topology &&
            std::memcmp(target.address, source.bytes.data(), target.size) != 0)
        {
            error = "island allocation topology changed at " + target.name;
            return false;
        }
    }
    for (std::size_t i = 0; i < live.buffers.size(); ++i)
    {
        const BufferRef& target = live.buffers[i];
        const IslandImage::Buffer& source = image.buffers[i];
        if (source.name != target.name ||
            source.address != reinterpret_cast<std::uintptr_t>(target.address) ||
            source.bytes.size() != target.size)
        {
            error = "island backing allocation changed at " + target.name;
            return false;
        }
    }
    if (!validateSavedImage(image, error) ||
        !validateSavedPointers(image,
            *reinterpret_cast<PxsIslandManager*>(live.manager), error))
        return false;

    IslandImage rollback;
    if (!captureIslandImpl(scene, rollback, error,
                           allowPendingChanges)) return false;
    writeImage(live, image);
    IslandImage observed;
    std::string verifyError;
    bool verified = false;
    try
    {
        verified = captureIslandImpl(scene, observed, verifyError,
                                     allowPendingChanges) &&
                   image.equals(observed, verifyError);
    }
    catch (...)
    {
        verifyError = "postwrite capture threw an exception";
    }
    if (!verified)
    {
        writeImage(live, rollback);
        IslandImage reverted;
        std::string rollbackError;
        if (!captureIslandImpl(scene, reverted, rollbackError,
                               allowPendingChanges) ||
            !rollback.equals(reverted, rollbackError))
            std::abort();
        error = "island restore failed verification and rolled back: " + verifyError;
        return false;
    }
    return true;
}

bool RestoreIsland(PxScene& scene, const IslandImage& image, std::string& error)
{
    return restoreIslandImpl(scene, image, error, false);
}

bool RestoreIslandForJoin(PxScene& scene, const IslandImage& image,
                          std::string& error)
{
    return restoreIslandImpl(scene, image, error, true);
}

}} // namespace oc2::offline
