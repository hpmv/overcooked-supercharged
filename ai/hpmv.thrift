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

struct InputData {
    1: map<i32, OneInputData> input,
    2: i32 resetOrderSeed,
    3: optional double gameSpeed,
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
    1: Point pos,
    2: map<i32, binary> data,
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

struct OutputData {
    1: map<i32, CharPositionData> charPos,
    2: map<i32, ItemData> items,
    3: list<ServerMessage> serverMessages,
    4: list<EntityRegistryData> entityRegistry,
}

service Interceptor {
    InputData getNext(1: OutputData output)
}
