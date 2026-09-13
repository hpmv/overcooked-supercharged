# Optional shared pantry chopping

Enable with `--pantry-chop --share-pantry-chop`. `SharedPantryChopping` defaults to false and requires `PantryChopping`; the option does not silently change the existing pantry policy.

After all four chefs receive their ordinary priorities, the dispatcher may hand a pantry supplier's remaining chop to a nearby idle central chef. The supplier must have just completed native placement, returned a neutral input frame, and have exactly one unstarted chop left in its existing job. The destination must hold the exact raw entity from that completed placement, with zero native work progress and the correct native workable ingredient family. Source crate and board identities are checked against the supply job's captured metadata.

The helper must be empty-handed, stationary, controlled, and free of other jobs or sauce, onion, mixer, fryer, cannon, and traffic ownership. Its full-clearance route to the board must take at most one second at observed native walking speed. Pre-service stock jobs are excluded because their ready-meal and callback ownership must remain intact.

The existing board reservation transfers without becoming free between owners. The exact raw ingredient receives its own reservation, and only the completed crate pickup's source reservation is released. The supplier can receive new stock work on the next native frame. Its original prepared-food callback is not invoked at raw placement.

The helper completes only after observed native server interaction and positive work progress, followed by the correct native prepared replacement on the same board. It must finish empty-handed and controlled in the center. The helper then releases only its board and raw-item reservations, preserving any later supplier job.

`pantryChopDelegated` explicitly closes the supplier's raw-placement work and identifies the original job, supplier, helper, native source/board/raw identity, and transferred stage. Analysis tools must treat this as a job handoff rather than wait for the original job's usual prepared-food completion event. `sharedPantryChopComplete` records the later native preparation result separately.

The recorded V7 frame684 fixture contains the actual raw bun147 on board23 and neutral supplier inputs. Twenty-nine new controller assertions cover admission, original callback preservation, exact reservation transfer, later supplier work, native completion evidence, stale/pending input rejection, incorrect ingredient/identity rejection, pre-service exclusion, and bounded failure. The source policy requires a fresh native planner trial to measure throughput.
