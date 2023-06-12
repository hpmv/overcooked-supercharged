## Plate Stacks

Relevant classes:

* `ServerStack`
* `ServerPlateStackBase`
  * `ServerCleanPlateStack`
  * `ServerDirtyPlateStack`

and their client counterparts

### Stack
`ServerStack` does not synchronize anything using messages; it only provides some functions
to add or remove elements from the stack:

```cs
// ServerStack::AddToStack
public void AddToStack(GameObject _item)
{
    if (this.m_stackItems.Contains(_item))
    {
        return;
    }
    
    // IAttachment's only implementation is ServerPhysicalAttachment.
    // Clean plates are PhysicalAttachment's. Dirty plates aren't.
    // PhysicalAttachment objects have more things to do when attaching/detaching.
    IAttachment attachment = _item.RequestInterface<IAttachment>();
    if (attachment != null)
    {
        attachment.Attach(this.m_stack);
    }
    else
    {
        _item.transform.SetParent(this.m_stack.GetAttachPoint(_item));
    }
    this.m_stackItems.Add(_item);
    // Properly positions and aligns each item in the stack.
    this.m_stack.RefreshStackTransforms(ref this.m_stackItems);
    // Properly calculates the height of the colliders for the stack.
    this.m_stack.RefreshStackCollider(ref this.m_boxCollider, this.m_stackItems.Count);
}

// ServerStack::RemoveFromStack - does the reverse, and recalculates transforms and collider.
public GameObject RemoveFromStack()
{
    if (this.m_stackItems.Count > 0)
    {
        GameObject gameObject = this.m_stackItems[this.m_stackItems.Count - 1];
        this.m_stackItems.Remove(gameObject);
        IAttachment attachment = gameObject.RequestInterface<IAttachment>();
        if (attachment != null)
        {
            attachment.Detach();
        }
        else
        {
            gameObject.transform.SetParent(null);
        }
        this.m_stack.RefreshStackTransforms(ref this.m_stackItems);
        this.m_stack.RefreshStackCollider(ref this.m_boxCollider, this.m_stackItems.Count);
        return gameObject;
    }
    return null;
}
```

On the client side, the things to do are very similar:

```cs
// ServerStack::AddToStack
public void AddToStack(GameObject _item)
{
    if (this.m_stackItems.Contains(_item))
    {
        return;
    }
    if (_item.RequestInterface<IClientAttachment>() == null)
    {
        _item.transform.SetParent(this.m_stack.GetAttachPoint(_item));
    }
    this.m_stackItems.Add(_item);
    this.m_stack.RefreshStackTransforms(ref this.m_stackItems);
    this.m_stack.RefreshStackCollider(ref this.m_boxCollider, this.m_stackItems.Count);
}

// ServerStack::RemoveFromStack
public GameObject RemoveFromStack()
{
    if (this.m_stackItems.Count > 0)
    {
        GameObject gameObject = this.m_stackItems[this.m_stackItems.Count - 1];
        this.m_stackItems.Remove(gameObject);
        if (gameObject.RequestInterface<IClientAttachment>() == null)
        {
            gameObject.transform.SetParent(null);
        }
        this.m_stack.RefreshStackTransforms(ref this.m_stackItems);
        this.m_stack.RefreshStackCollider(ref this.m_boxCollider, this.m_stackItems.Count);
        return gameObject;
    }
    return null;
}
```

Note that `AddToStack` is NOT called by some server message propagated from `ServerStack`.
We'll later cover what calls `ServerStack`/`ClientStack`.

The functions on the client side fills in the non-synchronizing case where the object is
not a `PhysicalAttachment` (which would automatically synchronize the attachment on the
client side), as well as ensuring that the transforms/collider are refreshed.

### PlateStackBase
Now we turn to the other component: `ServerPlateStackBase`, which is a separate component not
directly related to Stack.

In the `StartSynchronising` function we add a spawnable prefab for
the element in the stack:

```cs
// ServerPlateStackBase::StartSynchronising
public override void StartSynchronising(Component synchronisedObject)
{
    base.StartSynchronising(synchronisedObject);
    this.m_plateStack = (PlateStackBase)synchronisedObject;
    this.m_stack = base.gameObject.RequireComponent<ServerStack>();
    NetworkUtils.RegisterSpawnablePrefab(base.gameObject, this.m_plateStack.m_platePrefab);
}
```

Then the add/remove functions (AddToStack is triggered externally):

```cs
// ServerPlateStackBase::AddToStack
public virtual void AddToStack()
{
    // Adding to stack always involves spawning a prefab for the inner element.
    GameObject item = NetworkUtils.ServerSpawnPrefab(base.gameObject, this.m_plateStack.m_platePrefab);
    this.m_stack.AddToStack(item);
}

// ServerPlateStackBase::RemoveFromStack (this is virtual, overridden by ServerCleanPlateStack)
protected virtual GameObject RemoveFromStack()
{
    GameObject result = this.m_stack.RemoveFromStack();
    // This is an empty event message; the message is only used for removal from the stack.
    this.SendServerEvent(this.m_data);
    return result;
}
```

On the client side, adding to a plate is triggered by a spawning callback:

```cs
// ClientPlateStackBase::StartSynchronising
public override void StartSynchronising(Component synchronisedObject)
{
    base.StartSynchronising(synchronisedObject);
    this.m_plateStack = (PlateStackBase)synchronisedObject;
    this.m_stack = base.gameObject.RequireComponent<ClientStack>();
    // This callback is called when a prefab is spawned; the same does NOT happen on the server side.
    NetworkUtils.RegisterSpawnablePrefab(base.gameObject, this.m_plateStack.m_platePrefab, new VoidGeneric<GameObject>(this.PlateSpawned));
}
```

```cs
// ClientPlateStackBase::ApplyServerEvent
public override void ApplyServerEvent(Serialisable serialisable)
{
    this.PlateRemoved();
}

// ClientPlateStackBase::PlateSpawned (overridden later)
protected virtual void PlateSpawned(GameObject _object)
{
    this.m_plateAdded(_object);  // cosmetics
}

// ClientPlateStackBase::PlateRemoved (overridden later)
protected virtual void PlateRemoved()
{
    this.m_plateRemoved(null);  // cosmetics
}
```

### DirtyPlateStack

`ServerDirtyPlateStack` registers two additional spawnable prefabs: a clean plate stack and a clean
single plate, because washing with the water jet can turn the stack directly into a clean stack or a
single plate.

Placing a dirty stack on top of another dirty stack triggers `HandlePlacement`:

```cs
// ServerDirtyPlateStack::HandlePlacement
public void HandlePlacement(ICarrier _carrier, Vector2 _directionXZ, PlacementContext _context)
{
    GameObject obj = _carrier.InspectCarriedItem();
    ServerStack serverStack = obj.RequireComponent<ServerStack>();
    for (int i = 0; i < serverStack.GetSize(); i++)
    {
        this.AddToStack();
    }
    _carrier.DestroyCarriedItem();
}
```

this just adds N *new* plates to the stack and destroys the original.

`AddToStack` is overridden to update washable properties. There's also `OnWashingFinished` which
I'll skip here because it's complicated and mostly deals with different cases of respawning the
clean plate stack or single clean plate in the same place (chef's hands or a counter).

```cs
// ServerDirtyPlateStack::AddToStack
public override void AddToStack()
{
    base.AddToStack();
    if (this.m_washable != null)
    {
        Washable washable = base.gameObject.RequireComponent<Washable>();
        this.m_washable.SetDuration((float)(washable.m_duration * this.m_stack.GetSize()));
    }
}
```

The other place where `ServerDirtyPlateStack::AddToStack` is called is in `ServerPlateReturnStation`,
when a dirty plate is supposed to be returned after a plate was delivered for 7 seconds. (Note that
the same logic is also used to respawn clean plate stacks.)

```cs
// ServerPlateReturnStation::ReturnPlate
public void ReturnPlate()
{
    if (this.m_stack == null)
    {
        this.CreateStack();
    }
    this.m_stack.AddToStack();
}
```

There isn't a lot of useful things happening on `ClientDirtyPlateStack`.

### CleanPlateStack

```cs
// ServerCleanPlateStack::AddToStack
public override void AddToStack()
{
    bool flag = false;
    if (this.m_stack.InspectTopOfStack() != null)
    {
        IIngredientContents ingredientContents = this.m_stack.InspectTopOfStack().RequestInterface<IIngredientContents>();
        flag = (ingredientContents != null && ingredientContents.HasContents());
    }
    if (flag)
    {
        // If the top plate has stuff on it, remove it first, add a new plate, then add it back.
        GameObject item = this.m_stack.RemoveFromStack();
        this.AddToBaseStack();
        this.m_stack.AddToStack(item);
    }
    else
    {
        // Otherwise just add one.
        this.AddToBaseStack();
    }
    // For the plate that was just added, set the pickup and placement referral to the stack.
    // (Isn't this wrong if the plate we added was the second item from the top??)
    // (But then maybe it doesn't matter - when would the plate actually be queried first?)
    GameObject obj = this.m_stack.InspectTopOfStack();
    ServerHandlePickupReferral serverHandlePickupReferral = obj.RequestComponent<ServerHandlePickupReferral>();
    if (serverHandlePickupReferral != null)
    {
        serverHandlePickupReferral.SetHandlePickupReferree(this);
    }
    ServerHandlePlacementReferral serverHandlePlacementReferral = obj.RequestComponent<ServerHandlePlacementReferral>();
    if (serverHandlePlacementReferral != null)
    {
        serverHandlePlacementReferral.SetHandlePlacementReferree(this);
    }
}

// ServerCleanPlateStack::AddToBaseStack
private void AddToBaseStack()
{
    // The base class's function wich spawns the plate and adds it to the stack.
    base.AddToStack();
    GameObject obj = this.m_stack.InspectTopOfStack();
    // This is for if the spawned plate would be destroyed, respawn it where the stack would respawn.
    ServerUtensilRespawnBehaviour serverUtensilRespawnBehaviour = base.gameObject.RequestComponent<ServerUtensilRespawnBehaviour>();
    ServerUtensilRespawnBehaviour serverUtensilRespawnBehaviour2 = obj.RequestComponent<ServerUtensilRespawnBehaviour>();
    if (serverUtensilRespawnBehaviour != null && serverUtensilRespawnBehaviour2 != null)
    {
        serverUtensilRespawnBehaviour2.SetIdealRespawnLocation(serverUtensilRespawnBehaviour.GetIdealRespawnLocation());
    }
}

// ServerCleanPlateStack::RemoveFromStack
protected override GameObject RemoveFromStack()
{
    GameObject gameObject = base.RemoveFromStack();
    // Reverses the referree settings.
    ServerHandlePickupReferral serverHandlePickupReferral = gameObject.RequestComponent<ServerHandlePickupReferral>();
    if (serverHandlePickupReferral != null && serverHandlePickupReferral.GetHandlePickupReferree() == this)
    {
        serverHandlePickupReferral.SetHandlePickupReferree(null);
    }
    ServerHandlePlacementReferral serverHandlePlacementReferral = gameObject.RequestComponent<ServerHandlePlacementReferral>();
    if (serverHandlePlacementReferral != null && serverHandlePlacementReferral.GetHandlePlacementReferree() == this)
    {
        serverHandlePlacementReferral.SetHandlePlacementReferree(null);
    }
    return gameObject;
}
```

Now the client side... is a bit strange.

```cs
// ClientCleanPlateStack::PlateSpawned
protected override void PlateSpawned(GameObject _object)
{
    bool flag = false;
    if (this.m_stack.InspectTopOfStack() != null)
    {
        IIngredientContents ingredientContents = this.m_stack.InspectTopOfStack().RequestInterface<IIngredientContents>();
        flag = (ingredientContents != null && ingredientContents.HasContents());
    }
    if (flag)
    {
        GameObject item = this.m_stack.RemoveFromStack();
        this.m_stack.AddToStack(_object);
        this.m_stack.AddToStack(item);
    }
    else
    {
        this.m_stack.AddToStack(_object);
    }
    this.m_delayedSetupPlates.Add(_object);
    base.NotifyPlateAdded(_object);
}

// ClientCleanPlateStack::DelayedPlateSetup
private void DelayedPlateSetup(GameObject _plate)
{
    if (_plate != null && _plate.gameObject != null)
    {
        ClientHandlePickupReferral clientHandlePickupReferral = _plate.RequestComponent<ClientHandlePickupReferral>();
        if (clientHandlePickupReferral != null)
        {
            clientHandlePickupReferral.SetHandlePickupReferree(this);
        }
        ClientHandlePlacementReferral clientHandlePlacementReferral = _plate.RequestComponent<ClientHandlePlacementReferral>();
        if (clientHandlePlacementReferral != null)
        {
            clientHandlePlacementReferral.SetHandlePlacementReferree(this);
        }
    }
}

// ClientCleanPlateStack::PlateRemoved
protected override void PlateRemoved()
{
    GameObject gameObject = this.m_stack.RemoveFromStack();
    ClientHandlePickupReferral clientHandlePickupReferral = gameObject.RequestComponent<ClientHandlePickupReferral>();
    if (clientHandlePickupReferral != null && clientHandlePickupReferral.GetHandlePickupReferree() == this)
    {
        clientHandlePickupReferral.SetHandlePickupReferree(null);
    }
    ClientHandlePlacementReferral clientHandlePlacementReferral = gameObject.RequestComponent<ClientHandlePlacementReferral>();
    if (clientHandlePlacementReferral != null && clientHandlePlacementReferral.GetHandlePlacementReferree() == this)
    {
        clientHandlePlacementReferral.SetHandlePlacementReferree(null);
    }
    base.NotifyPlateRemoved(gameObject);
}

// ClientCleanPlateStack::UpdateSynchronising
public override void UpdateSynchronising()
{
    for (int i = this.m_delayedSetupPlates.Count - 1; i >= 0; i--)
    {
        this.DelayedPlateSetup(this.m_delayedSetupPlates[i]);
        this.m_delayedSetupPlates.RemoveAt(i);
    }
}
```

So... no surprises except that the setting of the referrees on the client side is delayed by 1 frame. Why??
