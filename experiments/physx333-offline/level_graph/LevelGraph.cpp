// Synthetic Story 1-1 f444->f445 interaction graph in source-built PhysX 3.3.3.
// No Unity or game binary is loaded. This is a fresh-scene baseline, not rewind.
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iostream>
#include <map>
#include <set>
#include <sstream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "../oracle/Oracle.h"
#include "../cache/TransformCacheImage.h"
#include "../sap/SapImage.h"
#include "../island/IslandImage.h"
#include "../aux_interactions/AuxInteractionImage.h"

#define private public
#define protected public
#include "NpScene.h"
#include "NpShape.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScActor.h"
#include "ScActorCore.h"
#include "ScActorPair.h"
#include "ScActorSim.h"
#include "ScBodySim.h"
#include "ScStaticSim.h"
#include "ScElementSimInteraction.h"
#include "ScInteractionScene.h"
#include "ScNPhaseCore.h"
#include "ScScene.h"
#include "ScShapeSim.h"
#include "ScShapeInstancePairLL.h"
#include "PxsContext.h"
#include "PxsBroadPhaseCommon.h"
#include "GuPersistentContactManifold.h"
#undef protected
#undef private

using namespace physx;

namespace {

static_assert(sizeof(void*) == 4, "Requires the source-built Win32 PhysX");
static_assert(sizeof(Gu::LargePersistentContactManifold) == 240,
              "Pinned capsule/box large manifold layout changed");

const PxU32 kCommonA = 1, kCommonB = 2;
const PxU32 kExtraFirst = 3, kExtraLast = 6;
const PxU32 kTriggerA = 7, kTriggerB = 8;
const PxU32 kMover = 9, kOtherFirst = 10, kOtherLast = 12;
const PxU32 kIdleBody = 13;
const PxReal kStep = 1.0f / 60.0f;

enum Role {
    Common = 1, ExtraMarkerTouch = 2, ExtraMarkerNoTouch = 3,
    ExtraPlainTouch = 4, ExtraPlainNoTouch = 5, Trigger = 6,
    MovingMain = 7, OtherMain = 8, MovingAux = 9, Idle = 10
};

void fail(const std::string& why)
{
    std::cerr << "FAIL: " << why << '\n';
    std::exit(1);
}

PxU32 id(const PxActor* actor)
{
    return static_cast<PxU32>(
        reinterpret_cast<std::uintptr_t>(actor->userData));
}

PxFilterFlags fixtureFilter(PxFilterObjectAttributes, PxFilterData data0,
                            PxFilterObjectAttributes, PxFilterData data1,
                            PxPairFlags& flags, const void*, PxU32)
{
    const PxU32 a = data0.word0, b = data1.word0;
    const PxU32 moving = a == MovingMain ? b : b == MovingMain ? a : 0;
    const PxU32 other = a == OtherMain ? b : b == OtherMain ? a : 0;
    const PxU32 aux = a == MovingAux ? b : b == MovingAux ? a : 0;
    flags = PxPairFlags(0);
    if (moving == Trigger || aux == Trigger)
    {
        flags = PxPairFlag::eTRIGGER_DEFAULT;
        return PxFilterFlag::eDEFAULT;
    }
    if (aux == ExtraMarkerTouch || aux == ExtraMarkerNoTouch)
        return PxFilterFlag::eSUPPRESS;
    if (moving == Common || moving == ExtraMarkerTouch ||
        moving == ExtraMarkerNoTouch || moving == ExtraPlainTouch ||
        moving == ExtraPlainNoTouch || other == Common)
    {
        flags = PxPairFlag::eCONTACT_DEFAULT |
                PxPairFlag::eNOTIFY_TOUCH_FOUND |
                PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
                PxPairFlag::eNOTIFY_TOUCH_LOST;
        return PxFilterFlag::eDEFAULT;
    }
    return PxFilterFlag::eKILL;
}

struct Event
{
    char kind = 0;
    PxU32 chef = 0;
    PxU32 fixed = 0;
    PxU32 dynamicShape = 0;
    PxU32 flags = 0;
    bool operator==(const Event& b) const {
        return kind == b.kind && chef == b.chef && fixed == b.fixed &&
               dynamicShape == b.dynamicShape && flags == b.flags;
    }
};

struct Callbacks : PxSimulationEventCallback
{
    PxShape* moverMain = NULL;
    PxShape* moverAux = NULL;
    std::vector<Event> rows;
    virtual void onConstraintBreak(PxConstraintInfo*, PxU32) {}
    virtual void onWake(PxActor**, PxU32) {}
    virtual void onSleep(PxActor**, PxU32) {}
    virtual void onContact(const PxContactPairHeader& header,
                           const PxContactPair* pairs, PxU32 count)
    {
        for (PxU32 i = 0; i < count; ++i)
        {
            const PxU32 a = id(header.actors[0]), b = id(header.actors[1]);
            Event e;
            e.kind = 'C';
            e.chef = a >= kMover ? a : b;
            e.fixed = a < kMover ? a : b;
            e.dynamicShape = 0;
            e.flags = static_cast<PxU32>(pairs[i].events);
            rows.push_back(e);
        }
    }
    virtual void onTrigger(PxTriggerPair* pairs, PxU32 count)
    {
        for (PxU32 i = 0; i < count; ++i)
        {
            Event e;
            e.kind = 'T';
            e.chef = id(pairs[i].otherActor);
            e.fixed = id(pairs[i].triggerActor);
            e.dynamicShape = pairs[i].otherShape == moverMain ? 0u :
                pairs[i].otherShape == moverAux ? 1u : 0xffffffffu;
            e.flags = static_cast<PxU32>(pairs[i].status);
            rows.push_back(e);
        }
    }
};

struct Runtime
{
    struct Errors : PxErrorCallback {
        PxU32 count = 0;
        virtual void reportError(PxErrorCode::Enum code, const char* message,
                                 const char* file, int line) {
            ++count;
            std::cerr << "PhysX " << static_cast<int>(code) << ": "
                      << message << " (" << file << ':' << line << ")\n";
        }
    } errors;
    struct Dispatcher : PxCpuDispatcher {
        virtual void submitTask(PxBaseTask& task) {
            task.run(); task.release();
        }
        virtual PxU32 getWorkerCount() const { return 0; }
    } dispatcher;
    PxDefaultAllocator defaultAllocator;
    PxAllocatorCallback* allocator;
    PxFoundation* foundation = NULL;
    PxPhysics* physics = NULL;
    PxMaterial* material = NULL;
    explicit Runtime(PxAllocatorCallback* customAllocator = NULL)
        : allocator(customAllocator ? customAllocator : &defaultAllocator) {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, *allocator, errors);
        if (!foundation) fail("PxCreateFoundation");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation,
                                  PxTolerancesScale());
        if (!physics) fail("PxCreatePhysics");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        if (!material) fail("createMaterial");
    }
    ~Runtime() {
        material->release(); physics->release(); foundation->release();
    }
};

struct ShapeKey
{
    PxU32 actor = 0, shape = 0;
    bool operator==(const ShapeKey& b) const {
        return actor == b.actor && shape == b.shape;
    }
    bool operator<(const ShapeKey& b) const {
        return actor < b.actor || (actor == b.actor && shape < b.shape);
    }
};

struct PairKey
{
    PxU32 type = 0;
    ShapeKey a, b;
    bool operator==(const PairKey& q) const {
        return type == q.type && a == q.a && b == q.b;
    }
    bool operator<(const PairKey& q) const {
        if (type != q.type) return type < q.type;
        if (a < q.a) return true;
        if (q.a < a) return false;
        return b < q.b;
    }
};

PairKey pair(PxU32 type, ShapeKey a, ShapeKey b)
{
    if (b < a) std::swap(a, b);
    PairKey result;
    result.type = type; result.a = a; result.b = b;
    return result;
}

struct ActorOrder
{
    PxU32 id = 0;
    std::vector<PairKey> pairs;
    bool operator==(const ActorOrder& b) const {
        return id == b.id && pairs == b.pairs;
    }
};

struct GraphImage
{
    std::vector<PxU32> activeBodies;
    std::vector<PxU32> counts;
    std::vector<PxU32> activeCounts;
    std::vector<PairKey> scenePairs;
    std::vector<ActorOrder> actors;
    bool operator==(const GraphImage& b) const {
        return activeBodies == b.activeBodies && counts == b.counts &&
            activeCounts == b.activeCounts && scenePairs == b.scenePairs &&
            actors == b.actors;
    }
};

Sc::Actor* scActor(PxActor& actor)
{
    if (actor.getType() == PxActorType::eRIGID_DYNAMIC)
        return static_cast<Sc::ActorCore&>(static_cast<NpRigidDynamic&>(actor)
            .getScbBodyFast().getScBody()).getSim();
    return static_cast<Sc::ActorCore&>(static_cast<NpRigidStatic&>(actor)
        .getScbRigidStaticFast().getScStatic()).getSim();
}

bool captureGraph(PxScene& scene, GraphImage& image, std::string& error)
{
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    if (count != 13) { error = "public rigid actor inventory differs"; return false; }
    std::vector<PxActor*> publicActors(count);
    if (scene.getActors(flags, publicActors.data(), count) != count)
    { error = "actor enumeration changed"; return false; }
    std::map<const Sc::Actor*, PxU32> actorIds;
    std::map<const Sc::ShapeCore*, ShapeKey> shapeKeys;
    for (PxActor* publicActor : publicActors)
    {
        const PxU32 actorId = id(publicActor);
        Sc::Actor* internal = scActor(*publicActor);
        if (!actorId || !internal || !actorIds.insert(
                std::make_pair(internal, actorId)).second)
        { error = "actor identity is incomplete"; return false; }
        PxRigidActor& rigid = *static_cast<PxRigidActor*>(publicActor);
        const PxU32 shapeCount = rigid.getNbShapes();
        std::vector<PxShape*> shapes(shapeCount);
        if (shapeCount && rigid.getShapes(shapes.data(), shapeCount) != shapeCount)
        { error = "shape enumeration changed"; return false; }
        for (PxU32 s = 0; s < shapeCount; ++s)
        {
            Sc::ShapeCore* core = &static_cast<NpShape&>(*shapes[s])
                .getScbShape().getScShape();
            ShapeKey key; key.actor = actorId; key.shape = s;
            if (!shapeKeys.insert(std::make_pair(core, key)).second)
            { error = "shape identity duplicated"; return false; }
        }
    }
    NpScene& np = static_cast<NpScene&>(scene);
    Sc::InteractionScene& interactions = np.getScene().getScScene()
        .getInteractionScene();
    GraphImage next;
    for (PxU32 i = 0; i < interactions.getNumActiveBodies(); ++i)
    {
        const Sc::Actor* body = interactions.getActiveBodiesArray()[i];
        const auto found = actorIds.find(body);
        if (found == actorIds.end())
        { error = "active body is outside public fixture"; return false; }
        next.activeBodies.push_back(found->second);
    }
    std::map<const Sc::Interaction*, PairKey> keys;
    for (PxU32 type = 0; type < Sc::PX_INTERACTION_TYPE_COUNT; ++type)
    {
        const Sc::InteractionType kind =
            static_cast<Sc::InteractionType>(type);
        next.counts.push_back(interactions.getInteractionCount(kind));
        next.activeCounts.push_back(
            interactions.getActiveInteractionCount(kind));
        Cm::Range<Sc::Interaction*const> rows =
            interactions.getInteractions(kind);
        while (!rows.empty())
        {
            Sc::Interaction* item = rows.front(); rows.popFront();
            if (type != Sc::PX_INTERACTION_TYPE_OVERLAP &&
                type != Sc::PX_INTERACTION_TYPE_TRIGGER &&
                type != Sc::PX_INTERACTION_TYPE_MARKER)
            { error = "unsupported interaction type"; return false; }
            Sc::ElementSimInteraction* shapes =
                static_cast<Sc::ElementSimInteraction*>(item);
            const Sc::ShapeCore* a = &static_cast<Sc::ShapeSim&>(
                shapes->getElementSim0()).getCore();
            const Sc::ShapeCore* b = &static_cast<Sc::ShapeSim&>(
                shapes->getElementSim1()).getCore();
            const auto foundA = shapeKeys.find(a), foundB = shapeKeys.find(b);
            if (foundA == shapeKeys.end() || foundB == shapeKeys.end())
            { error = "interaction shape is outside fixture"; return false; }
            const PairKey key = pair(type, foundA->second, foundB->second);
            next.scenePairs.push_back(key);
            keys[item] = key;
        }
    }
    for (PxActor* publicActor : publicActors)
    {
        Sc::Actor* actor = scActor(*publicActor);
        ActorOrder row; row.id = id(publicActor);
        for (PxU32 i = 0; i < actor->mInteractions.size(); ++i)
        {
            const auto found = keys.find(actor->mInteractions[i]);
            if (found == keys.end())
            { error = "actor interaction is outside scene arrays"; return false; }
            row.pairs.push_back(found->second);
        }
        next.actors.push_back(row);
    }
    std::sort(next.actors.begin(), next.actors.end(),
              [](const ActorOrder& a, const ActorOrder& b) {
                  return a.id < b.id;
              });
    image = next;
    error.clear();
    return true;
}

struct Facts
{
    PxU32 cacheLive = 0, cacheRefs = 0, cacheCurrent = 0;
    std::vector<PxU32> cacheFreeIds;
    PxU32 largeManifolds = 0, sphereManifolds = 0;
    PxU32 islandEdges = 0, sapPairs = 0, sapDeletes = 0;
    PxU32 contactManagers = 0, touchPairs = 0, reportPairs = 0;
    bool operator==(const Facts& b) const {
        return cacheLive == b.cacheLive && cacheRefs == b.cacheRefs &&
            cacheCurrent == b.cacheCurrent && cacheFreeIds == b.cacheFreeIds &&
            largeManifolds == b.largeManifolds &&
            sphereManifolds == b.sphereManifolds &&
            islandEdges == b.islandEdges && sapPairs == b.sapPairs &&
            sapDeletes == b.sapDeletes &&
            contactManagers == b.contactManagers &&
            touchPairs == b.touchPairs && reportPairs == b.reportPairs;
    }
};

const std::vector<std::uint32_t>& part(
    const physx333_offline::OracleImage& image, const char* name)
{
    const auto found = image.parts.find(name);
    if (found == image.parts.end()) fail(std::string("Oracle section missing: ") + name);
    return found->second;
}

PxU32 sapScalar(const oc2::offline::SapImage& image, const char* name)
{
    for (const auto& item : image.scalars)
        if (item.name == name)
        {
            if (item.bytes.size() != sizeof(PxU32))
                fail(std::string("SAP scalar size changed: ") + name);
            PxU32 value = 0;
            std::memcpy(&value, item.bytes.data(), sizeof(value));
            return value;
        }
    fail(std::string("SAP scalar missing: ") + name);
    return 0;
}

std::vector<PairKey> deletedOverlapKeys(PxScene& scene,
                                         const oc2::offline::SapImage& sap)
{
    std::map<std::uintptr_t, ShapeKey> coreKeys;
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    std::vector<PxActor*> actors(scene.getNbActors(flags));
    if (scene.getActors(flags, actors.data(), static_cast<PxU32>(actors.size())) !=
        actors.size()) fail("deleted-overlap actor inventory changed");
    for (PxActor* actor : actors)
    {
        PxRigidActor& rigid = *static_cast<PxRigidActor*>(actor);
        std::vector<PxShape*> shapes(rigid.getNbShapes());
        if (rigid.getShapes(shapes.data(), static_cast<PxU32>(shapes.size())) !=
            shapes.size()) fail("deleted-overlap shape inventory changed");
        for (PxU32 i = 0; i < shapes.size(); ++i)
        {
            ShapeKey key; key.actor = id(actor); key.shape = i;
            const auto core = reinterpret_cast<std::uintptr_t>(
                &static_cast<NpShape&>(*shapes[i]).getScbShape()
                    .getScShape().getCore());
            coreKeys.insert(std::make_pair(core, key));
        }
    }
    std::map<std::uintptr_t, ShapeKey> userKeys;
    for (const auto& binding : sap.bindings)
    {
        const auto found = coreKeys.find(binding.shapeCore);
        if (found != coreKeys.end())
            userKeys.insert(std::make_pair(binding.userData, found->second));
    }
    const auto found = std::find_if(sap.buffers.begin(), sap.buffers.end(),
        [](const oc2::offline::SapImage::Buffer& b) {
            return b.name == "aabb.deletedOverlaps";
        });
    if (found == sap.buffers.end()) fail("deleted-overlap SAP buffer missing");
    const PxU32 count = sapScalar(sap, "aabb.deletedOverlapSize");
    if (found->bytes.size() < count * sizeof(PxvBroadPhaseOverlap))
        fail("deleted-overlap SAP buffer truncated");
    std::vector<PairKey> result;
    for (PxU32 i = 0; i < count; ++i)
    {
        PxvBroadPhaseOverlap raw;
        std::memcpy(&raw, found->bytes.data() +
                    i * sizeof(PxvBroadPhaseOverlap), sizeof(raw));
        const auto a = userKeys.find(reinterpret_cast<std::uintptr_t>(raw.userdata0));
        const auto b = userKeys.find(reinterpret_cast<std::uintptr_t>(raw.userdata1));
        if (a == userKeys.end() || b == userKeys.end())
            fail("deleted overlap endpoint absent from SAP binding image");
        const PxU32 fixed = a->second.actor < kMover ? a->second.actor :
                            b->second.actor < kMover ? b->second.actor : 0u;
        if (!fixed) fail("deleted overlap is not dynamic/static");
        const PxU32 type = fixed == kTriggerA || fixed == kTriggerB ?
            Sc::PX_INTERACTION_TYPE_TRIGGER : Sc::PX_INTERACTION_TYPE_OVERLAP;
        result.push_back(pair(type, a->second, b->second));
    }
    return result;
}

struct Snapshot
{
    physx333_offline::OracleImage oracle;
    physx333_offline::AuxInteractionImage aux;
    GraphImage graph;
    Facts facts;
    std::vector<PairKey> deletedOverlaps;
    std::vector<Event> events;
};

struct World
{
    Runtime& runtime;
    Callbacks callback;
    PxScene* scene = NULL;
    PxRigidDynamic* chefs[4] = {NULL, NULL, NULL, NULL};
    PxRigidDynamic* idle = NULL;
    std::vector<PxRigidActor*> actors;

    explicit World(Runtime& rt) : runtime(rt)
    {
        PxSceneDesc desc(rt.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &rt.dispatcher;
        desc.filterShader = fixtureFilter;
        desc.simulationEventCallback = &callback;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        desc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
        desc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
        desc.flags |= PxSceneFlag::eENABLE_PCM;
        scene = rt.physics->createScene(desc);
        if (!scene) fail("createScene");

        addStatic(kCommonA, Common, -3.3f, 0.0f, 4.5f, false);
        addStatic(kCommonB, Common, -3.3f, 0.0f, 4.5f, false);
        addStatic(3, ExtraPlainNoTouch, 1.2f, 0.85f, 0.5f, false);
        addStatic(4, ExtraMarkerTouch, 0.0f, 0.85f, 0.5f, false);
        addStatic(5, ExtraMarkerNoTouch, 1.2f, 0.85f, 0.5f, false);
        addStatic(6, ExtraPlainTouch, 0.0f, 0.85f, 0.5f, false);
        addStatic(kTriggerA, Trigger, 0.0f, 0.85f, 0.5f, true);
        addStatic(kTriggerB, Trigger, 0.0f, 0.85f, 0.5f, true);

        const PxReal x[4] = {0.0f, -2.2f, -4.4f, -6.6f};
        for (PxU32 i = 0; i < 4; ++i)
        {
            chefs[i] = rt.physics->createRigidDynamic(
                PxTransform(PxVec3(x[i], 0.8f, 0.0f)));
            if (!chefs[i]) fail("create chef dynamic");
            PxShape* main = rt.physics->createShape(
                PxCapsuleGeometry(0.5f, 0.4f), *rt.material);
            if (!main) fail("create chef capsule");
            setRole(*main, i == 0 ? MovingMain : OtherMain);
            chefs[i]->attachShape(*main);
            if (i == 0) callback.moverMain = main;
            main->release();
            if (i == 0)
            {
                PxShape* aux = rt.physics->createShape(
                    PxBoxGeometry(1.4f, 0.5f, 1.0f), *rt.material);
                if (!aux) fail("create moving chef auxiliary shape");
                setRole(*aux, MovingAux);
                chefs[i]->attachShape(*aux);
                aux->setLocalPose(PxTransform(PxVec3(0.0f, 0.0f, 0.4f)));
                callback.moverAux = aux;
                aux->release();
            }
            chefs[i]->setMass(1.0f);
            chefs[i]->setMassSpaceInertiaTensor(PxVec3(1.0f));
            chefs[i]->setLinearDamping(0.0f);
            chefs[i]->setAngularDamping(0.0f);
            chefs[i]->userData = reinterpret_cast<void*>(
                static_cast<std::uintptr_t>(kMover + i));
            scene->addActor(*chefs[i]);
            actors.push_back(chefs[i]);
        }
        idle = rt.physics->createRigidDynamic(
            PxTransform(PxVec3(100.0f, 0.8f, 0.0f)));
        if (!idle) fail("create isolated active body");
        PxShape* far = rt.physics->createShape(
            PxSphereGeometry(0.25f), *rt.material);
        if (!far) fail("create isolated shape");
        setRole(*far, Idle);
        idle->attachShape(*far);
        far->release();
        idle->setMass(1.0f);
        idle->setMassSpaceInertiaTensor(PxVec3(1.0f));
        idle->setWakeCounter(100.0f);
        idle->userData = reinterpret_cast<void*>(
            static_cast<std::uintptr_t>(kIdleBody));
        scene->addActor(*idle);
        actors.push_back(idle);
    }

    static void setRole(PxShape& shape, Role role)
    {
        PxFilterData data;
        data.word0 = role;
        shape.setSimulationFilterData(data);
    }

    void addStatic(PxU32 actorId, Role role, PxReal x, PxReal z,
                   PxReal halfX, bool trigger)
    {
        PxRigidStatic* fixed = runtime.physics->createRigidStatic(
            PxTransform(PxVec3(x, 0.0f, z)));
        if (!fixed) fail("create static actor");
        PxShape* shape = runtime.physics->createShape(
            PxBoxGeometry(halfX, 0.5f, 0.5f), *runtime.material);
        if (!shape) fail("create static box");
        setRole(*shape, role);
        if (trigger)
        {
            shape->setFlag(PxShapeFlag::eSIMULATION_SHAPE, false);
            shape->setFlag(PxShapeFlag::eTRIGGER_SHAPE, true);
        }
        fixed->attachShape(*shape);
        shape->release();
        fixed->userData = reinterpret_cast<void*>(
            static_cast<std::uintptr_t>(actorId));
        scene->addActor(*fixed);
        actors.push_back(fixed);
    }

    ~World()
    {
        for (PxRigidActor* actor : actors) actor->release();
        scene->release();
    }

    Snapshot step(PxReal movingZ)
    {
        callback.rows.clear();
        const PxReal x[4] = {0.0f, -2.2f, -4.4f, -6.6f};
        for (PxU32 i = 0; i < 4; ++i)
        {
            chefs[i]->setGlobalPose(PxTransform(PxVec3(
                x[i], 0.8f, i == 0 ? movingZ : 0.0f)));
            chefs[i]->setLinearVelocity(PxVec3(0.0f));
            chefs[i]->setAngularVelocity(PxVec3(0.0f));
        }
        idle->setWakeCounter(100.0f);
        scene->simulate(kStep);
        if (!scene->fetchResults(true)) fail("fetchResults");
        Snapshot image;
        std::string error;
        if (!physx333_offline::CaptureOracle(*scene, image.oracle, error))
            fail("CaptureOracle: " + error);
        if (!captureGraph(*scene, image.graph, error))
            fail("captureGraph: " + error);
        if (!physx333_offline::CaptureAuxInteractionImage(
                *scene, image.aux, error))
            fail("CaptureAuxInteractionImage: " + error);
        oc2::offline::TransformCacheImage cache;
        if (!oc2::offline::CaptureTransformCache(*scene, cache, error))
            fail("CaptureTransformCache: " + error);
        image.facts.cacheCurrent = cache.currentId;
        if (cache.freeIds.bytes.size() < cache.freeIds.size * sizeof(PxU32))
            fail("TransformCache free-ID array is too short");
        for (PxU32 i = 0; i < cache.freeIds.size; ++i)
        {
            PxU32 freeId = 0;
            std::memcpy(&freeId, cache.freeIds.bytes.data() +
                        i * sizeof(freeId), sizeof(freeId));
            image.facts.cacheFreeIds.push_back(freeId);
        }
        if (cache.referenceCounts.bytes.size() <
            cache.currentId * sizeof(PxU32))
            fail("TransformCache refcount array is too short");
        for (PxU32 i = 0; i < cache.currentId; ++i)
        {
            PxU32 refs = 0;
            std::memcpy(&refs, cache.referenceCounts.bytes.data() +
                        i * sizeof(refs), sizeof(refs));
            if (refs) ++image.facts.cacheLive;
            image.facts.cacheRefs += refs;
        }
        oc2::offline::SapImage sap;
        if (!oc2::offline::CaptureSap(*scene, sap, error))
            fail("CaptureSap: " + error);
        image.facts.sapPairs = sapScalar(sap, "pair.activeCount");
        image.facts.sapDeletes = sapScalar(sap, "aabb.deletedOverlapSize");
        image.deletedOverlaps = deletedOverlapKeys(*scene, sap);
        oc2::offline::IslandImage island;
        if (!oc2::offline::CaptureIsland(*scene, island, error))
            fail("CaptureIsland: " + error);
        for (const auto& binding : island.bindings)
            if (binding.kind == oc2::offline::IslandImage::Binding::Edge)
                ++image.facts.islandEdges;
        NpScene& np = static_cast<NpScene&>(*scene);
        Sc::InteractionScene& interactions = np.getScene().getScScene()
            .getInteractionScene();
        PxsContext* context = interactions.getLowLevelContext();
        image.facts.largeManifolds = context->mManifoldPool.mUsed;
        image.facts.sphereManifolds = context->mSphereManifoldPool.mUsed;
        const auto& cm = part(image.oracle, "contact.managers");
        if (cm.size() % 14) fail("contact manager row layout changed");
        image.facts.contactManagers = static_cast<PxU32>(cm.size() / 14);
        std::set<const Sc::ActorPair*> seen;
        Cm::Range<Sc::Interaction*const> overlaps = interactions.getInteractions(
            Sc::PX_INTERACTION_TYPE_OVERLAP);
        while (!overlaps.empty())
        {
            const Sc::ShapeInstancePairLL* sip =
                static_cast<const Sc::ShapeInstancePairLL*>(overlaps.front());
            overlaps.popFront();
            const Sc::ActorPair* actorPair = sip->getActorPair();
            if (seen.insert(actorPair).second)
            {
                if (actorPair->mTouchCount) ++image.facts.touchPairs;
                if (actorPair->mReportData) ++image.facts.reportPairs;
            }
        }
        image.events = callback.rows;
        return image;
    }
};

std::set<PairKey> expectedPairs(bool successor)
{
    std::set<PairKey> result;
    for (PxU32 chef = kMover; chef <= kOtherLast; ++chef)
        for (PxU32 fixed = kCommonA; fixed <= kCommonB; ++fixed)
            result.insert(pair(Sc::PX_INTERACTION_TYPE_OVERLAP,
                               {chef, 0}, {fixed, 0}));
    if (!successor)
        for (PxU32 fixed = kExtraFirst; fixed <= kExtraLast; ++fixed)
            result.insert(pair(Sc::PX_INTERACTION_TYPE_OVERLAP,
                               {kMover, 0}, {fixed, 0}));
    for (PxU32 fixed = kTriggerA; fixed <= kTriggerB; ++fixed)
    {
        if (!successor)
            result.insert(pair(Sc::PX_INTERACTION_TYPE_TRIGGER,
                               {kMover, 0}, {fixed, 0}));
        result.insert(pair(Sc::PX_INTERACTION_TYPE_TRIGGER,
                           {kMover, 1}, {fixed, 0}));
    }
    result.insert(pair(Sc::PX_INTERACTION_TYPE_MARKER,
                       {kMover, 1}, {4, 0}));
    result.insert(pair(Sc::PX_INTERACTION_TYPE_MARKER,
                       {kMover, 1}, {5, 0}));
    return result;
}

PairKey graphKey(const physx333_offline::AuxPairKey& key)
{
    // The auxiliary image preserves PhysX's endpoint orientation. The graph
    // image intentionally canonicalizes its semantic pair keys.
    return pair(key.type,
                {key.shape0.actorId, key.shape0.shapeIndex},
                {key.shape1.actorId, key.shape1.shapeIndex});
}

void verifyAux(const Snapshot& s, bool successor)
{
    const auto& aux = s.aux;
    const PxU32 expectedTriggers = successor ? 2u : 4u;
    if (aux.triggerPool.usedCount != expectedTriggers ||
        aux.markerPool.usedCount != 2 ||
        aux.triggers.size() != expectedTriggers || aux.markers.size() != 2 ||
        aux.sceneOrders.size() != 3 ||
        aux.actorOrders.size() != s.graph.actors.size())
        fail("aux physical pool or interaction inventory differs");

    const PxU32 types[3] = {
        Sc::PX_INTERACTION_TYPE_OVERLAP,
        Sc::PX_INTERACTION_TYPE_TRIGGER,
        Sc::PX_INTERACTION_TYPE_MARKER
    };
    std::map<PxU32, const physx333_offline::AuxActorOrder*> actorOrders;
    for (const auto& actor : aux.actorOrders)
        if (!actorOrders.insert(std::make_pair(actor.actorId, &actor)).second)
            fail("aux actor order contains a duplicate identity");
    for (PxU32 i = 0; i < s.graph.actors.size(); ++i)
    {
        const auto& graphActor = s.graph.actors[i];
        const auto& auxActor = aux.actorOrders[i];
        if (graphActor.id != auxActor.actorId ||
            graphActor.pairs.size() != auxActor.pairs.size())
            fail("aux mixed actor order differs from graph actor order");
        for (PxU32 p = 0; p < graphActor.pairs.size(); ++p)
            if (!(graphActor.pairs[p] == graphKey(auxActor.pairs[p])))
                fail("aux oriented actor endpoint differs from graph key");
    }
    PxU32 graphIndex = 0;
    for (PxU32 typeIndex = 0; typeIndex < 3; ++typeIndex)
    {
        const auto& order = aux.sceneOrders[typeIndex];
        const PxU32 type = types[typeIndex];
        if (order.type != type ||
            order.activeCount != s.graph.activeCounts[type] ||
            order.pairs.size() != s.graph.counts[type])
            fail("aux scene type/count/active prefix differs from graph");
        for (const auto& oriented : order.pairs)
        {
            if (graphIndex >= s.graph.scenePairs.size() ||
                !(graphKey(oriented) == s.graph.scenePairs[graphIndex++]))
                fail("aux oriented scene endpoint differs from graph key");
        }
    }
    if (graphIndex != s.graph.scenePairs.size())
        fail("aux scene order omits a graph interaction");

    std::set<PxU32> triggerSlots, markerSlots;
    for (PxU32 i = 0; i < aux.triggers.size(); ++i)
    {
        const auto& row = aux.triggers[i];
        if (!(row.pair == aux.sceneOrders[1].pairs[i]) ||
            row.sceneIndex != i || row.active != 1 ||
            row.pair.shape0.shapeIndex != 0 ||
            (row.pair.shape0.actorId != kTriggerA &&
             row.pair.shape0.actorId != kTriggerB) ||
            row.pair.shape1.actorId != kMover ||
            row.pair.shape1.shapeIndex > 1 ||
            row.lastFrameHadContacts != 1 ||
            row.triggerCacheState != 0 ||
            !triggerSlots.insert(row.poolSlot).second)
            fail("oriented trigger slot/history differs");
        const auto a = actorOrders.find(row.pair.shape0.actorId);
        const auto b = actorOrders.find(row.pair.shape1.actorId);
        if (a == actorOrders.end() || b == actorOrders.end() ||
            row.actorIndex0 >= a->second->pairs.size() ||
            row.actorIndex1 >= b->second->pairs.size() ||
            !(a->second->pairs[row.actorIndex0] == row.pair) ||
            !(b->second->pairs[row.actorIndex1] == row.pair))
            fail("trigger actor reverse indices differ");
    }
    for (PxU32 i = 0; i < aux.markers.size(); ++i)
    {
        const auto& row = aux.markers[i];
        if (!(row.pair == aux.sceneOrders[2].pairs[i]) ||
            row.sceneIndex != i || row.active != 0 ||
            !markerSlots.insert(row.poolSlot).second ||
            row.triggerFlags || row.lastFrameHadContacts ||
            row.triggerCacheState)
            fail("marker slot/history differs");
        const auto a = actorOrders.find(row.pair.shape0.actorId);
        const auto b = actorOrders.find(row.pair.shape1.actorId);
        if (a == actorOrders.end() || b == actorOrders.end() ||
            row.actorIndex0 >= a->second->pairs.size() ||
            row.actorIndex1 >= b->second->pairs.size() ||
            !(a->second->pairs[row.actorIndex0] == row.pair) ||
            !(b->second->pairs[row.actorIndex1] == row.pair))
            fail("marker actor reverse indices differ");
    }
}

void verifyAuxTransition(const Snapshot& a, const Snapshot& b)
{
    if (!(a.aux.markerPool == b.aux.markerPool) ||
        a.aux.triggerPool.slabCount != b.aux.triggerPool.slabCount ||
        a.aux.triggerPool.elementsPerSlab !=
            b.aux.triggerPool.elementsPerSlab ||
        b.aux.triggerPool.freeOrder.size() !=
            a.aux.triggerPool.freeOrder.size() + 2)
        fail("auxiliary physical pool transition differs");
    for (const auto& before : a.aux.triggers)
    {
        const auto survivor = std::find_if(b.aux.triggers.begin(),
            b.aux.triggers.end(), [&before](
                const physx333_offline::AuxInteractionRow& row) {
                return row.pair == before.pair;
            });
        if (before.pair.shape1.shapeIndex == 0)
        {
            if (survivor != b.aux.triggers.end() ||
                std::find(b.aux.triggerPool.freeOrder.begin(),
                          b.aux.triggerPool.freeOrder.end(),
                          before.poolSlot) == b.aux.triggerPool.freeOrder.end())
                fail("deleted trigger physical slot was not returned to pool");
        }
        else if (survivor == b.aux.triggers.end() ||
                 survivor->poolSlot != before.poolSlot)
            fail("surviving trigger changed physical pool slot");
    }
    for (const auto& before : a.aux.markers)
    {
        const auto survivor = std::find_if(b.aux.markers.begin(),
            b.aux.markers.end(), [&before](
                const physx333_offline::AuxInteractionRow& row) {
                return row.pair == before.pair;
            });
        if (survivor == b.aux.markers.end() ||
            survivor->poolSlot != before.poolSlot)
            fail("surviving marker changed physical pool slot");
    }
}

void verify(const Snapshot& s, bool successor)
{
    verifyAux(s, successor);
    const PxU32 contacts = successor ? 8u : 12u;
    const PxU32 triggers = successor ? 2u : 4u;
    if (s.graph.counts.size() != Sc::PX_INTERACTION_TYPE_COUNT ||
        s.graph.counts[0] != contacts || s.graph.counts[1] != 0 ||
        s.graph.counts[2] != triggers || s.graph.counts[3] != 2 ||
        s.graph.counts[4] != 0 || s.graph.counts[5] != 0 ||
        s.graph.activeCounts[0] != contacts ||
        s.graph.activeCounts[2] != triggers ||
        s.graph.activeCounts[3] != 0)
        fail("12/4/2 -> 8/2/2 interaction counts/active prefixes differ");
    if (s.graph.activeBodies.size() != 5)
        fail("five active rigid bodies were not retained");
    std::set<PairKey> actual(s.graph.scenePairs.begin(),
                             s.graph.scenePairs.end());
    if (actual != expectedPairs(successor) ||
        actual.size() != s.graph.scenePairs.size())
        fail("interaction semantic key graph differs");
    std::map<PxU32, PxU32> actorCounts;
    for (const auto& actor : s.graph.actors)
        actorCounts[actor.id] = static_cast<PxU32>(actor.pairs.size());
    if (actorCounts.size() != 13 ||
        actorCounts[kMover] != (successor ? 6u : 12u) ||
        actorCounts[kIdleBody] != 0 ||
        actorCounts[kOtherFirst] != 2 ||
        actorCounts[kOtherFirst + 1] != 2 ||
        actorCounts[kOtherLast] != 2 ||
        actorCounts[kCommonA] != 4 || actorCounts[kCommonB] != 4 ||
        actorCounts[4] != (successor ? 1u : 2u) ||
        actorCounts[5] != (successor ? 1u : 2u) ||
        actorCounts[kTriggerA] != (successor ? 1u : 2u) ||
        actorCounts[kTriggerB] != (successor ? 1u : 2u))
        fail("per-actor graph degree differs from f444 pattern");
    if (s.facts.cacheLive != (successor ? 6u : 10u) ||
        s.facts.cacheRefs != (successor ? 16u : 24u) ||
        s.facts.cacheFreeIds != std::vector<PxU32>() ||
        s.facts.contactManagers != contacts ||
        s.facts.largeManifolds != contacts ||
        s.facts.sphereManifolds != 0 ||
        s.facts.islandEdges != contacts ||
        s.facts.touchPairs != (successor ? 8u : 10u) ||
        s.facts.reportPairs != (successor ? 8u : 10u))
        fail("cache, manifold, island, or mixed report facts differ");
    if (successor && s.facts.sapDeletes != 6)
        fail("settled B AABB manager did not retain six deleted overlaps");
    std::set<PairKey> expectedDeleted;
    if (successor)
    {
        for (PxU32 fixed = kExtraFirst; fixed <= kExtraLast; ++fixed)
            expectedDeleted.insert(pair(Sc::PX_INTERACTION_TYPE_OVERLAP,
                                        {kMover, 0}, {fixed, 0}));
        for (PxU32 fixed = kTriggerA; fixed <= kTriggerB; ++fixed)
            expectedDeleted.insert(pair(Sc::PX_INTERACTION_TYPE_TRIGGER,
                                        {kMover, 0}, {fixed, 0}));
    }
    if (std::set<PairKey>(s.deletedOverlaps.begin(),
                          s.deletedOverlaps.end()) != expectedDeleted ||
        s.deletedOverlaps.size() != expectedDeleted.size())
        fail("ordered broadphase deletion rows have wrong semantic keys");
    PxU32 persistentContacts = 0, lostContacts = 0, lostTriggers = 0;
    for (const Event& event : s.events)
    {
        if (event.kind == 'C' && event.flags == 8) ++persistentContacts;
        else if (event.kind == 'C' && event.flags == 16) ++lostContacts;
        else if (event.kind == 'T' && event.flags == 16) ++lostTriggers;
        else fail("unexpected event type or flags at settled boundary");
    }
    if (persistentContacts != (successor ? 8u : 10u) ||
        lostContacts != (successor ? 2u : 0u) ||
        lostTriggers != (successor ? 2u : 0u))
        fail("ordered callback stream has wrong contact/trigger classes");
}

void print(const char* name, const Snapshot& s)
{
    std::cout << name << " graph=" << s.graph.counts[0] << '/'
              << s.graph.counts[2] << '/' << s.graph.counts[3]
              << " active_bodies=" << s.graph.activeBodies.size()
              << " cache=" << s.facts.cacheLive << '/'
              << s.facts.cacheRefs << " current_id=" << s.facts.cacheCurrent
              << " free_ids=" << s.facts.cacheFreeIds.size()
              << " manifolds=" << s.facts.largeManifolds << '/'
              << s.facts.sphereManifolds
              << " island_edges=" << s.facts.islandEdges
              << " sap_pairs=" << s.facts.sapPairs
              << " aabb_deleted=" << s.facts.sapDeletes
              << " touch_reports=" << s.facts.touchPairs << '/'
              << s.facts.reportPairs << '\n';
    for (const PairKey& p : s.deletedOverlaps)
        std::cout << "DELETE " << name << " type=" << p.type
                  << " actors=" << p.a.actor << ':' << p.a.shape
                  << ',' << p.b.actor << ':' << p.b.shape << '\n';
    for (const auto& e : s.events)
        std::cout << "EVENT " << name << ' ' << e.kind
                  << " chef=" << e.chef << " static=" << e.fixed
                  << " dynamic_shape=" << e.dynamicShape
                  << " flags=" << e.flags << '\n';
}

} // namespace

#ifndef OC2_LEVEL_GRAPH_NO_MAIN
int main()
{
    Runtime runtime;
    World first(runtime);
    first.step(0.0f);
    const Snapshot a = first.step(0.0f);
    print("A", a);
    verify(a, false);
    const Snapshot b = first.step(-0.2f);
    print("B", b);
    verify(b, true);
    verifyAuxTransition(a, b);
    World fresh(runtime);
    fresh.step(0.0f);
    const Snapshot freshA = fresh.step(0.0f);
    const Snapshot freshB = fresh.step(-0.2f);
    std::string difference;
    if (!a.oracle.equals(freshA.oracle, difference) ||
        !b.oracle.equals(freshB.oracle, difference) ||
        !(a.graph == freshA.graph) || !(b.graph == freshB.graph) ||
        !(a.facts == freshA.facts) || !(b.facts == freshB.facts) ||
        a.deletedOverlaps != freshA.deletedOverlaps ||
        b.deletedOverlaps != freshB.deletedOverlaps ||
        a.events != freshA.events || b.events != freshB.events)
        fail("fresh-scene A/B Oracle, graph, component facts, or callback order differs: " +
             difference);
    if (!a.aux.equals(freshA.aux, difference) ||
        !b.aux.equals(freshB.aux, difference))
        fail("fresh-scene A/B trigger/marker physical images differ: " +
             difference);
    if (runtime.errors.count) fail("PhysX issued an error");
    std::cout << "PASS level-like shared-endpoint 12/4/2 -> 8/2/2 graph, "
                 "six SAP deletions, cache/manifold/island facts, and "
                 "fresh-scene ordered callback/Oracle/auxiliary equality\n";
}
#endif
