# Optional direct clean-pass assembly

`--direct-clean-pass` sets `CarnivalPlannerOptions.DirectCleanPassAssembly=true`; the default is false. At an idle central chef's ordinary dispatch boundary, the existing FIFO-head assembly attempt retains first priority. The option then tries an otherwise eligible zero- or one-condiment meal with the exact clean plate currently on the shared washer handoff, before the ordinary clean-plate relocation job. Existing meal-window, last-plate/FIFO, native food preparation, sauce, output, and resource checks still apply. Two-condiment assembly keeps its existing scheduling path.

The motivating native observation is `artifacts/v14-storage-snapshots/clear-clean-plate-handoff-gf7228.json`. At gameplay frame7228, clean plate272 is on handoff44, another clean plate249 is on counter41, and prepared plain hotdog245 is on counter38 for the upcoming Mustard meal at zero-based index11. The unfinished FIFO onion meal has no prepared onion base. The original policy relocates272 to storage during frames7228–7372, then starts Mustard assembly. The new captured-state test chooses272 directly, even though249 is closer to chef3. It emits the existing switch, take, assemble, apply, and final place actions. This removes the intermediate storage stop in that tested decision; it does not establish that all144 frames are recoverable in a native run.

The ordinary assembly Work owns the exact plate, food source, output, and required condiment resources. Admission requires both plate and washer handoff to be free, so a persistent washer-side plating lease excludes this path. The source counter is occupied until native pickup and is deliberately not reserved for the entire assembly trip. A per-frame observer requires the same plate and handoff incarnations and the unchanged recipe index, then proves that the selected chef holds the same empty plate while the handoff is empty. After that proof a washer may immediately reuse the counter. The original Work's completion cannot release a successor counter/plate reservation. Existing cannon interruption may temporarily suspend the exact Work without replacing its queue or ownership.

Completion uses the existing recipe classifier and additionally requires that the original plate is attached to the original output and that the chef is empty handed. Audit events are `directCleanPassAssemblyStarted`, `directCleanPassPlatePickedUp`, and `directCleanPassAssemblyComplete`; each records the recipe, plate and source identities, output, job, and frame. Native food, positions, timing, scoring, and input semantics are unchanged.

Offline checks use the actual7228 snapshot plus explicitly synthetic ownership and completion states. They cover default-off behavior; exact plate selection; plate/source leases; last-plate protection; unavailable food, outputs, and sauce; held, busy, suppressed, or heat-blocked chefs; changed native incarnations; wrong-chef pickup; replacement Work; successor washer ownership; cannon interruption; and wrong final output. Run:

```powershell
dotnet build controller/OvercookedTAS.Controller.csproj -c Release -o artifacts/direct-clean-pass-check
dotnet run --project scripts/DirectCleanPassCheck/DirectCleanPassCheck.csproj -c Release
```

The native V16 bundle predates this option and remains unchanged. Production admission, comparative native timing, and a full successful round with this option remain to be measured.

The subsequent V17 native run demonstrated a sole-plate allocation problem when this shortcut skipped an otherwise eligible earlier two-condiment meal. The mandatory narrow correction and its native witness are documented in [DIRECT-CLEAN-PASS-FAIRNESS.md](DIRECT-CLEAN-PASS-FAIRNESS.md). It preserves the earlier meal only when the clean-pass plate is the sole usable clean token; parallel behavior with multiple clean plates remains unchanged.
