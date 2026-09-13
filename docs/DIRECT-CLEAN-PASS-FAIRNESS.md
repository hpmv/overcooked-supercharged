# Protecting the sole clean plate from a later-meal shortcut

When `DirectCleanPassAssembly` is enabled, the shortcut now declines to allocate its sole available clean plate to a later meal if an earlier skipped two-condiment meal admits ordinary assembly with that exact plate. The ordinary dispatcher then continues. This is a narrow allocation correction, not a new assembly priority or a two-condiment extension of the shortcut. With two or more usable clean plates, the previous shortcut behavior remains unchanged.

## Recorded cause

The unchanged native witness is `artifacts/v17-head-plate-gf14995.json`. At gameplay frame14995, the current FIFO meal23 already has plate377 held by chef2. The next meal24 needs both condiments and has prepared plain base419 on counter40. Clean plate484 on shared washer pass44 is the only available clean plate. Counter49 and both condiment resources are available. Chef0 has just finished a bun-buffer job; chef3 is occupied by an unrelated pot-rescue lease.

The direct-clean shortcut supports at most one condiment, so it skipped meal24 and used484 for the later Chocolate meal25. After meal23 was delivered at14998, the replacement clean plate506 did not reach pass44 until15701. Meal24 then took348 frames to assemble and stage, completing at16049. The native service output47 was empty throughout14998–16047. The exact event and plate timeline is in `artifacts/v17-endgame-head-report.json`.

The observed703-frame wait for a replacement plate is not a claimed saving from this fix. The fix prevents the demonstrated sole-plate allocation; ordinary dispatch, transport, washing and assembly still determine the resulting native schedule.

## Admission and ownership

The narrow inspection reuses ordinary `TryAssemble` admission with the earlier exact recipe index and the same clean-pass plate. It is permitted only for the reviewed two-condiment case. Recipe readiness, source and plate ownership, FIFO plate allocation, output availability, condiment locks, serial staging and cooperative-helper eligibility are the actual assembly checks. An unready base, occupied output or owned resource does not cause a blanket refusal of the later shortcut.

Inspection returns before changing a Work, reservation, recipe assignment, plating set, cooperative lease or staging registration. The near-sauce selector suppresses its diagnostic refusal event during inspection, so even a refused inspection does not append to the trace. A busy helper may still permit the normal serial path. An idle eligible helper may permit the existing cooperative path, but inspection creates neither helper Work nor lease.

The guard requires exactly one `AvailablePlates()` token. Initial shortcut admission already proves that this token is the exact clean plate on pass44. When another usable clean plate exists, the later shortcut may still proceed and leave the other plate available for parallel work. No input, native state, scoring, recipe or timing rule changes.

## Evidence and checks

`artifacts/v17-direct-clean-fairness-admission-proof.json` records an independent call to the original, frozen V17 ordinary assembly implementation using the native14995 response and explicitly reconstructed scheduler ownership. It admits `assemble-meal-24-Hotdog_Ketchup_Mustard` with plate484, base419, source40, output49 and condiment72/button79. The native response remains unchanged. This is an offline admission proof, not a counterfactual game run; its walking input options are not a native timing comparison.

`artifacts/direct-clean-fairness-tests.json` records74 focused assertions,56 prior direct-clean assertions and71 planner assertions, all passing. It pins the source, witness and isolated controller hashes. The focused checks compare serialized planner state, reservation sets, worker queues, relevant object references and actual trace-file byte count before and after inspection. Negative fixtures cover missing or unprepared food, source/plate/resource leases, unavailable outputs, busy owners, unallocated current FIFO, unsupported inspection scope and the selector's refusal-log branch. The two-clean fixture preserves the previous later-meal shortcut behavior.

The public fixture entry point is:

```csharp
CarnivalPlanner.DirectCleanFairnessSelfTest(native14995, nativePreviewCall)
```

The harness is `scripts/DirectCleanFairnessCheck`. The isolated tested DLL is in `artifacts/direct-clean-fairness-check`; its SHA256 is `830ec9120d68d6094b5b8b23f0ccfaed3169c9c9dfaf0730f5423b6a6c3ba929`. The source passed independent read-only ownership review. A fresh native candidate run is still required to measure the resulting schedule; these checks do not demonstrate the requested final score.
