// Test-only export for the disposable pinned PhysX 3.3.3 source mirror.
// AABBTree::release() is not exported from the original Win32 DLL. The
// caller strictly preflights the settled BUILD_INIT <- BUILD_IN_PROGRESS
// transition before invoking this source-owned lifecycle method.
#include "PsIntrinsics.h"
#include "PsVecMath.h"
#include "PsUserAllocated.h"
#include "PsMathUtils.h"
#include "GuContainer.h"
#include "SqAABBTree.h"

extern "C" __declspec(dllexport) unsigned __cdecl
oc2_physx333_query_release_cold_v1(void* tree)
{
    if (!tree) return 1;
    static_cast<physx::Sq::AABBTree*>(tree)->release();
    return 0;
}
