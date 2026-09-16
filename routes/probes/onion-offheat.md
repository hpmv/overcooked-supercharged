# Native onion off-heat probe

`onion-offheat.json` is an unverified experiment for a fresh native Carnival of Chaos 3-4 four-player round. It uses the existing adaptive actions and ordinary logical inputs. It does not set cooking progress, change the timer, restore state, or require a plugin change.

The runtime-derived selectors choose the pan initially at `(18,-21.6)`, its stove, an empty counter immediately beside it at `(19.2,-21.6)`, and the final food counter at `(21.6,-21.6)`. These resolve to native IDs 4, 16, 61 and 37 in the captured fresh scene; the authored actions use validated scene selectors rather than those IDs.

Chef 2 chops an onion and a bun. Chef 0 cooks the onion normally, takes the whole cooked pan off its stove, places it on the adjacent non-heating counter, and waits 780 logical frames. Chef 0 then clears the pickup approach. Chef 3 independently cooks a sausage and prepares an unplated hotdog, combines it with the parked onion after the wait, and places the finished onion hotdog on the output counter. Chef 0 returns the now-empty pan to its original stove. No nonempty cooked pan is intentionally returned to heat.

The fixed wait is 13 seconds under the validated 60 Hz clock. Native telemetry must separately prove at least 12 seconds of both client-time advance and timer decrease while the same cooked pan remains attached to the non-heating counter. Every observed cooking-progress value and native composition during that interval must match exactly. The trace must also show normal heating before pickup, the cooked pan held away from its stove, a fully prepared native unplated recipe 472326 at the end, and the original empty pan restored to its stove. The checker makes no score, full-round or reproducibility claim.

Run from the workspace root using the dedicated lab port when the lab is available:

```powershell
dotnet artifacts/planner-candidate-v5/OvercookedTAS.Controller.dll plan --port 17635 --file routes/probes/onion-offheat.json --restart --seed 0 --isolate-recipe-random --out artifacts/onion-offheat-a.jsonl.gz
dotnet run --project scripts/OffheatProbeCheck/OffheatProbeCheck.csproj -- analyze artifacts/onion-offheat-a.jsonl.gz routes/probes/onion-offheat.json artifacts/onion-offheat-a-proof.json
```

The read-only checker references the frozen v5 controller for its existing geometry and recipe classifiers. It has no game connection or input-emission code. Its initial static geometry and dependency check can be repeated without a running game:

```powershell
dotnet run --project scripts/OffheatProbeCheck/OffheatProbeCheck.csproj -- preflight routes/probes/onion-offheat.json artifacts/audited-start.json artifacts/onion-offheat-preflight.json
```

The initial preflight passed nine jobs, 26 actions, unique station selection, acyclic dependencies, bounded action waits, empty staging surfaces and all required approach paths. This does not establish native interaction success or account for every concurrent moving-chef collision; native failures must remain visible and terminate the attempt.
