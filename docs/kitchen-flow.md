# Kitchen Flow Controller

The Kitchen Flow Controller (`ServerKitchenFlowControllerBase` and `ClientKitchenFlowControllerBase`)
is responsible for keeping track of:
* the list of orders currently on the screen, what each order is and their timer
* when to spawn the next order, and the RNG state that should be used to derive
    the next order
* the score and tip streak for the "team" (which there can be two of, in case of
    Versus mode)
* managing the PlateReturnController (see [plates.md](plates.md))

## Maintaining the order list (Server side)
The order list is maintained in an inner structure called `ServerOrderControllerBase`. It maintains:
* `RoundDataBase m_roundData` which is the implementation used to generate new orders
* `RoundInstanceDataBase m_roundInstanceData`, which is the current RNG state of the `m_roundData`
* `m_nextOrderID`, starting at 1, responsible for assigning an `OrderID` for the next order
  * `OrderID` is used to identify orders internally -- since the active orders list shifts around.
* `List<ServerOrderData> m_activeOrders` which is the list of currently active orders
* `float m_timerUntilOrder`, the amount of time until the next order should be automatically added
  (only relevant if there are currently 2, 3, or 4 orders)
* `m_comboIndex`, which is the index of the last delivered order.

There are also a couple of other fields that are not quite relevant.

The `ServerOrderData` is a serializable class that maintains:
* The `OrderID`
* The `RecipeList.Entry` which is a reference to one element in the level's `RecipeList`
* `float Lifetime`, the time allowed for the order
* `float Remaining`, the remaining time allowed for the order before it expires

In the `ServerOrderControllerBase::Update()` function:
* The remaining time of each order is decreased by the time elapsed; if the remaining time is
  `<= 0` then the timeout callback is invoked;
* The `m_timerUntilOrder` is decreased by the time elapsed
* If there are fewer than 5 orders and it's time to add a new order, or if there are fewer
  than 2 orders, generate a new order, invoke the order added callback, and reset `m_timerUntilOrder`.

Now coming to `ServerTeamMonitor`, which hosts the `ServerOrderControllerBase` as its field; it also has
two other fields, `TeamMonitor` and `TeamMonitor.TeamScoreStats`. The former hosts nothing but the
`RecipeFlowGUI`, which we'll describe when talking about the client side later. The latter hosts the
scores of the team - which we discuss in the scoring section.

`ServerTeamMonitor::OnFoodDelivered` function is called when a dish is delivered by the player:

```cs
public virtual bool OnFoodDelivered(AssembledDefinitionNode _definition, PlatingStepData _plateType, out OrderID o_orderID, out RecipeList.Entry o_entry, out float o_timePropRemainingPercentage, out bool o_wasCombo)
{
    o_orderID = default(OrderID);
    o_entry = null;
    o_timePropRemainingPercentage = 0f;
    o_wasCombo = false;
    // This matches the recipe against the orders. It'll return the matching order with the
    // least amount of time remaining. If there are no matching orders it returns false.
    bool flag = this.m_ordersController.FindBestOrderForRecipe(_definition, _plateType, out o_orderID, out o_timePropRemainingPercentage);
    if (flag)
    {
        // Calculates whether the order was a combo, as this affects scoring.
        o_entry = this.m_ordersController.GetRecipe(o_orderID);
        bool restart = this.m_score.TotalCombo == 0;
        o_wasCombo = this.m_ordersController.IsComboOrder(o_orderID, restart);
        // Removes the order from the list of orders.
        this.m_ordersController.RemoveOrder(o_orderID);
        return true;
    }
    return false;
}
```

Now coming back to the root struct, `ServerKitchenFlowControllerBase`, which hosts a collection of
`ServerTeamMonitor`s; for simplicity we only look at co-op games, i.e. one team.

* `OnOrderAdded` is called when the `ServerOrderControllerBase` invokes the order added callback.
  Its only task here is to call `SendOrderAdded` to send this as a `KitchenFlowMessage` to the client side.
* `OnOrderExpired` is similar, and invokes `SendOrderExpired` to send to the client side. Additionally
  it updates a few fields such as resetting the combo on the team's score struct, and resets the order's
  timer.
    * When sending the data over to the client side, the data also includes the complete updated score
      data.
* `OnFoodDelivered` is called externally. It calls the `ServerTeamMonitor`'s `OnFoodDelivered` and
  * if successful, calls `OnSuccessfulDelivery`; this computes how to affect the team's score stats
    and then calls `SendDeliverySuccess` to send it to the client. The message once again includes the
    full score data.
  * if failing, calls `OnFailedDelivery`, which resets the combo in the score struct and similarly calls
    `SendDeliveryFailed` to share it with the client.
* Note that there's also a way to send only the score to the client, but this is only used when cheating
  to skip a level with certain number of stars.

These `OnXXX` Methods are actually overridden by the concrete implementation `ServerCampaignFlowController`
(or `ServerCompetitiveFlowController`, which we don't cover here). The additional logic controls minor things
like updating the timer suppressor when the first dish is delivered successfully.

## Warping on server side
The `KitchenFlowMessage` can allow us to maintain on the controller side:
* For `ServerOrderControllerBase`:
  * The list of active orders, by incremental computation (deletion is done by successful delivery)
  * The next order ID, which should be the added order's ID + 1
  * The combo index, which can be derived from what was delivered
  * The `m_timerUntilOrder` timer, by decrementing per frame and resetting upon adding orders
  * !!! The `RoundInstanceDataBase` is not recoverable as is, so would need a custom implementation
* For each order:
  * The whole `ServerOrderData`, which comes straight from deserialization;
  * except the `Remaining` time is kept by decrementing per frame
* For the `ServerTeamMonitor`, the `TeamScoreStats` struct is wholly replaced each time by
  deserialization.
* For the `ServerKitchenFlowControllerBase`, there aren't any meaningful fields to warp.

## Maintaining the order list (client side)

The client side mostly mirrors the server side but is more complicated because it additionally
needs to maintain the GUI rendering of the order list.

In `ClientKitchenFlowControllerBase`, we maintain:
* (through a subclass) a `ClientTeamMonitor`, which contains:
  * a `TeamMonitor`, same as `ServerTeamMonitor`
  * a `ClientOrderControllerBase`
  * a `TeamMonitor.TeamScoreStats`
* A `DataStore m_dataStore`; this seems to be an in-memory transient key-value store used for
  notifying UI rendering logic to update UI elements.
* `OrderDefinitionNode[] m_cachedRecipeList`, `AssembledDefinitionNode[] m_cachedAssembledRecipes`,
  and `CookingStepData[] m_cachedCookingStepList`. They... don't seem to actually do anything.

The events from the server are handled as:

```cs
protected virtual void OnSuccessfulDelivery(TeamID _teamID, OrderID _orderID, float _timePropRemainingPercentage, int _tip, bool _wasCombo, ClientPlateStation _station)
{
    GameUtils.TriggerAudio(GameOneShotAudioTag.SuccessfulDelivery, base.gameObject.layer);
    ClientTeamMonitor monitorForTeam = this.GetMonitorForTeam(_teamID);
    int uID = monitorForTeam.OrdersController.GetRecipe(_orderID).m_order.m_uID;
    // See ClientOrderControllerBase.
    monitorForTeam.OrdersController.OnFoodDelivered(true, _orderID);
    // Writes into the DataStore with the key "score.team" to update UI.
    this.UpdateScoreUI(_teamID);
    // This is to keep track of level objectives which don't seem to be used in the game?
    if (this.m_onMealDelivered != null)
    {
        this.m_onMealDelivered(uID, _wasCombo);
    }
    // Achievement stuff.
    OvercookedAchievementManager overcookedAchievementManager = GameUtils.RequestManager<OvercookedAchievementManager>();
    if (overcookedAchievementManager != null)
    {
        overcookedAchievementManager.IncStat(1, 1f, ControlPadInput.PadNum.One);
        overcookedAchievementManager.AddIDStat(22, uID, ControlPadInput.PadNum.One);
        overcookedAchievementManager.AddIDStat(100, uID, ControlPadInput.PadNum.One);
        overcookedAchievementManager.AddIDStat(500, uID, ControlPadInput.PadNum.One);
        overcookedAchievementManager.AddIDStat(801, uID, ControlPadInput.PadNum.One);
    }
}

protected virtual void OnFailedDelivery(TeamID _teamID, OrderID _orderID)
{
    ClientTeamMonitor monitorForTeam = this.GetMonitorForTeam(_teamID);
    monitorForTeam.OrdersController.OnFoodDelivered(false, _orderID);
    this.UpdateScoreUI(_teamID);
    // Also for level objectives which aren't useful.
    if (this.m_onFailedDelivery != null)
    {
        this.m_onFailedDelivery();
    }
}

protected virtual void OnOrderAdded(TeamID _teamID, Serialisable _orderData)
{
    ClientTeamMonitor monitorForTeam = this.GetMonitorForTeam(_teamID);
    monitorForTeam.OrdersController.AddNewOrder(_orderData);
    this.UpdateScoreUI(_teamID);
}

protected virtual void OnOrderExpired(TeamID _teamID, OrderID _orderID)
{
    ClientTeamMonitor monitorForTeam = this.GetMonitorForTeam(_teamID);
    monitorForTeam.OrdersController.OnOrderExpired(_orderID);
    this.UpdateScoreUI(_teamID);
}
```

Note that the actual concrete class `ClientCampaignFlowController` overrides these methods to call
a few extra things in `ClientCampaignMode`. This includes updating the score tip display (`DataStore`
key "score.tip"), and timer suppression update.

Into the `ClientOrderControllerBase`,

```cs
public virtual void OnFoodDelivered(bool _success, OrderID _orderID)
{
    if (_success)
    {
        ClientOrderControllerBase.ActiveOrder activeOrder = this.m_activeOrders.Find((ClientOrderControllerBase.ActiveOrder x) => x.ID == _orderID);
        if (activeOrder != null)
        {
            // Tells the GUI to use an animation to remove the order item. It's not immediately removed
            // from the rendered list but only after the animation is done.
            this.m_gui.RemoveElement(activeOrder.UIToken, new RecipeSuccessAnimation());
        }
        // Mirroring of the server side's active order list.
        this.m_activeOrders.RemoveAll((ClientOrderControllerBase.ActiveOrder x) => x.ID == _orderID);
    }
    else
    {
        for (int i = 0; i < this.m_activeOrders.Count; i++)
        {
            // On failure animate all items with the failure animation.
            this.m_gui.PlayAnimationOnElement(this.m_activeOrders[i].UIToken, new RecipeFailureAnimation());
        }
    }
}

public virtual void AddNewOrder(Serialisable _data)
{
    ServerOrderData data = (ServerOrderData)_data;
    RecipeList.Entry entry = new RecipeList.Entry();
    entry.Copy(data.RecipeListEntry);
    // Adds the item to the GUI.
    RecipeFlowGUI.ElementToken token = this.m_gui.AddElement(entry.m_order, data.Lifetime, this.m_expiredDoNothingCallback);
    // Adds the item to the mirrored list of active orders.
    ClientOrderControllerBase.ActiveOrder item = new ClientOrderControllerBase.ActiveOrder(data.ID, entry, token);
    this.m_activeOrders.Add(item);
}

public virtual void OnOrderExpired(OrderID _orderID)
{
    GameUtils.TriggerAudio(GameOneShotAudioTag.RecipeTimeOut, this.m_gui.gameObject.layer);
    ClientOrderControllerBase.ActiveOrder activeOrder = this.m_activeOrders.Find((ClientOrderControllerBase.ActiveOrder x) => x.ID == _orderID);
    if (activeOrder != null)
    {
        // Order expiration is only a GUI effect because on the client side we don't mirror
        // the amount of time remaining (only graphically).
        this.m_gui.PlayAnimationOnElement(activeOrder.UIToken, new RecipeFailureAnimation());
        this.m_gui.ResetElementTimer(activeOrder.UIToken);
    }
}
```

Now we go into the `RecipeFlowGUI` class, which is responsible for rendering the order list.

It maintains:
* `int m_nextIndex`, used to number newly added recipe widgets (each order is one recipe widget).
* `List<RecipeFlowGUI.RecipeWidgetData> m_widgets`, the list of active widgets.
* `List<RecipeFlowGUI.RecipeWidgetData> m_dyingWidgets`, the list of widgets being deleted but still going
  through the deletion animation.
* (not important) `List<RecipeFlowGUI.RecipeWidgetData> m_ordererWidgets`, which is used only as a local
  variable to merge sort the `m_widgets` and `m_dyingWidgets` lists.
* (not important) `bool[] m_occupiedTables` which simulates tables at a restaurant but is not actually
  displayed or used anywhere in the game. We still need to maintain it though because each order does have
  a table ID.

The actions in `RecipeFlowGUI` are:

```cs
public RecipeFlowGUI.ElementToken AddElement(OrderDefinitionNode _data, float _timeLimit, VoidGeneric<RecipeFlowGUI.ElementToken> _expirationCallback)
{
    int tableNumber = this.ClaimUnoccupiedTable();  // not important

    // Sets up the UI element to render the recipe.
    // When the UI element is first set up, there's an initial animation transition to slide the
    // recipe into place.
    GameObject obj = GameUtils.InstantiateUIController(this.m_recipeWidgetPrefab.gameObject, base.transform as RectTransform);
    RecipeWidgetUIController recipeWidgetUIController = obj.RequireComponent<RecipeWidgetUIController>();
    recipeWidgetUIController.SetupFromOrderDefinition(_data, tableNumber);

    // Sets up the data structure to maintain the UI element.
    RecipeFlowGUI.RecipeWidgetData recipeWidgetData = new RecipeFlowGUI.RecipeWidgetData(recipeWidgetUIController, 0f, this.m_nextIndex, _timeLimit, _expirationCallback);
    this.m_nextIndex++;
    // adds it to the active list.
    this.m_widgets.Add(recipeWidgetData);
    return new RecipeFlowGUI.ElementToken(recipeWidgetData);
}

public void RemoveElement(RecipeFlowGUI.ElementToken _token, WidgetAnimation _deathAnim = null)
{
    for (int i = this.m_widgets.Count - 1; i >= 0; i--)
    {
        RecipeFlowGUI.RecipeWidgetData recipeWidgetData = this.m_widgets[i];
        if (new RecipeFlowGUI.ElementToken(recipeWidgetData) == _token)
        {
            if (_deathAnim != null)
            {
                // If there's an animation, add it to the list of dying widgets and
                // play the animation.
                this.ReleaseTable(recipeWidgetData.m_widget.GetTableNumber());
                recipeWidgetData.m_widget.PlayAnimation(_deathAnim);
                this.m_dyingWidgets.Add(recipeWidgetData);
            }
            this.m_widgets.RemoveAt(i);
        }
    }
}

// Called in `ClientOrderControllerBase` to decrement each order's remaining time.
public void UpdateTimers(float _dt)
{
    for (int i = 0; i < this.m_widgets.Count; i++)
    {
        float timePropRemaining = this.m_widgets[i].m_widget.GetTimePropRemaining();
        float num = timePropRemaining;
        num = Mathf.Max(num - _dt / this.m_widgets[i].m_timeLimit, 0f);
        this.m_widgets[i].m_widget.SetTimePropRemaining(num);
        if (timePropRemaining > 0f && num <= 0f)
        {
            // This doesn't seem to do anything.
            this.m_widgets[i].m_expirationCallback(new RecipeFlowGUI.ElementToken(this.m_widgets[i]));
        }
    }
}

private void Update()
{
    // Remove those dying widgets that finished their deletion animations.
    this.m_dyingWidgets.RemoveAll(delegate(RecipeFlowGUI.RecipeWidgetData obj)
    {
        if (!obj.m_widget.IsPlayingAnimation())
        {
            UnityEngine.Object.Destroy(obj.m_widget.gameObject);
            return true;
        }
        return false;
    });
    // Layouts the remaining widgets.
    this.LayoutWidgets();
}

private void LayoutWidgets()
{
    // Merge sorts the active and dying widgets.
    this.FindAllWidgetsOrdered(ref this.m_ordererWidgets);
    float distanceFromEndOfScreen = this.m_distanceFromEndOfScreen;
    float distanceBetweenOrders = this.m_distanceBetweenOrders;
    float num = distanceFromEndOfScreen - distanceBetweenOrders;
    for (int i = 0; i < this.m_ordererWidgets.Count; i++)
    {
        RecipeWidgetUIController widget = this.m_ordererWidgets[i].m_widget;
        float width = widget.GetBounds().width;
        // There's nothing fancy going on here. The entry animation is done within the UI prefab.
        // When removing a widget there's no animation for the remaining widgets to slide over.
        RectTransformExtension rectTransformExtension = widget.gameObject.RequireComponent<RectTransformExtension>();
        float num2 = num + distanceBetweenOrders;
        rectTransformExtension.AnchorOffset = new Vector2(0f, 0f);
        rectTransformExtension.PixelOffset = new Vector2(num2, 0f);
        num = num2 + width;
    }
}
```

Finally, the `RecipeWidgetUIController`:
```cs
public void SetupFromOrderDefinition(OrderDefinitionNode _data, int _tableNumber)
{
    this.m_recipeTree = _data.m_orderGuiDescription;
    this.m_tableNumber = _tableNumber;
    // Delays the preparation of the actual UI.
    base.StartCoroutine(this.RefreshSubObjectsAtEndOfFrame());
    if (T17InGameFlow.Instance != null)
    {
        T17InGameFlow.Instance.RegisterOnPauseMenuVisibilityChanged(new BaseMenuBehaviour.BaseMenuBehaviourEvent(this.OnPauseMenuVisibilityChange));
    }
}

private IEnumerator RefreshSubObjectsAtEndOfFrame()
{
    yield return new WaitForEndOfFrame();
    base.RefreshSubElements();
    yield break;
}

// From base class UISubElementContainer:
public void RefreshSubElements()
{
    // Does some stuff, mostly calling OnCreateSubobjects.
    this.EnsureImagesExist();
    this.m_debugActivated = true;
    this.RefreshSubObjectProperties();
}
```

And then the `OnCreateSubobjects` function adds the animator, sets up the recipe tiles, etc.

## Warping the client side
* Update the m_activeOrders in the `ClientOrderControllerBase` - just overwrite the entire list.
* Very intrusively update the `RecipeFlowGUI`:
  * Overwrite the `m_nextIndex` - we can make it arbitrarily high, maybe just don't reuse any IDs.
  * Overwrite `m_widgets` to be the currently active orders; reinitialize all of them, force set
    the timePropRemaining of each order
    * The `OnCreateSubobjects` callback need to pulled earlier. To do this, instead of invoking
      `SetupFromOrderDefinition`, we will do that ourselves (it's just the m_recipeTree and
      m_tableNumber), and then call `RefreshSubElements`.
  * Overwrite `m_dyingWidgets` to be empty.
  * Overwrite `m_occupiedTables` to be consistent with whatever indexes assigned to our newly
    created orders.
* Completely replace the `ClientTeamMonitor`'s score struct and refresh DataStore "score.team" key.
  * Note, not "score.tip" key. That's for showing up the tip amount above the serving station.

## Scoring

```cs
public class TeamScoreStats : Serialisable {
    // ...
    public int TotalBaseScore;
    public int TotalTipsScore;
    public int TotalMultiplier;
    public int TotalCombo;
    public int TotalTimeExpireDeductions;
    public bool ComboMaintained = true;
    public int TotalSuccessfulDeliveries;
}
```
