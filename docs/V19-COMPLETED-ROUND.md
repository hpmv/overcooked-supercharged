# V19 completed native round

V19 completed Carnival of Chaos 3-4 with four native local chefs at **2,396 points: 1,800 base + 596 tips**, 22 deliveries and no deductions. Both native round-active flags were false and the timer was zero at gameplay frame16203. This is below V16's 2,612-point best and below the required5,000.

The run used recorded native seed0, 60 logical updates with native50Hz physics, the unchanged workspace plugin, and the frozen V19 controller. It restarted the kitchen in the existing lab process; it was not a fresh-process validation and has not been replayed without feedback. Its classification is an adaptive authored round. There remain zero qualifying5,000-point fresh-start runs.

- [Native trial and delivery history](../artifacts/native-round-v19/summary.json)
- [Native score, initial four-chef state and preview-restoration audit](../artifacts/native-round-v19-native-score-audit.json)
- [Frozen source/binary and2,557 offline assertions](../artifacts/planner-candidate-v19/validation.json)
- [Score screenshot](../lab/artifacts/native-round-v19-final.png)
- [Screenshot receipt](../artifacts/native-round-v19-screenshot.json)

Controller SHA256: `6a0a218a0d485c3030315a848148ea9170a79b01e7f9e089fa5b844c3fa0ac4b`. Captured source tree: `8cf318f7625e708b9bc541e5d86d2085d19bedf2e32d0cf2e426e7d39eb23830`.

The separate size1 raw-sausage buffer prefix completed8 deliveries at864 points. Its eighth delivery atGF5560 was624 frames/10.4 native seconds later than the same V19 no-buffer configuration's eighth delivery atGF4936, with the same recipe and score sequence. The [comparison](../artifacts/v19-sausage-buffer-prefix-comparison.json) pins both closed native trials. Buffering remains disabled for the current route.

The late bakery queue remains a bottleneck: order20 Chocolate was delivered atGF12790; order21 Raspberry followed atGF15569. Native records show a later Chocolate batch being supplied and cooked before the earlier Raspberry batch. Subsequent policy experiments must demonstrate their improvement in the actual game; this completed result is retained unchanged.
