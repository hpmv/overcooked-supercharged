### Cannon structure
* `ClientCannon` - handles most animations, e.g. the coroutine for the main flight
* `ServerCannon` - ?
* `IClientCannonHandler` - handler that can be used to launch different kinds of objects.
  Currently there's only one implementation - `ClientCannonPlayerHandler`.

### Launch process
`LaunchProjectile` is the main entry point to the coroutine. This is in response to
receiving a server event to launch the projectile.

```cs
private IEnumerator LaunchProjectile(GameObject _objectToLaunch, Transform _target)
{
    IClientCannonHandler handler = this.GetHandler(_objectToLaunch);
    if (handler != null)
    {
        handler.Launch(_objectToLaunch);
    }
    _objectToLaunch.transform.SetParent(null, true);
    if (this.m_onLaunchedCallback != null)
    {
        this.m_onLaunchedCallback(_objectToLaunch);
    }
    IEnumerator animation = this.m_cannon.m_animation.Run(_objectToLaunch, _target);
    while (animation.MoveNext())
    {
        yield return null;
    }
    if (handler != null)
    {
        handler.Land(_objectToLaunch);
    }
    this.m_cannon.EndCannonRoutine(_objectToLaunch);
    if (handler != null)
    {
        IEnumerator exit = handler.ExitCannonRoutine(_objectToLaunch, _target.position, _target.rotation);
        while (exit.MoveNext())
        {
            yield return null;
        }
    }
    yield break;
}
```

1. Call `handler.Launch`. This sets up the player - disabling controls, setting kinematic,
   disabling collider. See `ClientCannonPlayerHandler.Launch`:

   ```csharp
   public void Launch(GameObject _obj)
	{
		if (_obj != null)
		{
			this.m_inCannon = false;
			this.m_controls.enabled = false;
			this.m_controls.Motion.SetKinematic(true);
			Collider collider = _obj.RequireComponent<Collider>();
			collider.enabled = false;
		}
	}
    ```
2. Detach the transform of the object being launched from the cannon.
3. Call the `m_onLaunchedCallback` if exists. This callback is only registered by 
   `ClientCannonCosmeticDecisions`, so it's cosmetics only (animations, particles,
   audio).
4. Start the `ProjectileAnimation` animation coroutine. See `ProjectileAnimation::Run`;
   this manually animates the projectile by positioning it according to a parabola.
5. Call the `handler.Land` function; see `ClientCannonPlayerHandler.Land`:
   ```csharp
   public void Land(GameObject _obj)
	{
		if (_obj != null)
		{
			this.m_controls.enabled = true;
			if (this.m_controls.GetComponent<PlayerIDProvider>().IsLocallyControlled())
			{
				this.m_controls.Motion.SetKinematic(false);
			}
			Collider collider = _obj.RequireComponent<Collider>();
			collider.enabled = true;
			this.m_controls.GetComponent<ClientPlayerControlsImpl_Default>().ApplyImpact(this.m_controls.transform.forward.XZ() * 2f, 0.2f);
		}
	}
    ```
6. Call `m_cannon.EndCannonRoutine` (note this is on the `Cannon`, not `ClientCannon`). This is registered by only `ServerCannon`
   to call `ServerCannon.EndCannonRoutine`:
   ```csharp
   public void EndCannonRoutine(GameObject _obj)
	{
		this.m_flying = false;
		IServerCannonHandler handler = this.GetHandler(_obj);
		if (handler != null)
		{
			handler.ExitCannonRoutine(_obj);
		}
	}
    ```
    This handler is the `IServerCannonHandler`, which also has only one implementation: `ServerCannonPlayerHandler.ExitCannonRoutine`:
    ```csharp
    public void ExitCannonRoutine(GameObject _obj)
	{
		_obj.GetComponent<Rigidbody>().isKinematic = false;
		GroundCast groundCast = _obj.RequestComponent<GroundCast>();
		if (groundCast != null)
		{
			groundCast.ForceUpdateNow();
		}
		ServerWorldObjectSynchroniser serverWorldObjectSynchroniser = _obj.RequestComponent<ServerWorldObjectSynchroniser>();
		if (serverWorldObjectSynchroniser != null)
		{
			serverWorldObjectSynchroniser.ResumeAllClients(false);
		}
	}
    ```
7. Start the coroutine from `handler.ExitCannonRoutine`; see `ClientCannonPlayerHandler.ExitCannonRoutine`:
   ```csharp
   public IEnumerator ExitCannonRoutine(GameObject _player, Vector3 _exitPosition, Quaternion _exitRotation)
	{
		this.m_inCannon = false;
		this.m_controls.AllowSwitchingWhenDisabled = false;
		this.m_controls = null;
		this.m_playerIdProvider = null;
		if (_player != null)
		{
			DynamicLandscapeParenting dynamicParenting = _player.RequestComponent<DynamicLandscapeParenting>();
			if (dynamicParenting != null)
			{
				dynamicParenting.enabled = true;
			}
			yield return null;
		}
		if (_player != null)
		{
			ClientWorldObjectSynchroniser synchroniser = _player.RequireComponent<ClientWorldObjectSynchroniser>();
			while (synchroniser != null && !synchroniser.IsReadyToResume())
			{
				yield return null;
			}
			if (synchroniser != null)
			{
				synchroniser.Resume();
			}
		}
		yield break;
	}
    ```
    Even though this is a coroutine, it's really only necessary to resume the `ClientWorldObjectSynchroniser`. The
    first yield appears unnecessary.

What is `GroundCast`?

* Seems to be responsible for handling sticking to the ground. Doesn't seem all that important to understand it;
  just need to call `ForceUpdateNow()`. Also, the client part doesn't call this, so maybe it's not necessary here?

What is `ServerWorldObjectSynchroniser` and `ClientWorldObjectSynchroniser`?

* It's for physics synchronization. When the chef is in flight it doesn't need to be synchronized. Afterwards we
  must re-enable it.
* However, physics synchronization is disabled for local session anyway, so this is probably still not important.

What is `DynamicLandscapeParenting`?

* It doesn't matter what it is... we just need to enable it.

### Load process
`ServerCannon`'s Load doesn't really do anything except sending a message. We begin with `ClientCannon.Load`.

```cs
public void Load(GameObject _obj)
{
    this.m_loadedObject = _obj;
    this.m_exitPosition = _obj.transform.position;
    this.m_exitRotation = _obj.transform.rotation;
    IClientCannonHandler handler = this.GetHandler(_obj);
    if (handler != null)
    {
        handler.Load(_obj);
    }
    _obj.transform.position = this.m_cannon.m_attachPoint.position;
    _obj.transform.rotation = this.m_cannon.m_attachPoint.rotation;
    _obj.transform.SetParent(this.m_cannon.m_attachPoint, true);
    if (this.m_onLoadedCallback != null)
    {
        this.m_onLoadedCallback(_obj);
    }
}
```
Pretty simple here - sets all the transforms, parents, and calls `ClientCannonPlayerHandler.Load`:
```cs
public void Load(GameObject _player)
{
    this.m_controls = _player.RequireComponent<PlayerControls>();
    this.m_controls.AllowSwitchingWhenDisabled = true;
    this.m_controls.ThrowIndicator.Hide();
    this.m_playerIdProvider = _player.RequireComponent<PlayerIDProvider>();
    this.m_inCannon = true;
    DynamicLandscapeParenting dynamicLandscapeParenting = _player.RequestComponent<DynamicLandscapeParenting>();
    if (dynamicLandscapeParenting != null)
    {
        dynamicLandscapeParenting.enabled = false;
    }
    ClientWorldObjectSynchroniser clientWorldObjectSynchroniser = _player.RequestComponent<ClientWorldObjectSynchroniser>();
    if (clientWorldObjectSynchroniser != null)
    {
        clientWorldObjectSynchroniser.Pause();
    }
}
```
It's mostly the inverse of the end of the launch process.

### Unload process
`ClientCannon.Unload`:
```cs
public IEnumerator Unload(GameObject _obj, Vector3 _exitPosition, Quaternion _exitRotation)
{
    if (_obj != null)
    {
        _obj.transform.position = _exitPosition;
        _obj.transform.rotation = _exitRotation;
        _obj.transform.SetParent(null, true);
    }
    this.m_cannon.EndCannonRoutine(_obj);
    IClientCannonHandler handler = this.GetHandler(_obj);
    if (handler != null)
    {
        IEnumerator exit = handler.ExitCannonRoutine(_obj, _exitPosition, _exitRotation);
        while (exit.MoveNext())
        {
            yield return null;
        }
    }
    if (this.m_onUnloadedCallback != null)
    {
        this.m_onUnloadedCallback(_obj);
    }
    yield break;
}
```
Basically the same as the launch process except there's no launching.
