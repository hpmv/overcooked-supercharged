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

struct Quaternion {
    1: double x,
    2: double y,
    3: double z,
    4: double w,
}

struct EntityPathReference {
    1: list<i32> ids,
}

struct ButtonInput {
    1: bool down,
    2: bool justPressed,
    3: bool justReleased,
}

struct OneInputData {
    1: PadDirection pad,
    7: ButtonInput pickup,
    8: ButtonInput interact,
    9: ButtonInput dash,
}

struct ChefSpecificData {
    4: i32 highlightedForPickup,
    5: i32 highlightedForUse,
    6: i32 highlightedForPlacement,
    7: double dashTimer,
    8: i32 interactingEntity,
    9: Point lastVelocity,
    10: bool aimingThrow,
    11: bool movementInputSuppressed,
    12: Point lastMoveInputDirection,
    13: double impactStartTime,
    14: double impactTimer,
    15: Point impactVelocity,
    16: double leftOverTime,
}

struct ItemData {
    1: optional Point pos,
    // 2: map<i32, binary> data,
    4: optional Quaternion rotation,
    3: optional Point velocity,
    6: optional Point angularVelocity,
    // EntityReference passed in via EntityWarpSpec.
    5: optional EntityPathReference entityPathReference,
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
    1: map<i32, ChefSpecificData> chefs,
    2: map<i32, ItemData> items,
    3: list<ServerMessage> serverMessages,
    4: list<EntityRegistryData> entityRegistry,
    6: bool lastFramePaused,
    7: bool nextFramePaused,
    8: i32 frameNumber,
}

struct EntityIdOrRef {
    1: optional i32 entityId,
    2: optional EntityPathReference entityPathReference,
}

struct PlatePendingReturnWarpData {
    1: i32 returnStationEntityId,
    2: double timer,
    3: i32 platingStepData,
}

struct PlateStationWarpData {
    1: list<PlatePendingReturnWarpData> plates,
}

struct IngredientContainerWarpData {
    1: binary msgData,  // encoded IngredientContainerMessage
}

struct CannonWarpData {
    1: binary modData,  // encoded CannonModMessage
}

struct WorkstationInteracterWarpData {
    1: i32 chefEntityId,
    4: double actionTimer,
}

struct WorkstationWarpData {
    1: list<WorkstationInteracterWarpData> interacters,
    2: optional EntityIdOrRef item,
}

struct WorkableItemWarpData {
    1: i32 progress,
    2: i32 subProgress,
    3: bool onWorkstation,
}

struct ColliderRef {
    1: EntityIdOrRef entity,
    2: i32 colliderIndex,
}

struct ThrowableItemWarpData {
    1: bool isFlying,
    2: double flightTimer,
    3: i32 throwerEntityId,
    4: list<ColliderRef> throwStartColliders,
}

struct TerminalWarpData {
    1: optional i32 interacterEntityId,
}

struct PilotRotationWarpData {
    1: double angle,
}

struct MixingHandlerWarpData {
    1: double progress,
}

struct CookingHandlerWarpData {
    1: double progress,
}

struct ChefCarryWarpData {
    1: optional EntityIdOrRef carriedItem,
}

struct AttachStationWarpData {
    1: optional EntityIdOrRef item,
}

struct EntityWarpSpec {
    // Either entityId or spawningPath is specified. If the former, the entity
    // already exists in the game; if the latter, the entity is to be created
    // using the spawningPath, whose first element is the source entity ID, and
    // subsequent elements are the spawnable object indices.
    1: optional i32 entityId,
    2: list<i32> spawningPath,

    // If this is specified, the reference should be attached to the GameObject
    // so that the controller may identify it.
    3: optional EntityPathReference entityPathReference,

    // Physics data.
    4: optional Point position,
    5: optional Quaternion rotation,
    6: optional Point velocity,
    11: optional Point angularVelocity,

    // Zero or more of the following can be present.
    8: optional PlateStationWarpData plateStation,
    9: optional ChefSpecificData chef,
    10: optional IngredientContainerWarpData ingredientContainer,
    12: optional CannonWarpData cannon,
    13: optional WorkstationWarpData workstation,
    14: optional WorkableItemWarpData workableItem,
    15: optional ThrowableItemWarpData throwableItem,
    16: optional TerminalWarpData terminal,
    17: optional PilotRotationWarpData pilotRotation,
    18: optional MixingHandlerWarpData mixingHandler,
    19: optional CookingHandlerWarpData cookingHandler,
    20: optional ChefCarryWarpData chefCarry,
    21: optional AttachStationWarpData attachStation,
    // TODO: more entity types
}

struct WarpSpec {
    1: list<EntityWarpSpec> entities,
    2: list<i32> entitiesToDelete,
    3: i32 frame,
    4: double remainingTime,
    // TODO: animations
}

struct InputData {
    1: map<i32, OneInputData> input,
    2: i32 resetOrderSeed,
    3: optional double gameSpeed,
    4: optional WarpSpec warp,
    5: optional bool requestPause,
    6: optional bool requestResume,
    7: optional i32 nextFrame,
}

service Interceptor {
    InputData getNext(1: OutputData output),
}
