# Predictive one-kit bakery return

`--predictive-bakery-return` enables `CarnivalPlannerOptions.PredictiveBakeryReturn`, which defaults to false. It requires direct vessel throws, `--pantry-chopping` and `--direct-flavor-throws`. The new policy addresses the recorded V16 empty-bowl starvation described in [the evidence and proposal](PREDICTIVE-BAKERY-RETURN-PROPOSAL.md). Its native scheduling benefit is unverified; the evidence demonstrates a missed earlier opportunity, not a score improvement.

The option checks the normal lower-left return boundary after immediate washing work and the existing partial-mixer/FIFO escapes. P1 must be idle, empty-handed, controlled and resting, with no native dirty work in the sink, drying station, handoff, remote return or another chef's hands. There must be at least three distinct usable plate tokens, including an available clean plate and the actual FIFO head. Finished unserved plates and clean plates held by their exact ordinary assembly owner count; served animation residues, retired plates, duplicate meal mappings and duplicate observation identities do not.

The first scope selects only the proven near bowl at its original mixer, uniquely assigned to the earliest missing donut in the unchanged ordinary recipe window. The bowl must be empty with native mixing progress0 and duration12. Ingredient crates and the pantry chopping board must be available. An exact same-round controlled portal arrival establishes the return endpoint. The native throw-home guard, full collision-aware pantry paths and existing prepared-flavor route establish the ingredient plan.

Admission checks the full walking/portal/three-ingredient budget, native12-second mixing and10-second frying, and current clear center and serving routes. The center estimate uses the maximum of successful measured routes through bowl pickup, fryer transfer, mixer restoration, clean-plate pickup, cooked collection and output, plus8 seconds of input/transfer allowance. The current controlled lower-right service route adds2 seconds; another4 seconds supplies headroom. A real empty original fryer and an available output must exist. These are conservative admission estimates; downstream chefs, plates and output are not reserved by the visit, and future traffic or ownership may change. The option does not claim a guaranteed production time.

Once admitted, one ordinary portal job starts. A small scheduling commitment binds the recipe, bowl, mixer and source observation identities. Ordinary raw and prepared-flavor jobs retain all actual resource ownership; the commitment owns no extra plate, bowl, station or chef lease. New dirty plates arriving during the trip cannot immediately send P1 back to washing before its first ingredient. Each observed native ingredient increase must equal the one ingredient issued by the current exact supply job, and existing addressed transfers remain owned and cannot be duplicated.

The visit ends only after the same original bowl contains the complete required ingredient multiset and P1's supply job has completed with empty hands. This does not label the dough Mixed, allocate a plate or bypass native frying. The unchanged21-second mixer guard remains active. The visit additionally has a fixed frame deadline: its initial measured ingredient-kit budget plus120 frames. Identity, ownership, recipe or progress failures terminate the attempt through its existing neutral-input cleanup. Cancellation removes only visit bookkeeping; it does not steal ordinary Work resources or reset an input edge.

Earlier ordinary dough may later need the existing off-heat frying rescue. Its competition for counters and any additional washing delay require native validation. The option does not extend recipe lookahead, change preparation/scoring rules or create plates.

## Verification

The captured positive is V16 GF12373: P1 was idle after returning a clean plate, all dirty work was empty, and exact plates375/395/408/412 supplied four usable tokens while the old `AvailablePlates` returned only412. Negative captured conditions include an out-of-window future donut at11069, served residue379 and remote dirty work at11445, and insufficient production time at14401/15625. Mutations cover duplicate plates, duplicate assignments, missing or changed sources, native duration changes, unrelated supplier Work, stolen resources, nonfinite progress, unexpected ingredient deltas, cancellation and timeout. Synthetic continuation cases are explicitly labeled; they are not native executions.

`artifacts/predictive-bakery-tests.json` records93 new assertions plus29 prior bakery-departure,36 mixer-prerequisite and71 planner assertions:229 passing checks. It pins the tested DLL, feature/test source and five native fixture hashes. The isolated tested DLL is `artifacts/predictive-bakery-check/OvercookedTAS.Controller.dll`, SHA256 `be6e0dfa5f9ad9f84e1ce41a06ae7d48de000644fbcef2d6f78e96e1c52b0c7e`. This is a development test bundle; use the parent frozen candidate's own manifest for a native run.

The offline harness is:

```powershell
dotnet run --project scripts/PredictiveBakeryCheck/PredictiveBakeryCheck.csproj --configuration Release
```

It performs no game I/O. For the integrated policy, inspect `predictiveBakeryVisitStarted`, `predictiveBakeryIngredientIssued`, `predictiveBakeryKitComplete` and `predictiveBakeryVisitCancelled` in the recorded trace. The visit status retains the exact admission evidence and current ordinary job. A successful native test must show a complete issued kit, normal subsequent processing and no abandoned washer or resource ownership.
