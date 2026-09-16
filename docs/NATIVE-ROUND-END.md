# Neutral native round-end evidence

Two recorded ordinary-input diagnostics now establish native termination of the loaded270-second, four-player Carnival of Chaos3-4 round. They are neutral runs with sparse observation prefixes, not high-score TAS movies or the20-run primitive qualification. Both finish with zero deliveries, zero base score, zero tips,150 native deductions and total score−150.

`scripts/check_neutral_round_end.py` reads only existing files. Its report is `artifacts/native-round-end-proof.json`. It verifies exact route/request equality, complete gzip recording, four explicit neutral input packets in every step request and recorded response, requested/native frame accounting, unchanged frame origin, native clock elapsed time, loaded round-data and timer limits of270, DLC8/four-user session fields, four distinct chef entities, native binary instrumentation hashes, complete score-event history, lifecycle callbacks and exact final-result equality. It does not create, connect to, advance or otherwise control a game.

| Evidence | A | B |
|---|---|---|
| Native setup | load, seed0, isolated native recipe stream | restart, seed0, isolated native recipe stream |
| Gameplay requests |28×600-frame steps |27×600-frame steps, then180×1-frame steps |
| Final observed gameplay frame |16800 |16380 |
| Native client clock elapsed from initial sample |279.9999978s |273.0000000s |
| First sampled complete native stop |16800, sparse interval |16202, exact per-frame sample |
| Final native state |RunLevelOutro, both rounds inactive |RunLevelOutro, both rounds inactive |

A's lifecycle history retains exact callbacks despite the sparse request interval. Both histories place server deactivation and native `RunLevelOutro` entry at gameplay16201, followed by client deactivation at16202. B observes those transitions directly on consecutive frames:

| Gameplay frame | Timer | Server active | Client active | Native state |
|---:|---:|---|---|---|
|16200|0.005126953|true|true|InLevel|
|16201|0|false|true|RunLevelOutro|
|16202|0|false|false|RunLevelOutro|

The native client clock in B advances270.016675s from the initial running sample to server stop and270.033337s to client stop. Its initial native timer was269.983337 because gameplay frame zero already includes the first running update. The remaining fractional timer at16200 is observed native float accumulation; neither the checker nor the route edits a timer or skips a gameplay delay.

The bot's default `MaximumFrames=16320` covers the observed complete stop at16202 with118 frames remaining. Its existing round-end branch would then emit a final neutral frame at16203. This result establishes enough budget for these observed stop/outro-entry callbacks. It does not establish that all results-screen animation finishes by16320, nor guarantee that every future loaded build has the same transition frame.

The full native score chain contains five `timeout` records at gameplay8160,8161,8761,9361 and9961. Each records an expired order, unchanged base/tip/delivery totals and30 additional deductions. Their before/after totals chain from0 to−150 with no missing indices or dropped events. The record field `inputsNeutral=false` on these timeout events is an unset default: `GameEvents.RecordInputRelease` alone populates that field. Neutrality is instead checked from the actual four-controller request/response packets. The sparse step requests are preserved and are not represented as an individually recorded per-frame TAS.

The visually inspected A results screenshot `artifacts/native-round-end-a.png` shows level3-4, four player slots, Orders Delivered×0, Tips0, Orders Failed×5/−150 and TOTAL−150. It corroborates the ledger. The screenshot's SHA256 is `e843477d532289774f7072da33ac0f55692f3d71099b63e9ccc95d60bc787463`.

The supplied OS process receipt identifies primary PID65624/listener17634 and distinguishes concurrent lab PID74800. Both trace manifests match its plugin and executable hashes. The receipt declares frozen controller SHA256 `ab961385e7fc5a616fcf9b81d3ffd25a0cbddcc9a63b10106ee2edef4d12c418`, and the frozen file hash agrees. The receipt was captured for A; this file audit does not independently prove a new process or a separate OS identity capture for B. Neither run counts as a fresh-process high-score verification.

| File | SHA256 |
|---|---|
|A trace|`1a8e6860c641cd30d9b866f3e38a5efe470dd5255509da932adef10d24a5f233`|
|B trace|`c840a9a72eae3ff5be5556dbcbd4851bb78f07a4799528d065c63b323a8282a7`|
|A route|`dd5a629d8c2c159f856e9a8a18cd36ce5267f083b1b604fbb7bf944a0bb41b94`|
|B route|`01cb0b8a3304fea4e137a3c32fe678c434ff1b47e62bd538a8cae7c33aa7e569`|
|A result|`054ee70d6d9802274dae56b58a6ecade48dc59bea8a53ffdb0be58a9c9520952`|
|B result|`96e0d616f03fc2e81237a4c164a58f202fe14ce555b39e4614607513fcc03e00`|

Ten checker tests pass, including rejected changed requests, non-neutral/duplicate packets, broken/missing ledger entries, an unsupported score claim, missing lifecycle evidence and falsely early stop claims. Omitting a transition sample correctly downgrades temporal precision instead of reporting an exact frame. This evidence closes the neutral round-end mechanism check only. The requested≥5000 TAS and its repeated complete native runs remain outstanding.
