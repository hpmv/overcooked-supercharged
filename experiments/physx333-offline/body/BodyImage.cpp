#include "BodyImage.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <set>
#include <utility>
#include <vector>

#include "PxPhysicsAPI.h"

// The access-label shim is confined to this offline source-built test object.
// No vendor source, game binary, or runtime layout is patched.
#define private public
#define protected public
#include "NpScene.h"
#include "NpRigidDynamic.h"
#include "NpRigidStatic.h"
#include "ScBodySim.h"
#include "ScStaticSim.h"
#undef protected
#undef private

namespace oc2 { namespace offline {
namespace {

using namespace physx;
static_assert(sizeof(void*) == 4, "Body image requires Win32 PhysX 3.3.3");
static_assert(sizeof(PxsBodyCore) == 128, "Unexpected PxsBodyCore layout");
static_assert(sizeof(PxsRigidCore) == 32, "Unexpected PxsRigidCore layout");

template <typename T>
void addField(BodyImage& image, const std::string& name, T& value,
              bool topology = false)
{
    BodyImage::Field field;
    field.name = name;
    field.address = reinterpret_cast<std::uintptr_t>(&value);
    field.bytes.resize(sizeof(value));
    std::memcpy(field.bytes.data(), &value, sizeof(value));
    field.topology = topology;
    image.fields.push_back(std::move(field));
}

std::string label(std::uintptr_t id, const char* suffix)
{
    return std::string("actor[") + std::to_string(id) + "]." + suffix;
}

bool settledActor(Scb::Actor& actor, const char* kind, std::string& error)
{
    if (actor.getControlState() != Scb::ControlState::eIN_SCENE ||
        actor.getBufferFlags() != 0 || actor.hasUpdates() ||
        actor.mStreamPtr != nullptr)
    {
        error = std::string(kind) + " has a pending Scb update or buffer";
        return false;
    }
    return true;
}

bool appendShapeIdentity(PxRigidActor& rigid,
                         BodyImage::Actor& binding, std::string& error)
{
    const PxU32 count = rigid.getNbShapes();
    if (count > 4096)
    {
        error = "actor has an excessive shape count";
        return false;
    }
    std::vector<PxShape*> shapes(count);
    if (count && rigid.getShapes(shapes.data(), count) != count)
    {
        error = "actor shape enumeration changed during capture";
        return false;
    }
    for (PxShape* shape : shapes)
    {
        if (!shape)
        {
            error = "actor has a null shape";
            return false;
        }
        binding.shapes.push_back(reinterpret_cast<std::uintptr_t>(shape));
    }
    return true;
}

void appendActorCore(BodyImage& image, std::uintptr_t id,
                     Sc::ActorCore& core)
{
    addField(image, label(id, "ActorCore.aggregateId"),
             core.mAggregateID, true);
    addField(image, label(id, "ActorCore.actorFlags"),
             core.mActorFlags, true);
    addField(image, label(id, "ActorCore.actorType"),
             core.mActorType, true);
    addField(image, label(id, "ActorCore.clientBehaviorFlags"),
             core.mClientBehaviorFlags, true);
    addField(image, label(id, "ActorCore.dominanceGroup"),
             core.mDominanceGroup, true);
    addField(image, label(id, "ActorCore.ownerClient"),
             core.mOwnerClient, true);
}

bool appendDynamic(PxRigidDynamic& actor, BodyImage& image,
                   BodyImage::Actor& binding, std::string& error)
{
    NpRigidDynamic& np = static_cast<NpRigidDynamic&>(actor);
    Scb::Body& scb = np.getScbBodyFast();
    if (!settledActor(scb, "dynamic actor", error) ||
        scb.mBodyBufferFlags != 0)
    {
        if (error.empty()) error = "dynamic actor has pending body flags";
        return false;
    }
    Sc::BodyCore& core = scb.getScBody();
    Sc::BodySim* sim = static_cast<Sc::BodySim*>(
        core.Sc::ActorCore::getSim());
    if (!sim)
    {
        error = "dynamic actor has no BodySim";
        return false;
    }
    PxsRigidBody& ll = sim->getLowLevelBody();
    if (ll.mCore != &core.getCore() || ll.mCCD != nullptr ||
        sim->mConstraintGroup != nullptr || sim->mArticulation != nullptr ||
        sim->readInternalFlag(Sc::BodySim::BF_HAS_CONSTRAINTS))
    {
        error = "dynamic body has unsupported CCD, constraint, articulation, or core pointer: core=" +
                std::to_string(ll.mCore == &core.getCore()) +
                " CCD=" + std::to_string(ll.mCCD != nullptr) +
                " group=" + std::to_string(sim->mConstraintGroup != nullptr) +
                " articulation=" + std::to_string(sim->mArticulation != nullptr) +
                " constraints=" + std::to_string(sim->mBodyConstraints) +
                " constraintFlag=" +
                std::to_string(sim->readInternalFlag(Sc::BodySim::BF_HAS_CONSTRAINTS));
        return false;
    }
    Sc::SimStateData* state = core.mSimStateData;
    if (state && state->getType() > Sc::SimStateData::eKine)
    {
        error = "dynamic body has invalid SimStateData type";
        return false;
    }
    const bool kinematic = core.getFlags().isSet(PxRigidBodyFlag::eKINEMATIC);
    if (state && state->isKine() != kinematic)
    {
        error = "SimStateData type disagrees with kinematic body flag";
        return false;
    }

    binding.scbActor = reinterpret_cast<std::uintptr_t>(&scb);
    binding.core = reinterpret_cast<std::uintptr_t>(&core);
    binding.sim = reinterpret_cast<std::uintptr_t>(sim);
    binding.lowLevelBody = reinterpret_cast<std::uintptr_t>(&ll);
    binding.simStateData = reinterpret_cast<std::uintptr_t>(state);
    binding.rigidId = sim->getID();
    binding.simStateType = state ? state->getType() + 1u : 0u;
    binding.active = sim->isActive() ? 1u : 0u;

    appendActorCore(image, binding.userId, core);
    addField(image, label(binding.userId, "Scb.controlState"),
             scb.mControlState, true);
    addField(image, label(binding.userId, "Scb.bodyBufferFlags"),
             scb.mBodyBufferFlags, true);
    addField(image, label(binding.userId, "Scb.bufferedBody2World"),
             scb.mBufferedBody2World);
    addField(image, label(binding.userId, "Scb.bufferedLinVelocity"),
             scb.mBufferedLinVelocity);
    addField(image, label(binding.userId, "Scb.bufferedAngVelocity"),
             scb.mBufferedAngVelocity);
    addField(image, label(binding.userId, "Scb.bufferedWakeCounter"),
             scb.mBufferedWakeCounter);
    addField(image, label(binding.userId, "Scb.bufferedIsSleeping"),
             scb.mBufferedIsSleeping);

    addField(image, label(binding.userId, "BodyCore.lowLevelCore"), core.mCore);
    addField(image, label(binding.userId, "BodyCore.sleepThreshold"),
             core.mSleepThreshold);
    addField(image, label(binding.userId, "BodyCore.freezeThreshold"),
             core.mFreezeThreshold);
    addField(image, label(binding.userId, "BodyCore.wakeCounter"),
             core.mWakeCounter);

    addField(image, label(binding.userId, "BodySim.islandNodeInfo"),
             sim->mIslandNodeInfo, true);
    addField(image, label(binding.userId, "BodySim.islandHook"),
             sim->mLLIslandHook, true);
    addField(image, label(binding.userId, "BodySim.internalFlags"),
             sim->mInternalFlags);
    addField(image, label(binding.userId, "BodySim.velocityModState"),
             sim->mVelModState);
    addField(image, label(binding.userId, "BodySim.bodyConstraints"),
             sim->mBodyConstraints);
    addField(image, label(binding.userId, "BodySim.sleepLinearAccumulator"),
             sim->mSleepLinVelAcc);
    addField(image, label(binding.userId, "BodySim.freezeCount"),
             sim->mFreezeCount);
    addField(image, label(binding.userId, "BodySim.sleepAngularAccumulator"),
             sim->mSleepAngVelAcc);
    addField(image, label(binding.userId, "BodySim.accelerationScale"),
             sim->mAccelScale);

    addField(image, label(binding.userId, "PxsRigidBody.acceleration"),
             ll.mAcceleration);
    addField(image, label(binding.userId, "PxsRigidBody.lastTransform"),
             ll.mLastTransform);
    addField(image, label(binding.userId, "PxsRigidBody.aabbManagerId"),
             ll.mAABBMgrId, true);
    if (state)
        addField(image, label(binding.userId, "SimStateData.payload"),
                 state->data);
    return true;
}

bool appendStatic(PxRigidStatic& actor, BodyImage& image,
                  BodyImage::Actor& binding, std::string& error)
{
    NpRigidStatic& np = static_cast<NpRigidStatic&>(actor);
    Scb::RigidStatic& scb = np.getScbRigidStaticFast();
    if (!settledActor(scb, "static actor", error)) return false;
    Sc::StaticCore& core = scb.getScStatic();
    Sc::StaticSim* sim = static_cast<Sc::StaticSim*>(
        core.Sc::ActorCore::getSim());
    if (!sim)
    {
        error = "static actor has no StaticSim";
        return false;
    }
    binding.scbActor = reinterpret_cast<std::uintptr_t>(&scb);
    binding.core = reinterpret_cast<std::uintptr_t>(&core);
    binding.sim = reinterpret_cast<std::uintptr_t>(sim);
    binding.rigidId = sim->getID();
    binding.active = sim->isActive() ? 1u : 0u;
    appendActorCore(image, binding.userId, core);
    addField(image, label(binding.userId, "Scb.controlState"),
             scb.mControlState, true);
    addField(image, label(binding.userId, "StaticCore.lowLevelCore"),
             core.getCore());
    addField(image, label(binding.userId, "StaticSim.islandNodeInfo"),
             sim->mIslandNodeInfo, true);
    return true;
}

bool sameActor(const BodyImage::Actor& a, const BodyImage::Actor& b)
{
    return a.userId == b.userId && a.pxActor == b.pxActor &&
           a.scbActor == b.scbActor && a.core == b.core &&
           a.sim == b.sim && a.lowLevelBody == b.lowLevelBody &&
           a.simStateData == b.simStateData && a.type == b.type &&
           a.rigidId == b.rigidId && a.simStateType == b.simStateType &&
           a.active == b.active && a.shapes == b.shapes;
}

bool sameField(const BodyImage::Field& a, const BodyImage::Field& b)
{
    return a.name == b.name && a.address == b.address &&
           a.topology == b.topology && a.bytes == b.bytes;
}

const BodyImage::Field* findField(const BodyImage& image,
                                  std::uintptr_t id, const char* suffix)
{
    const std::string name = label(id, suffix);
    for (const BodyImage::Field& field : image.fields)
        if (field.name == name) return &field;
    return nullptr;
}

template <typename T>
bool decodeField(const BodyImage& image, std::uintptr_t id,
                 const char* suffix, T& value, std::string& error)
{
    const BodyImage::Field* field = findField(image, id, suffix);
    if (!field || field->bytes.size() != sizeof(T))
    {
        error = "missing or malformed saved field " + label(id, suffix);
        return false;
    }
    std::memcpy(&value, field->bytes.data(), sizeof(T));
    return true;
}

bool validatePayload(const BodyImage& image, std::string& error)
{
    for (const BodyImage::Actor& actor : image.actors)
    {
        if (actor.type == PxActorType::eRIGID_STATIC)
        {
            PxsRigidCore core;
            if (!decodeField(image, actor.userId, "StaticCore.lowLevelCore",
                             core, error)) return false;
            if (!core.body2World.isSane())
            {
                error = "saved static pose is invalid";
                return false;
            }
            continue;
        }
        if (actor.type != PxActorType::eRIGID_DYNAMIC)
        {
            error = "saved body image has unsupported actor type";
            return false;
        }
        PxsBodyCore core;
        PxTransform last(PxIdentity);
        PxTransform buffered(PxIdentity);
        PxU32 sleeping = 0;
        if (!decodeField(image, actor.userId, "BodyCore.lowLevelCore",
                         core, error) ||
            !decodeField(image, actor.userId, "PxsRigidBody.lastTransform",
                         last, error) ||
            !decodeField(image, actor.userId, "Scb.bufferedBody2World",
                         buffered, error) ||
            !decodeField(image, actor.userId, "Scb.bufferedIsSleeping",
                         sleeping, error)) return false;
        if (!core.body2World.isSane() || !core.body2Actor.isSane() ||
            !last.isSane() || !buffered.isSane() ||
            !core.linearVelocity.isFinite() ||
            !core.angularVelocity.isFinite() ||
            !core.inverseInertia.isFinite() || sleeping > 1)
        {
            error = "saved dynamic body has invalid pose, velocity, inertia, or sleep bit";
            return false;
        }
        if (sleeping == actor.active)
        {
            error = "saved Scb sleep bit disagrees with Actor active state";
            return false;
        }
        const bool kinematic = core.mFlags.isSet(PxRigidBodyFlag::eKINEMATIC);
        if (kinematic != (actor.simStateType == Sc::SimStateData::eKine + 1u))
        {
            error = "saved body flag disagrees with SimStateData type";
            return false;
        }
        if (actor.simStateType)
        {
            Sc::SimStateData data;
            if (!decodeField(image, actor.userId, "SimStateData.payload",
                             data.data, error)) return false;
            if (data.getType() + 1u != actor.simStateType)
            {
                error = "saved SimStateData discriminator changed";
                return false;
            }
            if (data.isKine())
            {
                const Sc::Kinematic* kine = data.getKinematicData();
                if (kine->targetValid > 1 ||
                    (kine->targetValid && !kine->targetPose.isSane()))
                {
                    error = "saved kinematic target is invalid";
                    return false;
                }
            }
            else
            {
                const Sc::VelocityMod* velocity = data.getVelocityModData();
                if (!velocity->linearPerSec.isFinite() ||
                    !velocity->angularPerSec.isFinite() ||
                    !velocity->linearPerStep.isFinite() ||
                    !velocity->angularPerStep.isFinite())
                {
                    error = "saved velocity modification is invalid";
                    return false;
                }
            }
        }
    }
    return true;
}

void writeFields(const BodyImage& image)
{
    for (const BodyImage::Field& field : image.fields)
        if (!field.topology && !field.bytes.empty())
            std::memcpy(reinterpret_cast<void*>(field.address),
                        field.bytes.data(), field.bytes.size());
}

} // namespace

bool BodyImage::equals(const BodyImage& other,
                       std::string& firstDifference) const
{
    firstDifference.clear();
    if (scene != other.scene || actors.size() != other.actors.size() ||
        fields.size() != other.fields.size())
    {
        firstDifference = "scene identity or body image size";
        return false;
    }
    for (std::size_t i = 0; i < actors.size(); ++i)
        if (!sameActor(actors[i], other.actors[i]))
        {
            firstDifference = "actor identity/shape topology at index " +
                              std::to_string(i);
            return false;
        }
    for (std::size_t i = 0; i < fields.size(); ++i)
        if (!sameField(fields[i], other.fields[i]))
        {
            firstDifference = "body field " + fields[i].name;
            return false;
        }
    return true;
}

bool CaptureBodies(PxScene& scene, BodyImage& image, std::string& error)
{
    error.clear();
    NpScene& np = static_cast<NpScene&>(scene);
    if (np.isPhysicsRunning() || np.isPhysicsBuffering())
    {
        error = "scene is inside simulate/collide/fetchResults";
        return false;
    }
    if (scene.getNbArticulations() != 0)
    {
        error = "articulation body state is unsupported";
        return false;
    }
    const PxActorTypeFlags flags = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    const PxU32 count = scene.getNbActors(flags);
    if (count > 65536)
    {
        error = "scene has an excessive rigid actor count";
        return false;
    }
    std::vector<PxActor*> actors(count);
    if (count && scene.getActors(flags, actors.data(), count) != count)
    {
        error = "actor enumeration changed during capture";
        return false;
    }
    std::sort(actors.begin(), actors.end(), [](PxActor* a, PxActor* b) {
        return reinterpret_cast<std::uintptr_t>(a->userData) <
               reinterpret_cast<std::uintptr_t>(b->userData);
    });
    BodyImage fresh;
    fresh.scene = reinterpret_cast<std::uintptr_t>(&scene);
    std::set<std::uintptr_t> userIds;
    for (PxActor* actor : actors)
    {
        if (!actor)
        {
            error = "scene returned a null actor";
            return false;
        }
        BodyImage::Actor binding;
        binding.userId = reinterpret_cast<std::uintptr_t>(actor->userData);
        if (binding.userId == 0 || !userIds.insert(binding.userId).second)
        {
            error = "rigid actors require nonzero unique userData IDs";
            return false;
        }
        binding.pxActor = reinterpret_cast<std::uintptr_t>(actor);
        binding.type = actor->getType();
        if (!appendShapeIdentity(static_cast<PxRigidActor&>(*actor),
                                 binding, error))
            return false;
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
        {
            if (!appendDynamic(static_cast<PxRigidDynamic&>(*actor),
                               fresh, binding, error)) return false;
        }
        else if (actor->getType() == PxActorType::eRIGID_STATIC)
        {
            if (!appendStatic(static_cast<PxRigidStatic&>(*actor),
                              fresh, binding, error)) return false;
        }
        else
        {
            error = "unsupported actor type";
            return false;
        }
        fresh.actors.push_back(std::move(binding));
    }
    image = std::move(fresh);
    return true;
}

bool RestoreBodies(PxScene& scene, const BodyImage& image,
                   std::string& error)
{
    error.clear();
    BodyImage live;
    if (!CaptureBodies(scene, live, error)) return false;
    if (image.scene != live.scene ||
        image.actors.size() != live.actors.size() ||
        image.fields.size() != live.fields.size())
    {
        error = "body image belongs to another scene or actor topology";
        return false;
    }
    for (std::size_t i = 0; i < live.actors.size(); ++i)
        if (!sameActor(image.actors[i], live.actors[i]))
        {
            error = "body actor, shape, active state, or SimStateData binding changed";
            return false;
        }
    for (std::size_t i = 0; i < live.fields.size(); ++i)
    {
        const BodyImage::Field& saved = image.fields[i];
        const BodyImage::Field& current = live.fields[i];
        if (saved.name != current.name ||
            saved.address != current.address ||
            saved.bytes.size() != current.bytes.size() ||
            saved.topology != current.topology)
        {
            error = "body field layout or address changed at index " +
                    std::to_string(i);
            return false;
        }
        if (saved.topology && saved.bytes != current.bytes)
        {
            error = "body topology field changed: " + saved.name;
            return false;
        }
    }
    if (!validatePayload(image, error)) return false;
    const PxU16 sceneListBits = Sc::BodySim::BF_IS_IN_SLEEP_LIST |
                                Sc::BodySim::BF_IS_IN_WAKEUP_LIST |
                                Sc::BodySim::BF_SLEEP_NOTIFY |
                                Sc::BodySim::BF_WAKEUP_NOTIFY;
    for (const BodyImage::Actor& actor : image.actors)
        if (actor.type == PxActorType::eRIGID_DYNAMIC)
        {
            PxU16 savedFlags = 0;
            PxU16 liveFlags = 0;
            if (!decodeField(image, actor.userId, "BodySim.internalFlags",
                             savedFlags, error) ||
                !decodeField(live, actor.userId, "BodySim.internalFlags",
                             liveFlags, error)) return false;
            if ((savedFlags & sceneListBits) != (liveFlags & sceneListBits))
            {
                error = "body sleep/wake list membership changed; restore the scene lists first";
                return false;
            }
        }

    // All identities, addresses, field sizes, and read-only relationships are
    // validated before writing. The original image is the rollback snapshot.
    writeFields(image);
    BodyImage observed;
    std::string verifyError;
    bool verified = false;
    try
    {
        verified = CaptureBodies(scene, observed, verifyError) &&
                   image.equals(observed, verifyError);
    }
    catch (...)
    {
        verifyError = "postwrite body capture threw an exception";
    }
    if (!verified)
    {
        writeFields(live);
        BodyImage reverted;
        std::string rollbackError;
        try
        {
            if (!CaptureBodies(scene, reverted, rollbackError) ||
                !live.equals(reverted, rollbackError))
                std::abort();
        }
        catch (...)
        {
            std::abort();
        }
        error = "body restore failed and was rolled back: " + verifyError;
        return false;
    }
    return true;
}

}} // namespace oc2::offline
