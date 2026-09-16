# Bakery lookahead and native off-mixer proof

`BakeryLookahead` / `--bakery-lookahead N` is implemented in source as an experimental option, after the native off-mixer probe below passed. The option is off by default; frozen artifacts are unchanged. The actual bakery planner policy has not yet been tested in a full native round.

The option is default `0`, meaning the existing `Lookahead`; positive values from2 through32 use the larger of that number and ordinary `Lookahead`. It affects ingredient/flavor and mixing assignments for the two existing bowls only. It does not expand `Pending()` for hotdogs, plates, frying, service or scoring. In V7, the near and far bowls returned empty at GF1345 and1645 while the bakery chef remained idle until1967. The next donut index10 was outside the eight-order window untilGF2579. This is the specific opportunity to test.

## Native constraint

The installed game's `ServerMixingHandler.Mix` increases progress with native delta time. `IsMixed` is true at the configured mixing time; `IsOverMixed` becomes true strictly above twice that time. `SetMixingProgress` also displays OverDoing above1.3 times mixing time. `MixingHandler.GetMixedOrderState` uses the same thresholds, and `ServerMixingStation.UpdateSynchronising` switches off only after the bowl becomes overmixed. Both observed bowls have a native mixing time of12seconds. Therefore leaving future completed dough on its active mixer indefinitely would violate the native food rules; no controller time or progress adjustment is acceptable.

`ServerMixingStation.OnItemRemoved` unregisters its bowl, clears the target and mixing state. `ServerMixableContainer.OnContentsChanged` preserves progress for nonempty contents. The actual native probe now confirms that whole-bowl storage off the mixer preserves the dough and supports later frying.

Native source directory: `M:\projects\AssetRipper\Source\0Bins\AssetRipper.Tools.SystemTester\Release\Ripped\ExportedProject\Assets\Scripts\Assembly-CSharp` (`ServerMixingHandler.cs`, `MixingHandler.cs`, `ServerMixingStation.cs`, `ServerMixableContainer.cs`). These were read without editing.

## Bounded implementation

1. Keep the default candidate enumeration exactly equal to the current `Pending()`. For enabled larger windows, use stable native preview indices and avoid indices already assigned to either bowl/fryer or already represented by a completed/assembling meal. Duplicate flavors remain distinct indexed orders. Do not create bowls, plates, ingredients or cookware.
2. Admit an out-of-window bowl only with an exclusive ordinary counter reserved for storing that exact bowl off the mixer. Exclude FIFO workspace and shared handoffs, and preserve existing food/storage reservations. At most the two existing bowls can have such leases. If a counter is unavailable, retain current-window behavior instead of starting unstoreable future dough.
3. Keep a lease for the exact bowl observation identity, original mixer, order index and reserved counter. Once its complete native recipe reaches the rescue threshold, use normal navigation/take/place to park it only after native Mixed is observed. Every frame retains native overmix limits; if an incomplete batch or blocked rescue approaches the deadline, terminate the candidate before claiming success. The supply/wash policy must not strand an incomplete future bowl on a running mixer.
4. Verify the same bowl is on the reserved non-mixing counter, the original mixer is empty, its exact complete food tree and mixing progress stay unchanged across advancing native frames, and the chef has released it. Parked dough holds no chef reservation. This is a state guard, not state restoration.
5. Add an explicit ordinary-window admission to `PrepareDonut`'s transfer-to-fryer branch. Its present `assigned >= 0` check alone is insufficient after extending assignments. When the same index becomes normally eligible, use native bowl-to-fryer transfer and return the original emptied bowl to its mixer, then release its storage lease. Plates continue to be allocated only by the existing ordinary-window rules.
6. Log assignment, deadline/rescue, off-mixer verification, frying eligibility and release decisions. Tests should compare default emitted jobs with the old behavior, distinct same-flavor indices, bounded two-bowl assignment, no future frying/plates, exact lease identity, legal deadline failure, and preservation of native score/cooking rules. Revisit exact-input replay after measured native success.

## Prepared native probe

`routes/probes/bowl-offmix.json` contains six jobs and19 bounded existing actions. P1 supplies flour/egg and chops chocolate; P3 loads the near bowl, waits for native Mixed, takes the whole bowl and parks it on the ordinary top counter at(21.6,-10.8). It remains there for780 ordinary frames (13seconds). P3 then takes that same bowl, pours into the fryer at(22.8,-21.6), returns the original empty bowl to the mixer at(24,-10.8), and waits for native cooked dough. No plates, deliveries, clock changes or food mutations are used.

Offline preflight passed against the actual fresh V7 gameplay-frame-zero response: `artifacts/bowl-offmix-preflight.json`. Its scene-property selectors uniquely resolve bowl6, mixer18, counter33 and basket5 in that fixture; runtime selectors rather than those transient IDs are authored in the route. All movement approaches are reachable in the initial static geometry. This is not a live completion claim.

Parent-owned native execution, using an explicitly selected frozen candidate:

```powershell
dotnet artifacts\planner-candidate-v8\OvercookedTAS.Controller.dll plan --file routes\probes\bowl-offmix.json --restart --seed 0 --isolate-recipe-random --port 17635 --out artifacts\bowl-offmix-a.jsonl.gz --timeout 1800
```

The parent ran this probe successfully. `artifacts/bowl-offmix-a-proof.json` independently verifies the same bowl6 off mixer18 on counter33 across782 consecutive samples, GF1013–1794, with unchanged mixing progress12.1374874 and food tree for13.016464 native seconds. AtGF1901 the dough entered basket5 while chef3 retained the emptied bowl. FinalGF2503 has native cooked chocolate dough and the original empty bowl restored. This worker issued no game commands.

After the trace closes, the read-only checker is:

```powershell
python scripts\check_bowl_offmix.py artifacts\bowl-offmix-a.jsonl.gz routes\probes\bowl-offmix.json artifacts\bowl-offmix-a-proof.json
```

It requires all six completed jobs; actual native mixing progression and Mixed-before-removal; the same bowl and fryer identities; at least781 consecutive parked samples spanning780 advancing frames and at least12.99 native seconds; exactly unchanged parked progress/food; the native fryer gaining the same complete Mixed ingredients while the original bowl is retained and emptied; final native cooked chocolate dough with frying step17160; original empty bowl restored; empty parking counter; and four controlled empty chefs. The output pins source-trace and route hashes. Seven offline lifecycle/rejection regressions pass, including progressing or remounted parked bowls, observation-ID reuse, short waits, frozen clocks and the wrong native frying step.
