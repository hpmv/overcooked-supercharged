// Test-only bridge. Copy this file and NPhaseBridge.h into the disposable
// pinned PhysX 3.3.3 mirror's SimulationController/src directory, compile it
// into SimulationController.lib, and force-link its exported C symbol into
// PhysX3_x86.dll. The original source checkout is not modified.
#include "NPhaseBridge.h"

#include "ScNPhaseCore.h"
#include "ScScene.h"
#include "ScShapeSim.h"
#include "ScShapeCore.h"
#include "ScRigidCore.h"
#include "ScRigidSim.h"
#include "ScInteractionScene.h"
#include "ScElement.h"

static_assert(sizeof(void*) == 4, "The offline bridge requires Win32 PhysX");
static_assert(sizeof(physx333_offline::NPhaseBridgePairV1) == 20,
              "Unexpected bridge pair ABI");

namespace {

using namespace physx;
using namespace physx333_offline;

Sc::ShapeSim* findShape(Sc::RigidSim& actor, const Sc::ShapeCore& core)
{
    for (Sc::Element* element = actor.getElements_(); element;
         element = element->mNextInActor)
    {
        if (element->getElementType() == Sc::PX_ELEMENT_TYPE_SHAPE)
        {
            Sc::ShapeSim* shape = static_cast<Sc::ShapeSim*>(element);
            if (&shape->getCore() == &core) return shape;
        }
    }
    return NULL;
}

struct ResolvedPair {
    Sc::ShapeSim* shape0;
    Sc::ShapeSim* shape1;
};

bool matchesFixtureFilter(Sc::Scene& scene, Sc::ShapeSim& shape0,
                          Sc::ShapeSim& shape1, PxU32 expected)
{
    const PxSimulationFilterShader shader = scene.getFilterShaderFast();
    if (!shader || scene.getFilterCallbackFast()) return false;
    PxFilterObjectAttributes attr0, attr1;
    PxFilterData data0, data1;
    shape0.getFilterInfo(attr0, data0);
    shape1.getFilterInfo(attr1, data1);
    PxPairFlags pairFlags;
    const PxFilterFlags filterFlags = shader(
        attr0, data0, attr1, data1, pairFlags,
        scene.getFilterShaderDataFast(), scene.getFilterShaderDataSizeFast());
    return static_cast<PxU32>(filterFlags) ==
               static_cast<PxU32>(PxFilterFlag::eDEFAULT) &&
           static_cast<PxU32>(pairFlags) == expected;
}

} // namespace

extern "C" __declspec(dllexport) PxU32 __cdecl
oc2_physx333_nphase_recreate_v1(
    void* nphaseCore,
    const physx333_offline::NPhaseBridgePairV1* pairs,
    PxU32 pairCount)
{
    using namespace physx333_offline;
    if (!nphaseCore || !pairs || pairCount != 6)
        return NPhaseBridgeInvalidInput;

    Sc::NPhaseCore& nphase =
        *static_cast<Sc::NPhaseCore*>(nphaseCore);
    Sc::Scene& scene = nphase.getScene();
    Sc::InteractionScene& interactions = scene.getInteractionScene();
    if (interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_OVERLAP) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_TRIGGER) ||
        interactions.getInteractionCount(Sc::PX_INTERACTION_TYPE_MARKER) ||
        nphase.getNbContactReportActorPairs() ||
        nphase.getAllPersistentContactEventPairCount() ||
        nphase.getForceThresholdContactEventPairCount() ||
        scene.getFilterCallbackFast())
        return NPhaseBridgeUnsupportedScene;

    ResolvedPair resolved[6];
    for (PxU32 i = 0; i < pairCount; ++i)
    {
        const NPhaseBridgePairV1& pair = pairs[i];
        if (!pair.actorCore0 || !pair.actorCore1 || !pair.shapeCore0 ||
            !pair.shapeCore1 || pair.actorCore0 == pair.actorCore1)
            return NPhaseBridgeInvalidInput;

        Sc::RigidSim* actor0 =
            static_cast<Sc::RigidCore*>(pair.actorCore0)->getSim();
        Sc::RigidSim* actor1 =
            static_cast<Sc::RigidCore*>(pair.actorCore1)->getSim();
        if (!actor0 || !actor1 || &actor0->getScene() != &scene ||
            &actor1->getScene() != &scene)
            return NPhaseBridgeUnresolvedShape;

        const bool fixtureActorKinds =
            (actor0->getActorType() == PxActorType::eRIGID_DYNAMIC &&
             actor1->getActorType() == PxActorType::eRIGID_STATIC) ||
            (actor0->getActorType() == PxActorType::eRIGID_STATIC &&
             actor1->getActorType() == PxActorType::eRIGID_DYNAMIC);
        if (!fixtureActorKinds) return NPhaseBridgeUnsupportedScene;

        Sc::ShapeSim* shape0 = findShape(
            *actor0, *static_cast<Sc::ShapeCore*>(pair.shapeCore0));
        Sc::ShapeSim* shape1 = findShape(
            *actor1, *static_cast<Sc::ShapeCore*>(pair.shapeCore1));
        if (!shape0 || !shape1 || shape0 == shape1 ||
            !shape0->hasAABBMgrHandle() || !shape1->hasAABBMgrHandle())
            return NPhaseBridgeUnresolvedShape;
        if (shape0->getGeometryType() != PxGeometryType::eBOX ||
            shape1->getGeometryType() != PxGeometryType::eBOX ||
            !(shape0->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
            !(shape1->getFlags() & PxShapeFlag::eSIMULATION_SHAPE) ||
            (shape0->getFlags() & PxShapeFlag::eTRIGGER_SHAPE) ||
            (shape1->getFlags() & PxShapeFlag::eTRIGGER_SHAPE))
            return NPhaseBridgeUnsupportedScene;
        if (!matchesFixtureFilter(scene, *shape0, *shape1,
                                  pair.expectedPairFlags))
            return NPhaseBridgeUnexpectedFilter;

        for (PxU32 j = 0; j < i; ++j)
            if ((resolved[j].shape0 == shape0 && resolved[j].shape1 == shape1) ||
                (resolved[j].shape0 == shape1 && resolved[j].shape1 == shape0))
                return NPhaseBridgeExistingInteraction;
        resolved[i].shape0 = shape0;
        resolved[i].shape1 = shape1;
    }

    // No object or scene memory is written before this point. These calls
    // construct objects through the SDK's own pair, manager and island path.
    for (PxU32 i = 0; i < pairCount; ++i)
        nphase.onOverlapCreated(resolved[i].shape0, resolved[i].shape1, 0);
    return NPhaseBridgeSuccess;
}
