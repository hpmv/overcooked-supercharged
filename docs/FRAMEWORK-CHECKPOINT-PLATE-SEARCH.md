# Checkpoint plate search runner

`scripts/framework_plate_search.py` evaluates the same four fixed plate routes already measured by the fresh-round runner: central chefs 103/106, each walking or dashing between the validated source and output approaches. It starts from the current paused round, arms input, advances 30 neutral frames, and captures one native checkpoint. Every later candidate requires a verified native restore and exact settled boundary comparison. The selected emitted input is repeated once through `raw-input`, exported, then repeated through `raw-replay`.

The operator must first perform a normal native restart and clear the reset action graph while the bridge is paused and input-fenced. This runner does not restart or clear an old graph before its first arm. It rejects any residual graph or active input operation. After its own warps it clears future action nodes through the existing API and verifies the graph is empty. A live spawned ingredient, unsupported cannon state, missing native physics/private clocks, changed food, or nonidentical restored boundary still stops the search. Activate the independently validated body-restore and resume-phase modules before running; this Python update does not load or modify modules.

```powershell
python scripts/framework_plate_search.py --trace artifacts/framework-migration/native-x-v11/exchange.jsonl --out artifacts/framework-migration/native-x-v11/plate-checkpoint-search-1 --maximum-frames 900 --candidates 4
```

Use the actual current owned host trace path and a new output directory. Ports remain 17636/17637 unless supplied explicitly. The host's checkpoint and input-recording filenames include a SHA-256-derived identity of that absolute output directory, so repeated frame-31 rounds no longer collide with earlier create-new files. Both overwrite guards remain enabled.

Evidence is now written incrementally:

* `observations.jsonl` receives each complete RPC/native receipt once, compactly, and flushes after every record. Lightweight `status` polls are retained; full `inspect` runs only at a settled pause. Status errors, trace failure, and a changed full-inspection pause reject.
* `summary.json` contains incremental candidate/comparison results and journal count, size and hash. It does not embed the growing receipt collection.
* At shutdown, including caught failures, the journal streams once into compact `observations.json`. Its array and record schema remain compatible with existing label-based readers. No full record list is retained in memory or repeatedly pretty-printed. A terminated process still leaves its flushed JSONL prefix even if final compatibility output was not reached.
* Candidate and selected-replay `*-raw-capture.json` files retain original advancing input exchanges before input conversion, goal classification, or endpoint comparison can reject. The exact extracted `*-inputs.json` files are retained for each candidate. Input edge inconsistency is reported; it is never repaired. The complete original exchange trace remains the authority for native output and phase/event comparison, including callbacks not retained by the input-only extractor.
* Native pause proofs and all their raw receipts remain intact. The acceptance condition is exact settled-pause equality, not an assertion that the first warp acknowledgement had identical raw physics.

Offline validation: 24 checkpoint tests plus 15 fresh-runner tests pass. These include an actual native S goal fixture, full two-sided attachment rejection cases, old-graph rejection before arm, lightweight polling failure/timeout cases, preserved failed input exchanges, collision-free host filenames, and final compatibility output on failure. The shared journal extraction leaves the fresh runner's API and evidence format intact.

This release adds no new native success claim. The separate fresh-round experiment measured 56 versus 73 frames and repeated the selected input twice in fresh rounds within one process. Genuine checkpoint search and strict per-frame native physics/event/phase parity remain separate validations; no food delivery or score gain is claimed here.
