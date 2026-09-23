// Differential test for replaying through an existing PhysX 3.3.3 scene.
// No Unity binary is loaded. The reused path retains every PxActor/PxShape
// object, removes all scene participation, flushes, and reinserts in the
// original order before replaying the exact same public mutation script.
#define main level_graph_embedded_main
#include "../level_graph/LevelGraph.cpp"
#undef main

namespace {

struct BodyStart
{
    PxTransform pose;
    PxVec3 linear, angular;
    PxReal wakeCounter;
    bool sleeping;
};

std::map<PxU32, BodyStart> captureBodyStarts(World& world)
{
    std::map<PxU32, BodyStart> result;
    for (PxRigidActor* actor : world.actors)
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
        {
            PxRigidDynamic& body = *static_cast<PxRigidDynamic*>(actor);
            BodyStart row = { body.getGlobalPose(), body.getLinearVelocity(),
                              body.getAngularVelocity(),
                              body.getWakeCounter(), body.isSleeping() };
            result.insert(std::make_pair(id(actor), row));
        }
    return result;
}

std::vector<std::uintptr_t> actorShapePointers(World& world)
{
    std::vector<std::uintptr_t> result;
    for (PxRigidActor* actor : world.actors)
    {
        result.push_back(reinterpret_cast<std::uintptr_t>(actor));
        std::vector<PxShape*> shapes(actor->getNbShapes());
        if (actor->getShapes(shapes.data(),
                static_cast<PxU32>(shapes.size())) != shapes.size())
            fail("identity shape enumeration changed");
        for (PxShape* shape : shapes)
            result.push_back(reinterpret_cast<std::uintptr_t>(shape));
    }
    return result;
}

void removeAndReinsert(World& world,
                       const std::map<PxU32, BodyStart>& starts,
                       bool reverseRemoval, bool replaceScene)
{
    const PxActorTypeFlags rigid = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    if (reverseRemoval)
        for (auto it = world.actors.rbegin(); it != world.actors.rend(); ++it)
            world.scene->removeActor(**it, false);
    else
        for (PxRigidActor* actor : world.actors)
            world.scene->removeActor(*actor, false);
    if (world.scene->getNbActors(rigid) != 0)
        fail("actor removal left public actors in scene");
    world.scene->flushSimulation(false);
    world.scene->flushQueryUpdates();
    if (replaceScene)
    {
        world.scene->release();
        PxSceneDesc desc(world.runtime.physics->getTolerancesScale());
        desc.gravity = PxVec3(0.0f);
        desc.cpuDispatcher = &world.runtime.dispatcher;
        desc.filterShader = fixtureFilter;
        desc.simulationEventCallback = &world.callback;
        desc.broadPhaseType = PxBroadPhaseType::eSAP;
        desc.staticStructure = PxPruningStructure::eSTATIC_AABB_TREE;
        desc.dynamicStructure = PxPruningStructure::eDYNAMIC_AABB_TREE;
        desc.flags |= PxSceneFlag::eENABLE_PCM;
        world.scene = world.runtime.physics->createScene(desc);
        if (!world.scene) fail("fresh scene for retained actors");
        world.memBlockRegistry = physx333_offline::MemBlockIdentityRegistry();
    }

    for (PxRigidActor* actor : world.actors)
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
        {
            PxRigidDynamic& body = *static_cast<PxRigidDynamic*>(actor);
            const auto found = starts.find(id(actor));
            if (found == starts.end()) fail("missing initial dynamic body");
            const BodyStart& row = found->second;
            body.setGlobalPose(row.pose);
            body.setLinearVelocity(row.linear);
            body.setAngularVelocity(row.angular);
            body.setWakeCounter(row.wakeCounter);
            if (row.sleeping) body.putToSleep();
        }
    for (PxRigidActor* actor : world.actors)
        world.scene->addActor(*actor);
    world.scene->flushQueryUpdates();
    if (world.scene->getNbActors(rigid) != world.actors.size())
        fail("actor reinsert inventory differs");
}

PxU32 bits(PxReal number)
{
    PxU32 result = 0;
    static_assert(sizeof(result) == sizeof(number), "float word changed");
    std::memcpy(&result, &number, sizeof(result));
    return result;
}

void appendPose(std::vector<PxU32>& out, const PxTransform& pose)
{
    out.push_back(bits(pose.p.x)); out.push_back(bits(pose.p.y));
    out.push_back(bits(pose.p.z)); out.push_back(bits(pose.q.x));
    out.push_back(bits(pose.q.y)); out.push_back(bits(pose.q.z));
    out.push_back(bits(pose.q.w));
}

void appendVector(std::vector<PxU32>& out, const PxVec3& v)
{
    out.push_back(bits(v.x)); out.push_back(bits(v.y)); out.push_back(bits(v.z));
}

PxU32 shapeOrdinal(PxActor& actor, const PxShape& shape)
{
    PxRigidActor& rigid = static_cast<PxRigidActor&>(actor);
    std::vector<PxShape*> shapes(rigid.getNbShapes());
    if (rigid.getShapes(shapes.data(), static_cast<PxU32>(shapes.size())) !=
        shapes.size()) fail("query shape enumeration changed");
    for (PxU32 i = 0; i < shapes.size(); ++i)
        if (shapes[i] == &shape) return i;
    fail("query hit shape is outside its actor");
    return PX_INVALID_U32;
}

PxU32 queryScalar(const oc2::offline::QueryImage& image, const char* name)
{
    for (const auto& field : image.fields)
        if (field.name == name)
        {
            if (field.bytes.size() != sizeof(PxU32))
                fail(std::string("query field size changed: ") + name);
            PxU32 value = 0;
            std::memcpy(&value, field.bytes.data(), sizeof(value));
            return value;
        }
    fail(std::string("query field missing: ") + name);
    return 0;
}

struct PublicFrame
{
    std::vector<PxU32> bodies;
    std::vector<PxU32> queries;
    std::vector<Event> events;
    bool operator==(const PublicFrame& b) const {
        return bodies == b.bodies && queries == b.queries &&
               events == b.events;
    }
};

struct PrivateFrame
{
    PxU32 timestamp = 0;
    PxU32 shapeCursor = 0, rigidCursor = 0;
    std::vector<PxU32> shapeFree, rigidFree;
    PxU32 staticQueryTimestamp = 0, dynamicQueryTimestamp = 0;
    PxU32 staticFirstFresh = 0, dynamicFirstFresh = 0;
    PxU32 staticFreeHead = 0, dynamicFreeHead = 0;
    std::vector<PxU32> actorOrder;
    std::vector<PxU32> actorAndShapeIds;
    GraphImage graph;
    bool operator==(const PrivateFrame& b) const {
        return timestamp == b.timestamp &&
            shapeCursor == b.shapeCursor && rigidCursor == b.rigidCursor &&
            shapeFree == b.shapeFree && rigidFree == b.rigidFree &&
            staticQueryTimestamp == b.staticQueryTimestamp &&
            dynamicQueryTimestamp == b.dynamicQueryTimestamp &&
            staticFirstFresh == b.staticFirstFresh &&
            dynamicFirstFresh == b.dynamicFirstFresh &&
            staticFreeHead == b.staticFreeHead &&
            dynamicFreeHead == b.dynamicFreeHead &&
            actorOrder == b.actorOrder &&
            actorAndShapeIds == b.actorAndShapeIds && graph == b.graph;
    }
};

struct Frame { PublicFrame pub; PrivateFrame native; };

std::vector<PxU32> publicBodies(World& world)
{
    std::vector<PxU32> result;
    for (PxRigidActor* actor : world.actors)
    {
        result.push_back(id(actor));
        appendPose(result, actor->getGlobalPose());
        if (actor->getType() == PxActorType::eRIGID_DYNAMIC)
        {
            PxRigidDynamic& body = *static_cast<PxRigidDynamic*>(actor);
            appendVector(result, body.getLinearVelocity());
            appendVector(result, body.getAngularVelocity());
            result.push_back(bits(body.getWakeCounter()));
            result.push_back(body.isSleeping() ? 1u : 0u);
        }
    }
    return result;
}

Frame observe(World& world, const Snapshot& snapshot)
{
    Frame result;
    result.pub.events = snapshot.events;
    result.pub.bodies = publicBodies(world);

    const PxQueryFilterData allTouches(PxQueryFlag::eSTATIC |
        PxQueryFlag::eDYNAMIC | PxQueryFlag::eNO_BLOCK);
    const PxReal rayX[] = { 0.0f, -2.2f, -3.3f, -4.4f, -6.6f };
    for (PxReal x : rayX)
    {
        PxRaycastHit touches[64];
        PxRaycastBuffer hits(touches, 64);
        const bool any = world.scene->raycast(PxVec3(x, 0.8f, -8.0f),
            PxVec3(0.0f, 0.0f, 1.0f), 20.0f, hits,
            PxHitFlag::eDEFAULT, allTouches);
        result.pub.queries.push_back(any ? 1u : 0u);
        result.pub.queries.push_back(hits.nbTouches);
        for (PxU32 i = 0; i < hits.nbTouches; ++i)
        {
            result.pub.queries.push_back(id(hits.touches[i].actor));
            result.pub.queries.push_back(shapeOrdinal(
                *hits.touches[i].actor, *hits.touches[i].shape));
            result.pub.queries.push_back(bits(hits.touches[i].distance));
        }
    }
    PxOverlapHit touches[64];
    PxOverlapBuffer hits(touches, 64);
    const bool any = world.scene->overlap(PxBoxGeometry(5.0f, 1.0f, 1.5f),
        PxTransform(PxVec3(-2.2f, 0.5f, 0.0f)), hits, allTouches);
    result.pub.queries.push_back(any ? 1u : 0u);
    result.pub.queries.push_back(hits.nbTouches);
    for (PxU32 i = 0; i < hits.nbTouches; ++i)
    {
        result.pub.queries.push_back(id(hits.touches[i].actor));
        result.pub.queries.push_back(shapeOrdinal(
            *hits.touches[i].actor, *hits.touches[i].shape));
    }

    PrivateFrame& native = result.native;
    native.timestamp = world.scene->getTimestamp();
    native.shapeCursor = snapshot.clock.shapeIds.currentId;
    native.rigidCursor = snapshot.clock.rigidIds.currentId;
    native.shapeFree = snapshot.clock.shapeIds.freeIds;
    native.rigidFree = snapshot.clock.rigidIds.freeIds;
    native.staticQueryTimestamp = queryScalar(snapshot.query,
        "manager.staticTimestamp");
    native.dynamicQueryTimestamp = queryScalar(snapshot.query,
        "manager.dynamicTimestamp");
    native.staticFirstFresh = queryScalar(snapshot.query,
        "static.pool.firstFresh");
    native.dynamicFirstFresh = queryScalar(snapshot.query,
        "dynamic.pool.firstFresh");
    native.staticFreeHead = queryScalar(snapshot.query,
        "static.pool.freeHead");
    native.dynamicFreeHead = queryScalar(snapshot.query,
        "dynamic.pool.freeHead");
    native.graph = snapshot.graph;

    const PxActorTypeFlags rigid = PxActorTypeFlag::eRIGID_STATIC |
                                   PxActorTypeFlag::eRIGID_DYNAMIC;
    std::vector<PxActor*> sceneActors(world.scene->getNbActors(rigid));
    if (world.scene->getActors(rigid, sceneActors.data(),
            static_cast<PxU32>(sceneActors.size())) != sceneActors.size())
        fail("native actor enumeration changed");
    for (PxActor* actor : sceneActors)
        native.actorOrder.push_back(id(actor));
    for (PxRigidActor* actor : world.actors)
    {
        Sc::RigidSim* rigidSim = static_cast<Sc::RigidSim*>(scActor(*actor));
        if (!rigidSim) fail("actor lacks Sc rigid simulation");
        native.actorAndShapeIds.push_back(id(actor));
        native.actorAndShapeIds.push_back(rigidSim->getID());
        std::vector<PxShape*> shapes(actor->getNbShapes());
        if (actor->getShapes(shapes.data(),
                static_cast<PxU32>(shapes.size())) != shapes.size())
            fail("native shape enumeration changed");
        for (PxShape* shape : shapes)
        {
            Sc::ShapeCore& core = static_cast<NpShape&>(*shape)
                .getScbShape().getScShape();
            Sc::ShapeSim* sim = nullptr;
            for (Sc::Element* element = rigidSim->getElements_(); element;
                 element = element->mNextInActor)
                if (element->getElementType() == Sc::PX_ELEMENT_TYPE_SHAPE &&
                    &static_cast<Sc::ShapeSim*>(element)->getCore() == &core)
                    sim = static_cast<Sc::ShapeSim*>(element);
            if (!sim) fail("shape lacks Sc simulation object");
            native.actorAndShapeIds.push_back(sim->getID());
            native.actorAndShapeIds.push_back(sim->getTransformCacheID());
            native.actorAndShapeIds.push_back(sim->getAABBMgrHandle());
        }
    }
    return result;
}

struct ScriptStep { bool input; PxReal z; };
const ScriptStep kScript[] = {
    {true, 0.0f}, {false, 0.0f}, {true, 0.5f},
    {false, 0.0f}, {true, 0.0f}, {true, 0.5f},
    {false, 0.0f}, {true, 0.0f}
};

std::vector<Frame> runScript(World& world)
{
    std::vector<Frame> frames;
    for (const ScriptStep& step : kScript)
        frames.push_back(observe(world, step.input ?
            world.step(step.z) : world.advanceWithoutInputs()));
    return frames;
}

void report(const Frame& a, const Frame& b, std::size_t i)
{
    auto firstDifferent = [](const std::vector<PxU32>& x,
                             const std::vector<PxU32>& y) -> std::size_t {
        const std::size_t n = std::min(x.size(), y.size());
        for (std::size_t j = 0; j < n; ++j)
            if (x[j] != y[j]) return j;
        return x.size() == y.size() ? n : n;
    };
    const std::size_t bodyAt = firstDifferent(a.pub.bodies, b.pub.bodies);
    const std::size_t queryAt = firstDifferent(a.pub.queries, b.pub.queries);
    const std::size_t nativeIdsAt = firstDifferent(
        a.native.actorAndShapeIds, b.native.actorAndShapeIds);
    std::vector<Event> sortedA = a.pub.events, sortedB = b.pub.events;
    const auto eventLess = [](const Event& x, const Event& y) {
        if (x.kind != y.kind) return x.kind < y.kind;
        if (x.chef != y.chef) return x.chef < y.chef;
        if (x.fixed != y.fixed) return x.fixed < y.fixed;
        if (x.dynamicShape != y.dynamicShape)
            return x.dynamicShape < y.dynamicShape;
        return x.flags < y.flags;
    };
    std::sort(sortedA.begin(), sortedA.end(), eventLess);
    std::sort(sortedB.begin(), sortedB.end(), eventLess);
    std::cout << "SCENE_RESET frame=" << i
              << " public=" << (a.pub == b.pub ? "equal" : "DIFFERENT")
              << " native=" << (a.native == b.native ? "equal" : "different")
              << " timestamp=" << a.native.timestamp << '/'
              << b.native.timestamp
              << " shape_cursor=" << a.native.shapeCursor << '/'
              << b.native.shapeCursor
              << " rigid_cursor=" << a.native.rigidCursor << '/'
              << b.native.rigidCursor
              << " static_query_stamp=" << a.native.staticQueryTimestamp
              << '/' << b.native.staticQueryTimestamp
              << " static_handle_cursor=" << a.native.staticFirstFresh
              << '/' << b.native.staticFirstFresh
              << " graph=" << (a.native.graph == b.native.graph ?
                              "equal" : "different")
              << " callbacks=" << (a.pub.events == b.pub.events ?
                                  "equal" : "different")
              << " callback_multiset=" << (sortedA == sortedB ?
                                          "equal" : "different")
              << " queries=" << (a.pub.queries == b.pub.queries ?
                                "equal" : "different")
              << " body_first=" << bodyAt
              << " body_size=" << a.pub.bodies.size() << '/'
              << b.pub.bodies.size()
              << " query_first=" << queryAt
              << " query_size=" << a.pub.queries.size() << '/'
              << b.pub.queries.size()
              << " ids_first=" << nativeIdsAt << '\n';
    if (bodyAt < a.pub.bodies.size() && bodyAt < b.pub.bodies.size())
    {
        PxReal x = 0, y = 0;
        std::memcpy(&x, &a.pub.bodies[bodyAt], sizeof(x));
        std::memcpy(&y, &b.pub.bodies[bodyAt], sizeof(y));
        std::cout << "SCENE_RESET_BODY_WORD frame=" << i
                  << " index=" << bodyAt << " float=" << x << '/' << y
                  << " bits=" << a.pub.bodies[bodyAt] << '/'
                  << b.pub.bodies[bodyAt] << '\n';
    }
    if (queryAt < a.pub.queries.size() && queryAt < b.pub.queries.size())
        std::cout << "SCENE_RESET_QUERY_WORD frame=" << i
                  << " index=" << queryAt << " values="
                  << a.pub.queries[queryAt] << '/'
                  << b.pub.queries[queryAt] << '\n';
    if (!(a.pub.events == b.pub.events))
    {
        std::cout << "SCENE_RESET_CALLBACK frame=" << i
                  << " counts=" << a.pub.events.size() << '/'
                  << b.pub.events.size();
        const std::size_t n = std::min(a.pub.events.size(), b.pub.events.size());
        for (std::size_t j = 0; j < n; ++j)
            if (!(a.pub.events[j] == b.pub.events[j]))
            {
                const Event& x = a.pub.events[j], &y = b.pub.events[j];
                std::cout << " first=" << j << " kind=" << x.kind << '/'
                          << y.kind << " chef=" << x.chef << '/' << y.chef
                          << " fixed=" << x.fixed << '/' << y.fixed
                          << " flags=" << x.flags << '/' << y.flags;
                break;
            }
        std::cout << '\n';
    }
}

void check(bool shippedSapOrder, bool reverseRemoval, bool replaceScene)
{
    std::vector<Frame> fresh;
    std::vector<PxU32> freshStart;
    {
        Runtime runtime;
        World world(runtime, shippedSapOrder);
        freshStart = publicBodies(world);
        world.scene->flushQueryUpdates();
        fresh = runScript(world);
        if (runtime.errors.count) fail("fresh path reported a PhysX error");
    }
    {
        Runtime runtime;
        World control(runtime, shippedSapOrder);
        control.scene->flushQueryUpdates();
        const std::vector<Frame> repeated = runScript(control);
        if (fresh.size() != repeated.size()) fail("fresh control length changed");
        for (std::size_t i = 0; i < fresh.size(); ++i)
            if (!(fresh[i].pub == repeated[i].pub))
            {
                report(fresh[i], repeated[i], i);
                fail("fresh control is not publicly deterministic");
            }
        if (runtime.errors.count) fail("fresh control reported a PhysX error");
    }
    std::vector<Frame> reused;
    {
        Runtime runtime;
        World world(runtime, shippedSapOrder);
        const auto starts = captureBodyStarts(world);
        const auto originalObjects = actorShapePointers(world);
        (void)runScript(world); // Populate contact, callback, and query history.
        removeAndReinsert(world, starts, reverseRemoval, replaceScene);
        if (actorShapePointers(world) != originalObjects)
            fail("PxActor/PxShape object identity was not retained");
        const std::vector<PxU32> reusedStart = publicBodies(world);
        if (freshStart != reusedStart)
        {
            const std::size_t n = std::min(freshStart.size(), reusedStart.size());
            for (std::size_t j = 0; j < n; ++j)
                if (freshStart[j] != reusedStart[j])
                {
                    std::cout << "SCENE_RESET_START_DIFF index=" << j
                              << " fresh=" << freshStart[j]
                              << " reused=" << reusedStart[j] << '\n';
                    break;
                }
            fail("public body state differs before replay");
        }
        reused = runScript(world);
        if (runtime.errors.count) fail("reused path reported a PhysX error");
    }
    if (fresh.size() != reused.size()) fail("script length changed");
    bool sawNativeDifference = false, sawPublicDifference = false;
    for (std::size_t i = 0; i < fresh.size(); ++i)
    {
        report(fresh[i], reused[i], i);
        sawNativeDifference |= !(fresh[i].native == reused[i].native);
        sawPublicDifference |= !(fresh[i].pub == reused[i].pub);
    }
    if (!replaceScene && (!sawNativeDifference || !sawPublicDifference))
        fail("same-scene control lost its observed native/public divergence");
    if (replaceScene && (sawNativeDifference || sawPublicDifference))
        fail("new-scene retained-actor control lost normalized replay parity");
    std::cout << "SCENE_RESET_SUMMARY sap_order="
              << (shippedSapOrder ? "shipped_like" : "default")
              << " remove_order=" << (reverseRemoval ? "reverse" : "forward")
              << " scene=" << (replaceScene ? "new" : "same")
              << " native_history_diff=" << (sawNativeDifference ? 1 : 0)
              << " public_diff="
              << (sawPublicDifference ? 1 : 0) << '\n';
}

} // namespace

int main()
{
    check(false, false, false);
    check(false, true, false);
    check(true, false, false);
    check(true, true, false);
    check(false, false, true);
    check(false, true, true);
    check(true, false, true);
    check(true, true, true);
    std::cout << "PASS source-built remove/flush/reinsert differential completed\n";
    return 0;
}
