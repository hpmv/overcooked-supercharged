# Bounded V15/V16 pantry comparison and native wait correction

The file-only comparison covers the same gameplay frames0–8500 in each run. `artifacts/v15-pantry-critical-prefix.json.gz` and `artifacts/v16-pantry-critical-prefix.json.gz` pin the exact consumed compressed-byte prefix and complete decompressed JSONL prefix. They retain compact native snapshots at every10 frames and at job boundaries, native delivery events, and scheduler events. The V16 source was growing when captured; the reader is bounded to its initial file length and the explicit8500-frame cutoff. These are diagnostic prefixes, not round qualification reports.

By frame8500 V15 had12 native deliveries/1280 points; V16 had10/1068. This endpoint cuts across different production/service waves. It does not by itself attribute the difference to a feature or prove a full-round regression.

Completed chef2 job occupancy, in native logical frames:

| Work | V15 | V16 |
| --- | ---: | ---: |
| Bun supply and native chopping | 1898 | 2421 |
| Onion supply and native chopping | 823 | 684 |
| Sausage supply | 750 | 915 |
| Cannon boarding | 422 | 466 |
| Return portals | 738 | 839 |
| Meal collection and delivery | 898 | 769 |
| Initial cannon aim | 44 | 44 |

The totals include only completed jobs; an in-progress final job at the cutoff is excluded. They are chef occupancy, not recoverable wall time. V16's14 completed bun jobs occupy40.35 seconds, so preparation remains a material supplier cost. That is not permission to shorten the native chop duration. Transferring bun flight work to chef2 also consumes this same chef; the separate counter-landing probe established a mechanic, not a net scheduling improvement.

Intervals with no chef2 Work are not all useful idle. Eight V15 service flights account for416 frames and nine V16 flights account for468 frames from native launch to stable arrival, exactly52 each. Waiting loaded for the fire edge accounts for791 and801 additional frames respectively. Those waits depend on central availability/button approach. They cannot be counted as idle pantry capacity. Several remaining upper-left gaps have both the bun and onion boards occupied; for example V16 frames8087–8318 starts with bun317 on23 and onion259 on56 while both pots contain sausage. The next action is service boarding once the FIFO raspberry donut finishes. Sending another bun supply during that gap would require clearing and owning its actual source/storage path.

## Exact avoidable wait rejection

V16 provides a narrow concrete witness. Chef2 finishes delivery0 at1440. The existing imminent-head policy waits for the next complete plate13 while chef3 performs its final placement on shared output49. The initial measured walk-plus-input budget is89.06 frames inside the existing120-frame wait.

At1508 the wait ends because recomputing a conservative walking approach fails. The native snapshot simultaneously reports chef3 still holding the same complete plate13, output49 empty, and **placementTargetId49**. The already-active ordinary action presses pickup on the next frame: native attachment13→49 is observed at1509, and the assembly callback completes at1510. Chef2 has already started a100-frame return portal trip. It supplies a bun1608–1772, boards1772–1818, reaches service1948, and delivers that same second plate at2015.

Exact full responses are saved as `artifacts/v16-pantry-wait-gf1440.json`, `...1507.json`, `...1508.json`, `...1509.json`, and `...1510.json`. Frozen V16's offline path command rejects the1508 approach, reproducing the conservative model discrepancy. The native final placement nevertheless succeeds without a new route or state correction.

`ValidateImminentHead` now retains the existing wait when its unchanged owner holds the exact complete FIFO plate and the native placement target equals the exact reserved empty output for the existing final action. All earlier identity, ownership, recipe, control, and output checks remain. The original120-frame deadline is retained; it is not extended or restarted. A wrong or absent native target still requires the ordinary clear-walk budget. Native same-plate attachment continues to use the existing acceptance path, and collection waits for the ordinary completion callback.

`scripts/ImminentTargetCheck` passes45 new captured/negative checks plus32 prior imminent-head,56 direct-clean-pass, and71 planner assertions. `artifacts/imminent-native-target-tests.json` pins the tested isolated DLL, sources, and native fixtures. Production execution of this scheduler change is still pending. The observed505-frame interval from assembly callback1510 to actual delivery2015 is **not** a claimed recoverable saving: collection/delivery time and changed pantry work must be measured in the next native candidate.

The highest-confidence next reduction from this bounded pantry analysis is the exact native-target correction above. Broader service-batch expansion should remain tied to observed complete-head ownership and deadlines. Source-board handoffs and fire-button work need coordination with central jobs; raw idle totals alone do not justify another supplier task.
