### Throwing Basics

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

### Chef Throwing

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

### Throwing Details

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

Finally, we check for any initially colliding items with the thrown item, and ignore collisions for
all of them. This allows us to throw an object over another object (such as if there's a strawberry
on the counter right in front of us we can still throw another strawberry over it).


### Landing

TODO: This is left for another day...

### Catching

There are five implementations of `IHandleCatch`:
* `ServerAttachmentCatcher`: chefs. Note that teleportals don't seem to use this component.
* `ServerAttachStation`: countertops and anything that act like countertops, like chopping boards.
* `ServerHeatedStation`: these are the heating stations in campfire levels where chopped firewood can be thrown in. The component is also used in Surf n' Turf barbeque stations and even coal furnaces but those don't catch anything.
* `ServerIngredientCatcher`: containers that can catch ingredients, e.g. pans, mixers, pots.
* `ServerPlayerAttachmentCarrier.BlockCatching`: this is a special implementation used to block catching when an
  object is being held by a chef. See below for `ServerAttachmentCatchingProxy`.


`ServerAttachmentCatchingProxy` is automatically added to objects with a `HeatedStation`, `AttachStation`, or
  `IngredientCatcher` component (see `MultiplayerController.AsyncScanEntities`).
  * TODO.
  * When a chef is carrying a `ServerAttachmentCatchingProxy` (which should only be possible for the
    `IngredientCatcher` case, e.g. a pan), the game blocks the carried item from catching other objects, by setting the `ServerPlayerAttachmentCarrier.BlockCatching` class on the proxy.

TODO: The rest is left for another day...