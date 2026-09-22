// Test-only export for the disposable pinned PhysX 3.3.3 mirror. The
// original vendor checkout is never modified. This translation unit must be
// linked into PhysX3_x86.dll by the offline build script.
#include "InteractionReportBridge.h"

#include <set>

#include "ScNPhaseCore.h"
#include "ScActorPair.h"
#include "ScShapeInstancePairLL.h"
#include "ScInteractionScene.h"
#include "ScScene.h"

extern "C" __declspec(dllexport) physx::PxU32 __cdecl
oc2_physx333_report_create_v1(void* nphaseCore,
                              void* const* orderedActorPairs,
                              physx::PxU32 count)
{
    using namespace physx;
    using namespace physx333_offline;
    if (!nphaseCore || !orderedActorPairs || count != 6)
        return InteractionReportBridgeInvalidInput;
    Sc::NPhaseCore& nphase = *static_cast<Sc::NPhaseCore*>(nphaseCore);
    Sc::InteractionScene& interactions =
        nphase.getScene().getInteractionScene();
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) != 6 ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_OVERLAP) != 6 ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER))
        return InteractionReportBridgeUnsupportedScene;

    std::set<Sc::ActorPair*> live;
    Cm::Range<Sc::Interaction*const> range =
        interactions.getInteractions(Sc::PX_INTERACTION_TYPE_OVERLAP);
    while (!range.empty())
    {
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        live.insert(sip->getActorPair());
    }
    if (live.size() != 6) return InteractionReportBridgeUnsupportedScene;
    std::set<Sc::ActorPair*> requested;
    for (PxU32 i = 0; i < count; ++i)
    {
        Sc::ActorPair* pair =
            static_cast<Sc::ActorPair*>(orderedActorPairs[i]);
        if (!pair || !live.count(pair) || !requested.insert(pair).second)
            return InteractionReportBridgeUnknownPair;
        if (pair->hasReportData())
            return InteractionReportBridgeAlreadyAllocated;
    }
    // Preflight is complete. The SDK's own lazy path initializes report IDs,
    // actor pointers and pool ownership. A failed allocation leaves a changed
    // scene, which the caller must discard.
    for (PxU32 i = 0; i < count; ++i)
    {
        Sc::ActorPair* pair =
            static_cast<Sc::ActorPair*>(orderedActorPairs[i]);
        pair->getContactStreamManager();
        if (!pair->hasReportData())
            return InteractionReportBridgeAllocationFailure;
    }
    return InteractionReportBridgeSuccess;
}

extern "C" __declspec(dllexport) physx::PxU32 __cdecl
oc2_physx333_report_create_subset_v2(
    void* nphaseCore, void* const* orderedMissingActorPairs,
    physx::PxU32 missingCount, physx::PxU32 expectedOverlapCount,
    physx::PxU32 expectedAlreadyReported)
{
    using namespace physx;
    using namespace physx333_offline;
    if (!nphaseCore || !orderedMissingActorPairs || !missingCount ||
        missingCount > 64 || expectedOverlapCount > 64 ||
        expectedAlreadyReported > expectedOverlapCount ||
        missingCount + expectedAlreadyReported > expectedOverlapCount)
        return InteractionReportBridgeInvalidInput;
    Sc::NPhaseCore& nphase = *static_cast<Sc::NPhaseCore*>(nphaseCore);
    Sc::InteractionScene& interactions =
        nphase.getScene().getInteractionScene();
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) !=
            expectedOverlapCount ||
        interactions.getActiveInteractionCount(
            Sc::PX_INTERACTION_TYPE_OVERLAP) != expectedOverlapCount ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER))
        return InteractionReportBridgeUnsupportedScene;

    std::set<Sc::ActorPair*> live;
    PxU32 alreadyReported = 0;
    Cm::Range<Sc::Interaction*const> range =
        interactions.getInteractions(Sc::PX_INTERACTION_TYPE_OVERLAP);
    while (!range.empty())
    {
        Sc::ShapeInstancePairLL* sip =
            static_cast<Sc::ShapeInstancePairLL*>(range.front());
        range.popFront();
        Sc::ActorPair* pair = sip->getActorPair();
        if (!live.insert(pair).second)
            return InteractionReportBridgeUnsupportedScene;
        if (pair->hasReportData()) ++alreadyReported;
    }
    if (live.size() != expectedOverlapCount ||
        alreadyReported != expectedAlreadyReported)
        return InteractionReportBridgeUnsupportedScene;
    std::set<Sc::ActorPair*> requested;
    for (PxU32 i = 0; i < missingCount; ++i)
    {
        Sc::ActorPair* pair =
            static_cast<Sc::ActorPair*>(orderedMissingActorPairs[i]);
        if (!pair || !live.count(pair) || !requested.insert(pair).second)
            return InteractionReportBridgeUnknownPair;
        if (pair->hasReportData())
            return InteractionReportBridgeAlreadyAllocated;
    }

    for (PxU32 i = 0; i < missingCount; ++i)
    {
        Sc::ActorPair* pair =
            static_cast<Sc::ActorPair*>(orderedMissingActorPairs[i]);
        pair->getContactStreamManager();
        if (!pair->hasReportData())
            return InteractionReportBridgeAllocationFailure;
    }
    return InteractionReportBridgeSuccess;
}
