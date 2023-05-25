## Throwing Basics

Throwing is pretty non-trivial in this game.

* `AttachmentThrower` is a component that must be present for anything that can throw an object. There are two
  I'm aware of: chef and teleportal. The `ServerAttachmentThrower::ThrowItem` is the one performing the throwing
  action, but throwing is triggered via a few steps, starting elsewhere:
  * For chef, the `ClientPlayerControlsImpl_Default` listens for controller events in `Update_Throw()` and then
    *requests* a throw by sending an `ChefEventType.Throw` event to the server. See Chef Throwing later
  * For teleportal, the `ServerTeleportalAttachmentReceiver` has a coroutine that initiates the throw when
    receiving a teleported throwable object.
* `ServerThrowableItem` is a component that is present on anything that can be thrown. Its two important methods
  are `CanHandleThrow` and `HandleThrow`:
  * `CanHandleThrow` makes an object conditionally throwable. (Anything that is never throwable just doesn't have
    the `ThrowableItem` component attached to it at all.) It has only one implementation, by
    `ServerPreparationContainer` (used by burger buns, tortillas, etc.) which is only throwable if it doesn't
    have anything inside it.
  * `HandleThrow` does a few things. See later in Throwing Details.

## Chef Throwing

After `ClientPlayerControlsImpl_Default.Update_Throw` decides to send a throw event, the server counterpart, `ServerPlayerControlsImpl_Default.ReceiveThrowEvent(GameObject _target)` processes the throwing.

```cs
public void ReceiveThrowEvent(GameObject _target)
{
    if (_target != null)
    {
        if (this.m_iCarrier.InspectCarriedItem() == null || PlayerControlsHelper.IsHeldItemInsideStaticCollision(this.m_controls))
        {
            return;
        }
        IThrowable throwable = _target.RequireInterface<IThrowable>();
        Vector3 v = this.m_controlObject.transform.forward.normalized.XZ();
        if (throwable.CanHandleThrow(this.m_iThrower, v))
        {
            this.m_iCarrier.TakeItem();
            throwable.HandleThrow(this.m_iThrower, v);
            EntitySerialisationEntry entry = EntitySerialisationRegistry.GetEntry(_target);
            this.SendServerEvent(new InputEventMessage(InputEventMessage.InputEventType.EndThrow)
            {
                entityId = entry.m_Header.m_uEntityID
            });
        }
        else
        {
            this.m_iThrower.OnFailedToThrowItem(this.m_iCarrier.InspectCarriedItem());
        }
    }
}
```

First, `this.m_iCarrier` is the `ICarrier` component on the same chef; `.InspectCarriedItem()` returns the item
currently being carried by the chef. It's it's null then we bail.

Then, we check if the item is inside static collision:

```cs
public static bool IsHeldItemInsideStaticCollision(PlayerControls _control)
{
    if (PlayerControlsHelper.s_staticCollisionLayerMask == 0)
    {
        PlayerControlsHelper.s_staticCollisionLayerMask = LayerMask.GetMask(new string[]
        {
            "Default",
            "Ground",
            "Walls",
            "Worktops",
            "PlateStationBlock",
            "CookingStationBlock"
        });
    }
    ICarrier carrier = _control.gameObject.RequireInterface<ICarrier>();
    IAttachment attachment = carrier.InspectCarriedItem().RequestInterface<IAttachment>();
    Collider collider = attachment.AccessGameObject().RequestComponent<Collider>();
    Bounds bounds = collider.bounds;
    Quaternion rotation = collider.transform.rotation;
    Vector3 position = collider.transform.position;
    Vector3 halfExtents = bounds.extents * 0.5f;
    int num = Physics.OverlapBoxNonAlloc(bounds.center, halfExtents, PlayerControlsHelper.s_collisions, rotation, PlayerControlsHelper.s_staticCollisionLayerMask, QueryTriggerInteraction.Ignore);
    return num > 0;
}
```

This corresponds to the behavior that if, say, a cooking station is right in the chef's face, the chef cannot throw anything. Similarly one cannot throw something into the wall.

After that, we check for `CanHandleThrow`, which we discussed above. If this fails, we call `OnFailedToThrowItem`, whose only implementation by `ServerAttachmentThrower` does nothing, so we can ignore this.

Next, we `TakeItem()` from the chef, which detaches the item from the chef.

Then we call `ServerThrowableItem.HandleThrow`. Before we dig into it, we look at the final thing that happens after: sending a EndThrow message to the clients. This does nothing important except triggering a sound effect on the clients as well as some chef animations.

## Throwing Details

Here's the function that performs the throwing: `ServerThrowableItem.HandleThrow`:

```cs
public void HandleThrow(IThrower _thrower, Vector2 _directionXZ)
{
    _thrower.ThrowItem(base.gameObject, _directionXZ);
    this.m_thrower = _thrower;
    this.m_previousThrower = _thrower;
    if (this.m_attachment == null)
    {
        this.m_attachment = base.gameObject.RequestInterface<IAttachment>();
    }
    if (this.m_attachment != null)
    {
        this.m_attachment.RegisterAttachChangedCallback(this.m_OnAttachChanged);
    }
    if (this.m_pickupReferral != null)
    {
        this.m_pickupReferral.SetHandlePickupReferree(this);
    }
    this.m_flying = true;
    this.SendStateMessage(this.m_flying, ((MonoBehaviour)_thrower).gameObject);
    this.ResumeAndClearThrowStartCollisions();
    this.m_ignoredCollidersCount = Physics.OverlapBoxNonAlloc(this.m_Transform.position, this.m_Collider.bounds.extents, this.m_ThrowStartColliders, this.m_Transform.rotation, ServerThrowableItem.ms_AttachmentsLayer);
    for (int i = 0; i < this.m_ignoredCollidersCount; i++)
    {
        Physics.IgnoreCollision(this.m_Collider, this.m_ThrowStartColliders[i], true);
    }
}
```

The first call, `ThrowItem`, is implemented by `ServerAttachmentThrower`:

```cs
public void ThrowItem(GameObject _object, Vector2 _directionXZ)
{
    IAttachment attachment = _object.RequestInterface<IAttachment>();
    if (attachment != null)
    {
        Vector3 velocity = this.CalculateThrowVelocity(_directionXZ);
        attachment.AccessMotion().SetVelocity(velocity);
        ICatchable catchable = _object.RequestInterface<ICatchable>();
        if (catchable != null)
        {
            this.AlertPotentialCatchers(catchable, attachment.AccessGameObject().transform.position, _directionXZ);
        }
    }
    this.m_throwCallback(_object);
}
```

It sets a velocity on the attachment (which basically forwards the velocity to the Rigidbody container),
and then gets the `ICatchable` side of the throwable and calls `AlertPotentialCatchers`. The final m_throwCallback is always a no-op (nobody registers any logic on it).

```cs
private void AlertPotentialCatchers(ICatchable _object, Vector3 _position, Vector2 _directionXZ)
{
    Vector3 direction = VectorUtils.FromXZ(_directionXZ, 0f);
    int num = Physics.SphereCastNonAlloc(_position, 1f, direction, ServerAttachmentThrower.ms_raycastHits, 10f, ServerAttachmentThrower.m_playersLayerMask);
    for (int i = 0; i < num; i++)
    {
        RaycastHit raycastHit = ServerAttachmentThrower.ms_raycastHits[i];
        GameObject gameObject = raycastHit.collider.gameObject;
        if (!(gameObject == base.gameObject))
        {
            IHandleCatch handleCatch = gameObject.RequestInterface<IHandleCatch>();
            if (handleCatch != null)
            {
                handleCatch.AlertToThrownItem(_object, this, _directionXZ);
            }
        }
    }
}
```

This queries the physics system for anything that intersects a radius-1 ray in the thrown direction. For any such
objects that isn't the thrower itself, and implements `IHandleCatch`, it calls `AlertToThrownItem`.

Of the five implementations of `IHandleCatch`, only one has non-empty logic: `ServerAttachmentCatcher.AlertToThrownItem`, i.e. another potentially catching chef:

```cs
public void AlertToThrownItem(ICatchable _thrown, IThrower _thrower, Vector2 _directionXZ)
{
    if (this.m_controls != null && this.m_controls.GetDirectlyUnderPlayerControl())
    {
        return;
    }
    if (this.m_trackedThrowable != null)
    {
        IThrowable throwable = this.m_trackedThrowable.RequireInterface<IThrowable>();
        if (throwable.IsFlying())
        {
            return;
        }
    }
    if (this.m_carrier.InspectCarriedItem() == null && this.m_controls.GetCurrentlyInteracting() == null)
    {
        this.m_trackedThrowable = _thrown.AccessGameObject();
        this.SendTrackingData(this.m_trackedThrowable);
    }
}
```

If the potential catching chef is under a player's control (e.g. not in a cannon, not piloting a raft, not being the non-active chef of single player), then there's nothing to do.

If the previously tracked throwable is still flying, there's also nothing to do.

Otherwise, if the chef is not currently carrying something, and it's not currently doing something (e.g. chopping), start tracking this throwable and sends the same to the client side that also sets its `m_trackedThrowable`. This is then used in `Update_Rotation` to automatically face the thrown object. In other words, `AlertPotentialCatchers` is only for the purpose of non-active player rotation in single player.

Let's come back to `HandleThrow`. The next thing that happens is setting `m_thrower` and `m_previousThrower`:

* `m_thrower` is used for three purposes:
  * It is used to prevent in-flight collision with the thrower itself, in
    `ServerThrowableItem.OnCollisionEnter`.
  * It is used in `ServerAttachmentThrower.CanHandleCatch` to prevent catching an object thrown by the
    same chef.
  * It is used in `ServerHeatedStation.HandleCatch` and `ServerIngredientCatcher.HandleCatch` for
    achievement tracking.
* `m_previousThrower` is used for `ServerRubbishBin` for achievement tracking only.

Next, it lazily fetches `IAttachment` for the item itself (only implemented by `ServerPhysicalAttachment`),
and then registers a callback to call `OnAttachChanged` which basically forcefully ends the flight if
someone else claims attachment.

Next, it sets the pickup referree of the item to itself, whose `CanHandlePickup` function returns whether
the object is flying. (I doubt this would become important without some bad network latency, such as
someone picking up an object and before the message arrives, someone else already threw the object).

Next it sets `m_flying`. This field is used in a few places:

* It prevents pickup in its `CanHandlePickup` implementation.
* `OnCollisionEnter` does nothing if the object is not flying.
* It is used to update flight time to detect landing.
* It is used by `ServerAttachmentCatcher`'s throwable tracking to detect if the object is still flying.
* It is used by `ServerAttachStation.CanHandleCatch` to only catch an object if it's NOT flying (TODO:
  this is cryptic).
* It is used by `ServerCatchableItem.AllowCatch` to return false if the object is not flying or has only
  been flying for a short time.
* It is added to the object's `ServerLimitedQuantityItem` as an invincibility condition if the object is
  flying.

Next, it sends a message that the object is flying. On the client side (`ClientThrowableItem`), this
triggers `StartFlight`:

```cs
private void StartFlight(IClientThrower _thrower)
{
    this.m_isFlying = true;
    this.m_thrower = _thrower;
    if (this.m_pickupReferral != null)
    {
        this.m_pickupReferral.SetHandlePickupReferree(this);
    }
    if (this.m_throwableItem.m_throwParticle != null)
    {
        Transform parent = NetworkUtils.FindVisualRoot(base.gameObject);
        GameObject gameObject = this.m_throwableItem.m_throwParticle.InstantiateOnParent(parent, true);
        this.m_pfx = gameObject.GetComponent<ParticleSystem>();
    }
}
```

`m_isFlying` here is used for the similar handle pickup referree's `CanHandlePickup`, as well as
`ClientPlayerControlsImpl_Default.OnThrowableCollision`, which applies knockback if an object is thrown
onto a chef. Let's not go into that mess here.

After that is a similar handle pickup referree, and particle effects.

Going back to server's `HandleThrow`. Next is `ResumeAndClearThrowStartCollisions()`:

```cs
private void ResumeAndClearThrowStartCollisions()
{
    for (int i = 0; i < this.m_ignoredCollidersCount; i++)
    {
        Collider collider = this.m_ThrowStartColliders[i];
        if (collider != null && collider.gameObject != null)
        {
            Physics.IgnoreCollision(this.m_Collider, collider, false);
            this.m_ThrowStartColliders[i] = null;
        }
    }
    this.m_ignoredCollidersCount = 0;
}
```

this is to clear the previously ignored collisions, if any somehow remains (because the same thing
is done in `EndFlight`).

Finally, we check for any initially colliding items (within the "Attachments" layer) with the thrown
item, and ignore collisions for all of them. This allows us to throw an object over another object
(such as if there's a strawberry on the counter right in front of us we can still throw another
strawberry over it).

## During Flight

`ServerThrowableItem.UpdateSynchronising` does this every frame:
```cs
public override void UpdateSynchronising()
{
    float deltaTime = TimeManager.GetDeltaTime(base.gameObject);
    if (this.IsFlying())
    {
        this.m_flightTimer += deltaTime;
        if (this.m_flightTimer >= 0.1f)
        {
            IAttachment component = base.GetComponent<IAttachment>();
            if (component.AccessMotion().GetVelocity().sqrMagnitude < 0.0225f)
            {
                this.EndFlight();
            }
            if (this.m_flightTimer >= 5f)
            {
                this.EndFlight();
            }
            this.ResumeAndClearThrowStartCollisions();
        }
    }
    else
    {
        this.m_throwerTimer -= deltaTime;
        if (this.m_throwerTimer <= 0f)
        {
            this.m_previousThrower = null;
        }
    }
}
```
This essentially says that:

* End the flight if we have been flying for more than 5 seconds, or for at least 0.1 second and the velocity
  is below 0.15.
* If we've been flying for at least 0.1 second, regardless of these conditions, call `ResumeAndClearThrowStartCollisions()`, i.e. stop ignoring the initial collisions.
  * According to the game data, the initial throwing velocity is 18. In 0.1 second the object should have
    travelled for 1.8, which is longer than the diagonal of a grid cell (1.2 * sqrt(2)). So definitely we
    would have cleared any obstacles in this time.
* If not flying, then the `m_previousThrower` field times out after a timeout. This is unimportant; we've
  seen earlier that this field is only used for tracking rubbish bin achievement.

In addition, there's the `ServerThrowableItem.OnCollisionEnter`, which is a Unity lifecycle event when
the object collides with something:

```cs
private void OnCollisionEnter(Collision _collision)
{
    if (!this.IsFlying())
    {
        return;
    }
    IThrower thrower = _collision.gameObject.RequestInterface<IThrower>();
    if (thrower != null && thrower == this.m_thrower)
    {
        return;
    }
    Vector3 normal = _collision.contacts[0].normal;
    float num = Vector3.Angle(normal, Vector3.up);
    if (num <= 45f)
    {
        this.EndFlight();
        this.m_landedCallback(_collision.gameObject);
    }
}
```

* If it's not flying anymore, do nothing.
* If the collision is with the original thrower, do nothing.
* If the collision contact **normal** is less than 45 degrees with the up direction, end flight and trigger landing
  (i.e. the thing is more similar to a ground, not a wall).
  * the m_landedCallback is always no-op. The only registrant of this callback is `ServerSplattable` (which is some
    logic that destroys the throwable if someone throws it in a hazard, like fire); however, `ServerSplattable` is
    not attached to `Splattable` objects (it's missing in `MultiplayerController`), so it's never instantiated.

## Landing or Ending Flight

Flight is ended by any of the following:

* Colliding with an eligible object at an eligible angle in `ServerThrowableItem.OnCollisionEnter`.
* Reaching a low speed or exceeding flight time in `ServerThrowableItem.UpdateSynchronising`.
* Being attached to a parent, in `ServerThrowableItem.OnAttachChanged`.

`EndFlight()` is called in any of these cases, which effectively undoes all the steps of `HandleThrow()`,
and additionally the `m_previousThrower` and `m_throwerTimer` is set. The former is used for achievement
tracking only, and the latter is just a timeout to set the former to null.

```cs
public void EndFlight()
{
    if (this.m_pickupReferral != null && this.m_pickupReferral.GetHandlePickupReferree() == this)
    {
        this.m_pickupReferral.SetHandlePickupReferree(null);
    }
    if (this.m_attachment != null)
    {
        this.m_attachment.UnregisterAttachChangedCallback(this.m_OnAttachChanged);
    }
    this.m_previousThrower = this.m_thrower;
    this.m_throwerTimer = this.m_throwableItem.m_throwerTimeout;
    this.m_thrower = null;
    this.m_flightTimer = 0f;
    this.m_flying = false;
    this.SendStateMessage(this.m_flying, null);
    this.ResumeAndClearThrowStartCollisions();
}
```

## Catching

Catching has three aspects: the catchable item, the catcher, and the catching processor.

### Catchable item

On the catchable item side, there's a single interface `ICatchable` whose only implementation is `ServerCatchableItem`.
(The counterpart, `ClientCatchableItem`, is an empty class.)

It is a pretty simple class that contains only one important function (comments inlined):
```cs
public bool AllowCatch(IHandleCatch _catcher, Vector2 _directionXZ)
{
    if (this.m_throwable == null)
    {
        // This is just defensive programming in case the object does not have
        // a IThrowable component.
        return false;
    }
    GameObject gameObject = (_catcher as MonoBehaviour).gameObject;
    if (_catcher != null && gameObject == null)
    {
        // This looks like a very weird condition but Unity Objects have an
        // internal reference that may be null, and the operator == is
        // overloaded to compare equal to null if the internal reference is null.
        // So it's possible for the _catcher (who is an ICatchable, not a
        // UnityEngine.Object) to not be null but its gameObject's operator==
        // to return null. This means the object is already destroyed.
        //
        // Still, this is bad programming because _catcher can't be null anyway
        // or it would trigger an null reference exception above.
        return false;
    }
    if (!(gameObject.RequestComponent<ServerAttachStation>() != null) && !(gameObject.RequestComponent<ServerIngredientContainer>() != null))
    {
        if (!this.m_throwable.IsFlying() || (this.m_throwable.IsFlying() && this.m_throwable.GetFlightTime() < 0.1f))
        {
            return false;
        }
    }
    // The item is catchable (from the item's perspective) if:
    //  - The catcher is a ServerAttachStation; or
    //  - The catcher is a ServerIngredientContainer; or
    //  - The item has been flying for more than 0.1f and it's still flying.
    return true;
}
```

### Catcher

On the catcher side the interface is `IHandleCatch`. This generally queries `AllowCatch` on the
catchable item first, so the catcher can be the sole decider.

There are five implementations of `IHandleCatch`:
* `ServerAttachmentCatcher`: chefs. Note that teleportals don't seem to use this component.
* `ServerAttachStation`: countertops and anything that act like countertops, like chopping boards.
* `ServerHeatedStation`: these are the heating stations in campfire levels where chopped firewood can be thrown in. The component is also used in Surf n' Turf barbeque stations and even coal furnaces but those don't catch anything.
* `ServerIngredientCatcher`: containers that can catch ingredients, e.g. pans, mixers, pots.
* `ServerPlayerAttachmentCarrier.BlockCatching`: this is a special implementation used to block catching when an
  object is being held by a chef.

Let's go through these one by one:

`ServerAttachmentCatcher`:

```cs
public bool CanHandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    if (!_object.AllowCatch(this, _directionXZ))
    {
        return false;
    }
    GameObject gameObject = _object.AccessGameObject();
    if (this.m_carrier.InspectCarriedItem() != null)
    {
        // If chef is already holding something, don't catch.
        return false;
    }
    IThrower thrower = base.gameObject.RequestInterface<IThrower>();
    if (thrower != null)
    {
        IThrowable throwable = gameObject.RequireInterface<IThrowable>();
        if (throwable.GetThrower() == thrower)
        {
            // The chef throwing the object can't catch it (e.g. smashing
            // against a wall.)
            return false;
        }
    }
    // In game data, this is 1.8. Catching will not happen if the item is
    // further away from the chef than 1.8 (1.5 * grid size).
    float catchDistance = this.m_catcher.m_catchDistance;
    if ((base.transform.position - gameObject.transform.position).sqrMagnitude > catchDistance * catchDistance)
    {
        return false;
    }
    // Finally, catching can only happen if the chef is facing less than 120 degrees
    // from the direction the item is flying (horizontal directions only).
    //
    // Note that catching at 120 degrees may seem weird but can happen if someone throws
    // from the side but slightly behind you, towards a spot in front of you. If the
    // thrown object would hit you first, it would knock you to the side instead of
    // triggering a catch.
    IAttachment attachment = gameObject.RequireInterface<IAttachment>();
    Vector2 from = attachment.AccessMotion().GetVelocity().XZ();
    Vector2 a = base.transform.forward.XZ();
    float num = Vector2.Angle(from, -a);
    return num <= this.m_catcher.m_catchAngleMax;
}

public void HandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    GameObject @object = _object.AccessGameObject();
    // Cause the chef to carry the item.
    this.m_carrier.CarryItem(@object);
    // Stop tracking the item (for chef rotation; see the throwing section).
    this.m_trackedThrowable = null;
    this.SendTrackingData(this.m_trackedThrowable);
}
```

`ServerAttachStation`:
```cs
public bool CanHandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    if (!_object.AllowCatch(this, _directionXZ))
    {
        return false;
    }
    if (this.HasItem())
    {
        // If there's something on top of the AttachStation that can also catch, forward the call to that.
        IHandleCatch handleCatch = this.m_item.AccessGameObject().RequestInterface<IHandleCatch>();
        if (handleCatch != null)
        {
            return handleCatch.CanHandleCatch(_object, _directionXZ);
        }
    }
    IThrowable throwable = _object.AccessGameObject().RequestInterface<IThrowable>();
    // Conditions for catching:
    //  - Object is throwable (should always be true for catchable objects).
    //  - The object is NOT flying.
    //  - The station can catch things (this is a setting that the game devs can toggle).
    //  - The object would normally be attachable to the station (this is a complicated query, but
    //    can only be true if the station is empty to begin with. It handles things like, a frier
    //    cannot be attached to a normal heating station).
    return throwable != null && !throwable.IsFlying() && this.m_station.m_canCatch && this.CanAttachToSelf(_object.AccessGameObject(), new PlacementContext(PlacementContext.Source.Game));
}

public void HandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    if (this.HasItem())
    {
        // Forward catching to the catcher on the station if there is one.
        IHandleCatch handleCatch = this.m_item.AccessGameObject().RequestInterface<IHandleCatch>();
        if (handleCatch != null)
        {
            handleCatch.HandleCatch(_object, _directionXZ);
        }
    }
    else
    {
        // Otherwise just simply attach the object.
        this.AttachObject(_object.AccessGameObject(), default(PlacementContext));
    }
}
```

`ServerHeatedStation`:
```cs
public bool CanHandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    if (!_object.AllowCatch(this, _directionXZ))
    {
        return false;
    }
    // Conditions for catching:
    //  - If this is a heat transfer item (i.e. firewood); OR
    //  - If this is normally catchable by the associated attach station.
    IHeatTransferBehaviour heatTransferBehaviour = _object.AccessGameObject().RequestInterface<IHeatTransferBehaviour>();
    return (heatTransferBehaviour != null && heatTransferBehaviour.CanTransferToContainer(this)) || (this.m_attachStation != null && this.m_attachStation.CanHandleCatch(_object, _directionXZ));
}

// This one is pretty straight forward.
public void HandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    GameObject gameObject = _object.AccessGameObject();
    IHeatTransferBehaviour heatTransferBehaviour = gameObject.RequestInterface<IHeatTransferBehaviour>();
    if (heatTransferBehaviour != null)
    {
        IThrowable throwable = gameObject.RequireInterface<IThrowable>();
        IThrower thrower = throwable.GetThrower();
        if (thrower != null)
        {
            this.BurnAchievement((thrower as MonoBehaviour).gameObject, gameObject);
        }
        ICarrier carrier = new ServerHeatedStation.HolderAdapter(gameObject);
        heatTransferBehaviour.TransferToContainer(carrier, this);
        this.SynchroniseItemAdded();
        return;
    }
    if (this.m_attachStation != null && this.m_attachStation.CanHandleCatch(_object, _directionXZ))
    {
        this.m_attachStation.HandleCatch(_object, _directionXZ);
    }
}
```

`ServerIngredientCatcher`:

```cs
public bool CanHandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    if (!_object.AllowCatch(this, _directionXZ))
    {
        return false;
    }
    // If m_requireAttached is configured for this object, only catch if
    // the container is on an attach station. (For example, mixer can't catch
    // food if it's on the floor).
    if (this.m_catcher.m_requireAttached)
    {
        if (this.m_attachment == null)
        {
            return false;
        }
        if (!this.m_attachment.IsAttached())
        {
            return false;
        }
    }
    // This is a complicated query that determines whether the container can
    // accept an ingredient of this type. For example you cannot throw onion into
    // a pan.
    if (!this.m_allowCatchingCallback(_object.AccessGameObject()))
    {
        return false;
    }
    // A further complicated query to make sure the item can be transferred to the container.
    IOrderDefinition orderDefinition = _object.AccessGameObject().RequestInterface<IOrderDefinition>();
    if (orderDefinition != null && orderDefinition.GetOrderComposition().Simpilfy() != AssembledDefinitionNode.NullNode)
    {
        IContainerTransferBehaviour containerTransferBehaviour = _object.AccessGameObject().RequestInterface<IContainerTransferBehaviour>();
        ServerIngredientContainer component = base.gameObject.GetComponent<ServerIngredientContainer>();
        if (containerTransferBehaviour != null && component != null && containerTransferBehaviour.CanTransferToContainer(component))
        {
            return component.CanAddIngredient(orderDefinition.GetOrderComposition());
        }
    }
    return false;
}

public void HandleCatch(ICatchable _object, Vector2 _directionXZ)
{
    GameObject gameObject = _object.AccessGameObject();
    IThrowable throwable = gameObject.RequireInterface<IThrowable>();
    IThrower thrower = throwable.GetThrower();
    if (thrower != null)
    {
        GameObject gameObject2 = (thrower as MonoBehaviour).gameObject;
        ServerMessenger.Achievement(gameObject2, 12, 1);
    }
    IOrderDefinition orderDefinition = gameObject.RequireInterface<IOrderDefinition>();
    if (orderDefinition.GetOrderComposition().Simpilfy() != AssembledDefinitionNode.NullNode)
    {
        IContainerTransferBehaviour containerTransferBehaviour = gameObject.RequireInterface<IContainerTransferBehaviour>();
        ServerIngredientContainer component = base.gameObject.GetComponent<ServerIngredientContainer>();
        if (containerTransferBehaviour != null && component != null && containerTransferBehaviour.CanTransferToContainer(component))
        {
            // Have the item decide how to transfer to the container.
            containerTransferBehaviour.TransferToContainer(null, component, true);
            // Send clients a message about the change of contents.
            component.InformOfInternalChange();
            // Destroy the thrown item.
            gameObject.SetActive(false);
            NetworkUtils.DestroyObject(gameObject);
        }
    }
}
```

Finally, `ServerPlayerAttachmentCarrier.BlockCatching.CanHandleCatch` just returns false.

### Catching processor

Catchable items and catchers both do not actually trigger any catching. They only decide whether something
can be caught and what to do when catching. There appears to be two ways that the catching action is actually
triggered:

* `ServerAttachmentCatchingProxy`. It is automatically added to objects with a `HeatedStation`, `AttachStation`, or
  `IngredientCatcher` component (see `MultiplayerController.AsyncScanEntities`).
  * On every frame, this component will check all its collisions that are IAttachment, and calls AttemptToCatch.
    ```cs
    private void AttemptToCatch(GameObject _object)
	{
		ICatchable catchable = _object.RequestInterface<ICatchable>();
		if (catchable != null)
		{
			IHandleCatch controllingCatchingHandler = this.GetControllingCatchingHandler();
			if (controllingCatchingHandler != null)
			{
				Vector2 directionXZ = (_object.transform.position - base.transform.position).SafeNormalised(base.transform.forward).XZ();
				if (controllingCatchingHandler.CanHandleCatch(catchable, directionXZ))
				{
					controllingCatchingHandler.HandleCatch(catchable, directionXZ);
				}
			}
		}
		else
		{
			Vector2 param = (_object.transform.position - base.transform.position).SafeNormalised(base.transform.forward).XZ();
			if (this.m_uncatchableItemCallback != null)
			{
                // This is only registered by one party: ServerAttachStation.HandleUncatchableItem. It's used to
                // "catch" stuff like plates that happen to collide with the station.
				this.m_uncatchableItemCallback(_object, param);
			}
		}
	}

    public IHandleCatch GetControllingCatchingHandler()
	{
        // If there's a referree then return it.
		if (this.m_iHandleCatchReferree != null)
		{
			return this.m_iHandleCatchReferree;
		}
        // Otherwise return the IHandleCatch with the highest priority, if there are multiple
        // of these on the same GameObject.
        // I think this is only needed between ServerAttachStation and ServerHeatedStation,
        // where the latter returns a int.MaxValue.
		IHandleCatch handleCatch = null;
		for (int i = 0; i < this.m_iHandleCatches.Length; i++)
		{
			IHandleCatch handleCatch2 = this.m_iHandleCatches[i];
			if ((!(handleCatch2 is MonoBehaviour) || (handleCatch2 as MonoBehaviour).enabled) && (handleCatch == null || handleCatch2.GetCatchingPriority() > handleCatch.GetCatchingPriority()))
			{
				handleCatch = handleCatch2;
			}
		}
		return handleCatch;
	}
    ```
  * The `m_iHandleCatchReferree` is set in two cases:
    * `ServerAttachStation` sets the referree on the *item* to the station, if the *item* has a `ServerAttachmentCatchingProxy`.
      In other words, if we place a mixer bowl on a counter, and the mixer bowl detects a collision, we force the handling to
      be done by the station and not the mixer bowl.
    * When a chef is carrying a `ServerAttachmentCatchingProxy`, the referree is set to `ServerPlayerAttachmentCarrier.BlockCatching`,
      which prevents the carried item from catching anything.
    * Both of these cases should only be relevant when the attached item is a ingredient catcher. I can't think of any other
      cases.
* `ServerPlayerControlsImpl_Default` implements the chef catching behavior:
  ```cs
  private void Update_Catch(float _deltaTime)
  {
      if (this.m_controls.m_bRespawning)
      {
          return;
      }
      // See below.
      ICatchable catchable = this.m_controls.ScanForCatch();
      if (catchable != null)
      {
          MonoBehaviour monoBehaviour = (MonoBehaviour)catchable;
          if (monoBehaviour == null || monoBehaviour.gameObject == null)
          {
              return;
          }
          Vector2 normalized = this.m_controlObject.transform.forward.XZ().normalized;
          if (this.m_iCatcher.CanHandleCatch(catchable, normalized))
          {
              this.m_iCatcher.HandleCatch(catchable, normalized);
              EntitySerialisationEntry entry = EntitySerialisationRegistry.GetEntry(monoBehaviour.gameObject);
              // This only triggers some audio and pfx on the client side. The actual attachment behavior
              // is already sent over in the CarryItem call inside HandleCatch.
              this.SendServerEvent(new InputEventMessage(InputEventMessage.InputEventType.Catch)
              {
                  entityId = entry.m_Header.m_uEntityID
              });
          }
      }
  }

  public ICatchable ScanForCatch()
  {
      // GetCollidersInArc is a complex function but when the last argument is false, it scans for all objects whose
      // closest point lies in an arc in front of the chef. It returns position + 0.5 * forward * detectionRadius, so
      // 0.5 in front of the chef.
      // The 2f here is the distance, and 1.57f here is pi/2 so anywhere in front of the chef.
  	  Vector3 collidersInArc = InteractWithItemHelper.GetCollidersInArc(2f, 1.5707964f, this.m_Transform, this.m_colliders, this.m_catchMask, false);
      // This then gets whatever ServerCatchableItem we can find from these colliders, prioritizing the closest one to
      // that chef + 0.5 point.
  	  return InteractWithItemHelper.ScanForComponent<ServerCatchableItem>(this.m_colliders, collidersInArc, this.m_CatchCondition);
  }
  ```
  In other words, the chef scans for anything in front of it within radius 2 and initiates catching. Later we've seen
  above that there's also a 1.8 minimum distance between the center of the chef and the center of the object, and a
  max 120 angle between chef forward and object velocity. The catching is done on the server side unlike most of the other
  chef controls.

## Warping

States we need to warp:

* `m_flying` (if changing, perform flight start/end actions)
* `m_flightTimer`
* `m_thrower`
* `m_ThrowStartColliders` (if changing, perform physics collision changes)

Things to do when warping from non-flight to flight:

* Set pickup referral, server & client
* Register attach changed callback
