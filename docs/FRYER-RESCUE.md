# Native fryer rescue

The V10 planner lost its first Raspberry donut before that order became the FIFO head. Basket8 received mixed dough at gameplay frame1734, became natively Cooked at2334, and became Burnt at2935. The FIFO cursor reached its recipe index7 only at4996. Native snapshots at these exact transitions are saved under `artifacts/v10-fryer-*`.

The first cleaned plate reached the shared washing handoff at2519 and center storage at2660. The second arrived at2759. Central chefs were occupied with onion work until2839 and2854. When chef3 became free at2854, the old dispatcher chose dirty-stack relay before non-head donut assembly; that relay ended2941, after the native burn. The future index10 dough successfully remained intact off its mixer and did not replace or lose the earlier order assignment.

The separate native offheat probe is pinned in `artifacts/fryer-offheat-a-proof.json`. Original basket5, ordinal4, remained on ordinary counter37 with its original fryer15 empty for782 contiguous samples, frames1850–2631. Native client time advanced13.0166672 seconds and round time advanced13.016465 seconds. Cooking progress11.6166611 and the complete food-tree hash stayed identical. Original plate13 then acquired the fully cooked chocolate donut, and the same empty basket returned to its original fryer. All actions were ordinary movement, native cooking, pickup, and transfer inputs.

The planner's mandatory rescue policy preserves the original baskets and native deadlines:

- Idle central chefs consider assigned baskets from native cooking progress9 seconds before other speculative work and dirty relay.
- A ready donut can use ordinary exact-index plating when native FIFO plate allocation permits it, both collision-safe pickup/harvest paths exist, and their native walking time plus2 seconds fits before progress19. Direct plating is rejected at progress15 or later.
- Otherwise the planner reserves the exact basket, its observed original stove, and an empty ordinary center counter. A chef navigates to the stove, waits for native Cooked, picks up the whole basket, and parks it off heat.
- Two advancing samples with unchanged food and cooking progress prove parking. The chef becomes available; all vessel and storage reservations remain.
- Ordinary FIFO-safe assembly may later scoop that exact cooked recipe from its parked basket. The same empty-handed chef restores the empty basket to its original stove before its basket, stove, and parking-counter reservations are released.
- Every assigned basket still attached to its native stove has an independent progress19 deadline, including baskets owned by ordinary plate jobs. A delayed controller action terminates the attempt instead of silently burning food.

Onion, mixer, sauce, cannon, sausage, and traffic schedulers cannot borrow a chef held by the rescue's verification phase. Two baskets bound the number of rescue leases. Empty storage and available cooks remain real capacity constraints; the policy does not synthesize counters, plates, ingredients, or time.

`CarnivalPlanner.FryerRescueSelfTest` has27 controller assertions, including captured native geometry, exact recipe/basket ownership, advancing offheat proof, ordinary plating, restoration, duplicate ownership rejection, changed identity/progress rejection, and both parking and direct-plating deadline checks. The native probe verifies the mechanism. Fresh full-round execution of the complete policy remains separate validation.
