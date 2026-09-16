# Fresh-round plate preparation search

`scripts/framework_plate_fresh_search.py` runs four actual candidates, followed by two exact-input repetitions of the fastest successful candidate. The operator supplies an already running, owned native bridge and headless host. Importing the script and running its tests do not connect to either service.

```powershell
python scripts/framework_plate_fresh_search.py --out artifacts/framework-migration/native-w/plate-fresh-search --trace artifacts/framework-migration/native-w/exchange.jsonl
```

The output directory must not already exist. The trace must be the current host's append-only JSONL trace. Bridge and controller ports default to 17636 and 17637, with explicit command-line overrides available.

Each attempt uses a normal native restart with seed 0, validates the loaded four-player Carnival 3-4 round with its native 270-second timer and weighted recipe generator, then advances 30 neutral warmup frames from observed frame 1 to frame 31. It never issues an authoring warp. The framework currently retains completed action nodes across restart, so the runner records a pre-restart clear, then clears reset nodes again at frame 1 while the bridge reports both its neutral input fence and pause hold. It verifies the original clean plate, empty hands, and unchanged native entities, food, and round state across that cleanup, and requires an empty action graph before arming input.

The four candidates use each of the two observed central chefs with walking or dash-enabled navigation. All candidates relocate the same clean plate from the validated counter at `(19.2, -15.6)` to the empty counter at `(19.2, -10.8)`. IDs are resolved from the actual native registry, current scene, and attachments. The existing cardinal approach and settle sequence comes from the earlier native S case, where chef 103 completed the relocation in 73 frames. A completed action graph is insufficient: the final goal requires both attachment directions, empty hands, unchanged plate composition and score ledger, and the trace must contain the native pickup and placement event groups for the same plate.

The starting-condition comparison uses exact current geometry and motion for every reconstructed entity and the complete observed chef state at frame 31. Registration metadata remains strict, but its historical `Pos` is retained separately as raw evidence. In the first W attempt, candidate 0 succeeded in 73 frames; candidate 1 was then rejected because the chefs' registration heights differed (`0.049987196922302246` versus `0.04993605613708496`), although all current geometry and chef states matched exactly. The original failed attempt remains intact. The corrected comparison uses the actual warmup state without a numeric tolerance; it does not treat historical registration position as current position.

Ordinary bounded candidate timeouts remain failed candidates. Unexpected identity, food, lifecycle, or control changes abort the search. Selection uses observed native elapsed seconds, with frames and candidate ID as deterministic tie-breakers. Improvement is reported only against a successful baseline; a failed baseline cannot establish a time saving.

The selected candidate's exact four-pad input rows are converted to a raw-input request. The two terminal neutral frames are removed from the request payload because the existing raw runner appends those same two frames. Two more normal fresh rounds execute this identical request. Success requires exact emitted input equality, the actual native goal, identical relative timer changes, unchanged food and score, and the same retained gameplay events at the same relative frames.

The explicit event projection retains PhysicalAttach, AttachStation, ChefCarry, and InputEvent entity messages, including exact bytes. Event order is counted within that retained subset; excluded clock messages cannot shift its ordinal. Complete raw native messages and endpoint state are also saved and compared separately. Absolute clocks and Unity instance IDs can differ between normal restarts. Their differences are reported without claiming full raw-state parity.

This is a same-process fresh-round preparation search. It does not establish fresh-process reproducibility, rewind parity, a recipe delivery, a full-round score improvement, or the 5,000-point goal. The score must remain zero. `summary.json` records candidate results, baseline savings when established, both selected-input comparisons, and any failure; per-attempt evidence retains native observations and complete advancing input/output rows.

During work, each controller/native receipt is appended once as compact JSONL to `observations.jsonl` and flushed. Incremental saves rewrite only the small summary. At finalization, the journal is streamed once into compact `observations.json`, preserving the legacy JSON-array schema and every receipt, including final pause/failure observations. The JSONL remains the live evidence source and survives a later compatibility-write failure. Summary metadata records counts, byte lengths, and hashes. The completed X/V11 search's old 638 MB pretty-printed observations file is retained; this change affects subsequent runs only.

The completed native X/V11 search evaluated all four candidates, selected 56 frames versus a 73-frame baseline, and passed two selected exact-input repetitions. Its independent evidence and limits are recorded in `artifacts/framework-migration/native-x-v11/plate-search-achievement-review/README.md`.

Offline validation:

```powershell
python -m unittest discover -s scripts/tests -p test_framework_plate_fresh_search.py
```

The fifteen tests use actual native S/W captures where relevant and labeled mutations for refusal cases. They cover failed-edge evidence retention, append-only receipts, failure-path finalization, and compatibility readers. They do not substitute a simulated score for a native achievement.
