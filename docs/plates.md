### Plate Delivery

`ServerPlateStation` is the serving station.
* It keeps a list of `ServerPlateReturnStation`s which corresponds to the stations where dirty plates will spawn.
    There can be multiple because there may be multiple kinds of plates (cups and plates, e.g.).
* `IKitchenOrderHandler m_orderHandler` handles the delivery of the order.
    * Implemented by `ServerKitchenFlowControllerBase`. The `OnFoodDelivered` calls its
      `m_plateReturnController.FoodDelivered`, which adds a `PlatesPendingReturn` to its list of pending plates.
    * A `PlatesPendingReturn` specifies the return station, the timer, and the `PlatingStepData` (identifying the
      kind of plate).
    * `PlatingStepData` is just an `m_uID` plus a sprite. The actual prefab for the dirty plate is created by the
      `PlateReturnStation` as a spawn.
* Logic is triggered by `OnItemAdded`, registered on the `m_attachStation` (sibling component) via `RegisterOnItemAdded`.

