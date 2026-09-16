# A complete development movie at 2,042 points

The V14 seed-0 planner delivered 20 native orders, then failed at gameplay frame 15,520 when idle chef P3 automatically caught raw sausage 443 thrown by P2 toward the original near pot 7. Its ordinary exception cleanup emitted one neutral frame, ending at frame 15,521 with 11.319 native seconds left. This remains a failed bot trial; its recording and failure checkpoint are immutable.

A separately authored continuation supplied 700 frames of neutral four-chef input. It made no placement, inventory, timer, score, position, RNG, or physics-state correction. The native server round ended at frame 16,201 and the client round ended at 16,202. The last observation at 16,221 reports 2,042 points: 1,600 base score plus 442 tips, 20 deliveries and no deductions. This is below the requested 5,000-point target.

The two recordings meet at the same gameplay frame with exactly matching recorded state except three added neutral controller-disconnect observer events. Every other state field, including clocks, recipe RNG, entities and chef transforms, matches at this boundary. Both sets of emulated inputs are neutral. Those three transport observations cannot recur in a continuous single-connection replay; they are explicitly retained in the manifest and source observation trace.

`scripts/compose_neutral_tail.py` validates that join and preserves every recorded request, including the original failed trial's final neutral cleanup and both tail inspections. It creates a **new authored movie**, not a modified trial. The combined observation artifact is the exact byte concatenation of the original gzip members; headers, planner failures and observations remain present.

```powershell
python scripts/compose_neutral_tail.py `
  --prefix artifacts/native-round-v14/trial001.jsonl.gz `
  --tail artifacts/native-round-v14-neutral-tail.jsonl.gz `
  --movie routes/probes/v14-completed-inputs.jsonl.gz `
  --combined artifacts/v14-completed-observation.jsonl.gz `
  --manifest artifacts/v14-completed-movie-manifest.json
```

The existing output paths are immutable; choose new paths to repeat composition. The movie contains 16,226 requests and 16,221 single-frame steps. Its SHA-256 is `3e74bf57e1695b767a0b47daf696083428a55a20967b91a02509e4d838d48920`. The manifest pins both source recordings and the composer. The composer has 12 offline checks, including strict clocks/RNG/physics matching, four neutral chefs, source preservation, event-history checks, frame continuity, output collision and native round-end rejection.

Fresh-process playback uses frozen candidate V14, a native level load before the movie's native restart, and ordinary exact input execution:

```powershell
dotnet artifacts/planner-candidate-v14/OvercookedTAS.Controller.dll record `
  --script routes/probes/v14-completed-inputs.jsonl.gz `
  --out artifacts/v14-completed-replay-a.jsonl.gz --port 17635 --timeout 1800 --compact
```

The fresh process identity, listener owner and game/plugin/controller hashes are in `artifacts/v14-completed-replay-a-process.json`. Replay success and state differences must be established from the completed replay and comparison reports; composition alone proves neither. The five complete fresh-start runs at 5,000 or more points remain a separate, unmet delivery requirement.

The fresh-process replay completed at the same2,042 points. All16,226 recorded requests, input snapshots and current-round recipe RNG states match. The first15,524 original-prefix gameplay/native-event samples match directly. All702 continuation samples also match those named gameplay/event projections after removing only the three exact pinned neutral disconnect records from the expected observer history. The original raw comparison remains unchanged: raw physics first differs at gameplay frame56, and strict clock/observation state differs from frame0. This does not establish bit-identical native state or classify every physical difference as minor. Full evidence and hashes are in `artifacts/v14-completed-replay-a-report.json`; native initial four-player/frame-zero and preview-restoration checks also pass. The target still has zero qualifying5,000-point fresh runs.
