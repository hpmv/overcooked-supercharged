# Central work and a bounded hotdog route consolidation

This audit reads the closed native V16 and V17 recordings. The largest recurring central work remains meal assembly and the separate unplated preparation stages, rather than navigation corners alone. The most concrete next route candidate is to plate an already prepared plain hotdog, apply its one condiment in the upper kitchen, then add cooked onions in the lower kitchen and stage it for service. This can avoid returning an onion hotdog to its source counter and collecting it again for plating. Admission needs an actually available plate; several expensive recorded instances did not have one.

## Recorded workload

The table gives completed jobs for chefs0 and3. Seconds are job occupancy in native gameplay frames divided by60, with explicitly suspended cannon-firing time removed from the original job. Distances sum the first successful planned path for each action; they are neither measured displacement nor a prediction of time saved. Cooking waits, interaction edges, legal retries and neutral boundaries contribute to job occupancy. Jobs for future meals are included even when the round did not deliver those meals.

| Job family | V16 jobs / chef-seconds | V17 jobs / chef-seconds | V17 first planned distance |
|---|---:|---:|---:|
| Meal assembly |25 /101.88 |23 /85.20 |421.26 |
| Unplated hotdog construction |23 /49.72 |23 /53.72 |260.68 |
| Dirty-stack relay |20 /34.75 |20 /39.70 |194.47 |
| Wait and transfer mixed dough |5 /23.98 |6 /32.78 |131.29 |
| Direct cooked-onion addition |6 /26.82 |7 /30.22 |121.50 |
| Clean-plate handoff relocation |14 /23.65 |13 /24.68 |142.45 |
| Chopped-bun buffering |11 /18.37 |13 /23.08 |111.38 |

All completed central jobs total386.67 chef-seconds in V16 and403.88 in V17. Cooperative sauce child jobs are separate from the assembly row. Remaining idle time is not automatically available to remove: raw supply, the FIFO plate pipeline, vessel deadlines and native transport can prevent useful admission. These aggregates identify workload; they do not establish the delivery critical path or promise the roughly doubled throughput still needed for the requested score.

`scripts/project_central_work.py` preserves relevant event records and compact native boundary evidence; `scripts/analyze_central_work.py` rebuilds Work lifecycles. The latter handles a completion callback starting the next Work before the previous completion event and handles explicit cannon pause/resume. The corrected `*-central-work-analysis.json` job lists should be used for durations, rather than treating all open jobs in the raw projection as unfinished.

## Repeated native route witnesses

| Native boundary | Available state | Existing onion stage | Subsequent plate assembly |
|---|---|---:|---:|
| V16 GF5730 |Plain234 on north counter32; clean243 on41; pan4 on16 at9.233s |P0 GF5730–6090:360 frames,25.75 planned units |P3 GF6091–6293:202 frames,15.40 units |
| V17 GF2824 |Plain168 on38; clean185 on43; pan4 at9.000s |P3 GF2824–3146:322 frames,14.16 units |P3 GF3146–3442:296 frames,23.36 units |
| V17 GF9611 |Plain342 on38; clean340 on41; pan4 at9.000s |P0 GF9611–9856:245 frames,13.14 units |P0 GF9856–10031:175 frames,13.87 units |

V16 GF5730 is the strongest geographic example. Chef0 is empty handed near the lower-left shared handoff. The plain hotdog is on counter32 at(19.2,-10.8), while pan4 is on stove16 at(18,-21.6). The current path takes99 frames to collect the plain hotdog,130 to approach the pan,7 to combine, and119 to return the onion hotdog to counter32, plus neutral boundaries. Chef3 then begins a separate plate/condiment/service trip. Combined occupancy is562 chef-frames across two workers, not a single continuous chef journey. The eventual assembly uses a later plate257; a consolidated admission at5730 must explicitly prove whether the earlier clean243 is allocatable to the exact FIFO index.

The recorded proposal is narrower than constructing every hotdog from an empty plate. It starts only from the existing exact plain base and an exact onion cooking lease for that meal. If the requested condiment is not currently selected, the worker must switch it while empty handed before taking the plate. Ordinary assembly then joins plate and plain base, the native condiment transfer occurs in the upper kitchen, native cooking completion is awaited as needed, the same held plate receives the exact onion batch, and the meal goes to the original selected service output. The original emptied pan remains on its stove. Existing food, plate, source, output and condiment ownership must remain exclusive through their native use; the persistent onion lease must be retired only by exact empty-pan and final-food evidence.

This is not yet implemented in the audit's V20 scope. The independent native sequence `routes/probes/plated-hotdog-sauce-before-onion-b.json` completed under frozen V19: plate plus prepared bun (including native plate-under recovery), cooked sausage, Mustard, cooked onions and service staging. Its full proof is owned separately. That mechanism validates the order of native transfers; it does not establish the proposed production admission, deadline safety or savings at5730.

Important counterexamples limit admission. V17 GF4225 has three already filled plates and no empty plate; V17 GF6263 likewise has no usable empty plate. V16 GF9298 and10132 have no observable free plate. At V16 GF11939 a clean plate on44 belongs to another assembly, and at V17 GF8510 the washer holds the clean plate. Removing the unplated stage in those states would delay work behind the plate pipeline. The default existing path must remain available when consolidation lacks a real plate or a safe route budget.

## Allocation takes precedence over route tuning

The same audit found a separate concrete V17 failure: the direct-clean shortcut used the only available plate for future Chocolate25 while ready Both24 was skipped. The next head then waited703 frames for a replacement plate. The mandatory narrowly scoped fix and its unchanged native witness are in [DIRECT-CLEAN-PASS-FAIRNESS.md](DIRECT-CLEAN-PASS-FAIRNESS.md). It is the V20 change. The potential held-plate route is a later, separately validated policy; its effect must not be attributed to the allocation fix.

## Evidence paths

- `artifacts/v16-central-work-projection.json.gz` and `v17-central-work-projection.json.gz`: event/native projections with compressed and decompressed source hashes.
- `artifacts/v16-central-work-analysis.json` and `v17-central-work-analysis.json`: corrected job lifecycles, action timings, planned paths and aggregates.
- `artifacts/v16-onion-chain-gf5730.json`, `v17-onion-chain-gf2824.json` and `v17-onion-chain-gf9611.json`: native response values extracted unchanged.
- `artifacts/v16-onion-chain-witnesses.json` and `v17-onion-chain-witnesses.json`: snapshot and source-call hashes.

Full compressed trace SHA256: V16 `d3802824e17cf667c954b822c5479c3d36ac30ed45b5699caa601c683be1f5e2`; V17 `8f9850c49bb6316f8c3f8298cc58ae52417d5255ea3a2df7d823a3162809317f`. All work for this report was file-only; no game state or frozen candidate was changed.
