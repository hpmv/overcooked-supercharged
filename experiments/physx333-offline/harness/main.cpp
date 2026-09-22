// Standalone PhysX 3.3.3 rewind fixture. This executable never loads the game.
// The private-header access below is test-only and is limited to observation.
#include <algorithm>
#include <cstdint>
#include <cstdlib>
#include <cstring>
#include <iomanip>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <vector>

#include "PxPhysicsAPI.h"
#include "NpScene.h"
#include "ScInteractionScene.h"
#include "PxsContext.h"
#include "../sap/SapImage.h"
#include "../oracle/Oracle.h"
#include "../cache/TransformCacheImage.h"
#include "../shape_cache/ShapeCacheBindings.h"
#include "../nphase/NPhaseTopology.h"
#include "../island/IslandImage.h"
#include "../memblock/MemBlockImage.h"
#include "../memblock_restore/MemBlockRestore.h"
#include "../interaction/InteractionImage.h"
#include "../body/BodyImage.h"
#include "../scene_clock/SceneClockImage.h"
#include "../context_image/ContextImage.h"
#include "../query_image/QueryImage.h"

// Only this translation unit opens the original 3.3.3 access labels. These
// declarations retain their original field order and are used read-only.
#define private public
#define protected public
#include "PxsAABBManager.h"
#include "PxsBroadPhaseSap.h"
#undef protected
#undef private

using namespace physx;

static_assert(sizeof(void*) == 4, "The offline parity fixture must use Win32 PhysX");
static_assert(sizeof(PxcBpHandle) == 4, "Unexpected broadphase handle width");
static_assert(sizeof(IntegerAABB) == 24, "Unexpected IntegerAABB layout");
static_assert(sizeof(SapBox1D) == 8, "Unexpected SAP box layout");
static_assert(sizeof(PxcBroadPhasePair) == 8, "Unexpected SAP pair layout");

namespace {

const PxReal kStep = 1.0f / 60.0f;
const PxU32 kPairCount = 6;

void die(const std::string& message)
{
    std::cerr << "FAIL: " << message << "\n";
    std::exit(1);
}

PxU32 bits(PxReal value)
{
    PxU32 result = 0;
    static_assert(sizeof(result) == sizeof(value), "Expected 32-bit PxReal");
    std::memcpy(&result, &value, sizeof(result));
    return result;
}

struct Snapshot
{
    std::map<std::string, std::vector<PxU32> > parts;

    void add(const std::string& name, PxU32 value)
    {
        parts[name].push_back(value);
    }

    void addFloat(const std::string& name, PxReal value)
    {
        add(name, bits(value));
    }
};

bool compare(const Snapshot& expected, const Snapshot& actual,
             std::string& firstDifference)
{
    std::map<std::string, std::vector<PxU32> >::const_iterator a = expected.parts.begin();
    std::map<std::string, std::vector<PxU32> >::const_iterator b = actual.parts.begin();
    while (a != expected.parts.end() && b != actual.parts.end())
    {
        if (a->first != b->first)
        {
            firstDifference = "section name " + a->first + " vs " + b->first;
            return false;
        }
        if (a->second.size() != b->second.size())
        {
            std::ostringstream out;
            out << a->first << " size " << a->second.size()
                << " vs " << b->second.size();
            firstDifference = out.str();
            return false;
        }
        for (size_t i = 0; i != a->second.size(); ++i)
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
        ++a;
        ++b;
    }
    if (a != expected.parts.end() || b != actual.parts.end())
    {
        firstDifference = "section count differs";
        return false;
    }
    return true;
}

struct ErrorCallback : PxErrorCallback
{
    PxU32 errors;
    ErrorCallback() : errors(0) {}

    virtual void reportError(PxErrorCode::Enum code, const char* message,
                             const char* file, int line)
    {
        ++errors;
        std::cerr << "PhysX " << static_cast<int>(code) << ": " << message
                  << " (" << file << ':' << line << ")\n";
    }
};

struct InlineDispatcher : PxCpuDispatcher
{
    virtual void submitTask(PxBaseTask& task)
    {
        task.run();
        task.release();
    }
    virtual PxU32 getWorkerCount() const { return 0; }
};

PxFilterFlags reportFilter(PxFilterObjectAttributes, PxFilterData,
                           PxFilterObjectAttributes, PxFilterData,
                           PxPairFlags& pairFlags, const void*, PxU32)
{
    pairFlags = PxPairFlag::eCONTACT_DEFAULT |
                PxPairFlag::eNOTIFY_TOUCH_FOUND |
                PxPairFlag::eNOTIFY_TOUCH_PERSISTS |
                PxPairFlag::eNOTIFY_TOUCH_LOST;
    return PxFilterFlag::eDEFAULT;
}

struct ContactEvents : PxSimulationEventCallback
{
    std::vector<PxU32> rows;

    virtual void onConstraintBreak(PxConstraintInfo*, PxU32) {}
    virtual void onWake(PxActor**, PxU32) {}
    virtual void onSleep(PxActor**, PxU32) {}
    virtual void onTrigger(PxTriggerPair*, PxU32) {}

    virtual void onContact(const PxContactPairHeader& header,
                           const PxContactPair* pairs, PxU32 count)
    {
        const PxU32 id0 = header.actors[0]
            ? static_cast<PxU32>(reinterpret_cast<uintptr_t>(header.actors[0]->userData)) : 0;
        const PxU32 id1 = header.actors[1]
            ? static_cast<PxU32>(reinterpret_cast<uintptr_t>(header.actors[1]->userData)) : 0;
        for (PxU32 i = 0; i != count; ++i)
        {
            rows.push_back(PxMin(id0, id1));
            rows.push_back(PxMax(id0, id1));
            rows.push_back(static_cast<PxU16>(pairs[i].events));
            rows.push_back(pairs[i].contactCount);
        }
    }
};

struct Runtime
{
    PxDefaultAllocator allocator;
    ErrorCallback errors;
    InlineDispatcher dispatcher;
    PxFoundation* foundation;
    PxPhysics* physics;
    PxMaterial* material;

    Runtime() : foundation(NULL), physics(NULL), material(NULL)
    {
        foundation = PxCreateFoundation(PX_PHYSICS_VERSION, allocator, errors);
        if (!foundation) die("PxCreateFoundation failed");
        physics = PxCreatePhysics(PX_PHYSICS_VERSION, *foundation, PxTolerancesScale());
        if (!physics) die("PxCreatePhysics failed");
        material = physics->createMaterial(0.5f, 0.5f, 0.0f);
        if (!material) die("createMaterial failed");
    }

    ~Runtime()
    {
        material->release();
        physics->release();
        foundation->release();
    }
};

struct PublicBodyState
{
    PxTransform pose;
    PxVec3 linear;
    PxVec3 angular;
    PxReal wakeCounter;
    bool sleeping;
};

struct World
{
    Runtime& runtime;
    ContactEvents events;
    PxScene* scene;
    PxRigidDynamic* mover;
    std::vector<PxRigidActor*> actors;

    explicit World(Runtime& value) : runtime(value), scene(NULL), mover(NULL)
    {
        PxSceneDesc desc(runtime.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &runtime.dispatcher;
        desc.filterShader = reportFilter;
        desc.simulationEventCallback = &events;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        desc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
        desc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
        scene = runtime.physics->createScene(desc);
        if (!scene) die("createScene failed");

        for (PxU32 i = 0; i != kPairCount; ++i)
        {
            const PxReal x = static_cast<PxReal>(i) * 3.0f;
            PxRigidStatic* fixed = runtime.physics->createRigidStatic(
                PxTransform(PxVec3(x, 0.0f, 0.0f)));
            if (!fixed) die("createRigidStatic failed");
            PxShape* shape = runtime.physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *runtime.material);
            if (!shape) die("create static shape failed");
            fixed->attachShape(*shape);
            shape->release();
            fixed->userData = reinterpret_cast<void*>(static_cast<uintptr_t>(i + 1));
            scene->addActor(*fixed);
            actors.push_back(fixed);
        }

        mover = runtime.physics->createRigidDynamic(
            PxTransform(PxVec3(0.0f, 0.95f, 0.0f)));
        if (!mover) die("createRigidDynamic failed");
        for (PxU32 i = 0; i != kPairCount; ++i)
        {
            PxShape* shape = runtime.physics->createShape(
                PxBoxGeometry(0.5f, 0.5f, 0.5f), *runtime.material);
            if (!shape) die("create dynamic shape failed");
            mover->attachShape(*shape);
            shape->setLocalPose(PxTransform(PxVec3(static_cast<PxReal>(i) * 3.0f, 0.0f, 0.0f)));
            shape->release();
        }
        mover->setMass(6.0f);
        mover->setMassSpaceInertiaTensor(PxVec3(18.0f, 18.0f, 18.0f));
        mover->setLinearDamping(0.0f);
        mover->setAngularDamping(0.0f);
        mover->userData = reinterpret_cast<void*>(static_cast<uintptr_t>(kPairCount + 1));
        scene->addActor(*mover);
        actors.push_back(mover);
    }

    ~World()
    {
        for (size_t i = 0; i != actors.size(); ++i)
            actors[i]->release();
        scene->release();
    }

    PublicBodyState saveBody() const
    {
        PublicBodyState result;
        result.pose = mover->getGlobalPose();
        result.linear = mover->getLinearVelocity();
        result.angular = mover->getAngularVelocity();
        result.wakeCounter = mover->getWakeCounter();
        result.sleeping = mover->isSleeping();
        return result;
    }

    void restorePublicBody(const PublicBodyState& state)
    {
        mover->setGlobalPose(state.pose);
        mover->setLinearVelocity(state.linear);
        mover->setAngularVelocity(state.angular);
        mover->setWakeCounter(state.wakeCounter);
        if (state.sleeping) mover->putToSleep();
    }

    void step(bool away)
    {
        events.rows.clear();
        const PxReal wantedZ = away ? 5.0f : 0.0f;
        mover->setGlobalPose(PxTransform(PxVec3(0.0f, 0.95f, wantedZ)));
        mover->setLinearVelocity(PxVec3(0.0f));
        mover->setAngularVelocity(PxVec3(0.0f));
        scene->simulate(kStep);
        if (!scene->fetchResults(true)) die("fetchResults returned false");
    }

    Snapshot capture() const
    {
        Snapshot result;
        const PxTransform pose = mover->getGlobalPose();
        const PxVec3 linear = mover->getLinearVelocity();
        const PxVec3 angular = mover->getAngularVelocity();
        const PxReal bodyFloats[] = {
            pose.p.x, pose.p.y, pose.p.z, pose.q.x, pose.q.y, pose.q.z, pose.q.w,
            linear.x, linear.y, linear.z, angular.x, angular.y, angular.z,
            mover->getWakeCounter()
        };
        for (size_t i = 0; i != sizeof(bodyFloats) / sizeof(bodyFloats[0]); ++i)
            result.addFloat("body", bodyFloats[i]);
        result.add("body", mover->isSleeping() ? 1u : 0u);

        for (size_t i = 0; i != events.rows.size(); ++i)
            result.add("contact.events", events.rows[i]);
        result.add("contact.event_words", static_cast<PxU32>(events.rows.size()));

        PxSimulationStatistics stats;
        scene->getSimulationStatistics(stats);
        result.add("scene.counts", stats.nbActiveDynamicBodies);
        result.add("scene.counts", stats.nbDynamicBodies);
        for (PxU32 i = 0; i != PxGeometryType::eGEOMETRY_COUNT; ++i)
            for (PxU32 j = 0; j != PxGeometryType::eGEOMETRY_COUNT; ++j)
                result.add("scene.contact_pairs", stats.nbDiscreteContactPairs[i][j]);

        NpScene& np = static_cast<NpScene&>(*scene);
        PxsContext* context = np.getScene().getScScene()
            .getInteractionScene().getLowLevelContext();
        if (!context) die("null PxsContext");
        PxsAABBManager* aabb = context->getAABBManager();
        if (!aabb) die("null PxsAABBManager");
        PxvBroadPhase* base = aabb->getBroadPhase();
        if (!base || base->getType() != PxBroadPhaseType::eSAP)
            die("fixture did not select SAP");
        const PxsBroadPhaseContextSap& sap = *static_cast<PxsBroadPhaseContextSap*>(base);
        const BPElems& elems = aabb->mBPElems;

        const PxU32 capacity = elems.getCapacity();
        if (capacity > 4096) die("unexpected BPElem capacity");
        std::vector<PxU32> freeMask(capacity, 0);
        PxU32 freeCount = 0;
        PxcBpHandle cursor = elems.getFirstFreeElem();
        result.add("aabb.meta", capacity);
        result.add("aabb.meta", cursor);
        while (cursor != PX_INVALID_BP_HANDLE)
        {
            if (cursor >= capacity || freeMask[cursor])
                die("BPElem free list is cyclic or out of range");
            freeMask[cursor] = 1;
            result.add("aabb.free_order", cursor);
            ++freeCount;
            cursor = elems.mGroups[cursor];
        }
        result.add("aabb.meta", freeCount);
        result.add("aabb.meta", capacity - freeCount);
        result.add("aabb.meta", elems.getStaticAABBDataArrayCapacity());
        result.add("aabb.meta", elems.getDynamicAABBDataArrayCapacity());

        for (PxU32 i = 0; i != capacity; ++i)
        {
            if (freeMask[i]) continue;
            result.add("aabb.active", i);
            result.add("aabb.active", elems.mGroups[i]);
            result.add("aabb.active", elems.mOwnerIds[i]);
            result.add("aabb.active", elems.mAABBDataHandles[i]);
            result.add("aabb.active", elems.mElemNextIds[i]);
            for (PxU32 j = 0; j != 6; ++j)
                result.add("aabb.active", elems.mBounds[i].mMinMax[j]);
        }

        result.add("sap.meta", sap.mBoxesSize);
        result.add("sap.meta", sap.mBoxesSizePrev);
        result.add("sap.meta", sap.mBoxesCapacity);
        result.add("sap.meta", sap.mEndPointsCapacity);
        result.add("sap.meta", sap.mCreatedPairsSize);
        result.add("sap.meta", sap.mDeletedPairsSize);
        result.add("sap.meta", sap.mPairs.mNbActivePairs);
        result.add("sap.meta", sap.mPairs.mActivePairsCapacity);
        result.add("sap.meta", sap.mPairs.mHashSize);
        result.add("sap.meta", sap.mPairs.mHashCapacity);
        result.add("sap.meta", sap.mPairs.mMask);

        if (sap.mBoxesSize != capacity - freeCount)
            die("SAP box count differs from active BPElem count");
        const PxU32 endpointCount = 2 * sap.mBoxesSize + NUM_SENTINELS;
        if (endpointCount > sap.mEndPointsCapacity)
            die("SAP endpoint count exceeds capacity");
        for (PxU32 axis = 0; axis != 3; ++axis)
        {
            std::ostringstream axisName;
            axisName << "sap.axis" << axis;
            for (PxU32 i = 0; i != endpointCount; ++i)
            {
                result.add(axisName.str(), sap.mEndPointValues[axis][i]);
                result.add(axisName.str(), sap.mEndPointDatas[axis][i]);
            }
            for (PxU32 i = 0; i != capacity; ++i)
            {
                if (freeMask[i]) continue;
                result.add(axisName.str(), i);
                result.add(axisName.str(), sap.mBoxEndPts[axis][i].mMinMax[0]);
                result.add(axisName.str(), sap.mBoxEndPts[axis][i].mMinMax[1]);
            }
        }
        if (sap.mPairs.mNbActivePairs > sap.mPairs.mActivePairsCapacity ||
            sap.mPairs.mHashSize > sap.mPairs.mHashCapacity)
            die("SAP pair manager count exceeds capacity");
        for (PxU32 i = 0; i != sap.mPairs.mHashSize; ++i)
            result.add("sap.hash", sap.mPairs.mHashTable[i]);
        for (PxU32 i = 0; i != sap.mPairs.mNbActivePairs; ++i)
        {
            const PxcBroadPhasePair& pair = sap.mPairs.mActivePairs[i];
            result.add("sap.pairs", pair.mVolA);
            result.add("sap.pairs", pair.mVolB);
            result.add("sap.pairs", sap.mPairs.mNext[i]);
            result.add("sap.pairs", sap.mPairs.mActivePairStates[i]);
        }
        return result;
    }
};

void requireEqual(const char* label, const Snapshot& expected, const Snapshot& actual)
{
    std::string difference;
    if (!compare(expected, actual, difference))
        die(std::string(label) + ": " + difference);
    std::cout << "PASS " << label << "\n";
}

physx333_offline::OracleImage captureOracle(World& world)
{
    physx333_offline::OracleImage image;
    std::string error;
    if (!physx333_offline::CaptureOracle(*world.scene, image, error))
        die("NPhase/island oracle capture: " + error);
    return image;
}

void requireOracleEqual(const char* label,
                        const physx333_offline::OracleImage& expected,
                        const physx333_offline::OracleImage& actual)
{
    std::string difference;
    if (!expected.equals(actual, difference))
        die(std::string(label) + ": " + difference);
    std::cout << "PASS " << label << "\n";
}

std::string sixSlotOrder(const physx333_offline::OracleImage& image,
                         const char* section, size_t slotOffset)
{
    const auto found = image.parts.find(section);
    if (found == image.parts.end() || found->second.size() % kPairCount != 0)
        return "unavailable";
    const auto& words = found->second;
    const size_t stride = words.size() / kPairCount;
    if (slotOffset >= stride) return "unavailable";
    std::ostringstream out;
    for (size_t i = 0; i != kPairCount; ++i)
    {
        if (i) out << ',';
        out << words[i * stride + slotOffset];
    }
    return out.str();
}

std::string sixSlotsByMoverShape(const physx333_offline::OracleImage& image,
                                  const char* section, size_t slotOffset)
{
    const auto sip = image.parts.find("nphase.shape_pairs");
    const auto slots = image.parts.find(section);
    if (sip == image.parts.end() || slots == image.parts.end() ||
        sip->second.size() % kPairCount != 0 ||
        slots->second.size() % kPairCount != 0)
        return "unavailable";
    const size_t sipStride = sip->second.size() / kPairCount;
    const size_t slotStride = slots->second.size() / kPairCount;
    if (sipStride < 6 || slotOffset >= slotStride) return "unavailable";
    PxU32 byShape[kPairCount] = {};
    bool seen[kPairCount] = {};
    for (size_t i = 0; i != kPairCount; ++i)
    {
        const PxU32 shape = sip->second[i * sipStride + 3];
        if (sip->second[i * sipStride + 2] != 7 || shape >= kPairCount || seen[shape])
            return "unavailable";
        seen[shape] = true;
        byShape[shape] = slots->second[i * slotStride + slotOffset];
    }
    std::ostringstream out;
    for (size_t i = 0; i != kPairCount; ++i)
    {
        if (!seen[i]) return "unavailable";
        if (i) out << ',';
        out << byShape[i];
    }
    return out.str();
}

} // namespace

int main(int argc, char** argv)
{
    bool publicProbe = false;
    bool nphaseTopologyProbe = false;
    bool nphaseReverseProbe = false;
    bool interactionOrderProbe = false;
    bool interactionMetadataProbe = false;
    bool joinedPayloadProbe = false;
    bool joinedReplayProbe = false;
    bool allShapeBindingProbe = false;
    for (int i = 1; i < argc; ++i)
    {
        if (std::string(argv[i]) == "--public-rewind-probe") publicProbe = true;
        else if (std::string(argv[i]) == "--nphase-topology-probe") nphaseTopologyProbe = true;
        else if (std::string(argv[i]) == "--nphase-reverse-probe") nphaseReverseProbe = true;
        else if (std::string(argv[i]) == "--interaction-order-probe") interactionOrderProbe = true;
        else if (std::string(argv[i]) == "--interaction-metadata-probe") interactionMetadataProbe = true;
        else if (std::string(argv[i]) == "--joined-payload-probe") joinedPayloadProbe = true;
        else if (std::string(argv[i]) == "--joined-replay-probe")
        {
            joinedPayloadProbe = true;
            joinedReplayProbe = true;
        }
        else if (std::string(argv[i]) == "--all-shape-binding-probe")
            allShapeBindingProbe = true;
        else die(std::string("unknown argument: ") + argv[i]);
    }

    if (allShapeBindingProbe)
    {
        Runtime runtime;
        World world(runtime);
        PxShape* distant = runtime.physics->createShape(
            PxBoxGeometry(0.5f, 0.5f, 0.5f), *runtime.material);
        if (!distant) die("create contactless shape failed");
        distant->setLocalPose(PxTransform(PxVec3(100.0f, 0.0f, 0.0f)));
        world.mover->attachShape(*distant);
        distant->release();
        world.step(false);
        oc2::offline::ShapeCacheBindings saved;
        oc2::offline::ShapeCacheBindings repeated;
        std::string error;
        if (!oc2::offline::CaptureShapeCacheBindings(*world.scene, saved,
                                                      error) ||
            !oc2::offline::CaptureShapeCacheBindings(*world.scene, repeated,
                                                      error) ||
            !saved.equals(repeated, error))
            die("all-shape binding duplicate capture: " + error);
        if (saved.bindings.size() != 13)
            die("all-shape binding image omitted a contactless ShapeSim");
        if (!oc2::offline::RestoreShapeCacheBindings(*world.scene, saved,
                                                     error))
            die("all-shape binding same-image restore: " + error);
        oc2::offline::ShapeCacheBindings corrupt = saved;
        corrupt.bindings[0].shapeId ^= 1u;
        if (oc2::offline::RestoreShapeCacheBindings(*world.scene, corrupt,
                                                    error))
            die("all-shape binding corrupt identity was accepted");
        if (!oc2::offline::CaptureShapeCacheBindings(*world.scene, repeated,
                                                      error) ||
            !saved.equals(repeated, error))
            die("all-shape binding rejection mutated scene: " + error);
        std::cout << "PASS all 13 rigid ShapeSim bindings, including one contactless shape, duplicate capture and atomic rejection\n";
        return 0;
    }

    if (nphaseTopologyProbe || nphaseReverseProbe || interactionOrderProbe ||
        interactionMetadataProbe || joinedPayloadProbe)
    {
        if (publicProbe) die("NPhase topology probe is a separate process mode");
        Runtime runtime;
        // The lifecycle-only reconstruction does not yet restore SAP,
        // contact caches or islands. Keep this scene isolated and terminate
        // the process without simulating or tearing down an incoherent scene.
        World* source = new World(runtime);
        source->step(false);
        source->step(true);
        source->step(false);
        const physx333_offline::OracleImage checkpointOracle = captureOracle(*source);
        {
            const auto& reports = checkpointOracle.parts.at("nphase.report_buffer");
            const auto& eventLists = checkpointOracle.parts.at("nphase.event_lists");
            const auto& actors = checkpointOracle.parts.at("nphase.actor_pairs");
            std::cout << "NPHASE_REPORT cursor=" << reports[0]
                      << " capacity=" << reports[1]
                      << " default=" << reports[2]
                      << " last=" << reports[3]
                      << " locked=" << reports[4]
                      << " event_meta=" << eventLists[0] << ',' << eventLists[1]
                      << ',' << eventLists[2] << ',' << eventLists[3]
                      << " actor_report_slots=";
            for (size_t i = 0; i < kPairCount; ++i)
            {
                if (i) std::cout << ',';
                std::cout << actors[i * (actors.size() / kPairCount) + 7];
            }
            std::cout << "\n";
        }
        physx333_offline::NPhaseTopologyImage checkpointTopology;
        std::string error;
        if (!physx333_offline::CaptureNPhaseTopology(*source->scene,
                                                     checkpointTopology, error))
            die("NPhase checkpoint topology capture: " + error);
        physx333_offline::InteractionImage checkpointInteraction;
        if ((interactionOrderProbe || interactionMetadataProbe || joinedPayloadProbe) &&
            !physx333_offline::CaptureInteractionImage(*source->scene,
                                                        checkpointInteraction, error))
            die("interaction checkpoint capture: " + error);
        physx333_offline::MemBlockIdentityRegistry memBlockIds;
        physx333_offline::MemBlockRestoreImage checkpointMemBlocks;
        oc2::offline::IslandImage checkpointIsland;
        oc2::offline::SapImage checkpointSap;
        oc2::offline::TransformCacheImage checkpointCache;
        oc2::offline::ShapeCacheBindings checkpointShapeCache;
        oc2::offline::BodyImage checkpointBody;
        oc2::offline::SceneClockImage checkpointClock;
        oc2::offline::ContextImage checkpointContext;
        oc2::offline::QueryImage checkpointQuery;
        if (joinedPayloadProbe &&
            !physx333_offline::CaptureMemBlockRestore(*source->scene,
                memBlockIds, checkpointMemBlocks, error))
            die("joined checkpoint memory-block capture: " + error);
        if (joinedPayloadProbe &&
            !oc2::offline::CaptureIsland(*source->scene,
                                         checkpointIsland, error))
            die("joined checkpoint island capture: " + error);
        if (joinedPayloadProbe &&
            (!oc2::offline::CaptureSap(*source->scene, checkpointSap, error) ||
             !oc2::offline::CaptureTransformCache(*source->scene,
                                                  checkpointCache, error) ||
             !oc2::offline::CaptureShapeCacheBindings(*source->scene,
                checkpointShapeCache, error) ||
             !oc2::offline::CaptureBodies(*source->scene,
                                          checkpointBody, error) ||
             !oc2::offline::CaptureSceneClock(*source->scene,
                                              checkpointClock, error) ||
             !oc2::offline::CaptureContextImage(*source->scene,
                                                checkpointContext, error) ||
             (joinedReplayProbe &&
              !oc2::offline::CaptureQueryImage(*source->scene,
                                               checkpointQuery, error))))
            die("joined checkpoint component capture: " + error);
        physx333_offline::NPhaseTopologyImage requestedTopology = checkpointTopology;
        if (nphaseReverseProbe)
            std::reverse(requestedTopology.pairs.begin(), requestedTopology.pairs.end());
        source->step(true);
        Snapshot expectedDeletion;
        physx333_offline::OracleImage expectedDeletionOracle;
        oc2::offline::SapImage expectedDeletionSap;
        oc2::offline::TransformCacheImage expectedDeletionCache;
        oc2::offline::ShapeCacheBindings expectedDeletionShapeCache;
        oc2::offline::BodyImage expectedDeletionBody;
        oc2::offline::SceneClockImage expectedDeletionClock;
        oc2::offline::ContextImage expectedDeletionContext;
        oc2::offline::QueryImage expectedDeletionQuery;
        oc2::offline::IslandImage expectedDeletionIsland;
        physx333_offline::MemBlockRestoreImage expectedDeletionBlocks;
        if (joinedReplayProbe)
        {
            expectedDeletion = source->capture();
            expectedDeletionOracle = captureOracle(*source);
            if (!oc2::offline::CaptureSap(*source->scene,
                                          expectedDeletionSap, error) ||
                !oc2::offline::CaptureTransformCache(*source->scene,
                    expectedDeletionCache, error) ||
                !oc2::offline::CaptureShapeCacheBindings(*source->scene,
                    expectedDeletionShapeCache, error) ||
                !oc2::offline::CaptureBodies(*source->scene,
                    expectedDeletionBody, error) ||
                !oc2::offline::CaptureSceneClock(*source->scene,
                                                  expectedDeletionClock, error) ||
                !oc2::offline::CaptureContextImage(*source->scene,
                                                    expectedDeletionContext, error) ||
                !oc2::offline::CaptureQueryImage(*source->scene,
                                                  expectedDeletionQuery, error) ||
                !oc2::offline::CaptureIsland(*source->scene,
                                              expectedDeletionIsland, error) ||
                !physx333_offline::CaptureMemBlockRestore(*source->scene,
                    memBlockIds, expectedDeletionBlocks, error))
                die("joined deletion component capture: " + error);
        }
        physx333_offline::NPhaseTopologyImage deletedTopology;
        if (!physx333_offline::CaptureNPhaseTopology(*source->scene,
                                                     deletedTopology, error) ||
            !deletedTopology.pairs.empty())
            die("NPhase successor is not empty: " + error);
        physx333_offline::NPhaseTopologyImage badTopology = requestedTopology;
        badTopology.pairs[0].pairFlags ^= 1u;
        if (physx333_offline::RestoreNPhaseTopology(*source->scene,
                                                     badTopology, error))
            die("NPhase bridge accepted a mismatched filter pair");
        physx333_offline::NPhaseTopologyImage afterReject;
        if (!physx333_offline::CaptureNPhaseTopology(*source->scene,
                                                     afterReject, error) ||
            !deletedTopology.equals(afterReject, error))
            die("NPhase bridge preflight rejection changed the scene: " + error);
        std::cout << "PASS NPhase filter mismatch rejected before mutation\n";
        if (interactionOrderProbe || interactionMetadataProbe || joinedPayloadProbe)
        {
            if (!physx333_offline::RestoreInteractionOrder(*source->scene,
                                                           checkpointInteraction, error))
                die("interaction order reconstruction: " + error);
            physx333_offline::InteractionImage recreatedInteraction;
            if (!physx333_offline::CaptureInteractionImage(*source->scene,
                                                            recreatedInteraction, error) ||
                !checkpointInteraction.sameSlotsAndOrder(recreatedInteraction, error))
                die("interaction slot/order comparison: " + error);
            std::cout << "PASS interaction scene/actor and physical slot order restored\n";
            if (interactionMetadataProbe || joinedPayloadProbe)
            {
                if (!physx333_offline::RestoreInteractionMetadata(*source->scene,
                                                                    checkpointInteraction, error))
                    die("interaction metadata reconstruction: " + error);
                std::cout << "PASS interaction report/event/touch metadata restored\n";
            }
        }
        else if (!physx333_offline::RestoreNPhaseTopology(*source->scene,
                                                           requestedTopology, error))
            die("NPhase lifecycle reconstruction: " + error);
        physx333_offline::NPhaseTopologyImage recreated;
        if (!physx333_offline::CaptureNPhaseTopology(*source->scene,
                                                     recreated, error))
            die("NPhase recreated topology capture: " + error);
        if (!requestedTopology.sameShapePairs(recreated, error))
            die("NPhase reconstructed pair identities: " + error);
        const bool topologyExact = requestedTopology.equals(recreated, error);
        if (interactionMetadataProbe || joinedPayloadProbe)
        {
            if (!topologyExact)
                die("interaction metadata did not restore topology/touch state: " + error);
            std::cout << "PASS NPhase topology and touch metadata image\n";
        }
        else
        {
            if (topologyExact)
                die("NPhase lifecycle-only reconstruction unexpectedly restored full touch state");
            std::cout << "PASS NPhase lifecycle restores six requested shape pairs"
                      << (nphaseReverseProbe ? " (reverse order)" : "")
                      << "; touch/contact history remains different (" << error << ")\n";
        }
        if (joinedPayloadProbe)
        {
            physx333_offline::MemBlockRestoreImage recreatedMemBlocks;
            if (!physx333_offline::CaptureMemBlockRestore(*source->scene,
                    memBlockIds, recreatedMemBlocks, error))
                die("joined recreated memory-block capture: " + error);
            std::string difference;
            const bool poolEqual = checkpointMemBlocks.pool.equals(
                recreatedMemBlocks.pool, difference);
            std::cout << "JOINED_MEMBLOCK equal=" << poolEqual
                      << " first_difference=" << difference << "\n";
            for (size_t i = 0; i < checkpointMemBlocks.pool.arrays.size(); ++i)
                std::cout << "JOINED_ARRAY " << checkpointMemBlocks.pool.arrays[i].name
                          << " checkpoint=" << checkpointMemBlocks.pool.arrays[i].size
                          << "/" << checkpointMemBlocks.pool.arrays[i].capacity
                          << " recreated=" << recreatedMemBlocks.pool.arrays[i].size
                          << "/" << recreatedMemBlocks.pool.arrays[i].capacity
                          << " same_storage=" <<
                              (checkpointMemBlocks.pool.arrays[i].address ==
                               recreatedMemBlocks.pool.arrays[i].address)
                          << "\n";
            if (!physx333_offline::InstallInteractionContactBindings(
                    *source->scene, checkpointInteraction, error))
                die("joined contact binding installation: " + error);
            std::cout << "PASS joined contact binding installation\n";
            physx333_offline::MemBlockRestoreImage installedMemBlocks;
            if (!physx333_offline::CaptureMemBlockRestore(*source->scene,
                    memBlockIds, installedMemBlocks, error))
                die("joined installed memory-block capture: " + error);
            std::cout << "JOINED_BINDINGS equal=" <<
                (checkpointMemBlocks.contactBindings ==
                 installedMemBlocks.contactBindings) << "\n";
            for (size_t i = 0; i < checkpointMemBlocks.contactBindings.size(); ++i)
            {
                const auto& saved = checkpointMemBlocks.contactBindings[i];
                const auto& installed = installedMemBlocks.contactBindings[i];
                if (!(saved == installed))
                    std::cout << "JOINED_BINDING_DIFF slot=" << i
                              << " manager=" << saved.managerAddress << "/"
                              << installed.managerAddress
                              << " manifold=" << saved.manifold << "/"
                              << installed.manifold << "\n";
            }
            if (!physx333_offline::RestoreMemBlockPoolForJoin(*source->scene,
                    memBlockIds, checkpointMemBlocks, error))
                die("joined memory-block restore: " + error);
            std::cout << "PASS joined memory-block restore\n";
            if (!physx333_offline::RestoreInteractionContactPayload(
                    *source->scene, checkpointInteraction, error))
                die("joined contact-manager payload restore: " + error);
            std::cout << "PASS joined contact-manager payload restore\n";
            for (unsigned iteration = 0; iteration < 100; ++iteration)
            {
                if (!physx333_offline::InstallInteractionContactBindings(
                        *source->scene, checkpointInteraction, error))
                    die("repeat contact binding installation: " + error);
                if (!physx333_offline::RestoreMemBlockPoolForJoin(
                        *source->scene, memBlockIds, checkpointMemBlocks,
                        error))
                    die("repeat memory-block restore: " + error);
                if (!physx333_offline::RestoreInteractionContactPayload(
                        *source->scene, checkpointInteraction, error))
                    die("repeat contact payload restore: " + error);
            }
            std::cout << "PASS joined contact/allocator idempotence x100\n";
            if (!oc2::offline::RestoreIslandForJoin(*source->scene,
                                             checkpointIsland, error))
                die("joined island restore: " + error);
            std::cout << "PASS joined island restore\n";
            if (!oc2::offline::RestoreSap(*source->scene, checkpointSap, error))
                die("joined SAP restore: " + error);
            std::cout << "PASS joined SAP restore\n";
            if (!oc2::offline::RestoreTransformCache(*source->scene,
                                                      checkpointCache, error))
                die("joined transform-cache restore: " + error);
            std::cout << "PASS joined transform-cache restore\n";
            oc2::offline::ShapeCacheBindings recreatedShapeCache;
            if (!oc2::offline::CaptureShapeCacheBindings(*source->scene,
                    recreatedShapeCache, error))
                die("joined recreated shape-cache binding capture: " + error);
            std::string shapeCacheDifference;
            std::cout << "SHAPE_CACHE_BINDINGS before_restore_equal=" <<
                checkpointShapeCache.equals(recreatedShapeCache,
                                            shapeCacheDifference) << "\n";
            oc2::offline::ShapeCacheBindings corruptShapeCache =
                checkpointShapeCache;
            corruptShapeCache.bindings[0].shapeId ^= 1u;
            if (oc2::offline::RestoreShapeCacheBindings(*source->scene,
                    corruptShapeCache, error))
                die("corrupt shape-cache binding image was accepted");
            oc2::offline::ShapeCacheBindings afterShapeReject;
            if (!oc2::offline::CaptureShapeCacheBindings(*source->scene,
                    afterShapeReject, error) ||
                !recreatedShapeCache.equals(afterShapeReject, error))
                die("shape-cache binding rejection changed the scene: " + error);
            std::cout << "PASS corrupt shape-cache binding rejected atomically\n";
            if (!oc2::offline::RestoreShapeCacheBindings(*source->scene,
                    checkpointShapeCache, error))
                die("joined shape-cache binding restore: " + error);
            if (!oc2::offline::CaptureShapeCacheBindings(*source->scene,
                    recreatedShapeCache, error) ||
                !checkpointShapeCache.equals(recreatedShapeCache, error))
                die("joined shape-cache binding verification: " + error);
            std::cout << "PASS joined shape-cache binding restore\n";
            if (!oc2::offline::RestoreBodies(*source->scene,
                                              checkpointBody, error))
                die("joined body restore: " + error);
            std::cout << "PASS joined body restore\n";
            if (!oc2::offline::RestoreSceneClock(*source->scene,
                                                  checkpointClock, error))
                die("joined scene-clock restore: " + error);
            std::cout << "PASS joined scene-clock restore\n";
            if (!oc2::offline::RestoreContextImage(*source->scene,
                                                    checkpointContext, error))
                die("joined context restore: " + error);
            std::cout << "PASS joined low-level context restore\n";
            if (joinedReplayProbe &&
                !oc2::offline::RestoreQueryImage(*source->scene,
                                                 checkpointQuery, error))
                die("joined query restore: " + error);
            if (joinedReplayProbe)
                std::cout << "PASS joined scene-query restore\n";
        }
        const physx333_offline::OracleImage recreatedOracle = captureOracle(*source);
        const bool oracleExact = checkpointOracle.equals(recreatedOracle, error);
        if (joinedPayloadProbe)
        {
            if (!oracleExact)
                die("joined source oracle mismatch: " + error);
            std::cout << "PASS joined 39-section source oracle image\n";
            oc2::offline::ContextImage recreatedContext;
            oc2::offline::QueryImage recreatedQuery;
            if (!oc2::offline::CaptureContextImage(*source->scene,
                    recreatedContext, error) ||
                (joinedReplayProbe &&
                 !oc2::offline::CaptureQueryImage(*source->scene,
                     recreatedQuery, error)))
                die("joined reconstructed component capture: " + error);
            if (!checkpointContext.equals(recreatedContext, error))
                die("joined reconstructed context image: " + error);
            if (joinedReplayProbe &&
                !checkpointQuery.equalsWithRebasedStack(recreatedQuery,
                                                        error))
                die("joined reconstructed query image: " + error);
            std::cout << "PASS joined checkpoint context"
                      << (joinedReplayProbe ? "/query" : "") << " images\n";
        }
        else
        {
            if (oracleExact)
                die("NPhase lifecycle-only reconstruction unexpectedly matched the complete oracle");
            std::cout << "NPHASE_REMAINING first_difference=" << error << "\n";
        }
        std::cout << "NPHASE_SLOTS checkpoint_cm="
                  << sixSlotOrder(checkpointOracle, "contact.managers", 0)
                  << " recreated_cm="
                  << sixSlotOrder(recreatedOracle, "contact.managers", 0)
                  << " checkpoint_sip="
                  << sixSlotOrder(checkpointOracle, "nphase.shape_pairs", 1)
                  << " recreated_sip="
                  << sixSlotOrder(recreatedOracle, "nphase.shape_pairs", 1)
                  << "\n";
        std::cout << "NPHASE_BY_SHAPE checkpoint_cm="
                  << sixSlotsByMoverShape(checkpointOracle, "contact.managers", 0)
                  << " recreated_cm="
                  << sixSlotsByMoverShape(recreatedOracle, "contact.managers", 0)
                  << " checkpoint_sip="
                  << sixSlotsByMoverShape(checkpointOracle, "nphase.shape_pairs", 1)
                  << " recreated_sip="
                  << sixSlotsByMoverShape(recreatedOracle, "nphase.shape_pairs", 1)
                  << "\n";
        if (joinedReplayProbe)
        {
            source->step(true);
            const Snapshot replayedDeletion = source->capture();
            std::string replayDifference;
            if (!compare(expectedDeletion, replayedDeletion,
                         replayDifference))
                die("joined next-step public/callback parity: " + replayDifference);
            std::cout << "PASS joined next-step public/callback parity\n";
            const physx333_offline::OracleImage replayedOracle =
                captureOracle(*source);
            if (!expectedDeletionOracle.equals(replayedOracle,
                                               replayDifference))
                die("joined next-step oracle parity: " + replayDifference);
            std::cout << "PASS joined next-step 39-section oracle parity\n";
            oc2::offline::SapImage replayedSap;
            oc2::offline::TransformCacheImage replayedCache;
            oc2::offline::ShapeCacheBindings replayedShapeCache;
            oc2::offline::BodyImage replayedBody;
            oc2::offline::SceneClockImage replayedClock;
            oc2::offline::ContextImage replayedContext;
            oc2::offline::QueryImage replayedQuery;
            oc2::offline::IslandImage replayedIsland;
            physx333_offline::MemBlockRestoreImage replayedBlocks;
            physx333_offline::MemBlockIdentityRegistry replayIds = memBlockIds;
            if (!oc2::offline::CaptureSap(*source->scene, replayedSap,
                                          replayDifference) ||
                !oc2::offline::CaptureTransformCache(*source->scene,
                    replayedCache, replayDifference) ||
                !oc2::offline::CaptureShapeCacheBindings(*source->scene,
                    replayedShapeCache, replayDifference) ||
                !oc2::offline::CaptureBodies(*source->scene,
                    replayedBody, replayDifference) ||
                !oc2::offline::CaptureSceneClock(*source->scene,
                    replayedClock, replayDifference) ||
                !oc2::offline::CaptureContextImage(*source->scene,
                    replayedContext, replayDifference) ||
                !oc2::offline::CaptureQueryImage(*source->scene,
                    replayedQuery, replayDifference) ||
                !oc2::offline::CaptureIsland(*source->scene,
                    replayedIsland, replayDifference) ||
                !physx333_offline::CaptureMemBlockRestore(*source->scene,
                    replayIds, replayedBlocks, replayDifference))
                die("joined next-step component capture: " + replayDifference);
            bool extendedParity = true;
            if (!expectedDeletionSap.equals(replayedSap, replayDifference))
            {
                std::cout << "EXTENDED_DIFFERENCE SAP=" << replayDifference << "\n";
                extendedParity = false;
            }
            if (!expectedDeletionCache.equals(replayedCache, replayDifference))
            {
                const auto& savedIds = expectedDeletionCache.freeIds;
                const auto& actualIds = replayedCache.freeIds;
                size_t firstByte = 0;
                while (firstByte < savedIds.bytes.size() &&
                       firstByte < actualIds.bytes.size() &&
                       savedIds.bytes[firstByte] == actualIds.bytes[firstByte])
                    ++firstByte;
                std::cout << "CACHE_FREE_IDS expected_size=" << savedIds.size
                          << " actual_size=" << actualIds.size
                          << " capacity=" << savedIds.capacity << "/"
                          << actualIds.capacity << " first_byte=" << firstByte;
                if (firstByte < savedIds.bytes.size() &&
                    firstByte < actualIds.bytes.size())
                    std::cout << " expected=" << unsigned(savedIds.bytes[firstByte])
                              << " actual=" << unsigned(actualIds.bytes[firstByte]);
                std::cout << "\n";
                for (size_t id = 0; id < savedIds.size; ++id)
                {
                    PxU32 wanted = 0;
                    PxU32 got = 0;
                    std::memcpy(&wanted, &savedIds.bytes[id * sizeof(PxU32)],
                                sizeof(PxU32));
                    std::memcpy(&got, &actualIds.bytes[id * sizeof(PxU32)],
                                sizeof(PxU32));
                    std::cout << "CACHE_FREE_ID " << id << " expected="
                              << wanted << " actual=" << got << "\n";
                }
                std::cout << "EXTENDED_DIFFERENCE transform_cache="
                          << replayDifference << "\n";
                extendedParity = false;
            }
            if (!expectedDeletionShapeCache.equals(replayedShapeCache,
                                                   replayDifference))
            {
                std::cout << "EXTENDED_DIFFERENCE shape_cache="
                          << replayDifference << "\n";
                extendedParity = false;
            }
            if (!expectedDeletionBody.equals(replayedBody, replayDifference))
            {
                std::cout << "EXTENDED_DIFFERENCE body=" << replayDifference << "\n";
                extendedParity = false;
            }
            if (!expectedDeletionClock.equals(replayedClock, replayDifference))
            {
                std::cout << "EXTENDED_DIFFERENCE clock=" << replayDifference << "\n";
                extendedParity = false;
            }
            if (!expectedDeletionContext.equals(replayedContext,
                                                replayDifference))
            {
                std::cout << "EXTENDED_DIFFERENCE context=" << replayDifference
                          << "\n";
                for (size_t field = 0; field < expectedDeletionContext.fields.size() &&
                     field < replayedContext.fields.size(); ++field)
                {
                    const auto& wanted = expectedDeletionContext.fields[field];
                    const auto& got = replayedContext.fields[field];
                    if (wanted.name != "context.simStats" ||
                        got.name != wanted.name) continue;
                    for (size_t byte = 0; byte + sizeof(PxU32) <=
                         wanted.bytes.size() && byte + sizeof(PxU32) <=
                         got.bytes.size(); byte += sizeof(PxU32))
                    {
                        PxU32 a = 0, b = 0;
                        std::memcpy(&a, &wanted.bytes[byte], sizeof(a));
                        std::memcpy(&b, &got.bytes[byte], sizeof(b));
                        if (a != b)
                            std::cout << "CONTEXT_STATS word=" <<
                                byte / sizeof(PxU32) << " expected=" << a
                                      << " actual=" << b << "\n";
                    }
                }
                extendedParity = false;
            }
            if (!expectedDeletionQuery.equalsWithRebasedStack(
                    replayedQuery, replayDifference))
            {
                std::cout << "EXTENDED_DIFFERENCE query=" << replayDifference
                          << "\n";
                extendedParity = false;
            }
            if (!expectedDeletionIsland.equals(replayedIsland, replayDifference))
            {
                std::cout << "EXTENDED_DIFFERENCE island=" << replayDifference << "\n";
                extendedParity = false;
            }
            if (!expectedDeletionBlocks.pool.equals(replayedBlocks.pool,
                                                     replayDifference) ||
                expectedDeletionBlocks.contactBindings !=
                    replayedBlocks.contactBindings)
            {
                std::cout << "EXTENDED_DIFFERENCE memblock=" << replayDifference
                          << "\n";
                extendedParity = false;
            }
            if (!extendedParity)
                die("joined next-step extended image parity differs");
            std::cout << "PASS joined next-step SAP/cache/body/clock/context/query/island/block images\n";
            for (unsigned iteration = 1; iteration < 100; ++iteration)
            {
                if (!physx333_offline::RestoreInteractionOrder(
                        *source->scene, checkpointInteraction, error) ||
                    !physx333_offline::RestoreInteractionMetadata(
                        *source->scene, checkpointInteraction, error) ||
                    !physx333_offline::InstallInteractionContactBindings(
                        *source->scene, checkpointInteraction, error) ||
                    !physx333_offline::RestoreMemBlockPoolForJoin(
                        *source->scene, memBlockIds, checkpointMemBlocks,
                        error) ||
                    !physx333_offline::RestoreInteractionContactPayload(
                        *source->scene, checkpointInteraction, error) ||
                    !oc2::offline::RestoreIslandForJoin(
                        *source->scene, checkpointIsland, error) ||
                    !oc2::offline::RestoreSap(*source->scene,
                                              checkpointSap, error) ||
                    !oc2::offline::RestoreTransformCache(*source->scene,
                                                          checkpointCache, error) ||
                    !oc2::offline::RestoreShapeCacheBindings(*source->scene,
                        checkpointShapeCache, error) ||
                    !oc2::offline::RestoreBodies(*source->scene,
                                                  checkpointBody, error) ||
                    !oc2::offline::RestoreSceneClock(*source->scene,
                                                      checkpointClock, error) ||
                    !oc2::offline::RestoreContextImage(*source->scene,
                                                        checkpointContext, error) ||
                    !oc2::offline::RestoreQueryImage(*source->scene,
                                                      checkpointQuery, error))
                    die("joined repeat rewind " + std::to_string(iteration) +
                        ": " + error);
                const physx333_offline::OracleImage repeatedCheckpoint =
                    captureOracle(*source);
                if (!checkpointOracle.equals(repeatedCheckpoint,
                                             replayDifference))
                    die("joined repeat checkpoint " +
                        std::to_string(iteration) + ": " + replayDifference);
                oc2::offline::ContextImage repeatedContext;
                oc2::offline::QueryImage repeatedQuery;
                if (!oc2::offline::CaptureContextImage(*source->scene,
                        repeatedContext, replayDifference) ||
                    !oc2::offline::CaptureQueryImage(*source->scene,
                        repeatedQuery, replayDifference))
                    die("joined repeat component capture " +
                        std::to_string(iteration) + ": " + replayDifference);
                if (!checkpointContext.equals(repeatedContext,
                                               replayDifference) ||
                    !checkpointQuery.equalsWithRebasedStack(
                        repeatedQuery, replayDifference))
                    die("joined repeat context/query checkpoint " +
                        std::to_string(iteration) + ": " + replayDifference);
                source->step(true);
                const Snapshot repeatedDeletion = source->capture();
                if (!compare(expectedDeletion, repeatedDeletion,
                             replayDifference))
                    die("joined repeat public/callback " +
                        std::to_string(iteration) + ": " + replayDifference);
                const physx333_offline::OracleImage repeatedOracle =
                    captureOracle(*source);
                if (!expectedDeletionOracle.equals(repeatedOracle,
                                                   replayDifference))
                    die("joined repeat oracle " +
                        std::to_string(iteration) + ": " + replayDifference);
                oc2::offline::SapImage repeatedSap;
                oc2::offline::TransformCacheImage repeatedCache;
                oc2::offline::ShapeCacheBindings repeatedShapeCache;
                oc2::offline::BodyImage repeatedBody;
                oc2::offline::SceneClockImage repeatedClock;
                oc2::offline::ContextImage repeatedNextContext;
                oc2::offline::QueryImage repeatedNextQuery;
                oc2::offline::IslandImage repeatedIsland;
                physx333_offline::MemBlockRestoreImage repeatedBlocks;
                physx333_offline::MemBlockIdentityRegistry repeatedIds =
                    memBlockIds;
                if (!oc2::offline::CaptureSap(*source->scene, repeatedSap,
                        replayDifference) ||
                    !oc2::offline::CaptureTransformCache(*source->scene,
                        repeatedCache, replayDifference) ||
                    !oc2::offline::CaptureShapeCacheBindings(*source->scene,
                        repeatedShapeCache, replayDifference) ||
                    !oc2::offline::CaptureBodies(*source->scene,
                        repeatedBody, replayDifference) ||
                    !oc2::offline::CaptureSceneClock(*source->scene,
                        repeatedClock, replayDifference) ||
                    !oc2::offline::CaptureContextImage(*source->scene,
                        repeatedNextContext, replayDifference) ||
                    !oc2::offline::CaptureQueryImage(*source->scene,
                        repeatedNextQuery, replayDifference) ||
                    !oc2::offline::CaptureIsland(*source->scene,
                        repeatedIsland, replayDifference) ||
                    !physx333_offline::CaptureMemBlockRestore(*source->scene,
                        repeatedIds, repeatedBlocks, replayDifference))
                    die("joined repeat extended capture " +
                        std::to_string(iteration) + ": " + replayDifference);
                if (!expectedDeletionSap.equals(repeatedSap,
                        replayDifference) ||
                    !expectedDeletionCache.equals(repeatedCache,
                        replayDifference) ||
                    !expectedDeletionShapeCache.equals(repeatedShapeCache,
                        replayDifference) ||
                    !expectedDeletionBody.equals(repeatedBody,
                        replayDifference) ||
                    !expectedDeletionClock.equals(repeatedClock,
                        replayDifference) ||
                    !expectedDeletionContext.equals(repeatedNextContext,
                        replayDifference) ||
                    !expectedDeletionQuery.equalsWithRebasedStack(
                        repeatedNextQuery, replayDifference) ||
                    !expectedDeletionIsland.equals(repeatedIsland,
                        replayDifference) ||
                    !expectedDeletionBlocks.pool.equals(repeatedBlocks.pool,
                        replayDifference) ||
                    expectedDeletionBlocks.contactBindings !=
                        repeatedBlocks.contactBindings)
                    die("joined repeat extended image " +
                        std::to_string(iteration) + ": " + replayDifference);
            }
            std::cout << "PASS joined rewind and full next-step image parity x100\n";
            World reference(runtime);
            reference.step(false);
            reference.step(true);
            reference.step(false);
            reference.step(true);
            const bool suffixAway[] = {false, false, true, false, true};
            for (unsigned suffix = 0; suffix <
                   sizeof(suffixAway) / sizeof(suffixAway[0]); ++suffix)
            {
                source->step(suffixAway[suffix]);
                reference.step(suffixAway[suffix]);
                if (!compare(reference.capture(), source->capture(),
                             replayDifference))
                    die("joined suffix public/callback " +
                        std::to_string(suffix) + ": " + replayDifference);
                const physx333_offline::OracleImage expectedSuffix =
                    captureOracle(reference);
                const physx333_offline::OracleImage replayedSuffix =
                    captureOracle(*source);
                if (!expectedSuffix.equals(replayedSuffix,
                                           replayDifference))
                    die("joined suffix oracle " +
                        std::to_string(suffix) + ": " + replayDifference);
            }
            std::cout << "PASS joined five-step contact suffix parity\n";
        }
        std::cout.flush();
        std::_Exit(0);
    }

    Runtime runtime;
    Snapshot checkpoint, deletion, settled;
    physx333_offline::OracleImage checkpointOracle, deletionOracle, settledOracle;
    {
        World source(runtime);
        // Exercise both overlap output arrays before checkpoint. Their first
        // growth is allocator history that this same-allocation restore does
        // not attempt to reverse.
        source.step(false);
        source.step(true);
        source.step(false);
        std::cout << "PASS six-contact warmup\n";
        checkpoint = source.capture();
        if (source.events.rows.size() != 4u * kPairCount)
            die("checkpoint did not report six contact events");
        for (PxU32 i = 0; i != kPairCount; ++i)
            if (!(source.events.rows[4u * i + 2u] & PxPairFlag::eNOTIFY_TOUCH_FOUND))
                die("checkpoint contact event is not touch-found");
        checkpointOracle = captureOracle(source);
        requireOracleEqual("NPhase/island checkpoint double capture",
                           checkpointOracle, captureOracle(source));
        const PublicBodyState publicCheckpoint = source.saveBody();
        oc2::offline::SapImage sapCheckpoint;
        oc2::offline::SapImage sapCheckpointCopy;
        std::string sapError;
        if (!oc2::offline::CaptureSap(*source.scene, sapCheckpoint, sapError) ||
            !oc2::offline::CaptureSap(*source.scene, sapCheckpointCopy, sapError))
            die("SAP checkpoint capture: " + sapError);
        std::string sapDifference;
        if (!sapCheckpoint.equals(sapCheckpointCopy, sapDifference))
            die("SAP checkpoint capture is not stable: " + sapDifference);
        std::cout << "PASS SAP checkpoint double capture\n";
        oc2::offline::TransformCacheImage cacheCheckpoint;
        oc2::offline::TransformCacheImage cacheCheckpointCopy;
        std::string cacheError;
        std::string cacheDifference;
        if (!oc2::offline::CaptureTransformCache(*source.scene, cacheCheckpoint, cacheError) ||
            !oc2::offline::CaptureTransformCache(*source.scene, cacheCheckpointCopy, cacheError))
            die("transform-cache checkpoint capture: " + cacheError);
        if (!cacheCheckpoint.equals(cacheCheckpointCopy, cacheDifference))
            die("transform-cache checkpoint capture is not stable: " + cacheDifference);
        std::cout << "PASS transform-cache checkpoint double capture\n";
        oc2::offline::IslandImage islandCheckpoint;
        oc2::offline::IslandImage islandCheckpointCopy;
        std::string islandError;
        std::string islandDifference;
        if (!oc2::offline::CaptureIsland(*source.scene, islandCheckpoint, islandError) ||
            !oc2::offline::CaptureIsland(*source.scene, islandCheckpointCopy, islandError))
            die("island checkpoint capture: " + islandError);
        if (!islandCheckpoint.equals(islandCheckpointCopy, islandDifference))
            die("island checkpoint capture is not stable: " + islandDifference);
        std::cout << "PASS island checkpoint double capture\n";
        physx333_offline::MemBlockIdentityRegistry memBlockIds;
        physx333_offline::MemBlockImage memBlockCheckpoint;
        physx333_offline::MemBlockImage memBlockCheckpointCopy;
        std::string memBlockError;
        std::string memBlockDifference;
        if (!physx333_offline::CaptureMemBlockPool(*source.scene, memBlockIds,
                                                    memBlockCheckpoint, memBlockError) ||
            !physx333_offline::CaptureMemBlockPool(*source.scene, memBlockIds,
                                                    memBlockCheckpointCopy, memBlockError))
            die("memblock checkpoint capture: " + memBlockError);
        if (!memBlockCheckpoint.equals(memBlockCheckpointCopy, memBlockDifference))
            die("memblock checkpoint capture is not stable: " + memBlockDifference);
        std::cout << "PASS memblock pool checkpoint double capture"
                  << " (unsupported=" << memBlockCheckpoint.unsupported.size()
                  << ")\n";
        for (PxU32 iteration = 0; iteration != 100; ++iteration)
            if (!oc2::offline::RestoreIsland(*source.scene,
                                               islandCheckpoint, islandError))
                die("island idempotent restore: " + islandError);
        std::cout << "PASS island same-topology idempotent restore x100\n";
        oc2::offline::IslandImage corruptIsland = islandCheckpoint;
        corruptIsland.scene = 0;
        if (oc2::offline::RestoreIsland(*source.scene,
                                         corruptIsland, islandError))
            die("corrupt island image was accepted");
        oc2::offline::IslandImage islandAfterReject;
        if (!oc2::offline::CaptureIsland(*source.scene,
                                          islandAfterReject, islandError) ||
            !islandCheckpoint.equals(islandAfterReject, islandDifference))
            die("rejected island image changed live state: " +
                (islandError.empty() ? islandDifference : islandError));
        std::cout << "PASS corrupt island image rejected atomically\n";

        source.step(true);
        deletion = source.capture();
        if (source.events.rows.size() != 4u * kPairCount)
            die("deletion did not report six contact events");
        for (PxU32 i = 0; i != kPairCount; ++i)
            if (!(source.events.rows[4u * i + 2u] & PxPairFlag::eNOTIFY_TOUCH_LOST))
                die("deletion contact event is not touch-lost");
        deletionOracle = captureOracle(source);
        oc2::offline::SapImage sapDeleted;
        if (!oc2::offline::CaptureSap(*source.scene, sapDeleted, sapError))
            die("SAP deletion capture: " + sapError);
        oc2::offline::TransformCacheImage cacheDeleted;
        if (!oc2::offline::CaptureTransformCache(*source.scene, cacheDeleted, cacheError))
            die("transform-cache deletion capture: " + cacheError);
        oc2::offline::IslandImage islandDeleted;
        if (!oc2::offline::CaptureIsland(*source.scene,
                                          islandDeleted, islandError))
            die("island deletion capture: " + islandError);
        physx333_offline::MemBlockImage memBlockDeleted;
        physx333_offline::MemBlockImage memBlockDeletedCopy;
        if (!physx333_offline::CaptureMemBlockPool(*source.scene, memBlockIds,
                                                    memBlockDeleted, memBlockError) ||
            !physx333_offline::CaptureMemBlockPool(*source.scene, memBlockIds,
                                                    memBlockDeletedCopy, memBlockError))
            die("memblock deletion capture: " + memBlockError);
        if (!memBlockDeleted.equals(memBlockDeletedCopy, memBlockDifference))
            die("memblock deletion capture is not stable: " + memBlockDifference);
        std::cout << "PASS memblock pool deletion double capture\n";
        if (oc2::offline::RestoreIsland(*source.scene,
                                         islandCheckpoint, islandError))
            die("island restore accepted missing contact-edge bindings");
        if (!oc2::offline::CaptureIsland(*source.scene,
                                          islandAfterReject, islandError) ||
            !islandDeleted.equals(islandAfterReject, islandDifference))
            die("island topology rejection changed live state: " +
                (islandError.empty() ? islandDifference : islandError));
        std::cout << "PASS missing island contact edges rejected atomically\n";

        for (PxU32 iteration = 0; iteration != 100; ++iteration)
        {
            if (!oc2::offline::RestoreSap(*source.scene, sapCheckpoint, sapError))
                die("SAP checkpoint restore: " + sapError);
            oc2::offline::SapImage restored;
            if (!oc2::offline::CaptureSap(*source.scene, restored, sapError))
                die("SAP recapture after checkpoint restore: " + sapError);
            if (!sapCheckpoint.equals(restored, sapDifference))
                die("SAP checkpoint round-trip: " + sapDifference);

            // Contacts and islands still describe the deletion state. Return
            // SAP to that same state before any subsequent simulate/release.
            if (!oc2::offline::RestoreSap(*source.scene, sapDeleted, sapError))
                die("SAP deletion restore: " + sapError);
            if (!oc2::offline::CaptureSap(*source.scene, restored, sapError))
                die("SAP recapture after deletion restore: " + sapError);
            if (!sapDeleted.equals(restored, sapDifference))
                die("SAP deletion round-trip: " + sapDifference);
        }
        std::cout << "PASS SAP/BPElem A-B round-trip x100\n";

        oc2::offline::SapImage corrupted = sapCheckpoint;
        bool changed = false;
        for (size_t i = 0; i != corrupted.scalars.size(); ++i)
        {
            if (corrupted.scalars[i].name == "pair.activeCount")
            {
                std::fill(corrupted.scalars[i].bytes.begin(),
                          corrupted.scalars[i].bytes.end(),
                          static_cast<unsigned char>(0xffu));
                changed = true;
                break;
            }
        }
        if (!changed) die("SAP image lacks pair.activeCount scalar");
        if (oc2::offline::RestoreSap(*source.scene, corrupted, sapError))
            die("corrupt SAP image was accepted");
        oc2::offline::SapImage afterReject;
        if (!oc2::offline::CaptureSap(*source.scene, afterReject, sapError))
            die("SAP recapture after rejected image: " + sapError);
        if (!sapDeleted.equals(afterReject, sapDifference))
            die("rejected SAP image changed live state: " + sapDifference);
        std::cout << "PASS corrupt SAP image rejected atomically\n";

        for (PxU32 iteration = 0; iteration != 100; ++iteration)
        {
            if (!oc2::offline::RestoreTransformCache(*source.scene,
                                                      cacheCheckpoint, cacheError))
                die("transform-cache checkpoint restore: " + cacheError);
            oc2::offline::TransformCacheImage restored;
            if (!oc2::offline::CaptureTransformCache(*source.scene, restored, cacheError) ||
                !cacheCheckpoint.equals(restored, cacheDifference))
                die("transform-cache checkpoint round-trip: " +
                    (cacheError.empty() ? cacheDifference : cacheError));
            // Other PhysX subsystems still own the deleted-state topology.
            if (!oc2::offline::RestoreTransformCache(*source.scene,
                                                      cacheDeleted, cacheError))
                die("transform-cache deletion restore: " + cacheError);
            if (!oc2::offline::CaptureTransformCache(*source.scene, restored, cacheError) ||
                !cacheDeleted.equals(restored, cacheDifference))
                die("transform-cache deletion round-trip: " +
                    (cacheError.empty() ? cacheDifference : cacheError));
        }
        std::cout << "PASS transform-cache/ID-pool A-B round-trip x100\n";
        oc2::offline::TransformCacheImage corruptCache = cacheCheckpoint;
        corruptCache.currentId = 0xffffffffu;
        if (oc2::offline::RestoreTransformCache(*source.scene,
                                                 corruptCache, cacheError))
            die("corrupt transform-cache image was accepted");
        oc2::offline::TransformCacheImage cacheAfterReject;
        if (!oc2::offline::CaptureTransformCache(*source.scene,
                                                  cacheAfterReject, cacheError) ||
            !cacheDeleted.equals(cacheAfterReject, cacheDifference))
            die("rejected transform-cache image changed live state: " +
                (cacheError.empty() ? cacheDifference : cacheError));
        std::cout << "PASS corrupt transform-cache image rejected atomically\n";

        source.step(false);
        settled = source.capture();
        settledOracle = captureOracle(source);

        const PxU32 checkpointPairs = checkpoint.parts["sap.meta"][6];
        const PxU32 deletedPairs = deletion.parts["sap.meta"][6];
        if (checkpointPairs != kPairCount || deletedPairs != 0)
        {
            std::ostringstream out;
            out << "fixture expected six SAP pairs then zero; observed "
                << checkpointPairs << " then " << deletedPairs;
            die(out.str());
        }
        std::cout << "PASS six-pair predecessor and deletion fixture\n";

        if (publicProbe)
        {
            // The scripted settled suffix re-entered contact. Return to the
            // empty-contact state before trying public-only restoration.
            source.step(true);
            source.restorePublicBody(publicCheckpoint);
            source.step(true);
            const Snapshot naive = source.capture();
            const physx333_offline::OracleImage naiveOracle = captureOracle(source);
            std::string difference;
            if (compare(deletion, naive, difference))
                die("public-only rewind unexpectedly matched; fixture needs stronger history pressure");
            std::cout << "PUBLIC_REWIND_INSUFFICIENT first_difference=" << difference << "\n";
            if (deletionOracle.equals(naiveOracle, difference))
                die("public-only rewind unexpectedly matched NPhase/island oracle");
            std::cout << "PUBLIC_ORACLE_INSUFFICIENT first_difference=" << difference << "\n";
        }
    }

    {
        World replay(runtime);
        replay.step(false);
        replay.step(true);
        replay.step(false);
        requireEqual("fresh replay checkpoint", checkpoint, replay.capture());
        requireOracleEqual("fresh replay NPhase/island checkpoint",
                           checkpointOracle, captureOracle(replay));
        replay.step(true);
        requireEqual("fresh replay six deletions", deletion, replay.capture());
        requireOracleEqual("fresh replay NPhase/island six deletions",
                           deletionOracle, captureOracle(replay));
        replay.step(false);
        requireEqual("fresh replay settled suffix", settled, replay.capture());
        requireOracleEqual("fresh replay NPhase/island settled suffix",
                           settledOracle, captureOracle(replay));
    }

    if (runtime.errors.errors)
        die("PhysX emitted at least one error during the fixture");
    std::cout << "PASS offline PhysX 3.3.3 source-backed oracle ("
              << checkpointOracle.summary() << ")\n";
    return 0;
}
