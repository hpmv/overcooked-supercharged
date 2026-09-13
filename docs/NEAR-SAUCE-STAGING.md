# Optional nearby serial sauce staging

`NearSauceStaging = false` is the default. Enable the planner option with `--near-sauce-stage`. It changes only temporary plate parking/recovery for the serial two-sauce fallback; cooperative admission and native mustard-then-ketchup order remain unchanged.

At admission, the selector uses one currently reachable centered dispenser approach as a common comparison origin. The later first application approach depends on other chefs' movement, so the selector does not borrow a future recorded position. It evaluates dispenser → candidate counter → switch → same counter → dispenser with the existing collision-aware navigation. It selects the smallest complete feasible path sum, rounded to micrometres for ranking only, then native counter ID for ties. Every actual action continues to resolve native targets and replan against live geometry.

Candidates are the exact original unplated source (which becomes empty through this job's native assembly) and actually empty, unreserved ordinary center counters. The protected workspace, output, processing stations, missing identities and unrelated reservations are excluded. The selected extra counter is acquired in the same ordinary Work as the original source, food, plate, output, dispenser and switch. A pre-CompleteWork observer verifies station identities/positions, plate/base provenance, attachments and the original Work's current ownership before further inputs. A bounded cannon interruption may suspend that exact Work without releasing its leases. Final native recipe/output/empty-temp evidence retires the observer; old completion cannot release a successor's ownership.

The GF1346 native fixture selects counter40 over33 and the original37. Validation is `SauceStagingSelfTest(admission1346)`:41 assertions cover exact selection, reserved/occupied/unknown fallback, protected workspace, source replacement, static obstruction, deterministic ties, default-off behavior, native ingredient order, lease transfer and identity failures. `SauceStagingRecordedGuardSelfTest` separately checks the observer against all810 unchanged native GF1346–2155 states of the original source37 serial route, including native source destruction and plate recovery. These are offline correctness checks, not measured gameplay savings.

Build/test outputs are isolated in `artifacts/near-sauce-check`; `tests.json` records the checked assembly and source-trace hashes. The public test entry points permit the combined candidate harness to test the same frozen assembly. Runtime diagnostic events are `nearSauceStagingAdmitted`, `nearSauceStagingRefused` and`nearSauceStagingComplete`; admission/refusal includes all candidate paths and reasons.

## Matched native experiment prepared, not executed here

`scripts/prepare_sauce_staging_probe.py` checks the closed V11 trace SHA and refuses all existing output paths. It preserves all1349 original requests throughGF1346, including restart/inspect/preview and unmodified step boundaries. It then appends one **explicitly new** all-neutral step, ending at expectedGF1347. This appended step is not represented as part of the exact original recording.

Prepared files and exact hashes are in `artifacts/sauce-staging-probe-preparation.json`:

1. `routes/probes/sauce-staging-prefix-gf1346-neutral.jsonl.gz`
2. `routes/probes/sauce-staging-common-setup.json`
3. `routes/probes/sauce-staging-source37.json`
4. `routes/probes/sauce-staging-near40.json`

At the original boundary P3 carries Mixed bowl6 and still has an active dash input. Bowl3 remains on its mixer at9.579203/12, only14.420797 seconds before native overmix; comparing a13-second sauce branch without removing that clock would leave little margin. The new common setup waits18 neutral frames, places bowl6 on empty ordinary33, takes bowl3 off its mixer, and waits two frames. It is an authored legal-input experiment: actual bowl identity, partial/Mixed state, offmixer progress and chef settlement must be observed, not inferred from the earlier finished-dough proof.

For both branches, independently replay the same fresh prefix and the same common setup. Record the first setup's actual input requests; replaying those exact requests for the second branch pins the setup inputs more strongly than merely rerunning its adaptive action plan. Require comparable observed pre-branch positions/food/stations/inputs/physics phase and keep raw differences visible. All other chefs remain neutral during each P0 branch. The branches differ only in temporary parking/recovery37 versus40; source37, plate13, output49, both sauce steps and ordinary actions are identical.

The common setup budget is360 frames and branch budget900 frames. Including the appended neutral step, their combined maximum is21.0167 seconds fromGF1346; native pot2 then starts with22.250001 seconds to burn and pot7 with22.733334 seconds. These arithmetic bounds are not path guarantees. Observe actual processing clocks and abort before unsafe native heat/overmix progress; if the setup fails or consumes its budget, do not start a branch without sufficient observed remaining margin. Confirm final exact plate13 recipe125780 on49, empty selected temp, consumed base151, no deliveries/deductions/ruined food, and actual branch frame/timer duration. Compare the two new measurements, not the old full-planner13.4833-second route whose other chefs continued working.

Before the first setup execution, root found that the prepared wait actions used `frames`, which the runner ignores in favor of `durationFrames` (default one). The two property names were corrected to `durationFrames: 18` and `durationFrames: 2`; the prefix and both branch files remained byte-identical. `artifacts/sauce-staging-wait-correction.json` records old/new hashes, and the original receipt is archived. `scripts/test_sauce_staging_preparation.py` now tests effective durations as well as original prefix hashes, the two permitted branch changes, and no-overwrite behavior.

No plugin, native game or frozen candidate files were edited for this implementation or preparation.
