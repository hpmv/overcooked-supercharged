# Four-chef Overcooked 2 TAS

Independent controller and instrumented local runtime for Carnival of Chaos 3-4. Target: five fresh-start runs of at least 5,000 points with four native local chefs and the native 270-second round. Development and score validation are still in progress; a completed high-score TAS is not yet verified.

The current combined probe prepares and delivers a mustard hotdog, prepares a chocolate doughnut, uses both cannons and a return portal, washes and returns a plate, throws directly into cookware, and catches an ingredient with another chef. It emits a recording of every logical input frame. Diagnostic dashes and collisions are appended through a checked, continuous neutral boundary.

## Build and launch

Requirements: the user's installed Steam build 20236421, its BepInEx 5.4 core, PowerShell 7, .NET SDK 10, and Python 3 for analysis. The game uses Unity 2017.4.8 and x86 Mono. The plugin is compiled against its .NET 3.5 assemblies; the controller targets .NET 10.

```powershell
Set-Location M:\projects\game-test-2
.\scripts\Setup.ps1 -GamePath 'K:\trash\Steam\steamapps\common\Overcooked! 2'
.\scripts\Build.ps1
.\scripts\Launch.ps1
$tas = '.\controller\bin\Release\net10.0\OvercookedTAS.Controller.dll'
dotnet $tas call --file routes\probes\start.json --compact > artifacts\start.json
dotnet $tas call --file routes\probes\render-fast.json --compact > artifacts\render.json
```

Setup copies the original runtime/assets into `runtime`; saves are redirected into `profile`. Original game files and mods are retained. Launch refuses any existing Overcooked2 process. Close the isolated game using `dotnet $tas quit` before rebuilding its plugin. Game/assets and copied dependencies remain local and are excluded from source delivery.

## Control and diagnostics

Protocol version 1 uses loopback TCP port 17634 with a four-byte little-endian byte length followed by UTF-8 JSON. Commands include `inspect`, `load`, `restart`, `pause`, `step`, `render`, `preview`, `screenshot`, and `quit`. Four chef inputs contain player index, x/y movement axes, pickup, use/throw, and dash. All input buttons go through the game's logical input classes and event claims. Disconnect releases inputs.

```powershell
dotnet $tas inspect --compact > artifacts\state.json
dotnet $tas plan --restart --seed 0 --isolate-recipe-random --file routes\probes\production-cycle.json --out artifacts\probe.jsonl.gz --compact > artifacts\probe-result.json
python scripts\extract_inputs.py artifacts\probe.jsonl.gz --out artifacts\probe-inputs.jsonl.gz
dotnet $tas replay --file artifacts\probe-inputs.jsonl.gz --no-compare --out artifacts\replay.jsonl.gz --compact > artifacts\replay-result.json
python scripts\analyze_repro.py artifacts\probe.jsonl.gz artifacts\replay.jsonl.gz --map-initial-rigidbodies --out artifacts\comparison.json
dotnet $tas screenshot --path M:\projects\game-test-2\artifacts\score.png --compact > artifacts\screenshot.json
```

`--no-compare` plays the immutable input movie without feedback or corrections. It does not assert reproducibility; the subsequent analyzer reports strict state, raw physics, clocks, gameplay and native event differences separately. Adaptive actions record their guard decisions and all emitted inputs. No state restoration or position writes are used.

The best completed authored adaptive run is **V21: 2,720 points from 25 native deliveries** over the native 270-second round, with 2,040 base points, 680 tips and no deductions. It completed after a native kitchen restart in the existing lab process. **V21 exact-input replay is untested**; this is not a fresh-process validation. See the [completed-round report](docs/V21-COMPLETED-ROUND.md), [native score audit](artifacts/native-round-v21-native-score-audit.json), and [frozen controller validation](artifacts/planner-candidate-v21/validation.json).

The earlier V16 round scored 2,612/24; its [report](docs/V16-COMPLETED-ROUND.md) and [extracted input manifest](routes/probes/v16-completed-inputs.jsonl.gz.manifest.json) remain available. Its exact-input replay is also untested.

The earlier completed authored V14 movie scores **2,042 points from 20 native deliveries** over the native 270-second round: 1,600 base points plus 442 tips, with no deductions. Its adaptive planner prefix failed with 11.3 seconds left after an idle chef caught a sausage intended for a pot; a separately authored 700-frame neutral continuation reaches native round end. The failed trial remains unchanged. One fresh-process playback of that composed movie reproduced all 16,226 requests and pad snapshots, current-round recipe RNG, the 20 delivery events and their native timing, and the final score. See the [completed replay report](artifacts/v14-completed-replay-a-report.json) and [movie manifest](artifacts/v14-completed-movie-manifest.json).

The [original full comparison](artifacts/v14-completed-replay-a-comparison.json) retains raw physics and clock/observation differences. Its first 15,524 gameplay/event samples match directly; all 702 continuation samples match only under the [explicit additional projection](artifacts/v14-completed-replay-a-pinned-neutral-join-projection.json) removing the three complete, manifest-pinned neutral disconnect records from expected observer history. No other event is excluded. An earlier eight-delivery movie separately reproduced 864 points across 6,023 samples (`artifacts/planner-eight-exact-analysis.json`). These are development checkpoints; **zero fresh runs qualify for the 5,000-point target**.

```powershell
# Run a bounded startup trial, or omit --stop-after-deliveries for the full round.
dotnet $tas bot --restart --seed 0 --stop-after-deliveries 8 --out artifacts\candidate.jsonl.gz --compact > artifacts\candidate-result.json
python scripts\analyze_planner.py artifacts\candidate.jsonl.gz --out artifacts\candidate-analysis.json
# Observe a running recording without connecting to or advancing the game.
python scripts\watch_trace.py artifacts\candidate.jsonl.gz --out artifacts\candidate-live.json

# Rank recorded native recipe sequences with explicitly measured cost assumptions.
dotnet $tas optimize-seeds --file artifacts\native-seed-previews.jsonl --costs artifacts\probe-calibrated-costs.json --out artifacts\seed-candidates.json

# Actual game evaluations: deterministic beam/local parameter search with a fixed trial budget.
# Each trial restarts the kitchen. Use a new output directory for every search.
python scripts\optimize_native.py --controller $tas --out artifacts\native-search --seeds 0,8 --beam 2 --rounds 1 --budget 6 --deliveries 8
```

`optimize-seeds` produces a surrogate ranking, not a native score. `optimize_native.py` records actual trial scores, emitted inputs, failures, and completion evidence. Its default compares delivery prefixes; `--full-round` evaluates native round completion. Neither replaces the five-fresh-process final verification gate. Planner options include `--short-dash`, `--cooperative-sauces`, `--bun-buffer`, `--early-onion`, `--parallel-onion`, `--pantry-chop`, `--lookahead`, `--service-batch`, and `--no-direct-throws`. These are candidate policies, not guaranteed high-score settings. Native probes separately verified cooked-onion storage off heat, mixed-dough storage off the mixer, and pantry-side chopping of buns, onions, chocolate and raspberries.

The frozen V10 candidate also supports `--staged-release`, `--conditional-far-pot`, `--pre-service-stock`, `--bakery-lookahead 12`, `--preempt-cannons`, and `--service-side-head`. These release proven completed station uses, select demonstrated clear throw lanes, refill before departure within a bounded native order/tip window, prepare future dough, fire cannons at completed action boundaries, and stage FIFO meals on the serving side. Each feature is off by default and recorded in the native trial's observed planner configuration. Immutable controller bundles under `artifacts/planner-candidate-vNN` keep ongoing tests independent of later source edits.

V11 adds `--sausage-buffer 1` (or `2`), `--share-pantry-chop` with `--pantry-chop`, and `--wait-ready-head`. They reserve exact raw sausages for empty pots, transfer a completed raw placement to a free chopping helper, and briefly keep the server near an imminent complete FIFO plate. Native fryer rescue parks an exact cooked basket until a plate is available, then restores the empty basket to its original stove. The native fryer and pot probes each separately held cooked food off heat for over 13 seconds and completed normal food transfer and original-vessel restoration. These mechanism proofs do not establish full-route performance; V11 still encountered a pot deadline failure.

Frozen V13 adds common native heat arbitration and a bounded recovery that returns an urgent harvested hotdog to its own emptied bun board when ordinary counters are full. That recovery completed in the native game. Its baseline attempt stopped later at 876 points after nine deliveries because a parked pot changed the current-position near/far classification of a throw target; this is a failed development attempt. V13 passed 700 captured-fixture assertions and 545 core assertions (`artifacts/planner-candidate-v13/validation.json`).

V13 also supports default-off `--near-sauce-stage` and `--release-firer`. Matched native sauce branches took 668 versus 360 frames using the original counter37 versus nearby counter40. The cannon mechanism released the firing chef 51 frames before two verified passenger-arrival samples and observed ordinary movement during flight. These are bounded mechanism results; integrated planner validation remains separate. See `docs/NEAR-SAUCE-STAGING.md` and `artifacts/cannon-fire-release-a-proof.json`. A neutral round-end diagnostic independently confirms the native 270-second timer, server stop at frame16201 and client stop at16202 (`docs/NATIVE-ROUND-END.md`); it scored −150 and does not qualify as a TAS score run.

Frozen V14 fixes pot and bowl throw selection using their original processing homes, guards the original target throughout a throw, and can wait for nearly mixed complete dough before transferring directly to a free fryer. It also adds a bounded direct leased-onion harvest and default-off `--short-dash-vessels`, which applies ordinary short-dash navigation to cookware approach/transfer actions while retaining native waits and conservative walking budgets. Four native workflow probes passed; actual dash while carrying cookware was observed for the bowl only. Details and hash-bound evidence are in `docs/V13-SUPPLY-TOPOLOGY.md`, `docs/NEAR-READY-DOUGH.md`, `docs/OFFHEAT-CHECKERS.md`, and `artifacts/vessel-short-dash-probe-results.json`. V14 passed 859 fixture assertions and 545 core assertions. Its full-round native trials remain development experiments.

The V15 adaptive trial stopped at 1,800 points after 17 deliveries, with 58.774 native seconds remaining, when an incomplete bowl reached the unchanged 21-second safety guard. Frozen V16 adds a preventive supplier rule before departure for washing and a bounded return path for an active partial mix. The completed V16 run above enabled its default-off `--near-ready-pot` and `--washer-side-plating` options. This successful adaptive round is below the target and does not isolate any feature's performance contribution. See [the V15 failure and correction](docs/V15-BAKERY-DEPARTURE-CYCLE.md), [near-ready pot evidence](docs/NEAR-READY-POT.md), and the [V16 frozen manifest](artifacts/planner-candidate-v16/manifest.json).

Freeze a new candidate before running it while source development continues:

```powershell
python scripts\freeze_controller.py --candidate my-candidate --out artifacts\planner-candidate-my-candidate
$tas = '.\artifacts\planner-candidate-my-candidate\OvercookedTAS.Controller.dll'
```

The freezer builds from the captured `source/controller` directory and saves the source tree, build log, and binary hashes. It refuses an existing output directory. A successful build is recorded separately from controller tests and native score verification.

## Instrumentation and validation

The game runs 60 Hz logical updates with native 50 Hz physics. The main thread pauses only at complete frame boundaries. Native `FixedUpdate` and physics run normally. A measured start gate waits for a natural no-physics frame before native kitchen activation. Client/server real-time clock sources use the logical clock so debugger pauses do not consume gameplay time.

The isolated recipe RNG wraps the native weighted generator, preserving its algorithm and saving/restoring unrelated Unity RNG state. The `preview` command invokes that generator on independent round data and verifies restoration of live RNG, frequency counters, observers and frame numbers. Ingredient rules, movement, collision, preparation durations, order deadlines, delivery matching and scoring remain native.

```powershell
# Requires a completed per-frame movie and its extraction manifest.
.\scripts\Verify.ps1 -Movie routes\probes\complete-probe-inputs.jsonl.gz -Mode 20
# Only a completed high-score movie qualifies for this gate:
.\scripts\Verify.ps1 -Movie routes\successful-tas.jsonl.gz -Mode 5
```

`Launch.ps1` and `Verify.ps1` accept optional `-AllowIsolatedLab` when running a separate planner experiment concurrently. This permits only one kernel-verified `lab\runtime\Overcooked2.exe` in this workspace, records its identity as concurrent rendering/CPU load, and never controls or stops it. Other game processes and a listener already occupying the primary control port are rejected. Omit the switch for strict isolation. `-PlanOnly` performs read-only preflight; resume must retain the same concurrent-lab policy.

The current instrumented runtime passed the 20-run gate with five fresh process starts (`artifacts/gate20-c-summary.json`), classified as exact inputs with stable native events and differing recorded physics. The final gate requires five native completed rounds scoring at least 5,000, identical input requests and delivery sequences, and varied rendering loads. See [validation details](docs/VALIDATION.md). Build manifests, native score events, screenshots and comparisons are stored under `artifacts`. Video is omitted.

Technical references: [overcooked-supercharged](https://github.com/hpmv/overcooked-supercharged), [GUA's TAS tool](https://github.com/gua248/Overcooked2-TAS), and [Unity's Physics.Simulate documentation](https://docs.unity3d.com/2017.4/Documentation/ScriptReference/Physics.Simulate.html). The new bot and route are implemented independently.
