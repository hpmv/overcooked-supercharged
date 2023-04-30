namespace csharp Hpmv
namespace netstd Hpmv

struct Point {
    1: double x,
    2: double y,
    3: double z,
}

struct PadDirection {
    1: double x,
    2: double y,
}

struct OneInputData {
    1: PadDirection pad,
    2: bool pickDown,
    3: bool chopDown,
    4: bool dashDown,
    5: bool throwDown,
    6: bool throwUp,
}

struct CharPositionData {
    1: Point forwardDirection,
    3: optional i32 attachment,
    4: optional i32 highlightedForPickup,
    5: optional i32 highlightedForUse,
    6: optional i32 highlightedForPlacement,
    7: double dashTimer,
}

struct ItemData {
    1: optional Point pos,
    2: map<i32, binary> data,
    3: optional Point velocity,
    4: optional Point forward,
    5: optional Point up,
}

struct EntityRegistryData {
    1: i32 entityId,
    2: string name,
    3: Point pos,
    4: list<string> components,
    5: list<i32> syncEntityTypes,
    6: list<string> spawnNames,
}

struct ServerMessage {
    1: i32 type,
    2: binary message,
}

struct CoroutineEvent {
    1: i32 coroutineId,
    2: optional double totalTime,
    3: optional bool endEvent,
    4: optional i32 plateRespawnEntityId,
}

struct OutputData {
    1: map<i32, CharPositionData> charPos,
    2: map<i32, ItemData> items,
    3: list<ServerMessage> serverMessages,
    4: list<EntityRegistryData> entityRegistry,
    5: list<CoroutineEvent> coroutineEvents,
    6: bool lastFramePaused,
    7: bool nextFramePaused,
}

struct EventSetItemPosition {
    1: i32 entityId,
    2: ItemData data,
}

struct SetCoroutineTimer {
    1: i32 coroutineId,
    2: double remainingTime,
}

struct SetDashTimer {
    1: i32 chefEntityId,
    2: double dashTimer,
}

struct WarpEvent {
    1: optional EventSetItemPosition setItemPosition,
    2: optional SetCoroutineTimer setCoroutineTimer,
    3: optional SetDashTimer setDashTimer,
    4: optional ServerMessage entityMessage,
}

struct WarpSpec {
    1: list<WarpEvent> events,
    2: i32 frame,
    3: double remainingTime,
}

struct InputData {
    1: map<i32, OneInputData> input,
    2: i32 resetOrderSeed,
    3: optional double gameSpeed,
    4: optional WarpSpec warp,
    5: optional bool requestPause,
    6: optional bool requestResume,
}

service Interceptor {
    InputData getNext(1: OutputData output),
}
