# Final-sauce service wait

`WaitForFinalSauceHead` / `--wait-final-sauce` is an optional extension of `WaitForImminentHead` / `--wait-ready-head`. Both flags must be enabled. It keeps the lower-right service chef available for an already ongoing FIFO assembly when the owner holds the exact prepared base plate at the correct native condiment dispenser and has only its final ordinary `apply` followed by its reserved final `place` remaining. It does not change the assembly chef's input, actions, resource ownership, or native food.

The motivating V16 observation is gameplay frame11099. Chef3 held plate379 (ordinal378), containing the native onion-hotdog base472326, and targeted dispenser72, which offered Ketchup158482. The exact FIFO recipe was257844. The existing serial job owned plate379, output52, dispenser72 and switch79. Its clear remaining output route measured6.677732 units at native walking speed6. Including12 frames for the final application and12 for placement gave an admission estimate of90.7773 frames.

The original run applied Ketchup at11105, staged the plate at11184, and completed its original job callback at11185:86 frames after the departure decision. This is an observed remaining assembly duration. The service chef actually left in that run; these fixtures do not replace its recorded movement with a hypothetical waiting pose or establish a saved service trip.

## Bounds and identity

Admission requires a strict native prepared-base recipe match with exactly one expected Mustard or Ketchup missing. The actual dispenser selection, offered ingredient, and native placement target must agree. Additional ingredients, switches, or other queued actions reject admission. The original Work, complete owned-resource set, plate/output/dispenser/button incarnations, native FIFO index and recipe remain fixed.

The existing120-frame wait starts once per FIFO head per lower-right visit and is never reset. Ordinary ready-head collection still runs before the departure check. Loss of identity, owner, output, native target, recipe, or required resources cancels the wait. Once native application completes, the existing final-placement wait validation takes over, including its native-output-target and attachment-before-callback checks.

At11104 the chef was0.11020 units from its admission pose and still targeted72, but lay inside the navigation model's conservative native-contact margin. The fresh path solver rejected departure for that frame;11105 had a valid path again. Pending native application therefore retains the **initial path evidence**, without inventing a replacement path, only while all of these conditions remain true:

- The same final application and native target are active, with no added work or ingredient changes.
- Fewer than the original12 application frames have elapsed.
- Displacement from admission is no more than two observed native fixed walking steps:0.24 units at speed6 and fixed delta0.02.
- Current and cached motion are finite and no faster than native walking; no dash or impact is active; the measured walking speed and physics step remain unchanged.

This exception preserves only the service chef's bounded wait. It gives the assembly owner no movement instruction and changes no geometry or gameplay clock.

The other captured candidate at12407 is rejected. Its owner still targeted source43 and needed to walk to the dispenser; application and final placement took197 frames. The full remaining walking/application estimate also exceeded120 frames.

## Validation and limits

`FinalSauceWaitSelfTest` uses the original full observations at11099,11104,11105,11184,11185 and12407 with explicitly reconstructed controller Work metadata. It exercises native food/attachment transitions and the ordinary action-completion and meal callback, plus mutations of identity, recipe, resources, target, speed, clocks, displacement, deadlines, and remaining actions. No fixture invokes the game or rewrites recorded observations to claim counterfactual service movement.

The isolated check passed68 new assertions,32 existing imminent-head assertions,45 existing native-placement-target assertions,71 planner assertions, and545 controller core assertions. `artifacts/final-sauce-wait-check/validation.json` records source, fixture and assembly hashes. The default-off adaptive policy still requires a native trial; these results establish the bounded decision logic, not a score or trip-time improvement.

Evidence: `artifacts/v16-service-departures-screen.json`, `artifacts/v16-final-sauce-wait-paths.json`, and `artifacts/v16-final-sauce-wait-paths-lifecycle.json`. The latter uses the frozen V17 path model and discloses the temporary11104 solver rejection. `scripts/FinalSauceWaitCheck` runs the offline assertions against the isolated candidate assembly.
