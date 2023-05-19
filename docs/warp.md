## Components that Require Special Warping Logic

* [Done] Cannon: ServerCannonMod and related classes.
* [Done] Chef (`ClientPlayerControlsImpl_Default`): Handled by ChefSpecificData and custom warp logic. See controls.md.
* [TODO] PilotRotation: should be a simple angle parameter. Note that PilotRotation extends PilotMovement; the latter is more complicated because it involves velocity. PilotRotation however does not seem to have an (angular) velocity involved. Making the angle actually trigger object rotation is done at the ClientPilotRotation side.
* [TODO] Terminal: needs to be overridden like Cannon because it's currently a SessionInteractable, but this is much simpler than Cannon. It directly calls into ServerPilotMovement. I suppose AssignPlayer should be taken care of like OnLoad and OnExit like in Cannon. (It can also be handled by PilotRotation warping but I think that's less natural).
* [TODO] WorkableItem progress. (simple)
* [TODO] CookingHandler progress. (simple)
* [TODO] IngredientContainer contents. This should be simply sending over serialized AssembledDefinitionNode[].
* [TODO] Teleportation. Seems like two manual animations (math lerps) being coded into some coroutines. Should be able to unroll all of these without too much difficulty.
* [TODO] AttachStation attachment and detachment. This is subtle, needs to be done carefully.
* [TODO] PlateStation (serving). This needs some coroutine hacks. Also, dirty plate spawning needs to be managed.
* [TODO] WashingStation progress and plate count (simple).
* [TODO] Item throwing. Items need to be in the right state when flying. It might be more complicated than it initially looks because it needs to ignore certain collisions?
* [TODO] PlacementItemSwitcher - condiment switching, need to store which condiment is the one used.
* [TODO] Workstation. m_interacters store the chefs chopping. Not sure if there's state to restore on the chef side.
