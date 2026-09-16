Direct Flour throws into original far bowl3 failed in both native probes. Neither run consumed or changed the complete near bowl6 kit. The failed traces are retained; no production far-bowl throw policy follows from these experiments.

| Native observation | A: staging (27.2, −12.2) | B: staging (28.8, −11.3) |
|---|---:|---:|
| Same near Flour/Egg/prepared Chocolate setup completed | GF372 | GF372 |
| Original far Flour source131 began flight | GF469 | GF480 |
| First observed nonflying state | GF478 | GF516 |
| First observed attachment to upper board59 | GF478 | GF517 |
| Far bowl3 received Flour | No | No |
| Near kit contents unchanged | Yes | Yes |
| Final frame after failure cleanup | 560 | 571 |

Both probes used ordinary one-frame inputs to P1, with the other three chefs neutral and zero deliveries, score or deductions. They executed frozen V21, identified by the operator; the independent checker uses that exact frozen classifier assembly. [A proof](M:/projects/game-test-2/artifacts/full-near-far-bowl-flour-a-proof.json) and [B proof](M:/projects/game-test-2/artifacts/full-near-far-bowl-flour-b-proof.json) pin each compressed trace, authored plan, checker source, plugin and instrumentation hashes. Empty result files are recorded as failed runs.

B reached (24.8094, 1.0549, −11.1321) at GF495, then lost forward progress and rose. It was still unattached when flight ended at GF516, before snapping onto board59 the next frame. This distinguishes flight deflection/termination from later board catching. The current telemetry does not identify the colliding object; these observations do not prove which collider caused the deflection. A's first nonflying and attached observations occur in the same sample.

The [native scene evidence](M:/projects/game-test-2/artifacts/far-bowl-native-mechanics-evidence.json) reads and hashes the installed `s_day_3_4` bundle directly. Its original IngredientContainer components (pathIDs8325 and9864) both set capacity3, and both catchers require attachment. The generic exported prefab's capacity4 is not the setting used by this scene. [ServerIngredientContainer](M:/projects/AssetRipper/Source/0Bins/AssetRipper.Tools.SystemTester/Release/Ripped/ExportedProject/Assets/Scripts/Assembly-CSharp/ServerIngredientContainer.cs:57) tests remaining capacity, without rejecting duplicate ingredient identities. A partial near bowl containing Flour therefore does not form a safe lane. A full three-item kit rejects another Flour through the native capacity gate, while retaining its physical bowl collider.

Three bounded lower-diagonal candidates were screened and rejected before authoring a C run. [The full-box sweep](M:/projects/game-test-2/artifacts/far-bowl-lower-sweep-screen.json) uses Flour's native0.95×0.4×0.95 box, its observed launch offset, bowl6's solid box and bowl3's catch sphere. All three points have legal walking paths, but near-body overlap precedes even the optimistic first far-sphere overlap:

| Staging | Near solid overlap | Far catch-sphere overlap on an unimpeded arc |
|---|---:|---:|
| (27.2, −13.6) | 0.1676s | 0.2380s |
| (27.2, −14.0) | 0.1994s | 0.2582s |
| (26.32, −14.5) | 0.1980s | 0.2244s |

The unchanged native force18, inclination12°, drag2, gravity and0.02s physics step reproduce eight pre-contact A positions within1.31e−6m. The predicted free-flight bottom peaks at y1.0633, below the near solid top1.1. These are geometric rejection results for the named candidates, not a claim that every possible throw is impossible or that the native collision solver was replayed exactly. [Screen source](M:/projects/game-test-2/scripts/screen_far_bowl_lower.py) preserves the consumed snapshot/report hashes.

[ServerAttachStation](M:/projects/AssetRipper/Source/0Bins/AssetRipper.Tools.SystemTester/Release/Ripped/ExportedProject/Assets/Scripts/Assembly-CSharp/ServerAttachStation.cs:450) delegates catching to an occupied container; an empty station can attach a stopped throwable. Thus removing the near bowl does not by itself prove that its original empty mixer can be crossed safely.

A possible next mechanism probe is an **already detached near bowl**, parked legally while its contents and progress remain unchanged, followed by a throw to the still-original far bowl. Reusing mandatory offmix rescue or an ordinary whole-bowl transfer avoids adding relocation solely to clear the flight lane. The remaining near mixer worktop has native solid top y0.5; its tall layer26 block does not collide with released layer14 ingredients. A native far catch remains unproved.

Dedicated relocation is a weaker scheduling candidate: P1 has no measured walking approach to original near bowl6, so a center chef must take/park/restore it. Shared ordinary counter48 is reachable from center and UR. Four center transfer actions, temporary counter ownership and paused incomplete mixing would replace V19's three far ingredient relays, which occupied210 center frames. That comparison establishes neither net savings nor earlier FIFO completion. No relocation route or production policy was added.

The file-only checker reproduces each report with `dotnet run --project scripts/FarBowlProbeCheck/FarBowlProbeCheck.csproj -c Release -- TRACE PLAN RESULT OUTPUT`. It rejects active writers, reads the complete gzip stream, and requires native source identity/removal plus exact far contents delta for a passing catch. [Evidence extraction source](M:/projects/game-test-2/scripts/build_far_bowl_native_evidence.py) rechecks the original scene components and consumed artifact hashes.
