# V21 completed native round

V21 completed Carnival of Chaos 3-4 with four native local chefs at **2,720 points: 2,040 base + 680 tips**, 25 deliveries and no deductions. The native 270-second round ended at gameplay frame16203. This is 108 points above the previous V16 best, but still below the required 5,000.

The run used native seed0 with the recorded isolated recipe RNG, 60 logical updates and native50Hz physics. V21 adds the optional plate-based onion finish and FIFO reassignment of two uncommitted empty bowls. At least one new onion transaction completed in the native game; the integrated score improvement is not a controlled measurement of either feature alone.

This is an adaptive authored round from a native kitchen restart in the existing lab process71732. It is not a fresh-process trial and exact-input playback remains untested. There remain zero qualifying 5,000-point fresh-start runs.

- [Native trial and final result](../artifacts/native-round-v21/summary.json)
- [Independent native score, four-player initial-state and preview-restoration audit](../artifacts/native-round-v21-native-score-audit.json)
- [Frozen controller source/binary and 2,819 offline assertions](../artifacts/planner-candidate-v21/validation.json)
- [Score screenshot](../lab/artifacts/native-round-v21-final.png)
- [Screenshot receipt](../artifacts/native-round-v21-screenshot.json)

The original runtime and plugin were unchanged. The complete native trace retains every request, adaptive decision and observed state; exact-input movie extraction and replay qualification are separate operations.

Controller SHA256: `19eae67bcc66348d83cff1184bc74f4a03e73abfd25f3d241bb684fd6d9bd399`. Captured source tree: `b60e8dc2b1b0694909f60cc29ca83e447747a1ce3ec3b1a96e821345a80fa237`.

The [exact input movie](../routes/probes/v21-completed-inputs.jsonl.gz) and [manifest](../routes/probes/v21-completed-inputs.jsonl.gz.manifest.json) preserve all 16206 original requests and 16203 one-frame steps. No request boundaries or inputs were changed. Movie SHA256: `0ec23f8e81f8f43479eba5b545c0a38804711063ee09fad3b1253be702a540b4`; canonical request SHA256: `74a25d8aab485c04846f725fae7bf0648f27d732790e4a85360428770d980d71`. This extraction is not a playback verification.
