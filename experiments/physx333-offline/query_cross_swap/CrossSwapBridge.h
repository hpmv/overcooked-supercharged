#pragma once

#include <stdint.h>

namespace physx333_offline {

// Win32 source-mirror contract. Counts describe owned allocations, not only
// the subset of nodes currently visited by a query. An empty BUILD_INIT tree
// has zero allocation counts but may carry all 128 captured refit-array words.
struct CrossSwapTreeSpecV1
{
    uint32_t structBytes;
    uint32_t indexCount;
    uint32_t nodeCapacity;
    uint32_t nodeCount;
    uint32_t totalPrims;
    uint32_t refitWordCount;
    uint32_t refitHighestWord;
    uint32_t refitCount;
    uint32_t refitArrayCount;
    const uint32_t* indices;
    const void* nodes;
    const uint32_t* refitBits;
    const uint32_t* refitArray;
};

// Return 0 on success. Outputs are detached, owned by the source-built DLL,
// and remain the caller's responsibility until installed into a pruner.
#ifdef PHYSX333_CROSS_SWAP_BRIDGE_BUILD
#define OC2_CROSS_SWAP_BRIDGE_API __declspec(dllexport)
#else
#define OC2_CROSS_SWAP_BRIDGE_API __declspec(dllimport)
#endif
extern "C" OC2_CROSS_SWAP_BRIDGE_API unsigned __cdecl
oc2_physx333_query_tree_create_v1(const CrossSwapTreeSpecV1* spec,
                                   void** outTree);
extern "C" OC2_CROSS_SWAP_BRIDGE_API void __cdecl
oc2_physx333_query_tree_destroy_v1(void* tree);
extern "C" OC2_CROSS_SWAP_BRIDGE_API unsigned __cdecl
oc2_physx333_query_alloc_copy_v1(const void* bytes, uint32_t byteCount,
                                  void** out);
extern "C" OC2_CROSS_SWAP_BRIDGE_API void __cdecl
oc2_physx333_query_free_v1(void* block);

} // namespace physx333_offline
