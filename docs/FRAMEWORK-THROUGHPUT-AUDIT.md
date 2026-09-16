# Framework callback throughput audit

This is a file-only audit of native N/O receipts and the frozen V7 host, followed by the separately frozen V8 status change. No native process, port, plugin or existing capture was changed by this audit.

## Measurements

`scripts/FrameworkHostProfile` replays the captured native-O prefix through the first warp request. Its 1,792 exchanges and two mutating controls produce **zero recorded input mismatches**. JSON reading/decompression is outside the per-callback timer; native Unity and socket costs are not simulated. Timings are local measurements under the machine's current workload, not native FPS predictions.

| Operation | V7 mean | Scope |
| --- | ---: | --- |
| Advancing reconstruction | 0.934 ms | 300 actual advancing callbacks, without trace attached |
| Exact advancing trace to file | 0.106 ms | 6,089-byte actual sample; serialization, free-space query, write and flush |
| Compact inspection | 6.063 ms | 1.44 MB allocated per call; holds the session lock |
| Full inspection | 11.319 ms | 2.78 MB allocated per call; holds the session lock |
| Paused trace to null | 0.059 ms | Includes periodic lossless block compression; max 6.48 ms at a block boundary |

The native-O original idle interval has 128 collector callbacks in 2.985 wall seconds, including 120 advancing frames and paused/control callbacks. The collector alone records 1,223.7245 ms, or **9.560 ms per callback**. The two replay intervals average 9.384 and 9.471 ms. These counters surround all of `ActiveStateCollector.CollectDataForFrame`, including its kitchen checkpoint; they do not isolate `NativeKitchenCheckpoint.Capture` alone. The approximately 40 advancing FPS therefore has a substantial measured native collection cost, while exact advancing trace writes are small in the offline profile. Remaining Unity/render/socket/wait costs require subphase measurements.

Evidence:

- `artifacts/framework-migration/offline/host-profile-o-v7/report.json` pins native-O trace SHA256 `40bd18278cfd72f9d01ea96b2858084a6601e513215d424051c8ddae48029d81` and frozen V7 DLL `b7e6eb77…e759d7`.
- `artifacts/framework-migration/offline/native-capture-profile.json` pins each native observation file and differences its cumulative native timing counters.
- `scripts/profile_framework_capture.py` reproduces those timing intervals from files only.

## Implemented host change: explicit lightweight status

Even `inspect` without `full` reconstructs the entire registry-validation report and copies mapping diagnostics. The search loops currently poll every 20–30 ms. Each such inspection contends with `getNext` for the same lock; this can consume a meaningful part of the interval while the native input queue waits. The configured non-realtime host uses effectively zero `FramerateController` delay, and both TCP endpoints enable `NoDelay`; no fixed per-frame asynchronous sleep was found there.

V8 adds opt-in `command: "status"`. It returns the exact current frame/state/pending request, connection/errors/trace failure, movement completion, raw stream outcome/counts, and scalar typed action outcome/budget. It performs no registry, entity or action-graph formatting. Full inspection and every trace exchange remain unchanged. The Python RPC client adds an explicit `status()` convenience method; probe/search callers are not changed by this audit.

The V8 check build, on the same captured prefix with zero input mismatches, measures **0.0052 ms** and **5.8 KB** per status call versus 6.762 ms and 1.44 MB for compact inspection. This demonstrates reduced local polling cost only. Native end-to-end improvement must be measured after callers opt in. There are 32 focused read-only/schema/error/negative checks; reports and the captured-source freeze identify the exact tested binary.

## Remaining plugin opportunities, not implemented

1. **Avoid duplicate full cannon snapshots within one callback.** `ObserveFrame` calls the full `Observe(server)` merely to construct auxiliary wire data; later kitchen `CaptureAll` calls it again for both cannons. Full observation copies pilot fields/messages, scans registered transform hierarchies, scans parented chefs, and can clone active-flight iterator state. With 122 registry entries and two cannons this invokes four full observations and approximately 976 registry-loop visits per callback, before other collector work. A narrowly shared same-callback full snapshot, or an auxiliary-only reader preserving every auxiliary and settlement field, can avoid this repeated restoration-only work. It must not cache across callbacks: paused callbacks can change animation or flush native messages and still require exact boundary comparison.
2. **Cache reflection metadata, never field values.** Cannon, kitchen and station helpers repeatedly call `AccessTools.Field` for the same installed types/names before reading current values. Static validated `FieldInfo` references preserve current native values and failures while avoiding repeated discovery. Exact DLL/type contracts and missing-field rejection must remain intact.
3. **Separate current round checkpoint state from historical diagnostics.** Every kitchen capture calls `GetDiagnostics`, which rebuilds the entire recipe-history report and clones frequencies, though capture needs current index/frequencies. A bounded current-state accessor can retain the same native validation/read semantics and leave complete diagnostics available on demand. Savings should be measured, particularly later in a round.
4. **Measure global searches before caching memberships.** Each InLevel capture performs global flow, cannon, chef, cooking-station and mixing-station searches. These are concrete repeated operations, but there is no native subphase timer proving their individual cost. Caching live memberships is a larger lifecycle change than caching reflection metadata and needs exact registration/removal/incarnation checks.

Before a plugin optimization, add aggregate count/total/max timing counters around registry/chef collection, cannon auxiliary observation, and kitchen capture (with subcounts for cannon, chef and station checkpoint capture). Keep counters in diagnostics rather than emitting per-frame timing logs. Compare paired bounded native probes with identical inputs and restore/parity evidence. Do not skip paused state checks, drop fields, loosen raw equality, change native time, or disable evidence to obtain a faster number.

## Reproduce offline host measurements

```powershell
$env:NUGET_PACKAGES='M:\projects\game-test-2\framework\.packages'
dotnet run --project scripts/FrameworkHostProfile/FrameworkHostProfile.csproj -c Release -p:HeadlessDirectory=M:/projects/game-test-2/artifacts/framework-headless-host-v8 -- artifacts/framework-migration/native-o/exchange.jsonl artifacts/framework-migration/offline/host-profile-o-v8-new
```

Use a new output directory: benchmark trace creation refuses overwrite. The profiler creates no listeners and calls no native game APIs. It stops before the first recorded warp, so it does not replay development mutation commands against a native process or pretend offline reconstruction proves native state restoration.
