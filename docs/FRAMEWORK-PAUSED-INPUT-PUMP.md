# Paused controller input pump

The injector now polls a pending controller reply without waiting on the Unity main thread when the native game is authoring-paused. A missing reply returns from `ControllerHandler.LateUpdate` before warp dispatch, state capture, or exchange publication. This removes the former two-second main-thread queue wait in that state. Running input retains the bounded two-second wait for this first change.

`InjectorServer` binds each observation and reply to a connection epoch and monotonically increasing exchange ID. There is one outstanding exchange in the current epoch. Replacement retires the old exchange; queued or late old replies cannot resume, warp, or supply inputs to the replacement connection. A failure from an old socket cannot clear a new socket. Transport failure/replacement produces only a pause directive, without an accepted frame tag, and wakes an existing running wait. The background worker no longer disconnects a healthy controller merely because no observation arrived for two seconds.

Paused callbacks waiting for a reply retain pending native messages and registration records in their original order. Only `PhysicsFramesElapsed` is reset at the skipped callback boundary, so it continues to describe one Unity callback. The original `FixedUpdate` and `Update` phase accounting is unchanged. Native messages from that interval accompany the next actual capture; the previously published observation remains immutable. No logical input is applied on an empty poll.

`Injector.Server.Diagnostics()` reports the connection epoch, outstanding exchange, published observations, consumed controller replies, separate transport-only pauses, paused misses, rejected stale envelopes, queue sizes, and running waits/timeouts. These are protocol diagnostics, not native simulation frame counts.

## Offline verification

`scripts/FrameworkPausedPumpCheck` links the actual queue, server, and handler sources with stubbed native/network surfaces. It opens no socket. The test checks 31 named semantic/compiled-IL conditions, 1,000 concurrent FIFO items, and 1,000 immediate missing-reply polls (2,031 assertions). It includes retained events, no duplicate capture or input acceptance, replacement/late-reply fencing, old-socket failure, and unchanged compiled `FixedUpdate`/`Update` instructions. The isolated CLR2 plugin also compiled successfully.

The hash-pinned report is `artifacts/framework-paused-pump-check/tests.json`; the isolated candidate DLL SHA-256 is `a032c1dc1e6a9a923a239f9fdcb7d95a375b0944b28dacdaafa10bc19e779664`. Re-run the fixture with the actual frozen DLL as its optional argument to verify that bundle's compiled paths:

```powershell
$env:NUGET_PACKAGES='M:\projects\game-test-2\framework\.packages'
dotnet run --project scripts/FrameworkPausedPumpCheck/FrameworkPausedPumpCheck.csproj -c Release -- ABSOLUTE_FROZEN_DLL_PATH
```

This is offline evidence. Native delayed-reply responsiveness, exact advancing continuation/phase counts, and paused controller replacement still require the root-owned experiment. No game or runtime deployment was performed for this change.

## Replacing the headless process

The plugin listener accepts a new controller connection while the game stays open. A replacement headless process still needs a normal level restart after connecting: `HeadlessSession` requires a fresh level baseline and does not reconstruct a current round from a late connection. `RuntimeHost.ConnectAndProcess` retries initial connection, but intentionally returns after an established connection ends rather than silently reconnecting with incomplete history. This change does not add full-state reconnection.

The existing bridge restart/arm fence remains responsible for neutral input during native level loading. When reusing the same headless setup, the fresh-search runner records the existing action clear before restart and clears any residual graph again at fenced paused frame 1 before arm/warmup. A new headless process starts with its own selected setup; the pump does not create or reuse planner actions.
