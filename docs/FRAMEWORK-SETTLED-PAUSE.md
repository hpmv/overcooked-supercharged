# Settled pause observation for native comparisons

The shared `scripts/framework_pause_boundary.py` observes an explicit pause condition without sending input, advancing gameplay, simulating physics, setting a pose or forcing sleep. Native R's active frames and clocks replay exactly, while the first paused bridge receipts can differ in native body sleep bits. Those raw receipts remain evidence and must not be called identical.

`observe_settled_pause(read_receipt, read_frame, expected_frame=None, chef_ids=None)` returns the final unchanged receipt and a proof containing **every** original receipt. The caller supplies read-only callbacks; this module opens no connection. It requires:

* The same actual controller frame before and after every native food receipt.
* A loaded native pause and the observed `native-float-capture-step-with-authoring-pause-suspension` clock policy.
* Exactly unchanged full native round data, source value, eligible clock ticks, capture step, fixed step, session and framework identity.
* Complete native Rigidbody/frozen-physics fields, including every raw/resume velocity, body identity, quaternion, kinematic/gravity flag and sleep bit. An optional four-chef inventory is also checked.
* Exact equality of that **whole physics object** over two distinct increasing FixedTime observations after its stable anchor. Repeated FixedTime does not count. A changed physics field resets the streak; no numeric tolerance or quaternion normalization is applied.

The budget is at most one wall-clock second and 128 receipts. The budget is checked around callbacks; operators must also set bounded RPC transport timeouts. Errors expose the retained proof on `PauseBoundaryError.report`. Native frame/time/clock changes fail immediately rather than starting a new baseline. The native physics loop can continue its own paused-body bookkeeping during this observation; the helper does not modify it.

S adds exact private native server clock fields (3) and client fields (6). When either appears, both complete finite arrays are required and held invariant. Historical R receipts may omit both; current plate-search execution requires S's arrays. Authoring attempt counters and checkpoint history tags are not compared as clocks.

The proof's classification is **settled authoring-pause observation; raw acknowledgement identity not asserted**. It separately reports initial versus settled physics hashes, the exact first differing field for every transition, and whether the initial raw acknowledgement happened to match. A passing condition does not retroactively make an earlier sleep-bit difference equal. Active-frame parity is independently checked from the original native exchange trace.

The plate search uses this same condition for its original base, every restored base, each candidate endpoint and both selected-input replay endpoints. It writes `*-pause-boundary.json` files and labels the selected receipt as a derived observation, with every actual food/status receipt retained. The endpoint comparison additionally checks full reconstructed entities, native round/food and the optional paired native clock arrays exactly.

Offline validation:

```powershell
python -m unittest discover -s scripts/tests -p test_framework_pause_boundary.py
python -m unittest discover -s scripts/tests -p test_framework_plate_search.py
```

Seven pause-policy tests and sixteen plate-search tests pass. These exercise retained initial sleep differences, duplicate physics ticks, timer/frame/clock drift, malformed or absent telemetry, bounded timeout and strict comparisons; they are not a native proof of an optimized plate relocation.

# Q's failed first plate candidate

Q's original pickup node reported completion at frame 43 after its release edge. Exact native plate 10 remained on counter 38. The subsequent placement node ran empty-handed until its bounded timeout at 494. The captured edge table is `artifacts/framework-migration/native-q/plate-search/first-edge-observations.json`: at frame 42 the chef already highlighted counter 38, but its preceding cached horizontal velocity was still about 6 units/s. Thus a correct observed highlight and completed input edge alone did not establish native pickup.

The revised candidates use the validated source counter's left approach `(x-1.1,z)` and the validated destination's lower approach `(x,z-1.2)`, with six ordinary neutral frames before each transfer. These correspond to approximately `(18.1,-15.6)` and `(19.2,-12.0)` in the observed level. Dash alternatives affect only the goto legs. These are candidate routes, not newly claimed native successes. The headless agent separately adds opt-in attachment-effect completion for typed pickup/place.

Normal action/whole-graph timeouts are now retained as failed trials if the exact fixed inventory, food compositions, ledger and allowed plate/source/chef/output attachment envelope remain intact. A completed input graph with no native plate goal is also ineligible. Another candidate starts only after a verified native checkpoint acknowledgement and exact restored baseline comparison. Unknown failures, unrelated catches, changed food/attachments, missing telemetry or rollback differences still stop the search. A failed baseline's timeout is never reported as a successful baseline completion time or a time saving.
