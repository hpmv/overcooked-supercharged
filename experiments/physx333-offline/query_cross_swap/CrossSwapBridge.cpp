// Test-only source-owned allocations for the disposable PhysX 3.3.3 mirror.
// No shipped Unity binary loads this bridge.
#define PHYSX333_CROSS_SWAP_BRIDGE_BUILD
#include "CrossSwapBridge.h"

#include <cstring>

#include "PxMemory.h"
#include "PsIntrinsics.h"
#include "PsVecMath.h"
#include "PsUserAllocated.h"
#include "PsMathUtils.h"
#include "PxBounds3.h"
#include "GuContainer.h"

#define private public
#define protected public
#include "SqAABBTree.h"
#undef protected
#undef private

using namespace physx;
using namespace physx333_offline;

static_assert(sizeof(void*) == 4, "Cross-swap bridge requires Win32");
static_assert(sizeof(CrossSwapTreeSpecV1) == 52,
              "Unexpected cross-swap tree request ABI");

namespace {

bool validSpec(const CrossSwapTreeSpecV1& spec)
{
    if (spec.structBytes != sizeof(spec) || spec.indexCount > 65536 ||
        spec.refitWordCount > 8192 ||
        spec.refitArrayCount > SUPPORT_UPDATE_ARRAY ||
        spec.refitCount > SUPPORT_UPDATE_ARRAY)
        return false;
    if (!spec.indexCount)
        return !spec.nodeCapacity && !spec.nodeCount && !spec.totalPrims &&
               !spec.refitWordCount && !spec.refitHighestWord &&
               !spec.refitCount &&
               ((spec.refitArrayCount == 0 && !spec.refitArray) ||
                (spec.refitArrayCount == SUPPORT_UPDATE_ARRAY &&
                 spec.refitArray)) &&
               !spec.indices && !spec.nodes && !spec.refitBits;
    return spec.nodeCapacity == spec.indexCount * 2 - 1 &&
           spec.nodeCount && spec.nodeCount <= spec.nodeCapacity &&
           spec.indices && spec.nodes &&
           (spec.refitWordCount ? spec.refitBits != 0 &&
              spec.refitHighestWord < spec.refitWordCount :
              spec.refitBits == 0 && spec.refitHighestWord == 0) &&
           spec.refitArrayCount == SUPPORT_UPDATE_ARRAY && spec.refitArray;
}

} // namespace

extern "C" OC2_CROSS_SWAP_BRIDGE_API unsigned __cdecl
oc2_physx333_query_tree_create_v1(const CrossSwapTreeSpecV1* spec,
                                   void** outTree)
{
    if (!outTree) return 1;
    *outTree = 0;
    if (!spec || !validSpec(*spec)) return 1;

    Sq::AABBTree* tree = PX_NEW(Sq::AABBTree);
    if (!tree) return 2;
    std::memset(tree->mRefitArray, 0, sizeof(tree->mRefitArray));
    if (!spec->indexCount)
    {
        if (spec->refitArrayCount)
            std::memcpy(tree->mRefitArray, spec->refitArray,
                        sizeof(tree->mRefitArray));
        *outTree = tree;
        return 0;
    }

    tree->mIndices = static_cast<PxU32*>(PX_ALLOC(
        sizeof(PxU32) * spec->indexCount, PX_DEBUG_EXP("CrossSwap indices")));
    if (!tree->mIndices)
    {
        PX_DELETE(tree);
        return 2;
    }
    tree->mPool = PX_NEW(Sq::AABBTreeNode)[spec->nodeCapacity];
    if (!tree->mPool)
    {
        PX_DELETE(tree);
        return 2;
    }
    if (spec->refitWordCount)
    {
        tree->mRefitBitmask.mBits = static_cast<PxU32*>(PX_ALLOC(
            sizeof(PxU32) * spec->refitWordCount,
            PX_DEBUG_EXP("CrossSwap refit bits")));
        if (!tree->mRefitBitmask.mBits)
        {
            PX_DELETE(tree);
            return 2;
        }
        tree->mRefitBitmask.mSize = spec->refitWordCount;
    }

    std::memcpy(tree->mIndices, spec->indices,
                sizeof(PxU32) * spec->indexCount);
    std::memcpy(tree->mPool, spec->nodes,
                sizeof(Sq::AABBTreeNode) * spec->nodeCapacity);
    if (spec->refitWordCount)
        std::memcpy(tree->mRefitBitmask.mBits, spec->refitBits,
                    sizeof(PxU32) * spec->refitWordCount);
    std::memcpy(tree->mRefitArray, spec->refitArray,
                sizeof(tree->mRefitArray));
    tree->mTotalNbNodes = spec->nodeCount;
    tree->mTotalPrims = spec->totalPrims;
    tree->mRefitHighestSetWord = spec->refitHighestWord;
    tree->mNbRefitNodes = spec->refitCount;
    *outTree = tree;
    return 0;
}

extern "C" OC2_CROSS_SWAP_BRIDGE_API void __cdecl
oc2_physx333_query_tree_destroy_v1(void* tree)
{
    PX_DELETE(static_cast<Sq::AABBTree*>(tree));
}

extern "C" OC2_CROSS_SWAP_BRIDGE_API unsigned __cdecl
oc2_physx333_query_alloc_copy_v1(const void* bytes, uint32_t byteCount,
                                  void** out)
{
    if (!out) return 1;
    *out = 0;
    if (!byteCount) return bytes ? 1 : 0;
    if (!bytes || byteCount > 16u * 1024u * 1024u) return 1;
    void* block = PX_ALLOC(byteCount, PX_DEBUG_EXP("CrossSwap copied bytes"));
    if (!block) return 2;
    std::memcpy(block, bytes, byteCount);
    *out = block;
    return 0;
}

extern "C" OC2_CROSS_SWAP_BRIDGE_API void __cdecl
oc2_physx333_query_free_v1(void* block)
{
    if (block) PX_FREE(block);
}
