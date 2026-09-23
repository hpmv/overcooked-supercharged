// Source-built diagnostic of a public actor/shape lifetime crossing an A
// checkpoint. The arena return below is a reference, never a component stage.
#define main level_arena_embedded_main
#include "../level_arena/LevelArena.cpp"
#undef main

namespace {

struct ActorIdentity
{
    std::uintptr_t npActor = 0;
    std::uintptr_t npShape = 0;
    std::uintptr_t scActorSim = 0;
    std::uintptr_t scShapeCore = 0;
    std::uintptr_t scShapeSim = 0;
    PxU32 shapeId = PX_INVALID_U32;
    PxU32 transformCacheId = PX_INVALID_U32;
};

PxRigidStatic* findStatic(World& world, PxU32 actorId)
{
    for (PxRigidActor* actor : world.actors)
        if (id(actor) == actorId &&
            actor->getType() == PxActorType::eRIGID_STATIC)
            return static_cast<PxRigidStatic*>(actor);
    fail("selected static actor is absent");
    return nullptr;
}

ActorIdentity identify(PxRigidStatic& actor)
{
    if (actor.getNbShapes() != 1) fail("selected actor shape count changed");
    PxShape* shape = nullptr;
    if (actor.getShapes(&shape, 1) != 1 || !shape)
        fail("selected actor shape enumeration changed");
    Sc::Actor* actorSim = scActor(actor);
    if (!actorSim) fail("selected actor has no Sc simulation object");
    Sc::RigidSim* rigid = static_cast<Sc::RigidSim*>(actorSim);
    Sc::Element* element = rigid->getElements_();
    if (!element || element->mNextInActor ||
        element->getElementType() != Sc::PX_ELEMENT_TYPE_SHAPE)
        fail("selected actor Sc shape list changed");
    Sc::ShapeSim* shapeSim = static_cast<Sc::ShapeSim*>(element);
    ActorIdentity result;
    result.npActor = reinterpret_cast<std::uintptr_t>(&actor);
    result.npShape = reinterpret_cast<std::uintptr_t>(shape);
    result.scActorSim = reinterpret_cast<std::uintptr_t>(actorSim);
    result.scShapeCore = reinterpret_cast<std::uintptr_t>(
        &static_cast<NpShape&>(*shape).getScbShape().getScShape());
    result.scShapeSim = reinterpret_cast<std::uintptr_t>(shapeSim);
    result.shapeId = shapeSim->getID();
    result.transformCacheId = shapeSim->getTransformCacheID();
    return result;
}

void traceIdentity(const char* stage, const ActorIdentity& value)
{
    std::cout << "ACTOR_LIFETIME_IDENTITY " << stage
              << " np_actor=" << value.npActor
              << " np_shape=" << value.npShape
              << " sc_actor_sim=" << value.scActorSim
              << " sc_shape_core=" << value.scShapeCore
              << " sc_shape_sim=" << value.scShapeSim
              << " shape_id=" << value.shapeId
              << " transform_cache_id=" << value.transformCacheId << '\n';
}

void requireRejectedWithoutWrite(World& world, const Snapshot& changed,
                                 const char* stage, bool accepted,
                                 const std::string& error)
{
    if (accepted) fail(std::string(stage) + " accepted a replaced actor");
    std::cout << "ACTOR_LIFETIME_GATE " << stage
              << " rejected=1 reason=" << error << '\n';
    equalSnapshot(changed, world.capture());
}

void checkActorLifetime()
{
    oc2::offline::ArenaSnapshotAllocator arena(256u * 1024u * 1024u);
    if (!arena.valid()) fail("reserve actor-lifetime diagnostic arena");
    Runtime runtime(&arena);
    World world(runtime);
    world.step(0.0f);
    const Snapshot a = world.step(0.0f);
    verify(a, false);
    if (a.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != 12 ||
        a.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != 4 ||
        a.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2)
        fail("A is not the 12/4/2 joined fixture");
    const auto arenaA = arena.capture();
    const auto actorsA = world.actors;
    const auto registryA = world.memBlockRegistry;
    const ActorIdentity original = identify(*findStatic(world, kCommonA));
    traceIdentity("A", original);

    // kCommonA owns one box that participates in the 12-contact graph.
    // Repeat its public construction inputs while crossing both lifetimes.
    PxRigidStatic* oldActor = findStatic(world, kCommonA);
    const auto oldEntry = std::find(world.actors.begin(), world.actors.end(),
                                    static_cast<PxRigidActor*>(oldActor));
    if (oldEntry == world.actors.end()) fail("selected actor bookkeeping absent");
    world.actors.erase(oldEntry);
    oldActor->release();
    world.addStatic(kCommonA, Common, -3.3f, 0.0f, 4.5f, false);
    const ActorIdentity replacement = identify(*findStatic(world, kCommonA));
    traceIdentity("replacement", replacement);
    if (original.npActor != replacement.npActor ||
        original.npShape != replacement.npShape ||
        original.scActorSim != replacement.scActorSim ||
        original.scShapeCore != replacement.scShapeCore ||
        original.scShapeSim != replacement.scShapeSim)
        fail("pinned actor/shape pool no longer reuses every measured address");
    if (original.shapeId != 0 || replacement.shapeId != 14 ||
        replacement.transformCacheId != PX_INVALID_U32)
        fail("pinned replacement shape generation changed");
    const Snapshot changed = world.step(0.0f);
    const ActorIdentity settled = identify(*findStatic(world, kCommonA));
    traceIdentity("settled_replacement", settled);
    if (settled.shapeId != 14 ||
        settled.npActor != original.npActor ||
        settled.scShapeSim != original.scShapeSim ||
        settled.transformCacheId != original.transformCacheId)
        fail("settled replacement lost the observed pointer/ID split");
    std::cout << "ACTOR_LIFETIME_GRAPH contacts="
              << changed.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP]
              << " triggers="
              << changed.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER]
              << " markers="
              << changed.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER]
              << " semantic_equal=" << (a.graph == changed.graph)
              << " scene_pairs_equal="
              << (a.graph.scenePairs == changed.graph.scenePairs)
              << " actor_order_equal="
              << (a.graph.actors == changed.graph.actors)
              << " active_bodies_equal="
              << (a.graph.activeBodies == changed.graph.activeBodies)
              << '\n';
    if (changed.graph.counts[Sc::PX_INTERACTION_TYPE_OVERLAP] != 12 ||
        changed.graph.counts[Sc::PX_INTERACTION_TYPE_TRIGGER] != 4 ||
        changed.graph.counts[Sc::PX_INTERACTION_TYPE_MARKER] != 2 ||
        a.graph == changed.graph ||
        a.graph.scenePairs == changed.graph.scenePairs ||
        a.graph.actors == changed.graph.actors ||
        a.graph.activeBodies != changed.graph.activeBodies)
        fail("lifetime path no longer retains 12/4/2 with a changed graph");
    std::cout << "ACTOR_LIFETIME_SHAPE_IDS A_current="
              << a.clock.shapeIds.currentId
              << " changed_current=" << changed.clock.shapeIds.currentId
              << " A_free=" << a.clock.shapeIds.freeIds.size()
              << " changed_free=" << changed.clock.shapeIds.freeIds.size()
              << '\n';
    if (a.clock.arrays.size() < 3 || changed.clock.arrays.size() < 3 ||
        a.clock.arrays[2].name != "Np.rigidActorOrder" ||
        changed.clock.arrays[2].name != "Np.rigidActorOrder" ||
        a.clock.arrays[2].values == changed.clock.arrays[2].values)
        fail("public actor array no longer changes order across replacement");
    for (std::size_t i = 0; i < a.clock.arrays[2].values.size(); ++i)
        if (a.clock.arrays[2].values[i] != changed.clock.arrays[2].values[i])
        {
            std::cout << "ACTOR_LIFETIME_ACTOR_ORDER first_index=" << i
                      << " A_actor=" << a.clock.arrays[2].values[i]
                      << " changed_actor=" << changed.clock.arrays[2].values[i]
                      << '\n';
            break;
        }
    for (std::size_t i = 0; i < a.sap.bindings.size() &&
                            i < changed.sap.bindings.size(); ++i)
    {
        const auto& source = a.sap.bindings[i];
        const auto& live = changed.sap.bindings[i];
        if (source.id != live.id || source.userData != live.userData ||
            source.group != live.group || source.ownerId != live.ownerId ||
            source.aabbDataHandle != live.aabbDataHandle ||
            source.shapeCore != live.shapeCore ||
            source.rigidCore != live.rigidCore ||
            source.bodyAtom != live.bodyAtom ||
            source.localSpaceAabb != live.localSpaceAabb)
        {
            std::cout << "ACTOR_LIFETIME_SAP_BINDING first_ordinal=" << i
                      << " A_slot=" << source.id << " changed_slot=" << live.id
                      << " A_owner=" << source.ownerId
                      << " changed_owner=" << live.ownerId
                      << " A_handle=" << source.aabbDataHandle
                      << " changed_handle=" << live.aabbDataHandle
                      << " A_shape_core=" << source.shapeCore
                      << " changed_shape_core=" << live.shapeCore
                      << " A_rigid_core=" << source.rigidCore
                      << " changed_rigid_core=" << live.rigidCore << '\n';
            break;
        }
    }

    std::string error;
    const bool clockAccepted = oc2::offline::RestoreSceneClock(
        *world.scene, a.clock, error);
    requireRejectedWithoutWrite(world, changed, "SceneClock", clockAccepted,
                                error);
    const bool shapeAccepted = oc2::offline::RestoreShapeCacheBindings(
        *world.scene, a.shapeCache, error);
    requireRejectedWithoutWrite(world, changed, "ShapeCacheBindings",
                                shapeAccepted, error);
    const bool sapAccepted = oc2::offline::RestoreSap(
        *world.scene, a.sap, error);
    requireRejectedWithoutWrite(world, changed, "SAP", sapAccepted, error);

    const auto changedArena = arena.capture();
    std::cout << "ACTOR_LIFETIME_LEDGER A_blocks=" << arenaA.blocks.size()
              << " changed_blocks=" << changedArena.blocks.size()
              << " A_cursor=" << arenaA.cursor
              << " changed_cursor=" << changedArena.cursor << '\n';
    if (!arena.restore(arenaA, error))
        fail("raw arena reference return: " + error);
    // These fixture bookkeeping containers are outside PhysX's arena.
    world.actors = actorsA;
    world.memBlockRegistry = registryA;
    world.callback.rows = a.events;
    const auto returnedArena = arena.capture();
    if (!arenaA.equals(returnedArena, error))
        fail("raw arena reference bytes/ledger: " + error);
    equalSnapshot(a, world.capture());
    const ActorIdentity returned = identify(*findStatic(world, kCommonA));
    traceIdentity("raw_return_A", returned);
    if (returned.npActor != original.npActor ||
        returned.npShape != original.npShape ||
        returned.scActorSim != original.scActorSim ||
        returned.scShapeCore != original.scShapeCore ||
        returned.scShapeSim != original.scShapeSim ||
        returned.shapeId != original.shapeId ||
        returned.transformCacheId != original.transformCacheId)
        fail("raw arena did not return selected actor/shape identity");
    if (runtime.errors.count)
        fail("PhysX reported an actor-lifetime diagnostic error");
    std::cout << "PASS actor-lifetime negative gates and raw arena A reference\n";
}

} // namespace

int main()
{
    checkActorLifetime();
    return 0;
}
