#include "SceneClockImage.h"

#include <algorithm>
#include <cstring>
#include <set>
#include <utility>

#include "PxPhysicsAPI.h"

// Access is confined to this source-built offline test object. No vendor
// source, shipped game binary, or SDK runtime layout is modified.
#define private public
#define protected public
#include "CmIDPool.h"
#include "ScObjectIDTracker.h"
#include "NpScene.h"
#include "NpRigidDynamic.h"
#include "ScSimStats.h"
#undef protected
#undef private

namespace oc2 { namespace offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "Scene clock image requires Win32 PhysX 3.3.3");

template <typename T>
void addField(SceneClockImage& image, const char* name, T& value,
              bool invariant = false)
{
    SceneClockImage::Field field;
    field.name = name;
    field.address = reinterpret_cast<std::uintptr_t>(&value);
    field.bytes.resize(sizeof(value));
    std::memcpy(field.bytes.data(), &value, sizeof(value));
    field.invariant = invariant;
    image.fields.push_back(std::move(field));
}

template <typename T>
void addPointerArray(SceneClockImage& image, const char* name,
                     Ps::Array<T*>& array, bool invariant = false)
{
    SceneClockImage::PointerArray row;
    row.name = name;
    row.object = reinterpret_cast<std::uintptr_t>(&array);
    row.data = reinterpret_cast<std::uintptr_t>(array.begin());
    row.capacity = array.capacity();
    row.invariant = invariant;
    for (PxU32 i = 0; i < array.size(); ++i)
        row.values.push_back(reinterpret_cast<std::uintptr_t>(array[i]));
    image.arrays.push_back(std::move(row));
}

bool zeroBitmap(const Cm::BitMap& map)
{
    for (PxU32 i = 0; i < map.getWordCount(); ++i)
        if (map.getWords()[i] != 0) return false;
    return true;
}

bool unsupportedDeformables(const Sc::Scene& sc)
{
#if PX_USE_PARTICLE_SYSTEM_API
    if (sc.mParticleSystems.size() || sc.mEnabledParticleSystems.size())
        return true;
#endif
#if PX_USE_CLOTH_API
    if (sc.mCloths.size()) return true;
#endif
    return false;
}

bool captureTracker(Sc::ObjectIDTracker& tracker, const char* name,
                    SceneClockImage::IDTracker& out, std::string& error)
{
    if (tracker.mPendingReleasedIDs.size() != 0 ||
        !zeroBitmap(tracker.mDeletedIDsMap))
    {
        error = std::string(name) + " has deferred releases at checkpoint";
        return false;
    }
    out.name = name;
    out.object = reinterpret_cast<std::uintptr_t>(&tracker);
    out.currentId = tracker.mIDPool.mCurrentID;
    Ps::Array<PxU32>& free = tracker.mIDPool.mFreeIDs;
    out.freeData = reinterpret_cast<std::uintptr_t>(free.begin());
    out.freeCapacity = free.capacity();
    for (PxU32 i = 0; i < free.size(); ++i)
        out.freeIds.push_back(free[i]);
    out.pendingData = reinterpret_cast<std::uintptr_t>(
        tracker.mPendingReleasedIDs.begin());
    out.pendingCapacity = tracker.mPendingReleasedIDs.capacity();
    out.bitmapData = reinterpret_cast<std::uintptr_t>(
        tracker.mDeletedIDsMap.getWords());
    out.bitmapWords = tracker.mDeletedIDsMap.getWordCount();
    std::set<PxU32> unique;
    for (PxU32 id : out.freeIds)
    {
        if (id >= out.currentId || !unique.insert(id).second)
        {
            error = std::string(name) + " has an invalid free-ID stack";
            return false;
        }
    }
    return true;
}

bool validTrackerImage(const SceneClockImage::IDTracker& row,
                       const SceneClockImage::IDTracker& live,
                       std::string& error)
{
    if (row.name != live.name || row.object != live.object ||
        row.freeData != live.freeData ||
        row.freeCapacity != live.freeCapacity ||
        row.pendingData != live.pendingData ||
        row.pendingCapacity != live.pendingCapacity ||
        row.bitmapData != live.bitmapData ||
        row.bitmapWords != live.bitmapWords ||
        row.freeIds.size() > row.freeCapacity)
    {
        error = row.name + " identity or allocation changed";
        return false;
    }
    std::set<std::uint32_t> unique;
    for (std::uint32_t id : row.freeIds)
    {
        if (id >= row.currentId || !unique.insert(id).second)
        {
            error = row.name + " image has duplicate or out-of-range free ID";
            return false;
        }
    }
    return true;
}

bool preflight(const SceneClockImage& target, const SceneClockImage& live,
                std::string& error)
{
    if (target.npScene != live.npScene ||
        target.scScene != live.scScene ||
        target.fields.size() != live.fields.size() ||
        target.arrays.size() != live.arrays.size())
    {
        error = "scene identity or image schema changed";
        return false;
    }
    for (size_t i = 0; i < target.fields.size(); ++i)
    {
        const SceneClockImage::Field& a = target.fields[i];
        const SceneClockImage::Field& b = live.fields[i];
        if (a.name != b.name || a.address != b.address ||
            a.bytes.size() != b.bytes.size() || a.invariant != b.invariant ||
            (a.invariant && a.bytes != b.bytes))
        {
            error = a.name + " layout or invariant changed";
            return false;
        }
    }
    for (size_t i = 0; i < target.arrays.size(); ++i)
    {
        const SceneClockImage::PointerArray& a = target.arrays[i];
        const SceneClockImage::PointerArray& b = live.arrays[i];
        if (a.name != b.name || a.object != b.object ||
            a.data != b.data || a.capacity != b.capacity ||
            a.values.size() > a.capacity || a.invariant != b.invariant ||
            (a.invariant && a.values != b.values))
        {
            error = a.name + " identity, allocation, or ordering changed";
            return false;
        }
    }
    return validTrackerImage(target.shapeIds, live.shapeIds, error) &&
           validTrackerImage(target.rigidIds, live.rigidIds, error);
}

bool validBodyListPointers(PxScene& scene, const SceneClockImage& target,
                           std::string& error)
{
    if (target.arrays.size() < 2 ||
        target.arrays[0].name != "Sc.sleepBodies" ||
        target.arrays[1].name != "Sc.wokeBodies")
    {
        error = "scene clock sleep/wake list schema changed";
        return false;
    }
    // Sc::Scene stores BodyCore*, not BodySim*, in these two lists. Array
    // storage identity does not establish entry identity, so require every
    // saved entry to name a current dynamic actor core before writing it.
    if (target.arrays[0].values.empty() && target.arrays[1].values.empty())
        return true;
    const PxActorTypeFlags dynamics = PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(dynamics);
    std::vector<PxActor*> actors(count);
    if (count && scene.getActors(dynamics, actors.data(), count) != count)
    {
        error = "dynamic actor enumeration changed during clock preflight";
        return false;
    }
    std::set<std::uintptr_t> liveCores;
    for (PxActor* actor : actors)
    {
        if (!actor || actor->getType() != PxActorType::eRIGID_DYNAMIC)
        {
            error = "dynamic actor inventory is malformed";
            return false;
        }
        Sc::BodyCore& core = static_cast<NpRigidDynamic*>(actor)
            ->getScbBodyFast().getScBody();
        if (core.Sc::ActorCore::getSim())
            liveCores.insert(reinterpret_cast<std::uintptr_t>(&core));
    }
    for (std::size_t list = 0; list < 2; ++list)
    {
        std::set<std::uintptr_t> seen;
        for (std::uintptr_t pointer : target.arrays[list].values)
            if (!liveCores.count(pointer) || !seen.insert(pointer).second)
            {
                error = target.arrays[list].name +
                    " has an absent or duplicate live BodyCore";
                return false;
            }
    }
    return true;
}

template <typename T>
void writePointerArray(Ps::Array<T*>& array,
                       const SceneClockImage::PointerArray& row)
{
    array.resize(static_cast<PxU32>(row.values.size()));
    for (PxU32 i = 0; i < array.size(); ++i)
        array[i] = reinterpret_cast<T*>(row.values[i]);
}

void writeTracker(Sc::ObjectIDTracker& tracker,
                  const SceneClockImage::IDTracker& row)
{
    tracker.mIDPool.mCurrentID = row.currentId;
    Ps::Array<PxU32>& free = tracker.mIDPool.mFreeIDs;
    free.resize(static_cast<PxU32>(row.freeIds.size()));
    for (PxU32 i = 0; i < free.size(); ++i)
        free[i] = row.freeIds[i];
}

void writeImage(PxScene& scene, const SceneClockImage& image)
{
    NpScene& np = static_cast<NpScene&>(scene);
    Sc::Scene& sc = np.getScene().getScScene();
    for (const SceneClockImage::Field& field : image.fields)
    {
        if (!field.invariant)
            std::memcpy(reinterpret_cast<void*>(field.address),
                        field.bytes.data(), field.bytes.size());
    }
    writePointerArray(sc.mSleepBodies, image.arrays[0]);
    writePointerArray(sc.mWokeBodies, image.arrays[1]);
    writeTracker(*sc.mShapeIDTracker, image.shapeIds);
    writeTracker(*sc.mRigidIDTracker, image.rigidIds);
}

bool equalTracker(const SceneClockImage::IDTracker& a,
                  const SceneClockImage::IDTracker& b,
                  std::string& firstDifference)
{
    if (a.name != b.name || a.object != b.object ||
        a.currentId != b.currentId || a.freeData != b.freeData ||
        a.freeCapacity != b.freeCapacity || a.freeIds != b.freeIds ||
        a.pendingData != b.pendingData ||
        a.pendingCapacity != b.pendingCapacity ||
        a.bitmapData != b.bitmapData || a.bitmapWords != b.bitmapWords)
    {
        firstDifference = a.name + " differs";
        return false;
    }
    return true;
}

} // namespace

bool SceneClockImage::equals(const SceneClockImage& other,
                             std::string& firstDifference) const
{
    firstDifference.clear();
    if (npScene != other.npScene || scScene != other.scScene ||
        fields.size() != other.fields.size() ||
        arrays.size() != other.arrays.size())
    {
        firstDifference = "scene identity or schema differs";
        return false;
    }
    for (size_t i = 0; i < fields.size(); ++i)
    {
        if (fields[i].name != other.fields[i].name ||
            fields[i].address != other.fields[i].address ||
            fields[i].bytes != other.fields[i].bytes ||
            fields[i].invariant != other.fields[i].invariant)
        {
            firstDifference = fields[i].name + " differs";
            return false;
        }
    }
    for (size_t i = 0; i < arrays.size(); ++i)
    {
        const PointerArray& a = arrays[i];
        const PointerArray& b = other.arrays[i];
        if (a.name != b.name || a.object != b.object || a.data != b.data ||
            a.capacity != b.capacity || a.values != b.values ||
            a.invariant != b.invariant)
        {
            firstDifference = a.name + " differs";
            return false;
        }
    }
    return equalTracker(shapeIds, other.shapeIds, firstDifference) &&
           equalTracker(rigidIds, other.rigidIds, firstDifference);
}

bool CaptureSceneClock(PxScene& scene, SceneClockImage& image,
                       std::string& error)
{
    error.clear();
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering() ||
        np.mCollisionRunning)
    {
        error = "scene is not stopped after fetchResults";
        return false;
    }
    Sc::Scene& sc = np.getScene().getScScene();
    if (sc.mBatchRemoveState || sc.mConstraintArray.size() ||
        sc.mArticulations.size() || unsupportedDeformables(sc) ||
        sc.mBrokenConstraints.size() ||
        sc.mActiveBreakableConstraints.size() ||
        sc.mLostTouchPairs.size() ||
        sc.mLostTouchPairsDeletedBodyIDs.count() ||
        sc.mTriggerBufferAPI.size() ||
        sc.mTriggerBufferExtraData->size() ||
        sc.mOutOfBoundsIDs.size() || sc.mErrorState)
    {
        error = "scene has pending events, removal, error, or unsupported feature";
        return false;
    }
    SceneClockImage next;
    next.npScene = reinterpret_cast<std::uintptr_t>(&np);
    next.scScene = reinterpret_cast<std::uintptr_t>(&sc);

    // Next-step reads/updates from ScScene.cpp and NpScene.cpp.
    addField(next, "Sc.gravity", sc.mGravity);
    addField(next, "Sc.bodyGravityDirty", sc.mBodyGravityDirty);
    addField(next, "Sc.dt", sc.mDt);
    addField(next, "Sc.oneOverDt", sc.mOneOverDt);
    addField(next, "Sc.globalTime", sc.mGlobalTime);
    addField(next, "Sc.timeStamp", sc.mTimeStamp);
    addField(next, "Sc.reportShapePairTimeStamp", sc.mReportShapePairTimeStamp);
    addField(next, "Sc.removedShapeCountAtSimStart",
             sc.mRemovedShapeCountAtSimStart);
    addField(next, "Sc.wokeBodyListValid", sc.mWokeBodyListValid);
    addField(next, "Sc.sleepBodyListValid", sc.mSleepBodyListValid);
    addField(next, "Sc.internalFlags", sc.mInternalFlags);
    addField(next, "Sc.contactReportsNeedPostSolverVelocity",
             sc.mContactReportsNeedPostSolverVelocity);
    addField(next, "Sc.constraintIndex", sc.mConstraintIndex);
    addField(next, "Sc.visualizationScale", sc.mVisualizationScale);
    addField(next, "Sc.visualizationParameterChanged",
             sc.mVisualizationParameterChanged);
    // ScSimStats.h defines five fixed-size PxU32 tables. Capture each
    // source-defined table, not the enclosing allocation or its padding.
    Sc::SimStats& stats = sc.getStatsInternal();
    addField(next, "Sc.simStats.broadPhaseAdds", stats.numBroadPhaseAdds);
    addField(next, "Sc.simStats.broadPhaseRemoves", stats.numBroadPhaseRemoves);
    addField(next, "Sc.simStats.broadPhaseAddsPending",
             stats.numBroadPhaseAddsPending);
    addField(next, "Sc.simStats.broadPhaseRemovesPending",
             stats.numBroadPhaseRemovesPending);
    addField(next, "Sc.simStats.triggerPairs", stats.numTriggerPairs);
    addField(next, "Np.elapsedTime", np.elapsedTime);
    addField(next, "Np.hasSimulated", np.mHasSimulated);
    addField(next, "Np.controllingSimulation", np.mControllingSimulation);

    // Configuration/topology cannot be silently imported from another scene.
    addField(next, "Sc.publicFlags", sc.mPublicFlags, true);
    addField(next, "Sc.simulateOrder", sc.mSimulateOrder, true);
    addField(next, "Sc.enableStabilization", sc.mEnableStabilization, true);
    addField(next, "Sc.dominanceBitMatrix", sc.mDominanceBitMatrix, true);
    addField(next, "Sc.nbRigidStatics", sc.mNbRigidStatics, true);
    addField(next, "Sc.nbRigidDynamics", sc.mNbRigidDynamics, true);
    addField(next, "Sc.nbGeometries", sc.mNbGeometries, true);
    addField(next, "Sc.filterShaderData", sc.mFilterShaderData, true);
    addField(next, "Sc.filterShaderDataSize", sc.mFilterShaderDataSize, true);
    addField(next, "Sc.filterShader", sc.mFilterShader, true);
    addField(next, "Sc.filterCallback", sc.mFilterCallback, true);
    addField(next, "Sc.nPhaseCore", sc.mNPhaseCore, true);
    addField(next, "Sc.interactionScene", sc.mInteractionScene, true);
    addField(next, "Sc.staticAnchor", sc.mStaticAnchor, true);
    addField(next, "Sc.shapeIDTracker", sc.mShapeIDTracker, true);
    addField(next, "Sc.rigidIDTracker", sc.mRigidIDTracker, true);
    addField(next, "Sc.simStats.object", sc.mStats, true);
    addField(next, "Np.nbClients", np.mNbClients, true);

    addPointerArray(next, "Sc.sleepBodies", sc.mSleepBodies);
    addPointerArray(next, "Sc.wokeBodies", sc.mWokeBodies);
    addPointerArray(next, "Np.rigidActorOrder", np.mRigidActorArray, true);
    if (!captureTracker(*sc.mShapeIDTracker, "shape IDs", next.shapeIds,
                        error) ||
        !captureTracker(*sc.mRigidIDTracker, "rigid IDs", next.rigidIds,
                        error))
        return false;
    image = std::move(next);
    return true;
}

bool RestoreSceneClock(PxScene& scene, const SceneClockImage& image,
                       std::string& error)
{
    SceneClockImage live;
    if (!CaptureSceneClock(scene, live, error)) return false;
    if (!preflight(image, live, error) ||
        !validBodyListPointers(scene, image, error)) return false;
    writeImage(scene, image);
    SceneClockImage observed;
    std::string verification;
    if (CaptureSceneClock(scene, observed, verification) &&
        image.equals(observed, verification))
    {
        error.clear();
        return true;
    }
    writeImage(scene, live);
    SceneClockImage rolledBack;
    std::string rollbackError;
    if (!CaptureSceneClock(scene, rolledBack, rollbackError) ||
        !live.equals(rolledBack, rollbackError))
    {
        error = "FATAL scene clock rollback failed: " + rollbackError;
        return false;
    }
    error = "scene clock post-write verification failed: " + verification;
    return false;
}

}} // namespace oc2::offline
