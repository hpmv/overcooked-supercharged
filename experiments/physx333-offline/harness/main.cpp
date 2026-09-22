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

} // namespace

int main(int argc, char** argv)
{
    bool publicProbe = false;
    for (int i = 1; i < argc; ++i)
    {
        if (std::string(argv[i]) == "--public-rewind-probe") publicProbe = true;
        else die(std::string("unknown argument: ") + argv[i]);
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
