### Mega Burger

https://www.bilibili.com/video/BV1ZX4y157Ra/

`ServerPreparationContainer` is the primary component of a burger bun; here's the code that is invoked when trying to
place something onto it:

```cs
public bool CanHandlePlacement(ICarrier _carrier, Vector2 _directionXZ, PlacementContext _context)
{
    // Get the thing the chef is holding (a plate of ingredients, in this case).
    GameObject gameObject = _carrier.InspectCarriedItem();
    // Gets the definition of the plate's contents. In this case it would be 3 separate items
    // (cooked meat, cooked meat, pineapple). See below.
    AssembledDefinitionNode[] orderDefinitionOfCarriedItem = this.GetOrderDefinitionOfCarriedItem(gameObject);
    // This is the plate's ServerIngredientContainer component.
    ServerIngredientContainer component = gameObject.GetComponent<ServerIngredientContainer>();
    // It indeed is a plate.
    Plate component2 = gameObject.GetComponent<Plate>();
    // See below. Passes if individual ingredients are each eligible, and also container is not
    // overfilled in capacity. 
    if (!this.CanAddOrderContents(orderDefinitionOfCarriedItem))
    {
        // Tray is the rectangular tray, not the case here.
        Tray component3 = gameObject.GetComponent<Tray>();
        if (component3 != null)
        {
            return true;
        }
        // If the check above fails, and the thing is not a plate, or its ingredients can't
        // individually be inserted, then fail.
        if (!(component2 != null) || !(component != null) || !this.CanTransferToContainer(component))
        {
            return false;
        }
    }
    // Finally check if the thing we're inserting is a plate (true), or the two plating steps are the same (plating steps are like,
    // cup vs plate vs tray).
    return !(component2 != null) || !(this.m_preparationContainer.m_ingredientOrderNode.m_platingStep != component2.m_platingStep);

    // To summarize, if the thing we're transferring from is a plate, we succeed if either:
    //  - The plates' contents are individually eligible for the target, and the target wouldn't be overfilled; OR
    //  - the plate's ServerIngredientContainer's individual elements are transferrable to the target (but without checking
    //    target's capacity!)
}
```

```cs
// ServerPreparationContainer::GetOrderDefinitionOfCarriedItem
private AssembledDefinitionNode[] GetOrderDefinitionOfCarriedItem(GameObject _carriedItem)
{
    IBaseCookable cookingHandler = null;
    // A plate is not cookable, so doesn't apply here.
    ServerCookableContainer component = _carriedItem.GetComponent<ServerCookableContainer>();
    if (component != null)
    {
        cookingHandler = component.GetCookingHandler();
    }
    return this.m_preparationContainer.GetOrderDefinitionOfCarriedItem(_carriedItem, this.m_itemContainer, cookingHandler);
}

// PreparationContainer::GetOrderDefinitionOfCarriedItem
public AssembledDefinitionNode[] GetOrderDefinitionOfCarriedItem(GameObject _carriedItem, IIngredientContents _itemContainer, IBaseCookable _cookingHandler)
{
    AssembledDefinitionNode[] result = null;
    if (_carriedItem.GetComponent<CookableContainer>() != null)
    {
        ClientIngredientContainer component = _carriedItem.GetComponent<ClientIngredientContainer>();
        IContainerTransferBehaviour containerTransferBehaviour = _carriedItem.RequireInterface<IContainerTransferBehaviour>();
        if (component.HasContents() && containerTransferBehaviour.CanTransferToContainer(_itemContainer))
        {
            CookableContainer component2 = _carriedItem.GetComponent<CookableContainer>();
            ClientMixableContainer component3 = _carriedItem.GetComponent<ClientMixableContainer>();
            AssembledDefinitionNode cookableMixableContents = null;
            bool isMixed = false;
            if (component3 != null)
            {
                cookableMixableContents = component3.GetOrderComposition();
                isMixed = component3.GetMixingHandler().IsMixed();
            }
            CookedCompositeAssembledNode cookedCompositeAssembledNode = component2.GetOrderComposition(_itemContainer, _cookingHandler, cookableMixableContents, isMixed) as CookedCompositeAssembledNode;
            cookedCompositeAssembledNode.m_composition = new AssembledDefinitionNode[]
            {
                component.GetContentsElement(0)
            };
            result = new AssembledDefinitionNode[]
            {
                cookedCompositeAssembledNode
            };
        }
    }
    else if (_carriedItem.GetComponent<IngredientPropertiesComponent>() != null)
    {
        IngredientPropertiesComponent component4 = _carriedItem.GetComponent<IngredientPropertiesComponent>();
        result = new AssembledDefinitionNode[]
        {
            component4.GetOrderComposition()
        };
    }
    // This case applies.
    else if (_carriedItem.GetComponent<Plate>() != null)
    {
        ClientIngredientContainer component5 = _carriedItem.GetComponent<ClientIngredientContainer>();
        result = component5.GetContents();
    }
    return result;
}

// ClientIngredientContainer::GetContents. Why Client and not Server? I have no idea.
public AssembledDefinitionNode[] GetContents()
{
    return this.m_contents.ToArray();
}
```

```cs
// ServerPreparationContainer::CanAddOrderContents - can we add these things into the bun?
public bool CanAddOrderContents(AssembledDefinitionNode[] _contents)
{
    if (_contents != null)
    {
        foreach (AssembledDefinitionNode toAdd in _contents)
        {
            // Check each individual ingredient.
            if (!this.CanAddIngredient(toAdd))
            {
                return false;
            }
        }
        // Then check the whole container.
        // This does nothing more than checking the capacity.
        return this.m_itemContainer.CanTakeContents(_contents);
    }
    return false;
}

// ServerPreparationContainer::CanAddIngredient
protected virtual bool CanAddIngredient(AssembledDefinitionNode _toAdd)
{
    CompositeAssembledNode asOrderComposite = this.GetAsOrderComposite();
    return asOrderComposite.CanAddOrderNode(_toAdd, true);
}

// CompositeAssembledNode::CanAddOrderNode
// Basically checks that each ingredient is allowable for this container,
// and that we don't already have too many (e.g. can't insert two lettuces).
public bool CanAddOrderNode(AssembledDefinitionNode _toAdd, bool _raw = false)
{
    if (!_raw && this.m_freeObject != null && this.m_freeObject.RequestInterface<IHandleOrderModification>() != null)
    {
        IHandleOrderModification handleOrderModification = this.m_freeObject.RequestInterface<IHandleOrderModification>();
        return handleOrderModification.CanAddOrderContents(new AssembledDefinitionNode[]
        {
            _toAdd
        });
    }
    int num = 0;
    foreach (AssembledDefinitionNode node in this.m_composition)
    {
        if (AssembledDefinitionNode.Matching(node, _toAdd))
        {
            num++;
        }
    }
    Predicate<OrderContentRestriction> match = (OrderContentRestriction _specifier) => AssembledDefinitionNode.Matching(_specifier.m_content, _toAdd);
    OrderContentRestriction orderContentRestriction = this.m_permittedEntries.Find(match);
    return orderContentRestriction != null && num < orderContentRestriction.m_amountAllowed && !this.CompositionContainsRestrictedNode(_toAdd, orderContentRestriction);
}
```

Also:
```cs
// ServerPreparationContainer::CanTransferToContainer. It basically checks every component of
// the container's contents.
public virtual bool CanTransferToContainer(IIngredientContents _container)
{
    if (_container.HasContents())
    {
        for (int i = 0; i < _container.GetContentsCount(); i++)
        {
            AssembledDefinitionNode contentsElement = _container.GetContentsElement(i);
            if (!this.CanAddIngredient(contentsElement))
            {
                return false;
            }
        }
    }
    return true;
}
```