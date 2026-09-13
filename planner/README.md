# Overcooked TAS controller

Build with `dotnet build controller -c Release`. Run with `dotnet run --project controller -- <command>`. The isolated game and TAS plugin must already be running. The client connects only to a numeric loopback address, default `127.0.0.1:17634`.

`dotnet run --project controller -- selftest` tests framing, fragmented/truncated reads, state comparison, four-player release, action button duration, resource collisions, and dependency scheduling without the game.

`dotnet run --project controller/protocol-tests/ProtocolJsonTests.csproj -c Release` compiles the actual plugin JSON reader/writer against minimal DTO stubs and independently checks nested arrays, Unicode, culture, strict request validation, and malformed input handling. The game-side reader requires `version: 1`, a command, and an explicit unique player index on each input entry; omitted axes/buttons default to neutral.

## Calls and recordings

Use `inspect`, `setup`, `load --seed 1`, `restart --seed 1`, `pause`, `resume`, `step --steps 60`, and `screenshot --path <absolute-path>`. `setup` initiates asynchronous player/session setup; inspect and advance until it completes. `load` and `restart` wait atomically until both native server and client rounds activate, then return paused at `gameplayFrame: 0`. Their returned snapshot establishes the gameplay origin; do not add arbitrary post-load stepping to a reproducibility probe. `call --file request.json` sends any protocol request without a new client implementation. Add `--out recording.jsonl.gz` to any command to save requests and responses. TCP framing is a four-byte little-endian byte count followed by UTF-8 JSON; request and response versions are 1.

A request script is JSONL, one complete request per line. For example, a single frame moving chef zero in positive world X, with all other inputs neutral:

Recordings and scripts ending in `.jsonl.gz` transparently use gzip compression. Prefer this format for full-frame telemetry. Input and output paths must differ. Raw `.jsonl` remains supported for editing probes.

```json
{"version":1,"command":"step","steps":1,"inputs":[{"player":0,"x":1,"y":0,"pickup":false,"use":false,"dash":false},{"player":1,"x":0,"y":0,"pickup":false,"use":false,"dash":false},{"player":2,"x":0,"y":0,"pickup":false,"use":false,"dash":false},{"player":3,"x":0,"y":0,"pickup":false,"use":false,"dash":false}]}
```

- `record --script probe.jsonl --out first.jsonl` executes requests and records full responses.
- `replay --file first.jsonl --out replay.jsonl` resends recorded requests and fails at the first observed gameplay-state difference. `--no-compare` only replays inputs.
- `compare --expected first.jsonl --actual replay.jsonl` compares recordings offline.
- `diagnose --script probe.jsonl --runs 20 --out diagnosis.jsonl` repeats a script beginning with atomic `restart` or `load` (or `setup` plus explicit asynchronous readiness steps). It reports the first difference per run. It does not restart the executable or claim fresh-process validation.

Comparison uses exact numeric values and stable property ordering. It ignores no gameplay field by default. `--ignore '$.frame,$.fixedFrame'` excludes explicitly named fields relative to `response.state`; include every exclusion in the reproducibility report. Do not hide gameplay divergence with exclusions. Entity array order and native IDs remain significant until a separately validated canonical entity mapping exists.

`--timeout 600` bounds the entire operation. Cancellation closes the socket; the plugin must release inputs on connection loss. The controller deliberately does not send another stepping request while an earlier request is pending.

## Adaptive route authoring

`route --file route.json --out route-trace.jsonl` executes phases. Each phase contains one independent action per participating chef, stepping every complete Unity frame. Missing chefs receive neutral inputs. Actions run concurrently; the next phase begins after all complete, with one release frame between phases.

```json
{"phases":[
  {"name":"approach","actions":[
    {"player":0,"type":"navigate","target":{"x":2,"z":-3},"tolerance":0.15,"timeoutFrames":180},
    {"player":1,"type":"navigate","targetEntityId":123,"offset":{"x":0,"z":1},"dash":true}
  ]},
  {"name":"pickup","actions":[{"player":0,"type":"pulse","button":"pickup","requireTargetId":123}]},
  {"name":"work","actions":[{"player":0,"type":"hold","button":"use","durationFrames":180}]},
  {"name":"observe","actions":[{"player":0,"type":"wait","path":"score","atLeast":100,"timeoutFrames":120}]}
]}
```

This example is syntax documentation, not a valid route for a discovered level. Resolve real positions and IDs from telemetry. Carnival navigation derives a collision-free A* path from the live snapshot and follows cached waypoints; it replans after a blocked segment, moved target, or stalled movement. Other scenes retain the explicit segment behavior. It uses world X and negative world Z for input axes; `invertX` and `invertY` allow camera-dependent calibration. `face` points toward a target for `durationFrames` (default 3), returning neutral once native facing agrees. `pulse` lasts one frame; `hold` lasts the specified duration. `wait` accepts a frame duration or a dotted state `path` with `equals` or `atLeast`, plus optional `stableFrames`. Target-checked button actions stop before sending input if the observed target differs. For plate placement, set `targetField` to `placementTargetId`. Failed actions record the complete observed state and release inputs; timeouts send neutral input before terminating.

`navigate` and `face` also accept `"station":"<selector>"`. Navigation selects a reachable approach and resolves its current entity ID from the live snapshot. Group aliases choose the nearest reachable candidate on the chef's platform. Supported aliases include `plates`, `cookpot`/`pots`, `pans`, `mixers`/`bowls`, `baskets`, `chopboards`, `hotdogcrate`/`buns`, `sausages`, `onions`, `cookers`, `mixing-stations`, and `fryers`. `mixers` means movable mixing bowls; `mixing-stations` means appliances. Explicit role/location keys select one station.

High-level transfers use the same navigation and resolved station:

```json
{"phases":[
  {"actions":[{"player":2,"type":"take","station":"hotdogcrate"}]},
  {"actions":[{"player":2,"type":"place","station":"chopboards"}]}
]}
```

`take` requires empty hands and accepts any observed newly held entity, including a crate-generated ID that was not known beforehand. `place` requires a held entity and waits for empty hands plus a change to the target attachment, contents, plate count, or delivery score. Both navigate, face the measured entity, verify the native pickup/placement target (including attachment referrals), issue one pickup edge, and observe completion. Missing interaction targets retry alternative legal approaches; the default is at most six retries and the action's normal frame timeout. Use `maxRetries` or `timeoutFrames` to tighten bounds. Every stage, path replan, native target check, retry, and completion is recorded. These are adaptive actions; replay their emitted recordings without feedback to assess exact-input reproducibility.

`combine` (also named `apply`) uses the same verified interaction, requiring the held entity ID to remain unchanged while material contents or attachments change. This supports scooping food onto a held plate, pouring a held bowl into another vessel, and applying condiments. Optional `expectedIngredient`, `expectedIngredientId`, `expectedIngredientIds`, or `expectedRecipeId` constrain the observed result. Cooking timers and food state labels alone do not count as transferred material. An unexpected drop or item exchange fails.

`assemble` adds the game's native plate-under-food behavior: if the original plate becomes attached to the verified station and its material composition changes, the action releases pickup, waits until the native pickup deadline, then picks that same plate up again. `combine`/`apply` can opt into this behavior with `recoverPlaced: true`. Recovery rejects a plate dropped on the floor, a different held item, or a mismatched requested result. The misleading native field `lastPickupTimestamp` stores the next eligible client time; both ordinary empty-hand pickup and assembly recovery compare against it directly.

`chop` selects an occupied chopping board, navigates and verifies its native use target, holds the use button, and waits for the raw workable item to be replaced by observable chopped food. `cook` and `mix` emit neutral input while waiting on a nonempty vessel's native progress and classified food state. They accept a `station` alias or exact `entityId`; they do not change cooking or mixing time. Preparation fails if its vessel disappears, empties, becomes ruined, or makes no progress for `maxStallFrames` (default 300), subject to the overall action timeout.

Mix completion accepts the native mixed stage beneath a raw cooking wrapper, which represents the later frying step. Chopping and washing inspect `useSuppressed` after verifying their native station target. When suppressed, they send one legal press/release and verify the flag clears before holding use. This handles native session `ClearEvents` behavior without changing control flags directly.

`wash` requires empty hands and dirty plates already placed in the selected `station` (default `sink`). It navigates, verifies the native use target, and holds use until `count` plates are washed; omitting `count` washes the initial sink contents. Completion requires both dirty sink count decreases and clean plate count increases at the sink's native `washingOutputEntityId`. A reset progress timer alone cannot complete the action. Reserve both sink and drying output during washing so concurrent plate removal cannot hide the count evidence. The telemetry exposes `washingTime`, actual `plateCount`, `plateStackEntityId`, and `plateStackKind` for sinks, return stations, dirty/clean stacks, and individual clean plates.

If washing has a verified target but no progress or server interaction after `reacquireAfterFrames` (default 20), it retreats through collision-free legal inputs, observes the target and prediction clear, and approaches again. `maxReacquireAttempts` defaults to 2. Every suppression decision, target clearing, and retry is recorded; `clientPredictedInteractionId` and `serverInteractionId` distinguish client prediction from server acceptance.

`switch-condiment` accepts `index` or `expectedIndex` of 0 or 1. It requires empty hands: native controls only scan world use targets when no item is held. It resolves the condiment switch and dispenser by their validated scene roles, verifies the native use target, emits a fresh use edge, and waits for the dispenser's native `switchIndex` to match twice. It emits no toggle if the desired index is already selected. Explicit `station` and `dispenser` selectors can select their measured keys. Omitting the desired index toggles the initially observed selection.

For independent per-chef queues, use `runner.CreateAction(spec)` and call `runner.Tick(handle, stateOrResponse)` once per logical simulation frame. Tick performs no network call. It returns one chef's input and exposes `IsDone`, `Error`, `ElapsedFrames`, `Stage`, and the current `RuntimeEntityId` on the handle. Apply every returned input, including the neutral release input from the tick that sets `IsDone`. Replace the completed handle on the following frame. Check `Error` before treating completion as successful. This interface lets the scheduler keep other chefs working without global phase gaps.

Transport actions are `aim-cannon`, `board-cannon`, `fire-cannon`, and `portal`. Cannon selection accepts `cannon: "left"`, `"right"`, a current native ID, or a measured station key; aiming defaults to the native downward limit, with optional absolute `targetAngle`. Portal selection uses a sender on the current platform. These actions verify native linked controls, loaded/passenger identity, launch/receiving state, control restoration, and destination region while preserving held items. They require expanded native telemetry and emit ordinary input only. Firing and riding are coordinated actions on separate chefs; the fire action observes the passenger's arrival.

Successful adaptive routes emit exact input traces; use `replay` to test those traces without state feedback. A successful adaptive execution alone is not evidence that its exact replay will work.

## Scheduling

`schedule --file tasks.json` produces a deterministic schedule with resource and chef reservations. Input is an array of tasks with `id`, `dependencies`, `resources`, eligible `chefs`, `durationFrames`, and optional `priority`. Resources are arbitrary stable keys such as `station:123`, `plate:456`, or `cannon:landing-west`. Dependency cycles and missing dependencies fail. Scheduling selects the earliest feasible start, then priority, finish time, task ID, and chef ID. These estimated schedules are authoring tools; actual route progression must use measured state and durations.

This component provides protocol, diagnostics, playback, adaptive action execution, and reservation scheduling. It does not contain a completed high-score Carnival route or claim a verified score.

## Measured Carnival kitchen map and navigation

These commands are offline and do not connect to or advance the game:

```powershell
dotnet run --project controller -c Release -- map --file artifacts/kitchen-first.json --out artifacts/kitchen-map.json
dotnet run --project controller -c Release -- path --file artifacts/kitchen-first.json --player 0 --target 23,-14
dotnet run --project controller -c Release -- path --file artifacts/kitchen-first.json --player 2 --station HotdogBun
dotnet run --project controller -c Release -- navigation-test --file artifacts/kitchen-first.json
```

`KitchenModel.Build` derives five platform polygons for `s_Day_3_4` using the four observed countertop corner columns, the upper/lower counter rows, cannon floor edges, and lower portal edges. It validates the expected topology and refuses incomplete or changed geometry. It does not join the outer platform gaps into a large rectangular walking area. The measured grid pitch is 1.2 units, and the observed chef capsule radius is approximately 0.4 units; the planner adds 0.03 units of clearance.

Station mappings include the current native entity ID, role, ingredient prefab, validated components, measured location, and approach points. The role/name/location key re-resolves stationary stations across process starts without assuming their entity IDs. Movable cookware keys describe the observed location and should not serve as persistent identities after relocation. `--station` accepts an exact key, unique role, ingredient prefab, or current snapshot entity ID; ambiguous selectors fail with candidates.

Active solid child colliders supply station footprints when available. Earlier snapshots fall back to known station trigger AABBs. Duplicate collider hierarchy/geometry entries, `_Rigidbody` containers, held-object hierarchies, and carried-item layer 15 are excluded. The chef radius comes only from active controlled chefs' root capsules; carried food and sideways disabled cannon passengers cannot enlarge that radius. Navigation uses a 0.15-unit A* grid and true swept-circle checks around rectangular obstacles, preserving free space around rounded corners. A chef already resting at native collider contact may depart outward using its physical radius with a 2 mm numerical allowance; new obstacles retain the full clearance margin. It accounts for other observed chefs by default; `--ignore-chefs` plans against static geometry only.

The native game normalizes every nonzero movement vector, and physics consumes velocity cached during the previous logical update. The follower therefore sends full directional input, predicts the pending displacement from observed `lastVelocity * fixedDeltaTime` when the measured next phase contains physics, then releases before arrival and waits for two settled observations. It does not attempt analog speed reduction. Regression tests cover all six starting phases of the 60 Hz logical/50 Hz physics relationship. Queued moves repeatedly blocked by native collision trigger a replan and fail after `maxFailedBrakes` (default 6), with an explicit diagnostic. Missing paths replan every ten frames, report concrete candidate failures, and fail after `maxNoPathFrames` (default 180) instead of idling through the whole action timeout.

`"dash": true` optionally enables native dash edges on long straight segments. The controller requires observed native speed, duration, cooldown, surface, control, and clock telemetry; absent or unsupported data leaves walking enabled. It predicts the native sinusoidal speed blend and queued physics across all six clock phases, requires sufficient stopping space and heading alignment, checks other chefs' projected motion, and observes the native dash timer and velocity throughout. It never changes movement parameters. Neutral input does not immediately stop an active native dash, so it retains the coasting distance when braking. `dashMinDistance` adds an optional minimum; it cannot override the computed native travel bound. `dashSafetyMargin` defaults to 0.15 units and has a 0.05-unit minimum. Decision reasons, requested edges, native acceptance, completed displacement, peak observed speed, and interruptions are traced. Native game validation is still required before relying on dash timing in a production recording.

Cross-platform walking fails explicitly and returns available transition sources. Portal graph edges use native destination IDs, sender initialization, and teleport transforms; upper receiver-only portals are not walk-in sources. Cannon graph edges expose the current native landing target and barrel angle. A verified configured endpoint is distinct from a successfully executed flight. Open island floor edges extend half the measured tile pitch past the last tile center, admitting native cannon landing cells while retaining the void gap. `navigate` consumes this model directly and checks live interaction targets through the transfer actions.

`route-test --file artifacts/kitchen-first.json` simulates movement and native interaction observations against the captured kitchen without controlling the game. It tests an unknown generated ingredient ID, take/place edges, target attachment evidence, refusal to press on an incorrect target, and input release after failure. This test validates the controller state machine; it does not prove native game interactions work.
