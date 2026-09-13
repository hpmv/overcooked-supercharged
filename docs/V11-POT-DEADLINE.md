# V11 native near-pot burn

Observed attempt: `artifacts/native-round-v11/trial001.jsonl.gz`, frozen controller `7856cad051fbe0ce0f9fb60b612ec796f1e77452bc31b13b1ea11cc02ff2bc7b`. The attempt stopped on native Burnt pot7 at GF2711, with score156 and two deliveries. This is a failed prefix, not a completed round.

## Native timeline

| Event | Gameplay frame | Native progress / observation |
|---|---:|---|
| First near-pot load | 130 | Raw Frankfurter starts cooking |
| First near-pot Cooked | 850 | 12.016655 seconds |
| First near-pot emptied | 1120 | Previous hotdog consumed it |
| Second near-pot load | 1271 | Fresh raw Frankfurter |
| Second near-pot Cooked | 1991 | 12.016655 seconds |
| Last available chopped bun161 claimed | 2121 | P3 starts hotdog3 with far pot2 |
| Bun board released | 2208 | No replacement bun yet |
| Far pot2 emptied | 2245 | Pot7 remains Cooked |
| P2 returns to pantry | 2356 | Starts the next bun supply |
| New chopped bun180 ready | 2521 | Pot7 at20.8498535; both central chefs busy |
| P3 starts raw-buffer refill of pot2 | 2604 | Pot7 at22.2331657; bun180 untouched |
| P0 starts early-onion5 | 2627 | Pot7 at22.6164932; bun180 untouched |
| Native near-pot Burnt | 2711 | 24.0164719; candidate aborts |

The second near-pot contents remained Cooked for720 frames, exactly12 logical seconds, before the observed Burnt transition. Far pot2's second load was earlier (GF1241), became Cooked at1962, and was correctly consumed before near7 at2245.

## Central work during the cooked interval

P0: second hotdog sauce/plate job1346–2155; already-offheat chocolate donut plating2155–2316; empty basket restoration2316–2382; third hotdog plate/sauce2382–2627; new early-onion loading2627–failure.

P3: second dough transfer1656–1982; fryer4 rescue1982–2119, then two-frame offheat verification; third unplated hotdog2121–2288; washer-cannon fire2288–2384; dirty-stack relay2384–2489; fryer8 rescue2489–2602, then two-frame verification; raw-buffer refill2604–failure.

The fryer rescue worked: basket8 remained Cooked off heat on counter37. At failure, bun180 was still purely Chopped on board23, raw sausage153 was held by P3, raw sausage165 remained reserved on pass45, far pot2 was still empty, and future bowl6's mixing progress was17.8415661. Existing raw-stock supply was not the missing ingredient.

## Concrete scheduling gap

`Central` checks optional `TryLoadBufferedSausage` and prioritized new onion heat before `BuildUnplatedHotdog`. Its urgent heat branch covers onion pans, not boiled sausages. Thus the available bun and nearly burning pot were bypassed by new cooking work at2604/2627.

Moving only that late harvest earlier in the priority list is insufficient proof of recovery: P3 was beside the lower fryer at2604, with only about1.77 native seconds until burn. Earlier idle decisions occurred at2155 for P0 and2288 for P3, while the sole bun was already committed to another hotdog. A bounded whole-pot offheat rescue for cooked contents without an independently available bun would cover that earlier interval. When a bun is available, urgent cooked-pot consumption should precede new raw loading and onion heating. Neither proposed change was implemented during this audit.

## Storage observation

The physical and current resource union did reach zero free ordinary counters earlier in the attempt (first observed GF935). It cleared as meals and plates moved. At the actual burn, ordinary counters38/40/43 were free even after accounting for the five live long counter reservations. Storage capacity did not block the terminal harvest. Reducing sausage buffer size from2 to1 would change timing and reserve one less counter, but does not directly resolve the demonstrated pot deadline gap.

Eight ordinary storage counters are eligible:32,33,37,38,40,41,43,61. Workspace42 remains protected. Seed0's first46 FIFO positions bound combined onion/fryer/offmix plus2raw/1bun storage at8 slots; the nominal9-slot combination first becomes possible around delivered48. These bounds use distinct donut assignments and allow offmix leases to persist after entering the ordinary horizon. They exclude loose bases and clean plates, so they are not a guarantee of free rescue storage.

Evidence: `artifacts/v11-pot-deadline-analysis.json`; `artifacts/v11-storage-at-potburn.json`; unchanged full native response fixtures `artifacts/v11-pot-evidence-gf{1991,2121,2208,2521,2604,2627,2711}.json`. The storage audit records trace hash, post-event reservation reconstruction, native attachment identities, and periodic long-lease status cross-checks. No native game calls or controller edits were made by this audit.
