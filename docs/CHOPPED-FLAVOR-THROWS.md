# Native chopped-flavor throw probes

The two bounded plans test whether pantry chef P1 can chop Chocolate or Raspberry, pick up the exact prepared replacement, and throw it into the original near mixing bowl. P3 then waits for native mixing, pours the dough into the original fryer basket, restores the empty bowl, and waits for native cooking. Each plan has four sequential jobs and a 3,000-frame limit. They make no score or production-planner claim.

The plans resolve stations through measured scene properties. The receiving mixer is at `(24, -10.8)`, the shared board at `(25.2, -14.4)`, and the throw staging position at `(27.2, -12.2)`. The initial observed lane is 3.42685 horizontal units to the bowl; the native chef configuration reports force 18 and inclination 12 degrees. `artifacts/chopped-chocolate-throw-preflight.json` and the corresponding Raspberry report bind the plans and captured geometry. The prior shared-board probe contains both prepared foods with native `ThrowableItem` components.

Both native probes subsequently passed with frozen V14 controller `0afc09032339fb9a69c006bd303bc3bf45862c433afb3bed07fc62253d086404`. The hash-bound reports are `artifacts/chopped-chocolate-throw-a-proof.json` and `artifacts/chopped-raspberry-throw-a-proof.json`. Each records84 native P1 chopping frames and8 observed flight frames for its exact prepared source129. Chocolate entered the original bowl at gameplay frame369 and finished correctly cooked at1712; Raspberry caught at365 and finished at1710. These are successful preparation mechanisms, not a high-score or repeated-replay result.

With an available isolated game already configured for four chefs, the operator can run one probe using its actual frozen controller bundle. The example uses V14 and the primary port; use the explicitly intended port if operating the lab:

```powershell
dotnet artifacts/planner-candidate-v14/OvercookedTAS.Controller.dll plan --file routes/probes/chopped-chocolate-throw.json --restart --seed 0 --isolate-recipe-random --port 17634 --out artifacts/chopped-chocolate-throw-a.jsonl.gz > artifacts/chopped-chocolate-throw-a-result.json 2> artifacts/chopped-chocolate-throw-a-stderr.txt
python scripts/check_flavor_throw.py artifacts/chopped-chocolate-throw-a.jsonl.gz routes/probes/chopped-chocolate-throw.json artifacts/chopped-chocolate-throw-a-result.json artifacts/chopped-chocolate-throw-a-proof.json --controller-bundle artifacts/planner-candidate-v14
```

Replace `chocolate` with `raspberry` for the second plan. Use fresh output paths for every attempt. The checker never connects to the game.

The independent Python checker requires native P1 client/server interaction and work progress on the measured board, a distinct prepared replacement, the same item held by P1, observed flight attributed to P1, and the exact original flour-and-egg bowl gaining the flavor while the other bowl stays empty. It then requires the native Mixed barrier, whole-bowl pickup, exact fryer transfer, empty-bowl restoration, and a correctly Cooked final Chocolate or Raspberry donut. It also checks ordinary inputs, contiguous frames, unchanged instrumentation, final neutral empty chefs, the closed result, and frozen source/binary hashes. Its report distinguishes the operator-selected executing controller bundle from its own independently hashed food-schema checks.

Run `python scripts/test_flavor_throw.py` for the eight independent checker transition and rejection tests.

The new optional production flag is `--direct-flavor-throws` (`DirectPreparedFlavorThrows`, defaultfalse), used alongside native pantry chopping. It targets only the original near bowl with exactly Flour and Egg, the assigned missing flavor, an empty shared board, and a collision-checked walking/chopping/input budget within the unchanged21-second mixing guard. Far bowls and other input states retain the existing central transfer.

One ordinary pantry Work retains the exact bowl, home, board and crate through raw pickup, board placement, chopping, prepared pickup, measured staging and the native throw. A per-frame observer verifies actual raw work, replacement identity, P1 holding the replacement, native P1 flight, source consumption and the exact receiving-bowl delta. The native home guard also remains active through RouteThrow's release. The observer requires explicitly active original entities and a present, finite, nonnegative mixing-progress observation, and continues enforcing the mixer deadline while the bowl is owned. Another chef intercepting the prepared item causes a clean failure; the separate sausage recovery does not imply that this flavor interception has been validated.

`PreparedFlavorThrowSelfTest` replays the two successful native jobs through this production observer, then applies explicit identity, activity, ownership, ingredient, flight, finite-clock and admission mutations. All52 checks pass, with29 prior pantry,36 mixer-recovery and71 planner checks also passing in `artifacts/prepared-flavor-tests.json`. A complete production run using the optional flag remains unverified.
