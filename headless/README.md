# Headless framework session

This .NET 10 executable reuses the upstream `RealGameConnector`, `RealGameSimulator`, entity reconstruction, action graph, controller history, navigation, and protobuf checkpoint format. It does not start the Blazor application. Its only network endpoints are configurable loopback ports.

The default `Carnival34FourLevel` setup is **unvalidated upstream geometry and prefab metadata**. The upstream README calls levels other than Carnival 3-2 outdated. A successful connection or reconstructed state does not establish correct Carnival 3-4 mechanics, native scoring, RNG behavior, or replay qualification.

## Build and offline checks

From the workspace root in PowerShell:

```powershell
$env:NUGET_PACKAGES = 'M:\projects\game-test-2\framework\.packages'
dotnet restore framework/headless/Headless.csproj
dotnet build framework/headless/Headless.csproj -c Release --no-restore -o artifacts/framework-headless-host-new
dotnet artifacts/framework-headless-host-new/Headless.dll selftest
```

Choose a new output directory when a previous host is running. The project generates its own .NET Thrift bindings under `headless/Generated` using the bundled compiler, and generates the original `save.proto` bindings. Linked upstream source remains unchanged. The separate game patch uses its own compatible Unity dependencies.

## Native session operated by the integration owner

Start the host **before** a fresh bridge load/restart, so it observes the complete level-load and registration stream. A late connection is not automatically repaired from the hardcoded setup. Keep the bridge's owning TCP connection open; losing that connection deliberately pauses and neutralizes the game. Game launches must remain windowed.

```powershell
dotnet artifacts/framework-headless-host-v2/Headless.dll serve --game-port 14455 --control-port 17637 --level carnival34 --evidence-root artifacts/framework-headless --trace artifacts/framework-headless/session-new.jsonl
```

Use a separate terminal for the following commands. The bridge controller uses its own framed JSON protocol on port 17636. After a successful paused bridge load, issue **bridge `arm`** on the persistent bridge owner connection; it releases the input fence while leaving native time paused. Then the headless host owns phase-aligned resume.

```powershell
dotnet artifacts/framework-headless-host-v2/Headless.dll inspect --control-port 17637 --full
dotnet artifacts/framework-headless-host-v2/Headless.dll checkpoint --control-port 17637 --path before-move.pb
dotnet artifacts/framework-headless-host-v2/Headless.dll step --control-port 17637 --frames 10
dotnet artifacts/framework-headless-host-v2/Headless.dll pause --control-port 17637
```

`step` accepts 2–36000 advancing reconstructed frames and uses the original two-callback pause handshake. Commands acknowledge queued requests; inspect until the request is settled. A command arriving while another transition is pending is rejected.

For a bounded movement test, select a nearby destination from the observed chef position and validated clear floor:

```powershell
dotnet artifacts/framework-headless-host-v2/Headless.dll goto --control-port 17637 --chef 103 --x 20 --z -14 --frames 120
```

Those coordinates illustrate syntax and are **not a validated test destination**. The command requires a paused observed chef, no unfinished graph actions, and a path in the supplied framework map. It adds the original walking `GotoAction`, resumes through the original physics phase handshake, and requests pause on completion or the 2–600 frame limit. `movementCompleted=false` distinguishes a bounded stop from arrival. An unfinished action remains in the graph; a later explicit `resume` continues that graph. No dash or pickup/use input is added by diagnostic `goto`.

`inspect --full` includes the exact received registry and the reconstructed entity paths, positions, progress, semantic data, chef state, and action graph. Progress fields belong to the framework's versioned reconstruction, which can include automatic progress prediction. They are not substitutes for independent native telemetry.

## Checkpoint, warp, and offline reconstruction

Checkpoint requires a settled paused fresh-level session. It writes the original framework `GameSetup` protobuf and a SHA-256 receipt under the evidence root, using create-new semantics. Loading a protobuf with `serve --setup checkpoint.pb` loads its setup/history; it does **not** synchronize or warp the native game by itself.

```powershell
dotnet artifacts/framework-headless-host-v2/Headless.dll warp --control-port 17637 --frame 100 --development
dotnet artifacts/framework-headless-host-v2/Headless.dll reconstruct --input artifacts/framework-headless/session-new.jsonl --full
```

Warp requires explicit development mode, a paused session, a target within observed history, and no framework warp-critical section at either endpoint. `warpUsed` remains true afterward. Warped candidate development must be kept separate from fresh native validation.

## V3 actual registry validation and import

V3 adds `registryValidation` to inspection, and retains `initialRegistry` separately from later metadata refreshes. All 122 actual native-d initial positions match upstream within 0.000012 metres; the validator uses a 0.0001 metre tolerance, exact captured native names, and observed component requirements. It records the framework's eight explicit cannon synchronizer substitutions. Additive post-load component/spawn metadata may refresh; it cannot replace the first registration pose or erase required component identity.

```powershell
dotnet artifacts/framework-headless-host-v3/Headless.dll validate-registry --input artifacts/framework-migration/native-d/load.json
dotnet artifacts/framework-headless-host-v3/Headless.dll import-registry --input artifacts/framework-migration/native-d/load.json --evidence-root artifacts/framework-migration/native-d --path imported-carnival34.pb
```

Import writes an original-format setup protobuf plus a source/hash/validation receipt. It imports only actual initial positions; prefab semantics and navigation polygons remain explicit framework annotations. Missing data is never supplied from the reference. Goto requires the session's actual matching registry. Explicit development warp requires that registry and currently supports only validated fixed entity paths; dynamic spawning is rejected until its ordered native mapping is validated. These gates permit native experiments without claiming full clock/RNG/score correctness.

Fresh native-d also demonstrated an order message arriving on an already-paused callback. V3 applies that actual message at the existing frame, while leaving automatic progress and time unchanged. It also preserves native messages sharing the first InLevel callback. Ordinary advancing-frame message/chef update order and the separate warp reconstruction path remain unchanged.

New traces contain an initial setup protobuf/hash, host DLL hash, requested seed, ordered native Thrift output/input exchanges, and accepted mutating control commands. Offline reconstruction reuses these commands and reports input mismatches; it never connects to the game. Checkpoint export and inspection do not mutate the session and are not replayed as commands. A partial trace or older exchange-only trace cannot be assumed to have complete control history.

The host always disables `PreventInvalidState` in outgoing requests, omits implicit upstream seed 12347, and does not request game-speed changes. Explicit `--seed N` requests the framework seed control and therefore still requires the integration's native RNG policy review. The host does not establish native high-score qualification by itself.

## V4 fixed logical input segments

`raw-input` requires a settled paused session, all four observed chef registrations, and no unfinished graph actions. Each segment explicitly specifies all five logical controls for each native chef ID. It permits concurrent movement, pickup, interaction, and dash; it imposes no additional pickup cooldown. Existing original controller history advances once per emitted frame, and independent button held/rise/release values are recorded exactly.

```json
{"segments":[{"frames":30,"chefs":{"103":{"x":0.25,"y":0,"pickup":false,"interact":true,"dash":false},"104":{"x":0,"y":0,"pickup":false,"interact":false,"dash":false},"105":{"x":0,"y":0,"pickup":false,"interact":false,"dash":false},"106":{"x":0,"y":0,"pickup":false,"interact":false,"dash":false}}}]}
```

These example axes are syntax, not a validated safe movement route. Use actual observed IDs. Axes must be finite in [-1,1]; the adapter accepts 1–1024 segments and 1–36000 payload frames. It appends **two counted neutral release/pause frames**: a 30-frame payload consumes 32 advancing frames. Completion means the entire stream and pause barrier were observed; it does not imply washing, throwing, or other interaction success. A disconnect, unexpected pause, frame mismatch, or bounded wall timeout cannot produce a completed recording.

```powershell
dotnet artifacts/framework-headless-host-v4/Headless.dll raw-input --input segments.json --control-port 17637
dotnet artifacts/framework-headless-host-v4/Headless.dll inspect --control-port 17637 --full
dotnet artifacts/framework-headless-host-v4/Headless.dll record-input --control-port 17637 --path completed-input.json
dotnet artifacts/framework-headless-host-v4/Headless.dll raw-replay --control-port 17637 --input artifacts/framework-headless/completed-input.json
```

Export is create-new and allowed only after `rawInput.outcome=complete`. The recording pins the starting controller state and every actual logical pad. Replay requires that matching controller state (normally an explicitly restored development checkpoint), validates the checksum and logical edges, and emits the fixed stream without adapting it to observed geometry or task progress. Native physics and interaction equivalence need separate observations. Accepted commands and actual input/output exchanges remain in the session trace. Full snapshots now include raw rotation and angular velocity in addition to position and velocity.

`transport-selftest --host-dll <frozen-host.dll> --evidence-root <directory>` starts only its own synthetic host and fake Thrift server on OS-assigned loopback ports. It tests HTTP controls and trace visibility while that child is alive; it never connects to a native game endpoint. On Windows it may require permission to create the HTTP listener. Live trace readers must open with `FileShare.ReadWrite`; ordinary `File.ReadLines` conflicts with the active writer's sharing contract.

## V5 original typed action graphs and native observations

Submit `actions --input plan.json`. The file contains `maximumFrames` (2–36000) and 1–256 topologically ordered `actions`. Every node has a unique string `id`, observed native `chef`, `type`, optional `after` IDs, optional native entity `resources`, and `timeoutFrames` (2–3600, default600). All nodes are checked before installing any. Chef order, repeated exact targets, and shared resources become dependencies in the original framework graph; no extra simulated chef or station is created.

```json
{"maximumFrames":600,"actions":[{"id":"get-egg","chef":104,"type":"pickup","target":65,"expectSpawn":true,"timeoutFrames":300},{"id":"stage-egg","chef":104,"type":"place","target":48,"timeoutFrames":300},{"id":"receive-egg","chef":106,"type":"pickup","spawnedBy":"get-egg","after":["stage-egg"],"timeoutFrames":300}]}
```

This demonstrates the schema, not a natively validated route. `spawnedBy` resolves the original framework claim from an earlier `pickup` with `expectSpawn:true`. Literal targets must be actual live mapped native IDs. A graph needs validated fixed registry mappings; extra live IDs additionally need matching observed names/components, complete ordered parent spawn metadata, and the exact reconstructed parent/ordinal path. Removed metadata is tolerated only after an actual destruction/removal receipt. This validates identities, not dynamic collider shapes or all navigation geometry.

Supported original primitives:

| Type | Additional fields |
| --- | --- |
| `goto` | finite `x,z`, or `target`/`spawnedBy`; optional `dash` |
| `pickup` | `target`/`spawnedBy`, optional `expectSpawn`, `dash` |
| `place`, `interact` | `target`/`spawnedBy`, optional `dash`; interact uses secondary input |
| `throw`, `drop` | finite `x,z`, or `target`/`spawnedBy` |
| `wait` | positive `frames` smaller than its timeout |
| `wait-progress` | `target`/`spawnedBy`, `progressType` cooking/mixing/chopping/washing, nonnegative `progress` |
| `pilot-rotation` | `target`/`spawnedBy`, finite `angle` |

Chopping/washing can be authored from original interaction and progress-wait actions; native held-use behavior still needs a mechanism probe. Cannon boarding/firing use explicit interaction targets, with arrival checked independently. There is no invented cannon-flight completion action. Original transfer actions often finish on input release; `typedActions.outcome=complete` reports those original predicates plus a final observed pause, not proof of food transfer, native progress, catch, arrival, or score. Native event/state checks remain necessary.

Per-action and whole-graph bounds request a neutral release/pause and report failure without marking unfinished actions complete. `actions-clear` while paused removes only this graph's unfinished nodes, refuses to erase a live spawn claim, and keeps completed original nodes/claims. An incomplete stopped graph cannot silently resume without this explicit cleanup. Original protobuf checkpoints retain the actual graph and dependencies; graph budget/status metadata is session-local and remains in the command trace.

V5 decodes the native two-bit `CannonMessage`, retaining its original wire bytes separately from auxiliary native private-state observations. Full snapshots expose `nativeCannon` and `plateLifecycle`; original protobuf histories retain their exact auxiliary bytes. A stale loaded ID alone is not occupancy. Missing, active, or malformed cannon observations reject authoring warp. Served plates remain observable until actual native removal; served animation and entity retirement are separate events. Older replacement-cannon traces require their frozen older host, rather than reinterpretation as the new native schema.

V5.1 pins the supplied setup's registration-position reference before reconstruction. Native startup legitimately settles attached pots, bowls, baskets, plates, and their cosmetic children after registration; the repaired startup replay records those actual settled poses at frame zero. Registration validation compares against its immutable earlier reference, while `startupPoseDifferences` reports the separate reconstructed pose, native component roles, and attachment links. Raw `initialRegistry` remains unchanged. The 0.0001m registration tolerance and name/component checks apply to every original ID, including movable utensils. No native pose is reset or replaced by this audit fix.

## V6 bounded exact tracing

`serve --trace exchange.jsonl` writes version2 evidence. Session headers, controls, advancing callbacks and pause/resume/warp transitions remain ordinary JSONL rows and flush immediately. Only fully paused callbacks in the settled `Paused` state enter lossless Brotli JSONL blocks. Every output/input field remains intact, including the native physics phase counters, small raw physics changes and messages received while paused. Each `paused-exchanges` row contains encoding/version, callback count, original byte length, SHA256 of the exact original UTF8 JSONL bytes, and Base64 compressed data. This is compression, not a state projection or omitted idle-frame inference.

The pending block is bounded by256callbacks or1MiB and flushes on the next callback after5seconds, before a control/advancing exchange, or on clean disconnect/shutdown. `inspect.trace` reports any buffered tail. Abrupt process termination can lose that bounded, fully paused tail; no advancing exchange is buffered. `reconstruct --input exchange.jsonl` expands and verifies blocks before replaying each original callback, and also reads archived `.jsonl.gz` files. A reader that skips compressed rows can inspect advancing inputs but cannot claim full reconstruction.

Storage is bounded by `--trace-max-mib` (default1024, minimum16) and `--trace-reserve-mib` (default256, minimum0). An exhausted cap/reserve or an actual write error latches one evidence failure. The next RPC response requests pause with four neutral released inputs; the framework transport then closes. HTTP inspection remains available with `ok:false` and `traceFailure`; further control/checkpoint commands are rejected. That last emergency response may lack a durable trace and the failed session is not qualifying evidence. Resolve storage and start a new host/trace; no automatic overwrite, deletion or silent trace disablement occurs. Parent bridge disconnect handling remains a separate control fence.

V6 also rejects finite JSON doubles that overflow the framework's float coordinates/angles. Native-I malformed float-encoded auxiliary version bytes are rejected; independently captured native-J corrected uint32 cannon observations are accepted and retained. These codec tests do not substitute for native rewind parity.

V6-b starts a new authoring history immediately after a verified warp acknowledgement, using the original cleanup previously deferred until resume. It then applies the acknowledged native messages, chef observations and full physics at the restored frame. Failed acknowledgements do not trim history. Old future evidence remains in previously written traces/checkpoints; it is no longer the current branch.

## V7 observed dynamic authoring paths

The current native `OutputData` must advertise `NativeWarpCapabilities {Version:1, Features:1}` before a warp containing dynamic entities. Feature bit0 means the installed plugin supports dynamic operations behind mandatory native checkpoint/spawn-chain preflight; it does not claim that a particular restore or replay is correct. Fixed-only authoring remains compatible with an older plugin.

The host retains actual mapped-record registry receipts after each callback, including consumed raw ingredients needed as historical parents of prepared food. Every source/target logical path must have an unchanged recorded prefab identity, exact parent/ordinal links, actual name/component metadata, and complete ordered native spawn entries. Framework display names can differ from native prefab names: the native `SpawnEntity` index binds the observed entry to the selected framework prefab reference. A recreated target's `WarpCalculator` spawn path must equal that exact chain, and its original fixed spawn root must remain live. Missing receipts, changed metadata, duplicate mappings and unsupported capabilities reject the request.

Physical container IDs remain separate from logical item paths. Only an actual `SpawnPhysicalAttachment` message linking its logical and container headers, plus the current observed `Rigidbody`/`ObjectContainer`/physics-sync metadata, permits that otherwise-unmapped proxy. Unknown registered collision objects still reject. Actual retirement receipts remove native mappings; target existence comes from the restored history. `warpMappingValidation` reports the logical paths, historical/native IDs, physical-container associations and scope. These checks do not substitute for the plugin's exact native preflight or independent native food, round and replay comparisons. Metadata receipts are session-local; a checkpoint file alone does not establish a new native process's dynamic observations.

Original fixed physics containers have a separate restore path. The connector records an existing fixed entity only when its actual registry metadata includes `Rigidbody`, `ObjectContainer`, and native `PhysicsObject` sync type47. The warp specification includes that body's exact target position, raw rotation, linear velocity and angular velocity even when it has no logical gameplay component block. It cannot invent or recreate an absent fixed body. This addresses native-O plate12's physical container121, which moved during pickup while the old calculator restored only the logical plate. The captured checkpoint regression proves specification coverage; plugin restore and subsequent replay parity remain native tests.

## V8 lightweight polling

Explicit `status` returns `supercharged-headless-status` version1: current frame, controller state, pending request, connection/error and trace-failure information, movement completion, raw stream counts/outcome, and scalar typed-graph outcome/budget. It performs no registry audit, entity formatting, per-action formatting, state change or trace-control write. It remains available after an evidence-write failure and then reports `ok:false`. `full:true`, development controls and unrelated action arguments are rejected.

Use `status` only while waiting for a bounded operation. Continue to capture `inspect --full` at validation boundaries; the ordinary inspection schema and its validation work are unchanged. `scripts/framework_rpc.py` exposes `ControllerClient.status()` as an explicit opt-in convenience; existing callers are unchanged. This separates polling cost from evidence collection without discarding any advancing exchange or compressed paused callback.

## V9 observed transfer completion

New typed `pickup` and `place` actions set the serialized `InteractAction.require_observed_transfer` option. Its default is false so existing framework graphs retain their prior behavior. The exact expected item/source comes from the action's persisted start frame and the versioned native attachment history at that frame; it is not rebound from the current counter contents. The policy and history survive original protobuf checkpoint serialization and rewind.

Pickup completes only when the original item is attached to the exact chef in both directions, has left its original source, and primary release has been observed. Crate pickup additionally requires one new native child with the correct producer and available original graph claim. An unheld spawned child blocks further crate presses. Placement requires the exact originally held item to attach to the specified initially empty attach station in both directions while the chef is empty and primary is released. It uses the native placement highlight. Consuming an ingredient into a container is outside this attachment-placement predicate and cannot be reported as a successful place.

Release input alone does not finish either action or unblock its dependencies. Existing button-duration, pickup-cooldown, per-action and whole-graph bounds remain. Native refusal can produce legal retries until the fixed deadline; timeout emits the existing neutral pause. Full typed-action inspection includes original item/source paths and attachment/release evidence at completion (or the current pending boundary). Other primitive completion semantics remain unchanged. Native-Q frame43 provides the negative regression; successful native movement/transfer/replay still requires an independent probe.
