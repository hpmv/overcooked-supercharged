# Concrete controller migration seam

The smallest working foundation is now `framework/headless`: original framework reconstruction and action/input histories behind a command-line session, with no Blazor dependency. It links upstream source rather than copying or rewriting the framework engine. Native integration and actual Carnival 3-4 discovery remain separate acceptance gates.

## Coupling that the adapter must preserve

The current `controller/CarnivalPlanner.cs` accepts `Func<JsonObject, Task<JsonObject>> call`, but that transport seam is not a complete state adapter. `RunAsync` requests an initial native `inspect`, gets a restored native recipe preview, observes each completed step, advances persistent vessel/plate/transport leases, completes `Work` actions and callbacks, selects work, then emits four `RouteRunner.Tick` inputs through a one-frame `step`. The planner's many partial files share exact native IDs, registration/observation identity, resource sets, action references, and native timing fields.

`controller/Protocol.cs` uses length-prefixed JSON on the legacy ports. Framework `Connector.cs` instead dials port 14455 and serves `Interceptor.getNext` over that established Thrift connection. Native output messages drive reconstruction and immediately require the next controller input. A polling replacement for `call` would need to queue a full exchanged frame and must preserve the planner's completion-neutral-frame convention; it cannot independently advance native frames.

The framework's strongest seam is `RealGameSimulator.ComputeInputForNextFrame`: it steps `GameAction` nodes, updates dependency/ownership predictions, transforms desired controls through `ControllerState`, and records actual controller history. `ApplyGameUpdate`, registry early/late handling, and versioned `GameEntityRecord` state provide the observation side. Dynamic native IDs map to stable `EntityPath` values rooted at their actual spawner; use this mapping, not invented old JSON identities.

## Minimum next adapter modules

1. **Carnival discovery setup importer.** Build `GameSetup`, exact initial entities/prefabs, and per-chef maps from the actual installation's discovery. Verify initial native ID/name/component/position matches before using an upstream annotation. The existing `Carnival34FourLevel` is a bootstrap only. Its duration and other configuration annotations must be checked against level-specific native fields; a class default is not the level's configured value.
2. **Decision-step boundary.** Separate the legacy planner's observe/complete/select portion from its async transport loop. Given one completed framework/native observation, produce framework action-graph changes or four desired inputs. Keep the original action engine as the executor. Port a small production route first; do not synthesize missing legacy telemetry values merely to make every old partial compile.
3. **Native evidence side channel.** Maintain actual score/order/timer/registration/RNG-policy receipts alongside reconstructed state. Framework automatic progress and upstream future-order annotations are not native proof. The previous validator's exact old plugin/hash/hook schema cannot truthfully identify the replacement plugin.

An interim JSON state projection is viable only for fields backed by actual framework messages or an explicitly reviewed native bridge observation. Legacy navigation and action guards currently require native colliders/layers, capsule dimensions, controls and input-suppression flags, exact interaction/placement targets, rigidbody and cached velocity, cooking/mixing progress, registration identities, and recipe trees. Many are absent or differently represented. A broad default-filled projection would silently weaken the proven guards.

## Reusable source and evidence

| Existing material | Retain or port |
| --- | --- |
| `controller/CarnivalRecipes.cs` and recipe fixtures | Carnival ingredient/preparation/recipe mapping and expected finished-meal checks, bound to actual native recipe discovery. |
| `controller/KitchenModel.cs`, `Navigation.cs`, scene discovery artifacts | Actual role/region/collider geometry and proven clearances; framework map import or independent checks, rather than simultaneous competing movement engines. |
| `controller/CarnivalPlanner.cs` and `Carnival*.cs` policy partials | FIFO priorities, exact resource leases, native deadline obligations, plate/food identity rules and demonstrated failure fixtures. Extract decisions incrementally. |
| `RouteCannon`, `RouteThrow`, preparation and vessel lifecycle fixtures | Proven native action ordering and identity/arrival/consumption conditions; express using the framework graph plus missing native observations. |
| `Verification.cs`, `NativeProbeCoverage`, native probe routes/checkers | Acceptance criteria for fresh process, native 270-second round, four chefs, native recipes/score, exact input replay, physical interactions and final screenshot. Rebind provenance to the actual replacement instrumentation. |
| Frozen native movies and manifests | Historical regression evidence; do not label them evidence that a new framework binary has passed. |

## Native optimizer seam and cost

`scripts/optimize_native.py` already separates candidates, immutable controller hashes, observed configuration, per-trial artifacts, ranking, and final qualification. `evaluate()` launches the controller's `bot --restart` for every candidate and waits for a full native round or explicit delivery checkpoint. Initial seeds and neighboring policies run serially; there is no shared simulation or checkpoint suffix reuse. Every legacy action frame produces full JSON state and a compressed trace. The expensive unit is a fresh native candidate rollout, not the inexpensive Python neighbor enumeration.

The rank is full target qualification, requested-run completion, native score, delivered count, gameplay frame, and stable candidate tie-break. Wall time is not optimized; a reconstruction score or a prefix result cannot replace a completed native-round result. The runtime's actual configuration must match the candidate, and failed trial observations do not qualify a run.

The minimal optimizer change is a backend for `evaluate()` that drives a headless framework session and produces the same candidate/result/provenance contract. Message deltas and versioned histories can replace repeated full JSON polling. Framework checkpoints can later support explicitly labeled **development** suffix trials; winners still require untouched fresh native runs and the independent native validation gate. Preserve existing candidate/rank/artifact logic until measured evidence justifies changing the search objective.

No games were controlled for this review. The headless offline tests verify protocol/lifecycle/action/checkpoint behavior only; native movement, discovery fidelity, RNG compatibility, and scoring remain integration work.
