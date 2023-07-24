## Components that Require Special Warping Logic

* [Done] Cannon: ServerCannonMod and related classes.
* [Done] Chef (`ClientPlayerControlsImpl_Default`): Handled by ChefSpecificData and custom warp logic. See controls.md.
* [Done] WorkableItem progress. (simple)
* [Done] Workstation. m_interacters store the chefs chopping. Not sure if there's state to restore on the chef side.
* [Done] PilotRotation: should be a simple angle parameter. Note that PilotRotation extends PilotMovement; the latter is more complicated because it involves velocity. PilotRotation however does not seem to have an (angular) velocity involved. Making the angle actually trigger object rotation is done at the ClientPilotRotation side.
* [Done] Terminal: needs to be overridden like Cannon because it's currently a SessionInteractable, but this is much simpler than Cannon. It directly calls into ServerPilotMovement. I suppose AssignPlayer should be taken care of like OnLoad and OnExit like in Cannon. (It can also be handled by PilotRotation warping but I think that's less natural).
* [Done] Item throwing. See [throwing.md](throwing.md).
* [Done] CookingHandler progress. (simple)
* [Done] MixingHandler progress. (simple)
* [Done] IngredientContainer contents. This should be simply sending over serialized AssembledDefinitionNode[].
* [Done] AttachStation attachment and detachment. This is subtle, needs to be done carefully.
* [Done] PickupItemSwitcher, TriggerColourCycle - both are simple, just need to update an index and send a server message.
* [Done] PlacementItemSwitcher - condiment switching, need to store which condiment is the one used. (very simple)
* [Done] PlateStation (serving). This was resolved by stopping the plate entity synchronization as soon as it enters the delivery sequence coroutine, and notifying the controller via a new EntityRetirementMessage.
* [Done] Also, dirty plate spawning needs to be managed.
* [Done] Plate stack warping. See stacks.md.
* [Done] WashingStation progress and plate count (simple).
* [Done] ServerKitchenFlowController - all the parameters for orders, score.
* [Done] Game timer.

Deferred:
* [TODO] Teleportation. Seems like two manual animations (math lerps) being coded into some coroutines. Should be able to unroll all of these without too much difficulty.

Bugs:
* [TODO] Sometimes after restarting the level the UI stops updating.