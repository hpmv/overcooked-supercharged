# Fixed raw-sausage transfer deadline

The closed V18 buffer-size1 trial failed at GF2738, followed by neutral cleanup at GF2739 with score156/two deliveries. The failure was the old 240-frame whole-parking deadline, not an observed lost worker or changed ingredient. It remains a failed prefix, with no claim that the revised controller has completed this transfer natively.

`artifacts/native-prefix-v18-buffer1/trial001.jsonl.gz`, its summary/stderr, and unchanged GF2497/GF2738 responses preserve the witness. `artifacts/v18-buffer1-lifecycle-events.json` and `artifacts/v18-buffer1-p3-route-events.json` preserve the selected controller notifications separately from native state.

| Native frame | Observed transaction |
|---|---|
| 2446 | P2 starts second raw supply; storage38 and pass45 reserved. |
| 2480 | Fresh raw178, observed ordinal177, identified. |
| 2497 | Handoff finishes; P3 starts parking, source45 contains178, destination38 empty. |
| 2577 | Dynamic navigation replan uses a14.46684m remaining detour. The prior GF2567 remaining path was4.79097m. |
| 2724 | Native pickup: P3 holds the same raw178; pass45 becomes empty. |
| 2725 | Placement at reserved storage38 begins. |
| 2738 | Old aggregate expires at241 frames; placement is only13 frames old. Native raw178 is still correctly held. |

The unchanged GF2497 geometry yields a9.31654583m pickup path and a2.26480432m placement path. At the observed6m/s walking speed, the new admission computes `ceil((9.31654583+2.26480432)/6*60)=116` walking frames, plus90 interaction and180 recovery frames. The total is386, with an immutable deadline atGF2883. Both path results and allowance components are logged in `transferEvidence`.

The observed detour adds about9.676m relative to the previous remaining plan (about1.61 walking seconds), and repeated replanning occupied roughly40 frames before the large detour was selected. These are measured costs that justify a finite recovery allowance; they do not establish a universal path-completion guarantee. Future obstruction still fails at the same fixed deadline. Combined estimates above600 frames or without both legal paths are declined. No clock, position, input-edge or food correction is introduced.

`SausageTransferBudgetSelfTest` passes47 admission, mutation, timeout and lifecycle checks. `SausageTransferNativeTraceSelfTest` passes311 assertions over all305 consecutive native samples: the earlier successful refill GF2093–2155 and the failed parking GF2497–2738. The native refill shows original raw145 consumed into original pot2/home17 and its stock lease released. The parking samples retain source45/storage38/raw178/owner3 and the same deadline. A final parked-attachment continuation in the mutation suite is explicitly synthetic; the original native run never reached that completion. The existing64 buffer lifecycle checks still pass.

Run the isolated checks with:

```powershell
dotnet build controller/OvercookedTAS.Controller.csproj -c Release -o artifacts/sausage-transfer-check
dotnet run --project scripts/SausageTransferCheck/SausageTransferCheck.csproj -c Release
```

The bounded fixture `artifacts/v18-buffer1-transfer-native-fixtures.jsonl.gz` contains the original complete call-line bytes from those305 frames, compressed separately. `scripts/extract_v18_buffer_transfer.py` requires a closed source and refuses to overwrite the fixture. The hash receipt is `artifacts/v18-buffer1-transfer-budget-proof.json`; it distinguishes the failed V18 binary from the newly tested source and isolated binary. No native rerun or throughput result is claimed here.
