# Pot rescue proposal after V11

This proposal was implemented after the native whole-pot offheat probe passed. The frozen V11 controller remains unchanged; source and isolated tests are described in [POT-RESCUE.md](POT-RESCUE.md). The timeline and storage analysis below record the reason for the change.

The timeline establishes a missing urgency decision. Pot7 reached native Burnt at frame2711. At frame2489, chef3 completed dirty relay while pot7 had20.3165 cooking seconds, only2.6835 seconds before a proposed23-second stop margin. The same chef instead began a fryer rescue for basket8, which had9.2666 seconds before its existing19-second stop. At frames2382 and2384, earlier central-job boundaries offered substantially more time to remove the pot. The collision-checked approach paths and original snapshot hashes are recorded in `artifacts/v11-pot-rescue-path-budget.json`; those paths are only travel-to-pot lower bounds and exclude pickup, settling, and carrying to storage.

## Vessel lifecycle

Capture the two original pot-to-stove mappings through native attachment state at initialization. A rescue lease binds the pot, stove, ordinary parking counter, native object identities, and exact pure Cooked Frankfurter composition. Unlike a fryer, a sausage pot need not be assigned to one future recipe at loading time: several pending hotdogs legitimately need the same native cooked ingredient. Bind the selected recipe index only when one legal unplated-hotdog job claims the parked pot.

| Phase | Native actions or evidence | Ownership |
| --- | --- | --- |
| Rescue | Navigate, wait for native Cooked, take the whole pot, place it on the reserved counter | Exact pot/stove/counter plus chef |
| Verify | Two advancing samples with the same pot, unchanged complete food tree and cooking progress; original stove empty | Keep chef and all vessel resources |
| Parked | Observe unchanged offheat food; preserve normal cooking and order rules | Release chef, retain vessel resources |
| Consume | Existing chopped-bun pickup and native combine from the parked pot; preserve exact unplated recipe validation | One normal hotdog job atomically claims the lease |
| Restore | After staging the verified unplated hotdog, take the same empty pot and return it to its original stove | Retain the consuming chef and vessel resources |
| Complete | Original stove contains that empty pot, parking counter empty, chef empty-handed | Release only this pot/stove/counter lease |

`BuildUnplatedHotdog` must accept either a normally free cooked pot or an unclaimed parked pot, while avoiding duplicate ownership of the latter's resources. Its existing board/item/output resources remain owned by the ordinary hotdog job. The staged-resource-release callback must not free a rescued pot's persistent lease when the native contents empty. Normal pantry throws, addressed raw transfers, and sausage-buffer loading must continue rejecting the reserved empty pot until restoration completes.

Where a prepared bun and a short safe route already exist, direct native consumption can avoid parking. That branch requires an explicit path/time budget and the same advancing on-heat deadline observation; the fryer direct-plating bypass showed why checking only parked leases is insufficient.

## One urgency decision

Adding another fixed-position rescue call would reproduce the V11 failure under a different vessel. Build one set of pending native heat/mixing obligations before assigning idle central chefs. Compare measured time until native ruin and the expected legal action needed to make the vessel safe, including travel and button/settling allowance. Preserve already active jobs and their resources; choose among newly eligible work at safe action boundaries.

For the observed frame2489, pot7 must rank ahead of basket8. Earlier frames2382/2384 have greater slack and should beat dirty relay, cold onion loading, and loading an empty pot with another raw sausage. The full native paths to pot7 are11.348 units for chef0 at2382 and8.476 units for chef3 at2384; at observed speed6, those are1.891 and1.413 seconds before pickup and parking. A late failure should terminate cleanly before native burn rather than steal a resource from an active owner or reset an action.

The urgency set also needs to represent a partial mixer recipe. V11 bowl6 at failure contains only Flour and Egg, already Mixed at17.858 seconds. Its complete-recipe parking guard cannot admit it. Native chopped Raspberry was available on board24, so supplying that prerequisite could be an urgency action. Longer-term admission should keep the assigned flavor ready before starting an incomplete mixture's clock, or separately prove a partial-bowl parking/restoration lifecycle. The completed-bowl probe does not by itself establish that extended lifecycle.

## Storage and admission

There are eight ordinary eligible center counters. At observed frame2640, five were allocated to the future bowl, parked fryer, cold onion, and two raw-sausage buffer phases; three remained physically empty outside those reported leases. The immediate burn therefore does not require attributing the failure to complete storage exhaustion.

Two additional pot leases could nevertheless exhaust storage alongside existing pan, fryer, mixer, and raw-buffer leases. Before speculative loading or raw-buffer admission, preserve enough empty storage for active vessels that lack a guaranteed direct harvest. Reserving an exact escape counter before starting a native heat clock is stronger than hoping one remains available later, but requires a coordinated loading/processing lease and may reduce concurrency. Do not reserve two speculative raw-sausage counters while urgent cooked food has no safe destination. The minimal first change should use the observed available counter capacity and a shared urgency decision; expand pre-load reservation only if a measured run demonstrates its necessity.

## Required verification

The native probe must show the same pot leaves its original heat, remains Cooked with the exact same food tree/progress for at least13 native seconds, combines normally into a chopped bun, and returns empty to its original stove. Controller tests must then cover exact native identity, recipe claim exclusivity, staged lease release, restoration, raw refill rejection while parked, unavailable storage, cross-vessel urgency ordering, partial-mix admission limits, native-time deadlines, and preservation of the original jobs and input edges. A fresh full planner round remains the performance and correctness test.
