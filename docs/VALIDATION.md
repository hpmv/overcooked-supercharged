# Validation status

The best completed authored adaptive run is **V21: 2,720 points and 25 deliveries** over the native 270-second round, comprising 2,040 base points and 680 tips with zero deductions. The independent native score audit validates all 25 recipe/order matches and the before/after score chain. It ended at gameplay frame16,203 after a native kitchen restart in existing lab process71,732; this was not a fresh-process trial. **V21 exact-input replay is untested**. See the [completed-round report](V21-COMPLETED-ROUND.md), [native summary](../artifacts/native-round-v21/summary.json), and [score audit](../artifacts/native-round-v21-native-score-audit.json).

V16's earlier 2,612-point/24-delivery run and its untested extracted movie remain preserved in the [V16 report](V16-COMPLETED-ROUND.md).

The completed authored V14 movie reaches the native 270-second round end with **2,042 points and 20 deliveries**, comprising 1,600 base points and 442 tips with no deductions. One fresh-process exact-input replay matches all 16,226 requests and pad snapshots, current-round recipe RNG, all 20 native delivery events and their timing, and the final score. The adaptive V14 prefix remains a failed trial; its separately authored 700-frame neutral continuation and replay are distinct artifacts. See the [completed replay report](../artifacts/v14-completed-replay-a-report.json), [movie manifest](../artifacts/v14-completed-movie-manifest.json), and [continuation details](AUTHORED-ROUND-CONTINUATION.md).

The [unmodified full comparison](../artifacts/v14-completed-replay-a-comparison.json) records exact gameplay/native-event agreement for the first 15,524 samples. Every one of the 702 continuation samples also matches gameplay events, native event payloads, sequence and timing under the separately named [pinned neutral-disconnect projection](../artifacts/v14-completed-replay-a-pinned-neutral-join-projection.json). That projection removes only the three complete expected `input_release` records at the authored join, pinned as indices 20, 21 and 22 in the manifest. It retains every other event and its index. Strict state, raw physics and clock/observation differences remain in both the original and supplemental reports; this is not bit-identical replay.

The V15 adaptive trial later stopped at gameplay frame 12,672 (neutral cleanup 12,673), with 1,800 points, 17 deliveries and 58.7741241 native seconds left. The far bowl exceeded the controller's 21-second safety guard after its supplier left for washing; this guard failure does not establish that native food was already ruined. Frozen V16 contains a preventive supplier/departure fix and a bounded return for an active partial mix, supported by captured fixtures. The completed V16 result validates one integrated adaptive run; its improvement is not a controlled feature comparison. See [the failure and correction](V15-BAKERY-DEPARTURE-CYCLE.md), [native trial summary](../artifacts/native-round-v15/summary.json), and [V16 manifest](../artifacts/planner-candidate-v16/manifest.json). **Zero fresh runs qualify at 5,000 points; the five-run high-score gate remains unmet.**

The earlier `gate20-c` runtime passed twenty probe replays across five fresh processes, including every cross-run comparison and stronger initial/coverage checks. Inputs, recorded gameplay events, native delivery timing and current-round recipe RNG match; raw physics differs. Its independent audit remains `artifacts/gate20-c-independent-comparison-audit.json`. This probe gate does not require a complete round or the target score.

## Observed probes

The installed Steam build is `20236421`, using Unity 2017.4 and the native 50 Hz physics step. The isolated runtime is under `runtime/`; the original installation remains separate. The bot gates complete frames at 60 logical Hz and preserves native cooking, chopping, throwing, cannon, order and score rules. Native recipe RNG isolation uses the original weighted generator and native Unity RNG state.

| Evidence | Observation | Scope |
| --- | --- | --- |
| `artifacts/restart-a.jsonl.gz`, `restart-b.jsonl.gz`, `restart-ab-analysis.json` | Before phase alignment/RNG isolation, equal absolute frame 443 and timer 269.983337 had different physics phase and first recipe. Recorded spawn physics matched. | One initial sample per restart. |
| `artifacts/movement-a.jsonl.gz`, `movement-b.jsonl.gz`, `movement-ab-analysis.json` | With the natural no-physics start gate and seed 0 isolated recipe RNG, all eight samples at gameplay frames 0, 1, 16, 31, 61, 62, 86 and 146 matched raw physics hashes, gameplay events, orders, inputs and other recorded native state apart from client clock readings. Initial phase was 5 in both, and the first order was `Hotdog_Mustard`. | Two same-process restarts, movement/dash only. Largest unsampled interval: 60 frames. |
| `artifacts/planner-first-c2.jsonl.gz`, `planner-first-c2-analysis.json` | Native matched mustard delivery at gameplay frame 1463; checkpoint ended at frame 1464, score 68, one delivery. Stable-parent plate recovery succeeded. | One partial native run, 24.4 seconds; not a complete round or replay gate. |
| `artifacts/planner-four-a.jsonl.gz`, `planner-four-a-analysis.json` | One native matched delivery, score 68; attempted four-delivery trial stopped on a station-approach navigation failure at frame 1894. Pan 4 had 0.3002 seconds of additional native heating margin before burning. | Partial run, 31.5833 observed seconds including failure neutralization. No native Burnt label had appeared yet. |
| `artifacts/planner-four-b.jsonl.gz`, `planner-four-b-analysis.json` | Four native matched deliveries at frames 1386, 2236, 2920 and 3040; checkpoint ended at frame 3041, score 356, with no deductions or burnt-food observations. The final two deliveries were two seconds apart. | Successful four-delivery checkpoint, 50.6833 seconds; still a partial round. Delayed onion completion and reuse through a delivered washed plate require the next longer trial. |

The second pair started at absolute frames 438 and 430. Their initial `clientDeltaTime` values were 0.01666689 and 0.0166664124, differing by approximately 4.776e-7 seconds. The native clock subtracts rounded float absolute timestamps. The clock implementation has not been changed to conceal this difference. No inference about unobserved frames follows from matching sampled states.

The extracted movement input movie has 8 original requests covering 146 logical frames. Its exact file SHA-256 is `f144136203df395cb95d1af7bd63e73fd4404d6c798b7dfcc0a2aaaf8fb6c16e`; its recording SHA-256 is `8e09fe6a054077060fc873abac3ff52b341ededaa506d14d57033f25351b7dee`. This short, sparse movie does not qualify for either verification gate below.

## Authoring an auditable movie

Record one successful measured route with all four players' inputs. Extract its requests without replacing native decisions or adding adaptive actions during playback:

```powershell
python scripts/extract_inputs.py artifacts/candidate-recording.jsonl.gz --out artifacts/candidate-inputs.jsonl.gz
```

The extractor streams JSONL/gzip, preserves every request in order, and writes a manifest with exact recording/movie hashes and a request digest. It never infers input commands from response pad values, changes a seed, normalizes axes, discards inspection calls, or adds release frames.

`--expand-step1` is explicit. Repeated unchanged `Inputs.Apply` polls are idempotent in the current native logical-button implementation: claim flags remain claimed and held-time start is set only on the original rising edge. Each repeated request recomputes the same axes from the same original values. Extra protocol completions still add telemetry captures and wall-time pauses. Compare the expanded movie against the original at its original checkpoints before treating full runtime equivalence as established. That live expansion comparison remains pending.

The qualifying movie must begin with an explicit native `load` or `restart`, seed and recipe-isolation setting, and end with neutral inputs. Every `step` must be one frame. The body may contain `step`, `inspect`, `state`, recorded `pause`, and a strictly validated read-only `preview`; render, setup, additional restarts and unrestricted `resume` do not belong in the body. Preserve the final native round-end frames inside a high-score movie; the verifier does not append a state-dependent waiting tail.

A preview request must provide an Int32 seed and count 1–1024. The verifier checks the matching native generator and response count and requires all six restoration assertions: ambient RNG, isolated RNG, the isolated per-round RNG registry, observation state, live round instance and logical frame. Recorded responses are checked when supplied; an input-only movie defers missing response proof to the actual playback response. The planner also requires the registry-restoration assertion before using a native preview.

## Verification gates

`scripts/Verify.ps1` uses the existing isolated `Launch.ps1` and the .NET 10 controller's `verify-run` command. A single controller connection covers the prelude and movie, avoiding disconnect-generated input changes between commands. When the movie begins with `restart`, a logged native `load` prelude first establishes the Carnival session; the original movie restart is retained. Prelude/render requests are separate from the hashed movie body.

Before any body input, the controller requires `kitchen_ready`, DLC 8, the four-player `s_Day_3_4` variant, four synchronized users and chef IDs 0–3, a paused gameplay-frame-zero snapshot, zero score/deliveries, and healthy event telemetry. `SessionSetup.kitchen_ready` includes native local-user and local-chef ownership validation. The current snapshot independently reads the native server timer limit and loaded four-player RoundData timer: both must be 270, with level configuration `Day_3_4_4P`. It also checks the measured first round tick, `270 - 1/60`, with 0.0001-second serialization tolerance.

The runner creates five fresh isolated process identities with render caps 0, 30, 60, 120 and 60 to vary wall/render load while leaving logical capture time and native physics fixed. It defaults to refusing pre-existing game processes. Explicit `-AllowIsolatedLab` permits only the kernel-identified workspace lab image as logged concurrent wall-clock load. Cleanup first uses the existing quit command and may terminate only the primary PID it created after checking its executable path and creation timestamp again. It never closes the lab or original game by process name.

The high-score commands require a future qualifying movie; the twenty-run example uses the existing complete probe:

```powershell
# Review the planned invocation and bound input hash without launching a game.
pwsh scripts/Verify.ps1 -Movie artifacts/candidate-inputs.jsonl.gz -Mode 5 -PlanOnly

# Five complete high-score playbacks in five fresh processes.
pwsh scripts/Verify.ps1 -Movie artifacts/candidate-inputs.jsonl.gz -Mode 5

# Twenty combined probes: four exact movies per process, five fresh starts.
pwsh scripts/Verify.ps1 -Movie routes/probes/complete-probe-inputs.jsonl.gz -Mode 20
```

Mode `5` requires each movie to cover the native 270-second round, reach native outro with timer zero and both rounds inactive, score at least 5000, and retain matched native delivery evidence. Every delivery must have the expected native recipe, base value, ingredient multiset and order ID. The complete before/after chain of base score, tips, deductions, total score and delivery count must reconcile with the native final totals. Native matching remains the cooking/plating authority, and native tip computation remains game-owned. A score reached before the round ends is insufficient.

Mode `20` requires held-item state, native work/cooking/mixing progress, throw release, an actual chef catch, both identified cannon flights and landings, linked portal travel, washing with dirty-plate decrement and clean output, a completed native dash, corroborated solid-contact blocked motion, and native matched/scored delivery. Collision evidence is explicitly an inference from queued velocity, positions and solid geometry; it is not a collision callback. Initial gates bind actual clocks, hook/binary/plugin identities, complete current-round registry writes, four local slots and the loaded 270-second configuration. This mode does not require a full round or 5,000 points.

Both modes require exactly matching executed input hashes and matching per-frame gameplay/native event sequences across all requested runs. No dropped event or observer error is accepted. Keep each process's all-call trace, each movie trace, input manifest, PID/path/creation-time record, render setting, game/plugin/controller hashes, native result and offline comparison report.

The five closed combined `gate20-b` traces were losslessly archived as XZ after byte-for-byte decompressed verification; their duplicate gzip containers were then removed. All twenty individual movie traces remain unchanged. `artifacts/gate20-b-closed-combined-archive-index.json` pins original/container/raw hashes and restore commands. Restoring preserves exact JSONL bytes; a recreated gzip container need not have the original gzip hash.

## Reading reproducibility reports

`scripts/analyze_repro.py` separates strict recorded state, native state, gameplay events, raw IEEE754 physics hashes, clocks, RNG and observations. It streams plain/gzip traces and aligns by gameplay frame. It records missing samples and never describes an unsampled interval as verified.

Optional initial Rigidbody-ID normalization requires unique same-prefab/same-position proxies attached to matching stable semantic anchors in both initial snapshots. The manifest records each native anchor, components and validation hash; ambiguous/unmatched entities remain native. Strict/native-ID comparisons are retained. No route-wide position tolerance or arbitrary entity-ID remapping is used.

Native event sequence comparison retains event order, gameplay frames, recipes, matches and score/delivery deltas. Absolute diagnostic epochs are separated; `remainingFraction`, `orderRemaining` and `roundElapsed` are also reported as exact event timing rather than suppressing their differences silently. Full native event payload comparisons remain available. Native state and timer differences are always reported, including any timestamps inside chef or entity state.

The runner distinguishes `bit_exact_recorded_state`, `exact_inputs_stable_native_events_and_recorded_physics`, `exact_inputs_stable_native_events_with_physics_differences`, and `gameplay_divergence`. Stable input/event playback with float differences is not a claim that every Unity internal state is bit-identical. Both `gate20-b` and the current, more extensively instrumented `gate20-c` passed all twenty probes with the third classification. The five-process/high-score gate remains pending.

## Reading planner performance reports

`python scripts/analyze_planner.py artifacts/planner-four-a.jsonl.gz --out artifacts/planner-four-a-analysis.json` streams only the file prefix present at startup, including an unfinished gzip stream. It reports logical job/action/stage durations per chef, idle and observed control-suppression time, native deliveries, region transitions, service-wave sizes, time-weighted inventory, vessel empty/ready/warning intervals, native burnt/overmixed labels and the first planner failure. Examples are bounded and no frame snapshots are retained. Seven offline fixtures check interval accounting, pending native event finalization, frame resets, corrupted versus unfinished gzip input, fixed-prefix reading, and warning-versus-burn interpretation.

Inventory is held constant between observed samples; consult the reported largest frame gap. Idle-with-inventory labels overlap and do not establish causality. A movement-stall flag is an explicit observed-input/low-motion heuristic, and a cooking warning is not a burnt-food event. Reports label a trace as partial unless native timer zero and both inactive rounds are actually observed.
