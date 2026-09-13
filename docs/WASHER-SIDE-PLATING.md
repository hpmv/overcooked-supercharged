# Optional washer-side plating

`WasherSidePlating` / `--washer-side-plating` is off by default. It lets the LL washer assemble a fully prepared, sauce-free unplated hotdog onto one exact clean plate during a washing gap. A center chef stages the food on shared counter50, the washer takes the clean plate from44 and performs ordinary plate-under assembly on50, and an available center chef recovers the prepared plate to an existing meal output.

This first scope accepts Plain and completed Onion hotdogs. It excludes donuts and meals needing ketchup or mustard. It does not allocate new surfaces or alter the normal dirty handoff46, sink75, washing time, plate count, food rules or inputs. A plate still in the clean output stack must first follow the ordinary handoff to44; this option does not infer a particular stack plate identity.

## Native mechanism evidence

The root-controlled probe `artifacts/washer-plating-a.jsonl.gz` and file-only report `artifacts/washer-plating-a-proof.json` establish the following transitions. These are native frames from the dedicated mechanism probe, not a full-round planner result.

| Native gameplay frame | Observation |
| --- | --- |
| 1143 | Exact unplated Plain food125 is attached to shared50. |
| 1159 | Washer1 holds the original clean plate10 from44. |
| 1194 | Native assembly has consumed food125; the same plate10 holds the prepared Plain. |
| 1202 | Washer1 has placed plate10 on50 and has empty hands. |
| 1211 | Center0 holds that same prepared plate10. |
| 1299 | Plate10 is on the existing output49. |

The checked trace contains1300 contiguous native observations, with maximum pot progress12.54998 and all other processing vessels empty. Its SHA256 is `b00ecc250fd205142619b3ada3b46e6273e6e920779f42309a524ba2bcfb7b76`; the final response SHA256 is `fc5a0027c624f951cc3466ae50302aa88d3178e39b91c67a49c74e0e89e600e6`.

This proves the input mechanism. It does not prove a delivery, washing cycle, throughput gain, a5000-point score, or reproducibility across fresh processes.

## Planner admission and ownership

Admission requires an idle, controlled, empty-handed washer on LL; no currently available sink, clean-output or dirty-handoff work; the known clean plate on44; an exact prepared unplated food assigned to a pending native recipe; and available source/50/output resources. Normal FIFO plate and output allocation predicates still apply. Existing native heat obligations prevent this elective admission.

The selected native food, plate and station ordinals are retained. One persistent transaction reserves the exact food, plate, original source,44,50 and output. Its child jobs own their chef slots but do not independently release those station leases. This prevents ordinary clean-plate relocation or meal assembly from stealing the same plate or surfaces.

If the food is already on50, no staging job is created. Otherwise a center chef performs ordinary take/place and is released when the exact food is observed on50. The washer remains in an explicitly owned neutral wait until then. The washer performs take44/assemble50/place50 and is released immediately after the same prepared plate is observed on50. Center recovery may happen while the washer performs a subsequent normal washing job. The persistent transaction closes only after the exact prepared plate is observed on its reserved output; only its own reservations are then released.

All three phases use full-clearance walking paths and ordinary actions with dash disabled. Admission uses measured native walking speed and a bounded path estimate; this estimate is not a guarantee under later chef congestion. Recovery respects the common heat arbiter's blocked-chef set and leaves safety work ahead of elective plating. Every phase is limited to360 frames, and the transaction to900 frames. A changed identity, composition, participant ownership, resource lease or missed deadline fails the attempt through the planner's existing diagnostic/neutral-cleanup path. It does not repair native state.

Logs are `plannerWasherPlatingStarted`, `plannerWasherFoodStaged`, `plannerWasherPreparedPlateStaged` and `plannerWasherPlatingComplete`; normal child jobs record their actions. `plannerStatus.washerPlating` reports current phase, native identities, output and phase/transaction start frames. Every emitted input remains part of the ordinary exact input trace.

## Offline validation and limits

`CarnivalPlanner.WasherPlatingSelfTest` has42 assertions using the six native observations above, explicit synthetic planner ownership and negative mutations. They cover exact consumption/plate preservation, phase callbacks, releasing the washer before recovery, dirty-loop independence, resource conflicts, fixed bounds, missed heat priority and default-off behavior. The additional center-staging test uses an explicitly synthetic food location and clear chef pose; it is not described as a native trajectory proof. The fixture driver cannot call the game.

`scripts/WasherPlannerCheck` also runs71 existing planner and40 intercepted-pot assertions. Its current isolated build is `artifacts/washer-planner-check`; it is not the frozen production candidate. Native use of this optional planner path, full-round performance and fresh-process replay qualification remain pending.
