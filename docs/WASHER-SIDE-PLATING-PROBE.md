# Prepared washer-side plating experiment

This mechanism probe completed on a fresh native Carnival of Chaos 3-4 four-player round. The root-controlled run and independent file-only checker passed in `artifacts/washer-plating-a-proof.json`. It used ordinary inputs through the frozen V14 controller (`0afc09032339fb9a69c006bd303bc3bf45862c433afb3bed07fc62253d086404`). It did not change the controller/plugin, mutate native state, qualify a score, or demonstrate washing. The later optional planner integration is documented in `docs/WASHER-SIDE-PLATING.md`.

`routes/probes/washer-plating-cold-setup.json` performs only cold work: chef1 boards the normal right cannon and chef3 fires it to LL; chef2 chops one bun and places one raw sausage on the other UL board; chef0 moves one original clean plate to handoff44. Every cooking/mixing vessel remains empty. The runtime barrier then requires actual completed LL arrival, four empty hands, that exact plate, the chopped bun, raw sausage, and empty shared/output counters before admitting heat.

`routes/probes/washer-plating-main.json` has three sequential jobs:

1. Chef0 transfers the raw sausage into the native near pot, takes the chopped bun, waits for native cooking, and immediately combines the cooked sausage into that same bun. Chef0 stages the resulting native unplated Plain on shared counter50 at(15.6,−18).
2. Chef1 takes the clean plate from44, performs the existing native plate-under assembly on50, and leaves the same completed plate on50.
3. Chef0 takes that plate and stages it on the existing LR handoff49 at(25.2,−20.4).

The original unplated food entity is expected to be consumed during native plate assembly; retaining its identity means proving that exact source-to-plate transition, not insisting that a consumed entity remains alive. The original plate's identity and ordinal must persist through the whole chain. The source hotdog must match native recipe296560 before plating, and the final plate must match the same recipe afterward.

## Guarded runner and checker

`scripts/WasherPlatingProbe` uses the frozen controller's existing `TasClient`, `ConcurrentPlanRunner` and trace writer. Its `run` mode restarts the connected native kitchen with isolated seed0, then executes both plans in one connection. It records all four chefs' ordinary single-frame inputs. It does not launch, stop or kill a game process.

The observer runs before each subsequent request and after every native response. It rejects skipped frames, changed original identities, unexpected processing work, native ruined food, or the single pot reaching22 seconds. That leaves two seconds before the native24-second burn boundary. A failed run emits one bounded neutral cleanup request and stops; it does not attempt world repair. All cooking/mixing vessels except the one planned pot must remain empty, and the pot must remain attached to its original stove. The pot must be empty with progress0 before the probe succeeds.

The `analyze` mode is file-only. It checks the recorded plan/controller manifest, fresh seeded restart, four-player single-frame input protocol, all heat/identity predicates, the native intermediate transitions, closed success marker, and exact final response. It pins trace, result, plan and checker hashes. The report explicitly excludes washing, serving, score and reproducibility qualification.

Static preflight passed seven jobs and22 actions against `artifacts/cycle-start.json`; the report is `artifacts/washer-plating-preflight.json`. It verifies unique native selectors, dependency order, empty surfaces, and static paths for the ordinary actions. Cannon boarding/firing uses the already proven native transport primitive; preflight's computed LL endpoint is not a new landing proof. Ten offline observer checks cover the22-second boundary, unexpected heat, skipped frames, plate incarnation replacement, missing stove attachment and incomplete mechanism evidence.

The root executed the prepared probe using these commands:

```powershell
dotnet scripts/WasherPlatingProbe/bin/Release/net10.0/WasherPlatingProbe.dll run 17634 artifacts/washer-plating-a
dotnet scripts/WasherPlatingProbe/bin/Release/net10.0/WasherPlatingProbe.dll analyze artifacts/washer-plating-a.jsonl.gz artifacts/washer-plating-a-result.json artifacts/washer-plating-a-proof.json
```

The runner refuses to overwrite an existing trace/result. No `run` command was executed by the file-only preparation/review agent. To repeat the offline checks:

```powershell
dotnet scripts/WasherPlatingProbe/bin/Release/net10.0/WasherPlatingProbe.dll preflight artifacts/cycle-start.json artifacts/washer-plating-preflight.json
dotnet scripts/WasherPlatingProbe/bin/Release/net10.0/WasherPlatingProbe.dll selftest artifacts/cycle-start.json artifacts/v13-buffer-boundary-gf1178.json
```
