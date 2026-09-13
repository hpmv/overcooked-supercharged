# Reusable checkpoint plate replay audit

`scripts/audit_framework_plate_replay.py` audits a completed checkpoint plate search without contacting any process. Run it from the workspace root:

```powershell
python scripts/audit_framework_plate_replay.py --run artifacts/framework-migration/native-x-v11b/plate-checkpoint-sync-r2 --out artifacts/framework-migration/native-x-v11b/new-plate-audit
```

The output directory must be new. Exit `0` means strict advancing-frame parity passed; `1` means the audit completed and found a difference; `2` means evidence binding/comparison could not be completed. An advancing-frame pass does not override the source run's separately retained endpoint result or qualify a score.

The script reads the selected completed trial, its saved action request, both input captures, and the selected raw request. It validates the raw request against the selected inputs, including the existing two-frame neutral-tail conversion. It copies the exact source prefix through the replay capture's recorded `traceByteOffset`, re-reads the same source bytes to check for concurrent changes, and retains its hash. Later live bytes are excluded. An incomplete final line after the operation is retained as bytes but never parsed.

For each capture, the last traced control at its recorded end offset must match the exact saved operation. Every following input exchange must match the capture in order, and exactly one terminal advancing output must follow the final input. This independently supplies the terminal output omitted by the input-only capture. Missing/duplicate inputs, duplicate terminal outputs, changed requests, and ambiguous operation intervals fail. The native load callback and emitted warp directives determine the comparison window and epochs; no epoch numbers or line numbers are supplied by the caller.

`AdvancingTrace` originally serialized captures after Python's ordinary `json.loads`, which turns an integer `-0` token into `0`. Capture binding reproduces this documented producer behavior. **The actual parity comparison reads the unchanged native trace with the strict comparator, preserving signed zero, numeric types, all input presence flags, native event order/bytes, and physical values.** No physical normalization, tolerance, or message removal is used.

The contiguous same-load window preserves all intervening callbacks, including verified Brotli paused blocks. `prefix-manifest.json` pins the original source range, evidence files, auditor/comparator, and derived command/load/epoch locations. The output includes:

- `strict-comparison.json`: independent physical, chef, phase, input, native-message and auxiliary-message checks.
- `divergence-timeline.json` and `first-divergence-context.json`: first differing fields plus original/replay observations and inputs.
- `world-object-event-timeline.json`: exact native WorldObject message bytes and frame offsets.
- `summary.json`: first divergence, per-entity physical divergence, event counts and the original endpoint comparison.

The recorded `Items` fields are reconstructed from actual deltas. Full native Rigidbody mass properties are available in endpoint bridge receipts; the audit does not invent per-frame COM/inertia observations.

## Native r2 result

The reusable auditor was run on both `plate-checkpoint-sync-r2` and prior `plate-checkpoint-synced-r4`. Both selected chef103 dash, 56 frames, derived epochs `1 → 4`, interval `(31,87]`. The prior fixture reproduced the earlier independently pinned window hash and every strict check exactly.

| Observation | Prior R4 / WorldSync r1b | WorldSync r2 |
|---|---|---|
| Plate10 WorldObject event, original/replay offset | 26 / 28 | 25 / 25 |
| First event difference | Frame 57 | None in all 56 frames |
| First physical difference | Proxy118, frame 59 | Plate10 and chef103, frame 74 |
| First proxy118 difference | Frame 59 | Frame 75 |
| First full chef-state difference | Frame 81 | Frame 81 |
| Differing advancing frames | 30 / 56 | 14 / 56 |
| Full inputs, phases and auxiliary bytes | Exact | Exact |

R2's plate event at frame56 is byte-identical in both runs:

```text
AoIzwAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAD+AAAAA=
```

All native message bytes and order also match across all 56 frames. Physical state stays exact through frame73. At frame74, plate10 Y differs (`0.5983898639678955` versus `0.5983898043632507`) and chef103 rotation Z differs (`0` versus `5.587935447692871e-09`). Proxy118 first differs at frame75. The first complete chef-state difference is frame81 `LastVelocity.Y`. This confirms that the earlier event cadence mismatch is fixed in this interval. It does not identify the cause of the later physical drift. The endpoint COM discrepancy (`0.0347953` versus `0.03479535`) still fails exact replay.

R2 evidence: `artifacts/framework-migration/native-x-v11b/plate-checkpoint-sync-r2-strict-review-b`. Exact source prefix: 45,598,300 bytes, SHA256 `5740fdce814965cd42bfdc94111f4bedc85e16594e8eecd1277e0ff2598e04b5`. Same-load window: original bytes `[39302685,45598300)`, lines6472–7392, SHA256 `ab96ddb534af000237051ee9310b0ea7c4ddb6f6c229e279660b756e27dc1542`.

Prior reusable evidence: `artifacts/framework-migration/native-x-v11b/plate-checkpoint-synced-r4-reusable-review`; window SHA256 `70991910a8e08e6f91f2d430614f2f8f85fa9a4234ef24d16f1ef9f439932cb3`, identical to its earlier manual audit. The first r2 audit attempt's capture-decoder rejection is retained in `plate-checkpoint-sync-r2-strict-review`; no evidence was replaced.

Validation: 10 new binding/captured regression tests and all 9 existing strict-comparator tests pass. Tests include offset/request mismatch, missing and duplicate terminal outputs, extra inputs, changed input flags, truncated/partial sources, immutable copies, and a signed-zero mismatch that remains a strict parity failure after successful capture binding.

## Client cache follow-ups: r3 and r3a

The same unchanged auditor completed both `plate-checkpoint-sync-r3-strict-review` and `plate-checkpoint-sync-r3a-strict-review` under `artifacts/framework-migration/native-x-v11b`. Both retain the exact r2 first differences: plate10 Y and chef103 rotation Z at frame74, proxy118 Y at frame75, and chef103 `LastVelocity.Y` at frame81. Both differ on14 of56 advancing frames and retain the same endpoint COM failure. Inputs, phases, and all native/auxiliary event bytes match between original and replay. The plate WorldObject packet appears at frame59 in both r3 branches and frame61 in both r3a branches. The changing cadence between separate rounds is not a replay timing mismatch.

R3's complete replay-difference field list matches r2 through frame85. Only proxy118 rotation X/Y/Z differences at frames86–87 change; these are retained in `plate-checkpoint-sync-r3-strict-review/r2-comparison.json`. The client cache revisions therefore did not fix the onset of the observed drift, although the entire physical tail is not identical across revisions. R3a's follow-up checks the first differences and all strict replay gates; it does not claim a full cross-revision field-list comparison.

Pinned window hashes: r3 `4e41a573ba189907e98f83d2cf1a4ae4830c2586fcca7390134a74ce217cde0f`; r3a `c4a8fed47c92799fff813a9e116c2c410bc9a76cb47b969edd394005d3e77ab8`. Each audit retains the original byte prefix, exact window, full first-divergence context and endpoint receipt.
