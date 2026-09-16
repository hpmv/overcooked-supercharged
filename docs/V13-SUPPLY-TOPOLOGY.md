# V13 receiving-lane failure and correction

V13 stopped at native gameplay frame7851, score876, nine deliveries. This was an incomplete adaptive candidate, not a qualified round or score. The earlier source-board storage recovery succeeded at frames1104–1210 and3981–4087; the temporary heat admission block at3689 resolved when the next chopped bun became available at3981.

The terminal failure was a throw into an empty stove. The original near pot7 had been legally parked on counter38. Its cooked sausage composition and progress13.0666389 remained unchanged. Chef2's raw sausage271, thrown toward far pot2 at7841, attached to the vacant original near stove19 at7851. Far pot2 remained empty on17. The parked-pot observer correctly rejected this occupation of its reserved return home.

The failing supply job at7782 was unguarded: resources70,2, target2. `Supply` sorted the pots by their current X coordinates. Parked pot7 atX19.2 made far pot2 atX18 appear to be the near pot, bypassing the conditional far-lane guard entirely. Sorting moved mixer bowls had the same latent defect.

The correction records each receiving vessel's original native processing home, vessel and home incarnations, and measured pose. Near/far selection uses that stationary topology. Direct near-pot and near-bowl throws require the vessel to remain attached to that exact home at the validated pose, lease the home through the throw, and carry a barrier checked before arming and release. Unexpected invalidation cleanly fails the candidate; neutral cleanup can release an already armed ingredient, so it is not reported as successful cancellation or delivery. Existing addressed counter fallback and exact target-content completion remain in place. The conditional far-pot guard additionally checks both stove components, incarnations and geometry, including the near stove actually holding the original near pot.

The captured before/after fixtures are `artifacts/v13-buffer-boundary-gf7848.json` and `artifacts/v13-buffer-boundary-gf7851.json`. `scripts/SupplyTopologyCheck` runs25 original-topology checks,7 direct-home input barriers,15 existing conditional planner checks,25 conditional input barriers,71 planner checks,54 pot checks and64 sausage-buffer checks (261 total). These are offline checks against native captures and explicitly mutated cases; the correction still requires a native route trial.

## Raw stock observation

Both initial raw sausage buffers eventually loaded legally, but retained two counter leases for most of the first100 seconds:

| Raw entity | Counter | Lease acquired | Parked | Load job | Lease released |
|---|---:|---:|---:|---:|---:|
|145|32|401|572|6340–6529|6529|
|153|61|658|892|6710–6893|6893|

Those leases lasted102.13 and103.92 logical seconds. Central parking initially cost122 and184 frames, and loading later cost189 and183 frames. Both costs are ordinary navigation and placement, not native preparation.

The supplier is dispatched before the central chefs and fresh supplies do not consider existing parked stock. At1178 and4055, staged hotdog preparation released pot2 and chef2 immediately reserved it for a new crate-to-counter supply. Those newer counter handoffs completed1212 and4109, but waited until2934 and5089 respectively for a central load. Buffer loading is also below immediate meal, clean/dirty circulation, onion completion and common heat-safety work; for example, restoring pot7 at2611 was immediately followed by a necessary fryer rescue.

This is slow optional stock circulation, not a permanent buffer ownership deadlock. A controlled next comparison with `SausageBufferSize=0` removes the demonstrated storage burden. A later priority change should compare an actually available central buffer loader against fresh supply, while preserving genuine heat and plate duties; simply withholding fresh supplies can leave pots cold while both centers are occupied.
