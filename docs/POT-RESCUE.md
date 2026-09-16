# Native pot rescue

`CarnivalPotRescue.cs` adds a mandatory vessel lifecycle. It is a controller policy and emits existing ordinary actions; it changes no game timers, cooking progress, scoring, positions, or native recipe rules. Shared heat arbitration selects the exact pot alongside fryer, pan, and mixer obligations.

The initial native snapshot supplies exactly two boiling pots and their original attached cooking stations, including observed object identities. A free pot containing only the native Frankfurter boiling composition becomes a candidate after 11 cooking seconds. The controller first considers direct unplated-hotdog harvest when the sausage is Cooked and a chopped bun is available. This branch checks the full collision-clear chef-to-bun-to-selected-pot walk, native speed, and a two-second input allowance against a 23-second deadline. The assembly receives an exact-pot filter, so another ready pot cannot replace the selected vessel.

When direct harvest cannot be established, the chef navigates, waits for native Cooked state, picks up the whole pot, and places it on an empty ordinary counter. One persistent lease owns the pot, original stove, and counter. Two advancing observations must show the same attachment, exact native composition, unchanged cooking progress, and advancing round timer before releasing the chef.

A parked sausage is not assigned to a future recipe until one eligible unplated-hotdog job claims it. That job owns its bun, source, and output resources; the persistent lease retains the pot resources. Source-board staged release still permits an independent refill, while the emptied parked pot remains unavailable. After the native hotdog is verified and staged, the same chef returns the same empty pot to its original stove. Only the verified empty restoration releases the vessel lease.

An independent per-frame observer rejects any original nonempty pot still on heat at 23 cooking seconds, including a pot owned by an ordinary direct-harvest job without a parking lease. Phase timeouts, missing native telemetry, changed object identities, conflicting owners, changed offheat food, and early stove reuse terminate the attempt through the existing neutral-input failure path.

The integration API is `CapturePotHomes`, `AdvancePotRescues`, `PendingPotRescues`, `TryRescuePot(player, pot)`, `IsPotParticipant`, and `PotStatus`. Admission order belongs to `CarnivalHeatArbitration.cs`; this module adds no fixed pot-priority loop.

## Evidence and tests

`artifacts/pot-offheat-a-proof.json` records the independent native mechanism check. Pot7, ordinal6, left original stove19 and remained on counter32 for 782 observed samples: 13.0166668 native client seconds. Cooking progress13.0499725 and the complete composition hash remained unchanged. The same chopped bun became a native plain hotdog296560, and the original empty pot returned to its stove. The probe used ordinary input commands and frozen V11/plugin hashes; it is not a high-score or replay qualification.

`CarnivalPotRescueTests.cs` uses the actual V11 frame2384 boundary plus explicitly synthetic future observations. Its 54 assertions cover no-bun parking, exact direct-pot selection, timing admission, stable offheat proof, recipe claims, restoration, overlapping source-board refill ownership, unleased deadlines, and adversarial identity/ownership/telemetry changes. The isolated harness additionally passed 375 existing related assertions, for 429 total. These offline checks never connect to the game.

```powershell
dotnet build controller/OvercookedTAS.Controller.csproj -c Release -o artifacts/pot-rescue-check
dotnet run --project artifacts/pot-rescue-tests/PotRescueTests.csproj -c Release
```

The result is written to `artifacts/pot-rescue-tests.json`. A fresh native planner run remains necessary to validate the combined heat scheduling and its throughput. The implementation does not establish the requested 5,000-point result.
