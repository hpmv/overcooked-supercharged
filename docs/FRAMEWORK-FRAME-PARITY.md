# Strict advancing-frame comparison

`scripts/compare_framework_frames.py` reads a closed framework exchange trace. It performs no game, process, or network operations. Select two explicit warp epochs (zero is before the first emitted Warp directive) and an exact native output interval. For the initial idle experiment:

```powershell
python scripts/compare_framework_frames.py --trace artifacts/framework-migration/native-m/exchange.jsonl --original-epoch 0 --replay-epoch 1 --start 181 --end 301 --out artifacts/framework-migration/native-m/frame-parity-0-1.json
```

The interval is `(181,301]`: exactly120 emitted advancing inputs and120 corresponding advancing observations. Inputs are indexed by `InputData.NextFrame`; output observations are indexed by `OutputData.FrameNumber`. These are usually in adjacent exchange callbacks. The last paused observation at181 initializes each branch. Paused observations reconstruct that boundary but do not count as gameplay frames.

The reader accepts original JSONL, gzip JSONL, and V6 `paused-exchanges` Brotli blocks. It verifies block version, declared bounds, byte hash, UTF-8, callback count, and paused status. An incomplete or changing file, duplicate/missing frame, incomplete live physical inventory, missing fresh four-chef observation, invalid native state, or unsupported bulk-destroy message fails the comparison. Retained selected observations are capped at128MiB; split longer comparisons into smaller intervals if needed. This is not a lossy trace reader.

Every advancing frame compares:

- All observed native ItemData physical fields, reconstructed from sparse field updates, including actual quaternion components and signed zero.
- Every field in all four native ChefSpecificData snapshots.
- Physics-update count, the no-physics-frame phase counter, and pause transition flags.
- Emitted input values and optional-field presence flags. A newly present seed directive is a mismatch even if its scalar default is zero.
- The complete ordered message stream, plus separate header-decoded native-message and auxiliary-message sequences. Message payload bytes remain exact.

Raw divergence never becomes a pass because the native-only projection matches. That projection distinguishes instrumented auxiliary events from native events; it does **not** pretend to decode the full food/order state. Event order is significant. Physics tolerances, quaternion sign canonicalization, float rounding, and omission of cannon animation bytes are deliberately absent. The existing independent native food/round endpoint checker remains necessary.

`scripts/tests/test_compare_framework_frames.py` has9 tests, including actual native M. Mutations cover transient drift with identical endpoints, quaternion sign, signed zero, input presence/edges, native event order, auxiliary-only changes, missing/duplicate frames, truncated files, nonfinite numbers, and corrupted Brotli receipts. Both plain and gzip compressed-block fixtures pass.

The pinned native M report is `artifacts/framework-migration/native-m/frame-parity-0-1.json`. All120 emitted input frames, all native chef fields, and all physics phases match. The first physical mismatch is AttachPoint81 Z at frame182: `-14.399848937988281` versus `-14.399833679199219`. Native ordered event bytes and cannon auxiliary bytes also first differ at182. The report correctly fails; matching native round/food endpoints does not erase these differences.
