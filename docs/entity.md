## The Entity System

### Basics of Multiplayer
The game is written with multiplayer in mind, and even single player gameplay uses the same stack as the
multiplayer scenario, just with a single client.

The host of a game is always the server, and all players in the game (including the host) are clients.

Generally speaking, the clients are only responsible for rendering and input, whereas the server is
responsible for the game logic. For example, when a player grabs an tomato from a crate, its client
sends a message to the server that "I will pickup something from the crate", and the server decides
to spawn a tomato and put it in the chef's hands, and synchronizes that information to the clients.
Even if the player is the host, it goes through the same code path, except the networking part just
calls local code.

The physics simulation is somewhat of an exception. Both the server and the client run physics simulation.
I don't yet understand how that works.

### Basics of the entity system
Each object during the gameplay that requires multiplayer synchronization has an entity ID. The entity IDs
are agreed upon at all times between the server and the clients. The bidirectional mapping between entity IDs
and their corresponding GameObjects are kept by `EntitySerialisationRegistry` as static fields. The registry
also keeps track of some other properties.
  * The staticness of the fields are relied upon by the deserialization code. When receiving a message over
    the network references an object by its the entity ID, the message is deserialized directly into a
    corresponding GameObject, by accessing `EntitySerialisationRegistry` statically. This is a questionable
    design decision, but in the face of Unity (which uses static objects all over the place), it doesn't
    stand out.

### Synchronizing Entities

Initially, when the level is first launched, the starting objects (such as counters, mixers, plates, chefs,
etc.) are assigned entity IDs by iterating through the objects:
  * See `EntitySerialisationRegistry.SetupSynchronisation` which then calls `LinkAllEntitiesToSynchronisationScripts()`.
  * This assignment is done independently by both the server and all the (non-server) clients, and the
    agreement of the IDs depends on the iteration order of the game objects in the scene. This is presumably
    deterministic.
  * The initial state of all the initial objects are not synchronized over the network; rather, the server
    and clients just assume they all have identical contents in the Unity scene.

Each synchronized entity (i.e. GameObject) can contain multiple components, each of which may provide its own
synchronization logic:
  * Each component has a base type, a server synchronizer, and a client synchronizer.
  * For example, a cutting board typically contains components like `Workstation` and `AttachStation`. The
    `Workstation` component corresponds to the functionality of a cutting board, whereas `AttachStation`
    provides the ability to place anything on the counter (such as a plate). The two components interact: if
    the attached object is a `WorkableItem`, then the `Workstation` may be used to "work on" the item, which
    is the game's way of saying, chopping it.
  * In this example, when `SetupSynchronisation` is called, the server will attach `ServerWorkstation` and
    `ServerAttachStation` to the same GameObject. A client (including the host's) will attach
    `ClientWorkstation` and `ClientAttachStation`.
  * The pair of `ServerWorkstation` and `ClientWorkstation` work with each other to synchronize the state for
    just that aspect of the cutting board object.
  * The synchronization of each type of component uses a different entity message. For `Workstation` the
    message type is `WorkstationMessage`. The message contains all states represented by this component.

### Spawning an entity
An entity is spawned by specifying a spawner entity and the prefab to be spawned.
* On the server side this is done via `NetworkUtils.ServerSpawnPrefab(GameObject spawner, GameObject prefab)`.
  * For example, `ServerPlateReturnStation.CreateStack` calls
    `NetworkUtils.ServerSpawnPrefab(this.gameObject, this.m_returnStation.m_stackPrefab)`, the latter being
    the prefab for a dirty plate stack.
* On initialization, any object that can spawn something calls `NetworkUtils.RegisterSpawnablePrefab`. This
  will add a `SpawnableEntityCollection` onto the GameObject if it's not already there, and add the prefab
  to the list of spawnables. Inside `NetworkUtils.ServerSpawnPrefab`, the `SpawnableEntityCollection` is
  requested from the spawner GameObject via the interface `INetworkEntitySpawner` (whose only implementation
  is `SpawnableEntityCollection`).
* When calling `NetworkUtils.ServerSpawnPrefab`, the spawned object is automatically registered as a new
  entity in the `EntitySerialisationRegistry`, and it automatically calls
  `EntitySerialisationRegistry.StartSynchronisingEntry` on the resulting GameObject.
  * This happens before calling `ServerMessenger.SpawnEntity` to notify clients about the spawning of the
    entity. This implies that StartSynchronisingEntry should not *immediately* emit any synchronization messages,
    or else the message will just be dropped at the client side.
  * The function also checks if the spawned object is a `PhysicalAttachment`, which generally means something
    that has a container object that has a `RigidBody`. If so, it additionally registers the container object
    as a separate entity, and instead of calling `ServerMessenger.SpawnEntity`, calls
    `ServerMessenger.SpawnPhysicalAttachment` instead - this is a separate network message type. This message
    includes both entity IDs.
    * See the client side at `ClientSynchronisationReceiver.OnSpawnPhysicalAttachmentMessageReceived`.

### Messaging and Serialization
All the message types and entity message types are registered in `MultiplayerController.Awake`.

### Physics Synchronization
TODO: Understand how this works. Relevant code is in `ServerPhysicsObjectSynchroniser`.