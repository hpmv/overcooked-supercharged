#include "CrossSwapImage.h"
#include "CrossSwapBridge.h"

#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <limits>
#include <string>

#include <windows.h>
#include "PxPhysicsAPI.h"

// This access is confined to the disposable source-built offline experiment.
#define private public
#define protected public
#include "NpScene.h"
#include "SqAABBPruner.h"
#include "SqAABBTree.h"
#undef protected
#undef private

namespace oc2 { namespace offline {
namespace {

using namespace physx;
using physx333_offline::CrossSwapTreeSpecV1;
static_assert(sizeof(void*) == 4, "Cross-swap rewind requires Win32");
static_assert(offsetof(Sq::AABBTreeNode, mBitfield) == 16 &&
              sizeof(Sq::AABBTreeNode) == 24,
              "Unexpected compressed-node padding layout");

typedef unsigned (__cdecl* CreateTree)(const CrossSwapTreeSpecV1*, void**);
typedef void (__cdecl* DestroyTree)(void*);
typedef unsigned (__cdecl* AllocCopy)(const void*, std::uint32_t, void**);
typedef void (__cdecl* FreeCopy)(void*);

struct Bridge
{
    CreateTree create = nullptr;
    DestroyTree destroy = nullptr;
    AllocCopy alloc = nullptr;
    FreeCopy free = nullptr;
};

FARPROC exported(HMODULE dll, const char* name, const char* decorated)
{
    FARPROC result = GetProcAddress(dll, name);
    return result ? result : GetProcAddress(dll, decorated);
}

bool bridge(Bridge& out, std::string& error)
{
    HMODULE dll = GetModuleHandleA("PhysX3_x86.dll");
    if (dll)
    {
        out.create = reinterpret_cast<CreateTree>(exported(
            dll, "oc2_physx333_query_tree_create_v1",
            "_oc2_physx333_query_tree_create_v1"));
        out.destroy = reinterpret_cast<DestroyTree>(exported(
            dll, "oc2_physx333_query_tree_destroy_v1",
            "_oc2_physx333_query_tree_destroy_v1"));
        out.alloc = reinterpret_cast<AllocCopy>(exported(
            dll, "oc2_physx333_query_alloc_copy_v1",
            "_oc2_physx333_query_alloc_copy_v1"));
        out.free = reinterpret_cast<FreeCopy>(exported(
            dll, "oc2_physx333_query_free_v1",
            "_oc2_physx333_query_free_v1"));
    }
    if (!out.create || !out.destroy || !out.alloc || !out.free)
    {
        error = "isolated source-owned cross-swap bridge is unavailable";
        return false;
    }
    return true;
}

const QueryImage::Field* field(const QueryImage& image, const std::string& name)
{
    for (const QueryImage::Field& row : image.fields)
        if (row.name == name) return &row;
    return nullptr;
}

bool word(const QueryImage& image, const char* name, PxU32& out)
{
    const QueryImage::Field* row = field(image, name);
    if (!row || row->bytes.size() != sizeof(out)) return false;
    std::memcpy(&out, row->bytes.data(), sizeof(out));
    return true;
}

void hashU64(std::uint64_t& hash, std::uint64_t value)
{
    for (unsigned i = 0; i < 8; ++i)
    {
        hash ^= (value >> (i * 8)) & 0xff;
        hash *= UINT64_C(1099511628211);
    }
}

std::uint64_t seal(const QueryImage& image)
{
    std::uint64_t hash = UINT64_C(14695981039346656037);
    hashU64(hash, image.scene);
    hashU64(hash, image.fields.size());
    for (const QueryImage::Field& row : image.fields)
    {
        hashU64(hash, row.address);
        hashU64(hash, row.invariant);
        hashU64(hash, row.name.size());
        for (char byte : row.name)
        {
            hash ^= static_cast<std::uint8_t>(byte);
            hash *= UINT64_C(1099511628211);
        }
        hashU64(hash, row.bytes.size());
        for (std::uint8_t byte : row.bytes)
        {
            hash ^= byte;
            hash *= UINT64_C(1099511628211);
        }
    }
    return hash;
}

bool owned(const std::string& name)
{
    return name.compare(0, 13, "dynamic.tree.") == 0 ||
           name.compare(0, 23, "dynamic.newTreeStorage.") == 0 ||
           name == "dynamic.cachedBoxStorage";
}

bool lifecycle(const std::string& name)
{
    return name == "dynamic.currentTree" ||
           name == "dynamic.newTree" ||
           name == "dynamic.cachedBoxes" ||
           name == "dynamic.progress" ||
           name == "dynamic.builderInputPointer" ||
           name == "dynamic.builderNodePointer" ||
           name == "dynamic.saveFixups";
}

bool pointerValue(const std::string& name)
{
    return name == "dynamic.currentTree" ||
           name == "dynamic.newTree" ||
           name == "dynamic.cachedBoxes" ||
           name == "dynamic.builderInputPointer" ||
           name == "dynamic.builderNodePointer" ||
           name == "dynamic.tree.indicesPointer" ||
           name == "dynamic.tree.nodesPointer" ||
           name == "dynamic.tree.refitBitsPointer" ||
           name == "dynamic.newTreeStorage.indicesPointer" ||
           name == "dynamic.newTreeStorage.nodesPointer" ||
           name == "dynamic.newTreeStorage.refitBitsPointer";
}

bool semanticBytesEqual(const QueryImage::Field& a,
                        const QueryImage::Field& b)
{
    if (a.bytes == b.bytes) return true;
    if (pointerValue(a.name)) return true;
    if (a.name != "dynamic.tree.nodes" &&
        a.name != "dynamic.newTreeStorage.nodes") return false;
    if (a.bytes.size() != b.bytes.size() ||
        a.bytes.size() % sizeof(Sq::AABBTreeNode)) return false;
    // The compiler leaves four bytes of padding between the three compressed
    // coordinates and mBitfield. Progressive build does not initialize it.
    for (std::size_t offset = 0; offset < a.bytes.size();
         offset += sizeof(Sq::AABBTreeNode))
        if (std::memcmp(a.bytes.data() + offset,
                        b.bytes.data() + offset, 12) ||
            std::memcmp(a.bytes.data() + offset + 16,
                        b.bytes.data() + offset + 16, 8))
            return false;
    return true;
}

bool at(const QueryImage& image, const char* name, PxU32 expected)
{
    PxU32 actual = 0;
    return word(image, name, actual) && actual == expected;
}

bool array(const QueryImage& image, const std::string& name,
           PxU32 pointer, std::size_t size)
{
    const QueryImage::Field* row = field(image, name);
    return row && row->address == pointer && row->bytes.size() == size;
}

bool treeSpec(const QueryImage& image, const char* prefix,
              PxU32 indices, bool empty, CrossSwapTreeSpecV1& spec)
{
    const std::string p(prefix);
    PxU32 indexPointer = 0, nodePointer = 0, stackPointer = 0;
    PxU32 refitPointer = 0;
    spec = CrossSwapTreeSpecV1();
    spec.structBytes = sizeof(spec);
    if (!word(image, (p + ".indicesPointer").c_str(), indexPointer) ||
        !word(image, (p + ".nodesPointer").c_str(), nodePointer) ||
        !word(image, (p + ".stackPointer").c_str(), stackPointer) ||
        !word(image, (p + ".refitBitsPointer").c_str(), refitPointer) ||
        !word(image, (p + ".nodeCount").c_str(), spec.nodeCount) ||
        !word(image, (p + ".primitiveCount").c_str(), spec.totalPrims) ||
        !word(image, (p + ".refitWordCount").c_str(), spec.refitWordCount) ||
        !word(image, (p + ".refitHighestWord").c_str(), spec.refitHighestWord) ||
        !word(image, (p + ".refitCount").c_str(), spec.refitCount) ||
        stackPointer || indices > 65536 || spec.refitWordCount > 8192 ||
        spec.refitCount > SUPPORT_UPDATE_ARRAY)
        return false;
    const QueryImage::Field* index = field(image, p + ".indices");
    const QueryImage::Field* nodes = field(image, p + ".nodes");
    const QueryImage::Field* bits = field(image, p + ".refitBits");
    const QueryImage::Field* refit = field(image, p + ".refitArray");
    if (!index || !nodes || !bits || !refit ||
        refit->bytes.size() != SUPPORT_UPDATE_ARRAY * sizeof(PxU32) ||
        !array(image, p + ".refitBits", refitPointer,
               spec.refitWordCount * sizeof(PxU32)))
        return false;
    spec.refitArrayCount = SUPPORT_UPDATE_ARRAY;
    spec.refitArray = reinterpret_cast<const PxU32*>(refit->bytes.data());
    if (empty)
    {
        return !indices && !indexPointer && !nodePointer &&
               !spec.nodeCount && !spec.totalPrims &&
               !spec.refitWordCount && !spec.refitHighestWord &&
               !spec.refitCount && !index->address && index->bytes.empty() &&
               !nodes->address && nodes->bytes.empty() &&
               !bits->address && bits->bytes.empty();
    }
    spec.indexCount = indices;
    spec.nodeCapacity = indices * 2 - 1;
    if (!indices || !indexPointer || !nodePointer ||
        !spec.nodeCount || spec.nodeCount > spec.nodeCapacity ||
        !array(image, p + ".indices", indexPointer,
               indices * sizeof(PxU32)) ||
        !array(image, p + ".nodes", nodePointer,
               spec.nodeCapacity * sizeof(Sq::AABBTreeNode)) ||
        (spec.refitWordCount &&
         (!refitPointer || spec.refitHighestWord >= spec.refitWordCount)) ||
        (!spec.refitWordCount &&
         (refitPointer || spec.refitHighestWord)))
        return false;
    spec.indices = reinterpret_cast<const PxU32*>(index->bytes.data());
    spec.nodes = nodes->bytes.data();
    spec.refitBits = spec.refitWordCount ?
        reinterpret_cast<const PxU32*>(bits->bytes.data()) : nullptr;
    return true;
}

bool relation(const QueryImage& target, const QueryImage& before,
              CrossSwapTreeSpecV1& oldSpec,
              CrossSwapTreeSpecV1& emptySpec,
              std::string& error)
{
    PxU32 oldTree = 0, newTree = 0, current = 0, boxes = 0;
    PxU32 count = 0, poolCount = 0, bBoxes = 0, bNew = 0;
    const QueryImage::Field* boxBytes = field(target, "dynamic.cachedBoxStorage");
    if (target.scene != before.scene ||
        !at(target, "dynamic.progress", Sq::BUILD_INIT) ||
        !at(before, "dynamic.progress", Sq::BUILD_NOT_STARTED) ||
        !word(target, "dynamic.currentTree", oldTree) ||
        !word(target, "dynamic.newTree", newTree) ||
        !word(target, "dynamic.cachedBoxes", boxes) ||
        !word(target, "dynamic.cachedBoxCount", count) ||
        !word(target, "dynamic.pool.objects", poolCount) ||
        !word(before, "dynamic.currentTree", current) ||
        !word(before, "dynamic.newTree", bNew) ||
        !word(before, "dynamic.cachedBoxes", bBoxes) ||
        !oldTree || !newTree || !boxes || !count || count > 65536 ||
        poolCount != count || oldTree == current || current != newTree ||
        bNew || bBoxes ||
        !at(before, "dynamic.cachedBoxCount", count) ||
        !at(target, "dynamic.builderInputPointer", boxes) ||
        !at(target, "dynamic.builderInputCount", count) ||
        !at(target, "dynamic.builderNodePointer", 0) ||
        !at(before, "dynamic.builderNodePointer",
            [&]() { PxU32 p = 0; word(before, "dynamic.tree.nodesPointer", p); return p; }()) ||
        !boxBytes || boxBytes->address != boxes ||
        boxBytes->bytes.size() != count * sizeof(PxBounds3) ||
        !treeSpec(target, "dynamic.tree", count, false, oldSpec) ||
        !treeSpec(target, "dynamic.newTreeStorage", 0, true, emptySpec))
    {
        error = "checkpoint and live scene do not form one supported BUILD_INIT-to-swap transition";
        return false;
    }

    // A committed swap may update mutable fields, but it must not replace
    // any other backing allocation or change actor/shape identity. In
    // particular, the map is writable only if its backing stayed in place.
    for (const QueryImage::Field& to : target.fields)
    {
        if (owned(to.name)) continue;
        const QueryImage::Field* from = field(before, to.name);
        if (!from || to.address != from->address ||
            to.bytes.size() != from->bytes.size() ||
            to.invariant != from->invariant ||
            (to.invariant && !lifecycle(to.name) &&
             to.name != "dynamic.map" && to.bytes != from->bytes))
        {
            error = "cross-swap preflight changed unrelated storage at " + to.name;
            return false;
        }
    }
    for (const QueryImage::Field& from : before.fields)
        if (!owned(from.name) && !field(target, from.name))
        {
            error = "cross-swap preflight has an extra live field at " + from.name;
            return false;
        }
    return true;
}

// All image rows here still refer to owned live memory. The released tree and
// boxes from target are intentionally excluded; they are never dereferenced.
void restoreShared(const QueryImage& image)
{
    for (const QueryImage::Field& row : image.fields)
        if (!owned(row.name) && !lifecycle(row.name) &&
            (!row.invariant || row.name == "dynamic.map") &&
            !row.bytes.empty())
            std::memcpy(reinterpret_cast<void*>(row.address),
                        row.bytes.data(), row.bytes.size());
}

bool equalAddress(const QueryImage::Field& expected,
                  const QueryImage::Field& observed,
                  PxU32 oldTree, PxU32 newTree,
                  PxU32 oldSecond, PxU32 newSecond,
                  PxU32 oldBoxes, PxU32 newBoxes,
                  const QueryImage& a, const QueryImage& b)
{
    if (expected.address == observed.address) return true;
    const std::string& name = expected.name;
    const auto inside = [](std::uintptr_t address, PxU32 base,
                           std::size_t size) {
        return base && address >= base && address - base < size;
    };
    if (name == "dynamic.cachedBoxStorage")
        return expected.address == oldBoxes && observed.address == newBoxes;
    if (name == "dynamic.tree.indices" || name == "dynamic.tree.nodes" ||
        name == "dynamic.tree.refitBits")
    {
        const char* pointer = name == "dynamic.tree.indices" ?
            "dynamic.tree.indicesPointer" :
            name == "dynamic.tree.nodes" ?
            "dynamic.tree.nodesPointer" :
            "dynamic.tree.refitBitsPointer";
        PxU32 lhs = 0, rhs = 0;
        return word(a, pointer, lhs) && word(b, pointer, rhs) &&
               expected.address == lhs && observed.address == rhs;
    }
    if (inside(expected.address, oldTree, sizeof(Sq::AABBTree)) &&
        inside(observed.address, newTree, sizeof(Sq::AABBTree)))
        return observed.address - newTree == expected.address - oldTree;
    if (inside(expected.address, oldSecond, sizeof(Sq::AABBTree)) &&
        inside(observed.address, newSecond, sizeof(Sq::AABBTree)))
        return observed.address - newSecond == expected.address - oldSecond;
    return false;
}

} // namespace

bool EqualsCrossSwapRebased(const QueryImage& expected,
                           const QueryImage& observed,
                           std::string& error)
{
    error.clear();
    if (expected.scene != observed.scene ||
        expected.fields.size() != observed.fields.size())
    {
        error = "rebased query scene or field count differs";
        return false;
    }
    PxU32 oldTree = 0, newTree = 0, oldSecond = 0, newSecond = 0;
    PxU32 oldBoxes = 0, newBoxes = 0;
    if (!word(expected, "dynamic.currentTree", oldTree) ||
        !word(observed, "dynamic.currentTree", newTree) ||
        !word(expected, "dynamic.newTree", oldSecond) ||
        !word(observed, "dynamic.newTree", newSecond) ||
        !word(expected, "dynamic.cachedBoxes", oldBoxes) ||
        !word(observed, "dynamic.cachedBoxes", newBoxes) ||
        !oldTree || !newTree || bool(oldSecond) != bool(newSecond) ||
        bool(oldBoxes) != bool(newBoxes))
    {
        error = "rebased query owner pointers are inconsistent";
        return false;
    }
    for (std::size_t i = 0; i < expected.fields.size(); ++i)
    {
        const QueryImage::Field& a = expected.fields[i];
        const QueryImage::Field& b = observed.fields[i];
        if (a.name != b.name || a.invariant != b.invariant ||
            a.bytes.size() != b.bytes.size() ||
            !equalAddress(a, b, oldTree, newTree, oldSecond,
                          newSecond, oldBoxes, newBoxes, expected, observed) ||
            !semanticBytesEqual(a, b))
        {
            error = "rebased query differs at " + a.name;
            if (a.bytes.size() == b.bytes.size())
                for (std::size_t offset = 0; offset < a.bytes.size(); ++offset)
                    if (a.bytes[offset] != b.bytes[offset])
                    {
                        error += " byte " + std::to_string(offset) +
                                 " (" + std::to_string(a.bytes[offset]) +
                                 " vs " + std::to_string(b.bytes[offset]) + ")";
                        if (a.name == "dynamic.tree.nodes")
                        {
                            const std::size_t node =
                                offset / sizeof(Sq::AABBTreeNode);
                            Sq::AABBTreeNode lhs, rhs;
                            std::memcpy(&lhs, a.bytes.data() +
                                node * sizeof(lhs), sizeof(lhs));
                            std::memcpy(&rhs, b.bytes.data() +
                                node * sizeof(rhs), sizeof(rhs));
                            error += " node " + std::to_string(node) +
                                " bits " + std::to_string(lhs.mBitfield) +
                                "/" + std::to_string(rhs.mBitfield) +
                                " centers " + std::to_string(lhs.mCx) +
                                "/" + std::to_string(rhs.mCx);
                        }
                        break;
                    }
            return false;
        }
    }
    PxU32 aInput = 0, bInput = 0, aNode = 0, bNode = 0;
    PxU32 aTreeNodes = 0, bTreeNodes = 0;
    if (!word(expected, "dynamic.builderInputPointer", aInput) ||
        !word(observed, "dynamic.builderInputPointer", bInput) ||
        !word(expected, "dynamic.builderNodePointer", aNode) ||
        !word(observed, "dynamic.builderNodePointer", bNode) ||
        !word(expected, "dynamic.tree.nodesPointer", aTreeNodes) ||
        !word(observed, "dynamic.tree.nodesPointer", bTreeNodes) ||
        (oldBoxes ? (aInput != oldBoxes || bInput != newBoxes ||
                     aNode || bNode) :
                    (aNode != aTreeNodes || bNode != bTreeNodes)))
    {
        error = "rebased query builder aliases differ";
        return false;
    }
    return true;
}

bool RestoreAcrossOneQuerySwap(PxScene& scene,
                               const QueryImage& checkpoint,
                               std::string& error,
                               bool forceVerificationFailure)
{
    error.clear();
    if (checkpoint.seal != seal(checkpoint))
    {
        error = "cross-swap checkpoint was modified after capture";
        return false;
    }
    if (checkpoint.scene != reinterpret_cast<std::uintptr_t>(&scene))
    {
        error = "cross-swap checkpoint belongs to another scene";
        return false;
    }
    QueryImage before;
    if (!CaptureQueryImage(scene, before, error)) return false;
    CrossSwapTreeSpecV1 oldSpec, emptySpec;
    if (!relation(checkpoint, before, oldSpec, emptySpec, error))
        return false;
    Bridge api;
    if (!bridge(api, error)) return false;

    void* oldTree = nullptr;
    void* secondTree = nullptr;
    void* boxes = nullptr;
    const QueryImage::Field* boxBytes =
        field(checkpoint, "dynamic.cachedBoxStorage");
    if (api.create(&oldSpec, &oldTree) ||
        api.create(&emptySpec, &secondTree) ||
        api.alloc(boxBytes->bytes.data(),
                  static_cast<PxU32>(boxBytes->bytes.size()), &boxes))
    {
        if (oldTree) api.destroy(oldTree);
        if (secondTree) api.destroy(secondTree);
        if (boxes) api.free(boxes);
        error = "source-owned cross-swap staging allocation failed";
        return false;
    }

    NpScene& np = static_cast<NpScene&>(scene);
    Sq::SceneQueryManager& manager = np.getSceneQueryManagerFast();
    Sq::AABBPruner& dynamic =
        *static_cast<Sq::AABBPruner*>(manager.mPruners[1]);
    Sq::AABBTree* escrow = dynamic.mAABBTree;
    restoreShared(checkpoint);
    dynamic.mAABBTree = static_cast<Sq::AABBTree*>(oldTree);
    dynamic.mNewTree = static_cast<Sq::AABBTree*>(secondTree);
    dynamic.mCachedBoxes = static_cast<PxBounds3*>(boxes);
    dynamic.mProgress = Sq::BUILD_INIT;
    dynamic.mBuilder.mAABBArray = dynamic.mCachedBoxes;
    dynamic.mBuilder.mNodeBase = nullptr;
    dynamic.mDoSaveFixups = true;

    QueryImage observed;
    std::string verify;
    const bool verified = CaptureQueryImage(scene, observed, verify) &&
        EqualsCrossSwapRebased(checkpoint, observed, verify);
    if (verified && !forceVerificationFailure)
    {
        api.destroy(escrow);
        return true;
    }
    if (verified && forceVerificationFailure)
        verify = "test-injected verification failure";

    // Escrow is still intact. Restore all fixed-address bytes and owner
    // pointers before releasing staged allocations. No source operation that
    // can allocate or commit runs between the two captures.
    restoreShared(before);
    dynamic.mAABBTree = escrow;
    dynamic.mNewTree = nullptr;
    dynamic.mCachedBoxes = nullptr;
    dynamic.mProgress = Sq::BUILD_NOT_STARTED;
    PxU32 input = 0, node = 0;
    word(before, "dynamic.builderInputPointer", input);
    word(before, "dynamic.builderNodePointer", node);
    dynamic.mBuilder.mAABBArray = reinterpret_cast<PxBounds3*>(input);
    dynamic.mBuilder.mNodeBase = reinterpret_cast<Sq::AABBTreeNode*>(node);
    dynamic.mDoSaveFixups = false;
    api.destroy(oldTree);
    api.destroy(secondTree);
    api.free(boxes);
    QueryImage rolledBack;
    std::string rollbackError;
    if (!CaptureQueryImage(scene, rolledBack, rollbackError) ||
        !before.equals(rolledBack, rollbackError))
    {
        std::fprintf(stderr,
                     "Fatal cross-swap rollback verification failure: %s; %s\n",
                     verify.c_str(), rollbackError.c_str());
        std::abort();
    }
    error = "cross-swap verification failed and live state was rolled back: " + verify;
    return false;
}

}} // namespace oc2::offline
