# First native plate-preparation candidate

Native S successfully moved the same clean plate **10 from counter 38 to counter 32** using chef 103. The full candidate ran from gameplay frame 31 to 104: **73 frames and 1.2166655 seconds of observed native round time**. Two ordinary pickup-button presses performed the pickup and placement; no other chef moved, and no dash or use input occurred. The exact clean plate composition, fixed native inventory and zero score/delivery ledger were preserved.

The native network messages confirm both transitions independently of the typed action's completion label:

| Native frame | Evidence |
| --- | --- |
| 55 | Plate 10 parent becomes chef 103; ChefCarry reports item 10; counter 38 reports empty. |
| 102 | Plate 10 parent becomes counter 32; ChefCarry reports empty; counter 32 reports item 10. |
| 104 | Candidate ends with all chefs empty-handed and the same clean plate on the output counter. |

The original trace contains all 73 input frames, including pickup edges at 54/55 and 101/102 and the final two neutral frames. `scripts/check_framework_plate_achievement.py` checks the attachment messages against the actual registry's native component types, binds the decoded slice to the original trace byte prefix, and pins the frozen plugin/host binaries against the execution receipts.

This is **one successful candidate**, not a completed optimization or verified replay. Before trying the next candidate, strict rollback comparison failed: reconstructed plate 10 remained at the future counter's position, although native physics, native food, round state and private clock fields matched. The runner stopped and preserved that difference. There is no optimized winner, measured improvement over another successful candidate, cooked meal, delivery or score achievement from this test.

Files in `artifacts/framework-migration/native-s/plate-search/`:

* `single-candidate-achievement.json`: checked native effect, timing, binary hashes, exact event receipts, source trace prefix hash and rollback limitation.
* `chef-103-walk-inputs.json`: all 73 observed input frames.
* `achievement-trace-slice.json`: decoded original rows and exact physical line locations, checked against the source byte prefix.
* `chef-103-walk-prepared-raw-request.json`: **unexecuted** command prepared offline from those inputs. It has 71 payload frames plus the raw runner's two automatic neutral-release frames. The operator must first establish and verify the intended frame-31 baseline in the target session; this file does not make S's failed rollback valid.

Recheck without connecting to the game:

```powershell
python scripts/check_framework_plate_achievement.py --folder artifacts/framework-migration/native-s/plate-search
python -m unittest discover -s scripts/tests -p test_framework_plate_achievement.py
```

The native captured case and nine negative proof mutations pass their expected checks. Endpoint physics is compared at the explicitly recorded settled-pause observation condition; this does not assert equality of the initial raw pause acknowledgement.
