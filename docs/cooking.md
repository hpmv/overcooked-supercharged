### Cooking Handler

`ServerCookingHandler` manages the progress and state of cooking.

```cs
public void SetCookingProgress(float _cookingProgress)
{
    CookingHandler cookingHandler = this.GetCookingHandler();
    this.m_ServerData.m_cookingProgress = _cookingProgress;
    CookingUIController.State cookingState = this.m_ServerData.m_cookingState;
    if (this.IsBurning())
    {
        this.m_ServerData.m_cookingState = CookingUIController.State.Ruined;
    }
    else if (_cookingProgress >= cookingHandler.m_cookingtime)
    {
        if (this.m_ServerData.m_cookingState == CookingUIController.State.Progressing)
        {
            this.m_ServerData.m_cookingState = CookingUIController.State.Completed;
        }
        if (_cookingProgress > 1.3f * cookingHandler.m_cookingtime)
        {
            this.m_ServerData.m_cookingState = CookingUIController.State.OverDoing;
        }
    }
    else
    {
        this.m_ServerData.m_cookingState = CookingUIController.State.Progressing;
    }
    if (this.m_ServerData.m_cookingState != cookingState)
    {
        this.m_cookingStateChangedCallback(this.m_ServerData.m_cookingState);
    }
}
```

The cooking state is completely determined by the progress.

The `m_cookingStateChangedCallback` is mostly used for cosmetics. It changes the order composition
(because it changes uncooked ingredients to cooked ones), and so can affect certain cosmetic
functions. There's one exception to the cosmetics which is `ServerCookingStation.OnItemAdded`, which
updates whether the cooking station should be cooking. Either way, calling this more than once
shouldn't hurt.

Also, `ServerCookingHandler`'s synchronization is one of the very few components that synchronize on
every frame. It does so by using the `EntitySynchronisationMessage` instead of `EntityEventMessage`.
The message is automatically sent by `ServerSynchronisationScheduler` by querying the component's
`GetServerUpdate()`.

### ServerMixingHandler

Same with `ServerCookingHandler`.

### ServerCookableContainer

This is mostly a helper component. It does not have its own synchronized entity message.

### ServerIngredientContainer

This also uses `GetServerUpdate()` and is synchronized every frame. So correctness isn't really an
issue. We just need to make sure to call `OnContentsChanged()` upon warping.



